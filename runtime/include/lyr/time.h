/* Time (design/v5/spec/01 L9: a thin wrapper of the system's clocks). */
#ifndef LYR_TIME_H
#define LYR_TIME_H

#include <stdint.h>

/* Nanoseconds on a clock that only moves forward, from an unspecified start: for intervals and
 * deadlines — a timer, a timeout — never for the time of day. */
int64_t lyr_clock_monotonic_ns(void);

/* Nanoseconds since 1970-01-01T00:00:00Z on the system's wall clock (design/v5/spec/10 Q1, the
 * time of an Instant): the time of day, which the system may set back — never for an interval.
 * Leap seconds are not counted, as POSIX time does not count them. */
int64_t lyr_clock_realtime_ns(void);

#endif
