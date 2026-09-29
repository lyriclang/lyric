/* Two faults a sanitizer must report: mode 1 writes past a heap block (ASan), mode 2 overflows a
 * signed integer (UBSan). Mode 0 is clean. The stores are volatile so no optimizer removes them. */
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
    volatile int *p = malloc(4 * sizeof(int));
    int mode = argc > 1 ? atoi(argv[1]) : 0;
    if (mode == 1) { p[4] = 1; }
    if (mode == 2) { volatile int x = 2147483647; x += argc; printf("%d\n", x); }
    p[0] = mode;
    printf("clean %d\n", p[0]);
    free((void *)p);
    return 0;
}
