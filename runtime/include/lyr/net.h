/* The network's calls (design/v5/spec/10 O7; 13 M8b P1, P2, S10b): one system call each, EINTR
 * taken again, a socket a POSIX descriptor in an int64_t — non-blocking and closed on exec. A call
 * that would block answers LYR_NET_WOULD_BLOCK, and std.net waits on the poller (lyr/poll.h) for
 * the socket's readiness, then asks again. A failure is as lyr/fs.h writes one. An address crosses
 * as its family (4 or 6), its 16 bytes — an IPv4 address in the first four — and its port.
 *
 * Windows' sockets are Winsock's (S11): a SOCKET in the int64_t, waited for by AFD (lyr/poll.h). */
#ifndef LYR_NET_H
#define LYR_NET_H

#include <stdint.h>

#include "lyr/types.h"

/* The call would block: wait for the readiness its direction needs, then call again. No kind of
 * IoErrorKind's: std.net sees it before a failure is made. */
#define LYR_NET_WOULD_BLOCK (-(INT64_C(64) << 32))

/* What a socket carries. */
#define LYR_NET_STREAM 1
#define LYR_NET_DATAGRAM 2

/* How much of a stream a shutdown ends: std.net's Shutdown, from 0. */
#define LYR_NET_SHUT_READ 0
#define LYR_NET_SHUT_WRITE 1
#define LYR_NET_SHUT_BOTH 2

/* An address's room in a slice that answers one (lyr_net_name): its family, its port's two bytes
 * (the high first), its 16 bytes. */
#define LYR_NET_ADDRESS_BYTES 19

/* A new socket of `family` that carries `kind`. */
int64_t lyr_net_socket(int64_t family, int64_t kind);

/* The socket bound to the address — `reuse` lets a listener take a port its last one left in
 * TIME_WAIT (SO_REUSEADDR) —, and made to listen with `backlog` waiting connections. 0. */
int64_t lyr_net_bind(int64_t fd, int64_t family, const uint8_t *address, int64_t port, int64_t reuse);
int64_t lyr_net_listen(int64_t fd, int64_t backlog);

/* A connection the listener has waiting: its socket. */
int64_t lyr_net_accept(int64_t fd);

/* The socket connecting to the address: 0 where it is connected at once, LYR_NET_WOULD_BLOCK where
 * it goes on — wait for writing, then lyr_net_connected says how it ended: 0, or the failure. */
int64_t lyr_net_connect(int64_t fd, int64_t family, const uint8_t *address, int64_t port);
int64_t lyr_net_connected(int64_t fd);

/* At most `n` bytes of the stream into `into`: how many, 0 at its end. At most `n` bytes of `from`
 * into the stream: how many. A peer that is gone is BrokenPipe — the signal is ignored (10 Q9),
 * and Linux is asked not to send it. */
int64_t lyr_net_recv(int64_t fd, uint8_t *into, int64_t n);
int64_t lyr_net_send(int64_t fd, const uint8_t *from, int64_t n);

/* The stream's reading, writing or both ended (LYR_NET_SHUT_*). 0. */
int64_t lyr_net_shutdown(int64_t fd, int64_t how);

/* Nagle's waiting off (TCP_NODELAY) where `on`, back on where not. 0. */
int64_t lyr_net_set_nodelay(int64_t fd, int64_t on);

/* The socket's own address, or its peer's where `peer`, into `into` as LYR_NET_ADDRESS_BYTES
 * say: how many bytes, LYR_NET_ADDRESS_BYTES. */
int64_t lyr_net_name(int64_t fd, int64_t peer, uint8_t *into, int64_t n);

/* The socket closed: 0, or the failure the system's close gave — the socket is gone either way. */
int64_t lyr_net_close(int64_t fd);

/* A datagram (M8b S10c): at most `n` bytes of `from` sent to the address — how many; and the next
 * datagram received, at most `n` of its bytes into `into` — how many, the rest of a longer one
 * lost — with its sender into `sender` as lyr_net_name writes an address. */
int64_t lyr_net_sendto(int64_t fd, const uint8_t *from, int64_t n, int64_t family, const uint8_t *address, int64_t port);
int64_t lyr_net_recvfrom(int64_t fd, uint8_t *into, int64_t n, uint8_t *sender, int64_t room);

/* The addresses `host` names (M8b S10c, getaddrinfo — a blocking call, which std.net makes on the
 * I/O pool): each as LYR_NET_HOST_BYTES — its family, its 16 bytes — into `into`, each once, in the
 * resolver's order; how many there are, which may be more than fit: then call again with room. A
 * name nobody knows is NotFound. */
#define LYR_NET_HOST_BYTES 17
int64_t lyr_net_resolve(const LyrStr *host, uint8_t *into, int64_t n);

/* A Slice<uint8> of emitted code is a pointer and a length (CEmitter). */
#define LYR_NET_BIND(fd, family, address, port, reuse) lyr_net_bind((fd), (family), (address).ptr, (port), (reuse))
#define LYR_NET_CONNECT(fd, family, address, port) lyr_net_connect((fd), (family), (address).ptr, (port))
#define LYR_NET_RECV(fd, slice) lyr_net_recv((fd), (slice).ptr, (slice).len)
#define LYR_NET_SEND(fd, slice) lyr_net_send((fd), (slice).ptr, (slice).len)
#define LYR_NET_NAME(fd, peer, slice) lyr_net_name((fd), (peer), (slice).ptr, (slice).len)
#define LYR_NET_SENDTO(fd, slice, family, address, port) lyr_net_sendto((fd), (slice).ptr, (slice).len, (family), (address).ptr, (port))
#define LYR_NET_RECVFROM(fd, slice, sender) lyr_net_recvfrom((fd), (slice).ptr, (slice).len, (sender).ptr, (sender).len)
#define LYR_NET_RESOLVE(host, slice) lyr_net_resolve((host), (slice).ptr, (slice).len)

#endif
