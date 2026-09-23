# Konformanz-Synthese: `Equatable`, `Hashable`, `Ordered`, `Display` ohne Body

**Status:** designt (kein Prototyp — Lowering-Teil ist der größte Einzelposten meiner Liste)
**Version:** Minor (4.5) — additiv; heute ist jede dieser Konformanzen ohne Methode `LYR-SEM0020`
**Spec:** §5.1 (neuer Abschnitt „synthesized conformance"), §11 (Anker-Liste), Appendix A (SEM0020-Text, ein neuer Code) · **Guide:** 07
**Abhängigkeiten:** keine harte. Multipliziert sich mit `design/fstring-display.md` (synthetisiertes `show()` macht jedes Struct/Enum im f-String renderbar) und mit `design/conditional-conformance.md` (generische Typen). Braucht `std.core.combineHash(seed, value): int` (stdlib-redesign hat ihn angelegt).
**Abgestimmt mit:** stdlib-redesign (Anker `combineHash`, Wunschliste `IoErrorKind`, `JsonValue`, `Wait`, `Result<T,E>`), macro-abi (Stufe 1 ihres Makro-Designs referenziert genau diese Tabelle; ToJson/FromJson als fünfte/sechste Zeile desselben Schemas)

## Motivation

Ein Wertetyp mit drei Feldern, der Map-Schlüssel, sortierbar und druckbar sein soll, kostet heute **vier
Methoden und ~30 Zeilen**, die alle dieselbe Feldliste wiederholen (Prototyp 03: 72 → 38 Zeilen). Für ein
Enum mit Payload ist `equals` ein verschachteltes `match` über beide Seiten, quadratisch in der
Variantenzahl. Drei Fehlerklassen entstehen dabei still:

1. **`hash` passt nicht zu `equals`** — ein Feld in einem der beiden vergessen. `core.lyr:156` sagt
   ausdrücklich, dass das niemand prüft. Symptom: „Map verliert Einträge".
2. **Ein neues Feld wird in `equals`/`show`/`hash` nicht nachgezogen.**
3. **`compare` inkonsistent zu `equals`** (`compare == 0`, aber `!equals`) — bricht `sortList` und `Set`
   gemeinsam.

Belege aus der stdlib: `IoErrorKind`, `JsonValue` und `Wait` sind heute nicht `Equatable`, obwohl jeder
Nutzer sie vergleichen will.

## Syntax

**Keine Grammatikänderung.** Ein Struct, eine Klasse oder ein Enum deklariert die Konformanz und schreibt
die Methode NICHT:

```lyr
struct Coord :: [Hashable<Coord>, Ordered<Coord>, Display] {
    x: int,
    y: int,
    name: string,
}
```

Heute ist das `LYR-SEM0020` („does not implement 'equals'"); genau diese Stelle
(`TypeChecker.cs:714`) wird zur Synthese-Stelle.

**Verworfene Alternativen:** `@Derive { … }` scheitert an §4.7 (ein Attribut beschreibt und tut nichts) und
daran, dass `AttrArgs` keine Typen trägt (`Equatable<Coord>`). `:: [derive Equatable<Coord>]` mit
kontextuellem Wort wäre expliziter — der Preis ist ein neues Wort für etwas, das die Konformanz ohne Body
schon eindeutig ausdrückt. Swift zeigt, dass die implizite Form nicht überrascht: wer `equals` schreibt,
bekommt seins; wer nicht, bekommt das Offensichtliche.

## Semantik

### Das Schema (eine Regel, vier Instanzen)

Die Synthese ist **feldweise in Deklarationsreihenfolge**; eine geschriebene Methode gewinnt (wie bei
Default-Methoden, Guide 7 „its own member wins"); ein Enum behandelt zuerst den **Tag**, dann die
Payload-Felder in Deklarationsreihenfolge; Unit-Varianten sind nur Tag.

| Interface | Anforderung | Synthese je Feld `f` | Kombination | Enum |
|---|---|---|---|---|
| `Equatable<Self>` | `fn equals(other: Self): bool` | `this.f == other.f` | `&&`-Fold, Kurzschluss | Tag gleich **und** Payload feldweise gleich |
| `Hashable<Self>` | `fn hash(): int` (impliziert `Equatable`) | `f.hash()` | `combineHash(seed, …)`-Fold, `seed = 17` | `combineHash` über Tag, dann Payload |
| `Ordered<Self>` | `fn compare(other: Self): int` | `this.f.compare(other.f)` | erster Nicht-Null-Wert gewinnt | Tag-Reihenfolge (Deklarationsreihenfolge), dann Payload |
| `Display` | `fn show(): string` | `f.show()` bzw. `fromXxx(f)` | Initializer-Syntax: `Coord { x = 1, y = 2, name = "a" }` | `Shape.Rect { w = 2, h = 3 }`, Unit: `Shape.Dot` |

Dieses Schema ist bewusst als **allgemeines** Formular formuliert (macro-abi baut darauf): eine künftige
`ToJson`/`FromJson`-Synthese ist die fünfte und sechste Zeile derselben Tabelle, mit derselben
Reihenfolge-, Vorrang- und Enum-Regel.

### Voraussetzungen und Fehler

- **Jedes Feld muss die Eigenschaft selbst haben.** Sonst Fehler **am Feld**, nicht am Typ:
  `LYR-SEM-neu: 'Coord' cannot synthesize 'equals': field 'pos' of type 'Point' is not Equatable<Point>`.
  Das ist die wichtigste Diagnose-Entscheidung: der heutige SEM0020 zeigt auf die Konformanzliste, die
  Ursache steht aber im Feld.
- **Generische Typen brauchen die Constraint geschrieben** — nichts implizit:
  `struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>]`. Rust fügt den Bound automatisch hinzu und
  erzeugt damit die bekannte Falle, dass `#[derive(Clone)]` auf `Rc<T>` einen unnötigen `T: Clone` fordert.
  Lyric-typisch ist: nichts passiert implizit.
- **Optionale Felder:** `?T` ist `Equatable`, wenn `T` es ist (`null == null` wahr, `null == v` falsch).
  Heute ist `?T == ?T` selbst `LYR-SEM0059` (siehe `design/optional-equality.md`) — die Synthese erzeugt
  diese Regel in ihrem Körper, und beide Features sollten zusammen ausgeliefert werden.
- **Array-Felder:** `T[]` ist heute keine Konformanz. Vorschlag: refusen mit Hinweis (dieselbe neue
  Diagnose), bis Arrays eine Konformanz haben. Elementweiser Vergleich wäre eine stille
  O(n)-Entscheidung in einem `==`.
- **Klassen:** `derive Equatable` auf einer `class` ist **Wert**gleichheit, nicht Referenzgleichheit.
  Erlaubt, aber im Guide ausdrücklich zu dokumentieren — C# hat hier mit `record class` vs `class` eine
  jahrelange Verwirrung erzeugt.
- **Mehrfachkonformanz (§5.1, seit 3.0):** `Tag :: [Equatable<Tag>, Equatable<int>]` — nur die
  Konformanz auf **`Self`** wird synthetisiert; jede andere Argumentbelegung muss geschrieben werden
  (es gibt keine feldweise Regel für „gleich einem `int`").

### Lowering

Die Synthese erzeugt **AST-Methodenkörper** vor der IR-Senkung (nicht IR direkt): damit laufen
Monomorphisierung, Devirtualisierung, Inlining und der Verifier über denselben Code wie über geschriebene
Methoden, und die Fehlerpfade (fehlende Konformanz eines Feldtyps) melden sich in der Sema statt im IR.
Das ist der größte Einzelposten (~250 Z.), und für Enums die meiste Arbeit.

Kein neuer Opcode, Bytecode-Format unverändert.

## Wechselwirkungen

- **f-Strings** (implementiert): ein synthetisiertes `show()` macht `f"{p}"` ohne Handarbeit möglich.
- **Operatoren (§6.2):** `==`, `<` usw. sind Interface-Methodenaufrufe — sie finden die synthetisierte
  Methode wie eine geschriebene; null Änderung an der Operator-Auflösung.
- **Map/Set:** `K :: [Hashable<K>]` erfüllt sich durch die Synthese; die hash/equals-Konsistenz ist
  **strukturell** garantiert statt per Kommentar erbeten.
- **Deprecation/Attribute:** unberührt.
- **LSP:** „Go to definition" auf eine synthetisierte Methode hat kein Ziel — Vorschlag: auf die
  Konformanzliste zeigen. **DocGen:** die synthetisierte Methode erscheint mit dem Vermerk „synthesized".
- **macro-abi Stufe 2 (`comptime`):** setzt auf dieser Tabelle auf, konkurriert nicht.

## Breaking

Nein (Minor). Ein Programm, das heute kompiliert, hat die Methoden geschrieben — und geschriebene
Methoden gewinnen.

## Aufwandsschätzung

| Ebene | Aufwand |
|---|---|
| Lexer/Parser | 0 |
| Sema | mittel: Synthese-Entscheidung an der SEM0020-Stelle, Feldtyp-Prüfung über die bestehende `Satisfies`-Maschinerie, Kollision mit eigener Methode, neue Diagnose am Feld (~120 Z.) |
| Lowering (AST-Synthese) | **groß**: vier Körper-Generatoren, Enum-Fall mit match über beide Seiten (~250 Z.) |
| VM / Bytecode | 0 |
| stdlib | `combineHash` (liegt vor), Konformanzlisten von `IoErrorKind`, `JsonValue`, `Wait`, `Result` ergänzen |
| Spec/Guide | §5.1 ein Abschnitt mit der Tabelle, Guide 07 ein Abschnitt |

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| **Swift** | `struct P: Hashable {}` — Konformanz ohne Body, Compiler synthetisiert `==` und `hash(into:)` | **Vorbild.** Keine eigene Syntax; jede geschriebene Methode gewinnt. Synthese nur, wenn alle gespeicherten Properties konform sind — sonst Fehler mit Nennung des Members. |
| **Rust** | `#[derive(PartialEq, Eq, Hash, PartialOrd, Ord, Debug, Clone)]` | Explizit sichtbar, aber ein Attribut, das Code erzeugt — was §4.7 gerade ausschließt. Fügt Bounds implizit hinzu (`T: Clone` auch wo unnötig) — bekannte Falle. |
| **Kotlin** | `data class P(val x: Int)` erzeugt `equals`/`hashCode`/`toString`/`copy`/`componentN` | Alles-oder-nichts an einem Klassen-Modifier: wer nur `equals` will, bekommt trotzdem `copy` und Destrukturierung. |
| **C#** | `record` / `record struct` | Wie Kotlin am Typ-Modifier; `ToString` erzeugt `P { X = 1 }` — **die Initializer-Form, die ich für `Display` vorschlage**. |
| **Go** | `==` ist strukturell eingebaut (vergleichbare Felder), kein Hash-Interface | Keine Synthese nötig, aber auch keine Kontrolle: ein Feld vom Typ Slice macht den ganzen Typ unvergleichbar — erst zur Compile-Zeit der VERWENDUNG sichtbar. |
| **Python** | `@dataclass(eq=True, frozen=True, order=True)` | Feingranular, aber zur Laufzeit; `__hash__` wird bei `eq=True, frozen=False` auf `None` gesetzt — die hash/equals-Konsistenz wird erzwungen, indem Hashing abgeschaltet wird. |

Swift:
```swift
struct Coord: Hashable, Comparable, CustomStringConvertible {
    let x: Int; let y: Int; let name: String
    static func < (a: Coord, b: Coord) -> Bool { (a.x, a.y, a.name) < (b.x, b.y, b.name) }
    var description: String { "Coord(\(x), \(y), \(name))" }
}   // == und hash(into:) synthetisiert; < und description hier von Hand, weil Comparable nicht synthetisiert
```
Rust:
```rust
#[derive(PartialEq, Eq, Hash, PartialOrd, Ord, Debug)]
struct Coord { x: i64, y: i64, name: String }
```

**Fallen, die Lyric vermeidet:** (1) Kotlins/C#' „Alles-oder-nichts" am Typ-Modifier — Lyric leitet pro
Interface ab, also genau das, was in der Liste steht; (2) Rusts implizite Bounds; (3) Rusts `derive` als
Attribut, das Code erzeugt — Lyrics Attribute bleiben Daten (macro-abi baut seine Stufe 2 bewusst auf
einem anderen Schlüsselwort auf); (4) Pythons Laufzeit-Lösung; (5) Gos unsichtbare Vergleichbarkeit.

**Empfehlung: Swift-Modell.** Es ist die einzige Form, die ohne neues Wort auskommt, die Fehlerklasse
`hash ≠ equals` strukturell beseitigt und sich nicht als Bibliothek nachbauen lässt (ohne Reflexion kann
keine Bibliothek über fremde Felder iterieren — das macht dieses Feature zum stärksten Kandidaten der
gesamten Liste). Reihenfolge der Umsetzung: **Equatable + Hashable zuerst** (Map/Set-Schlüssel sind der
häufigste Bedarf), **Display zweitens** (multipliziert sich mit dem f-String-Feature), **Ordered zuletzt**.

## Offene Fragen

1. **Display-Format:** Initializer-Syntax (`Coord { x = 1, y = 2 }`, round-trip-fähig, mein Vorschlag —
   wie C# `record`) oder kompakt (`Coord(1, 2)`)? stdlib-redesign braucht für Container eine
   Verschachtelungsregel („oben ohne Anführungszeichen, geschachtelt mit") — beide Regeln müssen zusammen
   entschieden werden.
2. **Darf ein Feld ausgeschlossen werden** (Rust: nein, dann von Hand)? Vorschlag: nein.
3. **`Ordered` für Enums mit Payload:** Tag-Reihenfolge, dann Payload (wie Rust) — bestätigt?
4. **Zyklische Typen** (`class Node { next: ?Node }`): synthetisiertes `equals`/`hash` läuft unendlich.
   Vorschlag: erlauben und im Guide warnen (Swift/Rust tun dasselbe), oder Klassen-Felder vom
   Hash-Schema ausnehmen? Offen.

## Spec-Diff (§5.1)

```diff
 Conformance requires one matching implementation per abstract method of the interface — an own
 member or a visible extension method (`LYR-SEM0020` otherwise, naming the implying interface
 when it came through a chain) — with an **exact** signature match: …
+
+### Synthesized conformance
+
+For four interfaces of `std.core` — `Equatable<Self>`, `Hashable<Self>`, `Ordered<Self>` and
+`Display`, a closed list the compiler knows the way it knows `Throwable` — a declared
+conformance whose method is NOT written is **synthesized** rather than refused. The synthesis
+is field-wise in declaration order; an own member always wins; for an enum the tag comes first
+and the payload fields follow in declaration order, and a unit variant is its tag alone.
+`equals` folds `==` over the fields with `&&`, `hash` folds `std.core.combineHash`, `compare`
+answers the first non-zero field comparison, and `show` writes the initializer syntax.
+
+Every field must itself have the property, and a field that does not is the error — reported
+AT THE FIELD, not at the conformance list. A generic type carries the constraint in writing
+(`struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>]`): nothing is added implicitly. A
+`?T` field is `Equatable` when `T` is. An array field is refused for now. On a `class` the
+synthesis is VALUE equality. Only the conformance on `Self` is synthesized; any other argument
+(`Tag :: [Equatable<int>]`) is written by hand.
```
