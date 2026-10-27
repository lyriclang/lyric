/* A coroutine with C frames on its stack is not collected (01 K4): C may point into its stack.
 * Expected: sixty-four coroutines suspend with a foreign frame counted, sixty-four without; all are
 * dropped and collected: those without go with their stacks — a few may survive as stale words
 * the scan takes for references — those with keep theirs, and one warning says so on stderr.
 * Nothing on a dropped stack runs again. Prints "coro foreign ok". */
#include "lyr/lyr.h"
#include "check.h"

static const LyrDesc coroutine_desc = { .size = 0, .flags = LYR_DESC_CORO, .name = "test.Coroutine" };

enum { EACH = 64 };

static void plain_body(void *arg) {
    (void)arg;
    lyr_coro_yield();
    CHECK(0);  /* never: a dropped coroutine does not run on */
}

/* as a callback's trampoline leaves it: a C frame counted, then a yield inside it */
static void foreign_body(void *arg) {
    (void)arg;
    lyr_coro_enter_foreign();
    lyr_coro_yield();
    CHECK(0);
}

static __attribute__((noinline)) void drop(void) {
    for (int i = 0; i < EACH; i++) {
        lyr_coro_resume(lyr_coro_new(&coroutine_desc, plain_body, NULL, 0));
        LyrCoro *co = lyr_coro_new(&coroutine_desc, foreign_body, NULL, 0);
        lyr_coro_resume(co);
        CHECK(lyr_coro_foreign_depth(co) == 1);
    }
}

static int64_t program(void) {
    drop();
    for (int i = 0; i < 4; i++) lyr_gc_collect();
    size_t kept = lyr_coro_stacks();
    CHECK(kept >= EACH);             /* every stack with C frames on it */
    CHECK(kept < EACH + EACH / 4);   /* and few of the others */
    lyr_println(lyr_str_from_cstr("coro foreign ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
