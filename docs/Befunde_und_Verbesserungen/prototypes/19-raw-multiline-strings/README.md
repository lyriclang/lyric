# 19 — Raw-Strings (`r"…"`, `r#"…"#`) und Mehrzeilen-Strings (`"""…"""`)

Zugehöriger language-review-Punkt: **"Keine Raw-/Mehrzeilen-Strings"** (MEDIUM).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 22 |
| `soll.lyr` | PROPOSAL (rein lexikalisch) | 30 |
| `vergleich.rs` | Rust (`r#"…"#`, `\`-Zeilenfortsetzung), Swift als Kommentar (`"""`-Einzugregel) | 20 |
| Lib-Variante | nicht möglich (Lexik); `join(lines, "\n")` ist die heutige Notlösung für (2) | — |

## Ist-Stand
Ein JSON-Objekt mit zwei Feldern und einem Array braucht **elf** escapte Anführungszeichen; ein
sechszeiliger usage-Text ist sechs Literale, fünf `+` und fünf `\n`, von denen eines beim
Umsortieren der Zeilen verloren geht; ein Windows-Pfad verdoppelt jeden Backslash. Alles
funktioniert — und ist in jedem CLI-Tool, jedem Test mit Textdaten und jeder JSON-Nutzung
(`Guide 13: parse("{\"name\": …")`) täglich zu lesen. §1.6 sagt ausdrücklich "no raw or
multiline string form" — eine Entscheidung; der Prototyp misst ihren Preis.

## Soll — Lexer (Spec §1.5/§1.6, Grammatik §1.5)
```
RawStringLit   = 'r' { '#' } '"' { any-char } '"' { '#' } .     (* schließt bei '"' + gleich vielen '#' *)
MultiLineStr   = '"""' [ Newline ] { any-char | EscapeSeq } '"""' .
InterpolatedML = 'f' MultiLineStr .
```
- **Eindeutig**: `r"` ist heute `IDENTIFIER("r")` + `StringLit` — zwei Primaries ohne Operator
  sind ein Parsefehler (`Parser.cs` ParseExpr) → frei; das Muster ist exakt das, mit dem `f"`
  gelöst ist (Lexer erkennt `f` direkt vor `"`; `Lexer.cs`, InterpolatedStr). `"""` lext heute als
  `""` + `"…` (offener String → Lexerfehler) → frei. `r#"` ist heute `r` `#`?? — `#` ist kein
  Token in Lyric (§1.6) → Lexerfehler → frei.
- **Raw**: keine Escapes, keine Löcher (`rf"…"`/`fr"…"` für Raw + Interpolation: nicht in Runde 1;
  Python hat es, Rust nicht — bei Bedarf additiv).
- **Mehrzeilig, Swift-Regel**: Inhalt beginnt nach dem Zeilenumbruch hinter `"""`; der Einzug
  (Leerraum-Präfix) der Zeile mit der schließenden `"""` wird von jeder Zeile entfernt; eine
  Zeile mit weniger Einzug ist ein Lexerfehler (nicht still); der letzte Umbruch vor `"""` gehört
  nicht dazu; `\` am Zeilenende schluckt den Umbruch; Escapes und Löcher (`f"""`) wie in `"…"`.
  Zur LEXZEIT, nicht wie Kotlins `trimIndent()` zur Laufzeit — der String ist eine Konstante.
- **Formatter** (`lyrfmt`): Lexeme unverändert übernehmen — der Einzug IST Inhalt; ein Formatter,
  der den Block einrückt, ändert den Wert. Test nötig. TextMate-Grammatik (`tooling/textmate`,
  gegen den Lexer gepinnt) muss die drei Formen lernen.
- **Spec**: §1.5 (drei Produktionen), §1.6 (Satz streichen), §1.7 (Einzugregel); Guide 2.

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| JSON-Literal | 11 Escapes | 0 | 0 |
| 6-Zeilen-Text | 6 Literale + 5 `+` + 5 `\n` | 1 Literal | 1 Literal (`\`-Fortsetzung) |
| Windows-Pfad | 3 `\\` | `r"…"` | `r"…"` |

- **Fehlerklassen verhindert**: vergessenes `\n` beim Umsortieren; falsch escapte `"` in JSON
  (die häufigste Ursache "invalid JSON" in Tests); `\U`/`\u` in Pfaden als Escape fehlgelesen.
- **Aufwand**: Lexer ~80 Z. (Raw ~20, Mehrzeilig mit Einzugregel ~50, f-Variante ~10);
  Parser 0 (dieselben Token-Arten `StringLiteral`/`InterpolatedStr`); Sema 0; Lowering 0; VM 0;
  Formatter ~10 Z.; TextMate-Grammatik; LSP-Tokens.
- **Breaking**: nein (Minor) — alle drei Formen sind heute Fehler.
- **Wechselwirkungen**: f-Strings (§1.5 Interpolation gilt in `f"""`; `{{`/`}}` bleiben);
  Attribute (§4.7: ein Raw-/Mehrzeilen-String ist ein Literal und damit als Attributwert erlaubt
  — nützlich für Hilfetexte in `@Command { help = """…""" }`); `*`/`+` auf Strings: unverändert;
  Doc-Kommentare: keine.
- **Offene Semantikfragen**: (1) `"""` einzeilig (`"""a"b"""`) erlauben, nur um `"` zu vermeiden?
  Swift: nein (muss mehrzeilig sein); Vorschlag: nein, dafür ist `r#"…"#` da. (2) Tabs vs.
  Leerzeichen im Einzug: der Präfix muss byteweise gleich sein (Swift-Regel), gemischt → Fehler.
  (3) CRLF-Quellen: Umbrüche im Inhalt werden zu `\n` normalisiert (§1.1 kennt `\r\n`).

## Empfehlung
**Sprachfeature, Minor, rein lexikalisch, ~100 Zeilen** — hoher Alltagsnutzen, null Risiko für
Parser/Sema. → lexer-parser wäre der Umsetzer (language-review hat es dort schon gemeldet).
