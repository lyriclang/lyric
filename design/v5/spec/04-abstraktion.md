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

## D1 — Vererbung: **offen**
## D2 — Dispatch und Auflösungsreihenfolge: **offen**
## D3 — Default-Methoden, Konflikte, expliziter Aufruf: **offen**
## D4 — Überladung: **offen**
## D5 — Default-Argumente und benannte Argumente: **offen**
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
