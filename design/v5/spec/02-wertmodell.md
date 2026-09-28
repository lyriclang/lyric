# 02 — Wertmodell

Lebendes Dokument des Bereichs 2. Fragen M1–M14, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` (Structs als C-Structs inline, Klassen mit Ein-Wort-Header, Innenzeiger,
nicht bewegender GC).

## Bestandsaufnahme Lyric 4

Gemessen (Proben dieser Runde, Dossier `../werte-mutabilitaet.md` mit 18 Befunden):

- Versprechen: `struct` Wert (Zuweisung kopiert), `class` Referenz; `let` bindet den Namen;
  `mut fn` markiert Schreiben auf `this`.
- Die lvalue-Prüfung läuft nicht zur Wurzel: `let s = S{…}; s.v = 2;` kompiliert (Uhr
  `SEM0109`); ein Parameter ist eine unveränderliche Bindung mit schreibbaren Feldern.
- `mut fn` für Structs erzwungen, für Klassen nicht (Uhr `SEM0108`); läuft auf `let`.
- `this` in einer Struct-Methode ist eine Referenz (Teilschreibung vor `throw` bleibt); der
  Guide-Satz „written back to the caller's value" ist falsch.
- Der Typ sagt nicht, ob eine Bindung kopiert oder teilt: `?S` als Feld/Parameter aliast,
  `[S{…}] * 3` sind drei Aliasse; `[s, s]` kopiert.
- Kopie ist flach (Arrays, Klassen im Struct werden geteilt) — vom Typ her ehrlich.
- `l[0].v = 9` auf `List<S>` ist ein stiller No-Op (Temporary), `S[]` schreibt in place.
- Alle Bindungsformen (`for`, Destructuring, `if let`, `match`) binden Kopien.
- Closures fangen Structs per Referenz; die Spec sagt beides (§3 Referenz, §7 „as its value").
- `p.x++` ist `IR0001`, `p.x += 1` geht. Kein `with`. Keine feldweise Erstinitialisierung.
- Rekursive Optional-Structs laufen (200k-Kette gemessen); SEM0056 begründet mit „unendlicher
  Größe", die diese VM nie hatte.
- Korpus: 15 Structs, 65 Klassen; ein `mut fn` auf einem Struct, eine Feldschreibung von außen
  (beide `examples/vectors.lyr`); die stdlib schreibt Structs bereits unveränderlich.

Unter Bereich 1 sind die Aliasing-Löcher der VM verschwunden (kein `structcopy`, Structs sind
Speicher); was bleibt, sind Sprachregeln, die jetzt sauber definiert werden.

## M1 — Zwei Typformen oder eine? **entschieden** (2026-09-28)

**A — zwei Formen am Typ: `struct` (Wert, inline, keine Identität) und `class` (Referenz,
Header, Identität).**

| Modell | Warum nicht |
|---|---|
| B · alles Referenz, Werte durch Unveränderlichkeit (Java, Kotlin, Scala, OCaml, JS) | widerspricht Z2 grundsätzlich: jeder `Point` eine Allokation, jedes `Point[]` ein Zeiger-Array. Java betreibt mit Valhalla seit zehn Jahren die Nachrüstung, Kotlin/Scala haben Value-Classes eingebaut — das Modell, das alle verlassen |
| C · alles Wert, Referenz explizit (Go `*T`, Rust, Zig, C) | gleich schnell wie A unter unserer Laufzeit, aber die Wert/Referenz-Frage stellt sich an **jeder Verwendung** statt einmal am Typ (Go's Dauerfrage „pointer or value receiver"); Rust beantwortet sie über Ownership, das wir nicht haben |

Begründung für A: die Wert/Entität-Frage ist eine **Domänenfrage** (`Vec3`, `Duration`,
`Money` sind Werte; `Player`, `Connection`, `Node` haben Identität) und wird einmal beim
Schreiben des Typs beantwortet. A's bekannte Schwäche — der veränderliche Struct (C#'s
defensive Kopien, `list[0].X = 9` verboten, `readonly` nachgerüstet) — wird durch M2
(unveränderlich per Default) auf die Ausnahme beschränkt; dort gilt Swift's Regel „`mut fn` nur
auf einem veränderlichen Ort" ohne Swift's Exklusivitätsmaschinerie, weil `this` eine Referenz
ist (M5) und Reentranz zugelassen statt verhindert wird. Reife: das Modell von C# und Swift.

**Vorgemerkt für Bereich 3**: eine `inout`/`ref`-Parameterform (Swift, C#) für große Structs
und Schreibzugriff ohne Klasse — dieselbe Laufzeit, kein neues Modell, nur eine Parameterform.
Unter den 4.x-Regeln verboten, jetzt Kandidat.

## M2 — Unveränderlichkeit: **entschieden** (2026-09-28) — Feld + Bindung, kein `mut struct`

**Felder sind unveränderlich, sofern sie nicht `var` heißen; ein `var`-Feld ist schreibbar,
wenn die Wurzelbindung `var` ist.** Ein Wort (`var`) an zwei Orten (Bindung, Feld), eine Regel
für Structs und Klassen.

| Form | Vorbild | Warum nicht |
|---|---|---|
| Bindung allein entscheidet | Swift, Rust | keine Autorenabsicht ausdrückbar; Swift-Asymmetrie bei Klassen (`let` schützt nichts) |
| **Feld + Bindung** | **F#, OCaml, Scala, Kotlin** | **gewählt** |
| Typ entscheidet (`struct` / `mut struct`) | — (C# `readonly struct` ist die Umkehrung) | zweite Struct-Art mit Folgen für Generics, Interfaces, Konvertierung; seine zwei Block-1-Argumente sind unter Bereich 1 weg: Structs sind inline (V2), also wird nichts geteilt und Rekursion braucht ohnehin Indirektion (M13); Hash-Sicherheit folgt aus Wertsemantik (ein Map-Schlüssel ist eine Kopie). Was bleibt, Autorenabsicht, drückt die Feldform feiner aus. `mut struct` als Zucker für „alle Felder `var`": weggelassen, bis es jemand vermisst |
| veränderlich per Default | C#, Go, Lyric 4 | C#'s Fallen: defensive Kopien, `list[0].X = 9` verboten, `readonly` nachgerüstet |

`struct Vec3 { x: float, y: float, z: float }` ist ein Wert, den niemand in place ändert;
`struct Counter { var n: int }` sagt, was veränderlich ist. Ein Klassen-`let`-Feld ist wirklich
fest (M9 damit erledigt). Migration von 4: jedes geschriebene Feld bekommt `var`.

## M3 — `let` bis in die Felder, Parameter, Wurzel: **entschieden** (2026-09-28)

- `let` friert einen **Wert tief** ein (Swift); ein **Parameter ist `let`**; eine
  Schleifen-/Pattern-Bindung ist `let`.
- Die **Wurzel** einer Zugriffskette `a.b[i].c` ist die erste Bindung — **eine Referenz wurzelt
  die Kette neu**: hinter `let arr: Point[]` ist `arr[0].x = 1` erlaubt (das Array-Objekt ist
  veränderlich, das Element ein Ort im Block, V10); hinter `let c: Cls` ist `c.varField = 1`
  erlaubt, `c.letField = 1` nicht. Ein Array ist eine Referenz (wie Kotlin, C#, Go, Lyric 4) —
  Swift's Werttyp-Arrays mit Copy-on-Write sind ein zweites, verstecktes Kopiermodell und
  werden nicht übernommen.
- `[Point{…}] * 5` erzeugt unter V10 fünf Kopien im Block; das 4.x-Loch „n Aliasse" ist durch
  die Darstellung weg.

## M4 — `mut fn` und Temporaries: **entschieden** (2026-09-28)

- `mut fn` (Swift `mutating`) bleibt der Methodenmarker; erlaubt nur auf Typen mit mindestens
  einem `var`-Feld oder mit Ganzzuweisung an `this`; **aufrufbar nur auf einer `var`-Wurzel**.
- **Temporaries werden abgelehnt**, nie still kopiert: `list[0].x = 1` und `list[0].bump()` sind
  Fehler, wenn `list[0]` das Ergebnis eines `get` ist — mit der Meldung, dass ein `set` fehlt
  (C# CS1612 als Vorbild, ohne C#'s defensive Kopie CS8656). Das Problem betrifft nur Werttypen:
  für eine Klasse liefert `get` die Referenz, und die wurzelt neu.
- Wie ein Nutzercontainer einen **Ort** liefert, ist Bereich 3/10: Get/Set-Paar mit
  Rückschreibung durch den Compiler (Swift: `list[0].x = 1` → get, ändern, set) als allgemeine
  Form; `ref`-Rückgabe über Innenzeiger (L1) als Optimierung mit der Regel „lebt nur bis zum
  Ende des Statements" (Umallokation).

## M5 — `this` als Referenz: **entschieden** (2026-09-28)

`this` einer Struct-Methode ist eine **Referenz auf den Ort des Aufrufers** (C# `ref this`),
kein Copy-in/Copy-out: eine Teilschreibung vor einem Wurf bleibt stehen, Reentranz ist sichtbar
— festgeschrieben, nicht verhindert (keine Swift-Exklusivitätsregel). Bereich 1 macht es so
(V2, S1); die Sprache sagt es.

**Vorgemerkt für Bereich 3/10 (Indexierung)**: die `Indexable`-Familie neu — `get`/`set`/`ref`,
Schlüsseltypen jenseits `int`, **Index-Ranges für Nutzertypen** (heute nur auf Arrays
definiert, nie auf `Indexable` portiert). **Für Bereich 4 (Operatoren)**: die arithmetischen
Operator-Interfaces (`Add<T, R>` mit zwei Typargumenten, fehlende `Neg`/`Rem`/Bit-Operatoren,
`++`/`--` außerhalb der Interfaces) gesamt überarbeiten.

## M6 — `with`: **entschieden** (2026-09-28)

**`p with { x = 3, y = 4 }`** — Postfix, kontextuelles Schlüsselwort; unter M2 die einzige
Form, ein unveränderliches Feld zu ändern.

| Form | Warum nicht |
|---|---|
| Rust `Point { x = 3, ..p }` | Typname wiederholen; `..p` liest sich in Ketten schlecht; Rust braucht es wegen Moves |
| F#/OCaml `{ p with x = 3 }` | kollidiert mit Value-Block-Syntax |
| Kotlin/Scala `p.copy(x = 3)` | Methode, die keine ist; umgeht Invarianten |

| # | Regel |
|---|---|
| W1 | Kopie von `p`, genannte Felder ersetzt; `p` unverändert; Typ von `p` |
| W2 | **nur auf Structs**; Klassen nicht (flacher Klon mit neuer Identität → `Clone`-Frage, Bereich 4); Tupel nicht (keine Feldnamen) |
| W3 | **Sichtbarkeit wie der Initializer an derselben Stelle** — folgt automatisch der Member-Sichtbarkeit (Bereich 7); private Felder bleiben geschützt, Kotlins `copy`-Loch entsteht nicht |
| W4 | links nach rechts; Ausdrücke sehen die **alten** Werte (`p with { x = p.y, y = p.x }` vertauscht); Feld doppelt = Fehler; unbekannt = Fehler; kein `var`-Feld nötig |
| W5 | Präzedenz Postfix-Stufe: `p with { x = 1 }.x` = `(p with { x = 1 }).x`; `q + p with {…}` = `q + (p with …)` |
| W6 | **verschachtelte Pfade** `p with { pos.x = 1 }` = entfalteter innerer `with`; Sichtbarkeit je Segment (kein Mainstream-Vorbild, aber eindeutig; tiefe unveränderliche Structs brauchen es) |
| W7 | `p with {…};` als Statement ist ein Fehler (Ergebnis ungenutzt) |
| W8 | auf Enum-Feldvarianten nicht in 5.0 (Variante statisch selten bekannt) — Tür |

Nebeneffekt für M13: `with` baut neu und kann nie einen Zyklus schließen.

## M7 — Kopieren: **entschieden durch Darstellung** (2026-09-28)

**Aus dem Typ allein ist ablesbar, ob eine Bindung kopiert oder teilt** — unter Bereich 1 ist
das die Darstellung selbst: `struct`, Tupel, Enum mit Nutzlast, `?Wert` kopieren (inline);
`class`, Array, Interface-Wert, Closure, Koroutine, `?Referenz` teilen (Zeiger). `[x] * n` und
`[x, x]` sind n Kopien (V10).

**Tiefe, ein Satz für die Spec**: *Eine Kopie kopiert die Wertfelder und teilt die
Referenzfelder.* „Unveränderlich" (M2) ist immer flach: ein `let`-Feld vom Typ `int[]` ist eine
feste Referenz auf ein veränderliches Array — F#s, Kotlins, Scalas Bedeutung.

## M8 — Closure-Capture: **entschieden** (2026-09-28) — nach Bindungsart

Ein gefangenes **`let` liegt als Kopie** in der Umgebung (semantisch unbeobachtbar, es kann
sich nicht ändern); ein gefangenes **`var` liegt in einer Box** auf dem Heap, die Umgebung und
umschließender Scope teilen, **ab seiner Deklaration** (kein Stack-Ort, der die Closure
überleben müsste; Go, Swift). Kosten sichtbar: eine gefangene `var` allokiert einmal, sonst
nichts. Löst den Spec-Widerspruch §3/§7 („by reference" / „as its value"): beides.

| | Warum nicht |
|---|---|
| immer per Referenz (Go, C#, JS, Kotlin) | boxt auch nie geänderte Variablen |
| immer per Wert (C++ `[=]`, Rust `move`) | `var n = 0; xs.each { n += 1 }` stirbt |

| # | Folgeregel |
|---|---|
| C1 | **Schleifenbindungen je Durchlauf frisch**: `for (i in 0..3) { fs.add(() => i) }` → 0, 1, 2 — folgt aus M3 (`let`), was Go 1.22 reparieren und C# bis heute erklären muss |
| C2 | **`this` einer Struct-Methode wird per Wert gefangen** (Referenz auf einen ggf. Stack-Ort, M5; Swift verbietet `self`-Capture in `mutating` bei escaping Closures — wir kopieren); Klassen-`this` wird geteilt |
| C3 | **`inout`/`ref`-Parameter** (Bereich-3-Kandidat) sind von entkommenden Closures nicht fangbar |

## M9 — Unveränderliche Klassenfelder: **entschieden durch M2** (2026-09-28)

Ein Feld ohne `var` ist fest, durch jede Referenz; mit `var` schreibbar durch jede Referenz
(Kotlin `val`/`var`).

## M10 — Identität und Gleichheit: **entschieden** (2026-09-28)

- **Werte haben keine Identität** (`struct`, Tupel, Enum — unter V2 nicht einmal eine stabile
  Adresse); **Referenzen haben sie** (`class`, Array, Koroutine; V3: die Adresse ist die
  Identität). Swift's Linie.
- **`==`/`!=` bedeuten genau eines: Wertgleichheit nach `Equatable`.** Kein `===`. Identität ist
  eine **Funktion** in `std.core` (`same(a, b)`, Name offen), nur für Referenztypen — auf einem
  Wert ein Übersetzungsfehler, nicht `false`. (Rust `ptr::eq`, Dart `identical`.)
- Verworfen: **A** `==` = Identität für Referenzen bis `equals` überschrieben wird (Java, C#,
  Kotlin) — ein Operator, zwei Bedeutungen, stiller Wechsel beim Hinzufügen von `Equatable`;
  **C** fest verdrahtetes `==` ohne Überladung (Go) — keine eigene Gleichheit, NaN eingebaut.
- **Woher die Gleichheit kommt** (Details Bereich 4): Structs/Enums/Tupel **feldweise
  synthetisiert auf Anfrage** (`:: [Equatable]` ohne Körper; alle Felder `Equatable`, ein
  Klassenfeld verlangt also eine `Equatable`-Klasse, nie stille Identität — Swift; Go's
  „automatisch, wenn vergleichbar" verworfen). Klassen: `equals` geschrieben oder synthetisiert
  (strukturell), **oder** ein Marker-Interface (`Identity`, Name offen), dessen Konformanz
  **Identitäts-`Equatable` und Adress-`Hashable` synthetisiert** — `Map<Node, X>` nach
  Objektidentität ohne Handarbeit. Eine Klasse ohne beides hat kein `==`.

## M11 — Große Werte: **offen**
## M12 — Besitzende Werttypen / RAII: **offen**
## M13 — Rekursive Werttypen: **offen**
## M14 — Felddefaults und Initialisierung: **offen**
