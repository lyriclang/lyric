// Measurement point 5, 'binarytrees': the Go twin of binarytrees.lyr — the same trees on one
// goroutine, Go's collector concurrent beside it.
package main

import "fmt"

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
}
