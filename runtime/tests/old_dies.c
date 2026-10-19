/* Old objects that die while the program promotes nothing (open point 208): 128 MiB built and made
 * old by a full collection, let go, then 640 MiB of short-lived garbage. The old heap never doubles,
 * so the growth of the old heap alone (13 M11 P8) never collects fully again and the dead stay in
 * use. Expected: a full collection comes after the allocation passes four times what lived through
 * the last one, and the heap in use falls below half of what it held. Prints "old dies ok". */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

enum { OLD = 128 * 1024, PIECE = 1024, GARBAGE = 640 * 1024 };

static const LyrDesc bytes_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = 1, .name = "test.Bytes" };
static const uint64_t element_refs[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = element_refs,
                                   .name = "test.Bytes[]" };

static LyrRoot *old_root;

NOINLINE static void build_old(void) {
    LyrArr *all = lyr_alloc_array(&refs_desc, OLD);
    old_root = lyr_root_new(all);
    for (int i = 0; i < OLD; i++) {
        LyrArr *piece = lyr_alloc_array(&bytes_desc, PIECE);
        piece->data[0] = (uint8_t)i;
        LYR_WRITE_BARRIER(all, &LYR_ARR_DATA(all, LyrArr *)[i], piece);
    }
}

NOINLINE static void make_garbage(void) {
    for (int i = 0; i < GARBAGE; i++) {
        LyrArr *piece = lyr_alloc_array(&bytes_desc, PIECE);
        piece->data[0] = (uint8_t)i;
        LYR_SAFEPOINT();
    }
}

static int64_t program(void) {
    build_old();
    wipe_stack();
    lyr_gc_collect();  /* full: the 128 MiB are old */
    size_t held = lyr_gc_heap_size();
    size_t fulls = lyr_gc_collections() - lyr_gc_young_collections();
    CHECK(held >= (size_t)OLD * PIECE);
    lyr_root_set(old_root, NULL);  /* the old die, and no young collection can tell */
    wipe_stack();
    make_garbage();
    size_t fulls_after = lyr_gc_collections() - lyr_gc_young_collections();
    CHECK(fulls_after > fulls);
    CHECK(lyr_gc_heap_size() < held / 2);
    lyr_root_free(old_root);
    lyr_println(lyr_str_from_cstr("old dies ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
