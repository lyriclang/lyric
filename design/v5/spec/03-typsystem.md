# 03 — Typsystem-Kern

Lebendes Dokument des Bereichs 3. Fragen T1–T19, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` (Typidentität über Deskriptoren, `??T` darstellbar, Innenzeiger, Enums inline,
Monomorphisierung mit Instanz-Cache) und `02-wertmodell.md`.

## Bestandsaufnahme Lyric 4

Aus `docs/Grammar.md`, Spec §3/§8, Korpus (`../generics.md`, `../skalare.md`,
`../optionals.md`):

- **Skalare**: `int`/`uint`/`float` 64-bit-Defaults, *distinkt* neben `int64`/`uint64`/`float64`;
  `int8…64`, `uint8…64`, `float32/64`, `bool`, `char` (Code-Punkt), `string`. Keine implizite
  Konversion, `as` einzige Brücke; Literale adaptieren an den Kontext (§3.1), Variablen nie.
  Überlauf definiert, nicht prüfbar (§3.2 verspricht `checked`, nie gebaut).
- **Nominal, strikt invariant, kein Subtyping** (§3.7): `?Circle` wird nicht `?Shape`, auch
  nicht lesend (gemessen). Polymorphie nur über Interface-Werte.
- **Optionals**: `?T`, kein Nesting (`??T` — die VM hatte keinen Platz), `!`, `??`, `?.`,
  Narrowing. Gemessen: im generischen Rumpf beantworten `== null`/`??` (kompilieren) und
  `!`/`match null` (abgelehnt) dieselbe Frage verschieden. Iterator-Ende ist `null` → kein
  `Iterator<?T>`.
- **Generics**: Monomorphisierung bedarfsgetrieben; `::`-Constraints, **an der Deklaration
  geprüft** (Rust/Swift/C#); ~90 B und ~0.5 ms je Instanz, linear, Verifier teuerste Achse;
  Inferenz vorwärts, erste Bindung gewinnt, kein LUB, keine Kontext-Inferenz. Funktioniert:
  generische Methoden auf generischen Typen (4.6), F-Bounds, Mehrfachkonformanz,
  deklarationsseitige bedingte Konformanz (`struct Pair<T :: [Equatable<T>]> ::
  [Equatable<Pair<T>>]`).
- **Fehlt** (gemessen): `Self`; statische Interface-Member; assoziierte Typen; generische
  Extends und die `extend`-Form bedingter Konformanz; Varianz; höhere Kinds; Wertparameter;
  parametrisierte Aliase; Default-Typargumente; partielle Typargumentlisten; instanziierte
  Funktion als Wert; `throws E` mit substituiertem Typparameter (Generics × Fehler rot);
  Typargumente im Pattern-Pfad.
- **Still oder falsch**: `<T :: [int]>` filtert nicht; Phantom-Typparameter ohne Warnung;
  `f<Box<int>>(x)` Parserfehler (`>>`), Formatter zerstört den Workaround; divergente
  Monomorphisierung stürzt in einer Form ab; `a<b>(c)` mit drei `int`s ist ein Aufruf.
- Tupel ab Arität 2; Funktionstypen; `type` transparent, `opaque type` neue Identität; `T[]`
  fester Länge, Länge im Wert; Ranges nur im Schleifenkopf; `extend` auf Arrays/Opaque stiller
  No-Op.

## T1 — Skalare, Literale, Konversion: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| T1a | **`int` = `int64`, `uint` = `uint64`, `float` = `float64` — Aliase**, ein Typ mit zwei Namen (C#-Form). Alle Ziele sind 64-bit; die Distinktheit (Go, Swift, Lyric 4) kaufte 32-bit-Portabilität, die wir nicht brauchen, und kostete zwei Instanzen je Generik, zwei FFI-Zuordnungen und `as` zwischen gleich breiten Typen | distinkt |
| T1b | Literale adaptieren an den Kontext, Default `int`/`float`; **Bereichsprüfung** (`let x: uint8 = 300` Fehler); Suffixe, Unterstriche, Hex/Bin/Okt bleiben | — |
| T1c | **Verlustfreie Ganzzahl-Weitung implizit** (Zig, Java): in jeden Ganzzahltyp, dessen Bereich den Quellbereich enthält (`int8→int16→int32→int64`, `uint8→…`, `uint8→int16`, `uint32→int64`); `float32→float64`; **nie** Verengung, **nie** `int`→`float` (auch nicht verlustfreie Fälle — „Ganzzahl bleibt Ganzzahl"). **Nur an Koerzionsstellen** (Zuweisung, Argument, Rückgabe), nie in der Inferenz; Überladung braucht einen Weitungsrang (Bereich 4) | keine implizite Konversion (Rust, Swift, Go, Kotlin, Lyric 4 — `as` bei jedem FFI-Puffer); `int`→`float` implizit (C#'s Präzisionsfalle über 2⁵³) |
| T1d | **`as`** bleibt der eine, immer gelingende, bit-nahe Operator: Ganzzahl-Verengung **wrappend**, `float`→`int` **sättigend**, NaN → 0 (Rust); `char`↔`uint32` explizit, `uint32`→`char` prüft den Skalarwert; `bool`↔Zahl **nie**. **Geprüfte Verengung als Methode** (`n.toInt8(): ?int8` o. ä., Form Bereich 5) | zweiter Operator |
| T1e | `char` ist Unicode-Skalarwert, **keine Zahl** (keine Arithmetik, kein Vergleich mit Ganzzahlen); `bool` keine Zahl | Go `rune`, C |
| T1f | `int128`/`uint128`: Tür (clang/`zig cc` liefern `__int128`); `float16`/`bfloat16`: Tür; **kein `usize`** (C `size_t` ↔ `uint` an der FFI); `decimal` Bibliothek | — |

## T2 — Überlauf: **offen**
## T3 — Subtyping und Varianz: **offen**
## T4 — Optionals, Nesting, Null im generischen Rumpf: **offen**
## T5 — `Self` und statische Interface-Member: **offen**
## T6 — Assoziierte Typen: **offen**
## T7 — Generische Extends, bedingte Konformanz in `extend`-Form: **offen**
## T8 — Inferenz: **offen**
## T9 — Vereinigungstypen gegen Enum ohne Zeremonie: **offen**
## T10 — `any`: **offen**
## T11 — Downcast und Typ-Patterns auf Interface-Werten: **offen**
## T12 — `inout`/`ref`-Parameter, `ref`-Rückgabe: **offen**
## T13 — Arrays und Ranges als Typen mit Membern, Views, Länge im Typ: **offen**
## T14 — Index-Familie: **offen**
## T15 — Aliase, `opaque type`, Newtype: **offen**
## T16 — Tupel: **offen**
## T17 — Funktionstypen: **offen**
## T18 — Typparameter-Hygiene: **offen**
## T19 — Grenzen der Monomorphisierung: **offen**
