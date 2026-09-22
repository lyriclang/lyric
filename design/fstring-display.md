# f-Strings rendern `Display`-Typen

**Status:** implementiert (Prototyp auf Branch `worktree-agent-a4962b919be70e622`)
**Version:** Minor (4.5) — additiv, kein Programm ändert seine Bedeutung
**Spec:** §6.6, Appendix A (Text der SEM0006-Zeile) · **Guide:** 02
**Abhängigkeiten:** keine. Multipliziert sich mit der Konformanz-Synthese (`design/conformance-synthesis.md`) und
mit stdlib-redesigns `Display` für Container (braucht bedingte Konformanz, `design/conditional-conformance.md`).
**Abgestimmt mit:** stdlib-redesign (liefert Display für List/Set/Map/JsonValue/IoError/Result), pattern-lambda (keine Berührung).

## Motivation

`println(p)` nimmt jeden `Display`-Typ; `f"{p}"` nahm bis 4.4 nur Skalare (§6.6: „there is no implicit
Display call in interpolation“). Folge in jedem Programm mit `std.time`, `IoError` oder einem eigenen Enum:
`.show()` in jedem Loch mit eigenem Typ, und Guide 2 dokumentierte eine Sonderregel. Das war die einzige
Stelle, an der `println` und f-Strings verschieden antworteten. Zusätzlich war die Prüfung zu lax: ein
Loch mit `?int`, `int[]` oder `(int, int)` passierte die Sema und scheiterte erst im IR-Lowering
(`InternalCompilationException: interpolating a non-scalar value`) — der Fehlerort war das Lowering,
nicht der Nutzercode.

## Syntax

Keine Grammatikänderung. §1.5 `Interpolation = '{' Expr [ ':' FormatSpec ] '}'` bleibt.

## Semantik

Ein Loch `{e}` ohne Spezifizierer, dessen Typ `T` ist:

1. `T` primitiv (Zahl, `bool`, `char`, `string`) → `std.string.fromXxx(e)` wie bisher.
2. `T` opak → Fehler `LYR-SEM0006` (unverändert, §3.5).
3. `T` erfüllt `Display` (Struct/Class/Enum mit Konformanz oder `extend`; Typparameter mit Constraint;
   Interface-Wert, dessen Interface `Display` ist oder erreicht) → das Loch **ist** der Aufruf `e.show()`.
   Die Sema prüft den synthetischen Aufruf wie einen geschriebenen (`CheckExpr(call)`) und legt ihn als
   Bedeutung des Lochs ab (derselbe Mechanismus wie Operator-Desugaring und `as`: `TypeResult.DesugarOperator`).
4. sonst → `LYR-SEM0006` mit dem Hinweis, dem Typ `:: [Display]` mit `fn show(): string` zu geben.

Ein Loch `{e:spec}` mit `Display`-Typ ist ein Fehler (`LYR-SEM0006`): `Display` kennt keine
Spezifizierersprache; ein still ignorierter Spec wäre die Klasse „es druckt, aber nicht, was ich verlangt habe“.

`e` wird genau einmal ausgewertet (es ist der Empfänger des Aufrufs; Test
`The_hole_evaluates_its_expression_once`).

**Optionals, Arrays, Tupel** bleiben Fehler — jetzt in der Sema statt im Lowering. `?T` ist nicht
`Display`, und `null` → `"null"` (Kotlin-Verhalten) widerspricht §6.3/§7.4: eine Abwesenheit wird
narrowt oder mit `??` beantwortet, nicht gedruckt.

### Lowering

Null neue Instruktionen. `LowerInterpolatedString` fragt `OperatorCallOf(hole)` und lowert den
gespeicherten `CallExpr` (`LowerCall`) — ein gewöhnlicher Methodenaufruf, statisch auf dem konkreten Typ,
über die Instanz bei Generics, über die vtable beim Interface-Wert.

### Bytecode

Keine Änderung, Format bleibt 4.0.

## Wechselwirkungen

- **Narrowing:** `if (o != null) { f"{o}" }` — im Loch gilt der narrowte Typ wie in jedem Ausdruck.
- **Optionals:** kein `Display` für `?T` (bewusst, s.o.). `f"{o ?? fallback}"` ist die Form.
- **throws:** `show()` darf nicht werfen? Doch — ein `show()` mit `throws`-Klausel macht das Loch zu
  einer Throw-Site wie jeder Aufruf; der ExceptionAnalyzer sieht den desugarten Aufruf nicht (er läuft
  über den AST) — **offener Punkt**: Analyzer muss `OperatorCallOf` auch für Löcher lesen, wie er es
  für Operatoren tun muss (heute gleiches Verhalten bei `a == b` mit werfendem `equals`; kein Regress).
- **Generics:** `T :: [Display]` → direkter Aufruf über die Constraint; ohne Constraint Fehler.
- **Coroutinen, Formatter, LSP:** keine. Der Formatter gibt das Lexem unverändert wieder; das LSP
  könnte das Loch als Aufruf von `show` annotieren (Hover) — optional.
- **Konformanz-Synthese:** synthetisiertes `show()` macht jedes Struct/Enum ohne Handarbeit renderbar.
- **stdlib:** `println` und das Loch stellen jetzt dieselbe Frage. Nebenbefund: `println(d)` mit
  `d: Display` (Interface-Wert) scheitert an SEM0028, das Loch akzeptiert ihn — `Satisfies` sollte einen
  Interface-Wert gegen sich selbst/Parents prüfen (Bug-Eintrag in TASKLIST).

## Breaking

Nein. Jedes heute gültige Programm bleibt gültig und bedeutet dasselbe; `f"{p}"` war ein Fehler, und die
Fälle, die neu SEM0006 melden (Optional/Array/Tupel), waren vorher ein Compiler-Absturz.

## Aufwand (real)

Sema 45 Zeilen (`TypeChecker.CheckDisplayHole`), Lowering 6 Zeilen, Spec ein Absatz, Guide ein Absatz,
Tests 7 Sema + 4 Vm.

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| Rust | `format!("{p}")` → `Display`, `{p:?}` → `Debug` | Zwei Traits; `{}` auf einem Typ ohne `Display` ist ein Compile-Fehler. Formatspecs (`{:>8}`) gehen an den Trait weiter (`Formatter` trägt Breite/Präzision). |
| Python | `f"{p}"` → `format(p, "")` → `__format__` → `__str__`; `{p!r}` → `repr` | Alles rendert immer (Fallback `<P object at 0x…>`), Spec geht an `__format__`. |
| Kotlin | `"$p"` → `toString()` | Alles rendert immer, `null` → `"null"`. Keine Specs. |
| C# | `$"{p}"` → `ToString()`, `{p:N2}` → `IFormattable.ToString(format, provider)` | Alles rendert immer, `null` → `""`. |
| Swift | `"\(p)"` → `CustomStringConvertible.description`, sonst Reflection-Fallback | Rendert immer. |

Rust:
```rust
struct P { x: i32 }
impl fmt::Display for P { fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result { write!(f, "P{}", self.x) } }
println!("{p}");      // ok
println!("{}", vec);  // Fehler: Vec<T> implementiert Display nicht (nur Debug)
```
Python:
```python
class P:
    def __str__(self): return f"P{self.x}"
print(f"{p}")         # P1
print(f"{[1,2]}")     # [1, 2] — alles rendert, ob gewollt oder nicht
```

**Fallen, die Lyric vermeidet:** (1) das „alles rendert“-Modell (Python/Kotlin/C#/Swift) druckt
`P@1a2b3c` oder `<object at …>` statt zu scheitern — der Fehler taucht in Logs auf, nicht beim
Kompilieren; (2) `null` → `"null"`/`""` (Kotlin/C#) versteckt eine Abwesenheit im Text; (3) Rusts
Spec-Weiterreichung an den Trait ist mächtig, aber jeder `Display`-Impl muss Breite/Präzision selbst
respektieren (`f.pad`), was fast niemand tut — Specs auf Display still zu ignorieren ist dort üblich.

**Empfehlung:** Rusts Modell (Konformanz oder Fehler, kein Fallback), ohne Rusts Spec-Weiterreichung —
Spec auf `Display` ist ein Fehler. Ein `Debug`-Zwilling (`{p:?}`) ist NICHT vorgesehen: die
Konformanz-Synthese liefert ein `show()` in Initializer-Syntax, das die Debug-Rolle für Wertetypen
abdeckt.

## Offene Fragen

1. Soll `Display` für `?T` in der stdlib definiert werden (`extend<T :: [Display]> ?T :: [Display]`)?
   Empfehlung: nein (s.o.), aber mit bedingter Konformanz wäre es ausdrückbar, und die Entscheidung
   läge dann bei der stdlib statt im Compiler.
2. ExceptionAnalyzer über desugarte Aufrufe (Operatoren und Löcher) — gemeinsamer Fix, nicht Teil dieses Features.

## Spec-Diff (§6.6)

```diff
 ## 6.6 Interpolated strings

-`f"a{x}b{y:N2}"` desugars to concatenation of the literal parts with, per hole: the matching
-`std.string.fromXxx` converter for a primitive value, or `std.fmt.formatXxx(value, "spec")`
-when a specifier is present. A hole whose type has no converter — a struct, a class, an opaque
-alias — is an error; there is no implicit `Display` call in interpolation. `{{`/`}}` in the
-text are one literal brace (§1.7).
+`f"a{x}b{y:N2}"` desugars to concatenation of the literal parts with, per hole: the matching
+`std.string.fromXxx` converter for a primitive value, or `std.fmt.formatXxx(value, "spec")`
+when a specifier is present. A hole holding any other value IS the call `value.show()` when the
+value's type conforms to `Display` (§5.1) — a struct, a class, an enum, a type parameter with
+the constraint, or a value held through an interface that reaches `Display` — and the call is
+checked as if written. A hole whose type neither has a converter nor conforms — an optional, an
+array, a tuple, an opaque alias (§3.5) — is an error (`LYR-SEM0006`). A specifier on a
+`Display` value is an error: `Display` renders one way. `{{`/`}}` in the text are one literal
+brace (§1.7).
```

Appendix A:
```diff
-| LYR-SEM0006 | E | No conversion path: `as` without an `Into` conformance, or an opaque type in an f-string (§3.5). |
+| LYR-SEM0006 | E | No conversion path: `as` without an `Into` conformance, or an f-string hole whose value neither has a converter nor conforms to `Display`, or carries a specifier on a `Display` value (§6.6). |
```
