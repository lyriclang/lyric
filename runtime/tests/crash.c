/* Fault signals are crashes, not panics (10 Q9), chosen by the first argument.
 * Expected with "segv": a write through a null pointer; stderr starts with
 *   crash: SIGSEGV (invalid memory access at 0x0)                    (Linux, macOS)
 *   crash: access violation (invalid memory access at 0x0)           (Windows)
 * and its next line is the faulting frame, "    at fault_here (…crash.c:N)"; the process ends as
 * the fault would have ended it (not with 101). With "abort": stderr starts with
 * "crash: SIGABRT (abort)" (Windows: "crash: abort") and the trace names abort_here. */
#include "lyr/lyr.h"
#include "conservative.h"

#include <stdlib.h>
#include <string.h>

static int *volatile target = NULL;

NOINLINE static int fault_here(int value) {
    *target = value;
    return value + 1;
}

NOINLINE static int abort_here(int value) {
    if (value > 0) abort();
    return value + 1;
}

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "segv";
    if (strcmp(which, "abort") == 0) return abort_here(1) + 1;
    return fault_here(42) + 1;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
