# Lyric 5 — Gebiet: Strings, Zeichen, Formatierung

Stand der Messung: Checkout `6f6f029f` (Merge `release/v4.6.0-cut`), Debug-Binaries `lyrc.dll` /
`lyrvm.dll` / `lyric.dll` (für `disasm`) / `lyrfmt.dll`. Proben: erste Runde
`…/scratchpad/v5-design/probes/strings/` (`p00`–`p22`), Kritikrunde 1 `…/probes/strings-review/`
(`r01`–`r21`), Nachmessung `…/probes/strings-rev2/` (`m01`–`m10`, `cmp_*`, `ctrl_*`, `hash_*`,
`fs_*`), Kritikrunde 2 (fremd, nachgeprüft) `…/probes/strings-adv3/` (`a01`–`c04`), **diese
Fassung** `…/probes/strings-rev3/` (`n01`–`n23`, Erwartungen vorab in `ERWARTUNG.txt`).
Python-Vergleiche sind mit Python 3.14.4 auf dieser Maschine gemessen; rustc, cargo und go sind
**nicht** installiert, jede Aussage über Rust und Go ist deshalb „behauptet" (Sprachwissen) und so
gekennzeichnet.

Belegtypen: **gemessen** (Programm kompiliert und gelaufen), **gelesen** (Pfad:Zeile), **behauptet**.

---

## 1. Ist-Stand

### 1.1 Was die Sprache sagt

| Punkt | Beleg |
|---|---|
| Quelle ist UTF-8, BOM wird übersprungen; Zeilenenden sind `\n` und `\r\n` | `docs/Grammar.md:40` (gelesen) |
| `char` = „one Unicode code point", Beispiel `'\u{1F600}'` | `docs/guide/02-values-and-types.md:30` (gelesen) |
| `string` = „immutable UTF-8" | `docs/guide/02-values-and-types.md:31` (gelesen) |
| `+` verkettet, `*` wiederholt | `docs/guide/02-values-and-types.md:54` (gelesen) |
| Literale: `StringLit`, `InterpolatedStr`, `CharLit`, `EscapeSeq` | `docs/Grammar.md:103-109` (gelesen) |
| `{{`/`}}` = eine Klammer; `FormatSpec` läuft bis zur passenden `}`, **verschachtelte geschweifte, runde und eckige Klammern werden mitgezählt** | `docs/Grammar.md:115-117` (gelesen) |
| `FormatSpec` wird benutzt (`Grammar.md:105`), aber **nirgends produziert** | `docs/Grammar.md:100-117` (gelesen) |
| Loch nimmt Skalar **oder** `Display`; Spec auf `Display` ist ein Fehler | `docs/guide/02-values-and-types.md:84-89` (gelesen) |
| Jede Position zählt CODE POINTS, kein `s[i]`; `length()` ist ein Aufruf, **weil** es O(n) kostet | `stdlib/std/string.lyr:114-127`, `docs/guide/13-standard-library.md:653` (gelesen) |
| Bytecode: `lt/le/gt/ge` verlangen einen „numeric type", `eq/ne` zusätzlich `bool`, `char`, `string`; was „numeric" umfasst, definiert die Datei nicht — `char` ist Tag `0x0C` in derselben Tabelle wie `i8`…`f64` | `docs/Bytecode.md:586-596, 719-720` (gelesen) |
| `X` auf einer negativen Zahl rendert **absichtlich die BITS**; `formatHex` ist die Gegenfunktion | `stdlib/std/fmt.lyr:22-25` (gelesen) |
| `std.fmt.formatInt/Uint/Float/Bool/Char/String(value, spec: string)` sind **öffentliche Natives mit einem Laufzeit-String als Spec** | `stdlib/std/fmt.lyr:26-37`, `src/Lyric.Vm/NativeRegistry.cs:640-685` (gelesen) |
| `string.hash()` ist FNV-1a-64, in Lyric geschrieben, ausdrücklich damit es „the same result everywhere" liefert | `stdlib/std/core.lyr:300-316` (gelesen) |
| `char` ist `Ordered<char>` in `std.core`, und `compare` benutzt `<`/`>` auf `char` direkt | `stdlib/std/core.lyr:213-223` (gelesen) |
| Ein Attributwert ist ein Literal **oder (seit v2.4) ein Name, der an ein solches `let` gebunden ist** | `docs/Grammar.md:199-201` (gelesen) |

### 1.2 Was der Compiler tut — und wo er von der Doku abweicht

**(a) Ein astrales `char`-Literal ist nur in Escape-Form schreibbar.** Gemessen (`p01`, bestätigt
`a01`):

```
error[LYR-LEX0008]: expected only 1 character in character literal, got 2      // let direct = '😀';
```

`'\u{1F600}'` wird akzeptiert, `'😀'` nicht — der Lexer zählt UTF-16-Einheiten
(`src/Lyric.Frontend/Lexing/Lexer.cs:590-596`, `contentCount++` pro `_pos++`). Die Diagnose sagt
„got 2" über einen Wert, den der Typ „one Unicode code point" nennt, und zwar über genau das
Beispiel, das die Doku hinschreibt. **Kontrolle** (`p02`): auf der String-Seite stimmt alles —
`"a😀b"` hat `length()==3`, `utf8Encode().length==6`, `for (c in s)` läuft 3 Runden. Der Defekt
sitzt allein im Char-Literal.

**(b) `\xNN` ist ein CODE-PUNKT-Escape, kein Byte-Escape.** Gemessen (`p03b`, bestätigt `a15`):
`'\xFF' as int` = 255, und `"\xE2\x82\xAC"` hat `length()==3` und **6** UTF-8-Bytes — drei
Zeichen statt des Euro-Zeichens. Es gibt keine Form, mit der man ein rohes Byte in ein Literal
schreibt.

**(c) Es gibt heute ZWEI Formatsprachen, und keine steht in der Spec.** Die erste Fassung schrieb
„erreichbar ist die ganze .NET-Formatsprache" — das gilt **nur für numerische Löcher**. Gemessen
(`n01a`, `n01b`, `n01c`; die Kritik hatte es mit `b11`/`c03` gefunden):

| Loch-Typ | Spec | Ergebnis |
|---|---|---|
| `char` | `{c:X}` | **Panik** `LYR-VM0006: 'X' is not a width — for this type a format spec is a number` |
| `string` | `{s:>8}` | **Panik** `'>8' is not a width …` |
| `bool` | `{true:5}` | `true ` (Breite, links) |
| `char` | `{c:5}` / `string` `{s:-5}` | `a    ` / `    x` |

Quelle (gelesen): `NativeRegistry.cs:2784-2833` — für `IFormattable`-Werte (Zahlen) greift
`IsWidth`, sonst `.NET ToString(spec)`; für `char`/`bool`/`string` gibt es **nur** `Padded`, und
alles außer Ziffern ist dort eine Panik. Damit ist die Sprache heute: **Zahlen: .NET-Spec oder
Breite; `char`/`bool`/`string`: nur Breite.** Der Doc-Kommentar in `stdlib/std/fmt.lyr:33-34`
(„A `bool` through a .NET specifier", „A `char` through a .NET specifier") ist gelesen **falsch**
— beide Funktionen kennen keinen .NET-Specifier, nur eine Breite. Das ist ein Doku-Fehler, den die
erste Fassung nicht gefunden hat (Anhang A #14).

Gemessen erreichbar auf **numerischen** Löchern (`p21`, `m04`, `m05`, `n20`):

| Quelle | Ausgabe |
|---|---|
| `f"{1234.5:#,##0.00}"` | `1,234.50` |
| `f"{42:'abc'}"` | `abc` — die Zahl verschwindet |
| `f"{0.5:P1}"` | `50.0 %` |
| `f"{3.7:C}"` | `¤3.70` — Währungszeichen in einer Sprache, die Kultur ablehnt |
| `f"{-255:X}"` | `FFFFFFFFFFFFFF01` (Bitmuster, absichtlich, `fmt.lyr:22-25`) |
| `f"{255:o}"` | **Panik** `LYR-VM0006: 'o' is not a valid format spec` — .NET hat kein Oktal |
| `f"{255:#x}"` | **`255x`** — `#` ist Platzhalter, `x` Literalzeichen (Rust/Python gäben `0xff`) |
| `f"{1234567:,}"` | **`` (leer)** — ein Komma am Ende ist in .NET ein Skalierungs-Operator (÷1000) ohne Platzhalter (Python gäbe `1,234,567`) |
| `f"{5:.2}"` | **`52`** |

Damit ist der Sprachumfang „was diese .NET-Version kann" — nicht reimplementierbar für ein
zweites Runtime (Lyricpp), nicht versionsstabil, nicht in der Spec.

**(c2) Der naheliegendste Spec der Welt liefert heute still Müll.** Gemessen (`m05`, Kontrolle `m10`,
bestätigt `a02`):

| Quelle | Ausgabe heute | Kontrolle | Rust gäbe | Python gäbe |
|---|---|---|---|---|
| `f"{3.14159:.2}"` | **`32`** | `f"{3.14159:F2}"` → `3.14` ✔ | `3.14` | **`3.1`** |
| `f"{3.14159:.3}"` | `33` | `f"{3.14159:N2}"` → `3.14` ✔ | `3.142` | **`3.14`** |

Grund: in einem .NET-**Custom**-Format ist `.` das Dezimaltrennzeichen und `1`–`9` sind
**Literalzeichen** (nur `0` und `#` sind Platzhalter). **Korrektur gegenüber der zweiten Fassung:**
die Spalte „Python/Rust gäben `3.14`" war für Python falsch. Gemessen mit Python 3.14.4:
`format(3.14159, '.2') == '3.1'`, `format(3.14159, '.3') == '3.14'` — in Python heißt `.N` ohne
Typ **N signifikante Stellen** (`g`-Semantik), in Rust **N Nachkommastellen**. Auf einem Integer:
Python `ValueError: Precision not allowed in integer format specifier` (gemessen); Rust
**ignoriert** die Präzision auf Integern still (behauptet, `std::fmt`-Doku: „For integral types,
this is ignored"). Die „Rust/Python-Sprache" der zweiten Fassung ist an genau dieser Stelle **zwei
Sprachen** — S32 entscheidet.

**(c3) NEU, gemessen — JEDES .NET-Custom-Format ohne Platzhalter gibt nur seine Literalzeichen
aus.** Die zweite Fassung verortete die stille Falschausgabe bei verschachtelten Klammern und
dynamischer Breite (Befund e). Das ist nur der Spezialfall einer allgemeinen Regel. Gemessen
(`n02`, Kontrolle in derselben Probe; die Kritik hatte es mit `c03`):

| Quelle (`n = 5`) | Ausgabe | Rust/Python gäben |
|---|---|---|
| `f"[{n:>8}]"` | `[>8]` | `[       5]` |
| `f"[{n:<8}]"` | `[<8]` | `[5       ]` |
| `f"[{n:*>8}]"` | `[*>8]` | `[*******5]` |
| `f"[{n:+}]"` | `[+]` | `[+5]` |
| `f"[{n:^8}]"` | `[^8]` | `[   5    ]` |
| Kontrolle `f"[{n:8}]"` / `f"[{n:-8}]"` | `[5       ]` / `[       5]` | — |

**Genau die für 5.0 empfohlene Syntax (`<`, `>`, `^`, Füllzeichen, `+`, `#`) liefert heute still
den Spec-Text und verschluckt den Wert.** Kein Fehler, keine Warnung. Diese Formen fehlten in der
S05-Warnklassentabelle der zweiten Fassung vollständig — jetzt Klasse 4 (S05) und S44.

**(d) Ein ungültiger Spec ist eine Laufzeit-Panik, obwohl er ein Quelltext-Literal ist.** Gemessen
(`p06`, `p16`, `m04`, `n18`):

```
panic [LYR-VM0006]: 'Q9' is not a valid format spec
panic [LYR-VM0006]: 'N2' is not a width — for this type a format spec is a number
```

Zwei Dinge daran. Erstens ist der Spec im f-String eine Konstante (`fmt.lyr:18-21` argumentiert
selbst so), also könnte die Sema ihn prüfen. Zweitens ist `LYR-VM0006` der Code für **index out
of range / Allokation abgelehnt** (`src/Lyric.Vm/VmDiagnostics.cs:46`, `docs/Bytecode.md:806`).
**Aber — Korrektur gegenüber der zweiten Fassung:** der Spec ist **nicht immer** eine Konstante.
Gemessen (`n18`): `formatFloat(3.14159, "F" + fromInt(2))` kompiliert und liefert `3.14`,
`formatFloat(3.14159, "Q" + fromInt(9))` panikt zur Laufzeit. Die öffentlichen
`std.fmt.formatX(value, spec: string)`-Funktionen nehmen **jeden Laufzeit-String** — der
Spec-Parser muss also in jedem Runtime bleiben, solange diese API existiert (S36).

**(e) Dynamische Breite ist still falsch — und die Form ist grammatisch gültig.** Gemessen (`p20`,
`m07`, bestätigt `a04`): `f"[{42:{w}}]"` → `[{w}]`, `f"[{42:(a)[b]}]"` → `[(a)[b]]`, einzige
Diagnose `LYR-SEM0071: 'w' is never used`. Nach (c3) ist das kein Sonderfall: `{w}` ist für .NET
ein Custom-Format aus Literalzeichen. `docs/Grammar.md:115-116` sagt ausdrücklich, `FormatSpec`
zähle verschachtelte Klammern mit — die Form ist eine **grammatische Form von Lyric 4**, nur zur
Laufzeit bedeutungslos. **Python** beantwortet genau diese Schreibweise (`{x:{w}.{p}}`); **Rust**
hat verschachtelte Löcher **nicht** und schreibt `{:width$}` / `{:1$}` / `{:.prec$}` / `{:.*}`
(behauptet).

**(f) Breite: Zahlen werden LINKS ausgerichtet, und das Minus bedeutet das Gegenteil von .NET.**
Gemessen (`p07`, `n02`, bestätigt `a03`): `f"[{42:8}]"` → `[42      ]`, `f"[{42:-8}]"` →
`[      42]`, `f"[{42:08}]"` → `[42      ]`, `f"[{42:D8}]"` → `[00000042]`. Die Regel steht samt
Begründung in `src/Lyric.Vm/NativeRegistry.cs:2785-2791` (gelesen) — **nur dort**, nicht im Guide,
nicht in der Spec; für Strings zusätzlich in `stdlib/std/fmt.lyr:36-37`.

**(g) `println` und das Loch decken NICHT dieselben Typen ab — und das Loch weitet, was `X`
sichtbar macht.** Gemessen (`p17`/`p17b`, `m08`, bestätigt `a06`): `println(a)` mit `a: int32`
und `println(u)` mit `u: uint` sind `LYR-SEM0028` (kein `Display`), `println(i)` mit `i: int`
geht, `f"{a}"`/`f"{u}"` gehen. `std.core` gibt `Display` genau an `int`, `float`, `bool`, `char`,
`string` (`stdlib/std/core.lyr:106-134`), das Loch weitet jeden Skalar auf i64/u64/f64
(`FunctionLowerer.cs:4915-4935`, gelesen).

**NEU, gemessen (`n03`) — die Weitung ist beobachtbar:**

| Quelle | Ausgabe | Rust gäbe (behauptet) | .NET direkt gäbe |
|---|---|---|---|
| `a: int32 = -7`, `{a:X}` | **`FFFFFFFFFFFFFFF9`** (16 Stellen) | `FFFFFFF9` (8) | `FFFFFFF9` (8) |
| `b: int = -7`, `{b:X}` (Kontrolle) | `FFFFFFFFFFFFFFF9` | `FFFFFFFFFFFFFFF9` | dito |
| `u: uint8 = 200`, `{u:X}` | `C8` | `C8` | `C8` |
| `s: int8 = -1`, `{s:X}` / `{s:b}` | `FFFFFFFFFFFFFFFF` / 64 Einsen | `FF` / `11111111` | dito |

Für schmale vorzeichenbehaftete Typen ist Lyric 4 also **heute schon von allen Vorbildern
verschieden** — der Kommentar `fmt.lyr:22-25` („as `{:x}` in Rust … write it") stimmt nur für
`int`. Nebenbei behauptet derselbe Kommentar das auch für Gos `%x`; Go schreibt für negative
Zahlen `-ff` (behauptet, Sprachwissen) — ein zweiter Doku-Fehler in derselben Zeile.

Der 4.5-Befund aus `design/fstring-display.md` („`println(d)` mit `d: Display` scheitert") ist
**geschlossen** (Kontrolle `p12b`). Der echte Doku-Fehler: nirgends steht, WELCHE Skalare `println`
annimmt.

**(h) `<` auf Strings ist quadratisch — `hash` genauso — und `length()` ist es in jeder Schleife.**
Der Bytecode kennt keine String-Ordnung (`docs/Bytecode.md:719-720`), also geht `a < b` durch
`extend string :: [Ordered<string>]` (`stdlib/std/core.lyr:267-297`), dessen `compare`
`charAt(this, i)` **und** `charAt(other, i)` in einer Schleife über `i` ruft (`core.lyr:278-279`).
`Hashable<string>.hash` (`core.lyr:300-316`) läuft FNV-1a über `charAt(this, i)` in derselben
Positionsschleife (`core.lyr:311`), und `stdlib/std/collections.lyr:547` deklariert
`pub class Map<K :: [Hashable<K>], V>` — **jeder `Map<string, V>`-Zugriff zahlt das.**

Gemessen (`cmp_*`/`ctrl_*`/`hash_*`, Minimum aus 3 Läufen, Grundlast ~120–140 ms abgezogen; die
Kritik hat die Größenordnung mit `c04` bestätigt, „eher schlechter"):

| n (Code-Punkte) | `compare`-Anteil | `hash`-Anteil |
|---|---|---|
| 10 000 | 422 ms | 176 ms |
| 20 000 | 1 467 ms | 789 ms |
| 40 000 | 5 235 ms | 2 661 ms |

**NEU, gemessen (`n13`) — `length()` allein reicht für die Quadratik:**

```
while (i < s.length()) { … i = i + 1; }     // n = 40 000: 9 698 ms
let n = s.length(); while (i < n) { … }      // Kontrolle:      14 ms
```

`CodepointCount` (`NativeRegistry.cs:1232-1237`, gelesen) läuft bei **jedem** Aufruf über den
ganzen String. Die Doku sagt das ehrlich (`string.lyr:114-121`, Guide 13:653: „`s.length()` is a
call BECAUSE it costs O(n)"), aber die Konsequenz — die naheliegendste Schleife der Welt ist
quadratisch **ohne ein einziges `charAt`** — steht nirgends, und S21 der zweiten Fassung listete nur
`charAt`-Schleifen. Jetzt S39.

**(i) Interpreter und JIT antworten auf `lt string` verschieden.** Der Compiler emittiert das nie
und der Verifier verbietet es — aber der **Reader** prüft nicht, was der Verifier prüft
(`STATUS.md:2183-2186`, gelesen; die Zeilenangabe der zweiten Fassung war um 19 Zeilen veraltet).
`src/Lyric.Vm/Interpreter.cs:1378-1385` antwortet für `Lt/Le/Gt/Ge` auf String `!equal`,
`src/Lyric.Vm/Jit/JitCompiler.cs:1085-1087` lehnt ab. *(gelesen, nicht gemessen.)*

**(j) Unicode-Casing trifft auf ASCII-Prädikate, und das Mapping ist SIMPLE.** Gemessen (`p15`,
`m09`, bestätigt `a07`): `"é".toUpper()` → `É`; `isUpper('É')` → `false`; `isAlpha('é')` →
`false`; `"ß"`, `"ﬁ"`, `"İ"` bleiben unverändert, alle Längen 1 → 1. `toUpper`/`toLower` sind
`ToUpperInvariant`/`ToLowerInvariant` (`NativeRegistry.cs:530-536`); die Prädikate sind ASCII
(`string.lyr:395`); `isWhitespace` ist seit 4.5 Unicode (`string.lyr:406-410`). Das Wort
„ordinal" im Kommentar (`NativeRegistry.cs:529-530`) ist für eine Tabellen-Abbildung falsch
(gelesen), und **welche Unicode-Version normativ ist, sagt weder Spec noch Guide** (gelesen: nicht
auffindbar).

**(k) Raw- und Mehrzeilen-Strings fehlen — und das Problem ist die Folgefehler-Lawine.** Gemessen
(`m01`, bestätigt `a18`/`a19`): `r"abc"` → `LYR-SEM0002 unknown identifier 'r'` + 2 Folgefehler;
`"""` über drei Zeilen → **18** Diagnosen, deren erste (`LYR-LEX0009: unterminated string
literal`) das Problem korrekt benennt. Der reale Defekt sind die 17 Folgefehler, die den
Literalinhalt als Quelltext parsen.

**(k2) NEU, gemessen — Backslash + Zeilenumbruch ist dieselbe Lawine.** Nachgemessen mit der
Kritikprobe `b10` (LF-Datei, `od` geprüft): `"abc\` + LF + `def"` ergibt **8** Diagnosen —
`LEX0009` an der öffnenden Stelle plus 7 Folgefehler (`PAR0016`, `SEM0002 'def'`, `SEM0022`, ein
zweites `LEX0009` am schließenden `"`, …). Quelle (gelesen, `Lexer.cs:604-607`):
`ConsumeEscapeSequence` kehrt bei `\n` **ohne Diagnose** zurück, der String-Scanner meldet dann
am Zeilenende „unterminated". Es gibt also weder eine Zeilenfortsetzung noch eine benennende
Meldung („`\` vor einem Zeilenumbruch ist kein Escape"). Meine eigene Erstprobe `n08` war ein
Tippfehler (`printf` hatte `\n` statt Backslash+LF geschrieben — `od` zeigt `\ n`) und ist
verworfen; die Zahl stammt aus dem Lauf von `b10` auf diesem Checkout. Jetzt S42.

**(l) Die Escape-Menge ist die Grammatik, und `\e` fehlt.** Gemessen (`p19`, bestätigt `a13`):
`\e`, `\a`, `\b`, `\f`, `\v`, `\$` sind `LYR-LEX0007`. Positiv: ein unbekanntes Escape wird
**abgelehnt** statt still durchgelassen, und `\u{…}` prüft Bereich und Surrogate
(`Lexer.cs:686-699`).

**(m) `{{` bedeutet in `"…"` und in `f"…"` Verschiedenes.** Gemessen (`p11`, bestätigt `a08`):
`println("{{ plain }}")` → `{{ plain }}`, `println(f"{{ fstring }}")` → `{ fstring }`. Ein
**einzelnes** `}` im f-Text geht still durch; Python, C# und Rust lehnen das ab.

**(m2) NEU, gemessen — ein leerer Spec ist still gültig.** `n09` (Kritik `b04`): `f"[{s:}] [{n:}]
[{f:}]"` → `[x] [5] [2.5]`, also wie ohne Spec. Quelle (gelesen): `Padded` und `Formatted` fangen
`spec.Length == 0` ab (`NativeRegistry.cs:2795, 2826`). Die Grammatik (`Interpolation = '{' Expr
[':' FormatSpec] '}'`) sagt nichts, weil `FormatSpec` nicht produziert ist. Jetzt S43.

**(n) Skalare passen in keinen `Display`-Wert.** Gemessen (`p22`, bestätigt `a09b`):
`let d: Display = 5;` → `LYR-SEM0001` (mit `import std.core { Display }`; ohne Import `SEM0011`).
Bewusst so (`core.lyr:94-100`). Folge: **eine Laufzeit-`format(template, args…)` mit heterogenen
Argumenten ist heute nicht baubar.**

**(o) Die Stringmethoden brauchen einen Import, und der Compiler warnt dann darüber.** Gemessen
(`p03`–`p05`, `n05`, bestätigt `a17`, `c01`): ohne Import `LYR-SEM0012`; mit
`import std.string as strings;` läuft es **und** meldet `LYR-SEM0072: import 'strings' is never
used`, obwohl der Import die Methoden sichtbar macht; `import std.string;` meldet `LYR-SEM0077`.

**(p) Die Breite eines Specs zählt UTF-16-Code-Einheiten.** Gemessen (`m03`, bestätigt `a10`):
`f"[{emoji:6}]"` mit `emoji = "\u{1F600}"` → `[😀    ]` — **vier** Leerzeichen bei `length()==1`;
`"世界"` und `"ab"` polstern gleich; `"e\u{301}"` polstert wie zwei Zeichen. Quelle:
`Padded` sitzt auf `PadRight`/`PadLeft` (`NativeRegistry.cs:2826-2833`).

**(p2) NEU, gemessen — die Breite kennt kein Budget.** `n14`: `f"{x:2000000000}"` endet mit dem
nackten .NET-Text **`Out of memory.`** — kein `LYR-`-Code, keine Panik, ein unbehandelter
`OutOfMemoryException`-Absturz. Kontrolle (gelesen): `string.repeat` prüft die Ergebnislänge und
panikt sauber mit `LYR-VM0006` (`NativeRegistry.cs:343-366`), `Padded` prüft nichts
(`NativeRegistry.cs:2826-2833`). Jetzt S38.

**(q) String- und Char-Literale SIND Muster.** Gemessen (`m02`, bestätigt `a11`):
`match (s) { "a" => 1, "b" => 2, _ => 3 }` und `match (c) { 'x' => 10, … }` kompilieren und
laufen. **NEU, gelesen per `lyric disasm` (`n19`):** ein String-`match` mit drei Armen wird zu
einer **Kette aus `eq string` + `condbr`** gesenkt, ein Arm nach dem anderen — die zweite Fassung
hatte das als „behauptet" markiert, jetzt ist es Disassembler-Ausgabe.

**(r) Die Längengrenze ist ein .NET-Artefakt und ihre Fehlermeldung zählt falsch.** Gemessen
(`m06`, bestätigt `a12`): `"\u{1F600}" * 2000000000` → `panic [LYR-VM0006]: string repetition of
2 code point(s) x 2000000000 exceeds the length a string can hold` — die Quelle ist **ein**
Code-Punkt. Grund (`NativeRegistry.cs:355-364`): geprüft wird `source.Length * count >
int.MaxValue`, UTF-16-Länge.

**(s) NEU, gemessen — `substring` hat zwei Bereichsregeln.** `n04a`, `n04d`, `n04e`, `n04c`
(Kritik `b12`):

| Aufruf auf `"hello"` (Länge 5) | Ergebnis |
|---|---|
| `substring(1, 10)` — `count` läuft über das Ende | **`ello`** — still geklemmt |
| `substring(4, 1)`, `substring(5, 0)` | `o`, `` |
| `substring(10, 2)` — `start` hinter dem Ende | **Panik** `LYR-VM0006: string index 10 is out of range` |
| `substring(-1, 2)` | **Panik** `string index -1 is out of range` |
| Kontrolle `"abc".charAt(3)` (`n04b`) | **Panik** `string index 3 is out of range` |

`start` ist strikt, `count` wird geklemmt — zwei Regeln in einer Funktion, und keine steht im
Guide (gelesen: `docs/guide/13-standard-library.md` nennt nur die O(n)-Kosten; `string.lyr:130-133`
sagt „`count` characters from `start`", nicht was bei Überlauf passiert). Jetzt S35.

**(t) NEU, gemessen — `char` ist geordnet, direkt im Bytecode.** `n06`: `'a' < 'b'` → `true`,
`'\u{1F600}' > 'z'` → `true`. `lyric disasm` zeigt **`lt char`** und **`gt char`** als
Instruktionen (gelesen aus der Disassembler-Ausgabe). Der IR-Verifier zählt `Char` zu den
Integer-Typen (`src/Lyric.Frontend/Ir/IrVerifier.cs:1755-1761`, gelesen: `IsInteger` enthält
`IrScalar.Char`); `docs/Bytecode.md:719` sagt nur „numeric type" und definiert die Menge nicht.
Die Ordnung ist der Skalarwert, ohne Kollation — und das steht nirgends im Guide (gelesen:
kein Treffer für Char-Vergleich in `docs/guide/`). Jetzt S40.

**(u) NEU, gemessen — eingebettetes NUL ist ein gewöhnliches Zeichen, bis es die Grenze
erreicht.** `n07`: `"a\0b"` hat `length()==3`, `utf8Encode().length==3`, `indexOf("b")==2`, und
`println` schreibt das NUL roh. `n21`: `std.io.file.text("nul\0name.txt")` liefert `null` — nicht
unterscheidbar von „Datei fehlt". Jetzt S41.

**(v) NEU, gemessen — der Hash ist FNV-1a-64 über Code-Punkte, und das ist beobachtbar.** `n05`:
`"".hash() == -3750763034362895579` (der FNV-Offset-Basis als i64), `"a".hash() ==
-5808556873153909620`. `n22`: `"\u{1F600}".hash() == -5686430818073629217` — nachgerechnet in
Python ist das FNV-1a-64 über den **Code-Punkt** `0x1F600`, nicht über die UTF-16-Einheiten
(`-1881279744732067624`) und nicht über UTF-8-Bytes (`-72331129952009592`). Der Algorithmus ist
damit Teil des beobachtbaren Verhaltens (Map-Iterationsreihenfolge), steht aber nur in einem
Quelltextkommentar (`core.lyr:301-303`). Jetzt S37.

**(w) NEU, gemessen — `comptime` faltet den heutigen Fehler mit.** `n10`: `comptime
f"{3.14159:.2}"` ergibt zur Compile-Zeit `32` — der VM-Sandbox-Pfad führt denselben Formatierer
aus. Jetzt S45.

**(x) NEU, gemessen — Spec-Fehler zur Compile-Zeit tragen `LYR-SEM0006`, und der Code ist geteilt.**
`n11` (Kritik `c02`): `f"{p:N2}"` mit `p: Display` → `LYR-SEM0006: a format specifier ':N2' does
not apply to 'P'`; `f"{[1, 2]}"` → `LYR-SEM0006: 'int[]' does not render in an f-string`. Gelesen
(`src/Lyric.Frontend/Sema/TypeChecker.cs:1776, 2982, 3008, 3016`): `SEM0006` ist **zusätzlich der
Code für einen ungültigen `as`-Cast** („cannot cast … a conversion comes from 'Into'"). Ein Code,
drei Bedeutungen. Jetzt S47.

**(y) NEU, gemessen — ein Spec auf einem generischen Loch ist heute ein Fehler.** `n23`:
`fn p<T :: [Display]>(x: T) { f"[{x:6}]" }` → `LYR-SEM0006 … does not apply to 'T'`. Die
Frage „wie richtet sich ein generisches Loch aus" (S27) stellt sich also erst mit S09-D.

**(z) NEU, gemessen — `lyrfmt` lässt den Inhalt eines Lochs unangetastet.** `n15_fmt` über
`lyrfmt --stdin`: `f"{ x : 8 }|{y:F2}|{x}"` kommt **zeichengleich** zurück. Gelesen
(`src/Lyric.Lsp/Analysis/SemanticTokensProvider.cs:15`): Literale bleiben beim TextMate-Grammar,
der Language Server tokenisiert **nicht** in ein Literal hinein. Jetzt S46.

**Was gut ist und bleiben sollte** (gemessen): `as char` prüft den Bereich und panikt sauber
(`p14`, `a22`: `LYR-VM0012`), `"a" + 1` ist ein Fehler (`p18`), `"ab" * n` prüft die
Ergebnislänge (`NativeRegistry.cs:343-366`, vorbildlich kommentiert), `comptime f"…"` funktioniert
(`p18b`, `a20`), Optional/Array/Tupel im Loch sind eine Sema-Diagnose mit brauchbarem Text
(`p12`, `n11`), der String-Konstantenpool ist dedupliziert und deterministisch
(`BytecodeWriter.cs:876-880`), `std.fmt` trennt `formatInt(v,"x")` von `formatHex(v)` mit
Begründung (`fmt.lyr:22-25`), und der Formatter fasst Literale nicht an (`n15_fmt`).

---

## 2. Sprachvergleich

| Sprache | String-Einheit / `char` | Index & Slice | Interpolation | Formatsprache | Prüfzeitpunkt | Raw / mehrzeilig |
|---|---|---|---|---|---|---|
| **Rust** | UTF-8-Bytes; `char` = 32-bit Scalar Value | kein `s[i]`; `&s[a..b]` ist ein Borrow, O(1), panikt auf Nicht-Grenze; `.len()` = Bytes, O(1) | `format!`/`println!` als **Makro**, Literal zwingend — **es gibt keine Laufzeit-`format`** | fest: `{[fill]align[sign][#][0][width][.prec][type]}`; `.N` = **Nachkommastellen**, auf Integern ignoriert; `X`/`b` auf Signed = **Zweierkomplement in der Breite des Typs**; `{:?}` für `Debug`; dynamisch nur `{:width$}`/`{:1$}`/`{:.prec$}`/`{:.*}` — keine verschachtelten Löcher | **Compile-Zeit** (Makro) | `r"…"`, `r#"…"#`; mehrzeilig = echter Umbruch, `\`+Umbruch frisst Einzug |
| **Python** | Code-Punkte (PEP 393) | `s[i]` O(1); `s[a:b]` O(b−a), kopiert | f-String, plus `str.format`/`format()` zur Laufzeit | fest: `[[fill]align][sign][#][0][width][,_][.prec][type]`; `.N` = **signifikante Stellen** (gemessen: `.2` → `3.1`), auf Integern `ValueError`; `X`/`b` auf negativ = **`-FF`/`-11111111`** (gemessen); `,` Tausender; verschachtelt `{x:{w}.{p}}`; `{0}`, `{name}` | Laufzeit (`ValueError`), f-String-Syntax zur Compile-Zeit | `r"…"`, `"""…"""`; Einzug per `textwrap.dedent` zur Laufzeit |
| **Swift** | Grapheme-Cluster; `Character` = Cluster, `Unicode.Scalar` = Code-Punkt | kein Int-Index; `count` O(n) | `\(expr)` + `StringInterpolationProtocol` | keine im Kern — `String(format:)` (printf) oder `FormatStyle` | printf-Teil ungeprüft | `#"…"#`; `"""…"""` mit Einzugregel zur Compile-Zeit |
| **C#** | UTF-16; `char` = Code-Einheit, `Rune` = Scalar | `s[i]` O(1); `Substring(start, length)` | `$"…"`, `{expr,align:format}` | `IFormattable`, kulturabhängig per Default; `{0}` | Laufzeit (`FormatException`) | `@"…"`; `"""…"""` (C# 11), `$$"""…"""` |
| **Kotlin** | UTF-16 | `s[i]` O(1) | `"$name"`, `"${expr}"`, alles rendert | keine im Template — `String.format` separat | Laufzeit | `"""…"""` roh nur gegenüber Escapes, interpoliert weiter; `trimIndent()` zur Laufzeit |
| **Go** | Bytes; `rune` = int32 | `s[i]` = Byte O(1); `s[a:b]` O(1) | keine | printf-Verben; `%x` auf negativ = `-ff` (behauptet) | `go vet`, seit 1.10 automatisch bei `go test` | Backticks roh und mehrzeilig; **`\r` wird im Raw-Literal verworfen** (Go-Spec, behauptet) |
| **Zig** | `[]const u8`; `u8`/`u21` | Byte-Index; Slices O(1) | keine | `{[arg][spec]:[fill][align][width].[prec]}` | **Compile-Zeit** | `\\`-Zeilenpräfix |
| **Scala** | UTF-16 | `s(i)` O(1) | `s"…"`, `f"…"`, `raw"…"` + eigene Interpolatoren | `f"$x%2.2f"` printf | Compile-Zeit (Makro) für `f` | `"""…"""` + `stripMargin` zur Laufzeit |
| **Java** | UTF-16 | `charAt`; `substring(begin, end)` | **keine** — die String Templates (JEP 430/459/465) wurden in JDK 23 wieder entfernt (behauptet, Stand 2025) | `String.format`, `%1$s` positionell | Laufzeit | `"""…"""` mit Einzugregel (Java 15) |

**Korrekturen gegenüber der zweiten Fassung** (alle behauptet, Sprachwissen, sofern nicht als
gemessen markiert): Rust hat **keine** Laufzeit-`format` — `format!` ist ein Makro; Java hat heute
**keine** Interpolation, auch keine Preview; Gos Raw-Literale **normalisieren** `\r\n` auf `\n`
(die Spec: „Carriage return characters inside raw string literals are discarded"); Rusts
`{:X}`/`{:b}` auf Signed rendern das **Zweierkomplement** (die zweite Fassung schrieb `-FF`, das
ist Pythons Antwort — gemessen); Pythons `.N` sind signifikante Stellen (gemessen).

### Die Gegenentscheidungen

**Go entscheidet gegen Interpolation — vollständig.** Was Go dadurch gewinnt: das Format ist ein
**Wert**. Lyric hat heute die andere Hälfte und nur die andere: f-Strings funktionieren, eine
Laufzeit-`format` existiert nicht und ist wegen (n) nicht baubar. Preis bei Go: jede Meldung ist
ein Funktionsaufruf; die Verbprüfung hängt an `vet`, das seit Go 1.10 bei jedem `go test` läuft.

**Rust entscheidet gegen eine Laufzeit-Formatsprache — vollständig.** Das Template ist immer ein
Literal, der Spec-Parser lebt nur im Compiler (Makro). Lyric hat heute das Gegenteil: der
Spec-Parser lebt **nur im Runtime** (`Formatted`/`Padded`), und `std.fmt.formatX(value, spec:
string)` machen ihn zur öffentlichen Laufzeit-API (gemessen `n18`). Das ist die Weiche für S06/S36.

**Swift entscheidet gegen Code-Punkte.** `==` vergleicht kanonisch äquivalent; der String wird
nicht normalisiert, identische Bytes sind ein memcmp. Lyric vergleicht ordinal über Code-Punkte
(`core.lyr:268-270`, „no locale and no normalization"); gemessen (`n12`, Kritik `b07`):
`"\u{E9}" == "e\u{301}"` ist `false`, die Längen sind 1 und 2 — und das steht nirgends im Guide
(gelesen: kein Treffer für „normaliz"/„canonical" in `docs/guide/` außer 13:577 zu UTF-8).

**Go und Zig entscheiden gegen O(n)-Positionen.** Lyric zahlt O(n) pro Position, um `s[i]`
ehrlich zu halten — und zahlt es in `compare`, `hash` **und in jeder `length()`-Schleife**
(`n13`: 9,7 s) dann doch quadratisch.

**Rust entscheidet gegen verschachtelte Löcher.** `{:width$}` hält den Spec **konstant** — das ist
der Grund, warum Rusts Compile-Zeit-Prüfung und ein konstanter Spec-String zusammenpassen. Pythons
`{x:{w}}` macht den Spec nicht-konstant. Die zweite Fassung hat diese Abwägung nicht gemacht
(S08).

**C# entscheidet für eine Länge statt eines Endes.** `Substring(startIndex, length)` — die
Sprache, in der Lyrics Laufzeit geschrieben ist; die Mehrheit nimmt ein Ende.

**Roslyn entscheidet gegen die Linksfaltung.** `$"{a}{b}{c}"` mit String-Löchern ohne Spec wird
zu **einem** `string.Concat(a, b, c)` (bis 4 Argumente, danach `string.Concat(string[])`), sonst
zu `string.Format` bzw. seit C# 10 zum `DefaultInterpolatedStringHandler` — **nie** zu einer Kette
binärer Concats (behauptet). Der Kommentar in `FunctionLowerer.cs:4820-4824` („Roslyn does the same
for `$"…"` without a format spec") ist damit gelesen **falsch**; die zweite Fassung hat ihn
weitergetragen (S22).

### Was zu Lyrics Charakter passt

- **Zig, Scala und Rust sind die Vorbilder für den Prüfzeitpunkt**: ein Format-Literal wird zur
  Compile-Zeit geparst. Passt zu „spec-first" — **aber nur, wenn der Spec-Parser nicht zugleich
  öffentliche Laufzeit-API bleibt** (S36).
- **Rust ist das Vorbild für die Formatsprache selbst**, und zwar **als eine Sprache, nicht als
  „Rust/Python"**: `.N` = Nachkommastellen, `X`/`b` = Bits in Typbreite, kein `,`, `{:?}` als
  zweite Rolle. Wo Python abweicht, ist es zu benennen, nicht zu vermischen (S32, S34).
- **Swifts `"""`-Einzugregel zur Lexzeit** ist Kotlins `trimIndent()` überlegen und passt zu Rule 2.
- **Kotlin ist der Beleg, dass „roh" und „interpoliert" orthogonale Schalter sind.**
- **Bei dynamischer Breite gibt es zwei Vorbilder, und sie sind nicht gleichwertig**: Pythons
  verschachtelter Spec kostet die Konstanz des Specs; Rusts `{:w$}` behält sie. Für Lyric spricht
  die Python-Form nur, wenn ihre Struktur zur Compile-Zeit fest bleibt (S08, präzisiert).
- **Nicht** zu Lyric passt C#/Kotlins „alles rendert" — begründet abgelehnt in
  `design/fstring-display.md`, bleibt so.


---

## 3. Designfragen

Jede Frage: Ist-Stand mit Beleg, Optionen (Name – Beschreibung – Vorbild – Preis), Empfehlung,
Bruchgrad (nein / minor / major), Abhängigkeiten, Belegtyp.

### S01 — Was ist die Element-Einheit eines Strings?

**Heute:** Code-Punkte. `length()`, `charAt`, `substring`, `indexOf` zählen Code-Punkte
(`stdlib/std/string.lyr:114-135`, `NativeRegistry.cs:444-452, 1232-1252`, gelesen); intern hält
die VM einen .NET-UTF-16-String und rechnet pro Aufruf um. Die Doku sagt „immutable UTF-8"
(Guide 02:31), der Bytecode speichert UTF-8 (`docs/Bytecode.md:79`), die Laufzeit hält UTF-16.
**Vier weitere Stellen zählen anders oder verraten die Repräsentation:** die Breite eines Specs
(UTF-16, `m03`), die Längengrenze (UTF-16, `m06`), der Hash (Code-Punkte, `n22` — hier stimmt es),
und die Char-Literal-Prüfung (UTF-16, `p01`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Code-Punkte behalten** | Einheit = Unicode Scalar Value, Repräsentation Sache des Runtime | am nächsten Python | O(n)-Positionen bleiben (S39); „UTF-8" im Guide ist eine Aussage über die Kodierung an Grenzen, nicht über den Speicher |
| B **Bytes, Code-Punkte per Iterator** | `length()` = Bytes O(1), `s.chars()` | Rust, Go | Bruch in jedem Programm mit `length()`; `charAt` verschwindet; O(1)-Positionen |
| C **Grapheme-Cluster** | zählt, was ein Mensch zählt | Swift | Unicode-Tabellen im Runtime; versionsabhängig → Bytecode nicht plattformneutral |

**Empfehlung: A**, mit drei Präzisierungen in der Spec: (1) die Einheit ist der Unicode Scalar
Value; `utf8Encode`/`utf8Decode` sind die einzige Stelle, an der Bytes sichtbar werden; (2)
**jede** Stelle, die heute UTF-16 zählt, wird auf diese Einheit gezogen — Breite (S20),
Längengrenze (S23), Char-Literal (S02); (3) die Komplexität von `length()` wird zugesichert (S39),
sonst ist „Code-Punkte" eine Einheit ohne Kostenmodell. Grapheme gehören in `std.unicode` (S16).

**Bruch:** nein. **Abhängig von:** S02, S20, S23, S39. **Belegtyp:** gemessen.

---

### S02 — Was ist `char`, und warum lässt der Lexer ein astrales Literal nicht durch?

**Heute:** `char` ist ein Unicode Scalar Value, geprüft beim `as` (gemessen `p14`) und beim
`\u{…}`-Escape (`Lexer.cs:686-699`). `'😀'` direkt ist `LYR-LEX0008 … got 2` (`p01`, `a01`), weil
`ScanChar` UTF-16-Einheiten zählt (`Lexer.cs:590-596`). An der FFI-Grenze wird ein `char` über
U+FFFF abgelehnt statt gesplittet (`DotnetBinding.cs:165-172`) — dort ist die Regel richtig.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Fehler beheben, `char` bleibt Scalar Value** | `ScanChar` zählt Code-Punkte | Rust | ~5 Zeilen; muss auch als Muster gelten (S29) |
| B **`char` streichen** | ein Zeichen ist ein `string` der Länge 1 | Python | `std.string` ist auf `char`-als-Zahl gebaut (`string.lyr:375-378`) |
| C **`char` = Code-Einheit** | 16 bit | C#, Kotlin | Widerspricht der Doku und macht das Problem zum Dauerzustand |

**Empfehlung: A**, vor v5 — Bugfix einer Aussage, die die Doku schon macht.

**Bruch:** nein (additiv). **Abhängig von:** S29, S40 (Ordnung auf `char`). **Belegtyp:** gemessen.

---

### S03 — Gibt es `s[i]` und `s[a..b]`, und nimmt `substring` ein Ende oder eine Länge?

**Heute:** Indizierung und Slices gibt es nicht (`string.lyr:45-46` begründet es mit der
Quadratik); Slices stehen in `STATUS.md:2173-2174` unter §Still open (gelesen).
`string.lyr:131` deklariert `substring(start: int, count: int)` — eine **Länge**, und die
Bereichsregel dieser Funktion ist gemessen zweigeteilt (Befund s → S35).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **nichts, `substring(start, count)` bleibt** | Status quo | C# | Mehrheit nimmt ein Ende; Denkschritt für Umsteiger |
| B **`s[a..b]` als kopierender Substring** | Zucker | — | sagt „Index", meint O(n) |
| C **`s[a..b]` als View** | echte Slices | Rust (Borrow), Go (Header) | braucht die Slice-Entscheidung; über Code-Punkt-Positionen nur mit Byte-Offsets billig → S01 |
| D **Byte-Indizierung** | `s[i]` ist `uint8` | Go | widerspricht der Code-Punkt-Einheit |
| E **`slice(start, end)` neu, `substring` mit `@Deprecated`** | beide eine Weile nebeneinander | Rust `str::get`, JS `slice`/`substr` | zwei Namen für eine Uhr; nichts wechselt still die Bedeutung |

**Empfehlung: A für Indizierung/Slices (C als eigene Runde), E für die Signatur.** Eine stille
Bedeutungsänderung derselben Signatur ist nie ratterbar (`substring(2, 5)` bliebe gültig und
meinte etwas anderes). E muss die Bereichsregel aus S35 **mitbringen** — ein neuer Name ist die
eine Gelegenheit, sie ohne Bruch festzulegen.

**Bruch:** A nein; E minor (Uhr ab 4.7). **Abhängig von:** S01, S35, Slices (Arrays/Collections).
**Belegtyp:** gelesen.

---

### S04 — Wie vergleicht, ordnet und hasht man Strings?

**Heute:** `==` ordinal in der VM (`Interpreter.cs:1378-1385`); `<` über `Ordered<string>` in
Lyric, lexikografisch über Code-Punkte ohne Normalisierung (`core.lyr:267-297`), quadratisch;
`hash` über `Hashable<string>` (`core.lyr:300-316`), FNV-1a-64 über Code-Punkte (gemessen `n22`),
quadratisch; `Map<K :: [Hashable<K>], V>` (`collections.lyr:547`) zahlt beides. Gemessen: 5,2 s
`compare`, 2,7 s `hash` bei n = 40 000. Der Bytecode hat keine String-Ordnung
(`Bytecode.md:719-720`); Interpreter und JIT antworten auf eine trotzdem vorhandene verschieden
(Befund i). `"\u{E9}" == "e\u{301}"` ist `false` (gemessen `n12`), und der Guide sagt es nicht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Native `compare` UND native `hash`, Interfaces bleiben** | `std.core` ruft `rawCompare`/`rawHash` | Rust (`Ord`/`Hash for str` nativ) | ~20 Zeilen; zwei Natives mehr in Section 4 (`Bytecode.md:133`); **verlagert die Portabilitätsgarantie von `core.lyr:301-303` ins Runtime — ohne Spec-Zeile für den Algorithmus verliert Lyricpp die Map-Kompatibilität (S37)** |
| B **Opcode `lt/le/gt/ge` auf `string`** | Ordnung in die VM | die meisten VMs | Formatänderung (5.0 darf das); beendet die Interpreter/JIT-Divergenz; löst `hash` nicht |
| C **Ordnung streichen** | nur `==` | — | `sort` auf `string[]` zu häufig |
| D **Kanonische Äquivalenz** | `==` kanonisch, Speicher unverändert | Swift | Tabellen im Runtime; **und `hash` müsste NFC-normalisieren, sonst bricht die Equatable/Hashable-Konsistenz** (S48) |

**Empfehlung: A jetzt (4.x, `compare` UND `hash`) — mit der Spec-Zeile aus S37 im selben
Commit —, B in 5.0 mitnehmen.** D nein, und der Guide muss den Preis benennen: gleich aussehend
ist nicht gleich (S48).

**Bruch:** A nein, B nein für Programme (Formatversion +1). **Abhängig von:** S21, S37, S39,
S48. Berührt Bytecode/VM. **Belegtyp:** gemessen.

---

### S05 — Welche Formatsprache?

**Heute:** zwei Teilsprachen (Befund c): Zahlen → `IsWidth`, sonst .NET unverändert
(`fmt.lyr:6-8`, `NativeRegistry.cs:2795-2797`); `char`/`bool`/`string` → nur Breite
(`NativeRegistry.cs:2826-2833`). `FormatSpec` ist in der Grammatik undefiniert
(`Grammar.md:105`). `lyric-v5-features.md:59` (Zeile 22 der Tabelle) und `PLAN.md:337` haben die
Ablösung als Uhr auf 4.7.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **weiter .NET** | Status quo | C# | zweites Runtime kann die Spec nicht erfüllen; Kultur sickert ein; `.2` → `32`, `>8` → `>8`, `#x` → `255x`, `,` → leer — alles still |
| B **Rust-Formatsprache, in der Spec** | `{[fill]align[sign][#][0][width][.prec][type]}`; `.N` = Nachkommastellen; `X`/`b` = Bits in Typbreite; Typen `b o x X e E ?`; kein `,`, kein `%`, kein Kultur-Begriff | **Rust** (nicht „Rust/Python") | Bruch für jeden .NET-Spec; ~200 Zeilen Parser, **zweimal** (Compiler und Runtime, S36); spezifiziert, portierbar |
| B2 **Python-Formatsprache** | wie B, aber `.N` = signifikante Stellen, `X` = `-FF`, `,` Tausender | Python | Dieselben Kosten; `-FF` widerspricht `fmt.lyr:22-25` und dem heutigen Verhalten auf `int` |
| C **printf-Verben** | `%5.2f` | Go, C | Verb und Typ redundant in einer statisch typisierten Sprache |
| D **Nur Layout** | Breite/Ausrichtung im Loch, Zahlformatierung über `std.fmt` | — | Kleinste Sprache; `f"{x:.2}"` ist der Grund für f-Strings |

**Empfehlung: B — Rust, und zwar als EIN Vorbild.** Die zweite Fassung schrieb „Rust/Python" und
rechnete die Warnklassen auf einer Rust-Behauptung, die Pythons Antwort war. Gemessen und
behauptet auseinandergezogen:

| Spec | Lyric 4 heute | Rust (behauptet) | Python (gemessen) | Klasse bei Vorbild Rust |
|---|---|---|---|---|
| `{255:x}` | `ff` | `ff` | `ff` | gleich |
| `{-255:X}` mit `int` | `FFFFFFFFFFFFFF01` | `FFFFFFFFFFFFFF01` (i64) | `-FF` | **gleich** (bei Python: still verschieden) |
| `{a:X}` mit `a: int32 = -7` | `FFFFFFFFFFFFFFF9` | `FFFFFFF9` | `-7` | **still verschieden — bei jedem Vorbild** (S34) |
| `{3.14159:e}` | `3.141590e+000` | `3.14159e0` | `3.141590e+00` | still verschieden |
| `{3.14159:.2}` | `32` | `3.14` | `3.1` | heute falsch, künftig richtig — Klasse 4 |
| `{5:.2}` | `52` | `5` (ignoriert) | `ValueError` | heute falsch; 5.0: Fehler (S32) |
| `{n:>8}`, `{n:<8}`, `{n:^8}`, `{n:*>8}`, `{n:+}`, `{n:#x}` | Spec-Text bzw. `255x` | richtig | richtig | **heute falsch, künftig richtig — Klasse 4** |
| `{n:,}` | leer | ungültig | `1,234,567` | heute falsch; 5.0: Fehler |
| `{x:N2}`, `{x:C}`, `{x:P1}`, `{x:D8}` | richtig (.NET) | ungültig | ungültig | laut — Klasse 1 |
| `{x:8}` auf Zahl | links | rechts | rechts | Klasse 2 |

**Warnstufe in 4.7, jetzt VIER Klassen:**

1. **Ungültig in 5.0** (`N2`, `C`, `P1`, `D8`, `#,##0.00`, `'abc'`, `,`): Warnung mit
   Ersatzvorschlag; trifft kein richtiges Programm, weil die Form verschwindet.
2. **Gültig in beiden, andere Bedeutung, laut erkennbar** (reine Ziffernbreite auf numerischem
   Loch → Ausrichtung dreht, S07): Warnung mit künftigem Ergebnis.
3. **Gültig in beiden, Bedeutung ändert sich STILL** — bei Vorbild Rust: `X`/`x`/`b` **nur auf
   schmalen vorzeichenbehafteten Löchern** (`int8`/`int16`/`int32`, S34) und `e`/`E`. Für `int`
   ist die `X`-Zeile bei Rust **gleich**; die Warnung pro `int`-Loch der zweiten Fassung wäre bei
   Rust Lärm und ist gestrichen.
4. **NEU — heute still falsch, in 5.0 gültig und richtig** (`.N` auf Float, `<`/`>`/`^`, Füllzeichen,
   `+`, `#`): Warnung „gibt heute `>8` aus, ab 5.0 den ausgerichteten Wert". Das ist die wertvollste
   Klasse, weil sie einen **heutigen** Fehler aufdeckt — und die gefährlichste für Golden-Tests,
   die den heutigen Müll eingefroren haben (S44).

Setzt voraus, dass der Spec zur Compile-Zeit geparst wird (S06) und dass `.N` (S32), die
Nicht-Zahl-Löcher (S33) und die Typbreite (S34) entschieden sind.

**Bruch:** **major**. **Abhängig von:** S06, S07, S08, S09, S20, S26, S27, S32, S33, S34, S36,
S44. **Belegtyp:** gemessen (Lyric, Python); behauptet (Rust).

---

### S06 — Wann wird ein Spezifizierer geprüft?

**Heute:** zur Laufzeit, als Panik unter dem Index-Code (`p06`, `p16`, `m04`, `n18`). Im
f-String ist der Spec ein Literal; **in `std.fmt.formatX(value, spec: string)` ist er ein
beliebiger Laufzeit-String** (gemessen `n18`: `"F" + fromInt(2)` → `3.14`, `"Q" + fromInt(9)` →
Panik). Was die Sema heute schon prüft: Spec auf `Display`-Loch, nicht renderbarer Typ — beides
`LYR-SEM0006` (`n11`, `TypeChecker.cs:3008-3016`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Sema prüft f-String-Specs, Fehler zur Compile-Zeit** | Loch + Typ + Spec zusammen | Zig, Scala `f`, Rust | braucht eine eigene Formatsprache (S05); generische Löcher brauchen eine Regel (S27); **deckt `std.fmt.formatX` nicht** |
| B **weiter Laufzeit** | Status quo | Python, C# | Tippfehler in seltenem Zweig kippt das Programm in Produktion |
| C **Laufzeit, eigener Diagnose-Code** | statt VM0006 | — | behebt das Etikett, nicht das Problem; **bleibt nötig, solange S36 die Laufzeit-API behält** |

**Empfehlung: A für f-Strings, C sofort in 4.x — und die Aussage der zweiten Fassung, nach A sei
„der Laufzeitpfad aus Quelltext unerreichbar", ist ZURÜCKGENOMMEN.** Sie war falsch: jedes
Programm kann `formatFloat(x, userInput)` schreiben (`fmt.lyr:26-37`, gemessen `n18`). Daraus
folgt: (1) der Vorschlag, den Spec in die Ladezeit-Validierung (`Bytecode.md` §6) zu ziehen,
**trägt nicht** — der Verifier sieht am Native-Call nur ein String-Argument und kann Konstante von
Laufzeitwert nicht trennen; (2) es gibt in 5.0 **zwei Implementierungen derselben Formatsprache**
(Compiler-Sema, Runtime-Native), und die Spec muss sie so festlegen, dass beide gleich antworten
— dieselbe Zwei-Implementierungen-Frage wie bei Lyricpp, nur innerhalb eines Repos. Ob die
Laufzeit-API bleibt, ist S36; welchen Code die Sema benutzt, ist S47.

**Bruch:** minor (Programme mit ungültigem Spec in nie genommenem Zweig kompilieren nicht mehr;
Warnung in 4.7, Fehler in 5.0). **Abhängig von:** S05, S27, S36, S45, S47. **Belegtyp:** gemessen.

---

### S07 — Wie schreibt man Breite und Ausrichtung, und wie richten Zahlen sich aus?

**Heute:** reine Zahl = Breite, führendes Minus dreht, Vorgabe **links auch für Zahlen**
(`p07`, `n02`); `08` = Breite, `D8` = Nullauffüllung; Regel nur in `NativeRegistry.cs:2785-2791`.
Breite zählt UTF-16 (`m03`). **Die künftige Syntax gibt heute still ihren Text aus** (`n02`,
Befund c3).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **explizite Ausrichtungszeichen** | `<` `^` `>`, optional Füllzeichen: `{x:*>8}` | Rust, Python | drei Zeichen mehr; `0` als Füllzeichen fällt als Sonderfall weg |
| B **Komma-Feld** | `{expr,8:N2}` | C# | zwei Felder; Komma kollidiert mit Tupeln im Loch |
| C **heutige Konvention, Zahlen rechts** | nur die Vorgabe drehen | — | Minus bleibt das Gegenteil von .NET/Python/C |

**Empfehlung: A** mit S05, Vorgabe wie Rust: **nicht-numerisch links, numerisch rechts**, Einheit
aus S20. **Preis, den die zweite Fassung unterschätzt hat:** unter S27-Antwort 2 (Display-Löcher
immer links) richtet sich derselbe `int` in derselben Tabelle anders aus, wenn er durch eine
generische Hilfsfunktion läuft. Rust vermeidet das, weil die Ausrichtung am **Trait** hängt
(`Display for i32` richtet rechts aus, auch generisch). Heute ist ein Spec auf einem generischen
Loch ein Fehler (`n23`), also stellt sich die Frage erst mit S09-D — und dann braucht sie
mindestens die Warnung „generisches Loch ohne explizite Ausrichtung" (S27, präzisiert).

**Bruch:** major (Klasse 2). **Abhängig von:** S05, S20, S27, S33, S44. **Belegtyp:** gemessen.

---

### S08 — Dynamische Breite und Präzision

**Heute:** still falsch (`p20`, `m07`, `a04`); grammatisch gültig (`Grammar.md:115-116`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **verschachtelte Löcher im Spec** | `{x:{w}}`, `{x:.{p}}`, beide `int` | Python | **der Spec ist nicht mehr konstant** — S06 (Compile-Zeit-Prüfung), S26 (Template als Wert) und S31 (Spec im Pool) setzen einen konstanten Spec voraus; die Grammatikregel bekommt endlich eine Semantik |
| A2 **Rusts Argumentbezug** | `{x:w$}` / `{x:.p$}` | Rust | Spec bleibt flach und konstant; zweite Schreibweise für „ein Name" neben dem Loch; nur Namen, keine Ausdrücke |
| B **ausdrücklich ablehnen** | Sema-Fehler mit Verweis auf `std.fmt.padLeft` | — | Grammatik muss die „tracking nested braces"-Regel einengen |
| C **weiter still** | Status quo | — | nicht vertretbar |

**Empfehlung: A, aber mit der Einschränkung, die die zweite Fassung nicht gemacht hat:** ein
verschachteltes Loch ist **nur** an der Breiten- und der Präzisionsstelle erlaubt und trägt einen
`int`-Ausdruck. Damit bleibt die **Struktur** des Specs zur Compile-Zeit fest (die Sema parst
`{x:{·}.{·}f}` mit Platzhaltern und prüft alles andere wie einen konstanten Spec), nur zwei Werte
kommen zur Laufzeit als Argumente — genau das, was Rusts `{:w$}` erreicht, mit Lyrics eigener
Loch-Regel statt einer zweiten Schreibweise. Für S26 (Template als Wert) gilt die Form **nicht**
(dort gibt es nur `{0}`-Indizes), für S31 wandern Breite/Präzision aus dem Pool in Argumente.
Mindestens B, sofort in 4.x — C ist ein stiller Fehler.

**Bruch:** nein. **Abhängig von:** S05, S06, S26, S31. **Belegtyp:** gemessen.

---

### S09 — Wie verstehen eigene Typen einen Spezifizierer?

**Heute:** gar nicht; Spec auf `Display` ist `LYR-SEM0006` (`p12`, `n11`, `n23`), bewusst
(`design/fstring-display.md`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`Display` allein, Spec bleibt Fehler** | Status quo | — | `f"{duration:>10}"` geht nie |
| B **`Format`-Interface neben `Display`** | `fn format(spec: FormatSpec): string` | Rust, Go | zweiter Mechanismus → Rule 2, ADR |
| C **`Display` erweitern** | `show(spec: string)` | — | bricht jede `show()`-Implementierung |
| D **Ausrichtung generisch, Rest typspezifisch** | Compiler wendet `[fill]align[width]` nach `show()` an | — | deckt Tabellen ohne neues Interface; `{duration:.2}` geht nicht |

**Empfehlung: D für 5.0, B danach.** D muss in der Spec dreierlei sagen: (1) die Grammatik teilt
`FormatSpec = Layout Presentation?` in **zwei Nichtterminale**, `Layout = [[fill] align] [width]`
für jeden Typ, `Presentation = [sign][#][0][.prec][type]` nur für Skalare — **und nach S33 ist
das ein DRITTER Fall: Zahl (Layout + Presentation), Nicht-Zahl-Skalar (Layout + ein Teil von
Presentation?), Display (nur Layout)**; (2) die Diagnose nennt die Regel; (3) der Preis — eine
Formatsprache, deren Gültigkeit vom Lochtyp abhängt — ist benannt.

**Bruch:** D nein. **Abhängig von:** S05, S07, S20, S27, S33, Konformanz-Synthese
(`PLAN.md:225-226`). **Belegtyp:** gemessen.

---

### S10 — Gibt es einen `Debug`-Zwilling (`{x:?}`)?

**Heute:** nein (`design/fstring-display.md`); `lyric-v5-features.md:59, 85` nennen `{x:?}` und
`Debug` trotzdem. Nirgends entschieden.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **kein `Debug`** | Synthese liefert ein `show()` | — | „für Menschen" und „für Logs" nicht unterscheidbar |
| B **`Debug` als zweites Interface** | `{x:?}` | Rust, Python `__repr__` | Rule 2 wie S09 |
| C **ein Interface, zwei Methoden** | `Display { show(); debug() = show() }` | Swift (Default-Kette) | ein Mechanismus; Synthese füllt `debug()` |

**Empfehlung: C.** Die Escape-Regel für `debug()` auf einem String ist S30.

**Bruch:** nein. **Abhängig von:** S30, Konformanz-Synthese. **Belegtyp:** gelesen.

---

### S11 — Gibt es Formatierung zur Laufzeit?

**Heute:** keine `format(template, args)`; `std.fmt` bietet `formatX(value, spec)`, `formatRadix`,
`formatHex`, `formatBinary`, Polsterfunktionen (`fmt.lyr:26-93`). Kein Skalar passt in `Display`
(`p22`). Die f-String-Absenkung ist eine Linksfaltung aus `concat` (S22).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **bleibt so** | f-Strings sind die einzige Form | Rust (hat ebenfalls keine Laufzeit-`format`) | keine i18n, keine Log-Templates aus Konfiguration |
| B **`format(tmpl: string, args: string[])`** | Aufrufer rendert selbst | — | ohne Positionsargumente kann eine Übersetzung nicht umordnen |
| B2 **`format` MIT Positionsargumenten** | `format("{1} {0}", ["Welt", "Hallo"])` | .NET, Python, Go, Java | ein Index im Loch; derselbe Parser wie S26 |
| C **Boxing für Skalare** | `Boxed`-Wrapper, `Display[]` wird schreibbar | Java, C# | eine Allokation pro Argument |
| D **Variadische Generics** | `format<T…>` | Zig, Rust (Makro) | großes Sprachfeature |

**Empfehlung: B2, C prüfen.** **Korrektur:** die zweite Fassung zählte Rust unter „alle fünf
Vergleichssprachen mit einer Laufzeit-`format`". Rust hat keine — `format!` ist ein Makro, das
Template ist ein Literal, `{0}`/`{name}` sind Compile-Zeit-Bezüge. **Es sind vier** (.NET, Python,
Go, Java), und Rust ist ein Beleg für Option A. Der Bedarf (Übersetzungstabellen) bleibt.

**Bruch:** nein. **Abhängig von:** S26, Boxing (Typen/Interfaces), S36 (die Laufzeit-`format`
teilt den Parser mit `formatX`). **Belegtyp:** gemessen.


---

### S12 — Raw-Strings

**Heute:** `r"abc"` ist `LYR-SEM0002 unknown identifier 'r'` + 2 Folgefehler (`p13`, `a18`).
Prototyp 19 (`docs/Befunde_und_Verbesserungen/prototypes/19-raw-multiline-strings/README.md`)
hat Syntax, lexikalische Freiheit und Aufwand (~20 Zeilen) durchgearbeitet;
`lyric-v5-features.md:40` führt es als P2.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`r"…"` mit `#`-Zaun** | `r#"say "hi""#` | Rust, Swift | einzige Form, die `"` im Inhalt zulässt; `#` wird Token |
| B **`r"…"` ohne Zaun** | wie Python | Python | in **Lyric** (eine Anführungsform) bleibt `"` im Inhalt unmöglich — der JSON-Fall; in Python ist er über `r'…'` möglich |
| C **Backticks** | `` `…` `` | Go | kollidiert mit nichts; unbequem auf manchen Layouts, lästig in Markdown |

**Empfehlung: A.** `rf"…"` nicht in Runde 1 (additiv nachrüstbar). Kotlin belegt, dass „roh" und
„interpoliert" orthogonale Schalter sind.

**Bruch:** nein. **Abhängig von:** S29, S30, S46 (Formatter/LSP reichen Lexeme durch).
**Belegtyp:** gemessen.

---

### S13 — Mehrzeilen-Strings

**Heute:** gibt es nicht; `"""` über drei Zeilen erzeugt 18 Diagnosen, die erste (`LEX0009`)
benennt das Problem korrekt (`m01`). Ein sechszeiliger Hilfetext ist sechs Literale, fünf `+` und
fünf `\n`. **Korrektur gegenüber der zweiten Fassung:** ein Attributwert ist **nicht** nur ein
Literal — `docs/Grammar.md:199-201` (gelesen) erlaubt seit v2.4 auch einen Namen, der an ein
solches `let` gebunden ist. Aber eine `+`-Kette ist kein Literal, also ist auch die
`let`-Konstante aus einer Kette **kein** zulässiger Attributwert: ein mehrzeiliger Hilfetext in
`@Command { help = … }` bleibt heute nur als eine lange escapte Zeile schreibbar. Der Befund
steht, seine Formulierung war unvollständig.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`"""…"""`, Einzug der schließenden Klammer zur LEXZEIT** | weniger Einzug = Fehler | Swift, C# 11, Java 15 | ~50 Zeilen Lexer; Konstante bleibt Konstante — und damit Attributwert; Formatter darf den Block nie anfassen |
| B **`"""…"""` roh + `trimIndent()`** | Einzug zur Laufzeit | Kotlin, Scala | zwei Mechanismen; nicht mehr konstant → kein Attributwert, schlecht für `comptime` |
| C **`\\`-Zeilenpräfix** | jede Zeile beginnt mit `\\` | Zig | kein Einzugproblem; ungewohnt |
| D **`\`+Umbruch als Zeilenfortsetzung** | wie heute plus Fortsetzung | Rust, C | löst nur die `+`-Kette; ist als S42 eigene Frage |

**Empfehlung: A**, mit S12 in einer Runde. `f"""…"""` gehört dazu; einzeiliges `"""a"b"""` nicht
(dafür ist `r#"…"#` da). Die Folgefehler-Lawine (Befund k, k2) gehört unabhängig repariert:
ein `LEX0009` sollte den Rest der Zeile verschlucken.

**Bruch:** nein. **Abhängig von:** S12, S14, S25, S29, S42, Formatter-Vertrag. **Belegtyp:** gemessen.

---

### S14 — Die Escape-Menge, `\x`, und rohe Bytes

**Heute:** exakt `\n \r \t \\ \" \' \0 \xHH \u{…}` (`Grammar.md:107-109`, `Lexer.cs:604-628`).
`\e`, `\a`, `\b`, `\f`, `\v`, `\$` sind `LYR-LEX0007` (`p19`, `a13`). `\xFF` ist der Code-Punkt
U+00FF (`p03b`, `a15`). **Zusätzlich gelesen:** `\`+Zeilenumbruch ist **kein** `LEX0007`, sondern
kehrt still zurück (`Lexer.cs:606`) und wird dann zum `LEX0009` — S42.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`\e` aufnehmen** | eine Zeile Lexer | C# 13 | nichts bricht |
| B **`\x` streichen** | `\u{…}` ist die eine Form | — | Bruch für jeden `\xNN`-Benutzer |
| C **`\x` auf 0..0x7F begrenzen** | wie Rust in `str` | Rust | der irreführende Bereich fällt weg; kleiner Bruch |
| D **Byte-Literale `b"…"`** | `uint8[]` im Quelltext | Rust | zweiter Literaltyp; gehört zum Gebiet Bytes/IO |

**Empfehlung: A + C, D als Frage an Bytes/IO.** C ist über eine 4.7-Warnung ratterbar.

**Bruch:** A nein; C minor. **Abhängig von:** S13, S30, S42, Gebiet Bytes. **Belegtyp:** gemessen.

---

### S15 — `{{`, `}}` und die f/nicht-f-Asymmetrie

**Heute:** `"{{ x }}"` bleibt, `f"{{ x }}"` wird `{ x }` (`p11`, `a08`); einzelnes `}` im f-Text
geht still durch. Faltung im Lowering (`FunctionLowerer.cs:4845-4851`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **so lassen** | Status quo | Python, C#, Rust | die Asymmetrie haben alle |
| B **einzelnes `}` ablehnen** | Lexerfehler | Python, C#, Rust | minor Bruch |
| C **anderes Loch-Zeichen** | `\(…)` | Swift | `{}` ist der Konsens |

**Empfehlung: A + B.** Warnung in 4.7.

**Bruch:** B minor. **Abhängig von:** S05, S08 (dieselbe Klammerregel). **Belegtyp:** gemessen.

---

### S16 — Wo lebt Unicode?

**Heute:** `toUpper`/`toLower` = Simple-Case-Mapping des Hosts (`p15`, `m09`, `a07`);
`isAlpha`/`isUpper`/`isLower`/`isDigit` ASCII (`string.lyr:395`); `isWhitespace` seit 4.5 Unicode
(`string.lyr:406-410`). `lyric-v5-features.md:92` plant `std.unicode`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Kern bleibt ASCII, `std.unicode` kommt** | `isAlpha` → `isAsciiAlpha` | Rust | Namen brechen |
| A2 **A, plus `toUpper` wird benannt** | `toUnicodeUpper` (heutige Funktion) im Kern, Unicode-Version in der Spec | Rust | ein Name mehr bricht; die eine unicode-abhängige Funktion verschweigt ihren Namen nicht mehr |
| B **Kern wird Unicode** | Kategorien | Python, C#, Swift | Tabellen in jedes Runtime (Lyricpp) |
| C **Casing auch ASCII** | `toUpper` nur ASCII | Zig | bricht `"é".toUpper()` |

**Empfehlung: A2.** **Reihenfolge, die die zweite Fassung nicht bedacht hat:** S19-A (Prelude für
`std.string`) und A2 (Umbenennung) treffen dieselben Namen. Die Uhr muss so laufen: **4.7**
Umbenennung mit `@Deprecated` auf den alten Namen; **5.0** Prelude, das **nur die neuen Namen**
importfrei macht — die alten bleiben importpflichtig und deprecated, damit nichts erst importfrei
wird und dann anders heißt. Zwei Uhren, eine Reihenfolge.

**Bruch:** A2 minor. **Abhängig von:** S19 (Reihenfolge), S28, Bibliotheks-Umkehr
(`STATUS.md:2168-2172`). **Belegtyp:** gemessen.

---

### S17 — Renderbarkeit: warum decken Loch und `println` nicht dasselbe ab?

**Heute:** `f"{a}"` mit `int32` geht, `println(a)` ist `SEM0028` (`m08`, `a06`); `Display` hängt
an fünf Typen (`core.lyr:106-134`); das Loch weitet (`FunctionLowerer.cs:4915-4935`) — und die
Weitung ist bei `X`/`b` sichtbar (`n03`, S34). Nirgends steht, welche Skalare `println` nimmt.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`Display` für alle Zahlentypen** | zehn `extend`-Blöcke | Rust | ~60 Zeilen |
| B **das Loch verengen** | nur fünf Typen | — | bricht `f"{x}"` mit `int32` |
| C **numerische Interfaces** | `Num`/`Integer` liefern `Display` | — | hängt an statischen Interface-Membern (`PLAN.md:227`) |

**Empfehlung: A jetzt, C später; Menge in den Guide.**

**Bruch:** nein. **Abhängig von:** bedingte Konformanz, S34. **Belegtyp:** gemessen.

---

### S18 — Bleiben `+` und `*` auf Strings?

**Heute:** ja. `"a" + 1` ist `SEM0003` (`p18`); `"ab" * -2` ist still `""` (`p18b`, `a14`);
`"ab" * n` prüft die Ergebnislänge (`NativeRegistry.cs:343-366`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **beides behalten, negativ = Fehler** | Panik / Sema-Fehler bei Konstante | Rust (`repeat(usize)`) | minor |
| B **wie heute** | Status quo | Python | stille Null-Länge |
| C **`*` streichen** | `repeat(s, n)` | Rust, Go, C#, Swift, Kotlin | ein Operator weniger |
| D **`+=` in Schleife warnen** | Heuristik | — | Fehlalarme; **und der Compiler baut dieselbe Falle selbst (S22)** |

**Empfehlung: A; D erst nach S22.**

**Bruch:** A minor. **Abhängig von:** S22, Operator-Interfaces. **Belegtyp:** gemessen.

---

### S19 — Müssen Stringmethoden importiert werden?

**Heute:** ja; `SEM0072` warnt über den Import, der die Methoden sichtbar macht (`p03b`, `n05`,
`c01`); `import std.string;` ist `SEM0077`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Prelude** | `std.string` immer da | Rust, Kotlin, Swift | impliziter Import; braucht Regel „expliziter Import schlägt Prelude" |
| B **`extend` auf Builtins immer sichtbar** | ohne Import | — | ungleichmäßige Regel |
| C **so lassen, Warnung korrigieren** | `SEM0072` darf nicht feuern | — | minimal |
| D **Methoden in den Compiler** | eingebaut | Go, Python | Rule 2 |

**Empfehlung: A mit C als Sofortfix — nach S16-A2, nicht gleichzeitig** (Reihenfolge dort).

**Bruch:** A nein. **Abhängig von:** S16 (Reihenfolge), Gebiet Module. **Belegtyp:** gemessen.

---

### S20 — In welcher Einheit zählt die Breite eines Format-Spezifizierers?

**Heute:** UTF-16-Code-Einheiten (`m03`, `a10`; `NativeRegistry.cs:2826-2833`). `"😀"` polstert
wie zwei Zeichen bei `length()==1`; `"世界"` wie `"ab"`; `"e\u{301}"` wie zwei.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Code-Punkte** | dieselbe Einheit wie `length()` | Python, Rust (`char`s) | ein Native oder eine Umrechnung; `世界` bleibt schmal, aber stimmig |
| B **UTF-16 festschreiben** | Ist-Zustand in die Spec | C# | zwingt UTF-8-Runtimes zur UTF-16-Buchhaltung |
| C **Grapheme-Cluster** | | Swift | Tabellen, versionsabhängig |
| D **Terminalspalten** | East-Asian-Width | Go/Rust (extern) | keine Vergleichssprache hat das im Kern |

**Empfehlung: A**, in der Spec; D als `unicode.displayWidth(s)` in `std.unicode`.

**Bruch:** minor (astrale Zeichen bekommen ein Leerzeichen mehr; nicht per Warnung ratterbar,
Menge winzig). **Abhängig von:** S01, S05, S07, S16, S38. **Belegtyp:** gemessen.

---

### S21 — Welche `std.core`-Extensions auf `string` schreiben noch eine Positionsschleife?

**Heute:** `Ordered<string>.compare` (`core.lyr:271-297`) und `Hashable<string>.hash`
(`core.lyr:306-316`) — beide `charAt(this, i)` in einer `while`-Schleife. **Und jede
`while (i < s.length())`-Schleife in Benutzercode** (`n13`: 9,7 s bei n = 40 000, S39).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **beide Fälle nativ** | `rawCompare`, `rawHash` | Rust | ~20 Zeilen; Spec-Zeile für den Hash (S37) |
| B **A plus Audit aller `string`-Extensions in `stdlib/`** | systematisch | — | eine Session Lesearbeit |
| C **`charAt`/`length` billig machen** | Cursor-Cache oder Code-Punkt-Zähler pro String | CPython (PEP 393), Swift | repariert jede Schleife auf einmal; Speicher pro String; hängt an S01/S39 |
| D **Iterator-Form erzwingen** | `charAt` verschwindet | Rust, Go | major |

**Empfehlung: B**, und die `length()`-Frage getrennt als S39 — sie ist mit C halb beantwortbar
(ein gecachter `int` pro String kostet fast nichts), was für `charAt` nicht gilt.

**Bruch:** A/B nein; C nein; D major. **Abhängig von:** S01, S03, S27, S37, S39. **Belegtyp:** gemessen.

---

### S22 — Was kostet die Absenkung eines f-Strings selbst?

**Heute:** Linksfaltung aus Laufzeit-`concat` (`FunctionLowerer.cs:4874-4877`): k Löcher → bis zu
2k Aufrufe, k−1 Zwischen-Allokationen, Präfix mehrfach kopiert. Gemessen (`fs_*`, k Löcher mit je
20 000 Zeichen, 100 Durchläufe, Kontrolle k = 1): k = 8: +92 ms, k = 16: +222 ms, k = 32:
+625 ms — Faktor 2,4 / 2,8 pro Verdopplung, also überproportional. **Nicht gemessen: k = 2..4
mit kurzen Teilen — der Normalfall.**

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **so lassen** | Status quo | **keine** — der Quelltextkommentar beruft sich auf Roslyn, aber Roslyn senkt String-Löcher ohne Spec zu **einem** `string.Concat(a, b, c)` (bis 4 Argumente, dann `string[]`), nie zu einer Kette (behauptet) | überproportional; S18-D wollte vor genau dieser Falle warnen |
| B **n-stelliges `concatAll(parts: string[])`** | ein Aufruf, eine Allokation | Java (`StringConcatFactory`), C# (`String.Concat(params)`) | braucht ein Array im IR („the IR can do neither", `FunctionLowerer.cs:4820-4824`) |
| B2 **feste Arität `concat2/3/4`, darüber Builder** | Roslyns Muster | Roslyn, Java vor 9 | drei Natives mehr; keine IR-Änderung; deckt den Normalfall ohne Objekt |
| C **Builder-Absenkung** | `StringBuilder` auf, anhängen, zu | Kotlin | für k ≤ 4 vermutlich **teurer** als zwei native `concat` (Objekt + `string[]` + k Lyric-Aufrufe + `join`) — **nicht gemessen** |
| D **Rechtsfaltung mit Längenvorausberechnung** | erst Längen summieren | Rust (`format!` ruft `String::with_capacity(args.estimated_capacity())` — es rechnet **grob vor**, behauptet; die zweite Fassung schrieb das Gegenteil) | zwei Durchläufe; Nebenwirkungen im Loch heikel |

**Empfehlung: B2 — nach einer Messung bei k = 2..4, die vor der Entscheidung fehlt.** Die
zweite Fassung empfahl C auf Basis von k ≥ 8 mit 20-k-Teilen; für den realen f-String (1–3 Löcher,
kurze Teile) ist das keine Evidenz, und ein Lyric-geschriebener Builder ist dort plausibel eine
Verschlechterung. Roslyn und Java benutzen für kleine k ein festes Concat, nicht einen Builder.
Der Quelltextkommentar zu Roslyn gehört korrigiert. A ist nach S18-D nicht vertretbar.

**Bruch:** nein (Auswertungsreihenfolge festschreiben: links nach rechts, genau einmal).
**Abhängig von:** S18, Bytecode/VM (bei B). **Belegtyp:** gemessen (k ≥ 8); behauptet (Roslyn,
Rust); **offen** (k = 2..4).

---

### S23 — Wie lang darf ein Lyric-String sein, und steht das in der Spec?

**Heute:** `int.MaxValue` UTF-16-Einheiten, Meldung zählt „code point(s)" (`m06`, `a12`;
`NativeRegistry.cs:355-364`). Keine Grenze in der Spec (gelesen). Und die Breite kennt gar keine
Grenze (`n14`, S38).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Garantierte Mindestkapazität in Code-Punkten, Überlauf = definierte Panik** | „ein String hält mindestens 2²⁹ Code-Punkte; darüber ist das Verhalten runtime-definiert, ein Überlauf panikt mit Code X" | C (`SIZE_MAX` ist implementierungsdefiniert, aber die Semantik des Überlaufs nicht) | 2²⁹ ist so gewählt, dass das .NET-Runtime es **heute** erfüllt (≈2³⁰ UTF-16-Einheiten, astral = 2 Einheiten); die Schwelle bleibt pro Runtime verschieden, aber der Vertrag sagt, ab wo |
| B **Grenze in Bytes (UTF-8)** | „mindestens 2³¹−1 Bytes" | Go, Rust | vom .NET-Runtime **nicht erfüllbar** (max ≈2³⁰ Einheiten) |
| C **rein implementierungsdefiniert** | Spec nennt nur Code X | — | **widerspricht der Linie**, mit der S05, S16, S20, S31 entscheiden (die Kritik hat recht): wenn die Schwelle pro Runtime frei ist, darf die Case-Tabelle es auch |
| D **schweigen** | Status quo | — | nicht vertretbar |

**Empfehlung: A statt C.** **Korrektur gegenüber der zweiten Fassung:** C war inkonsistent mit
dem eigenen Argument. A ist die kleinste Form, die konsistent bleibt: die Spec **garantiert** einen
Bereich, in dem jedes Runtime gleich antwortet, und **benennt** den Rest als nicht portabel. Die
frühere Nennung von Java als Vorbild („`String` ≤ 2³¹−1, spezifiziert") ist **gestrichen** — die
JLS spezifiziert keine Maximallänge, die Grenze folgt aus `length(): int` und der Array-Größe,
also aus demselben Implementierungsartefakt wie bei .NET (behauptet). Sofort in 4.x: die Meldung
zählt Code-Punkte statt UTF-16-Einheiten.

**Bruch:** nein. **Abhängig von:** S01, S06, S24, S38. **Belegtyp:** gemessen.

---

### S24 — Wie sieht ein String an der Host-/FFI-Grenze aus?

**Heute:** `System.String`, ungeprüft (`DotnetBinding.cs:152-190`, gelesen): `TypeTag.String` ↔
`typeof(string)`, `null` vom Host → leerer String (Zeile 184-187), `char` über U+FFFF abgelehnt
(Zeile 165-172). Ungültiges UTF-8 / unpaarige Surrogate über die FFI: nicht geprüft.
`utf8Decode` ist strikt (`NativeRegistry.cs:456-480`), `std.io.file.text` ersetzt durch U+FFFD
(`NativeRegistry.cs:710-717`) — zwei Antworten, nie nebeneinandergestellt. **Und NUL** (`n07`,
`n21`): im String ein Zeichen wie jedes andere, an der Datei-Grenze ein `null` ohne Grund (S41).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **UTF-8 mit Länge, Lyric besitzt den Speicher** | `(ptr, len)`; eingebettete NUL erlaubt | Rust, Go | richtig für Lyricpp und C-ABI; .NET-Pfad konvertiert pro Aufruf |
| B **NUL-terminiert** | C-ABI | C | verbietet eingebettete NUL; Länge O(n) |
| C **Status quo festschreiben** | Host-String | — | Grenze pro Host verschieden |
| D **validieren beim Eintritt** | ungültige Form = Panik | Swift, Rust | O(n) pro Aufruf; kein Lyric-Wert kann je eine Form haben, die `utf8Encode` nicht darstellt |

**Empfehlung: A für die Spec, D als Regel, C bis dahin.** Strikt ist Vorgabe, ersetzend heißt so
(`readTextLossy`); `null` → `?string`; NUL nach S41.

**Bruch:** A/D minor bis major je nach Hostmenge. **Abhängig von:** S01, S23, S41, Gebiet FFI.
**Belegtyp:** gelesen.

---

### S25 — Was macht ein Mehrzeilen-Literal mit den Zeilenenden der Quelldatei?

**Heute:** stellt sich nicht; `Grammar.md:40` lässt `\n` und `\r\n` zu.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Inhalt auf `\n` normalisieren** | Lexer ersetzt `\r\n` | Swift, C# 11, Java 15, Kotlin — **und Go** (die Go-Spec verwirft `\r` in Raw-Literalen; behauptet) | plattformunabhängiger Wert; CRLF absichtlich nur per Escape |
| B **Bytes durchreichen** | was in der Datei steht | **keine** — die zweite Fassung nannte Go, das ist falsch | Golden-Tests und Hilfetexte werden `autocrlf`-abhängig |
| C **CRLF im Block verbieten** | Lexerfehler | — | unzumutbar auf Windows |

**Empfehlung: A**, ohne Alternative, in die Spec. **Korrektur:** B hat kein reales Vorbild mehr;
alle fünf Vergleichssprachen normalisieren.

**Bruch:** nein. **Abhängig von:** S13, S14. **Belegtyp:** gelesen (Grammar); behauptet (Go).

---

### S26 — Braucht die Formatsprache Positions- oder Namensargumente?

**Heute:** nein — das Loch enthält einen Ausdruck (`Grammar.md:105`); es gibt keine
Argumentliste. Sobald S11 ein Template als Wert einführt, braucht es einen Bezug.
**Korrektur:** „alle fünf Vergleichssprachen mit einer Laufzeit-`format`" → **vier** (.NET `{0}`,
Python `{0}`/`{name}`, Go `%[2]d`, Java `%1$s`); Rusts `{0}`/`{name}` sind Compile-Zeit-Bezüge
in einem Makro und belegen ein Template als Wert nicht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Positionsindizes** | `{0}`, `{1}` nur im Laufzeit-Template | .NET, Python, Java | zwei Loch-Formen an verschiedenen Stellen |
| B **Namensargumente** | `{name}` gegen `Map<string, string>` | Python | Fehler „Name fehlt" zur Laufzeit |
| C **beides** | | Python | Rule 2 |
| D **nichts** | Reihenfolge fix | — | löst den Bedarf nicht |

**Empfehlung: A.** Plural/Geschlecht (ICU-MessageFormat) gehören nicht in die Sprache — CLDR-Daten
im Bytecode; das gehört in die Spec geschrieben.

**Bruch:** nein. **Abhängig von:** S05, S08, S11, S36. **Belegtyp:** gelesen.

---

### S27 — Wie wird die Migration von S05/S07 werkzeugseitig getragen?

**Heute:** nur Warnungen; kein `lyric fix` (`README.md:123`: `new run build pack fmt test check
disasm repl`). `lyrfmt` ist optionsfrei (`README.md:129`) und lässt Literale unangetastet
(gemessen `n15_fmt`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **nur Warnungen** | | — | n Handgriffe; Klasse 3/4 nur, wenn jemand liest |
| B **`lyric fix`** | eigenes Verb | `cargo fix`, `go fix` | ein Verb mehr; Umschreibung muss verlustfrei sein oder sich weigern |
| C **`lyric fmt --fix-specs`** | Formatter trägt es | `gofmt -r` | widerspricht „no style options" |
| D **nur Sicheres umschreiben, Rest markieren** | Klasse 1/2 automatisch, 3/4 als `// TODO(lyric-5)` | `go fix` | ehrlich |

**Empfehlung: B + D.** Klasse 3 (`X`/`b` auf `int8`..`int32`, `e`) und Klasse 4 (heute still
falsch) sind **nicht** sicher umschreibbar: bei Klasse 4 könnte ein Golden-Test den heutigen Müll
eingefroren haben. **Bei Vorbild Rust ist die Klasse-3-Menge kleiner** als in der zweiten Fassung
(kein `int`-`X`-Fall mehr).

**Generische Löcher:** heute ein Fehler (`n23`), also erst mit S09-D relevant. Drei Antworten:
(1) Regel am Typ, Prüfung an der Instanz; (2) `Display`-Löcher immer links; (3) explizite
Ausrichtung im generischen Loch Pflicht. **Empfehlung: 2 plus eine Warnung „generisches Loch ohne
explizite Ausrichtung"** — die zweite Fassung nannte den Preis von 2 (derselbe Wert verrutscht,
wenn er über eine generische Hilfsfunktion läuft) und unterschätzte ihn; die Warnung macht die
Stelle sichtbar, ohne die Sonderregel aus 3 zu brauchen.

**Bruch:** nein (Werkzeug). **Abhängig von:** S05, S06, S07, S09, S44, CLI/Toolchain.
**Belegtyp:** gelesen; gemessen (`n15_fmt`, `n23`).

---

### S28 — Welche Case-Abbildung will Lyric — SIMPLE oder FULL?

**Heute:** SIMPLE, 1:1 (`m09`, `a07`; `NativeRegistry.cs:530-536`): `s.toUpper().length() ==
s.length()` gilt ausnahmslos; `ß` bleibt `ß`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **SIMPLE festschreiben** | Längenzusicherung | C#, Java | `"ß".toUpper()` bleibt `ß` |
| B **FULL auf `string`, SIMPLE auf `char`** | `ß` → `SS` | Swift, Python | Sondertabelle in jedes Runtime; zwei Stärken unter ähnlichem Namen |
| C **FULL überall, `char → string`** | | Rust (`to_uppercase` → Iterator) | bricht jede `char`-Signatur |
| D **ASCII im Kern, Unicode in `std.unicode`** | `toAsciiUpper` + `unicode.toUpper` | Zig, Rust | Kern tabellenfrei; `"é".toUpper()` geht im Kern nicht mehr |

**Empfehlung: A für 5.0 — OHNE „D als Richtung".** **Korrektur:** die zweite Fassung wollte A
festschreiben und D „als Richtung" behalten; eine Zusicherung, die man zu brechen plant, ist
keine. Die Auflösung liegt in S16-A2: die heutige Funktion heißt `toUnicodeUpper` und **sichert
SIMPLE zu**; eine spätere `std.unicode.toUpperFull` ist dann eine **Ergänzung** unter neuem Namen,
kein Bruch der Zusicherung. D wird damit nicht „Richtung", sondern ein additiver Nachbar. Die Spec
nennt die Unicode-Version der Tabelle.

**Bruch:** A nein; B/C major; D minor. **Abhängig von:** S16, S01. **Belegtyp:** gemessen.

---

### S29 — Sind String- und Char-Literale Muster, und was heißt das für die neuen Literalformen?

**Heute:** ja, beide (`m02`, `a11`). **Absenkung gelesen per `lyric disasm` (`n19`):** Kette aus
`eq string` + `condbr`, Arm für Arm — die zweite Fassung hatte das als „behauptet" markiert.

Jede neue Literalform muss beantworten, ob sie ein Muster ist: S02 (`'😀'` im `match` — muss
nach dem Fix gehen), S12/S13 (`r#"…"#`, `"""…"""` — Konstanten, also ja, aber in der Grammatik
sichtbar), f-Strings (**nein**, sie rechnen; in die Spec schreiben).

| Option (Absenkung) | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Kette von `eq string`** | **Ist-Zustand (gelesen)** | — | O(k) Vergleiche |
| B **Länge zuerst** | nach Länge gruppieren | Roslyn | billig; schneidet die meisten Arme weg — **aber `length()` ist O(n) (S39)** |
| C **Hash-Sprungtabelle** | `hash` + Bucket + Vergleich | C#, Java | hängt an S04-A: `hash` ist heute quadratisch |
| D **Trie** | | — | viel Compilerarbeit |

**Empfehlung: B messen, dann entscheiden — und B setzt S39 voraus**, sonst kostet das Vorfiltern
mehr als der Vergleich.

**Bruch:** nein. **Abhängig von:** S02, S12, S13, S04, S21, S39, Gebiet Enums/Patterns.
**Belegtyp:** gemessen; gelesen (disasm).

---

### S30 — Wie stellt ein Programm einen String mit Steuerzeichen dar?

**Heute:** keine Regel, weil es `debug()` nicht gibt (S10).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **nur `"` und `\`** | minimal | — | `\n` zerreißt die Zeile |
| B **Nicht-Druckbares als `\u{…}`, ASCII roh, Nicht-ASCII roh** | Lyrics Escape-Menge rückwärts | Rust `{:?}`, Swift | `debug(s)` ist ein gültiges Lyric-Literal |
| C **alles Nicht-ASCII escapen** | | Python `ascii()` | `"世界"` unlesbar |
| D **`{x:?}` + `{x:#?}`** | zwei Stufen | Rust | mehr Sprache |

**Empfehlung: B**, mit der Zusicherung „`debug(s)` ist eine gültige Literalschreibweise für `s`";
`\x` taucht nie auf; Compiler-Diagnosen benutzen dieselbe Funktion.

**Bruch:** nein. **Abhängig von:** S10, S14, Diagnosen, `lyrtest`. **Belegtyp:** gelesen.

---

### S31 — Wird der String-Konstantenpool dedupliziert, und ist das zugesichert?

**Heute:** dedupliziert (`BytecodeWriter.cs:876-880`), nicht zugesichert (`Bytecode.md:133`:
„constant pool, strings only").

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **zusichern** | gleiche Strings teilen einen Index | Java (`CONSTANT_Utf8`) | kostet nichts |
| B **ausdrücklich nicht** | | Rust | `==`-Schnellpfad über Identität unzulässig |
| C **schweigen** | | — | zweites Runtime rät |

**Empfehlung: A**, mit S05/S08 zusammen. Unter S08-A (eingeschränkt) bleibt der Spec-**Rest**
konstant im Pool, nur Breite/Präzision werden Argumente. Messung (Dateigröße, Ladezeit) gehört in
die S05-Umsetzungsrunde — behauptet als Erwartung, nicht als Zahl.

**Bruch:** nein. **Abhängig von:** S05, S08, Bytecode/VM. **Belegtyp:** gelesen.


---

### S32 — Was bedeutet `.N` ohne Typ — auf einem Float, auf einem Integer?

**Heute:** auf Float `32` statt `3.14` (`m05`, `a02`), auf Integer `52` (`n20`: `f"{5:.2}"`).
Beides .NET-Custom-Format mit Literalziffern; keine Diagnose. Die Vorbilder widersprechen sich —
gemessen mit Python 3.14.4: `format(3.14159, '.2') == '3.1'` (**signifikante** Stellen),
`format(5, '.2')` → `ValueError`; behauptet für Rust: `{:.2}` = **Nachkommastellen** (`3.14`),
auf Integern **ignoriert** (`std::fmt`: „For integral types, this is ignored"). Die Kritik nannte
Rust hier „Fehler" — das ist nicht richtig, Rust ignoriert still; die Konsequenz für die
Empfehlung ist dieselbe.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Nachkommastellen; auf Integer Fehler** | `.2` → `3.14`; `{5:.2}` ist ein Sema-Fehler | Rust (Float-Teil); strenger als Rust auf Integern | die Erwartung der meisten Umsteiger (C `%.2f`, Rust, .NET `F2`); ein Fehler statt Rusts stillem Ignorieren, weil ein ignorierter Spec „it printed, but not what I asked" ist |
| B **Signifikante Stellen** | `.2` → `3.1` | Python | überrascht jeden, der `%.2f` kennt; Python selbst braucht `.2f` für den Normalfall |
| C **`.N` nur mit Typ gültig** | `.2f` Pflicht, `.2` allein Fehler | — | eindeutig, aber ein Zeichen mehr an der häufigsten Stelle |
| D **wie heute** | | — | still falsch |

**Empfehlung: A.** Sie ist die Rust-Antwort mit einer Verschärfung, die zur Lyric-Linie passt
(kein stilles Ignorieren; vgl. `TypeChecker.cs:3006-3010`, das denselben Grundsatz für
`Display`-Specs formuliert). Die Spec schreibt die Regel als eine Zeile hin, damit die
Compile-Zeit- und die Laufzeit-Implementierung (S36) nicht auseinanderlaufen.

**Bruch:** major (Teil von S05; heute-falsche Ausgabe wird richtig — Klasse 4).
**Abhängig von:** S05, S06, S33, S36, S44. **Belegtyp:** gemessen (Lyric, Python); behauptet (Rust).

---

### S33 — Welche Spec-Bestandteile gelten auf `char`-, `bool`- und `string`-Löchern?

**Heute:** nur eine Breite mit optionalem Minus; alles andere ist eine Panik (`n01a`, `n01b`;
`NativeRegistry.cs:2826-2833`). `f"{c:X}"` panikt, obwohl `fmt.lyr:33-34` „through a .NET
specifier" verspricht (gelesen falsch). Es gibt heute also **zwei Spec-Teilsprachen** (Zahl:
.NET + Breite; Nicht-Zahl: Breite), und keine ist dokumentiert.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **nur `Layout`** (`[[fill]align][width]`) | Nicht-Zahl-Skalare bekommen genau das, was `Display`-Werte unter S09-D bekommen | Rust (Präzision auf `&str` ist dort ein Maximum — siehe C) | ein Fall weniger in der Grammatik: **zwei** statt drei (Zahl / alles andere) |
| B **`Layout` + `?`** | zusätzlich `{s:?}` für `debug()` (S10/S30) | Rust | `?` muss auch auf Zahlen gelten, sonst zwei Regeln |
| C **`Layout` + `.N` als Maximallänge auf `string`** | `{s:.5}` schneidet ab | Rust, Python | eine dritte Bedeutung von `.N` (Nachkomma / Maximum) — genau die Mehrdeutigkeit, die S32 vermeiden will |
| D **wie heute, aber dokumentiert** | Breite mit Minus | — | zementiert die Minus-Konvention, die S07 kippt |

**Empfehlung: A + B.** Die Grammatik hat dann **zwei** Fälle: `Numeric = Layout Presentation?`,
`Other = Layout ['?']` — und `Other` gilt für `char`, `bool`, `string` **und** `Display`
gleichermaßen. Das ist die Antwort auf die Frage aus S09 („dritter Fall?"): nein, Nicht-Zahl-Skalar
und `Display` sind **derselbe** Fall. C ausdrücklich nein: `substring` ist die Stelle für ein
Abschneiden. Der `fmt.lyr`-Kommentar wird in 4.x korrigiert (Anhang A #14).

**Bruch:** minor (`{s:-5}` verliert die Minus-Form → S07-Klasse 2). **Abhängig von:** S05, S07,
S09, S10, S32. **Belegtyp:** gemessen.

---

### S34 — Rendert `{x:X}`/`{x:b}` die Breite des DEKLARIERTEN Typs oder des geweiteten i64, und gibt es `#`?

**Heute:** i64 (`n03`): `int32 = -7` → `FFFFFFFFFFFFFFF9` (16 Stellen), `int8 = -1` →
`FFFFFFFFFFFFFFFF`, `uint8 = 200` → `C8`. Grund: das Loch weitet jeden Skalar auf i64/u64
(`FunctionLowerer.cs:4915-4935`) — der Native sieht den Ursprungstyp nicht. `#x` gibt `255x`
(`n20`). Rust rendert `-7i32` als `FFFFFFF9` und `{:#x}` als `0xff` (behauptet); .NET direkt
ebenfalls 8 Stellen für ein `Int32`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Breite des deklarierten Typs** | die Sema reicht die Bitbreite als Argument oder wählt `formatInt8/16/32` | Rust, .NET, C (`%hhx` mit Cast) | das Lowering muss den Typ vor der Weitung festhalten; ein Argument mehr pro Loch **oder** vier weitere Natives |
| B **immer 64 bit, dokumentiert** | Ist-Zustand in die Spec | — | `{s:X}` mit `int8` gibt 16 Stellen — kein Vorbild, aber konsistent mit „das Loch weitet" |
| C **`X`/`b` auf schmale Signed-Typen verbieten** | nur `int`/`uint*` erlaubt | — | vermeidet die Frage; zwingt zu `as int` und macht dann 16 Stellen explizit |
| — **`#`-Präfix** | `{x:#x}` → `0xff`, `{x:#b}` → `0b11`, `{x:#o}` → `0o377` | Rust (`0x`/`0b`/`0o`), Python (`0x`/`0b`/`0o`) | ein Flag; gehört zu S05-B |

**Empfehlung: A, und `#` aufnehmen.** A ist die einzige Option, unter der `fmt.lyr:22-25` („as
`{:x}` in Rust") wahr wird. Für `uint*` ändert sich nichts (Weitung ist wertneutral); für `int*`
ist es Klasse 3 (still verschieden), also Warnung pro Loch mit schmalem Signed-Typ in 4.7. Die
Spec schreibt: „`X`, `x`, `b`, `o` rendern das Zweierkomplement in der Bitbreite des statischen
Typs des Lochs." Ein Runtime, das den Typ nicht sieht (Lyricpp mit demselben Bytecode), braucht
das Bitbreiten-Argument — daher A über ein Argument, nicht über vier Natives.

**Bruch:** minor (nur `int8/16/32` mit `X`/`x`/`b`; `int` unverändert). **Abhängig von:** S05, S17,
S36. **Belegtyp:** gemessen (Lyric); behauptet (Rust).

---

### S35 — Was ist die Bereichsregel von `substring`?

**Heute:** zwei Regeln (`n04a`, `n04c`, `n04d`, `n04e`): `start` außerhalb `0..=length` panikt
(`LYR-VM0006`), `count` über das Ende wird still geklemmt (`"hello".substring(1, 10)` → `ello`),
`substring(5, 0)` → `""`. `charAt(3)` auf `"abc"` panikt (`n04b`). Weder Guide noch
`string.lyr:130-133` sagen, was bei Überlauf passiert (gelesen).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **strikt an beiden Enden** | `start + count > length` panikt | Rust (`&s[a..b]` panikt), Java (`StringIndexOutOfBounds`), C# (`ArgumentOutOfRange`) | Bruch für jedes Programm, das das Klemmen (bewusst oder nicht) nutzt; konsistent mit `charAt` |
| B **klemmen an beiden Enden** | auch `start > length` → `""` | Python (`s[a:b]` klemmt), Go (`s[a:b]` panikt — nicht Vorbild) | nie eine Panik; verbirgt Rechenfehler (`start` um eins zu groß liefert leer statt Fehler) |
| C **wie heute, dokumentiert** | `start` strikt, `count` geklemmt | — | zwei Regeln bleiben, aber sie stehen im Guide; „bis zum Ende" ist damit `substring(i, length)` ohne Rechnen |
| D **Regel an den neuen Namen binden** | `slice(start, end)` (S03-E) ist strikt; `substring(start, count)` bleibt wie heute bis zur Entfernung | JS (`slice` vs `substr`) | keine stille Änderung einer bestehenden Signatur; die Uhr aus S03 trägt beides |

**Empfehlung: D, mit C für die Übergangszeit.** Eine Änderung der Bereichsregel unter demselben
Namen ist dieselbe Falle wie eine Änderung der Signatur (S03): `substring(1, 10)` bliebe gültig
und würde plötzlich panikken. D nutzt die ohnehin laufende Umbenennung, um die strikte Regel
(A) ohne Bruch einzuführen. Der Guide bekommt sofort die heutige Regel (C) — das ist ein
Doku-Fehler, kein Feature.

**Bruch:** D nein (neuer Name), C nein. A wäre minor–major (still). **Abhängig von:** S03, S39.
**Belegtyp:** gemessen.

---

### S36 — Bleibt `std.fmt.formatInt/Float/…(value, spec: string)` mit einem LAUFZEIT-Spec öffentlich?

**Heute:** ja. Sechs `pub fn formatX(value, spec: string)` (`fmt.lyr:26-37`), registriert als
Natives (`NativeRegistry.cs:640-685`); gemessen (`n18`) nehmen sie einen zur Laufzeit gebauten
String. Der Spec-Parser lebt damit **nur** im Runtime; der Compiler übersetzt `{x:spec}` in einen
Aufruf mit Spec-Konstante (`FunctionLowerer.cs:4905-4913`). Jede Compile-Zeit-Prüfung (S06)
verdoppelt den Parser, jedes zweite Runtime (Lyricpp) verdreifacht ihn.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **API bleibt, Parser in jedem Runtime** | `formatX(value, spec: string)` öffentlich; Spec definiert die Sprache einmal, Compiler und Runtime implementieren sie | Python (`format(x, spec)` zur Laufzeit) | zwei (drei) Implementierungen, die gleich antworten müssen; Konformanzfälle für beide Pfade; ungültiger Spec zur Laufzeit bleibt eine Panik (S47) |
| B **API verschwindet; nur f-Strings tragen Specs** | `formatX` wird `@Deprecated`, Ersatz sind typisierte Funktionen (`fixed(x, 2)`, `hex(x)`, `padLeft`) | Rust (kein `format` mit String-Spec; `format!` ist Makro) | Bruch für jeden `formatX`-Aufrufer; ein Parser weniger; dynamische Präzision geht nur über S08 |
| C **API bleibt, Spec als Wert** | `formatX(value, spec: FormatSpec)`, `FormatSpec` ist ein Struct (`width`, `align`, `precision`, `kind`), den `FormatSpec.parse(s)` als `Result` liefert | .NET (`NumberFormatInfo`) grob; Go (`fmt.State`) | ein Typ mehr; der Parser bleibt, ist aber **einmal** in Lyric geschrieben (`std.fmt`) und der Native nimmt nur noch den Struct — kein String-Parser mehr im Runtime |
| D **`spec` muss ein Literal sein** | Sema verlangt eine Konstante | — | kein Sprachmittel dafür (keine `comptime`-Parameter); ein Native, der Konstanz verlangt, ist ein Sonderfall |

**Empfehlung: C.** Es ist die einzige Form, unter der der Parser **genau einmal** existiert: in
Lyric, in `std.fmt`, portabel per Bytecode (std ist source-first). Der Compiler ruft für ein
f-String-Loch **denselben** Lyric-Parser zur Compile-Zeit über die VM-Sandbox — wie `comptime`
heute (S45) — und bekommt so die Diagnose ohne zweite Implementierung; der Native formatiert nur
noch nach einem geparsten Struct. Für Lyricpp bedeutet das: ein `FormatSpec`-Struct-Layout und
sechs Natives, kein String-Parser. B ist die reine Rust-Antwort, kostet aber `formatFloat(x,
userSpec)` ersatzlos; A ist der Status quo mit verdoppeltem Parser — genau die
Zwei-Implementierungen-Falle, die S06 der zweiten Fassung übersehen hat.

**Bruch:** minor (Signaturwechsel mit `@Deprecated`-Uhr: `formatX(v, "F2")` → `formatX(v,
FormatSpec.parse("F2")!)` oder sprechende Helfer). **Abhängig von:** S05, S06, S45, S47,
std-Grundsatz „source-first". **Belegtyp:** gemessen (`n18`); gelesen.

---

### S37 — Ist der Hash-Algorithmus von `string` Teil des Sprachvertrags?

**Heute:** beobachtbar und implementierungsfest, aber nur im Kommentar: `"".hash() ==
-3750763034362895579` (FNV-Offset-Basis, `n05`, `b08`), FNV-1a-64 **über Code-Punkte** (`n22`,
nachgerechnet gegen UTF-16 und UTF-8). `core.lyr:301-303` (gelesen) begründet „in Lyric rather
than native" mit „produce the same result everywhere". Die Map-Iterationsreihenfolge hängt daran
(`collections.lyr:547`). S04-A (nativ) verlagert die Garantie ins Runtime.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Algorithmus in der Spec** | „`string.hash()` ist FNV-1a-64 über die Code-Punkte, Offset `0xcbf29ce484222325`, Prime `0x100000001b3`, Überlauf wrapt" | Java (`String.hashCode` ist in der JLS/JavaDoc als `s[0]*31^(n-1) + …` spezifiziert) | jedes Runtime muss es so tun; der Algorithmus ist dann nie mehr änderbar ohne major |
| B **nicht zusichern, nur „stabil innerhalb eines Laufs"** | Runtime darf wählen | Rust (`Hash` ist nicht stabil zwischen Versionen; `HashMap` ist randomisiert), Python (`hash(str)` ist per Prozess randomisiert) | Map-Reihenfolge ist dann nicht portabel — und Lyric-Programme (Golden-Tests) haben sie heute de facto |
| C **A, aber `hash()` bleibt in Lyric** | S04-A nur für `compare`; `hash` wird über S39/S21-C schnell | — | die Quadratik von `hash` bleibt, bis `charAt` billig ist |

**Empfehlung: A, im selben Commit wie S04-A.** Das Programm kann den Hash sehen, also gehört er
in den Vertrag — die Kritik hat recht, dass die zweite Fassung diesen Preis von S04-A nicht
genannt hat. B ist die sicherere Antwort für eine Sprache mit untrusted Input (HashDoS), aber
Lyric hat sich in `core.lyr:301-303` bereits für Determinismus entschieden; das gehört
festgeschrieben, nicht heimlich behalten. Die Spec-Zeile enthält ausdrücklich „über Code-Punkte",
weil ein UTF-8-Runtime sonst über Bytes hashen würde (gemessen andere Werte).

**Bruch:** nein (Zusicherung des Ist-Zustands). **Abhängig von:** S04, S21, S39, S48, Gebiet
Collections. **Belegtyp:** gemessen.

---

### S38 — Wird eine Breite gegen das Allokationsbudget der Sandbox geprüft?

**Heute:** nein. `f"{x:2000000000}"` endet als nackter .NET-Absturz **`Out of memory.`** (`n14`)
— kein `LYR-`-Code, kein Panik-Rahmen, keine Zeilenangabe. Kontrolle: `string.repeat` prüft die
Ergebnislänge und panikt sauber (`NativeRegistry.cs:343-366`), `Padded` ruft `PadRight` ungeprüft
(`NativeRegistry.cs:2826-2833`, gelesen). Ob `ExecutionBudget.cs` Allokationen deckt, ist für
diesen Pfad gemessen irrelevant — er läuft daran vorbei.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Breite gegen dieselbe Grenze wie `repeat`** | `width > Grenze` → `LYR-VM0006`-Panik mit Text | — (Rust: `usize`, praktisch unbegrenzt; Python: `MemoryError`) | fünf Zeilen; gleiches Verhalten wie `*` |
| B **Breite in der Spec begrenzen** | z. B. `width ≤ 2¹⁶` als Sprachregel; Sema lehnt größere Konstanten ab | — | eine willkürliche Zahl in der Spec; dynamische Breite (S08) braucht die Laufzeitprüfung trotzdem |
| C **Allokationsbudget der Sandbox gilt für jede String-Allokation** | `LYR-CAP`-Budget, das `repeat`, `Padded`, `concat`, `StringBuilder` gleichermaßen prüft | Wren/Lua (Allokator-Hook) | eine Stelle statt fünf; kostet einen Zähler pro Allokation |

**Empfehlung: C, mit A als Sofortmaßnahme in 4.x.** Ein unbehandelter `OutOfMemoryException`
ist in einer Sprache mit Sandbox-Anspruch nicht vertretbar: ein eingebettetes Lyric-Skript kann
den Host mit einem einzigen f-String abschießen. Die Spec sagt: „jede String-Allokation zählt
gegen das Allokationsbudget; ein Überschreiten ist Panik X" — und genau dieser Code ist der aus
S23-A. B nein: Breite braucht keine eigene Grenze, sie braucht dieselbe wie jede Allokation.

**Bruch:** nein (heute Absturz → Panik). **Abhängig von:** S20, S23, Gebiet Laufzeit/Sandbox
(Budget). **Belegtyp:** gemessen.

---

### S39 — Ist `length()` O(1) oder O(n), und ist das zugesichert?

**Heute:** O(n) bei jedem Aufruf (`CodepointCount`, `NativeRegistry.cs:1232-1237`), dokumentiert
als Grund für die Klammern (`string.lyr:114-121`, Guide 13:653). Gemessen (`n13`):
`while (i < s.length())` über 40 000 Code-Punkte **9 698 ms**, mit gecachter Länge **14 ms** —
quadratisch ohne ein einziges `charAt`. Das Argument von `string.lyr:45-46` („no `s[i]`, so a
quadratic index loop cannot be written") ist damit **halb**: die naheliegendste
Schleifenbedingung der Welt ist sie trotzdem.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Code-Punkt-Zahl pro String cachen** | ein `int` neben dem .NET-String (Lyric-eigener String-Wrapper oder ein `ConditionalWeakTable`); `length()` O(1) nach dem ersten Aufruf | Python (PEP 393: Länge gespeichert), Swift (`count` O(n), aber `utf8.count` O(1)) | ein Wrapper-Objekt pro String-Wert **oder** ein Nebentisch; berührt die `LyrValue`-Repräsentation |
| B **O(n) zusichern, Doku warnen** | Guide: „cache `length()` before a loop" | — | die Falle bleibt; billig |
| C **Sema-Warnung „`length()` in einer Schleifenbedingung"** | Heuristik | — | Fehlalarme (der String ändert sich in der Schleife nicht — er ist immutable, also **kein** Fehlalarm möglich: die Warnung ist exakt) |
| D **`length()` als Property mit O(1)-Vertrag** | `s.length` wie bei Arrays | Go (`len(s)` O(1), aber Bytes) | setzt A voraus; ändert jede Schreibweise (major) |

**Empfehlung: A + C, und die Komplexität in die Spec.** C ist ungewöhnlich exakt: weil ein String
immutable ist, ist ein `length()` in einer Schleifenbedingung über denselben Wert **immer**
hoisting-fähig — die Warnung hat keine Fehlalarme, und der Compiler könnte das Hoisting gleich
selbst machen. A macht die Warnung dann überflüssig, kostet aber Repräsentation; deshalb C
sofort (4.x), A mit S01/S21-C. Die Spec sichert zu: „`length()` ist amortisiert O(1)" **oder** „ist
O(n)" — eines von beiden muss dastehen, sonst kann Lyricpp beliebig entscheiden und derselbe
Code läuft dort 700× anders.

**Bruch:** nein. **Abhängig von:** S01, S21, S29-B, S35. **Belegtyp:** gemessen.

---

### S40 — Ist `char` geordnet, und in welcher Einheit?

**Heute:** ja, direkt im Bytecode: `'a' < 'b'` → `true`, `'\u{1F600}' > 'z'` → `true` (`n06`),
und `lyric disasm` zeigt **`lt char`** / **`gt char`**. `std.core` deklariert `extend char ::
[Ordered<char>]` mit `this < other` (`core.lyr:213-223`, gelesen). Der IR-Verifier zählt `Char`
als Integer (`IrVerifier.cs:1755-1761`). **`docs/Bytecode.md:719` sagt `lt … require a numeric
type` und definiert „numeric" nicht** — `char` ist Tag `0x0C` (`Bytecode.md:594`); ob es numerisch
ist, steht nirgends. Der Guide nennt die Ordnung auf `char` nicht (gelesen: kein Treffer).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Ordnung nach Skalarwert, in der Spec** | `Bytecode.md`: „numeric = `i8`..`f64` und `char`; `char` vergleicht nach Code-Punkt"; Guide: „`'a' < 'b'` ist die Code-Punkt-Ordnung, keine Kollation" | Rust (`char: Ord` nach Skalarwert), Go (`rune` ist `int32`), C# (`char` UTF-16-Einheit) | eine Zeile Spec, eine Zeile Guide; nichts ändert sich |
| B **`char` nicht numerisch, Ordnung über `Ordered<char>` in Lyric** | `compare` nutzt `as int` | — | ein Umweg für etwas, das die VM kann; der Verifier müsste `lt char` verbieten (Formatänderung) |
| C **Ordnung auf `char` streichen** | nur `==` | — | `sort` auf `char[]` und Bereichsprüfungen (`c >= 'a' && c <= 'z'` in `std.string`) brechen |

**Empfehlung: A.** Der Ist-Zustand ist richtig und muss nur aufgeschrieben werden — an **zwei**
Stellen: `Bytecode.md:719` (was „numeric" ist) und im Guide (welche Ordnung). Dass `char` im
Bytecode als Integer läuft, ist auch die Grundlage für `c as int - '0' as int` in `std.string`
(`string.lyr:375-378`) und sollte bewusst so bleiben. Gleiche Einheit wie S01/S04: Skalarwert.

**Bruch:** nein. **Abhängig von:** S01, S02, S04, Gebiet Bytecode/VM (Spec-Text). **Belegtyp:**
gemessen; gelesen (disasm, Verifier).

---

### S41 — Was passiert mit eingebettetem NUL an den Grenzen?

**Heute:** im String ein Zeichen wie jedes andere: `"a\0b"` hat `length()==3`, 3 UTF-8-Bytes,
`indexOf("b")==2`, `println` schreibt es roh (`n07`, `b02`). An der Datei-Grenze:
`std.io.file.text("nul\0name.txt")` liefert `null` (`n21`) — ununterscheidbar von „Datei fehlt".
Prozessargumente und Umgebungsvariablen: nicht gemessen (in `std.process` gibt es `start(program,
args)`, `process.lyr:66`; **behauptet**, dass .NET dort `ArgumentException` wirft oder still am
NUL abschneidet — je nach API). Die FFI (`DotnetBinding.cs:152-190`) reicht den .NET-String mit
NUL durch.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **an jeder OS-Grenze ablehnen, mit eigener Fehlerart** | Pfad, Argument, Umgebungsvariable mit NUL → `IoError`/`Result.Err` mit Text „contains NUL" | Rust (`NulError` in `CString::new`, `std::fs` lehnt ab), Go (`os` lehnt ab, seit 1.x „invalid argument") | eine Prüfung pro Grenzfunktion (O(n) über den Pfad — vernachlässigbar); **kein** stilles `null` mehr |
| B **still abschneiden** | C-ABI-Verhalten | C | Pfad `"a\0b"` öffnet `"a"` — eine Sicherheitsfrage (Pfad-Truncation ist eine bekannte Angriffsklasse) |
| C **NUL im String verbieten** | Lexer und `as char` lehnen U+0000 ab | — | bricht `"\0"`, das die Grammatik ausdrücklich hat (`Grammar.md:107`); Binärdaten gehören ohnehin in `uint8[]` |
| D **wie heute** | `null` ohne Grund | — | ununterscheidbar von „fehlt" |

**Empfehlung: A.** Die Sprache erlaubt NUL im String (richtig — es ist ein Code-Punkt), also muss
die Grenze es benennen, nicht verschlucken. Die Spec bekommt eine Zeile für `std.io`/`std.process`/
`std.os`: „ein Pfad, ein Argument, ein Name oder ein Wert einer Umgebungsvariablen mit U+0000 wird
abgelehnt". Das ist dieselbe Klasse wie die strikte `utf8Decode`-Antwort (S24): lieber ein Fehler
als ein stilles anderes Ergebnis. Die Messung für Prozess und Umgebung gehört nachgeholt, bevor
die Zeile geschrieben wird.

**Bruch:** nein (heute `null` → künftig Fehler mit Grund; ein Programm, das auf `null` prüft,
sieht weiter „fehlgeschlagen"). **Abhängig von:** S24, Gebiet IO/Prozess. **Belegtyp:** gemessen
(String, Datei); behauptet (Prozess, Umgebung).

---

### S42 — Bleibt Backslash + Zeilenumbruch in einem String ein Lexerfehler?

**Heute:** ja, aber ein schlecht benannter: `"abc\` + LF + `def"` → `LEX0009 unterminated string
literal` plus **7** Folgefehler (Kritikprobe `b10`, auf diesem Checkout nachgelaufen). Quelle
(`Lexer.cs:604-607`, gelesen): `ConsumeEscapeSequence` kehrt bei `\n` **ohne** `LEX0007` zurück,
der String-Scanner meldet dann am Zeilenende „unterminated". Ein `\` vor `\r\n` (CRLF-Datei)
läuft in den `default`-Zweig (`\r` ist kein bekanntes Escape) und erzeugt zusätzlich `LEX0007`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Fehler bleibt, wird benannt** | `LEX0007: '\' before a line break is not an escape; use """ for multi-line text` — und der Rest der Zeile wird verschluckt (keine Lawine) | C# (kein `\`+Newline in `"…"`) | eine Diagnose; keine Semantik |
| B **Zeilenfortsetzung, frisst Einzug der Folgezeile** | `"abc\` LF `    def"` → `"abcdef"` | Rust, C (ohne Einzug-Fressen) | zweiter Weg zu mehrzeiligem Text neben `"""` (S13) → Rule 2; und in einer CRLF-Datei muss `\`+`\r\n` dasselbe tun wie `\`+`\n` |
| C **Zeilenfortsetzung, Umbruch bleibt im Wert** | `\`+LF = LF | — | eine dritte Bedeutung von `\n` (Escape, echter Umbruch, Fortsetzung) — Verwirrung ohne Gewinn |
| D **im `"""`-Block als Fortsetzung, im Einzeiler Fehler** | Rusts Regel nur dort, wo Mehrzeiligkeit ohnehin gilt | Swift (`\` am Zeilenende im `"""`-Block unterdrückt den Umbruch) | zwei Regeln für ein Zeichen — aber Swift zeigt, dass es trägt, weil der Block der einzige Ort ist, an dem „Umbruch unterdrücken" Sinn hat |

**Empfehlung: A für 5.0, D als Teil von S13.** Im Einzeiler ist die Fortsetzung überflüssig, sobald
`"""` existiert (S13-A), und ein zweiter Weg wäre ein Rule-2-Fall. Im `"""`-Block ist Swifts
Zeilenende-`\` das eine Mittel, einen langen Absatz ohne Umbruch im Wert zu schreiben — das gehört
in S13 mitentschieden, nicht hierher. Die Diagnose (A) ist sofort in 4.x fällig und ist derselbe
Fix wie die Lawinen-Unterdrückung aus Befund k. Und: die CRLF-Regel aus S25 gilt für den Block
auch hier.

**Bruch:** nein. **Abhängig von:** S13, S14, S25. **Belegtyp:** gemessen (`b10`); gelesen.

---

### S43 — Ist ein leerer Spec `{x:}` gültig?

**Heute:** ja, still — `f"[{s:}] [{n:}] [{f:}]"` → `[x] [5] [2.5]` (`n09`, `b04`), weil `Padded`
und `Formatted` `spec.Length == 0` als „kein Spec" behandeln (`NativeRegistry.cs:2795, 2826`).
Die Grammatik `Interpolation = '{' Expr [':' FormatSpec] '}'` erlaubt das nur, wenn `FormatSpec`
leer sein darf — und `FormatSpec` ist nicht produziert.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **zulassen, in der Grammatik** | `FormatSpec` darf leer sein (`{x:}` ≡ `{x}`) | Rust (`{:}` gültig), Python (`{:}` gültig) | eine Zeile Grammatik; nichts ändert sich |
| B **ablehnen** | Sema-Fehler „leerer Spezifizierer" | — | fängt `{x:}` als Tippfehler-Rest; minor Bruch für Programme, die es heute schreiben (vermutlich null) |
| C **schweigen** | Status quo | — | eine Form, die die Grammatik nicht kennt, ist gültig |

**Empfehlung: A.** Beide Vorbilder erlauben es, es ist harmlos, und bei S08-A entsteht die Form
natürlich, wenn eine dynamische Breite leer bleibt. Die Grammatik schreibt `FormatSpec = [ Layout ]
[ Presentation ]`, beides optional — dann ist leer automatisch gültig.

**Bruch:** nein. **Abhängig von:** S05, S08. **Belegtyp:** gemessen.

---

### S44 — Welche Warnklasse bekommen `<`, `>`, `^`, Füllzeichen, `+` und `#`?

**Heute:** gültig, ohne Diagnose, und still falsch: `{n:>8}` → `>8`, `{n:*>8}` → `*>8`, `{n:+}` →
`+`, `{n:^8}` → `^8` (`n02`, `c03`), `{255:#x}` → `255x` (`n20`). In 5.0 sind genau diese Formen
gültig **mit der richtigen Bedeutung**. Die S05-Tabelle der zweiten Fassung hatte sie nicht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Klasse 4: Warnung ab 4.7 mit heutigem UND künftigem Ergebnis** | „`{n:>8}` gibt heute `>8` aus; ab 5.0 `       5`" | — | eine Warnung, die einen heutigen Fehler meldet — kein Programm, das sie sieht, wollte `>8` |
| B **Fehler ab 4.7** | die Form ist heute sinnlos, also sofort ablehnen | — | ein Programm mit eingefrorenem Golden-Test bricht in 4.7 statt 5.0 — aber es war schon vorher kaputt |
| C **keine Warnung** | Klasse „gleich" | — | ein Golden-Test, der `>8` erwartet, bricht in 5.0 still |
| D **`lyric fix` schreibt NICHT um, markiert** | S27-D | `go fix` | richtig, weil das Werkzeug nicht weiß, ob der Müll erwartet wurde |

**Empfehlung: A + D, und B ernsthaft erwägen.** A ist die wertvollste Warnung des ganzen
Ratterplans: sie meldet einen Fehler, den das Programm **heute** hat. B ist vertretbar, weil kein
richtiges Programm diese Ausgabe wollen kann — der einzige Verlierer ist ein Golden-Test, der
einen Fehler eingefroren hat, und der ist ohnehin kaputt. Entscheidend ist, dass die Klasse
**existiert**: ohne sie würde 5.0 an genau der Stelle still, an der es Programme repariert, und
ein Test-Diff ohne Warnung davor sieht aus wie eine Regression.

**Bruch:** A nein (Warnung); B minor. **Abhängig von:** S05, S07, S27, S32. **Belegtyp:** gemessen.

---

### S45 — Wie interagiert die Compile-Zeit-Spec-Prüfung mit `comptime`?

**Heute:** `comptime f"{3.14159:.2}"` ergibt zur Compile-Zeit `32` (`n10`) — `comptime` läuft
über die VM-Sandbox (`VmComptimeRunner.cs`, gelesen: Datei existiert; `a20` misst das Verhalten),
also über **denselben** Formatierer wie die Laufzeit. Ein f-String ohne `comptime` wird **nicht**
gefaltet, auch nicht bei konstanten Löchern (Linksfaltung aus `concat`, S22).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **`comptime` bleibt der VM-Umweg; der Compiler faltet f-Strings nie selbst** | die Sema **prüft** den Spec (S06), **rendert** aber nicht | Zig (comptime-Interpreter ist der einzige Evaluator) | Compiler braucht keinen Formatierer, nur den Parser — und mit S36-C nicht einmal den, weil er `FormatSpec.parse` über die Sandbox ruft; `comptime f"…"` und Laufzeit stimmen per Konstruktion überein |
| B **Konstantenfaltung von f-Strings im Compiler** | `f"{1+1}"` wird zu `"2"` ohne `comptime` | Roslyn (faltet `$"…"` mit konstanten Löchern zu einer Konstante seit C# 10), rustc (`concat!`) | Formatierer **zweimal** (Compiler in C#, Runtime in C#, später Lyricpp in C++) — genau die Divergenzfalle; kleiner Gewinn |
| C **Faltung, aber über die Sandbox** | der Compiler führt jeden konstant-lochigen f-String durch die VM | — | Compile-Zeit steigt; ein Mechanismus (Sandbox), aber implizit statt per `comptime` — Rule-2-verdächtig, weil `comptime` genau diesen Schalter ist |

**Empfehlung: A.** Es gibt bereits ein Wort dafür, dass ein Ausdruck zur Compile-Zeit läuft, und
es heißt `comptime`; eine stille zweite Faltung wäre ein zweiter Mechanismus. Was A verlangt:
S06 prüft den Spec **ohne** zu rendern, und die Prüfung benutzt denselben Parser, den das Runtime
benutzt (S36-C: `FormatSpec.parse` in Lyric, über die Sandbox aufgerufen — so, wie `comptime`
heute jede Konstante auswertet). Damit ist die Zahl der Spec-Parser in der Toolchain **eins**.

**Bruch:** nein. **Abhängig von:** S06, S22, S36, Gebiet Meta/`comptime`. **Belegtyp:** gemessen
(`n10`); gelesen.

---

### S46 — Was tun `lyrfmt` und der Language Server mit einem Loch und einem Spec?

**Heute:** `lyrfmt --stdin` lässt `f"{ x : 8 }|{y:F2}|{x}"` **zeichengleich** stehen (`n15_fmt`)
— der Formatter behandelt das Literal als Lexem. Der Language Server tokenisiert nicht ins Literal
hinein (`SemanticTokensProvider.cs:15`, gelesen: „literals and comments stay with the grammar"),
Löcher und Specs sind für ihn Teil eines String-Tokens; die TextMate-Grammatik der Clients
(`vscode-lyric`, `jetbrains-lyric`, externe Repos) ist nicht gelesen. Diagnosen zu Specs stehen
heute am **Loch** (`n11`: Spanne `{p:N2}`), nicht an der Spec-Spalte.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **Formatter: Literal bleibt Lexem; LSP: Loch-Ausdruck als Semantic Tokens, Spec als eigener Token-Typ** | `{ x : 8 }` bleibt so, wie es steht; der Editor färbt `x` als Variable und `8` als Spec | rust-analyzer (färbt `{}`-Löcher und Specs), Pylance (f-String-Löcher als Ausdrücke) | LSP braucht den Spec-Parser (S36-C liefert ihn); Hover „rechtsbündig, Breite 8" wird möglich |
| B **Formatter normalisiert Löcher** | `{ x : 8 }` → `{x:8}` | — | der Formatter fasst Literale an — bricht die Zusicherung, die S13-A braucht (`"""`-Block unverändert) |
| C **nichts** | Status quo | — | Spec-Fehler bleiben am Loch, ohne Spalte; kein Hover |

**Empfehlung: A.** Der Formatter darf ein Literal **nie** verändern (auch nicht seine Löcher) —
sonst gibt es keinen Ort im Quelltext mehr, der garantiert byteweise bleibt. Der Language Server
soll dagegen sehen, was der Compiler sieht: mit S06 hat die Sema die Spec-Spanne, also gehört die
Diagnose an die Spec-Spalte (Span des Specs statt des Lochs), und Semantic Tokens für den Loch-
Ausdruck sind eine Folge davon, dass der Lexer den Ausdruck ohnehin lexiert. Zu klären mit dem
Editor-Gebiet: welche Token-Typen (`formatSpecifier` ist kein LSP-Standardtyp — rust-analyzer
nutzt `formatSpecifier` als Custom-Typ, behauptet).

**Bruch:** nein. **Abhängig von:** S06, S36, Gebiet Editor/LSP, Formatter-Vertrag (S13).
**Belegtyp:** gemessen (`lyrfmt`); gelesen (LSP).

---

### S47 — Welcher Diagnose-Code trägt Spec-Fehler zur Compile-Zeit?

**Heute:** `LYR-SEM0006` — und der Code ist **dreifach belegt** (gelesen,
`TypeChecker.cs:1776, 2982, 3008, 3016`): (1) „cannot cast X to Y — a conversion comes from
'Into'" (`as`), (2) „X does not render in an f-string", (3) „a format specifier ':N2' does not
apply to X". Gemessen (`n11`, `c02`): (2) und (3). Zur Laufzeit: `LYR-VM0006` (Index-Code) für
ungültige Specs (`n18`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **ungültige Specs unter SEM0006** | der f-String-Code wächst um „invalid specifier" | — | ein Code für Cast, Renderbarkeit und Spec-Syntax — drei Fehlerklassen, ein Etikett |
| B **eigener Sema-Code `SEM00xx: invalid format specifier`** | mit Spalte der Spec, Regel im Text, Ersatzvorschlag | rustc (`E0277`-Familie ist getrennt von Format-Fehlern: `format argument must be a string literal`, `invalid format string`) | eine Nummer mehr im Register; der Code wird von `lyric fix` (S27) als Anker benutzt |
| C **eigener VM-Code für den Laufzeitpfad** | `VM00xx` statt VM0006 | — | **bleibt nötig** (S36-A/C: `formatX` zur Laufzeit) — die zweite Fassung nannte ihn „überflüssig nach A", das ist zurückgenommen |
| D **SEM0006 aufteilen** | Cast bekommt eigenen Code, f-String behält 0006 | — | ein Bruch für Werkzeuge, die auf 0006 filtern; sauberer |

**Empfehlung: B + C, D prüfen.** Eine Warnstufe (S05) braucht einen Code, den ein Werkzeug
eindeutig zuordnen kann; „SEM0006" trifft heute schon zwei Bedeutungen, mit Specs wären es drei.
C ist nicht überflüssig: solange `formatFloat(x, userSpec)` existiert (S36), gibt es einen
Laufzeitfehler, und er darf nicht „index out of range" heißen. Unter S36-C wird C zum
`Result.Err` von `FormatSpec.parse` — dann ist der VM-Code nur noch für den Fall nötig, dass ein
Native einen unmöglichen Struct sieht.

**Bruch:** nein (neue Codes; Diagnose-Register ist additiv). **Abhängig von:** S05, S06, S27,
S36, Gebiet Diagnostik. **Belegtyp:** gemessen; gelesen.

---

### S48 — Wie verhält sich die Equatable/Hashable-Konsistenz auf `string` zu kanonischer Äquivalenz?

**Heute:** `==` ordinal über Code-Punkte; `"\u{E9}" == "e\u{301}"` ist `false`, Längen 1 und 2,
`<` ebenfalls `false` (`n12`, `b07`). `hash` ist FNV über Code-Punkte (`n22`). Damit gilt
`a == b ⇒ a.hash() == b.hash()` heute trivial. `core.lyr:268-270` sagt „no locale and no
normalization" — im Quelltext; **im Guide steht es nicht** (gelesen: kein Treffer für
„normaliz"/„canonical" außer 13:577 zu UTF-8-Kanonizität).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A **ordinal bleibt, im Guide als Zusicherung** | „zwei Strings sind gleich, wenn ihre Code-Punkt-Folgen gleich sind; `é` und `e`+U+0301 sind verschieden; `std.unicode.nfc(s)` ist der Weg, sie gleichzumachen" | Rust (`str == str` ist Byte-Gleichheit), Go, Python (Code-Punkt-Gleichheit) | eine Zusicherung, die man nie mehr ändern kann; `std.unicode` muss die Normalisierung liefern |
| B **kanonische Äquivalenz in `==`** | wie Swift | Swift | **`hash` muss NFC-normalisieren**, sonst bricht `a == b ⇒ hash(a) == hash(b)` und jede `Map<string, V>` — der Preis, den die zweite Fassung bei S04-D nicht nannte; dazu die Tabellen im Runtime |
| C **beides, getrennt** | `==` ordinal; `s.canonicallyEquals(t)` in `std.unicode` | Rust (`unicode-normalization` extern) | zwei Gleichheitsbegriffe, aber nur einer ist Operator und Hashable-Basis — kein Rule-2-Fall, weil der zweite eine Funktion ist |

**Empfehlung: A + C.** Die Konsistenz `==`/`hash` ist die Eigenschaft, an der jede Map hängt; sie
ist nur mit ordinal billig und tabellenfrei. Der Guide muss sie **als Zusicherung** hinschreiben
(nicht als Beobachtung), und die Spec-Zeile aus S37 nennt dieselbe Einheit. B ist damit endgültig
abgelehnt, mit dem vollständigen Preis.

**Bruch:** nein. **Abhängig von:** S04, S16, S37. **Belegtyp:** gemessen.
