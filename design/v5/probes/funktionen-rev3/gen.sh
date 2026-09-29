#!/bin/bash
cd "$(dirname "$0")"
cat > ERWARTUNGEN.txt <<'EOF'
Erwartungen VOR dem Lauf (Fassung 3, 2026-09-27) - Nachpruefung der Kritik
r01  each(f, n=2) mit each{it+1} / each((k)=>k+1) / each((k)=>k+1,5)  -> 3 3 6 (Kritiker recht)
r01b each(5){it+1}                                                    -> SEM0001 + 2x SEM0045
r01c KONTROLLE: each(n=2, f) Deklaration                              -> SEM0025 (+2x SEM0045 im Aufruf)
r02b Default nur im Interface, Impl ohne                              -> laeuft, YoB YoB
r02c Default nur in der Impl                                          -> laeuft, YoB HiB
r02d Interface "Hi", Impl "Yo", s.greet("B")                          -> YoB (Impl-Default gewinnt am statischen Typ S)
r02e KONTROLLE: Interface "Hi", Impl "Yo", g.greet("B") ueber Iface   -> CLI0020 (Befund 2), kein Lauf
r03  lyrc check c_q09 (this)                                          -> exit 1, CLI0020
r03b lyrc check c_q10 (break)                                         -> exit 1, CLI0020
r03c lyrc check c_q11b (yield gerufen)                                -> exit 0, ok
r03d lyrc check c_p61                                                 -> exit 0, ok
r03e lyrc check c_p61 --emit                                          -> exit 1, CLI0020 CallVirt needs 3
r04  c_q19 down(5000): stderr-Zeilen ~1026, stdout ~1022; Panik in println<string>
r05  c_p01 connect(host:"h", port:80)                                 -> 19 Diagnosen, 7 verschiedene Codes
r06  q18 release lower main: callvirt 4 callind 2; a11 release: callvirt 2 callind 2
r07  var counter auf Modulebene                                       -> PAR0027
r07b let base im Lambda, lower                                        -> mkclosure (no captures) + ldglobal
r08  apply<T,U> mit drei Lambda-Formen                                -> 6 6 6
r08b apply{it>1} gegen fn(int)->int / fn(int)->bool                   -> SEM0086
r09  c_q12 (n=2 vor f)                                                -> SEM0025 + 2x SEM0045
r10  fn()->S an fn()->Speaker; fn(Speaker)->int an fn(S)->int         -> 2x SEM0001
r10b KONTROLLE: Lambda an Ort und Stelle mit Iface-Rueckgabe          -> laeuft 7 3
r11  defer sieht Endwert                                              -> 2
r11b return im defer                                                  -> Diagnose (Code offen)
r11c break im defer in Schleife                                       -> Diagnose oder ICE (offen)
r11d defer in Lambda                                                  -> before body d after 1
r12  Closure ueber var in Koroutine ueber 2 yield                     -> 1 2
r13  (n: int) => bei aeusserem n                                      -> keine SEM0107, Ausgabe 2
r13c KONTROLLE: Block-Lokale n verdeckt aeusseres n                   -> SEM0107 Warnung? (offen), Ausgabe 2
r14  3M Aufrufe release: direct < virt(devirt) <= ind; ind - direct ~ 50-150ms
r15  q16 --jit -> 1 2 1; p39 --jit -> VM0004
r16  f() mit a=side("a"), b=side("b") -> a b 2; f(1) -> b 2; f(side("x")) -> x b 2
r17  h.f(1) mit Feld f -> 2; g = h.f; g(10) -> 11 (Feldwert)
r17b struct mit Feld f UND Methode f -> Fehler (Code offen)
r19  n16 Trace: 'main.main.<lambda1>' ~1023x
r20  run(5) { let g = (n: int) => it + n; g(1) } -> 6 (it gefangen) — oder Fehler, wenn it kein gewoehnlicher Name
r20b dito mit return-Form -> 6
r22  lyrfmt behaelt x => x*3 und run { it + 7 }
EOF
cat > r01.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn each(f: fn(int) -> int, n: int = 2): int { return f(n); }
fn main(): int {
    println(fromInt(each { it + 1 }));
    println(fromInt(each((k: int) => k + 1)));
    println(fromInt(each((k: int) => k + 1, 5)));
    return 0;
}
EOF
cat > r01b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn each(f: fn(int) -> int, n: int = 2): int { return f(n); }
fn main(): int {
    println(fromInt(each(5) { it + 1 }));
    return 0;
}
EOF
cat > r01c.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn each(n: int = 2, f: fn(int) -> int): int { return f(n); }
fn main(): int {
    println(fromInt(each { it + 1 }));
    return 0;
}
EOF
cp ../funktionen-audit/a05b_iface_default_only_iface.lyr r02b.lyr
cp ../funktionen-audit/a05c_iface_default_only_impl.lyr r02c.lyr
cp ../funktionen-audit/a05d_iface_default_differ.lyr r02d.lyr
cat > r02e.lyr <<'EOF'
import std.io.console { println };
interface Greeter { fn greet(name: string, greeting: string = "Hi"): string; }
struct S :: [Greeter] { v: int, fn greet(name: string, greeting: string = "Yo"): string { return greeting + name; } }
fn main(): int {
    let s = S { v = 1 };
    let g: Greeter = s;
    println(g.greet("B"));
    return 0;
}
EOF
cat > r07.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
var counter: int = 0;
fn main(): int {
    let f = () => { counter = counter + 1; return counter; };
    println(fromInt(f()));
    return 0;
}
EOF
cat > r07b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
let base: int = 10;
fn main(): int {
    let f = () => base + 1;
    println(fromInt(f()));
    return 0;
}
EOF
cat > r08.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn apply<T, U>(x: T, f: fn(T) -> U): U { return f(x); }
fn main(): int {
    println(fromInt(apply(3, (n: int) => n * 2)));
    println(fromInt(apply(3, n => n * 2)));
    println(fromInt(apply(3) { it * 2 }));
    return 0;
}
EOF
cat > r08b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn apply(x: int, f: fn(int) -> int): int { return f(x); }
fn apply(x: int, f: fn(int) -> bool): int { if (f(x)) { return 1; } return 0; }
fn main(): int {
    println(fromInt(apply(3) { it > 1 }));
    return 0;
}
EOF
cat > r10.lyr <<'EOF'
import std.io.console { println };
interface Speaker { fn speak(): int; }
struct S :: [Speaker] { v: int, fn speak(): int { return this.v; } }
fn mk(): S { return S { v = 1 }; }
fn take(s: Speaker): int { return s.speak(); }
fn main(): int {
    let f: fn() -> Speaker = mk;
    let g: fn(S) -> int = take;
    println("x");
    return 0;
}
EOF
cat > r10b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
interface Speaker { fn speak(): int; }
struct S :: [Speaker] { v: int, fn speak(): int { return this.v; } }
fn main(): int {
    let f: fn() -> Speaker = () => { return S { v = 7 }; };
    let g: fn(S) -> int = (s: S) => { let sp: Speaker = s; return sp.speak(); };
    println(fromInt(f().speak()));
    println(fromInt(g(S { v = 3 })));
    return 0;
}
EOF
cat > r11.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn main(): int {
    var x = 1;
    defer { println(fromInt(x)); }
    x = 2;
    return 0;
}
EOF
cat > r11b.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    defer { return 3; }
    return 0;
}
EOF
cat > r11c.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    for (i in 0..3) {
        defer { break; }
    }
    return 0;
}
EOF
cat > r11d.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn main(): int {
    let f = () => { defer { println("d"); } println("body"); return 1; };
    println("before");
    let r = f();
    println("after " + fromInt(r));
    return 0;
}
EOF
cat > r12.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn gen(): Coroutine<int> {
    var n = 0;
    let inc = () => { n = n + 1; return n; };
    yield inc();
    yield inc();
}
fn main(): int {
    let c = gen();
    println(fromInt(resume c));
    println(fromInt(resume c));
    return 0;
}
EOF
cat > r13.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn main(): int {
    let n = 1;
    let f = (n: int) => n + 1;
    println(fromInt(f(n)));
    return 0;
}
EOF
cat > r13c.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn main(): int {
    let n = 1;
    {
        let n = 2;
        println(fromInt(n));
    }
    return 0;
}
EOF
cat > r16.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn side(s: string): int { println(s); return 1; }
fn f(a: int = side("a"), b: int = side("b")): int { return a + b; }
fn main(): int {
    println(fromInt(f()));
    println(fromInt(f(1)));
    println(fromInt(f(side("x"))));
    return 0;
}
EOF
cat > r17.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
struct H { f: fn(int) -> int }
fn main(): int {
    let h = H { f = (n: int) => n + 1 };
    println(fromInt(h.f(1)));
    let g = h.f;
    println(fromInt(g(10)));
    return 0;
}
EOF
cat > r17b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
struct H { f: fn(int) -> int, fn f(n: int): int { return n + 100; } }
fn main(): int {
    let h = H { f = (n: int) => n + 1 };
    println(fromInt(h.f(1)));
    return 0;
}
EOF
cat > r20.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn run(v: int, f: fn(int) -> int): int { return f(v); }
fn main(): int {
    println(fromInt(run(5) { let g = (n: int) => it + n; g(1) }));
    return 0;
}
EOF
cat > r20b.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn run(v: int, f: fn(int) -> int): int { return f(v); }
fn main(): int {
    println(fromInt(run(5) { let g = (n: int) => { return it + n; }; return g(1); }));
    return 0;
}
EOF
cat > t_direct.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn inc(n: int): int { return n + 1; }
fn main(): int {
    var acc = 0;
    var i = 0;
    while (i < 3000000) { acc = inc(acc); i = i + 1; }
    println(fromInt(acc));
    return 0;
}
EOF
cat > t_ind.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn inc(n: int): int { return n + 1; }
fn main(): int {
    var f: fn(int) -> int = inc;
    var acc = 0;
    var i = 0;
    while (i < 3000000) { acc = f(acc); i = i + 1; }
    println(fromInt(acc));
    return 0;
}
EOF
cat > t_virt.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
interface Inc { fn inc(n: int): int; }
struct I :: [Inc] { v: int, fn inc(n: int): int { return n + 1; } }
fn main(): int {
    let d: Inc = I { v = 0 };
    var acc = 0;
    var i = 0;
    while (i < 3000000) { acc = d.inc(acc); i = i + 1; }
    println(fromInt(acc));
    return 0;
}
EOF
cat > r22.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn apply(f: fn(int) -> int, v: int): int { return f(v); }
fn run(f: fn(int) -> int): int { return f(1); }
fn main(): int {
    println(fromInt(apply(x => x * 3, 2)));
    println(fromInt(run { it + 7 }));
    return 0;
}
EOF
echo generated; ls
