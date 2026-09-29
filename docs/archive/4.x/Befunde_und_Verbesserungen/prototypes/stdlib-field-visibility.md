# stdlib-Felder: Vertrag (pub) oder Implementierungsdetail (privat)?

Vorbereitung für Prototyp 17 (Member-Sichtbarkeit). Alle 104 Felder der 15 Module, Stand 4.4.1
(`awk` über `stdlib/std/**/*.lyr`, Zeilen aus dem aktuellen Stand). Regel: ein Feld ist Vertrag, wenn
(a) der Doku-Kommentar es als Antwort beschreibt, (b) Nutzercode es lesen MUSS (Fehlertypen, `Packet`),
oder (c) der Typ per Initializer im Nutzercode gebaut wird (`Exception { text = … }`, `Deprecated { … }`).
Ein Feld ist privat, wenn ein Getter existiert oder die Invariante bricht, sobald jemand schreibt.

| Datei:Zeile | Typ | Feld | pub? | Begründung |
|---|---|---|---|---|
| core.lyr:74 | Exception | text | **pub (read+init)** | Initializer im Nutzercode: `throw Exception { text = "…" }`; `message()` liest es |
| core.lyr:496 | Deprecated | message | **pub (init)** | Attribut-Argumente `@Deprecated { message = …, until = … }` |
| core.lyr:503 | Deprecated | until | **pub (init)** | dito; der Compiler liest es |
| string.lyr:60 | Utf8Error | offset | **pub (read)** | die Antwort „wo brach es“ — es gibt keinen Getter |
| string.lyr:580 | StringBuilder | parts | privat | Backing-Array; Doku „`count` says how many are filled“ |
| string.lyr:582 | StringBuilder | count | privat | Invariante; `build()` ist die Antwort |
| encoding.lyr:23 | EncodingError | offset | **pub (read)** | Fehlerinhalt ohne Getter |
| encoding.lyr:24 | EncodingError | expected | **pub (read)** | dito |
| encoding.lyr:35-37 | DecodeAttempt | value, at, what | privat | Struct ist selbst privat |
| json.lyr:304-309 | JsonError | line, column, offset, expected | **pub (read)** | Fehlerinhalt; `message()` rendert nur Zeile/Spalte, `offset` ist NUR über das Feld erreichbar |
| json.lyr:389-392 | JsonParser | chars, pos, errPos, errWhat | privat | Klasse privat |
| time.lyr:28 | Duration | millis | privat | Getter `totalMillis()`; Struct-Kopie macht Schreiben ohnehin wirkungslos |
| time.lyr:112 | Instant | millis | privat | Getter `epochMillis()` |
| time.lyr:102 | TimeError | text | **pub (read)** | Fehlerinhalt (= `message()`; könnte auch privat sein, dann wäre `message()` der Vertrag — Empfehlung: privat, `message()` reicht) |
| io/error.lyr:33 | IoError | kind | **pub (read)** | `match (e.kind)` ist der dokumentierte Weg (Guide Kap. 10) |
| io/error.lyr:34 | IoError | path | **pub (read)** | dito |
| io/error.lyr:35 | IoError | detail | **pub (read)** | dito |
| io/net.lyr:41-45 | Packet | bytes, host, port | **pub (read)** | Doku: „the payload plus who sent it, which is the address a reply's `sendTo` takes“ |
| random.lyr:40 | Random | state | privat | Doku „every draw replaces it“; Schreiben von 0 wäre der Fixpunkt, den `seeded` gerade abfängt |
| collections.lyr:43-45 | List | data, count | privat | Invariante (`count <= data.length`, Slots ≥ count sind null) |
| collections.lyr:255-257 | ListIterator | source, index | privat | Cursorzustand; `iter()` ist der Vertrag |
| collections.lyr:303-307 | Deque | data, head, count | privat | Ringpuffer-Invariante |
| collections.lyr:438-446 | Map | keys, values, states, count, used | privat | Hash-Invariante; **aber**: MapKeyIterator/MapValueIterator/MapEntryIterator (Z. 968-1030) lesen `source.states/keys/values` von AUSSEN → bei Durchsetzung müssen die drei Iteratoren in die Klasse (Methoden `keys()/values()/entries()`, siehe Minor 2) oder modul-sichtbar (`pub(module)`) werden |
| collections.lyr:740-746 | Set | items, states, count, used | privat | dito; SetIterator (Z. 888-905) liest `source.items/states` von außen |
| collections.lyr:890-892 | SetIterator | source, index | privat | Cursor |
| collections.lyr:970-1015 | Map*Iterator | source, index | privat | Cursor |
| iter.lyr:80-82 | RangeIterator | current, end | privat | compiler-gebunden per Initializer (`RangeIterator { current, end }` wird vom Lowering gebaut → Compiler braucht Initializer-Zugriff; für Nutzercode privat) |
| iter.lyr:101-151 | Inclusive*/Unsigned* | current, last/end, done | privat | Klassen privat, compiler-gebunden |
| iter.lyr:172-174 | ArrayIterator | source, index | privat | compiler-gebunden für `for (x in array)`; `over()` ist der Nutzer-Vertrag |
| iter.lyr:191-193 | StringIterator | chars, index | privat | compiler-gebunden für `for (c in s)` |
| iter.lyr:261-263 | CompactIterator | source, index | privat | `compact()` ist der Vertrag |
| iter.lyr:293-497 | Map/Filter/Take/Skip/TakeWhile/Enumerate/Zip/Chain/FlatMap/ChunksIterator | source, f, keep, remaining, pending, done, index, left, right, first, second, onFirst, current, size | privat | alle über Methoden/Funktionen gebaut; Doku nennt sie „the lazy machinery behind …“ — die KLASSEN könnten ebenfalls privat werden (sie sind nur pub, weil die Funktionen `Iterator<T>` zurückgeben und der Typ nie im Nutzercode erscheint) |
| option.lyr:97-99 | OptionIterator | value, done | privat | `iter()` ist der Vertrag |
| io/console.lyr:112 | LineIterator | done | privat | `lines()` ist der Vertrag |
| io/stream.lyr:207-216 | LineReader | source, pending, at, ended, broke | privat | `failed()` ist der Getter für `broke`; Rest Pufferzustand |
| task.lyr:75-82 | Scheduler | ready, sleepers, … | privat | Klasse privat |
| build.lyr | Artifact | (keine Felder) | – | |

## Zusammenfassung
- **pub (Vertrag): 19 Felder** — Exception.text; Deprecated.message/until; Utf8Error.offset; EncodingError.offset/expected; JsonError.line/column/offset/expected; IoError.kind/path/detail; Packet.bytes/host/port; TimeError.text (empfohlen privat, dann 18).
- **privat: 85 Felder.**
- **Vier Stellen, die heute fremde private Felder lesen** und bei Durchsetzung umgebaut werden müssen: MapKeyIterator/MapValueIterator/MapEntryIterator (collections.lyr:975-1030 lesen `Map.states/keys/values`), SetIterator (collections.lyr:895-905 liest `Set.items/states`), ListIterator (liest nur `length()/get()` — sauber), LineReader (eigene Felder — sauber). Empfehlung: die Iteratoren werden Methoden ihres Containers (`Map.keys()` usw., deckt sich mit Minor 2) oder die Sprache bekommt eine Modul-Sichtbarkeit.
- **Compiler-gebundene Initializer**: RangeIterator/InclusiveRangeIterator/Unsigned*/ArrayIterator/StringIterator werden vom Lowering per Initializer gebaut (§4.4 „compiler-bound edges“); privat-Durchsetzung darf diese Bindung nicht treffen (Compiler-Ausnahme oder die Felder bleiben modul-sichtbar).
- **Spec §11 Punkt 2**: `Exception`, `Deprecated` sind Anker — ihre Felder sind damit Vertrag und gehören namentlich in §11.
- **Konsequenz für Fehlertypen**: alle `*Error`-Klassen tragen ihre Antwort als Felder ohne Getter; nach Durchsetzung sind sie `pub`-Felder — oder bekommen Getter (`kind()`, `offset()`), was der `Throwable.message()`-Konvention entspräche. Empfehlung: pub-Felder (Records), wie Rust `pub struct IoError { pub kind, … }`.
