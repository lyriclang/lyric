# Lyric 5 — Gebiet: Enums und Pattern Matching

Basis: Arbeitsbaum `C:/Users/Olivier/CLionProjects/lyric`, HEAD `6f6f029f` (Merge `release/v4.6.0-cut`),
`Directory.Build.props:31` sagt `4.6.0`. Normative Spec: `C:/Users/Olivier/CLionProjects/lyric-spec/spec/`
(im Folgenden `spec/…`). Alle Messungen mit den vorgebauten Debug-Binaries (`lyrc.dll` / `lyrvm.dll`).
Proben unter `…/scratchpad/v5-design/probes/`:
`enums-patterns/` (`p01`…`p47`), `rev/` (`r01`…`r47`), `rev2/` (`r50`…`r53b`),
`enums-patterns-audit/` (`a01`…`b26`, die Proben des Kritikers, hier nicht übernommen, sondern
nachgemessen als) `enums-patterns-rev3/` (`q01`…`q11`, dritter Revisionslauf).

Belegarten: **gemessen** = Probe kompiliert und gelaufen · **gelesen** = Pfad:Zeile · **behauptet** = geraten.
Aussagen über Vergleichssprachen sind durchgehend **behauptet** (Diagnosekataloge, Proposals, in
dieser Sitzung per Web-Suche nachgeschlagen, nicht ausgeführt); wo eine Quelle nachgeschlagen wurde,
steht sie dabei.

> **Zur Zeilentreue.** `STATUS.md` ändert sich fast täglich; jedes Zitat daraus nennt zusätzlich
> seinen Ankertext. Heute: Enum-Werfbarkeit `STATUS.md:2290`, „A value carries no type tag" `:2329`,
> „four ways to ask 'is this null?'" `:2212` (gemessen per `grep -n`). `docs/Grammar.md`- und
> `spec/`-Zeilen sind in dieser Sitzung geprüft.

---

## 1. Ist-Stand

### 1.1 Was da ist und funktioniert (alles gemessen)

| Form | Probe | Ergebnis |
|---|---|---|
| Tupel- und Struct-Varianten, Methoden nach `;` | `p01` | läuft; `Grammar.md:256-263` |
| Beliebig geschachtelte Varianten-Patterns `Neg(Neg(x))` | `p02` | läuft (`7`, `5`) |
| Tupel-Scrutinee mit Varianten darin `(Idle, Dial(n))` | `p03` | läuft |
| Bindendes Or-Pattern `A(x) \| B(x)` | `p04` | läuft (`3 4 -1`); Spec `spec/07-statements.md:176-181` |
| Or-Pattern **geschachtelt im Payload** `Add(Lit(0) \| Lit(1), r)` | `p21` | läuft (`100 2`) |
| Feld-Pattern mit Test `Rect { w = 0, h }` | `p05` | läuft (`1 2`); Spec `:161-166` und `:176-178` |
| Array-Patterns `[]`, `[x]`, `[a,b]`, `[first, .., last]` | `p06` | läuft (`0 5 3 103`) |
| **Benannter Rest** `[first, ..rest]`, `rest.length` | `p06b`, `r21` | läuft — **Kopie**, und die Kopie ist schreibbar |
| Array-Pattern mit Varianten darin `[Word("add"), Num(a), Num(b)]` | `p37b` | läuft (`5 9`) |
| Erschöpfung über Längenklassen `[] \| [x] \| [a, ..]` | `p38`, `r51` | erschöpfend ohne `_` — **über die Spec hinaus**, siehe §1.2 (f) |
| Erschöpfung **durch ein Tupel hindurch**, eine testende Spalte | `r38`, `r40` | läuft — **über die Spec hinaus**, siehe §1.2 (f) |
| Zeugen-Diagnose `no arm matches 'Some(false)'` | `p07` | SEM0050 mit Zeuge |
| Zeuge `'null'` über `?E` | `p26` | SEM0050; Spec `:183-187` |
| `if let` / `while let` / `let … else` | `p09` | läuft (`r=5 / n=7 / r2=5 / v=3`) |
| **Warnung auf irrefutables `if let`** (`LYR-SEM0104`) | `r34` | `this pattern matches every 'int' — the 'if let' never fails` |
| Range-Patterns, exklusiv und inklusiv, auch `char` | `p13` | `0..5`, `5..=9`, `'a'..='z'` |
| **Negative** Literal- und Range-Patterns `-5..=-1`, `-7` | `q01` | läuft (`1 2 3`) — **nicht in der Grammatik**, siehe §1.2 (d) |
| Range- und Literal-Patterns über `float` | `r31`, `r36` | laufen; `Grammar.md:560,563` erlaubt jedes `Literal` |
| String-Literale und String-Or `"put" \| "post"` | `p23b`, `q10` | läuft (`1 2 0`; `3 0`) — ein `eq` pro Arm, siehe EP-45 |
| `match` als Ausdruck: `true => 1, false => null` ist `?int` | `q03` | läuft (`1`); Spec `spec/06-operators.md:130-139` |
| `match` als Ausdruck: `throw`-Arm zählt nicht mit | `q03c` | läuft (`1`); `TypeChecker.cs:5074-5076` |
| `match` als Ausdruck: Kontexttyp `let r: ?int = match …` | `q03e` | läuft (`2`); Spec `:136-139` |
| Guards, Armreihenfolge = Testreihenfolge | `p28` | `A(x) if x>10` fällt korrekt durch |
| Guard zählt **nicht** zur Erschöpfung | `p18`, `p35` | SEM0050; Spec `:191-192` |
| Struct-/Klassen-Destrukturierung im `match` | `p44` | läuft |
| Struct-Pattern im Enum-Payload `Wrap(P { x, y })` | `p34` | läuft (`7`) |
| Tupel-Pattern als Lambda-Parameter `((a,b)) => …` | `p32` | läuft (`7`) |
| Doppelte Bindung im Pattern → LYR-SEM0097 | `p16` | Fehler, korrekt; Spec `:169-174` |
| Or-Alternativen mit ungleichen Bindungen → LYR-SEM0032 | `p22`, `q04`, `q04b` | Fehler, korrekt — auch `int` vs `?int` (`q04b`) |
| Ungleiche Arm-Typen → LYR-SEM0016 | `q03b`, `q03d` | `int` vs `string`; **auch `int8` vs `int`** |
| Qualifiziertes Varianten-Pattern `Signal.Red`, auch im Or | `r03`, `r27` | läuft; Tippfehler ist `LYR-SEM0031` |
| `@NonExhaustive` aus `std.core` ist **deklariert und anwendbar** | `r46` | kompiliert und läuft (`1`) |
| `?T` narrowt am Kopf, nicht im Payload | `p17`, `q05c` | `n => n+1` liefert `5`; `null => 0, n => n + 1` druckt `0 5` |
| Pattern-Bindung im `match`-Arm ist unveränderlich (LYR-SEM0019) | `p29` | Fehler — aber `var P { x, y } = …` läuft (`p47`), siehe EP-16 |
| Enum-Wert: kein Feldzugriff, keine Mutation | `q09b` | `s.w = 2` ist `LYR-SEM0012: 'S' has no member 'w'` |

Damit ist die Liste aus `design/patterns.md` §1 (zwölf Defekte) abgearbeitet, und §3.3 dort ist
überholt: der **benannte Rest ist gebaut**, nicht mehr `LYR-IR0001`.

### 1.2 Wo Spezifikation und Implementierung auseinandergehen

**(a) Der Guide beschreibt eine Regel, die nicht mehr gilt.**
`docs/guide/06-enums-and-matching.md:131` (Anker: *„A field pattern only **binds**"*) sagt, ein
Test im Feld-Pattern werde zurückgewiesen. Gemessen (`p05`): `Rect { w = 0, h }` kompiliert und
wählt den richtigen Arm — und die Spec sagt es seit 4.5 selbst (`spec/07-statements.md:176-178`).
Kapitel 06 kennt außerdem **weder Array-Patterns noch `if let`/`while let`/`let … else`** — beides
steht in `Grammar.md:371-382` und läuft. Das Weglassen von Feldern ist dagegen **dokumentiert**
(`:127-129`, *„leave a field out entirely if you do not want it — it is not read"*) und normiert
(`spec/07-statements.md:164-165`).

**(b) Die Grammatik verspricht Typargumente im Pattern-Pfad, der Parser kann sie nicht.**
`Grammar.md:548` (`TypePath [ '(' … ')' ]`), `:524` (`Opt<int>.Some`), `:527-538` (§6.3 mit
Folgetoken `.`, Beispiel `Pair<int>.of(3)`). Gemessen (`r41`): `Opt<int>.Some(x)` im Arm ist
`LYR-PAR0002 … got Less` plus `PAR0034`. Kontrolle (`p20b`): ohne Typargumente läuft es. Siehe EP-14.

**(c) `SPEC-RUNDE.md:186-189` ist überholt.** „Erschöpfung durch ein Tupel mit Enum darin sei
`SEM0050`" — gemessen `r38`/`r40`: kompiliert und druckt `3`.

**(d) Negative Literale: die Grammatik hat keine, der Parser schon.** *Neu.* Gelesen
`Grammar.md:92-100`: `IntLit`/`FloatLit` tragen **kein Vorzeichen**; `:560` `RangePattern = Literal
… Literal`; `:563` „`Literal` is an integer, float, string, char, bool or null literal". Gelesen
`src/Lyric.Frontend/Parsing/Parser.Patterns.cs:46` (`case TokenKind.Minus: // negatives
numerisches Literal`) und `:73-90` (`ParsePatternLiteral`: ein `-` vor einem **Zahl**-Literal wird
akzeptiert, vor allem anderen `LYR-PAR0033`). Gemessen `q01`: `-5..=-1 => 1, -7 => 2, _ => 3`
druckt `1 2 3`. Der Parser ist hier vernünftiger als die Grammatik; die Grammatik muss nachziehen
— oder der Parser zurück. Siehe EP-37. `spec/07-statements.md:150` sagt nur „literals (integer, …)".

**(e) Feld-Defaults in Struct-Varianten: Grammatik ja, Compiler nein.** *Neu.* Gelesen
`Grammar.md:263` `StructVariant = '{' Field { ',' Field } [ ',' ] '}'` mit `Field = IDENTIFIER
':' TypeExpr [ '=' Expr ]` (`:237`) — ein Default ist grammatisch erlaubt. Gemessen `q07`:
`enum S { R { w: int = 0, h: int }, D }` ist `LYR-IR0001: a field default — this compiler version
cannot lower it yet`, **an der Deklaration**. Kontrolle `q07b`: auch wenn der Initializer beide
Felder angibt (`S.R { w = 1, h = 2 }`), fällt derselbe Fehler — der Default selbst ist das
Problem, nicht sein Gebrauch. Siehe EP-38.

**(f) Die Erschöpfungsprüfung reicht weiter als die Spec verspricht.** *Neu — und ein
spec-first-Posten, den die zweite Fassung übersehen hat, obwohl sie spec-first fordert.* Gelesen
`spec/07-statements.md:189-192`: *„Exhaustiveness is checked where the scrutinee is enumerable —
enum variants, `bool`, and the two states of a `?T` … Open types (`int`, `string`, …) require a `_`
or binding arm."* Gemessen `p38`/`r51` (Array-Längenklassen) und `r38`/`r40` (Einspalten-Tupel):
beide werden **ohne `_`** als erschöpfend akzeptiert. Nach Spec ist `int[]` ein offener Typ mit
`_`-Pflicht. Die Implementierung ist der Spec voraus; ein konformer Zweitcompiler dürfte diese
Programme ablehnen. Siehe EP-39.

**(g) Die Auflösungsregel für bloße Namen im Pattern steht nirgends.** *Neu, Korrektur der
zweiten Fassung.* Die zweite Fassung zitierte „`Grammar.md:547-548` — ein bloßer `IDENTIFIER` ist
die Unit-Variante, wenn es eine gibt, sonst eine frische Bindung". Gelesen: `Grammar.md:547` listet
nur `| IDENTIFIER` als Alternative, `:562-568` sagt nichts zur Auflösung; `spec/07-statements.md:150-151`
sagt nur „bindings; enum variants". Die Regel steht **ausschließlich im Compiler**:
`TypeChecker.cs:5031-5033` (`IsIrrefutable`: `VariantOf(e, b.Name) is null` → Bindung, sonst
Test) und `:4928` (`NamesVariant`: `b.Name == variant && VariantOf(enumTs, b.Name) is not null`).
Sie ist **typgerichtet**, nicht scopegerichtet: der Scrutinee-Typ entscheidet, nicht der Import
(`p42c`: die Variante schlägt ein gleichnamiges Local). Und sie gilt **nur im Pattern**: gemessen
`q06`, `let s: Signal = Red;` ist `LYR-SEM0002: unknown identifier 'Red'`. Siehe EP-40, EP-01.

### 1.3 Wo Lyric 4 still falsch ist

**(1) Ein vertippter Variantenname wird stillschweigend ein Catch-all — ohne jede Diagnose.**
Gemessen, `p46`:

```lyr
enum Signal { Red, Yellow, Green }
fn f(s: Signal): int {
    return match (s) {
        Red   => 10,
        Yelow => code(Yelow) + 100,   // Tippfehler
        Green => 30,                  // ab hier tot
    };
}
```
`f(Signal.Green)` liefert **102** statt 30, **diagnosefrei**. Grund: die ungeschriebene Regel aus
§1.2 (g) — ein unbekannter Name ist eine Bindung, und eine Bindung deckt alles. Wird der Name im
Arm-Körper nicht benutzt, rettet einen zufällig `LYR-SEM0071` (`p24`, `2` statt `3`). Kontrolle
`p24b`: `3`. **Qualifiziert ist der Fall sauber**: `r03`, `Signal.Yelow` ist `LYR-SEM0031`.

**(2) Kein Diagnoseton für einen unerreichbaren Arm — und das ist so normiert.** Gemessen `p15`:
`A(_) => 1, A(0) => 2, _ => 3, B => 4` kompiliert warnungsfrei. *Korrektur der zweiten Fassung*:
das ist keine reine Implementierungslücke. Gelesen `spec/07-statements.md:158`: *„an arm made
unreachable by an earlier one is not an error"* — ein normativer Satz. Eine Warnung ist mit ihm
verträglich (er sagt „not an error"), ein Fehler (C#-Modell) widerspricht ihm und braucht einen
ADR. Siehe EP-02 und EP-41. — Der irrefutable Fall in `if let` hat bereits `LYR-SEM0104` (`r34`;
`TypeChecker.cs:1556`, `:1589`, über denselben `IsIrrefutable`-Test).

**(3) Mehrfeldrige Varianten sind nie erschöpfend beweisbar.** Gemessen `r19`: das vollständige
Kreuzprodukt über `enum S { Pair(bool,bool), Dot }` wird mit `no arm matches 'Pair(_, _)'`
abgelehnt; `r23` ebenso die Einspalten-Form; Kontrolle `r39`: `Pair(x,y) | Dot` läuft. Gelesen
`TypeChecker.cs:4915` (`TupleFields is { Length: 1 }`), begründet `:4806-4810`. Falsche
Zurückweisung, keine Diagnosetextfrage. Siehe EP-24.

**(4) Struct-Varianten sind von der Rekursion ganz ausgenommen.** `r13` (`Wrap(Col)`) läuft,
`r14` (`Wrap { c: Col }`, formgleich) ist `SEM0050`; Kontrolle `r24`. Rule 2 mitten im Gebiet.

**(5) Erschöpfung über Integer-Intervalle fehlt.** `p14`: `match (v: uint8) { 0..=255 => 1 }` ist
`SEM0050`. Über `bool` funktioniert es (`p14b`). Nach Spec `:190` ist das korrekt (offener Typ).

**(6) Die Längenschranke der Array-Erschöpfung erzeugt einen Miscompile.** Gelesen
`TypeChecker.cs:4970`: `n <= 64`. Gemessen `r50`: 65 Arme `[]`…`[_ ×64]` ohne `_` kompilieren
warnungsfrei; Länge 65 druckt `arm=64`. Grund `FunctionLowerer.cs:2443` („The last arm is not
tested"). Kontrolle `r51`. Siehe EP-28.

**(7) Zwei ICEs am leeren Enum.** `r08`/`p36`: `LYR-CLI0020: lowering: match expression produced
no value`; `p36c`: `ir-verifier … bb0: has no terminator`. Kontrolle `p36b`. Siehe EP-13.

**(8) Typ-Patterns auf Interface-Werten gibt es nicht — und die Spec verbietet den Downcast
ausdrücklich.** `p10d`: `LYR-SEM0029: pattern names 'Sq', but the matched value is 'Draw'`.
Gelesen `docs/Bytecode.md:854` = `spec/13-bytecode.md:864`: *„There is no downcast; an interface
value cannot be narrowed back to its class."* Siehe EP-05, EP-42.

**(9) `match` ohne Klammern zerfällt in elf Fehler.** `p12`. Recovery-Defekt, siehe EP-09.

**(10) Kein `==` auf Enums.** `p25`: `LYR-SEM0059`.

**(11) Kein Sprungtisch.** `FunctionLowerer.cs:2404-2407`; `docs/Bytecode.md:837`: *„`match` has no
opcode. It reads the tag with `enumtag` and branches on it"*. Gemessen `q10`: ein `match` über
`string` ist im IR eine Kette aus je einem `eq` pro Arm (`q10.ir:268-306`). Siehe EP-18, EP-45.

**(12) Sinnlose Range-Arme sind stumm — auch signiert und über `char`.** `r33`: `5..=1`, `7..7`
tot ohne Ton; `r32`: Überlappung stumm. *Neu* `q08`: `'z'..='a' => 1` und `-1..=-5 => 1` kompilieren
ebenso wortlos als tote Arme (druckt `2 2`). Siehe EP-23.

**(13) Ein Name im Pattern ist nie eine Konstante.** *Neu.* Gemessen `q02`: `let LIMIT = 5; match
(n) { LIMIT => 1, _ => 2 }` macht `LIMIT` **still zu einer frischen Bindung** — zwei `SEM0071`
(„'LIMIT' is never used" für das `let` **und** für das Pattern), `f(3)` und `f(5)` liefern beide
`1`. Dieselbe Klasse wie (1), mit einem lokalen Namen statt einem Variantennamen. Siehe EP-36.

**(14) Kein Pattern bindet den narrowten Wert eines `?T`-Payloads.** *Neu — Korrektur von EP-19
der zweiten Fassung.* Gemessen `q05`: `enum E { Has(?int), No }`, `Has(null) => 0, Has(n) => n`
ist `LYR-SEM0001: cannot assign '?int' to 'int'` — der `Has(null)`-Arm **existiert** (die zweite
Fassung behauptete das Gegenteil), aber der folgende `Has(n)` bindet weiterhin `?int`. Kontrolle
`q05b`: mit `n ?? -9` läuft es (`0 4 -2`). Kontrolle `q05c`: am Kopf narrowt `null => 0, n => n + 1`
(`0 5`). Siehe EP-35.

### 1.4 Was fehlt (Formen, die andere Sprachen haben)

| Fehlend | Beleg | Heute |
|---|---|---|
| `@`-Bindung `n @ 1..=9` | `p11` gemessen | `LYR-LEX0012 expected identifier after '@'` |
| Typ-Pattern auf Interface | `p10d` gemessen | `LYR-SEM0029`; Verbot `Bytecode.md:854` |
| `..` im Feld-Pattern `P { x, .. }` | `p33` gemessen | `PAR0026 expected field name in pattern` |
| `..` im Tupel-Pattern `(a, ..)` | `p45` gemessen | `SEM0029` + `PAR0033` |
| `let`-Ketten `if (let a = x && let b = y)` | `p31` gemessen | `PAR0002 expected an expression, got Let` |
| Irrefutable Patterns im `for`-Kopf | `p30` gemessen | `PAR0002`; `Grammar.md:382` |
| Präsenz-Pattern für `?T` im Payload (Dual zu `null`) | `q05` gemessen | fehlt; nur `??`/`!` |
| Konstanten-Pattern | `q02` gemessen | Name wird still Bindung |
| **Regel** hinter `@NonExhaustive` | `grep -rn NonExhaustive src/` null Treffer | Attribut vorhanden (`core.lyr:536`), Regel nicht |
| Enum-Reflexion (`variants()`, `fromName`) | `lyric-v5-features.md:47` (Punkt 15) | fehlt |
| Explizite Diskriminanten / Rohwerte | `Grammar.md:256-263` | fehlt |
| Attribut an einer Variante | `r47` gemessen | `PAR0026 … got AtIdentifier` |
| Sichtbarkeit an Variante oder Feld | `Grammar.md:237,261` | kein `pub` |
| Array-Pattern über `List<T>` | `r53` gemessen | `SEM0029`; Kontrolle `r53b` |
| Lokale Unterdrückung einer Warnung | `lyrc --help` gemessen | nur `--deny-warnings` |
| Varianten-Completion in Pattern-Position (`lyrls`) | `ScopeCompletion.cs:140-158` gelesen | nur Scope-Namen, siehe EP-44 |
| Benutzerdefinierte Patterns (Extraktoren) | — | fehlt, bewusst |

---

## 2. Sprachvergleich

| Frage | Rust | Swift | Scala 3 | OCaml | F# | Haskell | Kotlin | C# | **Lyric 4** |
|---|---|---|---|---|---|---|---|---|---|
| Geschlossene Summe | `enum` | `enum` | `enum`/sealed | Variante | DU | ADT | `sealed` | — | `enum` |
| `match` ist Ausdruck | ja | seit 5.9 | ja | ja | ja | ja | ja (`when`) | ja | **ja** (`spec/06-operators.md:130`) |
| Erschöpfung erzwungen | Fehler | Fehler | Warnung | Warnung (8) | Warnung (25) | Warnung | Fehler (Statements seit 1.7) | **nein** (Laufzeit) | **Fehler** |
| Zeuge in der Diagnose | ja | ja (Fix-it) | teilweise | ja | ja (FS0025) | ja | ja | — | **teilweise** |
| Unerreichbarer Arm | Warn-Lint | Warnung | Warnung | Warnung (11) | FS0026 | Warnung | — | **Fehler** (CS8120/CS8510) | **nein, normiert** (`spec/07:158`) |
| Geschachtelte Patterns | ja | ja | ja | ja | ja | ja | **nein** | ja | **ja** |
| Or-Pattern mit Bindung | ja | ja | **nein** | ja | ja | **nein** (GHC 9.12) | — | `or` ohne Bindung | **ja** |
| `@`-Bindung | `x @ p` | **nein** | `x @ p` | `(p as x)` | `p as x` | `x@p` | — | `p x` | **nein** |
| Konstanten im Pattern | `const` | Ausdrucks-Pattern via `~=` | stabile Bezeichner (Großbuchstabe/Backticks) | nein | Literale/aktive | nein | jeder Ausdruck | `const` | **nein** (still Bindung) |
| Array-/Slice-Pattern | ja (Slice) | **nein** | `List(a, b, rest*)` | Listen + Arrays | `[a; b]`, `h::t` | `(x:xs)` | — | ja (C# 11) | **ja (Kopie, nur `T[]`)** |
| Bereichs-Pattern | `1..=9`, `1..9` | `1...9` | nur Guard | nur `char` | nur Guard/aktiv | nur Guard | `in 1..9` | `> 5 and < 9` | **`1..9`, `1..=9`, `float`, negativ** |
| Leeres/umgedrehtes Range | Fehler (E0579) | Laufzeit-Trap | — | — | — | — | — | Fehler | **stumm** |
| Float-Literal-Pattern | **erlaubt, `==`-Semantik; NaN-Konstante Fehler** (Lint 2024 entfernt) | via `~=` | ja | ja | ja | ja | ja | ja (relational) | **erlaubt, unspezifiziert** |
| Typ-Pattern/Downcast | nur `dyn Any` | `case c as Circle` | `case c: Circle` | — | `:? T` | — | `is T` | `is T c` | **nein, verboten** (`Bytecode.md:854`) |
| Offene Enums | `#[non_exhaustive]` | `@frozen`-Modell | — | polymorphe Varianten | — | — | — | (immer offen) | **Attribut ja, Regel nein** |
| Rohwerte/Diskriminanten | ja (`A = 1`, `as i32`) | ja | `ordinal` | — | — | `fromEnum` | `ordinal` | ja | **nein** |
| Benutzerdefinierte Patterns | nein | `~=` **testet nur** | `unapply` | nein | aktive Patterns | View-Patterns | nein | `Deconstruct` | **nein** |
| Gleichheit geschenkt | `#[derive]` | synthetisiert | `case class` | strukturell | strukturell | `deriving Eq` | ja | ja | **nein** |
| Lokale Warnungsunterdrückung | `#[allow]` | — | `@nowarn` | `[@warning "-8"]` | `#nowarn` | `OPTIONS_GHC` | `@Suppress` | `#pragma warning` | **nein** |
| Sprungtisch | ja | ja | ja | ja | ja | ja | ja | ja | **nein** |

**Korrekturen gegenüber der zweiten Fassung** (alle behauptet, per Web-Suche nachgeschlagen):

1. **Rust hat Float-Patterns nie zum Fehler gemacht.** Es gab ab 2017 nur die
   Future-Incompat-Warnung `illegal_floating_point_literal_pattern` (rust-lang/rust#41620); sie
   wurde 2024 mit PR #116098 **entfernt**, Begründung im Lint-Katalog: *„no longer a warning, float
   patterns behave the same as `==`"*. Nur ein NaN-Konstanten-Pattern ist ein Fehler. Rust steht
   damit auf Option **B** von EP-22, nicht auf A. Die zweite Fassung („seit 2018 ein Fehler") war
   falsch und hatte EP-22 A damit ein Vorbild gegeben, das es nicht gibt.
2. **Swift, SE-0110**: landete in **Swift 4**, nicht 3; das Core Team hat die parenthesierte
   Tupel-Destrukturierung im Closure-Parameter `{ (a, b) in }` als Sonderfall wieder zugelassen
   (Forum „Addressing the SE-0110 usability regression in Swift 4", forums.swift.org/t/6147); nur
   die klammerlose Form blieb verboten. `for (a, b) in pairs` war nie betroffen. Swift trägt EP-12
   für **Tupel** — was Swift nicht hat, sind Struct-Patterns überhaupt.
3. **Kotlin 1.7** machte nicht-erschöpfende `when`-**Statements** über sealed/enum/Boolean vom
   Warning (1.6) zum Fehler (KT-47709). Die Meldung mit den fehlenden Zweigen gab es vorher schon.
   Die Versionsangabe „Zeuge seit 1.7" der zweiten Fassung war falsch, die Substanz richtig.
4. **rustc** hat keinen CLI-Flag `--pattern-complexity-limit`; die Schranke ist das instabile
   Crate-Attribut `#![pattern_complexity_limit = "N"]` (Rust Reference, „Limits"), Fehler „reached
   pattern complexity limit". Substanz (harte Schranke + eigene Diagnose) richtig.
5. **Roslyn**: ein wanduhrbasierter Abbruch in der Nullable-Analyse ist mir **nicht** belegbar;
   Roslyns Komplexitätsschutz (CS8078 „expression is too long or complex") ist stackbasiert und
   deterministisch. EP-28 Option C steht deshalb ohne Vorbild.
6. **Java** wirft seit 21 (JEP 441) bei einem erschöpfenden `switch` über ein zur Laufzeit
   gewachsenes Enum `MatchException`, nicht mehr `IncompatibleClassChangeError` (Java 14–20).
7. Weiterhin gültig aus der zweiten Fassung: Swift meldet einen unerreichbaren `case` als
   Warnung; C# als Fehler (CS8120/CS8510); F# FS0025 nennt einen Beispielwert; Haskells Or-Patterns
   (GHC 9.12) binden nicht; Swifts `~=` liefert `Bool`; Rust hat Rohwerte ohne `#[repr]`.

### Was jede Sprache konkret tut, und was sie dafür zahlt

**Rust** — das nächste Vorbild. Erschöpfung ist ein Fehler mit Zeugen-Pattern,
`unreachable_patterns` ein Warn-Lint, `bindings_with_variant_name` **deny-by-default** — für EP-01
der eigentliche Punkt: Rust lehnt den Fall ab, es warnt nicht nur. `const`-Namen sind Patterns
(EP-36); `#[non_exhaustive]` zwingt fremde Crates zum `_`-Arm; Slice-Patterns binden mit `rest @ ..`
ohne Kopie; `let … else` seit 1.65, `let`-Ketten seit Edition 2024, exklusive Ranges seit 1.80; ein
leeres Range ist E0579; **Float-Patterns sind erlaubt mit `==`-Semantik, ein NaN-Pattern ist ein
Fehler** (Korrektur 1). *Preis*: „match ergonomics" (RFC 2005) — existiert nur wegen des
Borrow-Checkers; für Lyric fällt er weg.

**Swift** — die reichste Ergonomie, die teuerste Mechanismenzahl. `if let`, `guard let`, `while
let`, `if case`, `guard case`, `for case`; **`case .red` mit führendem Punkt** — ein Tippfehler ist
sofort ein Fehler. `@unknown default`, synthetisierte Conformances, Ausdrucks-Patterns via `~=`
(Konstanten: EP-36). Tupel-Destrukturierung in Closure-Parametern (parenthesiert) und `for` ist
erlaubt (Korrektur 2). *Preis*: sechs Schreibweisen für dieselbe Frage.

**Scala 3** — `unapply` macht jedes Objekt zum Pattern; `case c: Circle` ist ein Typtest; stabile
Bezeichner (Großbuchstabe, Backticks) sind Konstanten-Patterns. *Preis*: Erschöpfung kapituliert
vor Extraktoren; Or-Patterns binden nicht; kein `..` für Produktfelder.

**OCaml** — sauberste Kernsprache: Or-Patterns mit identischen Bindungen, `as`, Listen/Arrays,
Zeugen in Warnung 8, unbenutzte Regel in Warnung 11, lokale Unterdrückung. *Preis*: Erschöpfung ist
Warnung; polymorphe Varianten sind ein zweites Summen-System.

**F#** — aktive Patterns (total: nimmt an der Erschöpfung teil), `[<RequireQualifiedAccess>]`
tötet die Tippfehlerklasse an der Wurzel. *Preis*: sieben Fälle pro Pattern, Erschöpfung Warnung.

**Haskell** — ADTs, Pattern-Guards, `as`, View-Patterns, Or-Patterns ohne Bindung. *Preis*:
Unvollständigkeit Warnung; Faulheit zwingt zur `~`/`!`-Entscheidung.

**Kotlin — die entgegengesetzte Entscheidung (1).** Keine algebraischen Daten; `when` über
`sealed` mit `is` + Smart Cast; jede Konstante ist ein `when`-Zweig. *Preis*: `when` sieht nicht in
Werte hinein.

**C# — die entgegengesetzte Entscheidung (2).** Reichstes Pattern-Vokabular über eine offene
Klassenwelt, `const`-Patterns, relationale Patterns über `double`. *Preis*: **Erschöpfung ist nicht
erzwingbar** (CS8509 warnt, `SwitchExpressionException` zur Laufzeit) — das Argument für den
geschlossenen Summentyp.

### Was davon zu Lyrics Charakter passt

- **Passt gut**: Rusts Zeugen-Diagnose, `unreachable_patterns`, `#[non_exhaustive]`, `@`,
  `let`-Ketten, E0579, `const`-Patterns, **Rusts `==`-Semantik für Float-Patterns mit
  NaN-Verbot**; C#' Strenge bei subsumierten Armen; F#' `RequireQualifiedAccess`-Idee; Swifts
  Punkt und synthetisierte Conformances; OCamls lokale Unterdrückung.
- **Passt nicht**: Extraktoren (EP-15), Swifts sechs `if case`-Formen, Kotlins/C#' Typtest auf
  beliebigen Werten (Lyric taggt Werte nicht — `STATUS.md:2329`), OCamls polymorphe Varianten.
- **Nur teilweise**: Rusts Slice-Patterns ohne Kopie (`lyric-v5-features.md:42`, Punkt 10, 💥).

---

## 3. Designfragen

### EP-01 — Wie wird ein Variantenname im Pattern von einer Bindung unterschieden?

**Heute**: die Regel steht **nur im Compiler** (`TypeChecker.cs:5031-5033`, `:4928`; §1.2 (g)),
nicht in `Grammar.md:547` und nicht in `spec/07-statements.md:150-151`. Sie ist typgerichtet: hat
der Scrutinee-Typ eine Variante dieses Namens, ist es ein Test, sonst eine Bindung. Gemessen `p46`:
ein Tippfehler liefert stillschweigend 102 statt 30. `p42c`: ein gleichnamiges Local verliert gegen
die Variante. `p24`: unbenutzt fängt zufällig `SEM0071`. Qualifiziert ist der Fall sauber (`r03`,
`SEM0031`). **Ausdrucksseite**: `let s: Signal = Red;` ist `SEM0002` (`q06`) — Pattern und Ausdruck
lösen denselben Namen verschieden auf (EP-40).

**Gemessene Bruchbreite** (`rev2/count2.py`, Kontrolle per grep): über **213** `.lyr` in
`examples/`, `stdlib/`, `tests/` (ohne `bin/`/`obj/`) gibt es **20** bare-PascalCase-Arme, alle 20
benennen eine bekannte Variante, null Bindungen. Mit `bin/`-Kopien: 863 Dateien, 124 Arme — gleiche
Schlussfolgerung, sechsfach aufgeblasen. Fremder Code: **behauptet**.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen, zwei Warn-Netze (Lint auf `PascalCase`-Bindung + EP-02) | Rust — dort aber deny-by-default | abschaltbar; keine lokale Ausnahme (EP-21) |
| B: führender Punkt `.Red` | Swift | neue Syntax; `Shape.Circle` und `.Circle` sind zwei Schreibweisen (Rule 2) |
| C: `PascalCase` im Pattern MUSS eine Variante sein | Rusts Lint als Regel | Konvention wird Sprachregel (`CONTRIBUTING.md:108` gilt nur für stdlib); `camelCase`-Varianten kollidieren weiter |
| D: immer qualifizieren | F# `RequireQualifiedAccess` | laut im `this`-Match; aber **schon gebaut** (`r03`) |
| **E: eine Bindung braucht `let`** — `let yelow => …` bindet, ein bloßer Name ist immer ein Variantenversuch (Fehler, wenn es keine gibt) | Swift `case let x`, C# `var x` | eine Grammatikzeile; keine zweite Schreibweise, keine Konvention; `LetPatternStmt` (`Grammar.md:372`) schreibt `let Pattern` schon. **Löst nebenbei EP-36**: ein bloßer Name ohne `let` kann dann eine Konstante sein |
| F: Qualifizierung nur ohne Signaturtyp | — | halbe Regel |

**Empfehlung: E, zusammen mit EP-40 (dieselbe Regel für beide Positionen) und der
Sofortmaßnahme.** E schließt die Klasse vollständig (auch `camelCase`-Varianten, auch die
Konstantenfrage aus `q02`) ohne zweite Schreibweise. **Was E allein nicht löst**: die
Ausdrucksseite (`q06`) — das ist EP-40. **Sofortmaßnahme** (keine Sprachänderung): gelesen
`src/Lyric.Frontend/Sema/NameSuggestion.cs` hängt an sechs Stellen (`TypeChecker.cs:1889, 3859,
4039, 4130, 4336, 4480`), nicht an SEM0031 im Pattern-Pfad (`:5297`) — eine Zeile.
**Vorfrage, spec-first**: die heutige Regel muss **zuerst aufgeschrieben** werden
(`spec/07-statements.md` §7.6), damit E eine Regel revidiert statt eine erfindet.
**Bruch**: E ist **minor** (null Zeilen im Repo). **4.x-Warnstufe**: ab 4.7 Warnung „`Yelow` names
no variant of `Signal` — write `let yelow` to bind, or fix the name", ab 5.0 Fehler.
**Hängt ab von**: EP-02, EP-21, EP-36, EP-40, Namensregel-Gebiet, Modul-Gebiet.

---

### EP-02 — Gibt es eine Diagnose für einen unerreichbaren Arm?

**Heute**: im `match` nein (`p15`, `p46`), und das ist **normiert**: `spec/07-statements.md:158`
*„an arm made unreachable by an earlier one is not an error"*. Für `if let` gibt es `SEM0104`
(`r34`; `TypeChecker.cs:1556/1589`), über `IsIrrefutable` (`:5021`), dasselbe Werkzeug wie
`MissingCases` (`:4818`).

**Die Frage zerfällt in zwei**: (a) ein nicht-letzter Arm ist **irrefutabel** (der `Yelow`-Fall;
der Test existiert, braucht keine Matrix); (b) ein Arm ist durch die **Summe** der vorherigen
gedeckt (`A(0)` nach `A(_)`) — braucht EP-03.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: (a) sofort als Warnung, (b) mit EP-03 — der Spec-Satz bleibt und wird um „but a warning" ergänzt | Rust, OCaml 11, F# FS0026, Swift | zwei Diagnosen; Guards ausnehmen; braucht EP-21 unter `--deny-warnings` |
| B: Fehler | C# (CS8120/CS8510) | **Spec-Bruch**: `spec/07:158` sagt „not an error" — ein ADR retiriert den Satz (EP-41); bricht defensives `_` |
| C: nichts | heute, spec-konform | die Klasse aus EP-01 bleibt unbemerkt |

**Empfehlung: A, mit (a) vorgezogen**, und der Spec-Satz wird **in derselben Änderung**
ergänzt, nicht stillschweigend überschrieben (EP-41). B ist nicht „verteidigbar", wie die zweite
Fassung schrieb, sondern ein Spec-Bruch, der einen ADR braucht — C# tut es, aber C# hat den Satz
nicht. **Bruch**: A nein; B minor + ADR. **Hängt ab von**: (a) EP-41, (b) EP-03; beide EP-21.

---

### EP-03 — Wie weit rechnet die Erschöpfungsprüfung?

**Heute**: eine **partielle Matrix** mit benannten Grenzen. Gelesen `TypeChecker.cs:4816-4975`:
Fälle für `Optional`, `bool`, `ArrayOf` (`MissingArrayCases`), `TupleOf` (`MissingTupleCases`, mit
Spaltenreduktion), Enums (`MissingVariants`). Gemessen trägt die einspaltige Rekursion durch Tupel
(`r38`, `r40`), `?T` (`p26`), `bool` (`p14b`), Array-Längen (`p38`, `r51`), einfeldrige Varianten
(`r13`). **Die Spec verspricht davon nur Enum, `bool`, `?T`** (`spec/07:189-192`; EP-39).

| Grenze | Beleg | Begründung im Code |
|---|---|---|
| Tupel mit ≥2 testenden Spalten → `_` | `:4883` | `:4853-4864` bewusste Soundness-Entscheidung |
| Varianten mit ≥2 Feldern nie beweisbar | `:4915`, `r19`/`r23` | `:4806-4810` |
| Struct-Varianten nicht rekursiert | `r14` vs `r13` | keine (EP-24) |
| Integer-/Char-Intervalle fehlen | `p14` | keine; spec-konform („open type") |
| Array-Längen nur bis 64 → stumm „erschöpfend" | `:4970`, `r50` | keine; **Miscompile** (EP-28) |
| `string`/`float` nur `_` | `:4842`, `r52` | „coverable only by a default" |

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: volle Nützlichkeitsmatrix (Maranget), die Spaltenreduktion verallgemeinern | Rust `rustc_pattern_analysis`, OCaml, Scala | < 300 Zeilen Sema (die Zerlegung existiert); exponentiell im Worst Case → EP-28 |
| B: nur Einzelgrenzen nachziehen | — | vier Sonderfälle statt eines Algorithmus |
| C: Erschöpfung zur Warnung | OCaml, F#, C# | verschenkt die Garantie |

**Empfehlung: A, in einem Zug, vor 5.0 — und die Spec (EP-39) zieht mit, weil sie heute weniger
verspricht, als gebaut ist.** Konstruktorenmenge: Varianten (alle Felder als Spalten), `bool`,
`null`/präsent, Tupel (alle Spalten), Array-Längen, Integer-/Char-Intervalle über die Typbreite;
`string` nur `_`; `float` nach EP-22 B ebenfalls nur `_` (NaN macht `float` nicht aufzählbar).
**Bruch**: nein für die Akzeptanz; EP-28 kann heute falsch akzeptierte Programme ablehnen.
**Hängt ab von**: Typen-Gebiet, EP-22, EP-24, EP-28, EP-39.

---

### EP-04 — Gibt es `@`-Bindungen?

**Heute**: nein. `p11`: `d @ 1..=9` ist `LYR-LEX0012`; `Grammar.md:153` `SingleAttr = AT_IDENT`.

**Optionen**: A: `name @ pattern` (Rust, Scala, Haskell) — `@` wird eigenes Token, `LEX0012`
entfällt. B: `pattern as name` (OCaml, F#) — `as` ist Cast (`Grammar.md:446`) und Import-Alias
(`:162`), dritte Bedeutung. C: weglassen — im Arm nicht ersetzbar.

**Empfehlung: A.** Drei Vorbilder, Lowering kann es (`BindLocal` + Rekursion).
**Bruch**: nein (additiv); Lexer-Regel ist Spec-Änderung. **Hängt ab von**: Lexik-Gebiet, EP-32.

---

### EP-05 — Kann man auf einem Interface-Wert auf den konkreten Typ prüfen?

**Heute**: nein, und **die Spec verbietet es ausdrücklich** — Korrektur der zweiten Fassung, die
das als „Präzisierung, kein Bruch" verkaufte und den Satz nicht zitierte. Gemessen `p10d`:
`SEM0029`. Gelesen `docs/Bytecode.md:854` = `spec/13-bytecode.md:864`: *„There is no downcast; an
interface value cannot be narrowed back to its class."* Gelesen `:856-859`: ein Interface-Wert
trägt *„the object reference and the index of its concrete type in the Types section"* — der
Mechanismus wäre ein **Typindex-Vergleich**, nicht ein vtable-Zeigervergleich. Gemessen `q11`:
`Pair<int>` und `Pair<string>` hinter demselben Interface sind **zwei Typindizes** (`ty1`, `ty2`,
`mkiface t2, ty1` / `mkiface t6, ty2` im IR) — Monomorphisierung macht jede Instanz zu einem eigenen
Typ. `STATUS.md:2329` („A value carries no type tag") bleibt wahr: der Index hängt am
Interface-Wert, nicht am Objekt. Auf der v5-Liste als Punkt 13 (`lyric-v5-features.md:45`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts | Rust ohne `dyn Any` | jeder Besucher wird eine Interface-Methode |
| B: Typ-Pattern **nur** über Interface-Scrutinees, per Typindex | Scala `case c: Circle`, F# `:? T` | neue Pattern-Form; `x: T` kollidiert mit `LetPatternStmt` (`Grammar.md:372`); **`Bytecode.md:854` muss per ADR retiriert werden**; Generics-Frage (EP-42) muss vorher beantwortet sein |
| C: `d is Circle` + Narrowing | Kotlin, C# | zweiter Mechanismus (Rule 2) |
| D: `Sq { s }` einfach erlauben | — | verwischt „Struct-Pattern = irrefutabel" |

**Empfehlung: B, aber erst nach EP-42, und mit ausdrücklicher Revision von `Bytecode.md:854`.**
Schreibweise weder `x: T` noch `… as x`: `is Circle c` oder mit EP-04 `c @ Circle`. Erschöpfung
bleibt offen, `_` ist Pflicht.
**Bruch**: additiv für Quelltext, **Spec-Revision** im Bytecode-Kapitel (kein Formatwechsel: der
Index ist schon da). **Hängt ab von**: EP-42, Interface-Gebiet, Grammatik-Gebiet, EP-29.

---

### EP-06 — Dürfen Enums wachsen, ohne fremden Code zu brechen?

**Heute**: Attribut ja (`core.lyr:536`, `r46`), Regel nein (null Treffer in `src/`). Zwei Texte
sagen „außerhalb des deklarierenden **Moduls**": `design/patterns.md:261-263`, `core.lyr:532-533`.

**Optionen**: A: Grenze = Modul (wie geschrieben; die std zahlt `_` in jedem anderen std-Modul).
A′: Grenze = **Paket** (Rust `#[non_exhaustive]` wirkt außerhalb der Crate; revidiert beide Texte).
B: Bibliotheks-Enums standardmäßig offen, `@Frozen` opt-out (Swift) — dreht die sichere
Voreinstellung um. C: nichts.

**Empfehlung: A′, als ausdrückliche Revision von `design/patterns.md:261` und `core.lyr:532`.** Die
Paketgrenze ist die Auslieferungseinheit; eine Modulgrenze innerhalb eines Pakets ist kein
Versionsversprechen. **Bruch**: nein. **Hängt ab von**: Modul-/Paketgebiet, EP-03, EP-25 (nur für
ein künftiges Binärformat), Bytecode-Gebiet.

---

### EP-07 — Was bekommt ein Enum geschenkt?

**Heute**: nichts (`p25`: `SEM0059`). v5-Liste Punkte 3 und 15.

**Optionen**: A: Konformanz-Synthese `Equatable`, `Hashable`, `Ordered`, `Display`, `Debug` per
`::`-Liste ohne Körper (Swift). B: `derive`-Attribut (Rust) — zweite Schreibweise, Rule 2.
C: automatische strukturelle Gleichheit nur für payloadlose Enums (Kotlin, C#, OCaml) — zwei
Sorten Enum. D: nichts.

**Empfehlung: A**, plus `E.variants(): E[]` und `E.fromName(s): ?E` für payloadlose Enums
(Swift `CaseIterable`). Synthese zur Übersetzungszeit, keine Reflexion (`lyric-v5-features.md:156`).
**Was `Equatable` vergleicht** (EP-43): Tag und Payload strukturell; eine Klassen-Payload per
ihrer eigenen `Equatable`-Konformanz, sonst gar nicht synthetisierbar (Fehler, kein Referenzvergleich
— Enums haben keine Identität, siehe EP-43). **Bruch**: nein. **Hängt ab von**: Interface-/
Generics-Gebiet, EP-30, EP-22, EP-43.

---

### EP-08 — Haben Varianten einen Rohwert?

**Heute**: nein. Tag = Index in `variantTypes` (`docs/Bytecode.md:172`).

**Optionen**: A: nichts; `toInt`/`fromInt` von Hand (OCaml). B: `enum E : int { Red = 1 }` mit
`rawValue`/`fromRaw` für payloadlose Enums (Rust `A = 1` + `as i32`, Swift, C#) — zweite Bedeutung
für `:`; Tag bleibt Index. C: `@Discriminant(0x81)` — braucht EP-32 (`r47`: Attribut an Variante ist
`PAR0026`) und ein zweites compilergelesenes Attribut (`docs/guide/15-attributes.md:251`).

**Empfehlung: B, falls überhaupt.** **Bruch**: nein. **Hängt ab von**: Bytecode-/ABI-Gebiet,
EP-32, EP-30, Serialisierungsgebiet.

---

### EP-09 — Klammern um den Scrutinee?

**Heute**: Pflicht (`Grammar.md:385`, `506`). `p12`: elf Fehler ohne Klammern — Recovery-Defekt,
Sweep, keine Designfrage. `design/patterns.md` §3.7 will beide Formen.

**Optionen**: A: behalten. B: beide (Rule 2). C: Klammern überall streichen — Major, Formatter.

**Empfehlung: A; die Kaskade in den Sweep.** `match` allein klammerfrei macht die Sprache
inkonsistenter; „überall oder nirgends" gehört ins Kontrollfluss-Gebiet. **Formatter-Berührung**
(EP-44): `AstFormatter.cs:634-640` schreibt `match (` fest. **Bruch**: A nein; C major.
**Hängt ab von**: Kontrollfluss-Gebiet, EP-44.

---

### EP-10 — Wie sagt ein Pattern, dass es Felder auslässt?

**Heute**: syntaktisch gar nicht, aber dokumentiert (`guide/06:127-129`) und normiert
(`spec/07:164-165` „a field left out is not read"). `p33b`: `Rect { w }` über drei Felder läuft.
`p33`: `Rect { w, .. }` ist `PAR0026`; `p45`: `(a, ..)` ist `SEM0029`+`PAR0033`. Arrays haben `..`
(`Grammar.md:557-559`). **Bruchbreite gemessen**: 11 Feld-Patterns im Repo (`examples/enums.lyr:17,25`,
`examples/shapes.lyr:18,30`, `examples/patterns/binding-conditions.lyr:53,79`,
`examples/patterns/state-machine.lyr:41,43`, `tests/Lyric.Tests.Ir/golden/lowering/enums.lyr:15`,
`tests/Lyric.Tests.Sema/e2e/shapes.lyr:18,30`), **null** lassen ein Feld weg. Fremd: behauptet.

**Optionen**: A: so lassen (inkonsistent). B: `..` erlauben, Weglassen bleibt (Rule 2).
C: `..` **verlangen** (Rust `P { x, .. }`, `(a, ..)`) — bricht Teil-Feld-Patterns; im Repo null.

**Empfehlung: C.** Ein Mechanismus an drei Stellen; mit EP-31 wird `..` nötig. **Revidiert
`spec/07:164-165`** (der Satz „a field left out is not read" wird zu „a field must be named or covered
by `..`"). **Bruch**: minor, null Stellen im Repo; `lyrfix` existiert nicht (`ls src/`;
`docs/Befunde_und_Verbesserungen/PLAN.md:343,384`). **4.x-Warnstufe** ab 4.7. **Hängt ab von**:
EP-31, Werkzeug-Gebiet.

---

### EP-11 — Darf eine Bedingung mehrere `let` verketten?

**Heute**: nein (`p31`: `PAR0002`; `Grammar.md:377-378`). **Optionen**: A: `&&`-Kette (Rust 2024).
B: Komma (Swift). C: nichts. **Empfehlung: A.** **Bruch**: nein. **Hängt ab von**: Kontrollfluss-Gebiet.

---

### EP-12 — Wo dürfen irrefutable Patterns sonst noch stehen?

**Heute**: Lambda nur Tupel (`Grammar.md:487-488`; `p32`), `for` nur `IDENTIFIER | TuplePattern`
(`:382`; `p30`: `PAR0002`). `LetPatternStmt` (`:372`) erlaubt alle Formen.

**Optionen**: A: jedes irrefutable Pattern an beiden Stellen — Vorbilder **Rust, Kotlin und Swift
für Tupel** (Korrektur: Swift 4 erlaubt `{ (a, b) in }` und `for (a, b) in`; was Swift fehlt, sind
Struct-Patterns als solche). B: so lassen. C: refutable im `for` mit implizitem Überspringen (Swift
`for case`) — versteckter Filter.

**Empfehlung: A.** **Bruch**: nein. **Hängt ab von**: Lambda-Gebiet, Iterator-Gebiet.

---

### EP-13 — Was ist ein `match` über einen unbewohnten Typ?

**Heute**: zwei ICEs (`r08`/`p36`, `p36c`; Kontrolle `p36b`). `design/patterns.md` §3.6 ist
„TEILWEISE IMPLEMENTIERT" (`:226`). **Ausdruckstyp**: `UnifyArms` (`TypeChecker.cs:5066`) liefert
für null Arme `LyrType.Error`, nicht `never` — der Typ eines leeren `match` ist heute also nicht
einmal definiert (EP-34).

**Optionen**: A: erschöpfend, Typ `never`, Lowering `unreachable` (Rust, Haskell `EmptyCase`,
Swift `Never`). B: leeres Enum verbieten. C: nur den Absturz zum Fehler machen.

**Empfehlung: A.** `never` existiert (`Grammar.md:518`). Der ICE gehört in den Sweep.
**Bruch**: nein. **Hängt ab von**: Typen-Gebiet, EP-03, EP-34.

---

### EP-14 — Typargumente im Pattern-Pfad

**Heute**: Grammatik ja (`Grammar.md:548`, `:524`, §6.3 `:527-538`), Parser nein (`r41`).
**Optionen**: A: parsen (Regel anwenden). B: aus der Grammatik streichen. **Empfehlung: A.**
**Bruch**: nein. **Hängt ab von**: Generics-Gebiet.

---

### EP-15 — Dürfen Benutzer eigene Patterns definieren?

**Heute**: nein (`Grammar.md:545-560`). **Optionen**: A: nein (Rust, OCaml, Kotlin). B: `Unapply<T>`
(Scala) — Erschöpfung stirbt. C: totale aktive Patterns (F#) — neue Deklarationsform. D: `~=` (Swift)
— nur Test, zweites Testvokabular. **Empfehlung: A, mit Begründung in der Spec.** **Bruch**: nein.

---

### EP-16 — Ist eine Pattern-Bindung veränderlich?

**Heute**: `p29`: `SEM0019` im Arm; `p47`: `var P { x, y } = …` läuft. **Optionen**: A: `var` im
Arm. B: `var` auch in `LetPatternStmt` verbieten. C: so lassen. **Empfehlung: B mit A3 #23
(`lyric-v5-features.md:60`), sonst A.** **Bruch**: B minor. **Hängt ab von**: Wertesemantik, EP-26.

---

### EP-17 — Ist ein Enum werfbar?

**Heute**: drei Antworten (`STATUS.md:2290`, Anker „An enum's throwability has three answers").
**Optionen**: A: werfbar (Swift, Rust). B: nicht werfbar. C: nur mit `;`-Membern.
**Empfehlung: A.** **Bruch**: nein. **Hängt ab von**: Fehlergebiet §9.1, Bytecode, EP-29, EP-43
(ein geworfenes Enum hat keine Identität — `catch` bekommt eine Kopie oder den Verweis auf eine
unveränderliche Zelle, das ist dasselbe).

---

### EP-18 — Soll `match` in O(1) springen?

**Heute**: nein (`FunctionLowerer.cs:2404-2407`; `Bytecode.md:837`). Linear im Tag; **über
`string` ebenfalls linear, ein `eq` pro Arm** (`q10.ir`). Keine Zahl gemessen.

**Optionen**: A: so lassen. B: `switch`-Opcode (Formatwechsel). C: binärer Suchbaum im Lowering.
**Empfehlung: zuerst messen** — und zwar für Tag **und** String (EP-45 nennt die Messfragen).
**Bruch**: nein. **Hängt ab von**: VM-/Optimierer-Gebiet, EP-03, EP-45.

---

### EP-19 — Bleibt die `?T`-Asymmetrie (oben narrowt, im Payload nicht)?

**Heute**: ja (`p17`, `q05c` am Kopf; `q05` im Payload). `TypeChecker.cs:4812-4814` (Anker: *„at the
top of a match a name over a `?T` leaves `null` uncovered, inside a payload it binds the whole
optional"*), `design/patterns.md` §2.1. Spec: `spec/07:183-187` regelt nur `?E` am Kopf. *Korrektur*:
die zweite Fassung bezifferte den Preis von B mit „dann fehlt im Payload der Arm, der null benennen
könnte" — **falsch**, `Has(null) => 0` kompiliert (`q05`). Der **wirkliche Preis von A** ist
§1.3 (14): kein Pattern liefert den narrowten `int`; man schreibt `n ?? …` oder `n!` (`q05b`).

**Optionen**: A: so lassen und aufschreiben — Preis: EP-35 bleibt offen. B: überall narrowen —
eine Regel für jede Tiefe (passt zu `spec/07:176` „tests and binds at every depth"); Preis: das
ganze `?int` ist im Payload nicht mehr bindbar, ein `match` ohne `Has(null)`-Arm wird `SEM0050`
(minor; im Repo gemessen **zwei** Varianten mit `?`-Payload, `binding-conditions.lyr:70` und
`nullable.lyr:8`, beides Signaturen, kein betroffenes Pattern gefunden). C: nirgends narrowen.

**Empfehlung: A für die Bindungsregel, B lehne ich ab — aber nur, weil EP-35 die Lücke additiv
schließt.** Ohne EP-35 wäre B die ehrlichere Wahl. Dazu die Baustelle `STATUS.md:2212` („four ways
to ask 'is this null?' disagree inside a GENERIC body"). **Bruch**: nein. **Hängt ab von**: EP-35,
Optional-Gebiet, Generics-Gebiet.

---

### EP-20 — Wie wird das Guide-Kapitel wieder wahr?

Schuld, keine Designfrage: `guide/06:131` falsch (`p05`), Array-Patterns und `if let`-Familie
fehlen. **Empfehlung**: vor der ersten v5-Regeländerung korrigieren. **Bruch**: nein.

---

### EP-21 — Wie wird eine Diagnose lokal stummgeschaltet?

**Heute**: nur `--deny-warnings` (`src/Lyrc/Program.cs:138`); ein compilergelesenes Attribut
(`guide/15:251`); Attribute nur an Modul und `TopLevelDecl` (`Grammar.md:147,165`).
**Optionen**: A: `@Allow("LYR-SEM0100")` pro Deklaration (Rust, Kotlin, Scala). B: Kommentar-Pragma
(Go, C#) — Syntax, die der Lexer nicht sieht. C: Projektdatei-Stufen (`lyric.json` v2). D: nichts.
**Empfehlung: A für die Ausnahme, C für die Stufe.** **Bruch**: nein. **Hängt ab von**: EP-32,
Diagnostik-Gebiet, Build-Gebiet; EP-01/EP-02/EP-22 hängen umgekehrt hieran.

---

### EP-22 — Sollen Float-Patterns existieren, und was heißt dort Gleichheit?

**Heute**: erlaubt (`Grammar.md:560,563`; `spec/07:150`), gemessen `r31` (`1 2 3`), `r36` (`-0.0`
trifft `0.0`, `NaN` trifft nichts, `nan == nan` ist `false`), `r52` (`float` ist offen). Lowering:
Literal-Pattern ist ein `IrBinKind.Eq` (`FunctionLowerer.cs:2596-2610`), Range `Ge`/`Le`. Der
Widerspruch im Haus: `core.lyr:539-546` verweigert `Hashable` für `float` mit genau dieser
Begründung.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: Fehler — kein Float im Pattern | **keines** (Korrektur: Rust hat es nie zum Fehler gemacht, Lint 2024 entfernt) | bricht legalen Code; Grammatik ändern |
| **B: erlaubt, IEEE-Semantik spezifiziert: Literal = `==` (`0.0` trifft `-0.0`), Range = `>=`/`<`/`<=`, **ein NaN-Konstanten-Pattern ist ein Fehler**, `float` bleibt offen** | **Rust (seit 2024)**, C# | ein Absatz in `spec/07` §7.6, **im selben Atemzug** wie die `Hashable`-Absage in `core.lyr:539-546`: Gleichheit ist definiert, nur als Schlüssel taugt sie nicht |
| C: nur `_` | — | härter als A |
| D: Warnung auf Float-Literal | — | braucht EP-21; für Rust/C#-Leser ein Rauschen |

**Empfehlung: B** — Wechsel gegenüber der zweiten Fassung („D in 4.x, A in 5.0"), die ihr
einziges Vorbild falsch zitiert hatte. B ist die Option, die Rust **und** C# tragen, sie bricht
nichts, und sie beantwortet die Frage, die heute niemand gestellt hat, im selben Absatz wie
`core.lyr`. Lyric hat keine NaN-Konstante im Literal, der NaN-Fall ist also heute gar nicht
schreibbar — die Regel steht für den Tag, an dem `float.nan` ein Konstanten-Pattern (EP-36) wird.
**Bruch**: nein. **Bruchbreite**, Korrektur: `grep -rnE "[0-9]+\.[0-9]+\s*(\.\.=?|=>)" --include=*.lyr
examples stdlib tests` liefert **einen** Treffer, `tests/Lyric.Tests.Parsing/golden/match_stmt.lyr:2`
(`Circle(r) if r < 1.0 =>`) — ein Guard, kein Pattern. Float-Patterns im Repo: **null**. Die zweite
Fassung berichtete „keine Treffer"; die Schlussfolgerung hielt, die Messung nicht.
**Hängt ab von**: EP-03, EP-36, Skalargebiet.

---

### EP-23 — Ist ein leeres, umgedrehtes oder überlappendes Range-Pattern diagnostizierbar?

**Heute**: alles stumm — `r33` (`5..=1`, `7..7`), `r32` (Überlappung), **und** `q08` (`'z'..='a'`,
`-1..=-5`): druckt `2 2`. Signierte Grenzen sind nach §1.2 (d) grammatisch gar nicht vorgesehen.

**Optionen**: A: leeres/umgedrehtes Range = **Fehler** (Rust E0579), Überlappung = Warnung (EP-02/
EP-03). Der Vergleich ist über `int` (mit Vorzeichen, EP-37), `char` (Code-Point-Ordnung) und
`float` (IEEE `<`; `NaN`-Grenze = Fehler nach EP-22) je ein Literalvergleich. B: beides Warnung.
C: nichts.

**Empfehlung: A**, mit der Ordnung pro Typ in der Spec: Integer nach Wert **einschließlich
Vorzeichen** (`-1..=-5` ist leer), `char` nach Code-Point (`'z'..='a'` ist leer), `float` nach
IEEE (`1.0..0.5` leer). **Bruch**: minor; im Repo keines. **Hängt ab von**: EP-02, EP-03, EP-22, EP-37.

---

### EP-24 — Sind Variantenfelder Spalten der Matrix?

**Heute**: nein (`r19`, `r23`, `r14` vs `r13`; `TypeChecker.cs:4915`; Zeugen `Pair(_, _)`,
`Rect { … }` per `WitnessOf` `:4940-4945`). Begründung `:4806-4810`: lieber falsch zurückweisen als
falsch annehmen (`FunctionLowerer.cs:2443`).

**Optionen**: A: alle Felder als Spalten (= EP-03 A). B: nur Struct-Varianten an die einfeldrige
Rekursion anschließen (Symmetrie). C: so lassen.

**Empfehlung: B sofort, A mit EP-03.** Zeuge bei Teillücke: `Rect { w = 0, .. }` (mit EP-10),
einfügbar (EP-44). **Bruch**: nein. **Hängt ab von**: EP-03, EP-10, EP-44.

---

### EP-25 — Was passiert, wenn ein getrennt übersetztes Modul auf ein gewachsenes Enum trifft?

**Heute: die Frage kann im Format 4.0 nicht auftreten** — Korrektur der zweiten Fassung, die
schrieb „mit M37 scharf geworden". Gelesen `STATUS.md:140-147` (M37 slice 2): `dependencies` sind
**Quell**-Segmente, die in eine flache Tabelle aufgelöst und **mit dem Projekt übersetzt** werden.
Gelesen `docs/Bytecode.md:236-250` (Imports, Id 4): der Import-Abschnitt kennt nur
**Host-Funktionen** nach Name und Signatur — keinen Lyric-Modul-zu-Modul-Import. Gelesen
`STATUS.md:1595-1597`: `lyric pack` ist *„a byte copy, no linker"*. Ein `.lyrbc` ist ein
Whole-Program-Artefakt; „Modul A gebaut gegen 3 Varianten trifft 4" ist heute unmöglich.
`r50` belegt die **Soundness-Abhängigkeit** der Sema (`FunctionLowerer.cs:2443`), nicht dieses
Loch — das war ein Kategorienfehler.

**Was bleibt**: eine Regel für ein **künftiges Binärpaketformat** (`design-round-2026-09`: Pakete
als „kompiliertes Lyric mit Header"), die **vor** dessen Bau stehen muss, weil das Lowering die
Erschöpfung als Beweis benutzt.

**Optionen**: A: Variantenzahl (und -reihenfolge) ist Teil der Modulsignatur, der Reader lehnt
einen Mismatch ab (Rust: kein stabiles ABI). B: letzter Arm wird immer getestet, Mismatch ist
Laufzeitfehler (C# `SwitchExpressionException`; Java seit 21 `MatchException`, JEP 441) — ein
Vergleich pro `match` überall. C: nur `@NonExhaustive`-Enums testen den letzten Arm (Swift
`@unknown default`). D: nichts.

**Empfehlung: A und C, als Vorbedingung des Binärpaketformats, nicht als heutiger Sweep.**
**Bruch**: heute nein; für ein Binärformat Teil seiner Definition. **Hängt ab von**: EP-06,
Bytecode-/ABI-Gebiet, Paketgebiet (Stufen B–F), EP-30.

---

### EP-26 — Ist eine Pattern-Bindung flach oder tief unveränderlich?

**Heute**: flach. `r25b`: `Has(c) => { c.n = 42 }` mutiert das Objekt; `q09`: dieselbe Mutation
durch `let b = a;` hindurch sichtbar (`42`) — die Klassen-Payload ist geteilt. `r21`: der Rest ist
eine schreibbare Kopie. Das Enum **selbst** ist unveränderlich, weil es keinen Feldzugriff gibt
(`q09b`, `SEM0012`; EP-43).

**Optionen**: A: so lassen und aufschreiben (Referenzen bleiben Referenzen). B: tief unveränderlich
(braucht Mutabilität am Typ). C: `mut` im Pattern — dritte Bedeutung (`collections.lyr:81`).
D: Rest als lesender Slice (EP-27).

**Empfehlung: A, mit EP-16 und EP-43.** Spec-Satz: eine Pattern-Bindung kopiert genau so viel wie
`let x = e;`. **Bruch**: A nein; C minor; B major. **Hängt ab von**: EP-16, EP-27, EP-43, Wertesemantik.

---

### EP-27 — Array-Patterns: Slice, Trägertyp, Zeugen

**Heute**: `..rest` bindet eine Kopie — gelesen `FunctionLowerer.cs:4615-4640` (`BindNamedRest`:
`std.core.rawArrayAlloc` + Kopierschleife, **O(n) pro Arm**, siehe EP-45); nur `T[]` (`r53`);
Zeugen nur ohne Elementtests (`:4964`, `r51`). Nach Spec (`spec/07:189-192`) ist das Array gar
nicht enumerable (EP-39).

**Optionen**: (a) Rest: A Kopie · B `Span<T>` (💥 Punkt 10) · C lesender Slice. (b) Trägertyp: A nur
`T[]` · B `Indexable` (tötet Längen-Erschöpfung) · C stdlib-Typen mit bekanntem Layout. (c) Zeugen:
A wie heute · B Element+Länge in der Matrix.

**Empfehlung: (a) C, (b) A, (c) B.** **Bruch**: (a) C minor — benannter Rest im Repo: null
(`grep -rnE "\.\.[a-z]\w*\s*[,\]]"`). **Hängt ab von**: Slice-Gebiet, EP-03, EP-26, EP-39, EP-45.

---

### EP-28 — Was ist die Abbruchgrenze der Erschöpfungsmatrix?

**Heute**: `n <= 64` (`TypeChecker.cs:4970`), antwortet „erschöpfend" (`r50`, Miscompile;
Kontrolle `r51`).

**Optionen**: A: jede Schranke antwortet „nicht erschöpfend" mit eigener Diagnose — Vorbild rustc
(**Korrektur**: Crate-Attribut `#![pattern_complexity_limit = "N"]`, kein CLI-Flag; Fehler
„reached pattern complexity limit"). B: Array-Schranke weg, Schleife endet bei `max(exact)+1`.
C: Zeitbudget — **kein Vorbild** (Korrektur: das Roslyn-Beispiel der zweiten Fassung ist nicht
belegbar; Roslyns CS8078 ist stackbasiert, deterministisch) und nichtdeterministisch, abzulehnen.
D: so lassen.

**Empfehlung: B sofort (Sweep), A als Regel für EP-03.** Grundsatz in die Spec, und zwar in
`spec/07` §7.6 neben EP-39: **die Prüfung darf nur in Richtung „nicht erschöpfend" irren.**
**Bruch**: B formal minor. **Hängt ab von**: EP-03, EP-27, EP-39, Diagnostik-Gebiet.

---

### EP-29 — Wird `catch` zu einem Pattern?

**Heute**: nein (`Grammar.md:396-399`). **Optionen**: A: Bindung bleibt, `match` im Block. B:
`CatchBinding` wird `Pattern` (Swift, Rust) — ohne typed throws immer `_`. C: nur Typ-Pattern —
EP-05 zum zweiten Mal. **Empfehlung: A bis typed throws (`lyric-v5-features.md:32`), dann B.**
**Bruch**: nein. **Hängt ab von**: EP-17, EP-05, EP-03, Fehlergebiet.

---

### EP-30 — Was sieht der einbettende C#-Host von einem Enum?

**Heute: den Tag, nicht die Variante** — Korrektur der zweiten Fassung („kein Enum, kein Tag").
Gelesen `src/Lyric.Embedding/Marshal.cs:143`: `TypeTag.Ref or TypeTag.Enum => "an object"` — die
Grenze **kennt** ein Enum-Tag, behandelt es aber wie eine opake Referenz. Gelesen `grep -rn
"Variant" src/Lyric.Embedding/*.cs`: null Treffer — keine Variantensicht, kein Konstruktor.

**Optionen**: A: nichts. B: `ScriptValue.VariantName`, `VariantIndex`, `Field(i)`, Konstruktor
(Lua, Wren) — Index wird ABI. C: nur über den Namen. **Empfehlung: C, B später mit EP-08 B.**
**Was der Host bekommt** (EP-43): einen Verweis auf eine unveränderliche Zelle; eine Kopie wäre
semantisch gleich. **Bruch**: nein. **Hängt ab von**: EP-07, EP-08, EP-25, EP-43, Embedding-Gebiet.

---

### EP-31 — Wer darf eine Variante und ihre Felder im Pattern nennen?

**Heute**: keine Sichtbarkeit an Variante (`Grammar.md:261`) oder Feld (`:237`); nur am Enum
(`:256`, `:165`). Vorfrage zu EP-10 und A3 #20 (`lyric-v5-features.md:57`).
**Optionen**: A: Varianten erben die Enum-Sichtbarkeit, privates Feld → `..` Pflicht (Rust). B:
Zerlegung außerhalb verboten. C: `pub` pro Variante/Feld (Swift, C#). D: nichts.
**Empfehlung: A.** **Bruch**: minor mit A3 #20. **Hängt ab von**: Sichtbarkeitsgebiet, EP-10, EP-06.

---

### EP-32 — Wo dürfen Attribute stehen, und wie viele darf der Compiler lesen?

**Heute**: Modul und `TopLevelDecl` (`Grammar.md:147,165`; `r47`); genau eines compilergelesen
(`guide/15:251`); `core.lyr:527-530` `OnMethod` als Anker ohne Member.
**Optionen**: A: überall, Zahl offen. B: nur Syntax. C: aufgezählter Satz in der Spec (Swift
`@frozen`-Modell). **Empfehlung: C** (`@Deprecated`, `@NonExhaustive`, `@Allow`; kein
`@Discriminant`). **Bruch**: nein. **Hängt ab von**: EP-06, EP-08, EP-21, Rule 2.

---

### EP-33 — Ist der Zeuge maschinenlesbar?

**Heute**: `grep -rn CodeAction src/` null Treffer; `lyrc build --json` auf `r51` liefert den
Zeugen nur in `message`. **Optionen**: A: `witnesses: [...]` im JSON, Code-Action in `lyrls`
(rust-analyzer, Roslyn). B: Fix-it-Text vom Compiler (Swift) — zweiter Formatter. C: nichts.
**Empfehlung: A**; Anforderung an EP-03/EP-24/EP-10: der Zeuge ist ein gültiges Pattern.
**Bruch**: nein. **Hängt ab von**: EP-03, EP-24, EP-10, EP-44, Diagnostik-Gebiet.

---

### EP-34 — Welchen Typ hat ein `match`-Ausdruck? *(neu)*

**Heute**: die Regel steht — anders als die zweite Fassung nahelegte, die sie gar nicht behandelte
— **in der Spec**: `spec/06-operators.md:130-139` §6.9: *„`match` in value position unify their arm
types: equal types, or one arm `null` widening the result to the optional. Disagreeing arms are one
error (`LYR-SEM0016`)"*; in einem Adaptationskontext prüfen die Arme gegen den Kontexttyp
(`:136-139`). Gemessen: `q03` (`true => 1, false => null` → `?int`, druckt `1`), `q03b` (`int` vs
`string` → `SEM0016`), `q03e` (`let r: ?int = match …` mit zwei `int`-Armen läuft, `2`).
**Nicht in der Spec, aber gebaut**: ein divergierender Arm zählt nicht mit — `q03c` (`false =>
throw Oops {}` neben `true => 1` läuft, `1`); gelesen `TypeChecker.cs:5074-5076` (*„A diverging arm
('throw', 'panic') contributes nothing (§6.9)"*) — der Kommentar verweist auf §6.9, §6.9 sagt es
nicht. **Nicht adaptiert**: Zahlbreiten — `q03d` `true => 1i8, false => 2` ist `SEM0016: 'int8' vs
'int'` (kontextlos; ob ein Kontext `let r: int8 = …` das unsuffigierte `2` adaptiert, ist nach
`:137` zu erwarten, nicht gemessen — **behauptet**). **Leerer `match`**: `UnifyArms` liefert
`LyrType.Error` (`:5068`), dann ICE (EP-13).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: §6.9 um die `never`-Regel ergänzen (Arm vom Typ `never` trägt nichts bei; alle `never` → `never`) und den leeren `match` als `never` definieren (EP-13 A); Breiten bleiben strikt | Rust (`!` koerziert), Swift `Never` | eine Spec-Zeile; keine Implementierungsarbeit außer EP-13 |
| B: dazu Breitenadaption **ohne** Kontext (kleinste gemeinsame Breite) | C# (`int`/`long` → `long` im `switch`-Ausdruck über „best common type") | Rule-2-Verdacht: eine zweite Vereinigungsregel neben dem Adaptationskontext (§3.1), die schon existiert |
| C: nichts | heute | ein Kommentar im Compiler ist die einzige Fassung der `never`-Regel |

**Empfehlung: A.** Die Breitenfrage ist mit dem Adaptationskontext beantwortet: wer `int8`
will, annotiert. **Bruch**: nein. **Hängt ab von**: EP-13, Typen-Gebiet (§3.1 Adaptation).
**Konfidenz**: gemessen (q03–q03e), gelesen (§6.9, `:5066-5083`).

---

### EP-35 — Wie bindet ein Pattern den narrowten Wert eines `?T`-Payloads? *(neu)*

**Heute**: gar nicht. `q05`: `Has(null) => 0, Has(n) => n` ist `SEM0001 cannot assign '?int' to
'int'`; `q05b`: `n ?? -9` läuft; `q05c`: am Kopf narrowt der Name. Die Or-Regel verlangt Identität:
`q04b` `A(x) | B(x)` über `int`/`?int` ist `SEM0032` (plus Folgefehler `SEM0005`). Lyric hat das
`null`-Pattern (`spec/07:183-187`) als **eine** Hälfte der Optional-Zustände — die andere Hälfte
hat kein Pattern.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen; `n ?? …` oder `n!` im Körper | heute | ein Nulltest nach einem Pattern, das den Nullfall gerade ausgeschlossen hat — der Leser fragt, warum |
| B: Flussnarrowing — nach einem `Has(null)`-Arm bindet `Has(n)` das `int` | TypeScript-artig | die Bindung hängt von der **Armreihenfolge** ab; `Has(n)` allein bindet weiterhin `?int` — zwei Typen für dasselbe Pattern |
| C: **Präsenz-Pattern**, das Dual zu `null`: `Has(n?)` (Schreibweise offen; Rust `Some(n)`) — bindet `T`, deckt nur den präsenten Fall, `null` bleibt der andere | Rust `Some(n)`/`None`, Swift `.some(n)`/`.none` | eine neue Pattern-Form, aber die **fehlende Hälfte** eines Mechanismus, der schon da ist (`null`); die Matrix hat `null`/präsent bereits als Konstruktoren (EP-03) |
| D: EP-19 B — überall narrowen | — | siehe EP-19 |
| E: Or-Regel lockern: `int`/`?int` vereinigen zu `?int` | — | zweite Vereinigungsregel (Rule 2); löst `q05` nicht |

**Empfehlung: C.** Es ist die additive Antwort, und sie ist die Hälfte, die zu `null` fehlt; die
Erschöpfungsmatrix kennt beide Konstruktoren schon. Die Schreibweise gehört ins Optional-Gebiet
(Kandidaten: `n?`, `?n`, `some(n)` — letzteres wäre ein Schlüsselwort). E lehne ich ab: die
Identitätsregel für Or-Bindungen ist richtig. **Bruch**: nein (additiv). **Hängt ab von**:
Optional-Gebiet, EP-19, EP-03. **Konfidenz**: gemessen.

---

### EP-36 — Dürfen Konstanten im Pattern stehen? *(neu)*

**Heute**: nein, und der Versuch ist still falsch (§1.3 (13), `q02`): `LIMIT => 1` bindet einen
neuen Namen `LIMIT`, der das `let LIMIT` verdeckt; zwei `SEM0071`, `f(3)` liefert 1. Die
Auflösungsregel (§1.2 (g)) kennt nur Variante-oder-Bindung.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: nein; Konstanten per Guard `n if n == LIMIT` | OCaml, Haskell | der Tippfehler-/Verdeckungsfall bleibt still, solange EP-01 nicht E wählt |
| B: ein Name, der ein `static`/Modul-Level-Konstante bezeichnet, ist ein Test; ein lokales `let` nicht | Rust (`const` im Pattern, Lokale sind Bindungen), C# `const` | Auflösung wird **scope-gerichtet**: dieselbe Schreibweise ist Test oder Bindung, je nachdem, was in Sichtweite ist — genau die Klasse aus EP-01; Rust zahlt sie mit `bindings_with_variant_name` |
| C: mit EP-01 E: bloße Namen sind **immer** Tests (Variante, Konstante, Global), Bindungen tragen `let` | Swift (`case .a`, Ausdrucks-Pattern via `~=`, `case let x`), C# (`var x`) | fällt mit E kostenlos an; eine Konstante im Pattern verlangt `Equatable` (heute für `int`/`string`/`char`/`bool`/`float` eingebaut) |

**Empfehlung: C, als Teil von EP-01 E.** B alleine importiert Rusts Problemklasse. **Bruch**: mit
E minor (siehe dort); ohne E nein. **Hängt ab von**: EP-01, EP-40, Globals-/Konstantengebiet
(`Grammar.md:236` `StaticBinding`). **Konfidenz**: gemessen.

---

### EP-37 — Negative Literale und die Ordnung von Ranges: Grammatik oder Parser hat recht? *(neu)*

**Heute**: §1.2 (d) — `Grammar.md:92-100` ohne Vorzeichen, `Parser.Patterns.cs:46,73-90` mit; `q01`
läuft (`1 2 3`); `spec/07:150` sagt nur „literals". `q08`: `-1..=-5` und `'z'..='a'` sind stumme tote
Arme.

**Optionen**: A: Grammatik nachziehen — `PatternLiteral = [ '-' ] ( IntLit | FloatLit ) | …` in
§7 (Rust: `-5..=-1` ist erlaubt, negative Literal-Patterns sind eine eigene Produktion). B: Parser
zurücknehmen — ein negatives Pattern nur per Guard. C: allgemeine Konstantenausdrücke im Pattern
(C# `case -5:` erlaubt jeden Konstantenausdruck) — zieht EP-36 nach sich.

**Empfehlung: A**, mit der Ordnungsregel aus EP-23 im selben Absatz (leeres Range ist ein Fehler,
also ist `-1..=-5` einer). Der Parser-Kommentar (`:79-81`: „a pattern that quietly matches a
runtime value, which grammar §7 does not have") zeigt, dass die Einschränkung auf Zahlen bewusst
ist — sie gehört in die Grammatik, nicht nur in den Kommentar. **Bruch**: nein (die Grammatik
holt nach, was der Parser tut). **Hängt ab von**: EP-23, EP-36, Grammatik-Gebiet. **Konfidenz**:
gemessen, gelesen.

---

### EP-38 — Feld-Defaults in Struct-Varianten: bauen oder streichen? *(neu)*

**Heute**: §1.2 (e) — grammatisch erlaubt (`Grammar.md:263` + `:237`), `IR0001` an der Deklaration
(`q07`, `q07b` — auch bei vollständigem Initializer). Bei Structs gibt es Defaults (`SEM0106`
„omits field ';', which has no default" aus `q11`s erstem Lauf zeigt, dass Struct-Initializer
Defaults kennen).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bauen — `S.R { h = 2 }` füllt `w = 0`; Patterns unberührt (ein Default ändert nicht, was ein Feld-Pattern sieht) | Rust: **keine** Defaults in Varianten (`..Default::default()` nur für Structs); Swift: Enum-Assoziierte Werte **haben** Default-Argumente seit SE-0155 | dasselbe Lowering wie beim Struct-Default; Rule 2 gewahrt (eine Default-Regel für alle `Field`) |
| B: streichen — `StructVariant` auf `IDENTIFIER ':' TypeExpr` einschränken | Rust | eine Grammatikzeile; ein Enum, das einen Default will, wird ein Struct-Payload |
| C: so lassen | heute | eine Grammatikform, die kein Programm je benutzen kann |

**Empfehlung: A.** `Field` ist eine Produktion, und sie hat an einer Stelle einen Default und an
der anderen nicht — das ist Rule 2 rückwärts. Wenn A zu teuer ist, ist B ehrlich; C ist es nicht.
**Bruch**: nein. **Hängt ab von**: Struct-/Initializer-Gebiet, EP-31. **Konfidenz**: gemessen,
gelesen.

---

### EP-39 — Welche Scrutinee-Formen verspricht §7.6 als enumerable? *(neu)*

**Heute**: §1.2 (f) — `spec/07:189-192` verspricht Enum, `bool`, `?T`; gebaut sind zusätzlich
Array-Längenklassen (`p38`, `r51`) und Einspalten-Tupel (`r38`, `r40`). Ein Zweitcompiler nach Spec
(Lyricpp, `erato-repo`-Notiz) dürfte `match (xs: int[]) { [] => …, [_, ..] => … }` ablehnen.

**Optionen**: A: die Spec zieht nach — enumerable sind Enum, `bool`, `?T`, **Tupel (spaltenweise),
Arrays (nach Länge)**, ab EP-03 auch Integer-/Char-Intervalle; plus der Grundsatz aus EP-28. B: die
Implementierung zieht zurück — Arrays/Tupel verlangen `_` (bricht `p38`-artigen Code). C: Spec
nennt „mindestens" und lässt die Implementierung vorauslaufen — verbietet sich, weil die
Erschöpfung das Lowering steuert (`FunctionLowerer.cs:2443`): zwei Compiler mit verschiedener
Reichweite erzeugen verschiedene Programme aus demselben Text.

**Empfehlung: A, spec-first, vor EP-03.** C ist bei einer soundness-tragenden Prüfung
ausgeschlossen. **Bruch**: nein. **Hängt ab von**: EP-03, EP-28, Spec-Repo (Regel-PR mit
`since:`-Gate). **Konfidenz**: gemessen, gelesen.

---

### EP-40 — Wo wird die Auflösung bloßer Namen normiert, und gilt sie in Pattern und Ausdruck gleich? *(neu)*

**Heute**: §1.2 (g). Im Pattern typgerichtet und nur im Compiler (`TypeChecker.cs:5031-5033`,
`:4928`): `Red` matcht auch ohne Import, eine Variante schlägt ein Local (`p42c`). Im Ausdruck
scopegerichtet: `let s: Signal = Red;` ist `SEM0002` (`q06`). Zwei Regeln für einen Namen.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: beide Positionen typgerichtet — auch im Ausdruck darf ein bloßer Name eine Variante des Kontexttyps sein | Swift `.red` (mit Punkt) in beiden Positionen; Java `switch` über Enum (nur im `case`) | im Ausdruck braucht es einen Kontexttyp (§3.1 Adaptation existiert); `let s = Red;` ohne Annotation bleibt `SEM0002` — zwei Antworten je nach Kontext |
| B: beide Positionen scopegerichtet — im Pattern ist ein bloßer Name eine Variante nur, wenn sie importiert/in Sichtweite ist | Rust (Varianten müssen `use`d sein), Kotlin | **bricht** jeden bare Arm ohne Import — gemessen 20 Arme im Repo, alle ohne `import … { Red }`? nicht gezählt (**behauptet**); mit EP-01 E wird ein nicht sichtbarer Name ein Fehler statt einer Bindung |
| C: so lassen, aber **aufschreiben**: Pattern typgerichtet, Ausdruck scopegerichtet | heute | ehrlich, und die Asymmetrie bleibt |
| D: A mit Swifts Punkt: `.Red` in beiden Positionen | Swift | zweite Schreibweise neben `Signal.Red` (EP-01 B) |

**Empfehlung: C sofort (spec-first: die Regel muss stehen, bevor EP-01 sie ändert), A für 5.0
zusammen mit EP-01 E.** Mit E ist ein bloßer Name im Pattern immer ein Test; die typgerichtete
Auflösung wird dann im Ausdruck mit Kontexttyp nachgezogen, sodass `let s: Signal = Red;` und
`f(Red)` gehen — dieselbe Regel, beide Positionen. **Bruch**: A additiv im Ausdruck; im Pattern
gemeinsam mit E minor. **Hängt ab von**: EP-01, EP-36, Namensauflösungs-/Modulgebiet, §3.1.
**Konfidenz**: gemessen, gelesen.

---

### EP-41 — Revidiert EP-02 den Satz „an arm made unreachable by an earlier one is not an error"? *(neu)*

**Heute**: `spec/07-statements.md:158`, normativ. Kein Compiler-Code widerspricht ihm.

**Optionen**: A: Satz bleibt, ergänzt um „but a warning (`LYR-SEMnnnn`)" — mit `since:`-Gate
(Spec-Modus spec-first). B: Satz wird retiriert, Fehler (C#) — ADR nach `CONTRIBUTING.md`, 30 Tage.
C: Satz bleibt unverändert, keine Diagnose.

**Empfehlung: A, in demselben Regel-PR wie EP-02 (a).** **Bruch**: nein. **Hängt ab von**:
EP-02, Spec-Repo-Ritual. **Konfidenz**: gelesen.

---

### EP-42 — EP-05 bei Generics und Structs: was trifft `is Pair`? *(neu)*

**Heute**: Monomorphisierung erzeugt je Instanz einen Typ (`q11`: `ty1 Pair<int>`, `ty2
Pair<string>`, zwei `mkiface` mit verschiedenen Indizes); `guide/08-generics.md:35`. Der
Interface-Wert trägt den Index (`Bytecode.md:856`). Ein Struct hinter einem Interface: `mkiface`
hebt eine Objektreferenz (`:845`), Wertsemantik lebt in `structcopy` (`:183`, `:874-878`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: der Typ im Pattern muss **voll instanziiert** sein (`is Pair<int> p`); `is Pair` ohne Argumente ist ein Fehler | Rust `downcast_ref::<Pair<i32>>()`; C# `is Pair<int>` (nicht `is Pair<>`) | ehrlich gegenüber dem Format: ein Index, ein Test; verlangt EP-14 (Typargumente im Pattern-Pfad) |
| B: `is Pair` trifft jede Instanz, die Bindung hat einen existenziellen Typ | Scala (Typlöschung), Kotlin `is Pair<*>` | Lyric hat keine Typlöschung und keinen Existenzialtyp — ein neues Typkonzept für einen Sonderfall |
| C: Struct-Narrowing liefert eine **Kopie** (`structcopy` nach dem Test), Klassen-Narrowing den Verweis | folgt `:874` „wherever a struct value is bound into a new location" | keine neue Regel: die Bindung ist eine neue Location |

**Empfehlung: A und C.** A setzt EP-14 voraus — ein weiterer Grund für EP-14 A. **Bruch**: nein
(additiv). **Hängt ab von**: EP-05, EP-14, Generics-Gebiet, Bytecode-Gebiet. **Konfidenz**:
gemessen (q11), gelesen.

---

### EP-43 — Sind Enum-Werte Werte oder Referenzen? *(neu)*

**Heute**: eine Referenz auf eine **unveränderliche** Zelle, und deshalb unbeobachtbar. Gelesen
`docs/Bytecode.md:826-838`: `newvariant` **allokiert**; kind 1 hat keine Wertsemantik-Klausel, kind 3
(struct) sagt „with value semantics" (`:175`); `structcopy` (`:865`, `:874-878`) gilt nur für
Struct-Einträge — **für Enums wird nie kopiert**. Gelesen `Marshal.cs:143`: `TypeTag.Enum` wird wie
`Ref` behandelt. Gemessen `q09b`: es gibt keinen Feldzugriff auf einen Enum-Wert (`SEM0012`), also
keine Mutation, also ist Teilen von Kopieren nicht unterscheidbar. Gemessen `q09`: eine
**Klassen-Payload** wird geteilt (`42` durch `let b = a;` hindurch). Identität: kein `==`
(`p25`), kein Referenzvergleich — es gibt keine.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: **aufschreiben**: ein Enum-Wert ist unveränderlich; `let b = a;` teilt oder kopiert, das ist nicht beobachtbar und bleibt Implementierungsfreiheit; Payloads folgen ihrer eigenen Semantik (Struct: Kopie beim Binden, Klasse: Verweis) | Rust `enum` mit `Copy`/`Clone`, Swift Enum als Werttyp — beide mit unveränderlichem Payload-Zugriff nur über Patterns | eine Spec-Zeile in §3 und §7.6; `Equatable`-Synthese (EP-07) ist damit strukturell definiert |
| B: Wertsemantik ausdrücklich, mit `structcopy`-artigem Kopieren | Swift | Kosten ohne beobachtbaren Nutzen, solange Enums unveränderlich sind |
| C: Referenzsemantik mit Identität (`===`) | Java-Enum-Singletons | Identität ist bei Payload-Enums sinnlos (`Has(4) === Has(4)` wäre `false`) |

**Empfehlung: A.** Das entscheidet EP-07 (strukturell), EP-26 (flach: die Zelle ist unveränderlich,
ein Klassen-Payload nicht) und EP-30 (der Host bekommt eine unveränderliche Sicht, Kopie oder
Verweis egal). **Bruch**: nein. **Hängt ab von**: Wertesemantik-Gebiet (A3 #23), EP-07, EP-26,
EP-30. **Konfidenz**: gemessen, gelesen.

---

### EP-44 — Werkzeug-Ergonomie jenseits des Zeugen: Completion und Formatter *(neu)*

**Heute**:
- **`lyrls`-Completion in Pattern-Position**: gelesen `src/Lyric.Lsp/Analysis/ScopeCompletion.cs:32-50`
  (`At`: Scope-Namen inner-to-outer, dann Modul-Member, dann Builtins) und `:140-158`
  (`PatternNames`: liefert aus Patterns nur `LocalSymbol`s; Kommentar `:151-152` *„a variant is
  bound to its EnumVariantSymbol and is not a name in scope"*). `EnumVariantSymbol` kennt die Liste
  als Kind (`:209`), aber es gibt **keine Kontexterkennung „Cursor steht in einem Arm-Pattern über
  Typ E"** — die Varianten von `Signal` werden nur angeboten, wenn sie importiert im Scope stehen.
  Genau die Tippfehlerklasse aus EP-01 würde eine typgerichtete Completion vor dem Compiler
  abfangen. (**gelesen**, nicht gegen einen laufenden Server gemessen.)
- **`lyrfmt` und Or-Patterns**: gelesen `src/Lyric.Frontend/Formatting/AstFormatter.cs:995`:
  `OrPattern o => Doc.Join(Doc.From(" | "), …)` — **flach**, ohne Umbruchpunkt; ein Or-Pattern mit
  zwölf Alternativen wird eine Zeile. `ArmDoc` (`:642-661`): Guard, ` => `, Block ohne Komma,
  Ausdruck mit Komma. `MatchDoc` (`:634-640`) schreibt `match (` fest (EP-09).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: Completion typgerichtet: im Arm-Pattern über `E` die Varianten von `E` zuerst, unabhängig vom Import; im Feld-Pattern die Feldnamen | rust-analyzer, IntelliJ (Kotlin `when`) | LSP-Arbeit: die Sema muss dem Server den Scrutinee-Typ am Cursor liefern (SemanticModel hat ihn) |
| B: Formatter bricht Or-Patterns wie Argumentlisten (`Doc.Group` mit `Line`) | rustfmt (bricht nach `\|`) | eine Formatter-Regel; **ändert die Ausgabe** für bestehende lange Or-Arme — der Formatter-Vertrag (`lyrfmt --check`, `STATUS.md:1590`) verlangt einen Bump |
| C: nichts | heute | EP-01s Tippfehler bleibt bis zum Compiler unsichtbar; lange Or-Patterns sind unlesbar |

**Empfehlung: A und B, beide im Werkzeug-Gebiet, aber hier vermerkt, weil A die billigste Hälfte
von EP-01 ist und B die einzige Stelle, an der EP-09/EP-10 den Formatter-Vertrag berühren.**
**Bruch**: A nein; B Formatter-Ausgabe (minor). **Hängt ab von**: Editor-Gebiet, Formatter-Gebiet,
EP-01, EP-33. **Konfidenz**: gelesen.

---

### EP-45 — Performanz außerhalb des Tag-Vergleichs *(neu)*

**Heute, gelesen und im IR gemessen, keine Zeiten**:
- **`match` über `string`**: `q10.ir:268-306` — pro Arm `const "…"`, `eq`, `condbr`; **n
  Stringvergleiche**, kein Hash, keine Längenvorprüfung im IR (ob `eq` auf Strings in der VM die Länge
  zuerst vergleicht: **behauptet**, nicht gelesen).
- **`..rest`**: `FunctionLowerer.cs:4615-4640` — `rawArrayAlloc(count)` plus Kopierschleife pro
  **Arm**, der den Rest bindet; ein `match` mit drei `[x, ..rest]`-Armen, die durchfallen, kopiert
  bis zu dreimal (**gelesen**, die Dreifachkopie ist gefolgert, nicht gemessen).
- **Tiefe Payload-Patterns**: pro Ebene `enumtag` + `enumas` + `ldfld` (`Bytecode.md:837-838`);
  kein Zwischenspeichern über Arme hinweg (`FunctionLowerer.cs:2404-2407`: „the arms are tried in
  the order written") — `Add(Lit(0), r)` und `Add(Lit(1), r)` lesen den inneren Tag zweimal
  (**behauptet**: aus dem Kommentar gefolgert, IR nicht geprüft).

**Optionen**: A: messen, drei Fragen — (1) `match` über 20 String-Arme gegen `Map<string,int>`-Lookup,
(2) `[x, ..rest]` in einer Rekursion über 10⁵ Elemente (O(n²)-Verdacht), (3) ein zehnarmiger
`match` mit zweistufigen Payload-Patterns gegen die handgeschriebene Verschachtelung. B: ohne
Messung optimieren (Hash-Switch, Slice, Entscheidungsbaum) — verletzt den Maßstab aus EP-18.
C: nichts.

**Empfehlung: A, zusammen mit der EP-18-Messung, als eine Messrunde.** Der Rest-Fall (2) ist der
einzige mit einem Komplexitätsklassen-Verdacht, nicht nur einem Konstantenfaktor — er verstärkt
EP-27 (a) C. **Bruch**: nein. **Hängt ab von**: EP-18, EP-27, Optimierer-/VM-Gebiet.
**Konfidenz**: gelesen (IR, Lowering), Zeiten nicht gemessen.

---

## 4. Was wir übernehmen sollten

Nach Hebelwirkung:

1. **Spec-first-Nachträge, bevor irgendetwas geändert wird**: die Auflösungsregel für bloße
   Namen (EP-40 C), die Reichweite der Erschöpfung (EP-39 A), die `never`-Regel für `match`-Arme
   (EP-34 A), das negative Literal (EP-37 A), die Enum-Wertsemantik (EP-43 A), die `?T`-Payload-
   Regel (EP-19 A). Sechs Sätze, die heute nur im Compiler stehen.
2. **Die billige Hälfte von EP-01/EP-02 sofort**: `NameSuggestion` an SEM0031 (`TypeChecker.cs:5297`)
   und die Warnung auf einem irrefutablen nicht-letzten Arm — mit dem Spec-Zusatz aus EP-41.
3. **Die Sweep-Liste** — jetzt **sechs** gemessene Defekte, keine Designfragen: die
   Array-Längenschranke (`r50`, Miscompile, EP-28 B); die Struct-/Tupel-Varianten-Asymmetrie
   (`r14`/`r13`, EP-24 B); der ICE am leeren Enum (`r08`, EP-13); die elf Fehler nach `PAR0019`
   (`p12`, EP-09); **das negative Literal in der Grammatik** (`q01`, EP-37 — ein Grammatik-PR);
   **der Feld-Default in Struct-Varianten** (`q07`, EP-38 — bauen oder Grammatik einschränken).
4. **Rusts Diagnose-Trio** (EP-01/02/03/24) mit einfügbarem Zeugen (EP-33) und typgerichteter
   Completion (EP-44 A).
5. **Rusts `==`-Semantik für Float-Patterns mit NaN-Verbot** (EP-22 B) — Wechsel gegenüber der
   zweiten Fassung, die Rust falsch zitiert hatte.
6. **Rusts `Some(n)` als fehlende Hälfte zu `null`** (EP-35 C) — additiv, schließt `q05`.
7. **`@NonExhaustive`s Regel** (EP-06 A′), und EP-25 als Vorbedingung eines Binärpaketformats,
   nicht als heutiges Loch.
8. **Swifts synthetisierte Conformances** (EP-07 A), strukturell definiert über EP-43.
9. **Rusts `@`-Bindung** (EP-04), **`let`-Ketten** (EP-11), **`..` einheitlich** (EP-10 C).
10. **Rusts Kopie-Bindung ohne Bindungsmodi**: in die Spec, zusammen mit EP-26 und EP-43.

Bewusst **nicht** übernehmen:
- Extraktoren (EP-15); Swifts `if case`-Familie; `Indexable`-Array-Patterns (EP-27 b); `match`
  ohne Klammern als Insellösung (EP-09); offene Summen per Klassenhierarchie; Laufzeit-Tagtest
  in jedem `match` (EP-25 B); Zeitbudgets in der Matrix (EP-28 C); Rusts scopegerichtete
  Konstanten-Patterns ohne `let`-Marker (EP-36 B); Typlöschung für `is Pair` (EP-42 B).

---

## 5. Konflikte

| Konflikt | Gegenüber | Auflösung |
|---|---|---|
| **EP-02** Warnung/Fehler | `spec/07:158` „not an error" | Warnung ist verträglich, wird als Zusatz geschrieben (EP-41 A); Fehler bräuchte ADR. |
| **EP-05/EP-42** Typ-Pattern | `Bytecode.md:854` „There is no downcast" | **Normativer Satz, muss revidiert werden** — kein Formatwechsel, aber Spec-Revision per ADR; erst nach der Generics-Antwort (EP-42 A). |
| **EP-39** Erschöpfungsreichweite | `spec/07:189-192` vs. Implementierung | Spec zieht nach, **vor** EP-03; C (Spec „mindestens") ist bei soundness-tragender Prüfung ausgeschlossen. |
| **EP-40/EP-01/EP-36** | Regel steht nur im Compiler | Erst aufschreiben (C), dann ändern (E). |
| **EP-01/EP-02/EP-22** Warnungen | `--deny-warnings` ohne EP-21 | EP-21 vor der Warnstufe. |
| **EP-22** Float | `core.lyr:539-546` | Ein Absatz für beide: Gleichheit ist definiert (`==`), als Schlüssel untauglich. Rust und C# tragen B. |
| **EP-19/EP-35** | `design/patterns.md` §2.1, `TypeChecker.cs:4812` | Regel bleibt (A), Lücke wird additiv geschlossen (Präsenz-Pattern). |
| **EP-25** | Format 4.0 kennt keine Modul-Imports (`Bytecode.md:236-250`) | Keine heutige Frage; Vorbedingung des Binärpaketformats. |
| **EP-06** Modul vs. Paket | `design/patterns.md:261`, `core.lyr:532` | Derselbe PR retiriert beide Sätze. |
| **EP-37** negatives Literal | `Grammar.md:92-100` vs `Parser.Patterns.cs:46` | Grammatik zieht nach. |
| **EP-38** Feld-Default | `Grammar.md:263` vs `IR0001` | Bauen (A) oder Grammatik einschränken (B); nicht lassen. |
| **EP-43** Enum-Semantik | Wertesemantik-Gebiet (A3 #23) | Unveränderlich, Teilen/Kopieren unbeobachtbar — aufschreiben, nicht entscheiden. |
| **EP-08** vs **EP-32** | `r47` | Entweder Attribute wachsen, oder EP-08 nimmt Syntax (B). |
| **EP-10** vs **EP-31** | — | `..` wird mit privaten Feldern nötig; `spec/07:164-165` wird revidiert. |
| **EP-10 / EP-01** Migration | `lyrfix` fehlt | Null Zeilen im Repo — kein Blocker für diese zwei. |
| **EP-15** | Rule 2 | Ablehnen mit Begründung in der Spec. |
| **EP-16 + EP-26 + EP-43** | Wertesemantik | Drei Facetten einer Frage: Zelle, Tiefe, Wert/Referenz. |
| **EP-17 + EP-29** | Fehlergebiet | Zusammen; B erst mit typed throws. |
| **EP-18 + EP-45** | Optimierer | Eine Messrunde: Tag, String, Rest, Tiefe. |
| **EP-28** | Soundness (`FunctionLowerer.cs:2443`) | Grundsatz in §7.6: nur in Richtung „nicht erschöpfend" irren. |
| **EP-30** | Tag = Index (`Bytecode.md:172`) | Name statt Index; Diskriminanten, wenn der Index Vertrag wird. |
| **EP-33 + EP-44** | Werkzeug-Gebiet | Zeuge einfügbar, Completion typgerichtet, Or-Umbruch im Formatter — drei LSP/Formatter-Posten, die an EP-01/03/10/24 hängen. |
| **EP-09 / EP-13** | — | Sweep, kein Design. |

---

## 6. Nach der Kritik geändert

**Falsche Aussagen korrigiert (jede selbst nachgeprüft):**
- **EP-01/§1.3 (1)**: die Auflösungsregel für bloße Namen steht **nirgends geschrieben** —
  `Grammar.md:547` listet nur `IDENTIFIER`, `spec/07:150-151` sagt „bindings; enum variants"; die
  Regel lebt in `TypeChecker.cs:5031-5033` und `:4928`. Neu als §1.2 (g) und EP-40.
- **EP-02**: `spec/07:158` „an arm made unreachable by an earlier one is not an error" ist normativ.
  Option B ist ein Spec-Bruch, nicht „verteidigbar"; Option A muss den Satz ergänzen. Neu EP-41.
- **EP-05**: `Bytecode.md:854` verbietet den Downcast ausdrücklich; der Mechanismus ist ein
  **Typindex**-Vergleich (`:856-859`), und `q11` zeigt zwei Indizes für `Pair<int>`/`Pair<string>`.
  „Kein Bruch, nur Präzisierung" war falsch. Neu EP-42.
- **EP-19**: „dann fehlt im Payload der Arm, der null benennen könnte" war falsch — `Has(null)`
  kompiliert (`q05`). Der wirkliche Preis: kein Pattern bindet den narrowten Wert (`SEM0001`),
  `q05b`/`q05c` als Kontrollen. Neu EP-35.
- **§1.1/EP-03**: Array-Längen und Einspalten-Tupel sind implementiert, aber **nicht normiert**
  (`spec/07:189-192`). Neu §1.2 (f) und EP-39.
- **EP-25**: „mit M37 scharf geworden" war falsch — M37-`dependencies` sind Quellsegmente
  (`STATUS.md:140-147`), das Format kennt nur Host-Imports (`Bytecode.md:236-250`), `pack` hat
  keinen Linker (`STATUS.md:1595-1597`). Umgeschrieben als Vorbedingung eines Binärpaketformats.
- **EP-22 Bruchbreite**: der grep liefert **einen** Treffer (`match_stmt.lyr:2`, ein Guard), nicht
  „keine Treffer". Schlussfolgerung (null Float-Patterns) hält; die Messung ist jetzt richtig
  berichtet.
- **EP-30**: `Marshal.cs:143` kennt `TypeTag.Enum` — „kein Tag" war selektiv zitiert; richtig ist
  „Tag ja, Variantensicht nein".

**Vergleichssprachen korrigiert (§2, Korrekturen 1–6, per Web-Suche nachgeschlagen):**
Rust hat Float-Patterns nie verboten, Lint 2024 entfernt (PR #116098), heute `==`-Semantik + NaN-
Fehler → **EP-22-Empfehlung kippt von „D dann A" auf B** · Swift SE-0110 ist Swift 4, parenthesierte
Tupel-Destrukturierung wieder erlaubt, `for (a, b)` nie betroffen → Swift trägt EP-12 für Tupel ·
Kotlin 1.7: Statements werden Fehler, die Zeugen-Meldung ist älter · rustc: Attribut
`#![pattern_complexity_limit]`, kein CLI-Flag · Roslyn-Timeout nicht belegbar, EP-28 C ohne Vorbild ·
Java seit 21 `MatchException` (JEP 441).

**Zwölf fehlende Designfragen eingearbeitet:** EP-34 (Typ des `match`-Ausdrucks — Regel steht in
`spec/06:130-139`, `never`-Arm fehlt dort), EP-35 (narrowter `?T`-Payload), EP-36 (Konstanten im
Pattern, `q02`), EP-37 (negative Literale, `q01`; Ranges `q08`), EP-38 (Feld-Defaults, `q07`/`q07b`),
EP-39 (Spec-Reichweite der Erschöpfung), EP-40 (Auflösung bloßer Namen in beiden Positionen, `q06`),
EP-41 (Revision des Spec-Satzes :158), EP-42 (EP-05 bei Generics/Structs, `q11`), EP-43 (Enum-Werte:
Wert oder Referenz, `q09`/`q09b`), EP-44 (Completion/Formatter, gelesen), EP-45 (Performanz:
String-`match` `q10.ir`, Rest-Kopie, Tiefe).

**Empfehlungen geändert:** EP-22 → **B** · EP-19 bleibt A, aber **nur mit EP-35 C** · EP-25 → Regel
für ein künftiges Format, kein heutiger Sweep · EP-05 → B erst nach EP-42 und mit ADR gegen
`Bytecode.md:854` · EP-02 → A mit Spec-Zusatz (EP-41) · EP-01 → E **plus** EP-40 (Ausdrucksseite)
und EP-36 (Konstanten) · §4 Sweep-Liste von vier auf sechs Posten, plus die sechs spec-first-Nachträge
als Punkt 1.

**Wo die Kritik nicht recht hatte — und die Aussage steht bleibt:** keine. Jeder der acht
`wrongClaims`, der fünf `wrongComparisons` und der zwölf `missingQuestions` hielt der Nachprüfung
stand; die Messungen `q01`–`q11` reproduzieren die Befunde des Kritikers (`b02`–`b11`) und
ergänzen sie um `q03c`–`q03e`, `q04b`, `q07b`, `q08`, `q09b`, `q10`, `q11`.

**Neu gemessen (`enums-patterns-rev3/`):** `q01` negative Literale `1 2 3` · `q02` Konstante wird
Bindung `1 1` + 2× SEM0071 · `q03` `?int`-Arm `1` · `q03b` `SEM0016` · `q03c` `throw`-Arm `1` ·
`q03d` `int8`/`int` `SEM0016` · `q03e` Kontexttyp `2` · `q04`/`q04b` `SEM0032` · `q05` `SEM0001` ·
`q05b` `0 4 -2` · `q05c` `0 5` · `q06` `SEM0002` · `q07`/`q07b` `IR0001` · `q08` `2 2` · `q09` `42` ·
`q09b` `SEM0012` · `q10` `3 0` + IR mit drei `eq` · `q11` `pair pair` + zwei Typindizes.
