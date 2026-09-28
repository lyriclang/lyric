# 05 — Fehler und Kontrollfluss

Lebendes Dokument des Bereichs 5. Fragen E1–E12, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` L5 (Fehlerrückgabe-ABI: ein Wurf kostet einen Return; Paniken unfangbar auf
Prozessebene; erster Fehler gewinnt, zweiter wird angehängt; Wurf darf C nicht durchqueren),
`03-typsystem.md` T17 (`throws` im Funktionstyp), T11 (Typ-Patterns), `04-abstraktion.md`.

## Bestandsaufnahme Lyric 4

Aus `../fehler.md` (41 Fragen, Fassung 3), Spec §7.5/§9/§10, Guide 10:

- **Drei Fehlerformen**: Wert (`?T`/`bool`), Ausnahme (`throw`), Panik. Doktrin „whether vs.
  why": stille Form + `OrThrow`-Zwilling aus einer Implementierung (`text`/`textOrThrow`).
- **Nur Klassen sind werfbar** (`Throwable`, `fn message(): string`); ein Enum hat vier
  Antworten (Deklaration/Klausel kompilieren, `catch` `IR0001`, Wurf ICE, `extend` `SEM0030`).
- Klausel `throws [Typ]` — **ein** Typ oder keiner; `throws A, B` existiert nicht; wer zwei Typen
  wirft, schreibt `throws` bar, und `catch (_: A) … catch (_: B)` deckt das nicht — **der
  typisierte Wurf kollabiert auf `Throwable`, ohne Weg zurück** (schwerster Befund).
- Typparameter in der Klausel wird an der Aufrufstelle nicht substituiert; Deckung rechnet auf
  der Definition, nicht der Instanz (`throws Box<int>` gegen `catch Box<string>` kompiliert).
- **Lambdas dürfen nicht werfen** (kein `throws` am Funktionstyp) — der Grund für das fehlende
  `assertThrows`.
- `try`/`catch` nur als Statement; `throw` als Ausdruck (`never`) seit 4.5; `never` fehlt in
  der Grammatik, drei Positionen sind ICEs.
- **`defer`** einziger Cleanup, block-scoped, einmal je Iteration, läuft beim Abwickeln (LIFO);
  Panik und `std.os.exit` laufen keine `defer`; fallengelassene Koroutine läuft keine (normiert).
  Werfender `defer`: unspezifiziert (Uhr `SEM0110`); gemessen: Rückgabepfad läuft ihn zweimal,
  Abwicklungspfad ersetzt die erste Ausnahme; `defer { throw }` im `try` ist ein ICE.
- Kein präzises Rethrow (`catch (e) { throw e; }` verliert den Typ); keine Ursache, keine
  Unterdrückten (`Exception` hat ein Feld); entkommene Ausnahme meldet nur den Typ.
- Tote/verdeckte `catch`-Klauseln still; Catch-all zuletzt nur im Anhang normiert.
- Ein Task kann sein Scheitern nicht melden (`spawn` mit werfender Koroutine per Typ
  ausgeschlossen); eine Lyric-Ausnahme sieht für den Host aus wie eine Panik; .NET-Ausnahme =
  Panik.
- Kontrollfluss: `if`/`match` als Ausdruck, Value-Block, Labels, `let`-Bedingungen, `while`/
  `do-while`/`for-in`, `break`/`continue` ohne Wert.

## E1 — Grundmodell: **entschieden** (2026-09-28) — Klausel, markierte Propagation, `Result` als Wert

Unter L5 ist ein Wurf ein Return; die Frage ist reine Sprachform, auf drei Achsen:

| Achse | Entscheidung | Verworfen |
|---|---|---|
| Wo steht der Fehlertyp | **Klausel**: `fn f(): int throws IoError, ParseError` — der Vertrag neben der Signatur; ehrlich zur ABI (zwei Kanäle), lesbar | Rückgabetyp `Result<int, E>` (Rust), `E!int` (Zig), `(int, error)` (Go) — jeder Fehler durch `match` |
| Propagation | **markiert**: `try f()` ist Pflicht an jedem werfenden Aufruf, ohne `try` ein Übersetzungsfehler; die umschließende Funktion deklariert oder fängt. Sichtbarkeit gegen Kürze — Rusts `?` und Swifts `try` sind das meistgelobte Stück beider Sprachen; ein Refactoring zu „werfend" meldet sich an jedem Aufrufer statt still mitzulaufen | implizit (Java, C#, Kotlin, Lyric 4) |
| Fehler als Wert | **`Result<T, E>` bleibt Bibliothek** für „Fehler aufheben", Brücken in beide Richtungen (`Result.of { try f() }`, `try r.get()`); **kein eigener Propagationsoperator** auf `Result` | Rust/Go (Fehler immer Wert) |

Die drei Fehlerformen werden **zwei**: `?T` für Abwesenheit, Wurf für Scheitern; Panik ist kein
Fehler (L5 E5). Die Zwillingsdoktrin fällt (E5). Familie um ein Wort: `try f()` propagiert,
`try? f()` → `?T`, `try! f()` → Panik, `try f() catch (e: E) { … }` als Ausdruck (E4).
**Schreibweisen sind Arbeitsnotation — Bereich 8 bestimmt die Syntax.** (Swift 6 typed throws)

## E2 — Typisierte `throws`: **entschieden** (2026-09-28)

| # | Regel | Vorbild |
|---|---|---|
| K1 | **`throws A, B, C` ist eine Menge** (Reihenfolge egal, Duplikat Fehler) | Zig Error-Sets, Java |
| K2 | **`throws` bar = `throws Error`** (Wurzeltyp, E6): erlaubt, untypisiert; der Aufrufer fängt `Error` und schaut per Typ-Pattern (T11) hinein. Die Liste ist die empfohlene Form; die stdlib schreibt Listen | Swift |
| K3 | **Deklariert, nie inferiert** für benannte Funktionen; **inferiert für Lambdas**, wenn der erwartete Funktionstyp es nicht vorgibt | Swift |
| K4 | **Generisch**: `fn map<T, U, E>(xs: T[], f: fn(T) -> U throws E): U[] throws E`; `E` aus dem Argument inferiert, **an der Aufrufstelle substituiert**; `E = never` ≡ keine Klausel; ein Aufruf mit leerer Menge nach Substitution braucht kein `try` (überflüssiges `try` = Warnung) | Swift `throws(Never)` |
| K5 | **Deckung auf der Instanz** (`throws Box<int>` ≠ `catch Box<string>`) | — |
| K6 | **Teilmengenregel**: Implementierung wirft ⊆ Interface-Member; Funktionswert mit kleinerer Menge koerziert zu größerer (T3-Liste), nie umgekehrt | Swift, Java |
| K7 | **Präzises Rethrow**: `catch (e)` ohne Typ trägt statisch **die Menge** der gefangenen Typen; `throw e` wirft genau sie; `match (e)` über die Menge ist erschöpfend ohne `_`; beim Speichern weitet `e` auf `Error`. Beschränkt auf Catch-Bindungen — kein zweiter Typmechanismus | **Zig Error-Sets** |
| K8 | `try` in einer Funktion ohne Klausel und ohne `catch`: „behandle oder deklariere" | Swift, Java |

## E3 — Was ist werfbar: **entschieden** (2026-09-28)

**Jeder Typ, der `Error` konformiert** — Klassen, Structs, Enums. Enums sind der natürliche
Fehlertyp (`enum ParseError :: [Error] { Empty, BadDigit(char, int) }`, erschöpfendes `match`
im `catch`), Structs für Fehler mit Daten, Klassen für Identität oder Ursachenkette (E6);
`sealed`-Interfaces (D8) als Fehlerfamilie mit erschöpfendem `match`. Laufzeit: **Boxing beim
Wurf** (V7-Form), Kosten nur auf dem Fehlerpfad; Typtest = Deskriptorvergleich. Verworfen: nur
Klassen (Lyric 4, Java, C#). (Rust, Zig, Swift)

## E4 — `try` als Ausdruck, `try?`, `catch`-Ausdruck: **offen**
## E5 — Die Zwillingsdoktrin: **offen**
## E6 — Fehlerobjekt: Rethrow, Ursache, Unterdrückte, Backtrace: **offen**
## E7 — `defer`, werfender `defer`, Ressourcen-Scope: **offen**
## E8 — Panik: Katalog, `recover`, Hooks: **offen**
## E9 — `catch`-Klauseln: Reihenfolge, tote Klauseln: **offen**
## E10 — Fehler über Grenzen: Tasks, Koroutinen, FFI, Host: **offen**
## E11 — Kontrollfluss: Ausdrucksformen, `loop`, `break` mit Wert: **offen**
## E12 — `never`: **offen**
