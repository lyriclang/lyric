# 13 — Umsetzungsplan: von Lyric 4 zu Lyric 5

Das Ergebnis der Runde (Maintainer: „einen detailreichen Plan, nach dem Lyric 4 stückweise auf 5
gebracht wird"). Grundlage: alle Entscheidungen in `00`–`12`. Regeln des Plans:

- **Rule 3 gilt** (CONTRIBUTING): jeder Meilenstein liefert ein konkretes, laufendes Artefakt.
- **Vertikale Schnitte**: das erste native Hello World kommt vor jeder Breite; jede Sprachschicht
  wird durch migrierte `examples/` und Konformanzfälle bewiesen, nicht durch Absicht.
- **Spec-first je Meilenstein** (R4 P2): das Spec-Kapitel und die Konformanzfälle entstehen mit
  dem Meilenstein, nicht am Ende. Das Konformanz-Gate liest die Spec an dem Commit, den
  `spec.pin` nennt: Regel und Compiler sind zwei Pull Requests, die zusammen geprüft werden, und
  die Spec mergt zuerst (Review 2026-10-05, M5-8).
- **Messpunkte** (Z2): eine `bench/`-Suite mit Referenzzahlen (Go, C#, Python, C auf derselben
  Maschine) ab M3; jeder Messpunkt ist ein Ratchet, keine Behauptung.
- **Zwei Spuren**: Runtime (C, unter WSL2 mit ASan/UBSan/valgrind) und Compiler (C#, aus dem
  4.x-Frontend) laufen nebeneinander; der Maintainer arbeitet mit KI-Unterstützung an beiden.
- **Größen** statt Termine: S (Tage), M (1–2 Wochen), L (3–4 Wochen), XL (>4 Wochen) an
  Sessions — Erfahrungswert 4.x: M37 (fünf Slices) in ~4 Wochen. Ein Meilenstein, der >100 %
  über seiner Größe liegt, wird neu geschnitten (CONTRIBUTING).

Zahlen zur Einordnung: das 4.x-Frontend + Core sind ~40 000 Zeilen C# und werden zu großen
Teilen weiterverwendet (Lexer, Parser, Resolver, Sema); neu sind IR→C-Emitter, Monomorphisierer,
`comptime`-Interpreter, Makros, die Runtime in C (~15 000 Zeilen: GC, Koroutinen, Poller,
Strings, Unicode-Tabellen) und `std` in Lyric (~25 000 Zeilen). Größenordnung: **60 000+ neue
Zeilen**, Meilensteine M0–M18.

## Phasen und Meilensteine

### Phase 0 — Vorbereitung

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M0** | R5/T5 umsetzen: `main` = 5-Entwicklung, `release/4.x` abzweigen, Repo `lyric-script` aus dem Tag (R6, Umbenennung kann warten), `lyric-spec` `main` → 5.0-Skelett (Kapitelliste P1); `docs/archive/4.x/`; CI-Matrix Tier 1 mit `zig cc` (T4); WSL2-Umgebung (00); NativeAOT-Build des leeren `lyric`-Binaries; `dev`-Build-Pipeline (`5.0.0-dev.<datum>+<sha>`) | `lyric --version` als NativeAOT-Binary auf fünf Tier-1-Zielen aus CI, `dev`-Archiv veröffentlicht | M |

### Phase 1 — Kern lauffähig

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M1 Runtime-Kern** | `liblyr.a` in C11: Allokations-API mit Typdeskriptoren (V4), **Boehm-GC dahinter** (L1 Stufe 1), Wurzeln/Pinning-API, Panik mit Backtrace (E5, libbacktrace), `lyr_init/shutdown` (H1), Strings (V9, NUL-terminiert X3), Arrays (V10), Konsole; ASan/UBSan/TSan-Profil mit **clang** (C7: `zig cc` hat keine ASan/TSan-Runtime), valgrind über debuginfod | C-Testprogramm gegen `liblyr.a` auf Tier 1; Sanitizer grün | M |
| **M2 Erstes natives Programm** | Frontend aus 4.x auf den 08-Sprachkern (Funktionen, Ganzzahlen, Strings, `if`/`while`/`for`-Range, Structs als Wert, Aufrufe, `let`/`var`); **IR** (typisiert, selbstbeschreibend — L7-Tür) → **C-Emission** (C1–C6: eine `.c` je Modul, Mangling, `#line`, `goto`-Form) → `zig cc` → Binary; `lyric build`/`run` minimal (C1/C2 Teilmenge), `out/`-Layout (P5), Cache-Schlüssel (L7) | `hello.lyr`, `fizzbuzz.lyr`, `fibonacci.lyr` migriert und nativ; **Messpunkt 1**: Start < 5 ms, Binary < 2 MB (L11); `lyric run` warm ≤ 50 ms über Programmstart (C9) | L |
| **M3 Wertmodell und Typsystem** | 02/03 vollständig: Structs inline, Klassen (Header, GC), Felder `var`/unveränderlich, `with`, `?T` mit Niche (V5), `??T`, `T[]`/`T[N]`/`Slice<T>`/`StringView` (A2), Tupel, Enums (V6), Ranges als Structs, `int`-Aliase, Koerzionen (T1c/T3), **Überlaufprüfung** (T2, `+%`), `as`, Generics durch **Monomorphisierung** mit Cache-Einheiten (C3), Typ-Patterns, Pattern-Matching (08 Y-Reihe) | `arith/arrays/structs/enums/tuples/optionals/patterns/generics`-Beispiele nativ; Konformanz 02/03; **Messpunkt 2** `bench/`: Schleifen/Arith/Struct-Arrays gegen Go/C# (Ziel Faktor 1,2–3 zu C) | XL |
| **M4 Interfaces und Abstraktion** | 04: Fat Pointer + VTables (V7), Boxing beim Übergang, generische Extends (X1/X2) auf `T[]`/`?T`/Interfaces, assoziierte Typen, Operator-Interfaces (D6), `by`-Delegation, Überladung nach Arität, benannte Argumente, `Display`/**`Debug` fest im Compiler** (D7), `Point(1, 2)`-Zucker, `sealed`; Synthese von `Equatable`/`Hashable`/`Clone`/`Default` **vorläufig im Compiler**, wandert in M9 nach `std.core` | `interfaces/shapes/objects/vectors/closures/lambdas` nativ; Konformanz 04 | L |
| **M5 Fehler** | 05 + L5: versteckter Fehlerslot (E1–E3), `throws`-Mengen, `try`/`try?`/`try!`, `catch`-Patterns und `in [A, B]`, `Error`-Wurzel + `Exception`, `defer`-Cleanup-Kette (E4), `using let`/`Closeable` (R-Reihe), `main throws`, Paniken (E5, 101), Backtrace-Profil (E8), `never` | `bank.lyr`/`errors`-Beispiele; Konformanz 05; Sanitizer-Lauf über Wurfpfade | M |
| **M6 Koroutinen, Scheduler, Threads** | L4 + 06: Assembler-Switch je ABI (K2), Stacks mit Guard-Pages (K1), Fremd-Frame-Zähler (K4), `park`/`unpark`, Scheduler je Thread (G1), Poller in C (S2: epoll/kqueue/AFD — Windows' Sockets mit M8b S11, wepoll verworfen: Review M6-29), `spawn`/`Task`/`TaskScope`/`spawnDetached`, `Channel`/`Select`/`Timer`/`sleep`/`timeout`, `Cancelled`, Threads (G2) + `Mutex`/`RwLock`/`Once`/`Atomic`, **Generatoren als Iteratoren** (I7, `Coroutine :: Closeable`), Signale als Kanal (Q9) | `generator.lyr`, Channel-Pingpong, Thread-Pool-Test, Ctrl+C-Shutdown-Beispiel; Konformanz 06; TSan-Profil grün | XL |

### Phase 2 — Bibliothek und Module

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M7 Module und Pakete** | 07 + W2: Sichtbarkeit `private`/`internal`/`pub`, Modulname aus Pfad, `pub import`, Prelude (`std.prelude`, B2), Editionen, **`lyric.toml`** (P-Reihe), Pfad + Git, MVS, `lyric.lock` mit Hashes, `[native]`, Profile (P3), Ziele (P4, Runtime aus Quelle je Tripel T3), `out/`-Sperre, **Reproduzierbarkeits-Test** (P6), `lyric metadata`, `lyric clean/add/update`; **`build.lyr` (BS1–BS6) kommt nach M8b** — ein Build-Skript braucht `std.fs` und `std.process` | Mehrpaket-Beispiel mit Git-Abhängigkeit baut offline; Reproduzierbarkeit zweimal aus zwei Verzeichnissen; Cross-Build linux→windows | L |
| **M8a std-Kern** | B2–B7, B9, B12: `core` (Kern-Interfaces, `Num`-Turm, `Result`, `Box`, `Slice`/`StringView`/`T[]`-Member, Ranges), `iter` (Adapter als Extends, `Error`-Typ, `collect`), `collections` (`List`, Swiss-Table `Map`/`Set`, `Deque`, `Heap`, `+`/`*`), `string` (`StringView`, `Pattern`, `StringBuilder`; die Unicode-Tabellen: M8c), `fmt` (Formatsprache, f-String-Lowerung über `showTo`), `math`, `hash` (`Hasher`, SipHash), `test` (Assertions, `@callerExpr`, Subtests, `lyric test`; `@Bench`: M8c); dazu die Entscheidungen des Reviews vom 2026-10-05 (Nachtrag unten) | `lyric test` läuft die std-Tests; `inventory/stats/stack`-Beispiele; **Messpunkt 3**: Map/Sort/String-Benchmarks gegen Go/C# | XL |
| **M8b std I/O und System** | B8, B11: `io` (Reader/Writer/Seek, Puffer/Text, `ByteBuffer`, Konsole), `fs`, `path`, `net`, `process`, `os`, `time` (Duration/Instant/Monotonic/Date; Zone später M10), `random` (ChaCha8), `encoding`, `crypto` (Digests, HMAC, `randomBytes`), `sync`/`thread`-Feinschliff (Modulumzug nach 10 Q10, `Semaphore`, `parallelMap`), der **Windows-Poller über AFD** (06 S2), danach `build.lyr` | Echo-Server über `std.net`, Prozess-Pipeline-Beispiel, Datei-Werkzeug; **hier ist Lyric 5 für Alltagswerkzeuge benutzbar** (Dogfood: `stdlib-tests` und Beispiele vollständig) | L |
| **M8c std-Rest** | Was der Zuschnitt vom 2026-10-05 aus M8a herausnimmt: Unicode-Tabellen aus UCD-Dateien, `char`-Prädikate, `toUpper`/`toLower`; die übrigen Adapter und Terminatoren (`flatMap`, `chunks`, `peekable`, `rev`, …) mit werfenden Lambdas; Set-Operationen, `map[k]`, `list[a..b]`, List-Extras; `Result`, `From`/`Into`; `std.fmt.format`; die Wertform `Iterator<Item = T>`; `suppressed()`/`backtrace()` an der `catch`-Bindung (05 O3); `@Bench` und `lyric bench` | Wortzähler über Unicode-Text; `lyric bench` läuft die std-Benchmarks | L |
| **M9a `comptime`** | 09 A-Reihe: **IR-Interpreter** (die L7-Tür, hier gebaut) mit Budget, `comptime`-Ausdrücke/Blöcke/`if`/`for`, `std.meta` (R1–R6), `embed`, `@When`, Attribute Art 1/2 (`@Deprecated`, `@Allow`, `@MustUse`, `@Inline`-Familie mit `inline fn` und lokalen `fn`, `@Layout`, `@Shared`), **Synthese nach A5** (`Equatable`/`Hashable`/`Ordered`/`Clone`/`Default` als `comptime`-Defaults in `std.core`, M4-Provisorium fällt), Enum-Reflexion (R5) | `constants.lyr`, Synthese-Konformanzfälle; `comptime`-Tabellen-Beispiel | L |
| **M9b Makros** | 09 A8 + 08 Q-Reihe: `std.syntax`, `quote`/`#{}`, `macro`, `name!()`/`name! {}`, Attribut-Makros, Hygiene, `lyric expand`, Fehler mit Span (G12) | `examples/macros` migriert; `retry!`, `@Builder` | L |
| **M10 std nach Regel D** | B10, B11: `codec` + `json` + `toml` (mit `@Codec`-Synthese), `regex` (Pike-VM/lazy DFA), `uri`, `http` (Client + Server), `compress`, `term`, Zeitzonen (TZif) + UTS #35, `crypto`-Türen bleiben zu | HTTP-Server liefert JSON, Client holt es; Log-Filter mit Regex; **Messpunkt 4**: HTTP-Durchsatz, JSON-Codierung gegen Go | XL |
| **M11 Eigener GC** | L1 Stufen 2–3: nicht bewegender Immix, präziser Heap/konservativer Stack, Safepoints, Weak-Refs mit Rückruf, dann Sticky-Mark-Bit-Generational; Boehm bleibt als Profil zum Vergleich; mit den Safepoints die **Stack-Prüfung im Prolog** (01 S3) und `Atomic<Klasse>` (06) | **Messpunkt 5**: Allokationsdurchsatz, Pausen, Speicher gegen Boehm und Go; `bench/gc/` | XL |

### Phase 3 — Werkzeuge

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M12a Diagnostik und Lints** | W6/W7: Record mit beschrifteten Spans und Notizen, rustc-Ausgabe, Folgefehler-Vergiftung, NDJSON, `lyric explain`, **die Verben `check`, `new`, `init`, `env`, `completions`, `fix`** (11 C2–C4, BS1; Durchsicht 2026-10-14), `diagnostics.toml` → Appendix, Wächter (G10), **alle Lints** mit rot/grün, `--deny-warnings`, Fixes mit `applicability`, `lyric fix` (5-intern); die Noten zu verborgenen Kandidaten und verdeckten Prelude-Namen (07), der Lint für fremde Konformanzen (03 X3) — Plan im Nachtrag 2026-10-19 | Katalog vollständig, jede Warnung getestet; `lyric fix` wendet `safe`-Fixes an | L |
| **M12b Inferenz und Abstraktion** | Die Sprach-Uhren der Reviews (geschnitten 2026-10-19): 03 S15a (Typargumente am Methodenaufruf, Fixierung in Funktionen), N2b (Schranken geschriebener Typen), S5, S16, T8 (Unifikation mit Literal), T19 (Kettenmeldung); 04 R4a3 (Arität über Blöcke), R4b (generischer Weiterleiter), S7 (mehrere `From`: entscheiden); SEM0047, SEM0082, Delegation an ein Kind-Interface-Feld | je Punkt ein Konformanzfall rot davor, grün danach; S7 entschieden | M |
| **M12c Doku-Grundlage und Website** | 14 D (geschnitten 2026-10-20): die Kommentare in W8-Form (K2a–K2d), der Renderer `Lyric5.Doc` mit dem Thema aus 14 B, `tools/Site` (Startseite, Releases), das Gerüst des Books, Spec und Katalog, Pagefind, der Legacy-Umzug, `site.yml`; `tools/DocGen` und `docs.yml` fallen | `/dev/` live mit Startseite, Book-Gerüst, Spec, Katalog, Suche und Legacy; Kommentar-Wächter auf 0 | L |
| **M13a fmt, doc, api** | E5/E7: Formatter mit Trivia (Kommentare, Klammern), Semantik-Erhalt-Test, `lyric doc` (Site aus `///`, über `Lyric5.Doc` aus M12c; die std unter `/dev/std/`, ihre `# Examples` als Doc-Tests), `///`-Doc-Tests, `lyric api` + `--diff` | Repo hält `lyric fmt --check`; Doc-Site der std | M |
| **M13b LSP, DAP, Clients** | E1–E4, E8: `lyric lsp` mit modulgranularer Wiederverwendung, Code Actions, Tests im Editor; `lyric dap` über lldb-dap + `lyric.lldbinit` (Summaries, Panik-Breakpoint, Tasks-Scope); `tree-sitter-lyric`; VS Code/JetBrains-Clients auf 5 | Editor: Hover/Completion/Rename/Fix, Test-CodeLens; Debugger hält an Panik mit Lyric-Werten; Windows-lldb-Risiko gemessen | XL |
| **M14 FFI und Einbettung** | W4/W5 nutzerseitig: `extern "C"` vollständig, `@Layout`, `Ptr`/`CStr`/`unsafe`, `ffi.Callback`/`GcHandle`, fremde Threads (X7), `@Export` + Header, `lyric build --lib`, `LyrConfig`/`LyrStatus`/`lyr_step` (H1–H6), `ffi.Library` | `examples/ffi` (C-Bibliothek gebunden), `embedded-host` in C gegen `lib<app>.a`; Spec `13-abi` | L |
| **M14b C-Bibliotheken** | 11 „Review 2026-10-28“ (E1–E13, E3′, N1–N8): `[native]` mit `archives`, `dylibs` (neben das Binary), `defines`, `frameworks`, `pkgConfig`, Tabellen je Tripel; `[native.vendor]` (Git, URL mit Hash, lazy, im Lock); Bindungen aus dem Header (`bindings`, `lyric bindgen`); `std.build` neu (`Target`, `cLibrary`, `link*`, `cEnv`); `--lib` bündelt Archive; `LNK`-Diagnosen — Plan im Nachtrag 2026-10-28 | `examples/native-prebuilt` (vorgebautes Archiv, CoreFoundation auf macOS), `examples/raylib` aus dem Quelltext mit erzeugten Bindungen | L |
| **M15 Distribution** | W9: Archive Tier 1, minisign, `lyric toolchain install/list/default/update`, Go-1.21-Re-exec nach `[package] toolchain`, `lyric toolchain install zig`, `dev`/`stable`-Kanäle, Website-Mirror auf dem VPS des Maintainers (14 C9: härten, `dl.lyric.oil1i.de`, Upload im Release-Job) | `lyric toolchain install dev` auf drei OS; Release-Pipeline probeweise `5.0.0-dev` | M |

### Phase 4 — Abschluss

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M16 Migration** | R1/R2: eingefrorenes 4.x-Frontend als Bibliothek, `lyric fix --from-4` mit allen R2-Regeln, `tests/fix-from-4/` (vorher/nachher je Regel), `lyric.json` → `lyric.toml` | `examples/` vollständig über das Werkzeug migriert; Bericht listet jede `needsReview`-Stelle | L |
| **M17 Spec 5.0, Suite, Guide** | R4: Lückenschluss der Spec-Kapitel (der Rest entstand je Meilenstein), Konformanzsuite vollständig (`since: 5.0.0`), das Book (14 W6: Kapitel nach dem Lernweg, Snippets in der Suite), Katalog-Appendix generiert, Spec-Pin | Spec 5.0 getaggt; Suite grün; Guide-Suite grün | L |
| **M18 Release 5.0.0** | Release-Checkliste (Version + README + Ratchets zusammen, Tag nach grüner CI auf `main`, Spec-Pin zuletzt), der 4.x-Treiber verlässt `main` und **`lyric5` wird `lyric`** (11 C1), `lyric-script 1.0.0`-Umbenennung (R6), Archivierung, Website (`/5.0/` eingefroren, `/` auf das Stable — 14 C4) | `5.0.0` auf GitHub + Website; `lyric-script 1.0.0` daneben | M |

## Abhängigkeiten

```
M0 → M1 → M2 → M3 → M4 → M5 → M6
                 │      │    │    └→ M8b → M10
                 │      │    └→ M14 → M14b
                 │      └→ M8a → M9a → M9b
                 └→ M7 ─┴→ M13a/M13b (nach M12)
M2 → M11 (parallele Spur, jederzeit; vor M10 für Messpunkt 5)
M2 → M12a (wächst mit jedem Meilenstein; Abschluss nach M9b) → M12b → M12c → M13a
M7 → M15
M8b → M16 (fix braucht die Zielbibliothek)
M8b → M8c (std-Rest; vor M10, dessen `regex` die Unicode-Tabellen braucht)
alle → M17 → M18
```

**Kritischer Pfad**: M1 → M2 → M3 → M4 → M5 → M6 → M8b (erste Alltagstauglichkeit) → M9a →
M10 → M17 → M18. Parallel dazu: M11 (GC) und M12a/M12b (Diagnostik, Inferenz) als eigene Spuren; M13 und M14
nach M8b.

## Messpunkte (Z2) und ihre Ratchets

| Punkt | Nach | Misst | Ratchet |
|---|---|---|---|
| 1 | M2 | Start, Binary, `lyric run` warm | < 5 ms, < 2 MB, ≤ 50 ms über Programmstart |
| 2 | M3 | Arithmetik, Schleifen, Struct-Arrays | ≤ 3× C, ≤ 1,5× Go |
| 3 | M8a | `Map` einfügen/suchen, Sort, String-Ops | ≤ 1,5× Go — nach M8a: maps 1,36×, sorting 1,21×, strings 1,70×; Ratchet mit Uhr: erreicht nach M11, sonst neu entscheiden (Nachtrag 2026-10-05, Uhren) |
| 4 | M10 | HTTP-Durchsatz, JSON | ≤ 2× Go `net/http` |
| 5 | M11 | Allokationsdurchsatz, Pausen, RSS | besser als Boehm in allen drei; Pausen < 10 ms bei 1 GB |

## Was bewusst nicht im Plan steht

Alle **Türen** aus 00–12 (IR-Interpretation als Ausführungsart, Script-Nesting, GUI-Format,
Registry, Workspaces, `bindgen`, SortedMap, Ed25519, HTTP/2, Bare-Metal, Windows-msvc-PDB,
Work-Stealing, präemptive Koroutinen): sie bekommen erst dann einen Meilenstein, wenn ein
Nutzer sie braucht — das ist der Unterschied zu Oil.

## Nachtrag 2026-10-05 — Review vor dem Abschluss von M8a

Nach M8a S11a hat der Maintainer die offenen Punkte aus M4 bis M8a durchgesehen und entschieden
(110 Entscheidungen). Sie stehen in den Bereichsdokumenten, je in einem Abschnitt „Review
2026-10-05“. Für den Plan folgt daraus:

| Was | Folge |
|---|---|
| **Zuschnitt M8a** | M8a schließt mit den Review-Entscheidungen und dem, was M8b braucht: `StringView`, `Pattern`, `fromUtf8`; `showTo`/`debugTo` und f-Strings in einen Builder; Container-Anzeige (10 C9); `collect`/`FromIterator`; `x in xs` |
| **M8c neu** | der Rest der alten M8a-Liste, nach M8b (Tabelle oben) |
| **`build.lyr`** | aus M7 hinter M8b gelegt |
| **Abschlussblöcke** | R0 Ablauf (Spec-Pin, diese Texte, Test-Hygiene) · R1 Compiler-Fundament (Tiefengrenze, Emitter-Tests als Pakete, Namen statt Nummern, nur Erreichtes senken, unerreichbarer Boden) · R2 Syntax · R3 Namen und Module · R4 Typen und Inferenz · R5 Fehler und Generatoren · R6 Nebenläufigkeit · R7 std-Korrekturen · R8 Pakete und CLI · R9 Leistung (IR-Optimierer, ThinLTO, Messungen) · S12–S16 der M8a-Rest |
| **Uhren** | M11: Stack-Prüfung im Prolog, `Atomic<Klasse>`, Messpunkt 3, der GC-Test `weak` · M12: Noten und der Lint für fremde Konformanzen · M8c: Unicode aus UCD |
| **Abgeschlossen** | 2026-10-07: R0–R9 (#308–#362), S12–S16 (#363–#373) gemergt; Messpunkt 3 nach M8a: maps 1,36×, sorting 1,21×, strings 1,70× Go (vorher 1,90/1,53/2,53) |
| **Durchsicht 2026-10-07** | Die offenen Punkte des Laufs R0–S16, mit dem Maintainer entschieden: in 01, 03, 04, 10, 11 je im Abschnitt „Review 2026-10-07“. Uhren: M8c — `Debug` eines Textes maskiert, `&` aus den Builder-Signaturen, `trimMatches`, `toBytes`, `char`-Bereiche, das Builder-Wachstum aus der Reihe · M12 — Typargumente am Methodenaufruf, Fixierung in Funktionen, Arität über Blöcke, der generische Weiterleiter · M10/M11 — der Vorgabe-Hasher. Die Plattform-Jobs und die Benchmarks der CI laufen auf `main` |
| **Durchsicht 2026-10-08** | Die offenen Punkte aus M8b S2–S9, mit dem Maintainer entschieden („alle Empfehlungen“): in 03 und 10 je im Abschnitt „Review 2026-10-08“. In M8b: S8c (stderr zeilengepuffert; `print` in eine Pipe, die niemand liest, beendet das Programm), S10 (`Reader`/`Writer` werfen `[IoError, Cancelled]`). Uhren: M8c — `T[N]` gleich und hashbar, der Elementtyp eines Literals an einer View-Stelle, **ein Sammelslice Melde-Qualität**: `1.` und `.5` sagen, dass ein Gleitkommaliteral Ziffern auf beiden Seiten des `.` hat; ein Name mit Zeichen außerhalb von ASCII gibt eine Meldung statt einer je Zeichen und Folgefehlern; nach `LYR-SEM0126` läuft der abgelehnte Typ als Fehlertyp weiter, ohne `SEM0138`/`SEM0003`/`SEM0006` am Gebrauch; `lyric5 test --target` wie `build` und `run` · M10 — Base64Url ohne Padding · M12 — geschriebene Typen gegen ihre Schranken, die R3-Warnung für eine Hülle um einen Handle ohne Namen |
| **Durchsicht 2026-10-08 (2)** | Die offenen Punkte aus M8b S10–S12, mit dem Maintainer entschieden („alle Empfehlungen“): in 10 im Abschnitt „Review 2026-10-08 (2)“. Bestätigt: `IpAddr` mit Feldern, `fork`/`exec`, `env` über der geerbten Umgebung, die konkreten Pipe-Typen. Uhren: M10 — Fristen für `resolve`, die Pipes eines Kindes unter Windows über den Completion-Port. M8c nach M8b freigegeben |
| **Durchsicht 2026-10-09** | Die offenen Punkte aus M8c S1–S13, mit dem Maintainer entschieden („alle Empfehlungen“; `v` nach Rückfrage „voll, wie T10“): in 03, 04, 05, 08, 10, 11 je im Abschnitt „Review 2026-10-09“. Uhren: M8c S15–S19 (Nachtrag zum M8c-Plan unten) — `is` auf eingebauten Typen und `Any` für sie; `Debug` für `?T`, `Display`/`Debug` für `T[N]`; die Synthese über `T[N]`-Feldern als Ganzes, `never` als Typargument unter `Error`; der Bereichsindex über den View, `LYR-SEM0126` an Feldern, Elementen und Typargumenten, die leere nachgestellte Lambda; das Gate je Profil · M12 — `?U` aus einem Lambda, das `T` liefert; mehrere `From` eines Typs (vertagt). Bestätigt: die Lesarten zu Unicode, Adaptern, `map[k] = null`, Blankets, Zeugen über den View, `Backtrace`, Prüfsummen, Assertions und Benchmarks |
| **Durchsicht 2026-10-12** | Die offenen Punkte aus M9a S1–S9 und S12a (D1–D8 des Berichts), mit dem Maintainer entschieden („alle Empfehlungen“): in 07, 09 und 10 je im Abschnitt „Review 2026-10-12“. In M9a: S10 (D2–D5: die Familie fragt mit dem Modul des Konformers, der generische Kopf bleibt, der Initialisierer über Infos, Variante und Nutzlast per Info), S11 (D5). Bestätigt: `module;` als Ort der Modul-Attribute (D6). Uhren: M9a S12 — `assert(c) { … }` (D1), eine Modulbindung nur auswerten, wo ihr Global erreicht wird (D8) · M10 — der libm-Port für Compile-Zeit und Laufzeit (D7). Dazu entschieden: **die Elementarfunktionen werden freie Funktionen in `std.math`** (`sqrt`, `cbrt`, `exp`, `ln`, `log2`, `log10`, `pow`, die Winkelfunktionen, `hypot`, `floor`, `ceil`, `round`, `trunc`, `abs`, `signum`); am Typ bleibt, was den Typ beschreibt (`epsilon`, `infinity`, `nan`, `min`/`max`/`leastPositive`/`bitWidth`, `isNan`/`isInfinite`/`isFinite`, `toBits`/`fromBits`, `totalCompare`, `parse`, `wrapping*`/`checked*`); `clamp` bleibt Methode — 10 Z2 wird mit dem Slice nach S11 geändert; und **`std.core` wird durchgesehen**: sortiert, verdichtet, ausgelagert, was nicht Kern ist (das größte Modul, das jedes Programm lädt) |
| **Abgeschlossen** | 2026-10-13: M9a S1–S12 (#444–#457, dazu S11b, S11c, S12b), Suite 1391 in beiden Profilen, Spec-Pin lyric-spec#292. Messpunkt 1: hello 1 362 896 Bytes, 841 Zeilen C, Start 1,28 ms gegen 0,94 ms plain C. Lesarten, die der Maintainer sehen soll: statische Defaults (03 Review 2026-10-12), `Variants.all` statt `variants`, der Integer-`pow(n)` bleibt Methode, `std.iter` als Prelude-Modul, ein `inline`-Rumpf trägt die Aufrufzeile. Türen: `Default` nach `std.core` (braucht `f.type` in Typposition, M9b), `comptime for` über Tupelelemente, `std.unicode`, libm-Port (M10, D7) |
| **Abgeschlossen** | 2026-10-13: M9b S1–S6 (lyric#460–#465, Spec lyric-spec#293–#297): `std.syntax` und die Brücke, `macro` und `name!(…)`, `quote`/`#{}` mit Hygiene, Attribut-Makros, die Diagnosen expandierten Codes (eine synthetisierte Datei je Expansion) und `lyric expand`, `examples/macros`. Messpunkt 1: hello 791 936 Bytes, 841 Zeilen C, Start 1,48 ms gegen 1,03 ms plain C; ein Bau mit einer Stelle 2,2 s gegen 1,57 s ohne (Punkt 117). Türen geprüft (S6): `Default` bleibt beim Compiler (bestätigt 2026-10-13); `comptime for` über Tupel bleibt Tür — ein Makro könnte die Tupelblöcke je Größe schreiben, aber `std.core` trägt keine Stelle eines eigenen Makros (P6), und ein Makro-Modul in std kostete jedes Programm eine Runde. Punkt 104 erledigt. Offene Punkte 105–146 im Bericht; vor M10 wird der Maintainer gefragt |
| **Durchsicht 2026-10-13** | Der M9a-Abschlussbericht: die sieben Lesarten — statische Interface-Defaults (03 Review 2026-10-12), `Variants.all()`, der Integer-`pow(n)` als Methode, `std.iter` als Prelude-Modul ohne Namen, ein `inline`-Rumpf trägt die Aufrufzeile, `Default` bleibt beim Compiler, was in `std.core` bleibt (10 Review 2026-10-13 A3) — **alle bestätigt** („Alle sieben Lesarten bestätigt, M9b planen“). Die Punkte 81–84 (CI) und 85–104 (S10–S12b) bleiben gesammelt; 104 (die std-Selbsttests schreiben ins Arbeitsverzeichnis) bekommt die Uhr M9b S6 |
| **Durchsicht 2026-10-14** | Der M9b-Abschlussbericht liegt vor (Lesarten L1–L9: die Engine sagt eine Diagnose einmal, Aufrufer-Knoten behalten ihren Ort, die Noten-Kette innen zuerst, `lyric expand` druckt die Deklarationen, freie Namen im Quote-Inhalt zählen als Import-Gebrauch, ein Paket unter `examples/` ist nicht der 4.x-Korpus, Punkt 104 über `tempDir()`, die beiden Türen; die offenen Punkte 105–146) — **alle bestätigt** (2026-10-14: „Plan freigegeben, alle Lesarten bestätigt, M10 S1 starten"). M10 nach dem Nachtrag „der Plan für M10" unten, freigegeben. Dazu entschieden: **die Verben ohne eigene Plan-Zeile — `check`, `new`, `init`, `env`, `completions` (11 C2–C4, BS1) — gehören zu M12** |
| **Abgeschlossen** | 2026-10-15: M10 S1–S13 (lyric#468–#480, Spec lyric-spec#300–#309; dazu lyric#475, ein verlorenes Wecken im Scheduler, das der Abschluss fand): `std.codec` mit Synthese, `std.json`, `std.toml`, `std.uri`, `std.regex`, `std.http` Client und Server, `std.compress`, `std.term`, die Zonen und UTS #35, der musl-libm-Port bitgleich in Laufzeit und Compiler, die Windows-Pipes auf dem Poller. Messpunkt 4: http 2,36×, json 3,90×, regex 2,96× Go (berichtet). Offene Punkte 147–191 im Bericht (lyric#480) |
| **Durchsicht 2026-10-15** | Der M10-Abschlussbericht: die dreizehn Lesarten — Format per Referenz und die Schicht am Format, `Bytes` als Byteform, `default = true` als Tür, `JsonValue`-Objekt als `Map`, `at(i)`, `%20`, IPvFuture und relatives `resolve`, `find` mit Gruppen und kein `Regex :: [Pattern]`, `withText`/`withJson`, leeres `NO_COLOR`, `Zone.offset()` jetzt und die Lückenregel, die Pipe auf dem Thread des ersten Aufrufs, `codec` neben `encoding` ohne Rename — **alle nach Empfehlung bestätigt** (10 „Review 2026-10-15" M1–M13); freigegeben: `ParseError` mit `detail`/`offset` (M14), `Duration`/`Date`/`Time`/`DateTime` als `Codable` über ihren Text (M15), JSONTestSuite als Fixtures (M16). Entschieden: **eine Sweep-Runde vor M11** — 188 (der Macro-Host und die synthetisierten Konformanzen), 191 (ein armiertes Warten nach den http-Tests), 148/147/149, M14–M16, 153/158–160 |

## Nachtrag 2026-10-07 — der Plan für M8b

M8b nach seiner Zeile oben (B8, B11, 06 S2–S4, W3). Freigegeben am 2026-10-07 „wie M8a“: ohne
Halt bis zum Abschluss, Merge bei grüner CI, offene Punkte gesammelt, Bericht am Ende; vor M8c
wird neu gefragt. Die Slices heißen **M8b S1–S14**; jeder ist ein PR, sein Regel-PR in der Spec
geht voran (Spec-Pin).

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Die Grenze std ↔ C** bleibt die der Laufzeit-Natives: rumpflose std-Funktionen über die Intrinsics-Tabelle (wie `waitOnPoller`, `putBytes`). Dazu dünne C-Hüllen in `runtime/src/{fs,net,process,os}.c`, **ein Systemaufruf je Hülle**: `EINTR` wird dort wiederholt (O1), `errno`/`GetLastError` wird ein Code, aus dem `std.io` die `IoErrorKind` macht (O3). Texte gehen als `LyrStr*` (NUL-terminiert, 11 X3; Windows wandelt nach UTF-16), Puffer als `Slice<uint8>` | Die Nutzer-FFI (`Ptr`, `CStr`, `unsafe`) ist M14; sie vorzuziehen hieße M14 in M8b. Gos Weg (Systemaufrufe in Go selbst) braucht `Ptr`. Rusts `sys`-Schicht ist genau diese Form: dünne Plattform-Hüllen unter einer plattformfreien API. **Uhr: M14** — mit `Ptr`/`CStr` ziehen die Hüllen nach `extern "C"`, die Tabelle behält nur, was Compiler-Semantik ist |
| P2 | **Warten auf I/O**: Sockets und POSIX-Pipes sind nicht blockierend und melden sich über den Poller (06 S2): `register(fd, lesen \| schreiben)`, einmal feuernd (epoll `EPOLLONESHOT`, kqueue `EV_ONESHOT`, unter Windows eine AFD-Poll-Anfrage je Warten). Der Scheduler parkt den Task bis zur Meldung und versucht dann erneut. **Reguläre Dateien, DNS, das Lesen der Konsole und Windows-Pipes** laufen über einen std-internen **I/O-Pool** (06 S3, S4): ein `Pool` aus `std.thread`, beim ersten Gebrauch gestartet | io_uring und das Completion-Modell von IOCP sind verworfen (06 S2: zwei Modelle in der stdlib). Gos Übergabe des P bei einem blockierenden Aufruf setzt einen M:N-Scheduler voraus, den es nicht gibt. Der Pool ist der Weg von libuv und tokio |
| P3 | **Konsole**: `stdout()`/`stderr()` sind gepufferte `Writer` (O9). Ein Flush schreibt über den Pool, wenn auf dem Thread ein Scheduler läuft, sonst direkt — ohne Scheduler gibt es keinen zweiten Task, den ein blockierender Write aufhielte. Die Frage „läuft hier einer“ zählt nicht für die Erreichbarkeit: `main` wird davon kein Task (M6-26), Messpunkt 1 bleibt | Alles über den Pool hieße: jedes `println`-Programm startet Scheduler und Pool-Thread. Immer direkt zu schreiben verletzte S4 |
| P4 | **Handles** sind Klassen (O4) mit einem Zustand `closed`; eine Operation nach `close()` wirft `IoError { kind: Closed }`. Gelesen und geschrieben wird nur über `Slice<uint8>`, nie über eine Kopie | O4; die Kopie ist für einen Strom die Hälfte des Durchsatzes |
| P5 | **Plattformen**: jeder Slice baut POSIX (Linux, macOS) und Windows, außer dem Netz — Windows' Poller über AFD und Winsock sind ein eigener Slice (S11); bis dahin sind die Netz-Tests unter Windows übersprungen und sagen warum. Ein Slice, der Plattform-Code ändert, läuft vor dem Merge **einmal von Hand auf allen Plattformen** (`workflow_dispatch`, 11 Review 2026-10-07), da ein PR nur noch Linux x86-64 und Windows-Tests prüft | Die CI-Entscheidung vom 2026-10-07 verschiebt Plattformfehler auf `main`; für Plattform-Code ist das zu spät |
| P6 | **Spec**: Kapitel 12 bekommt je Slice seinen Abschnitt (I/O, Puffer und Text, Encoding/Zufall/Krypto, Zeit, Dateien und Pfade, Konsole und System, Netz, Prozesse); die Fälle liegen unter `conformance/cases/12-stdlib/`. Fälle mit Dateien arbeiten im temporären Arbeitsverzeichnis des Läufers, Netz nur über `127.0.0.1` innerhalb eines Programms | Ein Fall, der das Netz außerhalb braucht, prüft das Netz, nicht die Sprache |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** Modulschnitt | 10 Q10: `Mutex`, `RwLock`, `Once` nach `std.sync`, dazu **`Semaphore`**; `Thread`, `Pool` nach `std.thread`, dazu **`parallelMap`**; `Signal`/`signals` nach `std.os` (Q9) | Fälle für `Semaphore` und `parallelMap`; alle M6-Fälle mit den neuen Importen |
| **S2** io-Kern | O1, O3: `IoError`, `IoErrorKind`, `Reader`/`Writer`/`Seek`, `SeekFrom`, die Defaults (`readExact`, `readToEnd`, `readToString`, `writeAll`, `writeString`), `io.copy`; die Speicherströme `ByteReader` und `ByteBuffer` | EOF nur bei 0, ein leerer Slice gibt 0 sofort, ein partieller `write` |
| **S3** Puffer und Text | O2: `BufReader`, `BufWriter` (die R3-Warnung, wenn ungeschlossen), `TextReader` (`readLine`, `lines`, `readToEnd`, `chars`, `skipBom`; ein geteilter Codepunkt bleibt im Decoder, ungültiges UTF-8 wirft `InvalidData`), `TextWriter` | ein Codepunkt über die Puffergrenze, ein BOM, eine letzte Zeile ohne Umbruch |
| **S4** Encoding, Zufall, Krypto | Q5: `Base64`, `Base64Url`, `Hex`, `Utf16`, die Byte-Getter auf `Slice<uint8>`, `int.fromBytesLE` & Co. (`std.bytes` geht auf); Q2: `Random` (ChaCha8, Strom innerhalb des Majors zugesagt), die freien Kurzformen; Q4: `Sha256`, `Sha512`, `Sha1`, `Md5`, `Hmac`, `randomBytes`, `randomUint64`, `constantTimeEq` | die Testvektoren der RFCs und NIST; der Strom von `Random.seeded` als Fall |
| **S5** Zeit | Q1 ohne Zonen-Datenbank und UTS #35: `Instant` (Wanduhr), `Monotonic`, `Duration` mit `Display`/`Parse`/Operatoren, `Date`, `Time`, `DateTime`, `Zone.utc`, `Zone.fixed`, RFC 3339 | Schaltjahre, Grenzen von `Duration`, RFC-3339-Beispiele |
| **S6** Dateien | O4, O5 (erster Teil), O6, P1–P2: die fd-Hüllen (POSIX, Windows), der I/O-Pool, `File` (`open`, `create`, `openWith`; `Reader`, `Writer`, `Seek`, `Closeable`), `fs.readText`/`readBytes`/`lines`/`writeText`/`writeBytes`/`appendText`; `std.path` | jede `IoErrorKind`, die eine Datei geben kann; TSan über den Pool |
| **S7** Verzeichnisse | O5 (Rest): `exists`, `metadata`, `remove`, `removeDir`, `removeAll`, `createDir(All)`, `copy`, `rename`, `readDir`, `walk`, `canonicalize`, `absolute`, `tempDir`, `tempFile` | ein Baum angelegt, gelaufen, entfernt — auf jeder Plattform |
| **S8** Konsole | O9, P3: `stdin()`, `stdout()`, `stderr()`, `print<T :: [Display]>`, `println`, `eprint`, `eprintln`, `flush`, der Flush am Programmende | Messpunkt 1 hält; zeilenweise am Terminal, blockweise sonst |
| **S8c** Konsole, Nachtrag | 10 Review 2026-10-08: die Standardfehlerausgabe immer zeilengepuffert; `print` in eine Pipe, die niemand liest, beendet das Programm, wie SIGPIPE es beendet hätte | stderr kommt in einer Pipe vor stdout; `prog \| true` endet am Signal |
| **S9** System | Q9: `args`, `env`/`envs`/`setEnv`, `cwd`/`setCwd`, `exit`, `platform`/`arch`, `homeDir`, `tempDir`, `hostname`, `cpuCount`, `pid` | — |
| **S10** Netz (POSIX) | 10 Review 2026-10-08 zuerst (**S10a**): `Reader`/`Writer` werfen `[IoError, Cancelled]`; dann O7, P2 — **S10b**: die Bereitschaft im Poller (epoll, kqueue), `IpAddr`/`SocketAddr` mit `Parse`, `TcpListener`, `TcpStream`; **S10c**: `resolve` über den Pool (kein Abbruchpunkt, wie ein Datei-Aufruf), `UdpSocket`, das Wecken über Threads (offener Punkt 1t: die `Waits` eines Sockets) | **Echo-Server über `std.net`** (S10c); viele Verbindungen auf einem Thread (S10b) |
| **S11** Netz (Windows) | 06 S2: der Poller über AFD (eigene Anbindung, Review M6-29), Winsock — eine AFD-Anfrage je Socket für alle bewaffneten Richtungen (wepoll, mio) | die Netz-Tests von S10 auch unter Windows |
| **S12** Prozesse | O8: `Command`, `Stdio`, `Child`, `ExitStatus`, `Output`; **S12a** POSIX: `fork`/`exec` (statt `posix_spawn`: ein Arbeitsverzeichnis bräuchte glibc 2.29, offener Punkt 1z) mit `SIGCHLD` intern (Q9) und SIGPIPE im Kind auf dem Default — ein ignoriertes Signal überlebt `exec` (wie Rusts `Command`); **S12b** Windows: `CreateProcessW` (die Argumente nach den Regeln der C-Laufzeit gequotet, nur die drei Handles vererbt), das Ende über ein registriertes Warten; die Pipes über den Poller (POSIX) oder den Pool (Windows) | **Prozess-Pipeline-Beispiel** (S12a) |
| **S13** `build.lyr` | W3 BS1–BS6: das Skript vor dem Compile, `std.build`, der eigene Modulraum, `rerunIfChanged`, `gen/`, die Vertrauensregel | ein Paket, dessen Skript Code erzeugt |
| **S14** Abschluss | **Datei-Werkzeug** als Beispiel; Dogfood (die std-Tests und die Beispiele auf dem Stand von M8b); I/O-Messung berichtend (eine Datei kopieren, Echo-Durchsatz, gegen Go); STATUS, CHANGELOG | die Zeile oben erfüllt |

**Nicht in M8b**: `Zone.load`/`Zone.local` und UTS #35 (M10), `std.uri`, `http`, `term`, `compress` (M10). Die
Prüfsummen von Q3 (`crc32`, `crc32c`, `adler32`) standen in keiner Zeile — **Uhr: M8c**.

### Tests

Je Slice: die Regel-Fälle der Spec (rot gegen den Compiler davor — dort fehlt die API), die
std-Tests unter `tests/std` (Dateien in `fs.tempDir()`), für Netz und Prozesse Programme in
`Lyric5.Tests` mit einer **Frist je Lauf** (ein hängender Test ist ein roter Job, kein verbrannter
Abend), TSan über Poller und Pool (`SanitizerTests`), je Regel eine Mutationskontrolle, die
rot werden muss (EOF-Semantik, partieller Write, geteilter Codepunkt, `EINTR`), und für
Plattform-Slices der Lauf von Hand (P5).

### Umfang

Laufzeit in C etwa 2500 Zeilen (davon AFD etwa 600), std in Lyric etwa 4000, der Compiler wenig
(die neuen Natives in der Tabelle, `print<T>`). Groß: S6, S10, S11, S12; mittel: S2–S5, S7,
S13, S14; klein: S1, S8, S9.

### Reflexion

- **Go** (`io`, `os`, `net`): die Form der Schnittstellen (O1) und der Netpoller mit parkenden
  Goroutinen ist das Vorbild. Gos Übergabe des P bei blockierenden Aufrufen ist es nicht — sie
  braucht den M:N-Scheduler, und der Pool (P2) erreicht dasselbe für die wenigen blockierenden Fälle.
- **Rust** (`std::io`, `std::sys`): die Plattform-Schicht unter einer plattformfreien API (P1) und die
  eine Fehlerart mit `kind` (O3). Rusts `std` blockiert; Lyric parkt (O10), ohne Färbung (08 D11) —
  kein `async`-Zwilling jeder API wie bei tokio.
- **libuv, tokio**: Bereitschaft für Sockets, ein Thread-Pool für Dateien — dieselbe Teilung wie P2.
- **Zig** (`std.os`, die neue `Io`-Schnittstelle): ebenfalls dünne Hüllen je Systemaufruf; Zigs Weg,
  das Warten als Parameter durchzureichen, ist hier unnötig, weil jede Koroutine parken kann.

## Nachtrag 2026-10-07 — Prüfung M0–M8a und der Nachholblock N

Der Maintainer hat am 2026-10-07 nach dem Abschluss von M8a geprüft, ob alles aus den bisherigen
Meilensteinen gebaut ist. Etwa 95 % waren es. Entscheidungen der Bereichsdokumente ohne Zeile im
Plan hatten aber keine Uhr: fünf Fehler, Tupel-`==`, `Identity`, generische Aliase und weitere.
Entschieden: ein **Nachholblock vor M8b S2**, alle übrigen Punkte bekommen eine Uhr.

| Block | Inhalt |
|---|---|
| **N1** Fehler und halbe Entscheidungen | `"x" * n` und `n * "x"`, `opaque type` (SEM0175), ein Generator ohne `yield`, ein geschlossener `TaskScope`, ein Name zweimal gebunden (SEM0176) — N1a; 01 B13 ganz (`GlobalPruning`), Traces ab dem ersten `.lyr`-Rahmen, keine Meldung nennt einen erledigten Meilenstein — N1b |
| **N2** Sprachlücken aus M3/M4 | Tupel `Equatable`/`Hashable`/`Debug`/`Display`, `?T :: [Equatable, Hashable]`, `T[]` und die Container `Clone` — N2a; generische Aliase, `Identity` für Klassen — N2b; Feldkurzform, Typmengen-Pattern — N2c; `Coroutine :: [Identity]` (06 A7) — N2d |
| **N3** Texte | veraltete Kommentare, CHANGELOG, STATUS, diese Dokumente — N3a; die Spec-Kapitel 01 (lexikalisch), Kernregeln 07/08, 04 Init-Reihenfolge — N3b |

| Was | Uhr |
|---|---|
| Interface-Wert aus Shape/Blanket (SEM0047), A8 generische Interfaces (SEM0082), Delegation an ein Kind-Interface-Feld | M12, mit dem generischen Weiterleiter (04 R4b) |
| `lyric check` (mit `--locked`) | M12 |
| 03 T8 Unifikation mit Literal, T19 Kettenmeldung, `it`-Verdeckungswarnung (08 F4), 07 K4/K7 | M12 |
| `inline fn`, lokale `fn` | M9a, mit der `@Inline`-Familie (Zeile oben) |
| `@Shared` | M9a (Zeile oben) |
| kleine std-APIs (`Map.from`, `Map.entries`, `FromArrayLiteral`, `toBytesLE`, `charIndices`, `isBlank`, `fromChars`, `parse<T>()`, `assertContains`, `assertEmpty`, String-Diff, `@Test{parallel}`, `chars()` als `DoubleEnded`, `sizeHint` in `collect`, die List-Extras), höhere std-Funktionen mit Fehlermenge, `string`/`StringView :: [Contains<char>]` | M8c |
| 01 K4 Fremdrahmen bei Abbruch und GC | M14 (Callbacks) |
| `x86_64-macos` ausführen, das `dev`-Archiv; bis dahin „gebaut, nicht ausgeführt“ in STATUS | M15 |
| 01 C2: eine `.c` je Programm und je Instanz (Abweichung in 01 festgehalten) | neu entscheiden bei Bauzeit-Bedarf, spätestens M15 (Messpunkt 1 kalt) |
| 01 E8: der Release hält keinen Fehler-Trace, „wenn der Typ es verlangt“ ist eine Tür | entschieden |
| `fibonacci` in Koroutinen-Form | M16 (Beispiele) |
| die 4.x-`stdlib/`, Version 4.6.0 | M17 (das Frontend ist bis dahin geteilt) |
| die Intrinsics-Tabelle → `extern "C"` | M14 (M8b P1) |

## Nachtrag 2026-10-08 — der Plan für M8c

M8c nach seiner Zeile oben, dazu jede Uhr, die auf M8c steht: 01 S13; 03 M4-1, S15b, S3; 05 M5-3;
10 C1, C2, S4b, S12c, S12d, S13, S14, Q3; in diesem Dokument die Zeile „kleine std-APIs“ und die
Durchsichten vom 2026-10-07 und 2026-10-08. Freigegeben am 2026-10-08 („Darf M8c nach dem Merge
von S14 starten?“ — ja): wie M8b ohne Halt bis zum Abschluss, Merge bei grüner CI, offene Punkte
gesammelt, Bericht am Ende; vor M9a wird neu gefragt. Grundlage ist eine Prüfung jedes Punkts gegen
den Code (gebaut, halb, fehlt), damit keine Entscheidung ohne Zeile bleibt (Lehre des Nachtrags
davor). Die Slices heißen **M8c S1–S14**; jeder ist ein PR, sein Regel-PR in der Spec geht voran.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Unicode-Tabellen in C, erzeugt** (10 C2): die UCD-Dateien `UnicodeData.txt` und `PropList.txt` mit ihrer Lizenz liegen unter `runtime/third_party/ucd/` (Version 18.0.0); `tooling/unicode/gen.py` (Python, nur die Standardbibliothek) schreibt `runtime/src/unicode_tables.h` und die Zeile `std.string.unicodeVersion`, beides eingecheckt. Die Kategorie als zweistufige Tabelle (Blöcke à 2^k Codepunkte, gleiche Blöcke einmal, `k` das kleinste Ganze), die einfachen Groß-/Kleinabbildungen als sortierte Paare, `White_Space` als Bereiche. CI prüft, dass der Generator dieselben Dateien schreibt | S4 sagt C-Schicht. Rust erzeugt seine Tabellen mit Python (`unicode.py`), Go mit Go (`maketables`). Ein Generator in C# bände die Tabellen an den Compilerbau; Tabellen als Lyric-Literale kosteten jedes Programm, das `char` benutzt, die Übersetzung von 70 000 Einträgen. Im Archiv der Laufzeit zieht nur ein Programm die Tabellen, das sie fragt |
| P2 | **Werfende Lambdas über 05 K4**: jeder Adapter und Terminator mit einem Funktionsparameter bekommt ein `E :: [Error]` — `map<U, E>(f: fn(Item) -> U throws E)` gibt einen Iterator mit `type Error = Join<I.Error, E>`, ein Terminator wirft `Join<I.Error, E>`. Mit `E = never` bleibt alles, wie es ist, ohne `try`. Ebenso `sortBy`, `sortByKey`, `arrayOf`, `Map.update`/`getOrInsert`/`retain`, `List.removeWhere` | 13 „mit werfenden Lambdas“, „höhere std-Funktionen mit Fehlermenge“. Swifts `rethrows` ist dieselbe Absicht als Schlüsselwort; K4 hat sie als Typparameter. Verworfen: zwei Fassungen je Funktion |
| P3 | **Der Builder ohne `&`** (10 S13) in std und Senkung zuerst, dann `std.fmt.format` zur Laufzeit über die Grammatik der f-Strings (S7) | ein Pfad für die Formatierung; `format` liest die Vorlage zur Laufzeit, ein f-String zur Übersetzungszeit |
| P4 | **Was die Sprache betrifft, steht als Regel in der Spec**, bevor der Code kommt: `T[N]` gleich und hashbar, ein Literal an einer View-Stelle, `char`-Bereiche, die Wertform `Iterator<Item = T>`, die Auskunft an der `catch`-Bindung | Spec-Pin |
| P5 | **Spec**: Kapitel 12 bekommt „Characters“ (S1), seine Abschnitte zu Text, Iteration und Sammlungen wachsen, dazu „Results and conversion“, „Formatting“, „Benchmarks“; die Sprachstücke in 03, 05, 06 | wie M8b P6 |
| P6 | **Plattform**: ein Dispatch-Lauf auf allen Plattformen nur, wo Laufzeit-Code plattformabhängig ist (M8b P5) — die Tabellen sind es nicht | Messpunkt 1 bleibt: hello zieht nichts davon |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** Unicode | P1; `char`: `category(): UnicodeCategory`, `isAlpha`, `isDigit` (Nd), `isAlphanumeric`, `isWhitespace` (`White_Space`), `isUpper`, `isLower`, `isControl`, `isAscii*`, `toUpper`/`toLower` (einfach), `toDigit(radix)`, `char.fromDigit`; `std.string.unicodeVersion` | jeder Skalarwert gegen eine eigene Lesung der Dateien im Test |
| **S2** Text über Unicode | `toUpper`/`toLower` auf Text; `trim*` über `White_Space`, `trimMatches*` (S12c); `isBlank`, `toBytes` (S12d), `string.fromChars`, `charIndices`, `chars()` als `DoubleEnded`, `parse<T>()`; `Debug` eines Textes maskiert (S14). *`Contains<char>` ging nach S9: das generische `contains<P>` erfüllt das Member nicht (`LYR-SEM0042`)* | `ß` bleibt `ß`; `Debug` druckt, wie geschrieben |
| **S3** Sprache klein | **S3a**: ein Literal an einer View-Stelle (03 S3); `char`-Bereiche, iterierbar (03 S15b) — über ein internes `Step` in std.core, da ein eigener Block für `Range<char>` neben dem für `Integer` dieselbe Konformanz zweimal wäre. **S3b**: ein nacktes `T[N]` `Equatable` und `Hashable` (10 S4b) | `'a'..='z'` iteriert; `Map<uint8[16], V>` |
| **S4** werfende Lambdas | P2 über die vorhandenen Adapter, Terminatoren und höheren Funktionen | `try xs.map((s) => try int.parse(s)).toList()` |
| **S5** Adapter und Terminatoren | `mapNotNull`, `flatMap`, `flatten`, `chunks`, `windows`, `dedup`/`dedupBy`, `scan`, `peekable`, `rev`, `cycle`; `average`, `minBy`, `maxBy`, `toMap`, `sorted`, `sortedBy`, `partition`, `groupBy`; `collect` liest `sizeHint` (I4) | I4 vollständig |
| **S6** Sammlungen | **S6a**: `Set`-Operationen (C5), `Set.of` (N3); `map[k]`, `map[k] = v`, `Map.from`, `entries` (C4); `List.from`, `shrinkToFit` (C3); `+` auf `Slice<T>` (C7; auf `T[]` schon da). **S6b**: `FromArrayLiteral` (K4). *`list[a..b]` nicht gebaut: 03 N2s `Index<Range…>` ist für einen eigenen Typ zwei Konformanzen derselben Schnittstelle, die D4 verbietet — eine Entscheidung (offener Punkt); bis dahin `list.asSlice()[a..b]`* | `let s: Set<int> = [1, 2]` |
| **S7** Ergebnis und Konversion | `Result<T, E>` mit `Result.of { … }` und `try r.get()` (05 E1); `From<T>`/`Into<T>` mit dem Blanket (04) | beide Brücken |
| **S8** Formatierung | P3: `&` aus `showTo`, `debugTo`, `format`; `std.fmt.format(template, args...)`; der Wachstumspfad des Builders aus der Reihe (01 S13). Geteilt: **S8a** `&` und Wachstumspfad; **S8b** der Wert eines eingebauten Typs als Interface-Wert — 01 V7 boxt „immer“, gebaut war es nur für Structs, Klassen und Enums, und `args...` braucht `int` und `string` als `Display`; **S8c** `std.fmt.format` | Messpunkt 3 `strings` nicht schlechter |
| **S9** `Iterator<Item = T>` als Wert, generische Zeugen | 03 M4-1: ein Interface-Wert mit fixiertem assoziiertem Typ; ein generisches Member erfüllt ein Interface-Member, wo seine Typparameter sich passend binden lassen — damit `string`/`StringView :: [Contains<char>]` (aus S2) | eine Funktion, die einen solchen Iterator nimmt und gibt; `assertContains("abc", 'b')` |
| **S10** Fehler an der Bindung | 05 M5-3: `e.suppressed()`, `e.backtrace()`, der Typ `Backtrace` | ein unterdrückter Fehler aus `using`, gesehen im `catch` |
| **S11** Prüfsummen, Bytes | Q3: `crc32`, `crc32c`, `adler32`; `toBytesLE`/`toBytesBE` | Testvektoren der Normen |
| **S12** Test und Bench | X1, X3: `assertContains`, `assertEmpty`, der String-Diff, `@Test { parallel = true }`; X4: `@Bench`, `Bench`, `blackBox`, `lyric5 bench` (`--save`, `--compare`). Geteilt: **S12a** die Tests — die drei Stücke von X1 und der Opt-in von X3; **S12b** die Benchmarks — X4 | **`lyric5 bench` läuft die std-Benchmarks** |
| **S13** Melde-Qualität | der Sammelslice der Durchsicht vom 2026-10-08: `1.` und `.5`, ein Name außerhalb von ASCII, nach `LYR-SEM0126`, `lyric5 test --target` | je Meldung ein Fall |
| **S14** Abschluss | **Wortzähler über Unicode-Text** als Beispiel; Dogfood; STATUS, CHANGELOG | die Zeile oben erfüllt |

**Nachtrag 2026-10-09 — nach der Durchsicht.** Die Durchsicht der offenen Punkte von M8c (oben,
„Durchsicht 2026-10-09“) gab neun Punkten die Uhr M8c; sie folgen S14 in fünf Slices, dazu S20 aus der Rückfrage danach, vor M9a
(vor dem wird weiter gefragt):

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S15** `is` auf eingebauten Typen | 03 T10/T11, Zeile M8c S8b der Durchsicht: `is`, Typ-Pattern und Typmengen auf `bool`, `char`, den zehn Zahltypen, `string`, `StringView`, exakt; `Any` nimmt ihre Werte | `let a: Any = 5; a is int` |
| **S16** `Debug` und `Display` | 10, Zeilen S1 und S11: `?T :: [Debug]`; `T[N] :: [Display, Debug]` wie `T[]` | `assertEq('7'.toDigit(10), 7)`, `assertEq(x.toBytesLE(), …)` |
| **S17** Synthese und `never` | 04, Zeile S3b: ein `T[N]`-Feld als Ganzes; 05, Zeile S5: `never` als Typargument unter `Error` | die Goldens ohne 4096 `&&`; `DedupIter<I, never>` |
| **S18** Index über den View, Meldungen | 03, Zeile S6: `list[a..b]`; 04, Zeile S13: `LYR-SEM0126` an Feldern, Elementen und Typargumenten; 08, Zeile S12a: die leere nachgestellte Lambda | je Regel ein Fall |
| **S20** `never` als Default | 05, Zeile S4 (S20) — die Antwort auf den offenen Punkt nach S15–S19: `E :: [Error] = never` an den neun Adaptern, `never` als Default eines Typparameters unter `Error` | `MapIter<SliceIter<int>, int>` |
| **S19** Gate je Profil | 11, Zeile CI: die Matrix `debug`/`release` (keine Einstellung: `main` verlangt keinen Check beim Namen); als eigener PR von `main` aus vor S15–S18 geliefert | die Wandzeit des Gates halbiert |

**Nicht in M8c**: `std.regex` (10 S8) — M10, der die Tabellen braucht; Normalisierung, Grapheme
und Breite gehören dem Ring `unicode`, nicht `std` (S4).

### Tests

Je Slice die Regel-Fälle der Spec, rot gegen den Compiler davor; die std-Tests unter `tests/std`;
eine Mutationskontrolle je Regel, die rot werden muss. Unicode zusätzlich: jeder Skalarwert gegen
eine Lesung der UCD-Dateien im C#-Test, die nicht der Generator ist.

### Umfang

Laufzeit in C wenig (die Tabellen erzeugt, die Nachschläge etwa 50 Zeilen, `crc32c`), std in Lyric
etwa 3000 Zeilen, der Compiler mittel (S3, S9, S10, S13; die Senkung der Builder in S8). Groß: S4,
S5, S9, S12; mittel: S1, S2, S3, S6, S7, S8, S10; klein: S11, S13, S14.

### Reflexion

- **Rust**: `core::unicode` aus Python-erzeugten Tabellen; ein Iterator-Adapter als Typ je Adapter
  (P2 bleibt dabei); `escape_debug` (S2).
- **Go**: das Paket `unicode` mit Bereichstabellen aus `maketables`; `isUpper` als Kategorie Lu.
- **Swift**: `rethrows` — dieselbe Absicht wie P2, als Schlüsselwort statt Typparameter.
- **Kotlin**: `CharRange` (S3), Mengenoperationen als Methoden (S6).

## Nachtrag 2026-10-09 — der Plan für M9a

M9a nach seiner Zeile oben, dazu jede Uhr, die auf M9a steht:

- in diesem Dokument die M4-Synthese nach `std.core` (M4-Zeile), `inline fn` und lokale `fn`, `@Shared`;
- 04 S3b (`T[N]` als Ganzes);
- 08 Y1 (`comptime`, `quote` reserviert);
- STATUS (`assert` baut seine Meldung bis `inline` eifrig);
- `std.io` (`IoErrorKind` wird `@NonExhaustive`);
- Spec 01 (`inline`, `macro`, `quote`), 11 und 12 (`@NonExhaustive` an den I/O-Arten).

Freigegeben am 2026-10-09 („wie M8c“): ohne Halt bis zum Abschluss, Merge bei grüner CI, offene Punkte gesammelt, Bericht am Ende. Vor M9b wird neu gefragt.

Grundlage ist eine Prüfung jedes Punkts gegen den Code (2026-10-09). Von M9a steht fast nichts:

- `comptime e` wird geprüft und nie ausgewertet: `SourceCompiler` reicht keinen Auswerter durch, die Stelle wird als Laufzeitausdruck gesenkt.
- Die Attribute laufen auf dem 4.x-Zeilenmodell.
- Die Synthese ist das M4-Provisorium im Resolver (Quelltext, Interfaces am bloßen Namen).
- Es gibt keinen Interpreter, kein `std.meta`, kein `embed`, kein `@When`, kein `inline fn`; von Art 2 nur `@Test`, `@Bench`, `@callerExpr`.

Die Slices heißen **M9a S1–S12**; jeder ist ein PR, sein Regel-PR in der Spec geht voran. Größe: **XL** statt L (die Zeile schätzte, bevor die Prüfung zeigte, wie wenig steht).

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Der Interpreter ist C#, im Frontend (`Ir/Interp/`), über der typisierten IR** (L7, die Tür hier gebaut). Mit **expliziten Rahmen** (kein Rekursionsstapel in C#), sodass auch ein Generator zur Compile-Zeit läuft. Werte: Skalare roh, Strings als Bytes, Structs/Enums/Tupel als Feldfolgen, Objekte und Arrays auf dem Heap des .NET-GC. Fehlerslot und Panik wie zur Laufzeit; ein **Budget zählt IR-Instruktionen** (Q5). | Die IR ist selbstbeschreibend gebaut (M2), damit genau das geht. **Verworfen:** das Programm mit dem C-Backend bauen und laufen lassen — ein C-Compiler-Lauf je Auswertung, das Ergebnis hinge an der Plattform (Q7), und ein Cross-Build bräuchte den Zielrechner. **Verworfen:** die 4.x-VM, die mit M2 S0 ging. |
| P2 | **Die reinen Eingebauten als C#-Gegenstück, bitgleich.** Das gilt für die rund 150 Intrinsics: Zahl ↔ Text, die kürzeste Gleitkommaform, Strings, Unicode — der UCD-Generator schreibt die Tabellen zusätzlich als C#, die CI prüft beide. Die **unreinen** (Konsole, Dateien, Netz, Zeit, Zufall, Threads, Tasks, Prozesse, Signale) sind an der Stelle ein Fehler mit der Aufrufkette (Q3). Der **SipHash-Schlüssel ist zur Compile-Zeit fest** (Q7: eine `Map` läuft jedes Mal gleich). | Ein Aufruf aus `comptime` landet über std in denselben Intrinsics wie zur Laufzeit. **Verworfen:** die C-Laufzeit per P/Invoke — nicht für jedes Host-Ziel gebaut, und ein Fehler darin wäre ein Absturz des Compilers. |
| P3 | **Ergebnisse jeder Werttyp** (Q1): neue IR-Konstanten für Arrays, Structs, Enums, Tupel, Optionals, `Box<T>`, als statische Daten im C. Ein Array ist schreibbar: jede Auswertung des Ausdrucks baut eins aus den Daten neu, wie ein Literal. Ein Wert ist eine Kopie. Klassen, Closures und Koroutinen sind kein Ergebnis (`LYR-CT…`). | Zigs Regel; die Tabelle (CRC, Unicode-Teilmengen) ist der Fall, der zählt. **Verworfen:** Arrays nur lesend (`const`) — ein `let t = comptime table()` darf geschrieben werden wie jedes Array. |
| P4 | **Die Formen** (Q2): `comptime { … }`, `comptime if` — beide Zweige geprüft, einer gesenkt —, `comptime for (x in w)` über einen `comptime`-Wert, entfaltet. Schlüsselwörter **`comptime`, `inline`, `quote`, `macro` reserviert** (08 Y1, Spec 01). `quote` und `macro` bekommen ihre Form erst mit M9b; der Parametername `quote` in `std.core` weicht. | Die Liste der Wörter steht seit Bereich 8; ein Wort, das erst in M9b reserviert würde, bräche Programme dazwischen. |
| P5 | **Typinformation als Entfaltung** (A7): `fields(T)`, `variants(E)`, `members(T)`, `conformances(T)`, `typeName<T>()`, `isStruct/isClass/isEnum` geben `comptime`-Werte (`FieldInfo` mit Name, Typ, Sichtbarkeit, Attributen, Index). Ein `comptime for` über sie und `this.[f]` werden **je Instanz entfaltet und geprüft**: eine Stufe zwischen Checker und Senkung, die den Rumpf je Konformer mit den Infos als Konstanten prüft. R7: ein fremdes Paket sieht nur `pub`. | Der Rumpf eines generischen Defaults wird heute einmal geprüft. `this.[f]` hat aber je Feld einen anderen Typ, also muss die Prüfung je Entfaltung laufen (Zigs `inline for`, Nims `fieldPairs`). **Verworfen:** `this.[f]` zur Laufzeit — das wäre Reflexion, die R6 verbietet. **Verworfen:** Makros dafür — die Trennregel in A1 gibt Rumpfcode über Typinfo `comptime`. |
| P6 | **Synthese nach A5:** `Equatable`, `Hashable`, `Ordered`, `Clone`, `Default` als `comptime`-Defaults in `std.core`, geschrieben über P5; das Provisorium fällt für sie. **Lesart:** `TotalOrder` geht mit `Ordered` (dieselbe Familie; A5 nennt fünf). `Debug` und `Identity` bleiben im Compiler (A5), `Display` auf Anfrage mit `Debug`. Die Blöcke je Länge für `T[N]` (S3b, S16) bleiben — es gibt keine generische Konstante (T13). | Spec 05 §14 ist die Messlatte, ihre Fälle bleiben grün. Regel 1 („die Liste ist fest“) fällt: jedes Interface kann synthetisieren. |
| P7 | **Attribute:** die Art je Attribut festgehalten (T1); die Marker aus F1 vollständig, mit Hierarchie; Attribute an allen Deklarationen (T7). Werte sind `comptime`-Ausdrücke (F4), mit **einem Weg** für Checker, Runner und Warnungen. Der latente Fehler: `@Test { timeout = LIMIT }` lief mit 60, weil der Runner nur Literale liest. **Positionsform** (Lesart): ein Attribut mit genau einem Feld ohne Default nimmt `@X(v)`, statt `WithArg<T>` aus 4.x. Das 4.x-Zeilenmodell (Prüfungen, Texte über das Format) gilt nur noch im 4.x-Pfad. In Lyric 5 ist eine attributierte Funktion keine Wurzel (01 B13), außer `@Test`/`@Bench` unter ihrem Befehl. | A1–A3 sind entschieden; gebaut war 4.x. |
| P8 | **Art 2 vollständig** (A11): `@Deprecated { message, until, replacement }` — `until` gegen die 5er-Version —, `@AlwaysInline`/`@NoInline`/`@Cold` als C-Attribute, `@MustUse` an Funktionen und Typen (die Sperre `SEM0067` gegen generische Ziele fällt für Art 2), `@NonExhaustive` (an `IoErrorKind`), `@callerLine`/`@callerFile`, `@When`, `@Allow(code)` mit `[lints] allow` im Manifest, `@Shared`. **Nicht hier:** `@Export` und `@Layout` gehören M14 (Spec 13, die ABI); die M9a-Zeile oben nannte `@Layout` doppelt. | Ein Name, eine Art; jede Anweisung bekommt ihren Vertrag in Spec 11 und einen roten und einen grünen Fall (11 G10). |
| P9 | **Ziel und Profil im Frontend** (A6): `target.os`, `target.arch`, `target.pointerWidth`, `profile.debug` sind `comptime`-Konstanten aus dem Bau, beim Cross-Build die des Ziels. `@When(cond)` an Deklarationen und am Modulkopf; der inaktive Zweig wird geprüft, soweit das Ziel es erlaubt. Features (07 P9) bleiben Tür. | B1–B4. |
| P10 | **`inline fn`** (Y11 F6): in der IR eingesetzt, ihre Lambda-Argumente an Ort und Stelle, mit nicht-lokalem `return`. **Lokale `fn`** (F8/F9) mit Captures und Rekursion, ohne Defaults und `...`. `assert` wird `inline`: seine Meldung entsteht erst im Fehlerfall. | Kotlins `inline`; die Uhr aus M3. |
| P11 | **Kosten:** ausgewertet wird nur, wo `comptime` steht; ein Programm ohne Stelle senkt einmal wie bisher. Mit Stellen senkt eine Hebe-Senkung nur, was die Stellen erreichen, ohne Optimierer, und die Ausgabe-Senkung schreibt die Werte ein. Messpunkt 1 (hello warm ≤ 50 ms) gilt weiter; gemessen wird er in S12, wenn std selbst `comptime` nutzt. *Korrigiert mit S1: hier stand „einmal gesenkt und für die Ausgabe wiederverwendet“.* | Einmal zu senken hieße, den Optimierer erst nach der Auswertung laufen zu lassen und die Stellen in der fertigen IR zu ersetzen. Das wäre eine Umstellung der Senkung, die keine Messung verlangt: die Hebe-Senkung kostet nur, was die Stellen erreichen. |
| P12 | **Spec:** Kapitel 11 wächst um `comptime`, Attribute, `std.meta`, `@When` und `embed`; 05 §14 neu nach A5; 01 die Wörter; 16 die Codes `LYR-CT…`. | wie M8c P4/P5 |
| P13 | **`lyric check` wertet aus** (Q8): heute gibt es keinen `check`-Befehl (M12). Jeder Bau, der senkt, wertet aus, und der Satz in `ComptimeTable` („kein Auswerten im Check“) fällt. Der Editor (Q8 „strenger“) gehört M13b. | |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** Der Interpreter | P1, P2 für Skalare und Strings: Kontrollfluss, Aufrufe, Structs, Enums, Tupel, Optionals, Arrays, Objekte, Fehler; eine Panik ist ein Übersetzungsfehler mit innerem Backtrace (Q6); Budget mit Manifest-Feld (Q5); unreine Eingebaute als Fehler mit Kette; `comptime e` wird ausgewertet | `comptime fib(30)` ist eine Konstante, `fib` fehlt im Binary; eine C#-Testreihe des Interpreters über IR-Stücke |
| **S2** Ergebnisse | P3 | `let t = comptime crcTable(0xEDB88320u32)`, ein `uint32[]` in den Daten |
| **S3** Formen und Wörter | P4 | Block, `comptime if` mit Fehler im inaktiven Zweig, `comptime for` über ein Array |
| **S4** Determinismus und `embed` | P2-Rest: Unicode als C#-Tabellen, SipHash fest; `embed("pfad")` (Q4), Bauabhängigkeit im Cache | eine Datei als `uint8[]`, ihr Wechsel baut neu |
| **S5** Attribute | P7 | `@Test { timeout = LIMIT }` läuft mit `LIMIT`; ein Attribut an jeder Deklarationsart; die Marker |
| **S6** Art 2 | P8 | je Attribut ein roter und ein grüner Fall |
| **S7** Ziel und Profil | P9 mit `@When` | ein Modul je Plattform über `@When` am Kopf; ein Cross-Build wählt das Ziel |
| **S8** `inline fn`, lokale `fn` | P10; `@Shared` beim `Clone` | `forEach { if (…) return }` verlässt die Funktion |
| **S9** `std.meta` | P5 mit R7 | `comptime for (f in fields(Self))` mit `this.[f]` in einer eigenen Synthese (`ToJson`-artig) |
| **S10** Synthese nach A5 | P6 | Spec 05 §14 grün, das Provisorium für die fünf gelöscht |
| **S11** Enum-Reflexion | R5: `E.variants()`, `E.fromName(s): ?E`, `e.name()` über ein `std.core`-Interface, das ein Enum auf Anfrage konformiert. Der Name des Interfaces ist eine Lesart | `match`-freie Namenstabelle eines Enums |
| **S12** Abschluss | `examples/constants.lyr` für Lyric 5; das `comptime`-Tabellen-Beispiel: `std.hash`s CRC-Tabellen über `comptime` statt SpinLock (M8c S11); Dogfood; Messpunkt 1; STATUS, CHANGELOG | die Zeile oben erfüllt |
| **S11b** `std.math` frei | 10 Z2 neu (Maintainer 2026-10-12): die Elementarfunktionen als freie Funktionen über `Float`, `abs`/`signum` über `Signed`; libm's Externs ziehen mit | die ersten Konformanzfälle, die eine Wurzel oder einen Winkel rechnen (#455) |
| **S11c** `std.core`-Audit | 10 Review 2026-10-13: die Adapter in `std.iter` (vom Prelude ohne Namen importiert), `std.core` nach Sachgebieten mit Inhaltsverzeichnis, 6363 → 5275 Zeilen; was bleibt, bleibt mit Grund | hello-Bau 1554 → 1555 ms (gleich), C byte-gleich (#456) |
| **S12b** D1, D8 | `assert(c) { … }` als `inline fn` mit Lambda-Meldung; Modulbindungs-Stellen nur auswerten, wo das Programm die Bindung erreicht; ein `inline`-Rumpf trägt die Zeile des Aufrufs | hello-Bau 1530 → 1443 ms (#457) |

**Nicht in M9a:**

- Makros, `std.syntax`, `quote`-Form, `lyric expand` — M9b;
- `@Export`, `@Layout` — M14;
- die Lint-Namen und `lyric check` — M12;
- der Editor — M13b;
- Doc-Tests — M13a;
- Features — Tür.

### Tests

- Je Slice die Regel-Fälle der Spec, rot gegen den Compiler davor, dazu **die ganze Suite** vor dem Ausliefern (Lehre aus #402).
- Der Interpreter bekommt eine C#-Testreihe über IR-Stücke, je Konstrukt und je Eingebautem, mit dem Wert gegen den des C-Laufs desselben Programms: bitgleich, sonst rot.
- Eine Mutationskontrolle je Regel. Die 4.x-Attributtests (Sema 73, Parsing 17) bleiben grün; ihr Pfad wird abgegrenzt, nicht gelöscht.

### Umfang

| | Umfang |
|---|---|
| Interpreter (C#) | etwa 3000 Zeilen |
| Entfaltung | etwa 1500 |
| Attribute | etwa 1500 |
| `std.meta` und die Synthese in Lyric | etwa 800 |
| Spec | etwa 600 Zeilen |

Nach Aufwand: groß S1, S5, S9, S10; mittel S2, S3, S6, S8; klein S4, S7, S11, S12.

### Reflexion

- **Zig**: `comptime` im Compiler interpretiert, `@typeInfo` und `inline for` (P5), `@embedFile` (S4), Determinismus über Plattformen (P2).
- **Rust**: `const`-Auswertung über einen Interpreter der mittleren Repräsentation (Miri), mit dem Rahmenstapel bei einer Panik (Q6); `derive` neben dem Trait, das A5 nicht übernimmt.
- **Nim**: `static:`/`when` und `fieldPairs` — die Entfaltung je Instanz.
- **Kotlin**: `inline` mit nicht-lokalem `return` (P10).
- **Swift**: `CaseIterable` (S11).

## Nachtrag 2026-10-13 — der Plan für M9b

M9b nach seiner Zeile oben (09 A8 Sprosse 5, 08 Y12 Q1–Q7, 10 B13 M2–M3, 11 G12 und Q7), dazu jede Uhr, die auf M9b
steht: `Default` nach `std.core` (braucht `f.type` in Typposition — 09 Review 2026-10-12, S10-Zeile), `comptime for`
über Tupelelemente (10 Review 2026-10-13 A3), die std-Selbsttests ins Arbeitsverzeichnis (Punkt 104).

Grundlage ist eine Prüfung jedes Punkts gegen den Code (2026-10-13). Von M9b steht:

- die Wörter `macro` und `quote` sind reserviert (M9a S3), sonst nichts von ihnen: kein Parser, kein Knoten;
- der Interpreter läuft über der typisierten IR mit expliziten Rahmen, Budget und Panik-Kette (S1), und die
  Hebe-Senkung senkt, was Wurzeln erreichen (S1, S12b) — die Maschine, auf der ein Makro läuft;
- `Box<T>`-, Struct-, Enum- und Array-Werte sind `comptime`-Ergebnisse (S2) — die Form, die ein Syntaxbaum als Wert
  braucht (10 M2: Enums mit `Box`-Nutzlast);
- `std.meta` gibt Typinformation als Konstanten je Instanz (S9), die Attribute der Arten 1 und 2 sind Werte des Baus
  (S5, S6); Art 3 gibt es nicht;
- die Diagnose-Engine kennt `Within` (Noten-Ketten, Hauptspan, Recode — S9, S10), die Grundlage für G12.

Die Slices heißen **M9b S1–S6**; jeder ist ein PR, sein Regel-PR in der Spec geht voran. Größe: **L** wie die Zeile.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **`std.syntax` ist eine Lyric-Bibliothek** (10 M2): der Syntaxbaum als Enums und Structs mit `Box`-Nutzlast — `Expr`, `Stmt`, `Block { stmts }`, `Decl` mit seinen Arten (`FnDecl`, `StructDecl`, `ClassDecl`, `EnumDecl`, `InterfaceDecl`, `ExtendDecl`, `LetDecl` …), `Type`, `Pattern`, `Ident { name, span }`, `Literal`, `Param`, `Attribute`, `Span { file, line, column }`; jeder Knoten mit `span`, `Debug` und `toSource()`; `children()`, `walk(f)`, `map(f)`; `Ident.fresh(hint)`; `parseExpr(s)`, `parseStmts(s)`, `parseDecls(s)` für DSL-Strings (Q5); `error(node, msg): never`, `warn`, `note`. **Gespiegelt wird, was ein Programm schreiben kann** — die Formen der Spec, nicht die inneren Knoten des Compilers (`FieldByInfoExpr`, `ErrorExpr` …); die Enums sind `@NonExhaustive` (M3), ein Makro-`match` braucht `_`. | Nim `macros`, Rust `syn` — ohne Token-Strom (A8 T1). Alternative, den C#-Baum direkt zu reichen: es gibt keine Fremdobjekte im Interpreter (Q1), und die Bibliothek ist die Spec der Formen. |
| P2 | **Die Syntaxbrücke** (C#, `Sema/Syntax/`): der Compiler baut aus seinem Baum die `std.syntax`-Werte des Interpreters und liest Werte zu Knoten zurück — eine Tabelle je Knotenart in beide Richtungen, geprüft durch einen Rundlauf: Baum → Werte → Baum → Senkung, dasselbe C. Ein zurückgelesener Knoten ohne Span (aus `quote` gebaut) bekommt den Span der Aufrufstelle, die Herkunft (`quote`-Zeile) als Note (G12). | Die Brücke ist der große Posten; sie ist mechanisch und wird mit der Spiegelung in P1 in einem Slice gebaut, damit beide dieselbe Liste haben. |
| P3 | **`macro` ist eine `comptime`-Funktion über `std.syntax`** (Q1): `macro retry(n: Expr, body: Block): Expr { … }` — geprüft wie eine Funktion, gesenkt wie eine `comptime`-Stelle, im Interpreter gelaufen unter dessen Regeln (09 Q9: rein, Budget, Panik = Fehler mit Kette). Rückgabe ein AST-Typ, `Stmt[]` oder `Decl[]`. Ein Makro ist eine Top-Level-Deklaration im Namensraum des Moduls (07 K3), `pub` und importierbar wie eine Funktion, nie ein Wert. | Nim `macro`; Swift `macro` mit externer Implementierung verworfen (zweiter Übersetzungsweg). |
| P4 | **Die drei Aufrufformen** (Q4, Q5): `name!(args)`, `name!(args) { … }`, `name! { … }` — `!` unmittelbar nach dem Namen vor `(` oder `{` ist die Expansion (sonst das Unwrap-Postfix; der Parser sieht ein Token voraus); jedes Argument ist Lyric-Syntax und kommt dem Makro als Wert seines Parametertyps (`Expr`, `Block`, `Ident`, `Literal`, `Type`, `Stmt[]`), ein nachgestellter Block als `Block`. **Attribut-Makro** (Art 3, F2, T4): `@Builder struct P { … }`, `@Retry(3) fn f() { … }` — ein `macro`, dessen erster Parameter ein Deklarationstyp ist (der Typ ist der Zielmarker: `StructDecl` sitzt an Structs); die Deklaration geht als Wert hinein, das Ergebnis (`Decl` oder `Decl[]`) ersetzt sie: T5 — ein Attribut ändert nie Code, der es nicht trägt. Kein `!` am Attribut (T4). | Rust `#[attr]`/`name!`, Swift attached/freestanding. Verworfen: `name!` als Token-Strom (T1); DSLs über neue Grammatik (A8 Grenze). |
| P5 | **`quote { … }` und `#{…}`** (Q2, Q3): `quote` ist ein Ausdruck vom AST-Typ seines Inhalts — ein Ausdruck gibt `Expr`, Anweisungen `Stmt[]` oder `Block`, Deklarationen `Decl` oder `Decl[]`; wo der Inhalt mehrdeutig ist, entscheidet der erwartete Typ. **Hygienisch**: ein Name, den das `quote` selbst bindet (`let`, Parameter, Schleifenvariable), wird je Expansion frisch (`Ident.fresh`), ein Name aus `#{…}` gehört dem Aufrufer, ein freier Name im `quote` wird im Modul des Makros aufgelöst (Julias `esc`-Grenze in der anderen Richtung: Einfügen ist die Ausnahme, nicht das Entkommen). `#{e}` setzt einen AST-Wert, ein `Ident` oder ein Literal ein; ein Array von Knoten wird gespleißt; in Bezeichnerposition (`fn #{name}()`) baut es Namen. Im Parser ist `#{` nur innerhalb von `quote` ein Token. | Elixir `quote`/`unquote`, Nim `quote do`/backticks, Julia-Hygiene. Verworfen: unhygienisch mit `gensym` von Hand (Lisp) — der Fehler, den jeder macht. |
| P6 | **Die Expansion ist eine Phase je Modul, in Importreihenfolge**: ein Makro wird in den Modulen expandiert, die es importieren; das Modul, das es schreibt, prüft es zuerst (`LYR-SEM…`: ein Makro in seinem eigenen Modul aufzurufen ist abgelehnt — Rusts Regel, hier je Modul statt je Crate). Der Ablauf: Parsen aller Module; dann je Modul in Importreihenfolge — (1) seine Makrostellen (`!`-Aufrufe, Art-3-Attribute) finden, (2) die Makros der importierten Module samt dem, was sie erreichen, mit der Hebe-Senkung senken (einmal je Makro-Modul, gecacht), (3) jede Stelle im Interpreter expandieren, äußere vor inneren, mit Tiefenlimit aus dem Budget, (4) das Ergebnis durch die Brücke in den Baum des Moduls setzen, (5) das Modul prüfen wie geschrieben. Ohne Makrostelle kostet ein Bau nichts (P11 von M9a gilt weiter). | Nim verschränkt Sem und VM je Deklaration — mächtiger (Typen des Arguments), aber der Checker müsste unterbrechbar werden; Rust staged je Crate. Je Modul ist die kleinste Stufe, die der Modulgraph (07 K-Reihe, ohne Zyklen) schon hergibt. |
| P7 | **Diagnosen** (G12, Q6): `error(node, msg)` aus dem Makro ist ein Fehler mit Hauptspan am Knoten (ein Argument der Aufrufstelle) und einer Note am `!`; ein Fehler im **expandierten** Code hat den Hauptspan an der Aufrufstelle und die Noten „in der Expansion von `retry!`“ und die `quote`-Zeile; eine Panik oder ein Budget im Makro wie bei `comptime` (`LYR-CT0001/2`, mit Kette). Die Spans der Argumente bleiben die geschriebenen (P2). | rustc: Makro-Backtrace als Noten. |
| P8 | **`lyric expand <datei>`** (Q7, 11): schreibt das Modul mit expandierten Makros als Quelltext (`toSource`) auf stdout — `#line`-los, lesbar, derselbe Formatierer, der `toSource` schreibt. Der Editor (E2) kommt mit M13b. | `cargo expand`. |
| P9 | **Typen auf Anfrage: nur, was das Makro selbst nennt.** `std.meta` läuft im Makro wie in jeder `comptime`-Funktion — `fields<app.P>()` über einen Typ, den der Makro-Rumpf schreibt. `typeOf(expr)` über ein **Argument** gibt es nicht: die Expansion läuft vor der Prüfung des Aufrufers (P6), ein Argument hat noch keinen Typ. A8 sagt „auf Anfrage Typen“; die Tür ist Nims Verschränkung — festgehalten als Tür, nicht gebaut. Was `@Builder` braucht, sind die geschriebenen Feldtypen der `StructDecl`: Syntax. | Rust-proc-macros haben es auch nicht; Nim hat es um den Preis des verschränkten Compilers. |
| P10 | **Spec:** 11 §5 Makros (neu: Deklaration, Aufrufformen, `quote`/`#{}`, Hygiene, Staging je Modul, Diagnosen), 08 §1/§2 die Formen (`name!`, `quote`, `#{}`), 01 die Wörter (`macro`, `quote` erhalten ihre Form), 12 `std.syntax`, 14 `lyric expand`, 16 die neuen Codes. | wie M9a P12 |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** `std.syntax` und die Brücke | P1, P2: die Bibliothek, die Brücke in beide Richtungen, `toSource` | ein Rundlauf über jedes Programm der `CEmitterTests`: Baum → Werte → Baum → dasselbe C; `toSource` eines Falls gleich dem Formatierer |
| **S2** `macro` und `name!(…)` | P3, P4 (Ausdrucks- und Anweisungsform), P6, P7 ohne `quote`: der Rumpf baut Knoten über die Konstruktoren | `twice!(x)` als `Expr`-Rückgabe; ein Makro aus einem importierten Modul; der eigene Modulaufruf abgelehnt; ein `error(node, …)` mit beiden Spans |
| **S3** `quote` und `#{}` | P5 mit Hygiene | **`retry!(3) { try fetch(url) }`** (die Zeile aus 08 Y12); ein `let` im `quote` kollidiert nicht mit dem des Aufrufers; `#{names}` gespleißt; `fn #{name}()` |
| **S4** Attribut-Makros | P4 Art 3 an jeder Deklarationsart, die ein Marker-Typ nennt; `Decl[]` als Ersatz | **`@Builder struct P { … }`** erzeugt `PBuilder`; `@Retry(3) fn f()` umwickelt; T5 geprüft |
| **S5** Diagnosen und `lyric expand` | P7 vollständig (Fehler im expandierten Code), P8 | `lyric expand` über `examples/macros`; ein Fehler in expandiertem Code zeigt Aufruf, Makro und `quote`-Zeile |
| **S6** Abschluss | `examples/macros` für Lyric 5 (das 4.x-`comptime.lyr` wird S12as `constants.lyr`; dazu `retry!`, `@Builder`, ein DSL-Makro über `parseExpr`); Punkt 104 (die std-Selbsttests schreiben nach `tempDir()`); die Türen `Default` nach `std.core` und `comptime for` über Tupel geprüft: was `std.syntax`/`Type` in Typposition jetzt hergibt, wird gebaut, der Rest bleibt Tür; Dogfood; Messpunkt 1; STATUS, CHANGELOG | die Zeile oben erfüllt |

**Nicht in M9b:**

- `typeOf(expr)` über ein Makro-Argument (P9) — Tür;
- die Expansion im Editor — M13b;
- `@Export`, `@Layout` — M14;
- Makros, die neue Grammatik definieren — nie (A8).

### Tests

- Je Slice die Regel-Fälle der Spec, rot gegen den Compiler davor, dazu die ganze Suite vor dem Ausliefern.
- Die Brücke bekommt den Rundlauf (S1) als Dauertest in `Lyric5.Tests`: jede Knotenart, die ein Programm schreiben kann, geht hin und zurück.
- Eine Mutationskontrolle je Regel: Hygiene aus (ein `let` des `quote` fängt den Aufrufer), Staging aus (der eigene Modulaufruf läuft), Span-Abbildung aus (der Fehler zeigt ins `quote`).

### Umfang

| | Umfang |
|---|---|
| `std.syntax` (Lyric) | etwa 700 Zeilen |
| Brücke (C#) | etwa 1200 |
| Parser: `macro`, `quote`, `#{}`, `name!` | etwa 400 |
| Expansionsphase, Staging, Hygiene | etwa 1000 |
| Diagnosen, `lyric expand` | etwa 350 |
| Spec | etwa 400 Zeilen, ~25 Fälle |

Nach Aufwand: groß S1, S3; mittel S2, S4; klein S5, S6. Etwa fünf Sessions.

### Reflexion

- **Nim**: Makros über dem typisierten AST in der Compiler-VM — das Modell (A8); Nims Verschränkung von Sem und VM gibt Argumenttypen, kostet den unterbrechbaren Checker — hier die Modul-Stufung (P6, P9).
- **Rust**: proc-macros in eigenen Crates (Staging), `syn` statt Token-Strom für den, der Syntax will; die Backtrace-Noten (P7).
- **Elixir/Julia**: `quote`/`unquote` und Hygiene als Standard, Entkommen als Ausnahme (P5).
- **Swift**: attached und freestanding macros = Art 3 und die `!`-Formen; der externe Makro-Prozess verworfen (ein Interpreter steht schon).
- **Racket**: die erweiterbare Grammatik, bewusst nicht (A8 Grenze).

---

**Damit ist die Runde vollständig** (Bereiche 0–12 entschieden, 2026-09-28/29). Die Dokumente
`design/v5/spec/00–13` sind die Grundlage der Spec 5.0 (R4) und der Arbeitsplan (M0–M18).

## Nachtrag 2026-10-14 — der Plan für M10

M10 nach seiner Zeile oben (10 B10 E1–E9, B11 Q1 Zonen und UTS #35, Q6 `http`, Q7 `term`, Q8 `compress`, B1 `uri`
und `regex`), dazu jede Uhr, die auf M10 steht: Base64 ohne Padding (10 S4a, mit `http`/`uri`), Fristen für `resolve`
(10 S10c, mit `http`), die Pipes eines Kindes unter Windows über den Completion-Port (10 S12b), der libm-Port für
Compile-Zeit und Laufzeit (09 D7, 13 Durchsicht 2026-10-12), die Synthese von `Display` für Enums (03 Zusage an
Bereich 4/8 — `Equatable`/`Hashable` stehen seit M9a, `Display` wird geprüft), `Coroutine :: Identity` (06 A7 —
steht seit N2d, die Uhr fällt).

Grundlage ist eine Prüfung jedes Punkts gegen den Code (2026-10-14). Von M10 steht:

- `std.time` mit `Duration`, `Instant`, `Monotonic`, `Date`, `Time`, `DateTime`, `Zone.utc`/`Zone.fixed`, RFC 3339
  (M8b S5) — es fehlen `Zone.load`/`Zone.local` (TZif) und die UTS-#35-Muster;
- `std.encoding` (Base64/Base64Url/Hex/UTF-16, gepaddet und streng), `std.crypto` (Digests, HMAC, `randomBytes`),
  `std.hash`, `std.random` (ChaCha8) — M8b/M8c;
- `std.net` (TCP, UDP, `resolve`, der Windows-Poller über AFD), `std.io` (`Reader`/`Writer` mit `[IoError,
  Cancelled]`, Puffer- und Text-Hüllen, `ByteBuffer`), `std.process`, `std.os.signals` — M8b: alles, worauf ein
  HTTP/1.1-Client und -Server stehen;
- die Unicode-Tabellen und `char`-Prädikate (M8c S1/S2) — was `regex` für seine Klassen braucht;
- die Synthese nach A5 über `comptime for (f in fields<Self>())` mit `attribute<A>(f)` (M9a S9–S10) — die Maschine,
  mit der `Codable` sich selbst schreibt; Art-1-Attribute als Werte des Baus (`@Codec`);
- `std.math`s freie Elementarfunktionen (M9a S11) über libm — zur Compile-Zeit `LYR-CT0005`;
- kein `codec`, `json`, `toml`, `uri`, `regex`, `http`, `term`, `compress`: nichts davon existiert, auch nicht als
  Stub.

Die Slices heißen **M10 S1–S13**; jeder ist ein PR, sein Regel-PR in der Spec (12 §je Modul, 16 die Codes) geht
voran. Größe: **XL** wie die Zeile — die Reihenfolge ist so gewählt, dass das Artefakt der Zeile („HTTP-Server liefert
JSON, Client holt es; Log-Filter mit Regex") mit S8 steht und der Rest es verfeinert.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **`std.codec` ist das Paar, nicht ein Format** (E1, E2): `Encode { fn encode(&e: Encoder) }`, `Decode { static fn decode(&d: Decoder): Self }`, `Codable`; `Encoder`/`Decoder` als Interfaces mit dem Container-Modell (`encodeSeq(len): SeqEncoder`, `encodeStruct(name, n): StructEncoder`, `decodeStruct(name, fields): StructDecoder` …), zehn Formen; die Fehler `EncodeError { path, detail }`, `DecodeError { kind, path, detail }`. Die Skalare, `?T`, `T[]`, `List`, `Map` (Schlüssel `string`/`Integer`), Tupel bis 8, `Instant` konformieren in `std.codec` (nicht in `core`: die Trägerklausel hält `core` frei von Formaten). | Swift `Codable`, Rust `serde` ohne Visitor-Doppeldispatch (E2). Verworfen: `ToJson`/`FromJson` je Format (4.x). |
| P2 | **`Codable` schreibt sich selbst** (E3, A5): die Defaults von `encode`/`decode` laufen `comptime for (f in fields<Self>())` in Deklarationsreihenfolge, lesen `attribute<Codec>(f)` (E4: `name`, `skip`, `default`) und das Typ-Attribut (`rename`, `denyUnknown`, `tag`); Enums: Unit-Varianten als String, Nutzlast extern getaggt, `@Codec { tag = "kind" }` intern. Eine eigene `encode`/`decode` ersetzt die Synthese (05 §14). **Der Interpreter trägt die Synthese**, nicht der Compiler: kein neues Provisorium. | serde-Attribute; Swifts `CodingKeys` verworfen (E4). Die Synthese ist der Test der A5-Maschine über einen echten Vertrag — findet sie Lücken, sind die Lücken M10s. |
| P3 | **`std.json` und `std.toml` sind je ein `Encoder`/`Decoder` plus ein dynamischer Baum** (E7, E8): `json.encode<T :: [Encode]>(v, opts)`, `encodeTo(v, w)`, `decode<T :: [Decode]>(text)`, `decodeFrom<T>(r)`; `JsonValue` als rekursives Enum, selbst `Codable`, mit `[]`-Index zu `?JsonValue`, `path("a.b[0]")`, kompaktes `Display`; strikt RFC 8259 (JSON5 Tür). `TomlValue` mit `Datetime`, TOML 1.0 vollständig (Tabellen, Arrays von Tabellen, Inline-Tabellen, Mehrzeilenstrings, die vier Datumsformen). Der Manifest-Leser der Toolchain bleibt C# (11 P1): `std.toml` ist für Programme; ein Selbsttest liest das eigene `lyric.toml`. | Rust `serde_json::Value`, `toml`; Go `encoding/json`. Verworfen: ein Parser je Format ohne das Paar (SL-14). |
| P4 | **`std.uri` nach RFC 3986** vollständig: `Uri.parse`, die fünf Teile, `resolve(base, ref)` (§5.2), `normalize` (§6), Prozentkodierung je Komponente, `Query` als geordnete Paare, `Display`/`Parse`/`Codable`, IPv6-Host. **Base64 ohne Padding** (S4a) landet hier als `Base64Url.encodeUnpadded`/`decodeUnpadded` (JOSE braucht es; die gepaddeten Formen bleiben streng). | WHATWG-URL verworfen (Browser-Semantik, kein RFC); Go `net/url` als Vorbild. |
| P5 | **`std.regex` mit RE2-Syntax in linearer Zeit** (B1): Parser → NFA (Thompson), **Pike-VM** mit Captures als Grundlage (benannte Gruppen, Alternativen, Zähler `{m,n}` entfaltet, Klassen über die Unicode-Tabellen, `\b`, `^`/`$`, Flags `i m s`); `Regex.new(pattern) throws RegexError { offset }`, `isMatch`, `find`, `findAll` (Iterator), `captures`, `replace`/`replaceAll` (`$1`, `${name}`), `split`; **lazy DFA** für `isMatch`/`find` ohne Captures als S9b, wenn Messpunkt 4 (der Log-Filter) es braucht — sonst Tür. Keine Rückverweise, kein Lookaround (RE2: lineare Zeit ist die Zusage). | RE2, Rusts `regex` (Pike + lazy DFA), Go `regexp`. Verworfen: Backtracking (PCRE — exponentiell), ein `comptime`-kompiliertes Regex-Literal (Tür: `regex!("…")` ist ein Makro-Fall für später). |
| P6 | **`std.compress` als Strom-Adapter** (Q8): Inflate (feste und dynamische Huffman-Tabellen, LZ77-Fenster 32 KiB) und Deflate (Stufen 0–9: gespeichert, feste Tabellen, dynamische mit Lazy-Matching), Zlib (RFC 1950, Adler-32 aus `std.hash`) und Gzip (RFC 1952, CRC-32) als `GzipReader<R :: [Reader]>`/`GzipWriter<W :: [Writer]>` und Zlib-/Deflate-Zwillinge, dazu `gzip.compress(bytes, level)`/`decompress(bytes)`. Ganz in Lyric (L9: keine Systembibliothek — kein zlib). | Go `compress/flate`, Rust `miniz_oxide`; `flate2` als API-Vorbild. |
| P7 | **`std.http` Client und Server über `Reader`+`Writer`** (Q6): `Request`, `Response`, `Headers` (case-insensitiv, geordnet), `Method`, `Status`; HTTP/1.1 vollständig: Chunked, Content-Length, Keep-Alive, `Expect: 100-continue` (Server), `Host`, Redirects (Client, `followRedirects`), Fristen (`timeout` je Anfrage — **hier bekommt `resolve` seine Frist**: S10c, über `std.task.timeout` um den Pool-Aufruf); `Client.get/post/send`, `Response.text()/bytes()/json<T>()` (über `std.json`); `Server.bind(addr).serve(handler)` — ein Task je Verbindung, `Server.shutdown()` über `os.signals`; **TLS über den `Transport`-Haken** (`fn(SocketAddr) -> Stream`), den das Ring-Paket `tls` besetzt; HTTP/2, Cookies, Proxys: Türen. | Go `net/http` (Form), Rust `hyper` (Trennung TLS). Verworfen: eigenes TLS (U2). |
| P8 | **`std.term` nach ECMA-48** (Q7): `isTerminal(stream)`, `size()`, `Style { fg, bg, bold, dim, italic, underline }.apply(s)`, `Color` (16/256/RGB), `cursor`/`clear`/`eraseLine`, `NO_COLOR`/`TERM`/`FORCE_COLOR`; unter Windows wird `ENABLE_VIRTUAL_TERMINAL_PROCESSING` einmal gesetzt (runtime `console.c`), sonst keine Plattformzweige; Raw-Mode Tür. | Rust `crossterm` (Teilmenge), Go `golang.org/x/term`. |
| P9 | **Zeitzonen aus TZif** (Q1): `Zone.load(name)` liest RFC 8536 (v1–v3, inkl. Footer-TZ-String für die Zukunft) aus der System-Datenbank (`/usr/share/zoneinfo`, `TZDIR`, macOS gleich; Windows hat keine → `TimeError { kind = NoZoneData }`, Ring `tzdata`), `Zone.local()` (`TZ`, `/etc/localtime`; Windows: `Zone.utc` plus Hinweis — die Registry-Abbildung ist Tür), `instant.toDateTime(zone)`, `DateTime.toInstant()` mit Lücken-/Überlappungsregel (nach vorn, die frühere), `plusDays` über DST. **UTS #35** als `format(pattern)`/`parse(pattern)` mit den Feldern `y M d H h m s S a E Z X x` und Literalen in `'…'`; Lokalisierung nur `en` (Monats-/Tagesnamen), Tür. | Java `java.time`, Go `time.LoadLocation`; verworfen: Gos Referenz-Layout, ein eingebautes tzdata (Trägerklausel: Pflege). |
| P10 | **Der libm-Port** (09 D7): `exp`, `ln`, `log2`, `log10`, `pow`, `sin`/`cos`/`tan`, `asin`/`acos`/`atan`/`atan2`, `sinh`/`cosh`/`tanh`, `cbrt`, `hypot` als **musl-Port in C** (`runtime/src/libm/`, MIT), den die Laufzeit statt des System-libm bindet — plattformgleich auf Tier 1 —, und als **Zeile-für-Zeile-Port in C#** (`Lyric.Frontend/Ir/Interp/Libm.cs`) für den Interpreter; ein Test läuft beide über denselben Eingabevektor (1 M Werte je Funktion, Bitgleichheit). `sqrt`, `floor`, `ceil`, `round`, `trunc`, `abs` sind IEEE-exakt und bleiben beim Compiler. | musl (klein, korrekt gerundet genug, MIT); CORE-MATH (korrekt gerundet, aber größer) als Tür. Verworfen: `LYR-CT0005` dauerhaft (D7 wollte die Antwort), ein Interpreter, der die C-Laufzeit lädt (zweiter Übersetzungsweg). |
| P11 | **Die Pipes eines Kindes unter Windows parken** (S12b): überlappte Named Pipes (`FILE_FLAG_OVERLAPPED`) über den Completion-Port des Pollers wie Sockets seit M8b S11 — ein Kind, das nie schreibt, hält keinen Pool-Thread, ein Abbruch wirkt sofort. POSIX bleibt wie es ist (Pipes im Poller). | Go `os/exec` (IOCP), Rusts `read2`. |
| P12 | **Messpunkt 4**: HTTP-Durchsatz (Keep-Alive, 1 KiB JSON-Antwort, 64 Verbindungen) gegen Go `net/http` ≤ 2×; JSON-Kodierung/-Dekodierung eines 1-MiB-Dokuments gegen Go `encoding/json`; der Log-Filter (`regex`) gegen Go `regexp` — gemessen mit `bench/` wie Messpunkt 3, berichtet, nicht gegated. | wie 13 Messpunkte. |
| P13 | **Spec:** 12 §Codec/JSON/TOML/URI/Regex/Compress/HTTP/Term, 12 §Time (Zonen, Muster), 12 §Math (Compile-Zeit-Antwort), 16 die Codes (`LYR-RT`/`LYR-SEM` für `@Codec`-Fehler: ein `@Codec` an einem Nicht-Feld, `tag` an einem Struct); je Slice die Fälle rot gegen den Compiler davor. | wie M9b P10 |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** `std.codec` | P1: das Paar, die Container, die Fehler, die Konformanzen der Skalare/Container/`Instant`; ein Test-Encoder in `std.test`-Nähe (`codec.debugEncoder`, der die Formen als Text schreibt) | ein Struct über den Debug-Encoder: Formen in Deklarationsreihenfolge; `?T` und `Map<string, V>` |
| **S2** `Codable`-Synthese, `@Codec` | P2: die Defaults über `fields<Self>()`, das Attribut (Feld/Enum/Typ), extern/intern getaggte Enums, `denyUnknown`, `default` | `struct User :: [Codable]` ohne eine Zeile; `@Codec { name = "user_id" }`; eine eigene `decode` ersetzt |
| **S3** `std.json` | P3: Encoder/Decoder, `JsonValue`, `encode`/`decode`/`encodeTo`/`decodeFrom`, `pretty`/`sortKeys`, `JsonError { line, column }`; strikt RFC 8259 (JSONTestSuite als Konformanz: `y_*` an, `n_*` ab) | Rundreise `User` → Text → `User`; `JsonValue.path("a.b[0]")`; die Testsuite |
| **S4** `std.toml` | P3: Lexer/Parser TOML 1.0, `TomlValue`, Encoder/Decoder; `toml-test` (BurntSushi) als Konformanz | das eigene `lyric.toml` gelesen; `toml-test` valid/invalid |
| **S5** `std.uri` + Base64 unpadded | P4: Parse/Resolve/Normalize/Prozentkodierung/`Query`; S4a | die RFC-3986-§5.4-Beispiele (normal und abnormal) als Fälle; JOSE-Beispiel aus RFC 7515 |
| **S6** `std.regex` (Pike) | P5: Parser, NFA, Pike-VM mit Captures, die API; Unicode-Klassen über M8c | der Log-Filter (`programs/logfilter.lyr`) über einen Text; lineare Zeit: `(a*)*b` über 10⁵ `a` in Millisekunden |
| **S7** `std.http` Client | P7 Client: Verbindung, Anfrage, Antwort, Chunked, Keep-Alive, Redirects, Fristen (`resolve` inklusive, S10c), `Transport`-Haken | Client gegen den eigenen Testserver (aus S8, hier ein minimaler `net`-Handler) und gegen `httpbin`-Fälle lokal |
| **S8** `std.http` Server | P7 Server: `bind`/`serve`, ein Task je Verbindung, Keep-Alive, Chunked, `100-continue`, `shutdown` | **das Artefakt der Zeile**: Server liefert JSON (`std.json`), Client holt es — `programs/jsonserver.lyr` |
| **S9** `std.compress` | P6: Inflate, Deflate 0–9, Zlib, Gzip, Adapter; die Konformanz: Rundreise über die std-Quellen, Vergleich mit `gzip -d` der Plattform (ein Testprozess) | `GzipReader` über `fs`; `Content-Encoding: gzip` im Client (S7 wird erweitert) |
| **S10** `std.term` | P8: die API, VT unter Windows, `NO_COLOR` | ein farbiger Logfilter (S6s Programm, jetzt mit Stil) |
| **S11** Zonen und Muster | P9: TZif-Parser, `Zone.load/local`, DST-Regeln, UTS #35 `format`/`parse` | `Europe/Berlin` über die Umstellung 2026-03-29; `America/New_York` Footer-Regel 2040; `"yyyy-MM-dd HH:mm Z"` hin und zurück |
| **S12** libm und Windows-Pipes | P10, P11: der musl-Port in C und C#, der Bitgleichheitstest; die überlappten Pipes | `comptime exp(1.0)` gibt die Laufzeit-Antwort; ein Kind, das schweigt, hält keinen Pool-Thread (Windows-Test wie M8b S12b) |
| **S13** Abschluss | P12: Messpunkt 4; `examples/` (jsonserver, logfilter); Dogfood (std-Tests, `lyric5 test`); STATUS, CHANGELOG, Bericht mit offenen Punkten; die Türen geprüft (lazy DFA, JSON5, CBOR, HTTP/2, Raw-Mode, Lokalisierung) | die Zeile oben erfüllt |

**Nicht in M10:**

- TLS (Ring `tls`), HTTP/2, Cookies, Proxys — Türen (Q6);
- `tzdata` eingebaut, Windows-Registry-Zonen, Lokalisierung über `en` — Türen (Q1);
- CBOR, MessagePack, CSV, JSON5/JSONC, streamender `SeqDecoder` — Türen (E9);
- Rückverweise und Lookaround im Regex — nie (RE2-Zusage);
- Ed25519/X25519/ChaCha20-Poly1305/Argon2 — Türen (Q4, U2);
- der Manifest-Leser der Toolchain über `std.toml` — M18 frühestens (die Toolchain ist C# bis dahin).

### Tests

- Je Slice die Regel-Fälle der Spec (12 §Modul), rot gegen den Compiler davor; die externen Suiten (JSONTestSuite,
  `toml-test`, RFC-3986-Beispiele, TZif-Dateien der Plattform) als Fixturen unter `tests/std/fixtures/` mit Lizenz.
- Eine Mutationskontrolle je Regel: `denyUnknown` aus (das unbekannte Feld geht durch), die Chunked-Länge falsch
  (der Client liest zu viel), die Pike-VM ohne Thread-Dedup (`(a*)*b` wird exponentiell), die DST-Lückenregel
  umgedreht.
- Messpunkt 4 vor S13 vorbereitet (die Go-Referenzen in `bench/http`, `bench/json`, `bench/regex`), damit S8 und S6
  schon messen.

### Umfang

| | Umfang |
|---|---|
| `codec` + Synthese (Lyric) | etwa 900 Zeilen |
| `json` + `toml` | etwa 1 600 |
| `uri` | etwa 500 |
| `regex` | etwa 1 800 |
| `http` Client + Server | etwa 1 800 |
| `compress` | etwa 1 400 |
| `term`, Zonen + Muster | etwa 900 |
| libm (C + C#) | etwa 2 500, davon 2 000 Port |
| Windows-Pipes (C) | etwa 300 |
| Spec | etwa 900 Zeilen, ~70 Fälle |

Nach Aufwand: groß S3, S6, S7/S8, S9, S12; mittel S1, S2, S4, S11; klein S5, S10, S13. Etwa zehn Sessions — XL.

### Reflexion

- **Go** ist das Vorbild der Zeile: eine std, die HTTP, JSON, Regex, Gzip und Zeitzonen trägt, ohne Registry. Go
  bringt tzdata mit (`time/tzdata`), Lyric nicht (Trägerklausel: Pflege); Go hat `encoding/json` über Reflexion,
  Lyric über die Synthese zur Compile-Zeit (A5) — der Weg, den Rust mit `serde_derive` geht, hier ohne Proc-Macro.
- **Rust** trennt `hyper`/`rustls`, `regex` (Pike + lazy DFA) und `miniz_oxide` — die Architektur von P5–P7; Lyric
  hat sie in `std`, weil Regel D sie dorthin stellt.
- **Swift** `Codable` ist das Paar (P1); Lyrics Container-Modell nimmt Swifts Form ohne die `CodingKeys`-Enums.
- **Zig** hat eigenes TLS in `std` — verworfen (U2); Zig hat `std.compress` und `std.json` ganz in der Sprache,
  wie hier.
- **Java** `java.time` für Zonen und Muster (P9); `DateTimeFormatter`s Lokalisierung bleibt Tür.

## Nachtrag 2026-10-15 — der Plan für M11

M11 nach seiner Zeile oben (01 L1 Stufen 2–3, L6, V3; Messpunkt 5), dazu jede Uhr, die auf M11 steht: die
Stack-Prüfung im Prolog (01 S3, M6-25 — mit ihr fallen die Abweichung von 05 E8 und der offene Punkt 192 der zweiten
Sweep-Runde: eine Überlauf-Panik aus dem Signal-Handler leert stdout nicht), `Atomic<Klasse>` (06 M6-23),
Messpunkt 3 (erreicht nach M11, sonst neu entscheiden), der Vorgabe-Hasher (10 R9c, spätestens mit Messpunkt 3), der
GC-Test `weak` (Review D1: M11 verlangt wieder alle Rückrufe). Dazu zwei Notizen, die in Testtexten stehen: der
TSan-Lauf des Thread-Programms läuft nur lokal, weil Boehms Stopp über Signale in der CI scheitert
(`SanitizerTests`: „geht mit dem Stopp an Safepoints“), und ein Weak-Ref auf eine Koroutine kollidiert mit deren
Finalizer (Boehm hält einen je Objekt).

Grundlage ist eine Prüfung jedes Punkts gegen den Code (2026-10-15). Von M11 steht:

- der **Vertrag der Stufe 0** (`lyr/gc.h`, `lyr/types.h`): `lyr_alloc(desc)`, `lyr_alloc_array`, `lyr_alloc_string`;
  je Klasse, Box, Closure-Umgebung und Array-Elementtyp ein Deskriptor mit Referenz-Bitmap (CEmitter);
  `LYR_DESC_CONSERVATIVE`, wo ein Enum mit Referenzen im Objekt liegt; die Barriere an jedem Referenz-Store des
  emittierten Codes (Feld, Platz, Element, ganzer Wert) und in `coro.c`/`task.c`; `LyrRoot`, `lyr_register_stack`,
  `LyrWeak` mit Rückruf, `lyr_thread_attach`/`detach`, die Zähler;
- **Stufe 1**: Boehm 8.2.12 (`gc_boehm.c`), der die Bitmaps nie liest; eine eigene Art für Koroutinen
  (Markierprozedur über Felder und Stack), ein Stopp über Signale, ein Handshake um jeden Stackwechsel;
- **Lücken**, die erst ein präziser oder generationaler Sammler sieht: `LYR_SAFEPOINT()` ist definiert und wird nie
  emittiert; die Modul-Globalen sind schlichte C-Globale, die Boehm mit `.data`/`.bss` abtastet; keine Barriere in
  `LYR_SLICE_MOVE` (array.h nennt die Stelle) und an `into->suppressed = grown` (error.c, ein bestehendes Objekt);
  `Atomic<T>` nur für `int` und `bool`;
- der **Überlauf** über Guard-Page und Handler allein: er beendet den Prozess, auch in einem Task (crash.c);
- die **Tests** in `runtime/tests` (`alloc`, `gc_smoke`, `heap_limit`, `roots`, `weak` mit 900 von 1000,
  `coro_gc`, `coro_pace`, `coro_storm`, `threads`, `stack_overflow`, `coro_overflow` …) und die `*_gc.lyr`-Programme
  unter Heap-Grenze; `CEmitterTests` vertagt den Test der Bitmaps, „bis ein Sammler sie liest (Stufe 2, M11)“;
- kein `bench/gc/`.

Die Slices heißen **M11 S1–S10**; jeder ist ein PR, sein Regel-PR in der Spec geht voran, wo sich eine Regel ändert
(13 §1.6 der Überlauf, 10 §8 die Atomics, 15 §4 und 14 das Profilfeld). Größe: **XL** wie die Zeile. Der eigene
Sammler wächst **hinter einem Entwicklungsschalter der Tests**; Boehm bleibt Vorgabe, bis die ganze Suite unter Immix
grün ist (S5) — dann kommt das Profilfeld `gc`, und Boehm ist das Vergleichsprofil der Zeile.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Eine zusammenhängende Reservierung**: `lyr_gc_init` reserviert den Adressraum des Heaps an einem Stück (256 GiB, bei Ablehnung halbiert bis 4 GiB; `PROT_NONE`/`MEM_RESERVE` — nur Adressraum), committet wird je **Chunk von 4 MiB**; `heap_limit` (11 H1) begrenzt das Committete. Daneben, parallel adressiert, die Seitentabellen: je 256-B-Zeile ein Markier- und ein Kartenbyte, je 16-B-Granulat ein Markier- und ein Startbit. „Ist das eine Heap-Adresse?“ ist dann eine Subtraktion und ein Vergleich — für den konservativen Filter, für Referenzen auf statische Objekte (String-Literale) und für die Kartenbarriere (P8). | V8 (Pointer-Cage), Go bis 1.10 (eine Arena von 512 GB), JSC (Gigacage). Verworfen: eine Chunk-Tabelle über den ganzen Adressraum (Gos zweistufiger Arena-Index — zwei abhängige Loads in jeder Barriere); Objekte je `malloc` (Boehm). Die Folge — kein Heap über die Reservierung hinaus — liegt mit 256 GiB über jeder Messung. |
| P2 | **Immix nach 01 L1**: Blöcke 32 KiB, Zeilen 256 B, **drei Größenklassen** — klein (≤ 256 B) und mittel (≤ 8 KiB) per Bump in Zeilenlücken, mittel bei zu kleiner Lücke in einen eigenen Überlaufblock, groß (> 8 KiB) im **Large-Object-Space**: seitengenau (4 KiB) in eigenen Chunks, über 2 MiB je eine eigene Chunk-Folge. Granulat und Ausrichtung 16 B (`LyrArr.data`); kein Objekt kreuzt einen Block. | Immix (Blackburn/McKinley 2008): Bump-Geschwindigkeit ohne Bewegen, Fragmentierung über Zeilenrecycling. „Größenklassen“ in L1 lese ich als Immix' drei Klassen, nicht als getrennte Freilisten je Größe (JSC, Go, Boehm) — die kosten den Bump, den L1 will. |
| P3 | **Allokation je Thread** (L6.1): ein thread-lokaler Puffer (Cursor, Grenze) im TLS-Block des Mutators; `lyr_alloc`, `lyr_alloc_array` und `lyr_alloc_string` werden `static inline` in `gc.h`: Bump, Vergleich, Header — **rund fünf Instruktionen** —, sonst `lyr_gc_alloc_slow` (nächste Lücke, wiederverwendbarer Block, freier Block, Sammlung). **Die Startbits schreibt nicht der Fast-Path**: ein Puffer wird beim Wechsel einmal über seine Header geparst (Größe aus Deskriptor und Länge), die laufenden Puffer aller Threads zu Beginn jeder Sammlung; beim selben Parsen entstehen die Zähler aus 10 X4 (Allokationen und Bytes je Thread). Lücken werden beim Nehmen genullt (der Vertrag verspricht genullte Objekte; Strings bis auf das NUL nicht). TLS-Modell `initial-exec`. Unter ASan nimmt jede Allokation den Slow-Path (L12), der entgiftet; freie Zeilen bleiben vergiftet. | Der Fast-Path ist die Stelle, an der L1 Boehm verwirft. Die konservative Immix-Variante (Shahriyar et al. 2014) führt dieselbe Objektkarte; P3 schreibt sie beim Pufferwechsel statt je Allokation, weil nur eine Sammlung sie liest. Verworfen: Zähler je Allokation (zwei Additionen mehr im Fast-Path, wie heute unter Boehm). |
| P4 | **Markieren**: Stop-the-World (Stufe 2) mit einem expliziten Markierstapel, ohne Rekursion. Präzise über die Bitmap (Wort i des Objekts, bei Arrays je Element); **Innenzeiger** (Views, `StringView`) über die Startbits zum Objektanfang; Wörter außerhalb der Reservierung (statische Objekte) werden übergangen. Objekte mit `LYR_DESC_CONSERVATIVE` (ein Enum mit Referenzen, V6) werden wortweise mit dem konservativen Filter gelesen — die Lesart von L1s „präzise über Typdeskriptoren“: für ein Union-Wort sagt der Deskriptor „vielleicht“. **Wurzeln**: (1) jeder Stack konservativ — der laufende eines Threads ab seinem Stack-Pointer, nachdem die Register gesichert sind (`setjmp`), und der eigene Stack eines Threads, auf dem gerade eine Koroutine läuft, ab seinem gesicherten `sp`; (2) eine Koroutine über ihr Objekt: Felder präzise, ihr Stack konservativ, solange er gemappt und nicht auf der CPU ist (wie heute); (3) die Modul-Globalen: der Emitter legt je Programm eine Tabelle ihrer Referenzwörter an, die der Start registriert; (4) `LyrRoot`-Zellen; (5) Bereiche aus `lyr_register_stack`, konservativ. Der **konservative Filter**: in der Reservierung, in einem benutzten Block oder LOS-Objekt, ein Startbit an oder vor dem Wort, und das Objekt reicht über das Wort. | Ruby ≥ 2.7, JSC: konservativer Stack, präziser Heap. Verworfen: präzise Enum-Karten je Variante (Tag-Offset und eine Bitmap je Variante, rekursiv) — eine Tür mit Auslöser: wenn `bench/gc` falsche Rückhaltung über Enum-Wörter misst. |
| P5 | **Safepoints und der Stopp**: der Emitter setzt `LYR_SAFEPOINT()` an jeden Schleifenrücksprung (Load eines globalen Flags und ein Branch, L6.3) und an jeden Funktionseingang die Prolog-Prüfung (P6), die zugleich Poll ist. Eine Sammlung setzt das Flag und jede Mutator-Grenze auf „sofort“ und wartet, bis jeder angemeldete Thread an einem Safepoint steht oder in einem **sicheren Bereich** ist; dann markiert sie, teilt Puffer und Blöcke neu ein und lässt weiterlaufen. Sicher ist ein Thread in jedem blockierenden Laufzeitaufruf — Poller-Warten, `lyr_task_wait`, die Syscalls der I/O-Pool-Threads, `waitpid`, Konsolen-Lesen, `getaddrinfo`, Schlafen, `join`: er klammert sich mit `lyr_gc_enter_safe`/`lyr_gc_leave_safe`, sichert vorher Register und Stack-Pointer und wartet beim Verlassen, solange eine Sammlung läuft. Es sammelt der Thread, dessen Allokation die Sammlung auslöst — kein eigener GC-Thread. Ein Koroutinenwechsel braucht keinen Handshake mehr: zwischen zwei Safepoints hält niemand die Welt an. | Go (`entersyscall`, kooperative Preemption bis 1.14), HotSpot (Poll, Thread-Zustände), .NET (kooperativer und präemptiver Modus). Verworfen: Signale — Boehms Weg; L6 schließt sie aus (unbekannter Compilerzustand), die CI sieht unter TSan „Signals delivery fails constantly“. |
| P6 | **Die Stack-Prüfung im Prolog** (01 S3): jede emittierte Funktion mit einem Aufruf beginnt mit einem Vergleich ihrer Stackadresse gegen die Grenze des Threads (TLS): untere Stackgrenze + Guard + Reserve; ein Koroutinenwechsel setzt die Grenze des Zielstacks mit, wie heute den Guard-Bereich. Eine Sammlung fordert den Halt an, indem sie jede Grenze auf das Maximum setzt — **ein Vergleich ist Poll und Überlaufprüfung** (Gos `stackguard0 = stackPreempt`). Der Slow-Path unterscheidet: angeforderte Sammlung → parken; sonst Überlauf → **Panik `LYR-RT0006` mit Backtrace**, gebaut auf dem Notstack des Threads (dem Speicher, den crash.c für den Signalstack hält), die die Koroutine verlässt wie jede Panik (05 E8; Spec 13 §1.6 Regel 10) — die Abweichung M6-25 fällt und mit ihr Punkt 192 (der normale Panikpfad leert stdout). Eine Funktion ohne Aufruf prüft nicht (die Reserve deckt ihren Rahmen); eine, deren Rahmen der Emitter größer als die Reserve kennt (Inline-Arrays), prüft mit ihrer Größe. Die Reserve bleibt so klein, dass Spec 12 JSON Regel 3 hält (512 Ebenen in jedem Task, 256 KiB im Release) — in S4 gemessen. Guard-Page und Handler bleiben das Netz für die C-Rahmen der Laufzeit und fremden Code. | Go (Prolog gegen `stackguard`), Rust (Guard-Page und Stack-Probes; Überlauf bricht ab). Verworfen: ein Tiefenzähler (S3); `-fsplit-stack`/`-fstack-check` (über `zig cc` nicht portabel; Split-Stacks haben Go und Rust aufgegeben). |
| P7 | **Wiederverwenden ohne Sweep-Phase**: nach dem Markieren sagen die Zeilenmarken, welche Zeilen frei sind; ein Block wird erst eingeordnet (frei, wiederverwendbar, voll), wenn ein Allokator ihn nimmt — „Sweep faul je Zeile“ (L1). Zeilenmarken über eine Epoche, damit sie nicht gelöscht werden müssen; die Markierbits einer vollen Sammlung werden je benutztem Block gelöscht (1/128 des Heaps). Große Objekte ohne Marke werden am Ende der Pause frei. **Auslöser**: allokierte Bytes seit der letzten Sammlung gegen ein Budget, das mit dem lebenden Heap wächst (die Grundform von Gos `GOGC = 100`); an `heap_limit` erst eine volle Sammlung, dann `LYR-RT0005`. **Keine Stellschraube** außer `heap_limit`. Blöcke, die mehrere Sammlungen frei blieben, gehen an das System zurück (`madvise`/`MEM_DECOMMIT`) — RSS ist eine der drei Größen von Messpunkt 5. | Immix (Zeilenmarken, Epochen), Go (Pacer). Verworfen: eine Sweep-Phase in der Pause; `GOGC` als Umgebungsvariable — eine Tür, die erst ein Nutzer öffnet. |
| P8 | **Generationen über Sticky-Mark-Bits** (Stufe 3): eine junge Sammlung löscht keine Markierbits — markiert heißt alt —, markiert von den Wurzeln und den **schmutzigen Karten** aus und steigt nie in ein markiertes Objekt ab; was überlebt, ist danach alt, ohne zu wandern. Eine volle Sammlung löscht alle Bits. **Die Barriere** wird echt: der Store, dann das Kartenbyte der Zeile des Slots (`slot − Heapbasis < Reservierung → Karte = 1`) — unbedingt im Heap, ohne Alt-/Jung-Test: der Emitter hat für den Slot nicht immer den Objektanfang (gc.h), die Karte braucht ihn nicht. `LYR_WRITE_BARRIER_VALUE` markiert die Karten des ganzen Werts, `LYR_SLICE_MOVE` die des Zielbereichs (die Span-Barriere aus array.h), `error.c` bekommt die fehlende. Globale, Thread-Stacks und registrierte Bereiche sind in jeder Sammlung Wurzeln; **eine Koroutine, die seit der letzten Sammlung lief**, wird in der jungen Sammlung als Wurzel gelesen — ihr Stack hat keine Barriere; die Laufzeit führt beim Fortsetzen die Liste. Jung nach dem Budget aus P7, voll, wenn sich der alte Heap seit der letzten vollen Sammlung verdoppelt hat. | Sticky Immix (Blackburn/McKinley 2008), JSC Riptide (Sticky-Mark-Bits, nicht bewegend, konservativer Stack), Demmers et al. 1990. Verworfen: eine Barriere mit Log-Bit je Objekt (braucht den Objektanfang); Schmutzbits über `mprotect` (Boehms inkrementeller Modus — Signale, Seitenkörnung). |
| P9 | **Weak-Refs und der Tod einer Koroutine**: die Weak-Records in einer `malloc`-Tabelle; nach dem Markieren wird jeder geleert, dessen Objekt unmarkiert ist, und sein Rückruf eingereiht; die Rückrufe laufen nach dem Neustart auf dem sammelnden Thread (der Vertrag aus gc.h: beim nächsten Allokieren oder `lyr_gc_collect`). Eine junge Sammlung leert, was jung starb. Mehrere Weak-Refs je Objekt sind keine Verkettung mehr — die Kollision mit der Koroutine fällt. Eine Koroutine bekommt das Deskriptor-Flag `LYR_DESC_CORO` (Felder präzise, Stack konservativ); eine beobachtete, unmarkierte Koroutine geht in der Pause an `lyr_coro_abandoned` (ihr Stack geht, 06 A5) — ein Haken der Laufzeit, kein Finalizer: nichts von ihr läuft, nichts wird wieder lebendig. Der Test `weak` verlangt wieder alle 1000 Rückrufe (D1). | Java `Cleaner`, Go `runtime.AddCleanup` (1.24). |
| P10 | **`Atomic<Klasse>`** (06 M6-23): `AtomicValue` nimmt Klassentypen auf; `load`, `store`, `exchange` und `compareAndSet` (nach Identität) als `__atomic_*` auf dem Zeigerwort, nach jedem Schreiben die Karte (P8); sequentiell konsistent wie `int` und `bool` (Spec 10 §8 Regel 4). `fetchAndAdd`/`addAndFetch` bleiben bei `int`. | Java `AtomicReference`, Go `atomic.Pointer[T]`. |
| P11 | **Boehm als Vergleichsprofil**: ein Profilfeld **`gc`** (`"immix"` oder `"boehm"`, Spec 15 §4 Regel 2) mit seinem Feldflag `--gc` (Spec 14); die Laufzeit wird je Sammler gebaut (bdwgc nur für `boehm`), der Laufzeit-Cache trägt den Sammler im Schlüssel. Beide erfüllen denselben Vertrag in `gc.h`; Safepoint-Flag und Karten sind unter Boehm leer, die Prolog-Prüfung bleibt (der Überlauf hängt nicht am Sammler). **Das Feld kommt mit S5**, wenn Immix Vorgabe wird: eine Regel über einen Sammler, der noch nicht sammelt, wäre falsch. Bis dahin wählt ein Entwicklungsschalter (`LYRIC5_GC=immix`, undokumentiert, fällt mit S5) den Sammler für jeden Bau, damit S1–S4 die ganze Suite vor dem Wechsel laufen. | 13: „Boehm bleibt als Profil zum Vergleich“. Das Wachsen hinter einem Schalter wie Gos `GOEXPERIMENT`. |
| P12 | **Messpunkt 5** (`bench/gc/`): `binarytrees` (Allokationsdurchsatz), `cache` (1 GB lebend in einer `Map` von Objekten, darüber stetige Kurzlebige und Ersetzungen alt → jung: Pausen, RSS, Barriere), `lru` (eine Liste über einer `Map`, viele Stores alt → jung); je Programm der Go-Zwilling und dasselbe Lyric-Programm unter `gc = "boehm"`. Die **Pausen aus `lyr_gc_stats()`** (11 H5, hier gebaut: Sammlungen jung und voll, Summe, Maximum, ein log2-Histogramm der Pausen, Heap- und Lebend-Bytes), vom Programm über `extern "C"` gelesen und neben der Prüfsumme ausgegeben; Go liest seine aus `runtime/metrics`; RSS misst `run.py` (`getrusage` der Kinder). Ratchet: **besser als Boehm in Durchsatz, Pausen und RSS; p99 der Pausen unter 10 ms bei 1 GB lebendem Heap** — die Lesart von „Pausen < 10 ms bei 1 GB“ unter Stufe 3: eine volle Sammlung über 1 GB Lebendes markiert länger als 10 ms, solange nicht nebenläufig markiert wird (Stufe 4, nach 5.0); ihr Maximum wird berichtet und gegen Boehm verglichen. **Paralleles Markieren** ist S9b, wenn dieses Maximum gegen Boehms parallelen Markierer verliert — sonst Tür. | wie die Messpunkte in 13; `binarytrees` aus dem Benchmarks Game. |
| P13 | **Die Uhren der Messung**: Messpunkt 3 wird nach M11 neu gemessen; bleibt `strings` über 1,5× Go, entscheidet der Maintainer neu (13). Der Vorgabe-Hasher (10 R9c) kommt mit derselben Messung zur Entscheidung: SipHash-1-3 gegen einen schnellen Hash mit Prozessschlüssel, auf `maps` und auf Header-Schlüsseln aus `http` — die Entscheidung trifft der Maintainer, mit einer Empfehlung. | wie die Uhren es verlangen. |
| P14 | **Spec**: 15 §4 (das Feld `gc`) und 14 (`--gc`) mit S5, 13 §1.4 Punkt 7 und §1.6 Regel 10 (ein Überlauf verlässt die Koroutine wie jede Panik), 10 §8 (`Atomic<Klasse>`); je Slice die Fälle rot gegen den Compiler davor. Der Sammler selbst ist keine Regel; was ein Programm von ihm sieht — genullte Objekte, stabile Adressen, Identität —, steht schon. | wie M10 P13 |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** Sammlerwahl und Immix-Heap | P11 (der Bau je Sammler, der Entwicklungsschalter); P1–P3: Reservierung, Chunks, Blöcke, Zeilen, LOS, Puffer, der Fast-Path inline, Startbits und Zähler beim Pufferwechsel; noch keine Sammlung — der Heap wächst bis `heap_limit` | unter Immix bauen und laufen die Laufzeittests, die keine Sammlung brauchen (`alloc`, `strings`, `arrays`, `hello` …); eine Allokationsschleife gegen Boehm gemessen |
| **S2** Markieren und Wiederverwenden | P4, P7 und P9 für einen Thread: Markierstapel, Bitmaps, Innenzeiger, konservative Wörter, der Stack des Threads und die Stacks seiner Koroutinen (`main` läuft selbst als Koroutine), die Globalen-Tabelle (Emitter), `LyrRoot`, Bereiche; Zeilenmarken, Blockeinteilung, Auslöser, `heap_limit`; der Koroutinen-Haken und die Weak-Refs; die Laufzeit auf Referenzen in C-Statics geprüft (nur über `LyrRoot`). Solange mehr als ein Thread angemeldet ist, sammelt S2 nicht — der Heap wächst, der Stopp kommt mit S3. Dazu eine **Prüf-Variante** der Laufzeit (`LYR_GC_VERIFY`, nur für Tests): ein Wort eines markierten Objekts, das auf einen unmarkierten Objektanfang zeigt, bricht mit Typ und Wortindex ab — der Bitmap-Test, den `CEmitterTests` vertagt | `alloc`, `gc_smoke`, `roots`, `heap_limit`, `conservative`, `coro_gc`, `coro_pace`, `weak` mit allen 1000 und die `*_gc.lyr`-Programme unter Immix mit kleiner Heap-Grenze, auch unter der Prüf-Variante |
| **S3** Safepoints und Threads | P5: die Polls im Emitter (Rücksprung; Eingang gegen die Grenze aus P6, noch ohne Überlaufzweig), das Mutator-Register, der Stopp, die sicheren Bereiche in jedem blockierenden Aufruf, die Stacks eines Threads, auf dem eine Koroutine läuft, für jeden Thread | `threads`, `coro_threads`, `coro_storm`, `poll_basic`; die ganze Suite (Lyric5.Tests, Konformanz, std-Tests) mit `LYRIC5_GC=immix` grün; der TSan-Lauf des Thread-Programms in der CI |
| **S4** Stack-Prüfung im Prolog | P6: die echte Grenze, der Slow-Path, der Notstack, die Panik verlässt die Koroutine; Windows (Grenze aus dem TEB) und macOS; das Netz bleibt | `stack_overflow` und `coro_overflow` erwarten die Panik; ein Fall „ein Task läuft über, `main` läuft weiter“ (rot davor: der Prozess endet); JSON mit 512 Ebenen in einem Task im Release |
| **S5** Immix wird Vorgabe | das Profilfeld `gc` und `--gc` (Spec 15 §4, 14), Vorgabe `immix`, der Schalter fällt; ASan (Slow-Path, vergiftete freie Zeilen), TSan (die Boehm-Annotationen fallen, wo der Stopp ordnet), Windows und macOS (Reservierung, TLS); Messpunkt 2 und 3 ohne Rückschritt durch Poll und Prolog — kostet der Poll im Rücksprung die Vektorisierung einer Schleife, pollt sie je 2^k Durchläufe (Entscheidung mit der Messung) | die Plattform-Jobs grün mit dem eigenen Sammler; `gc = "boehm"` bleibt grün |
| **S6** Generationen | P8: Sticky-Bits, die Kartenbarriere in `gc.h`, Wert- und Span-Barriere, `error.c`, die junge Sammlung über Karten und die Liste gelaufener Koroutinen, das Verhältnis jung zu voll | ein Laufzeittest alt → jung über jede Barrierenform und über einen Koroutinenstack; Kontrollen: Barriere leer → das junge Objekt stirbt, Liste aus → der Koroutinenfall stirbt |
| **S7** `Atomic<Klasse>` | P10 in `std.sync` und den Intrinsics; Spec 10 §8 | Fälle: ein Stack nach Treiber über `Atomic<?Node>`, `compareAndSet` nach Identität — rot davor (`LYR-SEM0028`) |
| **S8** Speicher zurück, Wachstum | P7: freie Blöcke und LOS-Chunks zurück an das System, das Budget nach dem lebenden Heap | das RSS eines Programms, das 1 GB aufbaut und loslässt, fällt wieder |
| **S9** Messpunkt 5 | P12: `bench/gc/` mit den Go-Zwillingen, `lyr_gc_stats()`, `run.py` (Pausen, RSS, die Boehm-Spalte); **S9b** paralleles Markieren, nur wenn verlangt | die Tabelle der Zeile; der Ratchet in `run.py --ratchet` |
| **S10** Abschluss | P13: Messpunkt 3 neu, die Vorlage zum Hasher; STATUS, CHANGELOG, Bericht mit offenen Punkten; die Türen geprüft (Stufe 4, präzise Enum-Karten, Stellschrauben, Bewegen nie) | die Zeile oben erfüllt |

**Nicht in M11:**

- Stufe 4 — nebenläufiges Markieren, paralleles Sweeping —: Minor nach 5.0 (01 L1);
- Bewegen, Kompaktieren, Pinning mit Wirkung — nie (01 L1: die Tür, die zugeht); Finalizer — nie;
- `GcHandle`, `lyr_gc_stats()` als Host-API mit Header-Zusage, fremde Threads (11 X6, X7, H5) — M14; hier ist
  `lyr_gc_stats()` intern und für `bench/gc`;
- eine Weak-Ref-API in Lyric — nicht entschieden (L1 meint das Netz für FFI-Handles, M14);
- Stellschrauben (`GOGC`, die Größe der jungen Generation) — Tür;
- Präemption über die Polls (01 K6) und ein Zeitbudget (01 L10) — Türen.

### Tests

- Die Laufzeittests laufen unter beiden Sammlern (`RuntimeProgramTests` mit dem Sammler als Achse, Boehm mit einer
  Auswahl), die Sanitizer-Profile unter Immix.
- Die Prüf-Variante (S2) über die `*_gc.lyr`-Programme und die std-Tests: sie fängt jede Bitmap, die eine Referenz
  verschweigt.
- Je Mechanismus ein Stresstest mit kleiner Heap-Grenze, der viele Sammlungen erzwingt: Threads × Koroutinen ×
  Allokation (`coro_storm` unter Immix), alt → jung über jede Barrierenform.
- Eine Mutationskontrolle je Regel: die Startbits beim Pufferwechsel aus (ein Innenzeiger verliert sein Objekt), ein
  sicherer Bereich ohne Klammer (der Stopp hängt — der Test hat eine Frist), die Karte nicht gesetzt, die Liste
  gelaufener Koroutinen aus, der Überlaufzweig auf „parken“ (die Panik bleibt aus).
- Messpunkt 5 wird in S6 vorbereitet (die Go-Zwillinge in `bench/gc`), damit die Generationen schon messen.

### Umfang

| | Umfang |
|---|---|
| `gc_immix.c`: Heap, Allokation, LOS, Markieren, Wiederverwenden | etwa 2 200 Zeilen C |
| Safepoints, Stopp, sichere Bereiche | etwa 600, dazu Klammern in neun Dateien |
| Generationen (Karten, junge Sammlung) | etwa 500 |
| `gc.h`: Fast-Path, Barrieren, Polls | etwa 200 |
| Emitter (Polls, Prolog, Globalen-Tabelle) und das Profilfeld | etwa 400 Zeilen C# |
| `Atomic<Klasse>` | etwa 150 |
| Tests (C und C#) | etwa 1 500 |
| `bench/gc` (Lyric, Go, `run.py`) | etwa 700 |
| Spec | etwa 120 Zeilen, ~10 Fälle |

Nach Aufwand: groß S2, S3, S6; mittel S1, S4, S5, S9; klein S7, S8, S10. Etwa acht bis zehn Sessions — XL.

### Reflexion

- **Go** ist der nächste Verwandte: nicht bewegend, ein Pacer über den lebenden Heap, und die Prolog-Prüfung, die
  zugleich Preemptionspunkt ist — P6 übernimmt sie. Go hat Stack-Maps aus dem eigenen Compiler, Lyric hinter einem
  C-Backend nicht, daher der konservative Stack. Go hat keine Generationen und markiert stattdessen nebenläufig;
  Lyric nimmt die Generationen zuerst, weil Sticky-Bits keine Änderung im Backend brauchen (L1).
- **JavaScriptCore (Riptide)** ist das Muster der Kombination: konservativer Stack, präziser Heap, nicht bewegend,
  generational über Sticky-Mark-Bits. JSC trennt Größenklassen in Freilisten; Lyric nimmt den Bump aus Immix (P2).
- **Immix** (Blackburn/McKinley 2008) ist der Heap, **Sticky Immix** dieselbe Arbeit ohne Kopieren, und die
  konservative Variante (Shahriyar et al. 2014) zeigt, dass eine Objektkarte die Lücke zum konservativen Stack
  schließt.
- **Ruby** (RGenGC ab 2.1) ging denselben Weg zu Generationen ohne Bewegen und lebt mit Objekten ohne Barriere
  („shady“). Lyric emittiert die Barriere seit Stufe 0, daher gibt es solche Objekte nicht.
- **Boehm** bleibt als Messlatte (P11): konservativer Heap, Signale, langsame Allokation — die drei Gründe aus L1,
  jetzt gemessen statt behauptet. **MMTk** bleibt verworfen (L1): sein Binding-Vertrag wäre so groß wie dieser Plan.

### Nachtrag 2026-10-16 — S5 gemessen: der Poll zählt

- Messpunkt 2 und 3 gegen den Baum vor den Polls (M11 S2): mit dem Flag in jedem Durchlauf verlor `structs`
  13 % — der Load und sein Sprung in der inneren Schleife, nicht die Vektorisierung (sie vektorisierte weder
  davor noch danach). Entschieden mit der Messung: eine Schleife pollt je **2^10 Durchläufe** ihres Kopfes,
  heruntergezählt in einer lokalen Variable der Funktion (`LYR_LOOP_POLL`, `LYR_POLL_EVERY`); `structs` ist
  zurück (0,99). Ein Stopp wartet höchstens so viele Durchläufe einer Schleife ohne Aufruf — eine mit Aufruf hält
  am Eingang des Gerufenen.
- `sorting` behält 4–7 %: sein C ohne den Poll läuft wie zuvor, die Prolog-Prüfung kostet nichts Messbares; es
  bleibt das Dekrement mit Sprung in einer engen Merge-Schleife. Strip-Mining gezählter Schleifen (die innere
  Grenze `min(last, i + 1024)`, der Poll in einem äußeren Kopf — HotSpots `LoopStripMiningIter`) nähme auch das;
  die Entscheidung fällt mit der Messung aus P13. Die Ratschen halten (`sorting` 1,28× Go, `maps` 1,44×).

### Nachtrag 2026-10-16 — S6 gebaut und gemessen

- **Der Wechsel ist die Barriere des Stacks.** P8 sah eine Liste der Koroutinen vor, die seit der letzten Sammlung
  liefen. Gebaut ist sie als Karte: `lyr_gc_switch_begin` markiert die Karte der Zeile, auf der das Objekt der
  verlassenen und der betretenen Koroutine beginnt, und die junge Sammlung liest eine Koroutine von ihrer
  Anfangszeile aus ganz, ihren Stack mit. Ein Mechanismus statt zweier (CONTRIBUTING Regel 2): die Felder einer
  Koroutine schreibt `coro.c` ohnehin über die Barriere. Die Kontrolle der Liste blieb grün, solange der Test nur gab
  und fortsetzte — jedes Fortsetzen und jedes Geben schreibt `resumer_coro` und markiert die Karte; erst die innere
  Koroutine einer geparkten Kette, die nichts Eigenes schreibt, braucht die Karte des Wechsels. Der Test hat sie jetzt.
- **Die Stacks takten jung zuerst** (06 A5): was ein Programm an Koroutinen fallen lässt, ist meist jung; voll erst,
  wenn die junge Sammlung die Stacks nicht unter die Schwelle brachte.
- **Die Prüfung der Barriere** (`LYR_GC_VERIFY`): eine Abschrift des Heaps nach jeder Sammlung; vor der nächsten ist
  jede Referenz eines markierten Objekts, die sich seither geändert hat, auf ein junges Objekt zeigt und deren Karte
  sauber ist, ein Store ohne Barriere — das Programm endet. „Markiert zeigt auf Markiertes" taugt als Prüfung nicht:
  ein totes Objekt, das ein veraltetes Stackwort am Leben hält, darf auf längst Freigegebenes zeigen.
- **Das Verhältnis jung zu voll** bleibt P8: jung nach dem Budget aus P7 (so groß wie der alte Heap), voll, wenn der
  alte Heap sich verdoppelt hat. Gemessen gegen eine junge Generation von einem Viertel und einem Achtel des alten
  Heaps, voll schon beim Anderthalbfachen: `cache` 30–40 % langsamer, seine RSS unverändert; `lru` und
  `binarytrees` 30–43 % weniger RSS bei bis zu 27 % mehr Zeit.
- **Gemessen** (`bench/gc`, für Messpunkt 5 vorbereitet; WSL2, Release-Profil): die Generationen machen `cache`
  26 % schneller als die vollen Sammlungen allein (12,3 s gegen 16,6 s) und kosten 12 % RSS (3,0 GiB gegen 2,7);
  `binarytrees` braucht die Hälfte der Zeit Boehms und drei Viertel der Gos (`bench/README.md`).
- **Zwei Befunde für S9**, gesammelt als offene Punkte:
  1. *Pausen*: das Budget aus P7 macht die junge Generation so groß wie die alte — bei 1,4 GiB Lebendem pausiert eine
     junge Sammlung 0,9 s, eine volle 1,4 s (Markieren mit etwa 0,8 GB/s, drei Seitentabellen je Objekt). Das p99 von
     P12 (unter 10 ms bei 1 GB) braucht eine Obergrenze der jungen Generation und ein schnelleres Markieren
     (Prefetch; Start- und Markierbit in einem Byte). S9 entscheidet mit den Pausen aus `lyr_gc_stats()`.
  2. *RSS*: `cache` hält 1,4 GiB Lebendes in 3,0 GiB, Boehm in 1,6, Go in 1,8 — bei jeder Taktung. Es sind die
     Zeilen: kurzlebige Scratch-Arrays liegen zwischen langlebigen Einträgen in denselben 256-Byte-Zeilen, und eine
     Zeile mit einem lebenden Byte bleibt belegt; Boehm trennt nach Größenklassen. „Besser als Boehm in RSS" (P12)
     braucht damit mehr als Taktung — Größenklassen für kleine Objekte (01 nannte sie, P2 nahm Immix' Bump),
     kleinere Zeilen, oder die Ratsche neu gelesen. Die Entscheidung trifft der Maintainer.

### Nachtrag 2026-10-16 — S8 gebaut: Speicher zurück

- **Wann**: nach jeder vollen Sammlung, wenn die Welt wieder läuft, unter der Sperre des Heaps — keine Arbeit
  in der Pause (P7). Der sammelnde Thread kehrt die Blöcke, die noch kein Allokator angesehen hat, damit jeder
  freie bekannt ist; die freien über dem Budget des nächsten Zyklus gehen an das System, die höchsten zuerst,
  in Läufen; der Stapel der freien Blöcke wird neu gebaut, die niedrigsten oben — der Heap bleibt unten, was
  zurückging, wird zuletzt genommen. Große Objekte schon in der Pause: ein Chunk großer Objekte, der leer wurde,
  und die Chunks eines riesigen Objekts mit einem Aufruf je Chunk-Lauf.
- **Wie**: Linux `madvise(MADV_DONTNEED)`; Windows `MEM_DECOMMIT` und vor dem nächsten Ausgeben wieder
  `MEM_COMMIT`; macOS frische Seiten mit `MAP_FIXED` darüber — Darwins Hinweise geben träge frei, und das
  Resident-Set behielte die Seiten. Was zurückging, wird beim Ausgeben genullt wie jeder wiederverwendete Speicher.
- **Gemessen**: ein Programm, das 512 MiB aufbaut (kleine Objekte, große, zwei riesige) und loslässt — 601 MiB
  resident, nach zwei vollen Sammlungen 17 MiB (`runtime/tests/memory_return.c`). Ohne die Rückgabe: 601.
- **Was es nicht löst**: die RSS von `cache` (Zeilenfragmentierung, der S6-Nachtrag) — dort ist der Speicher
  belegt, nicht frei. Und ein Programm, dessen Altes stirbt und das danach wenig befördert, sammelt nie wieder
  voll: der alte Heap wächst nicht, P8 löst keine volle Sammlung aus, sein Toter bleibt resident. Ein Auslöser
  über die Zeit (Gos Scavenger arbeitet so) ist eine Tür; die Entscheidung mit S9s Messung.

### Nachtrag 2026-10-16 — S9 gemessen: Messpunkt 5

- **Die Zahlen** (`lyr_gc_stats()`, `runtime/src/gc_stats.c`, intern bis M14): jede Pause vom Stopp-Wunsch bis
  zum Neustart, für beide Sammler — Boehm über `GC_EVENT_PRE_STOP_WORLD` und `POST_START_WORLD`; jede Pause wird
  behalten (acht Byte), die Perzentile sind genau. Die Programme lesen sie über `extern "C" fn lyr_gc_stat`, der
  Go-Zwilling aus `runtime/metrics`; `run.py` vergleicht nur die Zeile der Prüfsumme.
- **Die Lesart von P12**: „p99 … bei 1 GB lebendem Heap" zählt den stetigen Teil — `cache` beginnt die Zählung nach
  dem Aufbau neu (`lyr_gc_stat(9)`; Go: die Differenz der Histogramme). Über den ganzen Lauf bestimmt die Aufbauphase
  das p99: der Heap verdoppelt sich von 4 MiB auf 1,4 GiB neunmal, jede Verdopplung eine volle Sammlung.
- **Die junge Generation ist begrenzt** (8 MiB, `YOUNG_MAX`): das Budget aus P7 machte sie so groß wie die alte, und
  eine junge Sammlung über 1,4 GiB pausierte 0,9 s. Für die vollen bleibt P8: voll, wenn der alte Heap sich verdoppelt
  hat. 16 MiB gaben junge Pausen von 23–36 ms, 8 MiB vor den Schritten unten 14–19.
- **Das Markieren**: (1) `mark_object` liest den Kopf nicht mehr — er wird beim Abarbeiten gelesen, durch einen Ring
  von acht Objekten vorab geholt (Boehms Prefetch-Puffer); (2) ein Wort auf einen markierten Anfang beantwortet das
  Markierbit; (3) eine junge Sammlung überspringt Wörter in Blöcke und Chunks, die seit der letzten Sammlung nichts
  ausgegeben haben — eine kleine Tabelle statt des Markierbit-Felds, das bei 1,4 GiB 11 MiB groß ist; (4) der
  Kartenscan überspringt Zeilen ohne altes Objekt (ihre Zeilenmarke ist nicht die Epoche). Junge Pausen in `cache`:
  p50 7 ms, p90 9 ms. Start- und Markierbits nebeneinander (ein Cache-Zugriff statt zweier) machten das volle
  Markieren 10 % schneller und die jungen Sammlungen langsamer — die Startbits eines Blocks lagen nicht mehr dicht —
  und wurden zurückgenommen.
- **Gemessen** (WSL2, Release-Profil, das Minimum von drei Läufen, die Maschine ruhig; die Pausen des schnellsten):

  | bench | twin | time | RSS | collections | pause p99 | pause max |
  |---|---|---|---|---|---|---|
  | binarytrees | Lyric | 0.933 s | 60 MiB | 259 | 6.42 ms | 10.94 ms |
  | binarytrees | Lyric (Boehm) | 1.849 s | 62 MiB | 121 | 9.89 ms | 11.79 ms |
  | binarytrees | Go | 1.122 s | 38 MiB | 133 | 0.20 ms | 0.23 ms |
  | cache | Lyric | 12.582 s | 2454 MiB | 240 | 11.01 ms | 881.17 ms |
  | cache | Lyric (Boehm) | 15.839 s | 1571 MiB | 7 | 718.75 ms | 722.35 ms |
  | cache | Go | 5.962 s | 1853 MiB | 1 | 12.58 ms | 12.58 ms |
  | lru | Lyric | 3.592 s | 114 MiB | 32 | 14.30 ms | 15.24 ms |
  | lru | Lyric (Boehm) | 3.454 s | 41 MiB | 21 | 19.39 ms | 24.34 ms |
  | lru | Go | 1.501 s | 37 MiB | 15 | 1.31 ms | 1.31 ms |

- **Die Ratsche** (`run.py --ratchet`) hält für `binarytrees` (Zeit, RSS, längste Pause besser als Boehm) und
  verfehlt: `cache` in RSS (die Zeilen, S6-Nachtrag), längster Pause (881 gegen 722 ms) und p99 (11,0 gegen 10 ms);
  `lru` in Zeit (+4 %) und RSS. **S9b ist damit verlangt**: P12 band das parallele Markieren an Boehms parallelen
  Markierer — unser Boehm-Build markiert auf einem Faden (kein `PARALLEL_MARK`; bdwgc setzt es nur über configure
  oder CMake), und die längste Pause verliert schon gegen ihn. S9b folgt als eigener Schritt; RSS und die Lesart der
  Ratsche entscheidet der Maintainer (die offenen Punkte).

### Nachtrag 2026-10-19 — S9b gebaut und gemessen: paralleles Markieren

- **Wie**: eine volle Sammlung, die mindestens 128 MiB Lebendes erwartet (`SHARE_MIN`), markiert mit Helfern — einer
  weniger als die Prozessoren, höchstens sieben —, die beim ersten Sammeln starten, zwischen den Sammlungen schlafen,
  kein Signal des Programms nehmen und keinen Programmcode laufen. Das Markierbit wird dann in einem atomaren Schritt
  gesetzt, nur wer es setzte, legt das Objekt auf seinen Stapel; die Zeilenmarke ist ein relaxter Store der Epoche.
  Ein Markierer, dessen Stapel über zwei Pakete wächst, während ein anderer wartet, gibt ein Paket zu 256 Objekten in
  einen gemeinsamen Pool; das Markieren endet, wenn alle warten und der Pool leer ist. Allein — jede junge Sammlung,
  jede volle unter `SHARE_MIN` — markiert der sammelnde Faden ohne den atomaren Schritt und gibt nichts ab.
- **Ein Fehler, gefunden von der Messung** (offener Punkt 212): der Wartezähler blieb nach einer geteilten Markierung
  stehen, und die nächste junge Sammlung gab allein Pakete in einen Pool, den niemand las — ihre Objekte wurden nie
  gescannt, ihre Zeilen galten als frei, und die nächste volle Sammlung scannte, was dort inzwischen lag (`cache`:
  SIGSEGV). `runtime/tests/mark_shared.c` (160 MiB Ballast über `SHARE_MIN`) ist davor rot, danach grün; auch unter
  ASan und TSan.
- **Die Schwelle**: jede volle Sammlung geteilt kostete die kleinen Heaps — `lru` (rund 100 MiB) 17–20 ms längste
  Pause gegen 14,5 allein, `binarytrees` 11 gegen 8–9 ms. Junge Sammlungen geteilt maßen nicht besser (`cache`, 90.
  Perzentil 10,0 gegen 10,9 ms, im Rauschen) und weckten die Helfer 369-mal.
- **Gemessen** (die Maschine ruhig, zwei abwechselnde Runden gegen main mit S9, je das Minimum aus 3 Läufen):

  | bench | S9: Pause max | S9b: Pause max | Boehm | S9b: p99 |
  |---|---|---|---|---|
  | binarytrees | 8,4 / 9,3 ms | 8,1 / 9,0 ms | 9,5 / 8,4 ms | 6,3 ms |
  | cache | 786 / 823 ms | **462 / 482 ms** | 661 / 654 ms | 9,2 ms |
  | lru | 14,3 / 14,6 ms | 14,2 / 16,4 ms | 17,1 / 15,0 ms | 14,2 ms |

- **Die Ratsche** hält damit jedes Pausenziel aus P12 (`cache`: p99 unter 10 ms, die längste Pause unter Boehms); es
  bleiben RSS (`cache`, `lru` — die Zeilen, S6-Nachtrag) und die Zeit von `lru` (+10 % gegen Boehm), die Entscheidungen
  des Maintainers (offene Punkte 204, 205).

### Nachtrag 2026-10-19 — S10: Abschluss, Durchsicht

- **Messpunkt 3 neu** (P13): `strings` 1,54–1,68× Go in vier Messungen (18./19.10., die Maschine verrauscht —
  Cs eigenes `loops` schwankte um ein Viertel), jedes Mal über der Schranke 1,5; `maps` 1,09–1,56×, um sie herum;
  `sorting` 0,99–1,43×. Der Poll kostet `sorting` 4–7 % (S5); `strings` lag schon vor M11 über der Schranke.
- **Der Vorgabe-Hasher** (P13, 10 R9c): ein gefaltetes Produkt unter Prozessschlüssel gegen SipHash-1-3 auf
  `maps`, einer Map aus 40 Header-Namen und einer neuen Map je Anfrage — 6–15 % schneller in drei Sitzungen.
  Der größere Posten ist `string.hash`, das Byte für Byte über `writeByte` schreibt: FNV ohne jede Mischung liegt
  bei Strings bei 40–55 % von SipHash. `std.http` legt Header in einer Liste ab, nicht in einer Map.
- **Die Türen geprüft**: Stufe 4 (nebenläufiges Markieren, paralleles Sweeping) nicht gebaut; Enums mit Referenzen
  weiter konservativ gelesen (präzise Karten je Variante: Tür, kein Befund falscher Rückhaltung in `bench/gc`);
  keine Stellschraube (kein `getenv` im Sammler, `heap_limit` die einzige); nichts bewegt sich — Bewegen bleibt nie.
- **Durchsicht 2026-10-19, alle Punkte nach Empfehlung**:

  | # | Entscheidung |
  |---|---|
  | 204 | **Die Ratsche neu gelesen**: RSS wird berichtet, nicht gegated. Größenklassen für kleine Objekte oder kleinere Zeilen sind eine Tür (Uhr: nach 5.0, oder wenn ein Nutzer es misst). |
  | P13 `strings` | **Ein Profil-Sweep vor M12**, die Schranke bleibt 1,5. |
  | P13 Hasher | **SipHash-1-3 bleibt** (R9c bestätigt); `string.hash` wortweise (offener Punkt 214) im Sweep, danach `maps` und die Header-Map nachmessen. |
  | Sweep vor M12 | 208 (eine volle Sammlung über die Zeit: ein Programm, dessen Altes stirbt und das wenig befördert, sammelt sonst nie wieder voll), 203 und 206 (der Testprogramm-Cache je Checkout, der Laufzeit-Cache ausgedünnt), 200, 209, 215 (Hygiene), 214 und `strings`. |
  | 207 | Strip-Mining der Polls: Tür (die Ratsche hält). |

## Nachtrag 2026-10-19 — der Plan für M12a

M12 nach seiner Zeile oben, geschnitten mit dem Maintainer (2026-10-19): **M12a** ist die Diagnostik — W6 (G1–G13), W7
(L1–L5 und die Lint-Liste), die Verben `check`, `new`, `init`, `env`, `completions`, `explain`, `fix` (11 C2–C4, BS1, BS4;
Durchsicht 2026-10-14), die Noten aus 07 (M7-3, M7-11, K4, K7), die Warnungen aus 08 F4 und 10 S3, der Lint für fremde
Konformanzen (03 M4-5) und die Exit-Bereichswarnung aus 11 C5. **M12b** sind die Sprach-Uhren, die die Reviews an M12
hängten — 03 S15a (Typargumente am Methodenaufruf; Fixierung in Funktionen), N2b (geschriebene Typen prüfen ihre
Schranken), S5 (`?U` aus einem Lambda, das `T` liefert), S16 (ein Literal nimmt `T[N]`), T8 (Unifikation mit Literal), T19
(die Kettenmeldung), 04 R4a3 (Arität über Rumpf und Blöcke), R4b (der generische Weiterleiter), S7 (mehrere `From`), die
Zeile „SEM0047, SEM0082, Delegation an ein Kind-Interface-Feld“ —, mit eigenem Plan direkt nach M12a. Die Diagnostik
allein ist die Zeile; mit den Sprachpunkten wäre sie mehr als das Doppelte (CONTRIBUTING, Scope-Check).

Grundlage ist eine Prüfung jedes Punkts gegen den Code (2026-10-19). Von M12a steht:

- der **Record** `Diagnostic(Code, Severity, Span, Message, Notes?)` — ein Span, Noten `(Location, Message)` ohne Art,
  kein Fix; Severities `Error`, `Warning`, `Info`, `Hint`; die Engine dedupliziert, verschiebt Fehler aus synthetisiertem
  Code an den Aufrufer mit Notenkette, unterdrückt Nicht-Fehler über `Suppress`, sortiert deterministisch, maskiert
  Steuerzeichen;
- die **Ausgabe** `pfad:zeile:spalte: severity[CODE]: message`, die Quellzeile, Carets — keine Rinne, kein `-->`, keine
  Farbe, keine beschrifteten Nebenspans; JSON als ein Dokument (`{"diagnostics":[…]}`), nur im 4.x-Pfad; kein
  `--max-errors`;
- **Folgefehler**: der Checker vergiftet über `ErrorType` (ein Test hält die Invariante „wer ihn sieht, schweigt“);
- **Warnungen** ad hoc, ohne Namen (WarningAnalyzer: „Deliberately no lint framework“): SEM0071–0073, 0076, 0077, 0103,
  0104, 0112, 0139, 0144, 0152, 0153, 0162, 0168, 0171, 0172, 0173, 0178, 0180; SEM0075 als `Hint`;
- **`@Allow(code)`** und `[lints] allow = [codes]` (AllowFilter, Manifest — „Namen kommen mit M12“), `denyWarnings` als
  Profilfeld und `--[no-]deny-warnings` (Pipeline.Denied; meldet heute `LYR-CLI0006`, den Code von `VmLaunchFailed`, statt
  `CLI0016`, und schreibt die Zeile an der Engine vorbei);
- **Codes**: 323 verschiedene im Code, kein Katalog, kein `explain`; `LYR-IR0001` an 15 Stellen als Sammelcode (G4);
- **Fixes**: keine; `NameSuggestion` (Editierdistanz) als schlichte Note; `@Deprecated.replacement` nur als Note;
- **Verben** von `lyric5`: `build`, `run`, `expand`, `test`, `bench`, `update`, `clean`, `metadata`, `add`, `remove`,
  `version`, `help` — `check`, `new`, `init`, `env`, `completions`, `explain`, `fix` fehlen; die Optionen je Verb liegen als
  Code in `Program.cs`, nicht als Tabelle;
- **Spec**: Kapitel 16 und Anhang A sind Skelette („written with M12“).

Die Slices heißen **M12a S1–S11**; jeder ist ein PR, sein Regel-PR in der Spec geht voran, wo sich eine Regel ändert (16
für Record, Ausgabe, JSON, Lints, Fixes; 14 für die Verben; Anhang A wird erzeugt). Größe: **L**.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Der Record nach G1** in `Lyric.Core`: `Diagnostic { Code, Severity, Labels: LabeledSpan[] (genau ein primärer, sekundäre mit Text), Message, Notes: Note[] }`, `Note { Kind: Place \| Help \| Suggestion \| Category, Span?, Text, Replacement?, Applicability? }`, `Applicability { Safe, NeedsReview }`; Severities `Error`, `Warning`, `Hint` — `Info` fällt (keine Stelle meldet es heute). Die bisherigen `Report`-Überladungen bleiben als dünne Formen über dem neuen Record (ein Span = ein primäres Label, eine Note = `Place` oder ohne Span `Help`), damit S1 kein Tausend-Zeilen-Umbau der Melder wird; neue und berührte Melder nehmen die volle Form. | rustc `Diagnostic` (Labels, Sugg. mit `Applicability`), Clang. Verworfen: Nebenspans als Noten mit Ort (heute) — der Editor kann sie nicht als zweiten Unterstrich zeigen, die Ausgabe nicht als Label. |
| P2 | **Die Ausgabe nach G5** als eigener Renderer (`Lyric.Core/Rendering`): `error[LYR-SEM0042]: message`, `--> pfad:zeile:spalte`, Rinne mit Zeilennummern, die Zeilen aller Labels einer Datei zusammen, primär `^^^`, sekundär `---` mit Text, `= note:`/`= help:`; lange Zeilen gekürzt (um den Span, mit `…`); Farbe nach C7 (`--color auto\|always\|never`, `NO_COLOR`, `TERM=dumb`); Pfade projektrelativ, `--absolute-paths`; `--max-errors N` (Vorgabe 50, 0 = alle), danach eine Zeile „N weitere Fehler“. Die Golden-Tests der Ausgabe sind Textvergleiche ohne Farbe. | rustc, Clang. Verworfen: die heutige GCC-Form beibehalten — sie zeigt keine Nebenspans und keine Notenorte im Quelltext. |
| P3 | **Folgefehler (G6)**: die Vergiftung bleibt im Checker (`ErrorType`), die Engine bekommt die Regel dazu, die der Checker nicht sehen kann — eine Deklaration, deren Parse scheiterte, meldet nichts Abgeleitetes (Y4 machte das für die Synthese; S2 macht es für Resolver und Checker allgemein: ein Fehler im Ausdruck einer Deklaration mit Parserfehler wird unterdrückt, die Parserfehler bleiben). | rustc (`ErrorGuaranteed`), Clang. Verworfen: eine Fehlerobergrenze je Deklaration — verbirgt echte zweite Fehler. |
| P4 | **Der Katalog** `diagnostics.toml` im Compiler-Repo (`src/Lyric.Core/`, eingebettet): je Code `title`, `severity`, `area`, `summary` (ein Absatz), `example` (ein minimales Programm, das ihn auslöst), für Fehler und Lints zusätzlich `explanation` mit `wrong`/`right` — mit dem Maintainer (2026-10-19): kurz für alle, lang für Fehler und Lints; für Lints `name`, `group`, `level`. Zurückgezogene Codes bleiben mit `retired = "…"`. **`lyric explain CODE\|lint-name`** druckt den Eintrag (Markdown als Text). Der Spec-Anhang A wird aus dem Katalog **erzeugt** (`tooling/diagnostics/appendix.py`), als Teil des Spec-PRs jedes Slices, der Codes ändert. | rustc `--explain`, GHC Error Index, G9. Verworfen: der Katalog in der Spec (Regeln sind spec-first, der Katalog nicht — D16 E); Texte aus den Doc-Kommentaren der C#-Konstanten — sie decken nur CLI ab. |
| P5 | **Die Wächter (G10)** als Tests in `Lyric5.Tests`: (a) jeder Code, den der Compiler meldet, steht im Katalog — ein Quelltext-Scan der Literale `"LYR-…"` plus die dynamisch gebildeten (Interfaces der Laufzeit `LYR-RT…` aus `panic.h`); (b) jeder Katalogcode wird gemeldet oder ist `retired`; (c) die Severity im Katalog ist die gemeldete; (d) **jede Warnung und jeder Lint hat einen roten und einen grünen Konformanzfall** in der Spec-Suite (der Wächter liest die Fälle des gepinnten Spec-Commits); (e) Meldungsinvarianten: klein beginnend, ohne Punkt am Ende, Code im Katalog, kein `IR0001`. | rustc `tidy`, 4.x appendix-a-Wächter. |
| P6 | **`LYR-IR0001` fällt (G4)**: jede der 15 Stellen bekommt ihren eigenen Code und Satz (`LYR-SEM…`, wo es eine Sprachgrenze ist; `LYR-ICE…`, wo es ein Compilerfehler wäre). | G4, rustc ICE. |
| P7 | **NDJSON (G8, C6)**: `--json` als globale Option auf `build`, `run`, `test`, `check`, `fix`: eine Zeile je Ereignis — `{"type":"diagnostic","version":1,…}` mit dem Record (Labels, Noten, Fixes, Ort mit Zeile/Spalte/Offset), `{"type":"artifact",…}`, `{"type":"test",…}`, `{"type":"summary","errors":N,"warnings":M}`; Panik und ICE als `{"type":"ice"/"panic",…}`. Das Schema steht in Spec 16 (und 14 für die Ereignisse der Verben). Fortschritt nie auf stdout, wenn `--json`. | Cargo `--message-format=json`, rustc `--error-format=json`. Verworfen: SARIF (Tür nach G8); das heutige eine Dokument (nicht streamfähig). |
| P8 | **Lints (W7)**: ein Lint ist ein Katalogeintrag mit `name`, `group`, `level`; die bestehenden Warnungscodes werden Lints mit Namen (`unused-variable` = SEM0071, `unused-import` = SEM0072, `unreachable-code` = SEM0073, `deprecated` = SEM0076, `prelude-shadowing` = SEM0153, `unused-label` = SEM0103, `unreachable-pattern` = SEM0112, `closeable-never-closed` = SEM0144, `unused-result` = SEM0178 …); eine Warnung, die kein Lint ist, gibt es nicht mehr (die Severity eines Lints ist sein Level). **Ebenen** `allow \| warn \| deny` je Lint und je Gruppe (`unused`, `correctness`, `resources`, `concurrency`, `deprecation`, `style`, `perf`, `docs`, `warnings` = alle); **drei Orte, ein Mechanismus**: `[lints]` (`deny = […]`, `allow = […]`, `warn = […]`, Gruppen und Namen und Nummern), `@Allow`/`@Warn`/`@Deny(name)` an Deklaration oder Modul (09 A11 nimmt `@Warn`/`@Deny` auf, mit dem Maintainer), CLI `-A`/`-W`/`-D name` und `--deny-warnings`; Präzedenz C4 (Kommandozeile > `LYRIC_*` > `lyric.toml` > Vorgabe), innerhalb einer Ebene das Engste (Deklaration > Modul > Paket). `deny` = Fehler mit Exit 1 (C5), die Meldung nennt, wer es verlangte. **G11**: Warnungen aus Abhängigkeiten und `std` werden nicht gezeigt (`--warn-deps`), Fehler immer. | Rust (`#[allow]`, `[lints]`, `-D`), Clippy-Gruppen. Verworfen: `lyric lint` als eigenes Verb (L4); Warnungen ohne Namen neben Lints (zwei Mechanismen). |
| P9 | **Die Lint-Liste** aus W7, je mit rotem und grünem Fall; die Pegel nach W7, dazu mit dem Maintainer (2026-10-19): `module-shadowing` = `allow` (07 K4: „standardmäßig aus“). Namen für die entschiedenen Warnungen ohne Namen: `shadowed-it` (08 F4, `correctness`, warn), `irrefutable-pattern` (SEM0104, `correctness`, warn), `void-binding` (03 M6-5, SEM0173, `correctness`, warn), `exit-code-range` (11 C5, `correctness`, warn), `useless-allow` (09 A10, SEM0180, `unused`, warn), `foreign-conformance` (03 M4-5, `correctness`, warn), `missing-module-docs` (08, `docs`, allow), `wrapped-handle-never-closed` geht in `closeable-never-closed` auf (10 S3: eine Hülle um einen Handle ohne eigenen Namen). Die übrigen heutigen Warnungen bekommen Namen nach ihrer Natur (`useless-try` SEM0139, `visibility-leak` SEM0152, `blanket-shadowing` SEM0168, `iterator-reused` SEM0162, `unreachable-catch` SEM0171, `generator-copies-this` SEM0172, `builtin-type-shadowing` SEM0077, `unused-var` als Lint über SEM0075). | W7; Clippy. |
| P10 | **Fixes (G7)**: `Note.Kind = Suggestion` mit `Replacement` (Span + Text) und `Applicability`; Quellen: die Lints, die eine mechanische Korrektur haben (`unused-import` entfernt, `unused-var` → `let`, `useless-try`, `needless-return`, `self-assignment`, `naming`), `@Deprecated.replacement`, `NameSuggestion` (`needsReview`). **`lyric fix`** wendet die `safe`-Fixes eines Pakets an — alle Dateien, nicht überlappende Ersetzungen, dann ein erneuter Check; `--dry-run` zeigt den Diff, `--json` nach P7; was übrig bleibt, wird gemeldet. `fix --from-4` gehört M16, `fix --edition` einer späteren Edition. | rustc `MachineApplicable`, `cargo fix`. |
| P11 | **Die Optionstabelle (C3)**: jedes Verb deklariert seine Optionen als Daten (Name, Kurzform, Wert, Feldflag, Hilfe); `--help`, die Completions und die Spec-Tabelle in 14 entstehen daraus; unbekannte Optionen = Exit 2 (heute schon). `Program.cs` wird dabei in eine Datei je Verb geteilt. | Cargo (clap), Go `flag`. Verworfen: Hilfe und Completions von Hand — sie driften. |
| P12 | **`lyric check`** (C2, M7-9, BS4, P13 aus M9a): der Bau bis vor die C-Emission — Front-End, Lints, `comptime`-Auswertung (P13: „jeder Bau, der senkt, wertet aus“; `check` senkt nicht nach C, wertet aber `comptime` aus, damit seine Fehler dieselben sind), `build.lyr` einmal, damit `gen/` existiert; `--locked`, `--profile`, `--json`, die Lint-Optionen; Exit 0/1 wie `build`. Keine Ausgabedatei. | `cargo check`, `go vet` nein (L4). |
| P13 | **`lyric new`/`init`** (C2, BS1) — Form mit dem Maintainer (2026-10-19): Cargos. `new <name>` legt das Verzeichnis an, `init` nutzt das aktuelle (und weigert sich, wo schon ein `lyric.toml` liegt): `lyric.toml` (`[package] name`, `version = "0.1.0"`), `src/main.lyr` (Hallo) oder mit `--lib` `src/lib.lyr`, `.gitignore` (`out/`); kein `build.lyr` (BS1). **git**: das Flag `--git`/`--no-git`; ohne Flag entscheidet die Umgebungsvariable **`LYRIC_GIT_ON_NEW`** (`1`/`true` → `git init`, wo kein Repository darüber liegt und `git` gefunden wird), ohne beides kein git. Der Paketname folgt den Namensregeln (15); ein ungültiger ist Exit 2 mit Vorschlag. | `cargo new`/`init`. |
| P14 | **`lyric env`** (C4): jede Einstellung, die ein Bau liest — Toolchain, Ziel, Profil und seine Felder, Cache-Wurzel, `zig`, `LYRIC_*` — mit Wert und **Herkunft** (`command line`, `LYRIC_X`, `lyric.toml`, `config.toml`, `default`); `--json`. **`lyric completions bash\|zsh\|fish\|pwsh`** druckt das Skript aus der Optionstabelle (P11), führt nie etwas aus (BS4). | `go env`, `cargo config get`; clap_complete. |
| P15 | **Die Noten (07, 08, 10)**: M7-3 — scheitert eine Auflösung und hätte ein verborgener Kandidat gepasst, nennt eine `Place`-Note Name, Sichtbarkeit und Modul (Überladungen, Extensions, Member gleich); M7-11 und K4 — scheitert etwas, weil ein Local einen Prelude- oder Modulnamen verdeckt, erklärt eine Note die Verdeckung mit Ort; K7 — ein Modulpfad-Tippfehler bekommt einen Vorschlag (`NameSuggestion` über die Modultabelle, als `Suggestion`, `needsReview`). | rustc „private item … not accessible“, „did you mean“. |
| P16 | **Spec**: 16 bekommt die Regeln zu Record, Codes und Bereichen, Severities, Lints und Ebenen, Präzedenz, Fixes, Text- und JSON-Ausgabe; 14 die Verben `check`, `new`, `init`, `env`, `completions`, `explain`, `fix` mit ihren Optionen (aus P11) und Ereignissen; Anhang A wird erzeugt; die Konformanzfälle der Lints in einem neuen Kapitelverzeichnis `16-diagnostics`. Der Harness der Spec braucht dafür die Direktive `warning: <lint>` (gibt es) und `lint: <name> = <level>` (neu, S5). | wie M11 P14. |

### Slices

| Slice | Inhalt | Artefakt, Prüfung |
|---|---|---|
| **S1** Der Record | P1: `LabeledSpan`, `Note` mit Art, `Applicability`; `Info` fällt; die alten Überladungen als Formen über dem neuen Record; die LSP-Abbildung nimmt Labels als `relatedInformation`; P6: die 15 `IR0001` mit eigenen Codes; der CLI0006-Fehler von `denyWarnings` | alle Suiten grün ohne geänderte Ausgabe außer den IR0001-Stellen; Tests je neuem Code |
| **S2** Die Ausgabe | P2 (Renderer, Farbe, Kürzung, `--absolute-paths`, `--max-errors`), P3 (keine Folgefehler aus einer Deklaration mit Parserfehler) | Golden-Tests der Ausgabe (ein Span, Nebenspans, Noten, mehrzeilig, lange Zeile, Tab, Unicode-Breite); die Goldens der Treiber-Tests neu |
| **S3** Katalog und `explain` | P4 (`diagnostics.toml` mit allen 323 Codes, kurz für alle, lang für Fehler und Lints), `lyric explain`, der Generator des Anhangs; P5 (a)–(c), (e) | `lyric explain LYR-SEM0042` und `explain unused-import`; der Wächter rot, wenn ein Code fehlt (Kontrolle); Spec-PR mit dem erzeugten Anhang A |
| **S4** NDJSON | P7 auf `build`, `run`, `test`; Schema in 16/14 | ein Lauf mit Fehler, Warnung, Panik als Zeilen, die das Schema lesen; Konformanzfall für die Form |
| **S5** Lints: der Mechanismus | P8 — Namen, Gruppen, Ebenen, `[lints]` voll, `@Allow`/`@Warn`/`@Deny` (09 A11 nachgetragen), `-A`/`-W`/`-D`, `--deny-warnings`, Präzedenz, G11 `--warn-deps`; die heutigen Warnungen als Lints; der Spec-Harness `lint:` | je Ort und Ebene ein Test; `deny` → Exit 1 mit Herkunft; `std`-Warnungen still, `--warn-deps` zeigt sie |
| **S6** Lints I: `unused`, `correctness` | P9 — `unused-parameter`, `unused-assignment`, `unused-var`, `unused-generic`, `dead-code`, `assignment-in-condition`, `self-assignment`, `comparison-always`, `float-equality`, `shadowed-local`, `module-shadowing`, `int-literal-adapts`, `shadowed-it`, `exit-code-range`, `foreign-conformance`; je rot/grün | P5 (d) für diese Gruppen grün; Kontrolle: ein Lint ohne Fall → Wächter rot |
| **S7** Lints II: der Rest | `resources` (`unused-task`, die Hülle aus 10 S3), `concurrency` (`global-var-without-sync`), `deprecation` (`until` abgelaufen = Fehler, `deprecated-edition` als Name ohne Auslöser bis zur ersten Edition), `style` (`naming`, `reserved-device-name` deny, `redundant-clone`, `needless-return`, `loop-instead-of-while-true`), `perf` (`string-concat-in-loop` warn, `large-value-copy`, `boxed-interface-in-hot-loop`), `docs` (`missing-docs`, `broken-doc-link`, `missing-module-docs`) | P5 (d) vollständig: jeder Lint rot und grün |
| **S8** Fixes und `lyric fix` | P10 — Suggestions an den Lints mit mechanischer Korrektur, `@Deprecated.replacement`, `NameSuggestion`; `lyric fix` (`--dry-run`, `--json`) | ein Paket mit fünf Fix-Arten wird von `fix` sauber; zweiter Lauf ändert nichts; `needsReview` bleibt stehen |
| **S9** `check`, `env`, Optionstabelle | P11 (eine Datei je Verb, die Tabelle), P12 `check`, P14 `env`; Spec 14 | `check` meldet, was `build` meldet, ohne C; `env` mit Herkunft je Stufe der Leiter |
| **S10** `new`, `init`, `completions`, die Noten | P13, P14 `completions`; P15 (M7-3, M7-11, K4, K7) | `new`/`init` mit und ohne `--git` und `LYRIC_GIT_ON_NEW`; die Completions laden in bash/zsh/fish ohne Fehler; je Note ein Fall |
| **S11** Abschluss | P5 (d) über alles; ein Book-Kapitel als Markdown-Entwurf unter `docs/book/` (Diagnostik, Lints, die Verben — 14 W6 statt `docs/guide`; gerendert ab M12c S2); STATUS, CHANGELOG, Bericht mit offenen Punkten; der M12b-Plan als nächster Schritt | die Zeile oben: „Katalog vollständig, jede Warnung getestet; `lyric fix` wendet `safe`-Fixes an“ |

**Nicht in M12a:**

- die Sprachpunkte — M12b (oben);
- SARIF (Tür nach G8); `lyric fix --from-4` — M16; `--edition` — mit der ersten Edition;
- Code Actions und `codeDescription.href` im LSP — M13b nimmt die Fixes und Katalog-Links aus dem Record;
- `lyric doc`, `fmt`, `api` — M13a; `toolchain`, `lsp`, `dap` — M13b/M15.

### Tests

- **Golden** für die Textausgabe (S2) und die NDJSON-Zeilen (S4), farblos, plattformunabhängig (Pfadtrenner normalisiert).
- **Wächter** (P5) als Tests in `Lyric5.Tests`, gegen den gepinnten Spec-Commit für (d).
- **Konformanz**: je Lint ein roter und ein grüner Fall in `conformance/cases/16-diagnostics/`, `since: 5.0.0`; die Fälle
  für Ebenen und Präzedenz; die Verben in `14-cli`.
- **Kontrollen** je Slice rot gegen den Stand davor (der Lint aus → sein roter Fall schweigt; ein Code aus dem Katalog
  gestrichen → der Wächter rot).

## Nachtrag 2026-10-20 — Website, Book und Doku (Bereich 14)

Der Bereich 14 (`14-website.md`, im Dialog mit dem Maintainer) entscheidet Inhalt, Aussehen und
Technik der Seite und den Plan dafür. Für diesen Plan folgt daraus:

| Was | Folge |
|---|---|
| **M12c neu** | zwischen M12b und M13a (Zeile oben, Slices in 14 D): K2a–K2d, S1–S6. Eigener Meilenstein statt M13a, das von M auf XL wüchse |
| **K1** | der Kommentar-Wächter als Ratchet sofort, als eigener PR während M12a |
| **M12a S11** | schreibt sein Kapitel als Book-Entwurf unter `docs/book/`, nicht in den 4.x-Guide |
| **M13a** | `lyric doc` auf `Lyric5.Doc`; die `# Examples` der std mit den Doc-Tests |
| **M15** | der Mirror auf dem VPS (14 C9) |
| **M17** | der Inhalt des Books statt Guide 5 (12 Q2 geändert) |
| **M18** | `/5.0/` eingefroren, `/` auf das Stable |

## Nachtrag 2026-10-21 — der Plan für M12b

M12b nach seiner Zeile oben und dem Schnitt vom 2026-10-19: die Sprach-Uhren der Reviews, die an M12 hängen, und
was M12a dazu fand. Das Artefakt: **je Punkt ein Konformanzfall, rot gegen den Compiler davor, grün danach**;
die eine Sprachfrage, 04 S7, entschieden. Grundlage ist der Stand nach M12a (#528) und seine offenen Punkte
218, 221–225, 240, 242 (die Liste im Bericht von M12a).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Typargumente am Methodenaufruf** (03 S15a): `it.collect<List<int>>()` wie an einer Funktion; der Parser liest `name<…>(` nach einem `.` als Typargumente, wo die schließende `>` direkt vor `(` steht und dazwischen nur Typen stehen — dieselbe Prüfung wie heute am Funktionsaufruf (`empty<int>()`). | C#, Kotlin. Verworfen: Rusts `::<>` (eine zweite Schreibweise derselben Sache). |
| P2 | **Fixierung in Funktionen** (03 S15a): `fn f<I :: [Iterator<Item = T>], T>(it: I)` leitet `T` aus der Fixierung von `I` ab, wie ein Block (Spec 05 §13 Regel 2) — die Unifikation nimmt die Fixierungen der Schranken als Gleichungen auf; SEM0060 nur, wo danach etwas offen bleibt. | Rusts Projektion `I::Item`. Verworfen: die Regel 03 §9.2.1 zu belassen (Funktion und Block ungleich). |
| P3 | **Geschriebene Typen prüfen ihre Schranken** (03 N2b): ein Durchgang über Signaturen, Felder, Annotationen und Aliase prüft jede geschriebene Instanz gegen die Schranken ihrer Deklaration (`fn f(s: Set<NoHash>)` → SEM0028 an der Signatur), einmal je Stelle; die Prüfung am Gebrauch bleibt für Inferiertes. | Rusts WF-Prüfung. |
| P4 | **Inferenz an den Rändern** (03 S5, S16, T8): ein Lambda, das `T` liefert, bindet `?U` mit `U = T` (`mapNotNull`); ein Literal an einem Parameter, den ein früheres Argument auf `T[N]` band, nimmt `T[N]` als Ziel (`assertEq(x.toBytesBE(), [2u8, 1u8])`); ein Literal unifiziert mit dem Typparameter, an dem es steht, nachdem die übrigen Argumente ihn gebunden haben (T8: erst die Nicht-Literale, dann die Literale als Ziel). | Kotlin, C#. Verworfen: Whole-Function-Inferenz (T8). |
| P5 | **Die Kettenmeldung und das Tiefenlimit** (03 T19; Punkt 218): die Monomorphisierung zählt die Tiefe je Instanziierungskette (128, wie Rust) und meldet bei Überschreitung die Kette (`grow<W<W<…>>>` mit den ersten und letzten Gliedern) statt zu laufen — heute läuft `lyric5 build` bei `fn grow<T>(x: T) { grow(W<T> { … }) }` ohne Ende. Ein Instanzlimit je Programm mit eigener Meldung. | Rust `recursion_limit`. |
| P6 | **Arität über Rumpf und Blöcke** (04 R4a3): zwei Funktionen eines Namens mit verschiedener Anzahl dürfen im Rumpf, in inhärenten Blöcken und über Rumpf und Block stehen (08 §1.2 Regel 1); Spec 05 §3 Regel 1 heißt „eine Funktion je Name und Anzahl“; SEM0121 nur noch für gleiche Anzahl. | — |
| P7 | **Der generische Weiterleiter** (04 R4b): ein an ein Feld delegiertes generisches Member bekommt einen synthetisierten generischen Weiterleiter, eine Instanz je Gebrauch; dazu die Delegation an ein Feld vom Typ eines Kind-Interfaces (der Weiterleiter geht über die Elternmethode) und der Interface-Wert aus einer Shape- oder Blanket-Konformanz (SEM0230; die Methodentabelle der Konformanz je Instanz). | — |
| P8 | **Generische Interfaces mehrfach** (A8, SEM0082) **und mehrere `From`** (04 S7): **zu entscheiden mit dem Maintainer vor S8**. Empfehlung: Konformanzen einer generischen Schnittstelle mit verschiedenen Argumenten unterscheiden ihre Member an der Instanz (`Label :: [From<int>, From<string>]`, `Label.from(3)` wählt über den Argumenttyp wie eine Überladung, `From<int>.from(3)` qualifiziert) — wie Rusts mehrere `From` für einen Fehlertyp; D4 („ein Name, eine Bedeutung je Anzahl“) gilt dann je Konformanz. Die Alternative: D4 bleibt, ein Fehlertyp mit mehreren Quellen nimmt ein Enum (der Stand). | Rust. Die Entscheidung bestimmt S8. |
| P9 | **Die Lücken, die M12a fand**: die ICEs 221–225 werden Fehler mit Code (die Einfuhr einer Einfuhr ist RES0009; eine generische statische Methode als Wert; der qualifizierte Aufruf eines statischen Interface-Members; `comptime if` als Wert; `==` auf `void` im Checker abgelehnt); ein Typfehler an einer Bindung zeigt auf den Initialisierer, mit Label (242); die Note zu verborgenen Kandidaten auch für Überladungen (240). | — |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** Typargumente am Methodenaufruf | P1 | `collect<List<int>>()` und `parse<int>()` an Methoden; ein Vergleich `a.b < c > (d)` bleibt einer (Fall) |
| **S2** Fixierung in Funktionen | P2 | `fn f<I :: [Iterator<Item = T>], T>` ohne SEM0060 am Aufruf |
| **S3** Schranken geschriebener Typen | P3 | `fn f(s: Set<NoHash>)` rot an der Signatur, einmal |
| **S4** Inferenz an den Rändern | P4 | `mapNotNull`, `assertEq` mit `T[N]`, das Literal als Ziel — je ein Fall |
| **S5** Kettenmeldung | P5 | `grow` endet mit einer Meldung, die Kette darin; 218 zu |
| **S6** Arität über Blöcke | P6 | die drei Stellungen grün, gleiche Anzahl bleibt SEM0121 |
| **S7** Weiterleiter und Interface-Werte | P7 | delegiertes generisches Member, Kind-Interface-Feld, Interface-Wert aus Shape und Blanket — SEM0230 fällt |
| **S8** mehrere Konformanzen | P8 nach der Entscheidung | je nach ihr: `From<int>` und `From<string>` an einem Typ, oder ein Fall, der D4 hält |
| **S9** Lücken aus M12a | P9 | fünf ICEs als Fehler mit Code, die Bindung mit Label, die Überladungsnote |
| **S10** Abschluss | STATUS, CHANGELOG, Bericht, der Plan für M12c als nächster Schritt (14 S1–S6, K2a–K2d) | die Zeile oben |

Nach Aufwand: groß S5, S7, S8; mittel S1, S2, S3, S4, S9; klein S6, S10. Zusammen **M an der oberen Kante**.
**Reflexion**: Die Punkte teilen einen Kern — die Unifikation (P2, P4) und die Monomorphisierung (P5, P7) —, und
die Reihenfolge baut auf ihm auf: erst was der Parser liest (S1), dann was die Unifikation bindet (S2, S4), dann
was die Instanzen erzeugen (S5, S7). P8 steht spät, weil seine Entscheidung die Überladung über Konformanzen
berührt, die S6 erst ordnet. Verworfen: die Inferenzpunkte einzeln über Sonderfälle zu lösen (S5 und S16 sind
derselbe Mangel — der erwartete Typ fließt nicht in ein schon gebundenes Argument zurück).

## Nachtrag 2026-10-22 — M12b abgeschlossen; ein Slice vor M12c

M12b ist auf `main` (S1–S9, #529–#538; S9 vor S8, weil S8 auf die Entscheidung wartete). Mit dem Maintainer
entschieden am 2026-10-22:

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| 04 S7 | **Mehrere Konformanzen einer generischen Schnittstelle**: `Label :: [From<int>, From<string>]`; Member gleicher Arität an der Instanz unterschieden, der Aufruf wählt über die Typen der Argumente, die eigenen Typen zuerst; über eine Schranke die Instanz der Schranke (gebaut in S8, Spec 05 §3 Regel 5) | Rusts mehrere `From`. Verworfen: die qualifizierte Form `From<int>.from(…)` — ein statisches Member wird nicht über sein Interface gerufen (05 §4 Regel 2, S9) |
| 233 | **Keine Lint-Stufe für ein Modul oder eine Datei**: Deklaration, `[lints]`, Kommandozeile (09 A10 nachgetragen) | Rusts `#![allow]` — ein zweiter Mechanismus für dieselbe Sache |
| 255 | **Ein Typtest sieht die Konformanzen, die ein Blanket- oder Shape-Block gibt** (Option C): für jedes Interface `J`, nach dem ein Programm mit `x is J` oder einem Typmuster `d: J` fragt, baut die Senkung die Zeilen der Blanket- und Shape-Konformanzen aller Typen, die in ein `Any` gelangen; ein Interface-Wert behält die Regel von S7 (Zeilen, wo einer gemacht wird) | A: `is` sieht nur Erklärtes — eine Asymmetrie, die der Compiler bei `Any` nicht anmahnen kann. B: Zeilen für jede festgehaltene Konformanz — Größe in jedem Programm, und die erste S7-Fassung zerbrach damit die Zeilen der std-Iteratoren |

**Der Slice für 255** steht zwischen M12b und M12c: zuerst die Ursache, warum Zeilen für jede festgehaltene
Konformanz die std-Iteratoren zerbrachen (`sizeHint` „not lowerable“); dann die Sammlung der gefragten
Interfaces (`TypeTestExpr`, Typmuster) in der Senkung, die Zeilen für sie, ein Konformanzfall (`a is Describe`
auf einem `Any`, das nie in `Describe` gewandelt wurde) und die Regel in Spec 05 §9. Artefakt: der Fall, rot gegen
`main`, grün danach. Größe S. Danach M12c nach Bereich 14 (S1–S6, K2a–K2d).


## Nachtrag 2026-10-24 — der Plan für M13a

M13a nach seiner Zeile oben, 11 E5 und E7, 07 B3 und dem Teil „in M13a“ von 14 D: **`lyric fmt`**, **`lyric doc`**
mit den Doc-Tests und **`lyric api`**. Das Artefakt: **das Repo hält `lyric fmt --check`, und die std steht als
Referenz unter `/dev/std/`**, ihre `# Examples` laufen als Tests. Grundlage ist der Stand nach M12c (#555): der
4.x-Formatierer (`src/Lyric.Frontend/Formatting`, Doc-Algebra, Kommentare nach ihrer Stellung, Breite 100), die
Lints `missing-docs` und `broken-doc-link` (M12a), der Renderer `Lyric5.Doc` mit Referenzseite und Snippet-Arten
(M12c S1, S2, S4).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Ein Formatierer, der vorhandene**: der AstFormatter des Frontends lernt die Lyric-5-Syntax (Trailing-Lambdas, `match`-Arme mit Block, Attribute, Typargumente am Aufruf, `comptime`, Makros, Initialisierer, `::`-Listen …). Zuerst gemessen: der Formatierer über den Korpus — `stdlib5`, `tests/std`, `examples`, die Konformanzfälle am Pin, die Book-Snippets —, je Datei: parst die Ausgabe, ist sie gleich dem Eingang bis auf Leerraum, wo nicht. Was der Formatierer nicht kennt, wird ein Slice-Punkt, nicht eine Vermutung | gofmt, zig fmt. Verworfen: ein zweiter Formatierer für Lyric 5 neben dem des 4.x-Frontends — zwei Formen derselben Syntax |
| P2 | **Eine Form, keine Optionen** (E5, E6 A): Breite 100, Einrückung 4, die Regeln des 4.x-Formatierers, wo Lyric 5 sie nicht ändert; **Kommentare und Klammern bleiben** (Trivia, die Klammer als Knoten); eine Datei, die nicht parst, bleibt unangetastet | Verworfen: `.lyricfmt`-Optionen |
| P3 | **Zwei Prüfungen über den Korpus**: Idempotenz (`fmt(fmt(x)) == fmt(x)`) und **Semantik-Erhalt** — Original und formatierte Fassung zu C (`--emit c`), verglichen ohne `#line`; für Dateien ohne Programm (Bibliotheksmodule) der AST ohne Spannen. Beide als Tests in der Suite, über den ganzen Korpus | rustfmt's Idempotenz-Test, `go vet`. Verworfen: nur Goldens — sie zeigen nicht, dass sich die Bedeutung hielt |
| P4 | **`lyric fmt [<dateien>…] [--check]`**: ohne Dateien das Paket (Quelle, Tests, `build.lyr`); `--check` schreibt nichts und endet mit 1, wo eine Datei anders würde, und nennt sie; die Book-Snippets formatiert `fmt` nicht (sie stehen in Markdown), die Suite prüft sie wie den Korpus. **Das Repo hält es**: ein CI-Schritt über `stdlib5`, `tests/std`, `examples` | `cargo fmt --check` |
| P5 | **Formvertrag** (E30 A): eine Formänderung nach S3 nur für einen Bedeutungsfehler oder eine dokumentierte Regel, mit CHANGELOG-Satz; die Formatierer-Goldens zeigen sie | gofmt's Versprechen |
| P6 | **`lyric doc`**: aus der Sema des Lyric-5-Frontends die `pub`-Deklarationen eines Pakets mit ihrem `///`, ihre Signaturen (mit dem Highlighter), Member, Konformanzen; je Modul eine Seite, je Typ eine (14 P6: `std/<modul>/<Typ>/`, Anker `#method.push`); die Referenzseite in drei Spalten (14 B10); **ein Paket ohne Site-Navigation** in `out/doc/` (14 C1), die std **mit** ihr unter `/dev/std/` über `tools/Site`. Abhängigkeiten auf Wunsch (`--deps`) | rustdoc. Verworfen: JSON + externer Renderer (14 P1) |
| P7 | **Intra-Doc-Links** `[Type]`, `[Type.member]`, `[module.Type]` zu Links auf die Seite und den Anker; was nicht auflöst, ist `broken-doc-link` (der Lint aus M12a, dieselbe Auflösung) | rustdoc |
| P8 | **Doc-Tests** (X5, 14): ` ```lyric `-Blöcke in `///` mit den Snippet-Arten des Books (`no_run`, `compile_fail=CODE`, `output`) laufen in `lyric test` — je Block ein Test, benannt nach Element und Zeile; ein Block ohne `fn main` wie im Book gewickelt, mit dem Modul des Elements importiert | rustdoc's Doc-Tests |
| P9 | **Die `# Examples` der std**: die Modul-Docs und die wichtigsten Typen jedes Moduls bekommen ein ausführbares Beispiel (14 W8) — nicht jede Funktion; `missing-docs` bleibt die Uhr für den Rest | Rusts std |
| P10 | **`lyric api`** (07 B3, E7): die öffentliche Fläche eines Pakets als Text (eine Zeile je Element, sortiert, deterministisch) und `--json`; **`--diff <rev>`** vergleicht mit der Fläche an einem Git-Stand oder einer veröffentlichten Version und ordnet jede Änderung ein (entfernt, Signatur geändert — brechend; hinzugefügt — verträglich); Endcode 1 bei Brechendem | `cargo public-api`, `cargo semver-checks`. Verworfen: eine Semver-Prüfung, die Verhalten errät |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** Messung und Lyric-5-Syntax | P1, P2 | der Korpus-Bericht vorher; nachher parst jede formatierte Datei, Kommentare und Klammern stehen |
| **S2** Idempotenz und Semantik-Erhalt | P3 | die zwei Korpus-Tests grün; eine absichtlich falsche Regel wird rot (Kontrolle) |
| **S3** `lyric fmt` und das Repo formatiert | P4, P5 | `lyric fmt --check` grün über `stdlib5`, `tests/std`, `examples` als CI-Schritt; der Formatierungs-Commit ohne Codeänderung (die emittierte C gleich) |
| **S4** `lyric doc` | P6 | `lyric doc` schreibt die Seiten eines Pakets; Goldens je Seitenform |
| **S5** Intra-Doc-Links und die std auf der Seite | P6, P7 | `/dev/std/` live über `site.yml`, die Linkprüfung über die std-Seiten grün |
| **S6** Doc-Tests | P8 | `lyric test` läuft die `///`-Blöcke; ein falsches `output` wird rot |
| **S7** die `# Examples` der std | P9 | je Modul ein Beispiel, das als Doc-Test läuft |
| **S8** `lyric api` | P10 | `lyric api` der std; `--diff` gegen einen früheren Stand nennt eine entfernte Funktion brechend |
| **S9** Abschluss | STATUS, CHANGELOG, Bericht, der Plan für M13b als nächster Schritt | die Zeile oben |

Nach Aufwand: groß S1, S4; mittel S2, S3, S5, S6, S8; klein S7, S9 (S7 ist Text, aber viel davon). Zusammen **M an
der oberen Kante**, wie die Zeile sagt. Die Reihenfolge folgt den Abhängigkeiten: der Formatierer zuerst (S3
formatiert das Repo einmal — danach sind alle Diffs klein), dann die Doku über den Renderer aus M12c, die
Doc-Tests über die Snippet-Arten des Books, `lyric api` zuletzt, weil es die Fläche liest, die `lyric doc` schon
sammelt.

**Reflexion**: Der Formatierer ist der Punkt mit dem größten Risiko und dem kleinsten Spielraum — eine falsche
Regel ändert Bedeutung oder Kommentare in jedem Paket. Deshalb misst S1, bevor es baut, und S2 prüft, bevor S3
das Repo umschreibt. Gos Erfahrung (eine Form, früh, für alle) spricht dafür, das Repo in S3 einmal ganz zu
formatieren statt Datei für Datei; Rusts (Optionen, die rustfmt nie wieder los wurde) gegen jede Option. `lyric
doc` und `lyric api` teilen eine Sammlung der Fläche — rustdoc und `cargo public-api` lesen beide dieselbe
JSON-Form; hier ist es eine C#-Struktur in `Lyric5.Doc`, aus der die Seiten und die Textform entstehen.

## Nachtrag 2026-10-25 — der Plan für M13b

M13b nach seiner Zeile oben, 11 E1–E4 und E8, 09 Q5/Q8 für den Editor und 12 O4: **`lyric lsp`**, **`lyric
dap`**, **`tree-sitter-lyric`** und die **Clients auf 5**. Das Artefakt: **im Editor Hover, Completion, Rename und
ein Fix über eine Lyric-5-Quelle, Tests per CodeLens; der Debugger hält an einer Panik und zeigt Lyric-Werte;
das Risiko lldb auf Windows gemessen**. Grundlage ist der Stand nach M13a (#566): der 4.x-Sprachserver
`src/Lyric.Lsp` (`lyrlsp`, ~5 300 Zeilen: JSON-RPC, Dokumente, 15 Provider über das `SemanticModel` des Frontends,
`Lyric.Tests.Lsp`), das gemeinsame Frontend mit `SourceCompiler.Check`/`CheckProject`, `Pipeline` mit Manifest
und Paketgraph, der Diagnose-Record mit Fixes und Katalog-Code (M12a), `lyric test --json`, `lyric fmt` (M13a),
die Mangling-Regel `lyr_<modul>_<name>` (01 C4) und die Laufzeit-Layouts (`RuntimeLayout`).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Ein Server, der vorhandene, auf 5** (E1 „kein zweites Frontend“): `Lyric.Lsp` wird der Server der 5-Linie — die Einheit kommt aus `lyric.toml` über `Pipeline` (Manifest, Paketgraph, `tests/` mit dem Paket, `build.lyr` als eigene Einheit „Build-Skript“, BS3), eine Datei außerhalb jedes Manifests ist Skript-Modus (C8), die std ist `stdlib5` bei der Toolchain. **`lyric lsp`** im selben Binary (C1); `lyrlsp` und die `lyric.json`-Einheit fallen (Lyric-Script hat seinen Server im eigenen Repo, 12 O4) | gopls, rust-analyzer. Verworfen: ein neuer Server neben dem alten — 15 Provider zweimal |
| P2 | **Erst gemessen, dann die Wiederverwendung** (E1 B): S1 misst die Zeit je Analyse über drei Größen — eine Datei, ein Paket mit ~20 Modulen, die std selbst als Paket — und teilt sie in Parse, Auflösung, Sema; die Zahl entscheidet, wo der Schnitt liegt. Die Vorgabe des Designs: **je Modul Parse+Sema, Schlüssel der Modulgraph** — ein Modul, dessen Text und dessen Abhängigkeiten gleich sind, wird nicht neu geprüft; die std zuerst, weil sie jede Analyse trägt (4.x: 93–106 ms je 5-Zeilen-Datei, von der std dominiert). Was das Frontend dafür ändern muss, nennt S1 als Bericht, nicht als Vermutung | gopls (Paket-Granularität), Go's Export-Daten. Verworfen: Salsa/Roslyn-Feinkörnigkeit (E1); Ganz-Compile je Tastendruck ohne Zahl |
| P3 | **Abbrechbar und inkrementell** (E1): ein `CancellationToken` durch die Modulschleife des Checks — eine neue Eingabe bricht die laufende Analyse zwischen zwei Modulen ab, nicht nur ihr Ergebnis (4.x verwarf nur); inkrementeller Text-Sync; **Pull-Diagnosen** (`textDocument/diagnostic`, `workspace/diagnostic`) neben dem Push für alte Clients. Die Diagnosen sind die von `lyric check` — ohne Monomorphisierung und Senkung, **mit** Auswertung (Q8), im Editor unter einem **kleineren Budget** (Q5 „strenger“), das das Manifest nicht hebt | rust-analyzer's Cancellation |
| P4 | **Fähigkeiten auf 5** (E2): Hover, Completion aus dem Schnappschuss, Signaturhilfe, Definition/Referenzen/Rename paketweit **und in Abhängigkeiten** (Quelle, L7; `std` bei der Toolchain, schreibgeschützt), Inlay-Hints, semantische Tokens, Symbole, Folding — jeder Provider über die Lyric-5-Syntax geprüft (Trailing-Lambdas, `match`-Blöcke, `comptime`, Makros, Initialisierer, `::`-Listen); was ein Provider nicht kennt, wird ein Slice-Punkt | rust-analyzer |
| P5 | **Code Actions aus dem Record** (G7, M12a): jede Diagnose mit Fix wird eine Quick-Fix-Aktion mit genau den Edits, die `lyric fix` schreibt; `codeDescription.href` auf die Katalogseite des Codes (`DiagnosticCatalog.PagePath`, dieselbe, die `lyric explain` nennt); `@Allow(code)` als Aktion für Lints. **Formatierung als minimale Edits** (E29 B): `lyric fmt`s Form, als Differenz zum Puffer, nicht als Ganz-Ersatz; `lyric expand` als Server-Kommando (Q7) | gopls, clippy-Fixes über rust-analyzer |
| P6 | **Tests im Editor** (E2, E24): CodeLens „Run test“/„Debug test“ über jedem `@Test` und jedem Doc-Test-Block, `lyric/tests` liefert die Tests eines Dokuments, ausgeführt über `lyric test --json -f <name>` — der Server führt nichts selbst aus | rust-analyzer's Runnables |
| P7 | **`lyric dap` = Adapter über lldb-dap** (E4, L12): der Adapter sucht `lldb-dap` (Pfad, `LYRIC_LLDB_DAP`, die Xcode-/LLVM-Orte), baut das Ziel im Debug-Profil mit Debug-Info und reicht DAP durch; er setzt die Startkommandos (`command source lyric.lldbinit`), übersetzt `launch`/`attach` (Programm, Argumente, `runInTerminal`) und meldet **„Panic“ als Exception-Breakpoint-Filter**, der ein Breakpoint auf `lyr_panic` ist. Bedingte Breakpoints, Logpoints, Attach sind lldb's — nativ, nicht nachgebaut (4.x ignorierte `condition` still) | `rust-lldb`, CodeLLDB, delve. Verworfen: ein eigener Debugger (01 L12) |
| P8 | **Lyric-Werte in lldb** (E4): `lyric.lldbinit` mit Python-Summaries und synthetischen Kindern für `string`, Listen, `Map`/`Set`, `?T`, `Result`, Enums mit Payload, Klassen/Structs, Tupel und Closures; Rahmen- und Funktionsnamen entmangelt (01 C4) — **die Layouts aus `RuntimeLayout` erzeugt**, nicht von Hand, damit eine Layoutänderung die Summaries mitnimmt; die Datei liegt bei der Toolchain. **Tasks**: OS-Thread = DAP-Thread; die schwebenden Koroutinen eines Threads als Scope „Tasks“ über ein lldb-Kommando, das die Koroutinentabelle der Laufzeit liest (E15 A) | `rust-lldb`'s Formatter, Go's `runtime-gdb.py` |
| P9 | **Windows gemessen, nicht versprochen** (E4): `lyric dap` auf `x86_64-windows-gnu` mit dem lldb-dap einer LLVM-Release — hält es an einem Breakpoint, zeigt es lokale Werte, hält es an `lyr_panic`; das Ergebnis steht im Bericht, ein Fehlschlag macht `-windows-msvc` + PDB zur benannten Tür nach 5.0, nicht zum Slice | — |
| P10 | **`tree-sitter-lyric`** (E8, 12 O4): ein Repo im Org mit der Grammatik der 5-Syntax, `highlights.scm`, `locals.scm`, `injections.scm`; **die Korpusprüfung**: jede Datei aus `stdlib5`, `tests/std`, `examples`, den Konformanzfällen am Pin und den Book-Snippets parst ohne `ERROR`-Knoten — dieselbe Liste wie M13a P1. Die Grammatik ist die Hervorhebungsquelle; die TextMate-Grammatik der Clients wird **aus ihr erzeugt** (Schlüsselwörter, Operatoren, Literalformen), damit es eine Quelle gibt; semantische Tokens vom Server liegen darüber (E13) | Zig, Rust (`tree-sitter-rust`). Verworfen: TextMate von Hand neben Tree-sitter — zwei Wahrheiten |
| P11 | **Die Clients auf 5** (E8): `vscode-lyric` und `jetbrains-lyric` je **2.0.0** — starten `lyric lsp` und `lyric dap` (kein `lyrlsp`, kein `lyrdbg`), `.lyr` mit der erzeugten Grammatik, die CodeLens-Befehle, die Debug-Konfiguration „Lyric“ (launch, attach, Panik-Filter); dünn: kein Wissen über Lyric, das nicht der Server hat. Die Snippets der Clients werden gegen `lyric check` geprüft (ein Snippet hat schon einmal nie geparst). Releases wie bisher als Asset an einem Tag, kein Marketplace | Zig, Rust |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** der Server auf 5 und die Messung | P1, P2 | `lyric lsp` beantwortet `Lyric.Tests.Lsp` über Lyric-5-Pakete (`lyric.toml`, std 5); der Messbericht je Größe und Phase |
| **S2** Wiederverwendung je Modul | P2, P3 | eine Änderung in einem Modul prüft nur es und die, die es importieren — die std nie; die Zeit je Tastendruck gemessen vorher/nachher; Abbruch zwischen Modulen; Pull-Diagnosen |
| **S3** die Fähigkeiten über die 5-Syntax | P4 | je Provider ein Test über Lyric-5-Konstrukte; Rename über zwei Pakete und eine Abhängigkeit; Hover über ein std-Element zeigt sein `///` |
| **S4** Code Actions, Formatierung, Expand | P5 | ein Quick-Fix aus dem Record schreibt dieselben Edits wie `lyric fix`; Formatierung als minimale Edits; Katalog-Link je Diagnose |
| **S5** Tests im Editor | P6 | CodeLens über `@Test` und Doc-Test-Blöcken; `lyric/tests`; ein Lauf über `lyric test --json` |
| **S6** `lyric dap` | P7 | ein DAP-Skripttest (Linux, macOS): launch, Breakpoint, Schritt, Stack mit entmangelten Namen, Halt an einer Panik |
| **S7** Lyric-Werte und Tasks | P8, P9 | `lyric.lldbinit` aus den Layouts erzeugt; lldb im Batch-Modus zeigt eine Liste, eine Map, ein Enum, ein `?T` als Lyric-Werte; der Scope „Tasks“; der Windows-Lauf gemessen |
| **S8** `tree-sitter-lyric` | P10 | das Repo mit Grammatik, Abfragen und CI; der Korpus parst ohne `ERROR`; die erzeugte TextMate-Grammatik |
| **S9** die Clients | P11 | `vscode-lyric` 2.0.0 und `jetbrains-lyric` 2.0.0 veröffentlicht; die Checkliste der Clients einmal gegen das Release gefahren |
| **S10** Abschluss | STATUS, CHANGELOG, Bericht, der Plan für M14 als nächster Schritt | die Zeile oben |

Nach Aufwand: groß S1, S2, S7; mittel S3, S4, S6, S8, S9; klein S5, S10. Zusammen **XL**, wie die Zeile sagt —
der größte Brocken der Werkzeuge. Die Reihenfolge folgt den Abhängigkeiten und dem Risiko: der Server zuerst auf
5 gestellt und gemessen (S1), weil jede weitere Zahl auf ihm steht; die Wiederverwendung (S2) vor den
Fähigkeiten, weil ein Provider-Test über einen 2-Sekunden-Check niemand fährt; der Debugger danach als eigener
Strang (S6, S7), die Grammatik und die Clients zuletzt, weil sie alles Vorige brauchen. S6–S8 hängen nicht an
S2–S5 und dürfen davor gezogen werden, wo ein PR in CI wartet.

**Was der Maintainer freigibt, bevor der Slice läuft**: S8 legt ein neues Repo im Org an (`lyriclang/
tree-sitter-lyric`) und lädt die Tree-sitter-CLI; S9 veröffentlicht zwei Client-Releases. Beides fragt am
Slice-Anfang.

**Reflexion**: Der Sprachserver ist da — die Gefahr ist nicht das Bauen, sondern das Messen auszulassen. 4.x hat
jeden Tastendruck die Welt kompiliert und es nie gezählt, bis die Bestandsaufnahme 0,3–1,9 s je Projekt fand;
deshalb misst S1, bevor S2 einen Cache baut, und der Cache bekommt die Granularität, die die Zahl verlangt —
gopls fährt mit Paketen gut, rust-analyzer brauchte Salsa erst für Makros und Traits über Crate-Grenzen, die
Lyric so nicht hat. Der Debugger ist bewusst **kein** eigener: lldb kennt DWARF, Threads, bedingte Breakpoints
und Attach, und was Lyric dazu gibt, sind Namen und Werte — Rust und Go tun genau das mit Python-Formattern. Die
Gefahr dort ist Drift: eine Layoutänderung der Laufzeit, die die Summaries still falsch macht; deshalb erzeugt
S7 sie aus `RuntimeLayout` und prüft sie mit lldb im Batch-Modus. Die Tree-sitter-Grammatik ist der zweite Parser
der Sprache und darum der riskanteste Text des Meilensteins — die Korpusprüfung über dieselben Dateien, die der
Formatierer schon bestand, hält sie ehrlich; eine TextMate-Grammatik von Hand daneben wäre der Fehler, den die
Regel „ein Mechanismus pro Konzept“ verbietet.

### Nachtrag 2026-10-25 — S1 gemessen: die std trägt jede Analyse

`lyric lsp` analysiert ein Paket so, wie `lyric test` es kompiliert (`src/` und `tests/`, der Graph offline,
kein Lock geschrieben); eine Datei außerhalb jedes Pakets allein mit der std. Gemessen je Analyse mit
`CheckProject` und seinen neuen Phasenzeiten (Release, 8 Kerne, WSL; warm = Median aus sechs Läufen nach
dem ersten):

| Einheit | Dateien | kalt | warm | warm je Phase (ms) |
|---|---|---|---|---|
| eine Datei (`println`) | 199 | 2715 ms | 812 ms | parse 0, resolve 95, check 391, evaluate 105, lower 201, lint 20 |
| ein Paket, 21 Module | 153 | 426 ms | 431 ms | parse 4, resolve 70, check 224, evaluate 35, lower 75, lint 22 |
| eine Datei, die 31 std-Module importiert | 429 | 2193 ms | 1609 ms | resolve 245, check 753, evaluate 291, lower 285, lint 35 |

„Dateien“ zählt die Quellen der Kompilation: die std-Module und die synthetisierten Konformanzen machen fast
alle aus. 4.x brauchte 93–106 ms je 5-Zeilen-Datei; Lyric 5 braucht für dieselbe Größe das Achtfache, weil die
std größer ist und jede Analyse sie **ganz** prüft, auswertet und senkt. Daraus für S2:

- **Die std ist der Schnitt.** Ihre Module hängen nicht vom Paket ab; geprüft einmal je Toolchain und
  wiederverwendet, fallen resolve und check auf den Anteil des Pakets (im 21-Modul-Paket ~60 von 300 ms).
  Was der Checker dafür braucht — die Tabellen eines Moduls ohne die Instanzen, die erst das Paket
  verlangt, und die synthetisierten Konformanzen der Pakettypen in den std-Interfaces —, klärt S2 zuerst
  als Bericht.
- **Senken gehört nicht in den Editor**: `lower` sind 25 % der Einzeldatei; die Meldungen des Senkens
  (`LYR-IR…`) sind die einzigen, die verloren gingen. S2 misst, welche das sind, bevor es den Schritt
  streicht — 11 E1 schreibt „ohne Monomorphisierung/Senkung“.
- `evaluate` (Q8) bleibt, mit dem kleineren Budget des Editors.

Neben der Messung: Referenzen und Rename fanden Stellen in synthetisiertem Code (`<synthesized Debug for
main.Point>`) und hätten dort editiert — jetzt ausgeschlossen; Hover schreibt Signaturen noch in 4.x-Form
(`fn println<T>(T) -> void`) — ein Punkt für S3.

### Nachtrag 2026-10-25 — S2: geteilt wird die Parse, nicht die Prüfung

**Befund vor dem Bauen** (P2 verlangte ihn als Bericht): die Wiederverwendung je Modul, die 11 E1 B
beschreibt — ein Modul, dessen Text und Abhängigkeiten gleich sind, wird nicht neu geprüft —, setzt voraus,
dass das Ergebnis der std nicht vom Paket abhängt. **Das stimmt für Lyric 5 nicht**, und nicht aus einer
Schwäche des Checkers, sondern aus der Sprache:

- ein Konformanzblock erreicht jedes Modul ohne Import (04 Regel 10, `Compilation.Reaches`): ein Block
  des Pakets auf einem std-Typ ist im Rumpf einer std-Funktion sichtbar und kann dort entscheiden, welches
  Member ein Aufruf trifft; die Kohärenz („eine Konformanz je Typ und Interface im Programm“) ist eine
  Frage über das ganze Programm;
- jede geschriebene Länge `T[n]` des Pakets synthetisiert Blöcke **in std.core**;
- der Checker schreibt Antworten des Pakets in std-Symbole (`AssociatedTypeSymbol.Bind` je Konformer,
  die Felder eines `ExtensionBlock`), und welche std-Module überhaupt geladen werden, hängt von den
  Importen des Pakets ab.

Eine geprüfte std aus einer Kompilation in die nächste zu tragen hieße, die Tabellen des Checkers in
„std allein“ und „was das Programm hinzufügt“ zu teilen und jede Antwort des zweiten Teils als
Abhängigkeit zu verfolgen — das ist Salsa, und das hat 11 E1 verworfen. rust-analyzer braucht es aus
genau diesem Grund (Traits über Crate-Grenzen); gopls kommt ohne aus, weil Go keine Methoden außerhalb
des Pakets eines Typs kennt. **Entscheidung (nach Empfehlung, zur Bestätigung)**: E1 B wird für 5.0
**auf die Parse** beschränkt; die geprüfte std ist eine Tür mit Uhr nach 5.0, Voraussetzung: der Checker
trennt seine Tabellen nach Herkunft.

**Gebaut:**

- **`StdlibSeed`**: die std einmal geparst — je Prozess, im Hintergrund beim Start des Servers —, jede
  Kompilation beginnt mit einer Kopie seines Quellverwalters (`SourceManager.Fork`, die std unter
  denselben `FileId`s) und bekommt die Bäume statt der Dateien. Eine Datei, die sich seit der Parse
  geändert hat, und eine, die der Editor offen hält, werden gelesen wie ohne Seed.
- **Kein Senken im Editor** (`CompilerOptions.Lower`): Prüfung, Auswertung, Lints — was nur das Senken
  findet (`LYR-SEM0197`), sagt der Build (14 §11 Regel 3).
- **Abbruch zwischen den Phasen** (`CompilerOptions.Cancellation`): eine neue Eingabe hält die laufende
  Analyse nach dem Auflösen, nach der Prüfung und nach der Auswertung an.
- **Inkrementeller Text-Sync** und **Pull-Diagnosen** (`textDocument/diagnostic`) für einen Client, der
  sie verlangt; die Dateien, die er nicht offen hat, werden weiter geschickt. `workspace/diagnostic`
  nicht: die Push-Meldungen decken es.
- **Ein Fehler im Protokoll, gefunden beim Messen** (auch in 4.x): eine Diagnose, deren Analyse eine
  neuere Eingabe überholte, brach **nach dem Header** ab — jede folgende Nachricht wurde an der falschen
  Stelle gelesen, die Verbindung war tot. Das Token bricht jetzt das Warten auf den Strom ab, nie das
  Schreiben.

**Gemessen** — die Latenz vom Edit bis zur Diagnose im NativeAOT-Binary (`lyric5 lsp`, Median aus 30
Edits, die 50 ms Entprellung eingeschlossen; S1 = main vor diesem Slice):

| Einheit | S1 | S2 |
|---|---|---|
| eine Datei (`println`) | 510–585 ms | 400–490 ms |
| ein Paket, 21 Module | — (die Verbindung brach, s. o.) | 320–340 ms |

Im Prozess, nach dem JIT warm (die Messung aus S1, wiederholt): eine Datei 370 → 251 ms, das Paket
263 → 186 ms, alle std-Module 1290 → 965 ms. Übrig bleibt die Prüfung der std (~190 ms der einen
Datei) — der Teil, den nur die Tür oben wegnähme.

**Nicht gebaut, mit Uhr:** das kleinere Budget der Auswertung im Editor (P3, 09 Q5). Eine Auswertung,
die im Editor am Budget scheitert, im Build aber nicht, wäre eine Meldung, die `lyric check` nicht
sagt — gegen 14 §11 Regel 3; der Editor müsste eine solche Stelle **ungewertet lassen**, ohne Meldung.
Das braucht den Abbruch im Interpreter ohne Diagnose und kommt mit **S3**.

### Nachtrag 2026-10-25 — S3: die Fähigkeiten über den Korpus

Statt je Provider eine Liste von Lyric-5-Formen zu raten, läuft **jeder Provider über die Lyric-5-Pakete des
Repos** — die Tests der std (71 Dateien) und die drei Beispiele — **an jedem Namen**, den sie schreiben
(`ProviderCorpusTests`, wie der Korpus des Formatierers in M13a): kein Provider wirft, eine Definition und
jede Referenz liegt in einer geschriebenen Datei und nennt den Namen unter dem Cursor, Gliederung, Faltung,
Inlay-Hints und semantische Tokens halten die Regeln des Protokolls. Gefunden und behoben:

- ein **Modul** als Definition markierte seine ganze Datei; jetzt ihr Anfang, nichts markiert;
- das **Feld einer Varianten-Nutzlast** (`.Other { code = 0 }`) hat kein eigenes Symbol — die Definition
  landete auf der Variante; jetzt auf dem Feld;
- die späteren Namen eines **Typpfads** (`I.Iter.Item`) und die Namen **benannter Typargumente**
  (`Iterator<Item = int>`) sprangen zum ersten Namen bzw. zum Interface; jetzt zum assoziierten Typ;
- `Shape.Rect { … }` an `Shape`: jetzt das Enum;
- eine Definition **antwortet nur für den Namen unter dem Cursor**: an `with` in `(u with {…}).show()`
  oder an `length` in `n = items.length()` sprang sie auf das umgebende `show` bzw. `n`; ein `_` nennt
  nichts; synthetisierter Code (`hash` eines `T[4]`) hat keine Datei — jeweils keine Antwort statt einer
  falschen.

Dazu: **Hover zeigt den Kopf einer Deklaration, wie er geschrieben steht** — `fn twice(n: int): int`,
mit Namen, Schranken, Defaults und `throws` —, aus derselben Funktion, die `lyric doc` schreibt (P6);
**Referenzen und Rename über die Pakete**, die der Editor analysiert hat: die Verwendungen einer
Bibliotheksfunktion in einem offenen Programm, das von ihr abhängt, findet dessen Kompilation, an derselben
Deklaration (gleiche Datei, Stelle, Text); ein Rename, der ein Paket aus dem Git-Cache oder die std
änderte, wird abgelehnt (14 §11 Regel 7); das **kleinere Budget** der Auswertung im Editor (P3, offener
Punkt aus S2): ein Zehntel des Standards, eine Stelle des Programms, die es überschreitet, bleibt ohne Wert
und ohne Meldung (Regel 6) — die std behält ihres.

Nicht gebaut: Completion im Korpus (jede Anfrage kompiliert neu; ihre Tests decken die Formen); ein
Workspace-weites Suchen nach Paketen, die der Editor nicht offen hat — die Verwendungen in einem nie
geöffneten abhängigen Paket findet niemand, wie bei gopls ohne geladenes Paket.

### Nachtrag 2026-10-26 — S4–S9 und der Abschluss von M13b

- **S4** (#572): die Vorschläge eines Befunds sind Quick Fixes (die sicheren bevorzugt), ein Lint bekommt `@Allow`
  über der Deklaration, der Katalog-Link steht als `codeDescription`, Formatieren sendet die Zeilen, die sich ändern
  — über 4M Zellen nach Myers. Gefunden: eine Datei ohne Paket hatte keinen Modulnamen, und die Lints verwarfen
  dadurch alle ihre Warnungen.
- **S5** (#573): eine Linse über jedem Test und Doc-Test; die Befehle `lyric.runTest`/`lyric.debugTest` gehören dem
  Client.
- **S6** (#574): `lyric dap` über lldb-dap, der Panik-Filter als Breakpoint auf `lyr_panic`, Frames über die Namen,
  die der Bau neben das Binary schreibt. Die CI fand zwei Unterschiede zwischen lldb-dap 21 und 23 (`pathFormat`
  verlangt; die Antwort auf `launch` vor `initialized`) — der Adapter schreibt die Vorgaben des Protokolls aus.
- **S7** (#575): die Werte über `lldb/lyric_lldb.py` aus den Typen, Varianten und Globalen der Namensdatei — nicht
  aus `RuntimeLayout`, wie der Plan sagte: das kennt die Quellen der Laufzeit, nicht ihre Objekte. Gefunden: das
  `#line` vor einer Funktion deckte ihren Prolog, ein Zeilen-Breakpoint hielt vor jedem Wert; die Quellen eines
  Binarys tragen die Namen ihrer Pakete und brauchen `target.source-map`. Die Linse „Debug test“ hatte nichts
  hinter sich: `lyric test --no-run` und `test` im `launch`. **Nicht gebaut, mit Uhr: der Scope „Tasks“** (offener
  Punkt 281) — die Laufzeit führt keine Liste der wartenden Koroutinen, der Scheduler von `std.task` führt sie in
  Lyric-Daten; ein Scope braucht ein lldb-OS-Plugin. Eine Tür nach 5.0, zu bestätigen.
- **S8** (lyriclang/tree-sitter-lyric): die Grammatik aus dem Parser der Toolchain geschrieben — wo er
  vorausschaut, spaltet sie (GLR) und behält, was parst; ein externer Scanner für verschachtelte Kommentare, rohe
  Strings und den Text in Strings. Der Korpus (1846 Dateien) parst ohne `ERROR`; die Auswahl gegen den Parser der
  Toolchain geprüft (jede ausgewählte nimmt er an, 37 der 39 ausgeschlossenen lehnt er ab, die zwei übrigen sind
  benannt). Die TextMate-Grammatik der Clients wird aus `grammar.json` und `highlights.scm` erzeugt.
- **S9** (vscode-lyric#2, jetbrains-lyric#3): beide 2.0.0 sprechen mit `lyric lsp` und `lyric dap`; die
  Snippets von VS Code prüft die CI mit `lyric check` des Dev-Kanals (das alte `for`-Snippet scheiterte dort mit
  LYR-PAR0019). Die Checkliste von JetBrains bleibt dem Maintainer; ihr Punkt 17 fragt, was hier nicht messbar war
  (offener Punkt 283).
- **Windows gemessen**: `lldb-dap.exe` von LLVM startet auf dem Runner nicht (offener Punkt 286, Uhr M15).
- **Zu bestätigen**: 273 (E1 B teilt die Parse, nicht die Prüfung — die geprüfte std eine Tür nach 5.0) und 281.
  **Nächster Schritt: M14, sein Plan hier zuerst.**

## Nachtrag 2026-10-26 — der Plan für M14

M14 nach seiner Zeile oben, 11 W4 (X1–X10) und W5 A (H1–H8), 01 K4, 06 G1/S5/S6, 07 B5/B6: **FFI und
Einbettung** — C-Bibliotheken aus Lyric binden, Lyric als C-Bibliothek in einen Host einbetten. Das Artefakt:
**`examples/ffi` bindet eine C-Bibliothek; `examples/embedded-host` ist ein C-Programm gegen `lib<app>.a`; Spec
13 (ABI) vollständig**. Grundlage ist der Stand nach M13b (#577), gemessen vor dem Plan:

- **Stufe 1 steht**: `extern "C" fn f(…): T [= "sym"];` mit Skalaren (`CrossesC`, `LYR-SEM0099`), als `IrImport`
  `C:<sym>` gesenkt und direkt in C gerufen; `[native]` mit `sources`, `include`, `libs` je OS im Manifest
  (`NativePart`); Spec 13 §1–§2, sieben Fälle.
- **Die Laufzeit hat den Kern**: `lyr_init(const LyrConfig*)` mit Ausgabe-Haken, Heap-Grenze und Signalen,
  `lyr_shutdown`; Wurzeln `lyr_root_new/get/set/free` (das Netz für `GcHandle`); `lyr_thread_attach/detach`; der
  Fremdrahmen-Zähler `lyr_coro_enter_foreign/leave_foreign` (01 K4, „noch fragt ihn niemand“); `lyr_gc_collect`,
  `lyr_gc_stats` („intern bis M14“); ein Panik-Haken. Öffentlich ist nur der Sammel-Header `lyr/lyr.h` aller internen.
- **Es fehlt** alles auf der Sprachseite: `Ptr`, `CStr`, `@Layout`, Funktionszeiger-Typen, die Blockform, C-Variadik,
  `unsafe`, `std.ffi`, Callbacks, `GcHandle`, `@Export`, `lyric build --lib`, Header, `LyrStatus`, `lyr_take_error`,
  `lyr_step`/`lyr_run`, `ffi.Library`. Die Intrinsics-Tabelle der std (`Intrinsics.Runtime`, ~156 Einträge) steht
  noch neben `extern "C"` — und die `extern`-Deklarationen der std **prüft niemand** (`RequireBody` kehrt bei
  `IsNative` früh zurück; `hash.lyr` gibt `uint64` zurück, was `CrossesC` außerhalb der std ablehnt).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Die Typtabelle vollständig** (X2, X3): `Ptr<T>` (nie null) und `?Ptr<T>` (die Nische, V5) als Typen der Sprache in `std.ffi`, `CStr` ebenda; `string` als Argument koerziert zu `CStr` **ohne Kopie** (das Layout ist NUL-terminiert, Spec 13 §1.2), `CStr` aus C ist geliehen (`toString() throws Utf8Error`, `length()`); `@Layout(C) struct` als Wert und hinter `Ptr`, Felder C-fähig sonst `LYR-SEM0099`, `T[N]` darin inline; `@Layout(C)`/`@Layout(uint8)` an Enums ohne Nutzlast; `char` ↔ `uint32_t`. Die Prüfung ist **eine** Funktion (`CrossesC` wächst), die der Checker, die Spec-Tabelle und die Fälle teilen | Rust `#[repr(C)]`, Zig `extern struct`; verworfen: alle Structs im C-Layout (nimmt dem Compiler die Nischen, X2) |
| P2 | **Die Formen** (X1): `extern "C" { fn …; fn …; }` gebündelt; C-Variadik als nacktes `...` ohne Namen (`fn printf(fmt: CStr, ...): int`), **getrennt** von Lyrics `xs: T...` — Argumente dahinter nach C-Promotion (`float32` → `double`, kleine Ganzzahlen → `int`), nur Typen der Tabelle; `extern "C" fn(int) -> int` als Typ eines Funktionszeigers | C, Rust; verworfen: `@Link` (X1: `[native] libs` gibt es) |
| P3 | **`unsafe` und Speicher** (X4): `unsafe { … }` als kontextuelles Wort vor einem Block; darin `Ptr<T>.read()/write(v)/offset(n)/cast<U>()`, `ffi.alloc<T>(n)`/`ffi.free(p)`; `T[].asPtr()`/`Slice<T>.asPtr()` sicher, ihre Nutzung unsafe; der **Aufruf** eines `extern` ist nicht unsafe (Zig); `LYR-SEM` für eine Operation außerhalb; keine Arena | Rust `unsafe`, Zig; verworfen: Java-FFM-Arena, Handle-Tabelle |
| P4 | **Callbacks** (X5, 01 K4): eine nicht-fangende Funktion oder ein nicht-fangendes Lambda koerziert zu `extern "C" fn(…)` — der Emitter schreibt je Signatur ein Trampolin, das den Fremdrahmen-Zähler setzt; `ffi.Callback.new(closure)` → `(fnptr, userdata: Ptr<void>)`, der Zustand über eine Wurzel gehalten bis `release()`; der Callback-Typ wirft nicht, die Brücke ist `ffi.stash(e)`/`ffi.takeStashed()` (Task-lokal); **Panik im Callback = Abbruch**; ein Callback darf parken (L4). K4: kein Abbruch und keine Thread-Migration einer Koroutine mit Fremdrahmen — die Laufzeit prüft und meldet | Rust, Go cgo |
| P5 | **Lebensdauer und fremde Threads** (X6, X7, H5): `ffi.GcHandle.pin(obj)`, `release()`, `asPtr()`, `GcHandle.from(ptr)` über die Wurzeln; ein Callback auf einem Thread ohne Lyric-Scheduler geht in die **Queue des besitzenden Threads** (06), nur `void`; mit Rückgabe von fremdem Thread = Abbruch mit Meldung; `Callback.new(closure, thread: …)` wählt den Besitzer; `lyr_gc_stats`/`lyr_gc_collect` werden Host-API mit Header-Zusage | .NET `GCHandle`, Go `cgo.Handle`, Kotlin-Dispatcher |
| P6 | **Die Intrinsics-Tabelle wird `extern "C"`** (M8b P1, die Uhr aus der Zeile 2026-10-07): jeder rumpflose std-Native wird eine `extern "C"`-Deklaration auf seine Laufzeitfunktion; was die Tabelle nicht trägt, weil es Objekte der Laufzeit kreuzt (`string`, Arrays, Klassen als `LyrStr*`/`LyrArr*`/Objektzeiger), kreuzt **nur in der std** — ein Vorrecht nach Herkunft, wie `IsNative` heute, aber **geprüft**: die std-Deklarationen gehen durch dieselbe Prüfung mit einer std-Zeile mehr in der Tabelle. Danach gibt es einen Mechanismus statt zwei; `Intrinsics.Runtime` fällt bis auf die Makros, die keine Funktionen sind (`LYR_CONSOLE_PUT` u. a. werden `static inline`-Funktionen der Laufzeit) | Go `//go:linkname` nur im Runtime-Paket; verworfen: die Tabelle behalten (zwei Wege zu einer C-Funktion, die Regel „ein Mechanismus“) |
| P7 | **Export und Bibliotheken** (X8, B6): `@Export("c_name") pub fn` nur mit Typen der Tabelle, Reachability-Wurzel; **`lyric build --lib`** baut `lib<name>.a` und auf Wunsch `.so`/`.dylib`/`.dll` (`--lib=static\|shared`, Standard `static`), dazu **`<name>.h`** mit den Exporten, den `@Layout(C)`-Typen, die sie nennen, und dem Einbettungs-API; ein werfender Export bekommt `LyrStatus name(args…, R* out)` (H3) | Rust `cbindgen`, Go `//export`, Swift `@_cdecl` |
| P8 | **Einbettung** (H1–H6): `lyr_init`/`lyr_shutdown` bekommen `LyrStatus`; **ein Export läuft als Task** auf dem Scheduler des rufenden Threads und pumpt bis zu seinem Ende (synchron für den Host, parken erlaubt); `lyr_step(): bool`/`lyr_run()` für Tasks, die ein Export mit `spawnDetached` begann; `LYR_OK`/`LYR_ERROR`/`LYR_PANIC`, **eine Panik im Export ist `LYR_PANIC` und die Laufzeit bleibt nutzbar** (die Task-Grenze, T4); `lyr_take_error(LyrError*)` mit Typname, Meldung, Ursachenkette; der erste Aufruf auf einem Host-Thread attacht ihn (H4), `lyr_detach_thread()` gibt ihn frei; Reentranz ohne Zähler (H6). **Ein kuratierter öffentlicher Header** `lyric.h` — der Sammel-Header bleibt intern | Python `Py_Initialize`, Swift; gegen Rust (Panik über FFI = abort) |
| P9 | **Dynamisches Laden** (H7): `ffi.Library.open(path): Library throws IoError`, `lib.symbol<extern "C" fn(int) -> int>("name"): ?…`, `Closeable`; dlopen/LoadLibrary in einer Hülle je OS. Lyric-Plugins (eine Lyric-`.so` im Lyric-Programm) bleiben die Tür, die H7 nennt | Rust `libloading`, C# `NativeLibrary` |
| P10 | **Beispiele, die laufen** (die Zeile oben): `examples/ffi` bindet eine kleine C-Bibliothek, die als `[native]`-Quelle im Beispiel liegt (kein Download, keine Systembibliothek außer libc) — Structs, Strings, ein Callback, ein Handle; `examples/embedded-host` ist ein C-Programm, das `lib<app>.a` linkt, Exporte ruft, einen Fehler und eine Panik als Status sieht und Ausgabe über die Haken umleitet. Beide baut und fährt die CI auf Linux, macOS und Windows | — |
| P11 | **Spec 13 spec-first** (H, X10): die Typtabelle je Zieltripel, `@Layout`-Regeln, Callbacks, Export, das Einbettungs-API und seine Status als Normtext, je Slice die Regeln und Fälle vor oder mit dem Code; ein Fall, der C braucht, bringt seinen `[native]`-Teil mit | wie M11 P14 |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** Zeiger, Strings, Structs an der Grenze | P1, P2 (Blockform, Funktionszeiger-Typ) | `strlen(s)` mit einem `string`; ein `@Layout(C)`-Struct als Wert und hinter `Ptr` in beide Richtungen; ein `@Layout(uint8)`-Enum; was nicht kreuzt, je ein Fall mit `LYR-SEM0099` |
| **S2** C-Variadik | P2 (Variadik) | `printf` mit `int`, `float32` (befördert), `CStr`; ein Lyric-Variadik daneben unverändert |
| **S3** `unsafe` und Speicher | P3 | `ffi.alloc`/`free`, `read`/`write`/`offset`/`cast` in `unsafe`; dieselbe Operation außerhalb abgelehnt; `asPtr()` |
| **S4** Callbacks | P4 | `qsort` mit einer Lyric-Funktion; `Callback.new` über eine Closure mit `userdata`; ein Fehler über `stash`/`takeStashed`; eine Panik im Callback endet das Programm; ein Callback, der parkt; K4 gemeldet |
| **S5** Handles und fremde Threads | P5 | `GcHandle` hält ein Objekt über eine Sammlung; ein C-Thread ruft einen `void`-Callback, der auf dem Besitzer läuft; einer mit Rückgabe bricht mit Meldung ab |
| **S6** die Tabelle wird `extern "C"` | P6 | `Intrinsics.Runtime` fällt (bis auf Dokumentiertes); jede std-Deklaration geprüft; Codegröße und Messpunkte unverändert |
| **S7** Export und `--lib` | P7 | `lyric build --lib` → `.a`/`.so`/`.dll` + Header; ein C-Programm ruft einen Export; ein werfender Export liefert `LyrStatus` |
| **S8** Einbettung | P8, P10 (`embedded-host`) | `examples/embedded-host` auf drei Systemen: Exporte als Tasks, `lyr_step`, Panik als `LYR_PANIC` mit nutzbarer Laufzeit danach, `lyr_take_error`, Ausgabe umgeleitet, zweiter Host-Thread |
| **S9** dynamisches Laden und `examples/ffi` | P9, P10 (`ffi`) | `ffi.Library` lädt die Beispiel-Bibliothek als `.so`/`.dylib`/`.dll`; `examples/ffi` läuft auf drei Systemen |
| **S10** Abschluss | STATUS, CHANGELOG, Bericht, der Plan für M15 als nächster Schritt | die Zeile oben |

Nach Aufwand: groß S1, S4, S8; mittel S3, S5, S6, S7; klein S2, S9, S10. Zusammen **L**, wie die Zeile sagt. Die
Reihenfolge folgt den Abhängigkeiten: die Typen zuerst, weil jede weitere Grenze sie nennt (S1); Variadik und
`unsafe` danach als kleine Nachbarn (S2, S3); die Callbacks (S4) vor den Handles und Threads (S5), die sie
brauchen; die Tabelle (S6) erst, wenn die Typen ihre Natives tragen; Export (S7) vor der Einbettung (S8), weil
ein Export die API der Einbettung ist; das dynamische Laden zuletzt, weil es die Funktionszeiger-Typen nur
benutzt. S6 hängt nur an S1 und darf gezogen werden, wo ein PR in CI wartet.

**Plattformen**: jeder Slice mit nativem Code fährt den vollen Plattformlauf vor dem Merge (die Regel seit M11
S4); `.so`/`.dylib`/`.dll` und Callbacks sind die Stellen, an denen Windows und macOS anders sind — die Aufrufkonvention
der Variadik unter Windows x64, der Export einer DLL (`__declspec(dllexport)` über zig cc), die Install-Namen
unter macOS.

**Reflexion**: Die Entscheidungen stehen (11 W4/W5, 2026-09-29); die Gefahr ist nicht das Entscheiden, sondern dass
die Grenze an einem Ende anders aussieht als am anderen. Darum hält **eine** Funktion fest, was kreuzt (P1), und
die Spec-Tabelle, der Header (P7) und die Fälle lesen dieselbe Liste — Rust brauchte für `repr(C)` und
`improper_ctypes` zwei Mechanismen, die auseinanderliefen. Der zweite Punkt mit Risiko ist P6: die Intrinsics-Tabelle
ist bequem, und die ungeprüften std-Deklarationen zeigen, was Bequemlichkeit kostet; ein Vorrecht nach Herkunft,
das geprüft wird, ist das, was Go mit `linkname` im Runtime-Paket tut, und der eine Weg, auf dem eine C-Funktion
gerufen wird. Bei der Einbettung ist die Wahl H3 gegen Rusts „Panik über FFI = abort“ schon getroffen; sie kostet
die Task-Grenze um jeden Export, die die Laufzeit seit M6 hat — ein Export ist ein Task, der zufällig von C
gestartet wird, nicht ein neuer Weg in die Laufzeit. Callbacks, die parken dürfen (L4), sind das Teuerste: der
Fremdrahmen-Zähler (K4) liegt seit M6 bereit und bekommt hier seine Prüfer; ein Callback auf fremdem Thread wird
in eine Queue gelegt statt ausgeführt, weil eine Koroutine ihren Thread nicht wechselt (G1).

**Nicht in M14**: `lyric bindgen` (X9, Tür); `@Pure` an `extern` (X10, Tür); Unions und Bitfelder (X2, Tür);
Lyric-Plugins in Lyric (H7, Tür); ein Compiler im Host (H8); Lyric-Script in Lyric 5 (H9, neu zu planen, wenn die
native Script-VM steht); `lyr_gc_stats` für Stellschrauben (Tür, 01).

*Nachtrag 2026-10-28:* `lyric bindgen` ist mit M14b entschieden (11 „Review 2026-10-28“ N2) und dort geplant.

### Nachtrag 2026-10-28 — der Abschluss von M14

Die Zeile hält: `examples/ffi` bindet seine C-Bibliothek und lädt sein Plugin, `examples/embedded-host` ist ein
C-Programm gegen `libtally.a` und gegen die DLL, beide im vollen Plattformlauf; Spec 13 hat §2–§6. Was anders kam als
geplant:

- **Geschnitten**: S4 in a (Typ, Trampoline, Park, Panik), b (`Callback`, userdata) und c (`stash`, K4); S5 in a
  (`GcHandle`) und b (fremde Threads); S6 in a/b/c nach Modulen, weil die Senkung die Helfer von string, core und
  task beim Namen ruft.
- **P2**: der Funktionszeiger-Typ kam mit S4, seiner ersten Nutzung, nicht mit S1 (offener Punkt 291).
- **P4**: die Form von `Callback` hat der Maintainer gewählt — der C-Typ ausgeschrieben,
  `Callback<extern "C" fn(Ptr<void>, int) -> void>.new(…)`, statt eines aus der Closure abgeleiteten.
- **P5**: `Callback.new(closure, thread: …)` ist nicht gebaut; der Besitzer ist der Thread, der `new` ruft
  (offener Punkt 297).
- **P6**: 28 Zeilen bleiben, je dokumentiert — mehr als „die Makros“: die Starter, `schedulerRuns`, die
  StringView-Helfer, die der Header `static inline` hält.
- **P7**: `--lib` und `--lib --shared` statt `--lib=static|shared`; `lyr_init` antwortet `LyrStatus`.
- **P8**: eine Panik ist `LYR_PANIC` nur in einem werfenden Export, in einem anderen endet der Prozess (offener
  Punkt 298, entschieden); ein Export, den Lyric durch C erreicht, läuft im laufenden Task, eine Panik dort endet
  den Prozess, auch unter einem äußeren Export (299, entschieden; K4).
- **P9**: wie geplant; Linux linkt `-ldl`. `symbol<F>` senkt auf die Adresse — der Wert eines C-Funktionszeigers ist
  ein `uint`, `?F` also `?uint`, nichts wandelt.
- **Gefunden**: der Plattformlauf fand in S5b ein Rennen im ersten Zug des Schedulers; der Formatter-Beweis lief
  an einem `extern "C" { … }`-Block im Kreis (die Rückreferenz `ExternSpec.Block`), sobald ein Beispiel einen hatte.
- **Zu entscheiden, je „entscheiden oder Tür“**: 287 (Werte an `@Layout`-Enums), 288 (ein Enum als Feld), 289
  (`?Ptr`/`?CStr` als Feld, die Nische), 292 (ein Lint für ein `unsafe` ohne Speicheroperation), 293 (`asPtr()`
  hält das Array nicht), 297 (der Besitzer-Thread eines `Callback`). **Nächster Schritt: M14b, dann M15, sein Plan
  hier zuerst.**

## Nachtrag 2026-10-28 — der Plan für M14b

M14b nach 11 „Review 2026-10-28“ (E1–E13, N1–N8) und 07 „Review 2026-10-28“: **C-Bibliotheken einbinden, so
angenehm wie möglich** — vorgebaut, aus fremdem Quelltext oder aus dem System, mit erzeugten Bindungen. Das
Artefakt: **`examples/native-prebuilt` linkt ein vorgebautes Archiv aus dem Paket und nutzt auf macOS ein
Framework; `examples/raylib` baut raylib aus seinem Repository und ruft es über erzeugte Bindungen.** Grundlage
ist der Stand nach M14 S7 (#596), gemessen vor dem Plan:

- `[native]` hat `sources`, `include`, `libs` und Tabellen je OS (`NativePart`); `libs` und `build.linkLib`
  werden `-l<name>` (`CBuild.Link`), ohne Suchpfad.
- Die nativen Einheiten bekommen nur das `include` ihres Pakets; die `compileC`-Einheiten eines Skripts nur die
  Paketwurzel und die `cFlags` desselben Skripts (`BuildScripts.Run`).
- Der Link-Record (`Pipeline.UpToDate`) hält Pfade und `-l`-Namen, verglichen über Zeitstempel; die Pakete
  linken nach Namen geordnet.
- Ein statisches `--lib` legt die Objekte und die Laufzeit in `lib<name>.a`; welche Bibliotheken der C-Nutzer
  dazulinkt, sagt niemand.
- zig 0.16 (gemessen): Archive linken auf allen Tier-1-Tripeln über Pfad und `-L`/`-l`; Frameworks und
  Linux-Systembibliotheken cross nicht; der clang-AST als JSON kommt aus `zig cc` (mit Exit 1 danach).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Ein Modell für „das C eines Pakets“**: `NativePart` wächst um `archives`, `defines`, `frameworks`, `pkgConfig`, `bindings` und die Tabellen je Tripel (E1, E2, E4, E6, N2, N3); die Schichtung `[native]` → OS → Tripel ist **eine** Funktion, die Manifest, Build und `lyric metadata` teilen. Pfade sind Paketpfade oder `@name/…` (N1), geprüft beim Lesen des Manifests | CMake-Targets mit `INTERFACE`-Eigenschaften; verworfen: je Schlüssel eine eigene Schichtung |
| P2 | **Der Link nach Plan**: ein `LinkPlan` (Objekte, Archive, Bibliotheken nach Namen, Frameworks, die Zeilen aus `pkg-config`) entsteht aus dem Graphen in Abhängigkeitsreihenfolge (E12); `CBuild` linkt ihn, der Link-Record hält ihn mit den **Inhalts-Hashes** der Archive; `--lib` bündelt seine Archive und nennt den Rest (E11). Was heute `libraries` heißt, geht darin auf | heute: drei Listen, zweimal zusammengesetzt (`Pipeline`, `BuildScripts`) |
| P3 | **Quellbäume über den Git-Cache** (N1): ein Eintrag `[native.vendor]` ist im Cache und im Lock eine Revision wie eine Git-Abhängigkeit, aber ohne Manifest (`kind = "native"` im Lock); URLs laden in den Benutzer-Cache unter ihrem Hash, entpacken einmal; lazy nach Ziel. Laden darf nur, wer den Graphen liest — `--offline` und `--locked` gelten unverändert | Zig `build.zig.zon`; verworfen: Submodule |
| P4 | **Bindungen als erzeugter Modulraum** (N2): je Ziel unter `out/cache/bindings/<tripel>/`, gelesen wie `gen/` (BS6), Schlüssel = Header samt Includes, Defines, Ziel, Generator-Version. Die Abbildung ist eine Funktion über den JSON-AST nach der Typtabelle von `CrossesC` (M14 P1) — dieselbe Liste, die Checker und Spec lesen; `lyric bindgen` ruft sie ohne Bau | Rust `bindgen`; verworfen: libclang einbinden (NativeAOT + eine C++-Bibliothek je Plattform) |
| P5 | **`std.build` neu** (N4): `Target` als Struct, `cLibrary` als Builder über Direktiven (eine Bibliothek je Direktive mit ihren Flags), `linkArchive`/`linkFramework`/`pkgConfig`, `vendorDir`, `cEnv`; `compileC` und `cFlags` fallen samt ihren Tests | Rusts `cc`-Crate |
| P6 | **`LNK`-Diagnosen** (N6): der Linker spricht zig/lld- und ld64-Text; eine kleine Lesart je Linker zieht Symbol und Bibliothek heraus und sucht die `extern`-Deklaration im IR (`C:<sym>`); was sie nicht erkennt, bleibt `BLD0001` mit dem rohen Text — nie ein falscher Hinweis | rustc gibt den Linker roh aus |
| P7 | **Spec spec-first**: 15 §5 (der native Teil), §6 (Reproduzierbarkeit), §8 (das Build-Skript), 13 §4 Regel 9 (die Link-Zeile) und 16 §1 (Bereich `LNK`) gehen in L1 als Text voraus; die Fälle und die Codes kommen mit ihrem Slice. 14 (`add --vendor`, `bindgen`, `-v`) kommt mit L3, L4, L7 — die Verbtabelle ist aus dem Code gedruckt | wie M14 P11 |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **L1** Entwurf und Spec | 07, 11, dieser Plan; Spec 15 §5/§6/§8, 13 §4, 16 §1 | die Regeln als Text |
| **L2a** SDKs über das Skript | E3′c, E3′d, der Kopierteil von E3′b: lyriclang/lyric#585 und lyric-spec#365 auf main gezogen — `linkSearch`, `besideBinary`, `rerunIfEnvChanged`, `$ORIGIN`/`@executable_path`, kein Suchpfad im Binary, zigs `libX.dll.a`; `LYR-BLD0004` → `LYR-BLD0006`; das Book-Kapitel „C libraries“ entsteht | die drei Tests aus #585 (geteilte Bibliothek außerhalb des Pakets, Umgebungsvariable, Namenskonflikt); der Katalogfall von `BLD0006` |
| **L2b** der Kern des nativen Teils | P1, P2 ohne `--lib`: `archives`, `dylibs` (E3′a/b, mit der Prüfung des Mach-O-Namens), `defines`, Tripel-Tabellen, `lyric.h` (E9), Header der Abhängigkeiten (N8), Reihenfolge und Inhaltsschlüssel (E12); Suchpfade und „eigenes Verzeichnis zuerst“ aus L2a gehen im `LinkPlan` auf | `NativeTests`: ein Archiv und eine geteilte Bibliothek, im Test gebaut, auf dem Wirt gelaufen und auf alle Tier-1-Tripel cross gelinkt; `ReproducibilityTests` mit Archiv; Ablehnungen als Konformanzfälle |
| **L3** Quellbäume | P3: Git, dann URL mit Hash; Lock; lazy; `lyric add --vendor` (N5) | ein Paket baut einen Baum aus einem lokalen Git-Repository und einer URL eines lokalen Servers; Hash-Fehler, `--offline`, `--locked` |
| **L4** Bindungen | P4: der Generator, das Modul je Ziel, `lyric bindgen` | Header mit Struct, Enum, Typedef, Funktionszeiger, Variadik, Konstanten → aufrufbares Modul; das Übersprungene aufgelistet; je Tier-1-Tripel erzeugt |
| **L5** `std.build`, Frameworks, `pkgConfig` | P5, E4, N3; der macOS-Messlauf zuerst (zig + Framework auf dem Wirt) | ein Skript baut eine `cLibrary` und linkt ein Framework; `pkgConfig` auf Linux; cross abgelehnt |
| **L6** `--lib` | P2 für Bibliotheken (E11, E3′e) | ein C-Programm linkt `lib<name>.a` mit gebündeltem Archiv und der genannten Zeile; die `dylibs` liegen daneben |
| **L7** `LNK`-Diagnosen | P6, `lyric build -v` | undefiniertes Symbol → die `extern`-Zeile; fehlende Bibliothek → die Manifestzeile; doppeltes Symbol → die Pakete |
| **L8** Beispiele und Abschluss | `examples/native-prebuilt` (Archive je Tripel mit Quelle und Skript zum Neuerzeugen, die CI prüft die Bytes; CoreFoundation auf macOS), `examples/raylib` (gebaut, nicht gefahren: die Runner haben kein Display); STATUS, CHANGELOG, Bericht, die Uhr von E7 | die Zeile oben, im vollen Plattformlauf |

Nach Aufwand: groß L4; mittel L2b, L3, L5; klein L1, L2a, L6, L7, L8. Zusammen **L**. Die Reihenfolge folgt den
Abhängigkeiten: der Kern (L2) trägt alles; Quellbäume (L3) vor den Bindungen (L4), die ihre Header lesen; die
Skript-API (L5) nach beiden, weil `vendorDir` und `cLibrary` sie nennen; `--lib` (L6) und die Diagnosen (L7)
lesen den fertigen Link-Plan. **L2 beginnt nach dem Merge von M14 S8** — beide ändern `Pipeline` und `CBuild`.
L2a zuerst, weil #585 fertig und getestet ist und L2b seinen `LinkPlan` darauf baut.

**Doku je Slice** (Maintainer, 2026-10-28: „alle diese Änderungen müssen auch in die Erklärungen und in das
Book“): jeder Slice zieht im selben PR nach, was ein Benutzer davon sieht — das Book-Kapitel
`docs/book/c-libraries.md` (entsteht in L2a, wächst je Slice, L8 schließt es), die Erklärung (`explanation`,
`wrong`/`right`) jedes neuen Codes in `diagnostics.toml`, die Doc-Kommentare von `std.build` und die
Manifest-Beschreibung von `lyric new`/`lyric metadata`, wo sie Schlüssel nennen. Der 4.x-Guide (`docs/guide`)
bleibt unberührt.

**Plattformen**: L2, L5, L6 und L8 fahren den vollen Plattformlauf vor dem Merge; macOS ist die Stelle mit dem
Risiko (Frameworks über den Mach-O-Linker von zig, ungeprüft; der Laufpfad `@executable_path`), Windows die mit
der ABI-Frage (E3: nur mingw). L2a fährt ihn ebenfalls: der Mach-O-Laufpfad und die `.dylib`-Suche aus #585
liefen nie auf macOS.

**Reflexion**: Die Vorbilder liegen an zwei Enden. Cargo lässt fast alles das Build-Skript tun (`build.rs`,
`cc`, `pkg-config`, `bindgen` als Crates) — mächtig, aber jede C-Bibliothek ist ein kleines Programm, und die
Suche über `rustc-link-search` macht das Ergebnis von der Maschine abhängig. Zig macht den Build zur Wahrheit
(`build.zig`) und holt C-Bäume über `build.zig.zon` mit Hash — die angenehmste Erfahrung heute, weil `zig cc`
alles cross übersetzt, aber Lyric hat sich gegen das Skript als Wahrheit entschieden (W3). Lyric nimmt Zigs
Bezug (Hash, lazy) und Zigs Compiler, Cargos Trennung (Manifest erklärt, Skript tut) und CMakes Sicht auf eine
Bibliothek als Bündel aus Ort, Headern und Defines. cgo zeigt, was man meidet: freie Flags im Quelltext. Das
Risiko ist der Umfang — die Oil-Lehre: darum kein C-Paketindex und kein eingebauter CMake-Lauf, und der
Generator (L4) bildet nur ab, was die Typtabelle schon trägt; was sie nicht trägt, fällt sichtbar weg, statt
eine zweite Tabelle zu bekommen.

**Nicht in M14b**: ein Sysroot je Tripel (E5, Uhr M15); Objective-C-Quellen (N7, Uhr M15); freie Linker-Flags
(E7, Uhr: Abschluss von M14b); ein C-Paketindex
und ein eingebauter CMake-Lauf (verworfen für 5.0).

### Nachtrag 2026-10-28 — L3: wo ein Quellbaum aufgelöst wird

P3 sagt „wie eine Git-Abhängigkeit“ und „lazy nach Ziel“; beides zusammen hat eine Stelle, die der Plan offen
ließ: der Graph wird ohne Ziel gelesen (`Project.Resolve` — von `build`, `check`, `metadata`, dem Editor), das
Ziel kennt erst der Bau. Gemessen am Code und entschieden:

| # | Entscheidung | Verworfen |
|---|---|---|
| L3-1 | **Aufgelöst im Bau, je Ziel**: der Graph trägt die Bäume ungelesen; erst der native Teil eines Baus (`Pipeline.Native`) fragt nach `@name`, und nur nach denen, die eine Tabelle für sein Ziel nennt. `check`, `metadata` und der Editor holen nichts | im Graphen auflösen (holt raylibs Windows-Baum auf Linux — gegen N1) |
| L3-2 | **Der Lock** hält einen Git-Baum als `[[package]]` mit `kind = "native"`, `of = "<paket>"` (ein Baum gehört seinem Paket, N1), ohne `version`. Der Bau trägt ein, was er aufgelöst hat; das Lesen des Graphen behält, was ein Manifest des Graphen noch so nennt, und lässt den Rest fallen. Unter `--locked` lehnt der Bau einen gebrauchten, nicht gehaltenen Baum ab, der Graph einen gehaltenen, nicht mehr genannten — beide `PKG0011` | ein eigener `[[tree]]`-Abschnitt (zweite Form für dasselbe: Commit und Hash einer Revision); jeden Baum beim Lesen des Graphen sperren (nicht lazy) |
| L3-3 | **`lyric update`** liest die Bäume, die der Lock hält, neu (ein Zweig an seine Spitze, ein Tag an seinen Commit), genannt oder alle; ein ungehaltener bleibt lazy. Ein Tag eines Baums wird nicht angehoben — ein Baum hat keine Version, an der eine Linie hinge | alle genannten Bäume holen (bricht lazy) |
| L3-4 | **URLs**: `https://`, `http://` und `file://` — der Hash macht den Weg gleichgültig, und ein lokaler Server oder eine Datei sind die Fälle der Tests und des Spiegels. Die Form erkennt der Bau **an den Bytes** (gzip-tar oder zip), nicht am Namen: GitHubs Archiv-URLs tragen keine Endung. Geladen in den Benutzer-Cache als `vendor/<sha256>`, entpackt einmal daneben; nicht im Lock — der Hash im Manifest ist die Sperre | nur `https` (Tests bräuchten ein Zertifikat, und der Hash prüft ohnehin) |
| L3-5 | **`.tar.xz` nicht in 5.0** (N1 nannte es): .NET hat keinen xz-Decoder, und ein eigener LZMA2 wären einige hundert Zeilen im Werkzeug für eine Form, die GitHub-Archive nicht liefern; ein xz-Baum kommt über Git. Entschieden, keine Tür | den `tar` des Systems aufrufen (eine Abhängigkeit von der Maschine, die P6 meidet) |
| L3-6 | **`@name/`** gilt in `sources`, `include`, `archives` und `dylibs` (die Spec nannte `dylibs` nicht, E3′ schon) — und in `bindings` mit L4. Eine Datei eines Baums ist der Inhaltsregel nicht unterworfen (sie ist kein Paketinhalt); sie muss da sein | — |
| L3-7 | **`lyric add <name> --vendor --git … [--tag\|--branch\|--rev …] \| --url …`** schreibt eine Zeile in `[native.vendor]`, wie `add` eine in `[dependencies]`: bei `--git` nach einem Abruf, der zeigt, dass die Revision da ist (kein Lock — den schreibt der Bau, der den Baum braucht); bei `--url` nach dem Laden, mit dem Hash | den Baum gleich sperren (der Lock wüchse um Bäume, die kein Ziel braucht) |

Die Tests: `VendorTests` mit einem lokalen Git-Repository und einem lokalen HTTP-Server (`HttpListener`) — gzip-tar
und zip, das oberste Verzeichnis, der fehlende und der falsche Hash, `--offline`, `--locked`, ein Baum nur für
ein anderes Ziel bleibt ungeholt, `lyric update` bewegt einen Zweig, `add --vendor`; die Ablehnungen des Manifests
als Konformanzfälle, ein Git-Baum über die `repos/` der Suite.

### Nachtrag 2026-10-28 — L4: der Generator, gemessen

Gemessen an zig 0.16 mit einem Header aus Struct, anonymem Struct mit `typedef`, undurchsichtigem Struct, Enum mit
`1 << 3`, Funktionszeiger-`typedef`, Variadik, Union, Bitfeld, `static inline`, Variable und vier `#define`s:
`zig cc -target <t> -Xclang -ast-dump=json -fsyntax-only` schreibt den AST (Exit 1 mit „FileNotFound“ danach,
wie 11 sagt); jede Deklaration trägt ihren C-Typ als Text (`qualType`), ein Enum-Wert steht ausgerechnet im
`ConstantExpr`, `variadic`, `isBitfield`, `storageClass` und `inline` stehen am Knoten; die Datei eines Orts nur,
wo sie wechselt. **Makros fehlen im AST** — sie kommen aus `-E -dM`. Entschieden:

| # | Entscheidung | Verworfen |
|---|---|---|
| L4-1 | **Erzeugt, wo die Skripte laufen** (`BuildScripts.Run`, das jeder Bau, `check`, `test`, `bench`, `--emit` vorher ruft), je Ziel nach `out/cache/bindings/<tripel>/<paket>/`, geladen wie `gen/` über eine Wurzel je Paket; neu nur, wo sich ein gelesener Header, `include`, `defines`, Ziel oder Werkzeug änderte (ein Schlüssel mit den Hashes jeder gelesenen Datei). Der Editor und `doc` lesen, was der letzte Bau für den Wirt schrieb | eine eigene Stufe je Verb (sieben Aufrufstellen) |
| L4-2 | **`check` liest die Bäume, die seine Bindungen brauchen** — ein Modul, das `game.rl` importiert, wird sonst nicht geprüft. 15 §5 Regel 15 („`check` liest keinen“) wird darum enger: keinen außer für eine Bindung | Bindungen in `check` auslassen (der Importfehler wäre falsch) |
| L4-3 | **Ein Zeiger in einem Feld** ist `Ptr<T>`, `CStr` oder der Funktionszeigertyp, nicht optional — 13 §3 Regel 4 nimmt in Feldern kein `?` (offener Punkt 289); C's `null` dort ist Sache des Programms. Als Parameter und Ergebnis `?Ptr<T>`/`?CStr` wie die Spec sagt | das Feld oder den Struct auslassen (raylib verlöre die Hälfte) |
| L4-4 | **Mehrdimensionale Arrays** in Feldern werden flach: `float m[4][4]` → `float32[16]` — dasselbe Layout, und 13 §3 Regel 4 nimmt kein Array von Arrays | auslassen |
| L4-5 | **Konstanten** sind `pub let`: ein Enum-Wert vom Ganzzahltyp seines Enums, ein ganzzahliges `#define` vom Typ, den C dem Literal gibt — `int32`, wo es passt (gemessen: als `int` ging `GeoAdd(GEO_MAX, 1)` nicht, `int` weitet nicht zu `int32`; `int32` weitet zu `int`) —, ein String-`#define` als `string`. Ein leeres `#define` (Include-Wächter) wird übergangen, ein anderes ohne Ganzzahl oder String aufgelistet | `const` (gibt es nicht) |
| L4-6 | **Nur zig cc oder clang** schreibt den AST; mit gcc lehnt der Bau ab (`BLD0005`) und nennt `LYRIC_CC` | einen eigenen C-Parser |
| L4-7 | **Kein Lint** schaut in das Modul (nicht nur `naming` und `dead-code`): es ist geschrieben, nicht verfasst — die Datei zählt wie eine fremde | `@Allow` an jeder Deklaration (`useless-allow` schlüge an) |
| L4-9 | **Ein `string` geht auch in `?CStr`** (13 §3 Regel 3 erweitert): `const char *` ist als Parameter `?CStr`, und ohne die Regel ginge `InitWindow(800, 450, "Titel")` nicht — gemessen am ersten Modul. Ein String ist nie null, die Erweiterung verliert nichts | `const char *` als `CStr` (dann ginge `null` nicht, wo C es nimmt) |
| L4-10 | **Namen der eingebauten Typen** bekommen ein `_` wie Lyrics Wörter: ein C-`typedef int string;` war `LYR-RES0011` im erzeugten Modul (gemessen) | — |
| L4-8 | **`lyric bindgen <header> [--target <tripel>]`**: im Paket mit dessen `include` und `defines` für das Ziel, ein `@name/`-Pfad aus dessen Bäumen; außerhalb mit keinen | Optionen für `-I`/`-D` (eine zweite Stelle für das, was das Manifest sagt) |

### Nachtrag 2026-10-28 — L5: gemessen auf dem Mac, entschieden

**Gemessen** (macOS-Runner, Xcode 26.6, zig 0.16, aarch64): `zig cc -framework CoreFoundation main.c` ohne Ziel
linkt und läuft; mit `-target aarch64-macos` — dem eigenen Ziel — und mit `-target x86_64-macos` findet zig das
Framework nicht („searched paths: none“), auch nicht mit `--sysroot` auf das SDK, das `xcrun` nennt. zig sucht die
Frameworks des SDK nur für den Mac, auf dem es läuft, und nur, wo es kein Ziel genannt bekommt. **pkg-config**
(pkgconf 3.0.7, Linux): `--cflags --libs` schreibt eine Zeile, Exit 1 mit „Package 'x' not found“ auf stderr.

| # | Entscheidung | Verworfen |
|---|---|---|
| L5-1 | **Ein Framework nur im Bau für den Wirt**: von Linux und Windows wie geplant abgelehnt, und auch vom Mac für den anderen Mac (`BLD0004`). Die Tür bleibt E5 (Sysroot je Tripel, Uhr M15) | `-F` auf das SDK von Hand (ungemessen; E5 entscheidet es mit dem Sysroot) |
| L5-2 | **`target()` ist ein `BuildTarget`** mit `os: Platform` und `arch: Arch` aus dem Prelude, nicht mit eigenen `Os`/`Arch` (L1 schrieb `Os.linux`): ein Mechanismus je Begriff; `BuildTarget` statt `Target`, weil der Name im Prelude schon steht | ein zweites Enum-Paar in `std.build` |
| L5-3 | **`vendorDir` per Neustart**: fehlt der Baum, schreibt das Skript `vendor-dir` und endet mit 75; der Bau liest den Baum und startet das Skript neu, mit `LYRIC_BUILD_VENDOR_<NAME>`. Lazy wie L3, ohne Rückkanal | jeden Baum vor jedem Skript lesen (bricht lazy); ein Rückkanal zum Skript |
| L5-4 | **`cEnv()`**: `CC` und `AR` sind kleine Skripte neben dem Build-Skript, die zig mit `cc -target <tripel> -fno-sanitize=undefined` und `ar` rufen — ein Programm, das die Argumente eines C-Compilers nimmt, wie CMake und configure es wollen; `CFLAGS` die Optimierung des Profils. **Gemessen**: ohne `-fno-sanitize=undefined` braucht ein bei `-O0` übersetztes Objekt UBSans Laufzeit, die der Link nicht bringt | `CC="zig cc -target …"` als Zeichenkette (CMake nimmt keinen Befehl mit Argumenten) |
| L5-5 | **Ein Archiv eines Skripts** (`linkArchive`) ist der Inhaltsregel nicht unterworfen: es liegt, wo das Skript es baute — unter `out/` oder absolut | es unter `native/` verlangen (dann schriebe das Skript in den Quellbaum) |
| L5-6 | **pkg-config sortiert**: `-I`/`-D` an die C-Quellen und die Bindungen des Pakets, `-L`/`-l` wie `libs` (zig bekommt die Bibliothek über ihren Pfad, kein Lauf-Pfad im Binary), `-framework` wie `frameworks`, der Rest an den Link; `PKG_CONFIG` nennt ein anderes. Die Antwort ist je Programm und Namen einmal gefragt | den Rest roh an den Compiler (`-L` schriebe zig als Lauf-Pfad ins Binary) |
| L5-7 | **`compileC` und `cFlags` entfallen** (N4): eine C-Bibliothek trägt ihre Flags; der Test aus M8b S13 ist auf `cLibrary` umgestellt | beide als Kurzform behalten (zwei Wege) |

### Nachtrag 2026-10-29 — L6: `--lib` gebaut

**Gemessen** (zig 0.16, Linux): `zig ar qcsL out.a a.o lib.a` übernimmt die Glieder von `lib.a` für ELF, Mach-O und
COFF; `L` geht nur mit `q` („the 'L' modifier is only applicable to the 'q' operation“).

| # | Entscheidung | Verworfen |
|---|---|---|
| L6-1 | **Bündeln über `ar qcsL`**: die Archive der nativen Teile gehen als Glieder in `lib<name>.a`; ihr Inhalt (Hash) ist Teil des Archivschlüssels. Ein Bau mit gcc/clang braucht `llvm-ar` dafür, GNU `ar` wird mit Grund abgelehnt | auspacken und neu packen (Namenskollisionen der Glieder); ein MRI-Skript (Pfade mit Leerzeichen) |
| L6-2 | **Die Zeile**: Kopien der `dylibs` neben dem Archiv (unter Windows die Importbibliothek), `-L`/`-l`, der Rest von pkg-config, `-framework`, die Laufzeit (`CBuild.RuntimeLinkArgs`, dieselbe Liste wie der eigene Link) — als Note `note: link <archiv> with …` und als `linkArgs`; nichts, wo nichts ist; bei `--shared` keine | ein Lauf-Pfad in der Zeile (`-rpath`): wo das C-Programm sucht, entscheidet es selbst |
| L6-3 | **Die Export-Tests linken mit der genannten Zeile** statt einer Liste im Test: die Zeile ist damit auf jedem Plattformlauf geprüft | die Testliste behalten (zwei Wahrheiten) |
| L6-4 | **Gefunden im Plattformlauf** (L4): `bindgen` für Windows auf dem Mac-Runner lief über die zwei Minuten des Generators, und die `TimeoutException` kam als ICE (101). Jetzt gilt die Grenze eines C-Laufs des Baus (fünf Minuten), und eine Überschreitung ist `BLD0005` wie ein Fehler des Headers | die Grenze nur anheben (bliebe ein ICE) |

### Nachtrag 2026-10-29 — L7: die Diagnosen des Links

**Gemessen** (zig 0.16, von Linux): ELF und COFF linkt lld (`ld.lld: error: undefined symbol: x` /
`duplicate symbol: x`, darunter `>>>`-Zeilen mit Quelle und Objekt), Mach-O zigs eigener Linker (`error: undefined
symbol: _x`, `duplicate symbol definition: _x`, darunter `note: defined by <objekt>`); eine fehlende Bibliothek sagt
zig selbst, für alle drei gleich (`unable to find dynamic system library 'x'`). ld64 und GNU ld (clang, gcc) nach
ihren bekannten Texten.

| # | Entscheidung | Verworfen |
|---|---|---|
| L7-1 | **Die Lesart ist eine Funktion über den Text** (`LinkerText`, je Dialekt eine Regel), getrennt von der Zuordnung (`LinkReport`); was keine Regel liest, bleibt `BLD0001` mit dem rohen Text — auch neben erkannten Befunden | jeden Linkerfehler als `BLD0001` (rustc); eine Lesart, die rät |
| L7-2 | **Die `extern`-Stelle trägt der IR-Import** (`IrImport.Declared`), nicht eine Textsuche; ist das Frontend aktuell (keine IR), läuft es für die Stelle noch einmal — nur im Fehlerfall | die Quellen nach `extern "C" fn x` durchsuchen (Blöcke, Aliase, erzeugte Bindungen) |
| L7-3 | **Wem eine Eingabe gehört, weiß der Bau beim Link** (Objekt → Paket über die Einheit, Archiv/dylib → Paket, Laufzeit); der Linker nennt Pfade, die Quellen nennt er über die Präfixabbildung schon `paket/…` | die Pakete aus Pfaden raten |
| L7-4 | **Eine Bibliothek zeigt auf ihre Manifestzeile**, wo `libs`/`frameworks` sie nennen; sonst nennt der Befund das Build-Skript oder `pkgConfig` | immer die Manifestdatei ohne Zeile |
| L7-5 | **`-v` sagt die Befehle beim Start** (`running: …`) — nur, was läuft; mit `--json` abgelehnt, weil der Strom nur Ereignisse trägt | ein Ereignis `command` im JSON (neues Schema für eine Hilfe zum Lesen) |
| L7-6 | **Konformanz**: ein `run`-Fall mit `error:` erwartet Exit 2 (der Bau nach dem Frontend), drei Fälle in 15-project; der Runner unterschied bisher nur Exit 1 | die Befunde nur in den Werkzeugtests |
| L7-7 | **Ein fehlender Header** (N6) nennt die durchsuchten Verzeichnisse der Einheit — im Text von `BLD0001`, kein eigener Code | ein eigener Code (der Fehler ist der C-Quelle, nicht des Links) |

### Nachtrag 2026-10-29 — L8 und der Abschluss von M14b

**Gemessen** (zig 0.16):
- **Reproduzierbare Archive**: `zig cc -O2` schreibt Debug-Information mit dem Verzeichnis des Aufrufs (`.debug_str`),
  und ein COFF-Objekt trägt den Namen von zigs temporärer Datei (CodeView `S_OBJNAME`, zufällig je Lauf). Mit
  relativen Pfaden, `-g0` und für Windows `-Xclang -object-file-name=crc32.obj` sind die fünf Archive von
  `examples/native-prebuilt` aus jedem Verzeichnis und mit leerem zig-Cache dieselben Bytes — mit dem zig von
  ziglang.org, das `tooling/zig-version` pinnt. Archs zig 0.16.0 (gegen das System-LLVM 21 gebaut) erzeugt für
  Linux und Windows andere Bytes; der Plattformlauf fand es, die Archive kommen jetzt aus dem offiziellen Build.
- **Die Linux-Runner** haben weder X11 noch OpenGL als Entwicklungsdateien: die Linux-Jobs installieren
  `libx11-dev` und `libgl-dev` (Maintainer, 2026-10-29), wie schon `lldb`.
- **raylib 5.5** (Release-Archiv, 42 MB, SHA-256 `aea98ecf…e258e`) baut aus dem Quellbaum mit einer `cLibrary`:
  Linux auf dem Wirt (`_GLFW_X11`, wie raylibs Makefile es setzt; X11 und GL aus dem System), Windows cross von
  Linux ohne irgendetwas des Wirts, macOS auf dem Mac (`rglfw.c` als Objective-C, sechs Frameworks); ein Mac-Ziel von
  Linux ist `BLD0004`. Unter WSLg öffnet das Programm sein Fenster. **Keine freien Linker-Flags nötig** — E7 ist
  damit entschieden (11, E7: nicht in 5.0).

| # | Entscheidung | Verworfen |
|---|---|---|
| L8-1 | **`examples/native-prebuilt`**: CRC-32 als Archiv je Tier-1-Tripel mit Quelle und `native/make-archives.sh`; `ExampleTests` baut die Archive neu und vergleicht die Bytes (Cross-Lauf, Linux x64). Auf macOS zählt CoreFoundation (Framework), sonst C — dieselbe `extern`-Signatur, die Wahl liegt in den Tabellen je OS | die Archive ungeprüft einchecken; das Framework nur in einem Test |
| L8-2 | **`examples/raylib`**: der Baum als URL mit Hash (das Archiv, nicht der Git-Verlauf: ein Download statt eines Klons), die Bindungen aus `raylib.h`, der Bau im Skript; gebaut (Wirt, Windows cross), nicht gefahren — die Runner haben kein Display | raylib als Git-Baum (der volle Verlauf); vorgebaute raylib-Archive |
| L8-3 | **Gefunden: Zeilen einer Meldung**: der Renderer machte jedes Steuerzeichen einer Meldung sichtbar, auch `\n` — seit M12a S4 kam die Ausgabe von C-Compiler und Linker in `BLD0001` als eine Zeile mit `␊`. Eine Meldung behält jetzt ihre Zeilen (`PrintableLines`), alles andere bleibt ersetzt | die Werkzeugausgabe als Notes je Zeile (zerreißt Compilerausgaben) |

**Für den Maintainer, je „entscheiden oder Tür“** (gefunden beim Schreiben der Beispiele):
- **L8-a** Ein Struct aus `lyric bindgen` hat keine `var`-Felder: `pos.x += 1` geht nicht, das Beispiel baut je Bild
  einen neuen `Vector2`. In C ist jedes Feld schreibbar.
- **L8-b** `return unsafe { … };` — `unsafe` ist nur Anweisung; der Fehler kommt als `unknown identifier 'unsafe'` und
  zwei Folgefehler über ein Lambda `it`.
- **L8-c** `lyric fmt` bricht eine Builder-Kette (`cLibrary(…).include(…).define(…)`) Argument für Argument um; das
  Beispiel schreibt die Aufrufe als Anweisungen.
- **L8-d** Die eigenen C-Einheiten für Windows tragen denselben zufälligen `S_OBJNAME` wie oben; ein `--lib`-Archiv
  für Windows ist darum nicht bytegleich (der gelinkte PE wird normalisiert, das Archiv nicht).

**Der Abschluss von M14b.** Die Zeile hält: `examples/native-prebuilt` linkt sein Archiv je Tripel und auf macOS
CoreFoundation, `examples/raylib` baut raylib aus seinem Release und ruft es über erzeugte Bindungen — beide in
`ExampleTests`, im vollen Plattformlauf. Gebaut in L1–L8 (#597, #602, #604, #608, #611, #614, #622, #623 und dieser
Slice; Spec #372, #375, #376, #378, #381, #385, #387, #390): SDKs über das Skript, der native Teil mit Archiven,
`dylibs`, Definitionen und Tripeln, Quellbäume aus Git und von URLs, Bindungen aus Headern, `std.build` neu mit
`cLibrary`, `pkgConfig` und Frameworks, `--lib` mit gebündelten Archiven und genannter Link-Zeile, die Befunde des
Links und `-v`. Anders als geplant: Frameworks nur im Bau für den eigenen Mac (L5-1); `lyric bindgen` kam in M14b
statt als Tür (N2); E7 entschieden. **Nächster Schritt: die Halte von M15, dann M16.**

## Nachtrag 2026-10-28 — der Plan für M15

M15 nach seiner Zeile oben, 11 W9 (T1–T8), 11 C4/C9/C10, 14 C9 und den Uhren, die auf M15 zeigen: **Distribution** —
die Toolchain als signiertes Archiv je Plattform, `lyric toolchain` installiert Versionen nebeneinander und wechselt
in die, die ein Paket verlangt, `zig` kommt auf Wunsch dazu, Releases laufen durch eine Pipeline, der Mirror auf dem
VPS spiegelt sie. Das Artefakt: **`lyric toolchain install dev` auf drei OS; die Release-Pipeline probeweise mit
`5.0.0-dev`**. Der Befehl heißt bis M18 `lyric5` (11 C1); der Plan schreibt `lyric`, wo er das Verb meint.
Grundlage ist der Stand nach M14 (#600), gemessen vor dem Plan:

- **Der `dev`-Kanal steht** (`dev.yml`, seit M0): je Push auf `main` vier Archive (`win-x64`, `linux-x64`,
  `linux-arm64`, `osx-arm64`) mit `lyric5`, `stdlib5/` und der C-Quelle der Laufzeit, `SHA256SUMS`, die
  Vorabversion `dev` auf GitHub ersetzt; ein Probelauf je Archiv baut und fährt hello. **Es fehlen**: `osx-x64`
  (Tier 1, „gebaut, nicht ausgeführt“, Uhr M15), Signaturen, eine vorgebaute Laufzeit, `lyric.lldbinit`, der Header.
- **Der Pin steht halb**: `[package] toolchain` ist eine Untergrenze (`ToolchainCheck`, `LYR-PKG0009`, Exit 2) —
  kein Wechsel in eine andere Version, kein `toolchain`-Verb, keine `~/.lyric/`, keine `config.toml`.
- **Der C-Compiler** kommt aus `LYRIC_CC` oder dem PATH (`zig`, `clang`, `gcc`); die Laufzeit wird beim ersten Bau in
  den Benutzer-Cache übersetzt (`UserCache`, `~/.cache/lyric`).
- **Uhren auf M15**: 01 C2 (eine `.c` je Programm — neu entscheiden mit Messpunkt 1 kalt), `x86_64-macos` ausführen,
  11 E5 (Sysroot je Tripel), 11 N7 (Objective-C-Quellen), offene Punkte 219 (musl), 245 (`-q`/`-v`, Fortschritt),
  248 (`lyric env` und `zig`), 250 (ICE-Dump), 251 (C-Compiler-Zeitlimit als ICE), 252 (Windows-Backtrace-Flake),
  285 (`lyric5` gegen `lyric` in den Clients), 286 (`lldb-dap.exe` unter Windows), 295 (Lyrics Namen im erzeugten C,
  entschieden).

**Was außerhalb des Repos wirkt, tut der Maintainer oder gibt es frei** (STATUS, Arbeitsmodus): das Schlüsselpaar
und die Secrets (S6), das Laden von `minisign` (S6), der VPS, sein DNS-Name und der Deploy-Schlüssel (S7), die erste
echte Veröffentlichung auf dem Mirror (S7). Diese Schritte stehen im Plan als **Halt mit Vorlage**: Claude legt
Skript und Anleitung hin und wartet auf das Wort.

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Das Archiv** (T1, T4): je Tier-1-Tripel eines, jetzt fünf — `osx-x64` kommt dazu (NativeAOT baut quer über die Architektur auf dem ARM-Runner; ausgeführt unter Rosetta, wo der Runner es hat, sonst auf einem Intel-Runner — gemessen in S1). Inhalt: `lyric5`, `stdlib5/`, die Laufzeit als C-Quelle **und vorgebaut** für das eigene Tripel (beide Profile, der Standard-Sammler — dieselben Objekte, die `UserCache` heute beim ersten Bau erzeugt, nur vorher), `lyric.lldbinit` mit `lyric_lldb.py`, `include/lyric.h`, die Lizenzen, und **`toolchain.json`** (Version, Tripel, jede Datei mit SHA-256), das `lyric toolchain` beim Installieren gegen das Archiv prüft. Cross-Ziele bauen die Laufzeit weiter aus der Quelle (T3). **musl** (219) ist Tier 2 nach T4: ein Cross-Bau in der CI, kein Archiv der Toolchain dafür | Go (ein Archiv, `GOROOT` relativ), Zig (ein Archiv, keine Installation); verworfen: Installer-Pakete je OS (`.msi`, `.pkg`, `.deb`) — ein Archiv je Tripel reicht für `lyric toolchain`, Pakete für Distributionen sind eine Tür |
| P2 | **Signaturen mit minisign** (T5): geprüft **im Binary** — BLAKE2b-512 als Vorhash und Ed25519, in C# (~400 Zeilen, ohne Abhängigkeit, NativeAOT-fest), gegen die Vektoren aus RFC 8032 und RFC 7693 und gegen Signaturen, die `minisign` selbst gemacht hat. Der **öffentliche Schlüssel steht im Binary** und auf der Website; signiert wird in der CI mit dem `minisign`-Binary (Version und Hash in `tooling/minisign-version`, wie `zig` und Pagefind), der geheime Schlüssel als Secret. Ein Schlüssel; ein Wechsel kommt mit einer Release, die den neuen trägt | Zig signiert mit minisign (dasselbe Format prüft `lyric toolchain install zig`, P6); verworfen: nur `SHA256SUMS` über HTTPS (der Mirror wäre die Wahrheit, 14 C9 sagt: die Signatur), sigstore/cosign (OIDC-Infrastruktur, ein Prüfer von vielfacher Größe), GPG (Schlüsselringe, Format) |
| P3 | **Der Kanal-Index** (T5, 14 C9): je Kanal eine Datei — `dev.json`, `stable.json` — mit den Versionen, je Version und Tripel Archivname, Größe und SHA-256; **signiert** (`.minisig` daneben), damit ein Mirror keine Version unterschieben kann. Liegt auf dem Mirror (`https://dl.lyric.oil1i.de/`) und auf GitHub (der `dev`-Release trägt `dev.json`; `stable.json` liegt in einer festen Release `channels`, die der Release-Job ersetzt). `lyric toolchain` fragt den Mirror zuerst, GitHub danach (T8); beide liefern dieselben Bytes | Go (`go.dev/dl/?mode=json`), Zig (`index.json`, unsigniert); verworfen: das Verzeichnis listen (kein Format, keine Signatur) |
| P4 | **`~/.lyric/` und `lyric toolchain`** (T2, T7): `toolchains/<version>/` je Version, `zig/<version>/` (P6), `bin/` mit dem Binary der Standardversion (unter Unix ein Link, unter Windows eine Kopie), `config.toml` mit `default` — die benutzerweite Stufe der Leiter C4, `LYRIC_HOME` verlegt alles. Verben: `install <version\|dev\|stable>` (lädt Index und Archiv, prüft Signatur, Hash und `toolchain.json`, entpackt in ein Zwischenverzeichnis, benennt erst dann um — ein abgebrochener Lauf hinterlässt nichts), `list` (installiert; `--available` aus dem Index), `default <version>`, `remove <version>` (nicht die laufende, nicht die Standardversion), `update` (installiert die neueste des Kanals der Standardversion **daneben** und macht sie zum Standard; die alte bleibt bis `remove`). Fortschritt beim Laden nur am TTY (C7) — der erste Teil von 245 | Go (`go install golang.org/dl/…`, `GOTOOLCHAIN`), rustup (verworfen: Multiplexer, T2); Cargo-Präzedenz |
| P5 | **Der Wechsel** (T2, T7, P12): bevor `lyric` ein Verb ausführt, bestimmt es die gewünschte Toolchain — `LYRIC_TOOLCHAIN` (`local` = dieses Binary, oder eine Version) > der Pin des Pakets im Arbeitsverzeichnis (eine Untergrenze) > `default` aus `config.toml`. Erfüllt das laufende Binary sie, läuft es; sonst die Standardversion, wenn sie erfüllt, sonst die neueste installierte, die erfüllt; sonst bietet es am TTY die Installation der kleinsten stabilen Version an, die erfüllt, und schreibt ohne TTY `LYR-PKG0009` mit dem Befehl. Gewechselt wird unter Unix mit `execv`, unter Windows über einen Kindprozess mit geerbten Handles und durchgereichtem Exit-Code; `LYRIC_TOOLCHAIN_FROM` verhindert einen zweiten Wechsel. Eine Dev-Version zählt für den Pin als ihre Zahl (wie heute). Der Wechsel kostet einen Prozessstart — gemessen gegen C9 (warm ≤ 50 ms über dem Programmstart) | Go 1.21 (`toolchain`-Zeile, `GOTOOLCHAIN=auto`, Re-exec, Windows als Kindprozess); verworfen: ein Shim-Binary (T2), ein exakter Pin (P12 ist eine Untergrenze) |
| P6 | **`lyric toolchain install zig`** (T1): die Version aus `tooling/zig-version` (ins Binary übernommen), geladen aus zigs eigenem Index, **geprüft mit zigs minisign-Schlüssel** und dem SHA-256 aus dem Index, nach `~/.lyric/zig/<version>/`. Die Suche nach dem C-Compiler wird: `LYRIC_CC` > das installierte `zig` der gepinnten Version > `zig`, `clang`, `gcc` auf dem PATH. Fehlt jeder, nennt die Meldung den Befehl. `lyric env` zeigt `zig` mit Herkunft und die Lint-Stufen eines Baus (248) | Zig selbst; verworfen: `zig` ins Archiv (T1: kein C-Compiler im Archiv — Größe, Lizenzen, eine Version für alle) |
| P7 | **Die Release-Pipeline** (T5): `release.yml` auf ein Tag `v5.*` — die Paketmatrix (mit `dev.yml` geteilt als wiederverwendbarer Workflow), die Probeläufe, signieren, `SHA256SUMS`, GitHub-Release, `stable.json` neu, Upload auf den Mirror. Von Hand mit `dry-run`: ein **Entwurf** auf GitHub (nicht öffentlich) und der Upload in `staging/` auf dem Mirror. Das Artefakt der Zeile, „probeweise `5.0.0-dev`“, ist ein solcher Lauf; der Entwurf wird danach gelöscht. `dev.yml` signiert und schreibt `dev.json` ebenso; der Mirror behält die letzten zehn Dev-Builds | Zig (Master-Builds und Tags aus einer Pipeline); verworfen: Release von Hand |
| P8 | **Der Mirror** (14 C9): auf dem VPS des Maintainers, gehärtet wie 14 es vorschreibt — nur Schlüssel-Login, kein Root über SSH, Firewall 22/80/443, automatische Sicherheitsupdates, ein statischer Webserver mit TLS (Caddy), ein Deploy-Benutzer ohne Shell, der über `rrsync` nur ins Mirror-Verzeichnis schreibt. Die Anleitung und ein Skript liegen in `tooling/mirror/`; **ausgeführt mit dem Maintainer** (Zugang, DNS `dl.lyric.oil1i.de`, der Deploy-Schlüssel als Secret). Die Unversehrtheit hängt an P2, nicht am Server | 14 C9; verworfen: Objektspeicher eines Anbieters (Konto, Kosten, nicht entschieden) |
| P9 | **Die CLI-Reste mit Uhr M15**: 245 — `-q`/`-v` als globale Optionen über M14bs `build -v`, Fortschritt nur für Laden und Installieren (P4), für den Bau eine Tür nach 5.0; 250 — ein ICE schreibt neben Meldung und Trace einen Dump (Version, Befehl, Eingabedatei, Phase) unter `out/` und nennt ihn; 251 — ein C-Compiler, der sein Zeitlimit überschreitet, ist `BLD` mit Einheit und Grenze, Exit 2, kein ICE; 248 in P6 | Cargo (`-v`, ICE-Datei wie rustc `rustc-ice-*.txt`) |
| P10 | **Der Debugger unter Windows** (286, 295): erst messen, warum `lldb-dap.exe` auf dem Runner nicht startet (Exit-Code, fehlende DLL), dann festlegen, was ein Windows-Nutzer neben der Toolchain installiert — beschrieben in `lyric env` und im Book, nicht ins Archiv gepackt. 295 (entschieden 2026-10-26): das erzeugte C trägt Lyrics Namen (Feld `x`, Lokale `p`), gemangelt nur bei einem Zusammenstoß mit C — dann zeigt jeder Debugger Lyric, `lyric_lldb.py` bleibt | rustc/gdb; verworfen: lldb ins Archiv (Größe, Python-Abhängigkeit) |
| P11 | **Messpunkt 1 kalt** (01 C2, C9): hello aus dem frisch installierten Archiv, kalter Cache, mit vorgebauter Laufzeit (P1) — gegen „kalt ≤ 400 ms“. Hält er, steht die Abweichung „eine `.c` je Programm und je Instanz“ fest und die Uhr ist zu; hält er nicht, wird die Einheit je Modul ein eigener Meilenstein-Eintrag, nicht M15 | Messpunkte 1–5 |
| P12 | **Spec spec-first**: 14 (das Verb `toolchain`, seine Optionen und Exit-Codes, `LYRIC_HOME`, `LYRIC_TOOLCHAIN`), 15 (der Pin wählt eine Toolchain, die Leiter), ein Abschnitt zu Archiv, Index und Signatur als informativer Teil von 14; die Fälle prüft die Toolchain selbst (`ToolchainTests` gegen einen lokalen Server), denn ein Konformanzfall lädt nichts | wie M14 P11 |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** das Archiv | P1, P11; `osx-x64` | fünf Archive in `dev.yml` mit vorgebauter Laufzeit, `toolchain.json`, `lyric.lldbinit`, `lyric.h`; der Probelauf je Archiv; `osx-x64` ausgeführt (Rosetta oder Intel); Messpunkt 1 kalt gemessen, 01 C2 entschieden |
| **S2** Signatur und Index | P2 (Prüfung), P3 (Format) | `Minisign.Verify` gegen RFC-Vektoren und `minisign`-Signaturen; ein manipuliertes Byte, ein fremder Schlüssel, ein falscher Vorhash abgelehnt; `Channel`-Index gelesen und geprüft |
| **S3** `lyric toolchain` | P4, 245 (Fortschritt) | `install/list/default/remove/update` gegen einen lokalen Server mit signiertem Index; Abbruch hinterlässt nichts; die Ablehnungen (falscher Hash, falsche Signatur, `remove` der laufenden) |
| **S4** der Wechsel | P5 | ein Paket mit Pin startet die installierte Version, die ihn erfüllt; Angebot am TTY, `LYR-PKG0009` ohne; `LYRIC_TOOLCHAIN=local`; kein zweiter Wechsel; Exit-Codes durchgereicht unter Windows; die Kosten gemessen |
| **S5** `zig` | P6, 248 | `toolchain install zig` gegen einen lokalen Server mit zigs Format; die Suchreihenfolge; `lyric env` mit `zig` und Lint-Stufen |
| **S6** Signieren und Release | P2 (Signieren), P7 — **Halt mit Vorlage**: Schlüsselpaar und Secrets (Maintainer), `minisign` laden (Freigabe) | `dev.yml` signiert und schreibt `dev.json`; `release.yml` mit `dry-run`: ein Entwurf `5.0.0-dev.<datum>` mit fünf Archiven, Signaturen, Index — die Zeile „probeweise“ |
| **S7** der Mirror | P8 — **Halt mit Vorlage**: VPS, DNS, Deploy-Schlüssel (Maintainer) | `tooling/mirror/`; Upload aus `dev.yml`; ein CI-Job installiert `dev` auf Linux, macOS und Windows vom Mirror und einmal mit unerreichbarem Mirror von GitHub — die Zeile |
| **S8** die Uhren | P9 (250, 251), P10 (286, 295), 11 E5 und N7, 252 | je Punkt ein Test oder eine Entscheidung mit Text: der ICE-Dump, das Zeitlimit als `BLD`, Lyrics Namen im C (Goldens), `lldb-dap.exe` gemessen, `.m` mit `zig cc` auf macOS gemessen und entschieden, E5 als Tür bestätigt oder geplant |
| **S9** Abschluss | STATUS, CHANGELOG, Bericht, der Plan für M16 als nächster Schritt | die Zeile oben |

Nach Aufwand: groß S2, S3, S4; mittel S1, S6, S8; klein S5, S7, S9. Zusammen eher **L** als das M der Zeile — die
Krypto-Prüfung (S2) und der Wechsel mit seiner Windows-Hälfte (S4) sind die Posten, die die Zeile nicht sah, dazu
die Uhren (S8), die sich auf M15 gesammelt haben. Läuft M15 über das Doppelte, wird es hier neu geschnitten
(CONTRIBUTING); der erste Kandidat zum Abtrennen ist S8. Die Reihenfolge folgt den Abhängigkeiten: das Archiv (S1)
ist, was alles Weitere lädt; die Prüfung (S2) vor jedem Laden (S3), das Installieren vor dem Wechsel (S4), der
sonst nichts zu starten hat; `zig` (S5) braucht nur S2 und S3. S6 und S7 hängen am Maintainer und laufen, sobald er
Zeit hat — **S2–S5 testen gegen einen lokalen Server und warten auf nichts davon**. S8 ist unabhängig und darf
gezogen werden, wo ein PR in CI wartet.

**Plattformen**: S1, S4, S5, S7 und S8 fahren den vollen Plattformlauf; Windows ist die Stelle mit dem Risiko
(kein `exec`, Kopie statt Link in `bin/`, eine laufende `.exe` lässt sich nicht ersetzen — `update` legt daneben,
nie darüber), macOS die mit der Quarantäne (ein geladenes Binary trägt `com.apple.quarantine`, wenn ein Browser
es lud; `lyric toolchain` lädt selbst und setzt kein Attribut — geprüft in S3).

**Reflexion**: Die Entscheidungen stehen (11 W9, 14 C9); offen war, wie viel davon Code im Binary ist. Go zeigt den
Weg, den T2 gewählt hat: kein zweites Programm, die Toolchain selbst liest die gewünschte Version und wechselt, und
unter Windows ist der Wechsel ein Kindprozess. rustup zeigt, was es kostet, es anders zu machen — ein Multiplexer,
Proxies je Werkzeug, eine zweite Versionsquelle (`rust-toolchain.toml` neben `Cargo.toml`). Die Signatur ist die
Stelle, an der Bequemlichkeit verführt: `SHA256SUMS` über HTTPS fühlt sich sicher an, macht aber den Server zur
Wahrheit, und 14 C9 hat entschieden, dass es die Signatur ist. minisign ist das kleinste Format, das das trägt
(Zig benutzt es, darum prüft derselbe Code auch `zig`), und eine eigene Prüfung in C# ist ein paar hundert Zeilen
gegen Testvektoren — weniger Risiko als eine native Krypto-Bibliothek je Plattform im NativeAOT-Binary. Das zweite
Risiko ist nicht technisch: der VPS und die Schlüssel sind Dinge außerhalb des Repos; der Plan baut alles, was ohne
sie geht, gegen einen lokalen Server, damit das Warten auf den Maintainer nichts blockiert.

**Nicht in M15**: Installer-Pakete je OS und Distributionspakete (Tür); ein Sysroot je Tripel (11 E5 — in S8
bestätigt oder geplant, nicht gebaut); `lyric publish` und eine Paket-Registry (Türen, 07); Fortschritt beim Bau
(Tür, P9); Tier 2 als Archiv der Toolchain; die Umbenennung `lyric5` → `lyric` (M18, 285 dort).

**S3, beim Bauen entschieden** (2026-10-28):

- **`bin` ist ein Link auf das Verzeichnis der Standardversion** — ein Symlink, unter Windows eine Junction —, nicht
  der Link auf das Binary und unter Windows seine Kopie (P4). Das Binary findet `stdlib5/` und die Laufzeit neben
  sich (`AppContext.BaseDirectory`); eine Kopie in `bin/` fände sie nicht, und ein Link auf das Verzeichnis ersetzt
  beim Wechsel der Standardversion nie eine laufende `.exe`. Eine Junction braucht weder Recht noch Entwicklermodus.
- **Ein eigener Mirror bringt seinen Schlüssel mit**: `LYRIC_MIRROR` ersetzt beide Quellen, `LYRIC_MIRROR_KEY` den
  Schlüssel, gegen den geprüft wird (wie Gos `GOSUMDB` mit Name und Schlüssel); der Schlüssel allein wird
  abgelehnt. Bis S6 trägt das Binary keinen Schlüssel — installiert wird dann nur von einem solchen Mirror, und
  so prüfen die Tests gegen den lokalen Server.
- **Auf GitHub wird nur zurückgefallen, wo der Mirror nicht antwortet**; eine Signatur oder ein Hash, die nicht
  halten, sind ein Fehler, kein Umweg. Das Layout des Mirrors: `<base>/<kanal>.json` und die Archive unter
  `<base>/<kanal>/` (S7 lädt so hoch).
- Die Codes: `LYR-CLI0023` (nicht zu haben), `LYR-CLI0024` (nicht, was der Index sagt), `LYR-CLI0025` (die
  installierten Toolchains lehnen die Änderung ab); alle Exit 2. `toolchain default` ohne Version sagt die
  Standardversion. Die Prüfung auf `com.apple.quarantine` läuft im Test auf macOS.

**S4, beim Bauen entschieden** (2026-10-28):

- **Gelesen wird nur das Paket, in dem der Befehl läuft** (das um `-C` oder das Arbeitsverzeichnis), nicht der Graph:
  den Graphen aufzulösen hieße vor dem Wechsel git zu fragen. Der Pin einer Abhängigkeit bleibt die Prüfung des
  Baus (`LYR-PKG0009`), wie Go die `toolchain`-Zeile nur des Hauptmoduls liest.
- **Ohne Terminal kein eigener Fehler vor dem Verb**: das Verb läuft und lehnt das Paket ab (`LYR-PKG0009`); die
  Meldung nennt jetzt `lyric5 toolchain install stable`. So bleiben Verben, die kein Paket brauchen (`version`), auch
  in einem gepinnten Paket benutzbar. Nie gewechselt wird bei `toolchain`, `help`, `explain`, `completions`, `new`,
  `init`; das Angebot am Terminal nimmt nur den stabilen Kanal und nicht bei `version`.
- **Weitergereicht wird die Befehlszeile, wie sie kam** — die globalen Optionen vor dem Verb eingeschlossen; unter
  Unix mit `execve` und einer eigenen Umgebung (die verwaltete Umgebung von .NET erreicht `environ` nicht), unter
  Windows als Kind, dessen Ctrl+C der Elternprozess wartend übergeht.
- **Die Tests laufen im Testprozess**: ein Modulinitialisierer setzt `LYRIC_HOME` auf ein leeres Verzeichnis und
  nimmt `LYRIC_TOOLCHAIN*` weg, sonst ersetzte ein `execve` den Testhost, wo `~/.lyric` etwas Passendes hält. Der
  Wechsel selbst ist ein Prozesstest gegen einen Stellvertreter, den `zig cc` baut.
- **Gemessen** (NativeAOT, Linux, warm, das Beste aus 5 × 20): der Stellvertreter allein 2,1 ms, `lyric5 version`
  ohne Pin 21,2 ms, mit Pin an den Stellvertreter gereicht 20,9 ms — der Wechsel kostet einen `lyric5`-Start,
  rund 19 ms, unter C9s 50 ms über dem Programmstart.

**S5, beim Bauen entschieden** (2026-10-28):

- **zig kommt über zigs Community-Mirrors**, wie zig es von einem Werkzeug verlangt: Name, Größe und SHA-256 aus
  zigs `index.json`, die Mirrors aus `community-mirrors.txt` daneben, gemischt, jede Anfrage mit `?source=lyric`, die
  Download-Seite zuletzt. Geprüft mit zigs minisign-Schlüssel (im Binary, wie ihn die Download-Seite nennt) und gegen
  die Datei, die der trusted comment nennt — sonst reichte ein Mirror ein älteres signiertes Archiv unter dem Namen
  eines neueren. Ein Mirror, dessen Archiv nicht hält, wird **übergangen**, anders als beim eigenen Mirror (S3):
  zigs Mirrors sind Dritte, die Signatur ist zigs. Die Liste wird nicht zwischengespeichert (zig bittet um einen
  Tag) — installiert wird einmal. `LYRIC_ZIG_MIRROR` mit `LYRIC_ZIG_MIRROR_KEY` ersetzt die Seite, wie
  `LYRIC_MIRROR` (S3); so prüfen die Tests gegen einen lokalen Server.
- **Ein eigener `.xz`-Leser** (`Xz` in `Lyric5.Toolchain`): .NET liest kein xz, und zigs Archive für Linux und macOS
  gibt es nur so; das `tar` des Systems bräuchte `xz` daneben, das nicht jede Maschine hat. LZMA2 nach Pavlovs
  Spezifikation, die Prüfsummen CRC32, CRC64 und SHA-256, Index und Fußzeile gegen die Blöcke; **nur der Filter
  LZMA2** — zigs Archiv von 0.16.0 ist ein Block mit 64 MiB Wörterbuch und CRC64 (an seinen ersten Bytes
  nachgesehen); BCJ und die anderen Filter werden abgelehnt. Gemessen an 320 MB aus `xz -9` (ein Block): 7,3 s
  gegen 2,3 s für `xz -d`, dieselben Bytes — für eine einmalige Installation genug.
- **`lyric env`** nennt `zig` getrennt vom C-Compiler, beide mit der Herkunft `installed` (unter dem Zuhause) oder
  `PATH`, und jede Lint-Stufe als `lint.<name>` mit `command line`, `lyric.toml` oder `default`, dazu
  `lint.warnDeps`; `env` nimmt dafür `-A`, `-W`, `-D` und `--warn-deps` — Punkt 248 ist zu.
- Die Suche nach dem C-Compiler: `LYRIC_CC` > das installierte zig **der gepinnten Version** > `zig`, `clang`, `gcc`
  auf dem PATH. Ein zig anderer Version unter `~/.lyric/zig/` zählt nicht. Nicht gebaut: `toolchain remove zig`
  und eine andere als die gepinnte Version (Tür).

**S8, beim Bauen entschieden** (2026-10-28): S8 ist dreigeteilt — S8a (250, 251), S8b (295), S8c (286, N7, E5, 252),
je ein PR.

- **250 — der ICE-Bericht**: `LYR-ICE0001` schreibt neben Meldung und Trace `lyric5-ice-<zeit>-<pid>.txt` unter
  `out/` des Pakets, in dem der Befehl lief (sonst des Arbeitsverzeichnisses, sonst ins Temp-Verzeichnis), mit
  Version, Befehl wie gegeben, Verzeichnis, Maschine, Stelle, Ausnahme und Trace, und nennt ihn
  (`= note: a report to attach is in …`), wie rustc `rustc-ice-*.txt`. Die Form JSON (16 §4) bleibt, wie sie ist.
- **251 — das Zeitlimit**: jeder Lauf des C-Compilers, des Archivers und des Linkers in `CBuild` hat ein Limit
  (fünf Minuten, `CBuild.Limit`); wer es überschreitet, wird angehalten und ist `LYR-BLD0001` (Exit 2) mit Schritt,
  Limit und Befehl — die Umgebung, kein ICE, auch im parallelen Übersetzen. Nach dem Frontend vergisst
  `CompilerPosition` die Stelle (`End`), so dass ein Fehler auf der C-Seite keine veraltete „while lowering …“-Zeile
  mehr trägt.
- **295 — Lyrics Namen im C** (S8b): ein Feld heißt im C wie im Programm (`x`, nicht `f_x`), ein Lokal ebenso
  (`total`, nicht `l3_total`), wo C den Namen tragen kann — ein ASCII-Bezeichner, kein Schlüsselwort von C11 oder C23,
  kein kleingeschriebenes Makro der Standard-Header (`errno`, `stdin`, `bool` …) oder eines GNU-Modus (`linux`), keine
  Funktion, die das erzeugte C ohne Präfix ruft (`fmod`, `main`), nicht der Implementierung vorbehalten (führender
  Unterstrich, `…_t`), nicht in Makroform (nur Großbuchstaben) und nicht in einer Form, die der Emitter für seine
  eigenen Namen hält (`lyr_`, `Lyr`, `LYR_`, `t3`, `f_…`, `l2_…`) — so treffen ein schlichter und ein gemangelter Name
  nie zusammen. Ein Lokal muss zudem in seiner C-Funktion einmalig sein (alle stehen oben in ihr), ein Feld neben dem
  `header` einer Klasse nicht `header` heißen. Sonst bleibt es bei `f_…` und `l<id>_…`; synthetische Lokale
  (`$fstring` …) und Tupelfelder (`f_0`) bleiben so. `lyric_lldb.py` bleibt (es nimmt `f_` weiter ab); die
  Goldens sind neu, Verhalten und Layout gleich.
- **Gemessen** (S8c) mit `measure.yml` — einem Workflow nur für Messungen, von Hand oder über einen Branch
  `measure/…` gestartet, der keinen Bau scheitern lässt.
- **286 — `lldb-dap.exe` unter Windows**: LLVM 20.1.8s `lldb-dap.exe` endet auf dem Runner mit `0xC0000135` — sein
  `liblldb.dll` linkt `python310.dll`, das LLVMs Installer nicht mitbringt. Mit Python 3.10 auf dem PATH (und mit
  `PYTHONHOME`) endet es ebenso, nun mit `0xC0000409`, ohne Ausgabe. **Entschieden**: `lyric dap` bleibt unter Windows
  gemessen, nicht versprochen (11 E4); der Job `lyric dap on Windows (measured)` bleibt, wie er ist. Ein lldb-dap, das
  binnen 300 ms nach dem Start endet, beantwortet `lyric dap` jetzt mit seinem Exit-Code — für die beiden Codes von
  Windows mit dem Grund und dem Hinweis auf Visual Studios Debugger, der die PDB des Programms liest (seit S8b mit
  Lyrics Namen). Das Book (Erste Schritte, „Debugging“) sagt es. Punkt 286 ist zu.
- **N7 — Objective-C**: `zig cc` 0.16.0 übersetzt eine `.m` gegen Foundation auf dem macOS-Runner, mit und ohne
  `-fobjc-arc`, und das Programm läuft. **Entschieden**: Tür nach 5.0, gebaut mit dem ersten Paket, das Objective-C
  aus der Quelle braucht (die Messung steht, der Weg ist bekannt: die Datei mit ihrer Endung, `frameworks` wie
  heute); raylib unter macOS baut bis dahin aus seinem Archiv (E1).
- **E5 — Sysroot je Tripel**: bestätigt als Tür nach 5.0. zig bringt die libc jedes Ziels mit; ein Sysroot fehlt nur,
  wo ein Paket quer gegen Systembibliotheken eines anderen Systems linkt (Linux außer libc, macOS-Frameworks), und
  das braucht keines der Pakete, die es gibt. Go verlangt dafür `CC` mit Sysroot; das bleibt der Vergleich.
- **252 — der Backtrace-Flake unter Windows**: seit M12b S1 (Lauf 39679825907) nicht wieder gesehen.
  **Entschieden**: Beobachtung mit Uhr M18 (vor der Veröffentlichung); kommt er wieder, wird die Symbolisierung unter
  Windows in einem eigenen Test wiederholt gelaufen, bis sie fällt.

## Nachtrag 2026-10-29 — der Plan für M16

M16 nach seiner Zeile oben und 12 R1–R3: **Migration** — `lyric fix --from-4` liest ein Lyric-4-Programm mit vollem
Typwissen, schreibt die mechanischen Regeln aus 12 R2 als Lyric 5 zurück, meldet die semantischen als `needsReview`
und die von Hand als Bericht, und macht aus `lyric.json` ein `lyric.toml`. Das Artefakt: **die Beispiele von 4.6,
vollständig über das Werkzeug migriert; der Bericht listet jede `needsReview`-Stelle**. Der Befehl heißt bis M18
`lyric5` (11 C1). Grundlage ist der Stand nach M15 S8 (main 23e1f26d), gemessen vor dem Plan:

- **Es gibt kein eingefrorenes 4.x-Frontend.** `src/Lyric.Frontend` ist geteilt; sein 4.x-Modus (`SourceManager.Lyric5`
  aus) ist nicht mehr 4.6: M3–M6 haben Regeln für beide Modi geändert — `xs.length` ist ein Aufruf (`LYR-SEM0012`),
  ein bloßer Variantenname im Muster wird abgelehnt (`LYR-SEM0111`), jede werfende Stelle braucht `try`
  (`LYR-SEM0138`), `resume` ist kein Schlüsselwort mehr, `Throwable` heißt `Error`, ein geschriebenes Feld braucht
  `var`. Ein echtes 4.6-Programm prüft dort nicht.
- **Auch der Korpus ist nicht mehr 4.6.** `examples/*.lyr` und `stdlib/` wurden an diesen Stellen von Hand
  nachgezogen (fünf Commits: `.length()`, `.Red`, `var`, `Error`, `next()!`) — Mischformen. `macros/`, `ffi/` und
  `embedded-host/` sind neu in Lyric 5 geschrieben, `jsonserver/` und `logfilter/` kamen mit M10 dazu. Die Originale
  liegen nur im Zweig `release/v4.6.0` (Spitze 002b7a4b, Vorfahr von main): 22 Programme, `lambdas/`, `patterns/`,
  `macros/comptime.lyr`, `ffi/dotnet.lyr`, `embedded-host/` (C#), dazu `stdlib/` mit 25 Dateien.
- **4.6s Frontend ohne IR**: Lexer, Parser, AST, Resolver und Sema sind rund 16 000 Zeilen (Sema 8 744, davon
  `TypeChecker` 5 953), dazu ein Teil von `Lyric.Core` (Spans, Quellen, Diagnosen, `ProjectFile` für `lyric.json`).
  Sema führt `comptime` nicht aus (die Stellen sammelt es, ausgeführt hat sie der 4.x-Treiber über die VM) — die IR
  mit 13 000 Zeilen und Bytecode/VM bleiben draußen.
- **Was Lyric 5 zum Umschreiben schon hat**: `Replacement` (Span, Text, `Applicability` `Safe`/`NeedsReview`) an einer
  Diagnose-Notiz, `SourceEdits.Apply` (nicht überlappende Ersetzungen von hinten), `FixRun` mit `--dry-run` als
  Unified Diff, `lyric fix` als Verb. Ersetzungen über Spans behalten Kommentare und Layout genau. Der Formatierer
  (`AstFormatter`) druckt, was der Baum hält, übersetzt aber nichts; er hält Kommentare nur auf Zeilenebene.
- **`lyric.json` liest noch `ProjectFile`** (Lyric.Core); ein `lyric.toml` schreibt nur `NewPackage` als festen Text
  und `ManifestEdit` zeilenweise — kein allgemeiner TOML-Schreiber.
- **Orakel**: `tests/Lyric5.Tests/programs/` hält handgeschriebene Lyric-5-Fassungen der meisten Beispiele, mit ihren
  Ausgaben (`ExampleTests`).

| # | Entscheidung | Vergleich / Verworfen |
|---|---|---|
| P1 | **Das 4.6-Frontend eingefroren** (F1): Lexer, Parser, AST, Resolver und Sema aus `release/v4.6.0` (002b7a4b) als eigene Bibliothek `src/Lyric4.Frontend`, Namensräume mechanisch nach `Lyric4.*` (sonst stoßen sie mit dem geteilten Frontend im selben Prozess zusammen), mit den Teilen von `Lyric.Core`, die sie brauchen, als eigene Kopie darin; dazu 4.6s `stdlib/` als eingebettete Ressource, die Sema für die Typen liest. **Eingefroren** heißt: kein Commit ändert diese Dateien außer über das Werkzeug zum Einfrieren; ein Test hält den Inhalt gegen eine Liste mit SHA-256 (wie `toolchain.json`), Korrekturen gehen nur in die Regeln, nie in den alten Checker. Verfallsdatum: eine Major-Linie (F3) | verworfen: den 4.x-Modus des geteilten Frontends zurückdrehen — vier Regeländerungen je Modus wieder trennen und bis M17 pflegen, und M17 nimmt den Modus ohnehin weg (die Zeile „die 4.x-`stdlib/` … M17“); ein Fork über Git-Submodul — ein zweites Repo für einen Kopie-und-friere-ein-Schritt |
| P2 | **Regeln als Besucher über den geprüften 4.x-Baum**: jede Regel aus 12 R2 ist eine Klasse, die über den getypten Baum läuft und `Replacement`s erzeugt — Art M schreibt (`Safe`), Art R wird eine Notiz `needsReview` mit Erklärung und Vorschlag (SL-28), Art H eine Zeile im Bericht. Die Ersetzungen gehen über `SourceEdits.Apply` auf den 4.x-Text: Kommentare und Layout bleiben, gedruckt wird nur, was eine Regel ändert. Ein Durchlauf, keine Schleife wie `FixRun` (die Regeln lesen den 4.x-Baum, nicht ihr eigenes Ergebnis); zwei Regeln, die dieselbe Stelle anfassen, sind ein Fehler des Katalogs, den ein Test findet | `go fix` (AST-Umschreiber mit Typen), `cargo fix` (Ersetzungen aus Diagnosen); verworfen: den Baum ändern und alles neu drucken — Kommentare im Ausdruck gingen verloren, und jede Datei sähe nach dem Formatierer aus |
| P3 | **Bibliothekstabellen als Daten**: die 4.x-Funktionen und ihre 5er-Form (E01 `xOrThrow` → `try x`, E02 die stillen Formen, B01–B13, K04 die Modulpfade, T11) stehen in einer Tabelle neben den Regeln (`from4.toml`, eingebettet), je Eintrag 4.x-Name, Modul, Form in 5, Art; die Regeln lesen sie über das Typwissen (welche Funktion ein Aufruf trifft). Ein Test hält jede 4.6-`pub`-Funktion gegen die Tabelle: steht sie nicht drin, ist sie gleich geblieben oder ein Fehler der Tabelle | `go fix`s feste Tabellen; verworfen: eine Regel je Funktion (130 Klassen) |
| P4 | **Der Treiber**: `lyric fix --from-4 [pfad]` — ein Verzeichnis mit `lyric.json` (dessen Quellen und Abhängigkeiten) oder eine Datei. Er prüft mit dem eingefrorenen Frontend; ein 4.x-Programm, das schon dort nicht prüft, wird nicht angefasst (`LYR-FIX…`, Exit 1 — erst 4.6 grün, dann migrieren). Schreibt um in place, `--dry-run` zeigt den Unified Diff, `--json` die Notizen nach 16 §4; macht `lyric.toml` aus `lyric.json` (K02) und legt die Quellen nach `src/` (das vorgeschlagene Layout, Datei für Datei mit `git mv`-Hinweis, nicht ausgeführt); der Bericht (`fix-from-4.txt` neben dem Manifest) listet jede Notiz mit Regel, Stelle, Grund und Vorschlag. Danach sagt er: `lyric5 check` | `cargo fix --edition`; verworfen: in einen neuen Ordner schreiben (die Versionskontrolle des Nutzers ist der Rückweg) |
| P5 | **Codes**: die Notizen bekommen einen Bereich `MIG` mit einem Code je Regel der Art R und H (`LYR-MIG0101` für K06 usw.), im Katalog mit Erklärung; die Regeln der Art M brauchen keinen Code (sie schreiben). `lyric explain LYR-MIG…` sagt, was die Regel prüft und was zu tun ist | 16 G-Reihe |
| P6 | **Tests je Regel** (12 R2): `tests/fix-from-4/<regel>/` mit `before.lyr` (4.6), `after.lyr` (erwartet) und `notes.txt` (die Notizen); der Läufer prüft das 4.x-Programm mit dem eingefrorenen Frontend (muss grün sein), wendet an, vergleicht, und prüft `after.lyr` mit Lyric 5 (`lyric check` grün außer den genannten `needsReview`-Stellen). So beweist jeder Fall beide Seiten | — |
| P7 | **Abnahme (R3 Q3)**: die Beispiele von 4.6 kommen eingefroren nach `tests/fix-from-4/examples-4.6/` (die Eingabe); das Werkzeug migriert sie, das Ergebnis ersetzt `examples/` auf main (die Mischformen fallen; `macros/`, `ffi/`, `embedded-host/`, `jsonserver/`, `logfilter/` bleiben, wie sie sind — ihre 4.6-Fassungen sind `.NET`-FFI, C#-Host und VM-`comptime`, Art H). Jedes Beispiel muss danach mit Lyric 5 bauen und dieselbe Ausgabe geben wie sein Orakel in `programs/`, oder die Abweichung steht im Bericht mit Grund. Was nicht baut, ist eine fehlende Regel (R2 wird ergänzt, mit Fall) oder eine `needsReview`-Stelle — von Hand gelöst, und der Bericht nennt es. `fibonacci` bekommt seine Koroutinen-Form (Uhr M16) | 12 R3 Q3 |
| P8 | **Spec**: 14 bekommt `fix --from-4` (die Option, was es liest und schreibt, Exit-Codes, der Bericht) als informativen Abschnitt — F3: kein Versprechen, dass das Ergebnis kompiliert; die Regeln stehen in 12 R2 und im Katalog (`MIG`), nicht in der Spec. Keine Konformanzfälle: die Fälle aus P6 prüft das Werkzeug selbst | wie M15 P12 |

| Slice | Inhalt | Artefakt |
|---|---|---|
| **S1** einfrieren | P1: `src/Lyric4.Frontend` aus 002b7a4b, Namensräume, die Core-Kopie, 4.6s `stdlib/` als Ressource, das Werkzeug zum Einfrieren (`tooling/from4/freeze.py`) und die Liste mit SHA-256; ein API `From4.Check(files)` → getypter Baum + Diagnosen | die 22 Beispiele und `lambdas/`, `patterns/` von 4.6 prüfen mit dem eingefrorenen Frontend ohne Fehler; ein Programm, das 4.6 abgelehnt hat, wird abgelehnt; der Einfrier-Test rot bei einem geänderten Byte |
| **S2** Treiber und Gerüst | P2, P4, P5: `lyric fix --from-4`, die Regel-Schnittstelle, die Anwendung, `--dry-run`/`--json`, der Bericht; K01–K08 (Modulkopf, `lyric.json` → `lyric.toml`, Imports, Prelude, Sichtbarkeit als R, `build.lyr` als R, `stdlib-tests` als H) und der Läufer aus P6 | `hello.lyr` von 4.6 migriert und baut mit Lyric 5; je Regel K ein Fall |
| **S3** Syntax | S01, S04–S08, S10, S11 | je Regel ein Fall |
| **S4** Interfaces und Werte | T01–T05, T08–T16, T18 (P3: die Tabelle beginnt) | je Regel ein Fall |
| **S5** Fehler und Koroutinen | E01–E09, N01–N02 (die Tabelle der werfenden und stillen Formen) | je Regel ein Fall |
| **S6** Bibliothek | B01–B13 über die Tabelle (P3); der Test, der jede 4.6-`pub`-Funktion gegen die Tabelle hält | je Regel ein Fall; die Tabelle vollständig |
| **S7** Abnahme | P7: `examples-4.6/` eingefroren, migriert, `examples/` ersetzt, Ausgaben gegen die Orakel, der Bericht; was fehlt, wird Regel | die Zeile: `examples/` aus dem Werkzeug, Bericht mit jeder `needsReview`-Stelle |
| **S8** Abschluss | P8 (Spec 14), STATUS, CHANGELOG, Bericht, der Plan für M17 | — |

Nach Aufwand: groß S1, S4, S6; mittel S2, S5, S7; klein S3, S8 — **L** wie die Zeile, mit S1 als Unbekannter: die
Namensräume und die Core-Kopie können mehr kosten als gedacht, wenn 4.6s Sema an Stellen hängt, die der Plan nicht
sah. Läuft M16 über das Doppelte, wird es hier neu geschnitten (CONTRIBUTING); der erste Kandidat zum Abtrennen ist
S6 (die Bibliothekstabelle ist lang, jede Zeile klein). Die Reihenfolge folgt den Abhängigkeiten: ohne das Frontend
(S1) keine Typen, ohne den Treiber (S2) kein Fall; S3–S6 sind unabhängig voneinander; S7 braucht alle.

**Plattformen**: S1, S2 und S7 fahren den vollen Plattformlauf (Dateipfade, `lyric.toml`, Zeilenenden); die Regeln
sind Text und laufen überall gleich.

**Reflexion**: Die Entscheidung F1 stand — ein 4.x-Frontend mit Typwissen statt Textregeln, weil `abs(-3)` und `var`
ohne Typ nicht zu entscheiden sind. Offen war, dass es dieses Frontend nicht mehr gibt: das geteilte hat sich seit M3
in beiden Modi bewegt, und der Korpus mit ihm. Eine eingefrorene Kopie von 4.6 ist die ehrliche Lesart von F1 — die
Sprache, aus der migriert wird, ist 4.6, nicht der Zustand, den das geteilte Frontend heute „4.x“ nennt. Go hat es
einfacher (`go fix` liest dieselbe Sprache, die es schreibt); rustc's Editionsmigration läuft im selben Compiler über
Lints, weil Rust seine alten Editionen weiter spricht — Lyric 5 spricht 4 nicht mehr, darum die Kopie. Die Gefahr ist
die Versuchung, den alten Checker zu reparieren; die Einfrier-Liste verbietet es, und jeder Fehler wird eine Regel.
Die Ersetzung über Spans statt Neudruck folgt `cargo fix`: wer migriert, will seinen Code wiedererkennen.

**Nicht in M16**: die 4.x-`stdlib/` und der 4.x-Modus des geteilten Frontends (M17); Lyric-Script (`.lyrs`) hat
sein eigenes Repo; ein Migrator für Erato (Lyricpp, Z-Reihe); `--edition` (mit der ersten Edition).

**S1, beim Bauen entschieden** (2026-10-29):

- **Was eingefroren ist**: 4.6s `Lexing`, `Parsing`, `AST`, `Resolver`, `Sema`, dazu die eine Datei der IR, die der
  Checker fragt (`Ir/Lowering/HostTypes.cs`: welche leere Klasse ein Host-Typ ist), 18 Dateien von `Lyric.Core`
  (Spans, Quellen, Diagnosen, Escapes, `ProjectFile`, `CliDiagnostics`) und 4.6s `stdlib/` als Ressourcen — 122
  Dateien mit den 30 Beispielen. Die einzige Änderung ist der Namensraum `Lyric.` → `Lyric4.` und eine Kopfzeile je
  Datei; gebaut hat es beim ersten Mal bis auf zwei fehlende Dateien, die jetzt in der Liste stehen.
- **Die Beispiele von 4.6 sind mit eingefroren** (`tests/fix-from-4/examples-4.6/`, nur `.lyr`; `embedded-host/` war
  C#) — die Eingabe der Abnahme (S7) liegt damit im Baum, nicht nur in einem Zweig.
- **`From4/` ist eigener Code** neben der Kopie: `CheckFile`, `CheckProject` (über `lyric.json`) mit 4.6s Lader, die
  Bibliothek aus den Ressourcen über das Overlay des alten `StdlibLoader` — kein Byte der Kopie geändert.
- `CatalogTests` lässt die eingefrorenen Verzeichnisse aus: ihre Codes sind 4.6s, nicht die dieses Katalogs.
