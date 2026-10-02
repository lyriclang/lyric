/* Backtraces for panics and crashes (01 E8): libbacktrace over the DWARF of the executable on
 * Linux and macOS, DbgHelp over the PDB on Windows (zig cc writes CodeView there). Both halves
 * capture program counters first, then name them, then share the filtering and the format.
 *
 * Runs on the panic path, possibly in a signal handler on the alternate stack: no allocation
 * through malloc (libbacktrace maps its memory; the Windows half keeps names in a static pool),
 * bounded work, and silence on every failure — a trace without names is still a trace. */
#include "internal.h"

#include <stdarg.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>

enum { MAX_PCS = LYR_TRACE_PCS, MAX_FRAMES = 384 };

typedef struct Frame {
    uintptr_t pc;          /* as the unwinder answered it */
    const char *function;  /* NULL when unknown */
    const char *file;
    int line;
} Frame;

typedef struct Trace {
    Frame frames[MAX_FRAMES];
    int count;
    int truncated;         /* the stack was deeper than MAX_PCS */
} Trace;

/* Only one thread formats at a time (panic.c and crash.c let one report through), so one static
 * trace suffices — a stack overflow has no room for 20 KB of frames on its own stack anyway. */
static Trace trace;

static void add_frame(uintptr_t pc, const char *function, const char *file, int line) {
    if (trace.count == MAX_FRAMES) {
        trace.truncated = 1;
        return;
    }
    trace.frames[trace.count++] = (Frame){ pc, function, file, line };
}

#ifndef _WIN32
/* --- Linux, macOS: libbacktrace ----------------------------------------------------------- */
#include "backtrace.h"

/* Created at the first panic, not at start (01 L11): creating it is cheap, reading the debug
 * information happens on the first lookup anyway, but a program that never panics pays nothing. */
static _Atomic(struct backtrace_state *) state;

static void ignore_error(void *data, const char *message, int errnum) {
    (void)data;
    (void)message;
    (void)errnum;
}

static struct backtrace_state *get_state(void) {
    struct backtrace_state *current = atomic_load(&state);
    if (current) return current;
    struct backtrace_state *fresh = backtrace_create_state(NULL, 1, ignore_error, NULL);
    /* A losing racer's state is leaked: libbacktrace has no way to free one. */
    return atomic_compare_exchange_strong(&state, &current, fresh) ? fresh : current;
}

typedef struct Pcs {
    uintptr_t pc[MAX_PCS];
    int count;
    int full;
} Pcs;

static int collect_pc(void *data, uintptr_t pc) {
    Pcs *pcs = data;
    if (pcs->count == MAX_PCS) {
        pcs->full = 1;
        return 1;
    }
    pcs->pc[pcs->count++] = pc;
    return 0;
}

typedef struct Lookup {
    uintptr_t pc;
    int found;
} Lookup;

/* Called once per frame at a pc — more than once when functions were inlined into it, the
 * innermost first. */
static int name_frame(void *data, uintptr_t pc, const char *file, int line, const char *function) {
    Lookup *lookup = data;
    (void)pc;
    if (file == NULL && function == NULL) return 0;
    add_frame(lookup->pc, function, file, line);
    lookup->found = 1;
    return 0;
}

static void name_symbol(void *data, uintptr_t pc, const char *symbol, uintptr_t value, uintptr_t size) {
    (void)pc;
    (void)value;
    (void)size;
    *(const char **)data = symbol;
}

#if defined(__APPLE__)
static const char *symbol_at(struct backtrace_state *st, uintptr_t pc) {
    const char *symbol = NULL;
    backtrace_syminfo(st, pc, name_symbol, ignore_error, &symbol);
    return symbol;
}

/* Apple's unwinder does not step out of a signal handler's trampoline (_sigtramp), so a fault's
 * trace would end there. The Apple ABIs require the frame-pointer chain; a fault's frames come
 * from it instead: each record holds the caller's record and the return address into the caller.
 * On arm64 a leaf function may keep no record of its own — its caller's return address is then
 * only in the link register, taken unless the chain has it already or it points back into the
 * faulting function (a stale one, from a call that function made earlier). */
static void walk_frames(struct backtrace_state *st, Pcs *pcs, const LyrFault *fault) {
    collect_pc(pcs, fault->pc);
    uintptr_t fp = fault->fp;
    int aligned = fp != 0 && fp % sizeof(uintptr_t) == 0;
    if (fault->lr != 0) {
        uintptr_t first_return = aligned ? ((const uintptr_t *)fp)[1] : 0;
        const char *here = symbol_at(st, fault->pc), *there = symbol_at(st, fault->lr - 1);
        if (fault->lr != first_return && !(here && there && strcmp(here, there) == 0)) collect_pc(pcs, fault->lr - 1);
    }
    while (aligned) {
        const uintptr_t *record = (const uintptr_t *)fp;
        uintptr_t next = record[0], ret = record[1];
        if (ret == 0 || collect_pc(pcs, ret - 1)) break;
        /* Callers live higher up the same stack; anything else is the end of the chain. */
        if (next <= fp || next - fp > ((uintptr_t)8 << 20)) break;
        fp = next;
        aligned = fp % sizeof(uintptr_t) == 0;
    }
}
#endif

/* The program counters of the calling thread's stack — of the faulting frame first, for a fault. */
static void collect(struct backtrace_state *st, Pcs *pcs, const LyrFault *fault) {
    pcs->count = 0;
    pcs->full = 0;
#if defined(__APPLE__)
    if (fault != NULL && fault->fp != 0) walk_frames(st, pcs, fault);
    else
#else
    (void)fault;
#endif
    backtrace_simple(st, 0, collect_pc, ignore_error, pcs);
}

/* Each pc named into the trace, a function inlined at it first. */
static void name_pcs(struct backtrace_state *st, const uintptr_t *pc, int count) {
    for (int i = 0; i < count; i++) {
        Lookup lookup = { pc[i], 0 };
        backtrace_pcinfo(st, pc[i], name_frame, ignore_error, &lookup);
        if (!lookup.found) {
            /* No line table here (a system library, a stripped build): the symbol table may still
             * know the function. */
            const char *symbol = NULL;
            backtrace_syminfo(st, pc[i], name_symbol, ignore_error, &symbol);
            add_frame(pc[i], symbol, NULL, 0);
        }
    }
}

static void capture(const LyrFault *fault) {
    struct backtrace_state *st = get_state();
    if (st == NULL) return;
    static Pcs pcs;
    collect(st, &pcs, fault);
    trace.truncated = pcs.full;
    name_pcs(st, pcs.pc, pcs.count);
}

int lyr_trace_capture(uintptr_t *out, int max) {
    struct backtrace_state *st = get_state();
    if (st == NULL || max <= 0) return 0;
    Pcs pcs;
    collect(st, &pcs, NULL);
    int n = pcs.count < max ? pcs.count : max;
    memcpy(out, pcs.pc, (size_t)n * sizeof(uintptr_t));
    return n;
}

static void name_captured(const uintptr_t *pcs, int count) {
    struct backtrace_state *st = get_state();
    if (st != NULL) name_pcs(st, pcs, count);
}

#else
/* --- Windows: RtlCaptureStackBackTrace and DbgHelp ------------------------------------------ */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dbghelp.h>

/* DbgHelp hands out names in caller buffers; the frames keep them here. */
static char names[32 * 1024];
static size_t names_used;

static const char *keep(const char *text) {
    size_t len = strlen(text) + 1;
    if (names_used + len > sizeof names) return NULL;
    char *kept = memcpy(names + names_used, text, len);
    names_used += len;
    return kept;
}

static int symbols_ready;

/* DbgHelp is loaded here, at the first trace, and never through the import table: as an import
 * the DLL is mapped at every program start and costs 3 ms of it (measured), for a report most
 * programs never write. The inline-site half (functions the optimizer inlined at an address;
 * Windows 8 and later) is not in the MinGW headers zig ships; taking every entry by name treats
 * both halves alike. Without the library a trace is bare addresses. */
typedef BOOL (WINAPI *SymInitializeFn)(HANDLE, PCSTR, BOOL);
typedef DWORD (WINAPI *SymSetOptionsFn)(DWORD);
typedef BOOL (WINAPI *SymFromAddrFn)(HANDLE, DWORD64, PDWORD64, PSYMBOL_INFO);
typedef BOOL (WINAPI *SymGetLineFromAddr64Fn)(HANDLE, DWORD64, PDWORD, PIMAGEHLP_LINE64);
typedef DWORD (WINAPI *AddrIncludeInlineTraceFn)(HANDLE, DWORD64);
typedef BOOL (WINAPI *QueryInlineTraceFn)(HANDLE, DWORD64, DWORD, DWORD64, DWORD64, LPDWORD, LPDWORD);
typedef BOOL (WINAPI *FromInlineContextFn)(HANDLE, DWORD64, ULONG, PDWORD64, PSYMBOL_INFO);
typedef BOOL (WINAPI *LineFromInlineContextFn)(HANDLE, DWORD64, ULONG, DWORD64, PDWORD, PIMAGEHLP_LINE64);
static SymFromAddrFn sym_from_addr;
static SymGetLineFromAddr64Fn sym_line;
static AddrIncludeInlineTraceFn inline_count;
static QueryInlineTraceFn inline_query;
static FromInlineContextFn inline_symbol;
static LineFromInlineContextFn inline_line;

static void *entry(HMODULE dbghelp, const char *name) {
    return (void *)GetProcAddress(dbghelp, name);
}

/* zig records the PDB beside the executable under a relative name, which DbgHelp looks for in its
 * search path only: the executable's own directory goes first. */
static void init_symbols(HANDLE process) {
    if (symbols_ready) return;
    symbols_ready = 1;
    char path[MAX_PATH + 4];
    DWORD len = GetModuleFileNameA(NULL, path, MAX_PATH);
    char *search = NULL;
    if (len > 0 && len < MAX_PATH) {
        char *slash = strrchr(path, '\\');
        if (slash) {
            *slash = '\0';
            search = path;
        }
    }
    HMODULE dbghelp = LoadLibraryA("dbghelp.dll");
    if (dbghelp == NULL) return;
    SymSetOptionsFn set_options = (SymSetOptionsFn)entry(dbghelp, "SymSetOptions");
    SymInitializeFn initialize = (SymInitializeFn)entry(dbghelp, "SymInitialize");
    sym_from_addr = (SymFromAddrFn)entry(dbghelp, "SymFromAddr");
    sym_line = (SymGetLineFromAddr64Fn)entry(dbghelp, "SymGetLineFromAddr64");
    inline_count = (AddrIncludeInlineTraceFn)entry(dbghelp, "SymAddrIncludeInlineTrace");
    inline_query = (QueryInlineTraceFn)entry(dbghelp, "SymQueryInlineTrace");
    inline_symbol = (FromInlineContextFn)entry(dbghelp, "SymFromInlineContext");
    inline_line = (LineFromInlineContextFn)entry(dbghelp, "SymGetLineFromInlineContext");
    if (set_options == NULL || initialize == NULL) return;
    set_options(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES |
                SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS);
    initialize(process, search, TRUE);
}

/* Each pc named into the trace, the functions inlined at it first. */
static void name_pcs(const uintptr_t *pcs, int count, uintptr_t fault_pc) {
    HANDLE process = GetCurrentProcess();
    init_symbols(process);
    names_used = 0;
    for (int i = 0; i < count; i++) {
        uintptr_t pc = pcs[i];
        /* A return address names the instruction after the call; one byte back is the call. The
         * faulting instruction itself is exact. */
        DWORD64 address = pc == fault_pc ? pc : pc - 1;
        /* Functions the optimizer inlined here come first, innermost out — the PDB knows them
         * as inline sites — then the function the address is in. */
        DWORD inlined = inline_count && inline_query && inline_symbol && inline_line ? inline_count(process, address) : 0;
        DWORD context = 0, index = 0;
        if (inlined > 0 && inline_query(process, address, 0, address, address, &context, &index)) {
            for (DWORD n = 0; n < inlined; n++) {
                union {
                    SYMBOL_INFO info;
                    char bytes[sizeof(SYMBOL_INFO) + 256];
                } symbol;
                symbol.info.SizeOfStruct = sizeof(SYMBOL_INFO);
                symbol.info.MaxNameLen = 255;
                DWORD64 offset = 0;
                const char *function = inline_symbol(process, address, context + n, &offset, &symbol.info) ? keep(symbol.info.Name) : NULL;
                IMAGEHLP_LINE64 line;
                line.SizeOfStruct = sizeof line;
                DWORD column = 0;
                if (inline_line(process, address, context + n, 0, &column, &line)) {
                    add_frame(pc, function, keep(line.FileName), (int)line.LineNumber);
                } else {
                    add_frame(pc, function, NULL, 0);
                }
            }
        }
        union {
            SYMBOL_INFO info;
            char bytes[sizeof(SYMBOL_INFO) + 256];
        } symbol;
        symbol.info.SizeOfStruct = sizeof(SYMBOL_INFO);
        symbol.info.MaxNameLen = 255;
        DWORD64 offset = 0;
        const char *function = sym_from_addr && sym_from_addr(process, address, &offset, &symbol.info) ? keep(symbol.info.Name) : NULL;
        IMAGEHLP_LINE64 line;
        line.SizeOfStruct = sizeof line;
        DWORD column = 0;
        if (sym_line && sym_line(process, address, &column, &line)) {
            add_frame(pc, function, keep(line.FileName), (int)line.LineNumber);
        } else {
            add_frame(pc, function, NULL, 0);
        }
    }
}

static void capture_at(uintptr_t fault_pc) {
    static void *raw[MAX_PCS];
    static uintptr_t pcs[MAX_PCS];
    USHORT count = RtlCaptureStackBackTrace(0, MAX_PCS, raw, NULL);
    trace.truncated = count == MAX_PCS;
    for (USHORT i = 0; i < count; i++) pcs[i] = (uintptr_t)raw[i];
    name_pcs(pcs, count, fault_pc);
}

int lyr_trace_capture(uintptr_t *out, int max) {
    void *raw[MAX_PCS];
    USHORT count = RtlCaptureStackBackTrace(0, MAX_PCS, raw, NULL);
    int n = count < max ? count : max;
    for (int i = 0; i < n; i++) out[i] = (uintptr_t)raw[i];
    return n;
}

static void name_captured(const uintptr_t *pcs, int count) {
    name_pcs(pcs, count, 0);
}
#endif

/* --- filtering and format ------------------------------------------------------------------ */

static int starts_with(const char *text, const char *prefix) {
    return text && strncmp(text, prefix, strlen(prefix)) == 0;
}

/* The runtime's own frames above the program's: the panic and crash machinery, the trace itself,
 * the coroutine primitives a pull, a yield or a close panics in (RT0014) — named one by one,
 * since 'lyr_coro_main' is where a coroutine's frames END — and the poller (RT0015). */
static int is_runtime_frame(const Frame *frame) {
    return starts_with(frame->function, "lyr_panic") || starts_with(frame->function, "lyr_crash") ||
           starts_with(frame->function, "lyr_trace") || starts_with(frame->function, "lyr_err_new") ||
           starts_with(frame->function, "lyr_coro_resume") || starts_with(frame->function, "lyr_coro_yield") ||
           starts_with(frame->function, "lyr_coro_close") || starts_with(frame->function, "lyr_poller") ||
           starts_with(frame->function, "lyr_task_wait") || starts_with(frame->function, "lyr_task_start");
}

static int same_text(const char *a, const char *b) {
    return a == b || (a && b && strcmp(a, b) == 0);
}

static int same_place(const Frame *a, const Frame *b) {
    if (a->function == NULL && b->function == NULL) return a->pc == b->pc;
    return same_text(a->function, b->function) && same_text(a->file, b->file) && a->line == b->line;
}

/* snprintf into what is left of `out`; a line that does not fit is cut, never overrun. */
LYR_PRINTF(4, 5) static size_t append(char *out, size_t capacity, size_t used, const char *format, ...) {
    if (used + 1 >= capacity) return used;
    va_list args;
    va_start(args, format);
    int n = vsnprintf(out + used, capacity - used, format, args);
    va_end(args);
    if (n < 0) return used;
    return used + ((size_t)n < capacity - used ? (size_t)n : capacity - used - 1);
}

static size_t note_repeats(char *out, size_t capacity, size_t used, int repeats) {
    if (repeats == 0) return used;
    return append(out, capacity, used, "    ... the frame above repeats %d more time%s\n", repeats, repeats == 1 ? "" : "s");
}

static size_t emit(char *out, size_t capacity, uintptr_t fault_pc);

size_t lyr_trace_format(char *out, size_t capacity, const LyrFault *fault) {
    if (capacity == 0) return 0;
    out[0] = '\0';
    trace.count = 0;
    trace.truncated = 0;
    uintptr_t fault_pc = fault ? fault->pc : 0;
#ifndef _WIN32
    capture(fault);
#else
    capture_at(fault_pc);
#endif
    return emit(out, capacity, fault_pc);
}

size_t lyr_trace_format_pcs(char *out, size_t capacity, const uintptr_t *pcs, int count) {
    if (capacity == 0) return 0;
    out[0] = '\0';
    trace.count = 0;
    trace.truncated = count >= MAX_PCS;  /* the capture filled its buffer */
    name_captured(pcs, count);
    return emit(out, capacity, 0);
}

/* The named trace as lines: from the faulting frame, or below the runtime's own frames; up to
 * lyr_run_main; a recursion's repeats folded. */
static size_t emit(char *out, size_t capacity, uintptr_t fault_pc) {

    /* The faulting frame's pc is the fault address itself when the unwinder knows it came from a
     * signal frame (Linux, the macOS frame walk, Windows), and one byte less when it treats it as
     * a return address like any other. */
    int start = -1;
    if (fault_pc != 0) {
        for (int i = 0; i < trace.count && start < 0; i++) {
            if (trace.frames[i].pc == fault_pc || trace.frames[i].pc == fault_pc - 1) start = i;
        }
    }
    if (start < 0) {
        start = 0;
        for (int i = 0; i < trace.count; i++) {
            if (is_runtime_frame(&trace.frames[i])) start = i + 1;
        }
    }
    /* The program's frames end at the runtime's entry — or at the emitted glue that calls main from
     * there, 'lyr_entry', whose line is whatever '#line' stood last — or, on a coroutine's stack,
     * at the emitted runner that calls the body ('lyr_corun'), at a task's body or main's as a
     * task ('lyr_task_run', 'lyr_task_main'), or the runtime's frame below them. */
    int end = trace.count, reached_main = 0;
    for (int i = start; i < trace.count; i++) {
        const char *function = trace.frames[i].function;
        if (function && (strcmp(function, "lyr_run_main") == 0 || strcmp(function, "lyr_entry") == 0
                         || strcmp(function, "lyr_coro_main") == 0 || starts_with(function, "lyr_corun")
                         || strcmp(function, "lyr_task_run") == 0 || strcmp(function, "lyr_task_main") == 0)) {
            end = i;
            reached_main = 1;
            break;
        }
    }

    size_t used = 0;
    int repeats = 0;
    for (int i = start; i < end && used + 1 < capacity; i++) {
        const Frame *frame = &trace.frames[i];
        /* The emitted thunk that makes a function without an environment a function value (01 V8):
         * glue, at a line of the C file — the frames around it are the program's. */
        if (frame->function && starts_with(frame->function, "lyr_thunk_")) continue;
        /* A recursion — a stack overflow's usual cause — shows its frame once, and a count. */
        if (i > start && same_place(frame, &trace.frames[i - 1])) {
            repeats++;
            continue;
        }
        used = note_repeats(out, capacity, used, repeats);
        repeats = 0;
        if (frame->function && frame->file) {
            used = append(out, capacity, used, "    at %s (%s:%d)\n", frame->function, frame->file, frame->line);
        } else if (frame->function) {
            used = append(out, capacity, used, "    at %s\n", frame->function);
        } else {
            used = append(out, capacity, used, "    at 0x%llx\n", (unsigned long long)frame->pc);
        }
    }
    used = note_repeats(out, capacity, used, repeats);
    if (trace.truncated && !reached_main) used = append(out, capacity, used, "    ... deeper frames not shown\n");
    return used;
}
