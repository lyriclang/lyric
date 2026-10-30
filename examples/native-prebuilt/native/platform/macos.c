#include <stdint.h>
#include <CoreFoundation/CoreFoundation.h>

/* The text's length in UTF-16 units, as CoreFoundation counts it. */
int64_t text_units(const char *text) {
    CFStringRef string = CFStringCreateWithCString(kCFAllocatorDefault, text, kCFStringEncodingUTF8);
    if (string == NULL) return -1;
    int64_t units = (int64_t)CFStringGetLength(string);
    CFRelease(string);
    return units;
}

const char *text_counter(void) { return "CoreFoundation"; }
