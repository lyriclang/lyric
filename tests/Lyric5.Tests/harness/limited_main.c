/* A test stand-in for lyr_run_main: the emitted program is compiled with
 * -Dlyr_run_main=lyr_test_run_main and so starts here, under a heap limit of 16 MiB — far below
 * what a churning program allocates in total, so it finishes only if its garbage is collected.
 * After the program's own output it prints "collections N" with the number of collections that
 * ran, which the test reads: a graph "survived collections" only if there were any. */
#include "lyr/lyr.h"

#include <stdio.h>
#include <string.h>

int lyr_test_run_main(int argc, char **argv, int64_t (*program_main)(void)) {
    LyrConfig config;
    memset(&config, 0, sizeof config);
    config.argc = argc;
    config.argv = argv;
    config.heap_limit = (size_t)16 << 20;
    config.install_signal_handlers = 1;
    if (lyr_init(&config) != 0) return 3;
    int64_t result = program_main();
    /* the program's output is held in the console's buffers (lyr/console.h): out first, so the
     * line below comes after it */
    lyr_console_flush_all(1);
    char line[64];
    int n = snprintf(line, sizeof line, "collections %zu\n", lyr_gc_collections());
    lyr_write_stdout(line, (size_t)n);
    lyr_shutdown();
    return (int)(result & 0xFF);
}
