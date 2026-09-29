// 02 Vergleich: Swift — `if let`, `guard let … else`, `while let`, `if case`.
// Swift bindet in der Bedingung; `guard` erzwingt, dass der else-Block den Scope verlässt —
// genau die Regel, die der Lyric-Vorschlag für `let … else` übernimmt.

class Profile { var email: String?; init(email: String?) { self.email = email } }
class User { var name: String; var profile: Profile?
    init(name: String, profile: Profile?) { self.name = name; self.profile = profile } }

enum Shape { case circle(Int), rect(w: Int, h: Int), empty }

// (a) guard let: bindet das Feld direkt, sonst früher Exit
func emailOf(_ u: User) -> String {
    guard let p = u.profile else { return "no profile" }
    guard let e = p.email   else { return "no email" }
    return e
}

// (b) if case: Payload-Extraktion ohne vollständiges switch
func radiusOrZero(_ s: Shape) -> Int {
    if case .circle(let r) = s { return r }
    return 0
}
func describeIfRect(_ s: Shape) {
    if case .rect(let w, let h) = s { print("rect \(w)x\(h)") }
}

// (c) while let: der Pull steht in der Bedingung
func drain(_ q: inout [Int]) -> Int {
    var sum = 0
    while let v = q.isEmpty ? nil : q.removeFirst() { sum += v }
    return sum
}

let ada = User(name: "Ada", profile: Profile(email: "ada@x.org"))
let bob = User(name: "Bob", profile: Profile(email: nil))
let kim = User(name: "Kim", profile: nil)
print("\(emailOf(ada)) / \(emailOf(bob)) / \(emailOf(kim))")
print("\(radiusOrZero(.circle(4))) \(radiusOrZero(.empty))")
describeIfRect(.rect(w: 2, h: 3)); describeIfRect(.empty)
var q = [1, 2, 3]
print("drain = \(drain(&q))")
// Ausgabe:
// ada@x.org / no email / no profile
// 4 0
// rect 2x3
// drain = 6
