/* The conversions std.ffi's types make at a C function's boundary. */
#include "lyr/ffi.h"
#include "lyr/console.h"
#include "lyr/coro.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include "lyr/panic.h"
#include "lyr/string.h"
#include "lyr/task.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#include "lyr/poll.h"
#include "internal.h"

uint64_t lyr_ffi_string_address(const LyrStr *s) { return (uint64_t)(uintptr_t)s->bytes; }

int64_t lyr_ffi_cstr_length(uint64_t address) {
    return (int64_t)strlen((const char *)(uintptr_t)address);
}

int64_t lyr_ffi_cstr_invalid(uint64_t address) {
    const char *text = (const char *)(uintptr_t)address;
    int64_t bad = -1;
    return lyr_utf8_valid(text, (int64_t)strlen(text), &bad) ? -1 : bad;
}

LyrStr *lyr_ffi_cstr_string(uint64_t address) {
    return lyr_str_from_cstr((const char *)(uintptr_t)address);
}

uint64_t lyr_ffi_alloc(int64_t count, uint64_t size) {
    if (count < 0) lyr_panic(LYR_RT_ARGUMENT, "alloc of %lld values: a count is never negative", (long long)count);
    /* calloc says where count * size overflows; one byte where nothing is asked, never null */
    void *memory = calloc(count == 0 ? 1 : (size_t)count, size == 0 ? 1 : (size_t)size);
    if (memory == NULL)
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "alloc of %lld values of %llu bytes: C's heap has no memory left",
                  (long long)count, (unsigned long long)size);
    return (uint64_t)(uintptr_t)memory;
}

void lyr_ffi_free(uint64_t address) { free((void *)(uintptr_t)address); }

void lyr_ffi_callback_enter(void) {
    if (!lyr_thread_attached())
        lyr_panic(LYR_RT_FOREIGN_THREAD, "C called a Lyric function on a thread Lyric does not run — "
                                         "a Callback that returns nothing goes to the thread that made it");
    lyr_coro_enter_foreign();
}

void lyr_ffi_callback_leave(void) { lyr_coro_leave_foreign(); }

/* 0: the library has not started, 1: a thread starts it, 2: started */
static atomic_int library_state;

static void library_start(void (*start)(void)) {
    if (start == NULL || atomic_load_explicit(&library_state, memory_order_acquire) == 2) return;
    int expected = 0;
    if (atomic_compare_exchange_strong(&library_state, &expected, 1)) {
        start();
        atomic_store_explicit(&library_state, 2, memory_order_release);
        return;
    }
    while (atomic_load_explicit(&library_state, memory_order_acquire) != 2) lyr_task_spin();
}

/* The library's embedding, once its first export ran: what lyr_step, lyr_run and
 * lyr_detach_thread call. */
static _Atomic(const LyrEmbedding *) embedding_here;

/* Whether an export attached the thread: lyr_detach_thread gives back only such a one. */
static _Thread_local int attached_by_export;

/* What failed: the thread's record for lyr_take_error, one link per error of the chain, each
 * with its strings in one block behind it; the one waiting, and the one taken last, freed at the
 * next take. */
static _Thread_local LyrError *waiting_error, *taken_error;

static void free_error(LyrError *e) {
    while (e != NULL) {
        LyrError *cause = (LyrError *)e->cause;
        free(e);
        e = cause;
    }
}

/* A link of the record, its strings copied behind it; NULL where C's heap has no room. */
static LyrError *error_link(LyrStatus status, const char *type, size_t type_len, const char *message,
                            size_t message_len, const char *trace) {
    size_t trace_len = trace != NULL ? strlen(trace) : 0;
    LyrError *e = malloc(sizeof *e + type_len + 1 + message_len + 1 + (trace != NULL ? trace_len + 1 : 0));
    if (e == NULL) return NULL;
    char *at = (char *)(e + 1);
    e->status = status;
    memcpy(at, type, type_len);
    at[type_len] = '\0';
    e->type = at;
    at += type_len + 1;
    memcpy(at, message, message_len);
    at[message_len] = '\0';
    e->message = at;
    at += message_len + 1;
    e->trace = trace != NULL ? memcpy(at, trace, trace_len + 1) : NULL;
    e->cause = NULL;
    return e;
}

static void keep(LyrError *e) {
    free_error(waiting_error);
    waiting_error = e;
}

/* The type a value of Error is: its table's first word is the concrete type's descriptor — a
 * value type's box's, `box<…>` around the type's name. */
static const char *type_of(LyrIface value, size_t *len) {
    const LyrDesc *desc = *(const LyrDesc *const *)value.vt;
    const char *name = desc != NULL && desc->name != NULL ? desc->name : "";
    *len = strlen(name);
    if (*len > 5 && strncmp(name, "box<", 4) == 0 && name[*len - 1] == '>') {
        *len -= 5;
        return name + 4;
    }
    return name;
}

void lyr_export_keep_error(const LyrErr *err, LyrErrMessage message, LyrErrCause cause) {
    LyrError *first = NULL, **link = &first;
    LyrIface at = err->value, next;
    /* the chain is the program's data: a cycle in it ends here, as the report bounds it */
    for (int depth = 0; depth < 64; depth++) {
        const LyrStr *text = message != NULL ? message(at) : NULL;
        size_t type_len;
        const char *type = type_of(at, &type_len);
        LyrError *e = error_link(LYR_ERROR, type, type_len, text != NULL ? (const char *)text->bytes : "",
                                 text != NULL ? (size_t)text->len : 0, NULL);
        if (e == NULL) break;
        *link = e;
        link = (LyrError **)&e->cause;
        if (cause == NULL || !cause(at, &next)) break;
        at = next;
    }
    keep(first);
}

static void keep_panic(LyrCoro *co) {
    const LyrStr *code = co->panic_code, *message = co->panic_message, *trace = co->panic_trace;
    keep(error_link(LYR_PANIC, code != NULL ? (const char *)code->bytes : "", code != NULL ? (size_t)code->len : 0,
                    message != NULL ? (const char *)message->bytes : "", message != NULL ? (size_t)message->len : 0,
                    trace != NULL ? (const char *)trace->bytes : ""));
}

LyrStatus lyr_take_error(LyrError *error) {
    free_error(taken_error);
    taken_error = waiting_error;
    waiting_error = NULL;
    if (taken_error == NULL) {
        memset(error, 0, sizeof *error);
        return LYR_OK;
    }
    *error = *taken_error;
    return taken_error->status;
}

/* An export's task: the call. */
typedef struct ExportRun {
    void (*body)(void *frame);
    void *frame;
} ExportRun;

static void export_run(void *arg) {
    ExportRun *run = arg;
    run->body(run->frame);
}

static const LyrDesc export_desc = { .size = 0, .flags = LYR_DESC_CORO, .name = "<export>" };

LyrStatus lyr_export_call(void (*start)(void), const LyrEmbedding *embedding, void (*body)(void *frame),
                          void *frame, int throws) {
    if (!lyr_started())
        lyr_panic(LYR_RT_FOREIGN_THREAD, "C called an export before lyr_init started the runtime");
    if (!lyr_thread_attached()) {
        if (lyr_thread_attach() != 0) lyr_panic(LYR_RT_SYSTEM, "a thread could not attach to the collector");
        attached_by_export = 1;
    }
    if (lyr_coro_current() != NULL) {
        /* Lyric below, C between: the task that runs owns the call, and its frames count C's */
        lyr_coro_enter_foreign();
        library_start(start);
        body(frame);
        lyr_coro_leave_foreign();
        return LYR_OK;
    }
    /* the globals before the scheduler: std.task's loop reads its module's own */
    library_start(start);
    if (embedding != NULL) atomic_store_explicit(&embedding_here, embedding, memory_order_release);
    ExportRun run = { body, frame };
    LyrCoro *co = lyr_coro_new(&export_desc, export_run, &run, 0);
    co->task = 1;
    int64_t state = embedding != NULL ? embedding->run(co) : lyr_task_resume(co);
    /* what it wrote, out before C goes on */
    lyr_console_flush_all(1);
    if (state == 1) return LYR_OK;
    if (state == 0) lyr_panic(LYR_RT_INVARIANT, "an export's task waited where no scheduler runs");
    if (!throws) lyr_coro_repanic(co);
    keep_panic(co);
    return LYR_PANIC;
}

bool lyr_step(void) {
    const LyrEmbedding *e = atomic_load_explicit(&embedding_here, memory_order_acquire);
    if (e == NULL || !lyr_started() || !lyr_thread_attached() || lyr_coro_current() != NULL) return false;
    bool remain = e->step() != 0;
    lyr_console_flush_all(1);
    return remain;
}

void lyr_run(void) {
    const LyrEmbedding *e = atomic_load_explicit(&embedding_here, memory_order_acquire);
    if (e == NULL || !lyr_started() || !lyr_thread_attached() || lyr_coro_current() != NULL) return;
    e->pump();
    lyr_console_flush_all(1);
}

void lyr_detach_thread(void) {
    if (!attached_by_export || lyr_coro_current() != NULL) return;
    const LyrEmbedding *e = atomic_load_explicit(&embedding_here, memory_order_acquire);
    if (e != NULL) e->end();
    free_error(waiting_error);
    free_error(taken_error);
    waiting_error = taken_error = NULL;
    lyr_poller_release();
    lyr_thread_detach();
    attached_by_export = 0;
}

/* word 2 of the object, the environment, is a reference; the code is not the collector's */
static const uint64_t closure_refs[1] = { 1u << 2 };
static const LyrDesc closure_desc = {
    sizeof(LyrFfiClosure), LYR_DESC_HAS_REFS, 0, 1, closure_refs, "std.ffi.Callback", NULL,
};

/* What C's threads hand a thread that owns Callbacks, under its lock. */
typedef struct LyrFfiCall {
    struct LyrFfiCall *next;
    void (*run)(void *);
    void *payload;
} LyrFfiCall;

struct LyrFfiOwner {
    atomic_flag lock;
    LyrFfiCall *first, *last;
    LyrPoller *poller;           /* woken for each call handed over */
    atomic_int_fast64_t alive;   /* its Callbacks not released */
    atomic_bool drains;          /* it ran a scheduler's turn, and so will run what it is handed */
};

static _Thread_local LyrFfiOwner *owner_here;

/* The thread ran a scheduler's turn, which runs what it is handed: said before its first Callback
 * may exist, and taken over by the owner record made later — a C thread may call at once. */
static _Thread_local bool thread_drains;

/* The calling thread's owner record, made at its first Callback; it lives as long as the process,
 * since C may hold a Callback's pointer past the thread. */
static LyrFfiOwner *this_owner(void) {
    if (owner_here == NULL) {
        LyrFfiOwner *made = calloc(1, sizeof *made);
        if (made == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "no memory left for a Callback's owner");
        atomic_flag_clear(&made->lock);
        made->poller = lyr_poller_current();
        atomic_store(&made->drains, thread_drains);
        owner_here = made;
    }
    return owner_here;
}

static void owner_lock(LyrFfiOwner *owner) {
    while (atomic_flag_test_and_set_explicit(&owner->lock, memory_order_acquire)) { }
}

static void owner_unlock(LyrFfiOwner *owner) { atomic_flag_clear_explicit(&owner->lock, memory_order_release); }

uint64_t lyr_ffi_closure_root(void *code, void *env) {
    LyrFfiClosure *closure = lyr_alloc(&closure_desc);
    closure->code = code;
    LYR_WRITE_BARRIER(closure, &closure->env, env);
    closure->owner = this_owner();
    atomic_fetch_add(&closure->owner->alive, 1);
    return (uint64_t)(uintptr_t)lyr_root_new(closure);
}

void lyr_ffi_callback_release(uint64_t root) {
    LyrFfiClosure *closure = lyr_root_get((const LyrRoot *)(uintptr_t)root);
    if (closure != NULL) atomic_fetch_sub(&closure->owner->alive, 1);
    lyr_root_free((LyrRoot *)(uintptr_t)root);
}

void *lyr_ffi_call_alloc(size_t size) {
    void *memory = malloc(size);
    if (memory == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "no memory left to hand a callback to its owner");
    return memory;
}

void lyr_ffi_enqueue(LyrFfiOwner *owner, void (*run)(void *), void *payload) {
    if (!atomic_load(&owner->drains)) {
        free(payload);
        lyr_panic(LYR_RT_FOREIGN_THREAD,
                  "C called a Lyric function on a thread Lyric does not run, and its owner runs no scheduler to hand it to");
    }
    LyrFfiCall *call = malloc(sizeof *call);
    if (call == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "no memory left to hand a callback to its owner");
    call->next = NULL;
    call->run = run;
    call->payload = payload;
    owner_lock(owner);
    if (owner->last != NULL) owner->last->next = call; else owner->first = call;
    owner->last = call;
    owner_unlock(owner);
    lyr_poller_wake(owner->poller);
}

void lyr_ffi_drain(void) {
    thread_drains = true;
    LyrFfiOwner *owner = owner_here;
    if (owner == NULL) return;  /* it made no Callback: nothing can come */
    atomic_store(&owner->drains, true);
    owner_lock(owner);
    LyrFfiCall *calls = owner->first;
    owner->first = owner->last = NULL;
    owner_unlock(owner);
    while (calls != NULL) {
        LyrFfiCall *next = calls->next;
        calls->run(calls->payload);
        free(calls->payload);
        free(calls);
        calls = next;
    }
}

int64_t lyr_ffi_callbacks_alive(void) { return owner_here != NULL ? atomic_load(&owner_here->alive) : 0; }

void lyr_ffi_closure_release(uint64_t root) { lyr_root_free((LyrRoot *)(uintptr_t)root); }

void *lyr_ffi_rooted(uint64_t root, const LyrDesc *desc) {
    LyrObj *object = lyr_root_get((const LyrRoot *)(uintptr_t)root);
    if (object == NULL) lyr_panic(LYR_RT_FOREIGN_HANDLE, "a GcHandle that holds nothing, read as a handle of %s", desc->name);
    if (object->desc != desc)
        lyr_panic(LYR_RT_FOREIGN_HANDLE, "a GcHandle of %s read as a handle of %s", object->desc->name, desc->name);
    return object;
}

int64_t lyr_ffi_enum_tag(int64_t value, int64_t variants, const char *enum_name) {
    if (value < 0 || value >= variants)
        lyr_panic(LYR_RT_FOREIGN_ENUM, "C returned %lld, which is no variant of %s (it has %lld)",
                  (long long)value, enum_name, (long long)variants);
    return value;
}

int64_t lyr_ffi_enum_index(int64_t value, const int64_t *values, int64_t variants, const char *enum_name) {
    for (int64_t i = 0; i < variants; i++)
        if (values[i] == value) return i;
    lyr_panic(LYR_RT_FOREIGN_ENUM, "C returned %lld, which is no variant of %s (it has %lld)",
              (long long)value, enum_name, (long long)variants);
}
