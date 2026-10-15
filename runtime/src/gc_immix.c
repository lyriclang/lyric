/* Stages 2 and 3 of the collector contract (design/v5/spec/01 L1, the plan 13 M11): the runtime's
 * own collector — non-moving, Immix blocks and lines, a precise heap and conservative stacks.
 *
 * The heap is one reservation of address space (P1), taken by chunks of 4 MiB. A chunk holds
 * either Immix blocks of 32 KiB, each of 128 lines of 256 bytes (P2), or large objects, page by
 * page; an object larger than half a chunk takes chunks of its own. Beside the heap, addressed in
 * parallel, lie the side tables: a byte per line (its mark), a bit per 16-byte granule for where
 * an object starts, and one for its mark.
 *
 * A thread allocates by bumping through a buffer of its own — a run of free lines (gc.h, P3). The
 * start bits are not written as an object is made: a buffer is parsed once, header by header,
 * when the thread leaves it, and every thread's buffer when a collection begins; the same pass
 * counts what the thread allocated (10 X4).
 *
 * Collections come with M11 S2: until then the heap only grows, up to the heap limit. */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE  /* pthread_getattr_np */
#endif

#include "lyr/gc.h"
#include "lyr/panic.h"
#include "internal.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <sys/mman.h>
#  include <unistd.h>
#endif

/* --- geometry ---------------------------------------------------------------------------------- */

enum {
    GRANULE = LYR_GC_GRANULE,
    LINE = 256,
    BLOCK = 32 * 1024,
    CHUNK = 4 * 1024 * 1024,
    LINES_PER_BLOCK = BLOCK / LINE,
    BLOCKS_PER_CHUNK = CHUNK / BLOCK,
    LINES_PER_CHUNK = CHUNK / LINE,
    /* the side tables of one chunk: a byte per line, a bit per granule */
    LINE_BYTES_PER_CHUNK = CHUNK / LINE,
    BIT_BYTES_PER_CHUNK = CHUNK / GRANULE / 8,
    /* large objects are laid out by pages of their own */
    PAGE = 4096,
    PAGES_PER_CHUNK = CHUNK / PAGE,
    /* an object up to this size bumps through the lines; above it, it is a large object */
    MEDIUM_MAX = 8 * 1024,
};

/* The reservation: as much address space as the system grants, from 256 GiB down to 4 GiB. It is
 * address space only — memory is committed a chunk at a time, and used as the heap grows. */
#define RESERVE_MAX ((uintptr_t)256 << 30)
#define RESERVE_MIN ((uintptr_t)4 << 30)

#define NO_BLOCK UINT32_MAX

/* --- the system's memory ----------------------------------------------------------------------- */

static void *os_reserve(size_t bytes) {
#ifdef _WIN32
    return VirtualAlloc(NULL, bytes, MEM_RESERVE, PAGE_NOACCESS);
#else
    void *at = mmap(NULL, bytes, PROT_NONE, MAP_PRIVATE | MAP_ANON | MAP_NORESERVE, -1, 0);
    return at == MAP_FAILED ? NULL : at;
#endif
}

static void os_release(void *at, size_t bytes) {
#ifdef _WIN32
    (void)bytes;
    VirtualFree(at, 0, MEM_RELEASE);
#else
    munmap(at, bytes);
#endif
}

static bool os_commit(void *at, size_t bytes) {
#ifdef _WIN32
    return VirtualAlloc(at, bytes, MEM_COMMIT, PAGE_READWRITE) != NULL;
#else
    return mprotect(at, bytes, PROT_READ | PROT_WRITE) == 0;
#endif
}

/* A reservation aligned to `align` (a power of two): reserved larger, the rest given back where
 * the system lets it (POSIX), or kept (Windows, which releases a reservation only whole). */
static uintptr_t reserve_aligned(size_t bytes, size_t align) {
    void *raw = os_reserve(bytes + align);
    if (raw == NULL) return 0;
    uintptr_t start = ((uintptr_t)raw + align - 1) & ~(uintptr_t)(align - 1);
#ifndef _WIN32
    size_t before = start - (uintptr_t)raw, after = align - before;
    if (before) munmap(raw, before);
    if (after) munmap((void *)(start + bytes), after);
#endif
    return start;
}

/* --- the heap and its side tables ---------------------------------------------------------------- */

typedef enum ChunkKind { CHUNK_UNUSED, CHUNK_BLOCKS, CHUNK_LARGE, CHUNK_HUGE } ChunkKind;

typedef enum BlockState { BLOCK_FREE, BLOCK_IN_USE, BLOCK_FULL } BlockState;

typedef struct Block {
    uint8_t state;
    uint8_t fresh;   /* never handed out since it was committed: its memory is zero already */
} Block;

typedef struct Chunk {
    uint8_t kind;
    uint8_t committed;
    Block *blocks;          /* CHUNK_BLOCKS */
    uint16_t *pages;        /* CHUNK_LARGE: per page 0 when free, else 1 + the page its object starts on */
    uint32_t fresh_from;    /* CHUNK_LARGE: the pages from here on were never handed out */
    uint32_t free_pages;    /* CHUNK_LARGE */
    uintptr_t huge;         /* CHUNK_HUGE: the start of the object the chunk belongs to */
} Chunk;

static uintptr_t heap_base, heap_span;
static uint8_t *line_marks;   /* a byte per line: the epoch of the collection that last found it live */
static uint8_t *start_bits;   /* a bit per granule: an object starts there */
static uint8_t *mark_bits;    /* a bit per granule: the object starting there is marked (S2) */
static Chunk *chunks;
static size_t chunk_count;    /* chunks in the reservation */
static size_t chunks_taken;   /* the chunks below were taken once */

/* The epoch a line mark is compared against: a line is live when its mark is the current one. */
static uint8_t epoch = 1;

/* The bytes in use — blocks taken and pages of large objects — against the heap limit. */
static size_t bytes_in_use;
static size_t heap_limit;

static inline size_t granule_of(uintptr_t at) { return (at - heap_base) / GRANULE; }
static inline size_t line_of(uintptr_t at) { return (at - heap_base) / LINE; }
static inline uintptr_t block_address(uint32_t block) { return heap_base + (uintptr_t)block * BLOCK; }
static inline Block *block_info(uint32_t block) {
    return &chunks[block / BLOCKS_PER_CHUNK].blocks[block % BLOCKS_PER_CHUNK];
}

static inline void set_start(uintptr_t at) {
    size_t g = granule_of(at);
    start_bits[g >> 3] |= (uint8_t)(1u << (g & 7));
}

/* The start bits of [from, to), both line-aligned: two bytes a line. */
static void clear_starts(uintptr_t from, uintptr_t to) {
    memset(start_bits + granule_of(from) / 8, 0, (to - from) / GRANULE / 8);
}

/* One lock for what threads share: the free blocks, the chunks, the large objects, the lists of
 * roots and weak references, the registry of threads. A thread's own buffer needs none. */
#ifdef _WIN32
static SRWLOCK heap_lock = SRWLOCK_INIT;
static void lock(void) { AcquireSRWLockExclusive(&heap_lock); }
static void unlock(void) { ReleaseSRWLockExclusive(&heap_lock); }
#else
static pthread_mutex_t heap_lock = PTHREAD_MUTEX_INITIALIZER;
static void lock(void) { pthread_mutex_lock(&heap_lock); }
static void unlock(void) { pthread_mutex_unlock(&heap_lock); }
#endif

static size_t heap_in_use(void) {
    lock();
    size_t bytes = bytes_in_use;
    unlock();
    return bytes;
}

LYR_NORETURN static void exhausted(size_t asked) {
    lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating %zu bytes (heap %zu bytes)", asked, heap_in_use());
}

/* Under the lock: room for `bytes` more within the heap limit. */
static bool within_limit(size_t bytes) {
    return heap_limit == 0 || (bytes <= heap_limit && bytes_in_use <= heap_limit - bytes);
}

/* Under the lock: a chunk of `kind`, committed, its side tables with it; -1 when the reservation
 * is used up or the system refuses the memory. */
static ptrdiff_t take_chunk(ChunkKind kind) {
    size_t c = 0;
    while (c < chunks_taken && chunks[c].kind != CHUNK_UNUSED) c++;
    if (c == chunks_taken) {
        if (chunks_taken == chunk_count) return -1;
        chunks_taken++;
    }
    Chunk *chunk = &chunks[c];
    if (!chunk->committed) {
        if (!os_commit((void *)(heap_base + c * CHUNK), CHUNK)) return -1;
        if (!os_commit(line_marks + c * LINE_BYTES_PER_CHUNK, LINE_BYTES_PER_CHUNK)) return -1;
        if (!os_commit(start_bits + c * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return -1;
        if (!os_commit(mark_bits + c * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return -1;
        chunk->committed = 1;
    }
    if (kind == CHUNK_BLOCKS && chunk->blocks == NULL) {
        chunk->blocks = calloc(BLOCKS_PER_CHUNK, sizeof(Block));
        if (chunk->blocks == NULL) return -1;
    }
    if (kind == CHUNK_LARGE && chunk->pages == NULL) {
        chunk->pages = calloc(PAGES_PER_CHUNK, sizeof(uint16_t));
        if (chunk->pages == NULL) return -1;
    }
    chunk->kind = (uint8_t)kind;
    if (kind == CHUNK_BLOCKS) {
        for (size_t b = 0; b < BLOCKS_PER_CHUNK; b++) chunk->blocks[b] = (Block){ BLOCK_FREE, 1 };
    } else if (kind == CHUNK_LARGE) {
        memset(chunk->pages, 0, PAGES_PER_CHUNK * sizeof(uint16_t));
        chunk->fresh_from = 0;
        chunk->free_pages = PAGES_PER_CHUNK;
    }
    return (ptrdiff_t)c;
}

/* --- free blocks -------------------------------------------------------------------------------- */

static uint32_t *free_blocks;
static size_t free_count, free_capacity;

static bool push_free(uint32_t block) {
    if (free_count == free_capacity) {
        size_t capacity = free_capacity ? free_capacity * 2 : 1024;
        uint32_t *grown = realloc(free_blocks, capacity * sizeof *grown);
        if (grown == NULL) return false;
        free_blocks = grown;
        free_capacity = capacity;
    }
    free_blocks[free_count++] = block;
    return true;
}

/* A block for a thread's buffer, taken out of the free ones (a new chunk's when none is left), or
 * NO_BLOCK when the heap limit or the system says no. */
static uint32_t take_block(void) {
    lock();
    uint32_t block = NO_BLOCK;
    if (within_limit(BLOCK)) {
        if (free_count == 0) {
            ptrdiff_t c = take_chunk(CHUNK_BLOCKS);
            if (c >= 0) {
                /* the lowest block first: the heap grows upward, as it was taken */
                for (size_t b = BLOCKS_PER_CHUNK; b-- > 0;) {
                    if (!push_free((uint32_t)((size_t)c * BLOCKS_PER_CHUNK + b))) break;
                }
            }
        }
        if (free_count > 0) {
            block = free_blocks[--free_count];
            block_info(block)->state = BLOCK_IN_USE;
            bytes_in_use += BLOCK;
        }
    }
    unlock();
    return block;
}

/* --- the threads -------------------------------------------------------------------------------- */

/* A buffer a thread bumps through: a run of free lines of its block. The small one's bounds live in
 * lyr_mutator while the thread runs Lyric code (the fast path reads them there); the medium one —
 * Immix's overflow block, for an object of more than a line that does not fit where the small one
 * stands — only here. */
typedef struct Buffer {
    uintptr_t cursor, limit;
    uintptr_t parsed;   /* the objects below were parsed: their start bits set, counted */
    uint32_t block;     /* the block it bumps through, or NO_BLOCK */
    uint32_t line;      /* where the search for the block's next run goes on */
} Buffer;

typedef struct Thread {
    struct Thread *next, *prev;
    LyrMutator *mutator;   /* the thread's lyr_mutator */
    Buffer small, medium;
    uint64_t allocations, allocated_bytes;
    LyrStackCtx *own;      /* coro.c's record of the thread's own stack */
    LyrStackCtx *running;  /* the stack the thread runs on: its own, or a coroutine's */
    void *stack_base;      /* the cold end of its own stack */
} Thread;

_Thread_local LyrMutator lyr_mutator;
static _Thread_local Thread *self;
static Thread *threads;

static Thread *current(void) {
    Thread *t = self;
    if (LYR_UNLIKELY(t == NULL)) lyr_panic(LYR_RT_SYSTEM, "a thread allocates without being attached to the collector");
    return t;
}

/* The bytes of the object at `at`, rounded to granules: its descriptor's size, or for an array, a
 * string or a coroutine what the header and the length say. */
static size_t object_bytes(const LyrObj *object) {
    const LyrDesc *desc = object->desc;
    size_t bytes;
    if (desc->flags & LYR_DESC_ARRAY) bytes = offsetof(LyrArr, data) + (size_t)((const LyrArr *)object)->len * desc->elem_size;
    else if (desc->flags & LYR_DESC_STRING) bytes = offsetof(LyrStr, bytes) + (size_t)((const LyrStr *)object)->len + 1;
    else if (desc->flags & LYR_DESC_CORO) bytes = sizeof(LyrCoro);
    else bytes = desc->size;
    return LYR_GC_ROUND(bytes);
}

/* The objects made in `buffer` since its last parse: their start bits, and the thread's count. An
 * object whose descriptor gives it no size cannot be stepped over: a descriptor that lies (an
 * array's without LYR_DESC_ARRAY) is reported, not walked into. */
static void parse(Thread *t, Buffer *buffer) {
    uintptr_t at = buffer->parsed;
    while (at < buffer->cursor) {
        size_t bytes = object_bytes((const LyrObj *)at);
        if (LYR_UNLIKELY(bytes == 0)) {
            lyr_panic(LYR_RT_SYSTEM, "an object of %s has no size: its descriptor does not say what it is",
                      ((const LyrObj *)at)->desc->name);
        }
        set_start(at);
        t->allocations++;
        t->allocated_bytes += bytes;
        at += bytes;
    }
    buffer->parsed = at;
}

static void sync_in(Thread *t) {
    t->small.cursor = t->mutator->cursor;
    t->small.limit = t->mutator->limit;
}

static void sync_out(Thread *t) {
    t->mutator->cursor = t->small.cursor;
    t->mutator->limit = t->small.limit;
}

/* Hands out [from, to) of `buffer`'s block: zeroed (unless the block is fresh), its stale start
 * bits cleared. */
static void hand_out(Buffer *buffer, uintptr_t from, uintptr_t to) {
    Block *block = block_info(buffer->block);
    if (!block->fresh) {
        clear_starts(from, to);
        memset((void *)from, 0, to - from);
    }
    buffer->cursor = buffer->parsed = from;
    buffer->limit = to;
}

/* The block's next run of free lines of at least `bytes`, from where the search stands; false
 * when the block has none left — it is full then, until the next collection. */
static bool next_run(Buffer *buffer, size_t bytes) {
    if (buffer->block == NO_BLOCK) return false;
    uintptr_t base = block_address(buffer->block);
    const uint8_t *marks = line_marks + line_of(base);
    uint32_t line = buffer->line;
    while (line < LINES_PER_BLOCK) {
        while (line < LINES_PER_BLOCK && marks[line] == epoch) line++;
        uint32_t first = line;
        while (line < LINES_PER_BLOCK && marks[line] != epoch) line++;
        if (line > first && (size_t)(line - first) * LINE >= bytes) {
            buffer->line = line;
            hand_out(buffer, base + (uintptr_t)first * LINE, base + (uintptr_t)line * LINE);
            return true;
        }
    }
    Block *block = block_info(buffer->block);
    block->state = BLOCK_FULL;
    block->fresh = 0;
    buffer->block = NO_BLOCK;
    buffer->cursor = buffer->limit = buffer->parsed = 0;
    return false;
}

/* `buffer` refilled with a run of at least `bytes`: the rest of its block, else a block taken. */
static bool refill(Thread *t, Buffer *buffer, size_t bytes) {
    parse(t, buffer);
    if (next_run(buffer, bytes)) return true;
    for (;;) {
        uint32_t block = take_block();
        if (block == NO_BLOCK) return false;
        buffer->block = block;
        buffer->line = 0;
        if (next_run(buffer, bytes)) return true;
    }
}

static void *bump(Buffer *buffer, size_t rounded) {
    uintptr_t at = buffer->cursor;
    buffer->cursor = at + rounded;
    return (void *)at;
}

/* Gives the thread's blocks up: what it made in them parsed, the blocks full until the next
 * collection. */
static void leave_buffers(Thread *t) {
    sync_in(t);
    Buffer *buffers[] = { &t->small, &t->medium };
    for (size_t i = 0; i < 2; i++) {
        Buffer *buffer = buffers[i];
        parse(t, buffer);
        if (buffer->block != NO_BLOCK) {
            lock();
            Block *block = block_info(buffer->block);
            block->state = BLOCK_FULL;
            block->fresh = 0;
            unlock();
        }
        *buffer = (Buffer){ 0, 0, 0, NO_BLOCK, 0 };
    }
    sync_out(t);
}

/* --- large objects ---------------------------------------------------------------------------- */

/* Under the lock: `pages` free pages in a row in a large-object chunk, first fit; a chunk taken
 * when none has them. 0 when there is no room. */
static uintptr_t take_pages(size_t pages, bool *fresh) {
    for (size_t pass = 0; pass < 2; pass++) {
        for (size_t c = 0; c < chunks_taken; c++) {
            Chunk *chunk = &chunks[c];
            if (chunk->kind != CHUNK_LARGE || chunk->free_pages < pages) continue;
            size_t run = 0;
            for (size_t p = 0; p < PAGES_PER_CHUNK; p++) {
                run = chunk->pages[p] == 0 ? run + 1 : 0;
                if (run < pages) continue;
                size_t first = p + 1 - pages;
                for (size_t q = first; q <= p; q++) chunk->pages[q] = (uint16_t)(first + 1);
                chunk->free_pages -= (uint32_t)pages;
                *fresh = first >= chunk->fresh_from;
                if (p + 1 > chunk->fresh_from) chunk->fresh_from = (uint32_t)(p + 1);
                return heap_base + c * CHUNK + first * PAGE;
            }
        }
        if (pass == 0 && take_chunk(CHUNK_LARGE) < 0) return 0;
    }
    return 0;
}

/* Under the lock: chunks of their own for an object of more than half a chunk, `count` in a row;
 * `fresh` when none of them was taken before. */
static uintptr_t take_huge(size_t count, bool *fresh) {
    size_t run = 0;
    for (size_t c = 0; c < chunk_count; c++) {
        bool unused = c >= chunks_taken || chunks[c].kind == CHUNK_UNUSED;
        run = unused ? run + 1 : 0;
        if (run < count) continue;
        size_t first = c + 1 - count;
        uintptr_t start = heap_base + first * CHUNK;
        *fresh = first >= chunks_taken;
        for (size_t d = first; d <= c; d++) {
            Chunk *chunk = &chunks[d];
            if (!chunk->committed) {
                if (!os_commit((void *)(heap_base + d * CHUNK), CHUNK)) return 0;
                if (!os_commit(line_marks + d * LINE_BYTES_PER_CHUNK, LINE_BYTES_PER_CHUNK)) return 0;
                if (!os_commit(start_bits + d * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return 0;
                if (!os_commit(mark_bits + d * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return 0;
                chunk->committed = 1;
            }
            chunk->kind = CHUNK_HUGE;
            chunk->huge = start;
        }
        if (c + 1 > chunks_taken) chunks_taken = c + 1;
        return start;
    }
    return 0;
}

static void *allocate_large(Thread *t, size_t rounded, size_t asked) {
    size_t pages = (rounded + PAGE - 1) / PAGE;
    size_t bytes = pages * PAGE;
    bool fresh = true;
    lock();
    uintptr_t at = 0;
    if (within_limit(bytes)) {
        at = pages > PAGES_PER_CHUNK / 2 ? take_huge((bytes + CHUNK - 1) / CHUNK, &fresh) : take_pages(pages, &fresh);
        if (at != 0) {
            set_start(at);
            bytes_in_use += bytes;
        }
    }
    unlock();
    if (at == 0) exhausted(asked);
    if (!fresh) memset((void *)at, 0, rounded);
    t->allocations++;
    t->allocated_bytes += rounded;
    return (void *)at;
}

/* --- the slow paths ----------------------------------------------------------------------------- */

static void *allocate(size_t asked) {
    size_t rounded = LYR_GC_ROUND(asked);
    Thread *t = current();
    if (rounded > MEDIUM_MAX) return allocate_large(t, rounded, asked);
    sync_in(t);
    void *at;
    if (rounded <= t->small.limit - t->small.cursor) {
        at = bump(&t->small, rounded);  /* the fast path's own case, under ASan */
    } else if (rounded > LINE) {
        /* Immix's overflow: an object of more than a line that does not fit where the small buffer
         * stands goes to a buffer of its own instead of giving up the small one's run */
        if (rounded > t->medium.limit - t->medium.cursor && !refill(t, &t->medium, rounded)) exhausted(asked);
        at = bump(&t->medium, rounded);
    } else {
        if (!refill(t, &t->small, rounded)) exhausted(asked);
        at = bump(&t->small, rounded);
    }
    sync_out(t);
    return at;
}

void *lyr_gc_alloc_slow(const LyrDesc *desc, size_t bytes) {
    LyrObj *object = allocate(bytes);
    object->desc = desc;
    return object;
}

LyrArr *lyr_gc_alloc_array_slow(const LyrDesc *desc, int64_t count) {
    if (LYR_UNLIKELY(count < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "array length %lld is negative", (long long)count);
    }
    size_t fixed = offsetof(LyrArr, data);
    if (LYR_UNLIKELY(desc->elem_size != 0 && (uint64_t)count > (SIZE_MAX / 2 - fixed) / desc->elem_size)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "an array of %lld elements of %u bytes does not fit in memory",
                  (long long)count, desc->elem_size);
    }
    LyrArr *array = allocate(fixed + (size_t)count * desc->elem_size);
    array->header.desc = desc;
    array->len = count;
    return array;
}

LyrStr *lyr_gc_alloc_string_slow(int64_t len) {
    if (LYR_UNLIKELY(len < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "string length %lld is negative", (long long)len);
    }
    size_t fixed = offsetof(LyrStr, bytes) + 1;
    if (LYR_UNLIKELY((uint64_t)len > SIZE_MAX / 2 - fixed)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "a string of %lld bytes does not fit in memory", (long long)len);
    }
    LyrStr *string = allocate(fixed + (size_t)len);
    string->header.desc = &lyr_desc_string;
    string->len = len;
    return string;
}

/* --- start ------------------------------------------------------------------------------------- */

static int initialized;

/* The cold end of the calling thread's stack. */
static void *stack_base_of_this_thread(void) {
#if defined(_WIN32)
    ULONG_PTR low = 0, high = 0;
    GetCurrentThreadStackLimits(&low, &high);
    return (void *)high;
#elif defined(__APPLE__)
    return pthread_get_stackaddr_np(pthread_self());
#else
    pthread_attr_t attr;
    void *base = NULL;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        void *address;
        size_t size;
        if (pthread_attr_getstack(&attr, &address, &size) == 0) base = (char *)address + size;
        pthread_attr_destroy(&attr);
    }
    return base;
#endif
}

static Thread *register_this_thread(void) {
    Thread *t = calloc(1, sizeof *t);
    if (t == NULL) return NULL;
    t->mutator = &lyr_mutator;
    t->small.block = t->medium.block = NO_BLOCK;
    t->stack_base = stack_base_of_this_thread();
    lock();
    t->next = threads;
    if (threads) threads->prev = t;
    threads = t;
    unlock();
    self = t;
    return t;
}

void lyr_gc_init(void) {
    if (initialized) return;
    for (uintptr_t span = RESERVE_MAX; span >= RESERVE_MIN && heap_base == 0; span /= 2) {
        /* the side tables: a byte a line for its mark and one for its card (stage 3, S6), a bit
         * a granule for a start and one for a mark */
        size_t lines = span / LINE, bits = span / GRANULE / 8, tables_size = 2 * lines + 2 * bits;
        uint8_t *tables = os_reserve(tables_size);
        if (tables == NULL) continue;
        uintptr_t base = reserve_aligned(span, CHUNK);
        if (base == 0) {
            os_release(tables, tables_size);
            continue;
        }
        heap_base = base;
        heap_span = span;
        line_marks = tables;
        start_bits = tables + 2 * lines;
        mark_bits = start_bits + bits;
    }
    if (heap_base == 0) lyr_panic(LYR_RT_OUT_OF_MEMORY, "the system grants no address space for the heap");
    chunk_count = heap_span / CHUNK;
    chunks = calloc(chunk_count, sizeof(Chunk));
    if (chunks == NULL || register_this_thread() == NULL) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory starting the collector");
    }
    initialized = 1;
}

void lyr_gc_set_heap_limit(size_t bytes) {
    lock();
    heap_limit = bytes;
    unlock();
}

/* --- roots ------------------------------------------------------------------------------------- */

/* A root lives in malloc memory, in a list the collection reads (S2). */
struct LyrRoot {
    void *object;
    struct LyrRoot *next, *prev;
};

static LyrRoot *roots;

LyrRoot *lyr_root_new(void *object) {
    LyrRoot *root = malloc(sizeof *root);
    if (LYR_UNLIKELY(root == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a root");
    root->object = object;
    root->prev = NULL;
    lock();
    root->next = roots;
    if (roots) roots->prev = root;
    roots = root;
    unlock();
    return root;
}

void *lyr_root_get(const LyrRoot *root) { return root->object; }
void lyr_root_set(LyrRoot *root, void *object) { root->object = object; }

void lyr_root_free(LyrRoot *root) {
    lock();
    if (root->prev) root->prev->next = root->next;
    else roots = root->next;
    if (root->next) root->next->prev = root->prev;
    unlock();
    free(root);
}

/* Ranges scanned conservatively besides the stacks (S2). */
typedef struct Range { uintptr_t low, high; } Range;
static Range *ranges;
static size_t range_count, range_capacity;

void lyr_register_stack(void *low, void *high) {
    lock();
    if (range_count == range_capacity) {
        size_t capacity = range_capacity ? range_capacity * 2 : 16;
        Range *grown = realloc(ranges, capacity * sizeof *grown);
        if (grown == NULL) {
            unlock();
            lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory registering a range");
        }
        ranges = grown;
        range_capacity = capacity;
    }
    ranges[range_count++] = (Range){ (uintptr_t)low, (uintptr_t)high };
    unlock();
}

void lyr_unregister_stack(void *low, void *high) {
    lock();
    for (size_t i = 0; i < range_count; i++) {
        if (ranges[i].low == (uintptr_t)low && ranges[i].high == (uintptr_t)high) {
            ranges[i] = ranges[--range_count];
            break;
        }
    }
    unlock();
}

/* --- weak references ------------------------------------------------------------------------- */

/* A record in malloc memory, in a list the collection walks after marking (S3): one whose object
 * is unmarked is cleared, and its callback queued. */
struct LyrWeak {
    void *object;
    LyrWeakCallback on_death;
    void *context;
    struct LyrWeak *next, *prev;
};

static LyrWeak *weaks;

LyrWeak *lyr_weak_new(void *object, LyrWeakCallback on_death, void *context) {
    LyrWeak *weak = malloc(sizeof *weak);
    if (LYR_UNLIKELY(weak == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a weak reference");
    weak->object = object;
    weak->on_death = on_death;
    weak->context = context;
    weak->prev = NULL;
    lock();
    weak->next = weaks;
    if (weaks) weaks->prev = weak;
    weaks = weak;
    unlock();
    return weak;
}

void *lyr_weak_get(const LyrWeak *weak) {
    /* the collection clears it with the world stopped: no thread reads it meanwhile */
    return weak->object;
}

void lyr_weak_free(LyrWeak *weak) {
    lock();
    if (weak->prev) weak->prev->next = weak->next;
    else weaks = weak->next;
    if (weak->next) weak->next->prev = weak->prev;
    unlock();
    free(weak);
}

/* --- threads ----------------------------------------------------------------------------------- */

int lyr_thread_attach(void) {
    if (self != NULL) return 0;  /* the main thread, attached by lyr_gc_init */
    if (register_this_thread() == NULL) return -1;
    lyr_crash_thread_start();
    return 0;
}

void lyr_thread_detach(void) {
    Thread *t = self;
    if (t == NULL) return;
    leave_buffers(t);
    lyr_coro_thread_end();
    lyr_crash_thread_end();
    lock();
    if (t->prev) t->prev->next = t->next;
    else threads = t->next;
    if (t->next) t->next->prev = t->prev;
    unlock();
    self = NULL;
    lyr_mutator.cursor = lyr_mutator.limit = 0;
    free(t);
}

/* --- coroutines (01 L4, 06 A5) ------------------------------------------------------------------ */

/* The descriptor a coroutine is made with; lyr_coro_new puts its type's in place, which says
 * LYR_DESC_CORO as well. */
static const LyrDesc coro_desc = { .size = sizeof(LyrCoro), .flags = LYR_DESC_CORO, .name = "coroutine" };

LyrCoro *lyr_gc_alloc_coro(void) {
    return lyr_alloc(&coro_desc);
}

/* Until S3, nobody collects a coroutine. */
void lyr_gc_watch_coro(LyrCoro *co) { (void)co; }
void lyr_gc_unwatch_coro(LyrCoro *co) { (void)co; }

void *lyr_gc_thread_stack(void **base) {
    Thread *t = current();
    *base = t->stack_base;
    return t;
}

void lyr_gc_add_thread_stack(LyrStackCtx *own) {
    Thread *t = current();
    t->own = own;
    t->running = own;
}

void lyr_gc_remove_thread_stack(LyrStackCtx *own) {
    Thread *t = self;
    if (t != NULL && t->own == own) t->own = t->running = NULL;
}

/* No collection stops a thread inside a switch — the world stops at safepoints (S3), and a switch
 * has none — so a switch only says where the thread runs now. */
void lyr_gc_switch_begin(void *gc_handle, const LyrStackCtx *to) {
    ((Thread *)gc_handle)->running = (LyrStackCtx *)to;
}

void lyr_gc_switch_end(void) {}

/* --- collection -------------------------------------------------------------------------------- */

void lyr_gc_collect(void) {}

size_t lyr_gc_heap_size(void) { return heap_in_use(); }

size_t lyr_gc_collections(void) { return 0; }

int64_t lyr_gc_thread_allocations(void) {
    Thread *t = current();
    sync_in(t);
    parse(t, &t->small);
    parse(t, &t->medium);
    return (int64_t)t->allocations;
}

int64_t lyr_gc_thread_allocated_bytes(void) {
    Thread *t = current();
    sync_in(t);
    parse(t, &t->small);
    parse(t, &t->medium);
    return (int64_t)t->allocated_bytes;
}
