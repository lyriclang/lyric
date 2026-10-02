/* Coroutines and the collector (01 L1, L4; 06 A5).
 * Expected: sixteen coroutines each build a chain of objects referenced only from their own
 * stacks, a hundred nodes over ten rounds with a yield after every round and collections between
 * the rounds; every chain is whole at the end. Then two hundred coroutines are started, suspended
 * holding a large array each, and dropped: collections take them with their stacks, and nothing
 * on those stacks runs again (a coroutine that ran on would fail its check). A few may survive as
 * stale words the scan takes for references. Prints "coro gc ok". */
#include "lyr/lyr.h"
#include "check.h"

static const LyrDesc coroutine_desc = { .size = 0, .name = "test.Coroutine" };

typedef struct Node { LyrObj header; struct Node *next; int64_t value; } Node;
static const uint64_t node_refs[] = { 1u << 1 };
static const LyrDesc node_desc = { .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS,
                                   .refmap_words = 1, .refmap = node_refs, .name = "test.Node" };
static const LyrDesc bytes_desc = { .size = 0, .elem_size = 1, .name = "test.Bytes" };

enum { KEEPERS = 16, ROUNDS = 10, PER_ROUND = 1000, KEEP_EVERY = 100, ABANDONED = 200 };

static void keeper_body(void *arg) {
    int64_t id = (int64_t)(intptr_t)arg;
    Node *head = NULL;
    for (int64_t round = 0; round < ROUNDS; round++) {
        for (int64_t i = 0; i < PER_ROUND; i++) {
            Node *node = lyr_alloc(&node_desc);
            node->value = id * 1000000 + round * PER_ROUND + i;
            if (i % KEEP_EVERY == 0) {
                LYR_WRITE_BARRIER(node, &node->next, head);
                head = node;
            }
        }
        lyr_coro_yield();  /* the chain lives on this stack alone */
    }
    int count = 0;
    int64_t expected = id * 1000000 + (ROUNDS - 1) * PER_ROUND + (PER_ROUND - KEEP_EVERY);
    for (Node *node = head; node != NULL; node = node->next) {
        CHECK(node->value == expected);
        expected -= KEEP_EVERY;
        count++;
    }
    CHECK(count == ROUNDS * PER_ROUND / KEEP_EVERY);
}

static void keepers(void) {
    LyrCoro *coroutines[KEEPERS];
    for (int i = 0; i < KEEPERS; i++) {
        coroutines[i] = lyr_coro_new(&coroutine_desc, keeper_body, (void *)(intptr_t)i, 0);
    }
    for (int round = 0; round <= ROUNDS; round++) {
        for (int i = 0; i < KEEPERS; i++) lyr_coro_resume(coroutines[i]);
        lyr_gc_collect();
    }
    for (int i = 0; i < KEEPERS; i++) CHECK(lyr_coro_status(coroutines[i]) == LYR_CORO_DONE);
    CHECK(lyr_coro_stacks() == 0);
}

static void holder_body(void *arg) {
    (void)arg;
    LyrArr *held = lyr_alloc_array(&bytes_desc, 64 * 1024);
    lyr_coro_yield();
    CHECK(held == NULL);  /* never: a dropped coroutine does not run on */
}

static __attribute__((noinline)) void abandon(void) {
    for (int i = 0; i < ABANDONED; i++) {
        LyrCoro *co = lyr_coro_new(&coroutine_desc, holder_body, NULL, 0);
        lyr_coro_resume(co);
    }
}

static int64_t program(void) {
    keepers();
    abandon();  /* collections may already take some of them while it allocates */
    for (int i = 0; i < 4; i++) lyr_gc_collect();
    CHECK(lyr_coro_stacks() < ABANDONED / 4);
    lyr_println(lyr_str_from_cstr("coro gc ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
