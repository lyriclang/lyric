/* The number tower's runtime pieces (design/v5/spec/03 T1d, T2; lyr/numeric.h, string.c), one
 * per run, chosen by the first argument. Expected: "ok" computes every operation that must not
 * panic — shifts inside the width on every type, the wrap operators at their edges (in 64 bits
 * too, and a narrow product that would overflow an int), saturation at both bounds and NaN, the
 * char check on the last valid scalar values, float and char text — prints "numeric ok" and
 * exits 0. Every other argument panics with exit 101 and this first line:
 *   shl64   panic [LYR-RT0002]: shift by 64 exceeds the width of 64 bits
 *   shr8    panic [LYR-RT0002]: shift by 8 exceeds the width of 8 bits
 *   shlneg  panic [LYR-RT0002]: shift by -1 exceeds the width of 32 bits
 *   surrogate  panic [LYR-RT0009]: 0xDFFF is not a Unicode scalar value
 *   beyond     panic [LYR-RT0009]: 0x110000 is not a Unicode scalar value */
#include "lyr/lyr.h"
#include "check.h"

#include <math.h>
#include <string.h>

/* Volatile, so no operand is known at compile time and every check runs. */
static volatile int64_t one = 1, sixty_four = 64, minus_one = -1, big = INT64_MAX, small = INT64_MIN;
static volatile int32_t i32_one = 1;
static volatile int8_t i8_max = 127, i8_min = -128, i8_eight = 8;
static volatile uint8_t u8_max = 255, u8_zero = 0, u8_129 = 129, u8_eight = 8;
static volatile uint16_t u16_max = 65535;
static volatile uint64_t u64_max = UINT64_MAX;
static volatile uint32_t last_scalar = 0x10FFFF, before_surrogates = 0xD7FF, after_surrogates = 0xE000;
static volatile uint32_t low_surrogate = 0xDFFF, beyond_unicode = 0x110000;
static volatile double nan_value = NAN, huge = 1e300, tiny = -1e300, half = 0.5, minus_half = -0.5;
static volatile double just_over_i8 = 128.0, just_under_i8 = -129.0, near_i8 = 127.9, near_min_i8 = -128.9;
static volatile double two63 = 9223372036854775808.0, two64 = 18446744073709551616.0;

static int text_is(LyrStr *text, const char *expected) { return strcmp(text->bytes, expected) == 0; }

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "ok";
    int64_t result = 0;
    if (strcmp(which, "shl64") == 0) result = LYR_CHECKED_SHL(int64_t, uint64_t, 64, one, sixty_four);
    else if (strcmp(which, "shr8") == 0) result = LYR_CHECKED_SHR(uint8_t, uint8_t, 8, u8_max, u8_eight);
    else if (strcmp(which, "shlneg") == 0) result = LYR_CHECKED_SHL(int32_t, uint32_t, 32, i32_one, (int32_t)minus_one);
    else if (strcmp(which, "surrogate") == 0) result = LYR_CHAR_FROM_U32(low_surrogate);
    else if (strcmp(which, "beyond") == 0) result = LYR_CHAR_FROM_U32(beyond_unicode);
    else {
        /* shifts: bits shifted out are gone, a signed right shift is arithmetic, the top count
         * of the width is fine */
        CHECK(LYR_CHECKED_SHL(int64_t, uint64_t, 64, one, (int64_t)63) == INT64_MIN);
        CHECK(LYR_CHECKED_SHL(int64_t, uint64_t, 64, big, one) == -2);
        CHECK(LYR_CHECKED_SHR(int64_t, uint64_t, 64, small, (int64_t)63) == -1);
        CHECK(LYR_CHECKED_SHR(int64_t, uint64_t, 64, minus_one, (int64_t)1) == -1);
        CHECK(LYR_CHECKED_SHL(int8_t, uint8_t, 8, i8_max, (int8_t)1) == -2);
        CHECK(LYR_CHECKED_SHR(int8_t, uint8_t, 8, i8_min, (int8_t)7) == -1);
        CHECK(LYR_CHECKED_SHL(uint8_t, uint8_t, 8, u8_max, (uint8_t)7) == 128);
        CHECK(LYR_CHECKED_SHR(uint64_t, uint64_t, 64, u64_max, (uint64_t)63) == 1);
        CHECK(LYR_CHECKED_SHR(uint8_t, uint8_t, 8, u8_max, (uint8_t)0) == 255);

        /* the wrap operators */
        CHECK(LYR_WRAP_ADD(int64_t, uint64_t, big, one) == INT64_MIN);
        CHECK(LYR_WRAP_SUB(int64_t, uint64_t, small, one) == INT64_MAX);
        CHECK(LYR_WRAP_MUL(int64_t, uint64_t, big, (int64_t)2) == -2);
        CHECK(LYR_WRAP_ADD(uint64_t, uint64_t, u64_max, (uint64_t)1) == 0);
        CHECK(LYR_WRAP_SUB(uint64_t, uint64_t, (uint64_t)0, (uint64_t)1) == UINT64_MAX);
        CHECK(LYR_WRAP_MUL(uint64_t, uint64_t, u64_max, u64_max) == 1);
        CHECK(LYR_WRAP_ADD(int8_t, uint8_t, i8_max, (int8_t)1) == -128);
        CHECK(LYR_WRAP_SUB(int8_t, uint8_t, i8_min, (int8_t)1) == 127);
        CHECK(LYR_WRAP_MUL(int8_t, uint8_t, i8_min, (int8_t)-1) == -128);
        CHECK(LYR_WRAP_ADD(uint8_t, uint8_t, u8_max, (uint8_t)1) == 0);
        CHECK(LYR_WRAP_SUB(uint8_t, uint8_t, u8_zero, (uint8_t)1) == 255);
        CHECK(LYR_WRAP_MUL(uint8_t, uint8_t, u8_129, u8_129) == 1);          /* 16641 mod 256 */
        CHECK(LYR_WRAP_MUL(uint16_t, uint16_t, u16_max, u16_max) == 1);      /* an int product would overflow */

        /* saturation: toward zero inside the range, the bound outside, 0 for NaN */
        CHECK(lyr_f64_to_i8(near_i8) == 127 && lyr_f64_to_i8(near_min_i8) == -128);
        CHECK(lyr_f64_to_i8(just_over_i8) == 127 && lyr_f64_to_i8(just_under_i8) == -128);
        CHECK(lyr_f64_to_i8(huge) == 127 && lyr_f64_to_i8(tiny) == -128 && lyr_f64_to_i8(nan_value) == 0);
        CHECK(lyr_f64_to_u8(minus_half) == 0 && lyr_f64_to_u8(huge) == 255 && lyr_f64_to_u8(half) == 0);
        CHECK(lyr_f64_to_i64(two63) == INT64_MAX && lyr_f64_to_i64(-two63) == INT64_MIN);
        CHECK(lyr_f64_to_i64(huge) == INT64_MAX && lyr_f64_to_i64(tiny) == INT64_MIN && lyr_f64_to_i64(nan_value) == 0);
        CHECK(lyr_f64_to_u64(two64) == UINT64_MAX && lyr_f64_to_u64(two63) == 9223372036854775808ull);
        CHECK(lyr_f64_to_u64(tiny) == 0 && lyr_f64_to_u64(nan_value) == 0);
        CHECK(lyr_f64_to_i32(huge) == INT32_MAX && lyr_f64_to_u32(huge) == UINT32_MAX && lyr_f64_to_u16(huge) == 65535);
        CHECK(lyr_f64_to_i16(tiny) == INT16_MIN);

        /* the char check passes the edges of the scalar values */
        CHECK(LYR_CHAR_FROM_U32(last_scalar) == 0x10FFFF);
        CHECK(LYR_CHAR_FROM_U32(before_surrogates) == 0xD7FF && LYR_CHAR_FROM_U32(after_surrogates) == 0xE000);

        /* text: shortest round trip, integral values without a fraction, the non-numbers */
        CHECK(text_is(lyr_str_from_float(0.1), "0.1"));
        CHECK(text_is(lyr_str_from_float(1.0), "1"));
        CHECK(text_is(lyr_str_from_float(-0.0), "-0"));
        CHECK(text_is(lyr_str_from_float(0.1 + 0.2), "0.30000000000000004"));
        CHECK(text_is(lyr_str_from_float(1e21), "1e+21"));
        CHECK(text_is(lyr_str_from_float(1e-7), "1e-07"));
        CHECK(text_is(lyr_str_from_float(123456789012345680.0), "1.2345678901234568e+17"));
        CHECK(text_is(lyr_str_from_float(5e-324), "5e-324"));
        CHECK(text_is(lyr_str_from_float(1.7976931348623157e308), "1.7976931348623157e+308"));
        CHECK(text_is(lyr_str_from_float(nan_value), "NaN"));
        CHECK(text_is(lyr_str_from_float(huge * huge), "inf"));
        CHECK(text_is(lyr_str_from_float(-huge * huge), "-inf"));
        CHECK(text_is(lyr_str_from_char('A'), "A"));
        CHECK(text_is(lyr_str_from_char(0xE9), "\xC3\xA9"));            /* é */
        CHECK(text_is(lyr_str_from_char(0x20AC), "\xE2\x82\xAC"));      /* € */
        CHECK(text_is(lyr_str_from_char(0x1F600), "\xF0\x9F\x98\x80")); /* 😀 */
        CHECK(lyr_str_from_char(0x1F600)->len == 4);

        lyr_println(lyr_str_from_cstr("numeric ok"));
        return 0;
    }
    return result;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
