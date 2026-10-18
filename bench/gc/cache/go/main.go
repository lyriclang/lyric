// Measurement point 5, 'cache': the Go twin of cache.lyr — a map of pointers to entries, each with
// a slice of twenty-four ints.
package main

import "fmt"

type Entry struct {
	id   int
	data []int
}

func entry(id, seed int) *Entry {
	data := make([]int, 24)
	for j := range data {
		data[j] = seed + j
	}
	return &Entry{id, data}
}

func main() {
	entries := 4000000
	cache := make(map[int]*Entry)
	for i := 0; i < entries; i++ {
		cache[i] = entry(i, i)
	}
	var x uint64 = 88172645463325252
	check := 0
	for op := 0; op < 8000000; op++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		key := int(x % 4000000)
		scratch := make([]int, 8)
		for j := range scratch {
			scratch[j] = op
		}
		check = (check + scratch[op%8] + key) % 1000000007
		if op%2 == 0 {
			cache[key] = entry(key, op)
		} else if e, ok := cache[key]; ok {
			check = (check + e.id + e.data[op%24]) % 1000000007
		}
	}
	fmt.Printf("%d %d\n", len(cache), check)
}
