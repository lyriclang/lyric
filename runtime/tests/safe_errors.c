/* A safe region keeps what the system call inside it left (13 M11 P5): errno, and on Windows the
 * last error — the runtime reads them after the region, as poll.c does after its wait. On Windows a
 * wait that timed out once read as failed with error 0: leaving the region cleared the last error.
 * Another thread collects meanwhile, so that some of the regions are left while a stop is under
 * way. Prints "errors kept". */
#include "lyr/lyr.h"
#include "check.h"

#include <errno.h>
#include <stdatomic.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#endif

enum { REGIONS = 20000, COLLECTIONS = 50 };

static atomic_int done;

static void collect_meanwhile(void) {
    CHECK(lyr_thread_attach() == 0);
    for (int i = 0; i < COLLECTIONS; i++) lyr_gc_collect();
    atomic_store(&done, 1);
    lyr_thread_detach();
}

#ifdef _WIN32
static DWORD WINAPI collector(LPVOID unused) { (void)unused; collect_meanwhile(); return 0; }
#else
static void *collector(void *unused) { (void)unused; collect_meanwhile(); return NULL; }
#endif

static int64_t program(void) {
#ifdef _WIN32
    HANDLE thread = CreateThread(NULL, 0, collector, NULL, 0, NULL);
    CHECK(thread != NULL);
#else
    pthread_t thread;
    CHECK(pthread_create(&thread, NULL, collector, NULL) == 0);
#endif
    for (int i = 0; i < REGIONS || !atomic_load(&done); i++) {
        LYR_SAFEPOINT();
        LYR_GC_ENTER_SAFE();
        errno = 1000 + i % 1000;
#ifdef _WIN32
        SetLastError((DWORD)(2000 + i % 1000));
#endif
        LYR_GC_LEAVE_SAFE();
        CHECK(errno == 1000 + i % 1000);
#ifdef _WIN32
        CHECK(GetLastError() == (DWORD)(2000 + i % 1000));
#endif
    }
    LYR_GC_ENTER_SAFE();
#ifdef _WIN32
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
#else
    CHECK(pthread_join(thread, NULL) == 0);
#endif
    LYR_GC_LEAVE_SAFE();
    lyr_println(lyr_str_from_cstr("errors kept"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
