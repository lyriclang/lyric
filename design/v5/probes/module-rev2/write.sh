#!/bin/bash
set -e
cd "$(dirname "$0")"
mkdir -p q01 q01k q02 q03 q03b q03c q03d q04 q04k q05a q05b q06 q06k q07/a q07k/a q08 q08k q08c q09 q10 q10k q11 q12 q12b q12c q13 q14/src q14/tests q14k/src q14k/tests

cat > q01/main.lyr <<'EOF'
struct S { pub x: int, }
fn main(): int { return 0; }
EOF
cat > q01k/main.lyr <<'EOF'
struct S { x: int, pub fn f(): int { return this.x; } }
fn main(): int { return S { x = 4 }.f(); }
EOF
cat > q02/main.lyr <<'EOF'
interface I { pub fn f(): int; }
struct S :: [I] { n: int, fn f(): int { return this.n; } }
fn main(): int { return S { n = 4 }.f(); }
EOF
cat > q03/main.lyr <<'EOF'
fn main(): int { if (1 > 2) { panic("x"); } return 3; }
EOF
cat > q03b/main.lyr <<'EOF'
import std.io.console { println };
fn panic(msg: string): int { return 9; }
fn main(): int { println(panic("x")); return 0; }
EOF
cat > q03c/mylib.lyr <<'EOF'
pub fn panic(msg: string): int { return 9; }
EOF
cat > q03c/main.lyr <<'EOF'
import std.io.console { println };
import mylib { panic };
fn main(): int { println(panic("x")); return 0; }
EOF
cat > q03d/main.lyr <<'EOF'
import std.io.console { println };
fn concat(a: string, b: string): string { return "X"; }
fn main(): int { let s = "a" + "b"; println(s); println(concat("a", "b")); println(f"{1}-{2}"); return 0; }
EOF
cat > q04/api.lyr <<'EOF'
pub class Box {
    n: int,
    pub fn get(): int { return this.n; }
}
fn helper(): int { return 1; }
pub fn newBox(): Box { return Box { n = helper() }; }
EOF
{ echo "module api;"; cat q04/api.lyr; } > q04k/api.lyr
cat > q05a/lib.lyr <<'EOF'
pub fn f(): int { return 6; }
EOF
cp q05a/lib.lyr q05b/lib.lyr
cat > q05a/main.lyr <<'EOF'
import lib;
fn main(): int { let lib = 5; return lib; }
EOF
cat > q05b/main.lyr <<'EOF'
import lib;
let lib = 5;
fn main(): int { return 0; }
EOF
cat > q06/lib.lyr <<'EOF'
extern "dotnet" fn cbrt(x: float): float = "System.Math::Cbrt";
EOF
cp q06/lib.lyr q06k/lib.lyr
cat > q06/main.lyr <<'EOF'
import std.io.console { println };
import lib;
fn main(): int { println(lib.cbrt(8.0)); return 0; }
EOF
cat > q06k/main.lyr <<'EOF'
import std.io.console { println };
import lib { cbrt };
fn main(): int { println(cbrt(8.0)); return 0; }
EOF
cat > q07/a.lyr <<'EOF'
pub struct b { n: int, pub fn c(): int { return 1; } }
EOF
cat > q07/a/b.lyr <<'EOF'
pub fn c(): int { return 2; }
EOF
cat > q07/main.lyr <<'EOF'
import std.io.console { println };
import a;
import a.b;
fn main(): int { let x = a.b { n = 0 }; println(x.c()); println(b.c()); return 0; }
EOF
cat > q07k/a.lyr <<'EOF'
pub struct bb { n: int, pub fn c(): int { return 1; } }
EOF
cp q07/a/b.lyr q07k/a/b.lyr
cat > q07k/main.lyr <<'EOF'
import std.io.console { println };
import a;
import a.b;
fn main(): int { let x = a.bb { n = 0 }; println(x.c()); println(b.c()); return 0; }
EOF
cat > q08/app.lyr <<'EOF'
import b;
pub fn fa(): int { return 1; }
fn main(): int { return b.fb(); }
EOF
cat > q08/b.lyr <<'EOF'
import app;
pub fn fb(): int { return app.fa() + 10; }
EOF
cat > q08k/main.lyr <<'EOF'
import b;
pub fn fa(): int { return 1; }
fn main(): int { return b.fb(); }
EOF
cat > q08k/b.lyr <<'EOF'
import main;
pub fn fb(): int { return main.fa() + 10; }
EOF
cat > q08c/app.lyr <<'EOF'
import app;
pub fn fa(): int { return 1; }
fn main(): int { return app.fa(); }
EOF
cat > q09/lib.lyr <<'EOF'
fn main(): int { return 1; }
pub fn f(): int { return 5; }
EOF
cat > q09/main.lyr <<'EOF'
import lib;
fn main(): int { return lib.f(); }
EOF
cat > q10/main.lyr <<'EOF'
import std.io.consol { println };
fn main(): int { return 0; }
EOF
cat > q10k/main.lyr <<'EOF'
fn f(): int { return 1; }
fn main(): int { return g(); }
EOF
cat > q11/main.lyr <<'EOF'
import std.io.console { println };
fn main(): int { println("hi"); return 0; }
EOF
cat > q12/lib.lyr <<'EOF'
pub fn f(): int { return 6; }
pub fn g(): int { return 7; }
EOF
cp q12/lib.lyr q12b/lib.lyr; cp q12/lib.lyr q12c/lib.lyr
cat > q12/main.lyr <<'EOF'
import lib;
import lib { f };
fn main(): int { return f() + lib.g(); }
EOF
cat > q12b/main.lyr <<'EOF'
import lib;
import lib { f };
fn main(): int { return f(); }
EOF
cat > q12c/main.lyr <<'EOF'
import lib as L;
import lib { f };
fn main(): int { return f(); }
EOF
cat > q13/lib.lyr <<'EOF'
pub fn ident<T>(x: T): T { return x; }
EOF
cat > q13/main.lyr <<'EOF'
import std.io.console { println };
import lib;
struct Secret { v: int, }
fn main(): int { println(lib.ident(Secret { v = 3 }).v); return 0; }
EOF
cat > q14/lyric.json <<'EOF'
{ "sourceRoot": "src", "testRoot": "tests" }
EOF
cp q14/lyric.json q14k/lyric.json
cat > q14/src/calc.lyr <<'EOF'
fn internalDouble(x: int): int { return x * 2; }
pub fn quad(x: int): int { return internalDouble(internalDouble(x)); }
EOF
cp q14/src/calc.lyr q14k/src/calc.lyr
cat > q14/tests/calc_test.lyr <<'EOF'
import std.test { Test, assertEq };
import calc;
@Test
fn doubles() { assertEq(calc.internalDouble(3), 6); }
EOF
cat > q14k/tests/calc_test.lyr <<'EOF'
import std.test { Test, assertEq };
import calc { internalDouble };
@Test
fn doubles() { assertEq(internalDouble(3), 6); }
EOF
echo written
