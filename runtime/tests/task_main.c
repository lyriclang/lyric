/* main as a task (06 T6, N6 S1). Expected: the runtime makes main's context and hands it to the
 * scheduler — here a loop in C over the contexts made ready — and main runs in it, on a stack
 * deep enough for a recursion a default coroutine stack would overflow. main starts a task
 * around a function value and parks twice, the task parks once; the order of their steps is
 * 10 20 11 21 12. Prints "task ok" and exits with 7, main's value masked to 0..255. With the
 * argument "yield" main yields in its task, outside every generator: a panic, RT0014. */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

typedef struct Cell { LyrObj header; int64_t value; } Cell;
static const LyrDesc cell_desc = { .size = sizeof(Cell), .name = "test.Cell" };

static LyrCoro *ready[8];
static int head, tail;
static void make_ready(LyrCoro *co) { ready[tail++ % 8] = co; }

static int events[16];
static int logged;
static void note(int event) { events[logged++] = event; }

static int scheduler_token;

static void task_code(void *env, LyrErr **error) {
    CHECK(error == NULL);
    CHECK(((Cell *)env)->value == 42);
    note(20);
    make_ready(lyr_coro_current());
    lyr_coro_park();
    note(21);
}

static void scheduler(LyrCoro *main) {
    CHECK(lyr_task_scheduler() == NULL);
    lyr_task_set_scheduler(&scheduler_token);
    make_ready(main);
    while (lyr_coro_status(main) != LYR_CORO_DONE) {
        CHECK(head < tail);
        lyr_coro_resume(ready[head++ % 8]);
    }
    lyr_task_set_scheduler(NULL);
}

/* 4 KiB a frame: 400 of them overflow a default coroutine stack of 256 KiB. */
static int64_t deep(int64_t n) {
    volatile char pad[4096];
    pad[n % 4096] = (char)n;
    if (n == 0) return 0;
    return deep(n - 1) + (pad[n % 4096] == (char)n);
}

static int64_t program(void) {
    CHECK(lyr_task_scheduler() == &scheduler_token);
    CHECK(lyr_coro_current() != NULL);
    if (lyr_argc() > 1 && strcmp(lyr_argv()[1], "yield") == 0) {
        int64_t value = 1;
        lyr_coro_yield_dynamic(&value, "int");
    }
    CHECK(deep(400) == 400);
    note(10);
    Cell *cell = lyr_alloc(&cell_desc);
    cell->value = 42;
    LyrCoro *task = lyr_task_start(task_code, cell, 0);
    make_ready(task);
    make_ready(lyr_coro_current());
    lyr_coro_park();
    note(11);
    lyr_gc_collect();
    make_ready(lyr_coro_current());
    lyr_coro_park();
    note(12);
    CHECK(lyr_coro_status(task) == LYR_CORO_DONE);
    const int expected[] = { 10, 20, 11, 21, 12 };
    CHECK(logged == 5);
    for (int i = 0; i < 5; i++) CHECK(events[i] == expected[i]);
    CHECK(lyr_task_wait(0) == 0);
    lyr_println(lyr_str_from_cstr("task ok"));
    return 7 + 256;
}

int main(int argc, char **argv) { return lyr_run_main_task(argc, argv, program, scheduler); }
