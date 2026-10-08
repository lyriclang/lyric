/* The system's clocks (lyr/time.h).
 * Expected: the wall clock stands between 2025 and 2100, and the monotonic one never goes back —
 * "clock ok", exit 0. */
#include "lyr/lyr.h"
#include "check.h"

int main(void) {
    int64_t wall = lyr_clock_realtime_ns();
    /* 2025-01-01T00:00:00Z and 2100-01-01T00:00:00Z, in seconds since the epoch */
    CHECK(wall / INT64_C(1000000000) > INT64_C(1735689600));
    CHECK(wall / INT64_C(1000000000) < INT64_C(4102444800));

    int64_t last = lyr_clock_monotonic_ns();
    for (int i = 0; i < 1000; i++) {
        int64_t now = lyr_clock_monotonic_ns();
        CHECK(now >= last);
        last = now;
    }
    /* No such check on the wall clock: it may be set back. */

    fputs("clock ok\n", stdout);
    return 0;
}
