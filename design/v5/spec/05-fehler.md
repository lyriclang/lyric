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
- Klausel `throws Typ` — **ein** Typ oder keiner; `throws A, B` existiert nicht; wer zwei Typen
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
| K7 | **Nachtrag (Bereich 10 I5)** — assoziierte Fehlertypen: `interface Iterator { type Error :: [Error] = never; fn next(): ?Item throws Error; }`; **Join-Regel** beim Zusammensetzen (Adapter über werfendem Lambda): gleicher Typ → dieser, einer `never` → der andere, verschieden → Wurzel `Error` (K2, Typ-Pattern zum Unterscheiden) | Swift 6 `AsyncIteratorProtocol<Failure>` |
| K7 | **Präzises Rethrow**: `catch (e)` ohne Typ trägt statisch **die Menge** der gefangenen Typen; `throw e` wirft genau sie; `match (e)` über die Menge ist erschöpfend ohne `_`; beim Speichern weitet `e` auf `Error`. Beschränkt auf Catch-Bindungen — kein zweiter Typmechanismus | **Zig Error-Sets** |
| K8 | `try` in einer Funktion ohne Klausel und ohne `catch`: „behandle oder deklariere" | Swift, Java |

## E3 — Was ist werfbar: **entschieden** (2026-09-28)

**Jeder Typ, der `Error` konformiert** — Klassen, Structs, Enums. Enums sind der natürliche
Fehlertyp (`enum ParseError :: [Error] { Empty, BadDigit(char, int) }`, erschöpfendes `match`
im `catch`), Structs für Fehler mit Daten, Klassen für Identität oder Ursachenkette (E6);
`sealed`-Interfaces (D8) als Fehlerfamilie mit erschöpfendem `match`. Laufzeit: **Boxing beim
Wurf** (V7-Form), Kosten nur auf dem Fehlerpfad; Typtest = Deskriptorvergleich. Verworfen: nur
Klassen (Lyric 4, Java, C#). (Rust, Zig, Swift)

## E4 — Die `try`-Familie: **entschieden** (2026-09-28) — Arbeitsnotation, Syntax Bereich 8

| Form | Ergebnis | Regel |
|---|---|---|
| `try f()` | `T`, propagiert | Pflicht (E1); **deckt den ganzen Ausdruck rechts** (Swift); `try` ohne werfenden Aufruf darunter = Warnung |
| `try? f()` | `?T` | `null` bei jedem Fehler; **keine Abflachung** (`f(): ?int` → `??int`, „geworfen" ≠ „null geliefert", O1; Swift 5 flacht ab) |
| `try! f()` | `T` | Panik mit Meldung und Position |
| `try f() catch (e: A) expr` | `T` | **als Ausdruck**, mehrere Klauseln; Klauseln decken die Menge oder die Funktion deklariert den Rest; Klauselkörper Ausdruck oder Value-Block; `throw` als Körper für Rethrow-mit-Kontext (E6) |
| `try { … } catch (e: A) { … }` | Statement | bleibt, Blockform derselben Sache |
| `catch (e)` ohne Typ | Menge nach K7 | — |

`try` steht überall, wo ein Ausdruck steht; die Deckungsregel gilt für den umschließenden Rumpf.

## E5 — Die Zwillingsdoktrin: **entschieden** (2026-09-28) — fällt

Ihr Grund (Wurf teuer in der VM, Wurfpfad ohne JIT) ist unter L5 weg. **Eine Funktion je
Operation**; der Aufrufer wählt die Form mit einem Zeichen: `try? read(p)` (werfend →
optional), `map[k] ?? throw NotFound { key }` (optional → werfend; `throw` ist ein
`never`-Ausdruck seit 4.5). Kein `OrThrow`-, kein `OrNull`-Suffix, kein `tryParse` mit `bool`.

**Die Grenze (Guide-Satz)**: `?T`, wenn Abwesenheit ein normales Ergebnis ohne Grund ist
(`map[k]`, `first`, `find`, `parent`); **Wurf, wenn das Scheitern einen Grund trägt, den der
Aufrufer brauchen könnte** (I/O, Parsen mit Position, Netz, Prozesse). Nie beides. Je API
einmal entschieden nach „trägt der Fehler Information?". Beantwortet Korpus-Widerspruch W7.

## E6 — Fehlerobjekt: **entschieden** (2026-09-28)

| # | Regel | Vorbild |
|---|---|---|
| O1 | **`interface Error { fn message(): string; fn cause(): ?Error { return null; } }`** — Wurzel aller werfbaren Typen; ohne Vererbung (D1) kann die Wurzel nur ein Interface sein. `Debug` automatisch (D7) | Swift, Rust |
| O2 | Ursache ist Sache des Typs (`class ConfigError :: [Error] { message, cause: ?Error }`); Rethrow-mit-Kontext `catch (e: IoError) throw ConfigError { …, cause = e }` | Java, Rust |
| O3 | **Unterdrückte Fehler und Backtrace leben in der Box**, nicht im Typ (ein geworfener Wert wird geboxt, E3): `e.suppressed(): Error[]`, `e.backtrace(): ?Backtrace` von der Laufzeit für jeden Fehlerwert — löst „ein Interface ohne Speicher kann nichts anhängen" | — |
| O4 | **`main` darf `throws`**; entkommener Fehler: `error: <message>`, Ursachenkette, Backtrace im Debug, **Exit 1** (Panik: 101). Die 4.x-Regel fällt | Rust, Go |
| O5 | Host-/C-Grenze: Wrapper nach L5 E7, Form Bereich 11 | — |

## E7 — `defer` und Ressourcen: **entschieden** (2026-09-28)

**`defer`** unverändert: block-scoped, einmal je Iteration, LIFO, bei jedem Verlassen außer
Panik; fallengelassene Koroutine läuft keine (bleibt; das Verb dafür ist Bereich 6).

**Werfender `defer`** (SPEC-RUNDE 5, beide Hälften, nach L5 E4):

| Lage | Regel |
|---|---|
| wirft beim normalen Verlassen | der Fehler verlässt den Block wie jeder andere (Menge zählt zur Funktion); **früher registrierte `defer` laufen trotzdem** (Go) |
| wirft, während ein Fehler unterwegs ist | **erster gewinnt, zweiter wird angehängt** (`suppressed`), Kette läuft zu Ende (Java) — das Gegenteil des gemessenen 4.x-Verhaltens |
| zwei werfen beim normalen Verlassen | der zuerst gelaufene gewinnt, der zweite hängt an |
| `errdefer` | **nein** (Idiom `var ok = false; defer { if (!ok) … }`); Tür (Zig) |

**Ressourcen-Scope** — **Schlüsselwort an der Bindung** (Arbeitsnotation `using let f = open(p);`, C# `using var`; Syntax Bereich 8):

| # | Regel |
|---|---|
| R1 | `interface Resource { fn close(): void throws Error; }` (Name Bereich 10); `close` darf werfen |
| R2 | die Bindung ist `let`; `close()` läuft bei jedem Ausgang außer Panik; **`using` und `defer` sind eine gemeinsame LIFO-Liste** in Registrierungsreihenfolge |
| R3 | ein `Resource`-Wert, der weder `using`-gebunden noch gespeichert, zurückgegeben oder weitergereicht wird: **Warnung** „wird nie geschlossen"; das GC-Netz (L1) fängt den Rest |
| R4 | Fehler aus `close()` propagieren beim normalen Verlassen; während eines Fehlers werden sie angehängt |
| R5 | Kopien der Referenz sind Aliasse; Nutzung nach `close` → Laufzeitfehler „closed" (M12) |
| R6 | `using` auf einem Nicht-`Resource`-Typ: Übersetzungsfehler |
| R7 | **Nachtrag (Bereich 10 I6)**: `for` ruft `close()` auf einem Iterator, der `Closeable` ist, bei jedem Verlassen (`break`, `return`, Wurf) — statisch bei bekanntem Typ, sonst `is Closeable` auf dem Interface-Wert (T11); `Coroutine<T> :: [Closeable]` (I7) |

Verworfen: Blockform `with … as` (Python; zweiter Block für das, was die Bindung sagt),
automatisch für jede `let`-Bindung (implizit; ein abgelegtes Handle würde geschlossen),
`use { }`-Lambda (Kotlin; `return` im Lambda). Zwei Mechanismen — `defer` für beliebigen Code,
`using` für das, was der Typ verlangt und der Compiler prüfen kann (Java, C# ebenso).

## E8 — Panik: **entschieden** (2026-09-28)

Katalog (Index, Division durch 0, Überlauf, Stacküberlauf, `!` auf `null`, `try!`,
`panic(msg)`, `assert`, `unreachable()`): Meldung, Backtrace, **Exit 101**, keine `defer`/`using`
(L5 E5). **Kein `recover`-Wort.** Isolation **an der Koroutinengrenze, ohne Abwicklung**: eine
Panik in einer Koroutine verlässt deren Stack (K1) — kein Frame besucht, keine `defer` —, der
Resumer erhält den Status „gescheitert durch Panik" mit `PanicInfo`; Stack freigegeben, offene
Ressourcen fängt das GC-Netz (L1). Bereich 6 macht daraus Task-Zustände. Hauptstack: Prozessende.
Fremde Frames auf dem Stack (K4-Zähler > 0): Prozessende (Go: Panik durch cgo ist fatal).
**Host-Hook** `Runtime.onPanic(fn(PanicInfo))` zum Loggen/Flushen, kann das Ende nicht
verhindern. Verworfen: nie (Swift, Zig — ein Request-Bug tötet den Server), überall (Go
`recover`, Rust `catch_unwind` — braucht Abwicklung, die L5 nicht hat). (Erlang)

## E9 — `catch`-Klauseln: **entschieden** (2026-09-28)

| # | Regel |
|---|---|
| C1 | derselbe Typ zweimal → Fehler |
| C2 | eine Klausel, die eine frühere vollständig abdeckt (Interface, `sealed`-Elternteil, `Error`) → **Fehler „unerreichbare Klausel"** (heute still tot; Java) |
| C3 | Catch-all `catch (e)` zuletzt, sonst Fehler; aus dem Anhang nach §9 |
| C4 | Klauseln müssen die Menge nicht decken; Rest propagiert, wenn deklariert (K8) |
| C5 | **Mehrfach-Klausel `catch (e in [A, B])`** (Listenregel 08 D5/D6: `catch (e in A)` erlaubt, `catch (e: A)` bleibt die Annotationsform) (Bereich 8, Y6 — `in` + eckige Liste wie jede Mehrfachliste), `e` trägt die Menge (K7) — Java Multi-Catch ohne Unionstyp |
| C6 | Wurf aus einer Klausel wird von Schwesterklauseln nicht gefangen (normiert) |

## E10 — Fehler über Grenzen: **entschieden** (2026-09-28)

Task-Ergebnis **trägt den Fehler** (warten liefert `T throws E`; Form Bereich 6);
`Coroutine<T> throws E` bleibt (Ziehen wirft); C-Grenze nach L5 E6/E7 (Wrapper-Form Bereich 11);
Host sieht eine Lyric-Ausnahme als **strukturiertes Fehlerobjekt**, nie als Panik; eine Panik
über den Hook (Bereich 11).

## E11 — Kontrollfluss: **entschieden** (2026-09-28)

**`loop { … }`** als Endlosschleife (Rust, Zig; Definite Assignment weiß, dass sie nur über
`break` endet); **`break value` nur aus `loop`** (`let found = loop { …; break x; };` — bei
`while`/`for` gäbe es den „nicht gebrochen"-Fall; Rust ebenso). `while`, `do-while`,
`for (x in …)`, Labels, `let`-Bedingungen bleiben; `for` über `Iterable`/Ranges (Bereich 10);
`if`/`match`/`try` als Ausdruck und Value-Block bleiben; `match`-Erschöpfung durch Tupel
hindurch (4.x-Fund, gebaut). Kein `goto`, kein Fallthrough, kein `switch`.

## E12 — `never`: **entschieden** (2026-09-28)

Builtin-Typname; **gültig nur als Rückgabetyp** von Funktionen und Lambdas; `throw`, `panic`,
`unreachable()`, `loop` ohne `break`, `return` haben Typ `never`; **`never` koerziert zu jedem
Typ** an Ausdruckspositionen (T3-Liste); jede andere Position ist ein Sema-Fehler — die drei
4.x-ICEs werden Diagnosen. (Rust `!`, Swift `Never`)

---

**Bereich 5 ist damit vollständig entschieden** (E1–E12, 2026-09-28).
