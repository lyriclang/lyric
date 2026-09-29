// 15 Vergleich: Rust — `{p}` in `format!`/`println!` ruft `Display::fmt`; `{p:?}` ruft Debug.
// Ein Typ ohne Display ist ein Compile-Fehler ("doesn't implement std::fmt::Display") —
// dieselbe Regel, die Lyric für println schon hat und für f-Strings noch nicht.
// Kotlin: `"$p"` ruft `toString()` (jeder Typ hat es); Python: `f"{p}"` ruft `__str__`.
use std::fmt;

struct Point { x: i64, y: i64 }
impl fmt::Display for Point {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result { write!(f, "({}, {})", self.x, self.y) }
}

enum Status { Ok, Failed(String) }
impl fmt::Display for Status {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self { Status::Ok => write!(f, "ok"), Status::Failed(why) => write!(f, "failed: {why}") }
    }
}

fn report(p: &Point, s: &Status, tries: i64) -> String {
    format!("point {p} status {s} after {tries} tries")      // Display-Aufruf implizit
}

fn main() {
    let p = Point { x: 3, y: 4 };
    println!("{p}");
    println!("{}", report(&p, &Status::Ok, 1));
    println!("{}", report(&Point { x: 0, y: 0 }, &Status::Failed("timeout".into()), 3));
}
// Ausgabe:
// (3, 4)
// point (3, 4) status ok after 1 tries
// point (0, 0) status failed: timeout after 3 tries
