/* The runtime's side of std.task (lyr/task.h). */
#include "lyr/task.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include "lyr/poll.h"
#include "internal.h"

#include <string.h>

/* What a task's context runs: a function value `fn() -> void`, its code and its environment. */
typedef struct TaskBody {
    LyrObj header;
    void (*code)(void *env, LyrErr **error);
    void *env;
} TaskBody;

static const uint64_t task_body_refs[] = { 1u << 2 };  /* the environment, the third word */
static const LyrDesc task_body_desc = { .size = sizeof(TaskBody), .flags = LYR_DESC_HAS_REFS,
                                        .refmap_words = 1, .refmap = task_body_refs, .name = "std.task.<body>" };
static const LyrDesc task_desc = { .size = 0, .name = "std.task.<context>" };

/* A task's body: the function value, whose type throws nothing — so its code gets no error slot
 * (03 T17). The trace ends here, as at the runner of a generator's body. */
static void lyr_task_run(void *arg) {
    TaskBody *body = arg;
    body->code(body->env, NULL);
}

LyrCoro *lyr_task_start(void (*code)(void *env, LyrErr **error), void *env, int64_t stack_size) {
    TaskBody *body = lyr_alloc(&task_body_desc);
    body->code = code;
    LYR_WRITE_BARRIER(body, &body->env, env);
    LyrCoro *co = lyr_coro_new(&task_desc, lyr_task_run, body, stack_size > 0 ? (size_t)stack_size : 0);
    co->task = 1;
    return co;
}

static _Thread_local void *this_scheduler;

void *lyr_task_scheduler(void) { return this_scheduler; }

void lyr_task_set_scheduler(void *scheduler) { this_scheduler = scheduler; }

uint8_t lyr_task_wait(int64_t timeout_ns) {
    return (uint8_t)lyr_poller_wait(lyr_poller_current(), timeout_ns);
}

/* main's context keeps its function and, once it returned, its value. */
typedef struct MainTask {
    int64_t (*program_main)(void);
    int64_t result;
} MainTask;

/* main's body. The trace ends at the emitted glue above it, 'lyr_entry', or here. */
static void lyr_task_main(void *arg) {
    MainTask *main_task = arg;
    main_task->result = main_task->program_main();
}

int lyr_run_main_task(int argc, char **argv, int64_t (*program_main)(void), void (*scheduler)(LyrCoro *main)) {
    LyrConfig defaults;
    memset(&defaults, 0, sizeof defaults);
    defaults.argc = argc;
    defaults.argv = argv;
    defaults.install_signal_handlers = 1;
    lyr_init(&defaults);
    MainTask main_task = { program_main, 0 };  /* on this stack, which outlives main's context */
    LyrCoro *co = lyr_coro_new(&task_desc, lyr_task_main, &main_task, LYR_MAIN_TASK_STACK);
    co->task = 1;
    scheduler(co);
    lyr_shutdown();
    return (int)(main_task.result & 0xFF);
}
