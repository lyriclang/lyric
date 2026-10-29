#ifndef CRC32_H
#define CRC32_H

#include <stddef.h>
#include <stdint.h>

/* The CRC-32 of ISO 3309 and zlib over `length` bytes, continued from `crc` (0 to begin). */
uint32_t crc32_update(uint32_t crc, const uint8_t *bytes, size_t length);

#endif
