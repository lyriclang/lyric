/* What a C program that links a Lyric library calls: the runtime's start and stop, the status a
 * throwing export answers and what it failed with, the pump for the tasks an export left running.
 * The header `lyric build --lib` writes beside a library holds this text, then the library's own
 * types and exports. */
#ifndef LYRIC_H
#define LYRIC_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* What a shared library gives the system's loader: its exports and the runtime's start and stop,
 * nothing else of the runtime's. */
#if defined(LYRIC_BUILDING_SHARED) && defined(_WIN32)
#  define LYRIC_API __declspec(dllexport)
#elif defined(LYRIC_BUILDING_SHARED)
#  define LYRIC_API __attribute__((visibility("default")))
#else
#  define LYRIC_API
#endif

/* What an export that throws answers: LYR_OK with its result written through its last
 * parameter, LYR_ERROR where it threw, LYR_PANIC where it panicked — lyr_take_error says what.
 * The runtime goes on after either. */
typedef enum LyrStatus {
    LYR_OK = 0,
    LYR_ERROR = 1,
    LYR_PANIC = 2,
} LyrStatus;

/* Where the runtime's own output goes. NULL means the process's standard stream; a host passes a
 * function to capture it (an editor showing a program's println). */
typedef void (*LyrWriteFn)(const char *bytes, size_t len, void *context);

typedef struct LyrConfig {
    int argc;
    char **argv;
    size_t heap_limit;             /* bytes; 0 = no limit. Beyond it allocation panics (RT0005) */
    int install_signal_handlers;   /* crash reports for fault signals; an embedding host says 0 */
    LyrWriteFn stdout_write;
    LyrWriteFn stderr_write;
    void *write_context;
} LyrConfig;

/* One runtime per process, started before the first export is called: a second call answers
 * LYR_ERROR and changes nothing. NULL starts it with zeros. */
LYRIC_API LyrStatus lyr_init(const LyrConfig *config);
LYRIC_API void lyr_shutdown(void);

/* What the last export that answered LYR_ERROR or LYR_PANIC on the calling thread failed with.
 * An error: its type as Lyric names it (`geo.Odd`), its message, the error that caused it down to
 * NULL. A panic: its code (`LYR-RT0008`) as the type, its message, its frames. The strings stay
 * the runtime's until the thread's next lyr_take_error. */
typedef struct LyrError {
    LyrStatus status;
    const char *type;
    const char *message;
    const char *trace;              /* a panic's frames, a line each; NULL for an error */
    const struct LyrError *cause;
} LyrError;

/* Takes what the thread's last failed export left into `error`, and answers its status: LYR_OK,
 * with `error` zeroed, where nothing waits — taken already, or nothing failed. */
LYRIC_API LyrStatus lyr_take_error(LyrError *error);

/* An export runs as a task on the calling thread and returns when that task is done; a task it
 * started with spawnDetached goes on through these. lyr_step runs what is ready or due once,
 * without waiting, and answers whether tasks remain on the thread; lyr_run runs them all to their
 * end, waiting as they wait. */
LYRIC_API bool lyr_step(void);
LYRIC_API void lyr_run(void);

/* The first export a thread calls makes it Lyric's; this gives it back before the thread ends.
 * Its tasks that remain never run again. Nothing on the thread that called lyr_init. */
LYRIC_API void lyr_detach_thread(void);

#ifdef __cplusplus
}
#endif

#endif
