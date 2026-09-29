// 07 Vergleich: Kotlin — `enum class` hat `.name` und `valueOf()` eingebaut; eine sealed class
// mit data-Varianten bekommt `toString()` automatisch ("Dial(host=host)").
// Zig zum Vergleich: `@tagName(ev)` liefert den Tag-Namen jedes Enums/Tagged Unions zur
// Compile-Zeit — exakt Teil (a) des Vorschlags, ohne Interface.

sealed class Event {
    data class Dial(val host: String) : Event()
    object Ack : Event() { override fun toString() = "Ack" }
    data class Data(val n: Int) : Event()
    object Hangup : Event() { override fun toString() = "Hangup" }
}

enum class Mode { Fast, Safe, Dry }

fun main() {
    val events = listOf(Event.Dial("host"), Event.Ack, Event.Data(5), Event.Hangup)
    for (e in events) {
        println("${e::class.simpleName}: $e")           // Tag-Name + synthetisiertes toString
    }
    val m = Mode.entries.firstOrNull { it.name == "Safe" }   // oder Mode.valueOf("Safe") (wirft)
    println(m?.name ?: "unknown")
    println(Mode.entries.firstOrNull { it.name == "Turbo" }?.name ?: "unknown")
}
// Ausgabe:
// Dial: Dial(host=host)
// Ack: Ack
// Data: Data(n=5)
// Hangup: Hangup
// Safe
// unknown

// Zig:
//   const Event = union(enum) { dial: []const u8, ack, data: i64, hangup };
//   std.debug.print("{s}\n", .{@tagName(ev)});          // "dial"
//   const m = std.meta.stringToEnum(Mode, "safe");      // ?Mode — die Rückrichtung
