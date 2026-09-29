# 16 — Optional-Gleichheit `?T == ?T`; Narrowing von Feldpfaden (Skizze)

Zugehöriger language-review-Punkt: **"Narrowing erreicht keine Felder und keine Ausdrücke; zwei Optionals sind unvergleichbar"** (MEDIUM). Wie gewünscht: (a) prototypisiert, (b) nur skizziert.

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 36 |
| `soll.lyr` | PROPOSAL (a) + Skizze (b) | 26 |
| `vergleich.swift` | Swift (`Optional: Equatable`), C# als Kommentar (lifted equality) | 20 |
| Lib-Variante | (a) NICHT generisch schreibbar: `a == b` auf `?T` ist in generischem Code ebenso SEM0059 — nur eine Hilfsfunktion pro konkretem Typ (ist.lyr) | — |

## Ist-Stand
Zwei `?int` vergleichen ist `LYR-SEM0059` (`TypeChecker.cs:1982-2005`); der Umweg ist eine
dreizeilige Hilfsfunktion **pro Elementtyp** (`sameInt`, `sameString`), weil die generische
Form dieselbe Sperre trifft. Ein Struct mit drei optionalen Feldern braucht also drei Helfer +
einen Vergleicher. Das ist auch der Grund, warum Prototyp 03 (synthetisiertes `equals`) eine
eigene Regel für `?T`-Felder bräuchte — (a) macht sie überflüssig. Feld-Narrowing: `c.port`
narrowt nicht (§7.4 "not a field"), Kopie in ein Lokal.

## Soll (a): `?T == ?T` — Spec §6.2
- Tabelle: `null == null` → true; `null == v`, `v == null` → false; `v == w` → `v.equals(w)`
  (bzw. Skalar-Gleichheit). `!=` negiert. Voraussetzung `T :: [Equatable<T>]` oder Skalar.
  `?T == T` erlaubt (Widerung §3.7). **Ordnung bleibt Fehler** — Swift 3 hat `<` auf Optionals
  entfernt, weil `nil < 0` keine natürliche Antwort hat; Lyric sollte den Fehler behalten.
- Keine Grammatikänderung. Sema: die drei SEM0059-Stellen werden zu einer Typregel (Ergebnis
  `bool`, Operanden-Constraint prüfen). Lowering: Desugar in `OptIsSome`/`OptGet` (existieren,
  STATUS.md M36) + `equals`-Aufruf — ~40 Z. VM 0.
- **Wechselwirkung mit §7.4**: `p == q` narrowt NICHTS (nur `== null` beweist eine Tatsache);
  `p == null` bleibt die Narrowing-Form und ist ein Spezialfall der Tabelle — konsistent.
- Damit wird `fn same<T :: [Equatable<T>]>(a: ?T, b: ?T)` schreibbar, und `std.option.contains`
  (`contains(o: ?T, value: T)`) bekommt eine natürliche Kurzform `o == value`.

## Soll (b): Feldpfad-Narrowing — Skizze, Spec §7.4
- Ein Pfad `x.f` narrowt wie ein Identifier, wenn `x` eine `let`-Bindung (oder Parameter) eines
  STRUCT-Typs ist und `f` ein Feld: Wertsemantik (§3.4) garantiert, dass kein Alias `x.f`
  schreiben kann, und `let` verhindert Neubindung. Invalidierung: Zuweisung an `x.f`
  (nur bei `var x` möglich → dann nicht narrowbar) — also praktisch keine Invalidierung nötig.
- Für CLASS-Felder NICHT: ein Aufruf zwischen Prüfung und Nutzung könnte das Feld über einen
  Alias leeren; TypeScript nimmt das Risiko (unsound), Kotlin erlaubt Smart Casts nur auf `val`
  ohne Custom-Getter im selben Modul. Lyric-konform ist die Grenze "nur Struct-Felder von let".
  Klassen bekommen `let … else` / `if let` (Prototyp 02).
- Sema: der Narrowing-Schlüssel wird von `LocalSymbol` auf `(LocalSymbol, FieldPath)`
  erweitert (`FlowAnalyzer`/Narrowing-Tabelle); Lowering: der Lesezugriff wird zum
  checked-unwrap (§7.4 "compiles to a checked unwrap") — Mechanismus existiert. ~120 Z. Sema.
- Verschachtelte Pfade (`a.b.c`) rekursiv nach derselben Regel (alle Glieder Struct-Felder).

## Bewertung
| | Ist | Soll (a) | Swift |
|---|---|---|---|
| Vergleich von 3 optionalen Feldern | 3 Helfer (9 Z.) + 1 Z. | `==` (0 Z. Helfer) | `==` |
| generischer Optional-Vergleich | unmöglich | 1 Z. | 1 Z. |
| Feld-Narrowing | Kopie (1 Z.) | (b): 0 Z. | Kopie/`if let` |

- **Fehlerklassen verhindert**: (a) Helfer, die `null == null` falsch beantworten (die naive
  Form `a != null && b != null && a == b` sagt false für zwei nulls — ein Bug in genau der
  Umgehung, die language-review als Repro zeigt); (b) Kopien, die nach einer Mutation veralten.
- **Aufwand**: (a) Sema ~40 Z., Lowering ~40 Z., Spec ein Absatz — klein. (b) Sema ~120 Z.,
  Spec ein Absatz mit der Soundness-Begründung — mittel.
- **Breaking**: (a) nein — `?T == ?T` war ein Fehler; (b) nein.
- **Wechselwirkungen**: Prototyp 03 (Synthese über `?T`-Felder wird trivial); `std.option.contains`;
  match (`null`-Arm unverändert); Generics (Constraint `Equatable<T>` reicht, §8.2); Hashable —
  `?T` bleibt NICHT Hashable (Map-Schlüssel `?K` wäre stdlib-Frage, §7.2-Grenze für `?T` in
  Containern bleibt).
- **Offene Semantikfragen**: (a1) `?T == ?U` mit `T :: [Equatable<U>]` (Mehrfach-Konformanz,
  §5.1)? Ja, dieselbe Auswahl nach rechtem Operanden wie §6.1. (b1) Narrowing über
  `this.f` in einer nicht-`mut` Methode eines Structs? Ja — `this` ist dort ein `let`.

## Empfehlung
**(a) Sprachregel, Minor, klein — empfohlen.** (b) als Folgeschritt nach Prototyp 02 (let-else
deckt Klassen ab; (b) deckt Structs elegant ab).
