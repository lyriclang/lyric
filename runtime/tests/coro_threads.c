/* Coroutines on several threads (01 L4, L6; 06 G1).
 * Expected: four threads each run eight coroutines in rounds; every coroutine keeps a chain of
 * objects on its own stack across its yields while all threads allocate, so collections stop the
 * threads at any point — in a coroutine, on a thread's own stack, between the two. Every chain is
 * whole at the end, and every stack is released. Prints "coro threads ok". */
#include "lyr/lyr.h"
#include "check.h"

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#endif

enum { THREADS = 4, PER_THREAD = 8, ROUNDS = 10, PER_ROUND = 2000, KEEP_EVERY = 200 };

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

typedef struct Node { LyrObj header; struct Node *next; int64_t value; } Node;
static const uint64_t node_refs[] = { 1u << 1 };
static const LyrDesc node_desc = { .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS,
                                   .refmap_words = 1, .refmap = node_refs, .name = "test.Node" };

static void chain_body(void *arg) {
    int64_t id = (int64_t)(intptr_t)arg;
    Node *head = NULL;
    for (int64_t round = 0; round < ROUNDS; round++) {
        for (int64_t i = 0; i < PER_ROUND; i++) {
            Node *node = lyr_alloc(&node_desc);
            node->value = id * 1000000 + round * PER_ROUND + i;
            if (i % KEEP_EVERY == 0) {
                LYR_WRITE_BARRIER(node, &node->next, head);
                head = node;
            }
        }
        lyr_coro_yield();
    }
    int count = 0;
    int64_t expected = id * 1000000 + (ROUNDS - 1) * PER_ROUND + (PER_ROUND - KEEP_EVERY);
    for (Node *node = head; node != NULL; node = node->next) {
        CHECK(node->value == expected);
        expected -= KEEP_EVERY;
        count++;
    }
    CHECK(count == ROUNDS * PER_ROUND / KEEP_EVERY);
}

static void work(int index) {
    CHECK(lyr_thread_attach() == 0);
    LyrCoro *coroutines[PER_THREAD];
    for (int i = 0; i < PER_THREAD; i++) {
        coroutines[i] = lyr_coro_new(&coroutine_desc, chain_body, (void *)(intptr_t)(index * PER_THREAD + i), 0);
    }
    for (int round = 0; round <= ROUNDS; round++) {
        for (int i = 0; i < PER_THREAD; i++) lyr_coro_resume(coroutines[i]);
    }
    for (int i = 0; i < PER_THREAD; i++) CHECK(lyr_coro_status(coroutines[i]) == LYR_CORO_DONE);
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
    CHECK(lyr_gc_collections() > 0);
    CHECK(lyr_coro_stacks() == 0);
    lyr_println(lyr_str_from_cstr("coro threads ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
