# Roadmap-Empfehlung 4.5 / 4.6 / 5.0 — Sprachfeatures

Stand 2026-09-22, Basis v4.4.1 (`dc32100c`). Verfasst von **new-features**, abgestimmt mit
**stdlib-redesign**, **pattern-lambda**, **macro-abi**, **lyriclings**.

## Die Regel, an der alles hängt

CONTRIBUTING Regel 2 („ein Mechanismus pro Konzept") und Regel 3 („jeder Meilenstein liefert etwas")
geben die Reihenfolge vor: **erst die Features, die eine bestehende Inkonsistenz beseitigen**, dann die,
die etwas Neues ermöglichen, zuletzt die, die etwas brechen. Kein Vorschlag dieser Liste führt einen
zweiten Mechanismus für ein bestehendes Konzept ein — `Result<T,E>` bleibt ein **Wert** (stdlib), die
Propagation bleibt `throws`.

## 4.5 — „Die Ausdrücke werden vollständig"

Thema: was in Ausdrucksposition stehen darf, und was ein Wert rendert. Alles additiv, Format 4.0.

| # | Feature | Status | Aufwand | Abhängig von |
|---|---|---|---|---|
| 1 | **f-String rendert `Display`** | ✅ implementiert | S | — |
| 2 | **`throw` als Ausdruck, `never` als Rückgabetyp** | ✅ implementiert | M | — |
| 3 | **Labels für `break`/`continue`** | ✅ implementiert | M | — |
| 4 | **Value-Block (Tail-Expression)** | ✅ implementiert | M | 2 (never-Tail) |
| 5 | **`try` trägt zur Definite Assignment bei** | ✅ implementiert | XS | — |
| 6 | **`defer` in einem `if`-Körper** (Bugfix) | ✅ implementiert | XS | — |
| 7 | **`?T == ?T`** | designt | S | — |
| 8 | **Konformanz-Synthese** (Equatable/Hashable/Ordered/Display) | designt | L | 7 (für `?T`-Felder) |
| 9 | **typed throws Stufe 1** (Substitution an der Aufrufstelle — Bugfix) | designt | S | — |
| 10 | **typed throws Stufe 2** (Inferenz von `E`, `throws never`) | designt | M | 9, `never` (✅) |
| 11 | **`try`-Ausdruck** (`try e catch (x: E) …`, `try? e`) | designt | M | 4 (catch-ValueBlock), 5 |

**Fremde Voraussetzungen in 4.5** (nicht meine Arbeit, aber Blocker für die stdlib):
- **Generische Methode auf generischem Typ** (`Result<T,E>.map<U>`) → `LYR-IR0001`
  (`FunctionLowerer.ReturnTypeOfInstanceMethod`). **HIGH** — verhindert `Result.map`/`List.map` als
  Methoden. Von stdlib-redesign gemeldet.
- **Methode eines generischen Enums unaufrufbar** (`FunctionLowerer.cs:3898`, ein Wort `or Enum`) —
  Fix liegt auf stdlib-redesigns Branch.
- **Argumente einer generischen Methode werden nicht substituiert** (`LowerGenericMethodCall` reicht
  keine `calleeSubstitution` an `MaterializeArguments`): ein Parameter, der `T` geschrieben steht,
  bleibt ein Name, und bei `T = ?int` unterbleibt die Widerung (`store of i64 into ?i64`). Fix und
  Test liegen auf stdlib-redesigns Branch (`a8df8a5d`). Der Pfad für generische **Interface**-Member
  baute dieselbe Abbildung längst — die Lücke war verdeckt, weil der Klassen-Pfad sie zuerst erreicht.
- **`rawArrayAlloc<T>(n): T[]` als privates Native** (~15 VM-Zeilen): hängt inzwischen doppelt —
  `List<?T>`/`Map<K, ?V>` brauchen es für ihre Backing-Arrays (stdlib-redesign), und pattern-lambdas
  benannter Rest `[first, ..rest]` braucht es, weil das Lowering kein Array unbekannter Länge bauen
  kann. **Zwei Features, ein Native, und es ist der billigste Posten der ganzen Liste** — deshalb
  hier und nicht mehr nur als Fußnote unter „nestbare Optionals".
- `Satisfies` straffen, damit `println([1,2,3])` in der **Nutzerdatei** scheitert statt in `console.lyr:51`.
- `p.field ??= x` lowern (heute IR0001, von lyriclings gemeldet).
- Interface-Wert erfüllt seine eigene Constraint nicht (`Satisfies`, von mir gemeldet).
- `@Deprecated` auf Methoden/Feldern (`OnMethod`/`OnField`-Anker liegen vor).

**Warum diese Reihenfolge:** 1–6 sind gebaut und grün; 7 ist Voraussetzung für 8; 9 ist ein Bugfix, der
Stufe 2 trägt; 11 braucht den Value-Block, der schon steht. Die Kombination 1+8 ist der größte
sichtbare Gewinn pro Zeile: ein Wertetyp wird mit **einer** Konformanzliste druckbar, vergleichbar,
hashbar und sortierbar, und `f"{p}"` zeigt ihn.

## 4.6 — „Generische Konformanz und Werfbarkeit"

Thema: was Bibliotheken über Typen aussagen können. Additiv, aber Sema-schwer.

| # | Feature | Status | Aufwand | Abhängig von |
|---|---|---|---|---|
| 12 | **Bedingte Konformanz** (`extend<T :: [Display]> List<T> :: [Display]`) | designt | L | — (entfaltet sich mit 1) |
| 13 | **typed throws Stufe 3** (werfende Funktionstypen, werfende Lambdas) | designt | L | 10 |
| 14 | **Bedingte Methoden** (`extend<T :: [Ordered<T>]> List<T> { fn max() }`) | designt (fällt mit 12 ab) | S | 12 |
| 15 | **Raw-/Mehrzeilen-Strings** (`r"…"`, `"""…"""`) | Prototyp 19 des Vorteams, unverändert gültig | M (rein lexikalisch) | — |
| 16 | **Named arguments** (`connect(host: "h", port: 80)`) | Prototyp 12 des Vorteams | M | — |
| 17 | **`pub`/`pub(module)` als Warnstufe** | designt (Teil von 5.0) | M | — |

**Fremd:** pattern-lambdas if-let/while-let/let-else und Closure-Kurzsyntax (auf ihrem Branch gebaut),
macro-abis `comptime` (prototypisiert), `@NonExhaustive`.

**Warum hier:** 12 und 13 sind die beiden Features, die die stdlib von „geht nicht" auf „geht" heben
(`Display` auf Containern, `assertThrows`, werfende `map`/`filter`). Beide brauchen mehr Sema-Arbeit als
alles in 4.5 und sollten nicht mit einer Release-Frist kollidieren. 15/16 sind unabhängig und können
jederzeit dazwischenrutschen — ich habe sie nicht erneut designt, weil die Prototypen 19 und 12 des
Vorgängerteams vollständig sind; meine einzige Ergänzung: ein Raw-/Mehrzeilen-String ist ein Literal
und damit als Attributwert erlaubt, was macro-abis `@Command { help = """…""" }` ermöglicht.

## 5.0 — „Was bricht"

Thema: Entscheidungen mit Deprecation-Uhr. Jede braucht eine 4.x-Warnstufe, bevor sie greift.

| # | Feature | Status | Bruch | Uhr ab |
|---|---|---|---|---|
| 18 | **Member-Sichtbarkeit** (Default privat) | designt | groß | 4.5 (Warnung) |
| 19 | **`try e` → `Result<T, E>`** (Option B) | designt | nein, aber Anker | 4.6 |
| 20 | **stdlib: Überladung statt Typ-Suffixe** (`abs`/`absInt`) | stdlib-redesign | groß | 4.5 (`@Deprecated`) |
| 21 | **f-String-Formatsprache spec-fixiert** (Python/Rust statt .NET) | Usability-Analyse Major 5 | mittel | 4.6 |
| 22 | **Shadowing-Regel in §7.1** | Usability-Analyse (heute ein Bug) | klein | sofort als Bugfix |

**Bewusst NICHT in der Roadmap:**
- **Nestbare Optionals** (`??T`) — Major, Formatwechsel, und die beiden Stellen, die danach riefen
  (Backing-Arrays, `..rest`), lösen sich mit `rawArrayAlloc` ohne Sprachänderung.
  **Der Preis der Regel ist jetzt gemessen und benannt** (stdlib-redesign, Gegenprobe an `std.result`):
  jede Signatur, die `?T` schreibt, ist für `T = ?U` ein `??U` und damit nicht instanziierbar — konkret
  `Result.ok(): ?T` und `fromOptional(o: ?T)` für optionale Payloads. `Result<?int, E>` selbst trägt
  (Konstruktion, Matchen, `isOk`/`unwrapOr`/`err`/`map`); es fehlen genau die Methoden, die eine
  Abwesenheit als Rückgabe verwenden. Das ist die Bibliotheksoberfläche der Sprachregel, in `std.result`
  dokumentiert, und **kein Argument für `??T`**: wer beides will, hat zwei Abwesenheiten, die niemand
  auseinanderhalten kann — genau die Unterscheidung, die `?` nicht treffen soll.
- **`Option<T>` als Enum statt `?T`** — stdlib-redesign empfiehlt `Iterator.next(): ?T` auch für 5.0;
  ich stimme zu: `?T` ist die eine Antwortform, und ein zweiter Optionaltyp wäre ein zweiter Mechanismus.
- **`?`-Operator auf `Result`** — Prototyp 01 hat belegt, dass `throws` bereits Rusts `?` ist.
- **Vererbung, `finally`, Threads** — CONTRIBUTING Regel 2.
- **`never` als allgemeiner Typ** (Rust `!`) — nur Rückgabe- und throws-Typ.
- **Ordnung auf Optionals** (Rust/Swift `None < Some`) — stille Sortierfehler.
- **Block-Ausdruck für jeden Block** (Rust) — die `;`-Falle; Lyric bleibt bei Wert-Blöcken.

## Abhängigkeitsgraph (verdichtet)

```
never (✅) ──► Value-Block (✅) ──► try-Ausdruck ──► try e → Result (5.0)
   │                                    ▲
   └──► typed throws St.2 ──► St.3 ─────┘
            ▲                    │
     St.1 (Bugfix)               └──► stdlib: assertThrows, werfende map/filter

?T == ?T ──► Konformanz-Synthese ──► f-String (✅) zeigt jeden Wertetyp
                   │                      ▲
                   │                      │
bedingte Konformanz ┴──────────────────────┘  (Display auf Containern)
                   └──► ToJson/FromJson-Synthese (macro-abi Stufe 1)

Member-Sichtbarkeit (5.0) ◄── @Deprecated auf Membern (4.5) ◄── stdlib-Umbau (erledigt)
```

## Abhängigkeiten zu den anderen Teammitgliedern

**pattern-lambda** (Branch `worktree-agent-a1c2eb789de86ba9d`, **fertig**, 4 Commits):
- Gebaut: rekursiver Pattern-Compiler (verschachtelte Varianten, Or-Patterns mit Bindungen, Literale in
  Tupeln und Payloads, Literal-Adaption), if-let/while-let/let-else, Closure-Kurzsyntax mit
  Trailing-Lambda und Parameter-Destructuring, Array-Patterns mit Rest, und SEM0050 mit **Zeugen-Pattern**
  („no arm matches 'Some(false)'").
- Baut auf meiner never-Regel auf (`UnifyArms`: ein Arm vom Typ `never` trägt nichts bei); ihre
  Sema-Seite des lyriclings-ICE (ein `match` ohne wertliefernden Arm ist `never`) ist gebaut, meine
  Lowering-Seite ebenfalls — **der ICE ist nach dem Merge beidseitig geschlossen**.
- `@NonExhaustive` haben sie **designt, nicht implementiert** (`design/patterns.md` §3.6) — es bleibt
  eine 4.6-Position und die Exhaustiveness-Seite von stdlib-redesigns Anker. **Andockstelle benannt:**
  ihre Zeugen-Routine (`TypeChecker.MissingCases`/`MissingVariants`/`MissingArrayCases`, ~4290–4420)
  bekommt eine zusätzliche „unbekannte Variante" in die Aufzählung, wenn das Enum markiert ist und der
  `match` außerhalb des deklarierenden Moduls steht. Damit ist die Position nicht nur gewollt, sondern
  verortet.
- Ihr `design/lambdas.md` §3.2 (werfende Funktionstypen) ist die Lambda-Seite meines typed-throws-Designs;
  zwei Verfeinerungen daraus (`FnType.Throws`, geschriebene Klausel ohne Kontext) sind übernommen.
- **Merge:** keine Konflikte. Sechs Berührungspunkte, alle in `design/value-block.md` notiert; einer
  davon verkleinert nach dem Merge beide Seiten (ihr `HoldsStatements` entfällt zugunsten meines
  ValueBlocks). Ihr `IsStructInitAhead` beantwortet zusätzlich eine offene Frage meines
  Value-Block-Designs (Struct-Initializer als Tail ohne Klammern).

**stdlib-redesign** (Branch `worktree-agent-aa7b5e912a78e6f2d`, fertig):
- Braucht von mir: bedingte Konformanz (12), typed throws (9/10/13), Konformanz-Synthese (8),
  `?T == ?T` (7), `pub(module)` (18), und den `try`-Ausdruck (11/19).
- Liefert mir: `combineHash`, `OnMethod`, `NonExhaustive`, `Result<T,E>`, `Display`-Implementierungen.
- **Der eine Dissens ist aufgelöst** (ihr Nachtrag): `?T :: [Display]` bleibt undefiniert — eine
  Abwesenheit soll nicht still als Text erscheinen, und Rust hält es genauso. Sie nehmen
  `showOptional(o, ifNone)` in std.option auf; `assertEq` rendert Optionals selbst und schreibt `null`
  als sichtbares Wort, was in einem **Testbericht** gewollt ist und in Programmausgabe nicht. Keine
  offenen Punkte zwischen uns.
- **Korrektur zu ihrem ersten Stand:** `Result<?T, E>` ist nicht allgemein blockiert (Vollständigkeits-
  test liegt vor); offen bleibt nur die Payload-Bindung im Pattern (bei pattern-lambda gebaut) und die
  strukturelle `??U`-Grenze oben.

**macro-abi** (Branch `worktree-agent-ab3c14434f8eda027`, fertig):
- Ihre Makro-Stufe 1 **ist** meine Konformanz-Synthese; die Schema-Tabelle in
  `design/conformance-synthesis.md` ist so formuliert, dass ToJson/FromJson als weitere Zeilen passen.
- Ihr `comptime` und ihr `extern "dotnet"` kollidieren mit keiner meiner Grammatikänderungen (geprüft:
  `IDENTIFIER ':'`, `throw`/`try` als Primary, Tail im ValueBlock, `r"`/`"""`).
- Offener Punkt bei ihnen: `comptime`-Wert als Attributwert (Guide-15-Lücke „no constant folding").

**lyriclings** (fertig): drei Befunde, alle eingeordnet — ICE (meine Lowering-Seite ✅, ihre Sema-Seite
bei pattern-lambda), `??=` auf Feldern (4.5-Bug), `mut fn` auf Klassen (Doku-Klarstellung §3.4).

## Was ich anders entschieden hätte als die Vorgängeranalyse

1. **`break value`** (Prototyp 05 Vorschlag B, dort schon verworfen): bleibt verworfen — aber der Grund
   ist jetzt stärker, weil Labels existieren und `break outer value` erst recht mehrdeutig wäre.
2. **`derive`-Schlüsselwort** (Prototyp 03 Variante B'): verworfen zugunsten des Swift-Modells; die
   Konformanz ohne Body ist bereits eindeutig, und macro-abi baut ohnehin auf der Syntheseregel auf,
   nicht auf dem Wort.
3. **`throws` nur für Coroutinen** (§4-Prosa): aufheben. Die Grammatik kann es längst, und die
   Einschränkung kostet die stdlib ihre wichtigste Abstraktion.
