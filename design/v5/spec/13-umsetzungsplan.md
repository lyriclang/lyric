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
| **M6 Koroutinen, Scheduler, Threads** | L4 + 06: Assembler-Switch je ABI (K2), Stacks mit Guard-Pages (K1), Fremd-Frame-Zähler (K4), `park`/`unpark`, Scheduler je Thread (G1), Poller in C (S2: epoll/kqueue/wepoll), `spawn`/`Task`/`TaskScope`/`spawnDetached`, `Channel`/`Select`/`Timer`/`sleep`/`timeout`, `Cancelled`, Threads (G2) + `Mutex`/`RwLock`/`Once`/`Atomic`, **Generatoren als Iteratoren** (I7, `Coroutine :: Closeable`), Signale als Kanal (Q9) | `generator.lyr`, Channel-Pingpong, Thread-Pool-Test, Ctrl+C-Shutdown-Beispiel; Konformanz 06; TSan-Profil grün | XL |

### Phase 2 — Bibliothek und Module

| M | Inhalt | Artefakt | Größe |
|---|---|---|---|
| **M7 Module und Pakete** | 07 + W2: Sichtbarkeit `private`/`internal`/`pub`, Modulname aus Pfad, `pub import`, Prelude (`std.prelude`, B2), Editionen, **`lyric.toml`** (P-Reihe), Pfad + Git, MVS, `lyric.lock` mit Hashes, `[native]`, Profile (P3), Ziele (P4, Runtime aus Quelle je Tripel T3), `out/`-Sperre, **Reproduzierbarkeits-Test** (P6), `lyric metadata`, `lyric clean/add/update`; **`build.lyr` (BS1–BS6) kommt nach M8b** — ein Build-Skript braucht `std.fs` und `std.process` | Mehrpaket-Beispiel mit Git-Abhängigkeit baut offline; Reproduzierbarkeit zweimal aus zwei Verzeichnissen; Cross-Build linux→windows | L |
| **M8a std-Kern** | B2–B7, B9, B12: `core` (Kern-Interfaces, `Num`-Turm, `Result`, `Box`, `Slice`/`StringView`/`T[]`-Member, Ranges), `iter` (Adapter als Extends, `Error`-Typ, `collect`), `collections` (`List`, Swiss-Table `Map`/`Set`, `Deque`, `Heap`, `+`/`*`), `string` (`StringView`, `Pattern`, `StringBuilder`; die Unicode-Tabellen: M8c), `fmt` (Formatsprache, f-String-Lowerung über `showTo`), `math`, `hash` (`Hasher`, SipHash), `test` (Assertions, `@callerExpr`, Subtests, `lyric test`; `@Bench`: M8c); dazu die Entscheidungen des Reviews vom 2026-10-05 (Nachtrag unten) | `lyric test` läuft die std-Tests; `inventory/stats/stack`-Beispiele; **Messpunkt 3**: Map/Sort/String-Benchmarks gegen Go/C# | XL |
| **M8b std I/O und System** | B8, B11: `io` (Reader/Writer/Seek, Puffer/Text, `ByteBuffer`, Konsole), `fs`, `path`, `net`, `process`, `os`, `time` (Duration/Instant/Monotonic/Date; Zone später M10), `random` (ChaCha8), `encoding`, `crypto` (Digests, HMAC, `randomBytes`), `sync`/`thread`-Feinschliff (Modulumzug nach 10 Q10, `Semaphore`, `parallelMap`), der **Windows-Poller über AFD** (06 S2), danach `build.lyr` | Echo-Server über `std.net`, Prozess-Pipeline-Beispiel, Datei-Werkzeug; **hier ist Lyric 5 für Alltagswerkzeuge benutzbar** (Dogfood: `stdlib-tests` und Beispiele vollständig) | L |
| **M8c std-Rest** | Was der Zuschnitt vom 2026-10-05 aus M8a herausnimmt: Unicode-Tabellen aus UCD-Dateien, `char`-Prädikate, `toUpper`/`toLower`; die übrigen Adapter und Terminatoren (`flatMap`, `chunks`, `peekable`, `rev`, …) mit werfenden Lambdas; Set-Operationen, `map[k]`, `list[a..b]`, List-Extras; `Result`, `From`/`Into`; `std.fmt.format`; die Wertform `Iterator<Item = T>`; `suppressed()`/`backtrace()` an der `catch`-Bindung (05 O3); `@Bench` und `lyric bench` | Wortzähler über Unicode-Text; `lyric bench` läuft die std-Benchmarks | L |
| **M9a `comptime`** | 09 A-Reihe: **IR-Interpreter** (die L7-Tür, hier gebaut) mit Budget, `comptime`-Ausdrücke/Blöcke/`if`/`for`, `std.meta` (R1–R6), `embed`, `@When`, Attribute Art 1/2 (`@Deprecated`, `@Allow`, `@MustUse`, `@Inline`-Familie, `@Layout`), **Synthese nach A5** (`Equatable`/`Hashable`/`Ordered`/`Clone`/`Default` als `comptime`-Defaults in `std.core`, M4-Provisorium fällt), Enum-Reflexion (R5) | `constants.lyr`, Synthese-Konformanzfälle; `comptime`-Tabellen-Beispiel | L |
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
| 3 | M8a | `Map` einfügen/suchen, Sort, String-Ops | ≤ 1,5× Go — bei M8a verfehlt (1,5× bis 2,5×); Ratchet mit Uhr: erreicht nach M11, sonst neu entscheiden (Review C3) |
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

---

**Damit ist die Runde vollständig** (Bereiche 0–12 entschieden, 2026-09-28/29). Die Dokumente
`design/v5/spec/00–13` sind die Grundlage der Spec 5.0 (R4) und der Arbeitsplan (M0–M18).
