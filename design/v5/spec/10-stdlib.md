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
| Kern | `prelude`, `core` (Kern-Interfaces, `Num`-Familie, `Error`, `Result`, `Box`, `Slice`, `str`, Ranges, `?T`-Member, Art-2-Attribute — `result`/`option` gehen auf), `iter`, `collections`, `string`, `fmt`, `math` |
| Daten/Kodierung | `encode` (Encode/Decode-Paar, Name B10), `json` (RFC 8259), **`toml`** (Manifest), `encoding` (Base64/Hex/UTF-16/LE-BE; `bytes` geht auf), **`compress`** (RFC 1950–1952), **`regex`** (RE2-Syntax, lineare Zeit), `hash` (`Hasher`, SipHash, FNV, CRC32), `crypto` (SHA-2/SHA-1/MD5, HMAC, `secureRandom`; Ed25519/ChaCha20-Poly1305 Tür), `random` (PCG), `time` (RFC 3339, TZif-Parser RFC 8536 über die System-Zonendatenbank) |
| I/O und System | `io` (`Reader`/`Writer`/`Seek`, Puffer-/Text-Hüllen, `IoError`, `copy`, Speicherströme, `stdin/stdout/stderr`, `print`-Familie, `readLine`, `isInteractive` — `io.stream`/`io.error`/`io.console` gehen auf), `fs`, `path`, `net`, **`url`** (RFC 3986), **`http`** (HTTP/1.1 Client **und** Server über `Reader`+`Writer`; kein TLS, HTTP/2 Tür), `os` (ohne Zeit), `process`, **`term`** (ECMA-48, TTY-Erkennung; Raw-Mode Tür) |
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
| Text | `Display`, `Debug`, `Format`, `Parse { static fn parse(s: StringView): Self throws [ParseError] }` |
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

## B5 — Zahlen: **offen**

SL-07 mit 04 D4: kein `abs(int)`/`abs(float)` — also `interface Num`/`Integer`/`Float` mit
statischen Membern (`zero`, `one`, `min`, `max`, `parse`) und `abs<T :: [Num]>`; alle 13 Typen
gleich bedient; `checked*`/`saturating*`/`+%`-Familie als Methoden; Konstanten (`int.max`);
`parse`/`format` je Typ; BigInt nein/Tür.

## B6 — Iterator-Protokoll, Adapter, Generatoren: **offen**

`Iterator { type Item; fn next(): ?Item }`, `Iterable { type Iter; fn iter(): Iter }`; Adapter
als generische Extends auf `Iterator` (COL-12); Terminatoren mit Constraint (`sum`, `min`,
`sorted`, `joinToString`); `collect` über `FromIterator`; `Peekable`; `enumerate`/`chunks`/
`windows` als Methoden (assoziierte Typen machen es möglich); `sequence { yield }` ist ein
`Iterator` (COL-26); Iteration über `Map` (SL-19) und `Deque`; Reihenfolge-Vertrag (COL-28);
Mutation während Iteration (COL-16); Generator-Freigabe bei `break` (COL-25).

## B7 — Container: **offen**

`List`/`Map`/`Set`/`Deque` Oberfläche (COL-14, `stdlib-2.md` §5), `SortedMap`/`SortedSet`
(B-Tree) ja/nein, `entry`-API vs `getOrInsert`/`update`, Kapazität, Hash-Tabellen-Strategie
(Robin-Hood/Swiss, Verdichtung), `T[]`/`Slice<T>`-Member, Collection-Literale für eigene Typen
(COL-13 `FromLiteral`), Arrays als Map-Schlüssel (COL-30), `x in xs` (COL-31).

## B8 — I/O: Ströme, Dateien, Netz, Prozesse, Konsole: **offen**

`Reader`/`Writer`/`Seek` mit `Slice<uint8>` (SL-03), `BufReader`/`BufWriter`/`TextReader`
(SL-23: Codepoint-Split, `Utf8Error` mit Offset, BOM nur auf Wunsch), `IoError` als **ein**
Fehlertyp mit `kind` (SL-22), Handles als Structs/Klassen mit `Resource`, `file`/`net`/
`process`/`console`/`path`-API (Options-Structs statt Builder), `HostOptions.Input`-Frage
entfällt (kein Embedding).

## B9 — Strings und Unicode: **offen**

`string`/`str`/`char`-Oberfläche (Codepoints; Byte-Indizes im View), Unicode-Tiefe (SL-12/33:
`charClass`-Native, Simple Case Mapping invariant, `isAlpha` Unicode + `isAsciiAlpha`,
Normalisierung/Grapheme im Ring), `StringBuilder`, Formatsprache-Implementierung
(`Format`-Interface, Spec `{x:>8.2f}` — 08 Y7), `parse` je Typ.

## B10 — Serialisierung: **offen**

SL-14: `Encode`/`Decode`-Paar mit Formaten als Backends, Ableitung per Makro-Attribut (09
`@Derive`-Familie) statt Handschrift; `std.json` als erstes Backend; TOML (Manifest) als
zweites; kein Format ohne das Paar.

## B11 — Zeit, Zufall, Hash, Bytes, Encoding: **offen**

`Instant`/`Duration`/`Monotonic`/`Date` (UTC, proleptisch gregorianisch), Zeitzonen nach B1;
`Random` (PCG/ChaCha, `seeded`/`fresh`, Rejection Sampling), `secureRandom`; Digests + HMAC;
`std.bytes` (LE/BE, `BytesBuilder`) vs Auflösung in `std.encoding`; Base64/Hex.

## B12 — Test und Bench: **offen**

SL-15/32: Assertionsliste, `assertThrows` (Lambda-Form), tabellengetriebene Tests, `@Bench`,
`assertEq(actual, expected)`-Reihenfolge, Diff-Ausgabe; Property-Tests/Snapshots im Ring;
Setup/Teardown nein (Test = Funktion).

## B13 — `std.meta` und `std.syntax`: **offen**

Was Bereich 9 braucht, nichts mehr: `std.meta` (Typ-Introspektion zur Compile-Zeit: Felder,
Varianten, Konformanzen, Attribute), `std.syntax` (`Expr`, `Stmt`, `Block`, `Decl`,
`StructDecl`, `FnDecl`, `Ident`, `Type`, `Literal`, `Pattern`; `quote`-Ergebnistypen;
`error(node, …)`).
