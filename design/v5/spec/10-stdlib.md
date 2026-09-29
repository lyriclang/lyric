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
| COL-* Slices, Ranges, Index, Iterator-Ende | `Slice<T>`/`str` als Views, Range-Typen als Structs, `Index<K>{type Output}`, `next(): ?Item`, `??T` erlaubt | 02 M-, 03 A/N/O |
| Operator-Interfaces, Synthese | `Add<Rhs=Self>{type Out}`, `Equatable`/`Hashable`/`Ordered`/`TotalOrder`/`Display`/`Debug`, Konformanz ohne Körper | 04 D6/D7 |
| Prelude | Modul `std.prelude`, Liste hier (B2) | 07 I8 |
| Tests | im Paket (`@Test` neben dem Code oder `tests/`-Root) | 07 V4 |

## Fragen

Reihenfolge: **B1 zuerst** (sie entscheidet, was es überhaupt gibt), dann Schnitt/Namen (B2–B3),
dann die Kernverträge (B4–B6), dann die Fachmodule (B7–B12), zuletzt `std.meta`/`std.syntax` (B13).

## B1 — Umfang und Ringe (Bibliotheks-Umkehr): **offen**

SL-01, SL-16, SL-18. Kommen Regex, HTTP, TLS, Zeitzonen, Kompression, Krypto, CLI-Parser,
Logging, Terminal in `std`? Nach welcher **Regel**? Ein Ring oder zwei? Wird `std` mit dem
Compiler versioniert?

## B2 — Modulschnitt, Prelude, Namenskonventionen: **offen**

Modulliste 5.0 (Umzüge: Zeit, `iter`/`collections`, `bytes`/`encoding`, `io.*`, `task`,
`sync`); Prelude-Liste (07 I8); Namensgesetz (`stdlib-2.md` §2 als Vorlage; SL-10, SL-35:
Felder Substantive, Methoden Verben; Suffix beschreibt die Ausgabe, nie die Eingabe;
`of`/`from`/`ofX`; `toX`/`asX`); **ein Aufrufstil** (SL-09: Methode, wo generische Extends es
tragen; frei nur `copy(r, w)`-artige Zweistelligkeit).

## B3 — Antwortformen und Panik-Regel: **offen**

Nach 05 E1: eine werfende Funktion je Operation; wo bleibt die stille `?T`-Form (`parseInt`,
`Map.get`, `env`)? `bool` als Antwort? Panik-Regel („Argument aus Daten panikt nie; Argument
aus dem Programmtext darf") und ihre Fälle (`split("")`, `substring`, Index).

## B4 — Kern-Interfaces und Verträge in `std.core`: **offen**

Vollständige Liste und Namen: `Equatable`, `Hashable`, `Ordered`, `TotalOrder`, `Display`,
`Debug`, `Default`, `Clone`, `Parse`, `Into`/`From`, `Add`…`Neg`, `Index`/`IndexSet`,
`Iterator`/`Iterable`/`FromIterator`, `Resource` (05 R1 — Name), `Error`, `Format`
(Formatsprache), `FromLiteral` (COL-13). Hash-Vertrag (SL-26: `equals ⇒ hash`; nicht stabil
über Versionen; **Hasher-Modell**: `hash(): int` oder `hash(h: &Hasher)` streaming). Ordnungs-
vertrag (Sortierstabilität COL-39).

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
