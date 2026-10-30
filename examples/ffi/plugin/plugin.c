/* A shared library the example loads while it runs, not linked into it:
 *
 *     zig cc -shared plugin/plugin.c -o libplugin.so      (.dylib on macOS, plugin.dll on Windows)
 *     out/debug/<target>/geometry ./libplugin.so
 */
#include <stdint.h>

#if defined(_WIN32)
#  define PLUGIN_API __declspec(dllexport)
#else
#  define PLUGIN_API __attribute__((visibility("default")))
#endif

PLUGIN_API const char *plugin_name(void) { return "triple"; }

PLUGIN_API int64_t plugin_apply(int64_t x) { return 3 * x; }
