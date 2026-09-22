# stdlib 2 — Zielbild der Lyric-Standardbibliothek für 4.5 (additiv) und 5.0 (breaking)

Stand: 2026-09-22, Basis dc32100c (v4.4.1). Verfasser: stdlib-redesign (Evolution-Team 3).
Grundlage: stdlib-review (Team 2, 43 Einträge: 12 Bugs, 10 Minor, 5 Major), Prototypen 01
(Result), 09 (Slices), 18 (arrayOf), 21 (Option), `stdlib-field-visibility.md`. Diese Analyse
wird hier nicht wiederholt; wo ein Befund gebraucht wird, steht seine Nummer.

Der Prototyp zu diesem Dokument liegt auf dem Branch `worktree-agent-aa7b5e912a78e6f2d`
(fünf Commits, 208/208 stdlib-Tests grün); Abschnitt 17 sagt, was davon steht und was
Design bleibt.

---

## 0. Die drei Sätze, die alles andere tragen

1. **`throws` bleibt der Mechanismus; `Result<T, E>` ist ein Wert.** CONTRIBUTING Regel 2
   („ein Mechanismus pro Konzept“) ist nicht verletzt: ein Result wird geworfen (`orThrow`)
   oder aus einem Wurf gewonnen (try-Ausdruck), aber nie *propagiert* — Propagation bleibt
   `throws`. Rust braucht `?`, weil es kein `throws` hat; Lyric hat es und braucht `?` nicht.
2. **Das Iterator-Protokoll bleibt `next(): ?T`.** Ein `Option<T>`-Enum als zweite Absenzform
   wäre der teuerste Umbau der Sprache für den kleinsten Gewinn (Prototyp 21); die Fälle, in
   denen `?T` in Generics nicht reicht (`List<?T>`, `Iterator<?T>`), sind Compiler-Lücken, die
   ohne zweite Absenzform schließbar sind (Abschnitt 15).
3. **Die Antwortform steht im Namen.** `x` liest (`?T`), `xOrThrow` sagt warum (`throws`),
   `xOrErr` gibt den Grund als Wert (`Result`), eine Operation antwortet `bool`, ein
   Programmierfehler panikt. Das ist §11-fähig als Konvention, weil jede Form eine Frage
   beantwortet, die die anderen nicht beantworten.

---

## 1. Modulstruktur

### 1.1 Ist (4.4): 23 Module, drei Unschärfen

| Unschärfe | Befund | Wohin |
|---|---|---|
| Zeit an zwei Orten: `std.os.nowMillis/nowNanos/sleep` und `std.time.Instant.now` (zwei Natives, ein Wert) | Major 3 | `std.time` |
| `std.iter` darf `std.collections` nicht importieren → `collectArray` quadratisch, `collect` wohnt in collections | Major 3 | bleibt getrennt; `toArray()` ist Methode, `collect` bleibt in collections (siehe 1.3) |
| `std.io.error` als drittes Modul für einen Typ, `std.bytes` mit 3 Funktionen | Inventar | bleiben; `std.bytes` wächst |

### 1.2 Soll 4.5 (additiv): 25 Module

```
std.core        panic/assert/todo/unreachable, Exception, Display/Equatable/Hashable/Ordered,
                Add/Sub/Mul/Div/Into, Attribut-Anker (OnModule/OnType/OnFunction/OnMethod,
                WithArg, Deprecated, NonExhaustive), combineHash                      [erweitert]
std.result      Result<T, E> + Kombinatoren + Brücken                                [NEU]
std.option      Funktionen über ?T (unverändert; orElse, getOrCall kommen dazu)
std.string      + ParseError/ParseErrorKind, parseIntOrErr, Unicode-Klassifikation,
                lines(), removePrefix/removeSuffix, capitalize, reverse, equalsIgnoreCase
std.fmt         + formatHex-Doku (Wert vs. Bits)                                     [erweitert]
std.math        + intMax/intMin, checked*/saturating*, powInt ohne Überlauf         [erweitert]
std.collections + List.of/slice/filter/forEach/…, mapList, Map.keys()/entries()/
                getOrInsert/update/fromEntries, Set.of/toArray, Deque.toArray,
                groupBy/sortListByKey/sortArray/maxBy/minBy/slice, Verdichtung       [erweitert]
std.iter        + Terminatoren als Methoden, skipWhile/stepBy/inspect/dedupBy/zipWith [erweitert]
std.hash        sha256/sha1/md5/crc32, *Hex, hashCombine/hashAll                      [NEU]
std.random      + Random.fresh(), Rejection Sampling                                 [erweitert]
std.time        + Duration.ofHours/ofDays, Instant.minus, Monotonic, sleep(Duration),
                Date, fromRfc3339 tolerant                                          [erweitert]
std.test        + assertNotEq/assertNull/assertNotNull/assertClose/assertLess/
                assertContains/fail (assertThrows wartet auf typed throws)           [erweitert]
std.task        + Channel<T>, join, timeout, sleep(Duration), @NonExhaustive Wait      [erweitert]
std.json        + path(), ToJson/FromJson-Anker, parseOrErr                          [erweitert]
std.encoding    + hexDecodeOrErr, base64DecodeOrErr                                  [erweitert]
std.bytes       + toInt/fromInt (LE/BE), equals, BytesBuilder                          [erweitert]
std.io.path     + normalize/relative/components/absolute                              [erweitert]
std.io.file     + textOrErr, walk/removeAll/modifiedAt/writeLines                    [erweitert]
std.io.console, std.io.error, std.io.stream, std.io.net, std.os, std.process, std.build
```

**Was in 4.5 wandert, wandert per Alias:** `std.time.sleep(d: Duration)` und
`std.time.Monotonic.now()` kommen NEU dazu; `std.os.sleep/nowMillis/nowNanos` bleiben und
bekommen `@Deprecated { until = "5.0" }`. Die Native-Namen (`std.os.nowMillis`) bleiben im
Bytecode-Vertrag (§11 Punkt 3), weil die Registry — wie bei `std.string.raw*` — zwei Namen an
eine Host-Funktion binden kann.

### 1.3 Warum `std.iter` und `std.collections` NICHT zusammengelegt werden

Der Review schlug die Zusammenlegung vor, damit `Iterator.toList()` Methode werden kann. Der
Preis ist ein Modul mit 2000 Zeilen, das jeder Import von `List` zieht, und der Verlust der
Schicht „Iteration ohne Container“ (std.string, std.option, std.io.console importieren nur
`std.iter`). Die Lösung im Prototyp: `toArray()` ist Methode (quadratisch, dokumentiert),
`collect(it): List<T>` bleibt in collections und wird in 5.0 zur Extension-Methode
`Iterator<T>.toList()` in `std.collections` — sobald die Sprache Extensions auf Interfaces
mit Typargumenten erlaubt (heute LYR-SEM0047 „plain named type“; new-features:
conditional conformance). Bis dahin: kein Modulumbau.

**Vorbild:** Rust trennt `core::iter` und `alloc::vec` genauso und legt `collect` als
generische Trait-Methode über `FromIterator` an die Grenze; Kotlin hat `Sequence` in
`kotlin.sequences` und `toList()` als Extension. Python trennt `itertools` von `list`. Lyric
folgt Rust/Kotlin; die Zusammenlegung wäre Go (ein `slices`-Paket für alles) — vermeiden.

### 1.4 Soll 5.0 (breaking): Umzüge mit abgelaufener Uhr

- `std.os.sleep/nowMillis/nowNanos` fallen weg (`std.time`).
- Die freien Iterator-Terminatoren (`fold(it, …)`, `count(it)` …) und `keys(m)/values(m)/entries(m)`,
  `listContains/listIndexOf` fallen weg (Methoden seit 4.5).
- `std.io.file.listDir`-Muster: die `OrThrow`-Zwillinge bleiben (siehe 3.3), die `OrErr`-Formen
  bleiben; was fällt, ist `lastErrorKind/lastErrorDetail` als globaler Zustand (3.4).

---

## 2. Namenskonventionen (verbindlich, §11-fähig)

| Regel | Beispiel | Vorbild / Gegenbeispiel |
|---|---|---|
| Typen PascalCase, alles andere camelCase | `List<T>`, `pushAll`, `intMax` | Kotlin, Swift, .NET (Go: exportiert = Großbuchstabe → nein) |
| Funktionen sind **Verben** oder Verb-Objekt; Prädikate `is`/`has`/`contains`/`can` | `parseInt`, `isBlank`, `containsKey` | Kotlin/Swift; Rust lässt Prädikate ohne `is` zu (`is_empty` aber `contains`) — Lyric: `is` nur für Zustandsprädikate, `contains/starts With` ohne |
| Konstruktoren auf dem Typ: `empty()`, `of(array)`, `from<Quelle>(x)`, `seeded(n)`, `fresh()` | `List<int>.of([1,2])`, `Map.fromEntries(pairs)`, `Instant.ofEpochMillis` | Kotlin `listOf`, Java `List.of`, Rust `Vec::from`; **`of`** = aus Elementen, **`from`** = aus einer anderen Repräsentation, **`ofX`** = aus einer Einheit (`Duration.ofSeconds`) |
| Konversionen: `toX()` kopiert/materialisiert, `asX()` reinterpretiert ohne Kopie (nur wenn O(1)), `into()` nur als Operator-Anker | `toArray()`, `toChars()`, `asInt()` (JsonValue: O(1)), `x as T` | Rust `to_*`/`as_*`/`into` — Lyric übernimmt die Rust-Dreiteilung, aber `into` NIE als API-Name |
| Plural für Sammlungen, Singular für Elemente | `keys()`, `entries()`, `lines()`, `first()` | Rust/Kotlin |
| Antwortform als Suffix: `x` / `xOrThrow` / `xOrErr`; nie `try`-Präfix | `text`, `textOrThrow`, `textOrErr` | .NET `TryParse` (bool + out) — vermeiden; Swift `try?` ist Sprache, nicht Name |
| Typsuffixe (`absInt`, `sumFloat`) sind ein 3.0-Relikt: **keine neuen**; 5.0 überlädt (Abschnitt 2.1) | neu: `checkedAdd(int)` ohne Suffix, weil es nur für int existiert | — |
| Kein `get`-Präfix für Lesen von Zustand, aber `get(key)` für Nachschlagen | `length()`, `capacity()`, `m.get(k)` | Kotlin (Property-Stil), Rust `len()`; Lyric-Methoden haben Klammern, weil `length()` O(n) sein darf |
| Mutation heißt, was sie tut, und gibt `void` oder den entfernten Wert; Kopie hat `to`-/Partizip-Namen | `reverse()` (in place) vs. `toReversed()`; `sortList` vs. `sorted` (5.0) | Python `list.sort()`/`sorted()`, Kotlin `sort()`/`sorted()`, JS `toReversed()` |
| Boolesche Parameter vermeiden: Enum oder zwei Funktionen | `trimStart()/trimEnd()` statt `trim(side: bool)` | Swift API Design Guidelines |
| Reihenfolge in Assertions: `(actual, expected)` — und bleibt so | `assertEq(got, 4)` | JUnit ist `(expected, actual)`, Rust `assert_eq!(a, b)` symmetrisch, Kotlin `assertEquals(expected, actual)` — Lyric hält sein Ist (Review-Befund „unkonventionell“: ja, aber ein Wechsel bricht jede Meldung) |

### 2.1 Typsuffixe → Überladung (5.0, Major 2)

new-features hat geprüft: freie `pub`-Funktionen desselben Moduls mit verschiedenen
Parametertypen überladen seit 3.0. 5.0 definiert `abs(int)`/`abs(float)`, `min`, `max`,
`clamp`, `sign`, `sum(Iterator<int>)`/`sum(Iterator<float>)`, `format(int, spec)`/…, und die
Suffixnamen tragen `@Deprecated { until = "6.0" }`. Offen (an new-features): Import-Auflösung
`import std.math { abs }` bringt BEIDE Überladungen? §4.2 „one name, one binding“ muss dafür
eine Überladungsmenge als eine Bindung zählen. **Nicht in 4.5**, weil ein Name, der heute
`int` nimmt, morgen beides nimmt, nur additiv ist, solange kein Aufruf mehrdeutig wird —
und Literal-Adaption (`abs(3)`: int oder float?) genau das macht.

---

## 3. Antwortformen (§11-Anker)

### 3.1 Die fünf Formen und ihre Frage

| Form | Frage | Beispiel | Wann |
|---|---|---|---|
| `?T` | *Gibt es einen Wert?* | `parseInt`, `Map.get`, `file.text` | Absenz ist eine gewöhnliche Antwort, und die Ursache ist die ganze Wahrheit ODER dem Aufrufer egal |
| `bool` | *Ist es passiert?* | `writeText`, `Set.add`, `exists` | Operation ohne Wert; Prädikat |
| `T throws E` | *Warum nicht?* — an Ort und Stelle | `textOrThrow`, `parseOrThrow` | Der Grund wird sofort behandelt oder weitergereicht |
| `Result<T, E>` | *Warum nicht?* — als Wert | `parseIntOrErr`, `textOrErr` | Der Grund wird gesammelt, gemappt, später behandelt |
| Panik | *Wer hat sich geirrt?* | `List.get(-1)`, `split("")`, `o!` | Programmierfehler: Vorbedingung verletzt, Invariante gebrochen |

**Regel für Panik (Major 4, Empfehlung):** Ein Argument, das aus DATEN stammen kann, panikt
nie — es antwortet `?T`/`bool`/`Result`. Ein Argument, das der Programmierer schreibt (ein
Index, ein Format-Spec-Literal, ein Trenner), darf paniken. Konsequenz für 5.0: `split("")`
bleibt Panik (Trenner ist Programmiererwahl, Python wirft `ValueError`), `substring` außerhalb
bleibt Panik (Rust panikt, Python schneidet — Lyric folgt Rust), `readSome(max <= 0)` wird
`?uint8[]` mit `null` (max kann berechnet sein), `formatInt` mit ungültigem Spec bleibt Panik.

### 3.2 Was in §11 steht (Vorschlag, Punkt 5 neu)

> 5. **Die Antwortformen.** Eine Funktion der Bibliothek antwortet in genau einer der
> fünf Formen (`?T`, `bool`, `throws E`, `Result<T, E>`, Panik), und ihr Name sagt
> welche: ohne Suffix die stille, `OrThrow` die werfende, `OrErr` die Wert-Form. Eine Panik
> ist einer verletzten Vorbedingung vorbehalten, die aus dem Programmtext folgt, nie aus
> Daten.

Das ist eine Konvention über Bibliotheksoberfläche, keine Sprachregel — §11 sagt heute schon
„Everything else is library surface“; der Absatz macht die Oberfläche vorhersagbar, ohne die
Konformanz zu berühren.

### 3.3 Fehlermodell: die 45 OrThrow-Zwillinge und was aus ihnen wird

**Diagnose (Major 1):** Jede fehlbare Operation existiert zweimal, und die stille Form liest
den Grund hinterher aus einem globalen `lastErrorKind()`-Zustand (vier Kopien: file, net,
process, stream). Der Zwilling ist nicht das Problem — er ist die einzige Form, die mit dem
Sprachmechanismus (`throws`, typisiertes catch, Zero-Cost-Propagation) zusammenspielt. Das
Problem ist (a) der globale Zustand, (b) die fehlende Wert-Form.

**4.5 (im Prototyp):** `std.result` + `OrErr`-Formen für parseInt/text/parse(json)/hexDecode.
Drei Formen pro Operation sind mehr Namen, nicht weniger — bewusst: additiv geht nichts
anderes, und jede Form beantwortet eine andere Frage (3.1).

**5.0 (Design):** Der try-Ausdruck von new-features (Prototyp 04b: `try e catch (x: E) Expr`,
`try? e` → `?T`) macht die Wert-Form aus der werfenden Form ableitbar:

```lyr
let doc = try? json.parseOrThrow(text);                 // ?JsonValue   (= parse)
let r   = try Result.Ok(json.parseOrThrow(text))
          catch (e: JsonError) Result.Err(e);           // Result<JsonValue, JsonError>
```

Ich empfehle new-features ZUSÄTZLICH `try e` ohne catch → `Result<T, E>` mit E aus der
throws-Klausel (Swift: `Result { try f() }`, Kotlin: `runCatching`). Dann hat 5.0 **eine
Funktion pro Operation**, und sie wirft; die stille Form ist `try?`, die Wert-Form `try`. Die
`OrErr`-Namen aus 4.5 werden dann mit Uhr deprecated — sie waren die Brücke, bis der
try-Ausdruck da ist. Ob auch die stillen `?T`-Formen (`text`, `parse`) fallen, entscheidet
ein Zählen: `file.text(p) ?? ""` ist die häufigste Zeile in jedem Lyric-Programm; sie zu
`try? file.text(p) ?? ""` zu machen ist mechanisch, aber jede Datei trifft es. **Empfehlung:
stille Formen bleiben, wo `null` die ganze Wahrheit ist (parseInt, Map.get, env); wo ein
Grund existiert (file, net, json, encoding, process), wird die werfende Form die einzige
benannte, und `try?` ist die stille.** Migration in 12.

| Sprache | Modell | Lyric übernimmt | vermeidet |
|---|---|---|---|
| Rust | `Result<T,E>` überall, `?` propagiert, `.ok()` → Option | Result-Kombinatoren, `ok()`, carrier-plus-kind (`io::Error`/`ErrorKind`) | `?` (redundant zu throws) |
| Go | `(v, err)` Tupel, kein Typ | nichts | zwei Rückgabewerte ohne Typ, `if err != nil` |
| Swift | `throws` + `try`/`try?`/`try!` + `Result` | die Dreiteilung try/try?/Result — das ist exakt Lyrics 5.0-Bild | `try!` (Lyric: `expect`) |
| Kotlin | Exceptions ungeprüft + `Result<T>` nur für Throwable | `runCatching`-Idee als `try`-Ausdruck | ungeprüfte Exceptions, E fest auf Throwable |
| Python | Exceptions, EAFP | nichts | — |
| .NET | Exceptions + `TryParse(out)` | nichts | out-Parameter, bool-Rückgabe mit Seitenkanal |

### 3.4 `lastErrorKind`-Globalzustand abschaffen (5.0, VM-Vertrag)

Heute: `text(path): ?string` (Native) + `lastErrorKind(): int` + `lastErrorDetail(): string`
(Natives), die werfende Form ruft die stille und liest danach. Bei zwei Tasks, die je ein
`readSome` machen, kann der Grund des einen vom anderen überschrieben werden, bevor er
gelesen ist (nur, wenn zwischen Fehlschlag und Lesen ein Yield liegt — heute nicht, aber ein
Vertrag, der an „nichts läuft dazwischen“ hängt, ist keiner).

**Design:** Ein Native pro Operation, das ein Struct liefert: `readTextChecked(path):
(value: ?string, kind: int, detail: string)` per `RegisterStructReturning` (existiert, Zeile
141). Die stille Form ist `.value`, die werfende baut `IoError` aus `.kind/.detail`, die
Wert-Form desgleichen. Der Native-Namensraum verliert acht `lastError*`-Namen und gewinnt
keinen: die `Checked`-Natives ERSETZEN die stillen (der Bytecode-Vertrag ändert sich → 5.0).
Kein Sprachfeature nötig.

---

## 4. Iterator-Protokoll

### 4.1 Entscheidung: `next(): ?T` bleibt

Begründung (Prototyp 21): `Option<T>` als Protokoll bricht jede `next()`-Implementierung
(5.0), führt zwei Absenzformen ein, und gewinnt genau einen Fall — `Iterator<?T>`. Dieser
Fall ist heute ein Compiler-Verbot (LYR-SEM0091), also kein stiller Bug, und er ist mit
`compact()` und mit der Lösung aus 15.2 (`List<?T>` über Backing-Arrays ohne `??T`) abgedeckt.
Rust braucht `Option`, weil es kein `?T` hat; Kotlin hat `?T` UND `Sequence<T?>` (der
Iterator kennt `hasNext()`), zahlt aber mit zwei Methoden, die auseinanderlaufen können.
Lyric zahlt mit „kein `Iterator<?T>`“ — der billigere Preis.

### 4.2 Terminatoren als Default-Methoden (4.5, im Prototyp)

Alle Terminatoren, die von `T` nichts verlangen, sind Methoden: `fold<A>`, `reduce`, `count`,
`first`, `last`, `nth`, `any`, `all`, `none`, `find`, `position`, `countWhere`, `forEach`,
`toArray`. Nicht Methoden, mit Grund: `sum`/`sumFloat`/`minValue`/`maxValue` (Constraint auf
T — Rust löst es mit `Sum`/`Ord`-Bounds AUF der Methode: `fn sum<S: Sum<Self::Item>>` — Lyric
kann heute keinen Constraint auf einen Interface-Typparameter in einer Default-Methode legen;
Anforderung an new-features, niedrig); `enumerate`/`chunks` (Elementtypwechsel in
nicht-generischer Methode → Monomorphisierung terminiert nicht; iter.lyr:60-68; Rust hat
dasselbe Problem nicht, weil `Enumerate<I>` ein eigener Typ pro Aufruf ist, den der Trait
per assoziiertem Typ zurückgibt — Lyric ohne assoziierte Typen bräuchte eine Zyklusprüfung in
der Instanztabelle, an new-features gemeldet, Priorität niedrig, weil `enumerate(it)` als
freie Funktion + Tupel-Destructuring im for-Kopf (pattern-lambda) den Fall gut abdeckt).

### 4.3 Adapter-Vollständigkeit

| Adapter | 4.4 | 4.5 (Prototyp) | 5.0 | Vorbild |
|---|---|---|---|---|
| map, filter, take, skip, takeWhile, zip, chain, flatMap | ✓ | ✓ | ✓ | alle |
| skipWhile, stepBy, inspect, dedupBy, zipWith | – | ✓ | ✓ | Rust `skip_while/step_by/inspect`, itertools `dedup_by`, Haskell `zipWith` |
| enumerate, chunks | frei | frei | frei bzw. Methode, wenn Zyklusprüfung da | Rust Methoden |
| windows(n) | – | Design | frei `windows(it, n): Iterator<T[]>` | Rust `slice::windows` (nur auf Slices!), itertools `tuple_windows` |
| sorted / sortedBy(key) | – | Design | frei (Constraint `Ordered`), materialisiert in List | Kotlin `sorted()`, Python `sorted()` — Rust hat es NICHT auf Iterator (nur itertools); Lyric folgt Kotlin, weil die Materialisierung im Namen steht (`sorted` liefert `List<T>`, nicht `Iterator<T>`) |
| groupBy(key) | – | ✓ (auf List) | auch über Iterator | Kotlin `groupBy` (eager, Map) — nicht Rust `group_by` (lazy, nur benachbart; das ist Lyrics `dedupBy`-Verwandter) |
| partition(pred) → (List, List) | – | Design | ✓ | Rust/Kotlin |
| peekable | – | Design | ✓ Klasse `Peekable<T>` mit `peek(): ?T` | Rust; Parser brauchen es |
| cycle, scan, product, average, sumBy/minBy/maxBy | – | minBy/maxBy auf List | scan als Methode, Rest frei | Rust/Kotlin |
| toList/toSet/toMap | collect(it) | collect(it), Set.of/Map.fromEntries | Extension-Methoden in collections (braucht conditional extension) | Kotlin |
| joinToString(sep) | join(collectArray(map(fromInt))) | Design | frei `joinDisplay<T :: [Display]>(it, sep)` | Kotlin; Rust `itertools::join` |

**Lambdas** (pattern-lambda, abgestimmt): Kurzsyntax mit implizitem `it` nur bei einem
Parameter, Trailing-Lambda `xs.map { it * 2 }`; wichtiger ist Inferenz der
Lambda-Parametertypen aus dem erwarteten `fn(T) -> U` (heute erzwingen die Tests
`(n: int) =>` und `map<int>(…)`). Beides ist Sprache; die stdlib-Signaturen ändern sich nicht.

---

## 5. Container-API

### 5.1 List

| 4.5 (Prototyp) | 5.0 | Vorbild |
|---|---|---|
| `of(array)`, `pushAll(array)`, `pushAllFrom(list)`, `slice(from, to)`, `filter`, `forEach`, `removeWhere`, `toReversed`; frei `mapList`, `listRemove`, `sortListByKey`, `maxBy/minBy`, `groupBy` | `map<U>` als Methode (braucht generische Methoden auf generischen Typen — new-features), `contains/indexOf/remove` als Methoden (braucht Constraint auf Methode, nicht Klasse: Rust `impl<T: PartialEq> Vec<T> { fn contains }` — Lyric: „Extension mit Constraint“), `sorted()`, `join(sep)` für `T :: [Display]` | Kotlin `MutableList`, Rust `Vec`, JS `toReversed` |
| `slice(from, to)` exklusiv, Panik bei ungültigen Grenzen | `xs[a..b]` (Sprache, Prototyp 09) desugart auf `slice` | Python (Kopie), NICHT Go/Rust (View) — Wertsemantik der Arrays |

`arrayOf(n, f)`/`arrayFilled(n, x)` (Prototyp 18): OHNE Native schreibbar über
`List<T>.empty()` + push + `toArray()` — eine Kopie mehr, kein VM-Umbau. Kommt als
`std.collections.arrayOf` (4.5, nicht im Prototyp — trivial nach dem Container-Paket).
Das Native `rawArrayAlloc` bleibt die 5.0-Option für `List<?T>` (15.2).

### 5.2 Map

| 4.5 (Prototyp) | 5.0 | Vorbild |
|---|---|---|
| `keys()/values()/entries()` Methoden, `getOrInsert(k, make)`, `update(k, f)`, `fromEntries(pairs)`, `capacity()`, **Verdichtung** (Tombstone-Schwelle → Rehash bei gleicher Größe, wenn live ≤ 3/8) | `entry(k)`-API? NEIN — `getOrInsert/update` decken 95 % ohne einen Entry-Typ | Rust `entry().or_insert_with` (übernommen als getOrInsert), Kotlin `getOrPut` (Name: Lyric sagt `Insert` wie `insert`), Python `setdefault`, CPython-dict-Rebuild bei Tombstone-Last |
| — | `SortedMap<K :: [Ordered<K>], V>` als B-Tree? | Rust `BTreeMap`, Java `TreeMap`; .NET `SortedDictionary`. **Empfehlung: 5.x als `std.collections.sorted`, erst wenn ein Nutzer es braucht** — Go hat keine, Python keine (nur `bisect`), und Lyric-Programme sind heute Skripte/Tools |

Warum **Verdichtung bei gleicher Größe** statt „Tombstones bei remove zählen“: Rust hashbrown
macht `rehash_in_place`, CPython baut das dict neu, wenn `used + dummies` die Schwelle
erreicht; beides ist O(n) pro Schwelle, amortisiert O(1) pro Operation. Ein `remove`, das
Tombstones sofort auflöst (backward-shift deletion, wie in Robin-Hood-Tabellen), wäre besser,
ändert aber die Probe-Invariante — für 5.0 prüfbar, nicht für 4.5.

### 5.3 Set, Deque

`Set.of/addAll/toArray/toList` (Prototyp); `union/intersect/difference/isSubset` bleiben
frei (5.0: Methoden `a.union(b)` — heute geht das, weil beide Seiten `Set<T>` sind; bewusst
noch nicht, weil der Review ein Paket wollte und Set-Operationen als Methoden ein zweiter
Aufrufstil neben den freien wären; 5.0 entscheidet einheitlich). `Deque.toArray()` (Prototyp);
Iteration bleibt aus (Guide: „a queue is drained, not walked“).

### 5.4 Sichtbarkeit der stdlib-Felder (Prototyp 17 / stdlib-field-visibility.md)

Übernommen: 19 Vertragsfelder werden `pub` (Exception.text, Deprecated.message/until,
Utf8Error.offset, EncodingError.offset/expected, JsonError.line/column/offset/expected,
IoError.kind/path/detail, Packet.bytes/host/port, ParseError.kind/offset — neu), 85 privat.
Die vier Fremdzugriffe (Map*/SetIterator lesen `states/keyColumn/valueColumn/items`) bleiben im
Prototyp; drei Wege für 5.0: (a) `pub(module)` (Rust `pub(crate)`), (b) verschachtelte Klassen
(C#), (c) Snapshot-Iteration (`keys()` kopiert in ein Array — Go/Java-Semantik „Mutation während
Iteration ist definiert“, Kosten O(n) Speicher). **Empfehlung (a)**, an new-features gemeldet;
(c) als Fallback ohne Sprachänderung.

---

## 6. Display und Debug für alle Typen

**Ist:** `println([1,2,3])`, `println(?int)`, `println((1,"a"))` passieren die Sema-Constraint
und sterben im IR mit Fehlerort console.lyr:51 (Bug 8, HIGH). List/Set/Map/JsonValue/IoError
haben kein Display.

**Blocker:** bedingte Konformanz. `extend List<T :: [Display]> :: [Display]` ist heute
LYR-SEM0047. new-features nimmt es als „conditional conformance“ auf (Syntaxvorschlag
`extend<T :: [Display]> List<T> :: [Display] { … }`, Rust `impl<T: Display> Display for
Vec<T>`). Ohne das gibt es genau zwei schlechte Wege: Display unbedingt auf `List<T>` (dann
muss `show()` für `T` ohne Display etwas erfinden — Go's `%v` — nein) oder freie Funktionen
`showList<T :: [Display]>(xs): string` (funktioniert heute; ist aber kein `println(xs)`).

**Design 4.5 (nach conditional conformance):**

| Typ | `show()` | Vorbild |
|---|---|---|
| `List<T :: [Display]>`, `T[]`, `Set<T>` | `[1, 2, 3]` | Rust `{:?}`, Kotlin `toString()`, Python `repr` |
| `Map<K, V>` | `{a: 1, b: 2}` | Kotlin `{a=1}` — Lyric nimmt `:` (JSON-nah) |
| `?T` | `null` oder der Wert (KEIN `Some(…)`) | Kotlin — Rust's `Some(3)` ist Option-Syntax, Lyric hat keine |
| Tupel | `(1, "a")` | Rust |
| `string` in Containern | `"a"` mit Anführungszeichen (Debug-Form), außerhalb ohne | Rust unterscheidet Display/Debug; Lyric hat nur `show()` → Regel: **oben ohne, geschachtelt mit** — wie Python `print([“a”])` vs `print(“a”)` |
| JsonValue | `serialize()` | — |
| IoError, JsonError, ParseError, EncodingError | `message()` | Rust `impl Display for io::Error` |
| Result<T, E> | `Ok(3)` / `Err(…)` | Rust |
| Duration/Instant | existiert (4.1) | — |

Kein zweites `Debug`-Interface: eine Sprache mit einem `show()` und der Regel „geschachtelt
= zitiert“ deckt beide Rust-Traits für den Alltag; ein `debug()` neben `show()` wäre ein
zweiter Mechanismus für ein Konzept.

**Synthese (new-features E):** Equatable/Hashable/Ordered/Display ohne Body für
IoErrorKind, JsonValue, Wait, ParseErrorKind, Result. Anker: `combineHash(seed, value)` in
std.core (im Prototyp), `show()` der Felder, `equals`/`compare` wie heute. Bis dahin: der
Prototyp-Test vergleicht `ParseErrorKind` über einen `match` (Test-Kommentar sagt es).

---

## 7. Zahlen

4.5 (Prototyp): `intMax/intMin`, `checkedAdd/Sub/Mul/Abs` → `?int`, `saturatingAdd/Sub/Mul`,
`powInt` ohne Überlauf (per checkedMul; Quadrat nur, solange noch ein Faktor kommt),
`parseInt`-Überlauf → null (negativ akkumulierend, Grenztest VOR der Multiplikation:
`acc < (limit + digit) / radix`), `parseIntOrErr` mit `ParseErrorKind.Overflow`.

Design 4.5 (nicht im Prototyp): `parseUint(s): ?uint`, `uintMax`, `floatMax/floatMin/epsilon`,
`checkedDiv` (nur `intMin / -1` überläuft), `wrappingAdd` = `+` (dokumentiert; kein Name, weil
der Operator es schon ist — Rust hat `wrapping_add`, weil `+` dort im Debug panikt; Lyric's `+`
wrappt per Spec, ein zweiter Name wäre ein zweiter Weg).

| Vorbild | übernommen | vermieden |
|---|---|---|
| Rust `i64::MAX`, `checked_*`, `saturating_*`, `overflowing_*` | die drei Familien ohne `overflowing` (Tupel-Rückgabe ist in Lyric `?int` + Operator) | `wrapping_*` |
| Go `math.MaxInt64`, `strconv.ParseInt` → `ErrRange` | ErrRange als `ParseErrorKind.Overflow` | — |
| Python `int` unbegrenzt | — | BigInt in der stdlib (kein Bedarf; 6.0-Frage) |
| .NET `checked { }`, `int.MaxValue`, `Math.Clamp` | Namen `Max/Min` als `intMax/intMin` (Kleinschreibung: Konstanten sind `pub let`) | `checked`-Block (Sprache) |

---

## 8. Strings

4.5 (Prototyp): `isWhitespace` = Unicode White_Space (25 Codepoints), damit
`trim() == trimStart().trimEnd()` und `isBlank` konsistent (Bug 12). `ParseError`.

Design 4.5: `isLetter/isUpper/isLower/isDigit` Unicode-fähig als Natives
(`std.string.charClass(c): int` liefert die Kategorie; ein Native statt vier), `lines():
Iterator<string>` (lazy, `\r\n` und `\n`), `removePrefix/removeSuffix` (Rust `strip_prefix`
→ `?string`; Lyric: `removePrefix` gibt den String unverändert zurück, wenn der Präfix fehlt —
Python-Semantik, weil `?string` hier kein Wissen trägt, das `startsWith` nicht schon gibt),
`capitalize`, `reverse` (über Codepoints; Grapheme-Cluster NICHT — dokumentiert, Swift ist die
einzige Sprache, die Grapheme im Standard hat, und zahlt mit O(n) für alles), `equalsIgnoreCase`
(ordinal, wie `toUpper`), `substringFrom(start)`, `slice(from, to)`, `toUpper("ß")` bleibt `ß`
(ordinal; `SS` wäre Locale — dokumentiert). `StringBuilder`: `appendInt/appendFloat`
(spart `fromInt`), `length()`, `clear()`. 5.0: nichts Breaking.

---

## 9. Zeit

Design 4.5 (additiv, nicht im Prototyp): `Duration.ofHours/ofDays/ofNanos`,
`totalMinutes/totalHours`, `Instant.minus(d)`, `Instant.until(later)`, tolerantes
`Instant.fromRfc3339` (Offset `+02:00`, ohne Millis, `T`/Leerzeichen, `z`/`Z`), `Monotonic.now():
Monotonic` + `elapsed(): Duration` (aus `std.os.nowNanos` — der Native wandert namentlich nach
`std.time`, alter Name bleibt gebunden), `sleep(d: Duration)`. **Kalender-Minimalmodell:**
`Date { year, month, day }` als Struct mit `Instant.toDateUtc()`, `Date.plusDays`, `dayOfWeek`,
`isLeapYear`, `daysInMonth` — proleptisch gregorianisch, NUR UTC. Zeitzonen: `Instant.toLocal()`
NICHT in 4.5; 5.x als `std.time.zone` mit einer Native-Brücke zur Host-TZ-Datenbank (Rust hat
sie NICHT in std — `chrono`; Go hat `time.LoadLocation`; Python `zoneinfo`). Lyric folgt Rust:
Zeitzonen sind ein Paket, kein stdlib-Versprechen.

---

## 10. Random, Hash, Pfade/Dateien, Prozess, JSON, Bytes

**Random (Prototyp):** `fresh()` aus `secureRandom(8)` (capability-frei, im Gegensatz zu
einem Seed aus `std.os` — deshalb ohne osAccess); `nextIntRange` per Rejection Sampling über 63
Bits (Go `rand.Int63n`, Java `nextInt(bound)`); `nextFloat` aus den oberen 53 Bits (Java
`nextDouble`). Design: `nextFloatRange(lo, hi)`, `shuffleArray`, `choiceArray`, `sample(k)`.
**Determinismus-Regel (mit macro-abi abgestimmt):** `secureRandom` und `Random.fresh()` sind
die zwei nicht-deterministischen Züge des Moduls; ein Compile-Zeit-Runner (`comptime`) lehnt den
Import `std.random.secureRandom` per Namen ab (LYR-CT0002) statt eine Capability einzuführen;
`Random.seeded(n)` bleibt überall auswertbar.

**Nativ oder `extern "dotnet"` (mit macro-abi abgestimmt):** In der stdlib nativ bleibt, was ein
Programm ohne `hostAccess` braucht — Digests, UTF-8, Base64/Hex, Zeit, Datei, Netz, Prozess.
Über die Host-ABI im Nutzercode bleiben Kompression, HTTP-Client, Regex, Zeitzonen
(`TimeZoneInfo`), Kryptografie jenseits Digests (AES/RSA): für keines davon ist ein stdlib-Modul
vor 5.x geplant. §11-Zusage zu Bytes (Vorschlag, bei Punkt 5): „`uint8[]` ist der Bytepuffer der
Bibliothek: jede Funktion, die Bytes liest oder liefert, spricht ihn, und ein Host hält ihn als
EIN zusammenhängendes Array fester Länge, dessen Elemente die Bytes in Reihenfolge sind — eine
Fremdschnittstelle darf ihn per Kopie an der Grenze übergeben.“

**Hash (Prototyp):** `std.hash` mit sha256/sha1/md5/crc32 nativ, `*Hex`, `hashCombine`.
Python (hashlib/zlib) und Go (crypto/*, hash/crc32) haben Digests in der stdlib; Rust nicht
(sha2-Crate); .NET hat SHA aber kein CRC. Lyric: in der stdlib, weil ein Tool ohne
Paketmanager-Ökosystem sonst gar keinen Weg hat; `md5/sha1` mit Warntext im Doc-Kommentar.
`hmacSha256` folgt in 4.5 (ein Native mehr).

**Pfade (Design):** `normalize` (löst `.`/`..` lexikalisch, Go `filepath.Clean`), `relative(base,
target)`, `components(path): string[]`, `absolute(path)` (braucht `currentDir` → wandert nicht:
`std.io.file.absolute`, capability fileAccess — die Pfad-Module bleiben capability-frei).

**Dateien (Design):** `walk(dir): Iterator<string>` (Python `os.walk`, Rust `walkdir` extern,
Go `filepath.WalkDir`), `removeAll(dir)` (Rust `remove_dir_all`), `modifiedAt(path): ?Instant`,
`writeLines(path, lines)`, `createTempFile()`. `textOrErr` (Prototyp).

**Prozess (Design):** `output(program, args): ?Output { code, stdout, stderr }` (Go
`exec.Command().Output()`, Python `subprocess.run(capture_output=True)`) — die 20-Zeilen-Schleife
aus Guide 13 wird eine Zeile; `startIn(cwd, env, program, args)` als Options-Struct
`Spawn { program, args, cwd = "", env = [] }` mit Defaults, damit keine sechs Parameter entstehen
(Rust `Command`-Builder → Lyric-Struct mit Defaults, kein Builder).

**JSON (Design):** `path("users.0.tags.1"): ?JsonValue` (fünf Null-Checks → einer; jq-Pfad,
kein JSONPath), `JsonValue` Equatable+Display (Synthese/serialize), Builder `JsonObject.new()
.set("k", v)`, Anker `ToJson { fn toJson(): JsonValue }` / `FromJson<T> { static fn fromJson(v):
?T }` (Swift `Codable` ohne Derive — Derive kommt mit Synthese), `parseOrErr` (Prototyp).

**Bytes (Design):** `toIntLE/toIntBE(bytes, at)`, `fromIntLE/BE(n, width)`, `equals(a, b)`,
`BytesBuilder` (wie StringBuilder), `startsWith`. Für macro-abi: `uint8[]` bleibt der eine
Puffertyp der stdlib (§11-Anker), kein pinbarer `Buffer`.

---

## 11. Test

Prototyp: `assertNotEq/assertNull/assertNotNull/assertClose/assertLess/assertContains/fail`.
`assertThrows` ist BLOCKIERT: ein Lambda kann keine `throws`-Klausel tragen (LYR-SEM0084,
new-features F, vorgezogen). Zielform: `assertThrows<E :: [Throwable]>(f: fn() -> void throws E):
E` — die Exception ist die Antwort (Rust `#[should_panic]` ist schwächer, JUnit `assertThrows`
gibt sie zurück — Lyric folgt JUnit/Kotlin). `assertEq` bleibt `(actual, expected)`. Diff:
`assertEq` über Strings zeigt ab 4.5 bei Ungleichheit die erste abweichende Position
(`expected "…" got "…", differ at offset 12`) — kein Zeilen-Diff (Rust `pretty_assertions`
ist extern; Kotlin/JUnit zeigen Diff in der IDE). Ein `fail(): never` sobald die Sprache
`never` als Rückgabetyp erlaubt (new-features C).

---

## 12. Nebenläufigkeit (std.task)

Design 4.5/5.0 (Major 5): `Wait` mit `@NonExhaustive` (Anker im Prototyp), damit `Channel`/
`Timeout` als Varianten kommen dürfen; `spawn(task): TaskHandle` mit `join(handle)` und
`join(handles: TaskHandle[])`; `Channel<T>` (bounded, `send` yieldet bei voll, `receive(): ?T`
mit `null` = geschlossen — dieselbe Dreifaltigkeit wie `readSome`), `timeout(d, task): bool`,
`sleep(d: Duration)` als Hilfsfunktion (heute `yield Wait.Sleep(ms)` in Nutzercode). Vorbild Go
(Channels, `select` → Lyric `Wait.Any([...])`), nicht Kotlin-Flows (zu groß), nicht Rust async
(Function Coloring — Lyric's stackful Coroutinen sind der Grund, das zu vermeiden). Breaking:
`spawn` gibt Handle statt void (5.0; 4.5 als `spawnHandle`).

---

## 13. Deprecation-Pfad

- Attribut `@Deprecated { message, until }` existiert für Module/Typen/Funktionen mit Uhr
  (LYR-SEM0081: Build stoppt bei Erreichen). Fehlt: **Methoden und Felder** → Anker `OnMethod`
  (im Prototyp) + Compiler (new-features, klein).
- **Alias-Phase:** ein umgezogener Name bleibt eine Version als Weiterleitung mit
  `@Deprecated { until = "<nächster Major>" }`; die stdlib-Tests prüfen BEIDE Formen bis zum
  Ablauf, weshalb die freien Terminatoren im Prototyp noch kein `@Deprecated` tragen (die
  Warnung würde in stdlib-tests feuern) → 4.6: Tests auf die Methoden umstellen, dann Attribut.
- **Native-Namen** bleiben doppelt gebunden (Registry, wie `std.string.raw*`), bis der
  Bytecode-Major sie streicht.
- Vorbild: Rust `#[deprecated(since, note)]` (kein Ablauf), Swift `@available(deprecated,
  obsoleted:)` (Ablauf!), .NET `[Obsolete(error: true)]`. Lyric hat mit `until` bereits das
  Swift-Modell — das Beste der drei; nichts ändern.

---

## 14. Migrationsplan

| Schritt | Version | Wer | Werkzeug |
|---|---|---|---|
| Neue Namen neben alten (Prototyp) | 4.5 | stdlib | — |
| stdlib-Tests und Guide auf die neuen Formen umstellen; `@Deprecated { until = "5.0" }` auf freie Terminatoren, `keys(m)`, `listContains`, `std.os.sleep/now*` | 4.6 | stdlib | `lyric check` warnt |
| `lyric fmt --migrate`? NEIN — ein Formatter, der Semantik ändert, ist keiner. Stattdessen `lyrfix` (neues Tool oder `lyric fix`): mechanische Regeln `fold(it, s, f)` → `it.fold(s, f)`, `keys(m)` → `m.keys()`, `std.os.sleep(ms)` → `std.time.sleep(Duration.ofMillis(ms))` | 4.6 | Tooling | Rust `cargo fix`, Go `gofix` |
| try-Ausdruck; `OrErr`-Formen deprecated (`until = "6.0"`) | 4.7+ | Sprache+stdlib | — |
| Entfernen: freie Terminatoren, `keys/values/entries` frei, `listContains/listIndexOf`, `std.os`-Zeit, `lastErrorKind`-Natives; Typsuffixe → Überladung mit Uhr; `spawn` mit Handle; Felder `pub` durchgesetzt | 5.0 | alle | Build stoppt (SEM0081) |
| Guide: Kap. 13 neu gegliedert nach Antwortformen (3.1) statt nach Modulen; Kap. 10 (Fehler) bekommt „Result als Wert“; Kap. 20 (Testing) die Familie; Spec §11 Punkt 5 (Antwortformen), §9 Verweis auf `std.result` als Bibliothekskonvention, §11 Punkt 3 schrumpft um `lastError*` | 4.5 (Guide 13 im Prototyp teilweise), 5.0 (Spec) | Doku | DocGen-Ratchet |

Bestehender Code: 4.5 bricht nichts (verifiziert: 166 Alt-Tests laufen unverändert; die
einzige Verhaltensänderung ist `parseInt("<überlauf>")` → null statt Müll, `powInt(2, 64)` →
null statt 0, `trimStart` entfernt NBSP — drei Bugfixes, im CHANGELOG als solche).

---

## 15. Blocker durch Compiler-Bugs — exakte Anforderungen

### 15.1 Behoben im Prototyp (zwei Fixes, beide im Lowering)

**(a) Methoden generischer Enums wurden nie gelowert.** `FunctionLowerer.cs:3827`: der Fall
„Receiver ist `GenericInstance`“ nannte nur Class/Struct; ein generisches **Enum** fiel in den
Fallback `TryResolveFunction` → `LYR-IR0001 call to 'isOk' (external or bodiless)`. Damit war
JEDE Methode auf `Result<int, string>` unaufrufbar — Prototyp 21 hatte den Fall nur für
`Option<?int>` gesehen (FunctionLowerer.cs:3914 ist die Fallback-Zeile, nicht die Ursache).
Fix: `or TypeSymbolKind.Enum`. pattern-lambda hat ihn übernommen, der Merge ist deckungsgleich.

**(b) Argumente wurden nicht gegen die Substitution der Instanz gewidert** — der Folgefund von
(a), gefunden bei der Gegenprobe gegen pattern-lambdas Branch. `LowerGenericMethodCall` reichte
keine `calleeSubstitution` an `MaterializeArguments`, also blieb ein Parameter, der `T`
geschrieben steht, ein Name: bei `T = ?int` lowerte ein `int`-Literal zum blanken Skalar, und
der Store in den Optional-Slot war fehlerhaft — der Verifier fing es einen Schritt später im
AUFRUFER, ohne Zeile zum Hinzeigen (`store of t10 (i64) into l8 (?i64)`). Der Pfad für
generische Interface-Member baute dieselbe Abbildung längst; jetzt tut es der Instanz-Pfad
auch. Minimaler Repro (`probes/iso_enum.lyr`): `Holder<?int>.Full(x).or(3)` stürzte ab,
`Box<?int> { … }.or(3)` nicht — die Klasse erreichte den Pfad vor dem Enum, was die Lücke
verdeckte. Test: `tests/Lyric.Tests.Ir/LoweringTests.cs`, beide Arten gepinnt.

**Was damit geht:** `Result<?T, E>` wird konstruiert, gematcht (über `Ok(_)`), `isOk`/`isErr`/
`unwrapOr`/`err`/`map` arbeiten darauf — Vollständigkeitstest in
`stdlib-tests/tests/result_optional_tests.lyr`.

### 15.2 Offen — was daran hängt

| Blocker | Stelle | Hängt daran | Anforderung an | Umgehung im Prototyp |
|---|---|---|---|---|
| Generische Methode auf generischem Typ (`fn map<U>` in `Result<T,E>`, `List<T>.map<U>`) → `LYR-IR0001 this type argument` | `ReturnTypeOfInstanceMethod` (FunctionLowerer.cs:3456) kennt nur die Owner-Substitution, nicht die der Methode | `Result.map/andThen/mapErr/orElse`, `List.map`, `Iterator.toList` als Methoden | **new-features** (Instanztabelle: Request mit Owner- UND Methoden-Argumenten; Interface-Default-Methoden können es schon — `Iterator.map<U>` — der Klassen-/Enum-Pfad nicht) | freie Funktionen `map(r, f)`, `mapList(xs, f)` |
| Enum mit optionalem Typargument: `Ok(v)`/`Ok(null)` deckt die Variante nicht (nur `Ok(_)`) | TypeChecker.cs:4288 | den Payload BINDEN oder `Ok(null)` von `Ok(v)` trennen | **pattern-lambda** — auf ihrem Branch gebaut und gegengeprüft: die neue Regel bindet den Payload als `?T` und deckt ab | `Ok(_)` plus `isErr`/`unwrapOr`; der Test sagt, welche Form nach dem Merge dazukommt |
| **Strukturell, kein Bug:** jede Signatur mit `?T` ist für `T = ?U` ein `??U` — `Result.ok(): ?T` und `fromOptional(o: ?T)` sind für optionale Payloads nicht instanziierbar | Sprachregel „`?` schachtelt nicht“ | zwei Bibliotheksmitglieder, nicht der Typ | **niemanden** — `match` trennt `Ok(null)` von `Ok(v)`, `Result<?U, E>.Ok(x)` konstruiert; beide Stellen dokumentieren es | dokumentiert in `std.result` und im Test |
| `List<?T>`/`Map<K, ?V>` nicht instanziierbar (`??T`) | collections.lyr `data: (?T)[]`, TypeLowering | jede Sammlung optionaler Werte, `Iterator<?T>` per compact, UND pattern-lambdas benannter Rest `[first, ..rest]` (das Lowering kann kein Array unbekannter Länge bauen) | **new-features**: privates Native `rawArrayAlloc<T>(n): T[]` (VM ~15 Z., unbeobachtbar uninitialisiert — von new-features als „in Ordnung“ bewertet) → List/Map halten `T[]` + `states[]`, und `..rest` bekommt sein Array. `??T` bleibt abgelehnt (5.0, Format-Major). **Zwei Features hängen am selben Haken, was die Priorität hebt.** | Wrapper-Struct im Nutzercode; `..` ohne Namen plus `slice` |
| Display-Constraint zu lax (`println([1,2,3])` stirbt im IR, console.lyr:51) | TypeChecker Satisfies für Array/Optional/Tupel | Display für Container | **new-features**: (a) Satisfies straffen (Fehler in der Nutzerdatei), (b) conditional conformance `extend<T :: [Display]> List<T> :: [Display]` | freie `showList` möglich, nicht gebaut |
| `throws E` mit Typparameter wird nie substituiert (ExceptionAnalyzer.cs:218) | — | `orThrow(): T throws E` mit `catch (e: IoError)`; `assertThrows<E>` | **new-features** (F) | `catch (e: Throwable)` |
| Lambda darf nicht werfen (`fn() -> void throws E` als Parametertyp: LYR-SEM0084) | — | `assertThrows`, `attempt(f)`, werfende `map`-Lambdas | **new-features** (F, vorgezogen) | assertThrows fehlt, Kommentar in test.lyr |
| `?T == ?T` | — | `assertEq(parseInt("x"), null)` | **new-features** (J, übernommen) | `assertNull` |
| Feld und Methode teilen den Namensraum (LYR-RES0001) | Resolver | `Map.keys()` neben Feld `keys` | keine Änderung nötig — Feld umbenannt (`keyColumn`); aber: JEDE künftige Methode eines Nutzertyps kollidiert mit gleichnamigem Feld; Kotlin/Swift/C# trennen die Namensräume nicht (Property vs. Methode kollidieren dort auch) → Lyric bleibt konsistent | Umbenennung |
| Enum nicht `Equatable` ohne Handarbeit; `extend Enum :: [Hashable]` → IR0001 (TypeTable.cs:422) | — | `ParseErrorKind == ParseErrorKind` | **new-features** (E Synthese) | `match` im Test |
| `@Deprecated` nur auf Modul/Typ/Funktion | AttributeValues | Deprecation von Methoden/Feldern | **new-features** (OnMethod-Anker im Prototyp) | freie Funktionen nur |
| Interface-Default-Methode kann keinen Constraint auf `T` legen (`sum()` als Methode) | — | `sum/minValue/maxValue` als Methoden | new-features, niedrig | frei |
| Nicht-generische Interface-Methode mit Elementtypwechsel (`enumerate()`) | InstanceTable | enumerate/chunks als Methoden | new-features, niedrig | frei + for-Tupel (pattern-lambda) |

---

## 16. Vergleich pro Designentscheidung (Kurzform) und Fazit

| Entscheidung | Rust | Go | Kotlin | Swift | Python | .NET | Lyric übernimmt / vermeidet |
|---|---|---|---|---|---|---|---|
| Fehler als Wert | Result + `?` | (v, err) | Result<T> (Throwable) | Result + try? | – | TryParse | Rust-Typ, Swift-Brücke; vermeidet Go-Tupel, .NET-out |
| Absenz | Option | nil/zero | T? | Optional (nestbar) | None | Nullable<T> (nicht nestbar) | bleibt `?T` (Kotlin); vermeidet zweite Form |
| Iterator | Trait, assoziierter Typ, alles Methoden | range/keine | Sequence + Extensions | Sequence/IteratorProtocol | Iterator-Protokoll | IEnumerable + LINQ | Rust-Methodenstil ohne assoziierte Typen; Kotlin-Sequence-Semantik |
| Container-Konstruktion | vec!/from | Literale | listOf/mutableListOf | Literale | Literale | new List{…} | `of(array)` (Java/Kotlin) — kein Makro, kein Literal |
| Map-Insert-or-Get | entry API | Nullwert | getOrPut | Default-Subscript | setdefault | TryGetValue+Add | `getOrInsert/update` (Kotlin-Einfachheit, Rust-Name) |
| Display/Debug | 2 Traits | Stringer/%v | toString | CustomStringConvertible + Reflection | __str__/__repr__ | ToString | 1 Interface + Nesting-Regel; vermeidet Reflection |
| Checked-Arithmetik | checked_/saturating_/wrapping_ | – | – (Math.addExact) | &+ Operatoren | unbegrenzt | checked{} | Rust-Familien als Funktionen; vermeidet Operatoren |
| Whitespace | Unicode | unicode.IsSpace | Unicode | Unicode | Unicode | Unicode | Unicode (war Bug) |
| Digests | extern | stdlib | JVM | CryptoKit | hashlib | BCL (ohne CRC) | stdlib (Go/Python) |
| Zeitzonen | extern | stdlib | JVM | Foundation | zoneinfo | BCL | extern/5.x (Rust) |
| Random-Bereiche | rand extern, Rejection | Rejection | JVM | Rejection | Rejection | Rejection | Rejection (war Bias) |
| Deprecation | since/note | Kommentar | @Deprecated(level) | obsoleted: | warnings | Obsolete(error) | Swift-Modell (hat es) |
| Panik-Grenze | panic für Programmierfehler | panic selten | Exceptions | fatalError | Exceptions | Exceptions | Rust-Grenze, in §11 verankert |
| Assert-Reihenfolge | symmetrisch | testing.T | expected, actual | XCTAssertEqual symmetrisch | assertEqual(first, second) | Assert.Equal(expected, actual) | (actual, expected) — bleibt |

### Fazit: Was die Lyric-stdlib nach der Überarbeitung besser macht als X

- **Besser als Rust:** Eine Absenzform und ein Fehlermechanismus — `?T` und `throws` — und
  Result NUR als Wert: kein `?` an jedem Aufruf, kein `Option<Option<T>>`, kein
  `Box<dyn Error>`; die Antwortform steht im Namen, wo Rust sie aus dem Typ lesen lässt.
  Digests und Zeit-Grundlagen in der stdlib, wo Rust drei Crates braucht. Deprecation mit
  Ablaufdatum, das den Build stoppt.
- **Besser als Go:** Typisierte Fehler mit Grund-Enum hinter Träger statt `if err != nil`
  nach jeder Zeile; Generics-fähige Container mit Methoden-Ketten statt `slices.Sort(xs)`;
  ein Iterator-Protokoll statt drei Range-Formen; keine Nullwerte, die „nicht gesetzt“
  verschleiern.
- **Besser als Kotlin:** `Result<T, E>` mit freiem E (Kotlin: nur Throwable); geprüfte
  throws-Klauseln statt ungeprüfter Exceptions; Iteratoren ohne `hasNext/next`-Paar, das
  auseinanderlaufen kann; deterministische Numerik ohne JVM-Locale-Überraschungen.
- **Besser als Swift:** Kein Grapheme-Cluster-Preis auf jedem String-Zugriff (Codepoint-Semantik
  konsequent, O(n) sichtbar als `length()`); Capabilities pro Modul, die ein Host gewähren muss;
  Wertsemantik ohne Copy-on-Write-Magie.
- **Besser als Python:** Alles typisiert; `parseInt` sagt WARUM (Kind + Offset), `int()` wirft
  ValueError mit Text; Tombstone-Verdichtung und Load-Faktor dokumentiert; keine zwei
  String-Darstellungen (`str`/`repr`), sondern eine Nesting-Regel.
- **Besser als .NET:** Keine `TryParse(out)`-Seitenkanäle, kein `Nullable<T>`-Verbot des
  Schachtelns als Sonderfall, `Result` statt Exceptions für Batch-Fehler; ein Formatter, der die
  eine Form schreibt; CRC in der stdlib.
- **Was alle sechs besser machen und Lyric noch nicht:** Display für Container (wartet auf
  conditional conformance), generische Methoden auf generischen Typen (`xs.map(f)` als Methode),
  Zeitzonen (bewusst), ein Paketmanager, der die stdlib klein halten dürfte.

---

## 17. Was im Prototyp steht (Branch `worktree-agent-aa7b5e912a78e6f2d`)

| Commit | Paket | Dateien | Tests |
|---|---|---|---|
| 5164fe2b | (a) std.result + OrErr-Formen + parseInt-Überlauf + Compiler-Fix Enum-Methoden | result.lyr (neu), string.lyr, json.lyr, io/file.lyr, encoding.lyr, FunctionLowerer.cs | result_tests.lyr (12) |
| 09e97f13 | (b) Terminatoren als Methoden + 5 Adapter | iter.lyr | iter_tests.lyr (+8) |
| 2dd73b36 | (c) Container-Paket + Verdichtung | collections.lyr | collections_tests.lyr (+9) |
| 1eb318f8 | (d) powInt, checked/saturating, Unicode-Whitespace, formatHex-Doku, Random | math.lyr, string.lyr, fmt.lyr, random.lyr | math/string/random/fmt_tests (+5) |
| 6014114c | (e) std.hash + combineHash + Anker + Test-Familie | hash.lyr (neu), NativeRegistry.cs, core.lyr, test.lyr | hash_tests (5), test_tests (1) |
| 6bd1a6e9 | Doku: Guide 13, DocGen-Ratchet 554→683, Site 23→25 Seiten, Snapshot | docs/guide/13, tests/Lyric.Tests.DocGen | — |
| 2656bcb9 | arrayOf/arrayFilled ohne Native (Prototyp 18, Form A) | collections.lyr | collections_tests (+1) |
| 4fe6381e | `std.os.args()` liefert die Programmargumente (Bug, lyriclings-Fund); `file.modifiedMillis` | NativeRegistry.cs, io/file.lyr | file_tests (+1) |
| a8df8a5d | Argument-Widerung gegen die Substitution der Instanz (Folgefund der Gegenprobe) | FunctionLowerer.cs | LoweringTests (+1) |
| 881accbd | `Result<?T, E>`: was trägt, und wo die Nicht-Schachtelung die Grenze zieht | result.lyr | result_optional_tests (+1) |

211/211 stdlib-Tests (166 alt + 45 neu); Ir 176, Sema 770, Vm 1453, Formatting 190, DocGen 201,
Lsp 281, Embedding 222 grün; Cli 276/277 — `InterruptTests.Sigint` fällt in der WSL-Sandbox auch
auf unveränderten Branches (macro-abi bestätigt), umgebungsbedingt. new-features hat den Enum-Methoden-Fix als Voraussetzung in seine Roadmap übernommen
(er bleibt auf diesem Branch, um einen doppelten Einzeiler-Konflikt in FunctionLowerer.cs zu
vermeiden), die generische Methode auf generischem Typ als HIGH-Bug für 4.5, `try e` → Result als
Option B seines try-Ausdruck-Designs (Empfehlung: 5.0, zusammen mit dem Wegfall der Zwillinge),
`pub(module)` in sein Sichtbarkeits-Design, und entscheidet `??T` gegen nestbare Optionals —
womit `rawArrayAlloc` die 5.0-Lösung für `List<?T>` bleibt.
