# 03 — Typsystem-Kern

Lebendes Dokument des Bereichs 3. Fragen T1–T19, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` (Typidentität über Deskriptoren, `??T` darstellbar, Innenzeiger, Enums inline,
Monomorphisierung mit Instanz-Cache) und `02-wertmodell.md`.

## Bestandsaufnahme Lyric 4

Aus `docs/Grammar.md`, Spec §3/§8, Korpus (`../generics.md`, `../skalare.md`,
`../optionals.md`):

- **Skalare**: `int`/`uint`/`float` 64-bit-Defaults, *distinkt* neben `int64`/`uint64`/`float64`;
  `int8…64`, `uint8…64`, `float32/64`, `bool`, `char` (Code-Punkt), `string`. Keine implizite
  Konversion, `as` einzige Brücke; Literale adaptieren an den Kontext (§3.1), Variablen nie.
  Überlauf definiert, nicht prüfbar (§3.2 verspricht `checked`, nie gebaut).
- **Nominal, strikt invariant, kein Subtyping** (§3.7): `?Circle` wird nicht `?Shape`, auch
  nicht lesend (gemessen). Polymorphie nur über Interface-Werte.
- **Optionals**: `?T`, kein Nesting (`??T` — die VM hatte keinen Platz), `!`, `??`, `?.`,
  Narrowing. Gemessen: im generischen Rumpf beantworten `== null`/`??` (kompilieren) und
  `!`/`match null` (abgelehnt) dieselbe Frage verschieden. Iterator-Ende ist `null` → kein
  `Iterator<?T>`.
- **Generics**: Monomorphisierung bedarfsgetrieben; `::`-Constraints, **an der Deklaration
  geprüft** (Rust/Swift/C#); ~90 B und ~0.5 ms je Instanz, linear, Verifier teuerste Achse;
  Inferenz vorwärts, erste Bindung gewinnt, kein LUB, keine Kontext-Inferenz. Funktioniert:
  generische Methoden auf generischen Typen (4.6), F-Bounds, Mehrfachkonformanz,
  deklarationsseitige bedingte Konformanz (`struct Pair<T :: [Equatable<T>]> ::
  [Equatable<Pair<T>>]`).
- **Fehlt** (gemessen): `Self`; statische Interface-Member; assoziierte Typen; generische
  Extends und die `extend`-Form bedingter Konformanz; Varianz; höhere Kinds; Wertparameter;
  parametrisierte Aliase; Default-Typargumente; partielle Typargumentlisten; instanziierte
  Funktion als Wert; `throws E` mit substituiertem Typparameter (Generics × Fehler rot);
  Typargumente im Pattern-Pfad.
- **Still oder falsch**: `<T :: [int]>` filtert nicht; Phantom-Typparameter ohne Warnung;
  `f<Box<int>>(x)` Parserfehler (`>>`), Formatter zerstört den Workaround; divergente
  Monomorphisierung stürzt in einer Form ab; `a<b>(c)` mit drei `int`s ist ein Aufruf.
- Tupel ab Arität 2; Funktionstypen; `type` transparent, `opaque type` neue Identität; `T[]`
  fester Länge, Länge im Wert; Ranges nur im Schleifenkopf; `extend` auf Arrays/Opaque stiller
  No-Op.

## T1 — Skalare, Literale, Konversion: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| T1a | **`int` = `int64`, `uint` = `uint64`, `float` = `float64` — Aliase**, ein Typ mit zwei Namen (C#-Form). Alle Ziele sind 64-bit; die Distinktheit (Go, Swift, Lyric 4) kaufte 32-bit-Portabilität, die wir nicht brauchen, und kostete zwei Instanzen je Generik, zwei FFI-Zuordnungen und `as` zwischen gleich breiten Typen | distinkt |
| T1b | Literale adaptieren an den Kontext, Default `int`/`float`; **Bereichsprüfung** (`let x: uint8 = 300` Fehler); Suffixe, Unterstriche, Hex/Bin/Okt bleiben | — |
| T1c | **Verlustfreie Ganzzahl-Weitung implizit** (Zig, Java): in jeden Ganzzahltyp, dessen Bereich den Quellbereich enthält (`int8→int16→int32→int64`, `uint8→…`, `uint8→int16`, `uint32→int64`); `float32→float64`; **nie** Verengung, **nie** `int`→`float` (auch nicht verlustfreie Fälle — „Ganzzahl bleibt Ganzzahl"). **Nur an Koerzionsstellen** (Zuweisung, Argument, Rückgabe), nie in der Inferenz; Überladung braucht einen Weitungsrang (Bereich 4) | keine implizite Konversion (Rust, Swift, Go, Kotlin, Lyric 4 — `as` bei jedem FFI-Puffer); `int`→`float` implizit (C#'s Präzisionsfalle über 2⁵³) |
| T1d | **`as`** bleibt der eine, immer gelingende, bit-nahe Operator: Ganzzahl-Verengung **wrappend**, `float`→`int` **sättigend**, NaN → 0 (Rust); `char`↔`uint32` explizit, `uint32`→`char` prüft den Skalarwert; `bool`↔Zahl **nie**. **Geprüfte Verengung als Methode** (`n.toInt8(): ?int8` o. ä., Form Bereich 5) | zweiter Operator |
| T1e | `char` ist Unicode-Skalarwert, **keine Zahl** (keine Arithmetik, kein Vergleich mit Ganzzahlen); `bool` keine Zahl | Go `rune`, C |
| T1f | `int128`/`uint128`: Tür (clang/`zig cc` liefern `__int128`); `float16`/`bfloat16`: Tür; **kein `usize`** (C `size_t` ↔ `uint` an der FFI); `decimal` Bibliothek | — |

## T2 — Überlauf: **offen**
## T3 — Subtyping und Varianz: **entschieden** (2026-09-28) — Koerzion statt Subtyping

**Lyric 5 hat keine Subtyp-Beziehung zwischen deklarierten Typen; es hat eine feste Liste von
Koerzionen, die an Stellen mit Zieltyp gelten (Zuweisung, Argument, Rückgabe, Initializer-Feld)
und durch `?` hindurchreichen.** Keine deklarationsseitige Varianz (`out T`), keine Wildcards.

Warum die Darstellung es vorgibt: Varianz heißt Reinterpretieren ohne Umbau. `Circle[]` ist
ein Block aus Inline-Structs oder 8-Byte-Zeigern, `Shape[]` ein Block aus 16-Byte-Fat-Pointern
(V7, V10) — nie dasselbe. Rust und Go leben damit; Java/C# können Container-Varianz nur, weil
die VTable im Objekt-Header liegt, und C# nennt seine kovarianten Arrays einen Fehler.

| Koerzion | Status |
|---|---|
| `T → ?T` | behalten (§3.1) |
| `Typ → Interface` (Fat Pointer bauen, Struct boxen) | behalten |
| verlustfreie Ganzzahl-Weitung, `float32 → float64` | T1c |
| **`?Circle → ?Shape`, `Circle → ?Shape`** — Koerzion durch das Optional hindurch, neues Optional aus dem umgewandelten Wert (Optionals sind Werte, V5) | **neu**; der gemessene 4.x-Ablehnungsfall; Swift |
| Union-Einlegen (`int → int \| string`) | falls T9 — als Koerzion, nie als Subtyp-Gitter |
| Funktionstyp-Varianz | **nein** (bräuchte Thunks je Konversion — Swift zahlt sie, Rust hat sie nicht); eine Lambda ist der Weg |
| Vererbung `Sub → Super` | Bereich 4; ohne Vererbung keine |

Folgen: die **Inferenz bleibt Unifikation ohne Gitter** — kein LUB (`if (c) circle else
square` braucht die Annotation `Shape`), keine Varianzannotationen; T8 (bidirektional) bleibt
bezahlbar (Swift's Checker ist wegen Subtyping × Überladung × Literale berüchtigt). Die
Monomorphisierung bleibt eindeutig. **T9 muss diesem Grundsatz gehorchen.**

## T4 — Optionals: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| O1 | **`??T` ist erlaubt** (Rust `Option<Option<T>>`, Swift). Darstellung V5 hat den Platz; die konsistente Antwort auf Monomorphisierung: ein generisches `?T` ist ein Optional um das, was `T` ist — `first<T>(xs: T[]): ?T` mit `T = ?int` ist `??int`, `Map<K, ?V>.get(k): ??V` unterscheidet „fehlt" von „null", `Iterator<?T>` wird möglich. Ebenen werden über **Narrowing** erreicht, keine neue Syntax (kein `Some`) | Abflachung (Kotlin `String??` = `String?`), Verbot (C#, Lyric 4 — die vier gemessenen Löcher im generischen Rumpf sind die Kosten des Verbots) |
| O2 | **Ein bloßes `T` ist opak**: keine Null-Operation (`== null`, `??`, `!`, `match null`) auf einem Ausdruck vom Typ `T`; nur auf `?…`. Folgt aus „Constraints an der Deklaration geprüft"; schließt die vier Löcher mit einem Satz | Rust, Swift |
| O3 | Narrowing narrowt die **Bindung** um eine Ebene (`!= null` in `if`/`while`/`&&`/frühem `return`); ein danach zugewiesenes `var` verliert die Verengung; `if let`/`while let` bleiben | Kotlin, TypeScript, Lyric 4 |
| O4 | `?.`-Ketten **flachen ab** (`a?.b?.c` ist `?R`, Swift — das Ergebnis kann nur einmal fehlen); `??` rechtsassoziativ, lazy, schält eine Ebene; `!` Panik mit Position, schält eine Ebene; **keine Zuweisung durch `?.`** (Tür) | — |
| O5 | **Optional-Member über `extend<T> ?T { map, flatMap, orElse, filter, … }`** in der stdlib — `?T` ist ein Typkonstruktor, den generische Extends (T7) erreichen, wie `T[]` | Rust `impl<T> Option<T>` |
| O6 | `?T == ?T` über **bedingte Konformanz** (`Equatable` wenn `T` es ist; `null == null` true; `x == 5` mit `x: ?int` über Koerzion, T3); `Hashable` ebenso; **keine Ordnung auf `?T`** (Swift; Rust's `None < Some` überrascht mehr als es nützt) | — |

## T5 — `Self` und statische Interface-Member: **offen**
## T6 — Assoziierte Typen: **offen**
## T7 — Generische Extends, bedingte Konformanz in `extend`-Form: **offen**
## T8 — Inferenz: **offen**
## T9 — Vereinigungstypen gegen Enum ohne Zeremonie: **entschieden** (2026-09-28)

**Keine Vereinigungstypen `A | B` in 5.0.** Enum-Ergonomie stattdessen; die Union bleibt eine
**Tür, die rein additiv ist**: sie käme nach T3 nur als Koerzion (kein Subtyp-Gitter, kein LUB),
also verbaut nichts, was 5.0 entscheidet.

Was sie wäre: ein anonymes Enum (Tag + Union inline, V6), Eintritt nur an Stellen mit Zieltyp,
Austritt per Typtest — kommutativ, assoziativ, abflachend. Was sie kauft: Summen ohne
Deklaration an Signaturen, Rückgabe-Alternativen ohne Wrapper (`int | ParseError`), `?T` als
`T | null`.

| Warum nicht | |
|---|---|
| zweiter Summenmechanismus neben `enum` | jede Folgefrage (Synthese, Formatierung, Exhaustiveness, Serialisierung) zweimal |
| Abflachung in Generics | `f<T>(x: T \| string)` mit `T = string` — die Union verschwindet an der Instanz, in monomorphisiertem Code |
| `?T` als `T \| null` macht `??T` prinzipiell unmöglich | das Iterator-Ende könnte nicht `null` sein; TypeScript's `find`-Problem, Rust kann `Option<Option<T>>` |
| Überladung wächst um eine Dimension | `f(int)`, `f(string)`, Aufruf mit `int \| string` |
| rekursive Unionen brauchen parametrisierte rekursive Aliase (T15) | sonst ist `Json` nicht schreibbar |
| keine Sprache hat Unionen ohne Subtyping darunter | TypeScript, Scala 3, Crystal, Python alle mit Gitter; C#, Go, Rust, Swift, Kotlin, Java bewusst ohne Unionen |

**Enum-Ergonomie, zugesagt an Bereich 4/8**: Swift's **implizites Member bei bekanntem
Zieltyp** (`find(.Name("x"))`, `return .Missing;`, `match (id) { .Int(n) => … }`); Synthese von
`Equatable`/`Hashable`/`Display` (M10); Typ-Patterns auf Interface-Werten (T11) für offene
Typmengen; einzeilige Deklaration bleibt. Der `Json`-Fall ist ein rekursives Enum (Rust, Swift);
der `Result`-Fall gehört zu Bereich 5. `?T` bleibt ein eigener Typkonstruktor — T4 entscheidet
`??T` frei.

## T10 — `any`: **offen**
## T11 — Downcast und Typ-Patterns auf Interface-Werten: **offen**
## T12 — `inout`/`ref`-Parameter, `ref`-Rückgabe: **offen**
## T13 — Arrays und Ranges als Typen mit Membern, Views, Länge im Typ: **offen**
## T14 — Index-Familie: **offen**
## T15 — Aliase, `opaque type`, Newtype: **offen**
## T16 — Tupel: **offen**
## T17 — Funktionstypen: **offen**
## T18 — Typparameter-Hygiene: **offen**
## T19 — Grenzen der Monomorphisierung: **offen**
