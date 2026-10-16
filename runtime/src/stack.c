/* The prologue's check of the stack (design/v5/spec/01 S3; 13 M11 P6): every compiled function that
 * calls compares its frame with the thread's bound (gc.h, LYR_STACK_CHECK); below the bound lies a
 * reserve, enough for the panic an overflow is. The bound follows the stack the thread runs on —
 * its own, or a coroutine's (coro.c sets it at each switch). The guard page below each stack and
 * the fault handler stay the net under the frames that do not check: the runtime's own C and
 * foreign code (crash.c). */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE  /* pthread_getattr_np */
#endif

#include "lyr/gc.h"
#include "lyr/panic.h"
#include "internal.h"

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <unistd.h>
#endif

_Thread_local LyrMutator lyr_mutator;

/* Under ASan (the asan profile, 01 C7) two of its defaults fight a collector that scans stacks
 * conservatively, whichever it is: fake stacks move locals into memory the scan never sees, so
 * live objects would be collected; and LeakSanitizer does not scan the collector's heap, so malloc
 * blocks referenced only from there would be reported as leaks. */
#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
const char *__asan_default_options(void);
const char *__asan_default_options(void) {
    return "detect_stack_use_after_return=0:detect_leaks=0";
}
#  endif
#endif

uintptr_t lyr_stack_low(void) {
#if defined(_WIN32)
    ULONG_PTR low = 0, high = 0;
    GetCurrentThreadStackLimits(&low, &high);
    /* the reservation's bottom: the guard pages and the guarantee the filter keeps lie above it */
    return low == 0 ? 0 : (uintptr_t)low + LYR_STACK_GUARANTEE + 2 * 4096;
#elif defined(__APPLE__)
    uintptr_t top = (uintptr_t)pthread_get_stackaddr_np(pthread_self());
    return top - pthread_get_stacksize_np(pthread_self());
#else
    uintptr_t low = 0;
    pthread_attr_t attr;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        void *address;
        size_t size;
        if (pthread_attr_getstack(&attr, &address, &size) == 0) low = (uintptr_t)address;
        pthread_attr_destroy(&attr);
    }
    return low;
#endif
}

uintptr_t lyr_stack_own_limit(void) {
    uintptr_t low = lyr_stack_low();
    if (low == 0) return 0;
    /* called near the stack's top, when the thread starts: what lies below is about its size */
    uintptr_t here = (uintptr_t)__builtin_frame_address(0);
    return low + (here - low >= ((uintptr_t)1 << 20) ? LYR_STACK_OWN_RESERVE : LYR_STACK_RESERVE);
}

void lyr_stack_overflow(void) {
    lyr_panic(LYR_RT_STACK_OVERFLOW, "stack overflow");
}
