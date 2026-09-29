# Lyric Evolution Team — gemeinsame Taskliste

Format pro Eintrag (ein Block, mit `flock` anhängen, nie den Inhalt anderer umschreiben):

### [<agent>] <kurzer Titel>
- Kategorie: design | prototype | implementation | comparison | analysis | exercise | bug | decision
- Priorität: HIGH | MEDIUM | LOW
- Status: proposed | in-progress | done | blocked
- Intern (Lyric): <pfad relativ zum Repo bzw. Worktree>:<zeile> — Lyric-Beispiel (Codeblock)
- Extern (Vergleich): <Sprache(n)> — Erklärung und Beispiel (Codeblock)
- Beschreibung: Was, Warum, Semantik, Spec-Kapitel, Breaking ja/nein, Wechselwirkungen mit anderen Vorschlägen
- Artefakt: <pfad zu Datei/Verzeichnis im Worktree oder deliverables/>
- Abgestimmt mit: <namen oder "-">


### [stdlib-redesign] Design-Dokument design/stdlib-2.md — Zielbild 4.5 (additiv) / 5.0 (breaking)
- Kategorie: design
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): design/stdlib-2.md (Worktree agent-aa7b5e912a78e6f2d); Grundlage: team2 stdlib-review (43 Einträge), Prototypen 01/09/18/21, stdlib-field-visibility.md
- Extern (Vergleich): Rust std / Go / Kotlin / Swift / Python / .NET BCL pro Designentscheidung
- Beschreibung: Modulstruktur, Namenskonventionen (camelCase, Verben, of/from/to/as, Plural), Antwortformen (`?T`/bool/OrThrow/Result/Panik) in Spec §11 verankerbar, Fehlermodell (Result<T,E> als WERT-Form, throws bleibt der Mechanismus — CONTRIBUTING Regel 2 gewahrt), Iterator-Protokoll (next(): ?T bleibt; Terminatoren als Default-Methoden), Container, Display, Zahlen, Strings, Zeit, Random, Hash, Pfade/Dateien, Prozess, JSON, Test, Nebenläufigkeit, Deprecation-Pfad, Migrationsplan.
- Artefakt: design/stdlib-2.md; Kopie in deliverables/stdlib-redesign/
- Abgestimmt mit: new-features, pattern-lambda, macro-abi (Anfragen verschickt)

### [stdlib-redesign] Prototyp (a): std.result mit Result<T,E> + Kombinatoren + Umstellung von 3-5 Funktionen
- Kategorie: prototype
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): stdlib/std/result.lyr (neu)
  ```lyr
  pub enum Result<T, E> { Ok(T), Err(E), fn ok(): ?T, fn unwrapOr(x: T): T, fn map<U>(f), fn andThen<U>(f), fn mapErr<F>(f), fn orThrow(): T throws E }
  ```
- Extern (Vergleich): Rust `Result<T,E>`, Swift `Result<Success, Failure>` + `try?`, Kotlin `runCatching`/`Result<T>`, Go `(v, err)`
- Beschreibung: Additiv (4.5). Getestet ohne `?T`-Payload (Compiler-Bug P0-3). Alte Formen bleiben; Deprecated-Uhr erst in 5.0-Vorbereitung.
- Artefakt: stdlib/std/result.lyr, stdlib-tests/tests/result_tests.lyr
- Abgestimmt mit: new-features (try-Ausdruck, typed throws), pattern-lambda (Ok/Err-Patterns)

### [stdlib-redesign] Prototyp (b): Iterator-Terminatoren als Default-Methoden + 5 neue Adapter
- Kategorie: prototype
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): stdlib/std/iter.lyr — count/any/all/none/find/position/fold<A>/reduce/first/last/nth/forEach/toArray als Methoden; Adapter skipWhile/stepBy/inspect/dedupBy/zipWith
- Extern (Vergleich): Rust Iterator-Trait (alle Terminatoren Default-Methoden), Kotlin Sequence-Extensions, C# LINQ
- Beschreibung: Additiv; freie Funktionen bleiben (Deprecated bis 5.0). enumerate/chunks bleiben frei (Monomorphisierungsgrenze).
- Artefakt: stdlib/std/iter.lyr, stdlib-tests/tests/iter_tests.lyr
- Abgestimmt mit: pattern-lambda (Trailing-Lambda/`it`), new-features (Overloading-Frage für sum)

### [stdlib-redesign] Prototyp (c): Container-Paket (List.of/slice/map/filter, Map.keys()/entries()/getOrInsert, Map-Verdichtung)
- Kategorie: prototype
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): stdlib/std/collections.lyr; Map.set bei Tombstone-Last: resize(gleiche Kapazität) statt Verdopplung (collections.lyr:519)
- Extern (Vergleich): Kotlin listOf/getOrPut, Rust vec!/entry().or_insert_with, Python setdefault, .NET AddRange/TryGetValue
- Beschreibung: Additiv. Map/Set-Iteratoren werden Methoden → kein Fremdzugriff auf private Felder mehr (Vorbereitung Member-Sichtbarkeit).
- Artefakt: stdlib/std/collections.lyr, stdlib-tests/tests/collections_tests.lyr
- Abgestimmt mit: new-features (Feldsichtbarkeit)

### [stdlib-redesign] Prototyp (d): Bugfixes parseInt-Überlauf, powInt, trim-Konsistenz, formatHex/formatInt, absInt(int.min)
- Kategorie: bug
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): string.lyr:426-455 (parseInt/parseIntRadix), math.lyr:124-140 (powInt), string.lyr:189-212 (trimStart/trimEnd/isBlank), fmt.lyr:33/98, random.lyr:66-77 (absInt(int.min))
- Extern (Vergleich): Rust `parse::<i64>()` → Err(PosOverflow), `checked_pow`, `trim_start` = Unicode White_Space; Go `strconv.ParseInt` → ErrRange
- Beschreibung: parseInt → null bei Überlauf (Grenzprüfung vor der Multiplikation), powInt → null bei Überlauf, trimStart/trimEnd/isBlank über Unicode-Whitespace, formatHex/formatInt-Konsistenz, Random ohne absInt(int.min).
- Artefakt: stdlib/std/{string,math,fmt,random}.lyr + Tests
- Abgestimmt mit: -

### [stdlib-redesign] Prototyp (e): std.hash (sha256/sha1/crc32/hashCombine) + std.test-Erweiterungen (assertClose/assertThrows/assertNull/assertNotEq/fail)
- Kategorie: prototype
- Priorität: MEDIUM
- Status: in-progress
- Intern (Lyric): stdlib/std/hash.lyr (neu, Natives in NativeRegistry.cs), stdlib/std/test.lyr, stdlib/std/core.lyr (combineHash als Synthese-Anker)
- Extern (Vergleich): Python hashlib, Go crypto/sha256 + hash/crc32, .NET SHA256.HashData, Rust std::hash::Hasher
- Beschreibung: Additiv. assertThrows mit `fn() -> void throws Throwable`, bis typed throws mit Typparameter da ist (new-features F).
- Artefakt: stdlib/std/hash.lyr, stdlib/std/test.lyr, stdlib-tests/tests/{hash,test}_tests.lyr
- Abgestimmt mit: new-features (combineHash-Anker, typed throws)

### [macro-abi] Ist-Stand Metaprogrammierung: Attribute sind Daten, Native-Roots sind die einzige Erweiterungsschnittstelle
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): docs/guide/15-attributes.md (Attribute = Structs mit Marker-Interfaces OnFunction/OnType/OnModule, Argumente nur Literale/benannte Literale/Unit-Varianten, „describes; does nothing“, einzige compilergelesene: @Deprecated); src/Lyric.Frontend/Sema/AttributeValues.cs:40 („NOT constant folding, and deliberately not“); src/Lyric.Frontend/Ir/Lowering/HostTypes.cs:20 („@host would be clearer, but attributes are post-v1“); docs/guide/16-building.md: build.lyr darf Quelltext schreiben (`writeText("src/version.lyr", …)`) — ein Source-Generator-Modell (v) EXISTIERT bereits im Rohzustand. Generics per Monomorphisierung (InstanceTable), keine Reflection, kein comptime, keine Feldaufzählung.
- Extern (Vergleich): Rust (#[derive] + proc-macros), Zig (comptime), Swift (Macros 5.9), C# (Source Generators), Kotlin (KSP) — Lyric liegt bei „−−“ (usability-summary Matrix).
- Beschreibung: Drei Hebel liegen bereits im Compiler: (1) Attribute als typisierte Metadaten, die ein Host liest; (2) build.lyr als vollwertiges Programm vor dem Build; (3) die VM in C# (Lyric.Embedding kann Lyric-Code während eines Builds ausführen — lyrbuild tut das). Was fehlt: jede Form, mit der ein Lyric-Programm Code für sich selbst erzeugt oder Werte zur Compile-Zeit berechnet (Guide 15: `let LIMIT = 1 + 2` ist als Attributwert unzulässig, „there is no constant folding“).
- Artefakt: design/macros.md (Abschnitt 1)
- Abgestimmt mit: new-features (Synthese: Swift-Modell ohne derive/@Derive; Attribute bleiben inert)

### [macro-abi] Ist-Stand ABI: drei Wege in den Host, alle host-seitig verdrahtet
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Vm/NativeRegistry.cs:159 `Bind` bindet Import-Zeilen per Name+Signatur; :176 Capability-Bound je Import (`CapabilityTable.RequiredForImport`); src/Lyric.Embedding/LangVm.cs:150 `RegisterFunction` (Delegate → synthetisches `host`-Modul), :200 `RegisterNative` (Native-Root-Deklaration + Delegate), :85 `RegisterType<T>` (opaker Host-Typ, Host-Tag 0x47 im Konstantenpool §13); src/Lyric.Embedding/HostFunction.cs:118 Marshalling nur Skalare/string/Host-Typ, DynamicInvoke; Marshal.cs:98-122 (Bug-Hunt: FromLyric liefert für Objekt/Array/Optional still 0, Convert.ChangeType verlustbehaftet); src/Lyric.Core/Capabilities.cs:23 `HostAccess = 1<<3` ist seit 1.0 für `std.dotnet` RESERVIERT („host access through reflection“) — das Modul existiert nicht; Spec §4.5/§13 Tabelle nennt Bit 3 „reserved“. stdlib/std/process.lyr = Umweg über Kindprozesse.
- Extern (Vergleich): C# P/Invoke (DllImport/LibraryImport), Rust extern "C"/bindgen, Zig @cImport, Go cgo, Java FFM/jextract, Python ctypes/cffi, Lua C-API, Wasm Component Model/WIT.
- Beschreibung: Aus Lyric heraus ist heute KEIN fremder Code erreichbar — nur der Host kann Lyric etwas anbieten (Richtung Host→Skript). Ein eigenständiges Programm (`lyric run`, lyrpack) hat keinerlei Weg zu libc/.NET außer `std.process` (Prozessstart, Textparsen). Die Architektur ist aber vorbereitet: Import-Zeilen sind symbolisch (Name+Signatur, §13 „forward-open“), die Bindung passiert im Loader, das Capability-Bit existiert. Eine .NET-ABI braucht daher KEINEN neuen Opcode und KEINE Formatänderung: eine Import-Zeile mit Namensschema `dotnet:<Typ>::<Methode>` + Reflection-Bindung im Loader + Bit 3.
- Artefakt: design/abi.md (Abschnitt 1)
- Abgestimmt mit: stdlib-redesign (Capability-Namen camelCase `hostAccess`/`ffiAccess`, `uint8[]` als Bytepuffer, `throws HostError` statt Result)

### [pattern-lambda] Pattern-Defekte verifiziert (Basis dc32100c) — 12 Repros
- Kategorie: bug
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:943 (Tupel mit Varianten → ICE), :2588 (verschachtelte Variante → IR0001), :966 (Tupel mit Literal → IR0001), :2489/:2574 (`Dial(_) | Hangup` und `A(x) | B(x)` → IR0001), :2594 (`Rect { w = 0, h }` → ICE), :2412 (Binding über Struct aliast: druckt 99), :2288 (`n => n, null => -1` über ?int → Verifier-ICE „unreachable from entry“); Sema/TypeChecker.cs:4440 (Literal-/Range-Pattern über int8 → Verifier „i8 vs i64“), :4288 (`Some(v)` über `Opt<?int>` nicht erschöpfend; `(a, b)` über `(?int, int)` nicht erschöpfend); Parsing/Parser.Patterns.cs:219 (`x if (x > 0) =>` als Lambda gelesen → SEM0004/SEM0045). Repros: scratchpad/team3/pl-probes/p1…p12.
- Extern (Vergleich): Rust/Swift — alle zwölf Formen sind dort Standard.
- Beschreibung: Alle im Bug-Hunt genannten Defekte reproduziert, plus zwei neue (p11 Binding-Arm vor null-Arm; p12 Binding in Tupel über Optional). Ursache ist die Architektur: EmitPatternBranch testet nur die oberste Ebene, BindPattern/BindOne binden getrennt und nur Namen. Fix: ein rekursiver Test-und-Bind-Compiler (siehe nächster Eintrag).
- Artefakt: scratchpad/team3/pl-probes/
- Abgestimmt mit: new-features (Dateigrenzen), stdlib-redesign (Prioritäten)

### [pattern-lambda] Pattern-Lowering-Umbau: rekursiver Test-und-Bind-Compiler (Backtracking-Automat)
- Kategorie: implementation
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): FunctionLowerer.cs LowerMatch/EmitPatternBranch/BindPattern/BindPatternFields/BindOne/BindTupleElements → eine Routine `LowerPattern(pattern, value, type, onFail, assumeMatch)`, die pro Knoten Test und Bindung gemeinsam emittiert und rekursiv in Tupel-/Varianten-/Struct-Felder absteigt; Or-Alternativen binden auf die Slots der ersten Alternative (Sema-Symbole nach Name abgebildet); letzte Arm-Ebene ohne Guard nutzt `assumeMatch` (Exhaustiveness bewiesen) und entfällt Tests, außer innerhalb von Or-Alternativen.
  ```lyr
  match ((s, ev)) { (Idle, Dial(h)) => …, (Connecting { attempt }, Timeout) if attempt < 3 => …, _ => s }
  match (e) { Neg(Neg(x)) => …, Add(Lit(0), r) => …, A(x) | B(x) => x }
  ```
- Extern (Vergleich): Rust (rustc_mir_build: Decision-Tree mit Kandidaten-Backtracking), OCaml (Maranget-Matrix-Compilation, optimal), Swift (SILGen: Spalten-Matrix), Scala (Backtracking-Automat, "Matching Objects with Patterns"). Entscheidung: Backtracking (Arm-Reihenfolge = Testreihenfolge, wie §7.6 fordert) statt Entscheidungsbaum — keine Code-Duplikation, deterministische Guard-Reihenfolge, Diagnosen bleiben am Pattern.
- Beschreibung: Semantik-Entscheidungen (Spec §7.6): (1) Struct-Feldmuster DÜRFEN testen (`P { x = 0, y }`), da der Fehlerpfad existiert; (2) ein Binding in VERSCHACHTELTER Position über `?T` bindet `?T` (irrefutabel, Rust-Modell `Some(v)`), nur die oberste Ebene eines match/if-let narrowt `?T`→`T`; (3) Struct-Bindung kopiert. Breaking: nein (nur bisher abgelehnte/abstürzende Programme werden gültig; Ausnahme: Tupel-Binding über Optional-Element hatte falschen Typ). Tests: Lyric.Tests.Sema/PatternTests, Lyric.Tests.Vm/MatchTests (neue Klasse NestedPatternTests), Lyric.Tests.Ir Golden enums.ir.
- Artefakt: Branch worktree-agent-a1c2eb789de86ba9d, design/patterns.md, examples/patterns/
- Abgestimmt mit: new-features (fasst LowerMatch/BindOne nicht an), stdlib-redesign (Result<?int,E>-Patterns)
### [new-features] f-Strings rendern Display-Typen — Prototyp
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:1399 (CheckDisplayHole), src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:4265 — Branch worktree-agent-a4962b919be70e622, Commit "sema: an f-string hole renders a Display value"
  ```lyr
  struct P :: [Display] { x: int, fn show(): string { return f"P{this.x}"; } }
  println(f"p = {p}, c = {Color.Blue}, took {Duration.ofMillis(1500)}");   // p.show() implizit
  fn tag<T :: [Display]>(v: T): string { return f"<{v}>"; }                 // über die Constraint
  ```
- Extern (Vergleich): Rust `format!("{p}")` → Display oder Compile-Fehler (kein Fallback); Python/Kotlin/C#/Swift rendern alles (`<object at …>`, `null`). Lyric folgt Rust ohne Spec-Weiterreichung: `{p:N2}` auf Display ist ein Fehler.
- Beschreibung: §6.6 „no implicit Display call“ gestrichen; Loch mit Display-Typ IST `value.show()` (Struct/Class/Enum, Typparameter mit Constraint, Interface-Wert per vtable). Optional/Array/Tupel werden jetzt in der Sema refused (vorher Compiler-Absturz im Lowering). Minor, additiv. Nebenbefund: `println(d)` mit `d: Display` scheitert an SEM0028 (Satisfies kennt Interface-Werte nicht) — das Loch akzeptiert ihn; Fix in Satisfies empfohlen. Tests: 7 Sema + 4 Vm, Sema 773/773, Vm 1457/1457 grün.
- Artefakt: design/fstring-display.md (mit Spec-Diff §6.6/Appendix A), design/examples/fstring-display.lyr, docs/guide/02 angepasst
- Abgestimmt mit: stdlib-redesign (liefert Display für Container, braucht dafür bedingte Konformanz), pattern-lambda (keine Berührung)

### [new-features] Interface-Wert erfüllt seine eigene Constraint nicht
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:3205 (Satisfies) — `let d: Display = p; println(d);` → LYR-SEM0028 "type 'Display' does not satisfy constraint 'Display'"
- Extern (Vergleich): Swift/Rust: ein existenzieller Wert (`any Display`, `dyn Display`) erfüllt die Constraint des Protokolls trivial (Rust: `impl Display for dyn Display` via Blanket).
- Beschreibung: `Satisfies` behandelt `NamedRef{Kind: Interface}` nicht; ein Interface-Wert sollte jede Constraint erfüllen, die sein Interface ist oder zu seinen Parents gehört (`Conformance.WithParents`). Der f-String-Prototyp hat diese Regel lokal (CheckDisplayHole); sie gehört nach Satisfies, damit println und das Loch dieselbe Antwort geben. Kein Breaking.
- Artefakt: design/fstring-display.md (Abschnitt Wechselwirkungen)
- Abgestimmt mit: stdlib-redesign

### [lyriclings] ICE: match-Ausdruck, dessen Arme alle Block-Arme mit `return` sind
- Kategorie: bug
- Priorität: HIGH
- Status: proposed
- Intern (Lyric): src/Lyric.Frontend (FunctionLowerer.LowerExprOrVoid, "match expression produced no value") — Repro:
```lyr
import std.io.console { println };
enum V { A, B }
fn f(v: V): bool {
    return match (v) {
        A => { println("a"); return false; },
        B => { println("b"); return true; },
    };
}
fn main(): int { return if (f(V.B)) 0 else 1; }
```
  Sema akzeptiert (jeder Block-Arm returnt, §7.6 / Guide 06: "a block arm must return or throw"), das Lowering wirft `InternalCompilationException: lowering: match expression produced no value` → Exit 134 mit .NET-Stacktrace statt Diagnose. Erwartet: entweder kompilieren (der `return match` liefert nie einen Wert, Typ `never`) oder eine saubere Diagnose. Gefunden beim Schreiben des lyriclings-Runners; Workaround: match als Statement.
- Extern (Vergleich): Rust — `return match v { A => { return false } B => { return true } }` ist gültig (Typ `!`).
- Beschreibung: Gehört zur `never`-in-Ausdruck-Familie aus dem Bughunt (P1-16: `never` in if-Ausdruck/`??`), hier die match-Variante. Nicht breaking.
- Artefakt: lyriclings-Worktree proto/probes/r01.lyr
- Abgestimmt mit: -

### [lyriclings] `std.os.args()` liefert die Argumente der VM, nicht die des Programms
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): stdlib/std/os.lyr:24-28 — Doku sagt "`main(args)` receives them, but a function deeper in the program does not; here they are available". Tatsächlich liefert `args()` unter `lyric run prog.lyr -- a b` das Array `["<…>/lyrvm.dll", "run", "/tmp/prog-….lyrbc", "--", "a", "b"]`, während `main(args: string[])` nur `["a", "b"]` bekommt. Repro:
```lyr
import std.io.console { println };
import std.os { args };
fn main(): int { for (a in args()) { println(a); } return 0; }
```
  Ein Runner, der `args()` an `lyric run` weiterreicht, startet sich damit rekursiv selbst (so gefunden). Erwartet: dieselben Argumente wie `main(args)`.
- Extern (Vergleich): Rust `std::env::args()` liefert argv[0] + Programm-Argumente, dokumentiert; Python `sys.argv` ebenso. Hier widerspricht die Doku dem Verhalten UND es kommen Toolchain-Interna heraus.
- Beschreibung: Entweder Doku anpassen (argv der VM) oder `args()` auf die Programm-Argumente hinter `--` reduzieren. Nicht breaking, wenn nur die Doku korrigiert wird; sonst Verhaltensänderung.
- Artefakt: lyriclings-Worktree proto/proto.lyr (erste Fassung)
- Abgestimmt mit: -

### [stdlib-redesign] BUG (behoben, 1 Zeile): Methoden eines GENERISCHEN Enums werden nie gelowert — auch ohne optionales Typargument
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3827 — der Fall „Receiver ist GenericInstance“ prüfte nur `Kind is Class or Struct`; ein Enum fiel in den Fallback `TryResolveFunction` → `LYR-IR0001 call to 'isOk' (external or bodiless)`. Repro (worktree probes/enum2.lyr):
  ```lyr
  pub enum Opt<T> { Some(T), None; pub fn isSome(): bool { return match (this) { Some(_) => true, None => false }; } }
  Opt<int>.Some(3).isSome()   // vor dem Fix: IR0001, nach dem Fix: true
  ```
  Fix: `or TypeSymbolKind.Enum` in der when-Klausel; `LowerGenericMethodCall` funktioniert danach unverändert (Result<T,E>-Methoden isOk/ok/err/unwrapOr/unwrapOrElse/expect laufen, 12 Tests). Prototyp 21 hatte den Fall nur für `Option<?int>` gesehen (FunctionLowerer.cs:3914 ist die Fallback-Zeile, nicht die Ursache).
- Extern (Vergleich): Rust/Swift — Methoden auf generischen Enums sind selbstverständlich (`Option<T>::is_some`).
- Beschreibung: Blockierte std.result vollständig. Verbleibend (NICHT behoben): eine Methode eines generischen Typs mit EIGENEM Typparameter (`fn map<U>(f): Result<U, E>`) → `LYR-IR0001 this type argument` (ReturnTypeOfInstanceMethod kennt U nicht) → map/mapErr/andThen/orElse sind freie Funktionen. Anforderung an new-features: generische Methoden auf generischen Typen (Rust `impl<T,E> Result<T,E> { fn map<U>(…) }`).
- Artefakt: src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs (Worktree agent-aa7b5e912a78e6f2d), probes/enum2.lyr, probes/result2.lyr
- Abgestimmt mit: new-features (gemeldet)

### [stdlib-redesign] Prototyp (a) std.result — DONE (178/178 stdlib-Tests grün)
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib/std/result.lyr (Result<T,E>: isOk/isErr/ok/err/unwrapOr/unwrapOrElse/expect als Methoden; map/mapErr/andThen/orElse/orThrow/fromOptional frei), umgestellt: `std.string.parseIntOrErr/parseIntRadixOrErr` (neu: ParseError :: Throwable + ParseErrorKind {Empty, InvalidDigit, Overflow}, Offset), `std.io.file.textOrErr`, `std.json.parseOrErr`, `std.encoding.hexDecodeOrErr`. Namenskonvention der Antwortform: `x` (?T) / `xOrThrow` (throws) / `xOrErr` (Result) — Suffix nennt die Antwortform, wie OrThrow.
  ```lyr
  let n = parseIntOrErr("12a4");   // Err(ParseError { kind = InvalidDigit, offset = 2 })
  let results = List<Result<int, string>>.empty(); … match (r) { Ok(v) => …, Err(e) => … }
  ```
- Extern (Vergleich): Rust `str::parse::<i64>()` → `Result<i64, ParseIntError { kind: IntErrorKind }}` — exakt dieselbe Form (Kind-Enum hinter Carrier); Swift `Result` + `try?`; Kotlin `Result<T>` nur mit Throwable als E (Lyric lässt E frei).
- Beschreibung: Additiv. `orThrow` geht nur mit `catch (e: Throwable)` (ExceptionAnalyzer.cs:218 substituiert E nicht). `Result<?T, E>` ungetestet (P0-3). Zugleich Bugfix parseInt/parseIntRadix-Überlauf → null (negativ akkumulierend, Grenztest vor der Multiplikation).
- Artefakt: stdlib/std/result.lyr, stdlib-tests/tests/result_tests.lyr (12 Tests)
- Abgestimmt mit: new-features (Anker-Form bestätigt), pattern-lambda (Ok/Err-Patterns)

### [macro-abi] Prototyp ABI Stufe 1: `extern "dotnet" fn … = "Type::Method";` läuft
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): Worktree agent-ab3c14434f8eda027, Branch worktree-agent-ab3c14434f8eda027, Commit „abi: extern "dotnet" binds a public static .NET method by reflection“. Dateien: src/Lyric.Frontend/AST/Declarations.cs (ExternSpec), Parsing/Parser.Declarations.cs (kontextuelles `extern`, nur wenn ein String folgt), Sema/TypeChecker.cs (CheckExtern: SEM0098 unbekannte ABI, SEM0099 Typ/Symbol/Generics/throws), Ir/Lowering/ModuleLowerer.cs + NameMangling.ForExtern (Import-Zeile `dotnet:System.Math::Cbrt`, hostAccess-Bit), src/Lyric.Core/Capabilities.cs (`dotnet:`-Präfix → HostAccess), src/Lyric.Vm/DotnetBinding.cs (Reflection-Bindung, Overload-Wahl über Wire-Typen, Marshalling int-Breiten/float/bool/char/string, Exception → Panik LYR-VM0016), NativeRegistry.Bind (Fallback), Formatter/Dumper/AstChildren, docs/Grammar.md, docs/guide/14-embedding.md, examples/ffi/dotnet.lyr, tests/Lyric.Tests.Vm/ExternDotnetTests.cs (13 Tests grün; Parsing 489, Sema 765, Formatting 180, Vm 1453, Embedding 222, Bytecode 184, Ir 175 grün).
  ```lyr
  extern "dotnet" fn cbrt(x: float): float = "System.Math::Cbrt";
  extern "dotnet" fn hostName(): string = "System.Net.Dns, System.Net.NameResolution::GetHostName";
  fn main(): int { println(f"{cbrt(27.0)} {hostName()}"); return 0; }
  ```
  Ausgabe: `cbrt(27) = 3.0000000000000004`, `tmp = /tmp/`, `host = YogaG10-OW`; `lyrvm run --grant none` → `error[LYR-CAP0001]: module requires capability 'hostAccess'`; Disassembly zeigt `capabilities: 0x8` und `import dotnet:System.Math::Cbrt(f64) -> f64`.
- Extern (Vergleich): C# `[DllImport]`/`[LibraryImport]` (Deklaration + Symbolname, Marshalling nach Signatur), Rust `extern "C" { fn … }` (ABI-String + bodylose Deklaration), Java FFM `Linker.downcallHandle(lookup.find("strlen"), FunctionDescriptor…)` (Signatur wählt die Bindung).
- Beschreibung: Kein neuer Opcode, keine Formatänderung (Import-Tabelle §13 ist symbolisch und „forward-open“), Bit 3 war seit 1.0 reserviert. Regeln: Symbol muss `Type::Method` sein; die Lyric-Signatur wählt genau eine public-static-Überladung (int=Int64, int32=Int32, float=Double, char=Char, string=String); zwei Kandidaten = Ladefehler mit Liste; `string` null → "" (nie Nullreferenz); Host-Registrierung unter demselben `dotnet:`-Namen gewinnt vor Reflection. Fehlt: Optional↔Nullable, Arrays↔Span, Structs, Instanzmethoden/Host-Objekte (Host-Tag existiert bereits), Exceptions→throws HostError, Delegates/Callbacks, `std.dotnet`-Modul, lyrpack-Bündelung von Assemblies, NativeAOT (Reflection auf trimmbaren Typen). Breaking: nein (Minor; `extern` kontextuell). Spec: §4.5 Tabelle Bit 3 „std.dotnet“ → „extern "dotnet"“, §13 Imports-Abschnitt (Namensschema), §3.1 Grammatik, Anhang A SEM0098/0099, VM0016.
- Artefakt: examples/ffi/dotnet.lyr, design/abi.md
- Abgestimmt mit: new-features (extern kontextuell — umgesetzt), stdlib-redesign (hostAccess-Name, throws HostError als Folgestufe)
### [new-features] throw als Ausdruck + never als Rückgabetyp — Prototyp
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.cs:163 (ThrowExpr als Präfix), Sema/TypeChecker.cs (CheckExpr ThrowExpr, UnifyArms/Unify never-Regel), Resolver/BuiltinTypes.cs ("never"), Ir/Lowering/FunctionLowerer.cs (Diverges/LowerDiverging; LowerIfExpr, LowerCoalesce, LowerArm) — Branch worktree-agent-a4962b919be70e622, Commit "lang: throw as an expression, and never as a return type"
  ```lyr
  fn need(o: ?int, key: string): int throws NotFound { return o ?? throw NotFound { key = key }; }
  fn code(c: Cmd): int throws NotFound { return match (c) { Cmd.Go => 1, _ => throw NotFound { key = "dial" } }; }
  fn fail(msg: string): never { panic("fatal: " + msg); }
  fn guard(o: ?int): int { if (o == null) { fail("none"); } return o; }   // never-Aufruf narrowt
  ```
- Extern (Vergleich): Kotlin `x ?: throw E()` + `fun fail(): Nothing` (Smart-Cast danach) — Lyric folgt diesem Modell; C# erlaubt throw-Ausdruck nur an 3 Stellen ohne Typ (Sonderfall, vermieden); TypeScript hat keinen throw-Ausdruck (Helper mit `never`); Rust `!`, Zig `noreturn`, Swift `Never`.
- Beschreibung: §2 ThrowExpr = 'throw' UnaryExpr; §3.1 `never` als Typ (nur Rückgabe/throws); §6.9 never-Arm trägt nichts bei; §7.3 never-Funktion muss divergieren, Aufruf zählt als Exit; §9.2 Site wie Statement; §9.4 anpassen (todo/unreachable → never, stdlib-redesign macht es). Schließt Bug P0-5 (`if (b) 1 else panic()` / `x ?? panic()` Lowering-Absturz). Minor, additiv. Tests 11 Sema + 6 Vm + 3 Parser + 1 Formatter; alle Suiten grün (Sema 784, Vm 1463, Parsing 468, Formatting 179, Ir 175).
- Artefakt: design/throw-expression.md (Spec-Diff §2/§3.1/§6.9/§7.3/§9.4), design/examples/throw-expression.lyr, docs/guide/10 Abschnitt neu
- Abgestimmt mit: pattern-lambda (never in Patterns bleibt bei ihnen, Unifikationsregel liegt jetzt in UnifyArms), stdlib-redesign (panic/todo/unreachable/test.fail → never)

### [pattern-lambda] Pattern-Lowering-Umbau — Status: done (Commit fc1aecb7)
- Kategorie: implementation
- Priorität: HIGH
- Status: done
- Intern (Lyric): Commit fc1aecb7 auf worktree-agent-a1c2eb789de86ba9d — FunctionLowerer.cs `LowerPattern` (rekursiv, Test+Bind), `LowerFieldPattern`, `UnwrapPresent`, `EmitVariantTest`, `BindLocal`; TypeChecker.cs `BindPattern(nested)`, `BindsWholeOptional`, `RebindToCanonical`, Literal-Adaption in CheckLiteralPattern/CheckRangePattern; Parser `_guardHead`; LowerVariantCall/LowerStructVariant lowern Payload gegen Feldtyp (fixt `Result<?int,E>.Ok(4)`-Verifier-Absturz, P0-3 aus dem Review). Alle 12 Repros grün; Tests: Lyric.Tests.Vm/NestedPatternTests (46), Lyric.Tests.Sema/PatternCompilerSemaTests (8), Ir-Golden enums.ir (ein leerer Block weniger). Suiten: Vm 1499, Ir 175, Sema 772, Parsing 464, Formatting 178, Bytecode 184 — alle grün.
- Extern (Vergleich): siehe vorheriger Eintrag.
- Beschreibung: Spec-Folgen (§7.6): (a) Feldmuster dürfen testen — der Satz "a field pattern only binds" entfällt; (b) neue Regel "ein Name in verschachtelter Position über ?T bindet ?T"; (c) Struct-Bindung kopiert (war Bug). Breaking: nein.
- Artefakt: design/patterns.md (folgt), examples/patterns/
- Abgestimmt mit: new-features, stdlib-redesign

### [pattern-lambda] if-let / while-let / let-else (Prototyp 02 → Implementierung)
- Kategorie: prototype
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): Grammatik §5: `Condition = Expr | 'let' Pattern '=' Expr` in IfStmt/WhileStmt; `LetPatternStmt = ('let'|'var') Pattern [':' TypeExpr] '=' Expr ['else' Block] ';'` (verallgemeinert DestructuringStmt: auch `let Point { x, y } = p;` und `let Circle(r) = s else { return 0; };`). AST: LetCondExpr, LetPatternStmt; Sema: Bindung in then-/Body-Scope, SEM0098 (else muss verlassen / refutables Pattern ohne else), SEM0099 (Warnung: Pattern kann nicht fehlschlagen); Lowering: Desugar über LowerPattern mit Fail-Ziel = else-/exit-Block.
  ```lyr
  while (let x = it.next()) { sum += x; }
  let Ok(v) = r else { return 1; };
  if (let Rect { w, h } = s) { println(f"{w}x{h}"); }
  ```
- Extern (Vergleich): Rust (`if let`, `while let`, `let … else` — identische Semantik, Rust verlangt divergierenden else-Block), Swift (`if let`/`guard let` nur für Optionals; `if case` für Enums — zwei Formen, Lyric nimmt eine), Kotlin (`?: return` Elvis — nur Optionals, keine Enum-Payloads).
- Beschreibung: Reihenfolge nach stdlib-redesign: while-let (Iterator-Protokoll `next(): ?T`) > let-else > if-let. Breaking: nein. Wechselwirkung: Labels (new-features) stehen VOR `while`, kein Konflikt.
- Artefakt: Branch worktree-agent-a1c2eb789de86ba9d, examples/patterns/if-let.lyr
- Abgestimmt mit: stdlib-redesign (Protokollform), new-features (Parser-Dateigrenzen)

### [stdlib-redesign] Prototyp (b) Iterator-Terminatoren + Adapter — DONE (Commit 09e97f13)
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib/std/iter.lyr — Default-Methoden fold<A>/reduce/count/first/last/nth/any/all/none/find/position/countWhere/forEach/toArray; Adapter skipWhile/stepBy/inspect/dedupBy/zipWith<B,R>; freie Terminatoren leiten weiter (bleiben bis 5.0). 8 neue Tests.
  ```lyr
  let n = xs.iter().filter((n: int) => n % 2 == 0).count();
  let s = over<int>([1, 5, 2, 6]).skipWhile((n: int) => n < 3).toArray();   // [5, 2, 6]
  ```
- Extern (Vergleich): Rust Iterator (fold/count/last/nth/skip_while/step_by/inspect/dedup via itertools), Kotlin Sequence, C# LINQ Aggregate/Count/Last/ElementAt.
- Beschreibung: Additiv; `@Deprecated` auf den freien Formen noch NICHT gesetzt (Warnung würde in stdlib-tests feuern, die beide Formen prüfen) → 4.6 mit Uhr "5.0". enumerate/chunks bleiben frei (Monomorphisierungsgrenze). sum/minValue bleiben frei (Constraint auf T).
- Artefakt: stdlib/std/iter.lyr, stdlib-tests/tests/iter_tests.lyr
- Abgestimmt mit: pattern-lambda

### [stdlib-redesign] Prototyp (c) Container-Paket + Map-Verdichtung — DONE (Commit 2dd73b36)
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib/std/collections.lyr — List.of/pushAll/pushAllFrom/slice/filter/forEach/removeWhere/toReversed, mapList (frei, weil generische Methode auf generischem Typ nicht lowerbar: `LYR-IR0001 this type argument`, collections.lyr:108 im Versuch), Map.keys()/values()/entries()/getOrInsert/update/fromEntries/capacity (Felder keys/values → keyColumn/valueColumn: Feld und Methode teilen den Namensraum, LYR-RES0001), Set.of/addAll/toArray/toList, Deque.toArray, frei: sortListByKey/sortArray/maxBy/minBy/slice/listRemove/groupBy. Verdichtung: Map.set/Set.add bei Schwellwert → resize(gleiche Kapazität), wenn live*8 <= cap*3, sonst Verdopplung. Test: 10000 set/remove → capacity ≤ 16.
- Extern (Vergleich): Kotlin listOf/getOrPut/groupBy, Rust entry().or_insert_with, Python setdefault, .NET AddRange/TryGetValue; Verdichtung wie CPython dict (rebuild bei Tombstone-Last) und Rust hashbrown (rehash_in_place).
- Beschreibung: Additiv; 9 neue Tests. Restliche Fremdfeld-Zugriffe: MapKey/Value/EntryIterator + SetIterator lesen `source.states/keyColumn/valueColumn/items` → braucht Modul-Sichtbarkeit (`pub(module)`) oder verschachtelte Typen, an new-features gemeldet.
- Artefakt: stdlib/std/collections.lyr, stdlib-tests/tests/collections_tests.lyr
- Abgestimmt mit: new-features (Feldsichtbarkeit)

### [stdlib-redesign] Prototyp (d) Bugfixes — DONE (Commits 5164fe2b, e-Commit)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): parseInt/parseIntRadix-Überlauf → null (string.lyr scanInt: negative Akkumulation, Grenztest vor jedem Schritt); powInt → null bei Überlauf (math.lyr via checkedMul; Quadrat nur, solange noch ein Faktor kommt); intMax/intMin + checkedAdd/Sub/Mul/Abs + saturatingAdd/Sub/Mul; isWhitespace = Unicode White_Space (25 Codepoints) → trimStart/trimEnd/isBlank konsistent mit trim; formatHex(-255)="-ff" (Wert mit Vorzeichen) vs formatInt(-255,"X") (Bits) dokumentiert + Test (fmt_tests.lyr neu); Random: nextBits = nextInt & 0x7FFF…, nextIntRange per Rejection Sampling (kein Modulo-Bias, kein absInt(int.min)), nextFloat aus den oberen 53 Bits, Random.fresh() aus secureRandom(8).
- Extern (Vergleich): Rust checked_*/saturating_*/i64::MAX, parse → PosOverflow; Go strconv.ErrRange, rand.Intn (Rejection); Python hex(-255)='-0xff' vs Rust {:x} Zweierkomplement — Lyric bietet beide, benannt.
- Beschreibung: Nicht-breaking, außer: parseInt("9999999999999999999999") liefert jetzt null statt Müll (Verhaltensänderung, aber Bugfix). 4 neue Testfunktionen + fmt_tests.
- Artefakt: stdlib/std/{math,string,fmt,random}.lyr, stdlib-tests/tests/{math,string,random,fmt}_tests.lyr
- Abgestimmt mit: -

### [stdlib-redesign] Prototyp (e) std.hash + Test-Familie + Anker — DONE
- Kategorie: prototype
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib/std/hash.lyr (sha256/sha1/md5/crc32 nativ, sha256Hex/sha1Hex/md5Hex, hashCombine/hashAll), src/Lyric.Vm/NativeRegistry.cs (4 Natives, CRC-32 tabellengetrieben), std.core.combineHash (Boost hash_combine 64-bit, Synthese-Anker für new-features E), std.core.OnMethod + NonExhaustive (Attribut-Anker), std.test: assertNotEq/assertNull/assertNotNull/assertClose/assertLess/assertContains/fail. assertThrows BLOCKIERT: Lambda kann keine throws-Klausel tragen (LYR-SEM0084) → new-features (F).
- Extern (Vergleich): Python hashlib/zlib.crc32, Go crypto/sha256 + hash/crc32, .NET SHA256.HashData (kein CRC in der BCL), Rust sha2-Crate (nicht std) — Lyric hat Digests in der stdlib wie Go/Python, nicht wie Rust.
- Beschreibung: Additiv. 208/208 stdlib-Tests grün. Vektoren: NIST "abc", CRC32("123456789")=0xCBF43926.
- Artefakt: stdlib/std/hash.lyr, stdlib/std/core.lyr, stdlib/std/test.lyr, stdlib-tests/tests/{hash,test}_tests.lyr
- Abgestimmt mit: new-features (combineHash-Anker, OnMethod/NonExhaustive)

### [stdlib-redesign] Design-Dokument design/stdlib-2.md — DONE (Commit 8b32fdad)
- Kategorie: design
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/stdlib-2.md (17 Abschnitte: Modulstruktur, Namenskonventionen, Antwortformen als §11 Punkt 5, Fehlermodell, Iterator, Container, Display, Zahlen, Strings, Zeit, Random/Hash/Pfade/Prozess/JSON/Bytes, Test, Task, Deprecation, Migration, Blocker-Tabelle, Vergleich + Fazit, Prototyp-Inventar)
- Extern (Vergleich): Tabelle pro Entscheidung über Rust/Go/Kotlin/Swift/Python/.NET (§16)
- Beschreibung: Kernentscheidungen: (1) throws bleibt Mechanismus, Result ist Wert (Regel 2 gewahrt); (2) Iterator.next(): ?T bleibt, kein Option<T>; (3) Antwortform im Namen x/xOrThrow/xOrErr; (4) 5.0-Bild: try-Ausdruck (`try?` → ?T, `try` → Result) macht die Zwillinge ableitbar → eine werfende Funktion pro Operation, lastErrorKind-Globalzustand durch struct-liefernde Natives ersetzt; (5) std.iter/collections bleiben getrennt; (6) Display: ein Interface + Nesting-Regel statt Debug; (7) SortedMap erst 5.x bei Bedarf; Zeitzonen extern (Rust-Modell).
- Artefakt: design/stdlib-2.md; deliverables/stdlib-redesign/ (Design, Diff gegen main, geänderte Dateien, Commit-Liste)
- Abgestimmt mit: new-features, pattern-lambda, macro-abi

### [stdlib-redesign] DECISION: `try e` (ohne catch) → Result<T, E> als dritte Form des try-Ausdrucks
- Kategorie: decision
- Priorität: HIGH
- Status: proposed
- Intern (Lyric): stdlib/std/io/file.lyr (17 OrThrow-Zwillinge), stdlib/std/result.lyr (4 OrErr-Brücken) — beide Familien werden überflüssig, wenn
  ```lyr
  let content = try? file.text(p) ?? "";              // ?string
  let r: Result<string, IoError> = try file.text(p);   // Wert-Form, E aus der throws-Klausel
  ```
- Extern (Vergleich): Swift `try?`/`Result { try f() }`, Kotlin `runCatching { }` — Swift hat genau diese Dreiteilung und keine Zwillinge.
- Beschreibung: Empfehlung an new-features (Ergänzung zu Prototyp 04b `try e catch (x: E) Expr`). Konsequenz 5.0: eine werfende Funktion pro Operation (`text(p): string throws IoError`), stille Formen bleiben nur, wo null die ganze Wahrheit ist (parseInt, Map.get, env). Migration mechanisch (`try?` davor). Konflikt: keiner bekannt; Regel 2 bleibt (Result ist Wert, Propagation bleibt throws).
- Artefakt: design/stdlib-2.md §3.3
- Abgestimmt mit: new-features (gesendet), pattern-lambda (informiert)

### [stdlib-redesign] Blocker-Tabelle für new-features / pattern-lambda (Endstand)
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/stdlib-2.md §15.2 — 12 Zeilen: generische Methode auf generischem Typ (FunctionLowerer.cs:3456, HÖCHSTE Priorität: Result.map/List.map als Methoden), Enum mit optionalem Typargument (IrVerifier.cs:291, TypeChecker.cs:4288), List<?T> (rawArrayAlloc-Native vs ??T), Display-Constraint/conditional conformance, throws E-Substitution (ExceptionAnalyzer.cs:218), werfende Lambdas (SEM0084 → assertThrows), ?T == ?T, Feld/Methoden-Namensraum (RES0001, per Umbenennung gelöst), Enum-Equatable/Synthese, @Deprecated auf Methoden (OnMethod-Anker), Constraint auf Interface-Default-Methode (sum), Elementtypwechsel in nicht-generischer Methode (enumerate).
- Extern (Vergleich): Rust `impl<T,E> Result<T,E> { fn map<U> }` / `impl<T: Display> Display for Vec<T>` / `pub(crate)`
- Beschreibung: Jede Zeile mit Umgehung im Prototyp. Behoben: Enum-Methoden-Lowering (1 Zeile, FunctionLowerer.cs:3827).
- Artefakt: design/stdlib-2.md §15
- Abgestimmt mit: new-features, pattern-lambda (Endstand gesendet)
### [new-features] Labels für break/continue — Prototyp
- Kategorie: prototype
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.Statements.cs (ParseLabeled, ParseOptionalLabel), Sema/SemaRules.cs (WalkLoopBody/ResolveLabel, SEM0098/0099/0100), Sema/Flow.cs (HasBreak mit Label), Ir/Lowering/FunctionLowerer.cs (TargetLoop), LoopScope.Label — Branch worktree-agent-a4962b919be70e622, Commit "lang: labels for break and continue"
  ```lyr
  outer: for (i in 0..grid.length) { for (j in 0..grid[i].length) { if (grid[i][j] == t) { found = (i, j); break outer; } } }
  rows: for (row in rows) { for (x in row) { if (x < 0) { continue rows; } partial += x; } sum += partial; }   // 8, nicht 11
  ```
- Extern (Vergleich): Go `outer: for … break outer` (identisch; Go: unbenutztes Label = Fehler, Lyric: Warnung); Rust `'outer:` (Lexer-Kollision mit Char-Literal), Kotlin `outer@` (kollidiert mit Attributen), Swift/Java wie Go, Zig `:blk` mit Wert (vermischt Block-Wert und Label — getrennt gehalten), C# nur goto.
- Beschreibung: §2 LabeledStmt = IDENTIFIER ':' (While|DoWhile|ForIn), Break/Continue [IDENTIFIER]; §7.2 Absatz; §7.3 Coverage: `break L` in verschachtelter Schleife beendet die äußere (Flow.HasBreak steigt für die markierte Form ab); §7.5 defers aller verlassenen Scopes innerste zuerst. Labels sind keine Symbole. Minor, additiv; 4 neue Codes (PAR0044, SEM0098 E, SEM0099 E, SEM0100 W). Tests 9 Sema + 3 Vm + 2 Parser + 1 Formatter.
- Artefakt: design/loop-labels.md (Spec-Diff §2/§7.2/§7.3/§7.5/Appendix A), design/examples/loop-labels.lyr, docs/guide/04 Absatz
- Abgestimmt mit: pattern-lambda (Parser-Stellen disjunkt zu if-let/while-let)

### [new-features] defer in einem if-Körper wird auf dem umschließenden Scope registriert
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1063,1075 (LowerIf lowerte `stmt.Then` mit LowerStatements statt LowerScope) — Branch worktree-agent-a4962b919be70e622, Commit "ir: a defer inside an if body belongs to that body"
  ```lyr
  for (i in 0..3) { defer println("body"); if (i == 1) { defer println("o-a"); break; } }
  // 4.4.1: "o-a body o-a body" — der defer des NICHT genommenen Zweigs (i = 0) lief;
  // for (i in 0..2) { if (true) { defer println("in"); } println("after"); }  →  4.4.1: "in" lief NIE
  ```
- Extern (Vergleich): Go `defer` ist funktionsweit, kennt das Problem nicht; Zig `defer` ist blockweit (wie Lyric §7.5) und läuft genau beim Blockende — Lyrics Spec sagt dasselbe, die Implementierung tat es für if-Körper nicht.
- Beschreibung: §7.5 „each EXECUTION of a defer statement schedules one run“ war für `if`-Then-Blöcke verletzt: (1) im umschließenden Scope MIT eigenen defers lief der Zweig-defer bei jedem Scope-Ende, ob der Zweig genommen wurde oder nicht; (2) im umschließenden Scope OHNE defers (LowerPlainScope emittiert nie) lief er nie. else-Blöcke, match-Arme, Schleifenkörper waren korrekt (LowerScope). Fix: eine Zeile pro Zweig + 4 Vm-Tests (DeferInBranchTests). Gefunden durch den Labels-Test „defers innermost first“. Kein Breaking (Bugfix), Patch-fähig.
- Artefakt: tests/Lyric.Tests.Vm/DeferInBranchTests.cs
- Abgestimmt mit: -

### [pattern-lambda] if-let / while-let / let-else — Status: done (Commit 81c4bbef)
- Kategorie: implementation
- Priorität: HIGH
- Status: done
- Intern (Lyric): Commit 81c4bbef — AST LetCondExpr/LetPatternStmt; Parser.Statements.cs ParseCondition/ParseLetPattern; TypeChecker CheckIfLet/BindLetCondition/CheckLetPattern (SEM0098/SEM0099); FlowAnalyzer, ExceptionAnalyzer (+ DestructuringStmt-Fall, schließt den VM0010-Bug aus dem Bug-Hunt), WarningAnalyzer, SemaRules, AstChildren/AstDumper/AstFormatter, LSP ScopeCompletion; Lowering LowerIfLet/LowerWhileLet/LowerLetPattern über LowerPattern; LoopScope(blocks, continueTarget). Tests: Vm/BindingConditionTests (19), Sema/LetPatternTests (15); Beispiel examples/patterns/binding-conditions.lyr. Suiten grün (Vm 1520, Sema 790, Ir 175, Parsing 469, Formatting 184, Lsp 279; Cli 276/277 — der eine Fehler ist InterruptTests.Sigint…, SIGINT-Timing, unabhängig).
- Extern (Vergleich): siehe design/patterns.md §3.1.
- Beschreibung: Spec §5 Grammatik (Condition, LetPatternStmt), §7.1, §7.4 („die Bindung ist der Beweis"), §7.7. Minor, additiv. Zusätzlich: Fix von stdlib-redesign übernommen (Methodenaufruf auf generischem Enum, FunctionLowerer.cs:3840 `or TypeSymbolKind.Enum`).
- Artefakt: design/patterns.md, examples/patterns/
- Abgestimmt mit: stdlib-redesign (Protokollform while-let, Result-Patterns), new-features (Parser-Grenzen, Labels vor while)

### [pattern-lambda] Lambda-Analyse: Stand der Inferenz (verifiziert)
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): TypeChecker.cs:2746-2794 (Phasen: Nicht-Lambda-Argumente binden T, Lambdas dann gegen substituierte Parameter; „phase C" bindet U aus dem Body), CheckLambda :4866. Verifiziert mit pl-probes/l1…l3: `apply((n) => n * 2, 21)`, `twice((n) => n * 3, 2)`, `fold(over(xs), 0, (acc, n) => acc + n)`, `over(xs).map((n) => n * 10)`, `over(xs).filter((n) => n % 2 == 0)` — alle OHNE Annotation und ohne `map<int>` kompilieren und laufen (4.4.1 + meine Commits). Rückgabetyp aus Block-Lambda-Returns funktioniert.
- Extern (Vergleich): Kotlin/Swift/Rust inferieren dasselbe; C# ebenso (seit 10 auch natürliche Typen).
- Beschreibung: Die Annahme „stdlib-Tests erzwingen `(n: int) =>`" trifft für Iterator-Methoden nicht zu; falls stdlib-redesign einen konkreten fehlschlagenden Fall hat (z.B. Überladung + Lambda: „A lambda passed as an argument does not take part in choosing"), bitte Repro schicken. Was FEHLT: Lambda ohne Klammern (`x => x * 2`), Trailing-Lambda, implizites `it`, Method-References als Werte (`string.length`), rekursive Lambdas, werfende Funktionstypen, generische Funktionen als Werte — siehe nächste Einträge.
- Artefakt: scratchpad/team3/pl-probes/l1_lambdas.lyr, l2_generic_lambda.lyr, l3_iter_methods.lyr
- Abgestimmt mit: stdlib-redesign

### [pattern-lambda] Closure-Kurzsyntax: `x => …`, Trailing-Lambda `xs.map { it * 2 }`, implizites `it`
- Kategorie: prototype
- Priorität: HIGH
- Status: in-progress
- Intern (Lyric): Parser.cs ParsePrimary/IsLambdaAhead/ParseLambda, Postfix-Schleife (Call/Member), Parser.cs:459 IsStructInitAhead (§6.8-Kollision `Name { … }`); TypeChecker.CheckLambda (Synthese des `it`-Parameters bei genau einem erwarteten Parameter). Grammatik-Skizze:
  ```
  Lambda        = ( '(' [ LambdaParam { ',' LambdaParam } ] ')' [ ':' TypeExpr ] | IDENTIFIER ) '=>' ( Expr | Block )
                | TrailingBlock .
  TrailingBlock = '{' ( Expr | { Statement } ) '}'      (* nur als letztes Argument hinter einem Call oder Member *)
  ```
  Kollision `Name { … }`: nach `Ident {` entscheidet ein Zwei-Token-Lookahead — `}` oder `IDENT =` → Struct-Initializer (§6.8), sonst Trailing-Lambda. `_` bleibt Wildcard (Scala-`_` verworfen); `$0` verworfen (Lexer-Änderung, kollidiert mit f-String-Löchern optisch); Rust `|x|` verworfen (drittes Klammerpaar, stdlib-redesign dagegen).
- Extern (Vergleich): Kotlin (`xs.map { it * 2 }`, `it` nur bei einem Parameter, trailing lambda immer; kein Struct-Literal → keine Kollision), Swift (`xs.map { $0 * 2 }`, trailing closure; Struct-Init braucht Klammern → keine Kollision), Scala (`xs.map(_ * 2)`, `_` kollidiert mit Wildcard-Pattern — in Lyric ausgeschlossen), C#/TS/JS (`x => x * 2` ohne Klammern bei einem Parameter), Rust (`|x| x * 2`).
- Beschreibung: Reihenfolge: (1) `x => expr` (Parser 10 Z., frei: `IDENT =>` ist heute Parsefehler); (2) Trailing-Lambda + `it` (Parser ~60, Sema ~30); Body `{ it * 2 }` = Ausdruck ohne `;` → nutzt new-features' ValueBlock-Tail, bis dahin: einzelner Ausdruck ohne `;` wird als Expr-Body gelesen. Breaking: nein. Wechselwirkung §6.8 wie beschrieben.
- Artefakt: design/lambdas.md, examples/lambdas/
- Abgestimmt mit: stdlib-redesign (Kotlin-Modell bevorzugt), new-features (ValueBlock)

### [macro-abi] Nützlichkeit Metaprogrammierung: zwei große Posten sind Synthese, der Rest ist Compile-Zeit-Auswertung
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): Derive: 72 Zeilen Handarbeit (Team-2-Prototyp 03 ist.lyr) vs. 38 mit Synthese; stdlib/std/core.lyr 27 + time.lyr 8 Handimplementierungen von equals/hash/show/compare; IoErrorKind-Namensmapping stdlib/std/io/error.lyr:38-45 (8 Zeilen für 6 Varianten); JsonValue/IoErrorKind/Wait nicht Equatable; JSON-Writer json.lyr:124-290 (170 Zeilen) — ToJson/FromJson für Nutzertypen 15–30 Zeilen pro Struct; Test-Registrierung: 0 Zeilen (@Test + src/Lyrtest/Program.cs:137 liest Attributzeilen); Format-Spec paniert zur Laufzeit (fmt.lyr:18-21); Lookup-Tabellen werden im <globals>-Initializer bei jedem Start berechnet; Attributwert `1 + 2` unzulässig (Guide 15); 64 OrThrow-Zwillinge (Boilerplate anderer Art, Result-Thema von stdlib-redesign).
- Extern (Vergleich): Rust #[derive]/serde 1 Zeile; Swift Codable 0 Zeilen; Zig comptime-Tabellen; Rust format!/sqlx::query! Compile-Zeit-Prüfung; Lombok @Builder.
- Beschreibung: Tabelle „heute/mit Makro“ für 10 Anwendungsfälle in design/macros.md §2. Fazit: allgemeines Makrosystem lohnt sich NICHT; es lohnt sich in Form X = Konformanz-Synthese (new-features, feldweise Tabelle, später ToJson/FromJson) + `comptime` (Tabellen, Prüfungen, Konstanten, Attributwerte) + Generatoren über build.lyr (FFI-Bindings, Enum→Display-Dateien). Builder-Bedarf gering (Struct-Initializer mit Defaults + Struct-Update ..base).
- Artefakt: design/macros.md §2
- Abgestimmt mit: new-features, stdlib-redesign

### [macro-abi] Sprachcharakter Makros: Werte und Dateien ja, Syntax nein — Vergleich über 11 Systeme
- Kategorie: comparison
- Priorität: HIGH
- Status: done
- Intern (Lyric): Prüfsteine aus Guide 15 („describes; does nothing“), Guide 18 (ein Formatter, eine Gestalt), Spec §4.5 (Sandbox), ArchitectureTests „lyrc executes nothing“.
- Extern (Vergleich): C-Präprozessor (Textersetzung), Rust macro_rules! (hygienisch, kein Code), Rust proc-macros (ungesandboxt im Compiler), Zig comptime (Werte statt Syntax), Swift Macros 5.9 (typisiert, Expansion sichtbar, sandboxed Plugin, langsam), Nim/Julia AST-Makros, Kotlin KSP, C# Source Generators/Analyzer, Scala 3 inline/quotes, Elixir quote/unquote, D mixins. Bewertet je Hygiene/Debugbarkeit/Tooling/Compile-Zeit/Sandbox/Lesbarkeit/Fehlermeldungen (Tabelle design/macros.md §3).
- Beschreibung: Zerstörend: alles, was Syntax erzeugt/ersetzt (zwei Sprachen für Formatter/LSP, Spec bräuchte Expansionsmodell, Konventionen unterlaufbar) und alles, was Code ungesandboxt im Compiler ausführt (Sandbox-Versprechen gilt beim Bauen nicht). Passend: nur-Werte (Zig), nur-Dateien (KSP/Generators/build.lyr), spec-fixierte Synthese (Swift). Lyric-Alleinstellungsmerkmal: comptime ist per Konstruktion sandboxed (Capability.None + Instruktionsbudget) und deterministisch (Spec-Numerik, gated Nichtdeterminismus).
- Artefakt: design/macros.md §3
- Abgestimmt mit: new-features (Attribute bleiben inert; codeerzeugende Attribute nur mit eigener Namensklasse)

### [macro-abi] Design-Entscheidung: kein Makro-Schlüsselwort — Synthese + `comptime` + Generatoren
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/macros.md §4. Stufe 1 Synthese (§5.1, new-features); Stufe 2 `comptime UnaryExpr` (kontextuell; §6 neuer Präfix; Sema SEM0100: nur Modulnamen, Literal-Typen; Pipeline: Hoist-Lowering → IComptimeRunner (Lyric.Core) → Werte-Lowering; Auswertungsmodul mit eigenen Bits aus den erreichbaren Imports, geladen mit Capability.None + Budget; CT0001 ohne Evaluator, CT0002 mit Ursache an der Site); Stufe 3 build.lyr-Generatoren + Lesemodul `std.build.source` (Deklarationen als Daten) + `lyrbind`.
- Extern (Vergleich): Zig comptime (Semantik), Swift Synthese (Stufe 1), C# Source Generators/KSP (Stufe 3).
- Beschreibung: Abgelehnt: C#-Compiler-Plugins über Embedding (ungesandboxt, hostabhängig), AST-Template-Makros (Spec-Expansionsmodell, zwei Ebenen für Tooling, kein ungedeckter Bedarf), reines Generator-Modell (Konstanten als Dateien). Breaking: nein (Minor); einziger Bruch: Bezeichner `comptime` direkt vor `(`. Architekturregel neu: „lyrc führt nichts mit einer Capability aus; das Frontend referenziert keine Runtime“ (Test The_frontend_references_no_runtime ersetzt Lyrc_ships_no_runtime) — ADR-würdig. Folgeschritte: comptime als Attributwert (schließt Guide-15-Lücke „no constant folding“), Arrays/Structs als Literal-Typen, Backtrace-Notes, std.random-Determinismus im Runner.
- Artefakt: design/macros.md §4
- Abgestimmt mit: new-features

### [macro-abi] Prototyp Makros: `comptime` läuft — Sites werden zu Literalen, Funktionen verschwinden aus dem Modul
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): Commit 44cc6606 auf worktree-agent-ab3c14434f8eda027. Neue Dateien src/Lyric.Core/ComptimeRunner.cs (IComptimeRunner, ComptimeOutcome), src/Lyric.Frontend/Ir/Lowering/ComptimeTable.cs, src/Lyric.Vm/VmComptimeRunner.cs; geändert Parser.cs (ParsePrefix/BeginsExpression), TypeChecker.cs (CheckComptime), TypeResult.cs (ComptimeSites), ModuleLowerer.cs (Hoist: `<comptime:i>` als einzige Wurzeln, Bits aus verbleibenden Imports), FunctionLowerer.cs (LowerComptime → Const), SourceCompiler.cs (EvaluateComptime, CT0001/CT0002), Lyrc.csproj (+Lyric.Vm), Lyrc/Lyrbuild/LangVm setzen VmComptimeRunner; docs/Grammar.md, docs/guide/16-building.md; examples/macros/comptime.lyr; tests/Lyric.Tests.Vm/ComptimeTests.cs (12 grün). Gesamtsuite: alle Projekte grün außer Cli/InterruptTests.Sigint (setsid/kill im WSL-Sandbox, unabhängig von den Änderungen).
  ```lyr
  let FIB_90 = comptime fib(90);
  let SPEC = comptime checkedSpec(":08.3f");   // Panik → error[LYR-CT0002] an dieser Zeile
  fn main(): int { println(f"{FIB_90} {comptime (60 * 60 * 24 * 365)}"); return 0; }
  ```
  Ausgabe: `fib 90: 2880067194370816120`, `inline: 31536000`; Disassembly: `const i64 2880067194370816120`, `fib`/`powersOfTwo` nicht mehr im Modul. `comptime exists("/etc/hostname")` → `LYR-CT0002 … needs a capability … 'fileAccess'`; Endlosschleife → „did not finish within the compile-time budget“; Lokal/Lambda → LYR-SEM0100.
- Extern (Vergleich): Zig `comptime fib(90)` (Compiler-Interpreter, `@embedFile` erlaubt Dateizugriff — Lyric verbietet ihn); Rust `const fn` (eingeschränkte Sprache statt Sandbox); C++ `constexpr`/`consteval`.
- Beschreibung: Fehlend: nicht-skalare Werte, Attributwerte, Backtrace-Notes, Caching, LSP-Hover. Spec: §6 (Präfix), §4.5 (Auswertungsmodul ohne Grant), Anhang A (SEM0100, CT0001/0002). Breaking nein.
- Artefakt: examples/macros/comptime.lyr, design/macros.md §5
- Abgestimmt mit: new-features (kontextuelle Wörter), stdlib-redesign (std.random-Determinismus als Anforderung gemeldet)

### [macro-abi] Nützlichkeit ABI: sechs Anwendungsfälle, fünf davon heute unmöglich oder nur über Kindprozesse
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib/std/process.lyr (Umweg), src/Lyric.Vm/NativeRegistry.cs (2 700 Zeilen fest verdrahtet — nur Runtime-Autoren können Natives hinzufügen), Guide 14 (Embedding deckt „Lyric als Skriptsprache“ und die Export-Richtung über Call<T>), fehlende stdlib-Bereiche laut stdlib-review (Hash, Kompression, HTTP-Client, TLS, Regex) sind im BCL vorhanden.
- Extern (Vergleich): C# P/Invoke, Rust bindgen, Zig @cImport, Go cgo, Java FFM, Python ctypes, Lua FFI, Wasm Component Model.
- Beschreibung: Tabelle in design/abi.md §2: Systembibliotheken (nein/Prozess-Umweg), GUI (nein), .NET-Ökosystem (nur über C#-Host, pro Methode eine Registrierung), Skriptsprache im Host (ja, gut), Export-Richtung (nur untypisiert per Embedding), Hotspots (JIT deckt Arithmetik, keine Bibliotheken). Fazit: lohnt sich, .NET zuerst (kein Opcode, kein Format, keine Marshalling-Maschinerie, sofort BCL/NuGet für eigenständige Programme), C-ABI danach als P/Invoke-Spezialfall derselben Deklarationsform.
- Artefakt: design/abi.md §2
- Abgestimmt mit: stdlib-redesign

### [macro-abi] Sprachcharakter ABI: Capabilities bleiben die Grenze — Vergleich über 12 FFI-Systeme
- Kategorie: comparison
- Priorität: HIGH
- Status: done
- Intern (Lyric): Spec §4.5 (Bits nur hinzufügen), §13 („forward-open“ Imports, Host-Tag 0x47), Marshal.cs (Convert.ChangeType verlustbehaftet = Gegenbeispiel), CONTRIBUTING Regel 2 (GC einziger Speichermechanismus → FFI-Speicher ist host-owned, außerhalb der Sprache).
- Extern (Vergleich): C# DllImport/LibraryImport/function pointers, Rust extern+bindgen+unsafe, Zig @cImport, Go cgo (Warnung Coroutinen×Threads), Swift C/C++-Interop, Kotlin/Native cinterop, Python ctypes/cffi, Java FFM (Arena/Lifetime-Modell als Vorbild für CBuf/CPtr), Lua C-API/LuaJIT FFI, Wasm Component Model/WIT (Marshalling als Tabelle = Spec-Vorbild). Bewertet je Sicherheit/Ergonomie/Tooling/Portabilität/Laufzeitkosten/GC-Coroutinen (Tabelle design/abi.md §3).
- Beschreibung: Vereinbarung mit den vier Versprechen: (1) `hostAccess` (Bit 3, existiert) für "dotnet", neues Bit 5 `ffiAccess` für "C"; mit ffiAccess sind Abstürze in fremdem Code dokumentiert erlaubt, hostAccess bleibt abbruchfrei (Exceptions gefangen); (2) keine Nullreferenzen: string null → "", Referenzen nur als ?T oder opaker Handle; (3) breitenexaktes Marshalling, nie Convert.ChangeType; (4) Structs per Kopie geflattet, Klassen als Handle, Arrays per Kopie (uint8[] als Bytepuffer — stdlib-redesign); (5) CPtr<T> opak ohne Arithmetik, Zugriff nur in `unsafe { }` über std.ffi-Funktionen, Ownership als Konvention (Attribut-Vokabular, keine Semantik).
- Artefakt: design/abi.md §3
- Abgestimmt mit: stdlib-redesign (Capability-Namen camelCase, uint8[]-Garantie, throws HostError statt Result)

### [macro-abi] Design-Entscheidung ABI: vier Stufen — extern "dotnet" → extern "C" + std.ffi + unsafe → Export → lyrbind
- Kategorie: design
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/abi.md §4. Stufe 1 (prototypisiert): `extern "dotnet" fn f(…): T = "Type::Method";`, Import-Zeile `dotnet:<symbol>`, Bit 3, Reflection-Binder mit exakter Überladungswahl, Marshalling-Tabelle pro Lyric-Typ (Skalare/bool/char/string umgesetzt; ?T↔Nullable/null, T[]↔Kopie, Struct↔geflattet/Out-Buffer, Klasse↔Host-Handle 0x47, Enum↔Tag-Name, Callback↔Delegate mit Reentranz-Guard+Budget, Exception↔throws HostError geplant), Host-Registrierung gewinnt vor Reflection; Stufe 2: `extern "C" fn strlen(s: CStr): uint = "libc:strlen";`, NativeLibrary.Load mit RID-Suche, std.ffi (CInt/CSize/CStr/CBuf/CPtr<T>/CFn), `unsafe { }` als einzige Stelle für Zeigerzugriff, Bit 5 ffiAccess, LibraryImport-artige Stubs (AOT); Stufe 3: `pub extern fn` → typisiertes Delegate im Embedding / UnmanagedCallersOnly im Stub, Reachability-Wurzel; Stufe 4: lyrbind aus Assembly (Reflection) und C-Headern (ClangSharp) nach gen/, aus build.lyr aufrufbar.
- Extern (Vergleich): C# [DllImport]-Modell (Deklaration + Symbol + Signatur), Rust unsafe-Block, Java FFM Arena, WIT-Tabelle.
- Beschreibung: Bytecode: KEIN neuer Opcode, Imports-Namensschema als Konvention in §13, Capability-Tabelle Bit 3 belegt/Bit 5 neu; Reader unverändert. Spec: §3.1 ExternDecl, §4.5, §7 unsafe (Stufe 2), §11 std.ffi/HostError, Anhang A SEM0098/0099, VM0016. lyrpack: fremde Assemblies neben die Executable oder Footer v2 mit Anhang; NativeAOT-Stub braucht Trimmer-Wurzeln aus den dotnet:-Imports (lyrpack liest dafür das Modul). Breaking: Stufe 1 nein (Minor, kontextuelles extern, Format 4.0 bleibt), Stufe 2 Minor (Bit nur hinzugefügt), `unsafe` kontextuell möglich. Offene Punkte: host:<assembly>-Feingranularität als Host-Filter statt Bit; Coroutinen: Native-Call blockiert die VM (heute schon), Callbacks aus fremden Threads verboten.
- Artefakt: design/abi.md §4, examples/ffi/dotnet.lyr
- Abgestimmt mit: new-features (extern kontextuell), stdlib-redesign (std.ffi-Namen, HostError-Muster)

### [lyriclings] Meilenstein: Runner fertig, 21 Kapitel / 90 Übungen, audit + verify grün
- Kategorie: exercise
- Priorität: HIGH
- Status: done
- Intern (Lyric): lyriclings/lyriclings.lyr (Runner: watch/next/run/hint/list/verify/audit, std.process + std.task + std.json), lyriclings/exercises/NN_kapitel/*.lyr, lyriclings/solutions/, lyriclings/exercises.json
- Extern (Vergleich): rustlings/ziglings — `// I AM NOT DONE`-Marker, Hint pro Übung, verify in Reihenfolge
- Beschreibung: 90 Übungen in 21 Kapiteln (hello 2, variables 3, numbers 5, strings 4, functions 4, control flow 5, arrays/tuples 4, structs 3, classes 2, enums 4, matching 6, optionals 6, interfaces 7, generics 4, extend 3, errors 5, coroutines 3, modules 5 (Mehrdatei), attributes 2, stdlib 9, quiz 4). Jede Übung erzeugt im gebrochenen Zustand genau den in exercises.json notierten Code (68 Diagnosen, 13 Panics, 9 falsche Ausgaben); `audit`: 90/90 ok, `verify --solutions`: 90/90. Runner ist reines Lyric — kein Bash-Fallback nötig; einzige Einschränkung: `watch` pollt den Dateiinhalt (kein mtime in std.io.file).
- Artefakt: Worktree .claude/worktrees/agent-ac0107771533da8b3, Branch worktree-agent-ac0107771533da8b3; Kopie deliverables/lyriclings/
- Abgestimmt mit: -

### [lyriclings] Verwirrend für Lernende: `??=` auf ein Feld ist IR0001, auf eine Variable ok
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): FunctionLowerer (IR0001 "short-circuit or coalescing assignment") — Repro:
```lyr
class Profile { nickname: ?string = null }
fn main(): int { let p = Profile { }; p.nickname ??= "anonymous"; return 0; }
```
  → `error[LYR-IR0001]: short-circuit or coalescing assignment … this compiler version cannot lower it yet`. Guide 09 stellt `a ??= b` als normalen Operator vor; Appendix A.5 nennt unter IR0001 nur `&&=`/`||=`. Die Übung optionals4 musste auf eine lokale Variable umgebaut werden.
- Extern (Vergleich): Kotlin `p.nick = p.nick ?: "x"`, C# `p.Nick ??= "x"` — funktioniert auf Feldern.
- Beschreibung: Entweder lowern (ein Feldziel wird einmal ausgewertet) oder in §6.5/Appendix A dokumentieren. Nicht breaking.
- Artefakt: lyriclings/exercises/12_optionals/optionals4.lyr (Umgehung)
- Abgestimmt mit: -

### [lyriclings] Verwirrend: Sema-Fehler, die als IR0001 "cannot lower it yet" erscheinen
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): drei beim Übungsbau getroffene Fälle, alle mit Note "this compiler version cannot lower it yet", obwohl das Programm ungültig ist:
```lyr
struct P { x: int, y: int }
let p = P { x = 1 };            // IR0001: initializer omits field 'y' — gehört zu SEM0015-Familie
println(f"{p}");                 // IR0001: interpolating a non-scalar value — Guide 02 sagt "refused"
let s = "hi"; println(f"{s.length}");   // IR0001: member access '.length' on 'string' — s.length() wäre richtig
xs.length  (List<int>)           // IR0001: 'List' has no field 'length'
```
  Ein Lernender liest "Compiler kann es noch nicht" und sucht den Fehler beim Compiler statt im Programm. Deckt sich mit Bughunt P3-24; hier die Belege aus Lernenden-Sicht. Für Übungen daher unbenutzbar (structs: "fehlendes Pflichtfeld" wäre eine gute Übung gewesen).
- Extern (Vergleich): Rust E0063 "missing field `y` in initializer of `P`"; Swift "value of type 'String' has no member 'length'".
- Beschreibung: Eigene SEM-Codes (fehlendes Feld, Nicht-Skalar in f-String, Methode ohne Klammern / Feld statt Methode) statt IR0001. Nicht breaking (neue Codes).
- Artefakt: proto/probes (p46, p04, u01, u15)
- Abgestimmt mit: -

### [lyriclings] Verwirrend: `mut fn` wird auf Klassen nicht erzwungen, auf Structs schon
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): SemaRules (Schreibzugriff auf `this` in Nicht-`mut`-Methode) — Repro:
```lyr
class Button { clicks: int = 0, fn click(): void { this.clicks += 1; } }   // kompiliert, läuft, zählt 2
struct Button2 { clicks: int = 0, fn click(): void { this.clicks += 1; } } // SEM0019
```
  Guide 05: "A method that writes to `this` is marked `mut`" — ohne Unterscheidung. Für Lernende ist die Regel damit nicht lernbar (eine geplante Übung "classes: mut fn vergessen" war nicht baubar). Entweder erzwingen (breaking für Code, der es weglässt — Deprecation-Uhr) oder Guide/Spec präzisieren, dass `mut` bei Klassen nur Dokumentation ist.
- Extern (Vergleich): Swift `mutating` nur bei Structs (Klassen brauchen es nicht) — dort ist das dokumentierte Regel; Lyric sagt es nirgends.
- Beschreibung: Spec §3.4/§7.1 sagen nichts Explizites; Interface-Matching prüft `mut` exakt (§5.1). Wechselwirkung mit Member-Sichtbarkeit (P2-3 der Usability-Review).
- Artefakt: proto/probes (s04, t02, t03)
- Abgestimmt mit: -

### [stdlib-redesign] Prototyp (c) Nachtrag: arrayOf/arrayFilled ohne Native — DONE (Commit 2656bcb9)
- Kategorie: prototype
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib/std/collections.lyr — `arrayOf<T>(n, f: fn(int) -> T): T[]`, `arrayFilled<T>(n, x): T[]` über List + toArray (eine Kopie, kein VM-Umbau; Prototyp 18 Form A ohne das Native)
  ```lyr
  let squares = arrayOf<int>(4, (i: int) => i * i);   // [0, 1, 4, 9]
  ```
- Extern (Vergleich): Kotlin `Array(n) { i -> … }`, Scala `Array.tabulate`
- Beschreibung: Additiv; `rawArrayAlloc` bleibt die 5.0-Option für List<?T>. 209/209 stdlib-Tests.
- Artefakt: stdlib/std/collections.lyr, stdlib-tests/tests/collections_tests.lyr
- Abgestimmt mit: new-features (??T-Entscheidung gegen nestbar)

### [stdlib-redesign] Befund: Lambda-Parametertyp-Inferenz funktioniert bereits auf 4.4.1 — kein Blocker
- Kategorie: analysis
- Priorität: LOW
- Status: done
- Intern (Lyric): probes/infer.lyr (Worktree agent-aa7b5e912a78e6f2d): `over(xs).filter((n) => n % 2 == 0).count()`, `.map((n) => n * 10).fold(0, (acc, n) => acc + n)` laufen ohne Annotation und ohne `map<int>`. Die Annotationen in stdlib-tests sind Gewohnheit aus den Alt-Tests; Guide 13 nutzt jetzt die annotationsfreie Form.
- Extern (Vergleich): Kotlin/Swift/Rust — gleiche Inferenz aus dem erwarteten Funktionstyp
- Beschreibung: Korrigiert meine Nachricht an pattern-lambda (Inferenz war als Wunsch formuliert). Offen bleibt nur der überladene Aufruf, bei dem allein das Lambda die Kandidaten trennt (§7.3).
- Artefakt: probes/infer.lyr
- Abgestimmt mit: pattern-lambda

### [stdlib-redesign] Testergebnisse Endstand + ein nicht zuordenbarer Fehlschlag (Cli InterruptTests)
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib-tests 209/209; Ir 175/175, Sema 770/770, Vm 1453/1453, Formatting 190/190, DocGen 201/201 (Ratchet 554→685, Site 23→25, Snapshot neu), Lsp 281/281, Embedding 222/222 (Guide-13-Snippets neu geprüft), Cli 276/277 — FAIL `InterruptTests.Sigint_wakes_the_parked_task_and_the_program_drains` („did not drain within 10s of SIGINT“, stderr leer). Meine Änderungen berühren weder std.task noch die Interrupt-Natives (NativeRegistry-Diff: nur +47 Zeilen std.hash; FunctionLowerer: 1 Zeile Enum-Fall); der Test sendet SIGINT per `setsid` an eine Prozessgruppe — mit hoher Wahrscheinlichkeit Sandbox/WSL-Signalzustellung. Nicht auf main gegengeprüft (Worktree-Isolation).
- Extern (Vergleich): -
- Beschreibung: Bitte Team-Lead/main: den Test einmal auf main in derselben Umgebung laufen lassen; wenn er dort ebenfalls fällt, ist es Umgebung, sonst gehört er mir.
- Artefakt: probes/test-cli.log
- Abgestimmt mit: -

### [macro-abi] Abstimmung comptime-Determinismus: secureRandom per Import-Name abgelehnt, kein neues Capability-Bit
- Kategorie: decision
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): Commit f9757d36 auf worktree-agent-ab3c14434f8eda027: src/Lyric.Vm/VmComptimeRunner.cs `NonDeterministic = ["std.random.secureRandom"]` — der Runner prüft die Import-Zeilen des Auswertungsmoduls vor dem Laden und meldet LYR-CT0002 „reaches 'std.random.secureRandom', whose result differs from run to run“; `Random.seeded(n)` bleibt comptime-fähig. Test tests/Lyric.Tests.Vm/ComptimeTests.cs `A_draw_that_cannot_be_repeated_is_refused_while_a_seeded_one_evaluates` (13/13 grün). design/macros.md §4 Determinismus-Absatz und design/abi.md (uint8[]-§11-Zusage, Nativ/extern-Aufteilung) aktualisiert.
- Extern (Vergleich): Zig verbietet in comptime keine Zufallsquelle explizit (es gibt keine ohne OS-Aufruf, und OS-Aufrufe sind in comptime nicht ausführbar); Rust `const fn` kann keine Randomquelle aufrufen, weil `const fn` keine Natives erreicht.
- Beschreibung: stdlib-redesign (design/stdlib-2.md §10) hält fest, dass `secureRandom` und `Random.fresh()` die beiden nicht-deterministischen Züge des capability-freien Moduls sind; statt eines Bits, das jede Runtime tragen müsste, lehnt der Compile-Zeit-Evaluator den symbolischen Import-Namen ab (§11 Punkt 3: Native-Namen sind Vertrag). Damit ist ein `comptime`-Literal auf jeder Maschine gleich. Spec: Absatz in §4.5/§6 „compile-time evaluation grants no capability and binds no non-repeatable native“. Breaking nein.
- Artefakt: design/macros.md §4, src/Lyric.Vm/VmComptimeRunner.cs
- Abgestimmt mit: stdlib-redesign (Vorschlag von ihnen), new-features (Synthese-Schema bestätigt, Format-Spec-Validierung in Sema übernommen, comptime-Attributwert bleibt mein Folgeschritt)

### [stdlib-redesign] BUG behoben: `std.os.args()` lieferte das argv der VM statt der Programmargumente (lyriclings-Fund)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Vm/NativeRegistry.cs (`std.os.args`: vorher `Environment.GetCommandLineArgs()` roh → `["…/lyrvm.dll", "run", "x.lyrbc", "--", "a", "b"]`; jetzt alles nach dem ersten `--`, wie lyrvm für `main(args)`; ohne Trenner leer). Probe: `lyric run probes/args.lyr -- a b` → `a,b` / `a,b`.
- Extern (Vergleich): Python `sys.argv[1:]`, Go `os.Args[1:]`, Rust `env::args().skip(1)` — überall die Programmargumente, nie die des Runners.
- Beschreibung: Doku in os.lyr:24-28 versprach „what main(args) receives“; Verhaltensänderung nur für Programme, die das falsche Verhalten kompensierten. Kein stdlib-Test (lyrtest übergibt keine Argumente), Probe im Worktree.
- Artefakt: probes/args.lyr
- Abgestimmt mit: lyriclings

### [stdlib-redesign] `std.io.file.modifiedMillis(path): ?int` — Änderungserkennung für Tooling (lyriclings-Wunsch)
- Kategorie: implementation
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib/std/io/file.lyr + NativeRegistry.cs (`LastWriteTimeUtc` → Epoch-ms, `null` bei fehlender Datei mit RecordIoNotFound wie `size`). `?int` statt `?Instant`, weil std.time osAccess kostet; `Instant.ofEpochMillis` konvertiert. Test in file_tests.lyr.
- Extern (Vergleich): Rust `fs::metadata(p)?.modified()`, Go `os.Stat(p).ModTime()`, Python `os.path.getmtime`
- Beschreibung: Additiv; erster Baustein von Minor 9 (`walk/removeAll/modifiedAt/writeLines`).
- Artefakt: stdlib/std/io/file.lyr, stdlib-tests/tests/file_tests.lyr
- Abgestimmt mit: lyriclings

### [stdlib-redesign] Diagnose-Loch (an new-features): Methodenname ohne Aufruf (`s.length`, `list.length`) fällt als IR0001 durch
- Kategorie: bug
- Priorität: MEDIUM
- Status: proposed
- Intern (Lyric): Sema akzeptiert den Member-Zugriff auf eine Methode ohne `()` und erst das Lowering lehnt ab („cannot lower it yet“). Erwartet: SEM-Fehler „`length` is a method — call it: `s.length()`“ (oder, sobald pattern-lambdas Method-References da sind, ein Funktionswert vom Typ `fn() -> int`, mit SEM0003 beim Versuch, ihn wie eine Zahl zu benutzen).
- Extern (Vergleich): Kotlin „function invocation expected“, Rust E0615 „attempted to take value of method“
- Beschreibung: Häufigster Anfängerfehler laut lyriclings; §12.1 reserviert IR0001 für gültiges Lyric.
- Artefakt: -
- Abgestimmt mit: lyriclings (Fund), new-features (weitergeleitet)
### [new-features] Value-Block: Tail-Expression im Block-Arm und Block-Lambda — Prototyp
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/AST/Statements.cs (TailExprStmt, Block.Tail), Parsing/Parser.Statements.cs (ParseBlock(valueBlock), ParseExprStmt/ParseThrow Tail), Parser.Patterns.cs:221, Parser.cs:677, Sema/TypeChecker.cs (CheckMatch Block-Arm, CheckLambda Block, _tailExpected), Ir/Lowering/FunctionLowerer.cs (TailSink, LowerTail, LowerArm, RunLambda, LowerReturn never-aware) — Branch worktree-agent-a4962b919be70e622, Commit "lang: a value block ends in a tail expression"
  ```lyr
  return match (e) {
      Event.Tick => { let n = count + 1; println(f"tick {n}"); State.Busy(n) },   // Tail = Wert des Arms
      Event.Stop => { return State.Closed; },                                       // return bleibt
  };
  let twice = (x: int) => { let y = x * 2; y + 1 };                                 // == return y + 1;
  ```
- Extern (Vergleich): Kotlin `when`-Zweig/Lambda: letzter Ausdruck ist der Wert (Lyric folgt dem: Tail nur in Wert-Blöcken); Rust: jeder Block ist Ausdruck, `;`-Falle (`()`); Swift 5.9 nur Ein-Ausdruck-Zweige; Zig `break :blk v`; C# switch-Ausdruck ohne Blöcke.
- Beschreibung: §2 ValueBlock, §6.9 Tail-Typ nimmt an Unifikation/Kontext teil, Block ohne Tail muss verlassen (SEM0033 mit neuem Hinweis), Tail im match-STATEMENT unterliegt der ExprStmt-Regel (SEM0022); §7.3 Tail = `return tail;` im Block-Lambda; §7.5 Tail vor den defers. Statement-Blöcke (if/while/Funktionskörper) bleiben wertlos, auch verschachtelt im ValueBlock. Nebeneffekt: DA-Analyse läuft jetzt durch Block-Arme von match-Ausdrücken (vorher übersprungen). Schließt den lyriclings-ICE „match expression produced no value“ im Lowering (LowerReturn prüft Diverges); der Sema-Teil (match ohne wertliefernden Arm = never) liegt bei pattern-lambda. Minor, additiv. Tests 14 Sema + 5 Vm + 3 Parser + 1 Formatter; Sema 808, Vm 1475, Parsing 471, Formatting 180, Ir 175, Lsp 279 grün; Cli 276/277 (Sigint_wakes_the_parked_task scheitert in dieser Sandbox unabhängig vom Branch — Signalzustellung).
- Artefakt: design/value-block.md (Spec-Diff §2/§6.9/§7.3/§7.5/Appendix A), design/examples/value-block.lyr, docs/guide/03 + 06
- Abgestimmt mit: pattern-lambda (LowerArm/ParseMatchArm gemeinsame Zeilen, Merge trivial), lyriclings (ICE-Befund)

### [pattern-lambda] Closure-Kurzsyntax, Trailing-Lambda, Parameter-/for-Destructuring — Status: done (Commit 8cd174b7)
- Kategorie: implementation
- Priorität: HIGH
- Status: done
- Intern (Lyric): Commit 8cd174b7 auf worktree-agent-a1c2eb789de86ba9d.
  ```lyr
  apply(x => x * 2, 21)                 // bare: ein Parameter ohne Klammern
  applyTo(41) { it + 1 }                // trailing lambda, impliziter Parameter 'it'
  let seven = run { 7 };                // Kontext ohne Parameter: 'it' entfällt
  each([1, 2, 3]) { sum = sum + it; }   // Statement-Body, kein ';' nach '}'
  over(xs).map { it * 2 }               // Ketten über Member
  fold(ps, 0, (acc, (a, b)) => acc + a * b)   // Parameter-Destructuring
  for ((k, v) in entries(m)) { … }      // Pattern im Schleifenkopf
  ```
  Dateien: AST/Expressions.cs (LambdaForm, LambdaParam.Pattern/.Implicit), AST/Statements.cs (ForInStmt.Pattern), AST/INamedDecl.cs (leerer NameSpan = unbenannt), Parser.cs (ParseBareLambda, ParseTrailingLambda, HoldsStatements, IsTrailingLambdaAhead, _inGuard, IsStructInitAhead-Body-Test), Parser.Statements.cs (ParseForIn mit Pattern, ExprStmt ohne ';' nach Trailing-Lambda), TypeChecker.cs (CheckLambda mit implizitem 'it' + Pattern-Parametern, BindIrrefutable, match ohne wertliefernde Arme → never), FlowAnalyzer/AstChildren/AstDumper/AstFormatter/ScopeCompletion, FunctionLowerer (RunLambda entpackt Pattern-Parameter, LowerForIn entpackt Element, LowerReturn für divergierenden Wert, _lambdaParameterCount).
  Tests: Lyric.Tests.Vm/LambdaFormTests (14). Suiten grün: Vm 1534, Sema 792, Ir 175, Parsing 471, Formatting 188, Lsp 279. Beispiele: examples/lambdas/shorthand.lyr, examples/lambdas/destructuring.lyr (laufen, Ausgabe im Kopf dokumentiert).
- Extern (Vergleich): Kotlin (`it`, trailing lambda, `(a, b) ->` destructuring), Swift (`$0`, trailing closure), Rust (`|x|`, `|(a,b)|`), C#/TS (`x => …`), Scala (`_` — für Lyric ausgeschlossen, kollidiert mit dem Wildcard-Pattern), Python (kein Lambda-Destructuring).
- Beschreibung: §6.8 bleibt unangetastet — die Struct-Initializer-Entscheidung fällt am Primary (`Name { }` / `Name { feld = … }`), und ein nackter Name am STATEMENT-Anfang bleibt bei §6.8, damit `Point { x = 1 };` der Fehler bleibt, der er ist. Guard-Kollision `x if y => …` über ein Parser-Flag gelöst, das an jedem Delimiter zurückgesetzt wird. Nur irrefutable Patterns in Kopf/Parameter (SEM0098 nennt `let … else` als Ausweg). Breaking: nein; eine Fehlermeldung ändert sich (Aufruf-Statement ohne ';' vor einem Block).
- Artefakt: design/lambdas.md, examples/lambdas/
- Abgestimmt mit: stdlib-redesign (Kotlin-Modell, `it` nur bei einem Parameter — geprüft: kein `it` in der stdlib), new-features (ValueBlock deckt später den Statement-Body mit Tail ab)

### [pattern-lambda] ICE behoben: `return match (…)` mit lauter verlassenden Armen (lyriclings-Fund)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): TypeChecker.cs (CheckExpr/MatchExpr: keine wertliefernden Arme → `LyrType.Never`), FunctionLowerer.cs (LowerExprOrVoid ohne Ergebnis-Slot für never, LowerReturn lowert einen divergierenden Wert für den Effekt und siegelt mit `unreachable`). Repro aus der Taskliste läuft jetzt (`b`, Exit 0).
- Extern (Vergleich): Rust — `return match v { A => { return false } … }` hat Typ `!` und ist gültig.
- Beschreibung: Teil der never-Familie; abgestimmt mit new-features, deren `Diverges(Expr)` genau `TypeOf(expr) is NeverType` prüft — nach dem Merge deckt ihr LowerArm/LowerTail dieselbe Stelle zusätzlich ab. Nicht breaking.
- Artefakt: Commit 8cd174b7, Test LambdaFormTests.A_match_expression_whose_arms_all_leave_is_never_and_compiles
- Abgestimmt mit: new-features, lyriclings (Finder)
### [new-features] try trägt zur Definite Assignment bei — Prototyp
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Sema/FlowAnalyzer.cs (TryStmt) — Branch worktree-agent-a4962b919be70e622, Commit "sema: a try whose every catch leaves assigns what its body assigned"
  ```lyr
  var opts: Options;
  try { opts = parse(args); } catch (e: Exception) { println(e.message()); return 2; }
  println(f"port {opts.port}");     // 4.4: LYR-SEM0018, 4.5: ok
  ```
- Extern (Vergleich): C# — genau diese DA-Regel (Roslyn: ein catch, der verlässt, trägt den try-Body hinaus); Kotlin löst es über den try-AUSDRUCK; Zig `catch |e| { return }` ebenso.
- Beschreibung: §7.7 „A try contributes nothing afterwards" wird zu „ein try, dessen catches ALLE verlassen (Flow.AlwaysExits: return/throw/break/continue/never-Aufruf), trägt bei, was sein Body an seinem Ende zugewiesen hat". Korrekt, weil eine Ausnahme mitten im Body nur in einen catch führt und jeder catch verlässt. Nur weniger SEM0018 — kein Programm ändert seine Bedeutung. 8 Zeilen, 4 Sema-Tests. Beseitigt beide bisherigen Umwege (Helfer mit ?T verliert den Grund; Rest-der-Funktion-im-try dehnt den Schutzbereich und verschluckt fremde Fehler).
- Artefakt: design/try-expression.md (Stufe a implementiert, Stufe b `try`-Ausdruck designt inkl. Option B `try e` → Result), design/examples/try-expression.lyr, docs/guide/10
- Abgestimmt mit: stdlib-redesign (try-Ausdruck als Brücke throws→Result; Option B für 5.0 vereinbart)

### [new-features] Konformanz-Synthese Equatable/Hashable/Ordered/Display — Design
- Kategorie: design
- Priorität: HIGH
- Status: done
- Intern (Lyric): Synthese-Stelle wäre src/Lyric.Frontend/Sema/TypeChecker.cs:714 (heute LYR-SEM0020)
  ```lyr
  struct Coord :: [Hashable<Coord>, Ordered<Coord>, Display] { x: int, y: int, name: string }
  // 4 Methoden, 0 Zeilen Körper; heute 30 Zeilen Handarbeit, und niemand prüft hash gegen equals
  ```
- Extern (Vergleich): Swift (Konformanz ohne Body → Synthese) = Empfehlung; Rust `#[derive]` (Attribut erzeugt Code — widerspricht §4.7; implizite Bounds als Falle); Kotlin `data class`/C# `record` (Alles-oder-nichts am Typ-Modifier); Go (strukturelles ==, keine Kontrolle); Python `@dataclass` (Laufzeit).
- Beschreibung: §5.1 neuer Abschnitt; Schema feldweise in Deklarationsreihenfolge, geschriebene Methode gewinnt, Enum: Tag dann Payload; Fehler AM FELD, nicht an der Konformanzliste; generische Typen schreiben die Constraint. Beseitigt drei stille Fehlerklassen (hash≠equals, neues Feld nicht nachgezogen, compare≠equals). Einziger Kandidat, der sich NICHT als Bibliothek nachbauen lässt (keine Reflexion). Minor. Aufwand: Sema mittel (~120 Z.), Lowering groß (~250 Z., AST-Synthese), VM 0. Das Schema ist bewusst als allgemeines Formular formuliert, damit macro-abis ToJson/FromJson als weitere Zeilen passen. Braucht `?T == ?T` für optionale Felder.
- Artefakt: design/conformance-synthesis.md, design/examples/conformance-synthesis.lyr
- Abgestimmt mit: stdlib-redesign (Anker combineHash; Wunschliste IoErrorKind/JsonValue/Wait/Result), macro-abi (Stufe 1 ihres Makro-Designs)

### [new-features] typed throws: throws E mit Typparameter, throws auf Funktionstypen, werfende Lambdas — Design
- Kategorie: design
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs (ThrownOf substituiert Typparameter nie — Bug), TypeChecker.cs:5312 (SEM0084 lehnt throws auf Funktionstypen ab)
  ```lyr
  fn evens<E>(input: Coroutine<int> throws E): Coroutine<int> throws E { … }
  fn mapInts<E>(xs: string[], f: fn(string) -> int throws E): int[] throws E { … }
  evens(numbers());          // E = never → kein try nötig
  evens(parsed(lines));      // E = ParseError
  ```
- Extern (Vergleich): Swift 6 `throws(E)` + `throws(Never)` = Empfehlung (kein rethrows: throws E deckt es ab und ist präziser); Zig inferierte Error-Sets (mächtig, aber `!T` sagt nichts über eine öffentliche API); Rust Result<T,E> (braucht Result als Anker); Java `<E extends Exception> throws E` ohne Substitution an der Aufrufstelle = genau Lyrics heutiger Bug.
- Beschreibung: Drei Stufen. St.1 Substitution an der Aufrufstelle (Bugfix, ~60 Z., sofort). St.2 Inferenz von E aus Werten, `throws never` ≡ keine Klausel (~80 Z.). St.3 werfende Funktionstypen + Lambda-Klausel-Inferenz (~160 Z.). Lowering NULL — Werfbarkeit hat keine Laufzeitrepräsentation; E ist ein PHANTOM-Parameter und darf nicht Teil des Instanzschlüssels sein (sonst verdoppelt jede Pipeline-Stufe ihren Code). Minor. Verhindert: toten catch, der später echte Fehler verschluckt; Sentinel statt Grund (§9.0). Offen: generische Interface-Member mit throws-Typparameter (Vorschlag: aus St.3 ausklammern).
- Artefakt: design/typed-throws.md, design/examples/typed-throws.lyr
- Abgestimmt mit: stdlib-redesign (assertThrows<E>, Result.orThrow, Iterator.next mit throws E), pattern-lambda (liefert das Lambda-Design, Sema-Umsetzung hängt hier)

### [new-features] Bedingte Konformanz extend<T :: [Display]> List<T> :: [Display] — Design
- Kategorie: design
- Priorität: HIGH
- Status: done
- Intern (Lyric): heute LYR-SEM0047 „extend target must be a plain named type in v1" (gemessen: sieben Folgefehler bei `extend List<T :: [Display]>`); Satisfies TypeChecker.cs:3205; ExtensionTable hängt an (TypeSymbol, Modul)
  ```lyr
  extend<T :: [Display]> List<T> :: [Display] { fn show(): string { … } }
  extend<T :: [Ordered<T>]> List<T> { fn max(): ?T { … } }     // bedingte Methoden fallen mit ab
  ```
- Extern (Vergleich): Rust `impl<T: Display> Display for Vec<T>` = Syntaxvorbild (Parameter VOR dem Ziel, sonst nicht parsebar); Swift `extension Array: … where Element: …`; Haskell `instance Show a => Show [a]`; Kotlin/C# (jeder Typ hat toString → `Foo@1a2b3c` im Log); Zig comptime-Duck-Typing (Fehler aus dem Bibliotheksinneren = Lyrics heutiger P0-Bug 8).
- Beschreibung: Typparameter vor dem Ziel; Parameter müssen im Ziel linear vorkommen; pro (Ziel, Interface, Belegung) genau ein Block (keine Spezialisierung); Orphan-Regel §5.5 unverändert; Constraint-Prüfung rekursiv mit Tiefenlimit. Lowering: ExtensionTable auf Argumentbelegung erweitern, Monomorphisierung erzeugt List<int>.show wie List<int>.push. Minor. Macht `println(xs)` möglich UND gibt den Fehler für `List<Socket>` AM AUFRUF mit Grund statt in console.lyr:51. Offene Frage 4: `?T :: [Display]` bedingt definierbar? Vorschlag NEIN (Dissens mit stdlib-redesign, s. decision-Eintrag).
- Artefakt: design/conditional-conformance.md, design/examples/conditional-conformance.lyr
- Abgestimmt mit: stdlib-redesign (Blocker für Display auf Containern; ihre Alternative „Synthese-Sonderregel pro Containertyp" abgelehnt)

### [new-features] ?T == ?T — Design
- Kategorie: design
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:2010-2033 (LYR-SEM0059); Lowering neben TryLowerNullTest
  ```lyr
  let a: ?int = parseInt("42");  let b: ?int = parseInt("x");
  a == b        // 4.4: SEM0059 — heute braucht es 4 Tests und zwei `!`
  a == 42       // gemischte Form: vorhanden UND gleich
  // a < b      // bleibt bewusst undefiniert
  ```
- Extern (Vergleich): Kotlin = Empfehlung (Gleichheit inkl. gemischter Form, KEINE Ordnung); Rust/Swift definieren zusätzlich `None < Some(_)` → stille Sortierfehler; C# „lifted <" liefert bei null false, sodass a<b und a>=b gleichzeitig falsch sind; SQL dreiwertig (berüchtigt).
- Beschreibung: §6.2 Absatz; null==null true, null==Wert false, sonst die Regel für T; narrowt NICHT (die Präsenzfrage bleibt `== null`); keine Ordnung. Lowering 3 Blöcke, kein neuer Opcode (~40 Z.), Sema ~30 Z. Minor. Voraussetzung für die Konformanz-Synthese (?T-Felder) und für assertEq/assertNull.
- Artefakt: design/optional-equality.md, design/examples/optional-equality.lyr
- Abgestimmt mit: stdlib-redesign (angefordert)

### [new-features] Member-Sichtbarkeit pub / pub(module) — Design
- Kategorie: design
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): Visibility.Module existiert für Top-Level (Resolver.cs:447), fehlt an Membern; std.collections hat 19 öffentliche Felder
  ```lyr
  pub class List<T> { items: T[], count: int, pub(module) version: int, pub fn length(): int { … } }
  ```
- Extern (Vergleich): Rust pub/pub(crate) = Empfehlung; Swift fünf Stufen inkl. fileprivate (Konstruktionsfehler: Datei statt Modul); Kotlin public als Default (bereut); Go Groß-/Kleinschreibung (Umbenennen ändert Sichtbarkeit); TypeScript private wirkungslos neben #field; Zig hat exakt Lyrics heutiges Problem.
- Beschreibung: Drei Stufen (privat als Default, pub(module), pub); Initializer außerhalb des Moduls nur mit öffentlichen Feldern — dieselbe Regel, die LYR-SEM0093 für opaque types schon kennt; Feld-Pattern nur über sichtbare Felder; Interface-Methode ist implizit pub, ein privates Member kann keine Konformanz erfüllen. Lowering NULL (nur Reachability-Wurzeln werden weniger). BREAKING → 5.0, mit Deprecation-Uhr ab 4.5 (Warnung) nach dem SEM0093-Muster. pub(module) ist kein Luxus: vier konkrete Stellen in collections.lyr lesen Fremdfelder.
- Artefakt: design/member-visibility.md, design/examples/member-visibility.lyr
- Abgestimmt mit: stdlib-redesign (fordert pub(module), hat die Feldklassifikation geliefert)

### [new-features] Roadmap 4.5 / 4.6 / 5.0 für Sprachfeatures
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/roadmap.md — 22 nummerierte Positionen mit Abhängigkeitsgraph
- Extern (Vergleich): —
- Beschreibung: 4.5 „Die Ausdrücke werden vollständig" (6 implementierte + 5 designte Positionen, alle additiv, Format 4.0); 4.6 „Generische Konformanz und Werfbarkeit" (bedingte Konformanz, typed throws St.3, Raw-Strings, named arguments); 5.0 „Was bricht" (Member-Sichtbarkeit, `try e`→Result, stdlib-Überladung, f-String-Formatsprache, Shadowing-Regel). Enthält die Liste der bewusst NICHT aufgenommenen Vorschläge mit Begründung (nestbare Optionals, Option<T> als Enum, `?`-Operator auf Result, never als allgemeiner Typ, Ordnung auf Optionals, allgemeiner Block-Ausdruck) und die Fremd-Voraussetzungen (generische Methode auf generischem Typ = HIGH-Bug, Enum-Instanzmethoden-Fix, Satisfies straffen, ??= auf Feldern, @Deprecated auf Membern).
- Artefakt: design/roadmap.md
- Abgestimmt mit: pattern-lambda, stdlib-redesign, macro-abi, lyriclings

### [new-features] Dissens: soll ?T als Display definierbar sein?
- Kategorie: decision
- Priorität: LOW
- Status: done
- Intern (Lyric): design/fstring-display.md (Optionals rendern nicht), design/conditional-conformance.md Frage 4
  ```lyr
  println(f"{opt}");        // mein Vorschlag: bleibt ein Fehler
  ```
- Extern (Vergleich): Kotlin/C# drucken `null` bzw. `""` — eine Abwesenheit verschwindet im Text; Rust: `Option<T>` ist NICHT Display (nur Debug) — meine Position.
- Beschreibung: stdlib-redesign möchte `?T :: [Display]` (bedingt), damit `assertEq` bei Ungleichheit Optionals drucken kann. Ich lehne ab: eine Abwesenheit soll narrowt oder mit `??` beantwortet werden, nicht still als Text erscheinen (§6.3/§7.4-Geist); Rust entscheidet genauso. EMPFEHLUNG: stdlib bietet `showOptional(o, ifNone)` als Funktion an — explizit an der Aufrufstelle. Nicht blockierend für beide Seiten; die Entscheidung gehört in §11, wenn die bedingte Konformanz kommt.
- Artefakt: design/conditional-conformance.md (Offene Frage 4)
- Abgestimmt mit: stdlib-redesign (Dissens dokumentiert, beide Positionen begründet)

### [pattern-lambda] Array-Patterns — Status: done (Commit 06325fe4)
- Kategorie: prototype
- Priorität: HIGH
- Status: done
- Intern (Lyric): AST/Patterns.cs (ArrayPattern, RestPattern), Parser.Patterns.cs (ParseArrayPattern, PAR0033 bei zwei Rests), Parser.Statements.cs (`let [a, b] = xs else …`), TypeChecker (BindArrayPattern, IsIrrefutable nur für `[..]`), FunctionLowerer (ArrayLen + Längentest == bzw. >=, LoadElem von vorn und von hinten, rekursiv in die Positionen), Formatter/Dumper/Children/FlowAnalyzer/LSP.
  ```lyr
  match (xs) { [] => 0, [x] => x, [0, y] => y, [a, b] => a*b, [first, ..] => first }
  match (xs) { [a, .., z] => z - a, [_] => 0, _ => -1 }
  match (ts) { [Word("add"), Number(n), End] => n, [Word(w), ..] => 0, _ => -1 }
  let [a, b] = xs else { return -1; };    if (let [a, .., z] = xs) { … }
  ```
  Tests: Vm/ArrayPatternTests (16). Beispiel: examples/patterns/array-patterns.lyr (läuft).
- Extern (Vergleich): Rust `[first, rest @ ..]` (Slices), Python `case [x, *rest]` (Kopie), JS/TS `[a, ...rest]` (irrefutabel), Scala `List(a, b)` / `x :: rest`, OCaml Listen — Swift hat KEINE Array-Patterns.
- Beschreibung: Benannter Rest (`[first, ..rest]`) bewusst NICHT implementiert: er müsste ein Array unbekannter Länge bauen, wofür es weder Slice-Opcode noch `rawArrayAlloc`-Native gibt; Parser+Sema nehmen ihn, das Lowering lehnt mit IR0001 ab (§12.1: gültiges Lyric, das diese Version nicht lowern kann) und nennt `slice`. Auflösung in 4.6 zusammen mit stdlib-redesigns `rawArrayAlloc`. Breaking: nein.
- Artefakt: design/patterns.md §3.3, examples/patterns/array-patterns.lyr
- Abgestimmt mit: stdlib-redesign (os.args/splitFirst/Tokenizer — alle ohne benannten Rest schreibbar)

### [pattern-lambda] Exhaustiveness-Diagnose mit Zeugen-Pattern — Status: done (Commit 06325fe4)
- Kategorie: implementation
- Priorität: HIGH
- Status: done
- Intern (Lyric): TypeChecker.cs MissingCases(nested) + MissingVariants + NamesVariant + WitnessOf + MissingArrayCases; SEM0050-Meldung „no arm matches 'X'".
  ```
  Opt<bool> mit Some(true), None          → no arm matches 'Some(false)'
  Res<Opt<int>, string> mit Ok(Some(_))   → no arm matches 'Ok(None)'
  Shape mit Circle(_), Empty              → no arm matches 'Rect { … }'
  int[] mit [], [_, _]                    → no arm matches '[_]'
  ```
  Tests: Sema/ExhaustivenessWitnessTests (14), davon 5 „muss schweigen"-Fälle.
- Extern (Vergleich): Rust (rustc_pattern_analysis, Zeuge + unreachable arm), OCaml (Warning 8 mit Zeuge), Swift (Fix-it „add missing case '.some(false)'"), Kotlin (`when` über sealed, ohne verschachtelte Zeugen), C# (CS8509 nennt einen Beispielwert).
- Beschreibung: Exakt, wo geantwortet wird — Variante mit EINEM Payload-Feld wird verfolgt, mit mehreren nur als Ganzes gemeldet; Arrays über Längenklassen (damit ist `[] | [x] | [a, ..]` erschöpfend). Offen für die volle Maranget-Matrix: Tupel aus Enums, mehrere Spalten einer Variante, Integer-Ranges über die Typbreite, unreachable-arm-Warnung (Design in design/patterns.md §3.6). Breaking: nein — erschöpfende Matches, die vorher fälschlich SEM0050 bekamen (Array-Längenklassen), kompilieren jetzt.
- Artefakt: design/patterns.md §3.6
- Abgestimmt mit: new-features (@NonExhaustive als Folgeschritt in derselben Routine)
### [new-features] Merge-Plan new-features × pattern-lambda (vier Berührungspunkte)
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): Branches worktree-agent-a4962b919be70e622 (8 Commits) und worktree-agent-a1c2eb789de86ba9d (4 Commits), beide auf dc32100c
  ```
  FunctionLowerer.LowerArm    → meine Diverges-Zeilen + TailSink gewinnen (bei ihnen unverändert)
  Parser.Patterns.ParseMatchArm → beides behalten: ihr _guardHead um den Guard, mein ParseBlock(valueBlock: true) für den Body
  FunctionLowerer.LowerIf     → ihr if-let nutzt bereits LowerScope; mein 869c360f fixt dieselbe Stelle im gewöhnlichen LowerIf
  FunctionLowerer.LowerReturn → BEIDE haben einen never-Zweig gleicher Bedeutung; einen davon behalten
  ```
- Extern (Vergleich): —
- Beschreibung: Abschließende Abstimmung nach beiden Endständen. Keine inhaltlichen Konflikte. Der lyriclings-ICE („match expression produced no value") ist nach dem Merge BEIDSEITIG geschlossen: ihre Sema gibt einem match ohne wertliefernden Arm den Typ never, mein LowerReturn lowert einen never-Wert für den Effekt. Aus ihrem design/lambdas.md §3.2 habe ich zwei Verfeinerungen in design/typed-throws.md übernommen: (1) `FnType` bekommt ein `Throws`-Feld wie `CoroutineOf`, damit Werfbarkeit Teil der strukturellen Typgleichheit ist; (2) ein Lambda OHNE Kontext schreibt seine Klausel (`(x: int): int throws Boom => …`) statt sie zu inferieren — Inferenz gilt nur nach innen. Dritter Punkt von ihnen: der ExceptionAnalyzer muss den TYP des Callees lesen, nicht nur sein Symbol, sonst trägt der Funktionstyp die Klausel und niemand prüft sie (Aufwand in der Schätzung ergänzt: +40 Z. in Stufe 3). @NonExhaustive ist bei ihnen designt, nicht implementiert → bleibt 4.6-Position.
- Artefakt: design/value-block.md (Merge-Notizen), design/typed-throws.md (Verfeinerungen), design/roadmap.md (pattern-lambda-Abschnitt auf Endstand)
- Abgestimmt mit: pattern-lambda (ihr Endstand eingearbeitet, keine offenen Rückfragen beidseitig)

### [pattern-lambda] ENDSTAND: 5 Commits, Designs, Beispiele, Deliverables
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): Branch worktree-agent-a1c2eb789de86ba9d, Arbeitsbaum sauber:
  fc1aecb7 Pattern-Compiler · 81c4bbef if-let/while-let/let-else · 8cd174b7 Closure-Kurzsyntax + Trailing-Lambda + Parameter-/for-Destructuring + never-match · 06325fe4 Array-Patterns + Zeugen-Diagnose · c9e1e24f design/.
  Tests grün: Vm 1551, Sema 807, Ir 175, Parsing 475, Formatting 190, Lsp 279, Bytecode 184, Resolver 22, Core 375, Lexing 391. (Cli 276/277 — InterruptTests.Sigint… scheitert branchunabhängig in dieser Sandbox.)
  Beispiele (laufen, Ausgabe im Dateikopf): examples/patterns/{state-machine,simplifier,binding-conditions,array-patterns}.lyr, examples/lambdas/{shorthand,destructuring}.lyr.
- Extern (Vergleich): pro Thema in design/patterns.md und design/lambdas.md, je gegen mindestens zwei von Rust, Swift, Kotlin, Scala, OCaml/Haskell, Zig, C#, TypeScript, Python.
- Beschreibung: Offene Designs ohne Prototyp: `@`-Bindungen (braucht die Lexer-/Spec-Entscheidung zu LEX0012), benannter Rest in Array-Patterns (braucht Slice-Opcode oder rawArrayAlloc), volle Maranget-Matrix + @NonExhaustive + unreachable-arm-Warnung, Method-References, werfende Funktionstypen (an new-features' typed throws), generische Funktion als Wert, `match` ohne Klammern (Formatter-Vertrag), Trailing-Lambda mit mehreren Parametern.
- Artefakt: deliverables/pattern-lambda/{design,examples}
- Abgestimmt mit: new-features (Merge-Plan, typed-throws-Design übernommen), stdlib-redesign (alle sechs Anforderungen geliefert oder designt), lyriclings (ICE geschlossen)
### [new-features] Struct-Initializer als Tail ohne Klammern — offene Frage beantwortet
- Kategorie: decision
- Priorität: LOW
- Status: done
- Intern (Lyric): design/value-block.md (offene Frage 3), pattern-lambdas src/Lyric.Frontend/Parsing/Parser.cs IsStructInitAhead (Branch worktree-agent-a1c2eb789de86ba9d, Commit 8cd174b7)
  ```lyr
  // SOLL nach der Regel: kein Klammerzwang mehr fuer den Tail
  Event.Reset => { println("reset"); Point { x = 0, y = 0 } },
  ```
- Extern (Vergleich): Rust verlangt in `if`/`match`-KOEPFEN Klammern um Struct-Literale (struct-literal-in-condition-Regel) und loest damit dieselbe Mehrdeutigkeit durch eine Schreibpflicht; pattern-lambdas Zwei-Token-Regel kommt ohne sie aus.
- Beschreibung: Ich hatte fuer einen Struct-Initializer als Tail Klammern verlangt, weil die Alternative ein Zwei-Token-Lookahead NACH einem balancierten `{ … }` gewesen waere. Braucht es nicht: `IsStructInitAhead` entscheidet an den ersten zwei Tokens hinter der oeffnenden Klammer (`{}` oder `{ name = …` = Initializer, sonst Block). Regel fuer den Tail: innerhalb eines ValueBlocks bleibt `_allowStructInit` auch am Statement-Anfang gesetzt; ein `Point { x = 1 }` dort ist entweder der Tail (wenn `}` folgt) oder ein Ausdrucksstatement ohne Wirkung (SEM0022) — ein bloszer Bezeichner mit folgendem Block ist heute schon genau das. §6.8 bleibt fuer gewoehnliche Statement-Bloecke unveraendert. NICHT im Prototyp umgesetzt; beides ist vorwaertskompatibel (wer heute klammert, klammert danach umsonst).
- Artefakt: design/value-block.md (Offene Frage 3, jetzt beantwortet), design/roadmap.md
- Abgestimmt mit: pattern-lambda (ihre Regel, mein Anwendungsfall)

### [new-features] Nach dem Merge entfaellt HoldsStatements (Vereinfachung)
- Kategorie: analysis
- Priorität: LOW
- Status: done
- Intern (Lyric): pattern-lambdas Parser.cs HoldsStatements (~799) und ParseTrailingLambda (~778) gegen meinen ValueBlock (Parser.Statements.ParseBlock(valueBlock: true))
  ```lyr
  xs.map { it * 2 }            // heute: Ein-Ausdruck-Body, selbst gelesen
  xs.map { let y = it; y + 1 } // heute: Statement-Body ueber ParseBlock()
  ```
- Extern (Vergleich): Kotlin — ein Trailing-Lambda-Body IST ein Block, dessen letzter Ausdruck der Wert ist; eine Unterscheidung zwischen „Ausdrucks-Body" und „Statement-Body" existiert dort gar nicht.
- Beschreibung: Ihre Trailing-Lambda unterscheidet heute per `HoldsStatements` (balancierter Scan nach einem `;` auf Ebene 1), ob der Klammerinhalt ein Statement-Block oder ein einzelner Ausdruck ist, und liest den Ausdrucksfall selbst. Mit dem ValueBlock entfaellt die Unterscheidung: `{ it * 2 }` ist ein ValueBlock mit einem Tail, `{ let y = …; y }` ebenso. `HoldsStatements` faellt ersatzlos weg — ein Helfer weniger und eine Stelle weniger, an der zwei Parser-Pfade dasselbe Klammerpaar verschieden lesen. Einziger Punkt, an dem sich die beiden Branches nach dem Merge gegenseitig verkleinern; gehoert in den Merge-Commit oder unmittelbar danach.
- Artefakt: design/value-block.md (Abschnitt „Eine Vereinfachung, die erst NACH dem Merge moeglich ist")
- Abgestimmt mit: pattern-lambda (von ihnen gefunden, von mir bestaetigt)

### [stdlib-redesign] BUG behoben: Argument wird nicht gegen die Substitution der Instanz gewidert (Folgefund der Gegenprobe mit pattern-lambda)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs, `LowerGenericMethodCall` — reichte keine `calleeSubstitution` an `MaterializeArguments`, also blieb ein Parameter, der `T` geschrieben steht, ein Name. Bei `T = ?int` lowerte ein `int`-Literal zum blanken Skalar; der Verifier fing den Store einen Schritt später IM AUFRUFER, ohne Zeile zum Hinzeigen. Minimaler Repro (probes/iso_enum.lyr):
  ```lyr
  enum Holder<T> { Full(T), Empty; pub fn or(fallback: T): T { … } }
  class Box<T> { value: T, pub fn or(fallback: T): T { … } }
  let h = Holder<?int>.Full(slot);  h.or(3)   // vorher: ir-verifier "store of i64 into ?i64"
  let b = Box<?int> { value = slot }; b.or(3) // ging schon — die Klasse erreichte den Pfad zuerst
  ```
  Fix: dieselbe Abbildung bauen, die der Pfad für generische Interface-Member längst baut. Test: tests/Lyric.Tests.Ir/LoweringTests.cs `An_argument_widens_to_what_the_instance_makes_of_its_parameter_type` (pinnt Klasse UND Enum). Suiten: Ir 176, Sema 770, Vm 1453, Bytecode 184, DocGen 201, stdlib-tests 211 — grün.
- Extern (Vergleich): Rust/Swift monomorphisieren die Parametertypen mit; ein Argument gegen den substituierten Typ zu prüfen ist dort keine Extrastufe.
- Beschreibung: Folgefund meines eigenen Fixes aus 15.1(a) — der Enum-Pfad wurde durch ihn erst erreichbar, und dabei fiel auf, dass der Pfad die Widerung nie hatte. Gefunden durch die von pattern-lambda erbetene Gegenprobe (meine stdlib + ihr Compiler).
- Artefakt: probes/iso_enum.lyr, probes/iso_c.lyr, probes/iso_g.lyr, Commit a8df8a5d
- Abgestimmt mit: pattern-lambda

### [stdlib-redesign] Gegenprobe Result<?T, E> gegen pattern-lambdas Branch — was trägt und wo die Grenze strukturell ist
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib/std/result.lyr, stdlib-tests/tests/result_optional_tests.lyr
  ```lyr
  let full = Result<?int, string>.Ok(slot(true));   // konstruiert
  full.isOk(); full.unwrapOr(0); failed.err(); map<?int, string, int>(full, f)   // alles trägt
  full.ok()                    // NICHT: ?T mit T = ?int ist ??int
  fromOptional<?int, E>(o, e)  // NICHT: der Parameter ?T ist ??int
  ```
- Extern (Vergleich): Rust `Option<Option<T>>` schachtelt, deshalb hat `Result::ok` dort keine Grenze; Kotlin kollabiert `T??` wie Lyric und hat dieselbe Lücke, benennt sie aber nirgends.
- Beschreibung: Die beiden Mitglieder, deren eigene Signatur `?T` schreibt, sind für optionale Payloads nicht instanziierbar — das ist die Bibliotheksoberfläche einer SPRACHREGEL, kein Bug und nichts, was jemand fixen müsste: `match` trennt `Ok(null)` von `Ok(v)`, `Result<?U, E>.Ok(x)` konstruiert. Beide Stellen dokumentieren es jetzt dort, wo ein Leser sie trifft. Zwei falsche Annahmen aus meinem ersten Bericht damit korrigiert: (1) `fromOptional<?int, string>` war NIE der saubere Repro, sondern per Signatur unmöglich; (2) der verbleibende Blocker ist nicht „Result<?T,E> allgemein“, sondern nur die Payload-BINDUNG im Pattern (pattern-lambda, auf ihrem Branch gebaut).
- Artefakt: stdlib-tests/tests/result_optional_tests.lyr, Commit 881accbd, design/stdlib-2.md §15.1/§15.2
- Abgestimmt mit: pattern-lambda

### [stdlib-redesign] Methodischer Hinweis für alle: lyrtest hat eine eigene Binary — nach einem Compiler-Fix mitbauen
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): `lyric test` ruft `src/Lyrtest/bin/.../lyrtest`, das eine EIGENE Kopie des Frontends lädt. Nach `dotnet build src/Lyric.Cli` allein läuft `lyric test` weiter mit dem ALTEN Compiler — ein gefixter Fehler erscheint dort unverändert, während `lyric run` und `lyrc build` derselben Quelle schon grün sind. Kostete mich vier Isolationsrunden gegen einen Phantom-Absturz.
- Extern (Vergleich): -
- Beschreibung: Wer einen Frontend-/Lowering-Fix testet, baut `src/Lyrtest` mit (oder gleich `Lyric.slnx`). Zweiter Fallstrick derselben Art: `lyrc check` stoppt VOR dem Lowering, prüft also keinen Verifier-Befund — für IR-Fragen `lyrc build` oder `lyric run` nehmen.
- Artefakt: probes/bisect_runner.py
- Abgestimmt mit: -
### [new-features] Preis der „?-nestet-nicht"-Regel gemessen — kein Argument für ??T
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): design/roadmap.md (Ablehnungsliste), stdlib-redesigns Gegenprobe an std.result (Branch worktree-agent-aa7b5e912a78e6f2d)
  ```lyr
  // TRAEGT: Result<?int, E> wird gebaut, gematcht, isOk/unwrapOr/err/map arbeiten
  // TRAEGT NICHT: jede Signatur, die ?T in Rueckgabeposition schreibt, ist fuer T = ?U ein ??U
  fn ok(): ?T          // fuer Result<?int, E> nicht instanziierbar
  fn fromOptional(o: ?T)
  ```
- Extern (Vergleich): Rust/Swift haben `Option<Option<T>>` und brauchen `flatten`; Kotlin/TypeScript kollabieren still (`T??` existiert nicht) und koennen „kein Wert" und „Wert ist keiner" nicht unterscheiden — dieselbe Grenze wie Lyric, nur unbenannt.
- Beschreibung: Bisher stand in meiner Roadmap die Behauptung „nestbare Optionals braucht niemand". Jetzt steht dort die Messung: `Result<?T, E>` ist NICHT allgemein blockiert; es fehlen genau die Methoden, die eine Abwesenheit als Rueckgabe verwenden. Das ist die Bibliotheksoberflaeche der Sprachregel und bleibt ein Argument FUER sie — wer nestet, haendigt dem Aufrufer zwei Abwesenheiten aus, die niemand auseinanderhalten kann. Dokumentiert in std.result (stdlib-redesign) und in der Ablehnungsliste der Roadmap mit Begruendung statt Behauptung.
- Artefakt: design/roadmap.md („Bewusst NICHT in der Roadmap")
- Abgestimmt mit: stdlib-redesign (ihre Gegenprobe, meine Einordnung)

### [new-features] rawArrayAlloc haengt doppelt — Prioritaet steigt
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/roadmap.md („Fremde Voraussetzungen in 4.5"), stdlib-redesign design/stdlib-2.md §15.2
  ```lyr
  // (a) stdlib: List<?T>/Map<K, ?V> koennen ihre Backing-Arrays nicht bauen ((?T)[] wird fuer T = ?U zu ??U)
  // (b) pattern-lambda: [first, ..rest] — das Lowering kann kein Array unbekannter Laenge bauen
  ```
- Extern (Vergleich): Rust `Vec::with_capacity` + `MaybeUninit`; Go `make([]T, n)`; Zig `allocator.alloc(T, n)` — alle drei geben der Bibliothek einen uninitialisierten Puffer, ohne dafuer eine Sprachregel zu aendern.
- Beschreibung: Zwei unabhaengig gemeldete Features haengen an demselben privaten Native von ~15 VM-Zeilen (unbeobachtbar uninitialisiert, modulprivat). Damit ist es nach „generische Methode auf generischem Typ" der zweitwichtigste und zugleich billigste Posten der 4.5-Voraussetzungen; in meiner Roadmap ist es aus der Fussnote unter „nestbare Optionals" zu einer eigenen Position aufgestiegen. Sprachseitig unbedenklich: kein Sprachfeature, kein Formatwechsel, unbeobachtbar.
- Artefakt: design/roadmap.md
- Abgestimmt mit: stdlib-redesign (Zusammenfuehrung in §15.2), pattern-lambda (ihr ..rest haengt am selben Haken)

### [new-features] Dissens ?T :: [Display] aufgeloest
- Kategorie: decision
- Priorität: LOW
- Status: done
- Intern (Lyric): design/conditional-conformance.md (Offene Frage 4, jetzt entschieden), design/fstring-display.md
  ```lyr
  println(f"{opt}");                       // bleibt ein Fehler
  println(showOptional(opt, "none"));      // std.option, 4.5 (stdlib-redesign)
  assertEq(parseInt("x"), null);           // Testbericht rendert `null` als sichtbares Wort
  ```
- Extern (Vergleich): Rust — `Option<T>` ist `Debug`, nicht `Display`; Kotlin/C# drucken `null`/`""` und verstecken damit eine Abwesenheit in der Programmausgabe.
- Beschreibung: stdlib-redesign hat meine Ablehnung ohne Gegenrede angenommen. Die Begruendung, die traegt: der Unterschied liegt im LESER, nicht im Typ — ein Testbericht will das Wort `null` sehen, eine Programmausgabe darf eine Abwesenheit nicht verstecken. Deshalb gehoert es in eine Funktion (`showOptional`, `assertEq` rendert selbst) und nicht in eine Konformanz. Damit ist der einzige Dissens zwischen new-features und stdlib-redesign geschlossen; der frueher eingetragene decision-Eintrag „Dissens: soll ?T als Display definierbar sein?" ist hiermit erledigt.
- Artefakt: design/conditional-conformance.md (Offene Frage 4)
- Abgestimmt mit: stdlib-redesign (angenommen)

### [pattern-lambda] BUG behoben: Argument einer Methode auf generischer Instanz ohne Substitution gelowert
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): Commit 76ae5ee8 — FunctionLowerer.cs LowerGenericMethodCall (und der Constraint-Pfad für einen generischen Receiver) reichten keine `calleeSubstitution` an `MaterializeArguments`. Ein Parameter, der `T` geschrieben steht, ließ `_typeTable.Lower` am unaufgelösten Namen werfen; der catch in `LowerArgument` fing das und reichte das Argument OHNE Widerung durch. Repro (pl-probes/p50_generic_method.lyr):
  ```lyr
  enum Holder<T> { Full(T), Empty; fn or(fallback: T): T { … } }
  class Box<T> { v: T, fn or(fallback: T): T { … } }
  let x: ?int = 5;  Holder<?int>.Full(x).or(3)   // ir-verifier: store of t3 (i64) into l9 (?i64)
  ```
  Neuer Helfer `InstanceSubstitution(GenericInstance)`; Test GenericEnumTests.A_parameter_written_as_the_type_parameter_is_lowered_under_the_instance (3 Fälle: Enum-Full, Enum-Empty, Klasse). Suiten grün: Vm 1554, Sema 807, Ir 175, Parsing 475, Formatting 190, Lsp 279, Bytecode 184.
- Extern (Vergleich): —
- Beschreibung: Von stdlib-redesign bei der Gegenprobe gefunden (ihr Commit a8df8a5d auf ihrem Branch); auf meinem Branch aus demselben Grund erreichbar, weil die Methoden generischer Enums hier lowern — der Klassenpfad erreichte denselben Helfer zuerst und verdeckte die Lücke. Zwei unabhängige Fixes derselben Ursache; beim Merge einen behalten (meiner zieht zusätzlich den Constraint-Pfad mit).
- Artefakt: Commit 76ae5ee8, scratchpad/team3/pl-probes/p50_generic_method.lyr
- Abgestimmt mit: stdlib-redesign (Finder)

### [pattern-lambda] Designs nachgeführt: rawArrayAlloc-Haken und ValueBlock-Vereinfachung
- Kategorie: decision
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): design/patterns.md §3.3 — der benannte Rest `[first, ..rest]` hängt am SELBEN Haken wie `List<?T>`/`Map<K, ?V>`: ein Array unbekannter Länge bauen, also das private Native `rawArrayAlloc<T>(n): T[]` (~15 VM-Zeilen für zwei Features, danach ~20 Zeilen im Pattern-Compiler). design/lambdas.md §2.2 — nach dem Merge mit dem ValueBlock entfällt mein `HoldsStatements` ersatzlos (`{ it * 2 }` und `{ let y = it; y + 1 }` sind dann beide ValueBlocks mit Tail), und die Trailing-Lambda-Erkennung beantwortet umgekehrt new-features' offene Frage zum Struct-Initializer als Tail (Entscheidung an den zwei Tokens hinter der öffnenden Klammer, keine Klammernpflicht).
- Extern (Vergleich): Kotlin kennt die Unterscheidung Statement-Block/Ausdruck-Body gar nicht — nach dem Merge steht Lyric dort.
- Beschreibung: Zwei Entscheidungen, die erst durch den Abgleich zwischen drei Branches sichtbar wurden; beide vorwärtskompatibel, keine Codeänderung nötig.
- Artefakt: design/patterns.md §3.3, design/lambdas.md §2.2, deliverables/pattern-lambda/design/
- Abgestimmt mit: stdlib-redesign (rawArrayAlloc), new-features (ValueBlock)
### [new-features] Tail-Regel schaltet Initializer und Trailing-Lambda gemeinsam scharf
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): design/value-block.md (Offene Frage 3), geprueft an pattern-lambdas Parser.cs ~763 (`IsTrailingLambdaAhead(IdentifierExpr) => _allowStructInit`) und ~497 (`IsStructInitAhead`)
  ```lyr
  // im ValueBlock, wenn _allowStructInit am Statement-Anfang gesetzt bleibt:
  Event.Reset => { println("x"); Point { x = 0, y = 0 } },   // Initializer-Tail
  Event.Run   => { run { 7 } },                              // Aufruf mit Trailing-Lambda
  // Preis: ein Bezeichner am Statement-Anfang kann dort nicht mehr von einem verschachtelten
  // Statement-Block gefolgt werden — aber ein Bezeichner-Statement ist ohnehin SEM0022.
  ```
- Extern (Vergleich): Kotlin/Swift zahlen denselben Preis (ein Name vor `{` ist dort immer ein Aufruf mit Trailing-Lambda) und haben kein `;`, das man vermissen koennte; Rust loest dieselbe Mehrdeutigkeit in `if`/`match`-Koepfen durch Klammerzwang um Struct-Literale.
- Beschreibung: Nachtrag zur beantworteten Frage 3. Beide Lesarten — Struct-Initializer und Trailing-Lambda — haengen bei pattern-lambda am selben Flag `_allowStructInit`. Am Statement-Anfang ist es aus, und ein bloszer Name vor `{` bleibt die §6.8-Form „Name, dann Block". Setzt der ValueBlock das Flag, werden BEIDE Lesarten zugleich scharf — genau die Kombination, die man in Wert-Position will. Der Preis ist jetzt benannt statt spaeter entdeckt: innerhalb eines ValueBlocks kann ein Bezeichner am Statement-Anfang nicht mehr von einem verschachtelten Statement-Block gefolgt werden, ohne als Aufruf oder Initializer gelesen zu werden. Nichts, was heute kompiliert, aendert seine Bedeutung (ein Bezeichner-Statement ist ohnehin LYR-SEM0022); es aendert sich nur die Diagnose fuer einen Tippfehler.
- Artefakt: design/value-block.md (Offene Frage 3, Absatz „Die Regel kippt zwei Lesarten gleichzeitig")
- Abgestimmt mit: pattern-lambda (von ihnen gemeldet, von mir an ihrem Code geprueft und eingeordnet)

### [new-features] Generische-Argument-Substitution: derselbe Fix liegt zweimal vor
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/roadmap.md („Fremde Voraussetzungen in 4.5"); stdlib-redesign a8df8a5d, pattern-lambda 76ae5ee8, beide in FunctionLowerer (LowerGenericMethodCall → MaterializeArguments)
  ```lyr
  Holder<?int>.Full(x).or(3)     // Enum: malformed IR (store of i64 into ?i64)
  Box<?int>{ … }.or(3)           // Klasse: lief, deshalb war die Luecke verdeckt
  ```
- Extern (Vergleich): —
- Beschreibung: `LowerGenericMethodCall` reichte keine `calleeSubstitution` an `MaterializeArguments`; ein Parameter, der `T` geschrieben steht, blieb ein Name, und bei `T = ?int` unterblieb die Widerung. Der Pfad fuer generische INTERFACE-Member baute dieselbe Abbildung laengst. Zwei Teammitglieder haben den Defekt unabhaengig am selben Tag gefunden und gefixt. MERGE-EMPFEHLUNG: den von pattern-lambda (76ae5ee8) behalten — er zieht zusaetzlich den Constraint-Pfad mit —, die Tests BEIDER Branches uebernehmen (sie pinnen Klasse und Enum getrennt). Dass der Defekt doppelt gefunden wurde, ist das staerkste Argument dafuer, dass er in 4.5 gehoert.
- Artefakt: design/roadmap.md
- Abgestimmt mit: pattern-lambda, stdlib-redesign (beide Fixes gemeldet)
### [new-features] Praezisierung: der Preis der Tail-Regel trifft NUR den bloszen Bezeichner
- Kategorie: analysis
- Priorität: LOW
- Status: done
- Intern (Lyric): design/value-block.md (Offene Frage 3); Abgrenzung von pattern-lambda, design/lambdas.md §2.2
  ```lyr
  // BETROFFEN (und heute schon SEM0022): ein bloszer Bezeichner am Statement-Anfang, dem '{' folgt
  Event.X => { foo { let a = 1; } },
  // NICHT betroffen: ein Block, der fuer sich steht — beginnt mit '{', wird nie Argument von etwas
  Event.Y => { { let a = 1; } 7 },
  // NICHT betroffen: jede Form, die ihren Block ueber ein Schluesselwort erreicht
  Event.Z => { if (b) { … } while (c) { … } match (v) { … } 7 },
  ```
- Extern (Vergleich): Kotlin/Swift zahlen denselben, ebenso eng begrenzten Preis und haben kein `;`, dessen Fehlen man bemerken koennte.
- Beschreibung: Korrektur der Formulierung im vorigen analysis-Eintrag („Tail-Regel schaltet Initializer und Trailing-Lambda gemeinsam scharf"). Dort stand „ein Bezeichner am Statement-Anfang kann nicht mehr von einem verschachtelten Statement-Block gefolgt werden" — das lieszt offen, ob verschachtelte Bloecke allgemein betroffen sind. Sie sind es NICHT: ein fuer sich stehender Block beginnt mit `{` und wird nie zum Argument, und jede Form mit Schluesselwort (if/while/for/match/try/defer) erreicht ihren Block ohnehin anders. Uebrig bleibt genau die Konstruktion, die heute schon LYR-SEM0022 ist. Der Preis der Regel ist damit strikt kleiner als zuvor beschrieben: eine andere Diagnose fuer einen Tippfehler, sonst nichts.
- Artefakt: design/value-block.md (Offene Frage 3, Absatz „Der Preis, benannt und eingegrenzt")
- Abgestimmt mit: pattern-lambda (ihre Abgrenzung, von mir uebernommen)

### [stdlib-redesign] BUG behoben (Nachtrag): auch der statische und der Constraint-Pfad widerten Argumente nicht
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs — mein erster Fix (a8df8a5d) deckte nur `LowerGenericMethodCall`. pattern-lambda meldete eine weitere Stelle; die Prüfung (probes/iso_paths.lyr, zwei Verifier-Findings, u. a. `optissome expects an optional, found i64`) fand ZWEI: `LowerGenericStaticCall` und der Constraint-Pfad mit generischem Receiver.
  ```lyr
  interface Keeper<T> { fn or(fallback: T): T; }
  class Box<T> :: [Keeper<T>] { value: T, pub static fn of(v: T): Box<T> {…} pub fn or(fallback: T): T {…} }
  fn viaConstraint<K :: [Keeper<?int>]>(k: K): ?int { return k.or(3); }
  Box<?int>.of(3)        // statischer Pfad — stürzte ab
  viaConstraint(boxed)   // Constraint-Pfad — stürzte ab
  ```
  Fix: Helfer `InstanceSubstitution(GenericInstance)`, benutzt von allen drei Stellen ohne Abbildung (der Interface-Member-Pfad trug sie längst). Test `An_argument_widens_to_what_the_instance_makes_of_its_parameter_type` pinnt jetzt VIER Wege: Instanzmethode Klasse, Instanzmethode Enum, statische Methode, Constraint-Aufruf. Commit 330be0f3.
- Extern (Vergleich): Rust/Swift monomorphisieren die Parametertypen mit; die Frage „gegen welchen Typ wird das Argument geprüft“ stellt sich dort nicht pro Aufrufpfad.
- Beschreibung: Warum es unsichtbar blieb: eine Klasse MIT Interface-Konformanz nimmt ohnehin den Pfad, der die Abbildung hatte — nur die Kombination „generische Instanz + Methode ohne Interface-Weg“ traf die Lücke. pattern-lambda hat denselben Defekt unabhängig gefunden (ihr Commit 76ae5ee8); beim Merge genügt einer der beiden Fixes, mit der Prüfung, dass alle vier Wege abgedeckt sind. Suiten: Ir 176, Sema 771, Vm 1453, Embedding 222, Bytecode 184, stdlib-tests 211 — grün.
- Artefakt: probes/iso_paths.lyr, Commit 330be0f3, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] KORREKTUR zu 76ae5ee8: der statische Pfad fehlte — jetzt alle vier Wege (Commit 5dca585f)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): Mein Commit 76ae5ee8 deckte LowerGenericMethodCall und den Constraint-Pfad; `LowerGenericStaticCall` (FunctionLowerer.cs:3566) blieb offen — stdlib-redesign hat genau danach gefragt, und die Frage war berechtigt. Repro (pl-probes/p51_four_paths.lyr): `Box<?int>.of(3)` → `optissome expects an optional, found i64`. Jetzt übergeben alle drei Stellen ohne Abbildung `InstanceSubstitution(owner)`; der Interface-Member-Pfad trug sie längst.
  WICHTIG für jeden, der das nachprüft: das Argument muss ein LITERAL sein. Meine erste Probe übergab ein bereits optionales `x` — das braucht keine Widerung und rutscht durch die Lücke, ohne sie zu berühren; die Probe war grün, während drei von vier Wegen kaputt waren.
  Test GenericEnumTests jetzt mit sieben Fällen über vier Wege: Enum-Instanzmethode (mit/ohne Payload), Klassen-Instanzmethode mit und ohne Interface-Konformanz, statische Methode (beide Klassen), Constraint-Pfad. Suiten grün: Vm 1558, Sema 807, Ir 175, Parsing 475, Formatting 190, Lsp 279, Bytecode 184.
- Extern (Vergleich): —
- Beschreibung: Warum der Defekt so lange unsichtbar war: eine Klasse MIT Interface-Konformanz nimmt den Interface-Pfad, der die Abbildung hatte. Wer zuerst ein Repro mit einer konformen Klasse schrieb, sah nichts. stdlib-redesign hat parallel dieselben zwei Reststellen gefunden (ihr Commit 330be0f3) — beide Fassungen sind jetzt deckungsgleich, beim Merge genügt eine, aber mit der Prüfung „sind alle vier Wege drin".
- Artefakt: Commit 5dca585f, scratchpad/team3/pl-probes/p51_four_paths.lyr
- Abgestimmt mit: stdlib-redesign (Frage und Parallelfund)
### [new-features] KORREKTUR der Merge-Empfehlung zum Substitutions-Defekt: inhaltlich statt nach Herkunft
- Kategorie: decision
- Priorität: HIGH
- Status: done
- Intern (Lyric): design/roadmap.md („Fremde Voraussetzungen in 4.5"); pattern-lambda 76ae5ee8 + 5dca585f, stdlib-redesign a8df8a5d + 330be0f3
  ```lyr
  // Die VIER Wege zu einer Methode einer generischen Instanz — alle vier abhaken:
  Box<?int>{ v = null }.or(3)     // Instanzmethode, Klasse (mit UND ohne Interface-Konformanz getrennt)
  Holder<?int>.Full(x).or(3)      // Instanzmethode, Enum
  Box<?int>.of(3)                 // STATISCHE Methode (LowerGenericStaticCall) — war in beiden ersten Fassungen offen
  useIt<Box<?int>>(b, 3)          // Constraint-Pfad mit generischem Receiver
  // Bedingung an den Test: das Argument MUSS ein Literal sein — ein bereits optionaler Wert
  // braucht keine Widerung und passiert die Luecke, ohne sie zu beruehren.
  ```
- Extern (Vergleich): —
- Beschreibung: KORRIGIERT meinen vorigen Eintrag „Generische-Argument-Substitution: derselbe Fix liegt zweimal vor", der empfahl, pattern-lambdas Fassung zu behalten, „weil sie den Constraint-Pfad mitzieht". Das war zum Zeitpunkt der Notiz falsch: ihre Fassung liesz `LowerGenericStaticCall` offen (von stdlib-redesign erfragt, mit 5dca585f geschlossen); stdlib-redesign fand dieselben zwei Reststellen parallel (330be0f3). Beide Fassungen sind jetzt deckungsgleich. LEHRE: eine Empfehlung nach Herkunft haette genau den halben Fix konserviert — die Merge-Pruefung gehoert an den Inhalt: die vier Wege (Instanz Klasse, Instanz Enum, statisch, Constraint-Pfad), Klasse mit und ohne Interface-Konformanz getrennt, weil der Interface-Member-Pfad die Abbildung von Anfang an trug und den Defekt damit verdeckte. METHODISCH: das Testargument muss ein Literal sein, sonst ist der Test gruen und beweist nichts — eine Vier-Wege-Probe war gruen, waehrend drei der vier Wege kaputt waren. Beide Testsaetze uebernehmen (pattern-lambda: sieben Faelle bei der Ausfuehrung; stdlib-redesign: eine Schicht tiefer im IR).
- Artefakt: design/roadmap.md
- Abgestimmt mit: pattern-lambda (Korrektur von ihnen gemeldet), stdlib-redesign (hat die Luecke erfragt und parallel geschlossen)

### [stdlib-redesign] Nachweis statt Annahme: vier Findings, ein Weg je Finding — und eine eigene Erklärung widerlegt
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): tests/Lyric.Tests.Ir/LoweringTests.cs `An_argument_widens_to_what_the_instance_makes_of_its_parameter_type`. Die Abbildung an allen vier Stellen temporär neutralisiert → der Test wird rot mit GENAU vier Findings, eines pro Weg:
  ```
  main.f: call to Box<?int>.of: arg 0 is i64, expected ?i64        (statische Methode)
  main.f: call to Box<?int>.or: arg 1 is i64, expected ?i64        (Instanzmethode, Klasse)
  main.f: call to Holder<?int>.or: arg 1 is i64, expected ?i64     (Instanzmethode, Enum)
  viaConstraint<Box<?int>>: call to Box<?int>.or: arg 1 …          (Constraint-Pfad)
  ```
  Damit ist belegt, dass der Test die vier Pfade wirklich prüft und nicht nur danach aussieht.
- Extern (Vergleich): -
- Beschreibung: ZWEI Korrekturen fallen aus dem Nachweis. (1) Meine frühere Erklärung „eine Klasse MIT Interface-Konformanz nimmt den Interface-Pfad und zeigt die Lücke deshalb nicht" ist FALSCH — das konforme `Box` landet ebenfalls im Instanz-Pfad (Finding 2). Wer die Stelle dokumentiert, nehme pattern-lambdas Befund, nicht meine These. (2) pattern-lambdas Falle übernommen: JEDES Argument in einer solchen Probe muss ein LITERAL sein; ein Wert, der schon `?int` ist, braucht keine Widerung und reist durch die Lücke, ohne sie zu berühren — ihre Vier-Wege-Probe war deshalb grün, während drei Wege kaputt waren. Mein Test hatte an einer Stelle denselben Fehler (`made.or(slot)`), steht jetzt auf `made.or(3)`, und der Kommentar schreibt die Regel fest (Commit 1d043492).
- Artefakt: Commits 1d043492, 4158f539; design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] KORREKTUR: die Konformanz-These ist widerlegt — gemessen statt vermutet (Commit f9e8d193)
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): Experiment auf meinem Branch — Substitution in `LowerGenericMethodCall` entfernt, drei Minimalproben (pl-probes/q1_enum.lyr, q2_class_conform.lyr, q3_class_plain.lyr):
  - Enum-Instanzmethode: **bricht** (`store of t3 (i64) into l4 (?i64)`)
  - Klasse MIT `:: [Keeper<T>]`: **läuft**
  - Klasse OHNE Konformanz: **läuft**
  Die Konformanz entscheidet also nichts. Beide vorher kursierenden Erklärungen sind damit falsch: stdlib-redesigns „konforme Klasse nimmt den Interface-Pfad" ebenso wie meine eigene Behauptung „auf meinem Branch krachten Enum UND Klasse". Letztere war ein Lesefehler: die zwei Findings in p50 stammten von den zwei ENUM-Aufrufen (Full und Empty), nicht von Enum+Klasse — p50 hatte drei Aufrufe, nicht zwei.
  Zweites Experiment (früher, dokumentiert): ohne Substitution in `LowerGenericStaticCall` brechen beide statischen Zeilen. Welchen Pfad eine Klassen-Instanzmethode stattdessen nimmt, habe ich NICHT ermittelt — der Test-Kommentar behauptet es deshalb auch nicht mehr.
- Extern (Vergleich): —
- Beschreibung: Der Test-Kommentar in GenericEnumTests trug die widerlegte Kausalgeschichte; er nennt jetzt nur das Gemessene und markiert die offene Frage als offen. Für die Dokumentation des Defekts gilt: die verdeckten Pfade waren der statische und (laut stdlib-redesigns Messung auf ihrem Branch) der Constraint-Pfad; „Klasse zeigt nichts" ist der Grund, warum zwei aufeinanderfolgende Repros nichts fanden — beide waren mit einer Klasse geschrieben.
- Artefakt: Commit f9e8d193, pl-probes/q1_enum.lyr, q2_class_conform.lyr, q3_class_plain.lyr
- Abgestimmt mit: stdlib-redesign (hat ihre These selbst zurückgezogen; meine Messung bestätigt die Rücknahme und widerlegt zusätzlich meine eigene Gegenbehauptung)

### [stdlib-redesign] Findings pro Aufrufpfad zugeordnet — zwei eigene Erklärungen erledigt, eine Divergenz bleibt offen
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): probes/attribute_paths.py — neutralisiert die Instanz-Substitution an GENAU EINER Stelle, baut, lässt den Vier-Wege-Test laufen, stellt das Original wieder her. Zuordnung auf diesem Branch:
  ```
  LowerGenericMethodCall allein  → 3 Findings: Box<?int>.or (Klasse, konform), Holder<?int>.or (Enum),
                                   Box<?int>.or auf dem statisch erzeugten Objekt
  LowerGenericStaticCall allein  → 1 Finding:  Box<?int>.of
  Constraint-Pfad allein         → 1 Finding:  Box<?int>.or im Rumpf von viaConstraint
  ```
- Extern (Vergleich): -
- Beschreibung: Erledigt ZWEI eigene Aussagen, die aus einer Messung stammten, die die Stellen nicht trennte: (1) „eine Klasse MIT Interface-Konformanz nimmt den Interface-Pfad" — falsch, das konforme `Box` bricht im Instanz-Pfad; (2) „eine Klasse zeigt den Defekt nicht" (aus probes/iso_box.lyr) — falsch, sie zeigt ihn wie das Enum; der frühe Grünbefund war ein Messfehler. OFFEN und bewusst nicht wegargumentiert: pattern-lambda misst auf IHREM Branch das Gegenteil (nur Enum bricht, Klasse läuft, mit und ohne Konformanz). Beide Messungen sind je für sich sauber (Literale als Argumente, Stellen einzeln). Entweder unterscheiden sich die Branches (ihr Pattern-Compiler fasst das Lowering an) oder eine Probe misst etwas anderes als gedacht. Für den Fix folgenlos — beide Fassungen decken alle Stellen ab; beim Merge einmal nachmessen. Trennscharfe Probe steht in der Nachricht an pattern-lambda.
- Artefakt: probes/attribute_paths.py, Commit bf335732, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] Divergenz eingegrenzt: es liegt am Branch, nicht an der Probe (Commit db4b678c)
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib-redesign hat eine trennscharfe Probe vorgeschlagen — Klasse OHNE Konformanz, ein Feld, `pub fn or(fallback: T): T { return this.value; }`, Aufruf `Box<?int> { value = slot }.or(3)` mit `slot: ?int` — und die Vorhersage genannt: bleibt sie auf meinem Branch gruen, liegt es am Branch. Ich habe sie wortgetreu nachgebaut (pl-probes/q4_their_exact.lyr) und mit neutralisiertem `LowerGenericMethodCall` gemessen: **gruen**, waehrend q1_enum.lyr im selben Lauf bricht. Danach Original wiederhergestellt, neu gebaut, alle drei Proben und p51 verifiziert.
  Damit ist die Probe als Ursache ausgeschlossen: die beiden Baeume unterscheiden sich an einer Stelle, die dieses Experiment erreicht. Welche — nicht ermittelt, und ich messe nicht auf fremden Branches.
- Extern (Vergleich): —
- Beschreibung: Konsequenz fuer die Doku: mein Test-Kommentar behauptete eine Compiler-Eigenschaft, gemessen war aber eine Baum-Eigenschaft. Er nennt jetzt beide Messungen nebeneinander, ohne Ursachenbehauptung, und markiert es als die eine Frage, die ein Merge einmal nachmisst. Mein frueherer Vorschlag an stdlib-redesign, die Doku auf "eine Klasse zeigt den Defekt nicht" zu reduzieren, ist damit zurueckgezogen — auf ihrem Branch ist er falsch, sie haben ihn zu Recht nicht uebernommen. Folgenlos fuer den Fix: beide Fassungen decken alle vier Wege ab.
- Artefakt: Commit db4b678c, pl-probes/q4_their_exact.lyr
- Abgestimmt mit: stdlib-redesign (Probe und Vorhersage von dort, Messung hier)

### [stdlib-redesign] BEFUND für alle: der Optimierer kann fehlerhaftes IR verdecken — „läuft durch lyric run" belegt nichts
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): probes/class_variants.py, probes/inliner_hypothesis.py. Instanz-Substitution in `LowerGenericMethodCall` neutralisiert, dann dieselbe Klasse mit zwei Körpern über `lyric run` (optimiert):
  ```
  pub fn or(fallback: T): T { return this.value; }              → GRÜN (Inliner bettet ein, der fehlerhafte Call verschwindet)
  pub fn or(fallback: T): T { var i = 0; … while … return …; }  → bricht: store of t2 (i64) into l6 (?i64)
  enum-Methode mit match-Körper                                 → bricht immer (zu groß zum Einbetten)
  ```
- Extern (Vergleich): Rust/LLVM — ein Verifier läuft dort VOR den Optimierungen, genau damit eine Optimierung keinen Defekt zudecken kann.
- Beschreibung: Löst die Team-Divergenz auf, an der pattern-lambda und ich je zwei Erklärungen verbraucht haben (Konformanz, Branch-Unterschied — beide falsch). Es entscheidet die PIPELINE: der Testpfad (`TryLower`) lowert mit `optimize:false` und sieht den Defekt, `lyric run` optimiert und sieht ihn nicht, sobald der Körper klein genug zum Einbetten ist. Konsequenz für jeden im Team, der in dieser Runde mit `lyric run` gegengeprüft hat: das ist kein Beleg für wohlgeformtes IR. Wer einen Verifier-Befund sucht, misst unoptimiert oder mit einem nicht-inlinebaren Körper. Zusammen mit dem bekannten „IrVerifier läuft nur im Debug-Build" (Bughunt P2-19) heißt das: im Release-Build mit Optimierung ist ein solcher Defekt doppelt unsichtbar. Vorschlag an new-features/ir-Zuständige: den Verifier VOR dem Optimierer laufen lassen (heute läuft er danach, SourceCompiler.cs:132-143), dann kann keine Optimierung einen Lowering-Fehler mehr zudecken.
- Artefakt: probes/class_variants.py, probes/inliner_hypothesis.py, Commit 6c22040e, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] AUFLOESUNG bestaetigt: der Inliner verdeckte den Defekt — und mein Test hing auf der falschen Seite (Commit a47c8c49)
- Kategorie: bug
- Priorität: HIGH
- Status: done
- Intern (Lyric): stdlib-redesigns Inliner-Hypothese auf meinem Branch nachgemessen (pl-probes/q5_loop_body.lyr): dieselbe Klasse wie q4, aber `or` mit einer Schleife im Koerper — bei neutralisiertem `LowerGenericMethodCall` **bricht** sie mit `call to Box<?int>.or: arg 1 is i64, expected ?i64`, waehrend q4 (`return this.value;`, einbettbar) gruen bleibt. Die Klasse bricht also im Instanz-Pfad genauso wie das Enum; der Inliner frisst den fehlerhaften Call mitsamt Argument. **Meine These „die Baeume unterscheiden sich" ist damit widerlegt** — es gab nie einen Branch-Unterschied, nur zwei Pipelines (Testpfad unoptimiert vs. `lyric run` optimiert).
  FOLGE FUER MEINEN TEST: er lowerte mit `optimize: true`, seine Klassen-Zeilen haetten den Defekt also **nie gefangen** — sie waeren mit dem Bug gruen gewesen. `Run` nimmt jetzt ein `optimize`-Flag, der Vier-Wege-Test lowert unoptimiert. Nachweis: mit entfernter Substitution fallen **sechs von sieben** Zeilen, beide Klassen-Zeilen eingeschlossen; vorher waeren es zwei gewesen. Danach wiederhergestellt, Suiten grün (Vm 1558, Sema 807, Ir 175, Parsing 475, Formatting 190, Lsp 279, Bytecode 184).
- Extern (Vergleich): LLVM verifiziert VOR den Optimierungspaessen, genau damit keine Optimierung einen Lowering-Fehler verbergen kann.
- Beschreibung: Der allgemeine Befund (von stdlib-redesign, hier bestaetigt): der Verifier laeuft NACH dem Optimierer, also ist „laeuft durch `lyric run`" kein Beleg fuer wohlgeformtes IR. Wer einen Verifier-Befund sucht, misst unoptimiert oder mit einem Koerper, den der Inliner nicht frisst. Zusammen mit „IrVerifier nur im Debug-Build" (Bughunt P2-19) ist so ein Defekt im Release doppelt unsichtbar. Betrifft jede Gegenprobe dieser Runde, die ueber `lyric run` lief — meine eingeschlossen.
- Artefakt: Commit a47c8c49, pl-probes/q4_their_exact.lyr, q5_loop_body.lyr
- Abgestimmt mit: stdlib-redesign (Hypothese und Aufloesung von dort, Bestaetigung und Testkonsequenz hier)

### [stdlib-redesign] Messung zur Empfehlung „Verifier vor den Optimierer": kostet heute nichts
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): probes/verify_unoptimised.py — setzt `CompilerOptions.Optimize` (SourceCompiler.cs:449) testweise auf `false`, sodass `VerifyOrThrow` das GELOWERTE statt des optimierten IR prüft, und kompiliert damit das gesamte mitgelieferte Lyric-Korpus:
  ```
  stdlib + stdlib-tests : 211 test(s), all passed   (kein Verifier-Befund)
  examples              : 47 kompiliert, 0 mit Befund
  ```
- Extern (Vergleich): LLVM verifiziert VOR den Optimierungspässen, genau damit ein Pass keinen Defekt der Erzeugung zudecken kann.
- Beschreibung: Folgt auf den Befund „der Inliner verdeckt fehlerhaftes IR". Die Empfehlung, den Verifier vor die Optimierung zu ziehen (SourceCompiler.cs:132-143), ist damit nicht nur begründet, sondern als RISIKOARM belegt: es gibt heute im ausgelieferten Korpus nichts, was der Optimierer stillschweigend reparieren müsste — der Umbau würde also keine bestehende Datei rot machen. Wer ihn aufnimmt, braucht nur die Reihenfolge zu ändern, nicht Code zu reparieren. (Dass der Verifier zusätzlich nur im Debug-Build läuft — Bughunt P2-19 — bleibt der zweite Teil desselben Themas.)
- Artefakt: probes/verify_unoptimised.py, Commit dcf266d5, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda (Befundkette), new-features (Adressat der Empfehlung)

### [pattern-lambda] Pattern-Compiler unoptimiert verifiziert — mit Kontrolle, dass die Messung rot werden kann
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): Anschluss an stdlib-redesigns Korpusmessung, aber auf MEINEM Branch — mein Pattern-Compiler ist neues Lowering, und bis hierhin war er ausschliesslich OPTIMIERT verifiziert (alle Beispiele ueber `lyric run`, alle meine Testhelfer mit `optimize: true`). Genau die Blindstelle, die diese Runde aufgedeckt hat.
  Messung: `CompilerOptions.Optimize` testweise auf `false`, dann `lyric check <datei> --emit` (laeuft durch Lowering, Verifier und Bytes) ueber das gesamte Beispielkorpus des Branches: **28 Dateien, 0 Befunde** — einschliesslich meiner sechs neuen (state-machine, simplifier, binding-conditions, array-patterns, shorthand, destructuring), die zusaetzlich unoptimiert mit identischer Ausgabe laufen.
  KONTROLLE (die Lehre dieser Runde angewandt): Kann diese Messung ueberhaupt rot werden? Substitution testweise entfernt, `check --emit` auf q4_their_exact.lyr → exit 134, "ir-verifier: malformed IR (1 finding(s)) … call to Box<?int>.or: arg 1 is i64, expected ?i64". Der Messpfad erreicht den Verifier also nachweislich. Nebenbei ein dritter unabhaengiger Beleg fuer die Inliner-Erklaerung: dieselbe Datei ist optimiert gruen und unoptimiert rot.
  Danach beide Eingriffe zurueckgenommen, neu gebaut, Proben und Korpus erneut gemessen: 28/0, alles gruen. Arbeitsbaum sauber, Quellen unveraendert.
- Extern (Vergleich): LLVM verifiziert vor den Optimierungspaessen.
- Beschreibung: Ergebnis fuer die Empfehlung "Verifier vor den Optimierer": auf meinem Branch ebenso risikoarm wie auf stdlib-redesigns — das Pattern-Lowering (rekursiver Compiler, Array-Patterns, Bindungsbedingungen, Lambda-Formen) erzeugt wohlgeformtes IR ohne Zutun des Optimierers. Zusammen decken beide Messungen stdlib, stdlib-Tests und beide Beispielkorpora ab.
- Artefakt: pl-probes/corpus.sh, q4_check.log
- Abgestimmt mit: stdlib-redesign (Messansatz von dort, Kontrollmessung und Branch-Abdeckung hier)

### [stdlib-redesign] Kontrolle zur Korpusmessung: beide Messpfade können rot werden (die Regel auf sich selbst angewandt)
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): probes/control_corpus.py — `CompilerOptions.Optimize = false` UND die Instanz-Substitution in `LowerGenericMethodCall` entfernt, dann eine Datei mit sicher auslösendem Defekt (Klassenmethode mit nicht einbettbarem Körper) durch GENAU die zwei Pfade, über die das Korpus geprüft wurde:
  ```
  lyric test (stdlib-tests) → ir-verifier: malformed IR (4 finding(s))
  lyrc build (examples)     → ir-verifier: malformed IR (1 finding(s))
  ```
  Danach Quellen wiederhergestellt, neu gebaut, Kontrolldateien entfernt, 211 Tests grün.
- Extern (Vergleich): -
- Beschreibung: Meine Schlussmessung (stdlib + 211 Tests + 47 Beispiele, 0 Befunde) hatte diese Kontrolle NICHT — ich hatte die Regel „eine Gegenprobe muss zeigen können, dass sie rot wird" selbst formuliert und auf die eigene Messung nicht angewandt; aufgefallen ist es durch pattern-lambdas Nachtrag, der sie auf ihrer Seite angewandt hatte. Risiko konkret: `lyrc build` stellt seine CompilerOptions selbst zusammen, der Optimize-Patch hätte dort wirkungslos sein können — dann wäre die Aussage über die 47 Beispiele unbelegt gewesen. Ist sie nicht: beide Pfade melden. Die Empfehlung „Verifier vor die Optimierungspässe" steht damit auf zwei Korpora mit Kontrolle auf beiden Seiten (pattern-lambda: 28 Dateien mit neuem Pattern-Lowering, 0 Befunde, eigene Kontrolle).
- Artefakt: probes/control_corpus.py, Commit cc91d3cd, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] Auch die TESTKORPORA unoptimiert verifiziert — 2540 Tests, kein Verifier-Befund
- Kategorie: analysis
- Priorität: HIGH
- Status: done
- Intern (Lyric): Letzte verbliebene Blindstelle auf meiner Seite: meine Pattern- und Lambda-Tests (NestedPatternTests, BindingConditionTests, ArrayPatternTests, LambdaFormTests, PatternCompilerSemaTests, LetPatternTests, ExhaustivenessWitnessTests) lowern ueber `ModuleLowerer.Lower` mit `optimize`-Standard true — nur GenericEnumTests hatte ich umgestellt. Sie verifizierten also optimiertes IR, genau wie das Beispielkorpus vorher.
  Messung: `ModuleLowerer.Lower`-Standard testweise auf `optimize: false`, dann die drei Suiten, die IR erzeugen: **Vm 1555/1558, Ir 175/175, Sema 807/807 — 0 Verifier-Befunde** (im gesamten Log kein `malformed`/`InternalCompilationException`). Die drei Abweichungen sind Backtrace-Tests (`Collections differ`), die Frames vergleichen — ohne Inlining sieht ein Backtrace anders aus, was der Doc-Kommentar von `Optimize` genau so beschreibt. Kein einziger Fall von IR, das nur dank Optimierung wohlgeformt aussah.
  Danach wiederhergestellt, neu gebaut, Vm 1558/1558 gruen. Arbeitsbaum sauber.
- Extern (Vergleich): —
- Beschreibung: Damit ist mein gesamter Beitrag — Pattern-Compiler, Array-Patterns, Bindungsbedingungen, Lambda-Formen, Zeugen-Diagnose — auf BEIDEN Wegen unoptimiert belegt: 28 Beispieldateien ueber `check --emit` (mit Kontrolle) und 2540 Tests ueber die Suiten. Nichts davon haengt am Optimierer.
- Artefakt: tasks/bvn2sd567.output
- Abgestimmt mit: stdlib-redesign (Messansatz), ergaenzt deren stdlib-Korpus um die Testkorpora

### [stdlib-redesign] Testkorpora unoptimiert verifiziert: kein Verifier-Befund, fünf erwartbare Abweichungen
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): probes/suites_unoptimised.py — `ModuleLowerer.Lower` (ModuleLowerer.cs:68-69) lowert per Standard `optimize: false`, sodass der Verifier das Ergebnis des LOWERINGS prüft; danach die vier IR-erzeugenden Suiten:
  ```
  Ir       176/176  grün
  Sema     771/771  grün
  Vm       3 Abweichungen: A_backtrace_names_the_line_that_panicked_and_the_line_that_called,
                           Division_by_zero_panics_with_a_backtrace,
                           Without_a_source_map_a_backtrace_is_names_only
  Bytecode 2 Abweichungen: FusionTests.An_operation_whose_result_is_read_twice_does_not_fuse,
                           Gate_program_compiles_to_bytecode_and_disassembles
  → kein einziger Verifier-Befund (kein 'malformed IR', keine InternalCompilationException)
  ```
  Wiederhergestellt, neu gebaut, Vm 1453/1453 und Bytecode 184/184 grün.
- Extern (Vergleich): -
- Beschreibung: Ergänzt die Korpusmessung um die Testkorpora — die Empfehlung „Verifier vor die Optimierungspässe" war bisher gegen Programme belegt (stdlib, 211 stdlib-Tests, 47 Beispiele), nicht gegen die .NET-Suiten, die eigenes IR bauen. Alle fünf Abweichungen setzen Optimierung VORAUS (Backtrace-Frames ohne Inlining, ein Optimierer-Test, ein Golden-Disassembly) und sind keine IR-Befunde; wer die Reihenfolge ändert, muss diese fünf Tests anfassen und sonst nichts. pattern-lambda hat dieselbe Messung auf ihrem Branch gefahren (Ir/Sema grün, dieselben drei Backtraces) — damit ist die Empfehlung auf beiden Bäumen und beiden Korpusarten belegt.
- Artefakt: probes/suites_unoptimised.py, Commit c09af6f1, design/stdlib-2.md §15.1(b)
- Abgestimmt mit: pattern-lambda

### [pattern-lambda] Messung vervollstaendigt: Bytecode-Suite nachgeholt, Bild deckungsgleich mit stdlib-redesign
- Kategorie: analysis
- Priorität: MEDIUM
- Status: done
- Intern (Lyric): stdlib-redesign meldete FUENF Abweichungen bei unoptimiertem Lowering, ich hatte DREI. Statt die Differenz stehen zu lassen, nachgesehen: `An_operation_whose_result_is_read_twice_does_not_fuse` liegt in tests/Lyric.Tests.Bytecode/FusionTests.cs — der Suite, die ich nicht gemessen hatte. Keine Divergenz, sondern eine Luecke in MEINER Messung.
  Nachgeholt (`ModuleLowerer.Lower`-Standard auf `optimize: false`): Bytecode **182/184**, die zwei Abweichungen sind `FusionTests.An_operation_whose_result_is_read_twice_does_not_fuse` und `BytecodeTests.Gate_program_compiles_to_bytecode_and_disassembles` (Golden-Disassembly) — beide pruefen Optimierer-Verhalten, **kein Verifier-Befund**. Danach wiederhergestellt, Bytecode 184/184 gruen, Arbeitsbaum sauber.
  Gesamtbild meiner Seite unoptimiert: Vm 1555/1558 (3 Backtraces), Ir 175/175, Sema 807/807, Bytecode 182/184 (Fusion + Golden) = **fuenf Abweichungen, alle mit vorausgesetzter Optimierung, null Verifier-Befunde** — identisch zu stdlib-redesigns Bild, bis auf die Testnamen.
- Extern (Vergleich): —
- Beschreibung: Wer die Empfehlung "Verifier vor die Optimierungspaesse" umsetzt, fasst genau diese fuenf Tests an und sonst nichts — auf beiden Baeumen unabhaengig belegt. Damit sind alle vier Korpusarten abgedeckt: stdlib, stdlib-Tests, beide Beispielkorpora, beide .NET-Testkorpora.
- Artefakt: pl-probes/corpus.sh, tasks/bvn2sd567.output
- Abgestimmt mit: stdlib-redesign (Differenz von dort gemeldet, Aufloesung hier)
