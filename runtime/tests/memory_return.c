/* Memory given back (13 M11 P7, S8): a program builds a quarter gibibyte — small objects in blocks,
 * large ones in pages, two huge ones in chunks of their own —, lets it go and collects: the
 * memory it holds falls back to a fraction of what it was. Under Boehm, which keeps what it took,
 * only the program's run is checked. Prints "memory ok" and, on stderr, the two sizes in MiB.
 *
 * On macOS the measure is the footprint, not the resident set: the system compresses pages that
 * sit still, and a compressed page leaves the resident set while the process still holds it — the
 * half gibibyte it once built read as 318 MiB on a runner short of memory. Half a gibibyte was also
 * enough to push those runners into swapping, and the program past its two minutes there four
 * times: a quarter says the same, the gap between built and given back still wide. */
#include "lyr/lyr.h"
#include "check.h"
#include "conservative.h"

#include <stdio.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#  include <psapi.h>
#elif defined(__APPLE__)
#  include <mach/mach.h>
#else
#  include <unistd.h>
#endif

static size_t resident(void) {
#if defined(_WIN32)
    PROCESS_MEMORY_COUNTERS counters;
    if (!K32GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof counters)) return 0;
    return counters.WorkingSetSize;
#elif defined(__APPLE__)
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) != KERN_SUCCESS) return 0;
    return (size_t)info.phys_footprint;
#else
    FILE *statm = fopen("/proc/self/statm", "r");
    if (statm == NULL) return 0;
    unsigned long size = 0, pages = 0;
    int read = fscanf(statm, "%lu %lu", &size, &pages);
    fclose(statm);
    return read == 2 ? pages * (size_t)sysconf(_SC_PAGESIZE) : 0;
#endif
}

typedef struct Box { LyrObj header; struct Box *next; int64_t value; } Box;
static const uint64_t box_refs[] = { 1u << 1 };
static const LyrDesc box_desc = { .size = sizeof(Box), .flags = LYR_DESC_HAS_REFS, .refmap_words = 1,
                                  .refmap = box_refs, .name = "test.Box" };
static const LyrDesc bytes_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY,
                                    .elem_size = 1, .name = "test.Bytes" };
static const uint64_t element_refs[] = { 1u };
static const LyrDesc refs_desc = { .size = (uint32_t)offsetof(LyrArr, data), .flags = LYR_DESC_ARRAY | LYR_DESC_HAS_REFS,
                                   .elem_size = sizeof(void *), .refmap_words = 1, .refmap = element_refs,
                                   .name = "test.Object[]" };

enum { SMALL = 96 << 20, LARGE = 64 << 10, LARGES = (96 << 20) / LARGE, HUGE = 32 << 20 };

static LyrRoot *small_root, *large_root, *huge_root;

NOINLINE static void build(void) {
    Box *head = NULL;
    for (size_t made = 0; made < SMALL; made += sizeof(Box)) {
        Box *box = lyr_alloc(&box_desc);
        box->value = (int64_t)made;
        LYR_WRITE_BARRIER(box, &box->next, head);
        head = box;
    }
    small_root = lyr_root_new(head);
    LyrArr *larges = lyr_alloc_array(&refs_desc, LARGES);
    large_root = lyr_root_new(larges);
    for (int i = 0; i < LARGES; i++) {
        LyrArr *bytes = lyr_alloc_array(&bytes_desc, LARGE);
        memset(bytes->data, 1, LARGE);
        LYR_WRITE_BARRIER(larges, &LYR_ARR_DATA(larges, LyrArr *)[i], bytes);
    }
    LyrArr *huges = lyr_alloc_array(&refs_desc, 2);
    huge_root = lyr_root_new(huges);
    for (int i = 0; i < 2; i++) {
        LyrArr *bytes = lyr_alloc_array(&bytes_desc, HUGE);
        memset(bytes->data, 1, HUGE);
        LYR_WRITE_BARRIER(huges, &LYR_ARR_DATA(huges, LyrArr *)[i], bytes);
    }
}

static int64_t program(void) {
    build();
    lyr_gc_collect();
    size_t built = resident();
    lyr_root_set(small_root, NULL);
    lyr_root_set(large_root, NULL);
    lyr_root_set(huge_root, NULL);
    wipe_stack();
    lyr_gc_collect();
    lyr_gc_collect();
    size_t after = resident();
    fprintf(stderr, "resident: %zu MiB built, %zu MiB after\n", built >> 20, after >> 20);
    CHECK(built > (size_t)200 << 20);
#ifndef LYR_GC_BOEHM
    CHECK(after < built / 4);
#endif
    lyr_println(lyr_str_from_cstr("memory ok"));
    return 0;
}

int main(int argc, char **argv) { return lyr_run_main(argc, argv, program); }
