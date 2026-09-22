# 10 — Shadowing / Redeklaration einer Lokalen im selben Block

Zugehöriger language-review-Punkt: **"Redeklaration einer Lokalen im selben Block wird still verworfen — die ERSTE Bindung gewinnt"** (HIGH, bug).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `probe-silent.lyr` | kompiliert (nur SEM0071-Warnung) und druckt **1 statt 11** — der Bug | 7 |
| `ist.lyr` | läuft, exit 1 — neue Namen pro Schritt; `var`-Variante; inneres Shadowing | 36 |
| `soll.lyr` | PROPOSAL Variante A (Rust-Shadowing); Variante B als Kommentar | 28 |
| `vergleich.rs` | Rust (Variante A), Kotlin als Kommentar (Variante B) | 16 |

## Ist-Stand
Eine Verfeinerungskette (`?string` → `string` → getrimmt → geparst) braucht heute pro Schritt
einen neuen Namen (`filled`, `trimmed`, `parsed`). Die `var`-Form spart Namen, verliert aber: ein
`var x: ?string` bleibt nach `x = x ?? d` ein `?string`, weil Narrowing an Identifiern hängt und
jede Zuweisung es beendet (§7.4) — also `x!` auf einen Wert, der nie null sein kann. In einem
INNEREN Block funktioniert Shadowing bereits (`inner 11 / outer 1`); nur derselbe Block verwirft
die zweite Bindung still (`TypeChecker.cs:1040`: Rückgabewert von `TryDeclare` ignoriert — genau
die Form, die STATUS.md für Patterns mit `LYR-SEM0097` gerade geschlossen hat).

## Soll — Variante A (empfohlen): Shadowing erlauben
- **Grammatik**: unverändert.
- **Spec §7.1**: neuer Satz — *"A `let`/`var` may bind a name an earlier binding of the same
  block bound; from that statement on the name denotes the new binding, and the earlier one is
  unreachable."* Die Initializer-Seite sieht noch die alte Bindung (`let x = x ?? d;`).
- **Sema**: `TypeChecker.cs:1040` — der neue Eintrag ersetzt den alten im Block-Scope
  (`Redeclare` statt `TryDeclare`); die Unused-Warnung der alten Bindung wird unterdrückt, wenn
  der neue Initializer sie liest. Narrowing und Definite Assignment hängen am Symbol — automatisch
  korrekt. Lambdas, die die ALTE Bindung gefangen haben, behalten sie (Capture am Erstellungsort,
  §7.3) — das ist Rusts Verhalten und braucht keinen Sonderfall.
- **Lowering**: jedes Binding hat heute schon ein eigenes Lokal; nichts zu tun.
- **Formatter/LSP**: "go to definition" muss die jeweils sichtbare Bindung wählen — der Resolver
  tut das über den Scope bereits.

## Soll — Variante B: Redeklaration ist ein Fehler
`LYR-RES0001` auf Blöcke ausdehnen (Resolver `Resolver.cs:155` kennt die Meldung für Modul/Typ);
Meldung "redeclaration of 'n' in the same block". Sicher, ein Zweizeiler in der Sema — verbietet
aber das Verfeinerungs-Idiom, das in Lyric wegen des Identifier-gebundenen Narrowings besonders
wertvoll ist.

## Bewertung
| | Ist | Soll A | Rust | Kotlin (B) |
|---|---|---|---|---|
| Verfeinerung in 3 Schritten | 3 Namen | 1 Name | 1 Name | 3 Namen |
| Optional auffüllen ohne `!` | `let y = x ?? d` (neuer Name) | `let x = x ?? d` | `let x = x.unwrap_or(d)` | `val y = x ?: d` |
| Falsches Verhalten möglich | **ja (still 1 statt 11)** | nein | nein | nein |

- **Fehlerklassen verhindert** (beide Varianten): stilles Verwerfen einer Bindung mit falschem
  Programmverhalten — heute ein HIGH-Bug. Variante A zusätzlich: `!` auf nie-null-Werte,
  Namens-Inflation (`name1`, `name2`), versehentliche Weiterverwendung der alten Bindung (sie ist
  unerreichbar).
- **Aufwand**: A: Sema ~20 Z., Spec 1 Absatz; B: Sema ~5 Z., Appendix-Zeile. Beide Patch-fähig.
- **Breaking**: A: nein — heute kompilierende Doppel-`let`-Programme sind alle fehlerhaft gemeint
  (die zweite Bindung ist tot); ihr Verhalten ÄNDERT sich (von falsch zu gemeint). Streng
  genommen eine Verhaltensänderung → im CHANGELOG als Fix führen. B: Programme mit Doppel-`let`
  brechen mit Fehler — auch ein Fix.
- **Wechselwirkungen**: Narrowing (A macht das Idiom `let x = x ?? d` zum Standard und entschärft
  §7.4s Identifier-Grenze); Optionals (s.o.); Lambdas (alte Capture bleibt); match/throws/Generics:
  keine; `var`-Shadowing (`let n = 1; var n = n;`) ebenfalls erlaubt.
- **Offene Semantikfragen**: (1) Shadowing eines PARAMETERS im Funktionsblock (`fn f(x: ?int) { let x = x ?? 0; }`)
  — ja, derselbe Fall (Rust erlaubt es). (2) Warnung bei Shadowing OHNE Bezug auf die alte Bindung
  (`let n = 1; let n = 2;`)? Vorschlag: Hinweis-Level, weil fast immer ein Tippfehler.

## Empfehlung
**Variante A als Fix (Patch)** — der Bug muss ohnehin behoben werden, und A behebt ihn in die
nützlichere Richtung. Kein stdlib-Anteil.
