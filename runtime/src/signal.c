/* Signals as a channel (lyr/signal.h): a mask of caught signals and a wake for the watcher. POSIX
 * wakes it through a pipe the handler writes a byte to — write is async-signal-safe — whose read end
 * the watcher's poller watches; Windows calls the handler on a thread of its own, which wakes the
 * watcher's poller directly. */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE 1
#elif defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#  define _DARWIN_C_SOURCE 1
#endif

#include "lyr/signal.h"
#include "lyr/panic.h"
#include "lyr/poll.h"
#include "internal.h"

#include <stdatomic.h>

static _Atomic int64_t pending;

int64_t lyr_signal_take(void) {
    return atomic_exchange(&pending, 0);
}

static void mark(int number) {
    atomic_fetch_or(&pending, (int64_t)((uint64_t)1 << (number - 1)));
}

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>

/* The C runtime's numbers for the two that Windows has. */
enum { WIN_SIGINT = 2, WIN_SIGTERM = 15 };

static _Atomic(LyrPoller *) watcher;
static atomic_int catching_interrupt, catching_terminate, handler_set;

static BOOL WINAPI on_console(DWORD event) {
    int number;
    switch (event) {
    case CTRL_C_EVENT:
    case CTRL_BREAK_EVENT:
        if (!atomic_load(&catching_interrupt)) return FALSE;
        number = WIN_SIGINT;
        break;
    case CTRL_CLOSE_EVENT:
    case CTRL_LOGOFF_EVENT:
    case CTRL_SHUTDOWN_EVENT:
        if (!atomic_load(&catching_terminate)) return FALSE;
        number = WIN_SIGTERM;
        break;
    default:
        return FALSE;
    }
    mark(number);
    LyrPoller *poller = atomic_load(&watcher);
    if (poller != NULL) lyr_poller_wake(poller);
    return TRUE;
}

int64_t lyr_signal_number(int64_t kind) {
    return kind == 0 ? WIN_SIGINT : kind == 1 ? WIN_SIGTERM : -1;
}

int64_t lyr_signal_kind(int64_t number) {
    return number == WIN_SIGINT ? 0 : number == WIN_SIGTERM ? 1 : -1;
}

uint8_t lyr_signal_catch(int64_t number, uint8_t on) {
    if (!lyr_signals_allowed()) return 0;
    if (number == WIN_SIGINT) atomic_store(&catching_interrupt, on != 0);
    else if (number == WIN_SIGTERM) atomic_store(&catching_terminate, on != 0);
    else return 0;
    if (on && !atomic_exchange(&handler_set, 1)) SetConsoleCtrlHandler(on_console, TRUE);
    return 1;
}

void lyr_signal_attach(void) {
    atomic_store(&watcher, lyr_poller_current());
}

#else
#  include <errno.h>
#  include <fcntl.h>
#  include <pthread.h>
#  include <signal.h>
#  include <string.h>
#  include <unistd.h>

static int pipe_ends[2] = { -1, -1 };
static pthread_once_t pipe_once = PTHREAD_ONCE_INIT;

static void make_pipe(void) {
    if (pipe(pipe_ends) != 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a pipe for signals: %s", strerror(errno));
    for (int i = 0; i < 2; i++) {
        fcntl(pipe_ends[i], F_SETFL, fcntl(pipe_ends[i], F_GETFL) | O_NONBLOCK);
        fcntl(pipe_ends[i], F_SETFD, FD_CLOEXEC);
    }
}

static void on_signal(int number) {
    int saved = errno;
    mark(number);
    char byte = 1;
    ssize_t written = write(pipe_ends[1], &byte, 1);  /* a full pipe wakes the watcher already */
    (void)written;
    errno = saved;
}

static const int kinds[] = { SIGINT, SIGTERM, SIGHUP, SIGQUIT, SIGUSR1, SIGUSR2, SIGWINCH };

int64_t lyr_signal_number(int64_t kind) {
    return kind >= 0 && kind < (int64_t)(sizeof kinds / sizeof kinds[0]) ? kinds[kind] : -1;
}

int64_t lyr_signal_kind(int64_t number) {
    for (int i = 0; i < (int)(sizeof kinds / sizeof kinds[0]); i++) {
        if (kinds[i] == number) return i;
    }
    return -1;
}

/* KILL and STOP the kernel never delivers; the faults are crashes with a report (crash.c); PIPE,
 * CHLD and ALRM the runtime keeps for itself (10 Q9). Numbers up to 63: the mask is an int64_t. */
static int catchable(int64_t number) {
    if (number < 1 || number > 63) return 0;
    switch ((int)number) {
    case SIGKILL: case SIGSTOP: case SIGSEGV: case SIGBUS: case SIGFPE: case SIGILL: case SIGTRAP: case SIGABRT:
    case SIGPIPE: case SIGCHLD: case SIGALRM:
        return 0;
    default:
        return 1;
    }
}

uint8_t lyr_signal_catch(int64_t number, uint8_t on) {
    if (!lyr_signals_allowed() || !catchable(number)) return 0;
    pthread_once(&pipe_once, make_pipe);
    struct sigaction action;
    memset(&action, 0, sizeof action);
    sigemptyset(&action.sa_mask);
    action.sa_handler = on ? on_signal : SIG_DFL;
    action.sa_flags = SA_RESTART;
    return sigaction((int)number, &action, NULL) == 0;
}

void lyr_signal_attach(void) {
    pthread_once(&pipe_once, make_pipe);
    lyr_poller_watch(lyr_poller_current(), pipe_ends[0]);
}
#endif
