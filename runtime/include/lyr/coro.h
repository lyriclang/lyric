/* Stackful coroutines (design/v5/spec/01 L4): asymmetric and cooperative, each on a stack of its
 * own. The runtime gives the primitives only (K7) — create, resume, yield, status, the running
 * coroutine; generators are the compiler's, schedulers, tasks and channels Lyric's (06 N3–N6).
 * A coroutine runs on the thread that first resumed it, and only there (06 G1). */
#ifndef LYR_CORO_H
#define LYR_CORO_H

#include "lyr/types.h"

#include <stddef.h>

typedef struct LyrCoro LyrCoro;
struct LyrErr;

typedef enum LyrCoroStatus {
    LYR_CORO_SUSPENDED = 1,  /* not started, or stopped at a yield: a resume runs it */
    LYR_CORO_RUNNING = 2,    /* running, or waiting for a coroutine it resumed */
    LYR_CORO_DONE = 3,       /* its body returned */
    LYR_CORO_PARKED = 4,     /* a coroutine of its chain parked: a resume continues that one */
} LyrCoroStatus;

/* A coroutine's body, run on the coroutine's stack at its first resume; returning ends it. */
typedef void (*LyrCoroBody)(void *arg);

/* The default stack reservation (01 K1). */
enum { LYR_CORO_STACK_DEFAULT = 256 * 1024 };

/* A coroutine around `body`, not started. `desc` is its type's descriptor — the coroutine is a
 * heap object like any other; `arg` is handed to the body and kept alive by the coroutine, the
 * place compiled code keeps what crosses a yield. `stack_size` 0 is the default; the stack is
 * reserved at the first resume and committed page by page as the body reaches it. Unreachable
 * while suspended, a coroutine is collected with its stack, and nothing on that stack runs
 * (06 A5). Never NULL: exhaustion panics. */
LyrCoro *lyr_coro_new(const LyrDesc *desc, LyrCoroBody body, void *arg, size_t stack_size);

/* Runs `co` until it yields or its body returns, then comes back. Resuming a coroutine that runs
 * — itself, or one a coroutine already waits for — or one that is done, or one that belongs to
 * another thread, panics (RT0014). */
void lyr_coro_resume(LyrCoro *co);

/* Stops the running coroutine and goes back to its resumer; the next resume continues after this
 * call. With no coroutine running — on a thread's own stack — it panics (RT0014). */
void lyr_coro_yield(void);

/* A yield that hands over a value: its address, valid while the coroutine stays suspended — the
 * value lives in the suspended frame — and read by the resumer through lyr_coro_transfer. NULL
 * for a yield without one. */
void lyr_coro_yield_value(void *value);

/* A yield outside a coroutine's own body (06 §10a, A8): its value meets the running coroutine's
 * yield type only at run time, so the site names its type and the coroutine its own — a mismatch
 * panics (RT0014), as does such a yield with no coroutine running. `key` is the type as Lyric
 * writes it. */
void lyr_coro_yield_dynamic(void *value, const char *key);

/* The yield type a coroutine's pulls read, as Lyric writes it: what a dynamic yield is held to.
 * Left unset, a coroutine takes what a dynamic yield hands it unchecked. */
void lyr_coro_set_yield_key(LyrCoro *co, const char *key);

/* What the coroutine's latest yield handed over; after its body returned, what the body left
 * there (lyr_coro_set_transfer) — the result, or NULL. */
void *lyr_coro_transfer(const LyrCoro *co);
void lyr_coro_set_transfer(LyrCoro *co, void *value);

/* The error a coroutine's body ended with (05 E10): set by the body's caller on the coroutine's
 * stack, taken — once — by the resumer, who throws it on. A heap object the coroutine keeps
 * alive until it is taken. */
void lyr_coro_set_error(LyrCoro *co, struct LyrErr *error);
struct LyrErr *lyr_coro_take_error(LyrCoro *co);

/* Parks the running chain at the scheduler (06 N3): the innermost running coroutine stops where it
 * stands, and the outermost one of its chain — the one resumed from the thread's own stack, where
 * the scheduler runs — gives control back there, PARKED, as if it had yielded. The coroutines in
 * between keep waiting for the ones they resumed: the chain stays intact. A later lyr_coro_resume
 * of the outermost one continues the innermost where it parked. Parking with no coroutine running
 * panics (RT0014), as does closing a parked coroutine (cancellation is the scheduler's, M6 S4). */
void lyr_coro_park(void);

/* Closes a coroutine (06 A5, 01 K7a). One that is done, or was never resumed, ends without
 * running anything. One suspended at a yield is resumed to be unwound: the yield asks
 * lyr_coro_closing(), finds it set and throws, the body's defers run on the way out, and the body
 * ends — the error it ends with is left as at any end (lyr_coro_take_error). A yield while the
 * coroutine is being closed panics, as does closing one that runs (RT0014). Done afterwards. */
void lyr_coro_close(LyrCoro *co);

/* Whether the running coroutine was resumed to be closed: what compiled code asks right after a
 * yield returns. 0 on a thread's own stack. */
int lyr_coro_closing(void);

/* Marks a coroutine whose body has cleanup — a defer or a using — that only running it to its
 * end or closing it does. Dropped while suspended, such a coroutine is reported on the error
 * stream in the debug profile (06 A5: the collector's net); nothing of it runs either way. */
void lyr_coro_set_cleanup(LyrCoro *co);

LyrCoroStatus lyr_coro_status(const LyrCoro *co);
void *lyr_coro_arg(const LyrCoro *co);

/* The coroutine running on the calling thread; NULL on the thread's own stack. */
LyrCoro *lyr_coro_current(void);

/* Foreign frames (01 K4): C code that may call back into Lyric — and so may yield with its frames
 * on a coroutine's stack — brackets itself with these. A cancellation asks the count before it
 * unwinds a stack: C frames cannot be unwound. On a thread's own stack they do nothing. */
void lyr_coro_enter_foreign(void);
void lyr_coro_leave_foreign(void);
int lyr_coro_foreign_depth(const LyrCoro *co);

/* Coroutine stacks in use — started and not yet released. For tests and hosts. */
size_t lyr_coro_stacks(void);

#endif
