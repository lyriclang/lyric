#include "lyr/array.h"
#include "lyr/gc.h"
#include "lyr/panic.h"

#include <string.h>

LyrArr *lyr_arr_concat(const LyrDesc *desc, const LyrArr *a, const LyrArr *b) {
    /* Two lengths below SIZE_MAX / elem_size each: the sum cannot wrap in int64_t on any
     * target where either array exists, and lyr_alloc_array checks the product. */
    LyrArr *result = lyr_alloc_array(desc, a->len + b->len);
    size_t elem = desc->elem_size;
    memcpy(result->data, a->data, (size_t)a->len * elem);
    memcpy(result->data + (size_t)a->len * elem, b->data, (size_t)b->len * elem);
    return result;
}

LyrArr *lyr_arr_repeat(const LyrDesc *desc, const LyrArr *array, int64_t count) {
    if (LYR_UNLIKELY(count < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "repeat count %lld is negative", (long long)count);
    }
    if (LYR_UNLIKELY(array->len != 0 && count > INT64_MAX / array->len)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "an array of %lld elements repeated %lld times does not fit in memory",
                  (long long)array->len, (long long)count);
    }
    LyrArr *result = lyr_alloc_array(desc, array->len * count);
    size_t chunk = (size_t)array->len * desc->elem_size;
    for (int64_t i = 0; i < count; i++) memcpy(result->data + (size_t)i * chunk, array->data, chunk);
    return result;
}
