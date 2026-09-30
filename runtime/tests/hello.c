/* The shape of every emitted program: a static literal, println, and lyr_run_main.
 * Expected: prints "Hello, Lyric!" and the program's arguments count, exits with the program's
 * value masked to 0..255 — this program returns 263, so the exit code is 7 (11 C5). */
#include "lyr/lyr.h"

static const LyrStaticStr(sizeof("Hello, Lyric!")) hello = LYR_STR_INIT("Hello, Lyric!");

static int64_t program(void) {
    lyr_println((LyrStr *)&hello);
    lyr_println(lyr_str_concat(lyr_str_from_cstr("args: "), lyr_str_from_int(lyr_argc() - 1)));
    return 263;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
