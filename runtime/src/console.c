/* The console's two buffered streams (lyr/console.h). */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/console.h"
#include "lyr/fs.h"
#include "lyr/init.h"
#include "internal.h"

#include <stddef.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
typedef SRWLOCK ConsoleLock;
#  define CONSOLE_LOCK_INIT SRWLOCK_INIT
#  define console_lock(l) AcquireSRWLockExclusive(l)
#  define console_try(l) TryAcquireSRWLockExclusive(l)
#  define console_unlock(l) ReleaseSRWLockExclusive(l)
#else
#  include <errno.h>
#  include <pthread.h>
#  include <signal.h>
#  include <unistd.h>
typedef pthread_mutex_t ConsoleLock;
#  define CONSOLE_LOCK_INIT PTHREAD_MUTEX_INITIALIZER
#  define console_lock(l) pthread_mutex_lock(l)
#  define console_try(l) (pthread_mutex_trylock(l) == 0)
#  define console_unlock(l) pthread_mutex_unlock(l)
#endif

#define CONSOLE_ROOM 8192

enum { UNDECIDED, BY_LINE, BY_BLOCK };

typedef struct {
    ConsoleLock lock;
    int mode;
    size_t used;
    uint8_t held[CONSOLE_ROOM];
} Stream;

static Stream streams[2] = { { CONSOLE_LOCK_INIT, UNDECIDED, 0, { 0 } }, { CONSOLE_LOCK_INIT, UNDECIDED, 0, { 0 } } };

static Stream *stream_of(int64_t stream) { return &streams[stream == 2 ? 1 : 0]; }

/* The standard error always a line at a time (Review 2026-10-08, Python's way): a diagnostic in a
 * log or a CI job comes out when it is written, not 8 KiB later. */
static int a_line_at_a_time(int fd) {
    if (fd == 2 || lyr_stream_hooked(fd)) return 1;
#if defined(_WIN32)
    DWORD mode;
    return GetConsoleMode(GetStdHandle(fd == 1 ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE), &mode) != 0;
#else
    return isatty(fd);
#endif
}

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

/* `len` bytes to the stream: through a host's writer where one takes it, else to the system. */
static int64_t write_out(int fd, const uint8_t *bytes, size_t len) {
    if (lyr_stream_hooked(fd)) {
        if (fd == 2) lyr_write_stderr(bytes, len);
        else lyr_write_stdout(bytes, len);
        return 0;
    }
#if defined(_WIN32)
    HANDLE handle = GetStdHandle(fd == 1 ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE);
    while (len > 0) {
        DWORD chunk = len > 0x40000000u ? 0x40000000u : (DWORD)len, written = 0;
        if (!WriteFile(handle, bytes, chunk, &written, NULL)) {
            DWORD error = GetLastError();
            return failure(error == ERROR_BROKEN_PIPE || error == ERROR_NO_DATA ? LYR_IO_BROKEN_PIPE : 0, (int64_t)error);
        }
        if (written == 0) return failure(0, 0);
        bytes += written;
        len -= written;
    }
#else
    while (len > 0) {
        ssize_t written = write(fd, bytes, len);
        if (written < 0) {
            if (errno == EINTR) continue;
            return failure(errno == EPIPE ? LYR_IO_BROKEN_PIPE : 0, errno);
        }
        bytes += written;
        len -= (size_t)written;
    }
#endif
    return 0;
}

int64_t lyr_console_put(int64_t stream, const uint8_t *from, int64_t n) {
    Stream *s = stream_of(stream);
    console_lock(&s->lock);
    if (s->mode == UNDECIDED) s->mode = a_line_at_a_time((int)stream) ? BY_LINE : BY_BLOCK;
    size_t room = CONSOLE_ROOM - s->used;
    size_t taken = n < 0 ? 0 : (size_t)n < room ? (size_t)n : room;
    memcpy(s->held + s->used, from, taken);
    s->used += taken;
    int due = s->used == CONSOLE_ROOM || (s->mode == BY_LINE && taken > 0 && memchr(from, '\n', taken) != NULL);
    console_unlock(&s->lock);
    return (int64_t)taken | ((int64_t)due << 32);
}

int64_t lyr_console_flush(int64_t stream) {
    Stream *s = stream_of(stream);
    console_lock(&s->lock);
    int64_t result = s->used > 0 ? write_out(stream == 2 ? 2 : 1, s->held, s->used) : 0;
    s->used = 0;
    console_unlock(&s->lock);
    return result;
}

void lyr_console_flush_all(int waiting) {
    for (int fd = 1; fd <= 2; fd++) {
        Stream *s = stream_of(fd);
        if (waiting) console_lock(&s->lock);
        else if (!console_try(&s->lock)) continue;
        if (s->used > 0) (void)write_out(fd, s->held, s->used);
        s->used = 0;
        console_unlock(&s->lock);
    }
}

void lyr_console_finish(void) {
    int64_t out = lyr_console_flush(1);
    (void)lyr_console_flush(2);
    if (out < 0 && (-out) >> 32 == LYR_IO_BROKEN_PIPE) lyr_console_gone();
}

void lyr_console_gone(void) {
#if !defined(_WIN32)
    if (!lyr_signals_allowed()) return;
    (void)lyr_console_flush(2);
    signal(SIGPIPE, SIG_DFL);
    raise(SIGPIPE);
    _exit(128 + SIGPIPE);
#endif
}

#if defined(_WIN32)
/* A console's characters as UTF-8 (M8b S8b): at most n / 3 UTF-16 units a read — each becomes at
 * most three bytes, a pair four —, a high surrogate at the end held for the next read; one reader
 * at a time. */
static ConsoleLock reading = CONSOLE_LOCK_INIT;
static wchar_t held_unit;

static int64_t read_console(HANDLE in, uint8_t *into, int64_t n) {
    wchar_t units[1024];
    int64_t room = n / 3 < 1023 ? n / 3 : 1023;
    if (room < 2) return failure(LYR_IO_INVALID_INPUT, 0);
    console_lock(&reading);
    int have = 0;
    if (held_unit) {
        units[have++] = held_unit;
        held_unit = 0;
    }
    for (;;) {
        DWORD got = 0;
        if (!ReadConsoleW(in, units + have, (DWORD)(room - have), &got, NULL)) {
            DWORD error = GetLastError();
            console_unlock(&reading);
            return failure(0, (int64_t)error);
        }
        have += (int)got;
        if (got == 0 || !IS_HIGH_SURROGATE(units[have - 1])) break;
        /* half a pair: alone, read on for its other half; after others, keep it for the next */
        if (have == 1) continue;
        held_unit = units[--have];
        break;
    }
    int bytes = have == 0 ? 0 : WideCharToMultiByte(CP_UTF8, 0, units, have, (char *)into, (int)n, NULL, NULL);
    console_unlock(&reading);
    return bytes;
}
#endif

int64_t lyr_console_read(uint8_t *into, int64_t n) {
#if defined(_WIN32)
    HANDLE in = GetStdHandle(STD_INPUT_HANDLE);
    DWORD mode;
    if (GetConsoleMode(in, &mode)) return read_console(in, into, n);
    DWORD want = n > 0x40000000 ? 0x40000000 : (DWORD)n, got = 0;
    if (!ReadFile(in, into, want, &got, NULL)) {
        DWORD error = GetLastError();
        if (error == ERROR_BROKEN_PIPE || error == ERROR_HANDLE_EOF) return 0;
        return failure(0, (int64_t)error);
    }
    return (int64_t)got;
#else
    ssize_t got;
    do {
        got = read(0, into, (size_t)n);
    } while (got < 0 && errno == EINTR);
    if (got < 0) return failure(0, errno);
    return (int64_t)got;
#endif
}
