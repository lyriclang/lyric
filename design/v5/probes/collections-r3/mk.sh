#!/bin/bash
cd "$(dirname "$0")"
cat > EXPECT.txt <<'EOF'
Erwartungen VOR dem Lauf (collections-r3):
p01 star struct: "star 7 7", "lit 8 1", "nested 9 9" (inneres Array geteilt), "zero 0"
p02 float range: "1 2 3" und 0 Durchlaeufe fuer 0.0..0.5
p03 char index: LYR-CLI0020 ; p03b uint index: LYR-CLI0020 (u64 statt i64) ; p03c int index: laeuft, druckt 2
p04 extend int[] ohne Aufruf: kompiliert still ; p04b extend (int,int): still ; p04c extend List<int>: SEM0047
p05 xs[0]++: LYR-IR0001 ; xs[0] += 1 laeuft
p06 over(xs) zweimal: 3 dann 0
p07 catch um Index: kompiliert, Exit 101, "gefangen" wird NICHT gedruckt
p08 if (2 in xs): LYR-PAR0002
p09 Iterator<int> als Wert: ~17.7 KB, 97 Funktionen; Elementtypen int/float/string sichtbar ; p09b konkret: ~3 KB, 4 Funktionen
p10 Map<int[],string>: LYR-IR0001 in collections.lyr
p11 disasm for-in ueber Array: direkter call auf ArrayIterator.next, KEIN callvirt (Quelle EmitNextCall)
p12 loopvar: "x = 5" im for-in -> Fehler (unveraenderlich) ; p12b Bound-Neuzuweisung im Rumpf aendert Iterationszahl nicht (3) ; xs neu zuweisen im Rumpf: 3 Durchlaeufe ueber altes Array
p13 0..n mit n: int64 -> unbekannt (SEM0003 oder Vereinheitlichung); p13b n: int32 -> unbekannt
p14 let e = []; ohne Kontext -> Fehler (unbekannter Code)
p15 xs[-1]: panic VM0006, Exit 101
p16 for ueber (?int)[]: LYR-SEM0091
p18 [1] * -1: unbekannt (panik oder leer)
p19 s[0] auf string: LYR-SEM0007
p21 Klasse mit Iterable UND Iterator: Iterable gewinnt, 3 und 3
p25 for (c in 'a'..'d'): a b c
p26 let c = [seed]*2; c[0].x=7.0 kompiliert, "7 7"
p27 xs[i] += v mit struct Add: SEM0003 ; p27b p.x += 1 int-Feld: laeuft
EOF
cat > p01_star.lyr <<'EOF'
import std.io.console { println };
struct Vec2 { x: float, y: float }
fn main(): int {
    let seed = Vec2{x=1.0, y=2.0};
    var c = [seed] * 2;
    c[0].x = 7.0;
    println(f"star {c[0].x} {c[1].x}");
    var d = [seed, seed];
    d[0].x = 8.0;
    println(f"lit {d[0].x} {d[1].x}");
    var m = [[0] * 2] * 2;
    m[0][0] = 9;
    println(f"nested {m[0][0]} {m[1][0]}");
    let z = [seed] * 0;
    println(f"zero {z.length}");
    return 0;
}
EOF
cat > p02_float.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var line = "";
    for (x in 1.5..4.5) { line = line + f" {x}"; }
    println(f"a:{line}");
    var n = 0;
    for (x in 0.0..0.5) { n += 1; }
    println(f"b:{n}");
    return 0;
}
EOF
cat > p03_char.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    let c: char = 'b';
    println(f"{xs[c]}");
    return 0;
}
EOF
cat > p03b_uint.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    let u: uint = 1;
    println(f"{xs[u]}");
    return 0;
}
EOF
cat > p03c_int.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    let i: int = 1;
    println(f"{xs[i]}");
    return 0;
}
EOF
cat > p04_ext_array.lyr <<'EOF'
import std.io.console { println };
extend int[] { pub fn foo(): int { return 1; } }
fn main(): int { println("ok"); return 0; }
EOF
cat > p04b_ext_tuple.lyr <<'EOF'
import std.io.console { println };
extend (int, int) { pub fn foo(): int { return 1; } }
fn main(): int { println("ok"); return 0; }
EOF
cat > p04c_ext_list.lyr <<'EOF'
import std.io.console { println };
import std.collections { List };
extend List<int> { pub fn foo(): int { return 1; } }
fn main(): int { println("ok"); return 0; }
EOF
cat > p05_incdec.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var xs = [1, 2, 3];
    xs[0] += 1;
    println(f"{xs[0]}");
    xs[0]++;
    println(f"{xs[0]}");
    return 0;
}
EOF
cat > p06_twice.lyr <<'EOF'
import std.io.console { println };
import std.iter { over };
fn main(): int {
    let xs = [1, 2, 3];
    let it = over(xs);
    var a = 0;
    for (v in it) { a += 1; }
    var b = 0;
    for (v in it) { b += 1; }
    println(f"{a} {b}");
    return 0;
}
EOF
cat > p07_catch.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    var i = 5;
    i = i + 0;
    try {
        println(f"{xs[i]}");
    } catch (e: Throwable) {
        println("gefangen");
    }
    return 0;
}
EOF
cat > p08_in.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    if (2 in xs) { println("ja"); }
    return 0;
}
EOF
cat > p09_iface.lyr <<'EOF'
import std.io.console { println };
import std.iter { Iterator };
pub class Counter :: [Iterator<int>] {
    n: int = 0,
    pub mut fn next(): ?int {
        if (this.n >= 3) { return null; }
        this.n += 1;
        return this.n;
    }
}
fn main(): int {
    let c: Iterator<int> = Counter{};
    var total = 0;
    var v = c.next();
    while (v != null) { total += v; v = c.next(); }
    println(f"{total}");
    return 0;
}
EOF
cat > p09b_concrete.lyr <<'EOF'
import std.io.console { println };
import std.iter { Iterator };
pub class Counter :: [Iterator<int>] {
    n: int = 0,
    pub mut fn next(): ?int {
        if (this.n >= 3) { return null; }
        this.n += 1;
        return this.n;
    }
}
fn main(): int {
    let c = Counter{};
    var total = 0;
    var v = c.next();
    while (v != null) { total += v; v = c.next(); }
    println(f"{total}");
    return 0;
}
EOF
cat > p10_arraykey.lyr <<'EOF'
import std.io.console { println };
import std.collections { Map };
fn main(): int {
    let m = Map<int[], string>.empty();
    m.set([1,2], "a");
    println("ok");
    return 0;
}
EOF
cat > p11_forin.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    var total = 0;
    for (x in xs) { total += x; }
    println(f"{total}");
    return 0;
}
EOF
cat > p12_loopvar.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    for (x in xs) { x = 5; }
    return 0;
}
EOF
cat > p12b_bound.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var n = 3;
    var count = 0;
    for (i in 0..n) { n = 0; count += 1; }
    println(f"range {count}");
    var xs = [1, 2, 3];
    var c2 = 0;
    for (x in xs) { xs = [9]; c2 += 1; }
    println(f"array {c2}");
    return 0;
}
EOF
cat > p13_mixed.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let n: int64 = 3;
    var c = 0;
    for (i in 0..n) { c += 1; }
    println(f"{c}");
    return 0;
}
EOF
cat > p13b_mixed32.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let n: int32 = 3;
    var c = 0;
    for (i in 0..n) { c += 1; }
    println(f"{c}");
    return 0;
}
EOF
cat > p14_empty.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let e = [];
    println(f"{e.length}");
    return 0;
}
EOF
cat > p15_neg.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs = [1, 2, 3];
    var i = -1;
    i = i + 0;
    println(f"{xs[i]}");
    return 0;
}
EOF
cat > p16_optarr.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let xs: (?int)[] = [1, null, 3];
    for (x in xs) { println("x"); }
    return 0;
}
EOF
cat > p18_starneg.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var k = -1;
    k = k + 0;
    let xs = [1] * k;
    println(f"{xs.length}");
    return 0;
}
EOF
cat > p19_strindex.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let s = "abc";
    println(f"{s[0]}");
    return 0;
}
EOF
cat > p21_both.lyr <<'EOF'
import std.io.console { println };
import std.iter { Iterator, Iterable };
pub class Both :: [Iterable<int>, Iterator<int>] {
    n: int = 0,
    pub mut fn next(): ?int {
        if (this.n >= 3) { return null; }
        this.n += 1;
        return this.n;
    }
    pub fn iter(): Iterator<int> { return Both{}; }
}
fn main(): int {
    let b = Both{};
    var a = 0;
    for (v in b) { a += 1; }
    var c = 0;
    for (v in b) { c += 1; }
    println(f"{a} {c}");
    return 0;
}
EOF
cat > p25_charrange.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    var line = "";
    for (c in 'a'..'d') { line = line + f"{c}"; }
    println(line);
    return 0;
}
EOF
cat > p26_let.lyr <<'EOF'
import std.io.console { println };
struct Vec2 { x: float, y: float }
fn main(): int {
    let seed = Vec2{x=1.0, y=2.0};
    let c = [seed] * 2;
    c[0].x = 7.0;
    println(f"{c[0].x} {c[1].x}");
    return 0;
}
EOF
cat > p27_compound.lyr <<'EOF'
import std.io.console { println };
import std.core { Add };
struct V :: [Add<V, V>] {
    x: int,
    pub fn add(other: V): V { return V{x = this.x + other.x}; }
}
fn main(): int {
    var xs = [V{x=1}, V{x=2}];
    xs[0] += V{x=10};
    println(f"{xs[0].x}");
    return 0;
}
EOF
cat > p27b_field.lyr <<'EOF'
import std.io.console { println };
struct P { x: int }
fn main(): int {
    var p = P{x=1};
    p.x += 1;
    println(f"{p.x}");
    return 0;
}
EOF
ls *.lyr | wc -l
