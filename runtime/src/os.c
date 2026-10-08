/* The operating system's facts (lyr/os.h). */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif
/* Apple hides _SC_NPROCESSORS_ONLN under a plain _POSIX_C_SOURCE */
#if defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#  define _DARWIN_C_SOURCE
#endif

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  include <shellapi.h>
#else
#  include <errno.h>
#  include <pthread.h>
#  include <unistd.h>
#endif

#include "lyr/console.h"
#include "lyr/fs.h"
#include "lyr/init.h"
#include "lyr/os.h"
#include "internal.h"

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

int64_t lyr_os_cpu_count(void) {
#if defined(_WIN32)
    DWORD n = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
#else
    long n = sysconf(_SC_NPROCESSORS_ONLN);
#endif
    return n > 0 ? (int64_t)n : 1;
}

int64_t lyr_os_platform(void) {
#if defined(_WIN32)
    return 3;
#elif defined(__APPLE__)
    return 2;
#elif defined(__linux__)
    return 1;
#else
    return 0;
#endif
}

int64_t lyr_os_arch(void) {
#if defined(__x86_64__) || defined(_M_X64)
    return 1;
#elif defined(__aarch64__) || defined(_M_ARM64)
    return 2;
#else
    return 0;
#endif
}

int64_t lyr_os_pid(void) {
#if defined(_WIN32)
    return (int64_t)GetCurrentProcessId();
#else
    return (int64_t)getpid();
#endif
}

LYR_NORETURN void lyr_os_exit(int64_t code) {
    lyr_console_flush_all(1);
    exit((int)code);
}

#if defined(_WIN32)
/* ------------------------------------------------------------------ Windows */

static SRWLOCK env_lock = SRWLOCK_INIT;

static int64_t last_failure(void) {
    DWORD error = GetLastError();
    int64_t kind = error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND || error == ERROR_ENVVAR_NOT_FOUND
                 ? LYR_IO_NOT_FOUND
                 : error == ERROR_ACCESS_DENIED ? LYR_IO_PERMISSION_DENIED
                 : error == ERROR_DIRECTORY ? LYR_IO_NOT_DIRECTORY
                 : error == ERROR_INVALID_NAME ? LYR_IO_INVALID_INPUT
                 : 0;
    return failure(kind, (int64_t)error);
}

/* UTF-8 as UTF-16, freed by the caller; NULL with the failure in *failed. */
static wchar_t *wide_of(const char *text, int64_t *failed) {
    int wide = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, NULL, 0);
    if (wide <= 0) {
        *failed = failure(LYR_IO_INVALID_INPUT, (int64_t)GetLastError());
        return NULL;
    }
    wchar_t *w = malloc((size_t)wide * sizeof(wchar_t));
    if (w == NULL) {
        *failed = failure(0, ERROR_NOT_ENOUGH_MEMORY);
        return NULL;
    }
    MultiByteToWideChar(CP_UTF8, 0, text, -1, w, wide);
    return w;
}

/* `len` UTF-16 units as UTF-8 into `into`, where they fit: their length either way. An unpaired
 * surrogate becomes U+FFFD (a name the system holds that is no Unicode is taken, not refused). */
static int64_t give_wide(const wchar_t *w, int len, uint8_t *into, int64_t n) {
    if (len == 0) return 0;
    int bytes = WideCharToMultiByte(CP_UTF8, 0, w, len, NULL, 0, NULL, NULL);
    if (bytes <= 0) return last_failure();
    if (bytes <= n) WideCharToMultiByte(CP_UTF8, 0, w, len, (char *)into, bytes, NULL, NULL);
    return bytes;
}

/* An emitted program's arguments, from its command line as UTF-16, once. */
static wchar_t **wide_args;
static int wide_count = -1;
static INIT_ONCE args_once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK split_command_line(PINIT_ONCE once, PVOID parameter, PVOID *context) {
    (void)once; (void)parameter; (void)context;
    int count = 0;
    wide_args = CommandLineToArgvW(GetCommandLineW(), &count);
    wide_count = wide_args == NULL ? -1 : count;
    return TRUE;
}

/* Whether the runtime's argv is the process's own — an emitted main's, narrow in the C library's
 * code page — rather than a host's (zig's mingw hands main a copy of __argv, so a pointer does not
 * tell). */
static int argv_is_the_process(void) {
    return lyr_process_args();
}

int64_t lyr_os_arg_count(void) {
    if (argv_is_the_process()) {
        InitOnceExecuteOnce(&args_once, split_command_line, NULL, NULL);
        if (wide_count >= 0) return wide_count;
    }
    return lyr_argc();
}

int64_t lyr_os_arg(int64_t i, uint8_t *into, int64_t n) {
    if (argv_is_the_process()) {
        InitOnceExecuteOnce(&args_once, split_command_line, NULL, NULL);
        if (wide_count >= 0) {
            if (i < 0 || i >= wide_count) return failure(LYR_IO_INVALID_INPUT, 0);
            return give_wide(wide_args[i], (int)wcslen(wide_args[i]), into, n);
        }
    }
    if (i < 0 || i >= lyr_argc()) return failure(LYR_IO_INVALID_INPUT, 0);
    const char *arg = lyr_argv()[i];
    return give(arg, strlen(arg), into, n);
}

int64_t lyr_os_env(const LyrStr *name, uint8_t *into, int64_t n) {
    int64_t failed;
    wchar_t *key = wide_of(name->bytes, &failed);
    if (key == NULL) return failed;
    AcquireSRWLockShared(&env_lock);
    DWORD need = GetEnvironmentVariableW(key, NULL, 0);
    int64_t result;
    if (need == 0) {
        result = GetLastError() == ERROR_ENVVAR_NOT_FOUND ? failure(LYR_IO_NOT_FOUND, 0) : 0;
    } else {
        wchar_t *value = malloc((size_t)need * sizeof(wchar_t));
        DWORD len = value == NULL ? 0 : GetEnvironmentVariableW(key, value, need);
        result = value == NULL ? failure(0, ERROR_NOT_ENOUGH_MEMORY)
               : len >= need ? failure(0, 0)
               : give_wide(value, (int)len, into, n);
        free(value);
    }
    ReleaseSRWLockShared(&env_lock);
    free(key);
    return result;
}

int64_t lyr_os_envs(uint8_t *into, int64_t n) {
    AcquireSRWLockShared(&env_lock);
    wchar_t *block = GetEnvironmentStringsW();
    if (block == NULL) {
        int64_t f = last_failure();
        ReleaseSRWLockShared(&env_lock);
        return f;
    }
    /* entries end with a NUL each, the block with one more; `=C:=C:\…` entries are the drives' own
     * working directories, no variables, and stay out */
    int64_t total = 0;
    for (wchar_t *at = block; *at != 0; at += wcslen(at) + 1) {
        if (*at == L'=') continue;
        int len = (int)wcslen(at);
        int64_t bytes = give_wide(at, len, total < n ? into + total : into, total < n ? n - total : 0);
        if (bytes < 0) {
            FreeEnvironmentStringsW(block);
            ReleaseSRWLockShared(&env_lock);
            return bytes;
        }
        total += bytes;
        if (total < n) into[total] = 0;
        total += 1;
    }
    FreeEnvironmentStringsW(block);
    ReleaseSRWLockShared(&env_lock);
    return total;
}

int64_t lyr_os_set_env(const LyrStr *name, const LyrStr *value) {
    int64_t failed;
    wchar_t *key = wide_of(name->bytes, &failed);
    if (key == NULL) return failed;
    wchar_t *text = wide_of(value->bytes, &failed);
    if (text == NULL) {
        free(key);
        return failed;
    }
    AcquireSRWLockExclusive(&env_lock);
    BOOL done = SetEnvironmentVariableW(key, text);
    ReleaseSRWLockExclusive(&env_lock);
    free(key);
    free(text);
    return done ? 0 : last_failure();
}

int64_t lyr_os_cwd(uint8_t *into, int64_t n) {
    DWORD need = GetCurrentDirectoryW(0, NULL);
    if (need == 0) return last_failure();
    wchar_t *dir = malloc((size_t)need * sizeof(wchar_t));
    if (dir == NULL) return failure(0, ERROR_NOT_ENOUGH_MEMORY);
    DWORD len = GetCurrentDirectoryW(need, dir);
    int64_t result = len == 0 || len >= need ? last_failure() : give_wide(dir, (int)len, into, n);
    free(dir);
    return result;
}

int64_t lyr_os_set_cwd(const LyrStr *path) {
    int64_t failed;
    wchar_t *dir = wide_of(path->bytes, &failed);
    if (dir == NULL) return failed;
    BOOL done = SetCurrentDirectoryW(dir);
    free(dir);
    return done ? 0 : last_failure();
}

int64_t lyr_os_hostname(uint8_t *into, int64_t n) {
    DWORD size = 0;
    GetComputerNameExW(ComputerNameDnsHostname, NULL, &size);
    if (size == 0) return last_failure();
    wchar_t *name = malloc((size_t)size * sizeof(wchar_t));
    if (name == NULL) return failure(0, ERROR_NOT_ENOUGH_MEMORY);
    DWORD len = size;
    int64_t result = GetComputerNameExW(ComputerNameDnsHostname, name, &len) ? give_wide(name, (int)len, into, n)
                                                                              : last_failure();
    free(name);
    return result;
}

#else
/* ------------------------------------------------------------------ POSIX */

extern char **environ;
static pthread_rwlock_t env_lock = PTHREAD_RWLOCK_INITIALIZER;

static int64_t last_failure(void) {
    int error = errno;
    int64_t kind = error == ENOENT ? LYR_IO_NOT_FOUND
                 : error == EACCES || error == EPERM ? LYR_IO_PERMISSION_DENIED
                 : error == ENOTDIR ? LYR_IO_NOT_DIRECTORY
                 : error == EINVAL || error == ENAMETOOLONG ? LYR_IO_INVALID_INPUT
                 : 0;
    return failure(kind, error);
}

int64_t lyr_os_arg_count(void) { return lyr_argc(); }

int64_t lyr_os_arg(int64_t i, uint8_t *into, int64_t n) {
    if (i < 0 || i >= lyr_argc()) return failure(LYR_IO_INVALID_INPUT, 0);
    const char *arg = lyr_argv()[i];
    return give(arg, strlen(arg), into, n);
}

int64_t lyr_os_env(const LyrStr *name, uint8_t *into, int64_t n) {
    pthread_rwlock_rdlock(&env_lock);
    const char *value = getenv(name->bytes);
    int64_t result = value == NULL ? failure(LYR_IO_NOT_FOUND, 0) : give(value, strlen(value), into, n);
    pthread_rwlock_unlock(&env_lock);
    return result;
}

int64_t lyr_os_envs(uint8_t *into, int64_t n) {
    pthread_rwlock_rdlock(&env_lock);
    int64_t total = 0;
    for (char **at = environ; at != NULL && *at != NULL; at++) {
        size_t len = strlen(*at);
        if (total + (int64_t)len + 1 <= n) {
            memcpy(into + total, *at, len);
            into[total + (int64_t)len] = 0;
        }
        total += (int64_t)len + 1;
    }
    pthread_rwlock_unlock(&env_lock);
    return total;
}

int64_t lyr_os_set_env(const LyrStr *name, const LyrStr *value) {
    pthread_rwlock_wrlock(&env_lock);
    int r = setenv(name->bytes, value->bytes, 1);
    int64_t result = r < 0 ? last_failure() : 0;
    pthread_rwlock_unlock(&env_lock);
    return result;
}

int64_t lyr_os_cwd(uint8_t *into, int64_t n) {
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
    int64_t len = give(cwd, strlen(cwd), into, n);
    free(cwd);
    return len;
}

int64_t lyr_os_set_cwd(const LyrStr *path) {
    return chdir(path->bytes) < 0 ? last_failure() : 0;
}

int64_t lyr_os_hostname(uint8_t *into, int64_t n) {
    char name[256];
    if (gethostname(name, sizeof name) < 0) return last_failure();
    name[sizeof name - 1] = 0;
    return give(name, strlen(name), into, n);
}
#endif
