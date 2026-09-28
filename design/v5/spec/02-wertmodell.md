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

## M2 — Unveränderlichkeit per Default, `mut struct`: **offen**
## M3 — Reicht `let` bis in die Felder; Parameter als `let`; Wurzel einer Zugriffskette: **offen**
## M4 — Wer darf `mut fn` rufen; Temporaries: **offen**
## M5 — `this` als Referenz: **offen**
## M6 — `with`: **offen**
## M7 — Kopieren durch `?T`, Tupel, Enum, Closure-Umgebung, `[x] * n`: **offen**
## M8 — Closure-Capture: **offen**
## M9 — Unveränderliche Klassenfelder: **offen**
## M10 — Identität und Gleichheit: **offen**
## M11 — Große Werte: **offen**
## M12 — Besitzende Werttypen / RAII: **offen**
## M13 — Rekursive Werttypen: **offen**
## M14 — Felddefaults und Initialisierung: **offen**
