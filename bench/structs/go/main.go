// Measurement point 2, 'structs': the Go twin of structs.lyr — a slice of structs, in place.
package main

import "fmt"

type Particle struct{ x, y, vx, vy float64 }

func main() {
	const count, steps = 100000, 2000
	const dt = 0.01
	ps := make([]Particle, count)
	for i := 0; i < count; i++ {
		ps[i] = Particle{float64(i), float64(i) * 2.0, 1.0, -0.5}
	}
	for s := 0; s < steps; s++ {
		for i := 0; i < count; i++ {
			ps[i].x = ps[i].x + ps[i].vx*dt
			ps[i].y = ps[i].y + ps[i].vy*dt
			ps[i].vx = ps[i].vx - ps[i].y*0.001*dt
			ps[i].vy = ps[i].vy + ps[i].x*0.001*dt
		}
	}
	sum := 0.0
	for i := 0; i < count; i++ {
		sum = sum + ps[i].x + ps[i].y
	}
	fmt.Println(int64(sum))
}
