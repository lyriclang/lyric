/* Strings: construction, UTF-8 validation, comparison, integer <-> text. */
#include "lyr/string.h"
#include "lyr/gc.h"
#include "lyr/panic.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

const LyrDesc lyr_desc_string = {
    .size = (uint32_t)offsetof(LyrStr, bytes),
    .flags = LYR_DESC_STRING,
    .name = "std.core.string",
};

LyrStr *lyr_str_from_bytes(const void *bytes, int64_t len) {
    LyrStr *string = lyr_alloc_string(len);
    if (len > 0) memcpy(string->bytes, bytes, (size_t)len);
    return string;
}

LyrStr *lyr_str_from_cstr(const char *text) {
    return lyr_str_from_bytes(text, (int64_t)strlen(text));
}

/* Unicode 3.9 table D3-7: the well-formed byte sequences. */
bool lyr_utf8_valid(const void *bytes, int64_t len, int64_t *error_offset) {
    const unsigned char *s = bytes;
    int64_t i = 0;
    while (i < len) {
        unsigned char b = s[i];
        int64_t start = i;
        if (b < 0x80) { i++; continue; }

        int extra;
        unsigned char lo = 0x80, hi = 0xBF;  /* bounds of the second byte */
        if (b >= 0xC2 && b <= 0xDF) { extra = 1; }
        else if (b == 0xE0) { extra = 2; lo = 0xA0; }
        else if ((b >= 0xE1 && b <= 0xEC) || b == 0xEE || b == 0xEF) { extra = 2; }
        else if (b == 0xED) { extra = 2; hi = 0x9F; }              /* no surrogates */
        else if (b == 0xF0) { extra = 3; lo = 0x90; }
        else if (b >= 0xF1 && b <= 0xF3) { extra = 3; }
        else if (b == 0xF4) { extra = 3; hi = 0x8F; }              /* nothing above U+10FFFF */
        else { if (error_offset) *error_offset = start; return false; }

        if (len - i - 1 < extra) { if (error_offset) *error_offset = start; return false; }
        unsigned char second = s[i + 1];
        if (second < lo || second > hi) { if (error_offset) *error_offset = start; return false; }
        for (int k = 2; k <= extra; k++) {
            if ((s[i + k] & 0xC0) != 0x80) { if (error_offset) *error_offset = start; return false; }
        }
        i += extra + 1;
    }
    return true;
}

LyrStr *lyr_str_concat(const LyrStr *a, const LyrStr *b) {
    if (LYR_UNLIKELY(a->len > INT64_MAX - b->len)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "concatenating strings of %lld and %lld bytes overflows",
                  (long long)a->len, (long long)b->len);
    }
    LyrStr *result = lyr_alloc_string(a->len + b->len);
    memcpy(result->bytes, a->bytes, (size_t)a->len);
    memcpy(result->bytes + a->len, b->bytes, (size_t)b->len);
    return result;
}

bool lyr_str_eq(const LyrStr *a, const LyrStr *b) {
    return a == b || (a->len == b->len && memcmp(a->bytes, b->bytes, (size_t)a->len) == 0);
}

int lyr_str_cmp(const LyrStr *a, const LyrStr *b) {
    int64_t common = a->len < b->len ? a->len : b->len;
    int order = memcmp(a->bytes, b->bytes, (size_t)common);
    if (order != 0) return order < 0 ? -1 : 1;
    return a->len < b->len ? -1 : a->len > b->len ? 1 : 0;
}

LyrStr *lyr_str_from_int(int64_t value) {
    char digits[24];
    int n = 0;
    /* Work in the negative range so INT64_MIN needs no special case. */
    int64_t v = value < 0 ? value : -value;
    do {
        digits[n++] = (char)('0' - (v % 10));
        v /= 10;
    } while (v != 0);
    if (value < 0) digits[n++] = '-';

    LyrStr *text = lyr_alloc_string(n);
    for (int i = 0; i < n; i++) text->bytes[i] = digits[n - 1 - i];
    return text;
}

LyrStr *lyr_str_from_uint(uint64_t value) {
    char digits[24];
    int n = 0;
    do {
        digits[n++] = (char)('0' + value % 10);
        value /= 10;
    } while (value != 0);
    LyrStr *text = lyr_alloc_string(n);
    for (int i = 0; i < n; i++) text->bytes[i] = digits[n - 1 - i];
    return text;
}

LyrStr *lyr_str_from_bool(bool value) {
    return lyr_str_from_cstr(value ? "true" : "false");
}

LyrStr *lyr_str_from_char(uint32_t value) {
    unsigned char bytes[4];
    int n;
    if (value < 0x80) { bytes[0] = (unsigned char)value; n = 1; }
    else if (value < 0x800) {
        bytes[0] = (unsigned char)(0xC0 | (value >> 6));
        bytes[1] = (unsigned char)(0x80 | (value & 0x3F));
        n = 2;
    } else if (value < 0x10000) {
        bytes[0] = (unsigned char)(0xE0 | (value >> 12));
        bytes[1] = (unsigned char)(0x80 | ((value >> 6) & 0x3F));
        bytes[2] = (unsigned char)(0x80 | (value & 0x3F));
        n = 3;
    } else {
        bytes[0] = (unsigned char)(0xF0 | (value >> 18));
        bytes[1] = (unsigned char)(0x80 | ((value >> 12) & 0x3F));
        bytes[2] = (unsigned char)(0x80 | ((value >> 6) & 0x3F));
        bytes[3] = (unsigned char)(0x80 | (value & 0x3F));
        n = 4;
    }
    return lyr_str_from_bytes(bytes, n);
}

/* Shortest round trip by trial: the first number of significant digits whose `%g` text reads
 * back as the same double, from one digit up to the 17 that always suffice. Most values stop
 * early — an integer or a short decimal at its own length — and the worst case is 17 trials of
 * snprintf and strtod, both correctly rounded on every libc this toolchain links. Ryu answers
 * the same question without the trials and is the door if this shows up in a profile. */
LyrStr *lyr_str_from_float(double value) {
    if (value != value) return lyr_str_from_cstr("NaN");
    if (value == (double)INFINITY) return lyr_str_from_cstr("inf");
    if (value == -(double)INFINITY) return lyr_str_from_cstr("-inf");
    char text[32];
    for (int precision = 1; precision <= 17; precision++) {
        int n = snprintf(text, sizeof text, "%.*g", precision, value);
        if (n <= 0 || (size_t)n >= sizeof text) break;
        if (precision == 17 || strtod(text, NULL) == value) return lyr_str_from_bytes(text, n);
    }
    return lyr_str_from_cstr("?");
}

static int digit_value(unsigned char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'z') return c - 'a' + 10;
    if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
    return 99;
}

LyrParseStatus lyr_str_to_int(const LyrStr *text, int radix, int64_t *out, int64_t *error_offset) {
    if (radix < 2 || radix > 36) lyr_panic(LYR_RT_ARGUMENT, "radix %d is outside 2..36", radix);

    const unsigned char *s = (const unsigned char *)text->bytes;
    int64_t len = text->len, i = 0;
    if (len == 0) { if (error_offset) *error_offset = 0; return LYR_PARSE_EMPTY; }

    int negative = 0;
    if (s[0] == '+' || s[0] == '-') { negative = s[0] == '-'; i = 1; }
    if (i == len) { if (error_offset) *error_offset = i; return LYR_PARSE_INVALID; }

    /* Accumulate negatively: the negative range holds one more value than the positive one. */
    int64_t acc = 0;
    int digits = 0, last_was_digit = 0;
    for (; i < len; i++) {
        unsigned char c = s[i];
        if (c == '_') {
            if (!last_was_digit || i + 1 == len) { if (error_offset) *error_offset = i; return LYR_PARSE_INVALID; }
            last_was_digit = 0;
            continue;
        }
        int d = digit_value(c);
        if (d >= radix) { if (error_offset) *error_offset = i; return LYR_PARSE_INVALID; }
        if (acc < (INT64_MIN + d) / radix) { if (error_offset) *error_offset = i; return LYR_PARSE_OVERFLOW; }
        acc = acc * radix - d;
        digits++;
        last_was_digit = 1;
    }
    if (digits == 0) { if (error_offset) *error_offset = i; return LYR_PARSE_INVALID; }
    if (!negative) {
        if (acc == INT64_MIN) { if (error_offset) *error_offset = 0; return LYR_PARSE_OVERFLOW; }
        acc = -acc;
    }
    *out = acc;
    return LYR_PARSE_OK;
}
