# 05 — Block-Arm im match-Ausdruck liefert einen Wert (Tail-Expression)

Zugehöriger language-review-Punkt: **"Ein Block-Arm im match-Ausdruck liefert keinen Wert; `return` verlässt die Funktion"** (HIGH).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 12 — Umweg A (`var next` + match-Statement) und B (zwei matches) | 62 |
| `soll.lyr` | PROPOSAL — Vorschlag A (Tail-Expression), Vorschlag B nur als Kommentar | 44 |
| `vergleich.rs` | Rust (Block = Ausdruck), Kotlin als Kommentar | 30 |
| Lib-Variante | nicht möglich (Kontrollfluss) | — |

## Ist-Stand
`step` in `ist.lyr` ist Umweg A: `var next: State;` vor dem match-**Statement**, jeder Arm weist zu,
die Definite-Assignment-Regel für exhaustive match-Statements (§7.7) trägt `next` hinaus. Das ist
die beste heutige Form — sie kostet ein `var`, eine Deklaration ohne Initializer und eine
Zuweisung pro Arm, und der Arm `Ack` braucht ein inneres match als Ausdruck, das man sonst
direkt hätte. Umweg B (`stepB`) trennt Seiteneffekt und Wert in zwei matches über denselben
Scrutinee: dieselbe Fallunterscheidung steht zweimal und läuft auseinander, sobald jemand einen
Arm nur an einer Stelle ändert.

## Soll-Syntax und Grammatik
```
ValueBlock = '{' { Statement } [ Expr ] '}' .         (* nur in Wert-Position *)
MatchArm   = Pattern [ 'if' Expr ] '=>' ( Expr | ValueBlock ) .
Lambda     = … '=>' ( Expr | ValueBlock ) .
```
- **Wo**: nur Blöcke in Wert-Position — Arm eines match-Ausdrucks, Body eines Block-Lambdas,
  catch-Block eines `try`-Ausdrucks (Prototyp 04). Ein Statement-Block (`if`, `while`, `{ }`)
  bleibt wertlos; damit bleibt §6.8 ("ein Statement beginnt nie mit einem Struct-Initializer")
  unberührt.
- **Disambiguierung des Tails**: der Parser liest im ValueBlock Statements wie heute
  (`ParseBlock`, `Parser.Statements.cs:52`). Ein Tail entsteht, wenn `ParseExprStmt` einen
  Ausdruck geparst hat und statt `;` ein `}` folgt (`Parser.Statements.cs:261`) — heute ein
  Fehler (`LYR-PAR0001` "expected ';'"), also keine Kollision mit gültigen Programmen. Der
  Sonderfall `Name { … }` als Tail (`State.Closed { }` gibt es nicht, aber `Point { x = 1 }`):
  in Statement-Position wird `Name {` heute als "Name, dann Block" gelesen
  (`Parser.cs:459 IsStructInitAhead` mit `_allowStructInit = false`). Regel für den ValueBlock:
  steht `Name {` als LETZTES Element und folgt dem `}` des inneren Blocks direkt das `}` des
  ValueBlocks, ist es ein Struct-Initializer — ein Zwei-Token-Lookahead nach dem balancierten
  `{ … }`. Alternativ und einfacher: Tail-Struct-Initializer in Klammern verlangen
  (`(Point { x = 1 })`), analog zu Rusts Regel für Struct-Literale in `if`-Bedingungen.
- **Typregel** (§6.9): der Wert des Blocks ist der Typ des Tails; er nimmt an der Unifikation der
  Arme teil und erhält den Kontexttyp (§3.1) wie ein Ausdrucks-Arm. Ein Block, der auf jedem
  Pfad verlässt, trägt weiter nichts bei (heutige Regel, `TypeChecker.cs:4152-4159`). Ein Block
  ohne Tail und ohne Exit bleibt `LYR-SEM0033` — die Meldung sollte den Hinweis "a `return`
  inside a block arm leaves the enclosing function" tragen.
- **Definite Assignment** (§7.7): unverändert — der Tail wird wie ein Ausdrucks-Arm behandelt.
- **Block-Lambdas** (§7.3): heute inferieren sie aus `return`-Statements; mit Tail dürfen beide
  Formen koexistieren (Tail ≡ `return tail;` am Ende), und die Regel "die returns müssen
  übereinstimmen" nimmt den Tail auf.

## Bewertung
| | Ist A | Ist B | Soll | Rust |
|---|---|---|---|---|
| `step` | 17 Z., 1 `var`, 4 Zuweisungen | 15 Z., Fallunterscheidung 2× | 10 Z. | 10 Z. |

- **Fehlerklassen verhindert**: auseinanderlaufende Doppel-Matches (Umweg B); `return` im Arm,
  das die Methode verlässt statt den Arm zu beenden (die heute irreführende Diagnose "cannot
  assign 'State' to 'void'"); vergessene Zuweisung in einem Arm (heute SEM0018, mit Tail ein
  Typfehler am Arm — präziser).
- **Aufwand**: Parser klein (~30 Z.: `ParseValueBlock`, Tail-Erkennung nach fehlendem `;`);
  Sema mittel (~60 Z.: Block-Typ = Tail-Typ, Unifikation, Kontext-Propagation, bessere SEM0033);
  Lowering klein (~30 Z.: Tail wie den Ausdrucks-Arm lowern — `FunctionLowerer.cs:519` hat für
  Lambdas schon beide Formen); VM 0; stdlib 0; Formatter: Tail ohne `;` erhalten.
- **Breaking**: nein (Minor) — jedes Programm mit Tail ist heute ein Parsefehler.
- **Wechselwirkungen**: Narrowing (im Tail gilt, was der Block etabliert hat — z.B. nach
  `if (o == null) { return …; }` im Arm ist `o` im Tail `T`); Optionals (Tail `null` widert den
  Arm-Typ zu `?T`, wie heute ein `null`-Arm); throws (Tail darf werfen wie jeder Ausdruck);
  Generics: keine; `defer` im ValueBlock läuft NACH der Tail-Auswertung, vor der Übergabe des
  Werts — muss so in §7.5 stehen (Rust: Drop nach dem Tail).
- **Offene Semantikfragen**: (1) Tail auch in Statement-Blöcken erlauben (Rust: ja, `let x = { … };`)?
  Vorschlag nein — Lyric hat keinen Block-Ausdruck, und `Name { }`-Kollision bliebe. (2) Formatter:
  soll `lyrfmt` `return x;` als letzte Anweisung in einem Arm zu `x` umschreiben? Nein — Bedeutung
  ist verschieden. (3) Warnung bei Tail, dessen Wert verworfen wird (Statement-Kontext)? Nicht
  nötig, da dort kein Tail erlaubt ist.

## Empfehlung
**Sprachfeature, Minor, hoher Nutzen** — Vorschlag A. Vorschlag B (`break value`) verworfen:
`break` in einem Arm innerhalb einer Schleife wäre zweideutig, und Lyric hat keine Labels.

## Nebenbefund (Bug, beim Bauen gefunden)
`Dial(_) | Hangup => { … }` wird mit `LYR-IR0001: an or-pattern that binds` abgelehnt, obwohl
`_` nichts bindet: `FunctionLowerer.cs:2489-2491` fragt `alternatives.Any(PatternBinds)`, und
`PatternBinds` zählt eine Variante mit Sub-Patterns offenbar als bindend, ohne die Sub-Patterns
auf `_` zu prüfen. Umgehung: zwei Arme. Als `bug` in der Taskliste.
