/* Atomics (design/v5/spec/06 G4, K6; N7 P2, P4): the operations of std.sync's Atomic<T>, which the
 * C11 builtins make on its `value` field — sequentially consistent, and macros, so that one serves
 * every T the type allows (`int`, `bool`). The emitter names a Lyric field `f_<name>`. */
#ifndef LYR_ATOMIC_H
#define LYR_ATOMIC_H

#define LYR_ATOMIC_LOAD(a) __atomic_load_n(&(a)->f_value, __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_STORE(a, v) __atomic_store_n(&(a)->f_value, (v), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_EXCHANGE(a, v) __atomic_exchange_n(&(a)->f_value, (v), __ATOMIC_SEQ_CST)
#define LYR_ATOMIC_CAS(a, expected, desired) __extension__({                                    \
        __typeof__((a)->f_value) lyr_expected_ = (expected);                                     \
        __atomic_compare_exchange_n(&(a)->f_value, &lyr_expected_, (desired), 0, __ATOMIC_SEQ_CST, \
                                    __ATOMIC_SEQ_CST);                                           \
    })
/* Wraps around, as `+%` does: the builtins define signed overflow as two's complement. */
#define LYR_ATOMIC_FETCH_ADD(a, n) __atomic_fetch_add(&(a)->f_value, (n), __ATOMIC_SEQ_CST)

#endif
