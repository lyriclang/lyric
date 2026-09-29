# 11 — Werkzeuge und Interop

Lebendes Dokument des Bereichs 11. Fragen W1–W9, je **entschieden** oder **offen**. Basis:
`../cli.md` (39 Fragen), `../build-pakete.md` (33), `../ffi.md` (34), `../editor-werkzeuge.md`
(34), `../diagnostik.md` (32) — und die Vormerkungen der Bereiche 0–10 (Abschnitt „Schon
entschieden").

## Bestandsaufnahme Lyric 4

- **CLI**: elf Exe-Projekte, neun startbare Werkzeuge (`lyrc lyrvm lyrbuild lyrtest lyrfmt
  lyrpack lyrrepl lyrls lyrdbg`) hinter einem Treiber `lyric` mit neun Verben (`new run build
  pack fmt test check disasm repl`), der Argumente durchreicht, abschneidet oder auf zwei
  Werkzeuge verteilt (`--jit`/`--grant` an die VM, Rest an den Compiler) und dafür eine zweite
  Kopie der Optionsarität führt; `lyric --version` kennt sieben der neun; kein `clean`, `doc`,
  `update`, `toolchain`; zwei Profile (`debug`, `release`); `LYRIC_PROFILE` wählt Profil **und**
  schaltet den Instruktionszähler; `lyrpack` klebt Stub + Modul (nur `win-x64`-Stub geliefert).
- **Projekt**: `lyric.json` (sechs Schlüssel, JSONC), Abhängigkeiten nur lokale Pfade, kein
  Lock, keine Versionen, kein Cache, keine kompilierte Bibliothek — „der ganze Abschluss ist ein
  Compile"; Artefakt byteweise reproduzierbar (gemessen); `build.lyr` als Programm über
  `std.build` (`executable`, `library`, `packed`, `option`, `Profile`, `after()`), das jedes
  Artefaktfeld frei setzen darf; `main.lyr` ist das eine Programm.
- **FFI**: **vier** Grenzen (Stdlib-Native, Native-Root, `RegisterFunction`, `extern "dotnet"`)
  mit vier Typtabellen, vier Fehlerpolitiken, vier Gates; `hostAccess` ist ein **Bypass** der
  Capability-Tabelle (gemessen), `extern` kennt nur Skalare + `string`; ein `extern` kann einen
  String mit halber Surrogate erzeugen, Absturz später mit CLR-Stacktrace und Exit 127; `extern`
  fehlt in Spec §4/§11/§13; keine Export-Richtung, kein C.
- **Editor**: LSP, DAP, Formatter, REPL, DocGen, zwei Clients — Breite wie Swift/C#; Tiefe fehlt:
  kein inkrementeller/abbrechbarer Compile (jeder Tastendruck kompiliert die Welt, 93–106 ms
  je 5-Zeilen-Datei, 0,3–1,9 s je 61-Modul-Projekt), keine Code Actions, Debugger ignoriert
  `condition`/`hitCondition`/`logMessage` still und läuft mit `Capability.All`, Panik beendet
  die Sitzung, REPL wiederholt Seiteneffekte und zeigt keine Warnungen, zwei Deutungen von `///`,
  kein `lyric doc`, Profiler unerreichbar; Debugger/Budget/Profiler sind **Interpreter**-Werkzeuge.
- **Diagnostik**: `(Code, Severity, Span, Message, Notes?)`, vier Severities, Severity gehört dem
  Code (§12.1), Katalog per Wächtertest gegen `appendix-a`, „did you mean", Terminal-Injection
  geschlossen, Panik mit Code + Backtrace (Exit 101); aber `IR0001` als Sammelcode, kein
  Unterdrücker im Quelltext, `--deny-warnings` alles-oder-nichts, keine anwendbaren Fixes, ein
  Span je Diagnose, kein `explain`, JSON-Vertrag ungeschrieben, Migrationsfamilie
  `SEM0107–0110` immer an ohne Schalter.

## Schon entschieden (Bereiche 0–10) — wird hier nicht neu verhandelt

| Dossier | Antwort | Wo |
|---|---|---|
| CLI-16/35/36, F1/F14/F27/F33 (Capabilities, `hostAccess`, `--grant`) | **entfallen**: keine Capabilities in Lyric 5 (Sandbox → Lyric-Script) | 00 Z3 |
| F2 (vier Grenzen) | **eine Grenze**: `extern "C"` ist die ABI selbst; kein `"dotnet"` | 00 Z5, 08 D15 |
| F5/F6 (Host-Fehler, `defer` über die Grenze) | Wurf darf C nicht durchqueren; Callback nicht-werfend oder Wrapper mit Status; `defer` läuft innerhalb der Lyric-Frames | 01 E6/E7 |
| F16 (Callbacks aus fremden Threads) | Queue + Lyric-Handler; Scheduler je Thread, Koroutinen migrieren nicht | 06 G1, Design-Runde 2026-09 |
| CLI-26, BP-08/09 (Cache, kompilierte Bibliothek) | Quelle ist das Format; Build-Cache ohne Versprechen; kein kompiliertes Bibliotheksformat | 01 L7 |
| BP-01/02/05/06/07 (Manifest, Version, Quellen, Lock, Auflösung) | `lyric.toml`, Paketversion, Pfad + Git, `lyric.lock` mit Hashes, MVS, keine Registry in 5.0 | 07 P1–P8 |
| BP-13/18 (Toolchain-Pinnung, Sprachfassung) | Editionen; ein Compile spricht eine Fassung je Paket | 07 V-Editionen |
| BP-25 (Tests im Paket) | `@Test` im Modul oder `tests/`-Root, gleiche Kompilation | 07 V4 |
| BP-33 (std an Toolchain) | ja, ein Ring | 10 U3 |
| D3/D4/D13 (Unterdrückung, `--deny-warnings`, `@Deprecated`) | `@Allow(code)` + `[lints]` im Manifest; `@Deprecated { message, until, replacement }` | 09 A10/A11 |
| D14/D19–D22 (Migrationsfamilie 4.x) | gegenstandslos: harter Cut, keine 4.x-Warnstufen für 5 | 00 Migration |
| D18 (Panik als Diagnose) | Panik = Code + Backtrace, Exit 101; Fehler aus `main` Exit 1 | 05 O4/E8 |
| E11 (Debug = Interpreter), E10 (Profiler) | gdb/lldb/perf ab Tag eins; DAP = Adapter über lldb-dap; kein eigener Debugger | 01 L12 |
| E12/E31 (`///`, Doc-Tests) | `///` Markdown, `lyric doc`; `///`-Codeblöcke laufen als Tests | 09, 10 X5 |
| E2/E28 (Fixes) | `@Deprecated.replacement` speist `lyric fix`; Bereich 12 für Migrationsregeln | 09 A11 |
| Q7 (`lyric expand`), B3 (`lyric api`) | beschlossen als Verben | 08 Q7, 07 B3 |
| Signale im eingebetteten Runtime | keine Handler (`installSignalHandlers = false`) | 10 Q9 |
| Bare-Metal | Laufzeitprofil-Tür, nicht 5.0 | 10 Q9 |

## Fragen

Reihenfolge: W1 (CLI) → W2 (Projekt, Profile, Ziele) → W3 (`build.lyr`) → W4 (FFI) →
W5 (Einbettung, Script-Nesting) → W6 (Diagnostik) → W7 (Lints) → W8 (Editor, Formatter, REPL,
Doku) → W9 (Distribution).

## W1 — CLI: ein Binary, Verben, Optionen, Ausgabe: **entschieden** (2026-09-29)

Grundlage verschoben: keine VM (L7) — der Grund für den 4.x-Werkzeugkasten („`lyrvm` ohne
Compiler") entfällt; der C#-Compiler wird als NativeAOT-Binary ausgeliefert (W9).

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| C1 | **Ein Binary `lyric` mit Verben** (CLI-1 B); `lyric lsp`/`lyric dap` sind Verben (stdio). Kein Treiber, keine doppelte Optionsarität, keine drei Prozessstarts | Cargo, Go, Zig, Deno; verworfen: Werkzeugkasten (4.x), PATH-Erweiterungen (Tür) |
| C2 | **Verben 5.0**: `new`, `init`, `build`, `run`, `test`, `bench`, `check`, `fmt`, `fix`, `doc`, `api`, `expand`, `explain`, `clean`, `add`/`remove`, `update`, `metadata`, `env`, `lsp`, `dap`, `toolchain`, `version`, `help`, `completions`. **Weg**: `pack` (das native Binary ist das Artefakt), `disasm`/`verify` (`lyric build --emit c\|ir`), `repl` (W8). Türen: `publish`, `vendor`, `watch` | CLI-2 C |
| C3 | **Optionsgrammatik**: `--flag value`/`--flag=value`, Kurzflags, `--no-X`, globale Optionen vor oder nach dem Verb (`-q`, `-v`, `--color`, `--json`, `-C dir`), **streng** (unbekannt = Exit 2), `--` trennt Programmargumente; **eine Optionstabelle je Verb** erzeugt `--help`, Completions (`bash zsh fish pwsh`) und die Spec-Tabelle | Cargo, Go `flag` |
| C4 | **Präzedenzleiter** (CLI-21 A): Kommandozeile > `LYRIC_*` > `lyric.toml` > benutzerweite `config.toml` > Vorgabe; Feldflag > Profilflag; `lyric env` zeigt Werte mit Herkunft | Cargo, `go env` |
| C5 | **Exit-Codes** (CLI-32 A): 0 ok · 1 Eingabe abgelehnt (Compilefehler, rote Tests, `deny`-Lint) · 2 Kommandozeile/Umgebung falsch · 101 Panik der Toolchain; `lyric run`/`test` **reichen den Programm-Exit durch** (Panik 101, entkommener Fehler 1, 05 O4); `fn main(): int` wird `& 0xFF` (dokumentiert), Warnung bei konstantem `return` außerhalb `0..255`; **kein `ExitCode`-Typ** | Cargo; verworfen: Rust `ExitCode` (CLI-12 E), Sättigung, sysexits-Stufen |
| C6 | **`--json`** = NDJSON-Ereignisstrom (Diagnosen, Artefakte, Testergebnisse, Fortschritt) mit Schema in der Spec (Diagnoseform W6); `--quiet` unterdrückt nur Fortschritt | Cargo `--message-format=json` |
| C7 | **Terminal**: `--color auto\|always\|never`, `NO_COLOR`, `TERM=dumb`, Fortschritt nur am TTY; Diagnoseausgabe rustc-artig (W6) | rustc |
| C8 | **Datei oder Projekt**: `lyric run` = Standard-Binary des Pakets (sucht `lyric.toml` aufwärts); `lyric run pfad.lyr` = **Einzeldatei als implizites Paket** (Skript-Modus, nur `std`); `--bin name` bei mehreren | Go, Cargo |
| C9 | **Kosten** (CLI-25): Whole-Program-Compile je Lauf über den Cache (L7) — unverändert = kein Compile; Ratchets in Verhältnisform: Hello-World kalt ≤ 400 ms, **warm ≤ 50 ms über dem nackten Programmstart**; NativeAOT-Compiler + `zig cc` im selben Prozessbaum | Zig, Go |
| C10 | **CLI = versionierter Vertrag** (CLI-30): Verben, Optionen, Exit-Codes, JSON-Schema in der Spec; ein Verb/Flag fällt nur in einem Major, vorher `deprecated` in `--help` und beim Aufruf; `lyric --version` nennt Compiler, Edition, `std`, C-Compiler | Go 1 |

## W2 — Projektmodell: Manifest-Schema, Profile, Ziele, `out/`: **entschieden** (2026-09-29)

Neu gegenüber dem Dossier: mit dem C-Backend ist **Cross-Compilation real** (`zig cc`), das
Artefakt ist ein natives Binary je Ziel.

```toml
[package]
name = "app"            # Pflicht; Modulpräfix (07)
version = "0.1.0"       # Pflicht, SemVer
edition = "5"
description = "…"  license = "MIT"  repository = "…"  authors = ["…"]
include = […]  exclude = […]        # Abweichung von der Vorgabe (P8)
toolchain = ">=5.1"                 # P12

[[bin]]                 # optional; Konvention: src/main.lyr = das eine Programm
name = "tool"  entry = "src/tool.lyr"

[dependencies]
geo  = { path = "../geo" }
http = { git = "https://…", tag = "v1.2.0" }
util = "1.2"            # Registry-Form: Tür (07 P8)

[native]                # 07 B5
sources = ["native/*.c"]  include = ["native/include"]  libs = ["z"]
[native.windows]  libs = ["ws2_32"]

[lints]                 # 09 A10
deny = ["unused-result"]  allow = ["dead-code"]

[profile.release]       # eingebaut: debug, release
opt = 2  lto = true  debugInfo = false
[profile.staging]
inherits = "release"  debugInfo = true
```

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| P1 | **Manifest wird gelesen, nie ausgeführt** — Profile, Binaries, Abhängigkeiten, Lints stehen in `lyric.toml`; `build.lyr` (W3) *definiert* davon nichts | Cargo; gegen Zig (`build.zig` als Wahrheit) |
| P2 | **Programme**: `src/main.lyr` per Konvention, weitere über `[[bin]]` (07 M7d) — nicht über ein Skript, nicht `src/bin/*` (BP-32: kein vierter Weg); ohne beides ist das Paket eine Bibliothek | Cargo `[[bin]]` |
| P3 | **Profile** (BP-12 B + BP-24 D): `debug`/`release` eingebaut, benannte mit `inherits`; Felder `opt`, `lto`, `debugInfo`, `denyWarnings`, `overflowChecks` (nur explizit, T2), `fastMath` (L10); Kommandozeilen-Feldflag > Profil; unbekannter Profilname = Fehler (auch aus `LYRIC_PROFILE`) | Cargo |
| P4 | **Ziele**: `--target x86_64-linux-gnu` (Zig-Tripel), Vorgabe = Wirt; Cross-Compile über `zig cc`; `[native.<os>]` je Ziel; **`out/<profil>/<ziel>/<name>`** — Zielachse immer im Pfad (BP-15/19); kein Zielverzeichnis im Manifest | Zig, Go |
| P5 | **`out/` liegt beim Manifest** (BP-23 D); Projektsuche endet am nächsten Manifest, sonst `.git`/Wurzel; Bau aus Unterverzeichnis **nennt** das Projekt; `out/cache/` (L7), `out/.lock` (flock, BP-30); `lyric clean` = `out/` löschen | Cargo `target/`, Go |
| P6 | **Reproduzierbarkeit als Zusage + Konformanztest** (BP-29 C): gleiche Quelle, Optionen, Toolchain **und C-Compiler** ⇒ gleiche Bytes; keine Zeitstempel, `-ffile-prefix-map`, deterministische Emission (L10) | Go `-trimpath`, Nix |
| P7 | **Fremder Entry ist ein Fehler** (BP-14 D) mit Hinweis auf Pfadabhängigkeit; **Workspaces Tür** (`[workspace] members`, 07 P9) | Go, Cargo |
| P8 | **Paketinhalt** (BP-28 D): Vorgabe = Manifest, `src/`, `native/`, `build.lyr`, `README*`, `LICENSE*`; nicht `tests/`, `out/`; `include`/`exclude` weichen ab; der Lock-Hash (07 P5) läuft über diese Menge | Cargo |
| P9 | **Offline**: Git-Abhängigkeiten im Benutzer-Cache (`~/.cache/lyric/git`), `--offline`; `lyric vendor` Tür | Cargo |
| P10 | **`lyric metadata --json`** (BP-27): Paketgraph, Binaries, Profile, Ziele, `out/`-Pfade | Cargo `metadata` |
| P11 | **Build-Optionen ins Programm** (BP-17): nur `target.*` und `profile.*` als `comptime`-Konstanten (09 B4); `-D key=value`/Features **Tür** | Zig `-D…` verworfen für 5.0 |
| P12 | **Toolchain-Pin**: `[package] toolchain = ">=5.1"` (Fehler mit Hinweis auf `lyric toolchain install`); Edition wählt die Sprachfassung (07) | Rust `rust-version` |

## W3 — `build.lyr`: Programm, Daten oder Graph: **offen**

BP-10/11, CLI-17, SL-29. Reicht das Manifest (Cargo ohne `build.rs` für 95 %)? `build.lyr`
als Programm über `std.build` (4.x), als Zig-Graph (`std.Build`), oder nur als Hook für
Codegen (`gen/`, 09) und native Teile; Modulraum, Abhängigkeiten, Rechte, wann es läuft.

## W4 — FFI: `extern "C"`, Typen an der Grenze, Export, Bindungen: **offen**

F3/F4/F7–F13, F15, F17–F21, F25, F28–F31, F34. Form (`extern "C" fn`, Symbolname, Bibliothek),
Typen die kreuzen (`CStr`, `Ptr<T>`, `@Repr(C)`-Structs, Callbacks, Arrays/Slices, `string`
auf dem Draht), `unsafe`?, `@Export("name")` und `lib.a`-Bau, wer Bindungen schreibt
(`lyric bindgen` über libclang?), Linking über `[native]`, Variadics, Versionierung, Tests
hinter `extern`, Optimierer an der Grenze, `std.ffi`-Oberfläche.

## W5 — Einbettung und Script-Nesting: **offen**

00 Z4/Z5, F22–F24, E21. Lyric 5 als C-Bibliothek in einem Host (Erato): Runtime-Objekt,
Init/Shutdown, Scheduler-Pumpe (S5), Exporte, Objektlebensdauer über die Grenze, Reentranz;
**Lyric-Script in Lyric 5**: CoreCLR-Hosting (opt-in, zieht .NET in den Prozess) oder eine
native Script-VM — oder gar nicht in 5.0.

## W6 — Diagnostik: Katalog, Form, JSON, Fixes: **offen**

D1/D2/D5–D12, D15–D17, D23–D32. Codeschema (`LYR-XXX0000` bleibt?), Bereiche, Severities
(`Info` überlebt?), mehrere beschriftete Spans, Notizen, Ausgabeform (rustc-artig), Fehler-
obergrenze, Reihenfolge als Vertrag, JSON-Vertrag, `lyric explain CODE`, Fix-Vorschläge
(`MachineApplicable`), Katalog-Erzwingung, fremde Diagnosen gedämpft, `comptime`-Spans.

## W7 — Lints: **offen**

D17, 06/07/08/10-Vormerkungen. Welche Warnungen es gibt (toter Code, ungenutzt, globale `var`
ohne Sync, reservierte Gerätenamen, Prelude-Verdeckung, nie geschlossene `Closeable`,
Zuweisung in Bedingung, `x = x++`, `+=` auf Strings in Schleifen?), Stufen (`allow`/`warn`/
`deny`) über `[lints]` und `@Allow`, Standardstufen, ob Lints Teil von `check` sind.

## W8 — Editor, Formatter, REPL, Doku: **offen**

E1, E3–E9, E13–E20, E22–E27, E29–E34. Sprachserver-Analysemodell (abbrechbar, inkrementell,
abfragebasiert), Code Actions, Tests im Editor; DAP über lldb-dap (Quellkarte, Koroutinen/Tasks,
Panik-Stopp, Evaluate); Formatter (eine Form ohne Optionen, Stabilitätsvertrag, minimale Edits);
REPL (über das interpretierbare IR, oder Lyric-Script, oder streichen); `lyric doc`, `lyric api
--diff`, Navigation in Abhängigkeiten; Clients.

## W9 — Distribution und Toolchain: **offen**

CLI-18/30/31, Z1-Folgen. Installation (ein Archiv, `zig cc`/clang vorausgesetzt), Toolchain-
Verwaltung (`lyric toolchain install/pin`), Selbst-Update, Ort der `std`, Versionskohärenz
Treiber/Werkzeuge/Clients, Releasekanäle.
