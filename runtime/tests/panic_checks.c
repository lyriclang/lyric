/* The checks emitted code performs (03 T2, 05 E8), one per run, chosen by the first argument.
 * Expected: "ok" computes every checked operation that must not panic — signed and unsigned, the
 * narrow types at their limits, the quotient that rounds toward zero, an unwrap of a live pointer —
 * prints "checks ok" and exits 0. Every other argument panics with exit 101 and this first line:
 *   add     panic [LYR-RT0002]: arithmetic overflow in '+'
 *   sub     panic [LYR-RT0002]: arithmetic overflow in '-'
 *   mul     panic [LYR-RT0002]: arithmetic overflow in '*'
 *   narrow  panic [LYR-RT0002]: arithmetic overflow in '+'      (int8: 127 + 1)
 *   under   panic [LYR-RT0002]: arithmetic overflow in '-'      (uint8: 0 - 1)
 *   div0    panic [LYR-RT0001]: division by zero
 *   divmin  panic [LYR-RT0002]: arithmetic overflow in '/'      (INT64_MIN / -1)
 *   rem0    panic [LYR-RT0001]: division by zero
 *   remmin  panic [LYR-RT0002]: arithmetic overflow in '%'      (INT64_MIN % -1)
 *   unwrap  panic [LYR-RT0004]: unwrapped a null value
 *   index   panic [LYR-RT0003]: index -1 out of bounds for length 4 */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

/* Volatile, so no operand is known at compile time and every check runs. */
static volatile int64_t big = INT64_MAX, small = INT64_MIN, zero = 0, minus_one = -1, two = 2;
static volatile int8_t i8_max = 127;
static volatile uint8_t u8_zero = 0;
static volatile uint64_t u64_max = UINT64_MAX;
static void *volatile nothing = NULL;

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "ok";
    int64_t result = 0;
    if (strcmp(which, "add") == 0) result = LYR_CHECKED_ADD(big, (int64_t)1);
    else if (strcmp(which, "sub") == 0) result = LYR_CHECKED_SUB(small, (int64_t)1);
    else if (strcmp(which, "mul") == 0) result = LYR_CHECKED_MUL(big, two);
    else if (strcmp(which, "narrow") == 0) result = LYR_CHECKED_ADD(i8_max, (int8_t)1);
    else if (strcmp(which, "under") == 0) result = LYR_CHECKED_SUB(u8_zero, (uint8_t)1);
    else if (strcmp(which, "div0") == 0) result = LYR_CHECKED_DIV(big, zero);
    else if (strcmp(which, "divmin") == 0) result = LYR_CHECKED_DIV(small, minus_one);
    else if (strcmp(which, "rem0") == 0) result = LYR_CHECKED_REM(big, zero);
    else if (strcmp(which, "remmin") == 0) result = LYR_CHECKED_REM(small, minus_one);
    else if (strcmp(which, "unwrap") == 0) result = (int64_t)(intptr_t)LYR_UNWRAP(nothing);
    else if (strcmp(which, "index") == 0) LYR_CHECK_INDEX(minus_one, 4);
    else {
        CHECK(LYR_CHECKED_ADD(big, minus_one) == INT64_MAX - 1);
        CHECK(LYR_CHECKED_SUB(small, minus_one) == INT64_MIN + 1);
        CHECK(LYR_CHECKED_MUL(small, (int64_t)1) == INT64_MIN);
        CHECK(LYR_CHECKED_ADD(i8_max, (int8_t)0) == 127);
        CHECK(LYR_CHECKED_SUB((int8_t)-100, (int8_t)28) == -128);
        CHECK(LYR_CHECKED_ADD(u8_zero, (uint8_t)255) == 255);
        CHECK(LYR_CHECKED_DIV((int64_t)-7, two) == -3);
        CHECK(LYR_CHECKED_DIV(small, two) == INT64_MIN / 2);
        CHECK(LYR_CHECKED_DIV(u64_max, (uint64_t)1) == UINT64_MAX);  /* unsigned: -1 is not special */
        CHECK(LYR_CHECKED_DIV((int8_t)-128, (int8_t)1) == -128);
        CHECK(LYR_CHECKED_REM((int64_t)-7, two) == -1);
        CHECK(LYR_CHECKED_REM(u64_max, (uint64_t)10) == 5);
        CHECK(LYR_CHECKED_REM(small, two) == 0);
        int local = 5;
        CHECK(*LYR_UNWRAP(&local) == 5);
        LYR_CHECK_INDEX(3, 4);
        lyr_println(lyr_str_from_cstr("checks ok"));
        return 0;
    }
    return result;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
