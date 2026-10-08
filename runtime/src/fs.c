/* The file system's calls (lyr/fs.h): POSIX's open/read/write/lseek/close, Windows' CreateFileW,
 * ReadFile, WriteFile, SetFilePointerEx, CloseHandle. One system call each; EINTR taken again. */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/fs.h"

#include <stdint.h>

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  include <stdlib.h>

static int64_t kind_of(DWORD error) {
    switch (error) {
    case ERROR_FILE_NOT_FOUND:
    case ERROR_PATH_NOT_FOUND:
    case ERROR_INVALID_DRIVE:
        return LYR_IO_NOT_FOUND;
    case ERROR_ACCESS_DENIED:
    case ERROR_SHARING_VIOLATION:
        return LYR_IO_PERMISSION_DENIED;
    case ERROR_FILE_EXISTS:
    case ERROR_ALREADY_EXISTS:
        return LYR_IO_ALREADY_EXISTS;
    case ERROR_DIRECTORY:
        return LYR_IO_NOT_DIRECTORY;
    case ERROR_INVALID_NAME:
    case ERROR_INVALID_PARAMETER:
    case ERROR_BAD_PATHNAME:
        return LYR_IO_INVALID_INPUT;
    default:
        return 0;
    }
}

static int64_t last_failure(void) {
    DWORD error = GetLastError();
    return failure(kind_of(error), (int64_t)error);
}

int64_t lyr_fs_open(const LyrStr *path, int64_t how) {
    int wide = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path->bytes, -1, NULL, 0);
    if (wide <= 0) return failure(LYR_IO_INVALID_INPUT, (int64_t)GetLastError());
    wchar_t *name = malloc((size_t)wide * sizeof(wchar_t));
    if (name == NULL) return failure(0, ERROR_NOT_ENOUGH_MEMORY);
    MultiByteToWideChar(CP_UTF8, 0, path->bytes, -1, name, wide);

    DWORD access = 0;
    if (how & LYR_FS_READ_BIT) access |= GENERIC_READ;
    if (how & LYR_FS_APPEND_BIT) access |= FILE_APPEND_DATA;
    else if (how & LYR_FS_WRITE_BIT) access |= GENERIC_WRITE;
    DWORD disposition;
    if (how & LYR_FS_CREATE_BIT) disposition = (how & LYR_FS_TRUNCATE_BIT) ? CREATE_ALWAYS : OPEN_ALWAYS;
    else disposition = (how & LYR_FS_TRUNCATE_BIT) ? TRUNCATE_EXISTING : OPEN_EXISTING;
    HANDLE file = CreateFileW(name, access, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
                              disposition, FILE_ATTRIBUTE_NORMAL, NULL);
    free(name);
    if (file == INVALID_HANDLE_VALUE) return last_failure();
    return (int64_t)(intptr_t)file;
}

int64_t lyr_fs_read(int64_t file, uint8_t *into, int64_t n) {
    DWORD want = n > 0x40000000 ? 0x40000000 : (DWORD)n;
    DWORD got = 0;
    if (!ReadFile((HANDLE)(intptr_t)file, into, want, &got, NULL)) {
        if (GetLastError() == ERROR_BROKEN_PIPE) return 0;
        return last_failure();
    }
    return (int64_t)got;
}

int64_t lyr_fs_write(int64_t file, const uint8_t *from, int64_t n) {
    DWORD want = n > 0x40000000 ? 0x40000000 : (DWORD)n;
    DWORD put = 0;
    if (!WriteFile((HANDLE)(intptr_t)file, from, want, &put, NULL)) return last_failure();
    return (int64_t)put;
}

int64_t lyr_fs_seek(int64_t file, int64_t offset, int64_t whence) {
    LARGE_INTEGER to, at;
    to.QuadPart = offset;
    DWORD method = whence == 0 ? FILE_BEGIN : whence == 1 ? FILE_CURRENT : FILE_END;
    if (!SetFilePointerEx((HANDLE)(intptr_t)file, to, &at, method)) return last_failure();
    return at.QuadPart;
}

int64_t lyr_fs_close(int64_t file) {
    if (!CloseHandle((HANDLE)(intptr_t)file)) return last_failure();
    return 0;
}

#else
#  include <errno.h>
#  include <fcntl.h>
#  include <sys/types.h>
#  include <unistd.h>

static int64_t kind_of(int error) {
    switch (error) {
    case ENOENT:
        return LYR_IO_NOT_FOUND;
    case EACCES:
    case EPERM:
        return LYR_IO_PERMISSION_DENIED;
    case EEXIST:
        return LYR_IO_ALREADY_EXISTS;
    case EISDIR:
        return LYR_IO_IS_DIRECTORY;
    case ENOTDIR:
        return LYR_IO_NOT_DIRECTORY;
    case EINVAL:
    case ENAMETOOLONG:
        return LYR_IO_INVALID_INPUT;
    default:
        return 0;
    }
}

static int64_t last_failure(void) {
    return failure(kind_of(errno), errno);
}

int64_t lyr_fs_open(const LyrStr *path, int64_t how) {
    int read = (how & LYR_FS_READ_BIT) != 0;
    int write = (how & (LYR_FS_WRITE_BIT | LYR_FS_APPEND_BIT)) != 0;
    int flags = read && write ? O_RDWR : write ? O_WRONLY : O_RDONLY;
    if (how & LYR_FS_APPEND_BIT) flags |= O_APPEND;
    if (how & LYR_FS_CREATE_BIT) flags |= O_CREAT;
    if (how & LYR_FS_TRUNCATE_BIT) flags |= O_TRUNC;
    flags |= O_CLOEXEC;
    int fd;
    do {
        fd = open(path->bytes, flags, 0666);
    } while (fd < 0 && errno == EINTR);
    if (fd < 0) return last_failure();
    return fd;
}

int64_t lyr_fs_read(int64_t file, uint8_t *into, int64_t n) {
    ssize_t got;
    do {
        got = read((int)file, into, (size_t)n);
    } while (got < 0 && errno == EINTR);
    if (got < 0) return last_failure();
    return (int64_t)got;
}

int64_t lyr_fs_write(int64_t file, const uint8_t *from, int64_t n) {
    ssize_t put;
    do {
        put = write((int)file, from, (size_t)n);
    } while (put < 0 && errno == EINTR);
    if (put < 0) return last_failure();
    return (int64_t)put;
}

int64_t lyr_fs_seek(int64_t file, int64_t offset, int64_t whence) {
    off_t at = lseek((int)file, (off_t)offset, whence == 0 ? SEEK_SET : whence == 1 ? SEEK_CUR : SEEK_END);
    if (at < 0) return last_failure();
    return (int64_t)at;
}

int64_t lyr_fs_close(int64_t file) {
    /* close is not taken again on EINTR: the descriptor is gone either way (POSIX 2024). */
    if (close((int)file) < 0 && errno != EINTR) return last_failure();
    return 0;
}
#endif
