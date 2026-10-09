/* Processes (design/v5/spec/10 O8, Q9; 13 M8b S12): a child started with its arguments, working
 * directory, environment and three standard streams; its end, as the reaper sees it; a signal to it.
 * Each call answers a value, or a failure as lyr/fs.h writes one.
 *
 * POSIX: fork and exec — the parent finds the program on PATH first, the child does only what is
 * safe after a fork in a program with threads (its signal mask emptied, SIGPIPE back to its default:
 * an ignored signal outlives exec, Rust's Command resets it too; its streams; its directory) and
 * reports a failed exec through a pipe that exec closes. A child's end comes as SIGCHLD, which the
 * runtime keeps for itself (Q9): its handler writes to a pipe std.process's reaper watches, which
 * asks each child it waits for (waitpid, WNOHANG) — never waitpid(-1), which would take a host's
 * children too. Windows comes with S12b. */
#ifndef LYR_PROCESS_H
#define LYR_PROCESS_H

#include <stdint.h>

#include "lyr/types.h"

/* What a standard stream of the child is: the parent's, a pipe to the parent, or nothing. */
#define LYR_PROCESS_INHERIT 0
#define LYR_PROCESS_PIPED 1
#define LYR_PROCESS_NULL 2

/* A child of `program` — a path, or a name looked up on PATH — with `argc` arguments, its own name
 * the first, each NUL-ended in `args`; `envc` variables `NAME=value`, NUL-ended in `env`, set over
 * the parent's environment; its working directory `cwd` where it is not empty; its streams as
 * `modes` says, two bits each (stdin, stdout, stderr). The parent's ends of the piped ones go into
 * `ends` (three: -1 for one not piped), non-blocking and closed on exec. Its process id. */
int64_t lyr_process_spawn(const LyrStr *program, const uint8_t *args, int64_t argc, const uint8_t *env, int64_t envc,
                          const LyrStr *cwd, int64_t modes, int64_t *ends, int64_t n);

/* The reaper's pipe (once a process): the SIGCHLD handler installed, the pipe's read end watched by
 * the calling thread's poller (lyr/poll.h: its one watched descriptor, which the poller reads empty
 * as it wakes — so the reaper asks every child it waits for at each wake, and at its start). */
void lyr_process_attach(void);

/* The child's end, where it has ended: its exit code (0 to 255), or 256 plus the number of the
 * signal that ended it; -1 while it runs. The child is reaped then — asked once more it is gone. */
int64_t lyr_process_reap(int64_t pid);

/* The signal numbered `number` to the child (SIGKILL for a kill). 0. */
int64_t lyr_process_signal(int64_t pid, int64_t number);

/* A pipe's end: at most `n` bytes read into `into` — how many, 0 at its end —; at most `n` of
 * `from` written — how many; LYR_NET_WOULD_BLOCK (lyr/net.h) where the call would block; closed. */
int64_t lyr_process_read(int64_t fd, uint8_t *into, int64_t n);
int64_t lyr_process_write(int64_t fd, const uint8_t *from, int64_t n);
int64_t lyr_process_close(int64_t fd);

/* A Slice<uint8> or Slice<int> of emitted code is a pointer and a length (CEmitter). */
#define LYR_PROCESS_SPAWN(program, args, argc, env, envc, cwd, modes, ends) \
    lyr_process_spawn((program), (args).ptr, (argc), (env).ptr, (envc), (cwd), (modes), (ends).ptr, (ends).len)
#define LYR_PROCESS_READ(fd, slice) lyr_process_read((fd), (slice).ptr, (slice).len)
#define LYR_PROCESS_WRITE(fd, slice) lyr_process_write((fd), (slice).ptr, (slice).len)

#endif
