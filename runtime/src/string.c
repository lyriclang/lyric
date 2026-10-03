/* Strings: construction, UTF-8 validation, comparison, number -> text, a float's text -> value. */
#include "lyr/string.h"
#include "lyr/gc.h"
#include "lyr/panic.h"

#include <limits.h>
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

LyrStr *lyr_str_slice(const LyrStr *s, int64_t from, int64_t to) {
    if (from < 0 || to < from || to > s->len) lyr_panic_range(from, to, s->len);
    if ((from < s->len && ((unsigned char)s->bytes[from] & 0xC0) == 0x80)
        || (to < s->len && ((unsigned char)s->bytes[to] & 0xC0) == 0x80)) {
        lyr_panic(LYR_RT_INDEX, "byte range %lld..%lld does not fall on character boundaries",
                  (long long)from, (long long)to);
    }
    return lyr_str_from_bytes(s->bytes + from, to - from);
}

LyrStr *lyr_str_from_byte_array(const LyrArr *bytes, int64_t count) {
    if (count < 0 || count > bytes->len) lyr_panic_range(0, count, bytes->len);
    return lyr_str_from_bytes(bytes->data, count);
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

/* A float's text (10 B5 Z5): the SHORTEST digits that read back as the same double, laid out as
 * Python's repr and Swift write them — plain for a decimal exponent from -4 up to below 16, with
 * `.0` on an integral value (`1.0`, `-0.0`, `100.0`), an exponent beyond (`1e+16`, `1.5e-07`) —
 * and `nan`, `inf`, `-inf`. The digits by trial: the first precision whose `%e` text reads back,
 * from one digit up to the 17 that always suffice; snprintf and strtod are correctly rounded on
 * every libc this toolchain links. Ryu answers the same without the trials and is the door if this
 * shows up in a profile. */
LyrStr *lyr_str_from_float(double value) {
    if (value != value) return lyr_str_from_cstr("nan");
    if (value == (double)INFINITY) return lyr_str_from_cstr("inf");
    if (value == -(double)INFINITY) return lyr_str_from_cstr("-inf");

    char sci[40];
    for (int precision = 0; precision <= 16; precision++) {
        snprintf(sci, sizeof sci, "%.*e", precision, value);
        if (strtod(sci, NULL) == value) break;
    }
    /* sci is [-]d[.ddd]e(+|-)xx: the sign, the digits without the point, the exponent */
    const char *p = sci;
    bool negative = *p == '-';
    if (negative) p++;
    char digits[24];
    int count = 0;
    for (; *p != 'e'; p++)
        if (*p != '.') digits[count++] = *p;
    while (count > 1 && digits[count - 1] == '0') count--; /* "1.50e+00" would read 1.5 as well */
    int exponent = atoi(p + 1);

    char text[48];
    int n = 0;
    if (negative) text[n++] = '-';
    if (exponent < -4 || exponent >= 16) {
        text[n++] = digits[0];
        if (count > 1) {
            text[n++] = '.';
            for (int i = 1; i < count; i++) text[n++] = digits[i];
        }
        n += snprintf(text + n, sizeof text - (size_t)n, "e%c%02d", exponent < 0 ? '-' : '+',
                      exponent < 0 ? -exponent : exponent);
    } else if (exponent < 0) {
        text[n++] = '0';
        text[n++] = '.';
        for (int i = 0; i < -exponent - 1; i++) text[n++] = '0';
        for (int i = 0; i < count; i++) text[n++] = digits[i];
    } else {
        for (int i = 0; i <= exponent; i++) text[n++] = i < count ? digits[i] : '0';
        text[n++] = '.';
        if (count > exponent + 1)
            for (int i = exponent + 1; i < count; i++) text[n++] = digits[i];
        else
            text[n++] = '0';
    }
    return lyr_str_from_bytes(text, n);
}

/* A float with a precision (08 Y7): snprintf's `%.*f`, `%.*e`, `%.*E`, correctly rounded on every
 * libc this toolchain links (as lyr_str_from_float relies on). A NaN is `nan` whatever its sign
 * bit, as the shortest text writes it. The text on the stack while it fits, on the heap beyond. */
LyrStr *lyr_str_float_text(double value, int64_t precision, int64_t form) {
    if (value != value) return lyr_str_from_cstr(form == 2 ? "NAN" : "nan");
    const char *pattern = form == 0 ? "%.*f" : form == 1 ? "%.*e" : "%.*E";
    int digits = precision < 0 ? 0 : precision > INT_MAX ? INT_MAX : (int)precision;
    char small[128];
    int n = snprintf(small, sizeof small, pattern, digits, value);
    if (n < 0) lyr_panic_message(lyr_str_from_cstr("a float's text failed"));
    if ((size_t)n < sizeof small) return lyr_str_from_bytes(small, n);
    char *large = malloc((size_t)n + 1);
    if (large == NULL) lyr_panic_message(lyr_str_from_cstr("out of memory for a float's text"));
    snprintf(large, (size_t)n + 1, pattern, digits, value);
    LyrStr *text = lyr_str_from_bytes(large, n);
    free(large);
    return text;
}

void lyr_bytes_put_str(LyrArr *bytes, int64_t at, const LyrStr *s) {
    if (at < 0 || at > bytes->len - s->len) lyr_panic_range(at, at + s->len, bytes->len);
    memcpy(bytes->data + at, s->bytes, (size_t)s->len);
}

void lyr_bytes_copy(LyrArr *into, const LyrArr *from, int64_t count) {
    int64_t room = into->len < from->len ? into->len : from->len;
    if (count < 0 || count > room) lyr_panic_range(0, count, room);
    memmove(into->data, from->data, (size_t)count);
}

/* The text strtod reads: the string's own bytes (NUL-terminated, 11 X3) when it has no '_', else
 * a copy without them — on the stack while it fits, on the heap beyond. */
static const char *float_text(const LyrStr *text, char *buffer, size_t size, char **owned) {
    *owned = NULL;
    if (memchr(text->bytes, '_', (size_t)text->len) == NULL) return text->bytes;
    char *out = buffer;
    if ((size_t)text->len >= size) {
        out = *owned = malloc((size_t)text->len + 1);
        if (out == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "no memory for a float text of %lld bytes", (long long)text->len);
    }
    size_t n = 0;
    for (int64_t i = 0; i < text->len; i++)
        if (text->bytes[i] != '_') out[n++] = text->bytes[i];
    out[n] = '\0';
    return out;
}

double lyr_str_to_float64(const LyrStr *text) {
    char buffer[64], *owned;
    double value = strtod(float_text(text, buffer, sizeof buffer, &owned), NULL);
    free(owned);
    return value;
}

float lyr_str_to_float32(const LyrStr *text) {
    char buffer[64], *owned;
    float value = strtof(float_text(text, buffer, sizeof buffer, &owned), NULL);
    free(owned);
    return value;
}
