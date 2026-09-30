/* What the runtime's source files share with each other and not with anyone else: no part of the
 * ABI, no promise to emitted code or hosts. */
#ifndef LYR_INTERNAL_H
#define LYR_INTERNAL_H

#include "lyr/panic.h"

#include <stddef.h>
#include <stdint.h>

/* Where a fault happened, from the signal's or the exception's context: the faulting instruction,
 * and — where the platform's unwinder cannot step out of a signal handler (macOS) — the frame
 * pointer and the link register to walk from. Zero where unknown or not needed. */
typedef struct LyrFault {
    uintptr_t pc;
    uintptr_t fp;
    uintptr_t lr;
} LyrFault;

/* trace.c — the calling thread's stack as "    at function (file:line)\n" lines in `out`,
 * NUL-terminated; answers the length. For a fault it starts at the frame that was executing
 * there (a signal or an exception); for a panic (fault NULL), below the innermost frame of the
 * panic path (every runtime function on that path is named lyr_panic* or lyr_crash*). It stops
 * above lyr_run_main. Best effort: without debug information a frame is a bare address. */
size_t lyr_trace_format(char *out, size_t capacity, const LyrFault *fault);

/* panic.c — the report every panic ends in, also the one a stack overflow reaches from a signal
 * handler (in_handler: leave through _Exit, not exit). */
LYR_NORETURN void lyr_panic_report(const char *code, const char *message, const LyrFault *fault, int in_handler);

/* crash.c — handlers for fault signals (10 Q9: a crash, not a panic) and for stack overflow (a
 * panic, 01 S3), installed only when the configuration asks (a host owns its signals). Every
 * thread that runs Lyric code needs its own alternate signal stack: the thread functions set it
 * up and take it down; they do nothing when the handlers are not installed. */
void lyr_crash_install(void);
void lyr_crash_thread_start(void);
void lyr_crash_thread_end(void);

#endif
