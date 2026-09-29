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

## V1 — Modulbegriff, Modulname, Einstiegspunkt: **entschieden** (2026-09-29)

**Datei = Modul bleibt; der Name kommt aus genau einer Quelle — dem Pfad.**

| # | Regel | Vorbild |
|---|---|---|
| M1 | **Modulpfad = Dateipfad relativ zum Source-Root, mit dem Paketnamen als erstem Segment** — `src/net/http.lyr` in `mypkg` ist `mypkg.net.http`, **überall so geschrieben**, auch im Paket selbst (eine Schreibweise, kein `crate::`-Zwilling) | Go |
| M2 | **Der `module`-Header entfällt** (redundant oder falsch: `RES0006`, `std.core`-Hijack) | Go, Python, Zig |
| M3 | Paketname im Manifest (V7) ist ein Root; `std` reserviert; kein Paket beansprucht einen fremden Root | Lyric 4 |
| M4 | **Loader dedupliziert nach Datei**, nicht nach Name (die Doppelladung von `app.lyr`) | — |
| M5 | Verzeichnis = Namensraum, kein Modul; `net.lyr` neben `net/` ist Modul `net` | Rust |
| M6 | Modulpfade **case-sensitiv, müssen dem echten Dateinamen entsprechen, auf jeder Plattform** | Go |
| M7 | **Einstiegspunkt** (aufgerollt): |  |
| M7a | **Ein Modul darf Bibliothek und Programm sein.** `main` in einem importierten Modul ist eine normale `internal`-Funktion; nur das Entry-Modul des Artefakts macht sie zur Wurzel (L11), jedes andere `main` ist toter Code. `lyric run src/tools/gen.lyr` führt dessen `main` aus. Das 4.x-Verbot (`SEM0021`) war eine VM-Regel (ein `.lyrbc`, ein Entry) und fällt ersatzlos | Python-Kultur, Rust `lib.rs`+`main.rs` |
| M7b | **Signaturfamilie**: `fn main(): void` (Exit 0), `fn main(): int` (Exit-Code), beide optional `throws` (O4); **kein `args`-Parameter** — `std.env.args()` | Rust, Go |
| M7c | Name bleibt `main`; **kein `@Entry`-Attribut in dieser Fassung** — *vorläufig bis Bereich 9*, der entscheidet, was Attribute dürfen (die 4.x-Prämisse „Attribute tun nichts" ist für 5 **nicht** übernommen) | — |
| M7d | **Auswahl gehört dem Artefakt**: Manifest/`build.lyr` nennt das Entry-Modul (`executable("name", entry: …)`), Konvention `main.lyr`; mehrere Executables je Paket. Ob und wie `build.lyr` bleibt: **Bereich 11**; hier nur *ein Artefakt = ein Entry-Modul* | Rust, Zig |
| M7e | **Keine Top-Level-Statements** (C# 9) — Skript-Ergonomie ist Lyric-Script (Z3) | — |
| M7f | Exit-Codes: `int` = Code, `void` = 0, entkommener Fehler = 1, Panik = 101; `os.exit(n)` jederzeit ohne `defer`/`using` | Rust, Go |
| M8 | Reservierte Gerätenamen als Modulname: Lint (Bereich 11) | — |

## V2 — Sichtbarkeit: **entschieden** (2026-09-29)

**Drei Stufen** (Arbeitsnamen): **`private`** — dieses Modul; **`internal`** — dieses Paket,
**der Default**; **`pub`** — exportiert. Der Default ist die häufigste Bedeutung: in einer App
teilen Dateien ohne Wort, eine Bibliothek schreibt Wörter nur, wo sie eine Absicht hat
(exportieren, verstecken). Swift (`internal` Default), Go (paketweit); Rust's `private`-Default
verworfen (`pub`/`pub(crate)`-Rauschen in jeder App).

| # | Regel | Vorbild |
|---|---|---|
| S0 | **Member folgen der Typregel**: ohne Wort `internal`, `pub` exportiert, `private` modulintern. `struct Point { x: int, y: int }` ist in einer App vollständig; an der Exportgrenze `pub x`. Enum-Varianten wie das Enum; Extension-Methoden wie Member | Swift |
| S0a | **Felder: keine Lese-/Schreibtrennung** (Bereich 8, Y1 — `pub(read)` verworfen): `pub var` ist außen les- und schreibbar, `pub` (unveränderlich, M2) nur lesbar; „innen schreibbar, außen lesbar" ist ein Getter (Rust, Go); Property-Zucker als Y9-Tür | Rust, Go |
| S1 | Sichtbarkeit wird **bei jeder Namensauflösung** geprüft — qualifiziert, selektiv, Pattern, Konformanzliste, Constraint, `extend`-Ziel, `extern`; **eine** Implementierung im Symbolmodell (schließt den zentralen 4.x-Befund) | — |
| S2 | **Privater Typ in exportierter Fläche ist ein Fehler** (`pub fn make(): Secret`) | Rust E0446 |
| S3 | `pub` auf einem Member eines nicht exportierten Typs ist erlaubt (exportiert, sobald der Typ es wird) — **mit Warnung** „Member `x` ist sichtbarer als sein Typ `Y`" | Rust (ohne Warnung) |
| S4 | **Interface-Anforderungen** (Methoden, statische Member, assoziierte Typen) sind so sichtbar wie das Interface — ein Modifikator dort ist ein Fehler (ein Interface *ist* seine Memberliste; eine Pflicht ohne Aufrufer wäre sinnlos). **Private Helfer mit Rumpf** im Interface sind erlaubt, aufrufbar nur aus Default-Methoden desselben Interfaces; kein Konformer implementiert sie | Rust, Swift; Java 9 (private interface methods) |
| S5 | **`extend`-Blöcke**: inhärent `extend Foo { … }` — jede Methode mit eigener Stufe; ein Modifikator am Block ist der **Default für die Methoden darin** (`private extend Foo { … }`). Konformanz `extend Foo :: [Bar]` — die Konformanz ist **global**, sichtbar wo `Foo` und `Bar` sichtbar sind, nicht einschränkbar; Modifikator am Block ist ein Fehler. Grund: Kohärenz (X3) — zwei paketprivate `Hashable`-Konformanzen wären zwei Hash-Funktionen für einen `Set<Foo>` | Swift `private extension`; Rust/Swift (Konformanzen global) |
| S6 | Die Bibliotheks-Wurzelregel (Reachability, L11) folgt der Sichtbarkeit: exportiert = erreichbar, inklusive Member | — |

## V3 — Importformen, Re-Export, Prelude: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| I1 | drei Formen bleiben: selektiv `import a.b { f, g }`, qualifiziert `import a.b;`, Alias `import a.b as x` | Rust, Python |
| I2 | **Rename** im selektiven Import: `{ f as g }` | Rust, Python |
| I3 | **kein Glob**; Rusts Hauptfall (`use Color::*`) deckt das implizite Member `.Red` (T9) | Go, Rust (entmutigt) |
| I4 | **Re-Export `pub import a.b { Client }`** — ein Paket kuratiert seine Fläche im Wurzelmodul; ein Import ohne `pub` ist nie außerhalb sichtbar | Rust `pub use` |
| I5 | Nichtnutzungswarnung **einheitlich** für alle Formen | — |
| I6 | mehrere Formen für ein Modul koexistieren ohne Namenskollision; identischer Doppelimport Fehler | Lyric 4 |
| I7 | Importe sind Top-Level-Deklarationen an jeder Stelle; der Formatter zieht sie nach oben und sortiert | Go |
| I8 | **Prelude = Modul `std.prelude`**, in jedem Modul gebunden (Liste Bereich 10: mind. `panic`, `assert`, `unreachable`, `Error`, `Result`, `Box`, `Slice`, `StringView`, Range-Typen, Kern-Interfaces). Lokaler Name verdeckt Prelude-Namen → **Warnung** (heute still). Fest verdrahtete Helfer (`std.string.concat` für `+`) verschwinden — Operatoren über Interfaces (D6) | Rust, Swift/Kotlin |

## V4 — Kapselungseinheit und Tests: **entschieden** (2026-09-29)

**Das Paket ist die Einheit** (`internal` paketweit, `private` modulweit, V2). Tests:

| Wo | Sieht | Vorbild |
|---|---|---|
| **`@Test` im selben Modul** neben dem Code | alles, auch `private` | Rust `#[cfg(test)] mod tests`, Zig |
| **`tests/`-Root als Teil des Pakets** — dieselbe Kompilation, keine Fremdkompilation, die den Source-Root importiert (halbiert den gemessenen Doppel-Compile in CI) | `internal` und `pub` | Go (`_test.go` im Paket) |
| Blackbox | ein eigenes Paket im Workspace, das das geprüfte importiert — ein Muster, kein Mechanismus | Rust `tests/`, Go `foo_test` |

Testcode wird nicht ausgeliefert: Wurzeln der Reachability (L11) sind `main`/`pub`; `@Test`-
Funktionen sind nur unter `lyric test` Wurzeln — sonst toter Code. **Keine Bedingungs-
kompilierung** dafür (Rust `#[cfg(test)]` überflüssig); Plattformbedingungen: Bereich 9.

## V5 — Initialisierung und Globale: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| G1 | Ein Modul hat **keinen Rumpf** — nur Deklarationen; ein Import führt nichts aus | Go, Rust |
| G2 | Modul-`let`/`var` **eager beim Programmstart**, in Abhängigkeitsreihenfolge (Importe zuerst), innerhalb eines Moduls in Deklarationsreihenfolge; Zyklen Fehler; nur erreichbare Module (L11) | Go |
| G3 | Ein Initializer darf nicht werfen: `try!` oder Panik | Rust `static`, Go |
| G4 | **`Lazy<T>` als Bibliothek** (über `Once`, N10) — kein Sprachfeature | Rust `LazyLock`, Kotlin |
| G5 | **Top-Level-`var` erlaubt** (`internal` per Default); unter Threads ohne `Atomic`/`Mutex` → Warnung (N10) | Go, Kotlin, Swift |
| G6 | Kein `init()`-Hook (versteckter Kontrollfluss) | Go's `init` verworfen |

## V6 — Namensräume und Kollisionen: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| K1 | **Modul- und Typnamensraum überlappen nicht**: kein Top-Level-Name, der ein Untermodul benennt — Fehler am Modul; `a.b.c` eindeutig | Rust |
| K2 | **Mangling aus dem vollen Pfad** mit unterscheidbaren Trennern (C4); generische Instanzen tragen den **vollen Typpfad** (`lib.ident<app.Secret>`) | — |
| K3 | Top-Level-Name, Import und Untermodul teilen **einen** Namensraum je Modul — Kollision = Fehler mit Note | Rust |
| K4 | Ein Local darf einen Modulnamen verdecken — **legal und still**; aber wo es beißt (Memberzugriff auf das Local, dessen Name kein Member des Locals, wohl aber ein exportierter Name des verdeckten Moduls ist), trägt der Fehler eine **Note** „`path` ist hier das Local vom Typ `string` (Zeile 12) und verdeckt das Modul `std.path`"; dazu ein **Lint, standardmäßig aus**, für jede Verdeckung. „Immer warnen" verworfen: `path`, `time`, `json`, `log` sind als Local- wie Modulnamen häufig — Rauschen erzieht zum Ignorieren | Go `vet -shadow` |
| K5 | **Builtin-Typnamen** nicht deklarierbar — Fehler | — |
| K6 | Prelude-Funktionen verdecken → Warnung (I8) | — |
| K7 | Modulpfad-Tippfehler bekommt einen Vorschlag | Bereich 11 |

## V7 — Pakete: **entschieden** (2026-09-29)

**`lyric.toml`** als deklaratives Manifest (von Werkzeugen ohne Ausführung lesbar), `build.lyr`
für Logik (ob und wie: Bereich 11) — die Cargo-Trennung, von Zig (`build.zig.zon`) nachvollzogen.
Verworfen: JSON/JSONC (Korpus W18: kein Standard), Manifest in der Sprache (nicht ohne
Ausführung lesbar).

| # | Regel | Vorbild |
|---|---|---|
| P1 | `name` (Root-Segment, M3), `version` (Semver), `lyric` (Sprachversion, V9) | Cargo, Go |
| P2 | `[dependencies]` mit Versionsanforderung (`"1.2"` = `^1.2`), Pfad, Git; Registry-Quelle später als dieselbe Eintragsform | Cargo |
| P3 | **Genau eine Version je Paket im Programm** — whole-program, Monomorphisierung, Kohärenz (X3) vertragen keine zwei `foo`; ein nebeneinander lebender Major ist ein anderer Paketname (`foo2`) | Go (semantische Import-Versionierung); Rust (Duplikate) verworfen |
| P4 | **Minimal Version Selection**: das kleinste, das alle Anforderungen erfüllt; deterministisch ohne Solver; Upgrade nur per `lyric update` | Go MVS; Cargos Solver verworfen |
| P5 | **`lyric.lock`** mit aufgelöster Menge und **Inhalts-Hashes**, eingecheckt (MVS macht die Auswahl, das Lock den Inhalt reproduzierbar) | Go `go.sum` |
| P6 | **Nur deklarierte Abhängigkeiten importierbar** (heute: flacher Abschluss gibt jedem alles) | Cargo, Go |
| P7 | **`[override]`** ersetzt ein Paket durch einen Pfad; das ersetzte wird **nicht gelesen** | Cargo `[patch]`, Go `replace` |
| P8 | **Keine Registry in 5.0** (Pfad + Git; LUECKEN C-06); Manifest registry-fähig, statischer HTTP-Index als Tür | Cargo sparse index, Go proxy — Bereich 11 |
| P9 | Features, Workspaces, Vendoring: Türen in Bereich 11 | Cargo |
| P10 | Ein Paket ist ein Quellbaum (L7); der Cache liegt beim Nutzer | Go |

## V8 — Bibliotheksform und öffentliche Fläche: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| B1 | Ein Paket ist ein Quellbaum (L7); alle Module per Pfad importierbar; die **Fläche** = `pub`-Deklarationen samt exportierten Membern (S6); ein Modul ohne `pub` exportiert nichts | Go, Rust |
| B2 | Kuratierte Fläche über `pub import` im Wurzelmodul (I4) | Rust `lib.rs` |
| B3 | **`lyric api`** zeigt die Fläche; `--diff <version>` als Semver-Tür | `cargo semver-checks`, `apidiff` — Bereich 11 |
| B4 | `///`-Doku an `pub`-Deklarationen; `lyric doc` | Bereich 11 |
| B5 | **C-Bibliotheken als Pakete**: nativer Teil (`extern "C"`-Deklarationsmodule + C-Quellen oder `lib.a` je Ziel) im Manifest (`[native]`), statisch gelinkt (C7); FFI-Details Bereich 11 | Rust `-sys`, Zig |
| B6 | Reachability-Wurzeln (L11): Executable → `main` des Entry-Moduls; C-ABI-Artefakt (`lib.a`) → `pub extern`-Exporte (Bereich 11); ein Bibliotheks-Bau für Lyric-Nutzer ist Prüfung plus Cache | — |

## V9 — Toolchain- und Sprachversion im Paket: **entschieden** (2026-09-29)

**Beides**: `toolchain = "5.3"` (Minimum, Cargo `rust-version`, Lyric 4) **und** **Edition**
`lyric = "5"` — „in der Sprache dieser Edition geschrieben"; der Compiler beherrscht alle
Editionen, ein Programm mischt Pakete verschiedener Editionen; eine Edition ändert nur
**Oberfläche und Defaults** (Syntax, Warnstufen, Prelude), nie ABI oder Typidentität (Rust
Editions, Go's `go`-Zeile). Der Sinn: Sprache verändern, **ohne dafür einen Major zu brauchen**,
solange keiner ansteht — 5.0 ist auf absehbare Zeit der letzte Major, nicht für immer (00). 5.0
hat eine Edition; das Feld existiert ab Tag eins. `lyric fix --edition` migriert. `lyric
toolchain`-Verb: Bereich 11.

## V10 — `std` als Paket: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| D1 | **`std` wird mit der Toolchain ausgeliefert und mit ihr versioniert**; Root `std` reserviert | Go, Rust |
| D2 | `std` ist Quelle (L7), in den Cache des Nutzers kompiliert; C-Schicht `liblyr.a` (L9); „fünf Module je Bau parsen" trifft den Cache | — |
| D3 | `std.prelude` (I8) ist ein gewöhnliches Modul | Rust |
| D4 | `std` darf **Compiler-Intrinsics** nutzen, die Nutzerpakete nicht sehen — gebunden an den Paketnamen | Rust `core`/`#[lang]` |
| D5 | Umfang und Ringe (Bibliotheks-Umkehr): Bereich 10 | — |

---

**Bereich 7 ist damit vollständig entschieden** (V1–V10, 2026-09-29).
