# 03 — Abgeleitete Konformanz (`derive`): Equatable / Hashable / Ordered / Display

Zugehöriger language-review-Punkt: *automatische Equatable/Hashable/Printable-Ableitung* (eigener Kandidat, bis language-review einen Titel nennt).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 72 (davon 42 handgeschriebene Methoden) |
| `soll.lyr` | PROPOSAL, kompiliert nicht | 38 |
| `vergleich.rs` | Rust `#[derive(...)]` | 30 |
| Lib-Variante | **nicht möglich**: ohne Reflexion/Feldaufzählung kann keine Bibliothek über die Felder eines fremden Typs iterieren; eine `extend`-Variante müsste die Felder trotzdem aufzählen | — |

## Was heute nötig ist
Ein 3-Feld-Wertetyp als Map-Schlüssel + sortierbar + druckbar = **4 Methoden, 30 Zeilen**, die
alle dieselbe Feldliste wiederholen (`equals`, `hash`, `compare`, `show`). Für ein Enum mit
Payload ist `equals` ein verschachteltes `match` über beide Seiten (8 Zeilen für 3 Varianten,
quadratisch in der Variantenzahl, wenn man es lesbar hält). Auffällig: `Equatable` wird über
`Hashable`s Parent impliziert, `equals` muss trotzdem geschrieben werden — und **niemand prüft,
dass `hash` zu `equals` passt** (core.lyr:156 sagt es ausdrücklich).

## Soll-Syntax und Grammatik (Primärform: Swift-Modell, mit language-review abgestimmt)
**Keine Grammatikänderung.** Ein Struct/Enum deklariert `:: [Equatable<Self>]` (Hashable, Ordered,
Display) und schreibt die Methode NICHT — der Compiler synthetisiert sie. Heute ist das
`LYR-SEM0020` ("does not implement 'equals'", `TypeChecker.cs` Konformanzprüfung); genau diese
Stelle wird zur Synthese-Stelle: statt des Fehlers wird für die vier bekannten Interfaces ein
Körper erzeugt, und der Fehler bleibt nur, wenn ein FELD die Eigenschaft nicht hat (Meldung am
Feld). Eine geschriebene Methode gewinnt (Guide 7: "its own member wins", wie bei Default-Methoden).
Unit-Enums sind immer synthetisierbar. Generische Typen brauchen die Constraint geschrieben
(`Pair<T :: [Equatable<T>]>`) — nichts implizit.

- **Spec**: §5.1 neuer Absatz "synthesized conformance" (welche Interfaces — die vier aus
  `std.core`, geschlossene Liste, dem Compiler bekannt wie `Throwable`; Regel je Interface;
  Fehler am Feld), §6.2 unverändert (der Operator bleibt der Methodenaufruf).
- **Variante B (verworfen als Primärform)**: `@Derive { … }` — Guide 15 / Spec §4.7 legen fest,
  dass ein Attribut beschreibt und nichts tut; eine Ableitung erzeugt Code. Zudem trägt
  `AttrArgs` keine Typen (`Equatable<Coord>`). Variante B' `:: [derive Equatable<Coord>]`
  (kontextuelles Wort in der Liste; parsebar über `Parser.Declarations.cs:756` + `AtContextual`
  `:792`, eindeutig) wäre expliziter — der Preis ist ein neues Wort für etwas, das die Konformanz
  ohne Body schon eindeutig ausdrückt. Swift zeigt, dass die implizite Form in der Praxis
  nicht überrascht: wer `equals` schreibt, bekommt seins; wer nicht, bekommt das Offensichtliche.

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Struct mit 3 Feldern, 4 Konformanzen | 33 Z. | 5 Z. | 2 Z. |
| Enum mit 3 Varianten, 2 Konformanzen | 20 Z. | 5 Z. | 2 Z. |
| Gesamtprogramm | 72 Z. | 38 Z. | 30 Z. |

- **Fehlerklassen verhindert**: (1) `hash` inkonsistent zu `equals` (ein Feld in einem der beiden
  vergessen) — der stille Klassiker, der als "Map verliert Einträge" auftaucht; (2) ein neues Feld,
  das in `equals`/`show` nicht nachgezogen wird; (3) `compare` inkonsistent zu `equals`
  (`compare == 0` aber `!equals`), was `sortList` + `Set` gemeinsam bricht.
- **Aufwand**: Lexer 0; Parser klein (~15 Z.); Sema mittel: Eintrag markieren, Feldtypen auf die
  Eigenschaft prüfen (die Constraint-Prüfung `Conformance.cs` existiert), Kollision mit eigener
  Methode; **Lowering groß-mittel**: Synthese von vier Methodenkörpern als AST oder direkt als IR
  (~250 Z.) — für Enums mit `match` über beide Seiten die meiste Arbeit; Display braucht
  `std.string`-Konverter (fromInt etc., die der f-String heute schon bindet, §6.6); VM 0; stdlib:
  ggf. `std.core.combineHash(a, b)` als benannte Formel (siehe Hinweis an stdlib-review).
- **Breaking**: nein (Minor). `derive` ist heute ein gewöhnlicher Identifier; das Wort wird nur in
  einer Position kontextuell, in der es heute ein Parsefehler wäre (zwei Identifier ohne Komma).
- **Wechselwirkungen**: Generics — `struct Pair<T> :: [derive Equatable<Pair<T>>]` verlangt
  `T :: [Equatable<T>]` als implizite Constraint (Rust: Bound je Derive; Vorschlag: der Compiler
  fügt die Constraint hinzu ODER verlangt sie explizit; letzteres ist Lyric-typischer: nichts
  passiert implizit); Optionals — ein `?T`-Feld ist `Equatable`, wenn `T` es ist (null == null,
  null != v), das ist heute nicht als Interface-Konformanz vorhanden (§6.2: zwei Optionals
  vergleichen ist `LYR-SEM0059`!) → derive Equatable muss diese Regel selbst enthalten; Klassen —
  `derive Equatable` auf einer `class` ist Wert-Gleichheit, nicht Referenz-Gleichheit; sollte
  erlaubt, aber dokumentiert sein; match/throws: keine.
- **Offene Semantikfragen**: (1) Display-Format: Initializer-Syntax (Vorschlag, weil
  round-trip-fähig und eindeutig) oder `Coord(1, 2, a)`? (2) Darf ein Feld ausgeschlossen werden
  (Rust: nein, manuell impl)? Vorschlag: nein, wer ausschließen will, schreibt die Methode. (3)
  Arrays als Felder: `T[]` ist heute nicht `Equatable` — derive müsste elementweise vergleichen
  oder das Feld refusen; Vorschlag: refusen mit Hinweis (`LYR-SEM-neu`), bis Arrays eine
  Konformanz haben. (4) `Ordered` für Enums mit Payload: Tag-Reihenfolge, dann Payload — wie Rust.

## Empfehlung
**Sprachfeature, Minor, hoher Nutzen** — die einzige der bisherigen Kandidaten, die sich NICHT
als Bibliothek nachbauen lässt und die eine stille Fehlerklasse (hash ≠ equals) strukturell
beseitigt. Reihenfolge: Equatable + Hashable zuerst (Map/Set-Schlüssel sind der häufigste
Bedarf), Display zweitens, Ordered zuletzt.
