# 10 — Standardbibliothek

Lebendes Dokument des Bereichs 10. Fragen B1–B13, je **entschieden** oder **offen**. Basis:
`../stdlib.md` (35 Fragen, Fassung 2), `../collections.md` (41 Fragen), `../../stdlib-2.md`
(Zielbild 4.5/5.0), `stdlib/std/*.lyr` (4.6), Spec §11 — und die Entscheidungen der Bereiche
0–9, die einen großen Teil des Dossiers bereits beantwortet haben (Abschnitt „Schon
entschieden").

## Bestandsaufnahme Lyric 4

Aus `../stdlib.md` §1 (gelesen und gemessen auf 4.6):

- **Umfang**: 25 Module, 8 403 Zeilen Lyric, 723 Funktionen, davon 151 rumpflos (nativ), 138
  Registrierungen in 2 842 Zeilen `NativeRegistry.cs`; 17 Interfaces, **kein** I/O-Interface;
  37 `OrThrow`-Zwillinge, 5 `OrErr`-Formen; 7 `Display`-Konformanzen (nicht `List`, `Map`,
  `Set`, `JsonValue`, `Result`, die Fehlertypen).
- **Module**: `core, result, option, string, fmt, math, collections, iter, hash, random, time,
  test, task, json, encoding, bytes, io.path, io.file, io.console, io.error, io.stream, io.net,
  os, process, build`.
- **Befunde** (a–p): `println([1,2,3])` stirbt **in der Bibliothek** (IR0001 in `console.lyr`);
  `abs(-3)` ist still `float`; 13 Zahlentypen, 10 davon ohne jede Konformanz, `uint` ohne
  `Display`; `std.build` von jedem Programm importierbar und stirbt erst zur Laufzeit;
  Duration-Rechnung kostet `osAccess`; Methoden Unicode, freie Prädikate ASCII, `ß` bleibt `ß`;
  vier Module tragen je ihr eigenes `lastErrorKind`-Ritual; `combineHash`/`hashCombine`,
  `std.os.now*` neben `std.time`, drei `slice`; drei Aufrufstile nebeneinander (frei, Methode,
  freie Zwillinge) — der genannte Grund („Methode kann keinen eigenen Typparameter haben") ist
  seit 4.6 falsch; Container weder vergleichbar noch anzeigbar; `Map` nicht `Iterable`;
  Deprecation-Uhren laufen nicht (kein Unterdrücker); Abhängigkeiten sind lokale Pfade;
  Native-Namensvertrag ungeschrieben.
- **Grenzkosten gemessen**: 14 ns/Byte an der Native-Grenze, 187 ns/Element in der Lyric-
  Schleife, 16 Byte pro `uint8` im Heap.
- **Die Grundsatzfrage** (Bibliotheks-Umkehr: Regex, HTTP, TLS, Zeitzonen, Kompression, Krypto)
  ist in 4.x unentschieden; `stdlib-2.md` sagt „draußen", `lyric-v5-features.md` „drinnen".

## Schon entschieden (Bereiche 0–9) — wird hier nicht neu verhandelt

| Dossier | Antwort | Wo |
|---|---|---|
| SL-02 Paketmanager | Pfad + Git, MVS, `lyric.lock`, keine Registry in 5.0 | 07 P2–P8 |
| SL-04 drei Namen pro Operation | **eine werfende Funktion**; `try?` → `?T`, `try!` → Panik, `Result` als Wert mit Brücken | 05 E1/E4 |
| SL-05 `lastErrorKind` | entfällt: Fehler-ABI über versteckten Slot, keine Natives mit Seitenkanal | 01 E1–E8 |
| SL-08 `Display` für Container | generische Extends mit Constraint (`extend<T :: [Display]> T[] :: [Display]`) | 03 X1 |
| SL-11/13/30 Capabilities, Natives, comptime-I/O | Sandbox geht an Lyric-Script; „nativ" = dünne C-Schicht; `embed("pfad")` | 00 Z3, 01 L9, 09 Q9 |
| SL-17 Unterdrücker | `@Allow(code)` an Deklaration/Modul + `[lints]` im Manifest | 09 A10 |
| SL-20 Handles | `opaque type` entfällt; Handle = Ein-Feld-Struct, konformiert frei | 03 T15 |
| SL-21 Orphan-Regel | keine (Kohärenz whole-program) | 03 T-Kohärenz |
| SL-22 (Wurzel) | `interface Error { message, cause }` Wurzel aller werfbaren Typen | 05 O1 |
| SL-24 Async und `Reader` | Warten = park/unpark, Datei-I/O über Pool + notify, keine blockierenden Natives | 06 S3/S4 |
| SL-25 `uint8[]`-Kosten | Arrays inline (1 Byte je `uint8`), `Slice<T>`-Views ohne Kopie | 01 V-Reihe, 03 A2 |
| SL-26 `float` und `Hashable` | `float`: `Equatable`, `Ordered`, weder `TotalOrder` noch `Hashable` | 04 D6 |
| SL-27/34 Native-Namensvertrag, Versionsprüfung | gegenstandslos: kein Bytecode, Quelle ist das Format | 01 L7 |
| SL-28 `lyrfix` | Bereich 12; `@Deprecated.replacement` speist `lyric fix` | 09 A11, 11/12 |
| SL-29 `std.build` erkennen | Bereich 11 (`build.lyr` ja/wie) | — |
| SL-31 Nesting-Regel | `Debug` automatisch für jeden Typ, `Display` nie; `{x:?}` | 04 D7, 08 Y7 |
| SL-32 `assertThrows` | Lambdas tragen inferiertes `throws` → Lambda-Form ist baubar | 05 K3 |
| SL-06 Typsuffixe → Überladung | **Überladung nur nach Arität** (04 D4) — `abs(int)`/`abs(float)` gibt es nicht; die Antwort muss generisch sein (B5) | 04 D4 |
| COL-* Slices, Ranges, Index, Iterator-Ende | `Slice<T>`/`StringView` als Views, Range-Typen als Structs, `Index<K>{type Output}`, `next(): ?Item`, `??T` erlaubt | 02 M-, 03 A/N/O |
| Operator-Interfaces, Synthese | `Add<Rhs=Self>{type Out}`, `Equatable`/`Hashable`/`Ordered`/`TotalOrder`/`Display`/`Debug`, Konformanz ohne Körper | 04 D6/D7 |
| Prelude | Modul `std.prelude`, Liste hier (B2) | 07 I8 |
| Tests | im Paket (`@Test` neben dem Code oder `tests/`-Root) | 07 V4 |

## Fragen

Reihenfolge: **B1 zuerst** (sie entscheidet, was es überhaupt gibt), dann Schnitt/Namen (B2–B3),
dann die Kernverträge (B4–B6), dann die Fachmodule (B7–B12), zuletzt `std.meta`/`std.syntax` (B13).

## B1 — Umfang und Ringe (Bibliotheks-Umkehr): **entschieden** (2026-09-29)

SL-01, SL-16, SL-18. **Regel D mit Trägerklausel, ein Ring in 5.0, der zweite als Tür.**

| # | Entscheidung | Vorbild |
|---|---|---|
| U1 | **Inhaltsregel D**: in `std` kommt, was eine **externe Norm** vorgibt (RFC, IANA, Unicode, ECMA-48, TOML-Spec); draußen bleibt, was **API-Geschmack** ist (Arg-Parser-Form, Logging-API, ORM, Template) | Go implizit; gegen Python (drei Arg-Parser, PEP 594) |
| U2 | **Trägerklausel**: rein kommt nur, was `std` selbst in Lyric plus dünner C-Schicht (L9) trägt — **keine Systembibliothek als Abhängigkeit** von `std`. Folge: TLS, Zonendaten, Ciphers sind Pakete | Rust `hyper`/`rustls`-Trennung; gegen Zig (eigenes TLS, nur Client) |
| U3 | **Ein Ring** in 5.0: `std` liegt bei der Toolchain, ist mit ihr versioniert (Quelle ist das Format, L7); Reachability (L11) macht Größe laufzeitkostenfrei — der Preis von D ist Pflege, nicht Binary | Rust, Go |
| U4 | **Zweiter Ring = Tür**: Pakete im Org mit eigener Version über Pfad/Git (07 P2) — kein Mechanismus, bis er existiert | Swift `swift-*`, Kotlin `kotlinx-*`, Deno `@std` |
| U5 | **Prelude ohne I/O**: `print`/`println`/`eprint`/`eprintln` liegen in `std.io` (`import std.io { println }`); das Prelude trägt, was Signaturen brauchen, nicht Wirkungen | Go `fmt`, C# `Console`, Zig; gegen Rust/Python/Swift |

**`std` in 5.0 — 35 Module** (Namen vorläufig, Schnitt B2):

| Gruppe | Module |
|---|---|
| Kern | `prelude`, `core` (Kern-Interfaces, `Num`-Familie, `Error`, `Result`, `Box`, `Slice`, `StringView`, Ranges, Member von `T[]`/`Slice`/`?T`, Art-2-Attribute — `result`/`option` gehen auf), `iter`, `collections`, `string`, `fmt`, `math` |
| Daten/Kodierung | `codec` (Encode/Decode-Paar, B10), `json` (RFC 8259), **`toml`** (Manifest), `encoding` (Base64/Hex/UTF-16/LE-BE; `bytes` geht auf), **`compress`** (RFC 1950–1952), **`regex`** (RE2-Syntax, lineare Zeit), `hash` (`Hasher`, SipHash, FNV, CRC32), `crypto` (SHA-2/SHA-1/MD5, HMAC, `secureRandom`; Ed25519/ChaCha20-Poly1305 Tür), `random` (PCG), `time` (RFC 3339, TZif-Parser RFC 8536 über die System-Zonendatenbank) |
| I/O und System | `io` (`Reader`/`Writer`/`Seek`, Puffer-/Text-Hüllen, `IoError`, `copy`, Speicherströme, `stdin/stdout/stderr`, `print`-Familie, `readLine`, `isInteractive` — `io.stream`/`io.error`/`io.console` gehen auf), `fs`, `path`, `net`, **`uri`** (RFC 3986), **`http`** (HTTP/1.1 Client **und** Server über `Reader`+`Writer`; kein TLS, HTTP/2 Tür), `os` (ohne Zeit), `process`, **`term`** (ECMA-48, TTY-Erkennung; Raw-Mode Tür) |
| Nebenläufigkeit (06) | `task`, `sync`, `thread` |
| Werkzeug-Seite | `test`, `meta`, `syntax`, `build` (Bereich 11), `ffi` (Bereich 11) |

**Ring** (Pakete im Org): `cli`, `log`, `tls` (Systembibliothek per FFI), `tzdata`, `unicode`
(Normalisierung, Grapheme, Konsolenbreite), `crypto.cipher` (AES, RSA), `proptest`/`snapshot`,
HTTP/2. **Bewusst nein**: BigInt (Tür), GUI, ORM/Template, XML/CSV (per D zulässig, ohne
Bedarf — Tür). **Fällt gegenüber 4.x**: `result`/`option`/`bytes`/`io.*`-Untermodule als
eigene Module, Zeit in `os`, `OrThrow`/`OrErr`-Zwillinge, `lastError*`, Typsuffixe, freie
Terminatoren, `LineReader`, `std.build` als Programmimport.

Verworfen: A (Rust-klein — ohne Registry zu karg), B ohne Regel (Go/Python — Halde), zweiter
Ring jetzt (doppelte Release-Mechanik für einen Maintainer), `extern`-Hüllen als Batterien
(Sandbox-Frage entfällt, Trägerklausel bleibt).

## B2 — Modulschnitt, Prelude, Namenskonventionen: **entschieden** (2026-09-29)

**Modulschnitt: flach.** `std.fs`, `std.path`, `std.net`, `std.io` statt `std.io.*` — die
4.x-Verschachtelung bildete die Capability-Tabelle ab, die es nicht mehr gibt. Höchstens zwei
Ebenen, nur für echte Unterräume (`std.crypto.cipher` als Ring-Tür). Rust/Go; gegen Python
(`os.path` → `pathlib`).

**Der String-View heißt `StringView`** (A2-Arbeitsname `str` verworfen: verstößt gegen N1 und
ist Pythons Name für den String selbst; `Substring` liest sich als Parametertyp falsch, `Str`
bleibt ein Ratespiel neben `string`). `string` koerziert zu `StringView` an Koerzionsstellen
(T3), wie `T[]` zu `Slice<T>`; gewöhnliche Funktionen nehmen `string` (ein Wort), der View
erscheint, wo geschnitten wird (Tokenizer, Parser, `split`/`lines`) — C++ `string_view`.

**Prelude** (`std.prelude`, alles `pub import` aus `std.core`; Verdecken = Warnung, I8):

| Gruppe | Namen |
|---|---|
| Funktionen | `panic`, `assert`, `unreachable`, `todo`, `same` |
| Typen | `Error`, `Result`, `Box`, `Slice`, `StringView`, `Range`/`RangeInclusive`/`RangeFrom`/`RangeTo`/`RangeFull`, `Ordering`, `List`, `Map`, `Set` (Sammlungsvokabular der Sprache; Rust hat `Vec`, nicht `HashMap` — wir alle drei) |
| Interfaces | `Equatable`, `Hashable`, `Ordered`, `TotalOrder`, `Display`, `Debug`, `Default`, `Clone`, `Iterator`, `Iterable`, `FromIterator`, `Into`, `From`, `Index`, `IndexSet`, `Resource`, `Num`, `Integer`, `Float` |
| Attribute | die geschlossene Art-2-Liste (09 A11): `@Test`, `@Deprecated`, `@Allow`, `@Inline`-Familie, `@MustUse`, … |
| **nicht** | `print`-Familie (U5), Operator-Interfaces `Add`…`Not` (Rust `std::ops`), `spawn`/`Task`, `min`/`max` |

**Namensgesetz** (normativ in Spec §11; Grundlage `stdlib-2.md` §2):

| # | Regel | Beispiel |
|---|---|---|
| N1 | Typen PascalCase (Builtins `int`, `string`, … ausgenommen); Funktionen, Methoden, Felder camelCase; Module klein und kurz; Konstanten sind statische Member — kein `intMax` | `int.max`, `float.epsilon` |
| N2 | Funktionen Verben oder Verb-Objekt; Prädikate `is`/`has`/`contains`/`can`; **Felder Substantive, Methoden Verben** (löst SL-35 bei einem Namensraum) | `int.parse`, `isBlank`, `containsKey` |
| N3 | Konstruktoren auf dem Typ: `new` (Zucker `Point(1, 2)`), `empty()`, `withCapacity(n)`, `of(Elemente)`, `from(andere Darstellung)`, `ofEinheit(x)` | `List.of([1, 2])`, `Map.from(pairs)`, `Duration.ofSeconds(5)` |
| N4 | `toX()` materialisiert/kopiert, `asX()` reinterpretiert in O(1), `into` nur Operator-Anker | `toList()`, `asBytes()` |
| N5 | Plural für Sammlungen, Singular für Elemente | `keys()`, `first()` |
| N6 | **Keine Antwortform im Namen**: kein `OrThrow`/`OrNull`/`tryX`/`OrErr`; die einzige benannte Form ist die werfende, die Form wählt der Aufrufer (`try?`, `try!`, `Result.of`); stille Reste regelt B3 | `try? fs.text(p)` |
| N7 | **Keine Typsuffixe**: generisch über `Num` (B5) | `abs(x)`, `sum()` |
| N8 | Kein `get`-Präfix fürs Lesen; `get(k)` nur fürs Nachschlagen; **`length()` überall mit Klammern**, auch auf `T[]`/`Slice`/`StringView` (Intrinsic, Rust `len()`) | `xs.length()`, `m.get(k)` |
| N9 | Mutation heißt, was sie tut (`mut fn`), gibt `void` oder das Entfernte; Kopie trägt Partizip/`to` | `sort()`/`sorted()`, `reverse()`/`toReversed()` |
| N10 | Boolesche Parameter: Enum oder zwei Funktionen bevorzugt; mit benanntem Argument (F5) toleriert, nie positional | `trimStart()`, nicht `trim(true)` |
| N11 | Ein Suffix beschreibt die **Ausgabe**, nie die Eingabe | `sha256Hex(bytes)`; Text über `s.asBytes()` |
| N12 | Fehlertypen enden auf `Error`, Gründe als `XErrorKind` im selben Modul; Options-Structs mit Defaults statt Builder | `IoError { kind, path, detail }`, `Command { program, args, cwd = "" }` |
| N13 | Assertions `(actual, expected)` | `assertEq(got, 4)` |

**Ein Aufrufstil**: **Methode, wenn es einen Empfänger gibt** — die Operation gehört dem Typ
ihres ersten Arguments; frei nur bei gleichrangigen Operanden (`copy(r, w)`, `min(a, b)`) oder
ohne Empfänger (`repeat(x)`, `once(x)`). Terminatoren mit Constraint sind Methoden über
bedingte Extends (`extend<T :: [Num]> Iterator<Item = T> { fn sum(): T }`, X1) — der
4.x-Grund für freie Zwillinge ist weg; keine `listX`/`mapList`-Namen. Kotlin/Swift; gegen Go
(`slices.Sort(xs)`).

## B3 — Antwortformen und Panik-Regel: **entschieden** (2026-09-29)

Nach 05 E1 gibt es je Operation **eine** werfende Funktion (N6). Die Formen:

| Form | Frage | bleibt für | Vorbild |
|---|---|---|---|
| `throws E` | „Warum nicht?" | **jede Operation mit einem Grund**: Datei, Netz, Prozess, JSON, Encoding, **Parse** | Swift |
| `?T` | „Gibt es einen Wert?" | **nur Nachschlagen und Absenz ohne Grund**: `m.get(k)`, `xs.first()`, `xs.find(p)`, `indexOf`, `os.env(name)`, Iterator-Ende, `Deque.pop()` | Kotlin, Swift |
| `bool` | „Ist es so?" | Zustandsprädikate (`exists`, `isEmpty`, `contains`) und Informationsantworten (`Set.add` = „war neu") — **nie** „hat es geklappt" | Go-`ok` nur beim Lookup |
| `Result<T, E>` | „Grund als Wert" | **nie Rückgabetyp der std**; der Aufrufer baut ihn (`Result.of { … }`); Ausnahme: Sammlungen von Ergebnissen aus Batch-Operationen | Swift `Result(catching:)` |
| Panik | „Wer hat sich geirrt?" | Vorbedingung aus dem **Programmtext** | Rust |

**`parse` wirft** — `int.parse(s)`, `float.parse`, `bool.parse`, `Instant.parse`, `Url.parse`
werfen `ParseError { kind: Invalid | Overflow | Empty }`: eine Parse-Antwort hat einen Grund,
den die Fehlermeldung braucht; die stille Form kostet ein Zeichen (`try? int.parse(s) ?? 0`).
Rust (`Result`); gegen Swift (`Int("42")` → Optional) und 4.x (`parseInt` → `?int`).

**Panik-Regel (normativ):** *Ein Argument, das aus Daten stammen kann, panikt nie — es
antwortet `?T`, `bool` oder wirft. Ein Argument, das der Programmierer schreibt (Index,
Trenner, Format-Spec, Bereichsgrenze), darf paniken.* Da Paniken unfangbar sind außer an der
Task-Grenze (E-Reihe): **kein Parser, Decoder oder I/O-Pfad der std panikt auf Eingabe.**

| Fall | Antwort |
|---|---|
| `xs[i]` außerhalb | Panik; `xs.get(i)` → `?T` (COL-23) |
| `s[a..b]` außerhalb oder nicht an Zeichengrenze | Panik (A2, Rust) |
| `s.split("")` | Panik (Python `ValueError`) |
| Ganzzahl-Überlauf, Division durch null | Panik (T-Reihe); `checkedDiv` für Daten |
| `x!` auf `null` | Panik |
| `List.remove(i)` außerhalb | Panik; `Map.remove(k)` fehlend → `?V` |
| `Duration.ofSeconds(-5)` | erlaubt (negativ ist ein Wert) |
| `fmt.format("{:zz}", x)` mit kaputtem Spec zur Laufzeit | Panik (im f-String prüft es der Compiler) |
| ungültiges UTF-8, kaputtes JSON, fehlende Datei | wirft |

Werfende Iteratoren (`lines()`, `fs.walk`): Protokollfrage → B6.

## B4 — Kern-Interfaces und Verträge in `std.core`: **entschieden** (2026-09-29)

| Gruppe | Interfaces |
|---|---|
| Gleichheit/Ordnung | `Equatable`, `Hashable`, `Ordered`, `TotalOrder`, `Identity` (Marker), Enum `Ordering { Less, Equal, Greater }` |
| Text | `Display`, `Debug`, `Format`, `Parse { static fn parse(s: StringView): Self throws ParseError }` |
| Erzeugung/Konversion | `Default { static fn default(): Self }`, `Clone { fn clone(): Self }`, `From<T> { static fn from(v: T): Self }`, `Into<T>` als Blanket über generischen Extend (`extend<T, U :: [From<T>]> T :: [Into<U>]`, X1) |
| Operatoren | `Add`…`Rem`, `Neg`, `BitAnd/Or/Xor/Not`, `Shl/Shr`, `Contains<T>`, `Index<K>`, `IndexSet<K>` (D6) |
| Iteration | `Iterator`, `Iterable`, `FromIterator` (B6) |
| Zahlen | `Num`, `Integer`, `Float` (B5) |
| Sonstige | `Error`, `Any`, `Closeable`, `FromArrayLiteral<T>` |

| # | Entscheidung | Vorbild |
|---|---|---|
| K1 | **`Closeable { fn close(): void throws Error }`** ist der Name des Ressourcen-Interfaces (05 R1) — nennt die geforderte Methode; `Resource` (Kategorie-Substantiv), `Disposable` (C#), `Drop` (Rust, RAII) verworfen | Java, Kotlin, Go `io.Closer` |
| K2 | **`Hashable { fn hash(h: &Hasher) }`** — streaming: der Typ füttert seine Felder, der Hasher mischt; `Hasher` ist ein Struct in `std.hash`; Synthese schreibt `h.write(feld)` je Feld; `Map<K, V, H = DefaultHasher>`. **Default-Hasher SipHash-1-3 mit zufälligem Prozess-Schlüssel** (Z3 „Server": HashDoS); Reproduzierbarkeit über `Map.withHasher(FixedHasher)`. `fn hash(): int` (C#, Java, 4.x: `combineHash` von Hand, schwache Mischung) verworfen | Rust `Hash`/`Hasher`, Swift `hash(into:)` |
| K3 | **Verträge (normativ)**: *Hash* — `a == b ⇒` derselbe Strom; Hashwerte **nicht stabil** über Prozesse/Versionen, nie persistieren; `float` ohne Hash (D6). *Ordnung* — `compare` konsistent mit `equals`; `TotalOrder` total und antisymmetrisch; **`sort()` stabil**, `sortUnstable()` daneben; `sortBy(cmp)`, `sortByKey(f)` (COL-39). *Clone* — siehe K5 | Rust, Go, Python (Hash-Instabilität ausgesprochen) |
| K4 | **`FromArrayLiteral<T> { static fn fromLiteral(items: T[]): Self }`**: ein Array-Literal koerziert an den erwarteten Typ (T3) — `let s: Set<int> = [1, 2, 3];`, `let m: Map<string, int> = [("a", 1)];` (kein Map-Literal); eine Kopie, die `Set.of` auch hätte | Swift `ExpressibleByArrayLiteral`; Rust nur `vec!` |
| K5 | **`Clone` folgt der Konformanz** (D7 revidiert): Zuweisung und `with` kopieren einen Struct ohnehin eine Ebene (Wertmodell) — `clone()` ist die ausdrückliche Kopie „so tief, wie der Typ besitzt", und Besitz steht in der Konformanzliste: Wertfelder kopiert, Referenzfelder `clone()`d, wenn ihr Typ `Clone` ist, sonst Fehler an der Synthese; **`@Shared`** am Feld kopiert die Referenz (Parent-Zeiger, Dienste, Zyklen); Container bedingt `Clone`. Kein `DeepClone` (zwei Mechanismen; C#s `ICloneable`-Unklarheit) | Rust; gegen Kotlin `copy`/Java `clone` (flach) |

## B5 — Zahlen: **entschieden** (2026-09-29)

Nach T1a sind es **10 Zahlentypen** (`int8…int64=int`, `uint8…uint64=uint`, `float32`,
`float64=float`); nach D4 keine Typüberladung, nach N7 keine Suffixe — die Oberfläche läuft
**generisch über Interfaces** mit statischen Membern (T5). Jeder Typ konformiert in
`std.core`; Interface-Defaults liefern das meiste einmal (`checked*` über
`__builtin_*_overflow`, `pow` per Quadrieren).

```
interface Num :: [Equatable, Ordered, Add, Sub, Mul, Div, Rem, Display, Debug, Parse, Default] {
    static let zero: Self;  static let one: Self;  fn isZero(): bool;
}
interface Signed :: [Num, Neg] { fn abs(): Self; fn signum(): Self; }
interface Integer :: [Num, TotalOrder, Hashable, BitAnd, BitOr, BitXor, BitNot, Shl, Shr] {
    static let min: Self;  static let max: Self;  static let bitWidth: int;  static let isSigned: bool;
    fn checkedAdd(o: Self): ?Self; …   fn saturatingAdd(o: Self): Self; …   fn wrappingAdd(o: Self): Self; …
    fn pow(n: uint): Self;  fn leadingZeros(): int;  fn trailingZeros(): int;  fn popCount(): int;
    fn rotateLeft(n: int): Self;  fn toBytesLE(): uint8[N]; …
    static fn parse(s: StringView, radix: int = 10): Self throws ParseError;
    static fn exact<T :: [Integer]>(v: T): ?Self;   static fn clamping<T :: [Integer]>(v: T): Self;
}
interface Float :: [Signed] {
    static let epsilon: Self;  static let infinity: Self;  static let nan: Self;
    static let min: Self;  static let max: Self;  static let leastPositive: Self;
    fn isNan/isInfinite/isFinite(): bool;
    fn floor/ceil/round/trunc/sqrt/cbrt/exp/ln/log2/log10/sin/cos/tan/…/atan2(y)/hypot(o): Self;
    fn pow(e: Self): Self;  fn totalCompare(o: Self): Ordering;   // IEEE totalOrder, für sortBy
    fn toBits(): uint64;  static fn fromBits(b: uint64): Self;
    static fn parse(s: StringView): Self throws ParseError;
}
```

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| Z1 | Turm `Num` / `Signed` / `Integer` / `Float`; `abs`/`signum` nur auf `Signed` (`uint.abs()` gibt es nicht) | Swift `Numeric`/`SignedNumeric`/`BinaryInteger`/`FloatingPoint`, .NET `INumber<T>`; verworfen: `abs` für alle (Rust `num`-Crate) |
| Z2 | **Elementarfunktionen sind Methoden** (`x.sqrt()`, `x.sin()`). **Konstanten nach Herkunft**: was den **Typ** beschreibt, ist statisches Member (`float.epsilon`, `float.infinity`, `float.nan`, `float.min`/`max`/`leastPositive`, `int.min`/`max`/`bitWidth`); was die **Welt** beschreibt, liegt in `std.math` (`math.pi`, `math.e`, `math.tau`, typisiert `float`; `math.pi as float32` für die schmale Breite). `std.math` behält daneben Zweistelliges: `min(a, b)`, `max`, `gcd`, `lcm`, `lerp`; `x.clamp(lo, hi)` ist Methode auf `Ordered` | Rust `f64::sin`; Python `math.pi`, C# `Math.PI`, Go `math.Pi`; verworfen: Swift `Double.pi` (generisches π selten), freies `sqrt(x)` (Go, Python, C) |
| Z3 | Generische Algorithmen einmal: `sum<T :: [Num]>()`, `product()`, `average()` (nur `Float`), `min()/max()` über `Ordered` (`?T`), `minBy/maxBy` | Rust `Sum`/`Product` |
| Z4 | Geprüfte Verengung **statisch am Zieltyp**: `int8.exact(n): ?int8`, `int8.clamping(n)`; Weitung implizit (T1c); `as` wrappt (T1d). Verworfen: `n.toInt8()` (64 Methoden), `TryFrom` (zweites Konversionsinterface) | Swift `Int8(exactly:)`/`(clamping:)` |
| Z5 | **Zahl → Text**: Ganzzahlen dezimal; Floats **kürzeste Darstellung, die zurückliest** (Ryu), ganze Werte mit `.0` (`1.0`), `nan`/`inf`/`-inf`; Radix/Breite/Präzision nur über die Formatsprache (`{x:x}`, `{x:.2f}`) — kein `toString(radix)` | Python `repr`, Swift, JS; verworfen: Go `%v` → `1`, C#-Kultur |
| Z6 | `Parse`: `int.parse(s, radix: 10)`, `float.parse(s)` — ASCII-Ziffern, Vorzeichen, `_` erlaubt, kein Whitespace; wirft `ParseError { kind: Invalid \| Overflow \| Empty }` (B3) | Rust `str::parse`, Go `strconv` |
| Z7 | **Kein BigInt, kein `decimal` in 5.0** — Türen (`int128` T1f, `decimal` als Paket; IEEE 754 decimal128 wäre per D zulässig, ohne Bedarf) | Python `int` verworfen |
| Z8 | `char` und `bool` außerhalb des Turms (T1e); `char.toUint32()` / `char.fromUint32(n): ?char` statt `as` | — |

## B6 — Iterator-Protokoll, Adapter, Generatoren: **entschieden** (2026-09-29)

```
interface Iterator {
    type Item;
    type Error :: [Error] = never;              // I5
    fn next(): ?Item throws Error;
    fn sizeHint(): (int, ?int) { return (0, null); }
}
interface Iterable { type Iter :: [Iterator]; fn iter(): Iter; }
interface DoubleEnded :: [Iterator] { fn nextBack(): ?Item; }   // Arrays, Slices, List, Ranges → rev()
```

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| I1 | **`next(): ?Item`, `null` = Ende** (COL-10 A; `??T` erlaubt, also `Iterator<Item = ?T>` möglich). `sizeHint` als Default für Vorallokation in `collect` | Swift; verworfen: `hasNext/next` (Kotlin), Push-Iteration (Go 1.23) |
| I2 | **`for` nimmt nur `Iterable`**; `Iterator :: [Iterable]` mit `iter(): this` (COL-11 B); Warnung bei zweifachem Iterator-Local im Schleifenkopf (COL-11 C) | Rust `IntoIterator` |
| I3 | **Adapter sind generische Extends auf `Iterator`** (COL-12 B, X1) und liefern **konkrete Adapter-Structs** (`Map<I, U>`, `Filter<I>`, …) — monomorphisiert, allokationsfrei; `Iterator<Item = T>` als Interface-Wert nur bei gewollter Typlöschung (Box) | Rust, Swift `LazyMapSequence`; verworfen: Default-Methoden mit Slots (4.x, 15 KB je Modul) |
| I4 | **Adapter**: `map`, `filter`, `mapNotNull`, `take`, `skip`, `takeWhile`, `skipWhile`, `stepBy`, `zip`, `chain`, `flatMap`, `flatten`, `enumerate` (→ `(int, Item)`), `chunks(n)`/`windows(n)` (→ `List<Item>`), `inspect`, `dedup`/`dedupBy`, `scan`, `peekable()` → `Peekable<I>` mit `peek(): ?Item`, `rev()` (nur `DoubleEnded`), `cycle()` (Iterator `Clone`). **Terminatoren**: `count`, `fold`, `reduce`, `first`, `last`, `nth`, `any`, `all`, `none`, `find`, `position`, `forEach`, `collect<C :: [FromIterator<Item>]>()` (Zieltyp inferiert), Kurzformen `toList`/`toArray`/`toSet`/`toMap`; bedingt: `sum`, `product`, `average`, `min`/`max` (`?Item`), `minBy`/`maxBy`, `sorted()`/`sortedBy()` → `List` (materialisiert, im Namen), `join(sep)` für `Item :: [Display]`, `partition(p)` → `(List, List)`, `groupBy(f)` → `Map` (eager) | Rust, Kotlin, Python |
| I5 | **Werfende Iteratoren über `type Error`**: Default `never` (E12) — gewöhnliches `for`. Ist `Error ≠ never` (`reader.lines()`, `fs.walk`), ist der Schleifenkopf markiert: **`for (line in try reader.lines())`** — das `try` deckt Quellausdruck *und* jedes `next()`; fangen/propagieren wie jeder Wurf. **Join-Regel** für Adapter: gleicher Typ → dieser; einer `never` → der andere; verschieden → Wurzel `Error` (K2, T11). Lambdas in Adaptern dürfen werfen (K3): `map { try parse(it) }` hat `Error = ParseError`. `Item = Result<…>` nur, wo B3 es erlaubt (Batch) | **Swift 6 `AsyncIteratorProtocol<Failure>`** + `for try await`; verworfen: `Item = Result` (Rust — gegen B3), lazy Adapter ohne Wurf (Swift-`lazy`, Kotlin-Falle beim Terminator) |
| I6 | **Freigabe bei `break`/`return`/Wurf**: `for` ruft `close()`, wenn der Iterator `Closeable` ist — statisch bei bekanntem Typ, sonst `is Closeable` (T11). Kein `close` am `Iterator` selbst (zweiter Mechanismus neben `Closeable`) | C# `IEnumerator : IDisposable`; Python `gen.close()` |
| I7 | **Generatoren sind Iteratoren**: `Coroutine<T> :: [Iterator<Item = T>]` eingebaut (COL-26 B); `sequence { yield … }` (Y11) ist die kürzeste Iterator-Definition; `enumerate`/`chunks` dürfen intern Generatoren sein. **`Coroutine<T> :: [Closeable]`**: `close()` auf einer schwebenden Koroutine wickelt ihren Stack ab, `defer`/`using` laufen (01 K7a) | C# `yield return`, Python, Kotlin `sequence` |
| I8 | **`Map<K, V> :: [Iterable<Item = (K, V)>]`** (SL-19), dazu `keys()`, `values()`, `entries()`; **Reihenfolge unspezifiziert** (COL-28 A) — mit SipHash-Prozessschlüssel (K2) je Lauf anders, wie Rust/Go; `LinkedMap`/`LinkedSet` als Tür | Go, Rust; verworfen: Einfügereihenfolge (Python 3.7 — Verkettung pro Container) |
| I9 | **Mutation während Iteration = Panik** („modified during iteration") über Modifikationszähler in `List`/`Map`/`Set`/`Deque` (COL-16 C; ein Vergleich je `next`) | C#, Java (fail-fast); verworfen: „undefiniert" (4.x), Gos Teilzusagen |
| I10 | `Range`/`RangeInclusive` sind `Iterator` **und** `DoubleEnded`; `T[]`/`Slice`/`List` liefern über `iter()` einen `SliceIter<T>` (Struct, bounds-check-frei); `for (x in arr)` lowert auf die Indexschleife (COL-09 B) | Rust |

Sprachnachträge eingetragen: 05 K7 (Join-Regel), 05 R7 (`for` schließt `Closeable`), 08 S3a
(`try` im Schleifenkopf), 01 K7a (`close` auf schwebender Koroutine).

## B7 — Container: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| C1 | **`std.collections`: `List<T>`, `Map<K, V, H = DefaultHasher>`, `Set<T, H>`, `Deque<T>`, `Heap<T :: [TotalOrder]>`** (Binärheap: `push`/`pop`/`peek`). **Türen**: `SortedMap`/`SortedSet` (B-Tree), `LinkedMap` (I8), `BitSet` | Rust `BinaryHeap`, C# `PriorityQueue`; Go/Python/Swift ohne SortedMap |
| C2 | **Member von `T[]`, `Slice<T>`, `T[N]` liegen in `std.core`** (Sprachprimitive, ohne Import sichtbar): `length()`, `isEmpty()`, `get(i): ?T`, `first`/`last`, `iter()`, `contains`, `indexOf`, `fill`, `copyInto(dst)`, `reversed()`, `reverse()`, `sort`-Familie, `binarySearch(v): ?int`, `partitionPoint(p)`, `join(sep)`; `arr[a..b]` → `Slice<T>` (A2). *Korrigiert B1: nicht `collections`* | Rust `core::slice` |
| C3 | **`List<T>`** (Klasse, Verdopplung, `shrinkToFit()`): `new()`/`withCapacity(n)`/`of(arr)`/`from(iterable)`; `[i]` (Panik), `get(i): ?T`, `[a..b]` → `Slice<T>` (View auf den Puffer; Wachstum löst den View — zeigt auf den alten Puffer, speichersicher, dokumentiert wie Go); `push`, `pop(): ?T`, `insert(i, v)`, **`removeAt(i): T`, `remove(v): bool`** (Arität trennt `remove(int)`/`remove(T)` nicht — Kotlins Paar), `removeWhere(p)`, `pushAll(iterable)`, `clear`, `truncate(n)`, `swap`, `dedup`, `toArray()`, `asSlice()`; C2 per `asSlice()` | Kotlin `MutableList`, Rust `Vec` |
| C4 | **`Map`**: `[k]` → `?V`, `[k] = v` (N3), `get`, `getOr(k, d)`, `getOrInsert(k, make)`, `insert(k, v): ?V` (alter Wert), `remove(k): ?V`, `containsKey`, `update(k, f)`, `retain(p)`, `keys/values/entries`, `from(pairs)`; **kein `entry`-Typ**. **Implementierung: Swiss-Table** (offene Adressierung, Gruppen-Metadaten, Last 7/8, Tombstone-Rehash), SipHash-1-3 (K2); `Set` über dieselbe Tabelle | Rust hashbrown, Abseil; verworfen: `entry`-API, Robin-Hood |
| C5 | **`Set`**: Mengenoperationen als **Methoden** `union`, `intersect`, `difference`, `symmetricDifference`, `isSubset/isSuperset/isDisjoint` (B2; 4.x frei); keine Mengen-Operatoren (Tür) | Kotlin, Swift |
| C6 | **`Deque`** (Ringpuffer): `pushFront/pushBack/popFront/popBack/peekFront/peekBack`, `[i]`, **`Iterable`** vorn→hinten — „a queue is drained, not walked" (4.x) fällt | Rust `VecDeque`, C# `Queue` |
| C7 | **Concat und Wiederholung als Operatoren** (Maintainer): `+` auf `string`, `T[]` (`Add<Out = T[]>`), `Slice<T>` (`Out = T[]`) → neuer Wert; **`xs * n`** auf `string` und **`extend<T :: [Clone]> T[] :: [Mul<Rhs = int, Out = T[]>]`** — **jeder Slot ein `clone()`** (K5): `[List.new()] * 3` sind drei Listen, ein nicht-`Clone`-Element ist ein Übersetzungsfehler mit Hinweis auf `arrayOf`. Kein `n * xs`. **`arrayOf<T>(n, f: fn(int) -> T): T[]`** bleibt (frische, indexabhängige Elemente); `arrayFilled` fällt (= `[v] * n`). **Inline**: `let buf: uint8[64] = [0] * 64;` — konstantes `n` mit erwartetem `T[N]` baut zur Übersetzungszeit (T13); `T[N] :: [Default]` bedingt (D7-Synthese für Structs mit Array-Feld); `T[]` hat kein `Default` (`[T.default()] * n`) | Rust `vec![x; n]` (verlangt `Clone`), Python `+`/`*`; verworfen: Verbot der Wiederholung (Vorfassung C7, Pythons Alias-Falle — gelöst durch Clone je Slot) |
| C8 | **Hash-Fähigkeit**: `string`, Tupel, `T[N]` (Inline-Wert), Structs strukturell; **`T[]`, `Slice`, `List`, `Map`, `Set` nicht `Hashable`** (Referenz mit schreibbaren Slots; COL-30 A); `Map<int[], V>` ist ein Übersetzungsfehler an der Aufrufstelle | C#, Java; Rust nur für `[T; N]` — gilt für unser `T[N]` |
| C9 | **Gleichheit/Anzeige**: `List`, `T[]`, `Slice`, `Set`, `Map`, `Deque` bedingt `Equatable` (elementweise; `Set`/`Map` ordnungsunabhängig); `Debug` automatisch (`[1, 2, 3]`, `{a: 1}`, Strings zitiert); **`Display` explizit konformiert = Debug-Form** (D7 „auf Anfrage"), damit `println("{xs}")` druckt — mit Anführungszeichen um Strings, weil `[a, b]` die Grenzen verlöre | Python/Kotlin (drucken Container); Rust (kein `Display` für `Vec`) verworfen |
| C10 | **Ordnung**: `sort()` stabil (K3), `sortUnstable()`, `sortBy(cmp: fn(T, T) -> Ordering)`, `sortByKey(f)`; `binarySearch` verlangt `TotalOrder` (`NaN` bräche sie, COL-39 D) | Rust |
| C11 | **Nicht thread-sicher**; Data Races sind Programmfehler (G3); `Mutex<List<T>>` aus `std.sync`; `ConcurrentMap` Tür | Rust, Go |
| C12 | `x in xs` über `Contains<T>` (D6) auf `List`, `Set`, `Map` (Schlüssel), `T[]`, `Slice`, Ranges, `string` | Kotlin, Python |

## B8 — I/O: Ströme, Dateien, Netz, Prozesse, Konsole: **entschieden** (2026-09-29)

```
interface Reader { fn read(into: Slice<uint8>): int throws IoError; }        // 0 = EOF
interface Writer { fn write(from: Slice<uint8>): int throws IoError; fn flush(): void throws IoError {} }
interface Seek   { fn seek(pos: SeekFrom): int throws IoError; }            // enum SeekFrom { Start(int), Current(int), End(int) }
```

(Ein einzelner Fehlertyp steht ohne Klammern — D9; die Liste `[A, B]` nur bei mehreren.)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| O1 | **Go-Form** mit `Slice<uint8>` (Aufrufer stellt den Puffer); `read` liefert 0 **nur** am Ende, leerer Slice → 0 sofort; `write` darf partiell sein. Defaults per generischem Extend: `readExact(into)` (wirft `UnexpectedEof`), `readToEnd(): uint8[]`, `readToString(): string`, `writeAll(from)`, `writeString(s)`; frei `io.copy(r, w): int` (B2). `EINTR` verschwindet in der C-Schicht | Go `io.Reader/Writer`, Rust `Read/Write`; verworfen: `BufRead` als drittes Lese-Interface, `Stream`-Basisklasse (C#) |
| O2 | **Puffer-Hüllen als Structs mit eigenem Puffer** (8 KiB, Inline-Bytes): `BufReader<R>`, `BufWriter<W>` (flush bei `close()`; ungeschlossen → R3-Warnung), **`TextReader<R>`** = `BufReader` + UTF-8-Decoder: `readLine(): ?string`, `lines(): Iterator<Item = string, Error = IoError>`, `readToEnd(): string`, `chars()`; gesplitteter Codepoint bleibt im Decoder, ungültiges UTF-8 → `IoError { kind: InvalidData, cause: Utf8Error }` (nie `""` wie 4.x), BOM nur mit `skipBom: true`. **`TextWriter<W>`**: `write(s: StringView)`, `writeLine(s)`. Speicherströme **`ByteReader`** (über `Slice<uint8>`, `Seek`) und **`ByteBuffer`** (wachsend, `Reader`+`Writer`, `toArray()`) — deklarieren `throws IoError`, werfen nie | Zig 0.15, C# `StreamReader`, Go `bytes.Buffer`; verworfen: `lines()` als Default auf `Reader` (heimliches Puffern) |
| O3 | **Ein Fehlertyp**: `struct IoError :: [Error] { kind: IoErrorKind, path: ?string, detail: string, cause: ?Error }`; `@NonExhaustive enum IoErrorKind { NotFound, PermissionDenied, AlreadyExists, IsDirectory, NotDirectory, InvalidInput, InvalidData, UnexpectedEof, TimedOut, ConnectionRefused, ConnectionReset, AddrInUse, BrokenPipe, Closed, Unsupported, Other(code: int) }` — Datei, Netz, Prozess, Speicher, Ring (SL-22 A); Fat-Pointer-Interfaces tragen keinen typabhängigen Fehler | Rust `io::Error`/`ErrorKind`, Go `errors.Is` |
| O4 | **Handles sind Klassen**: `class File :: [Reader, Writer, Seek, Closeable]`, `TcpStream :: [Reader, Writer, Closeable]`, `TcpListener`, `UdpSocket`, `Child` — Identität, `closed`-Zustand (M12), `using let f = File.open(p)`. Kein `opaque` (T15) | Go `*os.File`, Swift `FileHandle`; verworfen: Struct-Adapter um nackte fds |
| O5 | **`std.fs`**: `File.open(path)`, `File.create(path)`, `File.openWith(path, OpenOptions { read, write, append, create, truncate })`; Komfort `fs.readText`, `fs.readBytes`, `fs.lines(path)` (Iterator, `Error = IoError`), `fs.writeText`, `fs.writeBytes`, `fs.appendText`, `fs.exists`, `fs.metadata(path): Metadata { size, modified: Instant, isFile, isDir, isSymlink, readonly }`, `fs.remove`, `fs.removeDir`, `fs.removeAll`, `fs.createDir`, `fs.createDirAll`, `fs.copy`, **`fs.rename`** (4.x `move`), `fs.readDir(path): Iterator<Item = DirEntry>`, `fs.walk(path)`, `fs.canonicalize`, `fs.tempDir()`, `fs.tempFile()`; Türen: Symlinks, `watch`, Rechte-Bits | Rust `std::fs`, Kotlin `File.readText` |
| O6 | **Pfade sind Strings, kein `Path`-Typ**: `path.join(a, b, …)`, `fileName`, `parent`, `extension`, `stem`, `withExtension`, `isAbsolute`, `normalize` (lexikalisch), `relative(base, target)`, `components(p)`, `separator`; `fs.absolute(p)` (braucht cwd). `Path`-Typ Tür | Go `filepath`; verworfen: Rust `Path`/`PathBuf`, Python `pathlib` |
| O7 | **`std.net`**: `IpAddr` (Enum V4/V6, `Parse`), `SocketAddr { ip, port }` (`Parse`), `net.resolve(host): List<IpAddr>` (DNS auf dem Pool, S4); `TcpListener.bind(addr)`, `accept(): TcpStream`, `TcpStream.connect(addr)`, `shutdown(how)`, `setNoDelay`, `peerAddr`/`localAddr`; `UdpSocket.bind(addr)`, `sendTo(from, addr)`, `recvFrom(into): (int, SocketAddr)`. **Keine Socket-Timeouts** — `timeout(d) { … }` aus `std.task` ist der eine Mechanismus. Unix-Sockets Tür | Rust `std::net`, Go `net` |
| O8 | **`std.process`**: Options-Struct **`Command { program, args = [], cwd = null, env = [], stdin/stdout/stderr = Stdio.Inherit \| Piped \| Null }`** (N12); `cmd.spawn(): Child` mit `stdin: ?Writer`, `stdout: ?Reader`, `stderr: ?Reader`, `wait(): ExitStatus`, `kill()`; Komfort `cmd.output(): Output { status, stdout: uint8[], stderr: uint8[] }`, `cmd.status()` | Rust `Command`, Python `subprocess.run` |
| O9 | **Konsole in `std.io`**: `stdin()` (Reader; `lines()`, `readLine(): ?string` = null bei EOF), `stdout()`/`stderr()` (Writer; zeilengepuffert am Terminal, blockgepuffert sonst, Flush bei Programmende), `print<T :: [Display]>(v)`, `println`, `eprint`, `eprintln` (ein Argument, der f-String formatiert), `flush()`. Terminal-Erkennung und Escapes in `std.term` | Rust, C |
| O10 | **Alles yieldet**: `read`/`write`/`accept`/`connect`/`wait` auf Handles parken den laufenden Task (S3/S4); Speicherströme parken nie; `main` ist ein Task (T6) — kein „nur in `run()`"-Vorbehalt | Go |
| O11 | **`std.uri`, Typ `Uri`, streng RFC 3986** (`Parse`; `scheme`, `authority`, `host`, `port`, `path`, `query`, `fragment`, `resolve(relative)`, Percent-Encoding; RFC 3987 IRI Tür). Name `Uri`, weil es der Name der Norm ist, die HTTP (RFC 9110) referenziert; WHATWG-URL-Lenienz ist Browser-Verhalten, kein Bibliotheksvertrag | C# `System.Uri`, Java `URI`, Go `net/url`; verworfen: WHATWG-Parser (Rust `url`) |

`std.http`/`std.term`/`std.compress` als Formen in B11 (Bibliotheksarbeit, keine Designfragen).

## B9 — Strings und Unicode: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| S1 | **`string` = unveränderliches UTF-8, `length()` in Bytes, O(1)**; `s[a..b]` → `StringView` mit Byte-Indizes, Panik an Nicht-Zeichengrenzen (A2); **`s[i]` → `char`** — das Zeichen, das an Byte `i` beginnt (`Index<int> { type Output = char }`, derselbe Index-Raum wie `s[a..b]`, dieselbe Grenzregel, O(1); gültige Indizes liefern `find`, `charIndices()`, Slice-Grenzen; `for (i in 0..s.length()) s[i]` ist bei Nicht-ASCII **laut** falsch — Guide: Zeichen über `chars()`); kein `IndexSet`; `chars(): Iterator<Item = char>` (`DoubleEnded`), `charCount()` (O(n), im Namen), `charIndices()`, `asBytes(): Slice<uint8>` (O(1)); `==`/`Ordered`/`TotalOrder`/`Hashable` byteweise (= Codepoint-Ordnung). **Member stehen einmal auf `StringView`; `string` erreicht sie über den Auto-View** (wie `T[]` → `Slice<T>`, A2). 4.x zählte Codepoints in O(n) | Rust `str`, Go; verworfen: Swift-Graphem-`count`, Python-Codepoint-Index (zwei Index-Räume, O(n) still), `s[i]` als `uint8` (Go — `asBytes()[i]`), `s[i]` als 1-Zeichen-View (falscher Typ für ein Zeichen) |
| S2 | **`Pattern`-Interface für Suchargumente**, konformiert von `StringView`, `char`, `fn(char) -> bool`, `Regex` — eine Signatur je Operation (D4: keine Typüberladung): `contains(p)`, `startsWith(p)`, `endsWith(p)`, `find(p): ?int`, `rfind(p)`, `split(p): Iterator`, `splitN(p, n)`, `splitOnce(p): ?(StringView, StringView)`, `trim(p = whitespace)`/`trimStart`/`trimEnd`, **`stripPrefix(p): ?StringView`**/`stripSuffix` (Absenz ist Information, B3), `replace(p, with)`, `replaceN`, `matches(p): Iterator` | Rust `Pattern` |
| S3 | Weitere Member: `isEmpty`, `isBlank`, `lines()` (`\n`, `\r\n`), `toUpper`/`toLower` (S4), `padStart/padEnd(width, fill = ' ')` (Breite in Zeichen), `repeat` = `*` (C7), `parse<T :: [Parse]>()` als Zucker für `T.parse(s)`, `count(p)`; Konstruktion `string.fromChars(iter)`, **`string.fromUtf8(bytes): string throws Utf8Error { offset }`**, `string.fromUtf8Lossy(bytes)` (U+FFFD), UTF-16 in `std.encoding`. **`StringBuilder`** (Klasse): `append<T :: [Display]>(v)`, `appendChar`, `appendStr`, `length()`, `clear()`, `toString()`; Ziel der f-String-Lowerung | Rust, Kotlin |
| S4 | **Unicode-Tiefe**: Tabellen (Kategorie, Simple Case Mapping, `White_Space`) generiert in der C-Schicht, **Unicode-Version gepinnt**, lesbar als `std.string.unicodeVersion`; **Simple Case Mapping, invariant** (`ß` bleibt `ß`, kein Locale — dokumentiert); `==` ist Codepoint-Gleichheit; **keine** Normalisierung, Grapheme, Breite im Kern (Ring `unicode`, B1) | Go `unicode`; verworfen: Full Case Mapping (Rust — `char` → Iterator), Locale-Parameter |
| S5 | **`char`**: **`isX` = Unicode, `isAsciiX` = ASCII** — konsequent: `isAlpha` (L*), `isDigit` (**Nd**; 4.x ASCII), `isAlphanumeric`, `isWhitespace` (White_Space), `isUpper/isLower`, `isControl`, `category(): UnicodeCategory`; `isAsciiDigit`, `isAsciiAlpha`, `isAsciiHexDigit`, `isAsciiWhitespace`; `toUpper/toLower(): char` (simple), `toDigit(radix): ?int`, `char.fromDigit(n, radix): ?char`, `toUint32()`/`char.fromUint32(n): ?char` (Z8); `parse` bleibt ASCII (Z6) | Rust `is_alphabetic`/`is_ascii_alphabetic` |
| S6 | **`Display` bekommt ein zweites Mitglied mit Default**: `fn showTo(out: &StringBuilder): void { out.appendStr(show()); }` — f-Strings und Container-`Display` rufen `showTo`; Kerntypen überschreiben es (keine Zwischenstrings, kein O(n²) in Ketten). `Debug` ebenso (`debugTo`). Ein Interface, derselbe Mechanismus mit Ziel — kein `Formatter`-Typ | Rust `fmt(&mut Formatter)` als Ergänzung; verworfen: nur `show(): string` |
| S7 | **Formatsprache** (Y7): `std.fmt.format(template, args...)` zur Laufzeit mit der f-String-Grammatik; `Format { fn format(spec: StringView, out: &StringBuilder) }` für Typen mit eigenen Specs; Breite/Ausrichtung/Füllung generisch für jeden `Display`-Typ, gezählt in Zeichen | Python |
| S8 | **`std.regex`**: `Regex.new(pattern): Regex throws RegexError` (`r"…"`; `comptime Regex.new(…)` prüft zur Übersetzungszeit), `isMatch`, `find(s): ?Match { start, end, group(i), named(n) }`, `findAll`, `captures`, `replace`, `split`; RE2-Syntax (Klassen, Gruppen, benannte Gruppen, Unicode-Klassen; **keine** Backreferences, kein Lookaround), **lineare Zeit** (Pike-VM + lazy DFA); `Regex :: [Pattern]` | Go `regexp`, Rust `regex`; verworfen: Backtracking (PCRE, .NET — ReDoS) |

## B10 — Serialisierung: **entschieden** (2026-09-29)

```
interface Encode { fn encode(e: &Encoder): void throws EncodeError; }
interface Decode { static fn decode(d: &Decoder): Self throws DecodeError; }
interface Codable :: [Encode, Decode] {}
```

`Encoder`/`Decoder` sind Interfaces, die ein Format implementiert; Datenmodell mit zehn Formen:
`null`, `bool`, `int`, `uint`, `float`, `string`, `bytes`, `seq`, `map`, `struct(name, fields)`,
`variant(enum, name, payload)`.

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| E1 | **Modul `std.codec`** (Paar, `Encoder`/`Decoder`, Fehler, Attribut; B1-Arbeitsname `encode` ersetzt); Formate sind eigene Module (`std.json`, `std.toml`) mit je einem `Encoder`/`Decoder`. **Kein Format ohne das Paar** (SL-14) | Swift `Codable`, Rust `serde`; verworfen: `ToJson`/`FromJson` je Format (4.x), Go-Reflexion |
| E2 | **Container-Modell**, kein Visitor: `Encoder` bietet `encodeInt(v)`, …, `encodeSeq(len): SeqEncoder`, `encodeMap(len): MapEncoder`, `encodeStruct(name, n): StructEncoder { field(name, v: Encode) }`, `encodeVariant(enum, name, index, payload)`; `Decoder` spiegelt pull-basiert (`decodeStruct(name, fields: string[]): StructDecoder { field<T :: [Decode]>(name): T }`, `SeqDecoder.next<T>(): ?T`); nicht selbstbeschreibende Formate dekodieren Structs in Feldreihenfolge | Swift `KeyedDecodingContainer`; verworfen: serdes Visitor-Doppeldispatch |
| E3 | **Synthese über die Konformanzliste** (A5): `struct User :: [Codable] { id: int, name: string, mail: ?string }` feldweise per `comptime for (f in fields(Self))`, Deklarationsreihenfolge; Enums: Unit-Varianten als String, Nutzlast-Varianten **extern getaggt** (`{ "Circle": { "r": 1 } }`); eigene `encode`/`decode` ersetzt die Synthese | serde |
| E4 | **Steuerung per Art-1-Attribut `@Codec`** (R4): am Feld `@Codec { name = "user_id", skip = true, default = true }`, am Enum `@Codec { tag = "kind" }` (intern getaggt), am Typ `@Codec { rename = .camelCase \| .snakeCase, denyUnknown = true }` | serde-Attribute; Swift `CodingKeys` verworfen |
| E5 | **Fehler**: `DecodeError { kind: Missing \| TypeMismatch \| Invalid \| UnknownField \| UnknownVariant, path: string ("users[0].mail"), detail }`, `EncodeError { path, detail }`; Formatfehler eigene Typen (`JsonError { line, column }`, `TomlError`) — `json.decode<T>(text): T throws [JsonError, DecodeError]` | Swift `DecodingError.codingPath` |
| E6 | **Regeln**: `?T` ↔ `null` **oder fehlend**; fehlendes Pflichtfeld → `Missing` (außer `default`); unbekanntes Feld ignoriert (`denyUnknown` dreht es); `uint8[]` formatabhängig (JSON Base64, TOML Array); `Instant` ↔ RFC 3339; `Map<K, V>` bedingt für `K` = `string` oder `Integer` (Zahlen als Schlüsselstrings), sonst Übersetzungsfehler; Ganzzahl-Überlauf beim Dekodieren → `Invalid`; `Map`-Schlüsselreihenfolge unspezifiziert, `sortKeys`-Option | serde; verworfen: Swifts Map-als-Paarliste |
| E7 | **`std.json`**: `json.encode<T :: [Encode]>(v, opts = JsonOptions { pretty = false, indent = 2, sortKeys = false }): string`, `json.encodeTo(v, w: Writer)`, `json.decode<T :: [Decode]>(text): T`, `json.decodeFrom<T>(r: Reader)`; **`JsonValue`** als dynamischer Baum (`enum { Null, Bool, Int, Float, String, Array(List), Object(Map) }`), selbst `Codable` (`json.decode<JsonValue>` = altes `parse`); `[]` über `Index<string>`/`Index<int>` → `?JsonValue`, `path("a.b[0]")`, `Display` kompakt, `Debug`, `Equatable`. Strikt RFC 8259 (JSON5 Tür) | Rust `serde_json::Value`, Go |
| E8 | **`std.toml`**: `toml.decode<T>`, `toml.encode`, `TomlValue` (mit `Datetime`); TOML 1.0; Manifest-Leser (Bereich 11) nutzt es | Rust `toml` |
| E9 | **Türen**: CBOR (RFC 8949, per D zulässig), MessagePack, CSV, JSON5/JSONC, streamender `SeqDecoder` | — |

## B11 — Zeit, Zufall, Hash, Krypto, Encoding, HTTP, Terminal, Kompression, OS: **entschieden** (2026-09-29)

| # | Modul / Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| Q1 | **`std.time`** — `Duration` (Struct, `int` Nanosekunden; `ofSecs/ofMillis/ofMicros/ofNanos/ofMinutes/ofHours`, `secs()`/`millis()`/`asSecsFloat()`, `Add/Sub/Mul<int>/Div<int>`, `Ordered`/`TotalOrder`/`Hashable`/`Default`, `Display` `1.5s`, **`Parse` `"1h30m"`**); **`Instant`** = Wanduhr-Zeitpunkt UTC (`now()`, `epochSecs/Millis`, `+ Duration`, `since(o)`, `Parse`/`Display` RFC 3339, `Codable`); **`Monotonic`** (`now()`, `elapsed()`); Kalender `Date`, `Time`, `DateTime { date, time, offset }`, `Zone.utc`/`Zone.fixed(offset)`/**`Zone.load("Europe/Berlin")`** (TZif RFC 8536 aus der System-Datenbank; fehlt sie → `TimeError`, Ring `tzdata`)/`Zone.local()`, `instant.toDateTime(zone)`, `plusDays`, `dayOfWeek`, `isLeapYear`; **Formatmuster nach UTS #35** (`"yyyy-MM-dd HH:mm"`); `sleep`/`Timer` in `std.task` (K3) | Go `time`, Java `java.time`; verworfen: Gos Referenz-Layout, Rust `Instant`=monoton (Kollision mit 4.x), Zeitzonen ganz im Ring |
| Q2 | **`std.random`** — `Random` (Klasse, **ChaCha8**, Strom **stabil innerhalb eines Majors** — zugesagt), `Random.seeded(seed: uint64)`, `Random.fresh()` (aus `crypto.randomBytes`); `nextInt()` = **volle 64 Bit, `int.min..=int.max`** gleichverteilt (Rust; Gos nicht-negatives `Int64` verworfen — `% 10` überrascht), `nextUint64()`, `nextInRange(r: Range<int>)` (Rejection Sampling — die Form für alles Praktische), `nextFloat()` [0,1), `nextBool()`, `nextBytes(into)`, **`nextNormal(mean = 0.0, stdDev = 1.0)`** (Standardnormalverteilung; Javas Name `nextGaussian` verworfen), `shuffle(s: Slice<T>)`, `choice(s): ?T`, `sample(s, k)`; freie Kurzformen über eine thread-lokale Instanz: `random.int(0..10)`, `random.float()`, `random.shuffle(xs)`; weitere Verteilungen Tür | Go 1.22 `math/rand/v2`, Rust `rand`; verworfen: PCG, Mersenne Twister |
| Q3 | **`std.hash`** — `interface Hasher { fn write(bytes: Slice<uint8>); fn writeInt(v: int); …; fn finish(): uint64 }`; `DefaultHasher` (SipHash-1-3, Prozessschlüssel), `FixedHasher` (Nullschlüssel, reproduzierbar), `Fnv1a64`; **`Hashable { fn hash<H :: [Hasher]>(h: &H): void }`** (K2 präzisiert: generisch, kein Vtable); `crc32`, `crc32c`, `adler32`; xxHash Tür | Rust `Hash`/`Hasher`, Swift `Hasher` |
| Q4 | **`std.crypto`** — `interface Digest { static let size: int; mut fn update(bytes); fn finish(): uint8[N] }` → `Sha256`, `Sha512`, `Sha1`, `Md5` (Doku-Warnung), Einzeiler `sha256(bytes): uint8[32]`; `Hmac<D :: [Digest]>`; `randomBytes(n)`, `randomUint64()`; `constantTimeEq(a, b)`; **Türen** (U2): Ed25519, X25519, ChaCha20-Poly1305, Argon2 | Go `crypto/*`, Rust `digest` |
| Q5 | **`std.encoding`** — Namensräume als Structs mit statischen Membern: `Base64.encode(bytes): string`/`decode(s): uint8[] throws EncodingError { offset }`, `Base64Url`, `Hex`, `Utf16.encode(s): uint16[]`/`decode(units)`; `extend Slice<uint8> { getUint32LE(at), getInt64BE(at), … }`, `int.fromBytesLE(s)` (Z1-Gegenstück); `ByteBuffer` (O2) zum Bauen — **`std.bytes` geht auf** | Go `encoding/*`, Kotlin `object` |
| Q6 | **`std.http`** — HTTP/1.1 Client **und** Server über `Reader`+`Writer`: `Request { method, uri: Uri, headers: Headers, body: ?Reader }`, `Response { status, headers, body: Reader }` mit `text()`, `bytes()`, `json<T>()`; `Client.get(uri)`, `Client.send(req)`, Options `{ followRedirects = false, timeout }`; **`Server.bind(addr).serve(handler: fn(Request): Response throws Error)`** — ein Task je Verbindung; Keep-Alive, Chunked; **TLS über einen `Transport`-Haken** (`fn(SocketAddr) -> Stream`), den das Ring-Paket `tls` besetzt; `Headers` case-insensitiv; kein Cookie-Jar, HTTP/2 Tür | Go `net/http`, Rust `hyper`+`rustls` |
| Q7 | **`std.term`** — `isTerminal(stream)`, `size(): ?(int, int)`, `Style { fg, bg, bold, … }.apply(s)`, `Color` (16/256/RGB), `cursor`/`clear` (ECMA-48), respektiert `NO_COLOR`/`TERM`; Raw-Mode Tür | Rust `crossterm` (Teilmenge) |
| Q8 | **`std.compress`** — `GzipReader<R>`/`GzipWriter<W>`, `Deflate*`, `Zlib*` als Strom-Adapter; `gzip.compress(bytes, level)`/`decompress(bytes)` | Go `compress/gzip`, Rust `flate2` |
| Q9 | **`std.os`** — `args()`, `env(name): ?string`, `envs(): Map`, `setEnv`, `cwd()`, `setCwd`, `exit(code): never`, `platform`/`arch`, `homeDir`, `tempDir`, `hostname`, `cpuCount`, `pid`. **Signale**: `enum Signal { Interrupt, Terminate, Hangup, Quit, User1, User2, WindowChange, Other(n) }` — abstrakte Namen; Windows kennt nur `Interrupt` (Ctrl+C/Break) und `Terminate` (Konsole schließt); `os.signals(Signal.Interrupt, Signal.Terminate): Channel<Signal>` (mehrere je Aufruf, Kanal schließen = abbestellen; **ohne Abonnement OS-Default**); Zustellung Self-Pipe → Poller (S2) → Kanal, nie Lyric-Code im Handler; **intern**: `SIGCHLD` (Prozess-Modul), `SIGPIPE` ignoriert → `BrokenPipe`, `SIGALRM` (Timer); **nicht**: `KILL`/`STOP` (der Kernel liefert sie nicht), Fehler-Signale (`SEGV`/`BUS`/`FPE`/`ILL`/`ABRT` = Absturz mit Backtrace, kein Kanal; Guard-Pages der Koroutinen-Stacks intern); `child.signal(Signal.Terminate)` (O8); `os.kill(pid, s)` Tür. **Vormerk Bereich 11**: ein eingebettetes Runtime installiert **keine** Handler (`Runtime.init(installSignalHandlers = false)`, `os.signals` → `Unsupported`) — sie gehören dem Host. **Bare-Metal/Kernel/Firmware**: kein Ring, sondern ein „freestanding"-Laufzeitprofil ohne GC/libc — **Tür**, nicht 5.0 (Rust `no_std`, Zig `freestanding`; Go kann es nicht) | Go `signal.Notify`, Rust `signal-hook` |
| Q10 | **Nebenläufigkeit, Modulschnitt** (Inhalt 06): **`std.task`** — `spawn`, `Task<T>`, `TaskScope`, `spawnDetached`, `sleep`, `Timer`, `Channel<T>`, `Select`, `timeout`, `Cancelled`, `ChannelClosed`; **`std.sync`** — `Mutex<T>`, `RwLock<T>`, `Once`, `Atomic<T>`, `Semaphore`; **`std.thread`** — `Thread.spawn`/`join`, `Pool`, `parallelMap`, `Isolate` (Muster G7) | Kotlin, Rust `std::sync` |

## B12 — Test und Bench: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| X1 | **Assertions in `std.test`** (`assert(cond)` ist Prelude und panikt): `assertEq(actual, expected, msg = "")`, `assertNotEq`, `assertTrue/False`, `assertNull/NotNull`, `assertClose(a, e, tolerance)`, `assertLess/Greater`, `assertContains(c: Contains<T>, x)`, `assertEmpty`, `fail(msg): never`; **`assertThrows<E :: [Error]>(f: fn() -> void throws E): E`** (Lambda-Form, `E` inferiert, **gibt den Fehler zurück**), `assertPanics(f)` (eigener Task, prüft `Panicked`, T4). Reihenfolge `(actual, expected)` (N13). **Meldungen über `Debug`** (D7) und **`@callerExpr`** (09 Q10): `assertEq(xs.length(), 3)` meldet `xs.length() = 2, expected 3`; Strings: erste abweichende Position, mehrzeilig als Zeilen-Diff | JUnit/Kotlin, Rust `assert_eq!`, power-assert |
| X2 | **Test = Funktion** `@Test fn` ohne Parameter, im Paket (V4); **Subtests** `subtest("name") { … }` für tabellengetriebene Tests (einzeln berichtet, Lauf geht nach Fehlschlag weiter); `@Test { skip = "grund" }`; Filter über CLI (`lyric test -f name`). **Kein Setup/Teardown** — Fixtures sind Aufrufe plus `using` (SL-15) | Go `t.Run`; verworfen: JUnit `@Before`, Klassen-Fixtures |
| X3 | **Isolation**: jeder Test in einem eigenen Task (Panik → Test rot, Lauf geht weiter), Timeout je Test (Default 60 s, `@Test { timeout = … }`); **sequenziell je Paket, `@Test { parallel = true }` opt-in** (ohne `Send`/`Sync` ist Parallel-Default eine Falle: cwd, env, globale `var`); Pakete parallel zueinander | Go; verworfen: Rusts Parallel-Default |
| X4 | **`@Bench fn name(b: &Bench)`** mit `b.iter { … }` (Warmup, Iterationen bis stabil), `b.bytes(n)`, `blackBox(x)` gegen DCE; Bericht ns/op, Allokationen/op (GC-Zähler), MB/s; `lyric bench --save/--compare` (Bereich 11) | Go `testing.B`, Rust `criterion` (Teilmenge) |
| X5 | **`///`-Codeblöcke laufen als Tests** (09) | Rust doctests |
| X6 | **Ring**: Property-Tests, Snapshots, Mocking; Coverage-Werkzeug Bereich 11 | — |

## B13 — `std.meta` und `std.syntax`: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| M1 | **`std.meta`** (nur `comptime`, R1–R6): `fields<T>(): FieldInfo[]` (`name`, `type: Type`, `index`, `visibility`, `isVar`, `attributes`), `variants<E>(): VariantInfo[]` (`name`, `index`, `payload`), `members<T>()`, `conformances<T>()`, `hasConformance<T, I>()`, `typeName<T>()`, `isStruct/isClass/isEnum/isInterface<T>()`, `attributes<T>()`, `sizeOf<T>()`, `alignOf<T>()`; Feldzugriff `this.[f]` (R2); `target { os, arch, pointerWidth, endian }`, `profile { name, debug }` (A6); `Type` ist ein `comptime`-Wert, einsetzbar in Typposition eines `quote` | Zig `@typeInfo`, Nim |
| M2 | **`std.syntax`**: AST als **Enums/Structs mit `Box`-Nutzlast** (`Box` zählt als Wert — 09 Q1 präzisiert): `Expr`, `Stmt`, `Block { stmts }`, `Decl` (`FnDecl`, `StructDecl`, `ClassDecl`, `EnumDecl`, `InterfaceDecl`, `ExtendDecl`, `LetDecl`, …), `Type`, `Pattern`, `Ident { name, span }`, `Literal`, `Param`, `Attribute`, `Span { file, line, column }`; jeder Knoten `span`, `Debug`, `toSource(): string`; Bauen über `quote`/`#{}`, `Ident.fresh("hint")` (Hygiene), `parseExpr(s: string): Expr` für DSL-Strings (Q5); Gehen über `children()`/`walk(f)`/`map(f)`; Diagnosen `error(node, msg): never`, `warn(node, msg)`, `note` | Nim `macros`, Rust `syn` (ohne Token-Strom) |
| M3 | **Stabilität**: AST-Enums `@NonExhaustive` (neue Knotenarten je Minor; Makro-`match` braucht `_`); `std.syntax` mit dem Compiler versioniert, kein Ring | — |
| M4 | **Nicht**: Laufzeitreflexion, `typeof` zur Laufzeit, dynamischer Aufruf (R6) — Feldnamen zur Laufzeit nur als `comptime`-synthetisierte Tabelle (`Debug`, `Codec`) | Zig |

---

**Bereich 10 ist damit vollständig entschieden** (B1–B13, 2026-09-29). Nachträge in anderen
Bereichen aus diesem: 03 A2 (`StringView`), 04 D6 (`showTo`), 04 D7 (`Clone` folgt der
Konformanz), 05 K7/R7, 08 S3a, 01 K7a, 09 Q1 (`Box` als `comptime`-Wert).
Es bleiben 11 (Werkzeuge und Interop) und 12 (Migration).
