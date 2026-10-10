/* The Unicode Character Database's answers (lyr/unicode.h): lookups in the tables of
 * unicode_tables.h, which tooling/unicode/gen.py writes. A program that asks nothing of them links
 * none of them: the archive's member is pulled in by its first use. */
#include "lyr/unicode.h"

#include <stddef.h>

#include "unicode_tables.h"

int64_t lyr_unicode_category(uint32_t c) {
    if (c >= 0x110000) return 29; /* Cn: no character has a scalar value beyond 0x10FFFF */
    size_t block = CATEGORY_INDEX[c >> CATEGORY_SHIFT];
    return CATEGORY_BLOCKS[block << CATEGORY_SHIFT | (c & ((1u << CATEGORY_SHIFT) - 1))];
}

/* The mapping of `c` in a table sorted by the character mapped, or `c` itself. */
static uint32_t mapped(const LyrCaseMap *map, size_t n, uint32_t c) {
    size_t lo = 0, hi = n;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if (map[mid].from < c) lo = mid + 1;
        else hi = mid;
    }
    return lo < n && map[lo].from == c ? map[lo].to : c;
}

uint32_t lyr_unicode_upper(uint32_t c) {
    if (c < 0x80) return c >= 'a' && c <= 'z' ? c - 32 : c;
    return mapped(UPPER, sizeof UPPER / sizeof UPPER[0], c);
}

uint32_t lyr_unicode_lower(uint32_t c) {
    if (c < 0x80) return c >= 'A' && c <= 'Z' ? c + 32 : c;
    return mapped(LOWER, sizeof LOWER / sizeof LOWER[0], c);
}

bool lyr_unicode_white_space(uint32_t c) {
    for (size_t i = 0; i < sizeof WHITE_SPACE / sizeof WHITE_SPACE[0]; i++) {
        if (c < WHITE_SPACE[i][0]) return false;
        if (c <= WHITE_SPACE[i][1]) return true;
    }
    return false;
}
