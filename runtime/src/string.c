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
    lyr_check_char_range((const uint8_t *)s->bytes, from, to, s->len);
    return lyr_str_from_bytes(s->bytes + from, to - from);
}

void lyr_panic_char_boundary(int64_t low, int64_t high) {
    lyr_panic(LYR_RT_INDEX, "byte range %lld..%lld does not fall on character boundaries", (long long)low, (long long)high);
}

bool lyr_bytes_equal(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen) {
    return alen == blen && (alen == 0 || a == b || memcmp(a, b, (size_t)alen) == 0);
}

int lyr_bytes_compare(const uint8_t *a, int64_t alen, const uint8_t *b, int64_t blen) {
    int64_t n = alen < blen ? alen : blen;
    int c = n > 0 ? memcmp(a, b, (size_t)n) : 0;
    if (c != 0) return c < 0 ? -1 : 1;
    return alen < blen ? -1 : alen > blen ? 1 : 0;
}

/* The bytes are a string's, well-formed UTF-8: what begins at a first byte is a whole character. */
uint32_t lyr_char_at(const uint8_t *bytes, int64_t len, int64_t at) {
    LYR_CHECK_INDEX(at, len);
    uint8_t b0 = bytes[at];
    if (b0 < 0x80) return b0;
    if ((b0 & 0xC0) == 0x80)
        lyr_panic(LYR_RT_INDEX, "byte %lld does not begin a character", (long long)at);
    if (b0 < 0xE0) return ((uint32_t)(b0 & 0x1F) << 6) | (bytes[at + 1] & 0x3F);
    if (b0 < 0xF0)
        return ((uint32_t)(b0 & 0x0F) << 12) | ((uint32_t)(bytes[at + 1] & 0x3F) << 6) | (bytes[at + 2] & 0x3F);
    return ((uint32_t)(b0 & 0x07) << 18) | ((uint32_t)(bytes[at + 1] & 0x3F) << 12)
           | ((uint32_t)(bytes[at + 2] & 0x3F) << 6) | (bytes[at + 3] & 0x3F);
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

/* --- a float's text ------------------------------------------------------------------------------
 * The SHORTEST digits that read back as the same value (10 B5 Z5), laid out as Python's repr and
 * Swift write them — plain for a decimal exponent from -4 up to below 16, with `.0` on an integral
 * value (`1.0`, `-0.0`, `100.0`), an exponent beyond (`1e+16`, `1.5e-07`) — and `nan`, `inf`,
 * `-inf`. The digits by trial: the first precision whose `%e` text reads back, from one digit up to
 * the 17 that always suffice for a double, the 9 for a float32; snprintf, strtod and strtof are
 * correctly rounded on every libc this toolchain links. Ryu answers the same without the trials
 * and is the door if this shows up in a profile. */

/* `sci` is [-]d[.ddd]e(+|-)xx from `%e`; laid out as described. */
static LyrStr *laid_out(const char *sci) {
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

LyrStr *lyr_str_from_float(double value) {
    if (value != value) return lyr_str_from_cstr("nan");
    if (value == (double)INFINITY) return lyr_str_from_cstr("inf");
    if (value == -(double)INFINITY) return lyr_str_from_cstr("-inf");
    char sci[40];
    for (int precision = 0; precision <= 16; precision++) {
        snprintf(sci, sizeof sci, "%.*e", precision, value);
        if (strtod(sci, NULL) == value) break;
    }
    return laid_out(sci);
}

/* A float32's own shortest text (the review's M8a-9): `0.1`, not the `0.10000000149011612` of
 * the double it widens to. Nine significant digits always suffice. */
LyrStr *lyr_str_from_float32(float value) {
    if (value != value) return lyr_str_from_cstr("nan");
    if (value == INFINITY) return lyr_str_from_cstr("inf");
    if (value == -INFINITY) return lyr_str_from_cstr("-inf");
    char sci[40];
    for (int precision = 0; precision <= 8; precision++) {
        snprintf(sci, sizeof sci, "%.*e", precision, (double)value);
        if (strtof(sci, NULL) == value) break;
    }
    return laid_out(sci);
}

/* --- a float's exact decimal digits ----------------------------------------------------------------
 * A double is m × 2^e exactly, so its decimal expansion is finite: m × 2^e for e ≥ 0, else
 * m × 5^-e over 10^-e. Either is a big integer of at most 767 decimal digits (m × 5^1074), built
 * here in limbs of 10^9. The format language rounds on THESE digits, half away from zero — the
 * language's one rounding rule, `round()`'s (the review's M8a-8) — where snprintf rounds half to
 * even: `{0.125:.2f}` is `0.13`, `{2.5:.0f}` is `3`, and `{2.675:.2f}` stays `2.67`, since the
 * double nearest 2.675 lies below the half. The same on every host: nothing of the libc decides. */
enum { BIG_LIMBS = 96 };            /* 96 × 9 = 864 digits ≥ 767 */
enum { EXACT_DIGITS = BIG_LIMBS * 9 };

typedef struct {
    uint32_t limb[BIG_LIMBS];       /* base 10^9, least significant first */
    int count;
} BigDec;

static void big_mul_small(BigDec *b, uint32_t factor) {
    uint64_t carry = 0;
    for (int i = 0; i < b->count; i++) {
        uint64_t cur = (uint64_t)b->limb[i] * factor + carry;
        b->limb[i] = (uint32_t)(cur % 1000000000u);
        carry = cur / 1000000000u;
    }
    while (carry != 0) {
        b->limb[b->count++] = (uint32_t)(carry % 1000000000u);
        carry /= 1000000000u;
    }
}

/* The decimal digits of `b`, most significant first; how many. */
static int big_digits(const BigDec *b, char *out) {
    int n = 0;
    n += snprintf(out, 11, "%u", b->limb[b->count - 1]);
    for (int i = b->count - 2; i >= 0; i--) n += snprintf(out + n, 10, "%09u", b->limb[i]);
    return n;
}

/* The exact value: `digits` (no leading zero but for the value zero), and the point `point` digits
 * in — before the first digit when it is 0 or less, beyond the last when it exceeds `count`. */
typedef struct {
    char digits[EXACT_DIGITS + 1];
    int count;
    int point;
    bool negative;
} Exact;

static void exact_of(double value, Exact *x) {
    uint64_t bits;
    memcpy(&bits, &value, sizeof bits);
    x->negative = (bits >> 63) != 0;
    int biased = (int)((bits >> 52) & 0x7FF);
    uint64_t m = bits & ((UINT64_C(1) << 52) - 1);
    int e;
    if (biased == 0) {
        e = -1074;                      /* a subnormal */
    } else {
        m |= UINT64_C(1) << 52;
        e = biased - 1075;
    }
    if (m == 0) {
        x->digits[0] = '0';
        x->count = 1;
        x->point = 1;
        return;
    }
    BigDec b = { .count = 0 };
    while (m != 0) {
        b.limb[b.count++] = (uint32_t)(m % 1000000000u);
        m /= 1000000000u;
    }
    int scale = 0;                      /* the digits after the point */
    if (e >= 0) {
        for (; e >= 29; e -= 29) big_mul_small(&b, UINT32_C(1) << 29);
        if (e > 0) big_mul_small(&b, UINT32_C(1) << e);
    } else {
        scale = -e;
        int k = scale;
        for (; k >= 13; k -= 13) big_mul_small(&b, 1220703125u);   /* 5^13 */
        uint32_t rest = 1;
        for (; k > 0; k--) rest *= 5;
        if (rest > 1) big_mul_small(&b, rest);
    }
    x->count = big_digits(&b, x->digits);
    x->point = x->count - scale;
}

/* The digit at `i` of an exact value, '0' beyond either end. */
static char digit_at(const Exact *x, int i) { return i >= 0 && i < x->count ? x->digits[i] : '0'; }

/* `keep` digits from the first, rounded half away from zero on the next: into `out`, whose length
 * comes back — `keep` or, on a carry out of the first digit, `keep + 1` with `*carried` set. The
 * first digit kept keeps its weight; `keep` may exceed the digits there are (zeros), or be 0. */
static int rounded_digits(const Exact *x, int keep, char *out, bool *carried) {
    *carried = false;
    for (int i = 0; i < keep; i++) out[i] = digit_at(x, i);
    if (digit_at(x, keep) >= '5') {
        int i = keep - 1;
        for (; i >= 0; i--) {
            if (out[i] == '9') {
                out[i] = '0';
            } else {
                out[i]++;
                break;
            }
        }
        if (i < 0) {
            memmove(out + 1, out, (size_t)keep);
            out[0] = '1';
            *carried = true;
            return keep + 1;
        }
    }
    return keep;
}

/* The text of a float under a precision (08 Y7; spec 12 §2 rule 3). `form` 0: fixed, `precision`
 * digits after the point (none with 0); 1 and 2: with an exponent, `precision` digits after the
 * first, `e` or `E`, the exponent signed and two digits at least; 3: fixed with the point moved two
 * places right — a percentage, whose `%` the library appends. A NaN is `nan` (`NAN` under 2)
 * whatever its sign; an infinity `inf`, `-inf` (`INF` under 2). The sign stays on a value that
 * rounds to zero, as snprintf keeps it (`-0.00`). */
LyrStr *lyr_str_float_text(double value, int64_t precision, int64_t form) {
    if (value != value) return lyr_str_from_cstr(form == 2 ? "NAN" : "nan");
    if (value == (double)INFINITY) return lyr_str_from_cstr(form == 2 ? "INF" : "inf");
    if (value == -(double)INFINITY) return lyr_str_from_cstr(form == 2 ? "-INF" : "-inf");
    int digits = precision < 0 ? 0 : precision > 100000 ? 100000 : (int)precision;
    Exact x;
    exact_of(value, &x);

    /* room: the sign, the digits kept and a carry, the point, leading zeros below the point, the
     * exponent — on the stack while it fits */
    size_t room = (size_t)(x.count > 0 ? x.count : 1) + (size_t)digits + 32;
    if (x.point > 0) room += (size_t)x.point;
    if (x.point < 0) room += (size_t)-x.point;
    char small[256], kept_small[256];
    char *text = room <= sizeof small ? small : malloc(room);
    char *kept = room <= sizeof kept_small ? kept_small : malloc(room);
    if (text == NULL || kept == NULL) lyr_panic_message(lyr_str_from_cstr("out of memory for a float's text"));
    int n = 0;
    if (x.negative) text[n++] = '-';

    if (form == 1 || form == 2) {
        bool zero = x.count == 1 && x.digits[0] == '0';
        int exponent = zero ? 0 : x.point - 1;
        bool carried;
        int count = rounded_digits(&x, digits + 1, kept, &carried);
        if (carried) exponent++;        /* 9.99 → 10.0: the first digit is the 1, the rest zeros */
        text[n++] = kept[0];
        if (digits > 0) {
            text[n++] = '.';
            memcpy(text + n, kept + 1, (size_t)digits);
            n += digits;
        }
        (void)count;
        n += snprintf(text + n, 16, "%c%c%02d", form == 2 ? 'E' : 'e', exponent < 0 ? '-' : '+',
                      exponent < 0 ? -exponent : exponent);
    } else {
        int point = x.point + (form == 3 ? 2 : 0);
        int keep = point + digits;      /* the digits down to the last place kept */
        int count = 0;
        bool carried = false;
        if (keep > 0) {
            count = rounded_digits(&x, keep, kept, &carried);
            if (carried) point++;
        } else if (keep == 0 && digit_at(&x, 0) >= '5') {
            kept[0] = '1';              /* everything rounds up into the last place */
            count = 1;
            point++;
        }
        /* the integer part: the digits before the point, or a zero */
        if (point <= 0) {
            text[n++] = '0';
        } else {
            for (int i = 0; i < point; i++) text[n++] = i < count ? kept[i] : '0';
        }
        if (digits > 0) {
            text[n++] = '.';
            for (int i = 0; i < digits; i++) {
                int at = point + i;     /* the digit's index in `kept` */
                text[n++] = at >= 0 && at < count ? kept[at] : '0';
            }
        }
    }
    LyrStr *result = lyr_str_from_bytes(text, n);
    if (kept != kept_small) free(kept);
    if (text != small) free(text);
    return result;
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
