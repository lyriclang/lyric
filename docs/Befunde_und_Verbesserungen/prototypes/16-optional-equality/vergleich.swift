// 16 Vergleich: Swift — `Optional<Wrapped>: Equatable where Wrapped: Equatable`:
// nil == nil ist true, nil == .some(x) false, sonst Wrapped ==. Ordnung (`<`) auf Optionals
// wurde in Swift 3 entfernt — genau die Grenze des Lyric-Vorschlags.
// C#: `int? == int?` ist "lifted equality" mit derselben Tabelle; Kotlin: `==` ist null-sicher.
struct Config: Equatable {                        // Synthese schließt Optional-Felder ein
    var host: String
    var port: Int?
    var label: String?
}

func same<T: Equatable>(_ a: T?, _ b: T?) -> Bool { a == b }

func describe(_ c: Config) -> String {
    let p = c.port != nil ? c.port! + 1 : 0       // Swift narrowt Felder auch nicht: `if let`
    return "\(c.host):\(p)"
}

let a = Config(host: "h", port: 80, label: nil)
let b = Config(host: "h", port: 80, label: nil)
let c = Config(host: "h", port: nil, label: "x")
print(a == b ? "a == b" : "a != b")
print(a == c ? "a == c" : "a != c")
print(same(a.port, b.port) ? "ports same" : "ports differ")
print(describe(a) + " " + describe(c))
// Ausgabe:
// a == b
// a != c
// ports same
// h:81 h:0

// C#:  int? p = 1, q = 1;  p == q  → true;  (int?)null == (int?)null → true
