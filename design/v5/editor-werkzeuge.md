# Editor, Debugger, REPL, Formatter — Dossier für Lyric 5 (Fassung nach der Kritik)

Stand 2026-09-28. Gemessen gegen die gebauten Binaries im Checkout (`main @ 6f6f029f`,
Toolchain 4.6.0), Debug **und** Release. Proben unter `…/scratchpad/v5-design/probes/editor/`,
`editor2/`, `editor-r3/`, `editor-r4/` (frühere Runden), `editor-review-r2/` (Proben der Kritik)
und `editor-r5/` (heute: Nachmessung jedes Kritikpunkts, mit `ERWARTUNG.txt` vor dem Lauf).
Clients: `vscode-lyric` 1.4.0, `jetbrains-lyric` 1.4.0 (Checkouts neben `lyric/`).

**Belegregel.** Jede Aussage über Lyric 4 ist mit `[gemessen]`, `[gelesen: Pfad:Zeile]` oder
`[behauptet]` gekennzeichnet. Aussagen über andere Sprachen sind Allgemeinwissen aus deren
öffentlicher Dokumentation und tragen keine Marke.

**Vorwarnung zu den Zahlen.** Latenzen stammen von einer Maschine, auf der parallel gearbeitet
wird. Belastbar ist die Größenordnung und das Verhältnis der Fälle, nicht die dritte Stelle.

---

## 0. Kurzfassung

Lyric 4 liefert LSP, DAP, Formatter, REPL, Doc-Site und zwei Editor-Clients, alles first-party im
Repo. Von den acht Vergleichssprachen haben nur **Swift und C#** Sprachserver, Debugger und
Formatter zugleich aus erster Hand; Go hat den Debugger (delve) aus einer Community-Org, Rust, Zig,
Elm, Kotlin (bis 2025) und Julia mindestens eines der drei aus der Community. In der **Breite**
steht Lyric weit vorn.

Was fehlt, ist **Tiefe an sechs Stellen**, plus ein Rahmen, den kein Werkzeug ausspricht:

1. **Keine Inkrementalität, kein Abbruch, kein Rückgang.** Jeder Tastendruck kompiliert die Welt
   [gelesen: `src/Lyric.Lsp/Analysis/AnalysisService.cs:47-50`]; ein laufender Compile ist nicht
   abbrechbar, nur sein Ergebnis wird verworfen [gelesen: `AnalysisService.cs:52-55`]. Einzeldatei
   56–67 ms (Debug) bzw. 93–106 ms Median an einer 5-Zeilen-Datei in **beiden** Konfigurationen
   [gemessen, §1.1]; Projekt mit 61 Modulen 0,26–1,9 s [gemessen]; Arbeitssatz nach einem
   Tipp-Stoß bleibt bei 132 MB stehen [gemessen]. **Release dreht nichts** — das war die
   Vermutung der ersten Fassung, und sie ist gemessen falsch.
2. **Keine Code Actions.** `textDocument/codeAction` antwortet `-32601` [gemessen]. Der Compiler
   sagt *„did you mean 'count'?"* [gelesen: `docs/guide/19-diagnostics.md:98`] als Text-Note, und
   niemand kann es anklicken. Damit fehlt der Platz, an dem `lyrfix` und der Editor **denselben**
   Fix teilen würden — mit einer Grenze, die Spec §12.5 zieht: **eine Migrationsuhr darf keinen
   Fix tragen** (E28).
3. **Der Debugger lügt still an vier Stellen und läuft mit allen Rechten.** `condition`,
   `hitCondition`, `logMessage` werden mit `verified: true` beantwortet und ignoriert [gemessen,
   mit Kontrolllauf]; `noDebug: true` hält trotzdem an jedem Breakpoint [gemessen]. Eine Panik
   beendet die Sitzung statt am Fehlerort zu halten [gemessen]. `evaluate` erreicht Arrays nur
   über `xs.[0]` [gemessen]. Und `launch` gewährt `Capability.All` ohne ein Feld, das es
   einschränken könnte [gemessen: `editor-r5/dap_cap.py`, Datei geschrieben; gelesen:
   `src/Lyric.Dap/DapServer.cs:336`, `src/Lyric.Vm/LoadedProgram.cs:72-73`] — konsistent mit der
   Standalone-Regel [gelesen: `docs/guide/13-standard-library.md:628-629`], aber ohne Option (E21).
4. **Die REPL ist ein Wegwerf-Compiler mit Textgedächtnis.** Sie wiederholt jeden Seiteneffekt
   [gemessen], zeigt **keine Warnungen** [gemessen, mit Kontrolle] — also auch keine der vier
   Migrationsuhren — und ihr Deklarationsklassifikator kennt `type`, `opaque`, `extern` und
   `@`-Attribute nicht: `type Meters = int;` wird als Anweisung in `main` geparst und erzeugt
   fünf Fehler [gemessen: `editor-r5`, 10 Fehler für die Vier-Zeilen-Eingabe; gelesen:
   `src/Lyrrepl/Session.cs:42-44`]. `var` fehlt dort **zu Recht**: globale `var` sind illegal
   [gemessen: `LYR-PAR0027`; gelesen: `docs/Grammar.md:174`].
5. **Zwei Deutungen von `///`.** Hover reicht den Text roh durch [gelesen:
   `src/Lyric.Lsp/Analysis/HoverProvider.cs:66-70`]; DocGen rendert ihn mit Markdig [gelesen:
   `tools/DocGen/Rendering/MarkdownRenderer.cs:29-33`]. Grammatik und Spec definieren nur das
   Token [gelesen: `docs/Grammar.md:48-51`, `lyric-spec/spec/01-lexical.md:45-47`]. Ein Benutzer
   kann für sein Projekt keine Doku erzeugen: `docgen` verlangt `stdlib/` [gemessen], der Treiber
   hat kein `doc`-Verb [gemessen].
6. **Ein Profiler existiert, ist unerreichbar, und sein Schalter kollidiert mit dem Build-Profil.**
   `LYRIC_PROFILE=1` zählt Instruktionen **pro Funktion** [gelesen: `Interpreter.cs:218-228`,
   `LoadedProgram.cs:187-201`], niemand liest `Hotspots` [gemessen: grep]; dieselbe Variable
   wählt seit 4.5 das Compile-Profil [gelesen: `src/Lyric.Frontend/Compiler/Profile.cs:34,48`],
   `1` fällt still auf `debug` zurück [gemessen: `p_default.lyrbc` = `p_one.lyrbc`].

Der Rahmen: **Debugger, Budget und Profiler sind Interpreter-Werkzeuge.** Ein Lauf unter
`DebugController` oder `ExecutionBudget` bleibt interpretiert, auch mit `--jit` [gelesen:
`LoadedProgram.cs:61-69`, `Interpreter.cs:66-68` `AllowsCompiledCode => false`]. Das muss 5.0
als Entscheid aussprechen (E11), nicht im Kommentar eines Parameters lassen.

---

## 1. Ist-Stand

### 1.1 `lyrls` — der Sprachserver

**Größe.** 5245 Zeilen in `src/Lyric.Lsp` plus 104 in `src/Lyrls` [gemessen: `wc -l`]; 248 Tests
in `tests/Lyric.Tests.Lsp` [gemessen]. Transport nur stdio; `--pipe=`/`--socket=` werden
abgewiesen [gelesen: `src/Lyrls/Program.cs:47-52`].

**Was er kann** [gemessen: `initialize`-Antwort; gelesen: `src/Lyric.Lsp/LspServer.cs:1005-1035`]:

| Fähigkeit | Stand |
|---|---|
| `hover`, `definition`, `references`, `documentSymbol`, `workspace/symbol` | ja |
| `completion` (Trigger `.`), `signatureHelp` (`(`, `,`) | ja |
| `rename` mit `prepareRename` | projektweit unter `lyric.json`, sonst nur innerhalb der Datei [gelesen: `Analysis/RenameProvider.cs:13-19`, `:129-137`] |
| `semanticTokens/full` | 11 Typen, 3 Modifier [gelesen: `Analysis/SemanticTokensProvider.cs:30-43`]; kein `range`, kein `delta` |
| `foldingRange`, `inlayHint`, `documentFormatting` | ja; Inlay nur für Bindungen ohne Annotation [gelesen: `Analysis/InlayHintProvider.cs:10-15`]; Formatierung als **ein** `TextEdit` über das ganze Dokument [gelesen: `LspServer.cs:975-976`; gemessen: `editor-review-r2/lsp_ca.out`] |
| `textDocumentSync` | **Full** [gelesen: `LspServer.cs:1008`] |
| `positionEncoding` | nur utf-16 [gelesen: `LspServer.cs:1012`] |
| `serverInfo.version` | Toolchain-Version; der VS-Code-Client **zeigt** sie an, prüft sie nicht [gelesen: `vscode-lyric/extension.js:185`] |

**Was er nicht kann** [gemessen, jeweils `-32601`]: `codeAction` · `typeDefinition` ·
`implementation` · `documentHighlight` · `rangeFormatting` · `onTypeFormatting` ·
`selectionRange` · `prepareCallHierarchy` · `textDocument/diagnostic` (Pull) · `codeLens` ·
`documentLink` · `inlineValue` · `semanticTokens/range|delta` · `workspace/executeCommand` ·
`workspaceFolders` · `$/progress`. Behandelt werden **22 `LspMethods`** (`initialize` an zwei
`case`-Stellen zählt einmal) **plus `exit`**, das vor dem `switch` in jedem Zustand beantwortet
wird — 23 Methoden [gelesen: `LspServer.cs:171-320`, `:280-285`].

**Architektur.** *„The compiler is run WHOLE for every analysis, project or not"* [gelesen:
`AnalysisService.cs:47-48`]. Gemessen, Debug **und** Release:

| Fall | Dateien / Zeilen | Build | Median pro Tastendruck (debounce 0) |
|---|---|---|---|
| Einzeldatei | 1 / 42 | Debug | **56 ms** |
| Einzeldatei | 1 / 308 | Debug | **67 ms** |
| Einzeldatei `sample.lyr`, zwei Läufe je | 1 / 5 | Debug | **102 / 116 ms** (min 88–91) [gemessen: `editor-r5/lsp_lat_dbg.py`] |
| Einzeldatei `sample.lyr`, zwei Läufe je | 1 / 5 | **Release** | **106 / 93 ms** (min 66–83) [gemessen: `editor-r5/lsp_lat_rel.py`] |
| Erste Diagnose nach `didOpen` | 1 / 5 | Debug / Release | 918 / 880 ms · 874 / 724 ms |
| Projekt `stdlib-tests` | 22 / 3448 | Debug | **338 ms** |
| Projekt, synthetisch, Einstiegsdatei | 61 / 2528 | Debug | **259 ms** und **1159 ms** (zwei Läufe) |
| Projekt, synthetisch, Blattmodul | 61 / 2528 | Debug | **1852 ms** |

Zwei Befunde daraus. Erstens: es skaliert mit der **Zahl der Module**, nicht mit den Zeilen.
Zweitens: **Release bringt im Rauschen ~10 %** (Mediane 93–106 gegen 102–116 ms; die Minima
66–83 gegen 88–91). Der Kommentar bei `LspServerOptions.Debounce` [gelesen: `LspServer.cs:15-23`]
— Compile *„7 to 16 ms"*, Debounce die *„DOMINANT latency"* — hält in **keiner** Konfiguration:
der Ganz-Compile-Boden liegt bei 65–90 ms, der Debounce bei 50 ms. Die erste Fassung dieses
Dossiers hatte „Release kann das drehen" behauptet; die Release-Binaries lagen im Checkout
[gemessen: `src/Lyrls/bin/Release/net10.0/lyrls.dll`, 24.09.] und die Messung kostete Minuten.

Drei Eigenschaften, die die Inkrementalitätsfrage mitentscheiden:

- **Nicht abbrechbar.** *„Cancellation therefore means the RESULT IS DISCARDED"* [gelesen:
  `AnalysisService.cs:52-55`].
- **Entdopplung pro Einheit.** `_running` ist nach Quellwurzel bzw. Dokumentpfad geschlüsselt;
  ein neuer Lauf zieht den vorigen zurück [gelesen: `AnalysisService.cs:68-76`, `:747-759`].
- **Completion und SignatureHelp kompilieren erneut**, synchron auf dem Anfrageweg [gelesen:
  `AnalysisService.cs:400-423`, `Analysis/CompletionProvider.cs:50-51`].

**Speicher pro Tastendruck** [gemessen, mit Kontrolle]:

| Lauf | Basis | nach `didOpen` | Spitze im Stoß | 3 s später |
|---|---|---|---|---|
| Projekt, 8 Dokumente offen, 4 Stöße | 30 MB | 100 MB | **132 MB** | **132 MB** |
| Projekt, 1 Dokument offen, 4 Stöße (Kontrolle) | 30 MB | 63 MB | 72 MB | 72 MB |

**Diagnosen für geschlossene Dateien werden publiziert.** Der Projektlauf iteriert über **alle**
Wurzeln (`CollectRoots`: jede `*.lyr` unter der Quellwurzel plus offene Puffer darunter) und
publiziert je Datei, ob offen oder nicht (`open?.Version`) [gelesen: `AnalysisService.cs:664-681`,
`:496-521`]; was in der letzten Runde publiziert war und jetzt nicht mehr zur Einheit gehört,
wird mit leerer Liste zurückgezogen [gelesen: `:524-534`]. Welche `lyric.json` gilt, entscheidet
`ProjectFile.Discover` **aufwärts vom Verzeichnis des Dokuments** [gelesen: `:282-289`] — zwei
Projekte in einem Workspace sind zwei Einheiten, `workspaceFolders` spielt keine Rolle (und ist
`-32601`). Nicht gemessen: wie VS Code und JetBrains Diagnosen zu nicht geöffneten Dateien im
Problems-Panel zeigen [behauptet: beide zeigen sie].

**Das Fenster der ersten Analyse ist stumm und antwortet `null`** [gemessen]:

| Anfrage sofort nach `didOpen` | Antwort | nach der ersten Diagnose |
|---|---|---|
| `semanticTokens/full`, `documentSymbol`, `foldingRange`, `hover` | **null** | Daten |
| `completion` | Einträge | Einträge |

**Synchronisation.** Full-Sync; ein bereichsbasiertes `didChange` wird **als Gesamtinhalt**
genommen [gemessen: Puffer wird zu `"2"`]. `untitled:`-Puffer werden nicht analysiert — bewusst
[gelesen: `Documents/DocumentUri.cs:26-34`].

**Formatieren im Editor hat drei Fälle** [gelesen: `LspServer.cs:960-978`; gemessen]:

| Puffer | Antwort |
|---|---|
| parst nicht | **`null`** — kein Fehler, keine Meldung |
| ist schon in Form | `[]` |
| braucht Formatierung | **ein** `TextEdit` von `0:0` bis Dokumentende [gemessen: `lsp_ca.out`, Range `0:0`–`7:0`] |

Kontrolle: `lyrfmt` meldet die Diagnosen und lässt die Datei in Ruhe [gemessen; gelesen:
`src/Lyrfmt/Program.cs:107-115`].

**Diagnosen im Protokoll.** `range`, `severity`, `code`, `message`, `relatedInformation`, `tags`
(Unnecessary für SEM0071–0073, Deprecated für SEM0076) [gelesen: `Analysis/DiagnosticMapper.cs:54-77`].
**Kein `data`, kein `codeDescription`.** SEM0107–0110 tragen keinen Tag [gelesen: `:70-77`].

**Hover auf Deklarationsnamen** — präzise gemessen [`editor-r5/lsp_hover.py`, Kontrolllauf mit
fehlerfreier Datei]:

| Position | Antwort |
|---|---|
| `struct Point`, `enum Shade`, `type Meters`, `fn twice`, `fn main` — **der Name in der Deklaration** | **null** |
| Feldname `x` in `struct Point { x: int, … }` | **null** |
| `let p = …` — der Name in der Bindung | ```lyric let p: Point``` (Range = ganze Bindung) |
| Verwendungen (`Point`, `Meters`, `twice`, `p.x`) | `struct Point` · `type Meters` · `fn twice(int) -> int` · `x: int` |

Die erste Fassung hatte „Hover auf einem Deklarationsnamen antwortet null" verallgemeinert; richtig
ist: **Funktions-, Typ- und Feld-Deklarationsnamen** gehen ins Leere, Bindungsnamen nicht.

**Der Test-Root: Diagnosen ja, Einheit nein** [gemessen: `editor-r5/lsp_test.py`, `lsp_refs.py`;
Fixture `pj/` mit `sourceRoot: src`, `testRoot: tests`]:

| Probe | Ergebnis |
|---|---|
| `tests/util_test.lyr` mit `import util;` geöffnet | **keine Diagnose** — der Import löst auf |
| `tests/bad_test.lyr` (Kontrolle: `import nothere`, `let s: string = 1`) | `LYR-RES0003` + `LYR-SEM0001` — Diagnosen fließen |
| `references` auf `util.twice` aus `src/util.lyr` | nur `main.lyr`, `util.lyr` — **die Testdatei fehlt** |
| `rename` aus der Testdatei | `-32803` *„the file is not part of a project; … needs a lyric.json"* — **falsche Meldung**, `lyric.json` nennt den `testRoot` |

Ursache: `Options(project)` reicht `project?.SourceRoot` auch für ein Dokument **außerhalb** der
Quellwurzel durch [gelesen: `AnalysisService.cs:380-385`], darum löst der Import; aber die
Einheitswahl `IsUnder(document.Path, project.SourceRoot)` schickt die Testdatei in
`AnalyzeSingleAsync` [gelesen: `:429-437`], und `RenameProvider` deutet „nicht projektweit" als
„keine `lyric.json`" [gelesen: `RenameProvider.cs:129-137`]. Die erste Fassung hatte „sieht die
Projektmodule nicht" behauptet — falsch; die Lücke ist die **Einheitsmitgliedschaft** (E25).
`build.lyr` liegt ebenfalls außerhalb der Quellwurzel und nimmt denselben Einzeldatei-Zweig
[gelesen: gleicher Zweig; ob `std.build` darin auflöst, ist nicht gemessen — behauptet: ja, weil
`StdlibRoot` gesetzt ist].

**Weitere Befunde:**

- **Keine Schlüsselwort-Completion** [gemessen]. VS Code kompensiert mit 13 Client-Snippets
  [gemessen: `vscode-lyric/snippets/lyric.json`]; JetBrains hat nichts dergleichen.
- **`documentSymbol` antwortet `null` ohne `hierarchicalDocumentSymbolSupport`** — absichtlich
  [gelesen: `Analysis/DocumentSymbolProvider.cs:19-23`; gemessen].
- **Keine Test-Integration in beiden Clients** [gemessen: grep `test`/`codeLens` in
  `vscode-lyric/package.json`, `extension.js`, `jetbrains-lyric` `plugin.xml` — null Treffer außer
  einem Regex-`.test(`]; `codeLens` ist `-32601`; `lyric test` hat kein `--json` [gelesen:
  `src/Lyrtest/Program.cs:90` erwähnt `--json` nur in einem Kommentar zu einem alten Fehler;
  behauptet: keine Option heute].
- **Der stale Kommentar.** `tooling/textmate/syntaxes/lyric.tmLanguage.json:7-15` behauptet, der
  Server liefere *„bisher nur Diagnosen"* [gemessen: 261 semantische Token für 42 Zeilen].
- **Tiefenlimit ist erledigt** [gemessen: 20 000 Klammern → `LYR-PAR0045`, 30 000-gliedrige
  `+`-Kette → `LYR-SEM0105`], `PLAN.md:102` führt es noch als offen. Quellzeile wird ungekürzt
  ausgegeben — 119 KB Diagnose für eine Zeile.

### 1.2 `lyrdbg` — der Debug-Adapter

**Größe.** 842 Zeilen in `src/Lyric.Dap`, 51 in `src/Lyrdbg` [gemessen]; 18 Tests [gemessen].
Behandelt: `initialize launch attach setBreakpoints setExceptionBreakpoints configurationDone
threads stackTrace scopes variables evaluate continue next stepIn stepOut pause disconnect
terminate` [gelesen: `DapServer.cs:142-253`]. Fähigkeiten: genau `supportsConfigurationDoneRequest`
und `supportsEvaluateForHovers` [gelesen: `DapMessages.cs:56-66`; gemessen].

**`launch` kennt vier Felder**: `program`, `stopOnEntry`, `noDebug`, `args` [gelesen:
`DapServer.cs:303-315`]. Kein `grant`, kein `test`, kein `cwd`, kein `env`.

**Fähigkeiten, stdin, stdout.** Das Programm wird mit `LoadedProgram.Load(module, natives)`
geladen — `granted` bleibt auf dem Default `Capability.All` [gelesen: `DapServer.cs:336`,
`LoadedProgram.cs:72-73`]; stdout/stderr werden zu `output`-Events umgeleitet, **stdin ist
`TextReader.Null`** — *„The debuggee reads no stdin for the same reason"* [gelesen:
`DapServer.cs:330-334`]. Gemessen [`editor-r5/dap_cap.py`]: ein Programm mit `file.writeText`
läuft unter `launch` (`wrote=true`, Datei existiert), auch mit einem ignorierten `"grant": "none"`
im Request; Kontrolle `lyrvm run cap.lyrbc --grant none` → `LYR-CAP0001`, Exit 1.

**Die stillen Lügen.** Vier Breakpoint-Formen auf derselben Schleifenzeile [gemessen, mit
Kontrolle]:

| Breakpoint | Antwort | Stopps | erwartet |
|---|---|---|---|
| ohne Bedingung (Kontrolle) | `verified: true` | 3 | 3 |
| `condition: "n == 999"` | `verified: true` | **3** | 0 |
| `hitCondition: "2"` | `verified: true` | **3** | 1 |
| `logMessage: "n is {n}"` | `verified: true` | **3** | 0 Stopps, 3 Ausgaben |
| `launch { noDebug: true }` | `verified: true` | **3** | 0 |

`noDebug` unterdrückt nur Stop-on-Entry [gelesen: `DapServer.cs:314, 348`]. JetBrains bietet die
Felder ausdrücklich nicht an [gelesen: `jetbrains-lyric/…/LyricBreakpoints.kt:18-21`]; VS Code
ohne `supportsConditionalBreakpoints` [behauptet: blendet die Felder aus]. `setExceptionBreakpoints`
antwortet `success` und tut nichts [gelesen: `DapServer.cs:181-189`].

**Eine Panik beendet die Sitzung** [gemessen]:

```
output(stdout)  "before\n"
output(stderr)  "panic: index 9 is outside an array of length 3 in 'main.boom' …"
exited          {"exitCode": 101}
terminated      null
```

Kein `stopped` mit `reason: "exception"` [gelesen: `DebugModel.cs:4-22` — `StopReason` kennt
`Entry Breakpoint Step Pause Exited Terminated`, kein `Exception`]. Guide 21:63 sagt es zu.
**Wo die Panik entsteht:** 45 `throw new LyricPanic`-Stellen in `src/Lyric.Vm` [gemessen: grep —
`Interpreter.cs` 24, `NativeRegistry.cs` 12, `Jit/JitRuntime.cs` 5, `DotnetBinding.cs` 2,
`LyrValue.cs` 1, `ExecutionBudget.cs` 1]. Der Controller sieht sie erst als gefangene Exception
nach dem Verlassen von `RunEntry` [gelesen: `DebugController.cs:127-139`] und inspiziert Frames
nur an Instruktionsgrenzen (`OnInstruction(Stack<Frame>, Frame)` [gelesen: `:157`,
`Interpreter.cs:73`]). **Aber:** der Interpreter hat bereits einen Catch, der die Panik **mit
lebendem Frame-Stack** sieht — dort wird der Backtrace angehängt: *„The backtrace is attached here
rather than at the throw site: the loop holds the frame stack"* [gelesen: `Interpreter.cs:304-310`].
Das ist der eine Ort für E34; Native-Paniken laufen durch dieselbe Stelle, weil Natives aus der
Schleife gerufen werden.

**`evaluate` ist ein Pfadlauf** — `expression.Split('.')` [gelesen: `DebugController.cs:532`]:
`xs` → `int[3]`; `xs.[0]` → `1`; `xs[0]`, `xs.[1] + 1`, `1/0` → *not a known name here*
[gemessen]. Array-Kinder heißen `[i]` [gelesen: `Debugging/ValueRenderer.cs:116`]. Guide 21:49-52
(*„names, not expressions"*) beschreibt das nicht. Ein fehlschlagender `evaluate` tötet die Sitzung
nicht [gemessen].

**Quellkarte relativ zum Programmverzeichnis, nicht zur Projektwurzel** [gelesen: `DapServer.cs:369`;
Projektdatei wird gelesen, `:389-400`]. Gemessen: Start aus `pj/src/main.lyr` → Frame
`helper.twice @ helper.lyr` **mit Pfad**; Start aus `pj/build/app.lyrbc` → **ohne Pfad**;
Breakpoint greift nur, weil `ToMapFile` auf den Dateinamen zurückfällt [gelesen: `:502-503`].
Die Karte trägt seit **1.1.0** relative Pfade [gelesen: `CHANGELOG.md:3568-3570` *„Paths are
stored relative to the entry file's directory"*; `docs/Bytecode.md:90` *„No timestamps, no absolute
paths"*; `src/Lyric.Frontend/Emit/SourceMapBuilder.cs:120-128` `Path.GetRelativePath`]. Die erste
Fassung hatte „seit 1.0.1 [guide/21:68-69]" geschrieben — jene Zeile datiert nur die Source-Map
selbst, nicht die Relativierung.

**Sonstiges.** `threads` fix ein Strang `main` [gelesen: `DapServer.cs:205-207`]; Koroutinen-Ketten
als logischer Stack [gelesen: `STATUS.md:841-844`]. Nicht implementiert [gemessen]: `setVariable`
· `completions` · `exceptionInfo` · `breakpointLocations` · `stepBack` · `restartFrame` ·
`setFunctionBreakpoints` · `loadedSources` · `disassemble` · `runInTerminal` (Reverse-Request nie
gesendet). Attach nur an einen **Host**, der `DapServer(input, output, controller, dir)` selbst
serviert [gelesen: `guide/21:104-116`]; dort *„The debuggee's output does not travel as output
events"* [gelesen: `guide/21:128-129`]. Dokumentierte Grenzen [gelesen: `guide/21:141-151`]:
Globalinitialisierer vor dem Attach, stdlib nicht schrittbar, Release-Modul zeigt die Optimiererwelt.

### 1.3 Die REPL

**Größe.** 335 Zeilen [gemessen], **10** `[Fact]`-Tests, 0 `[Theory]` in
`tests/Lyric.Tests.Cli/ReplTests.cs` [gemessen: grep -c]. Die erste Fassung hatte 11 geschrieben.

**Sie wiederholt jeden Seiteneffekt** [gemessen]: `let g = greet();` → `INIT`; jede folgende
Eingabe → `INIT` erneut. `Session.Program` baut vor jeder Eingabe die gesamte Präambel neu
[gelesen: `Session.cs:57-79`]; `STATUS.md:2236-2245` nennt es Rearchitektur.

**Der Deklarationsklassifikator ist eine Präfixliste** — `fn class struct enum interface extend
import module pub let` [gelesen: `Session.cs:42-44`]. Gegen `TopLevelDecl` der Grammatik
[gelesen: `docs/Grammar.md:164-176`] fehlen: `type`, `opaque type`, `extern`, `@`-Attribute.
Gemessen [`editor-r5`, Eingabe `repl_alias.in`]:

| Eingabe | Ergebnis |
|---|---|
| `type Meters = int;` | **5 Fehler** (`SEM0002 unknown identifier 'type'`, `SEM0022`, `PAR0016`, `SEM0002 'Meters'`, `SEM0052`) — als Anweisung in `main` geparst |
| `let d: Meters = 3;` | `LYR-RES0002 unresolved type 'Meters'` |
| `var v = 5;` dann `v` | läuft, dann `unknown identifier 'v'` — vergessen |

**`var` fehlt zu Recht.** Globale Bindungen sind `let`-only [gelesen: `docs/Grammar.md:174`
`GlobalBinding = BindingStmt . (* 'let' only *)`]; `lyrc check topvar.lyr` → `LYR-PAR0027`
*„global bindings must be immutable — use 'let', not 'var'"* [gemessen: `editor-r5`]. Die REPL
kann `var` nicht als Deklaration akkumulieren, ohne ein ungültiges Programm zu erzeugen. Die erste
Fassung nannte das „ein Bug, kein Design" — falsch; das Vergessen ist die Statement-Regel. Was
fehlt, ist die **Meldung** (E27), und das eigentliche Loch ist der Klassifikator (E26).

**Die Diagnose des schlechteren Versuchs wird gezeigt** [gelesen: `Session.cs:119-131`]; `_entries`
zählt Versuche, nicht Eingaben — dritte Eingabe meldet sich als `repl[4].lyr` [gemessen; Kommentar
`Session.cs:27-29` falsch].

**Keine Mehrzeiligkeit** [gelesen: `src/Lyrrepl/Program.cs:45`]; eine dreizeilige Funktion erzeugt
fünfzehn Fehler, neun auf generierten Text [gemessen].

**Keine Warnungen.** Gerendert nur bei `HasErrors` [gelesen: `Session.cs:157-160`]; `lyrc check`
meldet dieselbe Zeile als `LYR-SEM0071` [gemessen]. Damit schluckt die REPL SEM0076 und
**SEM0107–0110**.

**Alle Fähigkeiten.** `Interpreter.Run(module, [], natives)` mit Default `Capability.All`
[gelesen: `Session.cs:181`, `Interpreter.cs:206-208`]; `lyrrepl --help` kennt nur `--stdlib`
[gelesen: `Program.cs:109-126`]. Das ist die **Standalone-Regel** [gelesen: `guide/13:628-629`
*„A standalone run grants everything; a host grants explicitly"*], dieselbe wie `lyrvm run` ohne
`--grant` [gelesen: `src/Lyrvm/Program.cs:49-52`] — kein Sandbox-Bruch, eine fehlende Option (E21).

**Befehlssatz** `:help :list :reset :quit` [gelesen: `Program.cs:65-93`]. Der `:help`-Text sagt
*„Declarations accumulate; statements run once"* [gelesen: `Program.cs:121`] — der Satz, den 4.3.2
aus dem README gestrichen hat [gelesen: `CHANGELOG.md:518`, `STATUS.md:586-590`].

### 1.4 `lyrfmt` — der Formatter

**Größe.** 174 Zeilen Treiber, 1370 Zeilen `Lyric.Frontend/Formatting` (kein eigenes Assembly
[gelesen: `STATUS.md:2130-2132`]), 54 Tests plus Korpus-Test [gemessen; gelesen: `guide/18:92-94`].

**Vertrag.** *„There are no style options. The shape is the tool's contract"* [gelesen:
`src/Lyrfmt/Program.cs:6-9, 172`; `guide/18:10`]. Eine Datei, die nicht parst, wird gemeldet und
bitgleich belassen [gelesen: `Formatter.cs:11-13`; gemessen].

**Was gemessen hält:** Idempotenz auf allen Proben [gemessen]; Kommentar im Ausdruck wandert ans
Anweisungsende — dokumentiert [gelesen: `guide/18:76-78`; gemessen: `editor-review-r2/fmt_ctl.out`
`let n = 1 + 2; /* inner */`]; präzedenz-redundante Klammern verschwinden — der AST hat keinen
Klammerknoten [gelesen: `Formatting/AstFormatter.cs:14-17`], die Spec verlangt die Re-Derivation
[gelesen: `lyric-spec/spec/02-grammar.md:18-20` *„the formatter re-derives parentheses from it"*];
Literale aus dem Quellspan [gelesen: `AstFormatter.cs:11-14`; gemessen].

**Die Spec nennt zwei Werkzeuge beim Namen**, nicht eines: den Formatter (§2.2) **und den
Debugger** — *„a debugger's Globals scope"* [gelesen: `spec/04-modules.md:65`], *„whether a
debugger can name a slot"*, *„what a debugger writes beside a value"* [gelesen:
`spec/13-bytecode.md:69, 493, 520, 546, 561`]. Die erste Fassung hatte den Formatter „das eine
Werkzeug, das die Spec beim Namen nennt" genannt — falsch.

**Die Form hat sich dreimal geändert** [gelesen: `CHANGELOG.md`]: v2.5.1 (Operatorketten am
100-Spalten-Limit; *„a `lyric fmt --check` in CI may report files that were clean before"*
[`:2237, :2251`]), v3.6.1 (`catch (_: Boom)` verlor den Typ — der Formatter **änderte Bedeutung**
[`:1102-1104`, `STATUS.md:1127-1131`]), v3.9.0 (Attributlisten kanonisch [`:972`]). Kein Guide-
Satz sagt, ob die Form zum Versionsvertrag gehört; `toolchain` in `lyric.json` pinnt nur die
Baubarkeit [gelesen: `guide/12:122-124` *„A minimum, nothing more"*] (E30).

### 1.5 `docgen` — der Dokumentationsgenerator

**Größe.** 1419 Zeilen, 103 Tests [gemessen], einzige NuGet-Abhängigkeit des Repos (Markdig)
[gelesen: `tools/DocGen/DocGen.csproj:19-24`], `IsPackable=false` [gelesen: `:7`].

**Was er tut.** `docgen model <repo-root>` extrahiert die stdlib **rein syntaktisch** [gelesen:
`Extraction/StdlibExtractor.cs:12-14`]; `docgen site` schreibt eine versionierte Site [gelesen:
`Program.cs:29-38`]. Heute: 25 Module, 395 Items, alle dokumentiert [gemessen: `editor-r4/model.json`].
Ein Projekt ohne `stdlib/` wird abgewiesen [gemessen]. Kein `lyric doc` [gemessen].

**Doc-Tests existieren nicht.** Die v5-Liste führt sie als Punkt 19, P2: *„Code in `///` wird von
`lyric test` ausgeführt"* [gelesen: `docs/Befunde_und_Verbesserungen/lyric-v5-features.md:51`,
`PLAN.md:289`]. Die Guide-Snippets werden von der Suite kompiliert [gelesen: `CLAUDE.md`
Pflichtlektüre §2; behauptet: der Test liegt in `tests/`, nicht nachgeschlagen] (E31).

### 1.6 Die Clients und die Syntaxhervorhebung

**vscode-lyric 1.4.0** [gemessen]: Sprache, TextMate, zwei Kommandos, Problem-Matcher, Debugger
`launch`/`attach`, Task-Definition, 13 Snippets, vier Settings [gelesen: `package.json`].
462 Zeilen JS. **Keine Test-Integration** [gemessen].

**jetbrains-lyric 1.4.0** [gemessen]: *„deliberately no PSI implementation: that would be a
second frontend in Kotlin"* [gelesen: `jetbrains-lyric/README.md:10-13`]; LSP über die
Plattform-Integration, DAP über `intellij.platform.dap`, Run-Konfiguration [gelesen: `plugin.xml`].
Keine Snippets, **keine Test-Integration** [gemessen].

**TextMate** kanonisch in `tooling/textmate`, Lexer-Test pinnt die Keyword-Liste [gelesen:
`STATUS.md:2101-2104`]; keine Tree-sitter-Grammatik.

### 1.7 Profiler, JIT, Budget

`LYRIC_PROFILE=1` zählt **pro Funktion** (`Prepared.Executed`, `Hotspots` sortiert nach Funktion)
[gelesen: `LoadedProgram.cs:187-201`], ungelesen [gemessen: grep], kollidiert mit dem Profilnamen
[gemessen]. Die erste Fassung hatte „derselbe Zähler pro Block liefert Coverage" geschrieben —
falsch: Block-Coverage ist ein **neuer** Zähler in der Dispatch-Schleife (E10). JIT und Debugger
schließen sich aus [gelesen: `LoadedProgram.cs:61-69`, `Interpreter.cs:66-68`].

### 1.8 Wo die Doku mehr verspricht als der Code hält

| Stelle | Sagt | Ist |
|---|---|---|
| `LspServer.cs:15-23` | Compile „7 to 16 ms", Debounce dominant | 65–90 ms Boden in Debug **und** Release; Projekt 0,3–1,9 s [gemessen] |
| `tooling/textmate/…json:15` | Server liefert „bisher nur Diagnosen" | semantische Tokens seit M16 [gemessen] |
| `Session.cs:27-29` | „line 3 refers to the third entry" | Versuche gezählt, nicht Eingaben [gemessen] |
| `Program.cs:121` (`:help`) | „Declarations accumulate; statements run once" | Initialisierer laufen pro Eingabe erneut [gemessen]; `type`/`extern`/`@` gelten nicht als Deklaration [gemessen] |
| `guide/21:49-52` | „names, not expressions … `v.x`" | Arrays nur über `xs.[0]` [gemessen] |
| `guide/21` + Client-READMEs | Bedingungen „not offered" | Adapter akzeptiert sie mit `verified: true` und ignoriert sie [gemessen] |
| `guide/21:3-6` | „pressing F5 is the whole setup" | wahr; aber „Run Without Debugging" debuggt [gemessen] |
| `vscode README` | „Diagnostics … for the whole project" | Diagnosen ja, auch für Testdateien; `references`/`rename` sehen den Test-Root nicht [gemessen] |
| `RenameProvider.cs:129-137` | „the file is not part of a project" | die Datei liegt im `testRoot` derselben `lyric.json` [gemessen] |
| `PLAN.md:102` | Tiefenlimit fehlt | erledigt [gemessen] |
| `STATUS.md:1957` | „neither has ever had a release" | beide Client-Repos tragen `v1.3.0`, `v1.4.0` [gemessen] |
| `guide/21:63-64` | Panik beendet die Sitzung | stimmt — und ist der Punkt (E4/E34) |
| `guide/13:628` | „A standalone run grants everything" | gilt für `lyrvm`, REPL, `lyrdbg launch` und `lyrtest` — nur `lyrvm` hat `--grant` [gelesen/gemessen] |

---

## 2. Sprachvergleich

Acht Sprachen. Kursiv = **entgegengesetzte** Entscheidung zu Lyric.

| | Server | Inkrementell | Fixes im Editor | Debugger | Evaluate | Formatter | REPL | Doku aus `///` |
|---|---|---|---|---|---|---|---|---|
| **Lyric 4** | first-party, Compiler ganz | nein | nein | first-party DAP, Interpreter | Pfad | eine Form | Replay, unrein | stdlib-only, Markdown/roh |
| **Rust** (rust-analyzer) | first-party-Org, *eigener Frontend* neben rustc | ja (Salsa) | ja (Assists + rustc-Suggestions mit `Applicability`) | Community (CodeLLDB) | LLDB/GDB, brüchig | rustfmt, *Optionen*: ein stabiler Kern (`max_width`, `hard_tabs`, `tab_spaces`, `newline_style`, `use_small_heuristics`, `fn_call_width`, `reorder_imports` …), viele unstabil | Community (evcxr) | rustdoc, Markdown, Intra-Doc-Links |
| **Go** (gopls) | first-party, Compiler-Frontend als Bibliothek | ja (Paket-Snapshots) | ja (`SuggestedFix`) | *delve, Community-Org (go-delve)*, headless | eigener Mini-Evaluator | gofmt, keine Optionen | *keine* | `go doc`, *Plain-Text-Konventionen*, seit 1.19 gofmt-formatiert |
| **Swift** (SourceKit-LSP) | first-party, sourcekitd in-process | Datei-lokal + Background-Index | ja (Fix-its aus dem Compiler) | LLDB mit *vollem Compiler als Evaluator* | Ausdrücke | swift-format, *konfigurierbar* | LLDB-REPL | DocC, Markdown |
| **C#** (Roslyn) | first-party, Compiler = Service | ja (Full-Fidelity-Bäume, lazy Sema) | ja (Analyzer + CodeFixProvider) | first-party, JIT mit Debug-Info | Expression Evaluator aus dem Compiler | `dotnet format`, *.editorconfig-Optionen* | C# Interactive, *Submission-Kette* | XML-Doc-Tags, *eigene Doku-Sprache* |
| **Zig** (zls) | *Community*; nutzt `std.zig.Ast`, den Parser des Compilers aus der stdlib | Datei-lokal | wenig | LLDB/GDB | — | `zig fmt`, keine Optionen | keine | Autodoc aus `///` |
| **Elm** | *Community* (elm-language-server), ruft den Compiler | nein | Compiler-Prosa, wenig Actions | *kein Step-Debugger*, Time-Travel im Browser | — | elm-format, keine Optionen (Community) | first-party, *reine Sprache: Replay ohne Seiteneffekte* | Markdown, Paket-Site |
| **Kotlin** | *IDE-first*: Compiler-Frontend (FE1.0) in IntelliJ mit IDE-eigener Lazy-Resolution und Caches über PSI; K2 = FIR + Analysis API; offizieller LSP 2025 | ja (IDE-Engine) | ja (Inspections) | JVM JDI + Koroutinen-Agent | JDI/Compiler | ktfmt/ktlint Community, Optionen | `kotlinc`/ki | KDoc Markdown, Dokka |
| **Julia** | *Community* (LanguageServer.jl) | nein | wenig | Debugger.jl = *Interpreter*, JIT-Code nicht schrittbar; hält bei Fehlern nur mit `break_on(:error)` | Ausdrücke im Interpreter | JuliaFormatter, Optionen | *REPL ist das Zentrum*, Revise.jl | Docstrings Markdown, Documenter.jl |

**Je Sprache, kurz:**

- **Rust.** rust-analyzer ist ein **zweiter Frontend** — Query-basiert, inkrementell, fehlertolerant.
  Was passt: die **strukturierten Vorschläge** von rustc (`Applicability::MachineApplicable`) sind
  der Mechanismus, mit dem `cargo fix` und der Editor denselben Fix teilen (E2). rustfmt hat einen
  stabilen Optionskern und einen unstabilen Rest — die Erfahrung dort: eine Option ist nie die
  letzte (E6-B). Was nicht passt: der zweite Frontend (Rule 2).
- **Go.** gopls kompiliert **pro Paket** und cacht Snapshots; Modulgraph = Cache-Schlüssel — die
  billigste Inkrementalität, und Lyric hat die Voraussetzung. gofmt ohne Optionen ist Lyrics
  Vorbild. Keine REPL. gopls berechnet für `formatting` **minimale Edits** per Diff (E29). Delve
  ist Community-Org, kein Go-Team-Produkt; `--headless` ist ein Debugger-**Prozess**, der auf
  einen Client lauscht — das ist, was `lyrdbg` heute ist, nicht das Vorbild für einen
  Lauschmodus der VM (E16).
- **Swift.** SourceKit-LSP nutzt den Compiler in-process plus Hintergrund-Index. Der
  LLDB-Evaluator ist ein voller Swift-Compiler im Debugger — mächtig und historisch die fragilste
  Komponente. swift-format ist konfigurierbar — die Gegenentscheidung.
- **C#.** Roslyn: Compiler als Service, Full-Fidelity-Bäume, lazy Sema. Was passt: die
  **Submission-Kette** von C# Interactive (E5), der Debugger-Evaluator aus dem Compiler (E3-C),
  minimale Format-Edits aus dem Baum (E29). Preis des Ganzen: ein Team.
- **Zig.** `zig fmt` ohne Optionen — deckungsgleich. zls ist Community, benutzt aber `std.zig.Ast`
  aus der stdlib, also den Parser des Compilers — kein eigener. Keyword-Completion im Server (E13).
- **Elm.** Reine Sprache, deshalb ist Replay dort korrekt. Ob Lyric die Strategie von Elm
  übernommen hat, ist **nicht belegt** — kein „Elm" in `src/Lyrrepl`, `STATUS.md`, `CHANGELOG.md`,
  `docs/guide` [gemessen: grep]; die Session-Kommentare begründen das Replay aus der
  Deklarationsliste. Die erste Fassung hatte „kopiert" geschrieben — gestrichen.
- **Kotlin.** IDE-first — aber nicht „zwei Typprüfer": das Vor-K2-Plugin nutzte den
  Compiler-Frontend (FE1.0, Descriptors/Resolution) mit IDE-eigener Lazy-Resolution und Caches;
  PSI ist der Syntaxbaum. K2 ersetzte FE1.0 durch FIR + Analysis API — eine Ablösung. Der Preis
  war eine IDE-Engine, die der Compiler nicht bot, nicht ein zweiter Compiler. Lyrics
  JetBrains-Plugin verweigert PSI [gelesen: `README.md:10-13`] — richtig. Der Koroutinen-Debugger
  zeigt suspendierte Koroutinen als eigene Sicht (E15).
- **Julia.** REPL-zentriert, dynamisch. Deckungsgleich: **Debugger.jl interpretiert**, JIT-Code
  ist nicht schrittbar (E11). Nicht deckungsgleich mit E4: Debugger.jl hält bei einem Fehler nur
  mit `break_on(:error)`, nicht per Default. Sampling-Profiler ist Standardbibliothek (E10).

**Wer hält bei einer unbehandelten Exception per Default an?** Rust (LLDB), Go (delve), Swift
(LLDB), C#, Kotlin/JVM (mit Exception-Breakpoint konfigurierbar, unhandled per Default), Zig
(LLDB/GDB) — **sechs**. Elm hat keinen Step-Debugger, Julia hält nur auf Anforderung. Die erste
Fassung hatte „alle acht" geschrieben.

**Lauschende Laufzeiten** (Vorbild für E16-A): Node `--inspect`, Java `-agentlib:jdwp=…,server=y`,
Python `debugpy --listen` — die VM startet den Adapter im Prozess und wartet auf einen Client.
`vsdbg` ist das Gegenteil (ein Debugger, der sich an einen Prozess hängt).

**Zählung „mindestens eine entgegengesetzte Entscheidung":** swift-format/rustfmt/dotnet format
(Optionen), Kotlin (IDE-first), Julia (REPL-zentriert), Zig/Elm/Julia (Server aus der Community),
Go (keine REPL, Debugger aus der Community), Elm (kein Debugger).

---

## 3. Designfragen

Form je Frage: Ist-Stand (belegt) · Optionen mit Vorbild und Preis · Empfehlung · Bruchgrad und
4.x-Warnstufe · Abhängigkeiten · Confidence. „Bruch" heißt: ändert sich für einen Benutzer von
4.x etwas Sichtbares; ein Werkzeug hat keine Migrationsuhr, aber `--check`-Läufe in CI und
Client-Versionen brechen genauso.

### 3.1 Sprachserver

### E1 — Analysemodell des Servers: ganz, abbrechbar, oder inkrementell?

**Ist.** Ganzer Compile pro Tastendruck, nicht abbrechbar, Speicher monoton, Completion kompiliert
ein zweites Mal [gemessen; gelesen: `AnalysisService.cs:47-55`, `CompletionProvider.cs:51`].
**Release-Build gemessen: ~10 %, Boden 65–90 ms** [gemessen: `editor-r5`].

**Optionen.**
- **A — bleibt ganz, wird abbrechbar; Completion aus dem Schnappschuss.** `CancellationToken`
  durch `SourceCompiler` bis Sema; Completion/SignatureHelp aus dem letzten Modell statt
  Zweitcompile, wo der Offset in einen unveränderten Bereich fällt. Vorbild: Elm. Preis: klein;
  Skalierung bleibt O(Module). **Nicht** Teil von A: Release ausliefern als Beschleunigung — es
  beschleunigt nicht messbar.
- **B — Modulgranulare Wiederverwendung (gopls).** Der Modulgraph ist der Cache-Schlüssel: ein
  Modul wird nur neu geparst/geprüft, wenn es oder ein Import sich änderte; Monomorphisierung und
  Lowering bleiben ganz. Preis: `Compilation` muss Modulergebnisse als Werte halten; mittel (eine
  Milestone-Größe); kein zweiter Frontend.
- **C — Query-basiert (rust-analyzer/Salsa).** Faktisch ein zweiter Frontend; Rule 2, ADR.
- **D — Compiler als Service (Roslyn).** Umbau des gesamten Frontends; nicht im Budget.

**Empfehlung.** A sofort (4.x, kein Bruch), B als 5.x-Milestone. Die Modulzahl-Skalierung
[gemessen] und die Release-Messung sagen beide: die Achse ist **B**, nicht Build-Konfiguration.
C und D lösen ein Problem, das Lyric nicht hat.

**Bruch:** nein. **Warnstufe:** keine. **Abhängig von:** E17 (inkrementeller Sync ist die
Voraussetzung für B). **Confidence:** gemessen.

### E2 — Maschinenlesbare Fixes: gehört ein `fix` an die Diagnose?

**Ist.** Notes sind Text [gelesen: `DiagnosticMapper.cs:84-104`, `Lyric.Core/Diagnostic.cs:3-10`];
keine `data`, keine Code Action [gemessen]; `lyrfix` fehlt (v5-Liste D [gelesen:
`docs/Befunde_und_Verbesserungen/lyric-v5-features.md:163`]).

**Optionen.**
- **A — Strukturierte Vorschläge am Compiler-Diagnostic** (rustc `Suggestion` + `Applicability`;
  gopls `SuggestedFix`; Roslyn `CodeFixProvider`): `fixes: [{title, edits: [{span, newText}],
  applicability}]`. LSP mappt nach `codeAction`, `lyrfix` wendet `MachineApplicable` in Batch an,
  `--json` gibt sie aus. Ein Format, drei Verbraucher. Preis: Spec §12 bekommt ein optionales Feld
  (minor); Aufwand pro Code.
- **B — Editor-seitige Heuristik über die Message.** Bricht bei jeder Umformulierung. Nein.
- **C — `lyrfix` als getrenntes Werkzeug mit eigenen Regeln.** Zweiter Mechanismus; Rule 2.

**Empfehlung.** A. Reihenfolge — **korrigiert**: zuerst die Codes des ordentlichen Katalogs
(SEM0002 „did you mean", SEM0071–0073 unbenutzt, SEM0075, SEM0076 Deprecation); die
Migrationsuhren SEM0107–0110 bekommen **keinen** Fix, weil Spec §12.5 es verbietet (E28).
`lyrfix` für die 5.0-Migration wird gegen die **5.0-Fehlercodes** gebaut, nicht gegen die Uhren.

**Bruch:** nein (additiv). **Warnstufe:** keine. **Abhängig von:** Diagnostik-Dossier, E28.
**Confidence:** gemessen.

### E7 — Formatfehler im Editor: `null` oder Meldung?

**Ist.** `null` bei Parse-Fehler [gelesen: `LspServer.cs:962-966`; gemessen]; `lyrfmt` meldet.
**Optionen.** **A** bleibt · **B** `ResponseError` mit der ersten Diagnose als Meldung · **C**
teilweise formatieren bis zur Fehlerstelle (Roslyn kann kaputte Bäume formatieren; Prettier
weigert sich). **Empfehlung:** B — die eine Antwort, die beide Werkzeuge geben können. **Bruch:**
nein. **Abhängig von:** —. **Confidence:** gemessen.

### E8 — Eine Analyseeinheit für alle Werkzeuge

**Ist.** `ProjectFile.Discover` wird von lyrc, lyrtest, lyrls und lyrdbg je selbst gerufen
[gelesen: `DapServer.cs:389-400`, `Lyrtest/Program.cs:77`, `AnalysisService.cs:289`]; `lyric check`
nimmt den Test-Root [gelesen: `guide/20:76-78`], der Server macht daraus keine Einheit (E25); die
REPL zeigt keine Warnungen [gemessen]; das Diagnostic-Objekt wird zweimal serialisiert
(`RenderJson` [gelesen: `Lyric.Core/DiagnosticEngine.cs:178`] und `DiagnosticMapper`); Fähigkeiten
werden je Werkzeug gesetzt (E21).

**Optionen.** **A** ein `AnalysisUnit` in `Lyric.Frontend` (Quellwurzel, Test-Root, native
Wurzeln, Abhängigkeiten, Profil, gewährte Fähigkeiten) — alle fünf Werkzeuge fragen es; ein
JSON-Schema für Diagnosen, aus dem der LSP-Mapper liest. Vorbild: gopls `Snapshot`, Roslyn
`Workspace`. Preis: Refactoring, kein Feature. **B** bleibt. **Empfehlung:** A, in 4.x. Die
Begründung der ersten Fassung („der Server sieht den Test-Root nicht") war falsch; die richtige
steht in E25: Einheitsmitgliedschaft, nicht Importauflösung. **Bruch:** nein. **Abhängig von:**
Build-Dossier, E25, E21. **Confidence:** gemessen.

### E9 — Migrationsuhren im Editor

**Ist.** SEM0107–0110 sind Warnungen ohne Tag, ohne Link, ohne Fix [gelesen: `DiagnosticMapper.cs:70-77`;
Spec §12.5 [gelesen: `spec/12-diagnostics.md:74-109`]]. Die REPL zeigt sie nicht [gemessen].

**Optionen.** **A** `codeDescription.href` auf die Spec-Stelle (billig, sofort) · **B** Code
Action „Fix für 5.0 anwenden" — **verworfen**: §12.5 *„A warning that fired only under one
candidate answer would be that answer, taken quietly"*, und ein Fix, der `mut` setzt oder
umbenennt, **ist** eine Kandidatenantwort; zudem *„It retires WITH its rule"* — der Code, an dem
der Fix hinge, existiert in 5.0 nicht (E28) · **C** eigener Tag: das Protokoll hat keinen;
`Deprecated` wäre eine Lüge. **Empfehlung:** A sofort; keine Fixes an Uhren. **Bruch:** nein.
**Abhängig von:** E28. **Confidence:** gelesen.

### E13 — Client-Strategie: dünn bleiben, was in den Server, und die dritte Grammatik

**Ist.** Zwei dünne Clients, kein PSI [gelesen: `jetbrains README:10-13`], TextMate kanonisch,
Snippets nur in VS Code [gemessen], keine Keyword-Completion [gemessen], keine Tree-sitter-Grammatik.

**Optionen.** **A** dünn bleiben, alles Sprachliche in den Server: Keyword-/Snippet-Completion als
`CompletionItemKind.Keyword`/`Snippet` (gopls, zls); VS-Code-Snippets werden gelöscht. Preis:
klein. **B** Tree-sitter-Grammatik für Zed/Helix/Neovim — ein **zweiter Parser** in anderer
Sprache; Rule 2, ADR. **C** TextMate abschaffen, nur semantische Tokens — das Null-Fenster wird
sichtbar; ohne E20 nicht tragbar. **Empfehlung:** A. **Bruch:** nein. **Abhängig von:** E20.
**Confidence:** gemessen.

### E17 — Protokollhaltung: Sync, Pull-Diagnosen, Progress, Encoding

**Ist.** Full-Sync [gelesen: `LspServer.cs:1008`]; Range-Change korrumpiert still [gemessen];
kein `$/progress`, kein Pull, nur utf-16.

**Optionen.** **A** inkrementeller Sync + Ablehnung nichtkonformer Changes; `$/progress`;
`positionEncoding`-Verhandlung (utf-8 für Zed/Helix). **B** Full bleibt, Range-Change →
`ResponseError`. **Empfehlung:** B sofort, A mit E1-B. **Bruch:** nein. **Abhängig von:** E1.
**Confidence:** gemessen.

### E18 — Versionsvertrag zwischen Clients und Toolchain

**Ist.** `serverInfo.version` angezeigt, nicht geprüft [gelesen: `extension.js:185`];
Mindestversionen in Prosa [gelesen: `jetbrains README`]; Clients versionieren unabhängig
[gelesen: `STATUS.md:2098-2101`].

**Optionen.** **A** Client prüft `serverInfo.version` gegen seine Mindestversion und meldet.
**B** Server meldet Feature-Flags unter `experimental` in `initialize`. **C** nichts.
**Empfehlung:** A jetzt, B wenn E2/E24 Features bringen, die ein alter Server nicht hat.
**Bruch:** nein. **Confidence:** gelesen.

### E19 — Hover- und Completion-Semantik

**Ist.** Hover auf Funktions-, Typ- und Feld-Deklarationsnamen `null`, auf Bindungsnamen und
Verwendungen Antwort [gemessen: `editor-r5/lsp_hover.py`]; Keywords fehlen [gemessen];
generischer Aufruf zeigt die deklarierte Signatur [gelesen: `vscode README`]; Inlay nur für
Bindungen [gelesen: `InlayHintProvider.cs:10-15`].

**Optionen.** **A** Hover auf Deklaration = Signatur + Doku (jede Vergleichssprache); Keywords aus
der Lexer-Tabelle; Substitution im Hover, wo die Sema sie kennt (`HoverProvider.cs:85-87` tut es
für Ausdrücke). **B** bleibt. **Empfehlung:** A, alles 4.x. **Bruch:** nein. **Confidence:**
gemessen.

### E20 — Das Null-Fenster: warten, Fallback, oder Fortschritt melden

**Ist.** Bis zur ersten Diagnose antworten alle Anfragen außer Completion `null` [gemessen:
724–918 ms Einzeldatei, bis 3,6 s Projekt]; kein Signal an den Client.

**Optionen.** **A** Anfragen bis zum ersten Schnappschuss halten, `$/progress`. **B**
Syntax-only-Antworten aus einem Parse-Schnappschuss für `documentSymbol`/`foldingRange`/
`semanticTokens` (Lexik) — `DocumentSymbolProvider` ist bereits syntaxbasiert [gelesen:
`DocumentSymbolProvider.cs:10-13`]. **C** bleibt. **Empfehlung:** B + A. **Bruch:** nein.
**Abhängig von:** E1. **Confidence:** gemessen.

### E24 — Tests im Editor: über welches Protokollstück? *(neu)*

**Ist.** Kein `codeLens` (`-32601`), kein `workspace/executeCommand` [gemessen]; beide Clients
ohne Test-Integration [gemessen: grep]; `lyric test` ohne maschinenlesbare Ausgabe [behauptet];
Testentdeckung im Runner über die Attributtabelle `module.Attributes.OnFunctions("Test")`
[gelesen: `Lyrtest/Program.cs:168`].

**Optionen.**
- **A — Standard-LSP: `codeLens` „Run test | Debug test" über jeder `@Test`-Funktion + `workspace/
  executeCommand` (`lyric.runTest`), Ergebnis als Client-Terminal-Lauf von `lyric test --filter`.**
  Vorbild: gopls (`run test`-CodeLens). Preis: klein; kein Explorer-Baum, kein Ergebnis im Gutter.
- **B — Eigene Anfrage `lyric/tests` (Entdeckung aus der Attributtabelle) + `lyric test --json`
  (Ergebnisstrom), Clients füllen VS Code Test Controller / JetBrains Test-Runner.** Vorbild:
  rust-analyzer (`experimental/runnables`), C# DevKit. Preis: zwei Client-Implementierungen, ein
  JSON-Vertrag für `lyric test`; mittel.
- **C — Client parst die `lyric test`-Textausgabe.** Zwei Parser, brechen bei jeder
  Umformulierung. Nein.
- **D — bewusst nichts; CLI ist das Werkzeug.** Preis: „Test rot im Editor" bleibt ein Feature,
  das jede Vergleichssprache hat.

**Empfehlung.** A in 4.x (CodeLens ist Standard, `--filter` existiert [gelesen: `guide/20:70-72`]),
B als 5.x-Slice zusammen mit E23 (Debug test braucht denselben Einstieg) — und `--json` an
`lyric test` gehört ins CLI-Dossier. **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:**
E23, E25, CLI-Dossier. **Confidence:** gemessen/gelesen.

### E25 — Einheitsmitgliedschaft: Test-Root, `build.lyr`, und die Rename-Meldung *(neu)*

**Ist.** Diagnosen einer Testdatei fließen, Importe lösen auf [gemessen: `lsp_test.py`];
`references` aus `src/` finden die Testdatei nicht, `rename` aus ihr wird mit „not part of a
project" abgewiesen [gemessen: `lsp_refs.py`; gelesen: `AnalysisService.cs:429-437`,
`RenameProvider.cs:129-137`]. `build.lyr` nimmt denselben Einzeldatei-Zweig [gelesen]. `lyric check`
kompiliert Quell- und Test-Root **zusammen** [gelesen: `guide/20:76-78`]; ein Produktions-Build
sieht Tests nie [gelesen: `guide/20:3-5`].

**Optionen.**
- **A — Einheit = Quellwurzel + Test-Root, wie `lyric check`; `build.lyr` als eigene Einheit mit
  `std.build`.** `references`/`rename`/`workspace/symbol` sehen Tests; ein Rename aus der Testdatei
  ist projektweit. Preis: `CollectRoots` bekommt eine zweite Wurzel, `IsUnder` zwei; die
  Analyse-Einheit heißt dann Projekt, nicht Quellwurzel. Klein.
- **B — Tests als eigene Einheit, die die Quelleinheit importiert; Referenzen über die Einheit
  hinweg per Index.** Vorbild: SourceKit-LSP (indexstore-db). Preis: zweiter Mechanismus für
  „wo kommt der Name vor" (Compile + Index). Rule 2.
- **C — bleibt; nur die Rename-Meldung wird ehrlich** („the test root is not part of the analysis
  unit; rename from the source root"). Preis: null; die Lücke bleibt.

**Empfehlung.** A in 4.x; C als Sofortmaßnahme, falls A wartet. Die Meldung heute ist eine
Falschaussage über die Projektdatei des Benutzers — das wiegt schwerer als die fehlende Referenz.
**Bruch:** nein (mehr Treffer). **Warnstufe:** keine. **Abhängig von:** E8. **Confidence:** gemessen.

### E29 — Form der Formatier-Edits: Gesamtdokument oder minimale Edits? *(neu)*

**Ist.** `textDocument/formatting` liefert **einen** `TextEdit` von `0:0` bis Dokumentende
[gelesen: `LspServer.cs:975-976`; gemessen: `lsp_ca.out`]. `lyrfmt --stdin` liefert ebenfalls das
Ganze — dort ist das richtig.

**Optionen.**
- **A — Gesamt-Edit bleibt Vertrag.** Preis: Cursor, Selektion, Folding-Zustand und
  Undo-Granularität hängen davon ab, ob der Client den Edit selbst diffed [behauptet: VS Code
  wendet `TextEdit`s literal an; ein Gesamt-Edit ersetzt den Puffer]; Format-on-Save springt.
- **B — Server diffed formatiert gegen original und liefert minimale Edits (Zeilen-Diff, dann
  Zeichen-Diff je geänderter Zeile).** Vorbild: gopls (`diff.ComputeEdits`), rust-analyzer
  (rustfmt-Ausgabe wird gediffed), Roslyn (Änderungen pro Token aus dem Baum). Preis: ein
  Diff-Algorithmus (~100–200 Zeilen, Myers auf Zeilen genügt) und ein Test „Edits angewandt =
  formatierter Text". Klein.
- **C — Edits aus dem Formatter selbst (er weiß, welche Anweisung er umbrach).** Preis: der
  Formatter muss Spans mitführen; mittel; nichts, was B nicht liefert.

**Empfehlung.** B, 4.x. Das ist ein reiner Werkzeugfix ohne Vertragsänderung: die Summe der
Edits ist dieselbe Form. **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:** —.
**Confidence:** gemessen/behauptet (Client-Verhalten).

### E33 — Diagnosen geschlossener Dateien und zwei `lyric.json` in einem Workspace *(neu)*

**Ist.** Der Projektlauf publiziert **für jede** Datei der Einheit, offen oder nicht [gelesen:
`AnalysisService.cs:496-521`]; zurückgezogen wird, was die Einheit verlassen hat [gelesen:
`:524-534`], nicht beim Schließen eines Puffers; die Einheit ist die `lyric.json`, die
`ProjectFile.Discover` **aufwärts** vom Dokument findet [gelesen: `:282-289`] — zwei Projekte sind
zwei Einheiten, `workspaceFolders` unbeteiligt und `-32601` [gemessen]. Nicht gemessen: ob der
Editor Diagnosen zu nie geöffneten Dateien zeigt [behauptet: VS Code und JetBrains tun es].

**Optionen.**
- **A — bleibt und wird ausgesprochen:** projektweite Diagnosen sind der Vertrag (`vscode README`
  sagt es bereits); zurückgezogen beim Verlassen der Einheit oder beim `shutdown`. Vorbild: gopls
  (Diagnosen für das ganze Paket). Preis: ein Guide-Absatz.
- **B — nur offene Dateien publizieren, Rest über Pull-Diagnostics (`textDocument/diagnostic`,
  `workspace/diagnostic`).** Vorbild: LSP 3.17, Roslyn-LSP. Preis: Pull-Implementierung; Clients
  ohne Pull sehen nichts Projektweites.
- **C — beides: Push für offene, `workspace/diagnostic` für den Rest.** Preis: zwei Wege für eine
  Antwort. Rule 2 im Kleinen.

**Empfehlung.** A; dazu `workspaceFolders` nicht implementieren, sondern dokumentieren, dass die
nächste `lyric.json` die Einheit bestimmt. **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:**
E25 (was „Einheit" umfasst). **Confidence:** gelesen.

### 3.2 Debugger

### E3 — Debugger-Evaluate: Pfad, Ausdruck, oder kompilierte Bedingung?

**Ist.** `Split('.')` [gelesen: `DebugController.cs:532`]; `xs.[0]` [gemessen]; Bedingungen still
ignoriert [gemessen]. v5-Liste D wünscht Ausdrücke [gelesen: `lyric-v5-features.md:164`]; Guide 21
begründet den Verzicht [gelesen: `guide/21:50-52`].

**Optionen.**
- **A — Pfad bleibt, wird ehrlich.** Pfadgrammatik = Lyric-Teilmenge (`v.x`, `xs[0]`); `xs.[0]`
  verschwindet; `condition`/`hitCondition`/`logMessage` mit `verified: false` + `message`
  abgewiesen. Preis: Stunden.
- **B — Zweiter Evaluator** (delve). Rule 2. Nein.
- **C — Bedingung als kompilierte Funktion** mit dem vorhandenen Compiler, geladen in dieselbe VM,
  gerufen bei jedem Treffer. Vorbild: Roslyn-EE. Preis: „Modul nachladen in laufende VM"
  (dieselbe Fähigkeit wie E5-B); Panik in der Bedingung → `verified: false`-Nachmeldung. Mittel.
- **D — `hitCondition` allein** sofort. Trivial.

**Empfehlung.** A + D in 4.x, C als 5.x-Ziel. B nie. **Bruch:** nein. **Abhängig von:** E5.
**Confidence:** gemessen.

### E4 — Panik im Debugger: anhalten oder beenden?

**Ist.** `exited(101)` + `terminated`, kein `stopped(exception)` [gemessen; gelesen:
`DapServer.cs:423-436`, `DebugModel.cs:4-22`]. Guide 21:63 dokumentiert es.

**Optionen.**
- **A — `stopped(reason: exception)` vor dem Abwickeln**, `exceptionInfo`, Frame inspizierbar;
  `continue` beendet. Vorbild: die sechs Debugger, die es per Default tun (§2). Preis: der
  Mechanismus aus E34 — **ein Hook an der einen Stelle, die den Frame-Stack schon hält**
  (`Interpreter.cs:304-310`), plus ein `StopReason.Exception`. Die erste Fassung hatte „ein Hook,
  kein Umbau" ohne Beleg gesagt; der Beleg steht jetzt in E34.
- **B — dazu Exception-Filter** (`exceptionBreakpointFilters`: `panic` immer; `uncaught` für
  einen `throw` ohne `catch` im Stack; `all`). Preis: „wird gefangen?" ist der Stack-Scan des
  Unwinding.
- **C — bleibt.** Der JetBrains-Client hält einen unsichtbaren Breakpoint-Typ am Leben.

**Empfehlung.** A sofort, B in 5.0 mit der `defer`-Entscheidung (SEM0110). **Bruch:** nein.
**Abhängig von:** E34, Fehler-Dossier. **Confidence:** gemessen.

### E11 — Debug-Profil = Interpreter: Regel oder Zufall?

**Ist.** JIT aus unter Controller/Budget [gelesen: `LoadedProgram.cs:61-69`, `Interpreter.cs:66-68`];
steht im Parameterkommentar, nicht in STATUS §Design decisions [gelesen: grep].

**Optionen.** **A** Regel aussprechen: „debuggen, budgetieren, profilieren = interpretiert; das
Release-Profil ist JIT-fähig" — Julia-Modell. Preis: ein Satz und ein Guide-Absatz. **B** JIT mit
Safepoints und Deoptimierung (HotSpot/V8 — dort ist auch Stepping in JIT-Code möglich, über
Deopt). Preis: der größte Umbau der VM. **C** JIT-Code mit Zeilentabellen nur für Breakpoints,
kein Stepping. Die erste Fassung hatte das „JDI-Stil" genannt — falsch, HotSpot/JDI steppt in
JIT-Code per Deoptimierung; C ist ein Modell **ohne** Vorbild und eine halbe Antwort.
**Empfehlung:** A in 5.0 als Design-Entscheid; B/C nie ohne Messung. **Bruch:** nein.
**Abhängig von:** Laufzeit-Dossier. **Confidence:** gelesen.

### E14 — Quellkarte: relativ zum Programm, zur Projektwurzel, oder absolut im Modul?

**Ist.** Basis = Verzeichnis der Programmdatei [gelesen: `DapServer.cs:369`], Rückfall auf den
Dateinamen [gelesen: `:502-503`], Frame ohne Pfad beim Start aus `build/` [gemessen]. Relative
Pfade seit **1.1.0** [gelesen: `CHANGELOG.md:3568-3570`], absolute verboten [gelesen:
`docs/Bytecode.md:90`], relativiert in `SourceMapBuilder.cs:120-128`.

**Optionen.** **A** Adapter nimmt die `lyric.json`-Quellwurzel als Basis, wenn eine da ist —
Werkzeugfix. **B** Modul trägt seine Kompilierwurzel als Feld (Format-Änderung: Major). **C**
absolute Pfade (Go `-trimpath` ist die Gegenrichtung; Bytecode.md:90 verbietet es).
**Empfehlung:** A in 4.x; B nur, wenn das Bytecode-Dossier ohnehin ein 5.0-Format schneidet. Dazu
`noDebug` ehrlich: ohne Controller laufen. **Bruch:** A nein; B major. **Abhängig von:**
Bytecode-Dossier. **Confidence:** gemessen.

### E15 — Koroutinen und Tasks im Debugger

**Ist.** Ein Thread `main` [gelesen: `DapServer.cs:205-207`]; laufende Kette als logischer Stack
[gelesen: `STATUS.md:841-844`]; **suspendierte** Tasks unsichtbar [behauptet]. stdlib nicht
schrittbar [gelesen: `guide/21:146-147`].

**Optionen.** **A** ein Thread bleibt; suspendierte Tasks als Scope „Tasks" mit ihrem Stack.
**B** jeder Task als DAP-Thread (delve: Goroutinen; Kotlin: Koroutinen-Agent). Preis:
`allThreadsStopped`-Semantik; für eine Single-Thread-Sprache eine Lüge im Protokoll. **C** nichts.
**Empfehlung:** A in 5.x nach dem Nebenläufigkeits-Dossier; dazu stdlib mit **Pfaden** in der
Karte, damit `stepIn` in `List.push` möglich wird. **Bruch:** nein. **Abhängig von:**
Nebenläufigkeits-Dossier. **Confidence:** gelesen/behauptet.

### E16 — Attach an eigenständige Programme: was ein Lauschmodus an der Semantik ändert

**Ist.** Attach nur an einen Host [gelesen: `guide/21:104-116`]; `lyrvm run` hat keinen
Lauschmodus [gemessen]. Unter `launch` gehören stdout/stderr dem Adapter (output-Events), stdin
ist `Null` [gelesen: `DapServer.cs:330-334`]; unter Host-Attach gehören sie dem Host [gelesen:
`guide/21:128-129`].

**Optionen.**
- **A — `lyrvm run --debug-listen <port>`:** die VM startet den Adapter im Prozess und wartet auf
  `attach`. Vorbild: **Node `--inspect`, Java JDWP `server=y`, Python `debugpy --listen`** — nicht
  delve `--headless` (das ist ein Debugger-Prozess, also `lyrdbg` selbst) und nicht `vsdbg` (hängt
  sich an). **Was sich ändert**, und was die erste Fassung mit „ohne neue Mechanik" verschwiegen
  hat: im Lauschmodus gehören stdout/stderr/stdin **der Konsole**, nicht dem Adapter — der
  Adapter darf sie nicht umleiten; `output`-Events entfallen (wie beim Host-Attach); der Editor
  zeigt die Konsole des Prozesses. Preis: eine dritte Betriebsart des Adapters (launch: umleiten;
  host-attach: Host-Writer; listen: Konsole), ein Socket-Transport im Adapter (heute nur stdio
  [gelesen: `Lyrdbg/Program.cs`]), Dokumentation. Klein bis mittel.
- **B — `runInTerminal`-Reverse-Request beim `launch`:** der Adapter bittet den Editor, `lyrvm run
  --debug-listen` in dessen Terminal zu starten, und attacht. Vorbild: debugpy/VS Code Python
  („console": "integratedTerminal"). Das löst E22 (stdin) gleich mit. Preis: A plus der
  Reverse-Request. 
- **C — bleibt Host-only.**

**Empfehlung.** A als Mechanismus, B als Default-Form des `launch` in 5.x, sobald A steht. Damit
gibt es **einen** Weg, ein Programm zu debuggen (die VM lauscht, der Adapter attacht), und
`launch` wird ein Komfort darüber. **Bruch:** minor (output-Events werden zur Terminal-Ausgabe;
Clients, die die Debug-Konsole lesen, sehen sie anders). **Warnstufe:** CHANGELOG. **Abhängig
von:** E14, E22, CLI-Dossier. **Confidence:** gelesen.

### E21 — Fähigkeiten unter den Werkzeugen: Standalone-Regel oder Host-Regel? *(neu)*

**Ist.** Die Regel: *„A standalone run grants everything; a host grants explicitly"* [gelesen:
`guide/13:628-629`]. Stand je Werkzeug:

| Werkzeug | Gewährt | Option |
|---|---|---|
| `lyrvm run` / `lyric run` | `Capability.All` | `--grant` [gelesen: `Lyrvm/Program.cs:49-52`] |
| `lyrdbg launch` | `Capability.All` — `Load(module, natives)` ohne `granted` [gelesen: `DapServer.cs:336`, `LoadedProgram.cs:72-73`] | **keine**; `"grant"` im Request wird ignoriert [gemessen: `dap_cap.py`, Datei geschrieben] |
| `lyrdbg attach` (Host) | was der Host gewährte | Host-Sache |
| REPL | `Capability.All` [gelesen: `Session.cs:181`] | **keine** [gelesen: `Program.cs:109-126`] |
| `lyrtest` | `Capability.All` [gelesen: `Lyrtest/Program.cs:134`] | **keine** |

Alle vier Standalone-Werkzeuge sind **konsistent** mit der Regel; keins außer `lyrvm` kann sie
einschränken. Die erste Fassung hatte die REPL als Sandbox-Konflikt gerahmt — falsch — und
`lyrdbg` gar nicht genannt.

**Optionen.**
- **A — Standalone-Regel bleibt; jede Oberfläche bekommt die Option:** `launch.json` `"grant":
  "fileAccess,netAccess"` (Semantik von `--grant`), `lyrrepl --grant`, `lyric test --grant`;
  Default `all`. Vorbild: Deno (`--allow-*` an jedem Verb, auch `deno test`). Preis: drei
  Optionen, ein gemeinsamer Parser (existiert in `Lyrvm`).
- **B — Host-Regel für Werkzeuge: nichts ohne Angabe.** Preis: jede F5-Sitzung bricht mit
  `LYR-CAP0001`; die Regel in Guide 13 kippt. Nein.
- **C — `lyric.json` deklariert `capabilities`, alle Werkzeuge gewähren genau das; ohne Feld die
  Standalone-Regel.** Vorbild: Deno `deno.json` permissions, Browser-Manifeste. Preis: ein
  Projektfeld (Build-Dossier), ein Leser (E8); Bytecode trägt die Anforderung bereits
  (`module.Capabilities` [gelesen: `LoadedProgram.cs:76`]), also ist „Projekt gewährt X" gegen
  „Modul braucht Y" beim Laden prüfbar. Der Test-Runner läuft dann mit den Rechten des Projekts —
  genau das, was ein Test prüfen soll.

**Empfehlung.** A in 4.x (kein Bruch, schließt die drei Löcher), C in 5.x über E8 als **der eine
Mechanismus** (Projektdatei statt drei Optionen). **Bruch:** nein. **Warnstufe:** keine.
**Abhängig von:** E8, Build-Dossier, Sandbox-Regel im Laufzeit-Dossier. **Confidence:** gemessen.

### E22 — stdin unter dem Debugger *(neu)*

**Ist.** Das debuggte Programm liest `TextReader.Null` — stilles EOF [gelesen: `DapServer.cs:330-334`
*„The debuggee reads no stdin for the same reason"*]; Guide 21 erwähnt es nicht [gelesen:
`guide/21:60-64` nennt nur stdout/stderr]. `runInTerminal` wird nie gesendet [gemessen:
Fähigkeitenliste].

**Optionen.**
- **A — dokumentierte Grenze:** Guide 21 bekommt den Satz „unter `launch` liest das Programm
  EOF; ein Programm, das stdin braucht, wird über attach debuggt". Preis: ein Satz. Die Grenze
  bleibt.
- **B — `runInTerminal`:** das Programm läuft im Editor-Terminal mit eigenem stdin/stdout, der
  Adapter attacht (E16-B). Vorbild: debugpy, VS Code Node („console": "integratedTerminal"),
  delve über VS Code Go. Preis: E16-A plus Reverse-Request; stdout wandert aus der Debug-Konsole
  ins Terminal.
- **C — stdin-Weiterleitung über eine eigene Anfrage** (`lyric/stdin`). DAP hat keinen
  stdin-Kanal; jeder Client bräuchte Sonderkode. Nein.

**Empfehlung.** A sofort (die Grenze ist heute undokumentiert), B mit E16. **Bruch:** nein.
**Warnstufe:** keine. **Abhängig von:** E16. **Confidence:** gelesen/gemessen.

### E23 — Einen einzelnen `@Test` debuggen *(neu)*

**Ist.** `launch` kennt `program`, `args`, `stopOnEntry`, `noDebug` [gelesen: `DapServer.cs:303-315`;
`guide/21:8-18`]; ein Testmodul hat kein `main`; `lyrtest` ruft Tests über die Embedding-API —
`vm.Instantiate(module).CallVoid(test)` je Test in frischer Instanz [gelesen:
`Lyrtest/Program.cs:16-19`, `:168-178`] — und kompiliert Test-Root mit Quellwurzel.

**Optionen.**
- **A — `launch` bekommt `"test": "module.function"`:** der Adapter kompiliert wie `lyrtest`
  (Test-Root + Quellwurzel aus `lyric.json`), lädt unter Controller, ruft das Call-Handle statt
  `main`. Vorbild: rust-analyzer `Debug test`-Lens (cargo test-Binary mit Filter), Go
  (`dlv test`). Preis: ein Feld, der Einstiegspfad, den `lyrtest` schon hat, wird geteilt (E8);
  klein bis mittel.
- **B — synthetischer `main`, der den Test ruft**, als Quelle generiert. Preis: generierter Text
  in Diagnosen (die REPL zeigt, wie das endet); zweiter Einstiegsmechanismus neben dem
  Call-Handle. Nein.
- **C — gar nicht; Tests werden mit `console.println` debuggt.** Preis: „Debug test" fehlt, wo
  jede Vergleichssprache es hat.

**Empfehlung.** A in 5.x zusammen mit E24-B (die CodeLens „Debug test" sendet genau dieses
`launch`). **Bruch:** nein (additiv). **Warnstufe:** keine. **Abhängig von:** E8, E21 (Rechte
des Tests), E24. **Confidence:** gelesen.

### E34 — Panik-Stopp: welcher Mechanismus hält den Frame-Stack am Leben? *(neu)*

**Ist.** 45 Wurfstellen in sechs Dateien [gemessen: grep, §1.2]; der Controller sieht die Panik
als gefangene Exception nach `RunEntry` [gelesen: `DebugController.cs:132-139`] und Frames nur an
Instruktionsgrenzen [gelesen: `:157`]. **Aber** `Interpreter.cs:304-310` fängt die Panik bereits
**mit lebendem `frames`-Stack und aktuellem `frame`** — dort wird der Backtrace gebaut. Natives
werfen aus der Schleife heraus und landen im selben Catch; JIT-Code läuft unter dem Debugger nicht
[gelesen: `Interpreter.cs:66-68`].

**Optionen.**
- **A — Policy-Aufruf im vorhandenen Catch:** `IExecutionPolicy` [gelesen: `Interpreter.cs:16`]
  bekommt `OnPanic(frames, frame, panic)`; `DebugPolicy` parkt den Thread dort genau wie
  `OnInstruction` es tut, publiziert `StopEvent(Exception, …)`, und wirft nach `continue` weiter.
  `ReleasePolicy`/`BudgetPolicy`/`ProfilePolicy` sind No-ops. Vorbild: CPython (`sys.settrace`
  „exception"-Ereignis vor dem Unwinding), JVM (`ExceptionEvent` vor dem Handler-Lookup). Preis:
  ein Interface-Member, ein Catch-Zweig, ein `StopReason`; die Scopes/Variables-Anfragen laufen
  gegen denselben `frames`-Stack wie an einem Breakpoint. **Klein — jetzt belegt.**
- **B — Policy-Aufruf vor jedem Throw** (45 Stellen). Preis: 45 Änderungen, die nächste
  Wurfstelle vergisst es. Nein.
- **C — ein Panic-Helfer als einziger Wurfort** (`Panic.Throw(code, message)`), der die Policy
  fragt. Preis: 45 Umstellungen plus die Regel „nie `throw new LyricPanic`" — ein Lint, den
  niemand erzwingt; und der Helfer hat keinen Frame-Zugriff, den hat nur die Schleife. Nein.

Was A **nicht** abdeckt: eine Panik im **Globalinitialisierer** (läuft vor dem Attach, dokumentiert
[gelesen: `guide/21:141-143`]) und ein Budget-Ende (`ExecutionBudget` wirft ebenfalls `LyricPanic`
[gemessen: 1 Wurfstelle] — unter Debugger gibt es kein Budget [gelesen: `guide/21:100-102`]).

**Empfehlung.** A, 4.x — es ist die Voraussetzung für E4-A und E4-B (`uncaught`-Filter: der
Catch bei `Interpreter.cs:584/614` `UncaughtException` ist derselbe Weg). **Bruch:** nein.
**Warnstufe:** keine. **Abhängig von:** —. **Confidence:** gelesen/gemessen.

### 3.3 REPL

### E5 — REPL: Replay, persistente VM, oder streichen?

**Ist.** Replay [gemessen], Klassifikator-Loch [gemessen], keine Warnungen, alle Fähigkeiten,
keine Mehrzeiligkeit [gemessen/gelesen, §1.3]. STATUS nennt es Rearchitektur [gelesen:
`STATUS.md:2236-2245`].

**Optionen.**
- **A — Replay bleibt, Fehler werden behoben** (Klassifikator E26, Warnungen, `:help`-Text,
  `--grant` E21, `var`-Meldung E27). Preis: Stunden. Das Modell bleibt für eine unreine Sprache
  falsch.
- **B — Submission-Kette (Roslyn C# Interactive).** Jede Eingabe wird ein Modul `repl[n]`, das
  `repl[1..n-1]` importiert; die VM lebt, Globals bleiben, Deklarationen laufen **einmal**;
  Redefinition = neues Modul, das schattet. Preis: `LoadedProgram` muss ein Modul in eine laufende
  VM linken — dieselbe Fähigkeit wie E3-C und Hot-Reload. Mehrzeiligkeit: Eingabe endet, wenn der
  Parser keinen `Eof`-Fehler mehr meldet. Mittel bis groß.
- **C — streichen.** Go hat keine; `lyric run` mit einem Einzeiler deckt „mal schnell" ab. Preis:
  ein Werkzeug weniger, ein Rule-3-Artefakt zurückgenommen.

**Empfehlung.** B, **wenn** E3-C ohnehin gebaut wird; sonst C. Nicht A als Endzustand: eine
REPL, die Seiteneffekte wiederholt, lehrt Verhalten, das die Sprache nicht hat. **Sofort** (4.x,
unabhängig von der Wahl): Klassifikator aus dem Parser (E26), Warnungen rendern, `--grant` (E21),
`var`-Meldung (E27), `:help` ehrlich. Die erste Fassung hatte „`var`-Bug beheben" verlangt —
das ist ohne globale `var` nicht möglich (E27).

**Bruch:** minor (REPL-Verhalten, kein Sprachbruch). **Warnstufe:** keine. **Abhängig von:** E3,
E26, E27, Laufzeit-Dossier. **Confidence:** gemessen.

### E26 — Deklarationsmenge der REPL: Präfixliste oder Parser? *(neu)*

**Ist.** Präfixliste `fn class struct enum interface extend import module pub let` [gelesen:
`Session.cs:42-44`]; `TopLevelDecl` der Grammatik umfasst zusätzlich `TypeAlias` (`type`, `opaque
type`), `ExternDecl`, `{ Attribute }` [gelesen: `docs/Grammar.md:164-176`]. `type Meters = int;`
→ 5 Fehler als Anweisung, `let d: Meters = 3;` → `RES0002` [gemessen: `editor-r5`].

**Optionen.**
- **A — Liste erweitern** um `type `, `opaque `, `extern `, `@`. Preis: Minuten; die nächste
  Grammatikänderung vergisst es wieder (das ist genau, was passiert ist: `type` kam nach der
  Liste).
- **B — Klassifikation aus dem Parser:** die Eingabe wird als Modul geparst; ist das Ergebnis
  eine Folge von `TopLevelDecl` ohne Fehler, ist sie Deklaration, sonst Anweisung/Ausdruck. Ein
  Mechanismus, der mit der Grammatik wandert. Vorbild: Julia/Elm (Parser entscheidet), C#
  Interactive (Submission-Parser mit Top-Level-Statements). Preis: ein Parse mehr pro Eingabe
  (Millisekunden); der Fehlerfall „beide Versuche scheitern" braucht die bessere Diagnose
  (heute wird die schlechtere gezeigt [gelesen: `Session.cs:119-131`]).
- **C — kommt mit E5-B umsonst:** in der Submission-Kette ist jede Eingabe ein Modul, und
  Anweisungen sind Top-Level-Statements dieses Moduls — es gibt nichts zu klassifizieren.

**Empfehlung.** B jetzt (4.x, Werkzeugfix), C übernimmt es, falls E5-B kommt. **Bruch:** nein.
**Warnstufe:** keine. **Abhängig von:** E5. **Confidence:** gemessen.

### E27 — Veränderbarer Sitzungszustand: `var` in der REPL? *(neu)*

**Ist.** Globale `var` sind illegal: `GlobalBinding = BindingStmt . (* 'let' only *)` [gelesen:
`docs/Grammar.md:174`], `LYR-PAR0027` [gemessen]. Die REPL akzeptiert `var v = 5;` als Anweisung
in `main` und vergisst sie **still** [gemessen]; `:help` sagt „Declarations accumulate" ohne zu
sagen, dass `var` keine ist [gelesen: `Program.cs:121`].

**Optionen.**
- **A — kein `var` in der REPL; die Sprache gilt.** `var x = …;` als Eingabe bekommt eine
  REPL-eigene Meldung („`var` is a statement here and does not persist; use `let`"), `:help`
  sagt es. Vorbild: Go (kein REPL), Elm (keine Mutation). Preis: eine Meldung.
- **B — `var` lebt in der persistenten VM (E5-B) als Local des lebenden Rahmens:** die
  Sitzung ist ein langlebiger Block, jede Eingabe wird in ihn kompiliert; `var` ist dort so legal
  wie in jedem Funktionsrumpf. Vorbild: Python/Julia-Top-Level, C# Interactive (Top-Level-
  Variablen sind Felder der Submission). Preis: ein zweiter Scope-Begriff („Sitzungsrahmen") neben
  Modul-Global — **Rule-2-nah**, braucht einen Satz in Guide/Spec: „die REPL ist ein Block, kein
  Modul". Und: mit E5-B als Submission-**Modulen** ist B nicht erreichbar, weil ein Modul kein
  `var` hat — B verlangt die Block-Variante von E5-B.
- **C — `var` als Deklaration mit Replay** (jede Eingabe generiert `let` und Zuweisung). Erfindet
  globale `var` durch die Hintertür. Nein.

**Empfehlung.** A sofort. B nur, wenn E5-B als **Block-Modell** gebaut wird — und dann muss
„Deklaration" im `:help` neu definiert werden („alles, was du eingibst, bleibt; Ausdrücke werden
gedruckt"). **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:** E5, E26. **Confidence:**
gemessen.

### 3.4 Fixes, Formatter, Doku

### E28 — Fixes und Migrationsuhren: welche Codes dürfen `MachineApplicable` tragen? *(neu)*

**Ist.** Spec §12.5 [gelesen: `spec/12-diagnostics.md:93-105`]: eine Migrationswarnung *„does not
presume the answer … A warning that fired only under one candidate answer would be that answer,
taken quietly"*, und *„It retires WITH its rule"* — der Code wird nicht als Fehler wiederverwendet.
`lyrfix` steht auf der v5-Liste als „für die 5.0-Migration" [gelesen: `lyric-v5-features.md:163`].
Heute gibt es keine Fixes [gemessen].

**Was folgt.** Ein `MachineApplicable`-Fix an SEM0107 („umbenennen"), 0108 („`mut` setzen"),
0109 („`var`") **ist** eine Kandidatenantwort in Editier-Form. Und der Code, an dem der Fix
hinge, ist in 5.0 retiriert. Die erste Fassung (E2-Reihenfolge, E9-B) hatte genau das
vorgeschlagen — verworfen.

**Optionen.**
- **A — Fixes nur am ordentlichen Katalog** (SEM0002, 0071–0073, 0075, 0076 …); Migrationsuhren
  tragen `codeDescription.href` und sonst nichts. `lyrfix` für die Migration entsteht mit den
  **5.0-Fehlercodes**, deren Fix die entschiedene Regel ist. Vorbild: `cargo fix --edition`
  arbeitet gegen die Lints der **neuen** Edition, nicht gegen Vorwarnungen. Preis: keiner; die
  Migration hat vor 5.0 keinen Knopf.
- **B — antwortneutrale Fixes an Uhren**, mit eigener Applicability `Neutral`: ein Edit, nach dem
  das Programm **unter jeder Kandidatenantwort dasselbe** bedeutet (0107: die zweite Bindung
  umbenennen ist neutral — das Programm bedeutet vorher und nachher dasselbe, egal ob Rebinding
  bleibt oder fällt). Preis: Neutralität ist **pro Code zu beweisen** und braucht einen Satz in
  §12.5 („eine Uhr darf einen Edit vorschlagen, der die Frage aus dem Programm entfernt"); für
  0108 (`mut` setzen ist die eine Antwort), 0109, 0110 gibt es keinen neutralen Edit.
- **C — Fixes als 5.0-Antwort vor der Entscheidung.** Verstößt gegen §12.5. Nein.

**Empfehlung.** A. B nur mit dem Spec-Satz und nur für 0107; die Spec-Runde entscheidet, ob ein
neutraler Edit „die Antwort vorwegnimmt" — der Verfasser meint nein, aber es ist eine Spec-Frage,
keine Werkzeugfrage. **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:** E2, Spec-Runde,
Diagnostik-Dossier. **Confidence:** gelesen.

### E6 — Formatter: eine Form ohne Optionen — und was der AST verliert

**Ist.** Keine Optionen [gelesen: `Lyrfmt/Program.cs:172`]; Klammern und Kommentare im Ausdruck
gehen den Weg des AST [gemessen; gelesen: `AstFormatter.cs:11-24`]; die `catch (_: Boom)`-Episode
[gelesen: `STATUS.md:1127-1131`, `CHANGELOG.md:1102-1104`].

**Optionen.**
- **A — bleibt genau so** (gofmt, zig fmt, elm-format). Die zwei dokumentierten Verluste bleiben.
- **B — eine Option Zeilenbreite.** Vorbild: rustfmt `max_width` (eine seiner **stabilen**
  Optionen). Preis: erste Verhandlung; eine Option ist nie die letzte.
- **C — Full-Fidelity für die zwei Verluste:** Klammer-Flag am Ausdruck und Kommentar-Trivia an
  Ausdrucksknoten, nur vom Formatter gelesen. Vorbild: Roslyn-Trivia, in klein. Preis:
  Parser-Änderung; die Spec-Regel §2.2 wird zu „redundante Klammern bleiben" — Spec-Änderung.
- **D — Semantik-Erhalt-Test:** Korpus formatiert, **beide** Fassungen bis zum Bytecode kompiliert
  und verglichen (Bytecode ist deterministisch [gelesen: `docs/Bytecode.md:86`]). Preis: ein Test.

**Empfehlung.** A + D sofort; C nur die Kommentar-Hälfte. B nie. **Bruch:** C-Kommentar = minor:
`--check` in CI wird einmal rot (Präzedenzfall v2.5.1 [gelesen: `CHANGELOG.md:2251`]).
**Warnstufe:** keine; CHANGELOG-Eintrag nach E30. **Abhängig von:** Parser, E30. **Confidence:**
gemessen.

### E30 — Stabilitätsvertrag der Form: gehört die Formatter-Ausgabe zur Version? *(neu)*

**Ist.** Drei Formänderungen seit 2.5: v2.5.1 (Ketten; CI „may report files that were clean
before" [gelesen: `CHANGELOG.md:2237-2251`]), v3.6.1 (Bedeutungsfehler behoben [`:1102`]),
v3.9.0 (Attributlisten [`:972`]). Guide 18 nennt die Form „the tool's contract" [gelesen:
`guide/18:10`], sagt aber nicht, **welche Version** den Vertrag hält. `toolchain` in `lyric.json`
pinnt die Baubarkeit, „a minimum, nothing more" [gelesen: `guide/12:122-124`]. Das Repo hält sich
selbst an `--check` [gelesen: `guide/18:92-94`].

**Optionen.**
- **A — Form ist Teil des Minor-Vertrags mit Ausnahme:** innerhalb eines Majors ändert sich die
  Form nur, um einen **Bedeutungsfehler** zu beheben (3.6.1) oder eine dokumentierte Regel
  einzulösen (2.5.1, 3.9.0), jeweils mit CHANGELOG-Satz „`--check` kann rot werden". Vorbild:
  gofmt (Form praktisch eingefroren; Änderungen in Release-Notes), Prettier (Form ändert sich in
  Majors, dokumentiert). Preis: ein Absatz in Guide 18 und die Disziplin, ihn zu halten.
- **B — Form pro Major eingefroren, `--check` bricht nie in einem Minor.** Preis: ein
  Formatierungsfehler wie 3.6.1 muss auf 5.0 warten — unvertretbar, weil er Bedeutung änderte.
- **C — `toolchain` pinnt auch die Form:** `lyric fmt` einer neueren Toolchain formatiert ein
  Projekt mit `"toolchain": "4.5"` in der 4.5-Form. Preis: der Formatter muss alte Formen
  mitführen — Optionen durch die Hintertür (rustfmt `edition`/`style_edition` ist genau das).
  Nein.

**Empfehlung.** A, ausgesprochen in Guide 18 und CONTRIBUTING (Release-Checkliste: „Formänderung
→ CHANGELOG-Satz"). `toolchain` bleibt Baubarkeit. **Bruch:** nein (beschreibt die Praxis).
**Warnstufe:** keine. **Abhängig von:** E6. **Confidence:** gelesen.

### E12 — `///` bekommt eine Sprache, und Projekte bekommen `lyric doc`

**Ist.** Zwei Deutungen [gelesen: `HoverProvider.cs:66-70`, `MarkdownRenderer.cs:29-33`], keine
Spec-Aussage [gelesen: `spec/01-lexical.md:45-47`], kein Projektmodus [gemessen], `IsPackable=false`
[gelesen: `DocGen.csproj:7`].

**Optionen.**
- **A — Spec sagt: CommonMark plus `[Name]`-Links auf Deklarationen** (rustdoc Intra-Doc-Links,
  DocC). `Lyric.Docs` wird Bibliothek, `lyric doc` schreibt die Site eines Projekts; Hover rendert
  dieselbe Pipeline. Preis: Markdig im Shipping-Pfad — **oder** ein CommonMark-Subset selbst.
  Spec-Änderung minor.
- **B — Go-Stil: Plain Text mit Konventionen**, `lyric fmt` formatiert Doc-Kommentare. Preis:
  keine Tabellen, keine Links.
- **C — bleibt.**

**Empfehlung.** A mit Subset-Parser (kein Host-Trick). Die Links brauchen den Resolver — DocGen
bleibt dann nicht „parsing only". Was ein Link in einem Codeblock bedeutet, entscheidet E31.
**Bruch:** nein (additiv; eine `///`-Zeile mit `|` oder `#` rendert im Hover anders).
**Warnstufe:** keine. **Abhängig von:** CLI-Dossier, Build-Dossier, E31. **Confidence:** gemessen.

### E31 — Doc-Tests: werden Codeblöcke in `///` kompiliert, ausgeführt, oder nie? *(neu)*

**Ist.** Keine Doc-Tests [gemessen: kein Konsument von `///` außer Hover und DocGen]. v5-Liste
Punkt 19, P2: „Code in `///` wird von `lyric test` ausgeführt" [gelesen:
`lyric-v5-features.md:51`, `PLAN.md:289`]. Die Guide-Snippets werden von der Suite **kompiliert**
[gelesen: `CLAUDE.md` §Pflichtlektüre; behauptet: nicht ausgeführt].

**Optionen.**
- **A — Kompilieren, nicht ausführen, durch `lyric doc`:** jeder ```` ```lyr ````-Block in `///`
  wird als Modul mit `import <eigenes Modul>` geprüft; Fehler = Doc-Fehler. Vorbild: die eigene
  Guide-Suite; Elm (Doc-Beispiele werden geprüft). Preis: ein Prüfschritt in DocGen; kein
  Testlauf, keine Fähigkeitenfrage.
- **B — Ausführen durch `lyric test` (Rust-doctests):** jeder Block wird ein `@Test` mit
  `assertEq`-Konvention (`// => 4`). Vorbild: Rust, Elixir (`doctest`). Preis: Doc-Blöcke laufen
  mit den Rechten des Test-Runners (E21); ein Block, der ein Beispiel **zeigt**, muss plötzlich
  **gelten** — Rust löst das mit `ignore`/`no_run`-Annotationen, also einer Mini-Sprache in `///`.
- **C — nie.** Preis: Beispiele veralten still.

**Empfehlung.** A in 5.x mit E12 (dieselbe Pipeline liest die Blöcke); B nicht — die
Annotationssprache ist ein zweiter Mechanismus neben `@Test`. Für `[Name]`-Links (E12-A) heißt
das: ein Link wird beim Rendern aufgelöst, ein Codeblock beim Kompilieren; beides sind Diagnosen
von `lyric doc`, keine Tests. **Bruch:** nein. **Warnstufe:** keine. **Abhängig von:** E12, E21.
**Confidence:** gelesen.

### E32 — Navigation in Abhängigkeiten ohne Quelle *(neu)*

**Ist.** `dependencies` in `lyric.json` zeigen auf **Quellverzeichnisse** anderer Projekte
[gelesen: `guide/12:116-117` *„maps a module path segment to the directory of another project"*];
der Server reicht `DependencyRoots` durch [gelesen: `AnalysisService.cs:384`], also funktionieren
`definition`/`hover` in eine Abhängigkeit **mit Quelle** wie im eigenen Projekt [behauptet, nicht
gemessen]. Ein **kompiliertes Paket mit Header** existiert in 4.6 nicht — M37 (4.5.0) lieferte
Projektsystem, Build v2 und Profile [gelesen: `STATUS.md:79-80`]; das Paketformat „Quelle + nativ
+ kompiliertes Lyric mit Header" ist Teil der Stufenfolge B–F der Design-Runde 2026-09
[behauptet: aus den Entscheidungsnotizen, nicht aus dem Repo]. Die Frage ist also eine an das
Build-Dossier, mit einer Werkzeug-Folge.

**Optionen.**
- **A — Header trägt Signaturen und `///`-Doku; Server antwortet aus dem Header:** `hover` zeigt
  Signatur + Doku, `definition` springt in eine **synthetische Header-Datei** (read-only, aus dem
  Header gerendert). Vorbild: Roslyn („Metadata as Source" aus der Assembly, XML-Doc daneben),
  Kotlin (Decompiler-Stub), Go (Quelle liegt im Modul-Cache immer bei). Preis: das Paketformat
  muss Doku mitführen (Bytes), der Server einen Header-Renderer.
- **B — Pakete tragen ihre Quelle immer mit (Go-Modell):** kein Header nötig, Navigation trivial.
  Preis: das Paketformat wird groß; „ohne Quelle" gibt es dann nicht.
- **C — nichts:** `definition` antwortet `null`, `hover` zeigt die Signatur aus dem Modul
  (Typtabelle ist im Bytecode [gelesen: `spec/13-bytecode.md:493-520`]), keine Doku.

**Empfehlung.** A, entschieden im Build-Dossier: das Header-Format muss `///` **von Anfang an**
tragen, sonst ist der Hover in Paketen leer und bleibt es. Bis das Format existiert: B ist der
Ist-Stand (Quellabhängigkeiten) und reicht. **Bruch:** nein. **Warnstufe:** keine. **Abhängig
von:** Build-Dossier (Paketformat), E12. **Confidence:** gelesen/behauptet.

### 3.5 Profiler, Laufzeit

### E10 — Profiler: anschließen, umbenennen, oder streichen?

**Ist.** Zähler **pro Funktion** vorhanden (`Prepared.Executed`) [gelesen: `LoadedProgram.cs:196-201`],
ungelesen, Variable kollidiert mit dem Profilnamen [gemessen]. v5-Liste D nennt Profiler und
Coverage [gelesen: `lyric-v5-features.md:165-166`].

**Optionen.**
- **A — Umbenennen und anschließen:** `LYRIC_TRACE=instructions` plus `lyrvm run
  --profile-instructions` → Hotspots auf stderr. **Coverage ist ein neuer Zähler** — pro Block in
  der Dispatch-Schleife (Blöcke sind Sprungziele [gelesen: `docs/Bytecode.md:640`]), nicht der
  vorhandene pro Funktion; die erste Fassung hatte „derselbe Zähler" geschrieben. Preis: Tage
  für den Profiler-Anschluss, ein weiterer Slice für Coverage (`lyrtest --coverage`).
- **B — Sampling-Profiler** (Go pprof, Julia Profile): Timer-Thread liest fremde Frames — unter
  der Single-Thread-Regel eine Abweichung mit ADR.
- **C — streichen.**

**Empfehlung.** Kollision sofort beheben (4.x; die VM-Variable ist undokumentiert). A als
5.x-Slice; B erst, wenn JIT der Auslieferungsstandard ist. **Bruch:** minor (undokumentierte
Variable). **Warnstufe:** keine. **Abhängig von:** E11. **Confidence:** gemessen.

---

## 4. Was wir übernehmen sollten

| Von | Was | Für |
|---|---|---|
| rustc / `cargo fix` | strukturierte Vorschläge mit `Applicability`; Migration gegen die **neuen** Lints, nicht gegen Vorwarnungen | E2, E28, `lyrfix` |
| gopls | Modulgranulare Snapshots; Keyword-Completion vom Server; `SuggestedFix`; **minimale Format-Edits per Diff**; `run test`-CodeLens; projektweite Diagnosen | E1-B, E13, E29, E24, E33 |
| gofmt / zig fmt / elm-format | keine Optionen — bestätigt | E6 |
| Go 1.19 | Doc-Kommentare vom Formatter formatiert | E12-Nebenschauplatz |
| Roslyn | Submission-Kette; Evaluator aus dem Compiler; Trivia nur so weit, wie der Formatter sie braucht; „Metadata as Source" für Pakete ohne Quelle | E5, E3-C, E6-C, E32 |
| Node `--inspect` / JDWP `server=y` / debugpy | die **Laufzeit** lauscht, der Adapter attacht; `runInTerminal` für stdin | E16, E22 |
| delve / LLDB / C# | Panik = Stopp mit Frame (per Default) | E4 |
| CPython `settrace` / JVM `ExceptionEvent` | Exception-Ereignis **vor** dem Unwinding aus der Schleife heraus | E34 |
| Deno | `--allow-*` an jedem Verb, auch `test`; Permissions in der Projektdatei | E21 |
| Julia | „debuggen = interpretiert" ausgesprochen; Profiler als Standardwerkzeug | E11, E10 |
| Kotlin (Koroutinen-Debugger) | suspendierte Koroutinen als eigene Sicht, nicht als Threads | E15 |
| rustdoc / DocC | `///` = Markdown mit `[Name]`-Links, Site pro Projekt; Doc-Beispiele **geprüft** | E12, E31 |
| SourceKit-LSP | Background-Index — erst wenn E1-B nicht reicht | E1 (später) |
| Elm | Reinheit ist die Voraussetzung für Replay — Lyric hat sie nicht | E5 |

**Was wir bewusst nicht übernehmen:** den zweiten Frontend (rust-analyzer), die IDE-eigene
Engine (Kotlin bis K2), den zweiten Evaluator (delve), den Full-Fidelity-Umbau (Roslyn),
Formatter-Optionen (rustfmt, swift-format, dotnet format), Rust-Doctests mit Annotationssprache,
die Community-Delegation des Servers (Zig, Elm, Julia) — Lyric ist ein Ein-Personen-Projekt, das
genau deshalb den einen Compiler überall benutzen muss.

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept):**
- E3-B (zweiter Evaluator), E1-C (Salsa), E13-B (Tree-sitter), E2-C (`lyrfix` mit eigenen
  Regeln), E25-B (Index neben Compile), E31-B (Doctest-Annotationen neben `@Test`), E33-C
  (Push + Pull) sind je ein zweiter Mechanismus. Die Empfehlungen umgehen alle.
- E27-B („Sitzungsrahmen" als Scope neben Modul) ist Rule-2-nah und braucht mindestens einen
  Guide-Satz, eher ein ADR.
- E6-C (Trivia/Klammerknoten) ist eine **Spec-Änderung** an §2.2. Die Spec nennt dort den
  Formatter beim Namen — und an fünf Stellen den Debugger (§04:65, §13:69/493/520/546/561); ein
  Format-Entscheid (E14-B, E15 stdlib-Pfade) berührt die Debugger-Sätze in §13.
- E12: Markdig im Shipping-Pfad wäre die erste Laufzeit-NuGet-Abhängigkeit; der Subset-Parser ist
  der Preis, das zu vermeiden.

**Mit der Capability-Sandbox (Standalone-Regel, guide/13:628):** Kein Bruch — REPL, `lyrdbg
launch` und `lyrtest` folgen der Regel. Der Konflikt ist ein anderer: **drei Werkzeuge haben
keine Option**, die Regel zu unterschreiten (E21), und ein Test läuft mit mehr Rechten, als das
Projekt je gewähren würde. E21-C (Projektfeld) gehört ins Build-Dossier und ins Sandbox-Kapitel
des Laufzeit-Dossiers.

**Mit Spec §12.5 (Migrationsuhren):** E28 — kein `MachineApplicable`-Fix an SEM0107–0110;
`lyrfix` entsteht mit den 5.0-Codes. Die REPL zeigt die Uhren nicht [gemessen] — das ist der eine
Befund, der vor jeder Designentscheidung behoben gehört. Ob ein **antwortneutraler** Edit (E28-B)
zulässig ist, ist eine Spec-Frage für die Spec-Runde.

**Mit „single-threaded plus Koroutinen":** E10-B (Sampling) und E15-B (Tasks als Threads). Beide
nicht empfohlen.

**Mit der JIT-Roadmap (Laufzeit-Dossier):** E11 zementiert „Debugger = Interpreter". Wenn JIT
Auslieferungsstandard wird, muss dort stehen, dass Debug-Profil, Budget und Profiler interpretiert
bleiben.

**Mit dem Bytecode-Dossier:** E14-B, E15 (stdlib-Pfade) sind Format-Fragen; E34 braucht keinen
Format-Eingriff.

**Mit dem Diagnostik-Dossier:** E2 hängt an einem `fixes`-Feld am `Diagnostic` und an Spec §12;
E28 an einem Satz in §12.5.

**Mit dem CLI-Dossier:** `lyric doc` (E12), `lyrvm run --debug-listen` (E16), `--profile-
instructions` (E10), `--grant` an REPL und `lyric test` (E21), `lyric test --json` (E24), die
`LYRIC_PROFILE`-Kollision (E10, **jetzt**).

**Mit dem Build-Dossier:** E8/E25 (Einheit = `lyric.json` mit Test-Root), E21-C (`capabilities`
im Projekt), E32 (Header-Format trägt `///`).

**Mit dem Nebenläufigkeits-Dossier:** E15.

---

## 6. Nach der Kritik geändert

**Falsche Aussagen korrigiert (alle nachgemessen, `editor-r5/`):**

1. **Test-Root (§1.1, §1.8, E8):** Diagnosen fließen, Importe lösen auf [gemessen]; die Lücke ist
   die Einheitsmitgliedschaft (`references`, `rename`) und die falsche Rename-Meldung. Neue
   Frage E25; E8-Begründung ersetzt.
2. **`var` in der REPL (§1.3, E5):** kein Bug — globale `var` sind illegal (`LYR-PAR0027`,
   Grammar.md:174). Der echte Defekt ist der Klassifikator (`type`/`opaque`/`extern`/`@`), 5 Fehler
   für `type Meters = int;` [gemessen]. Neue Fragen E26, E27.
3. **Hover auf Deklarationsnamen (§1.1, E19):** präzisiert — Funktions-, Typ- **und** Feldnamen
   `null`, Bindungsnamen antworten [gemessen mit Kontrolle, 11 Positionen].
4. **„Das eine Werkzeug, das die Spec beim Namen nennt" (§1.4, §5):** gestrichen; die Spec nennt
   den Debugger an fünf Stellen.
5. **Source-Map „relativ seit 1.0.1 [guide/21:68-69]" (E14):** ersetzt durch 1.1.0
   [CHANGELOG.md:3568-3570], Bytecode.md:90, SourceMapBuilder.cs:120-128.
6. **REPL als Sandbox-Konflikt (§5, E5):** umgerahmt — Standalone-Regel, fehlende Option; `lyrdbg
   launch` und `lyrtest` in dieselbe Antwort genommen [gemessen: `dap_cap.py`]. Neue Frage E21.
7. **„21 Methoden" → 22 `LspMethods` + `exit` = 23** [gelesen].
8. **„11 Tests" → 10 `[Fact]`, 0 `[Theory]`** [gemessen].
9. **„Release kann das drehen" / E1-A „Release ausliefern … schneller":** gemessen, zwei Läufe je
   Konfiguration: ~10 % im Rauschen; Debounce in keiner Konfiguration dominant. E1-Empfehlung
   auf B (Module) geschärft.
10. **Zitat `lyric-v5-features.md`** jetzt mit Pfad `docs/Befunde_und_Verbesserungen/`.

**Vergleiche korrigiert:** Kurzfassung „Go, Swift, C#" → „Swift, C#" (delve ist Community-Org);
„alle acht halten bei Exception" → sechs (Elm ohne Step-Debugger, Julia nur mit `break_on`);
rustfmt hat stabile Optionen; Kotlin-Beschreibung (FE1.0 in der IDE, K2 als Ablösung, PSI ist
der Syntaxbaum); Zig-Zeile konsistent (zls nutzt `std.zig.Ast`); Elm „kopiert" gestrichen
[gemessen: grep leer]; E16-Vorbild delve/vsdbg → Node `--inspect`/JDWP/debugpy; E11-C „JDI-Stil"
gestrichen (HotSpot steppt in JIT-Code per Deopt).

**Schwache Empfehlungen ersetzt:** E2-Reihenfolge und E9-B (Fixes an Uhren) verworfen wegen Spec
§12.5 → E28; E4-A „ein Hook" jetzt belegt über `Interpreter.cs:304-310` → E34; E5 „var-Bug
sofort" → Klassifikator + Meldung; E10-A „derselbe Zähler" → neuer Blockzähler; E16-A „ohne neue
Mechanik" → dritte Betriebsart mit Konsolen-Semantik ausgesprochen.

**Neue Fragen (14):** E21 Fähigkeiten unter den Werkzeugen · E22 stdin unter dem Debugger · E23
einen Test debuggen · E24 Tests im Editor · E25 Einheitsmitgliedschaft Test-Root/`build.lyr` ·
E26 Deklarationsmenge der REPL · E27 `var` in der REPL · E28 Fixes und Migrationsuhren · E29
Form der Formatier-Edits · E30 Stabilitätsvertrag der Form · E31 Doc-Tests · E32 Navigation in
Pakete ohne Quelle · E33 Diagnosen geschlossener Dateien · E34 Panik-Stopp-Mechanismus.

**Nicht übernommen, mit Grund:** Die Kritik nennt das kompilierte Paket mit Header „M37" — M37
lieferte Projektsystem, Build v2 und Profile [gelesen: `STATUS.md:79-80`]; `dependencies` sind
Quellverzeichnisse [gelesen: `guide/12:116-117`]. E32 ist darum als Frage an das Build-Dossier
formuliert, nicht als Ist-Lücke des Servers.

---

## Anhang — Probenverzeichnis

| Verzeichnis | Inhalt |
|---|---|
| `probes/editor/` | LSP-Latenz (`probe_lat.py`), Fähigkeiten (`probe_feat.py`), DAP-Panik/Evaluate, REPL-Eingaben, Tiefenlimit, Formatter-Roundtrip, `dapclient.py` |
| `probes/editor2/` | Speicherprobe, Null-Fenster, Formatter-Kommentarfälle, REPL-Warnungen/Numerierung, DAP-Projekt/Bytecode-Start, Profiler-Bytecode |
| `probes/editor-r3/` | untitled, Range-didChange, Hover, Keywords; DAP-Bedingungen, `noDebug`, Evaluate-Formen; Kommentar-Idempotenz |
| `probes/editor-r4/` | `LYRIC_PROFILE`-Kollision, `docgen model`, DocGen auf Benutzerprojekt |
| `probes/editor-review-r2/` | Proben der Kritik: `lsp_test.py`, `lsp_refs.py`, `lsp_ca.py`, `dap_cap.py`, `lsp_lat_rel.py`, `repl_alias.in/out`, `topvar.lyr`, Fixture `pj/` |
| `probes/editor-r5/` | **heute, Nachprüfung mit `ERWARTUNG.txt`:** Test-Root-Diagnosen und Kontrolle, `references`/`rename`, `topvar` → PAR0027, REPL-Alias (10 Fehler), DAP-Capability mit Kontrolle `--grant none`, Latenz Release vs. Debug (je zwei Läufe), Hover an 11 Positionen (`lsp_hover.py`, Kontrolllauf fehlerfrei) |
