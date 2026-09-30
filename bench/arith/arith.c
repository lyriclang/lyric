/* Measurement point 2, 'arith': the C twin of arith.lyr; built with -ffp-contract=off like the Lyric. */
#include <stdio.h>

int main(void) {
    const int width = 2000, height = 2000, limit = 200;
    long inside = 0;
    for (int py = 0; py < height; py++) {
        for (int px = 0; px < width; px++) {
            double cx = -2.0 + (double)px * (3.0 / (double)width);
            double cy = -1.5 + (double)py * (3.0 / (double)height);
            double x = 0.0, y = 0.0;
            int i = 0;
            while (i < limit && x * x + y * y <= 4.0) {
                double xt = x * x - y * y + cx;
                y = 2.0 * x * y + cy;
                x = xt;
                i++;
            }
            if (i == limit) inside++;
        }
    }
    printf("%ld\n", inside);
    return 0;
}
