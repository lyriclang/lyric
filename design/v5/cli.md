# Lyric 5 — Gebiet CLI, Treiber, Profile

Stand 2026-09-24, zweite Fassung nach adversarischer Kritik. Gemessen gegen die Debug-Binaries
in `src/*/bin/Debug/net10.0`, Version `lyric 4.6.0` (gemessen: `lyric --version` druckt
`lyric 4.6.0`). Probeprojekte unter `…/scratchpad/v5-design/probes/cli/demo`, `…/demo2`,
`…/jsonprobe`.

Belegarten: **gemessen** = ein Lauf hat geantwortet · **gelesen** = Pfad:Zeile · **behauptet** =
weder noch. Zeilennummern gegen den Arbeitsbaum bei `6f6f029f`.

**Zur Messgenauigkeit:** der Maintainer arbeitet parallel im selben Checkout. Alle Zeitmessungen
in diesem Dossier streuen deshalb um Faktor 3–4. Wo eine Zahl steht, steht sie als Minimum und
Median aus neun Läufen, und eine exakte Zahl behaupte ich nirgends.

---

## 1. Ist-Stand

### 1.1 Die Binaries

**Elf ausführbare Projekte insgesamt**, `lyric` und `lyrstub` eingeschlossen (gelesen: elf
`<OutputType>Exe</OutputType>`-Projekte unter `src/*/*.csproj`).

| Binary | Rolle | Verben |
|---|---|---|
| `lyric` | Treiber, kompiliert und führt nichts aus | `new run build pack fmt test check disasm repl` (`src/Lyric.Cli/Program.cs:29-45`) |
| `lyrc` | Compiler | `build check lower parse tokenize` (`src/Lyrc/Program.cs:39-43`) |
| `lyrvm` | Laufzeit | `run disasm verify info` (`src/Lyrvm/Program.cs:34-37`) |
| `lyrbuild` | Projektbauer | verblos, `[directory] [options]` |
| `lyrtest` | Testläufer | verblos |
| `lyrfmt` | Formatierer | verblos |
| `lyrpack` | Packer | verblos |
| `lyrrepl` | Prompt | verblos, `:`-Kommandos |
| `lyrls` | Sprachserver | verblos, stdio |
| `lyrdbg` | Debug-Adapter | verblos, stdio |
| `lyrstub` | Laufzeithälfte eines gepackten Programms, **keine startbare Anwendung**, sondern eine Vorlage, die `lyrpack` kopiert (`src/Lyrstub/Program.cs:6-18`) | — |

Startbare Werkzeuge sind damit **neun** (`lyrc lyrvm lyrbuild lyrtest lyrfmt lyrpack lyrrepl
lyrls lyrdbg`). Das ist die Zahl, die für `Tool.All` in Frage kommt — nicht elf.

**`lyric --version` kennt nur sieben davon** (gemessen: `lyrc lyrvm lyrrepl lyrbuild lyrpack
lyrfmt lyrtest`, jeweils mit „bundled"). `Tool.All` (`src/Lyric.Core/Tool.cs:38-39`) führt
`lyrls` und `lyrdbg` nicht — also nennt die eine Selbstauskunft der Toolchain ausgerechnet die
beiden Binaries nicht, die ein Editor startet und deren Versionsversatz man sonst nicht sieht.
Passend dazu gibt es keine `LYRIC_LS`- und keine `LYRIC_DBG`-Variable (gemessen per grep, 1.6 F11).

### 1.2 Der Treiber

`lyric` ist ein reiner Vermittler. Drei Muster:

- **Durchreichen** (`check`, `disasm`, `repl`): die Argumente gehen unverändert an das Werkzeug
  (`Program.cs:361-365`).
- **Verb abschneiden** (`fmt`, `test`): `args[1..]` (`Program.cs:370-374`).
- **Komponieren** (`run`, `build`, `pack`): zwei Werkzeuge hintereinander, mit einer
  Aufteilung der Optionen im Treiber (`Program.cs:212-236`) — `--jit` und `--grant` an die
  Laufzeit, **alles andere** an den Compiler.

`repl` steht in der ersten Gruppe, gehört aber in die zweite: `Forward(Tool.Repl, selection,
args)` (`Program.cs:42`) schickt `lyrrepl` das Wort `repl` als erstes Argument mit. Es fällt nur
deshalb nicht auf, weil `lyrrepl` jedes Argument außer `--version`, `--help` und `--stdlib`
ignoriert (`src/Lyrrepl/Program.cs:26-38`). `check` und `disasm` **brauchen** das Verb (`lyrc`
und `lyrvm` haben es), `repl` nicht. Ein latenter Defekt, kein sauberes Muster.

Die Regel, die entscheidet, ob ein Argument eine **Datei** oder ein **Artefaktname** ist: es
liegt auf der Platte oder trägt `.lyr`/`.lyrbc` (`Program.cs:205-208`). Dieselbe Regel für
`run`, `build` und `pack` — sauber, und Bun hat genau hier sein bekanntes Problem.

Der Treiber führt eine **zweite Kopie der Optionsarität** (`Program.cs:196-200`, neun Einträge).
Der Kommentar darüber (`Program.cs:173-176`) verwirft ausdrücklich die *vollständige* Tabelle
jedes Werkzeugs und baut bewusst nur die Teilmenge der wertnehmenden Optionen („it holds the
options that take a value rather than all of them"). Der Vorwurf **doppelte Wissenshaltung**
trägt — die Liste ist unvollständig und messbar falsch (F5). Der Vorwurf „widerspricht dem
eigenen Kommentar" trägt **nicht**; die erste Fassung dieses Dossiers hat den Kommentar
falsch gelesen.

### 1.3 Optionen

Vier Optionen gelten als „geteilt" und stehen in `ToolOptions`
(`src/Lyric.Core/ToolOptions.cs:46-87`): `--json`, `--quiet`/`-q`, `--verbose`,
`--progress <mode>`.

**Vier von elf Binaries parsen sie** (gelesen, `ToolOptions.Parse`-Aufrufe): `lyrc:24`,
`lyrvm:21`, `lyrfmt:23`, `lyrpack:25`. `lyrbuild`, `lyrtest`, `lyrrepl`, `lyrls`, `lyrdbg` und
der Treiber selbst haben je einen eigenen Parser (`src/Lyrbuild/Program.cs:113-182`,
`src/Lyrtest/Program.cs:31-66`).

**Parsen heißt nicht wirken.** `--json` hat im ganzen Quelltext genau drei Wirkorte (gelesen,
grep über `RenderJson` und `options.Json`): `TerminalOutput.Render`
(`src/Lyric.Core/TerminalOutput.cs:125`), die `Fail(…, json)`-Stellen in `src/Lyrc/Program.cs`
(siebzehn Aufrufe) und `lyrvm info` (`src/Lyrvm/Program.cs:142-143`). Gemessen:

| Aufruf | Ausgabe |
|---|---|
| `lyrvm run missing.lyrbc --json` | `error[LYR-CLI0001]: failed to read file: missing.lyrbc` (Text) |
| `lyrvm verify trunc.lyrbc --json` | `error[LYR-BC0003]: unexpected end of file …` (Text) |
| `lyrfmt --json syntax.lyr` | `syntax.lyr:3:1: error[LYR-PAR0002]: …` (Text; `RenderText` unbedingt, `src/Lyrfmt/Program.cs:62,112`) |
| `lyrpack --json missing.lyrbc` | `error[LYR-CLI0008]: cannot pack …` (Text) |
| **Kontrolle** `lyrc build --json missing.lyr` | `{"diagnostics":[{"code":"LYR-CLI0001",…}]}` |

Also: **ein** Werkzeug handelt nach `--json`, drei parsen es und tun nichts. `src/Lyrvm/Program.cs:226`
druckt in der eigenen Hilfe `--json  Diagnostics (and 'info') as JSON` — die Selbstauskunft ist
zur Hälfte unwahr.

Es gibt **keine `--flag=value`-Form** und **keine Kurzflag-Bündelung**. `-q` ist die einzige
Kurzform neben `-v`/`-h`/`-o`/`-D`.

### 1.4 Profile

Zwei, fest verdrahtet (`src/Lyric.Frontend/Compiler/Profile.cs:22-31`). **Vier Felder:**

| Feld | `debug` | `release` |
|---|---|---|
| `Optimize` | aus | an |
| `SourceMap` | an | an |
| `DebugInfo` | an | aus |
| `DenyWarnings` | aus | aus |

`Profile.Named` kennt genau zwei Namen (`Profile.cs:51-56`). `LYRIC_PROFILE` setzt den
Prozessdefault (`Profile.cs:47-48`). Ausgabepfad `out/<profile>/<name>.lyrbc`
(`src/Lyrbuild/Program.cs:425`).

Für diese vier Felder gibt es **sieben Schreibweisen** auf der Kommandozeile von `lyrc`
(gelesen, `src/Lyrc/Program.cs:137-181`): `--deny-warnings`, `--optimize`, `--no-optimize`,
`--source-map`, `--no-source-map`, `--debug-info`, `--no-debug-info`. Die Negation ist
**asymmetrisch**: gemessen `lyrc build hello.lyr --no-deny-warnings` →
`error[LYR-CLI0003]: unknown option '--no-deny-warnings'`. Dazu vier Schalter, die **keine**
Profilfelder sind, sondern Optimierungspässe (`src/Lyrc/Program.cs:183-197`): `--no-inline`,
`--no-scalar-replacement`, `--no-devirtualize`, `--no-fusion`; der Guide erklärt sie
ausdrücklich zu Diagnosehilfen ohne Kompatibilitätszusage (`docs/guide/16-building.md:370-373`).

**Die Feld-Overrides gelten nur auf dem Datei-Pfad.** Gemessen in einem `lyric new`-Projekt:
`lyric build --no-optimize`, `--source-map`, `--debug-info`, `--deny-warnings`, `--quiet`,
`--json` → **jeweils** `error[LYR-CLI0003]: unknown argument: … — try 'lyrbuild --help'`,
exit 2. Kontrolle: `lyric build src/main.lyr --no-optimize -o …` → exit 0.
`lyrbuild` kennt nur `--profile`, `--release`, `--debug`, `--only`, `-D`, `--stdlib`,
`--print-path`, `--help`, `--version` (gelesen, `src/Lyrbuild/Program.cs:113-182`).
Die erste Fassung dieses Dossiers hat das als „jedes Feld ist einzeln überschreibbar"
beschrieben — das gilt für `lyrc`, nicht für den Projektpfad.

### 1.5 Fehlercodes

Vier Prozess-Exit-Codes (`src/Lyric.Core/ExitCodes.cs:15-26`): `0` Erfolg, `1` Fehler, `2`
Fehlbedienung, `101` Panik. Der Rückgabewert von `main` wird mit `& 0xFF` maskiert
(`docs/guide/01-getting-started.md:20`).

**Neunzehn lebende `LYR-CLI####`-Diagnosen** (gelesen, `src/Lyric.Core/CliDiagnostics.cs:14-101`:
0001–0006 und 0008–0020, also 6 + 13), dazu die retirierte 0007 als reservierte Nummer. Die
erste Fassung sagte achtzehn und benutzte die Zahl zweimal als Argument.

### 1.6 Was gemessen wurde — und was dabei auffiel

| # | Befund | Beleg |
|---|---|---|
| **F1** | **`lyric <verb> --help` hat fünf verschiedene Ausgänge.** `build fmt test repl` → Hilfe des *Werkzeugs* (exit 0, Usage-Zeile nennt `lyrbuild`/`lyrtest`). `check` → `LYR-CLI0003 unknown option '--help'` (exit 2). `new` → `LYR-CLI0002 missing project name` (exit 2). `disasm` → `LYR-CLI0004 expected a .lyrbc module, got '--help'` (exit 2). `run`/`pack` → `LYR-CLI0012 lyrbuild did not name one program to run, it wrote:␊Usage: lyrbuild …` — die komplette lyrbuild-Hilfe, escaped, in einer Fehlerzeile (exit 1). | gemessen |
| **F2** | **Derselbe Verb nimmt je nach Argument andere Optionen — und zwar den kompletten Feld-Override-Satz.** `lyric build src/main.lyr --no-optimize` → exit 0. `lyric build --no-optimize` (Projekt) → `LYR-CLI0003`, exit 2. Ebenso `--source-map`, `--debug-info`, `--deny-warnings`, `--quiet`, `--json`. | gemessen |
| **F3** | **`LYRIC_PROFILE=bogus` wird still verschluckt**, `--profile bogus` ist ein Fehler. Gemessen: `LYRIC_PROFILE=bogus lyric build` baut nach `out/debug/`, exit 0; Kontrolle `LYRIC_PROFILE=release` baut nach `out/release/`. Ein Tippfehler in der CI (`relese`) liefert stillschweigend einen Debug-Build. Absicht laut Kommentar (`Profile.cs:40-45`: „a library has nowhere to complain") — nur hat jedes Werkzeug, das die Variable liest, sehr wohl wohin. | gemessen |
| **F4** | **Vier Verhaltensweisen bei unbekannten Optionen, nicht drei.** (1) **Ablehnen mit Code**: `lyrc`, `lyrvm`, `lyrbuild`, `lyrtest`. (2) **Still schlucken**: `lyrpack m.lyrbc --bogus -o p.exe` → exit 0, packt; `lyrrepl --bogus` → startet; `lyrdbg --bogus` → keine Ausgabe, exit 0 (`src/Lyrdbg/Program.cs:19-36` prüft nur `--version`/`--help`). (3) **Als fehlende Datei melden**: `lyrfmt --bogus f.lyr` → `LYR-CLI0001 no such file or directory: --bogus`. (4) **Ablehnen OHNE Diagnosecode**: `lyrls --bogus` → `lyrls: unknown argument '--bogus'` plus Usage, exit 2, kein `LYR-CLI`-Code (`src/Lyrls/Program.cs:73-76`). Guide 16:375 behauptet: *„Every tool refuses an option it does not know, by name (`LYR-CLI0003`)."* | gemessen |
| **F5** | **Die Optionsaritätstabelle des Treibers ist unvollständig.** `--progress` fehlt in `ValueOptions` (`Program.cs:196-200`). Gemessen: `lyric build --progress always` liest `always` als **Dateinamen**, wechselt von „Projekt bauen" auf „Datei übersetzen" und endet mit `LYR-CLI0002 build: missing file argument`. Kontrolle: `lyric build --profile release` baut das Projekt korrekt nach `out/release/`. | gemessen |
| **F6** | **`lyrtest` meldet einen fehlenden Wert als unbekannte Option.** `lyrtest --filter` (ohne Wert) → `unknown argument: --filter`; dieselbe Form gilt gelesen für `--stdlib` (`:44`) und `--profile` (`:47`), weil `case "--x" when i + 1 < args.Length` bei fehlendem Wert in `default` fällt. `lyrc` sagt an derselben Stelle `-o: missing path argument`; `lyrbuild` löst es sauber über `Value(args, ref i, …)` (`src/Lyrbuild/Program.cs:188-190`). | gemessen + gelesen |
| **F7** | **Globale Optionen dürfen nicht vor dem Verb stehen.** `lyric --json run x.lyr` → `unknown command: --json`, exit 2. `lyric --compiler <pfad> run x.lyr` funktioniert, weil `ToolSelection.Parse` (`src/Lyric.Cli/ToolSelection.cs:27-46`) die Werkzeugflags überall abgreift. Zwei Klassen globaler Flags mit zwei Stellungsregeln. | gemessen |
| **F8** | **`lyric` kennt `verify` und `info` nicht**, obwohl `lyrvm` beides hat. `lyric verify x.lyrbc` → `unknown command: verify`. Der Guide verweist an zwei Stellen auf `lyrvm verify`/`lyrvm info` (`17-packaging.md:82`, `21-debugging.md:74`). | gemessen |
| **F9** | **`--grant host` funktioniert, steht aber nicht in der Hilfe.** `lyrvm --help` listet „file,net,os,process; all; none" (`src/Lyrvm/Program.cs:223`), `CapabilityTable.Parse` (`src/Lyric.Core/Capabilities.cs:124-134`) kennt zusätzlich `host`. Gemessen: `lyric run p.lyr --grant host` läuft. | gemessen |
| **F10** | **Exit-Code-Maskierung ist eine Falle.** `return 256` → exit **0**. `return -1` → exit **255**. `return 101` → exit **101**, ununterscheidbar von einer Panik (in `ExitCodes.cs:9-11` eingeräumt). | gemessen |
| **F11** | **Zwölf Umgebungsvariablen, vier dokumentiert.** Gemessen per grep: `LYRIC_BUILD COMPILER FMT JIT PACK PROFILE REPL STDLIB STUB TEST VERIFY_IR VM`. Im Guide/README: `LYRIC_STDLIB`, `LYRIC_PROFILE`, `LYRIC_VERIFY_IR`, `LYRIC_VM`. Es gibt kein `lyric env`. | gemessen |
| **F12** | **Keine Shell-Completions, keine Manpages.** | gemessen |
| **F13** | **`lyric fmt` ohne Pfad druckt die lyrfmt-Hilfe und exit 0.** Ein CI-Schritt, der den Pfad aus einer leeren Variablen zieht, meldet Erfolg, ohne etwas formatiert zu haben. | gemessen |
| **F14** | **`lyric build` in fremdem Checkout führt fremden Code mit ALLEN Capabilities aus** (`src/Lyrbuild/Program.cs:336`: `Capabilities = Capability.All`; im Guide `16-building.md:168` ausdrücklich zugegeben). Kein `--no-script`, keine Sandbox, keine Vertrauensfrage. | gelesen |
| **F15** | **Drei gleichartige „Name existiert nicht"-Fälle, zwei Exit-Codes.** `--only nichtda` → `LYR-CLI0019`, exit 2. Nicht passender `--filter` → `LYR-CLI0009`, exit 1. `lyric --vm C:/nope/lyrvm.exe run hello.lyr` → `LYR-CLI0005: lyrvm not found …`, exit **1**. Nach der Regel „du hast einen Namen genannt, den es nicht gibt = Fehlbedienung" müssten die letzten beiden 2 sein. | gemessen |
| **F16** | **`lyrc` zerstört seinen eigenen JSON-Strom.** `lyric.json` mit einem unbekannten Schlüssel, dann `lyrc build --json src/main.lyr -o out.lyrbc` → auf stderr erst `warning[LYR-CLI0017]: …: unknown key 'typoKey'` als **Text**, dann `{"diagnostics":[]}`. Kontrolle ohne den Schlüssel: nur `{"diagnostics":[]}`. Ursache gelesen: `src/Lyrc/Program.cs:304` und `:494` rufen `CliDiagnostics.Warn` ohne das `json`-Argument, ebenso `src/Lyrtest/Program.cs:96`. Das ist exakt die Fehlerklasse, die `src/Lyric.Core/CliDiagnostics.cs:107-115` als behoben beschreibt („unter --json landete sie mitten im Dokument und zerstörte es"). | gemessen |
| **F17** | **Ein zusammengesetztes Verb mischt zwei Ausgabeformate auf einem Strom.** `lyric run oob.lyr --json` → stderr trägt `{"diagnostics":[]}panic [LYR-VM0006]: index 9 is outside an array of length 3 …` — JSON-Dokument und Text-Panikbericht ohne Trennzeichen, exit 101. Ursache: `Split` (`src/Lyric.Cli/Program.cs:212-236`) gibt nur `--jit`/`--grant` an die Laufzeit, `--json` bleibt beim Compiler. Kontrolle: `lyric run bad.lyr --json` (Compilefehler) liefert ein sauberes Dokument. | gemessen |
| **F18** | **Dasselbe Programm hat zwei Aufrufkonventionen.** `lyric run args.lyr --verbose` → der *Compiler* nimmt das Flag (Phasentabelle erscheint), das Programm sieht null Argumente (exit 0). `lyric run args.lyr --count 3` → `error[LYR-CLI0003]: unknown option '--count' — try 'lyrc --help'` — die Meldung beschuldigt den Compiler für ein Flag des Programms. Kontrolle: `lyric run args.lyr -- a b` → exit 2 (zwei Argumente angekommen). Dasselbe Programm **gepackt**: `./args.exe --count 3` → exit 2, Kontrolle `./args.exe` → exit 0, weil der Stub alles durchreicht (`src/Lyrstub/Program.cs:11-13`, `:68`). | gemessen |
| **F19** | **Kein Cache, keine Sperre.** Zwei `lyric build` hintereinander bauen beide neu und drucken beide dieselbe Bytezahl; keine „up to date"-Meldung. Gelesen: kein Treffer für `cache`, `fingerprint`, `LastWriteTime`, `incremental` unter `src/Lyrbuild` und `src/Lyric.Frontend/Compiler`; kein Treffer für `mutex`, `lockfile`, `.lock` im ganzen `src`. Zwei gleichzeitige Bauten schreiben dieselben Dateien. | gemessen + gelesen |
| **F20** | **Fünf Bedeutungen für `--`.** `lyric run x.lyr -- a b` reicht weiter. `lyric pack hello.lyr --` → `LYR-CLI0003: pack: '--' has no place here …`, exit 2 (`src/Lyric.Cli/Program.cs:258-262`). `lyric check -- hello.lyr` → `unknown option '--'`, exit 2. `lyric fmt -- hello.lyr` → `LYR-CLI0001: no such file or directory: --`, exit 1. `lyric build -- hello.lyr` → `LYR-CLI0003: unexpected argument 'hello.lyr' — build takes one file`. | gemessen |
| **F21** | **`lyric run` in einem Projekt ist nicht abschaltbar laut.** `lyric run` druckt `…\out\debug\demo2.lyrbc: 2832 bytes` auf **stderr** (Kontrolle: mit `2>/dev/null` bleibt nur die Programmausgabe auf stdout), und `lyric run --quiet` → `LYR-CLI0003: unknown argument: --quiet`, exit 2. Auf dem Datei-Pfad hängt der Treiber `--quiet` selbst an (`src/Lyric.Cli/Program.cs:94`); der Projektpfad hat diesen Griff nicht (`:147-148`). | gemessen |
| **F22** | **`lyric build --help` führt das Buildskript aus.** Die Ausgabe endet mit `build.lyr declares no options.` (`src/Lyrbuild/Program.cs:473`) — die Hilfe hat das Skript gestartet, um das zu wissen. Richtig als Hilfe, riskant als Grundlage für Completions (F14). | gemessen |
| **F23** | **`lyric.json` ist JSONC, ohne dass es irgendwo steht.** Gelesen `src/Lyric.Core/ProjectFile.cs:245-254`: `CommentHandling = JsonCommentHandling.Skip`, `AllowTrailingCommas = true`. Gemessen: die von `lyric new` erzeugte Datei enthält `//`-Kommentare und wird gelesen. | gemessen + gelesen |
| **F24** | **Keine Eingabekonvention für stdin.** `lyrfmt --stdin` existiert und funktioniert auch über den Treiber. `lyrc check --stdin` → `unknown option '--stdin'`. `lyrc build -` → `error[LYR-CLI0001]: failed to read file: -`, Kontrolle `lyrc build nosuch.lyr` liefert dieselbe Form — `-` ist nur ein Dateiname, den es nie gibt. | gemessen |
| **F25** | **Der Treiber fragt kein Werkzeug nach seiner Version.** `Tool.Display` (`src/Lyric.Core/Tool.cs:55-59`) liefert „bundled" oder den Pfad, nie die Version dahinter; `Tool.Run` (`:68-102`) startet, was der Pfad hergibt. Ein 4.6-Treiber mit `LYRIC_VM` auf einem alten `lyrvm` ist vollkommen stumm, obwohl `LYRIC_*`-Werkzeugpfade ein dokumentierter Mechanismus sind. | gelesen |
| **F26** | **Werkzeugkastenform kostet Prozessstarts.** Gemessen, Debug-Apphosts, neun Läufe, Minimum/Median in ms: `lyric --version` 270/631 · `lyrvm --version` 277/499 · `lyrvm run hello.lyrbc` 189/215 · `lyric run hello.lyr` 716/816. Ein `lyric run` startet drei .NET-Prozesse (`src/Lyric.Cli/Program.cs:93-97`). Größenordnung: rund 0,2 s je Prozessstart, dreimal bezahlt. Exakte Anteile behaupte ich nicht — die Streuung ist größer als der Unterschied. | gemessen |

**Was gut ist und bleiben sollte** (damit die Liste nicht nur Löcher zeigt):
`lyrbuild --help` führt das Build-Skript aus, um dessen eigene `-D`-Optionen zu listen
(`src/Lyrbuild/Program.cs:66-72`, `454-483`) — das ist Zigs Antwort, und sie ist richtig
(mit der Einschränkung aus F22/CLI-37).
`--print-path` schreibt genau eine Zeile auf stdout und schiebt alles andere nach stderr
(`Program.cs:49`, `262-271`) — eine saubere Maschinenschnittstelle zwischen zwei Werkzeugen.
Gemessen bestätigt: stdout eines `lyric run` trägt ausschließlich die Programmausgabe.
Die Datei-oder-Name-Regel ist eine Regel, nicht drei. `lyrbuild` behandelt fehlende Optionswerte
korrekt (`Value(args, ref i, …)`), im Gegensatz zu `lyrtest`. Und `Tool.Run` schirmt Ctrl+C ab und
wartet auf das Kind (`src/Lyric.Core/Tool.cs:80-95`) — das machen viele Wrapper falsch.

---

## 2. Sprachvergleich

Alle Angaben über fremde Sprachen sind **behauptet** im Sinne der Belegregel dieses Dossiers —
ich habe keine dieser Toolchains in dieser Sitzung laufen lassen. Die erste Fassung hat acht
davon falsch dargestellt; die Korrekturen stehen unter der Tabelle.

| | Treiberform | Profile / Buildmodi | Flagsyntax | Erweiterbar | Maschinenausgabe | Berechtigungen |
|---|---|---|---|---|---|---|
| **Rust/cargo** | ein Treiber, `rustc` nur im Notfall | `dev`/`release`/`test`/`bench` + **eigene, per `inherits`** | GNU: `--f=v`, `-f v`, Cluster | `cargo-foo` auf dem PATH | `--message-format=json` (JSONL) | keine |
| **Go** | ein Binary, `go tool <x>` als Luke | **keine** — ein Build, immer optimiert | `-f=v`, `-f v`; `-f=false` für bool | `go tool`, sonst nein | `go build -json`, `go test -json` | keine |
| **Deno** | ein Binary, **inkl. LSP** (`deno lsp`) | keine Profile | GNU, mit Wertlisten (`--allow-net=a,b`) | `deno task` | **uneinheitlich**: `deno info --json`, `deno lint --json`; `deno coverage` liefert LCOV; `deno test` steuert über `--reporter`; `deno check` hat keine JSON-Form | **`--allow-*`/`--deny-*`, pfad- und hostgenau, plus interaktiver Prompt** |
| **Swift** | `swift <verb>` + `swiftc` | `-c debug`/`-c release`, zwei | GNU + `-Xswiftc`-Durchreiche | SwiftPM-Plugins mit Rechten | `swift package describe --type json`, `swift package dump-package`, `swift test --xunit-output` | **Manifest und Plugins laufen sandboxed** |
| **.NET** | `dotnet <verb>` | **beliebige Konfigurationsnamen**, `-c <name>` | GNU + `-p:Key=Value` als Universal-`-D` | `dotnet tool install -g` | `--logger`, strukturiert über MSBuild | keine |
| **Zig** | ein Binary, alles drin | **vier**: `Debug`, `ReleaseSafe`, `ReleaseFast`, `ReleaseSmall` | `zig build-exe -O <Modus>` (getrennt), `zig build -Doptimize=<Modus>` (mit `=`) | `build.zig` ist ein Programm | `zig env` (JSON); für Builds **keine** JSON-Form, nur `--color on\|off\|auto` als Terminalsteuerung | keine |
| **Bun** | ein Binary: Runtime + PM + Bundler + Tests | keine | GNU | `bunx` | `--reporter` bei Tests | keine |
| **Elm** | **ein Binary, acht Verben, Schluss** | `--debug` xor `--optimize` | `--output=x`, `--report=json` | **nein, bewusst** | `--report=json` | keine |

**Acht Korrekturen gegenüber der ersten Fassung** (die Kritik hatte in allen acht recht):

1. **Zig, Flagsyntax.** Die zusammengeschriebene Form `-Ostufe` gibt es nicht. `zig build-exe`
   nimmt `-O <Modus>` als eigenes Argument, `zig build` nimmt `-Doptimize=<Modus>`. Für CLI-6
   heißt das: Zig taugt als Vorbild für `-Dname=value`, **nicht** für `-O=…`. Die `=`-Form gibt
   es dort nur beim `-D`-Mechanismus — genau dem, den Lyric schon hat.
2. **Zig, Maschinenausgabe.** `--color` ist Terminalsteuerung, keine Maschinenausgabe. Zigs
   echte Maschinenausgabe ist `zig env` (JSON) — und das ist genau das Vorbild, das CLI-9 A
   zitiert. Die erste Fassung hat Zig hier besser dastehen lassen, als es ist, und dabei das
   beste Argument für `lyric env` verschenkt.
3. **Deno, Konfigurationsdatei.** Die Behauptung „Deno musste dafür die Konfigurationsdatei
   nachziehen" ist unbelegt und vermutlich falsch: `deno.json` hat keine Rechtesektion. Denos
   Antwort auf lange Rechtelisten sind **benannte Aufrufe** — `deno task`, `deno install`,
   `deno compile` backen die `--allow-*`-Flags in einen Befehl oder ein Binary ein. Das ändert
   die Lehre: das Vorbild löst Kommandozeilenlänge über benannte Aufgaben. Diese Option fehlte
   im Dossier und steht jetzt in CLI-35.
4. **Deno, `--json` breit.** Nicht breit, sondern uneinheitlich (siehe Tabelle). Als Beleg
   dafür, dass „alle das haben", trägt es nicht.
5. **Elm, Compilerflags.** `elm make --docs <datei>` ist eine fünfte. Elms Minimalismus
   schließt die **Dokumentationserzeugung** ein — was die Empfehlung „`doc` als eigenes Verb"
   gegen ein Vorbild stellt, das sie als Compilerflag löst. Dazu CLI-2 und CLI-22.
6. **Go, `go env`.** Es ist **ein** Verb mit Flags: `go env`, `go env -w NAME=WERT`,
   `go env -u NAME`. Die erste Fassung sprach von zwei Verben und lehnte CLI-9 B unter anderem
   mit „ein Verb mehr"-Ökonomie ab — das Vorbild kostet kein zweites Verb.
7. **Rust, `cargo bench`.** Rusts eingebauter `#[bench]`-Harness ist nightly-only; auf stable
   ist `cargo bench` ein Verb, das ein `harness = false`-Binary (meist criterion) startet. Das
   Vorbild ist „ein Verb, das an einen vom Benutzer gelieferten Harness übergibt", nicht „ein
   Verb, das an ein Sprachattribut gebunden ist". Das stützt die Reihenfolge „erst `@Bench`,
   dann das Verb" gerade **nicht** — siehe CLI-22.
8. **Swift, `--format json`.** Diese Flagge gibt es in SwiftPM nicht; die Maschinenausgabe
   heißt `swift package describe --type json` bzw. `swift package dump-package`, und `swift
   test` schreibt JUnit über `--xunit-output`.

### Die Gegenentscheidungen

**Go hat gar keine Profile.** Das ist die direkte Umkehrung von Lyrics „zwei Profile, sieben
Schreibweisen für vier Felder". `go build` liefert immer denselben optimierten, mit Debug-Info
versehenen Binary; wer debuggen will, hängt `-gcflags="all=-N -l"` an, wer strippen will
`-ldflags="-s -w"`. **Was Go gewinnt:** es gibt keine Frage „in welcher Form habe ich das
eigentlich gebaut", keine Doppelverzeichnisse, keinen veralteten Debug-Build, und der
Compilerbau muss nicht zwei Formen gleich gut testen. **Was es kostet:** die Tuning-Knöpfe sind
Escape-Hatches mit Compiler-internen Namen, und der Debug-Pfad ist die unbequeme Variante statt
der Vorgabe — genau umgekehrt zu Lyric. Go kann sich das leisten, weil sein Optimierer bewusst
flach ist und Inlining den Stacktrace nicht zerstört; Lyrics Debug-Profil existiert, weil sein
Inliner das sehr wohl tut (`docs/guide/16-building.md:334-339`).

**Elm hat den Werkzeugkasten abgelehnt.** Acht Verben, kein Testläufer, kein Formatierer, fünf
Compilerflags (`--debug`, `--optimize`, `--output`, `--report`, `--docs`). Dafür zwei Verben,
die sonst niemand hat: `elm diff` und `elm bump` **rechnen die Semver-Erhöhung aus dem API-Diff
aus**, und `elm publish` verweigert eine falsche Version. **Was Elm gewinnt:** eine CLI, die man
in fünf Minuten kennt, und eine Registry, in der Versionsnummern nicht lügen. **Was es kostet:**
`elm-format`, `elm-test`, `elm-review` sind Drittprojekte mit eigener Installation, eigener
Versionierung und eigenem Flagdialekt — der Werkzeugkasten entstand trotzdem, nur unkoordiniert.

**Deno hat Lyrics `--grant` zu Ende gedacht.** Lyric kennt fünf Bits, zwei Sammelnamen (`all`,
`none`) und sechs Zweitschreibweisen (`fileAccess`, `network`, `networkAccess`, `osAccess`,
`hostAccess`, `processAccess`) — gelesen, `src/Lyric.Core/Capabilities.cs:118-137`. Die erste
Fassung sagte „drei Aliase". Deno kennt dieselben Achsen **mit Werten**: `--allow-read=./data`,
`--allow-net=api.example.com:443`, dazu `--deny-*`, das stärker ist als jedes `--allow`, und
einen interaktiven Prompt, wenn zur Laufzeit etwas fehlt. **Preis:** die Kommandozeile eines
echten Programms wird lang — und Denos Antwort darauf ist `deno task`/`deno install`/`deno
compile`, also **benannte Aufrufe**, nicht eine deklarative Rechtedatei. Für Lyric relevant,
weil das Capability-Modell laut Charakterliste die Host-Grenze ist.

**Swift sandboxt das Buildskript.** `Package.swift` ist ein Programm wie `build.lyr`, aber
SwiftPM wertet es unter `sandbox-exec` aus (kein Netz, Schreibrechte begrenzt), und Plugins
deklarieren ihren Bedarf im Manifest und brauchen die Bestätigung
(`--allow-writing-to-package-directory`, `--allow-network-connections`). Das ist die direkte
Antwort auf Lyrics F14 — und die Deklarationshälfte davon ist das Vorbild für CLI-35.
**Preis:** die Sandbox gibt es nur auf macOS; auf Linux ist die Zusage dünner.

**Zig hat vier Modi statt zwei, und sie heißen nach ihrem Zweck.** `ReleaseSafe` behält
Überlauf- und Bounds-Checks und optimiert; `ReleaseFast` wirft sie weg; `ReleaseSmall`
optimiert auf Größe. Damit ist „optimiert" von „ungeprüft" getrennt — in Lyric sind die beiden
nicht getrennt, weil `release` schlicht `Optimize=true` heißt und Lyric die Checks nie entfernt
(kein `unsafe`, Charakterliste). Zig zahlt dafür mit vier Formen, die jede Bibliothek testen
müsste.

**.NET hat Konfigurationen statt Profilen.** `-c` nimmt jeden String; `Debug` und `Release` sind
Konvention, kein Gesetz. Dazu `-p:Key=Value` als universeller Durchgriff. **Gewinn:**
`-c Staging` ohne Compileränderung. **Preis:** `-p:` akzeptiert jeden Tippfehler stumm — genau
der Fehler, den Lyric bei `lyric.json`-Schlüsseln bewusst vermeidet (`LYR-CLI0017`) und bei `-D`
ebenfalls (`LYR-CLI0003` mit den deklarierten Namen).

**Cargo ist erweiterbar, ohne dass cargo davon weiß.** `cargo foo` sucht `cargo-foo` im PATH.
So entstanden `cargo-audit`, `cargo-expand`, `cargo-flamegraph`. `cargo --list` zeigt sie.
**Preis:** die Erweiterungen haben keinen gemeinsamen Flagdialekt und keine gemeinsame
Ausgabeform, und `cargo install` ist ein zweiter Paketmanager neben dem für Bibliotheken.

**Cargo nimmt eine Sperre auf das Zielverzeichnis.** Zwei gleichzeitige `cargo build` warten
aufeinander und sagen das. Lyric hat keine (F19) — das ist CLI-27.

**Bun zeigt, wie die Datei-oder-Name-Regel schiefgeht.** `bun run x` trifft zuerst das
`package.json`-Skript `x`, dann die Datei. Lyric hat hier die bessere Regel
(`Program.cs:205-208`), und sie steht an EINER Stelle. Ein Punkt, an dem Lyric 4 einer
etablierten Sprache voraus ist.

**Gos `go env -w`** ist die sauberste Antwort auf Lyrics zwölf undokumentierte
Umgebungsvariablen: **ein** Verb druckt die wirksame Konfiguration, schreibt mit `-w` eine
benutzerweite Vorgabe und löscht sie mit `-u`. Keine Variable ist „geheim", weil `go env` sie
alle nennt — und die benutzerweite Ebene ist etwas, das eine im Repository liegende
Projektdatei nicht leisten kann. Das ist die Korrektur an CLI-9.

---

## 3. Designfragen

### CLI-1 — Ein Treiber oder ein Werkzeugkasten?

**Heute:** elf ausführbare Projekte, davon **neun startbare Werkzeuge** plus der Treiber plus
die Stub-Vorlage (1.1). Der Treiber vermittelt an sieben (`Tool.cs:38-39`), `lyrls` und `lyrdbg`
erreicht man nur direkt. Die Trennung ist architektonisch begründet: `lyrvm` referenziert nichts
Compilerseitiges (`src/Lyrvm/Program.cs:11-13`), `lyrpack` nur `Lyric.Core`.

**Optionen:**
- **A — bleiben wie es ist, aber der Treiber deckt alles ab.** Alle **neun** startbaren
  Werkzeuge in `Tool.All` (nicht elf: `lyric` startet sich nicht selbst, `lyrstub` ist keine
  Anwendung), Verben `lyric lsp`, `lyric dbg`, `lyric verify`, `lyric info`. *Vorbild:* Swift
  (`swift` + `swiftc`). *Preis:* die Verbenliste wächst; zwei Namen für dieselbe Sache bleiben;
  die drei Prozessstarts je `lyric run` bleiben auch (F26, CLI-25).
- **B — ein Binary, Verben statt Programme.** `lyrvm` nur noch als separat ausliefbares
  Minimal-Binary. *Vorbild:* Deno, Zig, Bun. *Preis:* die „lyrvm referenziert keinen Compiler"-
  Eigenschaft muss als Assembly-Grenze innerhalb eines Binaries erhalten bleiben, oder sie
  fällt. Ein Binary wird groß.
- **C — Treiber plus PATH-Erweiterung.** `lyric foo` startet `lyric-foo`. *Vorbild:* cargo.
  *Preis:* Erweiterungen ohne gemeinsamen Flagdialekt; Sicherheitsfrage, was auf dem PATH liegt.

**Empfehlung: A, plus C als zweite Stufe — aber erst, nachdem CLI-25 eine Zahl hat.** Die
Aufteilung trägt eine echte Eigenschaft (eine Laufzeit ohne Compiler ist verteilbar und
auditierbar). Was fällt, ist die *Sichtbarkeit*: der Treiber muss jedes Werkzeug erreichen und
jede Version nennen (CLI-31). **Neu gegenüber der ersten Fassung:** A ist nicht kostenlos.
Gemessen kostet `lyric run hello.lyr` 716 ms gegen 189 ms für `lyrvm run hello.lyrbc` (F26);
ob A das aushält, entscheidet CLI-25, nicht die Architektur.

**Bruch:** nein (additiv). **4.x:** `lyric lsp`, `lyric dbg`, `lyric verify`, `lyric info` ab
4.7; `lyric --version` um `lyrls` und `lyrdbg` ergänzen.
**Hängt ab von:** CLI-25, CLI-31.

---

### CLI-2 — Welche Verben hat v5?

**Heute:** neun (`Program.cs:29-45`). Gemessen fehlen: `verify`, `info` (existieren in `lyrvm`),
und es gibt kein `clean`, `doc`, `bench`, `fix`, `add`, `install`, `publish`, `watch`, `init`,
`lsp`, `dbg`, `env`, `explain`.

**Optionen:**
- **A — minimal halten.** Nur was Lyric heute kann plus `verify`/`info`. *Vorbild:* Elm.
  *Preis:* der Nutzer lernt die Löcher durch Ausprobieren.
- **B — der volle Satz einer ausgewachsenen Sprache.** *Vorbild:* Deno, cargo. *Preis:* ein
  Verb, das nichts Sinnvolles tut, ist schlimmer als keins. Jedes Verb ist ein Versprechen.
- **C — Stufen.** v5 liefert `verify info clean env doc fix explain lsp dbg` dazu;
  `add/install/publish` erst mit dem Paketmanager, `bench/lint` erst mit dem jeweiligen
  Werkzeug. *Vorbild:* Gos Wachstum.

**Empfehlung: C.** Begründung pro Stück: `verify`/`info` existieren schon (`lyrvm`), `env` löst
F11 auf einen Schlag, `explain LYR-SEM0071` ist `rustc --explain` und zahlt sich bei **19** CLI-
plus über hundert Sprachdiagnosen aus, `fix` ist die CLI-Hälfte von CLI-19, `doc` ist der
Konsument der `///`-Kommentare (`tools/DocGen` existiert bereits).

**Korrektur gegenüber der ersten Fassung:** `clean` ist **nicht** „drei Zeilen (`out/`
löschen)". Ein Skript darf den Ausgabepfad frei setzen (`docs/guide/16-building.md:112`:
`app.output = "dist/app.lyrbc"`), und ein `clean`, das die auch finden will, müsste das Skript
ausführen — womit es unter CLI-17 fällt. Das Verb ist billig, seine **Semantik** ist es nicht.
Was `clean` verspricht, entscheidet CLI-26.

**Bruch:** nein. **4.x:** alles davon ist additiv, kann also ab 4.7 einzeln kommen.
**Hängt ab von:** CLI-26 (`clean`), Gebiet Module/Pakete (`add`/`publish`), Gebiet
Dokumentation (`doc`).

---

### CLI-3 — Was tut `lyric <verb> --help`?

**Heute:** fünf verschiedene Dinge, zwei davon kaputt (F1, gemessen). `lyric run --help` und
`lyric pack --help` produzieren eine `LYR-CLI0012`-Fehlerzeile, in der die komplette
lyrbuild-Hilfe mit escapten Zeilenumbrüchen steckt, exit 1.

**Optionen:**
- **A — der Treiber beantwortet `--help` selbst, vor jeder Weiterleitung.** *Vorbild:* cargo.
  *Preis:* die Optionsliste steht zweimal — und die skriptabhängigen `-D`-Optionen (F22) fallen
  ganz weg.
- **B — der Treiber leitet `--help` weiter, jedes Werkzeug beantwortet es.** *Vorbild:* Gos
  `go help <cmd>` liegt dazwischen. *Preis:* die Usage-Zeile nennt das falsche Programm
  (gemessen), und Werkzeuge ohne Verb können den Kontext nicht kennen.
- **C — beides: der Treiber druckt seinen Verbteil zuerst und hängt den Werkzeugteil an.**
  *Preis:* zwei Prozesse für eine Hilfe, und bei `build` läuft dabei das Skript (F22).

**Empfehlung: C**, mit A als Mindestforderung. `lyric run --help` muss in jedem Fall *lyrics*
Usage-Zeile zeigen. Das Zusammensetzen ist genau das Muster, das `lyrbuild --help` für
Skriptoptionen schon benutzt — ein Mechanismus, zweimal angewandt.

**Zusatzregel, korrigiert.** Die erste Fassung schrieb: „`--help` und `--version` werden vom
Treiber an JEDER Position erkannt und **nie weitergereicht**." Das tötet genau die Eigenschaft,
die §4 Punkt 9 als richtig übernommen feiert: `lyrbuild --help` listet die `-D`-Optionen des
Skripts (F22, gemessen: die Ausgabe endet mit `build.lyr declares no options.`). Richtige
Fassung:

> **`--help` und `--version` werden vom Treiber an jeder Position erkannt; der Treiber antwortet
> ZUERST mit seinem eigenen Teil und reicht danach an das Werkzeug weiter, das etwas
> hinzuzufügen hat.** Bei `--version` hat kein Werkzeug etwas hinzuzufügen, also endet es dort.
> Bei `--help` haben `build` (Skriptoptionen) und `test` (Filterhinweise) etwas, die übrigen
> nicht.

**Bruch:** minor — heute liefert `lyric build --help` exit 0 mit lyrbuild-Text, künftig exit 0
mit anderem Text. Ein Skript, das die Hilfe *parst*, bricht; keins sollte das tun.
**4.x:** direkt in 4.7 reparieren, F1 ist ein Bug, kein Designstreit.
**Hängt ab von:** CLI-4, CLI-37 (die Skript-Ausführung in `--help`).

---

### CLI-4 — Wo dürfen globale Optionen stehen?

**Heute:** zwei Klassen mit zwei Regeln (F7, gemessen). Werkzeugflags (`--compiler`, `--vm`, …)
gelten überall (`ToolSelection.cs:27-46`); `--json`/`--quiet`/`--verbose`/`--progress` nur
hinter dem Verb, weil `args[0]` der Verb-Switch ist (`Program.cs:29`).

**Optionen:**
- **A — global vor dem Verb, verbspezifisch dahinter**, streng getrennt. *Vorbild:* `git
  --no-pager log`, `docker --host … run`. *Preis:* `lyric run --quiet` wäre ein Fehler, und das
  würde niemand erwarten.
- **B — überall erlaubt.** Der Treiber greift die globalen Flags an jeder Position ab, wie er es
  mit `--compiler` schon tut. *Vorbild:* cargo. *Preis:* die Grenze zum Programm muss irgendwo
  liegen — heute ist das `--` (`ToolOptions.cs:53`), und `--` bedeutet in fünf Verben fünf
  Dinge (F20). CLI-33 muss erst entschieden sein.
- **C — nur hinter dem Verb, auch die Werkzeugflags.** *Preis:* bricht bestehende
  Kommandozeilen.

**Empfehlung: B**, aber **nach** CLI-33 und CLI-24. Die erste Fassung schrieb „die Grenze bleibt
`--`" — gemessen existiert diese Grenze nur bei `run`. Solange `lyric check -- x.lyr` ein Fehler
ist, hat B keine Grenze, auf die es sich stützen kann.

**Bruch:** nein (heute abgelehnte Formen werden gültig). **4.x:** nach CLI-33, ab 4.7.
**Hängt ab von:** CLI-5, CLI-24, CLI-33.

---

### CLI-5 — Ein Optionssatz für alle Werkzeuge

**Heute:** `--json`, `--quiet`, `--verbose`, `--progress` werden von vier von elf Binaries
**geparst** und von einem (`lyrc`) vollständig **befolgt** (1.3, gemessen). `lyrbuild`,
`lyrtest`, `lyrrepl`, `lyrls`, `lyrdbg` kennen keins davon. Ergebnis: `lyric build --quiet`
verhält sich anders als `lyric build datei.lyr --quiet` (F2), und `lyrvm --help` verspricht ein
`--json`, das es nicht einlöst.

**Optionen:**
- **A — `ToolOptions` wird Pflicht für jedes Binary.** *Vorbild:* cargo. *Preis:* `--progress`
  muss für `lyrls` eine Bedeutung haben oder ausdrücklich „ohne Wirkung hier" sein — und „nimmt
  es an und tut nichts" ist laut den eigenen Kommentaren (`src/Lyrc/Program.cs:66-69`) genau das
  verbotene Muster. Dass `lyrvm`/`lyrfmt`/`lyrpack` `--json` heute annehmen und nichts tun, ist
  derselbe Fehler, nur schon begangen.
- **B — zwei Ebenen: ein Pflichtkern und ein optionaler Satz.** Pflicht: `--help`, `--version`,
  `--quiet`, `--json`, `--color`. Optional, mit Begründung pro Werkzeug: `--verbose`,
  `--progress`. *Vorbild:* Gos `flag`-Paket plus Konvention.
- **C — so lassen und im Guide dokumentieren.** *Preis:* der Guide behauptet heute schon das
  Gegenteil (16:375).

**Empfehlung: B**, und die Liste ausdrücklich in die Spezifikation (CLI-30). Der Pflichtkern
kostet jedes Werkzeug vier Zeilen. **Auflage, die die erste Fassung nicht hatte:** „Pflichtkern"
heißt **befolgen**, nicht parsen. Jedes Werkzeug, das `--json` annimmt, muss jede Diagnose durch
`RenderJson` schicken — heute tun das `lyrfmt` (`:62`, `:112`) und `lyrvm` nicht, und F16 zeigt,
dass selbst `lyrc` es nicht durchhält.

**Bruch:** nein (Annahme zusätzlicher Flags). **4.x:** ab 4.7 jedes Werkzeug nachziehen; der
Guide-Satz 16:375 wird dabei wahr.
**Hängt ab von:** CLI-11, CLI-13, CLI-23.

---

### CLI-6 — Flagsyntax: `--flag=value`, Kurzflags, Negation

**Heute:** nur `--flag value`; `lyrc build f.lyr --output=x` → `unknown option '--output=x'`,
exit 2 (gemessen). Kurzformen: `-v -h -q -o -D`. Keine Bündelung. Negation per ausgeschriebenem
Gegenflag — **unvollständig**: `--no-deny-warnings` gibt es nicht (1.4, gemessen).

**Optionen:**
- **A — GNU-Form voll unterstützen:** `--flag=value`, `-f value`, `-fvalue`, `-abc`-Cluster.
  *Vorbild:* cargo, Deno, .NET, Elm (`elm make --output=x`). *Preis:* mehr Parsercode; Cluster
  sind bei fünf Kurzflags Ballast.
- **B — nur `--flag=value` dazu, keine Cluster, plus vollständige Negation.** *Vorbild:* Zigs
  `-Dname=value` (nicht `-O`, siehe Korrektur 1 in §2), Go (`-flag=value`). *Preis:* eine Zeile
  im gemeinsamen Parser.
- **C — so lassen.** *Preis:* jedes Shell-Skript und jede CI-Vorlage aus einer anderen Sprache
  scheitert beim ersten `=`.

**Empfehlung: B**, mit der Auflage **symmetrischer Negation**: zu jedem booleschen Flag gibt es
`--no-<name>`, ausnahmslos. `--deny-warnings` ohne `--no-deny-warnings` ist heute schon ein Loch
und wird mit CLI-8 B zum Defekt (ein geerbtes `"denyWarnings": true` wäre sonst auf der
Kommandozeile nicht abschaltbar). `-D` hat mit `-Dname=value` die `=`-Form schon
(`src/Lyrbuild/Program.cs:166-171`), die Inkonsistenz existiert also innerhalb eines Werkzeugs.

**Bruch:** nein. **4.x:** ab 4.7.
**Hängt ab von:** CLI-5, CLI-8.

---

### CLI-7 — Strenge und Wortlaut beim Optionsparsen

**Heute:** **vier** Verhaltensweisen (F4, gemessen), nicht drei: ablehnen mit Code
(`lyrc lyrvm lyrbuild lyrtest`), still schlucken (`lyrpack lyrrepl lyrdbg`), als fehlende Datei
melden (`lyrfmt`), ablehnen ohne Diagnosecode (`lyrls`). Ein fehlender *Wert* heißt bei
`lyrtest` „unbekanntes Argument" (F6).

**Optionen:**
- **A — ein gemeinsamer Parser, der alle Fälle unterscheidet:** unbekannte Option, bekannte
  Option ohne Wert, Wert mit falscher Form. *Vorbild:* cargos `clap`. *Preis:* ein Parser als
  gemeinsame Abhängigkeit — bei Binaries, die schon `Lyric.Core` teilen, kein Problem.
- **B — pro Werkzeug, aber eine Checkliste in CONTRIBUTING.** *Preis:* driftet wieder; genau
  das ist passiert (vier Verhaltensweisen).
- **C — dazu Tippfehlervorschläge** („did you mean `--release`?"). Die Mechanik existiert:
  gemessen `lyrc check typo.lyr` → `error[LYR-SEM0002]: unknown identifier 'cout'` +
  `note: did you mean 'count'?`; gelesen `src/Lyric.Frontend/Sema/NameSuggestion.cs:6-49`
  (Levenshtein, Budget 1 bei ≤4 Zeichen, sonst 2, eine Antwort oder keine); Guide-Stelle ist
  `docs/guide/19-diagnostics.md:98`. *Preis:* eine Funktion, die es schon gibt.

**Empfehlung: A + C.** Die Vorschlagsmechanik ist *kein* zweiter Mechanismus, sondern derselbe.
Fehlender Wert bekommt einen eigenen Code (`LYR-CLI0002` ist der richtige). `lyrls` bekommt
Diagnosecodes; ein Werkzeug, das Usage-Fehler ohne Code meldet, ist von einem Skript nicht
unterscheidbar behandelbar.

*Belegkorrektur:* die erste Fassung zitierte für „did you mean" `19-diagnostics.md:63-70` — dort
steht die SEM0107–SEM0110-Warntabelle. Die Sache stimmt, die Fundstelle war falsch.

**Bruch:** minor — `lyrpack … --bogus`, `lyrrepl --bogus` und `lyrdbg --bogus` scheitern künftig.
**4.x:** ab 4.7 Deprecation-Warnung nach CLI-29, Wirkung in 5.0.
**Hängt ab von:** CLI-5, CLI-29.

---

### CLI-8 — Wie viele Profile, und dürfen es eigene sein?

**Heute:** genau zwei, im Compiler fest (`Profile.cs:22-31`, `51-56`), vier Felder, sieben
Schreibweisen — **und diese Schreibweisen gibt es nur auf dem Datei-Pfad** (1.4, gemessen).
`lyric.json` kann **kein** Profil setzen (`ProjectFile.cs:278-302` kennt `name sourceRoot
nativeRoots dependencies testRoot toolchain`); ein `build.lyr` kann pro Artefakt Felder setzen
und `use(Profile.release())` sagen (`docs/guide/16-building.md:104-113`).

**Optionen:**
- **A — bei zwei bleiben.** *Vorbild:* Swift (`-c debug|release`), Elm. *Preis:* „optimiert,
  aber mit Namen" braucht zwei Flags, und ein Projekt kann seine Hauskombination nicht benennen.
- **B — benannte Profile in `lyric.json`, mit Vererbung.** `"profiles": { "ci": { "inherits":
  "release", "denyWarnings": true } }`, `--profile ci`. *Vorbild:* cargo. *Preis:* siehe die
  drei Folgefragen unten.
- **C — vier feste, nach Zweck benannt** (Zig). *Preis:* Lyric entfernt nie Checks, also hätte
  `releaseSafe` keinen Gegenpart; `releaseSmall` bräuchte einen Größenoptimierer, den es nicht
  gibt.
- **D — beliebige Namen wie .NET.** *Preis:* `-c Relese` ist stumm gültig.

**Empfehlung: B**, mit drei Auflagen, die die erste Fassung schuldig geblieben ist:

1. **Der Feld-Override-Satz muss erst auf dem Projektpfad existieren.** Gemessen: `lyric build
   --no-optimize` → `LYR-CLI0003`, exit 2. Vererbung auf eine Override-Ebene zu bauen, die es
   dort nicht gibt, ist die falsche Reihenfolge. `lyrbuild` lernt den Satz zuerst (CLI-5).
2. **Die Negation muss symmetrisch sein** (CLI-6): ohne `--no-deny-warnings` ist ein geerbtes
   `"denyWarnings": true` auf der Kommandozeile nicht abschaltbar — gemessen, es gibt das Flag
   heute nicht.
3. **`out/<profil>/` braucht eine formulierte Namensregel.** Erlaubt: `[A-Za-z][A-Za-z0-9_-]*`,
   1–32 Zeichen, **Vergleich ohne Groß-/Kleinschreibung** (auf Windows kollidieren `Release` und
   `release` im Dateisystem, auf Linux nicht — eine Regel, die auf zwei Plattformen zwei Dinge
   bedeutet, ist keine), kein Pfadtrenner, und die zwei eingebauten Namen sind belegt. Ein Name,
   den niemand deklariert hat, ist ein Fehler mit der Liste der deklarierten Namen — dieselbe
   Behandlung, die `--only` schon hat (`LYR-CLI0019`, gemessen).

C fällt weg, weil Lyrics Charakter die Achse nicht hat; D fällt weg, weil es stumm ist.

**Bruch:** nein (additiv). **4.x:** Auflage 1 und 2 in 4.7, der `profiles`-Schlüssel danach;
unbekannte Schlüssel warnen in `lyric.json` heute schon (`LYR-CLI0017`), ältere Toolchains laden
die Datei also weiter — **sofern F16 behoben ist**, sonst zerbricht genau diese Warnung den
JSON-Strom.
**Hängt ab von:** CLI-5, CLI-6, CLI-38, Gebiet Projekt/Module, Gebiet Build.

---

### CLI-9 — Umgebungsvariablen, Selbstauskunft, und wo eine benutzerweite Vorgabe hingehört

**Heute:** zwölf `LYRIC_*`-Variablen im Quelltext, vier im Guide (F11, gemessen). Eine
verschluckt einen Tippfehler still (F3, `LYRIC_PROFILE`), eine ist ein reiner Diagnoseschalter
(`LYRIC_VERIFY_IR`), sieben benennen Werkzeugpfade — und für `lyrls`/`lyrdbg` gibt es keine.

**Optionen:**
- **A — `lyric env` als Verb**, das jede Variable, ihren wirksamen Wert und ihre Herkunft
  druckt; `--json` dazu. *Vorbild:* `go env`, `zig env` (JSON), `dotnet --info`. *Preis:* ein
  Verb mehr.
- **B — dazu `lyric env -w NAME=WERT` / `-u NAME`**, das eine **benutzerweite** Vorgabe schreibt.
  *Vorbild:* `go env -w` — **ein** Verb mit Flags, kein zweites (Korrektur 6 in §2). *Preis:*
  ein Konfigurationsort auf der Maschine, der nicht im Repository liegt; eine Präzedenzstufe
  mehr in CLI-21.
- **C — jede Variable, die ein Werkzeug liest, wird validiert**, wie ein Flag es würde. Ein
  unbekannter `LYRIC_PROFILE`-Wert ist ein Fehler.
- **D — so lassen, nur dokumentieren.**

**Empfehlung: A + C + B.** **Geändert gegenüber der ersten Fassung**, die B als
„Parallelmechanismus ohne Gewinn" abgelehnt hat, weil „die Projektdatei das schon kann". Kann
sie nicht: eine **benutzerweite** Vorgabe — bevorzugte Farbe, Werkzeugpfade, wo die Stdlib auf
*dieser* Maschine liegt — lässt sich in einer Datei, die im Repository liegt und mit
eingecheckt wird, nicht ausdrücken. Gleichzeitig fügt CLI-21 A `lyric.json` als neue Sprosse
für Profil/Farbe/Stdlib hinzu; zwei Konfigurationsorte mit zwei verschiedenen Ellen zu messen
war der Fehler. Die Frage heißt **projektweit gegen benutzerweit**, und sie hat zwei Antworten:

| Einstellung | gehört nach |
|---|---|
| Profil, `denyWarnings`, Artefakte, Abhängigkeiten, Toolchain-Minimum | `lyric.json` (projektweit, eingecheckt) |
| Werkzeugpfade (`LYRIC_VM`, …), Stdlib-Ort, Farbe, bevorzugtes Standardprofil dieser Maschine | benutzerweite Konfiguration (`lyric env -w`) |

C ist ein echter Bugfix — F3 kostet im Ernstfall einen ausgelieferten Debug-Build. Der Einwand
im Kommentar (`Profile.cs:40-45`, „eine Bibliothek hat keine Stelle zum Klagen") lässt sich
lösen, indem `Profile.Default` das Problem *meldet* statt es zu entscheiden.

**Bruch:** minor — eine CI mit `LYRIC_PROFILE=relese` schlägt künftig fehl statt still falsch zu
bauen. **4.x:** ab 4.7 Deprecation-Warnung nach CLI-29, Wirkung in 5.0. `lyric env` und
`env -w` sind additiv.
**Hängt ab von:** CLI-21, CLI-29, CLI-30.

---

### CLI-10 — Wie erfährt der Treiber, welches Flag zu welchem Werkzeug gehört?

**Heute:** zwei handgepflegte Tabellen im Treiber. `ValueOptions` (`Program.cs:196-200`) kennt
neun wertnehmende Optionen — `--progress` fehlt, und das ist messbar falsch (F5). `Split`
(`Program.cs:212-236`) schickt `--jit` und `--grant` an die Laufzeit, **alles andere** an den
Compiler — also auch `--json` (F17) und jedes künftige Laufzeitflag.

**Optionen:**
- **A — Tabellen pflegen, aber mechanisch absichern.** Jedes Werkzeug exportiert seine
  Optionsliste (Name, nimmt Wert, **Phase**: compile / run / beide) aus `Lyric.Core`; der
  Treiber liest dieselbe Liste. *Vorbild:* cargos zentrale `clap`-Definition. *Preis:* die
  Optionsdefinition wandert aus den `Program.cs` in eine gemeinsame Datei.
- **B — Durchreiche-Syntax wie Swift.** `lyric run app.lyr -Xvm --jit -Xc --release`. *Vorbild:*
  `swift build -Xswiftc -O`. *Preis:* hässlich, aber eindeutig und unkaputtbar.
- **C — Werkzeug fragt, Treiber probiert.** *Preis:* zwei Prozessstarts pro Lauf (bei gemessenen
  ~0,2 s je Start, F26, nicht harmlos), und ein Flag, das beide kennen, ist mehrdeutig.

**Empfehlung: A, mit B als ausdrücklichem Notausgang.** Die **Phasenspalte** ist neu gegenüber
der ersten Fassung und löst F17: `--json`, `--quiet` und `--color` sind „beide" und werden an
beide Werkzeuge gegeben, `--release` ist „compile", `--jit`/`--grant` sind „run". Siehe CLI-23.

**Bruch:** nein. **4.x:** F5 ist heute ein Bug und gehört in 4.7 repariert, unabhängig von A.
**Hängt ab von:** CLI-5, CLI-23.

---

### CLI-11 — Maschinenlesbare Ausgabe

**Heute — korrigiert.** Die erste Fassung schrieb, `--json` gebe Diagnosen als JSON in vier
Werkzeugen aus. Gemessen (1.3): **ein** Werkzeug handelt danach (`lyrc`), drei parsen das Flag
und tun nichts; `lyrvm info --json` gibt zusätzlich den Modulkopf als JSON
(`src/Lyrvm/Program.cs:142-143`). Kein Schema, keine Versionsangabe, kein `--json` für
`lyrbuild` oder `lyrtest`. Und der eine funktionierende Strom wird von einer Warnung desselben
Programms zerbrochen (F16) und von einem zusammengesetzten Verb mit Text vermischt (F17).

**Das ist kein Ausbau, sondern eine Erstimplementierung in acht von neun Werkzeugen.**

**Optionen:**
- **A — `--json` überall, ein Ereignisstrom, eine Zeile je Ereignis (JSONL).** *Vorbild:* cargo
  `--message-format=json`, `go test -json`. *Preis:* das Format wird eine Kompatibilitätszusage
  — Version im Dokument, Platz in der Spezifikation (CLI-30). **Und es ist ein Formatwechsel,
  kein Zusatz** (siehe Bruch).
- **B — `--json` nur für Diagnosen, wie heute, aber in allen Werkzeugen.** *Preis:* ein
  CI-Dashboard kann Testergebnisse nicht lesen, ohne die Textausgabe zu parsen.
- **C — ein Format pro Frage:** `--report=json` wie Elm, dazu JUnit-XML fürs Testprotokoll.
  *Preis:* zwei Formate, zwei Schemata.

**Empfehlung: A, mit einer Übergangsform.** Ein Ereignisstrom mit `"schema": 1` und `"kind"` in
jeder Zeile deckt Diagnosen, Buildfortschritt, Testergebnisse, Paniken (F17) und `info` mit
einem Mechanismus ab. Die stderr-Wahl bleibt (stdout gehört dem Programm oder der angeforderten
Ausgabe), mit der bestehenden Ausnahme `--print-path`.

**Bruchbewertung, korrigiert — die erste Fassung war zu milde („minor, zusätzliche Zeilen").**
Gemessen ist die heutige Ausgabe **ein** JSON-Dokument ohne abschließenden Zeilenumbruch
(`{"diagnostics":[…]}`). JSONL ist ein *anderes* Format, kein erweitertes: jeder Verbraucher,
der `JSON.parse` auf den ganzen Strom anwendet, bricht — und das sind alle, weil es heute nichts
anderes gibt. **Bruch: major.** Die Übergangsform:

- **4.7:** `--message-format=<form>` mit `json-document` (die heutige Form, Vorgabe) und `jsonl`
  (die neue). `--json` bleibt ein Alias für `--message-format=json-document`.
- **4.7:** F16 und F17 werden repariert — **vor** allem anderen. Ein Strom, den das eigene
  Programm zerbricht, taugt nicht als Vertrag.
- **4.8–4.9:** `--json` warnt nach CLI-29, dass seine Bedeutung in 5.0 auf `jsonl` wechselt.
- **5.0:** `--json` = `--message-format=jsonl`; `json-document` bleibt unter seinem Namen
  erreichbar.

**4.x:** F16/F17 sofort; `--message-format` ab 4.7; das Schemafeld von Anfang an.
**Hängt ab von:** CLI-23, CLI-29, CLI-30, Gebiet Diagnosen.

---

### CLI-12 — Der Rückgabewert von `main` und die Maskierung

**Heute:** gemessen `return 256` → exit **0**, `return -1` → exit **255**, `return 101` → exit
**101**, ununterscheidbar von einer Panik (in `ExitCodes.cs:9-11` eingeräumt). Die Maskierung
steht im Guide (`docs/guide/01-getting-started.md:20`).

**Optionen:**
- **A — so lassen.** *Vorbild:* Rust (Panik = 101, dieselbe Kollision). *Preis:* die 256-Falle
  bleibt.
- **B — `main` darf nur `0..255` liefern**, alles andere ist ein Laufzeitfehler mit Meldung.
  *Vorbild:* keins mir bekanntes auf Sprachebene. *Preis:* bricht Programme, die heute rechnen
  und zurückgeben.
- **C — Warnung zur Übersetzungszeit bei einem konstanten `return` außerhalb `0..255`.**
  *Preis:* trägt nur bei Konstanten — und **das ist genau der Fall, den man beim Hinschreiben
  ohnehin bemerkt**. Der reale Fall ist `return matches.length;`.
- **D — Panik-Kennzeichen auf stderr mit festem Präfix**, das ein Skript prüfen kann.
  *Vorbild:* der `ir-verifier (…)`-Abbruch benutzt das Muster schon
  (`docs/guide/16-building.md:382`); ab CLI-11 ist es ein Ereignis im Strom.
- **E — ein eigener Rückgabetyp für `main`.** `fn main(): ExitCode`, ein Typ mit
  `ExitCode.ok`, `ExitCode.failure`, `ExitCode.of(n)` — und `of` sättigt oder wirft bei
  `n ∉ 0..255`. `fn main(): int` bleibt erlaubt und behält die Maskierung, aber die Vorlage aus
  `lyric new` benutzt den neuen Typ. *Vorbild:* Rusts `std::process::ExitCode` (genau dieser
  Schritt: `main() -> ExitCode` neben `main()`), Haskells `ExitCode`. *Preis:* ein Typ in der
  Stdlib und eine Sonderregel in der Sema für den Rückgabetyp von `main` — also Arbeit im
  Gebiet Funktionen, nicht in der CLI.
- **F — Sättigung statt Maskierung**, mit einer Laufzeitmeldung auf stderr: `return 256` liefert
  255 und sagt es. *Vorbild:* keins. *Preis:* weicht sichtbar von POSIX ab; jedes Handbuch
  erklärt `& 0xFF`.

**Empfehlung: E + D, C fallen lassen, A für den Rest.** **Geändert gegenüber der ersten
Fassung**, die C empfahl: eine Analyse, die nur konstante `return`s trifft, fängt den Fall, den
man sieht, und verfehlt den, den man nicht sieht — das ist Aufwand ohne Wirkung. E trifft beide,
weil der Typ die Frage beim Schreiben stellt statt beim Laufen. F wäre eine Lüge über das
Betriebssystem; die Maskierung ist nicht Lyrics Entscheidung und sie zu verstecken hilft
niemandem. D bleibt, weil die 101-Kollision anders nicht auflösbar ist (ein Byte).

**F15 gehört nicht hierher**, sondern in CLI-32: es ist eine Exit-Code-**Leiter**, keine Frage
über `main`.

**Bruch:** nein (E ist additiv, `fn main(): int` bleibt). **4.x:** D ab 4.7 (fällt aus CLI-11
ab), E mit dem Gebiet Funktionen.
**Hängt ab von:** CLI-11, CLI-32, Gebiet Laufzeit/Panik, Gebiet Funktionen/Stdlib.

---

### CLI-13 — Farbe und Terminal

**Heute:** `TerminalOutput` erkennt ein Terminal über `!Console.IsErrorRedirected`
(`src/Lyric.Core/TerminalOutput.cs:47`) und benutzt eine Escape-Sequenz für die Fortschrittszeile
(`:20`). Es gibt **kein `--color`** und keine Auswertung von `NO_COLOR`. `--progress
auto|never|always` steuert nur den Fortschritt. Die Terminal-Injection-Frage (ESC/BEL/OSC roh
auf stderr) steht als offener Fund in `docs/Befunde_und_Verbesserungen/PLAN.md`.

**Optionen:**
- **A — `--color auto|always|never` plus `NO_COLOR`/`CLICOLOR_FORCE`.** *Vorbild:* cargo
  (`--color` + `CARGO_TERM_COLOR`), Zig (`--color on|off|auto`), die `NO_COLOR`-Konvention.
  *Preis:* eine Option mehr im Pflichtkern.
- **B — Farbe an `--progress` koppeln.** *Preis:* zwei Fragen an einem Schalter.
- **C — keine Farbe.** *Preis:* Diagnosen sind der Ort, an dem Farbe am meisten hilft.

**Empfehlung: A**, und die Escape-Frage gehört mit derselben Entscheidung erledigt: alles, was
aus einer Quelldatei in eine Diagnose wandert, wird escaped, bevor es auf ein Terminal geht.
Die benutzerweite Vorgabe dafür gehört nach CLI-9 B, nicht in `lyric.json`.

**Bruch:** nein. **4.x:** ab 4.7.
**Hängt ab von:** CLI-5, CLI-9.

---

### CLI-14 — Completions, Manpages, Selbstauskunft

**Heute:** keine (F12, gemessen). Keine `lyric completions <shell>`, keine `.1`-Dateien, kein
`lyric --list`.

**Optionen:**
- **A — `lyric completions bash|zsh|fish|powershell`** druckt ein Skript auf stdout. *Vorbild:*
  `rustup completions`, `deno completions`, `gh completion`. *Preis:* vier Ausgaben, die zur
  Verbenliste passen müssen.
- **B — Manpages mitliefern.** *Preis:* Pflegeaufwand; auf Windows, der Hauptplattform dieses
  Projekts, nutzlos.
- **C — nichts, aber `lyric --list` (Verben) und `lyric <verb> --help`** sauber.

**Empfehlung: A + C, B ablehnen.** **Korrektur gegenüber der ersten Fassung**, die schrieb,
Completions fielen „aus CLI-10 fast von selbst ab" und seien „automatisch korrekt": eine
Optionsliste liefert Flag**namen**. Nützliche Vervollständigung braucht **Werte** —
Profilnamen, Artefaktnamen, `-D`-Namen — und die stehen im Buildskript. Woher sie kommen, ohne
das Skript auszuführen, ist eine eigene Frage: CLI-37.

**Bruch:** nein. **4.x:** ab 4.7 für Flagnamen, Werte nach CLI-37.
**Hängt ab von:** CLI-10, CLI-37.

---

### CLI-15 — Templates: zwei eingebettete oder eine Maschine?

**Heute:** zwei, `app` und `lib`, als eingebettete Ressourcen (`src/Lyric.Cli/NewProject.cs:39`,
`73-87`), gewählt über `--lib`. Die Begründung steht im Kommentar (`NewProject.cs:9-11`): „mit
zwei Varianten ist ein Entdeckungsmechanismus mehr Maschinerie als Inhalt."

**Optionen:**
- **A — so lassen.** *Vorbild:* Zig (`zig init`), Elm (`elm init`). *Preis:* keine Hausvorlage,
  kein `--template cli-tool`.
- **B — `--type <name>` mit einem festen kleinen Satz.** `app`, `lib`, `tool`, `host-plugin`.
  *Vorbild:* `swift package init --type executable|library|tool|macro`. *Preis:* vier
  eingebettete Vorlagen statt zwei; die Testsuite kompiliert sie alle — das tut sie heute schon
  (`NewProject.cs:12-14`).
- **C — eine Template-Maschine mit Paketen.** *Vorbild:* `dotnet new install <paket>`. *Preis:*
  ein zweiter Paketmechanismus; ohne Paketmanager gar nicht möglich.

**Empfehlung: B.** `--lib` wird zu `--type lib` (`--lib` bleibt als Alias). C ablehnen, solange
es keinen Paketmanager gibt — und auch danach. **Auflage aus CLI-38:** die Vorlage schreibt
heute `//`-Kommentare in `lyric.json`; solange das Format nicht ausdrücklich JSONC ist, erzeugt
die Toolchain eine Datei, die ihre eigene Dokumentation verletzt.

**Bruch:** nein (`--lib` bleibt). **4.x:** ab 4.7.
**Hängt ab von:** CLI-38, Gebiet Embedding.

---

### CLI-16 — Capabilities auf der Kommandozeile

**Heute:** `--grant <liste>`. Gelesen `src/Lyric.Core/Capabilities.cs:118-137`: fünf Bits, zwei
Sammelnamen (`all`, `none`) und sechs Zweitschreibweisen (`fileAccess`, `network`,
`networkAccess`, `osAccess`, `hostAccess`, `processAccess`). Nur bei `lyrvm run` und damit bei
`lyric run` (`Program.cs:224-227`). Gemessen: `--grant host` funktioniert, steht aber nicht in
der Hilfe (F9). Kein `--deny`, keine Werte, kein Prompt.

**Optionen:**
- **A — so lassen, nur die Hilfe reparieren.** *Preis:* die CLI-Seite des Modells bleibt grob.
- **B — Werte auf den Achsen.** `--grant file=./data,net=api.example.com:443`. *Vorbild:* Deno.
  *Preis:* die Durchsetzung muss in die Laufzeit — Arbeit im Gebiet Laufzeit, nicht hier.
- **C — `--deny` zusätzlich, stärker als jedes `--grant`.** *Vorbild:* Deno. *Preis:* zwei
  Flags für eine Achse — Rule 2 will hören, warum. Antwort: `--grant all --deny net` ist
  ausdrückbar, die Aufzählung aller übrigen rät bei jeder neuen Achse falsch.
- **D — Capabilities in `lyric.json` statt auf der Kommandozeile.** *Preis:* ein Programm, das
  seine eigenen Rechte deklariert, ist keine Sandbox.

**Empfehlung: C jetzt, B als eigene Frage ans Gebiet Laufzeit.** F9 ist sofort zu reparieren.
**Auflage neu:** `--deny` ist nur dann ein ganzes Versprechen, wenn Bau- und Packpfad mitziehen
— das ist CLI-36, und ohne es deckt `--deny` einen von drei Ausführungspfaden. Und der
Aliasbestand gehört gekürzt: zwei Schreibweisen je Bit sind ein Mechanismus zu viel; `fileAccess`
und Geschwister bekommen in 4.7 eine Deprecation-Warnung (CLI-29).

**Bruch:** nein für `--deny`; minor für die Alias-Kürzung. **4.x:** F9 sofort; `--deny` ab 4.7.
**Hängt ab von:** CLI-36, CLI-29, Gebiet Laufzeit/Capabilities.

---

### CLI-17 — Darf `lyric build` fremden Code ausführen?

**Heute:** ja, mit allen Capabilities (`src/Lyrbuild/Program.cs:336`), und der Guide sagt es
offen (`16-building.md:168`: „This is code you are running"). Kein `--no-script`, keine Sandbox,
keine Vertrauensabfrage. **Und `lyric build --help` führt es ebenfalls aus** (F22, gemessen).

**Optionen:**
- **A — so lassen.** *Vorbild:* cargo (`build.rs`), Zig (`build.zig`), make/cmake. *Preis:* ein
  bekanntes Lieferkettenrisiko.
- **B — Sandbox für das Skript.** *Vorbild:* SwiftPM unter `sandbox-exec`. *Preis:* Lyrics
  Buildskripte *sollen* Dateien schreiben (`16-building.md:154-163`); eine Sandbox müsste
  Schreiben ins Projektverzeichnis erlauben und alles andere verweigern.
- **C — `--grant` auch für den Build**, Vorgabe `file,process`. *Vorbild:* Deno. *Preis:* ein
  Skript, das eine Abhängigkeit herunterlädt, braucht ein Flag in jeder CI-Zeile — genau die
  Länge, die Deno mit `deno task` löst (Korrektur 3 in §2). Deshalb CLI-35.
- **D — kein Skript, nur eine Deklaration.** *Vorbild:* Go. *Preis:* kippt M37 komplett.

**Empfehlung: C, mit der Deklarationshälfte aus CLI-35.** Die Bits sind da, die Durchsetzung ist
da, es fehlt die Vorgabe. D wäre die interessantere Diskussion, kommt aber zwei Jahre zu spät.
**Auflage neu:** die Regel muss auch für `--help` und für Completions gelten (F22, CLI-37) —
sonst ist jeder Tabulatordruck in einem fremden Checkout Codeausführung.

**Bruch:** minor — ein Skript, das heute herunterlädt, braucht künftig `--grant net`.
**4.x:** ab 4.7 Deprecation-Warnung nach CLI-29, Wirkung in 5.0.
**Hängt ab von:** CLI-29, CLI-35, CLI-37, Gebiet Build, Gebiet Laufzeit/Capabilities.

---

### CLI-18 — Toolchain-Verwaltung und Selbst-Update

**Heute:** `lyric.json` kennt `toolchain` als Mindestversion und lehnt ab, wenn die laufende
Toolchain zu alt ist (`LYR-CLI0018`). Gelesen `src/Lyric.Core/ProjectFile.cs:355-357`: die
Meldung lautet bereits `this project needs toolchain {minimum}, and this is
{ToolchainVersion.Value}` — sie nennt also **geforderte und vorhandene Version**. Es gibt keinen
Installer, kein `lyric upgrade`, keinen Versionsmultiplexer; eine Release ist ein Archiv
(`README.md:109-117`).

**Optionen:**
- **A — so lassen.** *Preis:* zwei Projekte mit verschiedenen Anforderungen auf einer Maschine
  sind Handarbeit.
- **B — `lyric upgrade`.** *Vorbild:* `deno upgrade`, `bun upgrade`, `rustup update`. *Preis:*
  Signatur-, Rechte- und Rückrollfrage.
- **C — ein Multiplexer nach rustup-Art.** *Vorbild:* rustup, `global.json` bei .NET. *Preis:*
  ein **zwölftes** Binary (nicht elftes — 1.1) und ein Installationsmodell.
- **D — nur die Diagnose verbessern.** **Korrektur:** die Hälfte, die die erste Fassung als
  „behauptet" führte, ist bereits Realität (gelesen, oben). Offen bleibt allein der Verweis auf
  die Releaseseite. *Preis:* fast null.

**Empfehlung: A + D für v5, C ausdrücklich als Nicht-Ziel notieren.** D ist damit fast ein
Nulltausch — eine URL in einer bestehenden Meldung. Ein persönliches Lernprojekt mit einem
Maintainer braucht keinen Versionsmultiplexer.

**Bruch:** nein. **4.x:** D sofort. **Exit-Code:** `LYR-CLI0018` ist heute exit 1 und wird nach
CLI-32 exit 2.
**Hängt ab von:** CLI-32, Gebiet Release/Verteilung.

---

### CLI-19 — `lyric fix`: eigenes Binary oder Verb?

**Heute:** existiert nicht. `docs/Befunde_und_Verbesserungen/PLAN.md:343` führt `lyrfix` als
Migrationswerkzeug für die 5.0-Brüche und sagt, die Ablage darunter setze es voraus;
`PLAN.md:376` nennt die Deprecations, die ohne es nicht entfernbar sind; `PLAN.md:384`: „Was
fehlt, ist das Werkzeug." *(Belegkorrektur: die erste Fassung zitierte `PLAN.md:310` und
`:326-330`; dort steht anderes. Die Datei liegt unter `docs/Befunde_und_Verbesserungen/`.)*

**Optionen:**
- **A — `lyric fix` als Verb, kein eigenes Binary.** *Vorbild:* `cargo fix`, `go fix`,
  `dotnet format`. *Preis:* der Treiber bekommt ein Verb, das etwas *schreibt*.
- **B — eigenes Binary `lyrfix`.** *Preis:* ein zwölftes Binary für eine Aufgabe mit
  Verfallsdatum.
- **C — in `lyrfmt` einbauen.** *Preis:* der Formatierer hat genau einen Vertrag.
- **D — die Diagnosen tragen die Korrektur**, LSP-Codeaktionen erledigen es im Editor, und
  `lyric fix` wendet dieselben Korrekturen auf der Kommandozeile an. *Vorbild:* Rusts
  `rustc --error-format=json` liefert `suggestions`, `cargo fix` wendet sie an — **ein**
  Mechanismus, zwei Oberflächen.

**Empfehlung: D, ausgeliefert als `lyric fix` (also A).** Der einzige Vorschlag, der keinen
zweiten Mechanismus schafft. Es verlangt, dass eine Diagnose eine maschinenlesbare Ersetzung
tragen kann — was CLI-11 ohnehin braucht. **Auflage:** `lyric fix` braucht die
stdin-Konvention aus CLI-28, sonst ist es in einem Pre-Commit-Hook nicht benutzbar.

**Bruch:** nein. **4.x:** ab 4.7 — je früher eine Uhr läuft, desto weniger Code muss `fix`
später anfassen (so argumentiert `PLAN.md:360` selbst).
**Hängt ab von:** CLI-11, CLI-28, CLI-29, Gebiet Diagnosen, Gebiet LSP.

---

### CLI-20 — Datei oder Artefaktname?

**Heute:** eine Regel, an einer Stelle (`Program.cs:205-208`): auf der Platte vorhanden oder
`.lyr`/`.lyrbc` → Datei, sonst Artefaktname. Gilt für `run`, `build` und `pack`.

**Optionen:**
- **A — so lassen.** *Vorbild:* niemand macht es so sauber; Bun rät. *Preis:* eine Datei namens
  `mktex` ohne Endung neben einem Artefakt `mktex` ist mehrdeutig — die Regel entscheidet sich
  für die Datei.
- **B — getrennte Schreibweisen.** `lyric run ./main.lyr` gegen `lyric run mktex`. *Vorbild:*
  `bun run ./file.ts`. *Preis:* `lyric run main.lyr` müsste dann ein Fehler sein — bricht jedes
  Tutorial.
- **C — ein Flag für den Zweifelsfall.** *Preis:* ein Flag, das fast nie gebraucht wird.

**Empfehlung: A + C.** Die Regel ist gut und dokumentiert (`16-building.md:244-246`); sie
braucht einen Ausweg für den Kollisionsfall, und der heißt bereits `--only` bei `build` — also
denselben Namen nehmen. **Zusammenhang neu:** dieselbe Regel entscheidet, wo die
Programmargumente anfangen (CLI-24); wer sie hier anfasst, fasst dort mit an.

**Bruch:** nein. **4.x:** ab 4.7.
**Hängt ab von:** CLI-24.

---

### CLI-21 — Die Präzedenzleiter

**Heute:** es gibt mehrere, und sie stimmen nicht überein. Werkzeugpfad: Flag > Env > gebündelt
(`Tool.cs:43-52`). Stdlib: `--stdlib` > `LYRIC_STDLIB` > daneben (`src/Lyrc/Program.cs:471`,
`src/Lyrrepl/Program.cs:97-107`). Profil: Flag > `LYRIC_PROFILE` > `debug`, **aber `lyric.json`
kommt nirgends vor** (`ProjectFile.cs:278-302`). Feldflag schlägt Profil
(`src/Lyrc/Program.cs:499-501`). Im Buildskript schlägt ein Artefaktfeld den Profilwert
(`16-building.md:104-113`).

**Optionen:**
- **A — eine Leiter, ausgeschrieben, für jede Einstellung dieselbe:** Kommandozeile >
  Umgebung > `lyric.json` > **benutzerweite Konfiguration** > eingebaute Vorgabe, und innerhalb
  der Kommandozeile: Feldflag > Profilflag. *Vorbild:* cargos dokumentierte Reihenfolge, `go
  env`. *Preis:* fünf Sprossen; `lyric.json` und die benutzerweite Datei müssen Einstellungen
  tragen dürfen, die sie heute nicht tragen (CLI-8, CLI-9 B).
- **B — Umgebung über Kommandozeile für Diagnoseschalter.** So ist es heute bei
  `LYRIC_VERIFY_IR` (es gibt gar kein Flag, `16-building.md:378-383`). *Preis:* eine Ausnahme,
  die man erklären muss — die Begründung („es muss jeden gestarteten Compiler erreichen") ist
  gut.
- **C — so lassen und dokumentieren.**

**Empfehlung: A**, mit B als benannter Ausnahme. **Geändert:** die Leiter hat jetzt **zwei**
Konfigurationssprossen, projektweit und benutzerweit, mit der Zuordnungstabelle aus CLI-9 — die
erste Fassung hatte nur `lyric.json` und lehnte die benutzerweite Ebene ab, ohne zu fragen,
wohin dann Werkzeugpfade und Farbe gehören. Die Leiter gehört in die Spezifikation (CLI-30).

**Bruch:** nein (heute gibt es die unteren Sprossen nicht). **4.x:** mit CLI-8 und CLI-9 ab 4.7.
**Hängt ab von:** CLI-8, CLI-9, CLI-30, CLI-38, Gebiet Projektdatei.

---

### CLI-22 — Was eine ausgewachsene Sprache hat und Lyric nicht einmal fragt

Fragen, die heute gar nicht gestellt werden, weil das Feature fehlt. Jede ist additiv.

| Frage | Heute | Vorbild | Empfehlung |
|---|---|---|---|
| **`lyric watch`** | gibt es nicht | `dotnet watch`, `cargo watch` (Drittprojekt), `deno --watch` | **Ja, ab 4.8.** Der LSP hat den Dateiwächter samt Debounce schon (`src/Lyrls/Program.cs:61-71`). Braucht CLI-27 (Sperre), sonst baut der Wächter gegen den Terminalbau. |
| **`lyric bench`** | gibt es nicht; `tools/Bench` ist ein C#-Projekt | `go test -bench`; `cargo bench` startet auf stable ein `harness = false`-Binary, der `#[bench]`-Harness ist nightly-only (Korrektur 7 in §2) | **Ja — und die Reihenfolge ist offen.** Das Vorbild bindet das Verb an einen Harness, nicht an ein Sprachattribut. `@Bench` zuerst zu verlangen ist eine Lyric-Entscheidung, keine übernommene. Gehört zusammen mit CLI-39 entschieden. |
| **Coverage** | gibt es nicht (`PLAN.md:346`) | `go test -cover`, `deno coverage` (LCOV), `dotnet-coverage` | **Ja, als `lyric test --coverage`** — aber **nach** CLI-39. `--coverage` an ein Werkzeug zu hängen, dessen Optionsfläche nie gefragt wurde, ist die falsche Reihenfolge. |
| **Profiler** | gibt es nicht (`PLAN.md:345`) | `go tool pprof`, `deno --inspect` | **Später.** Braucht eine Entscheidung über Sampling in der VM; Gebiet Laufzeit. |
| **`lyric doc`** | `tools/DocGen` existiert, ist aber kein Verb | `go doc`, `cargo doc`, `swift-docc` — **und `elm make --docs`, also als Compilerflag** (Korrektur 5) | **Ja, ab 4.8, als Verb.** Elms Gegenbeispiel ist ernst zu nehmen, fällt aber: Lyrics Doku-Erzeugung braucht Ausgabeort, Format und Umfang — drei Werte, die an einem `--docs`-Flag hässlich werden. |
| **`lyric explain <code>`** | gibt es nicht | `rustc --explain E0382`, Elms Fehlertexte | **Ja, ab 4.7.** Bei **19** CLI- und über hundert Sprachcodes die billigste große Verbesserung. |
| **`lyric lint`** | gibt es nicht; Warnungen kommen aus der Sema | `cargo clippy`, `deno lint`, `go vet` | **Nein.** Lyrics Warnungen sind Teil des Compilers (`19-diagnostics.md:30-34`). Ein zweiter Analysator wäre ein Parallelmechanismus — Rule 2. |
| **`lyric add` / `install` / `publish`** | gibt es nicht; `dependencies` sind lokale Pfade (`ProjectFile.cs:292-296`) | cargo, deno, bun, Elm (`elm bump`/`elm diff`) | **Ja, aber das ist das Gebiet Pakete.** Hier nur: die Verben gehören in denselben Treiber. |
| **`std.cli`** — Argumentparser für Lyric-Programme | gibt es nicht; `main(args: string[])` und mehr nicht | Gos `flag`, Rusts `clap`, Denos `parseArgs` | **Ja**, und er sollte **dieselbe Flagsyntax** sprechen wie die Toolchain (CLI-6). **Aber erst nach CLI-24**: solange ein Programm je nach Startart zwei Aufrufkonventionen hat (F18, gemessen), verschärft ein gemeinsamer Dialekt die Kollision, statt sie zu lösen. |

---

### CLI-23 — Ist eine Option phasengebunden, und wer dupliziert sie?

**Heute:** unentschieden, und das Ergebnis ist messbar kaputt. `Split`
(`src/Lyric.Cli/Program.cs:212-236`) gibt genau `--jit` und `--grant` an die Laufzeit, alles
andere an den Compiler. Gemessen (F17): `lyric run oob.lyr --json` schreibt auf stderr
`{"diagnostics":[]}panic [LYR-VM0006]: index 9 is outside an array of length 3 …` — der
JSON-Abschluss des Compilers und der Text-Panikbericht der Laufzeit ohne Trennzeichen auf
demselben Strom, exit 101. Kontrolle: `lyric run bad.lyr --json` (Compilefehler) liefert ein
sauberes Dokument.

**Optionen:**
- **A — jede Option trägt eine Phase in der gemeinsamen Optionsliste:** `compile`, `run` oder
  `beide`. Der Treiber gibt „beide"-Optionen an beide Werkzeuge. *Vorbild:* cargo gibt
  `--message-format` an `rustc` weiter **und** rendert selbst; `--color` geht an beide.
  *Preis:* die Optionsdefinition bekommt eine Spalte mehr, und ein Werkzeug, das ein
  „beide"-Flag nicht kennt, lehnt ab — also muss CLI-5 zuerst greifen.
- **B — der Treiber ist der einzige Renderer:** die Werkzeuge schreiben strukturiert, der
  Treiber formatiert. *Vorbild:* cargo gegenüber rustc. *Preis:* kehrt die Architektur um;
  `lyrvm` allein aufgerufen braucht trotzdem einen Renderer, also gäbe es zwei.
- **C — so lassen und dokumentieren**, dass `--json` bei `lyric run` nur die Compilerhälfte
  deckt. *Preis:* die Zusage „maschinenlesbar" ist genau dort falsch, wo sie am meisten
  gebraucht wird — im Lauf, der abstürzt.

**Empfehlung: A.** `--json`, `--quiet`, `--color` und `--verbose` sind phasenübergreifend und
müssen beide Hälften erfassen; `--release`/`--optimize`/`-o` sind „compile";
`--jit`/`--grant`/`--deny` sind „run". Die Panik gehört als Ereignis in den Strom (CLI-11), nicht
als Text daneben.

**Bruch:** minor — `lyric run … --json` liefert künftig ein anderes (nämlich vollständiges)
Dokument. **4.x:** F17 ist ein Bug und gehört in 4.7; die Phasenspalte mit CLI-10.
**Hängt ab von:** CLI-5, CLI-10, CLI-11.

---

### CLI-24 — Wem gehört die Kommandozeile hinter dem Ziel?

**Heute:** der Toolchain, und zwar unabhängig davon, ob die Option sinnvoll ist. Gemessen (F18):
`lyric run args.lyr --verbose` → der **Compiler** nimmt das Flag (die Phasentabelle erscheint),
das Programm sieht null Argumente, exit 0. `lyric run args.lyr --count 3` →
`error[LYR-CLI0003]: unknown option '--count' — try 'lyrc --help'` — die Meldung beschuldigt den
Compiler für ein Flag des Programms. Kontrolle: `lyric run args.lyr -- a b` → exit 2 (zwei
Argumente angekommen). Dasselbe Programm **gepackt**: `./args.exe --count 3` → exit 2, Kontrolle
`./args.exe` → exit 0 — der Stub reicht alles durch (`src/Lyrstub/Program.cs:11-13`, `:68`).
**Ein Programm hat damit zwei Aufrufkonventionen, je nachdem wie es gestartet wird.**

**Optionen:**
- **A — `--` bleibt Pflicht, und die Diagnose lernt den Fall.** Eine unbekannte Option nach dem
  Ziel nennt `--` im Vorschlag statt den Compiler zu beschuldigen. *Vorbild:* `cargo run --
  args`. *Preis:* der häufigste Aufruf braucht zwei Zeichen mehr, die jeder vergisst; die
  gepackte Form bleibt anders als die ungepackte.
- **B — alles nach dem Zielnamen gehört dem Programm.** *Vorbild:* `deno run --allow-net app.ts
  --count 3`, `node app.js --count 3`, `python x.py --count 3`. *Preis:* `lyric run x.lyr
  --release` erreicht dann das Programm statt den Compiler — bricht bestehende Zeilen und
  Tutorials.
- **C — B mit einem Bruchpunkt am Ziel:** Toolchain-Optionen **vor** dem Zielnamen,
  Programmoptionen danach, `--` bleibt als ausdrücklicher Ausweg. *Vorbild:* Deno, Node.
  *Preis:* `lyric run app.lyr --release` ändert die Bedeutung — Deprecation nötig; und
  CLI-4 B (Optionen überall) muss für `run` eine Ausnahme machen.

**Empfehlung: C.** Es ist die einzige Option, die die gepackte und die ungepackte Form gleich
macht, und das ist die eigentliche Unstimmigkeit: derselbe Quelltext, zwei Konventionen.
Der Preis — CLI-4 B gilt dann „überall außer rechts vom Ziel eines `run`" — ist eine Zeile in
der Spezifikation und deutlich billiger als zwei Konventionen.

**Bruch:** **major.** **4.x:** ab 4.7 eine Deprecation-Warnung auf **jeder** Option rechts vom
Ziel eines `run` („diese Option steht hinter dem Zielnamen; ab 5.0 gehört sie dem Programm —
stell sie davor oder trenne mit `--`"), Wirkung 5.0. Das ist die teuerste Uhr dieses Gebiets
und braucht CLI-29 zuerst.
**Hängt ab von:** CLI-4, CLI-20, CLI-23, CLI-29, CLI-33, Gebiet Stdlib (`std.cli`).

---

### CLI-25 — Was darf `lyric run` auf Hello-World kosten?

**Heute:** ungefragt. Gemessen (F26; Debug-Apphosts, neun Läufe, Minimum/Median in ms, Maschine
unter Last): `lyric --version` 270/631 · `lyrvm --version` 277/499 · `lyrvm run hello.lyrbc`
189/215 · `lyric run hello.lyr` 716/816. Ein `lyric run` startet drei .NET-Prozesse
(`src/Lyric.Cli/Program.cs:93-97`: Treiber → `lyrc build` → `lyrvm run`). Größenordnung: rund
0,2 s je Prozessstart, dreimal bezahlt. Eine exakte Aufteilung behaupte ich nicht — die
Streuung ist größer als der Unterschied.

**Optionen:**
- **A — eine Zielgröße festlegen und messen.** Ein Ratchet: `lyric run` auf Hello-World unter
  X ms in Release. *Vorbild:* keins direkt; Gos Build-Benchmarks. *Preis:* ein Ratchet mehr,
  und er ist plattform- und maschinenabhängig — also als Verhältnis formulieren
  (`lyric run` ≤ 2,5 × `lyrvm run` auf demselben Modul), nicht als absolute Millisekunde.
- **B — In-Prozess-Schnellpfad, wenn alle Werkzeuge gebündelt sind.** Der Treiber lädt
  `Lyric.Frontend` und `Lyric.Vm` selbst und startet keinen Prozess, sobald `Tool.Display` für
  beide „bundled" sagt. *Vorbild:* Deno, Bun, Zig (ein Prozess). *Preis:* der Treiber
  referenziert dann doch Compiler und Laufzeit — die Eigenschaft, die CLI-1 A verteidigt, gilt
  nur noch für das separat ausgelieferte `lyrvm`, und das ist genau die Grenze, auf die sich
  CLI-1 beruft.
- **C — so lassen.** *Preis:* jede `lyric run`-Schleife (Beispiele, Guide-Suite, Tutorials)
  zahlt den dreifachen Start; auf einem Laptop ist das der erste Eindruck der Sprache.

**Empfehlung: A jetzt, B erst wenn A rot wird** — und dann mit der ausdrücklichen Zusage, dass
`lyrvm` als eigenes Binary ohne Compilerreferenz bestehen bleibt. Ohne Zahl ist CLI-1 A eine
Architekturbehauptung ohne Preisschild; das ist genau die Form von Entscheidung, die dieses
Projekt sich abgewöhnen wollte.

**Bruch:** nein. **4.x:** die Messung ab 4.7 als Ratchet in der Suite.
**Hängt ab von:** CLI-1.

---

### CLI-26 — Gibt es einen Build-Cache, und was ist `clean` dann?

**Heute:** keiner (F19). Gemessen: zwei `lyric build` hintereinander bauen beide neu, drucken
beide dieselbe Bytezahl, keine „up to date"-Meldung. Gelesen: kein Treffer für `cache`,
`fingerprint`, `LastWriteTime`, `incremental` unter `src/Lyrbuild` und
`src/Lyric.Frontend/Compiler`. `lyric run <datei>` übersetzt in eine Temporärdatei und löscht
sie (`src/Lyric.Cli/Program.cs:86-103`). Ein Skript darf den Ausgabepfad frei setzen
(`docs/guide/16-building.md:112`: `app.output = "dist/app.lyrbc"`).

**Optionen:**
- **A — Fingerprint-Cache unter `out/.cache`.** Schlüssel: Compilerversion + Profilfelder +
  Quelldatei-Hashes + Stdlib-Version + `-D`-Werte. *Vorbild:* cargos `.fingerprint`, Gos
  Build-Cache. *Preis:* die Invalidierung ist die schwierigste Stelle jedes Buildsystems, und
  Lyric hat heute keinen Ort, an dem diese Eingaben zusammen gehasht werden. Ein falscher Cache
  ist schlimmer als keiner.
- **B — kein Cache; `clean` löscht `out/` **und** jeden Pfad, den das Skript deklariert hat.**
  *Vorbild:* `make clean` als Skriptziel. *Preis:* um die Pfade zu kennen, muss `clean` das
  Skript **ausführen** — womit ein Aufräumbefehl unter CLI-17 fällt. Ein `clean`, das fremden
  Code startet, ist die falsche Form von Aufräumen.
- **C — kein Cache; `clean` löscht `out/` und sagt das ausdrücklich.** Alles außerhalb `out/`
  gehört dem Skript, und die Hilfe sagt: „`clean` entfernt `out/`. Was ein Buildskript
  anderswohin geschrieben hat, entfernt das Skript." *Vorbild:* `zig build` hat kein `clean`
  und sagt, `zig-out/` und `.zig-cache/` seien löschbar. *Preis:* ein Projekt mit
  `app.output = "dist/…"` hat kein Aufräumkommando.

**Empfehlung: C für v5, A als eigene Frage ans Gebiet Build.** `clean` ist nur dann billig,
wenn sein Vertrag „löscht `out/`" heißt und nicht „löscht alles, was ein Bau erzeugt hat". Die
erste Fassung hat das Verb mit „drei Zeilen" begründet und dabei die Semantik übersprungen. Wenn
A später kommt, liegt der Cache unter `out/` und `clean` bleibt derselbe Satz.

**Bruch:** nein. **4.x:** `clean` ab 4.7 mit dem engen Vertrag.
**Hängt ab von:** CLI-17, CLI-27, Gebiet Build.

---

### CLI-27 — Wer sperrt `out/`?

**Heute:** niemand (F19, gelesen: kein Mutex, kein Lockfile, kein Sperr-`FileShare` im ganzen
`src`). Zwei gleichzeitige `lyric build` im selben Verzeichnis, `lyrls` neben einem
Terminalbau, `lyric test` parallel zu `lyric build` — alle schreiben dieselben Dateien.

**Optionen:**
- **A — Verzeichnissperre, der zweite Prozess wartet und sagt es.** „waiting for the build in
  `<pfad>` (pid 1234)". *Vorbild:* cargo nimmt eine Sperre auf das Zielverzeichnis und meldet
  „Blocking waiting for file lock". *Preis:* eine verwaiste Sperre nach einem Absturz braucht
  einen Ausweg (Zeitlimit plus `--ignore-lock`), und ein Wartebalken in CI sieht aus wie ein
  Hänger.
- **B — Sperre, der zweite Prozess bricht mit eigener Diagnose ab** (neuer `LYR-CLI`-Code).
  *Vorbild:* keins Bekanntes. *Preis:* eine CI, die parallel testet und baut, scheitert, statt
  zu warten — und das ist der Normalfall, nicht der Fehlerfall.
- **C — so lassen.** *Preis:* der Fehler ist selten, nicht reproduzierbar und dann unerklärlich
  — die teuerste Sorte.

**Empfehlung: A**, mit Zeitlimit, mit dem Namen des haltenden Prozesses in der Meldung und mit
`--no-lock` für den Fall, dass die Sperre kaputt ist. Es ist die Form, die cargo nach genau
diesem Problem gewählt hat. Nötig spätestens mit `lyric watch` (CLI-22).

**Bruch:** nein. **4.x:** ab 4.8.
**Hängt ab von:** CLI-26, Gebiet Build.

---

### CLI-28 — Eine Eingabekonvention für stdin

**Heute:** eine Konvention, in einem Werkzeug (F24). Gemessen: `lyrfmt --stdin` formatiert und
funktioniert auch über den Treiber (`lyric fmt --stdin`); `lyrc check --stdin` →
`unknown option '--stdin'`; `lyrc build -` → `error[LYR-CLI0001]: failed to read file: -`,
Kontrolle `lyrc build nosuch.lyr` liefert dieselbe Form. Das Dossier hat `--stdin` in seiner
ersten Fassung nicht einmal erwähnt, obwohl es bestehende CLI-Fläche ist.

**Optionen:**
- **A — `--stdin` in jedem Werkzeug, das eine Quelle liest.** *Vorbild:* `prettier
  --stdin-filepath`, `rustfmt --emit`. *Preis:* ein Flag mehr im Pflichtkern, und es kollidiert
  mit Werkzeugen, die mehrere Dateien nehmen (`lyrfmt src/`).
- **B — `-` als Dateiname.** *Vorbild:* POSIX, `gofmt -`, `cat -`, `black -`. *Preis:* eine
  Datei, die wirklich `-` heißt, ist nicht mehr erreichbar (Ausweg: `./-`) — ein Preis, den
  jedes Unix-Werkzeug seit vierzig Jahren zahlt.
- **C — beides.** *Preis:* zwei Schreibweisen für eine Sache, Rule 2.

**Empfehlung: B**, weil es keine neue Option ist, sondern eine Bedeutung für ein Argument, das
heute garantiert scheitert (gemessen). `--stdin` bleibt bei `lyrfmt` als Alias und bekommt in
4.7 eine Deprecation-Warnung (CLI-29). Zwei Auflagen: die Quelle heißt in der Diagnose
`<stdin>`, und `--stdin-name <pfad>` setzt einen anderen Namen — das braucht jeder Editor und
jeder Pre-Commit-Hook, der Pfade in der Ausgabe erwartet. Betroffen sind `lyrc check`,
`lyrc build`, `lyrfmt` und künftig `lyric fix` (CLI-19).

**Bruch:** minor — heute ist `-` ein Fehler, künftig eine Quelle. **4.x:** ab 4.7.
**Hängt ab von:** CLI-5, CLI-7, CLI-19, CLI-29.

---

### CLI-29 — Wie sieht eine CLI-Deprecation konkret aus?

**Heute:** es gibt keine. Gelesen `src/Lyric.Core/CliDiagnostics.cs:14-101`: neunzehn lebende
Codes, keiner heißt „veraltete Aufrufform". Und der Mechanismus, auf dem eine Deprecation
säße, ist kaputt: eine Warnung desselben Programms zerstört heute den `--json`-Strom (F16,
gemessen). `--deny-warnings` existiert nur im Compiler (`src/Lyrc/Program.cs:137`) und macht
Compilerwarnungen zu Fehlern (`LYR-CLI0016`).

**Dieses Dossier schreibt an acht Stellen „ab 4.7 Warnung, Wirkung in 5.0". Ohne diese Frage ist
jede dieser acht Zeilen ein Versprechen ohne Deckung.**

**Optionen:**
- **A — ein eigener Code mit eigener Klasse.** `LYR-CLI0021 deprecated invocation`, auf stderr,
  im Maschinenstrom als eigenes `kind`, **einmal je Vorkommen** (nicht je Prozess: wer zwei
  veraltete Flags benutzt, will beide hören), `--quiet` unterdrückt sie **nicht**,
  `--deny-warnings` macht sie **nicht** zum Fehler, Ausschalter `LYRIC_NO_DEPRECATIONS=1` für
  CI-Läufe, die es schon wissen. *Vorbild:* Node (`--no-deprecation`, `--throw-deprecation`),
  Python (`DeprecationWarning`, standardmäßig sichtbar nur im `__main__`). *Preis:* eine
  Warnklasse, die `--deny-warnings` entkommt, ist eine Ausnahme im Warnmodell und muss erklärt
  werden.
- **B — wie A, aber `--deny-warnings` erfasst sie.** *Vorbild:* Rusts `#[deprecated]` unter
  `-D warnings`. *Preis:* jede CI mit `--deny-warnings`, die eine abgekündigte Form benutzt,
  wird mit 4.7 rot — in genau dem Release, das den Umstieg leicht machen soll. Das ist der Bruch,
  den die Deprecation vermeiden wollte, nur früher.
- **C — nur im Guide und im CHANGELOG ankündigen, keine Laufzeitwarnung.** *Vorbild:* Gos
  Release Notes. *Preis:* die Uhr läuft, aber niemand hört sie; 5.0 bricht dann für jeden, der
  keine Release Notes liest.

**Empfehlung: A**, mit drei Auflagen:

1. **F16 zuerst.** Solange eine Warnung den `--json`-Strom zerbricht, kann eine Deprecation
   nicht in ihn hinein. Reihenfolge: F16 reparieren → `LYR-CLI0021` anlegen → erste Uhr stellen.
2. **`--quiet` unterdrückt sie nicht.** Eine Abkündigung, die man mit einem Flag wegdrückt, das
   man aus anderen Gründen setzt, ist keine.
3. **`--deny-warnings` erfasst sie nicht**, und das steht in der Spezifikation (CLI-30). Wer
   Deprecations doch als Fehler will, bekommt `--deny-deprecations` — ausdrücklich, nicht
   nebenbei.

Die teuerste Uhr ist CLI-24 (jede Option rechts vom Ziel eines `run`); sie ist der Testfall für
den Mechanismus.

**Bruch:** nein (additiv). **4.x:** Code und Mechanismus in 4.7, **vor** jeder einzelnen
Deprecation.
**Hängt ab von:** CLI-11, CLI-30, Gebiet Diagnosen.

---

### CLI-30 — Ist die CLI ein versionierter Vertrag?

**Heute:** nein, und das ist ungesagt. Der Arbeitsmodus ist spec-first, die Sprache hat
`docs/Grammar.md` als Vertrag und ein eigenes Spec-Repo als Norm; die CLI steht nur im Guide
(`docs/guide/16-building.md`) und in `README.md`. Zugleich verlangt
`src/Lyric.Core/CliDiagnostics.cs:9` Stabilität für die Codes („a number is not reused when a
case disappears") — eine Vertragszusage ohne Vertrag. CLI-5 und CLI-21 sagen nebenbei „gehört
in die Spezifikation", ohne die Dachfrage zu stellen.

**Ohne diesen Maßstab sind alle Bruchurteile dieses Dossiers unüberprüfbar.**

**Optionen:**
- **A — ein CLI-Kapitel in der Spezifikation, mit `since:`-Gates.** Versioniert: Verbnamen,
  Flagnamen und ihre Bedeutung, Exit-Codes und ihre Zuordnung, `LYR-CLI`-Codes, der
  Pflichtoptionskern, das Maschinenformat samt Schemaversion, die Präzedenzleiter, die Regel
  für `--` und stdin. **Ausdrücklich nicht versioniert:** Hilfetextwortlaut, Farbgebung,
  Reihenfolge und Wortlaut menschenlesbarer Ausgaben, Fortschrittsdarstellung, Zeitangaben.
  *Vorbild:* POSIX Utility Syntax Guidelines als Vorlage; Rusts Trennung zwischen stabilen und
  instabilen `rustc`-Flags. *Preis:* jedes neue Flag braucht ein Gate und einen Spec-PR —
  spec-first gilt dann auch hier, mit allem, was das an Reihenfolge erzwingt.
- **B — eine Liste im Guide, ausdrücklich unverbindlich.** *Preis:* die Urteile „Bruch:
  nein/minor" in diesem Dossier bleiben Meinung.
- **C — nur Exit-Codes und `LYR-CLI`-Codes normativ, der Rest frei.** *Preis:* Verbnamen sind
  das, worauf jedes Skript zuerst trifft; sie freizugeben heißt, den häufigsten Bruch
  freizugeben.

**Empfehlung: A.** Die wichtigste Zeile des Kapitels ist die **Grenze**: was versioniert ist und
was nicht. Sie muss vor der ersten Deprecation stehen, sonst läuft eine Uhr auf eine Zusage, die
niemand gegeben hat.

**Bruch:** nein. **4.x:** das Kapitel vor 4.7, also vor CLI-29.
**Hängt ab von:** Gebiet Spezifikation. **Macht überprüfbar:** CLI-1 bis CLI-39.

---

### CLI-31 — Versionskohärenz zwischen Treiber und Werkzeug

**Heute:** keine (F25). Gelesen `src/Lyric.Core/Tool.cs:55-59`: `Display` liefert „bundled" oder
den Pfad, nie die Version dahinter; `Tool.Run` (`:68-102`) startet, was der Pfad hergibt.
Gemessen: `lyric --version` druckt `lyric 4.6.0` und darunter siebenmal „bundled". Ein
4.6-Treiber mit `LYRIC_VM` auf einem 4.2-`lyrvm` ist vollkommen stumm — obwohl `LYRIC_*`-
Werkzeugpfade ein dokumentierter Mechanismus sind (`Tool.cs:14-36`).

**Optionen:**
- **A — der Treiber fragt jedes extern gesetzte Werkzeug einmal nach `--version` und warnt bei
  Abweichung.** *Vorbild:* keins direkt; `git` mit fremdem `--exec-path` ist ebenso stumm.
  *Preis:* ein Prozessstart je gesetzter Variable — bei gemessenen ~0,2 s je Start (F26) ist das
  spürbar, und bei `lyric run` käme es zu drei Starts dazu.
- **B — nur bei `lyric --version` fragen** und dort die echten Versionen statt „bundled"
  drucken. *Vorbild:* `dotnet --info`. *Preis:* die Selbstauskunft wird langsam; der Normalfall
  bleibt stumm.
- **C — Abweichung ist ein Fehler.** *Preis:* eine bewusste Mischung (ein Werkzeug aus einem
  Branch testen) wird unmöglich — und genau dafür gibt es die Variablen.

**Empfehlung: B als Vorgabe, A nur für Werkzeuge, die per Flag oder Variable gesetzt wurden.**
Gebündelte Werkzeuge fragt niemand: sie kommen aus demselben Build und können nicht abweichen.
Damit kostet die Prüfung im Normalfall **nichts** und deckt genau den Fall, der heute stumm ist.
Bei Abweichung eine Warnung, kein Fehler — mit der Versionsnummer beider Seiten, wie
`LYR-CLI0018` es schon vormacht.

**Bruch:** nein. **4.x:** ab 4.7 (B); A mit CLI-1.
**Hängt ab von:** CLI-1, CLI-9, CLI-25.

---

### CLI-32 — Eine Leiter für Exit-Codes, nicht ein Paar

**Heute:** vier Codes (`src/Lyric.Core/ExitCodes.cs:15-26`), und die Zuordnung ist nicht
durchgehalten (F15, gemessen). Drei gleichartige Fälle, zwei Ergebnisse:

| Fall | Code | Exit |
|---|---|---|
| `lyrbuild --only nichtda` | `LYR-CLI0019` | **2** |
| `lyrvm disasm --function nichtda` | `LYR-CLI0009` | **1** |
| `lyric --vm C:/nope/lyrvm.exe run x.lyr` | `LYR-CLI0005` | **1** |

Alle drei sind „du hast einen Namen genannt, den es nicht gibt". Dazu gelesen: `LYR-CLI0015`
(Stub nicht gefunden) und `LYR-CLI0018` (Toolchain zu alt) sind heute ebenfalls exit 1, obwohl
beides die Umgebung des Aufrufs betrifft und nicht die Eingabe.

**Optionen:**
- **A — zwei Klassen, scharf gezogen.** **2 = die Kommandozeile oder die Umgebung ist falsch**:
  unbekanntes Verb, unbekannte Option, fehlender Wert, unbekannter Name (`0003`, `0002`, `0009`,
  `0019`), Werkzeug nicht gefunden (`0005`, `0006`), Stub nicht gefunden (`0015`), Toolchain zu
  alt (`0018`), Projektdatei kaputt (`0010`). **1 = die Eingabe wurde verstanden und
  abgelehnt**: Compilefehler, kaputtes Modul (`0014`), Datei unlesbar (`0001`), Ausgabe
  unschreibbar (`0008`), `--deny-warnings` (`0016`), interner Fehler (`0020`). *Vorbild:* die
  Zweiteilung von `sysexits` auf zwei Stufen; cargo (2 für Kommandozeilenfehler).
  *Preis:* `0005`, `0009`, `0015`, `0018` wechseln von 1 auf 2 — jedes Skript, das auf `-eq 1`
  prüft, sieht etwas anderes.
- **B — mehr Stufen** (`sysexits.h`: 64 usage, 66 noinput, 70 software, 78 config). *Vorbild:*
  BSD. *Preis:* niemand kennt sie, und `& 0xFF` teilt den Raum bereits mit `main`s Rückgabewert
  (CLI-12).
- **C — so lassen.** *Preis:* „Fehlbedienung" heißt dann nichts, und F15 bleibt.

**Empfehlung: A**, und die vollständige Tabelle Code → Exit gehört in die CLI-Spezifikation
(CLI-30), sonst driftet sie wieder. Die drei Konfigurationsfälle (`0005`, `0015`, `0018`) sind
Fehlbedienung: es ist die Umgebung des Aufrufs, nicht das Programm. **Das ist die Korrektur an
F15**, die die erste Fassung nur für `0019`/`0009` gesehen hat — sie ist größer als zwei Codes.

**Bruch:** minor. Ein Exit-Code lässt sich nicht warnen, nur ankündigen: vier Codes wechseln,
jeder davon druckt heute schon seine Kennung, ein Skript kann also vorher auf die Kennung
umstellen. **4.x:** ab 4.7 im CHANGELOG und in der Spezifikation ankündigen, Wirkung 5.0.
**Hängt ab von:** CLI-30.

---

### CLI-33 — Eine Regel für `--`

**Heute:** fünf Bedeutungen (F20, gemessen):

| Aufruf | Ergebnis |
|---|---|
| `lyric run x.lyr -- a b` | reicht an das Programm weiter (Kontrolle: exit 2 bei zwei Argumenten) |
| `lyric pack hello.lyr --` | `LYR-CLI0003: pack: '--' has no place here …`, exit 2 (`src/Lyric.Cli/Program.cs:258-262`) |
| `lyric check -- hello.lyr` | `LYR-CLI0003: unknown option '--'`, exit 2 |
| `lyric fmt -- hello.lyr` | `LYR-CLI0001: no such file or directory: --`, exit 1 |
| `lyric build -- hello.lyr` | `LYR-CLI0003: unexpected argument 'hello.lyr' — build takes one file` |

Zugleich hält `ToolOptions.Parse` `--` bereits ein (`src/Lyric.Core/ToolOptions.cs:53`: alles ab
`--` bleibt unangetastet). CLI-4 B stützte sich in der ersten Fassung ausdrücklich darauf, dass
„die Grenze `--` bleibt" — sie existiert heute nur bei `run`.

**Optionen:**
- **A — POSIX überall:** `--` beendet die Optionen, alles danach ist positional. Bei `run` heißt
  „positional nach dem Ziel" = Programmargumente. *Vorbild:* POSIX, git, cargo. *Preis:*
  `lyric pack x.lyr -- y` wäre dann „zwei Dateien" statt eines Fehlers, und die heutige
  erklärende Meldung fiele weg.
- **B — `--` nur bei `run`, überall sonst ein Fehler** mit der `pack`-Meldung, verallgemeinert.
  *Vorbild:* keins; es ist die heutige `pack`-Regel. *Preis:* bricht die POSIX-Erwartung für
  Dateinamen, die mit `-` anfangen — und die braucht man, sobald `-` für stdin steht (CLI-28).
- **C — A, und die `pack`-Meldung bleibt als benannter Sonderfall.** *Preis:* eine Ausnahme im
  Wortlaut der Regel.

**Empfehlung: C.** POSIX als Regel — sie ist die einzige, die niemand nachschlagen muss — und
die eine gute Meldung, die es schon gibt, bleibt: sie erklärt eine echte Verwechslung („ein
gepacktes Programm bekommt seine Argumente beim Laufen"), statt nur abzulehnen. Mit CLI-24 C
wird `--` bei `run` vom Pflichttrenner zum ausdrücklichen Ausweg.

**Bruch:** minor — drei Verben nehmen `--` künftig an, wo sie heute ablehnen. **4.x:** ab 4.7.
**Hängt ab von:** CLI-4, CLI-24, CLI-28, CLI-30.

---

### CLI-34 — Darf `lyric run` in einem Projekt leise sein?

**Heute:** nein (F21, gemessen). `lyric run` in einem Projekt druckt vor der Programmausgabe
`…\out\debug\demo2.lyrbc: 2832 bytes` auf **stderr** — Kontrolle: mit `2>/dev/null` bleibt nur
die Programmausgabe auf stdout. Und `lyric run --quiet` → `LYR-CLI0003: unknown argument:
--quiet`, exit 2. Auf dem Datei-Pfad löst der Treiber es selbst, indem er `--quiet` anhängt
(`src/Lyric.Cli/Program.cs:94`); der Projektpfad hat diesen Griff nicht (`:147-148` übergibt nur
`--print-path` und die Buildoptionen). Die erste Fassung nannte das Symptom (F2), aber nicht die
Anforderung.

**Optionen:**
- **A — der Treiber hängt bei `run` auch auf dem Projektpfad `--quiet` an**, sobald `lyrbuild`
  es kennt (CLI-5). *Vorbild:* `cargo run` druckt `Compiling…`/`Running…` auf stderr, `-q`
  schaltet es ab. *Preis:* keiner; es ist die Regel, die der Datei-Pfad schon hat.
- **B — Baumeldungen bei `run` grundsätzlich weglassen.** *Vorbild:* `deno run` sagt nichts.
  *Preis:* ein langer Bau sieht aus wie ein hängendes Programm.
- **C — so lassen.** *Preis:* `lyric run` ist in einer Pipeline unbrauchbar, sobald jemand
  stderr mitliest — und in CI liest jeder stderr mit.

**Empfehlung: A**, und die Anforderung ausschreiben statt sie zu unterstellen:

> **stdout eines `lyric run` trägt ausschließlich, was das Programm schreibt.** Alles, was die
> Toolchain sagt, geht auf stderr. `-q`/`--quiet` entfernt zusätzlich die Erfolgsmeldungen der
> Toolchain von stderr; Diagnosen bleiben.

Der erste Satz gilt heute schon (gemessen) und ist nirgends zugesagt — er gehört in die
Spezifikation (CLI-30). Der zweite ist die Reparatur.

**Bruch:** nein. **4.x:** mit CLI-5 ab 4.7.
**Hängt ab von:** CLI-5, CLI-30.

---

### CLI-35 — Wie deklariert ein Buildskript seinen Rechtebedarf?

**Heute:** gar nicht. Gelesen `src/Lyrbuild/Program.cs:336`: `Capabilities = Capability.All`;
`docs/guide/16-building.md:168`: „This is code you are running." `lyric.json` kennt keinen
Rechte-Schlüssel (`src/Lyric.Core/ProjectFile.cs:278-302`). CLI-17 empfiehlt Vorgabe
`file,process` und `--grant net` von Hand — ohne zu fragen, wer die Anforderung trägt.

**Optionen:**
- **A — nur die Kommandozeile.** Vorgabe `file,process`, mehr auf ausdrückliche Bitte.
  *Vorbild:* Deno für Programme. *Preis:* jede CI-Zeile eines Projekts, das im Bau
  herunterlädt, wiederholt das Flag — genau die Länge, die Deno mit `deno task` löst
  (Korrektur 3 in §2).
- **B — das Skript oder `lyric.json` deklariert den Bedarf**, die Toolchain gewährt nicht mehr
  als das Deklarierte. *Vorbild:* SwiftPM-Plugins deklarieren `permissions:` im Manifest;
  `deno.json`-Tasks backen die Flags in einen benannten Aufruf ein. *Preis:* ein Programm, das
  seine eigenen Rechte deklariert, ist **keine** Sandbox — es sei denn, die Deklaration wird
  bestätigt.
- **C — B plus eine Vertrauensfrage einmal je Checkout**, gespeichert außerhalb des Repos.
  *Vorbild:* VS Codes „Do you trust the authors", SwiftPMs Bestätigung. *Preis:* ein neuer
  Zustandsort auf der Maschine; auf CI braucht es einen Schalter (`--trust`, `LYRIC_TRUST=1`),
  der die Frage aushebelt.

**Empfehlung: B für die Deklaration, A für die Durchsetzung, C ablehnen.** Konkret: `build.lyr`
(oder `lyric.json`) deklariert `"buildCapabilities": ["file", "process", "net"]`; der Bau
gewährt **den Schnitt** aus Deklaration und dem, was die Kommandozeile bestätigt; die Vorgabe
der Kommandozeile ist `file,process`. Ein Skript, das mehr benutzt, als es deklariert hat,
scheitert mit der Zeile, die es verlangt hat — das ist die nützliche Diagnose. C fällt, weil
eine Vertrauensfrage, die auf CI einen Schalter braucht, auf CI immer gesetzt wird und dann nur
den schützt, der ohnehin hinschaut.

**Bruch:** minor (mit CLI-17: ein Skript, das heute herunterlädt, braucht künftig Deklaration
und Bestätigung). **4.x:** Deklarationsschlüssel ab 4.7 (wird zunächst nur gelesen und
gemeldet), Durchsetzung in 5.0.
**Hängt ab von:** CLI-17, CLI-29, CLI-38, Gebiet Build, Gebiet Projektdatei.

---

### CLI-36 — `--grant`/`--deny` deckt einen von drei Ausführungspfaden

**Heute:** gelesen, drei Pfade, eine Maske.

| Pfad | Capabilities | Beleg |
|---|---|---|
| `lyrvm run` | respektiert `--grant` | `src/Lyrvm/Program.cs:66-67`, `Capabilities.cs:124-134` |
| `lyrbuild` (Buildskript) | `Capability.All`, immer | `src/Lyrbuild/Program.cs:336` |
| gepacktes Programm | `Capability.All`, immer | `src/Lyrstub/Program.cs:68`, `docs/guide/17-packaging.md:60` |

Der Kommentar im Stub (`src/Lyrstub/Program.cs:15-18`) sagt ausdrücklich, die Einschränkung
gehöre in ein **Footer-Feld**, nicht in ein Laufzeitflag, „das ein Endbenutzer wegeditieren
könnte". CLI-16 und CLI-17 standen in der ersten Fassung nebeneinander, ohne dass jemand die
Frage stellt.

**Optionen:**
- **A — `lyric pack --grant <liste>` schreibt eine Maske in den Footer**, der Stub schneidet
  damit. *Vorbild:* `deno compile --allow-net` bäckt die Rechte ein. *Preis:* eine Änderung am
  Packformat, also `docs/Pack.md`, ein Footer-Versionsfeld und ein Formatgate; alte Stubs
  ignorieren ein neues Feld nicht von selbst.
- **B — so lassen und im Guide sagen, dass `--deny` nur den `lyrvm`-Pfad deckt.** *Preis:*
  „Capability-Modell als Host-Grenze" ist dann eine Zusage mit zwei Löchern, und die
  Charakterliste verkauft genau diese Eigenschaft.
- **C — Bau- und Packpfad prüfen, ohne Footer:** der Packer weigert sich, ein Programm zu
  packen, das mehr verlangt, als der Packende gewährt. *Preis:* zur Laufzeit nicht
  durchsetzbar, weil der Stub nichts weiß — es ist eine Zusage über den Packvorgang, nicht über
  den Lauf.

**Empfehlung: A für den Packpfad, CLI-17 + CLI-35 für den Baupfad.** Der Kommentar im Quelltext
beschreibt die richtige Lösung bereits; sie ist nie gebaut worden. Ohne beide Hälften ist
`--deny` ein Drittel eines Versprechens.

**Bruch:** nein (additiv: ohne Maske gilt weiter `all`). **4.x:** ab 4.8, weil es das Packformat
berührt und damit ein Spec-Gate braucht.
**Hängt ab von:** CLI-16, CLI-17, CLI-35, Gebiet Pack/Format, Gebiet Laufzeit/Capabilities.

---

### CLI-37 — Woher kommen Wertvervollständigungen, ohne fremden Code auszuführen?

**Heute:** nur über den teuren Weg. Gemessen (F22): `lyric build --help` endet mit `build.lyr
declares no options.` (`src/Lyrbuild/Program.cs:473`) — die Hilfe hat das Skript **ausgeführt**,
um das zu wissen. Profilnamen sind fest (`Profile.cs:51-56`); Artefaktnamen und `-D`-Namen
stehen nur im Skript. Zusammen mit CLI-17 heißt das: **jeder Tabulatordruck in einem fremden
Checkout wäre Codeausführung.**

**Optionen:**
- **A — Completions liefern nur Flagnamen und feste Wertmengen** (Profile, `--progress`-Modi,
  Shellnamen); alles Skriptabhängige bleibt leer. *Vorbild:* die meisten `clap`-Completions.
  *Preis:* die nützlichste Vervollständigung — Artefaktnamen — fehlt.
- **B — ein Cache:** der letzte erfolgreiche Bau schreibt Artefaktnamen, Optionsnamen und
  Profilnamen nach `out/.meta.json`; die Completion liest ihn und führt nichts aus. *Vorbild:*
  keins direkt; am nächsten kommt, wie Editoren cargos `--message-format`-Ausgabe zwischenlagern.
  *Preis:* veraltet, sobald sich das Skript ändert; ein leerer Cache heißt „noch nie gebaut",
  und das ist genau der Moment, in dem ein Neuling Tab drückt.
- **C — das Skript deklariert Artefakte und Optionen zusätzlich in `lyric.json`.** *Vorbild:*
  `package.json`-Skripte. *Preis:* zwei Wahrheitsorte für dieselbe Liste — Rule 2, und `build.lyr`
  kann Artefakte berechnen, `lyric.json` nicht.

**Empfehlung: A + B**, mit einer ausgeschriebenen Regel:

> **Eine Completion führt niemals ein Buildskript aus.** Was sie nicht aus einer festen Menge
> oder aus `out/.meta.json` weiß, vervollständigt sie nicht.

Das schließt zugleich die Lücke, die CLI-3 C und CLI-17 sonst gegeneinander aufreißen:
`--help` darf das Skript ausführen (der Mensch hat es angefordert), eine Completion nicht (sie
läuft auf Tastendruck).

**Bruch:** nein. **4.x:** A mit CLI-14 ab 4.7, B ab 4.8.
**Hängt ab von:** CLI-3, CLI-10, CLI-14, CLI-17.

---

### CLI-38 — Ist `lyric.json` JSON oder JSONC?

**Heute:** JSONC, ohne dass es irgendwo steht (F23). Gelesen
`src/Lyric.Core/ProjectFile.cs:245-254`: `CommentHandling = JsonCommentHandling.Skip`,
`AllowTrailingCommas = true`. Gemessen: die von `lyric new` erzeugte Datei enthält
`//`-Kommentare und wird anstandslos gelesen. Im Dossier kam das Format in seiner ersten Fassung
nirgends vor, obwohl CLI-8 B und CLI-21 A beide neue Schlüssel dort hineinlegen wollen.

**Optionen:**
- **A — JSONC normativ erklären und benennen.** Dateiendung bleibt `.json`, das Kapitel sagt:
  Kommentare und nachlaufende Kommata sind erlaubt. Dazu ein JSON-Schema fürs Editor-
  Autocomplete. *Vorbild:* `tsconfig.json`, `.vscode/*.json`, `deno.jsonc`. *Preis:* `jq`,
  CI-Linter und Editoren, die strenges JSON erwarten, scheitern an einer Datei, die die
  Toolchain selbst erzeugt — und der Dateiname warnt sie nicht.
- **B — strenges JSON**, Kommentare raus, `lyric new` schreibt sie nicht mehr. *Vorbild:*
  `package.json`. *Preis:* die erklärenden Kommentare in der Vorlage sind gute Doku und fielen
  weg; bestehende Projekte mit Kommentaren brechen.
- **C — TOML statt JSON.** *Vorbild:* cargo. *Preis:* ein Formatwechsel für jede bestehende
  Datei, und `lyric.json` ist erst seit M37 in v2.

**Empfehlung: A**, mit zwei Auflagen: (1) der Name sagt es — das Spezifikationskapitel heißt
„`lyric.json` ist JSONC" und nennt die beiden Abweichungen ausdrücklich; (2) externe Verbraucher
bekommen einen Weg, der nicht parst, sondern fragt: `lyric env --json` bzw. ein künftiges
`lyric project --json` druckt die **wirksame** Konfiguration als striktes JSON. Damit muss
niemand `jq` auf eine Datei werfen, die es nicht lesen kann.

**Bruch:** nein (es beschreibt den Ist-Zustand). **4.x:** ab 4.7 dokumentieren; das
Schema mit CLI-8.
**Hängt ab von:** CLI-8, CLI-9, CLI-21, Gebiet Projektdatei.

---

### CLI-39 — Was ist der Optionsvertrag von `lyrtest`?

**Heute:** fünf Optionen. Gelesen `src/Lyrtest/Program.cs:31-66`: `--filter` (`:35`), `--stdlib`
(`:44`), `--profile` (`:47`), `--release` (`:54`), `--debug` (`:57`), dazu `--version`/`--help`.
Alle drei wertnehmenden tragen den F6-Defekt (`case "--x" when i + 1 < args.Length` fällt bei
fehlendem Wert in `default`); gemessen für `--filter`, gelesen für die beiden anderen. Es fehlen:
`--fail-fast`, Parallelität, Reihenfolge/Seed, `--list`, Wiederholung, Maschinenprotokoll — und
`--json` wird zwar nicht geparst, aber CLI-22 hängt `--coverage` an dieses Werkzeug an, ohne
dass seine Optionsfläche je gefragt wurde.

**Optionen:**
- **A — der volle CI-Kern sofort:** `--list`, `--fail-fast`, `--jobs <n>`, `--seed <n>`,
  `--json` (Ereignisstrom aus CLI-11). *Vorbild:* `go test` (`-run`, `-failfast`, `-parallel`,
  `-shuffle`, `-json`, `-list`), `cargo test`. *Preis:* `--jobs` verlangt eine Antwort auf
  Testisolation, und die ist offen — Tests in derselben Datei teilen sich heute Zustand.
- **B — der beobachtende Kern zuerst:** `--list` und `--json`, alles andere später. *Vorbild:*
  `go test -list`. *Preis:* eine CI ohne `--fail-fast` zahlt jeden Lauf voll.
- **C — so lassen und `--coverage` anhängen.** *Preis:* ein Werkzeug mit sechs Optionen, von
  denen die neue die teuerste ist.

**Empfehlung: B jetzt, A ohne `--jobs` in 4.8, `--jobs` erst nach der Isolationsfrage,
`--coverage` zuletzt.** Die Reihenfolge ist der Punkt: erst der Vertrag, dann die Anbauten.
F6 gehört unabhängig davon sofort repariert — `lyrbuild` hat mit `Value(args, ref i, …)`
(`src/Lyrbuild/Program.cs:188-190`) die richtige Form bereits im selben Repository.

**Bruch:** nein (additiv). **4.x:** F6 sofort; `--list`/`--json` ab 4.7.
**Hängt ab von:** CLI-5, CLI-7, CLI-11, Gebiet Testen.

---

## 4. Was wir übernehmen sollten

Nach Gewicht sortiert, mit der Quelle. Die Reihenfolge ist gegenüber der ersten Fassung
geändert: zwei Punkte sind **Voraussetzungen**, nicht Verbesserungen, und stehen deshalb oben.

0. **Eine CLI-Spezifikation** (CLI-30). Kein Vorbild übernommen, sondern der eigene Arbeitsmodus
   angewandt: spec-first gilt für die Sprache, die CLI hat dieselbe Vertragsfläche. Ohne sie ist
   jedes „Bruch: nein" in diesem Dossier unüberprüfbar. Muss vor der ersten Uhr stehen.
0b. **Ein Deprecation-Mechanismus für die CLI** (CLI-29), Node/Python als Vorbild. Dieses
   Dossier stellt neun Uhren; keine davon hat heute einen Code, einen Strom oder eine Regel für
   `--deny-warnings`. Und F16 muss davor repariert sein, sonst kann eine Abkündigung nicht in
   den Maschinenstrom.
1. **Gos `go env` (ein Verb, `-w`/`-u` als Flags)** → `lyric env` plus benutzerweite Vorgabe.
   Löst F11 vollständig, macht die Präzedenzleiter (CLI-21) beobachtbar statt behauptet und
   gibt Werkzeugpfaden und Farbe einen Ort, der nicht im Repository liegt.
2. **Cargos einheitlicher Optionskern** → `ToolOptions` wird Pflicht für jedes Binary, und die
   Optionsdefinition wandert an einen Ort mit **Phasenspalte**, aus dem Treiber, Hilfe und
   Completions generiert werden. Schließt F2, F5, F12, F17 strukturell. **Pflicht heißt
   befolgen, nicht parsen** — heute nehmen drei Werkzeuge `--json` an und tun nichts.
3. **Cargos `--message-format` / Gos `go test -json`** → ein Ereignisstrom, JSONL, mit
   Schemaversion, über alle Werkzeuge — **mit Übergangsform**, weil es ein Formatwechsel ist und
   kein Zusatz (CLI-11).
4. **Denos `--deny-*`** → `--deny` neben `--grant`, stärker als jedes Grant. **Nur zusammen mit
   CLI-36**: heute deckt die Maske einen von drei Ausführungspfaden.
5. **Rusts `--explain` plus `cargo fix` über Diagnose-Ersetzungen** → `lyric explain <code>` und
   `lyric fix`. Die Korrektur gehört zur Diagnose; Editor und CLI sind zwei Oberflächen davon.
   Das ist gleichzeitig die Antwort auf `lyrfix`.
6. **Cargos benannte Profile mit `inherits`** → `profiles` in `lyric.json` — **nachdem** der
   Feld-Override-Satz auf dem Projektpfad überhaupt existiert und die Negation symmetrisch ist
   (CLI-8, beides gemessen offen).
7. **SwiftPMs Manifest-Deklaration plus Denos benannte Aufrufe** → ein Buildskript deklariert
   seinen Rechtebedarf, die Kommandozeile bestätigt ihn, der Bau gewährt den Schnitt (CLI-35).
   Ersetzt die erste Fassung, die nur „Vorgabe `file,process`" sagte und die Wiederholung in
   jeder CI-Zeile nicht bedacht hat.
8. **Cargos Sperre auf das Zielverzeichnis** → `out/` bekommt eine Sperre, der zweite Prozess
   wartet und sagt, wer hält (CLI-27). Neu gegenüber der ersten Fassung.
9. **Swifts `-Xswiftc`** → `-Xc`/`-Xvm` als Notausgang neben der Routing-Tabelle.
10. **Zigs `zig build --help` führt das Skript aus** → **schon übernommen**, richtig, bleibt —
    mit der Auflage aus CLI-37, dass **Completions** es nicht tun.
11. **Rusts `std::process::ExitCode`** → ein eigener Rückgabetyp für `main` statt einer
    Warnung, die nur Konstanten trifft (CLI-12). Neu; ersetzt die erste Empfehlung.
12. **Elms Zurückhaltung** → bei zwei eingebauten Profilen bleiben, keine Template-Maschine,
    kein Lint-Werkzeug neben dem Compiler, kein Toolchain-Multiplexer. Die Liste dessen, was
    v5 *nicht* bekommt, ist Teil des Designs.

Was wir **nicht** übernehmen sollten, mit Begründung:

- **.NETs `-p:Key=Value`** — ein universeller Durchgriff, der jeden Tippfehler stumm
  akzeptiert. Lyric lehnt genau das an zwei anderen Stellen ausdrücklich ab
  (`LYR-CLI0017` für `lyric.json`, `LYR-CLI0003` für unbekannte `-D`).
- **.NETs beliebige Konfigurationsnamen** — dasselbe Problem eine Ebene höher.
- **Zigs vier Buildmodi** — die Achse „optimiert gegen ungeprüft", die `ReleaseSafe` von
  `ReleaseFast` trennt, existiert in Lyric nicht: es gibt kein `unsafe`, und die Checks fallen
  nie weg. `STATUS.md:2466-2470` begründet das gemessen — was ein `unsafe` entfernen würde, ist
  ein Vergleich und ein Sprung in einer Dispatch von ~23 Zyklen, also etwa ein Prozent, bezahlt
  mit der Eigenschaft, auf der das Embedding verkauft wird. *(Belegkorrektur: die erste Fassung
  zitierte `STATUS.md:2438-2442`; dort stehen `@Deprecated`- und Iterator-Einträge.)*
- **Denos Ein-Binary-Ansatz** — `lyrvm` ohne Compilerreferenz ist eine echte, prüfbare
  Eigenschaft. **Aber sie hat einen gemessenen Preis** (F26, CLI-25), und der gehört auf den
  Tisch, bevor man sie verteidigt.
- **Buns Skript-vor-Datei-Auflösung** — Lyrics Regel ist besser und steht an einer Stelle.
- **Eine Vertrauensfrage je Checkout** (CLI-35 C) — auf CI braucht sie einen Schalter, und der
  wird gesetzt.

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept):**

- **CLI-16, `--deny` neben `--grant`.** Zwei Flags für eine Achse. Begründung, die ich für
  tragfähig halte: es ist *kein* zweiter Mechanismus, sondern die zweite Richtung desselben
  (die Bitmaske ist dieselbe). Ohne `--deny` muss jeder, der eine Achse ausschließen will, alle
  anderen aufzählen — und rät bei jeder neuen Achse falsch. **Braucht einen Satz im ADR.**
- **CLI-16, zwei Schreibweisen je Capability-Bit** (`file`/`fileAccess`, `net`/`network`/
  `networkAccess`, …, gelesen `Capabilities.cs:124-134`). Das **ist** ein Parallelmechanismus,
  nur ein kleiner. **Kürzen**, mit Uhr nach CLI-29.
- **CLI-9, Option B (`lyric env -w`).** In der ersten Fassung als vierte Präzedenzstufe
  abgelehnt. **Angenommen**, weil die Ablehnung auf einem falschen Maßstab beruhte: eine
  benutzerweite Vorgabe lässt sich in einer eingecheckten Projektdatei nicht ausdrücken, und
  CLI-21 A fügt `lyric.json` im selben Dokument als Sprosse hinzu. Die Trennung ist jetzt
  ausgeschrieben (Tabelle in CLI-9).
- **CLI-22, `lyric lint`.** Ein zweiter Analysator neben der Sema. **Abgelehnt**, genau
  deswegen. Lyrics Warnungen sind Compilerdiagnosen mit stabilen Codes
  (`19-diagnostics.md:6-9`).
- **CLI-19, `lyrfix` als eigenes Binary.** Ein zweiter Umschreibemechanismus neben den
  LSP-Codeaktionen. **Abgelehnt** zugunsten von „die Diagnose trägt die Ersetzung".
- **CLI-28, `-` und `--stdin` nebeneinander.** Zwei Schreibweisen für eine Sache. **Aufgelöst:**
  `-` wird die Regel, `--stdin` bleibt ein Alias mit Uhr.
- **CLI-22, `std.cli`.** Wenn die Standardbibliothek einen Argumentparser bekommt, der eine
  andere Flagsyntax spricht als die Toolchain, hat Lyric zwei CLI-Dialekte. **Auflage:**
  `std.cli` spricht die Syntax aus CLI-6 — **und erst, nachdem CLI-24 entschieden ist**, weil
  ein Programm heute zwei Aufrufkonventionen hat (F18, gemessen) und ein gemeinsamer Dialekt die
  Kollision sonst verschärft.

**Mit anderen Gebieten:**

- **Gebiet Projektdatei/Module:** CLI-8, CLI-21, CLI-35 und CLI-38 verlangen alle, dass
  `lyric.json` Schlüssel trägt, die es heute nicht trägt, und CLI-38 verlangt eine Aussage über
  das Format selbst. Wer dort das Schema festlegt, legt hier mit fest. `dependencies` sind heute
  lokale Pfade (`ProjectFile.cs:292-296`) — `add`/`publish` hängen vollständig an der
  Paketentscheidung.
- **Gebiet Laufzeit/Capabilities:** CLI-16 B (Werte auf den Achsen) ist zu 90 % Arbeit in der
  Laufzeit. CLI-17 und CLI-35 ebenso.
- **Gebiet Pack/Format:** CLI-36 A verlangt ein Footer-Feld — Formatänderung, `docs/Pack.md`,
  Gate.
- **Gebiet Diagnosen:** CLI-11 (Ereignisstrom mit Schema), CLI-19 (Ersetzungen in der Diagnose)
  und CLI-29 (Deprecation als eigenes `kind`) sind dasselbe Format. Wenn dort ein anderes
  gewählt wird, fallen alle drei hier um.
- **Gebiet Build/`std.build`:** CLI-8 verlangt, dass der `Profile`-Struct in Lyric abgeleitete
  Profile abbilden kann — `Profile.selected()`, `Profile.debug()`, `Profile.release()` sind
  heute drei feste Funktionen (`16-building.md:116-118`). CLI-26 und CLI-27 (Cache, Sperre)
  gehören ebenfalls dorthin.
- **Gebiet Laufzeit/Panik:** CLI-12 D (Panik-Kennzeichen) ist eine Zusage der Laufzeit.
- **Gebiet Funktionen/Stdlib:** CLI-12 E (`ExitCode` als Rückgabetyp von `main`) ist eine
  Sprachentscheidung, keine CLI-Entscheidung.
- **Gebiet Testen:** CLI-39 A (`--jobs`) hängt an der offenen Isolationsfrage innerhalb einer
  Testdatei.
- **Gebiet Spezifikation:** CLI-30 legt fest, wie viel `since:`-Gates dieses Gebiet erzeugt.

**Innerhalb des Gebiets:**

- **CLI-3 gegen CLI-4 gegen CLI-33.** Wenn `--help` an jeder Position gilt, muss CLI-4 B
  gewählt sein; CLI-4 B braucht eine Grenze, und die Grenze ist `--`; `--` bedeutet heute in
  fünf Verben fünf Dinge (F20). Die Reihenfolge ist: CLI-33, dann CLI-4, dann CLI-3.
- **CLI-3 C gegen CLI-17 gegen CLI-37.** `lyric build --help` führt das Skript aus (F22) — als
  Hilfe richtig, als Completion-Quelle falsch. Die Regel aus CLI-37 („eine Completion führt
  niemals ein Buildskript aus") ist die Trennlinie; ohne sie widersprechen sich die drei.
- **CLI-1 A gegen CLI-25.** A verteidigt die Prozessgrenze architektonisch und zahlt gemessen
  drei Prozessstarts je `lyric run` (F26). Ein In-Prozess-Schnellpfad (CLI-25 B) würde genau die
  Grenze durchlöchern, auf die A sich beruft. Die Frage muss mit einer Zahl entschieden werden,
  nicht mit einem Prinzip.
- **CLI-1 C (PATH-Erweiterungen) gegen CLI-5 (ein Optionssatz).** Eine Erweiterung auf dem PATH
  kann nicht gezwungen werden, `--quiet` zu verstehen. Entweder die Zusage gilt nur für die
  mitgelieferten Werkzeuge — dann sagt die Spezifikation das —, oder C fällt.
- **CLI-11 gegen CLI-29.** Der Deprecation-Mechanismus braucht den Maschinenstrom, und der
  Maschinenstrom ist heute von genau so einer Warnung zerbrechbar (F16). F16 ist damit kein
  Bug unter vielen, sondern die Sperre vor dem gesamten Migrationspfad.
- **F5 und der `lyrbuild`-Parser verdecken sich gegenseitig** — aber **nur in einer
  Richtung**, und die erste Fassung hatte sie falsch herum. Repariert man allein `ValueOptions`
  (`--progress` eintragen), wird `lyric build --progress always` zu einer **korrekten Ablehnung**
  durch `lyrbuild` („unknown argument: --progress") statt zu einem Fehlgriff auf eine Datei
  namens `always` — die Diagnose wird besser, es öffnet sich nichts. Nur die umgekehrte
  Reihenfolge (`lyrbuild` lernt `--progress`, die Treibertabelle bleibt unvollständig) legt F5
  frei. Die Empfehlung „ein Commit" bleibt richtig; die Begründung war es nicht.

---

## 6. Nach der Kritik geändert

**Falsche Aussagen über Lyric 4 korrigiert (alle nachgemessen oder nachgelesen):**

- `--json` wirkt in **einem** Werkzeug, nicht in vier. Gemessen für `lyrvm run`, `lyrvm verify`,
  `lyrfmt`, `lyrpack`, mit `lyrc` als Kontrolle. CLI-11 ist damit eine Erstimplementierung, kein
  Ausbau.
- **Elf ausführbare Projekte insgesamt**, `lyric` und `lyrstub` eingeschlossen — nicht „elf plus
  Stub". Die richtige Zahl für `Tool.All` ist **neun**; ein neues Werkzeug wäre das **zwölfte**
  Binary (CLI-18 C, CLI-19 B).
- **Neunzehn** lebende `LYR-CLI`-Codes, nicht achtzehn. Die Zahl wurde zweimal als Argument
  benutzt (CLI-2, CLI-22) und ist dort korrigiert.
- **Vier Felder, sieben Schreibweisen**, nicht „sechs Feldflags"; dazu vier Pass-Schalter, die
  keine Profilfelder sind. Und `--no-deny-warnings` gibt es **nicht** — gemessen.
- **Die Feld-Overrides gelten nur auf dem Datei-Pfad**, nicht im Projekt. F2 ist entsprechend
  erweitert; CLI-8 B bekommt das als erste von drei Auflagen.
- **Vier Verhaltensweisen bei unbekannten Optionen**, nicht drei: `lyrdbg` schluckt still
  (gemessen), `lyrls` lehnt ohne Diagnosecode ab (gemessen).
- **Fünf falsche `Pfad:Zeile`-Belege ersetzt.** `PLAN.md` liegt unter
  `docs/Befunde_und_Verbesserungen/`; `lyrfix` steht auf `:343`, Profiler auf `:345`, Coverage
  auf `:346`, das Uhrenargument auf `:360`. Die ~23-Zyklen-Messung steht auf `STATUS.md:2466-2470`.
  „did you mean" steht auf `docs/guide/19-diagnostics.md:98` (die Mechanik in
  `NameSuggestion.cs:6-49`), nicht auf `:63-70`.
- **`LYR-CLI0018` nennt beide Versionen bereits** (`ProjectFile.cs:355-357`). CLI-18 D ist fast
  ein Nulltausch.
- **Capabilities:** zwei Sammelnamen und sechs Zweitschreibweisen, nicht „drei Aliase".
- **Der Kommentar in `Program.cs:173-176` wurde falsch gelesen.** Er verwirft die *vollständige*
  Tabelle und baut bewusst nur die wertnehmende Teilmenge. Der Vorwurf doppelte Wissenshaltung
  bleibt; der Vorwurf Selbstwiderspruch fällt.
- **`repl` ist kein sauberes Durchreich-Muster**, sondern ein latenter Defekt: der Treiber
  schickt `lyrrepl` das Wort `repl` mit, das nur deshalb nicht auffällt, weil `lyrrepl` es
  ignoriert.

**Neue gemessene Befunde, die die erste Fassung nicht hatte:** F16 (eine Warnung desselben
Programms zerstört den `--json`-Strom), F17 (zusammengesetztes Verb mischt JSON und Text auf
einem Strom), F18 (zwei Aufrufkonventionen für dasselbe Programm), F19 (kein Cache, keine
Sperre), F20 (fünf Bedeutungen für `--`, eine mehr als die Kritik fand), F21 (`lyric run` im
Projekt ist nicht leise zu bekommen), F22 (`--help` führt das Buildskript aus), F23
(`lyric.json` ist JSONC), F24 (keine stdin-Konvention), F25 (keine Versionskohärenz), F26
(Zeitkosten der Werkzeugkastenform).

**Falsche Aussagen über Vergleichssprachen korrigiert (acht):** Zigs `-O`-Form, Zigs
Maschinenausgabe (`zig env` statt `--color`), Denos angebliche Rechtesektion in `deno.json`
(es sind `deno task`/`install`/`compile`), Denos `--json`-Breite, Elms fünftes Flag `--docs`,
`go env` als **ein** Verb mit Flags, `cargo bench` als harness-gebundenes Verb (nicht
attributgebunden), Swifts erfundenes `--format json`. Jede Korrektur ist in §2 benannt und
schlägt auf mindestens eine Empfehlung durch.

**Siebzehn fehlende Designfragen ergänzt:** CLI-23 bis CLI-39.

**Sieben Empfehlungen geändert, weil die Kritik sie zu Recht angegriffen hat:**

- **CLI-3 Zusatzregel** umformuliert: der Treiber antwortet zuerst und reicht weiter, statt
  `--help` nie weiterzureichen — sonst stirbt die Eigenschaft, die §4 Punkt 10 lobt.
- **CLI-12** empfiehlt nicht mehr die Konstanten-Warnung (trifft den Fall, den man ohnehin
  sieht), sondern einen eigenen Rückgabetyp für `main` nach Rusts `ExitCode`.
- **CLI-9 B angenommen** statt abgelehnt: projektweit gegen benutzerweit ist eine eigene Frage,
  und `go env -w` kostet kein zweites Verb.
- **CLI-8 B** bekommt drei ausformulierte Auflagen (Feld-Overrides zuerst, symmetrische
  Negation, Namensregel für `out/<profil>/` mit der Windows-Kollision).
- **CLI-11** von „Bruch: minor" auf **major** hochgestuft, mit Übergangsform
  (`--message-format=json-document|jsonl`).
- **CLI-2** begründet `clean` nicht mehr mit „drei Zeilen"; die Semantik wandert nach CLI-26.
- **CLI-14** behauptet nicht mehr, Completions fielen aus CLI-10 ab — Werte brauchen CLI-37.

**Eine Aussage der Kritik zurückgewiesen, eine halb:**

- **§5, „wer einen der beiden repariert, öffnet den anderen"** — die Kritik hat recht, dass es
  nur in einer Richtung gilt, und der Abschnitt sagt das jetzt. Die Schlussfolgerung („ein
  Commit") bleibt.
- **§1.2, doppelte Wissenshaltung** — hält. Nur der Zusatz „widerspricht dem eigenen Kommentar"
  war eine Fehllesung und ist gestrichen.

