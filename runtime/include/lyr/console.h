/* The console's two buffered streams (design/v5/spec/10 O9; 13 M8b P3): the standard output (1)
 * and the standard error (2), 8 KiB each under a lock of their own. A stream holds a line at a
 * time where it is a terminal or a host's writer takes it (11 W5 H1), a block otherwise — decided
 * at its first put. std.io puts and, where a flush is due, decides where it runs: on the I/O pool
 * where a scheduler runs, on the thread otherwise. Every end of the program flushes both: the
 * runtime's stop, a panic's report, an error that leaves main.
 *
 * The buffers are the runtime's, not the library's: only the runtime sees every end. lyr_print,
 * lyr_println and lyr_write_* (lyr/init.h) stay a host's unbuffered way to the same streams. */
#ifndef LYR_CONSOLE_H
#define LYR_CONSOLE_H

#include <stdint.h>

/* BrokenPipe, as std.io's IoErrorKind counts it from 1 (lyr/fs.h). */
#define LYR_IO_BROKEN_PIPE 13

/* As much of `n` bytes as fits into the stream's buffer: how many | 1 << 32 where a flush is due —
 * the buffer full, or a newline taken in a stream held a line at a time. */
int64_t lyr_console_put(int64_t stream, const uint8_t *from, int64_t n);

/* What the stream holds, written: 0, or a failure as lyr/fs.h writes one — a pipe nobody reads is
 * BrokenPipe. What could not be written is dropped. */
int64_t lyr_console_flush(int64_t stream);

/* Both streams written, failures dropped — at every end of the program. With `waiting` 0 a stream
 * another thread holds is left as it is (a panic must not wait on a write that blocks). */
void lyr_console_flush_all(int waiting);

/* A Slice<uint8> of emitted code is a pointer and a length (CEmitter). */
#define LYR_CONSOLE_PUT(stream, slice) lyr_console_put((stream), (slice).ptr, (slice).len)

#endif
