/* Helpers for tests that watch objects die under a conservative stack scan: a stale copy of a
 * pointer in a dead stack slot or register can keep an object alive, so the tests allocate in
 * functions that return before the collection, wipe the stack below them, and count deaths over
 * many objects with a margin instead of expecting every single one. */
#ifndef LYR_TEST_CONSERVATIVE_H
#define LYR_TEST_CONSERVATIVE_H

#include <string.h>

#if defined(__GNUC__) || defined(__clang__)
#  define NOINLINE __attribute__((noinline))
#else
#  define NOINLINE
#endif

/* Volatile stores, one by one: a memset on a dead local is an optimizer's favourite deletion. */
NOINLINE static void wipe_stack(void) {
    volatile char area[64 * 1024];
    for (size_t i = 0; i < sizeof area; i++) area[i] = 0;
}

#endif
