/* Time (design/v5/spec/01 L9: a thin wrapper of the system's clocks). */
#ifndef LYR_TIME_H
#define LYR_TIME_H

#include <stdint.h>

/* Nanoseconds on a clock that only moves forward, from an unspecified start: for intervals and
 * deadlines — a timer, a timeout — never for the time of day. */
int64_t lyr_clock_monotonic_ns(void);

#endif
