// Measurement point 5, 'cache': the Go twin of cache.lyr — a map of pointers to entries, each with
// a slice of twenty-four ints.
package main

import (
	"fmt"
	"math"
	"runtime/metrics"
)

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
	// the numbers count the steady phase: a gibibyte alive (13 M11 P12)
	before = readPauses()
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
	fmt.Println(gcLine())
}

// gcLine is the collector's numbers as the Lyric twin prints them: the cycles, the 99th percentile
// and the longest of the stop-the-world pauses, in nanoseconds (runtime/metrics' histogram, its
// buckets' upper bounds).
func gcLine() string {
	now := readPauses()
	cycles := now.cycles - before.cycles
	h := now.histogram
	counts := make([]uint64, len(h.Counts))
	var total, seen uint64
	for i, c := range h.Counts {
		if before.histogram != nil {
			c -= before.histogram.Counts[i]
		}
		counts[i] = c
		total += c
	}
	p99, max := 0.0, 0.0
	for i, c := range counts {
		if c == 0 {
			continue
		}
		seen += c
		upper := h.Buckets[i+1]
		if math.IsInf(upper, 1) {
			upper = h.Buckets[i]
		}
		max = upper
		if p99 == 0 && seen*100 >= total*99 {
			p99 = upper
		}
	}
	return fmt.Sprintf("gc %d %d %d", cycles, int64(p99*1e9), int64(max*1e9))
}

// The pauses and cycles so far: read before the steady phase, and the difference printed after it.
type pauses struct {
	cycles    uint64
	histogram *metrics.Float64Histogram
}

var before pauses

func readPauses() pauses {
	samples := []metrics.Sample{{Name: "/gc/cycles/total:gc-cycles"}, {Name: "/sched/pauses/total/gc:seconds"}}
	metrics.Read(samples)
	return pauses{samples[0].Value.Uint64(), samples[1].Value.Float64Histogram()}
}
