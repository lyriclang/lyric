/* The conversions std.ffi's types make at a C function's boundary. */
#include "lyr/ffi.h"
#include "lyr/coro.h"
#include "lyr/gc.h"
#include "lyr/panic.h"
#include "lyr/string.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#include "lyr/poll.h"

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
