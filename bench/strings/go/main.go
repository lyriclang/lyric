// Measurement point 3, 'strings': the Go twin of strings.lyr — strings.Builder, strconv,
// strings.Cut and strings.Contains.
package main

import (
	"fmt"
	"strconv"
	"strings"
)

func main() {
	var sb strings.Builder
	for i := 0; i < 300000; i++ {
		sb.WriteString("item")
		sb.WriteString(strconv.Itoa(i))
		sb.WriteByte(',')
		sb.WriteString(strconv.Itoa((i * 7919) % 100000))
		sb.WriteByte('\n')
	}
	text := sb.String()
	lines, named, total := 0, 0, 0
	for len(text) > 0 {
		line := text
		if at := strings.IndexByte(text, '\n'); at >= 0 {
			line, text = text[:at], text[at+1:]
		} else {
			text = ""
		}
		if name, value, ok := strings.Cut(line, ","); ok {
			if strings.Contains(name, "99") {
				named++
			}
			if v, err := strconv.Atoi(value); err == nil {
				total += v
			}
		}
		lines++
	}
	fmt.Printf("%d %d %d\n", lines, named, total)
}
