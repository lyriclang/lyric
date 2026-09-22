# 17 — Member-Sichtbarkeit (`pub` auf Feldern und Methoden wird durchgesetzt)

Zugehöriger language-review-Punkt: **"`pub` auf Membern wird nicht durchgesetzt; Felder können gar nicht `pub` sein — es gibt keine Kapselung"** (HIGH, MAJOR-Kandidat).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist/bank.lyr` + `ist/app.lyr` | läuft (`lyric run ist/app.lyr`), exit 0 — und genau das ist das Problem | 20 + 16 |
| `soll/bank.lyr` + `soll/app.lyr` | PROPOSAL — `pub owner`, die drei Verstöße als Kommentar mit erwarteter Diagnose | 20 + 10 |
| `vergleich.rs` | Rust (`mod bank`, modulprivate Felder, E0616/E0624/E0451) | 30 |
| Lib-Variante | nicht möglich (Sichtbarkeit ist Sprachsache) | — |

## Ist-Stand — gemessen
`app.lyr` schreibt `a.balance = -999`, ruft die nicht-`pub` Methode `a.log()` und baut mit dem
Initializer ein `Account` mit einer Million — alles kompiliert, Ausgabe
`after tampering: -999 audit 2 / forged 1000000`. `pub fn read()` und `pub mut fn deposit()` in
`bank.lyr` sind Dekoration: `TypeChecker.cs:1590-1593` prüft `Visibility` nur für
MODUL-qualifizierte Namen (`bank.open`), nie für `x.member`; ein Feld kann syntaktisch kein
`pub` tragen (Grammatik §3.2 `Field = IDENTIFIER ':' TypeExpr`). Die Spec sagt in §4.2 "Only pub
declarations cross a module boundary" — Member sind keine Deklarationen im Sinne dieses Satzes,
und nirgends steht, was `pub` auf einem Member bedeutet. Die stdlib ist der größte Betroffene
(`xs.count = 0` auf `List`, language-review-Repro).

## Soll-Syntax und Grammatik
```
Field         = [ 'pub' ] IDENTIFIER ':' TypeExpr [ '=' Expr ] .      (* §3.2, §3.3 *)
StructVariant = '{' Field { ',' Field } [ ',' ] '}' .                 (* Enum-Felder: immer pub — s.u. *)
```
- **Parser**: `ParseStructBody` (`Parser.Declarations.cs:464-478`) liest `[pub] [static] [mut] fn`
  und `[pub] static let`; `pub` vor einem Feldnamen ist heute ein Fehler → frei.
- **Semantik (§4.2 neu)**: ein Member ohne `pub` ist im DEKLARIERENDEN MODUL sichtbar (Rust:
  modulprivat — freie Funktionen desselben Moduls wie `open` dürfen zugreifen; Kotlin/C#-
  typprivat wäre enger und würde das `open`-Muster verbieten). Mit `pub`: überall, wo der Typ
  sichtbar ist. Gilt für Felder (lesen UND schreiben), Methoden, `static let/fn`.
- **Initializer** (§6.2 StructInit): außerhalb des Moduls dürfen nur pub-Felder genannt werden;
  ein nicht-pub Feld ohne Default macht den Initializer außerhalb unmöglich → Konstruktorfunktion
  (gewollt: die Invariante entsteht nur dort). Enum-Struct-Varianten (`Rect { w, h }`) sind immer
  pub — Varianten werden gematcht, nicht gekapselt (Rust: dito).
- **Interfaces**: ein Member, das eine Anforderung eines pub-Interfaces erfüllt, ist über das
  Interface erreichbar, auch ohne `pub` am Member (die Anforderung ist der Vertrag; Rust-Regel).
  Einfacher wäre: Interface-erfüllende Member MÜSSEN `pub` sein (Fehler sonst) — expliziter,
  aber mehr Tipparbeit. Empfehlung: Rust-Regel.
- **Pattern-Matching** (§7.6): `Point { x, y }` außerhalb des Moduls liest Felder — nur pub-Felder
  dürfen im Muster stehen (sonst könnte man privat lesen). Konsequent, aber zu dokumentieren.
- **Diagnose**: `LYR-SEM-neu`: "member 'balance' of 'Account' is not public (declared in module
  'bank')"; für Initializer: "field 'balance' … — use a constructor function the module provides".
- **Embedding (§11, Rows)**: der Host liest Felder über Layout-Rows, nicht über `pub` — unberührt;
  `lyrembed` sollte aber die Sichtbarkeit in den Metadaten mitführen (Format-Zusatz, additiv).
- **Deprecation-Uhr** (Muster `LYR-SEM0093`, §3.5): 4.x — Zugriff auf ein nicht-pub Member eines
  fremden Moduls ist eine WARNUNG; 5.0 — Fehler. Die stdlib markiert in 4.x alle
  lesbaren Felder `pub` (`Packet.payload`, `IoError.kind`, `Exception.text` …) — das ist
  Vorarbeit für stdlib-review (jedes Feld einzeln entscheiden).

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Invariante schützbar | **nein** | ja | ja |
| `pub` an Membern | Dekoration | Vertrag | Vertrag |
| Kosten pro Typ | 0 | `pub` vor jedem exportierten Feld/Member | dito |

- **Fehlerklassen verhindert**: Fremdzugriff auf Interna (stdlib `List.count`), umgangene
  Konstruktoren (ungültige Zustände), API-Drift (heute ist JEDES Feld API — kein Feld der stdlib
  kann je umbenannt werden, ohne Nutzer zu brechen; mit `pub` gibt es eine Grenze, hinter der
  Refactoring frei ist — das ist der eigentliche Gewinn für die Bibliothek).
- **Aufwand**: Parser klein (~10 Z.); Sema mittel (~150 Z.: Sichtbarkeit bei `MemberExpr`,
  Initializer-Feldern, Feld-Patterns, Statics, Interface-Ausnahme, Warnung-mit-Uhr); Resolver
  speichert Visibility bereits (`Resolver.cs:134`); Lowering 0; VM 0; **stdlib groß**: jeder
  öffentliche Typ muss seine Felder klassifizieren (Schätzung: ~80 Felder in 15 Modulen);
  Docs (DocGen) zeigt nur pub-Member.
- **Breaking**: **JA — Major** (5.0), mit Warnung ab 4.x. Jedes Programm, das heute ein
  nicht-pub Feld/eine nicht-pub Methode eines fremden Moduls berührt, bricht; wie viele das sind,
  hängt davon ab, wie oft Nutzer stdlib-Interna angefasst haben (vermutlich selten, aber
  `Exception.text` ist ein Feld, das jeder liest — es MUSS `pub` werden, sonst bricht Guide 10).
- **Wechselwirkungen**: Attribute (§4.7: ein Attribut-Struct wird vom HOST gelesen — seine Felder
  müssen nicht pub sein; Rows sind Format, nicht Sprache); `extend` in einem anderen Modul
  (sieht nur pub-Member — Rust-Regel; ein `extend` im selben Modul sieht alles); Synthese
  (Prototyp 03) sieht alle Felder, weil sie im Typ selbst stattfindet; Interfaces (s.o.);
  Generics: keine; LSP: Completion zeigt außerhalb nur pub-Member.
- **Offene Semantikfragen**: (1) Ein drittes Level (`pub(crate)`-artig, paketweit)? Nein — Lyric
  hat keine Pakete-Hierarchie mit Kindern; Modul reicht. (2) `pub` auf Enum-Varianten? Nein,
  immer pub. (3) Default-Sichtbarkeit von Feldern eines `pub struct` OHNE `pub`-Felder: privat
  — bedeutet, ein bestehendes `pub struct Point { x: int, y: int }` verliert außerhalb seine
  Felder; Migrations-Warnung "struct 'Point' is pub but has no pub fields" hilft.

## Zuarbeit stdlib-review (Feldklassifikation)
`prototypes/stdlib-field-visibility.md`: alle 104 Felder der 15 Module klassifiziert — 19
Vertragsfelder (`Exception.text`, `Deprecated.message/until`, `Utf8Error.offset`,
`EncodingError.offset/expected`, `JsonError.line/column/offset/expected`,
`IoError.kind/path/detail`, `Packet.bytes/host/port`, `TimeError.text`), 85 private. Zwei
Anforderungen an die Semantik, beide durch das Rust-Modell (MODUL-privat) abgedeckt:
(a) `Map*Iterator`/`SetIterator` lesen private Felder ihres Containers — legal, solange sie im
selben Modul `collections.lyr` stehen; (b) compiler-gebundene Initializer (`RangeIterator`,
`ArrayIterator`, `StringIterator`, §4.4) werden vom Lowering synthetisiert und laufen nicht durch
die Sema-Prüfung — trotzdem als explizite Ausnahme in §4.2 nennen. Spec §11 Punkt 2 sollte die
Felder von `Exception`/`Deprecated` als Vertrag benennen.

## Empfehlung
**Sprachfeature, MAJOR (5.0) mit 4.x-Warnung** — das einzige Feature der Liste, das nicht
additiv ist, und zugleich das mit dem größten strukturellen Nutzen für die stdlib-Evolution.
stdlib-review sollte die Feldklassifikation vorbereiten (welches Feld ist Vertrag?).
