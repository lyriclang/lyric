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

enum { MAX_PCS = 256, MAX_FRAMES = 384 };

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

static void capture(void) {
    struct backtrace_state *st = get_state();
    if (st == NULL) return;
    static Pcs pcs;
    pcs.count = 0;
    pcs.full = 0;
    backtrace_simple(st, 0, collect_pc, ignore_error, &pcs);
    trace.truncated = pcs.full;
    for (int i = 0; i < pcs.count; i++) {
        Lookup lookup = { pcs.pc[i], 0 };
        backtrace_pcinfo(st, pcs.pc[i], name_frame, ignore_error, &lookup);
        if (!lookup.found) {
            /* No line table here (a system library, a stripped build): the symbol table may still
             * know the function. */
            const char *symbol = NULL;
            backtrace_syminfo(st, pcs.pc[i], name_symbol, ignore_error, &symbol);
            add_frame(pcs.pc[i], symbol, NULL, 0);
        }
    }
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
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_LOAD_LINES |
                  SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS);
    SymInitialize(process, search, TRUE);
}

static void capture_at(uintptr_t fault_pc) {
    static void *pcs[MAX_PCS];
    USHORT count = RtlCaptureStackBackTrace(0, MAX_PCS, pcs, NULL);
    trace.truncated = count == MAX_PCS;
    HANDLE process = GetCurrentProcess();
    init_symbols(process);
    names_used = 0;
    for (USHORT i = 0; i < count; i++) {
        uintptr_t pc = (uintptr_t)pcs[i];
        /* A return address names the instruction after the call; one byte back is the call. The
         * faulting instruction itself is exact. */
        DWORD64 address = pc == fault_pc ? pc : pc - 1;
        union {
            SYMBOL_INFO info;
            char bytes[sizeof(SYMBOL_INFO) + 256];
        } symbol;
        symbol.info.SizeOfStruct = sizeof(SYMBOL_INFO);
        symbol.info.MaxNameLen = 255;
        DWORD64 offset = 0;
        const char *function = SymFromAddr(process, address, &offset, &symbol.info) ? keep(symbol.info.Name) : NULL;
        IMAGEHLP_LINE64 line;
        line.SizeOfStruct = sizeof line;
        DWORD column = 0;
        if (SymGetLineFromAddr64(process, address, &column, &line)) {
            add_frame(pc, function, keep(line.FileName), (int)line.LineNumber);
        } else {
            add_frame(pc, function, NULL, 0);
        }
    }
}
#endif

/* --- filtering and format ------------------------------------------------------------------ */

static int starts_with(const char *text, const char *prefix) {
    return text && strncmp(text, prefix, strlen(prefix)) == 0;
}

static int is_runtime_frame(const Frame *frame) {
    return starts_with(frame->function, "lyr_panic") || starts_with(frame->function, "lyr_crash") ||
           starts_with(frame->function, "lyr_trace");
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

size_t lyr_trace_format(char *out, size_t capacity, uintptr_t fault_pc) {
    if (capacity == 0) return 0;
    out[0] = '\0';
    trace.count = 0;
    trace.truncated = 0;
#ifndef _WIN32
    capture();
#else
    capture_at(fault_pc);
#endif

    /* The faulting frame's pc is the fault address itself when the unwinder knows it came from a
     * signal frame (Linux), and one byte less when it treats it as a return address like any
     * other (macOS: libbacktrace steps back into "the call"). */
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
    int end = trace.count, reached_main = 0;
    for (int i = start; i < trace.count; i++) {
        if (trace.frames[i].function && strcmp(trace.frames[i].function, "lyr_run_main") == 0) {
            end = i;
            reached_main = 1;
            break;
        }
    }

    size_t used = 0;
    int repeats = 0;
    for (int i = start; i < end && used + 1 < capacity; i++) {
        const Frame *frame = &trace.frames[i];
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
