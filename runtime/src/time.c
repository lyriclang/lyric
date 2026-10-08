/* The system's clocks (lyr/time.h): the monotonic one and the wall clock. */
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

/* 100-nanosecond intervals since 1601-01-01T00:00:00Z, the system's finest (Windows 8 and later);
 * 1970 lies 11644473600 seconds after 1601. */
int64_t lyr_clock_realtime_ns(void) {
    FILETIME now;
    GetSystemTimePreciseAsFileTime(&now);
    uint64_t ticks = ((uint64_t)now.dwHighDateTime << 32) | now.dwLowDateTime;
    return ((int64_t)ticks - INT64_C(116444736000000000)) * 100;
}
#else
#include <time.h>

int64_t lyr_clock_monotonic_ns(void) {
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (int64_t)now.tv_sec * INT64_C(1000000000) + now.tv_nsec;
}

int64_t lyr_clock_realtime_ns(void) {
    struct timespec now;
    clock_gettime(CLOCK_REALTIME, &now);
    return (int64_t)now.tv_sec * INT64_C(1000000000) + now.tv_nsec;
}
#endif
