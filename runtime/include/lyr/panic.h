/* Panics (design/v5/spec/05 E-series, 01 E5): a programming error ends the process with a message
 * and exit code 101. Not an exception, not catchable except at a task boundary (M6). */
#ifndef LYR_PANIC_H
#define LYR_PANIC_H

#include <stdint.h>

/* The runtime's panic codes (catalogue: M12). */
#define LYR_RT_DIVISION_BY_ZERO "LYR-RT0001"
#define LYR_RT_OVERFLOW         "LYR-RT0002"
#define LYR_RT_INDEX            "LYR-RT0003"
#define LYR_RT_NULL_UNWRAP      "LYR-RT0004"
#define LYR_RT_OUT_OF_MEMORY    "LYR-RT0005"
#define LYR_RT_STACK_OVERFLOW   "LYR-RT0006"
#define LYR_RT_ARGUMENT         "LYR-RT0007"  /* a precondition from the program text: negative length, … */

#if defined(__GNUC__) || defined(__clang__)
#  define LYR_NORETURN __attribute__((noreturn, cold))
#  define LYR_PRINTF(f, a) __attribute__((format(printf, f, a)))
#  define LYR_UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
#  define LYR_NORETURN _Noreturn
#  define LYR_PRINTF(f, a)
#  define LYR_UNLIKELY(x) (x)
#endif

/* Prints `panic [code]: message`, then a backtrace, and exits with 101. */
LYR_NORETURN LYR_PRINTF(2, 3) void lyr_panic(const char *code, const char *format, ...);

/* The checks emitted code performs; each costs a compare and a branch that is never taken. */
#define LYR_CHECK_INDEX(index, length)                                                              \
    do {                                                                                            \
        if (LYR_UNLIKELY((uint64_t)(index) >= (uint64_t)(length)))                                  \
            lyr_panic(LYR_RT_INDEX, "index %lld out of bounds for length %lld",                    \
                      (long long)(index), (long long)(length));                                     \
    } while (0)

#endif
