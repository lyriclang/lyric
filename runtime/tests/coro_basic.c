/* Coroutines (01 L4): values cross a yield through the coroutine's argument; a coroutine resumes
 * another, which yields back to it, not to the thread; status and the running coroutine at every
 * step; a deep stack in a coroutine; registers the caller keeps across a resume are intact — the
 * switch saves what the ABI says a call preserves, integer and floating point. Expected output:
 * "coro ok". With an argument, a misuse that panics with LYR-RT0014 and exit 101:
 *   self     a coroutine resumes itself
 *   done     a coroutine is resumed after its body returned
 *   outside  a yield on the thread's own stack */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

/* --- values through the argument --- */

typedef struct Squares { int64_t limit; int64_t yielded; } Squares;

static void squares_body(void *arg) {
    Squares *s = arg;
    for (int64_t i = 0; i < s->limit; i++) {
        s->yielded = i * i;
        lyr_coro_yield();
    }
}

static void squares(void) {
    Squares state = { .limit = 5, .yielded = -1 };
    LyrCoro *co = lyr_coro_new(&coroutine_desc, squares_body, &state, 0);
    CHECK(lyr_coro_status(co) == LYR_CORO_SUSPENDED);
    CHECK(lyr_coro_arg(co) == &state);
    CHECK(lyr_coro_stacks() == 0);  /* a stack only once it runs */
    int64_t sum = 0;
    int steps = 0;
    for (;;) {
        lyr_coro_resume(co);
        if (lyr_coro_status(co) == LYR_CORO_DONE) break;
        CHECK(lyr_coro_status(co) == LYR_CORO_SUSPENDED);
        CHECK(lyr_coro_stacks() == 1);
        sum += state.yielded;
        steps++;
    }
    CHECK(steps == 5);
    CHECK(sum == 0 + 1 + 4 + 9 + 16);
    CHECK(lyr_coro_stacks() == 0);  /* released when the body returned */
}

/* --- nesting --- */

static LyrCoro *outer_co, *inner_co;
static char trail[128];
static size_t trail_len;

static void note(const char *step) {
    size_t n = strlen(step);
    CHECK(trail_len + n < sizeof trail);
    memcpy(trail + trail_len, step, n);
    trail_len += n;
    trail[trail_len] = '\0';
}

static void inner_body(void *arg) {
    (void)arg;
    CHECK(lyr_coro_current() == inner_co);
    CHECK(lyr_coro_status(outer_co) == LYR_CORO_RUNNING);  /* waiting for this one */
    note("i1 ");
    lyr_coro_yield();
    note("i2 ");
    lyr_coro_yield();
    note("i3 ");
}

static void outer_body(void *arg) {
    (void)arg;
    CHECK(lyr_coro_current() == outer_co);
    inner_co = lyr_coro_new(&coroutine_desc, inner_body, NULL, 64 * 1024);
    note("o1 ");
    lyr_coro_resume(inner_co);
    CHECK(lyr_coro_current() == outer_co);
    note("o2 ");
    lyr_coro_yield();  /* back to the thread, with the inner one suspended */
    note("o3 ");
    lyr_coro_resume(inner_co);
    lyr_coro_resume(inner_co);
    CHECK(lyr_coro_status(inner_co) == LYR_CORO_DONE);
    note("o4 ");
}

static void nesting(void) {
    outer_co = lyr_coro_new(&coroutine_desc, outer_body, NULL, 0);
    CHECK(lyr_coro_current() == NULL);
    lyr_coro_resume(outer_co);
    CHECK(lyr_coro_current() == NULL);
    note("m1 ");
    CHECK(lyr_coro_stacks() == 2);
    lyr_coro_resume(outer_co);
    CHECK(lyr_coro_status(outer_co) == LYR_CORO_DONE);
    CHECK(strcmp(trail, "o1 i1 o2 m1 o3 i2 i3 o4 ") == 0);
    CHECK(lyr_coro_stacks() == 0);
}

/* --- a deep stack: 1000 frames — about 100 KiB in the debug profile, more under ASan's redzones —
 * and a yield at the bottom --- */

typedef struct Deep { int64_t result; } Deep;

static int64_t descend(int64_t n) {
    volatile int64_t local[4] = { n, n + 1, n + 2, n + 3 };
    if (n == 0) {
        lyr_coro_yield();
        return local[0];
    }
    return descend(n - 1) + local[1] - n;  /* +1 per level */
}

static void deep_body(void *arg) { ((Deep *)arg)->result = descend(1000); }

static void deep(void) {
    Deep state = { 0 };
    LyrCoro *co = lyr_coro_new(&coroutine_desc, deep_body, &state, 0);
    lyr_coro_resume(co);
    CHECK(lyr_coro_status(co) == LYR_CORO_SUSPENDED);
    lyr_coro_resume(co);
    CHECK(lyr_coro_status(co) == LYR_CORO_DONE);
    CHECK(state.result == 1000);
}

/* --- registers across a resume --- */

static void churn_body(void *arg) {
    volatile double *sink = arg;
    double x = 1.0;
    for (int round = 0; round < 3; round++) {
        double a = x * 1.1, b = a * 1.2, c = b * 1.3, d = c * 1.4, e = d * 1.5, f = e * 1.6;
        double g = f * 1.7, h = g * 1.8, i = h * 1.9, j = i * 2.1, k = j * 2.2, l = k * 2.3;
        *sink = a + b + c + d + e + f + g + h + i + j + k + l;
        x = *sink / 1000.0;
        lyr_coro_yield();
    }
}

static __attribute__((noinline)) double kept_across(LyrCoro *co, double seed) {
    double a = seed + 1.5, b = seed + 2.25, c = seed + 3.125, d = seed + 4.0625, e = seed + 5.5;
    double f = seed + 6.75, g = seed + 7.875, h = seed + 8.125, i = seed + 9.25, j = seed + 10.5;
    int64_t k = (int64_t)seed + 11, l = k * 3, m = l + 13, n = m * 5, o = n - 15, p = o * 7;
    lyr_coro_resume(co);
    return a + b + c + d + e + f + g + h + i + j + (double)(k + l + m + n + o + p);
}

static void registers(void) {
    volatile double sink = 0;
    LyrCoro *co = lyr_coro_new(&coroutine_desc, churn_body, (void *)&sink, 0);
    for (int round = 0; round < 3; round++) {
        double seed = (double)round;
        double a = seed + 1.5, b = seed + 2.25, c = seed + 3.125, d = seed + 4.0625, e = seed + 5.5;
        double f = seed + 6.75, g = seed + 7.875, h = seed + 8.125, i = seed + 9.25, j = seed + 10.5;
        int64_t k = (int64_t)seed + 11, l = k * 3, m = l + 13, n = m * 5, o = n - 15, p = o * 7;
        double expected = a + b + c + d + e + f + g + h + i + j + (double)(k + l + m + n + o + p);
        CHECK(kept_across(co, seed) == expected);
    }
    lyr_coro_resume(co);
    CHECK(lyr_coro_status(co) == LYR_CORO_DONE);
}

/* --- misuse --- */

static void resumes_itself(void *arg) {
    (void)arg;
    lyr_coro_resume(lyr_coro_current());
}

static void returns_at_once(void *arg) { (void)arg; }

static int64_t program(void) {
    const char *which = lyr_argc() > 1 ? lyr_argv()[1] : "";
    if (strcmp(which, "self") == 0) {
        lyr_coro_resume(lyr_coro_new(&coroutine_desc, resumes_itself, NULL, 0));
    } else if (strcmp(which, "done") == 0) {
        LyrCoro *co = lyr_coro_new(&coroutine_desc, returns_at_once, NULL, 0);
        lyr_coro_resume(co);
        lyr_coro_resume(co);
    } else if (strcmp(which, "outside") == 0) {
        lyr_coro_yield();
    } else {
        squares();
        nesting();
        deep();
        registers();
        lyr_println(lyr_str_from_cstr("coro ok"));
        return 0;
    }
    lyr_println(lyr_str_from_cstr("not reached"));
    return 1;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
