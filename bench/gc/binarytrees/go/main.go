// Measurement point 5, 'binarytrees': the Go twin of binarytrees.lyr — the same trees on one
// goroutine, Go's collector concurrent beside it.
package main

import (
	"fmt"
	"math"
	"runtime/metrics"
)

type Node struct{ left, right *Node }

func bottomUp(depth int) *Node {
	if depth <= 0 {
		return &Node{}
	}
	return &Node{bottomUp(depth - 1), bottomUp(depth - 1)}
}

func check(node *Node) int {
	count := 1
	if node.left != nil {
		count += check(node.left)
	}
	if node.right != nil {
		count += check(node.right)
	}
	return count
}

func main() {
	maxDepth := 18
	total := check(bottomUp(maxDepth + 1))
	longLived := bottomUp(maxDepth)
	for depth := 4; depth <= maxDepth; depth += 2 {
		iterations := 1 << (maxDepth - depth + 4)
		for i := 0; i < iterations; i++ {
			total += check(bottomUp(depth))
		}
	}
	total += check(longLived)
	fmt.Printf("%d\n", total)
	fmt.Println(gcLine())
}

// gcLine is the collector's numbers as the Lyric twin prints them: the cycles, the 99th percentile
// and the longest of the stop-the-world pauses, in nanoseconds (runtime/metrics' histogram, its
// buckets' upper bounds).
func gcLine() string {
	samples := []metrics.Sample{{Name: "/gc/cycles/total:gc-cycles"}, {Name: "/sched/pauses/total/gc:seconds"}}
	metrics.Read(samples)
	cycles := samples[0].Value.Uint64()
	h := samples[1].Value.Float64Histogram()
	var total, seen uint64
	for _, c := range h.Counts {
		total += c
	}
	p99, max := 0.0, 0.0
	for i, c := range h.Counts {
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
