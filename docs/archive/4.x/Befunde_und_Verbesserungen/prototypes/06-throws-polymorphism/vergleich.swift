// 06 Vergleich: Swift — typed throws (SE-0413, Swift 6): `throws(E)` mit generischem E.
// `Never` als E bedeutet "wirft nicht"; ein Aufruf mit nicht-werfendem Argument braucht kein `try`.
// `rethrows` (älter) ist der Spezialfall "wirft genau dann, wenn das Closure-Argument wirft".

struct ParseError: Error { let text: String }

func numbers() -> [Int] { [1, 2, 3, 4] }

func parsed(_ lines: [String]) throws(ParseError) -> [Int] {
    try lines.map { l throws(ParseError) in
        guard let n = Int(l) else { throw ParseError(text: "not a number: \(l)") }
        return n
    }
}

// E fließt vom Argument in die Klausel; mit E == Never ist der Aufruf nicht-werfend.
func evens<E: Error>(_ input: () throws(E) -> [Int]) throws(E) -> [Int] {
    try input().filter { $0 % 2 == 0 }
}

func mapInts<U, E: Error>(_ xs: [Int], _ f: (Int) throws(E) -> U) throws(E) -> [U] {
    var out: [U] = []
    for x in xs { out.append(try f(x)) }
    return out
}

func checkedDouble(_ n: Int) throws(ParseError) -> Int {
    if n > 100 { throw ParseError(text: "too big") }
    return n * 2
}

let a = evens { numbers() }.reduce(0, +)             // E = Never: kein try
print("evens of numbers: \(a)")

do {
    let b = try evens { () throws(ParseError) in try parsed(["10", "x", "12"]) }.reduce(0, +)
    print("evens of parsed: \(b)")
} catch { print("parsed failed: \(error.text)") }   // typed: error IST ein ParseError

do {
    let d = try mapInts([1, 200, 3]) { n throws(ParseError) in try checkedDouble(n) }
    print(d)
} catch { print("map failed: \(error.text)") }
// Ausgabe:
// evens of numbers: 6
// parsed failed: not a number: x
// map failed: too big
