/* A switch under collections (01 L4): one thread switches between coroutines as fast as it can —
 * each coroutine holding a chain of objects on its own stack, the thread's own stack holding the
 * coroutines — while another thread runs 300 collections back to back. So collections stop the
 * switching thread at every point of a switch, the moment between the move of what the collector
 * takes for the thread's stack and the move of the stack pointer included, unless the runtime
 * keeps the world from stopping there. Expected: no crash, every chain whole. Prints
 * "coro storm ok". */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L  /* nanosleep under -std=c11 */
#endif
#include "lyr/lyr.h"
#include "check.h"

#include <stdatomic.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <time.h>
#endif

enum { COROUTINES = 4, LINKS = 50, COLLECTIONS = 300 };

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

typedef struct Node { LyrObj header; struct Node *next; int64_t value; } Node;
static const uint64_t node_refs[] = { 1u << 1 };
static const LyrDesc node_desc = { .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS,
                                   .refmap_words = 1, .refmap = node_refs, .name = "test.Node" };

static atomic_int collecting_done;

static void chain_body(void *arg) {
    int64_t id = (int64_t)(intptr_t)arg;
    Node *head = NULL;
    for (int64_t i = 0; i < LINKS; i++) {
        Node *node = lyr_alloc(&node_desc);
        node->value = id * 1000 + i;
        LYR_WRITE_BARRIER(node, &node->next, head);
        head = node;
    }
    while (!atomic_load_explicit(&collecting_done, memory_order_relaxed)) lyr_coro_yield();
    int64_t expected = id * 1000 + LINKS - 1;
    int count = 0;
    for (Node *node = head; node != NULL; node = node->next) {
        CHECK(node->value == expected);
        expected--;
        count++;
    }
    CHECK(count == LINKS);
}

static void pause_briefly(void) {
#ifdef _WIN32
    Sleep(0);
#else
    struct timespec pause = { 0, 100 * 1000 };  /* 100 µs */
    nanosleep(&pause, NULL);
#endif
}

static void collect_repeatedly(void) {
    CHECK(lyr_thread_attach() == 0);
    for (int i = 0; i < COLLECTIONS; i++) {
        lyr_gc_collect();
        pause_briefly();
    }
    atomic_store(&collecting_done, 1);
    lyr_thread_detach();
}

#ifdef _WIN32
static DWORD WINAPI collector(LPVOID argument) { (void)argument; collect_repeatedly(); return 0; }
#else
static void *collector(void *argument) { (void)argument; collect_repeatedly(); return NULL; }
#endif

static int64_t program(void) {
    LyrCoro *coroutines[COROUTINES];
    for (int i = 0; i < COROUTINES; i++) {
        coroutines[i] = lyr_coro_new(&coroutine_desc, chain_body, (void *)(intptr_t)i, 0);
    }
    for (int i = 0; i < COROUTINES; i++) lyr_coro_resume(coroutines[i]);  /* the chains exist */
#ifdef _WIN32
    HANDLE thread = CreateThread(NULL, 0, collector, NULL, 0, NULL);
    CHECK(thread != NULL);
#else
    pthread_t thread;
    CHECK(pthread_create(&thread, NULL, collector, NULL) == 0);
#endif
    int64_t switches = 0;
    int running = COROUTINES;
    while (running > 0) {
        for (int i = 0; i < COROUTINES; i++) {
            if (lyr_coro_status(coroutines[i]) == LYR_CORO_DONE) continue;
            lyr_coro_resume(coroutines[i]);
            switches += 2;
            if (lyr_coro_status(coroutines[i]) == LYR_CORO_DONE) running--;
        }
    }
#ifdef _WIN32
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
#else
    CHECK(pthread_join(thread, NULL) == 0);
#endif
    CHECK(switches > 1000);
    CHECK(lyr_gc_collections() >= COLLECTIONS);
    lyr_println(lyr_str_from_cstr("coro storm ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
