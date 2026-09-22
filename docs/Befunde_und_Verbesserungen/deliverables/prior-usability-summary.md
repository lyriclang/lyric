# Lyric Usability & Evolution Review — priorisierte Zusammenfassung (Team-Lead)

Stand: 2026-09-22, Basis dc32100c (v4.4.1). 3 Agenten, 116 Einträge in TASKLIST.md
(21 bug, 36 shortcoming, 7 qol, 9 strength, 11 proposal-minor, 10 proposal-major, 23 prototype),
22 Prototyp-Verzeichnisse unter prototypes/<NN>-*/ (ist.lyr lauffähig, soll.lyr, vergleich.*, README.md).

## P0 — Bugs, die beim Schreiben normaler Programme auftreten (zuerst fixen)
1. Zweite `let x` im selben Block still verworfen, erste Bindung gewinnt. TypeChecker.cs:1040. Prototyp 10 empfiehlt Rust-Shadowing (Sema ~20 Z.). [3 Agenten]
2. Generische Coroutine-Funktion (`fn gen<T>(): Coroutine<T>`) wird nicht als Coroutine gelowert. ModuleLowerer.cs:273, InstanceTable.cs:177. [language-review HIGH]
3. Generisches Enum mit optionalem Typargument kaputt: `Option<?int>.isSome()` nicht gelowert (FunctionLowerer.cs:3914), `Option<?int>.Some(7)` Verifier-Absturz (IrVerifier.cs:291), `Some(v)` über `?U`-Payload zählt nicht als Abdeckung (TypeChecker.cs:4288). Blockiert Result/Option in der stdlib. [prototyper HIGH ×3]
4. `List<?T>`/`Map<K,?V>` nicht instanziierbar (IR0001 „??T“), Fehlerort in collections.lyr:493 statt im Nutzercode. [stdlib-review HIGH]
5. `if (b) 1 else panic()` und `x ?? panic()` → InternalCompilationException. FunctionLowerer.cs:1393. [prototyper, bereits Team-1-Fund]
6. Tupel-Match mit Enum-Teilmustern stürzt den Compiler ab (FunctionLowerer.cs:943); verschachtelte Varianten-Muster (:2588) und Tupel-Muster mit Literalen (:966) nicht gelowert; `Dial(_) | Hangup` als bindendes Or-Pattern abgelehnt (:2574). [language-review, prototyper]
7. `List<Shape>.push(Sq{..})` (Interface-Lift beim generischen Call) → IR-Verifier-Absturz. IrVerifier.cs:291. [language-review HIGH]
8. `println([1,2,3])`, `println(?int)`, `println(tuple)` passieren die Display-Constraint und scheitern im IR mit Fehlerort in console.lyr:51. [stdlib-review HIGH, language-review]
9. `parseInt("99999999999999999999")` liefert 7766279631452241919 statt null (string.lyr:426; json.lyr:768 macht es richtig); `powInt(2,64)` = 0 trotz `?int` (math.lyr:124). [stdlib-review]
10. `throws E` mit Typparameter wird an der Aufrufstelle nie substituiert, typisiertes catch deckt nie (ExceptionAnalyzer.cs:218-227). [prototyper MEDIUM]
11. Map wächst bei set/remove-Churn unbegrenzt (Tombstones lösen Verdopplung aus, nie Verdichtung). collections.lyr:519. [stdlib-review MEDIUM]
12. `extend Enum :: [Hashable<Enum>]` → IR0001 mit Fehlerort core.lyr:149 (TypeTable.cs:422); `trim()` Unicode vs. `trimStart/trimEnd/isBlank` ASCII (string.lyr:189-212); `entries<int, List<string>>(m)` scheitert an `>>` (Parser); `formatInt(-255,"X")` vs. `formatHex(-255)` inkonsistent; `absInt(int.min)` bleibt negativ → Random-Bereiche falsch; `return voidCall();` Absturz (FunctionLowerer.cs:1393); Guide 13 mit drei veralteten Aussagen; Doku-Kommentare „there is no overloading“.

## P1 — Minor-Bump (additiv, nicht-breaking), sortiert nach Nutzen/Aufwand
Sprache (alle mit Prototyp, Grammatik-Kollisionen sind gelöst):
1. f-Strings rendern Display-Typen (Prototyp 15) — Sema ~25 Z., bester Nutzen/Aufwand. §6.6, §11.
2. try trägt zur Definite Assignment bei (Prototyp 04a, Sema ~5 Z.); dann `try`-Ausdruck `try ['?'] UnaryExpr [catch (…) Block|Expr]` (04b). §6.2, §7.7, §9.3.
3. Konformanz-Synthese Equatable/Hashable/Ordered/Display durch Konformanz ohne Body (Swift-Modell, Prototyp 03) — keine Grammatikänderung, Sema+Lowering mittel-groß, 72→38 Zeilen. Belege: IoErrorKind/JsonValue/Wait sind heute nicht Equatable. §5.1, §11.
4. Block-Arm im match-Ausdruck mit Tail-Expression (Prototyp 05). §6.9, §7.6.
5. typed throws: `throws E` mit Typparameter, `never`, `fn(T)->U throws E` (Prototyp 06) — Grammatik parst es schon, nur Sema (~300 Z.). Ermöglicht `assertThrows`, `rethrows`-Muster. §9.2, §10, §8.3.
6. while-let > let-else > if-let (Prototyp 02; let-else nach if-Ausdruck braucht Klammern). §7.1, §7.4.
7. `throw`/`panic` als never-Ausdruck (Prototyp 08, Sema hat NeverType bereits). §6.2, §9.4.
8. `variantName()` auf Enums + `static let` in Enums + statische Interface-Anforderungen (Prototypen 07, 13). §3.4, §5.
9. Labels für break/continue in Go-Form `outer:` (Prototyp 11, ~100 Z.); Tupel-Destructuring im for-Kopf und `((a,b)) =>` in Lambdas (Prototyp 14, ~90 Z.). §5, §7.2.
10. `?T == ?T` erlauben (Prototyp 16a); Raw-/Mehrzeilen-Strings (Prototyp 19, Lexer ~80 Z., §1.6); named arguments mit `:` (Prototyp 12); Slices erst als stdlib `slice(from,to)`, dann `xs[a..b]` mit offenen Grenzen (Prototyp 09, §3.3); Struct-Update `..base` und `t.1` eine Ebene (Prototyp 20, niedrig); Neg/Rem-Interfaces und Compound auf Feldern (Prototyp 22).
stdlib (stdlib-review Top-10):
11. Terminatoren (`count/any/all/find/fold/first/last/nth/toArray`) als Default-Methoden auf `Iterator<T>` + Adapter `skipWhile/stepBy/dedup/inspect` — Machbarkeit belegt (review-examples/terminators.lyr).
12. Container-Paket: `List.of/pushAll/slice/map/filter`, `Map.keys()/entries()/getOrInsert/update`, `Set.of/union`, `sortArray`, `listRemove`; `arrayOf(n, f)` mit einem Native (Prototyp 18).
13. Debug-Ausgabe (`showArray/showOptional`, Display für List/Set/Map/JsonValue/IoError); Zahlen (`intMax/intMin`, `checkedAdd/Mul`, `parseUint`); Strings (Unicode-`isLetter`, `removePrefix/Suffix`, `lines()`, `capitalize`, `reverse`).
14. Zeit (`Duration.ofHours/ofDays`, `Instant.minus`, `Date`-Struct, tolerantes `fromRfc3339`); Test (`assertNotEq/assertNull/assertClose/assertThrows/fail`, String-Diff); `std.hash` (sha256/crc32); Pfade/Dateien/Prozesse (`normalize/relative`, `walk/removeAll`, `process.output()`); JSON `path()`, `ToJson/FromJson`-Anker; `option.orElse`; `Random.fresh()` ohne Modulo-Bias.

## P2 — Major-Bump (breaking)
1. Nestbare Optionals oder stdlib-`Option<T>` (Prototyp 21, Lib-Variante läuft): Voraussetzung sind die P0-Bugs 3 und 4; Variante B (Option<T>) nur, wenn `Iterator.next()` der einzige Protokollpunkt ist; Variante A braucht einen Format-Major. §3.3, §6.3, §7.2, §10, §11.
2. `Result<T,E>` in `std.result` als eine Antwortform statt ~45 OrThrow-Zwillinge und 4× kopiertem `lastErrorKind`-Globalzustand (Prototyp 01: stdlib deckt 80 %, Sprachteil ist der try-Ausdruck). 
3. Member-Sichtbarkeit durchsetzen, Felder `pub`, mit 4.x-Deprecation-Uhr wie SEM0093 (Prototyp 17 + stdlib-field-visibility.md mit Klassifikation aller stdlib-Felder). §4.2, Grammatik §3.2/§3.3.
4. Shadowing-Regel in §7.1 festlegen (Empfehlung Rust-Shadowing; heute Bug P0-1).
5. f-String-Formatsprache spec-fixiert nach Python/Rust statt .NET; Ausrichtungsvorzeichen ist heute gegenüber printf/C#/Rust invertiert (fmt.lyr:6-8). §1.7, §6.6, §11.
6. stdlib: Überladung statt Typ-Suffix-Namen (`abs/absInt`, `sum/sumFloat`, `formatInt/formatFloat`) mit Deprecated-Stufe; Zeit/Sleep/Monotonic nach `std.time`; Panik-Konvention in §11 verankern (`split("")`, ungültiger Format-Spec → `?T`); `Wait` non-exhaustive + `Channel<T>`, `join`, `timeout`, `sleep(ms)`; `Iterator.next()` mit typed throws bzw. Option-Protokoll.

## Was Lyric besser macht (9 strength-Einträge)
- Geprüfte throws-Klauseln mit null Zeichen pro Aufruf und typisierter Catch-Abdeckung (schlägt Go/Kotlin/C#/TS, gleichauf mit Zig).
- Stackful Coroutinen ohne Function Coloring; Warten ohne `async` in Signaturen, Ctrl+C/`interrupt()` als ein Mechanismus.
- Soundes Flow-Narrowing mit frühem Exit und `&&`-Propagation plus Definite Assignment.
- Operator = Interface-Methode mit Mehrfachkonformanz über den rechten Operanden und Indexable.
- Deterministische Numerik (Wrapping, Shift-Maskierung, sättigend, Literal-Adaption nur für Literale).
- Code-Point-Semantik konsequent („Grüße 👋 Welt“ hat Länge 12, JS/C# sagen 13).
- Dokumentierte Antwortformen (`?T` lesen, `bool` operieren, `OrThrow` für den Grund), Capabilities pro Modul, JSON mit exakten int64 und Tiefenlimit, stabile Sortierung.
- Attribute als Structs mit Enum-Vokabular und Deprecation-Uhr; keine Nullreferenzen, keine Vererbung, keine impliziten Konvertierungen.

## Vergleichsmatrix (Kurzfassung, − = Lyric schlechter, + = Lyric besser)
- Fehlermodell: + vs. Go/Kotlin/C#/TS/Python, = Rust/Swift/Zig.
- Null-Sicherheit: − vs. Rust/Swift/Zig (nestbar, if-let), = Kotlin/TS, + C#/Go/Python.
- Pattern Matching: −− vs. Rust/Swift, − Python/C#, = Kotlin, + Go/Zig/TS.
- Generics/Traits: − vs. Rust/Swift (assoziierte Typen, Statics, derive), = Kotlin/C#, + Go/Zig.
- Closures/Iteratoren/Comprehensions: − vs. Rust/Kotlin/Swift/Python/C#, = Go/Zig.
- String-Interpolation: − vs. allen außer Zig (Display, Raw-Strings, spezifizierte Mini-Sprache).
- async/Coroutinen: + vs. allen außer Go (=).
- Member-Sichtbarkeit: − vs. allen acht.
- Metaprogrammierung: −− vs. Rust/Zig, − andere; dafür host-lesbare Attribute.
- Wertsemantik, Tooling (Doc/Tests/Deprecation): = bis +.

## Feature-QOL-Noten (language-review, 1-5)
Shadowing 1 · f-Strings 2 · Optional 3 · Arrays 3 · Tupel 3 · Enums 3 · Funktionen 3 · Kontrollfluss 3 · Module 3 · Primitive 4 · Literal-Adaption 4 · Structs/Classes 4 · Interfaces 4 · Generics 4 · Aliase/opaque 4 · Fehler 4 · Coroutinen 4 · Attribute 4 · Operatoren 4 · Narrowing/DA 4.

## stdlib-Reifegrad (stdlib-review, 1-5)
encoding 5 · core/option/json/os/io.console/io.error/io.file/io.stream/io.net 4 · string/collections/iter/math/fmt/time/random/process/task/build 3 · bytes/io.path/test 2. Tests: 166/166 grün, aber 0 Tests für core/option/fmt/os/console/test.

## Ablage
- TASKLIST.md (116 Einträge, alle mit Datei:Zeile, Lyric-Beispiel, Vergleichssprache)
- prototypes/01…22 (ist.lyr / soll.lyr / vergleich.* / README.md), prototypes/stdlib-field-visibility.md
- Worktrees: agent-a209c77ec010ebe12 (review-examples/), agent-a6f23b914e4110e71 (review/), agent-a53ed9674626249bb — nichts committet.
