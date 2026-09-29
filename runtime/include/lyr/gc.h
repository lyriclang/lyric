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

/* Stores a reference into a heap slot. Empty in stage 1; stage 3 records old-to-young stores.
 * Emitted code writes every reference field through it from day one. */
#define LYR_WRITE_BARRIER(object, slot, value) ((void)(object), (*(slot) = (value)))

/* A point where a stop-the-world collection may stop this thread. Empty until stage 2. */
#define LYR_SAFEPOINT() ((void)0)

/* Collects now. For tests and hosts; programs never need it. */
void lyr_gc_collect(void);

/* Bytes the collector currently holds from the operating system. */
size_t lyr_gc_heap_size(void);

/* Number of collections since start. */
size_t lyr_gc_collections(void);

#endif
