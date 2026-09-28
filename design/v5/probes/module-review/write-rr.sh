#!/bin/bash
P="C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/module-review"
mk(){ mkdir -p "$P/$1"; }

cat > "$P/EXPECT-rr.txt" <<'EOF'
Erwartungen VOR dem Lauf (Review-Runde rr, 2026-09-27):
rr01  qualifizierter Zugriff auf nicht-pub fn/global/struct/static let/enum/interface/extend-Ziel: KOMPILIERT, druckt 7 11 3 5 fast 30 4 3
rr01k import lib { hidden }: LYR-RES0004
rr02a lib.h(1) mit zwei nicht-pub Ueberladungen: kompiliert, 1
rr02b lib.h("a"): LYR-SEM0001 (Dossier); wenn nicht: Dossier falsch
rr02k beide pub, lib.h("a"): 2
rr03  app.lyr ohne Header: lyrvm info zeigt entry main.main; hdr.lyr mit module totally.different.name: entry totally.different.name.main
rr04  module std.core; in Entry: Fehler in stdlib-Dateien, 0 Treffer fuer main.lyr
rr05  Zyklus a->b->c->a: genau EIN LYR-RES0005, an c.lyr:1:1
rr06  import shapes.circle; + import shapes.circle as C;: kompiliert, 12 12
rr07a let lib = 5 LOKAL neben import lib: kompiliert wortlos, gibt 5
rr07b let lib = 5 TOP-LEVEL neben import lib: LYR-RES0001 (meine Erwartung, im Dossier nicht unterschieden)
rr08  import Lib; auf Windows mit lib.lyr: kompiliert, 42
rr09  extend int ohne pub in lib, von aussen: 12
rr10  zwei Module erweitern int mit twice: LYR-SEM0044; rr10b selektiv import e1 { ping }: derselbe SEM0044
rr11a pub import: LYR-PAR0025; rr11b import im Rumpf: LYR-PAR0002; rr11c import am Ende: laeuft
rr12  pub(package)/package/internal: 3x LYR-PAR0025
rr13a import lib; unbenutzt: KEINE Warnung; rr13b import lib { f }; unbenutzt: LYR-SEM0072
rr14  Re-Export mid.f(): LYR-SEM0012
rr15  Bibliothek: lyrvm info zeigt api.newBox UND api.helper, kein Box.get
rr16a std.io.console.println ohne Import: LYR-SEM0002 'std'; rr16b import std;: LYR-RES0003
rr17  import lib { f as g }: Parse-Fehler
rr18  import a.b; import b;: LYR-RES0001
rr19  import lib; import lib { f };  (Formen kombiniert): UNBEKANNT - vermutlich RES0001 "duplicate import"
rr20  app.lyr (=main) importiert b, b importiert app: UNBEKANNT - vermutlich app.lyr wird zweimal geladen (main + app), kein RES0005
rr21  Mangling: Modul a Struct b Methode c  vs Modul a.b fn c: UNBEKANNT - vermutlich Kollision "a.b.c" (Verifier/Lowering-Fehler) oder stille Fehlwahl
rr22  import std.io.consol: LYR-RES0003 ohne did-you-mean
rr23  fn main in lib UND in main: UNBEKANNT - vermutlich Fehler "more than one main" oder stilles Ignorieren
rr24  nicht-pub extern in lib, lib.cbrt(8.0) von aussen: kompiliert, 2
rr25  pub x: int als Feld: Parse-Fehler (Dossier sagt "pub vor jedem Member parst")
rr26  hello world --verbose: Zeit fuer 5 vorgeladene Module messbar
EOF

mk rr01
cat > "$P/rr01/lib.lyr" <<'EOF'
fn hidden(): int { return 7; }
let secretGlobal = 11;
struct Secret { v: int, }
pub class Counter {
    static let HIDDEN: int = 5;
    pub static let SHOWN: int = 9;
}
enum Mode { Fast, Slow }
interface HiddenI { fn size(): int; }
pub fn make(): Secret { return Secret { v = 3 }; }
EOF
cat > "$P/rr01/main.lyr" <<'EOF'
import std.io.console { println };
import lib;
struct Box :: [lib.HiddenI] { n: int, fn size(): int { return this.n; } }
extend lib.Secret { fn grafted(): int { return this.v * 10; } }
fn bounded<T :: [lib.HiddenI]>(x: T): int { return x.size(); }
fn main(): int {
    println(lib.hidden());
    println(lib.secretGlobal);
    let s: lib.Secret = lib.Secret { v = 3 };
    println(s.v);
    println(lib.Counter.HIDDEN);
    let m = lib.Mode.Fast;
    match (m) { lib.Mode.Fast => println("fast"), _ => println("other") }
    println(lib.make().grafted());
    println(bounded(Box { n = 4 }));
    println(lib.make().v);
    return 0;
}
EOF
mk rr01k; cp "$P/rr01/lib.lyr" "$P/rr01k/"
cat > "$P/rr01k/main.lyr" <<'EOF'
import std.io.console { println };
import lib { hidden };
fn main(): int { println(hidden()); return 0; }
EOF

for d in rr02a rr02b; do
mk $d
cat > "$P/$d/lib.lyr" <<'EOF'
fn h(a: int): int { return 1; }
fn h(a: string): int { return 2; }
EOF
done
mk rr02k
cat > "$P/rr02k/lib.lyr" <<'EOF'
pub fn h(a: int): int { return 1; }
pub fn h(a: string): int { return 2; }
EOF
cat > "$P/rr02a/main.lyr" <<'EOF'
import std.io.console { println };
import lib;
fn main(): int { println(lib.h(1)); return 0; }
EOF
cat > "$P/rr02b/main.lyr" <<'EOF'
import std.io.console { println };
import lib;
fn main(): int { println(lib.h("a")); return 0; }
EOF
cp "$P/rr02b/main.lyr" "$P/rr02k/main.lyr"

mk rr03
cat > "$P/rr03/app.lyr" <<'EOF'
fn main(): int { return 0; }
EOF
cat > "$P/rr03/hdr.lyr" <<'EOF'
module totally.different.name;
fn main(): int { return 0; }
EOF

mk rr04
cat > "$P/rr04/main.lyr" <<'EOF'
module std.core;
fn main(): int { return 0; }
EOF

mk rr05
cat > "$P/rr05/a.lyr" <<'EOF'
import b;
pub fn fa(): int { return 1; }
EOF
cat > "$P/rr05/b.lyr" <<'EOF'
import c;
pub fn fb(): int { return 2; }
EOF
cat > "$P/rr05/c.lyr" <<'EOF'
import a;
pub fn fc(): int { return 3; }
EOF
cat > "$P/rr05/main.lyr" <<'EOF'
import a;
fn main(): int { return a.fa(); }
EOF

mk rr06/shapes
cat > "$P/rr06/shapes/circle.lyr" <<'EOF'
pub fn area(r: int): int { return 3 * r * r; }
EOF
cat > "$P/rr06/main.lyr" <<'EOF'
import std.io.console { println };
import shapes.circle;
import shapes.circle as C;
fn main(): int { println(circle.area(2)); println(C.area(2)); return 0; }
EOF

for d in rr07a rr07b; do
mk $d
cat > "$P/$d/lib.lyr" <<'EOF'
pub fn f(): int { return 1; }
EOF
done
cat > "$P/rr07a/main.lyr" <<'EOF'
import lib;
fn main(): int { let lib = 5; return lib; }
EOF
cat > "$P/rr07b/main.lyr" <<'EOF'
import lib;
let lib = 5;
fn main(): int { return lib; }
EOF

mk rr08
cat > "$P/rr08/lib.lyr" <<'EOF'
pub fn f(): int { return 42; }
EOF
cat > "$P/rr08/main.lyr" <<'EOF'
import Lib;
fn main(): int { return Lib.f(); }
EOF

mk rr09
cat > "$P/rr09/lib.lyr" <<'EOF'
extend int { fn thrice(): int { return this * 3; } }
EOF
cat > "$P/rr09/main.lyr" <<'EOF'
import lib;
fn main(): int { return (4).thrice(); }
EOF

for d in rr10 rr10b; do
mk $d
cat > "$P/$d/e1.lyr" <<'EOF'
pub fn ping(): int { return 1; }
pub extend int { pub fn twice(): int { return this * 2; } }
EOF
cat > "$P/$d/e2.lyr" <<'EOF'
pub fn pong(): int { return 2; }
pub extend int { pub fn twice(): int { return this * 2; } }
EOF
done
cat > "$P/rr10/main.lyr" <<'EOF'
import e1;
import e2;
fn main(): int { return (3).twice(); }
EOF
cat > "$P/rr10b/main.lyr" <<'EOF'
import e1 { ping };
import e2;
fn main(): int { return (3).twice() + ping(); }
EOF

for d in rr11a rr11b rr11c; do
mk $d
cat > "$P/$d/lib.lyr" <<'EOF'
pub fn f(): int { return 6; }
EOF
done
cat > "$P/rr11a/main.lyr" <<'EOF'
pub import lib { f };
fn main(): int { return f(); }
EOF
cat > "$P/rr11b/main.lyr" <<'EOF'
fn main(): int { import lib { f }; return f(); }
EOF
cat > "$P/rr11c/main.lyr" <<'EOF'
fn main(): int { return f(); }
import lib { f };
EOF

mk rr12
cat > "$P/rr12/m1.lyr" <<'EOF'
pub(package) fn a(): int { return 1; }
fn main(): int { return 0; }
EOF
cat > "$P/rr12/m2.lyr" <<'EOF'
package fn a(): int { return 1; }
fn main(): int { return 0; }
EOF
cat > "$P/rr12/m3.lyr" <<'EOF'
internal fn a(): int { return 1; }
fn main(): int { return 0; }
EOF

for d in rr13a rr13b; do
mk $d
cat > "$P/$d/lib.lyr" <<'EOF'
pub fn f(): int { return 6; }
EOF
done
cat > "$P/rr13a/main.lyr" <<'EOF'
import lib;
fn main(): int { return 0; }
EOF
cat > "$P/rr13b/main.lyr" <<'EOF'
import lib { f };
fn main(): int { return 0; }
EOF

mk rr14
cat > "$P/rr14/leaf.lyr" <<'EOF'
pub fn f(): int { return 8; }
EOF
cat > "$P/rr14/mid.lyr" <<'EOF'
import leaf { f };
pub fn g(): int { return f(); }
EOF
cat > "$P/rr14/main.lyr" <<'EOF'
import mid;
fn main(): int { return mid.f(); }
EOF

mk rr15
cat > "$P/rr15/api.lyr" <<'EOF'
pub class Box {
    n: int,
    pub fn get(): int { return this.n; }
    pub mut fn bump() { this.n = this.n + 1; }
}
pub struct Pt { x: int, y: int, }
pub extend Pt { pub fn dist(): int { return this.x + this.y; } }
fn helper(): int { return 1; }
pub fn newBox(): Box { return Box { n = helper() }; }
EOF

mk rr16a
cat > "$P/rr16a/main.lyr" <<'EOF'
fn main(): int { std.io.console.println("x"); return 0; }
EOF
mk rr16b
cat > "$P/rr16b/main.lyr" <<'EOF'
import std;
fn main(): int { return 0; }
EOF

mk rr17
cat > "$P/rr17/lib.lyr" <<'EOF'
pub fn f(): int { return 6; }
EOF
cat > "$P/rr17/main.lyr" <<'EOF'
import lib { f as g };
fn main(): int { return g(); }
EOF

mk rr18/a
cat > "$P/rr18/a/b.lyr" <<'EOF'
pub fn x(): int { return 1; }
EOF
cat > "$P/rr18/b.lyr" <<'EOF'
pub fn y(): int { return 2; }
EOF
cat > "$P/rr18/main.lyr" <<'EOF'
import a.b;
import b;
fn main(): int { return 0; }
EOF

mk rr19
cat > "$P/rr19/lib.lyr" <<'EOF'
pub fn f(): int { return 6; }
pub fn g(): int { return 7; }
EOF
cat > "$P/rr19/main.lyr" <<'EOF'
import lib;
import lib { f };
fn main(): int { return f() + lib.g(); }
EOF

mk rr20
cat > "$P/rr20/app.lyr" <<'EOF'
import b;
pub fn fa(): int { return 1; }
fn main(): int { return b.fb(); }
EOF
cat > "$P/rr20/b.lyr" <<'EOF'
import app;
pub fn fb(): int { return app.fa() + 10; }
EOF

mk rr21/a
cat > "$P/rr21/a.lyr" <<'EOF'
pub struct b { n: int, pub fn c(): int { return 1; } }
EOF
cat > "$P/rr21/a/b.lyr" <<'EOF'
pub fn c(): int { return 2; }
EOF
cat > "$P/rr21/main.lyr" <<'EOF'
import std.io.console { println };
import a;
import a.b;
fn main(): int { let x = a.b { n = 0 }; println(x.c()); println(b.c()); return 0; }
EOF

mk rr22
cat > "$P/rr22/main.lyr" <<'EOF'
import std.io.consol { println };
fn main(): int { return 0; }
EOF

mk rr23
cat > "$P/rr23/lib.lyr" <<'EOF'
fn main(): int { return 1; }
pub fn f(): int { return 5; }
EOF
cat > "$P/rr23/main.lyr" <<'EOF'
import lib;
fn main(): int { return lib.f(); }
EOF

mk rr24
cat > "$P/rr24/lib.lyr" <<'EOF'
extern "dotnet" fn cbrt(x: float): float = "System.Math::Cbrt";
EOF
cat > "$P/rr24/main.lyr" <<'EOF'
import std.io.console { println };
import lib;
fn main(): int { println(lib.cbrt(8.0)); return 0; }
EOF

mk rr25
cat > "$P/rr25/main.lyr" <<'EOF'
struct S { pub x: int, }
fn main(): int { return 0; }
EOF

mk rr26
cat > "$P/rr26/main.lyr" <<'EOF'
import std.io.console { println };
fn main(): int { println("hi"); return 0; }
EOF
echo written
