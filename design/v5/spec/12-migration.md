# 12 — Migration 4 → 5

Lebendes Dokument des Bereichs 12. Fragen R1–R7, je **entschieden** oder **offen**. Basis:
00 (harter Cut, Migrationswerkzeug wo mechanisch, manuelle Migration akzeptabel, keine
4.x-Warnstufen), 07 (Editionen, `lyric fix --edition`), 09 A11 (`@Deprecated.replacement`),
10 SL-28 (mechanisch vs. semantisch), 11 G7/W1 (`lyric fix` als Verb über Diagnose-Fixes),
`../cli.md` CLI-19, `../diagnostik.md` D13/D14/D19–D22 (4.x-Migrationsfamilie — für 5
gegenstandslos, aber lehrreich).

## Bestandsaufnahme: was migriert werden muss

**Der Korpus in Lyric 4** (Stand 4.6): `stdlib/std` 19 Module, ~8 400 Zeilen; `examples/` 22
Programme plus Unterordner (`ffi`, `lambdas`, `macros`, `patterns`, `embedded-host`);
`stdlib-tests/` 22 Dateien; Guide 21 Kapitel, jedes Snippet kompiliert von der Suite;
Konformanzsuite im Spec-Repo (~158 Fälle, `since:`-Gates bis 4.6); Golden-Tests im Compiler
(97 Parsing, 27 Sema, 19 Lowering — **die fallen mit dem Compiler-Umbau ohnehin**); Erato
(eigene Runtime Lyricpp, C++ — Abnehmer, nicht Voraussetzer).

**Was sich für ein 4.x-Programm ändert** (aus 00–11, nach Art sortiert):

| Art | Änderungen |
|---|---|
| **Mechanisch** (Textersatz mit Typwissen) | `module`-Kopf fällt (07); `params` → `...`; `inout` → `&`; `throws A, B` → `throws [A, B]`; `catch (e: A \| B)` → `catch (e in [A, B])`; `opaque type` → Ein-Feld-Struct; `Equatable<T>` → `Equatable`; `Indexable<T>` → `Index<K>`; `Iterator<T>` → `Iterator<Item = T>`; freie Terminatoren/`listX`/`keys(m)` → Methoden; `xOrThrow(…)` → `x(…)`, stille Formen → `try? x(…)`; `absInt` → `abs`; `parseInt(s)` → `int.parse(s)` mit `try?`; `std.io.file.text` → `fs.readText`; `std.os.nowMillis` → `Instant.now()`; Modulpfade (`std.io.file` → `std.fs`); `combineHash` → `Hasher`; `println` braucht `import std.io`; `@Deprecated`-Ersetzungen; `@Repr`→`@Layout`-artige Attributnamen; `str` → `StringView` |
| **Semantisch** (Regel ändert Bedeutung; Werkzeug meldet, Mensch entscheidet) | Felder unveränderlich außer `var` (02: „jedes geschriebene Feld bekommt `var`" — mechanisch bestimmbar, aber Absicht prüfen); `abs(-3)` int statt float (Literal-Adaption); Überlauf = Panik statt Wrap (T2) — jede Stelle, die auf Wrap baute, braucht `+%`; `string.length()` in Bytes statt Codepoints (S1); `s[i]` = `char` an Byte i; `Map`-Iterationsreihenfolge je Lauf anders (K2); Ganzzahl-Weitung implizit, `int`→`float` nie; `as` nur numerisch; `Clone` tief (K5); `==` nur `Equatable` (kein `===`); `try` Pflicht an jeder werfenden Stelle; `for` über werfende Iteratoren mit `try`; `main` darf werfen; Panik unfangbar außer Task-Grenze; `spawn` gibt `Task`; Capabilities/`--grant` weg; `extern "dotnet"` weg (FFI neu über C) |
| **Strukturell** (kein Werkzeug) | Sichtbarkeit `internal` als Default statt `pub`-Ratchet; Pakete mit `lyric.toml` statt `lyric.json`; `build.lyr` definiert nichts mehr (P1/BS1); Tests im Paket; Prelude; Bytecode/`lyrpack`/VM-Werkzeuge entfallen; Embedding-API neu (H1–H8) |

## Fragen

Reihenfolge: R1 (Werkzeugform) → R2 (Regelkatalog) → R3 (Reihenfolge der Korpora) → R4 (Spec
und Suite) → R5 (Repo/Org) → R6 (4.x-Linie und Lyric-Script) → R7 (Umsetzungsplan — das
Ergebnis der ganzen Runde).

## R1 — Das Werkzeug: `lyric fix --from-4`: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| F1 | **Das 4.x-Frontend (Parser + Sema, C#) eingefroren als Bibliothek im 5er-`lyric`**, nur für `fix --from-4`: liest 4.x mit vollem Typwissen (Empfängertyp für `abs`, Schreibstellen für `var`), wendet die Regeln aus R2 an, druckt 5er-Syntax | `go fix`, `cargo fix`; verworfen: toleranter 5er-Parser mit 4-Modus (zwei Grammatiken, kein 4-Sema), Textregeln (`abs(` ohne Typ nicht entscheidbar), Migrator bei Lyric-Script (driftet mit Z4 weg) |
| F2 | **Form**: `lyric fix --from-4 [pfad]` schreibt um (`--dry-run`, `--json` nach G8), konvertiert `lyric.json` → `lyric.toml`, entfernt Modulköpfe, schlägt das Layout vor; **mechanische** Regeln schreiben, **semantische** werden `needsReview`-Notizen mit Erklärung und Vorschlag (SL-28) | — |
| F3 | **Kein Versprechen, dass das Ergebnis kompiliert** — danach `lyric check`; Maßstab ist der `examples/`-Lauf (R3). Verfallsdatum: eine Major-Linie, danach eingefroren | — |

## R2 — Der Regelkatalog: **entschieden** (2026-09-29)

**Verifiziert gegen `docs/Grammar.md` und `docs/guide/01–21` (4.6)** — eine erste Fassung aus
dem Gedächtnis hatte fünf Formen erfunden (`println` ohne Import, `inout`, `throws A, B`,
`catch (e: A | B)`, `===`); der Maintainer hat sie gefangen. Art: **M** = mechanisch (`fix`
schreibt), **R** = Review (`fix` schlägt vor, `needsReview`), **H** = Hand (nur Meldung).
Jede Regel bekommt einen Vorher/Nachher-Fall in `tests/fix-from-4/`. **Listenregel** (08
D5/D6): einzelne `[I]` bleiben gültig — `fix` fasst sie nicht an.

| # | 4.x (Beleg) | 5 | Art | Braucht |
|---|---|---|---|---|
| **Module / Pakete** | | | | |
| K01 | `module a.b;` (Grammar §2, guide 12) | entfällt (07) | M | Dateipfad |
| K02 | `lyric.json` (`name`, `sourceRoot`, `dependencies`, `nativeRoots`, `toolchain`; guide 12) | `lyric.toml` (`[package] name/version/edition`, `[dependencies] x = { path }`) ; `nativeRoots` → `[native]` (R) | M/R | — |
| K03 | `import std.io.console { println }` (guide 01) | `import std.io { println }` | M | — |
| K04 | `import std.io.file` / `std.io.stream` / `std.io.net` / `std.io.path` / `std.result` / `std.option` / `std.bytes` / `std.io.error` | `std.fs` / `std.io` / `std.net` / `std.path` / `std.core` / `std.core` / `std.encoding` / `std.io` | M | — |
| K05 | `import std.core { panic, assert, Exception }`, `import std.test { Test }` | Prelude — Import entfällt | M | — |
| K06 | nicht-`pub` = modulprivat | nicht-`pub` = `internal` (paketweit) — Sichtbarkeit wächst still | R | Hinweis je Modul |
| K07 | `build.lyr` mit `executable("app", "src/main.lyr")`, `option`, `flag`, `Profile`, `packed`, `library` (guide 16) | `[[bin]]`, `[profile.*]` im Manifest; `packed` entfällt; Rest des Skripts bleibt (BS1) | R | — |
| K08 | `stdlib-tests/`-Fremdkompilation | `tests/`-Root im Paket (V4) | H | — |
| **Syntax** (Grammar §1.4, §3–7) | | | | |
| S01 | `params xs: int[]` (§3.1) | `xs: int...` | M | — |
| S02 | `throws T` (ein Typ) / `throws` bar (§3.1, guide 10) | bleibt (`throws [A, B]` neu; Listenregel) | — | — |
| S03 | `catch (e: T)`, `catch (e)`, `catch (_)` (§5) | bleiben (`catch (e in [A, B])` neu) | — | — |
| S04 | `resume c` (Keyword; guide 11) | `c.next()!` (Panik bei Ende wie `resume`); `Coroutine<void>.next(): bool` bleibt | M | — |
| S05 | `opaque type H = int` (§2) | `struct H { v: int }`; `as`-Übergänge werden Feldzugriff/Konstruktion | R | Nutzungsstellen |
| S06 | `Red =>` (bloßer Variantenname im Pattern, typgerichtet) | `.Red =>` (bloßer Name bindet immer, 08) | M | Sema: war es eine Variante? |
| S07 | `'\xNN'` in `string`/`char` (§1.5) | `\u{NN}` (L4; `\xNN` nur in `b"…"`) | M | — |
| S08 | f-String-Specs `{pi:N2}`, `{x:C}` (guide 02; .NET-Grammatik) | eine Formatsprache (Y7): `:N2` → `:.2f`, `:C` → Hand | M/R | Spec-Tabelle |
| S09 | `Coroutine<T> throws E` als Typsuffix (§4) | bleibt | — | — |
| S10 | `xs.length` (Array-**Feld**, guide 02) vs `s.length()` | `length()` überall (N8) | M | Typ |
| S11 | `x` mit `Iterator<T>`/`Iterable<T>`, `Indexable<T>` als Typargument (guide 13) | `Iterator<Item = T>`, `Iterable<Item = T>`; Konformanz `:: Iterator { type Item = T; … }`; `Indexable<T>` → `Index<int> { type Output = T }` + `IndexSet<int>` | M/R | Methodenrümpfe |
| **Interfaces / Werte** (guide 05, 07, 08) | | | | |
| T01 | Felder ohne Modifikator, beschrieben (`b.x = 99`) | `var` an jedem geschriebenen Feld (02) | R | Schreibstellen |
| T02 | `Equatable<Point>`, `Ordered<Version>`, `Hashable<K>` (guide 07/08) | `Equatable`, `Ordered`, `Hashable` (`Self`, T-Reihe) | M | — |
| T03 | `Add<Vec2, Vec2>`, `Mul<float, Vec2>`, `Sub<…>` (zwei Typargumente: Rhs, Out) | `Add<Rhs = Vec2> { type Out = Vec2 }` (D6; Rhs Parameter, Out assoziiert) | M | — |
| T04 | `fn compare(other: T): int` (guide 07) | `compare(o: Self): ?Ordering`; `TotalOrder.totalCompare` dazu | R | — |
| T05 | `fn hash(): int` + `hashCombine`/`combineHash` | `hash<H :: Hasher>(&h: H)` mit `h.write…` (K2/Q3) | R | — |
| T06 | `equals(other: T): bool`, `show(): string`, `message(): string` | bleiben | — | — |
| T07 | `Into<Fahrenheit>` | bleibt (`From<T>` dazu) | — | — |
| T08 | `int32`/`int64`/`float64` distinkt neben `int`/`float`; `x as int64` | `int` = `int64`-Alias: `int64` → `int`, `as` zwischen gleich breiten entfällt | M | Typen |
| T09 | Überlauf wrappt still (Spec §3.2) | Panik (T2); Stellen, die Wrap wollen: `+%` | R | Bericht |
| T10 | `abs(-3)` → `float` (Literal-Adaption) | `abs(-3)` → `int`; `fix` schreibt `-3.0`, wo `float` erwartet war | M | Kontexttyp |
| T11 | `absInt`/`minInt`/`maxInt`/`clampInt`/`signInt`, `sum`/`sumFloat` | `abs`/`min`/`max`/`clamp`/`signum`, `sum()` | M | — |
| T12 | `s.length()` in Codepoints, O(n) (guide 13) | Bytes, O(1); `charCount()`, wo Zeichen gezählt werden | R | — |
| T13 | `s.charAt(i)`, `s.substring(start, count)` (Codepoint-Indizes) | `s[i]` (Byte), `s[a..b]` (Byte) — andere Semantik; Vorschlag `s.chars().nth(i)` / Byte-Grenzen aus `find` | R | — |
| T14 | `s.split(",")`, `splitLines()` → `string[]` | `split(",")`, `lines()` → Iterator (`.toArray()` anhängen, wo ein Array gebraucht wird) | M | Nutzungstyp |
| T15 | `s.indexOf(n): int` (−1) | `find(p): ?int` | R | — |
| T16 | `s.toChars()`, `s.utf8Encode()`, `utf8Decode(bytes): ?string` | `chars().toArray()`, `asBytes().toArray()`, `try? string.fromUtf8(bytes)` | M | — |
| T17 | `?T` nicht schachtelbar; `Iterator<?T>` verboten | `??T` erlaubt — nichts zu tun | — | — |
| T18 | `T[]` als `Map`-Schlüssel (war Lowering-Loch) | Übersetzungsfehler → Wrapper | H | — |
| **Fehler** (guide 10, 13) | | | | |
| E01 | `xOrThrow(a)` (37 Zwillinge) | `x(a)` mit `try` | M | Bibliothekstabelle |
| E02 | stille Form `x(a): ?T`/`bool` mit Grund (`file.text`, `writeText`, `json.parse`) | `try? x(a)` bzw. `try x(a)` (bool-Operationen werfen) | M | Bibliothekstabelle |
| E03 | `xOrErr(a)`, `std.result { Result }` | `Result.of { try x(a) }`; `Result` im Prelude | M | — |
| E04 | werfender Aufruf ohne Markierung | `try f()` an jeder werfenden Stelle (E4) | M | Sema |
| E05 | `Throwable` (eingebaut), `class X :: [Throwable] { fn message() }` | `Error` (`message()`, `cause()`) | M | — |
| E06 | `Exception { text = "…" }` (`std.core`, Feld `text`) | `Exception { message = "…" }` — **bleibt der Fertigtyp** (`:: Error`, `message`, `cause = null`), Feld heißt `message` | M | — |
| E07 | `lastErrorKind()`/`lastErrorDetail()`-Rituale | entfällt (`IoError.kind`) | R | — |
| E08 | `main` darf nicht `throws`; `catch` um alles | `main` darf werfen (O4) — Vorschlag: `throws` an `main`, Hülle weg | R | — |
| E09 | `defer stream.close(f)` | `using let f = …` (Closeable, R1) — Vorschlag | R | Typ |
| **Koroutinen / Tasks** (guide 11, 13) | | | | |
| N01 | `fn worker(): Coroutine<Wait>`, `yield Wait.Now` / `Wait.Sleep(ms)` / `Wait.Readable(fd)` / `Wait.Interrupt` | Task-Funktion ohne `Wait`: `yieldNow()`, `sleep(Duration.ofMillis(ms))`, I/O parkt selbst, `os.signals(Signal.Interrupt)` | R | — |
| N02 | `spawn(worker("a"))` (Koroutinenwert), `run()` | `spawn { worker("a") }` → `Task`; `run()` entfällt (`main` ist Task) | M/R | — |
| N03 | `co.next()`, `Coroutine<int>` als Wert, `for` über Koroutine nicht möglich | bleiben; `for (x in co)` jetzt erlaubt | — | — |
| **Bibliothek** (guide 13) | | | | |
| B01 | freie `over(xs)`, `range(a, b)`, `rangeInclusive`, `collectArray(it)`, `sum(it)`, `compact(it)`; freie `groupBy(xs, k)`, `sortList(xs)`, `sortListByKey`, `sortArray`, `maxBy`/`minBy`, `slice`, `mapList`, `listContains`, `listRemove`, `union`/`intersect`/`difference`/`isSubset`; `std.option { map, andThen, filter, expect }` | Methoden: `xs.iter()`, `(a..b)`, `it.toArray()`, `it.sum()`, `it.compact()`, `xs.groupBy(k)`, `xs.sort()`, `xs.sortByKey`, `xs.maxBy`, `xs[a..b]`, `xs.map`, `xs.contains`, `xs.remove`, `a.union(b)`; `opt.map(f)` … | M | Empfängertyp |
| B02 | `parseInt(s): ?int`, `parseIntOrErr`, `parseFloat`, `parseBool` | `try? int.parse(s)`, `Result.of { try int.parse(s) }` … | M | — |
| B03 | `fromInt(n)`/`fromFloat`/`fromBool`/`fromChar`; `"n = " + fromInt(n)` | f-String `f"n = {n}"` / `n.toString()` | M | — |
| B04 | `file.text(p)`/`bytes`/`lines`/`writeText`/`appendText`/`exists`/`remove`/`copy`/`move`/`createDir[All]`/`removeDir`/`entries`/`size`/`modifiedMillis`/`tempDir` | `fs.readText`/`readBytes`/`lines`/`writeText`/`appendText`/`exists`/`remove`/`copy`/**`rename`**/`createDir[All]`/`removeDir`/`readDir`/`metadata(p).size`/`metadata(p).modified`/`fs.tempDir` | M | — |
| B05 | `stream.open(p)!`, `readSome(f, n): ?uint8[]`, `write(f, bytes)`, `stream.close(f)`, `stream.lineReader(f)` | `try File.open(p)`, `f.read(into: buf)` (Pufferübergabe — R), `f.writeAll(bytes)`, `f.close()`/`using`, `TextReader.new(f).lines()` | M/R | — |
| B06 | `std.os.nowMillis()`/`nowNanos()`/`sleep(ms)` | `Instant.now().epochMillis()` / `Monotonic.now()` / `sleep(Duration.ofMillis(ms))` | M | — |
| B07 | `Instant.ofEpochMillis`, `.plus(d)`, `.since(o)`, `Duration.ofMillis`, `.totalMillis()` | bleiben; `.plus(d)` → `+ d`; `totalMillis()` → `millis()` | M | — |
| B08 | `List<int>.of([…])`, `.empty()`, `push`, `pushAll`, `xs.get(i)` (panikt), `groups.get(2)!`, `getOrInsert(k, () => …)`, `m.set(k, v)` | bleiben; `xs.get(i)` → `xs[i]` (`get` liefert jetzt `?T`), `m.get(k)!` → `m[k]!`, `m.set(k, v)` → `m[k] = v` | M | — |
| B09 | `hexEncode`/`hexDecode`/`base64Encode`/`base64Decode`, `sha256Hex(text)`, `secureRandom(n)` | `Hex.encode`/`Hex.decode`/`Base64.encode`/`Base64.decode`, `Hex.encode(sha256(s.asBytes()))`, `crypto.randomBytes(n)` | M | — |
| B10 | `json.parse(text): ?JsonValue`, `parseOrThrow`, `doc.field("n")`, `doc.at(i)`, `asString()`, `JsonValue.Obj(m)`, `serialize(v)`, `serializePretty(v, 2)` | `try? json.decode<JsonValue>(text)`, `try json.decode<JsonValue>`, `doc["n"]`, `doc[i]`, `asString()` bleibt, `JsonValue.Object(m)`, `json.encode(v)`, `json.encode(v, JsonOptions { pretty = true, indent = 2 })` | M | — |
| B11 | `Random.seeded(42)`, `shuffle(r, xs)`, `choice(r, xs)`, `nextGaussian` | bleiben / `r.shuffle(xs)`, `r.choice(xs)`, `nextNormal`; **Zahlenfolge anders** (ChaCha8) | M/R | — |
| B12 | `process.start(p, args)`, `readSomeOut(child, n)`, `write(child, b)`, `closeStdin`, `wait`, `kill`, `close` | `Command { program, args }.spawn()`, `child.stdout!.read(into)`, `child.stdin!.writeAll`, `child.stdin!.close()`, `wait()`, `kill()` | R | — |
| B13 | `net.listen`/`accept`/`connect`/`readSome`/`write`/`close`/`bind`/`sendTo`/`receiveFrom` | `TcpListener.bind`/`accept`/`TcpStream.connect`/`read`/`writeAll`/`close`/`UdpSocket.bind`/`sendTo`/`recvFrom` | R | — |
| B14 | `assertEq`, `assertTrue(c, msg)`, `@Test` aus `std.test` | bleiben; `@Test` Prelude | — | — |
| B15 | `@Deprecated { message, until }` | bleibt (+ `replacement`) | — | — |
| B16 | `extern "dotnet" fn … = "System.Math::Cbrt"` (guide 14) | entfällt — FFI über C (W4) | H | — |
| B17 | `OnType`/`OnFunction`/`OnModule`/`WithArg` (guide 15) | bleiben (F1-Marker) | — | — |

## R3 — Reihenfolge der Korpora: **entschieden** (2026-09-29)

| # | Korpus | Weg | Warum |
|---|---|---|---|
| Q1 | **`std`** | **neu schreiben**, nicht migrieren — 35 Module nach B1, neue Grenze, neue Verträge; die 4.x-Quelle ist Steinbruch für getestete Algorithmen (Map, Merge-Sort, JSON, Base64, RFC 3339) | Regel D und die neuen Interfaces machen jede Datei zu 70 % anders |
| Q2 | **Guide** | neu aus den Spec-Dokumenten; Kapitel folgen den Bereichen; jedes Snippet in der Suite | der 4.x-Guide erklärt eine andere Bibliothek |
| Q3 | **`examples/`** | **mit `fix --from-4` migrieren** — der Abnahmetest: was danach nicht kompiliert, ist eine fehlende Regel (R2 ergänzen) oder ein `needsReview`-Fall im Bericht | misst das Werkzeug ehrlich |
| Q4 | **Konformanzsuite** | neu (R4); 4.x-Fälle wandern mit Lyric-Script | — |
| Q5 | **Golden-Tests** des Compilers | fallen; Ersatz: Konformanzfälle + C-Emission-Goldens | Compiler-Umbau |
| Q6 | **Erato** | unberührt (Lyricpp) | Z-Reihe |
| Q7 | `design/`, `docs/Befunde_und_Verbesserungen/`, `PLAN.md`, `STATUS.md` (4.x) | archivieren (`docs/archive/4.x/`); `design/v5/spec` ist die Quelle | — |

## R4 — Spec 5.0 und Konformanzsuite: **entschieden** (2026-09-29)

| # | Entscheidung |
|---|---|
| P1 | **Die Spec wird von Hand geschrieben**, nicht generiert (Bereichsdokumente tragen Begründung und Verworfenes, die Spec nur die Regel). Kapitel: `01-lexical`, `02-grammar`, `03-types`, `04-modules`, `05-interfaces`, `06-errors`, `07-statements`, `08-expressions`, `09-patterns`, `10-concurrency`, `11-metaprogramming`, `12-stdlib` (Verträge), `13-abi` (X2, `@Layout`, Einbettung H1–H3 — ersetzt `13-bytecode`), `14-cli` (C10), `15-project` (W2), `16-diagnostics` (G-Reihe), Appendix A **generiert** aus `diagnostics.toml` (G9) |
| P2 | **Suite neu**, jeder Fall `since: 5.0.0`; **spec-first bleibt**: Regel-PR mit Konformanzfall (Zwilling) → Compiler; der Compiler pinnt einen Spec-Commit; jede Warnung rot/grün (G10) |
| P3 | Die 4.x-Spec bleibt als Spec von Lyric-Script (R6) |

## R5 — Repository und Org: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| O1 | **`lyric` bleibt das Repo von Lyric 5**: `main` wird 5-Entwicklung (`dev`-Kanal, T5), `release/4.x` Wartungszweig bis zum Schnitt | Go, Rust |
| O2 | **Neues Repo `lyric-script`** aus dem 4.6-Tag (Compiler, VM, stdlib, Werkzeuge) — die 4.x-Linie lebt dort weiter (R6) | — |
| O3 | **`lyric-spec`**: `main` = 5.0; Zweig `script` = 4.x-Spec, eigenes Repo sobald Lyric-Script sich bewegt | — |
| O4 | Clients `vscode-lyric`/`jetbrains-lyric` sprechen `lyric lsp` (5); Fork `…-script` für den Script-Server; **neu `tree-sitter-lyric`** (E8) | — |
| O5 | Erste Release **`5.0.0`**, davor `5.0.0-dev.<datum>+<sha>` | SemVer |

## R6 — Die 4.x-Linie wird Lyric-Script: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| L1 | **4.x endet nicht, es zieht um**: letzte Release unter `lyric` ist die beim Schnitt (4.6/4.7); die erste unter **`lyric-script 1.0.0`** ist dieselbe Codebasis umbenannt — Binary `lyric-script`, Endung **`.lyrs`**, eigener Versionszähler ab 1.0, Spec „Lyric-Script 1.0" = Lyric-Spec 4.x | verworfen: Weiterzählen als 4.7 (zwei Produkte, eine Nummernlinie) |
| L2 | Bugfixes der 4.x-Linie geschehen in Lyric-Script; **kein 4.x-Release mehr unter `lyric`** nach dem Schnitt | — |
| L3 | Rückfluss aus 5 (Formatsprache, Lints, Diagnostikform, `lyric.toml`) entscheidet die Script-Roadmap — nichts ist Pflicht | Z4 |

## R7 — Der Umsetzungsplan: **entschieden** (2026-09-29) → `13-umsetzungsplan.md` (M0–M18, Abhängigkeiten, Messpunkte)

Das eigentliche Ergebnis der Runde (Maintainer: „einen detailreichen Plan, nach dem Lyric 4
stückweise auf 5 gebracht wird"). Meilensteine mit konkretem Artefakt je Schritt (CONTRIBUTING
Rule 3): Reihenfolge Runtime-Bring-up (Boehm-Stufe) → C-Backend für einen Sprachkern →
Wertmodell/Typsystem → Fehler-ABI → Koroutinen/Scheduler → std-Kern → Module/Manifest →
Metaprogrammierung → volle std → Werkzeuge → Migration → 5.0. Welche Teile parallel, was der
erste lauffähige Meilenstein ist (Hello World nativ), wo die Messpunkte (Z2) sitzen. Eigenes
Dokument `13-umsetzungsplan.md`?
