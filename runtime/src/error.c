/* The error record, what a clause asks of it, and main's report. */
#include "lyr/error.h"
#include "lyr/console.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include "lyr/panic.h"
#include "lyr/string.h"
#include "internal.h"
#include <stdlib.h>
#include <string.h>

/* Word 1 is the value's object, word 3 the suppressed array and word 4 the trace; word 2 is the
 * value's table, which is static. */
static const uint64_t err_refmap[] = { (UINT64_C(1) << 1) | (UINT64_C(1) << 3) | (UINT64_C(1) << 4) };
const LyrDesc lyr_desc_err = { sizeof(LyrErr), LYR_DESC_HAS_REFS, 0, 1, err_refmap, "error", NULL };

/* The name in parentheses: the debug profile's macro of the same name is for emitted code. */
LyrErr *(lyr_err_new)(LyrIface value) {
    LyrErr *err = (LyrErr *)lyr_alloc(&lyr_desc_err);
    err->value = value;
    return err;
}

/* The program counters of a throw: words, no references. */
static const LyrDesc trace_desc = { (uint32_t)offsetof(LyrArr, data), LYR_DESC_ARRAY, sizeof(uintptr_t), 0, NULL, "trace", NULL };

LyrErr *lyr_err_new_traced(LyrIface value) {
    LyrErr *err = (lyr_err_new)(value);
    uintptr_t pcs[LYR_TRACE_PCS];
    int count = lyr_trace_capture(pcs, LYR_TRACE_PCS);
    if (count > 0) {
        LyrArr *kept = lyr_alloc_array(&trace_desc, count);
        memcpy(kept->data, pcs, (size_t)count * sizeof(uintptr_t));
        /* the allocation may have collected: err may be old by now */
        LYR_WRITE_BARRIER(err, &err->trace, kept);
    }
    return err;
}

/* The suppressed errors, an `Error[]`: interface values, whose object word is the
 * reference. Grown by one on the rare path where a defer fails during a failure. */
static const uint64_t suppressed_refmap[] = { UINT64_C(0x1) };
static const LyrDesc suppressed_desc = { (uint32_t)offsetof(LyrArr, data), LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                         sizeof(LyrIface), 1, suppressed_refmap, "Error[]", NULL };

void lyr_err_suppress(LyrErr *into, const LyrErr *err) {
    int64_t had = into->suppressed != NULL ? into->suppressed->len : 0;
    LyrArr *grown = lyr_alloc_array(&suppressed_desc, had + 1);
    if (had > 0) memcpy(grown->data, into->suppressed->data, (size_t)had * sizeof(LyrIface));
    LYR_ARR_DATA(grown, LyrIface)[had] = err->value;
    LYR_WRITE_BARRIER(into, &into->suppressed, grown);
}

LyrErr *lyr_err_unsuppress(const LyrErr *dropped) {
    if (dropped->suppressed == NULL || dropped->suppressed->len == 0) return NULL;
    int64_t count = dropped->suppressed->len;
    const LyrIface *all = (const LyrIface *)dropped->suppressed->data;
    LyrErr *err = (lyr_err_new)(all[0]);
    if (count > 1) {
        LyrArr *rest = lyr_alloc_array(&suppressed_desc, count - 1);
        memcpy(rest->data, all + 1, (size_t)(count - 1) * sizeof(LyrIface));
        LYR_WRITE_BARRIER(err, &err->suppressed, rest);
    }
    LYR_WRITE_BARRIER(err, &err->trace, dropped->trace);
    return err;
}

LyrArr *lyr_err_suppressed(const LyrErr *err, const LyrDesc *desc) {
    int64_t count = err->suppressed != NULL ? err->suppressed->len : 0;
    LyrArr *copy = lyr_alloc_array(desc, count);
    if (count > 0) memcpy(copy->data, err->suppressed->data, (size_t)count * sizeof(LyrIface));
    return copy;
}

/* The trace keeps addresses; the program reads `int`s. */
LyrArr *lyr_err_trace(const LyrErr *err, const LyrDesc *desc) {
    int64_t count = err->trace != NULL ? err->trace->len : 0;
    LyrArr *pcs = lyr_alloc_array(desc, count);
    for (int64_t i = 0; i < count; i++)
        LYR_ARR_DATA(pcs, int64_t)[i] = (int64_t)LYR_ARR_DATA(err->trace, uintptr_t)[i];
    return pcs;
}

LyrStr *lyr_trace_text(const LyrArr *pcs) {
    uintptr_t at[LYR_TRACE_PCS];
    int count = pcs->len < LYR_TRACE_PCS ? (int)pcs->len : LYR_TRACE_PCS;
    for (int i = 0; i < count; i++) at[i] = (uintptr_t)LYR_ARR_DATA(pcs, int64_t)[i];
    /* main's report's room, off the stack: a task's is small */
    enum { ROOM = 16 * 1024 };
    char *text = malloc(ROOM);
    if (text == NULL) return lyr_str_from_bytes("", 0);
    size_t n = lyr_trace_format_pcs(text, ROOM, at, count);
    LyrStr *shown = lyr_str_from_bytes(text, (int64_t)n);
    free(text);
    return shown;
}

static void write_line(const char *prefix, const LyrStr *text) {
    lyr_write_stderr(prefix, strlen(prefix));
    if (text != NULL) lyr_write_stderr(text->bytes, (size_t)text->len);
    lyr_write_stderr("\n", 1);
}

int lyr_err_report(const LyrErr *err, LyrErrMessage message, LyrErrCause cause) {
    /* what the program wrote before, ahead of the report */
    lyr_console_flush_all(1);
    if (message == NULL) {
        write_line("error: ", NULL);
        return 1;
    }
    write_line("error: ", (const LyrStr *)message(err->value));
    /* The chain is the program's data, and a cycle in it is bounded here rather than followed
     * forever: a report that hangs reports nothing. */
    LyrIface at = err->value, next;
    for (int depth = 0; cause != NULL && depth < 64 && cause(at, &next); depth++) {
        write_line("  caused by: ", (const LyrStr *)message(next));
        at = next;
    }
    if (err->suppressed != NULL)
        for (int64_t i = 0; i < err->suppressed->len; i++)
            write_line("  suppressed: ", (const LyrStr *)message(LYR_ARR_DATA(err->suppressed, LyrIface)[i]));
    /* Where it was thrown, where the profile kept it. */
    if (err->trace != NULL) {
        static char text[16 * 1024];
        size_t n = lyr_trace_format_pcs(text, sizeof text, LYR_ARR_DATA(err->trace, uintptr_t), (int)err->trace->len);
        lyr_write_stderr(text, n);
    }
    return 1;
}

void lyr_panic_error(const LyrErr *err, LyrErrMessage message) {
    const LyrStr *text = message != NULL ? (const LyrStr *)message(err->value) : NULL;
    if (text == NULL) lyr_panic(LYR_RT_FORCED_TRY, "'try!' on an error");
    lyr_panic(LYR_RT_FORCED_TRY, "'try!' on an error: %.*s", (int)(text->len < 1000 ? text->len : 1000), text->bytes);
}
