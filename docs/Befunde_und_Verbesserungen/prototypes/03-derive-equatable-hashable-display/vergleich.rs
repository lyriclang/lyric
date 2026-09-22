// 03 Vergleich: Rust — #[derive(...)] erzeugt die Trait-Implementierungen aus den Feldern.
// Feldweise Gleichheit, kombinierter Hash, lexikografische Ordnung in Felddeklarations-
// reihenfolge, Debug rendert die Struktur mit Feldnamen — exakt die vier Regeln des Vorschlags.
use std::collections::{HashMap, HashSet};

#[derive(Debug, Clone, PartialEq, Eq, Hash, PartialOrd, Ord)]
struct Coord { x: i64, y: i64, label: String }

#[derive(Debug, PartialEq, Eq)]
enum Cell { Empty, Wall, Item(i64) }

fn main() {
    let a = Coord { x: 1, y: 2, label: "a".into() };
    let b = Coord { x: 1, y: 2, label: "a".into() };
    let c = Coord { x: 0, y: 9, label: "c".into() };

    println!("{}", if a == b { "a == b" } else { "a != b" });
    println!("{}", if a < c { "a < c" } else { "a >= c" });

    let mut visited = HashSet::new();
    visited.insert(a.clone()); visited.insert(b.clone()); visited.insert(c.clone());
    println!("set size {}", visited.len());

    let mut names = HashMap::new();
    names.insert(a.clone(), "start");
    println!("{}", names.get(&b).unwrap_or(&"missing"));

    let mut sorted = vec![c.clone(), a.clone()];
    sorted.sort();
    println!("{:?}", sorted[0]);

    println!("{:?}", Cell::Item(3));
    println!("{}", if Cell::Item(3) == Cell::Item(3) { "same" } else { "diff" });
}
// Ausgabe:
// a == b
// a >= c
// set size 2
// start
// Coord { x: 0, y: 9, label: "c" }
// Item(3)
// same
