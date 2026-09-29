// 13 Vergleich: Rust — Enums haben assoziierte Konstanten und Funktionen (`impl Level`),
// Traits haben assoziierte Funktionen OHNE self (`fn zero() -> Self`), die in generischem Code
// als `T::zero()` aufgerufen werden — statisch dispatcht (Monomorphisierung), über ein
// `dyn Trait`-Objekt nicht erreichbar (nicht "object safe") — exakt die Regel des Vorschlags.
use std::ops::Add;

#[derive(Clone, Copy, Debug)]
enum Level { Low, Mid, High }

impl Level {
    const DEFAULT: Level = Level::Mid;             // (a) assoziierte Konstante
    const LOW_MAX: i64 = 10;
    const MID_MAX: i64 = 50;
    fn from_score(s: i64) -> Level {
        if s < Self::LOW_MAX { Level::Low } else if s < Self::MID_MAX { Level::Mid } else { Level::High }
    }
}

trait Zero { fn zero() -> Self; }                  // (b) assoziierte Funktion ohne self
impl Zero for i64 { fn zero() -> Self { 0 } }

#[derive(Clone, Copy, Debug)]
struct Vec2 { x: i64, y: i64 }
impl Add for Vec2 { type Output = Vec2; fn add(self, o: Vec2) -> Vec2 { Vec2 { x: self.x + o.x, y: self.y + o.y } } }
impl Zero for Vec2 { fn zero() -> Self { Vec2 { x: 0, y: 0 } } }

fn sum<T: Add<Output = T> + Zero + Copy>(xs: &[T]) -> T {
    let mut acc = T::zero();                       // direkter Aufruf nach Monomorphisierung
    for &x in xs { acc = acc + x; }
    acc
}

fn main() {
    println!("{:?} {:?} {:?}", Level::DEFAULT, Level::from_score(7), Level::from_score(99));
    println!("{}", sum(&[1, 2, 3]));
    let v = sum(&[Vec2 { x: 1, y: 2 }, Vec2 { x: 3, y: 4 }]);
    println!("({}, {})", v.x, v.y);
}
// Ausgabe:
// Mid Low High
// 6
// (4, 6)

// Swift: `protocol Zero { static func zero() -> Self }` … `T.zero()` in generischem Code;
// über einen existenziellen Wert (`any Zero`) nicht aufrufbar — dieselbe Grenze.
