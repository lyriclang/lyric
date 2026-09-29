# 22 — `Neg<R>` / `Rem<T, R>` und Compound-Zuweisung auf Feldern

Zugehöriger language-review-Punkt: **"Kein unäres Minus, kein `%`, kein `Neg` auf eigenen Typen; Compound-Zuweisung auf Feldern mit Interface-Operatoren verweigert"** (LOW, qol). Nicht ausdrücklich zugewiesen; klein genug für einen Prototyp.

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 6 | 28 |
| `soll.lyr` | PROPOSAL (Interfaces = stdlib, zwei Sätze in §6.1, §6.5 gelockert) | 28 |
| `vergleich.rs` | Rust `Neg`/`Rem`/`AddAssign` | 22 |
| Lib-Variante | (a) ist ZUR HÄLFTE stdlib: die Interfaces sind zwei Zeilen in `std.core`; nur die Operator-Bindung (`-v` → `neg()`, `%` → `rem()`) ist Sprache | — |

## Ist-Stand
`v.neg()` und `v.rem(w)` als Methoden — funktional gleichwertig, aber Vektor-/Geldtypen ohne
`-v` sind ungewohnt, und `%` ist der einzige arithmetische Operator ohne Interface (§6.1
"numeric-only", CHANGELOG v1.5.0 "deliberately"). `b.pos += b.vel` ist `LYR-SEM0003` (§6.5:
Feldziel mit Interface-Operator), also `b.pos = b.pos + b.vel` — bei tieferen Pfaden
(`world.bodies[i].pos = world.bodies[i].pos + …`) wird der Receiver zweimal geschrieben, was
genau das ist, was die Spec vermeiden wollte — nur jetzt sichtbar vom Autor statt vom Compiler.

## Soll
- **(a) Interfaces** — stdlib: `pub interface Neg<R> { fn neg(): R; }`,
  `pub interface Rem<T, R> { fn rem(other: T): R; }` nach dem Add-Muster; `extend int/uint/
  float :: [Neg<…>, Rem<…>]` für die Builtins, damit `T :: [Neg<T>]` generisch funktioniert.
  Sprache (§6.1): "`-a` on a non-primitive type is `a.neg()` through `Neg<R>`; `%` follows the
  `Add` rule". Keine Grammatikänderung (prefix `-` und `%` existieren, Präzedenz unverändert).
  Sema: der Operator-Dispatch für Add/Sub/Mul/Div (`TypeChecker`, Conformance nach rechtem
  Operanden) bekommt zwei weitere Einträge (~30 Z.); Lowering: dieselbe Methodenaufruf-Synthese.
  Eine Falle: `-x` auf einem LITERAL faltet heute ins Literal (§3.1 "a leading `-` folds into
  the literal") — unverändert, weil Literale primitiv sind.
- **(b) Compound auf Feld/Element** (§6.5): erlauben, mit der Spec-Zusage "receiver and index
  are evaluated exactly once". Lowering: Receiver/Index in Temps (`FunctionLowerer`
  `_chainReceivers`-Mechanismus für `?.`-Ketten zeigt das Muster), dann `load → op → store`.
  Für STRUCT-Felder (Wertsemantik) muss der Rückschreibpfad derselbe sein wie bei
  `b.pos = …` (die Lowering kennt ihn: `mut fn` auf Struct-Receivern schreibt zurück, Guide 5).
  ~60 Z. Lowering, ~10 Z. Sema (SEM0003-Fall entfernen).

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Negation | `v.neg()` | `-v` | `-v` |
| Rest | `v.rem(w)` | `v % w` | `v % w` |
| Feld akkumulieren | `b.pos = b.pos + b.vel` (Pfad 2×) | `b.pos += b.vel` | `b.pos += b.vel` |

- **Fehlerklassen verhindert**: (b) Tippfehler zwischen den zwei Pfad-Kopien
  (`a.x = b.x + w` — die häufigste Form des Copy-Paste-Fehlers); (a) keine harte.
- **Aufwand**: (a) stdlib ~20 Z., Sema ~30 Z., Lowering ~10 Z.; (b) Sema ~10 Z., Lowering ~60 Z.
- **Breaking**: nein (beides Minor). `%` auf eigenen Typen war bisher ein Fehler; `-v` ebenso.
- **Wechselwirkungen**: Generics (`T :: [Neg<T>]` — Builtin-Konformanzen nötig, sonst kann
  generischer Code nicht negieren); Literale (§3.1 unverändert); Opaque-Aliase (§3.5: Operatoren
  bleiben refused — konsistent); Prototyp 03 (Synthese: `Neg` NICHT synthetisieren — es gibt keine
  kanonische Negation eines Structs); Optionals (`-opt` ist ein Fehler wie `opt + 1`).
- **Offene Semantikfragen**: (b1) Compound auf `xs[i]` mit Index-Ausdruck mit Seiteneffekt
  (`xs[next()] += 1`) — "genau einmal" ist dann beobachtbar und gewollt (Rust/C#-Verhalten).
  (a1) Ein `Neg<R>` mit `R != Self` (z.B. `Neg<float>` auf einem Winkel-Typ) — erlaubt wie bei
  `Mul<float, Vec2>`.

## Empfehlung
**(a) stdlib + zwei Sätze Sema, Minor, klein. (b) Minor, klein** — beide bewusst entschieden
(v1.5.0), daher LOW; die Kosten der Entscheidung sind aber gering und der Nutzen für
numerische Typen sichtbar.
