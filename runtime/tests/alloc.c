/* Allocation (01 L1/L2).
 * Expected: fixed objects come back zeroed with their header set, whether or not their descriptor
 * has references; 10 M short-lived objects with references (a linked chain of 100 kept alive,
 * the rest dropped) keep the heap under 64 MiB with collections running; the kept chain is intact
 * after an explicit collection. Prints "alloc ok", exit 0. */
#include "lyr/lyr.h"
#include "check.h"

#include <string.h>

typedef struct Node { LyrObj header; struct Node *next; int64_t value; } Node;
static const uint64_t node_refs[] = { 1u << 1 };  /* slot 1: next (slot 0 is the header) */
static const LyrDesc node_desc = { .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS,
                                   .refmap_words = 1, .refmap = node_refs, .name = "test.Node" };

typedef struct Point { LyrObj header; int64_t x, y; } Point;
static const LyrDesc point_desc = { .size = sizeof(Point), .name = "test.Point" };

static int64_t program(void) {
    Point *p = lyr_alloc(&point_desc);
    CHECK(p->header.desc == &point_desc);
    CHECK(p->x == 0 && p->y == 0);
    p->x = 3;

    Node *n = lyr_alloc(&node_desc);
    CHECK(n->header.desc == &node_desc);
    CHECK(n->next == NULL && n->value == 0);

    /* A chain of 100 survivors among 10 M temporaries. */
    Node *head = NULL;
    for (int64_t i = 0; i < 10000000; i++) {
        Node *t = lyr_alloc(&node_desc);
        t->value = i;
        if (i % 100000 == 0) {
            LYR_WRITE_BARRIER(t, &t->next, head);
            head = t;
        }
    }
    CHECK(lyr_gc_heap_size() < 64u * 1024 * 1024);
    CHECK(lyr_gc_collections() > 0);

    lyr_gc_collect();
    int64_t count = 0, expected = 9900000;
    for (Node *c = head; c; c = c->next, expected -= 100000) {
        CHECK(c->value == expected);
        count++;
    }
    CHECK(count == 100);
    CHECK(p->x == 3);

    lyr_println(lyr_str_from_cstr("alloc ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
