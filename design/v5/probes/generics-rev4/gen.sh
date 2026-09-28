#!/bin/bash
cd "$(dirname "$0")"
mkdir -p out
# q01: sound contravariant direction. ERWARTUNG: SEM0001 (strikt invariant)
cat > q01_fn_contra_sound.lyr <<'EOF'
interface Shape { fn area(): int; }
class Circle :: [Shape] { fn area(): int { return 1; } }
fn apply(f: fn(Circle) -> int, c: Circle): int { return f(c); }
fn main(): int {
  let g = (s: Shape) => s.area();
  return apply(g, Circle {});
}
EOF
# q01c: Kontrolle, exakt gleiche Typen. ERWARTUNG: kompiliert, Exit 1
cat > q01c_fn_exact_control.lyr <<'EOF'
interface Shape { fn area(): int; }
class Circle :: [Shape] { fn area(): int { return 1; } }
fn apply(f: fn(Circle) -> int, c: Circle): int { return f(c); }
fn main(): int {
  let g = (c: Circle) => c.area();
  return apply(g, Circle {});
}
EOF
# q02: Interface-Member mit Ergebnistyp It<T[]>. ERWARTUNG: IR0001 ohne Position (Rundenwaechter)
cat > q02_guard_iface_member.lyr <<'EOF'
interface Nest<T> { fn nest(): It<T[]>; }
class It<T> :: [Nest<T>] {
  v: T,
  fn nest(): It<T[]> { return It<T[]> { v = [this.v] }; }
}
fn main(): int {
  let a = It<int> { v = 1 };
  let n: Nest<int> = a;
  let _ = n.nest();
  return 0;
}
EOF
# q03: bedingte Konformanz deklarationsseitig. ERWARTUNG: kompiliert, "true false"
cat > q03_generic_conformance_in_decl.lyr <<'EOF'
import std.io.console { println };
import std.core { Equatable };
struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>] {
  a: T, b: T,
  fn equals(other: Pair<T>): bool { return this.a.equals(other.a) && this.b.equals(other.b); }
}
fn main(): int {
  let p = Pair<int> { a = 1, b = 2 };
  let q = Pair<int> { a = 1, b = 2 };
  let r = Pair<int> { a = 1, b = 3 };
  println(f"{p.equals(q)} {p.equals(r)}");
  return 0;
}
EOF
# q03b: dieselbe Form mit T, das die Constraint NICHT erfuellt. ERWARTUNG: SEM0028 an der Instanz
cat > q03b_generic_conformance_unmet.lyr <<'EOF'
import std.core { Equatable };
struct Plain { n: int }
struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>] {
  a: T, b: T,
  fn equals(other: Pair<T>): bool { return this.a.equals(other.a) && this.b.equals(other.b); }
}
fn main(): int {
  let _ = Pair<Plain> { a = Plain { n = 1 }, b = Plain { n = 2 } };
  return 0;
}
EOF
# q04: zwei generische Ueberladungen, nur Constraint verschieden. ERWARTUNG: SEM0086 fuer beide Aufrufe
cat > q04_overload_constraint_only.lyr <<'EOF'
import std.io.console { println };
interface A1 { fn n(): int; }
class Impl :: [A1] { fn n(): int { return 7; } }
fn g<T :: [A1]>(x: T): int { return 1; }
fn g<T>(x: T): int { return 2; }
fn main(): int { println(f"{g(Impl {})} {g(5)}"); return 0; }
EOF
# q04b: A1 vs A2, Argument erfuellt nur A2. ERWARTUNG: SEM0086
cat > q04b_constraint_as_filter.lyr <<'EOF'
import std.io.console { println };
interface A1 { fn n(): int; }
interface A2 { fn m(): int; }
class OnlyA2 :: [A2] { fn m(): int { return 7; } }
fn g<T :: [A1]>(x: T): int { return 1; }
fn g<T :: [A2]>(x: T): int { return 2; }
fn main(): int { println(f"{g(OnlyA2 {})}"); return 0; }
EOF
# q04c: generisch-constrained vs konkret string, Aufruf g(5). ERWARTUNG: SEM0028 (Wahl vor Constraint)
cat > q04c_constraint_after_choice.lyr <<'EOF'
interface A1 { fn n(): int; }
fn g<T :: [A1]>(x: T): int { return 1; }
fn g(x: string): int { return 2; }
fn main(): int { return g(5); }
EOF
# q05: throws E mit Typparameter. ERWARTUNG: SEM0034 may throw Throwable
cat > q05_throws_typeparam.lyr <<'EOF'
class Boom :: [Throwable] { n: int, fn message(): string { return "boom"; } }
fn risky<T, E :: [Throwable]>(x: T, e: E, fail: bool): T throws E { if (fail) { throw e; } return x; }
fn main(): int {
  try { return risky<int, Boom>(1, Boom { n = 2 }, true); } catch (b: Boom) { return b.n; }
}
EOF
# q05c: Kontrolle, konkretes throws Boom in generischer Funktion. ERWARTUNG: kompiliert, Exit 2
cat > q05c_throws_concrete_control.lyr <<'EOF'
class Boom :: [Throwable] { n: int, fn message(): string { return "boom"; } }
fn risky<T>(x: T, fail: bool): T throws Boom { if (fail) { throw Boom { n = 2 }; } return x; }
fn main(): int {
  try { return risky<int>(1, true); } catch (b: Boom) { return b.n; }
}
EOF
# q05d: throws E mit catch (t: Throwable). ERWARTUNG: kompiliert (weitet auf Throwable), Exit 9
cat > q05d_throws_typeparam_catch_throwable.lyr <<'EOF'
class Boom :: [Throwable] { n: int, fn message(): string { return "boom"; } }
fn risky<T, E :: [Throwable]>(x: T, e: E, fail: bool): T throws E { if (fail) { throw e; } return x; }
fn main(): int {
  try { return risky<int, Boom>(1, Boom { n = 2 }, true); } catch (t: Throwable) { return 9; }
}
EOF
# q06: extend auf generischem Typ ohne Argumente. ERWARTUNG: IR0001 an 3:1 (Klasse), nicht am extend
cat > q06_extend_generic_no_args.lyr <<'EOF'
interface A1 { fn n(): int; }
class Impl :: [A1] { fn n(): int { return 7; } }
class Bag<T :: [A1]> { item: T }
extend Bag { pub fn tell(): int { return this.item.n(); } }
fn main(): int { let b = Bag<Impl> { item = Impl {} }; return b.tell(); }
EOF
# q06b: unbedingte generische Erweiterung extend<T> Bag<T>. ERWARTUNG: PAR0011 (wie p09)
cat > q06b_extend_generic_param.lyr <<'EOF'
class Bag<T> { item: T }
extend<T> Bag<T> { pub fn get(): T { return this.item; } }
fn main(): int { let b = Bag<int> { item = 4 }; return b.get(); }
EOF
# q07: generische METHODE auf nicht-generischer Klasse, Koroutine. ERWARTUNG: VM0013
cat > q07_generic_method_coroutine.lyr <<'EOF'
import std.io.console { println };
class Src { fn once<T>(a: T): Coroutine<T> { yield a; } }
fn main(): int { let s = Src {}; let c = s.once<int>(7); println(f"{resume c}"); return 0; }
EOF
# q08: Phantom-Typparameter. ERWARTUNG: still akzeptiert, Exit 3
cat > q08_phantom_typeparam.lyr <<'EOF'
class Tagged<T> { n: int }
fn f<T>(n: int): int { return n; }
fn main(): int { let t = Tagged<string> { n = 1 }; let u = Tagged<int> { n = 2 }; return f<bool>(t.n) + f<string>(u.n); }
EOF
# q09: void als Typargument. ERWARTUNG: CLI0020 (Verifier)
cat > q09_void_typearg.lyr <<'EOF'
class Box<T> { v: T }
fn main(): int { let _: ?Box<void> = null; return 0; }
EOF
# q09c: Kontrolle ?Box<int>. ERWARTUNG: kompiliert
cat > q09c_void_control.lyr <<'EOF'
class Box<T> { v: T }
fn main(): int { let _: ?Box<int> = null; return 0; }
EOF
# q10: a<b>(c) mit ints. ERWARTUNG: SEM0013 int is not callable
cat > q10_comparison_lookalike.lyr <<'EOF'
import std.io.console { println };
fn main(): int { let a = 1; let b = 2; let c = 3; let r = a<b>(c); println(f"{r}"); return 0; }
EOF
# q10c: Kontrolle a < b. ERWARTUNG: kompiliert, true
cat > q10c_comparison_control.lyr <<'EOF'
import std.io.console { println };
fn main(): int { let a = 1; let b = 2; let r = a < b; println(f"{r}"); return 0; }
EOF
# q11: G11 x Ueberladung: make<T>(): T neben make(): int, let s: string = make(). ERWARTUNG: konkrete gewinnt (Regel 2), dann SEM0001 int->string
cat > q11_context_vs_overload.lyr <<'EOF'
fn make<T>(x: T): T { return x; }
fn make(): int { return 1; }
fn main(): int { let s: string = make(); return s.length; }
EOF
# q11b: nur make<T>(): T ohne Argumente, mit Kontext. ERWARTUNG: SEM0060 (Regel 6)
cat > q11b_context_only_generic.lyr <<'EOF'
fn make<T>(): T[] { return []; }
fn main(): int { let s: string[] = make(); return s.length; }
EOF
# q11c: Kontrolle: make(): int. ERWARTUNG: kompiliert, Exit 1
cat > q11c_overload_control.lyr <<'EOF'
fn make(): int { return 1; }
fn main(): int { let n: int = make(); return n; }
EOF
# q12: opaque als Typargument: Box<Meters> as Box<int>. ERWARTUNG: refused
cat > q12_opaque_typearg_cross.lyr <<'EOF'
opaque type Meters = int;
class Box<T> { v: T }
fn main(): int { let m = 3 as Meters; let b = Box<Meters> { v = m }; let c = b as Box<int>; return c.v; }
EOF
# q12c: Kontrolle: Box<Meters> bauen und v as int lesen. ERWARTUNG: kompiliert, Exit 3
cat > q12c_opaque_typearg_control.lyr <<'EOF'
opaque type Meters = int;
class Box<T> { v: T }
fn main(): int { let m = 3 as Meters; let b = Box<Meters> { v = m }; return b.v as int; }
EOF
# q12d: Box<Meters> und Box<int> — zwei Instanzen? Bytecodegroesse gegen q12e (nur Box<int> zweimal). ERWARTUNG: q12d > q12e
cat > q12d_opaque_two_instances.lyr <<'EOF'
opaque type Meters = int;
class Box<T> { v: T, fn get(): T { return this.v; } }
fn main(): int { let m = 3 as Meters; let b = Box<Meters> { v = m }; let c = Box<int> { v = 4 }; return (b.get() as int) + c.get(); }
EOF
cat > q12e_opaque_one_instance_control.lyr <<'EOF'
opaque type Meters = int;
class Box<T> { v: T, fn get(): T { return this.v; } }
fn main(): int { let m = 3 as Meters; let b = Box<int> { v = m as int }; let c = Box<int> { v = 4 }; return b.get() + c.get(); }
EOF
# q13: Default-Methode eines generischen Interface ueber Interface-WERT. ERWARTUNG: laeuft, "42 2"
cat > q13_default_method_via_value.lyr <<'EOF'
import std.io.console { println };
interface Src<T> { fn v(): T; fn pair(): T[] { return [this.v(), this.v()]; } }
class IntSrc :: [Src<int>] { fn v(): int { return 21; } }
class StrSrc :: [Src<string>] { fn v(): string { return "a"; } }
fn main(): int {
  let a: Src<int> = IntSrc {};
  let b: Src<string> = StrSrc {};
  let xs = a.pair();
  let ys = b.pair();
  println(f"{xs[0] + xs[1]} {ys.length}");
  return 0;
}
EOF
# q14: lyrfmt-Eingabe mit Leerzeichen-Workaround. ERWARTUNG: lyrfmt schreibt > > zu >>, Ergebnis kompiliert nicht (SEM0052)
cat > q14_fmt_input.lyr <<'EOF'
class Box<T> {
    v: T,
}

fn take<U>(u: U): int {
    return 1;
}

fn main(): int {
    let inner = Box<int> { v = 1 };
    return take<Box<int> >(inner);
}
EOF
# q15: Klassen-Methoden-Form (r31) noch einmal, Absturz? ERWARTUNG: Stack overflow (kein Code)
cat > q15_guard_method_chain.lyr <<'EOF'
class It<T> {
  v: T,
  fn deep(n: int): int {
    if (n == 0) { return 0; }
    let next = It<T[]> { v = [this.v] };
    return next.deep(n - 1);
  }
}
fn main(): int {
  let it = It<int> { v = 1 };
  return it.deep(50);
}
EOF
ls *.lyr | wc -l
