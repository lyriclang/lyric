/* Stage 1 of the collector contract: the Boehm–Demers–Weiser collector behind lyr/gc.h. */
#include "lyr/gc.h"

#include <gc.h>

void lyr_gc_init(void) {
    GC_INIT();
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
