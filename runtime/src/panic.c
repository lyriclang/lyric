/* The panic path (05 E8): message, backtrace, hook, exit 101. */
#include "internal.h"
#include "lyr/init.h"
#include "lyr/types.h"

#include <stdarg.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <unistd.h>
#endif

static _Atomic(LyrPanicHook) hook;
static _Atomic(void *) hook_context;

void lyr_set_panic_hook(LyrPanicHook given, void *context) {
    atomic_store(&hook_context, context);
    atomic_store(&hook, given);
}

/* One report per process. The first panicking thread writes it and ends the process; a second
 * thread waits for that end instead of interleaving its report; the same thread panicking again —
 * in the hook, or in the trace itself — ends at once with what it has. */
static atomic_int panicking;
static _Thread_local int this_thread_panicking;

static char report[32 * 1024];
static char message_copy[1024];

LYR_NORETURN static void lyr_panic_wait(void) {
    for (;;) {
#ifdef _WIN32
        Sleep(1000);
#else
        pause();
#endif
    }
}

void lyr_panic_report(const char *code, const char *message, const LyrFault *fault, int in_handler) {
    if (this_thread_panicking) {
        char line[1200];
        int n = snprintf(line, sizeof line, "panic while panicking [%s]: %s\n", code, message);
        if (n > 0) lyr_write_stderr(line, (size_t)n < sizeof line ? (size_t)n : sizeof line - 1);
        _Exit(101);
    }
    this_thread_panicking = 1;
    if (atomic_exchange(&panicking, 1)) lyr_panic_wait();

    snprintf(message_copy, sizeof message_copy, "%s", message);
    int n = snprintf(report, sizeof report, "panic [%s]: %s\n", code, message_copy);
    size_t header = n < 0 ? 0 : (size_t)n < sizeof report ? (size_t)n : sizeof report - 1;
    size_t frames = lyr_trace_format(report + header, sizeof report - header, fault);
    lyr_write_stderr(report, header + frames);

    LyrPanicHook given = atomic_load(&hook);
    if (given) {
        LyrPanicInfo info = { code, message_copy, report + header };
        given(&info, atomic_load(&hook_context));
    }
    /* exit runs the C library's handlers (flushing a C host's stdio); from a signal handler only
     * _Exit is safe. */
    if (in_handler) _Exit(101);
    exit(101);
}

void lyr_panic(const char *code, const char *format, ...) {
    char message[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(message, sizeof message, format, args);
    va_end(args);
    lyr_panic_report(code, message, NULL, 0);
}

void lyr_panic_index(int64_t index, int64_t length) {
    lyr_panic(LYR_RT_INDEX, "index %lld out of bounds for length %lld", (long long)index, (long long)length);
}

void lyr_panic_range(int64_t low, int64_t high, int64_t length) {
    lyr_panic(LYR_RT_INDEX, "range %lld..%lld out of bounds for length %lld", (long long)low, (long long)high,
              (long long)length);
}

void lyr_panic_overflow(const char *operation) {
    lyr_panic(LYR_RT_OVERFLOW, "arithmetic overflow in '%s'", operation);
}

void lyr_panic_division_by_zero(void) {
    lyr_panic(LYR_RT_DIVISION_BY_ZERO, "division by zero");
}

void lyr_panic_null(void) {
    lyr_panic(LYR_RT_NULL_UNWRAP, "unwrapped a null value");
}

void lyr_panic_shift(int64_t count, int bits) {
    lyr_panic(LYR_RT_OVERFLOW, "shift by %lld exceeds the width of %d bits", (long long)count, bits);
}

void lyr_panic_char(uint32_t value) {
    lyr_panic(LYR_RT_CHAR, "0x%X is not a Unicode scalar value", (unsigned)value);
}

void lyr_panic_message(const LyrStr *message) {
    lyr_panic(LYR_RT_PANIC, "%.*s", (int)(message->len < 1000 ? message->len : 1000), message->bytes);
}
