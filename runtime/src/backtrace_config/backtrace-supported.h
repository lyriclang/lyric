/* What libbacktrace's configure script would have found (runtime/THIRD_PARTY.md). Windows reads
 * files through malloc (alloc.c, read.c); the others map them (mmap.c, mmapio.c). */
#define BACKTRACE_SUPPORTED 1
#ifdef _WIN32
#  define BACKTRACE_USES_MALLOC 1
#else
#  define BACKTRACE_USES_MALLOC 0
#endif
#define BACKTRACE_SUPPORTS_THREADS 1
#define BACKTRACE_SUPPORTS_DATA 1
#define BACKTRACE_SUPPORTS_MOREDATA 1
