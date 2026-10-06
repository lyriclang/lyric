/* What the runtime's source files share with each other and not with anyone else: no part of the
 * ABI, no promise to emitted code or hosts. */
#ifndef LYR_INTERNAL_H
#define LYR_INTERNAL_H

#include "lyr/coro.h"
#include "lyr/panic.h"
#include "lyr/types.h"

#include <stddef.h>
#include <stdint.h>

#if (defined(__GNUC__) || defined(__clang__)) && !defined(_WIN32)
#  define LYR_HIDDEN __attribute__((visibility("hidden")))
#else
#  define LYR_HIDDEN
#endif

/* For code that reads memory that is not its own on purpose — another stack's frames, redzones
 * included — the way the conservative collector does. */
#if defined(__clang__) || defined(__GNUC__)
#  define LYR_NO_SANITIZE __attribute__((no_sanitize("address", "thread")))
#else
#  define LYR_NO_SANITIZE
#endif

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

/* init.c — whether the runtime may catch signals: started, and with its handlers (lyr/signal.h). */
int lyr_signals_allowed(void);

/* crash.c — the address range whose fault is a stack overflow, for the stack the thread runs on:
 * a coroutine switch swaps it (nothing on Windows, where the system tells an overflow apart). */

/* Windows: what a thread keeps for the exception filter once its stack is spent
 * (SetThreadStackGuarantee, crash.c) — the trace needs DbgHelp, which is not frugal. The system
 * reads it off the TEB at every guard-page fault, so it holds for a coroutine's stack too while
 * the coroutine runs (coro.c swaps the TEB's bounds): a coroutine's mapping reserves as much below
 * its stack, and the kernel commits it only for the report. */
#define LYR_STACK_GUARANTEE (128 * 1024)
void lyr_crash_get_guard(uintptr_t *low, uintptr_t *high);
void lyr_crash_set_guard(uintptr_t low, uintptr_t high);

/* coro.c — a stack that can stop: a thread's own, or a coroutine's. While another stack runs on
 * the thread, `sp` is where this one's registers lie, and the collector scans [sp, base) — a
 * coroutine's as part of the coroutine object, a thread's own as a root. */
typedef struct LyrStackCtx {
    void *sp;
    void *base;                       /* the cold end, the highest address */
    void *low;                        /* the lowest usable address, above the guard page */
    int on_cpu;                       /* the thread runs on this stack now */
    uintptr_t guard_low, guard_high;  /* a fault in this range is a stack overflow */
    void *asan_fake;                  /* ASan's handle on the stack's fake frames across a switch */
    const void *asan_bottom;          /* the bounds ASan is told when the thread switches to it */
    size_t asan_size;
    void *tsan_fiber;
} LyrStackCtx;

/* The coroutine object. The collector stage traces `arg`, `resumer_coro` and `error` and, while
 * the stack is mapped and stopped, every word of [ctx.sp, ctx.base). `status` is 0 only on a free
 * list. */
struct LyrCoro {
    LyrObj header;
    void *arg;
    struct LyrCoro *resumer_coro;  /* the coroutine that resumed it; NULL: a thread's own stack */
    struct LyrErr *error;          /* what the body ended with, until the resumer takes it */
    void *transfer;                /* the latest yield's value, or the result: a pointer into the
                                      suspended stack or into `arg` — never the only reference */
    LyrCoroBody body;
    LyrStackCtx *resumer;          /* where a yield goes */
    LyrStackCtx ctx;
    void *mapping;                 /* the stack's mapping, its guard page first; NULL: none */
    size_t mapping_size;
    size_t stack_size;             /* usable bytes */
    uint64_t owner;                /* the thread it runs on, by the number of its coroutine state */
    int status;
    int foreign;                   /* foreign frames on its stack (01 K4) */
    int closing;                   /* resumed by lyr_coro_close: the next yield returns to throw */
    const char *yield_key;         /* the yield type, for a dynamic yield (lyr_coro_set_yield_key) */
    struct LyrCoro *parked_top;    /* PARKED: the innermost coroutine of its chain, where it parked */
    int cleanup;                   /* its body has defers or usings (lyr_coro_set_cleanup) */
    int task;                      /* a task's context (lyr/task.h): no generator, so no yield */
    struct LyrStr *panic_code;     /* PANICKED: the report it left — code, message, frames */
    struct LyrStr *panic_message;
    struct LyrStr *panic_trace;
};

/* coro.c — a suspended coroutine the collector found unreachable: its stack goes (06 A5). */
void lyr_coro_abandoned(LyrCoro *co);
/* coro.c — a thread leaving the runtime drops its coroutine state. */
void lyr_coro_thread_end(void);

/* coro.c — a panic leaves the coroutine that runs (05 E8): its resumer finds it PANICKED with the
 * report — `trace`, or the frames of where it stands when NULL. Returns only where there is
 * nowhere to leave to: on a thread's own stack, or with foreign frames on the coroutine's stack. */
void lyr_coro_panic_leave(const char *code, const char *message, const char *trace);

/* coro.c — resumes `co` and answers its status after: a panic of it stays its own, not passed on. */
int lyr_coro_resume_quiet(LyrCoro *co);

/* coro.c — the panic `co` left, again, in the running code: what a resumer does with it. */
LYR_NORETURN void lyr_coro_repanic(LyrCoro *co);

/* panic.c — a panic with the report an earlier one left: its code, message and frames. */
LYR_NORETURN void lyr_panic_again(const char *code, const char *message, const char *trace);

/* gc_boehm.c — what coroutines need of the collector stage. */
LyrCoro *lyr_gc_alloc_coro(void);                /* zeroed, of the kind that traces its stack */
void lyr_gc_watch_coro(LyrCoro *co);             /* lyr_coro_abandoned when it dies */
void lyr_gc_unwatch_coro(LyrCoro *co);
void *lyr_gc_thread_stack(void **base);          /* the calling thread's handle, and its stack bottom */
void lyr_gc_add_thread_stack(LyrStackCtx *own);  /* a root while a coroutine runs on its thread */
void lyr_gc_remove_thread_stack(LyrStackCtx *own);
void lyr_gc_switch_begin(void *gc_handle, const LyrStackCtx *to); /* no world stop until the end; the bottom moves */
void lyr_gc_switch_end(void);                    /* on the stack that runs now: the world may stop again */

#endif
