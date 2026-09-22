// 08 Vergleich: Kotlin — `throw` ist ein Ausdruck vom Typ `Nothing`, der mit jedem Typ
// unifiziert. Das Idiom `x ?: throw …` ist Standard; `when`-Zweig und if-else ebenso.
// (Rust: `panic!()`/`return`/`break` sind `!`-typisiert; C# 7: throw-Ausdrücke in `??`/`?:`;
//  Swift: `fatalError()` ist `Never`.)

class EvalError(val what: String) : Exception("eval: $what")

sealed class Token {
    data class Num(val n: Int) : Token()
    data class Name(val id: String) : Token()
    object Plus : Token()
}

fun value(t: Token, env: Map<String, Int>): Int = when (t) {
    is Token.Num -> t.n
    is Token.Name -> lookup(t.id, env)
    else -> throw EvalError("not a value")                   // (1) Zweig vom Typ Nothing
}

fun lookup(id: String, env: Map<String, Int>): Int =
    env[id] ?: throw EvalError("unbound $id")                // (2) Elvis + throw

fun checkedDiv(a: Int, b: Int): Int =
    if (b != 0) a / b else throw EvalError("division by zero")   // (3)

fun main() {
    val env = mapOf("x" to 42)
    var sum = 0
    for (t in listOf(Token.Num(1), Token.Name("x"), Token.Name("y"), Token.Plus)) {
        try { sum += value(t, env) } catch (e: EvalError) { println(e.message) }
    }
    try { sum += checkedDiv(sum, 0) } catch (e: EvalError) { println(e.message) }
    println("sum $sum")
}
// Ausgabe:
// eval: unbound y
// eval: not a value
// eval: division by zero
// sum 43
