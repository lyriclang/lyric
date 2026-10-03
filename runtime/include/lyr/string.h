/* Strings in C, as far as design/v5/spec/01 L9 puts them there: layout, UTF-8 validation,
 * comparison, number -> text, and a float's text -> value. Search, split, case mapping and the
 * integer parser are Lyric (std.string, std.core). */
#ifndef LYR_STRING_H
#define LYR_STRING_H

#include "lyr/panic.h"
#include "lyr/types.h"

/* A copy of `len` bytes as a string. The bytes are taken as they are; callers that got them from
 * outside validate first (lyr_utf8_valid). */
LyrStr *lyr_str_from_bytes(const void *bytes, int64_t len);

/* A copy of a NUL-terminated C string. */
LyrStr *lyr_str_from_cstr(const char *text);

/* Whether `len` bytes are well-formed UTF-8 (no overlong forms, no surrogates, nothing above
 * U+10FFFF). On failure `*error_offset` (if not NULL) is the offset of the first bad byte. */
bool lyr_utf8_valid(const void *bytes, int64_t len, int64_t *error_offset);

LyrStr *lyr_str_concat(const LyrStr *a, const LyrStr *b);

bool lyr_str_eq(const LyrStr *a, const LyrStr *b);

/* <0, 0, >0 in byte order, which is code point order for UTF-8. */
int lyr_str_cmp(const LyrStr *a, const LyrStr *b);

/* Decimal text of an integer. */
LyrStr *lyr_str_from_int(int64_t value);
LyrStr *lyr_str_from_uint(uint64_t value);
/* "true" or "false". */
LyrStr *lyr_str_from_bool(bool value);
/* The shortest decimal text that reads back as the same double: "1", "0.1", "1e+21", "-0",
 * "inf", "-inf", "NaN". An integral value has no ".0", as C#, Go and Rust print it. */
LyrStr *lyr_str_from_float(double value);
/* The character as a string of one code point, UTF-8 encoded. The value is a Unicode scalar
 * value by the type's invariant (numeric.h checks the one conversion that could break it). */
LyrStr *lyr_str_from_char(uint32_t value);

/* The float a text names, the nearest one (10 B5 Z6): a text whose form std.core checked — sign,
 * digits, '.', exponent, `inf`, `nan` — with the '_' between digits skipped; strtod and strtof round
 * correctly. A value beyond the range is an infinity, one below it a zero or a subnormal. */
double lyr_str_to_float64(const LyrStr *text);
float lyr_str_to_float32(const LyrStr *text);

/* A string's length and one byte of it (10 S1), for std.core until `StringView` gives the library
 * its bytes (M8a S8); the index is checked as `xs[i]` is. */
#define LYR_STR_LEN(s) ((int64_t)(s)->len)
#define LYR_STR_BYTE(s, i)                                                                          \
    __extension__({                                                                                 \
        const LyrStr *lyr_s_ = (s);                                                                 \
        const int64_t lyr_i_ = (i);                                                                 \
        LYR_CHECK_INDEX(lyr_i_, lyr_s_->len);                                                       \
        (int64_t)(unsigned char)lyr_s_->bytes[lyr_i_];                                              \
    })

#endif
