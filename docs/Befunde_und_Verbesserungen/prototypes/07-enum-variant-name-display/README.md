# 07 — Enum-Variantenname (`variantName()`) und synthetisiertes Display

Zugehöriger language-review-Punkt: **"Enums haben keinen abgeleiteten Namen/Display — Varianten-Namen werden dreimal von Hand geschrieben"** (MEDIUM). Teil (b) ist der Enum-Fall von Prototyp 03 (Synthese bei Konformanz ohne Body).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 0 | 46 (davon 30 Namens-/Parse-Funktionen) |
| `soll.lyr` | PROPOSAL | 16 |
| `vergleich.kt` | Kotlin (`.name`, `valueOf`, data-class `toString`), Zig als Kommentar (`@tagName`, `stringToEnum`) | 22 |
| Lib-Variante | nicht möglich: kein Zugriff auf Variantennamen zur Laufzeit; kein `extend` kann über Varianten iterieren | — |

## Ist-Stand
Zwei Enums, drei Hilfsfunktionen, 30 Zeilen: `show` (Name mit Payload), `kindOf` (nur der Tag),
`modeName`/`parseMode` (Hin- und Rückrichtung eines Unit-Enums). Jede Variante steht dreimal im
Programm. Die Rückrichtung hat `_ => null` und veraltet bei einer neuen Variante **still** — die
Exhaustiveness-Prüfung greift nur in der Hinrichtung. Der Modulname der Variante ist dem Compiler
bekannt (§13: das Enum trägt seine Namen für Attribute) und wird im Programm trotzdem als
String-Literal wiederholt.

## Soll-Syntax und Grammatik
- **(a) `e.variantName()`** — eingebautes Member, **keine Grammatikänderung** (`MemberExpr` + `CallArgs`).
  Sema: wie `next()` (§10, "a built-in member, not a method a type declares … exists only as a
  call") und `length` auf Arrays — eine Sonderregel im Member-Lookup für `EnumType`; ein vom
  Nutzer deklariertes `fn variantName()` hätte Vorrang? Vorschlag: **nein**, das eingebaute
  Member ist reserviert (Fehler bei Redeklaration), sonst hieße derselbe Name zwei Dinge.
  Lowering: neue IR-Op `EnumName` oder Desugar in `match` über alle Varianten mit
  String-Konstanten (~30 Z.; keine neue VM-Op nötig, wenn desugart). Spec §3.4 + §6 (Member).
- **(b) Synthese von `show`** bei `enum E :: [Display]` ohne Body — dieselbe Regel wie Prototyp
  03 (Swift-Modell, Spec §5.1 "synthesized conformance"): Unit → Name, Tupel → `Name(a, b)`,
  Struct → `Name { f = v }`; Payload-Felder müssen `Display` sein (sonst Fehler AM FELD).
- **(c) `E.fromVariantName(s): ?E`** — statisches eingebautes Member, nur für Enums ohne Payload
  (sonst Fehler bei Verwendung). Grammatik: `TypePath '.' IDENTIFIER` + CallArgs, existiert.
  Optional; deckt das `parseMode`-Muster, das in jedem CLI-Programm steht.

## Bewertung
| | Ist | Soll | Kotlin |
|---|---|---|---|
| Name mit Payload (4 Varianten) | 10 Z. | 0 Z. (`:: [Display]`) | 0 Z. (data class) |
| Nur Tag | 8 Z. | 0 Z. | 0 Z. |
| Unit-Enum hin/zurück | 12 Z. | 0 Z. | 0 Z. |

- **Fehlerklassen verhindert**: still veraltende Rückrichtung (`_ => null`) bei neuer Variante;
  Tippfehler in Namens-Strings ("Hangup" vs "HangUp"), die kein Compiler prüft; Divergenz von
  Deklarationsname und Anzeigename in Logs.
- **Aufwand**: Lexer/Parser 0; Sema klein für (a) (~40 Z. Member-Sonderfall + Reservierung),
  mittel für (b) (Teil der 03-Synthese); Lowering klein für (a) (Desugar, ~30 Z.) bzw. (c)
  (~40 Z.); VM 0; stdlib 0. Formatter 0.
- **Breaking**: (a) reserviert den Membernamen `variantName` auf Enums — ein Programm mit einer
  eigenen Methode dieses Namens bricht (theoretisch; nach Konvention Minor mit Hinweis, oder
  Name `tag()`? `tag` ist in Lyric der interne Begriff, §7.6 — Vorschlag: `variantName`, weil
  sprechend). (b)/(c) additiv.
- **Wechselwirkungen**: Generics (`Opt<int>.Some(3).variantName()` = "Some", unabhängig vom
  Typargument — pro Deklaration eine Tabelle, nicht pro Instanz); Optionals (`?E` hat das Member
  NICHT — erst narrowen; `?.variantName()` geht über §6.3); match: keine; Attribute (§4.7 nennt
  Unit-Varianten als Attributwerte — der Host liest "the variant's name and its tag"; dieselbe
  Tabelle kann (a) speisen).
- **Offene Semantikfragen**: (1) Name in Deklarationsschreibweise ("Safe") — ja, wie Kotlin/Rust;
  wer "safe" will, ruft `toLower()`. (2) Soll `show()` bei Payload-Struct-Varianten die
  Initializer-Syntax rendern (`Rect { w = 1, h = 2 }`) — konsistent mit 03, ja. (3) `variantName`
  auf einem Enum, das selbst `show` schreibt: unabhängig, beide existieren.

## Empfehlung
**(a) sofort als Minor** — winzig, kein Interface, deckt Logging/Debugging. **(b)** kommt mit
Prototyp 03 (ein Mechanismus). **(c)** optional; stdlib-review könnte alternativ ein
`std.enum`-Modul vorschlagen, das aber ohne (a) nichts synthetisieren kann — es bleibt ein
Sprachfeature.
