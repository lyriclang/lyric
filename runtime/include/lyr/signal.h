/* Signals as a channel (design/v5/spec/10 Q9, 06 K5). The runtime's handler never runs Lyric code:
 * it marks the signal and wakes the watcher — the thread std.task's delivery runs on — which takes
 * the marks and hands each signal to the channels that asked for it. A runtime started without
 * signal handlers (LyrConfig.install_signal_handlers = 0, an embedding host's) catches none. */
#ifndef LYR_SIGNAL_H
#define LYR_SIGNAL_H

#include <stdint.h>

/* The system's number of an abstract signal — 0 Interrupt, 1 Terminate, 2 Hangup, 3 Quit, 4 User1,
 * 5 User2, 6 WindowChange — and back; -1 where the system has none (Windows knows the first two
 * only). Every other number n of the system is Other(n). */
int64_t lyr_signal_number(int64_t kind);
int64_t lyr_signal_kind(int64_t number);

/* Catches the signal `number` from here on (`on`), or gives it back to the system's default: 1 —
 * 0 where the runtime may not catch it: no handlers allowed, or a signal it never catches (KILL,
 * STOP, the faults, the ones the runtime keeps for itself). */
uint8_t lyr_signal_catch(int64_t number, uint8_t on);

/* The calling thread becomes the watcher: a caught signal wakes its poller. */
void lyr_signal_attach(void);

/* The signals caught since the last call, as a mask — bit n-1 for the signal n — and taken. */
int64_t lyr_signal_take(void);

#endif
