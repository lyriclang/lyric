/* The runtime tests' one assertion: on failure, say where and what, and exit 1. */
#ifndef LYR_TEST_CHECK_H
#define LYR_TEST_CHECK_H

#include <stdio.h>
#include <stdlib.h>

#define CHECK(condition)                                                                   \
    do {                                                                                   \
        if (!(condition)) {                                                                \
            fprintf(stderr, "%s:%d: check failed: %s\n", __FILE__, __LINE__, #condition); \
            exit(1);                                                                       \
        }                                                                                  \
    } while (0)

#endif
