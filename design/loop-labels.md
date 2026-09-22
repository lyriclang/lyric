# Labels für `break` und `continue`

**Status:** implementiert (Prototyp auf Branch `worktree-agent-a4962b919be70e622`)
**Version:** Minor (4.5) — additiv; `name:` am Statement-Anfang war ein Parsefehler
**Spec:** §2 (Grammatik), §7.2 (ein Absatz), §7.3 (Coverage-Regel für `while (true)`), §7.5 (ein Satz), Appendix A (PAR0044, SEM0098/0099/0100) · **Guide:** 04
**Abhängigkeiten:** keine. Berührt nicht pattern-lambdas if-let/while-let (Label steht VOR `while`/`for`).
**Abgestimmt mit:** pattern-lambda (Parser-Stellen disjunkt), stdlib-redesign (kein Bedarf)

## Motivation

Doppel-`break` hieß bisher: Flag setzen, Flag in der äußeren Bedingung wiederholen, und die äußere
Schleife läuft ihr Inkrement trotzdem noch einmal (Prototyp 11: 14 Zeilen für „finde (i, j)“). Umweg 2 ist
die Hilfsfunktion mit `return` — sauber, aber die Schleife wandert aus ihrem Kontext (Captures, `defer`,
lokale Zähler als Parameter). `continue outer` ist schlimmer: das Flag wird NACH der inneren Schleife
geprüft, und Teilergebnisse der abgebrochenen Zeile sind schon in die Summe gelaufen — `ist.lyr` druckt 11,
das Label-Programm 8, und genau dieser Unterschied ist der Fehler, den Labels strukturell verhindern
(Test `Continue_outer_abandons_the_rest_of_the_inner_loop`).

## Syntax

```
Statement    = … | LabeledStmt | … .
LabeledStmt  = IDENTIFIER ':' ( WhileStmt | DoWhileStmt | ForInStmt ) .
BreakStmt    = 'break' [ IDENTIFIER ] ';' .
ContinueStmt = 'continue' [ IDENTIFIER ] ';' .
```

- **Eindeutig:** `IDENTIFIER ':'` am Statement-Anfang ist heute ein Parsefehler (§6.8: ein
  Ausdrucksstatement ist Aufruf, Zuweisung oder `resume`; `x:` ist nichts davon). Zwei-Token-Lookahead in
  `ParseStmt`. Ein Label vor etwas anderem als einer Schleife ist `LYR-PAR0044`; das Statement wird danach
  normal geparst (eine Diagnose, keine Kaskade).
- **Go-Form** (`outer:`), nicht Rust (`'outer:` — der Lexer läse `'o` als Char-Literal, §1.5) und nicht
  Kotlin (`outer@` — `@` ist für Attribute reserviert, §1.3).
- Labels sind **keine Symbole**: kein Konflikt mit einer Variablen `outer` (Test
  `A_label_shares_nothing_with_the_value_namespace`), kein Eintrag in der Symboltabelle, kein LSP-Symbol.

## Semantik

- `break L` verlässt die mit `L` markierte Schleife; `continue L` beginnt deren nächste Iteration (bei
  `while`/`do-while` die Bedingung, bei `for-in` den nächsten Wert).
- **Geltung:** nur im Körper der markierten Schleife. Ein Label, das eine umschließende Schleife wiederholt,
  ist `LYR-SEM0099` (ein Sprung wäre zweideutig; Rust warnt nur — Lyric lehnt ab, weil „das nähere gewinnt
  still“ genau die Fehlerklasse ist, die Labels beseitigen sollen). Unbekanntes Label → `LYR-SEM0098`.
  Unbenutztes Label → Warnung `LYR-SEM0100` (wie `SEM0071` für Bindungen).
- **`defer` (§7.5):** alle Scopes zwischen dem Sprung und der Zielschleife werden verlassen; ihre defers laufen
  in umgekehrter Reihenfolge, innerste zuerst — der Test misst `inner`, `outer`, dann die Summe 25.
- **Flow (§7.3):** `while (true)` divergiert, wenn nichts es verlässt. Ein `break L` in einer VERSCHACHTELTEN
  Schleife verlässt die äußere — die Analyse (`Flow.HasBreak(label, nested)`) steigt in innere Schleifen nur
  für die markierte Form ab. Ohne diese Ergänzung hätte `outer: while (true) { while (true) { break outer; } }`
  als „kehrt nie zurück“ gegolten und ein fehlendes `return` dahinter wäre unentdeckt geblieben.
- **Definite Assignment (§7.7):** unverändert — Schleifen werden konservativ behandelt; `break L` ist ein Exit
  für jeden Block bis L, und `AlwaysExits(BreakStmt)` gilt schon heute unabhängig vom Ziel.
- **Narrowing (§7.4):** `if (…) { break outer; }` narrowt den Rest des Blocks wie jeder frühe Exit — Ziel egal.
- **Lowering:** `LoopScope.Label`; `LowerBreak`/`LowerContinue` suchen den Stack von innen nach außen
  (`TargetLoop`); die Defer-Entladung bis `DeferDepth` des Ziels existierte bereits für `return`. Kein neuer
  Opcode, Bytecode-Format unverändert.
- **Coroutinen:** `break L` über ein `yield` hinweg ist ein gewöhnlicher Sprung.
- **Formatter:** `outer: for (…)`, `break outer;` — Test vorhanden. **LSP:** Label könnte als
  Semantic-Token markiert werden (Kategorie `label`), Rename über Label-Paare — optional.

### Nicht gemacht, bewusst

- Label auf einem Block (`blk: { … break blk; }`, Rust/Java): Lyric hat keinen Block-Ausdruck; Nutzen gering.
- Label vor `match`: kein Fallthrough, kein Bedarf.
- `break value` (Rust `break 'a v`): abgelehnt — kollidiert mit der Tail-Expression-Entscheidung
  (`design/value-block.md`) und Lyric hat keine Schleifen-Ausdrücke.

## Breaking

Nein. Drei neue Diagnosecodes, alle nur auf neuer Syntax.

## Aufwand (real)

AST 4 Properties, Parser ~45 Z., Formatter 4, Dumper 2, Flow 12, SemaRules ~40, LoopScope 1, Lowering ~15.
Tests: 9 Sema, 4 Vm, 2 Parser, 1 Formatter.

## Vergleich

| Sprache | Form | Bemerkung |
|---|---|---|
| Go | `outer: for … { break outer }` | Identisch mit Lyric. Label auf `for`/`switch`/`select`; `goto` existiert daneben (nicht in Lyric). Unbenutztes Label ist ein **Fehler** (Go: „label defined and not used“). |
| Rust | `'outer: loop { break 'outer; }`, `break 'a value` | Lifetime-Syntax; Label auf Blöcken (`'a: { … }`) seit 1.65; `break value` nur aus `loop`. Shadowing eines Labels ist eine Warnung. |
| Kotlin | `outer@ for (…) { break@outer }` | `@`-Syntax, Labels auch für `return@lambda` — dort ist es wirklich nötig (nicht-lokales `return` aus Lambdas). |
| Java/C# | Java: `outer: for … break outer;` C#: keine Labels für Schleifen, nur `goto`. | C#-Autoren nutzen `goto` oder Hilfsmethoden — ein bekannter Kritikpunkt. |
| Swift | `outer: for … { break outer }` | Wie Go/Java; auch auf `if`/`switch`/`do`. |
| Zig | `outer: while (…) { break :outer; }`, `break :blk value` | Block-Labels liefern Werte (`const v = blk: { … break :blk x; }`), was Zigs Ersatz für Block-Ausdrücke ist. |
| Python | keine | `for … else`-Klausel als Teilersatz; sonst Flag/Funktion. |

Go:
```go
outer:
for i := range grid {
    for j := range grid[i] {
        if grid[i][j] == target { found = [2]int{i, j}; break outer }
    }
}
```
Rust:
```rust
'rows: for row in rows {
    let mut partial = 0;
    for &x in row { if x < 0 { continue 'rows; } partial += x; }
    sum += partial;
}
```

**Fallen, die Lyric vermeidet:** (1) Kotlins `@`-Syntax kollidiert mit Attributen; (2) Rusts `'a` mit dem
Char-Literal; (3) Rusts Label-Shadowing als Warnung — Lyric lehnt ab; (4) Gos „unused label = error“ ist zu
hart für Refactoring-Zwischenstände — Lyric warnt (konsistent mit SEM0071); (5) Zigs Block-Labels als
Werteträger vermischen zwei Features — Lyric hält Wert-Blöcke (Tail-Expression) und Labels getrennt.

**Empfehlung:** Go/Java/Swift-Form (`name:` vor der Schleife), Sema-Regeln nach Go mit Warnung statt Fehler
für unbenutzte Labels.

## Offene Fragen

1. Soll `break L` auch aus einem `match`-Statement in einer Schleife die Schleife wählen dürfen, wenn `L`
   die Schleife ist? Ja — heute schon: `match` ist kein Sprungziel, `break` trifft die Schleife (getestet in
   `spin`).
2. LSP: Semantic Token `label`, Go-to-Definition Label → Schleife. Kleiner Aufwand, nicht im Prototyp.

## Spec-Diff

§2:
```diff
 Statement       = Block
                 | BindingStmt
                 …
+                | LabeledStmt
                 | WhileStmt
 …
+LabeledStmt     = IDENTIFIER ':' ( WhileStmt | DoWhileStmt | ForInStmt ) .
-BreakStmt       = 'break' ';' .
-ContinueStmt    = 'continue' ';' .
+BreakStmt       = 'break' [ IDENTIFIER ] ';' .
+ContinueStmt    = 'continue' [ IDENTIFIER ] ';' .
```
§7.2:
```diff
 `while`, `do-while`, and `for (x in e)` with `break`/`continue`. …
+A loop may carry a label, `outer: while (…) { … }`; `break outer` leaves that loop and
+`continue outer` starts its next iteration, from any depth. A label is not a name in the value
+namespace and is visible only inside the body of the loop it marks: a jump to a label no
+enclosing loop carries is `LYR-SEM0098`, a label repeating an enclosing one is `LYR-SEM0099`
+(the jump would be ambiguous), and a label nothing jumps to is a warning (`LYR-SEM0100`). A
+label stands before a loop and nothing else (`LYR-PAR0044`).
```
§7.3 (Coverage): „`while (true)` without a `break` that leaves it — a plain `break` in its body, or a `break L` naming it anywhere inside — never returns to the statement after it.“
§7.5:
```diff
 `defer stmt;` schedules the statement for **the enclosing block's exit**, on every exit path —
-falling off the end, `return`, `throw`, `break`, `continue`.
+falling off the end, `return`, `throw`, `break`, `continue` — a labeled `break` or `continue`
+leaves every scope between it and the loop it names, and each of them runs its defers,
+innermost first.
```
Appendix A: vier neue Zeilen (PAR0044 E, SEM0098 E, SEM0099 E, SEM0100 W).
