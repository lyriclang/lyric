/* A chunk that held a huge object and came free is no fresh memory (13 M11 P1, P7): taken again —
 * for large objects or for blocks — what it hands out is zeroed, as any reused memory is. A huge
 * array filled with 0xA5 dies; then a large array and twelve MiB of small objects, kept alive so
 * that new chunks are taken — the freed ones first —, and every word of them reads zero. Before the
 * fix the freed chunk counted as fresh and handed out the dead array's bytes. Prints "reuse ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

#include <string.h>

static const LyrDesc bytes_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = 1, .name = "test.Bytes" };

typedef struct Box { LyrObj header; struct Box *next; int64_t a, b; } Box;
static const uint64_t box_refs[] = { 1u << 1 };
static const LyrDesc box_desc = { .size = sizeof(Box), .flags = LYR_DESC_HAS_REFS, .refmap_words = 1,
                                  .refmap = box_refs, .name = "test.Box" };

enum { HUGE = 6 << 20, LARGE = 64 << 10, SMALL = 12 << 20 };

NOINLINE static void make_dead_huge(void) {
    LyrArr *huge = lyr_alloc_array(&bytes_desc, HUGE);
    memset(huge->data, 0xA5, HUGE);
}

static int64_t program(void) {
    make_dead_huge();
    wipe_stack();
    lyr_gc_collect();

    LyrArr *large = lyr_alloc_array(&bytes_desc, LARGE);
    for (int64_t i = 0; i < large->len; i++) CHECK(large->data[i] == 0);

    Box *head = NULL;
    for (size_t made = 0; made < SMALL; made += sizeof(Box)) {
        Box *box = lyr_alloc(&box_desc);
        CHECK(box->next == NULL && box->a == 0 && box->b == 0);
        box->a = (int64_t)made;
        LYR_WRITE_BARRIER(box, &box->next, head);
        head = box;
    }
    CHECK(head != NULL && large->len == LARGE);
    lyr_println(lyr_str_from_cstr("reuse ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
