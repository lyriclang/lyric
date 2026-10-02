/* Coroutine stacks pace collections of their own (06 A5).
 * Expected: with 128 MiB alive the collector waits long between collections of its own accord —
 * a third of the heap allocated anew — so fifty thousand coroutines, each started, suspended and
 * dropped, would all hold their stacks until the next one. The stacks pace a collection when
 * those in use reach twice what the last one left, at least 1024: the peak stays near that. The
 * live blocks are whole at the end. Prints "coro pace ok". */
#include "lyr/lyr.h"
#include "check.h"

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };
static const LyrDesc bytes_desc = { .size = 0, .elem_size = 1, .name = "test.Bytes" };

enum { LIVE_BLOCKS = 64, BLOCK = 2 * 1024 * 1024, DROPPED = 50000, BOUND = 2 * 1024 + 64 };

static LyrArr *live[LIVE_BLOCKS];  /* static data is a root */

static void body(void *arg) {
    (void)arg;
    lyr_coro_yield();
}

static int64_t program(void) {
    for (int i = 0; i < LIVE_BLOCKS; i++) live[i] = lyr_alloc_array(&bytes_desc, BLOCK);
    size_t peak = 0;
    for (int i = 0; i < DROPPED; i++) {
        LyrCoro *co = lyr_coro_new(&coroutine_desc, body, NULL, 0);
        lyr_coro_resume(co);
        size_t now = lyr_coro_stacks();
        if (now > peak) peak = now;
    }
    CHECK(peak <= BOUND);
    for (int i = 0; i < LIVE_BLOCKS; i++) CHECK(live[i]->len == BLOCK);
    lyr_println(lyr_str_from_cstr("coro pace ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
