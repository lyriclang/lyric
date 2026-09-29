# Lyric Bug Hunt — priorisierte Zusammenfassung (Team-Lead)

Stand: 2026-09-22, Basis dc32100c (v4.4.1). 10 Agenten, 145 Einträge in TASKLIST.md
(4 CRITICAL, 39 HIGH, 57 MEDIUM, 46 LOW), nach Dedupe ca. 120 eigenständige Funde.
Details, Repros und Zeilennummern: TASKLIST.md im selben Verzeichnis.

## P0 — Gültige Programme liefern falsche Ergebnisse (still)
1. do-while + break/continue in try/defer: Zielblock liegt in der Handler-Range → Endlosschleife, defer doppelt. LoopScope.cs:44-47, FunctionLowerer.cs:1359-1388. [ir-codegen, CRITICAL]
2. Monomorphisierung ohne Modulpräfix: gleichnamige generische fn/Typen aus zwei Modulen teilen eine Instanz → falscher Body. InstanceTable.cs:76-80,133-135. [ir-codegen, CRITICAL]
3. `if (c) 1 else 2.5` kompiliert, druckt 5e-324; Wurzel: Unify ohne AdaptLiteralType (int8-Fall crasht im Verifier). TypeChecker.cs:4116-4136, :4440/:4451. [spec-conformance CRITICAL, semantic HIGH]
4. ScalarReplacement (+Inliner): `p.x = (p = P{..}).y` schreibt ins neue Objekt (opt) vs. alte (noopt). ScalarReplacement.cs:467-513, Inliner.cs:131-207. [optimizer, HIGH ×2]
5. Struct-Aliasing-Familie („99“-Familie, Fortsetzung von 4.4.1): match-Binding aliast Scrutinee (FunctionLowerer.cs:2412-2431); Variant-Payload ohne structcopy (:2020-2039); `?Struct`-Feld/-Binding aliast statt Kopie (TypeChecker.cs:5046, Interpreter.cs:1041-1055). [ir-codegen HIGH ×2, cross-layer HIGH]
6. §5.4 verletzt: Interface-Default gewinnt gegen sichtbare Extension. FunctionLowerer.cs:3775-3788, ModuleLowerer.cs:929. [ir-codegen, spec-conformance HIGH]
7. defer: throw im defer-Body führt alle defers erneut aus (:786-797/:972-984); return/break im defer → Compiler-Stack-Overflow (:982/:857). [ir-codegen HIGH/MEDIUM, semantic]
8. Immutabilität löchrig: `let x` im selben Block redeklarierbar (TypeChecker.cs:1040); `x++` auf let (SemaRules.cs:181-199); Block-Arme eines match-Ausdrucks (SemaRules.cs:227) und Lambda-Bodies (SemaRules.cs:229) ungeprüft; `let s = St{}; s.v = 2` akzeptiert (SemaRules.cs:238). [semantic HIGH ×4, spec-conformance HIGH]
9. Flow: Narrowing nach innerem Block wiederhergestellt → VM0007 (TypeChecker.cs:946-953); DestructuringStmt im ExceptionAnalyzer unbekannt → VM0010 (ExceptionAnalyzer.cs:98-125); defer-Body zählt als sofort ausgeführt (FlowAnalyzer.cs:100). [semantic HIGH ×2, MEDIUM]
10. `lyric run app.lyr --grant none` schränkt NICHTS ein (Optionen nach .lyr gehen an lyrc). Lyric.Cli/Program.cs:247,265-269. [cli-security HIGH]
11. Embedding: Marshal.FromLyric liefert für Objekt/Array/Optional/Enum still 0 (Marshal.cs:98-108); Convert.ChangeType verlustbehaftet (:110-122). [embedding-parity HIGH/MEDIUM]

## P1 — Prozessabbrüche (Exit 134/141) statt Diagnose oder Panik
12. Global-Vorwärtsbezug über Funktionsaufruf → null → InvalidOperationException in ldfld; SEM0057 nicht transitiv. TypeChecker.cs:1451-1461, LyrValue.cs:130, VmHost.cs:42-49. [vm-bytecode, cross-layer, semantic — EIN Fund]
13. `string * n` / `arrrep` mit großem n: ArgumentOutOfRange (n ≥ 2^31) bzw. OOM-SIGABRT; Spec verlangt VM0006. NativeRegistry.cs:335-338, Interpreter.cs:820-835, JitRuntime.cs:60-75. [vm-bytecode HIGH ×2]
14. Stack Overflow bei tiefer Verschachtelung (~4000 `(`, 8000 `1+`): Parser, TypeChecker.CheckBinary, AstDumper — tötet lyrc, lyrfmt, lyrrepl, lyrls, lyrdbg und Embedding-Hosts. [cli-security, cross-layer, embedding-parity]
15. Reentranz Skript→Host→Skript umgeht MaxCallDepth → CLR-StackOverflow. Interpreter.cs:137/:423, ScriptInstance.cs:226-236. [embedding-parity HIGH]
16. Compiler-ICEs aus gültigem oder nur leicht ungültigem Code: `never` in if-Ausdruck/`??` (FunctionLowerer.cs:1392,3883); Enum-Struct-Variante mit Literal-Subpattern (:2580-2609); `defer { throw }` in try (:841-844); `o += 1` auf narrowed `?T` (:1774-1781); `let x = [[]]` (TypeChecker.cs:1026, TypeLowering.cs:15); Zuweisung an gecaptures `let` im Lambda; break/continue außerhalb Schleife (TypeChecker.cs:1014). Lyrc/Program.cs:33-56 fängt ICEs nicht. [ir-codegen, semantic, spec-conformance, cli-security]
17. Leeres Pfadargument `""` → ArgumentException in lyrc/lyrpack/lyrbuild/lyrtest. [cli-security HIGH]
18. Terminal-Injection: ESC/BEL/OSC aus Quelltext, Dateinamen, Panic-Texten roh auf stderr. DiagnosticEngine.cs:84-100, VmHost.cs:42-47, Session.cs:311. [cli-security HIGH]
19. IrVerifier läuft nur im Debug-Build (Phase.cs:52); Reader typisiert Slots/Args nicht (BytecodeReader.cs:880-1035) → im Release wird jede Sema-Lücke zu stillem Falschcode; Lader-Lücken: newarr-Immediate (int)-Cast, callind-Arity ungeprüft, Block-Offsets nicht monoton geprüft, GlobalInit auf Import still übersprungen. [ir-codegen MEDIUM, cross-layer, vm-bytecode LOW]

## P2 — Spec-Abweichungen ohne Laufzeitfolgen, Parser, Embedding-Parität
20. Parser: `f<List<int>>()` nicht als Call mit Typargs erkannt (Parser.cs:196-200, HIGH); throws-Klausel landet im Funktionstyp (:816-828); `(a < b) > (c)` als Call gelesen (:212); Guard in Klammern als Lambda (Parser.Patterns.cs:219); `'😀'` → LEX0008 (UTF-16-Zählung, Lexer.cs:565-602); NUL beendet Lexen still (Lexer.cs:57/225); BOM nur bei Dateien (Lexer.cs:62); `///`-Doku vor Attribut verloren. [lexer-parser]
21. Sema/Spec: doppelte Parameternamen; Throwable/Iterator via extend abgelehnt (§9.1/§7.2); Satisfies `_ => true`; Alias-Zyklus ohne SEM0064; `extend int[]` ohne SEM0047; SEM0072 fehlt für Namespace-Import; Struct-Rekursion über ?/Tuple/generisch ohne SEM0056; Enum mit optionalem Payload nur via `_` erschöpfend; RecordCaptures übersieht Destructuring. [semantic, spec-conformance MEDIUM]
22. Embedding-Parität (stdout/Exit über 196 Programme identisch; Divergenzen nur Diagnostik/Verhalten): Warnungen verloren (LangVm.cs:334-349); Modulname aus Dateiname statt Header (:251-257); lyric.json ignoriert (:334-341); Budget nicht an Callbacks vererbt (ScriptInstance.cs:82-84); parallele Calls → IndexOutOfRange; Host-Objekt ohne Typprüfung; Run gibt Rohwert statt &0xFF. [embedding-parity MEDIUM ×6]
23. Spec §13 Closures „+1“-Satz widerspricht Writer/Reader (BytecodeWriter.cs:605, spec 13-bytecode.md:936) — Format-Vertrag; Capability-Tabellen §4.5 vs §13 widersprüchlich. [ir-codegen, spec-conformance]

## P3 — Diagnostik-Qualität und CLI-Kosmetik
24. Semantikfehler unter IR0001 („cannot lower it yet“): fehlendes Pflichtfeld im Initializer, `p.x++`, `x == null` auf Nicht-Optional, Marker-Interface-Wert u.a. — §12.1 reserviert IR0001 für gültiges Lyric. [diagnostics HIGH, cross-layer, spec-conformance]
25. Codes richtungsabhängig/überlappend (aufteilen oder vereinen): RES0002 vs SEM0011; SEM0015 vs SEM0031; SEM0003 statt SEM0059 für `?int == int`; SEM0019 bündelt drei Ursachen (aufweiten); LEX0004/0006 + PAR0006 am selben Span; `0b12` → PAR0016 statt LEX0003; PAR0019 statt PAR0008; PAR0025 statt PAR0040. [diagnostics, spec-conformance]
26. Kaskaden/Dedupe: keine Deduplizierung (SEM0026 ×5 mit unsubstituiertem 'R'), SEM0058 vergiftet nicht (→ SEM0002 „did you mean“), SEM0052 schlägt das Geschriebene vor (6 Diagnosen), Sema läuft auf Parser-Recovery-Knoten, Sortierung instabil (List.Sort >16), Enum-Namen in Meldungen („got Semicolon“, „operator 'Eq'“), Caret bei Tabs, Pfade relativ/absolut gemischt, VM0004 druckt 1024 Frames. [diagnostics]
27. CLI: `--json` unvollständig (lyrvm ignoriert es; CLI0016/0008/0017 als Text hinter JSON; lyrfmt/lyrbuild ignorieren es); unbekannte Optionen/fehlende Werte still verschluckt; Option vor Datei → „failed to read file: -o“; lyrvm liest `--grant` hinter `--`; `--verbose`-Zeiten ×100 (Stopwatch-Ticks); quadratisches Rendern bei vielen Diagnosen (SourceManager.Locate); lyrfmt folgt Verzeichnis-Symlinks; REPL-Wrapper per Eingabe verlassbar; ungültiges UTF-8 still U+FFFD; Lyrtest fehlt in Lyric.Cli.csproj (Dev-Build). [cli-security, diagnostics]
28. JIT-Backtraces: Panics aus ldelem/stelem/optget ohne Quellposition; Mischbetrieb verliert Interpreter-Frames. [vm-bytecode, optimizer LOW]

## Als sauber verifiziert
- Bytecode: 62 Opcodes Spec ↔ Core ↔ Emitter ↔ Interpreter ↔ JIT, 0 Abweichungen; Format 4.0; Integer/Float/Konversions-Semantik spec-konform, Interpreter und JIT bitidentisch; Exceptions, Coroutinen, Rekursionslimit korrekt.
- Conformance-Suite 158/158 (1 skipped), stdlib-tests 166/166, Bytecode-Tests 184/184; Versionen 4.4.1/Spec 4.4 konsistent; docs-Mirror byte-identisch.
- Fuzzing: 1600 + 650 Bytecode-Mutanten (kein Reader-Crash), 2349 Truncation-Präfixe + 854 Deletions über lyrc check (kein Crash, keine ungültige Span), LSP 15 Request-Typen auf kaputten Dateien stabil.
- Embedding: 196 Programme mit identischem stdout und Exit-Code; gleiche Pipeline-Defaults, Stdlib-Auflösung, Loader, Capability-Prüfung.
- Optimierer: Inliner/Devirtualizer/Reachability/Fusion differentiell (opt/noopt × Interpreter/JIT) über 17 Probes ohne weiteren Befund; keine Konstantenfaltung vorhanden.
- CLI: `lyric new`-Templates injektionssicher, Import-Traversal unmöglich, lyric.json-Validierung, LSP/DAP-Framing robust.

## Hinweise zur Umgebung
- Kein Linux-dotnet im WSL: Agenten haben SDK 10.0.401 nach ~/.dotnet bzw. ins Scratchpad installiert oder das Windows-SDK genutzt.
- Verbleibende Agent-Worktrees mit Probes unter .claude/worktrees/ (nichts committet); `git worktree prune`/`remove` nach Sichtung.
