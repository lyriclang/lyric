/* The operating system's facts (design/v5/spec/10 Q9): M8b S1, the processors; S6a, the platform;
 * S9, the rest of std.os — the arguments, the environment, the working directory, the end. A text
 * comes back through the caller's buffer, as lyr/fs.h's paths do: its length, and a length beyond
 * `n` says the room it needs; a failure is negative, as lyr/fs.h writes one. */
#ifndef LYR_OS_H
#define LYR_OS_H

#include <stdint.h>

#include "lyr/panic.h"
#include "lyr/types.h"

/* The processors the system runs this process on — those online —, one at least. */
int64_t lyr_os_cpu_count(void);

/* The system the program was built for (M8b S6a, for std.path's separators; S9's os.platform):
 * 1 Linux, 2 macOS, 3 Windows, 0 another POSIX system. */
int64_t lyr_os_platform(void);

/* The machine it was built for: 1 x86-64, 2 AArch64, 0 another. */
int64_t lyr_os_arch(void);

/* The process's own number. */
int64_t lyr_os_pid(void);

/* The program's arguments, its own name first: how many, and the `i`-th as UTF-8. On Windows an
 * emitted program's come from the command line as UTF-16 (CommandLineToArgvW), not from the C
 * library's narrow argv; a host's argv is taken as given. */
int64_t lyr_os_arg_count(void);
int64_t lyr_os_arg(int64_t i, uint8_t *into, int64_t n);

/* The environment: a variable's value (NotFound where it is not set), all of it as
 * `NAME=value\0` one after another in one snapshot, a variable set. The runtime's own reads and
 * writes of it take a lock; a C library reading it on another thread does not. */
int64_t lyr_os_env(const LyrStr *name, uint8_t *into, int64_t n);
int64_t lyr_os_envs(uint8_t *into, int64_t n);
int64_t lyr_os_set_env(const LyrStr *name, const LyrStr *value);

/* The working directory, and the working directory moved: 0. */
int64_t lyr_os_cwd(uint8_t *into, int64_t n);
int64_t lyr_os_set_cwd(const LyrStr *path);

/* The machine's name. */
int64_t lyr_os_hostname(uint8_t *into, int64_t n);

/* The program ended with `code`: what the console holds written first, nothing else run (07 M7f). */
LYR_NORETURN void lyr_os_exit(int64_t code);

/* A Slice<uint8> of emitted code is a pointer and a length (CEmitter). */
#define LYR_OS_ARG(i, slice) lyr_os_arg((i), (slice).ptr, (slice).len)
#define LYR_OS_ENV(name, slice) lyr_os_env((name), (slice).ptr, (slice).len)
#define LYR_OS_ENVS(slice) lyr_os_envs((slice).ptr, (slice).len)
#define LYR_OS_CWD(slice) lyr_os_cwd((slice).ptr, (slice).len)
#define LYR_OS_HOSTNAME(slice) lyr_os_hostname((slice).ptr, (slice).len)

#endif
