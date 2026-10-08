/* The file system's calls (lyr/fs.h): POSIX's open/read/write/lseek/close, Windows' CreateFileW,
 * ReadFile, WriteFile, SetFilePointerEx, CloseHandle; the directories' (M8b S7a) — stat, mkdir,
 * rmdir, unlink, rename, opendir/readdir, realpath, getcwd; their Windows kin. One system call
 * each, or a short sequence where the system needs one; EINTR taken again. */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif
/* A directory entry's d_type: glibc and musl give it under _DEFAULT_SOURCE, Apple under
 * _DARWIN_C_SOURCE (with st_mtimespec, its name for POSIX's st_mtim). */
#if defined(__linux__) && !defined(_DEFAULT_SOURCE)
#  define _DEFAULT_SOURCE
#endif
#if defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#  define _DARWIN_C_SOURCE
#endif

#include "lyr/fs.h"

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

/* `bytes` (`len` of them) into `into`, where they fit: their length either way. */
static int64_t give(const char *bytes, size_t len, uint8_t *into, int64_t n) {
    if ((int64_t)len <= n) memcpy(into, bytes, len);
    return (int64_t)len;
}

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>

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
    case ERROR_NO_UNICODE_TRANSLATION:
        return LYR_IO_INVALID_DATA;
    default:
        return 0;
    }
}

static int64_t last_failure(void) {
    DWORD error = GetLastError();
    return failure(kind_of(error), (int64_t)error);
}

/* `path` as UTF-16 with room for `extra` more characters, freed by the caller; NULL with the
 * failure in `*failed`. */
static wchar_t *wide_of(const char *path, int extra, int64_t *failed) {
    int wide = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, NULL, 0);
    if (wide <= 0) {
        *failed = failure(LYR_IO_INVALID_INPUT, (int64_t)GetLastError());
        return NULL;
    }
    wchar_t *name = malloc(((size_t)wide + (size_t)extra) * sizeof(wchar_t));
    if (name == NULL) {
        *failed = failure(0, ERROR_NOT_ENOUGH_MEMORY);
        return NULL;
    }
    MultiByteToWideChar(CP_UTF8, 0, path, -1, name, wide);
    return name;
}

/* `len` UTF-16 characters of `w` as UTF-8 into `into`, where they fit: their length either way. */
static int64_t give_wide(const wchar_t *w, int len, uint8_t *into, int64_t n) {
    if (len == 0) return 0;
    int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, w, len, NULL, 0, NULL, NULL);
    if (bytes <= 0) return last_failure();
    if (bytes <= n) WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, w, len, (char *)into, bytes, NULL, NULL);
    return bytes;
}

int64_t lyr_fs_open(const LyrStr *path, int64_t how) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;

    DWORD access = 0;
    if (how & LYR_FS_READ_BIT) access |= GENERIC_READ;
    if (how & LYR_FS_APPEND_BIT) access |= FILE_APPEND_DATA;
    else if (how & LYR_FS_WRITE_BIT) access |= GENERIC_WRITE;
    DWORD disposition;
    if ((how & LYR_FS_CREATE_BIT) && (how & LYR_FS_EXCLUSIVE_BIT)) disposition = CREATE_NEW;
    else if (how & LYR_FS_CREATE_BIT) disposition = (how & LYR_FS_TRUNCATE_BIT) ? CREATE_ALWAYS : OPEN_ALWAYS;
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

/* A reparse point that names another path — a symbolic link or a junction — as Go counts it. */
static int is_link(DWORD attributes, DWORD tag) {
    return (attributes & FILE_ATTRIBUTE_REPARSE_POINT) && (tag == IO_REPARSE_TAG_SYMLINK || tag == IO_REPARSE_TAG_MOUNT_POINT);
}

/* A FILETIME (100 ns since 1601) as nanoseconds since 1970. */
static int64_t nanos_of(FILETIME t) {
    int64_t ticks = (int64_t)(((uint64_t)t.dwHighDateTime << 32) | t.dwLowDateTime);
    return (ticks - 116444736000000000LL) * 100;
}

/* `name` opened to ask about it — a directory too —, through links. */
static HANDLE handle_of(const wchar_t *name) {
    return CreateFileW(name, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING,
                       FILE_FLAG_BACKUP_SEMANTICS, NULL);
}

int64_t lyr_fs_stat(const LyrStr *path, int64_t follow, int64_t *into, int64_t n) {
    if (n < 5) return failure(LYR_IO_INVALID_INPUT, 0);
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    WIN32_FIND_DATAW own;
    HANDLE found = FindFirstFileW(name, &own);
    if (found == INVALID_HANDLE_VALUE) {
        /* a root, `C:\`, has no entry to find (and is no link); its attributes come through the
         * handle */
        own.dwFileAttributes = 0;
        own.dwReserved0 = 0;
    } else {
        FindClose(found);
    }
    int link = is_link(own.dwFileAttributes, own.dwReserved0);
    if (!follow && found != INVALID_HANDLE_VALUE) {
        /* the path itself, as its directory's entry says it */
        free(name);
        into[0] = link ? 0 : (own.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ? LYR_FS_KIND_DIRECTORY : LYR_FS_KIND_FILE;
        into[1] = (int64_t)(((uint64_t)own.nFileSizeHigh << 32) | own.nFileSizeLow);
        into[2] = nanos_of(own.ftLastWriteTime);
        into[3] = (own.dwFileAttributes & FILE_ATTRIBUTE_READONLY) != 0;
        into[4] = link;
        return 0;
    }
    HANDLE file = handle_of(name);
    free(name);
    if (file == INVALID_HANDLE_VALUE) return last_failure();
    BY_HANDLE_FILE_INFORMATION info;
    if (!GetFileInformationByHandle(file, &info)) {
        int64_t f = last_failure();
        CloseHandle(file);
        return f;
    }
    CloseHandle(file);
    into[0] = (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ? LYR_FS_KIND_DIRECTORY : LYR_FS_KIND_FILE;
    into[1] = (int64_t)(((uint64_t)info.nFileSizeHigh << 32) | info.nFileSizeLow);
    into[2] = nanos_of(info.ftLastWriteTime);
    into[3] = (info.dwFileAttributes & FILE_ATTRIBUTE_READONLY) != 0;
    into[4] = link;
    return 0;
}

int64_t lyr_fs_mkdir(const LyrStr *path) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    BOOL done = CreateDirectoryW(name, NULL);
    free(name);
    return done ? 0 : last_failure();
}

int64_t lyr_fs_rmdir(const LyrStr *path) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    BOOL done = RemoveDirectoryW(name);
    free(name);
    return done ? 0 : last_failure();
}

int64_t lyr_fs_unlink(const LyrStr *path) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    BOOL done = DeleteFileW(name);
    free(name);
    return done ? 0 : last_failure();
}

int64_t lyr_fs_rename(const LyrStr *from, const LyrStr *to) {
    int64_t failed;
    wchar_t *source = wide_of(from->bytes, 0, &failed);
    if (source == NULL) return failed;
    wchar_t *target = wide_of(to->bytes, 0, &failed);
    if (target == NULL) {
        free(source);
        return failed;
    }
    BOOL done = MoveFileExW(source, target, MOVEFILE_REPLACE_EXISTING);
    free(source);
    free(target);
    return done ? 0 : last_failure();
}

/* A directory read: the search, and the entry it holds that was not given yet. */
typedef struct {
    HANDLE find;
    WIN32_FIND_DATAW data;
    int held;
} Dir;

int64_t lyr_fs_dir_open(const LyrStr *path) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 2, &failed);
    if (name == NULL) return failed;
    size_t len = wcslen(name);
    if (len > 0 && name[len - 1] != L'\\' && name[len - 1] != L'/') name[len++] = L'\\';
    name[len++] = L'*';
    name[len] = 0;
    Dir *dir = malloc(sizeof(Dir));
    if (dir == NULL) {
        free(name);
        return failure(0, ERROR_NOT_ENOUGH_MEMORY);
    }
    dir->find = FindFirstFileW(name, &dir->data);
    free(name);
    if (dir->find == INVALID_HANDLE_VALUE) {
        /* a drive's root has no `.`: empty, it finds nothing */
        if (GetLastError() == ERROR_FILE_NOT_FOUND) {
            dir->held = 0;
            return (int64_t)(intptr_t)dir;
        }
        int64_t f = last_failure();
        free(dir);
        return f;
    }
    dir->held = 1;
    return (int64_t)(intptr_t)dir;
}

int64_t lyr_fs_dir_next(int64_t handle, uint8_t *into, int64_t n) {
    Dir *dir = (Dir *)(intptr_t)handle;
    if (dir->find == INVALID_HANDLE_VALUE) return 0;
    for (;;) {
        if (!dir->held) {
            if (!FindNextFileW(dir->find, &dir->data)) {
                if (GetLastError() == ERROR_NO_MORE_FILES) return 0;
                return last_failure();
            }
            dir->held = 1;
        }
        const wchar_t *w = dir->data.cFileName;
        if (wcscmp(w, L".") == 0 || wcscmp(w, L"..") == 0) {
            dir->held = 0;
            continue;
        }
        int64_t kind = is_link(dir->data.dwFileAttributes, dir->data.dwReserved0) ? LYR_FS_KIND_LINK
                     : (dir->data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ? LYR_FS_KIND_DIRECTORY
                     : LYR_FS_KIND_FILE;
        int64_t len = give_wide(w, (int)wcslen(w), into, n);
        if (len < 0) {
            dir->held = 0;
            return len;
        }
        if (len <= n) dir->held = 0;
        return len | (kind << 32);
    }
}

int64_t lyr_fs_dir_close(int64_t handle) {
    Dir *dir = (Dir *)(intptr_t)handle;
    BOOL done = dir->find == INVALID_HANDLE_VALUE || FindClose(dir->find);
    free(dir);
    return done ? 0 : last_failure();
}

/* A path the system gave, `\\?\C:\…` or `\\?\UNC\host\…`, as one writes it: `C:\…`, `\\host\…`. */
static int64_t give_plain(wchar_t *w, int len, uint8_t *into, int64_t n) {
    if (len >= 8 && wcsncmp(w, L"\\\\?\\UNC\\", 8) == 0) {
        w[6] = L'\\';
        return give_wide(w + 6, len - 6, into, n);
    }
    if (len >= 4 && wcsncmp(w, L"\\\\?\\", 4) == 0) return give_wide(w + 4, len - 4, into, n);
    return give_wide(w, len, into, n);
}

int64_t lyr_fs_canonical(const LyrStr *path, uint8_t *into, int64_t n) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    HANDLE file = handle_of(name);
    free(name);
    if (file == INVALID_HANDLE_VALUE) return last_failure();
    DWORD need = GetFinalPathNameByHandleW(file, NULL, 0, VOLUME_NAME_DOS);
    wchar_t *full = need == 0 ? NULL : malloc((size_t)need * sizeof(wchar_t));
    DWORD len = full == NULL ? 0 : GetFinalPathNameByHandleW(file, full, need, VOLUME_NAME_DOS);
    int64_t result = len == 0 || len >= need ? last_failure() : give_plain(full, (int)len, into, n);
    free(full);
    CloseHandle(file);
    return result;
}

int64_t lyr_fs_absolute(const LyrStr *path, uint8_t *into, int64_t n) {
    int64_t failed;
    wchar_t *name = wide_of(path->bytes, 0, &failed);
    if (name == NULL) return failed;
    DWORD need = GetFullPathNameW(name, 0, NULL, NULL);
    wchar_t *full = need == 0 ? NULL : malloc((size_t)need * sizeof(wchar_t));
    DWORD len = full == NULL ? 0 : GetFullPathNameW(name, need, full, NULL);
    int64_t result = len == 0 || len >= need ? last_failure() : give_plain(full, (int)len, into, n);
    free(full);
    free(name);
    return result;
}

int64_t lyr_fs_temp_root(uint8_t *into, int64_t n) {
    wchar_t dir[MAX_PATH + 1];
    DWORD len = GetTempPathW(MAX_PATH + 1, dir);
    if (len == 0 || len > MAX_PATH) return last_failure();
    /* its separator at the end goes, a drive's own (`C:\`) stays */
    if (len > 3 && dir[len - 1] == L'\\') len--;
    return give_wide(dir, (int)len, into, n);
}

#else
#  include <dirent.h>
#  include <errno.h>
#  include <fcntl.h>
#  include <stdio.h>
#  include <sys/stat.h>
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
    if ((how & LYR_FS_CREATE_BIT) && (how & LYR_FS_EXCLUSIVE_BIT)) flags |= O_EXCL;
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

#  if defined(__APPLE__)
#    define LYR_MTIME(st) ((int64_t)(st).st_mtimespec.tv_sec * 1000000000 + (st).st_mtimespec.tv_nsec)
#  else
#    define LYR_MTIME(st) ((int64_t)(st).st_mtim.tv_sec * 1000000000 + (st).st_mtim.tv_nsec)
#  endif

int64_t lyr_fs_stat(const LyrStr *path, int64_t follow, int64_t *into, int64_t n) {
    if (n < 5) return failure(LYR_IO_INVALID_INPUT, 0);
    struct stat own, st;
    int r;
    do {
        r = lstat(path->bytes, &own);
    } while (r < 0 && errno == EINTR);
    if (r < 0) return last_failure();
    int link = S_ISLNK(own.st_mode);
    if (link && follow) {
        do {
            r = stat(path->bytes, &st);
        } while (r < 0 && errno == EINTR);
        if (r < 0) return last_failure();
    } else {
        st = own;
    }
    into[0] = S_ISREG(st.st_mode) ? LYR_FS_KIND_FILE : S_ISDIR(st.st_mode) ? LYR_FS_KIND_DIRECTORY : 0;
    into[1] = (int64_t)st.st_size;
    into[2] = LYR_MTIME(st);
    into[3] = (st.st_mode & 0222) == 0;
    into[4] = link;
    return 0;
}

int64_t lyr_fs_mkdir(const LyrStr *path) {
    int r;
    do {
        r = mkdir(path->bytes, 0777);
    } while (r < 0 && errno == EINTR);
    return r < 0 ? last_failure() : 0;
}

int64_t lyr_fs_rmdir(const LyrStr *path) {
    int r;
    do {
        r = rmdir(path->bytes);
    } while (r < 0 && errno == EINTR);
    return r < 0 ? last_failure() : 0;
}

int64_t lyr_fs_unlink(const LyrStr *path) {
    int r;
    do {
        r = unlink(path->bytes);
    } while (r < 0 && errno == EINTR);
    return r < 0 ? last_failure() : 0;
}

int64_t lyr_fs_rename(const LyrStr *from, const LyrStr *to) {
    int r;
    do {
        r = rename(from->bytes, to->bytes);
    } while (r < 0 && errno == EINTR);
    return r < 0 ? last_failure() : 0;
}

/* A directory read: the stream, and the entry it holds that was not given yet. */
typedef struct {
    DIR *dir;
    struct dirent *held;
} Dir;

int64_t lyr_fs_dir_open(const LyrStr *path) {
    DIR *stream;
    do {
        stream = opendir(path->bytes);
    } while (stream == NULL && errno == EINTR);
    if (stream == NULL) return last_failure();
    Dir *dir = malloc(sizeof(Dir));
    if (dir == NULL) {
        closedir(stream);
        return failure(0, ENOMEM);
    }
    dir->dir = stream;
    dir->held = NULL;
    return (int64_t)(intptr_t)dir;
}

/* An entry's kind from its d_type; one the file system does not say, asked of it. */
static int64_t entry_kind(Dir *dir, const struct dirent *entry) {
    switch (entry->d_type) {
    case DT_REG:
        return LYR_FS_KIND_FILE;
    case DT_DIR:
        return LYR_FS_KIND_DIRECTORY;
    case DT_LNK:
        return LYR_FS_KIND_LINK;
    case DT_UNKNOWN: {
        struct stat st;
        if (fstatat(dirfd(dir->dir), entry->d_name, &st, AT_SYMLINK_NOFOLLOW) < 0) return LYR_FS_KIND_OTHER;
        return S_ISREG(st.st_mode) ? LYR_FS_KIND_FILE
             : S_ISDIR(st.st_mode) ? LYR_FS_KIND_DIRECTORY
             : S_ISLNK(st.st_mode) ? LYR_FS_KIND_LINK
             : LYR_FS_KIND_OTHER;
    }
    default:
        return LYR_FS_KIND_OTHER;
    }
}

int64_t lyr_fs_dir_next(int64_t handle, uint8_t *into, int64_t n) {
    Dir *dir = (Dir *)(intptr_t)handle;
    for (;;) {
        if (dir->held == NULL) {
            errno = 0;
            dir->held = readdir(dir->dir);
            if (dir->held == NULL) return errno != 0 ? last_failure() : 0;
        }
        const char *name = dir->held->d_name;
        if (strcmp(name, ".") == 0 || strcmp(name, "..") == 0) {
            dir->held = NULL;
            continue;
        }
        int64_t kind = entry_kind(dir, dir->held);
        int64_t len = give(name, strlen(name), into, n);
        if (len <= n) dir->held = NULL;
        return len | (kind << 32);
    }
}

int64_t lyr_fs_dir_close(int64_t handle) {
    Dir *dir = (Dir *)(intptr_t)handle;
    int r = closedir(dir->dir);
    free(dir);
    return r < 0 ? last_failure() : 0;
}

int64_t lyr_fs_canonical(const LyrStr *path, uint8_t *into, int64_t n) {
    char *full = realpath(path->bytes, NULL);
    if (full == NULL) return last_failure();
    int64_t len = give(full, strlen(full), into, n);
    free(full);
    return len;
}

int64_t lyr_fs_absolute(const LyrStr *path, uint8_t *into, int64_t n) {
    if (path->bytes[0] == '/') return give(path->bytes, strlen(path->bytes), into, n);
    size_t room = 256;
    char *cwd = NULL;
    for (;;) {
        char *more = realloc(cwd, room);
        if (more == NULL) {
            free(cwd);
            return failure(0, ENOMEM);
        }
        cwd = more;
        if (getcwd(cwd, room) != NULL) break;
        if (errno != ERANGE) {
            int64_t f = last_failure();
            free(cwd);
            return f;
        }
        room *= 2;
    }
    size_t base = strlen(cwd), rest = strlen(path->bytes);
    int64_t len = (int64_t)(base + (rest > 0 ? 1 + rest : 0));
    if (len <= n) {
        memcpy(into, cwd, base);
        if (rest > 0) {
            into[base] = '/';
            memcpy(into + base + 1, path->bytes, rest);
        }
    }
    free(cwd);
    return len;
}

int64_t lyr_fs_temp_root(uint8_t *into, int64_t n) {
    /* getenv beside a setenv on another thread is a race; std.os's setEnv (M8b S9) says so */
    const char *dir = getenv("TMPDIR");
    if (dir == NULL || dir[0] == 0) dir = "/tmp";
    size_t len = strlen(dir);
    while (len > 1 && dir[len - 1] == '/') len--;
    return give(dir, len, into, n);
}
#endif
