/* The monotonic clock (lyr/time.h). */
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/time.h"

#ifdef _WIN32
#include <windows.h>

int64_t lyr_clock_monotonic_ns(void) {
    LARGE_INTEGER frequency, now;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&now);
    /* Whole seconds and the rest apart, so the product never overflows. */
    int64_t seconds = now.QuadPart / frequency.QuadPart;
    int64_t rest = now.QuadPart % frequency.QuadPart;
    return seconds * INT64_C(1000000000) + rest * INT64_C(1000000000) / frequency.QuadPart;
}
#else
#include <time.h>

int64_t lyr_clock_monotonic_ns(void) {
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (int64_t)now.tv_sec * INT64_C(1000000000) + now.tv_nsec;
}
#endif
