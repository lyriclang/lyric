# Makros und Metaprogrammierung für Lyric — Analyse und Design

Stand: 2026-09-22, Basis dc32100c (v4.4.1), Autor: macro-abi (Evolution-Team 3).
Prototyp: `comptime` auf Branch `worktree-agent-ab3c14434f8eda027`, Beispiel `examples/macros/comptime.lyr`.

## 1. Ist-Stand

Lyric hat heute **keine** Metaprogrammierung im engeren Sinn, aber drei Hebel, die eine solche vorbereiten:

| Hebel | Wo | Was es leistet | Was es nicht leistet |
|---|---|---|---|
| Attribute als Daten | Guide 15, Spec §4.7, `Sema/AttributeValues.cs` | Structs mit Marker-Interfaces (`OnFunction`/`OnType`/`OnModule`), Argumente = Literale, benannte Literale, Unit-Varianten; Zeilen im Modul, Host liest sie (`module.Attributes`), Feldnamen attributierter Typen reisen mit | „describes; does nothing“: kein Attribut erzeugt Code; einzige compilergelesene Ausnahme `@Deprecated` (Warnung + Uhr). Kein `1 + 2` als Wert („there is no constant folding“) |
| build.lyr als Programm | Guide 16, `std.build` | Ein Lyric-Programm mit allen Capabilities läuft VOR dem Build und darf Quelltext schreiben (`writeText("src/version.lyr", …)`) | Kein Zugriff auf die eigene Programmstruktur (keine Deklarationsliste, kein Parser als Bibliothek) → Generatoren müssten Lyric-Quelltext selbst parsen |
| VM in C#, Embedding-API | Guide 14, `Lyric.Embedding` | Der Compiler könnte Lyric-Code während des Kompilierens ausführen (lyrbuild tut es bereits über `LangVm`) | Wurde bisher nicht genutzt; Architekturregel „lyrc executes nothing“ (`ArchitectureTests.Lyrc_ships_no_runtime`) |

Weitere Fakten: Generics per Monomorphisierung (`InstanceTable`), keine Reflection zur Laufzeit, keine Feldaufzählung aus Lyric heraus, keine Konformanz-Synthese (Vorschlag von new-features/Prototyp 03 aus Team 2: Swift-Modell, `struct P :: [Equatable<P>]` ohne Body → Compiler synthetisiert). `@Test` wird von `lyrtest` aus den Attributzeilen gelesen (`src/Lyrtest`), d.h. die Test-Registrierung ist bereits ohne Makro gelöst.

## 2. Nützlichkeit: welche Probleme würde Metaprogrammierung lösen?

Gezählt in `stdlib/std/`, `examples/`, `templates/`, den Prototypen aus Team 2 und dem eingebetteten Hosting.

| Anwendungsfall | Heute (Zeilen, Beleg) | Mit Makro/Metaprogrammierung | Löst es … |
|---|---|---|---|
| **Derive Equatable/Hashable/Ordered/Display** | 72 Zeilen für ein Struct mit 3 Feldern + Enum (Team-2-Prototyp 03, `ist.lyr`); `IoErrorKind`, `JsonValue`, `Wait` sind heute NICHT Equatable (stdlib-review); stdlib: 27 Handimplementierungen in `core.lyr`, 8 in `time.lyr`; `IoError.message()` mappt 6 Varianten in 8 Zeilen von Hand | 38 Zeilen (Konformanz-Synthese, Swift-Modell) bzw. 30 (Rust `#[derive]`); Enum-Display = 0 Zeilen | **Synthese** (new-features), kein Makro nötig |
| **JSON-Serialisierung von Nutzertypen** | `json.lyr` 805 Zeilen, davon Writer 170 Zeilen für `JsonValue`; für ein eigenes Struct `Point` heute: `toJson` 6–10 Zeilen, `fromJson` 10–20 Zeilen mit `?`-Kette pro Feld | Rust `#[derive(Serialize)]` 1 Zeile; Swift `Codable` 0 Zeilen | Synthese-Regel für `ToJson`/`FromJson` (Anker von stdlib-redesign) — dieselbe Maschinerie wie Equatable (feldweise), kein allgemeines Makro |
| **Builder-Boilerplate** | 1 Zeile pro Feld (`pub mut fn withX(x: T): Builder`) plus `build()` — bei 6 Feldern ~20 Zeilen | Lombok `@Builder` 1 Zeile | Gering: Lyric hat Struct-Initializer mit Defaults (`K { }`) — der Builder-Bedarf ist klein; Struct-Update `..base` (Team-2 Prototyp 20) deckt den Rest |
| **Test-Registrierung** | `@Test` + `lyrtest` liest Zeilen: **0 Zeilen Boilerplate** | Rust `#[test]` gleich | Bereits gelöst durch Attribute-als-Daten |
| **Format-Spec-Validierung** | `formatInt(v, "N2")` paniert bei ungültigem Spec zur Laufzeit (`fmt.lyr:18-21`); Spec steht als Literal im Quelltext | Rust `format!` prüft zur Compile-Zeit | (a) direkt im Compiler, weil f-String-Specs Literale sind (new-features fixiert die Mini-Sprache); (b) allgemein: `comptime checkedSpec(":08.3f")` → Panik wird Compile-Fehler an der Zeile (Prototyp, `examples/macros/comptime.lyr:25`) |
| **Regex-/SQL-Literale** | Kein Regex in der stdlib; SQL-Strings ungeprüft | Rust `regex!`/`sqlx::query!` (Compile-Zeit-Prüfung gegen Schema) | `comptime validate("…")` prüft und gibt den String zurück; ein kompiliertes Regex-Objekt bräuchte Stufe 2 (nicht-skalare Comptime-Werte) |
| **Lookup-Tabellen / teure Konstanten** | Werden im `<globals>`-Initializer bei jedem Start berechnet (`let TABLE = build();`) | Zig `comptime` | `comptime` (Prototyp: `fib(90)` und eine 21-Einträge-Tabelle werden zu Literalen; `fib` verschwindet aus dem Modul, Disassembly belegt es) |
| **Codegen für FFI-Bindings** | Nicht vorhanden (Bindings müssten von Hand als `extern`-Deklarationen geschrieben werden) | Rust bindgen, Zig `@cImport`, Java jextract | **Generator-Modell**: ein Werkzeug `lyrbind` schreibt `.lyr`-Dateien; build.lyr kann es aufrufen (Guide 16) — Quelltext-Generierung, kein Makro |
| **Compile-Zeit-Assertions / Versionsprüfung** | keine | Rust `const _: () = assert!(…)`, Zig `comptime assert` | `comptime check()` mit Panik → `LYR-CT0002` an der Stelle (Prototyp) |
| **Wert-Attribute aus Ausdrücken** | `@Retry { limit = 1 + 2 }` abgelehnt (Guide 15) | — | `comptime`-Ergebnis als Attributwert zulassen (Folgeschritt: `AttributeValues.Resolve` erkennt `ComptimeExpr` mit ausgewertetem Wert) |

**Gesamtbewertung:** Ein allgemeines Makrosystem **lohnt sich nicht**. Die zwei großen Posten (Derive, JSON) sind feldweise Synthesen mit geschlossenem Regelsatz, die Test-Registrierung ist über Attribute gelöst, und die Restfälle (Tabellen, Prüfungen, Konstanten) deckt eine **sandboxed Compile-Zeit-Auswertung** ab, die Lyric wegen seiner VM in C# fast geschenkt bekommt. Es lohnt sich also **in Form X = Synthese + `comptime` + Generatoren über build.lyr**, nicht als AST- oder Textmakros.

## 3. Sprachcharakter: welche Form passt, welche zerstört ihn?

Lyric ist explizit, deterministisch, spec-first, host-lesbar (Attribute sind Daten), ohne Vererbung, ohne implizite Konvertierung, mit einem Sandbox-Versprechen für die VM. Daraus folgen vier Prüfsteine für jede Makroform: (1) Bleibt der Quelltext das, was der Formatter/LSP/Leser sieht? (2) Bleibt die Bedeutung eines Programms aus der Spec ableitbar, ohne ein Makro auszuführen? (3) Kann Compile-Zeit-Code aus der Sandbox ausbrechen oder den Build nicht-deterministisch machen? (4) Zeigen Fehlermeldungen auf Quelltext, den der Nutzer geschrieben hat?

### Vergleichstabelle Makros (Bewertung 1 = schlecht … 5 = sehr gut)

| System | Hygiene | Debugbarkeit | Tooling (LSP/Formatter) | Compile-Zeit | Sandbox-Sicherheit | Lesbarkeit | Fehlermeldungen | Passt zu Lyric? |
|---|---|---|---|---|---|---|---|---|
| C Präprozessor (Textersetzung) | 1 | 1 | 1 | 5 | 5 (führt nichts aus) | 1 | 1 | Nein — abschreckendes Beispiel: bricht (1), (2), (4) |
| Rust `macro_rules!` (deklarativ, hygienisch) | 4 | 2 | 3 (rust-analyzer expandiert) | 4 | 5 | 3 | 2 (Fehler in Expansion) | Bedingt — Musterersetzung ohne Ausführung; Syntax-Erweiterung widerspricht (1) |
| Rust proc-macros / `#[derive]` | 3 (Spans, `Span::call_site`) | 2 | 3 | 2 (Compile des Makro-Crates) | 1 (beliebiger Code im Compiler-Prozess, Netz/Datei möglich) | 4 (derive) / 2 (attribute macros) | 3 | `derive` ja (als Synthese), proc-macros nein (3) |
| Zig `comptime` | 5 (keine Syntaxebene, nur Werte/Typen) | 3 | 4 (zls kennt comptime-Werte teils) | 3 | 4 (kein I/O, aber `@embedFile`) | 4 | 4 (comptime-Panik = Compile-Fehler mit Trace) | **Ja** — Werte statt Syntax; determinstisch; VM vorhanden |
| Swift Macros (5.9; typisiert, hygienisch, Expansion sichtbar) | 5 | 4 (Expansion im Editor) | 5 | 1 (eigener Compiler-Plugin-Prozess, langsam) | 4 (sandboxed Plugin-Prozess) | 3 | 4 | Konzept ja (Expansion sichtbar, sandboxed), Kosten nein |
| Nim / Julia AST-Makros | 2–3 | 2 | 2 | 4 | 1 | 2 | 2 | Nein (1), (2) |
| Kotlin KSP / Annotation Processing | 5 (erzeugt neue Dateien, ändert nichts) | 4 (Dateien sichtbar) | 4 | 3 | 2 (JVM-Code im Build) | 4 | 3 | Ja als **Generator-Modell** (build.lyr) |
| C# Source Generators + Analyzer | 5 | 4 (generierte Dateien im Projekt sichtbar) | 5 | 3 | 2 (läuft im Compiler-Prozess, ungesandboxt) | 4 | 4 (Analyzer-Diagnostics) | Ja als Generator; Analyzer = `lyrc check`-Plugins (nicht vorgeschlagen) |
| Scala 3 `inline` + Quotes | 4 | 2 | 3 | 2 | 2 | 2 | 3 | Nein — zu mächtig, Typebene |
| Elixir `quote`/`unquote` | 3 | 2 | 2 | 4 | 1 | 2 | 2 | Nein |
| D `mixin`/Templates | 1 (String-Mixins) / 3 | 2 | 2 | 3 | 3 | 2 | 2 | Nein |
| **Lyric-Vorschlag: Synthese + `comptime` + Generatoren** | 5 (keine Syntaxebene) | 4 (Wert sichtbar im Disassembly; Panik-Trace) | 5 (Formatter/LSP sehen nur normales Lyric) | 3 (zweiter Lowering-Lauf) | **5 (Capability.None + Budget, deterministisch per Spec)** | 4 | 4 (Fehler an der Site, Ursache aus der VM) | — |

**Was den Charakter zerstört:** alles, was Syntax erzeugt oder ersetzt (C, Nim, Elixir, proc-macros, D-Mixins): der Formatter hätte zwei Sprachen, der LSP müsste expandieren, die Spec müsste ein Makro-Ausführungsmodell definieren, und ein Makro könnte Konventionen der Sprache unterlaufen (implizite Konvertierung durch Umschreiben, „Vererbung“ durch Feld-Kopieren). Ebenso alles, was **Code im Compiler-Prozess ohne Sandbox** ausführt (proc-macros, C# Generators als Plugins): Lyrics stärkstes Versprechen — „was das Modul darf, steht im Modul“ — würde beim Bauen nicht gelten.

**Was passt:** Formen, bei denen (a) nur **Werte** entstehen (Zig-Comptime) oder (b) nur **neue Dateien** entstehen, die der Leser öffnen und der Formatter formatieren kann (KSP/Source Generators/build.lyr), oder (c) der Compiler nach einer **spec-fixierten Tabelle** Methoden synthetisiert (Swift-Synthese). Alle drei sind für den Leser ohne Makro-Ausführung nachvollziehbar: die Synthese steht in §5.1 als Tabelle, der comptime-Wert ist per Definition derselbe wie der Laufzeitwert, die generierte Datei liegt auf der Platte.

## 4. Design-Empfehlung: drei Stufen, kein Makro-Schlüsselwort

### Stufe 1 — Konformanz-Synthese (Spec §5.1, Eigentümer: new-features)
Genau wie mit new-features abgestimmt: `struct P :: [Equatable<P>, Hashable<P>, Ordered<P>, Display]` ohne Methodenbody → der Compiler synthetisiert feldweise nach einer **geschlossenen Tabelle** in §5.1 (Feldreihenfolge, `equal ⇒ equal hash`, Display = Initializer-Syntax, Enum: Tag dann Payload). Geschriebene Methode gewinnt. Erweiterung um `ToJson`/`FromJson` (Anker aus `std.json`, stdlib-redesign) nach derselben Tabelle in einem Folgeschritt — **kein `@Derive`, kein `derive`-Wort, kein compilergelesenes Attribut** (new-features: Attribute bleiben inert; falls je ein codeerzeugendes Attribut kommt, eigene Namensklasse `@!Name`).

### Stufe 2 — `comptime` (prototypisiert)
- **Syntax:** `ComptimeExpr = 'comptime' UnaryExpr` — kontextuelles Wort (wie `type`, `throws`, `extern`): es öffnet den Präfix nur, wenn ein Token folgt, das einen Ausdruck beginnt; `let comptime = 2; comptime + 3` bleibt ein Name (Test `The_word_stays_a_name_before_an_operator`). Bindet wie ein Präfixoperator: `comptime 60 * 60` ist `(comptime 60) * 60`; zusammengesetzte Ausdrücke in Klammern.
- **Semantik:** `comptime e` hat denselben Wert und Typ wie `e`. Das Präfix legt nur den Zeitpunkt fest und beschränkt, was `e` erreichen darf: nur Namen auf Modulebene (Globals, Funktionen, Enum-Varianten, Statics), kein Lokal/Parameter/`this`/Lambda/Zuweisung (`LYR-SEM0100`); Ergebnistyp muss ein Literal-Typ sein: Skalare, `bool`, `char`, `string` (`LYR-SEM0100`). Verschachteltes `comptime` wird als Teil des äußeren ausgewertet.
- **Expansion in der Pipeline** (`src/Lyric.Frontend/Compiler/SourceCompiler.cs`): nach der Sema, nur bei Stage Emit: (1) Lowering im **Hoist-Modus** — jede Site wird zur parameterlosen Funktion `<comptime:i>` (Name mit spitzen Klammern wie `<globals>`, aus keiner Quelle schreibbar), diese Funktionen sind die **einzigen Wurzeln**, Reachability schneidet alles andere weg, die Capability-Bits werden aus den **verbleibenden** Imports berechnet (§4.5-Regel „Bits = Bedarf der Imports“ auf das Auswertungsmodul angewandt); (2) Bytes an einen `IComptimeRunner` (Interface in `Lyric.Core`, damit das Frontend die VM nicht referenziert); (3) Lowering im **Werte-Modus**: jede Site wird zur Konstante (`Const`-Instruktion, `EmitConst`). Ohne Runner (Editor, `lyrc check`) wird die Site als ihr innerer Ausdruck gelowert — Prüfen läuft, nichts wird ausgeführt; ein Build ohne Runner meldet `LYR-CT0001`.
- **Runner** (`Lyric.Vm/VmComptimeRunner.cs`): `LoadedProgram.Load(module, natives, Capability.None, budget)`, pro Site `Invoke(index, budget)`; Capability-Verweigerung, Panik, Budget → `ComptimeOutcome.Failed(why)` → Diagnose `LYR-CT0002` an der Site mit der Ursache („panicked [LYR-VM0011]: too big“, „did not finish within the compile-time budget“, „module requires capability 'fileAccess'“).
- **Hygiene:** trivial, weil keine Syntaxebene existiert — es gibt nichts, was einen Namen einfangen könnte.
- **Fehlerzuordnung:** die Site hat einen Span; Sema-Fehler stehen am Teilausdruck (Lokal, Lambda), Laufzeitfehler an der ganzen Site. Das Auswertungsmodul trägt die Source-Map, so nennt ein Panik-Backtrace die Zeile innerhalb der aufgerufenen Funktion (Erweiterung: Backtrace als Notes der Diagnose ausgeben).
- **Determinismus:** folgt aus der Spec: Arithmetik bitgenau, die nicht-deterministischen Quellen (Uhr, Umgebung, Dateien, Netz) sind capability-gated und im Runner nicht gewährt; Budget zählt Instruktionen, nicht Zeit. Die eine capability-freie Ausnahme, `std.random.secureRandom` (und darüber `Random.fresh()`), lehnt der Runner per Import-Name ab (`VmComptimeRunner.NonDeterministic`, mit stdlib-redesign abgestimmt: kein neues Bit; `Random.seeded(n)` bleibt comptime-fähig). Ein Build ist damit reproduzierbar.
- **Formatter/LSP/DAP:** Formatter druckt `comptime ` + Operand (umgesetzt, idempotent). LSP: keine Änderung (Sema liefert Typ und Diagnosen; Hover könnte den ausgewerteten Wert zeigen, wenn der LSP einen Runner bekommt — bewusst nicht: der Editor führt nichts aus). DAP: keine Sites im Bytecode, nur Konstanten — ein Breakpoint auf einer comptime-Zeile hält am Laden der Konstante.
- **Sandbox/Capabilities:** unverändert für das Programm (Bits werden weiterhin aus allen Modulen berechnet); das Auswertungsmodul hat eigene Bits (nur was die Sites erreichen) und wird mit `Capability.None` geladen. Ein Host, der `Capability.All` gewährt, weitet damit NICHT die Compile-Zeit-Auswertung.
- **Spec-Kapitel:** §6 (neuer Präfix-Ausdruck), §7.1/§4.3 (welche Namen sichtbar sind), §4.5 (Auswertungsmodul erhält keine Capability), §12/Anhang A (`LYR-SEM0100`, `LYR-CT0001/0002`), §13 unverändert (kein neues Format, keine Opcodes).
- **Breaking:** nein (Minor): `comptime` kontextuell; ein Programm, das eine Funktion `comptime` hat und `comptime(x)` schreibt, würde heute anders parsen — das ist der einzige Bruch (Identifier `comptime` vor `(`). Empfehlung: im Major echtes Schlüsselwort.
- **Architektur:** `lyrc` referenziert nun `lyrrt` als Evaluator; die Architekturregel wird von „lyrc führt nichts aus“ zu „lyrc führt nichts mit einer Capability aus, und das Frontend referenziert keine Runtime“ (Test `The_frontend_references_no_runtime` ersetzt `Lyrc_ships_no_runtime`). Das ist eine bewusste ADR-Entscheidung (CONTRIBUTING Regel 2 ist nicht betroffen: comptime ist kein zweiter Mechanismus für ein bestehendes Konzept).
- **Folgeschritte (nicht prototypisiert):** (a) comptime-Werte als Attributargumente (`@Retry { limit = comptime 1 << 10 }`) — schließt die Guide-15-Lücke; (b) Literal-Typen erweitern: Arrays und Structs aus Literal-Typen (Lowering zu `newarr`/`newobj` + Stores — Lookup-Tabellen als `int[]`); (c) `comptime` an Funktionen (`comptime fn`) als Erzwingung „nur zur Compile-Zeit aufrufbar“ — nicht nötig, Sites genügen; (d) Caching der Auswertung pro Site-Hash für Editor-Builds.

### Stufe 3 — Generatoren über build.lyr (Source-Generator-Modell, kein Makro)
Guide 16 erlaubt es schon: build.lyr schreibt Dateien vor dem Build. Was fehlt, ist die **Leseseite**: ein Modul `std.build.source` mit `declarations(path): Declaration[]` (Typen, Felder, Varianten, Attribute als Daten — dieselben Informationen, die `module.Attributes.FieldsOf` einem Host liefert). Damit schreibt ein Generator aus einem Enum die Display-Konformanz oder aus einer .NET-Assembly `extern`-Deklarationen (`lyrbind`, design/abi.md). Regeln: generierte Dateien liegen unter `gen/` (in `lyric.json` als `sourceRoot`-Zweig), werden formatiert (`lyric fmt`), tragen einen Kopfkommentar `// generated by …`, und der LSP behandelt sie als Quelltext. Keine Spec-Änderung: es ist ein Werkzeug, keine Sprachregel.

### Abgelehnte Kandidaten
- (i) „keine Makros, nur C#-Compiler-Plugins über die Embedding-API“: verletzt die Sandbox (Plugin-Code ungesandboxt im Compiler), macht Builds hostabhängig; abgelehnt.
- (iii) hygienische AST-Template-Makros mit Expansion-Dump: technisch sauber (Swift), aber teuer (Spec braucht ein Expansionsmodell, Formatter/LSP zwei Ebenen) und ohne Bedarf, der nicht von Stufen 1–3 gedeckt wäre; abgelehnt.
- (v) reines Generator-Modell ohne comptime: deckt Tabellen/Prüfungen nur umständlich (jede Konstante eine Datei); daher Stufe 3 als Ergänzung, nicht als Ersatz.

## 5. Prototyp

**Commit** „comptime: the compiler evaluates a marked expression through the VM in a sandbox (prototype)“ auf `worktree-agent-ab3c14434f8eda027`.

Dateien: `src/Lyric.Frontend/AST/Expressions.cs` (`ComptimeExpr`), `Parsing/Parser.cs` (`ParsePrefix`, `BeginsExpression`), `Sema/TypeChecker.cs` (`CheckComptime`), `Sema/TypeResult.cs` (`ComptimeSites`), `Sema/{FlowAnalyzer,ExceptionAnalyzer,SemaRules,WarningAnalyzer}.cs`, `AST/{AstChildren,AstDumper}.cs`, `Formatting/AstFormatter.cs`, `Ir/Lowering/ComptimeTable.cs` (neu), `Ir/Lowering/{TypeTable,FunctionLowerer,ModuleLowerer}.cs`, `Ir/IrModule.cs`, `Compiler/SourceCompiler.cs` (`EvaluateComptime`, `ComptimeDiagnostics`), `src/Lyric.Core/ComptimeRunner.cs` (neu, Interface), `src/Lyric.Vm/VmComptimeRunner.cs` (neu), `src/Lyrc/{Lyrc.csproj,Program.cs}`, `src/Lyrbuild/Program.cs`, `src/Lyric.Embedding/LangVm.cs`, `docs/Grammar.md`, `docs/guide/16-building.md`, `examples/macros/comptime.lyr`, `tests/Lyric.Tests.Vm/ComptimeTests.cs` (12 Tests), `tests/Lyric.Tests.Cli/ArchitectureTests.cs`.

Beispiel (`lyric run examples/macros/comptime.lyr`):
```
powers: 1 2 4 8 16 32 64 128 256 512 1024 2048 4096 8192 16384 32768 65536 131072 262144 524288 1048576
spec:   :08.3f
fib 90: 2880067194370816120
inline: 31536000
```
Disassembly des Moduls: `const string "1 2 4 8 …"`, `const i64 2880067194370816120`, `const i64 31536000`; die Funktionen `powersOfTwo`, `fib`, `checkedSpec` sind nicht mehr im Modul.

Fehlerfälle (geprüft): Lokal in Site → `LYR-SEM0100`; Lambda → `LYR-SEM0100`; `comptime checked(99)` → `error[LYR-CT0002]: 'comptime' expression could not be evaluated: panicked [LYR-VM0011]: too big`; Endlosschleife → „did not finish within the compile-time budget“; `comptime exists("/etc/hostname")` → „it reaches something that needs a capability … 'fileAccess'“; eine reine Site in einem Programm mit `std.io.file` wertet aus (Test `A_pure_site_evaluates_in_a_program_that_reads_files_elsewhere`).

Was fehlt: nicht-skalare Werte (Arrays/Structs), comptime als Attributwert, Backtrace-Notes in `LYR-CT0002`, Caching, LSP-Hover mit Wert.
