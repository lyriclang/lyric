// 21 Vergleich: Rust — `Option<T>` ist ein gewöhnliches Enum, also nestet es: `Option<Option<i64>>`
// unterscheidet `None` (leer) von `Some(None)` (Element vorhanden, aber None). Das Iterator-
// Protokoll (`next() -> Option<Item>`) funktioniert deshalb für `Item = Option<T>` ohne Sonderfall.
// Kotlin: `T?` bei `T = Int?` kollabiert — DIESELBE Falle wie Lyric; `firstOrNull()` auf
// `List<Int?>` kann leer/null nicht trennen (bekannte Schwäche, Workaround `firstOrNull()?.let`).
fn first_of<T: Clone>(xs: &[T]) -> Option<T> { xs.first().cloned() }

fn main() {
    let slots: Vec<Option<i64>> = vec![None, Some(5), None];
    let empty: Vec<Option<i64>> = vec![];

    let present = slots.iter().filter(|x| x.is_some()).count();   // (1) Iteration über Option-Elemente
    println!("present {present}");

    match first_of(&slots) {                                        // (2) leer vs. Element-None
        Some(None) => println!("a: element present but None"),
        Some(Some(v)) => println!("a: {v}"),
        None => println!("a: empty"),
    }
    match first_of(&empty) {
        None => println!("b: empty"),
        _ => println!("b: element"),
    }

    let cells: Vec<Option<i64>> = vec![None, Some(7)];             // (3) selbstverständlich
    println!("cells {}", cells.len());
}
// Ausgabe:
// present 1
// a: element present but None
// b: empty
// cells 2
