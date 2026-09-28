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

## M6 — `with`: **offen**
## M7 — Kopieren durch `?T`, Tupel, Enum, Closure-Umgebung, `[x] * n`: **offen**
## M8 — Closure-Capture: **offen**
## M9 — Unveränderliche Klassenfelder: **offen**
## M10 — Identität und Gleichheit: **offen**
## M11 — Große Werte: **offen**
## M12 — Besitzende Werttypen / RAII: **offen**
## M13 — Rekursive Werttypen: **offen**
## M14 — Felddefaults und Initialisierung: **offen**
