#!/bin/bash
P="C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/module-review"
mk(){ mkdir -p "$P/$1"; }
cat >> "$P/EXPECT-rr.txt" <<'EOF'
--- Runde 2 (nach den Befunden rr20/rr21/rr15/rr25):
rr21k KONTROLLE: Struct heisst bb statt b: kompiliert, druckt 1 2
rr27  app.lyr importiert app (eigener Dateiname): app.lyr wird zweimal geladen (main + app): SEM0021 duplicate main, evtl. RES0005
rr28  panic("x") ohne Import: kompiliert (Spec 4.4 special edge) -> Lyric HAT implizite Namen
rr29  lib.lyr mit module lib; und import Lib;: LYR-RES0006 mit Pfad ...\Lib.lyr (Datei existiert nicht)
rr30  interface I { pub fn f(): int; }: parst
rr31  Projekt sourceRoot/testRoot, Test ruft calc.internalDouble (nicht pub) qualifiziert: lyric test PASS
rr32  Bibliothek MIT Header module api;: lyrvm info zeigt api.newBox und api.helper (Dossier r14); ohne Header hiess sie main.* (rr15)
rr33  KONTROLLE zu rr20: main.lyr importiert b, b importiert main: RES0005 (FindModule findet main), KEIN doppeltes main
EOF
mk rr21k/a
cat > "$P/rr21k/a.lyr" <<'EOF'
pub struct bb { n: int, pub fn c(): int { return 1; } }
EOF
cat > "$P/rr21k/a/b.lyr" <<'EOF'
pub fn c(): int { return 2; }
EOF
cat > "$P/rr21k/main.lyr" <<'EOF'
import std.io.console { println };
import a;
import a.b;
fn main(): int { let x = a.bb { n = 0 }; println(x.c()); println(b.c()); return 0; }
EOF
mk rr27
cat > "$P/rr27/app.lyr" <<'EOF'
import app;
pub fn fa(): int { return 1; }
fn main(): int { return app.fa(); }
EOF
mk rr28
cat > "$P/rr28/main.lyr" <<'EOF'
fn main(): int { if (1 > 2) { panic("x"); } return 3; }
EOF
mk rr29
cat > "$P/rr29/lib.lyr" <<'EOF'
module lib;
pub fn f(): int { return 42; }
EOF
cat > "$P/rr29/main.lyr" <<'EOF'
import Lib;
fn main(): int { return Lib.f(); }
EOF
mk rr30
cat > "$P/rr30/main.lyr" <<'EOF'
interface I { pub fn f(): int; }
struct S :: [I] { n: int, fn f(): int { return this.n; } }
fn main(): int { return S { n = 4 }.f(); }
EOF
mk rr31/src; mk rr31/tests
cat > "$P/rr31/lyric.json" <<'EOF'
{ "sourceRoot": "src", "testRoot": "tests" }
EOF
cat > "$P/rr31/src/calc.lyr" <<'EOF'
fn internalDouble(x: int): int { return x * 2; }
pub fn quad(x: int): int { return internalDouble(internalDouble(x)); }
EOF
cat > "$P/rr31/tests/calc_test.lyr" <<'EOF'
import std.test { assertEq };
import calc;
@Test
fn doubles() { assertEq(calc.internalDouble(3), 6); }
EOF
mk rr32
sed '1i module api;' "$P/rr15/api.lyr" > "$P/rr32/api.lyr"
mk rr33
cat > "$P/rr33/main.lyr" <<'EOF'
import b;
pub fn fa(): int { return 1; }
fn main(): int { return b.fb(); }
EOF
cat > "$P/rr33/b.lyr" <<'EOF'
import main;
pub fn fb(): int { return main.fa() + 10; }
EOF
echo written2
