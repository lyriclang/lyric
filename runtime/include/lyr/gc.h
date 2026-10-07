/* The collector contract, stage 0 (design/v5/spec/01 L1). Emitted code and the runtime talk to
 * the collector only through this header; what sits behind it changes by stage (1: Boehm,
 * 2: own Immix heap, 3: generational) without touching the emitter. */
#ifndef LYR_GC_H
#define LYR_GC_H

#include "lyr/types.h"

/* Starts the collector for the calling (main) thread. Idempotent. */
void lyr_gc_init(void);

/* A fixed-size object of `desc->size` bytes, zeroed, header set. Never NULL: exhaustion panics. */
void *lyr_alloc(const LyrDesc *desc);

/* An array of `count` elements of `desc->elem_size` bytes, zeroed, length set. A negative count
 * panics (a length comes from the program text or is checked before). */
LyrArr *lyr_alloc_array(const LyrDesc *desc, int64_t count);

/* A string of `len` bytes, contents unset except the terminating NUL; the caller fills it before
 * anyone else can see it. */
LyrStr *lyr_alloc_string(int64_t len);

/* Stores a reference into a slot. A plain store in stage 1; stage 3 records old-to-young stores.
 * Emitted code writes every reference field through it from day one. `object` is the place the
 * emitter holds for the slot and not always an object's start: a struct lies inline in its
 * object, so it may be an interior pointer, and a struct on the stack is no heap address at all.
 * A barrier that needs the object finds it from the slot (a card table does not need it). */
#define LYR_WRITE_BARRIER(object, slot, value) ((void)(object), (*(slot) = (value)))

/* The same for a struct value that holds references, stored whole: every reference word of the
 * value is a reference store. */
#define LYR_WRITE_BARRIER_VALUE(object, slot, value) ((void)(object), (*(slot) = (value)))

/* A point where a stop-the-world collection may stop this thread. Empty until stage 2. */
#define LYR_SAFEPOINT() ((void)0)

/* Strong roots for pointers the collector cannot see — held by C code or a host (11 X6 GcHandle).
 * A root keeps its object alive until it is freed. Drop the handle with it: while the collector is
 * conservative (stage 1), a stale handle in scanned memory keeps alive whatever reuses its cell. */
typedef struct LyrRoot LyrRoot;
LyrRoot *lyr_root_new(void *object);
void *lyr_root_get(const LyrRoot *root);
void lyr_root_set(LyrRoot *root, void *object);
void lyr_root_free(LyrRoot *root);

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

#endif
