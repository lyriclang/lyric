/* Threads on the heap (01 L6).
 * Expected: four threads attach, each allocates 500 000 objects keeping every 10 000th in its own
 * chain, and detaches; collections run while they allocate; after joining, every chain holds
 * exactly its 50 objects with the right values. Prints "threads ok". */
#include "lyr/lyr.h"
#include "check.h"

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#endif

enum { THREADS = 4, PER_THREAD = 500000, KEEP_EVERY = 10000 };

typedef struct Node { LyrObj header; struct Node *next; int64_t value; } Node;
static const uint64_t node_refs[] = { 1u << 1 };
static const LyrDesc node_desc = { .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS,
                                   .refmap_words = 1, .refmap = node_refs, .name = "test.Node" };

static LyrRoot *chains[THREADS];

static void work(int index) {
    CHECK(lyr_thread_attach() == 0);
    Node *head = NULL;
    for (int64_t i = 0; i < PER_THREAD; i++) {
        Node *node = lyr_alloc(&node_desc);
        node->value = index * 1000000 + i;
        if (i % KEEP_EVERY == 0) {
            LYR_WRITE_BARRIER(node, &node->next, head);
            head = node;
        }
    }
    chains[index] = lyr_root_new(head);
    lyr_thread_detach();
}

#ifdef _WIN32
static DWORD WINAPI entry(LPVOID argument) { work((int)(intptr_t)argument); return 0; }
#else
static void *entry(void *argument) { work((int)(intptr_t)argument); return NULL; }
#endif

static int64_t program(void) {
#ifdef _WIN32
    HANDLE handles[THREADS];
    for (int i = 0; i < THREADS; i++) {
        handles[i] = CreateThread(NULL, 0, entry, (LPVOID)(intptr_t)i, 0, NULL);
        CHECK(handles[i] != NULL);
    }
    WaitForMultipleObjects(THREADS, handles, TRUE, INFINITE);
    for (int i = 0; i < THREADS; i++) CloseHandle(handles[i]);
#else
    pthread_t threads[THREADS];
    for (int i = 0; i < THREADS; i++) CHECK(pthread_create(&threads[i], NULL, entry, (void *)(intptr_t)i) == 0);
    for (int i = 0; i < THREADS; i++) CHECK(pthread_join(threads[i], NULL) == 0);
#endif

    lyr_gc_collect();
    CHECK(lyr_gc_collections() > 0);
    for (int t = 0; t < THREADS; t++) {
        int count = 0;
        int64_t expected = t * 1000000 + (PER_THREAD - KEEP_EVERY);
        for (Node *n = lyr_root_get(chains[t]); n; n = n->next, expected -= KEEP_EVERY) {
            CHECK(n->value == expected);
            count++;
        }
        CHECK(count == PER_THREAD / KEEP_EVERY);
    }

    lyr_println(lyr_str_from_cstr("threads ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
