// Measurement point 4, 'regex': the Go twin of regex.lyr — the same 300 000 lines through
// Go's regexp (RE2 syntax, linear time, like std.regex).
package main

import (
	"fmt"
	"regexp"
	"strings"
)

func main() {
	var sb strings.Builder
	levels := []string{"INFO", "WARN", "ERROR", "DEBUG"}
	var x uint64 = 88172645463325252
	for i := 0; i < 300000; i++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		fmt.Fprintf(&sb, "2026-10-14T12:00:%d %s request %d took %dms\n", i%60, levels[x%4], x%1000, x%5000)
	}
	text := sb.String()
	severe := regexp.MustCompile(`\b(ERROR|WARN)\b`)
	slow := regexp.MustCompile(`\s[1-9]\d{3,}ms$`)
	severeCount, slowCount, both := 0, 0, 0
	for _, line := range strings.Split(strings.TrimSuffix(text, "\n"), "\n") {
		isSevere := severe.MatchString(line)
		isSlow := slow.MatchString(line)
		if isSevere {
			severeCount++
		}
		if isSlow {
			slowCount++
		}
		if isSevere && isSlow {
			both++
		}
	}
	fmt.Printf("%d %d %d\n", severeCount, slowCount, both)
}
