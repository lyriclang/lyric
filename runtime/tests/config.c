/* Configuration (11 W5 H1).
 * Expected: a second lyr_init returns -1; output through a configured stdout writer reaches the
 * writer, not the process's stdout; afterwards the program prints "config ok" on the real stdout
 * (through a second, plain writer path) — nothing else appears there. Exit 0. */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

static char captured[256];
static size_t captured_len;

static void capture(const char *bytes, size_t len, void *context) {
    CHECK(context == (void *)captured);
    CHECK(captured_len + len < sizeof captured);
    memcpy(captured + captured_len, bytes, len);
    captured_len += len;
}

int main(int argc, char **argv) {
    LyrConfig config = { .argc = argc, .argv = argv, .stdout_write = capture, .write_context = captured };
    CHECK(lyr_init(&config) == 0);
    CHECK(lyr_init(&config) == -1);

    lyr_println(lyr_str_from_cstr("to the host"));
    CHECK(captured_len == strlen("to the host\n"));
    CHECK(memcmp(captured, "to the host\n", captured_len) == 0);
    lyr_shutdown();

    fputs("config ok\n", stdout);
    return 0;
}
