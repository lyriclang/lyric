# 09 — Slices / Teilbereiche (`xs[a..b]`)

Zugehöriger language-review-Punkt: **"Keine Slices/Teilbereiche für Arrays und Strings"** (MEDIUM).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 5 | 38 |
| `soll.lyr` | PROPOSAL | 26 |
| `vergleich.py` | Python (Kopie-Semantik), Rust/Go als Kommentar (View-Semantik, bewusst nicht) | 18 |
| `probe-range-index.lyr` | zeigt die heutige Diagnose für `xs[1..3]` | — |
| Lib-Variante | **ja, teilweise**: `std.collections.slice<T>(xs: T[], from: int, to: int): T[]` und `string.slice(from, to)` decken 90 % — an stdlib-review gemeldet | — |

## Ist-Stand
Ein Teilarray ist Dummy-Element + Länge + Kopierschleife (`sliceChars`, 3 Zeilen, Off-by-one an
zwei Stellen: `to - from` und `k - from`). Die `first k`/`last k`-Fälle in `main` wiederholen das
Muster. Für Strings verlangt `substring(start, COUNT)` das Rechnen `length - 7` beim Leser, wo
`(from, to)` die Absicht direkt sagt. `char[] → string` hat die stdlib (`fromChars`), das ist ok.

## Soll-Syntax und Grammatik
```
IndexSuffix = '[' ( Expr | SliceRange ) ']' .
SliceRange  = [ Expr ] ( '..' | '..=' ) [ Expr ] .
```
- **Keine Parser-Kollision**: `..` ist heute Präzedenzstufe 7 und parst überall als `RangeExpr`
  (`Parser.cs:74,113-118`), die Sema lehnt jeden Wert-Kontext ab (§7.2). `xs[1..3]` wird also
  bereits geparst und dann abgelehnt (`probe-range-index.lyr`). Die Änderung ist eine Sema-Regel
  ("ein RangeExpr direkt in einer Index-Klammer ist ein Slice") plus die offenen Formen
  `[..b]`/`[a..]`/`[..]`, die der Parser neu lernen muss (`..` ohne linken Operanden ist heute
  `LYR-PAR0002`; nur in der Index-Klammer erlauben).
- **Semantik**: Kopie (Wertsemantik wie Arrays heute — §3.3 "reference values of fixed length":
  ein Slice ist ein NEUES Array; das vermeidet Go's Aliasing-Überraschungen und Rusts Lifetimes).
  Grenzen: `0 ≤ a ≤ b ≤ length`, sonst Panik wie ein Index außerhalb (§3.3). `..=b` ist `..b+1`,
  mit `b = length - 1` maximal. Typ `T[]`. §7.2 bleibt: eine Range ist kein Wert, sie kommt an
  ZWEI Stellen vor (Schleifenkopf, Index-Klammer).
- **Strings**: kein `s[a..b]` — §3.3 hält `string` bewusst unindexierbar (O(n) pro Position).
  Der Weg bleibt `s.toChars()[a..b]` (sichtbare O(n)-Materialisierung) oder eine
  stdlib-Methode `s.slice(from, to)` (O(n), aber EIN Durchlauf statt Materialisierung + Kopie).
- **Spec**: §3.3 (Arrays: Slice-Absatz), §6.1/§6.2 Grammatik (IndexSuffix), §7.2 (zweite
  Range-Stelle). Lowering: Desugar in Länge + `[default] * n`-Allokation + Kopierschleife — oder
  eine IR-Op `ArraySlice` (die VM hat `ArrayCopy`-ähnliches für `+`/`*` auf Arrays, §6.1).

## Bewertung
| | Ist | Soll | Lib (`slice(xs, a, b)`) | Python |
|---|---|---|---|---|
| Wort ausschneiden | 3 Z. + Hilfsfunktion (6 Z.) | 1 Z. | 1 Z. | 1 Z. |
| first/last k | 2 × 2 Z. | 2 × 1 Z. | 2 × 1 Z. | 2 × 1 Z. |
| Präfix abschneiden | `length - 7` rechnen | `[7..]` | `slice(7, len)` | `[7:]` |

- **Fehlerklassen verhindert**: Off-by-one in der Kopierschleife (zwei Subtraktionen); Dummy-Element
  falschen Typs; `substring(start, count)` mit falsch berechnetem count (der Klassiker
  `substring(7, arg.length())` läuft über das Ende).
- **Aufwand**: Parser klein (~20 Z. für offene Grenzen); Sema klein (~40 Z.: Range in Index
  → Slice-Typ `T[]`); Lowering klein-mittel (~50 Z. Desugar oder ~30 Z. + neue IR-Op); VM 0
  (Desugar) oder klein (Op); stdlib: `slice` für Arrays/Strings ist die 90-%-Lösung ohne
  Sprachänderung (~15 Z.).
- **Breaking**: nein (Minor).
- **Wechselwirkungen**: `for (x in xs[1..])` — funktioniert, ist aber eine Kopie pro Schleife
  (dokumentieren; `std.iter.skip` bleibt die kopierfreie Form); Optionals (`(?T)[]` sliced wie
  jedes Array); Generics: keine; match: `RangePattern` (§7) ist syntaktisch verwandt, semantisch
  unabhängig — bleibt so.
- **Offene Semantikfragen**: (1) negative Indizes wie Python (`xs[-1..]`)? Nein — Lyric hat sie
  bei `xs[i]` auch nicht. (2) `a > b` → Panik (Vorschlag) oder leeres Array (Python)? Panik,
  konsistent mit §7.2 ("range with lo > hi is empty" gilt für Schleifen, ein Slice mit
  vertauschten Grenzen ist fast immer ein Fehler). (3) `List<T>` — bekommt `slice` als Methode
  in der stdlib, kein Operator (List ist kein Builtin).

## Empfehlung
**Erst stdlib** (`slice` auf `T[]`, `string`, `List<T>` — deckt 90 %, kein Sprachrisiko), dann
das Sprachfeature `xs[a..b]` als Minor, wenn die Form sich bewährt. → stdlib-review informiert.
