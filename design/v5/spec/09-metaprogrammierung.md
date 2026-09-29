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

## A1 — Was ein Attribut ist und darf: **offen**
## A2 — Deklarationsform, Ziele, Argumente: **offen**
## A3 — Attribut-Identität (qualifizierte Namen): **offen**
## A4 — `comptime`: Umfang, Werte, Budget, `embed`: **offen**
## A5 — Synthese: fest im Compiler oder nutzererweiterbar: **offen**
## A6 — Bedingungskompilierung: **offen**
## A7 — Typinformation zur Compile-Zeit, Enum-Reflexion: **offen**
## A8 — Makros: **offen**
## A9 — Doc-Tests: **offen**
## A10 — Warnungsunterdrückung: **offen**
## A11 — Die compilergelesenen Attribute: **offen**
## A12 — Generierter Code und Werkzeuge: **offen**
