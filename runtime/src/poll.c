/* The poller (lyr/poll.h): epoll and an eventfd on Linux, a kqueue and a user event on macOS and
 * the BSDs, an auto-reset event on Windows. A signal that interrupts a wait — on Linux the
 * collector stops threads with signals — does not end it: it goes on until its deadline. */
#include "lyr/poll.h"
#include "lyr/panic.h"
#include "lyr/time.h"

#include <stdlib.h>
#include <string.h>

/* A wait's deadline on the monotonic clock; -1 for none — a negative timeout, or one past the
 * clock's range. */
static int64_t deadline_of(int64_t timeout_ns) {
    if (timeout_ns < 0) return -1;
    int64_t now = lyr_clock_monotonic_ns();
    return timeout_ns > INT64_MAX - now ? -1 : now + timeout_ns;
}

static int passed(int64_t deadline) {
    return deadline >= 0 && lyr_clock_monotonic_ns() >= deadline;
}

#ifndef _WIN32
#  include <unistd.h>
/* Reads a watched descriptor empty: it is non-blocking, so the reads stop where nothing is left. */
static void drain(int fd) {
    char bytes[64];
    while (read(fd, bytes, sizeof bytes) > 0) {}
}
#endif

#if defined(_WIN32) || defined(__linux__)
/* What is left until a deadline in whole milliseconds, at most `cap`: rounded up, so that the
 * waits that count in milliseconds never end early. */
static int64_t left_ms(int64_t deadline, int64_t cap) {
    int64_t left = deadline - lyr_clock_monotonic_ns();
    if (left <= 0) return 0;
    int64_t ms = left / 1000000 + (left % 1000000 != 0);
    return ms > cap ? cap : ms;
}
#endif

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>

struct LyrPoller {
    HANDLE wake;
};

static LyrPoller *lyr_poller_open(void) {
    LyrPoller *poller = calloc(1, sizeof *poller);
    if (poller == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory making a poller");
    poller->wake = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (poller->wake == NULL) lyr_panic(LYR_RT_SYSTEM, "the system refused a poller (error %lu)", (unsigned long)GetLastError());
    return poller;
}

int lyr_poller_wait(LyrPoller *poller, int64_t timeout_ns) {
    int64_t deadline = deadline_of(timeout_ns);
    for (;;) {
        DWORD ms = deadline < 0 ? INFINITE : (DWORD)left_ms(deadline, INFINITE - 1);
        DWORD result = WaitForSingleObject(poller->wake, ms);
        if (result == WAIT_OBJECT_0) return 1;
        if (result != WAIT_TIMEOUT) lyr_panic(LYR_RT_SYSTEM, "a poller's wait failed (error %lu)", (unsigned long)GetLastError());
        if (passed(deadline)) return 0;
    }
}

void lyr_poller_wake(LyrPoller *poller) {
    SetEvent(poller->wake);
}

static void lyr_poller_close(LyrPoller *poller) {
    CloseHandle(poller->wake);
}

#elif defined(__linux__)
#  include <errno.h>
#  include <limits.h>
#  include <sys/epoll.h>
#  include <sys/eventfd.h>
#  include <unistd.h>

struct LyrPoller {
    int epoll;
    int wake;
    int watched;  /* a descriptor whose readiness wakes it too, or -1 */
};

static LyrPoller *lyr_poller_open(void) {
    LyrPoller *poller = calloc(1, sizeof *poller);
    if (poller == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory making a poller");
    poller->epoll = epoll_create1(EPOLL_CLOEXEC);
    if (poller->epoll < 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a poller: %s", strerror(errno));
    poller->watched = -1;
    poller->wake = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    if (poller->wake < 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a poller its wakeup: %s", strerror(errno));
    struct epoll_event event = { .events = EPOLLIN, .data.fd = poller->wake };
    if (epoll_ctl(poller->epoll, EPOLL_CTL_ADD, poller->wake, &event) != 0) {
        lyr_panic(LYR_RT_SYSTEM, "the system refused a poller its wakeup: %s", strerror(errno));
    }
    return poller;
}

int lyr_poller_wait(LyrPoller *poller, int64_t timeout_ns) {
    int64_t deadline = deadline_of(timeout_ns);
    for (;;) {
        int ms = deadline < 0 ? -1 : (int)left_ms(deadline, INT_MAX);
        struct epoll_event events[4];
        int ready = epoll_wait(poller->epoll, events, 4, ms);
        if (ready < 0 && errno != EINTR) lyr_panic(LYR_RT_SYSTEM, "a poller's wait failed: %s", strerror(errno));
        for (int i = 0; i < ready; i++) {
            if (events[i].data.fd == poller->wake) {
                uint64_t count;  /* one read takes every wake since the last */
                while (read(poller->wake, &count, sizeof count) < 0 && errno == EINTR) {}
                return 1;
            }
            if (events[i].data.fd == poller->watched) {
                drain(poller->watched);
                return 1;
            }
        }
        if (passed(deadline)) return 0;
    }
}

void lyr_poller_wake(LyrPoller *poller) {
    uint64_t one = 1;
    while (write(poller->wake, &one, sizeof one) < 0 && errno == EINTR) {}
}

static void lyr_poller_close(LyrPoller *poller) {
    close(poller->wake);
    close(poller->epoll);
}

void lyr_poller_watch(LyrPoller *poller, int fd) {
    struct epoll_event event = { .events = EPOLLIN, .data.fd = fd };
    if (epoll_ctl(poller->epoll, EPOLL_CTL_ADD, fd, &event) != 0) {
        lyr_panic(LYR_RT_SYSTEM, "the system refused a poller a descriptor to watch: %s", strerror(errno));
    }
    poller->watched = fd;
}

#else
#  include <errno.h>
#  include <sys/types.h>
#  include <sys/event.h>
#  include <sys/time.h>
#  include <unistd.h>

struct LyrPoller {
    int queue;
    int watched;  /* a descriptor whose readiness wakes it too, or -1 */
};

enum { WAKE_IDENT = 1 };

static LyrPoller *lyr_poller_open(void) {
    LyrPoller *poller = calloc(1, sizeof *poller);
    if (poller == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory making a poller");
    poller->watched = -1;
    poller->queue = kqueue();
    if (poller->queue < 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a poller: %s", strerror(errno));
    struct kevent event;
    EV_SET(&event, WAKE_IDENT, EVFILT_USER, EV_ADD | EV_CLEAR, 0, 0, NULL);
    if (kevent(poller->queue, &event, 1, NULL, 0, NULL) != 0) {
        lyr_panic(LYR_RT_SYSTEM, "the system refused a poller its wakeup: %s", strerror(errno));
    }
    return poller;
}

int lyr_poller_wait(LyrPoller *poller, int64_t timeout_ns) {
    int64_t deadline = deadline_of(timeout_ns);
    for (;;) {
        struct timespec left_time;
        struct timespec *timeout = NULL;
        if (deadline >= 0) {
            int64_t left = deadline - lyr_clock_monotonic_ns();
            if (left < 0) left = 0;
            left_time.tv_sec = (time_t)(left / 1000000000);
            left_time.tv_nsec = (long)(left % 1000000000);
            timeout = &left_time;
        }
        struct kevent events[4];
        int ready = kevent(poller->queue, NULL, 0, events, 4, timeout);
        if (ready < 0 && errno != EINTR) lyr_panic(LYR_RT_SYSTEM, "a poller's wait failed: %s", strerror(errno));
        for (int i = 0; i < ready; i++) {
            if (events[i].filter == EVFILT_USER && events[i].ident == WAKE_IDENT) return 1;
            if (events[i].filter == EVFILT_READ && (int)events[i].ident == poller->watched) {
                drain(poller->watched);
                return 1;
            }
        }
        if (passed(deadline)) return 0;
    }
}

void lyr_poller_wake(LyrPoller *poller) {
    struct kevent event;
    EV_SET(&event, WAKE_IDENT, EVFILT_USER, 0, NOTE_TRIGGER, 0, NULL);
    while (kevent(poller->queue, &event, 1, NULL, 0, NULL) < 0 && errno == EINTR) {}
}

static void lyr_poller_close(LyrPoller *poller) {
    close(poller->queue);
}

void lyr_poller_watch(LyrPoller *poller, int fd) {
    struct kevent event;
    EV_SET(&event, fd, EVFILT_READ, EV_ADD, 0, 0, NULL);
    if (kevent(poller->queue, &event, 1, NULL, 0, NULL) != 0) {
        lyr_panic(LYR_RT_SYSTEM, "the system refused a poller a descriptor to watch: %s", strerror(errno));
    }
    poller->watched = fd;
}
#endif

static _Thread_local LyrPoller *this_poller;

LyrPoller *lyr_poller_current(void) {
    if (this_poller == NULL) this_poller = lyr_poller_open();
    return this_poller;
}

void lyr_poller_release(void) {
    if (this_poller == NULL) return;
    lyr_poller_close(this_poller);
    free(this_poller);
    this_poller = NULL;
}
