# 14 — Tupel-Destructuring im `for`-Kopf und in Lambda-Parametern

Zugehöriger language-review-Punkt: **"Kein Tupel-Destructuring im for-Kopf und in Lambda-Parametern"** (MEDIUM).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 53 | 30 |
| `soll.lyr` | PROPOSAL | 24 |
| `vergleich.py` | Python (for-Entpacken ja, Lambda nein), Rust als Kommentar (beides) | 16 |
| Lib-Variante | nicht anwendbar (Syntax) | — |

## Ist-Stand
Jede Schleife über `Iterator<(K, V)>` (Map-Einträge, `enumerate`, `zip`) beginnt mit einem
Hilfsnamen und `let (k, v) = kv;` als erster Anweisung; jedes Lambda über Paare braucht einen
benannten Parameter mit ausgeschriebenem Tupeltyp und dieselbe Zeile. Drei Vorkommen in 30
Zeilen — in einem Programm mit Map-Verarbeitung ist es jede zweite Schleife.

## Soll-Syntax und Grammatik
```
ForInStmt   = 'for' '(' ( IDENTIFIER | TuplePattern ) 'in' ( RangeExpr | Expr ) ')' Block .
LambdaParam = ( IDENTIFIER | TuplePattern ) [ ':' TypeExpr ] .
```
- **for**: nach `for (` steht heute nur ein IDENTIFIER (`Parser.Statements.cs:170`); `(` dort ist
  ein Parsefehler → frei und eindeutig. Das Muster ist das irrefutable `TuplePattern` aus
  `DestructuringStmt` (§5, §7.1) — dieselbe Prüfung (Arity, Namen, `_`), dieselbe Lowering
  (`DestructuringStmt` als erste Anweisung des Körpers ist exakt der Desugar).
- **Lambda**: `((k, v)) => …` — äußere Klammern = Parameterliste (`Parser.cs:643 ParseLambda`),
  innere = Muster. **Wichtig**: `(k, v) => …` bleibt ein Lambda mit ZWEI Parametern; die
  Doppelklammer ist der Preis dafür, die bestehende Form nicht zu ändern. `IsLambdaAhead`
  (`Parser.cs:388`) muss `( (` als möglichen Lambda-Anfang erkennen — heute beginnt `((` einen
  geklammerten Ausdruck/Tupel; die Entscheidung fällt wie heute am `=>` nach der schließenden
  Klammer (Lookahead ist bereits balanciert).
- **Typen**: for — Elementtyp des Iterators muss ein Tupel passender Arity sein (sonst der
  bestehende Destructuring-Fehler); Lambda — der Parametertyp kommt aus dem Kontext (§7.3
  bidirektional) oder aus der Annotation `((a, b): (int, int))`; ohne beides → wie heute
  "lambda needs a context".
- **Spec**: §5 (zwei Produktionen), §7.1 (Destructuring gilt in drei Positionen), §7.2 (for-in
  bindet ein Muster), §7.3 (Lambda-Parameter als Muster), §7.7 (alle Namen assigned).

## Bewertung
| | Ist | Soll | Python | Rust |
|---|---|---|---|---|
| Map-Schleife | Hilfsname + 1 Z. | 0 Z. extra | 0 | 0 |
| Lambda über Paare | Name + voller Typ + 1 Z. | `((a, b))` | nicht möglich | `|(a, b)|` |

- **Fehlerklassen verhindert**: keine harte — QOL. Indirekt: Hilfsnamen (`kv`, `pair`, `p`) die
  nach der Destructuring-Zeile fälschlich weiterverwendet werden; `p.0`-artige Zugriffe gibt es in
  Lyric nicht, also ist die Destructuring-Zeile heute der EINZIGE Weg — das Feature entfernt
  Pflicht-Boilerplate, keine Wahlmöglichkeit.
- **Aufwand**: Parser klein (~30 Z.), Sema klein (~40 Z.: Muster binden in zwei neuen Scopes —
  `TypeChecker.cs:1094` bindet die Schleifenvariable, `:920` die Parameter), Lowering klein
  (~20 Z.: Desugar in die erste Anweisung), VM 0, Formatter klein.
- **Breaking**: nein (Minor).
- **Wechselwirkungen**: `_` im Muster (unused-Warnung unterdrückt, wie §7.1); verschachtelte
  Tupel `for (((a, b), c) in …)` erlaubt (Muster ist rekursiv); Optionals — `(?T)[]`-Iteration
  bleibt SEM0091; Ranges — `for ((a, b) in 0..3)` ist ein Typfehler (Element ist `int`); Generics
  — Elementtyp nach Substitution.
- **Offene Semantikfragen**: (1) Muster in Funktionsparametern (`fn f((a, b): (int, int))`) —
  konsequent, aber `params` und Defaults kollidieren; Vorschlag: nicht in Runde 1. (2) `var`
  im for-Muster? Nein — Schleifenvariablen sind `let`.

## Empfehlung
**Sprachfeature, Minor, klein** — reine Grammatik-Erweiterung mit bestehender Semantik.
