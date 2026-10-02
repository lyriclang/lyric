/* A panic leaves the coroutine it happens in (05 E8). Expected: a coroutine that panics leaves its
 * stack, and the scheduler's quiet resume finds it PANICKED with the report kept — the code, the
 * message, the frames, which name the function that panicked — and its stack released. A plain
 * resume passes the panic on: a coroutine that resumes a panicking one panics in turn, with the
 * same report, and nothing after that resume runs. Prints "coro panic ok". With the argument
 * "main" the panic reaches the thread's own stack and ends the process with the first report; with
 * "foreign" a coroutine with a foreign frame on its stack ends the process at once. */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

static int after_resume;

static void explode(void *arg) {
    (void)arg;
    lyr_panic(LYR_RT_PANIC, "boom %d", 7);
}

static void outer(void *arg) {
    (void)arg;
    LyrCoro *inner = lyr_coro_new(&coroutine_desc, explode, NULL, 0);
    lyr_coro_resume(inner);
    after_resume = 1;
}

static void through_c(void *arg) {
    (void)arg;
    lyr_coro_enter_foreign();
    lyr_panic(LYR_RT_PANIC, "through C");
}

static const char *text(LyrStr *s) {
    CHECK(s != NULL);
    return (const char *)s->bytes;
}

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "";
    if (strcmp(which, "main") == 0) {
        lyr_coro_resume(lyr_coro_new(&coroutine_desc, explode, NULL, 0));
        return 0;
    }
    if (strcmp(which, "foreign") == 0) {
        (void)lyr_task_resume(lyr_coro_new(&coroutine_desc, through_c, NULL, 0));
        return 0;
    }

    LyrCoro *a = lyr_coro_new(&coroutine_desc, explode, NULL, 0);
    CHECK(lyr_task_resume(a) == 2);
    CHECK(lyr_coro_status(a) == LYR_CORO_PANICKED);
    CHECK(strcmp(text(lyr_task_panic_code(a)), "LYR-RT0008") == 0);
    CHECK(strcmp(text(lyr_task_panic_message(a)), "boom 7") == 0);
    CHECK(strstr(text(lyr_task_panic_trace(a)), "    at explode") != NULL);
    CHECK(lyr_coro_stacks() == 0);

    LyrCoro *b = lyr_coro_new(&coroutine_desc, outer, NULL, 0);
    CHECK(lyr_task_resume(b) == 2);
    CHECK(after_resume == 0);
    CHECK(strcmp(text(lyr_task_panic_message(b)), "boom 7") == 0);
    CHECK(strstr(text(lyr_task_panic_trace(b)), "    at explode") != NULL);
    lyr_gc_collect();
    CHECK(lyr_coro_stacks() == 0);
    CHECK(lyr_task_panic_code(lyr_coro_new(&coroutine_desc, explode, NULL, 0)) == NULL);

    lyr_println(lyr_str_from_cstr("coro panic ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
