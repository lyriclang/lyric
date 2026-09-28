# 04 — Abstraktion und Dispatch

Lebendes Dokument des Bereichs 4. Fragen D1–D15, je **entschieden** oder **offen**. Basis:
`03-typsystem.md` (`Self`, statische Member, assoziierte Typen, Objektsicherheit, Typ-Patterns,
Koerzion statt Subtyping, generische Extends, Kohärenz ohne Orphan-Regel) und `02-wertmodell.md`
(`==` nur über `Equatable`, Identitäts-Marker, `mut fn` erzwungen).

## Bestandsaufnahme Lyric 4

Aus `../interfaces.md` (48 Fragen), `../overloading.md` (41), Guide, Spec §4.3a/§5:

- **Interfaces**: `::` deklariert Konformanz; auf einem Interface Eltern (Implikation, keine
  Wertkonvertierung); mehrere Eltern, Diamant erlaubt, kein Overriding von Chain-Membern.
  Default-Methoden: eigener Member gewinnt. §5.4 sagt „eigener Member, Extension, Default" —
  gemessen: vtable-Zeile hält den Default, konkreter Pfad die Extension, monomorphisierter Pfad
  sieht keine Extension (**drei Pfade, drei Antworten**). Zwei Defaults gleichen Namens aus zwei
  Interfaces werden angenommen; `let a: I1 = C{}` → `i1`, `let b: I2 = C{}` → `i2`.
  Mehrfachkonformanz seit 3.0; Orphan-Regel `SEM0041`; Objektsicherheit teils unbenannt
  (slotloses Interface darf kein Wert sein). `catch` auf Interfaces seit 4.6. Fehlt: Felder im
  Interface, Upcast auf Eltern-Interface, `==` auf Interface-Werten, `sealed`, Super-Aufruf,
  privat gehaltene Konformanz, `extend` auf Interface (angenommen, dann `IR0001`). Struct →
  Interface-Wert: Kopie, dann geteilt (im Lowering begründet, in der Spec unbenannt).
- **Klassen**: Referenz ohne Vererbung; Felder, Methoden, `static let`/`fn`; **keine
  Konstruktoren**, nur Initializer; keine Member-Sichtbarkeit; `mut` nicht erzwungen (Uhr).
- **Überladung**: frei seit 3.0, §4.3a präziser als der Compiler. Gemessen: exakter Treffer
  gewinnt; nur Rückgabetyp → abgelehnt; kein Konversionsrang; Constraints in der Auswahl nicht
  gefragt; Typargumente am Aufruf ignoriert; Lambdas stimmen nie ab; Auswahl **vor**
  Monomorphisierung (`k(x)` mit `x: T` findet nichts); auf Primitiven keine Menge; Sichtbarkeit
  je Importstil anders; exponentiell in der Tiefe (49,7 s bei 24); Mangling positionsabhängig.
  **Sechs Auswahlalgorithmen** (Aufruf, Member, Extension, Operator, Interface-Default,
  `as`/`Into`), messbar verschieden.
- **Operatoren**: `Add<T, R>` u. a. mit zwei Typargumenten, Multi-Konformanz nach rechtem
  Operanden; `Equatable<T>`, `Ordered<T>`, `Into<T>`; `++`/`--` außerhalb der Interfaces;
  fehlend `Neg`, `Rem`, Bit-Ops, `Contains`.
- **Synthese**: Entwurf, nichts gebaut.

## D1 — Vererbung: **entschieden** (2026-09-28) — keine; Delegation `by`

**Keine Klassenvererbung.** Interface-Verfeinerung mit Defaults bleibt (Eltern als
Implikation; ob ein Kind-Wert zum Eltern-Wert koerziert: D10), dazu **`sealed`** (D8) und
**Delegation als Sprachfeature** (Kotlin `by`).

| Modell | Warum nicht |
|---|---|
| Einfache Klassenvererbung (C#, Kotlin, Swift, Java) | unter V3 billig (VTable im Deskriptor, Upcast = Reinterpretation), aber Sprachkosten: `open`/`final`, `override`, `abstract`, `protected`, `super`, Konstruktorverkettung (D11 dreimal schwerer), ein vierter Auflösungspfad (D2), ein zweiter Dispatch-Mechanismus neben Interface-Tabellen, Struct/Klasse asymmetrisch, fragile Basisklasse — Kotlin und Swift bremsen das Feature mit `final` per Default. Die drei Nutzen (Hooks, Zustand mitnehmen, offene Hierarchie) decken Defaults, Delegation und Interface-Werte. **Nachrüstbar** unter V3, falls je nötig |
| Go-Embedding (befördert alle Member) | Schatten- und Mehrdeutigkeitsregeln; `by` ist Konformanz, nicht Einbettung |

**Delegation `by`**: `class Dog :: [Walker by legs, Named by tag] { legs: Legs, … }` —
jedes `Walker`-Member, das `Dog` nicht selbst schreibt, wird an das Feld `legs` weitergeleitet
(synthetisiert: `fn walk(d) { this.legs.walk(d); }`). `by` spart genau diese
Weiterleitungsmethoden und sonst nichts:

| Regel | |
|---|---|
| das Feld muss zum Interface konformieren (Fehler an der `by`-Stelle) | |
| eigene Member gewinnen; Interface-Defaults laufen auf dem **äußeren** Typ (`Dog` ist der Konformer; `let w: Walker = dog` zeigt auf den Dog) | |
| **kein `this`-Durchgriff**: `Legs.walk` läuft mit `this = legs`; ruft es `this.speed()`, ist das `Legs.speed()`, nie die Dog-Fassung — der Unterschied zur Vererbung und der Grund, warum es keine Fragilität gibt | |
| nur Interface-Member werden weitergeleitet, keine anderen Methoden oder Felder von `Legs` | |
| `let`- oder `var`-Feld; bei `var` austauschbar zur Laufzeit (Strategiemuster) | |
| mehrere Delegationen erlaubt; zwei Interfaces mit demselben Member an verschiedene Felder: Fehler an der Deklaration | |
| `mut fn`-Member auf einem Struct-Feld brauchen `var legs` (M4) | |
| für Structs erlaubt; `by` auf ein Interface-Feld (`legs: Walker`) erlaubt (dynamische Wahl) | |

## D2 — Dispatch und Auflösungsreihenfolge: **offen**
## D3 — Default-Methoden, Konflikte, expliziter Aufruf: **offen**
## D4 — Überladung: **entschieden** (2026-09-28) — nur Arität

**Ein Name darf mehrere Signaturen haben, wenn sie sich in der Anzahl der Parameter
unterscheiden — nie nur im Typ.** Auswahl = Anzahl zählen, ein Kandidat. Keine Rangordnung,
kein Weitungsrang, keine Ambiguität am Aufruf, keine Wechselwirkung mit Inferenz (T8),
Generics, `inout`, `throws`, Operatoren. Mangling: Name + Anzahl, stabil.

- Default-Argumente (D5) und Arität sind derselbe Mechanismus: `f(a, b = 0)` ist `f(a)` und
  `f(a, b)`. Passen für irgendeine Argumentzahl zwei Signaturen, ist das ein **Fehler an der
  Deklaration**, nie am Aufruf.
- Typsuffixe (`absInt`, `maxInt`, …) gehen über **Generics + Interfaces** (T5: `Self`,
  statische Member; D6), nicht über Überladung — der Weg von Rust und Go.
- Operatoren (D6): eine Auflösung, die Konformanz zum rechten Operandentyp.

Gemessen als Grundlage: echte Überladungsmengen (gleicher Scope, gleicher Receiver, anderer
Typ) gibt es in der stdlib praktisch nicht, und die Typsuffix-Familien stehen nach anderthalb
Majors mit freier Überladung unverändert da — das Feature wurde eingeführt und nicht benutzt.

| Verworfen | Warum |
|---|---|
| freie Überladung, repariert (C#, Java, Swift, Kotlin) | der 41-Fragen-Katalog ist keine Implementierungsschwäche, sondern die Form, die freie Überladung in jeder Sprache annimmt (sechs Auflösungsalgorithmen, Rangordnung, exponentielle Auswahl, Signatur-Mangling); ihr einziger genannter Nutzen (Typsuffixe) ist generisch lösbar. Nimmt 3.0 zurück, mit 3.0's eigener Begründung („the shape Oil died of") |
| keine Überladung (Rust, Go, Zig) | verliert Fabriken (`of(hex)`, `of(r, g, b)`) und Bequemlichkeits-APIs, die Arität ohne jede Regelkomplexität deckt |

## D5 — Default-Argumente und benannte Argumente: **entschieden** (2026-09-28)

| # | Regel | Vorbild |
|---|---|---|
| F1 | **Der Default gehört zur Deklaration, einmal**: ein Interface-Member deklariert ihn, eine Implementierung darf keinen eigenen angeben (auch nicht denselben) — sie erbt ihn; jeder Aufrufpfad (statisch, virtuell, Constraint) liest denselben Ort. Schließt den gemessenen Drei-Leser-Befund | Kotlin |
| F2 | **Je Aufruf ausgewertet, im Scope der Funktion**, nach den Pflichtargumenten; darf frühere Parameter referenzieren (`fn sub(s: str, from: int = 0, to: int = s.length)`), kein `this` | Kotlin; Python (einmal bei Definition) und C# (nur Konstanten) verworfen |
| F3 | Defaults an jeder Position; ein mittlerer Default ist nur benannt überspringbar | Kotlin |
| F4 | zwei Signaturen, die für eine Argumentzahl beide passen: Fehler an der Deklaration (D4) | — |
| F5 | **Jeder Parameter ist benennbar** (`connect(host: "h", port: 80)`); **Parameternamen sind öffentliche API** (Umbenennung = Bruch für benannte Aufrufer) | Kotlin, C#, Python; Swift's externe Labels (zweite Namensmenge) verworfen |
| F6 | positionell vor benannt; benannte in beliebiger Reihenfolge; kein Name doppelt, keiner für einen positionell gesetzten Parameter | Python, Kotlin |
| F7 | Namen stimmen bei der Auswahl nicht mit (Arität wählt, D4); unbekannter Name → Fehler mit Kandidatenliste | — |
| F8 | `params` bleibt letzter Parameter, nicht benennbar; Trailing-Lambda bleibt (Bereich 8) | — |
| F9 | **Schreibweise `name: value`** — Zuweisung ist ein Ausdruck, `f(a = 3)` wäre eine Zuweisung als Argument; in Aufrufklammern kommen keine Typen vor. Struct-Initializer und Attribute behalten `=` (andere Klammern, W16 gelöst). Bereich 8 bestätigt | C#, Swift |

Guide: benannte Argumente für Konfiguration, positionelle für Daten.

## D6 — Operator-Interfaces: **offen**
## D7 — Konformanz-Synthese: **offen**
## D8 — `sealed`: **offen**
## D9 — Objektsicherheit vollständig: **offen**
## D10 — Interface-Eltern als Wert: **offen**
## D11 — Klassen: Konstruktion und Validierung: **offen**
## D12 — Struct als Interface-Wert: **offen**
## D13 — `Clone`: **offen**
## D14 — Interface-Member-Sichtbarkeit, private Konformanz: **offen**
## D15 — Extensions: **offen**
