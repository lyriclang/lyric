/* Atomics (design/v5/spec/06 G4, K6; N7 P2, P4): the operations of std.task's atomic natives, which
 * the C11 builtins make on a PLACE - a pointer to a field of the scheduler's own, or to the value
 * of std.sync's Atomic<T> (the review's M7-5). Sequentially consistent, and macros, so that one
 * serves every type the machine reads and writes in one step: an int, a bool, a reference (an
 * Atomic of a class, 13 M11 P10). */
#ifndef LYR_ATOMIC_H
#define LYR_ATOMIC_H

#include "lyr/gc.h"

/* A reference written into a place marks its card, as the write barrier does (gc.h, 13 M11 P8): an
 * Atomic of a class is a slot of an object that may be old. A number marks nothing — the place's
 * type decides at compile time (a pointer's class is 5 to GCC and Clang alike). */
#define LYR_ATOMIC_CARD(at) ((void)(__builtin_classify_type(*(at)) == 5 ? (lyr_gc_card(at), 0) : 0))

#define LYR_ATOMIC_LOAD(at) __atomic_load_n((at), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_STORE(at, v) (__atomic_store_n((at), (v), __ATOMIC_SEQ_CST), LYR_ATOMIC_CARD(at))
#define LYR_ATOMIC_EXCHANGE(at, v) __extension__({                                             \
        __typeof__(*(at)) lyr_replaced_ = __atomic_exchange_n((at), (v), __ATOMIC_SEQ_CST);    \
        LYR_ATOMIC_CARD(at);                                                                   \
        lyr_replaced_;                                                                         \
    })
/* For a reference the word is compared: identity (spec 10 §8 rule 2). */
#define LYR_ATOMIC_CAS(at, expected, desired) __extension__({                                  \
        __typeof__(*(at)) lyr_expected_ = (expected);                                          \
        _Bool lyr_set_ = __atomic_compare_exchange_n((at), &lyr_expected_, (desired), 0,       \
                                                     __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST);      \
        if (lyr_set_) LYR_ATOMIC_CARD(at);                                                     \
        lyr_set_;                                                                              \
    })
/* Wraps around, as `+%` does: the builtins define signed overflow as two's complement. */
#define LYR_ATOMIC_FETCH_ADD(at, n) __atomic_fetch_add((at), (n), __ATOMIC_SEQ_CST)

#endif
