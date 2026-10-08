/* Runtime start and stop, the configured writers, and the main of an emitted program. */
#include "lyr/init.h"
#include "lyr/console.h"
#include "lyr/gc.h"
#include "internal.h"

#include <errno.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <signal.h>
#  include <unistd.h>
#endif

void lyr_gc_set_heap_limit(size_t bytes);

static int started;
static LyrConfig config;

int lyr_init(const LyrConfig *given) {
    if (started) return -1;
    if (given) config = *given;
    lyr_gc_init();
    if (config.heap_limit) lyr_gc_set_heap_limit(config.heap_limit);
    if (config.install_signal_handlers) {
        lyr_crash_install();
#ifndef _WIN32
        /* a write into a pipe nobody reads fails — std.io's BrokenPipe — instead of ending the
         * process (10 Q9: SIGPIPE ignored); a host owns its signals and decides itself */
        signal(SIGPIPE, SIG_IGN);
#endif
    }
#ifdef _WIN32
    /* Strings are UTF-8; the console shows them as such only in this code page. */
    SetConsoleOutputCP(CP_UTF8);
#endif
    started = 1;
    return 0;
}

void lyr_shutdown(void) {
    /* the console's buffers out first (10 O9: flushed at the program's end) */
    lyr_console_flush_all(1);
    started = 0;
}

int lyr_stream_hooked(int fd) { return fd == 2 ? config.stderr_write != NULL : config.stdout_write != NULL; }

int lyr_signals_allowed(void) { return started && config.install_signal_handlers; }

int lyr_argc(void) { return config.argc; }
char **lyr_argv(void) { return config.argv; }

static void write_fd(int fd, const void *bytes, size_t len) {
    const char *p = bytes;
#ifdef _WIN32
    HANDLE handle = GetStdHandle(fd == 1 ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE);
    while (len > 0) {
        DWORD chunk = len > 0x40000000u ? 0x40000000u : (DWORD)len, written = 0;
        if (!WriteFile(handle, p, chunk, &written, NULL) || written == 0) return;
        p += written;
        len -= written;
    }
#else
    while (len > 0) {
        ssize_t written = write(fd, p, len);
        if (written < 0) {
            if (errno == EINTR) continue;
            return;
        }
        p += written;
        len -= (size_t)written;
    }
#endif
}

void lyr_write_stdout(const void *bytes, size_t len) {
    if (config.stdout_write) config.stdout_write(bytes, len, config.write_context);
    else write_fd(1, bytes, len);
}

void lyr_write_stderr(const void *bytes, size_t len) {
    if (config.stderr_write) config.stderr_write(bytes, len, config.write_context);
    else write_fd(2, bytes, len);
}

void lyr_print(const LyrStr *text) {
    lyr_write_stdout(text->bytes, (size_t)text->len);
}

void lyr_println(const LyrStr *text) {
    lyr_write_stdout(text->bytes, (size_t)text->len);
    lyr_write_stdout("\n", 1);
}

int lyr_run_main(int argc, char **argv, int64_t (*program_main)(void)) {
    LyrConfig defaults;
    memset(&defaults, 0, sizeof defaults);
    defaults.argc = argc;
    defaults.argv = argv;
    defaults.install_signal_handlers = 1;
    lyr_init(&defaults);
    int64_t result = program_main();
    lyr_shutdown();
    return (int)(result & 0xFF);
}
