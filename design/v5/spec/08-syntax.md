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

## Y2 — Deklarationssyntax: **entschieden** (2026-09-29)

| # | Regel |
|---|---|
| D1 | **Feldtrenner `,`** bleibt (Felder und Member in einer Liste; ein selbstschließender Member darf es weglassen) |
| D2 | **Enum-Methoden nach `;`** bleiben (`enum E { A, B; fn f() { } }`); `extend E` ebenso möglich |
| D3 | Sichtbarkeitswort vor der Deklaration: `pub fn`, `internal struct`, `private let`; Reihenfolge `pub static fn`, `pub mut fn` |
| D4 | `var`-Feld: `pub var count: int,`; ohne `var` unveränderlich (M2) |
| D5/D6 | `struct S :: [I, J]`, `<T :: [I, Iterator<Item = int>]>` bleiben (Y1) |
| D7 | `class Dog :: [Walker by legs]` — `by` im Listeneintrag |
| D8 | **`sealed interface Shape { }`** — geschlossene Konformermenge im Paket: `match` über Typ-Patterns erschöpfend ohne `_`, ein neuer Konformer macht jedes `match` ohne ihn zum Fehler, fremde Pakete konformieren nicht, der Compiler darf `switch` über Deskriptoren emittieren. Gegen Enum: eigenständige Typen mit eigenen Membern (Kotlin, Java 17) |
| D9 | `fn f(): int throws [IoError, ParseError]`; ein Typ auch ohne Klammern; bar = `Error` |
| D10 | Interface: `type Item;` / `type Out = Self;`, `static fn parse(s: string): ?Self;`, `static let ZERO: Self;`, Default-Rümpfe, `private fn` Helfer |
| D11 | **Koroutine nur über den Rückgabetyp** `Coroutine<int>` + `yield` — keine Signaturmarkierung (keine Färbung) |
| D12 | `extend T { }`, `extend T :: [I] { }`, `extend<T :: [I]> T[] { }`, `private extend T { }` |
| D13 | `macro Name(target: StructDecl, n: Expr): Decl { … }` (Y12) |
| D14 | `type Pair<T> = (T, T);`; kein `opaque` |
| D15 | `extern "C" fn strlen(s: CStr): uint = "strlen";`; kein `"dotnet"`; `@Export("name")` (Bereich 11) |
| D16 | `@[…]` vor der Deklaration; gestapelte `@A` → Gruppe (Formatter) |
| D17 | `struct Pair<T :: [Equatable]> :: [Equatable] { … }`, `<Rhs = Self>`, `_` |
| D18 | `fn main(): void \| int [throws …]` |

Gesamtform an einem Beispiel (Guide-Kapitel 1 übernimmt es):

```
@[Route { path = "/x" }]
pub sealed interface Shape :: [Display] {
    type Unit = float;
    static fn origin(): Self;
    fn area(): Unit;
    fn describe(): string { return f"area {this.area()}"; }
    private fn fmt(v: float): string { … }
}

pub struct Circle :: [Shape, Equatable, Hashable] {
    r: float,
    pub var tag: string,
    static let UNIT: Circle = Circle { r = 1.0, tag = "u" };
    static fn origin(): Circle { return Circle.UNIT; }
    fn area(): float { return 3.14159 * this.r * this.r; }
    mut fn scale(f: float): void { this.tag = f"{this.tag}*{f}"; }
}

class Dog :: [Walker by legs] { legs: Legs, name: string, }

enum Json { Null, Bool(bool), Num(float), Arr(Json[]), Obj(Map<string, Json>);
    fn isNull(): bool { return match (this) { .Null => true, _ => false }; }
}

pub fn parse<T :: [Parse]>(s: str): T throws [ParseError] { return try T.parse(s); }
fn swap<T>(a: &T, b: &T): void { let t = a; a = b; b = t; }
fn sum(nums: int...): int { … }
inline fn each<T>(xs: T[], f: fn(T) -> void): void { for (x in xs) { f(x); } }
macro Builder(target: StructDecl): Decl { return quote { … }; }
type Handler = fn(Event) -> void throws [IoError];
extend<T :: [Display]> T[] :: [Display] { fn show(): string { … } }
```

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

## Y6 — Patterns: **entschieden** (2026-09-29)

**Bloße Namen** (der Catch-all-Befund): **ein bloßer Name ist immer eine Bindung**; Varianten
schreibt man `.Red` (implizites Member, T9) oder `Signal.Red`. **Eine Bindung, die wie eine
Variante des Scrutinee-Typs heißt, ist ein Fehler** („meinst du `.Red`?" — fängt Migration und
Tippfehler; Rust `bindings_with_variant_name`); **ein unerreichbarer Arm ist eine Warnung**
(Spec-Satz „not an error" bleibt; Rust `unreachable_patterns`). `Yelow =>` bekommt damit zwei
Meldungen und keinen stillen Weg.

| Form | Beispiel | Regel |
|---|---|---|
| Literal | `0`, `-5`, `'a'`, `"put"`, `1.5`, `true`, `null` | negative Literale in die Grammatik; `NaN` matcht nie |
| Range | `0..5`, `5..=9`, `'a'..='z'`, `-5..=-1` | bleibt |
| Bindung | `n`, `_` | `let`/`var` vor dem Pattern entscheidet die Mutabilität aller Bindungen; Bindungen kopieren (M3) |
| Variante | `.Red`, `.Num(v)`, `.Rect { w = 0, h }` | Feld-Pattern mit **`=`** als Test (Initializer-Form; Rust `:` verworfen); weggelassene Felder erlaubt |
| **Typ-Pattern** | `c: Circle =>`, `_: Circle =>` | T11; Form wie `catch (e: T)` |
| **Typmengen-Pattern** | **`catch (e in [IoError, ParseError])`**, **`s in [Circle, Rect] =>`** im `match` | Maintainer: `in` + eckige Liste wie jede Mehrfachliste; in Klammern stehen **Typen** (Pattern-Kontext, nicht der `Contains`-Operator — `if (x in [1, 2])` bleibt der Operator auf Werten); die Bindung trägt die Menge (K7); korrigiert E9 C5 |
| Tupel, Struct, Array | `(a, _)`, `P { x, y }`, `[first, ..rest]`, `[a, b, ..]` | bleiben; `..rest` bindet einen View (T13) |
| Or, Guard | `.Red \| .Yellow`, `A(x) \| B(x)`, `.Num(n) if n > 0` | bleiben; Guard zählt nicht zur Erschöpfung |
| Typargumente im Pattern-Pfad | `Opt<int>.Some(x)` | **entfällt** — mit `.Some(x)` und Inferenz nie nötig |

**Erschöpfung**: über Enum-Varianten, `bool`, `?T`, `sealed`-Interfaces, Tupel/Structs durch
alle Spalten, **Array-Längenklassen** (die Implementierung war der Spec voraus, jetzt Regel);
offene Typen brauchen `_`; `@NonExhaustive` verlangt `_` außerhalb des Pakets; irrefutables
`if let` warnt.

## Y7 — Literale und Formatsprache: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| L1 | Ganzzahlen `1_000_000`, `0xFF`, `0b1010`, `0o755`, Suffixe; Floats mit Exponent, `f32` (T1b) | Rust |
| L2 | Führendes `-` **direkt** vor dem Literal gehört dazu; **nicht durch Klammern** (`-(9223372036854775808)` ist Überlauf) | — |
| L3 | **`'😀'` ist ein gültiges `char`-Literal** (Lexer zählt Skalarwerte — 4.x-Bug) | — |
| L4 | Escapes `\n \r \t \\ \' \" \0 \u{…}`; **`\xNN` nur in Byte-Strings** (in `string`/`char` entfällt es) | Rust (strenger) |
| L5 | **Raw** `r"…"`, `r#"…"#` (Regex, Pfade, SQL) | Rust |
| L6 | **Mehrzeilig** `"""…"""` mit Abzug der gemeinsamen Einrückung (Basis: Einrückung des schließenden `"""`); `r"""…"""` roh | Swift, Kotlin, Java |
| L7 | **Byte-Strings** `b"…"` → `uint8[]`; nur ASCII und `\xNN` | Rust |
| L8 | f-Strings `f"…"`, Präfixe kombinierbar (`fr"…"`, `f"""…"""`); `+`/`*` auf Strings bleiben (D6) | Python |

**Eine Formatsprache** (heute zwei; `{s:>8}` paniert, `{3.7:C}` gibt `¤3.70`), fest in der
Spec, locale-frei, **zur Compile-Zeit geprüft**:

```
{ ausdruck [ : [[füll]ausrichtung] [vorzeichen] [#] [0] [breite] [gruppierung] [.präzision] [typ] ] }
```

Ausrichtung `< > ^` mit Füllzeichen; Vorzeichen `+`/`-`; `#` alternative Form (`{255:#x}` →
`0xff`); `0`-Auffüllung; Breite, Präzision; Gruppierung `,`/`_` (`{1234567:,}` → `1,234,567`);
Typ `b o x X e E f %` auf Zahlen, **`?` = Debug** auf allem (D7). Zahlen und `char`/`string`
verstehen alles Passende; ein **`Display`-Typ** bekommt nur Breite/Ausrichtung/Füllung auf sein
`show()`; ein Typ mit eigenen Specs konformiert **`Format { fn format(spec: str): string }`**.
Kein `C`/`N2`/`P1`, keine Kultur. `std.fmt.format("{} {0:>4}", …)` zur Laufzeit mit derselben
Sprache. (Python-Grammatik, Rust `{:?}`)

**Comprehensions** (Maintainer-Nachfrage): keine Syntax — Iterator-Ketten mit `it`-Trailing-
Lambdas (`xs.filter { it > 0 }.map { it * 2 }.toList()`, lazy bis zum Terminator) und
**`sequence { … yield … }`** (ein yieldendes Lambda ist ein Generator, Y11) für Verschachtelung
mit Statements. C#'s LINQ-Query-Syntax als Gegenbeispiel einer zweiten Schreibweise, die
niemand nutzt.

## Y8 — Kommentare und Dokumentation: **offen**
## Y9 — Zucker: **entschieden** (2026-09-29)

| Form | Regel | Aus |
|---|---|---|
| `.Red`, `.Num(v)` | implizites Member überall mit erwartetem Typ (Argument, Rückgabe, Zuweisung, `x == .Red`, Default, Pattern) | T9 |
| `S { v, w }` | Feldkurzform | M14 |
| **`Point(1, 2)`** | Typname in Aufrufposition ≡ `Point.new(1, 2)` — **`new` ist eine gewöhnliche statische Funktion** (Namenskonvention wie Rust `new`/Go `NewX`), Arität wählt (D4); ohne `new` Fehler mit Hinweis auf den Initializer; **keine Konstruktoren** (kein halbfertiges `this`, keine Verkettung; eine Fabrik darf `?T` liefern, werfen, ein bestehendes Objekt zurückgeben); anders benannte Fabriken ruft man beim Namen | D11 |
| `xs[^1]`, `xs[1..^1]` | nur in Klammern, über `length` | T14 |
| `x !in xs` | `!(x in xs)` | D6 |
| `?.` `??` `!` `??=` `&&=` `\|\|=` | bleiben | — |
| Ranges `a..b`, `a..=b`, `a..`, `..b`, `..` | Werte | T13 |
| `f(host: "h")` | benannte Argumente | D5 |
| **nachlaufende Kommata** | überall erlaubt | Rust, Go |
| Property-Zucker | Tür | Y1 |

## Y10 — Formatierung und Stil: **offen**
## Y11 — Lambdas: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| F1 | **Drei Formen**: Paren `(n: int): int throws [E] => …`, bare `x => x * 3`, Trailing `xs.map { it * 2 }` / `f(a) { … }` — alle drei in Guide-Kapitel 3 | Kotlin |
| F2 | **Mehrere Parameter im Trailing-Block**: `xs.fold(0) { acc, x => acc + x }`, Destructuring `{ (k, v) => v }`; ohne Parameterliste ist `it` der Parameter (heute: zwölf Diagnosen) | Kotlin |
| F3 | **Rumpf = Ausdruck oder Value-Block** (Statements + Tail) in jeder Form — die 4.x-Unterscheidung „Trailing-Block ohne Tail" fällt | — |
| F4 | `it` implizit; inneres `it` verdeckt äußeres **mit Warnung** | Kotlin (still) |
| F5 | **Rahmengebundene Konstrukte** (die vier ICEs): `return` verlässt das Lambda; `break`/`continue` nur für Schleifen *im* Lambda, sonst Fehler; `this` und impliziter Feldzugriff **erlaubt** (gefangen nach M8 C2); **`yield` im Lambda macht es zum Generator-Lambda** vom Typ `fn(…) -> Coroutine<Y>` — D11 auf Lambdas; `sequence { … yield … }` ist eine gewöhnliche Funktion darüber | Kotlin `sequence`, C# Iterator-Blöcke |
| F6 | **`inline fn`**: Funktionsparameter eingesetzt, Lambda keine Closure; `return` darin **nicht-lokal**, `break`/`continue` treffen umschließende Schleifen; das Lambda darf im Callee nicht gespeichert/zurückgegeben werden (Fehler); `noinline`-Marker: Tür | Kotlin |
| F7 | `throws` inferiert (K3), in der Paren-Form annotierbar | Swift |
| F8 | **Lokale `fn`** in Rümpfen, mit Capture — benannte Closures, Rekursion | Kotlin |
| F9 | keine Defaults, kein `...` in Lambdas (lokale `fn`) | — |
| F10 | `obj.method` gebundene Closure, `Type.staticFn` Funktionswert (T17) | Kotlin |
| F11 | Parametertypen aus dem Kontext (T8), sonst annotieren | — |

## Y12 — Makro- und `comptime`-Syntax: **offen**
