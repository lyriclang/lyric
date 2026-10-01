/* Measurement point 2, 'loops': the C twin of loops.lyr, unchecked arithmetic. */
#include <stdio.h>
#include <stdint.h>

int main(void) {
    int64_t acc = 0;
    for (int64_t i = 0; i < 20000; i++)
        for (int64_t j = 0; j < 10000; j++)
            acc = (acc + i * j) % 1000000007;
    printf("%lld\n", (long long)acc);
    return 0;
}
