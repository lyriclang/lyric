/* The poller (lyr/poll.h): epoll and an eventfd on Linux, a kqueue and a user event on macOS and
 * the BSDs, an auto-reset event on Windows. A signal that interrupts a wait — on Linux the
 * collector stops threads with signals — does not end it: it goes on until its deadline.
 *
 * Since M8b S10b it also waits for descriptors — a socket's readiness, one-shot (13 M8b P2): what a
 * wait sees goes into the poller's list of fired tokens, which the scheduler takes after the wait.
 * Every event of a wait is looked at before it returns: a one-shot readiness left unread would be
 * lost, the descriptor disarmed by the system with nobody told. */
#include "lyr/poll.h"
#include "lyr/fs.h"
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

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

/* What is armed on a descriptor: a token each way, 0 for none; and whether the system's poller
 * knows the descriptor (epoll's ADD before its MODs). */
typedef struct {
    int64_t read, write;
    int added;
} Armed;

/* The descriptors' side of a poller, the same on every system: the tokens fired and not taken, and
 * what is armed, by descriptor. */
typedef struct {
    int64_t *fired;
    int64_t count, room;
    Armed *armed;
    int64_t armed_room;
} Ready;

#ifndef _WIN32
static void fire(Ready *r, int64_t token) {
    if (r->count == r->room) {
        int64_t room = r->room == 0 ? 64 : r->room * 2;
        int64_t *grown = realloc(r->fired, (size_t)room * sizeof *grown);
        if (grown == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory keeping a readiness");
        r->fired = grown;
        r->room = room;
    }
    r->fired[r->count++] = token;
}

/* The entry of `fd`, the table grown to hold it. */
static Armed *armed_at(Ready *r, int64_t fd) {
    if (fd >= r->armed_room) {
        int64_t room = r->armed_room == 0 ? 64 : r->armed_room;
        while (room <= fd) room *= 2;
        Armed *grown = realloc(r->armed, (size_t)room * sizeof *grown);
        if (grown == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory arming a descriptor");
        memset(grown + r->armed_room, 0, (size_t)(room - r->armed_room) * sizeof *grown);
        r->armed = grown;
        r->armed_room = room;
    }
    return &r->armed[fd];
}

/* The entry of `fd`, where it has one. */
static Armed *armed_of(Ready *r, int64_t fd) {
    return fd >= 0 && fd < r->armed_room ? &r->armed[fd] : NULL;
}

/* A readiness of `fd`: its armed directions that `readable` and `writable` say are fired and
 * disarmed. Whether one was. */
static int ready_on(Ready *r, int64_t fd, int readable, int writable) {
    Armed *a = armed_of(r, fd);
    if (a == NULL) return 0;
    int any = 0;
    if (a->read && readable) {
        fire(r, a->read);
        a->read = 0;
        any = 1;
    }
    if (a->write && writable) {
        fire(r, a->write);
        a->write = 0;
        any = 1;
    }
    return any;
}

/* What is armed on `fd`, fired, and the entry cleared — the descriptor goes. Whether one was. */
static int fire_all(Ready *r, int64_t fd) {
    Armed *a = armed_of(r, fd);
    if (a == NULL) return 0;
    int any = ready_on(r, fd, 1, 1);
    *a = (Armed){ 0, 0, 0 };
    return any;
}
#endif

static int64_t take(Ready *r, int64_t *into, int64_t n) {
    int64_t k = r->count < n ? r->count : n;
    if (k <= 0) return 0;
    memcpy(into, r->fired, (size_t)k * sizeof *into);
    memmove(r->fired, r->fired + k, (size_t)(r->count - k) * sizeof *into);
    r->count -= k;
    return k;
}

static void free_ready(Ready *r) {
    free(r->fired);
    free(r->armed);
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

/* The events one wait takes at most; the rest stay for the next. */
#define EVENTS 64

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>

struct LyrPoller {
    HANDLE wake;
    Ready ready;
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

/* Sockets wait on Windows with S11 (AFD, the review's M6-29). */
int64_t lyr_poller_arm(LyrPoller *poller, int64_t fd, int64_t interest, int64_t token) {
    (void)poller; (void)fd; (void)interest; (void)token;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

void lyr_poller_forget(LyrPoller *poller, int64_t fd) {
    (void)poller; (void)fd;
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
    Ready ready;
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

/* What is still armed on `fd`, as epoll's events: one-shot always. */
static uint32_t mask_of(const Armed *a) {
    return (a->read ? EPOLLIN : 0) | (a->write ? EPOLLOUT : 0) | EPOLLONESHOT;
}

/* A readiness of an armed descriptor: its fired directions out, the rest armed again — epoll's
 * one-shot disarmed the whole descriptor. Where that fails, the rest is fired too: its waiters
 * try again and see the descriptor's error. */
static int descriptor_ready(LyrPoller *poller, int fd, uint32_t events) {
    uint32_t both = EPOLLERR | EPOLLHUP;
    int any = ready_on(&poller->ready, fd, (events & (EPOLLIN | both)) != 0, (events & (EPOLLOUT | both)) != 0);
    Armed *a = armed_of(&poller->ready, fd);
    if (a != NULL && (a->read || a->write)) {
        struct epoll_event again = { .events = mask_of(a), .data.fd = fd };
        if (epoll_ctl(poller->epoll, EPOLL_CTL_MOD, fd, &again) != 0) any |= ready_on(&poller->ready, fd, 1, 1);
    }
    return any;
}

int lyr_poller_wait(LyrPoller *poller, int64_t timeout_ns) {
    int64_t deadline = deadline_of(timeout_ns);
    for (;;) {
        int ms = deadline < 0 ? -1 : (int)left_ms(deadline, INT_MAX);
        struct epoll_event events[EVENTS];
        int ready = epoll_wait(poller->epoll, events, EVENTS, ms);
        if (ready < 0 && errno != EINTR) lyr_panic(LYR_RT_SYSTEM, "a poller's wait failed: %s", strerror(errno));
        int woken = 0;
        for (int i = 0; i < ready; i++) {
            int fd = events[i].data.fd;
            if (fd == poller->wake) {
                uint64_t count;  /* one read takes every wake since the last */
                while (read(poller->wake, &count, sizeof count) < 0 && errno == EINTR) {}
                woken = 1;
            } else if (fd == poller->watched) {
                drain(poller->watched);
                woken = 1;
            } else if (descriptor_ready(poller, fd, events[i].events)) {
                woken = 1;
            }
        }
        if (woken) return 1;
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

int64_t lyr_poller_arm(LyrPoller *poller, int64_t fd, int64_t interest, int64_t token) {
    if (fd < 0 || fd > INT_MAX) return failure(LYR_IO_INVALID_INPUT, 0);
    Armed *a = armed_at(&poller->ready, fd);
    Armed before = *a;
    if (interest & LYR_POLL_READ) a->read = token;
    if (interest & LYR_POLL_WRITE) a->write = token;
    struct epoll_event event = { .events = mask_of(a), .data.fd = (int)fd };
    int done = epoll_ctl(poller->epoll, a->added ? EPOLL_CTL_MOD : EPOLL_CTL_ADD, (int)fd, &event);
    /* a number closed without a forget and given again is new to epoll */
    if (done != 0 && errno == ENOENT && a->added) done = epoll_ctl(poller->epoll, EPOLL_CTL_ADD, (int)fd, &event);
    if (done != 0) {
        int error = errno;
        *a = before;
        return failure(0, error);
    }
    a->added = 1;
    return 0;
}

void lyr_poller_forget(LyrPoller *poller, int64_t fd) {
    Armed *a = armed_of(&poller->ready, fd);
    if (a == NULL) return;
    if (a->added) (void)epoll_ctl(poller->epoll, EPOLL_CTL_DEL, (int)fd, NULL);
    if (fire_all(&poller->ready, fd)) lyr_poller_wake(poller);
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
    Ready ready;
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
        struct kevent events[EVENTS];
        int ready = kevent(poller->queue, NULL, 0, events, EVENTS, timeout);
        if (ready < 0 && errno != EINTR) lyr_panic(LYR_RT_SYSTEM, "a poller's wait failed: %s", strerror(errno));
        int woken = 0;
        for (int i = 0; i < ready; i++) {
            if (events[i].filter == EVFILT_USER && events[i].ident == WAKE_IDENT) {
                woken = 1;
            } else if (events[i].filter == EVFILT_READ && (int)events[i].ident == poller->watched) {
                drain(poller->watched);
                woken = 1;
            } else if (events[i].filter == EVFILT_READ || events[i].filter == EVFILT_WRITE) {
                /* one filter a direction: EV_ONESHOT disarmed this one alone */
                int64_t fd = (int64_t)events[i].ident;
                if (ready_on(&poller->ready, fd, events[i].filter == EVFILT_READ, events[i].filter == EVFILT_WRITE)) woken = 1;
            }
        }
        if (woken) return 1;
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

int64_t lyr_poller_arm(LyrPoller *poller, int64_t fd, int64_t interest, int64_t token) {
    if (fd < 0) return failure(LYR_IO_INVALID_INPUT, 0);
    Armed *a = armed_at(&poller->ready, fd);
    struct kevent events[2];
    int n = 0;
    if (interest & LYR_POLL_READ) EV_SET(&events[n++], (uintptr_t)fd, EVFILT_READ, EV_ADD | EV_ONESHOT, 0, 0, NULL);
    if (interest & LYR_POLL_WRITE) EV_SET(&events[n++], (uintptr_t)fd, EVFILT_WRITE, EV_ADD | EV_ONESHOT, 0, 0, NULL);
    if (kevent(poller->queue, events, n, NULL, 0, NULL) != 0) return failure(0, errno);
    if (interest & LYR_POLL_READ) a->read = token;
    if (interest & LYR_POLL_WRITE) a->write = token;
    a->added = 1;
    return 0;
}

void lyr_poller_forget(LyrPoller *poller, int64_t fd) {
    Armed *a = armed_of(&poller->ready, fd);
    if (a == NULL) return;
    struct kevent events[2];
    int n = 0;
    if (a->read) EV_SET(&events[n++], (uintptr_t)fd, EVFILT_READ, EV_DELETE, 0, 0, NULL);
    if (a->write) EV_SET(&events[n++], (uintptr_t)fd, EVFILT_WRITE, EV_DELETE, 0, 0, NULL);
    if (n > 0) (void)kevent(poller->queue, events, n, NULL, 0, NULL);
    if (fire_all(&poller->ready, fd)) lyr_poller_wake(poller);
}
#endif

int64_t lyr_poller_take(LyrPoller *poller, int64_t *into, int64_t n) {
    return take(&poller->ready, into, n);
}

static _Thread_local LyrPoller *this_poller;

LyrPoller *lyr_poller_current(void) {
    if (this_poller == NULL) this_poller = lyr_poller_open();
    return this_poller;
}

void lyr_poller_release(void) {
    if (this_poller == NULL) return;
    lyr_poller_close(this_poller);
    free_ready(&this_poller->ready);
    free(this_poller);
    this_poller = NULL;
}
