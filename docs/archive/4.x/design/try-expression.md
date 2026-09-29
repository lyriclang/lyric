# `try` in der Definite Assignment (implementiert) und `try` als Ausdruck (Design)

**Status:** Stufe (a) implementiert (Branch `worktree-agent-a4962b919be70e622`, Commit „sema: a try whose every catch leaves assigns what its body assigned“); Stufe (b) designt
**Version:** (a) Patch-fähig (reine Analyse-Verfeinerung, nur weniger SEM0018); (b) Minor (4.5/4.6)
**Spec:** (a) §7.7 ein Satz · (b) §2 (`TryExpr`), §6.2/§6.9, §7.7, §9.2, §9.3, Appendix A (SEM-neu) · **Guide:** 10
**Abhängigkeiten:** (b) braucht `design/value-block.md` (catch-Block mit Tail) und `design/throw-expression.md` (never); Option B braucht typed throws Stufe 1 (`design/typed-throws.md`) und `std.result` (stdlib-redesign).
**Abgestimmt mit:** stdlib-redesign (Result<T,E> in std.result; `try?` → `?T`; `try … catch (e: E) expr` unifiziert; Option B `try e` → Result als 5.0-Empfehlung), pattern-lambda (keine Berührung)

## Stufe (a): `try` trägt zur Definite Assignment bei — implementiert

### Motivation
Die Form, die jeder C#-/Java-Autor zuerst schreibt (Prototyp 04 `fails.lyr`):
```lyr
var opts: Options;
try { opts = parse(args); } catch (e: UsageError) { println(e.message()); return 2; }
use(opts);      // 4.4: LYR-SEM0018 — "A try contributes nothing afterwards"
```
Umweg A (Helfer, der die Ausnahme in `?Options` übersetzt) kostet 6 Zeilen und wirft den Grund weg; Umweg B
(Rest der Funktion in den try-Block) dehnt den Schutzbereich auf Code aus, der nie werfen sollte — ein
späterer `UsageError` aus anderer Quelle wird still als „usage:“ gemeldet. Beides ist heute idiomatisch,
und beides ist schlechter als die abgelehnte Form.

### Regel (§7.7)
Ein `try`, dessen catch-Klauseln ALLE immer verlassen (§7.3-Regel `Flow.AlwaysExits`: `return`, `throw`,
`break`, `continue`, never-Aufruf), trägt bei, was sein Body **an seinem Ende** zugewiesen hat; sonst nichts.
Korrekt, weil eine Ausnahme mitten im Body nur in einen catch führt, und jeder catch verlässt — der einzige
Weg zum Statement nach dem `try` ist das normale Ende des Bodys. Was der Body nur auf manchen Pfaden
zuweist (`if (n > 0) { m = 2; }`), zählt weiterhin nicht (Test `Only_what_the_body_assigns_on_its_own_end_counts`).

### Implementierung
`FlowAnalyzer.cs` TryStmt: `afterBody` behalten, `Flow.AlwaysExits(c.Body, _types)` je Klausel; 8 Zeilen.
Tests: 4 Sema. Kein Breaking (nur weniger Fehler).

### Spec-Diff §7.7
```diff
-- A `try` contributes nothing afterwards — the body may have thrown mid-way. The catch
-  binding is assigned inside its clause.
+- A `try` whose catch clauses ALL always leave (§7.3: return, throw, break, continue, a
+  `never` call) contributes what its body assigns at its own end — a throw mid-way lands in a
+  clause that leaves, so the body's end is the one way past the `try`. A `try` with a catch
+  that can fall through contributes nothing. The catch binding is assigned inside its clause.
```

## Stufe (b): `try` als Ausdruck — Design

### Syntax
```
Primary  += TryExpr .
TryExpr   = 'try' [ '?' ] UnaryExpr [ 'catch' '(' CatchBinding ')' ( Expr | ValueBlock ) ] .
```
- **Disambiguierung:** `try` + `{` bleibt `TryStmt` (§5, `Parser.Statements.cs:39`); jedes andere Token
  nach `try` öffnet den Ausdruck. Heute ist das `LYR-PAR0017` — keine Kollision. `catch` ist reserviert und
  beendet den Operanden eindeutig. `try?` ist `try` gefolgt vom `?`-Token (der Lexer kennt `?` als
  Optional-Präfix; `try ?x` vs `try? x` unterscheidet der Parser am Whitespace NICHT — Regel: `?` direkt nach
  `try` gehört zum `try`; ein optionaler Typ steht in Ausdrucksposition ohnehin nie).
- Operand `UnaryExpr`: `try f() ?? d` ist `(try f()) ?? d` — wie Swift (`try` bindet den Aufruf, nicht den Ausdruck rechts davon).

### Typisierung
| Form | Typ | Bedingung |
|---|---|---|
| `try e catch (x: E) Expr` | Unifikation von `T` und `Expr` (§6.9) | `Expr` darf `never` sein (`… catch (_) throw Other {}`) |
| `try e catch (x: E) ValueBlock` | `T` unifiziert mit dem Tail; ohne Tail muss der Block verlassen (SEM0033-Regel) | |
| `try? e` | `?T`; ist `e` bereits `?T`, bleibt `?T` (kein `??T`, §3.3 — „geworfen“ und „null“ fallen zusammen wie bei `next()` auf `Coroutine<?T>`, §10) | |
| `try e` (ohne `?`, ohne `catch`) | **Option A:** Fehler `LYR-SEM-neu` „try needs '?' or a catch“. **Option B (stdlib-redesign):** `Result<T, E>` mit `E` aus der throws-Klausel des Operanden (Swift `Result { try f() }`, Kotlin `runCatching`) | B setzt typed throws Stufe 1 und den Anker `std.result.Result` voraus |
- `catch (x)` ohne Typ bindet `Throwable` (§9.3); `catch (_)` deckt alles.
- Genau EINE catch-Klausel. Wer mehrere braucht, nimmt das Statement — die Ausdrucksform ist für den
  einen häufigen Fall gebaut (Kotlin hat nur `try … catch` als Ausdruck mit beliebig vielen Klauseln, was
  lange Ausdrücke erzeugt; Swift hat gar keinen catch-Ausdruck).

### Exception-Analyse (§9.2)
Der Operand ist die geschützte Region; `catch (x: E)` deckt genau `E` (ein `throws Other` des Operanden
bleibt ungedeckt → SEM0034 wie heute); `try?` deckt alles. Die Funktion um ein `try e catch (x: E) …`
braucht keine `throws`-Klausel für `E`.

### Definite Assignment (§7.7)
`let x = try … catch (…) { return 2; }` — die Bindung ist zugewiesen, weil der einzige Weg zur
Fortsetzung durch den Wert führt (Expr-Form trivial; Block-Form mit Tail liefert den Wert, ohne Tail verlässt sie).

### Lowering
Desugar in die bestehende try/catch-Lowering (`LowerTry`, Handler-Tabelle §9.3) mit einem synthetischen
Lokal: Region = Operand + Store; Handler = catch-Body + Store (oder Exit); danach Load. `try?`: Handler
speichert `optnone`. Kein neuer Opcode; ~60 Zeilen. Die Coroutine-Region-Regeln (§10: keine Suspension
innerhalb einer geschützten Region? — zu prüfen: heute darf ein `yield` in einem try-Body stehen) gelten unverändert.

### Result-Reifikation (Prototyp 01)
Mit (b) ist `Result<T,E>` reine stdlib: `try Result.Ok(f()) catch (e: E) Result.Err(e)` — keine eigene
Produktion. Option B verkürzt das auf `try f()`. Regel 2 (CONTRIBUTING: ein Mechanismus für Fehler) bleibt
gewahrt: `Result` ist ein Wert, den man in eine Liste legt und später auswertet; die Propagation bleibt
`throws`. Was zu vermeiden ist: eine zweite Propagationsform (`?`-Operator auf Result) — Prototyp 01 hat
belegt, dass `throws` bereits Rusts `?` ist.

### Wechselwirkungen
- Narrowing: `try? f()` ist ein Identifier-loser Ausdruck, narrowt erst nach Bindung (§7.4, konsistent).
- Optionals: `try? f()` mit `f(): ?T` kollabiert (oben).
- match: `try` im Arm-Ausdruck erlaubt; `throw` im catch-Ausdruck (never) erlaubt.
- Generics: `try? f<T>()` unproblematisch.
- Coroutinen: `try? resume co` / `try? co.next()` decken den Pull (§10 „Throwability of a pull“) — Nebeneffekt.
- Formatter: `try? e`, `try e catch (x: E) …` — Präfix-Level wie `resume`.
- Value-Block: der catch-Block ist ein ValueBlock (Tail).
- typed throws: `try? f()` über einen Aufruf mit `E = never` → Warnung „nothing to catch“ (wie ein unbenutztes Label), kein Fehler.

### Breaking
Nein (Minor). Option B ändert nichts an bestehenden Programmen, bindet aber den Compiler an einen stdlib-Anker.

### Aufwand (Schätzung)
Parser ~40 Z. (neuer Primary, CatchBinding-Parser existiert), Sema ~80 (Typ, ExceptionAnalyzer-Region,
DA), Lowering ~60, Formatter ~10, VM 0, stdlib 0 (Option B: +Anker-Lookup ~20). Spec: §2, §6.2, §7.7, §9.2/9.3.

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| Swift | `try f()` (Pflicht-Marker, propagiert), `try? f()` → Optional, `try! f()` → Crash; kein catch-Ausdruck; `Result { try f() }` | Marker am Aufruf ist Swifts Sichtbarkeitsentscheidung; Lyric hat sie bewusst nicht (§9: null Zeichen pro Aufruf). `try?`-Semantik (Optional-Kollaps) übernommen. |
| Kotlin | `val v = try { f() } catch (e: E) { fallback }` — try ist Ausdruck, Blöcke liefern den letzten Ausdruck; `runCatching { }.getOrNull()` ≈ `try?` | Volle Statement-Form als Ausdruck; DA wird durch die Ausdrucksform gelöst. |
| C# | try ist Statement; `int n; try { n = P(); } catch { return; } Use(n);` ist DA-korrekt (Roslyn: catch verlässt) | Stufe (a) ist genau C#s DA-Regel. |
| Rust | `f()?` propagiert; `f().ok()` ≈ `try?`; `f().unwrap_or_else(\|e\| …)` ≈ catch-Ausdruck | Alles Methoden auf Result — braucht Result als Sprachanker. |
| Zig | `try f()` propagiert; `f() catch \|e\| fallback` ist der catch-AUSDRUCK; `f() catch null` ≈ `try?` | Zigs `catch` als binärer Operator ist die kompakteste Form und das nächste Vorbild für `try e catch (x) expr`. |
| Python | try ist Statement; DA nicht statisch | — |

Zig:
```zig
const port = parsePort(line) catch |e| { std.log.err("{}", .{e}); return 2; };
const maybe = parsePort(line) catch null;      // ?u16
```
Kotlin:
```kotlin
val opts = try { parse(args) } catch (e: UsageError) { println(e.message); return 2 }
val maybe = runCatching { parse(args) }.getOrNull()
```

**Fallen, die Lyric vermeidet:** (1) Swifts `try` als Pflicht-Marker widerspricht §9 — nicht übernehmen;
(2) Kotlins Ausdrucks-`try` mit mehreren Klauseln erzeugt 20-zeilige Ausdrücke — Lyric: genau eine Klausel;
(3) Swifts `try!` (Crash) hat Lyric als `!` auf dem Optional bereits — kein Doppel; (4) Zigs `catch` ohne
Bindungstyp fängt alles — Lyric behält `catch (x: E)` mit Typ, damit ein fremder Fehler nicht still
verschluckt wird (Umweg B-Problem).

**Empfehlung:** Stufe (a) sofort (C#-Regel). Stufe (b) in der Zig-Form mit Lyric-Bindung
(`try e catch (x: E) expr`, `try? e`), Option A in 4.5; Option B (`try e` → `Result`) in 5.0 zusammen mit
typed throws und dem Wegfall der OrThrow-Zwillinge.

## Offene Fragen
1. `defer` innerhalb des catch-ValueBlocks — erlaubt (Block-Scope), läuft vor der Wertübergabe.
2. Mehrere Klauseln — nein (s.o.).
3. `try? e` auf einen Operanden, der nicht werfen kann — Warnung oder Fehler? Warnung (Refactoring-freundlich).
4. Option B: `E` bei einem Operanden, der mehrere Typen wirft (`throws` ohne Typ) → `Result<T, Throwable>`.

## Spec-Diff (b)
§2: `Primary += TryExpr . TryExpr = 'try' [ '?' ] UnaryExpr [ 'catch' '(' CatchBinding ')' ( Expr | ValueBlock ) ] .`
§9.3 neuer Absatz „`try` as an expression“: Typregeln der Tabelle oben, eine Klausel, `try?`-Kollaps.
§9.2: „the operand of a `try` expression is a protected region; its clause covers what a `catch` of the same binding covers; `try?` covers everything.“
§7.7: „A `try` expression assigns its binding: the value is the one way past it.“
Appendix A: `LYR-SEM-neu | E | A try expression with neither '?' nor a catch clause` (Option A).
