/* Marking side by side and alone (13 M11 S9b): a full collection marks with the helpers and ends
 * with every marker waiting; a young collection after it marks on the collecting thread alone, and
 * its stack grows past what a marker hands to a waiting one — four thousand young boxes behind one
 * array. Nobody waits in a young collection: were a packet handed out there, nobody would scan its
 * boxes, their lines would read free and be handed out again, and the next full collection would
 * take the packet up and scan what lies there now. Expected: every box keeps its value through the
 * young collection, through the allocations after it that fill what reads free, and through a full
 * collection. 160 MiB of byte arrays stay alive throughout: a full collection marks side by side
 * only above SHARE_MIN alive (128 MiB). Prints "shared marking ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

enum { BOXES = 4096, ROUNDS = 3, GARBAGE = 200000, BALLAST = 5, BALLAST_BYTES = 32 << 20 };

static const LyrDesc bytes_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = 1, .name = "test.Bytes" };

typedef struct Box { LyrObj header; int64_t value; } Box;
static const LyrDesc box_desc = { .size = sizeof(Box), .name = "test.Box" };

static const uint64_t element_refs[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = element_refs,
                                   .name = "test.Box[]" };

static LyrRoot *boxes_root;

/* young boxes, reachable through the rooted array alone */
NOINLINE static void make_young(int round) {
    LyrArr *boxes = lyr_alloc_array(&refs_desc, BOXES);
    for (int i = 0; i < BOXES; i++) {
        Box *box = lyr_alloc(&box_desc);
        box->value = (int64_t)round * 1000000 + i;
        LYR_WRITE_BARRIER(boxes, &LYR_ARR_DATA(boxes, Box *)[i], box);
    }
    lyr_root_set(boxes_root, boxes);
}

/* boxes nobody keeps: they take the lines a collection left free */
NOINLINE static void make_garbage(void) {
    for (int i = 0; i < GARBAGE; i++) {
        Box *box = lyr_alloc(&box_desc);
        box->value = -1;
    }
}

NOINLINE static void check_values(int round) {
    LyrArr *boxes = lyr_root_get(boxes_root);
    for (int i = 0; i < BOXES; i++) {
        CHECK(LYR_ARR_DATA(boxes, Box *)[i]->value == (int64_t)round * 1000000 + i);
    }
}

static int64_t program(void) {
    boxes_root = lyr_root_new(NULL);
    LyrRoot *ballast[BALLAST];
    for (int i = 0; i < BALLAST; i++) ballast[i] = lyr_root_new(lyr_alloc_array(&bytes_desc, BALLAST_BYTES));
    lyr_gc_collect();  /* full and alone: what it finds alive raises the next ones over SHARE_MIN */
    for (int round = 0; round < ROUNDS; round++) {
        lyr_gc_collect();  /* full: side by side, and every marker waits at its end */
        make_young(round);
        lyr_gc_collect_young();
        check_values(round);
        make_garbage();
        lyr_gc_collect_young();
        check_values(round);
    }
    lyr_gc_collect();
    check_values(ROUNDS - 1);
    lyr_root_free(boxes_root);
    for (int i = 0; i < BALLAST; i++) lyr_root_free(ballast[i]);
    lyr_println(lyr_str_from_cstr("shared marking ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
