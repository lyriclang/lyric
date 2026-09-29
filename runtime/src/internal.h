/* What the runtime's source files share with each other and not with anyone else: no part of the
 * ABI, no promise to emitted code or hosts. */
#ifndef LYR_INTERNAL_H
#define LYR_INTERNAL_H

#include "lyr/panic.h"

#include <stddef.h>
#include <stdint.h>

/* trace.c — the calling thread's stack as "    at function (file:line)\n" lines in `out`,
 * NUL-terminated; answers the length. With a fault address it starts at the frame that was
 * executing there (a signal or an exception); without, below the innermost frame of the panic path
 * (every runtime function on that path is named lyr_panic* or lyr_crash*). It stops above
 * lyr_run_main. Best effort: without debug information a frame is a bare address. */
size_t lyr_trace_format(char *out, size_t capacity, uintptr_t fault_pc);

/* panic.c — the report every panic ends in, also the one a stack overflow reaches from a signal
 * handler (in_handler: leave through _Exit, not exit). */
LYR_NORETURN void lyr_panic_report(const char *code, const char *message, uintptr_t fault_pc, int in_handler);

/* crash.c — handlers for fault signals (10 Q9: a crash, not a panic) and for stack overflow (a
 * panic, 01 S3), installed only when the configuration asks (a host owns its signals). Every
 * thread that runs Lyric code needs its own alternate signal stack: the thread functions set it
 * up and take it down; they do nothing when the handlers are not installed. */
void lyr_crash_install(void);
void lyr_crash_thread_start(void);
void lyr_crash_thread_end(void);

#endif
