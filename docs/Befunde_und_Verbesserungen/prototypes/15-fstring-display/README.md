# 15 — f-Strings rendern Display-Typen (`f"{p}"` ruft `show()`)

Zugehöriger language-review-Punkt: **"f-Strings rendern keine Display-Typen"** (HIGH, qol).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 28 |
| `soll.lyr` | PROPOSAL (keine Grammatikänderung) | 26 |
| `vergleich.rs` | Rust (`{p}` → Display) | 22 |
| Lib-Variante | nicht möglich (Desugaring-Regel im Compiler) | — |

## Ist-Stand
`println(p)` nimmt einen Display-Typ (`console.lyr`: `println<T :: [Display]>`), das Loch eines
f-Strings nicht (§6.6: "there is no implicit Display call in interpolation", `TypeChecker.cs:1399-1408`
→ `LYR-SEM0006`). Folge: `.show()` in jedem Loch mit eigenem Typ — bei `std.time` (`Instant`,
`Duration`), `IoError`, jedem Enum mit Display. Guide 2 dokumentiert das ausdrücklich als Regel
("f"{at}" is refused") — es ist eine Entscheidung, keine Lücke; der Prototyp misst, was sie kostet.

## Soll — nur eine Sema-Regel
- **Grammatik**: unverändert (§1.5 `Interpolation = '{' Expr [ ':' FormatSpec ] '}'`).
- **§6.6 neu**: ein Loch ohne Spezifizierer desugart (1) für Skalare wie heute zu `fromXxx`,
  (2) sonst zu `value.show()`, wenn der Typ `Display` erfüllt (Conformance-Prüfung wie bei
  `println`), (3) sonst `LYR-SEM0006` mit Hinweis "conform to Display". Ein Spezifizierer auf
  einem Display-Typ ist ein Fehler (Display kennt keine Specs). Opaque-Aliase bleiben refused
  (§3.5: bewusst — der Alias soll nicht als seine Zahl erscheinen; ein Alias mit eigener
  Display-Konformanz könnte erlaubt werden, ist aber heute nicht ausdrückbar, §3.5 "satisfies
  no constraint").
- **Sema**: `TypeChecker.cs:1399` — nach der Skalar-Prüfung `Conformance.Satisfies(type, Display)`
  und Bindung des `show`-Members als synthetischer Aufruf; Lowering sieht einen gewöhnlichen
  Methodenaufruf (0 Änderung).
- **Interface-Werte** (`d: Display` als Loch): `d.show()` ist ein vtable-Aufruf — erlaubt,
  konsistent mit `println(d)`.

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Loch mit eigenem Typ | `{p.show()}` | `{p}` | `{p}` |
| Konsistenz mit `println` | nein | ja | ja |

- **Fehlerklassen verhindert**: keine harte; die Regel ist heute konsistent-streng, nur
  ungleich zu `println`. Der Wert ist Lesbarkeit und die ENTFALLENDE Sonderregel im Guide.
- **Aufwand**: Parser 0, Sema ~25 Z., Lowering 0, VM 0, stdlib 0, Spec §6.6 ein Absatz, Guide 2
  ein Absatz gestrichen.
- **Breaking**: nein (Minor) — jedes heute gültige Programm bleibt gültig; `f"{p}"` war ein Fehler.
- **Wechselwirkungen**: Prototyp 03/07 (synthetisiertes `show` macht das Loch für Structs/Enums
  ohne Handarbeit nutzbar — die beiden Features multiplizieren sich); Optionals (`f"{opt}"` —
  `?T` ist kein Display; Vorschlag: bleibt Fehler, "narrow or `?? default`"; Alternative "null"
  rendern wäre Kotlin-Verhalten, aber gegen §6.2/§7.4-Geist); Generics (`T :: [Display]` im
  Loch: Constraint reicht, direkter Aufruf); stdlib-review-Punkt "println(array) scheitert im
  IR" — dieselbe Conformance-Prüfung muss Arrays/Tupel/Optionals ABLEHNEN, sonst erbt das Loch
  den Bug (Sema-Prüfung ist dort heute zu lax).
- **Offene Semantikfragen**: (1) `{p:spec}` auf Display — Fehler (Vorschlag) oder Spec ignoriert?
  Fehler. (2) Soll `Display` für `?T` definiert werden (`null` → "null")? Nein.

## Empfehlung
**Sprachregel ändern, Minor, ~25 Zeilen Sema** — hoher Nutzen pro Aufwand, und die einzige
Stelle, an der `println` und f-Strings heute verschieden antworten. Voraussetzung: die
Display-Conformance-Prüfung muss Arrays/Optionals/Tupel korrekt ablehnen (stdlib-review-Bug).
