# 02 — `if let` / `let … else` / `while let`

Zugehöriger language-review-Punkt: *if-let / let-else vs. match* (eigener Kandidat, bis language-review einen Titel nennt).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 52 |
| `soll.lyr` | PROPOSAL, kompiliert nicht | 38 |
| `vergleich.swift` | Swift (`guard let`, `if case`, `while let`) | 34 |
| Lib-Variante | in `ist.lyr` enthalten (`emailOfLib` über `std.option.andThen`) | — |

## Was heute fehlt — und was nicht
Für ein **lokales Identifier-Optional** ist let-else bereits da: `if (x == null) { return; }` narrowt
den Rest des Blocks (§7.4, "Narrowing nach frühem Exit"). Das Feature ist also **kein Muss für den
Grundfall**. Die drei echten Lücken (alle in `ist.lyr` gezeigt):

1. **Felder/Aufrufergebnisse narrowen nicht** (§7.4: nur Identifier). `u.profile` muss erst in ein
   Lokal kopiert werden — bei einer Kette zwei Kopien und zwei ifs (`emailOf`: 6 Zeilen statt 3).
   Die Kombinator-Form `andThen(u.profile, p => p.email) ?? "none"` ist kürzer, verliert aber die
   Unterscheidung der Fehlerfälle.
2. **Enum-Payload ohne vollständiges match**: der `_ => { }`-Arm existiert nur für die
   Exhaustiveness (`radiusOrZero`, `describeIfRect`).
3. **Pull-Schleifen** (`popFront`, `next()`, `readLine`): der Pull steht zweimal — vor der Schleife
   und am Ende — und das zweite Vorkommen ist der klassische Endlosschleifen-Fehler (`drain`).

## Soll-Syntax und Grammatik (Spec §2 / Grammatik §5)
```
Condition   = Expr | 'let' Pattern '=' Expr .
IfStmt      = 'if' '(' Condition ')' Block [ 'else' ( Block | IfStmt ) ] .
WhileStmt   = 'while' '(' Condition ')' Block .
BindingStmt = ( 'let' | 'var' ) Pattern [ ':' TypeExpr ] [ '=' Expr [ 'else' Block ] ] ';' .
```
- **Eindeutigkeit**: `let` ist ein reserviertes Keyword und kann keinen `Expr` beginnen, also ist
  `(let …` nach `if (`/`while (` heute ein Parsefehler → keine Kollision (`Parser.Statements.cs:121,137`).
- **let-else vs. if-Ausdruck**: `let x = if (c) a else b else { … };` — `IfExpr` verlangt sein
  `else` zwingend (`Parser.Patterns.cs:230`), das erste `else` gehört also immer dem if-Ausdruck; ein
  ZWEITES `else` nach dem vollständigen Initializer ist eindeutig let-else. Lesbar ist das nicht;
  Empfehlung wie Rust: der Initializer eines let-else darf kein nacktes `if`-/`match`-Expr sein
  (Klammern verlangen), als Sema-Regel.
- **Pattern in `let`**: heute `IDENTIFIER | TuplePattern` (`Parser.Statements.cs:66-77`). Mit
  `Pattern` wird `let Circle(r) = s else …` möglich; ein irrefutables Pattern (Name, Tuple, Struct-
  Feldpattern seit 4.4) braucht kein `else`, ein refutables (Enum-Variante, Literal, `?T`-Bindung)
  verlangt es (neuer Diagnosecode, analog `LYR-SEM0050`).
- **`?T` als Scrutinee eines Bindungs-Patterns**: bindet `T`, wenn present — genau die Regel, die
  §7.6 für `match (opt) { n => … }` schon hat. `while (let v = q.popFront())` fällt damit ohne
  eigene Regel ab. `if (let x = boolExpr)` wäre kein Fehler, nur nutzlos (Warnung).
- **`else`-Block muss den Scope verlassen**: dieselbe Prüfung, die §7.4 für "Branch always exits"
  bereits durchführt (`return`, `throw`, `break`, `continue`, `panic`).
- **Spec-Abschnitte**: §7.1 (Bindings), §7.4 (Narrowing — neue Quelle einer Tatsache), §7.6
  (Pattern-Semantik gilt auch hier), §7.7 (Definite Assignment: nach let-else sind die
  Pattern-Namen assigned, da der else-Block exitet).

## Bewertung
| | Ist | Soll | Swift |
|---|---|---|---|
| `emailOf` | 6 Z. (2 Kopien, 2 ifs) | 3 Z. | 3 Z. |
| `radiusOrZero` | 6 Z. (match + leerer Arm) | 2 Z. | 2 Z. |
| `drain` | 7 Z., Pull 2× | 5 Z., Pull 1× | 5 Z. |

- **Fehlerklassen verhindert**: vergessener Re-Pull am Schleifenende (Endlosschleife); leerer
  `_`-Arm, der später eine echte Variante verschluckt; Divergenz zwischen Kopie und Original bei
  Feld-Narrowing (`let p = u.profile; … u.profile = null; p` bleibt narrow — bei let-else genauso,
  aber sichtbar als eigene Bindung).
- **Aufwand**: Parser klein (3 Stellen, ~40 Z.); Sema mittel: neuer Narrowing-Ursprung (die
  Bindung ist ein frisches Lokal, also KEIN Narrowing des Scrutinees nötig — die §7.6-Regel
  "match narrowt den Scrutinee nicht" bleibt), Refutability-Prüfung (existiert für Field-Patterns
  seit 4.4 als "computes its own irrefutability", STATUS.md), Coverage des else-Blocks (existiert);
  Lowering: Desugar in `match` mit zwei Armen — die Match-Lowering über `?E` (M36) liefert genau
  das Nötige; VM/stdlib: nichts.
- **Breaking**: nein (Minor). Reine Erweiterung; kein heute gültiges Programm ändert seine Bedeutung.
- **Wechselwirkungen**: Narrowing (die Bindung ist ein neues Lokal vom Typ T — konsistent mit §7.4,
  das Original bleibt `?T`); Optionals (nicht nestend: `let v = q.popFront()` auf `Deque<?int>`
  — `null` wäre "leer" und "null-Element" zugleich, dieselbe Grenze wie §7.2 `LYR-SEM0091`; muss
  refused werden); match (Or-Pattern in `if let` erlaubt, bindende Or-Patterns heute `LYR-IR0001` —
  erbt die Grenze); throws (Initializer darf werfen, wie heute); Generics: keine.
- **Offene Semantikfragen**: (1) `var` in `if (var x = …)` zulassen? Vorschlag: nein, wie Rust
  (`let mut` innerhalb des Patterns) — Lyric hat kein `mut`-Pattern, also nur `let`. (2) Mehrere
  Bedingungen `if (let a = x && let b = y)` (Swift-Komma) — Vorschlag: NICHT in Runde 1; `&&`
  zwischen `let`-Bedingungen ist eine eigene Grammatikregel. (3) `else if (let …)` fällt aus der
  IfStmt-Kette gratis ab.

## Empfehlung
**Sprachfeature, Minor**, mit klarer Priorität: `while let` > `let … else` > `if let`. Nur
`while let` und Feld-Narrowing beseitigen echte Fehlerquellen; `if let` auf ein Lokal ist
gegenüber `if (x != null)` reiner Geschmack. Kein stdlib-Anteil.
