// 18 Vergleich: Kotlin — `Array(n) { i -> f(i) }` ist ein Konstruktor mit Generator-Lambda;
// `IntArray(n)` füllt mit dem Nullwert; `List(n) { … }` dito für Listen. Kein Dummy, kein
// Sonderfall für n == 0, f läuft genau n-mal. Das ist Form A des Vorschlags (`arrayOf(n, f)`).
// Rust: `vec![x; n]` (wie Lyric `[x] * n`) oder `(0..n).map(f).collect()`; Go `make([]T, n)`
// (Nullwert — Lyric hat keinen); C# `new T[n]` (default — Lyric hat keinen).

data class Slot(val id: Int, val used: Boolean)

fun <T, U> mapArr(xs: Array<T>, f: (T) -> U): List<U> = List(xs.size) { i -> f(xs[i]) }

fun main() {
    val doubled = mapArr(arrayOf(1, 2, 3)) { it * 2 }
    println("${doubled[0]} ${doubled[1]} ${doubled[2]}")

    val squares = IntArray(5) { i -> i * i }
    println(squares[4])

    val slots = Array(3) { i -> Slot(i, false) }        // kein Dummy-Slot
    println(slots[2].id)

    val cubes = IntArray(4) { i -> i * i * i }
    println(cubes[3])
}
// Ausgabe:
// 2 4 6
// 16
// 2
// 27
