/* Atomics (design/v5/spec/06 G4, K6; N7 P2, P4): the operations of std.task's atomic natives, which
 * the C11 builtins make on a PLACE - a pointer to a field of the scheduler's own, or to the value
 * of std.sync's Atomic<T> (the review's M7-5). Sequentially consistent, and macros, so that one
 * serves every type the machine reads and writes in one step. */
#ifndef LYR_ATOMIC_H
#define LYR_ATOMIC_H

#define LYR_ATOMIC_LOAD(at) __atomic_load_n((at), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_STORE(at, v) __atomic_store_n((at), (v), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_EXCHANGE(at, v) __atomic_exchange_n((at), (v), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_CAS(at, expected, desired) __extension__({                                  \
        __typeof__(*(at)) lyr_expected_ = (expected);                                          \
        __atomic_compare_exchange_n((at), &lyr_expected_, (desired), 0, __ATOMIC_SEQ_CST,      \
                                    __ATOMIC_SEQ_CST);                                         \
    })
/* Wraps around, as `+%` does: the builtins define signed overflow as two's complement. */
#define LYR_ATOMIC_FETCH_ADD(at, n) __atomic_fetch_add((at), (n), __ATOMIC_SEQ_CST)

#endif
