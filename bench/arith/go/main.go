// Measurement point 2, 'arith': the Go twin of arith.lyr (Go on amd64 fuses nothing by itself).
package main

import "fmt"

func main() {
	const width, height, limit = 2000, 2000, 200
	inside := 0
	for py := 0; py < height; py++ {
		for px := 0; px < width; px++ {
			cx := -2.0 + float64(px)*(3.0/float64(width))
			cy := -1.5 + float64(py)*(3.0/float64(height))
			x, y := 0.0, 0.0
			i := 0
			for i < limit && x*x+y*y <= 4.0 {
				xt := x*x - y*y + cx
				y = 2.0*x*y + cy
				x = xt
				i++
			}
			if i == limit {
				inside++
			}
		}
	}
	fmt.Println(inside)
}
