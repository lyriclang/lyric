/* A stack overflow is a panic, not a segfault (01 S3, 05 E8): until the emitter checks the stack
 * in every prologue, the guard page and the fault handler are the net.
 * Expected: exit 101; stderr starts with "panic [LYR-RT0006]: stack overflow", the next line is a
 * frame of recurse, and the trace ends with "    ... deeper frames not shown". The recursion is
 * not in tail position and keeps a frame of its own, so no optimizer turns it into a loop. */
#include "lyr/lyr.h"
#include "conservative.h"

NOINLINE static int64_t recurse(int64_t depth) {
    volatile char frame[256];
    frame[0] = (char)depth;
    return frame[0] + recurse(depth + 1) / 2;
}

static int64_t program(void) {
    return recurse(0);
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
