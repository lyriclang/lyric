#include "crc32.h"

/* Bit by bit, reflected, polynomial 0xEDB88320: small enough to read, the same on every target. */
uint32_t crc32_update(uint32_t crc, const uint8_t *bytes, size_t length) {
    crc = ~crc;
    for (size_t i = 0; i < length; i++) {
        crc ^= bytes[i];
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
    }
    return ~crc;
}
