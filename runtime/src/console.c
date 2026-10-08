/* The console's two buffered streams (lyr/console.h). */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/console.h"
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

static int a_line_at_a_time(int fd) {
    if (lyr_stream_hooked(fd)) return 1;
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
