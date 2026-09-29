# Lyric Usability & Evolution Review — gemeinsame Taskliste

Format pro Eintrag (ein Block, mit `flock` anhängen, nie den Inhalt anderer umschreiben):

### [<agent>] <kurzer Titel>
- Kategorie: shortcoming | bug | qol | strength | proposal-minor | proposal-major | prototype
- Priorität: HIGH | MEDIUM | LOW   (Nutzen für Anwender × Häufigkeit; bei bug: Schwere)
- Intern (Lyric): <pfad relativ zum Repo>:<zeile> — mit Lyric-Beispiel (Codeblock)
- Extern (Vergleich): <Sprache(n)> — Erklärung und Beispiel in der verglichenen Sprache (Codeblock)
- Beschreibung: was fehlt / stört / ist besser; bei Vorschlägen: Syntax-Skizze, Semantik, Spec-Kapitel, das sich ändert, Breaking (ja/nein) → Minor oder Major
- Prototyp: <pfad zur Prototyp-Datei oder "-">
- Betroffene Teammitglieder informiert: <namen oder "-">


### [prototyper] Prototyp 01: Result-Typ / `?`-Operator vs. throws
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): docs/guide/10-errors.md (throws), lyricspec §9.2; Parser: src/Lyric.Frontend/Parsing/Parser.Statements.cs:39 (`try` nur als Statement), Lexer.cs:950 (`?.` längster Match → postfix `?` kollidiert). Ist-Beispiel: throws propagiert implizit; Fehler-als-Wert braucht Hand-Enum + try/catch-Wrapper:
  ```lyr
  enum PortResult { Ok(int), Err(ParseError), }
  fn tryParse(line: string): PortResult {
      try { return PortResult.Ok(parseAndDouble(line)); }
      catch (e: ParseError) { return PortResult.Err(e); }
  }
  ```
- Extern (Vergleich): Rust — `Result<T,E>` ist ein Wert, `?` propagiert:
  ```rust
  fn parse_and_double(line: &str) -> Result<i64, ParseError> { Ok(parse_port(line)? * 2) }
  let results: Vec<Result<i64, ParseError>> = inputs.iter().map(|l| parse_and_double(l)).collect();
  ```
- Beschreibung: Befund: `throws` IST bereits das `?` (Propagation implizit, 0 Zeichen pro Aufruf). Was fehlt, ist der Fehler ALS WERT. Lib-Variante `Result<T,E>` läuft heute (46 Z.), aber ohne Sprachhilfe unhandlich (Kombinatoren nur als freie Funktionen, volle Typargumente bei jeder Konstruktion, `throw` kein Ausdruck). Soll: `std.result` (stdlib) + `try <Expr>` als AUSDRUCK vom Typ `Result<T,E>` (E aus der throws-Klausel). Grammatik: `Primary += 'try' UnaryExpr` — eindeutig, da `try` heute nur `try {` beginnt. Postfix `?` verworfen (Lexer-Kollision mit `?.`). Zeilen Ist 41 / Lib 46 / Soll 36 / Rust 30. Aufwand: Parser klein, Sema mittel (~40 Z.), Lowering = bestehendes try/catch-Desugar, VM 0, stdlib neu (~60 Z.). Breaking: nein (Minor). Empfehlung: stdlib reicht zu 80 %, `try`-Ausdruck als kleines Minor-Feature obendrauf.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/01-result-type-try-operator/
- Betroffene Teammitglieder informiert: stdlib-review (std.result-Kandidat), language-review (throw als Ausdruck, generische throws-Klausel)

### [prototyper] throws-Klausel mit Typparameter wird an der Aufrufstelle nicht substituiert
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:218-227 (`ThrownOf`: Typparameter → "any"/Throwable). Repro (prototypes/01-result-type-try-operator/orthrow-test.lyr):
  ```lyr
  enum Result<T, E :: [Throwable]> { Ok(T), Err(E);
      fn orThrow(): T throws E { match (this) { Ok(v) => { return v; } Err(e) => { throw e; } } } }
  let r: Result<int, Exception> = Result.Err(Exception { text = "boom" });
  try { return r.orThrow(); } catch (e: Exception) { return 0; }
  // → error[LYR-SEM0034]: call to 'orThrow' may throw 'Throwable', which nothing handles
  ```
- Extern (Vergleich): Java — `<E extends Exception> T orThrow() throws E` wird am Aufruf mit dem konkreten E geprüft; ein `catch (IOException e)` deckt `orThrow()` auf `Result<T, IOException>` ab.
- Beschreibung: `E = Exception` ist an der Aufrufstelle bekannt (Monomorphisierung), aber der ExceptionAnalyzer arbeitet auf der unsubstituierten Deklaration und stuft `throws E` als `Throwable` ein. Folge: ein typisiertes `catch` deckt den Aufruf nie ab; nur `catch (e)` oder bloßes `throws` hilft. Kein Crash, aber macht generische throws-Klauseln (Result.orThrow, generische Wrapper) praktisch unbenutzbar. Spec §9.2 sagt nichts über Typparameter in throws-Klauseln — Lücke auch in der Spec.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/01-result-type-try-operator/orthrow-test.lyr
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] `trim()` und `trimStart()/trimEnd()/isBlank()` haben zwei verschiedene Whitespace-Begriffe
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/string.lyr:189 (`trim` → nativer .NET `Trim()`, Unicode-Whitespace), stdlib/std/string.lyr:194-212 (`trimStart`/`trimEnd` über `isWhitespace`, nur `' '`, `\t`, `\n`, `\r`), stdlib/std/string.lyr:175 (`isBlank`), src/Lyric.Vm/NativeRegistry.cs:480-481
  ```lyr
  let nb = "\u{00A0}x\u{00A0}";
  println(f"trim=[{nb.trim()}] trimStart=[{nb.trimStart()}] trimEnd=[{nb.trimEnd()}] isBlank={"\u{00A0}".isBlank()}");
  // Ausgabe: trim=[x] trimStart=[ x ] trimEnd=[ x ] isBlank=false
  ```
  Repro: review-examples/strings.lyr
- Extern (Vergleich): Rust/Python/Kotlin/C# — `trim`, `trim_start`, `trim_end` benutzen in jeder dieser Sprachen DENSELBEN Whitespace-Begriff (Unicode White_Space).
  ```rust
  "\u{a0}x\u{a0}".trim_start() // "x\u{a0}" — konsistent mit trim()
  ```
- Beschreibung: `s.trim()` entfernt NBSP, Formfeed, vertikalen Tab usw. (weil nativ .NET `Trim()`), `s.trimStart()`/`s.trimEnd()`/`s.isBlank()` nicht. Damit gilt `s.trim() != s.trimStart().trimEnd()`. Doku-Kommentar von `trim` sagt nur „whitespace“. Fix (nicht-breaking, Minor): entweder `trimStart`/`trimEnd` nativ machen (.NET `TrimStart()`/`TrimEnd()`) oder `isWhitespace` auf Unicode White_Space erweitern und dokumentieren; `isBlank` folgt dann automatisch. Test in stdlib-tests/tests/string_tests.lyr fehlt für Nicht-ASCII-Whitespace.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `parseInt` überläuft still statt `null` zu liefern
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): stdlib/std/string.lyr:426-455 (`parseInt`), stdlib/std/string.lyr:458 (`parseIntRadix`, gleiches Problem)
  ```lyr
  println(parseInt("99999999999999999999") ?? -1);   // 7766279631452241919 — kein null
  ```
  Repro: review-examples/strings.lyr. Gegenbeispiel im eigenen Haus: stdlib/std/json.lyr:768 (`intFits`) prüft die Grenze korrekt, `std.json` liefert für dieselbe Zahl `Float`.
- Extern (Vergleich): Rust `"99999999999999999999".parse::<i64>()` → `Err(PosOverflow)`; Go `strconv.ParseInt` → `ErrRange`; C# `long.TryParse` → `false`; Kotlin `toLongOrNull()` → `null`; Python → beliebig groß.
- Beschreibung: Der Doku-Kommentar räumt es ein („No overflow protection … Detecting it would need a division per digit“). Das Argument trägt nicht: ein Vergleich `result > (9223372036854775807 - digit) / 10` vor jedem Schritt ist EINE Division pro Ziffer, die Funktion ist ohnehin O(n) und `std.json` macht es bereits richtig. Ein `?int`, dessen Vertrag „unparsable → null“ lautet, das aber für gültig aussehende Eingaben eine falsche Zahl liefert, ist ein Bug im Sinne der eigenen Konvention („a failure the caller has to handle belongs in the type“). Fix: Overflow-Prüfung → `null` (Minor, nicht-breaking; nur Programme, die auf das Wrapping bauen, ändern sich). Test fehlt.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `List<?T>` und `Map<K, ?V>` sind nicht instanziierbar — Fehler zeigt in die stdlib
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): stdlib/std/collections.lyr:41-44 (`data: (?T)[]`), stdlib/std/collections.lyr:493 (`let valueHole: ?V = null;`), stdlib/std/collections.lyr:56
  ```lyr
  let l = List<?int>.empty();          // error[LYR-IR0001]: a nested optional '??T' — gemeldet in
                                        // …/stdlib/std/collections.lyr:493, nicht in der Nutzerdatei
  let m = Map<string, ?int>.empty();   // dito
  ```
  Repro: review-examples/map_optional.lyr
- Extern (Vergleich): Rust `Vec<Option<i32>>`, Kotlin `MutableList<Int?>`, Swift `[Int?]`, C# `List<int?>`, TS `(number | null)[]` — überall selbstverständlich (Lückentabellen, Sparse-Spalten, „noch nicht berechnet“-Slots).
  ```kotlin
  val slots: MutableList<Int?> = mutableListOf(null, 5)
  ```
- Beschreibung: Weil `?` nicht nestet, kann kein Container der stdlib einen optionalen Elementtyp tragen: die Backing-Arrays `(?T)[]` kollabieren, `first()`/`pop()`/`get(k)` könnten „leer“ und „null-Wert“ nicht unterscheiden. Ein `T[]` mit `(?int)[]` geht (Literal), eine `List` nicht. Das ist ein Sprachfeature-Loch (kein `Option<Option<T>>`), das die stdlib-Ergonomie direkt begrenzt. Workaround heute: eigener Wrapper-Struct `struct Slot { value: ?int }`. Vorschlag (Minor, stdlib-seitig): (a) Diagnose an die Instanziierungsstelle im Nutzercode statt in `collections.lyr:493` (Diagnostics); (b) `List<T>` intern mit Sentinel + `T[]`-Backing (braucht `default(T)` oder einen `Slot<T>`-Struct) statt `(?T)[]`, dann sind `List<?T>` und `Map<K, ?V>` möglich, wobei `pop()`/`first()`/`get()` als `?T` dann für `T = ?U` weiterhin nicht „leer“ von „null“ trennen — deshalb (c, Major/Sprache): ein nestbares Optional oder ein `Option<T>`-Enum in `std.option` als Ausweichtyp.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review, diagnostics

### [stdlib-review] Expliziter Generik-Aufruf mit verschachteltem Typargument `f<A, B<C>>(x)` ist ein Parse-Fehler (`>>`)
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/collections.lyr:1047 (`entries<K, V>` — der einzige Weg, ein `Map<int, List<string>>` zu iterieren, braucht bei expliziten Typargumenten genau diese Form)
  ```lyr
  let it = entries<int, List<string>>(groups);   // LYR-SEM0052/PAR0002/PAR0016: '>>' wird als Shift gelesen
  let ok = entries<int, List<string> >(groups);  // kompiliert
  let ok2 = entries(groups);                      // kompiliert (Inferenz)
  ```
  Repro: review-examples/nested_generic.lyr
- Extern (Vergleich): C#, Java, Rust, Kotlin, TS lösen `>>` in Typargument-Kontext seit jeher auf (`Map<int, List<string>>` ist in jedem dieser Sprachen gültig).
- Beschreibung: In Typ-Position (`Map<int, List<string>>.empty()`) funktioniert es, in Ausdrucks-Position (expliziter Generik-Aufruf) nicht. Die stdlib-Doku (`docs/guide/13-standard-library.md`, Abschnitt „Iterators chain“) empfiehlt explizite Typargumente (`map<int>(…)`), was Nutzer in diese Falle führt. Betrifft den Parser, nicht die stdlib.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review, lexer-parser

### [prototyper] Prototyp 02: if-let / let-else / while-let
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §7.4 (Narrowing nur für Identifier), §7.6; Parser: src/Lyric.Frontend/Parsing/Parser.Statements.cs:66-77 (`let` nimmt nur IDENT|TuplePattern), :121/:137 (if/while-Bedingung ist `Expr`). Ist-Beispiel — Feld narrowt nicht, Pull steht zweimal:
  ```lyr
  let p = u.profile; if (p == null) { return "no profile"; }
  var v = q.popFront(); while (v != null) { sum += v; v = q.popFront(); }
  match (s) { Circle(r) => { return r; } _ => { } }
  ```
- Extern (Vergleich): Swift — `guard let p = u.profile else { return "no profile" }`, `while let v = q.pop() { … }`, `if case .circle(let r) = s { return r }`.
- Beschreibung: Für ein LOKALES Optional deckt der frühe Exit (`if (x == null) { return; }`, §7.4) let-else schon ab. Echte Lücken: (1) Felder/Aufrufe narrowen nicht → Kopie + if; (2) Enum-Payload ohne vollständiges match → leerer `_`-Arm; (3) Pull-Schleifen → Pull vor und am Ende (Endlosschleifen-Falle). Soll: `Condition = Expr | 'let' Pattern '=' Expr` in if/while und `BindingStmt … [ 'else' Block ]`. Eindeutig, weil `let` keinen Expr beginnen kann; let-else nach if-Ausdruck ist parsebar (IfExpr verlangt sein else), sollte aber Klammern verlangen. Zeilen Ist 52 / Soll 38 / Swift 34. Aufwand: Parser klein, Sema mittel (Refutability existiert seit 4.4, Exit-Prüfung existiert), Lowering = Desugar in match (M36-Lowering über ?E reicht), VM 0. Breaking nein (Minor). Empfehlung: Sprachfeature, Priorität while-let > let-else > if-let.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/02-if-let-let-else/
- Betroffene Teammitglieder informiert: language-review

### [language-review] Generische Coroutine-Funktion wird nicht als Coroutine gelowert
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/ModuleLowerer.cs:273 (CoroutineYield nur für nicht-generische Deklarationen), src/Lyric.Frontend/Ir/Lowering/InstanceTable.cs:177 (Instanzen gehen immer durch FunctionLowerer, nie durch CoroutineFactory). Spec ~/dev/projects/lyricspec/spec/10-coroutines.md:76 ("A function whose return type is Coroutine<T> is a coroutine") schließt Generics nicht aus. Repro: review/probes/co_generic_int.lyr, review/pipeline.lyr
  ```lyr
  fn pass<T>(c: Coroutine<T>): Coroutine<T> {
      while (true) { let v = c.next(); if (v == null) { return; } yield v; }
  }
  fn main(): int { let p = pass<int>(src()); ... }   // Laufzeit: panic [LYR-VM0013]: yield outside a running resume in 'main.main'
  ```
  Mit `Coroutine<T> throws Exception` als Parameter/Return stattdessen Compiler-Absturz: `InternalCompilationException: ir-verifier: malformed IR ... 'ret' carries no value` (IrVerifier.cs:291).
- Extern (Vergleich): Python/Kotlin/C# — generische Generatoren sind selbstverständlich (`def passthrough(it: Iterator[T]) -> Iterator[T]: for v in it: yield v`).
- Beschreibung: Jede generische Pipeline-Stufe (`where<T>`, `batches<T>`, `mapCo<T,U>`) ist damit unmöglich; die Sema meldet nichts, der Fehler kommt als Laufzeit-Panik im FALSCHEN Frame (main) oder als Compiler-Absturz. Sema müsste die Instanz als Coroutine registrieren (CoroutineTable.Register) statt FunctionLowerer.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, ir-codegen

### [language-review] Tupel-Pattern mit Enum-Varianten-Teilmustern stürzt den Compiler ab
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:943 (BindTupleElements: "'Idle' in a destructuring was not bound by the type checker" → InternalCompilationException) im match-AUSDRUCK; im match-STATEMENT stattdessen LYR-IR0001 mit falschem Text "this pattern in a destructuring binding". Spec ~/dev/projects/lyricspec/spec/02-grammar.md:522-533 erlaubt `TuplePattern` mit beliebigen Sub-Patterns, ~/dev/projects/lyricspec/spec/07-statements.md:243-247 ebenso. Repro: review/fsm.lyr (Kommentar in `step`)
  ```lyr
  let next: State = match ((this.state, ev)) {
      (Idle, Dial(host)) => State.Connecting { attempt = 1 },
      (Connecting { attempt }, Timeout) if attempt < 3 => State.Connecting { attempt = attempt + 1 },
      (Connected(h), Hangup) => State.Closed { reason = "hangup" },
      _ => this.state,
  };
  ```
- Extern (Vergleich): Rust — `match (state, event) { (State::Idle, Event::Dial(h)) => ..., (State::Connecting{attempt}, Event::Timeout) if attempt < 3 => ... }` ist DAS Idiom für Zustandsmaschinen; Swift `switch (state, event)` ebenso.
- Beschreibung: Der Zustandsmaschinen-Fall zwingt zu verschachtelten Matches (fsm.lyr: 4 Ebenen statt 1 Tabelle). Sema akzeptiert das Pattern (keine Diagnose), Lowering bindet Tupelelemente nur als Namen. Entweder Lowering ergänzen (Tupel-Element gegen Sub-Pattern testen) oder Sema muss es diagnostizieren.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, ir-codegen

### [language-review] `return voidCall();` in einer void-Funktion stürzt das Lowering ab
- Kategorie: bug
- Priorität: LOW
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1393 ("expression ... produced no value"), Sema akzeptiert (TypeChecker return-Prüfung). Repro: review/fsm.lyr Kommentar bei `finish`.
  ```lyr
  mut fn finish(...): void { ... }
  mut fn step(ev: Event): void { ... return this.finish(before, ev, next); }   // Crash statt Diagnose oder Akzeptanz
  ```
- Extern (Vergleich): TypeScript/C# erlauben `return voidCall();` in void-Funktionen; Rust/Go nicht — beide Wege sind ok, ein Absturz nicht.
- Beschreibung: Entweder erlauben (TS-Semantik) oder LYR-SEM-Fehler; Spec ~/dev/projects/lyricspec/spec/07-statements.md:172 schweigt.
- Prototyp: -
- Betroffene Teammitglieder informiert: ir-codegen

### [language-review] try/catch trägt nichts zur Definite Assignment bei, auch wenn jeder catch die Funktion verlässt
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/FlowAnalyzer.cs:129-137 (`case TryStmt` gibt unverändert `assigned` zurück), Spec ~/dev/projects/lyricspec/spec/07-statements.md:303 ("A try contributes nothing afterwards"). Beispiel review/cli.lyr (Kommentar in `main`):
  ```lyr
  var opts: Options;
  try { opts = parseArgs(args); }
  catch (e: UsageError) { eprintln(e.message()); return 2; }
  let limit = opts.limit ?? ...;   // error[LYR-SEM0018]: use of possibly unassigned variable 'opts'
  ```
  Umgehung: Helferfunktion `parseOrReport(): ?Options` (+7 Zeilen) oder der ganze Rest der Funktion wandert in den try-Block.
- Extern (Vergleich): C# (definite assignment: nach `try{x=..}catch{return;}` ist x zugewiesen), Swift (`do { x = try f() } catch { return }`), Kotlin (`val x = try { f() } catch (e: E) { return }` — try als Ausdruck).
- Beschreibung: Regel verfeinern: Wenn JEDER catch-Zweig immer verlässt, zählt das, was der try-Body am Ende zugewiesen hat (nur der normale Abschluss des Bodys erreicht die Fortsetzung). Additiv, keine Programme brechen. Weitergehend: `try` als Ausdruck (`let opts = try parseArgs(args) catch (e: UsageError) { return 2; };`). Spec §7.7 + §6.9. Minor.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] `throw` ist kein Ausdruck (kein `never`-Typ in Ausdrucksposition)
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.Statements.cs:38,224 (`throw` nur als Statement), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:283 `ThrowStmt`. Beispiel review/tokenizer.lyr:142-143:
  ```lyr
  return match (t) {
      Num(n) => Expr.Lit(n),
      _ => throw ParseError { ... },          // LYR-PAR0002 + Kaskade von 30 Folgefehlern
      _ => { throw ParseError { ... }; }      // Umgehung: Block-Arm
  };
  let v = env.get(name) ?? throw EvalError { ... };   // ebenso unmöglich; heute 3 Zeilen if/throw
  ```
- Extern (Vergleich): Kotlin (`val v = map[k] ?: throw IllegalStateException()`), Rust (`panic!`/`return Err` sind `!`-typisiert), C# 7 (throw-Ausdrücke in `??` und `?:`), Swift (`fatalError()` ist `Never`).
- Beschreibung: `throw Expr` als Ausdruck vom Typ `never`, der in jede Unifikation passt (match-Arm, `??`-Rechte, if-else-Ausdruck). Gleiches für `panic(...)` (heute `void`, §9.4 sagt "returns never" — Widerspruch zwischen Spec-Text und Typ). Spec §6.2 Primary, §6.9, §9.4. Minor, additiv.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper (hat es unabhängig gefunden)

### [language-review] Ein Block-Arm im match-Ausdruck liefert keinen Wert; `return` verlässt die Funktion
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:4155-4159 (LYR-SEM0033 "blocks have no value"), Spec ~/dev/projects/lyricspec/spec/06-operators.md:109-117, ~/dev/projects/lyricspec/spec/02-grammar.md:312 ("An arm whose body is a block may omit it"). Beispiel review/fsm.lyr (Kommentar über dem `Data(n)`-Arm):
  ```lyr
  let next: State = match ((this.state, ev)) {
      (Connected(h), Data(n)) => {
          this.received = this.received + n;
          return State.Connected(h);      // error: cannot assign 'State' to 'void'  (return verlässt step()!)
      }
  };
  ```
  Die Diagnose nennt die Ursache nicht; Umgehung: Seiteneffekt in ein zweites match-Statement heben oder Helfermethode.
- Extern (Vergleich): Rust (Block ist Ausdruck, Tail-Expression `{ self.received += n; State::Connected(h) }`), Kotlin (`when` + Lambda-Block: letzter Ausdruck ist Wert), Swift (`switch`-Case mit mehreren Statements + `return` innerhalb einer Closure).
- Beschreibung: Vorschlag A (Minor): Block-Ausdruck mit Tail-Expression — ein Block, dessen letzte Anweisung ein Ausdruck OHNE `;` ist, hat dessen Wert (nur in Arm-/Lambda-Position, um `Name { }`-Mehrdeutigkeit zu vermeiden). Vorschlag B: `=> { ...; break value; }`. Betrifft §6.9, §7.6, Grammatik `MatchArm`. Zusätzlich: Diagnose sollte sagen "return inside a block arm leaves the enclosing function".
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Keine Polymorphie über Werfbarkeit (kein `rethrows`, kein `throws E` mit Typparameter)
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/10-coroutines.md:173-189 (Werfbarkeit ist Teil des Coroutine-Typs, Zuweisung einseitig), ~/dev/projects/lyricspec/spec/09-errors.md:41-47; src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:218-227 (Typparameter in `throws E` wird nicht substituiert, laut prototyper). Beispiel review/pipeline.lyr:29-42:
  ```lyr
  fn where<T>(input: Coroutine<T>, keep: fn(T) -> bool): Coroutine<T> { ... }
  where(parsed(src), ...)   // error: cannot assign 'Coroutine<(int, int)> throws Exception' to 'Coroutine<(int, int)>'
  // Umgehung: JEDE Stufe deklariert `Coroutine<T> throws Exception` -> auch nicht-werfende Quellen werden werfend,
  // und `Iterator<T>.next()` kann nicht werfen, also muss ein Adapter die Exception verschlucken (pipeline.lyr:57-67).
  ```
  Gleiches Problem bei `fn map<T,U>(xs: T[], f: fn(T) -> U)`: ein werfendes Lambda passt nicht in `fn(T) -> U`.
- Extern (Vergleich): Swift (`rethrows`, seit 5.x typed throws `throws(E)` generisch: `func map<E>(_ f: (T) throws(E) -> U) throws(E)`), Rust (Result<T,E> ist generisch über E, kein Sonderfall), Kotlin (ungeprüft, daher kein Problem).
- Beschreibung: Vorschlag (Minor, additiv): `throws E` mit Typparameter E in Signatur und Funktionstyp erlauben (`fn(T) -> U throws E`), Inferenz von E aus dem Argument (Konstante "nichts" als leeres Throws), `Coroutine<T> throws E` generisch. Alternativ Swift-`rethrows` als Kurzform. Betrifft §9.2, §10, §8.3 (Inferenz), Grammatik `FunctionType`.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, stdlib-review (Iterator.next() ohne throws)

### [language-review] Enums haben keinen abgeleiteten Namen/Display — Varianten-Namen werden dreimal von Hand geschrieben
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:89-95 (f-String rendert nur Skalare), ~/dev/projects/lyricspec/spec/04-modules.md:327ff (Attribute, kein Derive). Beispiel review/fsm.lyr:25-43 (`stateName`, `eventName`), review/tokenizer.lyr:21-29 (`tokenName`), review/cli.lyr:20-27 + 97 (`parseMode` + Rückrichtung):
  ```lyr
  fn eventName(e: Event): string {
      return match (e) { Dial(h) => f"Dial({h})", Ack => "Ack", Timeout => "Timeout", Data(n) => f"Data({n})", Hangup => "Hangup" };
  }   // 8 Zeilen pro Enum, in jedem Programm; ändert sich das Enum, veraltet die Funktion still
  ```
- Extern (Vergleich): Rust `#[derive(Debug)]` → `format!("{:?}", ev)`; Kotlin `ev.name`/`toString()`; Swift `String(describing:)`; C# `enum.ToString()`; Zig `@tagName(ev)`; Python `Enum.name`.
- Beschreibung: Vorschlag (Minor): (a) eingebautes Member `variantName()` (oder `.name`) auf jedem Enum-Wert, wie `length` auf Arrays — kein Interface nötig; (b) `@Derive(Display, Equatable, Hashable)` als compilergelesenes Attribut (Spec §4.7 nennt @Deprecated als einziges; die Menge "wächst durch Entscheidung"). Betrifft §3.4, §4.7, §6.6.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Kein Equatable/Hashable/Ordered auf Enums und Structs ohne Handarbeit (kein Derive)
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:52-58 (`==` verlangt Konformanz), ~/dev/projects/lyricspec/spec/05-interfaces.md:398-407. Beispiel review/tokenizer.lyr:92-98 (`expect` vergleicht Tokens über `tokenName`, weil `got == want` LYR-SEM0003 ist), review/container.lyr:56-83 (Task: `compare`, `equals`, `show` — 16 Zeilen für einen 2-Felder-Struct; Money: nochmal 3 Methoden):
  ```lyr
  struct Task :: [Ordered<Task>, Equatable<Task>, Display] {
      priority: int, name: string,
      fn compare(other: Task): int { if (this.priority != other.priority) { return if (this.priority < other.priority) -1 else 1; } return this.name.compare(other.name); }
      fn equals(other: Task): bool { return this.priority == other.priority && this.name == other.name; }
      fn show(): string { return f"[{this.priority}] {this.name}"; }
  }
  ```
  Ein Enum als Map-Schlüssel (`Map<Mode, int>`) ist ohne `hash()`/`equals` von Hand unmöglich; Equatable auf einem Enum mit Payload ist ein voller match.
- Extern (Vergleich): Rust `#[derive(PartialEq, Eq, Hash, PartialOrd, Ord, Debug)]`; Kotlin `data class`/`enum class` (equals/hashCode/toString automatisch); Swift (automatische `Equatable`/`Hashable`-Synthese bei Konformanzdeklaration); C# `record`; Zig `std.meta.eql`.
- Beschreibung: Vorschlag (Minor, additiv): Swift-Modell — deklariert ein Struct/Enum `:: [Equatable<Self>]` (bzw. Hashable, Ordered) und implementiert die Methode NICHT, synthetisiert der Compiler sie feldweise (alle Felder müssen konform sein, sonst LYR-SEM0020 wie heute). Für Unit-Enums immer möglich. Kein neues Schlüsselwort; Spec §5.1 bekommt einen Absatz "synthesized conformance". Alternativ `@Derive` (siehe Enum-Display-Eintrag).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, stdlib-review (Equatable/Hashable/Ordered/Display in std.core)

### [language-review] Keine Slices/Teilbereiche für Arrays und Strings
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/03-types.md:71-75 (Arrays, keine Slices; string nicht indexierbar), ~/dev/projects/lyricspec/spec/02-grammar.md:316-318 (RangeExpr nur im Schleifenkopf). Beispiel review/tokenizer.lyr:47-52, review/cli.lyr:36:
  ```lyr
  let start = i;
  while (i < cs.length && isAlpha(cs[i])) { i = i + 1; }
  var word = [' '] * (i - start);                 // Teilarray von Hand:
  for (k in start..i) { word[k - start] = cs[k]; } // 3 Zeilen + Dummy-Element
  let value = a.substring(7, a.length() - 7);     // (start, count) statt (from, to) — Rechnen beim Leser
  ```
- Extern (Vergleich): Rust `&cs[start..i]`, Python `cs[start:i]`, Go `cs[start:i]`, Kotlin `cs.slice(start until i)`, Swift `cs[start..<i]`, Zig `cs[start..i]`.
- Beschreibung: Vorschlag (Minor): Index mit Range `xs[a..b]` / `xs[a..=b]` als Kopie (Wertsemantik wie heute, keine View-Semantik nötig), auch `s.toChars()[a..b]`; Grammatik: `Postfix '[' RangeExpr ']'`. Wechselwirkung: RangeExpr bleibt kein Wert (§7.2), tritt nur in dieser Klammer und im Schleifenkopf auf. Betrifft §3.3, §6.1, Grammatik §6.2. Stdlib-Seite (`substringFrom`, `slice`) ist stdlib-review gemeldet.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, stdlib-review

### [language-review] Format-Spec-Sprache in f-Strings ist implementierungsdefiniert (.NET)
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:89-95 (§6.6 nennt nur `std.fmt.formatXxx(value, "spec")`), stdlib/std/fmt.lyr:6-8 ("The specifier language is .NET's ... passed to the runtime unchanged"), ~/dev/projects/lyricspec/spec/11-stdlib-contract.md:262-269 (fixiert nur fromFloat, nicht die Spezifizierer). Beispiel review/container.lyr:75:
  ```lyr
  fn show(): string { return f"{this.cents / 100}.{(this.cents % 100):02}"; }   // gibt "25.0 " statt "25.00" — `02` ist .NET-Custom-Format, gemeint war `D2`
  ```
- Extern (Vergleich): Python (PEP 3101 Format-Mini-Language ist Teil der Sprache: `{x:02d}`), Rust (`{:02}` in std::fmt, sprachdefiniert), Go (`%02d` in fmt, spezifiziert), Zig (`{d:0>2}`).
- Beschreibung: Eine zweite Implementierung (Spec-Anspruch, Kap. 2/12) kann Programme mit `{x:N2}` nicht konform ausführen. Vorschlag: minimale, in §6.6/§11 fixierte Mini-Sprache (Breite, Ausrichtung, Füllzeichen, Präzision, Basis, Vorzeichen) — additiv gegenüber heute, .NET-Spezifizierer bleiben als Superset erlaubt oder werden mit Deprecation-Uhr ersetzt.
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review

### [language-review] String-Methoden brauchen einen Import (`import std.string as strings;` in jedem Programm)
- Kategorie: qol
- Priorität: LOW
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/04-modules.md:175-179 (Extension-Methoden kommen mit dem Import; Idiom `import std.string as strings;`), stdlib/std/string.lyr:116ff (`extend string`). Jedes der 6 Review-Programme beginnt mit dieser Zeile; ohne sie: "'string' has no member 'trim'" ohne Hinweis auf den Import.
- Extern (Vergleich): Rust/Go/Kotlin/Swift/Python/C#: `s.trim()` ohne Import — String-Methoden sind Teil des Prelude/Kerntyps.
- Beschreibung: Vorschlag (Minor): `std.string`-Extensions (und `std.core`) als Prelude implizit sichtbar (wie `panic`/`Throwable` heute, §4.4); mindestens: Diagnose LYR-SEM0025/"no member" soll den fehlenden Import vorschlagen. Betrifft §4.2, §4.4.
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review, diagnostics

### [prototyper] Prototyp 03: Abgeleitete Konformanz (`derive` Equatable/Hashable/Ordered/Display)
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): stdlib/std/core.lyr:149-172 (die vier Interfaces; :156 "no compiler checks it" für hash≡equals); Parser src/Lyric.Frontend/Parsing/Parser.Declarations.cs:756 (`ParseInterfaceList`), :792 (`AtContextual`); Sema TypeChecker.cs:622-650 (Konformanzlisten-Prüfung). Ist-Beispiel — 4 Methoden, 30 Zeilen, dieselbe Feldliste viermal:
  ```lyr
  struct Coord :: [Hashable<Coord>, Ordered<Coord>, Display] { x: int, y: int, label: string,
      fn equals(other: Coord): bool { return this.x == other.x && this.y == other.y && this.label == other.label; }
      fn hash(): int { var h = this.x.hash(); h = h * 31 + this.y.hash(); h = h * 31 + this.label.hash(); return h; }
      fn compare(other: Coord): int { /* 3 ifs */ }  fn show(): string { /* f-String */ } }
  ```
- Extern (Vergleich): Rust — `#[derive(Debug, PartialEq, Eq, Hash, PartialOrd, Ord)] struct Coord { x: i64, y: i64, label: String }` (1 Zeile; Kotlin: `data class`).
- Beschreibung: Soll: `ConformanceEntry = [ 'derive' ] TypeExpr` in der `::`-Liste — `struct Coord :: [derive Hashable<Coord>, derive Ordered<Coord>, derive Display]`. `derive` kontextuell (wie `type`/`throws`), eindeutig, weil zwei Identifier ohne Komma in der Liste heute ein Parsefehler sind. `@Derive`-Attribut verworfen: Attribute "beschreiben und tun nichts" (Guide 15), und AttrArgs tragen keine Typen. Regeln: feldweise ==, kombinierter Hash (equal ⇒ equal hash per Bau), lexikografisch nach Felddeklaration, Display = Initializer-Syntax; Enum: Tag dann Payload. Keine Lib-Variante möglich (keine Feldaufzählung). Zeilen Ist 72 / Soll 38 / Rust 30. Aufwand: Parser klein, Sema mittel, Lowering mittel-groß (Synthese von 4 Methodenkörpern, ~250 Z.), VM 0. Breaking nein (Minor). Offene Fragen: implizite Constraint bei generischen Typen; `?T`-Felder (zwei Optionals vergleichen ist heute LYR-SEM0059 — derive braucht eigene Regel); Arrays als Felder refusen. Empfehlung: Sprachfeature, hoher Nutzen (beseitigt hash≠equals strukturell).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/03-derive-equatable-hashable-display/
- Betroffene Teammitglieder informiert: language-review; stdlib-review (Hinweis: `combineHash` als benannte Formel in std.core)

### [prototyper] Prototyp 04: try/catch und Definite Assignment; `try` als Ausdruck
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/FlowAnalyzer.cs:129-137 (`case TryStmt` → `return assigned`), Flow.cs:46 (`AlwaysExits` über try/catch existiert), Parser.Statements.cs:39 (`try` nur Statement), TypeChecker.cs:4155 (Block-Exit-Regel). Ist-Beispiel (prototypes/04/fails.lyr → LYR-SEM0018; ist.lyr = zwei Umwege):
  ```lyr
  fn parseOrReport(args: string[]): ?Options {            // Umweg A: +6 Zeilen, Grund geht verloren
      try { return parseArgs(args); } catch (e: UsageError) { println(e.message()); return null; } }
  let opts = parseOrReport(args); if (opts == null) { return 2; }
  ```
- Extern (Vergleich): C# — Definite Assignment zählt die Body-Zuweisung, wenn jeder catch verlässt (`Options opts; try { opts = Parse(); } catch (E e) { return 2; } use(opts);`). Kotlin — `val opts = try { parseArgs(args) } catch (e: UsageError) { println(e.message); return 2 }`; `runCatching { … }.getOrNull()` ≈ `try?`.
- Beschreibung: Stufe (a): Regel in §7.7 — ein try, dessen catch-Klauseln ALLE verlassen, trägt bei, was der Body am Ende zuweist; FlowAnalyzer ~5 Zeilen, Patch-fähig, additiv. Stufe (b), mit language-review vereinbart: `Primary += TryExpr = 'try' ['?'] UnaryExpr [ 'catch' '(' CatchBinding ')' ( Block | Expr ) ]`; `try`+`{` bleibt TryStmt, sonst TryExpr (heute PAR0017 → keine Kollision). `try e catch (x:E) {exit}` → T; `… catch (x:E) Expr` → Unifikation; `try? e` → ?T (auf ?T bleibt ?T, kein ??T). Ersetzt den Sprachteil von Prototyp 01 (Result bleibt reine stdlib). Zeilen "parse oder exit 2": natürlich 8 (abgelehnt) / Ist A 12 / Ist B 9 / Soll (b) 2 / Kotlin 2 / C# 6. Aufwand: (a) Sema 5 Z.; (b) Parser ~40, Sema ~80, Lowering ~60 (Desugar auf bestehende try/catch-Lowering), VM 0. Breaking nein. Empfehlung: (a) sofort, (b) Minor-Feature.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/04-try-catch-definite-assignment/
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] `Map` wächst bei set/remove-Churn unbegrenzt: Tombstones lösen eine VERDOPPELUNG aus, nie eine Verdichtung
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/collections.lyr:519-521 (`if ((this.used + 1) * 4 > this.keys.length * 3) { this.resize(this.keys.length * 2); }`), stdlib/std/collections.lyr:580-591 (`remove` hinterlässt Tombstone, `used` sinkt nie), stdlib/std/collections.lyr:593
  ```lyr
  let m = Map<int, int>.empty();
  for (i in range(0, 200000)) { m.set(i, i); m.remove(i); }   // ein Cache mit Ablauf, eine Session-Tabelle
  // danach: m.length() == 0, aber count(keys(m)) braucht 120 ms — die Tabelle hat ~262144 Slots
  ```
  Repro: review-examples/edge.lyr (Zeile „after churn“). Rechnung: jedes `remove` hinterlässt einen Tombstone, `used` zählt ihn; sobald `used` 3/4 der Kapazität erreicht, verdoppelt `set` die Kapazität — unabhängig von `count`. Nach n Churn-Zyklen liegt die Kapazität bei ~n·4/3, obwohl die Map leer ist; sie schrumpft nie.
- Extern (Vergleich): Rust `HashMap` (hashbrown) rehashed bei Tombstone-Druck IN PLACE („rehash_in_place“) und wächst nur, wenn die Zahl der LEBENDEN Einträge es verlangt; Go/.NET-Dictionary recyceln gelöschte Slots über eine Freelist. In keiner der Sprachen wächst eine leere Map durch Churn.
- Beschreibung: Ein langlebiger Prozess (Server-Sessions, Cache, Scheduler-Tabellen in `std.task` selbst) verliert Speicher und Iterationszeit proportional zur Zahl aller je eingefügten Schlüssel. Fix (nicht-breaking, Minor): in `set` bei Erreichen der Lastgrenze `resize(capacityFor(this.count))` — also Verdoppeln nur, wenn `count * 4 > keys.length * 3 / 2` (die Hälfte der Lastgrenze), sonst gleiche Kapazität (Tombstones verschwinden beim Reinsert ohnehin). Test für Churn in stdlib-tests/tests/collections_tests.lyr fehlt; ein `capacity()` wie bei `List` fehlt der `Map`, um es überhaupt zu messen.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `println(array)`, `println(optional)`, `println(tuple)` passieren Sema und scheitern im IR — mit Fehlerort in `console.lyr`
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): stdlib/std/io/console.lyr:47-52 (`pub fn println<T :: [Display]>(value: T)`), stdlib/std/core.lyr:88-121 (`Display` nur für int/float/bool/char/string)
  ```lyr
  let xs = [1, 2, 3];
  println(xs);          // KEIN Sema-Fehler; dann: …/stdlib/std/io/console.lyr:51:16: error[LYR-IR0001]: call to 'show' on 'int[]'
  let o: ?int = 5;
  println(o);           // dito: call to 'show' on '?int'
  println((1, "a"));    // dito: call to 'show' on '(int, string)'
  println(List<int>…);  // korrekt: error[LYR-SEM0028]: type 'List<int>' does not satisfy constraint 'Display'
  ```
  Repro: review-examples/display.lyr, display2.lyr, display3.lyr
- Extern (Vergleich): Rust `println!("{:?}", xs)` druckt `[1, 2, 3]`, `Some(5)`, `(1, "a")` für JEDEN Debug-Typ; Python/Kotlin/Swift/JS drucken jede Liste/Tuple/Option ohne Vorbereitung; C# `Console.WriteLine(xs)` gibt zumindest den Typnamen.
- Beschreibung: Zwei Befunde in einem. (1) Sema: die Constraint-Prüfung `T :: [Display]` akzeptiert Array-, Optional- und Tupeltypen, obwohl keine `extend`-Konformanz existiert — der Fehler taucht erst im IR auf und zeigt in eine Datei, die der Nutzer nicht geschrieben hat (Sema-Bug, semantic/diagnostics). (2) stdlib: es gibt KEINE Möglichkeit, eine Liste, ein Array, ein Optional oder ein Tupel zum Debuggen auszugeben — jeder Nutzer schreibt die `join(map(fromInt))`-Schleife selbst (review-examples/collections.lyr:10 braucht `join(collectArray(xs.iter().map((n) => fromInt(n))), ",")` für Rusts `{:?}`). Vorschlag (Minor): `extend`-Konformanzen für `T[]`, `?T`, `(A, B)` sind sprachlich nicht möglich (kein `extend` über Typkonstruktoren) → stattdessen `std.fmt.showArray<T :: [Display]>(xs: T[]): string`, `showList`, `showOptional`, plus `Display` für `List<T :: [Display]>`, `Set`, `Map`, `Deque`, `JsonValue`, `Duration` (hat es), `IoError`. Langfristig (Major/Sprache): ein `Debug`-artiges, synthetisiertes `show` für jeden Typ (Sprachfeature; language-review).
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review, semantic, diagnostics

### [stdlib-review] `powInt` überläuft still, obwohl der Rückgabetyp bereits `?int` ist
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/math.lyr:124-140
  ```lyr
  println(powInt(10, 19) ?? -1);   // -8446744073709551616
  println(powInt(2, 64) ?? -1);    // 0
  ```
  Repro: review-examples/numbers.lyr
- Extern (Vergleich): Rust `10_i64.checked_pow(19)` → `None`; Kotlin/C# haben keine Int-Potenz (nutzen `Math.pow` → double); Python beliebig groß.
- Beschreibung: Die Funktion liefert schon `?int` („negative exponent → null“). Ein Überlauf ist dieselbe Antwort („kein int-Ergebnis“), kostet in der Schleife nur einen Vergleich `result > MAX / b` und wäre die einzige Stelle in der stdlib, an der Int-Arithmetik geprüft ist — eine `checkedMul/checkedAdd`-Familie (Minor-Vorschlag, separater Eintrag) fehlt ganz.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `sinh`/`cosh`/`tanh`/`exp` über `pow(e, x)` sind ungenau
- Kategorie: bug
- Priorität: LOW
- Intern (Lyric): stdlib/std/math.lyr:213-216 (`exp(value) = pow(e, value)`), stdlib/std/math.lyr:246-262 (`sinh = (exp(x) - exp(-x)) / 2`)
  ```lyr
  println(sinh(1e-10));   // 1.000000082740371e-10 — richtig wäre 1.0000000000000000e-10 (relativer Fehler 8e-8)
  ```
  Repro: review-examples/numbers.lyr
- Extern (Vergleich): C# `Math.Sinh(1e-10)` = `1E-10`, Rust `f64::sinh`, Python `math.sinh` — alle korrekt gerundet, weil sie die libm-Funktionen nutzen, die für kleine |x| die Auslöschung vermeiden.
- Beschreibung: `exp(x) - exp(-x)` für kleine x ist katastrophale Auslöschung; `pow(e, x)` rundet zudem anders als `exp(x)`. Das Modul hat für `log2`/`log10` bereits genau dieses Argument benutzt („the derived form gave 2.9999999999999996“) und sie nativ gemacht — `exp`, `sinh`, `cosh`, `tanh` gehören aus demselben Grund nativ (`Math.Exp`, `Math.Sinh`, …). Kein Test deckt kleine Argumente ab.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `formatHex(-255)` und `formatInt(-255, "X")` liefern zwei verschiedene Darstellungen
- Kategorie: shortcoming
- Priorität: LOW
- Intern (Lyric): stdlib/std/fmt.lyr:33 (`formatInt` → .NET `X`: Zweierkomplement `FFFFFFFFFFFFFF01`), stdlib/std/fmt.lyr:98 (`formatHex` → Vorzeichen: `-ff`)
  ```lyr
  println(f"{formatInt(-255, "X")} {formatHex(-255)} {formatRadix(-255, 2)}");   // FFFFFFFFFFFFFF01 -ff -11111111
  ```
  Repro: review-examples/fmt.lyr
- Extern (Vergleich): Rust `{:x}` von `-255i64` → `ffffffffffffff01` (Bitmuster); Python `hex(-255)` → `-0xff` (Vorzeichen). Beide Konventionen existieren — aber nicht im selben Modul unter zwei Namen ohne Hinweis.
- Beschreibung: Zwei Funktionen desselben Moduls für „hex“ antworten für negative Werte verschieden; die Doku von `formatHex` („lower case, no prefix“) sagt nichts über das Vorzeichen. Minor: Doku-Satz in beiden; ggf. `formatHex` für Bitmuster (`formatBits`) ergänzen.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Doku-Kommentare behaupten „there is no overloading“ — seit 3.0 falsch
- Kategorie: shortcoming
- Priorität: LOW
- Intern (Lyric): stdlib/std/math.lyr:5-8 („there is no overloading. Cast with `as`“), stdlib/std/string.lyr:9-10, stdlib/std/iter.lyr:530 (`sum`/`sumFloat`: „there is no overloading“), stdlib/std/collections.lyr:626-628 (`sortList`/`sortListBy`), stdlib/std/io/console.lyr:23, stdlib/std/random.lyr (Doku „xorshift64*“, implementiert ist xorshift64 ohne Multiplikation: src/Lyric.Vm/NativeRegistry.cs:515-521)
- Extern (Vergleich): -
- Beschreibung: docs/guide/13-standard-library.md („One name per type, in the library“) sagt ausdrücklich, Überladung gebe es seit 3.0 und die Namen seien nur aus Kompatibilität geblieben. Die Modulkommentare begründen die Namen weiterhin mit einem nicht mehr existierenden Sprachdefizit — wer die Quellen liest, lernt die Sprache falsch. Außerdem: `random.lyr` nennt den Generator „xorshift64*“, die Native ist plain xorshift64 (kein `* 0x2545F4914F6CDD1D`); der Seed-Ersatz `0x2545F4914F6CDD1D` in `seeded` ist genau die Konstante des `*`-Varianten und legt nahe, dass die Multiplikation verloren ging. Das schwächt `nextBool()` (`% 2`, unterstes Bit) messbar nicht, ist aber eine Doku-Lüge.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] String-Methoden sind unsichtbar, solange `std.string` nicht importiert ist
- Kategorie: qol
- Priorität: HIGH
- Intern (Lyric): stdlib/std/string.lyr:96-100 (`extend string { … }`), docs/guide/13-standard-library.md („Strings have methods … The methods come with the module“)
  ```lyr
  import std.time { Instant };
  let iso = Instant.now().iso();
  println(iso.substring(0, 4));   // error[LYR-SEM0012]: 'string' has no member 'substring'
  // Fix: import std.string as strings;   (ein Import, dessen Name nie benutzt wird)
  ```
  Repro: review-examples/time.lyr (erste Fassung, Zeile 26)
- Extern (Vergleich): Rust/Kotlin/Swift/C#/Go/TS — `s.len()`, `s.substring()` sind ohne Import da; nur Kotlin-Extension-Functions aus Fremdpaketen brauchen einen Import, die der stdlib nie.
- Beschreibung: Jeder Anfänger trifft es beim zweiten Programm; die Fehlermeldung nennt den Import nicht. `std.core` wird bereits ohne Import gebunden (panic, Display-Konformanzen), `std.string` ebenfalls für f-Strings — nur die `extend string`-Methoden nicht. Vorschlag (Minor, Sprache/Resolver): die Extensions des Prelude-Moduls `std.string` (und `std.core`) implizit sichtbar machen, oder mindestens die Diagnose LYR-SEM0012 um „import std.string to use string methods“ ergänzen.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] Ausrichtungs-Vorzeichen in `{x:10}` ist gegenüber printf/C#/Rust invertiert und behandelt Zahlen wie Text
- Kategorie: shortcoming
- Priorität: LOW
- Intern (Lyric): stdlib/std/fmt.lyr:39-41 (`formatString`: `{name:10}` rechts auffüllen = linksbündig; `{name:-10}` links auffüllen), src/Lyric.Vm/NativeRegistry.cs (formatInt mit Breite)
  ```lyr
  println(f"[{"ab":6}] [{"ab":-6}] [{1234567:12}] [{1234567:-12}]");
  // [ab    ] [    ab] [1234567     ] [     1234567]
  ```
  Repro: review-examples/fmt.lyr
- Extern (Vergleich): printf `%10s`/`%-10s`, C# `{x,10}`/`{x,-10}`, Rust `{:>10}`/`{:<10}`: eine Breite ohne Vorzeichen ist RECHTSBÜNDIG; Python `{:10}` ist für Strings links-, für Zahlen rechtsbündig. In Lyric ist eine Zahl mit Breite linksbündig — für Tabellen mit Zahlen die falsche Voreinstellung.
- Beschreibung: Zusammen mit language-reviews Befund (Format-Mini-Sprache nicht in der Spec) gehört fixiert: Vorzeichenkonvention, Zahlen rechtsbündig per Default, und dass `.NET`-Spezifizierer wie `02` (→ `"0 "`) nicht das tun, was `%02d`-Gewohnte erwarten. Major (breaking), falls die Konvention gedreht wird; Minor, falls nur `{x:>10}`/`{x:<10}` ergänzt werden.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 05: Block-Arm im match-Ausdruck liefert einen Wert (Tail-Expression)
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:4152-4159 (LYR-SEM0033), Parser.Statements.cs:52 (`ParseBlock`), :261 (`ParseExprStmt` — fehlendes `;` vor `}` ist heute PAR0001), Parser.cs:459 (`IsStructInitAhead`/`_allowStructInit`), FunctionLowerer.cs:519 (Lambda-Body: Expr ODER Block, beide Formen vorhanden). Ist-Beispiel — Umweg A, `var next` + match-Statement:
  ```lyr
  var next: State;
  match (ev) {
      Data(n) => { this.received += n; next = this.state; }
      Hangup => { this.log += 1; next = State.Closed; }
      …
  }
  this.state = next;
  ```
- Extern (Vergleich): Rust — `self.state = match ev { Event::Data(n) => { self.received += n; self.state.clone() } … }`; Kotlin `when` mit Block-Zweig, letzter Ausdruck ist der Wert.
- Beschreibung: Soll (Vorschlag A): `ValueBlock = '{' { Statement } [ Expr ] '}'` NUR in Wert-Position (match-Arm, Block-Lambda, catch-Block eines try-Ausdrucks). Tail = Ausdruck ohne `;` vor `}` — heute ein Parsefehler, also keine Kollision. `Name { … }` als Tail: Zwei-Token-Lookahead nach dem balancierten Block oder Klammern verlangen (Rust-Regel). Exit-Blöcke bleiben wertlos (heutige Regel), SEM0033 bekommt den Hinweis "return inside a block arm leaves the enclosing function". `defer` im ValueBlock läuft nach dem Tail (§7.5). Vorschlag B (`break value`) verworfen: zweideutig in Schleifen, keine Labels. Zeilen `step`: Ist A 17 / Ist B 15 (Fallunterscheidung 2×) / Soll 10 / Rust 10. Aufwand: Parser ~30, Sema ~60, Lowering ~30, VM 0, Formatter (Tail ohne `;` erhalten). Breaking nein (Minor).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/05-match-block-arm-value/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Or-Pattern mit `_`-Sub-Pattern wird als "bindend" abgelehnt
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:2571-2578 (`PatternBinds`: Zeile 2574 `VariantPattern { TupleElements: not null } … => true` ohne Blick auf die Sub-Patterns), :2489-2491 (Refusal). Repro (prototypes/05-match-block-arm-value/ist.lyr, Kommentar in `stepB`):
  ```lyr
  enum Event { Dial(string), Ack, Data(int), Hangup, }
  match (ev) {
      Dial(_) | Hangup => { this.log += 1; }   // error[LYR-IR0001]: an or-pattern that binds — every alternative would have to bind in its own branch
      _ => { }
  }
  ```
- Extern (Vergleich): Rust `Event::Dial(_) | Event::Hangup => …` ist die Standardform für "zwei Varianten, Payload egal".
- Beschreibung: `Dial(_)` bindet nichts; die Sema akzeptiert das Pattern (SEM0032 findet keine Namen), das Lowering stuft jede Variante mit Sub-Patterns als bindend ein und verweigert mit einer Meldung, die auf ein Binding verweist, das es nicht gibt. Fix: `PatternBinds` für `VariantPattern` rekursiv über `TupleElements`/`StructFields` (ein `_`/Literal bindet nicht; Kurzform-Feldpattern `{ n }` bindet). Umgehung: zwei Arme. Betrifft die §7.6-Aussage "an or-pattern that binds nothing … is unaffected" — die ist heute für Varianten mit `_`-Payload falsch.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/1391f604-6ac2-4463-9002-b8e50bdc158d/scratchpad/team2/prototypes/05-match-block-arm-value/ist.lyr
- Betroffene Teammitglieder informiert: language-review, ir-codegen

### [language-review] Redeklaration einer Lokalen im selben Block wird still verworfen — die ERSTE Bindung gewinnt
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:1040 (`scope.TryDeclare(local);` — Rückgabewert ignoriert, keine Diagnose). Spec: LYR-RES0001 (appendix-a-diagnostics.md:86) deckt nur Modul/Typkörper/extend ab; ~/dev/projects/lyricspec/spec/07-statements.md:121-133 schweigt zu Shadowing im selben Block. Repro: review/probes/p05c_shadow_int.lyr, p05_shadow.lyr
  ```lyr
  let n = 1;
  let n = n + 10;          // nur warning[LYR-SEM0071]: 'n' is never used
  println(f"{n}");         // druckt 1  — die zweite Bindung ist tot, jeder spätere Zugriff sieht die erste
  let s: ?string = "x";
  let s = s ?? "";
  s.length()               // error: '?string' has no member 'length'  — also ebenfalls die erste
  ```
- Extern (Vergleich): Rust (Shadowing im selben Block ist DAS Idiom: `let n = n.unwrap_or(0);`), Kotlin/Swift/C#/Go/Zig (Fehler "redeclaration"). Beides ist vertretbar — stilles Verwerfen mit falschem Programmverhalten nicht.
- Beschreibung: Entweder Rust-Shadowing erlauben (dann muss die NEUE Bindung gelten; hübsch für das Narrowing-Idiom `let x = x ?? d;`) oder LYR-RES0001 auf Blöcke ausdehnen. Spec §7.1 muss die Regel nennen. Empfehlung: Shadowing erlauben (Minor, additiv — heute kompilierende Programme mit Doppel-let sind vermutlich alle fehlerhaft gemeint).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, semantic

### [language-review] Verschachtelte Varianten-Muster (`Neg(Neg(x))`, `Some(Add(a, b))`) werden nicht gelowert
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:2588 (`NotSupported("a nested VariantPattern in a pattern")` → LYR-IR0001), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:522-528 erlaubt `TypePath '(' Pattern ... ')'` rekursiv, Spec ~/dev/projects/lyricspec/spec/07-statements.md:243-247 ("enum variants with nested payload patterns"). Repro: review/probes/p11b_patterns.lyr:8-9
  ```lyr
  return match (e) {
      Neg(Neg(x)) => simplify(x),      // error[LYR-IR0001]: a nested VariantPattern in a pattern
      Neg(Lit(0)) => 0,
      Add(Lit(0), r) => simplify(r),
      ...
  };
  ```
  Umgehung: pro Ebene ein match (Baum-Rewriter, Interpreter: 2-3 Verschachtelungsebenen pro Regel).
- Extern (Vergleich): Rust/Swift/Kotlin(eingeschränkt)/Scala: beliebig tiefe Muster sind Standard, insbesondere für AST-Transformationen (`Expr::Neg(box Expr::Neg(x)) => *x`).
- Beschreibung: Feature ist in der Spec versprochen, die Referenzimplementierung liefert nur eine Ebene. Für die Zielgruppe "Tokenizer/Parser/Interpreter in Lyric" ist das der wichtigste Pattern-Matching-Mangel. Implementierungsarbeit im Lowering (rekursives EmitTagTest + Bindung), keine Spec-Änderung.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, ir-codegen

### [language-review] Tupel-Muster im match tragen nur Namen und `_` — keine Literale, Varianten, Ranges
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:966 (LYR-IR0001 "this pattern in a destructuring binding" — Text stammt aus dem let-Destructuring und ist im match irreführend), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:531 `TuplePattern = '(' Pattern ',' Pattern ... ')'`. Repro: review/probes/p11_match.lyr:17-23
  ```lyr
  return match ((x, y)) {
      (0, 0) => "origin",          // error[LYR-IR0001]
      (0, _) => "y-axis",
      (a, b) if a == b => "diagonal",
      _ => "other",
  };
  ```
- Extern (Vergleich): Rust `match (x, y) { (0, 0) => ..., (0, _) => ... }`, Swift `switch (x, y) { case (0, 0): ... }`, Python `match (x, y): case (0, 0):`.
- Beschreibung: Zusammen mit dem Enum-Teilmuster-Absturz (Bug-Eintrag oben) heißt das: ein Tupel-Match kann heute nichts testen. Das Zustandsmaschinen-Idiom `match ((state, event))` — der Hauptgrund, warum man Tupel matcht — ist damit unbenutzbar. Lowering-Arbeit, keine Spec-Änderung; Guide 06 sollte bis dahin die Grenze nennen.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, ir-codegen

### [language-review] Funktionstypen können nicht werfen: eine `throws`-Funktion ist kein Wert, ein werfendes Lambda passt nirgends
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:283-288 (LYR-SEM0037 "function types carry no throws information"), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:225 `FunctionType = 'fn' '(' ... ')' '->' TypeExpr` (kein `throws`), ~/dev/projects/lyricspec/spec/02-grammar.md:232-236 (ThrowsSuffix "coroutine types only"), ~/dev/projects/lyricspec/spec/09-errors.md:41-47. Repro: review/probes/p13_throwing_fn.lyr
  ```lyr
  fn strict(s: string): int throws Exception { ... }
  fn parseAll(xs: string[], f: fn(string) -> int): int[] { ... }
  parseAll(["ab", "c"], strict);   // error[LYR-SEM0037]: 'strict' declares 'throws' and cannot be used as a value
  // Und: ein Lambda mit `throw` im Body kann keinem fn-Typ zugewiesen werden — Higher-Order-Code
  // (map/filter/forEach/retry/withResource) muss jeden Fehler in ?T verwandeln oder paniken.
  ```
- Extern (Vergleich): Swift (`(String) throws -> Int`, `rethrows`, typed throws), Kotlin (ungeprüft — jedes Lambda darf werfen), Rust (`Fn(&str) -> Result<i32, E>`), C#/TS (ungeprüft).
- Beschreibung: Vorschlag (Minor, additiv): `ThrowsSuffix` auf Funktionstypen erlauben — `fn(string) -> int throws E` — mit derselben einseitigen Zuweisbarkeit wie bei Coroutine-Typen (§10: nicht-werfend passt in werfend). Ein Aufruf über einen werfenden Funktionswert ist eine Wurfstelle. Zusammen mit `throws E` als Typparameter (Eintrag "Keine Polymorphie über Werfbarkeit") ergibt das Swift-`rethrows`-Ausdruckskraft. Betrifft Grammatik §4 TypeExpr, §9.2, §7.3 (Lambda-Inferenz: geworfene Typen im Body ergeben die Klausel), LYR-SEM0037 wird gegenstandslos.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] `extend Enum :: [Hashable<Enum>]` scheitert im Lowering mit LYR-IR0001 in std/core.lyr
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/TypeTable.cs:422 (meldet "generic type 'Equatable' needs 1 type argument(s), got 0" an stdlib/std/core.lyr:149, also am Parent-Interface `Hashable<T> :: [Equatable<T>]`). Inline deklariert (`enum Mode :: [Hashable<Mode>] { ... }`, review/probes/p08b_enum_hash.lyr) funktioniert es; über einen extend-Block (review/probes/p08_maps.lyr:8-13) nicht:
  ```lyr
  enum Mode { Fast, Slow }
  extend Mode :: [Hashable<Mode>] {
      fn equals(other: Mode): bool { ... }
      fn hash(): int { ... }
  }   // error[LYR-IR0001] ... in std/core.lyr:149  (Fehlerort liegt in der stdlib, nicht im Programm)
  ```
- Extern (Vergleich): Rust `impl Hash for Mode` nachträglich in jedem Modul des Crates; Swift `extension Mode: Hashable {}`.
- Beschreibung: Die Parent-Kette einer per `extend` erklärten Konformanz wird ohne die Typargumente des Kindes aufgelöst. Spec §5.5 verspricht "extend T :: [I] additionally declares conformance". Diagnose zeigt zudem in die Bibliothek. Zu prüfen, ob dasselbe mit struct/class passiert (mein Test: enum).
- Prototyp: -
- Betroffene Teammitglieder informiert: ir-codegen, semantic

### [language-review] Kein if-let / let-else / while-let: jede Optional-Abfrage kostet 2-3 Zeilen und eine Hilfsvariable
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/07-statements.md:70-77 (Narrowing nur über Identifier-Vergleich mit `null`), 07-statements.md:100-103 (match narrowt den Scrutinee nicht). Beispiele: review/probes/p01_iflet.lyr, review/tokenizer.lyr:157-159, review/errors.lyr:66-73, review/pipeline.lyr (jede `next()`-Schleife):
  ```lyr
  let v = m.get(k);                      // 1. Hilfsvariable
  if (v == null) { return -1; }          // 2. Prüfung + Exit
  use(v);                                // 3. erst hier ist v: int
  // Schleife:
  while (true) { let x = it.next(); if (x == null) { break; } ... }   // statt while (let x = it.next())
  ```
  Durchschnitt über die 6 Review-Programme: 14 solcher Dreizeiler.
- Extern (Vergleich): Swift `if let v = m[k] { }` / `guard let v = m[k] else { return -1 }`; Rust `let Some(v) = m.get(k) else { return -1 }; while let Some(x) = it.next() {}`; Kotlin `val v = m[k] ?: return -1`; Zig `if (m.get(k)) |v| {}`; C# `if (m.TryGetValue(k, out var v))`; Python walrus `if (v := m.get(k)) is not None`.
- Beschreibung: Vorschlag (Minor, additiv): (a) `let x = expr else { <leaving block> };` — bindet `x: T` aus `?T`, der else-Block muss verlassen (Regel wie LYR-SEM0033); (b) `if (let x = expr) { } else { }` und `while (let x = expr) { }` — Bindung im then-/Schleifen-Block. Beide sind reine Narrowing-Formen, ändern §7.4 nicht (die Bindung IST der Beweis) und brauchen keine neuen Schlüsselwörter. Grammatik §5 IfStmt/WhileStmt/BindingStmt. Wechselwirkung: `let` in Bedingung ist heute ein Parsefehler, also frei.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper (arbeitet bereits an 02 if-let)

### [language-review] Keine Labels für break/continue in verschachtelten Schleifen
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:283-284 (`BreakStmt = 'break' ';'`), Spec ~/dev/projects/lyricspec/spec/07-statements.md:153. Repro: review/probes/p02_labels.lyr
  ```lyr
  var done = false;
  for (r in 0..3) {
      if (done) { break; }
      for (c in 0..3) { if (grid[r][c] == 5) { found = (r, c); done = true; break; } }
  }   // Flag + doppelter break statt `break outer;`
  ```
- Extern (Vergleich): Rust `'outer: for ... { break 'outer; }`, Go `outer: for { break outer }`, Kotlin `outer@ for { break@outer }`, Swift `outer: for`, Java, Zig `outer: while ... break :outer`.
- Beschreibung: Vorschlag (Minor, additiv): `IDENTIFIER ':' (WhileStmt|DoWhileStmt|ForInStmt)` und `break IDENTIFIER;` / `continue IDENTIFIER;`. Kein Konflikt: ein Statement beginnt heute nie mit `IDENTIFIER ':'`. Wechselwirkung mit `defer` (Block-Exit-Regel §7.5 bleibt: jeder verlassene Block läuft seine defers). Grammatik §5, Spec §7.2.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Keine benannten Argumente — Defaults sind nur positional nutzbar
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/07-statements.md:135-140 (§7.1a Defaults sind Call-Site-Transformation), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:481 `CallArgs = ... '(' [ Expr { ',' Expr } ] ')'`. Repro: review/probes/p06_namedargs.lyr
  ```lyr
  fn connect(host: string, port: int = 80, secure: bool = false, retries: int = 3, timeoutMs: int = 1000): string
  connect("a", 80, false, 3, 500);                 // nur timeoutMs gewollt: vier Defaults wiederholen
  connect2("a", ConnectOptions { timeoutMs = 500 }); // Umgehung: Options-Struct (funktioniert gut, +3 Zeilen Deklaration)
  ```
- Extern (Vergleich): Kotlin/Swift/Python/C# `connect("a", timeoutMs = 500)`; Rust/Go/Zig haben es NICHT (Builder/Options-Struct wie hier).
- Beschreibung: Vorschlag (Minor, additiv): `CallArg = [ IDENTIFIER '=' ] Expr`, benannte nach positionalen, Überladungsauflösung §4.3a bekommt einen Schritt "Name muss existieren" vor dem Zählen. Wechselwirkung: `=` in Argumentposition ist heute die Zuweisung als Ausdruck (`f(x = 3)` ist legal und bedeutet Zuweisung!) — daher `:` als Trenner vorziehen (`connect("a", timeoutMs: 500)`), das ist heute ein Parsefehler und damit frei. Priorität MEDIUM, weil das Options-Struct-Idiom akzeptabel ist.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Enums dürfen kein `static let` tragen; Interfaces keine statischen Member und keine Konstanten
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.Declarations.cs:644 (LYR-PAR0040 "static let outside a struct or class body"), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:194-201 (`EnumBody ... [ ';' { FunctionDecl } ]`), 02-grammar.md:216-218 und ~/dev/projects/lyricspec/spec/05-interfaces.md:3-4 (Interface-Member ohne `static`). Repro: review/probes/p07_statics.lyr
  ```lyr
  enum Color { Red, Green, Blue;
      static fn all(): Color[] { ... }      // geht
      static let COUNT: int = 3;            // error[LYR-PAR0040]
  }
  interface Zero { static fn zero(): Self; }   // nicht ausdrückbar -> kein generisches `fn zeroes<T :: [Zero]>()`
  ```
- Extern (Vergleich): Rust (assoziierte Konstanten und Funktionen in Traits: `trait Zero { const ZERO: Self; fn zero() -> Self; }`), Swift (`static func`/`static var` in Protokollen), C# 11 (static abstract members), Kotlin (companion), Go/Zig (Zig: comptime-Interfaces).
- Beschreibung: (a) Minor: `StaticBinding` im EnumBody erlauben — reine Grammatikerweiterung, keine Semantikfrage. (b) Minor: statische Interface-Member (`static fn`, `static let`) — dispatchen nicht über die vtable, sondern werden wie generische Member (§5.2a) monomorphisiert: in generischem Code `T.zero()` ist nach Monomorphisierung ein direkter Aufruf; über einen Interface-WERT nicht erreichbar (kein Receiver → LYR-SEM-Fehler). Braucht ein `Self`-Typwort oder das Muster `interface Zero<T> { static fn zero(): T; }`. Betrifft §5, §8.2, Grammatik §3.5.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper (hat statische Interface-Methoden auf der Liste)

### [language-review] Kein Tupel-Destructuring im for-Kopf (und in Parametern)
- Kategorie: qol
- Priorität: MEDIUM
- Intern (Lyric): Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:400 (`ForInStmt = 'for' '(' IDENTIFIER 'in' ...`), 02-grammar.md:154-155 (`Param = [ 'params' ] IDENTIFIER ':' TypeExpr`). Repro: review/probes/p08_maps.lyr:19-23, review/pipeline.lyr:84-87
  ```lyr
  for (e in entries(m)) {
      let (k, v) = e;        // eine Zeile pro Schleife, plus Warnung 'k' is never used, wenn nur v gebraucht wird
      total += v;
  }
  ```
- Extern (Vergleich): Rust `for (k, v) in &m`, Python `for k, v in m.items()`, Kotlin `for ((k, v) in m)`, Swift `for (k, v) in m`, Go `for k, v := range m`.
- Beschreibung: Vorschlag (Minor): `ForInStmt = 'for' '(' ( IDENTIFIER | TuplePattern ) 'in' ...` und `LambdaParam = TuplePattern | IDENTIFIER [...]` (unfehlbare Muster wie bei `let`). Betrifft §7.2, §7.3, Grammatik §5/§6.2.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Narrowing erreicht keine Felder und keine Ausdrücke; zwei Optionals sind unvergleichbar
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/07-statements.md:73-77 ("Nothing else narrows — not a field, not an index, not a function call"), ~/dev/projects/lyricspec/spec/06-operators.md:60-61 (LYR-SEM0059, Optionals vergleichen). Repro: review/probes/p09_optionals.lyr:20-30
  ```lyr
  // if (h.opt != null) { println(h.opt + 1); }   // h.opt bleibt ?int
  let o = h.opt; if (o != null) { ... }             // Umgehung: Kopie in Lokale
  let p: ?int = 1; let q: ?int = 1;
  // p == q                                         // LYR-SEM0059
  p != null && q != null && p == q                  // Umgehung
  ```
- Extern (Vergleich): Kotlin (Smart Cast auf `val`-Felder desselben Moduls), TypeScript (Narrowing auf Property-Pfaden `if (h.opt !== null)`), Swift (`if let o = h.opt`; `Optional ==` ist definiert: nil == nil), Rust (`Option<T>: PartialEq`), C# (`int? == int?` erlaubt).
- Beschreibung: (a) Optional-Gleichheit `?T == ?T` mit `null == null` = true, `null == x` = false, sonst `Equatable<T>` — additiv, Spec §6.2 (Minor). (b) Narrowing von `let`-Feld-Pfaden `x.f` (mit x immutabel/`let` und f nicht durch einen Aufruf dazwischen invalidiert) — Spec §7.4; konservativ: nur wenn zwischen Prüfung und Nutzung kein Aufruf und keine Zuweisung an x oder x.f steht. Für Klassenfelder unsound bei Aliasing — daher nur für STRUCT-Felder von `let`-Bindungen (Wertsemantik: niemand sonst kann schreiben) sicher. Minor.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] f-Strings rendern keine `Display`-Typen — `.show()` von Hand, obwohl `println(p)` geht
- Kategorie: qol
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:89-95 (§6.6 "there is no implicit Display call in interpolation"), STATUS.md §Still open "an f-string interpolation holds a SCALAR" (ST:1912-1918, LYR-IR0001 "interpolating a non-scalar value"). Repro: review/probes/p12_strings.lyr:24-26, review/container.lyr:100 (`heap.peek()!.show()`), docs/guide/02-values-and-types.md:79-92
  ```lyr
  struct P :: [Display] { x: int, fn show(): string { return f"P({this.x})"; } }
  println(p);            // geht: Display über die Constraint
  println(f"{p}");       // error[LYR-IR0001]: interpolating a non-scalar value
  println(f"{p.show()}");// Umgehung — in jedem Log-Satz
  ```
- Extern (Vergleich): Rust `format!("{p}")` über `Display`, Kotlin `"$p"` über `toString()`, Swift `"\(p)"` über `CustomStringConvertible`, Python `f"{p}"` über `__str__`, C# `$"{p}"` über `ToString()`.
- Beschreibung: Vorschlag (Minor, additiv): Ein Loch, dessen Typ `Display` konform ist, desugart zu `value.show()` (mit Spezifizierer: `formatString(value.show(), spec)`). Genau die Regel, die `println` schon hat (`T :: [Display]`). Kein Nachteil für Skalare (die Konverter bleiben). Enum ohne Display → mit der Synthese aus dem Derive-Eintrag automatisch. Spec §6.6, §11 (Display wird "Sprach-Anker" — ist es über println faktisch schon).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, stdlib-review

### [language-review] Display-Constraint prüft Arrays/Optionals/Tupel nicht — `println([1,2,3])` scheitert erst im IR
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): Constraint-Prüfung bei generischem Aufruf (src/Lyric.Frontend/Sema/TypeChecker.cs, LYR-SEM0028-Pfad; Details und Repro bei stdlib-review: review-examples/display*.lyr in dessen Worktree, Fehlerort stdlib/std/io/console.lyr:51 "call to 'show' on 'int[]'"). Spec ~/dev/projects/lyricspec/spec/08-generics.md:76-83 (§8.3 Regel 7: Constraints gegen die volle Abbildung prüfen).
  ```lyr
  println([1, 2, 3]);   // passiert `T :: [Display]`, IR0001 in der stdlib statt LYR-SEM0028 am Aufruf
  ```
- Extern (Vergleich): Rust (`Vec<T>: Debug` ist abgeleitet, `{:?}` druckt), Python/Kotlin drucken Listen. In Lyric fehlen `Display` für Arrays/Tupel/Optionals ganz (stdlib-Punkt) UND die Prüfung (Sprach-Bug).
- Beschreibung: Sema muss Konformanz für Array-/Tupel-/Optional-Typen als "nicht konform" beantworten statt durchzuwinken. Zusätzlich Minor-Vorschlag: eingebaute Display-Regel für `T[]`, `(A, B)`, `?T` wenn Elemente konform (Rust-Modell), damit `println(xs)` geht.
- Prototyp: -
- Betroffene Teammitglieder informiert: semantic, stdlib-review (Quelle)

### [prototyper] Prototyp 06: Polymorphie über Werfbarkeit (`throws E` mit Typparameter, `never`)
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): lyricspec §10 ("Throwability of a pull", Zuweisung einseitig), §9.2, §4 (ThrowsSuffix "coroutine types only"); src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:11,201 (Lambdas ohne throws-Klausel), :218-227 (Typparameter → any), TypeChecker.cs (LYR-SEM0084). Ist-Beispiel — die Stufe wählt die werfende Signatur, die nicht-werfende Quelle bekommt einen toten catch:
  ```lyr
  fn evens(input: Coroutine<int> throws Exception): Coroutine<int> throws Exception { … }
  try { a = sumAll(evens(numbers())); } catch (e: Exception) { println("cannot happen"); }
  fn mapInts(xs: int[], f: fn(int) -> ?int): (?int)[]   // werfende Operation → Grund geht verloren
  ```
- Extern (Vergleich): Swift 6 typed throws — `func evens<E: Error>(_ input: () throws(E) -> [Int]) throws(E) -> [Int]`; `E == Never` ⇒ Aufruf ohne `try`. Rust: Result<T,E> ist generisch über E, kein Sonderfall.
- Beschreibung: Soll: `fn evens<E>(input: Coroutine<int> throws E): Coroutine<int> throws E`, `fn mapInts<U, E>(xs: int[], f: fn(int) -> U throws E): U[] throws E`. E strukturell inferiert (§8.3), "wirft nichts" = `never` (§9.4-Typ von panic, wird als throws-Typ schreibbar), Substitution der Klausel an der Aufrufstelle (schließt den Bug aus Prototyp 01 ein), Lambdas dürfen werfen, wo der Parametertyp `throws E` trägt. GRAMMATIK UNVERÄNDERT: `fn(int) -> U throws E` parst heute (ThrowsSuffix), nur SEM0084 lehnt ab; §4-Prosa und §10 werden auf Funktionstypen ausgedehnt. Zeilen: nicht-werfende Quelle Ist 6 (toter catch) / Soll 1; werfende Operation Ist Grund verloren / Soll erhalten. Aufwand: Parser 0, Sema groß (~300 Z.: Inferenz, never, Lambda-Klausel, Analyzer-Substitution, Assignability), Lowering 0 (Werfbarkeit hat keine Laufzeitrepräsentation; E sollte NICHT in den Instanzschlüssel), VM 0. Breaking nein (Minor). Offene Fragen: mehrere Throw-Typen in einem Lambda → Throwable?; `never` nur nach throws; generische Interface-Member nötig für `Iterator.each<E>`. Empfehlung: Sprachfeature, in drei Schritten (Substitution → E aus Coroutine-Typen → Lambdas).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/06-throws-polymorphism/
- Betroffene Teammitglieder informiert: language-review, stdlib-review (Iterator.next() throws E)

### [prototyper] `[null] * n` bekommt keinen Kontexttyp; Diagnose schreibt `?int[]` für `(?int)[]`
- Kategorie: bug
- Priorität: LOW
- Intern (Lyric): lyricspec §3.1 (Kontext propagiert in Array-Literale seit 2.1), §3.3 (`[x] * n` als DIE Form, ein Array zu bauen), §4 (`?T[]` ist ein optionales Array, `(?T)[]` ein Array von Optionals); Sema: TypeChecker (Kontext-Propagation in `ArrayLit`, nicht durch `Mul`), TypeFacts.Display (klammert `?T` in `T[]` nicht). Repro (prototypes/06-throws-polymorphism/probe-null-array.lyr):
  ```lyr
  let a: (?int)[] = [null, 1];      // ok
  let b: (?int)[] = [null] * 3;     // error[LYR-SEM0001]: cannot assign 'null[]' to '?int[]'
  ```
- Extern (Vergleich): Rust `vec![None; 3]`, Kotlin `arrayOfNulls<Int>(3)`, C# `new int?[3]` — Null-Initialisierung eines Optional-Arrays ist ein Einzeiler.
- Beschreibung: (1) Für das Idiom `[x] * n` fehlt die Kontext-Propagation durch den `*`-Operanden — das Literal `[null]` hat keinen eigenen Typ, und `null[]` ist kein Typ, der irgendwohin passt. Umgehung: `let hole: ?int = null; [hole] * n`. Vorschlag: Kontext des Bindings in den LINKEN Operanden von `ArrayLit * Expr` propagieren (analog zu §3.1 für Arme und Elemente). (2) Die Typanzeige `?int[]` bedeutet nach §4 etwas anderes als der gemeinte Typ `(?int)[]` — irreführend; `TypeFacts.Display` sollte `(?int)[]` schreiben. (Punkt 2 → diagnostics.)
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/06-throws-polymorphism/probe-null-array.lyr
- Betroffene Teammitglieder informiert: language-review, diagnostics (Inbox)

### [stdlib-review] Guide Kap. 13 widerspricht sich selbst und der Implementierung (OrThrow, freie Adapter, `sum`-Begründung)
- Kategorie: bug
- Priorität: MEDIUM
- Intern (Lyric): docs/guide/13-standard-library.md:341-344 („What none of the shapes carries is a REASON … left open on purpose“) vs. docs/guide/13-standard-library.md:28-31 (`textOrThrow` seit 3.7 mit `IoError`) und stdlib/std/io/file.lyr:60-70; docs/guide/13-standard-library.md:266 („the free forms still work, warn, and go with 3.0“ — Stand 4.4.1, `grep -n '^pub fn map' stdlib/std/iter.lyr` findet nichts, die freien Formen sind längst weg); docs/guide/13-standard-library.md:255-262 (empfiehlt `map<int>((n: int) => …)` mit expliziten Typargumenten; Inferenz funktioniert seit langem: review-examples/iter.lyr:7 `over([1,2,3]).map((n) => n * 2)`); docs/guide/13-standard-library.md:270-276 (Begründung, `fold/count/any/all/find/first/reduce/collectArray` seien keine Methoden — nur `sum/min/max` und `enumerate/chunks` sind begründet, die übrigen sind generisch/typ-erhaltend und als Methoden möglich: review-examples/terminators.lyr kompiliert und läuft).
- Extern (Vergleich): -
- Beschreibung: Drei veraltete Aussagen und eine Begründungslücke in dem Kapitel, das Anwender zuerst lesen. Fix: Absatz „Files answer two ways“ um die 3.7-Twins ergänzen, den 3.0-Satz streichen, Beispiele ohne explizite Typargumente schreiben (und damit auch die `>>`-Falle vermeiden), und die Terminator-Begründung entweder korrigieren oder — besser — die Terminatoren zu Methoden machen (siehe Minor-Vorschlag „Terminatoren als Methoden“).
- Prototyp: review-examples/terminators.lyr (im Worktree von stdlib-review)
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `Random.nextIntRange`/`nextFloat`: `absInt(int.min)` bleibt negativ → Ergebnis unter `lo` bzw. negativer Float
- Kategorie: bug
- Priorität: LOW
- Intern (Lyric): stdlib/std/random.lyr:66-71 (`lo + absInt(this.nextInt()) % (hi - lo)`), stdlib/std/random.lyr:74-77 (`absInt(this.nextInt()) % 9007199254740992`), stdlib/std/math.lyr:39-42 (`absInt`: „the most negative int … comes back unchanged“)
  ```lyr
  // xorshift64 liefert jeden Nicht-Null-Zustand genau einmal pro Periode, also auch -9223372036854775808:
  // absInt(-9223372036854775808) == -9223372036854775808; % (hi - lo) ist dann <= 0 → nextIntRange(1, 7) antwortet 0 oder negativ,
  // nextFloat() antwortet einen Wert < 0.0, vertragswidrig zu "[0, 1)".
  ```
  Zusätzlich: `absInt(x) % n` ist modulo-verzerrt (für `hi - lo` nahe 2^62 messbar), was die Doku „uniformly distributed“ nicht einlöst.
- Extern (Vergleich): Rust `rand::gen_range` (Lemire/rejection sampling), Go `rand.Intn` (rejection), .NET `Random.Next(min, max)` — alle ohne Vorzeichenfalle und ohne Modulo-Bias.
- Beschreibung: Zweiter Repro-Weg ohne 2^64 Züge: `Random { state = 1 }`-artige Konstruktion ist privat, aber `seeded(seed)` mit dem Zustand, dessen nächste xorshift-Runde `int.min` ergibt, erreicht es in einem Schritt (Zustand berechenbar durch Invertieren der drei Shifts). Fix (Minor): `(x >>> 1)` bzw. `x & 0x7FFFFFFFFFFFFFFF` statt `absInt`, und Rejection-Sampling für `nextIntRange`. Test für `nextFloat() >= 0.0` über viele Züge fehlt in stdlib-tests/tests/random_tests.lyr.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Keine `int`/`float`-Grenzkonstanten und keine geprüfte Arithmetik
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/math.lyr:14-19 (nur `pi`, `e`, `tau`; `grep -rn 'pub let' stdlib/std` findet sonst nichts); review-examples/numbers.lyr:8 (`let big = 9223372036854775807;` — die Zahl muss man auswendig wissen)
  ```lyr
  println(f"max+1={big + 1}");                 // -9223372036854775808, still
  println(f"float->int: {1e300 as int} {(0.0 / 0.0) as int}");   // 9223372036854775807 0 (saturiert / NaN → 0, nirgends dokumentiert)
  ```
- Extern (Vergleich): Rust `i64::MAX`, `checked_add`, `saturating_mul`, `f64::EPSILON`, `f64::INFINITY`; C# `long.MaxValue`, `checked { }`; Kotlin `Long.MAX_VALUE`; Go `math.MaxInt64`; Swift `Int.max`, `addingReportingOverflow`.
- Beschreibung: Jede Überlaufprüfung (Parser, Akkumulator, Zeitrechnung) braucht die Grenze; sie existiert nicht als Name. Vorschlag (Minor): `std.math.intMax/intMin/uintMax`, `floatMax/floatMin/floatEpsilon/infinity/nan`, plus `checkedAdd/checkedSub/checkedMul(a, b): ?int` und `saturatingAdd/…`. Die Cast-Semantik `float as int` (Saturierung, NaN→0) gehört in die Spec (§ Konversionen), nicht nur ins Verhalten.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] Kein kryptografischer Hash, kein CRC — nur FNV-1a in Lyric
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/core.lyr:288-303 (`string.hash` = FNV-1a in Lyric, O(n) Bytecode pro Zeichen), `grep -rniE 'sha|crc|md5' stdlib/std` → nichts
- Extern (Vergleich): Go `crypto/sha256`, `hash/crc32` in der stdlib; Python `hashlib`, `zlib.crc32`; .NET `SHA256.HashData`; Rust hat es nicht in std (aber `Hasher`-Trait mit SipHash). Kotlin/JS über Plattform (`java.security.MessageDigest`, WebCrypto).
- Beschreibung: Integritätsprüfung von Downloads, Content-Adressierung, ETag, Passwort-Hash-Vorstufe — in jeder Anwendung, die Dateien oder Netz berührt. Da `std.random.secureRandom` bereits die OS-Kryptoquelle ohne Capability anbindet, wäre `std.hash { sha256(bytes: uint8[]): uint8[], sha1, md5, crc32(bytes): int }` als Natives ohne Capability konsistent (Minor). Ebenfalls fehlend: ein `hashCombine(a: int, b: int): int` für handgeschriebene `hash()`-Methoden (alle Beispiele im Repo nutzen `x * 31 + y`).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Ergonomie-Lücken der Container-APIs (List/Map/Set/Deque/Array) gegenüber allen Vergleichssprachen
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): stdlib/std/collections.lyr (öffentliche API, siehe Zeilen 41-250 `List`, 301-420 `Deque`, 436-620 `Map`, 738-870 `Set`, 1035-1079 freie Funktionen)
  ```lyr
  // 1) Liste aus Literal:            Rust vec![3,1,2] / Kotlin mutableListOf(3,1,2)
  let xs = collect(over([3, 1, 2]));                                  // review-examples/collections.lyr:8
  // 2) Liste als Text:               Rust format!("{:?}", xs)
  println(join(collectArray(xs.iter().map((n) => fromInt(n))), ","));   // collections.lyr:10
  // 3) dedup:                        Kotlin xs.distinct()
  let seen = Set<int>.empty(); let uniq = List<int>.empty();
  for (x in xs) { if (seen.add(x)) { uniq.push(x); } }              // collections.lyr:13-19
  // 4) groupBy:                      Kotlin words.groupBy { it.length }     — 11 Zeilen, collections.lyr:22-33
  // 5) Map-Schlüssel walken:         Rust m.keys()  → keys(m) freie Funktion, Import nötig; entries(m) liefert Tupel, das in
  //                                  der Schleife erst destrukturiert wird: for (e in entries(m)) { let (k, v) = e; … }
  // 6) Array sortieren:              Rust xs.sort() → collect(over(arr)); sortList(l); l.toArray()   (review-examples/tuplekey.lyr:38-42)
  // 7) Wert aus Liste entfernen:     Kotlin xs.remove(3) → listIndexOf + removeAt, zwei Aufrufe + Null-Check
  ```
  Konkret fehlen (per grep bestätigt): `List.of(...)`/`fromArray`, `List.contains/indexOf` als METHODEN (nur freie `listContains`/`listIndexOf`), `List.remove(value)`, `List.pushAll/extend`, `List.slice/sub`, `List.sort()`-Methode, `List.map/filter` direkt (nur über `.iter()`), `List.forEach`, `List.join`, `Map.keys()/values()/entries()` als Methoden, `Map.getOrInsert/update/merge`, `Map.fromEntries`, `Map.capacity`, `Set.toArray/toList`, `Set.addAll/removeAll`, `Set`-Operatoren als Methoden (`a.union(b)`), `Deque`-Iteration (bewusst, aber `toArray` fehlt ebenfalls), `sortListBy` mit Key-Funktion (`sortByKey`), `binarySearch`, `reversed()`, `maxBy/minBy`, jede Sortierung für `T[]`.
- Extern (Vergleich): Kotlin `listOf(3,1,2).distinct().sorted().joinToString(",")` — eine Zeile für 1)+3)+6)+2); Rust `xs.iter().collect::<HashSet<_>>()`, `xs.sort()`, `m.keys()`, `m.entry(k).or_insert_with(Vec::new).push(v)`; Python `Counter`, `sorted(set(xs))`, `defaultdict(list)`; C# LINQ `GroupBy`, `Distinct`, `ToDictionary`.
- Beschreibung: Die Bausteine sind korrekt und gut dokumentiert, aber JEDER alltägliche Schritt ist 3–11 Zeilen statt einer, und die Namen zwingen zu Imports von freien Funktionen (`keys`, `entries`, `listContains`), die in jeder anderen Sprache Methoden sind. Die Begründung in collections.lyr:1052-1055 (Constraint `Equatable<T>` nicht auf die Klasse legen) trägt nur für `contains/indexOf`; `keys/values/entries` haben KEINE zusätzliche Constraint und könnten Methoden sein. Vorschlag siehe Minor-Liste („Container-Ergonomie-Paket“).
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review (Punkt 3 Slices; Punkt 4 Derive für `contains` ohne Constraint auf der Klasse)

### [stdlib-review] `std.iter`: Terminatoren und `enumerate` brechen die Methodenkette; viele Standard-Adapter fehlen
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): stdlib/std/iter.lyr:20-70 (Methoden: map, filter, take, skip, takeWhile, zip, chain, flatMap), stdlib/std/iter.lyr:480-721 (frei: fold, reduce, first, count, sum, sumFloat, any, all, none, find, position, collectArray, minValue, maxValue; enumerate, chunks)
  ```lyr
  // Lyric: die Kette endet als Funktionsaufruf von außen, mit Import jedes Namens
  let n = count(xs.iter().filter((n) => n > 2));
  let s = fold(xs.iter(), 0, (acc, n) => acc + n);
  for (p in enumerate(over(arr))) { let (i, v) = p; … }
  ```
  Nachweis, dass Terminatoren Methoden sein KÖNNEN (Default-Methoden mit eigenem Typparameter `fold<A>`): review-examples/terminators.lyr läuft (count, any, find, fold<A>, toList als Interface-Default-Methoden).
  Fehlend (grep): `last`, `nth`, `skipWhile`, `stepBy`, `rev` (auf Indexable), `sorted`/`sortedBy`, `dedup`/`distinct`, `windows`, `peekable`, `inspect`/`tap`, `forEach`, `scan`, `cycle`, `product`, `average`, `sumBy/minBy/maxBy` (mit Key-Funktion), `groupBy`, `partition`, `unzip`, `zipWith`, `toList()`/`toSet()`/`toMap()` als Methoden, `joinToString`.
- Extern (Vergleich): Rust `xs.iter().filter(..).count()`, `.enumerate()`, `.fold(0, ..)`, `.collect::<Vec<_>>()`, `.max_by_key(..)`, `.rev()`; Kotlin `.withIndex()`, `.groupBy`, `.joinToString`, `.sortedBy`; C# LINQ; Python `enumerate`, `sorted(key=)`, `itertools`.
  ```rust
  let n = xs.iter().filter(|n| **n > 2).count();
  for (i, v) in arr.iter().enumerate() { … }
  ```
- Beschreibung: `enumerate` als Methode ist wegen der Monomorphisierungs-Endlosigkeit (iter.lyr:60-68) nicht möglich — ein Sprachdefizit (Methoden nicht-generischer Interfaces dürfen den Elementtyp nicht ändern); `for ((i, v) in …)` mit Tupel-Pattern im Schleifenkopf fehlt ebenfalls (Sprache). Die Terminatoren dagegen sind reine stdlib-Arbeit. Vorschlag: Minor „Terminatoren als Methoden“ + Adapter-Paket; Major: `enumerate`/`chunks` als Methoden, sobald das Sprachfeature da ist.
- Prototyp: review-examples/terminators.lyr
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] `std.time`: kein Kalender, keine lokale Zeit, kein `Instant.minus`, keine Stunden/Tage
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/time.lyr:26-100 (`Duration`: nur `ofMillis/ofSeconds/ofMinutes`, `totalMillis/totalSeconds`), stdlib/std/time.lyr:110-230 (`Instant`: `now`, `ofEpochMillis`, `epochMillis`, `since`, `plus`, `iso`, `fromIso`)
  ```lyr
  let earlier = now.plus(Duration.ofMillis(0 - 5000));   // kein minus; review-examples/time.lyr:13
  let iso = x!.iso();                                      // Jahr/Monat/Tag nur per substring aus dem ISO-Text, time.lyr:26
  println(f"year={iso.substring(0, 4)} month={iso.substring(5, 2)}");
  println(f"{d.totalSeconds() / 3600}h {d.totalSeconds() / 60 % 60}m");   // keine hours()/minutes()
  Instant.fromIso("2024-02-29T12:34:56Z")        // null: Sekundenauflösung wird abgelehnt
  Instant.fromIso("2024-02-29T12:34:56.789+01:00") // null: Offsets werden abgelehnt (RFC 3339 erlaubt beides)
  ```
- Extern (Vergleich): Rust `chrono`/`time` (nicht std, aber Standard), Go `time.Time.Year()`, `t.Add(-5*time.Second)`, `time.Now().Local()`, `t.Format(layout)`; Kotlin `LocalDate`, `Duration.hours`; Swift `Calendar`, `DateComponents`; Python `datetime`, `timedelta(hours=2)`; C# `DateTime`, `TimeSpan.FromHours`. Alle parsen mindestens RFC 3339 mit Offset.
- Beschreibung: Logfile-Zeitstempel in lokaler Zeit, „vor 3 Tagen“, Tagesgrenzen — mit `std.time` allein nicht möglich; die Kalenderarithmetik (`iso`, `parseIso`, `daysInMonth`, `isLeap`) ist bereits privat vorhanden (time.lyr:150-330) und müsste nur als `Date { year, month, day }`/`Instant.toDate()`/`Date.toInstant()` freigelegt werden. Minor: `Duration.ofHours/ofDays`, `totalMinutes/totalHours/totalDays`, `Duration.times(n)`, `Instant.minus(Duration)`, `Instant.year()/month()/day()/hour()/…` (UTC), `fromIso` tolerant für `Z` ohne Millis und für Offsets (`fromRfc3339`), `Duration.show()` menschenlesbar (`1h 2m 3.5s`) als `format()`-Zusatz. Lokale Zeitzone: eigener Native (`localOffsetMillis(at)`), `osAccess`.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `std.test`: nur `assertTrue`/`assertEq`; kein `assertThrows`, keine Toleranz, keine Diff-Ausgabe, Argumentreihenfolge unkonventionell
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/test.lyr:14-30; docs/guide/20-testing.md
  ```lyr
  assertEq(0.1 + 0.2, 0.3);            // FAIL expected 0.3, got 0.30000000000000004 — kein assertClose
  assertEq("hello world", "hello  world");  // "expected hello  world, got hello world" — Unterschied unsichtbar
  // erwartete Exception: nur try { … } catch (e: X) { return; } panic("no throw") von Hand
  assertEq(P { x = 1 }, P { x = 1 });   // SEM0028: braucht Equatable UND Display von Hand (review-examples/hashing2.lyr:32)
  ```
  Repro: review-examples/testproj/tests/t.lyr
- Extern (Vergleich): Rust `assert_eq!(left, right)` mit Diff-Ausgabe, `#[should_panic]`, `approx`; JUnit `assertEquals(expected, actual, delta)`, `assertThrows`; pytest `pytest.raises`, `approx`; Go `t.Errorf`, testify `require.InDelta`; Swift `XCTAssertThrowsError`, `XCTAssertEqual(accuracy:)`.
- Beschreibung: Testen ist die Einstiegsstelle jedes Anwenders; das Modul hat 2 Funktionen und 0 eigene Tests. Minor: `assertNotEq`, `assertNull/assertNotNull`, `assertClose(a, b, epsilon)`, `assertThrows<E>(f: fn() -> void throws E)` (braucht `throws`-Polymorphie — Verweis prototyper Prototyp 01/language-review), `fail(message)`, `assertContains(text, needle)`; Fehlermeldung mit Zeichen-Diff für Strings. Die Reihenfolge `assertEq(actual, expected)` ist Rust-Reihenfolge (`left, right`), aber die Meldung sagt „expected X, got Y“ — JUnit-Nutzer vertauschen sie garantiert; Doku-Satz nötig.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper (assertThrows braucht Prototyp 01/throws-Typparameter)

### [stdlib-review] `std.io.path`: keine Normalisierung, kein `absolute`, kein `relative`, kein `split`; `joinPath("a", "../b")` bleibt `a/../b`
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/io/path.lyr:15-126 (joinPath, fileName, parentDir, extension, stem, withExtension, isAbsolute — 7 Funktionen)
  ```lyr
  println(f"{joinPath("a", "../b")} {parentDir("a")}");   // a/../b  ""  — review-examples/file.lyr:23
  // absolute(p), normalize(p), relative(from, to), components(p), home-Expansion: nicht vorhanden
  ```
- Extern (Vergleich): Go `filepath.Clean/Abs/Rel/Split`; Python `os.path.normpath/abspath/relpath`, `pathlib.Path.parts/resolve`; Rust `Path::components/canonicalize`, `PathBuf::push`; .NET `Path.GetFullPath/GetRelativePath`; Node `path.normalize/resolve/relative`.
- Beschreibung: Ein Build-Skript oder ein CLI-Tool braucht `absolute` und `normalize` beim ersten Argument. `absolute` braucht `currentDir` (`osAccess`) — passt nach `std.os` oder als `std.io.file.absolute` (fileAccess); `normalize/relative/components` sind reine Textarbeit und gehören capability-frei nach `std.io.path`. Minor.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `std.io.file`: kein rekursives Löschen/Walken, keine Metadaten, kein `writeLines`; `std.process`: kein `output()`, kein Arbeitsverzeichnis/Env für Kinder
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/io/file.lyr:200-247 (`removeDir` nur leer, `entries` nur Namen), stdlib/std/process.lyr:60-170 (`start(program, args)` ohne cwd/env; Capture = Schleife über `readSomeOut` + `wait` + `close`, 15 Zeilen in review-examples/osproc.lyr:10-28)
  ```lyr
  for (name in file.entries(dir) ?? []) { file.remove(joinPath(dir, name)); }   // review-examples/file.lyr:26-28, nur flach
  ```
- Extern (Vergleich): Rust `fs::remove_dir_all`, `fs::metadata(p)?.modified()`, `walkdir`; Go `os.RemoveAll`, `filepath.WalkDir`, `os.Stat`, `exec.Command(...).Output()`, `cmd.Dir`, `cmd.Env`; Python `shutil.rmtree`, `os.walk`, `subprocess.run(capture_output=True, cwd=…)`; .NET `Directory.Delete(recursive: true)`, `ProcessStartInfo.WorkingDirectory`.
- Beschreibung: „a caller that needs one writes the loop visibly“ (file.lyr:236) ist für rekursives Löschen ein Argument, für `walk`/`modifiedAt`/`writeLines` nicht. Prozesse: 90 % der Aufrufe sind „starte X, warte, gib mir stdout als Text“ — das sollte EIN Aufruf `process.output(program, args): ?Output { code, stdout, stderr }` innerhalb einer Task sein; `start` braucht Überladungen mit `cwd` und `env`. Minor.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] `std.task`: kein Channel, kein Timeout, kein Task-Ergebnis, kein `sleep`-Helfer; Konsumenten müssen mit `Wait.Now` busy-pollen
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/task.lyr:10-40 (`Wait`: Now, Sleep, Readable, Writable, Interrupt), stdlib/std/task.lyr:96-110 (`spawn(task: Coroutine<Wait>): void` — kein Handle, kein Ergebnis)
  ```lyr
  // Produzent/Konsument: review-examples/tasks_demo.lyr:23-37 — der Konsument yieldet Wait.Now in einer Schleife,
  // bis die Deque etwas hat: ein Spin, der jede Runde des Schedulers belegt.
  // Globale Zustandsflags: `var` auf Modulebene ist verboten (PAR0027), also eine Klasse nur für ein bool (tasks_demo.lyr:8-13).
  ```
- Extern (Vergleich): Go `chan`, `select`, `context.WithTimeout`, `sync.WaitGroup`; Kotlin `Channel`, `withTimeout`, `async/await`, `delay`; Rust `tokio::sync::mpsc`, `timeout`, `JoinHandle`; Python `asyncio.Queue`, `wait_for`, `gather`; Swift `AsyncStream`, `Task.sleep`, `withThrowingTaskGroup`; C# `Channel<T>`, `Task.WhenAll`, `CancellationToken`.
- Beschreibung: Das Scheduler-Modell (ein `Wait`-Enum, Warten im Modul statt in Signaturen) ist elegant — aber ohne `Channel<T>` (mit `Wait.Signal(id)`-Variante oder intern über einen Deque + Wecker), `Task<T>`-Handle mit `join()`, `timeout(ms, task)` und `sleep(ms)` als Funktion bleibt jede Koordination Handarbeit und jeder Warteplatz ein Spin. Minor (additiv): `pub fn sleep(ms)` (yield im Modul), `Channel<T> { send, receive }` mit Wecken über eine neue `Wait`-Variante (Enum-Erweiterung ist für `match` in Nutzercode breaking → Major, oder intern ohne neue Variante via Readiness-Liste), `Task<T>`/`join`. Generische Coroutine-Helfer sind laut language-review derzeit ein Compiler-Bug (HIGH) — Abhängigkeit.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review (generische Coroutinen), prototyper

### [stdlib-review] `std.json`: tiefer Zugriff ist eine Null-Check-Treppe; keine (De-)Serialisierung von Nutzertypen; `JsonValue` weder `Equatable` noch `Display`
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/json.lyr:8-90 (`field`/`at` → `?JsonValue`), review-examples/json.lyr:40-52 (5 verschachtelte `if != null` für `doc.users[0].tags[1]`), json.lyr:12-34 (Struct→JSON von Hand, 6 Zeilen pro Typ; JSON→Struct 13 Zeilen)
  ```lyr
  let a = parse("[1]"); let b = parse("[1]"); println(a == b);   // SEM0059: kein Equatable
  ```
- Extern (Vergleich): JS `doc.users?.[0]?.tags?.[1]`; Python `doc["users"][0]["tags"][1]`; Rust `doc["users"][0]["tags"][1].as_str()` (serde_json `Index` gibt `Null` statt Panik) + `#[derive(Serialize, Deserialize)]`; Kotlin `kotlinx.serialization`; C# `JsonSerializer.Deserialize<Point>`; Swift `Codable`; Go `json.Unmarshal(&p)`.
- Beschreibung: Minor (stdlib): `JsonValue.path("users.0.tags.1"): ?JsonValue` oder `get(key)`/`index(i)` die auf einem NICHT-optionalen Empfänger `JsonValue.Null` statt `null` liefern (serde-Stil: eine Kette, ein Null-Check am Ende); `Equatable<JsonValue>`, `Display` (=`serialize`); Builder `JsonValue.object([("x", JsonValue.Int(1))])`, `JsonValue.array([...])`; Interfaces `ToJson { fn toJson(): JsonValue }` / `FromJson<T> { static fn fromJson(v: JsonValue): ?T }` als Anker, so wie `Display` für `println`. Major/Sprache: Derive für ToJson/FromJson (language-review Synthese-Vorschlag).
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] `std.string`: ASCII-only Klassifikation, kein `removePrefix/removeSuffix`, kein `lines()`-Iterator, kein `substringFrom`, kein `equalsIgnoreCase`, kein `reverse`; `toUpper("ß")` bleibt `ß`
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/string.lyr:330-370 (`isAlpha/isUpper/isLower/isWhitespace`: ASCII), stdlib/std/string.lyr:96-300 (Methodenliste)
  ```lyr
  println(f"isAlpha(é)={isAlpha('é')} isUpper(É)={isUpper('É')} toUpper(é)={strings.fromChar('é').toUpper()}"); // false false É
  println("straße".toUpper());   // STRAßE (Rust: STRASSE; .NET ToUpperInvariant lässt ß stehen — dokumentieren)
  // reverse: 6 Zeilen (review-examples/strings.lyr:12-18); Python s[::-1], Rust s.chars().rev().collect()
  // capitalize: w.substring(0, 1).toUpper() + w.substring(1, w.length() - 1)  (strings.lyr:30) — kein substringFrom(1)
  ```
- Extern (Vergleich): Rust `char::is_alphabetic` (Unicode), `strip_prefix`, `lines()`, `eq_ignore_ascii_case`; Kotlin `removePrefix`, `lines()`, `equals(ignoreCase=true)`, `reversed()`, `capitalize`; Python `str.isalpha` (Unicode), `removeprefix`, `casefold`; Go `strings.TrimPrefix`, `unicode.IsLetter`; Swift `hasPrefix`+`dropFirst`.
- Beschreibung: `isAlpha(c)` antwortet für jeden nicht-lateinischen Buchstaben `false` — für ein Sprachprojekt, das Code-Points korrekt zählt, überraschend; .NET `char.IsLetter`/`Rune.IsLetter` sind einen Native entfernt. Minor: `isLetter/isUpper/isLower/isWhitespace` Unicode-korrekt (oder `isAsciiAlpha` daneben), `removePrefix/removeSuffix`, `substringFrom(start)`, `lines(): Iterator<string>`, `equalsIgnoreCase`, `reverse()`, `capitalize()`, `startsWith(char)`, `indexOf(char)`, `toInt(): ?int`-Methode als Alias für `parseInt`. Doku-Satz zu `ß`/Sonderfällen bei `toUpper`.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Kein `int`↔Bytes-Helfer in `std.bytes`; Byte-Arrays sind nicht vergleichbar; `[1, 2] as uint8[]` geht nicht
- Kategorie: shortcoming
- Priorität: LOW
- Intern (Lyric): stdlib/std/bytes.lyr:1-55 (slice, indexOf, indexOfFrom — 3 Funktionen)
  ```lyr
  fn u32be(n: int): uint8[] { return [(n >> 24 & 255) as uint8, …]; }    // review-examples/bytes_demo.lyr:7-13 von Hand
  let a = [1, 2] as uint8[];   // SEM0006 — ein uint8[]-Literal braucht typisierte Elemente: `let one: uint8 = 1; [one, one]`
  ```
- Extern (Vergleich): Go `binary.BigEndian.PutUint32`, `bytes.Equal`; Rust `u32::to_be_bytes`, `a == b`; Python `struct.pack`, `int.to_bytes`; .NET `BinaryPrimitives`, `Span.SequenceEqual`; Swift `withUnsafeBytes`.
- Beschreibung: Jeder Wire-Protocol-Code (Längenpräfixe, Ports, Header) braucht `readU16be/readU32be/readU64le` + `writeU32be(n): uint8[]` und `equals(a, b)`. Minor. Das Literal-Problem (`uint8[]` aus Int-Literalen) ist Sprache (language-review: Literal-Typinferenz gegen den erwarteten Typ).
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 07: Enum-Variantenname (`variantName()`) und synthetisiertes Display
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §10 (`next()` als eingebautes Member — das Vorbild), §3.4, §13 (Enum trägt seine Variantennamen im Modul für Attribute); Sema: Member-Lookup auf EnumType (TypeChecker), Lowering: Desugar in match über Varianten. Ist-Beispiel — pro Enum eine Namensfunktion und die Rückrichtung mit stillem `_ => null`:
  ```lyr
  fn kindOf(e: Event): string { return match (e) { Dial(_) => "Dial", Ack => "Ack", Data(_) => "Data", Hangup => "Hangup" }; }
  fn parseMode(s: string): ?Mode { return match (s) { "fast" => Mode.Fast, "safe" => Mode.Safe, "dry" => Mode.Dry, _ => null }; }
  ```
- Extern (Vergleich): Kotlin `e::class.simpleName`, `enum.name`, `Mode.valueOf("Safe")`, data-class `toString()`; Zig `@tagName(ev)`, `std.meta.stringToEnum(Mode, s)`.
- Beschreibung: Soll (a) eingebautes Member `e.variantName(): string` auf jedem Enum-Wert, keine Grammatikänderung, reservierter Name; (b) `enum E :: [Display]` ohne Body → `show` synthetisiert (Unit → Name, Tupel → `Name(a, b)`, Struct → Initializer-Syntax), derselbe Mechanismus wie Prototyp 03; (c) optional `E.fromVariantName(s): ?E` als statisches eingebautes Member für Unit-Enums. Zeilen: Ist 30 Z. Hilfsfunktionen / Soll 0 / Kotlin 0. Aufwand: Parser 0, Sema klein (a) / mittel (b, mit 03), Lowering ~30-40 Z. je Teil, VM 0. Breaking: (a) reserviert `variantName` auf Enums (praktisch Minor), (b)/(c) additiv. Fehlerklassen: still veraltende Rückrichtung, Tippfehler in Namens-Strings. Empfehlung: (a) sofort, (b) mit 03, (c) optional.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/07-enum-variant-name-display/
- Betroffene Teammitglieder informiert: language-review

### [language-review] `pub` auf Membern wird nicht durchgesetzt; Felder können gar nicht `pub` sein — es gibt keine Kapselung
- Kategorie: shortcoming
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Resolver/Resolver.cs:134 (Member-Visibility wird gespeichert), src/Lyric.Frontend/Sema/TypeChecker.cs:1590-1593 (Visibility wird NUR für modul-qualifizierte Namen geprüft, nie bei `x.member`), Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:263 (`Field = IDENTIFIER ':' TypeExpr` — kein `pub`), 02-grammar.md:239 (`FunctionDecl = [ 'pub' ] ...` auch als Member), Spec ~/dev/projects/lyricspec/spec/04-modules.md:16 ("Only pub declarations cross a module boundary" — Member sind nicht erfasst). Repro: review/mod/app.lyr + review/mod/lib.lyr, review/probes/p21_privacy.lyr:
  ```lyr
  // lib.lyr:  pub class Account { balance: int, fn secret(): int {...}, pub fn read(): int {...} }
  // app.lyr:
  println(f"{a.balance}");    // nicht-pub Feld eines fremden Moduls: kompiliert
  println(f"{a.secret()}");   // nicht-pub Methode: kompiliert
  let xs = List<int>.empty(); xs.push(1); xs.push(2);
  xs.count = 0;               // internes Feld der stdlib-List: kompiliert, length() ist danach 0
  xs.resize(1);               // nicht-pub mut fn der stdlib: kompiliert
  ```
- Extern (Vergleich): Rust (`pub` pro Feld/Methode, Standard privat im Modul), Swift (`private`/`internal`/`public`), Kotlin/C# (`private` Standard in Klassen), Go (Großschreibung = exportiert, gilt für Felder und Methoden), Zig (alles im Struct sichtbar, aber Modulgrenze `pub`), TypeScript (`private`).
- Beschreibung: Eine Bibliothek kann keine Invariante schützen; `pub fn` in einem Klassenkörper ist heute Dekoration und in der Spec undefiniert. Vorschlag: Semantik von `pub` auf Membern festlegen — ohne `pub` ist ein Member (Feld, Methode, static let) nur im DEKLARIERENDEN MODUL sichtbar, mit `pub` überall, wo der Typ sichtbar ist; Felder bekommen `[ 'pub' ]` in der Grammatik; ein Struct-Initializer außerhalb des Moduls darf nur pub-Felder setzen (Felder ohne Default müssen dann pub sein oder der Typ bietet eine Konstruktorfunktion). Neue Diagnose (LYR-SEM "member is not public"). BREAKING: heute kompilierende Zugriffe auf nicht-pub Member fremder Module werden Fehler → Major (4.x: Warnung mit Uhr, 5.0: Fehler, nach dem Muster von LYR-SEM0093). Betrifft §4.2, Grammatik §3.2/§3.3, stdlib (jedes Feld, das gelesen werden soll, braucht `pub`).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, stdlib-review, semantic

### [language-review] Konkreter Struct-Wert direkt an einen Parameter vom Interface-Typ einer generischen Instanz: IR-Verifier-Absturz
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/IrVerifier.cs:291 ("call to List<Shape>.push: arg 1 is val ty1, expected dyn ty0") — die Sema akzeptiert die Zuweisung (§3.7: konkreter Typ → Interface-Wert), das Lowering baut den Fat-Pointer nicht, wenn der Parametertyp erst durch Substitution zum Interface wird. Repro: review/probes/p17_generics.lyr:44-46 (Kommentar), review/probes/p17b_ifacelist.lyr (Umgehung).
  ```lyr
  let ls = List<Shape>.empty();
  ls.push(Sq { s = 1.0 });          // InternalCompilationException (IR-Verifier)
  let sq: Shape = Sq { s = 1.0 };
  ls.push(sq);                      // geht — Umgehung über Zwischenvariable
  ```
- Extern (Vergleich): Rust `Vec<Box<dyn Shape>>::push(Box::new(Sq{..}))` erzeugt die Coercion an der Aufrufstelle; Swift `[Shape]`-Append ebenso.
- Beschreibung: Jede Sammlung von Interface-Werten (`List<Drawable>`, `Map<string, Handler>`) ist damit nur über Hilfsvariablen befüllbar; Absturz statt Diagnose. Vermutlich derselbe Pfad wie bei Feldinitialisierern generischer Typen (`Box<Shape> { v = Sq {..} }`) — prüfen.
- Prototyp: -
- Betroffene Teammitglieder informiert: ir-codegen, semantic

### [language-review] Index-Operator über `Indexable<T>` existiert und funktioniert — steht aber nicht in der Spec
- Kategorie: strength
- Priorität: LOW
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:110-114, 1115 (`Indexable<T>` ist zu `[i]`, was `Iterator<T>` zu `for-in` ist), stdlib/std/collections.lyr:19-23. In der Spec nur in appendix-a-diagnostics.md:106 (LYR-SEM0007) erwähnt; ~/dev/projects/lyricspec/spec/06-operators.md nennt `[ ]` nirgends, ~/dev/projects/lyricspec/spec/11-stdlib-contract.md:270-275 zählt `Indexable` nicht zu den Operator-Ankern. Repro: review/probes/p22_indexable.lyr
  ```lyr
  let xs = List<int>.empty(); xs.push(10); xs.push(20);
  println(f"{xs[1]}");   // 20 — get(i)
  xs[0] = 5;             // set(i, v)
  ```
- Extern (Vergleich): Rust `Index`/`IndexMut`-Traits, C# Indexer, Kotlin `operator fun get/set`, Python `__getitem__`, Swift `subscript`. Lyric macht es genauso konsequent über EIN Interface — gut. Go und Zig haben keinen Nutzer-Index.
- Beschreibung: Stärke (Operator = Interface-Methode, wie bei `+`/`==`), aber Spec-Lücke: §6 braucht einen Absatz "Indexing: `a[i]` ist `a.get(i)`, `a[i] = v` ist `a.set(i, v)` über `Indexable<T>`", und §11 muss `Indexable<T>` als Anker nennen (sonst darf eine zweite stdlib es weglassen und `xs[i]` verschwindet). Ergänzung: heute nur `int`-Index; `Map<K,V>` ist nicht indexierbar (get liefert ?V) — ein `Indexable<K, ?V>` wäre mit dem heutigen Interface nicht ausdrückbar (Set-Seite bräuchte V).
- Prototyp: -
- Betroffene Teammitglieder informiert: spec-conformance, stdlib-review

### [language-review] Array-Konstruktion braucht ein erstes Element: `[x] * n` — generische Funktionen müssen den Leer-Fall sonderbehandeln
- Kategorie: qol
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/03-types.md:71-73 ("the value is built with `[x] * n`"), ~/dev/projects/lyricspec/spec/07-statements.md:129-130 (`let xs = [];` braucht Annotation). Repro: review/probes/p17_generics.lyr:11-17, review/cli.lyr:63-66, review/tokenizer.lyr:49-52
  ```lyr
  fn mapArr<T, U>(xs: T[], f: fn(T) -> U): U[] {
      if (xs.length == 0) { return []; }        // Sonderfall, weil es ohne Element kein Array gibt
      var out = [f(xs[0])] * xs.length;         // f(xs[0]) läuft ZWEIMAL (hier und in der Schleife) oder Schleife ab 1
      for (i in 1..xs.length) { out[i] = f(xs[i]); }
      return out;
  }
  var word = [' '] * (i - start);               // Dummy-Element ' ' nur, um Platz zu bekommen
  ```
- Extern (Vergleich): Rust `vec![x; n]` (gleich) aber `Vec::with_capacity`/`collect()`; Go `make([]T, n)` (Nullwert); Zig `try alloc.alloc(T, n)`; C# `new T[n]` (default); Kotlin `Array(n) { i -> f(i) }`; Python `[f(i) for i in range(n)]`.
- Beschreibung: Vorschlag (Minor): Array-Konstruktor mit Generator `[n] of (i) => f(i)` oder stdlib `arrayOf<T>(n, f: fn(int) -> T): T[]` und `arrayFilled<T>(n, x)`. Kotlin-Form `Array(n) { i -> ... }` ist die kürzeste. Für eine Sprache ohne Nullwerte (kein `default(T)`) ist der Generator-Konstruktor die einzige saubere Form. Betrifft §3.3, ggf. Grammatik ArrayLit. Stdlib-Seite: `over(xs).map(f)` + `collectArray` ist die heutige Umgehung (2 Imports, Iterator-Allokationen).
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review, prototyper

### [language-review] Keine Raw-/Mehrzeilen-Strings
- Kategorie: shortcoming
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/01-lexical.md:73-74 ("There is no raw or multiline string form"), Repro: review/probes/p04_misc_types.lyr:28-30, review/errors.lyr:29-32 (Testdaten mit `\n`), std.json-Nutzung in docs/guide/13-standard-library.md:295 (`"{\"name\": \"aria\", \"level\": 3}"`).
  ```lyr
  let doc = parse("{\"name\": \"aria\", \"level\": 3}");   // jedes Anführungszeichen escaped
  let s = "line1\n" +
          "line2";                                          // Mehrzeilentext als Konkatenation
  ```
- Extern (Vergleich): Rust `r#"..."#`, Kotlin `"""..."""` + `trimIndent()`, Python `'''`/`r""`, C# `@""`/`"""raw"""`, Swift `"""`, Zig `\\`-Zeilen, Go Backticks, TypeScript Template-Literals.
- Beschreibung: Für Tests, JSON, SQL, Hilfetexte von CLI-Tools (`usage:` über 10 Zeilen) fehlt es täglich. Vorschlag (Minor, rein lexikalisch): `r"..."` ohne Escapes (Rust-Form mit `#`-Zaun für Anführungszeichen) und `"""..."""` mehrzeilig mit Einrück-Entfernung nach Swift-Regel (die schließende `"""` bestimmt den Einzug). f-String-Variante `f"""..."""` kombinierbar. Betrifft §1.6/§1.7, Grammatik §1.5; Formatter (Lexeme unverändert übernehmen).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper, lexer-parser

### [language-review] Keine Struct-Update-Syntax und kein positionaler Tupel-Zugriff
- Kategorie: qol
- Priorität: LOW
- Intern (Lyric): Grammatik ~/dev/projects/lyricspec/spec/02-grammar.md:471-472 (`StructInit` nur Feld=Wert), ~/dev/projects/lyricspec/spec/03-types.md:76 (Tupel "taken apart by destructuring"). Repro: review/probes/p10_structs.lyr:26-28, review/probes/p04_misc_types.lyr:22-24, review/probes/p18_iter.lyr:19-21
  ```lyr
  let copy = Counter { n = base.n };            // bei 8 Feldern: 8 Zeilen, um EIN Feld zu ändern
  let (t0, t1, t2) = t;                          // nur um t1 zu lesen (zwei tote Namen, zwei Warnungen ohne `_`)
  let (n, s) = z[1];
  ```
- Extern (Vergleich): Rust `Counter { n: 5, ..base }`, `t.1`; Kotlin `data.copy(n = 5)`; Swift `t.1`; C# `with { N = 5 }`, `t.Item2`; Zig `.{ .n = 5 }` + Feldzugriff; Python `t[1]`.
- Beschreibung: (a) `StructInit ... [ '..' Expr ]` als letzter Eintrag: nicht genannte Felder aus dem Basiswert (Struct: Kopie, Class: neue Instanz mit denselben Referenzen). (b) `t.0`/`t.1` als Postfix (Lexer: `.` gefolgt von DecLit — Konflikt mit Float-Literal `1.0` nur, wenn kein Punkt-Operator davor; `t.0.1` ist Rust-bekannt heikel → `t.0` nur einstufig oder Klammern). Beide Minor.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Kein unäres Minus, kein `%`, kein `Neg` auf eigenen Typen; Compound-Zuweisung auf Feldern mit Interface-Operatoren verweigert
- Kategorie: qol
- Priorität: LOW
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:9-13 (`%` numeric-only), CHANGELOG.md:2948 (v1.5.0: "`%` on user types, and unary `-`: no interface exists for either, deliberately"), ~/dev/projects/lyricspec/spec/06-operators.md:84-87 (§6.5 Compound auf Feld/Element = LYR-SEM0003). Repro: review/probes/p20_ops.lyr:10, review/probes/p09_optionals.lyr:31-33
  ```lyr
  fn neg(): V { return V { x = -this.x }; }    // v.neg() statt -v
  h.v = h.v + V { x = 2 };                      // statt h.v += V { x = 2 }
  ```
- Extern (Vergleich): Rust `Neg`, `Rem`, `AddAssign` (Feldziel `p.v += w` ist erlaubt, Auswertung einmal); Swift `prefix func -`; Kotlin `unaryMinus`, `rem`, `plusAssign`; C# `operator -`, `%`; Python `__neg__`, `__mod__`.
- Beschreibung: Vektor-/Geldtypen ohne `-v` sind ungewohnt; `Neg<R>` mit `fn neg(): R` ist eine Zeile in std.core und ein Fall in §6.1. Compound auf Feld: das Lowering kann Objekt/Index in einen Temp legen (Rust/C# tun genau das) — das "sichtbar zweimal auswerten"-Argument entfällt, wenn die Spec "einmal" verspricht. Beides Minor; bewusst entschieden, daher LOW.
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review (Neg/Rem-Interfaces wären std.core)

### [stdlib-review] Minor 1: Terminatoren als Methoden auf `Iterator<T>` + Adapter-Paket
- Kategorie: proposal-minor
- Priorität: HIGH
- Intern (Lyric): stdlib/std/iter.lyr:20-70, 480-721
  ```lyr
  // Soll (additiv; die freien Funktionen bleiben, mit @Deprecated bis 5.0):
  pub interface Iterator<T> {
      mut fn next(): ?T;
      fn count(): int { … }                        fn any(test: fn(T) -> bool): bool { … }
      fn all(test: fn(T) -> bool): bool { … }      fn find(test: fn(T) -> bool): ?T { … }
      fn position(test: fn(T) -> bool): ?int       fn first(): ?T   fn last(): ?T   fn nth(n: int): ?T
      fn fold<A>(seed: A, step: fn(A, T) -> A): A  fn reduce(step: fn(T, T) -> T): ?T
      fn forEach(f: fn(T) -> void): void           fn toArray(): T[]     fn toList(): List<T>   // toList braucht std.collections → bleibt frei ODER Iterator zieht nach std.collections
      fn skipWhile(keep: fn(T) -> bool): Iterator<T>   fn stepBy(n: int): Iterator<T>   fn inspect(f: fn(T) -> void): Iterator<T>
      fn dedupBy(key: fn(T) -> int): Iterator<T>    fn zipWith<B, R>(other: Iterator<B>, f: fn(T, B) -> R): Iterator<R>
  }
  // frei (Constraint auf T): sumBy<T>(it, key: fn(T) -> int), minBy/maxBy<T, K :: [Ordered<K>]>(it, key), average(Iterator<float>), product
  let n = xs.iter().filter((n) => n > 2).count();
  ```
- Extern (Vergleich): Rust `Iterator`-Trait: alle Terminatoren sind Default-Methoden (`count`, `fold`, `any`, `find`, `last`, `nth`, `collect`); Kotlin `Sequence`-Extension-Funktionen; C# LINQ.
- Beschreibung: Nachweis der Machbarkeit: review-examples/terminators.lyr (Default-Methoden mit eigenem Typparameter `fold<A>` kompilieren und laufen). Motivation: heute bricht jede Kette am Ende in einen freien Aufruf mit Import (collections.lyr:47-48 im Review-Beispiel). Nicht-breaking; Spec §11 unberührt (Bibliotheksoberfläche). Bestehender Code: unverändert.
- Prototyp: review-examples/terminators.lyr
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] Minor 2: Container-Ergonomie-Paket (`List.of`, Methoden statt freier Funktionen, `remove(value)`, `Map.entries()`, `Set.toArray`, Array-Sortierung)
- Kategorie: proposal-minor
- Priorität: HIGH
- Intern (Lyric): stdlib/std/collections.lyr
  ```lyr
  pub class List<T> {
      pub static fn of(items: T[]): List<T>;              // Rust vec![], Kotlin mutableListOf
      pub mut fn pushAll(items: T[]): void;  pub mut fn pushAllFrom(other: List<T>): void;
      pub fn slice(start: int, count: int): List<T>;      // string.substring-Kanten
      pub fn toReversed(): List<T>;  pub fn forEach(f: fn(T) -> void): void;
      pub fn map<U>(f: fn(T) -> U): List<U>;  pub fn filter(keep: fn(T) -> bool): List<T>;   // eager, ohne .iter()
      pub fn join(separator: string): string  // nur sinnvoll mit T :: [Display] → frei: joinDisplay<T :: [Display]>(xs, sep)
  }
  // frei mit Constraint (wie heute), aber vollständig: listRemove<T :: [Equatable<T>]>(xs, value): bool,
  // sortArray<T :: [Ordered<T>]>(xs: T[]): void, sortListByKey<T, K :: [Ordered<K>]>(xs, key: fn(T) -> K), binarySearch, dedup
  pub class Map<K, V> { pub fn keys(): Iterator<K>; pub fn values(): Iterator<V>; pub fn entries(): Iterator<(K, V)>;
      pub mut fn getOrInsert(key: K, make: fn() -> V): V;  pub mut fn update(key: K, f: fn(?V) -> V): void;
      pub static fn fromEntries(pairs: (K, V)[]): Map<K, V>;  pub fn capacity(): int; }
  pub class Set<T> { pub static fn of(items: T[]); pub fn toArray(): T[]; pub mut fn addAll(items: T[]); pub fn union(other): Set<T>; … }
  pub class Deque<T> { pub fn toArray(): T[]; }
  ```
- Extern (Vergleich): Kotlin `mutableListOf(1,2).apply { addAll(xs) }.distinct()`, `map.getOrPut(k) { mutableListOf() }`; Rust `m.entry(k).or_insert_with(..)`, `v.sort()`, `v.dedup()`; Python `d.setdefault(k, []).append(v)`; C# `list.AddRange`, `dict.TryAdd`.
- Beschreibung: Belegt in TASKLIST „Ergonomie-Lücken der Container-APIs“ (7 Beispiele mit Zeilenzahlen). Alles additiv; freie `keys/values/entries/listContains/listIndexOf` bleiben (deprecated bis 5.0). `groupBy` ist mit `getOrInsert` eine Zeile: `groups.getOrInsert(w.length(), () => List<string>.empty()).push(w)`. Spec §11: unberührt.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 3: Debug-Ausgabe für Container, Optionals, Tupel und Fehlertypen (`Display` für `List/Set/Map/Deque/JsonValue/IoError…`, `showArray/showOptional`)
- Kategorie: proposal-minor
- Priorität: HIGH
- Intern (Lyric): stdlib/std/core.lyr:88-121, stdlib/std/io/console.lyr:47-52
  ```lyr
  // Sprachgrenze: `extend T[] :: [Display]` über Typkonstruktoren gibt es nicht → freie Funktionen:
  pub fn showArray<T :: [Display]>(xs: T[]): string;        // "[1, 2, 3]"
  pub fn showOptional<T :: [Display]>(o: ?T): string;       // "null" | "5"
  pub fn showPair<A :: [Display], B :: [Display]>(p: (A, B)): string;
  // und Konformanzen, wo der Typ deklariert ist:
  extend List<T :: [Display]> :: [Display] { fn show(): string { … } }     // falls bedingte Konformanz möglich, sonst showList<T>(xs)
  pub enum JsonValue :: [Display, Equatable<JsonValue>] { … }  // show() = serialize(this)
  pub class IoError :: [Throwable, Display]                     // show() = message()
  ```
- Extern (Vergleich): Rust `{:?}` für alles mit `Debug` (derived); Python `repr`; Kotlin/Swift/JS `toString()`/String-Interpolation für Listen/Maps ohne Vorbereitung.
- Beschreibung: Heute unmöglich, siehe Bug „println(array) … scheitern im IR“. Bedingte Konformanz (`extend List<T :: [Display]>`) ist ein Sprachfeature (language-review prüfen); ohne sie freie Funktionen. Nicht-breaking; Spec §11 Punkt 2 (Display-Anker) unverändert.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review (bedingte Konformanz)

### [stdlib-review] Minor 4: Zahlen — Grenzkonstanten, `checked*`/`saturating*`, `parseInt` mit Überlauf → null, `parseUint`, `toString()`-Methoden
- Kategorie: proposal-minor
- Priorität: HIGH
- Intern (Lyric): stdlib/std/math.lyr, stdlib/std/string.lyr:426-520
  ```lyr
  pub let intMax: int = 9223372036854775807;  pub let intMin: int;  pub let uintMax: uint;
  pub let floatMax/floatMin/floatEpsilon/infinity/nan: float;
  pub fn checkedAdd(a: int, b: int): ?int;  checkedSub; checkedMul;  saturatingAdd/Sub/Mul(a, b): int;
  pub fn parseUint(text: string): ?uint;  pub fn parseIntOrThrow(text): int throws ParseError;   // OrThrow-Twin wie überall
  extend int { pub fn toString(): string { return fromInt(this); } pub fn toFloat(): float; pub fn abs(): int; pub fn clamp(lo, hi): int }
  extend float { pub fn toString(): string; pub fn round(): float; pub fn toInt(): ?int /* null bei NaN/∞/außerhalb */ }
  extend string { pub fn toInt(): ?int { return parseInt(this); } pub fn toFloat(): ?float; }
  ```
- Extern (Vergleich): Rust `i64::MAX`, `checked_add`, `"42".parse::<i64>()`; Kotlin `"42".toIntOrNull()`, `Int.MAX_VALUE`; Swift `Int.max`, `Int("42")`; C# `long.MaxValue`, `int.TryParse`; Go `math.MaxInt64`, `strconv.Atoi`.
- Beschreibung: Behebt die Bugs „parseInt überläuft“, „powInt überläuft“ und den Shortcoming „keine Grenzkonstanten“ in einem Paket. Da Overloading seit 3.0 existiert, kann `abs`/`min`/`max`/`clamp` zusätzlich als `int`-Überladung erscheinen (additiv; `absInt` bleibt). Spec: `float as int`-Saturierung dokumentieren (§ Konversionen); §11 unverändert.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 5: `std.string` — Unicode-Klassifikation, `removePrefix/removeSuffix`, `substringFrom`, `lines()`, `equalsIgnoreCase`, `reverse`, `capitalize`, `trimStart/trimEnd` konsistent
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/string.lyr:96-300, 330-370
  ```lyr
  pub fn isLetter(c: char): bool;  isUpper/isLower/isWhitespace Unicode (Native: Rune.IsLetter/IsWhiteSpace); isAsciiAlpha bleibt für ASCII-Parser
  extend string {
      pub fn removePrefix(p: string): string;  pub fn removeSuffix(s: string): string;
      pub fn substringFrom(start: int): string;  pub fn substringTo(end: int): string;
      pub fn lines(): Iterator<string>;  pub fn chars(): Iterator<char>;
      pub fn equalsIgnoreCase(other: string): bool;  pub fn reverse(): string;  pub fn capitalize(): string;
      pub fn startsWith(c: char): bool;  pub fn indexOf(c: char): int;   // Überladungen
  }
  ```
- Extern (Vergleich): Kotlin `removePrefix`, `lines()`, `reversed()`; Rust `strip_prefix`, `char::is_alphabetic`, `chars().rev()`; Python `removeprefix`, `isalpha`; Go `strings.TrimPrefix`, `unicode.IsLetter`.
- Beschreibung: Belege: review-examples/strings.lyr (reverse 6 Zeilen, capitalize mit `length()-1`-Arithmetik, `isAlpha('é') == false`). `chars()` auf `string` würde die Sonderbehandlung „over/range/compact als Eingänge“ (Guide „A chain has to start somewhere“) um den String-Fall verkleinern. Additiv; die `trim`-Inkonsistenz (Bug) wird mitbehoben, indem `trimStart/trimEnd` nativ werden. Spec §11 Punkt 1 (Interpolationshelfer): unverändert.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 6: `std.time` — `Duration.ofHours/ofDays`, `Instant.minus`, Datumsteile, tolerantes `fromIso`/`fromRfc3339`, `Date`-Struct
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/time.lyr
  ```lyr
  pub struct Duration { pub static fn ofHours(h: int); ofDays(d: int);  pub fn totalMinutes/totalHours/totalDays(): int;
      pub fn times(n: int): Duration;  pub fn abs(): Duration;  pub fn format(): string /* "1h 02m 03.500s" */ }
  pub struct Instant { pub fn minus(d: Duration): Instant;  pub fn toDate(): Date;  pub static fn fromRfc3339(text): ?Instant; }
  pub struct Date :: [Equatable, Ordered, Hashable, Display] { year, month, day, hour, minute, second, millis;
      pub fn toInstant(): Instant;  pub fn dayOfWeek(): int;  pub static fn of(y, m, d): ?Date; }
  ```
- Extern (Vergleich): Go `time.Time.Year()`, `t.Add(-d)`; Kotlin `Duration.hours`, `LocalDateTime`; Python `timedelta(days=1)`, `datetime.date()`; C# `TimeSpan.FromHours`, `DateTime.Day`; Swift `Calendar.dateComponents`.
- Beschreibung: Belegt in review-examples/time.lyr (negatives `ofMillis` statt `minus`, Datumsteile per `substring`, `fromIso` ohne Millis → null). Die Kalenderfunktionen existieren privat (time.lyr:150-330). Additiv. Lokale Zeit: separater Native `localOffsetMillis` (osAccess), spätere Stufe.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 7: `std.test` — `assertNotEq`, `assertNull/NotNull`, `assertClose`, `assertThrows`, `fail`, String-Diff
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/test.lyr
  ```lyr
  pub fn assertNotEq<T :: [Equatable<T>, Display]>(actual: T, unexpected: T): void;
  pub fn assertNull<T :: [Display]>(o: ?T): void;  pub fn assertNotNull<T>(o: ?T): T;   // liefert den Wert, wie Rust unwrap im Test
  pub fn assertClose(actual: float, expected: float, epsilon: float): void;
  pub fn assertThrows<E :: [Throwable]>(body: fn() -> void throws E): E;   // braucht throws-Typparameter (prototyper 01) — bis dahin: assertPanics? nicht fangbar → nur mit Sprachfeature
  pub fn fail(message: string): void;  pub fn assertContains(text: string, needle: string): void;
  ```
- Extern (Vergleich): JUnit `assertThrows`, `assertEquals(delta)`; pytest `raises`, `approx`; Rust `#[should_panic]`, `assert!(matches!(..))`; XCTest `XCTAssertThrowsError`.
- Beschreibung: Belegt in review-examples/testproj/tests/t.lyr (Float-Vergleich, unsichtbarer String-Unterschied). Additiv. `assertThrows` hängt am Bug „throws-Klausel mit Typparameter wird nicht substituiert“ (prototyper).
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper

### [stdlib-review] Minor 8: `std.hash` (sha256/sha1/md5/crc32) und `hashCombine`
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): neues Modul; stdlib/std/core.lyr:288 (`hash` als Anker)
  ```lyr
  module std.hash;   // capability-frei wie std.random.secureRandom
  pub fn sha256(bytes: uint8[]): uint8[];  pub fn sha1(bytes: uint8[]): uint8[];  pub fn md5(bytes: uint8[]): uint8[];
  pub fn crc32(bytes: uint8[]): int;  pub fn hashCombine(seed: int, value: int): int;   // boost::hash_combine
  // Nutzung: hexEncode(sha256(file.bytes(p)!))
  ```
- Extern (Vergleich): Go `crypto/sha256`, `hash/crc32`; Python `hashlib`; .NET `SHA256.HashData`; Node `crypto.createHash`.
- Beschreibung: Belegt: `grep -rniE 'sha|crc|md5' stdlib/std` → nichts. Spec §11 Punkt 3: vier neue Native-Namen im Contract (wie bei `secureRandom`). Additiv.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 9: `std.io.path.normalize/relative/components`, `std.io.file.absolute/walk/removeAll/modifiedAt/writeLines`, `std.process.output` + `cwd/env`
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/io/path.lyr, stdlib/std/io/file.lyr, stdlib/std/process.lyr
  ```lyr
  // path (capability-frei): normalize(p): string; relative(from, to): ?string; components(p): string[]; join(parts: string[]): string
  // file (fileAccess): absolute(p): string; walk(dir): ?string[] /* rekursiv, Pfade */; removeAll(dir): bool; modifiedAt(p): ?Instant (→ Import std.time = osAccess; alternativ ?int epochMillis);
  //   writeLines(p, lines: string[]): bool; isEmptyDir
  // process (processAccess): pub struct Output { code: int, stdout: uint8[], stderr: uint8[] }
  //   pub fn output(program: string, args: string[]): ?Output;   // yieldet intern, wie start
  //   pub fn startIn(program, args, cwd: string, env: (string, string)[]): ?Child;
  ```
- Extern (Vergleich): Go `filepath.Clean/Rel`, `os.RemoveAll`, `exec.Command().Output()`; Python `os.path.normpath`, `shutil.rmtree`, `subprocess.run(capture_output=True)`; Rust `fs::remove_dir_all`, `Command::output()`.
- Beschreibung: Belegt in review-examples/file.lyr (flaches Löschen von Hand, `a/../b`) und review-examples/osproc.lyr (15 Zeilen Capture). Additiv; Spec §11 Punkt 3: neue Native-Namen (`walk`, `removeAll`, `modifiedAt`, `procStartIn`).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Minor 10: `std.json` — `path()`/nicht-optionale Kette, Builder, `ToJson`/`FromJson`-Anker, `Equatable`/`Display`; `std.option` — `orElse`, `getOrCall`, `flattenArray`; `std.random` — `Random.fresh()`, `nextFloatRange`, Array-Überladungen
- Kategorie: proposal-minor
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/json.lyr, stdlib/std/option.lyr, stdlib/std/random.lyr
  ```lyr
  // json
  pub enum JsonValue :: [Equatable<JsonValue>, Display] { …; pub fn get(name: string): JsonValue /* Null statt null */; pub fn index(i: int): JsonValue;
      pub fn path(dotted: string): ?JsonValue; pub static fn object(members: (string, JsonValue)[]): JsonValue; pub static fn array(items: JsonValue[]): JsonValue; }
  pub interface ToJson { fn toJson(): JsonValue; }   pub fn serializeOf<T :: [ToJson]>(v: T): string;
  // option
  pub fn orElse<T>(o: ?T, make: fn() -> T): T;  pub fn okOr<T, E>(o: ?T, e: E): Result<T, E> /* falls prototyper 01 kommt */;
  // random
  pub class Random { pub static fn fresh(): Random /* aus secureRandom geseedet */; pub mut fn nextFloatRange(lo, hi): float; pub mut fn nextIntRange… mit Rejection-Sampling }
  pub fn shuffleArray<T>(r, xs: T[]): void;  pub fn choiceArray<T>(r, xs: T[]): ?T;  pub fn sample<T>(r, xs: List<T>, n: int): List<T>
  ```
- Extern (Vergleich): serde_json `doc["a"]["b"]` (Index → Null); JS `JSON.stringify`; Rust `Option::or_else`, `ok_or`; Python `random.random()` ohne Seed, `random.sample`; Kotlin `Random.Default`.
- Beschreibung: Belegt in review-examples/json.lyr:40-52 (5-fach-Null-Treppe), review-examples/random.lyr:9-14 (8 Zeilen, um einen ungeseedeten Generator zu bauen). Additiv.
- Prototyp: -
- Betroffene Teammitglieder informiert: prototyper (okOr/Result)

### [stdlib-review] Major 1: Fehlermodell vereinheitlichen — `Result<T, E>` in `std.result` als dritte Antwortform neben `?T` und `throws`, `OrThrow`-Twins darauf abbilden
- Kategorie: proposal-major
- Priorität: HIGH
- Intern (Lyric): stdlib/std/io/file.lyr:60-190 (17 `OrThrow`-Twins), stdlib/std/json.lyr, stdlib/std/encoding.lyr, stdlib/std/time.lyr, stdlib/std/io/net.lyr, stdlib/std/process.lyr, stdlib/std/io/stream.lyr (jeweils `lastErrorKind()`-Native + Twin-Funktion)
  ```lyr
  // Heute: jede fehlbare Operation existiert ZWEIMAL (text/textOrThrow) und die stille Form verliert den Grund;
  // ein `lastErrorKind()`-Seiteneffekt trägt den Grund zwischen beiden Aufrufen (file.lyr:33-40, net.lyr, process.lyr, stream.lyr — 4 Kopien).
  // Soll: pub enum Result<T, E> { Ok(T), Err(E) } (prototyper Prototyp 01, lib-variante.lyr) und
  pub fn text(path: string): Result<string, IoError>;      // EINE Form; `.ok()` gibt ?string, `.orThrow()` wirft
  let content = file.text(p).unwrapOr("");
  ```
- Extern (Vergleich): Rust `fs::read_to_string(p)?` (ein Rückgabetyp, `?` propagiert, `.ok()` wandelt in Option); Go `v, err := os.ReadFile(p)`; Swift `throws` + `try?` (ein Aufruf, drei Nutzungsformen); Kotlin `runCatching { }.getOrNull()`.
- Beschreibung: Das `OrThrow`-Twinning ist die größte API-Verdopplung der stdlib (≈45 Funktionen) und die `lastError*`-Natives sind ein globaler Zustand, der bei Tasks (zwei Coroutinen, zwei `readSome`, ein `lastErrorKind`) im Prinzip überschreibbar ist. Mit `Result` (Bibliothekstyp, kein Sprachfeature) und den Kombinatoren `ok()/unwrapOr()/orThrow()/map/andThen` reicht EINE Funktion pro Operation. Breaking: Rückgabetypen ändern sich → Major (5.0); Übergang: Twins mit `@Deprecated(until = "5.0")`. Spec §11 Punkt 3 (Native-Namen) schrumpft um `lastErrorKind/lastErrorDetail` ×4; §9 (Fehler) bekommt Result als Bibliothekskonvention. Voraussetzung: prototyper Prototyp 01 + Bug „throws-Klausel mit Typparameter“ (für `orThrow(): T throws E`).
- Prototyp: /tmp/…/scratchpad/team2/prototypes/01-result-type-try-operator/lib-variante.lyr (prototyper)
- Betroffene Teammitglieder informiert: prototyper, language-review

### [stdlib-review] Major 2: Überladung statt Typ-Suffix-Namen (`abs/absInt`, `min/minInt`, `sum/sumFloat`, `formatInt/formatFloat`, `fromInt/fromFloat`, `sortList/sortListBy`)
- Kategorie: proposal-major
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/math.lyr:36-65, 170-180; stdlib/std/iter.lyr:528-548; stdlib/std/fmt.lyr:28-37; stdlib/std/string.lyr:16-28; stdlib/std/collections.lyr:638-650; docs/guide/13-standard-library.md „One name per type, in the library“ (benennt die Entscheidung als offen)
  ```lyr
  // Soll: pub fn abs(v: int): int;  pub fn abs(v: float): float;  pub fn min(a: int, b: int): int; …  sum(Iterator<float>)
  // format<T :: [Display]>? — nein: fmt braucht die Typfamilie; format(value: int, spec) / format(value: float, spec) als Überladungen
  ```
- Extern (Vergleich): C# `Math.Abs(int)/Math.Abs(double)`, Kotlin `abs(Int)/abs(Double)`, Swift `abs<T: SignedNumeric>`, Go `math.Abs` nur float (Go hat KEINE Überladung — Lyric-Namen sind der Go-Stil).
- Beschreibung: Die Sprache hat Überladung seit 3.0; die Suffixnamen sind ein historisches Artefakt, das der Guide selbst so benennt. Additive Stufe (Minor, sofort): Überladungen HINZUFÜGEN, alte Namen `@Deprecated(until = "5.0")`. Breaking Stufe (Major): Entfernen. Spec §11 Punkt 1: `fromInt/fromFloat` sind compiler-gebunden — die Interpolations-Helfer behalten ihre Namen (oder der Contract wird auf eine überladene `from(value)` umgestellt, was Bytecode-Bindung ändert — Format-Bump). Empfehlung: Helfer-Namen im Contract lassen, nur die Nutzer-Oberfläche überladen.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review

### [stdlib-review] Major 3: Modul-Restrukturierung — `std.os.nowMillis/nowNanos/sleep` nach `std.time` (Monotonic/Stopwatch), `std.io.console` capability-frei bleibt, `std.iter` + `std.collections` zusammenführen (Iterator.toList)
- Kategorie: proposal-major
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/os.lyr:40-52 (`nowMillis`, `nowNanos`, `sleep`), stdlib/std/time.lyr:20 (eigener `nowMillis`-Native, Duplikat), stdlib/std/iter.lyr:648-660 (`collectArray` quadratisch, weil `std.iter` `std.collections` nicht importieren darf), stdlib/std/collections.lyr:275-290 (`collect` deshalb dort)
  ```lyr
  // Soll: std.time { Instant.now(), Monotonic.now(): Monotonic, m.elapsed(): Duration, sleep(d: Duration) }
  //       std.os behält env/args/exit/platform/… ; std.time.nowMillis-Native bleibt EIN Native.
  //       Iterator<T>.toList() als Methode → Iterator und List im selben Modul (std.collections importiert std.iter heute; Umkehr oder Zusammenlegung)
  ```
- Extern (Vergleich): Rust `std::time::Instant::now().elapsed()`, `std::thread::sleep(Duration)`; Go `time.Since(start)`, `time.Sleep(d)`; Kotlin `measureTime {}`; .NET `Stopwatch`.
- Beschreibung: Belegt: review-examples/time.lyr:16-19 (Zeitmessung über `std.os.nowNanos` + Division, `sleep(int)` in `std.os`, `Duration` in `std.time` — drei Module für „miss und warte“). Zwei Natives (`std.os.nowMillis`, `std.time.nowMillis`) liefern dasselbe. Breaking, wenn Namen wandern (Deprecated-Aliase bis 5.0 möglich → dann additiv). Spec §11 Punkt 3: Native-Namen ändern sich (Bytecode-Bindung symbolisch → alte Namen als Aliase registrierbar, wie `std.string.raw*`).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [stdlib-review] Major 4: Panik-Grenzfälle in Optional/`bool` überführen oder klar in der Spec verankern (`split("")`, `substring` außerhalb, `readSome(max<=0)`, `formatInt` mit ungültigem Spec, `List.get` außerhalb)
- Kategorie: proposal-major
- Priorität: LOW
- Intern (Lyric): stdlib/std/string.lyr:255 (`split("")` panikt), src/Lyric.Vm/NativeRegistry.cs:1188-1200, stdlib/std/bytes.lyr:9-16, stdlib/std/fmt.lyr:30-33 (ungültiger Spec = Panik), stdlib/std/io/net.lyr `readSome` (max<=0 Panik), stdlib/std/collections.lyr:121-135
- Extern (Vergleich): Rust paniziert bei Index außerhalb, gibt `Option` bei `get(i)`; Python `ValueError` bei `"".split("")`, fangbar; Go `strings.Split(s, "")` splittet in Zeichen; Kotlin `split("")` liefert Zeichen; C# `Split("")` gibt den String. Rust `chars().rev()` etc.
- Beschreibung: Lyric-Paniken sind NICHT fangbar (core.lyr:20-22). Das ist konsequent für Programmierfehler (`get(-1)`), aber `split("")` auf Nutzereingabe oder ein Format-Spec aus einer Konfigurationsdatei sind Laufzeitzustände — ein Server stirbt an einem leeren Trennzeichen. Vorschlag: pro Modul die Panik-Liste im Doc-Header und in Spec §11 (Bibliothekskonvention „Panik nur bei Programmierfehlern; Eingabeabhängiges antwortet ?T/bool“); `split("")` → Zeichen-Split (Go/Kotlin-Semantik) statt Panik (breaking, Major); `List.tryGet(i): ?T` additiv (Minor).
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review (Panik-Konvention in Spec §9)

### [stdlib-review] Major 5: `Wait`-Enum erweiterbar machen (Signal/Channel) und `spawn` mit Handle — `std.task` als Grundlage für Channel/Timeout/join
- Kategorie: proposal-major
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/task.lyr:10-40 (`pub enum Wait` — ein Nutzer-`match` über `Wait` bricht bei jeder neuen Variante), stdlib/std/task.lyr:96-100 (`spawn(): void`)
  ```lyr
  pub enum Wait { Now, Sleep(int), Readable(int), Writable(int), Interrupt, Signal(int) /* NEU: Wecker-Id */ }
  pub class Channel<T> { pub static fn bounded(n: int); pub fn send(v: T): Coroutine<Wait> … /* oder yield intern */; pub fn receive(): ?T; }
  pub fn spawnTask<T>(task: Coroutine<Wait>): TaskHandle;  pub fn join(h: TaskHandle): Coroutine<Wait>;  pub fn timeout(ms: int, task): bool
  pub fn sleep(ms: int): void { yield Wait.Sleep(ms); }   // heute schreibt jeder `yield Wait.Sleep(ms)` in eine void-Hilfsfunktion
  ```
- Extern (Vergleich): Go `chan`/`select`/`WaitGroup`; Kotlin `Channel`, `withTimeout`, `Deferred.await`; Rust tokio `mpsc`, `JoinHandle`, `timeout`; Python `asyncio.Queue`, `gather`, `wait_for`.
- Beschreibung: Belegt in review-examples/tasks_demo.lyr (Busy-Polling mit `Wait.Now`, Zustandsklasse für ein bool). `Wait` ist `pub enum` und in Nutzercode matchbar → jede neue Variante ist breaking (Major) — es sei denn, die Spec erklärt `Wait` für nicht erschöpfend matchbar (`_`-Pflicht, wie Rust `#[non_exhaustive]`; Sprachfeature → language-review). Hängt am Bug „generische Coroutine-Funktion wird nicht gelowert“ (language-review, HIGH) für `Channel<T>`.
- Prototyp: -
- Betroffene Teammitglieder informiert: language-review, prototyper

### [stdlib-review] Stärken: was Lyric in der stdlib besser macht als die Vergleichssprachen
- Kategorie: strength
- Priorität: MEDIUM
- Intern (Lyric): stdlib/std/string.lyr:36-40 (Code-Point-Semantik überall), stdlib/std/io/file.lyr:9-30 („zwei Formen, jede sagt welche“), stdlib/std/task.lyr + io/net.lyr + io/stream.lyr + process.lyr (Warten IM Modul), stdlib/std/collections.lyr:301-330 (`Deque` bewusst ohne Iteration), stdlib/std/json.lyr:765-800 (`intFits`: Ganzzahlen exakt über den vollen int-Bereich), stdlib/std/random.lyr:8-20 (deterministisch vs. `secureRandom` explizit getrennt), stdlib/std/encoding.lyr (strikte Decoder mit Offset+Erwartung), stdlib/std/time.lyr:70-80 („compare ist drei Vergleiche, keine Subtraktion“), stdlib/std/core.lyr:330-345 (kein `Hashable<float>` — begründet)
- Extern (Vergleich):
  - Unicode: `"Grüße 👋 Welt".length() == 12` (review-examples/strings.lyr) — JS/C#/Java liefern 13 (UTF-16), Rust `len()` 16 Bytes, nur Python/Swift zählen Skalare. `indexOf` liefert Code-Point-Positionen, die zu `substring` passen; `charAt` ist ehrlich O(n) und heißt deshalb nicht `s[i]`.
  - I/O-Antwortformen: `?T` für Lesen, `bool` für Operationen, `OrThrow` für den Grund — eine dokumentierte Konvention, die Go (`err != nil` überall) und Python (Exceptions überall) nicht haben; `readSome`s „drei Wahrheiten“ (Bytes / leeres Array = EOF / null = Fehler) sind über net, stream, process identisch.
  - Nebenläufigkeit: „no signature anywhere says waits“ — keine Async-Farbe (Rust/JS/C#/Kotlin/Swift haben alle `async`-Färbung), ein `Wait`-Enum statt eines Runtime-Objektmodells; `Wait.Interrupt` macht Ctrl+C und `interrupt()` zu EINEM Mechanismus.
  - Capabilities pro Modul (`fileAccess`, `osAccess`, `networkAccess`, `processAccess`) — ein Embedding-Host gewährt explizit; kein Vergleichs-Stdlib hat das (Deno kommt am nächsten).
  - JSON: Ganzzahlen exakt bis int64 (JS verliert ab 2^53; Python/Rust serde ok), Tiefenlimit 128 statt Stack-Panik, `parseOrThrow` mit Zeile/Spalte/Erwartung.
  - Sortierung stabil und bottom-up (Rust stabil, Go `sort.Slice` instabil, JS seit ES2019 stabil).
  - Jede öffentliche Funktion dokumentiert und mit BEGRÜNDUNG (warum kein `s[i]`, warum `rangeInclusive` eigenständig, warum `compact` ein Array nimmt) — eine Qualität, die in keiner Vergleichs-stdlib so konsequent ist; ein Test pinnt die Vollständigkeit der Doku.
- Beschreibung: Diese Entscheidungen sollten bei den Minor/Major-Vorschlägen unangetastet bleiben; die Vorschläge oben ergänzen die Ergonomie, ohne die Antwortformen-Konvention zu verwässern (Result wäre die Verallgemeinerung, nicht der Bruch).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [prototyper] Prototyp 08: `throw` als Ausdruck (Typ `never`)
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.Statements.cs:38,224 (throw nur Statement); Sema/LyrType.cs:23,122 (`NeverType` EXISTIERT: "the return type of panic; a bottom type, not nameable"), Flow.cs:18 (never-ExprStmt divergiert); stdlib/std/core.lyr:23,44-60 (panic/todo/unreachable als `void` deklariert). Ist-Beispiel — drei Umwege:
  ```lyr
  _ => { throw EvalError { what = "not a value" }; }          // Block-Arm statt Ausdrucks-Arm
  let v = env.get(id); if (v == null) { throw EvalError { … }; } return v;   // statt `?? throw`
  if (b == 0) { throw …; } return a / b;                       // statt if-else-Ausdruck
  ```
- Extern (Vergleich): Kotlin — `env[id] ?: throw EvalError("unbound $id")`, `else -> throw …` in `when`; `throw` hat Typ `Nothing`. C# 7 throw-Ausdrücke, Swift `Never`, Rust `!`.
- Beschreibung: Soll: `Primary += ThrowExpr = 'throw' UnaryExpr`, ThrowStmt wird ExprStmt-Fall; Typ `never`, Unifikationsregel "never trägt nichts bei" (§6.9), `?T ?? never` = `T`; `panic`/`todo`/`unreachable` erhalten den Rückgabetyp `never` schreibbar (§9.4 sagt es schon, der Typ nicht); `never` in die BuiltinType-Liste. Eindeutig (throw reserviert, heute PAR0002). Zeilen: `?? throw` Ist 3 + Hilfsname / Soll 1. Aufwand: Parser ~15, Sema ~40, Lowering ~30 (never-Wert lowern + unreachable — DIE LÜCKE IST SCHON DA, siehe Bug unten), VM 0, stdlib: Signaturen. Breaking nein (Minor). Empfehlung: Sprachfeature, klein; Lowering-Teil ohnehin fällig.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/08-throw-expression/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] `panic(...)` in Wert-Position (if-else-Ausdruck, rechts von `??`) stürzt das Lowering ab
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1393 ("expression … produced no value") via LowerIfExpr :1727 bzw. LowerCoalesce :2786; Sema akzeptiert (LyrType.cs:122 NeverType unifiziert), lyricspec §9.4 ("returns never: flow analysis treats everything after it as unreachable"). Repro (prototypes/08-throw-expression/probe-never.lyr, probe-never2.lyr):
  ```lyr
  fn pick(b: bool): int { return if (b) 1 else panic("no"); }
  fn lookup(id: string, env: Map<string, int>): int { return env.get(id) ?? panic("unbound " + id); }
  // → Unhandled exception. Lyric.Core.InternalCompilationException: lowering: expression at … produced no value
  ```
- Extern (Vergleich): Rust `x.unwrap_or_else(|| panic!())`, `if b { 1 } else { panic!() }` — `!` in Wert-Position ist Alltag; Kotlin `?: error("…")`.
- Beschreibung: Sema und Lowering widersprechen sich: die Sema hat einen Bottom-Typ und lässt ihn in Arme/Operanden, das Lowering erwartet einen Wert. Entweder SEM-Fehler ("a never-typed expression cannot stand in a value position", dann §9.4 präzisieren) oder — besser, und die Grundlage für Prototyp 08 — `LowerExprAs` lowert den Aufruf, emittiert `unreachable` und liefert einen Dummy des erwarteten IR-Typs. Kein Absturz darf bleiben (Spec Kap. 2/12: ein ill-formed Programm bekommt eine Diagnose).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/08-throw-expression/probe-never.lyr
- Betroffene Teammitglieder informiert: language-review, ir-codegen (Inbox), semantic (Inbox)

### [prototyper] Prototyp 09: Slices / Teilbereiche `xs[a..b]`
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): src/Lyric.Frontend/Parsing/Parser.cs:74,113-118 (`..` parst überall als RangeExpr, Stufe 7), Sema LYR-SEM0090 ("a range is a loop head, not a value") — `xs[1..3]` wird heute geparst und dann abgelehnt (probe-range-index.lyr); lyricspec §3.3, §7.2. Ist-Beispiel — Teilarray von Hand:
  ```lyr
  fn sliceChars(cs: char[], from: int, to: int): char[] {
      var out = [' '] * (to - from);
      for (k in from..to) { out[k - from] = cs[k]; }
      return out; }
  let value = arg.substring(7, arg.length() - 7);     // (start, count) statt (from, to)
  ```
- Extern (Vergleich): Python `cs[:i]`, `xs[3:]`, `arg[7:]` (Kopie); Rust `&cs[a..b]` (View, Lifetimes), Go `xs[a:b]` (View, Aliasing) — bewusst NICHT das Modell.
- Beschreibung: Soll: `IndexSuffix = '[' ( Expr | SliceRange ) ']'`, `SliceRange = [Expr] ('..'|'..=') [Expr]`; Kopie, Typ `T[]`, Panik außerhalb/`a > b`; §7.2 bekommt die Index-Klammer als zweite Range-Stelle. Keine Parser-Kollision (nur offene Grenzen `[..b]`/`[a..]` sind neu). Kein `s[a..b]` (§3.3 hält string unindexierbar) — `s.toChars()[a..b]` oder stdlib `s.slice(from, to)`. LIB-VARIANTE deckt 90 %: `slice<T>(xs: T[], from, to)` + `string.slice(from, to)` + `List.slice` (~15 Z., an stdlib-review). Zeilen: Wort ausschneiden Ist 3 + 6 / Soll 1 / Lib 1 / Python 1. Aufwand: Parser ~20, Sema ~40, Lowering ~50 (Desugar), VM 0. Breaking nein. Empfehlung: ERST stdlib `slice`, dann Sprachform als Minor.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/09-slices/
- Betroffene Teammitglieder informiert: language-review, stdlib-review (`slice` auf T[]/string/List)

### [prototyper] Nachtrag zu Prototyp 03: Primärform ist jetzt das Swift-Modell (Konformanz ohne Body = Synthese)
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): TypeChecker.cs Konformanzprüfung (LYR-SEM0020 "does not implement 'equals'") wird zur Synthese-Stelle; Grammatik UNVERÄNDERT. Zugehörig: language-review "Kein Equatable/Hashable/Ordered auf Enums und Structs ohne Handarbeit (kein Derive)".
  ```lyr
  struct Coord :: [Hashable<Coord>, Ordered<Coord>, Display] { x: int, y: int, label: string, }   // kein Methodenkörper
  enum Cell :: [Equatable<Cell>, Display] { Empty, Wall, Item(int), }
  ```
- Extern (Vergleich): Swift — `struct Coord: Hashable, Comparable { … }` ohne Body synthetisiert `==`/`hash(into:)`; Rust `#[derive(...)]`.
- Beschreibung: soll.lyr und README von 03 umgestellt; `derive`-Wort und `@Derive` nur noch als Variante B/B' dokumentiert. Bewertung unverändert (Aufwand Parser 0 statt klein, Sema mittel, Lowering mittel-groß). Zusätzlich abgestimmt: eigene Methode gewinnt; Unit-Enums immer; generische Typen brauchen die Constraint geschrieben.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/03-derive-equatable-hashable-display/
- Betroffene Teammitglieder informiert: language-review

### [language-review] Stärke: geprüfte `throws`-Klauseln mit impliziter Propagation und typisierter Abdeckung
- Kategorie: strength
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/09-errors.md:41-51, review/errors.lyr:78-85 (`loadSettings` propagiert drei Ebenen ohne ein Zeichen pro Aufruf; `catch (e: ConfigError)` deckt den Aufruf vollständig ab; ein vergessener Rethrow ist LYR-SEM0034 am richtigen Ort).
  ```lyr
  fn loadSettings(name: string): Settings throws ConfigError {
      let text = load(name);                       // ?T: Abwesenheit
      if (text == null) { throw ConfigError { kind = ConfigErrorKind.Missing, source = name }; }
      return validate(name, parse(name, text));    // beide werfen — keine Markierung am Aufruf
  }
  ```
- Extern (Vergleich): Rust (`?` an jedem Aufruf, `Box<dyn Error>`/`thiserror` für Fehlerketten), Go (`if err != nil { return err }` — 3 Zeilen pro Aufruf), Java (checked exceptions, aber `catch (Exception)` fängt still zu viel), Swift (`throws` untypisiert bis 6.0; `try` an jedem Aufruf), Kotlin/C#/TS/Python (ungeprüft: nichts sagt, was fliegt), Zig (`!T` Error-Union mit `try` — nah dran, aber Fehler sind nur Enums ohne Payload).
- Beschreibung: Lyric kombiniert Zigs/Javas statische Prüfung mit Kotlins Schreibaufwand (null Zeichen am Aufruf) und trennt sauber "whether" (?T) von "why" (throw) (§9.0). Die Doktrin `OrThrow`-Zwilling ist besser als Rusts `Result`-Monokultur für Skripte. Was fehlt, steht in den Shortcoming-Einträgen (throw als Ausdruck, typed throws auf Funktionstypen/Typparametern, try-Ausdruck).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: stackful Coroutinen ohne Function Coloring
- Kategorie: strength
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/10-coroutines.md:81-86, 125-171; review/probes/p24_yield_helper.lyr (yield in Helfer, in Lambda, defer läuft am Ende):
  ```lyr
  fn pause(n: int): void { for (i in 0..n) { yield i; } }      // keine Markierung
  fn gen(): Coroutine<int> { defer println("gen done"); pause(2); let f = (): void => { yield 99; }; f(); yield 7; }
  ```
- Extern (Vergleich): C#/TS/Python/Rust/Swift/Kotlin `async`/`suspend` färbt jeden Aufrufer ("what color is your function"); Go-Goroutinen sind ungefärbt, aber präemptiv und mit Scheduler-Laufzeit; Lua-Coroutinen sind das Vorbild (C-Boundary-Regel übernommen). Zig hat async entfernt.
- Beschreibung: Für die Zielgruppe Embedding/Game-Scripting (Cutscenes, Tasks, `std.task`) ist das der richtige Schnitt: eine Wartefunktion in einer Bibliothek ändert keine Signatur im Nutzercode. Preis: Fehler zur Laufzeit statt Compile-Zeit (Panik bei falschem Kettentyp) — bewusst dokumentiert (§10a). Der Bug "generische Coroutine-Funktion" (eigener Eintrag) ist die eine Lücke.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: Flow-Narrowing mit frühem Exit, `&&`/`||`-Propagation und Definite Assignment in einer kompilierten Sprache
- Kategorie: strength
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/07-statements.md:70-110, 7.7; review/probes/p01_iflet.lyr:8-10, review/cli.lyr:38-41
  ```lyr
  let m = parseMode(value);
  if (m == null) { throw UsageError { text = f"unknown mode '{value}'" }; }
  opts.mode = m;          // m: Mode — der Exit hat bewiesen
  ```
- Extern (Vergleich): TypeScript (gleiches Modell, aber unsound bei Aliasing), Kotlin (Smart Casts, nur `val`), Swift/Rust (kein Narrowing — `if let`/`guard let` binden neu), C# (`is not null`-Patterns), Go/Zig (nichts; Zig `orelse return`).
- Beschreibung: Lyrics Narrowing ist enger als TS (nur Identifier gegen `null`), aber SOUND (Lambda-Staleness → checked unwrap, §7.4). Mit if-let/let-else (Vorschlag) wäre es vollständig; ohne sie ist es schon besser als Rust/Swift für den Alltagsfall "prüfen und weiter".
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: Operator = Interface-Methode, mehrfache Konformanz wählt über den rechten Operanden, kein zweiter Dispatch
- Kategorie: strength
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/06-operators.md:3-7, 29-47, 52-58; review/container.lyr:73-83 (`Add<Money, Money>`, `sumAll<T :: [Add<T, T>]>` läuft für Money UND int); docs/guide/07-interfaces.md `Mul<Vec2, Vec2>` + `Mul<float, Vec2>`.
  ```lyr
  struct Money :: [Add<Money, Money>, Display, Equatable<Money>] { cents: int, fn add(other: Money): Money { ... } }
  fn sumAll<T :: [Add<T, T>]>(xs: T[], zero: T): T { var acc = zero; for (x in xs) { acc = acc + x; } return acc; }
  ```
- Extern (Vergleich): Rust (`impl Mul<f32> for Vec2` — gleiches Modell, Vorbild), C#/Kotlin/Swift (`operator`-Schlüsselwort/`operator fun`, ohne Constraint-Bindung an Generics in C# < 11), Python (`__mul__` dynamisch), Go/Zig (kein Operator-Overloading; Zig `.add()`-Methoden).
- Beschreibung: Eine Regel für `+ - * / == < as [ ]` (Indexable) — weniger Sonderfälle als jede Vergleichssprache außer Rust; und nach Monomorphisierung ein direkter Aufruf. Was fehlt: `Neg`/`Rem`, Compound auf Feldziel, `?T == ?T` (Einträge vorhanden).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: deterministische Numerik (Wrapping, Shift-Maskierung, sättigendes float→int) plus Literal-Adaption
- Kategorie: strength
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/03-types.md:29-64, 121-137, ~/dev/projects/lyricspec/spec/06-operators.md:20-24; review/probes/p14_numeric.lyr (u8 250+10 → 4; 1<<62 *4 → 0; `sqrt(16)` adaptiert; `let n: int8 = 200` ist Fehler).
- Extern (Vergleich): C/C++/Zig (UB bzw. Panik/UB bei Overflow, Shift ≥ Breite UB), Rust (Debug-Panik, Release-Wrap — zwei Verhalten), Go (wraps, kein Shift-UB, ähnlich gut), C#/Java (wrap; `checked` optional), Swift (Trap), Python (Bignum). float→int: Rust sättigt (seit 1.45), C# undefiniert für NaN, Java sättigt.
- Beschreibung: "Identisch auf jeder Plattform" ist für eingebettete Skripte (Replays, Determinismus in Spielen) das richtige Ziel; die Adaption ausschließlich für LITERALE vermeidet die C-Promotionsfalle und zwingt `int`/`int64` sauber auseinander. Preis: `s as float / xs.length as float` (zwei Casts, Go-Niveau) — akzeptabel.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: Attribute sind Structs mit Marker-Interfaces und Compile-Time-Werten; Capability-Bits pro Modul
- Kategorie: strength
- Priorität: MEDIUM
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/04-modules.md:288-314 (Capabilities), 327-390 (Attribute), docs/guide/15-attributes.md (`@On(Event.Damage)`, `@[Traced, On(...)]`, `@Deprecated { until = "3.5" }` mit erzwungener Uhr).
- Extern (Vergleich): C# (Attribute = Klassen + Reflection zur Laufzeit), Rust (`#[derive]`/proc macros — mächtig, aber Compile-Zeit-Kosten und Komplexität), Java (Annotations + Reflection), Python (Decorators laufen), Go/Zig/Swift/Kotlin (Struct-Tags bzw. Annotationen ohne typisiertes Vokabular). Capabilities: kein Vergleichs-Mainstream hat sie in der Sprache (Deno/WASI auf Laufzeitebene).
- Beschreibung: Ein Enum-Vokabular in Attributen (`Layout.Seperate` ist ein Compile-Fehler statt einer toten Zeile) und eine Deprecation-Uhr, die den Build bricht, sind Tooling-Features IN der Sprache, die Rust/Kotlin nur über Lints haben. Grenze: keine Derive-Attribute (Eintrag "kein Derive"), Metadaten nur an Top-Level-Deklarationen.
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] Stärke: keine Nullreferenzen, keine Vererbung, keine impliziten Konvertierungen — Exhaustiveness nennt die fehlenden Varianten
- Kategorie: strength
- Priorität: LOW
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/03-types.md:3-6, 139-151, ~/dev/projects/lyricspec/spec/07-statements.md:283-286; review/probes/p23_exhaust.lyr (`error[LYR-SEM0050]: match on 'S' is not exhaustive — missing case(s): 'B', 'C'`), review/probes/p10_structs.lyr (Struct/Class-Split ist an jeder Stelle vorhersagbar).
- Extern (Vergleich): Kotlin/Swift (Klassenvererbung + Protokolle: zwei Mechanismen), C#/Java/TS (Vererbung, `null` überall bzw. `?`-Nachrüstung), Go (Embedding, nil-Interfaces), Rust (gleich: Traits statt Vererbung, `Option`), Zig (kein `null` außer Optional, gleich), Python (dynamisch).
- Beschreibung: Der Struct(Wert)/Class(Referenz)-Split ohne Vererbung ist Go/Rust-Niveau und einfacher als Swift/Kotlin; `?T` ist die EINZIGE Absenz. Die eine Schwäche des Split ist die Nicht-Nestbarkeit von `?` (Major-Vorschlag).
- Prototyp: -
- Betroffene Teammitglieder informiert: -

### [language-review] MAJOR-Vorschlag: nestbare Optionals (oder `Option<T>` als Enum) — `?` schachtelt nicht, und die Rechnung kommt in Generics an
- Kategorie: proposal-major
- Priorität: HIGH
- Intern (Lyric): Spec ~/dev/projects/lyricspec/spec/03-types.md:68-70 (`??T` ist kein Typ), ~/dev/projects/lyricspec/spec/07-statements.md:159-163 (LYR-SEM0091: for über `(?T)[]`), ~/dev/projects/lyricspec/spec/10-coroutines.md:98-104 (LYR-SEM0080: `Coroutine<?T>.next()` verweigert), STATUS.md §Still open "vier Wege, ist das null zu fragen, in einem generischen Body" (C1), stdlib/std/collections.lyr:41-44, 493 (`List<?T>`/`Map<K, ?V>` nicht instanziierbar — stdlib-review-Eintrag), stdlib/std/iter.lyr:254 (`compact` nimmt ein Array, weil `Iterator<?T>` nicht existieren kann).
  ```lyr
  let slots: (?int)[] = [10, null, 20];
  for (x in slots) { }                 // error[LYR-SEM0091]
  let cache = Map<string, ?int>.empty();   // LYR-IR0001 in der stdlib ("??T")
  fn firstOf<T>(xs: T[]): ?T { ... }   // mit T = ?int: das Ergebnis kann "kein Element" nicht von "Element ist null" unterscheiden
  ```
- Extern (Vergleich): Rust `Option<Option<T>>` (nestbar, `Iterator<Item = Option<T>>` ist Alltag), Kotlin (`List<Int?>` ok, aber `T?` bei `T = Int?` kollabiert — DIESELBE Falle wie Lyric, gilt dort als bekannte Schwäche), Swift (`Optional<Optional<T>>` nestbar), Zig (`??T` nestbar), C# (`int??` nicht; `Nullable<Nullable<T>>` verboten — gleiche Schwäche), TypeScript (`T | null` kollabiert), Go (kein Optional).
- Beschreibung: Zwei Optionen. (A) `?` wird nestbar: `??T` ist ein Typ, `null` ist die äußerste Absenz, `Some(null)` braucht eine Schreibweise — Lyric hat keine (`?T` ist kein Enum), also müsste ein Konstruktor `some(x)` oder eine Regel "Zuweisung `?T` → `??T` hebt" her; Laufzeit: Tag-Bit statt Sentinel, ändert Layouts (Format-Major). (B) `Option<T>` als stdlib-Enum NEBEN `?T` für den generischen Fall (`Iterator<T>.next(): Option<T>`), `?T` bleibt für den nicht-generischen Alltag — Rust-Modell, kein Laufzeitumbau, aber zwei Absenzformen (Verletzung von "null is the one empty value"). Beides bricht: LYR-SEM0091/0080 verschwinden, `Iterator<T>`-Konformanzen und jede `next()`-Implementierung ändern sich (B) bzw. das Typsystem/Format (A). Spec §3.3, §6.3, §7.2, §8, §10, §11. Empfehlung: (B) erst prüfen, ob `Iterator<T>` der einzige Protokollpunkt ist; wenn ja, ist (B) ein 5.0-Kandidat mit Uhr; (A) nur, wenn ein Format-Major ohnehin ansteht.
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review (Quelle für List<?T>), prototyper

### [language-review] MAJOR-Vorschlag: Member-Sichtbarkeit durchsetzen (Verweis)
- Kategorie: proposal-major
- Priorität: HIGH
- Intern (Lyric): siehe Eintrag "[language-review] `pub` auf Membern wird nicht durchgesetzt" (TypeChecker.cs:1590-1593, Resolver.cs:134, Grammatik 02-grammar.md:263).
- Extern (Vergleich): Rust/Swift/Kotlin/C#/Go — überall gibt es Member-Privatheit; Lyric ist die einzige der acht Sprachen ohne.
- Beschreibung: Semantik: Member ohne `pub` sind modulprivat; Felder erhalten `[ 'pub' ]`; Initializer außerhalb des Moduls nur über pub-Felder. Deprecation-Uhr: 4.x warnt (neuer Code LYR-SEMxxxx, Warning), 5.0 Fehler. Stdlib: Felder mit Lesezugriff bekommen `pub` (z.B. `Packet.payload`, `IoError.kind`), interne (`List.data`, `count`) bleiben privat. Spec §4.2, Grammatik §3.2/§3.3; Embedding (§11) liest Felder weiter über Rows, nicht über `pub`.
- Prototyp: (prototyper, Batch 3 Punkt 1)
- Betroffene Teammitglieder informiert: stdlib-review, semantic, prototyper

### [language-review] MAJOR-Vorschlag: Shadowing-Regel für Lokale festlegen (beide Richtungen ändern die Bedeutung kompilierender Programme)
- Kategorie: proposal-major
- Priorität: HIGH
- Intern (Lyric): siehe Bug-Eintrag "Redeklaration einer Lokalen im selben Block wird still verworfen" (TypeChecker.cs:1040). Spec §7.1 nennt keine Regel.
- Extern (Vergleich): Rust (Shadowing erlaubt, Idiom), Kotlin/Swift/C#/Go/Zig/TS (Fehler im selben Scope; Go/Kotlin erlauben es in inneren Blöcken mit Warnung).
- Beschreibung: Variante A (Rust): zweite `let`/`var` im selben Block bindet neu, spätere Zugriffe sehen die neue Bindung; Lambdas, die vorher erzeugt wurden, behalten die alte. Variante B: LYR-RES0001 auf Blöcke ausdehnen. A ist QOL-besser (`let x = x ?? d;`, `let s = s.trim();`), B ist konservativer. Beide sind formal breaking, weil heute kompilierende Programme mit Doppel-`let` ihr Verhalten ändern (A) oder nicht mehr kompilieren (B) — praktisch sind solche Programme heute alle falsch (die zweite Bindung ist tot). Empfehlung: A, als 4.x-Fix mit CHANGELOG-Hinweis, da kein korrektes Programm betroffen ist. Spec §7.1.
- Prototyp: (prototyper, Batch 2 Punkt 1)
- Betroffene Teammitglieder informiert: prototyper, semantic

### [language-review] MAJOR-Vorschlag: f-String-Formatsprache in der Spec fixieren und vom .NET-Spezifizierer lösen (Verweis)
- Kategorie: proposal-major
- Priorität: MEDIUM
- Intern (Lyric): siehe Eintrag "Format-Spec-Sprache in f-Strings ist implementierungsdefiniert (.NET)" (stdlib/std/fmt.lyr:6-8, §6.6, §11) und stdlib-review "Ausrichtungs-Vorzeichen in `{x:10}` ist invertiert".
- Extern (Vergleich): Python PEP 3101 `[[fill]align][sign][width][.precision][type]`, Rust std::fmt (gleiche Familie), Go fmt-Verben, Zig `{[fill][align][width].[precision]}`.
- Beschreibung: Mini-Sprache nach Python/Rust-Vorbild: `{x:>8}`, `{x:08.2}`, `{x:x}`/`{x:b}`, `{x:+}`; `formatXxx(value, spec)` wird in §11 mit dieser Grammatik spezifiziert. Breaking für heutige `N2`/`D5`/`X`-Nutzer → Uhr: 4.x akzeptiert beide (Warnung bei .NET-Form), 5.0 nur die Spec-Form. Betrifft §1.7, §6.6, §11.
- Prototyp: -
- Betroffene Teammitglieder informiert: stdlib-review

### [language-review] Minor-Vorschlagsindex (Kurzfassung mit Spec-Kapitel; Details in den jeweiligen Einträgen)
- Kategorie: proposal-minor
- Priorität: HIGH
- Intern (Lyric): Sammel-Eintrag; jeder Punkt hat oben seinen eigenen Eintrag mit Datei:Zeile und Beispiel.
- Extern (Vergleich): siehe Einzeleinträge.
- Beschreibung (Priorität, Feature, Spec-Kapitel, Breaking nein):
  1. HIGH — `try`-Ausdruck (`try e catch (x: E) {…}`, `try? e`) + try/catch in Definite Assignment — §6.2, §7.7, §9.3
  2. HIGH — if-let / let-else / while-let — §5 Grammatik, §7.1, §7.4
  3. HIGH — Konformanz-Synthese (Equatable/Hashable/Ordered/Display ohne Body) — §5.1, §11
  4. HIGH — Block-Arm mit Tail-Expression (Wert aus `{ …; expr }`) — §6.9, §7.6
  5. HIGH — typed throws: `throws E` mit Typparameter, `fn(..) -> R throws E` — §9.2, §10, §8.3, Grammatik §4
  6. HIGH — f-String rendert `Display`-Typen über `.show()` — §6.6, §11
  7. MEDIUM — `throw`/`panic` als `never`-Ausdruck — §6.2, §9.4
  8. MEDIUM — Slices `xs[a..b]` — §3.3, §6.1
  9. MEDIUM — Labels `outer: for … break outer;` — §5, §7.2
  10. MEDIUM — Enum `variantName()` eingebaut — §3.4
  11. MEDIUM — Raw-/Mehrzeilen-Strings — §1.6/§1.7
  12. MEDIUM — benannte Argumente mit `:` — §4.3a, §7.1a, Grammatik §6.2
  13. MEDIUM — `static let` in Enums; statische Interface-Member (monomorphisiert) — §5, Grammatik §3.4/§3.5
  14. MEDIUM — Tupel-Destructuring in for-Kopf und Lambda-Parametern — §7.2, §7.3
  15. MEDIUM — `?T == ?T` mit `null == null` — §6.2
  16. MEDIUM — Array-Generator-Konstruktor / `arrayOf(n, f)` — §3.3 bzw. stdlib
  17. MEDIUM — Index-Operator in §6 und `Indexable` in §11 dokumentieren — §6, §11
  18. LOW — Struct-Update `{ …, ..base }`, Tupel-Index `t.1` — Grammatik §6.2
  19. LOW — `Neg`/`Rem`-Interfaces, Compound auf Feldziel — §6.1, §6.5
  20. LOW — std.string/std.core als Prelude bzw. Import-Hinweis in LYR-SEM0012 — §4.2/§4.4
  21. LOW — Narrowing von `let`-Struct-Feldpfaden — §7.4
- Prototyp: siehe prototypes/ (01-09 vom prototyper)
- Betroffene Teammitglieder informiert: prototyper

### [language-review] Korrektur zum Eintrag "`throw` ist kein Ausdruck": `panic` ist in der Sema bereits `never`
- Kategorie: shortcoming
- Priorität: LOW
- Intern (Lyric): src/Lyric.Frontend/Sema/LyrType.cs:122 (NeverType, nicht benennbar), src/Lyric.Frontend/Sema/Flow.cs:18 (panic-Aufrufe sind never-typisiert) — Hinweis vom prototyper; stdlib/std/core.lyr:23 deklariert `panic(...): void`, Spec ~/dev/projects/lyricspec/spec/09-errors.md:66 sagt "returns never". Prototyper-Bug: `if (b) 1 else panic("no")` / `x ?? panic("…")` passieren die Sema und stürzen das Lowering ab (FunctionLowerer.cs:1393).
- Extern (Vergleich): Rust `!`, Swift `Never`, Kotlin `Nothing`, TypeScript `never` — überall benennbar und in Unifikation gültig.
- Beschreibung: Der Vorschlag "throw als never-Ausdruck" steht damit auf vorhandener Sema-Infrastruktur; offen bleibt (a) das Lowering des never-Arms und (b) ob `never` als Typwort benennbar werden soll (für `fn fail(): never`-Helfer — heute muss so eine Funktion `void` deklarieren und verliert die Flow-Wirkung). Spec §9.4 und §11 (Signatur von panic) angleichen.
- Prototyp: prototypes/08 (prototyper)
- Betroffene Teammitglieder informiert: prototyper, spec-conformance

### [prototyper] Prototyp 10: Shadowing / Redeklaration einer Lokalen im selben Block
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): src/Lyric.Frontend/Sema/TypeChecker.cs:1040 (`scope.TryDeclare(local)` — Rückgabe ignoriert; dieselbe Form, die STATUS.md für Patterns mit SEM0097 gerade schloss), Resolver.cs:155 (RES0001 nur Modul/Typ); lyricspec §7.1, §7.4. Bestätigt: probe-silent.lyr druckt 1 statt 11 (nur SEM0071-Warnung). Ist-Beispiel — ein Name pro Verfeinerungsschritt, `var` verliert das Narrowing:
  ```lyr
  let filled = input ?? fallback; let trimmed = filled.trim(); let parsed = parseInt(trimmed);
  var x = input; x = x ?? fallback; let y = x!;     // x bleibt ?string (§7.4), Unwrap nötig
  ```
- Extern (Vergleich): Rust — `let input = input.unwrap_or(fallback); let input = input.trim();` (Shadowing als Idiom); Kotlin — `val n = 1; val n = 2` ist "conflicting declarations".
- Beschreibung: Soll A (empfohlen): Shadowing im selben Block erlauben — §7.1-Satz, Sema `Redeclare` statt `TryDeclare` (~20 Z.), Lowering 0 (eigenes Lokal je Binding), alte Capture in Lambdas bleibt; Idiom `let x = x ?? d;` wird möglich und entschärft die Identifier-Grenze des Narrowings. Soll B: RES0001 auf Blöcke ausdehnen (~5 Z.), sicher, verbietet das Idiom. Beide Patch-fähig; A ändert das Verhalten heute kompilierender (fehlerhaft gemeinter) Programme → als Fix führen. Zeilen: 3-Schritt-Verfeinerung Ist 3 Namen / Soll 1 / Rust 1. Empfehlung: A.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/10-shadowing/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 11: Labels für break/continue
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §7.2 (innerste Schleife), §7.5 (defer), Grammatik §5 BreakStmt/ContinueStmt; Parser.Statements.cs:26 (`ParseStmt`, `IDENTIFIER ':'` heute Parsefehler); FunctionLowerer.cs:127,987-999,1114 (`_loops`-Stack, `LoopScope.DeferDepth` — Defer-Entladung existiert für return). Ist-Beispiel — Flag + wiederholte Bedingung; bei "continue outer" bleibt ein Teilergebnis stehen (ist.lyr druckt sum 11, soll.lyr 8):
  ```lyr
  var found = false;
  while (i < grid.length && !found) { var j = 0; while (j < …) { if (grid[i][j] == wanted) { found = true; fi = i; fj = j; break; } j += 1; } i += 1; }
  for (row in rows) { var skip = false; for (v in row) { if (v < 0) { skip = true; break; } sum += v; } if (skip) { continue; } }
  ```
- Extern (Vergleich): Go — `outer: for … { for … { break outer } }`, `continue rows` (identische Syntax); Rust `'outer:`, Kotlin `outer@`.
- Beschreibung: Soll: `LabeledStmt = IDENTIFIER ':' (WhileStmt|DoWhileStmt|ForInStmt)`, `BreakStmt = 'break' [IDENTIFIER] ';'`, dito continue. Eindeutig (Statement mit `IDENT ':'` ist heute PAR-Fehler); Rust-Tick verworfen (kollidiert mit Char-Literal), Kotlin-`@` verworfen (AT_IDENT für Attribute). Labels im eigenen Namensraum, Geltung nur im Schleifenkörper, unbenutzt → Warnung. defer: Entladung bis `DeferDepth` des Ziels (Code wie bei return). Zeilen: Doppel-break Ist 14 (Flag) / 7+Funktion / Soll 8 / Go 8; continue outer Ist 8 mit Fehlerquelle / Soll 7. Aufwand: Parser ~25, Sema ~40, Lowering ~30, VM 0. Breaking nein (Minor). Empfehlung: Sprachfeature, klein.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/11-loop-labels/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 12: Benannte Argumente `f(name: value)`
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §7.1a, §4.3a, Grammatik §6.2 CallArgs; Parser.cs:429 (`ParseArguments`), TypeChecker.cs:1673 (`SelectOverload`). Ist-Beispiel — bewiesen: `f(x = 3)` ist heute eine Zuweisung als Ausdruck (ist.lyr druckt "port is now 8080"); Options-Struct funktioniert gut:
  ```lyr
  connect("a", 80, false, 3, 500);                       // vier Defaults abschreiben
  connect("a", 3, false, 80);                            // port/retries vertauscht — kompiliert
  connect2("b", ConnectOptions { timeoutMs = 500 });     // Lib-Idiom, +5 Z. Deklaration
  ```
- Extern (Vergleich): Kotlin `connect("a", timeoutMs = 500)` (dort geht `=`, weil Zuweisung kein Ausdruck ist); Swift `connect(host: "a", timeoutMs: 500)` (`:`, Labels Pflicht).
- Beschreibung: Soll: `CallArg = [ IDENTIFIER ':' ] Expr` — `:` weil `=` belegt ist; `f(x: 3)` ist heute PAR0002, also frei und eindeutig (kein Ternär, keine Typannotation im Ausdruck). Regeln: benannte nach positionalen, Name muss existieren, keine Doppelbelegung, `params` nicht benennbar, nur bei deklarierten Funktionen (Funktionswerte haben keine Namen, §7.1a). Überladung: Kandidat ohne den Namen scheidet vor dem Zählen aus; Parameternamen bleiben für die Redeklarationsprüfung bedeutungslos. Konsequenz: Parameternamen werden API (§11) — stdlib-review sollte öffentliche Namen sichten. Zeilen: nur timeoutMs Ist 4 Defaults / Struct 1+5 / Soll 1. Aufwand: Parser ~20, Sema ~120, Lowering 0, VM 0. Breaking nein (Minor, aber Namen werden Vertrag). Empfehlung: Sprachfeature, Priorität hinter 04/05/06/10, da Options-Struct akzeptabel.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/12-named-arguments/
- Betroffene Teammitglieder informiert: language-review, stdlib-review (Parameternamen als Vertrag)

### [prototyper] Prototyp 13: `static let` im Enum; statische Interface-Anforderungen
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): Parser.Declarations.cs:644 (PAR0040, bestätigt), :652 (Interface-`static` semantisch abgelehnt, parst aber), :478 (StaticBinding-Pfad für Structs); lyricspec §3.4/§3.5, §5.1, §8.1/§8.4. Ist-Beispiel — Konstanten auf Modulebene mit Präfix; Monoid über Witness-Argument:
  ```lyr
  let LEVEL_LOW_MAX = 10;                                  // gehört zu Level, steht außerhalb
  struct IntMonoid :: [Monoid<int>] { fn zero(): int { return 0; } }
  fn sum<T :: [Add<T, T>], M :: [Monoid<T>]>(xs: T[], m: M): T { var acc = m.zero(); … }
  sum([1, 2, 3], IntMonoid { })
  ```
- Extern (Vergleich): Rust — `impl Level { const DEFAULT: Level = Level::Mid; }`, `trait Zero { fn zero() -> Self; }`, `T::zero()` in generischem Code, nicht über `dyn Zero`; Swift `static func` als Protocol-Requirement.
- Beschreibung: Soll (a) `EnumMember = FunctionDecl | StaticBinding` nach dem `;` — Parser ~10 Z., Sema/Lowering 0. (b) `static fn` im Interface als statische Anforderung: konformer Typ muss es deklarieren; `T.zero()` bei `T :: [Zero<T>]` = direkter Aufruf nach Monomorphisierung; über Interface-WERT nicht erreichbar (kein Receiver, vtable unverändert, Format 4.0 unberührt); kein `Self` nötig (`Zero<T>`-Muster wie Equatable). `static let` im Interface erst Runde 2 (Deklaration ohne Initializer). Zeilen: Monoid-Summe Ist Witness-Struct + Extra-Typparameter + Extra-Argument / Soll `T.zero()`. Aufwand (b): Parser 3, Sema ~120, Lowering ~30, VM 0. Breaking nein. Empfehlung: (a) sofort, (b) Minor — größter Nutznießer stdlib (Fabriken, Zero/One).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/13-enum-static-interface-static/
- Betroffene Teammitglieder informiert: language-review, stdlib-review

### [prototyper] Prototyp 14: Tupel-Destructuring im for-Kopf und in Lambda-Parametern
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): Grammatik §5 ForInStmt/§6.2 LambdaParam; Parser.Statements.cs:170 (`for (` + IDENTIFIER), Parser.cs:388 (`IsLambdaAhead`), :643 (`ParseLambda`); TypeChecker.cs:1094/:920 (Schleifenvariable/Parameter binden). Ist-Beispiel — Hilfsname + Destructuring-Zeile in jeder Paar-Schleife:
  ```lyr
  for (kv in entries(m)) { let (k, v) = kv; … }
  fold(over(pairs), 0, (acc: int, p: (int, int)) => { let (a, b) = p; return acc + a * b; })
  ```
- Extern (Vergleich): Python `for k, v in m.items()` (Lambda kann NICHT entpacken); Rust `for (k, v) in &m`, `|acc, &(a, b)| …`.
- Beschreibung: Soll: `ForInStmt … ( IDENTIFIER | TuplePattern ) 'in' …`, `LambdaParam = ( IDENTIFIER | TuplePattern ) [ ':' TypeExpr ]`; Muster = irrefutables TuplePattern aus DestructuringStmt (gleiche Prüfung, gleiche Lowering als erste Anweisung). Eindeutig: `for ((` ist heute PAR-Fehler; Lambda `((k, v)) =>` mit Doppelklammer, weil `(k, v) =>` ZWEI Parameter bleiben muss. Typen: Elementtyp bzw. Kontext (§7.3). Zeilen: Map-Schleife Ist Hilfsname + 1 Z. / Soll 0. Aufwand: Parser ~30, Sema ~40, Lowering ~20, VM 0. Breaking nein (Minor). Empfehlung: Sprachfeature, klein.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/14-tuple-destructuring-for-lambda/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 15: f-Strings rendern Display-Typen
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): lyricspec §6.6 ("no implicit Display call in interpolation"), Guide 2 ("f"{at}" is refused"); TypeChecker.cs:1399-1408 (InterpolatedStringExpr → SEM0006); stdlib/std/io/console.lyr (`println<T :: [Display]>` — das Vorbild). Ist-Beispiel:
  ```lyr
  return f"point {p.show()} status {s.show()} after {tries} tries";   // f"{p}" wäre LYR-SEM0006, println(p) geht
  ```
- Extern (Vergleich): Rust `format!("point {p} status {s}")` → Display::fmt; Kotlin `"$p"` → toString(); Python `f"{p}"` → __str__.
- Beschreibung: Soll: keine Grammatikänderung; §6.6-Regel: Loch ohne Spezifizierer → Skalar wie heute, sonst `value.show()` wenn Display erfüllt (Conformance wie println), sonst SEM0006 mit Hinweis; Spec auf Display-Typ = Fehler; Opaque bleibt refused; `?T` bleibt Fehler (narrow oder `??`). Sema ~25 Z. (TypeChecker.cs:1399), Lowering 0. Multipliziert sich mit 03/07 (synthetisiertes show). Voraussetzung: Display-Conformance-Prüfung muss Arrays/Optionals/Tupel ablehnen (stdlib-review-Bug "println(array) scheitert im IR"), sonst erbt das Loch den Bug. Breaking nein (Minor). Empfehlung: Regel ändern — höchster Nutzen pro Aufwand der Liste.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/15-fstring-display/
- Betroffene Teammitglieder informiert: language-review, semantic (Inbox: Display-Constraint zu lax)

### [prototyper] Prototyp 16: Optional-Gleichheit `?T == ?T` (a); Feldpfad-Narrowing (b, Skizze)
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): TypeChecker.cs:1982-2005 (drei SEM0059-Stellen), lyricspec §6.2, §7.4; Lowering hat OptIsSome/OptGet (STATUS.md M36). Ist-Beispiel — Helfer PRO Typ, generisch unmöglich:
  ```lyr
  fn sameInt(a: ?int, b: ?int): bool { if (a == null) { return b == null; } if (b == null) { return false; } return a == b; }
  fn sameString(a: ?string, b: ?string): bool { … dieselben 3 Zeilen … }
  let port = c.port; let p = if (port != null) port + 1 else 0;      // Feld narrowt nicht
  ```
- Extern (Vergleich): Swift `Optional<Wrapped>: Equatable` (nil == nil true; `<` auf Optionals seit Swift 3 entfernt); C# lifted equality `int? == int?`.
- Beschreibung: Soll (a): §6.2-Tabelle null==null → true, null==v → false, v==w → equals; `?T == T` erlaubt; Ordnung bleibt Fehler; `p == q` narrowt nichts (nur `== null`). Sema ~40 Z., Lowering ~40 Z. (Desugar), VM 0, keine Grammatik. Macht `fn same<T :: [Equatable<T>]>(a: ?T, b: ?T)` schreibbar und die Synthese aus Prototyp 03 über ?-Felder trivial. Skizze (b): Pfad `x.f` narrowt, wenn x `let` eines STRUCT-Typs (Wertsemantik, kein Alias) — Klassen bleiben bei Kopie/let-else (Prototyp 02); Sema ~120 Z. Fehlerklasse: die naive Umgehung `a != null && b != null && a == b` beantwortet zwei nulls mit false. Breaking nein (Minor). Empfehlung: (a) empfohlen, (b) Folgeschritt.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/16-optional-equality/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 17: Member-Sichtbarkeit (`pub` auf Feldern/Methoden durchgesetzt) — Zwei-Datei-Beispiel
- Kategorie: prototype
- Priorität: HIGH
- Intern (Lyric): TypeChecker.cs:1590-1593 (Visibility nur für modul-qualifizierte Namen), Resolver.cs:134 (Visibility gespeichert), Parser.Declarations.cs:464-478 (Member-Formen; `pub` vor Feld heute Fehler), Grammatik §3.2 Field, lyricspec §4.2. Gemessen (ist/app.lyr, kompiliert und läuft):
  ```lyr
  a.balance = -999;                                               // nicht-pub Feld fremdes Modul
  a.log();                                                        // nicht-pub Methode
  let forged = Account { owner = "eve", balance = 1000000, audit = 0 };   // Initializer umgeht open()
  // Ausgabe: after tampering: -999 audit 2 / forged 1000000
  ```
- Extern (Vergleich): Rust — `mod bank { pub struct Account { pub owner: String, balance: i64 } }`; außerhalb E0616 (private field), E0624 (private method), E0451 (Literal mit privatem Feld).
- Beschreibung: Soll: `Field = [ 'pub' ] IDENTIFIER ':' TypeExpr [ '=' Expr ]`; Member ohne pub = MODULPRIVAT (Rust-Modell, damit `open()` im selben Modul funktioniert); Initializer außerhalb nur mit pub-Feldern; Feld-Patterns nur über pub-Felder; Enum-Varianten immer pub; Interface-erfüllende Member über das Interface erreichbar (Rust-Regel); Embedding liest Rows (unberührt); Uhr wie SEM0093 (4.x Warnung, 5.0 Fehler). Aufwand: Parser ~10, Sema ~150, Lowering/VM 0, stdlib GROSS (~80 Felder klassifizieren; `Exception.text` muss pub werden, sonst bricht Guide 10). BREAKING ja → Major. Gewinn: Invarianten schützbar, und erstmals eine Grenze, hinter der die stdlib refactorn darf (heute ist jedes Feld API). Empfehlung: Major 5.0 mit 4.x-Warnung; stdlib-review bereitet die Feldklassifikation vor.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/17-member-visibility/
- Betroffene Teammitglieder informiert: language-review, stdlib-review

### [prototyper] Prototyp 18: Array-Konstruktion ohne erstes Element (`arrayOf(n, f)` / `[n] of …`)
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §3.3 ("built with `[x] * n`"), §7.1 (`[]` braucht Annotation); VM-Allokation für `[x] * n` existiert. Ist-Beispiel — Sonderfall, f(xs[0]) extra, Dummy-Struct:
  ```lyr
  if (xs.length == 0) { return []; }  var out = [f(xs[0])] * xs.length;  for (i in 1..xs.length) { out[i] = f(xs[i]); }
  var slots = [Slot { id = -1, used = false }] * 3;         // Dummy, den es fachlich nicht gibt
  let cubes = collectArray(range(0, 4).map((i: int) => i * i * i));   // heutige Lib-Variante: 2 Imports, 3 Allokationen
  ```
- Extern (Vergleich): Kotlin `Array(n) { i -> f(i) }`, `IntArray(n)`; Rust `(0..n).map(f).collect()`; Go `make([]T, n)`.
- Beschreibung: Form A (EMPFOHLEN, stdlib + ein Native): `arrayOf<T>(n, f: fn(int) -> T): T[]`, `arrayFilled<T>(n, x)` — in Lyric nicht schreibbar (braucht das leere Array fester Länge), daher privates Native `rawArrayAlloc` (~15 Z. VM) + ~20 Z. stdlib, kein Uninitialisiert-Zustand beobachtbar. Form B (Sprache): `ArrayLit |= '[' Expr ']' 'of' Lambda`, `of` kontextuell nach `]` (heute Parsefehler) — spart nur den Import, nicht empfohlen. Fehlerklassen: Off-by-one "Schleife ab 1", f einmal zu viel (Seiteneffekte), überlebende Dummy-Werte. Breaking nein. Empfehlung: stdlib reicht.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/18-array-construction/
- Betroffene Teammitglieder informiert: language-review, stdlib-review (arrayOf/arrayFilled + Native)

### [prototyper] Prototyp 19: Raw-Strings `r"…"`/`r#"…"#` und Mehrzeilen-Strings `"""…"""`
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §1.5/§1.6 ("no raw or multiline string form"), Lexer.cs (Erkennung von `f"` als Vorbild), tooling/textmate (gegen den Lexer gepinnt), lyrfmt. Ist-Beispiel — 11 Escapes für ein JSON-Objekt, 6 Literale + 5 `+` + 5 `\n` für einen usage-Text:
  ```lyr
  let doc = parse("{\"name\": \"aria\", \"level\": 3, \"tags\": [\"a\", \"b\"]}");
  let usage = "usage: tool [options] <file>\n" + "  -v        verbose\n" + …;
  let pattern = "C:\\Users\\ada\\*.txt";
  ```
- Extern (Vergleich): Rust `r#"{"name": "aria"}"#`, `\`-Zeilenfortsetzung; Swift `"""` mit Einzugregel der schließenden `"""`; Kotlin `"""` + `trimIndent()` (Laufzeit).
- Beschreibung: Soll (rein lexikalisch): `RawStringLit = 'r' {'#'} '"' {any} '"' {'#'}`, `MultiLineStr = '"""' [Newline] {…} '"""'`, `f"""` kombinierbar. Eindeutig: `r"` ist heute IDENT + String (Parsefehler), `"""` lext als `""` + offener String, `#` ist kein Token. Swift-Einzugregel zur LEXZEIT (String bleibt Konstante); weniger Einzug → Fehler; Formatter übernimmt Lexeme unverändert (Einzug ist Inhalt — Test nötig). Aufwand: Lexer ~80 Z., Parser/Sema/Lowering/VM 0, Formatter ~10, TextMate. Breaking nein (Minor). Empfehlung: Sprachfeature, hoher Alltagsnutzen, null Risiko für Parser/Sema.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/19-raw-multiline-strings/
- Betroffene Teammitglieder informiert: language-review, lexer-parser (Inbox)

### [stdlib-review] Feld-Klassifikation der stdlib für Member-Sichtbarkeit (Zuarbeit zu Prototyp 17) und `arrayOf/arrayFilled` (Prototyp 18)
- Kategorie: proposal-major
- Priorität: MEDIUM
- Intern (Lyric): alle 104 Felder in stdlib/std/**/*.lyr klassifiziert in review-examples/tasks/field-visibility.md (Worktree stdlib-review): **19 Vertragsfelder** (Exception.text, Deprecated.message/until, Utf8Error.offset, EncodingError.offset/expected, JsonError.line/column/offset/expected, IoError.kind/path/detail, Packet.bytes/host/port, TimeError.text), **85 private**. Vier Stellen lesen heute fremde private Felder: collections.lyr:975-1030 (Map*Iterator liest `Map.states/keys/values`), collections.lyr:895-905 (SetIterator liest `Set.items/states`) — bei Durchsetzung müssen diese Iteratoren Methoden ihres Containers werden (deckt sich mit Minor 2) oder eine Modul-Sichtbarkeit existieren. Compiler-gebundene Initializer (RangeIterator, ArrayIterator, StringIterator, iter.lyr:80-193) brauchen eine Ausnahme.
  ```lyr
  // Prototyp 18: heutige Umgehung für ein Array ohne erstes Element (collections.lyr:56, 493; jede (?T)[]-Signatur):
  let hole: ?T = null; let slots = [hole] * n;            // und `[null] * 3` bekommt keinen Kontexttyp (SEM0001)
  // Soll (Minor, additiv): pub fn arrayOf<T>(n: int, f: fn(int) -> T): T[];  pub fn arrayFilled<T>(n: int, x: T): T[];
  ```
- Extern (Vergleich): Rust `pub struct IoError { pub kind, … }` (Records mit pub-Feldern), `Vec::from_fn`/`(0..n).map(f).collect()`; Kotlin `Array(n) { f(it) }`; C# `Enumerable.Range(0,n).Select(f).ToArray()`; JS `Array.from({length: n}, f)`.
- Beschreibung: Zuarbeit; Spec §11 Punkt 2 müsste die Felder von `Exception` und `Deprecated` als Vertrag benennen. `arrayOf` braucht ein privates Native (kein `default(T)`), passt in den Contract wie `fromChars`.
- Prototyp: prototypes/17-member-visibility/README.md, prototypes/18-… (prototyper); review-examples/tasks/field-visibility.md
- Betroffene Teammitglieder informiert: prototyper

### [prototyper] Prototyp 20: Struct-Update-Syntax `..base` und Tupel-Index `t.1`
- Kategorie: prototype
- Priorität: LOW
- Intern (Lyric): Grammatik §6.2 StructInit (Parser.cs:549-590 `ParseStructInit`), §1.5 FloatLit (`.1` ist kein Float → `t.1` lext heute als `t` `.` `1`, PAR0003), lyricspec §3.3 (Tupel nur per Destructuring). Ist-Beispiel — 6 Felder abschreiben; `var copy = base; copy.port = …` ist für Structs korrekt, für Klassen ein stiller Alias:
  ```lyr
  return Config { host = base.host, port = port, secure = base.secure, retries = base.retries, timeoutMs = base.timeoutMs, label = base.label };
  let (_, second, _) = triple;
  ```
- Extern (Vergleich): Rust `Config { port, ..base }`, `t.1`; Kotlin `base.copy(port = 8080)`; C# `base with { Port = 8080 }`.
- Beschreibung: Soll (a) `StructInit … [ '..' Expr ] '}'` als letzter Eintrag — keine Range, `..` ohne linken Operanden ist heute PAR0002 → frei; Struct = Kopie, Class = neue Instanz (flach), Basis vor Default, nicht für Enum-Varianten (Variante nicht statisch prüfbar), mit Prototyp 17 nur pub-Felder außerhalb. (b) `TupleIndex = Postfix '.' IntLit`, EINE Ebene (vermeidet Rusts `t.0.1`-Falle), kein Schreibzugriff. Aufwand: (a) Parser ~15, Sema ~40, Lowering ~30; (b) Parser ~10, Sema ~20, Lowering ~5. Breaking nein (Minor). Fehlerklasse: Alias-Bug der `var copy`-Form bei Klassen. Empfehlung: beides Minor, geringe Priorität.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/20-struct-update-tuple-index/
- Betroffene Teammitglieder informiert: language-review

### [prototyper] Prototyp 21: Nestbare Optionals (`??T`) vs. `Option<T>` als stdlib-Enum (MAJOR)
- Kategorie: prototype
- Priorität: MEDIUM
- Intern (Lyric): lyricspec §3.3 (`??T` ist kein Typ), §7.2 (SEM0091), §10 (`next(): ?T`); Lowering lehnt `firstOf<T>` mit T = ?int als IR0001 "nested optional" ab (Sema schweigt). Ist-Beispiel — Wrapper-Struct als durchgängige Umgehung:
  ```lyr
  struct Slot { value: ?int, }          // versteckt die Absenz eine Ebene tiefer
  let a = firstOf(wrapped);             // ?Slot statt ??int
  let cells = List<Slot>.empty();       // List<?int> geht nicht
  ```
- Extern (Vergleich): Rust `Option<Option<T>>` (nestbar, Iterator über Option-Items Alltag); Kotlin `T?` bei `T = Int?` kollabiert — dieselbe Falle.
- Beschreibung: Lib-Variante B (`Option<T>`-Enum) LÄUFT und trennt None von Some(null), Iterator-Protokoll `next(): Option<T>` funktioniert für T = ?int, `List<Option<?int>>` instanziierbar — aber nur nach vier Umgehungen, weil Enums mit optionalem Payload heute vier Compiler-Defekte treffen (eigener Bug-Eintrag). Preis B: zwei Absenzformen, Iterator-Bruch (5.0), Brücke kollabiert; Gewinn: Format bleibt. Variante A (`??T`): löst alles ohne zweite Absenzform, braucht aber einen zweiten Tag im Slot → Format-Major. Empfehlung: erst die vier Defekte beheben (nötig, sobald irgendein Enum — auch Result<?U, E> — ein ?T-Payload trägt); dann B nur, wenn Iterator der einzige Protokollpunkt ist; A nur mit ohnehin anstehendem Format-Major.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/21-nestable-optional/
- Betroffene Teammitglieder informiert: language-review, stdlib-review

### [prototyper] Enum mit OPTIONALEM Payload: vier Defekte (Exhaustiveness, Methoden nicht gelowert, Payload-Kontext, IR-Verifier-Absturz)
- Kategorie: bug
- Priorität: HIGH
- Intern (Lyric): (1) src/Lyric.Frontend/Sema/TypeChecker.cs:4288 (`IsIrrefutable`: BindingPattern über Optional → false — die ?E-Regel von §7.6 greift im SUB-Pattern) + `VariantCovered`; (3) src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3914 (`TryResolveFunction` findet die Instanz einer Enum-Methode mit optionalem Typargument nicht; Klasse geht); (4) FunctionLowerer (Widerung int→?int für Variant-Payload nach Substitution fehlt) + IrVerifier.cs:291. Repros: prototypes/21-nestable-optional/probe-optional-payload.lyr, probe-generic-method-optarg.lyr, lib-variante.lyr (Kommentare):
  ```lyr
  enum O { Some(?int), None, }
  match (o) { Some(v) => v ?? -1, None => -2 }          // (1) SEM0050 "missing Some"; v ist int, nicht ?int
  match (o) { Some(null) => 5, Some(v) => v, None => 0 } // (1) SEM0050 trotzdem; (2) Some(null) ist IR0001 "nested LiteralPattern"
  Option<?int>.Some(3).isSome()                          // (3) IR0001 "call to 'isSome' (external or bodiless)" — Box<?int>.get() geht
  Option<?int>.Some(null)                                // (4) IR0001 "'null' in a position without an expected type"
  Option<?int>.Some(7)                                   // (4) Unhandled InternalCompilationException: ir-verifier "newvariant field 0 is i64, expected ?i64"
  ```
- Extern (Vergleich): Rust `Option<Option<i64>>` — `Some(None)`, `Some(Some(v))`, `None` sind drei gewöhnliche Muster; Methoden und Literale brauchen nichts Besonderes.
- Beschreibung: Es gibt heute KEINE Schreibweise, die `Some(null)` von `Some(v)` im match trennt (1+2), Methoden auf `Option<?int>` sind unbenutzbar (3), und ein Literal-Payload stürzt den Compiler ab (4) — Spec Kap. 2/12 verlangt eine Diagnose. Trifft die stdlib erst mit einem Option/Result-Typ (stdlib-review: kein heutiges stdlib-Enum hat ?T-Payload), dann sofort (Result.Ok(?U)). Vorschlag: (1) im Sub-Pattern ist ein BindingPattern über ?T irrefutabel und bindet ?T (die ?E-Regel nur auf Scrutinee-Ebene); (3) Instanz-Auflösung für Enum-Methoden mit Optional-Typargument (Instanzschlüssel?); (4) Widerung am Variant-Konstruktor nach Substitution — dieselbe Familie wie language-reviews "List<Shape>.push(Sq {…})"-Absturz.
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/21-nestable-optional/
- Betroffene Teammitglieder informiert: language-review, stdlib-review, ir-codegen (Inbox), semantic (Inbox)

### [prototyper] Prototyp 22: `Neg<R>` / `Rem<T, R>`; Compound-Zuweisung auf Feldern
- Kategorie: prototype
- Priorität: LOW
- Intern (Lyric): lyricspec §6.1 (`%` numeric-only, kein Neg), §6.5 (Feldziel = SEM0003), stdlib/std/core.lyr:352-373 (Add/Sub/Mul/Div als Muster), FunctionLowerer `_chainReceivers` (Temp-Receiver-Mechanismus). Ist-Beispiel:
  ```lyr
  fn neg(): V { return V { x = -this.x, y = -this.y }; }      // statt -v
  fn rem(o: V): V { … }                                         // statt v % w
  b.pos = b.pos + b.vel;                                        // `b.pos += b.vel` ist SEM0003
  ```
- Extern (Vergleich): Rust `impl Neg for V`, `impl Rem for V`, `impl AddAssign` — `b.pos += b.vel` auf Feld erlaubt, Platz einmal ausgewertet.
- Beschreibung: Soll (a) zwei Interfaces in std.core (`Neg<R> { fn neg(): R }`, `Rem<T, R> { fn rem(other: T): R }`) + Builtin-Konformanzen; §6.1 zwei Sätze (`-a` = `a.neg()`, `%` folgt der Add-Regel); keine Grammatikänderung. (b) §6.5: Compound auf Feld/Element erlauben mit Spec-Zusage "genau einmal ausgewertet" (Temps im Lowering, ~60 Z.). Halb stdlib (Interfaces), halb Sema (Operator-Bindung ~30 Z.). Breaking nein (Minor). Fehlerklasse (b): Copy-Paste-Fehler zwischen den zwei Pfad-Kopien. Empfehlung: beides Minor, klein, Priorität LOW (bewusst entschieden in v1.5.0).
- Prototyp: /tmp/claude-1000/-home-Olivier-dev-projects-lyric/856170b2-04e0-40b3-a2f5-7a287182a921/scratchpad/team2/prototypes/22-neg-rem-compound-field/
- Betroffene Teammitglieder informiert: language-review, stdlib-review (Neg/Rem in std.core)
