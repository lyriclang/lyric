/* Stage 1 of the collector contract: the Boehm–Demers–Weiser collector behind lyr/gc.h.
 * Objects whose descriptor has no references are allocated "atomic": Boehm neither scans them nor
 * zeroes them, so they are zeroed here where the contract promises it. */
#include "lyr/gc.h"
#include "lyr/panic.h"

#include <gc.h>
#include <string.h>

static int initialized;

void lyr_gc_init(void) {
    if (initialized) return;
    GC_INIT();
    initialized = 1;
}

static void *allocate(size_t bytes, int has_refs, int zero) {
    void *memory = has_refs ? GC_MALLOC(bytes) : GC_MALLOC_ATOMIC(bytes);
    if (LYR_UNLIKELY(memory == NULL)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating %zu bytes (heap %zu bytes)",
                  bytes, (size_t)GC_get_heap_size());
    }
    if (!has_refs && zero) memset(memory, 0, bytes);
    return memory;
}

void *lyr_alloc(const LyrDesc *desc) {
    LyrObj *object = allocate(desc->size, (desc->flags & LYR_DESC_HAS_REFS) != 0, 1);
    object->desc = desc;
    return object;
}

LyrArr *lyr_alloc_array(const LyrDesc *desc, int64_t count) {
    if (LYR_UNLIKELY(count < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "array length %lld is negative", (long long)count);
    }
    size_t fixed = offsetof(LyrArr, data);
    if (LYR_UNLIKELY(desc->elem_size != 0 && (uint64_t)count > (SIZE_MAX - fixed) / desc->elem_size)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "an array of %lld elements of %u bytes does not fit in memory",
                  (long long)count, desc->elem_size);
    }
    LyrArr *array = allocate(fixed + (size_t)count * desc->elem_size, (desc->flags & LYR_DESC_HAS_REFS) != 0, 1);
    array->header.desc = desc;
    array->len = count;
    return array;
}

LyrStr *lyr_alloc_string(int64_t len) {
    if (LYR_UNLIKELY(len < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "string length %lld is negative", (long long)len);
    }
    size_t fixed = offsetof(LyrStr, bytes) + 1;
    if (LYR_UNLIKELY((uint64_t)len > SIZE_MAX - fixed)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "a string of %lld bytes does not fit in memory", (long long)len);
    }
    LyrStr *string = allocate(fixed + (size_t)len, 0, 0);
    string->header.desc = &lyr_desc_string;
    string->len = len;
    string->bytes[len] = '\0';
    return string;
}

void lyr_gc_collect(void) {
    GC_gcollect();
}

size_t lyr_gc_heap_size(void) {
    return GC_get_heap_size();
}

size_t lyr_gc_collections(void) {
    return (size_t)GC_get_gc_no();
}

void lyr_gc_set_heap_limit(size_t bytes) {
    GC_set_max_heap_size(bytes);
}
