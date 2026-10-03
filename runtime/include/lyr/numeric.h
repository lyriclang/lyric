/* The number tower's operations that are not a plain C operator (design/v5/spec/03 T1d, T2):
 * shifts that panic on a count the type cannot take, the wrap operators, a float to an integer
 * that saturates, and the check behind `uint32 as char`. Everything here is a macro or a
 * `static inline` so a site costs a compare and a never-taken branch, as the checked arithmetic
 * in panic.h does. */
#ifndef LYR_NUMERIC_H
#define LYR_NUMERIC_H

#include <stdint.h>
#include "lyr/panic.h"

/* A shift by a negative count or by the width of the type or more panics (T2: no silent
 * masking, which is what the hardware would do). Bits shifted out are gone — that is a shift,
 * not an overflow. `T` is the operand type, `U` its unsigned twin, `bits` its width: a left
 * shift computes in unsigned arithmetic, where C defines what a signed shift into the sign bit
 * leaves undefined; a right shift of a signed value is arithmetic on every compiler this
 * toolchain drives. A negative count is a huge unsigned one, so one compare covers both. */
LYR_NORETURN void lyr_panic_shift(int64_t count, int bits);
#define LYR_CHECKED_SHL(T, U, bits, a, b)                                                           \
    __extension__({                                                                                 \
        T lyr_a_ = (a), lyr_b_ = (b);                                                               \
        if (LYR_UNLIKELY((uint64_t)lyr_b_ >= (bits))) lyr_panic_shift((int64_t)lyr_b_, (bits));    \
        (T)(U)((uint64_t)(U)lyr_a_ << (unsigned)lyr_b_);                                            \
    })
#define LYR_CHECKED_SHR(T, U, bits, a, b)                                                           \
    __extension__({                                                                                 \
        T lyr_a_ = (a), lyr_b_ = (b);                                                               \
        if (LYR_UNLIKELY((uint64_t)lyr_b_ >= (bits))) lyr_panic_shift((int64_t)lyr_b_, (bits));    \
        (T)(lyr_a_ >> (unsigned)lyr_b_);                                                            \
    })

/* `+%` `-%` `*%` (08 Y4): two's-complement wrap at the type's width. The arithmetic is done in
 * uint64_t, where C defines the wrap, and the result reduced to the width by the conversion —
 * which C defines for the unsigned twin, and clang and gcc define as the same reduction for the
 * signed type. In 64 bits also so that a narrow multiplication never promotes to a signed int
 * and overflows there (uint16 * uint16 is an int product in C). */
#define LYR_WRAP_ADD(T, U, a, b) ((T)(U)((uint64_t)(U)(a) + (uint64_t)(U)(b)))
#define LYR_WRAP_SUB(T, U, a, b) ((T)(U)((uint64_t)(U)(a) - (uint64_t)(U)(b)))
#define LYR_WRAP_MUL(T, U, a, b) ((T)(U)((uint64_t)(U)(a) * (uint64_t)(U)(b)))

/* std.core's number natives (design/v5/spec/10 B5): whether + - * leave the operand type, and
 * the result wrapped to it — what the builtin stores either way, the `+%` of a builtin —, the bit
 * counts, the rotation. One macro each serves every integer type, through the operand's own type
 * and width; the operand's bits are taken zero-extended (a negative narrow value would
 * sign-extend into the upper bits). */
#define LYR_ADD_OVERFLOWS(a, b) LYR_OVERFLOWS_(__builtin_add_overflow, a, b)
#define LYR_SUB_OVERFLOWS(a, b) LYR_OVERFLOWS_(__builtin_sub_overflow, a, b)
#define LYR_MUL_OVERFLOWS(a, b) LYR_OVERFLOWS_(__builtin_mul_overflow, a, b)
#define LYR_OVERFLOWS_(builtin, a, b)                                                               \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(a) lyr_result_;                                                             \
        builtin((a), (b), &lyr_result_);                                                            \
    })
#define LYR_ADD_WRAPPING(a, b) LYR_WRAPPING_(__builtin_add_overflow, a, b)
#define LYR_SUB_WRAPPING(a, b) LYR_WRAPPING_(__builtin_sub_overflow, a, b)
#define LYR_MUL_WRAPPING(a, b) LYR_WRAPPING_(__builtin_mul_overflow, a, b)
#define LYR_WRAPPING_(builtin, a, b)                                                                \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(a) lyr_result_;                                                             \
        (void)builtin((a), (b), &lyr_result_);                                                      \
        lyr_result_;                                                                                \
    })
#define LYR_WIDTH_(x) ((int64_t)(sizeof(x) * 8))
#define LYR_MASK_(x) (sizeof(x) == 8 ? UINT64_MAX : ((UINT64_C(1) << (sizeof(x) * 8)) - 1))
#define LYR_BITS_(x) ((uint64_t)(x) & LYR_MASK_(x))
#define LYR_CLZ(x)                                                                                  \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(x) lyr_x_ = (x);                                                            \
        lyr_x_ == 0 ? LYR_WIDTH_(lyr_x_)                                                            \
                    : (int64_t)__builtin_clzll(LYR_BITS_(lyr_x_)) - (64 - LYR_WIDTH_(lyr_x_));     \
    })
#define LYR_CTZ(x)                                                                                  \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(x) lyr_x_ = (x);                                                            \
        lyr_x_ == 0 ? LYR_WIDTH_(lyr_x_) : (int64_t)__builtin_ctzll(LYR_BITS_(lyr_x_));             \
    })
#define LYR_POPCOUNT(x) ((int64_t)__builtin_popcountll(LYR_BITS_(x)))
/* A rotation by any count, taken modulo the width; a negative one turns the other way. */
#define LYR_ROTL(x, n)                                                                              \
    __extension__({                                                                                 \
        LYR_VALUE_TYPE_(x) lyr_x_ = (x);                                                            \
        const int64_t lyr_w_ = LYR_WIDTH_(lyr_x_);                                                  \
        const int64_t lyr_s_ = (((int64_t)(n) % lyr_w_) + lyr_w_) % lyr_w_;                        \
        const uint64_t lyr_b_ = LYR_BITS_(lyr_x_);                                                  \
        lyr_s_ == 0 ? lyr_x_                                                                        \
                    : (LYR_VALUE_TYPE_(x))(((lyr_b_ << lyr_s_) | (lyr_b_ >> (lyr_w_ - lyr_s_)))     \
                                           & LYR_MASK_(lyr_x_));                                    \
    })

/* A float to an integer (T1d, Rust's rule): toward zero; beyond the range, the nearest bound;
 * NaN gives 0. C leaves an out-of-range conversion undefined, so the bounds are tested first,
 * on the double, against the first value OUTSIDE the range in each direction — those are exact
 * doubles for every width, except that -2^63 - 1 is not, and -2^63 itself is INT64_MIN. A
 * float32 source converts to double first, exactly. */
#define LYR_SATURATE_(name, T, low, high, min, max)                                                 \
    static inline T name(double x) {                                                                \
        if (x != x) return 0;                                                                       \
        if (x >= (high)) return (max);                                                              \
        if (x <= (low)) return (min);                                                               \
        return (T)x;                                                                                \
    }
LYR_SATURATE_(lyr_f64_to_i8, int8_t, -129.0, 128.0, INT8_MIN, INT8_MAX)
LYR_SATURATE_(lyr_f64_to_i16, int16_t, -32769.0, 32768.0, INT16_MIN, INT16_MAX)
LYR_SATURATE_(lyr_f64_to_i32, int32_t, -2147483649.0, 2147483648.0, INT32_MIN, INT32_MAX)
LYR_SATURATE_(lyr_f64_to_i64, int64_t, -9223372036854775808.0, 9223372036854775808.0, INT64_MIN, INT64_MAX)
LYR_SATURATE_(lyr_f64_to_u8, uint8_t, -1.0, 256.0, 0, UINT8_MAX)
LYR_SATURATE_(lyr_f64_to_u16, uint16_t, -1.0, 65536.0, 0, UINT16_MAX)
LYR_SATURATE_(lyr_f64_to_u32, uint32_t, -1.0, 4294967296.0, 0, UINT32_MAX)
LYR_SATURATE_(lyr_f64_to_u64, uint64_t, -1.0, 18446744073709551616.0, 0, UINT64_MAX)
#undef LYR_SATURATE_

/* `uint32 as char` (T1d): a char is a Unicode scalar value, so a surrogate or a value above
 * U+10FFFF is refused at the conversion, with RT0009 — the one place a char can come from a
 * number, and therefore the one place the invariant is checked. A macro, as the checked
 * arithmetic is, so the panic's trace starts at the program's line and not in a helper. */
LYR_NORETURN void lyr_panic_char(uint32_t value);
#define LYR_CHAR_FROM_U32(v)                                                                            __extension__({                                                                                         uint32_t lyr_c_ = (v);                                                                              if (LYR_UNLIKELY(lyr_c_ > 0x10FFFF || (lyr_c_ >= 0xD800 && lyr_c_ <= 0xDFFF)))                          lyr_panic_char(lyr_c_);                                                                         lyr_c_;                                                                                         })

#endif
