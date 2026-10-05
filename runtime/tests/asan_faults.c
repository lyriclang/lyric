/* The controls for the ASan profile: the runtime turns two ASan defaults off for the collector's
 * sake (gc_boehm.c), and UBSan is set not to recover (Target.cs); these faults show the profile
 * still catches what it is for. Chosen by the first argument; under ASan:
 *   overflow     a write one past a malloc block: "heap-buffer-overflow", exit 1
 *   signed       a signed int overflow in plain C: "runtime error: signed integer overflow", and
 *                the program ends there — exit not 0, "not reached" never printed
 *   unreachable  control reaching 'LYR_UNREACHABLE()' — the emitter's C for a block the
 *                lowering sealed as unreachable, and no undefined behaviour for UBSan to find:
 *                "panic [LYR-RT0017]: control reached code the compiler holds unreachable",
 *                and the program ends there
 * (Built in the other profiles, the faults go unnoticed; the program is only run under ASan.) */
#include "lyr/lyr.h"

#include <stdlib.h>
#include <string.h>

static volatile int big = 2147483647;
static volatile int one = 1;

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "";
    if (strcmp(which, "overflow") == 0) {
        char *block = malloc(16);
        volatile char *past = block;
        past[16] = 1;
        free(block);
    } else if (strcmp(which, "signed") == 0) {
        int sum = big + one;
        (void)sum;
    } else if (strcmp(which, "unreachable") == 0) {
        if (one) LYR_UNREACHABLE();
    }
    lyr_println(lyr_str_from_cstr("not reached"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
