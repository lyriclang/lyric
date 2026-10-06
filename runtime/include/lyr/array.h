/* Arrays in C, as far as design/v5/spec/01 V10 and 03 T13 put them there: the layout is in
 * types.h, the allocation in gc.h, the index check in panic.h. What remains are the two
 * operators that build a new array from old ones (10 C7): concatenation and repetition, both by
 * copying elements as bytes — a struct element is copied whole, a reference element as the
 * reference, which is what a copy of the value means (02 M3). Every other member of T[] is
 * Lyric (std.core). */
#ifndef LYR_ARRAY_H
#define LYR_ARRAY_H

#include "lyr/types.h"

#include <string.h>  /* memmove, for LYR_SLICE_MOVE */

/* A new array of `desc` holding the elements of `a` then those of `b`. Both are arrays of
 * `desc`'s element size. */
LyrArr *lyr_arr_concat(const LyrDesc *desc, const LyrArr *a, const LyrArr *b);

/* A new array of `desc` holding the elements of `array` `count` times over. A negative count
 * panics with LYR-RT0007; a size that does not fit with LYR-RT0005. */
LyrArr *lyr_arr_repeat(const LyrDesc *desc, const LyrArr *array, int64_t count);

/* The copy primitive (the review's B16): the first `count` elements of the view `from` into the
 * view `into`, both `lyr_slice_<T>` of one element type (a pointer into an array and a length,
 * CEmitter) — memmove, so the two may overlap either way: a struct element is copied whole, a
 * reference element as the reference (02 M3). The counts are the library's to check
 * (std.core's `moveElements` callers; `Slice.copyInto` panics with LYR-RT0003). The one place a
 * write barrier for a span of references would go (M11): the collector of this stage needs none. */
#define LYR_SLICE_MOVE(into, from, count) \
    memmove((into).ptr, (from).ptr, (size_t)(count) * sizeof(*(into).ptr))

#endif
