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

## D2 — Dispatch: **entschieden** (2026-09-28) — eine Methodenmenge je Typ

„Drei Pfade, drei Antworten" entstand, weil ein Name aus zwei Mengen kommen konnte
(Typ-Methoden vs. Konformanzen). Keine Reihenfolge repariert das — nur eine Menge.

| # | Regel | Vorbild |
|---|---|---|
| R1 | Methodenmenge = eigene Member + inhärente Extension-Member + Konformanz-Implementierungen (eigene oder Default); **ein Name genau einmal**. Ein eigener Member `m` *ist* die Implementierung von `I.m` für jedes konformierte `I` (Signaturen müssen passen) | Lyric 4 |
| R2 | **Kollision = Fehler an der Deklaration**: inhärente Extension `extend T { fn m() }` neben einer Konformanz, die `m` per Default nimmt → „mach sie zur Konformanz-Implementierung oder benenne um". Whole-program prüfbar (X3) | Rust (inhärent gewinnt still) und Kotlin (Member gewinnt still) verworfen — dokumentierte Fallen |
| R3 | **Jeder Pfad liest dieselbe Menge**: statischer Aufruf, VTable-Zeile, Constraint-Aufruf; die VTable hält, was R1 ergibt | — |
| R4 | Im generischen Rumpf sieht `x: T` die Constraint-Member (O2) **plus generische Extends, deren Constraint `T` erfüllt** (`extend<T :: [Walker]> T { … }`, D15) | Rust blanket impl |
| R5 | Interface-Member **qualifiziert aufrufbar**: `Walker.describe(x)` (UFCS) | Rust, Kotlin `super<I>` |

## D3 — Default-Konflikte: **entschieden** (2026-09-28)

| Fall | Regel | Vorbild |
|---|---|---|
| `C :: [I1, I2]`, beide `greet` mit Default, `C` schreibt keins | **Fehler an der Konformanz**: `C` muss `greet` implementieren — eine Funktion für beide | Java, Kotlin; 4.x's stille Annahme mit je statischem Typ anderer Antwort fällt |
| `C` implementiert `greet` | implementiert beide; Signaturen müssen übereinstimmen | Java |
| zwei **verschiedene** `greet` je Interface | nur über getrennte Konformanzblöcke `extend C :: [I1] { fn greet() }` / `extend C :: [I2] { … }`; der Name ist **auf den Block beschränkt**: `c.greet()` ist ein Fehler („qualifiziere `I1.greet(c)`"), `let a: I1 = c; a.greet()` eindeutig | Rust (trait-scoped) |
| eigene Implementierung ruft den Default | `I1.greet(this)` (R5) — Swift's Protocol-Extension-Lücke geschlossen | Java `I1.super.greet()` |
| Diamant (ein Member über zwei Elternpfade) | ein Member, kein Konflikt | — |
| zwei Elternteile mit gleichem Namen aus verschiedenen Deklarationen | Fehler am Interface (bleibt) | — |

**Ein Name, eine Funktion je Typ — außer der Programmierer trennt ausdrücklich nach
Konformanzblock, und dann ist der unqualifizierte Aufruf verboten.**

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

## D6 — Operator-Interfaces: **entschieden** (2026-09-28)

Mit `Self`, assoziierten Typen und Default-Typargumenten: `interface Add<Rhs = Self> { type Out
= Self; fn add(rhs: Rhs): Out; }` — der homogene Fall ist `struct Vec2 :: [Add]` ohne
Zeremonie; heterogen `Mul<float>`, `Mul<Vec2>` mit `type Out = float`. **Defaults für
assoziierte Typen** (`type Out = Self;`) sind erlaubt.

| Operator | Interface / Regel |
|---|---|
| `+ - * / %` | `Add`, `Sub`, `Mul`, `Div`, `Rem` je `<Rhs = Self>`, `type Out = Self`. **Eine Auflösung**: `a * b` ist exakt `a.mul(b)`, Konformanz nach dem statischen Typ von `b`, Literale adaptieren (T8); zwei Konformanzen, die ein Literal beide nehmen → Mehrdeutigkeit, annotieren. §5.1's Versprechen gilt per Konstruktion |
| `-x` | `Neg { type Out = Self }` |
| `& \| ^ ~ << >>` | `BitAnd`, `BitOr`, `BitXor`, `BitNot`, `Shl<Rhs = int>`, `Shr` |
| `!x` | nur `bool`, kein Interface |
| `+=` u. a. | abgeleitet: `a = a.add(b)`, wenn `Out == Self` und `a` ein `var`-Ort; kein `AddAssign` (Tür) |
| **`++`/`--`** | **Ausdrücke** (Maintainer: C#-Form): Postfix liefert den alten, Präfix den neuen Wert, Schreibung sofort; Ziel jeder `var`-Ganzzahl-Ort (Local, Feld, Array-Element, `inout`) — schließt SPEC-RUNDE 1; Auswertung links nach rechts überall, Ziel vor Wert (C# §12.4.1); beide Formen auch als Statement; `x = x++;`/`x = ++x;` → Warnung; **nur Ganzzahlen**, eigene Typen schreiben `+= 1`; Überlauf Panik (T2). Verworfen: Go-Statement-Form, Swift's Streichung |
| `== !=` | `Equatable { fn equals(o: Self): bool }` (M10) |
| `< <= > >=` | `Ordered :: [Equatable] { fn compare(o: Self): ?Ordering }` — `?`, weil `float` partiell ist (Rust `PartialOrd`) |
| Sortieren, Schlüssel | `TotalOrder :: [Ordered] { fn totalCompare(o: Self): Ordering }` (Rust `Ord`). **`float`: `Equatable`, `Ordered`, aber weder `TotalOrder` noch `Hashable`** — `Map<float, X>` Übersetzungsfehler, `sort` auf `float[]` per `sortBy(float.totalCompare)` (IEEE-totalOrder). Beantwortet Korpus ★W5: `NaN != NaN` bräche jede Map-Suche |
| `x in xs` | `Contains<T> { fn contains(x: T): bool }` — Ranges, Arrays, `Slice`, `Set`, `Map` (Schlüssel), `string`/`str`; `!in` Bereich 8 |
| `x[k]` | `Index<K>`, `IndexSet<K>` (T14) |
| `as` | **nur numerisch** (T1d); Typkonversion als Methode `T.from(v)`/`v.into()` über `From<T>`/`Into<T>`, Auswahl über den erwarteten Typ (T8) — der `as`/`Into`-Mehrdeutigkeitsbefund verschwindet |
| `{x}` im f-String | `Display { fn show(): string }`; Debug-Form D7 |

## D7 — Konformanz-Synthese: **entschieden** (2026-09-28)

**Form: die Konformanz ohne Körper** — `struct P :: [Equatable, Hashable] { … }` synthetisiert
(Swift). Kein `derive`-Wort. *Vorläufig bis Bereich 9* (Maintainer, 2026-09-29): die Begründung
„ein Attribut, das *tut*, gibt es bei uns nicht" ist die 4.x-Prämisse und für 5 **nicht**
übernommen; Bereich 9 entscheidet, was Attribute dürfen. Die Form über die Konformanzliste
kann auch dann die bessere sein — sie ist dann begründet, nicht geerbt.

| Interface | Synthese | Bedingung |
|---|---|---|
| `Equatable`, `Hashable` | feldweise, Deklarationsreihenfolge, konsistent | alle Felder konform |
| `Ordered`, `TotalOrder` | lexikographisch nach Deklarationsreihenfolge — nur auf Anfrage | alle Felder konform |
| **`Debug`** | **für jeden Typ automatisch, bei Bedarf** (`P { x = 1, y = 2 }`, Variantenname, Klassen mit Feldern); kein Opt-out | — |
| `Display` | **nie automatisch**; auf Anfrage = Debug-Form (löst Korpus W9: zwei Interfaces) | — |
| `Default` | feldweise aus `Default` der Felder oder aus Feld-Defaults (M14) | — |
| `Clone` | **flach**: Wertfelder kopiert, Referenzfelder geteilt (Kotlin `copy`); auf Klassen die Antwort zu M6 W2 | — |
| `Identity` (M10) | Identitäts-`Equatable` + Adress-`Hashable`, nur Klassen | — |
| Enums | wie Structs; statische `variants()`/`fromName()` Bereich 9 | — |
| Tupel | automatisch bedingt (T16) | — |

Regeln: generische Typen **bedingt** synthetisiert (`struct Pair<T> :: [Equatable]` ⇒
`extend<T :: [Equatable]> Pair<T> :: [Equatable]`, Rusts `derive`-Bound); ein selbst
geschriebener Member ersetzt die Synthese für diesen Member (Swift); fehlende Feldkonformanz
nennt das Feld. **Liste in 5.0 fest im Compiler**; nutzererweiterbare Synthese (`ToJson`) ist
die Bereich-9-Frage (`comptime`-Kandidat, kein Makrosystem).

## D8 — `sealed`: **entschieden** (2026-09-28)

`sealed interface Shape { … }`: alle Konformer **im selben Modul** (Paketgrenze, sobald Bereich
7 sie definiert); `match` über Typ-Patterns dann **erschöpfend ohne `_`**; Konformer sind Structs
oder Klassen. Kein Doppel mit `enum`: Enum = geschlossene Varianten mit Nutzlast, versiegeltes
Interface = geschlossene Typen mit eigenen Methoden. (Kotlin, Java 17)

## D9 — Objektsicherheit: **entschieden** (2026-09-28)

**Wertfähig** (Fat Pointer) ist ein Interface, wenn: kein `Self` außerhalb der
Receiver-Position; keine statischen Member; **keine generischen Member** (als Constraint
erlaubt, als Wert unbenutzbar); assoziierte Typen im Werttyp **fixiert** (`Iterator<Item =
int>`). Sonst nur als Constraint; Diagnose an der Verwendungsstelle mit Grund. Die 4.x-Regel
„slotloses Interface darf kein Wert sein" **fällt** (`Any`). Default-Rümpfe mit `Self` sind
harmlos (je Konformer monomorphisiert). (Rust dyn-compatibility, Swift)

## D10 — Kind-Interface-Wert → Eltern-Wert: **entschieden** (2026-09-28)

**Ja, als Koerzion** (T3). Zero-Cost über **VTable-Präfix** (die Zeile des Kindes beginnt mit
den Slots des ersten Elternteils — 4.x-Layout); weitere Elternteile über die Konformanzliste im
Deskriptor. Nie implizit in Containern. (Go implizit, Rust seit 1.86)

## D11 — Klassenkonstruktion: **entschieden** (2026-09-28)

**Keine Konstruktoren.** Initializer `C { f = v }` bleibt die Primitive und der einzige Ort
für `let`-Felder; **Fabriken** `static fn new(…): C` tragen Validierung; mit
Member-Sichtbarkeit (Bereich 7) sind private Felder von außen nicht initialisierbar →
Fabrikpflicht, Invarianten sicher. Struct und Klasse symmetrisch. **Vorgemerkt Bereich 8**:
Aufrufsyntax `Point(1, 2)` als Zucker für `Point.new(1, 2)`. (Rust, Go; Kotlin/Swift-`init`
verworfen: zweite Deklarationsform, Verkettung, ohne D1 nicht nötig)

## D12 — Struct als Interface-Wert: **entschieden** (2026-09-28)

Der Übergang **kopiert** (V7 boxt), danach geteilt; unabhängige Kopie per Downcast (T11) oder
`Clone`. Die 4.x-Lowering-Regel wird Spec-Satz.

## D13 — `Clone`: **entschieden** (2026-09-28)

`interface Clone { fn clone(): Self }`, synthetisiert flach (D7); **`Self`-liefernd, also nur
Constraint** — durch einen Interface-Wert klont man per Downcast. `with` auf Klassen bleibt
nein (M6); `c.clone() with …` ist der Weg. (Rust)

## D14 — Interface-Member-Sichtbarkeit: **entschieden** (2026-09-28)

Interface-Member sind **immer öffentlich** (`pub` am Member ist ein Fehler); eine Konformanz
ist sichtbar, wo Typ und Interface sichtbar sind; **keine private Konformanz**; Sichtbarkeit des
Interfaces: Bereich 7. (Rust, Swift, Kotlin)

## D15 — Extensions: **entschieden** (2026-09-28)

Drei Formen: inhärent `extend T { … }`, Konformanz `extend T :: [I] { … }`, **generisch/
Blanket** `extend<T :: [I]> T :: [J] { … }` (X1/R4); ein Blanket-Extend schließt spezifische
`extend Foo :: [J]` aus (X4, Rusts Grenze). **`extend Walker { … }` auf ein Interface ist ein
Fehler mit Hinweis auf die generische Form** (Swift's Protocol Extension als zweite Schreibweise
verworfen). Statische Member ja, Felder nein (X5); Sichtbarkeit X6.

---

**Bereich 4 ist damit vollständig entschieden** (D1–D15, 2026-09-28).
