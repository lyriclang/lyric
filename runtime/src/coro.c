/* Stackful coroutines (design/v5/spec/01 L4, 06 N2): their stacks, the switch between stacks, and
 * what the collector, the sanitizers and the crash net learn at every switch. */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE 1
#elif defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#  define _DARWIN_C_SOURCE 1
#endif

#include "lyr/coro.h"
#include "lyr/gc.h"
#include "lyr/panic.h"
#include "internal.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <sys/mman.h>
#  include <unistd.h>
#endif

#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
#    include <sanitizer/asan_interface.h>
#    include <sanitizer/common_interface_defs.h>
#    define LYR_ASAN 1
#  endif
#  if __has_feature(thread_sanitizer)
#    include <sanitizer/tsan_interface.h>
#    define LYR_TSAN 1
#  endif
#endif

/* --- the switch (01 K2) -------------------------------------------------------------------------
 * lyr_ctx_switch(save, load) pushes the callee-saved registers of the platform's ABI on the running
 * stack, stores the stack pointer in *save, takes `load` as the stack pointer, pops the registers
 * it finds there and returns into that stack. A new coroutine's stack is laid out as though it had
 * switched away at the start of lyr_coro_start, which hands the coroutine — left in a callee-saved
 * register — to lyr_coro_main. Both are this file's own; their symbols are hidden. */
void lyr_ctx_switch(void **save, void *load);
void lyr_coro_start(void);

#if defined(__APPLE__)
#  define SYM(name) "_" #name
#  define FUNC(name) ".globl " SYM(name) "\n.private_extern " SYM(name) "\n.p2align 4\n" SYM(name) ":\n"
#  define ENDF(name) ""
#elif defined(_WIN32)
#  define SYM(name) #name
#  define FUNC(name) ".globl " #name "\n.def " #name "; .scl 2; .type 32; .endef\n.p2align 4\n" #name ":\n"
#  define ENDF(name) ""
#else
#  define SYM(name) #name
#  define FUNC(name) ".globl " #name "\n.hidden " #name "\n.type " #name ", %function\n.p2align 4\n" #name ":\n"
#  define ENDF(name) ".size " #name ", .-" #name "\n"
#endif

#if defined(__x86_64__) && !defined(_WIN32)
/* System V (Linux, macOS): rbx, rbp, r12–r15, and the control words of SSE and x87. */
__asm__(
    ".text\n"
    FUNC(lyr_ctx_switch)
    "    pushq %rbp\n"
    "    pushq %rbx\n"
    "    pushq %r12\n"
    "    pushq %r13\n"
    "    pushq %r14\n"
    "    pushq %r15\n"
    "    subq $8, %rsp\n"
    "    stmxcsr (%rsp)\n"
    "    fnstcw 4(%rsp)\n"
    "    movq %rsp, (%rdi)\n"
    "    movq %rsi, %rsp\n"
    "    ldmxcsr (%rsp)\n"
    "    fldcw 4(%rsp)\n"
    "    addq $8, %rsp\n"
    "    popq %r15\n"
    "    popq %r14\n"
    "    popq %r13\n"
    "    popq %r12\n"
    "    popq %rbx\n"
    "    popq %rbp\n"
    "    ret\n"
    ENDF(lyr_ctx_switch)
    FUNC(lyr_coro_start)
    "    .cfi_startproc\n"
    "    .cfi_undefined %rip\n"
    "    movq %rbx, %rdi\n"
    "    call " SYM(lyr_coro_main) "\n"
    "    ud2\n"
    "    .cfi_endproc\n"
    ENDF(lyr_coro_start));
#elif defined(__x86_64__) && defined(_WIN32)
/* Win64: rbx, rbp, rdi, rsi, r12–r15, xmm6–xmm15, the control words — and the thread's stack as
 * the TEB records it (StackBase, StackLimit, DeallocationStack), which the system's stack checks
 * and the exception dispatcher read. */
__asm__(
    ".text\n"
    FUNC(lyr_ctx_switch)
    "    pushq %rbp\n"
    "    pushq %rbx\n"
    "    pushq %rdi\n"
    "    pushq %rsi\n"
    "    pushq %r12\n"
    "    pushq %r13\n"
    "    pushq %r14\n"
    "    pushq %r15\n"
    "    movq %gs:0x08, %rax\n"
    "    pushq %rax\n"
    "    movq %gs:0x10, %rax\n"
    "    pushq %rax\n"
    "    movq %gs:0x1478, %rax\n"
    "    pushq %rax\n"
    "    subq $168, %rsp\n"
    "    movups %xmm6, 0(%rsp)\n"
    "    movups %xmm7, 16(%rsp)\n"
    "    movups %xmm8, 32(%rsp)\n"
    "    movups %xmm9, 48(%rsp)\n"
    "    movups %xmm10, 64(%rsp)\n"
    "    movups %xmm11, 80(%rsp)\n"
    "    movups %xmm12, 96(%rsp)\n"
    "    movups %xmm13, 112(%rsp)\n"
    "    movups %xmm14, 128(%rsp)\n"
    "    movups %xmm15, 144(%rsp)\n"
    "    stmxcsr 160(%rsp)\n"
    "    fnstcw 164(%rsp)\n"
    "    movq %rsp, (%rcx)\n"
    "    movq %rdx, %rsp\n"
    "    movups 0(%rsp), %xmm6\n"
    "    movups 16(%rsp), %xmm7\n"
    "    movups 32(%rsp), %xmm8\n"
    "    movups 48(%rsp), %xmm9\n"
    "    movups 64(%rsp), %xmm10\n"
    "    movups 80(%rsp), %xmm11\n"
    "    movups 96(%rsp), %xmm12\n"
    "    movups 112(%rsp), %xmm13\n"
    "    movups 128(%rsp), %xmm14\n"
    "    movups 144(%rsp), %xmm15\n"
    "    ldmxcsr 160(%rsp)\n"
    "    fldcw 164(%rsp)\n"
    "    addq $168, %rsp\n"
    "    popq %rax\n"
    "    movq %rax, %gs:0x1478\n"
    "    popq %rax\n"
    "    movq %rax, %gs:0x10\n"
    "    popq %rax\n"
    "    movq %rax, %gs:0x08\n"
    "    popq %r15\n"
    "    popq %r14\n"
    "    popq %r13\n"
    "    popq %r12\n"
    "    popq %rsi\n"
    "    popq %rdi\n"
    "    popq %rbx\n"
    "    popq %rbp\n"
    "    ret\n"
    FUNC(lyr_coro_start)
    "    movq %rbx, %rcx\n"
    "    subq $32, %rsp\n"
    "    call lyr_coro_main\n"
    "    ud2\n");
#elif defined(__aarch64__)
/* AAPCS64 (Linux, macOS): x19–x28, the frame pointer and the link register, d8–d15, FPCR. x18 is
 * the platform's on macOS and left alone everywhere. */
__asm__(
    ".text\n"
    FUNC(lyr_ctx_switch)
    "    sub sp, sp, #176\n"
    "    stp x19, x20, [sp, #0]\n"
    "    stp x21, x22, [sp, #16]\n"
    "    stp x23, x24, [sp, #32]\n"
    "    stp x25, x26, [sp, #48]\n"
    "    stp x27, x28, [sp, #64]\n"
    "    stp x29, x30, [sp, #80]\n"
    "    stp d8, d9, [sp, #96]\n"
    "    stp d10, d11, [sp, #112]\n"
    "    stp d12, d13, [sp, #128]\n"
    "    stp d14, d15, [sp, #144]\n"
    "    mrs x9, fpcr\n"
    "    str x9, [sp, #160]\n"
    "    mov x9, sp\n"
    "    str x9, [x0]\n"
    "    mov sp, x1\n"
    "    ldp x19, x20, [sp, #0]\n"
    "    ldp x21, x22, [sp, #16]\n"
    "    ldp x23, x24, [sp, #32]\n"
    "    ldp x25, x26, [sp, #48]\n"
    "    ldp x27, x28, [sp, #64]\n"
    "    ldp x29, x30, [sp, #80]\n"
    "    ldp d8, d9, [sp, #96]\n"
    "    ldp d10, d11, [sp, #112]\n"
    "    ldp d12, d13, [sp, #128]\n"
    "    ldp d14, d15, [sp, #144]\n"
    "    ldr x9, [sp, #160]\n"
    "    msr fpcr, x9\n"
    "    add sp, sp, #176\n"
    "    ret\n"
    ENDF(lyr_ctx_switch)
    FUNC(lyr_coro_start)
    "    .cfi_startproc\n"
    "    .cfi_undefined x30\n"
    "    mov x0, x19\n"
    "    bl " SYM(lyr_coro_main) "\n"
    "    brk #0\n"
    "    .cfi_endproc\n"
    ENDF(lyr_coro_start));
#else
#  error "coroutines: no context switch for this architecture (01 K2 names x86-64 and AArch64)"
#endif

/* --- stacks (01 K1) -----------------------------------------------------------------------------
 * A coroutine's stack is mapped at its first resume: one guard page at the low end, never
 * accessible, and above it the stack, reserved — its pages cost memory once touched. A stack whose
 * coroutine ended goes to a small pool for the next one of the default size. */

/* Any thread may ask first; each finds the same answer. */
static size_t page_bytes(void) {
    static atomic_size_t cached;
    size_t page = atomic_load_explicit(&cached, memory_order_relaxed);
    if (page == 0) {
#ifdef _WIN32
        SYSTEM_INFO info;
        GetSystemInfo(&info);
        page = info.dwPageSize;
#else
        long size = sysconf(_SC_PAGESIZE);
        page = size > 0 ? (size_t)size : 4096;
#endif
        atomic_store_explicit(&cached, page, memory_order_relaxed);
    }
    return page;
}

static void *map_stack(size_t mapping_size, size_t guard) {
#ifdef _WIN32
    char *mapping = VirtualAlloc(NULL, mapping_size, MEM_RESERVE, PAGE_NOACCESS);
    if (mapping == NULL) return NULL;
    if (VirtualAlloc(mapping + guard, mapping_size - guard, MEM_COMMIT, PAGE_READWRITE) == NULL) {
        VirtualFree(mapping, 0, MEM_RELEASE);
        return NULL;
    }
    return mapping;
#else
    int flags = MAP_PRIVATE | MAP_ANON;
#  ifdef MAP_NORESERVE
    flags |= MAP_NORESERVE;
#  endif
#  ifdef MAP_STACK
    flags |= MAP_STACK;
#  endif
    char *mapping = mmap(NULL, mapping_size, PROT_READ | PROT_WRITE, flags, -1, 0);
    if (mapping == MAP_FAILED) return NULL;
    if (mprotect(mapping, guard, PROT_NONE) != 0) {
        munmap(mapping, mapping_size);
        return NULL;
    }
    return mapping;
#endif
}

static void unmap_stack(void *mapping, size_t mapping_size) {
#ifdef _WIN32
    (void)mapping_size;
    VirtualFree(mapping, 0, MEM_RELEASE);
#else
    munmap(mapping, mapping_size);
#endif
}

enum { POOL_MAX = 64 };
static void *pool[POOL_MAX];
static int pool_count;
static atomic_flag pool_lock = ATOMIC_FLAG_INIT;
static atomic_size_t stacks_in_use;

static void lock_pool(void) {
    while (atomic_flag_test_and_set_explicit(&pool_lock, memory_order_acquire)) {}
}

static void unlock_pool(void) { atomic_flag_clear_explicit(&pool_lock, memory_order_release); }

static size_t mapping_bytes(size_t stack_size) { return stack_size + page_bytes(); }

/* The collector paces itself by its own heap and sees nothing of these stacks: a program that
 * starts coroutines and drops them while it keeps a large heap would pile their stacks up between
 * two collections — some four thousand at 64 MiB alive, measured, and in proportion to the heap.
 * So the stacks pace a collection of their own, which takes the dropped ones with it (06 A5): when
 * those in use reach twice what the last one left, and at least STACKS_PACED. */
enum { STACKS_PACED = 1024 };
static atomic_size_t collect_at = STACKS_PACED;

static void pace_stacks(void) {
    if (atomic_load_explicit(&stacks_in_use, memory_order_relaxed) < atomic_load_explicit(&collect_at, memory_order_relaxed)) {
        return;
    }
    lyr_gc_collect();
    size_t left = atomic_load_explicit(&stacks_in_use, memory_order_relaxed);
    atomic_store_explicit(&collect_at, left < STACKS_PACED / 2 ? (size_t)STACKS_PACED : left * 2, memory_order_relaxed);
}

/* --- the threads' coroutine state ---------------------------------------------------------------- */

/* Per thread, created at the first resume on it: the thread's own stack, as a stack that stops
 * while a coroutine runs, and the running coroutine. A coroutine belongs to the thread that first
 * resumed it, named by a number no other thread ever has. */
typedef struct CoroThread {
    LyrStackCtx own;
    LyrCoro *current;
    LyrStackCtx *last_from;  /* the stack the latest switch left, for ASan's bookkeeping */
    void *gc_handle;         /* the collector's record of the thread, whose stack bottom a switch moves */
    uint64_t id;
} CoroThread;

static _Thread_local CoroThread *thread_state;
static atomic_uint_least64_t thread_ids;

static CoroThread *this_thread(void) {
    CoroThread *ts = thread_state;
    if (LYR_UNLIKELY(ts == NULL)) {
        ts = calloc(1, sizeof *ts);
        if (ts == NULL) lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory starting coroutines on a thread");
        ts->id = atomic_fetch_add(&thread_ids, 1) + 1;
        ts->gc_handle = lyr_gc_thread_stack(&ts->own.base);
        ts->own.on_cpu = 1;
        lyr_crash_get_guard(&ts->own.guard_low, &ts->own.guard_high);
#ifdef LYR_TSAN
        ts->own.tsan_fiber = __tsan_get_current_fiber();
#endif
        lyr_gc_add_thread_stack(&ts->own);
        thread_state = ts;
    }
    return ts;
}

void lyr_coro_thread_end(void) {
    CoroThread *ts = thread_state;
    if (ts == NULL) return;
    lyr_gc_remove_thread_stack(&ts->own);
    thread_state = NULL;
    free(ts);
}

/* --- switching ----------------------------------------------------------------------------------- */

/* Arriving on a stack — a coroutine's first time, or back from a switch: ASan learns where it is
 * now (and, once, the bounds of the stack it came from, a thread's own stack ASan alone knows),
 * and collections may start again. */
static void arrived(LyrStackCtx *self) {
#ifdef LYR_ASAN
    const void *bottom = NULL;
    size_t size = 0;
    __sanitizer_finish_switch_fiber(self->asan_fake, &bottom, &size);
    LyrStackCtx *left = thread_state ? thread_state->last_from : NULL;
    if (left != NULL && left->asan_bottom == NULL) {
        left->asan_bottom = bottom;
        left->asan_size = size;
    }
#else
    (void)self;
#endif
    lyr_gc_switch_end();
}

/* Leaves `from` for `to`. The world does not stop from before the thread's stack bottom moves
 * until the side that runs next has arrived (lyr_gc_switch_begin/_end), so no collection ever
 * sees the stack pointer on one stack and the bottom on another. `from_ends`: the stack is left
 * for good (its coroutine's body returned). */
static void transfer(LyrStackCtx *from, LyrStackCtx *to, int from_ends) {
    lyr_gc_switch_begin(thread_state->gc_handle, to);
    from->on_cpu = 0;
    to->on_cpu = 1;
    lyr_crash_set_guard(to->guard_low, to->guard_high);
    thread_state->last_from = from;
#ifdef LYR_ASAN
    __sanitizer_start_switch_fiber(from_ends ? NULL : &from->asan_fake, to->asan_bottom, to->asan_size);
#else
    (void)from_ends;
#endif
#ifdef LYR_TSAN
    __tsan_switch_to_fiber(to->tsan_fiber, 0);
#endif
    lyr_ctx_switch(&from->sp, to->sp);
    arrived(from);
}

/* The registers the switch pops on a new coroutine's first resume: zero but for the coroutine
 * (for lyr_coro_start), the return into lyr_coro_start, a frame chain that ends there, and the
 * floating-point control state of the thread that starts it. */
static void *initial_frame(LyrCoro *co, void *mapping, char *top) {
    (void)mapping;  /* the TEB's DeallocationStack, Windows only */
    top = (char *)((uintptr_t)top & ~(uintptr_t)15);
#if defined(__x86_64__)
    uint32_t mxcsr;
    uint16_t fcw;
    __asm__ volatile("stmxcsr %0" : "=m"(mxcsr));
    __asm__ volatile("fnstcw %0" : "=m"(fcw));
    void **sp = (void **)top;
    *--sp = (void *)(uintptr_t)lyr_coro_start;  /* the switch returns here */
    *--sp = NULL;                               /* rbp: the frame chain ends */
    *--sp = co;                                 /* rbx */
#  ifdef _WIN32
    *--sp = NULL;                               /* rdi */
    *--sp = NULL;                               /* rsi */
#  endif
    for (int i = 0; i < 4; i++) *--sp = NULL;   /* r12–r15 */
#  ifdef _WIN32
    *--sp = co->ctx.base;                       /* TEB StackBase */
    *--sp = co->ctx.low;                        /* TEB StackLimit */
    *--sp = mapping;                            /* TEB DeallocationStack */
    char *bytes = (char *)sp - 168;             /* xmm6–xmm15, MXCSR, x87 control word */
    memset(bytes, 0, 168);
    memcpy(bytes + 160, &mxcsr, sizeof mxcsr);
    memcpy(bytes + 164, &fcw, sizeof fcw);
    return bytes;
#  else
    char *bytes = (char *)sp - 8;               /* MXCSR, x87 control word */
    memset(bytes, 0, 8);
    memcpy(bytes, &mxcsr, sizeof mxcsr);
    memcpy(bytes + 4, &fcw, sizeof fcw);
    return bytes;
#  endif
#elif defined(__aarch64__)
    uint64_t fpcr;
    __asm__ volatile("mrs %0, fpcr" : "=r"(fpcr));
    void **sp = (void **)(top - 176);
    memset(sp, 0, 176);
    sp[0] = co;                                 /* x19 */
    sp[10] = NULL;                              /* x29: the frame chain ends */
    sp[11] = (void *)(uintptr_t)lyr_coro_start; /* x30: the switch returns here */
    memcpy((char *)sp + 160, &fpcr, sizeof fpcr);
    return sp;
#endif
}

/* The stack for a coroutine's first resume, from the pool or newly mapped. */
static void acquire_stack(LyrCoro *co) {
    pace_stacks();
    size_t mapping_size = mapping_bytes(co->stack_size);
    void *mapping = NULL;
    if (co->stack_size == LYR_CORO_STACK_DEFAULT) {
        lock_pool();
        if (pool_count > 0) mapping = pool[--pool_count];
        unlock_pool();
    }
    if (mapping == NULL) mapping = map_stack(mapping_size, page_bytes());
    if (mapping == NULL) {
        lyr_panic(LYR_RT_OUT_OF_MEMORY, "out of memory reserving a coroutine stack of %zu bytes", co->stack_size);
    }
    char *low = (char *)mapping + page_bytes();
    co->mapping_size = mapping_size;
    co->ctx.low = low;
    co->ctx.base = low + co->stack_size;
    /* A fault in the guard page, or up to 1 MiB below it (one frame may be large), is an overflow. */
    uintptr_t guard = (uintptr_t)mapping;
    co->ctx.guard_low = guard > ((uintptr_t)1 << 20) ? guard - ((uintptr_t)1 << 20) : 0;
    co->ctx.guard_high = (uintptr_t)low;
    co->ctx.asan_bottom = low;
    co->ctx.asan_size = co->stack_size;
#ifdef LYR_TSAN
    co->ctx.tsan_fiber = __tsan_create_fiber(0);
#endif
    co->ctx.sp = initial_frame(co, mapping, co->ctx.base);
    co->mapping = mapping;  /* last: from here on the collector reads [sp, base) */
    atomic_fetch_add(&stacks_in_use, 1);
}

/* The stack goes: back to the pool or unmapped. The collector stops reading it first. */
static void release_stack(LyrCoro *co) {
    void *mapping = co->mapping;
    if (mapping == NULL) return;
    co->mapping = NULL;
    co->ctx.sp = NULL;
#ifdef LYR_TSAN
    __tsan_destroy_fiber(co->ctx.tsan_fiber);
    co->ctx.tsan_fiber = NULL;
#endif
#ifdef LYR_ASAN
    /* Frames that never returned leave their redzones poisoned. */
    __asan_unpoison_memory_region(co->ctx.low, co->stack_size);
#endif
    int pooled = 0;
    if (co->stack_size == LYR_CORO_STACK_DEFAULT) {
        lock_pool();
        if (pool_count < POOL_MAX) {
            pool[pool_count++] = mapping;
            pooled = 1;
        }
        unlock_pool();
    }
    if (!pooled) unmap_stack(mapping, co->mapping_size);
    atomic_fetch_sub(&stacks_in_use, 1);
}

/* A suspended coroutine nothing references any more (06 A5): nothing on its stack runs again; the
 * stack goes. Called by the collector stage on any thread, at an allocation or a collection. */
void lyr_coro_abandoned(LyrCoro *co) {
    release_stack(co);
}

/* The first frame on a coroutine's stack below lyr_coro_start: runs the body, then leaves for good.
 * A trace inside a coroutine ends here. */
LYR_HIDDEN void lyr_coro_main(LyrCoro *co);
LYR_HIDDEN void lyr_coro_main(LyrCoro *co) {
    arrived(&co->ctx);
    co->body(co->arg);
    co->status = LYR_CORO_DONE;
    CoroThread *ts = thread_state;
    ts->current = co->resumer_coro;
    LyrStackCtx *to = co->resumer;
    LYR_WRITE_BARRIER(co, &co->resumer_coro, (LyrCoro *)NULL);
    transfer(&co->ctx, to, 1);
    __builtin_trap();  /* nothing resumes a coroutine that is done */
}

/* --- the primitives (01 K7) ---------------------------------------------------------------------- */

LyrCoro *lyr_coro_new(const LyrDesc *desc, LyrCoroBody body, void *arg, size_t stack_size) {
    size_t page = page_bytes();
    size_t size = stack_size == 0 ? LYR_CORO_STACK_DEFAULT : (stack_size + page - 1) / page * page;
    LyrCoro *co = lyr_gc_alloc_coro();
    co->header.desc = desc;
    co->body = body;
    LYR_WRITE_BARRIER(co, &co->arg, arg);
    co->stack_size = size;
    co->status = LYR_CORO_SUSPENDED;
    return co;
}

void lyr_coro_resume(LyrCoro *co) {
    CoroThread *ts = this_thread();
    if (LYR_UNLIKELY(co->status != LYR_CORO_SUSPENDED)) {
        if (co->status == LYR_CORO_DONE) lyr_panic(LYR_RT_COROUTINE, "a coroutine resumed after its body returned");
        lyr_panic(LYR_RT_COROUTINE, "a coroutine resumed while it runs — by itself, or while a coroutine it resumed runs");
    }
    if (co->mapping == NULL) {
        co->owner = ts->id;
        acquire_stack(co);
        lyr_gc_watch_coro(co);
    } else if (LYR_UNLIKELY(co->owner != ts->id)) {
        lyr_panic(LYR_RT_COROUTINE, "a coroutine resumed on a thread other than the one it runs on");
    }
    LyrStackCtx *from = ts->current != NULL ? &ts->current->ctx : &ts->own;
    co->resumer = from;
    LYR_WRITE_BARRIER(co, &co->resumer_coro, ts->current);
    co->status = LYR_CORO_RUNNING;
    ts->current = co;
    transfer(from, &co->ctx, 0);
    /* Back here: the coroutine yielded, or its body returned and its stack can go. */
    if (co->status == LYR_CORO_DONE) {
        lyr_gc_unwatch_coro(co);
        release_stack(co);
    }
}

void lyr_coro_yield(void) {
    CoroThread *ts = this_thread();
    LyrCoro *co = ts->current;
    if (LYR_UNLIKELY(co == NULL)) lyr_panic(LYR_RT_COROUTINE, "a yield with no coroutine running — on the thread's own stack");
    co->status = LYR_CORO_SUSPENDED;
    ts->current = co->resumer_coro;
    LyrStackCtx *to = co->resumer;
    LYR_WRITE_BARRIER(co, &co->resumer_coro, (LyrCoro *)NULL);
    transfer(&co->ctx, to, 0);
}

void lyr_coro_yield_value(void *value) {
    CoroThread *ts = this_thread();
    if (LYR_UNLIKELY(ts->current == NULL)) lyr_panic(LYR_RT_COROUTINE, "a yield with no coroutine running — on the thread's own stack");
    ts->current->transfer = value;
    lyr_coro_yield();
}

void *lyr_coro_transfer(const LyrCoro *co) { return co->transfer; }
void lyr_coro_set_transfer(LyrCoro *co, void *value) { co->transfer = value; }

void lyr_coro_set_error(LyrCoro *co, struct LyrErr *error) { LYR_WRITE_BARRIER(co, &co->error, error); }

struct LyrErr *lyr_coro_take_error(LyrCoro *co) {
    struct LyrErr *error = co->error;
    LYR_WRITE_BARRIER(co, &co->error, (struct LyrErr *)NULL);
    return error;
}

LyrCoroStatus lyr_coro_status(const LyrCoro *co) { return (LyrCoroStatus)co->status; }

void *lyr_coro_arg(const LyrCoro *co) { return co->arg; }

LyrCoro *lyr_coro_current(void) {
    CoroThread *ts = thread_state;
    return ts != NULL ? ts->current : NULL;
}

void lyr_coro_enter_foreign(void) {
    CoroThread *ts = thread_state;
    if (ts != NULL && ts->current != NULL) ts->current->foreign++;
}

void lyr_coro_leave_foreign(void) {
    CoroThread *ts = thread_state;
    if (ts != NULL && ts->current != NULL && ts->current->foreign > 0) ts->current->foreign--;
}

int lyr_coro_foreign_depth(const LyrCoro *co) { return co->foreign; }

size_t lyr_coro_stacks(void) { return atomic_load(&stacks_in_use); }
