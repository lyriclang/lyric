# `?T == ?T`: zwei Optionals vergleichen

**Status:** designt (klein; Kandidat für die nächste Implementierungsrunde)
**Version:** Minor (4.5) — additiv; heute ist `a == b` für zwei `?int` `LYR-SEM0059`
**Spec:** §6.2 (ein Absatz), Appendix A (SEM0059-Text) · **Guide:** 09
**Abhängigkeiten:** keine. **Voraussetzung** für `design/conformance-synthesis.md` (ein `?T`-Feld muss in einem synthetisierten `equals` vergleichbar sein) und für stdlib-redesigns `assertEq(parseInt("x"), null)` / `assertNull`.
**Abgestimmt mit:** stdlib-redesign (angefordert, „brauche ich für assertEq/assertNull")

## Motivation

```lyr
let a: ?int = parseInt(s);
let b: ?int = parseInt(t);
if (a == b) { … }        // 4.4: LYR-SEM0059 — "'==' is not defined for '?int'"
```

Erlaubt ist heute nur der Vergleich **gegen `null`** (`a == null`), der kein Vergleich ist, sondern die
Präsenzfrage (`optissome`). Zwei Optionals vergleicht man mit:

```lyr
if ((a == null && b == null) || (a != null && b != null && a! == b!)) { … }
```

— vier Tests, zwei `!`-Operatoren, und wer einen Zweig vergisst, bekommt kein Signal. Die Testbibliothek
kann `assertEq(parseInt("x"), null)` nicht anbieten, und ein synthetisiertes `equals` (Prototyp 03) kann
kein `?T`-Feld behandeln.

## Syntax

Keine Änderung. `==` und `!=` sind bereits definiert (§6.2), nur für dieses Operandenpaar nicht.

## Semantik

**§6.2 neue Regel:** `a == b` für `a: ?T`, `b: ?U` ist definiert, wenn `T` und `U` denselben Typ haben
**und** `T` `Equatable<T>` erfüllt (oder ein Primitiv ist). Bedeutung:

| `a` | `b` | `a == b` |
|---|---|---|
| `null` | `null` | `true` |
| `null` | Wert | `false` |
| Wert | `null` | `false` |
| `x` | `y` | `x == y` (die Regel für `T`) |

Das ist **Kleene-frei**: kein dreiwertiges `null == null → null` wie in SQL. Jede Sprache mit Optionals
entscheidet sich hier, und alle acht Vergleichssprachen antworten `true` (SQL ist die Ausnahme, und sie
ist berüchtigt dafür).

**Gemischt `?T == T`** (`a == 5` für `a: ?int`): Vorschlag **erlauben**, mit der Bedeutung
„`a` ist vorhanden und gleich 5". Heute ist das ebenfalls SEM0059 („an optional compared against a
non-`null` value"). Begründung: die Alternative (`a != null && a! == 5`) ist dieselbe Boilerplate, und
das Narrowing macht die Form nicht kürzer. Gegenargument (§7.4-Geist): eine Abwesenheit soll sichtbar
behandelt werden. **Entscheidung: erlauben**, weil der Ausdruck seine Antwort vollständig gibt (`false`
bei Abwesenheit) und nichts verschluckt — anders als `f"{opt}"`, wo eine Abwesenheit im Text verschwände.

**Ordnung (`<`, `<=`, …) auf Optionals: NEIN.** Rust und Swift definieren sie (`None < Some(_)`), und das
ist die Quelle stiller Sortierfehler: eine Liste `?int` sortiert Abwesenheiten nach vorn, ohne dass
jemand das entschieden hat. Wer sortieren will, entscheidet mit `?? min` oder partitioniert.

### Lowering

Drei Blöcke, kein neuer Opcode:

```
t0 = optissome a ; t1 = optissome b
cmp = t0 == t1                       // beide vorhanden oder beide abwesend?
if (!cmp) → false
if (!t0)  → true                     // beide null
x = optget a ; y = optget b ; → (x == y)
```

`optget` kann hier nicht panicken — der Beweis steht im `optissome` davor, dieselbe Arbeitsteilung wie
beim Flow-Narrowing. `!=` ist die Negation (`UnOp Not`, wie bei jedem Interface-`equals`).
**~40 Zeilen in `FunctionLowerer`**, direkt neben `TryLowerNullTest` (das die `== null`-Form abfängt und
unverändert bleibt).

## Wechselwirkungen

- **Narrowing (§7.4):** `a == b` narrowt **nichts** — im Gegensatz zu `a == null`/`a != null`, die die
  Präsenzfrage sind. Das muss explizit in §6.2 stehen, sonst erwartet jemand nach `if (a == b)` ein
  narrowtes `a`. (`TryLowerNullTest` und `NarrowingFacts` prüfen beide auf ein `NullLiteralExpr` als
  Operand — die neue Regel greift nur, wenn **keiner** der Operanden das Literal `null` ist, also
  kollidiert sie nicht.)
- **Konformanz-Synthese:** ein `?T`-Feld ist damit im synthetisierten `equals` behandelbar; die beiden
  Features sollten **zusammen** ausgeliefert werden.
- **`Equatable<?T>` als Konformanz?** Nein — die Regel lebt im Operator (§6.2), wie die
  Primitiv-Gleichheit. Eine Konformanz `extend<T> ?T :: [Equatable<?T>]` bräuchte bedingte Konformanz auf
  Optionals und würde `?T` in Constraint-Positionen (`Map<?int, V>`) einschleusen — das ist eine andere,
  größere Entscheidung (Map-Schlüssel dürfen nicht abwesend sein).
- **Optionals von Optionals:** `?` nestet nicht (§3.3), also gibt es den Fall nicht.
- **Coroutinen/throws/Patterns/Formatter/LSP:** keine.

## Breaking

Nein (Minor). Beide Formen sind heute Fehler.

## Aufwandsschätzung

| Ebene | Aufwand |
|---|---|
| Parser | 0 |
| Sema | klein (~30 Z. in `CheckBinary`/`CheckEquality`: Optional-Paar erkennen, Innentypen vergleichen, `Equatable` prüfen, Narrowing-Pfad ausschließen) |
| Lowering | klein (~40 Z.) |
| VM/Bytecode | 0 |
| stdlib | `assertEq` nimmt Optionals, `assertNull`/`assertNotNull` (stdlib-redesign) |
| Spec/Guide | §6.2 ein Absatz, Guide 09 ein Beispiel |

## Vergleich

| Sprache | `null == null` | gemischt | Ordnung auf Optionals |
|---|---|---|---|
| **Rust** | `None == None` → `true` (`PartialEq for Option<T> where T: PartialEq`) | nein (`Option<T>` vs `T` ist ein Typfehler) | **ja**, `None < Some(_)` |
| **Swift** | `nil == nil` → `true` | **ja**, `opt == 5` ist erlaubt (Optional-Promotion) | ja (bis Swift 5 warnend, seit 5.3 eingeschränkt — genau wegen der Sortierfallen) |
| **Kotlin** | `null == null` → `true` | ja (`a == 5` auf `Int?`) | nein (`Comparable` nur auf non-null) |
| **C#** | `null == null` → `true`; `Nullable<T>` hat „lifted operators" | ja | „lifted" `<` ergibt **`false`** bei null — eine eigene Falle |
| **TypeScript** | `undefined === undefined` → `true` | ja | n/a |
| **Go** | `nil == nil` für Pointer → `true` | ja | n/a |
| **SQL** | `NULL = NULL` → **NULL** (dreiwertig) | — | — |

Rust:
```rust
let a: Option<i32> = "1".parse().ok();
let b: Option<i32> = "x".parse().ok();
assert!(a != b);                  // Some(1) != None
assert_eq!(None::<i32>, None);    // true
```
Swift:
```swift
let a: Int? = Int("1"), b: Int? = Int("x")
if a == b { … }                   // false
if a == 1 { … }                   // true — Optional-Promotion des rechten Operanden
```

**Fallen, die Lyric vermeidet:** (1) SQLs dreiwertige Logik — niemand will sie in einer
Programmiersprache; (2) C#' „lifted `<`", das bei `null` still `false` liefert und damit `a < b` und
`a >= b` gleichzeitig falsch macht; (3) Rusts/Swifts Ordnung auf Optionals, die Sortierungen still
umordnet. Lyric definiert **nur Gleichheit**.

**Empfehlung: Kotlin-Modell** — Gleichheit über Optionals inklusive der gemischten Form, **keine**
Ordnung. Das ist die einzige Variante, die keine der drei Fallen hat, und sie passt zu Lyrics
Narrowing-Kultur: `<` auf einer Abwesenheit hat keine sinnvolle Antwort, `==` schon.

## Offene Fragen

1. Gemischt `?T == T` wirklich erlauben? (Vorschlag ja; die Gegenposition ist der §7.4-Geist.)
2. Soll eine **Warnung** kommen, wenn beide Operanden statisch nie gleichzeitig vorhanden sein können?
   Nein — zu spitzfindig.
3. `assertEq` in `std.test` braucht Display für Optionals, um bei Ungleichheit etwas zu drucken —
   hängt an `design/conditional-conformance.md` Frage 4 (`?T :: [Display]`, dort mit **nein** beantwortet).
   stdlib-redesign muss dann `showOptional` von Hand anbieten.

## Spec-Diff (§6.2)

```diff
 `==` and `!=` on a non-primitive type are the `Equatable<T>` method call (§6.0). An optional
-compared against anything but `null` is refused (`LYR-SEM0059`).
+compared against `null` is the PRESENCE question, not a comparison (§6.3). Two optionals of the
+same inner type compare when that type does: both absent is `true`, one absent is `false`, and
+two present values compare by their own rule. An optional against a value of its inner type
+asks "present and equal". Neither form narrows: presence is settled by the `null` comparison
+alone. ORDERING is not defined on optionals — `<` on an absence has no answer, and a silent
+one would reorder a sort without anybody deciding it.
```
Appendix A: SEM0059-Text auf „an optional compared against a value of another type, or a type without `Equatable`" ändern.
