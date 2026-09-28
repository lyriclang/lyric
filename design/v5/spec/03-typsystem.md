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

## T1 — Skalare, Literale, Konversion: **offen**
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
