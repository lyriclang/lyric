/* The collector contract, stage 0 (design/v5/spec/01 L1). Emitted code and the runtime talk to
 * the collector only through this header; what sits behind it changes by stage (1: Boehm,
 * 2: own Immix heap, 3: generational) without touching the emitter. Which collector a build has
 * is a define of the build: LYR_GC_BOEHM for stage 1, kept to compare against (13 M11 P11);
 * without it, the runtime's own (gc_immix.c). */
#ifndef LYR_GC_H
#define LYR_GC_H

#include "lyr/types.h"

#include <stddef.h>
#include <stdint.h>

/* Starts the collector for the calling (main) thread. Idempotent. */
void lyr_gc_init(void);

#ifdef LYR_GC_BOEHM

/* A fixed-size object of `desc->size` bytes, zeroed, header set. Never NULL: exhaustion panics. */
void *lyr_alloc(const LyrDesc *desc);

/* An array of `count` elements of `desc->elem_size` bytes, zeroed, length set. A negative count
 * panics (a length comes from the program text or is checked before). */
LyrArr *lyr_alloc_array(const LyrDesc *desc, int64_t count);

/* A string of `len` bytes, contents unset except the terminating NUL; the caller fills it before
 * anyone else can see it. */
LyrStr *lyr_alloc_string(int64_t len);

#else

/* The own collector (13 M11 P3): each thread bumps through a buffer of its own — a run of free
 * lines in an Immix block — so an allocation is a few instructions inline, and everything else
 * (the next run, a block, a large object, a collection, exhaustion) is the slow path's. The three
 * functions promise what they promise under Boehm: memory zeroed (a buffer is zeroed when it is
 * handed out), the header set, the length set. */
typedef struct LyrMutator {
    uintptr_t cursor;       /* the buffer's next free byte */
    uintptr_t limit;        /* its end */
    uintptr_t stack_limit;  /* the prologue's bound (P6): a frame below it stops — the maximum while a
                               collection asks the thread to stop */
} LyrMutator;

#if defined(__ELF__) && (defined(__GNUC__) || defined(__clang__))
extern _Thread_local LyrMutator lyr_mutator __attribute__((tls_model("initial-exec")));
#else
extern _Thread_local LyrMutator lyr_mutator;
#endif

/* The slow paths, with `bytes` as asked (not rounded). Each returns its object zeroed with the
 * header set — the array's and the string's length too —, or panics: RT0005 when no memory is
 * left, RT0007 for a negative length. */
void *lyr_gc_alloc_slow(const LyrDesc *desc, size_t bytes);
LyrArr *lyr_gc_alloc_array_slow(const LyrDesc *desc, int64_t count);
LyrStr *lyr_gc_alloc_string_slow(int64_t len);

/* Objects lie on 16-byte granules: an array's elements are aligned to 16 (types.h). */
#define LYR_GC_GRANULE 16
#define LYR_GC_ROUND(bytes) (((size_t)(bytes) + (LYR_GC_GRANULE - 1)) & ~(size_t)(LYR_GC_GRANULE - 1))

/* A length from here up goes to the slow path whole, which checks it: below it the arithmetic
 * inline cannot overflow. A negative one is a huge one as unsigned. */
#define LYR_GC_INLINE_LENGTH ((uint64_t)1 << 40)

/* Under ASan every allocation takes the slow path (01 L12), which tells ASan what it hands out. */
#if defined(__SANITIZE_ADDRESS__)
#  define LYR_GC_INLINE 0
#elif defined(__has_feature)
#  if __has_feature(address_sanitizer)
#    define LYR_GC_INLINE 0
#  endif
#endif
#ifndef LYR_GC_INLINE
#  define LYR_GC_INLINE 1
#endif

#if defined(__GNUC__) || defined(__clang__)
#  define LYR_GC_SLOW(x) __builtin_expect(!!(x), 0)
#else
#  define LYR_GC_SLOW(x) (x)
#endif

/* The bump: `rounded` bytes from the buffer, or NULL for the slow path. A load of the two
 * bounds, a compare, a store — with the header, the fast path 01 L1 asks for. */
static inline void *lyr_gc_bump(size_t rounded) {
    uintptr_t at = lyr_mutator.cursor;
    if (LYR_GC_SLOW(!LYR_GC_INLINE || rounded > lyr_mutator.limit - at)) return NULL;
    lyr_mutator.cursor = at + rounded;
    return (void *)at;
}

static inline void *lyr_alloc(const LyrDesc *desc) {
    LyrObj *object = (LyrObj *)lyr_gc_bump(LYR_GC_ROUND(desc->size));
    if (LYR_GC_SLOW(object == NULL)) return lyr_gc_alloc_slow(desc, desc->size);
    object->desc = desc;
    return object;
}

static inline LyrArr *lyr_alloc_array(const LyrDesc *desc, int64_t count) {
    if (LYR_GC_SLOW((uint64_t)count >= LYR_GC_INLINE_LENGTH)) return lyr_gc_alloc_array_slow(desc, count);
    LyrArr *array = (LyrArr *)lyr_gc_bump(LYR_GC_ROUND(offsetof(LyrArr, data) + (size_t)count * desc->elem_size));
    if (LYR_GC_SLOW(array == NULL)) return lyr_gc_alloc_array_slow(desc, count);
    array->header.desc = desc;
    array->len = count;
    return array;
}

extern const LyrDesc lyr_desc_string;

static inline LyrStr *lyr_alloc_string(int64_t len) {
    if (LYR_GC_SLOW((uint64_t)len >= LYR_GC_INLINE_LENGTH)) return lyr_gc_alloc_string_slow(len);
    LyrStr *string = (LyrStr *)lyr_gc_bump(LYR_GC_ROUND(offsetof(LyrStr, bytes) + (size_t)len + 1));
    if (LYR_GC_SLOW(string == NULL)) return lyr_gc_alloc_string_slow(len);
    string->header.desc = &lyr_desc_string;
    string->len = len;
    return string;
}

/* Safepoints (13 M11 P5): a collection that stops the world sets the flag, and each thread stops at
 * its next loop back-edge — or at its next function entry, whose check of the stack (P6) the
 * collection makes fail by setting the thread's bound to the maximum. Compiled code polls; the
 * runtime's blocking calls are safe regions instead (below). */
extern int lyr_gc_stop_requested;
void lyr_gc_safepoint_slow(void);

#define LYR_SAFEPOINT()                                                                  \
    do {                                                                                 \
        if (LYR_GC_SLOW(__atomic_load_n(&lyr_gc_stop_requested, __ATOMIC_RELAXED)))       \
            lyr_gc_safepoint_slow();                                                      \
    } while (0)

/* The frame's address, not a local's: a local whose address is taken keeps the C compiler from
 * turning a call in tail position into a jump (measured: clang 21 kept the recursion), and a
 * program's tail recursion would overflow where it ran in place before. */
#define LYR_STACK_CHECK()                                                                \
    do {                                                                                 \
        if (LYR_GC_SLOW((uintptr_t)__builtin_frame_address(0)                            \
                        < __atomic_load_n(&lyr_mutator.stack_limit, __ATOMIC_RELAXED)))  \
            lyr_gc_safepoint_slow();                                                      \
    } while (0)

/* A safe region: a thread blocked in the system (a poller's wait, a read, a child's end) is
 * stopped already as far as a collection is concerned — its frames above the region hold still.
 * Entering saves the callers' registers (a reference held in one stays visible) and where the
 * frames begin; leaving waits while a collection runs. Inside, the thread touches no object it
 * does not hold through its frames, and allocates nothing. Used by the runtime around each call
 * that can block. */
void lyr_gc_enter_safe(void *marker);
void lyr_gc_leave_safe(void);
#define LYR_GC_ENTER_SAFE()                                                              \
    do {                                                                                 \
        volatile char lyr_safe_marker_ = 0;                                              \
        lyr_gc_enter_safe((void *)&lyr_safe_marker_);                                     \
    } while (0)
#define LYR_GC_LEAVE_SAFE() lyr_gc_leave_safe()

#endif

#ifdef LYR_GC_BOEHM
/* Boehm stops the world with signals: no poll, no region. */
#define LYR_SAFEPOINT() ((void)0)
#define LYR_STACK_CHECK() ((void)0)
#define LYR_GC_ENTER_SAFE() ((void)0)
#define LYR_GC_LEAVE_SAFE() ((void)0)
#endif

/* Stores a reference into a slot. A plain store in stage 1; stage 3 records old-to-young stores.
 * Emitted code writes every reference field through it from day one. `object` is the place the
 * emitter holds for the slot and not always an object's start: a struct lies inline in its
 * object, so it may be an interior pointer, and a struct on the stack is no heap address at all.
 * A barrier that needs the object finds it from the slot (a card table does not need it). */
#define LYR_WRITE_BARRIER(object, slot, value) ((void)(object), (*(slot) = (value)))

/* The same for a struct value that holds references, stored whole: every reference word of the
 * value is a reference store. */
#define LYR_WRITE_BARRIER_VALUE(object, slot, value) ((void)(object), (*(slot) = (value)))

/* Strong roots for pointers the collector cannot see — held by C code or a host (11 X6 GcHandle).
 * A root keeps its object alive until it is freed. Drop the handle with it: while the collector is
 * conservative (stage 1), a stale handle in scanned memory keeps alive whatever reuses its cell. */
typedef struct LyrRoot LyrRoot;
LyrRoot *lyr_root_new(void *object);
void *lyr_root_get(const LyrRoot *root);
void lyr_root_set(LyrRoot *root, void *object);
void lyr_root_free(LyrRoot *root);

/* The program's module globals (07 V5): the addresses of their reference words, a table the
 * emitted program registers before its initializer runs. The own collector reads them as roots
 * (13 M11 P4); Boehm scans the program's data itself and ignores them. */
void lyr_gc_add_globals(void **const *slots, size_t count);

/* Memory ranges scanned conservatively besides the thread stacks the collector knows: coroutine
 * stacks (M6) and any C memory that holds object pointers. */
void lyr_register_stack(void *low, void *high);
void lyr_unregister_stack(void *low, void *high);

/* Nothing moves (01 L1), so pinning does nothing. Foreign-code paths call it anyway: the rule
 * "an object passed to C must stay where it is" stays written at the place it applies. */
#define lyr_pin(object) ((void)(object))
#define lyr_unpin(object) ((void)(object))

/* An object's identity as a number: its address, which does not change while it lives, since
 * nothing moves. std.core hashes an `Identity` by it (02 M10). */
#define LYR_IDENTITY(object) ((int64_t)(uintptr_t)(object))

/* Weak references with a callback after death, in the Cleaner form (01 L1): the callback never
 * sees the object, only its context, so it cannot bring the object back. Callbacks run on the
 * allocating thread at the next allocation or explicit collection after the object died. */
typedef struct LyrWeak LyrWeak;
typedef void (*LyrWeakCallback)(void *context);
LyrWeak *lyr_weak_new(void *object, LyrWeakCallback on_death, void *context);
void *lyr_weak_get(const LyrWeak *weak);   /* NULL once the object is dead */
void lyr_weak_free(LyrWeak *weak);         /* before death: the callback will not run */

/* Threads (01 L6): a thread touches the heap only while attached. lyr_init attaches the main
 * thread; every other thread calls lyr_thread_attach first and lyr_thread_detach last. */
int lyr_thread_attach(void);   /* 0 on success */
void lyr_thread_detach(void);

/* Collects now, then runs the callbacks of what died. For tests and hosts; programs never need it. */
void lyr_gc_collect(void);

/* Bytes the collector currently holds from the operating system. */
size_t lyr_gc_heap_size(void);

/* Number of collections since start. */
size_t lyr_gc_collections(void);

/* The allocations the calling thread has made since it started, and their bytes (10 X4: what a
 * benchmark's run allocates). */
int64_t lyr_gc_thread_allocations(void);
int64_t lyr_gc_thread_allocated_bytes(void);

#endif
