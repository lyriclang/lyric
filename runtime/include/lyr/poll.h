/* Waiting (design/v5/spec/06 N6 S2): a poller per thread, where its scheduler blocks until it is
 * woken — from any thread — or a timeout passes. */
#ifndef LYR_POLL_H
#define LYR_POLL_H

#include <stdint.h>

typedef struct LyrPoller LyrPoller;

/* The calling thread's poller, made at its first call. A poller the system refuses is a panic
 * (RT0015). */
LyrPoller *lyr_poller_current(void);

/* Blocks until the poller is woken or `timeout_ns` passed — 0 asks without blocking, a negative
 * timeout waits without end. 1 when woken, 0 after the timeout, never earlier. A wake that came
 * before the wait is kept: the wait returns at once; the wakes before one wait count as one. A
 * signal that interrupts the wait does not end it. */
int lyr_poller_wait(LyrPoller *poller, int64_t timeout_ns);

/* Wakes the poller's thread from its wait, or from its next one; from any thread. */
void lyr_poller_wake(LyrPoller *poller);

/* Frees the calling thread's poller, at the thread's end: no thread may wake it after. A later
 * lyr_poller_current makes a new one. */
void lyr_poller_release(void);

#ifndef _WIN32
/* A descriptor whose readiness to read wakes the poller as a wake does, and which the poller drains
 * then — the pipe of the signals' watcher (lyr/signal.h). One per poller. */
void lyr_poller_watch(LyrPoller *poller, int fd);
#endif

/* A descriptor's readiness (13 M8b P2, S10b): `fd` armed for reading (LYR_POLL_READ) or writing
 * (LYR_POLL_WRITE), once — the readiness that comes disarms that direction until it is armed
 * again; the other stays as it is. `token` (> 0) is the caller's: a wait that sees the readiness
 * returns 1, as a wake does, and lyr_poller_take gives the token. An error or a hang-up counts as
 * readiness both ways — the next call on the descriptor says what it is. 0, or a failure as
 * lyr/fs.h writes one; Windows answers Unsupported until its poller (S11). Each of these is the
 * poller's own thread's, as its waits are. */
#define LYR_POLL_READ 1
#define LYR_POLL_WRITE 2
int64_t lyr_poller_arm(LyrPoller *poller, int64_t fd, int64_t interest, int64_t token);

/* The descriptor out of the poller, before it is closed: what is armed on it is given as ready —
 * its waiters run again and find it closed. */
void lyr_poller_forget(LyrPoller *poller, int64_t fd);

/* The tokens of the readinesses seen since the last call, at most `n`, into `into`, the oldest
 * first: how many. */
int64_t lyr_poller_take(LyrPoller *poller, int64_t *into, int64_t n);

#endif
