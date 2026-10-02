/* A coroutine's stack overflow (01 S3, K1): its guard page turns a recursion without end into a
 * panic, LYR-RT0006, with the coroutine's frames — not a crash, and not a write into whatever lies
 * below the stack. Expected: exit 101, "panic [LYR-RT0006]: stack overflow" on stderr, nothing on
 * stdout. (On POSIX: the handler runs on the thread's alternate stack.) */
#include "lyr/lyr.h"
#include "check.h"

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

static int64_t down(int64_t n) {
    volatile char pad[256];
    pad[0] = (char)n;
    return down(n + 1) + pad[0];
}

static void body(void *arg) {
    (void)arg;
    down(0);
}

static int64_t program(void) {
    lyr_coro_resume(lyr_coro_new(&coroutine_desc, body, NULL, 64 * 1024));
    lyr_println(lyr_str_from_cstr("not reached"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
