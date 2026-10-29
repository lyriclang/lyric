#include <stdint.h>

/* The text's length in UTF-16 units, counted from its UTF-8: four bytes are two units. */
int64_t text_units(const char *text) {
    int64_t units = 0;
    for (const unsigned char *p = (const unsigned char *)text; *p != 0; p++) {
        if ((*p & 0xC0) == 0x80) continue;
        units += (*p >= 0xF0) ? 2 : 1;
    }
    return units;
}

const char *text_counter(void) { return "portable C"; }
