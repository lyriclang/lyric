/* Stage 1 of the collector contract: the Boehm–Demers–Weiser collector behind lyr/gc.h.
 * Objects whose descriptor has no references are allocated "atomic": Boehm neither scans them nor
 * zeroes them, so they are zeroed here where the contract promises it. */
#include "lyr/gc.h"
#include "lyr/panic.h"
#include "internal.h"

#include <gc.h>
#include <gc_mark.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <sched.h>
#endif

static int initialized;

static void init_coroutine_kind(void);
static void GC_CALLBACK on_collection_event(GC_EventType event);

/* Under ASan (the asan profile, 01 C7) two of its defaults fight a conservative collector: fake
 * stacks move locals into memory the collector's stack scan never sees, so live objects would be
 * collected; and LeakSanitizer does not scan the collector's heap, so malloc blocks referenced
 * only from there (weak records, via the finalizer table) would be reported as leaks. */
#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
const char *__asan_default_options(void);
const char *__asan_default_options(void) {
    return "detect_stack_use_after_return=0:detect_leaks=0";
}
#  endif
/* Under TSan (the tsan profile) the collector is uninstrumented, so TSan sees neither its lock nor
 * the stopped world — only memory that one thread wrote and, after a collection recycled it,
 * another thread received. A collection does order those: it stops every thread (signals and
 * semaphores) before it decides what is garbage, and restarts them after. The runtime tells TSan
 * so, instead of silencing the reports: each allocation publishes what its thread wrote before
 * it, the stopped collector takes all of that over, and it publishes its own view before the
 * restart, which each allocation takes over after. A race that no collection separates is still
 * reported. What is left is memory the collector maps or sweeps itself (its frames: GC_*). */
#  if __has_feature(thread_sanitizer)
#    include <sanitizer/tsan_interface.h>
#    define LYR_TSAN 1
const char *__tsan_default_suppressions(void);
const char *__tsan_default_suppressions(void) {
    return "race:GC_*\n";
}
static char tsan_program_side, tsan_collector_side;  /* only their addresses matter */
static void tsan_on_collection(GC_EventType event) {
    if (event == GC_EVENT_POST_STOP_WORLD) __tsan_acquire(&tsan_program_side);
    else if (event == GC_EVENT_PRE_START_WORLD) __tsan_release(&tsan_collector_side);
}
#  endif
#endif
#ifdef LYR_TSAN
#  define TSAN_PUBLISH() __tsan_release(&tsan_program_side)
#  define TSAN_RECEIVE() __tsan_acquire(&tsan_collector_side)
/* An explicit free hands a cell back without a collection: the allocation that gets it next sees
 * what the freeing thread wrote, as after a collection — a root that one thread frees and another
 * allocates again (a thread's start, M6 S6b). */
#  define TSAN_RETURN() __tsan_release(&tsan_collector_side)
#else
#  define TSAN_PUBLISH() ((void)0)
#  define TSAN_RECEIVE() ((void)0)
#  define TSAN_RETURN() ((void)0)
#endif

/* Set by the collector when finalizers are ready; drained on the allocating thread. */
static atomic_int callbacks_pending;
static _Thread_local int draining;

static void notify_callbacks(void) {
    atomic_store_explicit(&callbacks_pending, 1, memory_order_relaxed);
}

static void run_callbacks(void) {
    if (draining) return;
    draining = 1;
    atomic_store_explicit(&callbacks_pending, 0, memory_order_relaxed);
    GC_invoke_finalizers();
    draining = 0;
}

void lyr_gc_init(void) {
    if (initialized) return;
    GC_INIT();
    GC_allow_register_threads();
    /* The collector's warnings ("Out of Memory! Returning NULL!", "Repeated allocation of very
     * large block") are not a Lyric program's output: what matters reaches the program as a
     * panic (RT0005) with a message of the runtime's own. */
    GC_set_warn_proc(GC_ignore_warn_proc);
    /* Death callbacks run at known points (an allocation or an explicit collection on the thread
     * that notices them), never inside the collector. */
    GC_set_finalize_on_demand(1);
    GC_set_finalizer_notifier(notify_callbacks);
    GC_set_on_collection_event(on_collection_event);
    init_coroutine_kind();
    initialized = 1;
}

static void *allocate(size_t bytes, int has_refs, int zero) {
    if (LYR_UNLIKELY(atomic_load_explicit(&callbacks_pending, memory_order_relaxed))) run_callbacks();
    TSAN_PUBLISH();
    void *memory = has_refs ? GC_MALLOC(bytes) : GC_MALLOC_ATOMIC(bytes);
    TSAN_RECEIVE();
    if (LYR_UNLIKELY(memory == NULL)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating %zu bytes (heap %zu bytes)",
                  bytes, (size_t)GC_get_heap_size());
    }
    if (!has_refs && zero) memset(memory, 0, bytes);
    return memory;
}

void *lyr_alloc(const LyrDesc *desc) {
    LyrObj *object = allocate(desc->size, (desc->flags & LYR_DESC_HAS_REFS) != 0, 1);
    object->desc = desc;
    return object;
}

LyrArr *lyr_alloc_array(const LyrDesc *desc, int64_t count) {
    if (LYR_UNLIKELY(count < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "array length %lld is negative", (long long)count);
    }
    size_t fixed = offsetof(LyrArr, data);
    if (LYR_UNLIKELY(desc->elem_size != 0 && (uint64_t)count > (SIZE_MAX - fixed) / desc->elem_size)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "an array of %lld elements of %u bytes does not fit in memory",
                  (long long)count, desc->elem_size);
    }
    LyrArr *array = allocate(fixed + (size_t)count * desc->elem_size, (desc->flags & LYR_DESC_HAS_REFS) != 0, 1);
    array->header.desc = desc;
    array->len = count;
    return array;
}

LyrStr *lyr_alloc_string(int64_t len) {
    if (LYR_UNLIKELY(len < 0)) {
        lyr_panic(LYR_RT_ARGUMENT, "string length %lld is negative", (long long)len);
    }
    size_t fixed = offsetof(LyrStr, bytes) + 1;
    if (LYR_UNLIKELY((uint64_t)len > SIZE_MAX - fixed)) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "a string of %lld bytes does not fit in memory", (long long)len);
    }
    LyrStr *string = allocate(fixed + (size_t)len, 0, 0);
    string->header.desc = &lyr_desc_string;
    string->len = len;
    string->bytes[len] = '\0';
    return string;
}

/* --- roots ----------------------------------------------------------------------------------- */

/* An uncollectable cell: scanned by the collector, never freed by it. */
struct LyrRoot {
    void *object;
};

LyrRoot *lyr_root_new(void *object) {
    TSAN_PUBLISH();
    LyrRoot *root = GC_MALLOC_UNCOLLECTABLE(sizeof *root);
    TSAN_RECEIVE();
    if (LYR_UNLIKELY(root == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a root");
    root->object = object;
    return root;
}

void *lyr_root_get(const LyrRoot *root) { return root->object; }
void lyr_root_set(LyrRoot *root, void *object) { root->object = object; }
void lyr_root_free(LyrRoot *root) {
    TSAN_RETURN();
    GC_FREE(root);
}

void lyr_register_stack(void *low, void *high) { GC_add_roots(low, high); }
void lyr_unregister_stack(void *low, void *high) { GC_remove_roots(low, high); }

/* --- weak references ------------------------------------------------------------------------- */

/* Lives in malloc memory, which the collector does not scan: the pointer in it is a disappearing
 * link, cleared when the object dies, never a reason for it to live. Two parties use the record —
 * the caller (until lyr_weak_free) and the object's finalizer (until it ran) — so it is freed by
 * whichever of the two finishes second; they may run on different threads. Boehm keeps one
 * finalizer per object; a second weak reference to the same object chains the first. */
enum { WEAK_RELEASED = 1, WEAK_FINALIZED = 2 };

struct LyrWeak {
    void *object;
    _Atomic(LyrWeakCallback) on_death;
    void *context;
    GC_finalization_proc previous;
    void *previous_data;
    atomic_int state;
};

static void finish(LyrWeak *weak, int part) {
    int before = atomic_fetch_or(&weak->state, part);
    if ((before | part) == (WEAK_RELEASED | WEAK_FINALIZED)) free(weak);
}

static void finalize(void *object, void *data) {
    LyrWeak *weak = data;
    LyrWeakCallback on_death = atomic_load(&weak->on_death);
    void *context = weak->context;
    GC_finalization_proc previous = weak->previous;
    void *previous_data = weak->previous_data;
    finish(weak, WEAK_FINALIZED);
    if (on_death) on_death(context);
    if (previous) previous(object, previous_data);
}

LyrWeak *lyr_weak_new(void *object, LyrWeakCallback on_death, void *context) {
    LyrWeak *weak = malloc(sizeof *weak);
    if (LYR_UNLIKELY(weak == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a weak reference");
    weak->object = object;
    atomic_init(&weak->on_death, on_death);
    weak->context = context;
    atomic_init(&weak->state, 0);
    GC_general_register_disappearing_link(&weak->object, object);
    GC_register_finalizer_no_order(object, finalize, weak, &weak->previous, &weak->previous_data);
    return weak;
}

static void *read_link(void *data) {
    return ((LyrWeak *)data)->object;
}

void *lyr_weak_get(const LyrWeak *weak) {
    /* The collector clears the link while other threads may run: read it under its lock. */
    return GC_call_with_alloc_lock(read_link, (void *)weak);
}

void lyr_weak_free(LyrWeak *weak) {
    /* The callback stops here; the record itself goes when the finalizer is done with it too. */
    atomic_store(&weak->on_death, (LyrWeakCallback)NULL);
    GC_unregister_disappearing_link(&weak->object);
    finish(weak, WEAK_RELEASED);
}

/* --- threads ----------------------------------------------------------------------------------- */

int lyr_thread_attach(void) {
    struct GC_stack_base base;
    if (GC_get_stack_base(&base) != GC_SUCCESS) return -1;
    int result = GC_register_my_thread(&base);
    if (result != GC_SUCCESS && result != GC_DUPLICATE) return -1;
    /* The thread's net for faults and stack overflow (crash.c), when the handlers are installed;
     * a thread the collector knew already (the main thread) has it from lyr_init. */
    if (result == GC_SUCCESS) lyr_crash_thread_start();
    return 0;
}

void lyr_thread_detach(void) {
    TSAN_PUBLISH();  /* the thread's last writes, for the collection that recycles them */
    lyr_coro_thread_end();
    lyr_crash_thread_end();
    GC_unregister_my_thread();
}

/* --- coroutines (01 L4, 06 A5) ------------------------------------------------------------------ */

/* A coroutine object is of a kind of its own. Its mark procedure traces its argument and its
 * resumer and, while its stack is mapped and stopped, every word of that stack from the saved
 * stack pointer up, conservatively, as a thread's stack is scanned: what a suspended coroutine's
 * frames hold lives as long as the coroutine, and an abandoned one dies with everything only its
 * stack held. Not instrumented: it reads frames that are not its own, redzones included. */
static unsigned coroutine_kind;

static LYR_NO_SANITIZE struct GC_ms_entry *mark_coroutine(GC_word *addr, struct GC_ms_entry *top,
                                                          struct GC_ms_entry *limit, GC_word env) {
    (void)env;
    LyrCoro *co = (LyrCoro *)addr;
    if (co->status == 0) return top;  /* on a free list: cleared but for its first word */
    top = GC_MARK_AND_PUSH(co->arg, top, limit, &co->arg);
    top = GC_MARK_AND_PUSH(co->resumer_coro, top, limit, (void **)&co->resumer_coro);
    top = GC_MARK_AND_PUSH(co->error, top, limit, (void **)&co->error);
    top = GC_MARK_AND_PUSH(co->parked_top, top, limit, (void **)&co->parked_top);
    top = GC_MARK_AND_PUSH(co->panic_code, top, limit, (void **)&co->panic_code);
    top = GC_MARK_AND_PUSH(co->panic_message, top, limit, (void **)&co->panic_message);
    top = GC_MARK_AND_PUSH(co->panic_trace, top, limit, (void **)&co->panic_trace);
    if (co->mapping != NULL && !co->ctx.on_cpu) {
        for (void **word = co->ctx.sp; word < (void **)co->ctx.base; word++) {
            top = GC_MARK_AND_PUSH(*word, top, limit, word);
        }
    }
    return top;
}

static void init_coroutine_kind(void) {
    coroutine_kind = GC_new_kind(GC_new_free_list(), GC_MAKE_PROC(GC_new_proc(mark_coroutine), 0), 0, 1);
}

LyrCoro *lyr_gc_alloc_coro(void) {
    if (LYR_UNLIKELY(atomic_load_explicit(&callbacks_pending, memory_order_relaxed))) run_callbacks();
    TSAN_PUBLISH();
    LyrCoro *co = GC_generic_malloc(sizeof(LyrCoro), (int)coroutine_kind);
    TSAN_RECEIVE();
    if (LYR_UNLIKELY(co == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a coroutine");
    return co;
}

static void GC_CALLBACK coroutine_died(void *object, void *data) {
    (void)data;
    lyr_coro_abandoned(object);
}

void lyr_gc_watch_coro(LyrCoro *co) { GC_register_finalizer_no_order(co, coroutine_died, NULL, NULL, NULL); }
void lyr_gc_unwatch_coro(LyrCoro *co) { GC_register_finalizer_no_order(co, NULL, NULL, NULL, NULL); }

void *lyr_gc_thread_stack(void **base) {
    struct GC_stack_base bottom;
    memset(&bottom, 0, sizeof bottom);
    void *handle = GC_get_my_stackbottom(&bottom);
    *base = bottom.mem_base;
    return handle;
}

/* A thread's own stack while a coroutine runs on the thread: no longer what the collector scans
 * as the thread's stack, so a root of its own, from where it stopped up. Registered under the
 * collector's lock, read with the world stopped. */
static LyrStackCtx **thread_stacks;
static size_t thread_stack_count, thread_stack_capacity;
static int pushing_thread_stacks;
static GC_push_other_roots_proc next_push_other_roots;

static LYR_NO_SANITIZE void GC_CALLBACK push_stopped_thread_stacks(void) {
    for (size_t i = 0; i < thread_stack_count; i++) {
        LyrStackCtx *own = thread_stacks[i];
        if (!own->on_cpu) GC_push_all_eager(own->sp, own->base);
    }
    if (next_push_other_roots) next_push_other_roots();
}

static void *GC_CALLBACK add_thread_stack(void *own) {
    if (thread_stack_count == thread_stack_capacity) {
        size_t capacity = thread_stack_capacity ? thread_stack_capacity * 2 : 8;
        LyrStackCtx **grown = realloc(thread_stacks, capacity * sizeof *grown);
        if (grown == NULL) return NULL;
        thread_stacks = grown;
        thread_stack_capacity = capacity;
    }
    if (!pushing_thread_stacks) {
        next_push_other_roots = GC_get_push_other_roots();
        GC_set_push_other_roots(push_stopped_thread_stacks);
        pushing_thread_stacks = 1;
    }
    thread_stacks[thread_stack_count++] = own;
    return own;
}

void lyr_gc_add_thread_stack(LyrStackCtx *own) {
    if (GC_call_with_alloc_lock(add_thread_stack, own) == NULL) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory registering a thread's stack");
    }
}

static void *GC_CALLBACK remove_thread_stack(void *own) {
    for (size_t i = 0; i < thread_stack_count; i++) {
        if (thread_stacks[i] == own) {
            thread_stacks[i] = thread_stacks[--thread_stack_count];
            break;
        }
    }
    return NULL;
}

void lyr_gc_remove_thread_stack(LyrStackCtx *own) { GC_call_with_alloc_lock(remove_thread_stack, own); }

/* A switch moves the thread from one stack to another, and with it what the collector must scan as
 * the thread's stack: from the stack pointer up to that stack's bottom. Between the move of the
 * bottom and the move of the stack pointer the two disagree, so the world must not stop there.
 * A switch is a reader of a lock of the runtime's own and a collection its writer, taken before
 * the world stops and given back once it runs again: switches on different threads never wait
 * for one another, a collection waits only for the switches under way, and a switch waits only
 * while a collection runs. Inside, the bottom moves through the thread's handle, without the
 * collector's lock, which the collector — its only reader — cannot hold then. No mutex is held
 * across the switch: the thread arrives on another stack, which a sanitizer would take for
 * another thread. */
static atomic_int switches_under_way;
static atomic_int world_stopping;

static void relax(unsigned *spins) {
    if (++*spins < 64) return;
#ifdef _WIN32
    SwitchToThread();
#else
    sched_yield();
#endif
}

void lyr_gc_switch_begin(void *gc_handle, const LyrStackCtx *to) {
    unsigned spins = 0;
    for (;;) {
        atomic_fetch_add(&switches_under_way, 1);
        if (!atomic_load(&world_stopping)) break;
        atomic_fetch_sub(&switches_under_way, 1);
        while (atomic_load(&world_stopping)) relax(&spins);
    }
    struct GC_stack_base bottom;
    memset(&bottom, 0, sizeof bottom);
    bottom.mem_base = to->base;
    GC_set_stackbottom(gc_handle, &bottom);
}

void lyr_gc_switch_end(void) { atomic_fetch_sub(&switches_under_way, 1); }

static void GC_CALLBACK on_collection_event(GC_EventType event) {
    if (event == GC_EVENT_PRE_STOP_WORLD) {
        atomic_store(&world_stopping, 1);
        unsigned spins = 0;
        while (atomic_load(&switches_under_way) != 0) relax(&spins);
    } else if (event == GC_EVENT_POST_START_WORLD) {
        atomic_store(&world_stopping, 0);
    }
#ifdef LYR_TSAN
    tsan_on_collection(event);
#endif
}

/* --- collection -------------------------------------------------------------------------------- */

void lyr_gc_collect(void) {
    TSAN_PUBLISH();
    GC_gcollect();
    TSAN_RECEIVE();
    run_callbacks();
}

size_t lyr_gc_heap_size(void) {
    return GC_get_heap_size();
}

size_t lyr_gc_collections(void) {
    return (size_t)GC_get_gc_no();
}

void lyr_gc_set_heap_limit(size_t bytes) {
    GC_set_max_heap_size(bytes);
}
