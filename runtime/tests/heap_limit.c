/* The heap limit (11 H1 `heap_limit`): allocation beyond it is a panic, not a crash or a hang.
 * Expected: objects that stay reachable fill a 32 MiB heap; the allocation that does not fit
 * panics — exit 101, stderr starts with "panic [LYR-RT0005]: out of memory allocating 1016 bytes"
 * and names the heap size; nothing else from the collector reaches stderr. */
#include "lyr/lyr.h"

#include <string.h>

typedef struct Node {
    LyrObj header;
    struct Node *next;
    char payload[1000];
} Node;

static const uint64_t node_refs[] = { 1u << 1 };  /* word 1: next */
static const LyrDesc node_desc = {
    .size = sizeof(Node), .flags = LYR_DESC_HAS_REFS, .refmap_words = 1, .refmap = node_refs, .name = "test.Node",
};

int main(int argc, char **argv) {
    LyrConfig config;
    memset(&config, 0, sizeof config);
    config.argc = argc;
    config.argv = argv;
    config.heap_limit = 32u << 20;
    config.install_signal_handlers = 1;
    lyr_init(&config);

    LyrRoot *head = lyr_root_new(NULL);
    for (;;) {
        Node *node = lyr_alloc(&node_desc);
        node->next = lyr_root_get(head);
        lyr_root_set(head, node);
    }
}
