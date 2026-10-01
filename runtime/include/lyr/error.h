/* Errors at run time (design/v5/spec/05 E1-E3, E6; 01 L5): a thrown value travels as a record in
 * the caller's error slot - the hidden last parameter of every throwing function - and a set slot
 * is the status. The record is allocated at the throw only: a call that does not fail costs one
 * compare. */
#ifndef LYR_ERROR_H
#define LYR_ERROR_H

#include "lyr/types.h"

/* The record a throw allocates (01 L5 E2): the thrown value as an `Error` interface value - the
 * object, or the box a value was copied into, with its table - and what lives beside the value
 * rather than in its type (05 E6 O3): the errors suppressed while this one was in flight and, in
 * the debug profile, the trace of the throw. Both are filled from M5 S3. A heap object, so the
 * collector keeps the value alive while it travels. */
typedef struct LyrErr {
    LyrObj header;
    LyrIface value;
    struct LyrArr *suppressed;
    void *trace;
} LyrErr;

extern const LyrDesc lyr_desc_err;

/* The record for a value thrown here. */
LyrErr *lyr_err_new(LyrIface value);

/* `main`'s report (05 E6 O4): `error: <message>`, then `  caused by: <message>` for each cause, on
 * the error writer; the answer is the exit code, 1. The emitted entry passes the two members of
 * `Error` it can call - the runtime knows no Lyric method. `cause` writes the cause to `next` and
 * answers whether there is one; both may be NULL where no error can ever be thrown. */
struct LyrStr;
typedef const struct LyrStr *(*LyrErrMessage)(LyrIface error);
typedef int (*LyrErrCause)(LyrIface error, LyrIface *next);
int lyr_err_report(const LyrErr *err, LyrErrMessage message, LyrErrCause cause);

#endif
