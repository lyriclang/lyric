/* The poller (06 N6 S2). Expected: a wait of 0 finds nothing and returns at once; a wait of 20 ms
 * returns after 20 ms at the earliest; a wake before a wait is kept — the wait returns at once —
 * and two wakes before one wait count as one; a wake from another thread ends a wait without end;
 * a wait of 100 ms while another thread allocates — on Linux the collections stop the waiting
 * thread with signals — returns no sooner, and not woken. Prints "poll ok". With the argument
 * "nofds" (not on Windows) the process has no descriptors left for its poller: a panic, RT0015. */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <sys/resource.h>
#endif

enum { MS = 1000000 };

static const LyrDesc node_desc = { .size = 32, .name = "test.Node" };

static LyrPoller *waiting;

static void wake_later(void) {
    CHECK(lyr_thread_attach() == 0);
    CHECK(lyr_poller_wait(lyr_poller_current(), 30 * MS) == 0);  /* this thread's own poller: a sleep */
    lyr_poller_wake(waiting);
    lyr_thread_detach();
}

static void allocate(void) {
    CHECK(lyr_thread_attach() == 0);
    for (int i = 0; i < 400000; i++) (void)lyr_alloc(&node_desc);
    lyr_thread_detach();
}

static void wait_without_end(void) {
    int64_t start = lyr_clock_monotonic_ns();
    CHECK(lyr_poller_wait(waiting, -1) == 1);
    CHECK(lyr_clock_monotonic_ns() - start >= 20 * MS);  /* the waker slept 30 ms first */
}

static void wait_through_collections(void) {
    int64_t start = lyr_clock_monotonic_ns();
    CHECK(lyr_poller_wait(waiting, 100 * MS) == 0);
    CHECK(lyr_clock_monotonic_ns() - start >= 100 * MS);
}

#ifdef _WIN32
static DWORD WINAPI waker_entry(LPVOID unused) { (void)unused; wake_later(); return 0; }
static DWORD WINAPI allocator_entry(LPVOID unused) { (void)unused; allocate(); return 0; }
static void run_beside(LPTHREAD_START_ROUTINE entry, void (*meanwhile)(void)) {
    HANDLE thread = CreateThread(NULL, 0, entry, NULL, 0, NULL);
    CHECK(thread != NULL);
    meanwhile();
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
}
#else
static void *waker_entry(void *unused) { (void)unused; wake_later(); return NULL; }
static void *allocator_entry(void *unused) { (void)unused; allocate(); return NULL; }
static void run_beside(void *(*entry)(void *), void (*meanwhile)(void)) {
    pthread_t thread;
    CHECK(pthread_create(&thread, NULL, entry, NULL) == 0);
    meanwhile();
    CHECK(pthread_join(thread, NULL) == 0);
}
#endif

static int64_t program(void) {
#ifndef _WIN32
    if (lyr_argc() > 1 && strcmp(lyr_argv()[1], "nofds") == 0) {
        struct rlimit none;
        CHECK(getrlimit(RLIMIT_NOFILE, &none) == 0);
        none.rlim_cur = 0;
        CHECK(setrlimit(RLIMIT_NOFILE, &none) == 0);
        (void)lyr_poller_current();
        return 1;
    }
#endif
    waiting = lyr_poller_current();
    CHECK(lyr_poller_current() == waiting);

    int64_t start = lyr_clock_monotonic_ns();
    CHECK(lyr_poller_wait(waiting, 0) == 0);
    CHECK(lyr_poller_wait(waiting, 20 * MS) == 0);
    CHECK(lyr_clock_monotonic_ns() - start >= 20 * MS);

    lyr_poller_wake(waiting);
    CHECK(lyr_poller_wait(waiting, -1) == 1);
    lyr_poller_wake(waiting);
    lyr_poller_wake(waiting);
    CHECK(lyr_poller_wait(waiting, 0) == 1);
    CHECK(lyr_poller_wait(waiting, 0) == 0);

    run_beside(waker_entry, wait_without_end);
    size_t before = lyr_gc_collections();
    run_beside(allocator_entry, wait_through_collections);
    CHECK(lyr_gc_collections() > before);

    lyr_println(lyr_str_from_cstr("poll ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
