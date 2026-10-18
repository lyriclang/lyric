// Measurement point 5, 'lru': the Go twin of lru.lyr — the same list over a map of pointers.
package main

import "fmt"

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
}
