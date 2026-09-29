// 11 Vergleich: Go — Labels `outer:` vor der Schleife, `break outer` / `continue outer`.
// Dieselbe Syntax wie der Lyric-Vorschlag (Identifier + Doppelpunkt, eigener Namensraum).
// Rust: `'outer: for … { break 'outer; }`; Kotlin: `outer@ for … { break@outer }`.
package main

import "fmt"

func main() {
	grid := [][]int{{1, 2, 3}, {4, 5, 6}, {7, 8, 9}}
	wanted := 5
	fi, fj := -1, -1
outer:
	for i := range grid {
		for j := range grid[i] {
			if grid[i][j] == wanted {
				fi, fj = i, j
				break outer
			}
		}
	}
	fmt.Printf("found at (%d, %d)\n", fi, fj)

	rows := [][]int{{1, 2}, {3, -1, 4}, {5}}
	sum := 0
rows:
	for _, row := range rows {
		rowSum := 0
		for _, v := range row {
			if v < 0 {
				continue rows
			}
			rowSum += v
		}
		sum += rowSum
	}
	fmt.Printf("sum %d\n", sum)
}
// Ausgabe:
// found at (1, 1)
// sum 8
