/* The system the program was built for (lyr/os.h): lyr_os_platform names the one the C compiler
 * targets. Expected: "platform ok", exit 0. */
#include "lyr/lyr.h"
#include "check.h"

int main(void) {
#if defined(_WIN32)
    CHECK(lyr_os_platform() == 3);
#elif defined(__APPLE__)
    CHECK(lyr_os_platform() == 2);
#elif defined(__linux__)
    CHECK(lyr_os_platform() == 1);
#else
    CHECK(lyr_os_platform() == 0);
#endif
    CHECK(lyr_os_cpu_count() >= 1);
    fputs("platform ok\n", stdout);
    return 0;
}
