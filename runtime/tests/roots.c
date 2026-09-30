/* Roots, pins and registered ranges (01 L1, 11 X6).
 * Expected: 1000 objects held only by roots all survive a collection (their weak references still
 * answer); after the roots are freed at least 950 of them die (conservative margin). 1000 objects
 * held only in malloc memory registered as a scanned range survive; after the range is
 * unregistered at least 950 die. lyr_pin/lyr_unpin compile to nothing. Prints "roots ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

#include <stdlib.h>

enum { N = 1000 };

typedef struct Box { LyrObj header; int64_t value; } Box;
static const LyrDesc box_desc = { .size = sizeof(Box), .name = "test.Box" };

static int deaths;
static void count_death(void *context) { (void)context; deaths++; }

static LyrRoot *roots[N];
static LyrWeak *weaks[N];

NOINLINE static void make_rooted(void) {
    for (int i = 0; i < N; i++) {
        Box *box = lyr_alloc(&box_desc);
        box->value = i;
        roots[i] = lyr_root_new(box);
        weaks[i] = lyr_weak_new(box, count_death, NULL);
    }
}

NOINLINE static void make_ranged(void **range) {
    for (int i = 0; i < N; i++) {
        Box *box = lyr_alloc(&box_desc);
        box->value = i;
        range[i] = box;
        weaks[i] = lyr_weak_new(box, count_death, NULL);
    }
}

NOINLINE static int alive(void) {
    int n = 0;
    for (int i = 0; i < N; i++) if (lyr_weak_get(weaks[i])) n++;
    return n;
}

static int64_t program(void) {
    /* roots */
    make_rooted();
    wipe_stack();
    lyr_gc_collect();
    CHECK(alive() == N && deaths == 0);
    for (int i = 0; i < N; i++) CHECK(((Box *)lyr_root_get(roots[i]))->value == i);

    Box *pinned = lyr_root_get(roots[0]);
    lyr_pin(pinned);
    lyr_unpin(pinned);
    pinned = NULL;

    /* Clear the handles too: this array is static data, which the collector scans conservatively,
     * and freed root cells get reused for new objects - a stale handle would keep them alive. */
    for (int i = 0; i < N; i++) {
        lyr_root_free(roots[i]);
        roots[i] = NULL;
    }
    wipe_stack();
    lyr_gc_collect();
    CHECK(deaths >= 950);
    CHECK(alive() <= 50);

    /* registered ranges */
    deaths = 0;
    void **range = calloc(N, sizeof(void *));
    lyr_register_stack(range, range + N);
    make_ranged(range);
    wipe_stack();
    lyr_gc_collect();
    CHECK(alive() == N && deaths == 0);

    lyr_unregister_stack(range, range + N);
    wipe_stack();
    lyr_gc_collect();
    CHECK(deaths >= 950);
    free(range);

    lyr_println(lyr_str_from_cstr("roots ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
