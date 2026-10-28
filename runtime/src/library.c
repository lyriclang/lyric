/* std.ffi's Library: a shared library loaded while the program runs, and the addresses of its
 * symbols — dlopen and dlsym, LoadLibraryW and GetProcAddress. A file of its own: a program that
 * loads none links no loader. */
#include "lyr/ffi.h"
#include "lyr/gc.h"
#include "lyr/panic.h"
#include "lyr/string.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <dlfcn.h>
#endif

/* Why the thread's last open failed, as the system says it. */
static _Thread_local char last_error[512];

#ifdef _WIN32
static void keep_system_error(DWORD code) {
    DWORD n = FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, code, 0, last_error,
                             sizeof last_error, NULL);
    while (n > 0 && (last_error[n - 1] == '\n' || last_error[n - 1] == '\r' || last_error[n - 1] == ' ')) n--;
    last_error[n] = '\0';
    if (n == 0) snprintf(last_error, sizeof last_error, "error %lu", (unsigned long)code);
}
#endif

uint64_t lyr_ffi_library_open(const LyrStr *path) {
    last_error[0] = '\0';
#ifdef _WIN32
    int wide = MultiByteToWideChar(CP_UTF8, 0, (const char *)path->bytes, (int)path->len, NULL, 0);
    wchar_t *name = malloc(((size_t)wide + 1) * sizeof(wchar_t));
    if (name == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory opening a library");
    MultiByteToWideChar(CP_UTF8, 0, (const char *)path->bytes, (int)path->len, name, wide);
    name[wide] = L'\0';
    LYR_GC_ENTER_SAFE();
    HMODULE handle = LoadLibraryW(name);
    DWORD code = handle == NULL ? GetLastError() : 0;
    LYR_GC_LEAVE_SAFE();
    free(name);
    if (handle == NULL) keep_system_error(code);
    return (uint64_t)(uintptr_t)handle;
#else
    LYR_GC_ENTER_SAFE();
    void *handle = dlopen((const char *)path->bytes, RTLD_NOW | RTLD_LOCAL);
    LYR_GC_LEAVE_SAFE();
    if (handle == NULL) {
        const char *why = dlerror();
        snprintf(last_error, sizeof last_error, "%s", why != NULL ? why : "the loader refused it");
    }
    return (uint64_t)(uintptr_t)handle;
#endif
}

LyrStr *lyr_ffi_library_error(void) { return lyr_str_from_cstr(last_error); }

uint64_t lyr_ffi_library_symbol(uint64_t handle, const LyrStr *name) {
#ifdef _WIN32
    return (uint64_t)(uintptr_t)GetProcAddress((HMODULE)(uintptr_t)handle, (const char *)name->bytes);
#else
    return (uint64_t)(uintptr_t)dlsym((void *)(uintptr_t)handle, (const char *)name->bytes);
#endif
}

void lyr_ffi_library_close(uint64_t handle) {
#ifdef _WIN32
    FreeLibrary((HMODULE)(uintptr_t)handle);
#else
    dlclose((void *)(uintptr_t)handle);
#endif
}
