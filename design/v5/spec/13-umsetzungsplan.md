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
| **M12 Diagnostik und Lints** | W6/W7: Record mit beschrifteten Spans und Notizen, rustc-Ausgabe, Folgefehler-Vergiftung, NDJSON, `lyric explain`, `diagnostics.toml` → Appendix, Wächter (G10), **alle Lints** mit rot/grün, `--deny-warnings`, Fixes mit `applicability`, `lyric fix` (5-intern); die Noten zu verborgenen Kandidaten und verdeckten Prelude-Namen (07), der Lint für fremde Konformanzen (03 X3) | Katalog vollständig, jede Warnung getestet; `lyric fix` wendet `safe`-Fixes an | L |
| **M13a fmt, doc, api** | E5/E7: Formatter mit Trivia (Kommentare, Klammern), Semantik-Erhalt-Test, `lyric doc` (Site aus `///`), `///`-Doc-Tests, `lyric api` + `--diff` | Repo hält `lyric fmt --check`; Doc-Site der std | M |
| **M13b LSP, DAP, Clients** | E1–E4, E8: `lyric lsp` mit modulgranularer Wiederverwendung, Code Actions, Tests im Editor; `lyric dap` über lldb-dap + `lyric.lldbinit` (Summaries, Panik-Breakpoint, Tasks-Scope); `tree-sitter-lyric`; VS Code/JetBrains-Clients auf 5 | Editor: Hover/Completion/Rename/Fix, Test-CodeLens; Debugger hält an Panik mit Lyric-Werten; Windows-lldb-Risiko gemessen | XL |
| **M14 FFI und Einbettung** | W4/W5 nutzerseitig: `extern "C"` vollständig, `@Layout`, `Ptr`/`CStr`/`unsafe`, `ffi.Callback`/`GcHandle`, fremde Threads (X7), `@Export` + Header, `lyric build --lib`, `LyrConfig`/`LyrStatus`/`lyr_step` (H1–H6), `ffi.Library` | `examples/ffi` (C-Bibliothek gebunden), `embedded-host` in C gegen `lib<app>.a`; Spec `13-abi` | L |
| **M15 Distribution** | W9: Archive Tier 1, minisign, `lyric toolchain install/list/default/update`, Go-1.21-Re-exec nach `[package] toolchain`, `lyric toolchain install zig`, `dev`/`stable`-Kanäle, Website-Mirror-Haken (T8) | `lyric toolchain install dev` auf drei OS; Release-Pipeline probeweise `5.0.0-dev` | M |

### Phase 4 — Abschluss

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M16 Migration** | R1/R2: eingefrorenes 4.x-Frontend als Bibliothek, `lyric fix --from-4` mit allen R2-Regeln, `tests/fix-from-4/` (vorher/nachher je Regel), `lyric.json` → `lyric.toml` | `examples/` vollständig über das Werkzeug migriert; Bericht listet jede `needsReview`-Stelle | L |
| **M17 Spec 5.0, Suite, Guide** | R4: Lückenschluss der Spec-Kapitel (der Rest entstand je Meilenstein), Konformanzsuite vollständig (`since: 5.0.0`), Guide 5 (Kapitel je Bereich, Snippets in der Suite), Katalog-Appendix generiert, Spec-Pin | Spec 5.0 getaggt; Suite grün; Guide-Suite grün | L |
| **M18 Release 5.0.0** | Release-Checkliste (Version + README + Ratchets zusammen, Tag nach grüner CI auf `main`, Spec-Pin zuletzt), der 4.x-Treiber verlässt `main` und **`lyric5` wird `lyric`** (11 C1), `lyric-script 1.0.0`-Umbenennung (R6), Archivierung, Website | `5.0.0` auf GitHub + Website; `lyric-script 1.0.0` daneben | M |

## Abhängigkeiten

```
M0 → M1 → M2 → M3 → M4 → M5 → M6
                 │      │    │    └→ M8b → M10
                 │      │    └→ M14
                 │      └→ M8a → M9a → M9b
                 └→ M7 ─┴→ M13a/M13b (nach M12)
M2 → M11 (parallele Spur, jederzeit; vor M10 für Messpunkt 5)
M2 → M12 (wächst mit jedem Meilenstein; Abschluss nach M9b)
M7 → M15
M8b → M16 (fix braucht die Zielbibliothek)
M8b → M8c (std-Rest; vor M10, dessen `regex` die Unicode-Tabellen braucht)
alle → M17 → M18
```

**Kritischer Pfad**: M1 → M2 → M3 → M4 → M5 → M6 → M8b (erste Alltagstauglichkeit) → M9a →
M10 → M17 → M18. Parallel dazu: M11 (GC) und M12 (Diagnostik) als eigene Spuren; M13 und M14
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
