# 12 — Benannte Argumente (`f(name: value)`)

Zugehöriger language-review-Punkt: **"Keine benannten Argumente — Defaults sind nur positional nutzbar"** (MEDIUM).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 — positional, Options-Struct, und der `f(x = 3)`-Beweis | 34 |
| `soll.lyr` | PROPOSAL (`:`-Trenner) | 16 |
| `vergleich.kt` | Kotlin (`=`), Swift als Kommentar (`:`, Pflicht-Labels) | 14 |
| Lib-Variante | das Options-Struct-Idiom IST die Bibliotheksvariante — in `ist.lyr` (B) | — |

## Ist-Stand — fair betrachtet
Das Options-Struct (`ConnectOptions { timeoutMs = 500 }`) ist **gut**: benannt, Reihenfolge frei,
Defaults im Struct, weglassbar, und die Initializer-Syntax ist der Aufrufsyntax anderer Sprachen
ebenbürtig. Preis: +5 Zeilen Deklaration pro Funktion mit Optionen, ein zweiter Typ im Namensraum,
und der Aufrufer muss wissen, welche Funktion die Struct-Form hat. Die positionale Form ist dort
gefährlich, wo gleichtypige Parameter nebeneinander stehen: `connect("a", 3, false, 80)` vertauscht
`port` und `retries` und kompiliert. Und `connect("c", port = 8080)` **kompiliert heute** — als
Zuweisung an die lokale `port` plus Übergabe von 8080 (`port is now 8080`): das ist der Grund,
warum die Soll-Syntax `=` nicht verwenden darf.

## Soll-Syntax und Grammatik (§6.2)
```
CallArg = [ IDENTIFIER ':' ] Expr .
```
- **Eindeutig**: `f(x: 3)` ist heute `LYR-PAR0002` (nach einem Identifier-Ausdruck kann kein `:`
  folgen — Lyric hat keinen Ternär-Operator und keine Typannotation im Ausdruck). Parser:
  `Parser.cs:429 ParseArguments` — Zwei-Token-Lookahead `Identifier Colon` vor `ParseExpr`.
  Struct-Initializer (`Name { f = v }`) behalten `=` — zwei verschiedene Dinge (Feld vs.
  Parameter), zwei verschiedene Zeichen; Swift trennt genauso (`init(x: 1)` vs. Zuweisung).
- **Regeln** (§7.1a): benannte nach positionalen; Name muss existieren; keine Doppelbelegung;
  fehlender Parameter ohne Default → Fehler; `params` nicht benennbar; nur bei Aufrufen einer
  DEKLARIERTEN Funktion/Methode — ein Funktionswert hat keine Parameternamen (§7.1a sagt das
  bereits für Defaults/params, dieselbe Begründung).
- **Überladung** (§4.3a, `TypeChecker.cs:1673 SelectOverload`): Kandidaten ohne einen genannten
  Namen scheiden vor dem Zählen aus; dann die bestehenden vier Regeln. Neue Mehrdeutigkeit:
  `f(a: int)` und `f(b: int)` (heute gleiche Signatur → Redeklaration, §4.3a "parameters the
  SAME") — Namen bleiben bei der Redeklarationsprüfung OHNE Bedeutung, sonst würde ein
  Umbenennen eines Parameters ein neues Überladungspaar erzeugen.
- **Parameternamen werden API** (Swift-Lektion): ein Umbenennen bricht Aufrufer. Für die stdlib
  heißt das: Namen wie `value`, `separator`, `count` sind ab dann Vertrag (§11) — stdlib-review
  sollte die Parameternamen der öffentlichen Signaturen einmal durchsehen (`substring(start, count)`
  vs. `slice(from, to)` — Prototyp 09).
- **Spec**: §2/§6.2 (CallArg), §7.1a (Regeln), §4.3a (Auflösung), §11 (Namen als Vertrag).

## Bewertung
| | Ist positional | Ist Options-Struct | Soll | Kotlin |
|---|---|---|---|---|
| nur `timeoutMs` setzen | 4 Defaults abschreiben | 1 Z. + 5 Z. Deklaration | 1 Z. | 1 Z. |
| Vertauschung gleichtypiger Parameter | **still** | unmöglich | unmöglich | unmöglich |
| Aufrufer muss Signatur kennen | Reihenfolge + Werte | Struct-Name | Namen | Namen |

- **Fehlerklassen verhindert**: vertauschte gleichtypige Argumente; abgeschriebene Defaults,
  die bei Änderung der Signatur veralten (der Default ändert sich, der Aufrufer hat den alten
  Wert hart kodiert); die `f(x = v)`-Falle (Zuweisung statt Benennung) — die bleibt als Falle
  bestehen, wird aber durch die `:`-Form für Benennung entschärft (Linter-Hinweis "assignment in
  argument position" wäre sinnvoll, unabhängig vom Feature).
- **Aufwand**: Parser klein (~20 Z.); Sema mittel (~120 Z.: Argument-Zuordnung nach Namen VOR
  der Default-Auffüllung, Überladungsfilter, Diagnosen); Lowering 0 (die Sema ordnet die
  Argumente in Deklarationsreihenfolge, das Lowering sieht positionale Argumente); VM 0;
  stdlib 0 (Namen prüfen). LSP: Signature-Help zeigt Namen bereits.
- **Breaking**: nein (Minor) — aber die Namen der öffentlichen Parameter werden ab dann Teil der
  Kompatibilitätszusage (weich breaking für spätere stdlib-Umbenennungen).
- **Wechselwirkungen**: Überladung (s.o.); Generics (Inferenz §8.3 läuft nach der Zuordnung —
  ein benanntes Lambda-Argument wird trotzdem zuletzt typisiert); `params` (nicht benennbar);
  Defaults (der Hauptnutzen); Lambdas/Funktionswerte (ausgeschlossen); Methoden/Statics: gleich.
- **Offene Semantikfragen**: (1) Positionale NACH benannten erlauben, wenn eindeutig (Python:
  nein, C#: seit 7.2 ja)? Nein — Swift/Kotlin-Regel, einfacher. (2) Externe vs. interne Namen
  (Swift `_ host`)? Nein — ein Name. (3) Soll die Doku-Generierung (`tools/DocGen`) Namen als
  Vertrag markieren? Ja, bei Annahme.

## Empfehlung
**Sprachfeature, Minor, mittlerer Aufwand, mittlerer Nutzen** — das Options-Struct-Idiom ist
heute eine akzeptable Bibliothekslösung, weshalb die Priorität hinter 04/05/06/10 liegt. Wenn
angenommen, dann mit `:` und der Swift/Kotlin-Regel "benannte nach positionalen".
