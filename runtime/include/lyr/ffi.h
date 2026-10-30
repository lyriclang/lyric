/* What a call into C needs beside the call itself: the symbol a C function goes by, and the
 * conversions std.ffi's types make at the boundary. */
#ifndef LYR_FFI_H
#define LYR_FFI_H

#include <stdint.h>

#include "lyric.h"
#include "lyr/types.h"
#include "lyr/coro.h"
#include "lyr/error.h"
#include "lyr/gc.h"

/* The name the linker knows a C function by: its own, behind the platform's prefix ('_' on
 * macOS, none elsewhere). The program declares a C function under a name of its own and binds it
 * to this one with an asm label, so its prototype never meets the one a system header gives the
 * same function — 'strlen' returns 'size_t' there, which is not 'uint64_t' on every target. */
#define LYR_C_STRINGIFY_(x) #x
#define LYR_C_STRINGIFY(x) LYR_C_STRINGIFY_(x)
#if defined(__USER_LABEL_PREFIX__)
#  define LYR_C_SYMBOL(name) LYR_C_STRINGIFY(__USER_LABEL_PREFIX__) name
#else
#  define LYR_C_SYMBOL(name) name
#endif

/* The address of a string's bytes, which a NUL ends: what a `string` handed to a `CStr` parameter
 * is, without a copy. */
uint64_t lyr_ffi_string_address(const LyrStr *s);

/* The bytes of the C string at `address` before its NUL. */
int64_t lyr_ffi_cstr_length(uint64_t address);

/* The offset of the first byte of the C string at `address` that is no well-formed UTF-8, or -1. */
int64_t lyr_ffi_cstr_invalid(uint64_t address);

/* A copy of the C string at `address` as a string; its bytes are well-formed UTF-8. */
LyrStr *lyr_ffi_cstr_string(uint64_t address);

/* The tag of an enum value C returned: `value` itself where the enum has a variant for it, a
 * panic (LYR-RT0021) where it has none — a C enum may hold any integer, a Lyric enum holds one
 * of its variants. */
int64_t lyr_ffi_enum_tag(int64_t value, int64_t variants, const char *enum_name);

/* The same for an enum whose variants are given C's integers: the index of `value` among
 * `values`, one per variant, or the panic where none is it. */
int64_t lyr_ffi_enum_index(int64_t value, const int64_t *values, int64_t variants, const char *enum_name);

/* std.ffi.alloc: count values of size bytes each, set to zero, from C's heap — never null: a
 * negative count panics (LYR-RT0007), no memory left panics (LYR-RT0005). */
uint64_t lyr_ffi_alloc(int64_t count, uint64_t size);

/* std.ffi.free: the memory lyr_ffi_alloc gave, back to C's heap. */
void lyr_ffi_free(uint64_t address);

/* A callback's trampoline, around the Lyric function C calls: the frames below are C's, which the
 * running coroutine counts (lyr_coro_enter_foreign). On a thread Lyric does not run — attached to
 * no collector — there is no Lyric to call, and the process ends with a report. */
void lyr_ffi_callback_enter(void);
void lyr_ffi_callback_leave(void);

/* What a library's exports run under where its code waits: std.task's side of the embedding. `run`
 * takes an export's context and runs the thread's scheduler until it is done — making the
 * scheduler at the thread's first call —, answering as lyr_task_resume; `step`, `pump` and `end`
 * are lyr_step, lyr_run and lyr_detach_thread's. */
typedef struct LyrEmbedding {
    int64_t (*run)(LyrCoro *context);
    uint8_t (*step)(void);
    void (*pump)(void);
    void (*end)(void);
} LyrEmbedding;

/* An export's call (@Export), from its wrapper: `body` calls the Lyric function with the values in
 * `frame` and leaves its result there. The runtime must be started (lyr_init) — else the process
 * ends with a report —, and a thread that never ran Lyric is attached. Where Lyric runs below the
 * call on the thread — C that Lyric called calls the export — the body runs right there, in the
 * running task, between the marks of C's frames; elsewhere in a task of its own, under `embedding`
 * where the library waits (NULL where it never does). The library's own start comes first, at its
 * first call: its globals registered and filled. `start` is NULL in a program, whose main did that.
 * A panic of that task is LYR_PANIC, kept for lyr_take_error, where `throws` — the wrapper can say
 * so —, else the process ends with it. */
LyrStatus lyr_export_call(void (*start)(void), const LyrEmbedding *embedding, void (*body)(void *frame),
                          void *frame, int throws);

/* Keeps the error an export's function threw for lyr_take_error: its type, its message and its
 * causes' through Error's members, which the program defines. Inside the export's task. */
void lyr_export_keep_error(const LyrErr *err, LyrErrMessage message, LyrErrCause cause);

/* std.ffi.Library (runtime/src/library.c): a shared library loaded while the program runs. The
 * handle, 0 where the loader refused it — lyr_ffi_library_error says why, on the same thread —;
 * a symbol's address, 0 where the library has none; the library given back. */
uint64_t lyr_ffi_library_open(const struct LyrStr *path);
struct LyrStr *lyr_ffi_library_error(void);
uint64_t lyr_ffi_library_symbol(uint64_t handle, const struct LyrStr *name);
void lyr_ffi_library_close(uint64_t handle);

/* std.ffi.Callback: a Lyric closure C holds through its userdata — its code and its environment
 * in an object a root keeps alive until the callback is released. The userdata C hands back is the
 * root's address; the trampoline reads the closure through it. */
typedef struct LyrFfiOwner LyrFfiOwner;

typedef struct LyrFfiClosure {
    LyrObj header;
    void *code;
    void *env;
    LyrFfiOwner *owner;  /* the thread that made it, which runs what C calls on a thread of its own */
} LyrFfiClosure;

uint64_t lyr_ffi_closure_root(void *code, void *env);
static inline LyrFfiClosure *lyr_ffi_closure_of(void *userdata) {
    return (LyrFfiClosure *)lyr_root_get((const LyrRoot *)userdata);
}
void lyr_ffi_closure_release(uint64_t root);

/* A Callback's end: its root freed, its owner told it has one fewer that a thread of C's may call. */
void lyr_ffi_callback_release(uint64_t root);

/* A Callback C calls on a thread Lyric does not run, and which returns nothing: the call — `run`
 * with what C passed, copied into `payload`, malloc'd — goes to its owner, which runs it in its
 * scheduler's next turn and frees the payload; C's thread goes on at once. An owner that never ran
 * a scheduler has no turn to run it in: the process ends (LYR-RT0022). */
void lyr_ffi_enqueue(LyrFfiOwner *owner, void (*run)(void *), void *payload);
void *lyr_ffi_call_alloc(size_t size);  /* the payload's memory, never null (LYR-RT0005) */

/* The owner's side, its scheduler's: runs what C's threads handed it, in the order they came. */
void lyr_ffi_drain(void);

/* How many Callbacks this thread owns, which a thread of C's may still call: while any lives, a
 * scheduler with every task waiting is not deadlocked — a call may come and wake one. */
int64_t lyr_ffi_callbacks_alive(void);

/* std.ffi.GcHandle: the object a root holds, which must be of the class `desc` — a handle made
 * again from C's pointer (GcHandle.from) may be another class's: a panic (LYR-RT0023). A root
 * released is gone with its memory; reading it is the unsafe block's word, not this check's. */
void *lyr_ffi_rooted(uint64_t root, const LyrDesc *desc);

#endif
