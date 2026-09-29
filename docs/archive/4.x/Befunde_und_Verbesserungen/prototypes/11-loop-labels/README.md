# 11 — Labels für `break`/`continue`

Zugehöriger language-review-Punkt: **"Keine Labels für break/continue"** (MEDIUM).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 2 — Flag-Variante, Hilfsfunktion, Flag pro Zeile | 48 |
| `soll.lyr` | PROPOSAL | 30 |
| `vergleich.go` | Go (identische Syntax), Rust/Kotlin als Kommentar | 30 |
| Lib-Variante | nicht möglich (Kontrollfluss) | — |

## Ist-Stand
Doppel-`break` heißt heute: Flag setzen, Flag in der äußeren Bedingung wiederholen, und die
äußere Schleife läuft trotzdem ihr `i += 1` noch einmal (Umweg 1, 14 Zeilen für "finde (i, j)").
Umweg 2 ist die Hilfsfunktion mit `return` — sauber, aber die Schleife wandert aus ihrem Kontext
(Captures, `defer`, lokale Zähler müssen als Parameter mit). `continue outer` ist noch
unangenehmer: das Flag muss NACH der inneren Schleife geprüft werden, und Teilergebnisse der
abgebrochenen Zeile (`3` in `[3, -1, 4]`) sind schon in `sum` gelandet — `ist.lyr` druckt 11,
`soll.lyr` 8, und genau dieser Unterschied ist der Fehler, den Labels verhindern.

## Soll-Syntax und Grammatik (§5)
```
LabeledStmt  = IDENTIFIER ':' ( WhileStmt | DoWhileStmt | ForInStmt ) .
BreakStmt    = 'break' [ IDENTIFIER ] ';' .
ContinueStmt = 'continue' [ IDENTIFIER ] ';' .
```
- **Eindeutig**: `IDENTIFIER ':'` am Statement-Anfang ist heute ein Parsefehler (ExprStmt erlaubt
  nur Aufruf/Zuweisung/`resume`, §6.8; `x:` ist nichts davon). Der Parser braucht in `ParseStmt`
  (`Parser.Statements.cs:26`) einen Zwei-Token-Lookahead `Identifier Colon` → Label. Rusts
  `'outer:` wurde verworfen: `'a:` würde der Lexer als unvollständiges Char-Literal lesen
  (`'a'`, §1.5). Kotlins `outer@` bräuchte ein neues Token (`@` ist `AT_IDENT`, §1.3, für Attribute
  reserviert — `outer@` wäre lexikalisch `outer` + `@`… kollidiert).
- **Namensraum**: Labels sind kein Symbol des Werte-Namensraums (kein Konflikt mit einer Variable
  `outer`); Geltung nur im Körper der markierten Schleife; unbekanntes Label → `LYR-SEM-neu`;
  ein Label auf einer Schleife, das nie verwendet wird → Warnung (wie SEM0071).
- **Semantik (§7.2)**: `break L` verlässt L, `continue L` startet deren nächste Iteration.
- **`defer` (§7.5)**: alle Scopes zwischen der Anweisung und L werden verlassen; ihre defers laufen
  in umgekehrter Reihenfolge. Das Lowering hat dafür `LoopScope.DeferDepth`
  (`FunctionLowerer.cs:127,1114`) und `_loops` als Stack — `LowerBreak` (`:987`) nimmt heute
  `_loops.Peek()`; mit Label wird es `_loops.First(l => l.Label == name)` und die Defer-Entladung
  bis zu dessen `DeferDepth` — genau der Code, den `return` durch verschachtelte Scopes schon hat.
- **Spec**: §5 Grammatik, §7.2 (ein Absatz), §7.5 (ein Satz), §7.7 (Definite Assignment:
  `break L` ist ein Exit für alle Blöcke bis L — die Analyse kennt `break` bereits als Exit,
  `Flow.cs:64 HasBreak`; sie muss das Ziel kennen, um `while (true)`-Schleifen mit `break outer`
  richtig als "verlässt die äußere" zu werten).

## Bewertung
| | Ist (Flag) | Ist (Helfer) | Soll | Go |
|---|---|---|---|---|
| Doppel-break | 14 Z., 3 Flags/Indizes | 7 Z. + Funktion | 8 Z. | 8 Z. |
| continue outer | 8 Z., Flag, **falsches Teilergebnis** | — | 7 Z. | 7 Z. |

- **Fehlerklassen verhindert**: vergessene Flag-Prüfung in der äußeren Bedingung (eine Runde zu
  viel); Teilergebnisse einer abgebrochenen Iteration, die stehen bleiben (`sum 11` vs `8`);
  Flag-Variablen, die über Iterationen hinweg nicht zurückgesetzt werden.
- **Aufwand**: Lexer 0; Parser klein (~25 Z.); Sema klein (~40 Z.: Label-Stack, Auflösung,
  unbenutzt-Warnung, Flow mit Ziel); Lowering klein (~30 Z.: Ziel-Loop wählen, Defer-Entladung
  bis dorthin — Code existiert für return); VM 0; stdlib 0; Formatter: Label vor der Schleife.
- **Breaking**: nein (Minor).
- **Wechselwirkungen**: `defer` (s.o.); Coroutinen (`break L` über einen `yield` hinweg ist ein
  gewöhnlicher Sprung — kein Sonderfall); match (`break` in einem match-Statement in einer
  Schleife trifft die Schleife, wie heute); Prototyp 05 (Tail-Expression) — kein `break value`,
  s. dort; Narrowing nach `if (…) { break outer; }`: der frühe Exit narrowt den Rest des
  Blocks — §7.4-Regel gilt unverändert, das Ziel spielt keine Rolle.
- **Offene Semantikfragen**: (1) Label auf einem Block (`blk: { … break blk; }`) wie Rust/Java?
  Nein — Lyric hat keinen Block-Ausdruck, Nutzen gering. (2) Label vor `match`? Nein (kein
  Fallthrough, kein Bedarf).

## Empfehlung
**Sprachfeature, Minor, klein** — Aufwand ~100 Zeilen über drei Phasen, keine neue Token-Art,
beseitigt eine Fehlerklasse, die in jedem Suchalgorithmus mit zwei Schleifen lauert.
