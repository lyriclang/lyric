# 18 — Array-Konstruktion ohne erstes Element (`arrayOf(n, f)` / `[n] of …`)

Zugehöriger language-review-Punkt: **"Array-Konstruktion braucht ein erstes Element: `[x] * n`"** (MEDIUM, qol). Wie gewünscht beide Formen skizziert, Empfehlung stdlib zuerst.

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 15 — Sonderfall + Schleife ab 1, Dummy-Slot, Iterator-Kette als heutige Lib-Variante | 30 |
| `soll.lyr` | PROPOSAL — Form A (stdlib `arrayOf`/`arrayFilled`), Form B (`[n] of (i) => …`) | 22 |
| `vergleich.kt` | Kotlin `Array(n) { i -> … }` | 16 |
| Lib-Variante | **heute**: `collectArray(range(0, n).map(f))` (in `ist.lyr`) — funktioniert, kostet zwei Imports und drei Allokationen; **Form A** ist die saubere Lib-Variante, braucht aber EIN Native | — |

## Ist-Stand
`mapArr` hat drei Stolpersteine, die alle aus "kein Array ohne erstes Element" folgen: der
Leer-Sonderfall (`return []` — nur mit Kontexttyp gültig), `f(xs[0])` als Füllwert (läuft einmal
extra oder die Schleife startet bei 1 — beides leicht falsch), und für Struct-Arrays ein
Dummy-Element mit erfundenen Werten (`Slot { id = -1 }`), das fachlich nicht existiert. Lyric hat
keinen Nullwert (`default(T)`), also ist `[x] * n` heute konsequent — nur ist x oft nicht da.

## Soll — Form A (stdlib + ein Native, empfohlen)
```lyr
pub fn arrayOf<T>(n: int, f: fn(int) -> T): T[];      // Kotlin Array(n) { i -> f(i) }
pub fn arrayFilled<T>(n: int, x: T): T[];             // [x] * n, aber n == 0 ohne Element
```
- In Lyric selbst NICHT schreibbar (sie brauchen genau das leere Array fester Länge) → ein
  Native `rawArrayAlloc<T>(n)` in der VM, das `arrayOf` sofort füllt (kein beobachtbarer
  Uninitialisiert-Zustand: das Native ist privat, `arrayOf` ruft f für jeden Slot bevor es
  zurückgibt; ein werfendes f — Prototyp 06 — lässt das halbgefüllte Array verschwinden).
  Die VM kann das (`[x] * n` alloziert genauso), also ~15 Z. VM + ~20 Z. stdlib. Keine
  Sprachänderung, kein Spec-Kapitel außer §11 (zwei Signaturen).
- `arrayFilled(0, x)` löst auch `let xs = [];` ohne Annotation (§7.1) für den generischen Fall.

## Soll — Form B (Sprache)
```
ArrayLit = '[' [ Expr { ',' Expr } [ ',' ] ] ']'
         | '[' Expr ']' 'of' Lambda .                   (* [n] of (i) => f(i) *)
```
`of` kontextuell nach `]` (heute Parsefehler dort → eindeutig; `of` bleibt sonst Identifier).
Vorteil gegenüber A: kein Import, Lambda-Typ aus dem Kontext (`(i) =>` ohne Annotation — bei A
geht das auch, weil `fn(int) -> T` den Parameter typisiert; nur T muss inferierbar sein).
Rusts `[x; n]` ist mit `[x] * n` schon abgedeckt. Ergebnis: B spart ein Wort und bringt eine
Grammatikregel — **nicht empfohlen**, solange A nicht als zu geschwätzig erlebt wird.

## Bewertung
| | Ist | Ist Lib (Iterator) | Soll A | Kotlin |
|---|---|---|---|---|
| `mapArr` | 5 Z., Sonderfall, f einmal extra | 1 Z., 2 Imports, 3 Allokationen | 1 Z. | 1 Z. |
| Struct-Array | Dummy + Schleife | 1 Z. | 1 Z. | 1 Z. |

- **Fehlerklassen verhindert**: Off-by-one bei "Schleife ab 1"; Seiteneffekt von `f` einmal zu
  viel (bei f mit I/O oder Zähler sichtbar); Dummy-Werte, die eine Prüfung überleben (`id = -1`).
- **Aufwand**: A: VM ~15 Z. (Native), stdlib ~20 Z., Spec §11 zwei Zeilen; B: Parser ~20 Z.,
  Sema ~30 Z., Lowering ~30 Z.
- **Breaking**: nein (beides Minor).
- **Wechselwirkungen**: Generics (T aus dem Lambda-Ergebnis inferiert, §8.3 Schritt 5 — der
  klassische "U aus dem Lambda"-Fall, funktioniert); Optionals (`arrayOf(n, (i) => null)` braucht
  Kontext für `?T` — wie heute `[null]`, siehe Bug aus Prototyp 06); throws (mit Prototyp 06:
  `arrayOf<T, E>(n, f: fn(int) -> T throws E): T[] throws E`); Coroutinen: keine.
- **Offene Semantikfragen**: (1) Name: `arrayOf` (Kotlin) vs. `Array.of`/`tabulate` (Scala) —
  stdlib-review entscheidet. (2) Soll `arrayFilled` das bestehende `[x] * n` ersetzen? Nein,
  ergänzen.

## Empfehlung
**stdlib reicht** (Form A mit einem Native). → stdlib-review informiert; kein Sprachfeature nötig.
