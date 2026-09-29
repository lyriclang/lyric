# 08 — Syntax und Ergonomie

Lebendes Dokument des Bereichs 8 — der **Sammelpunkt**: jede Schreibweise, die die Bereiche
1–7 und 9 als „Arbeitsnotation, Syntax Bereich 8" markiert haben, fällt hier. Fragen Y1–Y12, je
**entschieden** oder **offen**. Basis: `docs/Grammar.md` (4.x) und alle Spec-Dokumente 00–07, 09.

## Bestandsaufnahme Lyric 4

Aus `docs/Grammar.md`, `../operatoren.md` (46 Fragen), `../funktionen.md` (49),
`../enums-patterns.md` (45), `../strings.md` (48):

- **Vokabular**: 28 reservierte Wörter (`module import as pub static struct class enum
  interface extend fn mut let var params if else while do for in match break continue return
  yield resume defer try catch throw true false null this`), kontextuell `type`, `opaque`,
  `extern`, `throws`, `comptime`; Builtin-Typnamen sind Bezeichner.
- **Deklarationen**: `fn name<T :: [I]>(p: T = d): R throws E { }`; `struct`/`class`/`enum`/
  `interface` mit `:: [Konformanzen]`; Felder ohne Modifikator, Member mit `,` getrennt;
  Enum-Varianten, Methoden nach `;`; `extend T :: [I] { }`; `type`/`opaque type`; `extern`.
- **Ausdrücke**: 16 Präzedenzstufen; `as` auf Stufe 3 (über `*`); `..`/`..=` als Operator-
  stufe, obwohl kein Wert; `??` rechtsassoziativ; **Zuweisung ist ein Ausdruck** (Wert = neuer
  Wert des Ziels, `a = b = 3`, `if (f = true)` kompiliert kommentarlos); `++`/`--` als Ausdruck,
  `++x;` als Statement abgelehnt; `if (c) a else b` nur mit `Expr`-Seiten (`if (c) { 1 } else
  { 2 }` als Wert: fünf Diagnosen); `match` als Ausdruck mit Value-Block-Armen; `throw` als
  Ausdruck; kein allgemeiner Block-Ausdruck; `comptime e` Präfix; Lambdas: Paren
  `(n: int) => …`, bare `x => …`, Trailing `run { it * 2 }` mit implizitem `it` — die letzten
  zwei stehen in keinem Guide-Kapitel; Trailing-Block mit `;` hat keinen Tail; kein `throws`,
  kein Default, kein `params`, keine Rekursion, keine lokale `fn`; `this`/`break`/Feldzugriff
  im Lambda sind ICEs, `yield` eine Laufzeitpanik; Methodenwert `obj.method` in vier
  Schreibweisen vier Fehler; `a < b < c` ist ein Typfehler ohne Hinweis.
- **Statements**: `;`-terminiert; `if (…) { }` mit Pflichtklammern und Pflichtblöcken; `while`,
  `do … while`, `for (x in xs)`, `for (i in a..b)`; `if let`/`while let`/`let … else`; Labels
  `L: while`; `break`/`continue` ohne Wert; `defer` Block oder Ausdruck; `try { } catch (e: T) { }`.
- **Patterns**: Literale (negative im Parser, nicht in der Grammatik), Ranges, Or, Bindung,
  `_`, Varianten `Sig.Red`/`Red(x)`, Feld-Patterns mit Test, Tupel, Arrays mit Rest; **ein
  bloßer Name ist Variante, wenn der Scrutinee-Typ eine hat, sonst Bindung** — ungeschrieben,
  typgerichtet, und **ein vertippter Variantenname wird still ein Catch-all** (`Yelow =>`);
  Typargumente im Pattern-Pfad versprochen, nicht geparst; unerreichbarer Arm normiert „kein
  Fehler".
- **Literale**: Ganzzahl mit `_`, Hex/Bin/Okt, Suffixe; Float mit Exponent; `char` — ein
  astrales Zeichen ist nur als Escape schreibbar (Lexer zählt UTF-16); `\xNN` ist ein
  Code-Punkt, kein Byte; Strings mit `+`/`*`; **f-Strings mit zwei Formatsprachen** (Zahlen:
  .NET-Spec oder Breite; `char`/`bool`/`string`: nur Breite — `{s:>8}` paniert); keine Raw-,
  keine Mehrzeilen-, keine Byte-Strings; `///`-Doku gebunden.
- **Format**: `lyric fmt` schreibt die eine Form; 4 Spaces; `@[…]` Normalform.

## Y1 — Vokabular: **entschieden** (2026-09-29)

**Prinzip**: reserviert nur, wo sonst ein Bezeichner stehen könnte; kontextuell, wo keiner
stehen kann. `quote {` und `comptime {` sähen aus wie ein Trailing-Lambda-Aufruf → reserviert.

| | Wörter |
|---|---|
| **Reserviert** (31) | `import as pub internal private static struct class enum interface extend fn mut let var if else while do for in is match loop break continue return yield defer using try catch throw quote comptime true false null this` |
| **Entfällt** | `module` (M2), `resume` (N2: `co.next()`), `params` (→ `...`), `opaque` (T15) |
| **Kontextuell** | `type`, `throws`, `extern`, `sealed` (vor `interface`), `by` (Konformanzliste), `with` (nach Ausdruck), `inline` (vor `fn`), `macro` (vor Namen) |
| **Builtin-Typnamen** (Bezeichner, nicht deklarierbar, K5) | `int uint float int8…int64 uint8…uint64 float32 float64 bool char string void never Self` |
| **Bibliothek, keine Wörter** | `wait`, `await`, `spawn`, `select`, `panic`, `assert`, `new`, `main`, `it` |
| **Bewusst nicht** | `const`, `async`/`await`, `unsafe`, `where`, `override`/`abstract`/`super`/`virtual`, `impl`, `switch`/`case`/`goto`/`finally`, `ref`, `inout`, `params` |

**Zeichen statt Wörter** (Maintainer): **`x: &T` + Aufruf `&x`** statt `inout` (C++/Rust/C-Gewohnheit;
Präfix-`&` war frei) · **`nums: int...`** statt `params` (Java/Go) · **`throws [A, B]`** — die Liste in
eckigen Klammern, **wie jede andere Mehrfachliste** (`:: [I, J]`, `@[A, B]`, `<T :: [I]>`); **`::`
bleibt** (keine `:`-Form für Konformanzen/Constraints). Verworfen: `f()?` für `try` (Zeichen an
`?.` vergeben), `!` für `never`, Zig-`E!T`, Nim-`*` für Export.

**`inline fn`** (Semantik: Lambda-Argumente eingesetzt, keine Closure, nicht-lokales `return`;
Kotlin) ist **nicht** der Optimierungshinweis — der heißt **`@AlwaysInline`/`@NoInline`/`@Cold`**
(A11 korrigiert).

**Kein `pub(read)`**: `pub var` ist außen les- und schreibbar, `pub` (unveränderlich) lesbar;
„innen schreibbar, außen lesbar" ist ein Getter (Rust, Go). Property-Zucker: Y9-Tür.

## Y3 — Mutabilitätswörter: **entschieden** (2026-09-29)

`let`/`var` an Bindungen und Feldern (M2); **`mut fn`** bleibt der Methodenmarker (Swift
`mutating` verworfen: länger, nichts gewonnen); **`&T`-Parameter** mit **`&x`** an der
Aufrufstelle (Y1); keine `ref`-Locals, keine `ref`-Rückgabe (T12 Tür).

## Y2 — Deklarationssyntax: **offen**
## Y3 — Bindungs- und Mutabilitätswörter (`mut`, `inout`/`ref`, `&`): **offen**
## Y4 — Ausdrucksformen und Präzedenz: **entschieden** (2026-09-29)

| # | Operatoren | Assoz. | Bemerkung |
|---|---|---|---|
| 1 | Postfix `.` `?.` `[ ]` `( )` `!` `++` `--` `with { }` | links | `with` auf Postfix-Stufe (M6); Makro-`!` gehört zum Namen |
| 2 | Präfix `!` `-` `~` `++` `--` `&` `comptime` `try` `try?` `try!` `throw` | rechts | `&x` nur in Argumentposition; `try` deckt den ganzen Ausdruck rechts (`try a + b` = `try (a + b)`) |
| 3 | `as` | links | über `*` |
| 4 | `*` `/` `%` `*%` | links | **Wrap-Operatoren in Zig-Schreibweise `+%` `-%` `*%`** (Swift-`&+` kollidiert mit Präfix-`&`); **nur auf Ganzzahltypen**, kein Interface, kein Default (ein Default `addWrap = add` wäre eine Lüge — Wickeln folgt nicht aus Addieren; `WrappingAdd :: [Add]` als additive Tür, Rust-`num-traits`-Form) |
| 5 | `+` `-` `+%` `-%` | links | |
| 6 | `<<` `>>` | links | |
| 7 | `..` `..=` | nicht-assoz. | Werte (T13); `0..n+1` = `0..(n+1)` |
| 8–10 | `&` `^` `\|` | links | |
| 11 | `<` `<=` `>` `>=` **`is`** **`in`** **`!in`** | **nicht-assoz.** | `a < b < c` ist ein **Parsefehler mit Hinweis** („meinst du `a < b && b < c`?") statt des heutigen Typfehlers; `is`/`in` auf Vergleichsstufe (Kotlin) |
| 12 | `==` `!=` | **nicht-assoz.** | dito |
| 13–14 | `&&` `\|\|` | links | |
| 15 | `??` | rechts | |
| 16 | `=` und Compound | rechts | Ausdruck (S8) |

| Form | Regel |
|---|---|
| `if (c) a else b` / `if (c) { … } else { … }` | beide als Wert (S1); `else` Pflicht; `else if` kettet |
| `match (e) { p => v, p => { … v } }` | bleibt; Kontexttyp in die Arme (T8) |
| `loop { … break v; }` | Wert = `break`-Wert; ohne `break value` Typ `never` |
| `try f()`, `try? f()`, `try! f()` | Präfix (E4) |
| **`try expr catch (e: E) expr \| { … }`** | als Ausdruck, Klauseln kettbar; ein `catch` bindet an das nächste `try` links — eigene Form |
| Value-Block `{ …; v }` | nur als Arm-, Lambda-, `if`-Ausdrucks- und `??`-Rechtsseite; **kein allgemeiner Block-Ausdruck** (`{` hätte eine dritte Bedeutung neben Initializer und Trailing-Lambda) |
| `throw e`, `comptime e`/`{ }`, `p with { }`, `f(host: "h")`, Trailing-Lambda, `P { x = 1 }` (nie an Statement-Anfang), `(a, b)`, `[a, b]`, `[x] * n`, `a..b`, `x!` (`x!!` = zwei Postfixe) | wie entschieden |

## Y5 — Statement-Formen, Klammern, Semikolons: **entschieden** (2026-09-29)

**Klammern um Kontrollköpfe bleiben** (`if (c) { … }`; C#, Kotlin, Zig, Lyric 4): sie
begrenzen den Kopf, ohne die Rust-Sonderregel „kein Struct-Literal im Kopf"; Lyric-Script
bleibt ebenso. **`;` bleibt Pflicht**: es trägt Semantik (`x` Tail gegen `x;` Statement — Rusts
Grund), und Zeilenumbruch-Regeln wären Grammatikkomplexität für jedes Werkzeug inkl.
Makro-`quote`; JS-ASI verworfen.

| # | Regel | Vorbild |
|---|---|---|
| S1 | **Statement-Rümpfe immer geklammert** — kein `if (c) x;`. Der **`if`-Ausdruck** dagegen erlaubt beide Seitenformen: `if (c) a else b` (ohne Klammern, `else` Pflicht) und `if (c) { … a } else { … b }` mit Value-Blöcken (heute fünf Diagnosen) — kein `?:` | Go, Rust; Y4 |
| S2 | `if (…) { } else if … else { }`; `if (let p = e) { }`; **`let p = e else { … };`** — widerlegbare Bindung: `else` läuft, wenn das Pattern nicht passt, und muss den Scope verlassen (Compiler prüft). **Für Optionals ist `??` das Idiom** (`let n = parse(s) ?? throw …;`, `?? { return 0; }` über `never`); `let … else` bleibt für Gestalt (`let Circle(r) = shape else { … };`) | Swift `guard`, Rust `let-else` |
| S3 | `while (c) { }`, `do { } while (c);`, **`loop { }`**, `for (x in xs)`, `for (i in a..b)`, `for ((k, v) in map)`; kein `for (;;)`, kein `switch` | — |
| S4 | Labels `outer: loop { … break outer; }` (4.x-Form, kein `'outer`); `break value` nur aus `loop`; `break outer value` erlaubt | Rust (Wert) |
| S5 | **`using let f = open(p);`** — Wort vor `let`; `using var` ist ein Fehler | C# `using var` |
| S6 | `defer expr;` / `defer { … }`; `return`/`break` im `defer`-Rumpf ist ein Übersetzungsfehler (heute Compiler-Stack-Overflow) | Go |
| S7 | `try { } catch (e: T) { }` bleibt; `catch (e: A, B)` (E9) | — |
| S8 | **Zuweisung bleibt Ausdruck** (Maintainer): Wert = neuer Wert des Ziels, Typ = Typ des Ziels, rechtsassoziativ, für `=` und Compound (`o ??= 3` liefert `?T`) — die gemessene Wirklichkeit als Spec-Zeile; **Zuweisung direkt als Bedingung → Warnung** „`==` gemeint?", Doppelklammer schaltet sie ab; Präzedenz niedrigste, unter `??` | C#; Clang `-Wparentheses` |
| S9 | `match (e) { … }`; Ausdrucksarm endet mit `,`, Blockarm darf es weglassen (beide erlaubt; Formatter schreibt bei Blöcken keins) | Rust |
| S10 | Leeres `;` erlaubt, Formatter entfernt es | — |

## Y6 — Patterns: **offen**
## Y7 — Literale und Formatsprache: **offen**
## Y8 — Kommentare und Dokumentation: **offen**
## Y9 — Zucker: implizites Member, Feldkurzform, Aufrufsyntax, `^n`, `!in`: **offen**
## Y10 — Formatierung und Stil: **offen**
## Y11 — Lambdas: Formen, Rumpfregeln, `inline`: **offen**
## Y12 — Makro- und `comptime`-Syntax: **offen**
