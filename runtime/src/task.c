/* The runtime's side of std.task (lyr/task.h). */
#include "lyr/task.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include "lyr/panic.h"
#include "lyr/poll.h"
#include "internal.h"

#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  include <process.h>
#else
#  include <pthread.h>
#  include <sched.h>
#endif

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

uint8_t lyr_task_resume(LyrCoro *co) {
    int status = lyr_coro_resume_quiet(co);
    return (uint8_t)(status == LYR_CORO_PANICKED ? 2 : status == LYR_CORO_DONE ? 1 : 0);
}

LyrStr *lyr_task_panic_code(LyrCoro *co) { return co->panic_code; }
LyrStr *lyr_task_panic_message(LyrCoro *co) { return co->panic_message; }
LyrStr *lyr_task_panic_trace(LyrCoro *co) { return co->panic_trace; }

void lyr_task_repanic(LyrStr *code, LyrStr *message, LyrStr *trace) {
    lyr_panic_again((const char *)code->bytes, (const char *)message->bytes, (const char *)trace->bytes);
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

/* --- threads (06 G1, G2) ----------------------------------------------------------------------- */

/* What a new thread runs. The environment is a root until the thread holds it: between the start
 * and the thread's attach no stack the collector scans has it. */
typedef struct ThreadStart {
    void (*code)(void *env, LyrErr **error);
    LyrRoot *env;
} ThreadStart;

static void lyr_thread_run(ThreadStart *start) {
    if (lyr_thread_attach() != 0) lyr_panic(LYR_RT_SYSTEM, "a thread could not attach to the collector");
    void (*code)(void *env, LyrErr **error) = start->code;
    void *env = lyr_root_get(start->env);
    lyr_root_free(start->env);
    free(start);
    code(env, NULL);
    lyr_poller_release();
    lyr_thread_detach();
}

#ifdef _WIN32
static unsigned __stdcall lyr_thread_entry(void *arg) {
    lyr_thread_run(arg);
    return 0;
}
#else
static void *lyr_thread_entry(void *arg) {
    lyr_thread_run(arg);
    return NULL;
}
#endif

void lyr_thread_start(void (*code)(void *env, LyrErr **error), void *env) {
    ThreadStart *start = malloc(sizeof *start);
    if (start == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory starting a thread");
    start->code = code;
    start->env = lyr_root_new(env);
#ifdef _WIN32
    uintptr_t handle = _beginthreadex(NULL, 0, lyr_thread_entry, start, 0, NULL);
    if (handle == 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a thread (error %lu)", (unsigned long)GetLastError());
    CloseHandle((HANDLE)handle);
#else
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_DETACHED);
    pthread_t thread;
    int failed = pthread_create(&thread, &attr, lyr_thread_entry, start);
    pthread_attr_destroy(&attr);
    if (failed != 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a thread: %s", strerror(failed));
#endif
}

int64_t lyr_task_poller(void) { return (int64_t)(intptr_t)lyr_poller_current(); }

void lyr_task_wake(int64_t poller) { lyr_poller_wake((LyrPoller *)(intptr_t)poller); }

void lyr_task_spin(void) {
#ifdef _WIN32
    SwitchToThread();
#else
    sched_yield();
#endif
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
