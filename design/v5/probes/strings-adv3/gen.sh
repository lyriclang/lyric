#!/bin/bash
cd "$(dirname "$0")"
cat > ERWARTUNG.txt <<'EOF'
Erwartungen VOR dem Lauf (adversarische Nachpruefung strings.md):
a01 astral char literal '😀'      -> LEX0008 "got 2"; '\u{1F600}' ok
a02 f"{3.14159:.2}"               -> 32 ; .3 -> 33 ; F2 -> 3.14 (Kontrolle)
a03 f"[{42:8}]"                   -> [42      ] ; -8 -> [      42] ; 08 -> [42      ] ; D8 -> [00000042]
a04 f"[{42:{w}}]"                 -> [{w}] plus SEM0071 'w' never used
a05 f"{-255:X}" -> FFFFFFFFFFFFFF01 ; {-255:b} -> 64 bit ; {3.14159:e} -> 3.141590e+000 ; {255:o} -> panic VM0006
a06 println(int32)                -> SEM0028 ; println(uint) -> SEM0028 ; f"{a}" ok
a07 "ß".toUpper() -> ß ; "é".toUpper() -> É ; isUpper('É') false
a08 "{{ x }}" -> {{ x }} ; f"{{ x }}" -> { x } ; f"a } b" -> a } b
a09 let d: Display = 5            -> SEM0001
a10 f"[{emoji:6}]"                -> 4 Leerzeichen bei length()==1
a11 match string/char literals    -> 1 2 3 / 10 20 30
a12 "\u{1F600}" * 2000000000     -> panic "2 code point(s)"
a13 "\e"                          -> LEX0007
a14 "ab" * -2                     -> "" (leer)
a15 "\xE2\x82\xAC"                -> length 3, utf8 6
a16 "b" > "a" -> true ; "a" < "b" -> true (Ordered ueber core)
a17 import std.string { fromInt } + s.length() -> SEM0072 warning trotz Nutzung
a18 r"abc"                        -> SEM0002 unknown identifier 'r'
a19 """ dreizeilig                -> 18 Diagnosen, erste LEX0009
a20 comptime f"n={1+1}"           -> n=2
a21 f"{42:'abc'}" -> abc ; f"{3.7:C}" -> ¤3.70 ; f"{0.5:P1}" -> 50.0 % ; f"{1234.5:#,##0.00}" -> 1,234.50
a22 55296 as char                 -> panic VM0012
Neue Fragen (nicht im Dossier):
b01 'a' < 'b' (char ordering)     -> unbekannt; Bytecode sagt lt numerisch -> vermutlich SEM-Fehler oder geht
b02 "\0" embedded NUL: length 3 fuer "a\0b", println schreibt es
b03 charAt(3) / charAt(-1) auf "abc" -> panic VM0006?
b04 f"{s:}" leerer Spec           -> vermutlich unveraendert
b05 f"{x:>8}"                      -> FormatException -> panic VM0006
b07 "é" == "e\u{301}" -> false ; b.toUpper() Laenge 2
b08 hash("") und hash("a"), compare mit ""
b09 f"{a:8}" mit int32, {u:X} mit uint8, {a:X} mit int32 -7 -> 64-bit Bits? (Weitung auf i64)
b10 Backslash-Zeilenfortsetzung in String -> LEX0007 oder LEX0009
b11 Spec auf char/bool: {c:5} {c:X} {true:5} -> ?
b12 == nach concat true; substring(1,10) ueber Ende -> panic oder clamp?
EOF
cat > a01.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let esc = '\u{1F600}';
    let direct = '😀';
    println(f"{esc as int} {direct as int}");
    return 0;
}
EOF
cat > a02.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let x = 3.14159;
    println(f"[{x:.2}] [{x:.3}] [{x:F2}] [{x:.}]");
    return 0;
}
EOF
cat > a03.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println(f"[{42:8}] [{42:-8}] [{42:08}] [{42:D8}]");
    return 0;
}
EOF
cat > a04.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let w = 8;
    println(f"[{42:{w}}]");
    println(f"[{42:(a)[b]}]");
    return 0;
}
EOF
cat > a05.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println(f"{-255:X}|{-255:b}|{3.14159:e}|{255:x}");
    println(f"{255:o}");
    return 0;
}
EOF
cat > a06.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let a: int32 = 7;
    let u: uint = 3;
    let i: int = 1;
    println(f"{a} {u}");
    println(i);
    println(a);
    println(u);
    return 0;
}
EOF
cat > a07.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let sz = "ß";
    let e = "é";
    let up = e.toUpper();
    println(f"{sz.toUpper()} {sz.toUpper().length()} {up} {strings.isUpper(up.charAt(0))} {strings.isAlpha('é')}");
    let fi = "\u{FB01}";
    let dot = "\u{130}";
    println(f"{fi.toUpper().length()} {dot.toLower().length()}");
    return 0;
}
EOF
cat > a08.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println("{{ plain }}");
    println(f"{{ fstring }}");
    println(f"single close } here");
    return 0;
}
EOF
cat > a09.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let d: Display = 5;
    println(d);
    return 0;
}
EOF
cat > a10.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let emoji = "\u{1F600}";
    let plain = "ab";
    let cjk = "世界";
    println(f"[{emoji:6}] {emoji.length()}");
    println(f"[{plain:6}] {plain.length()}");
    println(f"[{cjk:6}] {cjk.length()}");
    return 0;
}
EOF
cat > a11.lyr <<'EOF'
import std.io.console { println };
fn pick(s: string): int { return match (s) { "a" => 1, "b" => 2, _ => 3 }; }
fn pickc(c: char): int { return match (c) { 'x' => 10, 'y' => 20, _ => 30 }; }
fn main(): int {
    println(f"{pick("a")} {pick("b")} {pick("zz")} {pickc('x')} {pickc('y')} {pickc('q')}");
    return 0;
}
EOF
cat > a12.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let e = "\u{1F600}";
    println(f"cp={e.length()}");
    let big = e * 2000000000;
    println(big.length());
    return 0;
}
EOF
cat > a13.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println("\e[31m");
    return 0;
}
EOF
cat > a14.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "ab" * -2;
    println(f"[{s}] {s.length()}");
    return 0;
}
EOF
cat > a15.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "\xE2\x82\xAC";
    println(f"{s.length()} {s.utf8Encode().length} {'\xFF' as int}");
    return 0;
}
EOF
cat > a16.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println(f"{"b" > "a"} {"a" < "b"} {"a" < "a"} {"" < "a"}");
    return 0;
}
EOF
cat > a17.lyr <<'EOF'
import std.io.console { println };
import std.string { fromInt };
fn main(): int {
    let s = "hello";
    println(s.length());
    return 0;
}
EOF
cat > a18.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let s = r"abc";
    println(s);
    return 0;
}
EOF
cat > a19.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let help = """
usage: tool [options]
  -h   help
""";
    println(help);
    return 0;
}
EOF
cat > a20.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let s = comptime f"n={1+1}";
    println(s);
    return 0;
}
EOF
cat > a21.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println(f"[{42:'abc'}] [{3.7:C}] [{0.5:P1}] [{1234.5:#,##0.00}]");
    return 0;
}
EOF
cat > a22.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let n = 55296;
    let c = n as char;
    println(c);
    return 0;
}
EOF
cat > b01.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    println(f"{'a' < 'b'} {'b' > 'a'}");
    return 0;
}
EOF
cat > b02.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "a\0b";
    println(f"{s.length()} {s.utf8Encode().length} [{s}]");
    return 0;
}
EOF
cat > b03.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "abc";
    println(s.charAt(3) as int);
    return 0;
}
EOF
cat > b03b.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "abc";
    println(s.charAt(-1) as int);
    return 0;
}
EOF
cat > b04.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let s = "x";
    let n = 5;
    println(f"[{s:}] [{n:}]");
    return 0;
}
EOF
cat > b05.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let n = 5;
    println(f"[{n:>8}]");
    return 0;
}
EOF
cat > b07.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let a = "\u{E9}";
    let b = "e\u{301}";
    println(f"{a == b} {a.length()} {b.length()} {b.toUpper()} {b.toUpper().length()}");
    return 0;
}
EOF
cat > b08.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let e = "";
    let a = "a";
    println(f"{e.hash()} {a.hash()} {e.compare(a)} {a.compare(e)}");
    return 0;
}
EOF
cat > b09.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let a: int32 = -7;
    let u: uint8 = 200;
    let f: float32 = 1.5;
    println(f"[{a:8}] [{u:X}] [{f:F3}] [{a:X}]");
    return 0;
}
EOF
cat > b10.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let s = "abc\
def";
    println(s);
    return 0;
}
EOF
cat > b11.lyr <<'EOF'
import std.io.console { println };
fn main(): int {
    let c = 'a';
    let s = "x";
    println(f"{c:5}|{c:X}|{true:5}|{s:3}");
    return 0;
}
EOF
cat > b12.lyr <<'EOF'
import std.io.console { println };
import std.string as strings;
fn main(): int {
    let s = "hello";
    let t = s + "";
    let u = "hel" + "lo";
    println(f"{s == t} {s == u} {s.substring(1, 10)}");
    return 0;
}
EOF
cat > run.sh <<'EOF'
#!/bin/bash
cd "$(dirname "$0")"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for f in "$@"; do
  echo "=================== $f"
  dotnet "$LYRC" build "$f.lyr" -o "$f.lyrbc" 2>&1 | head -40
  echo "--- build exit ${PIPESTATUS[0]}"
  if [ -f "$f.lyrbc" ]; then
    dotnet "$LYRVM" run "$f.lyrbc" 2>&1 | head -20
    echo "--- run exit ${PIPESTATUS[0]}"
  fi
done
EOF
echo generated
ls
