/* Errors at run time (design/v5/spec/05 E1-E3, E6; 01 L5): a thrown value travels as a record in
 * the caller's error slot - the hidden last parameter of every throwing function - and a set slot
 * is the status. The record is allocated at the throw only: a call that does not fail costs one
 * compare. */
#ifndef LYR_ERROR_H
#define LYR_ERROR_H

#include "lyr/types.h"
#include "lyr/panic.h"

/* The record a throw allocates (01 L5 E2): the thrown value as an `Error` interface value - the
 * object, or the box a value was copied into, with its table - and what lives beside the value
 * rather than in its type (05 E6 O3): the errors suppressed while this one was in flight and, in
 * the debug profile, the trace of the throw. Both are filled from M5 S3. A heap object, so the
 * collector keeps the value alive while it travels. */
typedef struct LyrErr {
    LyrObj header;
    LyrIface value;
    struct LyrArr *suppressed;
    struct LyrArr *trace;
} LyrErr;

extern const LyrDesc lyr_desc_err;

/* The record for a value thrown here. */
LyrErr *lyr_err_new(LyrIface value);

/* The same, keeping where it was thrown (01 E8): the program counters of the throw, named only if
 * the error is ever reported. The debug profile's — emitted code compiled without NDEBUG calls it
 * under the plain name. */
LyrErr *lyr_err_new_traced(LyrIface value);
#ifndef NDEBUG
#define lyr_err_new(value) lyr_err_new_traced(value)
#endif

/* A defer body failed while `into` was in flight (05 E7): the first error wins, and the body's is
 * appended to its suppressed errors. */
void lyr_err_suppress(LyrErr *into, const LyrErr *err);

/* `main`'s report (05 E6 O4): `error: <message>`, then `  caused by: <message>` for each cause and
 * `  suppressed: <message>` for each suppressed error, on the error writer; the answer is the exit
 * code, 1. The emitted entry passes the two members of
 * `Error` it can call - the runtime knows no Lyric method. `cause` writes the cause to `next` and
 * answers whether there is one; both may be NULL where no error can ever be thrown. */
struct LyrStr;
typedef const struct LyrStr *(*LyrErrMessage)(LyrIface error);
typedef int (*LyrErrCause)(LyrIface error, LyrIface *next);
int lyr_err_report(const LyrErr *err, LyrErrMessage message, LyrErrCause cause);

/* `try!` on an error (05 E4, E8): a panic, code RT0010, with the error's message - or without one
 * where the program has no member to ask. */
LYR_NORETURN void lyr_panic_error(const LyrErr *err, LyrErrMessage message);

#endif
