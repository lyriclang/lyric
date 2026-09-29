# 09 — Metaprogrammierung: Attribute, `comptime`, Synthese

Lebendes Dokument des Bereichs 9 — vor Bereich 8 (Syntax) behandelt, weil hier noch
Schlüsselwörter entstehen können. Fragen A1–A12, je **entschieden** oder **offen**. Basis:
`04-abstraktion.md` D7 (Synthese über die Konformanzliste, *vorläufig bis hier*),
`07-module.md` M7c (kein `@Entry`, *vorläufig bis hier*), V4 (keine Bedingungskompilierung für
Tests), `01-laufzeit.md` L7 (IR interpretierbar als Tür — der `comptime`-Runner ist ein Kunde).

**Die 4.x-Prämisse „Attribute beschreiben und tun nichts" ist für 5 nicht übernommen**
(Maintainer, 2026-09-29). Sie wird hier neu entschieden.

## Bestandsaufnahme Lyric 4

Aus `../metaprogrammierung.md` (37 Fragen), Guide 15, `design/macros.md`,
`design/conformance-synthesis.md`:

- **Attribute**: ein Attribut *ist* ein Struct; die Platzierung entscheidet über
  Marker-Konformanzen `OnModule`/`OnType`/`OnFunction` (Elternkette erbt); Positionsform
  `WithArg<T>`; Gruppe `@[A, B(…)]`; Werte: Literale, Unit-Variante, benannter `let`. **Inert**
  — Abschnitt 11 des Formats ist „skippable". Das einzige compilergelesene Attribut ist
  `@Deprecated` (mit `until`, durchgesetzt seit 2.13 — der STATUS-Posten dazu ist eine Leiche);
  `@Test` liest der **Runner**, nach bloßem Namen — ein eigenes `struct Test :: [OnFunction]`
  wird ausgeführt. Attributierte Funktionen überleben DCE. Ziele: Top-Level-Fn, Struct, Class,
  Enum, Modulkopf; **Member nur `@Deprecated`**; nicht auf Interface, Global, Alias, `extend`,
  Parameter, Variante (mit Parser-Rauschen). `OnMethod` ist exportierte, wirkungslose Vokabel;
  `@NonExhaustive` verspricht eine Regel, die niemand prüft und niemand trägt.
  **Attributnamen sind unqualifiziert** — zwei `Tag` aus zwei Modulen kollidieren in der
  Host-API (dokumentiert).
- **`comptime e`**: gebaut, in `main`; wertet zur Compile-Zeit über den VM-Runner aus
  (`fib(30)` → Konstante, `fib` verschwindet). Ergebnis nur Skalar/String (kein Array, Struct,
  Enum — `SEM0100`); kein Zugriff auf Locals; Panik/Budget/Capability → `CT0002` ohne inneren
  Frame; `check` wertet nicht aus, `check --emit` schon. Zwei Begriffe von „Wert zur
  Compile-Zeit" (Attributwert vs `comptime`-Ergebnis), die sich nicht decken.
- **Es gibt nicht**: Konformanz-Synthese (Entwurf liegt), `embed()`, Enum-Reflexion,
  Doc-Tests (Rohmaterial: `///`-Bindung im Parser, Guide-Snippets werden kompiliert),
  Warnungsunterdrückung, `lyrfix`, Attribut-Vervollständigung im LSP, Laufzeitreflexion
  (bewusst), Makrosystem (bewusst), Bedingungskompilierung.

## A1 — Was ein Attribut ist und darf: **entschieden** (2026-09-29) — drei Arten, ein Wort

| Art | Was `@Name` tut | Wer liest es | Beispiele | Vorbild |
|---|---|---|---|---|
| **1 · Daten** | nichts — annotiert mit Werten für Host, Werkzeuge, Makros | Host, `lyric api`, Runner, Makros | `@Route { path = "/x" }`, SDK-Marker | Lyric 4, C#, Java |
| **2 · Compiler-Anweisung** | ändert, was der Compiler tut — **geschlossene Liste** in `std.core`, je mit Vertrag in der Spec (A11) | der Compiler | `@Deprecated{until, replacement}`, `@Test`, `@Inline`, `@MustUse`, `@NonExhaustive`, `@callerExpr` | Rust `#[inline]`, Swift |
| **3 · Makroanwendung** | erweitert die Deklaration um erzeugten Code (A8) | Compile-Zeit-Interpreter | `@Builder`, `@Retry(3)` | Rust Attribut-Makros, Swift attached macros |

| # | Regel |
|---|---|
| T1 | **Ein Name, eine Art** — die Spec nennt bei jedem Attribut seine Art |
| T2 | Art 2 ist geschlossen; Nutzer definieren Art 1 und 3; was eine Compiler-Anweisung bräuchte, ist ein Makro |
| T3 | Deklarationsform unterscheidet 1 und 3 (A2): Daten-Attribut = Struct mit Zielmarker; Makro-Attribut = `macro` mit Deklarationsparameter |
| T4 | **Kein `!` an Attributen**; Sichtbarkeit der Expansion über Editor-Anzeige und `lyric expand` (Rust: `#[derive]` und `#[serde]` sehen gleich aus, die Doku sagt es) |
| T5 | **Ein Attribut ändert nie Code, der es nicht trägt** — Hygiene auf Deklarationsebene (Swift) |
| T6 | Art-2-Attribute sind **identitätsgebunden** (A3): `std.test.Test`, nicht „irgendein `Test`" (der Runner-Befund) |
| T7 | Attribute an **allen** Deklarationen (Top-Level, Member, Parameter, Variante, Interface, Alias, `extend`, Modul); der Zielmarker sagt je Attribut, wo es sitzen darf |

Die 4.x-Sätze „Attribute tun nichts" und „nur `@Deprecated` ist compilergelesen" fallen —
ersetzt durch „jedes Attribut hat eine deklarierte Art".

**`comptime` und Makros — die Trennregel** (Maintainer-Nachfrage): beides existiert, auf
demselben Interpreter. `comptime` für **Werte** (Tabellen, geprüfte Konstanten) und **Rumpfcode,
der von Typinformation abhängt** (`comptime for (f in fields(Self))`, `comptime if`) — lesbar,
weil man normalen Code liest, der entfaltet wird; **Makros** für **Deklarationen** (Builder-Typ,
`extend`-Block), **Umwickeln** (`@Retry`) und **Syntax lesen** (`sql!("…")`). „Alles über
Makros" verworfen: die `ToJson`-Schleife als `quote`-Konstruktion wäre die Unlesbarkeit, die an
`comptime` stört, an jeder Stelle statt an manchen. (Nim: `static:`/`when` + `macro`)

## A2 — Deklarationsform, Ziele, Argumente: **offen**
## A3 — Attribut-Identität (qualifizierte Namen): **offen**
## A4 — `comptime`: Umfang: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| Q1 | **Ergebnistypen**: jeder Werttyp (Skalare, Strings, Arrays, Structs, Enums, Tupel, `?T`); **keine Referenzen** (Klassen, Closures, Koroutinen) — ein Heap-Objekt des Interpreters wird keine Konstante (4.x: nur Skalar/String) | Zig |
| Q2 | **Formen**: `comptime expr`, `comptime { … }` (Block, letzter Ausdruck ist der Wert), `comptime if` (**beide Zweige typgeprüft**, einer emittiert — nicht Zigs Überraschung), `comptime for (x in werte)` (Entfaltung über einen `comptime`-Wert) | Zig `inline for` |
| Q3 | **Reiner Code**: Funktionen rufen (laufen dann zur Compile-Zeit), Werte bauen, `std.meta`; **kein I/O, Netz, Zeit, Zufall** — Übersetzungsfehler an der Stelle | Zig |
| Q4 | **`embed("pfad")`**: Datei relativ zum Modul, innerhalb des Pakets, als `uint8[]`/`string` — die einzige Compile-Zeit-I/O; Build hängt von der Datei ab (Cache) | Zig `@embedFile`, Rust `include_bytes!` |
| Q5 | **Budget mit Zahl** je Auswertung, im Manifest konfigurierbar; Meldung nennt Limit und Stelle | Zig |
| Q6 | Panik im `comptime`-Code = Übersetzungsfehler **mit innerem Backtrace** (4.x: ohne) | Rust const eval |
| Q7 | **Determinismus**: dieselbe Eingabe, dasselbe Ergebnis auf jeder Plattform (Q3, L10); `target.os`/`target.arch` sind `comptime`-Konstanten (A6) | Zig |
| Q8 | **`lyric check` wertet aus** — der Interpreter ist Teil des Compilers, nicht der VM (4.x: `check` grün, `build` rot); im Editor gilt Q5 strenger | rust-analyzer |
| Q9 | Makro-Rümpfe laufen unter denselben Regeln | — |

## A5 — Synthese: **entschieden** (2026-09-29) — die Synthese gehört dem Interface

**Die Konformanzliste ist das `derive`.** Ein Interface schreibt seine Member als
`comptime`-Default-Rümpfe generisch über `Self` (`comptime for (f, i) in fields(Self) { … }`);
`struct Point :: [ToJson] { … }` bekommt sie, eine eigene Implementierung ersetzt sie (D7).
**Kein `@derive(X)`** (Rust braucht es, weil Trait und Synthese dort getrennte Dinge sind) und
**kein `@ToJson`** (ein Makro mit dem Namen des Interfaces kollidierte in K3's einem Namensraum;
Rusts Makro-Namensraum wird nicht übernommen). D7 ist damit begründet, nicht geerbt.

- `Equatable`, `Hashable`, `Ordered`, `Clone`, `Default` wandern als Interfaces mit
  `comptime`-Defaults nach `std.core`; **fest im Compiler** bleibt nur, was keine Bibliothek
  schreiben kann: `Debug` für alles automatisch, `Identity` (Adresse).
- **Attribut-Makros** (Art 3) sind für Codeerzeugung, die **keine Konformanz** ist — `@Builder`,
  `@Retry(3)`, `@Route { … }` (Art 1) — in der normalen `@[…]`-Liste; die Art steht in der
  Deklaration, nicht in der Schreibweise.
- Synthese für ein **fremdes** Interface (Rusts `serde_derive`-Fall): ein Attribut-Makro mit
  eigenem Namen (`@JsonVia`), das `extend P :: [ToJson] { … }` erzeugt — erlaubt, Nebenweg.

## A6 — Bedingungskompilierung: **offen**
## A7 — Typinformation zur Compile-Zeit: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| R1 | **`std.meta`** liefert Typinformation als `comptime`-Werte: `fields(T)` (Name, Typ, Sichtbarkeit, Attribute, Index), `variants(E)`, `members(T)`, `conformances(T)`, `typeName(T)`, `isStruct/isClass/isEnum(T)` | Zig `@typeInfo`, Nim |
| R2 | **Feldzugriff über Info** `this.[f]` (Arbeitsnotation) — statisch je Entfaltung gelöst | Zig `@field` |
| R3 | **Nur über bekannte Typen** (`fields(Self)`, `fields(T)` monomorphisiert); keine Reflexion über Objekte — Compile-Zeit, nicht Laufzeit | Zig |
| R4 | **Attribute sind Teil der Info** (`f.attributes` → Art-1-Daten): Nutzer steuern Synthese je Feld ohne Makro (`@Serialize { name = "id" }`) | serde, Swift `CodingKeys` |
| R5 | **Enum-Reflexion zur Laufzeit ist Synthese**: `E.variants()`, `E.fromName(s): ?E`, `e.name()` als `comptime`-generierte Member eines `std.core`-Interfaces, das ein Enum auf Anfrage konformiert | Rust `strum`, Swift `CaseIterable` |
| R6 | `typeName<T>()` als gefaltete Konstante; `Debug` (D7); **keine allgemeine Laufzeitreflexion** — Deskriptoren (V4) tragen Identität, keine Feldnamen; Feldnamen zur Laufzeit sind eine zur Compile-Zeit synthetisierte Tabelle | — |
| R7 | **Sichtbarkeit gilt auch für Typinformation**: private Member nur im deklarierenden Paket sichtbar (V2) — ein fremdes `ToJson` sieht nur `pub`, das Interface im selben Paket alles | strenger als Rust |

## A8 — Der Mechanismus: **entschieden** (2026-09-29) — die ganze Leiter, Sprosse 5

„`comptime` *oder* Makros" war falsch gestellt: ein Makrosystem nach Nims Modell ist
**`comptime` plus drei Dinge** (AST-Typen als Werte, `quote`/Einfügung, Aufrufformen) auf
demselben Compile-Zeit-Interpreter (L7). Entschieden: **alle fünf Sprossen für 5.0.**

| Sprosse | Was | Kostet |
|---|---|---|
| 1 | `comptime`-Werte und -Blöcke mit allen Werttypen, `comptime if`/`for` in Rümpfen (A4) | den Interpreter (vorhanden) |
| 2 | **Typinformation zur Compile-Zeit**, `comptime for` über Felder/Varianten (A7) — der Hybrid: Nutzer sieht Konformanz/Attribut, `comptime` steht in der Bibliothek | Typinfo-API |
| 3 | **`@callerExpr`** u. Verwandte (Quelltext des Arguments, Zeile, Datei als Parameterwert) — deckt `assert`/`expect`/Logging ohne Makro | compilergelesenes Attribut (C# `CallerArgumentExpression`, Zig `@src()`) |
| 4 | **`inline`-Funktionen mit nicht-lokalem `return`** — Blöcke, die wie Keywords wirken (`forEach { if (…) return }`) | eine Funktionseigenschaft (Kotlin) |
| **5** | **Makros**: Compile-Zeit-Funktionen über typisierten Syntaxbäumen (`std.syntax`: `Expr`, `Block`, `Ident`, `StructDecl` …), Ausgabe über **`quote { … }` mit `#{…}`-Einfügung, hygienisch**; **drei feste Aufrufformen** — Deklaration (`@derive(ToJson) struct …`, das Attribut *ist* die Anwendung), Ausdruck/Statement (`sql!("…")`, `retry!(3) { … }`), Block (Trailing-Lambda als `Block`-Argument); das Makro sieht Syntax **und auf Anfrage Typen** (`typeOf(expr)`, `fields(T)` — was Rust-proc-macros fehlt); Fehler zeigen auf `quote`-Stelle und Aufrufstelle; `lyric expand` zeigt erzeugten Code | AST als **stabile öffentliche API** (eigenes Spec-Kapitel), Hygiene, Expansionswerkzeug im Editor |

**Grenze: T1, nicht T3.** Ein Makro gibt existierenden Formen neue Bedeutung (Elixir, Julia,
Rust, Nim); es erzeugt Deklarationen, Ausdrücke, Statements — **keine neuen Grammatikformen**
(keine Keywords, keine Statement-Shapes, keine Operatoren). Erweiterbare Grammatik (Racket —
funktioniert nur durch Syntaxlosigkeit; Seed7, Fortress †, Perl-Source-Filter) verlangt, dass
jedes Werkzeug die Erweiterungen ausführt, um eine Datei zu lesen, und Grammatiken komponieren
nicht. C-Makros (Textersetzung: keine Typen, kein Scope, keine Hygiene) verworfen.

**Syntax** (Makrodeklaration, `quote`, Einfügung, `!`/`@`, `inline`) — **Bereich 8**.

## A9 — Doc-Tests: **offen**
## A10 — Warnungsunterdrückung: **offen**
## A11 — Die compilergelesenen Attribute: **offen**
## A12 — Generierter Code und Werkzeuge: **offen**
