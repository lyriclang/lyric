/* The system's random bytes and the process's hash key (lyr/random.h).
 * Expected: two draws differ, a buffer of 33 is filled, the key's halves differ and each is the
 * same at every call — "random ok", exit 0. */
#include "lyr/lyr.h"
#include "check.h"

int main(void) {
    uint64_t a = lyr_random_u64();
    uint64_t b = lyr_random_u64();
    CHECK(a != b);

    unsigned char buffer[33] = { 0 };
    lyr_random_bytes(buffer, sizeof buffer);
    int seen = 0;
    for (size_t i = 0; i < sizeof buffer; i++) seen |= buffer[i];
    CHECK(seen != 0);

    uint64_t k0 = lyr_hash_key(0);
    uint64_t k1 = lyr_hash_key(1);
    CHECK(k0 != k1);
    CHECK(lyr_hash_key(0) == k0);
    CHECK(lyr_hash_key(1) == k1);

    fputs("random ok\n", stdout);
    return 0;
}
