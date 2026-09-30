/* The object model of design/v5/spec/01 L2: every heap object starts with one word, a pointer to
 * its type descriptor (V3); the descriptor is static and says how big the object is and which of
 * its words are references (V4). Strings and arrays carry their length after the header (V9, V10).
 * Type identity is the descriptor's address. */
#ifndef LYR_TYPES_H
#define LYR_TYPES_H

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

/* LyrDesc.flags */
enum {
    LYR_DESC_HAS_REFS = 1u << 0, /* some word of the object (or of an array element) is a reference */
    LYR_DESC_ARRAY    = 1u << 1, /* length-prefixed: size is the fixed part, elem_size the element */
    LYR_DESC_STRING   = 1u << 2, /* the string layout below */
    LYR_DESC_CONSERVATIVE = 1u << 3, /* the refmap does not tell all: some word is a reference only in
                                        some states of the object — a union under a tag (01 V6). A
                                        collector that reads the map scans this object as it scans a
                                        stack, word by word; nothing moves, so that is sound */
};

/* The tag of an absent `?Enum` (01 V5): the optional is the enum itself, with a tag no variant has. */
#define LYR_ENUM_NONE UINT32_MAX

typedef struct LyrDesc {
    uint32_t size;               /* bytes of a fixed object, header included; for arrays/strings the fixed part */
    uint32_t flags;
    uint32_t elem_size;          /* arrays: bytes per element */
    uint32_t refmap_words;       /* 64-bit words in refmap */
    const uint64_t *refmap;      /* bit i set: pointer-sized word i is a reference — for a fixed object,
                                    word i of the object (word 0 is the header; its bit stays clear);
                                    for an array, word i of one element */
    const char *name;            /* "module.Type", for backtraces, debuggers and Debug */
    const void *itables;         /* interface tables (M4) */
} LyrDesc;

/* The header every heap object starts with. */
typedef struct LyrObj {
    const LyrDesc *desc;
} LyrObj;

/* A string: immutable UTF-8, length in bytes (10 S1), NUL-terminated so it crosses to C as a
 * `const char *` without a copy (11 X3). */
typedef struct LyrStr {
    LyrObj header;
    int64_t len;
    char bytes[];                /* len bytes, then '\0' */
} LyrStr;

/* An array: fixed length, elements inline and contiguous, aligned for any scalar or struct. */
typedef struct LyrArr {
    LyrObj header;
    int64_t len;
    _Alignas(16) unsigned char data[];
} LyrArr;

#define LYR_ARR_DATA(array, T) ((T *)(array)->data)

/* The descriptors the runtime owns; the compiler emits one per user type. */
extern const LyrDesc lyr_desc_string;

/* A string literal as a static object: `static const LyrStaticStr(sizeof("hi")) s = LYR_STR_INIT("hi");`
 * then `(LyrStr *)&s`. Static objects are never collected and never written. */
#define LyrStaticStr(capacity) struct { LyrObj header; int64_t len; char bytes[capacity]; }
#define LYR_STR_INIT(literal) { { &lyr_desc_string }, (int64_t)(sizeof(literal) - 1), literal }

#endif
