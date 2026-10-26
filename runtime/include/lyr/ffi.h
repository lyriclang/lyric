/* What a call into C needs beside the call itself: the symbol a C function goes by, and the
 * conversions std.ffi's types make at the boundary. */
#ifndef LYR_FFI_H
#define LYR_FFI_H

#include <stdint.h>

#include "lyr/types.h"

/* The name the linker knows a C function by: its own, behind the platform's prefix ('_' on
 * macOS, none elsewhere). The program declares a C function under a name of its own and binds it
 * to this one with an asm label, so its prototype never meets the one a system header gives the
 * same function — 'strlen' returns 'size_t' there, which is not 'uint64_t' on every target. */
#define LYR_C_STRINGIFY_(x) #x
#define LYR_C_STRINGIFY(x) LYR_C_STRINGIFY_(x)
#if defined(__USER_LABEL_PREFIX__)
#  define LYR_C_SYMBOL(name) LYR_C_STRINGIFY(__USER_LABEL_PREFIX__) name
#else
#  define LYR_C_SYMBOL(name) name
#endif

/* The address of a string's bytes, which a NUL ends: what a `string` handed to a `CStr` parameter
 * is, without a copy. */
uint64_t lyr_ffi_string_address(const LyrStr *s);

/* The bytes of the C string at `address` before its NUL. */
int64_t lyr_ffi_cstr_length(uint64_t address);

/* The offset of the first byte of the C string at `address` that is no well-formed UTF-8, or -1. */
int64_t lyr_ffi_cstr_invalid(uint64_t address);

/* A copy of the C string at `address` as a string; its bytes are well-formed UTF-8. */
LyrStr *lyr_ffi_cstr_string(uint64_t address);

/* The tag of an enum value C returned: `value` itself where the enum has a variant for it, a
 * panic (LYR-RT0021) where it has none — a C enum may hold any integer, a Lyric enum holds one
 * of its variants. */
int64_t lyr_ffi_enum_tag(int64_t value, int64_t variants, const char *enum_name);

/* std.ffi.alloc: count values of size bytes each, set to zero, from C's heap — never null: a
 * negative count panics (LYR-RT0007), no memory left panics (LYR-RT0005). */
uint64_t lyr_ffi_alloc(int64_t count, uint64_t size);

/* std.ffi.free: the memory lyr_ffi_alloc gave, back to C's heap. */
void lyr_ffi_free(uint64_t address);

/* A callback's trampoline, around the Lyric function C calls: the frames below are C's, which the
 * running coroutine counts (lyr_coro_enter_foreign). On a thread Lyric does not run — attached to
 * no collector — there is no Lyric to call, and the process ends with a report. */
void lyr_ffi_callback_enter(void);
void lyr_ffi_callback_leave(void);

#endif
