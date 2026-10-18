/* Stages 2 and 3 of the collector contract (design/v5/spec/01 L1, the plan 13 M11): the runtime's
 * own collector — non-moving, Immix blocks and lines, a precise heap and conservative stacks.
 *
 * The heap is one reservation of address space (P1), taken by chunks of 4 MiB. A chunk holds
 * either Immix blocks of 32 KiB, each of 128 lines of 256 bytes (P2), or large objects, page by
 * page; an object larger than half a chunk takes chunks of its own. Beside the heap, addressed in
 * parallel, lie the side tables: a byte per line (the epoch of the collection that found it live)
 * and one for its card (stage 3), a bit per 16-byte granule for where an object starts, and one
 * for its mark.
 *
 * A thread allocates by bumping through a buffer of its own — a run of free lines (gc.h, P3). The
 * start bits are not written as an object is made: a buffer is parsed once, header by header,
 * when the thread leaves it, and every thread's buffer when a collection begins; the same pass
 * counts what the thread allocated (10 X4).
 *
 * A collection stops the world (P4), marks from the roots — every stack conservatively, the
 * module globals, the roots and ranges registered — through the heap precisely, by the
 * descriptors' maps, and leaves the rest to the allocators: a block is looked at again only when
 * one takes it, line by line (P7). Large objects without a mark are freed in the pause.
 *
 * The world stops at safepoints (P5): compiled code polls at loop back-edges and function entries,
 * and the runtime's blocking calls are safe regions. A thread stopped either way has its registers
 * on its stack and its stack scanned from where it stopped. No signal is involved.
 *
 * Most collections are young (P8, sticky mark bits): they clear no mark — what is marked is old —,
 * mark from the roots and from the old objects on the lines whose card a store dirtied (gc.h's
 * barrier) — a coroutine's among them, whose card a switch into it or out of it marks: its stack
 * has no barrier of its own —, and descend into no marked object.
 * What survives is old from then on, where it lies. A full collection clears the marks and marks
 * the whole heap; it comes when the old heap doubled since the last one, when the heap has no
 * room, and when the program asks. */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE  /* pthread_getattr_np */
#endif

#include "lyr/gc.h"
#include "lyr/panic.h"
#include "internal.h"

/* Under ASan (01 L12) every allocation takes the slow path (gc.h), memory the heap does not hand out
 * is poisoned — free lines and blocks, the pages of a large object that died — and a run is made
 * accessible as it is handed out: a stale pointer into freed memory is ASan's to report. The
 * collector's own reads of the heap are not instrumented: a conservative word may point into what
 * died. */
#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
#    include <sanitizer/asan_interface.h>
#    define POISON(at, bytes) ASAN_POISON_MEMORY_REGION((const void *)(at), (bytes))
#    define UNPOISON(at, bytes) ASAN_UNPOISON_MEMORY_REGION((const void *)(at), (bytes))
#  endif
#endif
#ifndef POISON
#  define POISON(at, bytes) ((void)(at), (void)(bytes))
#  define UNPOISON(at, bytes) ((void)(at), (void)(bytes))
#endif

#include <errno.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <sched.h>
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

/* A collection comes after this much allocation at least, and after as much as lived through the
 * last one otherwise (P7: the form of Go's GOGC = 100). */
#define MIN_BUDGET ((size_t)4 << 20)

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

/* Memory the heap holds and does not use, given back to the system (P7, S8): its pages leave the
 * process's resident set, the address space stays. Windows decommits, and the memory is committed
 * again before it is handed out (os_commit, which commits what is committed already at no cost);
 * Linux drops the pages, macOS maps fresh ones over them. What it holds afterwards is zeroed when
 * it is handed out again, as any reused memory is. */
static void os_discard(void *at, size_t bytes) {
#if defined(_WIN32)
    VirtualFree(at, bytes, MEM_DECOMMIT);
#elif defined(__APPLE__)
    /* Darwin's advice frees lazily and the resident set keeps the pages: mapped over, they go now */
    mmap(at, bytes, PROT_READ | PROT_WRITE, MAP_FIXED | MAP_PRIVATE | MAP_ANON, -1, 0);
#else
    madvise(at, bytes, MADV_DONTNEED);
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

/* A block is free (no live line, on the free list), in use (a thread's buffer bumps through it),
 * full (looked at since the last collection, no run left for anyone), recyclable (on that list,
 * with runs of free lines), or unswept (not looked at since the last collection). */
typedef enum BlockState { BLOCK_FREE, BLOCK_IN_USE, BLOCK_FULL, BLOCK_RECYCLABLE, BLOCK_UNSWEPT } BlockState;

typedef struct Block {
    uint8_t state;
    uint8_t fresh;      /* never handed out since it was committed: its memory is zero already */
    uint8_t discarded;  /* free, its memory given back (S8): committed again when it is taken */
} Block;

typedef struct Chunk {
    uint8_t kind;
    uint8_t committed;
    uint8_t used;           /* taken once: its memory is no longer the zero the system committed */
    uint8_t discarded;      /* unused or all free, its memory given back (S8): committed again when taken */
    Block *blocks;          /* CHUNK_BLOCKS */
    uint16_t *pages;        /* CHUNK_LARGE: per page 0 when free, else 1 + the page its object starts on */
    uint32_t fresh_from;    /* CHUNK_LARGE: the pages from here on were never handed out */
    uint32_t free_pages;    /* CHUNK_LARGE */
    uintptr_t huge;         /* CHUNK_HUGE: the start of the object the chunk belongs to */
    size_t huge_bytes;      /* CHUNK_HUGE, on the object's first chunk: the bytes it took */
} Chunk;

static uintptr_t heap_base, heap_span;
static uint8_t *line_marks;   /* a byte per line: the epoch of the collection that last found it live */
static uint8_t *start_bits;   /* a bit per granule: an object starts there */
static uint8_t *mark_bits;    /* a bit per granule: the object starting there is marked */
static Chunk *chunks;
static size_t chunk_count;    /* chunks in the reservation */
static size_t chunks_taken;   /* the chunks below were taken once */

/* The barrier's copy of the reservation, and the cards: a byte per line, set by every store of a
 * reference into the line since the last collection (gc.h, P8). */
uintptr_t lyr_gc_heap_base, lyr_gc_heap_span;
uint8_t *lyr_gc_cards;

/* The epoch a line mark is compared against: a line is live when its mark is the current one. A
 * collection begins a new one, so that no line has to be unmarked. */
static uint8_t epoch = 1;

/* The bytes in use — blocks not free and pages of large objects — against the heap limit. */
static size_t bytes_in_use;
static size_t heap_limit;

/* The pace (P7): bytes handed to allocators since the last collection, against the budget —
 * counted as runs are handed out, by every thread. The generations (P8): what the last full
 * collection found alive, and what the young ones marked since — the old heap is their sum, the
 * budget grows with it, and a full collection comes when it doubled. */
static atomic_size_t since_collection;
static size_t budget = MIN_BUDGET;
static atomic_size_t collections, full_collections, young_collections;
static size_t live_full, promoted;

static inline size_t granule_of(uintptr_t at) { return (at - heap_base) / GRANULE; }
static inline size_t line_of(uintptr_t at) { return (at - heap_base) / LINE; }
static inline uintptr_t block_address(uint32_t block) { return heap_base + (uintptr_t)block * BLOCK; }
static inline Block *block_info(uint32_t block) {
    return &chunks[block / BLOCKS_PER_CHUNK].blocks[block % BLOCKS_PER_CHUNK];
}
static inline bool in_heap(uintptr_t at) { return at - heap_base < heap_span; }

static inline bool bit(const uint8_t *bits, size_t g) { return (bits[g >> 3] >> (g & 7)) & 1; }
static inline void set_bit(uint8_t *bits, size_t g) { bits[g >> 3] |= (uint8_t)(1u << (g & 7)); }
static inline void clear_bit(uint8_t *bits, size_t g) { bits[g >> 3] &= (uint8_t)~(1u << (g & 7)); }

static inline void set_start(uintptr_t at) { set_bit(start_bits, granule_of(at)); }

/* The start bits of [from, to), both line-aligned: two bytes a line. */
static void clear_starts(uintptr_t from, uintptr_t to) {
    memset(start_bits + granule_of(from) / 8, 0, (to - from) / GRANULE / 8);
}

/* One lock for what threads share: the free blocks, the chunks, the large objects, the lists of
 * roots and weak references, the registry of threads; a collection holds it throughout. A
 * thread's own buffer needs none. */
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

/* Under the lock: chunk `c` committed with its side tables. */
static bool commit_chunk(size_t c) {
    Chunk *chunk = &chunks[c];
    if (chunk->committed) return true;
    if (!os_commit((void *)(heap_base + c * CHUNK), CHUNK)) return false;
    if (!os_commit(line_marks + c * LINE_BYTES_PER_CHUNK, LINE_BYTES_PER_CHUNK)) return false;
    if (!os_commit(lyr_gc_cards + c * LINE_BYTES_PER_CHUNK, LINE_BYTES_PER_CHUNK)) return false;
    if (!os_commit(start_bits + c * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return false;
    if (!os_commit(mark_bits + c * BIT_BYTES_PER_CHUNK, BIT_BYTES_PER_CHUNK)) return false;
    chunk->committed = 1;
    return true;
}

/* Under the lock: chunk `c`'s memory committed again where it was given back (S8). */
static bool recommit(size_t c) {
    if (!chunks[c].discarded) return true;
    if (!os_commit((void *)(heap_base + c * CHUNK), CHUNK)) return false;
    chunks[c].discarded = 0;
    return true;
}

/* Under the lock: a chunk of `kind`, committed; -1 when the reservation is used up or the system
 * refuses the memory. */
static ptrdiff_t take_chunk(ChunkKind kind) {
    size_t c = 0;
    while (c < chunks_taken && chunks[c].kind != CHUNK_UNUSED) c++;
    if (c == chunks_taken) {
        if (chunks_taken == chunk_count) return -1;
        chunks_taken++;
    }
    Chunk *chunk = &chunks[c];
    if (!commit_chunk(c)) return -1;
    /* a chunk a huge object held and left is taken again: what it hands out is zeroed, not fresh */
    uint8_t fresh = !chunk->used;
    chunk->used = 1;
    if (!recommit(c)) return -1;
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
        for (size_t b = 0; b < BLOCKS_PER_CHUNK; b++) chunk->blocks[b] = (Block){ BLOCK_FREE, fresh };
    } else if (kind == CHUNK_LARGE) {
        memset(chunk->pages, 0, PAGES_PER_CHUNK * sizeof(uint16_t));
        chunk->fresh_from = fresh ? 0 : PAGES_PER_CHUNK;
        chunk->free_pages = PAGES_PER_CHUNK;
    }
    return (ptrdiff_t)c;
}

/* --- the objects -------------------------------------------------------------------------------- */

/* The bytes of the object at `at`, rounded to granules: its descriptor's size, or for an array, a
 * string or a coroutine what the header and the length say. */
LYR_NO_SANITIZE static size_t object_bytes(const LyrObj *object) {
    const LyrDesc *desc = object->desc;
    size_t bytes;
    if (desc->flags & LYR_DESC_ARRAY) bytes = offsetof(LyrArr, data) + (size_t)((const LyrArr *)object)->len * desc->elem_size;
    else if (desc->flags & LYR_DESC_STRING) bytes = offsetof(LyrStr, bytes) + (size_t)((const LyrStr *)object)->len + 1;
    else if (desc->flags & LYR_DESC_CORO) bytes = sizeof(LyrCoro);
    else bytes = desc->size;
    return LYR_GC_ROUND(bytes);
}

/* The granule of the nearest start at or below granule `g`, not below `first`; -1 when there is
 * none. The table read a word at a time: a block's starts are 32 words. */
static ptrdiff_t start_at_or_below(size_t g, size_t first) {
    const uint64_t *words = (const uint64_t *)(const void *)start_bits;
    size_t w = g >> 6;
    uint64_t bits = words[w] & (~(uint64_t)0 >> (63 - (g & 63)));
    while (bits == 0) {
        if (w <= first >> 6) return -1;
        bits = words[--w];
    }
    size_t found = w * 64 + 63 - (size_t)__builtin_clzll(bits);
    return found < first ? -1 : (ptrdiff_t)found;
}

/* The start of the object `at` points into — its start or any byte inside it (views, 01 V9/V10) —
 * or 0 when `at` points into no object of the heap: outside it (a static object, a number), into
 * a free block, or past the end of the object before it. A conservative word and a reference of a
 * map are asked alike. A word on a start is that object, its header unread: most references are,
 * and a young collection meets an old object, marked already, at every other one (P8) — the
 * header is a cache miss the mark bit spares. */
LYR_NO_SANITIZE static uintptr_t object_of(uintptr_t at) {
    if (!in_heap(at)) return 0;
    size_t c = (at - heap_base) / CHUNK;
    if (c >= chunks_taken) return 0;
    const Chunk *chunk = &chunks[c];
    uintptr_t start;
    switch (chunk->kind) {
    case CHUNK_BLOCKS: {
        uint32_t block = (uint32_t)((at - heap_base) / BLOCK);
        if (block_info(block)->state == BLOCK_FREE) return 0;
        if (at % GRANULE == 0 && bit(start_bits, granule_of(at))) return at;
        ptrdiff_t found = start_at_or_below(granule_of(at), granule_of(block_address(block)));
        if (found < 0) return 0;
        start = heap_base + (uintptr_t)found * GRANULE;
        break;
    }
    case CHUNK_LARGE: {
        uint16_t owner = chunk->pages[((at - heap_base) % CHUNK) / PAGE];
        if (owner == 0) return 0;
        start = heap_base + c * CHUNK + (uintptr_t)(owner - 1) * PAGE;
        if (!bit(start_bits, granule_of(start))) return 0;
        break;
    }
    case CHUNK_HUGE:
        start = chunk->huge;
        if (!bit(start_bits, granule_of(start))) return 0;
        break;
    default:
        return 0;
    }
    if (at == start) return start;
    return at - start < object_bytes((const LyrObj *)start) ? start : 0;
}

/* --- free blocks -------------------------------------------------------------------------------- */

typedef struct BlockStack {
    uint32_t *items;
    size_t count, capacity;
} BlockStack;

static BlockStack free_blocks, recyclable_blocks;

static bool push_block(BlockStack *stack, uint32_t block) {
    if (stack->count == stack->capacity) {
        size_t capacity = stack->capacity ? stack->capacity * 2 : 1024;
        uint32_t *grown = realloc(stack->items, capacity * sizeof *grown);
        if (grown == NULL) return false;
        stack->items = grown;
        stack->capacity = capacity;
    }
    stack->items[stack->count++] = block;
    return true;
}

/* The blocks the sweep has yet to look at since the last collection, in address order. */
static size_t sweep_cursor, sweep_end;

/* Under the lock: the next unswept block looked at — free, recyclable or full by its line marks;
 * a free one is no longer in use. NO_BLOCK when none is left. */
static uint32_t sweep_one(void) {
    while (sweep_cursor < sweep_end) {
        uint32_t block = (uint32_t)sweep_cursor++;
        Chunk *chunk = &chunks[block / BLOCKS_PER_CHUNK];
        if (chunk->kind != CHUNK_BLOCKS) {
            sweep_cursor = (block / BLOCKS_PER_CHUNK + 1) * (size_t)BLOCKS_PER_CHUNK;
            continue;
        }
        Block *info = &chunk->blocks[block % BLOCKS_PER_CHUNK];
        if (info->state != BLOCK_UNSWEPT) continue;
        const uint8_t *marks = line_marks + line_of(block_address(block));
        size_t live = 0;
        for (size_t l = 0; l < LINES_PER_BLOCK; l++) live += marks[l] == epoch;
        if (live == 0) {
            info->state = BLOCK_FREE;
            bytes_in_use -= BLOCK;
            POISON(block_address(block), BLOCK);
        } else {
            info->state = live == LINES_PER_BLOCK ? BLOCK_FULL : BLOCK_RECYCLABLE;
            for (size_t l = 0; l < LINES_PER_BLOCK; l++) {
                if (marks[l] != epoch) POISON(block_address(block) + l * LINE, LINE);
            }
        }
        return block;
    }
    return NO_BLOCK;
}

/* What a collection is asked for: the budget is spent — a young one, unless the old heap doubled
 * since the last full one (P8) —; the heap has no room — a full one before LYR-RT0005 (P7) —; or
 * the program asks (lyr_gc_collect, lyr_gc_collect_young). */
typedef enum Need { PACED, FULL, YOUNG } Need;

static void collect(Need need);

/* A block for a thread's buffer: a recyclable one (for small objects, which fit its runs), a free
 * one, a new chunk's — and when the budget is spent a collection first, and when the heap limit is
 * reached a full one. NO_BLOCK when even that leaves none. */
static uint32_t take_block(bool small) {
    bool paced = false, full = false;
    lock();
    for (;;) {
        if (atomic_load_explicit(&since_collection, memory_order_relaxed) >= budget && !paced && !full) {
            unlock();
            collect(PACED);
            lock();
            paced = true;
            continue;
        }
        uint32_t block = NO_BLOCK;
        if (small && recyclable_blocks.count > 0) block = recyclable_blocks.items[--recyclable_blocks.count];
        while (block == NO_BLOCK) {
            uint32_t swept = sweep_one();
            if (swept == NO_BLOCK) break;
            Block *info = block_info(swept);
            if (info->state == BLOCK_FREE) {
                if (!push_block(&free_blocks, swept)) break;
            } else if (info->state == BLOCK_RECYCLABLE) {
                if (small) block = swept;
                else if (!push_block(&recyclable_blocks, swept)) break;
            }
        }
        if (block == NO_BLOCK && free_blocks.count > 0) {
            block = free_blocks.items[--free_blocks.count];
            Block *info = block_info(block);
            if (info->discarded) {
                if (!os_commit((void *)block_address(block), BLOCK)) {
                    free_blocks.count++;
                    break;
                }
                info->discarded = 0;
            }
            bytes_in_use += BLOCK;
        }
        if (block == NO_BLOCK && within_limit(BLOCK)) {
            ptrdiff_t c = take_chunk(CHUNK_BLOCKS);
            if (c >= 0) {
                /* the lowest block first: the heap grows upward */
                for (size_t b = BLOCKS_PER_CHUNK; b-- > 1;) push_block(&free_blocks, (uint32_t)((size_t)c * BLOCKS_PER_CHUNK + b));
                block = (uint32_t)((size_t)c * BLOCKS_PER_CHUNK);
                bytes_in_use += BLOCK;
            }
        }
        if (block != NO_BLOCK) {
            block_info(block)->state = BLOCK_IN_USE;
            unlock();
            return block;
        }
        if (full) break;
        unlock();
        collect(FULL);
        lock();
        full = true;
    }
    unlock();
    return NO_BLOCK;
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

/* Where a thread stands for a collection: running compiled code (it must be stopped), in a safe
 * region (blocked in the system: stopped already), or parked at a safepoint. */
enum { RUNNING, SAFE, PARKED };

typedef struct Thread {
    struct Thread *next, *prev;
    LyrMutator *mutator;   /* the thread's lyr_mutator */
    Buffer small, medium;
    bool in_slow_path;     /* the buffers here are the thread's truth, not lyr_mutator */
    uint64_t allocations, allocated_bytes;
    LyrStackCtx *own;      /* coro.c's record of the thread's own stack */
    LyrStackCtx *running;  /* the stack the thread runs on: its own, or a coroutine's */
    void *stack_base;      /* the cold end of its own stack */
    atomic_int state;
    uintptr_t stop_sp;     /* SAFE or PARKED: where its frames begin */
    uintptr_t *saved;      /* SAFE: the frames of the region's entry, copied — the callers' registers */
    size_t saved_words, saved_capacity;
    uintptr_t stack_limit; /* its own bound for the prologue's check, put back after a stop */
} Thread;

static _Thread_local Thread *self;
static Thread *threads;   /* under registry_lock */

/* The thread that has stopped the world: a region it enters during the collection (a warning
 * written by a coroutine's hook) is no region — it would wait for its own restart. */
static _Thread_local bool collecting_here;

/* The locks around a collection, taken in this order: one collection at a time (a mutator waits
 * for it in a safe region); the registry of threads, fixed from the stop to the restart; the heap,
 * once the world stands (a thread blocked on it is not stopped and would never park). */
#ifdef _WIN32
static SRWLOCK collect_lock = SRWLOCK_INIT, registry_lock = SRWLOCK_INIT, stop_lock = SRWLOCK_INIT;
static CONDITION_VARIABLE restarted = CONDITION_VARIABLE_INIT;
static void take(SRWLOCK *l) { AcquireSRWLockExclusive(l); }
static void give(SRWLOCK *l) { ReleaseSRWLockExclusive(l); }
static void wait_restart(void) { SleepConditionVariableSRW(&restarted, &stop_lock, INFINITE, 0); }
static void signal_restart(void) { WakeAllConditionVariable(&restarted); }
#else
static pthread_mutex_t collect_lock = PTHREAD_MUTEX_INITIALIZER, registry_lock = PTHREAD_MUTEX_INITIALIZER,
                       stop_lock = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t restarted = PTHREAD_COND_INITIALIZER;
static void take(pthread_mutex_t *l) { pthread_mutex_lock(l); }
static void give(pthread_mutex_t *l) { pthread_mutex_unlock(l); }
static void wait_restart(void) { pthread_cond_wait(&restarted, &stop_lock); }
static void signal_restart(void) { pthread_cond_broadcast(&restarted); }
#endif

int lyr_gc_stop_requested;

/* Waits until no stop is asked for. */
static void wait_for_restart(void) {
    take(&stop_lock);
    while (__atomic_load_n(&lyr_gc_stop_requested, __ATOMIC_SEQ_CST)) wait_restart();
    give(&stop_lock);
}

/* A thread stopped (SAFE or PARKED) running again: its state set first, the flag read after —
 * the collector sets the flag first and reads the state after, so one of the two sees the other.
 * When a stop is asked for meanwhile, the thread goes back to `stopped` and waits for its end. */
static void resume_running(Thread *t, int stopped) {
    for (;;) {
        atomic_store_explicit(&t->state, RUNNING, memory_order_seq_cst);
        if (!__atomic_load_n(&lyr_gc_stop_requested, __ATOMIC_SEQ_CST)) return;
        atomic_store_explicit(&t->state, stopped, memory_order_seq_cst);
        wait_for_restart();
    }
}

static Thread *current(void) {
    Thread *t = self;
    if (LYR_UNLIKELY(t == NULL)) lyr_panic(LYR_RT_SYSTEM, "a thread allocates without being attached to the collector");
    return t;
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
    if (t->in_slow_path) return;
    t->small.cursor = t->mutator->cursor;
    t->small.limit = t->mutator->limit;
}

static void sync_out(Thread *t) {
    if (t->in_slow_path) return;
    t->mutator->cursor = t->small.cursor;
    t->mutator->limit = t->small.limit;
}

/* Hands out [from, to) of `buffer`'s block: zeroed (unless the block is fresh), its stale start
 * bits cleared. */
static void hand_out(Buffer *buffer, uintptr_t from, uintptr_t to) {
    Block *block = block_info(buffer->block);
    UNPOISON(from, to - from);
    if (!block->fresh) {
        clear_starts(from, to);
        memset((void *)from, 0, to - from);
    }
    buffer->cursor = buffer->parsed = from;
    buffer->limit = to;
    atomic_fetch_add_explicit(&since_collection, to - from, memory_order_relaxed);
}

/* `buffer`'s block given up: full until the next collection looks at it again. */
static void retire(Buffer *buffer) {
    if (buffer->block != NO_BLOCK) {
        Block *block = block_info(buffer->block);
        block->state = BLOCK_FULL;
        block->fresh = 0;
    }
    *buffer = (Buffer){ 0, 0, 0, NO_BLOCK, 0 };
}

/* The block's next run of free lines of at least `bytes`, from where the search stands; false
 * when the block has none left — it is given up then. */
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
    lock();
    retire(buffer);
    unlock();
    return false;
}

/* `buffer` refilled with a run of at least `bytes`: the rest of its block, else a block taken. */
static bool refill(Thread *t, Buffer *buffer, size_t bytes, bool small) {
    parse(t, buffer);
    if (next_run(buffer, bytes)) return true;
    for (;;) {
        uint32_t block = take_block(small);
        if (block == NO_BLOCK) return false;
        /* a collection on the way retired the buffers: this one starts afresh either way */
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

/* --- large objects ---------------------------------------------------------------------------- */

/* Under the lock: `pages` free pages in a row in a large-object chunk, first fit; a chunk taken
 * when none has them. 0 when there is no room. */
static uintptr_t take_pages(size_t pages, bool *fresh) {
    for (size_t pass = 0; pass < 2; pass++) {
        for (size_t c = 0; c < chunks_taken; c++) {
            Chunk *chunk = &chunks[c];
            if (chunk->kind != CHUNK_LARGE || chunk->free_pages < pages) continue;
            if (!recommit(c)) return 0;
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
static uintptr_t take_huge(size_t count, size_t bytes, bool *fresh) {
    size_t run = 0;
    for (size_t c = 0; c < chunk_count; c++) {
        bool unused = c >= chunks_taken || chunks[c].kind == CHUNK_UNUSED;
        run = unused ? run + 1 : 0;
        if (run < count) continue;
        size_t first = c + 1 - count;
        uintptr_t start = heap_base + first * CHUNK;
        *fresh = true;
        for (size_t d = first; d <= c; d++) {
            if (!commit_chunk(d) || !recommit(d)) return 0;
            if (chunks[d].used) *fresh = false;
            chunks[d].used = 1;
            chunks[d].kind = CHUNK_HUGE;
            chunks[d].huge = start;
        }
        chunks[first].huge_bytes = bytes;
        if (c + 1 > chunks_taken) chunks_taken = c + 1;
        return start;
    }
    return 0;
}

static void *allocate_large(Thread *t, size_t rounded, size_t asked) {
    size_t pages = (rounded + PAGE - 1) / PAGE;
    size_t bytes = pages * PAGE;
    bool fresh = true, paced = false, full = false;
    uintptr_t at = 0;
    lock();
    for (;;) {
        if (atomic_load_explicit(&since_collection, memory_order_relaxed) >= budget && !paced && !full) {
            unlock();
            collect(PACED);
            lock();
            paced = true;
        }
        if (within_limit(bytes)) {
            at = pages > PAGES_PER_CHUNK / 2 ? take_huge((bytes + CHUNK - 1) / CHUNK, bytes, &fresh) : take_pages(pages, &fresh);
            if (at != 0) {
                set_start(at);
                bytes_in_use += bytes;
                atomic_fetch_add_explicit(&since_collection, bytes, memory_order_relaxed);
                break;
            }
        }
        if (full) break;
        unlock();
        collect(FULL);
        lock();
        full = true;
    }
    unlock();
    if (at == 0) exhausted(asked);
    UNPOISON(at, bytes);
    if (!fresh) memset((void *)at, 0, rounded);
    t->allocations++;
    t->allocated_bytes += rounded;
    return (void *)at;
}

/* --- callbacks of what died --------------------------------------------------------------------- */

/* A weak reference whose object died, waiting for its callback (01 L1, the Cleaner form): run on
 * the allocating thread at its next allocation or explicit collection, never in the pause. */
typedef struct Pending {
    struct Pending *next;
    LyrWeakCallback on_death;
    void *context;
    struct LyrWeak *weak;   /* the record, while it lives: freeing it cancels the callback */
} Pending;

/* A record in malloc memory, in a list the collection walks after marking: one whose object is
 * unmarked is cleared, and its callback queued. */
struct LyrWeak {
    void *object;
    LyrWeakCallback on_death;
    void *context;
    Pending *pending;   /* its callback, queued and not yet run */
    struct LyrWeak *next, *prev;
};

static Pending *pending;
static atomic_int callbacks_pending;
static _Thread_local bool draining;

static void run_callbacks(void) {
    if (draining) return;
    draining = true;
    for (;;) {
        lock();
        Pending *next = pending;
        if (next != NULL) {
            pending = next->next;
            if (next->weak != NULL) next->weak->pending = NULL;
        }
        if (pending == NULL) atomic_store_explicit(&callbacks_pending, 0, memory_order_relaxed);
        unlock();
        if (next == NULL) break;
        if (next->on_death != NULL) next->on_death(next->context);
        free(next);
    }
    draining = false;
}

/* --- the slow paths ----------------------------------------------------------------------------- */

static void *allocate(size_t asked) {
    size_t rounded = LYR_GC_ROUND(asked);
    Thread *t = current();
    if (LYR_UNLIKELY(atomic_load_explicit(&callbacks_pending, memory_order_relaxed))) run_callbacks();
    if (rounded > MEDIUM_MAX) return allocate_large(t, rounded, asked);
    sync_in(t);
    t->in_slow_path = true;
    void *at;
    if (rounded <= t->small.limit - t->small.cursor) {
        at = bump(&t->small, rounded);  /* the fast path's own case, under ASan */
    } else if (rounded > LINE) {
        /* Immix's overflow: an object of more than a line that does not fit where the small buffer
         * stands goes to a buffer of its own instead of giving up the small one's run */
        if (rounded > t->medium.limit - t->medium.cursor && !refill(t, &t->medium, rounded, false)) {
            t->in_slow_path = false;
            exhausted(asked);
        }
        at = bump(&t->medium, rounded);
    } else {
        if (!refill(t, &t->small, rounded, true)) {
            t->in_slow_path = false;
            exhausted(asked);
        }
        at = bump(&t->small, rounded);
    }
    t->in_slow_path = false;
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

/* --- roots ------------------------------------------------------------------------------------- */

/* A root lives in malloc memory, in a list the collection reads. */
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

/* Ranges scanned conservatively besides the stacks. */
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

/* The program's module globals (07 V5): the emitted table of their reference words (13 M11 P4). */
typedef struct Globals { void **const *slots; size_t count; } Globals;
static Globals *globals;
static size_t globals_count;

void lyr_gc_add_globals(void **const *slots, size_t count) {
    lock();
    Globals *grown = realloc(globals, (globals_count + 1) * sizeof *grown);
    if (grown == NULL) {
        unlock();
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory registering the globals");
    }
    globals = grown;
    globals[globals_count++] = (Globals){ slots, count };
    unlock();
}

/* --- weak references ------------------------------------------------------------------------- */

static LyrWeak *weaks;

LyrWeak *lyr_weak_new(void *object, LyrWeakCallback on_death, void *context) {
    LyrWeak *weak = malloc(sizeof *weak);
    if (LYR_UNLIKELY(weak == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a weak reference");
    weak->object = object;
    weak->on_death = on_death;
    weak->context = context;
    weak->pending = NULL;
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
    if (weak->pending != NULL) {
        weak->pending->on_death = NULL;  /* died, its callback not run yet: it will not */
        weak->pending->weak = NULL;
    }
    if (weak->prev) weak->prev->next = weak->next;
    else weaks = weak->next;
    if (weak->next) weak->next->prev = weak->prev;
    unlock();
    free(weak);
}

/* --- coroutines (01 L4, 06 A5) ------------------------------------------------------------------ */

/* The descriptor a coroutine is made with; lyr_coro_new puts its type's in place, which says
 * LYR_DESC_CORO as well. */
static const LyrDesc coro_desc = { .size = sizeof(LyrCoro), .flags = LYR_DESC_CORO, .name = "coroutine" };

LyrCoro *lyr_gc_alloc_coro(void) {
    return lyr_alloc(&coro_desc);
}

/* The coroutines whose death releases their stack (lyr_coro_abandoned): a list through the
 * coroutines themselves, which the marking does not follow. */
static LyrCoro *watched;

void lyr_gc_watch_coro(LyrCoro *co) {
    lock();
    if (!co->gc_watched) {
        co->gc_watched = 1;
        co->gc_prev = NULL;
        co->gc_next = watched;
        if (watched) watched->gc_prev = co;
        watched = co;
    }
    unlock();
}

static void unwatch(LyrCoro *co) {
    if (!co->gc_watched) return;
    if (co->gc_prev) co->gc_prev->gc_next = co->gc_next;
    else watched = co->gc_next;
    if (co->gc_next) co->gc_next->gc_prev = co->gc_prev;
    co->gc_next = co->gc_prev = NULL;
    co->gc_watched = 0;
}

void lyr_gc_unwatch_coro(LyrCoro *co) {
    lock();
    unwatch(co);
    unlock();
}

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

/* A coroutine's stack changes only while it runs, and has no barrier: the switch is its barrier.
 * The card of the line the coroutine object starts on, marked for the one a switch leaves and the
 * one it enters — a young collection reads the coroutine whole from there, its stack too (P8). */
static void ran(const Thread *t, const LyrStackCtx *ctx) {
    if (ctx == NULL || ctx == t->own) return;
    lyr_gc_card((const char *)ctx - offsetof(LyrCoro, ctx));
}

/* No collection stops a thread inside a switch — the world stops at safepoints (S3), and a switch
 * has none — so a switch only says where the thread runs now, and marks what it changes. */
void lyr_gc_switch_begin(void *gc_handle, const LyrStackCtx *to) {
    Thread *t = gc_handle;
    ran(t, t->running);
    ran(t, to);
    t->running = (LyrStackCtx *)to;
}

void lyr_gc_switch_end(void) {}

/* --- marking --------------------------------------------------------------------------------- */

static uintptr_t *mark_stack;
static size_t mark_top, mark_capacity;
static size_t live_bytes;

static void push(uintptr_t object) {
    if (mark_top == mark_capacity) {
        size_t capacity = mark_capacity ? mark_capacity * 2 : 4096;
        uintptr_t *grown = realloc(mark_stack, capacity * sizeof *grown);
        if (grown == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory marking the heap");
        mark_stack = grown;
        mark_capacity = capacity;
    }
    mark_stack[mark_top++] = object;
}

/* The object starting at `start` marked, its lines live in this epoch, and pushed when it holds
 * references; nothing when it was marked already. */
LYR_NO_SANITIZE static void mark_object(uintptr_t start) {
    size_t g = granule_of(start);
    if (bit(mark_bits, g)) return;
    set_bit(mark_bits, g);
    const LyrObj *object = (const LyrObj *)start;
    size_t bytes = object_bytes(object);
    live_bytes += bytes;
    if (chunks[(start - heap_base) / CHUNK].kind == CHUNK_BLOCKS) {
        size_t first = line_of(start), last = line_of(start + bytes - 1);
        for (size_t l = first; l <= last; l++) line_marks[l] = epoch;
    }
    uint32_t flags = object->desc->flags;
    if (flags & (LYR_DESC_HAS_REFS | LYR_DESC_CONSERVATIVE | LYR_DESC_CORO)) push(start);
}

/* A word that may be a reference: the object it points into marked. */
static inline LYR_NO_SANITIZE void mark_word(uintptr_t value) {
    uintptr_t start = object_of(value);
    if (start != 0) mark_object(start);
}

/* Every pointer-aligned word of [low, high), conservatively: a stack, a range, a union. Not
 * instrumented — it reads frames that are not its own, redzones included. */
static LYR_NO_SANITIZE void scan_range(uintptr_t low, uintptr_t high) {
    low = (low + sizeof(uintptr_t) - 1) & ~(uintptr_t)(sizeof(uintptr_t) - 1);
    for (uintptr_t at = low; at + sizeof(uintptr_t) <= high; at += sizeof(uintptr_t)) {
        mark_word(*(const uintptr_t *)at);
    }
}

/* The words of `object` its map names: by element for an array, by word for the rest. */
LYR_NO_SANITIZE static void trace_map(uintptr_t start, const LyrDesc *desc) {
    if (desc->refmap_words == 0) return;
    if (desc->flags & LYR_DESC_ARRAY) {
        const LyrArr *array = (const LyrArr *)start;
        size_t words = desc->elem_size / sizeof(uintptr_t);
        if (words > desc->refmap_words * 64) words = desc->refmap_words * 64;
        for (int64_t i = 0; i < array->len; i++) {
            const uintptr_t *element = (const uintptr_t *)(const void *)(array->data + (size_t)i * desc->elem_size);
            for (size_t w = 0; w < words; w++) {
                if ((desc->refmap[w / 64] >> (w % 64)) & 1) mark_word(element[w]);
            }
        }
    } else {
        const uintptr_t *words = (const uintptr_t *)start;
        size_t count = desc->size / sizeof(uintptr_t);
        if (count > desc->refmap_words * 64) count = desc->refmap_words * 64;
        for (size_t w = 1; w < count; w++) {
            if ((desc->refmap[w / 64] >> (w % 64)) & 1) mark_word(words[w]);
        }
    }
}

/* A coroutine: what it keeps alive by its fields, and, while its stack is mapped and stopped,
 * every word of that stack from the saved stack pointer up, conservatively — what a suspended
 * coroutine's frames hold lives as long as the coroutine (06 A5). */
LYR_NO_SANITIZE static void trace_coro(const LyrCoro *co) {
    if (co->status == 0) return;
    mark_word((uintptr_t)co->arg);
    mark_word((uintptr_t)co->resumer_coro);
    mark_word((uintptr_t)co->error);
    mark_word((uintptr_t)co->parked_top);
    mark_word((uintptr_t)co->panic_code);
    mark_word((uintptr_t)co->panic_message);
    mark_word((uintptr_t)co->panic_trace);
    if (co->mapping != NULL && !co->ctx.on_cpu && co->ctx.sp != NULL) {
        scan_range((uintptr_t)co->ctx.sp, (uintptr_t)co->ctx.base);
    }
}

/* The references of the marked object at `start` that lie in [low, high) — a dirty card's line: the
 * words its map names there, every word of a conservative one there. A coroutine whole from the
 * line it starts on, where a switch marks the card — its stack read once —, its fields from any. */
LYR_NO_SANITIZE static void trace_within(uintptr_t start, uintptr_t low, uintptr_t high) {
    const LyrObj *object = (const LyrObj *)start;
    const LyrDesc *desc = object->desc;
    if (desc->flags & LYR_DESC_CORO) {
        const LyrCoro *co = (const LyrCoro *)object;
        if (start >= low) {
            trace_coro(co);
        } else if (co->status != 0) {
            mark_word((uintptr_t)co->arg);
            mark_word((uintptr_t)co->resumer_coro);
            mark_word((uintptr_t)co->error);
            mark_word((uintptr_t)co->parked_top);
            mark_word((uintptr_t)co->panic_code);
            mark_word((uintptr_t)co->panic_message);
            mark_word((uintptr_t)co->panic_trace);
        }
        return;
    }
    uintptr_t end = start + object_bytes(object);
    if (low < start + sizeof(LyrObj)) low = start + sizeof(LyrObj);
    if (high > end) high = end;
    if (low >= high) return;
    if (desc->flags & LYR_DESC_CONSERVATIVE) {
        scan_range(low, high);
        return;
    }
    if (!(desc->flags & LYR_DESC_HAS_REFS) || desc->refmap_words == 0) return;
    if (desc->flags & LYR_DESC_ARRAY) {
        const LyrArr *array = (const LyrArr *)start;
        uintptr_t data = (uintptr_t)array->data;
        size_t size = desc->elem_size, words = size / sizeof(uintptr_t);
        if (words > desc->refmap_words * 64) words = desc->refmap_words * 64;
        if (words == 0 || high <= data) return;
        size_t first = low > data ? (low - data) / size : 0;
        size_t last = (high - data + size - 1) / size;
        if (last > (size_t)array->len) last = (size_t)array->len;
        for (size_t i = first; i < last; i++) {
            const uintptr_t *element = (const uintptr_t *)(data + i * size);
            for (size_t w = 0; w < words; w++) {
                uintptr_t at = (uintptr_t)&element[w];
                if (at < low || at >= high) continue;
                if ((desc->refmap[w / 64] >> (w % 64)) & 1) mark_word(element[w]);
            }
        }
    } else {
        const uintptr_t *words = (const uintptr_t *)start;
        size_t count = desc->size / sizeof(uintptr_t);
        if (count > desc->refmap_words * 64) count = desc->refmap_words * 64;
        size_t first = (low - start) / sizeof(uintptr_t);
        size_t last = (high - start + sizeof(uintptr_t) - 1) / sizeof(uintptr_t);
        if (last > count) last = count;
        for (size_t w = first; w < last; w++) {
            if ((desc->refmap[w / 64] >> (w % 64)) & 1) mark_word(words[w]);
        }
    }
}

/* A dirty card: the old objects on its line read there — the one reaching into it from below,
 * the ones starting on it. An unmarked one is young: the marking reaches it, or it died. */
LYR_NO_SANITIZE static void scan_card(size_t line) {
    uintptr_t low = heap_base + line * LINE, high = low + LINE;
    size_t c = (low - heap_base) / CHUNK;
    const Chunk *chunk = &chunks[c];
    uintptr_t start;
    switch (chunk->kind) {
    case CHUNK_BLOCKS: {
        uint32_t block = (uint32_t)((low - heap_base) / BLOCK);
        if (block_info(block)->state == BLOCK_FREE) return;
        size_t g = granule_of(low);
        ptrdiff_t below = start_at_or_below(g, granule_of(block_address(block)));
        if (below >= 0 && (size_t)below < g && bit(mark_bits, (size_t)below)) {
            start = heap_base + (uintptr_t)below * GRANULE;
            if (start + object_bytes((const LyrObj *)start) > low) trace_within(start, low, high);
        }
        for (size_t s = g; s < g + LINE / GRANULE; s++) {
            if (bit(start_bits, s) && bit(mark_bits, s)) trace_within(heap_base + s * GRANULE, low, high);
        }
        return;
    }
    case CHUNK_LARGE: {
        uint16_t owner = chunk->pages[((low - heap_base) % CHUNK) / PAGE];
        if (owner == 0) return;
        start = heap_base + c * CHUNK + (uintptr_t)(owner - 1) * PAGE;
        break;
    }
    case CHUNK_HUGE:
        start = chunk->huge;
        break;
    default:
        return;
    }
    size_t g = granule_of(start);
    if (bit(start_bits, g) && bit(mark_bits, g)) trace_within(start, low, high);
}

/* A young collection's roots in the heap: every dirty card, the table read eight cards at a time
 * and cleared as it is read. */
LYR_NO_SANITIZE static void scan_cards(void) {
    for (size_t c = 0; c < chunks_taken; c++) {
        if (!chunks[c].committed || chunks[c].kind == CHUNK_UNUSED) continue;
        uint8_t *cards = lyr_gc_cards + c * LINE_BYTES_PER_CHUNK;
        for (size_t w = 0; w < LINE_BYTES_PER_CHUNK; w += 8) {
            uint64_t eight;
            memcpy(&eight, cards + w, sizeof eight);
            if (eight == 0) continue;
            memset(cards + w, 0, sizeof eight);
            for (size_t b = 0; b < 8; b++) {
                if (((const uint8_t *)&eight)[b]) scan_card(c * LINE_BYTES_PER_CHUNK + w + b);
            }
        }
    }
}

/* After a full collection, which needs no card: every card clean. */
static void clear_cards(void) {
    for (size_t c = 0; c < chunks_taken; c++) {
        if (chunks[c].committed) memset(lyr_gc_cards + c * LINE_BYTES_PER_CHUNK, 0, LINE_BYTES_PER_CHUNK);
    }
}

LYR_NO_SANITIZE static void drain(void) {
    while (mark_top > 0) {
        uintptr_t start = mark_stack[--mark_top];
        const LyrObj *object = (const LyrObj *)start;
        const LyrDesc *desc = object->desc;
        if (desc->flags & LYR_DESC_CORO) {
            trace_coro((const LyrCoro *)object);
        } else if (desc->flags & LYR_DESC_CONSERVATIVE) {
            scan_range(start + sizeof(LyrObj), start + object_bytes(object));
        } else {
            trace_map(start, desc);
        }
    }
}

/* A stopped thread's stacks: the one it runs on from where it stopped, the copy of a safe region's
 * entry, and its own stack when a coroutine runs on it. */
static void scan_stopped_thread(Thread *t) {
    uintptr_t base = (uintptr_t)(t->running != NULL ? t->running->base : t->stack_base);
    scan_range(t->stop_sp, base);
    if (atomic_load_explicit(&t->state, memory_order_acquire) == SAFE && t->saved_words > 0) {
        scan_range((uintptr_t)t->saved, (uintptr_t)(t->saved + t->saved_words));
    }
    if (t->own != NULL && t->running != t->own && !t->own->on_cpu && t->own->sp != NULL) {
        scan_range((uintptr_t)t->own->sp, (uintptr_t)t->own->base);
    }
}

/* The stack the calling thread runs on, from here up — its registers spilled first, so a reference
 * held only in one is on the stack too —, and its own stack when a coroutine runs on it. */
static LYR_NOINLINE LYR_NO_SANITIZE void scan_from_here(Thread *t) {
    volatile uintptr_t anchor = 0;
    uintptr_t base = (uintptr_t)(t->running != NULL ? t->running->base : t->stack_base);
    scan_range((uintptr_t)&anchor, base);
    (void)anchor;
}

static LYR_NOINLINE void scan_this_thread(Thread *t) {
    __builtin_unwind_init();
    scan_from_here(t);
    __asm__ volatile("" ::: "memory");  /* no tail call: the spilled registers stay in this frame */
    if (t->own != NULL && t->running != t->own && !t->own->on_cpu && t->own->sp != NULL) {
        scan_range((uintptr_t)t->own->sp, (uintptr_t)t->own->base);
    }
}

static void mark_roots(Thread *t) {
    scan_this_thread(t);
    for (Thread *other = threads; other != NULL; other = other->next) {
        if (other != t) scan_stopped_thread(other);
    }
    for (size_t i = 0; i < globals_count; i++) {
        for (size_t j = 0; j < globals[i].count; j++) mark_word((uintptr_t)*globals[i].slots[j]);
    }
    for (LyrRoot *root = roots; root != NULL; root = root->next) mark_word((uintptr_t)root->object);
    for (size_t i = 0; i < range_count; i++) scan_range(ranges[i].low, ranges[i].high);
    drain();
}

#ifdef LYR_GC_VERIFY
/* The check the tests build in (13 M11 S2): a word of a marked object that its map does not name
 * and that points at the start of an unmarked object is a reference the map left out — the
 * object would be freed under a live pointer. Reported with the type and the word. */
LYR_NO_SANITIZE static void verify_object(uintptr_t start) {
    const LyrObj *object = (const LyrObj *)start;
    const LyrDesc *desc = object->desc;
    if (desc->flags & (LYR_DESC_CORO | LYR_DESC_CONSERVATIVE | LYR_DESC_STRING)) return;
    size_t bytes = object_bytes(object);
    size_t first = desc->flags & LYR_DESC_ARRAY ? offsetof(LyrArr, data) : sizeof(LyrObj);
    for (size_t at = first; at + sizeof(uintptr_t) <= bytes; at += sizeof(uintptr_t)) {
        size_t w;
        if (desc->flags & LYR_DESC_ARRAY) {
            if (desc->elem_size < sizeof(uintptr_t)) break;
            w = ((at - first) % desc->elem_size) / sizeof(uintptr_t);
        } else {
            w = at / sizeof(uintptr_t);
        }
        if (w < desc->refmap_words * 64 && ((desc->refmap[w / 64] >> (w % 64)) & 1)) continue;
        uintptr_t value = *(const uintptr_t *)(start + at);
        if (!in_heap(value) || value % GRANULE != 0) continue;
        uintptr_t target = object_of(value);
        if (target == value && !bit(mark_bits, granule_of(target))) {
            lyr_panic(LYR_RT_SYSTEM, "the map of %s does not name its word at offset %zu, which points at an unmarked %s",
                      desc->name, at, ((const LyrObj *)target)->desc->name);
        }
    }
}

/* The other check the tests build in (P8): the heap as the last collection left it, copied, and
 * at the next collection every reference of an object that collection left marked compared with
 * its copy. One that changed, on a line whose card is clean, to a young object is a store without
 * its barrier — the young object would die under it. (A reference that changed to an old object,
 * or to null, needs no card; a dead object a stale stack word kept may hold anything, but it
 * changes nothing.) */
static uintptr_t *snapshot;
static size_t snapshot_chunks;

LYR_NO_SANITIZE static void take_snapshot(void) {
    if (chunks_taken > snapshot_chunks) {
        free(snapshot);
        snapshot = malloc(chunks_taken * (size_t)CHUNK);
        snapshot_chunks = snapshot != NULL ? chunks_taken : 0;
    }
    for (size_t c = 0; c < snapshot_chunks; c++) {
        if (!chunks[c].committed || chunks[c].kind == CHUNK_UNUSED || chunks[c].discarded) continue;
        for (size_t b = 0; b < BLOCKS_PER_CHUNK; b++) {
            /* a block given back is no memory to read on Windows */
            if (chunks[c].kind == CHUNK_BLOCKS && chunks[c].blocks[b].discarded) continue;
            const uintptr_t *from = (const uintptr_t *)(heap_base + c * CHUNK + b * BLOCK);
            uintptr_t *into = snapshot + (c * CHUNK + b * BLOCK) / sizeof(uintptr_t);
            for (size_t w = 0; w < BLOCK / sizeof(uintptr_t); w++) into[w] = from[w];
        }
    }
}

LYR_NO_SANITIZE static void check_store(const char *name, uintptr_t start, size_t offset) {
    uintptr_t at = start + offset;
    size_t c = (at - heap_base) / CHUNK;
    if (c >= snapshot_chunks) return;
    uintptr_t now = *(const uintptr_t *)at, then = snapshot[(at - heap_base) / sizeof(uintptr_t)];
    if (now == then || lyr_gc_cards[line_of(at)]) return;
    uintptr_t target = object_of(now);
    if (target == 0 || bit(mark_bits, granule_of(target))) return;
    lyr_panic(LYR_RT_SYSTEM, "%s holds at offset %zu a young %s, stored without its barrier: its card is clean",
              name, offset, ((const LyrObj *)target)->desc->name);
}

LYR_NO_SANITIZE static void check_stores(uintptr_t start) {
    const LyrObj *object = (const LyrObj *)start;
    const LyrDesc *desc = object->desc;
    if (desc->flags & LYR_DESC_CORO) {
        static const size_t fields[] = {
            offsetof(LyrCoro, arg), offsetof(LyrCoro, resumer_coro), offsetof(LyrCoro, error),
            offsetof(LyrCoro, parked_top), offsetof(LyrCoro, panic_code), offsetof(LyrCoro, panic_message),
            offsetof(LyrCoro, panic_trace),
        };
        for (size_t i = 0; i < sizeof fields / sizeof fields[0]; i++) check_store(desc->name, start, fields[i]);
        return;
    }
    if (!(desc->flags & LYR_DESC_HAS_REFS) || desc->refmap_words == 0) return;
    if (desc->flags & LYR_DESC_ARRAY) {
        const LyrArr *array = (const LyrArr *)start;
        size_t size = desc->elem_size, words = size / sizeof(uintptr_t);
        if (words > desc->refmap_words * 64) words = desc->refmap_words * 64;
        for (int64_t i = 0; i < array->len; i++) {
            for (size_t w = 0; w < words; w++) {
                if ((desc->refmap[w / 64] >> (w % 64)) & 1) {
                    check_store(desc->name, start, offsetof(LyrArr, data) + (size_t)i * size + w * sizeof(uintptr_t));
                }
            }
        }
    } else {
        size_t count = desc->size / sizeof(uintptr_t);
        if (count > desc->refmap_words * 64) count = desc->refmap_words * 64;
        for (size_t w = 1; w < count; w++) {
            if ((desc->refmap[w / 64] >> (w % 64)) & 1) check_store(desc->name, start, w * sizeof(uintptr_t));
        }
    }
}

/* Before a collection marks: every object the last one left marked, its stores checked. */
static void verify_barriers(void) {
    for (size_t c = 0; c < chunks_taken && c < snapshot_chunks; c++) {
        Chunk *chunk = &chunks[c];
        if (chunk->kind == CHUNK_UNUSED) continue;
        uintptr_t base = heap_base + c * CHUNK;
        if (chunk->kind == CHUNK_HUGE) {
            if (chunk->huge == base && bit(mark_bits, granule_of(base))) check_stores(base);
            continue;
        }
        for (size_t g = 0; g < CHUNK / GRANULE; g++) {
            uintptr_t at = base + g * GRANULE;
            size_t global = granule_of(at);
            if (chunk->kind == CHUNK_BLOCKS && block_info((uint32_t)((at - heap_base) / BLOCK))->state == BLOCK_FREE) continue;
            if (bit(start_bits, global) && bit(mark_bits, global)) check_stores(at);
        }
    }
}

static void verify(void) {
    for (size_t c = 0; c < chunks_taken; c++) {
        Chunk *chunk = &chunks[c];
        if (chunk->kind == CHUNK_UNUSED) continue;
        uintptr_t base = heap_base + c * CHUNK;
        size_t granules = CHUNK / GRANULE;
        if (chunk->kind == CHUNK_HUGE) {
            if (chunk->huge == base && bit(mark_bits, granule_of(base))) verify_object(base);
            continue;
        }
        for (size_t g = 0; g < granules; g++) {
            uintptr_t at = base + g * GRANULE;
            size_t global = granule_of(at);
            if (chunk->kind == CHUNK_BLOCKS && block_info((uint32_t)((at - heap_base) / BLOCK))->state == BLOCK_FREE) continue;
            if (bit(start_bits, global) && bit(mark_bits, global)) verify_object(at);
        }
    }
}
#endif

/* After marking: every weak reference whose object is unmarked cleared, its callback queued. */
LYR_NO_SANITIZE static void clear_dead_weaks(void) {
    for (LyrWeak *weak = weaks; weak != NULL; weak = weak->next) {
        uintptr_t start = object_of((uintptr_t)weak->object);
        if (start == 0 || bit(mark_bits, granule_of(start))) continue;
        weak->object = NULL;
        if (weak->on_death == NULL) continue;
        Pending *p = malloc(sizeof *p);
        if (p == NULL) continue;  /* no memory for the callback: it is lost, the reference cleared */
        *p = (Pending){ pending, weak->on_death, weak->context, weak };
        weak->pending = p;
        pending = p;
        atomic_store_explicit(&callbacks_pending, 1, memory_order_relaxed);
    }
}

/* After marking: every watched coroutine nobody references any more goes with its stack. Its
 * object is still intact — nothing has been reused — so the hook reads it; it allocates nothing. */
LYR_NO_SANITIZE static void release_dead_coroutines(void) {
    LyrCoro *co = watched;
    while (co != NULL) {
        LyrCoro *next = co->gc_next;
        if (!bit(mark_bits, granule_of((uintptr_t)co))) {
            unwatch(co);
            lyr_coro_abandoned(co);
        }
        co = next;
    }
}

/* After marking: the large objects without a mark freed — their pages, or their chunks. */
LYR_NO_SANITIZE static void free_dead_large(void) {
    for (size_t c = 0; c < chunks_taken; c++) {
        Chunk *chunk = &chunks[c];
        uintptr_t base = heap_base + c * CHUNK;
        if (chunk->kind == CHUNK_LARGE) {
            for (size_t p = 0; p < PAGES_PER_CHUNK;) {
                if (chunk->pages[p] != p + 1) { p++; continue; }
                uintptr_t start = base + p * PAGE;
                size_t end = p + 1;
                while (end < PAGES_PER_CHUNK && chunk->pages[end] == p + 1) end++;
                if (!bit(mark_bits, granule_of(start))) {
                    clear_bit(start_bits, granule_of(start));
                    POISON(start, (end - p) * PAGE);
                    for (size_t q = p; q < end; q++) chunk->pages[q] = 0;
                    chunk->free_pages += (uint32_t)(end - p);
                    bytes_in_use -= (end - p) * PAGE;
                }
                p = end;
            }
            /* a chunk of large objects left empty goes back whole: one call, however many died */
            if (chunk->free_pages == PAGES_PER_CHUNK && !chunk->discarded && chunk->fresh_from > 0) {
                os_discard((void *)base, CHUNK);
                chunk->discarded = 1;
            }
        } else if (chunk->kind == CHUNK_HUGE && chunk->huge == base && !bit(mark_bits, granule_of(base))) {
            clear_bit(start_bits, granule_of(base));
            POISON(base, chunk->huge_bytes);
            size_t count = (chunk->huge_bytes + CHUNK - 1) / CHUNK;
            bytes_in_use -= chunk->huge_bytes;
            for (size_t d = c; d < c + count; d++) {
                chunks[d].kind = CHUNK_UNUSED;
                chunks[d].huge = 0;
                chunks[d].discarded = 1;
            }
            os_discard((void *)base, count * CHUNK);
            chunks[c].huge_bytes = 0;
        }
    }
}

/* After a full collection, with the world running again (P7, S8): the blocks no allocator swept
 * yet swept now, so that every free one is known; the free ones beyond what the next cycle
 * allocates — the budget — given back to the system, the highest first, in runs; and the free
 * stack rebuilt with the lowest on top, so that allocation stays low and what was given back is
 * taken last. Under the heap's lock, outside the pause: a thread that needs a block meanwhile
 * waits for it, the others run. */
static void give_back_blocks(void) {
    for (uint32_t block; (block = sweep_one()) != NO_BLOCK;) {
        if (block_info(block)->state == BLOCK_RECYCLABLE && !push_block(&recyclable_blocks, block)) break;
    }
    size_t keep = budget / BLOCK, kept = 0;
    size_t total = chunks_taken * (size_t)BLOCKS_PER_CHUNK, run_start = 0, run_length = 0;
    for (size_t block = 0; block <= total; block++) {
        Block *info = block < total && chunks[block / BLOCKS_PER_CHUNK].kind == CHUNK_BLOCKS ? block_info((uint32_t)block) : NULL;
        bool give = info != NULL && info->state == BLOCK_FREE && !info->discarded && kept++ >= keep;
        if (give && run_length > 0 && run_start + run_length == block) {
            run_length++;
        } else {
            if (run_length > 0) os_discard((void *)block_address((uint32_t)run_start), run_length * BLOCK);
            run_start = block;
            run_length = give ? 1 : 0;
        }
        if (give) info->discarded = 1;
    }
    free_blocks.count = 0;
    for (size_t block = total; block-- > 0;) {
        if (chunks[block / BLOCKS_PER_CHUNK].kind != CHUNK_BLOCKS) continue;
        if (block_info((uint32_t)block)->state == BLOCK_FREE && !push_block(&free_blocks, (uint32_t)block)) break;
    }
}

/* Under the lock: every block of the heap not free set to be looked at again by the sweep. */
static void unsweep_blocks(void) {
    for (size_t c = 0; c < chunks_taken; c++) {
        Chunk *chunk = &chunks[c];
        if (chunk->kind != CHUNK_BLOCKS) continue;
        for (size_t b = 0; b < BLOCKS_PER_CHUNK; b++) {
            if (chunk->blocks[b].state != BLOCK_FREE) chunk->blocks[b].state = BLOCK_UNSWEPT;
        }
    }
    recyclable_blocks.count = 0;
    sweep_cursor = 0;
    sweep_end = chunks_taken * (size_t)BLOCKS_PER_CHUNK;
}

/* A thread's buffers given up for a collection: what they made parsed, the blocks retired. Its
 * mutator record is its truth unless it stopped in the slow path. */
static void give_up_buffers(Thread *t) {
    if (!t->in_slow_path) {
        t->small.cursor = t->mutator->cursor;
        t->small.limit = t->mutator->limit;
    }
    parse(t, &t->small);
    parse(t, &t->medium);
    retire(&t->small);
    retire(&t->medium);
    if (!t->in_slow_path) t->mutator->cursor = t->mutator->limit = 0;
}

/* Every other thread asked to stop and waited for: parked at a safepoint or in a safe region. */
static void stop_the_world(Thread *t) {
    __atomic_store_n(&lyr_gc_stop_requested, 1, __ATOMIC_SEQ_CST);
    for (Thread *other = threads; other != NULL; other = other->next) {
        if (other != t) __atomic_store_n(&other->mutator->stack_limit, UINTPTR_MAX, __ATOMIC_RELAXED);
    }
    for (Thread *other = threads; other != NULL; other = other->next) {
        if (other == t) continue;
        for (unsigned spins = 0; atomic_load_explicit(&other->state, memory_order_seq_cst) == RUNNING; spins++) {
#ifdef _WIN32
            if (spins > 64) SwitchToThread();
#else
            if (spins > 64) sched_yield();
#endif
        }
    }
}

static void restart_the_world(Thread *t) {
    for (Thread *other = threads; other != NULL; other = other->next) {
        if (other != t) __atomic_store_n(&other->mutator->stack_limit, other->stack_limit, __ATOMIC_RELAXED);
    }
    take(&stop_lock);
    __atomic_store_n(&lyr_gc_stop_requested, 0, __ATOMIC_SEQ_CST);
    signal_restart();
    give(&stop_lock);
}

/* A collection with the world stopped, young or full as `need` asks and the old heap's growth
 * decides (P8). A thread that finds one under way waits for it in a safe region and does not
 * collect again — unless it needs a full one and none ran meanwhile. */
static void collect(Need need) {
    Thread *t = current();
    size_t seen = atomic_load(&collections), seen_full = atomic_load(&full_collections);
    LYR_GC_ENTER_SAFE();
    take(&collect_lock);
    resume_running(t, SAFE);  /* nobody stops the world while this thread holds the lock */
    if (need == FULL ? atomic_load(&full_collections) != seen_full : atomic_load(&collections) != seen) {
        give(&collect_lock);
        return;
    }
    take(&registry_lock);
    stop_the_world(t);
    collecting_here = true;
    lock();
    for (Thread *other = threads; other != NULL; other = other->next) give_up_buffers(other);
#ifdef LYR_GC_VERIFY
    verify_barriers();
#endif

    size_t floor = live_full > MIN_BUDGET ? live_full : MIN_BUDGET;
    bool full = need == FULL || (need == PACED && live_full + promoted >= 2 * floor);
    live_bytes = 0;
    if (full) {
        /* a new epoch: no line is live until this collection finds it so, no object old */
        if (++epoch == 0) {
            for (size_t c = 0; c < chunks_taken; c++) {
                if (chunks[c].committed) memset(line_marks + c * LINE_BYTES_PER_CHUNK, 0, LINE_BYTES_PER_CHUNK);
            }
            epoch = 1;
        }
        for (size_t c = 0; c < chunks_taken; c++) {
            if (chunks[c].committed) memset(mark_bits + c * BIT_BYTES_PER_CHUNK, 0, BIT_BYTES_PER_CHUNK);
        }
    } else {
        /* young: the marks and the epoch stay — the old lines stay live —, and the roots grow by
         * the stores into old objects and the coroutines that ran */
        scan_cards();
    }
    mark_roots(t);
#ifdef LYR_GC_VERIFY
    verify();
#endif
    if (full) clear_cards();
    clear_dead_weaks();
    release_dead_coroutines();
    free_dead_large();
    unsweep_blocks();
    if (full) {
        live_full = live_bytes;
        promoted = 0;
        atomic_fetch_add(&full_collections, 1);
    } else {
        promoted += live_bytes;
        atomic_fetch_add(&young_collections, 1);
    }
    atomic_store_explicit(&since_collection, 0, memory_order_relaxed);
    budget = live_full + promoted > MIN_BUDGET ? live_full + promoted : MIN_BUDGET;
    atomic_fetch_add(&collections, 1);
#ifdef LYR_GC_VERIFY
    take_snapshot();
#endif
    unlock();
    collecting_here = false;
    restart_the_world(t);
    if (full) {
        lock();
        give_back_blocks();
        unlock();
    }
    give(&registry_lock);
    give(&collect_lock);
}

/* --- safepoints and safe regions ------------------------------------------------------------- */

/* Parked: the frames above stand still, the registers lie in the frame below (park_here). */
static LYR_NOINLINE void park_with_registers_saved(Thread *t) {
    volatile uintptr_t anchor = 0;
    t->stop_sp = (uintptr_t)&anchor;
    atomic_store_explicit(&t->state, PARKED, memory_order_seq_cst);
    wait_for_restart();
    resume_running(t, PARKED);
    (void)anchor;
}

static LYR_NOINLINE void park_here(Thread *t) {
    __builtin_unwind_init();
    park_with_registers_saved(t);
    __asm__ volatile("" ::: "memory");  /* no tail call: the spilled registers stay in this frame */
}

/* A loop back-edge's poll found a stop asked for. */
void lyr_gc_safepoint_slow(void) {
    Thread *t = self;
    if (t == NULL || !__atomic_load_n(&lyr_gc_stop_requested, __ATOMIC_SEQ_CST)) return;
    park_here(t);
}

/* A function entry's check failed: a stop asked for — or, below the thread's own bound, a stack
 * overflow, the panic it is (13 §1.4 item 7). */
void lyr_gc_stack_slow(uintptr_t frame) {
    Thread *t = self;
    if (t == NULL) {
        if (frame < lyr_mutator.stack_limit) lyr_stack_overflow();
        return;
    }
    if (__atomic_load_n(&lyr_gc_stop_requested, __ATOMIC_SEQ_CST)) {
        park_here(t);
        return;
    }
    if (frame < t->stack_limit) lyr_stack_overflow();
}

/* The bound for the stack the thread runs on now (a switch, a start): the thread's own record,
 * and the one compiled code reads unless a stop is asked for — that maximum stays until the
 * restart, which puts back the record. */
void lyr_gc_stack_limit(uintptr_t limit) {
    Thread *t = self;
    if (t != NULL) t->stack_limit = limit;
    uintptr_t seen = __atomic_load_n(&lyr_mutator.stack_limit, __ATOMIC_SEQ_CST);
    while (seen != UINTPTR_MAX &&
           !__atomic_compare_exchange_n(&lyr_mutator.stack_limit, &seen, limit, false, __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST)) {
    }
}

/* The region's entry frames, from here up to the caller's marker, copied: the registers of every
 * caller lie there since __builtin_unwind_init spilled them in lyr_gc_enter_safe's prologue. */
static LYR_NOINLINE LYR_NO_SANITIZE void save_entry_frames(Thread *t, uintptr_t marker) {
    volatile uintptr_t anchor = 0;
    uintptr_t low = (uintptr_t)&anchor & ~(uintptr_t)(sizeof(uintptr_t) - 1);
    size_t words = marker > low ? (marker - low) / sizeof(uintptr_t) : 0;
    if (words > t->saved_capacity) {
        uintptr_t *grown = realloc(t->saved, words * sizeof *grown);
        if (grown == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory entering a safe region");
        t->saved = grown;
        t->saved_capacity = words;
    }
    for (size_t i = 0; i < words; i++) t->saved[i] = ((const uintptr_t *)low)[i];
    t->saved_words = words;
    t->stop_sp = marker;
    atomic_store_explicit(&t->state, SAFE, memory_order_seq_cst);
    (void)anchor;
}

/* Entering and leaving keep errno and Windows' last error as they were: the caller reads what its
 * system call left after the region — a thread-local read clears the last error on Windows, and a
 * wait for a restart takes locks. (A poller's wait that timed out read as failed with error 0
 * there once the own collector was the default.) */
LYR_NOINLINE void lyr_gc_enter_safe(void *marker) {
    __builtin_unwind_init();
    int kept_errno = errno;
#ifdef _WIN32
    DWORD kept_error = GetLastError();
#endif
    Thread *t = self;
    if (t != NULL && !collecting_here) save_entry_frames(t, (uintptr_t)marker);
#ifdef _WIN32
    SetLastError(kept_error);
#endif
    errno = kept_errno;
    __asm__ volatile("" ::: "memory");
}

void lyr_gc_leave_safe(void) {
    int kept_errno = errno;
#ifdef _WIN32
    DWORD kept_error = GetLastError();
#endif
    Thread *t = self;
    if (t != NULL && !collecting_here) resume_running(t, SAFE);
#ifdef _WIN32
    SetLastError(kept_error);
#endif
    errno = kept_errno;
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

/* The calling thread in the registry: registered stopped (SAFE), so a collection under way waits
 * for nothing of it, then running — after that collection, when there is one. */
static Thread *register_this_thread(void) {
    Thread *t = calloc(1, sizeof *t);
    if (t == NULL) return NULL;
    t->mutator = &lyr_mutator;
    t->small.block = t->medium.block = NO_BLOCK;
    t->stack_base = stack_base_of_this_thread();
    atomic_init(&t->state, SAFE);
    t->stack_limit = lyr_stack_own_limit();
    lyr_mutator.stack_limit = t->stack_limit;
    {
        volatile uintptr_t here = 0;
        t->stop_sp = (uintptr_t)&here;  /* nothing of the program on the stack yet */
    }
    self = t;
    take(&registry_lock);
    t->next = threads;
    if (threads) threads->prev = t;
    threads = t;
    give(&registry_lock);
    resume_running(t, SAFE);
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
        lyr_gc_cards = tables + lines;
        start_bits = tables + 2 * lines;
        mark_bits = start_bits + bits;
    }
    if (heap_base == 0) lyr_panic(LYR_RT_OUT_OF_MEMORY, "the system grants no address space for the heap");
    lyr_gc_heap_base = heap_base;
    lyr_gc_heap_span = heap_span;
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
    sync_in(t);
    parse(t, &t->small);
    parse(t, &t->medium);
    lock();
    retire(&t->small);
    retire(&t->medium);
    unlock();
    sync_out(t);
    lyr_coro_thread_end();
    lyr_crash_thread_end();
    /* out of the registry stopped: a collection under way has it SAFE, with nothing to scan */
    LYR_GC_ENTER_SAFE();
    take(&registry_lock);
    if (t->prev) t->prev->next = t->next;
    else threads = t->next;
    if (t->next) t->next->prev = t->prev;
    give(&registry_lock);
    self = NULL;
    lyr_mutator.cursor = lyr_mutator.limit = 0;
    free(t->saved);
    free(t);
}

/* --- collection -------------------------------------------------------------------------------- */

void lyr_gc_collect(void) {
    collect(FULL);
    run_callbacks();
}

void lyr_gc_collect_young(void) {
    collect(YOUNG);
    run_callbacks();
}

size_t lyr_gc_young_collections(void) { return atomic_load(&young_collections); }

size_t lyr_gc_heap_size(void) { return heap_in_use(); }

size_t lyr_gc_collections(void) { return atomic_load(&collections); }

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
