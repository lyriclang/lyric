#!/bin/bash
cd "$(dirname "$0")"
cat >> EXPECT.txt <<'EOF'
--- Runde 2 der Proben:
q01 let l: List<int> = [1,2,3]: LYR-SEM0001
q02 f"{xs}" mit int[]: LYR-SEM0006
q03 for (P{x,y} in ps): PAR0002 (+PAR0021)
q04 over(xs).map(parse) mit throws: LYR-SEM0037
q05 ?int[] ist ?(int[]): "let o: ?int[] = null;" kompiliert; for (x in o) -> SEM0007 (nicht SEM0091)
q06 Struct-Slot-Kopie: "var a = c[0]; a.x = 42" laesst c[0].x bei 7 (Kopie beim Lesen); Kontrolle c[0].x = 7 schreibt durch
q07 [0] * (1<<40): unbekannt — Panik, ICE oder Host-Exception
q08 [1, null] ohne Kontext: unbekannt — (?int)[] oder Fehler
q09 [[1,2]] + [[3]] dann inneres Array aendern: + ist flach, innere geteilt -> "9"
EOF
cat > q01_listlit.lyr <<'EOF'
import std.io.console { println };
import std.collections { List };
fn main(): int {
    let l: List<int> = [1, 2, 3];
    println("x");
    return 0;
}
EOF
cat > q02_fstr.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    println(f"{xs}");
    return 0;
}
EOF
cat > q03_structpat.lyr <<'EOF'
import std.io.console { println };
struct P { x: int, y: int, }
fn main(): int {
    let ps = [P{x=1,y=2}, P{x=3,y=4}];
    for (P { x, y } in ps) { println(f"{x},{y}"); }
    return 0;
}
EOF
cat > q04_throwmap.lyr <<'EOF'
import std.io.console { println };
import std.iter { over };
fn parse(s: string): int throws Exception { throw Exception("no"); }
fn main(): int {
    let xs = ["1", "2"];
    let it = over(xs).map(parse);
    println("x");
    return 0;
}
EOF
cat > q05_optarr.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let o: ?int[] = null;
    let p: ?int[] = [1, 2];
    for (x in p) { println(f"{x}"); }
    return 0;
}
EOF
cat > q06_slotcopy.lyr <<'EOF'
import std.io.console { println };
struct Vec2 { x: float, y: float }
fn main(): int {
    let seed = Vec2{x=1.0, y=2.0};
    var c = [seed] * 2;
    c[0].x = 7.0;
    var a = c[0];
    a.x = 42.0;
    println(f"read-copy {c[0].x} {a.x}");
    let b = c[1];
    println(f"shared {b.x}");
    return 0;
}
EOF
cat > q07_huge.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var n = 1;
    var i = 0;
    while (i < 40) { n = n * 2; i = i + 1; }
    let xs = [0] * n;
    println(f"{xs.length}");
    return 0;
}
EOF
cat > q08_nulllit.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, null];
    println(f"{xs.length}");
    return 0;
}
EOF
cat > q09_shallow.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let inner = [1, 2];
    let a = [inner];
    let b = a + [[3]];
    b[0][0] = 9;
    println(f"{inner[0]} {a[0][0]}");
    return 0;
}
EOF
echo written
