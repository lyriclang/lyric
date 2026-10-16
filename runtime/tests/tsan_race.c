/* The control for the TSan profile: what the runtime orders, TSan sees ordered — a collection's stop
 * and restart, the collector's locks (the own collector's are real locks; Boehm's the runtime tells
 * TSan about, gc_boehm.c) —, and this program shows that nothing else is hidden. Two attached
 * threads allocate and then write one plain global without synchronization, while both live: they
 * wait for each other on relaxed atomics, which order nothing, before the writes and after them —
 * so no lock the runtime takes, at an allocation or at the thread's end, falls between the writes.
 * Expected under TSan: a data race report on `shared`, exit 66. (Built in the other profiles it
 * just exits 0.) */
#include "lyr/lyr.h"
#include "conservative.h"

#include <pthread.h>
#include <stdatomic.h>

static const LyrDesc box_desc = { .size = sizeof(LyrObj) + 8, .name = "test.Box" };
static int64_t shared;
static atomic_int before, after, first_wrote;

static void meet(atomic_int *count) {
    atomic_fetch_add_explicit(count, 1, memory_order_relaxed);
    while (atomic_load_explicit(count, memory_order_relaxed) < 2) {}
}

static void *entry(void *arg) {
    lyr_thread_attach();
    (void)lyr_alloc(&box_desc);
    meet(&before);
    /* one after the other in time, still unordered: two writes at the very same moment can slip
     * past TSan's shadow, which it updates without a lock */
    if ((intptr_t)arg == 1) {
        shared += 1;
        atomic_store_explicit(&first_wrote, 1, memory_order_relaxed);
    } else {
        while (!atomic_load_explicit(&first_wrote, memory_order_relaxed)) {}
        shared += 2;
    }
    meet(&after);
    lyr_thread_detach();
    return NULL;
}

static int64_t program(void) {
    pthread_t a, b;
    pthread_create(&a, NULL, entry, (void *)1);
    pthread_create(&b, NULL, entry, (void *)2);
    LYR_GC_ENTER_SAFE();
    pthread_join(a, NULL);
    pthread_join(b, NULL);
    LYR_GC_LEAVE_SAFE();
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
