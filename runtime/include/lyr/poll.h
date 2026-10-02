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

#endif
