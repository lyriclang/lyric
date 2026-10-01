/* The error record and main's report (design/v5/spec/05 E6 O3/O4, 01 L5 E1-E2). */
#include "lyr/error.h"
#include "lyr/gc.h"
#include "lyr/init.h"
#include <string.h>

/* Word 1 is the value's object and word 3 the suppressed array; word 2 is the value's table, which
 * is static, and word 4 the trace, which the collector does not own. */
static const uint64_t err_refmap[] = { (UINT64_C(1) << 1) | (UINT64_C(1) << 3) };
const LyrDesc lyr_desc_err = { sizeof(LyrErr), LYR_DESC_HAS_REFS, 0, 1, err_refmap, "error", NULL };

LyrErr *lyr_err_new(LyrIface value) {
    LyrErr *err = (LyrErr *)lyr_alloc(&lyr_desc_err);
    err->value = value;
    return err;
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
    return 1;
}
