# 20 — Struct-Update-Syntax (`..base`) und Tupel-Index (`t.1`)

Zugehöriger language-review-Punkt: **"Keine Struct-Update-Syntax und kein positionaler Tupel-Zugriff"** (LOW, "nur wenn Zeit bleibt").

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 17 (= −1007 auf 8 Bit) | 40 |
| `soll.lyr` | PROPOSAL | 26 |
| `vergleich.rs` | Rust (`..base`, `t.1`), Kotlin/C# als Kommentar | 20 |
| Lib-Variante | nicht möglich (Syntax) | — |

## Ist-Stand
(a) Eine Kopie mit EINEM geänderten Feld schreibt alle sechs Felder ab (`withPort`, 8 Zeilen);
die `var copy = base; copy.port = port;`-Form ist für Structs korrekt (Wertsemantik) und für
Klassen ein stiller Alias — derselbe Code, zwei Bedeutungen je nach Deklaration des Typs.
(b) Ein Tupel-Element lesen heißt alle Elemente binden (`let (_, second, _) = triple;`).

## Soll-Syntax und Grammatik (§6.2)
```
StructInit = TypePath '{' [ StructInitField { ',' StructInitField } [ ',' ] ] [ '..' Expr ] '}' .
TupleIndex = Postfix '.' IntLit .              (* eine Ebene: t.1, nicht t.0.1 *)
```
- **`..base`**: heute steht `..` nie in einem Initializer (Einträge sind `IDENT '=' Expr`), und
  `..` ohne linken Operanden ist `LYR-PAR0002` → frei. Es ist KEINE Range (§7.2 bleibt unberührt),
  sondern ein Eintrag der Feldliste — der Parser liest es in `ParseStructInit` (`Parser.cs:549-590`)
  als letztes Element. Semantik: nicht genannte Felder aus `base` (gleicher Typ); Struct = Kopie,
  Class = NEUE Instanz mit denselben Referenzen (flach; explizit dokumentieren); Basis gewinnt
  gegenüber Felddefaults (Rust-Regel). Mit Prototyp 17: außerhalb des Moduls nur, wenn alle
  nicht genannten Felder pub sind (Rust E0451 gilt auch für `..base`).
- **`t.1`**: `.1` ist kein Float-Literal (§1.5 `FloatLit = DecLit '.' DecLit` braucht Ziffern
  davor), also lext `t.1` heute als `t` `.` `1` → `LYR-PAR0003` ("expected member name") → frei.
  Rusts `t.0.1`-Falle (`0.1` als Float) wird durch "nur eine Ebene" vermieden; verschachtelt
  `(t.0).1`. Sema: Index muss Literal < Arity sein; Ergebnis der Elementtyp; **kein
  Schreibzugriff** (Tupel sind unveränderlich, §3.3). Lowering: `TupleGet i` existiert für das
  Destructuring.
- **Spec**: §6.2 (beide Produktionen), §3.3 (Tupel: "taken apart by destructuring **or indexed
  by position**"), §6 (Postfix-Tabelle).

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Kopie mit 1 Änderung (6 Felder) | 8 Z. | 1 Z. | 1 Z. |
| Kopie einer CLASS mit 1 Änderung | 8 Z. (`var`-Form wäre Alias!) | 1 Z. | 1 Z. |
| ein Tupel-Element | Destructuring + 2 `_` | `t.1` | `t.1` |

- **Fehlerklassen verhindert**: (a) vergessenes Feld beim Abschreiben — heute ein Compilerfehler
  (fehlendes Feld ohne Default), also kein stiller Bug, aber Arbeit; die `var copy`-Form auf
  einer CLASS ist der stille Alias-Bug, den `..base` als "neue Instanz" ausschließt. (b) keine.
- **Aufwand**: (a) Parser ~15 Z., Sema ~40 Z. (Typgleichheit, Feldabdeckung, pub-Regel),
  Lowering ~30 Z. (Felder aus base laden); (b) Parser ~10 Z., Sema ~20 Z., Lowering ~5 Z.
- **Breaking**: nein (beides Minor).
- **Wechselwirkungen**: Prototyp 17 (pub-Felder), Prototyp 03 (Synthese unberührt), Defaults
  (§3.2: Basis vor Default), Enums mit Struct-Variante (`Rect { w = 1, ..r }` — nur, wenn `r` vom
  selben Enum UND derselben Variante ist — nicht statisch prüfbar → für Enum-Varianten NICHT
  erlauben), Generics (`Box<int> { v = 1, ..b }` mit gleichem Instanztyp).
- **Offene Semantikfragen**: (a1) `..base` für Klassen erlauben (flache Kopie) oder auf Structs
  beschränken? Rust erlaubt es für alle; Vorschlag: erlauben, weil gerade die Class-Kopie heute
  fehleranfällig ist. (b1) `t.1 = x` erlauben, wenn `t` ein `var` ist? Nein — Tupel sind Werte
  ohne Feldzuweisung; neu bauen.

## Empfehlung
**Beides Minor, klein, geringe Priorität** — (a) ist für Klassen-Kopien mehr als Komfort (Alias-
Falle); (b) ist reiner Komfort. Nach 14 (Destructuring in for/Lambda) einordnen.
