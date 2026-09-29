/* S1 smoke: the runtime archive links, the collector starts, and a churn of short-lived
 * allocations stays within a bounded heap because it is collected.
 * Expected: prints "gc ok" and exits 0; the heap stays under 64 MiB after 2 M allocations of
 * 64 bytes (128 MiB allocated in total) and at least one collection ran. */
#include "lyr/gc.h"

#include <gc.h>
#include <stdio.h>

int main(void) {
    lyr_gc_init();
    unsigned long survivors = 0;
    for (int i = 0; i < 2000000; i++) {
        char *p = GC_MALLOC(64);
        p[0] = (char)i;
        if (p[0] == 7) survivors++;
    }
    size_t heap = lyr_gc_heap_size();
    size_t collections = lyr_gc_collections();
    if (heap > 64u * 1024 * 1024) { fprintf(stderr, "heap grew to %zu bytes\n", heap); return 1; }
    if (collections == 0) { fprintf(stderr, "no collection ran\n"); return 1; }
    printf("gc ok\n");
    return survivors == 0 ? 1 : 0;
}
