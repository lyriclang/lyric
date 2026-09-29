# `throw` als Ausdruck und `never` als schreibbarer Rückgabetyp

**Status:** implementiert (Prototyp auf Branch `worktree-agent-a4962b919be70e622`)
**Version:** Minor (4.5) — additiv; `throw e;` bleibt gültig, `never` war kein gültiger Typname
**Spec:** §2 (Grammatik: `ThrowExpr`), §3.1 (Typtabelle: `never`), §6.9 (Unifikation), §7.3 (Coverage), §9.2 (Sites), §9.4 · **Guide:** 10
**Abhängigkeiten:** keine. Vorbereitung für typed throws (`throws never`, `design/typed-throws.md`) und den `try`-Ausdruck (`design/try-expression.md`).
**Abgestimmt mit:** pattern-lambda (baut in Patterns/Exhaustiveness auf „ein Arm vom Typ never trägt nichts bei“ auf), stdlib-redesign (deklariert `panic/todo/unreachable/std.test.fail` als `never`, sobald 4.5 steht).

## Motivation

Drei Stellen, drei Umwege (Prototyp 08): im match-Ausdruck der Block-Arm `_ => { throw …; }`, für
`x ?? throw` ein dreizeiliges `let v = …; if (v == null) { throw …; } return v;`, für den
if-Ausdruck ein if-Statement davor. Dazu ein struktureller Punkt: die Sema HAT `never` (`LyrType.Never`,
Typ von `panic`), aber niemand außer `panic` bekommt ihn — ein eigener Abbruch-Helfer
(`fn fail(msg): void`) hat keine Flow-Wirkung, `if (bad) { fail("x"); }` narrowt nichts und zählt nicht
als Exit (§7.3/§7.4/§7.7). Und: `if (b) 1 else panic("no")` sowie `x ?? panic("x")` passierten die Sema
und **stürzten das Lowering ab** (`FunctionLowerer.cs:1393`, Bug P0-5 der Usability-Analyse) — die Lücke
war ohnehin fällig.

## Syntax

```
UnaryExpr   = PrefixOp UnaryExpr | ResumeExpr | ThrowExpr | PostfixExpr .
ThrowExpr   = 'throw' UnaryExpr .
ThrowStmt   = 'throw' Expr ';' .        (* unverändert: am Statement-Anfang bleibt es das Statement *)
```

- `throw` ist reserviert; in Ausdrucksposition war es `LYR-PAR0002` — keine Kollision.
- Operand ist ein `UnaryExpr`, damit `throw e ?? f` nicht als `throw (e ?? f)` gelesen wird: `throw` bindet
  wie ein Präfix (Kotlin: `x ?: throw E()` ohne Klammern; Rust: `return`/`break` in Ausdrucksposition ebenso).
- Statement-Position: `throw e;` bleibt `ThrowStmt` (Parser unverändert), damit die Flow-Regeln (§7.3
  `AlwaysReturns`, §7.7) ihre bisherige Form behalten; semantisch ist `throw e;` das `ExprStmt` eines
  `ThrowExpr`. `(throw e);` als ExprStmt ist zugelassen (SemaRules).
- `never` wird Builtin-Typname (§3.1 Tabelle), gültig **als Rückgabetyp** einer Funktion und (mit
  `design/typed-throws.md`) als `throws`-Typ. `let x: never`, `never[]`, `?never`, Typargument `never`
  werden in dieser Runde nicht eingeschränkt, sind aber nutzlos; Empfehlung für die Spec: „a return type
  only“, analog zu `void`, plus ein `LYR-SEM`-Fehler an jeder anderen Stelle (nicht im Prototyp).

## Semantik

- **Typ:** `throw e` hat den Typ `never`. `e` muss `Throwable` erfüllen (`LYR-SEM0030`, dieselbe Regel wie das Statement).
- **Unifikation (§6.9):** ein Arm/Operand vom Typ `never` trägt nichts bei; sind alle `never`, ist der
  Ausdruck `never`. `?T ?? never` ist `T`. Mit Kontexttyp (§3.1) ist `never` an den Kontext zuweisbar (war es
  schon: `IsAssignable(never, X)` = true).
- **Flow (§7.3, §7.4, §7.7):** unverändert — ein `ExprStmt` vom Typ `never` beendet den Pfad (das galt für
  `panic` bereits). Neu ist nur, dass jetzt auch ein Aufruf einer `: never`-Funktion diesen Typ hat.
- **Coverage einer `never`-Funktion:** der Körper muss auf jedem Pfad werfen/panicken/eine never-Funktion
  aufrufen — die vorhandene Regel `LYR-SEM0017` greift, weil `never` nicht `void` ist und `return` (mit
  oder ohne Wert) nicht zu `never` zuweisbar ist (`LYR-SEM0001`).
- **Exception-Analyse (§9.2):** `throw e` in Ausdrucksposition ist eine Throw-Site wie das Statement
  (`LYR-SEM0034`, wenn nichts sie deckt).
- **Lowering:** ein never-Ausdruck seals seinen Block (`throw`-Terminator bzw. `unreachable` nach einem
  Aufruf einer never-Funktion). Die drei Wertpositionen, die ihn enthalten dürfen — `if`-Zweig,
  `??`-rechts, match-Arm — prüfen vorher `Diverges(expr)` und speichern/branchen dann nicht; der Merge-Block
  hat nur die lebenden Vorgänger. Ein `if`, dessen beide Zweige divergieren, ist `LYR-IR0001`
  (Implementierungsgrenze; die Sema typisiert ihn als `never`, sinnvoll ist er nie). Eine `never`-Funktion
  ist an der Maschine eine `void`-Funktion, deren Aufruf ein `unreachable` folgt — kein neuer Opcode.
- **Bytecode:** keine Änderung (Format 4.0).

### Was bewusst NICHT gemacht wurde

- `panic/todo/unreachable` in `core.lyr` auf `: never` umstellen: das ist die stdlib (stdlib-redesign hat es
  übernommen) und ändert §9.4 („deliberately `void`, so they never replace a `return`“) — mit `never` als
  ehrlichem Typ ist diese Begründung hinfällig: `fn f(): int { todo("x"); }` SOLL kompilieren, so wie Rust
  `todo!()` und Swift `fatalError()`. Empfehlung: umstellen, §9.4 anpassen.
- `never` in Patterns/Exhaustiveness: pattern-lambda.
- `break value`/Labels als Ausdruck: nein (siehe `design/loop-labels.md`).

## Wechselwirkungen

- **Narrowing:** `x ?? throw e` narrowt nichts — die Bindung IST bereits `T`; `if (o == null) { fail(); }`
  narrowt `o` danach (never-Aufruf ist Exit).
- **Optionals:** `?T ?? never` = `T`; `if (c) null else throw e` ist `null`-typisiert → wie heute nur mit Kontext sinnvoll.
- **throws:** Site wie Statement; `fn fail(): never throws E { throw E {}; }` ist gültig und nützlich.
- **Generics:** `never` als Typargument nicht vorgesehen (nicht refused im Prototyp — offene Frage).
- **Coroutinen:** `yield` in einer never-Funktion — der Körper ist keine Coroutine (Rückgabetyp ist nicht `Coroutine<T>`), unverändert.
- **Lambdas:** `() => throw e` inferiert `fn() -> never`; als Argument an `fn() -> T` passt es (Assignability), Lowering: never → void; Aufruf über den Funktionswert liefert nichts. Offener Punkt für typed throws (Lambda-Klausel).
- **Formatter:** `throw` ist Präfix-Level; überflüssige Klammern gehen (`(o ?? (throw E {}))` → `o ?? throw E { }`), Test vorhanden.
- **LSP:** Hover auf `throw`-Ausdruck zeigt `never`; keine weitere Änderung.
- **Tail-Expression (`design/value-block.md`):** ein Tail `throw e` macht den Block zu einem exit-Block.

## Breaking

Nein. Ein Nutzer-Typ namens `never` würde verdeckt (Builtin-Namen gewinnen? — geprüft: Builtin-Scope ist
der ROOT-Parent, ein Modul-Typ `never` verdeckt den Builtin; deshalb keine Kollision, aber `never` sollte
wie `void` reserviert werden — §1.3-Liste, Minor mit Deprecation-Hinweis, falls jemand den Namen nutzt).

## Aufwand (real)

Parser 6 Z., AST 1, Sema ~35 (CheckExpr-Fall, UnifyArms/Unify je 2, Builtin-Name), Analyzer 4×2, Lowering
~60 (LowerThrowExpr, Diverges/LowerDiverging, drei Positionen), TypeLowering 2×, Formatter 2, Dumper/Children 2.
Tests: 12 Sema, 6 Vm, 3 Parser, 1 Formatter.

## Vergleich

| Sprache | Form | Typ | Bemerkung |
|---|---|---|---|
| Kotlin | `val v = x ?: throw E()`; `fun fail(): Nothing` | `Nothing` (bottom) | `throw` ist Ausdruck; `Nothing` als Rückgabetyp gibt Smart-Casts nach dem Aufruf. |
| Rust | `let v = x.unwrap_or_else(\|\| panic!())`; `return`/`break`/`panic!` sind `!` | `!` (never, seit 1.41 stabil als Rückgabetyp) | Kein `throw`; `?` propagiert. Diverging-Funktionen `fn fail() -> !`. |
| Swift | `guard let v = x else { throw E() }`; `func fail() -> Never` | `Never` (uninhabited enum) | `throw` ist ein Statement, aber `Never` ist ein normaler Typ; Compiler kennt ihn für Coverage. |
| TypeScript | `const v = x ?? throwErr()`; `function fail(): never` | `never` | `throw` ist NUR Statement (Proposal „throw expressions“ Stage 2 seit 2017); Idiom ist eine Helper-Funktion mit `never`. |
| C# | `var v = x ?? throw new E();` (C# 7) | kein Typ (Sonderregel: nur rechts von `??`, `?:`, `=>`) | throw-Ausdruck nur an drei Stellen erlaubt — keine bottom-Typ-Semantik. |
| Zig | `const v = x orelse return error.E;` `fn fail() noreturn` | `noreturn` | `return`/`break` sind Ausdrücke; `noreturn` koerziert überall hin. |

Kotlin:
```kotlin
class NotFound(val key: String) : Exception()
fun need(m: Map<String, Int>, key: String): Int = m[key] ?: throw NotFound(key)
fun fail(msg: String): Nothing = throw IllegalStateException(msg)
fun guard(o: Int?): Int { if (o == null) fail("none"); return o }   // Smart-Cast nach Nothing-Aufruf
```
TypeScript:
```ts
function fail(msg: string): never { throw new Error(msg); }
function guard(o: number | undefined): number {
    if (o === undefined) fail("none");   // narrowing: `o` ist danach number
    return o;
}
const v = m.get(k) ?? fail("missing");   // throw selbst geht nicht: `?? throw` ist ein Syntaxfehler
```

**Fallen, die Lyric vermeidet:** (1) C#s Sonderregel „throw nur an drei Stellen“ ist ein Sonderfall ohne
Typ — jede neue Position (Tail-Expression, `try`-Ausdruck) müsste erneut freigeschaltet werden; ein echter
bottom-Typ deckt sie alle. (2) TypeScript ohne throw-Ausdruck zwingt zur Helper-Funktion. (3) Rusts
`!` als vollwertiger Typ (Typargument, Enum-Payload) ist elegant, kostet aber Sonderfälle im Typsystem
(uninhabited types, `Result<T, !>`) — Lyric braucht das nicht: `never` nur als Rückgabe- und throws-Typ.
(4) Kotlins `Nothing?` (nullable Nothing = Typ von `null`) ist eine Kuriosität; Lyric hat `NullType`
getrennt, und das bleibt so.

**Empfehlung:** Kotlin-Modell (throw als Präfix-Ausdruck vom bottom-Typ, `never` als schreibbarer
Rückgabetyp, Aufruf beendet den Pfad und narrowt), mit Zig/Swift-Einschränkung „nur Rückgabe-/throws-Typ“.

## Offene Fragen

1. `never` an anderer Stelle als Rückgabe/throws (`let x: never`, `never[]`): refusen (`LYR-SEM`-neu) oder
   erlauben und nutzlos lassen? Empfehlung: refusen wie `void` (§3.1 „a return type only“).
2. `if` mit zwei divergierenden Zweigen: Sema-Fehler „the if never delivers a value“ (freundlicher) statt IR0001?
3. Lambda `() => throw e` unter typed throws: die Klausel des Lambdas ist `E`, sein Rückgabetyp `never` — mit
   `fn() -> T throws E` Parametertyp kompatibel (never ⊂ T). Zu prüfen in `design/typed-throws.md`.

## Spec-Diff

§2 Grammatik:
```diff
-UnaryExpr       = PrefixOp UnaryExpr | ResumeExpr | PostfixExpr .
+UnaryExpr       = PrefixOp UnaryExpr | ResumeExpr | ThrowExpr | PostfixExpr .
 ResumeExpr      = 'resume' UnaryExpr .
+ThrowExpr       = 'throw' UnaryExpr .
 …
-ThrowStmt       = 'throw' Expr ';' .
+ThrowStmt       = 'throw' Expr ';' .        (* the statement form of ThrowExpr; one meaning *)
```
§3.1 Typtabelle:
```diff
 | `void` | the absence of a value; a return type only |
+| `never` | the type of an expression that does not deliver a value — `throw`, `panic`, a call to a function declared `never`; a return type and a `throws` type only |
```
§6.9:
```diff
 `if (c) a else b` and `match` in value position unify their arm types: equal types, or one arm
-`null` widening the result to the optional. Disagreeing arms are one error (`LYR-SEM0016`).
+`null` widening the result to the optional. An arm of type `never` — a `throw`, a `panic`, a
+call to a `never` function — contributes nothing; when every arm is `never`, so is the
+expression. `?T ?? never` is `T`. Disagreeing arms are one error (`LYR-SEM0016`).
```
§7.3:
```diff
-A non-void function must return or throw on every path (`LYR-SEM0017`); …
+A non-void function must return or throw on every path (`LYR-SEM0017`); a function declared
+`never` must throw, panic or call a `never` function on every path — the same rule, with no
+`return` admitted (`LYR-SEM0001`). A call to a `never` function ends the path as `panic` does
+(§9.4). …
```
§9.2: „A throw site is a `throw` statement or a `throw` expression, a call, …“
§9.4:
```diff
-`panic(message)` returns `never`: flow analysis treats everything after it as unreachable, and
-an `if`-branch ending in panic narrows the other branch. … Assertion helpers (`assert`, `todo`,
-`unreachable`) are `panic` with a statement about who got it wrong — deliberately `void`, so
-they never replace a `return`.
+`panic(message)` returns `never` (§3.1), a type any function may declare: flow analysis treats
+everything after such a call as unreachable, and an `if`-branch ending in one narrows the other
+branch. `throw` in expression position has the same type (§6.9). Assertion helpers (`assert`,
+`todo`, `unreachable`) … — `todo` and `unreachable` are declared `never` and cover a missing
+`return`; `assert` is `void`, because it may come back.
```
Appendix A: `LYR-SEM0030` Text um „or `throw` expression“ ergänzen; `LYR-SEM0017` um die never-Funktion.
