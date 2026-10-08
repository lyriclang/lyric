/* The operating system's facts (design/v5/spec/10 Q9): M8b S1, the processors. */
#ifndef LYR_OS_H
#define LYR_OS_H

#include <stdint.h>

/* The processors the system runs this process on — those online —, one at least. */
int64_t lyr_os_cpu_count(void);

/* The system the program was built for (M8b S6a, for std.path's separators; S9's os.platform):
 * 1 Linux, 2 macOS, 3 Windows, 0 another POSIX system. */
int64_t lyr_os_platform(void);

#endif
