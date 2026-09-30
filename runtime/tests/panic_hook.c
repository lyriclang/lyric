/* A host's view of a panic (05 E8, 11 H1): the runtime started through lyr_init with an error
 * writer of the host's own and no signal handlers, and a panic hook.
 * Expected without arguments: exit 101, nothing on stderr, and stdout exactly
 *   captured: panic [LYR-RT0007]: the host asked for it
 *   hook: LYR-RT0007 | the host asked for it | host_work in the trace
 * — the report reaches the host's writer before the hook runs, and the hook sees the same frames.
 * With "nested" the hook panics in turn: exit 101, and stderr ends with the line
 *   panic while panicking [LYR-RT0001]: division by zero */
#include "lyr/lyr.h"
#include "conservative.h"

#include <string.h>

static char captured[8192];
static size_t captured_len;

static void capture(const char *bytes, size_t len, void *context) {
    (void)context;
    if (len > sizeof captured - captured_len) len = sizeof captured - captured_len;
    memcpy(captured + captured_len, bytes, len);
    captured_len += len;
}

static void say(const char *text) { lyr_write_stdout(text, strlen(text)); }

static void on_panic(const LyrPanicInfo *info, void *context) {
    (void)context;
    const char *end = memchr(captured, '\n', captured_len);
    say("captured: ");
    lyr_write_stdout(captured, end ? (size_t)(end - captured) : captured_len);
    say("\nhook: ");
    say(info->code);
    say(" | ");
    say(info->message);
    say(strstr(info->backtrace, "at host_work") ? " | host_work in the trace\n" : " | host_work missing\n");
}

static volatile int64_t zero = 0;

static void on_panic_nested(const LyrPanicInfo *info, void *context) {
    (void)info;
    (void)context;
    say("hook runs\n");
    (void)LYR_CHECKED_DIV((int64_t)1, zero);
}

NOINLINE static void host_work(void) {
    lyr_panic(LYR_RT_ARGUMENT, "the host asked for it");
}

int main(int argc, char **argv) {
    int nested = argc > 1 && strcmp(argv[1], "nested") == 0;
    LyrConfig config;
    memset(&config, 0, sizeof config);
    config.argc = argc;
    config.argv = argv;
    config.install_signal_handlers = 0;
    if (!nested) config.stderr_write = capture;
    lyr_init(&config);
    lyr_set_panic_hook(nested ? on_panic_nested : on_panic, NULL);
    host_work();
    return 0;
}
