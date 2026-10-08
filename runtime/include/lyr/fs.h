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
/* With create: fails where the file is there (O_EXCL, CREATE_NEW) — std.fs's tempFile, M8b S7b. */
#define LYR_FS_EXCLUSIVE_BIT 32

/* The kinds, as std.io's IoErrorKind counts them from 1. */
#define LYR_IO_NOT_FOUND 1
#define LYR_IO_PERMISSION_DENIED 2
#define LYR_IO_ALREADY_EXISTS 3
#define LYR_IO_IS_DIRECTORY 4
#define LYR_IO_NOT_DIRECTORY 5
#define LYR_IO_INVALID_INPUT 6
#define LYR_IO_INVALID_DATA 7

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

/* The directories (M8b S7a). What a path names, for lyr_fs_stat's first slot and a directory
 * entry's kind. */
#define LYR_FS_KIND_FILE 1
#define LYR_FS_KIND_DIRECTORY 2
#define LYR_FS_KIND_LINK 3
#define LYR_FS_KIND_OTHER 4

/* What `path` names into `into` (five slots): its kind (a file, a directory, 0 for neither), its
 * size in bytes, when it was last written (nanoseconds since 1970), 1 where it cannot be written,
 * and 1 where `path` itself is a link. Through links where `follow` is 1 — a link that leads
 * nowhere is not found, as stat(2) says —; of the path itself where it is 0, a link neither file
 * nor directory (lstat(2)). 0. */
int64_t lyr_fs_stat(const LyrStr *path, int64_t follow, int64_t *into, int64_t n);

/* A directory made, an empty one removed, a file removed, a file or directory renamed — over a
 * file `to` names, which it replaces: 0. */
int64_t lyr_fs_mkdir(const LyrStr *path);
int64_t lyr_fs_rmdir(const LyrStr *path);
int64_t lyr_fs_unlink(const LyrStr *path);
int64_t lyr_fs_rename(const LyrStr *from, const LyrStr *to);

/* A directory opened to read its entries: the handle. */
int64_t lyr_fs_dir_open(const LyrStr *path);

/* The next entry's name (UTF-8) into `into`: its length | its kind << 32, 0 at the end; `.` and
 * `..` are not given. A name longer than `n` answers its length and kind without a byte written,
 * and comes again at the next call. */
int64_t lyr_fs_dir_next(int64_t dir, uint8_t *into, int64_t n);

/* The directory let go: 0. */
int64_t lyr_fs_dir_close(int64_t dir);

/* `path` absolute, its links resolved (realpath; GetFinalPathNameByHandleW, without `\\?\`),
 * into `into`: its length. A length beyond `n` says the room it needs; nothing is written then. */
int64_t lyr_fs_canonical(const LyrStr *path, uint8_t *into, int64_t n);

/* `path` absolute by the working directory and nothing else — no link resolved, nothing asked of
 * the file system (GetFullPathNameW) —, into `into` as lyr_fs_canonical. */
int64_t lyr_fs_absolute(const LyrStr *path, uint8_t *into, int64_t n);

/* The system's directory for temporary files — TMPDIR where it is set and not empty, else /tmp;
 * GetTempPathW —, without a separator at its end, into `into` as lyr_fs_canonical (M8b S7b). */
int64_t lyr_fs_temp_root(uint8_t *into, int64_t n);

/* A Slice<uint8> of emitted code, `lyr_slice_u8`, is a pointer and a length (CEmitter); a
 * Slice<int> the same. */
#define LYR_FS_READ(file, slice) lyr_fs_read((file), (slice).ptr, (slice).len)
#define LYR_FS_WRITE(file, slice) lyr_fs_write((file), (slice).ptr, (slice).len)
#define LYR_FS_STAT(path, follow, slice) lyr_fs_stat((path), (follow), (slice).ptr, (slice).len)
#define LYR_FS_TEMP_ROOT(slice) lyr_fs_temp_root((slice).ptr, (slice).len)
#define LYR_FS_DIR_NEXT(dir, slice) lyr_fs_dir_next((dir), (slice).ptr, (slice).len)
#define LYR_FS_CANONICAL(path, slice) lyr_fs_canonical((path), (slice).ptr, (slice).len)
#define LYR_FS_ABSOLUTE(path, slice) lyr_fs_absolute((path), (slice).ptr, (slice).len)

#endif
