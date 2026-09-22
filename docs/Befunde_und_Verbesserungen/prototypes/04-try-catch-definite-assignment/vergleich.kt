// 04 Vergleich: Kotlin — `try` ist ein Ausdruck; ein catch-Zweig, der mit `return` endet,
// hat den Typ Nothing und fällt aus der Unifikation heraus, also ist `opts` ein Options.
// (C# erreicht Stufe (a) ohne Ausdruck: Definite Assignment zählt die Body-Zuweisung,
//  wenn jeder catch die Methode verlässt — siehe vergleich.cs)

class UsageError(val what: String) : Exception("usage: $what")
data class Options(val limit: Int, val verbose: Boolean)

fun parseArgs(args: List<String>): Options {
    if (args.isEmpty()) throw UsageError("need at least one argument")
    return Options(args.size, args[0] == "-v")
}

fun runB(args: List<String>): Int {
    val opts = try { parseArgs(args) } catch (e: UsageError) { println(e.message); return 2 }
    println("B: limit ${opts.limit} verbose ${opts.verbose}")
    return 0
}

// "Grund egal": runCatching(...).getOrNull() entspricht Swift `try?` / Lyric-Vorschlag `try? e`
fun limitOrDefault(args: List<String>): Int =
    runCatching { parseArgs(args) }.getOrNull()?.limit ?: 1

fun main() {
    runB(listOf("-v", "x")); runB(listOf())
    println("default ${limitOrDefault(listOf())}")
}
// Ausgabe:
// B: limit 2 verbose true
// usage: need at least one argument
// default 1
