// Measurement point 5, 'lru': the Go twin of lru.lyr — the same list over a map of pointers.
package main

import (
	"fmt"
	"math"
	"runtime/metrics"
)

type Node struct {
	key, value int
	prev, next *Node
}

type Lru struct {
	m          map[int]*Node
	head, tail *Node
}

func (lru *Lru) unlink(node *Node) {
	if node.prev != nil {
		node.prev.next = node.next
	} else {
		lru.head = node.next
	}
	if node.next != nil {
		node.next.prev = node.prev
	} else {
		lru.tail = node.prev
	}
	node.prev = nil
	node.next = nil
}

func (lru *Lru) pushFront(node *Node) {
	node.next = lru.head
	if lru.head != nil {
		lru.head.prev = node
	}
	lru.head = node
	if lru.tail == nil {
		lru.tail = node
	}
}

func main() {
	capacity := 200000
	lru := &Lru{m: make(map[int]*Node)}
	var x uint64 = 88172645463325252
	hits, total := 0, 0
	for i := 0; i < 10000000; i++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		key := int(x % 400000)
		if node, ok := lru.m[key]; ok {
			hits++
			total += node.value
			lru.unlink(node)
			lru.pushFront(node)
			node.value = i
		} else {
			node := &Node{key: key, value: i}
			lru.m[key] = node
			lru.pushFront(node)
			if len(lru.m) > capacity {
				last := lru.tail
				lru.unlink(last)
				delete(lru.m, last.key)
			}
		}
	}
	fmt.Printf("%d %d %d\n", len(lru.m), hits, total)
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
