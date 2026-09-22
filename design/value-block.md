# Value-Block: Tail-Expression im Block-Arm und im Block-Lambda

**Status:** implementiert (Prototyp auf Branch `worktree-agent-a4962b919be70e622`)
**Version:** Minor (4.5) — additiv; ein Ausdruck ohne `;` vor `}` war `LYR-PAR0016`
**Spec:** §2 (`ValueBlock`), §6.9 (Wert des Blocks), §7.3 (Block-Lambdas), §7.5 (defer-Reihenfolge), §7.6, Appendix A (SEM0033-Text) · **Guide:** 03, 06
**Abhängigkeiten:** `design/throw-expression.md` (Tail `throw e` ist ein never-Tail). Vorbereitung für den catch-Block des `try`-Ausdrucks (`design/try-expression.md`).
**Abgestimmt mit:** pattern-lambda (Branch `worktree-agent-a1c2eb789de86ba9d`, fertig — Merge-Notizen unten), lyriclings (ICE „match expression produced no value“ — beide Hälften stehen jetzt: mein `LowerReturn` ist never-aware, ihre Sema gibt einem `match` ohne wertliefernden Arm den Typ `never`)

## Motivation

Ein Block-Arm im match-Ausdruck lieferte keinen Wert; `return` im Arm verlässt die Funktion. Die beste
heutige Form (Prototyp 05) ist `var next: State;` vor einem match-**Statement** mit Zuweisung in jedem Arm
— ein `var`, eine Deklaration ohne Initializer, eine Zuweisung pro Arm, und die Diagnose bei einem
vergessenen `return` im Arm hieß „cannot assign 'State' to 'void'“. Umweg B (zwei matches über denselben
Scrutinee: Seiteneffekt und Wert getrennt) läuft auseinander, sobald jemand einen Arm nur an einer
Stelle ändert. Block-Lambdas hatten dieselbe Lücke: `(x) => { let d = x * 2; return d + 1; }` verlangt
ein `return`, das im Ausdrucks-Lambda nicht steht.

## Syntax

```
ValueBlock = '{' { Statement } [ Expr ] '}' .      (* nur in Wert-Position *)
MatchArm   = Pattern [ 'if' Expr ] '=>' ( Expr | ValueBlock ) .
Lambda     = '(' … ')' [ ':' TypeExpr ] '=>' ( Expr | ValueBlock ) .
```

- **Wo:** Arm eines `match` (Ausdruck UND Statement — der Parser kennt den Unterschied nicht; die Sema
  entscheidet), Body eines Block-Lambdas, später der catch-Block des `try`-Ausdrucks. Statement-Blöcke
  (`if`, `while`, `{ }`, Funktionskörper) bleiben wertlos: `if (b) { 5 }` ist weiter `LYR-PAR0016`.
  Ein VERSCHACHTELTER Statement-Block innerhalb eines ValueBlocks ist wieder ein Statement-Block
  (das `_allowTail`-Flag gilt nur für die eigene Statement-Liste).
- **Tail-Erkennung:** `ParseExprStmt` liest einen Ausdruck; folgt statt `;` direkt `}`, ist es der Tail
  (`TailExprStmt`). `throw e` als Tail wird in `ParseThrow` erkannt (Tail eines `ThrowExpr`). Ein Tail, der
  mit `Name {` beginnt (Struct-Initializer), braucht Klammern — dieselbe Regel wie für jedes Statement
  (§6.8), damit `Name` + Block nicht umgedeutet wird. `State.Busy(n)` ist ein Aufruf, kein Initializer, und
  steht ohne Klammern.
- **AST:** kein neuer Block-Knoten — `Block.Tail` liest das letzte Statement als `TailExprStmt`. Damit
  bleiben alle Walker, die Statements durchlaufen, korrekt; nur die Typisierung der Owner kennt den Tail.

## Semantik

- **Typ (§6.9):** der Wert eines ValueBlocks ist der Typ seines Tails; er nimmt an der Unifikation der
  Arme teil und erhält den Kontexttyp (§3.1) wie ein Ausdrucks-Arm (`let v: int8 = match (b) { true => { 1 }, … }`
  adaptiert die `1`). Ein Block ohne Tail, der auf jedem Pfad verlässt, trägt weiter nichts bei; ohne
  Tail und ohne Exit bleibt `LYR-SEM0033`, mit dem Zusatz „or end in a tail expression“.
- **Tail vom Typ `never`** (`throw e`, `panic(…)`): der Arm divergiert, trägt nichts bei — die Regel aus
  `design/throw-expression.md`.
- **match-Statement:** ein Tail hat kein Ziel; er unterliegt der ExprStmt-Regel (`LYR-SEM0022`, nur
  Aufruf/Zuweisung/`resume`/`throw`). `true => { println("t") }` ist also erlaubt und liest sich wie ein
  Ausdrucks-Arm; `true => { 5 }` nicht.
- **Block-Lambdas (§7.3):** der Tail ist `return tail;` am Blockende — er zählt als Wert-Return für die
  void-Default-Regel, geht in die Inferenz mit ein (`{ if (x < 0) { return 0; } x * 2 }` unifiziert
  `int`/`int`), wird gegen den Kontext-/Annotationstyp geprüft (`(): string => { 1 }` ist SEM0001), und
  deckt das Blockende (kein SEM0046).
- **Definite Assignment (§7.7):** der Tail ist ein Lesezugriff wie ein Ausdrucks-Arm; die DA-Analyse läuft
  jetzt auch durch Block-Arme von match-**Ausdrücken** (vorher wurden sie übersprungen — ein Lesezugriff auf
  eine unzugewiesene Variable dort blieb ungemeldet; jetzt gemeldet).
- **`defer` (§7.5):** der Tail ist das letzte Statement des Scopes — sein Wert wird in den Ergebnis-Slot
  gespeichert (bzw. als Return-Wert berechnet), DANN laufen die defers des Blocks. Test
  `The_tail_is_taken_before_the_blocks_defers_run`: `{ defer n = 100; n }` liefert 7. Rust: Drop nach dem
  Tail — gleiche Reihenfolge.
- **Lowering:** `TailSink` (Slot+Typ für einen Arm, `AsReturn` für ein Lambda, nichts für ein
  match-Statement) wird vom Owner um `LowerScope` gesetzt; `LowerTail` speichert/returnt. Kein neuer Opcode.
- **Formatter:** Tail ohne `;` bleibt Tail (überflüssige Klammern gehen). `lyrfmt` schreibt `return x;` NICHT
  zu `x` um — andere Bedeutung im Lambda mit mehreren Pfaden, und im Arm verlässt `return` die Funktion.
- **LSP:** kein Symbol, keine Änderung.

## Wechselwirkungen

- Narrowing: im Tail gilt, was der Block etabliert hat (`if (o == null) { return …; } o` ist `T`).
- Optionals: Tail `null` widert den Arm-Typ zu `?T` wie ein `null`-Arm.
- throws: Tail darf werfen wie jeder Ausdruck; ExceptionAnalyzer hat den Fall.
- Generics: keine. Coroutinen: ein ValueBlock in einem Coroutine-Body (Arm eines match-Ausdrucks) —
  Tail ist ein gewöhnlicher Ausdruck; `yield` im Block-Arm bleibt möglich.
- Labels: kein `break value` — bewusst (siehe `design/loop-labels.md`).
- pattern-lambda (Endstand geprüft, 4 Commits): ihr Pattern-Compiler ersetzt die Pattern-Hälfte von
  `LowerMatch`; `LowerArm` ist bei ihnen **unverändert**, meine `Diverges`-Zeilen und der `TailSink`
  gewinnen dort. In `ParseMatchArm` ändern wir verschiedene Zeilen derselben Methode (sie `_guardHead`
  um den Guard, ich `ParseBlock(valueBlock: true)` für den Body) — beides behalten. Ihr `LowerIf` für
  if-let nutzt bereits `LowerScope`, nimmt meinen `defer`-Fund also vorweg; mein Commit `869c360f` fixt
  dieselbe Stelle im gewöhnlichen `LowerIf`. In `LowerReturn` haben **beide** einen never-Zweig mit
  derselben Bedeutung — beim Merge einen davon behalten. Ihre neuen AST-Knoten (`LetCondExpr`,
  `LetPatternStmt`, `ArrayPattern`, `RestPattern`) und die Konvention „ein `NameSpan` ist leer, wenn die
  Quelle nichts benennt" berühren meinen `TailExprStmt` nicht.

## Breaking

Nein. Jedes Programm mit Tail war ein Parsefehler. Die neue DA-Prüfung in Block-Armen von match-Ausdrücken
kann Programme ablehnen, die vorher fälschlich durchgingen (ein Lesezugriff auf eine unzugewiesene `var` im
Arm) — das ist eine Bug-Korrektur der Spec-Regel, kein Sprachbruch.

## Aufwand (real)

AST 10 Z., Parser ~25, Formatter/Dumper/Children 3×2, Analyzer 5×1, TypeChecker ~70, Lowering ~50.
Tests: 14 Sema, 5 Vm, 3 Parser, 1 Formatter.

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| Rust | Jeder Block ist ein Ausdruck; `let x = { a; b };`; Tail ohne `;`, `;` macht `()` | Konsequent, aber `;`-Semantik ist die häufigste Anfängerfalle („expected i32, found ()“). Struct-Literal-Ambiguität in `if`-Bedingungen per Klammerregel. |
| Kotlin | `when` mit Block-Zweig: letzter Ausdruck ist der Wert; Lambdas: letzter Ausdruck ist der Wert; `if` ist Ausdruck | Nur Lambdas/when/if, nicht jeder Block. `return` in Lambda ist nicht-lokal (Labels nötig). |
| Swift | `switch` als Ausdruck (5.9) nur mit EINEM Ausdruck pro Zweig; Single-Expression-Closures implizit; sonst `return` | Konservativ: kein Tail in mehrstatement-Blöcken (Evolution SE-0380 lehnte es ab: „makes code harder to read“). |
| Zig | `blk: { …; break :blk v; }` — Labeled Block mit `break value` | Explizit, aber Label-Rauschen; für Match-Arme Ausdruck oder Block mit `break :blk`. |
| Scala | Jeder Block ist ein Ausdruck (wie Rust, ohne `;`-Falle) | Vorbild für Kotlin. |
| Go/C#/TS/Python | kein Block-Wert; C# `switch`-Ausdruck nur mit Ausdrucksarmen, `throw` erlaubt | Umweg: lokale Funktion oder `var`+Zuweisung. |

Rust:
```rust
let next = match e {
    Event::Tick => { let n = count + 1; println!("tick {n}"); State::Busy(n) }
    Event::Stop => { println!("stopping"); State::Closed }
};
```
Kotlin:
```kotlin
val next = when (e) {
    Event.Tick -> { val n = count + 1; println("tick $n"); State.Busy(n) }
    Event.Stop -> { println("stopping"); State.Closed }
}
val twice = { x: Int -> val y = x * 2; y + 1 }
```

**Fallen, die Lyric vermeidet:** (1) Rusts `;`-Falle (Block-Wert `()` durch ein versehentliches `;`) — in
Lyric ist ein Block ohne Tail in einem match-Ausdruck weiterhin ein FEHLER (SEM0033), kein stilles
`void`; (2) Kotlins nicht-lokales `return` in Lambdas — Lyrics `return` im Block-Lambda bleibt lokal,
und der Tail koexistiert damit; (3) Zigs Label-Rauschen; (4) Swifts Beschränkung auf Ein-Ausdruck-Zweige,
die den häufigsten Fall (log + Wert) ausschließt; (5) Struct-Literal-Ambiguität: Lyric behält §6.8 und
verlangt Klammern für einen Initializer-Tail — kein Zwei-Token-Lookahead über balancierte Klammern.

**Empfehlung:** Kotlin-Modell (Tail nur in Wert-Blöcken: when/match-Zweig und Lambda; Statement-Blöcke
wertlos, kein allgemeiner Block-Ausdruck), mit Rusts Regel für den defer/Drop-Zeitpunkt.

## Offene Fragen

1. Tail auch im Funktionskörper (`fn f(): int { 1 }`, Rust)? Nein in 4.5 — die `return`-Coverage-Regel
   (§7.3) und die Lesbarkeit von Funktionsenden sprechen dagegen; Lambdas sind kurz, Funktionen nicht.
2. `if`-STATEMENT-Zweige als ValueBlock, wenn das `if` in Wert-Position steht (`let x = if (c) { … } else { … }`)?
   Das wäre ein neues Konstrukt (if-Ausdruck mit Blöcken) — Kandidat für 4.6, Grammatik: `IfExpr = 'if' '(' Expr ')' ( Expr | ValueBlock ) 'else' ( Expr | ValueBlock | IfExpr )`.
3. Struct-Initializer als Tail ohne Klammern per Lookahead? Nach Rückmeldung entscheiden.

## Spec-Diff

§2:
```diff
+ValueBlock      = '{' { Statement } [ Expr ] '}' .   (* a block in value position: its last
+                                                        expression, without ';', is its value *)
-MatchArm        = Pattern [ 'if' Expr ] '=>' ( Expr | Block ) .
+MatchArm        = Pattern [ 'if' Expr ] '=>' ( Expr | ValueBlock ) .
-Lambda          = '(' … ')' [ ':' TypeExpr ] '=>' ( Expr | Block ) .
+Lambda          = '(' … ')' [ ':' TypeExpr ] '=>' ( Expr | ValueBlock ) .
```
§6.9:
```diff
 `if (c) a else b` and `match` in value position unify their arm types: …
+A block arm may end in a TAIL — an expression standing last without `;` — and then has the
+tail's type, unified and adapted exactly as an expression arm. A block arm without a tail
+has no value: it must leave on every path (`LYR-SEM0033`) and contributes nothing. A tail
+standing where the value has nowhere to go — a block arm of a `match` STATEMENT — is held to
+the expression-statement rule (§6.8). A statement block (`if`, `while`, a function body) never
+carries a tail. A tail beginning with a struct initializer is parenthesized, as any
+statement-first initializer is (§6.8).
-Block lambdas infer their return type from their `return` statements under the same
-unification (chapter 7).
+Block lambdas infer their return type from their `return` statements and their tail, which
+is `return tail;` at the block's end, under the same unification (chapter 7).
```
§7.5: „A value block's tail is evaluated before the block's deferred statements run.“
§7.3: „A **block** lambda … infers its return type from its `return` statements and its tail …; a block ending in a tail is covered.“
Appendix A: `LYR-SEM0033 | E | A block arm of a match expression that neither ends in a tail expression nor leaves on every path.`
