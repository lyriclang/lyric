/* Allocation of every shape (13 M11 S1, P2 and P3): small objects, medium ones that do not fit
 * where the small ones bump (Immix's overflow), large ones (page by page) and one larger than half
 * a chunk; arrays of every length around the granule and the line; strings. Under either
 * collector.
 * Expected: each comes back aligned to 16, zeroed, with its header and its length set; a pattern
 * written into each is intact after all were made (no two overlap); the thread's allocation count
 * grows by exactly the allocations made, and its bytes by at least what they asked. Prints
 * "shapes ok", exit 0. */
#include "lyr/lyr.h"
#include "check.h"

#include <stddef.h>
#include <string.h>

typedef struct Small { LyrObj header; int64_t a, b; } Small;
typedef struct Medium { LyrObj header; uint8_t bytes[1000]; } Medium;
typedef struct Large { LyrObj header; uint8_t bytes[20000]; } Large;

static const LyrDesc small_desc = { .size = sizeof(Small), .name = "test.Small" };
static const LyrDesc medium_desc = { .size = sizeof(Medium), .name = "test.Medium" };
static const LyrDesc large_desc = { .size = sizeof(Large), .name = "test.Large" };
static const LyrDesc bytes_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = 1, .name = "test.Bytes" };
static const uint64_t ref_elem[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = ref_elem,
                                   .name = "test.Object[]" };

enum { ROUNDS = 3000 };
/* each round: a small, a medium, a byte array, a string; every 100th round a large one */
enum { PER_ROUND = 4, KEPT = ROUNDS * PER_ROUND + ROUNDS / 100 };

static int zero(const void *at, size_t bytes) {
    const uint8_t *p = at;
    for (size_t i = 0; i < bytes; i++) if (p[i] != 0) return 0;
    return 1;
}

static int filled(const void *at, size_t bytes, uint8_t value) {
    const uint8_t *p = at;
    for (size_t i = 0; i < bytes; i++) if (p[i] != value) return 0;
    return 1;
}

static int aligned(const void *at) { return ((uintptr_t)at & 15) == 0; }

static int64_t program(void) {
    int64_t count_before = lyr_gc_thread_allocations();
    int64_t bytes_before = lyr_gc_thread_allocated_bytes();
    int64_t made = 0;
    uint64_t asked = 0;

    /* Everything made stays reachable through this array, so a collector that collects (S2) keeps
     * it: the test asks the same of every stage. */
    LyrArr *kept = lyr_alloc_array(&refs_desc, KEPT);
    made++;
    asked += offsetof(LyrArr, data) + (size_t)KEPT * sizeof(void *);
    CHECK(kept->len == KEPT && zero(kept->data, (size_t)KEPT * sizeof(void *)));
    void **slots = (void **)kept->data;
    int64_t k = 0;

    for (int64_t i = 0; i < ROUNDS; i++) {
        uint8_t v = (uint8_t)(i * 7 + 1);

        Small *s = lyr_alloc(&small_desc);
        CHECK(aligned(s) && s->header.desc == &small_desc && s->a == 0 && s->b == 0);
        s->a = i;
        s->b = -i;
        LYR_WRITE_BARRIER(kept, &slots[k], s);
        k++;

        Medium *m = lyr_alloc(&medium_desc);
        CHECK(aligned(m) && m->header.desc == &medium_desc && zero(m->bytes, sizeof m->bytes));
        memset(m->bytes, v, sizeof m->bytes);
        LYR_WRITE_BARRIER(kept, &slots[k], m);
        k++;

        /* lengths around a granule and a line, and up to a medium object */
        int64_t len = (i * 37) % 3000;
        LyrArr *a = lyr_alloc_array(&bytes_desc, len);
        CHECK(aligned(a) && a->header.desc == &bytes_desc && a->len == len && zero(a->data, (size_t)len));
        memset(a->data, v, (size_t)len);
        LYR_WRITE_BARRIER(kept, &slots[k], a);
        k++;

        int64_t chars = i % 300;
        LyrStr *str = lyr_alloc_string(chars);
        CHECK(aligned(str) && str->header.desc == &lyr_desc_string && str->len == chars && str->bytes[chars] == 0);
        memset(str->bytes, 'a' + (int)(i % 26), (size_t)chars);
        LYR_WRITE_BARRIER(kept, &slots[k], str);
        k++;

        made += PER_ROUND;
        asked += sizeof(Small) + sizeof(Medium) + offsetof(LyrArr, data) + (size_t)len + offsetof(LyrStr, bytes) + (size_t)chars + 1;

        if (i % 100 == 0) {
            Large *l = lyr_alloc(&large_desc);
            CHECK(aligned(l) && l->header.desc == &large_desc && zero(l->bytes, sizeof l->bytes));
            memset(l->bytes, v, sizeof l->bytes);
            LYR_WRITE_BARRIER(kept, &slots[k], l);
            k++;
            made++;
            asked += sizeof(Large);
        }
    }
    CHECK(k == KEPT);

    /* one object larger than half a chunk: chunks of its own */
    LyrArr *huge = lyr_alloc_array(&bytes_desc, 3 << 20);
    made++;
    asked += offsetof(LyrArr, data) + ((size_t)3 << 20);
    CHECK(aligned(huge) && huge->len == (3 << 20) && zero(huge->data, (size_t)3 << 20));
    memset(huge->data, 0x5a, (size_t)3 << 20);

    CHECK(lyr_gc_thread_allocations() - count_before == made);
    CHECK((uint64_t)(lyr_gc_thread_allocated_bytes() - bytes_before) >= asked);

    /* every pattern intact: no two objects overlap */
    k = 0;
    for (int64_t i = 0; i < ROUNDS; i++) {
        uint8_t v = (uint8_t)(i * 7 + 1);
        Small *s = slots[k++];
        CHECK(s->a == i && s->b == -i);
        Medium *m = slots[k++];
        CHECK(filled(m->bytes, sizeof m->bytes, v));
        LyrArr *a = slots[k++];
        CHECK(a->len == (i * 37) % 3000 && filled(a->data, (size_t)a->len, v));
        LyrStr *str = slots[k++];
        CHECK(str->len == i % 300 && filled(str->bytes, (size_t)str->len, (uint8_t)('a' + (int)(i % 26))) && str->bytes[str->len] == 0);
        if (i % 100 == 0) {
            Large *l = slots[k++];
            CHECK(filled(l->bytes, sizeof l->bytes, v));
        }
    }
    CHECK(filled(huge->data, (size_t)3 << 20, 0x5a));

    lyr_println(lyr_str_from_cstr("shapes ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
