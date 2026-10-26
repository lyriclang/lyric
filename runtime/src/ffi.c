/* The conversions std.ffi's types make at a C function's boundary. */
#include "lyr/ffi.h"
#include "lyr/panic.h"
#include "lyr/string.h"

#include <string.h>

uint64_t lyr_ffi_string_address(const LyrStr *s) { return (uint64_t)(uintptr_t)s->bytes; }

int64_t lyr_ffi_cstr_length(uint64_t address) {
    return (int64_t)strlen((const char *)(uintptr_t)address);
}

int64_t lyr_ffi_cstr_invalid(uint64_t address) {
    const char *text = (const char *)(uintptr_t)address;
    int64_t bad = -1;
    return lyr_utf8_valid(text, (int64_t)strlen(text), &bad) ? -1 : bad;
}

LyrStr *lyr_ffi_cstr_string(uint64_t address) {
    return lyr_str_from_cstr((const char *)(uintptr_t)address);
}

int64_t lyr_ffi_enum_tag(int64_t value, int64_t variants, const char *enum_name) {
    if (value < 0 || value >= variants)
        lyr_panic(LYR_RT_FOREIGN_ENUM, "C returned %lld, which is no variant of %s (it has %lld)",
                  (long long)value, enum_name, (long long)variants);
    return value;
}
