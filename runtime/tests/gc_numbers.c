/* The collector's numbers (13 M11 P12): after collections young and full, lyr_gc_stats agrees with
 * itself and with the counters beside it — as many pauses as collections, young and full summing to
 * them, the percentiles below the maximum and the maximum below the total, the histogram holding
 * every pause, a heap that holds what lives. lyr_gc_stat answers the same numbers one by one.
 * Prints "numbers ok". */
#include "lyr/lyr.h"
#include "check.h"

typedef struct Box { LyrObj header; struct Box *next; int64_t value; } Box;
static const uint64_t box_refs[] = { 1u << 1 };
static const LyrDesc box_desc = { .size = sizeof(Box), .flags = LYR_DESC_HAS_REFS, .refmap_words = 1,
                                  .refmap = box_refs, .name = "test.Box" };

static int64_t program(void) {
    LyrRoot *root = lyr_root_new(NULL);
    for (int round = 0; round < 6; round++) {
        Box *head = lyr_root_get(root);
        for (int i = 0; i < 20000; i++) {
            Box *box = lyr_alloc(&box_desc);
            box->value = i;
            LYR_WRITE_BARRIER(box, &box->next, head);
            head = box;
        }
        lyr_root_set(root, head);
        if (round % 2 == 0) lyr_gc_collect_young();
        else lyr_gc_collect();
    }
    LyrGcStats stats;
    lyr_gc_stats(&stats);
    CHECK(stats.collections >= 6);
#ifndef LYR_GC_BOEHM
    /* Boehm may stop its world for more than its collections: the counts need not agree there */
    CHECK(stats.collections == lyr_gc_collections());
#endif
    CHECK(stats.young + stats.full == stats.collections);
    CHECK(stats.young == lyr_gc_young_collections());
#ifndef LYR_GC_BOEHM
    CHECK(stats.young >= 3 && stats.full >= 3);
#endif
    CHECK(stats.pause_p50_ns <= stats.pause_p99_ns && stats.pause_p99_ns <= stats.pause_max_ns);
    CHECK(stats.pause_max_ns <= stats.pause_total_ns && stats.pause_max_ns > 0);
    uint64_t counted = 0;
    for (size_t i = 0; i < sizeof stats.pause_log2 / sizeof stats.pause_log2[0]; i++) counted += stats.pause_log2[i];
    CHECK(counted == stats.collections);
    CHECK(stats.heap_bytes > 0 && stats.live_bytes > 0 && stats.live_bytes <= stats.heap_bytes);
    CHECK(lyr_gc_stat(LYR_GC_STAT_COLLECTIONS) == (int64_t)stats.collections);
    CHECK(lyr_gc_stat(LYR_GC_STAT_PAUSE_MAX_NS) == (int64_t)stats.pause_max_ns);
    CHECK(lyr_gc_stat(-1) == -1);
    lyr_root_free(root);
    lyr_println(lyr_str_from_cstr("numbers ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
