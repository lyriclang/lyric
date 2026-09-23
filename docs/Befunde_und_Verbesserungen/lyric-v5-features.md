# Lyric v5 — Feature-Liste: Sprache & Standardbibliothek

Stand: 2026-09-23.

**Basis:**
- Spec `lyriclang/lyric-spec` @ `8f4d870`; sie beschreibt Lyric 4.6.
- Compiler `origin/main` @ `031ca9dc` (4.6.0, Bytecode-Format 4.0).

**Die Liste baut auf auf:**
- `docs/Befunde_und_Verbesserungen/deliverables/new-features/roadmap.md`
- `design/stdlib-2.md`, `design/abi.md`, `design/macros.md`
- CONTRIBUTING Rule 2: ein Mechanismus pro Konzept, also kein `finally`, keine Threads, keine Vererbung.

**Legende:**
- Priorität: **P1** = trägt andere Features oder die stdlib · **P2** = Standard einer ausgewachsenen Sprache · **P3** = Kür
- 🟡 = designt, aber nicht gebaut
- 💥 = Bruch, braucht eine 4.x-Warnphase
- 📜 = verlangt ein ADR nach Rule 2 (30 Tage Bedenkzeit)

---

## A. Sprache

### A1. Das Fundament, an dem die stdlib hängt

| # | Feature | Prio | Notiz |
|---|---|---|---|
| 1 | **Generische Methoden auf generischen Typen** (`Result<T,E>.map<U>`, `Iterator.toList`) | P1 | Heute `LYR-IR0001`; blockiert die halbe Collection-API. |
| 2 | **Bedingte Konformanz und bedingte Methoden**: `extend<T :: [Display]> List<T> :: [Display]` | P1 🟡 | Macht `Display`/`Equatable` auf Containern möglich, ebenso `List<int>.sum()`. |
| 3 | **Konformanz-Synthese** für `Equatable`, `Hashable`, `Ordered`, `Display`, später `Debug`, `ToJson`/`FromJson` | P1 🟡 | Swift-Stil, kein `derive`. Voraussetzung ist `?T == ?T` (🟡). |
| 4 | **Statische Interface-Member und ein `Self`-Typ**: `interface Parse { static fn parse(s: string): ?Self }`, `Default`, `Zero`/`One` | P1 | Durch Monomorphisierung kostenlos. Aufrufbar nur über eine Constraint (`T.parse(s)`), nie über einen Interface-Wert. Ohne das gibt es kein `FromJson`, kein generisches `sum` und kein `parse<T>`. |
| 5 | **Typed throws, Stufen 1–3**: werfende Funktionstypen und Lambdas (`fn(int) -> int throws E`), Inferenz von `E` | P1 🟡 | Macht `assertThrows` möglich, ebenso werfende `map`/`filter` und Callbacks. |
| 6 | **`try` als Ausdruck**: `try e catch (x: E) …` und `try? e`, das `?T` liefert | P1 🟡 | Stufe 2 in 5.0: `try e` ergibt einen `Result<T,E>` (🟡). |

### A2. Ergonomie und Ausdruckskraft

| # | Feature | Prio | Notiz |
|---|---|---|---|
| 7 | **Benannte Argumente**: `connect(host: "h", port: 80)` | P2 🟡 | Prototyp existiert; wichtig für Konfigurations-APIs. |
| 8 | **Raw- und Mehrzeilen-Strings**: `r"…"`, `"""…"""` | P2 🟡 | Rein lexikalisch. Wird für Regex, SQL und Hilfetexte in Attributen gebraucht. |
| 9 | **Indexierung verallgemeinern**: `Index<K, V>` und `IndexSet<K, V>`, damit `m["k"]` und `m["k"] = v` gehen | P2 | Heute nur `Indexable<T>` mit `int`-Index. |
| 10 | **Slices und Views**: `xs[a..b]` als View ohne Kopie (`Span<T>`), Ranges als Werte, `(0..n).map(...)` | P2 💥 | Heißt, die Regel „Ranges sind kein Wert“ zu revidieren. Heute kopiert `..rest`, weil es keine Slices gibt. |
| 11 | **Collection-Literale für eigene Typen**: `let m: Map<string,int> = {"a": 1}`, `let s: Set<int> = [1, 2]` über `FromLiteral`-Interfaces | P2 | Der Kontexttyp wählt die Konstruktion, analog zu §3.1. |
| 12 | **Struct-Update**: `p with { x = 3 }` | P2 | Wertsemantik von Structs macht das billig. |
| 13 | **Typ-Patterns auf Interface-Werten**: `match (shape) { c: Circle => … }` | P2 | Der Fat Pointer kennt seine Tabelle, also ist ein Downcast möglich, ohne Werte zu taggen. |
| 14 | **Restliche `IR0001`-Grenzen schließen**: `&&=`/`\|\|=`, `p.field ??= x`, `catch` auf Interfaces, Exhaustiveness durch Tupel hindurch | P1 | Die Spec führt diese Lücken schon heute als Implementierungsgrenzen. |
| 15 | **`@NonExhaustive`** und **Enum-Reflexion per Synthese** (`E.variants()`, `E.fromName("…")`) | P2 🟡 | |
| 16 | **Weitere Operator-Interfaces**: `Neg`, `Rem`, Bit-Operatoren, `in` über `Contains<T>`; `++`/`--` aus `Add` abgeleitet | P3 🟡 | |
| 17 | **`checked { … }`-Konstrukt** für überlaufgeprüfte Arithmetik | P3 | §3.2 kündigt es als „future checked mode … new construct“ an. |
| 18 | **`comptime` ausbauen**: `embed("file")` bzw. `embedBytes` (Datei zur Compile-Zeit einbetten), comptime-Tabellen und -Arrays | P2 | Die Sandbox braucht dafür eine Lese-Ausnahme für Projektdateien. |
| 19 | **Doc-Tests**: Code in `///` wird von `lyric test` ausgeführt | P2 | |

### A3. Die 5.0-Brüche (brauchen eine Deprecation-Uhr in 4.x)

| # | Feature | Notiz |
|---|---|---|
| 20 | **Member-Sichtbarkeit, standardmäßig privat** (`pub` an Membern) | 💥 🟡 größter Bruch; eine Warnstufe in 4.7 ist nötig. |
| 21 | **Überladung statt Typ-Suffixen** (`abs` statt `absInt`/`abs`) | 💥 |
| 22 | **Feste Formatsprache in f-Strings** im Python/Rust-Stil (`{x:>8.2}`, `{x:?}`) statt .NET-Specifier, in der Spec festgeschrieben | 💥 Dazu ein `Format`-Interface, damit eigene Typen Specifier verstehen. |
| 23 | **Wertsemantik für `?Struct`**, `let`-Felder, Parameter als `let`, `spawn` liefert ein Handle | 💥 🟡 aus der 5.0-Sammlung. |
| 24 | **Deprecations entfernen**: freie Iterator-Terminatoren, `listContains`, `keys(m)`, die Zeitfunktionen in `std.os`, `addExecutable` | 💥 Braucht ein Migrationswerkzeug `lyrfix`. |

### A4. FFI und Nebenläufigkeit (innerhalb von Rule 2)

| # | Feature | Notiz |
|---|---|---|
| 25 | **`extern "dotnet"` Stufe 2**: Arrays, Optionals, Structs, Instanzmethoden, Handles, Exceptions als `HostError`, Callbacks | P1. Darauf baut ein großer Teil der stdlib unten auf. |
| 26 | **`extern "C"`** mit `std.ffi` und neuem Capability-Bit `ffiAccess` | P3 🟡 abi.md Stufe 2. |
| 27 | **Worker-Isolates**: eine eigene VM pro Worker, Austausch nur über Nachrichten, kein geteilter Speicher | P3 📜. Echte CPU-Parallelität, ohne Threads einzuführen. Bricht Rule 2 („single-threaded“), daher nur mit ADR. |

---

## B. Standardbibliothek

**Grundsatzentscheidung zuerst:** `stdlib-2.md` hat Regex, einen HTTP-Client, Zeitzonen, Kompression und AES/RSA ausdrücklich aus der std ausgeschlossen („nimm `extern "dotnet"`“). Für eine ausgewachsene std würde ich das **revidieren**. Das kostet wenig, weil .NET alles davon mitbringt: Die Module werden dünne Lyric-Hüllen über .NET-Natives und laufen unter den vorhandenen Capability-Bits.

### B1. Kern, Collections, Iteration

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.core` | `Default`, `Clone` (tiefe Kopie für Klassen), `Debug` (synthetisiert, für `{x:?}`), `Parse`, numerische Interfaces `Num`/`Integer`/`Float` (brauchen #4) | P1 |
| `std.collections` | `SortedMap`/`SortedSet` (B-Baum), `PriorityQueue` (Heap), `BitSet`, `MultiMap`, `LruCache`; `Display`/`Equatable`/`Hashable` auf allen Containern (brauchen #2); Backing-Arrays für `List<?T>`/`Map<K,?V>`; `binarySearch`, stabiles `sortBy` auf Arrays | P1 |
| `std.collections.immutable` | persistente `Vector`/`Map` (HAMT) | P3 |
| `std.iter` | `sum`/`min`/`max`/`sorted` als bedingte Methoden; `collect` in `List`/`Map`/`Set` über ein `FromIterator`-Interface; `partition`, `groupBy`, `windows`, `chunks`, `peekable`, `cycle`, `unzip`, `rev`, `flatten`, `scan` | P1 |
| `std.option` / `std.result` | das volle Kombinator-Set als **Methoden** (braucht #1), `transpose`, `flatten` | P1 |

### B2. Text

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.string` | Unicode-Zeichenklassen und Groß-/Kleinschreibung (heute nur ASCII), `removePrefix`/`removeSuffix`, `reverse`, `capitalize`, `words`, `splitN`, `compareIgnoreCase`, `StringBuilder.insert`/`clear` | P1 |
| `std.unicode` | Graphem-Cluster, Normalisierung (NFC/NFD), Breite in der Konsole, Kategorien | P2 |
| `std.regex` | kompilierte Regex mit Captures, benannten Gruppen, `replaceAll` mit Lambda und Iterator über Treffer (.NET `Regex`) | P1 |
| `std.fmt` | neue Formatsprache (#22), `format("{} {}", args…)` zur Laufzeit, `Formatter` für eigene Typen, Tabellen und Einrückung | P1 |
| `std.text.template` | einfache Mustache-artige Templates | P3 |

### B3. Daten und Formate

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.serial` | gemeinsame `Encoder`/`Decoder`-Interfaces, per Synthese abgeleitet, damit JSON, TOML und später MessagePack **eine** Mechanik teilen | P1 |
| `std.json` | `ToJson`/`FromJson` per Synthese, Builder, `path("a.b[0]")`, Streaming-Reader/-Writer, Optionen für die Ausgabe | P1 |
| `std.toml`, `std.csv` | Lesen und Schreiben (TOML ist ohnehin das natürliche Format für `lyric.json` v3 und Konfiguration) | P2 |
| `std.encoding` | URL-/Prozent-Kodierung, `base64url`, `base32`, UTF-16, Quoted-Printable | P2 |
| `std.bytes` | `ByteBuffer`/`BytesBuilder`, Lesen und Schreiben in LE/BE, `equals`, `compare`, Views | P1 🟡 |
| `std.uuid` | v4 und v7 | P2 |
| `std.xml` | DOM und Pull-Parser | P3 |

### B4. Mathematik und Zeit

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.math` | Überladung statt Suffixe; Bit-Funktionen (`popCount`, `leadingZeros`, `rotateLeft`, `byteSwap`); `fma`, `nextUp`, `approxEq`; generische Mathematik über `Num` | P1 |
| `std.math.big` | `BigInt`, `Decimal` (.NET `BigInteger`/`decimal`) | P2 |
| `std.random` | spezifizierter Algorithmus (PCG oder xoshiro, reproduzierbar über Plattformen), Verteilungen, `sample`, gewichtete Auswahl | P2 |
| `std.time` | `Duration` bis Nanosekunden und bis Tage; `Monotonic`/`Stopwatch`; `sleep(Duration)`; `Date`, `Time`, `DateTime`; **Zeitzonen** (IANA über `TimeZoneInfo`); Formatieren und Parsen mit Mustern | P1 🟡 |

### B5. I/O, System, Netz

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.io` | **gemeinsame `Reader`/`Writer`/`Seek`-Interfaces** über Datei, Socket, Prozess und Konsole; gepufferte Varianten; `copy(reader, writer)` | P1 |
| `std.io.file` / `path` | `walk`, `glob`, `removeAll`, Temp-Dateien, Metadaten-Struct, Rechte; `normalize`, `relative`, `components`, ein `Path`-Typ | P1 🟡 |
| `std.io.watch` | Datei-Überwachung | P3 |
| `std.process` | `output()`, ein `Spawn`-Struct (cwd, env, Pipes), Pipelines, Signale | P1 🟡 |
| `std.os` | Signal-Handler, Iteration über Umgebungsvariablen | P2 |
| `std.cli` | Argument-Parser, deklarativ über Attribute oder Synthese (`@Command`, `@Flag`), mit Hilfetexten | P1 |
| `std.term` | ANSI-Farben, Terminalgröße, Raw-Modus, Fortschrittsbalken | P2 |
| `std.log` | strukturiertes Logging mit Levels und austauschbarer Senke | P2 |
| `std.net` | `IpAddr`/`SocketAddr`, DNS-Auflösung, `Url`-Typ, Timeouts | P1 |
| `std.net.tls` | TLS-Client und -Server (`SslStream`); löst die offene TLS-Entscheidung aus STATUS.md | P1 |
| `std.http` | HTTP/1.1-Client und einfacher Server (Router, Middleware als Lambdas), auf dem Task-Scheduler | P1 |
| `std.net.ws` | WebSocket | P3 |

### B6. Nebenläufigkeit, Sicherheit, Tests

| Modul | Neu bzw. Ausbau | Prio |
|---|---|---|
| `std.task` | `Task<T>`-Handle mit `join`, `Channel<T>` (gepuffert und ungepuffert), `select`, `timeout`, Abbruch-Token, Timer; **alle** Streams nicht-blockierend im Scheduler (dank stackful Coroutines ohne Funktionsfärbung) | P1 🟡 |
| `std.crypto` | HMAC, SHA-512, sicherer Vergleich, PBKDF2/Argon2, AES-GCM, Ed25519, X25519 | P2 |
| `std.compress` | gzip, deflate, brotli (in .NET enthalten); `std.archive` für zip und tar | P2 |
| `std.test` | `assertThrows` (braucht #5), tabellengetriebene Tests, Snapshot-Tests, **Property-based Testing**, `@Bench`, Setup und Teardown, Ressourcen-Isolation pro Test | P1 |
| `std.db.sqlite` | SQLite-Bindung | P3, nur wenn `extern "dotnet"` Stufe 2 steht |

---

## C. Bewusst weiterhin nicht dabei

Diese Punkte bleiben draußen, weil sie Rule 2 oder frühere Designentscheidungen verletzen würden:

- `??T` und ein `Option`-Enum neben `?T`
- ein `?`-Operator auf `Result` (die Rolle übernimmt `throws`)
- Vererbung und `finally`
- Shared-Memory-Threads
- ein allgemeines Makrosystem
- Laufzeit-Reflexion (Werte tragen keinen Typ-Tag; Synthese und `comptime` decken den Bedarf ab)

## D. Außerhalb von Sprache und std, aber nötig

Eine ausgewachsene Sprache braucht auch Werkzeuge:

- **ein Paketmanager** mit Versionen, Lockfile und Registry (heute nur lokale Pfade)
- `lyrfix` für die 5.0-Migration
- Ausdrücke im Debugger-Evaluate
- einen Profiler
- Coverage

---

## Empfohlene Reihenfolge

1. **4.7 / 4.8:** A1 #1–#6 plus B1. Das ist das Fundament: Fast alles in B3–B6 hängt an Synthese, statischen Interface-Membern und bedingter Konformanz.
2. **4.x parallel:** Warnstufen für alle A3-Brüche.
3. **5.0:** die Brüche plus die großen Module: `regex`, `http`/`tls`, `time` mit Zeitzonen, `io`-Interfaces, `task` mit Channels.

> Hinweis: CONTRIBUTING Rule 1 sieht für Ideen nach v1 GitHub-Issues mit dem Label `idea` vor, kein Roadmap-Dokument im Repo. Deshalb liegt diese Datei außerhalb der Repositories.
