// Measurement point 2, 'loops': the Go twin of loops.lyr.
package main

import "fmt"

func main() {
	var acc int64 = 0
	for i := int64(0); i < 20000; i++ {
		for j := int64(0); j < 10000; j++ {
			acc = (acc + i*j) % 1000000007
		}
	}
	fmt.Println(acc)
}
