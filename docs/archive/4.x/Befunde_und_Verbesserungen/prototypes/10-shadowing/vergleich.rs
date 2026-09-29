// 10 Vergleich: Rust — Shadowing im selben Block ist DAS Idiom für schrittweise Verfeinerung;
// jede `let` erzeugt eine neue Bindung (auch mit anderem Typ), die alte ist danach unerreichbar.
fn describe(input: Option<&str>, fallback: &str) -> String {
    let input = input.unwrap_or(fallback);        // Option<&str> -> &str, gleicher Name
    let input = input.trim();                     // &str -> &str
    match input.parse::<i64>() {
        Ok(n) => format!("number {n}"),
        Err(_) => format!("not a number: '{input}'"),
    }
}

fn main() {
    println!("{}", describe(Some(" 42 "), "0"));
    println!("{}", describe(None, "7"));
    println!("{}", describe(Some("abc"), "0"));
    let n = 1;
    let n = n + 10;                               // 11 — die neue Bindung gilt
    println!("shadowed {n}");
}
// Ausgabe:
// number 42
// number 7
// not a number: 'abc'
// shadowed 11

// Kotlin (Variante B) zum Vergleich — Redeklaration im selben Scope ist ein Fehler:
//   val n = 1
//   val n = n + 10        // error: conflicting declarations: val n: Int, val n: Int
// Kotlin braucht das Idiom weniger, weil Smart Casts (`if (x != null)`) auch Felder/`val`s
// verfeinern; in Lyric endet das Narrowing bei Identifiern (§7.4), was Shadowing wertvoller macht.
