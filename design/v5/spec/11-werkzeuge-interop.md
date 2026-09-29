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

## W3 — `build.lyr`: Programm, Daten oder Graph: **entschieden** (2026-09-29)

Verschoben gegenüber dem Dossier: das Manifest ist die Wahrheit (P1), und ohne Capabilities
(Z3) gibt es keine Skript-Sandbox — es bleiben Vollzugriff mit Vertrauensregel oder kein Skript.

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| BS1 | **`build.lyr` bleibt — als gewöhnliches Lyric-Programm mit Vollzugriff, das *vor* dem Compile läuft und nichts definiert** (P1): nur **tun** — Quelltext nach `gen/` erzeugen (09), native Teile bauen, Link-Einstellungen beisteuern. `lyric new` erzeugt keins (BP-32 D) | Cargo `build.rs`; verworfen: Zig (`build.zig` definiert das Projekt), Sandbox (BP-10 B), Go `go generate` |
| BS2 | **`std.build`**: `build.target`, `build.profile`, `build.outDir`, `build.genDir` (= `gen/`), `build.rerunIfChanged(paths)`, `build.linkLib(name)`, `build.cFlags(…)`, `build.compileC(sources, …)`, `build.warn(msg)`; sonst die normale std | Cargo `cargo:rerun-if-changed`, `cargo:rustc-link-lib` |
| BS3 | **Eigener Modulraum** (BP-11 C): `build.lyr` + `build/`, `[build-dependencies]`; sieht `src/` lesend (D), kompiliert sich nie hinein | Cargo |
| BS4 | **Wann**: vor dem Compile, wenn Skript, `[build-dependencies]` oder eine `rerunIfChanged`-Eingabe sich geändert hat, sonst Cache; `lyric check`/`lsp` lassen es einmal laufen, damit `gen/` existiert; `--help`/Completions führen nie etwas aus | rust-analyzer |
| BS5 | **Vertrauensregel** (CLI-17): Wurzelpaket-Skript läuft immer; **Skripte von Abhängigkeiten nur mit `[trust] build-scripts = ["…"]`** im Wurzel-Manifest, sonst Fehler mit Paket und Zeile | **pnpm 10**, Deno; gegen Cargo/npm |
| BS6 | **`gen/` ist sichtbarer Modulraum** (`app.gen.schema`), nicht eingecheckt, in Diagnosen und Debugger eine Datei | 09 |
| BS7 | **Türen**: `[tasks]` (`lyric task name`), Schritt-Graph mit deklarierten Ein-/Ausgaben | Deno `task`, Bazel |

## W4 — FFI: `extern "C"`, Typen an der Grenze, Export, Bindungen: **entschieden** (2026-09-29)

Grundlage aus 00/01: C-ABI ist die Muttersprache (Z5), Structs sind inline C-Structs, der GC
bewegt nicht (L1), Yields dürfen C-Frames durchqueren (L4), `extern "C"` wirft nie (E6/E7).

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| X1 | **Form** (D15): `extern "C" fn strlen(s: CStr): uint = "strlen";` einzeln oder gebündelt `extern "C" { fn …; }`; Symbolname = Funktionsname, `= "sym"` weicht ab; **Linken über `[native] libs`** (P1), kein `@Link`; Variadik: `extern "C" fn printf(fmt: CStr, ...): int` (C-Promotion) | Rust, Zig; verworfen: `@Link` |
| X2 | **Typtabelle (normativ)**: `int8…uint64` ↔ `int8_t…uint64_t`, `int` = `int64_t`, `uint` = `uint64_t`/`size_t`, `float32/float64` ↔ `float/double`, `bool` ↔ `bool`, `char` ↔ `uint32_t`; **`Ptr<T>`** (nicht-null) ↔ `T*`, **`?Ptr<T>`** ↔ nullbarer Zeiger (V5-Niche); `CStr` ↔ `const char*`; **`@Layout(C) struct`** ↔ C-Struct nach Plattform-ABI (auch als Wert; Felder müssen C-fähig sein, sonst Fehler), `@Layout(C)`/`@Layout(uint8)` Enum ↔ C-Enum/Integer; `T[N]` in `@Layout(C)`-Structs inline; `extern "C" fn(int) -> int` ↔ Funktionszeiger. **Nicht**: Klassen, `string` als Rückgabe, `Slice`, `?T` außer Zeiger, Closures, Tupel, Interface-Werte. **`@Layout`** statt Rusts `@Repr` — der Name sagt, was es tut (Maintainer): ohne Attribut darf der Compiler Felder umordnen und Nischen nutzen | Rust `#[repr(C)]`, Zig `extern struct`, C# `StructLayout`; verworfen: alle Structs C-Layout, Union/Bitfelder (Tür) |
| X3 | **Strings**: `string` ist im Layout **NUL-terminiert** (ein Byte je String) → koerziert als Argument zu `CStr` **ohne Kopie** (GC unbeweglich); `CStr` aus C ist geliehen: `toString()` kopiert und prüft UTF-8 (`throws Utf8Error`), `length()`; Halten über den Aufruf hinaus nur mit `GcHandle` (X6) | Zig `[*:0]const u8`; F21 A |
| X4 | **Speicher und `unsafe`**: `Ptr<T>.read()/write(v)/offset(n)/cast<U>()`, `ffi.alloc(n)`/`free(p)` nur in **`unsafe { … }`** (kontextuelles Schlüsselwort); der *Aufruf* eines `extern` ist nicht `unsafe` (Zig-Haltung); `Slice<T>.asPtr()`/`T[].asPtr()` sicher, Nutzung unsafe; **keine Arena** (F11 B war die Sandbox-Antwort) | Rust, Zig; verworfen: Java-FFM-Arena, Handle-Tabelle |
| X5 | **Callbacks**: nur nicht-fangende Funktionen/Lambdas koerzieren zu `extern "C" fn(…)`; Closures über **`ffi.Callback.new(closure)`** → `(fnptr, userdata: Ptr<void>)` mit GC-Pin, `release()`; Callback-Typ **nicht-werfend** (E7); Fehlerbrücke `ffi.stash(e)` im Callback + `ffi.takeStashed()` nach dem Aufruf (Task-lokal, E7-Form); **Panik im Callback = Abbruch**; Callbacks dürfen parken/yielden (L4) | Rust, Go cgo |
| X6 | **Lebensdauer**: während eines Aufrufs sind Lyric-Zeiger stabil; darüber hinaus **`GcHandle.pin(obj)`** + `release()`, `asPtr()` für `void* userdata`, `GcHandle.from(ptr)`; C-Speicher gehört dem Programm | .NET `GCHandle`, Go `cgo.Handle` |
| X7 | **Fremde Threads**: Callback auf einem Thread ohne Lyric-Scheduler → **Queue des besitzenden Threads** (06), nur `void`-Callbacks; mit Rückgabewert von fremdem Thread = Abbruch mit Meldung; `Callback.new(closure, thread: …)` wählt den Besitzer | Kotlin-Dispatcher-Form |
| X8 | **Export**: `@Export("c_name") pub fn f(a: int): int` → C-Symbol (nur X2-Typen; Reachability-Wurzel, 07 B6); `lyric build --lib` erzeugt `lib<name>.a`/`.so`/`.dll` **und `<name>.h`** mit Exporten plus `lyr_init`/`lyr_shutdown` (W5) | Rust `cbindgen`, Go `//export`, Swift `@_cdecl` |
| X9 | **Bindungen**: 5.0 von Hand, C-Bibliotheken als Pakete (07 B5, `-sys`-Muster); **`lyric bindgen` Tür** (libclang) | Rust `bindgen`; Zig `@cImport` verworfen |
| X10 | **Optimierer/Vertrag**: `extern`-Aufruf ist opak (Speicher-Clobber); **`@Pure` auf `extern` Tür** (Reinheit ohne Körper ist eine Behauptung, die still miskompiliert — Gewinn klein); Typtabelle, `@Layout`-Regeln je Zieltripel und Callback-Regeln als Normtext (F2/F10/F18) | GCC `__attribute__((const))` |

## W5 — Einbettung und Script-Nesting: **entschieden** (2026-09-29)

### A. Lyric 5 im C-Host

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| H1 | **Ein Runtime je Prozess**: `lyr_init(const LyrConfig*)` / `lyr_shutdown()`; `LyrConfig { argc, argv, install_signal_handlers (10 Q9), heap_limit, stdout_write/stderr_write (nullbare Callbacks), log }`; zweites `lyr_init` = Fehler; generiert in `<name>.h` (X8) | Python `Py_Initialize`; Lua-Mehrfach-States verworfen (ein Heap, L1/G3) |
| H2 | **Exporte sind die API** (X8): ein Export läuft als Task auf dem Scheduler des rufenden Threads (S6) und **pumpt bis zum Ende** — synchron aus Host-Sicht, parken erlaubt; asynchron über `spawnDetached` + **`lyr_step(): bool`/`lyr_run()`** (S5) | Lyric 4, Erato |
| H3 | **Fehler/Panik über die Grenze** (E7-Form, 05 O5): werfende Exporte bekommen **`LyrStatus name(args…, R* out)`**; `LYR_OK`/`LYR_ERROR`/`LYR_PANIC`; `lyr_take_error(LyrError*)` (Typname, Meldung, Ursachenkette); **Panik im Export = `LYR_PANIC`, Runtime bleibt nutzbar** (Task-Grenze, T4) | Swift `@_cdecl`-Muster; gegen Rust (Panik über FFI = abort) |
| H4 | **Host-Threads**: erster Aufruf attacht lazy einen Scheduler (G1); `lyr_detach_thread()` optional; Objekte dürfen Threads wechseln, Container/Handles nicht gleichzeitig (G3) | Go cgo; JNI `AttachCurrentThread` verworfen |
| H5 | **Host-Objekte in Lyric**: `Ptr<void>` in Ein-Feld-Struct (T15) oder `Closeable`-Klasse — der Host besitzt; keine Handle-Tabelle, keine Lebendprüfung (F22 D verworfen); Lyric-Objekte im Host über `GcHandle` (X6); `lyr_gc_collect()`, `lyr_gc_stats()` | Go cgo, Lua `lightuserdata` |
| H6 | **Reentranz** Host → Lyric → Host → Lyric gewöhnlich; kein Tiefenzähler (S6); Stack-Overflow = Guard-Page-Panik | Lua `LUAI_MAXCCALLS` verworfen |
| H7 | **Dynamisches Laden**: `ffi.Library.open(path): Library throws IoError`, `lib.symbol<extern "C" fn(int) -> int>("name"): ?…`, `Closeable`; **Lyric-Plugins** (Lyric-`.so` in Lyric-Programm) **Tür** — zwei statisch gelinkte Runtimes = zwei GCs; bräuchte `--shared-runtime` | Rust `libloading`, C# `NativeLibrary` |
| H8 | **Kein Compiler im Host** (JIT-Skripting von Lyric 5) in 5.0; Tür = interpretierbares IR (L7) | — |

### B. Lyric-Script in Lyric 5

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| H9 | **Kein Script-Nesting in 5.0** (Z4). **Wird neu geplant, sobald die native Script-VM steht** (Maintainer, 2026-09-29 — „nur angestoßen"). Bis dahin: Prozessgrenze (`lyric-script` als Kindprozess, Pipes) als Ring-Paket; **CoreCLR-Hosting verworfen** (zieht das Laufzeitgewicht zurück, das Z1 ablegt). Vorgedacht für die spätere Planung: **(a) Modul-Mix im Paket** — `[script] root/toolchain/embed` im Manifest, Grenze **einmal** in Lyric 5 als `@ScriptApi`-Funktionen (→ Natives der VM) und `extern "script"`-Stubs, API-Beschreibung aus dieser Quelle, Bytecode per `embed` ins Binary oder daneben (Hot-Reload/Modding), VM als Bibliothek gelinkt, Marshalling = Native-Tabelle (Bibliotheks-, keine Sprachgrenze), zwei LSPs nach Dateiendung; **(b) Datei-Mix** (HTML-Idee des Maintainers) nur als Makro `script! { … }` (Q5) — braucht einen comptime-aufrufbaren Script-Compiler (Q3), kein Sprachfeature; die echte HTML-Analogie (Objektmodell + Verhalten in einer Datei) wäre ein Erato-Format, nicht Lyrics; **(c) die andere Tür**: Lyric-5-Module im IR-Interpreter (L7) — dasselbe Typsystem, keine Grenze, Hot-Reload/Budget/Sandbox durch den Interpreter, aber ohne Z4s Script-Freiheit | VS Code Extension-Host (Prozess), Rust `inline-python` (Makro) |
| H10 | **Vormerk GUI-Format** (Maintainer, 2026-09-29; Ring, nicht `std`): XAML-artiger **Baum** (Elemente = Lyric-Klassen der UI-Bibliothek, Attribute = Felder, **Bindungen kompiliert** wie `x:Bind`), **Blöcke in Lyric 5** (`onClick = { … }`, Blazor/Compose-Modell — QML ist die Warnung: JS-Blöcke wurden zehn Jahre lang wegkompiliert), erzeugt per `embed` + `comptime`/`gen/` (09, BS6) als Klasse; **Code-Behind = `extend MainWindow { … }`** (methoden-only `extend` ist frei); **Lyric-Script-Blöcke als Sandbox-Variante** (`script = "lyric-script"`) für nutzereditierbare Oberflächen, sobald H9 steht; Layout-Hot-Reload braucht kein Script (Baum ist Daten); LSP-Schema aus `lyric api`/`std.meta`; Rendering-Backends als Ring-Pakete über FFI. Geplant mit dem Ring | XAML/`x:Bind`, Blazor `@code`, Compose; gegen QML |

## W6 — Diagnostik: Katalog, Form, JSON, Fixes: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| G1 | **Record**: `Diagnostic { code, severity, spans: [LabeledSpan] (ein primärer, sekundäre mit Beschriftung), message, notes: [Note { kind: Place \| Help \| Suggestion \| Category, span?, text, replacement?, applicability? }] }` (D10 B, D26 B). **Severities `Error`, `Warning`, `Hint`** — `Info` gestrichen (D25 B); `Hint` = Editor-Abschwächung | rustc, Clang |
| G2 | **Codes nummeriert** `LYR-<BEREICH><NNNN>`, Bereichssatz **offen** (`LEX PAR RES SEM CT MAC CG LNK RT CLI ICE`; D16 B, D31 D); **Lints tragen zusätzlich einen Namen** (`dead-code`); `@Allow` und `[lints]` nehmen Name oder Nummer | TypeScript/Go, Rust |
| G3 | **Severity gehört dem Code** (§12.1); nur **Lints** haben eine bewegliche Stufe (`allow`/`warn`/`deny`, W7); `deny` = Fehler mit Exit 1 (C5; kein Exit 3, D30 B verworfen); **„Warnungen als Fehler" ist ein Schalter** (W7 L2) | Rust |
| G4 | **Kein Sammelcode**: `IR0001` fällt; jede Ablehnung nennt ihren Grund im eigenen Code; Compilerfehler = `LYR-ICE0001` mit „bitte melden", Dump und Backtrace (D1/D2) | rustc ICE |
| G5 | **Ausgabe rustc-artig**: `error[LYR-SEM0042]: message` · `--> pfad:zeile:spalte` · Snippet mit Rinne, mehrere beschriftete Unterstreichungen · `= note:`/`= help:`; Zeilenkürzung; Farbe nach C7; **Pfade projektrelativ** (`--absolute-paths`); Reihenfolge Datei → Position, deterministisch, kein Vertrag (D24) | rustc |
| G6 | **Folgefehler**: Typfehler vergiftet den Ausdruckstyp (`<error>`), Abgeleitetes schweigt (D6 D); `--max-errors 50` (D8) | rustc/Clang |
| G7 | **Fixes im Record** (D9 B): `Suggestion` mit `replacement` und `applicability: safe \| needsReview`; `lyric fix` wendet `safe` an, LSP zeigt beide; `@Deprecated.replacement` erzeugt eine (09 A11) | rustc `MachineApplicable` |
| G8 | **`--json` = NDJSON** (D15 B): eine Diagnose je Zeile, `version`, Summenzeile; Schema in der Spec; Panik/ICE als Zeilen mit `kind`; SARIF Tür | Rust `--error-format=json` |
| G9 | **`lyric explain CODE`** aus eingebettetem Katalog; LSP `codeDescription.href`. **Katalog-Heimat `diagnostics.toml` im Compiler-Repo**, Spec-Appendix daraus generiert (D16 E, D29) — Regeln spec-first, Katalog nicht | rustc `--explain`, GHC error index |
| G10 | **Wächter** (D16 B+C+D, D27): jeder emittierte Code im Katalog, jeder Katalogcode emittiert oder zurückgezogen, **jede Warnung mit rotem und grünem Konformanzfall**; Meldungsinvarianten als Test; Stilfibel in `CONTRIBUTING` | Rust |
| G11 | **Fremde Diagnosen** (D28 C): Warnungen aus Abhängigkeiten und `std` nicht gezeigt (`--warn-deps`), Fehler immer | Cargo `--cap-lints` |
| G12 | **`comptime`/Makro-Spans** (D31 C+D): Hauptspan an der Aufrufstelle, Notizkette mit Frames; `lyric expand` | rustc |
| G13 | **Laufzeit**: Paniken mit Code (`LYR-RT…`) und Backtrace, Exit 101; entkommener Fehler `error: message` + Ursachenkette, Exit 1 (05 O4/E8) — dieselbe Textform, `explain` kennt beides | Rust |

## W7 — Lints: **entschieden** (2026-09-29)

Maintainer: viele sinnvolle Warnungen (in 4.x erst vergessen, dann dünn) und ein schaltbares
„Warnungen als Fehler".

| # | Entscheidung | Vorbild |
|---|---|---|
| L1 | **Stufen** `allow`/`warn`/`deny` je Lint; gesetzt über `[lints]` (paketweit), `@Allow(name)`/`@Warn`/`@Deny` an Deklaration oder Modul (09 A10), CLI `-A`/`-W`/`-D name`; ein Mechanismus, drei Orte, Präzedenz C4 | Rust |
| L2 | **Warnungen als Fehler**: Gruppe `warnings` — `[lints] warnings = "deny"`, CLI **`--deny-warnings`** (= `-D warnings`), Profilfeld `denyWarnings` (P3); wirkt auf jede Warnung inkl. Deprecations; **neue Lints in einem Minor starten als `warn`** (Toolchain-Pin P12 schützt `deny`-Projekte) | Rust `-D warnings`, C `-Werror` |
| L3 | **Gruppen**: `unused`, `correctness`, `resources`, `concurrency`, `deprecation`, `style`, `perf`, `docs`; `[lints] style = "allow"` schaltet eine Gruppe | Clippy-Gruppen |
| L4 | **Lints laufen in `check` und `build`**, kein `lyric lint`; Teil des Katalogs (G2), jeder mit rotem und grünem Konformanzfall (G10) | Go `vet` (getrennt) verworfen |
| L5 | **Editor**: `unused` als `Hint` mit `Unnecessary`-Tag, sonst `Warning`; Fixes nach G7 | rust-analyzer |

**Die Liste** (Standardstufe):

| Gruppe | Lints |
|---|---|
| **unused** (warn) | `unused-variable`, `unused-parameter` (`_x` schweigt), `unused-import`, `unused-result` (Rückgabe einer werfenden oder `@MustUse`-Funktion verworfen), `unused-assignment`, `unused-var` (`var` nie neu zugewiesen → „`let` genügt"), `unused-label`, `unused-generic`, `dead-code` (nicht-`pub` ohne Nutzung; Attributträger ausgenommen), `unreachable-code`, `unreachable-pattern` (4.x „kein Fehler" → Warnung) |
| **correctness** (warn) | `assignment-in-condition` (Y), `self-assignment` (`x = x`, `x = x++`, D), `comparison-always` (`uint >= 0`), `float-equality` (allow), `prelude-shadowing` (I8), `shadowed-local` (allow), `module-shadowing` (07 K4), `int-literal-adapts` (allow). Fehler bleiben Fehler (Konstantenüberlauf, `!` auf Nicht-Optional, fehlendes `try`) — kein zweiter Fehlerkanal |
| **resources** (warn) | `closeable-never-closed` (R3), `unused-task` (`Task<T>` verworfen — `spawnDetached` sagt es) |
| **concurrency** (warn) | `global-var-without-sync` (Modul-`var` in einem Programm mit Threads, 06) |
| **deprecation** (warn) | `deprecated` (A11; abgelaufenes `until` = Fehler), `deprecated-edition` (Bereich 12) |
| **style** (warn) | `naming` (N1), `reserved-device-name` (07 M8 — deny), `redundant-clone`, `needless-return` (allow), `loop-instead-of-while-true` (allow) |
| **perf** (allow, außer *) | `string-concat-in-loop`* (warn → `StringBuilder`), `large-value-copy` (Struct > 64 B als Wert; 02: 16-B-Richtlinie bleibt Doku), `boxed-interface-in-hot-loop` |
| **docs** (allow) | `missing-docs` (`pub` ohne `///`; Bibliotheken setzen `deny`), `broken-doc-link` (warn) |

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
