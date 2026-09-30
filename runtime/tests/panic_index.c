/* The panic golden (M1 S4, 05 E8): an index check fails three calls deep.
 * Expected: exit 101; stderr is exactly the panic line and the three frames of this file, the
 * innermost first, each with its line — no runtime frame above them, nothing from lyr_run_main or
 * below. The calls are not in tail position, so no optimizer removes a frame. */
#include "lyr/lyr.h"
#include "conservative.h"

static int64_t values[3] = { 10, 20, 30 };
static volatile int64_t wanted = 7;

NOINLINE static int64_t fail_at(int64_t index) {
    LYR_CHECK_INDEX(index, 3);
    return values[index];
}

NOINLINE static int64_t middle(int64_t index) {
    return fail_at(index) + 1;
}

static int64_t program(void) {
    return middle(wanted) + 1;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
