/* The error record and main's report (design/v5/spec/05 E6 O3/O4, 01 L5 E1-E2). */
#include "lyr/error.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include "lyr/panic.h"
#include "internal.h"
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
        err->trace = kept;
    }
    return err;
}

/* The suppressed errors, an `Error[]` (05 E6 O3): interface values, whose object word is the
 * reference. Grown by one on the rare path where a defer fails during a failure. */
static const uint64_t suppressed_refmap[] = { UINT64_C(0x1) };
static const LyrDesc suppressed_desc = { (uint32_t)offsetof(LyrArr, data), LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                         sizeof(LyrIface), 1, suppressed_refmap, "Error[]", NULL };

void lyr_err_suppress(LyrErr *into, const LyrErr *err) {
    int64_t had = into->suppressed != NULL ? into->suppressed->len : 0;
    LyrArr *grown = lyr_alloc_array(&suppressed_desc, had + 1);
    if (had > 0) memcpy(grown->data, into->suppressed->data, (size_t)had * sizeof(LyrIface));
    LYR_ARR_DATA(grown, LyrIface)[had] = err->value;
    into->suppressed = grown;
}

static void write_line(const char *prefix, const LyrStr *text) {
    lyr_write_stderr(prefix, strlen(prefix));
    if (text != NULL) lyr_write_stderr(text->bytes, (size_t)text->len);
    lyr_write_stderr("\n", 1);
}

int lyr_err_report(const LyrErr *err, LyrErrMessage message, LyrErrCause cause) {
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
    /* Where it was thrown, where the profile kept it (01 E8). */
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
