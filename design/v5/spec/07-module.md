# 07 — Module, Sichtbarkeit, Pakete

Lebendes Dokument des Bereichs 7. Fragen V1–V10, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` L7 (Quelle ist das Format, Cache ohne Versprechen, whole-program), L8 (eine
C-Einheit je Modul), `03-typsystem.md` X3 (Kohärenz whole-program, keine Orphan-Regel), X6,
`04-abstraktion.md` D14 (Interface-Member immer öffentlich), D11 (Fabriken statt Konstruktoren,
private Felder → Fabrikpflicht).

## Bestandsaufnahme Lyric 4

Aus `../module.md` (45 Fragen), `../build-pakete.md` (33), Spec §4, Guide 12/16/17:

- **Eine Datei ist ein Modul**; Header optional; drei Importformen (selektiv, qualifiziert,
  `as`); Extension kommt mit dem Modul; Zyklus ist ein Fehler; ein Segment gehört genau einem
  Root (Projekt, std, native, Dependency); zwei Sichtbarkeitsstufen (Modul, `pub`).
- **`pub` wird auf dem qualifizierten Weg nicht geprüft** — der zentrale Befund: `lib.hidden()`,
  `lib.secretGlobal`, `lib.Secret { … }`, `lib.Mode.Fast`, `extend lib.Secret`, Generics-Schranke
  `<T :: [lib.Hidden]>`, `extern` — alles kompiliert; nur der selektive Import prüft. Drei
  Implementierungen einer Regel; die halbe Prüfung bei Überladungen ist schlimmer als keine.
  **Es ist die einzige Art, Whitebox zu testen** (Test-Root importiert den Source-Root
  qualifiziert).
- **Modulname**: drei Aufrufer, drei Ableitungen (Header, Pfad, `main`); eine Bibliothek ohne
  Header heißt `main`; ein Entry `app.lyr` wird bei `import app` **zweimal geladen**; `module
  std.core;` in der Entry-Datei verdrängt die stdlib.
- **`pub` steht an drei Stellen ohne Wirkung** (Methode, `static let`, `extend`, Interface-
  Member); **Felder nehmen syntaktisch kein `pub`**; alle Member eines `pub`-Typs sind öffentlich,
  Felder von außen schreibbar.
- Kein Re-Export, kein Rename im selektiven Import, kein `pub import`, kein `internal`/
  `pub(package)`; `panic` und die f-String-Helfer sind ohne Import gebunden (implizite Menge),
  ein Benutzer-`panic` verdeckt still; Modulpfad-Tippfehler ohne Vorschlag; bare Import warnt
  nie bei Nichtnutzung; ein Typ gleichen Namens wie ein Untermodul kollidiert im Mangling
  (`a.b.c`); modulprivate Typen gleichen Namens kollidieren in generischen Instanzen.
- **Pakete**: `lyric.json` (`name`, `sourceRoot`, `testRoot`, `nativeRoots`, `dependencies`,
  `toolchain`), Abhängigkeiten **nur lokale Pfade**; flacher Abschluss, Wurzel pinnt Segmente;
  keine Versionen, kein Lockfile, keine Registry, kein Cache, keine kompilierte Bibliothek —
  „der ganze Abschluss ist ein Compile"; Artefakt byteweise reproduzierbar; fünf stdlib-Module
  werden bei jedem Bau geparst (~90 ms); ein weggepinntes Projekt wird trotzdem gelesen.
- Bibliothek wurzelt in ihren `pub` **Funktionen** — Member nicht erfasst, Privates bleibt drin.

## V1 — Modulbegriff und Modulname: **offen**
## V2 — Sichtbarkeit: **entschieden** (2026-09-29)

**Drei Stufen** (Arbeitsnamen): **`private`** — dieses Modul; **`internal`** — dieses Paket,
**der Default**; **`pub`** — exportiert. Der Default ist die häufigste Bedeutung: in einer App
teilen Dateien ohne Wort, eine Bibliothek schreibt Wörter nur, wo sie eine Absicht hat
(exportieren, verstecken). Swift (`internal` Default), Go (paketweit); Rust's `private`-Default
verworfen (`pub`/`pub(crate)`-Rauschen in jeder App).

| # | Regel | Vorbild |
|---|---|---|
| S0 | **Member folgen der Typregel**: ohne Wort `internal`, `pub` exportiert, `private` modulintern. `struct Point { x: int, y: int }` ist in einer App vollständig; an der Exportgrenze `pub x`. Enum-Varianten wie das Enum; Extension-Methoden wie Member | Swift |
| S0a | **Felder: Lese-/Schreibtrennung** — ein `var`-Feld darf lesend weiter sichtbar sein als schreibend (Arbeitsnotation `pub(read) var n`; Syntax Bereich 8); unveränderliche Felder sind mit `pub` lesbar und nie schreibbar (M2) | Swift `private(set)`, Kotlin, C# |
| S1 | Sichtbarkeit wird **bei jeder Namensauflösung** geprüft — qualifiziert, selektiv, Pattern, Konformanzliste, Constraint, `extend`-Ziel, `extern`; **eine** Implementierung im Symbolmodell (schließt den zentralen 4.x-Befund) | — |
| S2 | **Privater Typ in exportierter Fläche ist ein Fehler** (`pub fn make(): Secret`) | Rust E0446 |
| S3 | `pub` auf einem Member eines nicht exportierten Typs ist erlaubt (exportiert, sobald der Typ es wird) — **mit Warnung** „Member `x` ist sichtbarer als sein Typ `Y`" | Rust (ohne Warnung) |
| S4 | **Interface-Anforderungen** (Methoden, statische Member, assoziierte Typen) sind so sichtbar wie das Interface — ein Modifikator dort ist ein Fehler (ein Interface *ist* seine Memberliste; eine Pflicht ohne Aufrufer wäre sinnlos). **Private Helfer mit Rumpf** im Interface sind erlaubt, aufrufbar nur aus Default-Methoden desselben Interfaces; kein Konformer implementiert sie | Rust, Swift; Java 9 (private interface methods) |
| S5 | **`extend`-Blöcke**: inhärent `extend Foo { … }` — jede Methode mit eigener Stufe; ein Modifikator am Block ist der **Default für die Methoden darin** (`private extend Foo { … }`). Konformanz `extend Foo :: [Bar]` — die Konformanz ist **global**, sichtbar wo `Foo` und `Bar` sichtbar sind, nicht einschränkbar; Modifikator am Block ist ein Fehler. Grund: Kohärenz (X3) — zwei paketprivate `Hashable`-Konformanzen wären zwei Hash-Funktionen für einen `Set<Foo>` | Swift `private extension`; Rust/Swift (Konformanzen global) |
| S6 | Die Bibliotheks-Wurzelregel (Reachability, L11) folgt der Sichtbarkeit: exportiert = erreichbar, inklusive Member | — |

## V3 — Importformen, Re-Export, Prelude: **offen**
## V4 — Kapselungseinheit und Whitebox-Tests: **offen**
## V5 — Initialisierung und Globale: **offen**
## V6 — Namensräume und Kollisionen: **offen**
## V7 — Pakete: Manifest, Versionen, Auflösung, Lockfile, Registry: **offen**
## V8 — Bibliotheksform und öffentliche Fläche: **offen**
## V9 — Toolchain- und Sprachversion im Paket: **offen**
## V10 — `std` als Paket: **offen**
