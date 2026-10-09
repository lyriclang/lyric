/* The network's calls (lyr/net.h): POSIX sockets, non-blocking; Windows answers Unsupported until
 * S11. */
#if defined(__linux__)
#  define _GNU_SOURCE  /* accept4 */
#elif defined(__APPLE__)
#  define _DARWIN_C_SOURCE
#elif !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#  define _POSIX_C_SOURCE 200809L
#endif

#include "lyr/net.h"
#include "lyr/console.h"
#include "lyr/fs.h"

#include <string.h>

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

#ifdef _WIN32

int64_t lyr_net_socket(int64_t family, int64_t kind) {
    (void)family; (void)kind;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_bind(int64_t fd, int64_t family, const uint8_t *address, int64_t port, int64_t reuse) {
    (void)fd; (void)family; (void)address; (void)port; (void)reuse;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_listen(int64_t fd, int64_t backlog) {
    (void)fd; (void)backlog;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_accept(int64_t fd) {
    (void)fd;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_connect(int64_t fd, int64_t family, const uint8_t *address, int64_t port) {
    (void)fd; (void)family; (void)address; (void)port;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_connected(int64_t fd) {
    (void)fd;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_recv(int64_t fd, uint8_t *into, int64_t n) {
    (void)fd; (void)into; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_send(int64_t fd, const uint8_t *from, int64_t n) {
    (void)fd; (void)from; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_shutdown(int64_t fd, int64_t how) {
    (void)fd; (void)how;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_set_nodelay(int64_t fd, int64_t on) {
    (void)fd; (void)on;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_name(int64_t fd, int64_t peer, uint8_t *into, int64_t n) {
    (void)fd; (void)peer; (void)into; (void)n;
    return failure(LYR_IO_UNSUPPORTED, 0);
}

int64_t lyr_net_close(int64_t fd) {
    (void)fd;
    return 0;
}

#else
#  include <errno.h>
#  include <fcntl.h>
#  include <netinet/in.h>
#  include <netinet/tcp.h>
#  include <sys/socket.h>
#  include <unistd.h>

/* What `errno` says, as a failure: the kinds a socket can give by their names (10 O3), any other
 * as Other with its number. */
static int64_t failed(void) {
    int e = errno;
    switch (e) {
    case EAGAIN:
#  if EWOULDBLOCK != EAGAIN
    case EWOULDBLOCK:
#  endif
    case EINPROGRESS:
        return LYR_NET_WOULD_BLOCK;
    case ECONNREFUSED: return failure(LYR_IO_CONNECTION_REFUSED, e);
    case ECONNRESET:
    case ECONNABORTED: return failure(LYR_IO_CONNECTION_RESET, e);
    case EADDRINUSE: return failure(LYR_IO_ADDR_IN_USE, e);
    case EPIPE: return failure(LYR_IO_BROKEN_PIPE, e);
    case ETIMEDOUT: return failure(LYR_IO_TIMED_OUT, e);
    case EACCES:
    case EPERM: return failure(LYR_IO_PERMISSION_DENIED, e);
    case EINVAL:
    case EADDRNOTAVAIL: return failure(LYR_IO_INVALID_INPUT, e);
    case EAFNOSUPPORT:
    case EPROTONOSUPPORT: return failure(LYR_IO_UNSUPPORTED, e);
    default: return failure(0, e);
    }
}

#  ifndef __linux__
/* The descriptor non-blocking and closed on exec, where its making could not say so. */
static int prepared(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0 || fcntl(fd, F_SETFL, flags | O_NONBLOCK) != 0) return 0;
    int fdflags = fcntl(fd, F_GETFD, 0);
    return fdflags >= 0 && fcntl(fd, F_SETFD, fdflags | FD_CLOEXEC) == 0;
}
#  endif

/* The address as the system's: its length, 0 for a family that is neither. */
static socklen_t address_of(int64_t family, const uint8_t *bytes, int64_t port, struct sockaddr_storage *into) {
    memset(into, 0, sizeof *into);
    if (family == 4) {
        struct sockaddr_in *v4 = (struct sockaddr_in *)into;
        v4->sin_family = AF_INET;
        v4->sin_port = htons((uint16_t)port);
        memcpy(&v4->sin_addr, bytes, 4);
        return sizeof *v4;
    }
    if (family == 6) {
        struct sockaddr_in6 *v6 = (struct sockaddr_in6 *)into;
        v6->sin6_family = AF_INET6;
        v6->sin6_port = htons((uint16_t)port);
        memcpy(&v6->sin6_addr, bytes, 16);
        return sizeof *v6;
    }
    return 0;
}

int64_t lyr_net_socket(int64_t family, int64_t kind) {
    int domain = family == 6 ? AF_INET6 : family == 4 ? AF_INET : -1;
    if (domain < 0) return failure(LYR_IO_INVALID_INPUT, 0);
    int type = kind == LYR_NET_DATAGRAM ? SOCK_DGRAM : SOCK_STREAM;
#  if defined(SOCK_NONBLOCK) && defined(SOCK_CLOEXEC)
    int fd = socket(domain, type | SOCK_NONBLOCK | SOCK_CLOEXEC, 0);
    if (fd < 0) return failed();
#  else
    int fd = socket(domain, type, 0);
    if (fd < 0) return failed();
    if (!prepared(fd)) {
        int64_t answer = failed();
        close(fd);
        return answer;
    }
#  endif
#  ifdef SO_NOSIGPIPE
    /* no MSG_NOSIGNAL here (macOS): the socket itself is told */
    int one = 1;
    (void)setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &one, sizeof one);
#  endif
    return fd;
}

int64_t lyr_net_bind(int64_t fd, int64_t family, const uint8_t *address, int64_t port, int64_t reuse) {
    struct sockaddr_storage at;
    socklen_t length = address_of(family, address, port, &at);
    if (length == 0) return failure(LYR_IO_INVALID_INPUT, 0);
    if (reuse) {
        int one = 1;
        if (setsockopt((int)fd, SOL_SOCKET, SO_REUSEADDR, &one, sizeof one) != 0) return failed();
    }
    if (bind((int)fd, (struct sockaddr *)&at, length) != 0) return failed();
    return 0;
}

int64_t lyr_net_listen(int64_t fd, int64_t backlog) {
    if (listen((int)fd, (int)backlog) != 0) return failed();
    return 0;
}

int64_t lyr_net_accept(int64_t fd) {
    int taken;
#  if defined(__linux__)
    do {
        taken = accept4((int)fd, NULL, NULL, SOCK_NONBLOCK | SOCK_CLOEXEC);
    } while (taken < 0 && errno == EINTR);
    if (taken < 0) return failed();
#  else
    do {
        taken = accept((int)fd, NULL, NULL);
    } while (taken < 0 && errno == EINTR);
    if (taken < 0) return failed();
    if (!prepared(taken)) {
        int64_t answer = failed();
        close(taken);
        return answer;
    }
#    ifdef SO_NOSIGPIPE
    int one = 1;
    (void)setsockopt(taken, SOL_SOCKET, SO_NOSIGPIPE, &one, sizeof one);
#    endif
#  endif
    return taken;
}

int64_t lyr_net_connect(int64_t fd, int64_t family, const uint8_t *address, int64_t port) {
    struct sockaddr_storage at;
    socklen_t length = address_of(family, address, port, &at);
    if (length == 0) return failure(LYR_IO_INVALID_INPUT, 0);
    /* one call: an EINTR leaves the connection going on, as EINPROGRESS does */
    if (connect((int)fd, (struct sockaddr *)&at, length) != 0) {
        return errno == EINTR ? LYR_NET_WOULD_BLOCK : failed();
    }
    return 0;
}

int64_t lyr_net_connected(int64_t fd) {
    int error = 0;
    socklen_t length = sizeof error;
    if (getsockopt((int)fd, SOL_SOCKET, SO_ERROR, &error, &length) != 0) return failed();
    if (error == 0) return 0;
    errno = error;
    return failed();
}

int64_t lyr_net_recv(int64_t fd, uint8_t *into, int64_t n) {
    ssize_t got;
    do {
        got = recv((int)fd, into, (size_t)n, 0);
    } while (got < 0 && errno == EINTR);
    if (got < 0) return failed();
    return (int64_t)got;
}

int64_t lyr_net_send(int64_t fd, const uint8_t *from, int64_t n) {
#  ifdef MSG_NOSIGNAL
    int flags = MSG_NOSIGNAL;
#  else
    int flags = 0;
#  endif
    ssize_t sent;
    do {
        sent = send((int)fd, from, (size_t)n, flags);
    } while (sent < 0 && errno == EINTR);
    if (sent < 0) return failed();
    return (int64_t)sent;
}

int64_t lyr_net_shutdown(int64_t fd, int64_t how) {
    int which = how == LYR_NET_SHUT_READ ? SHUT_RD : how == LYR_NET_SHUT_WRITE ? SHUT_WR : SHUT_RDWR;
    if (shutdown((int)fd, which) != 0) return failed();
    return 0;
}

int64_t lyr_net_set_nodelay(int64_t fd, int64_t on) {
    int value = on ? 1 : 0;
    if (setsockopt((int)fd, IPPROTO_TCP, TCP_NODELAY, &value, sizeof value) != 0) return failed();
    return 0;
}

int64_t lyr_net_name(int64_t fd, int64_t peer, uint8_t *into, int64_t n) {
    if (n < LYR_NET_ADDRESS_BYTES) return failure(LYR_IO_INVALID_INPUT, 0);
    struct sockaddr_storage at;
    socklen_t length = sizeof at;
    int done = peer ? getpeername((int)fd, (struct sockaddr *)&at, &length) : getsockname((int)fd, (struct sockaddr *)&at, &length);
    if (done != 0) return failed();
    memset(into, 0, LYR_NET_ADDRESS_BYTES);
    uint16_t port;
    if (at.ss_family == AF_INET) {
        struct sockaddr_in *v4 = (struct sockaddr_in *)&at;
        into[0] = 4;
        port = ntohs(v4->sin_port);
        memcpy(into + 3, &v4->sin_addr, 4);
    } else if (at.ss_family == AF_INET6) {
        struct sockaddr_in6 *v6 = (struct sockaddr_in6 *)&at;
        into[0] = 6;
        port = ntohs(v6->sin6_port);
        memcpy(into + 3, &v6->sin6_addr, 16);
    } else {
        return failure(LYR_IO_UNSUPPORTED, 0);
    }
    into[1] = (uint8_t)(port >> 8);
    into[2] = (uint8_t)(port & 0xFF);
    return LYR_NET_ADDRESS_BYTES;
}

int64_t lyr_net_close(int64_t fd) {
    /* never taken again: after an EINTR the descriptor is gone on Linux, maybe not elsewhere */
    if (close((int)fd) != 0 && errno != EINTR) return failed();
    return 0;
}
#endif
