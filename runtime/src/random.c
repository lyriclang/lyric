/* The system's random bytes (lyr/random.h). */
#if defined(_WIN32)
#define _CRT_RAND_S
#elif defined(__linux__)
#define _GNU_SOURCE
#endif

#include "lyr/random.h"
#include "lyr/panic.h"

#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
/* rand_s: the CRT's, over RtlGenRandom — 32 bits a call, and no library joins the link. */
static int fill(unsigned char *out, size_t length) {
    while (length > 0) {
        unsigned int word;
        if (rand_s(&word) != 0) return 0;
        size_t n = length < sizeof word ? length : sizeof word;
        memcpy(out, &word, n);
        out += n;
        length -= n;
    }
    return 1;
}
#elif defined(__APPLE__)
#include <sys/random.h>

/* getentropy: at most 256 bytes a call. */
static int fill(unsigned char *out, size_t length) {
    while (length > 0) {
        size_t n = length < 256 ? length : 256;
        if (getentropy(out, n) != 0) return 0;
        out += n;
        length -= n;
    }
    return 1;
}
#else
#include <errno.h>
#include <sys/random.h>

/* getrandom: a short read or an interrupted one is taken again. */
static int fill(unsigned char *out, size_t length) {
    while (length > 0) {
        ssize_t n = getrandom(out, length, 0);
        if (n < 0) {
            if (errno == EINTR) continue;
            return 0;
        }
        out += n;
        length -= (size_t)n;
    }
    return 1;
}
#endif

void lyr_random_bytes(void *buffer, size_t length) {
    if (!fill((unsigned char *)buffer, length)) {
        lyr_panic(LYR_RT_SYSTEM, "the system gave no random bytes");
    }
}

uint64_t lyr_random_u64(void) {
    uint64_t value;
    lyr_random_bytes(&value, sizeof value);
    return value;
}

/* 0 none, 1 being drawn, 2 drawn: the first caller draws, a racing one waits for it. */
static atomic_int key_state;
static uint64_t key[2];

uint64_t lyr_hash_key(int64_t which) {
    if (atomic_load_explicit(&key_state, memory_order_acquire) != 2) {
        int expected = 0;
        if (atomic_compare_exchange_strong(&key_state, &expected, 1)) {
            lyr_random_bytes(key, sizeof key);
            atomic_store_explicit(&key_state, 2, memory_order_release);
        } else {
            while (atomic_load_explicit(&key_state, memory_order_acquire) != 2) {
            }
        }
    }
    return key[which & 1];
}
