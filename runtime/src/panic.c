/* The panic path. S2: message and exit code; the backtrace, the host hook and the crash handler
 * for fault signals come with M1 S4. */
#include "lyr/panic.h"
#include "lyr/init.h"

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void lyr_panic(const char *code, const char *format, ...) {
    char message[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(message, sizeof message, format, args);
    va_end(args);

    char line[1200];
    int n = snprintf(line, sizeof line, "panic [%s]: %s\n", code, message);
    if (n < 0) n = 0;
    if ((size_t)n >= sizeof line) n = (int)sizeof line - 1;
    lyr_write_stderr(line, (size_t)n);
    exit(101);
}
