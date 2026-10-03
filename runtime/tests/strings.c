/* Strings (01 V9, 10 S1/Z6, 11 X3).
 * Expected: layout with byte length and a terminating NUL; a static literal works like a heap one;
 * concat, equality and byte-order comparison; the UTF-8 table accepts exactly the well-formed
 * sequences and names the first bad offset; integers round-trip through text including INT64_MIN
 * and INT64_MAX; a float's text reads back to the nearest value, '_' skipped, beyond the range an
 * infinity. Prints "strings ok". */
#include "lyr/lyr.h"
#include "check.h"

#include <math.h>
#include <string.h>

static const LyrStaticStr(sizeof("grüße")) greeting = LYR_STR_INIT("grüße");

static LyrStr *s(const char *text) { return lyr_str_from_cstr(text); }

static int valid(const char *bytes, int64_t len, int64_t *at) { return lyr_utf8_valid(bytes, len, at); }

static int64_t program(void) {
    /* layout */
    LyrStr *hello = s("hello");
    CHECK(hello->len == 5);
    CHECK(hello->bytes[5] == '\0');
    CHECK(strcmp(hello->bytes, "hello") == 0);
    LyrStr *g = (LyrStr *)&greeting;
    CHECK(g->len == 7);                         /* ü and ß are two bytes each */
    CHECK(g->header.desc == &lyr_desc_string);
    CHECK(lyr_str_eq(g, s("grüße")));

    /* concat, equality, order */
    LyrStr *both = lyr_str_concat(hello, s(", world"));
    CHECK(both->len == 12 && strcmp(both->bytes, "hello, world") == 0);
    CHECK(lyr_str_concat(s(""), s(""))->len == 0);
    CHECK(lyr_str_eq(s("a"), s("a")) && !lyr_str_eq(s("a"), s("b")) && !lyr_str_eq(s("a"), s("ab")));
    CHECK(lyr_str_cmp(s("a"), s("b")) < 0 && lyr_str_cmp(s("b"), s("a")) > 0);
    CHECK(lyr_str_cmp(s("ab"), s("a")) > 0 && lyr_str_cmp(s("a"), s("a")) == 0);
    CHECK(lyr_str_cmp(s("z"), s("ä")) < 0);     /* byte order = code point order */

    /* UTF-8 */
    int64_t at = -1;
    CHECK(valid("", 0, &at));
    CHECK(valid("ascii", 5, &at));
    CHECK(valid("\xC3\xA4", 2, &at));            /* ä */
    CHECK(valid("\xE2\x82\xAC", 3, &at));        /* € */
    CHECK(valid("\xF0\x9F\x98\x80", 4, &at));    /* 😀 */
    CHECK(valid("\xF4\x8F\xBF\xBF", 4, &at));    /* U+10FFFF */
    CHECK(!valid("\xC0\x80", 2, &at) && at == 0);             /* overlong NUL */
    CHECK(!valid("ab\xE0\x80\x80", 5, &at) && at == 2);       /* overlong 3-byte */
    CHECK(!valid("\xED\xA0\x80", 3, &at) && at == 0);         /* surrogate U+D800 */
    CHECK(!valid("\xF4\x90\x80\x80", 4, &at) && at == 0);     /* above U+10FFFF */
    CHECK(!valid("x\xE2\x82", 3, &at) && at == 1);            /* truncated */
    CHECK(!valid("\x80", 1, &at) && at == 0);                 /* lone continuation */
    CHECK(!valid("\xC3\x28", 2, &at) && at == 0);             /* bad continuation */
    CHECK(!valid("\xFF", 1, &at) && at == 0);

    /* integers to text */
    CHECK(strcmp(lyr_str_from_int(0)->bytes, "0") == 0);
    CHECK(strcmp(lyr_str_from_int(-42)->bytes, "-42") == 0);
    CHECK(strcmp(lyr_str_from_int(INT64_MAX)->bytes, "9223372036854775807") == 0);
    CHECK(strcmp(lyr_str_from_int(INT64_MIN)->bytes, "-9223372036854775808") == 0);
    CHECK(strcmp(lyr_str_from_uint(0)->bytes, "0") == 0);
    CHECK(strcmp(lyr_str_from_uint(UINT64_MAX)->bytes, "18446744073709551615") == 0);
    CHECK(lyr_str_from_uint(UINT64_MAX)->len == 20);
    CHECK(strcmp(lyr_str_from_bool(true)->bytes, "true") == 0 && strcmp(lyr_str_from_bool(false)->bytes, "false") == 0);

    /* a float's text to its value: the form is std.core's to check, the value C's */
    CHECK(lyr_str_to_float64(s("1.5")) == 1.5);
    CHECK(lyr_str_to_float64(s("-0.1")) == -0.1);
    CHECK(lyr_str_to_float64(s("1_000.25")) == 1000.25);
    CHECK(lyr_str_to_float64(s("2.5e-3")) == 0.0025);
    CHECK(lyr_str_to_float64(s("1e400")) == (double)INFINITY);
    CHECK(lyr_str_to_float64(s("-inf")) == -(double)INFINITY);
    double nan = lyr_str_to_float64(s("nan"));
    CHECK(nan != nan);
    CHECK(lyr_str_to_float32(s("0.1")) == 0.1f);
    CHECK(lyr_str_to_float32(s("1e39")) == (float)INFINITY);
    /* a copy beyond the stack buffer: 70 digits with '_' between pairs */
    CHECK(lyr_str_to_float64(s("10_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00_00")) == 1e69);

    lyr_println(s("strings ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
