# Lyric 5 — Gebiet: Standardbibliothek, Umfang und Aufbau (Fassung 2 nach der Kritik)

Stand der Messungen: 2026-09-28, main @ `6f6f029f` (4.6.0-Cut), Binaries aus
`src/Lyrc/bin/Debug/net10.0` und `src/Lyrvm/bin/Debug/net10.0`. Proben in
`…/scratchpad/v5-design/probes/stdlib/` (Erstfassung, `p*`), `…/probes/stdlib-adv/` (Kritik, `q*`)
und `…/probes/stdlib-rev3/` (diese Fassung: `q*` nachgemessen, `p31`–`p36` neu; Erwartungen
in `ERWARTUNG.txt` vor jedem Lauf).

Belegarten: **gemessen** = Probe kompiliert/gelaufen · **gelesen** = Pfad:Zeile · **behauptet** =
ausdrücklich so markiert.

---

## 1. Ist-Stand

### 1.1 Die Bibliothek in Zahlen

| | | Beleg |
|---|---|---|
| Module | 25 (`stdlib/std/`, davon 6 unter `std/io/`) | gemessen (`find -name '*.lyr'`) |
| Zeilen Lyric | 8 403 (`collections.lyr` 1439, `iter.lyr` 993, `json.lyr` 822 …) | gemessen |
| Funktionsdeklarationen (alle Ebenen) | 723 | gemessen (`grep -E '^\s*(pub )?(static )?(mut )?fn '`) |
| davon rumpflose Top-Level-`fn` (nativ) | 151 | gemessen (`^(pub )?fn …;`) |
| **Registrierungsaufrufe** in der Native-Registry | 138 — die Datei hat 2 842 Zeilen | gemessen (`grep -c 'Register…("'`, `wc -l`) — Etikett in Fassung 1 falsch |
| Interfaces in der ganzen Bibliothek | **17**: `Display`, `Equatable`, `Hashable`, `Ordered`, `Add`, `Sub`, `Mul`, `Div`, `Into`, `OnModule`, `OnType`, `OnFunction`, `OnMethod`, `WithArg` (`core.lyr`), `Indexable` (`collections.lyr:19`), `Iterator`/`Iterable` (`iter.lyr:21,421`) | gemessen |
| Interfaces für I/O | **0** | gemessen |
| `OrThrow`-Zwillinge | **37** (Deklarationen; `writeOrThrow` dreimal, `readSomeOrThrow` zweimal) | gemessen — Fassung 1 sagte 36 |
| `OrErr`-Formen | 5 (`encoding.lyr:101`, `io/file.lyr:102`, `json.lyr:364`, `string.lyr:536,542`) | gemessen |
| `@Deprecated` als Attribut in der Bibliothek | **1** (`build.lyr:179`); zwei weitere Treffer sind Kommentare (`core.lyr:528`, `iter.lyr:872`) | gemessen |
| `lastError*`-Natives | 8 (vier Module × `lastErrorKind`/`lastErrorDetail`) | gemessen |
| `Display`-Konformanzen | 7: `int, float, bool, char, string` (`core.lyr:106-134`), `Duration`, `Instant` (`time.lyr:26,110`) | gemessen (`grep 'Display\]'`) |

Native-Anteil pro Modul (gemessen, nur Top-Level-`fn`, Fassung 1 unverändert):
`std.os` 13/14 · `std.string` 25/42 · `std.io.file` 22/39 · `std.math` 18/50 · `std.io.net` 15/38 ·
`std.process` 11/25 · `std.io.console` 9/15 · `std.core` 9/13 · `std.io.stream` 8/22 ·
`std.fmt` 6/17 · `std.build` 6/17 · `std.hash` 4/9 · `std.random` 2/4 · `std.task` 2/15 ·
`std.time` 1/9 · **0 nativ**: `collections`, `iter`, `json`, `encoding`, `option`, `result`,
`test`, `bytes`, `io/path`, `io/error`.

### 1.2 Was der Compiler tut, wo die Doku etwas anderes verspricht

**(a) `println` auf einem Array stirbt in der Bibliothek, nicht beim Nutzer.** Gemessen
(`p1_display_array.lyr`): `error[LYR-IR0001]: call to 'show' on 'int[]'` in `console.lyr:51`.
Kontrolle `println(42)` → `42`. `println(ioError)` liefert dagegen sauber `LYR-SEM0028` in der
Nutzerdatei (`p18_err_display.lyr`). `docs/guide/02-values-and-types.md:103-104` dokumentiert die
Lücke als Absicht; die Diagnose verrät sie als Loch. In 4.6 unverändert.

**(b) `abs(-3)` ist still ein `float`.** Gemessen: `abs(-3)` läuft und druckt `3` (`p2`);
`let x: int = abs(-3)` ist `cannot assign 'float' to 'int'` (`p4`); `let n = -3; abs(n)` ist
`LYR-SEM0001` (`p3`). Kontrolle `absInt(-3)` → `3`. `math.lyr:22` deklariert `abs(value: float)`,
`:85` `absInt`. **Die Auflösungsregel für Überladung ist geschrieben** (`guide/03-functions.md:76-77`:
exakter Treffer schlägt Adaption, `f(2)` mit `f(int)`/`f(float)` ist die int-Form) und gemessen
(`q13_overload_literal.lyr`: `pick(3)` → `int`, `pick(3.0)` → `float`). Ein Import bringt den ganzen
Überladungssatz (`guide/03:95-96`). Die Bibliothek nutzt beides nicht — nur `net.lyr:107,253,221,317`
überlädt (`localPort`, `close`). — *Fassung 1 behauptete hier eine fehlende Regel; falsch.*

**(c) Die Sprache hat 13 Zahlentypen, die Bibliothek bedient sie ungleich.** `Grammar.md:319-322`:
`int, uint, float, int8…int64, uint8…uint64, float32, float64`. Gelesen `core.lyr`:
`int`, `char`, `string`, `bool` haben Equatable/Ordered/Hashable (bool ohne Ordered); **`uint` hat
Equatable, Ordered, Hashable** (`core.lyr:319-337`) **und Add/Sub/Mul/Div** (`:406-424`), ihm fehlt
**nur Display**; **`float` hat kein Hashable** (`core.lyr:243-259` nur Equatable/Ordered);
**die zehn schmalen Typen haben nichts** (`grep 'extend uint8|int32|…' core.lyr` = 0). Gemessen:
`println(u)` mit `u: uint` → `SEM0028 'uint' does not satisfy 'Display'` (`p21`); `Map<uint, int>`
läuft (`q03c` → `1`); `Map<float, int>` → `SEM0028 'float' does not satisfy 'Hashable<float>'`
(`q03e`); `fromInt(a)` mit `a: int32` → Typfehler (`p22b`); `int32`-Arithmetik läuft (`p22` → `10`).
Der Bytepuffer der I/O-Schicht ist `uint8[]`, und ein einzelnes Byte lässt sich nicht drucken.
— *Fassung 1 („zwei bedient“, „52 Konformanzen“) war in beide Richtungen ungenau.*

**(d) `std.build` ist von jedem Programm importierbar und stirbt erst zur Laufzeit.** Gemessen
(`p16b_build_call.lyr`): ein `main`-Programm mit `executable("app", "main.lyr")` kompiliert, meldet
`capabilities 0x0` und scheitert beim Lauf mit `LYR-VM0005: no native implementation for
'std.build.selectedProfile'`. Gelesen: die Natives bindet der Build-Runner (`Lyrbuild/BuildSession.cs:66-96`),
der Einstieg ist ein synthetisiertes `main` um `build.lyr` (`Lyrbuild/Program.cs:31,285-305`);
`spec/11-stdlib-contract.md:32-36` nennt das ausdrücklich als die eine Ausnahme des Native-Vertrags.
Der Compiler weiß nichts davon (`std.build` steht nicht in `Capabilities.cs:51-70`).

**(e) Reine `Duration`-Arithmetik kostet `osAccess` — im Programm, nicht unter `comptime`.**
Gemessen: `Duration.ofSeconds(5).totalMillis()` → `lyrvm info`: `capabilities 0x4` (`q04c`, `p36`).
**Aber** `let m = comptime millis();` mit derselben Rechnung **kompiliert und liefert 5000**
(`p36_comptime_duration.lyr`, Exit 136 = 5000 mod 256), während `comptime` über `Instant.now()`
mit `LYR-CT0002 … module requires capability 'osAccess'` scheitert (`p36c`). Ursache gelesen:
das Evaluationsmodul verlangt „was seine **behaltenen Native-Imports** verlangen — nicht mehr“
(`Ir/Lowering/ModuleLowerer.cs:588-595`), das Programm dagegen die Union über **Modulnamen**,
„recorded whether or not the program calls it“ (`ModuleLowerer.cs:961-970`, `Capabilities.cs:63`).
**Zwei Ableitungen derselben Frage im selben Compiler** — siehe SL-11 und K8.

**(f) Ein OS-Entropieabzug kostet nichts — und das ist entschieden, nicht vergessen.** Gemessen
(`p6`): `secureRandom(8)` → `0x0`. Gelesen: `design/stdlib-2.md:413-421` begründet es
(„capability-frei, im Gegensatz zu einem Seed aus `std.os`“) und legt den Compile-Zeit-Ausschluss
**per Namen** fest; `src/Lyric.Vm/VmComptimeRunner.cs:28-35` setzt genau das um und begründet
es mit Determinismus, nicht mit Sandboxing. — *Fassung 1 stellte es als Versehen dar.*
**Konsoleneingabe:** `readLine()` → `0x0` (`p17`). Gelesen: die Registry nimmt den Eingabestrom als
Parameter (`NativeRegistry.cs:272-279`, Default `Console.In`) — **aber die Einbettungs-API reicht ihn
nicht durch:** `HostOptions` hat `Output` und `Error`, kein `Input` (`Lyric.Embedding/HostOptions.cs:82-85`),
und `LangVm` ruft `CreateDefault(output, error)` ohne dritten Parameter (`LangVm.cs:45-47`). Ein
eingebettetes Skript mit `Capability.None` liest also den Stdin des Host-Prozesses. — *Der Kritiker
hat auf Registry-Ebene recht und auf API-Ebene nicht.*

**(g) `isWhitespace` ist Unicode, `isAlpha` ist ASCII, `toLower` ist wieder Unicode.** Gemessen
(`p12`): `isWhitespace(U+00A0)` `true`, `isAlpha('ä')` `false`, `isUpper('Ä')` `false`,
`"Ä".toLower()` → `ä`. Gelesen: `string.lyr:396` „ASCII only“, `:411-427` zählt die
Unicode-Whitespace-Codepoints. **Neu gemessen** (`p32_unicode_edges.lyr`): `"ß".toUpper()` bleibt
`ß` (Länge 1 — .NET-invariantes Casing, kein `SS`), `isDigit(U+0661)` `false`, `"İ".toLower()`
hat Länge 1, `"ﬁ".toUpper()` bleibt `ﬁ`. Die Host-Semantik ist also „einfaches Case-Mapping,
keine Sonderfälle“ — dokumentiert ist das nirgends.

**(h) Es gibt keine einzige I/O-Schnittstelle — und die Handles können heute keine annehmen.**
Gelesen: `io/stream.lyr:131,164`, `io/net.lyr:169,197`, `process.lyr:89,135` tragen dieselbe Form
(`readSome(h, max): ?uint8[]`, `write(h, bytes): bool`) ohne gemeinsames Interface. **Alle Handles
sind `opaque type … = int`** (`stream.lyr:41` File, `net.lyr:28-33` Listener/Socket/UdpSocket,
`process.lyr:36` Child), und `Grammar.md:186-190` sagt: „constraint satisfaction … is refused“.
Gemessen: `pub opaque type H = int; extend H :: [Reader] {…}` **im deklarierenden Modul** →
`readAll(h)` mit `r: Reader` ist `LYR-SEM0001 cannot assign 'H' to 'Reader'` (`q22d`); `extend H {
fn twice() }` → `SEM0012 'H' has no member 'twice'` (`q22e`); Kontrolle `struct Mem :: [Reader]`
läuft (`q22c` → `9`, `1`). `STATUS.md:2254-2259` führt „extend on an opaque“ als offen.
Arrays sind aliasiert (`p20_array_alias.lyr`: `fill(buf)` schreibt, Aufrufer sieht `7`), ein
füllender `read(into)` ist also **baubar** — was er kostet, steht in SL-25.

**(i) Globaler Fehlzustand, viermal kopiert — aber ohne Race.** Gelesen: `lastErrorKind()`/
`lastErrorDetail()` in `io/file.lyr:78-79`, `io/net.lyr:60-61`, `io/stream.lyr:57-58`,
`process.lyr:52-53`. Der Slot ist ein **Instanzfeld pro Registry** (`NativeRegistry.cs:1374-1375`,
Kommentar `:1368-1370`: „per REGISTRY rather than static keeps parallel VMs from seeing each
other's failures“). Der Pool-Thread schreibt **nicht** in den Slot, sondern in `state.*` unter Lock
(`:2400-2418`); der VM-Thread überträgt den Fehler beim nächsten `streamTryRead` (`:2515-2519`).
Zwischen Fehlschlag und Lesen liegt kein Yield-Punkt (`stream.lyr:131-155`: `readSome` yieldet
nur im Would-Block-Zweig und liest den Grund vor jedem Yield). Kooperative Tasks (`task.lyr:1-6`)
können den Slot also nicht dazwischen ändern. — *Fassung 1 („Wette, dass dazwischen nichts läuft“)
war unbelegt; das verbleibende Argument ist der versteckte Zustand, nicht ein Race.*

**(j) Zwei Namen für eine Funktion, in einer Release entstanden — und ein Fall, der keiner ist.**
`std.core.combineHash` (`core.lyr:521`) = `std.hash.hashCombine` (`hash.lyr:62-64`, sagt es selbst).
`std.os.nowMillis/nowNanos/sleep` (`os.lyr:49-56`) neben `std.time`. `sha256(bytes)` gegen
`sha256Hex(text)` (`hash.lyr:26,44`): das Suffix beschreibt die Ausgabe und ändert still die
Eingabe. **Die drei `slice` sind dagegen kein Duplikat:** gemessen (`q20_slice_import.lyr`,
`q20b_slice_call.lyr`) importieren `std.collections { slice }` und `std.bytes { slice }`
konfliktfrei und lösen nach Argumenttyp auf (`uint8[]` → bytes, `int[]` → generisch; druckt `2`, `2`)
— ein legaler Überladungssatz nach `guide/03:95`. `List.slice` (`collections.lyr:93`) ist eine
Methode auf einem anderen Empfänger.

**(k) Drei Aufrufstile nebeneinander — und der genannte Grund ist überholt.** Gelesen:
`std.option` frei (`option.lyr:34-127`); `std.result` gemischt — Methoden `isOk/isErr/ok/err/
unwrapOr/unwrapOrElse/expect` (`result.lyr:46-101`), frei `map/mapErr/andThen/orElse/orThrow/
fromOptional` (`result.lyr:113-169`); `std.iter` 27 Default-Methoden plus freie Zwillinge
(`iter.lyr:868-877`); `std.collections` elf freie List-Funktionen in drei Wortstellungen
(`mapList`, `listRemove`, `maxBy` — `collections.lyr:1298-1430`). `guide/13:75-77` begründet die
freien Result-Formen mit „a method of a generic type cannot introduce a type parameter of its own
yet“. **Gemessen: das geht seit 4.6** — `class Box<T> { pub fn map<U>(…): Box<U> }` und
`enum Opt<T> { …; pub fn map<U>(…): Opt<U> }` laufen beide und drucken `20` (`q14`, `q14b`).
Was **wirklich** blockiert (`iter.lyr:875-877`): eine Interface-Default-Methode kann keinen
Constraint auf ihr eigenes `T` legen (`sum`, `minValue`), und `Iterator.toList` wäre ein
Modulzyklus (`iter` importiert nur `core`, `collections` importiert `iter` — `iter.lyr:15-17`).
Gemessen weiter: `.count()` läuft (`p8`), `.toList()`/`.sum()` sind `SEM0012` (`p9`, `p11`).

**(l) Container sind weder vergleichbar noch anzeigbar.** Gemessen (`p19`): `assertEq(a, b)` auf
zwei `List<int>` meldet zweimal `SEM0028` (Equatable, Display). Gelesen: sieben Display-Konformanzen
insgesamt (Tabelle 1.1) — nicht `List`, `Map`, `Set`, `JsonValue`, `Result`, `IoError`, `JsonError`,
`ParseError`, `EncodingError`, `Utf8Error`.

**(m) `Map` ist nicht `Iterable`.** Gelesen: `List`/`Set` konformen (`collections.lyr:41,910`),
`Map` (`:547`) nicht. Gemessen (`p35_map_forin.lyr`): `for (kv in m)` →
`LYR-SEM0007 'Map<string, int>' is not iterable`. `m.entries()` (`collections.lyr:713`, frei `:1257`)
liefert `Iterator<(K, V)>`.

**(n) Die Deprecation-Uhren laufen nicht — und der Grund ist nicht das Attribut.** Gemessen:
ein `@Deprecated` in der Bibliothek (`build.lyr:179`). `PLAN.md:374` behauptet für fünf Positionen
„Uhren laufen bereits“; vier davon (freie Terminatoren, `listContains`, `keys(m)`, `std.os`-Zeit)
tragen keine Markierung. `iter.lyr:871-873` nennt den Grund: die Warnung würde in den
bibliothekseigenen Tests feuern. **`@Deprecated` auf Methoden und Feldern funktioniert** —
gemessen (`q12b`): eine markierte Klassenmethode `bump()` gibt `warning[LYR-SEM0076]: 'bump' is
deprecated: use tick()`; gelesen `Sema/WarningAnalyzer.cs:158-172` („MEMBERS carry @Deprecated
since 2.1“: FunctionDecl, StaticBindingDecl, FieldDecl), `:180-184` Interface-Member seit 2.15;
`guide/15-attributes.md:307-330` zeigt das Beispiel. Der Kommentar `core.lyr:528-529` („honoured
once the attribute rows carry members“) ist **veraltet**. Ein Unterdrücker fehlt: `Lyrc/Program.cs:138`
kennt nur `--deny-warnings`; kein `allow`/`suppress` in `guide/15`, `guide/19`, `Grammar.md`
(gesucht, nicht gefunden). — *Fassung 1 („Compilerteil fehlt“) war falsch.*

**(o) Abhängigkeiten sind lokale Pfade.** `guide/16-building.md:316`:
`"dependencies": { "geometry": "../geometry" }`. Keine Versionen, kein Lockfile, keine Registry.

**(p) Der Native-Namensvertrag ist ungeschrieben.** Gelesen: `docs/Bytecode.md:236-263` —
Host-Funktionen werden „symbolically by name and signature“ gebunden, ein Modul, das ein Native
nennt, das der Runtime fehlt, scheitert **beim Binden** mit dem Namen (`LYR-VM0005`,
`VmDiagnostics.cs:28`); der Header trägt nur die Formatversion (`Bytecode.md:96-100`), keine
Toolchain-Version. `spec/11-stdlib-contract.md:32-36`: die Menge der Natives ist „exactly the set
the shipped `stdlib/` declares as bodiless functions“. Was passiert, wenn diese Menge schrumpft,
steht nirgends.

### 1.3 Die offene Grundsatzfrage, und warum sie falsch gestellt ist

`STATUS.md:2168-2172` und `PLAN.md:320-334` führen „die Bibliotheks-Umkehr“ als unentschieden:
`design/stdlib-2.md:423-427` schließt Regex, HTTP-Client, Zeitzonen, Kompression und AES/RSA aus
der std aus („Über die Host-ABI im Nutzercode“), `lyric-v5-features.md:75` kehrt das um.

**Die beiden Dokumente reden aneinander vorbei, und der Unterschied ist die Sandbox.**
`Capabilities.cs:92-93`: jede `extern "dotnet"`-Deklaration zieht `Capability.HostAccess` — das
Bit, hinter dem Reflexion auf den ganzen Host-Prozess liegt. Eine `std.regex` über `extern "dotnet"`
verlangt vom Host dasselbe Bit wie beliebiger .NET-Zugriff. `lyric-v5-features.md:75` sagt
„dünne Lyric-Hüllen über .NET-**Natives** … unter den vorhandenen Capability-Bits“ — nur diese
Formulierung ist sandbox-verträglich. Die Runde entscheidet über **Natives**, nicht über FFI.

---

## 2. Sprachvergleich

### 2.1 Was in der Standardbibliothek steht

| | Regex | HTTP-Client | TLS | Zeitzonen | Kompression | Krypto (jenseits Digests) | JSON | Arg-Parser | Logging |
|---|---|---|---|---|---|---|---|---|---|
| **Go** | `regexp` (RE2-Syntax, eigene Engine) | `net/http` (+Server) | `crypto/tls` | `time.LoadLocation`, `time/tzdata` | `compress/*`, `archive/*` | `crypto/aes`, `rsa`, `ed25519` | `encoding/json` | `flag` | `log/slog` |
| **Rust** | – (`regex`) | – (`reqwest`) | – (`rustls`) | – (`chrono`/`jiff`) | – (`flate2`) | – (RustCrypto) | – (`serde_json`) | – (`clap`) | – (`log`/`tracing`) |
| **Python** | `re` | `http.client`, `urllib` | `ssl` | `zoneinfo` | `gzip`, `zlib`, `lzma`, `zipfile` | `hashlib`, `hmac`, `secrets` | `json` | `argparse` (+`optparse`, `getopt`) | `logging` |
| **Swift** | `Regex` in der stdlib (5.7) | Foundation `URLSession` | Foundation | Foundation `TimeZone` | – | `swift-crypto` (Paket) | Foundation `JSONEncoder` | `swift-argument-parser` (Paket) | `swift-log` (Paket) |
| **Kotlin** | `kotlin.text.Regex` (Common-stdlib, eigene JS-/Native-Backends) | – (Ktor) | JVM | `java.time` / `kotlinx-datetime` | JVM | JVM | `kotlinx.serialization` (Paket) | – (`clikt`) | – (`kotlin-logging`) |
| **C# BCL** | `System.Text.RegularExpressions` | `HttpClient` | `SslStream` | `TimeZoneInfo` | `System.IO.Compression` | `System.Security.Cryptography` | `System.Text.Json` | – (`System.CommandLine`, Paket) | `Microsoft.Extensions.Logging` (Paket) |
| **Zig** | – | `std.http` (Client+Server) | `std.crypto.tls` (**nur Client**) | `std.tz` (TZif-Parser, keine DB) | `std.compress` | `std.crypto` (AES-GCM, Ed25519, X25519, Argon2 …) | `std.json` | – | – |
| **Deno** | Sprache (`RegExp`) | Web `fetch` | Runtime | `Intl`/`Temporal` | Web `CompressionStream` | `crypto.subtle` | Sprache (`JSON`) | `@std/cli` (JSR) | `@std/log` (JSR) |
| **Lyric 4** | – | – | – | – | – | `std.hash` (nur Digests) | `std.json` | – | – |

**Die entgegengesetzte Entscheidung zu Lyric trifft Go** (und Zig, jünger). Go steckt Regex, HTTP,
TLS, die IANA-Datenbank, gzip/zip/tar und AES/RSA/Ed25519 in die stdlib. **Warum:** Go wurde für
Server gebaut, und das Go-1-Kompatibilitätsversprechen macht die stdlib zum einzigen Ort, an dem
etwas garantiert stabil ist; `go get` gab es seit 1.0 — was bis 1.11 fehlte, war **Versionierung**,
nicht ein Paketmanager. **Gewinn:** ein `go build` gegen die leere Maschine liefert ein
HTTPS-fähiges Programm ohne Abhängigkeit. **Preis:** eine stabilisierte API kann nur noch ergänzt
werden — `log/slog` neben `log`, `math/rand/v2` neben `math/rand` (`context` ist **kein** Beispiel
dafür: es wurde für API-übergreifende Cancellation eingeführt, und `net/http` bekam
`Request.WithContext`). Go hat die Rule-2-Verletzung in die Bibliothek verlagert, weil die Sprache
sie verbietet.

**Rust ist der Gegenpol und Lyrics heutige Linie.** `std` hat nichts davon — nicht einmal `rand`
oder `json`. **Warum:** Stabilisierung per RFC ist unumkehrbar, also wird fast nichts stabilisiert.
**Gewinn:** `std` rottet nicht; `regex` (eigene Engine mit RE2-artigen Garantien — lineare Zeit,
keine Backreferences) und `serde` sind besser als das, was ein Sprachteam nebenbei gebaut hätte.
**Preis:** ohne crates.io ist Rust unbenutzbar, und `chrono` gegen `time` gegen `jiff` ist die
Zersplitterung, die Go vermeidet.

**Python zeigt den Preis der Umkehr am deutlichsten.** Drei Argument-Parser werden bis heute
ausgeliefert; PEP 594 entfernte 19 tote Module aus 3.13; `urllib` ist trotz stdlib-Status so
unbeliebt, dass `requests` faktisch der Standard ist. **Lehre:** Aufnahme in `std` ist ein
Versprechen auf Jahrzehnte, keine Qualitätsaussage.

**Swift und Kotlin zeigen den Mittelweg.** Kleine Sprach-stdlib plus ein zweiter, eigenständig
versionierter Ring vom selben Team (Foundation, `swift-collections`, `swift-argument-parser`,
`swift-crypto`, `swift-nio`; `kotlinx-datetime`, `kotlinx-serialization`, `kotlinx-io`,
`kotlinx-coroutines`). **Gewinn:** Batterien in std-Qualität, die sich bewegen dürfen. **Preis:**
Versionsmatrix. Swift hat Regex ausdrücklich **in** die stdlib geholt (5.7, Literal-Syntax), weil
Regex Sprachintegration braucht.

**Deno ist das relevanteste Vorbild für Lyrics Capabilities.** Berechtigungen sind parametrisiert
(`--allow-read=/etc`, `--allow-net=example.com:443`, `--allow-env=HOME`, `--allow-run`,
`--allow-ffi`, `--allow-sys`); Lyric hat fünf undifferenzierte Bits (`Capabilities.cs:10-32`).
**Preis bei Deno:** die Prüfung sitzt in den Rust-Ops und läuft pro Zugriff — mit dem JIT hat das
nichts zu tun; Lyric prüft beim Laden, billiger und gröber. Denos `@std/*` lag **von Anfang an**
außerhalb des Runtime-Binaries (deno.land/std, 2024 nach JSR) — der zweite Ring, vom Sprachteam
betrieben.

**Zig ist der interessanteste Datenpunkt für `Reader`/`Writer`.** Zig 0.15 hat seine
Stream-Schnittstellen von generischen, compile-zeit-spezialisierten Lesern zu **nicht-generischen
`std.Io.Reader`/`std.Io.Writer` mit eigenem Puffer** und Vtable-Dispatch umgebaut, weil generische
Streams in jeder Kombination neu monomorphisieren. Lyrics Interface-Wert ist bereits ein Fat
Pointer (`STATUS.md:1820`, `:2492`), also ist Zigs Form direkt übertragbar.

### 2.2 Was jede Sprache für die Schnittstellen tut, die Lyric fehlen

| Frage | Go | Rust | Python | Swift | Kotlin | C# | Zig | Lyric 4 |
|---|---|---|---|---|---|---|---|---|
| Byteströme | `io.Reader`/`Writer`/`Seeker`/`Closer`, kombinierbar | `Read`/`Write`/`Seek`/`BufRead` | ABC-Turm `RawIOBase`/`BufferedIOBase`/`TextIOBase` | Foundation + `swift-nio` | `kotlinx-io` `Source`/`Sink` | abstrakte Klasse `Stream`; `System.IO.Pipelines` ist eine Pufferschicht **über** Stream, kein zweites Modell | `std.Io.Reader`/`Writer`, puffer-besitzend | **nichts** |
| Zahl-Abstraktion | `cmp.Ordered` in std (1.21); `constraints.Integer/Float` in x/exp | `std::ops`-Traits; `num`-Traits extern | `numbers.Number/Integral/Real` (ABCs) | `Numeric`/`BinaryInteger`/`FloatingPoint` | `Number`-Klasse (JVM) | `INumber<T>` (.NET 7) | `comptime`-Typen | `Add/Sub/Mul/Div`, sonst nichts |
| Serialisierung | Reflexion (`encoding/json` Tags) | `serde` (Derive, extern) | `__dict__`/`dataclasses` | `Codable`, Compiler-synthetisiert | `kotlinx.serialization` (Plugin) | Source-Generator | `comptime` über Felder | handgeschriebener `JsonValue`-Baum |
| Deprecation | Kommentar-Konvention (`// Deprecated:`), `go vet` | `#[deprecated(since, note)]` + `#[allow(deprecated)]` | `DeprecationWarning` + `warnings.filterwarnings` | `@available(deprecated:, obsoleted:)` | `@Deprecated(level)` + `@Suppress` | `[Obsolete(error:)]` + `#pragma warning disable` | – | `@Deprecated{until}`, **kein Unterdrücker** |
| Unterdrückung einer Warnung an EINER Stelle | – | ja | ja | **nein** (nur Wrapper-Trick oder compilerweit) | ja | ja | – | **nein** |
| Handles | `*os.File` (Struct) | `File` (Struct, `AsRawFd`) | Objekt | `FileHandle` (Klasse) | Objekt | `SafeHandle`/Objekt | `fs.File` (Struct mit `handle`) | `opaque type = int` |

Die Unterdrücker-Zeile bleibt der wichtigste Vergleich: **vier von fünf Sprachen mit einer
Deprecation-Maschinerie haben einen Unterdrücker** (Swift ist die Ausnahme — und zahlt damit,
dass Deprecations dort compilerweit oder gar nicht stumm werden). Lyric hat die Maschinerie und
keinen Unterdrücker, und deshalb läuft heute genau eine Uhr.

---

## 3. Designfragen

### SL-01 — Kommen Regex, HTTP, TLS, Zeitzonen, Kompression und Krypto in die Bibliothek?

**Heute.** Nichts davon existiert; `stdlib-2.md:423-427` schließt es aus, `lyric-v5-features.md:75`
kehrt das um, `STATUS.md:2168-2172` führt die Umkehr als unentschieden. Gelesen.

**Optionen.**

| | Linie | Vorbild | Preis |
|---|---|---|---|
| A | `std` bleibt klein, alles andere extern | Rust | Ohne Registry (SL-02) hat der Nutzer **nichts** — heute nur lokale Pfade (`guide/16:316`) |
| B | Alles in `std` | Go, Python, Zig | Ein Versprechen auf Jahrzehnte pro Modul; Pythons drei Arg-Parser und PEP 594 |
| C | Zwei Ringe: `std` (klein, am Compiler) + zweite, eigenständig versionierte Sammlung aus demselben Repo | Deno `@std/*`, Swift `swift-*`, Kotlin `kotlinx-*` | Versionsmatrix und doppelte Release-Mechanik für einen Einzel-Maintainer; **Orphan-Regel** (SL-21) |
| D | Inhaltsregel statt Größenregel: rein kommt, was eine **externe Norm** vorgibt (RFC, IANA, Unicode, ECMA-48: Regex-Syntax, HTTP/1.1, TLS, gzip, tz, AES-GCM, ANSI-Escapes); draußen bleibt, was **API-Geschmack** ist (Arg-Parser-Form, Logging-API, Web-Framework, ORM) | Go implizit | Die Grenze muss geschrieben sein, sonst wandert sie |

**Empfehlung: C mit D als Inhaltsregel — und wenn SL-02 nicht vor 5.0 kommt, B mit D.**
Die Umkehr-Frage ist zwei Fragen: „Brauchen Nutzer Regex und HTTP?“ — ja. „Muss das `std` heißen?“
— nein. D ist wichtiger als die Ringfrage: ohne sie ist jede Aufnahme ein Einzelfall. *Fassung 1
formulierte D als „Format oder Protokoll rein, Politik draußen“ und entschied SL-16 dann nach
Geschmack; die Fassung „externe Norm“ leitet SL-16 ab (`std.term` = ECMA-48 rein, `std.cli` und
`std.log` = API-Geschmack raus).*

**Unbedingt mitentscheiden (SL-13): als Natives, nicht als `extern "dotnet"`.**

**Bricht:** nein (additiv). **Hängt ab von:** SL-02, SL-13, SL-21 (Orphan-Regel im Ring),
Build-/Werkzeug-Gebiet.

---

### SL-02 — Braucht Lyric 5 einen Paketmanager, und welcher?

**Heute.** Lokale Pfade, keine Versionen, kein Lockfile, keine Registry (`guide/16-building.md:316`).
`PLAN.md:341` führt den Paketmanager als Werkzeugposten ohne Entscheidung. Gelesen.

**Optionen.**

- **A — keiner; `std` trägt alles.** Vorbild: Go 1.0–1.10 (`go get` ohne Versionen). Preis: `std`
  wird zur Halde, SL-01 ist damit auf B festgelegt.
- **B — Git-URL + Tag + Hash + Lockfile, keine Registry.** Vorbild: Zig `build.zig.zon`, Go-Module
  mit VCS-Pfaden, Deno vor JSR. Preis: keine zentrale Auffindbarkeit und keine Namensvergabe —
  aber **kein Serverbetrieb**.
- **C — Vollregistry.** Vorbild: crates.io, npm, JSR. Preis: Betrieb, Moderation, Namenskonflikte,
  Supply-Chain-Verantwortung.

**Empfehlung: B.** Ein Manifest mit `{ url, tag, hash }` kostet nichts im Betrieb und macht C
später möglich, ohne das Manifest zu ändern — Go hat genau diesen Weg genommen (Module 1.11, Proxy
1.13). C ist für dieses Projekt unrealistisch.

**Bricht:** nein (`lyric.json` v3 additiv; die Pfadform bleibt). **Hängt ab von:**
Build-/Werkzeug-Gebiet.

---

### SL-03 — Bekommt Lyric 5 `Reader`/`Writer`/`Seek`?

**Heute.** Kein I/O-Interface (1.1). Vier Module tragen dieselbe Form (1.2 h). Arrays sind
aliasiert (`p20`). Ein Interface mit `throws IoError` in der Signatur kompiliert und wird von einem
Struct erfüllt (`q23`, `q22c`). **Ein opaques Handle kann es nicht erfüllen** (`q22d`,
`Grammar.md:189`). `lyric-v5-features.md:122` listet die Interfaces als P1.

**Optionen.**

- **A — Go-Form:** `interface Reader { fn read(into: uint8[]): int throws IoError }`,
  `Writer { fn write(from: uint8[]): int throws IoError }`, `Seek` und `Closeable` getrennt; EOF
  ist `read == 0`; freie `copy(r, w)`, `readAll(r)`. Preis: `read` liefert Zahl **und** wirft — zwei
  Antwortkanäle, die Go mit `(n, err)` hat und die funktionieren. Und: der Gewinn „keine Allokation
  pro Aufruf“ ist nach SL-25 klein (die elementweise Verbreiterung an der Grenze bleibt).
- **B — Rust-Form:** dazu `BufRead` mit `fillBuf`/`consume`. Preis: ein drittes Lese-Interface.
- **C — Zig-0.15-Form:** `Reader`/`Writer` **besitzen ihren Puffer**, der Nutzer bekommt eine
  Sicht. Preis: Lebensdauer-Regel für den Puffer; Gewinn: keine Monomorphisierung im heißen Pfad,
  passend zum Fat-Pointer-Wert.
- **D — C#-Form:** abstrakte Basisklasse `Stream`. **Ausgeschlossen** — keine Vererbung.

**Empfehlung: A für die Grundschnittstellen, C für die gepufferten Hüllen** (`BufReader`/
`BufWriter` als Klassen mit eigenem Puffer). B ablehnen (zweiter Mechanismus für „lies mit Puffer“).

**Was Fassung 1 falsch hatte:** die Position hängt **nicht** „an nichts“. Sie hängt an SL-20
(Handle-Darstellung: ein `opaque` erfüllt kein Interface), an SL-22 (welcher Fehlertyp) und an
SL-24 (wie eine Task auf einen `Reader` wartet). Ohne SL-20 gibt es einen `Reader` nur über
Speicherpuffer und Nutzertypen, nicht über `File`, `Socket`, `Child`.

**Bricht:** minor. Freie Funktionen bleiben und delegieren; 5.0 macht `readSome` zum Alias.
**4.x-Warnstufe:** keine nötig; die freien Formen bekommen ihre Uhr, wenn die Interfaces stehen.
**Hängt ab von:** SL-20, SL-22, SL-24, SL-25.

---

### SL-04 — Bleiben drei Namen pro fehlbarer Operation?

**Heute.** 37 `OrThrow`-Zwillinge, 5 `OrErr`-Formen, dazu die stillen Formen (gemessen). `OrErr` ist
zu einem Siebtel ausgebaut: `hexDecodeOrErr` existiert (`encoding.lyr:101`), `base64DecodeOrErr`
nicht (`encoding.lyr:186-192`). `guide/13:28-80` beschreibt beide Familien als Entwurf.
`design/try-expression.md:1-7`: Stufe (b) `try` als Ausdruck ist designt, Option B „`try e` →
Result als 5.0-Empfehlung“ mit stdlib-redesign abgestimmt.

**Optionen.**

- **A — Bleiben, `OrErr` vollständig ausbauen.** Preis: ~110 Namen für ~37 Operationen; die
  Antwortform ist eine Namenskonvention, die niemand erzwingt.
- **B — Eine werfende Funktion pro Operation; die Antwortform kommt aus der Sprache.**
  `try? e` liefert `?T` (Vorbild Swift `try?`); `try e` **ohne** Handler liefert `Result<T, E>` —
  das ist **Lyrics eigener Entwurf** (`try-expression.md`, Option B), **nicht** Swift: in Swift
  propagiert ein nacktes `try`, ein Result entsteht nur über `Result(catching:)`/`Result { try e }`.
  Kotlin: `runCatching`. Preis: braucht den try-Ausdruck; jede Datei mit `file.text(p) ?? ""` wird
  zu `try? file.textOrThrow(p) ?? ""` (mechanisch, `lyrfix`-fähig, SL-28).
- **B′ — Nur `try?`**, `OrErr` bleibt Bibliothekskonvention (5 Namen, nicht ausgebaut). Preis:
  halbiert nur die stillen Zwillinge; `Result` bleibt Holder ohne Sprachanbindung.
- **C — Rust-Modell** (nur `Result` + `?`): **ausgeschlossen**, Propagation ist `throws`.
- **D — Go-Modell** `(v, err)`: **ausgeschlossen.**

**Empfehlung: B, mit der Einschränkung aus `stdlib-2.md` §3.3** — stille Formen bleiben, wo
`null` die ganze Wahrheit ist (`parseInt`, `Map.get`, `os.env`). **Und mit der Auflösung von K1
im selben Zug:** `try e → Result` hebt `Result` in den Fehlermechanismus; das ist nach
`CONTRIBUTING.md:38-40` ADR-pflichtig. Kommt der ADR nicht, ist B′ die Rückfalllinie — dann bleibt
`OrErr` bei fünf Namen und wird nicht ausgebaut.

**Sofortige Konsequenz für 4.x: `OrErr` nicht weiter ausbauen.**

**Bricht:** major. **4.x-Warnstufe:** `@Deprecated { until = "6.0" }` auf die fünf `OrErr`-Namen,
sobald der try-Ausdruck steht — vorher nicht. **Hängt ab von:** try-Ausdruck, typed throws
(Fehler-Gebiet), K1 (ADR).

---

### SL-05 — Der `lastErrorKind`-Zustand

**Heute.** Acht Natives in vier Modulen (1.2 i). Der Slot ist pro Registry-Instanz, der Pool-Thread
schreibt ihn nicht, kein Yield zwischen Fehlschlag und Lesen (gelesen, 1.2 i). **Es gibt heute kein
beobachtbares Fehlverhalten und keinen Race.**

**Optionen.**

- **A — Ein Native pro Operation gibt ein Struct** `(value, kind, detail)`; `RegisterStructReturning`
  existiert (`Bytecode.md:251-256` Result-Buffer-Konvention). Vorbild: Rust `io::Error` als Wert.
  Preis: acht Native-Namen verschwinden — **kein Formatwechsel**, aber ältere `.lyrbc`, die sie
  nennen, scheitern beim Binden (`VM0005`). Das ist der Native-Namensvertrag aus SL-27.
- **B — Das Native wirft selbst.** Preis: die stille Form muss fangen; die Kosten eines Wurfs in
  jedem `exists()`.
- **C — Lassen** und die Invariante schreiben: „kein Yield zwischen stillem Aufruf und
  `lastError*`“ als Regel in `std.io.error`.

**Empfehlung: C für 4.x, A gebündelt mit SL-27 in 5.0.** Das Argument aus Fassung 1 („`std.task`
macht die Annahme jederzeit falsch“) ist widerlegt. Was bleibt: versteckter Zustand, der eine
ungeschriebene Reihenfolge-Invariante trägt (`file.lyr:74-76` schreibt sie als Kommentar) und der
jedem neuen I/O-Modul zwei Natives und ein `lastXError`-Ritual aufzwingt (`stream.lyr:60-68`,
`net.lyr`, `process.lyr` — dreimal derselbe `match`). Das ist eine Rule-2-Frage der Bibliothek
(ein Fehlerkanal für Natives), kein Korrektheitsproblem — und deshalb nicht dringend genug für
einen Alleingang, aber richtig, sobald 5.0 Native-Namen ohnehin bricht.

**Bricht:** Native-Namensvertrag (SL-27), Format nein. **4.x-Warnstufe:** keine — kein Nutzer nennt
diese Namen. **Hängt ab von:** SL-27.

---

### SL-06 — Typsuffixe oder Überladung?

**Heute.** `absInt/minInt/maxInt/clampInt/signInt` neben `abs/min/max/clamp/sign` (`math.lyr:22-37,
85-289`), `fromInt/fromUint/fromFloat/fromBool/fromChar` (`string.lyr:22-33`), `formatInt/…`
(`fmt.lyr:26-38`), `parseInt/parseFloat/parseBool` (`string.lyr:492-628`), `sum/sumFloat`
(`iter.lyr:906,917`). Überladung existiert seit 3.0, die Regel steht (`guide/03:76-77`), der Import
bringt den Satz (`guide/03:95-96`), gemessen `q13`. `abs(-3)` ist still `float` (`p4`), `abs(n)` ein
Fehler (`p3`).

**Optionen.**

- **A — Überladen** (`abs`, `min`, `max`, `clamp`, `sign`, `sum`, `format`, `parse`). Vorbild: C#,
  Kotlin, Swift. Preis: **ein heute gültiger Aufruf ändert still seinen Typ** — `abs(-3)` ist
  heute `float 3.0` und wäre nach Regel 1 (`guide/03:76`) `int 3`; `abs(-3) / 2` wechselt von
  `1.5` zu `1`. Das ist der einzige Grund, warum A nicht in 4.7 additiv sein kann.
- **B — Generisch über `Num`** (SL-07 B). Vorbild: .NET `INumber<T>`, Swift `Numeric`. Preis:
  statische Interface-Member und `Self`.
- **C — Lassen.**

**Empfehlung: A in 5.0, B später — und die Warnung in 4.7.** *„int literal adapts to float here
— write `absInt(-3)` or `-3.0`“* auf genau die Aufrufe, deren Bedeutung sich ändert. Sie ist eine
Migrationswarnung im Sinn von `spec/12-diagnostics.md §12.5` (nennt die Version, presumiert die
Antwort nicht — gewarnt wird, was so oder so anders wird).

**Was Fassung 1 falsch hatte:** die Abhängigkeit „Grammar §4.2 ‚one name, one binding‘ muss eine
Überladungsmenge als eine Bindung zählen“ — der Satz steht nicht in `Grammar.md` (gesucht), und die
Import-Semantik existiert. **Gestrichen.**

**Bricht:** Warnung nein; Überladung minor bis major (stille Typänderung bei Literalen).
**4.x-Warnstufe:** 4.7 Literal-Adaption in diesen Aufrufen; 5.0 überlädt; Suffixnamen tragen
`until = "6.0"`. **Hängt ab von:** SL-17 (Unterdrücker, sonst feuern die Uhren in den
Bibliothekstests), SL-28 (`absInt → abs` ist mechanisch, das Literal nicht).

---

### SL-07 — 13 Zahlentypen: was fehlt wirklich

**Heute** (gelesen `core.lyr`, gemessen `p21`, `p22`, `p22b`, `q03c`, `q03e`):

| Typ | Display | Equatable | Ordered | Hashable | Add…Div |
|---|---|---|---|---|---|
| `int`, `char`, `string` | ja | ja | ja | ja | int/string ja |
| `bool` | ja | ja | – | ja | – |
| `float` | ja | ja | ja | **nein** | ja |
| `uint` | **nein** | ja | ja | ja | ja |
| `int8…int64`, `uint8…uint64`, `float32`, `float64` (10 Typen) | nein | nein | nein | nein | nein |

Zu schreiben wären also **41 Konformanzen für die vier Anker** (10 × 4 + `uint` Display) plus
40 Arithmetik-Konformanzen, plus die Entscheidung `float :: Hashable` (SL-26). *Fassung 1 („zwei
bedient“, „52“) war ungenau.*

**Optionen.**

- **A — Minimum von Hand:** die vier Anker für alle 13 per `extend` in `std.core`;
  `fromX`/`parseX`/`formatX` für `uint8`, `uint32`, `int32`, `int64`. Preis: 41 Blöcke à sechs
  Zeilen — aber **kein Sprachfeature nötig**.
- **B — `Num`/`Integer`/`Float`-Interfaces** mit statischen Membern (`zero`, `one`, `max`, `min`,
  `parse`). Vorbild: Swift `BinaryInteger`/`FloatingPoint`, .NET `INumber<T>`. Preis: statische
  Interface-Member und `Self` (`lyric-v5-features.md:31`, A1 #4).
- **C — Die schmalen Typen aus der Sprache werfen.** Nicht ernsthaft möglich (FFI, `uint8[]`).

**Empfehlung: A sofort (4.7, additiv), B mit 5.0.** `println(byte)` sollte gehen, und heute geht
es nicht — in einer Bibliothek, deren I/O-Schicht `uint8[]` spricht.

**Bricht:** nein. **Hängt ab von:** SL-26 (float Hashable); für B: statische Interface-Member +
`Self`.

---

### SL-08 — `Display` für Container, und die Diagnose dahinter

**Heute.** `println([1,2,3])` → `LYR-IR0001` in `console.lyr:51` (`p1`); `assertEq` über Listen →
zweimal `SEM0028` (`p19`). `design/conditional-conformance.md:1-6`: designt, nicht gebaut, Minor.

**Optionen.**

- **A — Nur die Diagnose reparieren:** `Satisfies` für Array/Tupel/Optional straffen. Billig, kein
  Sprachfeature. Muss ohnehin passieren (`stdlib-2.md:640`).
- **B — Bedingte Konformanz:** `extend<T :: [Display]> List<T> :: [Display]`, dazu `T[]`, `Set`,
  `Map`, `?T`, Tupel. Vorbild: Rust `impl<T: Display> Display for Vec<T>`. Preis: das
  Sprachfeature — designt.
- **C — Go `%v`:** unbedingtes Display für alles. **Ausgeschlossen** — kein Typ-Tag auf Werten.
- **D — `Display` und `Debug`** wie Rust. Preis: zweiter Mechanismus für „Wert als Text“;
  `stdlib-2.md:340-347` lehnt es ab und setzt die Nesting-Regel dagegen — **deren Mechanismus
  ist offen (SL-31).**

**Empfehlung: A in 4.7 als Bugfix, B mit dem Fundament, D ablehnen — aber SL-31 vorher
beantworten**, sonst ist B ohne Zitierregel für Strings in Containern unbrauchbar.

**Bricht:** A nein; B nein (additiv). **Hängt ab von:** bedingte Konformanz (Generics-Gebiet), SL-31.

---

### SL-09 — Ein Aufrufstil, oder eine geschriebene Regel für zwei?

**Heute.** Drei Stile (1.2 k). **Generische Methoden auf generischen Typen funktionieren**
(`q14`, `q14b`). Die freien Result-Formen haben damit **keinen technischen Grund mehr**; `guide/13:75-77`
ist überholt. Was bleibt: (1) Interface-Default-Methode ohne Constraint auf `T`
(`iter.lyr:875-877` — `sum`, `minValue`, `maxValue`); (2) `toList` als Modulzyklus
(`iter.lyr:15-17`); (3) nicht-generische Interface-Methode mit Elementtypwechsel
(`stdlib-2.md:648`, `enumerate`).

**Optionen.**

- **A — Alles Methode**, sobald die Sprache es kann. Vorbild: Kotlin, Swift, LINQ. Preis: für
  `std.result`/`std.option` **heute** möglich; für `sum`/`min`/`sorted` hängt es an bedingten
  Methoden (`design/conditional-conformance.md`); für `toList` daran, dass `collections` die
  Methode per `extend Iterator<T> { fn toList(): List<T> }` nachrüstet — methoden-only ist
  orphan-frei (`spec/05:141-142`), **aber `ExtendDecl` hat keine Typparameter**
  (`STATUS.md:2253-2254`), also braucht auch das den generischen `extend` aus
  `design/conditional-conformance.md`. Oder ein `FromIterator`-Interface in `iter`
  (`lyric-v5-features.md:84`).
- **B — Alles frei.** Vorbild: Go `slices.Sort(xs)`. Preis: keine Ketten; die 4.5-Arbeit rückgängig.
- **C — Regel statt Einheitlichkeit:** Methode, wenn die Operation von `T` nichts verlangt; frei,
  wenn sie einen Constraint braucht. Der Ist-Zustand — nirgends geschrieben.

**Empfehlung: A als Ziel; sofort in 4.7: `std.result`/`std.option` auf Methoden (additiv, freie
Formen bekommen die Uhr); C als Regel in `stdlib-2.md §2` bis bedingte Methoden da sind.** Keine
neuen freien `listX`-Namen mehr.

**Bricht:** major (freie Formen fallen mit 5.0/6.0). **4.x-Warnstufe:** Uhren auf die freien
Result-/Option-Kombinatoren (4.7), auf die freien Terminatoren, `listContains`, `listIndexOf`,
`keys/values/entries` — laufen heute nicht (SL-17). **Hängt ab von:** SL-17; für `sum`/`min`:
bedingte Methoden. *Fassung 1 hängte A an „generische Methoden auf generischen Typen“ — das war
das falsche Feature.*

---

### SL-10 — Modulschnitt und ein Namensgesetz: drei Fälle, drei Antworten

**Heute** (1.2 j). `combineHash`/`hashCombine` echtes Duplikat; `std.os.now*/sleep` neben `std.time`
Modulschnitt; drei `slice` ein **legaler Überladungssatz** (`q20b`); `sha256`/`sha256Hex` wechselt
still die Eingabe. `std.bytes` hat drei Funktionen (`bytes.lyr`). `stdlib-2.md:104` trägt bereits
„Namenskonventionen (verbindlich, §11-fähig)“.

**Optionen.**

- **A — Namens- und Schnittgesetz in die Spezifikation (§11)** und jeden Fall nach seiner Art
  behandeln: Duplikat → Uhr auf einen Namen; Modulschnitt → Uhr auf `std.os.now*/sleep`;
  Überladungssatz → **bleibt** (er ist die Form, die SL-06 anstrebt). Vorbild: Swift API Design
  Guidelines (normativ), Rust API Guidelines.
- **B — Nur räumen, kein Gesetz.** Preis: die nächste Dublette kommt mit dem nächsten Modul.
- **C — Lassen.**

**Empfehlung: A.** `combineHash`/`hashCombine` ist der Beweis, dass es ohne geschriebene Regel in
einer Release passiert. `slice` **nicht** deprecaten — *Fassung 1 hatte das vorgeschlagen und
widersprach damit SL-06.* `sha256Hex(text)` → Regel: ein Suffix beschreibt die Ausgabe, nie die
Eingabe; die Textform heißt `sha256Text`/nimmt `string` über eine Überladung. `std.bytes` ausbauen
(`ByteBuffer`, LE/BE, `equals`, `startsWith` — `lyric-v5-features.md:105`) oder in `std.encoding`
auflösen.

**Bricht:** minor. **4.x-Warnstufe:** `@Deprecated{until="5.0"}` auf `hashCombine` (oder
`combineHash`), auf `std.os.now*`/`sleep`. **Hängt ab von:** SL-17.

---

### SL-11 — Capability-Zuordnung: Granularität, Ableitung, Löcher

**Heute.** Fünf Bits (`Capabilities.cs:10-32`), Zuordnung pro Modul (`:51-70`). Gemessen:

| Programm | verlangt | Befund |
|---|---|---|
| `Duration.ofSeconds(5).totalMillis()` | `0x4` | Rechnen mit Dauern kostet den OS-Bit (`q04c`) |
| dieselbe Rechnung unter `comptime` | **läuft** | das Evaluationsmodul leitet aus behaltenen Natives ab (`p36`, `ModuleLowerer.cs:588-595`) |
| `comptime` über `Instant.now()` | `CT0002 osAccess` | Kontrolle: die Native-Ableitung greift (`p36c`) |
| `secureRandom(8)` | `0x0` | **entschieden** (`stdlib-2.md:413-421`), comptime lehnt per Namen ab (`p36b` → `CT0002`) |
| `readLine()` | `0x0` | Einbettungs-API hat kein `Input` (`HostOptions.cs:82-85`, `LangVm.cs:45`) |
| `executable("app","main.lyr")` | `0x0` | `std.build` nicht in der Tabelle, stirbt zur Laufzeit (`p16b`) |
| `std.io.stream.open` | `0x5` | die dokumentierte Union — funktioniert (`p15`) |

**Optionen.**

- **A — Bits behalten, Zuordnung korrigieren:** `std.time` aufspalten (`Duration`/`Date` frei,
  `Instant.now()` gated). Preis: ein Modulschnitt, um eine Tabellenzeile zu umgehen.
- **A′ — Bits behalten, Ableitung vereinheitlichen:** das Programm verlangt, was seine
  **behaltenen Native-Imports** verlangen — die Regel, die `comptime` schon hat. Dann kostet
  Duration-Rechnung `0x0` ohne Modulschnitt; `std.time.Instant.now` zieht `osAccess` über den
  Native-Namen. Preis: `ModuleLowerer.cs:967-969` will Bits „recorded whether or not the program
  calls it“ — diese Regel fällt (bewusst; für `extern "dotnet"` kann sie bleiben, weil die
  Deklaration selbst der Zugriff ist). Ein Modul, das `std.io.file` importiert und nie ruft,
  braucht dann kein Bit — das ist **weniger** Anspruch, nie mehr.
- **B — Deno-Modell, parametrisierte Capabilities** (`fileAccess=/etc`, `networkAccess=host:port`).
  Preis: die Bits sind eine `ulong` in Sektion 1 (`Capabilities.cs:3-8`, `Bytecode.md:204`) —
  Format-Major, und die Prüfung wandert teilweise zur Laufzeit.
- **C — Capability pro Funktion** (`@Requires`). Zweiter Mechanismus neben der Tabelle. **Rule 2 —
  ablehnen.**

**Empfehlung: A′ für 5.0 (ein Mechanismus statt zwei — heute hat der Compiler beide), B als
Kandidat für den nächsten Format-Major.** Zu den Einzelfällen:
- `secureRandom`: **kein Bit.** Die Begründung in `stdlib-2.md:413-421` hält: Entropie leakt nichts
  aus dem Host, und die comptime-Ablehnung beantwortet eine andere Frage (Determinismus), nicht die
  Sandbox-Frage — zwei Fragen, zwei Mechanismen ist kein Rule-2-Verstoß. *Fassung 1 zurückgenommen.*
- `readLine`: **kein Bit, sondern `HostOptions.Input`** (Host-seitig, wie `Output`). Ein Bit wäre ein
  zweiter Mechanismus für etwas, das der Host per Konstruktion entscheiden sollte — nur kann er
  es heute **nicht** (1.2 f). Bis dahin liest ein `Capability.None`-Skript den Host-Stdin; das gehört
  in `guide/14` als Warnung, sofort.
- `std.build`: kein Capability-Problem, ein Diagnoseproblem → SL-29.

**Bricht:** A′ nein für Programme (Anspruch schrumpft); Hosts, die ein Bit **erwarten**, gibt es
nicht. B major. **4.x-Warnstufe:** keine für A′. **Hängt ab von:** Einbettungs-Gebiet
(`HostOptions.Input`), Bytecode-Gebiet (B).

---

### SL-12 — Wie tief geht Unicode?

**Heute.** Methoden Unicode (Host), freie Prädikate ASCII (`string.lyr:391-437`); `isWhitespace`
Unicode seit 4.5. Casing ist einfaches Mapping ohne Sonderfälle (`p32`: `ß` bleibt `ß`).

**Optionen.**

- **A — Ein Native `charClass(c): int`** liefert die Unicode-Kategorie; alle Prädikate darauf.
  Vorbild: Go `unicode.IsLetter`, Python `str.isalpha`. Preis: ein Native, keine Tabelle im Repo.
- **B — Zwei ehrliche Familien:** `isAlpha` wird Unicode, `isAsciiAlpha` bleibt. Vorbild: Rust
  `is_alphabetic`/`is_ascii_alphabetic`.
- **C — Lassen** und dokumentieren.

**Empfehlung: A und B zusammen.** Eine Bibliothek, in der `toLower` und `isUpper` verschiedene
Alphabete kennen, ist still falsch. Die Kanten (Länge wechselndes Casing, Locale, Normalisierung,
Nicht-ASCII-Ziffern) sind eine eigene Frage: **SL-33**.

**Zweitfrage — `std.unicode` (Grapheme, Normalisierung, Konsolenbreite):** **nein im Kern, ja im
zweiten Ring.** Vergleich korrigiert: Swift ist **nicht** die einzige Sprache mit Graphem-Clustern
im Standard — Raku (NFG, graphem-indiziert) und Elixir (`String.length/at` auf Graphemen) haben es
auch. Und Swift zahlt nicht „O(n) auf jedem Zugriff“: Zugriff über `String.Index` ist O(1),
O(n) sind `count` und Integer-Offsets. Lyrics Codepoint-Semantik mit sichtbarem `length()`-Aufruf
(`guide/13:652-654`) ist für eine Werkzeugsprache trotzdem die bessere Wahl — weil `uint8[]`-I/O und
Codepoints zueinander passen und Grapheme eine Tabelle pro Unicode-Version bedeuten.

**Bricht:** minor (Nicht-ASCII-Verhalten ändert sich, bisher falsch). **4.x-Warnstufe:** keine
sinnvoll; Bugfix in einer Minor mit CHANGELOG. **Hängt ab von:** SL-33.

---

### SL-13 — Was darf nativ sein? (Und warum `extern "dotnet"` die falsche Antwort auf SL-01 ist)

**Heute.** 138 Registrierungen, 151 rumpflose Top-Level-Deklarationen (gemessen). „std ist
source-first“ steht **nirgends** (gesucht in `CONTRIBUTING.md`, `STATUS.md`, `Grammar.md`,
`guide/13`, `stdlib-2.md` — kein Treffer); `bytes.lyr:6-7` sagt es für ein Modul. `spec/11:3` sagt
nur „written in Lyric and versioned with the toolchain“.

**Optionen.**

- **A — Regel schreiben:** nativ ist nur, was Lyric nicht kann — ein Syscall, ein Host-Algorithmus
  mit externer Norm, oder eine **gemessene** Beschleunigung; jede Registrierung sagt, welches.
- **B — Die Batterien aus SL-01 als `extern "dotnet"`-Hüllen.** Preis: **`hostAccess`**
  (`Capabilities.cs:92-93`). Eine `std.regex` mit `hostAccess` hebt die Sandbox auf.
- **C — Lassen.**

**Empfehlung: A, und B ausdrücklich ablehnen.** Wer Regex, HTTP oder TLS in der Bibliothek will,
baut sie als Natives (mit passendem oder ohne Bit), nicht als FFI-Hüllen. `stdlib-2.md:423-427`
sagt „über die Host-ABI im **Nutzercode**“; `lyric-v5-features.md:75` sagt „Hüllen über .NET-Natives“.
Die Runde muss den Unterschied aussprechen.

**Bricht:** nein. **Hängt ab von:** FFI-/ABI-Gebiet (Stufe 2 ändert das Bild nicht).

---

### SL-14 — Ein Serialisierungsmechanismus, oder einer pro Format?

**Heute.** `JsonValue`-Baum (`json.lyr:29`), `parse`/`serialize`/`serializePretty`, **kein**
`ToJson`/`FromJson` (gegrept), kein `Encoder`/`Decoder`. `design/conformance-synthesis.md:1-8`:
designt, kein Prototyp, „Lowering-Teil ist der größte Einzelposten“.

**Optionen.**

- **A — Serde-/Codable-Modell:** `Encode`/`Decode`-Paar, Formate als Backends, Ableitung per
  Synthese. Vorbild: Rust `serde`, Swift `Codable`. Preis: ohne statische Interface-Member und
  `Self` nicht baubar.
- **B — Pro Format eigene API.** Vorbild: Python `json`/`csv`/`tomllib`. Preis: drei Mechanismen
  für ein Konzept, sobald TOML und CSV kommen.
- **C — Go-Modell über Reflexion.** **Ausgeschlossen.**
- **D — Nur `ToJson`/`FromJson` von Hand.** B einen Schritt später.

**Empfehlung: A — und zwar bevor ein zweites Format kommt.** Wenn A nicht baubar ist, **darf das
zweite Format nicht kommen.**

**Bricht:** nein (additiv), bindet `std.json`. **Hängt ab von:** statische Interface-Member + `Self`;
Synthese (Generics-/Interface-Gebiet, `lyric-v5-features.md:30-31`).

---

### SL-15 — Was gehört in `std.test`?

**Heute.** Neun Assertions (`test.lyr:23-91`), kein `assertThrows` (`test.lyr:95-98`: Lambda darf
kein `throws` tragen, `SEM0084`), kein Setup/Teardown, keine Tabellen, keine Snapshots, kein Bench.
Ressourcen sind pro **Datei** isoliert (`STATUS.md:2226-2238`). **Neu gemessen (`p33`):** ein
Parameter vom Typ `Coroutine<int> throws Exception` kompiliert, und `assertThrows(boom())` mit
`try { resume c } catch` druckt `threw` — die Coroutine-Form von `assertThrows` ist **heute baubar**
(SL-32).

**Optionen.**

- **A — Go-Minimalismus:** wenig Assertions, Benchmarks und Fuzzing in der stdlib; Fixtures sind
  gewöhnliche Aufrufe plus `defer`/`t.Cleanup`, `TestMain` für die Datei.
- **B — JUnit/Kotlin:** Assertions, `@Before`/`@After`, Parametrisierung.
- **C — Rust:** `assert_eq!`, `#[should_panic]`; Property-Testing extern.
- **D — Swift Testing / Zig:** Tests als Sprachkonstrukt mit Traits.

**Empfehlung: `assertThrows` (Coroutine-Form jetzt, Lambda-Form mit typed throws), tabellengetriebene
Tests und `@Bench` in 5.0; Property-Testing und Snapshots in den zweiten Ring. Setup/Teardown
ablehnen — aber mit dem richtigen Argument.** *Fassung 1 sagte „`defer` deckt es“; das ist zu kurz:
`defer` ist Scope-Exit **einer** Funktion, ein Fixture über viele Tests ist ein anderes Konzept.*
Die eigentliche Frage ist, ob ein Test eine **Funktion** oder ein **Wert** ist. Lyrics Antwort
steht: `@Test` markiert eine Top-Level-Funktion ohne Parameter (`test.lyr:15-17`). Eine Funktion
holt ihr Fixture per Aufruf (`let f = fixture(); defer f.close();`) — Go-Stil — und ein Fixture
über mehrere Tests ist Modulzustand, den `lyrtest` pro Datei isoliert. Ein `@Before` wäre eine
zweite Art, „vor dem Test“ zu sagen; abgelehnt, solange ein Test eine Funktion ist.

Die Ressourcen-Isolation (`STATUS.md:2226-2238`) ist **keine Bibliotheksfrage**: „diese VM beenden“
gegen „diesen Lauf darin beenden“ — Laufzeit-Gebiet.

**Bricht:** nein. **Hängt ab von:** typed throws für Lambdas (Lambda-Form), SL-08 (`assertEq` über
Listen), SL-32.

---

### SL-16 — `std.cli`, `std.log`, `std.term`: kommen die Anwendungsmodule?

**Heute.** Keins existiert; Argumente kommen aus `std.os.args()` (`os.lyr:28`),
`std.io.console.isInteractive()` (`console.lyr:86`) ist der einzige Terminal-Zugang.

**Vergleich.** Arg-Parser in std bei Go (`flag`) und Python (`argparse`, neben zwei veralteten);
ausgelagert bei Rust (`clap`), Swift, Kotlin, C# (`System.CommandLine` bis heute nicht in der BCL),
Deno (`@std/cli`).

**Optionen.**

- **A — Alle drei in `std`.** Vorbild: Go. Preis: ein deklarativer Parser ist Geschmack, der nie
  mehr geändert werden kann.
- **B — Alle drei in den zweiten Ring.** Vorbild: Swift, Deno, Rust.
- **C — Nur `std.term` in `std`**, der Rest in den Ring.

**Empfehlung: C — und zwar abgeleitet aus SL-01 D, nicht aus Geschmack.** `std.term` hat eine
externe Norm (ECMA-48/ANSI-Escapes, TTY-Erkennung), `isInteractive()` ist der halbe Anfang, und
die Compiler-Diagnostik hängt daran. `std.cli` (Argumentkonvention: POSIX-Utility-Guidelines sind
eine Empfehlung, GNU-Long-Options eine Gewohnheit) und `std.log` (reine API-Form) haben keine
Norm, die die API vorgibt → Ring.

**Bricht:** nein. **Hängt ab von:** SL-01, SL-02.

---

### SL-17 — Die Deprecation-Maschinerie hat keinen Unterdrücker

**Heute.** Ein `@Deprecated` (`build.lyr:179`). `PLAN.md:374` sagt „Uhren laufen bereits“ — vier
von fünf tun es nicht. Grund `iter.lyr:871-873`: die Warnung feuert in den Bibliothekstests.
`--deny-warnings` ist alles oder nichts (`Lyrc/Program.cs:138,401-413`). **Methoden und Felder
werden erfasst** (`q12b`, `WarningAnalyzer.cs:158-184`, `guide/15:307-330`) — *Fassung 1 hatte hier
einen falschen Blocker.* `guide/15:307-309` und `WarningAnalyzer.cs:180-184` widersprechen sich zu
Interface-Membern (Guide „carry no attributes at all“, Code „since 2.15“) — Doku-Drift, kein Blocker.

**Das ist der direkte Blocker für den Wunsch des Maintainers** („Lyric 4 Stück für Stück auf einen
Stand bringen, der den Wechsel auf 5 erlaubt“).

**Optionen.**

- **A — stdlib-Tests auf die neuen Formen umstellen, dann Attribut setzen.** Preis: die alten Formen
  sind ungetestet, bis sie fallen.
- **B — Sonderfall im Compiler:** `@Deprecated` feuert nicht innerhalb der stdlib. Preis: die Uhr
  ist unbeobachtet, und ein Nutzer hat den Sonderfall nicht.
- **C — Ein Unterdrücker.** Vorbild: Rust `#[allow(deprecated)]`, Kotlin `@Suppress`, C#
  `#pragma warning disable`, Python `warnings.filterwarnings`. **Reichweite ist die eigentliche
  Frage:**
  - C1 Ausdruck/Statement (C# `#pragma` um eine Zeile; feinste Form, aber Syntax im Statement-Raum),
  - C2 **Deklaration** (Rust `#[allow]` auf `fn`/`mod`; passt zu Lyrics Attributmodell — ein
    Attribut, das auf `OnFunction`/`OnModule` steht),
  - C3 Datei/Modul (Python-Filter; grob, deckt den Testfall mit einem Zeichen).

**Empfehlung: C2, als Attribut `@Allow { warning = "LYR-SEM0076" }` auf Funktion und Modul —
Sprachfrage, keine Bibliotheksfrage, und auf die 4.7-Liste vor jede weitere Uhr.** C1 ablehnen
(Attribute stehen in Lyric vor Deklarationen, nicht vor Statements — ein zweiter Attributort);
C3 als Sonderfall von C2 auf dem Modul.

**Bricht:** nein. **Hängt ab von:** Attribut-/Diagnostik-Gebiet.

---

### SL-18 — Wird `std` mit dem Compiler versioniert?

**Heute.** Quelltext neben dem Binary, kopiert beim Build, überschreibbar per `LYRIC_STDLIB`
(`guide/13:665-670`; `SourceCompiler.cs:445`). Keine eigene Version; `spec/11:3` schreibt „versioned
with the toolchain“ fest.

**Optionen.**

- **A — Bleiben.** Vorbild: Rust, Go, Python, C#. Preis: ein Tippfehler in `std.json` braucht eine
  Compilerrelease.
- **B — Eigenständige Version für `std`.** Vorbild: Deno `@std/*`. Preis: Versionsmatrix und die
  Frage, welche `std` ein `.lyrbc` erwartet — die schon heute niemand beantwortet (SL-34).

**Empfehlung: A für `std`, B für den zweiten Ring.**

**Bricht:** nein. **Hängt ab von:** SL-01, SL-02, SL-34.

---

### SL-19 — Ist eine `Map` iterierbar?

**Heute.** `List`/`Set` konformen `Iterable` (`collections.lyr:41,910`), `Map` (`:547`) nicht;
`for (kv in m)` → `SEM0007` (`p35`); `entries()` liefert `Iterator<(K, V)>` (`collections.lyr:713`,
frei `:1257`).

**Optionen.**

- **A — `Map<K,V> :: [Iterable<(K,V)>]`.** Vorbild: Go, Python, Kotlin, C#.
- **B — Bleiben.** Vorbild: Rust (`for (k,v) in &m` ist `IntoIterator` auf der Referenz).

**Empfehlung: A.** Elementtyp unstrittig. `Deque` bleibt bewusst nicht iterierbar („a queue is
drained, not walked“, `guide/13:126`).

**Bricht:** nein. **Hängt ab von:** nichts.

---

### SL-20 — Handle-Darstellung: `opaque type = int`, Struct/Klasse, oder `opaque` mit Konformanz?

**Heute.** `File`, `Listener`, `Socket`, `UdpSocket`, `Child` sind `opaque type … = int`
(`stream.lyr:41`, `net.lyr:28-33`, `process.lyr:36`). `Grammar.md:186-190`: ein `opaque` ist eine
neue Identität über derselben Schicht, „constraint satisfaction … is refused“, in einer
Native-Signatur löst es zum Underlying auf — „which is how a handle crosses the host boundary as a
plain number while scripts cannot forge one“. Gemessen: `extend H :: [Reader]` im deklarierenden
Modul → `SEM0001` (`q22d`), `extend H { fn twice() }` → `SEM0012` (`q22e`); `STATUS.md:2254-2259`
führt letzteres als offen („declaring-module SDK sugar would be the case for it“). Einbettung:
Host-Objekte sind `TypeTag.Host` (`Bytecode/Format.cs:180-192`, ohne Typtabellen-Eintrag) —
oder, wie `guide/14:439-455` empfiehlt, ebenfalls `opaque type Entity = int`.

**Optionen.**

- **A — Handles werden Klassen** (`class File { fd: int, … }`) mit Konformanzen. Vorbild: Swift
  `FileHandle`, Python. Preis: eine Allokation pro `open` (nicht pro Lese-Aufruf — vernachlässigbar
  gegen den Syscall); Identität wird Referenzidentität (heute Wertgleichheit des `int`); die
  Native-Signaturen bleiben `int` (das Feld kreuzt); **Format nein**. Vorteil: `Closeable` als
  Interface, `defer f.close()` bleibt.
- **B — `opaque` nimmt Konformanzen und Extends im deklarierenden Modul an** (Grammar §3.5
  lockern). Vorbild: Rust Newtype (`struct Fd(i32)` + `impl Read for Fd`), Go `type Fd int` mit
  Methoden. Preis: Sprachregel **und Format**: eine Impls-Zeile trägt links „the implementing
  type (class or enum)“, und „a type that is neither class nor enum must not appear on the left“
  (`Bytecode.md:355-363`, gelesen) — ein Alias über `int` hat keinen Typtabellen-Eintrag, den die
  Zeile nennen könnte. B braucht also entweder eine neue Zeilenform (Format-Minor, wenn skippbar;
  sonst Major) oder eine Box zur Laufzeit (dann ist es A durch die Hintertür). Vorteil: keine
  Allokation, die Handle-Doktrin aus `guide/14` bleibt eine.
- **C — Handles bleiben nackt; `Reader` wird ein Struct-Adapter** (`FileReader { file: File }`).
  Preis: zwei Typen pro Handle, und `Reader`-Werte sind nie das Handle selbst.
- **D — Host-Objekte (`TypeTag.Host`) müssen `Reader` werden können.** Vorbild: Deno-Ops über
  Resource-IDs. Preis: ein Host-Typ hat keinen Typtabellen-Eintrag; Konformanz hieße Vtable auf
  einen Typ, den nur der Host kennt. **Nicht in 5.0** — Host-Streams gehen über A/B als
  `opaque`-Handle, das der Host registriert (heutige Doktrin `guide/14:442`).

**Empfehlung: A (Handles werden Klassen), weil es das Format nicht anfasst und `Closeable`/
`Reader` sofort trägt; B nur, wenn das Bytecode-Gebiet ohnehin eine neue Impls-Zeilenform
beschließt.** Die Allokation pro `open` ist gegen den Syscall unsichtbar (SL-25: die Bytes
kosten das Tausendfache). Das `opaque`-Muster bleibt für **Host**-Handles aus dem Embedding
(`guide/14:442`), wo kein Interface gebraucht wird. In beiden Fällen wird der `STATUS`-Faden
„extend on an opaque“ **entschieden statt nur laut** — bei A: `opaque` nimmt weder Extends noch
Konformanzen, und der stille Extend wird ein Fehler.

**Bricht:** A major für `f == g` (Wert- → Referenzgleichheit) und für `f as int` (fällt; heute
das Privileg des deklarierenden Moduls, `STATUS.md:1059-1061`); B minor plus Format.
**4.x-Warnstufe:** A: Warnung auf jedes `as int`/`as File` außerhalb der Bibliothek (die gibt es
seit 3.8 als `SEM0093`, `STATUS.md:540`) — Nutzercode tut es also schon heute nicht ohne Warnung.
**Hängt ab von:** Sprachgebiet (`opaque`-Regel), Bytecode-Gebiet (nur B), SL-03.

---

### SL-21 — Orphan-Regel und zweiter Ring

**Heute.** `spec/05-interfaces.md:139-144`: `extend T :: [I]` ist orphan-geprüft — das erweiternde
Modul muss `T` oder eines der `I` deklarieren (`LYR-SEM0041`); methoden-only `extend T { … }` ist
frei. Gemessen (`p31`): `extend float :: [Hashable<float>]` im Nutzermodul → `SEM0041`. Ein
Ring-Modul `lyricx.regex` kann also weder `Match :: [Display]`… doch — `Match` ist seins — aber es
kann **`string` kein `Pattern`-Interface aus `std` geben** und **`std`-Typen kein `std`-Interface**
(z. B. `List<T> :: [Encode]`).

**Optionen.**

- **A — Regel bleibt; der Ring hält sich daran.** Vorbild: Rust (Orphan-Regel gilt für alle Crates
  gleich; `serde` löst es mit `#[derive]` im Nutzer-Crate und `impl` für std-Typen **im eigenen**
  Crate — erlaubt, weil `Serialize` seins ist). Preis: ein Ring-Modul definiert eigene Interfaces
  (erlaubt) und darf std-Typen daran konformieren (erlaubt, weil das `I` seins ist) — **nur
  std-Typ × std-Interface ist verboten**, und das ist genau die Kohärenz-Garantie.
- **B — Ausnahme für einen `lyricx`-Namensraum.** Preis: zwei Regeln, Kohärenz hängt an einem
  Präfix; Rule 2 dagegen.
- **C — `std` deklariert die Konformanzen selbst und der Ring füllt sie** (Encode/Decode-Backends
  als Werte, nicht als Konformanzen). Preis: Interfaces mit Backend-Parameter.

**Empfehlung: A.** Die Orphan-Regel ist keine Hürde für den Ring, sondern seine Ordnung: was der
Ring an std-Typen hängen will, muss über **seine** Interfaces gehen. Was er nicht kann —
`List<T> :: [Display]` — soll er auch nicht können (SL-08 gehört in `std`).

**Bricht:** nein. **Hängt ab von:** SL-01 C.

---

### SL-22 — Fehlertyp der I/O-Schnittstellen

**Heute.** `IoError` mit `kind`/`path`/`detail` (`io/error.lyr`; `file.lyr:81-89` baut ihn). Ein
Interface mit `throws IoError` kompiliert (`q23`). `throws E` mit **Typparameter** wird nie
substituiert (`stdlib-2.md:641`, `design/typed-throws.md:22` — Bug, designt). Kein `Self`, keine
statischen Member.

**Optionen.**

- **A — Ein Fehlertyp `IoError` für alle `Reader`/`Writer`** (Datei, Socket, Prozess, Speicher,
  Ring). Vorbild: Go `error`-Interface mit `errors.Is`, Rust `io::Error` mit `ErrorKind` + `Other`.
  Preis: ein Speicher-Reader wirft nie und deklariert trotzdem `throws IoError`; ein Ring-Reader
  (TLS) presst seinen Grund in `kind = Other` + `detail`.
- **B — Assoziierter Fehlertyp** (`interface Reader { type Error :: [Throwable]; fn read(…): int
  throws Error }`). Vorbild: Rust-Trait mit `type Error`. Preis: assoziierte Typen sind ein neues
  Sprachmittel; `Reader` als Fat-Pointer-Wert kennt seinen `Error` nicht → nur als Constraint
  nutzbar, nicht als Wert — für `copy(r: Reader, w: Writer)` unbrauchbar.
- **C — `throws Throwable`** (offen). Preis: der Aufrufer fängt ungetypt; genau das, was typed
  throws vermeiden soll.

**Empfehlung: A — mit `IoErrorKind` erweiterbar um `Closed`, `TimedOut`, `WouldBlock` (heute
Kind 6 intern, `stream.lyr:49-50`) und mit `detail` als Trägerin fremder Gründe.** Ein Fat-Pointer-
Interface **kann** keinen typabhängigen Fehler tragen; das ist keine Lücke, sondern die
Objektsicherheitsregel aus `STATUS.md:2492`. Ein Speicher-Reader deklariert `throws IoError` und
wirft nie — Go macht dasselbe mit `bytes.Reader`.

**Bricht:** nein. **Hängt ab von:** SL-03, typed-throws-Bug (`stdlib-2.md:641`) nur für generische
Helfer.

---

### SL-23 — Text über Bytes: wo liegt die Dekodierschicht?

**Heute.** Strings sind Codepoints, I/O ist `uint8[]`. Die einzige Textschicht über einem Strom ist
`LineReader` in `io/stream.lyr:205-270`: klassenbasiert, `Iterator<string>`, CRLF-Behandlung
(`:246-253`), ungültiges UTF-8 wird **still zum leeren String** (`:253`: `utf8Decode(…) ?? ""`),
kein BOM. `console.readLine/readAll/readChar` (`console.lyr:73-83`) dekodieren im Native.
`utf8Decode`/`utf8DecodeOrThrow` (`string.lyr:60,76`) sind die Bausteine; Fehlerposition über einen
zweiten Slot (`NativeRegistry.cs:1377-1379`).

**Optionen.**

- **A — Default-Methoden auf `Reader`:** `lines()`, `readAllText()` als Default-Methoden, die
  intern puffern. Vorbild: Go `bufio.Scanner` (frei, aber ein Typ), Python `TextIOWrapper`.
  Preis: ein Default kann nicht puffern, ohne Zustand zu halten — er müsste einen `BufReader`
  anlegen; `lines()` gibt einen Iterator zurück, der den Reader **besitzt**.
- **B — Ein `TextReader`-Typ** (Klasse über `Reader`, hält Puffer, Decoder-Zustand, split
  Codepoints, BOM). Vorbild: C# `StreamReader`, Python `TextIOWrapper`, Rust `BufRead::lines`.
  Preis: ein zweiter Typ — aber kein zweiter **Mechanismus**: er ist der gepufferte Hüllentyp aus
  SL-03 C mit Decoder.
- **C — Freie Funktionen** `lines(r)`, `readAllText(r)`. Vorbild: Go `io.ReadAll` + `strings`.
  Preis: SL-09-Widerspruch (frei statt Methode).

**Empfehlung: B — `TextReader` ist `BufReader` plus Decoder, also der Hüllentyp aus SL-03 C,
nicht ein neuer Mechanismus.** Und drei Regeln, die heute niemand schreibt: (1) ein Codepoint, der
über eine `read(into)`-Grenze gesplittet ist, bleibt im Decoder-Puffer (Go: `utf8.FullRune`), (2)
ungültige Sequenzen sind ein `Utf8Error` mit Offset — **nicht** `""` wie `LineReader` heute, (3) BOM
wird nur auf ausdrücklichen Wunsch entfernt (`skipBom = true`). `LineReader` wird zu
`TextReader.lines()`.

**Bricht:** minor (`LineReader` → Uhr). **4.x-Warnstufe:** keine bis `Reader` steht. **Hängt ab von:**
SL-03, SL-25.

---

### SL-24 — Async-Wechselwirkung: wie wartet eine Task auf einen `Reader`?

**Heute.** `std.task` wartet auf **Deskriptoren**: `Wait.Readable(int)`/`Writable(int)`
(`task.lyr:18-32`, „Descriptors come from std.io.net“). `readSome` in `stream.lyr:131-146` yieldet
`Wait.Readable(streamNotifyFd(file))`; ein `Reader`-Interfacewert verbirgt diesen `int`.
`lyric-v5-features.md:139`: „alle Streams nicht-blockierend im Scheduler (dank stackful Coroutines
ohne Funktionsfärbung)“.

**Optionen.**

- **A — `Reader.read` yieldet selbst** (die Implementierung enthält `yield Wait.Readable(fd)`,
  genau wie `readSome` heute). Vorbild: Lyrics eigene §10a-Regel (Yield aus jeder Aufruftiefe,
  `task.lyr:3-4`). Preis: ein `Reader` über eine Datei kann nur **in einer Task** gelesen werden —
  heute schon so (`stream.lyr:130`); ein Speicher-Reader yieldet nie. **Kein `fd()`-Member nötig.**
- **B — `AsyncReader` neben `Reader`.** Vorbild: Rust `tokio::io::AsyncRead`. Preis: die
  Zweiteilung, die stackful Coroutines gerade vermeiden — Rule 2.
- **C — `fd()`-Member auf `Reader`**, der Scheduler wartet darauf. Preis: ein Speicher-Reader hat
  keinen; `?int` als Antwort ist ein Sonderfall im Interface.

**Empfehlung: A.** Die §10a-Regel („every value it yields — from any call depth“) ist genau der
Grund, warum Lyric keine Funktionsfärbung braucht; ein `Reader`, dessen `read` intern yieldet, ist
für den Aufrufer ein gewöhnlicher Aufruf. Zu schreiben ist nur: „ein `Reader` über ein OS-Handle
braucht `std.task.run()`; ein Speicher-Reader nicht“ — als Doku-Vertrag am Interface.

**Bricht:** nein. **Hängt ab von:** SL-03; Nebenläufigkeits-Gebiet (ob `run()` der einzige
Treiber bleibt).

---

### SL-25 — Grenzkosten von `uint8[]`: kopiert das Native, und was kostet ein Byte?

**Heute — gemessen (`p34_bytes_cost.lyr`, Kontrolllauf gleich):** `bytes("big.bin")` über 4 194 304
Bytes: **57,9 ms / 57,7 ms** (≈ 14 ns/Byte inkl. Datei-Lesen); eine Lyric-Schleife
`dst[i] = b[i]` über dieselben 4 M Elemente: **786 ms / 782 ms** (≈ 187 ns/Element). Gelesen:
ein `uint8[]` ist in der VM ein `LyrValue[]` mit **16 Bytes pro Element** (`LyrValue.cs:18-21`:
`ulong Bits` + `object? Ref`); jedes Native verbreitert beim Hinein (`NativeRegistry.cs:2735-2741`
`Bytes()`: Schleife `FromBits(content[i])`) und verengt beim Hinaus (`:2745-2751` `ToBytes()`,
„copied out under the loan contract“); `stdlib-2.md:424-427` erlaubt die Kopie ausdrücklich.

**Konsequenzen.**
- Die Go-Form `read(into)` (SL-03 A) spart die **Array-Allokation** (`new LyrValue[max]`), nicht die
  elementweise Verbreiterung — die bleibt bei 16 Byte/Byte und ≈ 14 ns/Byte. Der Gewinn ist real,
  aber klein gegenüber dem, was der Lyric-Code mit den Bytes danach tut (13× teurer pro Element).
- Ein 1-MiB-Chunk (`streamTryRead` klemmt `max` auf 2^20, `NativeRegistry.cs:2511`) kostet 16 MiB
  Heap. Das ist die eigentliche Zahl für SL-03 C (puffer-besitzende Hüllen): der Puffer sollte
  **klein** sein (64 KiB), nicht 1 MiB.

**Optionen.**

- **A — So lassen, Chunkgrößen klein halten.** Preis: 16× Speicher pro Byte bleibt.
- **B — Kompakte Byte-Arrays in der VM** (`byte[]` hinter einem eigenen Tag, `TypeTag.U8`-Array
  als Sonderlayout). Vorbild: Java `byte[]`, .NET `byte[]`, Lua-Strings. Preis: eine zweite
  Array-Repräsentation in Interpreter und JIT — Format-relevant nur, wenn Konstanten davon
  betroffen sind; sonst Runtime-intern. **Bytecode-Gebiet.**

**Empfehlung: A für 5.0, B als gemessene Beschleunigung nach SL-13 A, wenn ein Profil es
verlangt.** Vorher gilt: SL-03 A wird **nicht** mit „spart die Allokation“ begründet, sondern mit
der Schnittstellenform.

**Bricht:** nein. **Hängt ab von:** Bytecode-/VM-Gebiet (B).

---

### SL-26 — `float` ohne `Hashable`: Absicht oder Lücke? Und was ist ein Hash-Vertrag?

**Heute.** `core.lyr:243-259`: `float` hat Equatable und Ordered, **kein** Hashable (gemessen `q03e`).
`Equatable.equals` ist `this == other` (`:244`) — NaN ist also sich selbst ungleich, `-0.0 == 0.0`
ist wahr. `int.hash()` ist `this` (`:199`), `uint.hash()` ist `this as int` (`:337-339`). Kein
Kommentar erklärt das fehlende `float`-Hashable (gelesen, nicht gefunden). Ob ein Hashwert über
VM-Versionen stabil ist, steht nirgends (`spec/11` erwähnt Hashable nur als Anker, `:26-28`).

**Optionen.**

- **A — Kein Hashable für Gleitkomma** (Absicht schreiben). Vorbild: Rust (`f64` ist nicht `Hash`
  und nicht `Eq`, weil NaN die Äquivalenzrelation bricht). Preis: kein `Map<float, _>` — für eine
  Werkzeugsprache tragbar, und konsistent mit `Equatable` als `==`.
- **B — Hashable mit Regel:** `-0.0` und `0.0` hashen gleich (weil `==`), NaN hasht auf einen festen
  Wert (aber `equals` bleibt falsch → ein NaN-Schlüssel ist nie wiederzufinden). Vorbild: Python
  (`hash(-0.0) == hash(0.0)`, `hash(nan)` seit 3.10 objektabhängig), Java `Double.hashCode` (bits,
  daher `-0.0 != 0.0` im Hash — **verletzt** den Equatable-Vertrag). Preis: der Vertrag muss die
  NaN-Anomalie ausdrücklich zulassen.
- **C — Hashable über die Bits** (Java). **Ausgeschlossen**, bricht `equals ⇒ hash` bei `-0.0`.

**Empfehlung: A — und dazu den Hash-Vertrag schreiben, den es heute nicht gibt:** (1) `equals ⇒
gleicher Hash` (Anker), (2) ein Hashwert ist **nicht** stabil über Toolchain-Versionen und nicht
persistierbar (Go, Rust, Python sagen das ausdrücklich; Lyric sagt nichts), (3) die schmalen
Ganzzahltypen hashen wie ihre Erweiterung nach `int` (SL-07 A), `float32`/`float64` wie `float`
(also gar nicht).

**Bricht:** nein. **Hängt ab von:** SL-07.

---

### SL-27 — Der Native-Namensvertrag

**Heute.** Binden ist symbolisch nach Name und Signatur (`Bytecode.md:246-247`); fehlt ein Native,
scheitert das Modul beim Laden mit dem Namen (`VM0005`, `Bytecode.md:258-263`, `VmDiagnostics.cs:28`).
`spec/11:32-36`: die Menge ist „exactly the set the shipped `stdlib/` declares as bodiless
functions“. Eine Regel, ob ein Name in einer **Minor** verschwinden darf, gibt es nicht
(gesucht in `Bytecode.md`, `spec/11`, `spec/13`, `CONTRIBUTING.md`). Der Format-Versionsvertrag
(`Bytecode.md:112-126`) spricht nur über Sektionen und Opcodes, nicht über Import-Namen.
`spec/11:11-16` zeigt den Präzedenzfall: `coroutineEnded`/`coroutineIsDone` sind „part of the
format's PAST“ und bleiben für Pre-4.0-Module gebunden.

**Optionen.**

- **A — Go-1-Regel für Natives:** ein einmal ausgelieferter Native-Name wird in keiner Minor
  entfernt; mit einem Major fällt er, und die Runtime bindet ihn **eine Major-Linie länger**
  (Präzedenz `spec/11:11-16`). Vorbild: Go 1 compatibility promise, Rust std („never remove“).
  Preis: die Registry wächst; `lastError*` (SL-05) bleibt bis 6.0 doppelt gebunden.
- **B — Native-Namen sind Implementierungsdetail; nur Format-Major zählt.** Vorbild: Pythons
  C-API vor der Stable ABI. Preis: ein 4.7-`.lyrbc` läuft auf 4.6 nicht — und niemand hat es
  versprochen; aber ein 4.6-`.lyrbc` auf 4.7 **muss** laufen, sonst ist „unknown minor tolerated“
  (`Bytecode.md:112`) eine Lüge.
- **C — Import-Namen tragen eine Version** (`std.io.file.readBytes@4`). Preis: Formatänderung der
  Imports-Sektion; zweiter Versionsmechanismus neben dem Header — Rule 2.

**Empfehlung: A, als ein Absatz in `spec/11 §3` — „ein Native-Name, den ein 4.x-Modul nennt, ist
in jedem 4.y ≥ x gebunden; mit 5.0 darf er fallen, und 5.x bindet die 4.x-Namen weiter“.** B ist
die halbe Wahrheit, die heute gilt, aber nur vorwärts; C erfindet, was der Header schon hat.

**Bricht:** nein (schreibt fest, was praktiziert wird). **Hängt ab von:** Bytecode-Gebiet
(Spec §13), SL-05, SL-34.

---

### SL-28 — `lyrfix`: welche Umbenennungen sind mechanisch, welche ändern Semantik?

**Heute.** `PLAN.md:343,374` und `lyric-v5-features.md:61,163` setzen `lyrfix` voraus; es
existiert nicht (`src/` hat 17 Projekte, keins davon). `stdlib-2.md`-Deliverable `:512` skizziert
Regeln (`fold(it, s, f)` → `it.fold(s, f)`, `keys(m)` → `m.keys()`, `std.os.sleep(ms)` →
`std.time.sleep(Duration.ofMillis(ms))`) und lehnt `lyric fmt --migrate` ab („ein Formatter, der
Semantik ändert, ist keiner“). Gelesen.

**Einteilung der SL-Positionen (das Dossier ist der Ort, sie zu treffen):**

| Umbenennung | Art | Bedingung |
|---|---|---|
| `absInt → abs`, `minInt/maxInt/clampInt/signInt → …` (SL-06) | **mechanisch**, wenn der Argumenttyp `int` ist — nach Überladung löst der Compiler auf | Typinformation nötig → `lyrfix` läuft auf der Sema, nicht auf Text |
| `abs(-3)` (Literal, heute `float`) | **semantisch**: bleibt nur gleich, wenn `lyrfix` `-3.0` schreibt | die 4.7-Warnung (SL-06) markiert genau diese Stellen |
| `listContains(xs, v) → xs.contains(v)`, `keys(m) → m.keys()`, freie Terminatoren (SL-09) | mechanisch | Empfängertyp bekannt |
| `hashCombine → combineHash` (SL-10) | mechanisch (Textersatz + Import) | – |
| `std.os.nowMillis() → std.time.Instant.now().epochMillis()` | mechanisch, **aber der Capability-Anspruch kann sich ändern** (SL-11) | Hinweis statt Ersatz |
| `xOrThrow → try x` / `x → try? x` (SL-04) | **semantisch** (Fehlerfluss wird Sprache; `?? ""`-Ketten bleiben gleich) | erst nach dem try-Ausdruck; Vorschlag statt Ersatz |
| `readSome(f, n) → f.read(buf)` (SL-03) | **semantisch** (Puffer wird vom Aufrufer gestellt) | nicht automatisierbar |
| `LineReader → TextReader.lines()` (SL-23) | mechanisch | – |

**Optionen.**

- **A — `lyrfix` als Sema-gestütztes Werkzeug** (kennt Typen, ersetzt nur die mechanischen
  Zeilen, meldet die semantischen). Vorbild: `cargo fix`, `go fix`, Roslyn-Analyzer + CodeFix.
- **B — Textbasiert** (Regex). Preis: `abs(` ist ohne Typ nicht entscheidbar; Fehlkorrekturen.
- **C — Kein Werkzeug; die Warnungen tragen den Ersatz als Text** (`SEM0076`-Message „use tick()“
  gibt es schon). Preis: jede Datei von Hand.

**Empfehlung: A, gebaut auf der `@Deprecated`-Message als Regelquelle** (das Attribut trägt den
Ersatz schon; `lyrfix` liest die Warnungen als JSON — `lyrc --help` nennt `--json`, „Diagnostics … as JSON“, gemessen).
Die Tabelle oben ist die Planungsgrundlage für die 4.x-Uhren: **nur mechanische Uhren dürfen vor
`lyrfix` starten**, semantische brauchen die Warnung mit Ersatzvorschlag.

**Bricht:** nein. **Hängt ab von:** Werkzeug-Gebiet, SL-17.

---

### SL-29 — `std.build` beim Kompilieren ablehnen: woran erkennt der Compiler ein Build-Skript?

**Heute.** `lyrbuild` liest die Datei `build.lyr` (`Lyrbuild/Program.cs:31`), synthetisiert ein
`main` um `build()`/`after()` (`:285-305`) und bindet die Natives selbst (`BuildSession.cs:66-96`).
Der Compiler kennt keinen Unterschied; ein Programm, das `std.build` importiert, stirbt erst mit
`VM0005` (`p16b`). `spec/11:32-36` legitimiert die Bindung durch den Build-Runner. Ein
Bibliotheksmodul, das `std.build` importiert (Build-Helfer als Paket), ist heute erlaubt und
funktioniert nur unter `lyrbuild`.

**Optionen.**

- **A — Dateiname:** `std.build` nur aus einer Datei `build.lyr` importierbar. Preis: ein Helfer-
  Paket (`buildkit.lyr`) kann es nicht — oder es muss `build.lyr` heißen; Namensregeln im
  Compiler sind ein Präzedenzfall.
- **B — Compiler-Flag** (`--build-script`), das `lyrbuild` setzt; ohne Flag ist der Import
  `error[LYR-SEMxxxx]: 'std.build' is only available to a build script`. Vorbild: Cargo baut
  `build.rs` als eigenes Ziel mit eigener Umgebung (`OUT_DIR`), Zig `build.zig` mit `std.Build`
  als Parameter. Preis: ein Flag im Frontend; **Bibliotheksmodule** unter dem Flag dürfen es
  (Build-Helfer als Paket bleibt möglich, weil das ganze Kompilat ein Build-Skript ist).
- **C — Einstiegspunkt:** `std.build` ist importierbar, wo ein `fn build(): void` als Einstieg
  dient. Preis: Heuristik.
- **D — Lassen** und `VM0005` einen besseren Text geben.

**Empfehlung: B.** Das Flag spiegelt, was `spec/11` schon sagt: `std.build`s Natives gehören
**dem Runner**, also weiß der Runner, wann er kompiliert. Bibliotheksmodule dürfen `std.build`
importieren — sie sind dann Bausteine von Build-Skripten, nicht von Programmen; das Flag
entscheidet pro Kompilat, nicht pro Datei.

**Bricht:** minor (ein Programm, das heute `std.build` importiert und nie ruft, kompiliert dann
nicht mehr — es lief auch nie). **4.x-Warnstufe:** 4.7 Warnung statt Fehler. **Hängt ab von:**
Build-/Werkzeug-Gebiet.

---

### SL-30 — `comptime` und Capabilities: was darf zur Compile-Zeit gerechnet werden?

**Heute — gemessen, gegen die Erwartung der Kritik:** `comptime` über `Duration.ofSeconds(5)
.totalMillis()` **läuft** (`p36`, Ergebnis 5000), weil das Evaluationsmodul nach Pruning keinen
Native-Import behält und nur die verlangt (`ModuleLowerer.cs:588-595`); `comptime` über
`Instant.now()` scheitert mit `CT0002 … osAccess` (`p36c`); `secureRandom` scheitert per Namen
(`p36b`, `VmComptimeRunner.cs:35`). Der Runner lädt mit `Capability.None` (`:52`).

**Damit ist die Frage des Kritikers („kann keine Duration-Konstante berechnet werden“) mit nein
beantwortet — und die Konsequenz ist die aus SL-11: die Native-Ableitung ist die genauere, und
das Programm sollte sie auch benutzen.** Was offen bleibt:

**Optionen.**

- **A — `comptime` bleibt bei `Capability.None` plus Namensliste.** Vorbild: Zig `comptime` (keine
  I/O), Rust `const fn` (keine I/O, kein Heap). Preis: `comptime` über `std.io.file.text("schema.json")`
  (Build-Zeit-Einbettung, C `#embed`, Rust `include_str!`) bleibt unmöglich — ein legitimer Wunsch.
- **B — `comptime` mit `fileAccess` auf das Projektverzeichnis.** Vorbild: Rust `include_bytes!`
  (relativ zur Quelle), Zig `@embedFile`. Preis: Determinismus hängt am Dateiinhalt (akzeptabel:
  der ist Teil der Quelle); die Grenze „Projektverzeichnis“ ist ein **parametrisiertes** Bit —
  genau SL-11 B in klein.
- **C — Ein eigener Mechanismus `embed("path")`** statt `comptime` + I/O. Preis: zweiter
  Mechanismus für „Wert zur Compile-Zeit“ — Rule 2.

**Empfehlung: A jetzt, B als Kandidat mit SL-11 B (parametrisierte Capabilities), C ablehnen.**
`std.time`-Aufspaltung (SL-11 A) ist **nicht** Voraussetzung für comptime-Ausbau — *die Kritik
hatte hier eine falsche Reihenfolge angenommen.*

**Bricht:** nein. **Hängt ab von:** SL-11, Metaprogrammierungs-Gebiet.

---

### SL-31 — Die Nesting-Regel für `Display`: mit welchem Mechanismus?

**Heute.** `stdlib-2.md:340-347`: „oben ohne, geschachtelt mit“ (`print("a")` → `a`,
`print(["a"])` → `["a"]`), **kein** `Debug`-Interface. `Display` hat genau ein Mitglied `show()`
(`core.lyr`, Anker `spec/11:26`). Wer in `List<string>.show()` die Strings zitiert, ist nicht
gesagt; ein Nutzertyp kann nicht ausdrücken, ob er zitiert werden will.

**Optionen.**

- **A — Zweites Interface-Mitglied mit Default:** `fn showNested(): string { return this.show(); }`,
  `string` überschreibt es mit Zitat, Container rufen `showNested` auf Elementen. Vorbild: Pythons
  `__repr__`/`__str__`-Paar (Container rufen `repr`), Rust `Debug` in `{:?}`. Preis: ein zweites
  Mitglied in einem Anker-Interface — Rule-2-verdächtig, **aber**: ein Mitglied mit Default ist
  kein zweiter Mechanismus, sondern derselbe mit Kontext; jeder heutige Konformant bleibt gültig.
- **B — Flag-Parameter** `show(nested: bool)`. Preis: ändert die Anker-Signatur → jeder Konformant
  bricht; **ausgeschlossen**.
- **C — Container zitieren nur `string`/`char` per Sonderfall** (Typprüfung im Container). Preis:
  ohne Typ-Tag geht das nur per Konformanz — also A durch die Hintertür, aber nur für zwei Typen.
- **D — Formatsprache entscheidet** (`{x}` vs `{x:?}`, `lyric-v5-features.md:59`, A3 #22) mit einem
  `Format`-Interface. Preis: hängt am f-String-Umbau; ohne `Debug` bleibt `{x:?}` ohne Inhalt.

**Empfehlung: A — `showNested()` als Default-Mitglied auf `Display`, und die Regel in `stdlib-2 §6`
und `spec/11 §2` schreiben.** Es ist der kleinste Eingriff, der die Nesting-Regel überhaupt
implementierbar macht, und er hält das Versprechen „kein zweites Interface“. D ergänzt A
(`{x:?}` ruft `showNested`), ersetzt es nicht.

**Bricht:** nein (Default-Mitglied; Anker-Erweiterung ist §11-relevant → Spec-PR zuerst).
**Hängt ab von:** SL-08 B, Spec-Gebiet.

---

### SL-32 — `assertThrows` heute: die Coroutine-Form

**Heute — gemessen (`p33_coroutine_throws_param.lyr`):** `fn assertThrows(c: Coroutine<int> throws
Exception): bool { try { resume c; return false; } catch (_: Exception) { return true; } }` kompiliert
und druckt `threw`. `Grammar.md:307` erlaubt `throws` auf Koroutinentypen; `guide/11:167-228`
beschreibt die Throwability als Teil des Typs. Der Lambda-Weg ist blockiert (`SEM0084`,
`test.lyr:95-98`); `throws E` mit Typparameter wird nicht substituiert (`stdlib-2.md:641`).

**Preis für den Nutzer:** der Testfall wird eine benannte Koroutinen-Funktion mit mindestens einem
(auch unerreichbaren) `yield` — `p33` braucht `if (true) { throw … } yield 1;` — statt eines
Lambdas; und `E` ist fest (`Exception`) oder offen (`throws`), nicht generisch.

**Optionen.**

- **A — Jetzt liefern:** `assertThrows(c: Coroutine<void> throws)` in `std.test` (4.7), Lambda-Form
  nach typed throws Stufe 3, dann Uhr auf die Koroutinen-Form? **Nein** — beide bleiben: sie sind
  Überladungen (Koroutine vs. Funktionstyp), kein Duplikat.
- **B — Warten auf typed throws** (Lambda-Form, `E` inferiert). Vorbild: Kotlin `assertThrows<E> { }`,
  Swift `#expect(throws:)`. Preis: `assertThrows` fehlt weiter.
- **C — Sprachmittel `expect throws E { … }`** (Zig `try std.testing.expectError`). Preis: Syntax
  für einen Testfall — Rule 2.

**Empfehlung: A, ohne Uhr — die Koroutinen-Form ist die ehrliche Form für „ein Ablauf, der werfen
soll“, und die Lambda-Form kommt als Überladung dazu.** Was `p33` zeigt, ist wichtiger als
`assertThrows`: **werfende Ablauf-Werte gibt es in Lyric schon**, nur nicht als Lambdas.

**Bricht:** nein. **Hängt ab von:** typed throws (nur für die Lambda-Form und generisches `E`).

---

### SL-33 — Unicode-Kanten jenseits `isAlpha`

**Heute — gemessen (`p32`):** `"ß".toUpper()` → `ß` (kein `SS`; .NET-invariantes Simple-Case-Mapping),
`isDigit(U+0661)` → `false` (`string.lyr:391` ist `'0'..'9'`), `"İ".toLower()` hat Länge 1,
`"ﬁ".toUpper()` → `ﬁ`. Kein Locale-Parameter, keine Normalisierung; `==` vergleicht Codepoints
(`core.lyr:262`). `spec/11:17-24` fixiert nur die Zahl-nach-Text-Formen, nichts zu Casing.

**Optionen.**

- **A — Simple Case Mapping, invariant, festschreiben:** `toUpper`/`toLower` sind Codepoint-zu-
  Codepoint (UnicodeData.txt Spalten 12-14), nie längenändernd, nie locale-abhängig; `isDigit`
  bleibt ASCII, `isNumeric` (Kategorie Nd) kommt dazu; `parseInt` bleibt ASCII (Go `strconv`,
  Rust `str::parse` sind ASCII-only). Vorbild: Go `unicode.ToUpper` (simple), Rust `char::to_uppercase`
  (**full**, `ß` → `SS` — Iterator!), Swift `uppercased()` (full, locale-frei). Preis: `ß` wird
  nie `SS` — dokumentiert.
- **B — Full Case Mapping** (SpecialCasing.txt). Preis: `toUpper` kann die Länge ändern; auf
  `char`-Ebene braucht es einen Iterator (Rust) oder `string`-Rückgabe für ein `char`.
- **C — Locale-Parameter** (`toUpper(locale)`). Preis: Locale-Daten im Runtime; Rule-2-neutral,
  aber ein Fass.

**Empfehlung: A, in `spec/11` festgeschrieben** (weil `toUpper` über Interpolation beobachtbar ist
wie `fromFloat`, `spec/11:17-19`), plus: Normalisierung ist **keine** Bibliotheksaufgabe im Kern
(`std.unicode` im Ring, SL-12), `==` bleibt Codepoint-Gleichheit. Das ist die Semantik, die der Host
heute hat — sie wird nur geschrieben.

**Bricht:** nein (schreibt Ist-Verhalten fest). **Hängt ab von:** SL-12, Spec-Gebiet.

---

### SL-34 — Versionsprüfung für `std` im Embedding

**Heute.** Ein Host bringt seine Runtime und eine `stdlib`-Kopie (`HostOptions.StdlibRoot`,
`HostOptions.cs:19`; `guide/13:665-670`). Der `.lyrbc`-Header trägt **nur** die Formatversion
(`Bytecode.md:96-100`); die Toolchain-Version steht nirgends im Modul (gesucht: `ToolchainVersion`
wird nur von `ProjectFile.cs:351-357` für `lyric.json` geprüft). Ein `.lyrbc`, gegen std 4.7
übersetzt, lädt auf einem 4.6-Host und scheitert **erst beim Binden** des ersten neuen Natives mit
`VM0005` — was `Bytecode.md:258-263` als gewollt beschreibt („fails at binding, with the import's
name in the message, never by misreading“).

**Optionen.**

- **A — So lassen:** Binden ist die Prüfung. Vorbild: ELF/dynamischer Linker (Symbol fehlt →
  Ladefehler mit Namen). Preis: der Fehler kommt spät (Laden statt Build) und nennt ein Native, kein
  „braucht 4.7“.
- **B — Toolchain-Minimum im Modul** (Attribut-Zeile oder Header-Feld `requires 4.7`), Loader
  vergleicht mit `ToolchainVersion.Value`. Vorbild: Java Classfile `major_version`, .NET
  `TargetFramework`. Preis: Format-Minor (neues Feld/Sektion — skippbar, `Bytecode.md:114-116`);
  der Compiler muss wissen, welche **Bibliotheks**-Minor ein Programm braucht — das weiß er nur
  über die Natives, also ist B nur A mit besserem Text.
- **C — Native-Namen versionieren** (SL-27 C). **Ablehnen.**

**Empfehlung: A, plus den Text von `VM0005` um die Version anreichern** („no native
'std.io.stream.streamPeek' — it was added in 4.7; this runtime is 4.6“ — die Registry kennt ihre
eigene Version, und `spec/11 §3` kann die „seit“-Version je Native tragen). Das ist B ohne
Formatänderung. Und **SL-27 A** ist die eigentliche Absicherung: neuere Runtime, älteres Modul
läuft immer.

**Bricht:** nein. **Hängt ab von:** SL-27, Einbettungs-Gebiet.

---

### SL-35 — Namensraum Feld/Methode

**Heute.** `LYR-RES0001` — ein Name pro Scope, Typkörper eingeschlossen (`spec/appendix-a:89`,
`Resolver.cs:155-163`). `Map` musste sein Feld `keys` zu `keyColumn` umbenennen
(`collections.lyr:549`, `stdlib-2.md:644`). Ein `Reader`-Interface mit `read`/`write` kollidiert
mit jedem Nutzer-Struct, das ein Feld `read` hat. Gelesen.

**Optionen.**

- **A — Bleibt so; als Regel in die Namenskonvention** (`stdlib-2 §2` / `spec/11`): Felder sind
  Substantive (`data`, `pos`), Methoden Verben (`read`, `close`); Interface-Mitglieder sind Verben.
  Vorbild: Kotlin, Swift, C# (Property und Methode teilen den Namensraum; die Konvention löst es).
  Preis: Konvention statt Regel; ein Nutzer mit Feld `read` bekommt beim Konformieren einen
  `RES0001` mit klarer Note.
- **B — Getrennte Namensräume** (Java: Feld `read` und Methode `read()` koexistieren). Preis:
  `x.read` ist dann mehrdeutig zwischen Feldzugriff und Methodenwert — Lyric hat Methodenwerte als
  Ausdruck? (`guide/14:380`: „A host member is read as a call“ — für Lyric-Typen ist `x.f` ohne
  Klammern der Feldzugriff, `x.m` ohne Klammern heute ein Fehler; B würde diese Frage
  aufmachen). Sprachbruch für nichts.
- **C — Interface-Mitglieder bekommen Vorrang** (Konformanz versteckt das Feld). Preis: ein Feld,
  das still unerreichbar wird — genau die Silence, die 3.8/4.6 überall beseitigt haben.

**Empfehlung: A.** Ein Namensraum ist die einfachere Sprache und die Regel „Feld = Substantiv,
Methode = Verb“ macht Kollisionen selten; wo sie auftreten, ist die Diagnose heute schon richtig.

**Bricht:** nein. **Hängt ab von:** SL-10 (Namensgesetz).

---

## 4. Was wir übernehmen sollten

| Von | Was | Wohin in Lyric 5 |
|---|---|---|
| **Go** | `io.Reader`/`io.Writer` als das Paar, an dem Datei, Socket, Prozess und Puffer zusammenkommen; `copy(r, w)` frei; `bytes.Reader` deklariert einen Fehler, den es nie wirft · `TestMain`/`t.Cleanup` als Beleg, dass Fixture ≠ `defer` — und trotzdem kein `@Before` | SL-03, SL-22, SL-15 |
| **Go 1** | Das Kompatibilitätsversprechen als Vorbild für den **Native-Namensvertrag** | SL-27 |
| **Zig (0.15)** | Nicht-generische, puffer-besitzende Stream-Hüllen; `@embedFile` als Muster für comptime-Einbettung | SL-03, SL-30 |
| **Rust** | `is_alphabetic` neben `is_ascii_alphabetic`; `f64` bewusst ohne `Hash`; `#[allow(deprecated)]` auf Deklarationen; `cargo fix`; Orphan-Regel ohne Ausnahmen | SL-12, SL-26, SL-17, SL-28, SL-21 |
| **Swift** | `try?` als Sprachform; `Codable` mit Synthese; `@available(obsoleted:)` (übernommen als `until`). **Nicht** übernehmen: `try` → Result (das ist Lyrics eigener Entwurf) und das Fehlen eines Unterdrückers | SL-04, SL-14, SL-17 |
| **Swift / Kotlin / Deno** | Zwei Ringe: kleine `std` am Compiler, eigenständig versionierte Batterien vom selben Team | SL-01, SL-18 |
| **Deno** | Parametrisierte Berechtigungen als Fernziel — und pro-Native statt pro-Modul als Ableitung schon jetzt | SL-11 |
| **C# / Swift** | `INumber<T>`/`BinaryInteger`: eine Zahl-Abstraktion für 13 Typen | SL-07 |
| **Python** | `__repr__`-Paar als Muster für `showNested()`; als **Warnung**: drei Arg-Parser und PEP 594 | SL-31, SL-01, SL-16 |
| **Cargo / Zig build** | Build-Skript als eigenes Kompilat mit eigener Umgebung | SL-29 |

**Die fünf billigsten Posten, die an keinem Sprachfeature hängen** (und deshalb 4.7-fähig sind):

1. `Display` für `uint`, die vier Anker für die zehn schmalen Zahlentypen (SL-07 A).
2. `std.result`/`std.option` auf Methoden — geht seit 4.6 (SL-09).
3. Die `Satisfies`-Straffung, damit `println([1,2,3])` in der Nutzerdatei scheitert (SL-08 A).
4. `HostOptions.Input` (SL-11) und `assertThrows` in Koroutinen-Form (SL-32).
5. Ein Warnungs-Unterdrücker als Attribut (SL-17 C2) — ohne ihn läuft keine weitere Uhr.

**Die zwei Entscheidungen, die alles andere blockieren:** SL-01 (Umkehr) hängt an SL-02
(Paketmanager) — wer SL-02 verneint, hat SL-01 auf B festgelegt. Und SL-03 (`Reader`) hängt an
SL-20 (Handle-Darstellung) — *Fassung 1 hatte das übersehen.*

---

## 5. Konflikte

**K1 — `std.result` steht neben `throws`, und Rule 2 nennt genau dieses Beispiel.**
`CONTRIBUTING.md:38-40`. `std.result` ist mit 4.5 ausgeliefert. **Korrigierte Suche:** `ADR` wird
referenziert — `design/abi.md:27` („ADR-001“), `design/macros.md:79` („bewusste ADR-Entscheidung“),
`lyric-v5-features.md:18,69`, `PLAN.md:313,446`, `SPEC-RUNDE.md:33,36`, `TASKLIST.md:130` („ADR-018“)
— **aber keine Datei existiert** (`find -iname '*adr*'` leer). Der eigentliche Befund: das Projekt
zitiert ADR-Nummern, die es nie geschrieben hat. Für `std.result` steht die Begründung in
`stdlib-2.md:14-30` (Designnotiz). *Zu entscheiden:* der ADR wird nachgereicht — **und SL-04 B
(`try e → Result`) wartet darauf** — oder Rule 2 verliert dieses Beispiel.

**K2 — SL-01 gegen Rule 2, auf der Bibliotheksebene.** Rule 2 spricht nur über Sprachkonzepte
(`CONTRIBUTING.md:25-40`). *Zu entscheiden:* gilt sie auch für die Bibliothek? Wenn ja, sind SL-14
(ein Serialisierungsmechanismus), SL-09 (ein Aufrufstil) und SL-23 (`TextReader` = `BufReader`)
verbindlich.

**K3 — SL-13 gegen das Capability-Modell.** Wer die Umkehr mit `extern "dotnet"` beantwortet,
gibt jedem Batteriemodul `hostAccess` (`Capabilities.cs:92-93`).

**K4 — SL-11 B gegen das Bytecode-Format.** Parametrisierte Bits brauchen ein Format-Major
(`Capabilities.cs:3-8`, `Bytecode.md:204`).

**K5 — SL-17 gegen `PLAN.md:374`.** „Uhren laufen bereits“ — gemessen läuft eine. Die Ursache ist
**nicht** das Attribut (das reicht bis auf Felder), sondern der fehlende Unterdrücker.

**K6 — SL-14 und SL-09 (`sum`/`min`) hängen am Generics-Fundament** (bedingte Konformanz,
statische Interface-Member, `Self`). SL-09 für `std.result`/`std.option` hängt **nicht mehr**
daran (gemessen). Die Runde sollte SL-07, SL-08 A, SL-12, SL-17, SL-19, SL-32 getrennt beschließen
können.

**K7 — `lyric-v5-features.md` liegt im Repository, gegen Rule 1.** `STATUS.md:2177-2180`.

**K8 — Zwei Capability-Ableitungen im selben Compiler** (`ModuleLowerer.cs:588-595` per
behaltenem Native, `:961-970` per Modulname). Eine davon ist die Wahrheit über das Programm, die
andere die Wahrheit über die Tabelle; heute gewinnt beim Programm die gröbere. Rule 2 im
Compiler selbst — SL-11 A′ löst es, alles andere lässt es stehen.

**K9 — `Grammar.md:189` („constraint satisfaction … refused“ für `opaque`) gegen SL-03.** Solange
§3.5 so steht, gibt es keinen `Reader` über `File`; `STATUS.md:2254-2259` fragt nur nach Extends,
nicht nach Konformanzen. SL-20 muss beides beantworten.

**K10 — SL-06/SL-09/SL-10-Uhren gegen SL-28.** Semantische Umbenennungen (`abs(-3)`, `OrThrow →
try`) dürfen nach der `PLAN.md:355-362`-Regel („eine Warnung … muss einen Befund haben, der sich
ohne die Antwort auf die Regel stellen lässt“) nur mit Ersatzvorschlag warnen; ohne `lyrfix`
ist „Deprecations wirklich entfernen“ (`PLAN.md:374`) nicht ausführbar.

---

## 6. Nach der Kritik geändert

**Falsche Aussagen (13 gemeldet):**

| # | Kritikpunkt | Befund | Änderung |
|---|---|---|---|
| 1 | `@Deprecated` nicht auf Methoden/Feldern | **Kritik richtig** — nachgemessen `q12b`, `WarningAnalyzer.cs:158-184`; `core.lyr:528` ist veraltet | SL-17 zweiter Absatz gestrichen; 1.2 n korrigiert |
| 2 | `abs(3)` mehrdeutig, Regel fehle; Grammar-§4.2-Abhängigkeit erfunden | **Kritik richtig** — `guide/03:76,95`, `q13` | SL-06: Regel als vorhanden, Abhängigkeit gestrichen; Überladung bleibt 5.0 (stille Typänderung bei Literalen, siehe SL-06 A) |
| 3 | Generische Methoden auf generischen Typen laufen | **Kritik richtig** — `q14`, `q14b`; wahrer Blocker `iter.lyr:875-877` | SL-09 umgeschrieben; `std.result` sofort auf Methoden |
| 4 | SL-03 hängt an opaque Handles | **Kritik richtig** — `q22d/e`, `Grammar.md:186-190`, `STATUS.md:2254` | SL-03 hängt an SL-20/22/24/25; neue SL-20 |
| 5 | `lastErrorKind`-Race unbelegt | **Kritik richtig** — `task.lyr:1-6`, `NativeRegistry.cs:1368-1375,2400-2418,2515-2519`, kein Yield in `stream.lyr:131-146` | SL-05: Race gestrichen, Empfehlung auf C für 4.x / A mit SL-27 |
| 6 | „138 Zeilen Native-Registry“ | **Kritik richtig** — 2842 Zeilen, 138 Registrierungen | Tabelle 1.1 korrigiert |
| 7 | OrThrow 36 statt 37 | **Kritik richtig** — 37 gemessen | Tabelle 1.1 |
| 8 | uint hat Anker außer Display; float ohne Hashable; „52“ falsch | **Kritik richtig** — `core.lyr:319-337,406-424`, `q03c/e` | SL-07 mit Tabelle; neue SL-26 |
| 9 | drei `slice` sind Überladungssatz | **Kritik richtig** — `q20/q20b` | SL-10 in drei Fälle geteilt, `slice`-Uhr gestrichen |
| 10 | ADR-Suche falsch | **Kritik richtig** — `abi.md:27` u. a. referenzieren ADR-001/018 ohne Datei | K1 korrigiert; Befund verschärft |
| 11 | `readLine` host-kontrolliert | **Teilweise** — Registry ja (`:272-279`), Einbettungs-API **nein** (`HostOptions.cs:82-85`, `LangVm.cs:45`) | 1.2 f und SL-11: kein Bit, sondern `HostOptions.Input` |
| 12 | STATUS/PLAN/collections-Zeilen veraltet | **Kritik richtig** | alle Zeilen neu gelesen (`STATUS.md:2168-2172, 2177-2180, 2226-2238, 2254-2259`; `PLAN.md:374`; `collections.lyr:713,1257`) |
| 13 | `secureRandom` ist dokumentierte Entscheidung | **Kritik richtig** — `stdlib-2.md:413-421`, `VmComptimeRunner.cs:28-35` | 1.2 f und SL-11: Entscheidung anerkannt und begründet gehalten (zwei Fragen, zwei Mechanismen) |

**Vergleichssprachen (10 gemeldet), alle übernommen:** Swift `try` propagiert, Result nur über
`Result(catching:)` (SL-04, Tabelle 4); Raku/Elixir haben Grapheme, Swift-Index ist O(1) (SL-12);
Rust `regex` ist keine RE2-Implementierung (2.1); Go `cmp.Ordered` in std seit 1.21 (2.2); Go
`context` kein Timeout-Workaround (2.1); Go hatte `go get` seit 1.0 (2.1, SL-02); Deno-Checks in
Rust-Ops, `@std` nie im Binary (2.1); Swift hat keinen Unterdrücker → Zeile auf „nein“, Kernsatz
auf „vier von fünf“ (2.2); C# `TimeProvider` raus, Pipelines als Schicht über Stream (2.1, 2.2);
Kotlin Regex Common-stdlib, Python `numbers`-ABCs, Zig TLS nur Client (2.1, 2.2).

**Schwache Empfehlungen (11 gemeldet):**

| Kritik | Antwort |
|---|---|
| SL-03 „hängt an nichts“ | Übernommen — SL-20 vorgeschaltet |
| SL-06 Abhängigkeit streichen, 4.7 additiv | Abhängigkeit gestrichen; **4.7-Überladung nicht übernommen**: `abs(-3)` wechselt still von `float` auf `int` (Regel 1, `guide/03:76`), das ist die Warnung wert, bevor es passiert |
| SL-17 Reichweite als Frage | Übernommen — C1/C2/C3 mit Empfehlung C2 |
| SL-09 falsches Feature | Übernommen |
| SL-10 drei Fälle | Übernommen |
| SL-05 neu begründen; „Format-relevant“ ungenau | Übernommen — Native-Namensvertrag statt Format, neue SL-27 |
| SL-11 readLine/secureRandom | secureRandom: übernommen; readLine: teilweise (Bit nein, `Input`-Option ja, weil die API es heute nicht kann) |
| SL-04 Swift-Basis; K1-Auflösung fehlt | Übernommen — B als Lyric-Entwurf mit ADR-Bedingung, B′ als Rückfall |
| SL-01 Regel D entscheidet SL-16 nicht | Übernommen — D neu gefasst als „externe Norm“, SL-16 daraus abgeleitet |
| SL-15 `defer`-Argument zu kurz | Übernommen — Frage „Test = Funktion oder Wert“ beantwortet (Funktion, `test.lyr:15-17`) |
| SL-03 A Allokationsgewinn unbelegt | Übernommen — **gemessen** (`p34`): 14 ns/Byte Grenze, 187 ns/Element Lyric-Schleife, 16 B/Element; neue SL-25 |

**Fehlende Designfragen (16 gemeldet) — alle eingearbeitet:** Handle-Darstellung (SL-20),
Orphan-Regel/Ring (SL-21), I/O-Fehlertyp (SL-22), Text über Bytes (SL-23), Async/`Reader` (SL-24),
Grenzkosten (SL-25, **gemessen**), float Hashable/Hash-Vertrag (SL-26), Native-Namensvertrag
(SL-27), `lyrfix` mechanisch/semantisch (SL-28), `std.build`-Erkennung (SL-29), comptime und
Capabilities (SL-30 — **Prämisse der Kritik widerlegt**: `Duration` unter `comptime` läuft, `p36`;
was die Messung stattdessen zeigt, ist K8), Nesting-Mechanismus (SL-31), `assertThrows` über
Koroutinen (SL-32 — **gemessen baubar**, `p33`), Unicode-Kanten (SL-33 — **gemessen**, `p32`),
Versionsprüfung im Embedding (SL-34), Namensraum Feld/Methode (SL-35).

**Was gegen die Kritik hält (in einem Satz je Punkt):**
- SL-11 readLine: die Registry kann den Eingabestrom nehmen, die Einbettungs-API reicht ihn nicht
  durch — der Befund „liest Host-Stdin ohne Bit“ stimmt für jeden `LangVm`-Host heute.
- SL-06: die Überladung ist für Variablen additiv und für Literale eine stille Typänderung; deshalb
  Warnung 4.7, Überladung 5.0 — nicht beides in 4.7.
- SL-30: comptime kann `Duration`-Konstanten rechnen; die Reihenfolge „SL-11 A vor comptime“
  ist hinfällig.

**Neu in dieser Fassung ohne Kritik-Anlass:** K8 (zwei Capability-Ableitungen — aus der p36-Messung),
K9 (Grammar §3.5 gegen `Reader`), K10 (Uhren gegen `lyrfix`), die `guide/15`/`WarningAnalyzer`-
Drift zu Interface-Member-Attributen (SL-17, Doku), das Native-Etikett `spec/11:11-16` als
Präzedenz für SL-27.
