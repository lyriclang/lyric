/* Processes (lyr/process.h): fork and exec on POSIX, the reaper's SIGCHLD pipe, a pipe's ends;
 * CreateProcessW on Windows, a registered wait for a child's end (S12b). */
#if defined(__linux__)
#  define _GNU_SOURCE  /* pipe2 */
#elif defined(__APPLE__)
#  define _DARWIN_C_SOURCE
#elif !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/process.h"
#include "lyr/console.h"
#include "lyr/fs.h"
#include "lyr/net.h"
#include "lyr/panic.h"
#include "lyr/poll.h"

#include <stdlib.h>
#include <string.h>

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  include <stdatomic.h>

/* What GetLastError says, as a failure: the kinds a start or a pipe can give by their names. */
static int64_t failed_with(DWORD e) {
    switch (e) {
    case ERROR_FILE_NOT_FOUND:
    case ERROR_PATH_NOT_FOUND:
    case ERROR_DIRECTORY:
    case ERROR_INVALID_NAME:
        return failure(LYR_IO_NOT_FOUND, (int64_t)e);
    case ERROR_ACCESS_DENIED: return failure(LYR_IO_PERMISSION_DENIED, (int64_t)e);
    case ERROR_BROKEN_PIPE:
    case ERROR_NO_DATA: return failure(LYR_IO_BROKEN_PIPE, (int64_t)e);
    case ERROR_INVALID_PARAMETER: return failure(LYR_IO_INVALID_INPUT, (int64_t)e);
    default: return failure(0, (int64_t)e);
    }
}

/* `n` bytes of UTF-8 as a NUL-ended UTF-16 string, allocated; NULL where they are no UTF-8. */
static wchar_t *wide_of(const char *bytes, int64_t n) {
    int units = n == 0 ? 0 : MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes, (int)n, NULL, 0);
    if (n > 0 && units <= 0) return NULL;
    wchar_t *wide = calloc((size_t)units + 1, sizeof *wide);
    if (wide == NULL) return NULL;
    if (units > 0) MultiByteToWideChar(CP_UTF8, 0, bytes, (int)n, wide, units);
    return wide;
}

/* A growing buffer of UTF-8: the command line, before it becomes UTF-16. */
typedef struct {
    char *bytes;
    size_t used, room;
} Text;

static int put(Text *t, const char *bytes, size_t n) {
    if (t->used + n + 1 > t->room) {
        size_t room = t->room == 0 ? 256 : t->room;
        while (room < t->used + n + 1) room *= 2;
        char *grown = realloc(t->bytes, room);
        if (grown == NULL) return 0;
        t->bytes = grown;
        t->room = room;
    }
    memcpy(t->bytes + t->used, bytes, n);
    t->used += n;
    t->bytes[t->used] = '\0';
    return 1;
}

static int put_char(Text *t, char c, size_t times) {
    for (size_t i = 0; i < times; i++) {
        if (!put(t, &c, 1)) return 0;
    }
    return 1;
}

/* One argument as the C runtime's command-line parser reads it back (CommandLineToArgvW's rules):
 * plain where it holds nothing to quote; else in quotes, a run of backslashes doubled before a
 * quote and before the closing one, a quote after a backslash. */
static int quoted(Text *t, const char *arg) {
    if (*arg != '\0' && strpbrk(arg, " \t\n\v\"") == NULL) return put(t, arg, strlen(arg));
    if (!put_char(t, '"', 1)) return 0;
    size_t slashes = 0;
    for (const char *at = arg;; at++) {
        if (*at == '\\') {
            slashes++;
        } else if (*at == '"') {
            if (!put_char(t, '\\', 2 * slashes + 1) || !put_char(t, '"', 1)) return 0;
            slashes = 0;
        } else if (*at == '\0') {
            if (!put_char(t, '\\', 2 * slashes)) return 0;
            break;
        } else {
            if (!put_char(t, '\\', slashes) || !put_char(t, *at, 1)) return 0;
            slashes = 0;
        }
    }
    return put_char(t, '"', 1);
}

/* Whether the environment entry `entry` (NAME=value, UTF-16) is one `set` sets — its name the same
 * but for case, as Windows' names are. A name may begin with '=' (the drives' directories). */
static int overridden(const wchar_t *entry, wchar_t **set, int64_t count) {
    const wchar_t *equals = wcschr(entry + 1, L'=');
    size_t name = equals != NULL ? (size_t)(equals - entry) : wcslen(entry);
    for (int64_t j = 0; j < count; j++) {
        const wchar_t *other = wcschr(set[j] + 1, L'=');
        size_t length = other != NULL ? (size_t)(other - set[j]) : wcslen(set[j]);
        if (length == name && CompareStringOrdinal(entry, (int)name, set[j], (int)name, TRUE) == CSTR_EQUAL) return 1;
    }
    return 0;
}

/* The parent's environment with `set` over it, as CreateProcessW's UTF-16 block: each entry ended
 * by a NUL, the block by another. */
static wchar_t *environment_of(wchar_t **set, int64_t count) {
    wchar_t *current = GetEnvironmentStringsW();
    if (current == NULL) return NULL;
    size_t units = 1;
    for (const wchar_t *at = current; *at; at += wcslen(at) + 1) {
        if (!overridden(at, set, count)) units += wcslen(at) + 1;
    }
    for (int64_t j = 0; j < count; j++) units += wcslen(set[j]) + 1;
    wchar_t *block = calloc(units, sizeof *block);
    if (block != NULL) {
        wchar_t *into = block;
        for (const wchar_t *at = current; *at; at += wcslen(at) + 1) {
            if (overridden(at, set, count)) continue;
            size_t n = wcslen(at) + 1;
            memcpy(into, at, n * sizeof *into);
            into += n;
        }
        for (int64_t j = 0; j < count; j++) {
            size_t n = wcslen(set[j]) + 1;
            memcpy(into, set[j], n * sizeof *into);
            into += n;
        }
        *into = L'\0';
    }
    FreeEnvironmentStringsW(current);
    return block;
}

/* The reaper's poller, which a child's end wakes (lyr_process_attach), and the children's waits:
 * each registered wait with its process, unregistered when the child is reaped. */
static _Atomic(LyrPoller *) reaper;

typedef struct Registered {
    HANDLE process, wait;
    struct Registered *next;
} Registered;

static Registered *registered;
static SRWLOCK registered_lock = SRWLOCK_INIT;

static VOID CALLBACK on_end(PVOID context, BOOLEAN timed_out) {
    (void)context; (void)timed_out;
    LyrPoller *poller = atomic_load(&reaper);
    if (poller != NULL) lyr_poller_wake(poller);
}

void lyr_process_attach(void) {
    atomic_store(&reaper, lyr_poller_current());
}

/* Before the reaper attaches there is nothing to wake: its first look comes after. */
void lyr_process_nudge(void) {
    LyrPoller *poller = atomic_load(&reaper);
    if (poller != NULL) lyr_poller_wake(poller);
}

/* A handle the child inherits for one of its streams — the parent's own stream for Inherit, a
 * pipe's end for Piped (the parent's end into `parent`), NUL for Null. NULL, with the error set,
 * where the system refuses one. */
static HANDLE stream_for(int index, int mode, HANDLE *parent) {
    SECURITY_ATTRIBUTES inherit = { sizeof inherit, NULL, TRUE };
    if (mode == LYR_PROCESS_PIPED) {
        HANDLE read, write;
        if (!CreatePipe(&read, &write, NULL, 0)) return NULL;
        HANDLE child = index == 0 ? read : write;
        *parent = index == 0 ? write : read;
        if (!SetHandleInformation(child, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT)) {
            DWORD e = GetLastError();
            CloseHandle(read);
            CloseHandle(write);
            *parent = NULL;
            SetLastError(e);
            return NULL;
        }
        return child;
    }
    if (mode == LYR_PROCESS_NULL) {
        HANDLE nul = CreateFileW(L"NUL", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, &inherit, OPEN_EXISTING, 0, NULL);
        return nul == INVALID_HANDLE_VALUE ? NULL : nul;
    }
    /* the parent's own, a copy the child may inherit; where there is none, NUL */
    HANDLE own = GetStdHandle(index == 0 ? STD_INPUT_HANDLE : index == 1 ? STD_OUTPUT_HANDLE : STD_ERROR_HANDLE);
    if (own == NULL || own == INVALID_HANDLE_VALUE) return stream_for(index, LYR_PROCESS_NULL, parent);
    HANDLE copy;
    if (!DuplicateHandle(GetCurrentProcess(), own, GetCurrentProcess(), &copy, 0, TRUE, DUPLICATE_SAME_ACCESS)) {
        return stream_for(index, LYR_PROCESS_NULL, parent);
    }
    return copy;
}

int64_t lyr_process_spawn(const LyrStr *program, const uint8_t *args, int64_t argc, const uint8_t *env, int64_t envc,
                          const LyrStr *cwd, int64_t modes, int64_t *ends, int64_t n) {
    (void)program;  /* the first argument is the program's name, and CreateProcessW looks it up */
    if (n < 3 || argc < 1) return failure(LYR_IO_INVALID_INPUT, 0);
    for (int i = 0; i < 3; i++) ends[i] = -1;
    int64_t answer = 0;
    Text line = { NULL, 0, 0 };
    wchar_t *command = NULL, *directory = NULL, *block = NULL;
    wchar_t **set = NULL;
    HANDLE streams[3] = { NULL, NULL, NULL }, parents[3] = { NULL, NULL, NULL };
    LPPROC_THREAD_ATTRIBUTE_LIST attributes = NULL;
    const char *at = (const char *)args;
    for (int64_t i = 0; i < argc; i++) {
        if ((i > 0 && !put_char(&line, ' ', 1)) || !quoted(&line, at)) {
            answer = failure(0, ERROR_NOT_ENOUGH_MEMORY);
            goto done;
        }
        at += strlen(at) + 1;
    }
    command = wide_of(line.bytes, (int64_t)line.used);
    if (command == NULL) {
        answer = failure(LYR_IO_INVALID_INPUT, 0);
        goto done;
    }
    if (cwd->len > 0 && (directory = wide_of(cwd->bytes, cwd->len)) == NULL) {
        answer = failure(LYR_IO_INVALID_INPUT, 0);
        goto done;
    }
    if (envc > 0) {
        set = calloc((size_t)envc, sizeof *set);
        if (set == NULL) {
            answer = failure(0, ERROR_NOT_ENOUGH_MEMORY);
            goto done;
        }
        const char *entry = (const char *)env;
        for (int64_t j = 0; j < envc; j++) {
            if ((set[j] = wide_of(entry, (int64_t)strlen(entry))) == NULL) {
                answer = failure(LYR_IO_INVALID_INPUT, 0);
                goto done;
            }
            entry += strlen(entry) + 1;
        }
        if ((block = environment_of(set, envc)) == NULL) {
            answer = failure(0, ERROR_NOT_ENOUGH_MEMORY);
            goto done;
        }
    }
    for (int i = 0; i < 3; i++) {
        streams[i] = stream_for(i, (int)((modes >> (2 * i)) & 3), &parents[i]);
        if (streams[i] == NULL) {
            answer = failed_with(GetLastError());
            goto done;
        }
    }
    /* the child inherits its three handles and nothing else the parent let be inherited */
    SIZE_T size = 0;
    InitializeProcThreadAttributeList(NULL, 1, 0, &size);
    attributes = malloc(size);
    if (attributes == NULL || !InitializeProcThreadAttributeList(attributes, 1, 0, &size)
        || !UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, streams, sizeof streams, NULL, NULL)) {
        answer = failed_with(GetLastError());
        goto done;
    }
    STARTUPINFOEXW start;
    memset(&start, 0, sizeof start);
    start.StartupInfo.cb = sizeof start;
    start.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    start.StartupInfo.hStdInput = streams[0];
    start.StartupInfo.hStdOutput = streams[1];
    start.StartupInfo.hStdError = streams[2];
    start.lpAttributeList = attributes;
    PROCESS_INFORMATION started;
    if (!CreateProcessW(NULL, command, NULL, NULL, TRUE, EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                        block, directory, &start.StartupInfo, &started)) {
        answer = failed_with(GetLastError());
        goto done;
    }
    CloseHandle(started.hThread);
    Registered *entry = malloc(sizeof *entry);
    if (entry == NULL || !RegisterWaitForSingleObject(&entry->wait, started.hProcess, on_end, NULL, INFINITE, WT_EXECUTEONLYONCE)) {
        /* no wait: the child runs, but its end would wake nobody — it is ended, and the start fails */
        DWORD e = entry == NULL ? ERROR_NOT_ENOUGH_MEMORY : GetLastError();
        free(entry);
        TerminateProcess(started.hProcess, 1);
        CloseHandle(started.hProcess);
        answer = failed_with(e);
        goto done;
    }
    entry->process = started.hProcess;
    AcquireSRWLockExclusive(&registered_lock);
    entry->next = registered;
    registered = entry;
    ReleaseSRWLockExclusive(&registered_lock);
    for (int i = 0; i < 3; i++) {
        ends[i] = parents[i] != NULL ? (int64_t)(intptr_t)parents[i] : -1;
        parents[i] = NULL;
    }
    answer = (int64_t)(intptr_t)started.hProcess;
done:
    for (int i = 0; i < 3; i++) {
        if (streams[i] != NULL) CloseHandle(streams[i]);
        if (parents[i] != NULL) CloseHandle(parents[i]);
    }
    if (attributes != NULL) {
        DeleteProcThreadAttributeList(attributes);
        free(attributes);
    }
    if (set != NULL) {
        for (int64_t j = 0; j < envc; j++) free(set[j]);
        free(set);
    }
    free(line.bytes);
    free(command);
    free(directory);
    free(block);
    return answer;
}

int64_t lyr_process_reap(int64_t pid) {
    HANDLE process = (HANDLE)(intptr_t)pid;
    DWORD state = WaitForSingleObject(process, 0);
    if (state == WAIT_TIMEOUT) return -1;
    if (state != WAIT_OBJECT_0) return failed_with(GetLastError());
    DWORD code = 0;
    if (!GetExitCodeProcess(process, &code)) return failed_with(GetLastError());
    /* its wait out, the handle closed: the child is reaped */
    AcquireSRWLockExclusive(&registered_lock);
    for (Registered **at = &registered; *at != NULL; at = &(*at)->next) {
        if ((*at)->process == process) {
            Registered *gone = *at;
            *at = gone->next;
            UnregisterWaitEx(gone->wait, NULL);
            free(gone);
            break;
        }
    }
    ReleaseSRWLockExclusive(&registered_lock);
    CloseHandle(process);
    return (int64_t)code;
}

int64_t lyr_process_signal(int64_t pid, int64_t number) {
    /* no signals on Windows: a kill — SIGKILL's number — ends the child, anything else is refused */
    if (number != 9) return failure(LYR_IO_UNSUPPORTED, 0);
    if (!TerminateProcess((HANDLE)(intptr_t)pid, 1)) return failed_with(GetLastError());
    return 0;
}

uint8_t lyr_process_pipes_block(void) { return 1; }

int64_t lyr_process_read(int64_t fd, uint8_t *into, int64_t n) {
    DWORD want = n > 0x40000000 ? 0x40000000 : (DWORD)n, got = 0;
    if (!ReadFile((HANDLE)(intptr_t)fd, into, want, &got, NULL)) {
        DWORD e = GetLastError();
        if (e == ERROR_BROKEN_PIPE) return 0;  /* the child closed its end: the pipe's end */
        return failed_with(e);
    }
    return (int64_t)got;
}

int64_t lyr_process_write(int64_t fd, const uint8_t *from, int64_t n) {
    DWORD want = n > 0x40000000 ? 0x40000000 : (DWORD)n, sent = 0;
    if (!WriteFile((HANDLE)(intptr_t)fd, from, want, &sent, NULL)) return failed_with(GetLastError());
    return (int64_t)sent;
}

int64_t lyr_process_close(int64_t fd) {
    if (!CloseHandle((HANDLE)(intptr_t)fd)) return failed_with(GetLastError());
    return 0;
}

#else
#  include <errno.h>
#  include <fcntl.h>
#  include <pthread.h>
#  include <signal.h>
#  include <sys/stat.h>
#  include <sys/types.h>
#  include <sys/wait.h>
#  include <unistd.h>

extern char **environ;

/* What an errno says, as a failure: the kinds a start can give by their names (10 O3). */
static int64_t failed_with(int e) {
    switch (e) {
    case EAGAIN:
#  if EWOULDBLOCK != EAGAIN
    case EWOULDBLOCK:
#  endif
        return LYR_NET_WOULD_BLOCK;
    case ENOENT: return failure(LYR_IO_NOT_FOUND, e);
    case EACCES:
    case EPERM: return failure(LYR_IO_PERMISSION_DENIED, e);
    case ENOTDIR: return failure(LYR_IO_NOT_DIRECTORY, e);
    case EISDIR: return failure(LYR_IO_IS_DIRECTORY, e);
    case EINVAL: return failure(LYR_IO_INVALID_INPUT, e);
    case EPIPE: return failure(LYR_IO_BROKEN_PIPE, e);
    default: return failure(0, e);
    }
}

/* A pipe, both ends closed on exec. */
static int pipe_of(int ends[2]) {
#  ifdef __linux__
    return pipe2(ends, O_CLOEXEC);
#  else
    if (pipe(ends) != 0) return -1;
    for (int i = 0; i < 2; i++) fcntl(ends[i], F_SETFD, fcntl(ends[i], F_GETFD) | FD_CLOEXEC);
    return 0;
#  endif
}

static void nonblocking(int fd) {
    fcntl(fd, F_SETFL, fcntl(fd, F_GETFL) | O_NONBLOCK);
}

/* The program's path: `name` itself where it holds a '/', else the first directory of PATH where an
 * executable file of that name stands (execvp's rule, done in the parent: the child may not
 * allocate). NULL where there is none — errno says why. */
static char *path_of(const char *name) {
    if (strchr(name, '/') != NULL) return strdup(name);
    const char *path = getenv("PATH");
    if (path == NULL || *path == '\0') path = "/usr/bin:/bin";
    size_t length = strlen(name);
    int seen = ENOENT;
    for (const char *at = path;;) {
        const char *end = strchr(at, ':');
        size_t dir = end != NULL ? (size_t)(end - at) : strlen(at);
        char *full = malloc(dir + 1 + length + 1);
        if (full == NULL) return NULL;
        if (dir == 0) {
            memcpy(full, name, length + 1);  /* an empty entry is the working directory */
        } else {
            memcpy(full, at, dir);
            full[dir] = '/';
            memcpy(full + dir + 1, name, length + 1);
        }
        struct stat info;
        if (stat(full, &info) == 0 && S_ISREG(info.st_mode)) {
            if (access(full, X_OK) == 0) return full;
            seen = EACCES;
        }
        free(full);
        if (end == NULL) break;
        at = end + 1;
    }
    errno = seen;
    return NULL;
}

/* `count` NUL-ended strings of `bytes` as a NULL-ended array; the strings stay where they are. */
static char **strings_of(const uint8_t *bytes, int64_t count) {
    char **list = calloc((size_t)count + 1, sizeof *list);
    if (list == NULL) return NULL;
    const char *at = (const char *)bytes;
    for (int64_t i = 0; i < count; i++) {
        list[i] = (char *)at;
        at += strlen(at) + 1;
    }
    return list;
}

/* The parent's environment with `set` (NAME=value each) over it: a NULL-ended array. */
static char **environment_of(char **set, int64_t count) {
    size_t inherited = 0;
    while (environ[inherited] != NULL) inherited++;
    char **list = calloc(inherited + (size_t)count + 1, sizeof *list);
    if (list == NULL) return NULL;
    size_t k = 0;
    for (size_t i = 0; i < inherited; i++) {
        const char *entry = environ[i];
        const char *equals = strchr(entry, '=');
        size_t name = equals != NULL ? (size_t)(equals - entry) : strlen(entry);
        int overridden = 0;
        for (int64_t j = 0; j < count && !overridden; j++) {
            overridden = strncmp(set[j], entry, name) == 0 && set[j][name] == '=';
        }
        if (!overridden) list[k++] = (char *)entry;
    }
    for (int64_t j = 0; j < count; j++) list[k++] = set[j];
    return list;
}

/* The reaper's pipe and the SIGCHLD handler that writes to it. */
static int child_pipe[2] = { -1, -1 };
static pthread_once_t child_once = PTHREAD_ONCE_INIT;

static void on_child(int number) {
    (void)number;
    int saved = errno;
    char byte = 1;
    ssize_t written = write(child_pipe[1], &byte, 1);  /* a full pipe wakes the reaper already */
    (void)written;
    errno = saved;
}

static void watch_children(void) {
    if (pipe_of(child_pipe) != 0) lyr_panic(LYR_RT_SYSTEM, "the system refused a pipe for the children's ends: %s", strerror(errno));
    nonblocking(child_pipe[0]);
    nonblocking(child_pipe[1]);
    struct sigaction action;
    memset(&action, 0, sizeof action);
    sigemptyset(&action.sa_mask);
    action.sa_handler = on_child;
    action.sa_flags = SA_RESTART | SA_NOCLDSTOP;
    if (sigaction(SIGCHLD, &action, NULL) != 0) lyr_panic(LYR_RT_SYSTEM, "the system refused the children's signal: %s", strerror(errno));
}

/* A byte into the pipe, as the handler writes one: before the reaper watches it, the byte waits there. */
void lyr_process_nudge(void) {
    pthread_once(&child_once, watch_children);
    char byte = 1;
    ssize_t written = write(child_pipe[1], &byte, 1);  /* a full pipe wakes the reaper already */
    (void)written;
}

void lyr_process_attach(void) {
    pthread_once(&child_once, watch_children);
    lyr_poller_watch(lyr_poller_current(), child_pipe[0]);
}

/* The child's side between fork and exec: only what is safe there. Its errno into `report` where
 * something fails, then its end. */
static void become(const char *path, char **argv, char **envp, const char *cwd, const int streams[3], int report) {
    sigset_t none;
    sigemptyset(&none);
    sigprocmask(SIG_SETMASK, &none, NULL);
    struct sigaction standard;
    memset(&standard, 0, sizeof standard);
    sigemptyset(&standard.sa_mask);
    standard.sa_handler = SIG_DFL;
    sigaction(SIGPIPE, &standard, NULL);
    for (int i = 0; i < 3; i++) {
        if (streams[i] >= 0 && dup2(streams[i], i) < 0) goto failed;
    }
    if (cwd != NULL && chdir(cwd) != 0) goto failed;
    execve(path, argv, envp);
failed:;
    int e = errno;
    ssize_t written = write(report, &e, sizeof e);
    (void)written;
    _exit(127);
}

int64_t lyr_process_spawn(const LyrStr *program, const uint8_t *args, int64_t argc, const uint8_t *env, int64_t envc,
                          const LyrStr *cwd, int64_t modes, int64_t *ends, int64_t n) {
    if (n < 3 || argc < 1) return failure(LYR_IO_INVALID_INPUT, 0);
    pthread_once(&child_once, watch_children);
    for (int i = 0; i < 3; i++) ends[i] = -1;
    int64_t answer = 0;
    char *path = path_of(program->bytes);
    char **argv = strings_of(args, argc);
    char **set = strings_of(env, envc);
    char **envp = set != NULL ? environment_of(set, envc) : NULL;
    int streams[3] = { -1, -1, -1 };   /* the child's ends, dup2'd onto 0, 1, 2 */
    int parents[3] = { -1, -1, -1 };   /* the parent's ends */
    int null = -1;
    int report[2] = { -1, -1 };
    if (path == NULL) {
        answer = failed_with(errno);
        goto done;
    }
    if (argv == NULL || set == NULL || envp == NULL) {
        answer = failure(0, ENOMEM);
        goto done;
    }
    for (int i = 0; i < 3; i++) {
        int mode = (int)((modes >> (2 * i)) & 3);
        if (mode == LYR_PROCESS_PIPED) {
            int pair[2];
            if (pipe_of(pair) != 0) {
                answer = failed_with(errno);
                goto done;
            }
            /* stdin: the child reads, the parent writes; stdout and stderr the other way */
            streams[i] = i == 0 ? pair[0] : pair[1];
            parents[i] = i == 0 ? pair[1] : pair[0];
            nonblocking(parents[i]);
        } else if (mode == LYR_PROCESS_NULL) {
            if (null < 0) null = open("/dev/null", O_RDWR | O_CLOEXEC);
            if (null < 0) {
                answer = failed_with(errno);
                goto done;
            }
            streams[i] = null;
        }
    }
    if (pipe_of(report) != 0) {
        answer = failed_with(errno);
        goto done;
    }
    pid_t pid = fork();
    if (pid < 0) {
        answer = failed_with(errno);
        goto done;
    }
    if (pid == 0) become(path, argv, envp, cwd->len > 0 ? cwd->bytes : NULL, streams, report[1]);
    close(report[1]);
    report[1] = -1;
    int e = 0;
    ssize_t got;
    do {
        got = read(report[0], &e, sizeof e);
    } while (got < 0 && errno == EINTR);
    if (got > 0) {
        /* the exec failed: the child is gone already, its end taken here */
        int status;
        while (waitpid(pid, &status, 0) < 0 && errno == EINTR) {}
        answer = failed_with(e);
        goto done;
    }
    for (int i = 0; i < 3; i++) {
        ends[i] = parents[i];
        parents[i] = -1;
    }
    answer = pid;
done:
    for (int i = 0; i < 3; i++) {
        if (streams[i] >= 0 && streams[i] != null) close(streams[i]);
        if (parents[i] >= 0) close(parents[i]);
    }
    if (null >= 0) close(null);
    if (report[0] >= 0) close(report[0]);
    if (report[1] >= 0) close(report[1]);
    free(path);
    free(argv);
    free(set);
    free(envp);
    return answer;
}

int64_t lyr_process_reap(int64_t pid) {
    int status;
    pid_t done;
    do {
        done = waitpid((pid_t)pid, &status, WNOHANG);
    } while (done < 0 && errno == EINTR);
    if (done < 0) return failed_with(errno);
    if (done == 0) return -1;
    if (WIFEXITED(status)) return WEXITSTATUS(status);
    if (WIFSIGNALED(status)) return LYR_PROCESS_SIGNALLED + WTERMSIG(status);
    return -1;
}

uint8_t lyr_process_pipes_block(void) { return 0; }

int64_t lyr_process_signal(int64_t pid, int64_t number) {
    if (kill((pid_t)pid, (int)number) != 0) return failed_with(errno);
    return 0;
}

int64_t lyr_process_read(int64_t fd, uint8_t *into, int64_t n) {
    ssize_t got;
    do {
        got = read((int)fd, into, (size_t)n);
    } while (got < 0 && errno == EINTR);
    if (got < 0) return failed_with(errno);
    return (int64_t)got;
}

int64_t lyr_process_write(int64_t fd, const uint8_t *from, int64_t n) {
    ssize_t sent;
    do {
        sent = write((int)fd, from, (size_t)n);
    } while (sent < 0 && errno == EINTR);
    if (sent < 0) return failed_with(errno);
    return (int64_t)sent;
}

int64_t lyr_process_close(int64_t fd) {
    if (close((int)fd) != 0 && errno != EINTR) return failed_with(errno);
    return 0;
}
#endif
