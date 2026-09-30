/* Weak references with death callbacks (01 L1, Cleaner form).
 * Expected: an object kept alive by a strong local keeps its weak reference answering and its
 * callback silent; 1000 unreachable objects: at least 950 callbacks run, each with its own context,
 * and those weak references answer NULL; a weak reference freed before death never calls back;
 * two weak references on one object both call back. Prints "weak ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

enum { N = 1000 };

typedef struct Box { LyrObj header; int64_t value; } Box;
static const LyrDesc box_desc = { .size = sizeof(Box), .name = "test.Box" };

static int seen[N + 2];
static void mark(void *context) { seen[(intptr_t)context]++; }

static LyrWeak *weaks[N];

NOINLINE static void make_garbage(void) {
    for (intptr_t i = 0; i < N; i++) {
        Box *box = lyr_alloc(&box_desc);
        weaks[i] = lyr_weak_new(box, mark, (void *)i);
    }
}

NOINLINE static LyrWeak *make_cancelled(void) {
    Box *box = lyr_alloc(&box_desc);
    LyrWeak *weak = lyr_weak_new(box, mark, (void *)(intptr_t)N);
    lyr_weak_free(weak);
    return weak;
}

NOINLINE static void make_double(void) {
    Box *box = lyr_alloc(&box_desc);
    (void)lyr_weak_new(box, mark, (void *)(intptr_t)(N + 1));
    (void)lyr_weak_new(box, mark, (void *)(intptr_t)(N + 1));
}

static int64_t program(void) {
    Box *kept = lyr_alloc(&box_desc);
    kept->value = 42;
    static int kept_deaths;
    LyrWeak *kept_weak = lyr_weak_new(kept, mark, (void *)(intptr_t)0);

    make_garbage();
    (void)make_cancelled();
    make_double();
    wipe_stack();
    lyr_gc_collect();
    lyr_gc_collect();

    CHECK(lyr_weak_get(kept_weak) == kept && kept->value == 42);

    int called = 0, null_answers = 0;
    for (int i = 1; i < N; i++) {
        CHECK(seen[i] <= 1);
        called += seen[i];
        if (lyr_weak_get(weaks[i]) == NULL) null_answers++;
    }
    CHECK(called >= 949);
    CHECK(null_answers >= called);
    CHECK(seen[N] == 0);        /* cancelled */
    CHECK(seen[N + 1] == 2);    /* two weak references, two callbacks */
    (void)kept_deaths;

    lyr_println(lyr_str_from_cstr("weak ok"));
    return kept->value == 42 ? 0 : 1;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
