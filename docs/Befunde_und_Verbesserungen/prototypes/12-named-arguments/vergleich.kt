// 12 Vergleich: Kotlin — benannte Argumente mit `=` (dort möglich, weil Zuweisung KEIN
// Ausdruck ist), nach den positionalen, beliebige Reihenfolge, Defaults bleiben stehen.
// Swift: Argument-Labels sind PFLICHT und Teil des Funktionsnamens (`connect(host:timeoutMs:)`),
// Trenner `:` — das ist die Form, die der Lyric-Vorschlag syntaktisch übernimmt (optional statt Pflicht).
// Rust/Go/Zig: keine benannten Argumente — Options-Struct/Builder, wie Lyric heute.

fun connect(host: String, port: Int = 80, secure: Boolean = false, retries: Int = 3, timeoutMs: Int = 1000) =
    "$host:$port secure=$secure retries=$retries timeout=$timeoutMs"

fun main() {
    println(connect("a", timeoutMs = 500))
    println(connect("a", retries = 80, port = 3))
    println(connect("b", secure = true, retries = 5))
    var port = 0
    println(connect("c", port = 8080))       // `port = 8080` ist hier ein Argument, keine Zuweisung
    println("port is still $port")
}
// Ausgabe:
// a:80 secure=false retries=3 timeout=500
// a:3 secure=false retries=80 timeout=1000
// b:80 secure=true retries=5 timeout=1000
// c:8080 secure=false retries=3 timeout=1000
// port is still 0

// Swift:
//   func connect(host: String, port: Int = 80, secure: Bool = false, retries: Int = 3, timeoutMs: Int = 1000) -> String
//   connect(host: "a", timeoutMs: 500)
