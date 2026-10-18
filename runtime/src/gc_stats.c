/* The collector's numbers (13 M11 P12; design 11 H5): every pause, the collections young and full,
 * the heap and what lives in it — for measurement point 5 (bench/gc), and for hosts once M14 gives
 * them the header's promise. One recorder for either collector: the own one records a pause from
 * the stop's request to the restart, Boehm's from its world's stop to its start. Every pause is
 * kept, eight bytes each, so that the percentiles are exact. */
#include "lyr/gc.h"
#include "internal.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

static atomic_flag guard = ATOMIC_FLAG_INIT;
static uint64_t *pauses;
static size_t pause_count, pause_capacity, young_count;
static uint64_t pause_total, pause_max;

static void take_guard(void) {
    while (atomic_flag_test_and_set_explicit(&guard, memory_order_acquire)) {}
}

static void give_guard(void) { atomic_flag_clear_explicit(&guard, memory_order_release); }

void lyr_gc_record_pause(uint64_t ns, int young) {
    take_guard();
    if (pause_count == pause_capacity) {
        size_t capacity = pause_capacity ? pause_capacity * 2 : 1024;
        uint64_t *grown = realloc(pauses, capacity * sizeof *grown);
        if (grown != NULL) {
            pauses = grown;
            pause_capacity = capacity;
        }
    }
    /* without room the pause still counts, only its place among the percentiles is lost */
    if (pause_count < pause_capacity) pauses[pause_count] = ns;
    pause_count++;
    young_count += young != 0;
    pause_total += ns;
    if (ns > pause_max) pause_max = ns;
    give_guard();
}

static int ascending(const void *a, const void *b) {
    uint64_t x = *(const uint64_t *)a, y = *(const uint64_t *)b;
    return x < y ? -1 : x > y;
}

void lyr_gc_stats(LyrGcStats *out) {
    memset(out, 0, sizeof *out);
    take_guard();
    size_t kept = pause_count < pause_capacity ? pause_count : pause_capacity;
    uint64_t *sorted = kept > 0 ? malloc(kept * sizeof *sorted) : NULL;
    if (sorted != NULL) memcpy(sorted, pauses, kept * sizeof *sorted);
    out->collections = pause_count;
    out->young = young_count;
    out->full = pause_count - young_count;
    out->pause_total_ns = pause_total;
    out->pause_max_ns = pause_max;
    give_guard();
    if (sorted != NULL) {
        qsort(sorted, kept, sizeof *sorted, ascending);
        out->pause_p50_ns = sorted[(kept - 1) / 2];
        out->pause_p99_ns = sorted[(kept - 1) * 99 / 100];
        for (size_t i = 0; i < kept; i++) {
            size_t bucket = 63 - (size_t)__builtin_clzll(sorted[i] | 1);
            if (bucket < sizeof out->pause_log2 / sizeof out->pause_log2[0]) out->pause_log2[bucket]++;
        }
        free(sorted);
    }
    out->heap_bytes = lyr_gc_heap_size();
    out->live_bytes = lyr_gc_live_bytes();
}

int64_t lyr_gc_stat(int64_t which) {
    if (which == LYR_GC_STAT_RESET) {
        take_guard();
        pause_count = young_count = 0;
        pause_total = pause_max = 0;
        give_guard();
        return 0;
    }
    LyrGcStats stats;
    lyr_gc_stats(&stats);
    switch (which) {
    case LYR_GC_STAT_COLLECTIONS: return (int64_t)stats.collections;
    case LYR_GC_STAT_YOUNG: return (int64_t)stats.young;
    case LYR_GC_STAT_FULL: return (int64_t)stats.full;
    case LYR_GC_STAT_PAUSE_TOTAL_NS: return (int64_t)stats.pause_total_ns;
    case LYR_GC_STAT_PAUSE_MAX_NS: return (int64_t)stats.pause_max_ns;
    case LYR_GC_STAT_PAUSE_P50_NS: return (int64_t)stats.pause_p50_ns;
    case LYR_GC_STAT_PAUSE_P99_NS: return (int64_t)stats.pause_p99_ns;
    case LYR_GC_STAT_HEAP_BYTES: return (int64_t)stats.heap_bytes;
    case LYR_GC_STAT_LIVE_BYTES: return (int64_t)stats.live_bytes;
    default: return -1;
    }
}
