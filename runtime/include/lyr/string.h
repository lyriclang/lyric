/* Strings in C, as far as design/v5/spec/01 L9 puts them there: layout, UTF-8 validation,
 * comparison, and integer <-> text. Search, split and case mapping are Lyric (std.string). */
#ifndef LYR_STRING_H
#define LYR_STRING_H

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

typedef enum LyrParseStatus {
    LYR_PARSE_OK = 0,
    LYR_PARSE_EMPTY,
    LYR_PARSE_INVALID,
    LYR_PARSE_OVERFLOW,
} LyrParseStatus;

/* ASCII digits in `radix` (2..36), an optional leading '+' or '-', '_' between digits; no
 * whitespace (design/v5/spec/10 Z6). `*error_offset` names the first byte that is not accepted. */
LyrParseStatus lyr_str_to_int(const LyrStr *text, int radix, int64_t *out, int64_t *error_offset);

#endif
