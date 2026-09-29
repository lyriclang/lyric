/* The control for the TSan profile: the runtime tells TSan that a collection orders the threads
 * (gc_boehm.c), and this program shows that the telling hides nothing else. Two attached threads
 * allocate and then write one plain global without synchronization, with no collection between.
 * Expected under TSan: a data race report on `shared`, exit 66. (Built in the other profiles it
 * just exits 0.) */
#include "lyr/lyr.h"
#include "conservative.h"

#include <pthread.h>

static const LyrDesc box_desc = { .size = sizeof(LyrObj) + 8, .name = "test.Box" };
static int64_t shared;

static void *entry(void *arg) {
    lyr_thread_attach();
    (void)lyr_alloc(&box_desc);
    shared += (int64_t)(intptr_t)arg;
    lyr_thread_detach();
    return NULL;
}

static int64_t program(void) {
    pthread_t a, b;
    pthread_create(&a, NULL, entry, (void *)1);
    pthread_create(&b, NULL, entry, (void *)2);
    pthread_join(a, NULL);
    pthread_join(b, NULL);
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
