# Pattern Matching in Lyric — Analyse, Architektur, Prototypen, Vergleich

Stand: 2026-09-22, Basis v4.4.1 (dc32100c), Branch `worktree-agent-a1c2eb789de86ba9d`.
Autor: pattern-lambda (Evolution-Team 3). Vorarbeiten: Usability-Review (Prototypen 02/05/14),
Bug-Hunt (Struct-Aliasing-Familie, Tupel-/Varianten-Lowering, Literal-Adaption).

## 1. Ist-Stand vor dem Umbau — zwölf verifizierte Defekte

| # | Programm | Verhalten 4.4.1 | Ursache |
|---|---|---|---|
| 1 | `match ((s, ev)) { (Idle, Dial(h)) => … }` | ICE `'Idle' … was not bound` (FunctionLowerer.cs:943) | `BindTupleElements` kennt nur Namen |
| 2 | `Neg(Neg(x))` | IR0001 „nested VariantPattern“ (:2588) | `BindOne` kennt nur Namen |
| 3 | `(0, 0) => …` | IR0001 (:966, Text vom let-Destructuring) | wie 1 |
| 4 | `Dial(_) \| Hangup` | IR0001 „or-pattern that binds“ (:2574) | `PatternBinds` schaut nicht in Sub-Patterns |
| 5 | `A(x) \| B(x)` | IR0001 (:2489) | Bindung läuft einmal pro Arm, Alternativen sind eigene Branches |
| 6 | `Rect { w = 0, h }` | ICE (:2594) | Literal als Feldmuster nicht gelowert |
| 7 | `match (b: int8) { 1 => …}` | Verifier „i8 vs i64“ | Literal nicht adaptiert (TypeChecker.cs:4440) |
| 8 | `match (p) { q => { p.x = 99; q.x } }` | druckt 99 | Binding aliast Struct-Scrutinee (:2412) |
| 9 | `Some(v)` über `Opt<?int>` | SEM0050 „missing Some“ | Binding über `?T` gilt als refutabel (:4288) |
| 10 | `x if (x > 0) =>` | SEM0004/SEM0045 (Lambda) | `IsLambdaAhead` sieht `(…) =>` |
| 11 | `match (o) { n => n, null => -1 }` | Verifier „unreachable from entry“ | Binding-Arm testet Präsenz nicht |
| 12 | `(a, b)` über `(?int, int)` | SEM0050 / falscher Typ | wie 9, plus Lowering ohne Unwrap |

Dazu (nicht in der Liste, beim Testen gefunden): `Result<?int, E>.Ok(4)` — Verifier
„newvariant field 0 is i64, expected ?i64“, weil Konstruktor-Argumente nicht gegen den Feldtyp
gelowert wurden (P0-3 des Reviews).

Alle zwölf sind mit Commit `fc1aecb7` behoben (Repros: `scratchpad/team3/pl-probes/p1…p12`,
Tests: `Lyric.Tests.Vm/NestedPatternTests`, `Lyric.Tests.Sema/PatternCompilerSemaTests`).

## 2. Architektur-Entscheidung: rekursiver Test-und-Bind-Compiler (Backtracking-Automat)

**Vorher.** Zwei getrennte Pässe pro Arm: `EmitPatternBranch` testete ausschließlich die
oberste Ebene (Tag, Literal, Range, null), `BindPattern`/`BindOne` banden danach Namen — und
nur Namen. Jede Form unterhalb der ersten Ebene fiel durch die Lücke zwischen den beiden.

**Nachher.** Eine Routine `LowerPattern(pattern, value, type, onFail, assumeMatch)`
(FunctionLowerer.cs, Abschnitt „pattern compiler“):

- Pro Knoten werden Test *und* Bindung zusammen emittiert; die Rekursion steigt mit demselben
  Code in Tupel-Elemente, Varianten-Payloads und Struct-Felder ab (`LowerFieldPattern`).
- Ein fehlgeschlagener Test springt zum Fail-Ziel des Arms (`Func<BlockId>`, lazily erzeugt —
  ein Block, den niemand erreicht, wird nie angelegt; der Verifier verlangt das).
- Optionals: `UnwrapPresent` macht aus `?T` ein `T` mit Präsenztest; Varianten: `EmitVariantTest`
  (Tag-Vergleich, dann `enumas`). Der Tag des Scrutinee wird einmal im Eingangsblock gelesen
  (`_scrutineeTag`), verschachtelte Payloads lesen ihren eigenen.
- Or-Patterns: jede Alternative bekommt einen eigenen Fail-Block, alle münden in einem
  Join-Block; die Sema (`RebindToCanonical`) hat die Bindungen aller Alternativen auf die
  Symbole der ersten gezeigt, also schreiben sie in *einen* Slot.
- `assumeMatch`: der letzte Arm eines exhaustiven `match` (und ein irrefutables `let`) wird
  ohne Tests kompiliert — nur Unwrap/Narrow/Bind bleiben. In einem Or-Pattern wird das Flag
  bis auf die letzte Alternative zurückgesetzt. Das reproduziert die alte IR für die
  Standardfälle (Golden `enums.ir` verliert nur einen leeren Block).
- `BindLocal` kopiert Struct-Werte (Bug 8), legt Zellen für gecapturete `var`-Bindungen an und
  deklariert den Slot beim ersten Sehen (Or-Alternativen teilen ihn).

**Warum Backtracking statt Entscheidungsbaum (Maranget/rustc).** §7.6 verspricht die
*geschriebene Reihenfolge* der Arme und Guards. Ein Entscheidungsbaum testet jede Spalte nur
einmal, dupliziert dafür Arm-Bodies und ordnet Guards um — in Lyric müssten Guards mit
Seiteneffekten dann trotzdem in Quellreihenfolge laufen, was den Baum wieder zu einer Kette
macht. Der Backtracking-Automat ist linear in der Musterzahl, dupliziert nichts, hält jede
Diagnose am Pattern-Knoten und kostet im Regelfall genau einen Tag-Vergleich pro Arm (der Tag
wird einmal gelesen). OCaml und Rust optimieren die Spaltenreihenfolge, weil ihre Arme keine
Reihenfolgegarantie über Guards hinweg brauchen; Scala („Matching Objects with Patterns“, Emir
et al.) und Swift-SILGen bei Guards machen dasselbe wie Lyric jetzt.

**Was die Sema dazu beiträgt** (TypeChecker.cs):

1. `BindPattern(…, nested)`: ein *Name* in verschachtelter Position über `?T` bindet `?T`
   (irrefutabel); nur die oberste Ebene eines `match`/if-let narrowt `?T → T`
   (`BindsWholeOptional`, `IsIrrefutable(…, nested)`). Begründung: oben ist der `null`-Arm
   ohnehin Pflicht, der Name danach heißt „der präsente Rest“; in einem Payload gibt es keinen
   solchen Arm, und ein Name, der `null` still verweigert, ließe ein Loch, das kein Arm
   benennen kann. Rust bindet in `Some(v)` ebenfalls das Innere, was immer es ist.
2. Literal-/Range-Pattern werden mit `AdaptLiteralType` an den Scrutinee angepasst (§3.1).
3. Or-Alternativen ≥ 1 zeigen auf die Symbole der ersten (`RebindToCanonical`), damit die
   Warnung „never used“ pro Alternative verschwindet und das Lowering einen Slot hat.
4. Parser: `_guardHead` — das erste `(` eines Guards öffnet eine Gruppe, kein Lambda.

**Spec-Folgen (§7.6, Minor, nicht-breaking):**
- „A field pattern only binds“ entfällt: `P { x = 0, y }` ist ein Test, der Arm ist refutabel
  (SEM0050 verlangt einen weiteren Arm).
- Neuer Satz: „A name nested inside a payload over a `?T` binds the `?T` itself and covers it;
  only the top of a match narrows.“
- „A bound struct is a copy“ gilt jetzt auch für die ganze Bindung, nicht nur für Felder.

## 3. Neue Pattern-Formen

### 3.1 if-let / while-let / let-else — implementiert (Commit 2)

Grammatik (§5):
```
Condition      = Expr | 'let' Pattern '=' Expr .
IfStmt         = 'if' '(' Condition ')' Block [ 'else' ( Block | IfStmt ) ] .
WhileStmt      = 'while' '(' Condition ')' Block .
LetPatternStmt = ( 'let' | 'var' ) Pattern [ ':' TypeExpr ] '=' Expr [ 'else' Block ] ';' .
```
`LetPatternStmt` verallgemeinert `DestructuringStmt`: `let Point { x, y } = p;` (irrefutabel,
ohne else), `let Circle(r) = s else { return 0; };`, `let x = opt else { continue; };`.

Semantik:
- Die Bindung ist der Beweis (§7.4): ein Name über `?T` bindet `T`; Enum-Payloads wie im Arm.
  Der Scrutinee selbst wird nicht narrowed (Regel „match narrowt nicht“ bleibt).
- if-let: Namen nur im then-Block; while-let: Pattern vor jeder Iteration, Namen im Body —
  die Schleifenform des Iterator-Protokolls `next(): ?T` (Abstimmung stdlib-redesign).
- let-else: Namen im umgebenden Block; der else-Block wird *vor* der Bindung geprüft (er sieht
  die Namen nicht) und muss verlassen — `Flow.AlwaysExits` (return/throw/break/continue/panic),
  sonst **LYR-SEM0098**. Ein refutables Pattern ohne else ist ebenfalls SEM0098. Ein Pattern,
  das nie fehlschlägt, in if-let/while-let/let-else ist Warnung **LYR-SEM0099**.
- `let` in einer anderen Ausdrucksposition ist ein Parsefehler (nur `ParseCondition` erzeugt
  `LetCondExpr`); `&&`-Ketten (`if (let a = x && let b = y)`) sind bewusst nicht in Runde 1.
- Definite Assignment (§7.7): nach let-else sind alle Namen assigned (FlowAnalyzer).
- Lowering: kein bool-Temp — `LowerPattern` mit Fail-Ziel = else-Block / Loop-Exit /
  else-Block des let. Der Loop-Exit von while-let entsteht on demand (irrefutables Pattern ohne
  `break` = Endlosschleife ohne unerreichbaren Block).
- Eindeutigkeit: `let` beginnt keinen Ausdruck; `let x = if (c) a else b else {…}` — das erste
  `else` gehört dem if-Ausdruck (Parser verlangt es), das zweite ist let-else. Formatter,
  LSP-Scope-Completion, AstDumper, alle Walker haben die neuen Knoten.

Vergleich: Rust (`if let`/`while let`/`let … else` — identisch; else muss divergieren),
Swift (`if let`/`guard let` nur Optionals, `if case` für Enums — zwei Syntaxen, Lyric eine),
Kotlin (`?: return` nur Optionals), Zig (`if (opt) |v|` nur Optionals/Error-Unions).
Empfehlung: **Minor 4.5**, so umgesetzt.

### 3.2 `@`-Bindungen (`n @ 1..=9`, `whole @ Add(l, r)`) — Design

```
Pattern = … | IDENTIFIER '@' Pattern .
```
Bindet den ganzen Wert *und* prüft/zerlegt ihn. Eindeutig: `@` steht heute nur vor Attributen
(`@Deprecated`) in Deklarationsposition, nie in einem Pattern. Sema: `BindingPattern` + Sub-Pattern
im selben Scope (SEM0097 bei Doppelname). Lowering: `BindLocal(value)` und dann Rekursion —
zwei Zeilen im Compiler. Irrefutabilität = die des Sub-Patterns. Exhaustiveness unverändert.
Vergleich: Rust `n @ 1..=9`, Haskell `whole@(x:xs)`, Scala `whole @ Add(l, r)`, Swift: fehlt
(man schreibt `case let x where 1...9 ~= x`), Kotlin: fehlt. Falle: Rust erlaubte lange keine
Bindungen *innerhalb* eines `@`-Sub-Patterns (E0303, seit 1.56 erlaubt) — Lyric erlaubt sie
von Anfang an, weil beide Bindungen Kopien sind. Aufwand: Parser 10, Sema 15, Lowering 5 Zeilen.
**Minor, empfohlen**, §2 Grammatik, §7.6.

### 3.3 Array-Patterns `[]`, `[x]`, `[a, b]`, `[first, ..]`, `[.., last]` — IMPLEMENTIERT (Commit 4)

```
ArrayPattern = '[' [ ArrayElem { ',' ArrayElem } ] ']' .
ArrayElem    = Pattern | '..' [ IDENTIFIER ] .
```
Höchstens ein Rest; ein zweiter ist PAR0033 („die Positionen dazwischen wären nicht platzierbar").
**Lowering**: zuerst die Länge — `== n` ohne Rest, `>= n` mit Rest —, dann die Positionen *vor*
dem Rest von vorne (`ldelem` mit Konstante) und die *danach* von hinten (`len - k`), sodass
`[first, .., last]` ohne Arithmetik über die Mitte auskommt. Positionen sind gewöhnliche
Patterns, also nesten sie (`[Word("add"), Number(n), End]`). Irrefutabel ist nur `[..]`.
**Exhaustiveness über Längenklassen** (implementiert, §3.6): ein Arm, dessen feste Positionen
alle binden ohne zu testen, deckt seine Länge exakt bzw. ab dort alles; `[] | [x] | [a, ..]` ist
damit erschöpfend und braucht kein `_`, und eine Lücke wird als `[_]`/`[_, _]` benannt.

**Nicht implementiert: der benannte Rest** `[first, ..rest]`. Er müsste die abgedeckten Elemente
in ein eigenes Array kopieren, und diese Compilerversion kann kein Array einer erst zur Laufzeit
bekannten Länge bauen (es gibt nur `newarr` mit ausgeschriebenen Elementen, kein Slice-Opcode und
kein `rawArrayAlloc`-Native). Parser und Sema nehmen die Form (`rest: T[]`), das Lowering lehnt
sie mit LYR-IR0001 und dem Hinweis auf `slice` ab — §12.1 reserviert IR0001 genau dafür: gültiges
Lyric, das diese Version nicht lowern kann. Auflösung in 4.6: ein `arrslice`-Opcode oder das
`rawArrayAlloc`-Native, das stdlib-redesign ohnehin für `List<?T>` braucht.

Vergleich: Rust `[first, rest @ ..]` (Slice, keine Kopie — geht nur, weil Rust Slices hat),
Python `case [x, *rest]` (Kopie, wie Lyric es täte), JS/TS-Destructuring `[a, ...rest]`
(irrefutabel, kein Test), Swift: **hat keine Array-Patterns** (man schreibt `if xs.count == 2`),
Scala `case List(a, b)` / `case x :: rest` (über Extraktoren), OCaml `a :: rest` (Listen, nicht
Arrays). Falle aus Python: ein Rest in der Mitte macht die hinteren Indizes von der Länge
abhängig — deshalb zählt Lyric sie von hinten statt `restLen` auszurechnen.
Aufwand real: Parser 45, Sema 55, Lowering 60, VM 0. Anwendungsfälle stdlib-redesign:
`os.args()`-Auswertung, `splitFirst`, Tokenizer — alle ohne benannten Rest schreibbar.

### 3.4 String-Patterns — Stand und Design

Literal-Strings funktionieren bereits (`"hi" => …`, Gleichheit über `Eq` auf Strings, jetzt
getestet). Präfix-/Suffix-Patterns (`"GET " ++ rest`) sind in keiner Vergleichssprache außer
Haskell (ViewPatterns) und Elixir (`"GET " <> rest`) Standard; Rust/Swift/Kotlin verweisen auf
`strip_prefix`/`hasPrefix`. Empfehlung: **nicht als Sprachfeature**, sondern `string.stripPrefix`
(liefert `?string`) + let-else: `let rest = s.stripPrefix("GET ") else { … };` liest genauso.

### 3.5 Patterns in Lambda-Parametern und for-Köpfen — IMPLEMENTIERT (Commit 3)

```
ForInStmt   = 'for' '(' ( IDENTIFIER | Pattern ) 'in' Expr ')' Block .
LambdaParam = ( IDENTIFIER | TuplePattern ) [ ':' TypeExpr ] .
```
Nur irrefutable Patterns (Tupel, Struct-Felder, `_`, `@`); ein refutables → SEM0098 mit dem
Hinweis auf let-else im Body. Lambda: `((k, v)) => …` — Doppelklammer, weil `(k, v) =>` zwei
Parameter bleibt (Prototyp 14; Python kann es nicht, Rust `|(k, v)|`, Kotlin
`{ (k, v) -> … }` mit eigener Destructuring-Syntax, Swift `{ (k, v) in … }` ist dort *ein*
Tupel-Parameter — die Swift-Regel wäre für Lyric breaking). Lowering: `LowerPattern` mit
`assumeMatch: true` als erste Anweisung des Bodies — der Compiler existiert schon.
Aufwand: Parser 30, Sema 40, Lowering 20. **Minor, empfohlen.**

### 3.6 Exhaustiveness mit Zeugen-Diagnose — TEILWEISE IMPLEMENTIERT (Commit 4)

**Vorher**: `MissingCases` prüfte pro Variante, ob *ein* Pattern sie irrefutabel abdeckt, und gab
Variantennamen aus: „missing case(s): 'Some'" — auch dann, wenn `Some` sehr wohl vorkam und nur
sein Payload eine Lücke hatte. Bool und `?T` waren aufgezählt, alles andere brauchte `_`.

**Jetzt**: die Antwort ist ein **Zeugen-Pattern**, geschrieben wie ein Pattern:

| Scrutinee und Arme | Diagnose |
|---|---|
| `Opt<bool>`: `Some(true)`, `None` | `no arm matches 'Some(false)'` |
| `Res<Opt<int>, string>`: `Ok(Some(_))`, `Err(_)` | `no arm matches 'Ok(None)'` |
| `Shape`: `Circle(_)`, `Empty` | `no arm matches 'Rect { … }'` |
| `?Shape`: `Circle(_)`, `Rect{…}`, `Empty` | `no arm matches 'null'` |
| `int[]`: `[]`, `[_, _]` | `no arm matches '[_]'` |

Regel: **exakt, wo geantwortet wird.** Eine Variante mit *einem* Payload-Feld wird verfolgt (die
Rekursion nennt die Lücke darin), eine Variante mit mehreren Feldern wird als Ganzes gemeldet,
wenn nichts sie deckt, und sonst als gedeckt behandelt — ein Zeuge, der sich als doch abgedeckt
herausstellt, wäre schlimmer als keiner. Arrays über Längenklassen wie in §3.3. Das `nested`-Flag
aus §2 wandert mit: oben lässt ein Name über `?T` `null` offen, im Payload deckt er alles.

**Noch offen (vollständige Matrix, Maranget/rustc):** Tupel aus Enums
(`(Idle, _), (Connecting, _), …`), Varianten mit mehreren Feldern spaltenweise, Ranges über die
volle Integer-Breite (`0..=255` über `uint8`), unreachable-arm-Warnung. Der Weg dahin:
- Konstruktoren: Varianten (Arity aus Payload), Bool (`true`/`false`), Optional (`null`,
  `present`), Tupel (ein Konstruktor), Array-Längen (0..n, `≥n`), Literale/Ranges über
  Integer-Typen mit *Intervallarithmetik* über die Typbreite (`int8`: −128..127), Char als
  Intervall über Code-Points, String/Float: nur `_`.
- Ergebnis ist ein **Zeugen-Pattern** (`Some(false)`, `(Connecting { .. }, Timeout)`,
  `[_, _]`, `128..=255`), das in SEM0050 ausgegeben wird: „match on 'Opt<bool>' is not
  exhaustive — 'Some(false)' is not covered“. Für Or-Patterns und Guards: Guards zählen nicht
  (wie heute), Or wird in Zeilen expandiert.
- Nebenprodukt: **unreachable arm** (Warnung, neu SEM0100): eine Zeile, die zur Matrix
  nichts beiträgt (`_ => …` gefolgt von weiteren Armen; `Some(x)` nach `Some(_)`).
- `@NonExhaustive`-Enums (Anforderung stdlib-redesign via new-features, Rust-Modell): außerhalb
  des deklarierenden Moduls zählt die Matrix eine zusätzliche unbekannte Variante; ein Match
  ohne `_` ist SEM0050 „'Wait' is non-exhaustive outside its module — add a '_' arm“. Anker
  `NonExhaustive :: [OnType]` in core.lyr (stdlib-redesign).
Aufwand: ~300 Zeilen Sema (eigene Datei `Sema/Exhaustiveness.cs`), keine Lowering-Änderung.
Vergleich: Rust (rustc_pattern_analysis, Zeugen + unreachable), OCaml (Warning 8/11 mit
Zeuge), Swift („switch must be exhaustive — add missing case '.some(false)'“ mit Fix-it),
Kotlin (`when` nur über sealed/enum, ohne verschachtelte Zeugen). **Minor, hoch empfohlen** —
die Diagnose mit Zeuge ist der sichtbarste Gewinn für Nutzer.

### 3.7 `match` ohne Klammern um den Scrutinee — Spec-Frage

`match (x) {` vs. `match x {`. Heute: Klammern Pflicht (§6.2). Ohne Klammern kollidiert
`match p { … }` mit einem Struct-Initializer `p { … }`? Nein — `p` ist ein Identifier, kein
TypePath mit `{` … doch: `match Point { x = 1 } { … }` wäre zweideutig; Rust löst das mit der
Regel „kein Struct-Literal in Scrutinee-Position ohne Klammern“ (`_allowStructInit = false`,
die Lyric für `if`/`while` bereits kennt). Tupel-Scrutinee `match (s, ev) {` würde dann ohne
Doppelklammer lesbar. Empfehlung: **beide Formen erlauben** (Klammern optional, wie Go/Rust
ohne, Swift `switch x {`, Kotlin `when (x) {` mit), Formatter normalisiert auf *ohne*
Klammern; Major-Frage nur für den Formatter-Vertrag („The shape is the tool's contract“),
Sprache selbst additiv. Nicht in dieser Runde umgesetzt, weil der Formatter-Vertrag eine
Team-Entscheidung ist.

### 3.8 `never` in Patterns

Ein Arm vom Typ `never` (`panic(…)`, `throw …` als Ausdruck — new-features) trägt nichts zur
Arm-Unifikation bei (Regel von new-features in `UnifyArms`). Für die Exhaustiveness ist ein
Scrutinee vom Typ `never` mit null Armen erschöpfend (`match (x) {}` über `never`); ein Enum
ohne Varianten ebenso. Beides ergibt sich aus der Matrix (§3.6) ohne Sonderfall.

## 4. Vergleichstabelle (Kurzfassung)

| Feature | Rust | Swift | Kotlin | Scala | OCaml | Lyric 4.4.1 | Lyric jetzt |
|---|---|---|---|---|---|---|---|
| Verschachtelte Patterns | ja | ja | nein (nur Typtests) | ja | ja | eine Ebene | **beliebig** |
| Or-Pattern mit Bindung | ja | ja | nein | ja | ja | nein | **ja** |
| Literal in Payload | ja | ja | nein | ja | ja | nein | **ja** |
| Feldmuster mit Test | ja | ja | nein | ja | ja | nein | **ja** |
| if-let / while-let / let-else | ja | if/guard/while (Optionals), `if case` | `?:` | – | – | nein | **ja** |
| `@`-Bindung | ja | nein | nein | ja | ja (`as`) | nein | Design |
| Array/Slice-Pattern | ja | nein | nein | ja | ja (Listen) | nein | Design |
| Zeugen-Diagnose | ja | ja | teilweise | ja | ja | nein | Design |
| Arm-Reihenfolge = Testreihenfolge | ja | ja | ja | ja | ja | ja | ja |

## 5. Offene Punkte / Entscheidungen für das Team

1. `match x {` ohne Klammern: Formatter-Vertrag (§3.7) — Empfehlung: optional, Formatter
   entfernt Klammern.
2. `if (let a = x && let b = y)`: Swift-Komma-Form oder Rust-`let chains` — Vorschlag: nach 4.5
   als eigene Grammatikregel (`Condition = LetCond { '&&' (LetCond | Expr) }`).
3. Nested-Optional-Regel (§2, Punkt 1) ist eine Semantikfestlegung, die vor 4.5 in §7.6
   stehen muss; heute kompilierende Programme sind nicht betroffen (die Form war kaputt).
