/* The system's random bytes, and the process's hash key drawn from them. */
#ifndef LYR_RANDOM_H
#define LYR_RANDOM_H

#include "lyr/types.h"

/* `length` bytes from the system's generator; a refusal panics with LYR-RT0015. */
void lyr_random_bytes(void *buffer, size_t length);
uint64_t lyr_random_u64(void);

/* The process's SipHash key, its halves 0 and 1: drawn once, at the first call from any
 * thread, the same for the rest of the process. */
uint64_t lyr_hash_key(int64_t which);

#endif
