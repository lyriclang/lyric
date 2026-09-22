# Der Plan — eine Liste

Zusammenführung von vier Quellen: dem Bug-Hunt (146 Einträge), dem Usability-Review (117), dem
Evolution-Team (94 plus `deliverables/new-features/roadmap.md`) und den Regelfragen, die der
Sweep aufgeworfen hat (`SPEC-RUNDE.md`).

**Lesart.** „gemessen" heißt: ein Repro lief gegen den Integrationsstand und hat geantwortet.
„behauptet" heißt: es steht in einem Bericht und ist nicht nachgefahren. Alles ohne Marke ist
Fund, nicht Arbeit.

**5.0 wird gesammelt, nicht geplant** (Maintainer, 2026-09-22). Der Abschnitt unten ist eine
Ablage mit Uhren, kein Meilenstein.

---

## 4.5 — was schon drin ist

Gemessen gegen `integration/v4.5`, Repro-Korpora unter `scratchpad/repro/`.

**Aus dem Sweep** (auf `main`, PR #161): do-while-Sprungziele in der Handler-Range · Monomorphisierung
ohne Modulpräfix · `if (c) 1 else 2.5` · Struct-Aliasing an drei Bindungspunkten · lvalue-Regel für
`++`/`--`, Lambda-Bodies und Block-Arme · Narrowing nach Zuweisung · Destructuring als Call-Site ·
§5.4 Extension vor Default · werfender `defer` auf dem Durchfall-Pfad. **16 Repros, alle grün.**

**Aus M37** (auf `main`): Profile · `lyric.json` v2 · `std.build` v2 · Projektverben · und drei
CLI-Funde nebenbei: `--grant none` greift, Option vor der Datei, `lyrvm … -- --grant`.

**Aus den vier Branches** (PR #162): Pattern-Compiler mit neun geschlossenen Defekten · f-String
rendert `Display` · `throw` als Ausdruck · Labels · Value-Block · `try` in der Definite Assignment ·
`defer` im if-Körper · `std.result` · Iterator-Terminatoren · Container · `comptime` ·
`extern "dotnet"` · `parseInt`/`powInt` ohne stillen Überlauf.

**Spec-Zwilling**: `lyric-spec#38` retiriert die zwei Pattern-Limits in §7.6. **Muss vor #162 landen**,
sonst kann die Suite den Stand nicht beurteilen.

---

## 4.5 — was noch reingehört

### A. Der Verifier läuft an der falschen Stelle (zuerst)

`ModuleLowerer.cs:468–487`: Inliner, ScalarReplacement, Devirtualizer und Reachability laufen
**vor** `IrVerifier.VerifyOrThrow`, und `Phase.cs:52` schaltet ihn nur im Debug-Build ein. Beides
zusammen heißt: ein Lowering-Defekt ist im Release doppelt unsichtbar, und eine Optimierung kann
fehlerhaftes IR zudecken — belegt am selben Fall mit und ohne Schleife im Körper.

**Der Umbau ist durchgerechnet**, nicht geschätzt: mit `Optimize = false` verifiziert bestehen die
gesamte stdlib, ihre Tests und alle 47 Beispiele ohne einen Befund. Wer die Reihenfolge ändert,
fasst fünf Tests an und sonst nichts. Dazu: im Release verifizieren oder den Bytecode-Reader
typisieren lassen (Bug-Hunt P1-19).

*Das gehört an den Anfang, weil jede folgende Messung sonst weniger wert ist.*

### B. Prozessabbrüche — Exit 134/141 statt Diagnose oder Panik

| Fund | Ort |
|---|---|
| Tiefenlimit fehlt im rekursiven Abstieg (Parser, Typ-Parser, `CheckBinary`) — tötet auch `lyrls`/`lyrdbg` | Parser.cs, TypeChecker.cs |
| `lyrc build -o ""` → unbehandelte `ArgumentException` (**gemessen, weiterhin offen**) | Lyrc/Program.cs:99 u. a. |
| `string * n` / `arrrep` mit großem n → `ArgumentOutOfRange` bzw. OOM statt `VM0006` | NativeRegistry.cs:335 |
| Global-Vorwärtsbezug → `InvalidOperationException` in `ldfld`; `SEM0057` nicht transitiv | TypeChecker.cs:1451 |
| Reentranz Skript→Host→Skript umgeht `MaxCallDepth` → CLR-StackOverflow | ScriptInstance.cs:226 |
| Terminal-Injection: ESC/BEL/OSC roh auf stderr | DiagnosticEngine.cs:92 |
| Rest-ICEs aus gültigem Code (`let x = [[]]`, `o += 1` auf narrowed `?T`, `defer { throw }` in `try`) | FunctionLowerer.cs |

`Lyrc/Program.cs:33` fängt `InternalCompilationException` nicht — eine ICE ist heute ein Stacktrace
ohne Code und ohne Position, und im JSON-Modus zerstört sie die Ausgabe.

### C. Sema-Löcher, die die Branches offen gelassen haben

- **Erschöpfung über ein Tupel, das ein Enum enthält** (`match ((E.A(n), m))` über `(E, int)` →
  `SEM0050`). **Gemessen.** Das Lowering kennt die Form, die Abdeckungsrechnung nicht.
- **Generische Methode auf generischem Typ** (`Result<T,E>.map<U>`) → `IR0001`. Blockiert
  `Result.map`, `List.map`, `Iterator.toList`. Von drei Seiten gemeldet.
- **Lambda kann keine `throws`-Klausel tragen** (`SEM0084`) — verhindert `assertThrows`.
- **`mut fn` wird auf Klassen nicht erzwungen, auf Structs schon.** Der Guide macht keinen
  Unterschied, die Regel ist so nicht lehrbar.
- **Feld und Methode teilen einen Namensraum** (`RES0001`).
- **`p.field ??= x`** ist `IR0001`, auf einer Variablen geht es.
- **Interface-Wert erfüllt seine eigene Constraint nicht.**
- **`rawArrayAlloc<T>(n)`** (~15 VM-Zeilen): hängt doppelt — `List<?T>`/`Map<K,?V>` brauchen es,
  und der benannte Rest `[first, ..rest]` auch. Billigster Posten der Liste.

### D. Diagnostik

`IR0001` trägt Semantikfehler, die keine Implementierungsgrenze sind (fehlendes Pflichtfeld,
`p.x++`, `x == null` auf Nicht-Optional) — §12.1 reserviert den Code für gültiges Lyric. Dazu:
keine Deduplizierung, `SEM0058` vergiftet nicht, `SEM0052` schlägt das Geschriebene vor, Sema
läuft auf Parser-Recovery-Knoten, `--json` unvollständig, `--verbose`-Zeiten ×100 auf Linux.

### E. Zwei Regelfragen, die als Bugfix durchgehen

- **Shadowing** (`SPEC-RUNDE` 4): `let x = 1; let x = 2;` verwirft die zweite Bindung still. Ablehnen
  oder Rust-Shadowing — **beides ist besser als heute**, der heutige Zustand ist keine der beiden
  Antworten. Prototyp 10 liegt vor.
- **Werfender `defer`** (`SPEC-RUNDE` 5): der `return`-Pfad führt die Kette weiterhin doppelt aus.
  Braucht vorher die Antwort, ob die vor dem Werfer geplanten Stufen noch laufen (Go: ja).

---

## 4.6 — die nächste Feature-Runde

Aus der Roadmap des Evolution-Teams, unverändert in der Reihenfolge, plus was aus den Reviews
dazugehört.

| # | Feature | Stand | Hängt an |
|---|---|---|---|
| 1 | `?T == ?T` | designt | — |
| 2 | Konformanz-Synthese (Equatable/Hashable/Ordered/Display), Swift-Modell ohne `derive` | designt | 1 |
| 3 | typed throws St. 1 (Substitution an der Aufrufstelle — Bugfix) | designt | — |
| 4 | typed throws St. 2 (Inferenz von `E`, `throws never`) | designt | 3 |
| 5 | `try`-Ausdruck (`try e catch (x: E) …`, `try? e`) | designt | Value-Block ✅ |
| 6 | Bedingte Konformanz (`extend<T :: [Display]> List<T> :: [Display]`) | designt | 2 |
| 7 | typed throws St. 3 (werfende Funktionstypen und Lambdas) | designt | 4 |
| 8 | Raw-/Mehrzeilen-Strings | Prototyp 19 | — |
| 9 | Named arguments | Prototyp 12 | — |
| 10 | `@NonExhaustive` | designt, Andockstelle benannt | Zeugen-Routine ✅ |
| 11 | **Inkrement abgeleitet statt eingebaut** (`SPEC-RUNDE` 1) | entschieden, nicht gebaut | — |

Zu **11**: der Maintainer hat die Richtung gesetzt — `++`/`--` folgen entweder aus `Add<T, R>`
(`x.add(1)`, verlangt eine Konformanz mit `int` als `other`) oder bekommen eigene `Inc`/`Dec`-
Interfaces. Empfehlung steht auf A (Rule 2: kein zweiter Mechanismus für „plus eins"). Im selben
Satz zu klären: die Statement-Form beider Schreibweisen (§6.8 lässt heute keine zu, akzeptiert aber
`x++;`) und worauf ein Inkrement stehen darf (heute nur auf einem Local, `p.x++` ist `IR0001`).

---

## Gesammelt für den Major — keine Planung, eine Ablage

Jede Position braucht eine 4.x-Warnstufe, bevor sie greifen kann. Die Uhren sind der eigentliche
Inhalt dieser Liste.

| Position | Bruch | Uhr muss starten |
|---|---|---|
| Member-Sichtbarkeit (Default privat) | groß | 4.6 als Warnung |
| stdlib: Überladung statt Typ-Suffixe (`abs`/`absInt`) | groß | 4.6 als `@Deprecated` |
| `try e` → `Result<T, E>` | additiv, aber Anker | nach dem `try`-Ausdruck |
| f-String-Formatsprache spec-fixiert (Python/Rust statt .NET) | mittel | 4.6 |
| **`?Struct` als Wert** (`SPEC-RUNDE` 2) | mittel | — |
| **`let`-Struct-Felder** (`SPEC-RUNDE` 3) | klein | — |
| **Parameter vs. `let`** (`SPEC-RUNDE` 6) | klein | — |

Die drei aus `SPEC-RUNDE` hängen zusammen und sind bisher **nirgends entschieden**: `?Struct` teilt
heute statt zu kopieren (§13 zählt `?Struct` in keiner seiner beiden Listen auf), ein `let`-Struct
lässt seine Felder schreiben, und ein Parameter ist eine unveränderliche Bindung mit veränderlichen
Feldern — letzteres ausdrücklich getestet, ersteres nur, weil die Prüfung nicht bis zur Wurzel läuft.
An `?Struct` hängt außerdem, ob `struct Node { next: ?Node }` weiter läuft.

**Bewusst nicht in der Ablage** (Begründungen in der Team-Roadmap): nestbare Optionals, `Option<T>`
als Enum, `?`-Operator auf `Result`, Vererbung, `finally`, Threads, `never` als allgemeiner Typ,
Ordnung auf Optionals, Block-Ausdruck für jeden Block, `break value`, `derive`-Schlüsselwort.

---

## Was als Nächstes ansteht

1. `lyric-spec#38` mergen, dann PR #162.
2. **A** (Verifier-Reihenfolge) — weil jede spätere Messung sonst weniger wert ist.
3. **B** (Prozessabbrüche) als eigene Sweep-Runde.
4. **C** und **D**, dann 4.5 ausliefern.
5. Erst danach die 4.6-Liste, Position für Position.
