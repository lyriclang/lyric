/* Processes (lyr/process.h): fork and exec on POSIX, the reaper's SIGCHLD pipe, a pipe's ends.
 * Windows answers Unsupported until S12b. */
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

int64_t lyr_process_spawn(const LyrStr *program, const uint8_t *args, int64_t argc, const uint8_t *env, int64_t envc,
                          const LyrStr *cwd, int64_t modes, int64_t *ends, int64_t n) {
    (void)program; (void)args; (void)argc; (void)env; (void)envc; (void)cwd; (void)modes; (void)ends; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

void lyr_process_attach(void) {}

int64_t lyr_process_reap(int64_t pid) {
    (void)pid;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_process_signal(int64_t pid, int64_t number) {
    (void)pid; (void)number;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_process_read(int64_t fd, uint8_t *into, int64_t n) {
    (void)fd; (void)into; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_process_write(int64_t fd, const uint8_t *from, int64_t n) {
    (void)fd; (void)from; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_process_close(int64_t fd) {
    (void)fd;
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
    if (WIFSIGNALED(status)) return 256 + WTERMSIG(status);
    return -1;
}

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
