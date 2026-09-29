/* Arrays (01 V10).
 * Expected: elements start 16-byte aligned right after the length; struct elements lie
 * contiguously; a new array is zeroed whether or not its elements hold references; an empty array
 * has length 0; an array of references keeps its elements alive across a collection.
 * Prints "arrays ok". */
#include "lyr/lyr.h"
#include "check.h"

#include <stddef.h>

typedef struct Vec3 { double x, y, z; } Vec3;
static const LyrDesc vec3_array = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = sizeof(Vec3), .name = "test.Vec3[]" };

static const uint64_t ref_elem[] = { 1u };
static const LyrDesc str_array = { .size = (uint32_t)offsetof(LyrArr, data),
                                   .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS, .elem_size = sizeof(LyrStr *),
                                   .refmap_words = 1, .refmap = ref_elem, .name = "std.core.string[]" };

static int64_t program(void) {
    CHECK(offsetof(LyrArr, data) == 16);

    LyrArr *vs = lyr_alloc_array(&vec3_array, 1000);
    CHECK(vs->len == 1000 && vs->header.desc == &vec3_array);
    CHECK(((uintptr_t)vs->data % 16) == 0);
    Vec3 *v = LYR_ARR_DATA(vs, Vec3);
    for (int i = 0; i < 1000; i++) CHECK(v[i].x == 0.0 && v[i].y == 0.0 && v[i].z == 0.0);
    CHECK((char *)&v[1] - (char *)&v[0] == sizeof(Vec3));   /* contiguous, no per-element header */
    v[999].z = 1.5;

    LyrArr *empty = lyr_alloc_array(&vec3_array, 0);
    CHECK(empty->len == 0);

    LyrArr *names = lyr_alloc_array(&str_array, 3);
    LyrStr **slot = LYR_ARR_DATA(names, LyrStr *);
    CHECK(slot[0] == NULL && slot[1] == NULL && slot[2] == NULL);
    for (int i = 0; i < 3; i++) LYR_WRITE_BARRIER(names, &slot[i], lyr_str_from_int(i * 11));
    for (int i = 0; i < 200000; i++) (void)lyr_str_from_int(i);   /* churn */
    lyr_gc_collect();
    CHECK(lyr_str_eq(slot[0], lyr_str_from_int(0)));
    CHECK(lyr_str_eq(slot[1], lyr_str_from_int(11)));
    CHECK(lyr_str_eq(slot[2], lyr_str_from_int(22)));
    CHECK(v[999].z == 1.5);

    lyr_println(lyr_str_from_cstr("arrays ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
