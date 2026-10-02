/* Tasks (design/v5/spec/06 N4, N6 S1): the runtime's side of std.task, whose scheduler is Lyric.
 * A task's context is a coroutine (lyr/coro.h) that yields nothing — it only ever parks — around a
 * Lyric function value; main is a task where the program waits (06 T6). */
#ifndef LYR_TASK_H
#define LYR_TASK_H

#include <stdint.h>

#include "lyr/coro.h"
#include "lyr/error.h"

/* The reservation of main's stack when main is a task: what a thread's is on Linux. Like every
 * coroutine stack it costs memory only as its pages are reached (01 K1). */
enum { LYR_MAIN_TASK_STACK = 8 * 1024 * 1024 };

/* A task's context, not started, around the function value `fn() -> void` given by its code and
 * environment (01 V8): the scheduler resumes it. A dynamic yield in it, outside every generator,
 * panics (RT0014). `stack_size` 0 is the default. */
LyrCoro *lyr_task_start(void (*code)(void *env, LyrErr **error), void *env, int64_t stack_size);
#define LYR_TASK_START(body, stack_size) lyr_task_start((body).fn, (body).env, (stack_size))

/* The running thread's scheduler: a Lyric object, which std.task keeps alive on the thread's own
 * stack while it runs. NULL where none runs. */
void *lyr_task_scheduler(void);
void lyr_task_set_scheduler(void *scheduler);

/* Waits on the thread's poller (lyr/poll.h) for `timeout_ns` at most, a negative one without end:
 * 1 when woken. */
uint8_t lyr_task_wait(int64_t timeout_ns);

/* The scheduler's resume of a task's context (06 T4): it runs until it parks, ends or panics — and
 * a panic stays the context's, for the scheduler to settle, instead of passing on (05 E8). 0: it
 * parked; 1: its body returned; 2: it panicked. */
uint8_t lyr_task_resume(LyrCoro *co);

/* The report a panicked context left: its code, its message, its frames as the report writes them;
 * NULL where it did not panic. */
struct LyrStr *lyr_task_panic_code(LyrCoro *co);
struct LyrStr *lyr_task_panic_message(LyrCoro *co);
struct LyrStr *lyr_task_panic_trace(LyrCoro *co);

/* A panic again, with a report an earlier one left (06 T4: `await` of a panicked task). */
LYR_NORETURN void lyr_task_repanic(struct LyrStr *code, struct LyrStr *message, struct LyrStr *trace);

/* The `main` of an emitted program whose main is a task (06 T6): start the runtime with its
 * defaults, make main's context — a coroutine around `program_main` on a stack of
 * LYR_MAIN_TASK_STACK — and hand it to `scheduler`, std.task's loop, which returns once that
 * context is done; then stop, and answer main's value masked to 0..255 (11 C5). */
int lyr_run_main_task(int argc, char **argv, int64_t (*program_main)(void), void (*scheduler)(LyrCoro *main));

#endif
