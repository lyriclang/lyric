# Lyric Bug Hunt — gemeinsame Taskliste

Format pro Fund (ein Block, mit `flock` anhängen, nie den Inhalt anderer umschreiben):

### [<agent>] <kurzer Titel>
- Severity: CRITICAL | HIGH | MEDIUM | LOW
- Datei: <pfad relativ zum Repo>:<zeile>  (mehrere Zeilen erlaubt)
- Ebene(n): lexer | parser | sema | ir | emit | optimizer | vm | cli | embedding | spec | diagnostics
- Beschreibung: was ist falsch, was wäre korrekt
- Repro: minimales Lyric-Programm / Kommando / Test, der es zeigt (verifiziert: ja/nein)
- Betroffene Teammitglieder informiert: <namen oder "-">


### [cli-security] `lyric run app.lyr --grant none` schränkt NICHTS ein — Runtime-Optionen nach einer .lyr-Quelle gehen an den Compiler statt an die VM
- Severity: HIGH
- Datei: src/Lyric.Cli/Program.cs:247, src/Lyric.Cli/Program.cs:265-269
- Ebene(n): cli
- Beschreibung: `Run` schiebt alle Optionen hinter dem Dateiargument (`passThrough`) in den `lyrc build`-Aufruf und ruft `Execute(selection, module, [], …)` mit LEERER Optionsliste auf. Auf dem .lyrbc-Pfad (Zeile 253) landen dieselben Optionen bei `lyrvm`. Folge: `--grant none` (die einzige Sandbox-Option des Runners) wird bei einer Quelle vom Compiler stillschweigend ignoriert, das Programm läuft mit `Capability.All`. Die Hilfe verspricht „Every other option is passed straight to the tool that runs the command". Korrekt: Runtime-Optionen (`--grant`, ggf. `--json/--quiet`) an lyrvm weiterreichen bzw. Optionen, die keiner der beiden Tools kennt, ablehnen.
- Repro (verifiziert: ja): Programm mit `import std.io.file; file.text("x")`; `lyric run readfile.lyr --grant none` → liest die Datei, exit 0. Dagegen `lyrc build readfile.lyr -o r.lyrbc && lyric run r.lyrbc --grant none` → `error[LYR-CAP0001]`.
- Betroffene Teammitglieder informiert: vm-bytecode (Info), embedding-parity (Info)

### [cli-security] `lyrc build x.lyr --output ""` (leerer Pfad) → unbehandelte ArgumentException, Prozess-Abbruch mit Exit 134
- Severity: HIGH
- Datei: src/Lyrc/Program.cs:97-105
- Ebene(n): cli
- Beschreibung: Der Schreibpfad fängt nur IOException/UnauthorizedAccessException. `File.WriteAllBytes("")` wirft ArgumentException („The value cannot be an empty string"), ebenso ein Pfad mit NUL-Byte. Statt `LYR-CLI0008` + Exit 1 gibt es einen Stacktrace und Exit 134 (SIGABRT). Dasselbe Muster: src/Lyrpack/Program.cs:244 (`Path.GetFullPath(output)` bei `-o ""`), src/Lyrtest/Program.cs:51 und src/Lyrbuild/Program.cs:55 (`Path.GetFullPath("")` bei leerem Verzeichnisargument) — siehe Folge-Eintrag nach Verifikation.
- Repro (verifiziert: ja): `lyrc build examples/hello.lyr --output ""` → `Unhandled exception. System.ArgumentException … at Lyric.Cli.Compiler.Program.Build … line 72` (Zeilennummer im Trace ist die PDB-Zeile; Quelle: Zeile 99), exit 134.
- Betroffene Teammitglieder informiert: -

### [cli-security] lyrvm liest `--grant` auch HINTER dem `--`-Trenner (Programmargumente werden zu Runtime-Optionen)
- Severity: MEDIUM
- Datei: src/Lyrvm/Program.cs:57-70
- Ebene(n): cli, vm
- Beschreibung: `Array.IndexOf(args, "--grant")` durchsucht die gesamte Argumentliste, während `ProgramArguments` (Zeile 147) und `Flag` (Zeile 154) korrekt am `--` stoppen. Ein Lyric-Programm, das selbst eine Option `--grant` entgegennehmen soll, bekommt sie nie: `lyrvm run app.lyrbc -- --grant bogus` endet als Usage-Fehler (Exit 2), `-- --grant none` setzt die Capabilities des Runners. Verletzt den Runner-Vertrag Bytecode.md §8.5 („everything after the first `--` belongs to the program").
- Repro (verifiziert: ja): `lyrc build examples/hello.lyr -o h.lyrbc && lyrvm run h.lyrbc -- --grant bogus` → `error[LYR-CLI0003]: unknown capability in 'bogus'`, exit 2.
- Betroffene Teammitglieder informiert: vm-bytecode

### [cli-security] `--verbose`-Zeittabelle ist auf Linux um Faktor 100 zu groß (Stopwatch-Ticks als TimeSpan-Ticks)
- Severity: MEDIUM
- Datei: src/Lyric.Core/TerminalOutput.cs:414, src/Lyric.Core/TerminalOutput.cs:436
- Ebene(n): cli
- Beschreibung: `_currentStartTicks = _total.ElapsedTicks` (Stopwatch-Ticks, auf Linux 1 ns) wird per `TimeSpan.FromTicks(...)` (100-ns-Ticks) in eine Dauer verwandelt. Auf Windows (Frequency 10 MHz) stimmt es zufällig, auf Linux/macOS sind die Phasenzeiten 100× zu groß, während „total" (über `_total.Elapsed`) korrekt ist. Korrekt: `TimeSpan.FromSeconds((now - start) / (double)Stopwatch.Frequency)` bzw. `Stopwatch.GetElapsedTime(start)`.
- Repro (verifiziert: ja): `lyrc build examples/hello.lyr --verbose` → z.B. `parse 5279.2 ms … lower 17804.3 ms … total 558.3 ms` (Phasen summieren sich auf > 47 s bei 0,56 s total).
- Betroffene Teammitglieder informiert: -

### [cli-security] `lyric run --quiet app.lyr`: eine Option vor dem Dateiargument wird als Dateiname genommen, Fehlermeldung nennt `-o`
- Severity: MEDIUM
- Datei: src/Lyric.Cli/Program.cs:246-247, src/Lyric.Cli/Program.cs:266
- Ebene(n): cli, diagnostics
- Beschreibung: `path = positional[1]` ohne Prüfung auf ein führendes `-`. `--quiet` wird zur „Datei", `app.lyr` zum Pass-Through; lyrc erhält `build --quiet -o <tmp> --quiet app.lyr`, ToolOptions entfernt beide `--quiet`, und lyrc versucht die Datei `-o` zu lesen. Korrekt: Option in Dateiposition mit LYR-CLI0002/0003 ablehnen oder Optionen vor der Datei akzeptieren. Gleiches Muster in `lyrc build -o out.lyrbc app.lyr` (Lyrc/Program.cs:235: `args[1]` ist die Datei, Ergebnis „failed to read file: -o").
- Repro (verifiziert: ja): `lyric run --quiet examples/hello.lyr` → `error[LYR-CLI0001]: failed to read file: -o`, exit 1 (statt Usage-Fehler, exit 2).
- Betroffene Teammitglieder informiert: diagnostics

### [optimizer] ScalarReplacement: Feldschreibzugriff über ein VOR der Neuzuweisung geladenes Objekt trifft das NEUE Objekt
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/ScalarReplacement.cs:467-513 (CommitPathIsClean), :413-420 (Rewrite LoadField/StoreField → Feld-Locals), :292-319 (Gruppenbildung)
- Ebene(n): optimizer
- Beschreibung: Eine Gruppe (ein Local mit mehreren Allokationen, z.B. `var p = P{..}; ...; p = P{..}`) teilt sich EINEN Satz Feld-Locals für alle Objekte, die je in dem Local lagen. `CommitPathIsClean` prüft nur, dass zwischen `newobj` und `store p` kein abgeleiteter Temp (Load von `p`) benutzt wird — NICHT, dass ein vor der Neuzuweisung geladener Temp NACH dem Commit noch benutzt wird. Das Lowering von `p.x = <rhs>` lädt das Zielobjekt aber VOR der rhs (FunctionLowerer.cs:1899-1906, `ResolveFieldAccess` zuerst). Bei `p.x = (p = P{x=10,y=20}).y` schreibt der Interpreter unoptimiert in das ALTE (nun unerreichbare) Objekt → p.x bleibt 10; optimiert wird `storefield t_alt.x` zu `store l8(p.x)` und p.x wird 20. Gilt für struct und class gleichermaßen. Korrekt wäre: eine Gruppe verwerfen, sobald ein abgeleiteter Temp eines Loads über einen Commit hinweg lebt (Use nach einem späteren `store` in dasselbe Local), oder pro Allokation eigene Feld-Locals statt pro Local.
- Repro: tests/OptDiff/cases/t_assign_expr.lyr (in meinem Worktree) — optimiert: `struct p.x=20 p.y=20` / `class c.x=20 c.y=20`; unoptimiert: `struct p.x=10 p.y=20` / `class c.x=10 c.y=20`. Optimiertes IR zeigt `t4 = load l9; store l8, t4` (l8/l9 = p.x/p.y-Feldlocals). (verifiziert: ja, Interpreter und JIT identisch je Variante)
- Betroffene Teammitglieder informiert: ir-codegen (Auswertungsreihenfolge Zielobjekt-vor-rhs ist die Voraussetzung), cross-layer-reviewer

### [cli-security] Terminal-Injection: Diagnostik-Textausgabe und Panic-Meldung geben Steuerzeichen (ESC, BEL, OSC) aus Quelltext, Dateinamen und Bezeichnern ungefiltert aus
- Severity: HIGH
- Datei: src/Lyric.Core/DiagnosticEngine.cs:92-100 (Pfad + Quellzeile), src/Lyric.Core/DiagnosticEngine.cs:84 (Message), src/Lyric.Vm/VmHost.cs:42-47 (Panic-Meldung), src/Lyrrepl/Session.cs:311, src/Lyrtest/Program.cs:148
- Ebene(n): cli, diagnostics, vm
- Beschreibung: `RenderText` schreibt `GetPath(...)`, `GetLineText(...)` und `diagnostic.Message` roh auf stderr. Ein Quelltext (z.B. aus einem fremden Repo oder ein Paste) mit `"\x1b]0;PWNED\x07"` oder `\x1b[2J` in einem String-Literal setzt beim bloßen `lyrc check` den Terminaltitel bzw. löscht den Bildschirm; ein Bezeichner aus rohem ESC landet in der Message (`binding '\x1b' needs a type`); der Dateiname ebenso; `panic("\x1b[2J…")` gibt die Sequenz über `panic [LYR-VM0011]: …` aus. Die JSON-Ausgabe escaped < 0x20 korrekt (DiagnosticEngine.cs:203) — nur der Textpfad ist offen. Korrekt: C0-Steuerzeichen (außer Tab), DEL und C1/U+2028/2029/Bidi-Steuerzeichen in Quellzeile, Pfad und Message für die Textausgabe sichtbar machen (`^[`, `\u{1b}`), wie der Lexer es in LEX0001 bereits tut („unexpected character U+001b").
- Repro (verifiziert: ja): `printf 'fn main(): int {\n    let s = "\033]0;PWNED\007\033[2J"; return nope;\n}\n' > a.lyr; lyrc check a.lyr` → Quellzeile mit rohen ESC/BEL auf stderr (cat -v zeigt `^[]0;PWNED^G^[[2J`). Ebenso `lyrc check $'\e[31mevil.lyr'` (Pfad) und `panic("\x1b[2Jboom")` unter `lyric run`.
- Betroffene Teammitglieder informiert: diagnostics, vm-bytecode

### [cli-security] Stack Overflow (Prozess-Abbruch, Exit 134) bei tief verschachtelten Eingaben — Parser, Typ-Parser, Sema; betrifft lyrc, lyrfmt, lyrrepl, lyrls, lyrdbg
- Severity: HIGH
- Datei: src/Lyric.Frontend/Parsing/Parser.cs (ParsePrimary/ParsePrefix/ParseBlock/Typ-Parsing, rekursiver Abstieg ohne Tiefenlimit), src/Lyric.Frontend/Sema/TypeChecker.cs (CheckBinary, rekursiv)
- Ebene(n): parser, sema, cli
- Beschreibung: Kein Tiefenlimit im rekursiven Abstieg. 50 000 `(`, 20 000 `{`, 200 000 `-`, 5 000 `List<` oder eine Kette `1+1+…` mit 200 000 Gliedern (die den Parser überlebt, dann aber `TypeChecker.CheckBinary` rekursiv sprengt) beenden den Prozess mit „Stack overflow" statt mit einer Diagnose. Für `lyrls`/`lyrdbg` heißt das: ein Paste im Editor tötet den Language Server. Korrekt: Tiefenzähler mit Diagnose (z.B. LYR-PAR „expression nesting too deep") in Parser (Ausdruck, Block, Typ) und ein iterativer/limitierter Pfad in Sema/Lowering.
- Repro (verifiziert: ja): `python3 -c "print('fn main(): int {\n    return ' + '('*50000 + '1' + ')'*50000 + ';\n}')" > deep.lyr; lyrc check deep.lyr` → `Stack overflow. Repeated 2989 times: at Lyric.Parsing.Parser.ParsePrimary()`, exit 134. Ebenso `'1+'*200000` → `TypeChecker.CheckBinary`, `'-'*200000` → `ParsePrefix`, `'{'*20000` → `ParseBlock`, `'List<'*5000` → TokenBuffer im Typ-Parser. `lyrfmt --check deep.lyr` identisch.
- Betroffene Teammitglieder informiert: lexer-parser, semantic

### [cli-security] Leeres Pfadargument (`""`) → unbehandelte ArgumentException in lyrc check/build/lower/parse, lyrpack -o, lyrbuild, lyrtest
- Severity: HIGH
- Datei: src/Lyrc/Program.cs:205 (`Path.GetFullPath(path)` in Options), src/Lyrc/Program.cs:99 (WriteAllBytes), src/Lyrpack/Program.cs:244, src/Lyrbuild/Program.cs:55, src/Lyrtest/Program.cs:51
- Ebene(n): cli
- Beschreibung: `Path.GetFullPath("")` und `File.WriteAllBytes("")` werfen ArgumentException, die nirgends gefangen wird → Stacktrace, Exit 134. Ein leeres Argument entsteht leicht aus einer leeren Shell-Variable (`lyrc check "$FILE"`). lyrfmt, lyrvm und `lyric new` behandeln denselben Fall sauber (Exit 1/2 mit Diagnose). Korrekt: leere/ungültige Pfade vor jeder Pfadoperation mit LYR-CLI0002 ablehnen, bzw. ArgumentException neben IOException fangen.
- Repro (verifiziert: ja): `lyrc check ""` → `Unhandled exception. System.ArgumentException: The value cannot be an empty string. (Parameter 'path') at System.IO.Path.GetFullPath … Lyrc.Program.Options`, exit 134. `lyrpack h.lyrbc -o ""`, `lyrbuild ""` identisch (lyrtest "" per Code identisch, Zeile 51).
- Betroffene Teammitglieder informiert: -

### [cli-security] Ungültiges UTF-8 in der Quelle wird stillschweigend zu U+FFFD (kein Fehler, Datei „ok")
- Severity: LOW
- Datei: src/Lyric.Core/SourceManager.cs:27 (`File.ReadAllText(path, Encoding.UTF8)` — Decoder mit Replacement-Fallback)
- Ebene(n): lexer, cli
- Beschreibung: Eine Datei mit Bytes `\xff\xfe\xc0` in einem String-Literal wird ohne Diagnose übersetzt; das Literal enthält dann drei U+FFFD. Ein Compiler, der die Kodierung als UTF-8 spezifiziert, sollte ungültige Sequenzen melden (Position ist über den Decoder verfügbar: `new UTF8Encoding(false, throwOnInvalidBytes: true)` + Fallback auf Diagnose).
- Repro (verifiziert: ja): `printf 'fn main(): int {\n    let s = "\xff\xfe\xc0"; return 0;\n}\n' > bad.lyr; lyrc check bad.lyr` → `bad.lyr: ok` (nur „'s' is never used").
- Betroffene Teammitglieder informiert: lexer-parser

### [vm-bytecode] `std.string.repeat` mit Zähler ≥ 2^31: unbehandelte ArgumentOutOfRangeException (Prozess-Abbruch) bzw. stilles Falschergebnis
- Severity: HIGH
- Datei: src/Lyric.Vm/NativeRegistry.cs:335-338
- Ebene(n): vm
- Beschreibung: Die Native castet den `int`-Zähler mit `(int)args[1].AsI64` auf 32 Bit. `"ab" * 2147483648` wird zu `Enumerable.Repeat(…, int.MinValue)` → `ArgumentOutOfRangeException`, die VmHost nicht fängt (nur LyricPanic/LyricRuntimeException) → Stacktrace, Exit 134 statt Panik/Exit 101. `"ab" * 4294967297` liefert stillschweigend `"ab"` (Zähler mod 2^32 = 1). Korrekt: Zähler > int.MaxValue bzw. Ergebnislänge > string-Maximum als Panik `LYR-VM0006` ablehnen (wie `arrrep`), nie casten.
- Repro (verifiziert: ja): `fn id(x:int):int{return x;}` … `let s = "ab" * id(2147483648);` → `Unhandled exception. System.ArgumentOutOfRangeException … NativeRegistry.cs:line 336`, Exit 134. `"ab" * id(4294967297)` → length 2.
- Betroffene Teammitglieder informiert: embedding-parity (VmHost fängt keine .NET-Exceptions aus Natives)

### [vm-bytecode] `arrrep`/`std.string.repeat` mit großem Zähler: „Out of memory." und SIGABRT statt Panik — Spec §Arrays verlangt ausdrücklich „never a process abort"
- Severity: HIGH
- Datei: src/Lyric.Vm/Interpreter.cs:820-835, src/Lyric.Vm/Jit/JitRuntime.cs:60-75, src/Lyric.Vm/NativeRegistry.cs:335-338
- Ebene(n): vm, spec
- Beschreibung: Der Guard in `ArrayRepeat` (und JitRuntime.Repeat) prüft nur `count > int.MaxValue / source.Length`; darunter wird `new LyrValue[n]` mit bis zu 2^31 Elementen × 16 Byte (32 GB) angefordert. Die .NET-Laufzeit bricht mit „Out of memory." fatal ab (kein catchbarer OutOfMemoryException-Pfad, Exit 134). Bytecode-Spec §5 Arrays: „a count, or an arrcat result length, the implementation cannot allocate: the bound is the implementation's, but the refusal is a panic (LYR-VM0006), never a process abort — a sandboxed program must not be able to kill its host with a length." Gleiches gilt für `arrcat` (Verdopplungsschleife) und `string * n`. Ein ExecutionBudget hilft nicht: es ist EINE Instruktion. Korrekt: ein konkretes Elementlimit (z. B. konfigurierbar, deutlich unter 2^31) als Panik, plus OutOfMemoryException-Fang als Rückfallebene.
- Repro (verifiziert: ja): `let a = [0] * id(2000000000);` → `Out of memory.` + Aborted, Exit 134. `"abcdefgh" * id(500000000)` → dito.
- Betroffene Teammitglieder informiert: embedding-parity, cli-security (Sandbox-Versprechen des Runners)

### [semantic] Redeklaration eines Locals im selben Block wird stillschweigend verworfen
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1040 (CheckBinding, `scope.TryDeclare(local);` Rückgabe ignoriert)
- Ebene(n): sema
- Beschreibung: `let x = 1; let x = 2; println(x)` kompiliert (nur SEM0071 „x never used“ auf der zweiten Bindung) und gibt `x=1` aus — die zweite Bindung landet nicht in der Symboltabelle, alle Referenzen binden an die erste. Der Initializer der zweiten wird trotzdem ausgewertet (Seiteneffekte). Gleiches Muster wie der kürzlich gefixte Pattern-Fall (SEM0097); hier müsste ein Fehler (Redeklaration im selben Scope) kommen. Vergleich: `let x = 1; let (x, y) = pair;` meldet SEM0097 mit irreführendem Text „already bound in this pattern“ (DeclareBinding wird für Destructuring in den umgebenden Scope aufgerufen, TypeChecker.cs:4592).
- Repro: scratchpad/sem/t1_shadow.lyr — `lyric run` gibt `x=1` (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [semantic] Literal-/Range-Pattern und if-Ausdruck adaptieren Literale nicht → ir-verifier-Crash
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:4440 (CheckLiteralPattern), :4451 (CheckRangePattern), :4132 (Unify)
- Ebene(n): sema, ir
- Beschreibung: Die drei Stellen prüfen nur `IsAssignable` (Literal passt in int8) und rufen nie `AdaptLiteralType`, so dass das Literal als `int`/i64 in der Typtabelle bleibt. `let x: int8 = 5; match (x) { 5 => …, _ => … }` und `let y = if (x > 2) x else 7;` (x: int8) enden als `InternalCompilationException: ir-verifier: operand types differ: t0 is i8, t1 is i64` bzw. `store of t3 (i64) into l2 (i8)`. Betrifft alle schmalen Int-/Float32-Typen als Scrutinee/Branch. Dieselbe Klasse wie der in UnifyNumeric dokumentierte `a + 1`-Bug.
- Repro: scratchpad/sem/t6a_litpat.lyr, t6b_ifexpr.lyr (verifiziert: ja, Crash exit 82)
- Betroffene Teammitglieder informiert: ir-codegen

### [semantic] SemaRules steigt nicht in Lambda-Bodies ab: Zuweisung an gefangenes `let` → Compiler-Crash
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/SemaRules.cs:229 (Children(): LambdaExpr fehlt, also kein WalkStmt/WalkExpr für Lambda-Bodies), :181 (WalkExpr)
- Ebene(n): sema, ir
- Beschreibung: Lvalue-/Mutabilitätsregel (SEM0019), ExprStmt-Regel (SEM0022) und try-Struktur (SEM0035/36) werden in Lambda-Bodies gar nicht geprüft. `let x = 1; let f = (): void => { x = 5; }; f();` wird von der Sema akzeptiert und stirbt im Lowering: `InternalCompilationException: lowering: assignment to captured 'x', which is not a cell - the sema should have boxed it (ADR-018) or rejected the assignment`. Ebenso ungeprüft: Zuweisung an Parameter/Globals im Lambda, `5;` als Statement im Lambda. Gleiches Loch für Block-Arme eines match-AUSDRUCKS (Children(MatchExpr) nimmt nur Expr-Arme).
- Repro: scratchpad/sem/t3b_lambda_let.lyr (verifiziert: ja, exit 82)
- Betroffene Teammitglieder informiert: ir-codegen

### [semantic] RecordCaptures übersieht DestructuringStmt → Capture fehlt, Lowering lehnt gültiges Programm ab
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:4855 (RecordCaptures.WalkNode: kein `case DestructuringStmt`)
- Ebene(n): sema, ir
- Beschreibung: `let pair = (3, 4); let f = (): int => { let (a, b) = pair; return a + b; };` — der Initializer des Destructurings wird nicht besucht, `pair` wird nicht als Capture eingetragen, das Lowering meldet `LYR-IR0001: reference to 'pair' (only parameters, locals and constants)` für ein korrektes Programm. Gleiche Symptomatik wie der in STATUS.md beschriebene Or-Pattern-Fall.
- Repro: scratchpad/sem/t5b_destr_capture.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen

### [semantic] Definite Assignment: `defer`-Body zählt als sofort ausgeführt
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/FlowAnalyzer.cs:100 (`case DeferStmt de: return AnalyzeStmt(de.Body, assigned);`)
- Ebene(n): sema
- Beschreibung: `var x: int; defer { x = 1; } println(x);` wird akzeptiert und gibt `x=0` aus — der Read passiert vor dem Defer-Lauf. Die Zuweisungen im Defer-Body dürfen nicht in den Folgezustand fließen (Body mit Klon analysieren, `assigned` unverändert zurückgeben). Spec §7.7 verlangt Zuweisung vor jedem Read.
- Repro: scratchpad/sem/t8_defer_da.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [semantic] Definite Assignment: Block-Arme eines match-Ausdrucks werden nicht analysiert
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/FlowAnalyzer.cs:206 (`case MatchExpr ma`: nur `arm.Body is Expr` wird analysiert)
- Ebene(n): sema
- Beschreibung: `var n: int; let v = match (x) { 1 => { println(f"n={n}"); return 0; }, _ => 5 };` — Read von `n` im Block-Arm wird nicht geprüft, Programm läuft und gibt `n=0` aus (kein SEM0018). Block-Arme müssen wie bei MatchStmt mit AnalyzeStatements besucht werden.
- Repro: scratchpad/sem/t12_matchexpr_da.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [semantic] Doppelte Parameternamen werden akzeptiert, zweiter Parameter unerreichbar
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:921 (CheckFunction: `scope.TryDeclare(ps);` Rückgabe ignoriert); Lambda-Parameter analog :4752
- Ebene(n): sema (evtl. parser)
- Beschreibung: `fn f(a: int, a: int): int { return a; }` kompiliert ohne Diagnose; `f(1, 2)` liefert 1, der zweite Parameter ist nie referenzierbar. Erwartet: Fehler an der Deklaration. Gleiches bei Lambda-Parametern `(a: int, a: int) => …`.
- Repro: scratchpad/sem/t11_dupparam.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [ir-codegen] do-while: break/continue-Zielblock wird INNERHALB eines try/defer-Bereichs angelegt — Code nach der Schleife ist vom Handler geschützt (Endlosschleife, doppelter defer)
- Severity: CRITICAL
- Datei: src/Lyric.Frontend/Ir/Lowering/LoopScope.cs:44-47 (ContinueTarget/BreakTarget `??= blocks.NewBlock()` lazily), src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1359-1388 (LowerDoWhile), :664-763 (LowerTry: `end = new BlockId(_blocks.Count)` nach dem Body), :797 (LowerScope: `end` für finally-Region)
- Ebene(n): ir
- Beschreibung: Handler-Regionen sind zusammenhängende Block-ID-Bereiche [start, end), wobei `end` = Blockanzahl nach dem Lowern des Bodys. Bei `do-while` werden Break-/Continue-Ziel erst BEI BEDARF angelegt (LoopScope) — steht das `break`/`continue` lexikalisch in einem `try` oder in einem Scope mit `defer` im Schleifenkörper, bekommt der Zielblock eine ID innerhalb von [start, end). Der Code NACH der Schleife (Break-Ziel) bzw. die Schleifenbedingung (Continue-Ziel) liegt damit im geschützten Bereich: ein `throw` nach der Schleife wird vom `catch` IM Schleifenkörper gefangen, danach läuft die Bedingung erneut und der Code nach der Schleife wird nochmals ausgeführt → Endlosschleife; bei `defer` läuft der defer-Body beim Unwinding ein zweites Mal. Korrekt wäre: Break-/Continue-Ziel vor dem Body anlegen (wie while/for-in) oder Handler-Ranges nicht als zusammenhängende Bereiche annehmen; das Verifier prüft diese Invariante nicht (kein Check, dass ein Handler-Range nur lexikalisch innere Blöcke enthält).
- Repro (verifiziert: ja, alle drei):
  (a) `fn work(): int throws Boom { do { try { break; } catch (e: Boom) { println("caught INSIDE loop"); } } while (false); println("after loop"); throw Boom { n = 1 }; }` → druckt endlos "caught INSIDE loop (wrong)" / "after loop" statt in main gefangen zu werden.
  (b) `do { try { calls += 1; continue; } catch (e: Boom) {…} } while (cond(calls));` mit `cond` das ab 2 wirft → Endlosschleife, catch im Körper fängt die Ausnahme der Bedingung.
  (c) `do { { defer println("defer ran"); break; } } while (false); println("after loop"); throw Boom{n=1};` → "defer ran" wird ZWEIMAL gedruckt (einmal beim break, einmal beim Unwinding des throw nach der Schleife).
- Betroffene Teammitglieder informiert: optimizer (Reachability/Inliner nehmen zusammenhängende Ranges an), vm-bytecode (Handler-Suche per Block-Range — Symptom, nicht Ursache)

### [ir-codegen] match: Binding-Pattern über einen Struct aliast den Scrutinee (kein structcopy)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:2412-2431 (BindPattern, BindingPattern-Zweig: `StoreLocal(slot, value)` bzw. `OptGet`+`StoreLocal` ohne CopyStructValue)
- Ebene(n): ir
- Beschreibung: 4.4.1 hat für Feld-Bindings (BindOne, Z.2606) den `structcopy` nachgezogen, das TOP-LEVEL-Binding `q => …` speichert aber den Scrutinee-Temp direkt in den Slot. Für einen Struct-Scrutinee (aus Slot/Feld/Element geladen, nicht "fresh") teilt sich `q` das Slot-Array mit dem Original; eine Mutation des Originals im Arm ist über `q` sichtbar. Gleiches im `?T`-Narrowing-Zweig (Z.2422-2428, OptGet ohne Kopie). Korrekt: `if (slotType is IrStructType s) value = CopyStructValue(value, s, span)` wie in BindOne/Coerce.
- Repro (verifiziert: ja): `var p = P { x = 1 }; match (p) { q => { p.x = 99; println(f"q.x={q.x}"); } }` → druckt `q.x=99`, erwartet 1 (`let r = p; p.x = 5;` liefert korrekt r.x=99).
- Betroffene Teammitglieder informiert: semantic (Wertsemantik-Garantie), spec-conformance (§7.6 Binding einer Struct-Variante)

### [ir-codegen] `n++`/`n--`/`n ??= v` auf einer gecapturten (geboxten) Variable: Lowering umgeht die Cell und lehnt gültigen Code mit irreführender Diagnose ab
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1568-1583 (LowerIncDec: `_slots.TypeOfLocal(slot)` + direktes LoadLocal/StoreLocal), :2803-2828 (LowerCoalesceAssign: dito)
- Ebene(n): ir | diagnostics
- Beschreibung: Eine von einem Lambda gecapturte `var` liegt in einer Cell (`_cells`); nur LoadValue/StoreValue/ValueTypeOf kennen das. LowerIncDec und LowerCoalesceAssign fragen `_slots.TypeOfLocal` (= Cell-Referenztyp) und lesen/schreiben den Slot direkt. Ergebnis: `n++` → LYR-IR0001 "increment/decrement on a non-numeric type", `s ??= 5` → LYR-IR0001 "'??=' on a non-optional target" mit Note "a value of this type is never null" — beides falsche Aussagen über korrektes Programm (`n += 1` funktioniert). Korrekt: ValueTypeOf/LoadValue/StoreValue verwenden.
- Repro (verifiziert: ja): `var n = 0; let f = () => n; n++;` → error LYR-IR0001 increment/decrement on a non-numeric type. `var s: ?int = null; let f = () => s; s ??= 5;` → error LYR-IR0001 '??=' on a non-optional target.
- Betroffene Teammitglieder informiert: diagnostics (irreführender Text/Note), semantic (Boxing-Regel: welche Stellen die Cell kennen müssen)

### [vm-bytecode] Null-Klassenreferenz (Global-Vorwärtsbezug über Funktionsaufruf) → `ldfld` wirft unbehandelte InvalidOperationException, Prozess-Abbruch statt Panik
- Severity: HIGH
- Datei: src/Lyric.Vm/Interpreter.cs:794 (LoadField), src/Lyric.Vm/LyrValue.cs:130 (`AsObject`), src/Lyric.Vm/VmHost.cs:38-55 (fängt nur LyricPanic/LyricRuntimeException); Sema: LYR-SEM0057 nur für DIREKTES Lesen
- Ebene(n): vm, sema, embedding
- Beschreibung: Spec §4.3: ein Initialisierer, der ein später deklariertes Global liest, ist `LYR-SEM0057`. Der Checker sieht das aber nur bei direktem Lesen; über einen Funktionsaufruf (`fn peek(): Node { return later; }` … `let early: Node = peek(); let later: Node = Node{…};`) kompiliert das Programm fehlerfrei, `early` ist zur Laufzeit eine Null-Referenz (Globals starten als `default`), und `early.value` läuft in `LyrValue.AsObject` → `InvalidOperationException("null object reference")`. Das ist eine .NET-Exception, keine LyricPanic: `lyrvm run` stirbt mit Stacktrace und Exit 134 (nicht 101), ein Embedding-Host bekommt eine fremde Exception statt einer LyricPanic. Dieselbe Klasse (.NET-Exception aus dem Interpreter-Loop) trifft `ldfld`/`stfld`/`ldelem`/`arrlen`/`enumtag` auf einer Null-Referenz und `AsString` auf einem Objekt (InvalidCastException) für handgebaute Module, die `verify` passieren (der Verifier prüft keine Operandentypen, nur Tiefe). Korrekt: (a) Sema: Vorwärtsbezug transitiv über Aufrufe erkennen oder Globals bis zur Initialisierung als „nicht lesbar" behandeln; (b) VM: Null-Deref als LyricPanic (`LYR-VM0007` oder eigener Code) melden bzw. VmHost/LoadedProgram jede Exception aus dem Loop in eine Panik mit Backtrace übersetzen.
- Repro (verifiziert: ja): scratchpad/vm/t_null2.lyr → `Unhandled exception. System.InvalidOperationException: null object reference at Lyric.Vm.LyrValue.get_AsObject() … Interpreter.cs:line 794`, Exit 134.
- Betroffene Teammitglieder informiert: semantic (SEM0057 nicht transitiv), embedding-parity (fremde Exceptions aus Invoke/RunEntry)

### [cross-layer-reviewer] Struct-Wertsemantik bricht durch ein `?Struct`-Feld/-Binding (Aliasing statt Kopie)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:5046-5058 (FindStructCycle überspringt `?S`, behandelt es also wie eine Referenz); src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:2606,2689,2698,2718 (structcopy nur für `IrStructType`, nie für `IrOptionalType(IrStructType)`); src/Lyric.Vm/Interpreter.cs:1041-1055 (CopyStruct kopiert nur Felder mit Tag Struct, nicht Optional-of-Struct)
- Ebene(n): sema | ir | vm | spec
- Beschreibung: Sema erlaubt `struct W { n: ?S }` mit Struct S und erlaubt Mutation durch `b.n!.v = 9`. Lowering und VM kopieren bei `var b = a;` (structcopy) das Feld `n` aber nicht (nur Felder mit Tag Struct werden rekursiv kopiert; `?S` ist Tag Optional). Ergebnis: `a` und `b` teilen sich das innere S — Wertsemantik von Structs ist gebrochen. Gleiches gilt für ein lokales `var y: ?S = x;`: kein structcopy, weil der IR-Typ `IrOptionalType` ist. Außerdem akzeptiert Sema `struct S { n: ?S }` (Selbstrekursion über Optional), obwohl 13-bytecode.md §Types nur Rekursion über class/array/interface erlaubt. Korrekt wäre entweder: `?Struct` als Wert behandeln (Copy in OptSome/Binding/CopyStruct-Rekursion durch Optional hindurch) oder Mutation durch `!` auf Struct-Optionals verbieten.
- Repro (verifiziert: ja, `lyric run`):
  ```
  import std.io.console { println };
  struct S { v: int }
  struct W { n: ?S }
  fn main(): int {
    var a = W { n = S { v = 1 } };
    var b = a;
    b.n!.v = 9;
    println(f"{a.n!.v} {b.n!.v}");   // druckt "9 9", erwartet "1 9"
    return a.n!.v;
  }
  ```
- Betroffene Teammitglieder informiert: semantic, ir-codegen, vm-bytecode, spec-conformance
### [lexer-parser] NUL-Byte im Quelltext beendet die Tokenisierung stillschweigend
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:57 (`Current` liefert '\0' als EOF-Sentinel), :225 (`if (Current == '\0') return Eof`)
- Ebene(n): lexer
- Beschreibung: Ein echtes U+0000 im Quelltext ist vom EOF-Sentinel nicht unterscheidbar; der Lexer liefert an dieser Stelle `Eof`, der Rest der Datei wird OHNE Diagnose verworfen. `let x = 1;\0let y = ;` tokenisiert zu `let x = 1; EOF` und ist "ok". Spec §1.9/§1.1: ein Zeichen, das kein Token beginnt, ist LYR-LEX0001 (Kontrollzeichen werden dort ausdrücklich als `U+NNNN` gemeldet — U+0001 bekommt diese Diagnose, U+0000 nicht). Korrekt: `_pos < _source.Length` explizit prüfen und U+0000 wie jedes andere Kontrollzeichen als LEX0001 melden. Dieselbe Verwechslung in ScanString/ScanChar/ScanFStringText (`'\0'`-Vergleiche): ein NUL in einem String-Literal wird als "unterminated" gemeldet statt als ungültiges Zeichen.
- Repro: Datei mit Inhalt `fn main() {}\0garbage garbage` → `lyrc check` meldet nichts; Probe: `Lexer.Next()` liefert nach `}` sofort Eof (verifiziert: ja, über Lexer-Probe im Scratchpad)
- Betroffene Teammitglieder informiert: cli-security (Hinweis: Sicherheitsrelevanz, da Inhalt nach NUL unsichtbar bleibt)

### [lexer-parser] Zeichenliteral mit Nicht-BMP-Codepunkt (`'😀'`) wird mit LEX0008 abgelehnt
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:565-602 (ScanChar zählt UTF-16-Einheiten, nicht Codepunkte)
- Ebene(n): lexer | spec
- Beschreibung: Spec §1.5: "'…' holds exactly one code point — a Lyric char is a Unicode scalar value, never a UTF-16 unit". `ScanChar` zählt `contentCount` pro `char` (UTF-16-Einheit); ein Surrogatpaar zählt 2 → `LYR-LEX0008 expected only 1 character in character literal, got 2`. Die Escape-Form `'\u{1F600}'` wird dagegen akzeptiert (contentCount 1) und LiteralDecoder.DecodeChar (Char.ConvertToUtf32) kann den Wert korrekt bilden — nur die Zählung ist falsch. Fix: bei `char.IsHighSurrogate(Current) && char.IsLowSurrogate(PeekAt(1))` beide Einheiten als EIN Zeichen konsumieren.
- Repro: `fn main() { let c = '😀'; }` → LYR-LEX0008 (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance

### [lexer-parser] `f<List<int>>()` — Aufruf mit Typargumenten wird bei `>>` nicht erkannt (Spec §6.3)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Parsing/Parser.cs:184-225 (LooksLikeCallTypeArguments), insbesondere :196-200 (nur `Greater`, kein `Shr`)
- Ebene(n): parser | spec
- Beschreibung: Der Lookahead, der `f<…>(` von einem Vergleich unterscheidet, kennt nur `Greater`; das Token `>>` (Shr) fällt in `default → return false`. Damit wird jeder Aufruf mit verschachtelten Typargumenten als Vergleichskette gelesen: `empty<List<int>>()` → `(empty < List) < (int >> ())` → `LYR-PAR0002 expected an expression, got RParen`. `SkipTypeArgs` (:528-547, für StructInit/TypePath) und `LambdaTailAhead` (:706) behandeln `Shr` korrekt (`depth -= 2`) — nur der Call-Pfad nicht. Spec §6.3 verlangt: `<` öffnet eine Typargumentliste, wenn sie balanciert schließt und `(` folgt. Workaround für Nutzer: `empty<List<int> >()` mit Leerzeichen — nicht dokumentiert. Auch `f<Map<int, List<int>>>()` (Shr gefolgt von Greater) betroffen. Fix: `case TokenKind.Shr: depth -= 2; if (depth == 0) …; if (depth < 0) return false;` plus analog `ShrEqual`/`GreaterEqual` sind hier nicht nötig (danach muss `(` folgen).
- Repro: `fn main() { let x = empty<List<int>>(); }` → PAR0002 (verifiziert: ja, Probe `expr`: AST ist `Binary Lt(Binary Lt(empty, List), Binary Shr(int, Error))`)
- Betroffene Teammitglieder informiert: spec-conformance

### [lexer-parser] `(a < b) > (c)` wird als Aufruf mit Typargumenten gelesen — unbalancierte `)` im Lookahead
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Parsing/Parser.cs:212-213 (`LParen`/`RParen` gelten als "typ-artig", ohne Klammerbilanz), analog :540 (SkipTypeArgs)
- Ebene(n): parser
- Beschreibung: `LooksLikeCallTypeArguments` zählt nur `<`/`>`; `(` und `)` werden als typ-artige Tokens durchgewinkt, ohne dass ihre Bilanz geprüft wird. In `(a < b) > (c)` sieht der Scan ab dem `<`: `b`, `)`, `>` (depth 0), dann `(` → "Typargumente" → `a<b)>(c)` wird als Call geparst und scheitert mit drei Folgefehlern (PAR0008 "expected '(' after type arguments", PAR0009, PAR0008), obwohl der Ausdruck ein gewöhnlicher, gültiger Vergleich ist. Spec §6.3: "only tokens that can occur in a type expression stand between the two" — eine schließende Klammer, die die eigene Gruppe verlässt, kann in keinem Typausdruck stehen. Fix: Klammer-/Bracket-Tiefe mitzählen und bei negativer Tiefe `return false` (in SkipTypeArgs gleich mit, dort ist es via `IsStructInitAhead` für `(a < b) > { …` theoretisch ebenfalls erreichbar).
- Repro: `fn main() { let x = (a < b) > (c); }` → PAR0008/PAR0009 (verifiziert: ja, Probe `expr` liefert `Call(a, c)` mit 3 Fehlern)
- Betroffene Teammitglieder informiert: -

### [lexer-parser] Match-Guard in Klammern (`n if (y) => 1`) wird als Lambda gelesen — kryptische Fehler
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Parsing/Parser.Patterns.cs:219 (Guard via ParseExpr(0)), src/Lyric.Frontend/Parsing/Parser.cs:680-702 (IsLambdaAhead: `(…)` gefolgt von `=>` ⇒ Lambda)
- Ebene(n): parser | spec | diagnostics
- Beschreibung: `MatchArm = Pattern [ 'if' Expr ] '=>' …`. Ein Guard, der (wie bei `if`-Statements üblich) in Klammern geschrieben wird, endet direkt vor dem `=>` des Arms; `IsLambdaAhead` sieht `( … ) =>` und liest Guard UND Arm-Pfeil als Lambda. `n if (y) => 1, _ => 2` wird zu Guard=`(y) => 1` und scheitert dann mit "expected '=>' in match arm, got Comma"; `n if (n > 1) => 1` liefert 9 Folgefehler ("expected ')' after lambda parameters" usw.), keiner nennt die Ursache. Die Grammatik ist an dieser Stelle formal mehrdeutig (Lambda ist ein Primary); die Referenzimplementierung sollte sie zugunsten des Guards auflösen (z. B. Lambda-Lookahead im Guard-Kontext unterdrücken, oder mindestens einen Hinweis "guard followed by '=>' — did you mean …"). Parenthesierte Guards sind eine naheliegende Schreibweise, da alle anderen Bedingungen der Sprache Klammern verlangen (§2.3).
- Repro: `fn f(x: int, y: bool): int { return match (x) { n if (y) => 1, _ => 2 }; }` → PAR0002 + PAR0034 (verifiziert: ja, Probe `expr` zeigt `Guard → Lambda(Param y, Int 1)`)
- Betroffene Teammitglieder informiert: spec-conformance (Grammatik-Mehrdeutigkeit), diagnostics (Folgefehler ohne Ursache)

### [lexer-parser] f-String darf laut Spec keine Zeile überspannen — innerhalb einer Interpolation wird ein Zeilenumbruch aber akzeptiert
- Severity: LOW
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:161-166 (SkipTrivia läuft VOR der `'\n'`-Prüfung im FStringInterp-Modus und verschluckt den Umbruch)
- Ebene(n): lexer | spec
- Beschreibung: Spec §1.7 "an f-string must not span a line", Appendix A zu LEX0011: "including one broken open by a line end inside an interpolation". Im Modus FStringInterp ruft `Next()` zuerst `SkipTrivia()` (überspringt `\n` und Zeilenkommentare) und prüft erst danach `Current is '\0' or '\n'` — die Prüfung kann für `\n` nie mehr anschlagen. `f"{1 +\n 2}"` und `f"{x // kommentar\n}"` tokenisieren fehlerfrei; nur ein Umbruch in einem Text-Chunk wird gemeldet. Fix: im Interp-Modus Whitespace ohne `\n` überspringen bzw. `\n` vor SkipTrivia prüfen.
- Repro: `fn main() { let s = f"{1 +\n 2}"; }` → keine Diagnose (verifiziert: ja, Token-Probe)
- Betroffene Teammitglieder informiert: spec-conformance

### [ir-codegen] Monomorphisierung: Instanzname ohne Modulpräfix — gleichnamige generische Funktionen aus zwei Modulen teilen sich EINE Instanz (stiller Fehlaufruf)
- Severity: CRITICAL
- Datei: src/Lyric.Frontend/Ir/Lowering/InstanceTable.cs:76-80 (`name = $"{baseName}<…>"`, `_byKey` dedupliziert darüber), :133-135 (RequestMethod: `owner.Definition.Name` unqualifiziert), src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3909 (`_instances.Request(symbol, generic, calleeName, …)` — calleeName ist der bloße Bezeichner)
- Ebene(n): ir
- Beschreibung: Der Instanzschlüssel ist `<unqualifizierter Name><Typargumente>`; NameMangling.ForFunction (Modulpfad-Präfix) wird für Instanzen nicht benutzt. Zwei Module mit je `pub fn twice<T>(x: T): T` (oder zwei generische Klassen `Box<T>` in zwei Modulen, oder `A.map<U>` auf gleichnamigen Interfaces) erzeugen denselben Schlüssel `twice<int>`; der zweite Aufruf bekommt die ID der ersten Instanz. Der Verifier (duplicate function name) kann das nicht sehen, weil die Tabelle vorher dedupliziert. Korrekt: Schlüssel/Name aus dem qualifizierten Symbol (Modul + Typ + Name), wie bei ForFunction/ForMethod.
- Repro (verifiziert: ja): probe/gen/{alpha,beta,main}.lyr — alpha: `pub fn twice<T>(x: T): T { return x; }`, beta: gleich, aber mit `println("beta.twice called")`; main: `import alpha; import beta; alpha.twice(1); beta.twice(2);` → "beta.twice called" wird NIE gedruckt, beta.twice ruft alphas Instanz.
- Betroffene Teammitglieder informiert: semantic (Symbol-Identität vs. Name), optimizer (Inliner/Devirtualizer indizieren über dieselben IDs)

### [ir-codegen] `never`-Ausdruck (panic) als Zweig eines if-Ausdrucks / rechte Seite von `??` → InternalCompilationException statt Code
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1392-1393 (LowerExpr wirft Bug bei null), :1723/1727 (LowerIfExpr: `StoreLocal(slot, LowerExprAs(...))` ohne Prüfung auf gesiegelten Block), :2786 (LowerCoalesce: `LowerExpr(expr.Right)`), :3883-3888 (LowerCall panic: Seal(Unreachable), return null)
- Ebene(n): ir | sema
- Beschreibung: Die Sema akzeptiert `never` in Wertposition (`let x = if (c) 5 else panic("…")`, `o ?? panic("…")`); LowerCall siegelt den Block mit `unreachable` und liefert null, LowerExpr wirft daraufhin "expression … produced no value" — Compiler-Absturz mit Stacktrace statt Diagnose oder Code. Inkonsistent dazu weist die Sema `match` mit einem `never`-Arm ab (LYR-SEM0016 'int' vs 'never'). Korrekt: entweder Sema lehnt/erlaubt einheitlich, und die Lowering-Zweige (LowerIfExpr, LowerCoalesce, LowerArm, LowerShortCircuit) prüfen nach dem Zweig `_b.IsSealed` und lassen Store+Branch weg.
- Repro (verifiziert: ja): `fn pick(c: bool): int { let x = if (c) 5 else panic("no value"); return x; }` → Unhandled InternalCompilationException "lowering: expression at … produced no value (in 'main.pick')". Ebenso `let v = o ?? panic("none");`.
- Betroffene Teammitglieder informiert: semantic (never-Typisierung in if/??/match uneinheitlich), diagnostics (Absturz statt Diagnose)

### [cross-layer-reviewer] `++`/`--` auf Nicht-Lokale wird von Sema akzeptiert und erst im Lowering als "cannot lower it yet" (LYR-IR0001) refusiert
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (Prüfung von ++/-- nur auf Operandentyp, SEM0003; keine Lvalue-Prüfung); src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs (grep "increment/decrement target (only parameters and locals)")
- Ebene(n): sema | ir | diagnostics
- Beschreibung: Sema garantiert nicht, dass das Ziel von `++`/`--` eine zuweisbare Stelle ist. `5++`, `--1`, `(a+b)++` sind Benutzerfehler und werden als Compiler-Limitierung ("this compiler version cannot lower it yet") gemeldet. `p.x++`, `a[0]++`, `this.v++` (Feld/Element/Receiver-Feld) sind laut Sema gültig, scheitern aber ebenfalls mit LYR-IR0001. Konsequenz für den LSP: der Editor zeigt für `p.x++` KEINE Diagnose (Pipeline endet nach Sema), `lyric build` bricht dann ab. Korrekt: Sema meldet für Nicht-Lvalues einen SEM-Fehler; für Feld/Element-Targets entweder Lowering implementieren oder Sema-Diagnose, die die Grammatik/Spec-Regel benennt.
- Repro (verifiziert: ja): `fn main(): int { let y = 5++; return y; }` → LYR-IR0001; `class P { x: int } fn main(): int { let p = P { x = 1 }; p.x++; return p.x; }` → LYR-IR0001; `fn main(): int { let a = [1]; a[0]++; return a[0]; }` → LYR-IR0001; `lyrc check` meldet in allen Fällen nichts.
- Betroffene Teammitglieder informiert: semantic, ir-codegen, diagnostics, spec-conformance

### [cross-layer-reviewer] Tiefe Verschachtelung → Stack Overflow (Prozessabbruch) in Parser, TypeChecker und AstDumper — trifft auch LSP-Server und Embedding-Host
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Parsing/Parser.cs (ParseParenOrTupleOrLambda → ParsePrimary → ParsePrefix → ParseExpr rekursiv, ~5000 Klammerebenen); src/Lyric.Frontend/Sema/TypeChecker.cs (CheckBinary → Compute → CheckTarget → CheckExpr rekursiv über den linken Operanden, ~5000 verkettete `+`); src/Lyric.Frontend/AST/AstDumper.cs (Write rekursiv, ~3000 Blockebenen)
- Ebene(n): parser | sema | lsp | embedding
- Beschreibung: Es gibt keine Tiefenbegrenzung. Ein Ausdruck `1 + 1 + … (5000 Terme)` oder `((((…1…))))` (5000) beendet den Prozess mit "Stack overflow" (SIGABRT, Exit 134) statt einer Diagnose. Da Lyric.Lsp und Lyric.Embedding denselben Parser/TypeChecker in-process ausführen, tötet ein solcher Puffer/Skript den Language-Server bzw. den Host — ein StackOverflow ist in .NET nicht abfangbar. Ein generiertes Programm mit 5000 verketteten `+` (z.B. String-Konkatenation aus einem Generator) ist keine exotische Eingabe. 4000 Terme laufen noch durch. Korrekt: Tiefenlimit mit Diagnose (z.B. LYR-PAR "expression nested too deeply") im Parser; für Binärketten ggf. iterative Behandlung des linken Operanden in Sema/Lowering.
- Repro (verifiziert: ja): python3 -c "print('fn main(): int { let x = 1' + ' + 1'*5000 + '; return x; }')" > deep.lyr; lyrc check deep.lyr → Exit 134 "Stack overflow" (TypeChecker.CheckBinary). Klammern: '('*5000 → Parser.ParseParenOrTupleOrLambda. `lyrc parse` mit 3000 Blöcken → AstDumper.Write.
- Betroffene Teammitglieder informiert: lexer-parser, semantic, embedding-parity

### [spec-conformance] if-Ausdruck mit int- und float-Arm wird akzeptiert und liefert Bitmuster-Müll (5e-324)
- Severity: CRITICAL
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (Arm-Unifikation des if-Ausdrucks; Literal-Adaption greift fälschlich über den anderen Arm)
- Ebene(n): sema | ir
- Beschreibung: Spec §6.9: if/match-Arme müssen gleiche Typen haben oder ein Arm `null`; „Disagreeing arms are one error (LYR-SEM0016)“. §3.1 zählt die Adaptionskontexte abschließend auf — „der andere Arm eines if-Ausdrucks“ ist keiner. `let q = if (c) 1 else 2.5;` kompiliert ohne Diagnose, `q` wird als float typisiert, aber der int-Arm liefert das rohe Bitmuster 1 → Ausgabe `5e-324` statt Ablehnung mit SEM0016. (Das Array-Pendant `[1, 2.5]` wird korrekt mit SEM0009 abgelehnt.)
- Repro (verifiziert: ja):
  ```
  import std.io.console { println };
  fn main(): int { let c = true; let q = if (c) 1 else 2.5; println(f"{q}"); return 0; }
  ```
  Ist: kompiliert, druckt `5e-324`. Soll: error[LYR-SEM0016].
- Betroffene Teammitglieder informiert: semantic, ir-codegen

### [spec-conformance] Compiler-Absturz (InternalCompilationException) bei Enum-Struct-Variante mit Literal-Subpattern
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs (BindOne / BindPatternFields: „pattern binding 'w' was not bound by the type checker“); Sema bindet Literal-Subpattern in Variant-Feldmustern nicht
- Ebene(n): sema | ir
- Beschreibung: Grammatik §7 `FieldPattern = IDENTIFIER [ '=' Pattern ]`, Spec §7.6 erlaubt „enum variants with nested payload patterns“. `Shape.Rect { w = 2.0, h = 1.0 }` bringt den Compiler mit einer unbehandelten Exception zum Absturz (Exit −6/SIGABRT). Das Tuple-Pendant `E.P(1, y)` wird sauber mit LYR-IR0001 („a nested LiteralPattern“) abgelehnt — diese Grenze fehlt auf dem Struct-Variant-Pfad. Nebenbefund (Spec-Lücke): die IR0001-Grenze „nested LiteralPattern in Variant-Payload“ ist in §7.6/Anhang A.5 nicht dokumentiert (dort nur das Struct/Class-Feldmuster).
- Repro (verifiziert: ja):
  ```
  enum Shape { Rect { w: float, h: float }, Dot }
  fn main(): int { let d = Shape.Dot;
    match (d) { Shape.Rect { w = 2.0, h = 1.0 } => { return 1; } _ => { return 0; } } }
  ```
  Ist: `Unhandled exception. Lyric.Core.InternalCompilationException: lowering: pattern binding 'w' was not bound by the type checker`. Soll: kompiliert (oder mindestens diagnostizierte IR0001-Grenze).
- Betroffene Teammitglieder informiert: ir-codegen, semantic

### [spec-conformance] Auflösungsreihenfolge §5.4 verletzt: Default-Methode gewinnt gegen sichtbare Extension-Methode
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (Member-Auflösung auf konkretem Typ; Default-Methoden werden vor Extensions gefunden)
- Ebene(n): sema
- Beschreibung: Spec §5.4: „Member resolution on a concrete type is fixed at compile time, in this order: own member, then visible extension method, then a default method of a conformed interface's chain.“ Der Compiler wählt für `s.name()` die Default-Methode des Interfaces, obwohl im selben Modul `extend S { fn name() }` sichtbar ist.
- Repro (verifiziert: ja):
  ```
  import std.io.console { println };
  interface A { fn name(): string { return "default"; } }
  struct S :: [A] { v: int }
  extend S { fn name(): string { return "ext"; } }
  fn main(): int { let s = S { v = 1 }; println(s.name()); return 0; }
  ```
  Ist: `default`. Soll: `ext`.
- Betroffene Teammitglieder informiert: semantic

### [spec-conformance] Zuweisung an Feld eines `let`-gebundenen Structs wird akzeptiert
- Severity: HIGH (Spec-Lücke mitgemeldet)
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (Lvalue-Prüfung SEM0019 prüft nur die Variable selbst, nicht den Feldpfad eines Werttyps)
- Ebene(n): sema | spec
- Beschreibung: §7.1: „`let` binds immutably … ANY assignment to a `let` … is LYR-SEM0019“; ausdrücklich erlaubt ist nur der Klassenfall („the REFERENCE is immutable, the object is the object's business“). Ein Struct hat Wertsemantik (§3.4), seine Felder SIND die Bindung. `let s = St { v = 1 }; s.v = 2;` kompiliert und druckt `2`; ebenso `s.inc()` mit `mut fn`. Die Spec sagt nichts Explizites zum Struct-Fall (Spec-Lücke) — aber der Klassenfall ist als Ausnahme formuliert.
- Repro (verifiziert: ja): `struct St { v: int } fn main(): int { let s = St { v = 1 }; s.v = 2; println(f"{s.v}"); return 0; }` → druckt 2, keine Diagnose. Soll: SEM0019.
- Betroffene Teammitglieder informiert: semantic

### [spec-conformance] Zeilenumbruch INNERHALB einer f-string-Interpolation wird akzeptiert (LEX0011 fehlt)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:160-165 (SkipTrivia() vor der `\n`-Prüfung im FStringInterp-Modus verschluckt den Zeilenumbruch)
- Ebene(n): lexer
- Beschreibung: §1.7 „an f-string must not span a line“; Anhang A LEX0011 „Unterminated f-string — including one broken open by a line end inside an interpolation“. `f"a{1\n}"` kompiliert ohne Diagnose (im Textteil wird der Umbruch korrekt mit LEX0011 abgelehnt).
- Repro (verifiziert: ja): `fn main(): int { let x = f"a{1` + Zeilenumbruch + `}"; return 0; }` → nur SEM0071 (unused). Soll: LYR-LEX0011.
- Betroffene Teammitglieder informiert: lexer-parser

### [spec-conformance] Optional gegen Nicht-null-Wert verglichen meldet SEM0003 statt SEM0059
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (Binary-Operator-Prüfung `==`/`!=` auf `?T` vs `T`)
- Ebene(n): sema | diagnostics
- Beschreibung: Anhang A LYR-SEM0059: „==/!= not defined: an optional compared against a non-null value, or a type without Equatable“. `let a: ?int = 1; a == 1` (und `a == one`) meldet `LYR-SEM0003: operator 'Eq' is not applicable to '?int' and 'int'`. Der Code ist Vertrag (§12.1).
- Repro (verifiziert: ja): `fn main(): int { let a: ?int = 1; return if (a == 1) 1 else 0; }` → SEM0003. Soll: SEM0059.
- Betroffene Teammitglieder informiert: diagnostics, semantic

### [spec-conformance] Zyklischer Typalias ohne Verwendung wird stillschweigend akzeptiert
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:5220-5228 (ExpandAlias meldet SEM0064 nur beim Expandieren, d.h. nur bei Verwendung)
- Ebene(n): sema
- Beschreibung: §3.5 „An alias must not expand through itself (LYR-SEM0064)“. `type A = B; type B = A;` (auch `pub`, auch `type A = A[];`) kompiliert ohne Diagnose, solange kein Ausdruck den Alias benutzt. Eine Bibliothek mit `pub type`-Zyklus wird als gültig durchgewinkt.
- Repro (verifiziert: ja): `pub type A = B; pub type B = A; fn main(): int { return 0; }` → exit 0. Soll: SEM0064.
- Betroffene Teammitglieder informiert: semantic

### [spec-conformance] `extend int[] { … }` wird angenommen statt mit SEM0047 abgelehnt; die Methode ist dann unauffindbar
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs (SEM0047-Prüfung des Extend-Ziels erfasst Array-Typen nicht) / src/Lyric.Frontend/Parsing (Extend-Ziel wird als TypeExpr mit Suffix geparst)
- Ebene(n): sema
- Beschreibung: Anhang A LYR-SEM0047 „An extend target that is not a plain named type (no generic, array, tuple or function targets)“. `extend int[] { fn first(): int { return this[0]; } }` wird ohne Diagnose akzeptiert; `xs.first()` scheitert dann mit SEM0012 („'int[]' has no member 'first'“) — irreführend. (`extend Box<int>` wird korrekt mit SEM0047 abgelehnt.)
- Repro (verifiziert: ja): siehe oben, Deklaration allein → exit 0.
- Betroffene Teammitglieder informiert: semantic

### [spec-conformance] Ziffer außerhalb der Basis (`0b12`) meldet PAR0016 statt LEX0003
- Severity: LOW
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:431-433 (ScanNonDecLiteral hört beim ersten Nicht-Basis-Zeichen auf, statt LEX0003 zu melden)
- Ebene(n): lexer | diagnostics
- Beschreibung: Anhang A LYR-LEX0003 „a digit outside the base after 0x/0o/0b“. `0b12` lexiert als `0b1` + `2` und liefert `PAR0016: expected ';'`; `0o9` liefert LEX0004 („empty literal“).
- Repro (verifiziert: ja): `let x = 0b12;` → PAR0016. Soll: LEX0003.
- Betroffene Teammitglieder informiert: lexer-parser

### [spec-conformance] LYR-IR0001 für sprachlich UNGÜLTIGE Konstrukte (Marker-Interface-Wert, Struct in f-string, `?(?T)`)
- Severity: LOW (Spec-Lücke + Code-Bereich)
- Datei: src/Lyric.Frontend/Ir/Lowering (IR0001-Emissionen „interface declares no methods“, „interpolating a non-scalar value“, „a nested optional '??T'“)
- Ebene(n): ir | diagnostics | spec
- Beschreibung: §12.1: IR = „valid Lyric this implementation cannot lower“. Drei Fälle, die die Spec als UNGÜLTIG definiert, werden mit IR0001 („cannot lower it yet“) abgelehnt: (1) Wert eines Marker-Interfaces (§5: „constructing a VALUE of it is refused“ — ohne Code, Spec-Lücke); (2) Struct/Class/Optional als f-string-Loch (§6.6 „is an error“ — ohne Code außer SEM0006 für opaque, Spec-Lücke); (3) `let m: ?(?int) = null;` (§3.3 „??T is not a type“ — Grammatik erlaubt aber `?(?T)` via GroupedType, Spec-Lücke). Anhang A.5 nennt für IR0001 abschließend nur `&&=`/`||=`, catch-Interface und nicht terminierende Monomorphisierung; auch `s.v++` (Feld-Inkrement) und `E.P(1, y)` (Literal in Variant-Payload) laufen undokumentiert auf IR0001.
- Repro (verifiziert: ja): `interface Marker {} struct S :: [Marker] { v: int } fn main(): int { let m: Marker = S { v = 1 }; return 0; }` → IR0001.
- Betroffene Teammitglieder informiert: diagnostics

### [spec-conformance] Runner-Hinweis: Suite ohne `--toolchain-version` meldet 158/159 (until-3.0.0-Fall); mit `--toolchain-version 4.4.1` 158/158, 1 skipped
- Severity: LOW (kein Bug; Doku)
- Datei: ~/dev/projects/lyricspec/tools/run_conformance.py:128 (Kommentar sagt „Omitted means: run everything“; ein `until`-Fall fällt dann zwangsläufig durch)
- Ebene(n): spec
- Beschreibung: Der Fall `06-operators/operator_is_the_interface_method.lyr` (`until: 3.0.0`) wird ohne Versionsangabe ausgeführt und scheitert erwartungsgemäß. Nebenbefund: dieselbe SEM0026-Diagnose wird dabei FÜNFMAL identisch ausgegeben (`generic type 'Add' expects 2 type argument(s), got 1` an 9:16) — Duplikat-Emission an diagnostics gemeldet.
- Betroffene Teammitglieder informiert: diagnostics

### [ir-codegen] defer: inline emittierte defer-Bodies liegen INNERHALB der eigenen finally-Region — ein throw aus einem defer-Body führt die defers ein zweites Mal aus
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:786-797 (LowerScope: `EmitDefers(pending)` inline VOR `end = new BlockId(_blocks.Count)`), :972-984 (LowerReturn: EmitAllPendingDefers im Block der Return-Anweisung, ebenfalls innerhalb des Bereichs), :987-1002 (break/continue analog)
- Ebene(n): ir
- Beschreibung: Die Normalpfad-Kopie der defer-Bodies wird emittiert, bevor `end` für die finally-Region [start, end) bestimmt wird; die Blöcke der Inline-Kopie liegen damit im geschützten Bereich. Wirft ein defer-Body (z.B. `defer close();` mit `throws`), fängt die eigene finally-Region das Unwinding und führt ALLE defers des Scopes erneut aus — der werfende Body läuft zweimal, und die davor registrierten (LIFO danach fälligen) laufen beim zweiten Durchlauf ebenfalls nicht, weil der zweite Durchlauf wieder an derselben Stelle wirft. Korrekt: `end` vor dem Inline-Emit fixieren (die Inline-Kopie außerhalb der Region) oder die Region auf den Body-Bereich beschränken.
- Repro (verifiziert: ja): `fn work() throws Boom { defer println("A"); defer boom(); println("body"); }` mit `fn boom(): int throws Boom { println("boom() called"); throw Boom{n=1}; }` → Ausgabe: body / boom() called / boom() called / caught — boom() zweimal, "A" nie.
- Betroffene Teammitglieder informiert: spec-conformance (§7.5 sagt nichts zu einem throw im defer-Body; welches Verhalten ist normativ?), vm-bytecode (endfinally-Semantik unbeteiligt, nur zur Kenntnis)

### [ir-codegen] Enum-Variantenkonstruktion: Payload wird ohne Koerzion/structcopy gespeichert (Struct-Payload aliast das Original)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:2020-2021 (LowerVariantCall: `fields[i] = LowerExpr(arguments[i])`), :2038-2039 (LowerStructVariant: `values[field.Name] = LowerExpr(field.Value)`)
- Ebene(n): ir
- Beschreibung: Anders als LowerObjectInit/LowerTupleLiteral (LowerExprAs gegen den Feldtyp) werden Variant-Payloads roh gelowert: kein `structcopy` für einen Struct-Payload (Wertsemantik gebrochen — das Original teilt sich das Slot-Array mit dem Variantenfeld), keine `optsome`-Hülle für einen `?T`-Payload, kein `mkiface` für einen Interface-Payload (letztere zwei würde der Verifier als malformed IR melden → Absturz im Debug-Build, stille Fehlinterpretation im Release-Build ohne Verifier; die Sema fängt heute zumindest den ?T-Fall vorher). Korrekt: `LowerExprAs(arguments[i], layout.FieldTypes[i + 1])`.
- Repro (verifiziert: ja): `struct P { x: int }  enum Sh { Box(P), Empty }  var p = P{x=1}; let s = Sh.Box(p); p.x = 99; match (s) { Sh.Box(q) => println(q.x) … }` → druckt 99, erwartet 1.
- Betroffene Teammitglieder informiert: semantic (Koerzionsregeln an Variant-Argumenten: ?T/Interface-Payload)

### [diagnostics] Eingebettetes NUL-Zeichen beendet das Lexen still — Rest der Datei wird ignoriert
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:58 (`Current` liefert '\0' für Ende UND für ein echtes U+0000), :224-227 (Eof bei '\0')
- Ebene(n): lexer | diagnostics
- Beschreibung: Ein U+0000 im Quelltext ist vom Dateiende nicht unterscheidbar; alles danach wird ohne Diagnose verworfen. Erwartet: LYR-LEX0001 („unexpected character U+0000“), wie die Spec A.1 für Steuerzeichen vorsieht.
- Repro: `printf 'fn main(): int {\n return 0;\n}\n\0garbage ###\n' > nul.lyr; lyrc check nul.lyr` → `nul.lyr: ok` (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] LEX0001 bei Nicht-BMP-Zeichen: zweimal gemeldet, Meldung enthält einsames Surrogat ('�')
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:264, :1038-1044
- Ebene(n): lexer | diagnostics
- Beschreibung: Ein Emoji (Surrogatpaar) erzeugt zwei LYR-LEX0001 mit je einer UTF-16-Hälfte als „Zeichen“ (ungültiges UTF-16 in der Ausgabe), dazu je einen PAR0002/PAR0016-Folgefehler und am Ende ein SEM0022 auf dem Rest — 8 Diagnosen für ein Zeichen. Spec A.1: „reported as the character“. Erwartet: ein LEX0001 über das ganze Codepoint (`Rune`), Span 2 Einheiten.
- Repro: `let x = 1 😀 2;` → zweimal `unexpected character '�'` (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] f-String-Interpolation läuft über das Zeilenende hinaus; LEX0011 an falscher Position
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:161-166 (SkipTrivia frisst das '\n' vor der Prüfung `Current is '\0' or '\n'`), :782 (Span = aktuelle Position)
- Ebene(n): lexer | diagnostics | spec
- Beschreibung: Spec A.1 LEX0011: „including one broken open by a line end inside an interpolation“. Tatsächlich überlebt `f"{x` das Zeilenende, das `}` der Funktion schließt die Interpolation, und LEX0011 wird erst zwei Zeilen später (5:2) gemeldet — mit 7 Folgefehlern (PAR0014, PAR0016×3, PAR0002×2, PAR0018). Erwartet: LEX0011 am Zeilenende in Zeile 3, Span am f-String-Anfang, ohne Kaskade.
- Repro: `let s = f"{x` + Newline + `return 0;` + `}` (verifiziert: ja, probes/lex_fstr.lyr)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] Derselbe Literalfehler unter zwei Codes: LEX0004/LEX0006 (Lexer) plus PAR0006 (LiteralDecoder)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:426 (LEX0004), :471 (LEX0006); src/Lyric.Frontend/Parsing/LiteralDecoder.cs:79 (PAR0006)
- Ebene(n): lexer | parser | diagnostics
- Beschreibung: `0x` → LEX0004 „empty integer literal after prefix“ UND PAR0006 „invalid integer suffix: 'x'“ am selben Span; `1e` → LEX0006 UND PAR0006 „invalid integer suffix: 'e'“. Die zweite Meldung ist falsch (kein Suffix) und widerspricht der ersten. Bei `0b2` kommen zusätzlich PAR0016 und SEM0022 hinzu (4 Diagnosen für einen Tippfehler). Erwartet: nach einem LEX-Fehler ein Fehler-Token, das der Decoder nicht erneut bewertet.
- Repro: probes/lex3.lyr, probes/lex_edge.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] Zeilenkommentar endet nur an '\n' — CR-only-Dateien werden zu einer Zeile, Kommentar frisst den Rest
- Severity: LOW
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:287, :336 (`while (Current != '\n' ...)`); src/Lyric.Core/SourceManager.cs:37-40 (LineStarts nur an '\n')
- Ebene(n): lexer | diagnostics
- Beschreibung: Spec §1.2 nennt `\r` als Whitespace/Zeilenende. Bei CR-only-Zeilenenden wird `// comment` bis zum Dateiende gelesen; Folge: SEM0017/PAR0018 mit einem 70-Zeichen-Caret auf „Zeile 1“. Kein LEX-Fehler, keine Warnung. Erwartet: `\r` allein beendet einen Zeilenkommentar (oder wird als Zeilenende gezählt).
- Repro: probes/cr_only.lyr (`printf 'fn main(): int {\r let x: int = "a";\r // c\r return 0;\r}\r'`) (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] Parser-Meldungen zeigen interne TokenKind-Namen statt Quellschreibweise („got Semicolon“, „got RBrace“, „got BadChar“)
- Severity: LOW
- Datei: src/Lyric.Frontend/Parsing/Parser.cs:53, :370, :811; Parser.Statements.cs:20, :68; Parser.Patterns.cs:220; Parser.Declarations.cs:343
- Ebene(n): parser | diagnostics
- Beschreibung: `expected an expression, got Semicolon` / `got Equal` / `got FStringInterpEnd` / `got BadChar` — Enum-Namen, die der Autor nicht im Quelltext findet. Erwartet: `got ';'`, `got '='`, `got end of file`. Für Schlüsselwörter existiert bereits `Lexer.KeywordSpelling`; ein `TokenKind`→Lexem-Display fehlt.
- Repro: `let x = ;` → `expected an expression, got Semicolon` (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser

### [diagnostics] Sema-Meldungen zeigen interne Operator-Namen („operator 'Eq'“, 'Add', 'Lt', '++/--')
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:5352 (`{b.Operator}` = BinaryOp-Enum), :5340
- Ebene(n): sema | diagnostics
- Beschreibung: `operator 'Add' is not applicable to 'string' and 'int'`, `operator 'Lt' ...`, `operator 'Eq' ...`. Erwartet: `'+'`, `'<'`, `'=='` wie in SEM0059, die bereits `==`/`!=` schreibt.
- Repro: `let x = "a" + 1;` (verifiziert: ja, probes/sem_ops.lyr)
- Betroffene Teammitglieder informiert: semantic

### [diagnostics] `?int == 3` meldet SEM0003 statt SEM0059 (Spec: Optional gegen Nicht-null-Wert ist SEM0059)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1929-1931 (Eq/Ne: `!LyrType.Equal(l, r)` → BadBinary), :1963-2006 (CheckEquatable wird für gemischte Typen nie erreicht)
- Ebene(n): sema | spec | diagnostics
- Beschreibung: Appendix A: SEM0059 = „`==`/`!=` not defined: an optional compared against a non-null value, or a type without Equatable“. Implementiert: `?int == int` fällt vor CheckEquatable in den Typgleichheitstest und wird als SEM0003 „operator 'Eq' is not applicable to '?int' and 'int'“ gemeldet; nur `?int == ?int` erreicht SEM0059 mit der hilfreichen Meldung („narrow it first“). Gleicher Fehler, zwei Codes je nach rechter Seite.
- Repro: `let p: ?int = null; if (p == 3) {}` → LYR-SEM0003 (verifiziert: ja, probes/sem_opt_eq.lyr)
- Betroffene Teammitglieder informiert: semantic, spec-conformance

### [diagnostics] Unaufgelöster Typname: RES0002 in Deklarationen, SEM0011 in Funktionskörpern — identische Meldung, zwei Codes
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Resolver/Resolver.cs:397 (RES0002 „unresolved type“); src/Lyric.Frontend/Sema/TypeChecker.cs:5171 (SEM0011 „unresolved type“)
- Ebene(n): sema | spec | diagnostics
- Beschreibung: `struct S { v: Nope }` und `fn f(x: Nope2): Nope3` → LYR-RES0002; `let a: Nope4 = 1;` im Body → LYR-SEM0011 — wortgleiche Meldung „unresolved type 'X'“. Wie SEM0070/SEM0097 (STATUS.md): welcher Code kommt, hängt von der Richtung ab, aus der der Fehler entdeckt wird. Tooling, das auf Codes matcht, sieht zwei Fehlerklassen. Spec A.3/A.4 beschreibt beide Codes überlappend („A type name that resolves to nothing“ vs. „Unknown or unusable type name: unresolved, …“); die Spec sollte die Grenze ziehen (z. B. RES0002 = Deklarationsposition, SEM0011 = nur Modul-/Nicht-Typ-Symbol) oder die Implementierung einen Code verwenden.
- Repro: probes/res_vs_sem.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic, spec-conformance

### [diagnostics] Unbekanntes Variantenfeld: SEM0015 im Initializer, SEM0031 im Pattern — identische Meldung
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:4051 (SEM0015 `variant 'P' has no field 'b'`), :4568 (SEM0031, gleicher Text)
- Ebene(n): sema | spec | diagnostics
- Beschreibung: `E.P { b = 1 }` → LYR-SEM0015; `match (e) { E.P { b } => … }` → LYR-SEM0031, gleiche Meldung. Spec A.4 ordnet „A field that does not exist on the struct, class or variant“ SEM0015 zu; das Struct-Pattern (`P { q }`) meldet auch SEM0015. Nur das Varianten-Pattern weicht ab. Genau das SEM0070/SEM0097-Muster aus STATUS.md.
- Repro: probes/variant_field.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic, spec-conformance

### [diagnostics] Fehlerhaftes Match-Pattern erzeugt Folgefehler SEM0017 „not all code paths return“
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:935 (SEM0017), :4568 (BindPoison nach SEM0031)
- Ebene(n): sema | diagnostics
- Beschreibung: Ein Match mit vergiftetem Arm (`E.P { b }` bei unbekanntem Feld) lässt die Flussanalyse `main` als nicht-zurückkehrend werten; SEM0017 steht dann VOR dem eigentlichen Fehler in der Ausgabe (Zeile 2 < Zeile 4). Erwartet: ein Arm mit Fehler-Pattern zählt für Exhaustiveness/Flow als abgedeckt (Error-Propagation).
- Repro: probes/variant_field2.lyr → SEM0017 + SEM0031 (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic

### [diagnostics] Fehlender Struct-Feld-Initializer wird als LYR-IR0001 („cannot lower it yet“) gemeldet — ein Semantikfehler unter dem Implementation-Limit-Code
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3176 (`initializer omits field '{field.Name}', which has no default`), :2045; src/Lyric.Frontend/Sema/TypeChecker.cs (StructInit-Check meldet fehlende Felder nicht)
- Ebene(n): sema | ir | spec | diagnostics
- Beschreibung: `struct S { v: int, w: string }` + `S { v = 1 }` → `error[LYR-IR0001]: initializer omits field 'w', which has no default` mit Note „this compiler version cannot lower it yet“. Das ist kein gültiges Lyric, sondern ein Typfehler; die Note verspricht eine spätere Compilerversion, die es nie geben wird. Spec §12/A.5: IR0001 = „valid Lyric this implementation cannot lower“, und A.5 listet nur `&&=`/`||=`, catch-Interface und Monomorphisierung. Ein SEM-Code für „fehlendes Feld ohne Default“ existiert weder in der Spec noch im Compiler (Spec-Lücke). Analog erreichen `??`/`?.`/`== null` auf Nicht-Optional die IR (siehe Eintrag von semantic).
- Repro: probes/ir_omit.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic, spec-conformance

### [diagnostics] Doppelter selektiver Import wird still akzeptiert; die Folge ist ein irreführendes SEM0086 mit zwei identischen Notes
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Resolver/Resolver.cs:246 (Funktions-Imports werden als Overload-Set-Einträge angelegt, TryDeclare-Ergebnis geht nicht durch DeclareImport :250-255)
- Ebene(n): sema | diagnostics
- Beschreibung: `import lib { other }; import lib { other };` → kein RES0001 (Spec A.3: „A name declared twice in the same scope: module …“), stattdessen beim Aufruf `other()`: `LYR-SEM0086: 'other' is ambiguous for ()` mit zwei Notes, die beide auf DIESELBE Deklaration lib.lyr:3:1 zeigen. Der Autor sieht einen Overload-Konflikt, wo nur ein Import doppelt ist. Ohne Aufruf: zwei SEM0072-Warnungen. Erwartet: RES0001 am zweiten Import (oder stille Dedup gleicher Symbole, aber dann ohne Ambiguität).
- Repro: probes/mf4/main.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic

### [diagnostics] SEM0019 „cannot assign to this target (not a mutable lvalue)“ fasst drei Ursachen zusammen, die der Autor unterschiedlich beheben muss
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/SemaRules.cs:173-174 (eine Meldung), :180-186 (IsMutableLvalue kennt die Ursache), :213-225 (IsFieldMutable kennt `this` in non-mut fn)
- Ebene(n): sema | diagnostics
- Beschreibung: Gleiche Meldung für (a) `let a = 1; a = 2;` → Behebung: `var`; (b) `1 = 2;` → kein lvalue; (c) `this.v = 3` in einer nicht-`mut` Methode → Behebung: `mut fn`. Der Fall (c) ist ohne Kenntnis der `mut fn`-Regel nicht erratbar; die Meldung nennt weder den Namen noch die Regel. Vorschlag (Code bleibt SEM0019): „'a' is immutable — declare it with 'var'“, „cannot assign to an expression“, „'get' is not a 'mut fn', so it may not assign to 'this.v'“.
- Repro: probes/sem_assign.lyr, probes/sem_mutfn.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic

### [diagnostics] SEM0058 Aritätsfehler beim Destructuring vergiftet die Namen nicht: jede Verwendung wird zum SEM0002 „unknown identifier“ mit falschem Did-you-mean
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:4356-4363 (SEM0058 ohne Deklaration der Pattern-Namen)
- Ebene(n): sema | diagnostics
- Beschreibung: `let (a, b, c) = t;` bei `(int, int)` → SEM0058 (korrekt) und danach `return a;` → `LYR-SEM0002: unknown identifier 'a' — note: did you mean 't'?`. Der Name IST deklariert; die Meldung und der Vorschlag sind irreführend (das STATUS.md-Muster „only parameters, locals and constants“ für deklarierte Namen). Erwartet: Namen mit ErrorType binden, keine Folgefehler.
- Repro: probes/sem_destr.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic

### [diagnostics] SEM0052-Hinweis „did you mean 'S { … }'?“ schlägt genau die Form vor, die geschrieben wurde; Statement-Position `S { v = 1 };` liefert 6 Diagnosen
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1355-1356; src/Lyric.Frontend/Parsing/Parser.Statements.cs (Statement beginnt mit `Name {` → Name + Block, Spec §6.8)
- Ebene(n): parser | sema | diagnostics
- Beschreibung: `S { v = 1 };` in Statement-Position → SEM0052 „'S' is a type, not a value — did you mean 'S { … }'?“ (der Autor hat exakt das geschrieben), SEM0022, PAR0016, SEM0002 „unknown identifier 'v' — did you mean 'S'?“, PAR0016, PAR0002. Erwartet: ein Fehler („a struct initializer cannot stand as a statement — bind it: `let _ = S { … }`“), Rest unterdrückt.
- Repro: probes/sem_stmt_structinit.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: semantic, lexer-parser

### [diagnostics] SEM0022-Meldung nennt „only calls, assignments and resume“, akzeptiert aber `x++;` (nicht `++x;`); Spec §6.8 kennt `++` gar nicht als Statement
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/SemaRules.cs:161-166
- Ebene(n): sema | spec | diagnostics
- Beschreibung: Meldung und Spec-Zeile (A.4 SEM0022, §6.8) stimmen überein, die Implementierung erlaubt zusätzlich Postfix-Inc/Dec. Entweder Spec+Meldung ergänzen oder `x++;` ablehnen. (semantic hat den `++x`/`x++`-Widerspruch bereits gemeldet.)
- Repro: `var i = 0; i++;` → ok; `i;` → SEM0022 (verifiziert: ja, probes/sem_exprstmt.lyr)
- Betroffene Teammitglieder informiert: spec-conformance

### [diagnostics] PAR0019 für fehlendes '(' nach Funktionsname — Spec ordnet die Parameterliste PAR0008 zu
- Severity: LOW
- Datei: src/Lyric.Frontend/Parsing/Parser.Declarations.cs:347
- Ebene(n): parser | spec | diagnostics
- Beschreibung: Appendix A: PAR0019 = „`(` missing after a keyword: if, while, for, match, catch“; PAR0008 = „A `(` or `)` the construct requires is missing (call, grouping, function type, parameter list)“. `fn foo: int {}` meldet PAR0019. Einer von beiden (Spec oder Code) muss angepasst werden.
- Repro: Code-Lesung; `fn foo: int { return 1; }` (verifiziert: Code ja, Lauf nein)
- Betroffene Teammitglieder informiert: spec-conformance, lexer-parser

### [diagnostics] `--json`: CLI-Diagnosen (CLI0016 --deny-warnings, CLI0008 Ausgabe, CLI0017 lyric.json) werden als Text HINTER das JSON geschrieben
- Severity: MEDIUM
- Datei: src/Lyric.Core/CliDiagnostics.cs:84-100 (`Fail`/`Warn` rendern immer Text); src/Lyrc/Program.cs:113-120 (DeniedWarnings nach Render), :71-77 (CLI0008), :183-186 (CLI0017 vor Render)
- Ebene(n): cli | diagnostics
- Beschreibung: `lyrc check w.lyr --json --deny-warnings` → `{"diagnostics":[…]}error[LYR-CLI0016]: 1 warning denied by --deny-warnings` auf stderr — ein JSON-Konsument bekommt ungültige Eingabe, und der Grund für Exit 1 fehlt im JSON. Dasselbe für CLI0008 (`{"diagnostics":[]}error[LYR-CLI0008]: cannot write …`) und CLI0017. RenderJson schreibt zudem kein abschließendes Newline. Erwartet: CLI-Diagnosen in den gemeinsamen Engine-Lauf aufnehmen (oder als eigenes JSON-Objekt) und `\n` nach dem JSON.
- Repro: probes/t2.lyr mit `--json --deny-warnings`; `lyrc build ok.lyr -o nowrite/ok.lyrbc --json` (verifiziert: ja)
- Betroffene Teammitglieder informiert: cli-security

### [diagnostics] lyrvm ignoriert `--json` vollständig: BC-Fehler, VM-Startfehler und Panics sind immer Text; `lyric run --json` liefert JSON gefolgt von Panic-Text
- Severity: MEDIUM
- Datei: src/Lyric.Vm/VmHost.cs:44-56 (Panic/RuntimeException als Text), :76-82 (`Load` → RenderText ohne Options); src/Lyrvm/Program.cs:46, :75, :96 (`VmHost.Load(bytes, Console.Error)`), :186 (Help verspricht „--json Diagnostics (and 'info') as JSON“)
- Ebene(n): vm | cli | diagnostics
- Beschreibung: `lyrvm verify bad.lyrbc --json` → `error[LYR-BC0001]: not a .lyrbc file …` (Text). `lyric run div.lyr --json` → `{"diagnostics":[]}panic [LYR-VM0002]: division by zero` — JSON und Panic auf demselben Stream ohne Trenner. Spec §12.3 verlangt Code + Exit 101, Format ist QoI, aber die Hilfe verspricht JSON. Erwartet: Panics/Ladefehler unter `--json` als JSON (`{"panic":{"code":…,"message":…,"stack":[…]}}`) oder Help korrigieren.
- Repro: probes/bad.lyrbc, probes/vm_div.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode, cli-security

### [diagnostics] Unbekannte Optionen (`--deny-warning`, `--jsn`) und `-o` ohne Wert werden still ignoriert — kein LYR-CLI0003/CLI0002
- Severity: MEDIUM
- Datei: src/Lyrc/Program.cs:227-238 (`Flag`/`Present` suchen nur bekannte Namen; kein Rest-Check), :62-63 (`-o` ohne Wert → Default-Ausgabe)
- Ebene(n): cli | diagnostics
- Beschreibung: `lyrc check f.lyr --deny-warning` (Tippfehler) → Exit 0, Warnungen nicht verweigert; `lyrc build f.lyr -o` → schreibt `f.lyrbc` still neben die Quelle. Spec A.6 CLI0003 „Unknown command or option“ wird für Optionen nie ausgegeben. (cli-security hat den Spiegelfall „Option in Dateiposition → CLI0001“ bereits eingetragen.)
- Repro: probes/t2.lyr mit `--deny-warning --jsn`; `lyrc build ok.lyr -o` (verifiziert: ja)
- Betroffene Teammitglieder informiert: cli-security

### [diagnostics] Sortierung ist nicht stabil: bei >16 Diagnosen kippt die Reihenfolge gleicher (Datei, Span, Code)
- Severity: LOW
- Datei: src/Lyric.Core/DiagnosticEngine.cs:76-81 (`List.Sort` = Introsort, instabil); src/Lyric.Core/DiagnosticsComparer.cs:5-16 (Tie-Break endet beim Code, nicht bei Message/Einfügereihenfolge)
- Ebene(n): diagnostics
- Beschreibung: 12 Funktionen `fn fN(params x: int, y: int)` → 24 SEM0024; in Zeile 6 erscheint „requires an array type“ VOR „must be the last parameter“, in allen anderen Zeilen umgekehrt. Deterministisch, aber abhängig von der Gesamtzahl der Diagnosen — Golden-Tests und Conformance-Pins („pin the first“) werden zerbrechlich. Erwartet: Message als letzter Tie-Break oder stabile Sortierung (OrderBy).
- Repro: probes/sort_stab.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [diagnostics] Caret-Zeile bei Tabs verschoben; Spaltenzählung in UTF-16-Einheiten ohne Dokumentation
- Severity: LOW
- Datei: src/Lyric.Core/DiagnosticEngine.cs:101-103 (Spaces bis `Column-1`, Quellzeile enthält aber Tabs), :107-112 (Caret-Anzahl = UTF-16-Länge)
- Ebene(n): diagnostics
- Beschreibung: Bei `\tlet x: int = "a";` steht der Caret unter Spalte 2 der Ausgabezeile, die Zeile beginnt aber mit einem Tab (Breite 4–8) — Caret zeigt auf `\t`, nicht auf `let`. Emoji vor dem Fehler zählen 2 Spalten; `column` im JSON ist UTF-16 (wie LSP), was nirgends dokumentiert ist. Erwartet: Whitespace der Quellzeile in der Caret-Zeile übernehmen (Tab → Tab); JSON-Feldsemantik dokumentieren.
- Repro: probes/tab.lyr, probes/astral.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [diagnostics] IR0001-Meldung „catching a specific interface is not supported by this compiler version yet“ + Note „this compiler version cannot lower it yet“ — genau die Doppelung, die LoweringDiagnostics.cs verbietet
- Severity: LOW
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:719; src/Lyric.Frontend/Ir/Lowering/LoweringDiagnostics.cs:20-27
- Ebene(n): ir | diagnostics
- Beschreibung: Der Kommentar in LoweringDiagnostics erklärt, warum die Kategorie als Note und nicht im Satz steht; die catch-Interface-Stelle trägt sie trotzdem im Satz. Außerdem verwenden drei Pattern-Meldungen `.GetType().Name` („a TuplePattern in a match over an enum“) — .NET-Klassennamen in Benutzermeldungen (FunctionLowerer.cs, grep `GetType().Name`).
- Repro: probes/ir_catch_iface.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen

### [diagnostics] Pfadangabe inkonsistent: Entry-Datei relativ wie angegeben, importierte Module absolut (auch in Notes und JSON)
- Severity: LOW
- Datei: src/Lyric.Frontend/Compiler/SourceCompiler.cs (Loader registriert importierte Module mit vollem Pfad; Entry mit dem übergebenen Pfad)
- Ebene(n): cli | diagnostics
- Beschreibung: `mf2/main.lyr:2:1: error …` neben `/tmp/…/probes/mf2/lib.lyr:2:24: error …` in einem Lauf; Notes in SEM0086 zeigen absolute Pfade. Editor-Problem-Matcher und JSON-Konsumenten müssen beide Formen behandeln. Erwartet: einheitlich (relativ zum cwd oder beide absolut).
- Repro: probes/mf2/main.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: cli-security

### [diagnostics] VM0004-Backtrace druckt alle 1024 Frames identisch
- Severity: LOW
- Datei: src/Lyric.Vm/VmHost.cs:48-49
- Ebene(n): vm | diagnostics
- Beschreibung: `panic [LYR-VM0004]: call depth exceeded 1024 frames in 'main.r'` gefolgt von 1024 Zeilen `in main.r (vm_depth.lyr:1)`. Erwartet: Wiederholungen zusammenfassen („… ×1023“) oder auf N Frames kürzen.
- Repro: probes/vm_depth.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode

### [diagnostics] Sema läuft nach Parse-Fehlern und meldet Folgefehler auf Recovery-Knoten (SEM0010 „binding 'resume' needs a type“, SEM0022 auf Restausdrücken)
- Severity: LOW
- Datei: src/Lyric.Frontend/Compiler/SourceCompiler.cs:112-121 (Analyze ohne Parse-Gate, bewusst für den Editor); src/Lyric.Frontend/Parsing/Parser.Statements.cs:66-71 (Expect-Fehlschlag liefert Token als Namen)
- Ebene(n): parser | sema | diagnostics
- Beschreibung: `let resume = 1;` → PAR0020 (richtig, mit Keyword-Note), außerdem SEM0010 „binding 'resume' needs a type or an initializer“ (sortiert VOR dem Parse-Fehler), PAR0016, PAR0002, PAR0016, SEM0022 — 6 Diagnosen. Erwartet: Bindungen/Statements aus fehlgeschlagener Recovery als Error-Knoten markieren, die Sema überspringt.
- Repro: probes/par2.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser, semantic

### [optimizer] Nachtrag zu "ScalarReplacement: Feldschreibzugriff über altes Objekt" — auch über Methodenaufruf (Inliner-Pfad)
- Severity: HIGH (gleicher Fehler, zweiter Weg)
- Datei: src/Lyric.Frontend/Ir/ScalarReplacement.cs:467-513; src/Lyric.Frontend/Ir/Inliner.cs:131-207 (Splice) + ScalarReplacement.cs:104-165 (ForwardLocals leitet `__inl_this` an den Receiver-Temp weiter)
- Ebene(n): optimizer
- Beschreibung: Derselbe Fehler ohne direkte Feldzuweisung: `p.setX((p = P{x=10,y=20}).y)` mit `mut fn setX(v) { this.x = v }` — Receiver wird vor den Argumenten geladen, das Argument ersetzt `p`, die geinlinte Methode schreibt über den alten Receiver (nach Forwarding: `storefield t_alt.x`), nach Scalarization landet der Wert in den geteilten Feld-Locals von `p`. Ohne Inlining bleibt der `call` eine Escape-Nutzung und die Gruppe wird nicht gebildet, d.h. der Fehler entsteht erst durch Inliner + ScalarReplacement zusammen. Class-Variante: `n.setV((n = Node{v=10}).v + 5)` → optimiert n.v=15, unoptimiert n.v=10.
- Repro: tests/OptDiff/cases/t_assign_expr2.lyr (mein Worktree): optimiert `struct via method: p.x=20` / `class via method: n.v=15`; unoptimiert `p.x=10` / `n.v=10`. (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen, cross-layer-reviewer (bereits zum Hauptfund)
### [lexer-parser] `fn f(): fn() -> int throws E { … }` — die `throws`-Klausel der Funktion wird dem Funktionstyp-Rückgabetyp zugeschlagen
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Parsing/Parser.cs:354 (ParseType(allowThrows: false) für den Rückgabetyp), :816-828 (ParseFunctionType ruft für seinen Rückgabetyp `ParseType()` mit allowThrows=true), :763-768
- Ebene(n): parser | sema | spec
- Beschreibung: Der Rückgabetyp einer Funktion wird bewusst mit `allowThrows: false` gelesen, damit `throws` dort die FUNKTIONS-Klausel bleibt (Spec §4: "It is not parsed after a function's return type: the throws there is the FUNCTION's clause and remains so"). `ParseFunctionType` gibt dieses Flag aber nicht an den eigenen Rückgabetyp weiter: in `fn f(): fn() -> int throws E { … }` wird `int throws E` zum ThrowingType INNERHALB des Funktionstyps, `f` bekommt keine Klausel. Sema meldet dann `LYR-SEM0084 'throws' belongs to a coroutine type, and 'int' is not one` — für eine Signatur, die laut Spec gültig ist und `f` als werfende Funktion deklariert. Mit `-> Coroutine<int> throws E` wird die Signatur sogar stillschweigend umgedeutet (Rückgabe eines Funktionstyps mit werfender Coroutine statt werfender Funktion). Fix: `ParseFunctionType(bool allowThrows)` das Flag an `ParseType(allowThrows)` für den Rückgabetyp durchreichen (die Parametertypen dürfen weiterhin `throws` tragen, sie stehen in Klammern). Gleiches gilt für `?fn() -> int throws E` als Rückgabetyp.
- Repro: `fn g(): int { return 1; }\nfn f(): fn() -> int throws E { return g; }` → `lyrc check` meldet SEM0084 statt die Klausel an `f` zu binden (verifiziert: ja; AST-Dump: `Fn f → FunctionType → Throwing(int, E)`, kein ThrowsClause)
- Betroffene Teammitglieder informiert: semantic (SEM0084 trifft eine gültige Signatur), spec-conformance

### [lexer-parser] Byte-Order-Mark wird nur von File.ReadAllText entfernt, nicht vom Lexer — virtuelle Quellen (Embedding, LSP) bekommen LYR-LEX0001
- Severity: LOW
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:62 (IsWhitespace), :153 (`_pos = 0`, kein BOM-Skip); src/Lyric.Core/SourceManager.cs:31 (AddVirtual)
- Ebene(n): lexer | embedding | spec
- Beschreibung: Grammatik §1.1: "A byte-order mark at offset 0 is skipped." Der Lexer kennt U+FEFF nicht; ein Text, der mit BOM beginnt, liefert `BadChar` + `LYR-LEX0001 unexpected character '﻿'` (unsichtbares Zeichen in der Meldung). Für Dateien von der Platte verdeckt `File.ReadAllText(path, Encoding.UTF8)` das (StreamReader entfernt das BOM), und `lyrfmt --stdin` ebenso (Console.In). Jeder Pfad über `AddVirtual` mit hostseitigem Text — `ScriptSource` mit Text (Embedding-Host, der z. B. `Encoding.UTF8.GetString(bytes)` benutzt, was das BOM NICHT entfernt), LSP-Dokumenttext, REPL — trifft die Lücke. Fix: im Lexer-Konstruktor `if (_source.Length > 0 && _source[0] == '﻿') _pos = 1;` (Spans bleiben dann Text-Offsets; SourceManager.Locate ist unbetroffen).
- Repro: `new SourceManager().AddVirtual("x", "﻿fn main(): int { return 0; }")` → LEX0001 an 1:1 (verifiziert: ja, Probe `tokens --raw`; lyrc/lyrfmt von Datei bzw. stdin sind NICHT betroffen)
- Betroffene Teammitglieder informiert: embedding-parity

### [lexer-parser] Diagnostik-Folgefehler an Lexer/Parser-Grenzen (Sammel-Eintrag)
- Severity: LOW
- Datei: src/Lyric.Frontend/Parsing/LiteralDecoder.cs:75-79 (PAR0006 nach LEX0006/LEX0004), src/Lyric.Frontend/Lexing/Lexer.cs:411-434 (kein Fehler für `0b12`), :1038-1044 (ReportBadCharacter mit halbem Surrogatpaar), src/Lyric.Frontend/Parsing/Parser.cs:802-806 (`<` nach Typname in `as`-Position)
- Ebene(n): lexer | parser | diagnostics
- Beschreibung: (a) `let x = 1.5e;` → LEX0006 UND `PAR0006 invalid integer suffix: '.5e'`; `let x = 0x;` → LEX0004 UND `PAR0006 invalid integer suffix: 'x'` — der Decoder dekodiert ein Token, das der Lexer bereits als fehlerhaft gemeldet hat, und erfindet einen zweiten Fehler. (b) `0b12` wird ohne Lexer-Diagnose zu `0b1` + `2`; der Nutzer sieht "expected ';'" und "expression statement has no effect" an Spalte 29 — kein Hinweis auf die ungültige Binärziffer (analog `0o8`, `123abc`). (c) Ein Nicht-ASCII-Zeichen außerhalb der BMP (`let 😀 = 1;`) erzeugt ZWEI LEX0001 mit je einem einsamen Surrogat als "Zeichen" ('�' in der Ausgabe) plus je einen PAR-Folgefehler; ScanChar/ReportBadCharacter sollten Surrogatpaare als ein Zeichen behandeln (siehe Eintrag zum Zeichenliteral). (d) `let c = a as int < b;` → `PAR0009 expected '>' to close type arguments, got Semicolon` — nach `as` wird `<` immer als Typargumentliste gelesen (grammatikkonform), aber die Meldung nennt nicht die naheliegende Absicht (Vergleich; Klammern um den Cast setzen).
- Repro: siehe Beschreibung, jeweils `lyrc check` (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [diagnostics] Keine Deduplizierung in DiagnosticEngine: identische Diagnose (Code, Span, Message) fünffach; Folgefehler nennt unsubstituierten Typparameter 'R'
- Severity: MEDIUM
- Datei: src/Lyric.Core/DiagnosticEngine.cs:36-40 (`Report` hängt blind an); src/Lyric.Frontend/Sema/TypeChecker.cs (SEM0026 an der Konformanzliste wird bei jeder Auflösung von `Add<Vec>` erneut gemeldet — 5 Aufrufer)
- Ebene(n): sema | diagnostics
- Beschreibung: `struct Vec :: [Add<Vec>]` (2.x-Form) → `LYR-SEM0026: generic type 'Add' expects 2 type argument(s), got 1` FÜNFMAL an 9:16, wortgleich; danach `LYR-SEM0042: 'Vec.add' does not match interface 'Add': returns 'Vec', expected 'R'` — 'R' ist der Parametername der Schnittstelle, kein Typ des Programms — und zweimal SEM0003 an den Operatorstellen. 8 Diagnosen, 1 Ursache. Erwartet: (a) DiagnosticEngine dedupliziert exakte Duplikate (Code+Span+Message) oder die Konformanz-Auflösung cached ihr Ergebnis; (b) eine fehlgeschlagene Konformanz vergiftet ihre Signaturprüfung (kein SEM0042 mit 'R').
- Repro: lyricspec/conformance/cases/06-operators/operator_is_the_interface_method.lyr mit `lyrc check` (verifiziert: ja; von spec-conformance gemeldet, hier auf die Engine bezogen)
- Betroffene Teammitglieder informiert: semantic

### [cross-layer-reviewer] Lader-Prüfungen (BytecodeReader.Validate) decken nicht alles ab, worauf der Interpreter "unchecked" vertraut — Sammelfund aus Code-Lesung
- Severity: MEDIUM (Sammelfund; einzelne Punkte LOW)
- Datei: src/Lyric.Core/Bytecode/BytecodeReader.cs:863-1045 (ValidateOperands), :1220-1278 (ValidateStack); src/Lyric.Core/Bytecode/CodeDecoder.cs:313 (NewArray-Pops), :348-355 (NewVariant/MakeClosure/CallIndirect-Effekte); src/Lyric.Vm/Interpreter.cs:548-590 (CallIndirect), :807-813 (NewArray), :908-916 (NewVariant), :1406-1427 (Prepared.From/BlockOfInstruction); src/Lyric.Vm/LoadedProgram.cs:117-118 (GlobalInit-Import)
- Ebene(n): vm | spec
- Beschreibung (jeweils Invariante des Erzeugers, die der Konsument voraussetzt, ohne dass der Lader sie prüft):
  1. `newarr`: der ULEB-Immediate wird in CodeDecoder.StackEffect UND in Interpreter.cs:809 per `(int)` verengt; kein Bereichscheck. Ein Wert ≥ 2^31 ergibt negative Pops (Stack-Validierung rechnet damit weiter) und zur Laufzeit `new LyrValue[negativ]` → OverflowException statt LYR-BC0004/Panic. Spec §Arrays: "never a process abort".
  2. `newvariant <T>`: ValidateOperands prüft nur `T < Types.Count`, nicht dass T ein Varianten-Layout eines Enums ist. Für ein Layout mit 0 Feldern ist variantArity = -1 (Validate akzeptiert), zur Laufzeit schreibt Interpreter.cs:914 `slots[0]` in ein leeres Array → IndexOutOfRangeException; TagOf wirft für Nicht-Varianten LyricRuntimeException (ok), erreicht die Stelle aber erst nach dem Array-Zugriff.
  3. `callind`: Arity und Rückgabe-Flag stammen aus dem Immediate; weder Lader (kann er per Spec nicht: Funktionswert trägt keine Signatur) noch Laufzeit vergleichen sie mit `ParamCount`/`ReturnType` des Ziels. Interpreter.cs:577 schreibt `callFrame.Slots[offset+i]` ohne Grenze (zu viele Args → IndexOutOfRange), zu wenige → Slots bleiben default; das Rückgabe-Flag wird beim `ret` (Interpreter.cs:935ff) aus dem Callee-Typ genommen, der validierte Stack-Stand des Callers stimmt dann nicht mehr. Ebenso wird der Wert unter den Argumenten nicht daraufhin geprüft, ein Closure-Wert zu sein (`ClosureFunction` aus beliebigen Bits → `prepared[index - natives.Length]` außerhalb).
  4. Block-Offsets müssen laut Lader nur auf Instruktionsgrenzen liegen, nicht aufsteigend sein; Prepared.From (Interpreter.cs:1420-1426) baut BlockOfInstruction aber unter der Annahme aufsteigender Offsets → falsche Handler-Zuordnung bei nicht-monotoner Tabelle.
  5. `GlobalInit`, der auf einen Import zeigt, besteht ValidateGlobals (nur "im Aufrufraum"), wird in LoadedProgram.Load:117 aber still übersprungen — Globals bleiben uninitialisiert (default), obwohl Spec §Globals einen Initializer verlangt.
  6. Grundsätzlich: ValidateStack prüft nur Tiefe, nicht Typen. Jeder `AsObject`-Cast (LyrValue.cs:130) auf einen Nicht-Referenzwert ist eine InvalidOperation/InvalidCastException, die VmHost (VmHost.cs:42-49) nicht fängt (nur LyricPanic/LyricRuntimeException) → Prozess endet mit .NET-Trace statt LYR-VM-Diagnose. Spec §6 verlangt nur Tiefe — Spec-Lücke vs. "may then run it without safety checks".
- Repro: nur mit handgebauten Modulen (Muster: tests/Lyric.Tests.Bytecode/ReaderValidationTests.cs `Module(...)`/`FunctionSection(...)`). verifiziert: nein (Code-Lesung; Punkte 1-3 sind an den genannten Zeilen eindeutig).
- Betroffene Teammitglieder informiert: vm-bytecode, cli-security, spec-conformance

### [semantic] `x++`/`x--` auf einer `let`-Bindung wird akzeptiert und mutiert sie
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/SemaRules.cs:181-190 (WalkExpr prüft nur AssignExpr), :199 (IsMutableLvalue wird für Postfix/Prefix Inc/Dec nie befragt)
- Ebene(n): sema
- Beschreibung: `let x = 1; x++; println(x)` kompiliert ohne Diagnose und gibt `x=2` aus — Immutabilität von `let` (§7.1: JEDE Zuweisung an ein let ist SEM0019) wird durch die Inkrement-Operatoren umgangen. Ergänzt den [cross-layer-reviewer]-Fund zu Nicht-Lvalues (`5++`, `p.x++`): SemaRules muss Inc/Dec wie eine Zuweisung behandeln (Lvalue + Mutabilität).
- Repro: scratchpad/sem/t2c_letinc.lyr (verifiziert: ja, gibt x=2)
- Betroffene Teammitglieder informiert: cross-layer-reviewer

### [semantic] ExceptionAnalyzer kennt DestructuringStmt nicht → werfender Initializer entkommt `main` (VM0010)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:98-125 (AnalyzeStmt hat keinen `case DestructuringStmt`)
- Ebene(n): sema, vm
- Beschreibung: `fn mk(): (int, int) throws Boom { throw Boom {}; }  fn main(): int { let (a, b) = mk(); … }` kompiliert ohne SEM0034 (main deklariert nichts, kein try) und endet zur Laufzeit als `panic [LYR-VM0010]: uncaught exception of type 'Boom'` — genau der Zustand, den §9.2 als „aus Quelltext nicht erreichbar“ beschreibt. Der Initializer eines Destructurings wird nie analysiert (BindingStmt schon).
- Repro: scratchpad/sem/t13_destr_throw.lyr (verifiziert: ja, exit 101)
- Betroffene Teammitglieder informiert: vm-bytecode

### [semantic] Compound-Zuweisung auf ein narrowed `?T` → ir-verifier-Crash
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:2351-2400 (CheckAssign: Compound baut `BinaryExpr(target, op, value)`; das Target wird als narrowed `int` typisiert, die Zuweisung erwartet `?int`, nichts vermerkt den Unwrap)
- Ebene(n): sema, ir
- Beschreibung: `var o: ?int = 5; if (o != null) { o += 1; }` ist nach §7.4 gültig (im Narrowing ist o ein int, int assigniert an ?int) und stirbt als `InternalCompilationException: ir-verifier: operand types differ: t5 is ?i64, t6 is i64`. Das Lowering liest den Slot als ?i64, die Sema hat ihm den Operanden als i64 typisiert. Entweder muss die Sema den Load des narrowed Targets als checked unwrap markieren, oder das Lowering muss die Narrowing-Information nutzen.
- Repro: scratchpad/sem/t17_narrow_compound.lyr (verifiziert: ja, exit 82)
- Betroffene Teammitglieder informiert: ir-codegen

### [semantic] Block-Arme eines match-AUSDRUCKS werden von SemaRules nicht geprüft → `let` wird zur Laufzeit mutiert
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/SemaRules.cs:227 (Children(MatchExpr) liefert nur Expr-Arme; Block-Arme werden weder WalkStmt noch WalkExpr unterzogen)
- Ebene(n): sema
- Beschreibung: `let x = 1; let v = match (k) { 1 => { x = 9; return x; }, _ => 5 };` kompiliert ohne SEM0019; main liefert Exit-Code 9, d.h. die `let`-Bindung wurde tatsächlich überschrieben (kein Crash, stille Verletzung der Immutabilität). Ergänzend zum Lambda-Fund: WalkExpr muss in LambdaExpr-Bodies und in Block-Arme von MatchExpr absteigen.
- Repro: scratchpad/sem/t19_matchexpr_assign.lyr (verifiziert: ja, exit 9)
- Betroffene Teammitglieder informiert: -

### [semantic] Throwable-Konformität über `extend` wird nicht anerkannt (Spec §9.1)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/Conformance.cs:72-88 (IsThrowable → Implements → DeclaredInterfaces: nur die `::`-Liste der Deklaration, keine extend-Blöcke)
- Ebene(n): sema, spec
- Beschreibung: `class MyErr { code: int }  extend MyErr :: [Throwable] { fn message(): string {…} }` → `throws MyErr`, `throw MyErr {…}` und `catch (e: MyErr)` werden mit SEM0030 abgelehnt. §9.1 sagt ausdrücklich „directly, through an extend, or through an interface chain“. Gleiches Muster in ExceptionAnalyzer.CatchHandles/PermittedByDeclaration (Conformance.Implements) und in TypeChecker.TypeArgumentOfConformance:1131 (siehe nächster Eintrag).
- Repro: scratchpad/sem/t14_extend_throwable.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance

### [semantic] `for-in` erkennt Iterator-/Iterable-Konformität aus `extend`-Blöcken nicht (Spec §7.2, §5.5)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1121-1185 (TypeArgumentOfConformance: `Conformance.Implements` + nur `ClassDecl/StructDecl.Interfaces`)
- Ebene(n): sema, spec
- Beschreibung: `class Ones { left: int }  extend Ones :: [Iterator<int>] { mut fn next(): ?int {…} }` → `for (x in o)` meldet SEM0007 „not iterable“, obwohl die Konformität per extend (Orphan-Regel erfüllt, gleiches Modul) besteht und §5.5 extend-Konformitäten als vollwertig beschreibt. Dieselbe Funktion bedient `Indexable<T>` für `[i]`, also auch dort. Zudem sind Enums mit `:: [Iterator<T>]` ausgeschlossen (`_ => null` bei EnumDecl).
- Repro: scratchpad/sem/t18_extend_iterable.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance

### [semantic] Constraint-Prüfung lässt Arrays, Optionale, Tupel, Funktionstypen als „erfüllt“ durch
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:3223 (`Satisfies`: `_ => true // external or error`)
- Ebene(n): sema, diagnostics
- Beschreibung: Der Default-Arm gilt für ArrayOf, Optional, TupleOf, FnType, CoroutineOf, NullType — nicht nur für Error/External. `fn s<T :: [Show]>(x: T)` mit `s([1, 2])` oder `s(o)` (o: ?P) passiert SEM0028 und wird erst im Lowering mit `LYR-IR0001: call to 'show' on 'int[]'` (bzw. `'?P'`) als Compiler-Limit abgelehnt — ein Typfehler wird als Implementation-Limit gemeldet; im LSP erscheint keine Diagnose. Bei einem Constraint ohne Memberzugriff im Body (Marker-Interface) kompiliert es vermutlich sogar.
- Repro: scratchpad/sem/t7_constraint.lyr, t22_constraint_opt.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [semantic] Sema akzeptiert Struct-Werte als Throwable, IR lehnt erst das `catch` ab
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/Conformance.cs:78-80 (IsThrowable: NamedRef ohne Kind-Prüfung), TypeChecker.cs:1187/1197
- Ebene(n): sema, diagnostics
- Beschreibung: `struct SErr :: [Throwable] {…}`, `throws SErr`, `throw SErr {…}` gehen durch die Sema; erst `catch (e: SErr)` scheitert mit IR0001 „catching a non-class type“. §9.1: „Only class values … Structs do not throw“ — gehört als SEM0030 an die throw-/throws-Stelle.
- Repro: scratchpad/sem/t15_struct_throw.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [semantic] Parameter darf einen Typparameter gleichen Namens überschatten → IR0001
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:915-921 (Generics und Parameter im selben Scope, TryDeclare-Ergebnis ignoriert)
- Ebene(n): sema
- Beschreibung: `fn f<T>(T: int, x: T): int { return T; }` — der Parameter `T` verliert gegen den Typparameter, `return T` wird als Typname gebunden und endet als IR0001 „reference to 'T'“. Erwartet: Fehler an der Parameterdeklaration.
- Repro: scratchpad/sem/t27_param_shadow_generic.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [semantic] Fehlendes Pflichtfeld im Struct-Initializer ist kein Sema-Fehler (nur IR0001)
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:3800-3915 (CheckStructInit: keine Vollständigkeitsprüfung), analog CheckVariantInit
- Ebene(n): sema, diagnostics
- Beschreibung: `P { x = 1 }` bei `struct P { x: int, y: int }` → `LYR-IR0001: initializer omits field 'y', which has no default … this compiler version cannot lower it yet`. Das ist eine Sprachregel, kein Implementation-Limit; gehört als SEM-Fehler in CheckStructInit.
- Repro: scratchpad/sem/t10_missingfield.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [semantic] Generische variadische Funktion: Typargument wird nicht aus den Elementen inferiert
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:2681-2684 (Phase B: `n = Min(fn.Parameters.Length, args.Length)`, unifiziert `T[]` nur gegen das erste Argument)
- Ebene(n): sema, spec
- Beschreibung: `fn count<T>(params xs: T[]): int` mit `count(1, 2, 3)` → SEM0060 „cannot infer T“ plus drei SEM0001 „cannot assign 'int' to 'T'“. §8.3 Regel 3 („T[] against int[] binds the element“) trifft die variadische Position nicht; die Elementargumente müssten gegen das Elementtyp-Muster unifiziert werden (wie ExpectedParamAt es für den Kontext bereits tut). Spec sagt dazu nichts Explizites — evtl. Spec-Lücke.
- Repro: scratchpad/sem/t16_variadic_generic.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance

### [ir-codegen] Spec §13 Closures: "function index is stored incremented by one" — Writer und Reader kodieren OHNE +1
- Severity: LOW
- Datei: src/Lyric.Frontend/Emit/BytecodeWriter.cs:605-606 (`(importCount + target) << 1 | hasEnv`), src/Lyric.Core/Bytecode/BytecodeReader.cs:965-971 (`Immediate >> 1` gegen callable count, kein -1), src/Lyric.Core/Bytecode/CodeDecoder.cs:341; Spec ~/dev/projects/lyricspec/spec/13-bytecode.md:936-937
- Ebene(n): spec | emit
- Beschreibung: Die Spezifikation behauptet, der mkclosure-Zielindex sei "+1" gespeichert, damit "Funktion 0 ohne Environment" von "kein Wert" unterscheidbar sei. Writer, Reader und Disassembler benutzen `index << 1 | hasEnv` ohne Inkrement (Funktion 0 ohne Env ist Immediate 0, was als Instruktionsoperand völlig eindeutig ist — die "no value"-Begründung gehört zur Laufzeitrepräsentation, nicht zur Kodierung). Ein Fremd-Runtime nach Spec würde jeden mkclosure um eins verschoben binden. Satz in der Spec streichen oder die Kodierung ändern (Format-Bump).
- Repro: Codelesung (verifiziert: ja, Writer/Reader stimmen überein; Spec weicht ab)
- Betroffene Teammitglieder informiert: spec-conformance, vm-bytecode

### [ir-codegen] Verifier-only-Invarianten: im Release-Build (Pipeline.VerifiesIr = false) reicht das Lowering Sema-Lücken als stillen Falschcode durch, weil der Bytecode-Reader nur Indizes/Tags, aber keine Slot-/Feld-Typkonsistenz prüft
- Severity: MEDIUM
- Datei: src/Lyric.Core/Phase.cs:52-57 (VerifiesIr nur #if DEBUG), src/Lyric.Frontend/Ir/Lowering/ModuleLowerer.cs:44-51 (Design-Notiz), src/Lyric.Core/Bytecode/BytecodeReader.cs:880-1035 (Load-time-Checks: keine stloc/stfld/newarr/retval-Typprüfung)
- Ebene(n): ir | emit | vm
- Beschreibung: Alle Typkonsistenz-Checks (stloc-Typ, Argumenttyp gegen Callee-Slot, newarr-Elementtyp, retval-Typ, optsome-Inner) leben nur im IrVerifier. Im Debug-Build stürzt der Compiler bei einer Sema-Lücke mit InternalCompilationException ab (z.B. `let q = if (c) 1 else 2.5;` → "store of t1 (i64) into l2 (f64)"); im Release-Build (ausgelieferte Binaries) wird derselbe Fall zu einem gültig geladenen Modul mit falschem Bitmuster (spec-conformance hat 5e-324 gemessen). Der Reader könnte stloc/ldloc-Typen gegen SlotTypes und call-Argumente gegen die Callee-Signatur prüfen — die Informationen stehen im Modul (die Slot-Tabelle wird bereits gelesen). Kein eigener Lowering-Bug, aber die Sicherheitsnetz-Annahme aus ModuleLowerer.cs:44-51 gilt für Typfehler nicht.
- Repro (verifiziert: ja — Debug: Verifier-Absturz; Release-Beobachtung von spec-conformance): probe/t14.lyr
- Betroffene Teammitglieder informiert: vm-bytecode (Reader-Checks), semantic (SEM0016-Lücke ist die Ursache), spec-conformance

### [diagnostics] Interner Compilerfehler (InternalCompilationException) wird nicht als Diagnose gerendert: .NET-Stacktrace, Exit 134, kein Code, keine Position, JSON-Modus zerstört
- Severity: MEDIUM
- Datei: src/Lyrc/Program.cs:33-56 (try/catch fängt nur ProjectFileException); src/Lyric.Core/Exceptions.cs:7 (Exception trägt den Span nur als Text „FileId(1)[61..72)“); src/Lyric.Frontend/Compiler/SourceCompiler.cs:128-131 (kein Catch um ModuleLowerer.Lower)
- Ebene(n): cli | ir | diagnostics
- Beschreibung: `let x = if (c) 5 else panic("no");` (Sema akzeptiert; Fund von ir-codegen) → `Unhandled exception. Lyric.Core.InternalCompilationException: lowering: expression at FileId(1)[61..72) produced no value (in 'main.main')` + Stacktrace, Exit 134 (SIGABRT), unter `--json` derselbe Text statt JSON. Ein Editor/CI sieht weder Datei:Zeile noch einen LYR-Code. Erwartet: lyrc/lyric fangen InternalCompilationException, rendern „internal compiler error: … — please report“ mit der Position (die Exception sollte einen echten `Span` tragen statt `FileId(1)[61..72)` im Text) und beenden mit Exit 1; die Spec kennt keinen ICE-Code (bewusst?) — ein Hinweis in §12 wäre nötig.
- Repro: probes/ice.lyr, `lyrc check ice.lyr` (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen, cli-security

### [vm-bytecode] Compiler-Absturz (InternalCompilationException „block bb3 is already sealed") bei `defer { throw … }` innerhalb eines `try`
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:672 (LowerTry) → :810 (LowerScope) → src/Lyric.Frontend/Ir/Lowering/BlockBuilder.cs:61
- Ebene(n): ir
- Beschreibung: Ein `defer`-Block, der selbst wirft, in einem `try` mit passendem `catch` bringt das Lowering zum Absturz: `LowerScope` versiegelt den Block nach dem Defer-Body erneut, obwohl der `throw` ihn schon terminiert hat. `lyrc build` stirbt mit unbehandelter Exception und Exit 134 (kein Diagnostic). Semantisch ist das Programm gültig (Spec §9: defer läuft beim Verlassen; ein Wurf aus dem defer ersetzt die laufende Ausnahme / wird gefangen).
- Repro (verifiziert: ja): scratchpad/vm/t_exc_a.lyr — `fn deferThrows(): int { var r = 0; try { defer { throw E1 { n = 9 }; } r = 1; } catch (e: E1) { r = r + 10; } return r; }` → `Unhandled exception. Lyric.Core.InternalCompilationException: lowering: block bb3 is already sealed`.
- Betroffene Teammitglieder informiert: ir-codegen

### [diagnostics] Nachtrag zu „Zeilenkommentar endet nur an '\n' — CR-only-Dateien“
- Severity: LOW (herabgestuft: Spec-Frage)
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:287, :336
- Ebene(n): lexer | spec
- Beschreibung: lexer-parser weist darauf hin, dass Grammatik §1.1 nur `\n` und `\r\n` als Zeilenende nennt und ein nacktes `\r` Whitespace ist — das Verhalten ist damit spec-konform. Offen bleibt nur die QoI-Frage, ob ein Kommentar, der 4 „Zeilen“ verschluckt, eine Warnung wert ist; keine Aktion am Compiler nötig, ggf. Spec-Klarstellung.
- Repro: probes/cr_only.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance
### [lexer-parser] `///`-Dokumentation eines attributierten Members (`/// doc` + `@Deprecated fn f()`) geht stillschweigend verloren
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Parsing/Parser.Declarations.cs:440-443 (ParseTypeMembers: Attribute werden VOR `ParseTypeMember` gelesen, dessen `start` = Current NACH den Attributen), :462, :621-660 (ParseMethodSequence: `start = _buffer.Current.Span` nach ParseAttributeList); zum Vergleich :184 (Top-Level macht es richtig: `start = attributes[0].Span`); src/Lyric.Frontend/Parsing/TokenBuffer.cs:171 (Doc-Block wird an den Offset des ERSTEN Tokens nach ihm gebunden, hier das `@`)
- Ebene(n): parser | diagnostics (LSP-Hover/Completion)
- Beschreibung: `TokenBuffer` bindet einen `///`-Block an den Offset des nächsten Tokens; `Parser.DocOf(node)` schlägt mit `node.Span.Start` nach. Bei einem Member mit Attribut beginnt der Block-Offset beim `@`, der Member-Span aber erst hinter dem Attribut (bei `fn`, `pub` oder dem Feldnamen), weil `ParseTypeMember`/`ParseMethodSequence` `start` erst nach `ParseAttributeList()` nehmen. Ergebnis: `struct S { /// documented\n @Deprecated fn f() {} }` → `Documentation.Of(f)` ist null, der Block hängt in `DocComments` an einem Offset, den niemand nachschlägt. Betrifft Methoden, Felder, `static let` in struct/class sowie Interface-/Extend-/Enum-Methoden; Top-Level-Deklarationen mit Attribut funktionieren (dort umfasst der Span das Attribut). Konsumenten: LSP HoverProvider.cs:78, CompletionProvider.cs:154, ScopeCompletion.cs:179 — Hover auf ein `@Deprecated`-Member zeigt keine Doku, obwohl eine geschrieben wurde (und gerade dort wäre der Deprecation-Hinweis nützlich). Fix: `start` vor `ParseAttributeList()` nehmen (wie Top-Level), oder DocOf zusätzlich über `Attributes[0].Span.Start` nachschlagen.
- Repro: Probe `docs cases_mod2/doc_attr_member.lyr`: `FunctionDecl 'f' doc=<none>`, `UNBOUND doc block at offset 41: documented method`; `FunctionDecl 'g'` (ohne Attribut) `doc=plain method` (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics (LSP-Hover)

### [lexer-parser] Recovery: ein Member-Body, der nur ein Attribut enthält, verschluckt die schließende `}` des Typs
- Severity: LOW
- Datei: src/Lyric.Frontend/Parsing/Parser.Declarations.cs:435-445 (Schleifenbedingung prüft `}` VOR dem Attribut-Parsen; "force progress" konsumiert dann das `}`), analog :619-661 (ParseMethodSequence)
- Ebene(n): parser | diagnostics
- Beschreibung: `struct S { @A }\nfn main(): int { return 0; }` — nach `@A` steht `}`; `ParseTypeMember` liest ein leeres Feld (3 Fehler), macht keinen Fortschritt, und der Progress-Guard `_buffer.Advance()` frisst das `}` des Struct-Bodys. `fn main()` wird danach als METHODE von `S` geparst und `expected '}' to close type body` erst am Dateiende gemeldet — vier Meldungen, keine sagt "attribute without a member". Fix: nach `ParseAttributeList()` erneut auf `}`/EOF prüfen und PAR0042 ("an attribute must be followed by …") melden, statt in den Member-Parser zu laufen.
- Repro: `lyrc check` auf obige Datei → PAR0011/PAR0026/PAR0031 an 1:15 und PAR0018 an 3:1 (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [ir-codegen] defer-Body, der nicht durchfällt (throw/return/break), lässt EmitDefers weiterlaufen → "block already sealed" (Ergänzung zum [vm-bytecode]-Fund)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:841-844 (EmitDefers: `LowerStmt(pending[i].Body)` Rückgabewert ignoriert), :790 + :816-820 (Normalpfad siegelt/branch nach EmitDefers ohne Prüfung), :807-810 (cleanup-Block: `Seal(new EndFinally)` nach EmitDefers ohne `_b.IsSealed`-Prüfung), :857/:865 (EmitAllPendingDefers/EmitPendingDefersAbove analog)
- Ebene(n): ir | sema
- Beschreibung: Alle vier Emit-Pfade nehmen an, dass ein defer-Body durchfällt. Ein `defer { throw …; }` (oder ein defer mit `return`/`break`, falls die Sema das zulässt) siegelt den aktuellen Block; der nächste defer-Body bzw. das abschließende `Seal(EndFinally)`/`Seal(Branch)` wirft dann InternalCompilationException "block bbN is already sealed". Korrekt: Ergebnis von LowerStmt auswerten und bei false abbrechen (weitere defers sind dann unerreichbar — Semantik-Frage für §7.5: darf ein defer-Body werfen?), oder die Sema lehnt terminierende defer-Bodies ab.
- Repro (verifiziert von vm-bytecode: ja, scratchpad/vm/t_exc_a.lyr): `try { defer { throw E1{n=9}; } r = 1; } catch (e: E1) {…}` → "lowering: block bb3 is already sealed", exit 134.
- Betroffene Teammitglieder informiert: vm-bytecode (Meldender), semantic (Regel für terminierende defer-Bodies), spec-conformance (§7.5)

### [ir-codegen] Fehlendes Feld ohne Default im Initializer ist NUR im Lowering geprüft (IR0001 statt Sema-Fehler); für Struct-Varianten gar nicht
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3175-3177 (LowerObjectInit), :2044-2045 (LowerStructVariant)
- Ebene(n): sema | ir | diagnostics
- Beschreibung: `P { x = 1 }` für `struct P { x: int, w: int }` erzeugt keinen Sema-Fehler; erst das Lowering meldet LYR-IR0001 "initializer omits field 'w', which has no default" mit der Note "this compiler version cannot lower it yet" — ein Typfehler unter dem Implementation-Limit-Code (siehe auch [diagnostics]). Da IR0001 nur beim Lowern erreichbarer Funktionen entsteht, entgeht das z.B. `lyric check` ohne Lowering-Phase, falls eine solche Konfiguration existiert.
- Repro (verifiziert: ja): probe/t15.lyr → error[LYR-IR0001] initializer omits field 'w' (keine SEM-Diagnose).
- Betroffene Teammitglieder informiert: semantic, diagnostics

### [semantic] SEM0057 (Global vor Initialisierung gelesen) greift nur bei direktem Lesen, nicht über einen Aufruf
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1452-1462 (TypeOfGlobalReference: `_inGlobalInitializer` gilt nur für den Initializer-Ausdruck selbst), :221-268 (ComputeGlobals)
- Ebene(n): sema, spec
- Beschreibung: `pub let base = helper(); pub let later = 7; fn helper(): int { return later + 1; }` kompiliert ohne Diagnose und liefert `base=1` (later war beim Lesen 0). Mit einem Klassen-Global endet es als null-Dereferenz in der VM (siehe [vm-bytecode]-Eintrag mit .NET-Exception). §4.3 verlangt einen Fehler für „an initializer reading a global that comes later“; ein Read durch einen Funktionsaufruf ist ein solcher Read. Abhilfe: entweder eine Erreichbarkeitsanalyse über Funktionsbodies (wie Go) oder Aufrufe in Global-Initializern auf Funktionen beschränken, die keine späteren Globals erreichen — mindestens aber eine Spec-Klarstellung.
- Repro: scratchpad/sem/lib.lyr + t37_main.lyr (`lyric run t37_main.lyr` → base=1 statt 8) (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode (hatten den Crash-Fall bereits), spec-conformance

### [semantic] Enum mit optionalem Payload ist nur mit `_` erschöpfend matchbar
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:4376-4380 (BindPattern: Binding über `?T` bindet `T`), :4287 (IsIrrefutable: BindingPattern über Optional → false), §7.6-Limit für Or-Pattern mit Bindung
- Ebene(n): sema, spec
- Beschreibung: `enum O { Some(?int), Nothing }` — `match (o) { O.Some(v) => …, O.Nothing => … }` meldet SEM0050 „missing case(s): 'Some'“, weil `v` als `int` (unwrapped) gebunden wird und `Some(null)` damit nicht abgedeckt ist. Die einzige vollständige Schreibweise wäre `O.Some(null) | O.Some(v)`, und ein Or-Pattern mit Bindung ist ein Lowering-Limit (IR0001). Für den Nutzer bleibt nur `_`/`O.Some(_)` und ein späteres `!`. Entweder soll ein Binding im Payload den vollen Typ `?int` bekommen (Spec §7.4 spricht nur vom Top-Level-Scrutinee), oder Exhaustiveness soll `Some(null)`+`Some(v)` als getrennte Arme zählen. Von ir-codegen unabhängig gefunden (probe/t12b.lyr).
- Repro: scratchpad/sem/t39_opt_payload.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen, spec-conformance

### [semantic] `never` (panic) als Zweig von if-Ausdruck/`??` akzeptiert, in match-Armen abgelehnt
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:5067 (IsAssignable: NeverType assigniert überall hin), :4132 (Unify nutzt IsAssignable), :4318 (UnifyArms nutzt nur Equal)
- Ebene(n): sema, ir
- Beschreibung: `let x = if (c) 5 else panic("…")` und `o ?? panic("…")` passieren die Sema (Typ int) und crashen im Lowering („expression produced no value“, Fund von ir-codegen); `match` mit never-Arm gibt SEM0016. Sema-seitig sollte eine Entscheidung fallen (Spec §6.9 kennt nur Equal/null-Widening): entweder never überall in Unify erlauben und das Lowering zieht nach, oder IsAssignable in Unify nicht befragen (siehe auch Literal-Fund zu Unify).
- Repro: siehe [ir-codegen]-Eintrag (verifiziert: durch ir-codegen)
- Betroffene Teammitglieder informiert: ir-codegen

### [ir-codegen] Compound-Zuweisung auf ein flow-narrowed Optional (`o += 1` nach `if (o != null)`) → Verifier-Absturz (Ergänzung zum [semantic]-Fund)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1774-1781 (LowerAssign, Compound-Pfad: `type = ValueTypeOf(slot)` = ?i64, `LoadValue` ohne Narrow, EmitBinary mit i64-Operand, StoreValue ohne OptSome)
- Ebene(n): ir
- Beschreibung: Der Lese-Pfad (LowerIdentifier → Narrow) kennt das Flow-Narrowing, der Compound-Schreib-Pfad nicht: er rechnet mit dem Slot-Typ `?T`, bindet den rohen Slot-Wert als Operand (Verifier: "operand types differ: ?i64 vs i64") und müsste danach mit OptSome zurückschreiben. Gleiches gilt für LowerIncDec (`o++` nach Narrowing) und LowerCapturedAssign. Im Release-Build ohne Verifier: `add` auf einem Optional-Bitmuster. Korrekt: Narrow(expr.Target, current, type) lesen, mit TypeOfExpr(expr.Target) rechnen, Coerce(result, T, slotType) speichern.
- Repro (verifiziert: ja): `var o: ?int = 5; if (o != null) { o += 1; }` → ir-verifier "bb1: #2: operand types differ: t5 is ?i64, t6 is i64".
- Betroffene Teammitglieder informiert: semantic (Meldender)

### [ir-codegen] Call-Dispatch: Extension-Methode verliert gegen Interface-Default (Lowering ignoriert die Sema-Bindung)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3775-3788 (Case "INTERFACE DEFAULT method on a concrete receiver": Bedingung `concrete.Members.LookupLocal(member.Member) is not FunctionSymbol && InterfaceProviding(...)`), Vergleich :3593-3595 (LowerConstraintCall entscheidet ebenso nur über Members.LookupLocal)
- Ebene(n): ir | sema
- Beschreibung: Die Sema bindet `s.name()` korrekt an die Extension (own → extension → default); das Lowering wählt den callvirt-Pfad allein danach, ob der TYP selbst ein Member dieses Namens hat, und ignoriert `_types.RefOf(member)`. Eine Extension auf einem Typ, dessen Interface denselben Namen als Default liefert, wird also nie direkt aufgerufen; der Aufruf geht über mkiface+callvirt, und die vtable-Zeile (BuildImpls :924-938) resolvt "own → instance → extension → default" — hier gewinnt dann zwar ResolveInExtensions, aber nur für Extensions, die in `viaExtension` (Konformanz-Blöcke) stehen; ein plain `extend S { … }` ohne `:: [A]` ist dort nicht enthalten → Default. Korrekt: `_types.RefOf(member)` ist Member des Interface-Symbols ⇒ Default-Pfad, sonst direkter Call (TryResolveFunction kennt Extensions bereits).
- Repro (verifiziert: ja): `interface A { fn name(): string { return "default"; } } struct S :: [A] { v: int } extend S { fn name(): string { return "ext"; } }` → `s.name()` = "default", erwartet "ext" (§5.4).
- Betroffene Teammitglieder informiert: semantic (Meldender), spec-conformance (hat es als HIGH gelistet)

### [vm-bytecode] JIT-Panics aus `ldelem`/`stelem`/`optget` tragen keine Quellposition, `div`/`rem` schon — Backtrace divergiert je nach Opcode
- Severity: LOW
- Datei: src/Lyric.Vm/Jit/JitRuntime.cs:27-38 (Element/SetElement), :127-132 (Unwrap), :181-187 (Checked) vs. :143-172 (DivI64/RemI64 mit `where`); src/Lyric.Vm/Jit/JitCompiler.cs:1015-1026 (`Where(at)` nur für Div/Rem)
- Ebene(n): vm, diagnostics
- Beschreibung: Die Division bekommt beim Emit die Position (`Where`) als Literal mitgegeben und meldet `in main.main (t_line.lyr:7)`; Index- und Unwrap-Panics bekommen nur den Funktionsnamen und melden `in main.main` ohne Zeile. Der Interpreter meldet in beiden Fällen die Zeile. Die dokumentierte „one-line backtrace"-Einschränkung erklärt die fehlenden Rahmen, nicht die fehlende Position — die ist beim Emit genauso verfügbar wie bei der Division. Korrekt: `Where(op)` auch an Element/SetElement/Unwrap übergeben.
- Repro (verifiziert: ja): scratchpad/vm/t_line.lyr: `lyrvm run t_line.lyrbc -- idx` → `in main.main (t_line.lyr:22)`; `LYRIC_JIT=1 …` → `in main.main`. Mit `-- div` beide `(t_line.lyr:7)`.
- Betroffene Teammitglieder informiert: diagnostics

### [vm-bytecode] Lader-Lücken (Codelesung, Spec §6 verlangt Ablehnung): const-Breite, verschachtelte Optionals im Codestrom, unvalidierte Inline-Typen, Flag-Werte >1, Global-Init auf Import
- Severity: LOW
- Datei: src/Lyric.Core/Bytecode/CodeDecoder.cs:301-317 (DecodeConst), :246-289 (SkipTypeBody), :210-225 (DecodeChainOp); src/Lyric.Core/Bytecode/BytecodeReader.cs:904-911 (Const nur für String geprüft), src/Lyric.Vm/LoadedProgram.cs:117-118, src/Lyric.Vm/Interpreter.cs:643,708 (`Immediate == 1`)
- Ebene(n): vm, emit(reader), spec
- Beschreibung: (a) Spec §5 „The value must fit the width of the tag": `const i8 300` wird angenommen und stillschweigend normalisiert; `const` mit Composite-Tag (0x40…) wird als uleb gelesen und akzeptiert. (b) Spec §Optionals „A reader must reject an inner type tagged 0x42": ReadType prüft das für Sektionen, SkipTypeBody im Codestrom (optnone/optsome/newarr/mkcoro-Typen) nicht; ebenso werden Typindizes in diesen Inline-Typen nicht gegen die Types-Tabelle geprüft (nur Sektionstypen via ValidateTypeReferences). Folgenlos für den Interpreter (er liest die Typen nicht), aber `disasm` rendert den Index. (c) `resume lenient` / `yield hasvalue` werden nicht auf 0/1 begrenzt: StackEffect rechnet `(int)Immediate` Pops für yield, der Interpreter poppt nur bei `== 1` — bei Wert 2 drifted der Stack des Frames (handgebaut). (d) `const char` mit ungültigem Codepoint: Interpreter paniziert LYR-VM0012 beim Laden der Konstante (Normalize), der JIT emittiert das Bit-Muster roh (JitCompiler.cs:967-969) — Divergenz, nur handgebaut erreichbar; der Reader prüft Char-Konstanten in Attributzeilen (BytecodeReader.cs:309-318), im Codestrom nicht. (e) Globals-Init auf einen IMPORT-Index passiert ValidateGlobals, `LoadedProgram.Load` überspringt ihn still (`init >= Imports.Count`), die Globals bleiben `default` (Klassenreferenzen null → siehe HIGH-Eintrag Null-Deref). Spec sagt „index+1 into the shared call index space", also entweder rufen oder ablehnen. Alles nur mit handgebauten Modulen erreichbar; Mutations-Fuzzing (1600 Mutanten über 4 Module, verify+run) fand außer den Null-Deref-Crashes keine Reader-Exception und keinen Hang im Reader.
- Repro (verifiziert: nein — Codelesung; Fuzzing verifiziert: kein Reader-Crash)
- Betroffene Teammitglieder informiert: cross-layer-reviewer (Überschneidung mit dessen Sammelfund), spec-conformance

### [optimizer] JIT: Panic-Backtrace aus kompiliertem Code verliert die Interpreter-Aufruferframes (Mischbetrieb)
- Severity: LOW
- Datei: src/Lyric.Vm/Jit/JitRuntime.cs:117-120 (`Panic(...).WithCallStack([where])`), src/Lyric.Vm/Interpreter.cs:229-236 (`catch (LyricPanic) when (panic.CallStack.Count == 0)`), src/Lyric.Vm/Interpreter.cs:432-445 (Aufruf kompilierten Codes aus einem Interpreter-Frame)
- Ebene(n): vm | diagnostics
- Beschreibung: Eine Division-durch-Null-Panic in einer kompilierten Funktion, die aus einem INTERPRETIERTEN Frame aufgerufen wird, bekommt vom JIT einen einzeiligen Callstack (`[where]`). Der Interpreter hängt seine Frames nur an, wenn der Callstack leer ist — also gar nicht. Ergebnis: `in main.divide` statt `in main.divide / in main.outer / in main.main`. Umgekehrt setzen die anderen JIT-Panics (JitRuntime.Element/Unwrap, Index/Null) KEINEN Callstack, dann listet der Interpreter nur seine eigenen Frames und die kompilierte Funktion fehlt in der Liste (steht nur im Meldungstext). Die einzeilige Backtrace für den reinen Compiled-Fall ist dokumentiert (Interpreter.cs:188-197); der Mischfall ist es nicht und liefert je nach Panic-Art zwei verschiedene Verkürzungen. Korrekt wäre, im Interpreter-Catch die eigenen Frames an einen vorhandenen JIT-Callstack ANZUHÄNGEN statt ihn zu übernehmen.
- Repro: tests/OptDiff/cases/t_jitbt.lyr (mein Worktree), unoptimiert; Interpreter: `in main.divide / in main.outer / in main.main`; mit LYRIC_JIT=1: nur `in main.divide`. (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode, diagnostics

### [semantic] Narrowing wird nach einem inneren Block wiederhergestellt, obwohl der Block die Variable auf null gesetzt hat
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:946-953 (CheckBlock: `_narrowed = savedNarrowed` stellt den Zustand VOR dem Block wieder her — auch Entfernungen durch CheckAssign :2398 werden damit rückgängig gemacht), :1235-1267 (CheckIf analog mit Snapshot)
- Ebene(n): sema, spec
- Beschreibung: `var o: ?int = 5; if (o != null) { if (c) { o = null; } println(f"{o + 1}"); }` — die Zuweisung im inneren `if` entfernt das Narrowing nur bis zum Ende des inneren Blocks; danach ist `o` für die Sema wieder `int`, `o + 1` typisiert, und zur Laufzeit `panic [LYR-VM0007]: force-unwrapped a '?T' that had no value`. §7.4: „An assignment to the variable ends its narrowing from that point on“ — das Programm müsste SEM0003/SEM0001 bekommen. Korrekt wäre: am Blockende nur die im Block HINZUGEKOMMENEN Fakten verwerfen (Early-Exit-Narrowings), Entfernungen beibehalten; bei if/else beide Zweige vereinigen (nur Fakten behalten, die in beiden überleben). Gleiches Muster in Schleifenkörpern (`while (o != null) { if (c) { o = null; } … o + 1 }`).
- Repro: scratchpad/sem/t44_narrow_restore.lyr (verifiziert: ja, exit 101)
- Betroffene Teammitglieder informiert: ir-codegen (Lowering ist mit checked unwrap korrekt), spec-conformance

### [semantic] `break`/`continue` außerhalb einer Schleife wird von keiner Frontend-Stufe geprüft → InternalCompilationException
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1014 („break, continue and error need no check“); SemaRules.cs (kein Loop-Kontext)
- Ebene(n): sema, parser
- Beschreibung: `fn main(): int { if (true) { break; } return 0; }` (ebenso `continue;`) passiert Parser und Sema ohne Diagnose und endet als `Unhandled exception. InternalCompilationException: lowering: 'break' outside a loop` (exit 82). Braucht einen Loop-Tiefe-Zähler in der Sema (auch: `break` in einem Lambda innerhalb einer Schleife darf die äußere Schleife nicht verlassen; `break` in `defer` innerhalb einer Schleife?).
- Repro: scratchpad/sem/t45b_break_outside.lyr, t45c_continue_outside.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: lexer-parser, ir-codegen

### [semantic] `return` in einem `defer`-Body wird akzeptiert → Stack Overflow des Compilers
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1008 (`case DeferStmt de: CheckStmt(de.Body, scope)` — keine Regel gegen return/break/continue/throw im Defer-Body); Lowering: EmitDefers (siehe [ir-codegen]-Eintrag zu `defer { throw … }`)
- Ebene(n): sema, ir, spec
- Beschreibung: `fn g(): int { defer { return 7; } return 1; }` → `lyric check` stirbt mit „Stack overflow.“ (exit 141; vermutlich return-im-defer emittiert die Defers erneut, rekursiv). §7.5 definiert nicht, was ein `return`/`break` IM Defer-Body bedeutet (Go verbietet es implizit, da defer nur Aufrufe nimmt). Sema sollte return/break/continue in Defer-Bodies refusieren (oder die Spec definiert die Semantik). Schwesterfall `defer { throw E{}; }` → „block already sealed“ (ir-codegen/vm-bytecode).
- Repro: scratchpad/sem/t45a_defer_return.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: ir-codegen, spec-conformance

### [ir-codegen] `return` im defer-Body → unendliche Rekursion im Lowering (Stack overflow, exit 141) — Ergänzung zum [semantic]-Fund
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:972-984 (LowerReturn → EmitAllPendingDefers), :849-858 (EmitAllPendingDefers iteriert über eine Kopie der Scope-Liste, in der der gerade emittierte defer noch enthalten ist), :841-844 (EmitDefers → LowerStmt(defer.Body))
- Ebene(n): ir | sema
- Beschreibung: Ein `return` in einem defer-Body ruft EmitAllPendingDefers; die pending-Listen enthalten noch denselben defer (er wird erst nach EmitDefers nicht entfernt, sondern nie), also wird sein Body erneut gelowert, dessen `return` wieder alle defers emittiert usw. — Endlosrekursion ohne Abbruch. Gleicher Mechanismus für `break`/`continue` im defer-Body innerhalb einer Schleife (EmitPendingDefersAbove). Mitigation im Lowering: den gerade emittierten defer während seines Bodys aus der pending-Liste nehmen (bzw. nur die Einträge VOR ihm emittieren); die eigentliche Regel (return/break/continue in defer-Bodies) gehört in Sema/Spec §7.5.
- Repro (verifiziert von semantic: ja): `fn g(): int { defer { return 7; } return 1; }` → Stack overflow.
- Betroffene Teammitglieder informiert: semantic (Meldender), spec-conformance (§7.5-Regel)

### [cli-security] `--json` wird von lyrfmt (Parse-Fehler) und lyrbuild (alle Diagnosen) ignoriert — Text auf stderr trotz dokumentiertem JSON
- Severity: MEDIUM
- Datei: src/Lyrfmt/Program.cs:260, src/Lyrfmt/Program.cs:310 (`diagnostics.RenderText(Console.Error)` statt `terminal.Render`), src/Lyrbuild/Program.cs:128-131, src/Lyrbuild/Program.cs:201-203 (kein ToolOptions.Parse, `--json`/`--quiet` werden als unbekannte Argumente still verschluckt)
- Ebene(n): cli, diagnostics
- Beschreibung: `lyrfmt --help` verspricht „--json Diagnostics as JSON on stderr", die Option wird via ToolOptions geparst, aber die einzigen Diagnosen des Formatters (Parse-Fehler in Datei oder `--stdin`) gehen über `RenderText`. lyrbuild kennt weder `--json` noch `--quiet`, obwohl `lyric --help` verspricht, jede Option an das Tool durchzureichen; `lyric build <dir> --json` liefert Textdiagnosen. Korrekt: `terminal.Render(diagnostics)` in lyrfmt; ToolOptions.Parse + TerminalOutput in lyrbuild (oder `--json` dort als Usage-Fehler ablehnen).
- Repro (verifiziert: ja): `printf 'fn main(): int { return 1;\n' > b.lyr; lyrfmt --check b.lyr --json` → `b.lyr:2:1: error[LYR-PAR0018]: expected '}' to close block` als Text. `printf 'fn build(): void { let x: int = "s"; }\n' > p/build.lyr; lyric build p --json` → Textdiagnose.
- Betroffene Teammitglieder informiert: diagnostics (Info)

### [cli-security] `lyric test` findet lyrtest im Dev-Build nicht: Lyric.Cli.csproj kopiert alle Tools außer Lyrtest neben lyric
- Severity: LOW
- Datei: src/Lyric.Cli/Lyric.Cli.csproj:40-45 (ProjectReferences für Lyrc/Lyrvm/Lyrrepl/Lyrbuild/Lyrpack/Lyrfmt, Lyrtest fehlt), src/Lyric.Cli/Tool.cs:497
- Ebene(n): cli
- Beschreibung: Der Driver kennt `Tool.Test` und README/`lyric --help` dokumentieren `lyric test`, aber nach `dotnet build` liegt kein `lyrtest` neben `lyric` (nur im Publish über build/publish.proj:77). `dotnet run --project src/Lyric.Cli -- test` (die im README empfohlene Aufrufform) endet mit LYR-CLI0005. Die Tests umgehen es über `Toolchain.LyrtestPath`.
- Repro (verifiziert: ja): `dotnet build && src/Lyric.Cli/bin/Debug/net10.0/lyric test` → `error[LYR-CLI0005]: lyrtest not found: …/src/Lyric.Cli/bin/Debug/net10.0/lyrtest (set LYRIC_TEST or pass --tester)`, exit 1.
- Betroffene Teammitglieder informiert: -

### [cli-security] Driver und Tools verschlucken unbekannte Optionen und überzählige Positionsargumente still (lyric run/pack/new, lyrc, lyrvm, lyrpack, lyrbuild, lyrrepl)
- Severity: LOW
- Datei: src/Lyric.Cli/Program.cs:247 (`passThrough`), src/Lyric.Cli/Program.cs:320-323, src/Lyric.Cli/NewProject.cs:639, src/Lyrvm/Program.cs:154, src/Lyrpack/Program.cs:357, src/Lyrbuild/Program.cs:56,227, src/Lyrrepl/Program.cs:99
- Ebene(n): cli
- Beschreibung: Es gibt keinen Pfad, der LYR-CLI0003 für eine unbekannte OPTION ausgibt (nur für unbekannte Kommandos). `lyric run app.lyr --no-opt -O0 --bogus`, `lyric pack app.lyr --grant none`, `lyric new okapp extra --bogus`, `lyrbuild dir extra`, `lyrvm run m.lyrbc --bogus`, `lyrrepl --bogus`, `lyrpack m.lyrbc --bogus` laufen alle mit Exit 0. Ebenso bleibt ein fehlender Optionswert stumm (`lyrc build a.lyr -o`, `lyrbuild --stdlib`, `lyrrepl --stdlib`), und `--progress=never` (Gleichheitsform) ist ein stiller Pass-Through statt ein Fehler. Nur lyrtest und lyrls lehnen Unbekanntes ab. Ein Tippfehler in einer Policy-Option (`--deny-warning`) wird so zu „Policy nicht angewandt, Exit 0" (diagnostics hat den lyrc-Teil separat eingetragen). Korrekt: pro Tool eine Liste bekannter Optionen; der Driver sollte Optionen, die weder Compiler noch Runtime kennt, ablehnen statt weiterreichen.
- Repro (verifiziert: ja): `lyric run examples/hello.lyr --bogus --no-opt` → `Hello, Lyric!`, exit 0.
- Betroffene Teammitglieder informiert: -

### [cli-security] REPL-Ausdruckswrapper `console.println(f"{<input>}")` ist per Eingabe verlassbar; Stack Overflow beendet die Session
- Severity: LOW
- Datei: src/Lyrrepl/Session.cs:238, src/Lyrrepl/Session.cs:292-325
- Ebene(n): cli
- Beschreibung: Eine Eingabe wie `1 }"); console.println("injected` schließt das f-String-Loch und den Aufruf und hängt eigenes Statement an — kosmetisch (eigener Prozess, eigene Eingabe), aber die Klassifikation „Ausdruck vs. Statement" durch Textchirurgie ist unrobust; ein Parse des Eintrags als Ausdruck wäre sauberer. Außerdem endet ein tief verschachtelter Eintrag (`(`×50000) die ganze Session mit Stack overflow (Exit 134, siehe HIGH-Eintrag zu Stack Overflow), obwohl `Attempt` sonst jeden Fehler auf den Eintrag begrenzt.
- Repro (verifiziert: ja): `printf '1 }"); console.println("injected\n' | lyrrepl` → Ausgabe `1` und `injected}`.
- Betroffene Teammitglieder informiert: -

### [ir-codegen] Nachtrag zu "Spec §13 Closures: +1" — Severity-Korrektur auf HIGH
- Severity: HIGH (hochgestuft nach Rückmeldung von spec-conformance: Kapitel 13 ist das kanonische Format, docs/Bytecode.md byte-identisch; die Implementierung weicht vom normativen Format ab, eine Zweitimplementierung bindet jedes Closure-Ziel versetzt)
- Datei: src/Lyric.Frontend/Emit/BytecodeWriter.cs:605-606, src/Lyric.Core/Bytecode/BytecodeReader.cs:965-971, src/Lyric.Core/Bytecode/CodeDecoder.cs:341, Disassembler; Spec 13-bytecode.md:936-937
- Ebene(n): spec | emit | vm
- Beschreibung: siehe ursprünglichen Eintrag. Entscheidung nötig: Spec-Satz streichen (Kodierung bleibt) oder Format-Bump mit +1. Zum defer+throw-Fund (Nr. 5) bleibt HIGH: §7.5 "genau ein Lauf pro Ausführung" und "on every exit path" sind beide verletzt, unabhängig von der Spec-Lücke zum werfenden defer.
- Repro: Codelesung (verifiziert: ja)
- Betroffene Teammitglieder informiert: spec-conformance, vm-bytecode

### [semantic] Statische und Instanz-Methode gleichen Namens: Overload-Auswahl kommt zu spät, SEM0055 vorher
- Severity: LOW
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:3494-3500 (MemberOfType: `LookupLocal` liefert den ERSTEN Überlader, `IsStatic: false` → SEM0055 noch vor SelectOverload), analog InstanceMember :3306
- Ebene(n): sema, diagnostics
- Beschreibung: `struct S { fn f(x: int): string {…}  static fn f(x: string): string {…} }` wird von CheckOverloadSets akzeptiert (verschiedene Parameter), aber `S.f("a")` meldet „'f' is an instance method and needs a receiver“ — der Typ-Pfad sieht nur das erste Mitglied des Sets. Spec §4.3a erlaubt Overloads für statische Methoden; gemischte Sets sollten entweder an der Deklaration abgelehnt oder an der Aufrufstelle korrekt aufgelöst werden.
- Repro: scratchpad/sem/t61_static_overload.lyr (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [spec-conformance] Unbenutzter Namespace-Import (`import a.b;`) warnt nicht (SEM0072 fehlt)
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/WarningAnalyzer.cs:362-364, 375-387 (nur ImportSelective und ImportAlias werden geprüft; „a bare import a.b; is left alone“)
- Ebene(n): sema
- Beschreibung: §4.2: „Importing a module — under ANY form — … An import counts as used when one of its names is referenced OR one of its extension methods resolves in the file; otherwise it warns (LYR-SEM0072).“ `import std.math;` / `import std.iter;` ohne jede Verwendung erzeugen keine Warnung; `import std.option as o;` und `import std.math { abs };` warnen korrekt.
- Repro (verifiziert: ja): `import std.math; import std.iter; fn main(): int { return 0; }` → keine Diagnose. Soll: 2× SEM0072.
- Betroffene Teammitglieder informiert: semantic

### [spec-conformance] Struct-Selbstrekursion über Tuple, Optional und generische Struct-Instanz wird nicht als SEM0056 erkannt
- Severity: MEDIUM
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:5046-5058 (FindStructCycle betrachtet nur `NamedType`, das direkt an ein Struct bindet; `?S`, `(S, int)`, `Box<S>` werden übersprungen)
- Ebene(n): sema | spec
- Beschreibung: Anhang A SEM0056 „A struct containing itself — infinite size“; §13 Types/kind struct: „A struct must not contain itself … Recursion through a class, an array or an interface is permitted“ — Optional, Tuple und generische Struct-Instanzen stehen NICHT in der erlaubten Liste. `struct S { n: ?S }`, `struct S { t: (S, int) }` und `struct Box<T> { v: T } struct S { b: Box<S> }` kompilieren ohne Diagnose. Zur Laufzeit verhält sich `?S` als Referenz (`var b = a; b.n!.v = 9;` ändert `a.n!.v`, Ausgabe `9 9`) — bricht §3.4 Wertsemantik; die Spec legt nirgends fest, dass `?Struct` geboxt wird (Spec-Lücke; siehe auch [cross-layer-reviewer]-Eintrag).
- Repro (verifiziert: ja): `struct S { t: (S, int) } fn main(): int { return 0; }` → exit 0. Soll: SEM0056.
- Betroffene Teammitglieder informiert: semantic, cross-layer-reviewer

### [spec-conformance] Capability-Tabelle: Compiler gated `std.io.stream` (fileAccess), Spec §4.5 und §13 nennen das Modul nicht; §4.5 und §13 widersprechen sich
- Severity: LOW (Spec-Lücke / Doku)
- Datei: src/Lyric.Core/Capabilities.cs:44-64 (Gated-Tabelle: std.io.file, std.io.stream, std.io.net, std.os, std.time, std.task, std.dotnet, std.process); ~/dev/projects/lyricspec/spec/04-modules.md §4.5 Tabelle; spec/13-bytecode.md:218-224
- Ebene(n): spec | vm
- Beschreibung: §4.5 (Kap. 4) listet Bit 0 = `std.io.file`, Bit 2 = `std.os`, `std.time`, `std.task`, Bit 3 = `std.dotnet`. §13 (kanonisch, gespiegelt) listet Bit 2 nur `std.os` und Bit 3 „reserved“. Der Compiler gated zusätzlich `std.io.stream` unter fileAccess — in keiner der beiden Tabellen. Da „the bits and their values are part of the bytecode contract“, muss eine zweite Implementierung wissen, welche Module gated sind.
- Repro: Quellvergleich (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode

### [spec-conformance] `static let` auf Modulebene meldet PAR0025 statt PAR0040
- Severity: LOW
- Datei: src/Lyric.Frontend/Parsing/Parser.Declarations.cs (Top-Level-Dispatch kennt `static` nicht; PAR0040 wird nur bei :644 innerhalb eines Typkörpers erreicht)
- Ebene(n): parser | diagnostics
- Beschreibung: Anhang A PAR0040 „`static let` outside a struct or class body“. `static let X: int = 1;` auf Modulebene → `PAR0025: expected a declaration, got Static`.
- Repro (verifiziert: ja): `static let X: int = 1; fn main(): int { return 0; }`
- Betroffene Teammitglieder informiert: lexer-parser, diagnostics

### [spec-conformance] Spec-Lücken (gesammelt), keine Compiler-Bugs
- Severity: LOW
- Datei: ~/dev/projects/lyricspec/spec/*.md
- Ebene(n): spec
- Beschreibung:
  1. §10: „resume on an exhausted coroutine is a panic“ ohne Code; VM meldet LYR-VM0011 (Anhang A: „panic(msg) from the program“) — der Conformance-Fall `resume_exhausted_panics.lyr` pinnt deshalb nur `LYR-VM`. Ein eigener Code (oder VM0011 explizit) fehlt.
  2. §11 fixiert `fromFloat` (binary64); für `float32` ist die Interpolationsform nicht fixiert. Der Compiler rendert `0.1f32` als `0.10000000149011612` (Shortest-Round-Trip des erweiterten double, nicht des float32).
  3. Anhang A RES0001 nennt „module, type body or extend block“ — eine lokale Redeklaration im SELBEN Block (`let x = 1; let x = 2;`) wird stillschweigend als Shadowing akzeptiert; die Spec sagt dazu nichts.
  4. §3.3/§2 §4: `??T` wird von der Grammatik abgelehnt, `?(?T)` (GroupedType) aber abgeleitet; der Compiler lehnt es mit IR0001 ab.
  5. §7.6/A.5: IR0001-Grenzen des Compilers, die die Spec nicht nennt: Literal-Subpattern in Variant-Payload (`E.P(1, y)`), `g.next` als Wert, `s.v++`, Marker-Interface-Wert, Struct/Optional in f-string (Codes fehlen).
  6. §8.3/§7.1a: ob `T` bei `fn f<T>(params xs: T[])` aus Elementargumenten gebunden wird, ist nicht festgelegt; Compiler: SEM0060 (plus drei irreführende SEM0001 `int` → `T`).
  7. §9.2/§13 §8.5: `fn main(args: string[]): int` ist nur in Kap. 13 (Runner) beschrieben, in Kap. 9/Anhang SEM0021 nicht.
  8. §6.1: `++`/`--` „on integer variables“ — Feld/Element-Ziele und `5++` sind weder erlaubt noch mit Code abgelehnt.
  9. §7.5/§9.3: Verhalten eines defer-Bodys, der selbst wirft, ist nicht festgelegt (siehe [ir-codegen]).
  10. Anhang A RES0002 vs SEM0011 und SEM0015 vs SEM0031 überlappen in der Beschreibung (siehe [diagnostics]).
- Betroffene Teammitglieder informiert: -

### [spec-conformance] Conformance-Suite: Ergebnis und Lücken
- Severity: LOW (Info)
- Datei: ~/dev/projects/lyricspec/conformance/cases (159 Fälle)
- Ebene(n): spec
- Beschreibung: Ergebnis gegen diesen Worktree (lyrc/lyrvm 4.4.1): `--toolchain-version 4.4.1` → 158/158 passed, 1 skipped (until 3.0.0). stdlib-tests (`lyric test`): 166/166. Lücken (Spec-Regeln ohne Fall), die reale Abweichungen versteckt haben: §6.9 Arm-Unifikation ohne Kontext (int/float-Arme → 5e-324); §5.4 Auflösungsreihenfolge own→extension→default; §7.1 let-Immutabilität für Struct-Felder; §3.5 SEM0064 Alias-Zyklus; §4.2 SEM0072 für Namespace-Import; §1.7 f-string über Zeilenende in Interpolation; §5.5 SEM0047 Array/Tuple/Fn-Ziel; §13 Struct-Selbstrekursion über ?/Tuple/generisch; §9.1 Throwable „through an extend“; §7.2 Iterator-Konformität per extend; §6.5 Compound auf Feld (primitiv) und `??=`; §6.1 Shift auf schmalen Typen (int8/uint8 Maskierung), `%`-Vorzeichenregel, `-min`; §3.6 NaN/Inf-Sättigung für ALLE Zielbreiten; §4.5 Capability-Bits (nur VM-seitig testbar); §9.4 VM0004 Tiefe; §7.7 do-while/try/compound-Regeln; §4.7 SEM0065/0067/0068/0069/0081; §10a Regel 4 (Native-Barriere) und 5 (VM0014 hat einen Fall); §12.3 exit-Code-Maskierung (`return 300` → 44, `-1` → 255).
- Betroffene Teammitglieder informiert: -

### [cross-layer-reviewer] Global-Initializer liest ein späteres Klassen-Global über einen Funktionsaufruf → gültiges Programm stirbt mit unbehandelter .NET-Exception (Exit 134)
- Severity: HIGH
- Datei: src/Lyric.Frontend/Sema/TypeChecker.cs:1451-1461 (TypeOfGlobalReference: SEM0057 nur bei `_inGlobalInitializer`, d.h. nur für direkte Lesezugriffe im Initializer-Ausdruck; ein Aufruf einer Funktion, die das spätere Global liest, wird nicht erkannt); src/Lyric.Frontend/Ir/Lowering/GlobalInitializer.cs:21-23 ("Reading a later one yields the zero value" — für Klassen-/Array-Globals ist der Nullwert eine leere Referenz); src/Lyric.Vm/LyrValue.cs:130 (AsObject wirft InvalidOperationException statt LyricPanic); src/Lyric.Vm/VmHost.cs:42-49 (fängt nur LyricPanic/LyricRuntimeException)
- Ebene(n): sema | ir | vm | diagnostics
- Beschreibung: Sema garantiert "kein Global wird vor seiner Initialisierung gelesen" nur für den unmittelbaren Initializer-Ausdruck. Ruft der Initializer eine Funktion, die ein später deklariertes Global liest, gibt es keine Diagnose; zur Laufzeit ist das Klassen-Global noch `null` (LyrValue.Ref = null), `ldfld` ruft `AsObject` → `InvalidOperationException("null object reference")`, die weder in einen `LYR-VM`-Panic übersetzt noch von VmHost gefangen wird: der Prozess endet mit .NET-Stacktrace und Exit 134 statt Panic (101) mit Backtrace. Dasselbe gilt unter LYRIC_JIT=1. Zwei Korrekturen: (a) Sema: Aufrufe im Initializer konservativ als Lesezugriff auf ALLE später initialisierten Globals werten (oder SEM0057-Analyse über den Call-Graph), (b) VM: Null-Referenz an `ldfld`/`stfld`/`ldelem`/`callvirt`/… in einen LyricPanic (NullDereference, LYR-VM0004 o.ä.) übersetzen, damit ein Bytecode-Fehler nie zum Prozessabbruch wird (Spec §6: "may run without safety checks" setzt Validierung voraus, die es hier nicht gibt).
- Repro (verifiziert: ja, `lyric run` und `lyrvm run` mit LYRIC_JIT=1):
  ```
  import std.io.console { println };
  class C { v: int }
  let a = f();
  let c = C { v = 1 };
  fn f(): int { return c.v; }
  fn main(): int { println(f"{a} {c.v}"); return a; }
  ```
  → "Unhandled exception. System.InvalidOperationException: null object reference", Exit 134; `lyrc check` meldet nichts.
- Betroffene Teammitglieder informiert: semantic, vm-bytecode, diagnostics

### [cli-security] Nachtrag zu „Stack Overflow bei tief verschachtelten Eingaben": lyrls und lyrdbg sterben verifiziert; Schwellen niedriger als angenommen
- Severity: HIGH (Ergänzung, kein neuer Eintrag)
- Datei: src/Lyrls/Program.cs:452-456 (keine Isolation der Analyse), src/Lyric.Dap/DapServer.cs (Compile in-process beim `launch`), src/Lyric.Frontend/Parsing/Parser.cs, src/Lyric.Frontend/Sema/TypeChecker.cs
- Ebene(n): parser, sema, cli
- Beschreibung: Über das Protokoll verifiziert: `textDocument/didOpen` mit 4 000 verschachtelten `(` beendet lyrls nach der Debounce-Analyse mit „Stack overflow" (Exit -6/134, Editor verliert den Server); ein DAP-`launch` von deep-paren.lyr tötet lyrdbg identisch. Schwellen mit dem Standard-Stack: 4 000 `(` (2 000 gehen noch), `1+`×8 000 in Sema, `[`×4 000, und nur 200 verschachtelte leere Array-Literale `[[[…]]]` (Sema/Inferenz). Für Server-Prozesse wäre neben dem Tiefenlimit ein dedizierter Analyse-Thread mit großem Stack (wie DebugController.cs:101 ihn für das Programm anlegt) ein Sofortschutz.
- Repro (verifiziert: ja): scratchpad/proto3.py (didOpen mit 4000 Klammern, 20 s warten) → stderr `Stack overflow … Parser.ParsePrefix`, exit -6. `lyrc check` mit `'['*200+']'*200` als let-Initialisierer → Stack overflow.
- Betroffene Teammitglieder informiert: lexer-parser, semantic

### [embedding-parity] Marshal.FromLyric liefert für Objekt-/Array-/Optional-/Enum-Rückgaben stillschweigend 0 statt eines Fehlers
- Severity: HIGH
- Datei: src/Lyric.Embedding/Marshal.cs:98-108 (boxed-Switch, Default-Arm `_ => value.AsI64`)
- Ebene(n): embedding
- Beschreibung: `ScriptInstance.Call<T>` prüft den Tag des Rückgabetyps nur für Void/Host/String/Bool/Char/Float/Ints. Für `Ref`, `Array`, `Optional`, `Enum` (alles, was laut Doku "nicht über die Grenze kann") fällt der Switch auf `value.AsI64` zurück und gibt das Integer-Payload der Referenz (0) zurück. `Call<long>("retBox")` → 0, `Call<object>("retBox")` → boxed 0, `Call<long>("retArr")` → 0, `Call<long>("retColor")` (Enum) → 0, `Call<long?>("retOpt", false)` (abwesendes `?int`) → 0 statt null. Kein LYR-EMB0001/0003. Erwartet: ScriptException LYR-EMB0001 wie bei ToLyric (Marshal.cs:52-55), bzw. für `?T` eine Null-Abbildung.
- Repro: Modul mit `class Box{v:int=7} pub fn retBox(): Box { return Box{}; } pub fn retOpt(p: bool): ?int { return if (p) 5 else null; }`; Host: `inst.Call<long>("retBox")` → 0, `inst.Call<long?>("retOpt", false)` → 0. (verifiziert: ja, Sonde scratchpad/host/Probe.cs)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] Marshal.FromLyric konvertiert per Convert.ChangeType verlustbehaftet (float→int rundet, int→string/bool)
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/Marshal.cs:110-122
- Ebene(n): embedding
- Beschreibung: Der Fallback `Convert.ChangeType(boxed, wanted)` ist deutlich toleranter als die Hinrichtung (ToLyric lehnt double→int mit LYR-EMB0005 ab, Marshal.cs:158-160). Rückrichtung: `Call<long>("retFloat")` mit 3.7 → 4 (gerundet, kein Fehler), `Call<string>("retInt")` → "42", `Call<bool>("retInt")` → True, `Call<long>("retBool")` → 1, `Call<string>("retChar")` → "233". Widerspricht dem Klassenkommentar "Every conversion checks the range of the target type and throws rather than truncating" (Marshal.cs:13-14).
- Repro: `pub fn retFloat(): float { return 3.7; }` → `inst.Call<long>("retFloat")` == 4. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] ToLyric für float32 verengt double stillschweigend (1e300 → Infinity); ulong für float abgelehnt
- Severity: LOW
- Datei: src/Lyric.Embedding/Marshal.cs:45, src/Lyric.Embedding/Marshal.cs:143-150
- Ebene(n): embedding
- Beschreibung: `TypeTag.F32 => LyrValue.FromF32((float)ToDouble(...))` – ein double außerhalb des float-Bereichs wird zu ±Infinity, 0.1 zu 0.100000001, während Ganzzahlen mit LYR-EMB0004 geprüft werden (`300` als `int8` abgelehnt). Inkonsistent mit "throws rather than truncating". Außerdem fehlt `ulong` in ToDouble (Meldung "expected a number, got UInt64"), `uint` wird akzeptiert.
- Repro: `pub fn takeF32(x: float32): float { return x as float; }` → `inst.Call<double>("takeF32", 1e300)` == Infinity. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] Host-Objekt wird ohne Typprüfung als beliebiger Host-Typ akzeptiert; spätere Fehlermeldung nennt den falschen Typ
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/Marshal.cs:22-27 (ToLyric, Tag Host), src/Lyric.Embedding/Marshal.cs:81-87 (FromLyric-Meldung)
- Ebene(n): embedding
- Beschreibung: `ToLyric` prüft für `TypeTag.Host` nur auf `null` und ruft `LyrValue.FromHostObject(value)` mit jedem Objekt auf – der registrierte .NET-Typ (`_hostTypes`) wird nie gegen `expected.HostName` geprüft. `inst.Call("playerName", new Enemy())` und `inst.Call("playerName", "ein string")` werden angenommen; die VM hält einen `Player`-Wert, dessen Ref kein Player ist. Der Fehler kommt erst beim Aufruf einer Host-Methode im Skript, mit der Meldung "the value is host type 'Player', which is not a 'Player'" (beide Namen aus der Deklaration; der tatsächliche .NET-Typ fehlt).
- Repro: RegisterType<Player>/<Enemy>; Skript `pub fn playerName(p: Player): string { return p.name(); }`; Host `inst.Call<string>("playerName", new Enemy())`. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] Warnungen einer erfolgreichen Kompilation gehen im Embedding verloren (lyrc zeigt sie)
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/LangVm.cs:334-349 (Build), src/Lyric.Embedding/ScriptModule.cs (keine Diagnostics-Eigenschaft)
- Ebene(n): embedding | diagnostics
- Beschreibung: `Build` wirft nur bei `!result.Ok`; bei Erfolg werden `result.Diagnostics` (Warnungen wie LYR-SEM0071) verworfen. `lyrc build` rendert sie auf stderr, `--deny-warnings` macht daraus einen Fehler; ein Host hat keinen Zugriff. Differential-Test: 7 von 196 Programmen (strings.lyr, iter_tests.lyr, math_tests.lyr, string_tests.lyr, warn_ok.lyr, bom_crlf.lyr, coro_throw.lyr) zeigen mit lyrc Warnungen, im Embedding nichts.
- Repro: `fn main(): int { let unused = 5; return 0; }` – lyrc: `warning[LYR-SEM0071]`; `vm.CompileFile(...)` → keine Ausgabe, kein API-Zugriff. (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [embedding-parity] EmbeddingException.Diagnostics ist nicht in CLI-Reihenfolge (unsortiert), entgegen der Doku
- Severity: LOW
- Datei: src/Lyric.Embedding/LangVm.cs:343-346, src/Lyric.Embedding/EmbeddingException.cs:24-25 vs src/Lyric.Core/DiagnosticEngine.cs:71-84 (SortedSnapshot)
- Ebene(n): embedding | diagnostics
- Beschreibung: Der CLI-Renderer sortiert (`SortedSnapshot`: Datei, Position, Code); `Build` übernimmt `result.Diagnostics.Diagnostics` in Meldereihenfolge. Die Doku von `EmbeddingException.Diagnostics` verspricht "in the same order as on the command line". Bei tests/Lyric.Tests.Parsing/golden/type_function.lyr und yield_resume.lyr weicht die Reihenfolge ab (LYR-SEM0051 steht im Embedding zuletzt, bei lyrc nach Position an zweiter Stelle).
- Repro: `vm.CompileFile("tests/Lyric.Tests.Parsing/golden/type_function.lyr")`, Diagnostics ausgeben, mit `lyrc check` vergleichen. (verifiziert: ja)
- Betroffene Teammitglieder informiert: diagnostics

### [embedding-parity] CompileFile leitet den Modulnamen aus dem Dateinamen ab und überstimmt den `module`-Header – andere Funktionsnamen/Backtraces als lyrc
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/LangVm.cs:251-257 (CompileFile), src/Lyric.Frontend/Resolver/Compilation.cs:105-107 (Name vor Header)
- Ebene(n): embedding
- Beschreibung: lyrc lässt den Namen dem Header bzw. dem Default `main` (ScriptSource.FromDisk ohne Namen). `CompileFile` setzt `Path.GetFileNameWithoutExtension(path)`; `AddModule` nimmt ihn als Autorität, der Header wird stillschweigend überstimmt (kein LYR-RES0006). Folgen: (a) `hdr.lyr` mit `module foo.bar;` → `hdr.main` statt `foo.bar.main` (Backtrace, Disassembly, Call-Präfix); (b) `fibonacci.lyr` ohne Header → `fibonacci.main` statt `main.main`; (c) `my-script.v2.lyr` → Modulpfad `["my-script","v2"]` (Split an '.', Bindestrich im Bezeichner). Bytecode aus Embedding und lyrc für dieselbe Datei ist damit nicht identisch.
- Repro: scratchpad/cases/hdr.lyr: lyrvm-Backtrace `in foo.bar.main (hdr.lyr:5)`, Embedding `in hdr.main (hdr.lyr:5)`. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] CompileFile ignoriert lyric.json (sourceRoot/nativeRoots), lyrc wertet sie aus
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/LangVm.cs:334-341 vs src/Lyrc/Program.cs:182-201 (ProjectFile.Discover), src/Lyric.Embedding/HostOptions.cs:36-45
- Ebene(n): embedding | cli
- Beschreibung: lyrc sucht `lyric.json` aufwärts und setzt SourceRoot (Default: Verzeichnis der lyric.json) und NativeRoots. Das Embedding nutzt nur HostOptions; HostOptions.SourceRoot behauptet "null means the directory of the entry file, as everywhere else", was für lyrc nicht gilt. Ein Projekt, das `lyric run app/main.lyr` übersetzt, scheitert mit `vm.RunScript("app/main.lyr")`.
- Repro: proj/lyric.json `{"sourceRoot":"src"}`, proj/app/main.lyr `import util {twice}; fn main(): int { return twice(21); }`, proj/src/util.lyr. lyrc+lyrvm: Exit 42; Embedding: `proj/app/main.lyr:1:1: error[LYR-RES0003]: cannot find module 'util'`. (verifiziert: ja)
- Betroffene Teammitglieder informiert: cli-security (Kontext)

### [embedding-parity] LangVm.Run/RunScript ignorieren HostOptions.Compile (JIT) und bieten kein Budget – nur Instantiate honoriert beides
- Severity: LOW
- Datei: src/Lyric.Embedding/LangVm.cs:271-273 (Run → Interpreter.Run ohne jit) vs LangVm.cs:307-308; src/Lyric.Vm/Interpreter.cs:147-149
- Ebene(n): embedding | vm
- Beschreibung: `Interpreter.Run` ruft `LoadedProgram.Load(..., jit: false)`. HostOptions.Compile ("Whether scripts of this VM may run COMPILED") gilt nicht für `Run`. `Run` hat auch keine Budget-Überladung, obwohl `LoadedProgram.RunEntry(args, budget)` existiert – `main` eines fremden Skripts ist über die API nicht begrenzbar.
- Repro: Codepfad. (verifiziert: Codelesung; Sonde: Instantiate mit Compile=true meldet CompiledFunctions>0, Run hat keinen Pfad zum JitContext)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] Keine API, um fremde .lyrbc-Bytes zu laden – Doku verspricht es
- Severity: LOW
- Datei: src/Lyric.Embedding/ScriptModule.cs:16 (internal ctor), src/Lyric.Embedding/LangVm.cs:262-265, docs/guide/14-embedding.md ("For foreign bytes — mods, downloaded scripts — the module row is how a host decides whether to load at all")
- Ebene(n): embedding | spec
- Beschreibung: Es gibt kein `LangVm.Load(byte[])`/`ScriptModule.FromBytes`; ein Host kann von lyrc erzeugte Module oder gespeicherte `ScriptModule.Bytes` nicht über die Embedding-API laden (nur über `Lyric.Vm.VmHost.Load` + LoadedProgram, unter Umgehung der HostOptions). Der Loader selbst ist derselbe (BytecodeReader.ReadOrThrow, LangVm.cs:349 vs VmHost.cs:75), Format-Parität ist gegeben; die API-Seite fehlt.
- Repro: Codelesung. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] LangVm.Run liefert den Rohwert von main (256, -1) statt des Exit-Codes (&0xFF) wie lyrvm
- Severity: LOW
- Datei: src/Lyric.Embedding/LangVm.cs:271-273 vs src/Lyric.Vm/VmHost.cs:39-40
- Ebene(n): embedding
- Beschreibung: Doku "Runs the module's main and returns its exit code"; lyrvm liefert `& 0xFF` (Runner-Vertrag). `return 256;` → lyrvm Exit 0, `vm.Run` 256; `return -1;` → 255 vs -1.
- Repro: scratchpad/cases/exit256.lyr, exitneg.lyr. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

(Anmerkung [cross-layer-reviewer]: der Eintrag "Global-Initializer liest ein späteres Klassen-Global über einen Funktionsaufruf" beschreibt dieselbe Wurzel wie [vm-bytecode] "Null-Klassenreferenz (Global-Vorwärtsbezug über Funktionsaufruf)" und [semantic] "SEM0057 greift nur bei direktem Lesen" — bitte als EIN Fund zählen.)

### [embedding-parity] Reentranz Skript→Hostfunktion→Skript umgeht MaxCallDepth: CLR-StackOverflow (Prozessabbruch) statt LYR-VM0004
- Severity: HIGH
- Datei: src/Lyric.Vm/Interpreter.cs:137 (MaxCallDepth), src/Lyric.Vm/Interpreter.cs:423-425 (Prüfung `frames.Count` pro Execute), src/Lyric.Embedding/ScriptInstance.cs:226-236 (Invoke), src/Lyric.Embedding/HostFunction.cs:118-137 (Bridge/DynamicInvoke)
- Ebene(n): embedding | vm
- Beschreibung: Die Tiefenprüfung zählt nur die Frames des aktuellen `Interpreter.Execute`-Laufs. Ruft ein Skript eine Hostfunktion, die per `ScriptInstance.Call` zurück ins Skript ruft, entsteht pro Ebene ein neuer Execute-Lauf (Frame-Zähler beginnt bei 0) plus ~10 CLR-Frames (Loop/Execute/Invoke/DynamicInvoke). Bei ~950 Ebenen stirbt der Hostprozess mit "Stack overflow" (Exit 134) – nicht abfangbar, keine ScriptPanicException. Die Interpreter-Doku ("an overflow is a diagnostic rather than a process abort", Interpreter.cs:127-128) gilt damit nur ohne Host-Callbacks. Ein Budget hilft nicht (siehe nächster Eintrag). Im Standalone-lyrvm nicht erreichbar (keine Stdlib-Native ruft zurück nach Lyric).
- Repro: `vm.RegisterFunction("back", (long n) => n <= 0 ? 0 : inst.Call<long>("ping", n-1) + 1);` Skript `import host {back}; pub fn ping(n: int): int { return back(n); }`; `inst.Call<long>("ping", 5000)` → Exit 134 (scratchpad/host/Probe.cs mit PROBE_DEEP=1). Tiefe 50 funktioniert. (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode, cross-layer-reviewer

### [embedding-parity] ExecutionBudget wird durch Host-Callbacks nicht weitergegeben – Doku suggeriert das Gegenteil
- Severity: MEDIUM
- Datei: src/Lyric.Embedding/ScriptInstance.cs:82-84 (Doku "a host function that calls back in draws from whichever budget its own call was given"), docs/guide/14-embedding.md (Abschnitt "Bounding what a script may spend"), src/Lyric.Vm/Interpreter.cs:415-421 (Native-Aufruf ohne Budget-Kontext)
- Ebene(n): embedding | vm
- Beschreibung: Es gibt keinen "aktuellen" Budget-Kontext; ein Host, der aus einer registrierten Funktion heraus `inst.Call(...)` ohne Budget aufruft, führt das Skript ungemessen aus, obwohl der äußere Aufruf ein Budget von 1000 Instruktionen hatte. Ein fremdes Skript, dessen SDK irgendeinen Callback anbietet (forEach, on-Event …), entkommt so dem Budget. Die Doku liest sich, als würde das Budget vererbt.
- Repro: äußerer Aufruf `i3.Call<long>("outer", new ExecutionBudget(1000))`, `outer` ruft Hostfunktion `escape`, die `i3.Call<long>("spin")` ohne Budget ausführt (100 Mio. Iterationen) → Ergebnis 100000000, keine ScriptBudgetException. (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [embedding-parity] Compile(text, moduleName) validiert den Modulnamen nicht (Leerzeichen, "std.string" verdeckt die Stdlib)
- Severity: LOW
- Datei: src/Lyric.Embedding/LangVm.cs:244-245, src/Lyric.Frontend/Compiler/ScriptSource.cs:76-81, src/Lyric.Frontend/Resolver/Compilation.cs:105-107
- Ebene(n): embedding
- Beschreibung: Nur `ThrowIfNullOrWhiteSpace`. `vm.Compile(src, "a b")` erzeugt ein Modul mit Funktion "a b.main"; `vm.Compile(src, "std.string")` registriert das Skript als `std.string`, die Wohlbekannt-Module überspringen dann das Laden der echten Stdlib und die Kompilation scheitert mit 9 unverständlichen Fehlern (concat/fromXxx fehlen). Ein Name, den der Parser nie als `module`-Header akzeptieren würde, sollte hier abgelehnt werden.
- Repro: Sonde scratchpad/host/Probe.cs ("Compile name 'std.string'", "Compile name 'a b'"). (verifiziert: ja)
- Betroffene Teammitglieder informiert: -

### [cli-security] `lyrfmt <dir>` folgt Verzeichnis-Symlinks: formatiert Dateien AUSSERHALB des Zielbaums in place, listet zyklische Links 40-fach
- Severity: LOW
- Datei: src/Lyrfmt/Program.cs:274 (`Directory.GetFiles(path, "*.lyr", SearchOption.AllDirectories)`), gleiches Muster src/Lyrtest/Program.cs:83
- Ebene(n): cli
- Beschreibung: Die rekursive Suche folgt Symlinks auf Verzeichnisse. `lyrfmt target/` mit `target/dirlink -> ../outside` schreibt `outside/o.lyr` um; ein Zyklus (`a/up -> ..`) wird bis zum ELOOP-Limit des Kernels verfolgt und `a/f.lyr` 41-mal als `a/up/a/up/…/f.lyr` gelistet (bzw. 41-mal geschrieben, ohne `--check`). Kein Hänger, aber ein In-Place-Werkzeug sollte den Zielbaum nicht verlassen. Korrekt: `EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }` oder Symlinks ausdrücklich überspringen.
- Repro (verifiziert: ja): `mkdir -p t/target t/outside; printf 'fn main(): int { return  1 ; }\n' > t/outside/o.lyr; ln -s ../outside t/target/dirlink; lyrfmt t/target` → `t/target/dirlink/o.lyr: formatted`, Datei außerhalb geändert. Zyklus: `mkdir -p c/a; ln -s .. c/a/up; … lyrfmt --check c` → 41 Zeilen.
- Betroffene Teammitglieder informiert: -
### [lexer-parser] Nachtrag zum BOM-Eintrag: Datei-vs-Text-Parität der Embedding-API ist an dieser Stelle gebrochen
- Severity: LOW → MEDIUM (Parität der öffentlichen API betroffen)
- Datei: src/Lyric.Frontend/Lexing/Lexer.cs:62/:153 (Ursache), src/Lyric.Frontend/Compiler/ScriptSource.cs:99 (Text-Pfad ohne BOM-Strip)
- Ebene(n): lexer | embedding
- Beschreibung: Von embedding-parity über die Embedding-API bestätigt: `vm.Compile("﻿fn main(): int { return 0; }", "bom")` wirft EmbeddingException mit zwei LYR-LEX0001, während `vm.CompileFile(...)` derselben Datei mit BOM und `lyrc build` sauber übersetzen. Fix bleibt derselbe: BOM an Offset 0 im Lexer überspringen (Grammatik §1.1), dann sind alle Quellen gleich behandelt.
- Repro: siehe oben (verifiziert: ja, durch embedding-parity)
- Betroffene Teammitglieder informiert: embedding-parity (Urheber der Bestätigung)

### [embedding-parity] Parallele Call-Aufrufe auf einer ScriptInstance korrumpieren den Frame-Pool (IndexOutOfRangeException, kein Hinweis in der API-Doku)
- Severity: MEDIUM
- Datei: src/Lyric.Vm/Interpreter.cs:129-134 (Frame-Pool "not thread-safe … one thread"), src/Lyric.Vm/LoadedProgram.cs:25-27 (ArgumentPool pro Programm), src/Lyric.Embedding/ScriptInstance.cs:6-14 (Klassendoku ohne Thread-Kontrakt), docs/guide/14-embedding.md
- Ebene(n): embedding | vm
- Beschreibung: Der Ein-Thread-Kontrakt steht nur intern im Interpreter. Weder ScriptInstance/LangVm noch der Guide nennen ihn. Acht Threads, die `inst.Call<long>("fib", 12)` auf derselben Instanz aufrufen, erhalten rohe `IndexOutOfRangeException` (nicht ScriptException) aus dem Interpreter; da die Pools (Frames, Argumentpuffer) geteilt werden, sind auch stille Falschergebnisse möglich. Zwei Instanzen desselben Moduls auf zwei Threads sind hingegen sauber (eigene Prepared/Pools). Erwartet: Dokumentation des Kontrakts im Embedding-API und/oder ein klarer Fehler (z.B. Reentranz-/Besitzprüfung).
- Repro: scratchpad/host/Probe.cs "parallel Call on one instance": errors=7 von 8 Threads mit IndexOutOfRangeException. (verifiziert: ja)
- Betroffene Teammitglieder informiert: vm-bytecode

### [cli-security] `let x = [[]];` → unbehandelte InternalCompilationException in lyrc (Exit 134): Sema meldet für ein verschachteltes leeres Array-Literal keinen Fehler, Lowering stolpert über `<error>`
- Severity: HIGH
- Datei: src/Lyrc/Program.cs:60-82 (fängt nur ProjectFileException; vgl. src/Lyrrepl/Session.cs:320, das InternalCompilationException fängt), src/Lyric.Frontend/Ir/TypeLowering.cs:15 (wirft „type not lowerable in current version: <error>"), Sema: Array-Literal-Inferenz (SEM0010 „'[]' fixes none on its own" greift nur für das äußerste `[]`)
- Ebene(n): sema, ir, cli
- Beschreibung: `let x = [];` erhält korrekt LYR-SEM0010; `let x = [[]];` (und jede tiefere Verschachtelung) passiert Sema ohne Diagnose mit Elementtyp `<error>`, `lyrc check`/`build` sterben mit Stacktrace statt Diagnose. Mit Annotation (`let x: int[][] = [[]];`) ist alles gut. Zwei Korrekturen: (1) Sema muss den nicht fixierbaren Elementtyp im inneren `[]` melden (bzw. das Lowering nicht starten, wenn ein `<error>`-Typ übrig ist); (2) lyrc/lyrbuild/lyrtest/lyrls sollten InternalCompilationException als LYR-Diagnose („internal compiler error: …", Exit 1) rendern, wie der REPL es tut — ein Compiler-Bug darf kein Exit 134 sein.
- Repro (verifiziert: ja): `printf 'fn main(): int {\n    let x = [[]];\n    return 0;\n}\n' > e.lyr; lyrc check e.lyr` → `Unhandled exception. Lyric.Core.InternalCompilationException: ir: type not lowerable in current version: <error> at Lyric.Ir.TypeLowering.Lower …`, exit 134. `lyrrepl` mit `let x = [[]]` → `internal: ir: type not lowerable…` (gefangen).
- Betroffene Teammitglieder informiert: semantic, ir-codegen

### [cli-security] Quadratische Laufzeit beim Rendern vieler Diagnosen: `SourceManager.Locate` sucht die Zeile linear (O(Zeilen) pro Diagnose)
- Severity: MEDIUM
- Datei: src/Lyric.Core/SourceManager.cs:66-72 (lineare Schleife über `LineStarts`), Aufrufer src/Lyric.Core/DiagnosticEngine.cs:92,106,178-179, LSP-Diagnostikkonvertierung
- Ebene(n): diagnostics, cli
- Beschreibung: Jede Positionsbestimmung läuft von Zeile 0 bis zur Trefferzeile. Bei n Diagnosen in einer n-zeiligen Datei sind das O(n²) Schritte. Messung (`lyrc check`, eine Funktion mit `let a = 1;`×n → n „never used"-Warnungen): 50 000 → 13,1 s, 100 000 → 48,9 s, 200 000 → 202 s; dieselbe Datei OHNE Warnungen (`x = x + 1;`×200 000) kompiliert in 5,9 s, d.h. der Compiler selbst ist linear, die Zeit steckt im Rendern (`--json` ebenso: 13,8 s). Im Language Server kommt `publishDiagnostics` für die 50 000-Zeilen-Datei erst nach 26 s. `LineStarts` ist sortiert — `Array.BinarySearch` macht daraus O(log n).
- Repro (verifiziert: ja): `python3 -c "print('fn main(): int {\n' + '    let a = 1;\n'*100000 + '    return 0;\n}')" > w.lyr; time lyrc check w.lyr --quiet` → ~49 s; `--verbose` zeigt, dass alle Phasen zusammen < 2 s brauchen.
- Betroffene Teammitglieder informiert: diagnostics
