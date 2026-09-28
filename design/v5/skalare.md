# Lyric 5 — Gebiet: Skalare Typen, Literale, Konversion

Stand 2026-09-27, **Fassung 3** (nach zweiter adversarischer Kritik überarbeitet; siehe §6).
Gemessen gegen `lyrc.dll`/`lyrvm.dll` aus `src/Lyrc/bin/Debug/net10.0` bzw.
`src/Lyrvm/bin/Debug/net10.0` (4.6.0-Stand des Checkouts), Spec-Clone
`C:/Users/Olivier/CLionProjects/lyric-spec`.
Proben: `…/scratchpad/v5-design/probes/skalare/` (Runde 1), `…/probes/skalare2/` (Runde 2,
`w01`–`w35`), `…/probes/skalare-review/` (Proben des Kritikers, `p01`–`p71`, von mir nicht
verändert) und `…/probes/skalare3/` (Runde 3, `x01`–`x20`, `y00`–`y02`; Erwartungen vorab in
`probes/skalare3/ERWARTUNG.txt`, **alle 38 eingetroffen**).

**Zur Belegform.** Diese Fassung zitiert **Anker** — Abschnittsnummern (`spec §3.1`), Symbolnamen
(`TypeChecker.CheckCoalesce`) oder Zeilenzitate — und nennt die Zeilennummer nur als Fundhilfe.
Wo ich eine Zeile am 2026-09-27 nachgeschlagen habe, steht **[geprüft]**; ältere Fundhilfen
tragen ihr Datum. Fassung 2 nannte für Suffixe, Präfixe und Trennzeichen dreimal „§1.7" — das ist
der Abschnitt *Interpolated strings*; richtig sind **§1.3** (Integer literals) und **§1.4**
(Floating-point literals) [geprüft: `spec/01-lexical.md`, Überschriften `:52`, `:67`, `:91`].
Jede Behauptung ist als **gemessen**, **gelesen** oder **behauptet** gekennzeichnet.

---

## 1. Ist-Stand

### 1.1 Was fest und richtig ist

Gemessen und mit der Spec übereinstimmend. Das ist der Boden; nichts davon schlage ich zum
Ändern vor. *(Zwei Zeilen aus Fassung 2 sind hier herausgefallen, weil sie nicht stimmten:
„char-Cast außerhalb des Skalarbereichs paniert" gilt nur für `int → char`, siehe (u); und der
`>>`-Beleg `248u8 >> 1` unterschied nichts, siehe unten.)*

| Regel | Beleg Spec | Gemessen |
|---|---|---|
| Wrapping-Arithmetik, 64 bit | §3.2 | `a_wrap`: `intMax+1` → `int`-Minimum |
| Wrapping **auf der Operandenbreite** | §3.2 | `a_wrap`, `w24`: `127i8+1i8` → `-128`, `200u8+100u8` → `44`; `x18`: `x++` auf `127i8` → `-128` |
| `min / -1` wrappt, `min % -1` ist 0 | §3.2 | `a_wrap`; `x12c`: `intMin / -1` → `intMin` |
| Negation von `min` ist `min` | §3.2 („negation and `absInt` return it unchanged") | `w33`: `-(-128i8)` = `-128` |
| Division/Rest trunkieren Richtung null | §3.2 | `a_wrap`: `-7/2` = -3, `-7%2` = -1, `7%-2` = 1 |
| Ganzzahl-Division durch 0 = Panik | §3.2 | `c5`, `x12a`: `LYR-VM0002` |
| Float-Division durch 0 = IEEE | §6.1 („float per IEEE 754") | `c_as`, `w22`, `x14`: `Infinity`, `-Infinity`, `NaN` |
| Shift-Count maskiert auf Operandenbreite | §6.1 | `f_shift`: `1<<64`=1, `1<<65`=2, `1<<-1`=`min`, `1i8<<8`=1, `1i16<<16`=1 |
| `>>` arithmetisch signed, logisch unsigned | §6.1 | **`x05`** (Korrektur: `248u8>>1` = 124 in Fassung 2 unterschied nichts, weil ein `u8` nullerweitert wird und beide Shifts 124 geben): `uintMax >> 63` = **1**, `uintMax >> 1` = `9223372036854775807`; Kontrolle `-1 >> 63` = `-1`, `-1 >> 1` = `-1`. Quelle: `Interpreter.cs`, Zweig `case Op.Shr`: `signed ? … AsI64 >> … : lhs.Bits >> …` [geprüft] |
| float→int sättigt, NaN→0 (jede Breite) | §3.6 Punkt 1 | `c_as`: `1e300 as int` = `intMax`, `nan as int` = 0; **`x17`**: `nan as int8` = 0, `1e300 as int8` = 127, `-1e300 as uint8` = 0 |
| int→int narrowing behält die tiefen Bits | §3.6 Punkt 1 | `c_as`, `w23`, `x07`: `300 as int8` = 44, `200u8 as int8` = -56, `-1 as uint` = `uintMax` |
| **int→char** außerhalb des Skalarbereichs paniert | §3.6 Punkt 1 | `c1/c2`, **`x02c`**: `1114112 as char` → `LYR-VM0012` „not a Unicode codepoint (valid: 0..0x10FFFF, excluding the surrogate range)"; Kontrolle `x02b`: `65 as char` → `A` |
| float32-Arithmetik läuft echt in binary32 (im .NET-VM) | — | `h_f32`: `0.1f32+0.2f32` = 0.30000001192…, `1f32/3f32` = 0.33333334… |
| `as` auf bool/string ist ein Fehler | §3.6 Schlusssatz | `c3/c4/c7`: `LYR-SEM0006` mit `Into`-Hinweis |
| Keine impliziten Konversionen, `int` ≠ `int64` | §3.1, §3.7 | `g2`: `cannot assign 'int64' to 'int'`; `g3`: `int == uint` ist `SEM0003`; `g5`: `float + int` ist `SEM0003` |
| Selbst-Cast (`x as int` auf einem `int`) ist zulässig | — | `w28` |
| Trennzeichen, Präfixe, Exponent | **§1.3, §1.4** | `g7`: `1_000_000`, `0xFF_FF`, `0b1010_1010`, `0o7_7`, `1_0e1_0`; `m4`: `1.0_5` = 1.05 |
| Ein String ist **nicht** indizierbar — **in der Spec entschieden** | **§3.3**: „`string` is NOT indexable, by decision: code-point access is O(n), and an index operator would hide a quadratic loop" [geprüft, `spec/03-types.md:73-75`] | `w21`: `LYR-SEM0007` mit derselben Begründung |
| `for (c in s)` läuft über **Codepunkte** | §3.1 („an immutable sequence of code points") | `w21`: `"e\u{301}x"` → 3 Iterationen, `"\u{1F600}a"` → 2 |
| `f"{x:D4}"` formatiert jede Breite | §6.6 | `w29`: `int8`, `int64`, `uint` → `0005`, `0007`, `0009` |
| Ein unsuffigiertes Literal adaptiert an `uint` | §3.1 | **`x01`**: `let u: uint = 5;` und `let big: uint = 18446744073709551615;` drucken `5` und `18446744073709551615` |
| Range-Iteration bis zum Typmaximum terminiert | — | **`x15`**: `for (i in 0u8..=255u8)` zählt `256`; `p18`: `0i8..=127i8` → 128 |
| Ein unannotiertes Literal über `int`-Maximum ist ein Fehler **mit** Hinweis | §3.1 („a magnitude beyond `int`'s range is an error there — not a bit reinterpretation") | **`x04a`/`x04b`**: `LYR-SEM0001: integer literal does not fit 'int' — annotate the uint type that holds it`; Quelle `WarningAnalyzer.CheckLiteralInRange` [geprüft, `:631-638`] |

Die Shift-Maskierung pro Breite, das Wrapping pro Breite und die Weigerung, einen String zu
indizieren, sind die drei Stellen, an denen die meisten Hobby-Sprachen die billige Antwort
nehmen. Hier nicht. **Und die dritte davon steht — anders als Fassung 2 behauptete — in der
Spezifikation, nicht nur im Compiler.**

### 1.2 Wo Spec und Compiler auseinandergehen

**(a) `char` ist im Compiler eine Ganzzahl — bewusst, kommentiert, und in der Spec nur halb.**
`TypeFacts.IsInteger` listet `PrimitiveKind.Char` auf (`src/Lyric.Frontend/Sema/TypeFacts.cs:21`,
[geprüft]). *Korrektur gegenüber Fassung 2*, die schrieb „eine Lücke, die nie jemand entschieden
hat" und „Spec schweigt": **beides ist zu stark.** Der Doc-Kommentar direkt über der Zeile
(`TypeFacts.cs:9-19`, [geprüft]) dokumentiert die Entscheidung samt Grund:

> „A `char` is a Unicode code point and therefore a number; it counts as numeric, or
> `std.string` would have to descend into the host for 'is this a digit?'. The price is paid
> in the VM: every operation that PRODUCES a `char` checks the value range."

und benennt den Spiegel `IrVerifier.IsInteger` (`IrVerifier.cs:1748-1756`, [geprüft]: „'Char' is
included; missing here, the verifier would reject what the sema allows"). Und die Spec nennt
`char` in §3.6 Punkt 1 ausdrücklich als Cast-Partner („and `char`, which converts as its code
point"). Was die Spec **nicht** sagt: dass `char` ein Operand von `+ - * / % & | ^ ~ << >>` ist.
§3.1 führt ihn als eigene Zeile („one Unicode scalar value"), §6.2 nennt ihn nur bei
Vergleichen. **Der richtige Satz lautet also**: eine bewusste, begründete Compiler-Entscheidung,
die die Spec nicht übernommen hat. Gemessen (`d_char`, `d_and`, `d_shl`, `d_not`):

| Ausdruck | Ergebnis | Typ |
|---|---|---|
| `'a' + 1` | `'b'` | `char` |
| `'z' - 'a'` | Codepunkt 25 | `char` (nicht `int`!) |
| `'a' * 2` | 194 | `char` |
| `'a' / 2`, `'a' % 7` | 48, 6 | `char` |
| `'a' & 1`, `'a' << 1` | 1, 194 | `char` |
| `~'a'` | **Panik** `LYR-VM0012` (-98) | compiliert, paniert immer |
| `'\u{10FFFF}' + 1` | **Panik** `LYR-VM0012` | jede char-Arithmetik trägt eine Laufzeitprüfung |
| `c++` auf `char` | `LYR-IR0001: increment/decrement on a non-numeric type` | Sema sagt numerisch, Lowering sagt nicht |

Drei Mechanismen, zwei Antworten: Sema und Verifier sagen ja (mit Kommentar), Lowering sagt bei
`++` nein, Spec sagt „Codepunkt beim Cast" und sonst nichts. **Der Grund im Kommentar bindet
heute nicht mehr**: `stdlib/std/string.lyr` castet durchgehend `c as int` (`:412,447,455,590-594`,
[geprüft 2026-09-24]) und rechnet an keiner Stelle roh auf `char`; der Kommentar `:378` („That
works because a `char` is a number") beschreibt Code, der längst umgeschrieben ist. Eine Suche
über die gesamte `stdlib/` nach roher char-Arithmetik findet keinen Treffer — jeder Fund ist ein
Vergleich, und Vergleiche bleiben in jeder Option erlaubt (§6.2). → SK-04.

**(b) Ein Ganzzahlliteral adaptiert an `char` — nirgends aufgeschrieben.**
`d_litchar`: `let c: char = 65;` → `A`. Der Bereich wird geprüft (`d_litchar2`: `1114112` →
`SEM0001`). Implementiert und kommentiert in `TypeFacts.IntLiteralFits`, Zweig
`PrimitiveKind.Char` (`TypeFacts.cs:59-66`, [geprüft]: „'c + 1' and 'let c: char = 65' … a
literal is thereby rejected AT COMPILE TIME where the runtime would otherwise have to panic").
Die Spec (§3.1) nennt als Ziele nur „integer target" und „FLOAT target". → SK-05.

**(c) Die Literal-Adaption bei `??` erzeugt für die meisten Typen fehltypisierten IR.**

| Probe | Quelle | Ergebnis |
|---|---|---|
| `w01` | `let o: ?uint = null; let v: uint = o ?? 7;` | `LYR-CLI0020` — `store of t4 (i64) into l2 (u64)` |
| `w02` | `let o: ?uint64 = null; let v: uint64 = o ?? 7;` | `LYR-CLI0020` — dasselbe |
| `i5`/`k1` (R1) | `?int8`, `?int32`, `?uint8`, `?float32` | `LYR-CLI0020`, je in die Zielbreite |
| `w03` **Kontrolle** | `?float64` mit `o ?? 1.5` | **läuft**, druckt `1.5` |
| `w04` **Kontrolle** | `?int` mit `o ?? 7` | **läuft**, druckt `7` |
| `i5a` (R1) **Kontrolle** | `o ?? 7i8` (Suffix statt Adaption) | **läuft** |

**Die Regel**: es kracht für **jeden** Typ außer `int`, `int64`, `float`, `float64` — `uint` und
`uint64` eingeschlossen, obwohl sie 64 bit breit sind. Der Konstanttyp der Lowering ist `i64`
bzw. `f64` und fällt nur bei diesen vier mit dem Slottyp zusammen.

Ursache: `TypeChecker.CheckCoalesce` (`TypeChecker.cs:2768-2796`, [geprüft 2026-09-24]) ruft
`AdaptEmptyArray` und `IsAssignable`, **nie** `AdaptLiteralType` — anders als der Zuweisungspfad.
Der Doc-Kommentar an `AdaptLiteralType` (`TypeChecker.cs:5946-5951`) beschreibt exakt diesen
Fehler: „the lowering then produces a `const i64` and pushes it into an i8 slot".

**Warum es niemand gemerkt hat**: der Konformanzfall
`lyric-spec/conformance/cases/03-types/literal_adaptation_contexts.lyr` prüft alle sechs Kontexte
— **alle gegen `int64`** [gelesen 2026-09-24]. Der Fall ist grün aus dem falschen Grund.
**Folge für den Sweep-Posten**: der Zwilling **muss `uint` enthalten**, nicht nur `int8`.

**(c2) Dieselbe Fehlerfamilie beim Indizieren.**

| Probe | Quelle | Ergebnis |
|---|---|---|
| `w07` | `let j: int8 = 1i8; xs[j]` | `LYR-CLI0020` — `loadelem index t6 is i8, expected i64` |
| `w08` | `let u: uint = 1 as uint; xs[u]` | `LYR-CLI0020` — `loadelem index t7 is u64, expected i64` |
| `w09` **Kontrolle** | `let i: int = 1; xs[i]` | **läuft**, druckt `2` |

→ **SK-16**.

**(c3) Dieselbe Fehlerfamilie bei `comptime` auf Breitentypen.**

| Probe | Quelle | Ergebnis |
|---|---|---|
| `w32` | `comptime (127i8 + 1i8)` | `LYR-CLI0020` — `integer const 18446744073709551488 does not fit the bit pattern of i8` |
| `w32` | `comptime (100i8 * 100i8)` | dasselbe |
| `w34` **Kontrolle** | `comptime (100i8 + 1i8)` (kein Überlauf) | **läuft**, druckt `101` |
| `w35` **Kontrolle** | `comptime (intMax + 1)` (64 bit) | **läuft**, druckt `int`-Minimum |

Der Falter rechnet **semantisch richtig** (mit `LYRIC_VERIFY_IR=0` druckt `w32` `-128` und `16`),
kodiert das Ergebnis aber als 64-bit-Konstante. → **SK-26**.

**(c4) Wie schlimm ist die Familie? Gemessen.** Der Schalter ist dokumentiert:
`Lyric.Core.Pipeline.VerifiesIr` (`src/Lyric.Core/Phase.cs:52-70`, [geprüft 2026-09-24]) — „A
debug build says yes, a release build says no, and `LYRIC_VERIFY_IR` overrides both." Mit
`LYRIC_VERIFY_IR=0` **kompilieren `w01`, `w02`, `w07`, `w08`, `w32` klaglos und rechnen richtig**
(`7`, `7`, `2`, `2`, `-128`). Der Befund ist also kein Compilerabsturz für Benutzer eines
Release-`lyrc`, sondern **still falsch typisiertes Bytecode**, das die eigene Formatinvariante
verletzt. Ein zweites Laufzeitsystem mit typisierten Slots (Lyricpp/Erato 2) würde es ablehnen
oder falsch ausführen. `STATUS.md:2183-2186` [geprüft 2026-09-24] sagt dasselbe über die
Leser-Seite: „The bytecode reader still does not type what the verifier types".

**(c5) Vierter Fall derselben Familie — und eine Sema-Lücke: suffigierte Literale werden nicht
bereichsgeprüft.** *Neu in dieser Fassung; die Kritik hat es gefunden, ich habe es nachgemessen.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x03a`** | `let x = 200i8;` | kompiliert **mit** Verifier, druckt **`-56`** |
| **`x03c`** | `let x = 0x80i8;` | kompiliert, druckt **`-128`** |
| **`x03d`** | `let x = 16777217f32;` (2²⁴+1) | kompiliert, druckt **`16777216`** (still gerundet) |
| **`x03b`** | `let x = 300i8;` | `LYR-CLI0020 — integer const 300 does not fit the bit pattern of i8 — this is a defect in the compiler` |
| `p33`, `p69` (Kritiker) | `256u8`, `65536u16`, `99999999999i32` | `LYR-CLI0020`; mit `LYRIC_VERIFY_IR=0` still `0`, `0`, `1215752191` |
| **`x03e`** **Kontrolle** | `let g: float32 = 16777217;` (unsuffigiert, adaptiert) | `LYR-SEM0001` — der unsuffigierte Weg prüft |
| **`x03f`** **Kontrolle** | `let x = 100i8;` | `100` |

Ursache, gelesen: `TypeChecker.Compute` typisiert ein `IntLiteralExpr` mit Suffix **allein nach
dem Suffix** (`TypeChecker.cs:1748`: `il.Suffix is { } isx ? IntSuffixType(isx) : LyrType.Int`,
[geprüft]); `WarningAnalyzer.CheckLiteralInRange` steigt bei Suffix sofort aus (`:633`:
`if (lit.Suffix is not null) return;`, [geprüft]). Und die Spec **verlangt es auch nicht**: §1.3
sagt „The width suffixes are `i8 … u64`, legal on every integer form" und §3.1 „A suffixed
literal has exactly its suffix's type" — kein Wort über den Wert [geprüft]. Ob `200i8` ein
Bitmuster (heute: `-56`), ein Bereichsfehler (Rust: „literal out of range for `i8`") oder gar
nichts ist, ist damit **eine Designfrage** → **SK-28**, und die Verifier-Meldung „defect in the
compiler" für `300i8` ist ein Sweep-Posten unabhängig davon.

**(d) Float-Literale werden still gerundet, unsuffigierte Ganzzahlliterale nicht.**
§3.1 fordert für ein Float-Ziel „exactly representable". Der Code prüft das nur für
**unsuffigierte** Ganzzahlliterale (`TypeChecker.LiteralAdaptsTo` → `TypeFacts.IntLiteralExactInFloat`);
für ein Float-Literal gibt `LiteralAdaptsTo` **unbedingt** `true` zurück (`TypeChecker.cs:6090`,
[geprüft 2026-09-24]), für ein suffigiertes Literal gibt es gar keine Prüfung (c5).

| Probe | Gemessen |
|---|---|
| `e3`, `x03e` `let g: float32 = 16777217;` | Fehler `SEM0001` |
| `x03d` `let x = 16777217f32;` | kompiliert → `16777216` |
| `e4` `let g: float32 = 0.1;` | kompiliert → `0.10000000149011612` |
| `e10` `let g: float32 = 3.14;` | kompiliert → `3.140000104904175` |

**(e) Überlauf eines Float-Literals wird nur bei `float` geprüft, nicht beim Zieltyp.**

| Probe | Gemessen |
|---|---|
| `m2` `let x: float = 1e400;` | `LYR-PAR0007: float literal too large` |
| `m1` `let x: float32 = 1e300;` | kompiliert → **`Infinity`** |
| `m3` `let x: float32 = 1e-60;` | kompiliert → **`0`** |

**(f) Tupel-Elemente sind kein Adaptionskontext.**
`i1`: `let t: (int8, int8) = (1, 2);` → `cannot assign '(int, int)' to '(int8, int8)'`.
Bereits als offener Faden notiert: `STATUS.md:2284-2289` [geprüft 2026-09-24]. → SK-07.

**(g) ~~Spec „distinkt", Format „Alias"~~ — zurückgezogen (seit Fassung 2).**
`docs/Bytecode.md:548-549` trennt Repräsentation und Typidentität ausdrücklich: „An opaque alias
is a distinct type in the language and its underlying type everywhere below the checker". Kein
Widerspruch.

**(u) `float → char` liefert für JEDEN Wert 0 — ein Bug, den Fassung 2 als „Boden" führte.**
*Neu; von der Kritik gefunden, nachgemessen.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x02a`** | `(97.0 as char) as int` | **`0`** |
| **`x02a`** | `(65f32 as char) as int` | **`0`** |
| **`x02a`** | `(1e300 as char) as int` | **`0`** (keine Sättigung, keine Panik) |
| **`x02a`** | `(97.5 as char) as int` | **`0`** |
| `p63b` (Kritiker) | `55296.0 as char` (Surrogat) | `0` — keine `VM0012` |
| **`x02b`** **Kontrolle** | `65 as char` | `A`, Codepunkt `65` |
| **`x02c`** **Kontrolle** | `1114112 as char` (int) | Panik `LYR-VM0012` |

Ursache, gelesen: `Interpreter.FloatToInt` (`src/Lyric.Vm/Interpreter.cs:1457-1485`, [geprüft])
behandelt `I64` und `U64` gesondert und hat für die übrigen eine Tabelle
`to switch { I8 …, U32 …, _ => (0.0, 0.0) }` — `TypeTag.Char` fällt auf `(0, 0)` und wird auf 0
geklemmt. §3.6 Punkt 1 verspricht für `char` drei Dinge zugleich, die sich bei einem Float-Quell
widersprechen: „converts as its code point", „SATURATES at the target's bounds" und „a value
outside the scalar range panics `LYR-VM0012`". Sättigen auf `0x10FFFF` und Paniken schließen
sich aus. **Sweep-Posten (der Bug) und Designfrage (welche der drei Zusagen gilt)** → **SK-29**.

**(v) Vorzeichenwechsel bei WACHSENDER Breite ist unspezifiziert und sign-extendet.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x07`** | `-1i8 as uint64` | **`18446744073709551615`** (sign-extend, dann reinterpretieren) |
| **`x07`** | `-1i8 as uint8` | `255` (Narrowing/gleiche Breite: tiefe Bits) |
| **`x07`** | `-1i8 as int64` | `-1` |
| **`x07`** | `200u8 as int64` | `200` (zero-extend) |
| **`x07`** | `200u8 as int8` | `-56` |

§3.6 Punkt 1 regelt nur das Narrowing („keeps the low bits"); die Weitung signed→unsigned steht
nirgends [geprüft: keine Fundstelle in §3.6]. Die VM macht es so, weil `LyrValue` jede Ganzzahl
auf 64 bit weitet — „signed types sign-extended, unsigned types zero-extended"
(`src/Lyric.Vm/LyrValue.cs:14-16`, [geprüft]) — und der Cast danach nur den Tag wechselt. Das ist
C/Rust-Semantik (`-1i8 as u64` = `u64::MAX` in Rust), aber **nicht aufgeschrieben** → **SK-30**.

**(w) Der Shift-Count muss den Typ des linken Operanden haben.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x08a`** | `let a: int8 = 1i8; let m: int = 3; a << m` | `LYR-SEM0003: operator 'Shl' is not applicable to 'int8' and 'int'` |
| `p10` (Kritiker) | `a << n` mit `n: int32` | `SEM0003` |
| **`x08b`** **Kontrolle** | `a << 3` (Literal adaptiert) | `8` |

§6.1 regelt die **Maskierung** des Counts („masked to the LEFT operand's width"), nicht seinen
**Typ** [geprüft]. Die Sema behandelt `<<` wie `+` („operands of the SAME numeric type"). Jede
Schleife `x << i` auf einem Breitentyp braucht heute `i as int8` → **SK-31**.

**(x) Die Argumentreihenfolge entscheidet, ob ein Literal adaptiert.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x09a`** | `fn f<T>(a: T, b: T): T` … `f(1i8, 2)` | **läuft**, `T = int8`, druckt `2` |
| **`x09b`** | dieselbe Funktion … `f(2, 1i8)` | `LYR-SEM0001: cannot assign 'int8' to 'int'` |
| **`x20`** **Kontrolle** | `f(1i8, 2i8)` und `f(2, 3)` | beide laufen |

Die Inferenz bindet `T` am ersten Argument: ist es das Literal, wird `T = int` und `1i8` passt
nicht mehr; ist es `1i8`, wird `T = int8` und `2` adaptiert. → **SK-32**; SK-23 sieht nur den
Rückfluss aus dem Bindungsziel.

**(y) Ganzzahl-Formatierung rendert das Bitmuster der geweiteten 64 bit.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x10`** | `f"{-1i8:X}"` | `FFFFFFFFFFFFFFFF` (16 Stellen, nicht `FF`) |
| **`x10`** | `f"{-1i32:X}"` | `FFFFFFFFFFFFFFFF` |
| **`x10`** | `f"{-1i8:B}"` | 64 Einsen |
| **`x10`** **Kontrolle** | `f"{255u8:X}"` | `FF` (unsigned nullerweitert, also unauffällig) |

Mechanismus: `FunctionLowerer.WidenForHelper` (`FunctionLowerer.cs:4909,4928,4932`, [geprüft])
weitet jeden Wert vor dem Aufruf des Format-Helfers auf 64 bit; der Helfer kennt die Breite
nicht. Dasselbe Leck wie (r) für `float32`, nur für Ganzzahlen → **SK-33**.

**(z) Eine konstante Division durch null kompiliert; `comptime` fängt sie.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x12a`** | `let x = 1 / 0;` | kompiliert, Panik `LYR-VM0002` zur Laufzeit |
| **`x12b`** | `let y = comptime (1 / 0);` | `LYR-CT0002: 'comptime' expression could not be evaluated: panicked [LYR-VM0002]` |
| **`x12c`** **Kontrolle** | `intMin / -1` | kompiliert, `intMin` (wrappt, wie §3.2 sagt) |

Der Falter faltet `intMax + 1` still (l), lässt `1 / 0` bis zur Laufzeit und meldet es nur unter
`comptime`. Drei Verhalten für drei konstante Ausdrücke → **SK-35**.

**(aa) `opaque type` über einem numerischen Underlying kennt keine Literal-Adaption.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x13a`** | `opaque type Id = uint8; let i: Id = 5;` | `LYR-SEM0001: cannot assign 'int' to 'Id'` |
| **`x13b`** | `let i: Id = 5 as Id;` | `LYR-SEM0006: cannot cast 'int' to 'Id' — a conversion comes from 'Into': give 'int' the conformance :: [Into<Id>]` |
| **`x13c`** **Kontrolle** | `let i: Id = (5 as uint8) as Id;` | läuft, `5` |

Beides im Deklarationsmodul (§3.5-Privileg). Der `Into`-Hinweis in `x13b` ist irreführend: der
Benutzer soll `int` eine Konformanz geben, wo ein Cast auf `uint8` genügt. → **SK-37**.

**(ab) `-0.0` ist ein eigener Wert mit `==`-Gleichheit.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x14`** | `let z = -0.0; f"{z}"` | `-0` |
| **`x14`** | `0.0 == z` | `true` |
| **`x14`** | `1.0 / z` | `-Infinity` |
| **`x14`** | `z < 0.0` | `false` |

Verschiedene Bitmuster, gleiche `==`-Antwort. Das trifft SK-20 (`total_cmp` ordnet `-0 < +0`)
und SK-21 (Bitmuster-Hash) → **SK-38**.

**(ac) Float-Literale sind Patterns.** *Neu.*

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x16a`** | `match (1.0) { 1.0 => 1, _ => 2 }` | `1` |
| **`x16a`** | `match (nan) { 1.0 => 1, _ => 2 }` | `2` (Default-Arm; NaN matcht kein Literal) |

Rust hat das 2018 als Deprecation angekündigt (`illegal_floating_point_literal_pattern`) und
2024 (Rust 1.83) zurückgenommen — **behauptet**, aus Erinnerung, nicht nachgeschlagen. → **SK-42**.

### 1.3 Wo Lyric 4 unvollständig ist

**(h) Elf von dreizehn numerischen Typen haben keinen `Display`.**
§3.1 listet als numerische Primitive: `int`, `uint`, `int8/16/32/64`, `uint8/16/32/64`, `float`,
`float32`, `float64` = **13** [geprüft]. `stdlib/std/core.lyr` erklärt die Konformanzen nur für
`int`, `uint`, `float`, `bool`, `char`, `string` — und nicht einmal die vollständig
[geprüft 2026-09-24]:

| Interface | Erklärt für (core.lyr-Zeile) | Fehlt |
|---|---|---|
| `Display` | `int`:106, `float`:112, `bool`:118, `char`:124, `string`:130 | **`uint`** und alle 10 Breitentypen |
| `Equatable<T>` | `int`:181, `char`:207, `bool`:231, `float`:243, `string`:261, `uint`:319 | alle 10 Breitentypen |
| `Ordered<T>` | `int`:187, `char`:213, `float`:249, `string`:267, `uint`:325 | `bool`, alle 10 Breitentypen |
| `Hashable<T>` | `int`:199, `char`:225, `bool`:237, `string`:300, `uint`:337 | **`float`**, alle 10 Breitentypen |
| `Add/Sub/Mul/Div` | `int`:382-400, `uint`:406-424, `float`:430-448 (+`string` Add:455) | alle 10 Breitentypen |

| Probe | Gemessen |
|---|---|
| `w05` `let u: uint = 5 as uint; println(u);` | `LYR-SEM0028: type 'uint' does not satisfy constraint 'Display' on 'T'` |
| `w06` **Kontrolle** | `f"{u}"` druckt `5`, `println(5)` druckt `5` |
| `j4` `println(5i8)`, `j7` `println(5i64)` | `SEM0028` |
| `w13`/`j5` `Map<float,int>` / `Map<int32,string>` | `SEM0028` (kein `Hashable`) |
| `j2` `biggest<int8>(…)` mit `T :: [Ordered<T>]` | `SEM0028` |
| `j6`/`w29` `f"{x}"`, `f"{x:D4}"` | **funktioniert für jede Breite und für `uint`** |

**(h2) Die Lücke ist für die `std.core`-Interfaces von außen nicht zu schließen — enger als
Fassung 2 sagte.** *Korrektur.* Fassung 2 schrieb „was `std.core` nicht erklärt, kann niemand
nachrüsten". Die Orphan-Regel ist enger: `LYR-SEM0041` sagt „neither 'int8' nor any implemented
interface is declared in this module" — ein Interface aus dem **eigenen** Modul reicht. Gemessen:

| Probe | Quelle | Ergebnis |
|---|---|---|
| **`x06`** | `interface Mine { fn twice(): int; } extend int8 :: [Mine] { … }` im Benutzermodul | **läuft**, `21i8.twice()` = `42` |
| **`x19`**/`w25` **Kontrolle** | `extend int8 :: [Display]` (Interface aus `std.core`) | `LYR-SEM0041` |

Der richtige Satz: **nur die `std.core`-Interfaces** (`Display`, `Ordered`, `Hashable`, `Add`…)
sind für die Breitentypen von außen unerreichbar — und genau die braucht `println`, `Map`,
`sort` und `+`. SK-10 bleibt damit keine Komfortfrage, aber der Umfang ist präzise: was fehlt,
kann nur `std.core` liefern; eigene Protokolle über `int8` sind heute schon möglich.

**(i) Keine Grenzwerte außer für `int`.** `stdlib/std/math.lyr` hat genau fünf `pub let`: `pi`,
`e`, `tau`, `intMax`, `intMin` (`:14,17,70,77,81`, [geprüft 2026-09-24]). Kein `uintMax`, kein
`int8Max`, kein `float32Epsilon`, kein `nan`/`infinity` (NaN nur über `0.0/0.0`, `w26`),
kein `bitWidth`.

**(j) Die Escape-Route ist `int`-only.** `checkedAdd/Sub/Mul/Abs` → `?int` (`math.lyr:97,107,117,131`)
und `saturatingAdd/Sub/Mul` (`:139,148,157`) — alle nur für `int`. `absInt` (`:85`) ebenso.

**(k) Kein `checked`-Konstrukt.** §3.2 kündigt es an („A future checked mode would be a new
construct"); `docs/Befunde_und_Verbesserungen/PLAN.md` führt es als Posten 20.

**(l) Konstanter Überlauf wird still gefaltet.** `g4`: `let x = 9223372036854775807 + 1;` →
`-9223372036854775808`. `w35`: `comptime (intMax + 1)` ebenso.

**(m) Kein Suffix für `uint`, `int`, `float` — aber ein `uint`-Literal gibt es sehr wohl.**
*Korrektur.* Fassung 2 schrieb in SK-27 „kein Literal erzeugt je ein `uint`". **Falsch**
(`x01`): `let u: uint = 5;` und `let big: uint = 18446744073709551615;` kompilieren und drucken
`5` bzw. `18446744073709551615` — Kontext-Adaption an `uint` funktioniert
(`TypeFacts.IntLiteralFits`, Zweig `Uint64 or Uint => !negative`, [geprüft `:57`]). Was fehlt,
ist die **Suffixform** (`w30`: `5u` → `LYR-LEX0003` *und* `LYR-PAR0006`, zwei Meldungen für
einen Tippfehler; Suffixe sind `i8…i64`/`u8…u64`/`f32`/`f64`, **§1.3/§1.4**) — sie fehlt genau
dort, wo kein Kontext steht: `let x = 0xFFFFFFFFFFFFFFFF;` ist `SEM0001` (`x04b`). → SK-14, SK-41.

**(n) Diagnostik nennt bei Zieltyp-Fehlern den Typ, nicht den Wert — mit einer Ausnahme.**
*Korrektur.* Fassung 2 schrieb „nirgends steht … das Wort 'passt nicht'". Es gibt **eine**
Meldung, die es tut: der unannotierte Überlauf (`x04a`/`x04b`: `integer literal does not fit
'int' — annotate the uint type that holds it`, `WarningAnalyzer.cs:637`). Alle **anderen**
Bereichsfehler sind `LYR-SEM0001: cannot assign 'int' to 'int8'` (`e1`, `e3`, `e5`, `e8`,
`i7`, `x03e`); bei `e2` (`let f: float = 9007199254740993;`) „cannot assign 'int' to 'float'"
— irreführend, denn Ganzzahlliterale adaptieren an `float`, nur dieses nicht; bei `i4`
(`x += 200` auf `int8`) `operator 'Add' is not applicable to 'int8' and 'int'`. → SK-09, das
den vorhandenen Text als Muster nehmen soll.

**(o) Kein Hex-Float-Literal.** `w31`: `0x1p-3` → `LYR-PAR0016` + `LYR-SEM0002: unknown
identifier 'p'`.

**(p) `bool` hat keine nicht-kurzschließenden Operatoren.** `d_boolop`: `t & f` → `SEM0003`.
`t != f` (`m5`) ist bereits xor.

**(q) Kein Bitmuster-Zugang, keine Bit-Werkzeuge.** Suche über `stdlib/` nach
`countOnes|leadingZeros|trailingZeros|rotateLeft|byteSwap|popCount|bitPattern|toBits|bitWidth`:
kein Treffer [geprüft 2026-09-24]. → SK-21, SK-22.

**(r) `float32` wird als `float64` gerendert.** `w11`: `0.1f32` → `0.10000000149011612`
(`x11` bestätigt); `w22`: `3.4e38f32` → `3.3999999521443642e+38`. Mechanismus:
`FunctionLowerer.ToStringValue` (`FunctionLowerer.cs:4927-4928`) mappt `F32 or F64` auf
`std.string.fromFloat` nach `WidenForHelper`. Für `float` ist die Ausgabe kürzest-round-trip
(`json.lyr:166`; `w20`). → SK-18.

**(s) Unäres `-` auf einem vorzeichenlosen Typ ist still.** `w10`: `-u` auf `3 as uint` →
`18446744073709551613`. §3.2 spricht nur über signed-Minimum, §6.1 nennt unäres `-` nicht
[geprüft]. → SK-17.

**(t) `Ordered<float>` und `Equatable<float>` widersprechen sich bei NaN.** `w12`:
`compare(nan,1.0)` = 0, `compare(nan,nan)` = 0, `equals(nan,nan)` = false. `core.lyr:249-259`.
→ SK-20.

**(ad) Das Format-Spezifikator-Vokabular ist nirgends normativ.** *Neu.* `stdlib/std/fmt.lyr:6-8`
[geprüft]: „The specifier language is .NET's: `N2` … `F3` … `D5` … `X` … `E2` … `P1`. It is
passed to the runtime unchanged; there is no second notation beside it." `spec/11-stdlib-contract.md`
nennt kein einziges Spezifikator-Wort [geprüft: Suche nach `specif|N2|format` findet nur die
Zeile 4 „specified by its own documentation and test suite"]. Auch die **Default**-Ausgabe ist
nirgends festgelegt; gemessen `x11`: `f"{1.0}"` → `1`, `f"{1e21}"` → `1e+21`, `f"{1e-7}"` →
`1e-07` — das ist .NETs „R"/„G17"-Kurzform mit zweistelligem Exponenten. → **SK-34**.

**(ae) Breitentypen packen im Referenz-VM nicht.** *Neu.* `LyrValue` ist `ulong Bits` +
`object? Ref` (`LyrValue.cs:19-22`, [geprüft]); „Integers are always widened to 64 bits"
(`:14-16`). Ein `int8[]` belegt pro Element denselben Platz wie ein `int[]`. → **SK-36**.

**(af) Die Spec friert Determinismus nur für Ganzzahlen ein.** *Neu.* §3.2: „wrapping is
deterministic and identical on every platform"; §6.1: „float per IEEE 754" — mehr nicht
[geprüft: keine Fundstelle zu FMA, Zwischenrundung, x87 oder transzendenten Funktionen in
`spec/03-types.md` und `spec/06-operators.md`]. → **SK-39**.

---

## 2. Sprachvergleich

*Gegenüber Fassung 2 an vier Stellen korrigiert (Zig-Zelle, Nim-`char`, Rust-`as`,
Go-Indexregel in SK-16); die Korrekturen sind unter der Tabelle benannt. Aussagen über
Vergleichssprachen sind aus Erinnerung an deren Spezifikationen, ohne Nachschlag in dieser
Sitzung — Bruchgrad „gelesen" beansprucht dieses Dossier nur für Lyric.*

| | Typsatz | Literal | Implizite Konversion | Überlauf zur Laufzeit | Konstanter Überlauf | Konversion | `char` |
|---|---|---|---|---|---|---|---|
| **Lyric 4** | **13 numerisch**, `int`/`int64` distinkt | Suffix oder Kontext-Adaption | keine | wrappt, immer | **still** | `as`, still; float→char kaputt (u) | ist eine Ganzzahl (kommentierte Compiler-Entscheidung, Spec nur beim Cast) |
| **Rust** | `i8..i128,u8..u128,isize,usize,f32,f64` | Suffix, sonst Inferenz (Fallback `i32`); `200i8` ist ein **Fehler** (`overflowing_literals`, deny) | keine | Panik in Debug, wrappt in Release (Profil) | Compilerfehler (`arithmetic_overflow`, deny) | `as` still, **nur numerisch/Pointer/Unsizing, nie Benutzercode**; `From`/`Into`/`try_into()` sind Trait-Aufrufe ohne `as` | `char` = Unicode-Skalar, **keine** Arithmetik |
| **Swift** | `Int8..Int64,UInt8..UInt64,Int,UInt` — `Int` ≠ `Int64` wie bei Lyric | Protokoll `ExpressibleBy…Literal`, Kontext entscheidet; `let x: Int8 = 200` ist ein Fehler | keine | **trap** in Debug *und* Release; nur `-Ounchecked` nicht; `&+` wrappt | Compilerfehler | `Int8(x)` trapt, `(exactly:)` → `Int8?`, `(truncatingIfNeeded:)`, `(clamping:)` | **`Character`** = erweiterter Graphem-Cluster; `Unicode.Scalar` darunter; beide ohne Arithmetik |
| **Go** | `int8..int64,…,int,uint,float32/64`, `int` ≠ `int64` | **untypisierte Konstanten mit beliebiger Präzision** | keine | wrappt (signed wie unsigned) | Compilerfehler | `T(x)`, still; float→int außerhalb = implementierungsabhängig | `rune` = `int32`, **ist** Arithmetik |
| **C#** | `sbyte..long,…,float,double,decimal`; `int` ist **Alias** für `Int32` | Suffix `u L UL f d m` | **ja, auch verlustbehaftet**: `int→float`, `long→double` implizit | unchecked per Default, `checked{}`/Projektflag | **Fehler, CS0220**; `1/0` konstant ist **CS0020** | Cast `(T)x`; `checked` macht ihn prüfend | UTF-16-**Einheit**, ist Arithmetik → `int` |
| **Zig** | beliebige Breiten `i0..i65535`, `f16..f128` | `comptime_int` (beliebige Präzision), `comptime_float` (**f128-Präzision**) | Int→Int- und Float→Float-Widening; **ein `comptime_int` coerciert auch zu jedem Float-Typ** (`var f: f32 = 5;` übersetzt); ein **Laufzeit**-Int braucht `@floatFromInt` | illegal: Panik in Safe, UB in ReleaseFast; `+%` wrappt, `+\|` sättigt | Compilerfehler | `@intCast`, `@truncate`, `@floatFromInt`, `@as` — je ein Builtin | kein `char`-Typ; `u21` |
| **Nim** | `int8..int64,…,int,float32/64` | Suffix `'i8 'u32 'f32` | **ja, für Ganzzahlen ohne Verlust** | **`OverflowDefect`**, abschaltbar per Flag | Compilerfehler | `T(x)`, geprüft | `char` = **Byte**, `string` = **Bytefolge**; Codepunkte nur über `unicode.runes` |
| **Ada** | Typen werden als **Bereich** deklariert; `mod 2**n` für wrappende | benannte Zahlen, beliebige Präzision | keine zwischen verschiedenen Typen | `Constraint_Error` bei jeder Bereichsverletzung | Fehler | `T(X)` geprüft; `Unchecked_Conversion` ist ein zu instanziierendes Generic | `Character`/`Wide_Wide_Character` |
| **Kotlin** | `Byte,Short,Int,Long,Float,Double` + **stabile unsigned seit 1.5** | Typ hängt von der **Größe** ab (`Int`, sonst `Long`) | **keine** (bewusster Bruch mit Java) | wrappt still | **wrappt ebenfalls**, höchstens eine Warnung | `toInt()` etc., still trunkierend | UTF-16-Einheit; `Char+Int`→`Char`, **`Char-Char`→`Int`** |
| **Haskell** | `Int` (feste Breite), **`Integer` = beliebig groß und der DEFAULT**, `Word`, `Int8..Int64`, `Float`, `Double` | **polymorph**: ein Literal ist `fromInteger :: Num a => Integer -> a`, plus Defaulting-Regeln | keine (aber das polymorphe Literal ersetzt sie) | `Int` wrappt still, `Integer` läuft nie über | — | `fromIntegral`, `realToFrac`, `toEnum` | `Char` = Unicode-Codepunkt, `ord`/`chr` |

**Korrekturen gegenüber Fassung 2 (Kritik in allen vier Punkten bestätigt):**

1. **Zig, Int→Float.** Fassung 2 schrieb „Int→Float gar nicht". Zu absolut: ein `comptime_int`
   coerciert zu jedem Float-Typ, nur ein Laufzeit-Integer braucht `@floatFromInt`. In der Sache
   bleibt: Zig hat **keine** Wertemengen-Widening-Regel für Laufzeitwerte; die Regel, die
   Fassung 1 Zig zuschrieb, ist ein offener Vorschlag (Issue-Nummer ziglang/zig#18614 —
   **behauptet**, in dieser Prüfung nicht verifiziert), kein Sprachzustand.
2. **Nims Antwort auf die Einheitenfrage ist „Byte", nicht „Codepunkt".** `string` ist eine
   Bytefolge, `char` ein Byte, Iteration läuft über Bytes. Fassung 2 zählte Nim in SK-19 zu den
   Codepunkt-Sprachen — falsch. Go und Rust bleiben.
3. **Rusts `as` hat nicht „dieselbe Vierdeutigkeit".** Es deckt numerische, Pointer- und
   Unsizing-Casts plus `use … as`, ruft aber **nie Benutzercode** (kein `Into` hinter `as`). Die
   Reue der Rust-Community betrifft nur den verlustbehafteten numerischen Cast. Lyrics `as`
   ist mit §3.6 Punkt 3 (beliebiger `into()`-Aufruf) **breiter** als Rusts.
4. **Go ist bei der Indexfrage Option B, nicht A** (siehe SK-16): „the index x must be an
   untyped constant or its core type must be an integer" — `var i uint8; a[i]` ist gültiges Go.
5. Die Issue-Nummer dotnet/csharplang#9561 (SK-14, keine Hex-Float-Literale in C#) ist ebenfalls
   **behauptet**; der Sachgehalt (C# hat keine) stimmt.

**Die Gegenentscheidungen, ausdrücklich.**

- **Swift ist Lyrics Gegenpol beim Überlauf.** Swift trapt bei jedem Überlauf, in Debug *und*
  Release. Eine Semantik in allen Builds (anders als Rust), keine Klasse stiller Rechenfehler,
  und wer wrappen will, schreibt `&+`. Der Preis ist ein zweiter Operatorsatz — bei Lyric ein
  Rule-2-Verstoß — und Laufzeitkosten pro Operation.
- **C# ist Lyrics Gegenpol bei der impliziten Konversion.** Jede numerische Erweiterung
  implizit, Präzisionsverlust bei `long→double` in Kauf genommen. Die „Wertemengen-Obermenge"-Regel
  hat **keine** Vergleichssprache; wer sie will, erfindet sie.
- **Ada löst SK-02 auf einer Ebene, die niemand kopiert.** Wrappen ist eine Eigenschaft des
  **Typs** (`mod 2**8`), nicht des Operators und nicht des Builds.
- **Zigs `comptime_int` und Haskells `fromInteger` sind die zwei reifen Antworten auf die
  Literalfrage.** Beide haben keine Liste von Adaptionskontexten, die man vergessen kann — und
  genau eine vergessene Position ist Befund (c), eine reihenfolgeabhängige ist (x).
- **Kotlin hat für `char` entschieden, was Lyric 4 halb tut**: `Char + Int` → `Char`,
  `Char - Char` → `Int`, `Char * Int` existiert nicht.
- **Swift hat als einzige die Graphem-Frage beantwortet** (SK-19). Go und Rust haben sie
  andersherum beantwortet (Codepunkt), **Nim mit „Byte"** — Lyric heute wie Go/Rust, und seit
  §3.3 auch aufgeschrieben, was den Index angeht.
- **Rust und Swift lehnen `200i8` ab**, Lyric druckt `-56` (c5). Go hat die Frage nicht (keine
  Suffixe; eine untypisierte Konstante, die nicht passt, ist ein Fehler).

---

## 3. Designfragen

### SK-01 — Bleiben `int`/`int64`, `uint`/`uint64`, `float`/`float64` verschiedene Typen?

**Heute**: ja. §3.1: „Every row is a **distinct type**. `int` and `int64` have identical width and
identical runtime representation and still do not assign to each other" [gelesen]; gemessen `g2`.
Das Format widerspricht nicht (`docs/Bytecode.md:548-549`, siehe (g)).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo | **Go** (`int` ≠ `int64`), **Swift** (`Int` ≠ `Int64`) | Beide haben denselben Grund: ihr `int` ist plattformbreit. Lyrics `int` ist fest 64 bit — die Trennung kostet Casts und kauft die Portabilitätsreserve nicht ein |
| B | `int64` = transparenter Alias für `int`, ebenso `uint64`, `float64` | **C#** (`int` ≡ `System.Int32`) | (1) §3.1 gibt den Breitentypen die Aufgabe „layouts and boundaries" — B löst die Grenzprüfung auf. (2) §3.5: „the alias never appears in a diagnostic where `T` serves" — `int64` verschwände aus allen Meldungen. **(3) Neu, siehe SK-36: der Layout-Preis ist im Referenz-VM heute hohl** — ein `int64` packt nicht anders als ein `int` (`LyrValue.cs:14-16`); die Aufgabe „layouts" wirkt nur an der Native-Grenze und in Bytecode-Konstanten |
| B′ | `int64` bleibt ein eigener Name, aber zuweisungskompatibel in beide Richtungen | — | Ein neuer Mechanismus („kompatible, aber verschiedene Typen") — Rule-2-Risiko |
| C | `int64`/`uint64`/`float64` streichen | — | Bricht jeden Layout-Code, der die Breite dokumentiert |

**Empfehlung: A.** Ohne die Format-Begründung (Fassung 1) bleibt nur Ergonomie gegen einen
belegten Preis in §3.1 und §3.5. **Aber**: SK-36 muss beantworten, was „layouts" bei einem VM
bedeutet, der alles auf 64 bit weitet — sonst ist Preis (1) unter B eine Behauptung.
**Bricht: A nein, B minor, B′ minor, C major.**
**Confidence: gelesen** (§3.1, §3.5, `Bytecode.md:548-549,619`, `LyrValue.cs:14-22`) + **gemessen** (`g2`, `w28`).
**Hängt ab von**: SK-36. **Beeinflusst**: SK-10, SK-14, SK-25.

### SK-02 — Bleibt Überlauf stilles Wrapping?

**Heute**: ja, eingefroren: §3.2 „*This is a decision, frozen here (2026-08-19)*" [gelesen].
Gemessen `a_wrap`, `w24`, `x18`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo + `checked { … }`-Block | **C#** (`checked`/`unchecked`) | Zwei Antworten auf „was tut `+`", lexikalisch umgeschaltet — die Form, vor der Rule 2 warnt. Dafür: kein Bruch, und §3.2 hat es zugesagt |
| B | Überlauf trapt immer; `&+ &- &*` wrappen | **Swift** | Zweiter Operatorsatz (Rule 2), Laufzeitkosten, **major** Bruch. Gewinn: eine Semantik überall |
| C | Überlauf nach Build-Profil: `debug` trapt, `release` wrappt | **Rust** | Bricht das Determinismus-Argument von §3.2 frontal: dasselbe Programm rechnet in zwei Builds verschieden |
| D | Wrappen wird eine Typ-Eigenschaft: ein `wrapping`-Modifier bzw. modularer Typ | **Ada** (`mod 2**n`) | Größte Änderung; dafür ohne zweiten Operator, ohne Block, ohne Build-Modus |

**Empfehlung: A für 5.0, D als die Frage, die A nicht beantwortet.**
**Bricht: A nein, B major, C major (still!), D minor.**
**4.x-Warnstufe** (falls B): eine Warnung auf jeder Ganzzahl-`+`-Stelle ist unbrauchbar — B ist
realistisch nur mit einem Migrationswerkzeug. *Korrektur*: `lyrfix` **existiert nicht**; es ist
ein **geplantes** Werkzeug (`docs/Befunde_und_Verbesserungen/PLAN.md:343`: „als
Migrationswerkzeug für die 5.0-Brüche — die Ablage unten setzt es voraus"; `lyric-v5-features.md:163`,
[geprüft]). B setzt also ein Werkzeug voraus, das erst gebaut werden muss.
**Confidence: gelesen** (§3.2, `PLAN.md:343`), Verhalten **gemessen**.
**Hängt ab von**: SK-03, SK-10/SK-11, SK-40; Build-Profile (Gebiet CLI/Toolchain) bei C.

### SK-03 — Was ist ein `checked`-Block genau?

**Heute**: existiert nicht (`PLAN.md` Posten 20).

Vier Unterfragen, die zusammen beantwortet werden müssen — **plus SK-40** (welche Operationen
zählen), das diese Fassung ausgliedert:

1. **Lexikalisch oder dynamisch?** C# entscheidet lexikalisch: `checked { f(); }` prüft **nicht**
   in `f`. Die einzige Form ohne Laufzeitflag und ohne Funktionsfärbung.
2. **Panik oder Exception?** Division durch null ist der Präzedenzfall und ist eine **Panik**
   (`LYR-VM0002`, `c5`, `x12a`).
3. **Gilt er auch für `as`?** `300 as int8` ist still 44. **Korrektur**: diese Unterfrage setzt
   voraus, dass die ungeprüfte `as`-Semantik feststeht — **sie steht nicht**: `float → char` ist
   heute kaputt (u, SK-29) und die signed→unsigned-Weitung ist unspezifiziert (v, SK-30). **Ein
   `checked as` braucht zuerst ein definiertes `as`.**
4. **Was ist der Rückfall, wenn man nicht paniken will?** Heute `std.math.checkedAdd/Sub/Mul/Abs`
   — **nur für `int`**.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Lexikalisch, Panik, schließt `as` ein | C# (lexikalisch) + Lyrics Panik-Linie | Wer eine abfangbare Prüfung will, nimmt `std.math.checkedAdd` — das es für 12 der 13 numerischen Typen nicht gibt |
| B | Lexikalisch, `OverflowError :: Throwable` | Nim (`OverflowDefect` ist abfangbar) | `throws`-Ansteckung durch die halbe stdlib |
| C | `checked` als **Ausdruck**: `checked(a + b)` → `?int` | — | Komponierbar, aber ein drittes Optional-erzeugendes Konstrukt |

**Empfehlung: A — nach SK-10/SK-11 und nach SK-29/SK-30.** Panik, weil Division durch null es
vormacht. `as` eingeschlossen, sonst ist der Block eine Falle — aber erst, wenn `as` selbst
definiert ist. Die Operationsliste steht in **SK-40**.
**Bricht: nein.**
**Confidence: gelesen** (§3.2, `math.lyr`), Panik-Präzedenz **gemessen** (`c5`, `x12a`).
**Hängt ab von**: SK-02, SK-10, SK-11, SK-12, SK-26, **SK-29, SK-30, SK-40**.

### SK-04 — Ist `char` eine Zahl?

**Heute**: der Compiler sagt ja — **bewusst und kommentiert** (`TypeFacts.cs:9-19`, siehe (a)):
`char` zählt als numerisch, damit `std.string` für „ist das eine Ziffer?" nicht in den Host
absteigen muss; der Preis wird in der VM bezahlt (Bereichsprüfung an jeder char-produzierenden
Operation). Die Spec nennt `char` beim Cast (§3.6) und bei Vergleichen (§6.2), nicht bei
Arithmetik. Das Lowering sagt bei `++` nein (`LYR-IR0001`). Gemessen: (a).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Bleibt Zahl, die Spec schreibt es auf | **Go** (`rune` = `int32`) | `'z'-'a'` bleibt ein `char`; `~c` bleibt eine garantierte Panik, die der Typchecker durchlässt; jede char-Operation behält ihre versteckte Laufzeitprüfung |
| B | `char` ist **keine** Zahl; `c as int` / `n as char` ist der Weg; Vergleiche bleiben (§6.2) | **Rust**, **Swift** | Die stdlib castet bereits durchgehend und enthält **keine** rohe char-Arithmetik. Kosten: ein veralteter Kommentar (`string.lyr:378`) und Benutzercode, der heute rechnet |
| C | `char ± int → char`, `char - char → int`, sonst nichts | **Kotlin** | Zwei Regeln statt einer; die Panik-Prüfung sitzt an genau zwei Stellen |

**Empfehlung: B, mit C als Rückfallposition — und mit der Begründung, warum der dokumentierte
Grund nicht mehr bindet.** *Korrektur gegenüber Fassung 2*, die die Gegenentscheidung als
„nie entschieden" las. Der Grund im Kommentar lautet: ohne `char`-als-Zahl müsste `std.string`
für `isDigit`/`parseInt` in den Host. **Das stimmt seit dem Umbau von `string.lyr` nicht mehr**:
`c as int - '0' as int` (`:447,590-594`) ersetzt die Arithmetik auf `char` vollständig — der
Cast ist §3.6 Punkt 1 und bleibt in Option B erhalten. Der Grund ist also erfüllt, ohne dass
`char` numerisch sein muss; was übrig bleibt, sind die Kosten (Laufzeitprüfung an jeder
Operation, `~c` als garantierte Panik). Der Maintainer liest damit **eine Entscheidung, deren
Voraussetzung weggefallen ist**, nicht ein Versehen.
**Bricht: major.**
**4.x-Warnstufe**: ab 4.7 Warnung auf jedem arithmetischen/bitweisen Operator mit `char`-Operand;
feuert in der gesamten `stdlib/` an **null** Stellen. `~c` und `*`/`/`/`%` auf `char` sofort auf
Warnung. SK-24 wird trotzdem gebraucht.
**Confidence: gemessen** (`d_char`, `d_and`, `d_shl`, `d_not`; stdlib-Suche) + **gelesen**
(`TypeFacts.cs:9-21`, `IrVerifier.cs:1748-1756`, §3.1, §3.6, §6.2).
**Hängt ab von**: SK-05, SK-19, SK-24, SK-29; Gebiet Strings/stdlib.

### SK-05 — Adaptiert ein Ganzzahlliteral an `char`?

**Heute**: ja, undokumentiert. `d_litchar`: `let c: char = 65;` → `A`; Begründung in
`TypeFacts.IntLiteralFits`, Zweig `PrimitiveKind.Char` (`TypeFacts.cs:59-66`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Bleibt, kommt in §3.1 dazu | **C#** (`char c = 65;`), **Java** (narrowing für konstante Ausdrücke), **Go** (`var r rune = 65`) | Eine Regel mehr in einer Liste, die SK-06 ohnehin kritisiert |
| B | Streichen, `65 as char` ist der Weg | **Rust** (`char::from_u32` → `Option`), **Swift** (`Unicode.Scalar(65)`, failable) | Ein Cast mehr an wenigen Stellen |

**Empfehlung: B bei SK-04=B, sonst A.** Rust und Swift geben mit derselben SK-04-Antwort dieselbe
SK-05-Antwort.
**Bricht: minor.** Warnstufe: 4.7 Warnung, 5.0 Fehler.
**Confidence: gemessen** (`d_litchar`, `d_litchar2`) + **gelesen** (`TypeFacts.cs:59-66`).
**Hängt ab von**: SK-04, SK-24, SK-37 (dieselbe Frage für `opaque`).

### SK-06 — Bleibt Literal-Adaption eine Liste von Positionen?

**Heute**: ja — §3.1 zählt sie „exhaustively" auf [gelesen]. Gemessen: eine Position (`??`)
erzeugt für 9 der 13 Typen fehltypisierten IR (c), eine strukturelle Position (Tupel) fehlt
(SK-07), die Adaption an `char` steht nicht in der Liste (SK-05), an `opaque` gibt es sie nicht
(SK-37), die Generics-Inferenz ist ungeklärt (SK-23) **und reihenfolgeabhängig (SK-32)**.
Sechs Abweichungen bei sechs Einträgen.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Liste bleibt, Bugs fixen | — | Die nächste Position wird wieder vergessen |
| B | Liste bleibt, mechanisch abgesichert: jede `IsAssignable`-Zusage **muss** `AdaptLiteralType` nachziehen (prüfbar wie die `ErrorType`-Regel, `STATUS.md:2324`) | — | Eine Konvention mehr, aber eine, die dieses Projekt durchhält |
| C | Ein unsuffigiertes Literal bekommt einen **eigenen Typ** ohne Breite, der erst beim Verlassen der Compile-Zeit materialisiert | **Zig** (`comptime_int`; `comptime_float` nur f128), **Go** (untypisierte Konstanten) | Inferenz wird ein Solver statt einer Positionsliste; berührt `comptime`, Generics, Konstantenfaltung |
| D | Ein Literal ist ein **Aufruf mit offenem Rückgabetyp** (`fromInteger`) plus Defaulting | **Haskell** | Wie C, aber die Tür steht eigenen Typen offen (SK-15f, SK-37). Preis: Lyric braucht Defaulting-Regeln |

**Empfehlung: B für 4.7 (mit den Bugfixes), C oder D als die eigentliche v5-Frage.** C/D lösen
(c), SK-07, SK-13, SK-32 und SK-41 auf einmal und geben SK-08/SK-09/SK-28 eine Stelle, an der
geprüft wird.
**Bricht: C/D minor.**
**Confidence: gelesen** (§3.1) + **gemessen** (`w01`–`w04`, `i1`, `d_litchar`, `w17a/b`, `x09a/b`, `x13a`).
**Hängt ab von**: SK-23, SK-32; Gebiet Generics/Inferenz, Gebiet `comptime`.

### SK-07 — Sind Tupel-Elemente ein Adaptionskontext?

**Heute**: nein. `i1` → `SEM0001`. Notiert in `STATUS.md:2284-2289`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Aufnehmen — ein Wort in §3.1 | **Swift**, **Zig** (anonymes Struct-Literal) | Eine Position mehr in der Liste, die SK-06 kritisiert |
| B | Wegfallen lassen, weil SK-06=C/D sie überflüssig macht | — | Wartet auf eine große Änderung |

Go hat keinen Tupeltyp und ist kein Vorbild.
**Empfehlung: A jetzt, unabhängig von SK-06.** **Bricht: nein.**
**Confidence: gemessen** (`i1`) + **gelesen** (`STATUS.md:2284-2289`). **Hängt ab von**: SK-06.

### SK-08 — Dürfen Literale still runden?

**Heute**: **unsuffigierte** Ganzzahlliterale nicht (exakt oder Fehler, §3.1; `x03e`),
Float-Literale ja (`LiteralAdaptsTo` gibt unbedingt `true`), **suffigierte Literale werden gar
nicht geprüft** (`x03d`: `16777217f32` → `16777216`; → SK-28). Float-Literale dürfen zu
`Infinity` oder `0` kollabieren (`m1`, `m3`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Symmetrie nach oben: auch Float-Literale müssen exakt sein | — | Unbrauchbar: `let x: float32 = 0.1;` wäre ein Fehler |
| B | Symmetrie nach unten: Ganzzahlliterale dürfen runden | C#, Java | Gibt die Regel auf, die §3.1 bewusst gewählt hat |
| C | **Rundung erlaubt, Überlauf nicht**: ein Literal, das am Zieltyp zu ±Inf wird, ist ein Fehler. Ganzzahl-Exaktheit bleibt. Unterlauf zu 0 erlaubt | **Go** („constant values never result in an IEEE negative zero, NaN, or infinity"; `-1e-1000` ausdrücklich darstellbar) | Eine Regel mehr, die richtige Trennlinie |
| D | Wie C, plus Unterlauf als Fehler | — (kein Vorbild) | Neu erfunden |

**Empfehlung: C — und die Regel muss für suffigierte Literale genauso gelten (SK-28).**
**Bricht: minor.** **4.x-Warnstufe**: 4.7 Warnung „literal overflows 'float32'", 5.0 Fehler.
**Confidence: gemessen** (`m1`, `m2`, `m3`, `e3`, `e4`, `e10`, `x03d`, `x03e`) + **gelesen** (`TypeChecker.cs:6090`).
**Hängt ab von**: SK-06, SK-24, **SK-28**.

### SK-09 — Wie klingt ein nicht passendes Literal?

**Heute**: zwei Muster. Für den **unannotierten** Überlauf gibt es bereits eine gute Meldung:
`LYR-SEM0001: integer literal does not fit 'int' — annotate the uint type that holds it`
(`x04a`/`x04b`; `WarningAnalyzer.CheckLiteralInRange`). *Korrektur*: Fassung 2 sagte „nirgends
steht 'passt nicht'". Für **jeden Zieltyp-Fall** dagegen `LYR-SEM0001: cannot assign 'int' to
'int8'` (`e1`, `e3`, `e5`, `e8`, `i7`, `x03e`), bei `+=` `operator 'Add' is not applicable`
(`i4`). Für **suffigierte** Literale gar nichts oder „defect in the compiler" (c5).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Eigener Code: `literal 200 does not fit 'int8' (range -128..127)` — **nach dem Muster der vorhandenen Meldung** | **Swift** („integer literal '200' overflows when stored into 'Int8'"), **C#** CS0031, **Rust** („literal out of range for `i8`"); Lyrics eigener `x04a`-Text | Ein Diagnosecode mehr; `spec/appendix-a` und Guide ziehen nach |
| B | Status quo | — | Der häufigste Anfängerfehler bekommt die unspezifischste Meldung |

**Empfehlung: A — mit SK-28 als Voraussetzung.** Ein A, das nur `SEM0001` umtextet, lässt den
stilleren Fall (`200i8` → `-56`) unberührt; die Sema-Prüfung für Suffixe muss zuerst existieren,
sonst gibt es nichts zu melden. Drei Texte: außerhalb des Bereichs, nicht exakt in Float,
Bereichsverlust in Float (SK-08). Dazu `5u` (LEX0003 + PAR0006) und `j2` (zweimal `SEM0028`)
zusammenlegen; `j5`/`w13` **nicht** (dort ist `SEM0012` eine Folgemeldung → Gebiet Diagnostik,
`ErrorType`-Regel).
**Bricht: nein.** **Confidence: gemessen** (`e1`–`e8`, `i4`, `i7`, `w30`, `j2`, `j5`, `w13`, `x04a/b`)
+ **gelesen** (`WarningAnalyzer.cs:631-638`).
**Hängt ab von**: **SK-28**.

### SK-10 — Werden die Breitentypen (und `uint`) erstklassig?

**Heute**: nein. 11 der 13 numerischen Typen ohne `Display`, 10 ohne jede Konformanz, `float`
ohne `Hashable`, `uint` ohne `Display` (`w05`). Die `std.core`-Konformanzen sind von außen nicht
nachrüstbar (`x19`: `LYR-SEM0041`) — **eigene** Interfaces schon (`x06`, Korrektur h2).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Konformanzen für alle von Hand in `std.core` | — | ≈ **52 `extend`-Blöcke**; jedes neue Interface kostet wieder 13 |
| B | Numerische Interface-Hierarchie: `Numeric` → `BinaryInteger` → `FixedWidthInteger` (`bitWidth`, `max`, `min`) und `BinaryFloatingPoint` | **Swift**, **C#** (`INumber<T>`), **Rust** (`num`) | Braucht statische Interface-Member und `Self` (v5-Liste A1 #4) und bedingte Konformanz (#2) |
| C | Breitentypen bleiben Layout-Typen ohne Protokoll | — | Dann muss `int64` aus der Primitivliste, und `uint` erst recht |

**Was B kostet — gemessen** (`w27a`/`w27b`, 2026-09-24): dieselbe generische Funktion einmal an
einem, einmal an fünf Typen instanziiert: **2 920 B → 3 955 B**, +1 035 B für 4 Instanzen ≈
**259 B pro Instanz** einer dreizeiligen Funktion; Compile-Zeit 675/835 ms → 948/1 272 ms
(verrauscht). Die Achse ist Modulgröße und Compile-Zeit, nicht Laufzeit.

**Empfehlung: B, Swift-Schnitt; `Display` für `uint` und alle Breiten sofort.** Die
„Mengen angleichen": was ein f-String rendert (`w29`), soll auch `Display` haben. §6.6
entscheidet die zwei Rendering-Wege ausdrücklich („there is no implicit `Display` call in
interpolation") — kein Rule-2-Verstoß.
**Bricht: nein.**
**Confidence: gemessen** (`w05`, `w06`, `w13`, `x06`, `x19`, `w27a/b`, `j2`–`j7`, `w29`) + **gelesen** (`core.lyr:106-455`, §6.6).
**Hängt ab von**: statische Interface-Member + `Self`, bedingte Konformanz (Gebiet Generics/Interfaces).
**Muss VOR**: SK-03.

### SK-11 — Woher kommen `max`, `min`, `epsilon`, `infinity`, `nan`, `bitWidth`?

**Heute**: `std.math` hat fünf `pub let` (`pi`, `e`, `tau`, `intMax`, `intMin`); NaN nur über
`0.0/0.0` (`w26`, `x14`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | ~30 benannte Konstanten in `std.math` | Go (`math.MaxInt8`) | Namensflut; generischer Code kommt nicht ran |
| B | Statische Interface-Member: `T.max`, `T.min`, `T.bitWidth`, `T.nan`, `T.epsilon` | **Swift**, **Rust**, **C#** | Hängt an SK-10 |
| C | `comptime`-Funktionen | Zig (`std.math.maxInt(T)`) | Braucht Typen als comptime-Werte |

**Empfehlung: B als Teil von SK-10; `nan`/`infinity` zusätzlich als Konstanten in `std.math`.**
**Bricht: nein.** **Confidence: gelesen** (`math.lyr`) + **gemessen** (`w26`, `x14`).
**Hängt ab von**: SK-10. **Muss VOR**: SK-03.

### SK-12 — Bleibt `as` das Wort für vier verschiedene Dinge?

**Heute**: `as` ist (1) numerische Konversion mit stillem Verlust, (2) `opaque` ↔ underlying,
(3) der `Into<T>`-Aufruf, also **beliebiger Benutzercode** — alle drei in §3.6 [gelesen] — und
(4) der Import-Alias (`docs/Grammar.md`). Gemessen: `300 as int8` = 44; `9007199254740993 as
float` = …992 (`l2`) still, während `let f: float = 9007199254740993;` ein Fehler ist (`l1`).
**Und (1) ist heute nicht einmal vollständig definiert**: `float → char` ist kaputt (u),
signed→unsigned-Weitung unspezifiziert (v).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo | — (*Korrektur*: Rust ist **kein** Vorbild für „dieselbe Vierdeutigkeit" — Rusts `as` ruft nie Benutzercode; die Reue betrifft nur den verlustbehafteten numerischen Cast) | Ein Wort trägt vier Bedeutungen, davon eine mit beliebigen Laufzeitkosten |
| B | `as` behält nur (1) und (2); `Into` wird `v.into()` | **Rust** (genau dieser Schnitt: `as` numerisch, `into()` Trait) | `as` wird kleiner. Heute sind zwei Schreibweisen für dieselbe Konversion sichtbar — Rule 2 ist auf dieser Seite |
| C | Geprüfte Varianten in der **Bibliothek**: `toInt8Exact(v): ?int8`, bzw. `TryInto<T>` | **Swift** (`Int8(exactly:)`, `(clamping:)`, `(truncatingIfNeeded:)`), **Rust** (`try_into`) | Ein Protokoll mehr, keine neue Syntax |
| D | Warnung auf einem `as` mit **konstantem** Operanden, der Daten verliert | jeder Linter | `300 as int8` ist nie Absicht |

**Empfehlung: B + C + D — nach SK-29/SK-30.** Zuerst muss (1) vollständig definiert sein.
**Bricht: B minor** (Warnstufe 4.7, Fehler 5.0); C und D brechen nicht.
**Confidence: gelesen** (§3.6) + **gemessen** (`c_as`, `w23`, `l1`, `l2`, `x02a`, `x07`).
**Hängt ab von**: SK-10 (C), SK-03, SK-21, SK-24, **SK-29, SK-30, SK-37**.

### SK-13 — Darf eine Konstante still überlaufen?

**Heute**: ja. `g4`, `w35`. **Und eine konstante Division durch null kompiliert** (`x12a`) —
das ist SK-35.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Fehler | **C#** (CS0220), **Swift**, **Go**, **Rust** (`arithmetic_overflow`, deny), **Zig**, **Nim**, **Ada** — sieben | Bricht nur Programme, die schon falsch rechnen |
| B | Warnung | Kotlin | Halbe Antwort, ohne Bruch |
| C | Status quo | Kotlin (wrappt, keine Meldung) | Schlechte Gesellschaft |

**Empfehlung: B in 4.7, A in 5.0 — zusammen mit SK-35.** Das Determinismus-Argument von §3.2
gilt für Laufzeit-Arithmetik.
**Bricht: major** (formal), praktisch nur kaputten Code.
**Confidence: gemessen** (`g4`, `w35`, `x12a/b/c`) + **gelesen** (§3.2).
**Hängt ab von**: SK-06, SK-24, SK-26, **SK-35**.

### SK-14 — Fehlende Literalformen

**Heute**: kein Suffix für `uint`/`int`/`float` (**§1.3/§1.4** — *Korrektur*: nicht §1.7;
`w30`); kein Hex-Float (`w31`); Trennzeichen und Präfixe vorhanden (`g7`, `m4`). **Aber**: ein
`uint` entsteht sehr wohl aus einem Literal — per Kontext (`x01`); das Suffix fehlt nur im
unannotierten Kontext (`x04b`, → SK-41).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Suffixe `u`, `i`, `f` für die Default-Typen | **C#** (`u`, `L`, `f`), **Rust** (`1usize`) | Drei Suffixe mehr; `uint` ist der einzige Default-Typ ohne eigene Literalform |
| B | Hex-Float-Literale `0x1p-3` | **C99/C++17**, **Java** (seit 5), **Go 1.13**, **Swift** — **nicht C#** (keine Version; Issue dotnet/csharplang#9561 **behauptet**) | Rein lexikalisch; das Gegenmittel zu SK-08 |
| C | `bool`-Bitoperatoren `& \| ^` | **Rust**, **C#**, **Java** | Selten; `t != f` ist xor |

**Empfehlung: A und B (klein, P3); C ablehnen.**
**Bricht: nein.** **Confidence: gemessen** (`w30`, `w31`, `g7`, `m4`, `m5`, `x01`, `x04b`) + **gelesen** (§1.3, §1.4).
**Hängt ab von**: SK-01, SK-27, SK-41.

### SK-15 — Fragen, die Lyric 4 gar nicht stellt (Sammelposten)

| # | Frage | Wer hat es | Was es kostet |
|---|---|---|---|
| a | **Ein Dezimaltyp** für Geld | C# (`decimal`), Ada (fixed-point) | Ein vierzehnter Primitivtyp oder ein Bibliothekstyp; Formatfrage (Tags 0x01–0x0E belegt) |
| b | **Beliebig große Ganzzahlen** | **Haskell** (`Integer` ist der DEFAULT), Python; .NET bringt `BigInteger` mit. Nicht Nim (Drittpaket) | Bibliothek — `std.math.big` in der v5-Liste B4 |
| c | **`overflowingAdd` → `(T, bool)`** | Rust, Zig (`@addWithOverflow`) | Nur Tupel — der billigste Baustein für SK-02/SK-03 |
| d | **Saturierende Operatoren** `+\|` | Zig | Rule-2-Problem; die Bibliotheksform reicht |
| e | **Ein `Parse`/`FromStr`-Protokoll** | Rust (`FromStr`), Swift (`Int8("…")`) | Heute `parseInt → ?int`, `parseFloat → ?float` (`string.lyr:492,628`) — sonst nichts |
| f | **Literale für eigene Typen** (`let d: Duration = 5;`) | Swift (`ExpressibleByIntegerLiteral`), **Haskell** (`fromInteger`), **C++** (benutzerdefinierte Literal-Suffixe `5_ms`) — *Korrektur*: **nicht Nim** (`converter` ist eine implizite Konversion für beliebige Ausdrücke, kein Literal-Protokoll) | Der Mechanismus aus SK-06=D nach außen geöffnet; `FromLiteral` in der v5-Liste A2 #11; **SK-37 ist der erste konkrete Fall** |
| g | **Was bedeutet `==` auf `float`?** | Rust warnt nicht, Clippy schon; Swift/Go akzeptieren | `0.1+0.2 == 0.3` ist stumm `false` (`h_f32`); dazu `0.0 == -0.0` true (`x14`, SK-38) und Float-Literal-Patterns (`x16a`, SK-42) |

**Empfehlung**: (c) und (e) mit SK-10 bauen, (g) als Warnung, (a)/(b)/(d)/(f) vertagen — (f)
mit SK-06=D und SK-37 zusammendenken.
**Confidence: gelesen** (stdlib, Bytecode-Tagtabelle) + **gemessen** (`h_f32`, `x14`, `x16a`).

---

### SK-16 — Welcher Typ indiziert eine Sequenz?

**Heute**: `int` — jeder andere Ganzzahltyp erzeugt fehltypisierten IR (`w07`, `w08`; Kontrolle
`w09`; `w19`: Range-Grenzen und Schleifenzähler auf `int8` sind in Ordnung, nur der Indexslot
nicht). `docs/Bytecode.md` §Arrays: „An element index is a runtime value"; der Verifier verlangt
`i64`. Die Spec sagt zum **Typ** des Index nichts [gelesen].

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Ein Index ist genau `int`; jeder andere Typ ist ein Sema-Fehler mit `as int`-Hinweis | **—** (*Korrektur*: Fassung 2 nannte Go. **Falsch**: Gos Spec sagt „the index x must be an untyped constant or its core type must be an integer" — `var i uint8; a[i]` ist gültig, Go ist Option B. A hat damit **kein** Vorbild in der Tabelle) | Ehrlich und billig; `xs[u]` muss `xs[u as int]` werden |
| B | Jeder Ganzzahltyp indiziert; die Lowering weitet/verengt auf `i64`, negativ bleibt Panik | **Go** (jeder Ganzzahltyp), **C#** (`int`, `uint`, `long`, `ulong`), **Rust** (`usize` per `Index`; andere über `as`) | Eine Konversion mehr in der Lowering; `uint` > `int.max` als Index muss eine Panik geben |
| C | Ein eigener Indextyp (`usize`-artig) | **Rust** | Ein vierzehnter Typ; jede Länge in der stdlib müsste ihn tragen |
| D | Status quo (`int`), aber der Sema meldet es statt der Verifier | — | Minimalfix; beantwortet die Designfrage nicht |

**Empfehlung: D sofort (Sweep), B für 5.0 — neu begründet.** Fassung 2 stellte A (Go) gegen B
(C#); das war falsch, Go und C# geben dieselbe Antwort. Das Ergebnis wird dadurch **stärker**:
die drei Sprachen mit expliziten Breitentypen und ohne eigenen Indextyp (Go, C#, Kotlin/JVM
ebenso) lassen jeden Ganzzahltyp indizieren; nur Rust hat einen Sondertyp, und der ist mit
Option C teuer. `uint` als Index ist der Fall, den ein Benutzer natürlich schreibt (nicht-negative
Größe). Die Panik bei `uint > int.max` ist konsistent mit der Indexpanik.
**Bricht: D nein, B nein** (additiv).
**Confidence: gemessen** (`w07`, `w08`, `w09`, `w19`) + **gelesen** (`Bytecode.md` §Arrays).
**Hängt ab von**: SK-27, SK-01, SK-31 (dieselbe „Typ des zweiten Operanden"-Frage).

### SK-17 — Was bedeutet unäres `-` auf einem vorzeichenlosen Typ?

**Heute**: compiliert und wrappt still (`w10`). Die Spec sagt nichts (§3.2, §6.1, `spec/13`
`neg` „negation, numeric") [gelesen].

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, aufgeschrieben: `-u` ist `0 - u`, wrappend | Go, C | Ein fast nie gemeinter Ausdruck bleibt still |
| B | Unäres `-` auf unsigned ist ein **Fehler** | **Rust** (E0600), **Swift** (`SignedNumeric`), **Zig** (`-%` für wrappend; `-` illegal) | Wer das Zweierkomplement will, schreibt `0 - u` |
| C | Fehler, plus `abs` bekommt dieselbe Regel | Swift (`magnitude`) | `absInt` ist `int`-only, also kostenlos |

**Empfehlung: C.** **Bricht: minor.** Warnstufe 4.7, Fehler 5.0.
**Confidence: gemessen** (`w10`, `w33`) + **gelesen** (§3.2, §6.1 — Negativbefund).
**Hängt ab von**: SK-10, SK-24, SK-27.

### SK-18 — Wie wird eine Gleitkommazahl zu Text — und wieder zurück?

**Heute**: `float` kürzest-round-trip (`w20`), `float32` **nicht** (`w11`, `x11`, `w22`).
Mechanismus: `ToStringValue` mappt `F32 or F64` auf `fromFloat` nach `WidenForHelper`; kein
`fromFloat32`, kein `parseFloat32` [gelesen]. **Dasselbe Leck für Ganzzahlen ist SK-33**, das
Default-Vokabular ist SK-34.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo | — | Ein `float32` druckt eine Zahl, die der Benutzer nie geschrieben hat |
| B | Garantie „`parse(format(x)) == x` pro Breite": `fromFloat32`/`parseFloat32`, der f-String wählt nach Breite | **Rust**, **Swift**, **.NET Core ≥ 3.0** | Zwei native Signaturen in `std.string`; mit SK-10 generisch |
| C | Wie B, plus ausdrückliches Format-Vokabular | C# (`R`, `G17`), Java (`%a`) | `std.fmt` wächst — das ist SK-34 |

**Empfehlung: B; C geht in SK-34 auf.** **Bricht: nein** (Verhaltensänderung, CHANGELOG).
**Confidence: gemessen** (`w11`, `w20`, `w22`, `w03`, `x11`) + **gelesen** (`FunctionLowerer.cs:4927`, `string.lyr`, `json.lyr:166`).
**Hängt ab von**: SK-10, SK-11, SK-15e, **SK-33, SK-34**.

### SK-19 — Ist ein `char` ein Codepunkt oder ein Graphem-Cluster?

**Heute**: ein Codepunkt, **in der Spec entschieden** — §3.1 („one Unicode scalar value"), §3.3
(„`string` is NOT indexable, by decision: code-point access is O(n)") [gelesen, `03-types.md:73-75`].
*Korrektur*: Fassung 2 schrieb, `LYR-SEM0007` sei eine Entscheidung, die „nur im Compiler"
stehe. **Falsch** — sie steht in §3.3, Wort für Wort dieselbe Begründung. Gemessen `w21`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, ausdrücklich bestätigt und im Guide mit Beispiel | **Go** (`rune`), **Rust** (`char`) — *Korrektur*: **nicht Nim** (`char` = Byte) | Der Benutzer, der ein Combining-Paar zerschneidet, merkt es nicht |
| B | Zwei Ebenen wie Swift: `Character` (Graphem-Cluster) als Standardeinheit | **Swift** | Ein Primitivtyp mehr, UAX #29 in der Laufzeit, Unicode-Tabellen — für eine embeddable Runtime ein Brocken |
| C | A, plus Graphem-Sicht in der Bibliothek | Rust mit Crate | Tabellen in der stdlib; Pflegeaufwand |

**Empfehlung: A für 5.0, C als Bibliotheksposten.** Was fehlt, ist nur das Guide-Beispiel
(„`e` + Combining Acute sind zwei `char`"); die Spec hat die Frage schon beantwortet.
**Bricht: nein.** **Confidence: gelesen** (§3.1, §3.3) + **gemessen** (`w21`).
**Hängt ab von**: SK-04; Gebiet Strings.

### SK-20 — Was antwortet `Ordered<float>` auf NaN, und was `Hashable<float>`?

**Heute**: `compare` ist total und falsch (`w12`: `compare(nan, x)` = 0 für jedes `x`,
`equals(nan, nan)` = false); `Hashable<float>` gibt es nicht (`w13`). `core.lyr:249-259`.
**Und `0.0 == -0.0` ist `true` bei verschiedenen Bitmustern** (`x14`) — das ist SK-38.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `compare` wird **total**: IEEE-754 `totalOrder` (−NaN < −Inf < … < −0 < +0 < … < +Inf < +NaN) | **Rust** (`f64::total_cmp`), **Swift**, IEEE 754-2008 §5.10 | `compare` und `<` sagen bei NaN Verschiedenes — ehrlich und dokumentierbar. **Aber** (*Korrektur*): `total_cmp` sagt `-0 < +0`, wo `==` gleich sagt — **nicht nur bei NaN** widerspricht A dem `Equatable`; ohne Normalisierungsregel (SK-38) ist A kein „gratis" |
| B | `Ordered` wird geteilt: `PartiallyOrdered` (float) und `Ordered` (total) | **Rust** (`PartialOrd`/`Ord`) | Ein Interface mehr; jede generische Funktion muss sich entscheiden |
| C | `float` bekommt keinen `Ordered` | — | Bricht jede `sort`-Verwendung auf `float[]` |
| D | Status quo + Doku | — | Eine Bibliothek, die eine falsche Antwort gibt und sie aufschreibt |

**Empfehlung: A für 5.0 mit der Normalisierung aus SK-38, und den Widerspruch sofort als Bug
melden.** B ist die richtigere Modellierung und gehört in die Interfaces-Runde.
**Bricht: A minor.**
**Confidence: gemessen** (`w12`, `w13`, `x14`) + **gelesen** (`core.lyr:249-259`, §6.2).
**Hängt ab von**: SK-21, SK-10, **SK-38**; Gebiet Interfaces (B), Gebiet Collections.

### SK-21 — Wie kommt man an das Bitmuster eines Floats?

**Heute**: gar nicht (stdlib-Suche negativ; `as` ist wertetreu, §3.6). `w23`: `1.5 as int` = 1.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `as` bekommt eine zweite, **reinterpretierende** Bedeutung bei gleicher Breite | — | **Rule-2-Verstoß erster Güte**, still. Abzulehnen |
| B | Bibliotheksfunktionen `floatToBits(v: float): uint`, `bitsToFloat`, dito `float32` | **Rust** (`to_bits`), **Swift** (`bitPattern`), **Java**, **C#** (`BitConverter`) | Zwei bis vier native Signaturen; mit SK-10 in `BinaryFloatingPoint` |
| C | Builtin `bitcast<T>(v)` | **Zig** (`@bitCast`) | Sprachkonstrukt für etwas, das eine Funktion sein kann |

**Empfehlung: B, ausdrücklich nicht A. Und *Korrektur*: B gibt `Hashable<float>` nicht
„gratis"** — ein Bitmuster-Hash verletzt `equals ⇒ gleicher Hash` bei `0.0`/`-0.0` (`x14`) und
bei verschiedenen NaN-Bitmustern. Die Normalisierung (SK-38) ist Teil des Preises.
**Bricht: nein.** **Confidence: gelesen** (§3.6, stdlib-Suche) + **gemessen** (`w23`, `x14`).
**Hängt ab von**: SK-10, SK-12, **SK-38**. **Ermöglicht**: SK-20.

### SK-22 — Wo leben die Bit-Werkzeuge?

**Heute**: nirgends (`countOnes`, `leadingZeros`, `rotateLeft`, `byteSwap`, … — keine
Fundstelle). Die Operatoren gibt es (§6.1; `f_shift`, `d_and`, `x05`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Modul `std.bits` mit `int`-Funktionen | Go (`math/bits`) | Nur für `int`; genau die Operationen, die man auf `uint8`/`uint32` braucht |
| B | Mitglieder von `FixedWidthInteger` (SK-10) | **Swift**, **Rust**, **C#** | Hängt an SK-10; ein Mechanismus für alle Breiten |
| C | Als Opcodes ins Format | Zig (`@popCount`) | Neue Opcodes = Format-Minor; erst nach Messung |
| D | Weglassen | — | Dann ist `T.bitWidth` sinnlos |

**Empfehlung: B, C nach Messung.** **Bricht: nein.**
**Confidence: gelesen** (stdlib-Suche, §6.1) + **gemessen** (`f_shift`, `d_and`, `d_shl`, `x05`).
**Hängt ab von**: SK-10, SK-11.

### SK-23 — Wie verträgt sich Literal-Adaption mit Generics-Inferenz (Rückfluss aus dem Bindungsziel)?

**Heute**: `takes<int8>(5)` läuft (`w17a`), `let a: int8 = takes(5)` ist `SEM0001` (`w17b`).
§3.1: Adaption gilt „to LITERALS only"; bei `takes(5)` bestimmt die Inferenz den Parametertyp
zuerst und wählt `int`. **Die Reihenfolge *innerhalb* eines Aufrufs ist SK-32.**

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, aufgeschrieben | — | Nicht erklärbar: `takes<int8>(5)` und `let a: int8 = takes(5)` gehen verschieden aus |
| B | Bidirektionale Inferenz: der Zieltyp der Bindung fließt in die Instanziierung zurück | **Swift** (vollständig bidirektional), **Kotlin** (erwarteter Typ fließt in die Inferenz) — *Korrektur*: **nicht C#**. C#s generische Methodeninferenz benutzt den Zuweisungszieltyp nie; `sbyte a = Takes(5);` inferiert `T = int` und scheitert genau wie `w17b`. Target-typed `new` und Lambda-Rückgabe sind andere Mechanismen | Der Inferenzalgorithmus wird ein Constraint-Solver |
| C | SK-06=C/D: das Literal hat einen offenen Typ, die Instanziierung wählt ihn | **Zig**, **Haskell** | Dasselbe Ergebnis auf dem anderen Weg; löst (c), SK-07, SK-13, SK-32 mit |
| D | `takes<int8>(5)` auch verbieten | — | Konsistenz durch Verschlechterung |

**Empfehlung: C, gemeinsam mit SK-06 und SK-32.** **Bricht: C minor.**
**Confidence: gemessen** (`w17a`, `w17b`) + **gelesen** (§3.1).
**Hängt ab von**: SK-06, **SK-32**; Gebiet Generics/Inferenz.

### SK-24 — Wie schweigt man eine 4.x-Deprecationswarnung an?

**Heute**: gar nicht. §12.1 [gelesen, `spec/12-diagnostics.md:24-28`]: „**Severity belongs to
the code.** … a strict mode changes exit-code policy (the toolchain's `--deny-warnings` reports a
closing error), never a code's severity". Kein `@allow`/`suppress` in der Spec [Negativbefund].
`@Deprecated` **erzeugt** Warnungen (§4.7). Dieses Dossier plant Warnstufen für SK-04, SK-05,
SK-08, SK-12=D, SK-13, SK-17, SK-18, SK-28, SK-35.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Ein Attribut `@Allow("LYR-SEM0xxx")` auf Deklaration/Block | **Rust** (`#[allow(…)]`), **C#** (`#pragma warning disable`), **Java** (`@SuppressWarnings`), **Kotlin** (`@Suppress`) — *Korrektur*: **nicht Swift**. Swift hat kein quellseitiges Unterdrückungsattribut; SE-0443 (Swift 6.x) führt `-Wwarning`/`-Werror` als **Compiler-Flags** ein — das ist Option C. Vier Zeugen, nicht fünf | Ein zweiter Attributmechanismus neben `@Deprecated` — aber die beiden tun Gegensätzliches (melden vs. schweigen); kein Rule-2-Verstoß |
| B | Nur auf Dateiebene | Rust (crate-level) | Grobkörnig |
| C | Nur über die Toolchain: `lyric build --allow LYR-SEM0xxx` | **Swift** (SE-0443), Go, viele Linter | Die Unterdrückung steht nicht dort, wo die Absicht steht, und reist nicht mit dem Code |
| D | Gar keine | — | `--deny-warnings` wird in einem Projekt mit absichtlicher char-Arithmetik bis 5.0 unbenutzbar |

**Empfehlung: A.** *Korrektur der Begründung*: der Guide **bietet** `--deny-warnings` an
(`docs/guide/19-diagnostics.md:44-47`: „Warnings never fail a build by themselves. In CI you can
make them"), er **empfiehlt** es nicht — Fassung 2 überzeichnete. Das „nicht durchführbar"-Argument
wird dadurch schwächer, nicht falsch: wer die Option nutzt, hätte zwischen 4.7 und 5.0 die Wahl
zwischen „Build rot" und „Warnpolitik abschalten". §12.1 muss einen Satz bekommen: `@Allow`
ändert nicht die Severity, sondern unterdrückt die **Ausgabe** an dieser Stelle.
**Bricht: nein** (additiv, aber eine echte Spracherweiterung, die vor der ersten Warnstufe stehen muss).
**Confidence: gelesen** (§12.1, §4.7, Guide `19-diagnostics.md:44-47`; Negativbefund der Spec-Suche).
**Hängt ab von**: Gebiet Diagnostik, Gebiet Attribute.
**Muss VOR**: SK-04, SK-05, SK-08, SK-12=D, SK-13, SK-17, SK-18, SK-28, SK-35.

### SK-25 — Was passiert mit bestehendem `.lyrbc` beim Sprung auf 5.0?

**Heute**: `docs/Bytecode.md` [gelesen]: unbekannter Major abgelehnt, unbekannter Minor toleriert
(`:113`); ein Minor „may add anything a module can decline to use" (`:115-118`); Präzedenz
4.0/3.6: „a 4.0 reader accepts every 3.x module unchanged" (`:22-25`). Format 4.0 umfasst auch
„fused compare-and-branch" (`:19`, `brcmp`/`brcmpk` `:745-746`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | 5.0 ist ein Format-Major: ein 5.0-Reader liest 4.x weiter, 4.x-Reader lehnen 5.0 ab | Der eigene Präzedenzfall; **Java** | Am billigsten — **wenn** dieses Gebiet keine Formatänderung braucht |
| B | 5.0 bleibt formatkompatibel (Minor-Bump) | — | Falsch, sobald ein Posten eine Opcode-**Bedeutung** ändert |
| C | Migrationswerkzeug für `.lyrbc` | — | Nur nötig, wenn A nicht geht |

**Empfehlung: A — mit einer Abgleichung gegen SK-26 (*Korrektur*).** Fassung 2 sagte „dieses
Gebiet braucht gar keine Formatänderung". Das gilt nur, wenn `checked` als **Lowering** kommt
(SK-26=C). SK-26 empfiehlt C „für die erste Fassung" und **A danach** — und A heißt geprüfte
Arithmetik-Opcodes. Die beiden Fassung-2-Empfehlungen waren nicht gegeneinander abgeglichen.
Abgeglichen: **neue Opcodes sind minor-fähig** (`:115-117`), sie sind also kein Major-Grund;
SK-25=A hält für beide SK-26-Wege, nur der Satz „keine Formatänderung" gilt nur für SK-26=C.
Was feststeht: die Bugs (c)/(c2)/(c3)/(c5) erzeugen heute Bytecode, das die Formatinvariante
verletzt; ein strengerer 5.0-Reader würde alte Release-Module ablehnen. **Argument für den
Sweep vor 5.0.**
**Bricht: A nein** (für Leser); fehlerhaft erzeugte 4.x-Module können unter einem strengeren
Reader fallen.
**Confidence: gelesen** (`Bytecode.md:19-25,113-123,543-556,619,745-746`; `STATUS.md:2183-2186`).
**Hängt ab von**: SK-01, SK-03, SK-16, **SK-26**; Gebiet Bytecode/VM.

### SK-26 — Was tut `checked` mit dem Optimierer und mit `comptime`?

**Heute**: `checked` gibt es nicht; beide Gegenseiten sind gebaut.

**Der Optimierer.** `ModuleLowerer` verifiziert die rohe Lowering, lässt `Inliner`,
`ScalarReplacement`, `Devirtualizer` laufen und verifiziert erneut (`ModuleLowerer.cs:551-603`,
[geprüft 2026-09-24]). Die Passliste in `IrPasses.cs:14-27` ist `Inline | ScalarReplacement |
Devirtualize`. *Korrektur*: **das ist nicht die ganze Optimierung** — außerhalb von `IrPasses`
gibt es die **Compare-and-Branch-Fusion** (`--no-fusion`, `CHANGELOG.md:208-210`; `brcmp`/
`brcmpk`, `Bytecode.md:745-746`, [geprüft]). Sie berührt keine Arithmetik, die Schlussfolgerung
hält: es gibt keine Umsortierung, Zusammenfassung oder Stärkereduktion auf `+ - *`. Die Sorge,
die trägt, ist der **Inliner**: bei lexikalischem `checked` (SK-03=A) prüft `checked { f(); }`
nicht in `f`; spleißt der Inliner `f` hinein, liegt `f`s Arithmetik **physisch** im
`checked`-Bereich. Die Geprüftheit muss an der **Instruktion** hängen, nicht an einer Region.

**`comptime`.** `comptime (127i8 + 1i8)` → `LYR-CLI0020` (c3); der Falter rechnet richtig
(`-128`) und kodiert 64 bit. `comptime (1 / 0)` → `LYR-CT0002` (`x12b`) — der Falter **meldet**
Paniken unter `comptime` schon heute.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Geprüftheit ist ein **Flag an der IR-Instruktion**; geprüfte Opcodes im Format | **C#/CLR** (`add.ovf`), **LLVM** (`llvm.sadd.with.overflow`) | Neue Opcodes (minor-fähig). Sauber und optimiererfest |
| B | Geprüftheit als **Region** in der IR | — | Jeder Pass muss die Regel kennen; fragil |
| C | Die Lowering löst `checked` sofort in Vergleich + Panik auf | Viele Compiler kleiner Sprachen | Keine Formatänderung, optimiererfest per Konstruktion. **Preis, jetzt gemessen als Proxy** (`y00`–`y02`): zehn ungeprüfte `r = a + b; a = r;` kosten **101 B** Bytecode über der leeren Funktion (~10 B/Op); dieselben zehn mit quelltextseitiger Überlaufprüfung (`if ((b > 0 && r < a) \|\| (b < 0 && r > a)) panic(…)`) **1 222 B** (~122 B/Op) — **~12×** in Bytes. Das ist eine **obere Schranke**: eine Compiler-Lowering (add, overflow-test, `brcmpk`, trap) käme ohne die doppelte Auswertung und den `panic`-Aufruf aus; die „~4×" aus Fassung 2 war eine Instruktionszahl-Schätzung ohne Messung und ist als solche zu lesen |
| — | **`comptime`-Teilfrage**: gibt eine Faltung in `checked` dieselbe Antwort wie zur Laufzeit? | Zig (`comptime` erzwingt die Prüfung) | Mit SK-13=A und SK-35=A fallen beide zusammen |

**Empfehlung: C für die erste Fassung, A wenn die Messung an echtem Code die 12× bestätigt.**
Die `comptime`-Antwort ist SK-13=A + SK-35=A. Der Kodierfehler (c3) gehört in den Sweep.
**Bricht: nein.**
**Confidence: gelesen** (`ModuleLowerer.cs:551-603`, `IrPasses.cs:14-27`, `CHANGELOG.md:208-210`,
`Bytecode.md:115-117,745-746`) + **gemessen** (`w32`, `w34`, `w35`, `x12b`, `y00`–`y02`).
**Hängt ab von**: SK-02, SK-03, SK-13, SK-25, SK-35, SK-40; Gebiet Bytecode/VM, Gebiet Metaprogrammierung.

### SK-27 — Wofür ist `uint` eigentlich da, und soll 5.0 es behalten?

**Heute**: `uint` ist laut §3.1 einer der drei Default-Typen [gelesen]. Gemessen — **mit
korrigierter Prämisse**:

| Befund | Beleg |
|---|---|
| ~~Kein Literal erzeugt je ein `uint`~~ — **falsch** (*Korrektur*): `let u: uint = 5;` und `let big: uint = 18446744073709551615;` laufen. Was fehlt, ist die Suffixform im **unannotierten** Kontext (`0xFFFFFFFFFFFFFFFF` ohne Annotation ist `SEM0001`) | **`x01`**, `x04b`, `w30`; `TypeFacts.IntLiteralFits` `Uint => !negative` |
| **Kein `Display`** — `println(u)` ist `LYR-SEM0028` | `w05`; `core.lyr:106-130` |
| **Indizieren kracht** | `w08` |
| **`??`-Adaption kracht** | `w01` |
| **`-u` wrappt still** | `w10` |
| **Kein `uintMax`**, kein `checkedAdd` für `uint` | `math.lyr` |
| Was es hat: Kontext-Adaption, `Equatable`, `Ordered`, `Hashable`, `Add/Sub/Mul/Div`, `fromUint`, `f"{u:D4}"` | `x01`; `core.lyr:319-337,406-424`; `string.lyr:27`; `w06`, `w29` |

Die realen Lücken sind also **Display, Index, `??`, `-u` und das Suffix im unannotierten
Kontext** — nicht „kein Literal". Ein Default-Typ, der sich nicht drucken lässt und mit dem man
nicht indizieren kann, ist trotzdem kein Default-Typ.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `uint` bleibt Default-Typ und wird **vollständig gemacht**: `u`-Suffix (SK-14), `Display` (SK-10), `uintMax` (SK-11), Index (SK-16=B), `-u` verboten (SK-17), `??` gefixt (Sweep) | **Go**, **Swift** (`UInt`) | Fünf kleine Posten, die ohnehin auf der Liste stehen |
| B | `uint` wird ein Breitentyp und heißt `uint64`; Defaults sind `int`/`float` | **Rust** (kein Default-`uint`), **Zig** | Ehrlicher Schnitt; `type uint = uint64;` als transparenter Alias hält alten Code am Leben |
| C | `uint` verschwindet | — | Abzulehnen: Hashes, Bitmasken, FFI |
| D | `uint` wird der Indextyp (SK-16=C) | **Rust** (`usize`) | Nur sinnvoll bei nicht-64-bit-Zielen |

**Empfehlung: A, B als die ehrliche Alternative, falls A nicht komplett geliefert wird.** **Der
Nachweis** (*korrigiert*): nach 5.0 muss
`let u: uint = 5; let v = 5u; println(u); xs[u]; let m = -u;` — also **der heute schon
funktionierende Adaptionsfall eingeschlossen** — entweder vollständig funktionieren bzw. dort
scheitern, wo es soll (`-u` als Fehler), oder der Typ darf nicht mehr „Default" heißen.
**Bricht: A nein; B major** (mit Alias minor).
**Confidence: gemessen** (`x01`, `x04b`, `w01`, `w05`, `w06`, `w08`, `w10`, `w29`, `w30`) +
**gelesen** (§3.1, `TypeFacts.cs:57`, `core.lyr`, `math.lyr`, `string.lyr`).
**Hängt ab von**: SK-10, SK-11, SK-14, SK-16, SK-17, SK-41.

---

### SK-28 — Muss der Wert eines suffigierten Literals in seine Suffix-Breite passen? *(neu)*

**Heute**: nein, und **niemand prüft es**. Gemessen (`x03a`–`x03f`, Kritiker `p33`, `p69`):
`200i8` → still `-56`; `0x80i8` → `-128`; `16777217f32` → still `16777216`; `300i8`, `256u8`,
`65536u16`, `99999999999i32` erreichen den **IR-Verifier** („defect in the compiler"), mit
`LYRIC_VERIFY_IR=0` übersetzen sie still zu `44`, `0`, `0`, `1215752191`. Kontrolle: der
unsuffigierte Weg prüft (`x03e`). Ursache gelesen: `TypeChecker.cs:1748` typisiert nach dem
Suffix allein; `WarningAnalyzer.CheckLiteralInRange:633` steigt bei Suffix aus; §1.3 und §3.1
verlangen für ein suffigiertes Literal nur „exactly its suffix's type", keinen passenden Wert.
Die Grenze `200` vs. `300` ist zufällig: `200` passt als `u8`-Bitmuster in 8 bit, `300` nicht —
der Verifier prüft Bitmuster, nicht Bereich.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Der Wert muss in den **Bereich** des Suffixtyps passen; `200i8` ist ein Sema-Fehler, `0x80i8` auch (Hinweis: `0x80u8 as int8`) | **Rust** (`overflowing_literals`, deny: „literal out of range for `i8`", mit genau diesem Hinweis für Hex), **Swift**, **Nim** | Bricht Code, der `0xFFi8` als Bitmuster schreibt — heute still `-1` |
| B | Dezimal muss in den Bereich passen, **Hex/Bin/Okt darf ein Bitmuster** der Breite sein (`0x80i8` = -128, `200i8` Fehler) | **C** (`0x80` als `signed char` ist implementierungsdefiniert, aber üblich), Zig-nah | Zwei Regeln nach Schreibweise; dafür der Bitmasken-Fall ohne Cast |
| C | Status quo, aufgeschrieben: das Suffix ist eine Reinterpretation der tiefen Bits | — | `200i8` = `-56` bleibt still; und Float-Suffixe runden weiter still (`16777217f32`) |
| D | Wie A, aber nur als Warnung | — | Halbe Antwort |

**Empfehlung: A, mit derselben Exaktheitsregel wie §3.1 für Float-Suffixe (`16777217f32` ist
ein Fehler wie `let g: float32 = 16777217;`).** Das ist die Regel, die die Sprache für den
unsuffigierten Weg schon hat; das Suffix darf nicht der stillere Weg sein. Unabhängig von der
Wahl: die Verifier-Meldung „defect in the compiler" für `300i8` ist ein **Sweep-Posten**, weil
sie den Benutzer an die falsche Stelle schickt. **Vierter Fall der (c)-Familie** (64-bit-Konstante
in schmalem Slot) — der Zwilling im Konformanzfall muss auch ein suffigiertes Literal enthalten.
**Bricht: minor** (heute akzeptierte Literale werden abgelehnt; praktisch nur kaputter Code
oder Bitmasken).
**4.x-Warnstufe**: 4.7 Warnung „literal 200 does not fit 'int8' (range -128..127)", 5.0 Fehler.
**Confidence: gemessen** (`x03a`–`x03f`) + **gelesen** (`TypeChecker.cs:1748`, `WarningAnalyzer.cs:633`, §1.3, §3.1).
**Hängt ab von**: SK-08, SK-09, SK-24; gehört mit (c)/(c2)/(c3) in denselben Sweep-Posten.

### SK-29 — Was ist `float as char`? *(neu)*

**Heute**: für **jeden** Wert `0` (`x02a`: `97.0`, `65f32`, `97.5`, `1e300` → 0; Kritiker
`p63b`: Surrogat `55296.0` → 0, keine Panik). Kontrollen: `65 as char` → `A` (`x02b`),
`1114112 as char` → `VM0012` (`x02c`). Ursache gelesen: `Interpreter.FloatToInt`, Tabelle
`_ => (0.0, 0.0)` (`Interpreter.cs:1471-1480`). §3.6 Punkt 1 verspricht für `char` drei Dinge,
die sich bei Float-Quelle widersprechen: „converts as its code point" (also trunkieren), „SATURATES
at the target's bounds" (auf `0x10FFFF`?) und „outside the scalar range panics `LYR-VM0012`".

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Trunkieren wie `float → int`, dann dieselbe Skalarprüfung wie `int → char` (Panik `VM0012` außerhalb, Surrogate eingeschlossen); NaN → Panik oder 0? | Lyrics eigene `int → char`-Regel | Konsistent mit `x02c`; `1e300 as char` wird eine Panik statt einer Sättigung — §3.6 muss den Sättigungssatz für `char` ausnehmen |
| B | Sättigen auf `0..0x10FFFF`, Surrogate auf den nächsten Skalar? | — | Erfindet eine Ordnung auf Codepunkten, die keine ist; kein Vorbild |
| C | `float → char` **verbieten**; nur `(f as int) as char` | **Rust** (nur `u8 as char`; alles andere über `char::from_u32`), **Swift** (kein Float-Initializer für `Unicode.Scalar`) | Ein Cast mehr an einer Stelle, die praktisch nie vorkommt; §3.6 bekommt eine Ausnahme in der „total over exactly these cases"-Liste |

**Empfehlung: C — und den Bug sofort schließen, unabhängig von der Wahl.** Ein Float ist kein
Codepunkt; die einzig sinnvolle Bedeutung geht über `int`, und dann soll der Benutzer das
schreiben. Bei SK-04=B (char ist keine Zahl) ist C ohnehin die einzige konsistente Antwort. Bis
dahin ist A der Bugfix mit der kleinsten Spec-Änderung.
**Bricht: C minor** (heute akzeptierter Code wird abgelehnt — er rechnet heute immer 0, ist also
kaputt); A nein.
**Confidence: gemessen** (`x02a`, `x02b`, `x02c`, `p63b`) + **gelesen** (`Interpreter.cs:1457-1485`, §3.6).
**Hängt ab von**: SK-04, SK-12, SK-03 (`checked as`). **Sweep-Posten.**

### SK-30 — Vorzeichenwechsel bei WACHSENDER Breite: sign-extend oder zero-extend? *(neu)*

**Heute**: sign-extend, dann reinterpretieren. `x07`: `-1i8 as uint64` = `18446744073709551615`;
`-1i8 as uint8` = `255`; `200u8 as int64` = `200`. §3.6 regelt nur das Narrowing („keeps the
low bits"); die Weitung signed→unsigned ist unspezifiziert [gelesen]. Die VM macht es, weil
`LyrValue` „signed types sign-extended, unsigned types zero-extended" hält
(`LyrValue.cs:14-16`) und der Cast danach nur den Tag tauscht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo aufschreiben: „als Wert weiten, dann als Bitmuster reinterpretieren" — `-1i8 as uint64` = `2⁶⁴−1` | **C**, **Rust** (`-1i8 as u64` = `u64::MAX`), **Go** (`uint64(int8(-1))` = `2⁶⁴−1`), **C#** (unchecked) | Eine Zeile in §3.6; alle Vergleichssprachen tun es so |
| B | Zero-extend: `-1i8 as uint64` = `255` (erst das Bitmuster der Quellbreite, dann weiten) | — (keine Vergleichssprache) | Überrascht jeden, der C kennt; und `-1i8 as uint8 as uint64` gäbe dasselbe wie A |
| C | Signed→unsigned bei wachsender Breite verbieten; nur über die gleiche Breite (`as uint8 as uint64`) | — | Eine Ausnahme in einer Regel, die §3.6 „total" nennt |

**Empfehlung: A — aufschreiben, nichts ändern.** Das Verhalten ist das einzige, das eine
Vergleichssprache hat, und Lyricpp (C++) würde es mit `static_cast` genauso bekommen. Die
Spec-Lücke ist ein Determinismus-Risiko für ein zweites Laufzeitsystem, kein Sprachfehler.
**Bricht: nein.**
**Confidence: gemessen** (`x07`) + **gelesen** (§3.6, `LyrValue.cs:14-16`).
**Hängt ab von**: SK-12, SK-03 (`checked as` muss wissen, ob `-1i8 as uint64` ein Bereichsfehler ist — unter A ja, wenn `checked` Wertetreue verlangt).

### SK-31 — Welchen Typ hat der Shift-Count? *(neu)*

**Heute**: den des linken Operanden. `x08a`: `a << m` mit `a: int8`, `m: int` → `LYR-SEM0003:
operator 'Shl' is not applicable to 'int8' and 'int'`; `p10`: `n: int32` ebenso; Kontrolle
`x08b`: `a << 3` (Literal adaptiert) → `8`. §6.1 regelt die Maskierung, nicht den Typ
[gelesen]; die Sema wendet die „SAME numeric type"-Regel von `+` auf `<<` an. Folge: jede
Schleife `x << i` auf einem Breitentyp braucht `i as int8`, obwohl der Count nach §6.1 ohnehin
auf `width−1` maskiert wird und ein `int8`-Count für ein 8-bit-Shift schon zu breit ist.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, aufgeschrieben | — (keine Vergleichssprache verlangt es) | Jede Schleife über Bits auf `uint8`/`uint32` kastet den Zähler |
| B | Der Count ist immer `int` (bzw. jedes Literal); der linke Operand bestimmt den Ergebnistyp | **C#** (Count ist `int`), **Java** | Eine Sonderregel für zwei Operatoren; das Maskieren bleibt am linken Operanden |
| C | Der Count darf **jeder** Ganzzahltyp sein | **Rust** (`Shl<u8> for i64` usw. für alle Paare), **Go** („the right operand … must be an integer type or an untyped constant") | Wie B, aber ohne den Cast auch für `uint8`-Zähler; ein `u64`-Count wird maskiert wie heute |

**Empfehlung: C.** `<<`/`>>` sind die zwei Operatoren, deren Operanden **verschiedene Rollen**
haben (Wert und Zählung); die „SAME type"-Regel von §6.1 ist für sie ein Kategorienfehler. C ist
die Antwort von Rust und Go, und die Maskierung (§6.1) bleibt unverändert. SK-16 stellt dieselbe
Frage für den Index; beide sollten dieselbe Antwort bekommen („jeder Ganzzahltyp").
**Bricht: nein** (additiv).
**4.x**: keine Warnstufe nötig; mehr wird akzeptiert.
**Confidence: gemessen** (`x08a`, `x08b`, `p10`) + **gelesen** (§6.1, `Interpreter.ShiftCount`).
**Hängt ab von**: SK-16 (dieselbe Regel), Gebiet Operatoren.

### SK-32 — Darf die Argumentreihenfolge entscheiden, ob ein Literal adaptiert? *(neu)*

**Heute**: ja. `x09a`: `fn f<T>(a: T, b: T)`, `f(1i8, 2)` läuft (`T = int8`, `2` adaptiert);
`x09b`: `f(2, 1i8)` ist `LYR-SEM0001: cannot assign 'int8' to 'int'`. Kontrolle `x20`:
`f(1i8, 2i8)` und `f(2, 3)` laufen. Die Inferenz bindet `T` am ersten Argument, das ein Literal
sein darf; ein späterer Nicht-Literal-Operand kann die Bindung nicht mehr revidieren. SK-23
sieht nur den Rückfluss aus dem Bindungsziel — dies hier ist Inferenz **innerhalb eines
Aufrufs**.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, aufgeschrieben: Bindung von links nach rechts, Literale sind `int` bis sie adaptiert werden | — | Für den Benutzer nicht herleitbar; die Meldung nennt die Ursache nicht |
| B | Zwei Durchgänge: erst alle **Nicht-Literal**-Argumente binden, dann Literale adaptieren | **Swift** (Constraint-Solver, Literale haben die niedrigste Bindungsstärke), **Kotlin** (Literal-Typen werden als „integer literal type" offen gehalten) | Kleiner Eingriff in die Inferenz; behebt nur diesen Fall |
| C | SK-06=C/D: das Literal hat einen offenen Typ; die Inferenz sieht keine Konstante `int` | **Zig**, **Haskell** | Löst dies, SK-23 und (c) zusammen |

**Empfehlung: B sofort (Sweep-nah), C mit SK-06.** B ist eine Zeile in der Inferenzreihenfolge
und macht `x09a`/`x09b` gleich, ohne dass SK-06 entschieden sein muss.
**Bricht: nein** (mehr wird akzeptiert).
**Confidence: gemessen** (`x09a`, `x09b`, `x20`).
**Hängt ab von**: SK-06, SK-23; Gebiet Generics/Inferenz.

### SK-33 — Rendert `{x:X}` auf einem Breitentyp das Bitmuster der Breite oder der 64 bit? *(neu)*

**Heute**: der 64 bit. `x10`: `f"{-1i8:X}"` = `FFFFFFFFFFFFFFFF`, `f"{-1i32:X}"` ebenso,
`f"{-1i8:B}"` = 64 Einsen; Kontrolle `f"{255u8:X}"` = `FF` (unsigned nullerweitert, unauffällig).
Mechanismus: `WidenForHelper` (`FunctionLowerer.cs:4909,4928,4932`) weitet vor dem Format-Helfer;
der Helfer kennt die Breite nicht. Dasselbe Leck wie SK-18, für Ganzzahlen.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo, dokumentiert: `X`/`B` zeigen die 64-bit-Zweierkomplementdarstellung | — | Ein `int8` druckt 16 Hexstellen; `{x:X2}` hilft nicht (das ist Mindestbreite) |
| B | Das Bitmuster der **deklarierten Breite**: `-1i8:X` = `FF`, `-1i32:X` = `FFFFFFFF` | **Rust** (`{:X}` auf `i8` gibt `FF`), **C#** (`(-1).ToString("X")` auf `sbyte` gibt `FF`), **Go** (`%X` auf `int8(-1)` gibt `-1` — Go zeigt das Vorzeichen; kein Vorbild für B) | Der Format-Helfer braucht die Breite als Parameter oder je eine Signatur pro Breite; mit SK-10 wird es eine Konformanz |
| C | Ganzzahlen werden vor dem Formatieren auf ihre Breite **maskiert**, aber nur bei `X`/`B`/`o`; `D`/`N` bleiben Wert | — | Zwei Pfade im Helfer |

**Empfehlung: B, zusammen mit SK-18 als *ein* Posten „Formatieren kennt die Breite".** Rust und
C# geben dieselbe Antwort, und sie ist die, die ein Benutzer mit `int8` meint: er hat die Breite
angegeben, um sie zu sehen.
**Bricht: nein** (Verhaltensänderung, CHANGELOG-Eintrag; Programme, die 16 Stellen erwarten,
sind selten und heute schon falsch für ihren Zweck).
**Confidence: gemessen** (`x10`) + **gelesen** (`FunctionLowerer.cs:4909-4932`).
**Hängt ab von**: SK-18, SK-34, SK-10.

### SK-34 — Ist das Format-Spezifikator-Vokabular spezifiziert? *(neu)*

**Heute**: nein. `stdlib/std/fmt.lyr:6-8`: „The specifier language is .NET's … It is passed to
the runtime unchanged" [gelesen]. `spec/11-stdlib-contract.md` nennt kein Spezifikator-Wort
[gelesen, Negativbefund]; §6.6 regelt nur, dass `{x:spec}` zu `std.fmt.formatXxx(value, "spec")`
wird. Und die **Default**-Ausgabe ohne Spezifikator ist ebenso unspezifiziert; gemessen `x11`:
`f"{1.0}"` → `1`, `f"{1e21}"` → `1e+21`, `f"{1e-7}"` → `1e-07` — .NETs kürzeste Round-Trip-Form
mit **zweistelligem** Exponenten und ohne `.0`. Ein zweites Laufzeitsystem (Lyricpp) müsste für
`N2`, `D4`, `X`, `E2`, `P1` und für `1e-07` **.NETs Verhalten nachbauen**, ohne dass es
irgendwo steht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo: „.NET's" bleibt die Definition | — | Das zweite Laufzeitsystem muss .NET-Dokumentation lesen; jede .NET-Version kann die Ausgabe ändern (sie hat es bei 3.0 getan) |
| B | Ein **eigenes, kleines Vokabular** in `spec/11` normieren: `D`, `N`, `F`, `E`, `X`, `B`, `P` mit Breite/Präzision, und die Default-Ausgabe (kürzeste Round-Trip-Darstellung, `1` oder `1.0`?, Exponentform ab wann, wie viele Exponentstellen) | **Rust** (`std::fmt` ist in der Sprachreferenz definiert), **Go** (`fmt`-Verben sind Paketspezifikation), **C#** selbst (die Format-Strings sind dokumentiert, aber nicht sprachnormativ) | Ein Spec-Kapitel; `std.fmt` prüft den Spezifikator statt ihn durchzureichen; Konformanzfälle |
| C | B, aber die Default-Ausgabe wechselt auf ein sprachunabhängiges Muster (`1.0` statt `1`, `1e-7` statt `1e-07`) | Rust (`1.0` → `1`, `1e-7` → `0.0000001`; Go `%v` → `1e-07`) | Verhaltensänderung sichtbar in jedem Programm, das Floats druckt |

**Empfehlung: B, ohne C.** Die Default-Ausgabe ändern hieße jedes Golden-File anfassen; sie
**normieren** kostet einen Absatz und macht Lyricpp erst baubar. Das Vokabular selbst ist klein
genug, dass es nicht „.NET" heißen muss.
**Bricht: B nein** (nur Aufschreiben; Spezifikatoren, die heute per Zufall durch .NET gehen —
`C` für Währung, `G`, `R` — würden ungültig; das ist gewollt).
**Confidence: gelesen** (`fmt.lyr:6-8`, `spec/11`, §6.6) + **gemessen** (`x11`).
**Hängt ab von**: SK-18, SK-33; Gebiet Strings/Formatierung, Gebiet Laufzeit (Lyricpp).

### SK-35 — Werden konstant faltbare Paniken zur Compile-Zeit gemeldet? *(neu)*

**Heute**: nein, außer unter `comptime`. `x12a`: `let x = 1 / 0;` kompiliert und paniert zur
Laufzeit (`VM0002`); `x12b`: `comptime (1 / 0)` ist `LYR-CT0002` — der Falter *kann* es also
melden; `x12c`: `intMin / -1` kompiliert und wrappt (korrekt nach §3.2, keine Panik).
Dazu gehört `1114112 as char` (konstant, paniert `VM0012` zur Laufzeit, `x02c`). SK-13 fragt
nur nach Überlauf.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Fehler: ein konstanter Ausdruck, der zur Laufzeit garantiert paniert, ist ein Compile-Fehler (`1 / 0`, `1 % 0`, `1114112 as char`, `0xD800 as char`) | **C#** (CS0020 „Division by constant zero"), **Rust** (`unconditional_panic`, deny), **Go** („division by zero" bei konstantem Divisor) | Bricht nur Programme, die garantiert abstürzen |
| B | Warnung | — | Ohne Bruch; halbe Antwort |
| C | Status quo | — | `comptime` und Nicht-`comptime` geben verschiedene Antworten auf denselben Ausdruck |

**Empfehlung: A, zusammen mit SK-13 (Überlauf) als *ein* Posten „konstante Ausdrücke, die
nie richtig sind".** Mit SK-06=C/D entsteht der Fehler an der Materialisierung von selbst.
Wichtig: **nicht** `intMin / -1` — das wrappt per §3.2 und ist keine Panik.
**Bricht: major** (formal), praktisch nur garantiert kaputten Code.
**4.x-Warnstufe**: 4.7 Warnung „this expression always panics: division by zero", 5.0 Fehler.
**Confidence: gemessen** (`x12a`, `x12b`, `x12c`, `x02c`).
**Hängt ab von**: SK-13, SK-06, SK-24, SK-26 (`comptime`-Teilfrage).

### SK-36 — Was bedeutet „layouts" bei Breitentypen, wenn der VM alles auf 64 bit weitet? *(neu)*

**Heute**: §3.1 begründet die Breitentypen mit „layouts and boundaries" [gelesen]. Im Referenz-VM
ist jeder Wert eine `LyrValue` aus `ulong Bits` + `object? Ref` (`LyrValue.cs:19-22`), „Integers
are always widened to 64 bits" (`:14-16`) [gelesen]. Ein `int8[]` belegt also pro Element
16 Bytes wie ein `int[]` (behauptet für die Array-Repräsentation — `Ref` als `LyrValue[]` — aus
`LyrValue` gefolgert, nicht am Array gemessen). Wo ein Layout **wirkt**: in Bytecode-Konstanten
(der Verifier prüft Bitmuster pro Breite, c3/c5), an der Native-Grenze (Signaturen tragen
`i8`/`u32`), und in der Arithmetik (Wrapping auf der Breite, `w24`). **Nicht** im Speicher.
SK-01 rechnet den „Layout-Preis" von Option B mit dieser Aufgabe; ohne Antwort ist er hohl.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Aufschreiben: Breitentypen sind **Wertebereichs- und Grenztypen**, keine Speicherlayout-Typen; die Repräsentation ist Sache des Laufzeitsystems | **Java** (`byte[]` packt in HotSpot, aber die JLS verspricht kein Layout), **Wren/Lua** (ein Zahltyp, kein Layout) | Ehrlich; `int8[]` für Bildpuffer bleibt 8× zu groß, und niemand hat es versprochen |
| B | Packende Arrays für Breitentypen: `int8[]` ist ein Byte-Array im VM | **C#**, **Go**, **Rust** (Layout ist Sprachvertrag), **Zig** | Eine zweite Array-Repräsentation im VM (`byte[]`, `int[]`, …) und im Format (typisierte Array-Konstanten); großer VM-Eingriff, aber der einzige Weg zu Bild-/Netzpuffern ohne Host-Objekt |
| C | Ein eigener Puffertyp in der Bibliothek (`Bytes`, host-gestützt) für den Fall, der Layout braucht | **Java** (`ByteBuffer`), **JS** (`TypedArray`) | Ein zweiter Weg neben `uint8[]` — Rule-2-Frage, aber die JS-Antwort zeigt, dass es geht |

**Empfehlung: A jetzt, B als Frage für das Gebiet Bytecode/VM.** Für dieses Gebiet reicht, dass
§3.1 nicht mehr „layouts" verspricht, was die Sprache nicht hält. Das Wort „boundaries" trägt
die Breitentypen allein: Wrapping, Bereichsprüfung, Native-Signaturen.
**Bricht: A nein.**
**Confidence: gelesen** (§3.1, `LyrValue.cs:14-22`); Array-Größe **behauptet**.
**Hängt ab von**: SK-01 (Preis von B dort), Gebiet Bytecode/VM, Gebiet FFI.

### SK-37 — Adaptiert ein Literal an ein `opaque type` über numerischem Underlying? *(neu)*

**Heute**: nein, in keiner Form. `x13a`: `opaque type Id = uint8; let i: Id = 5;` →
`SEM0001: cannot assign 'int' to 'Id'`; `x13b`: `5 as Id` → `SEM0006` mit dem **irreführenden**
Hinweis „give 'int' the conformance :: [Into<Id>]"; nur `(5 as uint8) as Id` geht (`x13c`).
Alles im Deklarationsmodul, wo §3.5 den inward-Cast erlaubt. Die Adaptionsliste in §3.1 kennt
keine opaque-Position; `as` adaptiert das Literal nicht vor dem Cast.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo; die Meldung in `x13b` wird korrigiert („write `(5 as uint8) as Id`") | — | Zwei Casts für eine Konstante; der Hinweis ist ein Sweep-Posten unabhängig von der Wahl |
| B | `as` auf ein **Literal** adaptiert es zuerst an das Underlying: `5 as Id` funktioniert im Deklarationsmodul | — (Rust hat kein `opaque`; `Id(5)` über den Newtype-Konstruktor ist der Analogfall, und dort adaptiert `5` an `u8`) | Eine Regel in `CheckCast`: das Literal sieht das Underlying als Kontext. Klein |
| C | Das Deklarationsmodul darf ein Literal **direkt** an `Id` binden (`let i: Id = 5;`) | **Haskell** (`newtype` + `deriving Num` — `fromInteger` geht durch), **Swift** (Newtype mit `ExpressibleByIntegerLiteral`) | Eine opaque-Position in der §3.1-Liste — nur im Deklarationsmodul, sonst wäre das Privileg umgangen. Das ist SK-15f in seiner kleinsten Form |
| D | SK-06=D: `FromLiteral` als Protokoll; `Id` erklärt es | **Haskell**, **Swift** | Löst es allgemein, aber wartet auf SK-06 |

**Empfehlung: B sofort (klein, ohne Spec-Listenänderung), C mit SK-15f/SK-06=D.** B ist
keine neue Adaptionsposition, sondern die Regel „ein Literal unter `as` sieht das Cast-Ziel
bzw. dessen Underlying" — dieselbe, die `65 as char` schon heute stillschweigend nutzt. Der
Hinweis in `x13b` ist in jedem Fall ein Sweep-Posten.
**Bricht: nein** (additiv).
**Confidence: gemessen** (`x13a`, `x13b`, `x13c`) + **gelesen** (§3.1, §3.5, §3.6).
**Hängt ab von**: SK-05, SK-06, SK-12, SK-15f; Gebiet Werte/opaque.

### SK-38 — Was tun `-0.0` und NaN-Varianten im `Equatable`/`Hashable`/`Ordered`-Vertrag? *(neu)*

**Heute**: `0.0 == -0.0` ist `true` (`x14`), `-0.0` druckt `-0`, `1.0 / -0.0` = `-Infinity` —
verschiedene Bitmuster, gleiche `==`-Antwort. SK-21=B (Bitmuster-Funktion) und SK-20=A
(`total_cmp`) ohne Normalisierung würden: (1) `hash(0.0) ≠ hash(-0.0)` bei `equals` = true —
Vertragsbruch `equals ⇒ gleicher Hash`; (2) `compare(-0.0, 0.0)` = -1 bei `equals` = true —
derselbe Widerspruch wie bei NaN, nur ohne NaN. Und NaN hat 2⁵²−1 Bitmuster, die alle „NaN" sind.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Normalisieren vor Hash und Vergleich**: `-0.0 → +0.0`, jedes NaN → kanonisches NaN; `total_cmp` läuft auf dem normalisierten Muster | **Rust** (`f64::total_cmp` normalisiert nicht — dort ist `-0 < +0` gewollt, weil `Ord` für `f64` gar nicht existiert), **Swift** (`Double.hashValue` normalisiert `-0.0` zu `0.0`: „hash(into:)" behandelt sie gleich), **Java** (`Double.hashCode` unterscheidet `-0.0` und `0.0` — und `Double.equals` auch, anders als `==`; bewusster Bruch mit IEEE) | Eine Normalisierungsfunktion; `-0.0` und `0.0` sind dann in `Map` derselbe Schlüssel — was `==` schon sagt |
| B | `PartialEq`/`PartialOrd`-Schnitt: `float` bekommt keinen `Hashable` und keinen totalen `Ordered`; `Map<float,_>` bleibt verboten | **Rust** (`f64: !Eq, !Hash, !Ord`) | Ehrlich, aber `sort` auf `float[]` bleibt unmöglich ohne Komparator; und SK-20 will gerade sortieren |
| C | `equals` wird **bitweise** (`-0.0 != 0.0`, `nan == nan`), damit Hash und Ordnung konsistent sind; `==` bleibt IEEE | **Java** (`Double.equals`) | Zwei Gleichheiten auf demselben Typ — Rule-2-Verstoß, und `==` und `equals` widersprechen sich sichtbar |

**Empfehlung: A.** `equals` bleibt IEEE (`0.0 == -0.0`, `nan != nan`), `hash` und `compare`
normalisieren `-0` und NaN, `compare` ist danach `total_cmp` auf dem normalisierten Muster —
mit einer **dokumentierten** Restabweichung: `compare(nan, nan)` = 0 bei `equals(nan, nan)` =
false. Das ist der Rust-`total_cmp`-Kompromiss (dort ausdrücklich so dokumentiert) plus die
Swift-Normalisierung. B ist die reinere Modellierung und gehört in die Interfaces-Runde (SK-20=B).
**Bricht: minor** (nur SK-20/SK-21 sind betroffen, die selbst neu sind).
**Confidence: gemessen** (`x14`, `w12`) + **gelesen** (`core.lyr:243-259`).
**Hängt ab von**: SK-20, SK-21, SK-10; Gebiet Interfaces, Gebiet Collections (`Map`, `sort`).

### SK-39 — Ist Float-Arithmetik über Laufzeitsysteme hinweg deterministisch? *(neu)*

**Heute**: §3.2 friert Determinismus für **Ganzzahlen** ein („identical on every platform");
§6.1 sagt „float per IEEE 754" — mehr nicht [gelesen, Negativbefund für FMA, Zwischenrundung,
x87, transzendente Funktionen]. Gemessen ist nur, dass der .NET-VM `float32` echt in binary32
rechnet (`h_f32`). Sobald Lyricpp (C++) dasselbe `.lyrbc` ausführt, sind offen: (1)
**FMA-Kontraktion** (`a*b+c` mit einer Rundung statt zwei — GCC/Clang tun es mit `-ffp-contract=fast`
auf ARM und x86-FMA per Default), (2) **x87-Extended-Precision** auf 32-bit-x86 (Double-Rounding),
(3) **`float32`-Zwischenrundung** (rechnet die VM `f32 + f32` in binary32 oder in f64 mit
Abschlussrundung — für `+ - * /` ist das nach IEEE gleichwertig, für verkettete Operationen und
`fma` nicht), (4) **transzendente Funktionen** (`sin`, `pow`, `exp` — .NET ruft die C-Runtime,
C++ ebenso, beide sind nicht korrekt gerundet und unterscheiden sich um ULPs), (5)
**NaN-Bitmuster** (Vorzeichen, Payload) nach Operationen.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo: Floats sind „IEEE 754" und sonst Sache der Plattform | **C**, **C++** (beide erlauben FMA-Kontraktion und Extended Precision per Default) | Zwei Laufzeitsysteme dürfen verschiedene Bits liefern; Konformanzfälle mit Float-Ausgabe sind dann nicht plattformübergreifend |
| B | **Strikt**: jede Operation ist genau eine IEEE-Rundung auf der Operandenbreite, keine Kontraktion, kein Extended; transzendente Funktionen bleiben ausgenommen (dokumentiert „±1 ULP, plattformabhängig") | **Java** (seit 17 immer `strictfp`), **C#**/.NET (kein FMA ohne `Math.FusedMultiplyAdd`, kein x87 in RyuJIT seit .NET Core — behauptet), **Rust** (keine Kontraktion ohne `mul_add`), **Go** (Kontraktion **erlaubt** — Go ist hier Gegenpol) | Lyricpp muss mit `-ffp-contract=off` bauen und x87 meiden (auf x86-64 ohnehin SSE). Praktisch kostenlos, muss nur aufgeschrieben werden |
| C | B plus korrekt gerundete transzendente Funktionen (CORE-MATH-artig) | — (keine Mainstream-Laufzeit) | Eigene libm im Runtime; unverhältnismäßig |

**Empfehlung: B — aufschreiben, was der .NET-VM heute tut, als Vertrag für jedes Laufzeitsystem;
transzendente Funktionen ausdrücklich ausnehmen.** §3.2s Determinismus-Argument gilt sonst nur
für die Hälfte der Zahlen. Und der Konformanzfall, der `0.1f32 + 0.2f32` druckt, ist heute nur
zufällig portabel.
**Bricht: nein** (Ist-Verhalten des Referenz-VM wird normativ).
**Confidence: gelesen** (§3.2, §6.1 Negativbefund) + **gemessen** (`h_f32`, nur .NET); Aussagen
über .NET/RyuJIT-Kontraktion **behauptet**.
**Hängt ab von**: Gebiet Laufzeit/Lyricpp, Gebiet Bytecode; SK-18/SK-34 (Ausgabe).

### SK-40 — Welche Operationen zählt ein `checked`-Block? *(neu)*

**Heute**: kein `checked`. SK-03 stellt vier Unterfragen, nicht diese. Die Kandidaten, mit
Ist-Verhalten:

| Operation | Heute | Zählt sie in `checked`? |
|---|---|---|
| `+ - *` | wrappt (§3.2, `w24`) | ja, unstrittig |
| `min / -1` | wrappt zu `min` (`x12c`, §3.2) | **Rust: ja** (Debug-Panik „attempt to divide with overflow"), **C#: ja** (`OverflowException` in `checked`) → ja |
| `-min`, `absInt(min)` | unverändert `min` (§3.2, `w33`) | Rust: ja (`neg` mit Overflow paniert in Debug); C#: `-int.MinValue` in `checked` wirft → ja. **`absInt` ist eine Bibliotheksfunktion** — ein lexikalisches `checked` sieht sie nicht (SK-03 Unterfrage 1) |
| `<<` mit Bitverlust | maskiert, nie Panik (§6.1) | **Rust prüft nur den Count** (`1i8 << 8` paniert in Debug, `0x7F << 1` nicht); **C#: nie** → **nein**, Shift bleibt ungeprüft; der Count ist maskiert und damit auch nicht prüfbar |
| `++`, `+=`, `--`, `-=` | wrappt (`x18`: `127i8++` → `-128`) | ja — sie sind `+`/`-` |
| Range-Iteration bis zum Typmaximum | terminiert korrekt (`x15`: `0u8..=255u8` → 256; `p18`) | nein — die Schleife rechnet intern nicht über das Maximum hinaus; muss aufgeschrieben werden |
| `as` von Float (NaN/±Inf/außerhalb) | sättigt still (`x17`) | Rust: `as` ist **nie** geprüft (Sättigung ist die Definition); C#: `(sbyte)double.NaN` in `checked` wirft → strittig |
| `as` int→int narrowing | tiefe Bits (`w23`) | C#: `checked((sbyte)300)` wirft → ja, wenn SK-03 Unterfrage 3 „ja" sagt |
| `as` int→char | paniert schon heute (`x02c`) | ändert nichts |
| `as` float→char | kaputt (SK-29) | erst nach SK-29 |
| Lambdas, die **im** Block definiert werden | — | **C#: ja**, der Kontext ist lexikalisch und schließt Lambda-Rümpfe ein; Rust: n/a (kein Block) |

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Genau die Operationen, deren mathematisches Ergebnis nicht darstellbar ist**: `+ - *`, `/` (nur `min / -1`), unäres `-`, `++`/`--`/`+=`…, `as` bei Wertverlust (Narrowing, Float-Sättigung, signed↔unsigned außerhalb des Bereichs); **nicht** Shifts, nicht Bitoperatoren, nicht Range-Iteration; Lambdas im Block eingeschlossen | **C#** (`checked` deckt `+ - * / -x ++ --` und explizite Konversionen; Shifts nie) | Die C#-Liste ist 20 Jahre erprobt; `as` muss vorher definiert sein (SK-29, SK-30) |
| B | Wie A ohne `as` | **Rust** (Debug-Overflow-Checks decken Arithmetik, nie `as`) | Der Cast bleibt die Falle im Block |
| C | Wie A plus Shift-Bitverlust (`0x7Fi8 << 1` paniert) | — (Zig: `<<` ist bei Bitverlust illegal, `<<|` sättigt) | Shift-Semantik bekommt einen zweiten Modus; das Maskieren bleibt — der Count wird nie geprüft |

**Empfehlung: A, mit der C#-Liste als Vorlage und dem Rust-Ausschluss für Shifts.** Die Frage,
ob `absInt(min)` zählt, beantwortet SK-03 Unterfrage 1 (lexikalisch: nein, es ist ein Aufruf) —
und das macht `checkedAbs` in der Bibliothek (SK-10/SK-11) zur einzigen Antwort für Funktionen.
**Bricht: nein** (Teil des neuen Konstrukts).
**Confidence: gemessen** (`x12c`, `x15`, `x17`, `x18`, `w23`, `w33`) + **gelesen** (§3.2, §6.1).
**Hängt ab von**: SK-03, SK-29, SK-30, SK-26.

### SK-41 — Soll ein Literal über `intMax` in unannotiertem Kontext automatisch `uint` werden? *(neu)*

**Heute**: nein. `x04a`: `let x = 9223372036854775808;` → `LYR-SEM0001: integer literal does not
fit 'int' — annotate the uint type that holds it`; `x04b`: `0xFFFFFFFFFFFFFFFF` ebenso.
§3.1: „an unannotated context types the literal `int`, so a magnitude beyond `int`'s range is an
error there — not a bit reinterpretation" [gelesen] — also **entschieden**, mit Begründung
(`WarningAnalyzer.cs:622-630`: „used to reach the lowering as raw bits and reinterpret to a
negative number (found by the 2.0.1 audit)"). Mit Annotation funktioniert es (`x01`). SK-14
diskutiert das Suffix, nicht diesen Fall.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo: Annotation oder (nach SK-14) Suffix `u` ist Pflicht | **Rust** (`let x = 0xFFFF_FFFF_FFFF_FFFF;` ist ein Fehler `overflowing_literals` für den Default `i32`; man schreibt `u64`), **Go** (untypisierte Konstante über `int` ist beim Binden ein Fehler) | Eine Meldung, die schon heute den Weg nennt |
| B | Typ nach **Größe**: passt es nicht in `int`, wird es `uint` | **Kotlin** (`Int` → `Long` nach Größe; aber nie unsigned — `4294967296u` braucht das Suffix), **C** (`0xFFFFFFFFFFFFFFFF` ist `unsigned long long` nach der Suffix-Regel für Hex) | Ein Literal wechselt still den Typ; `let m = 0xFFFFFFFFFFFFFFFF; let n = m + 1;` rechnet dann unsigned, ohne dass es dasteht — und Kotlin ist kein Vorbild für die unsigned-Hälfte |

**Empfehlung: A, mit SK-14=A (`u`-Suffix) als Ergonomie-Ergänzung.** Die heutige Meldung ist
die beste Diagnose des Gebiets; ein stiller Typwechsel wäre genau der „bit reinterpretation"-Fall,
den §3.1 ausschließt. C ist das einzige echte Vorbild für B, und Cs Literal-Typregeln sind nicht
das, was man kopiert.
**Bricht: nein.**
**Confidence: gemessen** (`x04a`, `x04b`, `x01`) + **gelesen** (§3.1, `WarningAnalyzer.cs:622-638`).
**Hängt ab von**: SK-14, SK-27.

### SK-42 — Bleiben Float-Literale als Patterns erlaubt, und was tut ein NaN-Scrutinee? *(neu)*

**Heute**: erlaubt und matchend mit exakter Gleichheit. `x16a`: `match (1.0) { 1.0 => 1, _ => 2 }`
→ `1`; `match (nan) { 1.0 => 1, _ => 2 }` → `2` (NaN matcht kein Literal; ein `nan`-Pattern gäbe
es nicht, weil `nan == nan` false ist). Kritiker `p20` bestätigt.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Bleibt erlaubt, aufgeschrieben: Pattern-Gleichheit ist `==`, ein NaN-Scrutinee fällt immer in `_` | **Rust** (Verbot 2018 angekündigt, `illegal_floating_point_literal_pattern`, in 1.83/2024 **zurückgenommen** — behauptet), **Swift** (`case 1.0:` erlaubt), **C#** (`case 1.0:` erlaubt) | `case 0.1 + 0.2` matcht `0.3` nicht — dieselbe Falle wie `==` (SK-15g); die Warnung dort deckt sie mit |
| B | Verbieten: Floats nur über Guards (`x if x == 1.0`) | Rust 2018–2024 (aufgegeben) | Der Verlust an Bequemlichkeit hat Rust zur Umkehr gebracht |
| C | Erlaubt, aber Warnung bei nicht exakt darstellbaren Literalen (`0.1`) im Pattern | — | Dasselbe wie SK-15g als Warnung, nur im Pattern |

**Empfehlung: A, mit der SK-15g-Warnung als gemeinsamem Posten.** Rust hat den Versuch B
gemacht und beendet; die Sprache soll das Ergebnis übernehmen, nicht den Umweg.
**Bricht: nein.**
**Confidence: gemessen** (`x16a`, `p20`); Rust-Geschichte **behauptet**.
**Hängt ab von**: SK-15g, SK-38; Gebiet Pattern Matching.

---

## 4. Was wir übernehmen sollten

Nach Wert geordnet, nicht nach Aufwand.

1. **Zigs `comptime_int` bzw. Haskells `fromInteger`** (SK-06=C/D, mit SK-23, SK-32). Erledigt
   die kaputte `??`-Adaption, die fehlende Tupel-Position, den stillen Konstantenüberlauf, die
   Wechselwirkung mit der Inferenz **in beiden Richtungen** (SK-23, SK-32) und die Frage, wo
   Literale — auch suffigierte (SK-28) — geprüft werden. Haskells Variante öffnet die Tür für
   eigene Typen (SK-15f, SK-37).
2. **Swifts numerische Protokollhierarchie** (SK-10, SK-11, SK-22). Gemessener Preis ~259 B
   Bytecode pro Instanziierung. Die einzige Lücke, die ein Benutzer für die `std.core`-Interfaces
   nicht selbst schließen kann (`x19`; eigene Interfaces gehen, `x06`).
3. **Rusts/Swifts `char`** (SK-04=B) — mit der Begründung, warum der dokumentierte Grund
   (`TypeFacts.cs:9-19`) nicht mehr bindet: `c as int` hat ihn ersetzt. Und **Rusts/Swifts
   Verbot von `float → char`** (SK-29=C), das heute für jeden Wert 0 liefert.
4. **Rusts/Swifts Bereichsregel für suffigierte Literale** (SK-28=A): `200i8` ist ein Fehler,
   nicht `-56`.
5. **Gos Konstantenregel für Floats** (SK-08=C): runden ja, überlaufen nein.
6. **Rusts/Swifts/.NETs kürzeste Round-Trip-Ausgabe pro Breite** (SK-18) **und Rusts/C#s
   Bitmuster-Ausgabe pro Breite** (SK-33): `f"{-1i8:X}"` druckt `FF`. Dazu **Rusts/Gos
   Normierung des Format-Vokabulars** (SK-34), damit Lyricpp es bauen kann.
7. **Rusts `total_cmp` und `to_bits` mit Swifts `-0`-Normalisierung** (SK-20, SK-21, SK-38).
   Ohne die Normalisierung verletzt der Bitmuster-Hash den `Equatable`-Vertrag bei `0.0`/`-0.0`.
8. **Rusts `#[allow]`** (SK-24) — vier Zeugen (Rust, C#, Java, Kotlin), nicht fünf.
9. **Swifts Konversionsvokabular in der Bibliothek** (SK-12=C) — nach SK-29/SK-30, damit `as`
   selbst definiert ist.
10. **C#s `checked` als lexikalischer Block mit Panik und C#s Operationsliste** (SK-02=A,
    SK-03=A, SK-40=A) — nach SK-10/SK-11; Geprüftheit an der Instruktion oder sofort aufgelöst
    (SK-26; gemessene obere Schranke 12× Bytes für die Auflösung).
11. **Die Diagnosetexte** (SK-09) nach dem Muster, das Lyric für den unannotierten Überlauf
    schon hat (`x04a`).
12. **Gos/C#s Indexregel und Rusts/Gos Shift-Count-Regel** (SK-16=B, SK-31=C): jeder Ganzzahltyp
    indiziert, jeder Ganzzahltyp zählt einen Shift. *Korrigiert*: Go ist bei beiden auf der
    „jeder Typ"-Seite, Option A in SK-16 hat kein Vorbild mehr.
13. **C#s CS0020 / Rusts `unconditional_panic`** (SK-35): `1 / 0` konstant ist ein Fehler.
14. **Javas `strictfp`-Linie** (SK-39=B): eine Rundung pro Operation, keine Kontraktion — als
    Vertrag für Lyricpp.

---

## 5. Konflikte

**Gegen CONTRIBUTING Rule 2 („ein Mechanismus pro Konzept"):**

- **SK-02/SK-03, `checked { … }`**: eine zweite Antwort auf „was tut `+`", lexikalisch
  umgeschaltet. Verteidigung: ein Modus auf dem einen Mechanismus, nicht ein zweiter
  Operatorsatz, und §3.2 hat ihn zugesagt. **SK-02=B (`&+`) ist ein klarer Rule-2-Bruch.**
  SK-02=D (Ada) ist der einzige Weg ohne zweiten Mechanismus.
- **SK-21=A (`as` reinterpretierend)** wäre der schwerste Rule-2-Bruch, weil still. Deshalb B.
- **SK-12=B, `Into` verliert `as`**: entfernt einen Mechanismus — auf Rule 2s Seite.
- **SK-24=A (`@Allow`)**: zweiter Attributmechanismus neben `@Deprecated`, aber gegensätzliche
  Aufgabe (melden vs. schweigen) — kein Verstoß, wenn `@Allow` die Ausgabe und nicht die
  Severity unterdrückt (§12.1).
- **SK-36=C (eigener Puffertyp neben `uint8[]`)** wäre ein zweiter Weg für dasselbe Konzept;
  deshalb A jetzt und B als VM-Frage.
- **SK-38=C (bitweises `equals` neben IEEE-`==`)** wäre zwei Gleichheiten auf einem Typ;
  deshalb A.
- **SK-31=C** und **SK-16=B** geben demselben Konzept („der zweite Operand ist eine Zählung/
  Position, nicht ein Wert desselben Typs") dieselbe Antwort — das ist Rule 2 **eingehalten**;
  A in beiden wäre ebenfalls konsistent, nur A/B gemischt nicht.

**Gegen andere Gebiete:**

- **SK-10/SK-11/SK-22 hängen am Gebiet Generics/Interfaces** (statische Member, `Self`,
  bedingte Konformanz). Ohne sie ist SK-10 nur Option A.
- **SK-23 und SK-32 gehören mit dem Generics-Gebiet zusammen entschieden.**
- **SK-24 gehört Diagnostik und Attributen** — vor der ersten Warnstufe (jetzt neun geplante).
- **SK-18, SK-19, SK-33, SK-34 gehören halb ins Gebiet Strings/Formatierung.**
- **SK-20, SK-38 berühren Collections** (`sort`, `Map<float,_>`) und Interfaces.
- **SK-25, SK-26, SK-36, SK-39 berühren Bytecode/VM und Lyricpp** — Formatversion,
  Optimierer, Speicherlayout, Float-Vertrag.
- **SK-29, SK-30 berühren das Gebiet Operatoren** (`as`) und **SK-31** ebenso (`<<`).
- **SK-37 berührt das Gebiet Werte/opaque.**
- **SK-35 berührt Metaprogrammierung** (`comptime` meldet heute schon, `x12b`).
- **SK-42 berührt Pattern Matching.**
- **SK-04=B berührt `std.string` fast nicht** — nur den veralteten Kommentar `:378`.

**Gegen die Spec selbst:**

- §3.2 friert das Überlaufverhalten ein. SK-02=A respektiert das; B, C, D brechen den Satz.
- §3.1 gibt Breitentypen die Aufgabe „layouts and boundaries" — **„layouts" hält der
  Referenz-VM nicht** (SK-36); §3.5 „never appears in a diagnostic" steht gegen SK-01=B.
- §3.6 verspricht für `char` drei Dinge, die sich bei Float-Quelle widersprechen (SK-29), und
  schweigt zur signed→unsigned-Weitung (SK-30).
- §1.3/§3.1 verlangen für suffigierte Literale keinen passenden Wert (SK-28) — die Spec
  **erlaubt** heute `200i8` = `-56`.
- §6.1 regelt beim Shift die Maskierung, nicht den Count-Typ (SK-31).
- §6.2 verspricht Vergleiche „from ONE method"; `Ordered<float>.compare` und
  `Equatable<float>.equals` widersprechen sich (SK-20), und `-0.0` verschärft es (SK-38).
- §12.1 kennt keine Unterdrückung (SK-24).
- §3.2 friert Determinismus nur für Ganzzahlen ein (SK-39).
- `spec/11` normiert kein Format-Vokabular (SK-34).
- **Spec-first ist an fünf Stellen verletzt** — *Korrektur*: Fassung 2 zählte sechs und nannte
  `LYR-SEM0007` (String nicht indizierbar); die steht in **§3.3**. Die fünf: (1) `char` als
  Ganzzahl in der Arithmetik (`TypeFacts.IsInteger`, kommentiert), (2) die Literal-Adaption an
  `char` (`TypeFacts.IntLiteralFits`), (3) die fehlende Exaktheitsprüfung für Float-Literale
  (`LiteralAdaptsTo`), (4) der Indextyp `int` (nur der Verifier weiß es), (5) unäres `-` auf
  unsigned. **Dazu kommen Lücken, die keine Verstöße sind, weil auch der Compiler nichts
  entschieden hat**: Shift-Count-Typ (SK-31), signed→unsigned-Weitung (SK-30), Float-Determinismus
  (SK-39), Format-Vokabular (SK-34).

**Sofort, ohne v5-Entscheidung (Sweep-Posten):**

- **Die Literal-/Slottyp-Familie** ist **ein** Posten mit **vier** Fällen: `??`-Adaption (c),
  Index (c2), `comptime`-Faltung (c3), **suffigierte Literale** (c5: `300i8` → „defect in the
  compiler"). Gemeinsame Ursache: eine 64-bit-Konstante landet in einem schmaleren oder
  vorzeichenlosen Slot. Mit `LYRIC_VERIFY_IR=0` übersetzen alle still. **Der Konformanzfall
  braucht Zwillinge bei `int8`, bei `uint` und mit einem suffigierten Literal.**
- **`float → char` liefert immer 0** (u; `Interpreter.FloatToInt`, `_ => (0.0, 0.0)`) — ein
  Bug mitten im Gebiet, unabhängig von SK-29.
- **`200i8` ist still `-56`** (c5) — die Sema-Prüfung fehlt; bis SK-28 entschieden ist,
  mindestens eine Warnung.
- **`Ordered<float>.compare` verletzt den Ordnungsvertrag** (SK-20).
- **Die Argumentreihenfolge entscheidet über Adaption** (SK-32; `x09a`/`x09b`) — Option B
  dort ist Sweep-nah.
- **Der `Into`-Hinweis bei `5 as Id` führt in die Irre** (SK-37, `x13b`).
- **`j2` meldet `SEM0028` zweimal**; **`5u` gibt zwei Meldungen** für einen Tippfehler.
- **Der Kommentar `stdlib/std/string.lyr:378`** ist veraltet.

---

## 6. Nach der Kritik geändert (Fassung 3)

**Übernommen (Kritik hatte recht) — je selbst nachgeprüft:**

- **`LYR-SEM0007` steht in der Spec** (§3.3, `03-types.md:73-75`). Der sechste
  Spec-first-Verstoß ist gestrichen; es sind fünf. SK-19 und Tabelle 1.1 korrigiert.
- **§1.7 → §1.3/§1.4.** Dreimal falscher Abschnitt (Tabelle 1.1, (m), SK-14). Überschriften
  nachgeschlagen.
- **`uint` entsteht aus einem Literal** (`x01`: `let u: uint = 5;` und `uintMax` per Kontext).
  SK-27s Prämisse „kein Literal erzeugt je ein uint" war falsch; die realen Lücken (Display,
  Index, `??`, `-u`, Suffix im unannotierten Kontext) stehen jetzt; der Nachweis schließt den
  Adaptionsfall ein.
- **`float → char` ist kaputt** (`x02a`: jeder Wert → 0; `Interpreter.FloatToInt` `_ => (0,0)`).
  Aus Tabelle 1.1 entfernt, als Befund (u) und **SK-29** aufgenommen, Sweep-Posten.
- **Suffigierte Literale werden nicht bereichsgeprüft** (`x03a`–`x03f`: `200i8` → `-56`,
  `300i8` → Verifier, `16777217f32` still gerundet). Befund (c5), **SK-28**, vierter Fall der
  Familie; SK-08, SK-09 hängen jetzt daran.
- **Es gibt eine „does not fit"-Meldung** (`x04a/b`, `WarningAnalyzer.CheckLiteralInRange`).
  (n) und SK-09 nennen sie als vorhandenes Muster.
- **`char` als Zahl ist eine kommentierte Entscheidung** (`TypeFacts.cs:9-19`,
  `IrVerifier.cs:1748-1756`, §3.6 nennt `char` beim Cast). (a) und SK-04 sagen jetzt, warum der
  Grund nicht mehr bindet (`c as int` ersetzt ihn), statt „nie entschieden".
- **`248u8 >> 1` unterschied nichts.** Neu gemessen mit `uintMax >> 63` = 1 (`x05`), Kontrolle
  `-1 >> 63` = -1; Quelle `Op.Shr`-Zweig zitiert.
- **Die Orphan-Regel ist enger**: eigene Interfaces über `int8` gehen (`x06`), nur die
  `std.core`-Interfaces nicht (`x19`). (h2), SK-10 korrigiert.
- **Compare-and-Branch-Fusion** ergänzt (SK-26; `--no-fusion`, `brcmp`/`brcmpk`). Schluss hält.
- **`lyrfix` ist geplant, nicht vorhanden** (`PLAN.md:343`); SK-02 sagt es.
- **`--deny-warnings` wird angeboten, nicht empfohlen** (Guide `19-diagnostics.md:44-47`);
  SK-24 abgeschwächt.
- **Sieben Sprachvergleiche korrigiert**: Go ist bei der Indexfrage Option B (SK-16 neu
  begründet, A ohne Vorbild); Swift hat kein Quell-Unterdrückungsattribut (SE-0443 = Flags,
  SK-24 vier Zeugen); C#s Methodeninferenz nutzt kein Zuweisungsziel (SK-23); Rusts `as` ruft nie
  Benutzercode (SK-12); Nim ist „Byte", nicht „Codepunkt" (SK-19, §2); Zigs `comptime_int`
  coerciert zu Float (§2-Zelle); Nims `converter` ist kein Literal-Protokoll (SK-15f, ersetzt
  durch C++-UDL). Zwei Issue-Nummern als **behauptet** gekennzeichnet.
- **SK-26=C „~4×" gemessen als Proxy** (`y00`–`y02`): ~12× Bytes für eine quelltextseitige
  Prüfung, als obere Schranke gekennzeichnet; die Empfehlung C→A hängt jetzt an dieser Zahl.
- **SK-25 und SK-26 abgeglichen**: „keine Formatänderung" gilt nur für SK-26=C; neue Opcodes
  sind minor-fähig, SK-25=A hält trotzdem.
- **SK-20/SK-21 „gratis"** gestrichen: `0.0 == -0.0` (`x14`) verlangt eine Normalisierung → SK-38.
- **SK-03 Unterfrage 3** hängt jetzt an SK-29/SK-30: `checked as` braucht ein definiertes `as`.

**Nicht übernommen (Kritik hatte unrecht oder war zu weit) — je mit Gegenbeleg:**

- **Nichts Wesentliches.** Jeder gemessene Punkt der Kritik ist eingetroffen (38 von 38
  Erwartungen in `probes/skalare3/ERWARTUNG.txt`), jeder Lesebeleg fand sich an der genannten
  Stelle. Zwei Präzisierungen gegen die Kritik: (1) die Kritik sagt zu SK-04 „B muss sagen,
  warum dieser Grund nicht mehr bindet (Antwort: weil `c as int` ihn schon heute ersetzt)" —
  das ist übernommen, **aber** die Kritik nennt es „bewusste Entscheidung, die die Spec nicht
  übernommen hat"; genauer: §3.6 hat die Cast-Hälfte übernommen, nur die Arithmetik-Hälfte
  nicht. (2) Zur SK-26-Fusion sagt die Kritik selbst, dass die Schlussfolgerung hält — sie
  steht unverändert.
- **Aus Fassung 2 bleibt bestehen** (Kritik Runde 1, dort widerlegt): SK-04s Warnstufe feuert
  in der stdlib an null Stellen (`string.lyr:378` ist ein Kommentar); die Optimierer-Prämisse
  „Umsortierung/Stärkereduktion" beschreibt Passes, die Lyric nicht hat — der Inliner ist die
  echte Sorge.

**Neu aufgenommen (alle 15 fehlenden Fragen):**

- **SK-28** suffigierte Literale (`x03a`–`x03f`) · **SK-29** `float → char` (`x02a`–`x02c`) ·
  **SK-30** signed→unsigned-Weitung (`x07`) · **SK-31** Shift-Count-Typ (`x08a/b`) ·
  **SK-32** Reihenfolge der Inferenz (`x09a/b`, `x20`) · **SK-33** Ganzzahl-Format-Weitung
  (`x10`) · **SK-34** Format-Vokabular (`fmt.lyr:6-8`, `x11`) · **SK-35** konstant faltbare
  Paniken (`x12a`–`x12c`) · **SK-36** Layout-Rolle (`LyrValue.cs:14-22`) · **SK-37** Literal und
  `opaque` (`x13a`–`x13c`) · **SK-38** `-0.0` im Vertrag (`x14`) · **SK-39** Float-Determinismus
  (§3.2/§6.1 Negativbefund) · **SK-40** Operationsliste von `checked` (`x12c`, `x15`, `x17`,
  `x18`) · **SK-41** Literal über `intMax` unannotiert (`x04a/b`) · **SK-42** Float-Patterns
  (`x16a`).
- **Neue Befunde**: (u), (v), (w), (x), (y), (z), (aa), (ab), (ac), (ad), (ae), (af), (c5).
- **Neue Sweep-Posten**: `float → char` = 0; `200i8` still; `300i8` „defect in the compiler";
  Reihenfolge-Inferenz; `Into`-Hinweis bei `5 as Id`.
