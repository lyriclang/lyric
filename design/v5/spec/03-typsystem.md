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

## T2 — Überlauf: **entschieden** (2026-09-28) — Swift's Modell

**Überlauf ist eine Panik, in jedem Profil** (`+ - * / << >>`, unsigned Unterlauf; Division
durch 0 und `MIN / -1`; Shift um ≥ Breite — kein stilles Maskieren). Wrap, Sättigung und
Prüfung sind **explizit**: `a.wrappingAdd(b)`, `a.saturatingAdd(b)`, `a.checkedAdd(b): ?int`,
dazu Wrap-Operatoren (`&+` Swift / `+%` Zig — Schreibweise Bereich 8). Ein Profil-Schalter, der
die Prüfung entfernt, nur **ausdrücklich, nie stillschweigend** (wie Fast-Math, L10). `as`
bleibt wrappend (T1d); Float kennt keinen Überlauf (IEEE ∞). Emission über
`__builtin_add_overflow` + `unlikely`-Branch (signed overflow ist in C undefiniert — ohnehin
eine Wahl). **Kein `checked {}`-Block** (zweiter Mechanismus).

Verworfen: immer wrap (Go, Java, C# — stille falsche Zahl); Debug-Panik/Release-wrap (Rust —
dasselbe Programm rechnet je Profil anders, gegen L10); Block/Profil als Hauptform.

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

## T5 — `Self` und statische Interface-Member: **entschieden** (2026-09-28)

- **`Self`** ist der konformierende Typ innerhalb eines Interfaces (Rust, Swift). Unter
  Monomorphisierung ist ein Constraint-Aufruf ein direkter Aufruf; `Self` kostet nichts.
- **Objektsicherheitsregel**: ein Interface mit `Self` außerhalb der Receiver-Position oder mit
  statischen Membern ist **nur als Constraint** nutzbar, nicht als Wert; der Compiler sagt es an
  der Verwendungsstelle („benutze es als Constraint"). Rust „dyn-compatible", Swift.
- **Statische Member: ja** — `static fn parse(s: string): ?Self`, `static fn default(): Self`,
  `static let ZERO: Self` (assoziierte Konstante); nur durch einen Constraint aufrufbar
  (`T.parse(s)`). C# 11 „static abstract members" für generische Mathematik.
- **Bibliotheksaufteilung**: `Equatable`, `Hashable`, `Ordered`, `Display`, `Default`, `Parse`,
  `Clone` über `Self` (homogen); `Add<Rhs>`, `Mul<…>`, `Index<K>`, `Into<T>` mit Typparametern
  (heterogen by design), mit Default-Typargumenten (T18) als `Add<Rhs = Self>`. Die
  Mehrfachkonformanz `Equatable<Tag>, Equatable<int>` entfällt; heterogene Gleichheit wäre ein
  eigenes `EquatableWith<T>`.
- Verworfen: Parameter-Form `Equatable<T>` (C#, Java, Kotlin, Lyric 4 — jeder Typ nennt sich
  zweimal, und die Objektsicherheit war der einzige Grund dagegen).

## T6 — Assoziierte Typen: **entschieden** (2026-09-28)

**Assoziierte Typen kommen als neue Interface-Member-Art hinzu (`type Item;`), neben
Typparametern — nichts wird ersetzt, der Entwickler wählt je Interface, auch gemischt.**

| | Typparameter `Interface<T>` | Assoziierter Typ `type Item` |
|---|---|---|
| Wer wählt | Aufrufer / Konformanz-Deklaration (**Eingabe**) | der konformierende Typ, einmal (**Ausgabe**) |
| Mehrfachkonformanz | ja, sinnvoll (`Vec2 :: [Add<Vec2>, Add<float>]`) | nein — genau eine je Typ, Aufrufe eindeutig |
| Generischer Code | `T` muss von außen kommen oder aus Konformanzen gesucht werden | **`I.Item` wird vom Typ abgelesen** — `collect<I :: [Iterator]>(it: I): I.Item[]`; Iterator-Ketten inferieren ohne Annotation (Rust), was M33 in Lyric 4 per Konvention nachbauen musste |
| Fixierung | — | `Iterator<Item = int>` als Interface-Wert (Fat Pointer muss `next()` typisieren; Rust `dyn Iterator<Item = i32>`) und als Constraint mit Bedingung |
| Beide zusammen | `interface Index<K> { type Output; fn get(k: K): Output }` — Schlüssel Eingabe, Ergebnis je Schlüsseltyp Ausgabe | |

**Richtlinie für die Standardbibliothek** (kein Compilerzwang): Eingaben als Parameter (`Add<Rhs>`,
`Index<K>`, `Into<T>`, `From<T>`), Ausgaben assoziiert (`Iterator.Item`, `Iterable.Iter`,
`Index<K>.Output`, `Add<Rhs>.Out`). Wer `Iterator<T>` als Parameter schreibt, darf das und
verliert die Ketteninferenz — der Compiler sagt es an der Aufrufstelle. Preis: `type Item;`,
Typpfade `I.Item`, die `Item =`-Schreibweise. C#/Java haben nur Parameter und leben mit
`IEnumerable<T>`-Mehrdeutigkeit.


## T7 — Generische Extends, bedingte Konformanz, Kohärenz: **entschieden** (2026-09-28)

| # | Regel | Vorbild |
|---|---|---|
| X1 | **`extend<T> List<T> { … }`**, mit Constraints: `extend<T :: [Display]> List<T> :: [Display] { … }` — die `extend`-Form bedingter Konformanz (die deklarationsseitige existiert seit 4.x) | Rust `impl<T: Display> Display for Vec<T>`, Swift `extension … where` |
| X2 | **Extends auf eingebauten Konstruktoren**: `extend<T> T[]`, `extend<T> ?T`, `extend<T> Range<T>`, Tupel fester Arität — der Mechanismus, der Arrays, Optionals, Ranges ihre Member gibt (T13, O5) | Rust `impl<T> [T]`, `impl<T> Option<T>` |
| X3 | **Kohärenz whole-program**: je (Typinstanz, Interface) genau eine Konformanz; Duplikat = Fehler an beiden Stellen. **Keine Orphan-Regel** — Rust braucht sie wegen getrennter Kompilierung, wir kompilieren whole-program (L7); ein Extend darf in jedem Modul stehen | — |
| X4 | **Keine Spezialisierung**: überlappende generische Extends sind ein Fehler | Rust (Spezialisierung seit Jahren instabil) |
| X5 | Extends fügen Methoden und Konformanzen hinzu, **keine Felder** (Layout fix) | alle |
| X6 | Sichtbarkeit von Extend-Membern wie Modulmember (Bereich 7); sichtbar, wo das Modul importiert ist | Rust, Swift, Kotlin |

## T8 — Inferenz: **entschieden** (2026-09-28) — bidirektional je Statement

Die Klasse von C#, Kotlin, Swift: der **erwartete Typ fließt von außen nach innen**, Typen von
innen nach außen, Unifikation innerhalb eines Statements. **Keine Whole-Function-Inferenz**
(Rust, OCaml): sie kauft nur `let v = List.new(); v.add(1)` und zahlt mit nichtlokalen Fehlern
und einer Interaktion mit Überladung, die keine Sprache mit beidem gut gelöst hat. Schnell,
weil ohne Subtyping (T3) — Swift's Langsamkeit kommt aus Subtyping × Überladung × Literale.

| Position | Regel |
|---|---|
| Zuweisung, Argument, Rückgabe, Initializer-/`with`-Feld | Typparameter aus dem erwarteten Typ: `let xs: int[] = zero();` |
| Literale | adaptieren (T1b); ohne Kontext `int`/`float` |
| `null`, `[]`, `.Member` | Typ aus dem Kontext; ohne Kontext Fehler mit Vorschlag |
| Lambda | Parameter aus dem erwarteten Funktionstyp, Rückgabe aus dem Rumpf; ohne erwarteten Typ Parameter annotieren |
| `if`/`match`-Arme | jeder Arm koerziert zum erwarteten Typ; ohne erwarteten Typ bestimmt der erste Arm, kein LUB |
| Generische Argumente | **Unifikation innerhalb des Aufrufs** statt „erste Bindung gewinnt": `same(1, s)` → `T = string`, Fehler am Literal |
| Koerzion | nach der Unifikation, nie darin (T1c, T3) |
| nicht inferiert | ein Typparameter ohne Vorkommen in Argumenten oder erwartetem Typ; eine Variable aus späterer Verwendung |

Kleinigkeiten: `_` als Platzhalter in Typargumenten (`collect<_, List<int>>(it)`, Rust);
partielle Listen nur über `_`; explizite Typargumente bleiben; **Rückgabetypen immer
annotiert** (Signatur ist der Vertrag, auch für Fehlermeldungen und den Cache); `var x: int;`
mit Definite Assignment bleibt, **`var x;` ohne Typ nein**.

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

## T10 — `Any`: **entschieden** (2026-09-28)

**`Any` ist das leere Interface in `std.core`** — kein Feature, ein Name. `let a: Any = 5` ist
dieselbe Koerzion wie zu jedem Interface (Wert geboxt, V7); heraus über `is`/Typ-Pattern (T11);
`Any[]` ist das heterogene Array. Nichts konvertiert unaufgefordert dorthin — die Koerzion
greift nur, wo der Zieltyp `Any` ist. Guide: „meist willst du ein Enum oder ein Interface".

## T11 — Downcast und Typ-Patterns: **entschieden** (2026-09-28)

Billig durch V4/V7 (Typtest = Deskriptorvergleich; Downcast liefert bei Klassen die Referenz,
bei geboxten Structs eine Kopie).

| Form | Regel | Vorbild |
|---|---|---|
| Typ-Pattern im `match` | `match (shape) { c: Circle => …, r: Rect => …, _ => … }` | C#, Swift, Kotlin |
| `is` mit Narrowing | `if (shape is Circle) { shape.r }` — Bindung wird im Zweig zum konkreten Typ (Smart Cast), Regel wie O3 | Kotlin, C#, TypeScript |
| `as?` | **nein** — `as` ist die bit-nahe Konversion (T1d); `is` + Narrowing deckt den Fall | — |
| Interface → Interface | `x is Display` über die Konformanzliste im Deskriptor, lineare Suche | Go |
| Erschöpfung | Typ-Pattern-`match` braucht `_` (offene Menge) — außer bei **versiegelten** Interfaces (`sealed`, alle Konformer im Modul): **Bereich-4-Posten**, vorgemerkt | Kotlin, Java 17 |
| Bloßes `T` | **kein Typtest** (O2); dynamische Typisierung nimmt `Any` als Parametertyp | Rust |

## T12 — `inout`-Parameter: **entschieden** (2026-09-28); Keyword in Bereich 8

**Ein Parameter-Übergabemodus, kein Referenztyp** (Swift `inout`, Pascal `var`-Parameter):
der Parameter kommt per Ort (ein Zeiger, S1) und darf geschrieben werden. `mut fn` ist der
Sonderfall für den Receiver (`fn f(inout this: Self)`); `inout` ist dasselbe für jeden
Parameter — freie Funktionen (`swap`), Skalare (`increment(inout n: int)`), ein Nicht-`mut`-
Methode, die einen *anderen* Ort schreibt (`multiplyInto(other, inout target)`).

| Regel | |
|---|---|
| Argument | ein **`var`-Ort** nach M3 (Local, `var`-Feld unter `var`-Wurzel, Array-Element); kein Temporary, kein `let` |
| Aufrufstelle | **markiert** (Arbeitsnotation `&x`), damit die Mutation sichtbar ist (Swift, C#) |
| Entkommen | keine Closure fängt es (C3); nicht speicherbar (kein Referenztyp als Wert); Yield währenddessen harmlos (Aufrufer wartet auf demselben Stack) |
| Aliasing (`swap(&a, &a)`) | erlaubt, Wirkung undefiniert, dokumentiert — keine Exklusivitätsprüfung (Linie von M5) |
| Klassen-Parameter | `inout` biegt die *Variable* des Aufrufers um; erlaubt, selten |
| `mut fn` | darf `this` als `inout` weiterreichen (`normalize(&this)`); ein gewöhnliches `fn` nicht |
| C# `out`, `in` | **nein** (Rückgabe/Tupel; große Structs gehen ohnehin per Zeiger) |
| `ref`-Rückgabe (Ort im Backing-Speicher, statement-gebunden) | **Tür**, nicht 5.0; Get/Set-Rückschreibung (M4) deckt `list[0].x = 1` |

**Schreibweise — Bereich 8 (Y1, entschieden)**: **kein Wort, ein Zeichen** — Parameter `x: &T`,
Aufruf `swap(&a, &b)` (C++/Rust/C-Gewohnheit; Präfix-`&` war frei). `inout` (Swift) und `ref`
(C#) verworfen; `mut`/`var` als Parameterwort schieden ohnehin aus (Rust `mut x` = veränderliche
lokale *Kopie*).

## T13 — Arrays, Views, Ranges, `T[N]`: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| A1 | **`T[]`** bleibt Heap-Objekt, feste Länge, Referenz (V10); Member aus der stdlib über `extend<T> T[]` (X2) — nur `length` und Index sind Primitive | Compiler-Sonderfälle je Methode |
| A2 | **View-Typ `Slice<T>`** (Arbeitsname) in `std.core`: Struct {Innenzeiger, Länge}, 16 B, kopierbar, **teilt die Elemente** (schreibt durch); `arr[a..b]`, `arr[..b]`, `arr[a..]`, `arr[..]` erzeugen ihn ohne Kopie; die stdlib schreibt Member einmal auf `Slice<T>`, `T[]` gibt einen View von sich. Kein Lebensdauermodell: der GC hält den Block (L1). **`str`** (Arbeitsname) als unveränderlicher View auf UTF-8; `s[a..b]` mit **Byte-Indizes und Panik an Nicht-Zeichengrenzen** (Rust); kein `s[i]` für Zeichen, `s.chars()` ist der Iterator. Das Array-Pattern `[first, ..rest]` bindet `rest` als View (Bereich 8) | Go-Slices als Primitiv (Aliasing ohne Marker, `append`), Swift `ArraySlice` (CoW, Original-Indizes), C# `Span` (Escape-Regeln), Kopien (Java) |
| A3 | **Ranges als Typ je Form**: `Range` (`a..b`), `RangeInclusive` (`a..=b`), `RangeFrom` (`a..`), `RangeTo` (`..b`), `RangeFull` (`..`) — Structs in `std.core`; `Range`/`RangeInclusive` sind `Iterator`; `for (i in ..5)` ist ein Übersetzungsfehler; `for (i in a..b)` mit Range-Literal wird zur **Zählschleife** ohne Objekt (Rust). Die 4.x-Regel „Ranges sind keine Werte" fällt | ein Typ mit `inclusive`-Flag (Überlauf bei `0..=MAX` als halboffen, Laufzeitprüfung statt Typfehler) |
| A4 | **`T[N]` inline**: Werttyp mit `N` Elementen inline — `struct Mat4 { m: float[16] }` ohne Heap-Objekt, direkt ein C-Array an der FFI; `N` in 5.0 **nur Literal**, Generizität über `N` (`const N`) ist Tür (T18); mehrdimensional inline `float[4][4]`, `T[][]` bleibt gezackt. **View auf `T[N]` nur heap-resident** (Feld eines Objekts, Element eines `T[]`) — ein View auf ein Stack-Local dürfte den Frame nicht überleben; Stack-`T[N]` wird `inout` weitergereicht oder kopiert | Rust `[T; N]`, Go, Zig, Swift `InlineArray` |

## T14 — Index-Familie: **entschieden** (2026-09-28)

| # | Regel | Vorbild |
|---|---|---|
| N1 | `interface Index<K> { type Output; fn get(k: K): Output }`, `interface IndexSet<K> { fn set(k: K, v: …) }`; `x[k]` = `get`, `x[k] = v` = `set`; `x[k].f = v`/`x[k].bump()` über Get/Set-Rückschreibung (M4), ohne `IndexSet` ein Fehler | Rust, Swift, C# |
| N2 | `T[]`, `Slice<T>`: `Index<int>` → `T`, `Index<Range…>` → `Slice<T>`, `IndexSet<int>`; `string`/`str`: `Index<Range…>` → `str` | — |
| N3 | `Map<K, V>`: `Index<K>` mit `Output = ?V` (`m[k]` liefert `?V`, wirft nicht); `IndexSet<K>` | Kotlin |
| N4 | **Index-Typ `int`**; kleinere Ganzzahltypen weiten (T1c), `uint64` braucht `as`; negativ = Panik | Swift, C#, Kotlin |
| N5 | **Bereichsprüfung immer**, Panik, in 5.0 nicht abschaltbar (Go); `unchecked`-Profil als Tür | — |
| N6 | **Vom-Ende-Index als Klammer-Zucker**: `^n` **nur innerhalb von `[…]`**, statisch ersetzt durch `<Ausdruck>.length - n` — `xs[^1]`, `line[..^1]`, `buf[^4..]`; verlangt `length: int` am indizierten Ausdruck; `^0` als Bereichsende gültig, als Einzelindex Panik; **kein `Index`-Typ**, keine Konformanz, keine Laufzeitdarstellung. Bereich 8 bestätigt das Präfix-`^` neben XOR | C# (`Index`-Struct, verworfen als zu schwer); Python negative Indizes (verstecken Bugs, verworfen) |

## T15 — Aliase, `opaque type`, Newtype: **entschieden** (2026-09-28)

- `type Name = …` bleibt transparent, **parametrisiert erlaubt** (`type Pair<T> = (T, T)`,
  `type Handler = fn(Event) -> void throws IoError`) — Rust, Swift.
- **`opaque type` entfällt.** Ein Ein-Feld-Struct `struct Meters { v: float }` hat unter V2
  exakt das Layout von `float` (C garantiert es, auch an der FFI); Synthese gibt
  `Equatable`/`Ordered`/`Display` per `:: […]`; `with` und Feldzugriff ersetzen das
  Doppel-`as`. Rusts Newtype-Muster; Handles an der Host-Grenze werden Ein-Feld-Structs.

## T16 — Tupel: **entschieden** (2026-09-28)

Benannte Elemente `(x: int, y: int)` mit Zugriff `.x` **und** `.0`; **Namen sind
Übersetzungszeit-Etiketten**, `(x: int, y: int)` und `(int, int)` sind derselbe Typ (C#; Swift
macht Labels zum Typ und zahlt mit Konversionsregeln). Keine Arität 1, kein leeres Tupel (`void`
bleibt). Patterns/Destructuring bleiben. Gleichheit/Hash/Display bedingt synthetisiert.

## T17 — Funktionstypen: **entschieden** (2026-09-28)

`fn(A, B) -> R` bleibt, Closures und freie Funktionen einheitlich (V8). **`throws` im
Funktionstyp** (`fn(int) -> int throws ParseError`; ohne `throws` wirft er nichts) — Fehlerform
Bereich 5. Instanziierte generische Funktion als Wert **ja** (`map(ident<int>)`, ein
Funktionszeiger nach Monomorphisierung); die uninstanziierte bleibt verboten (Rust). Methode als
Wert (`obj.method`) ergibt eine Closure, die `obj` fängt (C2). Keine Varianz (T3), keine
benannten Parameter im Typ.

## T18 — Typparameter-Hygiene: **entschieden** (2026-09-28)

| | Entscheidung |
|---|---|
| Phantom-Parameter | **erlaubt, ohne Warnung** — typisierte Handles (`Id<User>`); Rust verbietet es nur wegen Varianzinferenz |
| `<T :: [int]>`, `<T :: [Plain]>` | **Fehler**: ein Constraint nennt ein Interface |
| doppelter Constraint | Fehler |
| **Default-Typargumente** | ja — `interface Add<Rhs = Self>`, `class Map<K, V, H = DefaultHasher>` (Rust, C++) |
| partielle Listen | über `_` (T8) |
| Wertparameter | 5.0 nur Literale in `T[N]` (T13); `const N` als Generik: Tür |
| höhere Kinds | nein; `FromIterator`-artige Interfaces decken den Fall |

## T19 — Grenzen der Monomorphisierung: **entschieden** (2026-09-28)

Divergenz: **immer Diagnose, nie Absturz** — Tiefenlimit je Instanziierungskette (Rust 128),
Meldung nennt die Kette; Instanzlimit mit klarer Meldung; Instanzen whole-program einmal (C3),
Sharing über Referenztypen als Optimierer-Tür (Bereich 11); der Cache (L7) hält Instanzen über
Builds; der 4.x-Verifier-Anteil (Hälfte der ~0.5 ms je Instanz) entfällt unter C-Emission.

---

**Bereich 3 ist damit vollständig entschieden** (T1–T19, 2026-09-28).
