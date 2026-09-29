/* Starting and stopping the runtime (design/v5/spec/11 W5 H1), and the output it owns. */
#ifndef LYR_INIT_H
#define LYR_INIT_H

#include "lyr/types.h"

/* Where the runtime's own output goes. NULL means the process's standard stream; a host passes a
 * function to capture it (an editor showing a program's println). */
typedef void (*LyrWriteFn)(const char *bytes, size_t len, void *context);

typedef struct LyrConfig {
    int argc;
    char **argv;
    size_t heap_limit;             /* bytes; 0 = no limit. Beyond it allocation panics (RT0005) */
    int install_signal_handlers;   /* crash reports for fault signals; an embedding host says 0 (10 Q9) */
    LyrWriteFn stdout_write;
    LyrWriteFn stderr_write;
    void *write_context;
} LyrConfig;

/* One runtime per process: a second call returns -1 and changes nothing. */
int lyr_init(const LyrConfig *config);
void lyr_shutdown(void);

/* The whole `main` of an emitted program: start with defaults, run the program's main, stop, and
 * answer the exit code — the program's value masked to 0..255 (11 C5). */
int lyr_run_main(int argc, char **argv, int64_t (*program_main)(void));

int lyr_argc(void);
char **lyr_argv(void);

/* Unbuffered output through the configured writers; buffering is the standard library's (O9). */
void lyr_write_stdout(const void *bytes, size_t len);
void lyr_write_stderr(const void *bytes, size_t len);
void lyr_print(const LyrStr *text);
void lyr_println(const LyrStr *text);

#endif
