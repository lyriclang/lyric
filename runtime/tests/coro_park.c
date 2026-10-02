/* Parking (06 N3). A task resumes a generator, the generator parks: control goes back to the
 * scheduler — the thread's own stack — not to the task, and the task is PARKED. The scheduler
 * resumes the task, and the generator continues where it parked, yields to the task, which goes on.
 * A parked chain survives collections, a thousand parks in a row keep their order, and closing a
 * parked coroutine or parking with no coroutine running panics (RT0014; the argument "closed" or
 * "outside" selects those). The clock only moves forward. Prints "coro park ok". */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };
static const LyrDesc bytes_desc = { .size = 0, .elem_size = 1, .name = "test.Bytes" };

static int events[16];
static int logged;
static void note(int event) { events[logged++] = event; }

static void generator(void *arg) {
    (void)arg;
    note(10);
    LyrArr *held = lyr_alloc_array(&bytes_desc, 64);  /* lives on this stack alone, across the park */
    memset(LYR_ARR_DATA(held, uint8_t), 7, 64);
    lyr_coro_park();
    note(11);
    CHECK(LYR_ARR_DATA(held, uint8_t)[63] == 7);
    int64_t value = 42;
    lyr_coro_yield_value(&value);
    note(12);
}

static void task(void *arg) {
    (void)arg;
    LyrCoro *gen = lyr_coro_new(&coroutine_desc, generator, NULL, 0);
    note(1);
    lyr_coro_resume(gen);  /* the generator parks inside: control goes to the scheduler, not here */
    note(2);               /* the scheduler resumed us, the generator went on and yielded */
    CHECK(*(int64_t *)lyr_coro_transfer(gen) == 42);
    lyr_coro_resume(gen);
    CHECK(lyr_coro_status(gen) == LYR_CORO_DONE);
    note(3);
}

static void parker(void *arg) {
    int64_t *count = arg;
    for (int i = 0; i < 1000; i++) {
        lyr_coro_park();
        (*count)++;
    }
}

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "";
    if (strcmp(which, "outside") == 0) lyr_coro_park();

    LyrCoro *t = lyr_coro_new(&coroutine_desc, task, NULL, 0);
    lyr_coro_resume(t);
    CHECK(lyr_coro_status(t) == LYR_CORO_PARKED);
    note(100);
    if (strcmp(which, "closed") == 0) lyr_coro_close(t);
    lyr_gc_collect();
    lyr_coro_resume(t);
    CHECK(lyr_coro_status(t) == LYR_CORO_DONE);
    const int expected[] = { 1, 10, 100, 11, 2, 12, 3 };
    CHECK(logged == 7);
    for (int i = 0; i < 7; i++) CHECK(events[i] == expected[i]);

    int64_t count = 0;
    LyrCoro *p = lyr_coro_new(&coroutine_desc, parker, &count, 0);
    int64_t before = lyr_clock_monotonic_ns();
    for (int i = 0; i <= 1000; i++) {
        lyr_coro_resume(p);
        CHECK(count == i);
        if (i % 100 == 0) lyr_gc_collect();
    }
    CHECK(lyr_coro_status(p) == LYR_CORO_DONE);
    CHECK(lyr_clock_monotonic_ns() >= before);
    CHECK(lyr_coro_stacks() == 0);

    lyr_println(lyr_str_from_cstr("coro park ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
