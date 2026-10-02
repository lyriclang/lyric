/* Panics (design/v5/spec/05 E8, 01 E5): a programming error ends the process with a message, a
 * backtrace and exit code 101. Not an exception, not catchable except at a task boundary (M6). */
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
#define LYR_RT_PANIC            "LYR-RT0008"  /* the program called panic(message) (05 E8) */
#define LYR_RT_CHAR             "LYR-RT0009"  /* `as char` of a value that is no Unicode scalar value (03 T1d) */
#define LYR_RT_FORCED_TRY       "LYR-RT0010"  /* `try!` on an error (05 E4, E8) */
#define LYR_RT_ASSERT           "LYR-RT0011"  /* `assert(condition, message)` with a false condition (05 E8) */
#define LYR_RT_UNREACHABLE      "LYR-RT0012"  /* `unreachable(message)` reached (05 E8, E12) */
#define LYR_RT_TODO             "LYR-RT0013"  /* `todo(message)` reached (05 E8, 10 prelude) */
#define LYR_RT_COROUTINE        "LYR-RT0014"  /* a coroutine resumed while it runs, after it ended or on another
                                                 thread; a yield with none running (06 A8) */

#if defined(__GNUC__) || defined(__clang__)
#  define LYR_NORETURN __attribute__((noreturn, cold, noinline))
#  define LYR_PRINTF(f, a) __attribute__((format(printf, f, a)))
#  define LYR_UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
#  define LYR_NORETURN _Noreturn
#  define LYR_PRINTF(f, a)
#  define LYR_UNLIKELY(x) (x)
#endif

/* Writes `panic [code]: message` and the backtrace of the calling thread to the configured error
 * writer, calls the hook, and exits with 101. A panic on one thread ends the whole process. */
LYR_NORETURN LYR_PRINTF(2, 3) void lyr_panic(const char *code, const char *format, ...);

/* What a host learns about a panic (05 E8 `Runtime.onPanic`). The strings live until the process
 * ends, which is right after the hook returns. */
typedef struct LyrPanicInfo {
    const char *code;
    const char *message;
    const char *backtrace;  /* the frames as printed, one "    at …" line each; may be empty */
} LyrPanicInfo;

/* Called once, after the report is written, on the panicking thread — to log or flush. It cannot
 * prevent the end: when it returns the process exits with 101. A panic inside the hook ends the
 * process at once. NULL removes it. */
typedef void (*LyrPanicHook)(const LyrPanicInfo *info, void *context);
void lyr_set_panic_hook(LyrPanicHook hook, void *context);

/* The checks emitted code performs (03 T2): a compare and a branch that is never taken, and a
 * call without a format string, so a check stays small at every one of its many sites. */
LYR_NORETURN void lyr_panic_index(int64_t index, int64_t length);
LYR_NORETURN void lyr_panic_overflow(const char *operation);
LYR_NORETURN void lyr_panic_division_by_zero(void);
LYR_NORETURN void lyr_panic_null(void);
/* `panic(message)` from the program: the message as written, code RT0008. */
struct LyrStr;
LYR_NORETURN void lyr_panic_message(const struct LyrStr *message);
/* The catalogue's other three (05 E8): the message as written, each under its own code. */
LYR_NORETURN void lyr_panic_assert(const struct LyrStr *message);
LYR_NORETURN void lyr_panic_unreachable(const struct LyrStr *message);
LYR_NORETURN void lyr_panic_todo(const struct LyrStr *message);

/* `assert(condition, message)`: the check at the call, so the trace starts in the program's own
 * frame; the message is the argument as evaluated, before the check. */
#define LYR_ASSERT(condition, message)                                                              \
    do {                                                                                            \
        if (LYR_UNLIKELY(!(condition))) lyr_panic_assert(message);                                  \
    } while (0)

/* A view's bounds (03 T13 A2): 0 <= low <= high <= length, or a panic with the same code as an index. */
LYR_NORETURN void lyr_panic_range(int64_t low, int64_t high, int64_t length);

#define LYR_CHECK_RANGE(low, high, length)                                                          \
    do {                                                                                            \
        if (LYR_UNLIKELY((uint64_t)(low) > (uint64_t)(high) || (uint64_t)(high) > (uint64_t)(length))) \
            lyr_panic_range((int64_t)(low), (int64_t)(high), (int64_t)(length));                    \
    } while (0)

#define LYR_CHECK_INDEX(index, length)                                                              \
    do {                                                                                            \
        if (LYR_UNLIKELY((uint64_t)(index) >= (uint64_t)(length)))                                  \
            lyr_panic_index((int64_t)(index), (int64_t)(length));                                   \
    } while (0)

/* Integer arithmetic that panics instead of wrapping, in every profile (03 T2). Both operands have
 * the operation's type (the emitter converts first); the expression's value is the result.
 * Shifts, the wrap operators and the conversions are in numeric.h. */
/* The type of x's value: a comma expression converts its operand as an assignment would — no
 * qualifiers (a volatile operand gives a plain result), and unlike arithmetic, no promotion. */
#define LYR_VALUE_TYPE_(x) __typeof__(((void)0, (x)))
#define LYR_CHECKED_ADD(a, b) LYR_CHECKED_OP_(__builtin_add_overflow, "+", a, b)
#define LYR_CHECKED_SUB(a, b) LYR_CHECKED_OP_(__builtin_sub_overflow, "-", a, b)
#define LYR_CHECKED_MUL(a, b) LYR_CHECKED_OP_(__builtin_mul_overflow, "*", a, b)
#define LYR_CHECKED_OP_(builtin, op, a, b)                                                          \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(a) lyr_result_;                                                             \
        if (LYR_UNLIKELY(builtin((a), (b), &lyr_result_))) lyr_panic_overflow(op);                  \
        lyr_result_;                                                                                \
    })

/* Division by zero panics (RT0001); so does the one signed quotient that does not fit, MIN / -1
 * (RT0002). A type is signed when its -1 is below zero. */
#define LYR_CHECKED_DIV(a, b)                                                                       \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(a) lyr_a_ = (a), lyr_b_ = (b);                                              \
        if (LYR_UNLIKELY(lyr_b_ == 0)) lyr_panic_division_by_zero();                                \
        if (LYR_UNLIKELY(LYR_IS_SIGNED_(lyr_a_) && lyr_b_ == (LYR_VALUE_TYPE_(a))-1 &&              \
                         lyr_a_ == LYR_MIN_OF_(lyr_a_)))                                            \
            lyr_panic_overflow("/");                                                                \
        lyr_a_ / lyr_b_;                                                                            \
    })
/* The remainder shares both faults: a zero divisor, and MIN % -1, whose quotient does not fit
 * (C leaves it undefined; the panic keeps `%` in step with `/`, as Swift and Rust do). */
#define LYR_CHECKED_REM(a, b)                                                                       \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(a) lyr_a_ = (a), lyr_b_ = (b);                                              \
        if (LYR_UNLIKELY(lyr_b_ == 0)) lyr_panic_division_by_zero();                                \
        if (LYR_UNLIKELY(LYR_IS_SIGNED_(lyr_a_) && lyr_b_ == (LYR_VALUE_TYPE_(a))-1 &&              \
                         lyr_a_ == LYR_MIN_OF_(lyr_a_)))                                            \
            lyr_panic_overflow("%");                                                                \
        lyr_a_ % lyr_b_;                                                                            \
    })
#define LYR_IS_SIGNED_(x) ((__typeof__(x))-1 < (__typeof__(x))0)
/* The smallest value of a signed type without naming it: the sign bit alone, shifted as unsigned
 * (a signed shift into the sign bit is undefined) and converted, which clang defines as wrapping. */
#define LYR_MIN_OF_(x) ((__typeof__(x))((uint64_t)1 << (sizeof(x) * 8 - 1)))

/* `x!` on an optional reference: the reference, or a panic when it is null (RT0004). */
#define LYR_UNWRAP(p)                                                                               \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(p) lyr_p_ = (p);                                                            \
        if (LYR_UNLIKELY(lyr_p_ == 0)) lyr_panic_null();                                            \
        lyr_p_;                                                                                     \
    })

#endif
