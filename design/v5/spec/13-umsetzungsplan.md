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

---

**Damit ist die Runde vollständig** (Bereiche 0–12 entschieden, 2026-09-28/29). Die Dokumente
`design/v5/spec/00–13` sind die Grundlage der Spec 5.0 (R4) und der Arbeitsplan (M0–M18).
