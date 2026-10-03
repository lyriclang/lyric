// Measurement point 3, 'sorting': the Go twin of sorting.lyr — slices.Sort (pdqsort).
package main

import (
	"fmt"
	"slices"
)

func main() {
	n := 1000000
	xs := make([]int, n)
	var x uint64 = 88172645463325252
	for i := 0; i < n; i++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		xs[i] = int(x % 1000000000)
	}
	slices.Sort(xs)
	check := 0
	for i := 0; i < n; i++ {
		check = (check*31 + xs[i]) % 1000000007
	}
	fmt.Printf("%d %d %d %d\n", xs[0], xs[n/2], xs[n-1], check)
}
