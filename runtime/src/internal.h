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
 * above the program's entry (lyr_run_main, or the emitted lyr_entry). Best effort: without debug
 * information a frame is a bare address. */
size_t lyr_trace_format(char *out, size_t capacity, const LyrFault *fault);

/* How many program counters a trace keeps, a panic's or an error's: a stack deeper than this is
 * cut, and the trace says so. */
enum { LYR_TRACE_PCS = 256 };

/* trace.c — the program counters of the calling thread's stack, innermost first, up to `max`:
 * what the debug profile keeps of where an error was thrown (01 E8), named only if the error is
 * ever reported. Not for a signal handler: the work buffer is on the caller's stack. */
int lyr_trace_capture(uintptr_t *pcs, int max);

/* trace.c — the frames at `pcs` (from lyr_trace_capture) as lyr_trace_format's lines: below the
 * capture's own runtime frames, stopping above the program's entry; a full LYR_TRACE_PCS reads as
 * a cut stack. */
size_t lyr_trace_format_pcs(char *out, size_t capacity, const uintptr_t *pcs, int count);

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
