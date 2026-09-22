# 01 — Result-Typ / `?`-Operator vs. `throws`

Zugehöriger language-review-Punkt: *Result-Typ / `?`-Operator* (eigener Kandidat, bis language-review einen Titel nennt).

## Dateien
| Datei | Status | Zeilen (ohne Kommentare/Leerzeilen) |
|---|---|---|
| `ist.lyr` | läuft, exit 1 (= okCount) | 41 |
| `lib-variante.lyr` | läuft, exit 1 | 46 (davon 18 stdlib-Kandidat `Result<T,E>`) |
| `soll.lyr` | PROPOSAL, kompiliert nicht | 36 |
| `vergleich.rs` | Rust | 30 |
| `orthrow-test.lyr` | Nebenprobe, kompiliert NICHT (siehe Befund unten) | — |

## Befund: `throws` ist bereits Lyrics `?`
Rusts `?` macht zwei Dinge: früh zurückkehren und den Fehler weiterreichen. In Lyric ist das
`throws` — implizit, ohne Zeichen am Aufruf (`parsePort(line) * 2` in `parseAndDouble`). Ein
Sprachfeature `?` wäre also **redundant** für die Propagation. Was fehlt, ist der Fehler
**als Wert**: ein Ergebnis in eine `List` legen, mappen, später auswerten. Dafür braucht man
heute pro Fehlertyp ein handgeschriebenes Enum (`PortResult`) plus ein `try/catch`, das die
Ausnahme in den Wert umpackt (`tryParse`, 7 Zeilen Boilerplate pro Funktion).

## Vergleich Ist / Soll / Rust
| | Ist (throws + Hand-Enum) | Lib-Variante (`Result<T,E>` ohne `?`) | Soll (`try`-Ausdruck + `std.result`) | Rust |
|---|---|---|---|---|
| Propagation | implizit (throws) | manuell: `andThen`-Kette, Lambda | implizit (throws) | `?` pro Aufruf |
| Fehler als Wert | eigenes Enum + try/catch (10 Z.) | nativ | `try f(x)` (1 Ausdruck) | nativ |
| Sammeln in Liste | ja, über Hand-Enum | ja | ja | ja |
| Boilerplate main-Pfad | 41 Z. | 46 Z. | 36 Z. | 30 Z. |

Die Lib-Variante zeigt: ein generisches `Result<T, E>` lässt sich heute schreiben, aber
**ohne Sprachhilfe ist es unangenehm** — `map`/`andThen` müssen freie Funktionen sein
(eine Methode eines generischen Typs kann keinen eigenen Typparameter `U` einführen), jeder
Konstruktor braucht die vollen Typargumente (`Result<int, string>.Err(...)`, weil die Konstruktion
nie aus Feldwerten inferiert, §8.2), und `throw` ist kein Ausdruck, also `Err(e) => { throw e; }`.

## Soll-Syntax und Grammatik (Stand nach Abstimmung mit language-review)
- **Kein postfix `?`**: `x?.m` lext heute als `?.` (Lexer `Lexer.cs:950`, längster Match). Ein
  postfix `?` gefolgt von `.` wäre unlexbar, gefolgt von `;` ginge, aber `f()?.g()` müsste dann
  zwischen "Result-Unwrap, dann Member" und "optional chaining" wählen — Mehrdeutigkeit. Verworfen.
- **Eine gemeinsame Produktion** (vollständig in Prototyp 04 beschrieben):
  `TryExpr = 'try' ['?'] UnaryExpr [ 'catch' '(' CatchBinding ')' ( Block | Expr ) ]`.
  `try? e` → `?T` (Swift `try?`, deckt die "Grund egal"-Fälle); `try e catch (x: E) Expr` →
  Unifikation von T und Expr — das Result-Enum entsteht dann als gewöhnlicher catch-Ausdruck
  (`try R.Ok(f()) catch (e: E) R.Err(e)`), ohne eigene Regel. `try`+`{` bleibt das Statement.
- **Rückweg** (`Result → throws`) ist reine Bibliothek: `fn orThrow(): T throws E` — heute durch
  den Nebenbefund unten blockiert.

## Bewertung
- **Fehlerklassen verhindert**: vergessenes Umpacken (heute jeder `tryParse`-Wrapper von Hand),
  Typdrift zwischen `throws`-Typ und Hand-Enum (`PortResult.Err(ParseError)` ist nur per
  Konvention derselbe Typ wie die throws-Klausel; `try` leitet E aus der Klausel ab).
- **Aufwand**: Parser (1 Produktion, ~20 Z.), Sema (Typ von `TryExpr` aus `ThrowsOf`, ~40 Z.,
  ExceptionAnalyzer markiert Site als gedeckt), Lowering (Desugar in `try { Ok(...) } catch (e: E) { Err(e) }`
  — das existiert schon als Statement-Lowering), VM: nichts, stdlib: neues Modul `std.result`
  (~60 Z., Lib-Variante ist der Entwurf). Kein neues Token.
- **Breaking**: nein (Minor). `Result` als Name ist heute nicht reserviert; ein Programm mit
  eigenem `Result` kollidiert nur, wenn es `std.result` importiert.
- **Wechselwirkungen**: Generics (Result<T,E> ist ein gewöhnliches generisches Enum; die
  Monomorphisierung deckt es); match (Ok/Err-Arme, Exhaustiveness greift); Optionals (`ok(): ?T`
  ist die Brücke zu `??`); throws (siehe Befund unten).
- **Offene Semantikfragen**: (1) `try` über einen Ausdruck mit MEHREREN throwenden Aufrufen —
  Vorschlag: nur der äußerste Aufruf zählt, oder das Gesamte wird als Block behandelt
  (`try { a(b()) }`); ich empfehle "genau ein Aufruf, sonst LYR-SEM-neu". (2) `try` in einer
  Funktion ohne throws-Klausel: erlaubt, das ist gerade der Zweck. (3) Soll `Result<T, E>`
  `E :: [Throwable]` verlangen? Für `orThrow` ja, für reine Wert-Fehler (`string`) nein →
  zwei Varianten oder `orThrow` als freie Funktion mit eigener Constraint.

## Empfehlung
**stdlib reicht zu 80 %** (`std.result` mit `Result<T,E>`, `map`, `andThen`, `unwrapOr`, `ok`,
`orThrow`); der Sprachteil (`try`-Ausdruck) ist ein kleines Minor-Feature, das das letzte
Boilerplate (den try/catch-Wrapper) beseitigt. Ein postfix-`?` ist NICHT zu empfehlen. →
stdlib-review informiert.

## Nebenbefund (Compiler, aus `orthrow-test.lyr`)
`fn orThrow(): T throws E` in `enum Result<T, E :: [Throwable]>` — der Aufruf auf
`Result<int, Exception>` meldet `LYR-SEM0034: may throw 'Throwable'`, obwohl `E = Exception`
bekannt ist und `catch (e: Exception)` daneben steht. Ursache:
`src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:218-227` (`ThrownOf`) behandelt einen
Typparameter als "any"; die Klausel wird an der Aufrufstelle nicht durch die inferierte
Belegung substituiert. Kein Crash, aber macht generische throws-Klauseln praktisch unbenutzbar
mit typisiertem catch. Als `bug`/`shortcoming` in der Taskliste eingetragen.

Zweiter Nebenbefund: `throw` ist kein Ausdruck (`Err(e) => throw e` → LYR-PAR0002); Rust/Kotlin
erlauben `throw` als Ausdruck vom Typ `never`. Kleiner QOL-Punkt, an language-review gemeldet.
