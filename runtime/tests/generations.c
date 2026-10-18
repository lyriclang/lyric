/* Generations (13 M11 P8): young objects stored into old ones — objects a full collection left
 * alive — through every form of the barrier: a reference field, a struct value stored whole, a
 * span moved into an array, an element of a large array; and young objects that coroutines which
 * ran keep on their stacks alone — coroutines that yielded, and the inner one of a chain that
 * parked, which writes nothing of its own: only the switch marks its card. A young collection
 * follows; a weak reference on each young object says whether it died. Expected: under the own
 * collector the collection is young and every young object survives it, its value intact —
 * without the cards the old objects are not read again and the young ones die, and without the
 * switch's card the parked coroutine's does. A second young collection and a full one keep them.
 * Under Boehm every collection is full and the same holds. Prints "generations ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

enum { N = 100, LARGE = 2000, KEEPERS = 8 };

typedef struct Box { LyrObj header; int64_t value; } Box;
static const LyrDesc box_desc = { .size = sizeof(Box), .name = "test.Box" };

typedef struct Pair { Box *first; Box *second; } Pair;

typedef struct Holder { LyrObj header; Box *field; Pair pair; } Holder;
static const uint64_t holder_refs[] = { (1u << 1) | (1u << 2) | (1u << 3) };
static const LyrDesc holder_desc = { .size = sizeof(Holder), .flags = LYR_DESC_HAS_REFS,
                                     .refmap_words = 1, .refmap = holder_refs, .name = "test.Holder" };

static const uint64_t element_refs[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = element_refs,
                                   .name = "test.Object[]" };

static const LyrDesc coroutine_desc = { .size = 0, .flags = LYR_DESC_CORO, .name = "test.Coroutine" };

typedef struct BoxSlice { Box **ptr; int64_t len; } BoxSlice;

/* the old objects, rooted: the holders, an array a span is moved into, a large array */
static LyrRoot *holders_root, *span_root, *large_root;

static LyrWeak *field_weaks[N], *first_weaks[N], *second_weaks[N], *span_weaks[N], *large_weak;
static LyrWeak *keeper_weaks[KEEPERS];
static LyrWeak *parked_weak;

static Box *new_box(int64_t value) {
    Box *box = lyr_alloc(&box_desc);
    box->value = value;
    return box;
}

NOINLINE static void make_old(void) {
    LyrArr *holders = lyr_alloc_array(&refs_desc, N);
    holders_root = lyr_root_new(holders);
    for (int i = 0; i < N; i++) {
        Holder *holder = lyr_alloc(&holder_desc);
        LYR_WRITE_BARRIER(holders, &LYR_ARR_DATA(holders, Holder *)[i], holder);
    }
    span_root = lyr_root_new(lyr_alloc_array(&refs_desc, N));
    large_root = lyr_root_new(lyr_alloc_array(&refs_desc, LARGE));
}

/* Each young box referenced from an old object only — the young array the span comes from dies. */
NOINLINE static void store_young(void) {
    LyrArr *holders = lyr_root_get(holders_root);
    for (int i = 0; i < N; i++) {
        Holder *holder = LYR_ARR_DATA(holders, Holder *)[i];
        Box *field = new_box(i);
        field_weaks[i] = lyr_weak_new(field, NULL, NULL);
        LYR_WRITE_BARRIER(holder, &holder->field, field);
        Pair pair = { new_box(1000 + i), new_box(2000 + i) };
        first_weaks[i] = lyr_weak_new(pair.first, NULL, NULL);
        second_weaks[i] = lyr_weak_new(pair.second, NULL, NULL);
        LYR_WRITE_BARRIER_VALUE(holder, &holder->pair, pair);
    }
    LyrArr *fresh = lyr_alloc_array(&refs_desc, N);
    for (int i = 0; i < N; i++) {
        Box *box = new_box(3000 + i);
        span_weaks[i] = lyr_weak_new(box, NULL, NULL);
        LYR_WRITE_BARRIER(fresh, &LYR_ARR_DATA(fresh, Box *)[i], box);
    }
    LyrArr *span = lyr_root_get(span_root);
    BoxSlice into = { LYR_ARR_DATA(span, Box *), N }, from = { LYR_ARR_DATA(fresh, Box *), N };
    LYR_SLICE_MOVE(into, from, N);
    LyrArr *large = lyr_root_get(large_root);
    Box *box = new_box(4000);
    large_weak = lyr_weak_new(box, NULL, NULL);
    LYR_WRITE_BARRIER(large, &LYR_ARR_DATA(large, Box *)[LARGE * 3 / 4], box);
}

/* A coroutine made before the full collection; resumed after it, it keeps a young box on its
 * stack alone, then checks it. */
static void keeper_body(void *arg) {
    intptr_t id = (intptr_t)arg;
    lyr_coro_yield();
    Box *box = new_box(5000 + id);
    keeper_weaks[id] = lyr_weak_new(box, NULL, NULL);
    lyr_coro_yield();
    CHECK(box->value == 5000 + id);
}

/* The inner coroutine of a chain, resumed after the full collection: a young collection while it
 * runs clears its card, then it keeps a young box on its stack alone and parks. */
static void inner_body(void *arg) {
    (void)arg;
    lyr_gc_collect_young();
    Box *box = new_box(6000);
    parked_weak = lyr_weak_new(box, NULL, NULL);
    lyr_coro_park();
    CHECK(box->value == 6000);
}

static void outer_body(void *arg) { lyr_coro_resume(arg); }

static void parked_chain(void) {
    LyrCoro *inner = lyr_coro_new(&coroutine_desc, inner_body, NULL, 0);
    LyrCoro *outer = lyr_coro_new(&coroutine_desc, outer_body, inner, 0);
    lyr_gc_collect();  /* full: both old */
    size_t young = lyr_gc_young_collections();
    lyr_coro_resume(outer);
    CHECK(lyr_coro_status(outer) == LYR_CORO_PARKED);
    wipe_stack();
    lyr_gc_collect_young();
#ifndef LYR_GC_BOEHM
    CHECK(lyr_gc_young_collections() == young + 2);
#else
    (void)young;
#endif
    CHECK(lyr_weak_get(parked_weak) != NULL);
    lyr_coro_resume(outer);
    CHECK(lyr_coro_status(outer) == LYR_CORO_DONE && lyr_coro_status(inner) == LYR_CORO_DONE);
}

static void check_alive(void) {
    LyrArr *holders = lyr_root_get(holders_root);
    LyrArr *span = lyr_root_get(span_root);
    LyrArr *large = lyr_root_get(large_root);
    for (int i = 0; i < N; i++) {
        CHECK(lyr_weak_get(field_weaks[i]) != NULL);
        CHECK(lyr_weak_get(first_weaks[i]) != NULL);
        CHECK(lyr_weak_get(second_weaks[i]) != NULL);
        CHECK(lyr_weak_get(span_weaks[i]) != NULL);
        Holder *holder = LYR_ARR_DATA(holders, Holder *)[i];
        CHECK(holder->field->value == i);
        CHECK(holder->pair.first->value == 1000 + i && holder->pair.second->value == 2000 + i);
        CHECK(LYR_ARR_DATA(span, Box *)[i]->value == 3000 + i);
    }
    CHECK(lyr_weak_get(large_weak) != NULL);
    CHECK(LYR_ARR_DATA(large, Box *)[LARGE * 3 / 4]->value == 4000);
    for (int i = 0; i < KEEPERS; i++) CHECK(lyr_weak_get(keeper_weaks[i]) != NULL);
}

static int64_t program(void) {
    LyrCoro *keepers[KEEPERS];
    for (int i = 0; i < KEEPERS; i++) {
        keepers[i] = lyr_coro_new(&coroutine_desc, keeper_body, (void *)(intptr_t)i, 0);
        lyr_coro_resume(keepers[i]);
    }
    make_old();
    lyr_gc_collect();  /* full: the holders, the arrays and the coroutines are old */

    size_t young = lyr_gc_young_collections();
    for (int i = 0; i < KEEPERS; i++) lyr_coro_resume(keepers[i]);
    store_young();
    wipe_stack();
    lyr_gc_collect_young();
#ifndef LYR_GC_BOEHM
    CHECK(lyr_gc_young_collections() == young + 1);
#else
    (void)young;
#endif
    check_alive();

    lyr_gc_collect_young();  /* promoted: old now, and still alive */
    check_alive();
    lyr_gc_collect();
    check_alive();

    for (int i = 0; i < KEEPERS; i++) {
        lyr_coro_resume(keepers[i]);
        CHECK(lyr_coro_status(keepers[i]) == LYR_CORO_DONE);
    }
    parked_chain();
    lyr_println(lyr_str_from_cstr("generations ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
