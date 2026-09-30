/* Measurement point 2, 'structs': the C twin of structs.lyr; -ffp-contract=off like the Lyric. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

typedef struct { double x, y, vx, vy; } Particle;

int main(void) {
    const int count = 100000, steps = 2000;
    const double dt = 0.01;
    Particle *ps = malloc(sizeof(Particle) * count);
    for (int i = 0; i < count; i++) {
        ps[i].x = (double)i;
        ps[i].y = (double)i * 2.0;
        ps[i].vx = 1.0;
        ps[i].vy = -0.5;
    }
    for (int s = 0; s < steps; s++) {
        for (int i = 0; i < count; i++) {
            ps[i].x = ps[i].x + ps[i].vx * dt;
            ps[i].y = ps[i].y + ps[i].vy * dt;
            ps[i].vx = ps[i].vx - ps[i].y * 0.001 * dt;
            ps[i].vy = ps[i].vy + ps[i].x * 0.001 * dt;
        }
    }
    double sum = 0.0;
    for (int i = 0; i < count; i++) sum = sum + ps[i].x + ps[i].y;
    printf("%lld\n", (long long)sum);
    free(ps);
    return 0;
}
