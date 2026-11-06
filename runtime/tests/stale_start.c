/* A dead object whose head lies on a line that stays, beside a live neighbour, and whose tail lies
 * on lines handed out again. A coroutine is laid over a line's end, the box before it kept, and
 * dies. An array takes the line after, and the words where the coroutine's stack pointer, bottom
 * and status lay now hold what the array holds. A word in a scanned range then points at the dead
 * head, as a stale word on a stack does. Expected: the word answers nothing, and the collection
 * reads no stack from the array's numbers. Before the fix the head kept its start bit, the word
 * marked the "coroutine", and the collector read from the address the array gave it. With "young"
 * the same under young collections. Prints "stale start ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"
#include "../src/internal.h"

#include <string.h>

enum { LINE = 256, ATTEMPTS = 32, TRIES = 1 << 14, ELEMENTS = 28 };

static const LyrDesc coroutine_desc = { .size = 0, .flags = LYR_DESC_CORO, .name = "test.Coroutine" };
typedef struct Box { LyrObj header; int64_t value; } Box;
static const LyrDesc box_desc = { .size = sizeof(Box), .flags = 0, .name = "test.Box" };
static const LyrDesc words_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = sizeof(int64_t), .name = "test.Words" };
static const uint64_t element_refs[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = element_refs,
                                   .name = "test.Words[]" };

static uintptr_t stale[1];    /* a range the collection scans: empty until the coroutine is dead */
static uintptr_t hidden;      /* the coroutine, where no scan takes it for a reference */
static LyrRoot *neighbour;    /* the box that keeps the head's line */
static LyrRoot *tail;         /* the array over the coroutine's tail */
static int young;

static void body(void *arg) { (void)arg; }

/* The next object on the last granule of a line, a box kept before it. False when it went elsewhere. */
NOINLINE static bool lay_out(void) {
    for (int i = 0; i < TRIES; i++) {
        Box *box = lyr_alloc(&box_desc);
        uintptr_t next = (uintptr_t)box + sizeof(Box);
        if (next % LINE != LINE - LYR_GC_GRANULE) continue;
        LyrCoro *co = lyr_coro_new(&coroutine_desc, body, NULL, 0);
        if ((uintptr_t)co != next) continue;
        neighbour = lyr_root_new(box);
        hidden = ~(uintptr_t)co;
        return true;
    }
    return false;
}

static void collect(void) {
    if (young) lyr_gc_collect_young();
    else lyr_gc_collect();
}

/* The coroutine's next line starts a run of free lines, if it died: an array there. The arrays
 * looked at stay alive, or a collection on the way would hand the same lines out again and the
 * search never get as far as the coroutine. */
NOINLINE static LyrCoro *cover_the_tail(void) {
    LyrCoro *co = (LyrCoro *)~hidden;
    LyrArr *looked_at = lyr_alloc_array(&refs_desc, TRIES);
    LyrRoot *keep = lyr_root_new(looked_at);
    for (int i = 0; i < TRIES; i++) {
        LyrArr *array = lyr_alloc_array(&words_desc, ELEMENTS);
        if ((uintptr_t)array == (uintptr_t)co + LYR_GC_GRANULE) {
            tail = lyr_root_new(array);
            lyr_root_free(keep);
            return co;
        }
        LYR_WRITE_BARRIER(looked_at, &LYR_ARR_DATA(looked_at, LyrArr *)[i], array);
    }
    lyr_root_free(keep);
    return NULL;
}

static int64_t program(void) {
    CHECK(sizeof(LyrCoro) > LINE - LYR_GC_GRANULE + 16);
    CHECK(offsetof(LyrCoro, status) + sizeof(int) <= LYR_GC_GRANULE + offsetof(LyrArr, data) + ELEMENTS * sizeof(int64_t));
    lyr_register_stack(stale, stale + 1);
    LyrCoro *co = NULL;
    for (int attempt = 0; attempt < ATTEMPTS && co == NULL; attempt++) {
        CHECK(lay_out());
        wipe_stack();
        collect();
        co = cover_the_tail();  /* not there: a stale word kept the coroutine, so another one */
        if (co == NULL) lyr_root_free(neighbour);
    }
    CHECK(co != NULL);
    co->ctx.sp = (void *)(uintptr_t)0x68;
    co->ctx.base = (void *)(uintptr_t)0x1000;
    co->ctx.on_cpu = 0;
    co->mapping = (void *)(uintptr_t)1;
    co->status = 1;

    stale[0] = (uintptr_t)co;
    collect();
    stale[0] = 0;
    lyr_gc_collect();

    CHECK(lyr_root_get(neighbour) != NULL && lyr_root_get(tail) != NULL);
    lyr_println(lyr_str_from_cstr("stale start ok"));
    return 0;
}

int main(int argc, char **argv) {
    young = argc > 1 && strcmp(argv[1], "young") == 0;
    return lyr_run_main(argc, argv, program);
}
