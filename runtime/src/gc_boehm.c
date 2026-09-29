/* Stage 1 of the collector contract: the Boehm–Demers–Weiser collector behind lyr/gc.h.
 * Objects whose descriptor has no references are allocated "atomic": Boehm neither scans them nor
 * zeroes them, so they are zeroed here where the contract promises it. */
#include "lyr/gc.h"
#include "lyr/panic.h"

#include <gc.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

static int initialized;

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
    /* Death callbacks run at known points (an allocation or an explicit collection on the thread
     * that notices them), never inside the collector. */
    GC_set_finalize_on_demand(1);
    GC_set_finalizer_notifier(notify_callbacks);
    initialized = 1;
}

static void *allocate(size_t bytes, int has_refs, int zero) {
    if (LYR_UNLIKELY(atomic_load_explicit(&callbacks_pending, memory_order_relaxed))) run_callbacks();
    void *memory = has_refs ? GC_MALLOC(bytes) : GC_MALLOC_ATOMIC(bytes);
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
    LyrRoot *root = GC_MALLOC_UNCOLLECTABLE(sizeof *root);
    if (LYR_UNLIKELY(root == NULL)) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory allocating a root");
    root->object = object;
    return root;
}

void *lyr_root_get(const LyrRoot *root) { return root->object; }
void lyr_root_set(LyrRoot *root, void *object) { root->object = object; }
void lyr_root_free(LyrRoot *root) { GC_FREE(root); }

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
    return result == GC_SUCCESS || result == GC_DUPLICATE ? 0 : -1;
}

void lyr_thread_detach(void) {
    GC_unregister_my_thread();
}

/* --- collection -------------------------------------------------------------------------------- */

void lyr_gc_collect(void) {
    GC_gcollect();
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
