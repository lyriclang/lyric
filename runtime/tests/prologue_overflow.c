/* The prologue's check (01 S3, 13 M11 P6): a function that checks its stack at its entry, as compiled
 * code does, overflows into a panic before its frames reach the guard page. In a coroutine the
 * panic leaves the coroutine (13 §1.6 rule 10) and its resumer goes on; on the thread's own stack
 * it ends the process with the report, and what was printed before comes out first.
 * Expected: with "coroutine", stdout "coroutine overflow: LYR-RT0006", exit 0; with "main", stdout
 * "before", exit 101, stderr starting with "panic [LYR-RT0006]: stack overflow" and a frame of
 * down. The recursion is not in tail position and keeps a frame of its own; in the coroutine it
 * goes past a hundred frames before the overflow. */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

#include <string.h>

static const LyrDesc coroutine_desc = { .size = 0, .flags = LYR_DESC_CORO, .name = "test.Coroutine" };

/* how deep the recursion got: an overflow found at once — a bound that is not the stack's — would
 * be found at depth 0 */
static volatile int64_t deepest;

NOINLINE static int64_t down(int64_t n) {
    LYR_STACK_CHECK();
    if (n > deepest) deepest = n;
    volatile char pad[256];
    pad[0] = (char)n;
    return down(n + 1) + pad[0];
}

static void body(void *arg) {
    (void)arg;
    down(0);
}

static int64_t program(void) {
    if (lyr_argc() > 1 && strcmp(lyr_argv()[1], "coroutine") == 0) {
        LyrCoro *co = lyr_coro_new(&coroutine_desc, body, NULL, 64 * 1024);
        CHECK(lyr_task_resume(co) == 2);  /* panicked */
        CHECK(strcmp(lyr_task_panic_code(co)->bytes, "LYR-RT0006") == 0);
        CHECK(deepest > 100);  /* 64 KiB of frames of some 300 bytes */
        lyr_println(lyr_str_from_cstr("coroutine overflow: LYR-RT0006"));
        return 0;
    }
    lyr_println(lyr_str_from_cstr("before"));
    return down(0);
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
