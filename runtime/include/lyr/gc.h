/* The collector contract, stage 0 (design/v5/spec/01 L1). Emitted code and the runtime talk to
 * the collector only through this header; what sits behind it changes by stage (1: Boehm,
 * 2: own Immix heap, 3: generational) without touching the emitter. */
#ifndef LYR_GC_H
#define LYR_GC_H

#include <stddef.h>

/* Starts the collector for the calling (main) thread. Idempotent. */
void lyr_gc_init(void);

/* Collects now. For tests and hosts; programs never need it. */
void lyr_gc_collect(void);

/* Bytes the collector currently holds from the operating system. */
size_t lyr_gc_heap_size(void);

/* Number of collections since start. */
size_t lyr_gc_collections(void);

#endif
