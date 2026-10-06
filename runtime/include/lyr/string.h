/* Strings in C, as far as design/v5/spec/01 L9 puts them there: layout, UTF-8 validation,
 * comparison, number -> text, and a float's text -> value. Search, split, case mapping and the
 * integer parser are Lyric (std.string, std.core). */
#ifndef LYR_STRING_H
#define LYR_STRING_H

#include <string.h>

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

/* The bytes from..to as a string of its own (10 S1): out of 0 ≤ from ≤ to ≤ len, or off a
 * character's boundary, a panic (LYR-RT0003). */
LyrStr *lyr_str_slice(const LyrStr *s, int64_t from, int64_t to);

/* A view of a string's bytes (10 S1, M8a S12): [low, high) of a string or of a view, checked as
 * a view's bounds are and on character boundaries — neither bound may fall on a byte that
 * continues a character; both panics are LYR-RT0003. What `mkslice.chars` is in C. */
LYR_NORETURN void lyr_panic_char_boundary(int64_t low, int64_t high);

static inline void lyr_check_char_range(const uint8_t *bytes, int64_t low, int64_t high, int64_t length) {
    LYR_CHECK_RANGE(low, high, length);
    if (LYR_UNLIKELY((low < length && (bytes[low] & 0xC0) == 0x80) || (high < length && (bytes[high] & 0xC0) == 0x80)))
        lyr_panic_char_boundary(low, high);
}

#define LYR_CHECK_CHAR_RANGE(bytes, low, high, length)                                              \
    lyr_check_char_range((const uint8_t *)(bytes), (int64_t)(low), (int64_t)(high), (int64_t)(length))

/* std.core's view natives (M8a S12), over a view's pointer and length — the emitter lays a view
 * out as { ptr, len }. Equal bytes; byte order, then the length (-1, 0, 1); the character that
 * begins at a byte, outside the bytes or on one that continues a character a panic (LYR-RT0003). */
bool lyr_bytes_equal(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen);
int lyr_bytes_compare(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen);
uint32_t lyr_char_at(const uint8_t *bytes, int64_t len, int64_t at);

#define LYR_VIEW_EQ(a, b) lyr_bytes_equal((a).ptr, (a).len, (b).ptr, (b).len)
#define LYR_VIEW_CMP(a, b) ((int64_t)lyr_bytes_compare((a).ptr, (a).len, (b).ptr, (b).len))
#define LYR_VIEW_STR(v) lyr_str_from_bytes((v).ptr, (v).len)
#define LYR_VIEW_CHAR(v, i) lyr_char_at((v).ptr, (v).len, (int64_t)(i))
#define LYR_VIEW_BYTES(v) (v)

/* A string of the first `count` bytes of a byte array (StringBuilder's): they are UTF-8, the
 * builder wrote them; `count` past the array is a panic. */
LyrStr *lyr_str_from_byte_array(const LyrArr *bytes, int64_t count);

/* Decimal text of an integer. */
LyrStr *lyr_str_from_int(int64_t value);
LyrStr *lyr_str_from_uint(uint64_t value);
/* "true" or "false". */
LyrStr *lyr_str_from_bool(bool value);
/* The shortest decimal text that reads back as the same double (10 B5 Z5), as Python and Swift
 * write it: "1.0", "0.1", "-0.0", "100.0", "1e+16", "1.5e-07", "inf", "-inf", "nan". */
LyrStr *lyr_str_from_float(double value);
/* A float32's own shortest text (the review's M8a-9): "0.1", where the double it widens to reads
 * "0.10000000149011612". */
LyrStr *lyr_str_from_float32(float value);
/* The character as a string of one code point, UTF-8 encoded. The value is a Unicode scalar
 * value by the type's invariant (numeric.h checks the one conversion that could break it). */
LyrStr *lyr_str_from_char(uint32_t value);
/* A float with a precision (08 Y7; spec 12 §2 rule 3): form 0 fixed, 1 with an exponent, 2 the
 * same in upper case, 3 fixed with the point moved two places right (a percentage, its `%` the
 * library's) — rounded half AWAY FROM ZERO on the exact binary value, by the runtime's own decimal
 * expansion, the same on every host (the review's M8a-8). `nan` (`NAN`), `inf` and `-inf` keep
 * their words. */
LyrStr *lyr_str_float_text(double value, int64_t precision, int64_t form);
/* A byte array's bulk copies, for std.core's StringBuilder: a string's bytes into `bytes` from
 * `at` on, and the first `count` bytes of one array into another — memcpy, the ranges checked as
 * an index is. */
void lyr_bytes_put(LyrArr *bytes, int64_t at, const uint8_t *from, int64_t count);
#define LYR_BYTES_PUT_VIEW(bytes, at, v) lyr_bytes_put((bytes), (int64_t)(at), (v).ptr, (v).len)
void lyr_bytes_copy(LyrArr *into, const LyrArr *from, int64_t count);

/* The float a text names, the nearest one (10 B5 Z6): a text whose form std.core checked — sign,
 * digits, '.', exponent, `inf`, `nan` — with the '_' between digits skipped; strtod and strtof round
 * correctly. A value beyond the range is an infinity, one below it a zero or a subnormal. */
double lyr_str_to_float64(const LyrStr *text);
float lyr_str_to_float32(const LyrStr *text);

/* A view's length and one byte of it (10 S1; M8a S12), the bytes std.core's string members read;
 * the index is checked as `xs[i]` is. Whether `p`'s bytes stand in `s` at byte `at` (false where
 * they would run past it). */
#define LYR_VIEW_LEN(v) ((int64_t)(v).len)
#define LYR_VIEW_BYTE(v, i)                                                                         \
    __extension__({                                                                                 \
        const int64_t lyr_i_ = (i);                                                                 \
        LYR_CHECK_INDEX(lyr_i_, (v).len);                                                           \
        (int64_t)(v).ptr[lyr_i_];                                                                   \
    })
static inline bool lyr_bytes_match_at(const uint8_t *s, int64_t slen, const uint8_t *p, int64_t plen, int64_t at) {
    return at >= 0 && at <= slen - plen && (plen == 0 || memcmp(s + at, p, (size_t)plen) == 0);
}
#define LYR_VIEW_MATCHES_AT(s, p, at) lyr_bytes_match_at((s).ptr, (s).len, (p).ptr, (p).len, (int64_t)(at))

#endif
