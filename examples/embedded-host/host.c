/* A C program that embeds the Lyric library of this directory: it starts the runtime with its
 * output captured, calls the exports, pumps the tasks one left running, reads an error and a
 * panic as a status, and calls an export from a second thread of its own.
 *
 *     lyric build --lib
 *     zig cc host.c -I out/debug/<target> out/debug/<target>/libtally.a -o host
 */
#include <stdio.h>
#include <string.h>

#include "tally.h"

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#endif

/* What Lyric writes comes here instead of to the standard output: each of its lines marked. */
static int at_line_start = 1;

static void captured(const char *bytes, size_t len, void *context) {
    FILE *out = context;
    for (size_t i = 0; i < len; i++) {
        if (at_line_start) fputs("lyric> ", out);
        fputc(bytes[i], out);
        at_line_start = bytes[i] == '\n';
    }
}

/* What the last export that failed on this thread left: the status, then each error of the chain. */
static void show(const char *what) {
    LyrError error;
    LyrStatus status = lyr_take_error(&error);
    printf("%s: status %d\n", what, (int)status);
    for (const LyrError *at = &error; at != NULL && at->type != NULL; at = at->cause)
        printf("  %s: %s\n", at->type, at->message);
    if (error.trace != NULL) printf("  with its frames\n");
}

static int64_t second_sum;

static void second(void) {
    /* the thread's first export makes it Lyric's, with a scheduler of its own */
    second_sum = tally_sum(10);
    lyr_detach_thread();
}

#ifdef _WIN32
static DWORD WINAPI second_thread(LPVOID unused) {
    (void)unused;
    second();
    return 0;
}
#else
static void *second_thread(void *unused) {
    (void)unused;
    second();
    return NULL;
}
#endif

int main(void) {
    LyrConfig config;
    memset(&config, 0, sizeof config);
    config.stdout_write = captured;
    config.write_context = stdout;
    if (lyr_init(&config) != LYR_OK) return 1;
    printf("init again: %d\n", (int)lyr_init(&config));

    /* an export that waits: synchronous here */
    printf("sum: %lld\n", (long long)tally_sum(100));

    /* tasks an export left: the pump runs them */
    tally_start(3);
    int turns = 0;
    while (lyr_step()) turns++;
    printf("pumped: %s\n", turns > 0 ? "yes" : "no");
    tally_start(2);
    lyr_run();

    /* errors and panics as a status */
    int64_t n = -1;
    LyrStatus status = tally_parse("42", &n);
    printf("parse 42: %d %lld\n", (int)status, (long long)n);
    tally_parse("4x2", &n);
    show("parse 4x2");
    tally_check(0, &n);
    show("check 0");
    tally_check(-1, &n);
    show("check -1");
    status = tally_check(7, &n);
    printf("check 7: %d %lld\n", (int)status, (long long)n);
    show("nothing left");

    /* a thread of the host's own */
#ifdef _WIN32
    HANDLE thread = CreateThread(NULL, 0, second_thread, NULL, 0, NULL);
    if (thread == NULL) return 2;
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
#else
    pthread_t thread;
    if (pthread_create(&thread, NULL, second_thread, NULL) != 0) return 2;
    pthread_join(thread, NULL);
#endif
    printf("second thread: %lld\n", (long long)second_sum);

    lyr_shutdown();
    return 0;
}
