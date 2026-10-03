// Measurement point 3, 'maps': the Go twin of maps.lyr — Go's map (a Swiss table since 1.24,
// its hash seeded per process).
package main

import "fmt"

func main() {
	m := make(map[int]int)
	var x uint64 = 88172645463325252
	for i := 0; i < 1000000; i++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		m[int(x%4000000)] = i
	}
	hits, total := 0, 0
	for k := 0; k < 4000000; k += 2 {
		if v, ok := m[k]; ok {
			hits++
			total += v
		}
	}
	fmt.Printf("%d %d %d\n", len(m), hits, total)
}
