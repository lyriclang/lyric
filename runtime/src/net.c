/* The network's calls (lyr/net.h): sockets, non-blocking — POSIX's, and Winsock's since M8b S11.
 * One call each; the few places where the two differ are the helpers below: what a failure says,
 * how a socket is made non-blocking, closed, its name resolved. */
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

#include <limits.h>
#include <string.h>

#ifdef _WIN32
#  define WIN32_LEAN_AND_MEAN
#  include <winsock2.h>
#  include <ws2tcpip.h>
#  include <windows.h>
#  include <stdlib.h>
#  ifndef WSA_FLAG_NO_HANDLE_INHERIT
#    define WSA_FLAG_NO_HANDLE_INHERIT 0x80
#  endif
typedef SOCKET Socket;
typedef int Length;  /* Winsock's counts are ints */
#  define LENGTH_MAX INT_MAX
#else
#  include <errno.h>
#  include <fcntl.h>
#  include <netdb.h>
#  include <netinet/in.h>
#  include <netinet/tcp.h>
#  include <sys/socket.h>
#  include <unistd.h>
typedef int Socket;
typedef size_t Length;
#  define LENGTH_MAX SIZE_MAX
#endif

static int64_t failure(int64_t kind, int64_t raw) {
    return -((kind << 32) | (raw & 0xFFFFFFFF));
}

/* A count of bytes as the system takes it: at most what one call takes. */
static Length length_of(int64_t n) {
    return n < 0 ? 0 : (uint64_t)n > (uint64_t)LENGTH_MAX ? (Length)LENGTH_MAX : (Length)n;
}

/* What the system's last error says, as a failure: the kinds a socket can give by their names
 * (10 O3), any other as Other with its number; a call that would block, as such. */
#ifdef _WIN32
static int64_t failed(void) {
    int e = WSAGetLastError();
    switch (e) {
    case WSAEWOULDBLOCK:
    case WSAEINPROGRESS:
    case WSAEALREADY:
        return LYR_NET_WOULD_BLOCK;
    case WSAECONNREFUSED: return failure(LYR_IO_CONNECTION_REFUSED, e);
    case WSAECONNRESET:
    case WSAECONNABORTED:
    case WSAENETRESET: return failure(LYR_IO_CONNECTION_RESET, e);
    case WSAEADDRINUSE: return failure(LYR_IO_ADDR_IN_USE, e);
    case WSAESHUTDOWN: return failure(LYR_IO_BROKEN_PIPE, e);
    case WSAETIMEDOUT: return failure(LYR_IO_TIMED_OUT, e);
    case WSAEACCES: return failure(LYR_IO_PERMISSION_DENIED, e);
    case WSAEINVAL:
    case WSAEADDRNOTAVAIL: return failure(LYR_IO_INVALID_INPUT, e);
    case WSAEAFNOSUPPORT:
    case WSAEPROTONOSUPPORT: return failure(LYR_IO_UNSUPPORTED, e);
    default: return failure(0, e);
    }
}

#  define INTERRUPTED() 0
#  define BAD_SOCKET(s) ((s) == INVALID_SOCKET)
#  define CLOSE(s) closesocket(s)

/* Winsock's start, once for the process (2.2, which every Windows since XP has). */
static INIT_ONCE started = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK start_winsock(PINIT_ONCE once, PVOID parameter, PVOID *context) {
    (void)once; (void)parameter; (void)context;
    WSADATA data;
    return WSAStartup(MAKEWORD(2, 2), &data) == 0;
}

static int nonblocking(Socket s) {
    u_long on = 1;
    return ioctlsocket(s, FIONBIO, &on) == 0;
}
#else
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

#  define INTERRUPTED() (errno == EINTR)
#  define BAD_SOCKET(s) ((s) < 0)
#  define CLOSE(s) close(s)

#  ifndef __linux__
/* The descriptor non-blocking and closed on exec, where its making could not say so. */
static int prepared(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0 || fcntl(fd, F_SETFL, flags | O_NONBLOCK) != 0) return 0;
    int fdflags = fcntl(fd, F_GETFD, 0);
    return fdflags >= 0 && fcntl(fd, F_SETFD, fdflags | FD_CLOEXEC) == 0;
}
#  endif
#endif

/* No signal for a peer that is gone: Linux is asked, macOS's socket was told (SO_NOSIGPIPE), and
 * Windows has none. */
#ifdef MSG_NOSIGNAL
#  define SEND_FLAGS MSG_NOSIGNAL
#else
#  define SEND_FLAGS 0
#endif

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

/* The system's address as lyr/net.h writes one: LYR_NET_ADDRESS_BYTES, or 0 for a family that is
 * neither. */
static int64_t write_address(const struct sockaddr_storage *at, uint8_t *into) {
    memset(into, 0, LYR_NET_ADDRESS_BYTES);
    uint16_t port;
    if (at->ss_family == AF_INET) {
        const struct sockaddr_in *v4 = (const struct sockaddr_in *)at;
        into[0] = 4;
        port = ntohs(v4->sin_port);
        memcpy(into + 3, &v4->sin_addr, 4);
    } else if (at->ss_family == AF_INET6) {
        const struct sockaddr_in6 *v6 = (const struct sockaddr_in6 *)at;
        into[0] = 6;
        port = ntohs(v6->sin6_port);
        memcpy(into + 3, &v6->sin6_addr, 16);
    } else {
        return 0;
    }
    into[1] = (uint8_t)(port >> 8);
    into[2] = (uint8_t)(port & 0xFF);
    return LYR_NET_ADDRESS_BYTES;
}

int64_t lyr_net_socket(int64_t family, int64_t kind) {
    int domain = family == 6 ? AF_INET6 : family == 4 ? AF_INET : -1;
    if (domain < 0) return failure(LYR_IO_INVALID_INPUT, 0);
    int type = kind == LYR_NET_DATAGRAM ? SOCK_DGRAM : SOCK_STREAM;
#ifdef _WIN32
    if (!InitOnceExecuteOnce(&started, start_winsock, NULL, NULL)) return failure(LYR_IO_UNSUPPORTED, (int64_t)GetLastError());
    /* overlapped, so AFD's poll can wait on it; no handle a child would inherit */
    Socket s = WSASocketW(domain, type, 0, NULL, 0, WSA_FLAG_OVERLAPPED | WSA_FLAG_NO_HANDLE_INHERIT);
    if (BAD_SOCKET(s)) return failed();
    if (!nonblocking(s)) {
        int64_t answer = failed();
        CLOSE(s);
        return answer;
    }
    return (int64_t)s;
#else
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
#endif
}

int64_t lyr_net_bind(int64_t fd, int64_t family, const uint8_t *address, int64_t port, int64_t reuse) {
    struct sockaddr_storage at;
    socklen_t length = address_of(family, address, port, &at);
    if (length == 0) return failure(LYR_IO_INVALID_INPUT, 0);
#ifndef _WIN32
    /* a port its last listener left in TIME_WAIT; Windows' SO_REUSEADDR would let another program
     * take a port in use (Rust and Go leave it there) */
    if (reuse) {
        int one = 1;
        if (setsockopt((Socket)fd, SOL_SOCKET, SO_REUSEADDR, &one, sizeof one) != 0) return failed();
    }
#else
    (void)reuse;
#endif
    if (bind((Socket)fd, (struct sockaddr *)&at, length) != 0) return failed();
    return 0;
}

int64_t lyr_net_listen(int64_t fd, int64_t backlog) {
    if (listen((Socket)fd, (int)backlog) != 0) return failed();
    return 0;
}

int64_t lyr_net_accept(int64_t fd) {
#if defined(_WIN32)
    Socket taken = accept((Socket)fd, NULL, NULL);
    if (BAD_SOCKET(taken)) return failed();
    /* it has the listener's properties; non-blocking said again all the same */
    if (!nonblocking(taken)) {
        int64_t answer = failed();
        CLOSE(taken);
        return answer;
    }
    return (int64_t)taken;
#elif defined(__linux__)
    int taken;
    do {
        taken = accept4((int)fd, NULL, NULL, SOCK_NONBLOCK | SOCK_CLOEXEC);
    } while (taken < 0 && errno == EINTR);
    if (taken < 0) return failed();
    return taken;
#else
    int taken;
    do {
        taken = accept((int)fd, NULL, NULL);
    } while (taken < 0 && errno == EINTR);
    if (taken < 0) return failed();
    if (!prepared(taken)) {
        int64_t answer = failed();
        close(taken);
        return answer;
    }
#  ifdef SO_NOSIGPIPE
    int one = 1;
    (void)setsockopt(taken, SOL_SOCKET, SO_NOSIGPIPE, &one, sizeof one);
#  endif
    return taken;
#endif
}

int64_t lyr_net_connect(int64_t fd, int64_t family, const uint8_t *address, int64_t port) {
    struct sockaddr_storage at;
    socklen_t length = address_of(family, address, port, &at);
    if (length == 0) return failure(LYR_IO_INVALID_INPUT, 0);
    /* one call: an EINTR leaves the connection going on, as EINPROGRESS does */
    if (connect((Socket)fd, (struct sockaddr *)&at, length) != 0) {
        return INTERRUPTED() ? LYR_NET_WOULD_BLOCK : failed();
    }
    return 0;
}

int64_t lyr_net_connected(int64_t fd) {
    int error = 0;
    socklen_t length = sizeof error;
    if (getsockopt((Socket)fd, SOL_SOCKET, SO_ERROR, (char *)&error, &length) != 0) return failed();
    if (error == 0) return 0;
#ifdef _WIN32
    WSASetLastError(error);
#else
    errno = error;
#endif
    return failed();
}

int64_t lyr_net_recv(int64_t fd, uint8_t *into, int64_t n) {
    for (;;) {
        int64_t got = (int64_t)recv((Socket)fd, (char *)into, length_of(n), 0);
        if (got >= 0) return got;
        if (!INTERRUPTED()) return failed();
    }
}

int64_t lyr_net_send(int64_t fd, const uint8_t *from, int64_t n) {
    for (;;) {
        int64_t sent = (int64_t)send((Socket)fd, (const char *)from, length_of(n), SEND_FLAGS);
        if (sent >= 0) return sent;
        if (!INTERRUPTED()) return failed();
    }
}

int64_t lyr_net_shutdown(int64_t fd, int64_t how) {
#ifdef _WIN32
    int which = how == LYR_NET_SHUT_READ ? SD_RECEIVE : how == LYR_NET_SHUT_WRITE ? SD_SEND : SD_BOTH;
#else
    int which = how == LYR_NET_SHUT_READ ? SHUT_RD : how == LYR_NET_SHUT_WRITE ? SHUT_WR : SHUT_RDWR;
#endif
    if (shutdown((Socket)fd, which) != 0) return failed();
    return 0;
}

int64_t lyr_net_set_nodelay(int64_t fd, int64_t on) {
    int value = on ? 1 : 0;
    if (setsockopt((Socket)fd, IPPROTO_TCP, TCP_NODELAY, (const char *)&value, sizeof value) != 0) return failed();
    return 0;
}

int64_t lyr_net_name(int64_t fd, int64_t peer, uint8_t *into, int64_t n) {
    if (n < LYR_NET_ADDRESS_BYTES) return failure(LYR_IO_INVALID_INPUT, 0);
    struct sockaddr_storage at;
    socklen_t length = sizeof at;
    int done = peer ? getpeername((Socket)fd, (struct sockaddr *)&at, &length) : getsockname((Socket)fd, (struct sockaddr *)&at, &length);
    if (done != 0) return failed();
    if (write_address(&at, into) == 0) return failure(LYR_IO_UNSUPPORTED, 0);
    return LYR_NET_ADDRESS_BYTES;
}

int64_t lyr_net_close(int64_t fd) {
    /* never taken again: after an EINTR the descriptor is gone on Linux, maybe not elsewhere */
    if (CLOSE((Socket)fd) != 0 && !INTERRUPTED()) return failed();
    return 0;
}

int64_t lyr_net_sendto(int64_t fd, const uint8_t *from, int64_t n, int64_t family, const uint8_t *address, int64_t port) {
    struct sockaddr_storage at;
    socklen_t length = address_of(family, address, port, &at);
    if (length == 0) return failure(LYR_IO_INVALID_INPUT, 0);
    for (;;) {
        int64_t sent = (int64_t)sendto((Socket)fd, (const char *)from, length_of(n), SEND_FLAGS, (struct sockaddr *)&at, length);
        if (sent >= 0) return sent;
        if (!INTERRUPTED()) return failed();
    }
}

int64_t lyr_net_recvfrom(int64_t fd, uint8_t *into, int64_t n, uint8_t *sender, int64_t room) {
    if (room < LYR_NET_ADDRESS_BYTES) return failure(LYR_IO_INVALID_INPUT, 0);
    struct sockaddr_storage at;
    for (;;) {
        socklen_t length = sizeof at;
        int64_t got = (int64_t)recvfrom((Socket)fd, (char *)into, length_of(n), 0, (struct sockaddr *)&at, &length);
        if (got >= 0) {
            if (write_address(&at, sender) == 0) return failure(LYR_IO_UNSUPPORTED, 0);
            return got;
        }
#ifdef _WIN32
        /* a datagram longer than `into`: what fit is there, the rest is lost (POSIX's reading) */
        if (WSAGetLastError() == WSAEMSGSIZE) {
            if (write_address(&at, sender) == 0) return failure(LYR_IO_UNSUPPORTED, 0);
            return (int64_t)length_of(n);
        }
#endif
        if (!INTERRUPTED()) return failed();
    }
}

/* One address of a resolver's answer into the list, where it is not there yet and there is room:
 * the count after it. */
static int64_t keep(int family, const void *bytes, uint8_t *into, int64_t n, int64_t count) {
    uint8_t entry[LYR_NET_HOST_BYTES] = { 0 };
    entry[0] = (uint8_t)family;
    memcpy(entry + 1, bytes, family == 4 ? 4 : 16);
    /* each once: a resolver may give an address again */
    int64_t held = count * LYR_NET_HOST_BYTES <= n ? count : n / LYR_NET_HOST_BYTES;
    for (int64_t k = 0; k < held; k++) {
        if (memcmp(into + k * LYR_NET_HOST_BYTES, entry, LYR_NET_HOST_BYTES) == 0) return count;
    }
    if ((count + 1) * LYR_NET_HOST_BYTES <= n) memcpy(into + count * LYR_NET_HOST_BYTES, entry, LYR_NET_HOST_BYTES);
    return count + 1;
}

#ifdef _WIN32
int64_t lyr_net_resolve(const LyrStr *host, uint8_t *into, int64_t n) {
    if (!InitOnceExecuteOnce(&started, start_winsock, NULL, NULL)) return failure(LYR_IO_UNSUPPORTED, (int64_t)GetLastError());
    /* the name as UTF-16: GetAddrInfoW takes what is not ASCII as it is */
    int wide = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, host->bytes, (int)host->len, NULL, 0);
    if (wide <= 0 && host->len > 0) return failure(LYR_IO_INVALID_INPUT, (int64_t)GetLastError());
    wchar_t *name = calloc((size_t)wide + 1, sizeof *name);
    if (name == NULL) return failure(0, ERROR_NOT_ENOUGH_MEMORY);
    if (wide > 0) MultiByteToWideChar(CP_UTF8, 0, host->bytes, (int)host->len, name, wide);
    ADDRINFOW hints;
    memset(&hints, 0, sizeof hints);
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;  /* one entry an address, not one a kind of socket */
    ADDRINFOW *found = NULL;
    int code = GetAddrInfoW(name, NULL, &hints, &found);
    free(name);
    if (code != 0) return code == WSAHOST_NOT_FOUND || code == WSANO_DATA ? failure(LYR_IO_NOT_FOUND, code) : failure(0, code);
    int64_t count = 0;
    for (ADDRINFOW *at = found; at != NULL; at = at->ai_next) {
        if (at->ai_family == AF_INET) count = keep(4, &((struct sockaddr_in *)at->ai_addr)->sin_addr, into, n, count);
        else if (at->ai_family == AF_INET6) count = keep(6, &((struct sockaddr_in6 *)at->ai_addr)->sin6_addr, into, n, count);
    }
    FreeAddrInfoW(found);
    return count;
}
#else
/* What getaddrinfo's answer says, as a failure: a name nobody knows NotFound (Rust's and Go's
 * reading), the system's error by errno, any other with the resolver's own number. */
static int64_t unresolved(int code) {
    switch (code) {
    case EAI_NONAME:
#  ifdef EAI_NODATA
#    if EAI_NODATA != EAI_NONAME
    case EAI_NODATA:
#    endif
#  endif
        return failure(LYR_IO_NOT_FOUND, code);
    case EAI_SYSTEM: return failed();
    default: return failure(0, code);
    }
}

int64_t lyr_net_resolve(const LyrStr *host, uint8_t *into, int64_t n) {
    struct addrinfo hints;
    memset(&hints, 0, sizeof hints);
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;  /* one entry an address, not one a kind of socket */
    struct addrinfo *found = NULL;
    int code = getaddrinfo(host->bytes, NULL, &hints, &found);
    if (code != 0) return unresolved(code);
    int64_t count = 0;
    for (struct addrinfo *at = found; at != NULL; at = at->ai_next) {
        if (at->ai_family == AF_INET) count = keep(4, &((struct sockaddr_in *)at->ai_addr)->sin_addr, into, n, count);
        else if (at->ai_family == AF_INET6) count = keep(6, &((struct sockaddr_in6 *)at->ai_addr)->sin6_addr, into, n, count);
    }
    freeaddrinfo(found);
    return count;
}
#endif
