# 04 — try/catch und Definite Assignment; `try` als Ausdruck

Zugehöriger language-review-Punkt: **"try/catch trägt nichts zur Definite Assignment bei, auch wenn jeder catch die Funktion verlässt"** (HIGH). Ersetzt zugleich den Sprachteil von Prototyp 01 (`try e` → Result) durch die vereinbarte gemeinsame Produktion.

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `fails.lyr` | kompiliert NICHT — zeigt `LYR-SEM0018` in der natürlichen Form | 22 |
| `ist.lyr` | läuft, exit 4 — beide Umwege (Helfer `?Options`, Rest-in-try) | 44 |
| `soll.lyr` | PROPOSAL — Stufe (a) Regel, Stufe (b) `try`-Ausdruck, `try?`, Tail-Variante | 48 |
| `vergleich.cs` | C# (Stufe a: Definite Assignment über try/catch) | 22 |
| `vergleich.kt` | Kotlin (Stufe b: `try` als Ausdruck, `runCatching().getOrNull()` ≈ `try?`) | 20 |

## Ist-Stand
`fails.lyr` ist die Form, die jeder C#-/Java-Autor zuerst schreibt; sie scheitert an §7.7 ("A try
contributes nothing afterwards"), obwohl der einzige Weg zur Fortsetzung der normale Abschluss des
try-Bodys ist. Umweg A (Helfer, der die Ausnahme in `?Options` übersetzt) kostet 6 Zeilen und
wirft den Grund weg; Umweg B (Rest der Funktion in den try-Block) dehnt den Schutzbereich auf
Code aus, der nie werfen sollte — ein späterer `UsageError` aus einer anderen Quelle wird still
mit "usage:" gemeldet. Beides ist in `ist.lyr` lauffähig.

## Soll — Stufe (a): Regel (keine Syntax)
Spec §7.7, Aufzählung "A `try` contributes nothing afterwards" → *"A `try` whose catch clauses
ALL always leave (§7.3's rule) contributes what its body assigns at its end; otherwise nothing."*
Implementierung: `src/Lyric.Frontend/Sema/FlowAnalyzer.cs:129-137` — statt `return assigned;` das
Ergebnis von `AnalyzeStatements(tr.Body…)` zurückgeben, wenn `tr.Catches.All(c => Flow.AlwaysExits(c.Body))`
(`Flow.cs:46` hat den Prädikat schon). **~5 Zeilen.** Korrekt, weil eine Ausnahme mitten im Body
nur in einen catch führt, und jeder catch verlässt. Additiv: kein Programm, das heute kompiliert,
ändert sich; nur `LYR-SEM0018`-Fälle werden weniger.

## Soll — Stufe (b): `try` als Ausdruck (mit language-review vereinbart)
```
Primary  += TryExpr .
TryExpr   = 'try' [ '?' ] UnaryExpr [ 'catch' '(' CatchBinding ')' ( Block | Expr ) ] .
```
- **Disambiguierung**: `try` + `{` bleibt `TryStmt` (§5, `Parser.Statements.cs:39`); jedes andere
  Token nach `try` öffnet `TryExpr` (§6.2). Heute ist das ein Parsefehler (`LYR-PAR0017`), also
  keine Kollision. `catch` ist reserviert, beendet also den Operanden eindeutig.
- **Typen**: `try e catch (x: E) { Block }` → `T`, Block muss auf jedem Pfad verlassen
  (Regel des Block-Arms, `LYR-SEM0033`, `TypeChecker.cs:4155`) — oder, mit Prototyp 05
  (Tail-Expression), darf er einen Wert liefern (`runB2`). `try e catch (x: E) Expr` → Unifikation
  von `T` und `Expr` (§6.9). `try? e` → `?T`; ist `e` selbst `?T`, bleibt es `?T` (kein `??T`,
  §3.3) — "geworfen" und "null" fallen zusammen wie bei `next()` auf `Coroutine<?T>` (§10).
  `try e` ohne `catch` und ohne `?` ist ein Fehler.
- **Exception-Analyse** (§9.2, `ExceptionAnalyzer.cs`): der Operand ist die geschützte Region;
  `catch (x: E)` deckt genau `E`, `try?` deckt alles (wie `catch (_)`).
- **Definite Assignment** (§7.7): `let x = try … catch { exit }` — die Bindung ist assigned, weil
  der einzige Weg zur Fortsetzung durch den Wert führt. Für die Expr-Form trivial.
- **Result-Reifikation** (Prototyp 01) wird damit `try Result<T,E>.Ok(f()) catch (x: E) Result<T,E>.Err(x)`
  — keine eigene Produktion mehr; das Enum ist reine stdlib.

## Bewertung
| | fails (natürlich) | Ist A | Ist B | Soll (a) | Soll (b) | Kotlin | C# |
|---|---|---|---|---|---|---|---|
| Zeilen für "parse oder exit 2" | 8 (abgelehnt) | 12 | 9 | 8 | **2** | 2 | 6 |
| Grund erhalten | ja | nein (`?`) | ja | ja | ja | ja | ja |
| Schutzbereich minimal | ja | ja | **nein** | ja | ja | ja | ja |

- **Fehlerklassen verhindert**: Umweg B (überdehnter Schutzbereich — fremde Ausnahmen werden vom
  falschen catch verschluckt); Umweg A (Grund geht verloren; `?T` für einen Fehler, der einen
  Grund HAT, widerspricht §9.0). `try?` ist die ehrliche Form von Umweg A: sichtbar am Aufruf.
- **Aufwand**: (a) Sema ~5 Z., Spec 1 Satz — **sofort machbar**. (b) Parser ~40 Z. (neuer
  Primary, CatchBinding-Parser existiert), Sema ~80 Z. (Typ, Block-Exit-Prüfung existiert,
  ExceptionAnalyzer: neue Region-Art), Lowering: Desugar in die bestehende try/catch-Lowering
  mit einem synthetischen Lokal (~60 Z.), VM 0, stdlib 0.
- **Breaking**: nein, beide Stufen additiv (Minor).
- **Wechselwirkungen**: Narrowing — `try? f()` ist ein Identifier-loser Ausdruck, narrowt also
  erst nach Bindung (konsistent mit §7.4); Optionals — `try?` auf `?T`-Rückgabe kollabiert (oben);
  match — `try` im Arm-Ausdruck erlaubt; throws — die Funktion um ein `try … catch (x: E) …`
  braucht keine `throws`-Klausel für E; Generics — `try? f<T>()` unproblematisch; Coroutinen —
  `try? resume co` / `try? co.next()` decken den Pull (§10 "Throwability of a pull") ab, ein
  hübscher Nebeneffekt.
- **Offene Semantikfragen**: (1) mehrere `catch`-Klauseln in der Ausdrucksform — Vorschlag nein,
  wer mehr braucht, nimmt das Statement. (2) `defer` innerhalb eines Ausdrucks — nein (kein Block).
  (3) Bindet `try e catch (x) …` bei bloßem `throws` des Operanden `x: Throwable`? Ja, wie §9.3.

## Empfehlung
**Stufe (a) sofort** (Patch-fähig, reine Analyse-Verfeinerung). **Stufe (b) als Minor-Feature** —
sie ist die Form, die 01 (Result), dieses Problem und Swifts `try?` in einer Produktion abdeckt.
