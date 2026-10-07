/* Fault signals and stack overflow (10 Q9, 01 S3).
 *
 * A fault signal — SEGV, BUS, FPE, ILL, ABRT — is a crash, not a panic: the runtime writes
 * `crash: …` and the backtrace of the faulting thread, then lets the signal take its default
 * course, so the process ends the way the operating system reports that signal. A stack overflow
 * is a panic (RT0006): until the emitter checks the stack pointer in every prologue (M11), the
 * guard page below each stack is the only net, and its fault is told apart by its address.
 * Windows has the same two paths through an unhandled-exception filter — and, on a coroutine's
 * stack, through a vectored handler, since the filter is never reached from there. */
#if defined(__linux__) && !defined(_GNU_SOURCE)
#  define _GNU_SOURCE 1
#elif defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#  define _DARWIN_C_SOURCE 1
#endif

#include "internal.h"
#include "lyr/coro.h"
#include "lyr/init.h"

#include <stdatomic.h>
#include <stdio.h>
#include <string.h>

static int installed;

/* One crash report per process: a second faulting thread waits for the end. */
static atomic_int crashing;
static char report[32 * 1024];

static size_t crash_header(const char *what) {
    int n = snprintf(report, sizeof report, "crash: %s\n", what);
    return n < 0 ? 0 : (size_t)n < sizeof report ? (size_t)n : sizeof report - 1;
}

#ifndef _WIN32
/* --- POSIX ------------------------------------------------------------------------------------ */
#include <pthread.h>
#include <signal.h>
#include <sys/mman.h>
#include <unistd.h>
#ifdef __linux__
#  include <ucontext.h>  /* macOS declares ucontext_t in <signal.h>; its <ucontext.h> is the deprecated API */
#endif

enum { ALT_STACK_SIZE = 256 * 1024 };

/* TRAP among them: what '__builtin_trap()' raises where the trap is a breakpoint (arm64) and not
 * an illegal instruction (x86-64) — the floor under unreachable code in a release build (panic.h). */
static const int fault_signals[] = { SIGSEGV, SIGBUS, SIGFPE, SIGILL, SIGTRAP, SIGABRT };

/* Per thread: the alternate stack the handler runs on (an overflowed stack has no room for it),
 * and the address range whose fault means the stack ran out. */
static _Thread_local void *alt_stack;
static _Thread_local uintptr_t guard_low, guard_high;

/* The lowest address the thread's stack may reach. A fault within 1 MiB below it (one frame can
 * be large) or a few pages above it (the bound is rounded) is an overflow. */
static void find_guard(void) {
    uintptr_t low = 0;
#if defined(__linux__)
    pthread_attr_t attr;
    if (pthread_getattr_np(pthread_self(), &attr) == 0) {
        void *address;
        size_t size;
        if (pthread_attr_getstack(&attr, &address, &size) == 0) low = (uintptr_t)address;
        pthread_attr_destroy(&attr);
    }
#elif defined(__APPLE__)
    uintptr_t top = (uintptr_t)pthread_get_stackaddr_np(pthread_self());
    low = top - pthread_get_stacksize_np(pthread_self());
#endif
    if (low == 0) return;
    guard_low = low > ((uintptr_t)1 << 20) ? low - ((uintptr_t)1 << 20) : 0;
    guard_high = low + 4 * 4096;
}

void lyr_crash_thread_start(void) {
    if (!installed) return;
    find_guard();
    /* A host thread may bring its own alternate stack; it stays. */
    stack_t current;
    if (sigaltstack(NULL, &current) == 0 && !(current.ss_flags & SS_DISABLE)) return;
    void *memory = mmap(NULL, ALT_STACK_SIZE, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    if (memory == MAP_FAILED) return;
    stack_t stack = { .ss_sp = memory, .ss_size = ALT_STACK_SIZE, .ss_flags = 0 };
    if (sigaltstack(&stack, NULL) != 0) {
        munmap(memory, ALT_STACK_SIZE);
        return;
    }
    alt_stack = memory;
}

void lyr_crash_get_guard(uintptr_t *low, uintptr_t *high) {
    *low = guard_low;
    *high = guard_high;
}

void lyr_crash_set_guard(uintptr_t low, uintptr_t high) {
    guard_low = low;
    guard_high = high;
}

void lyr_crash_thread_end(void) {
    if (alt_stack == NULL) return;
    stack_t off = { .ss_sp = NULL, .ss_size = 0, .ss_flags = SS_DISABLE };
    sigaltstack(&off, NULL);
    munmap(alt_stack, ALT_STACK_SIZE);
    alt_stack = NULL;
    guard_low = guard_high = 0;
}

/* The fault's place from the signal context. The frame pointer and link register only on macOS,
 * where the trace walks the frame chain itself (trace.c); Linux unwinds through the signal frame. */
static LyrFault context_fault(void *context) {
    ucontext_t *uc = context;
    LyrFault fault = { 0, 0, 0 };
#if defined(__linux__) && defined(__x86_64__)
    fault.pc = (uintptr_t)uc->uc_mcontext.gregs[REG_RIP];
#elif defined(__linux__) && defined(__aarch64__)
    fault.pc = (uintptr_t)uc->uc_mcontext.pc;
#elif defined(__APPLE__) && defined(__x86_64__)
    fault.pc = (uintptr_t)uc->uc_mcontext->__ss.__rip;
    fault.fp = (uintptr_t)uc->uc_mcontext->__ss.__rbp;
#elif defined(__APPLE__) && defined(__aarch64__)
    fault.pc = (uintptr_t)uc->uc_mcontext->__ss.__pc;
    fault.fp = (uintptr_t)uc->uc_mcontext->__ss.__fp;
    fault.lr = (uintptr_t)uc->uc_mcontext->__ss.__lr;
#else
    (void)uc;
#endif
    return fault;
}

static const char *signal_name(int sig) {
    switch (sig) {
    case SIGSEGV: return "SIGSEGV";
    case SIGBUS: return "SIGBUS";
    case SIGFPE: return "SIGFPE";
    case SIGILL: return "SIGILL";
    case SIGTRAP: return "SIGTRAP";
    case SIGABRT: return "SIGABRT";
    default: return "signal";
    }
}

static _Thread_local int in_crash;

static void lyr_crash_on_signal(int sig, siginfo_t *info, void *context) {
    /* A fault inside the report: give up on it and let the default action end the process. */
    if (in_crash) {
        signal(sig, SIG_DFL);
        return;
    }
    in_crash = 1;
    uintptr_t address = (uintptr_t)info->si_addr;
    LyrFault fault = context_fault(context);

    if ((sig == SIGSEGV || sig == SIGBUS) && guard_high != 0 && address >= guard_low && address < guard_high) {
        lyr_panic_report(LYR_RT_STACK_OVERFLOW, "stack overflow", &fault, 1);
    }

    if (atomic_exchange(&crashing, 1)) {
        for (;;) pause();
    }
    char what[160];
    switch (sig) {
    case SIGSEGV:
    case SIGBUS:
        snprintf(what, sizeof what, "%s (invalid memory access at 0x%llx)", signal_name(sig), (unsigned long long)address);
        break;
    case SIGFPE: snprintf(what, sizeof what, "SIGFPE (arithmetic fault)"); break;
    case SIGILL: snprintf(what, sizeof what, "SIGILL (illegal instruction)"); break;
    case SIGTRAP: snprintf(what, sizeof what, "SIGTRAP (trap)"); break;
    default: snprintf(what, sizeof what, "%s (abort)", signal_name(sig)); break;
    }
    size_t header = crash_header(what);
    size_t frames = lyr_trace_format(report + header, sizeof report - header, &fault);
    lyr_write_stderr(report, header + frames);

    /* The default action, so the process ends as that signal: a fault re-executes its instruction
     * after the return, an abort is raised again (it stays blocked until the handler returns). */
    signal(sig, SIG_DFL);
    if (sig == SIGABRT) raise(sig);
}

void lyr_crash_install(void) {
    if (installed) return;
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_sigaction = lyr_crash_on_signal;
    action.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&action.sa_mask);
    for (size_t i = 0; i < sizeof fault_signals / sizeof fault_signals[0]; i++) {
        sigaction(fault_signals[i], &action, NULL);
    }
    installed = 1;
    lyr_crash_thread_start();
}

#else
/* --- Windows ---------------------------------------------------------------------------------- */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <signal.h>

/* What the overflowing thread keeps for the filter after its guard page is spent (internal.h). */
static const ULONG STACK_GUARANTEE = LYR_STACK_GUARANTEE;

void lyr_crash_thread_start(void) {
    if (!installed) return;
    ULONG size = STACK_GUARANTEE;
    SetThreadStackGuarantee(&size);
}

void lyr_crash_thread_end(void) {}

void lyr_crash_get_guard(uintptr_t *low, uintptr_t *high) { *low = *high = 0; }
void lyr_crash_set_guard(uintptr_t low, uintptr_t high) { (void)low; (void)high; }

static LONG WINAPI lyr_crash_on_exception(EXCEPTION_POINTERS *pointers) {
    EXCEPTION_RECORD *record = pointers->ExceptionRecord;
    DWORD code = record->ExceptionCode;
    uintptr_t pc = (uintptr_t)record->ExceptionAddress;
    LyrFault fault = { pc, 0, 0 };
    if (code == EXCEPTION_STACK_OVERFLOW) lyr_panic_report(LYR_RT_STACK_OVERFLOW, "stack overflow", &fault, 1);

    if (atomic_exchange(&crashing, 1)) {
        for (;;) Sleep(1000);
    }
    char what[160];
    switch (code) {
    case EXCEPTION_ACCESS_VIOLATION:
        snprintf(what, sizeof what, "access violation (invalid memory access at 0x%llx)",
                 (unsigned long long)record->ExceptionInformation[1]);
        break;
    case EXCEPTION_ILLEGAL_INSTRUCTION: snprintf(what, sizeof what, "illegal instruction"); break;
    case EXCEPTION_INT_DIVIDE_BY_ZERO: snprintf(what, sizeof what, "arithmetic fault (integer division by zero)"); break;
    default: snprintf(what, sizeof what, "exception 0x%08lx", (unsigned long)code); break;
    }
    size_t header = crash_header(what);
    size_t frames = lyr_trace_format(report + header, sizeof report - header, &fault);
    lyr_write_stderr(report, header + frames);
    /* Ends as an unhandled exception would, with its code, but without the error-reporting dialog. */
    TerminateProcess(GetCurrentProcess(), code);
    return EXCEPTION_CONTINUE_SEARCH;
}

/* A coroutine's stack has no frame of the system's below it: the chain ends at lyr_coro_start,
 * which has no unwind data, so the dispatcher's walk finds no handler there — not even the
 * process's last one, which calls the filter — and the thread dies with the exception's code and
 * no line (the review's R6e). A vectored handler sees the exception before the walk; on a
 * coroutine's stack it does what the filter does, for the faults the filter names. On the thread's
 * own stack it leaves the exception to the chain, as before. Last among the vectored handlers:
 * whoever handles a fault of its own comes first. */
static LONG WINAPI lyr_crash_on_coroutine_stack(EXCEPTION_POINTERS *pointers) {
    DWORD code = pointers->ExceptionRecord->ExceptionCode;
    if (code != EXCEPTION_STACK_OVERFLOW && code != EXCEPTION_ACCESS_VIOLATION
        && code != EXCEPTION_ILLEGAL_INSTRUCTION && code != EXCEPTION_INT_DIVIDE_BY_ZERO) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    if (lyr_coro_current() == NULL) return EXCEPTION_CONTINUE_SEARCH;
    return lyr_crash_on_exception(pointers);
}

/* abort() raises SIGABRT through the C library; after this handler the UCRT ends the process with
 * a fast fail (0xC0000409). */
static void lyr_crash_on_abort(int sig) {
    (void)sig;
    if (atomic_exchange(&crashing, 1)) return;
    size_t header = crash_header("abort");
    size_t frames = lyr_trace_format(report + header, sizeof report - header, NULL);
    lyr_write_stderr(report, header + frames);
}

void lyr_crash_install(void) {
    if (installed) return;
    SetUnhandledExceptionFilter(lyr_crash_on_exception);
    AddVectoredExceptionHandler(0, lyr_crash_on_coroutine_stack);
    signal(SIGABRT, lyr_crash_on_abort);
    installed = 1;
    lyr_crash_thread_start();
}
#endif
