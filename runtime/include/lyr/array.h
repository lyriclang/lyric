/* Arrays in C, as far as design/v5/spec/01 V10 and 03 T13 put them there: the layout is in
 * types.h, the allocation in gc.h, the index check in panic.h. What remains are the two
 * operators that build a new array from old ones (10 C7): concatenation and repetition, both by
 * copying elements as bytes — a struct element is copied whole, a reference element as the
 * reference, which is what a copy of the value means (02 M3). Every other member of T[] is
 * Lyric (std.core). */
#ifndef LYR_ARRAY_H
#define LYR_ARRAY_H

#include "lyr/types.h"

/* A new array of `desc` holding the elements of `a` then those of `b`. Both are arrays of
 * `desc`'s element size. */
LyrArr *lyr_arr_concat(const LyrDesc *desc, const LyrArr *a, const LyrArr *b);

/* A new array of `desc` holding the elements of `array` `count` times over. A negative count
 * panics with LYR-RT0007; a size that does not fit with LYR-RT0005. */
LyrArr *lyr_arr_repeat(const LyrDesc *desc, const LyrArr *array, int64_t count);

#endif
