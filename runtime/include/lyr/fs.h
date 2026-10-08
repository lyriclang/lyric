/* The file system's calls (design/v5/spec/13 M8b P1): one system call each, EINTR taken again, a
 * file a handle — a POSIX descriptor, a Windows HANDLE — in an int64_t. A failure is a negative
 * value, -(kind << 32 | raw): `kind` counts std.io's IoErrorKind from 1 (NotFound) — 0 for one it
 * has no name for, `Other { code: raw }` —, `raw` is the system's number (errno, GetLastError).
 * std.fs calls them from its I/O pool (P2), never from a task's thread. */
#ifndef LYR_FS_H
#define LYR_FS_H

#include <stdint.h>

#include "lyr/types.h"

/* How to open (std.fs's OpenOptions): bits. */
#define LYR_FS_READ_BIT 1
#define LYR_FS_WRITE_BIT 2
#define LYR_FS_APPEND_BIT 4
#define LYR_FS_CREATE_BIT 8
#define LYR_FS_TRUNCATE_BIT 16

/* The kinds, as std.io's IoErrorKind counts them from 1. */
#define LYR_IO_NOT_FOUND 1
#define LYR_IO_PERMISSION_DENIED 2
#define LYR_IO_ALREADY_EXISTS 3
#define LYR_IO_IS_DIRECTORY 4
#define LYR_IO_NOT_DIRECTORY 5
#define LYR_IO_INVALID_INPUT 6

/* `path` (UTF-8, NUL-terminated; Windows takes it as UTF-16) opened as `how` says: the handle. */
int64_t lyr_fs_open(const LyrStr *path, int64_t how);

/* At most `n` bytes from the file's position into `into`: how many, 0 at the end. */
int64_t lyr_fs_read(int64_t file, uint8_t *into, int64_t n);

/* At most `n` bytes of `from` at the file's position: how many. */
int64_t lyr_fs_write(int64_t file, const uint8_t *from, int64_t n);

/* The position moved to `offset` from the start (0), from where it stands (1), from the end (2):
 * the new one, counted from the start. */
int64_t lyr_fs_seek(int64_t file, int64_t offset, int64_t whence);

/* The file closed: 0. */
int64_t lyr_fs_close(int64_t file);

/* A Slice<uint8> of emitted code, `lyr_slice_u8`, is a pointer and a length (CEmitter). */
#define LYR_FS_READ(file, slice) lyr_fs_read((file), (slice).ptr, (slice).len)
#define LYR_FS_WRITE(file, slice) lyr_fs_write((file), (slice).ptr, (slice).len)

#endif
