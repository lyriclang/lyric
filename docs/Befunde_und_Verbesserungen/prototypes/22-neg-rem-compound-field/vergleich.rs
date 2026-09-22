// 22 Vergleich: Rust — `Neg`, `Rem`, `AddAssign` sind gewöhnliche Traits; `b.pos += b.vel`
// auf einem Feld ist erlaubt, der Platz-Ausdruck wird genau einmal ausgewertet.
use std::ops::{Add, AddAssign, Neg, Rem};

#[derive(Clone, Copy, Debug)]
struct V { x: i64, y: i64 }
impl Add for V { type Output = V; fn add(self, o: V) -> V { V { x: self.x + o.x, y: self.y + o.y } } }
impl Neg for V { type Output = V; fn neg(self) -> V { V { x: -self.x, y: -self.y } } }
impl Rem for V { type Output = V; fn rem(self, o: V) -> V { V { x: self.x % o.x, y: self.y % o.y } } }
impl AddAssign for V { fn add_assign(&mut self, o: V) { *self = *self + o; } }

struct Body { pos: V, vel: V }

fn step(b: &mut Body) { b.pos += b.vel; }     // Feldziel, einmal ausgewertet

fn main() {
    let v = V { x: 3, y: -4 };
    println!("{:?}", -v);
    println!("{:?}", v % V { x: 2, y: 3 });
    let mut b = Body { pos: V { x: 0, y: 0 }, vel: V { x: 1, y: 2 } };
    step(&mut b); step(&mut b);
    println!("{:?}", b.pos);
}
// Ausgabe:
// V { x: -3, y: 4 }
// V { x: 1, y: -1 }
// V { x: 2, y: 4 }
