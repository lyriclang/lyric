/* What a C program that links a Lyric library calls: the runtime's start and stop, and the
 * status a throwing export answers. The header `lyric build --lib` writes beside a library holds
 * this text, then the library's own types and exports. */
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
 * parameter, LYR_ERROR where it threw. */
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

/* One runtime per process, started before the first export is called, on the thread that calls
 * them: a second call returns -1 and changes nothing. NULL starts it with zeros. */
LYRIC_API int lyr_init(const LyrConfig *config);
LYRIC_API void lyr_shutdown(void);

#ifdef __cplusplus
}
#endif

#endif
