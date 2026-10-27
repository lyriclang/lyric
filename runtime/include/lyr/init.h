/* Starting and stopping the runtime, and the output it owns. */
#ifndef LYR_INIT_H
#define LYR_INIT_H

#include "lyr/types.h"

/* LyrConfig, lyr_init and lyr_shutdown: the host's, in its public header. */
#include "lyric.h"

/* Whether lyr_init started the runtime and lyr_shutdown has not stopped it. */
int lyr_started(void);

/* The whole `main` of an emitted program: start with defaults, run the program's main, stop, and
 * answer the exit code — the program's value masked to 0..255. */
int lyr_run_main(int argc, char **argv, int64_t (*program_main)(void));

int lyr_argc(void);
char **lyr_argv(void);

/* Unbuffered output through the configured writers; buffering is the console's (lyr/console.h). */
void lyr_write_stdout(const void *bytes, size_t len);
void lyr_write_stderr(const void *bytes, size_t len);
void lyr_print(const LyrStr *text);
void lyr_println(const LyrStr *text);

#endif
