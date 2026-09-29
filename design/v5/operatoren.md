# Lyric 5 — Gebiet „Operatoren und Ausdrucksformen" (dritte Fassung)

**Messstand dieser Fassung:** HEAD `6f6f029f` (Merge PR #173, `release/v4.6.0-cut`), Arbeitsbaum
sauber, Binaries `src/Lyrc/bin/Debug/net10.0/lyrc.dll` und `lyrvm.dll` vom 2026-09-24 (18:33 /
18:26). Nichts gebaut. Proben dieser Runde: `scratchpad/v5-design/probes/operatoren-r4/`
(`d01`–`d19`, Erwartungen vorher in `EXPECT.md`, **alle getroffen bis auf zwei**, siehe §1.9 —
das negative Literal in Klammern und `Into` durch einen Interface-Wert). Die Proben der Kritik
liegen in `operatoren-r3/` (`c01`–`c71`); jede Messung, auf die die Kritik ihre Einwände stützt,
ist hier **selbst wiederholt** (`d01`–`d09`, `d16`, `d17` sind Replikate), nicht übernommen.
Proben der ersten beiden Runden: `operatoren/` und `operatoren-r2/`.

**Drei Quellen, nicht zwei.** Die zweite Fassung öffnete die normative Spec und ließ die
**Konformanz-Suite** zu — `lyric-spec/conformance/cases/06-operators/` (16 Fälle, gelesen per
`ls`). Drei Vorschläge dieses Dossiers retirieren je einen dieser Pins, und die zweite Fassung
nannte keinen davon. Jede Frage trägt deshalb jetzt neben **Regelort** auch **Retiriert**.

**Was die Kritik an der zweiten Fassung richtig gestellt hat, in einem Satz:** vier Zitate
zeigten auf die falsche Zeile (`STATUS.md`, `PLAN.md` dreimal), zwei Quellen wurden gegen ihren
Wortlaut zitiert (`PLAN.md:186-189` als Beleg für das Gegenteil dessen, was dort steht;
`PLAN.md:282-298` gar nicht, obwohl dort die Inkrement-Richtung für 4.7 **gesetzt** ist), eine
Messung bewies nicht, was sie beweisen sollte (`r16` teilte nie eine negative Zahl), und die
Vergleichsspalte hatte in fünf Zeilen das Vorbild auf der falschen Seite. Alles Weitere unter §7.

---

## 1. Ist-Stand

### 1.1 Die Präzedenztabelle

`docs/Grammar.md:442-459` und wortgleich `lyric-spec/spec/02-grammar.md:473-490`: 16 Stufen, alles
linksassoziativ außer Präfix (2), `..`/`..=` (7, nicht-assoziativ), `??` (15, rechts) und
Zuweisung (16, rechts). Das Spec-Kapitel 6 sagt in seinem ersten Satz, die Tabelle der Grammatik
sei die Norm (`lyric-spec/spec/06-operators.md:3`).

**Gemessen** (`operatoren/p01_prec.lyr`, Erwartungen vorher notiert, alle vier getroffen —
Ausgabe `14 / 32 / bit-tighter-than-eq / or-tighter-than-eq`):

| Ausdruck | Ergebnis | Heißt |
|---|---|---|
| `n as float * 2.0` mit `n = 7` | `14` | `as` bindet fester als `*` |
| `1 << 2 + 3` | `32` | `+` bindet fester als `<<` (wie C) |
| `1 & 3 == 1` | wahr | `&` bindet fester als `==` — **nicht** wie C |
| `1 \| 2 == 3` | wahr | dito für `\|` |

Die Bitoperatoren über den Vergleichen sind die richtige Wahl und der Punkt, an dem Lyric C
bewusst verlässt; `06-operators.md:16-18` schreibt es normativ fest („differs from C where C was
wrong"). **Kein offener Punkt; v5 behält das.**

**Gelesen**, `docs/Grammar.md:444-445`: Stufe 1 trägt postfix `++ -- !`, Stufe 2 prefix `! - ~ ++
-- resume`. Rust hat **kein** `~` (dort ist `!` die Komplementbildung), **kein** `++`/`--`, **kein**
`resume` und **kein** postfixes `!`. Zu den drei Einschüben gegenüber Rust — Vergleichs-Split in
Relation/Gleichheit (C, Java), `..` auf Stufe 7, `??` als Stufe 15 (C#) — kommen also mindestens
vier eigene Operatoren. Drei davon sind die, um die OP-7, OP-6 und OP-16 streiten.

**Was die Grammatik NICHT hat — und die Spec ebensowenig.** `docs/Grammar.md:465` schreibt
`Assign = Coalesce [ AssignOp Assign ]`, aber `Coalesce` ist nirgends definiert. Ebenso `UnaryExpr`
(benutzt in `:490-492`) und `PostfixExpr`. Umgekehrt sind `ResumeExpr`, `ComptimeExpr` und
`ThrowExpr` definiert und von keiner Produktion referenziert. **Dieselben drei Waisen und dieselbe
Lücke stehen in der normativen Spec** (gelesen: `02-grammar.md:496` für `Coalesce`, `:521-523` für
die drei Waisen). Zwischen `Assign` und `Primary` existiert in keinem der beiden Dokumente EBNF.
Siehe OP-2.

Dazu: `comptime` ist laut `docs/Grammar.md:491` kontextuell, steht aber nicht in der Tabelle der
kontextuellen Schlüsselwörter (`docs/Grammar.md:81-85`, dort nur `type` und `throws`). Siehe OP-17.

### 1.2 Die Operator-Interfaces — der vollständige Bestand

| Operator | Interface | Ort | Form |
|---|---|---|---|
| `==` `!=` | `Equatable<T>` | `stdlib/std/core.lyr:155` | `fn equals(other: T): bool` |
| `<` `<=` `>` `>=` | `Ordered<T>` | `stdlib/std/core.lyr:175` | `fn compare(other: T): int` |
| `+` `+=` | `Add<T, R>` | `stdlib/std/core.lyr:358` | `fn add(other: T): R` |
| `-` `-=` | `Sub<T, R>` | `stdlib/std/core.lyr:364` | |
| `*` `*=` | `Mul<T, R>` | `stdlib/std/core.lyr:370` | |
| `/` `/=` | `Div<T, R>` | `stdlib/std/core.lyr:376` | |
| `as` | `Into<T>` | `stdlib/std/core.lyr:473` | `fn into(): T` |
| `[ ]` lesen/schreiben | `Indexable<T>` | `stdlib/std/collections.lyr:19` | `get(index: int)` / `mut set(index: int, …)` |
| `for … in` | `Iterable<T>` / `Iterator<T>` | `stdlib/std/iter.lyr:421` / `:21` | |
| f-String `{x}` | `Display` | `stdlib/std/core.lyr:101` | **gebaut und released, von der Spec verboten — OP-25, OP-45** |

Die Bindung ist **nominal und auf `std.core` fixiert**: ein lokales `interface Add<T, R>` mit
identischer Signatur aktiviert `+` nicht (`operatoren/q9_localadd.lyr`). Die Diagnose ist dabei
irreführend — sie sagt *„declare the type with ':: [Add<V, V>]'"*, und genau das steht im
Programm; nur mit dem anderen `Add`.

**Was funktioniert, gemessen:** `a + b`, `a - b`, `a * 2` auf einem eigenen Struct
(`p07_ops_ok.lyr`); Mehrfachkonformanz `Mul<Vec2, Vec2>` neben `Mul<float, Vec2>`, Auswahl über
den rechten Operanden (`docs/guide/07-interfaces.md:312-343`; normativ `06-operators.md:35-42`);
zwei Konformanzen mit gleichem rechten Typ → `LYR-SEM0083` an der Benutzungsstelle
(`q12_ambig2.lyr`); `Ordered` allein gibt `<`, aber kein `==` (`q15_ordered_eq.lyr`,
`LYR-SEM0059`); `v += V { … }` auf einem **Local** läuft über `add` (neu, `operatoren-r4/d19`,
Ausgabe `add / 3`).

**Der Interface-WERT löst KEINEN Operator ein — nur `Display` ist die Ausnahme.** Die zweite
Fassung hatte das für `Add` gemessen (`operatoren-r2/r5_ifaceop.lyr`, `LYR-SEM0003: operator
'Add' is not applicable to 'Add<V, V>' and 'V'`; Kontrolle `q11`: `a.add(b)` läuft). **Neu
gemessen, dieselbe Form, drei weitere Interfaces:**

| Probe | Geschrieben | Antwort | Kontrolle |
|---|---|---|---|
| `d04_iface_eq.lyr` | `a == b` mit `a: Equatable<V>` | `LYR-SEM0003: operator 'Eq' is not applicable to 'Equatable<V>' and 'V'` | `d04b`: `a.equals(b)` läuft, druckt `eq` |
| `d10_iface_ordered.lyr` | `a < b` mit `a: Ordered<V>` | `LYR-SEM0003: operator 'Lt' is not applicable to 'Ordered<V>' and 'V'` | `d10b`: `a.compare(b) < 0` läuft, druckt `lt` |
| `d12_iface_into.lyr` | `a as int` mit `a: Into<int>` | `LYR-SEM0006: cannot cast 'Into<int>' to 'int' — … give 'Into<int>' the conformance :: [Into<int>]'` | `d12b`: `a.into()` läuft, druckt `7` |
| `d11_iface_display.lyr` | `f"got {d}"` mit `d: Display` | **kompiliert, druckt `got V`** | — |

Die `Into`-Diagnose ist dabei absurd: sie rät, `Into<int>` die Konformanz `Into<int>` zu geben.
Und `Display` ist die Ausnahme **aus dem Quelltext heraus**: `TypeChecker.cs:2998-3003` behandelt
einen Interface-Wert eigens („An interface VALUE renders through its vtable when the interface
reaches Display — 'd.show()' on a 'd: Display' is a dispatch, not a conformance question").
Dieselbe Sonderbehandlung fehlt in der Operatorprüfung.

**Das ist kein Loch in der Spec, sondern ein Konflikt mit ihr.** Gelesen, `06-operators.md:4-6`:
„an operator on a non-primitive type IS an interface method call, **resolved exactly as the written
call would be**". Der geschriebene Aufruf `a.add(b)` löst auf; der Operator tut es nicht. Die
zweite Fassung nannte das eine „benannte Lücke", die das Kapitel nicht adressiere — falsch, das
Kapitel verspricht den Operator durch den Wert, und der Compiler hält es nicht. Dazu die
Entscheidung, gelesen **`STATUS.md:2478-2496`** (Maintainer, 2026-08-22/23), Satz auf
**`:2493`**: „Two arguments keep the interfaces plain generics: usable as VALUES, with any result
type". Die zweite Fassung zitierte `:2483` und `:2479-2486` — ebenfalls daneben, zum vierten Mal
in Folge in diesem Absatz. Siehe OP-5 und die neue OP-42.

### 1.3 Was fehlt — gemessen, eine Probe je Zeile (`operatoren/p08_missing.lyr`)

| Geschrieben | Antwort |
|---|---|
| `-a` | `LYR-SEM0003: operator '-' is not applicable to 'V'` |
| `a % b` | `operator 'Rem' is not applicable to 'V' and 'V'` |
| `a & b` | `operator 'BitAnd' is not applicable …` |
| `~a` | `operator '~' is not applicable …` |
| `a << 1` | `operator 'Shl' is not applicable to 'V' and 'int'` |
| `!a` | `operator '!' is not applicable …` |
| `a / b` (Div nicht deklariert) | **`'/' is not defined for 'V' and 'V' — it comes from 'Div': declare the type with ':: [Div<V, V>]' and a 'fn div(other: V): V'`** |

Vier Operatoren erklären, wie man sie bekommt. Die anderen sechs sagen nur nein.

Weiter fehlt, gemessen:

- **`in` als Operator**: reserviert (`docs/Grammar.md:73`), nur im `for`-Kopf; in
  Ausdrucksposition eine Parse-Kaskade, deren Länge von der Probenform abhängt (`let b = 3 in xs;`
  → 4 Diagnosen, `r12b`; `if (3 in xs)` → 10, `r12`).
- **`**`**: Parse-Kaskade (`q4_pow.lyr`).
- **`&`/`|` auf `bool`**: `LYR-SEM0003` (`q7_misc.lyr`).
- **`m["k"]`**: `LYR-SEM0007: index must be an integer, got 'string'` (`q6_mapindexset.lyr`).
- **`?T == ?T`**: `LYR-SEM0059` (`q3_optcmp.lyr`), normativ `06-operators.md:60`.
- **Ein Bereich ist kein Wert**: `let r = 0..5;` ist `LYR-SEM0090` (`q2_range_value.lyr`).
- **KEIN zusammengesetzter Typ hat `==`.** Die zweite Fassung schrieb „Ein Struct hat kein `==`"
  (`r11_structeq.lyr`, `LYR-SEM0059`). Das war zu eng. **Neu gemessen** (`operatoren-r4/d03a-c`,
  Erwartung: dreimal `SEM0059`, getroffen):

  | Geschrieben | Antwort |
  |---|---|
  | `[1, 2] == [1, 2]` | `LYR-SEM0059: '==' is not defined for 'int[]'` — **ohne** Rat |
  | `(1, 2) == (1, 2)` | `LYR-SEM0059: '==' is not defined for '(int, int)'` — **ohne** Rat |
  | `Color.Red == Color.Red` (Enum ohne Payload) | `LYR-SEM0059: … equality comes from 'Equatable': declare the type with ':: [Equatable<Color>]' and a 'fn equals(other: Color): bool'` |
  | `Shape.Circle(1) == Shape.Circle(1)` | dito für `Shape` |

  Ein `match` vergleicht denselben Enum-Wert ohne jede Konformanz. Für Tupel und Arrays gibt es
  nicht einmal eine Deklarationsstelle, an der man `:: [Equatable<…>]` schreiben könnte — der Rat
  fehlt in der Diagnose also zu Recht, und das ist der Befund. Siehe OP-26 (Structs) und die neue
  OP-36 (Enum, Tupel, Array).
- **`Hashable` wird nicht synthetisiert** (`r19b_hashsynth.lyr`, `LYR-SEM0020`); der Kommentar in
  `stdlib/std/core.lyr:516` meint den Anker `combineHash`; die Synthese steht in `PLAN.md:258`
  als P1-Position 2 „designt".

### 1.4 Der umgekehrte Operand ist unschreibbar

`extend int :: [Mul<V, V>]` im Modul, das `V` deklariert, ist `LYR-SEM0041` orphan extension
(`q8_reverse.lyr`). Also ist `3 * v` für jeden Benutzertyp unschreibbar. Lyrics Orphan-Regel ist
damit strenger als Rusts (RFC 2451: *irgendeiner* der Typen der Impl muss lokal sein). Siehe OP-12.

### 1.5 `++`/`--` — drei Messungen, eine gesetzte Richtung

| Probe | Geschrieben | Antwort |
|---|---|---|
| `p09_inc_user.lyr` | `v++` auf Typ mit `Add<int, V>` | `LYR-SEM0003: operator '++/--' is not applicable to 'V'` |
| `r2b_fieldinc.lyr` | `p.x++` (int-Feld) | `LYR-IR0001: increment/decrement target (only parameters and locals)` |
| `r2c_eleminc.lyr` | `xs[0]++` | dasselbe `LYR-IR0001` |
| `r2d_prefixstmt.lyr` | `++x;` | `LYR-SEM0022`; `x++;` dagegen akzeptiert |
| `r2a_ctrl.lyr` (KONTROLLE) | `b.n += 1` und `xs[0] += 1` | läuft, `11 / 11` |
| **`d01_float_inc.lyr` (NEU)** | **`var f = 1.5; f++;`** | **kompiliert, druckt `2.5`** |
| `d01b_string_inc.lyr` (KONTROLLE) | `s++` auf `string` | `LYR-SEM0003: operator '++/--' is not applicable to 'string'` |
| `d14_inc_expr_forms.lyr` | `let y = x++; let z = f(x++);` | läuft, `1 / 2 / 3` — die Ausdrucksform ist gebaut und gepinnt |

**Der dritte Widerspruch zwischen Spec und Compiler.** Gelesen, `06-operators.md:26-27`: „`++`
and `--` exist prefix and postfix, **on integer variables**, as EXPRESSIONS with the classic
values". Gelesen, `SPEC-RUNDE.md:19-20`: `CheckUnary`/`CheckPostfix` verlangen
`TypeFacts.IsNumeric` — und das schließt `float` ein, wie `d01` beweist. Die zweite Fassung führte
in OP-23 „zwei Punkte, die dem Compiler widersprechen"; es sind drei. Siehe die neue OP-35.

**Die Richtung ist für 4.7 gesetzt, und die zweite Fassung verschwieg das.** Gelesen,
`PLAN.md:282`: Position 12 „**Inkrement abgeleitet statt eingebaut** (`SPEC-RUNDE` 1) —
entschieden, nicht gebaut". Gelesen, `PLAN.md:292-295`: „der Maintainer hat die Richtung gesetzt —
`++`/`--` folgen entweder aus `Add<T, R>` (`x.add(1)` …) oder bekommen eigene `Inc`/`Dec`-
Interfaces. Empfehlung steht auf A (Rule 2: kein zweiter Mechanismus für „plus eins"). Im selben
Satz zu klären: die Statement-Form beider Schreibweisen … und worauf ein Inkrement stehen darf".
Gelesen, `PLAN.md:297-298`: daran hängen `Neg`, `Rem`, Bit-Operatoren, `in` über `Contains<T>` —
„eine Runde, nicht zwei". OP-6, OP-7, OP-8, OP-9 und OP-33 sind damit **nicht** offene v5-Fragen,
sondern die 4.7-Runde, deren Richtung steht. Was v5 wirklich zu entscheiden hat, steht in OP-7
(umgeschrieben) und in der neuen OP-43.

**Die zweite Fassung las `PLAN.md:186-189` verkehrt herum.** Sie stützte „`IR0001` ist die falsche
Klasse" darauf, der Maintainer habe den Posten „genau deshalb" aus der Diagnostikliste genommen.
Gelesen, wörtlich: „§6.1 sagt „auf ganzzahligen **Variablen**", also ist die Frage, worauf ein
Inkrement stehen darf, **offen und keine Grenze, die falsch benannt wäre**. Sie gehört in die
Inkrement-Runde von 4.7 (Position 12)". Der Maintainer verneint ausdrücklich, dass der Code falsch
ist; das Dossier zitierte ihn als Beleg dafür. Was tatsächlich bleibt: `12-diagnostics.md:70-71`
definiert `IR0001` als „valid Lyric this implementation cannot lower — the program is right and the
compiler is limited". Ob `p.x++` gültiges Lyric ist, entscheidet die 4.7-Runde; **bis dahin ist
`IR0001` der ehrliche Code**, und OP-8 B (Umbenennung) ist zurückgezogen. Siehe OP-8.

### 1.6 Compound Assignment — gepinnt, dokumentiert, und §6.5 widerspricht sich trotzdem

| Form | Local | Feld | Array-Element |
|---|---|---|---|
| `+=` auf `int` | ja | ja | ja |
| `+=` über `Add<T,R>` | ja (`d19`, neu) | **nein** `LYR-SEM0003` | **nein** `LYR-SEM0003` |
| `&&=` `\|\|=` `??=` | ja | ja | ja |

Gemessen: `p06_ops.lyr` (die zwei Ablehnungen), `p07_ops_ok.lyr`, `r2a_ctrl.lyr`, `p17_logassign.lyr`
(alle neun Kombinationen plus Kurzschluss), `r20b_coalassign_field.lyr` (`b.n ??= 5`,
`boxes[0].n ??= 9`), und **neu `d17_compound_index_once.lyr`** (Replikat der Kritik-Probe `c62`):
`ys[c.next()] += 5` druckt `next` **einmal**, dann `5`; `boxes[c.next()].v ??= 9` druckt `next`
**einmal**, dann `9`. Die „ONCE"-Zusage von `:101` ist für den eingebauten und den `??=`-Pfad
gemessen wahr.

**Die Regel ist normativ UND gepinnt.** Gelesen, `06-operators.md:104-108`: „On a **field or
element** target an interface-backed compound is an error (`LYR-SEM0003`) telling the writer to
spell it out: the shorthand would evaluate the object or the index twice, and that stays visible
in source." Gelesen, **`lyric-spec/conformance/cases/06-operators/compound_on_field_is_refused.lyr`**,
Zeile 19: `h.v += Vec { x = 1 };` mit `//! error: LYR-SEM0003`. Gelesen, `06-operators.md:97-99`:
„**Since 4.6**: before it, `&&=` and `||=` were grammar-legal and refused by the reference lowering
as an implementation limit, and `??=` was carried on a variable but not on a field or an element."
Die Sektion wurde in 4.6 bewusst überarbeitet — die Feld-Einschränkung für Interface-Compounds
blieb dabei **absichtlich** stehen, mit Begründung („stays visible in source").

**Und dieselbe Sektion verspricht vier Zeilen vorher das Gegenteil.** `:101-102`: „The target is
evaluated ONCE however often the form reads it: `xs[next()] ??= v` calls `next` once, **the same
promise `xs[i] += 1` makes**." Die Begründung von `:107` („would evaluate … twice") ist damit durch
`:101` und durch die Messung (`d17`) widerlegt — der Compiler KANN einmal auswerten und tut es auf
zwei von drei Pfaden. Die zweite Fassung nannte das „eine nicht nachgezogene Implementierung".
**Das war falsch formuliert:** es ist eine gepinnte, begründete Regel, deren Begründung nicht
stimmt. OP-10 A heißt deshalb „**Pin retirieren**, weil `:101` und `:107` sich widersprechen", und
der Pin heißt `compound_on_field_is_refused.lyr`. `docs/guide/07-interfaces.md:350-353`
wiederholt die Regel mit derselben Begründung.

### 1.7 Zuweisung ist ein Ausdruck — mit welchem Typ und Wert, steht nirgends

Gemessen (`r3_assignexpr.lyr`, `3 / 7 / 7 / branch / 6`): `let x = (y = 3)`; `a = b = 7`;
`if (f = true) { … }` **kompiliert kommentarlos und der Zweig läuft**; `1 + (n = 5)` ist 6. Normativ
`06-operators.md:91`: „Assignment is an expression, right-associative: `a = b = 3` assigns both."
Gepinnt: `conformance/cases/06-operators/assignment_chains_right.lyr`, Zeile 6 (`a = b = 3;`).

**Neu gemessen — Typ und Wert jeder Zuweisungsform** (`d05_assign_values.lyr`, Erwartung `3 / 5 /
1 / 3 / 2`, getroffen):

| Form | Wert | Typ |
|---|---|---|
| `(p.x = 3)` (Feld) | `3` | `int` |
| `(xs[0] = 5)` (Element) | `5` | `int` |
| `(n += 1)` mit `n = 0` | `1` | `int` — der Wert **nach** der Zuweisung |
| `a += b += 1` mit `a = b = 1` | gültig | `a == 3`, `b == 2` — rechtsassoziativ auch über Compounds |
| `(o ??= 3)` mit `o: ?int` | | `?int` (`p23_compound_value.lyr`; neu `d15`: `let r: int = (o ??= 3)` ist `LYR-SEM0001: cannot assign '?int' to 'int'`) |

Die Regel, die all das erklärt, lautet „**der Wert einer Zuweisung ist der neue Wert des Ziels,
und ihr Typ ist der Typ des Ziels**" — und sie steht in keiner Zeile von §6.5. Unter dieser Regel
ist `??=` mit `?int` **nicht** die Ausnahme, sondern der Regelfall; die zweite Fassung stellte es
umgekehrt dar. Siehe OP-11 (umgeschrieben) und die neue OP-37.

### 1.8 Ausdrucksformen

| Form | Zustand | Beleg |
|---|---|---|
| `if (c) a else b` | ja, `else` Pflicht, beide Seiten nur `Expr` | `docs/Grammar.md:505`, `p13_exprforms.lyr` |
| `if (c) { … } else { … }` in Wertposition | **nein**, 5 Diagnosen für ein Konstrukt | `p14_ifblock.lyr` |
| `match` als Ausdruck, Arm = `Expr` oder Value-Block | ja | `p13_exprforms.lyr` |
| Value-Block im `match`-STATEMENT | nein, `LYR-SEM0022` (so entworfen) | `q17_matchstmt_tail.lyr` |
| Block-Lambda mit Tail | ja | `p13_exprforms.lyr` |
| `let a = { … }` (allgemeiner Block-Ausdruck) | nein, Parse-Kaskade (bewusst) | `p16_plainblock.lyr`, `design/value-block.md:29`, **`PLAN.md:406`** („Bewusst nicht in der Ablage: … Block-Ausdruck für jeden Block") |
| `throw` als Ausdruck | ja | `p18_throwexpr.lyr` |
| `comptime e` als Präfix auf `UnaryExpr` | ja | `p19`/`p20`, `docs/guide/16-building.md:302-303` |

Die zweite Fassung zitierte die Ausschlussliste als `PLAN.md:371`; dort steht „`?Struct` als
Wert". Die Liste — „nestbare Optionals, … `never` als allgemeiner Typ, Ordnung auf Optionals,
Block-Ausdruck für jeden Block, `break value`, `derive`-Schlüsselwort" — steht bei
**`PLAN.md:406`** und wird bei **`:413-414`** als weiterhin gültig bestätigt („gilt hier weiter die
ältere, ausdrückliche Begründung").

Die Fehlerqualität bei `if (c) { 1 } else { 2 }` bleibt der schlechteste Punkt des Gebiets
(`LYR-SEM0017` auf `main` zuerst, dann `PAR0002`, `PAR0036` „requires an 'else' branch" obwohl eins
dasteht, `PAR0016`, `PAR0025`).

**Kontexttyp wirkt in beide Ausdrucksformen** (`r13_ctx_if.lyr`, `r13b_ctx_matchblock.lyr`):
`let x: float = if (c) 1 else 2;`, `let y: float = match (n) { 1 => { 1 }, _ => { 2 } };` laufen.
Normativ `06-operators.md:137-139` (seit 2.1), `03-types.md:32-35`. Siehe OP-32.

### 1.9 Messungen, die in eine v5-Frage münden

- **`a ?? 3` auf Nicht-Optional ist `LYR-SEM0005`** (`q5_coalesce_nonopt.lyr` gegen den aktuellen
  Build; gelesen `PLAN.md:172`, PR #171). Von OP-19s „beide `IR0001`-Fälle" ist einer erledigt.
- **`1 + n as float` kompiliert** (Literal adaptiert; `p04`), **`a / b as float` nicht** (`p05`,
  Kontrolle).
- **`a < b < c`** ist `LYR-SEM0003 … 'bool' and 'int'` (`p02_chain.lyr`). Siehe OP-4.
- **`Into` nur EINMAL** (`q13_into.lyr`, `LYR-SEM0085` + 2× `SEM0086`). Siehe OP-13.
- **`never` außerhalb von Rückgabe/`throws` stürzt den Compiler ab**: `let x: ?never`, `var ys:
  never[]`, `fn f(x: never)` sind je `LYR-CLI0020` (`r6a-c`); Kontrolle `?int`/`int[]` läuft
  (`r6d`). Siehe OP-16.
- **Ein Operator darf nicht werfen** (`r8b_throws_op.lyr`, `LYR-SEM0042`, weil `std.core.Add.add`
  ohne `throws` steht, `core.lyr:358-362`). Siehe OP-22.
- **Ein Benutzer-Operator läuft im Compiler**: `comptime (V { x = 1 } + V { x = 2 }).x` = 3
  (`r15`); Panik im Benutzer-`add` → `LYR-CT0002 … panicked [LYR-VM0011]` (`r15e`); Struct-Ergebnis
  → `LYR-SEM0100` (`r15d`). Siehe OP-34.
- **Faltung == Laufzeit, jetzt WIRKLICH gemessen.** Die zweite Fassung stützte „`-7 / 2` ergibt
  beide Male `-3`" auf `r16_overflow.lyr:11,15` — dort steht `comptime (0 - 7 / 2)`, also
  `0 - (7/2)`; unter `comptime` wurde nie eine negative Zahl geteilt. Die Aussage stimmte, die Probe
  bewies sie nicht. **Neu** (`d02_comptime_negdiv.lyr`, Erwartung `-3 / -1 / -3 / -1`, getroffen):
  `comptime ((0 - 7) / 2)` = `-3`, `comptime ((0 - 7) % 2)` = `-1`, Laufzeit dasselbe. **Dazu der
  Randfall aus §3.2** (`d16`, `d16b`): `MIN / -1` zur Laufzeit ist `MIN`, `MIN % -1` ist `0`,
  `comptime ((0 - 9223372036854775807 - 1) / (0 - 1))` ist `MIN` — genau `03-types.md:57-58`
  („WRAPS to `min` … `min % -1` is 0 — not a second panic case"), in beiden Welten. Die Hypothese
  der Kritik (.NET-`OverflowException`) trifft nicht; die VM fängt den Fall.
- **Ein sicher panikender konstanter Ausdruck wird NICHT gemeldet** (`d09_divzero.lyr`): `let z =
  1 / 0;` kompiliert und panikt zur Laufzeit mit `LYR-VM0002: division by zero`, Exit 101.
  Kontrolle (`d09b`): `comptime (1 / 0)` ist `LYR-CT0002 … panicked [LYR-VM0002]`. Der Compiler
  faltet also **substituierend**, nicht **diagnostizierend**. Siehe die neue OP-44.
- **`%` auf float ist `fmod`, und Float-Division durch null panikt nicht** (`d08_float_rem.lyr`,
  Erwartung getroffen): `-7.5 % 2.0` = `-1.5` (Vorzeichen des Dividenden — **nicht** IEEE-754
  `remainder`, das `0.5` gäbe), `1.0 / 0.0` = `Infinity`, `0.0 / 0.0` = `NaN`, `1.0 % 0.0` = `NaN`.
  Gelesen, `03-types.md:56`: „Division by zero is a panic" — unter der Überschrift „Integer
  arithmetic", aber ohne den Satz, dass Floats ausgenommen sind; `06-operators.md:11-12`: „float
  per IEEE 754". Siehe die neue OP-41.
- **Negatives Literal am Rand** (`d07`, `d07b`, `d13`, `d13b`, `d13c`):

  | Geschrieben | Antwort |
  |---|---|
  | `let m = -9223372036854775808;` | kompiliert, druckt `MIN` |
  | `let m = 9223372036854775808;` | `LYR-SEM0001: integer literal does not fit 'int' — annotate the uint type that holds it` |
  | `let m = - 9223372036854775808;` (Leerzeichen) | kompiliert, `MIN` |
  | `let m = -(9223372036854775808);` (Klammern) | **kompiliert, `MIN`** — Erwartung war `SEM0001`, **nicht getroffen** |
  | `let m = 0 - 9223372036854775808;` | `LYR-SEM0001` |

  Die Kritik schrieb, die Regel stehe „weder in Grammar.md §6.1 noch in 03-types.md". **Das ist
  falsch**: gelesen, `03-types.md:36-38`: „A leading `-` folds into the literal, so `let n: int8 =
  -8;` and `-9223372036854775808` are literals, not unary expressions." Was die Kritik richtig sah:
  `docs/Grammar.md:91` (`IntLit`) kennt kein Vorzeichen, und die Spec-Zeile sagt nicht, ob
  „leading" durch Klammern hindurch gilt — der Compiler faltet auch dort. Siehe die neue OP-40.
- **Auswertungsreihenfolge, jetzt auch für Zuweisung und Empfänger** (`r7_evalorder.lyr` für
  binäre Operanden, Argumente, Array-Literal, Struct-Initializer; **neu `d06_evalorder.lyr`**,
  Erwartung getroffen): `xs[f()] = g()` → `f`, `g`; `xs[f()] += g()` → `f`, `g`; `r().m(f())` → `r`,
  `f`, `m`; `t() && u()` → `t`, `u`. Der Compiler wertet **Ziel (mit Index) vor der rechten Seite**
  und **Empfänger vor den Argumenten**. Festgeschrieben ist keins davon (gegrept über
  `lyric-spec/spec/` und `docs/Grammar.md` auf „left to right" / „evaluation order": kein
  Treffer). Siehe OP-21 (korrigiert) und die neue OP-38.
- **Interpolation ruft `Display`, obwohl die Spec das verbietet — und zwar RELEASED.**
  `r10_fstring.lyr` und neu `d11`: `f"value {v}"` ruft `show()`. Gelesen, `06-operators.md:114-115`:
  „there is no implicit `Display` call in interpolation." Gelesen, `CHANGELOG.md:13` (`## v4.6.0 —
  2026-09-23`) und `:64-65`: „**f-strings render a `Display` conformance.** `f"{p}"` on a conforming
  type calls `show()` instead of being `LYR-IR0001`." Gelesen, `docs/guide/02-values-and-types.md:84-88`
  („An interpolation holds a scalar or a `Display` value … the hole calls `show()`"). Gelesen, Commit
  `219b3108` (2026-09-22, „sema: an f-string hole renders a Display value"). Gelesen,
  `TypeChecker.cs:2989-2991`: der Doc-Kommentar beruft sich auf „(§6.6)" für das **Gegenteil**
  dessen, was §6.6 sagt. Gelesen, `ls lyric-spec/conformance/cases/06-operators/`: **kein**
  Display-Fall; der einzige Display-Fall der Suite
  (`05-interfaces/interface_value_from_annotated_binding.lyr:18-19`) ruft `d.show()` **als Methode**.
  Gelesen, `git -C lyric-spec log --all --grep=display|interpolat`: kein Regel-PR dazu. Die
  zweite Fassung schrieb „die Suite deckt die Zeile vermutlich nicht ab" — „vermutlich" war
  prüfbar, und der Befund ist größer: ein Feature, das gegen die Spec-Version 4.6 released wurde.
  Siehe OP-25 und die neue OP-45.
- Nebenbei bestätigt: `"a" + "b"` über `std.string.concat` (`06-operators.md:49-50`); `-n as uint`
  wickelt still (`q7_misc.lyr`, `q14_last.lyr`).

---

## 2. Sprachvergleich

Die Tabelle hat neun Zeilen. Die Kritik fand in der zweiten Fassung zehn falsche oder
verkehrt-herum gesetzte Vergleichsaussagen; alle zehn sind hier korrigiert und in §7 einzeln
benannt. Was **nicht** aus einer Sprachreferenz gelesen, sondern aus Kenntnis der Sprachen gesagt
wird, gilt in diesem Abschnitt als *behauptet* — Lyric-Aussagen sind gemessen oder gelesen,
Fremdsprachen-Aussagen sind es nicht.

### 2.1 Überblick

| Sprache | Wie ein Operator entsteht | `++` | Zuweisung | Preis |
|---|---|---|---|---|
| **Rust** | Traits `Add<Rhs = Self> { type Output; }`, `AddAssign` getrennt, `Neg`/`Not`/`Rem`/`BitAnd`/`Shl`/`Index`/`IndexMut` vollständig | **kein Inkrement, das Token existiert nicht** | Ausdruck vom Typ `()`; **Reihenfolge: rechte Seite VOR dem Ziel** (Rust Reference, Assignment expressions; bei Compound auf Primitiven ebenso, sonst Empfänger zuerst) | Assoziierter Typ + Default-Parameter sind zwei Mechanismen; kein `in`, kein `**`; `!` als Typ nur in Rückgabeposition (stable), sonst `never_type`-Gate |
| **Swift** | `static func + (lhs:rhs:)`, dazu selbstdefinierte Operatoren mit `precedencegroup` (Halbordnung) | SE-0004 Ende **2015** angenommen; 2.2 deprecated, **3.0 (September 2016) entfernt** | `Void` — Trap zu | Operator-Suppe; **„expression too complex" kommt aus der Auflösung ÜBERLADENER Operatoren mit Literalen im Constraint-Solver, nicht aus den Präzedenzgruppen** — das Sequence-Folding ist ein billiger Vorpass. `Never` ist ein gewöhnliches unbewohntes Enum in jeder Typposition (`Result<T, Never>`, seit 5.9 mit `Equatable`/`Hashable`) |
| **C#** | `public static T operator +(…)`; seit C# 11 / .NET 7 `IAdditionOperators<TSelf, TOther, TResult>`; `checked`-Operatoren | überladbar | **Ausdruck mit Wert** (Typ des Ziels); `if (b = true)` gibt **CS0665** vom Compiler selbst („Assignment in conditional expression is always constant"), `if (b = c)` nicht; **`??=` auf `T?` (Nullable-Werttyp) hat den Typ `T`**; Ziel wird VOR der rechten Seite ausgewertet; `-2147483648` ist ein Sonderfall nur für das **unmittelbar** folgende Token | Zwei Welten (statische Operatoren *und* Generic Math); Überladungsauflösung nutzt den Rückgabetyp nie — C# 9 hat zielgetypte `new()`/`?:` als ARGUMENTE eingeführt, keinen Kontext-Tiebreaker |
| **Kotlin** | Konvention `operator fun plus/…/inc/dec/contains/get/set/invoke/compareTo`; `equals` ist die Ausnahme (muss `Any.equals(other: Any?)` überschreiben) | `inc(): T` treibt `++` | **kein Ausdruck** — Trap zu | Keine generische Constraint „hat `+`"; keine Bitoperatoren (`and`/`shl` sind Infix-Funktionen) |
| **Scala** | Jede Methode ist infix; Präzedenz aus dem ersten Zeichen | **kein Inkrement**; `++` ist Kollektionskonkatenation | **Ausdruck vom Typ `Unit`** (nicht Statement — die zweite Fassung führte Scala in OP-3 unter A) | Präzedenz nicht deklarierbar |
| **Haskell** | Operatoren sind Funktionen; Fixität deklarierbar (`infixl 6 +`); `infix 4 <` nicht-assoziativ | **kein Inkrement**; `++` ist Listenkonkatenation (`infixr 5`) | gibt es nicht | Gebündelte Klasse `Num`; zehn freie Stufen |
| **Python** | Dunder: `__add__`, `__radd__`, `__iadd__`, `__contains__`, `__getitem__` mit beliebigem Schlüssel | gibt es nicht | Statement; `:=` eingeschränkt | Drei Methoden pro Operator; dynamisch |
| **Zig** | gar nicht; `+%` wrappend, `+\|` sättigend, `+` = illegal behavior bei Überlauf (Panik nur in `Debug`/`ReleaseSafe`) | gibt es nicht (`++`/`**` sind Compile-Zeit-Array-Operatoren) | Statement | Mathe-Code heißt `v.add(w)`; `noreturn` nur als Rückgabetyp |
| **Go** | gar nicht | **`x++` ist ein STATEMENT**, kein Präfix | Statement; Reihenfolge: Aufrufe lexikalisch links-nach-rechts, **aber Index- gegen Aufrufauswertung ausdrücklich „not specified"** (Go-Spec „Order of evaluation", Beispiel `y[f()], ok = g(h(), i()+x[j()], <-c), k()`); konstante Division durch null ist ein Compilefehler | Kein generischer Mathe-Code ohne Typmengen-Constraints |

### 2.2 Die entgegengesetzte Entscheidung: Zig

Zig verbietet Operator-Overloading vollständig; wer eine Zig-Zeile liest, weiß, was sie ausführt.
Aber Zigs gewöhnliches `+` ist bei Überlauf **illegal behavior** — Panik nur in `Debug`/
`ReleaseSafe`, undefiniert in `ReleaseFast`/`ReleaseSmall`. Nur `+%` und `+|` sind modusfest.
Zig ist damit kein Gegenbeispiel zu einer profilabhängigen Überlaufantwort, sondern ein Beispiel
dafür **plus** zwei modusfeste Zusatzoperatoren. Was für Lyric trägt: `a + b` kann in Lyric heute
nicht werfen (`r8b`, `LYR-SEM0042`) — Zigs Eigenschaft ohne Zigs Verbot. Siehe OP-18, OP-22.

### 2.3 Kotlin und Scala zur nominalen Bindung

Lyric verlangt die Konformanz (`06-operators.md:56-58`: „Conformance is required, not the method
alone"). Kotlin/Scala nehmen das Gegenteil und verlieren die generische Constraint. **Lyrics Wahl
ist richtig und wird nicht neu verhandelt**; sie ist die Voraussetzung für
`fn total<T :: [Add<T, T>]>` (`docs/guide/07-interfaces.md:294`). Dass Kotlin ausgerechnet bei
`equals` von seinem Konventionsmodell abweicht, bestätigt das.

### 2.4 Zur Form `Add<T, R>` vs. `Self` + assoziierter Typ

Rust: `trait Add<Rhs = Self> { type Output; }` — zwei Sprachmittel, die Lyric nicht hat, und `dyn
Add` geht nicht. Rust, Haskell (`Num`/`Ord`) und Scala (`Numeric[T]`) bieten Operatoren unter
generischer Constraint; **C# ist nur das einzige Vorbild für den Ergebnistyp als
Typ-PARAMETER** (`IAdditionOperators<TSelf, TOther, TResult>`). Lyrics `Add<T, R>` ist dieselbe
Wahl mit implizitem Empfänger. Die Begründung dafür, `STATUS.md:2493` („usable as VALUES"), ist
gemessen nur für den Methodenaufruf eingelöst (§1.2).

### 2.5 `++`: wer es hat, wer es abgeschafft hat, wer es nie hatte

| | Sprachen |
|---|---|
| Hatte `++` und hat es **abgeschafft** | Swift — **eine** |
| Hat `++` **behalten** | C# (überladbar), Kotlin (`inc()`), Go (Statement, kein Präfix) — **drei** |
| Kennt kein Inkrement, Token für **Konkatenation/Wiederholung** | Haskell, Scala, Zig — **drei** |
| Kennt weder Inkrement noch das Token | Rust, Python — **zwei** |

Swifts Begründung trifft Lyric wörtlich (C-Erbe, `a[i++] = i++`, `x += 1` sagt dasselbe). Aber
eine Mehrheit gibt es nicht — und seit `PLAN.md:282` ist die Richtung für Lyric ohnehin gesetzt
(§1.5). Die Zählung dient jetzt nur noch OP-33, nicht OP-7.

### 2.6 `in`, Index, Chaining, Konstanten

- **`in`**: Python (`__contains__`), Kotlin (`contains`). Rust, Swift, C#, Zig, Go: `xs.contains(x)`.
- **Index mit beliebigem Schlüssel**: Python, C# (Indexer), Kotlin, Scala. Rust `Index<Idx>`, aber
  `IndexMut` für `HashMap` fehlt.
- **Verkettete Vergleiche**: Rust, Swift (`associativity: none`) und Haskell (`infix 4`) lehnen
  `a < b < c` vor dem Typsystem ab; C, C#, Java, Kotlin, Go, Lyric lassen sie linksassoziativ
  laufen; Python liest sie als Konjunktion.
- **Konstante Division durch null** (neu, für OP-44): C# (`CS0020`, Fehler), Go (Compilefehler
  für konstante Ausdrücke), Rust (`unconditional_panic`, deny-by-default) lehnen ab; Java und
  Lyric lassen es zur Laufzeit werfen.
- **Strukturelle Gleichheit zusammengesetzter Typen** (neu, für OP-36): Go — Structs/Arrays aus
  vergleichbaren Feldern sind vergleichbar, **Slices nicht**; Swift — Tupel bis Arität 6 über
  Bibliotheks-`==`, `Array<E: Equatable>` bedingt konform, Enums ohne Payload automatisch
  `Equatable`; Rust — Tupel/Arrays `PartialEq` wenn die Komponenten es sind, Enums nur per
  `derive`.

---

## 3. Designfragen

Jede Frage trägt **Regelort** (Spec-Kapitel / `docs/Grammar.md`) und neu **Retiriert** (welcher
Konformanzfall mit der Regel fällt). `spec-first` heißt: Regel-PR mit `since:`-Gate plus
Konformanz-Zwilling, dann Compiler, dann Release, dann Pin (gelesen, `lyric-spec/README.md:45-46`
für das `since:`-Gate; die Reihenfolge ist Projektregel, behauptet). Die Zuordnung steht gesammelt
in OP-23 und OP-39.

### OP-1 — Bleibt die Präzedenztabelle fest, oder darf eine Bibliothek Operatoren definieren?

**Heute:** 16 feste Stufen; kein Mechanismus für neue Operatoren. `06-operators.md:6-7`: „There
is no second dispatch mechanism and no user-defined operator beyond the interfaces named here."

| Option | Vorbild | Preis |
|---|---|---|
| A — feste Tabelle behalten | Rust, C#, Kotlin, Go | Kein `\|>`, kein `<*>`; jeder neue Operator ist eine Spracherweiterung |
| B — deklarierbare Fixität (`infixl 6 <+>`) | Haskell | Zehn freie Stufen → Operator-Suppe; der Leser sucht Fixitätsdeklarationen |
| C — Präzedenzgruppen als Halbordnung | Swift | Mächtiger als B. **Korrektur:** der Preis ist NICHT Swifts Typprüfungskosten — „expression too complex" entsteht aus überladenen Operatoren mit Literalen im Constraint-Solver, das Präzedenz-Folding ist ein billiger Vorpass. Der Preis von C ist derselbe wie bei B: Werkzeuge (Formatter, LSP, `lyrfix`, Parser) müssen Fixität aus dem Programm lernen |
| D — Präzedenz aus dem ersten Zeichen | Scala | Keine Deklaration; Bibliotheksoperatoren an überraschender Stelle |

**Empfehlung: A.** Rule 2 (ein Mechanismus für „Operator" = ein Interface aus `std.core`), und
B/C/D verlangen von jedem Werkzeug eine Fixitätstabelle aus dem Programm. **Zusatzargument aus
OP-24:** die `<`-Auflösungsregel (`06-operators.md:120-123`) wird mit freien Operatoren
unentscheidbar. Das Kostenargument gegen Swift ist gestrichen; es trüge gegen Überladung, nicht
gegen Präzedenzgruppen.

**Bruch:** nein. **Vertrauen: gelesen** (Lyric); Swift-Aussage behauptet. **Regelort:**
`06-operators.md:6-7`, keine Änderung. **Retiriert:** nichts. **Hängt an:** nichts.

### OP-2 — Wird die Ausdrucksgrammatik zwischen `Assign` und `Primary` ausgeschrieben?

**Heute:** `Coalesce`, `UnaryExpr`, `PostfixExpr` benutzt und nie definiert; `ResumeExpr`/
`ComptimeExpr`/`ThrowExpr` definiert und nie benutzt — in `docs/Grammar.md:465, 490-492` UND
`02-grammar.md:496, 521-523` (gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A — die volle Kette (16 Produktionen) | Haskell-Report, C-Standard | Lang, redundant mit der Tabelle |
| B — parametrische Produktion + Tabelle, „die Tabelle ist normativ" | Rust-Referenz | Kurz; die Tabelle wird Norm |
| C — lassen | — | Lücke an der meistgelesenen Stelle eines normativen Dokuments |

**Empfehlung: B.** `Expr = Assign`, `Assign = Binary [AssignOp Assign]`, `Binary = Unary { BinOp
Unary }`, `Unary = PrefixOp Unary | ResumeExpr | ComptimeExpr | ThrowExpr | Postfix`, `Postfix =
Primary { PostfixOp }`. Dazu gehört ein Satz, wo das gefaltete negative Literal hängt (OP-40).
Regel-PR im Spec-Repo, `docs/Grammar.md` läuft als Mirror mit (`README.md:37-41`).

**Bruch:** nein. **Vertrauen: gelesen.** **Regelort:** `02-grammar.md` §6.2 + `docs/Grammar.md`
§6.2. **Retiriert:** nichts. **Hängt an:** OP-40 (wo das Vorzeichen hängt).

### OP-3 — Ist eine Zuweisung ein Ausdruck?

**Heute: ja, mit vollem Wert, in jeder Position** (`r3_assignexpr.lyr`; `if (f = true)` läuft
kommentarlos). Normativ `06-operators.md:91`. **Gepinnt:** `assignment_chains_right.lyr:6`.

| Option | Vorbild | Preis |
|---|---|---|
| A — Zuweisung wird ein STATEMENT | **Go**, Python, Kotlin | Bruch: `a = b = 7`, `a += b += 1` (`d05`) und `while ((line = next()) != null)` fallen. Löst die Falle vollständig. **Korrektur:** Scala gehört NICHT hierher — dort ist `x = e` ein Ausdruck vom Typ `Unit`, also B |
| B — Ausdruck vom Typ `void` | Rust, Swift, **Scala** (`Unit`) | `if (f = true)` wird `LYR-SEM0004` von selbst; `a = b = 7` bricht ebenfalls |
| C — Wert behalten, Zuweisung in einer BEDINGUNG verbieten | C (`-Wparentheses`), C# (`CS0665` nur für den KONSTANTEN Fall `if (b = true)`; `if (b = c)` braucht einen Analyzer) | Sonderregel; `1 + (n = 5)` bleibt |
| D — heutiger Zustand + Warnung | — | Die Warnung ist 4.x-fähig, die Regel v5 |

**Empfehlung: B, mit D als 4.x-Uhr.** Regelneutral ist nur die Teilwarnung „Zuweisung in einer
Bedingung" — sie trifft unter A, B und C dasselbe Programm (`PLAN.md:229-231`: eine Uhr setzt
keine Antwort voraus). Eine Warnung auf jede gelesene Zuweisung nähme die Entscheidung gegen C
vorweg.

**Bruch: major.** **Warnstufe 4.x:** `LYR-SEM`-neu nur auf die Zuweisung in `if`/`while`/
`match`-Scrutinee. **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.5 (erster
Satz) und §6.8. **Retiriert (A und B): `assignment_chains_right.lyr`** — der Fall pinnt `a = b =
3`; unter A ist das ein Parse-Fehler, unter B ein Typfehler. **Hängt an:** OP-11, OP-30, OP-33,
OP-37, OP-39.

### OP-4 — Was passiert bei `a < b < c`?

**Heute:** linksassoziativ, `(a < b) < c`, `LYR-SEM0003 … 'bool' and 'int'` (`p02_chain.lyr`).

| Option | Vorbild | Preis |
|---|---|---|
| A — so lassen | C, C#, Java, Kotlin, Go | Die Diagnose nennt `'bool' and 'int'` statt der Ursache |
| B — Vergleiche nicht-assoziativ | Rust, Swift, Haskell | Parse-Fehler mit dem richtigen Satz |
| C — Python-Kettenvergleich | Python | Zweiter Mechanismus für „und"; Auswertungsregel für `b` kollidiert mit OP-21 |

**Empfehlung: B.** **Bruch: minor.** **Vertrauen: gemessen.** **Regelort:** Tabelle
(`02-grammar.md` §6.1 + `docs/Grammar.md` §6.1) + ein Satz in `06-operators.md` §6.2.
**Retiriert:** nichts (kein Fall pinnt `(a<b)<c`; `ls 06-operators/`). **Hängt an:** OP-1.

### OP-5 — Bleibt die Form `Add<T, R>`?

**Heute:** `fn add(other: T): R` (`core.lyr:358`); Mehrfachkonformanz seit 3.0
(**`STATUS.md:2478-2496`**, Satz „usable as VALUES" auf **`:2493`**); normativ
`06-operators.md:29-33`.

| Option | Vorbild | Preis |
|---|---|---|
| A — `Add<T, R>` behalten | .NET 7 `IAdditionOperators<TSelf,TOther,TResult>` | `Add<Vec2, Vec2>` schreibt „Vec2" zweimal |
| B — `Add<T>` mit `Self` als Ergebnis | Swift `AdditiveArithmetic` | Braucht `Self` in Interfaces + Objektsicherheitsregel; heterogene Ergebnisse fallen weg |
| C — `Add<T>` mit assoziiertem Ergebnistyp | Rust `type Output` | Neues Sprachmittel; Interface-Werte gehen nicht mehr |
| D — Default-Typargument `Add<T = Self, R = Self>` | Rust `Rhs = Self` | Zwei neue Sprachmittel für Schreibgewinn |

**Empfehlung: A.** B/C/D kosten je ein Sprachmittel. Aber A **trägt einen Konflikt mit**, keine
„Lücke": `06-operators.md:4-6` verspricht den Operator „resolved exactly as the written call would
be", `STATUS.md:2493` „usable as VALUES", und gemessen greift kein Operator durch einen
Interface-Wert (§1.2: `Add`, `Equatable`, `Ordered`, `Into`) — außer `Display` in der
Interpolation. Die Antwort darauf ist eine Regel über **alle** Operator-Interfaces und steht in
OP-42, nicht pro Interface.

**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.1 —
und OP-42 für den Wert-Satz. **Retiriert:** nichts. **Hängt an:** OP-42 (bindend); `Self`-Entscheid
aus Generics/Interfaces (`PLAN.md:260`, P1-Position 4) — kommt `Self`, ist B neu zu bewerten.

### OP-6 — Welche Operator-Interfaces kommen dazu — und ist das eine v5-Frage?

**Heute:** sechs Operatoren haben eins, zehn nicht (§1.3). **Aktenstand:** `PLAN.md:297-298`
hängt `Neg`, `Rem`, Bit-Operatoren und `in` über `Contains<T>` an die Inkrement-Runde von 4.7 —
„**eine Runde, nicht zwei**". `Index<K,V>`/`IndexSet<K,V>` ist `PLAN.md:283` Position 13, die
Collection-Literale `:284` Position 14.

| Interface | Operator | Anmerkung |
|---|---|---|
| `Neg` | unäres `-` | Unstrittig; Rust, Kotlin, Python, C#, Scala |
| `Rem` | `%` | `docs/guide/07-interfaces.md:350` und `06-operators.md:12` sagen „numeric-only" — beide fallen. **Der Doc-Kommentar muss sagen, was die eingebauten Floats tun** (`d08`: `fmod`, `x % 0.0` = `NaN` ohne Panik) und was die eingebauten Ints tun (§3.2: Panik) — „wie bei `Div`" reicht nicht, weil `Div` selbst zwei Verhalten hat. Siehe OP-41 |
| `BitAnd`/`BitOr`/`BitXor`/`BitNot`/`Shl`/`Shr` | `& \| ^ ~ << >>` | Sechs Interfaces; Kotlins Gegenentscheidung (Infix-Funktionen) ist die Rule-2-Alternative |
| `Contains<T>` | `x in xs` | Grammatikänderung: `in` binär, Stufe in der Tabelle, Wirkung auf §6.7 (OP-24) |
| `Not` | `!` | Kollidiert mit dem Postfix-`!` (Stufe 1) |
| `Index<K, V>` / `IndexSet<K, V>` | `m[k]`, `m[k] = v` | Ersetzt `Indexable<T>`; **der einzige BRUCH** |
| `Pow` | `**` | Neuer Token; nur Python/Haskell |

**Empfehlung, innerhalb der gesetzten 4.7-Runde:** `Neg`, `Rem`, `Contains<T>` ja; Bitoperatoren
nein oder als EIN Interface; `Not`/`Pow` nein. Die Staffelung der zweiten Fassung („sofort" /
„später") ist zurückgenommen — `PLAN.md:298` sagt eine Runde. **Was davon v5 ist:** allein
`Index<K,V>` (Bruch, Position 13) und die Bit-Entscheidung, falls sie Sprachmittel kostet. Preis
für `Rem`: OP-22 (`x % 0` auf Benutzertypen) und OP-41 (Float-Verhalten als Vorbild).

**Bruch:** Neg/Rem/Contains nein; `Index<K,V>` major. **Warnstufe 4.x:** `@Deprecated` auf
`Indexable<T>` ist heute **unschreibbar** (`r9c_depr_iface.lyr`: `LYR-PAR0042: an attribute
cannot sit on an interface`; Kontrolle `r9d` auf Struct feuert `SEM0076`) — setzt OP-29 voraus.
**Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.1 („`%` is numeric-only"
fällt), §6.7 für `in`, Tabelle in `02-grammar.md`. **Retiriert:** nichts in `06-operators/`; ob
ein Collections-Fall `Indexable` pinnt, ist **nicht geprüft (behauptet: keiner)**. **Hängt an:**
OP-22, OP-24, OP-29, OP-41, OP-43, Collection-Literale.

### OP-7 — Woher kommt `+1`? — Aktenstand: entschieden, aus `Add`

**Heute:** `IsNumeric` (`SPEC-RUNDE.md:19-20`); `p09_inc_user.lyr`: `v++` auf `Add<int, V>` ist
`LYR-SEM0003`; **`f++` auf `float` läuft** (`d01`, §1.5). Normativ `06-operators.md:26-27`.
**Gepinnt:** `increment_has_the_classic_values.lyr:5-6` (`let post = i++; let pre = ++i;` —
beide Schreibweisen in AUSDRUCKSposition).

**Aktenstand, den die zweite Fassung nicht nannte:** `PLAN.md:282` „entschieden, nicht gebaut";
`PLAN.md:292-295` Richtung A (aus `Add<T, R>`), Rule-2-Begründung „kein zweiter Mechanismus für
„plus eins"". Die zweite Fassung empfahl C (streichen) und nannte die Entscheidung nirgends. Das
war ein Verstoß gegen die eigene Belegpflicht, kein Argument.

| Option | Vorbild | Preis |
|---|---|---|
| A — aus `Add<int, T>` ableiten (`x++` ⇒ `x.add(1)`) — **gesetzt für 4.7** | — | Rule 2 auf der richtigen Seite. Zielregel (`p.x++`) und Statement-Form „im selben Satz" (`PLAN.md:295-296`). Der Ausdruckswert (`a[i++] = i++`) bleibt — das ist die Frage von OP-33 |
| B — eigene `Inc`/`Dec` | Kotlin, C# | Zweiter Mechanismus — vom Maintainer verworfen |
| C — `++`/`--` streichen | Swift SE-0004 | **Major**-Bruch; retiriert `increment_has_the_classic_values.lyr`; braucht Uhr + `lyrfix` (OP-30/31/46). Gewinn: ein Operator weniger, OP-8/OP-9/OP-33 entfallen |
| D — nur postfix, nur Statement | Go | = OP-33 B, **auf A aufsetzbar** |

**Empfehlung: A, wie gesetzt — und die v5-Frage ist OP-33, nicht OP-7.** Die zweite Fassung
begründete C mit (1) „Lyric hat die Form doppelt" (`x += 1` neben `x++`) — das trifft genauso
`a = a + 1` neben `a += 1`, das niemand streicht; das Argument trägt nicht. (2) „der Streit um die
Zielregel entfällt nur mit C" — falsch, A klärt ihn ausdrücklich mit (`PLAN.md:295-296`). (3)
Swifts Falle `a[i++] = i++` — die verschwindet unter D/OP-33 B genauso, ohne Bruch des Operators
selbst. **Was von C bleibt:** es ist die einzige Option, die Rule 2 *noch* strenger bedient als A
(kein Mechanismus statt einer). Aber die Rule-2-Begründung des Maintainers richtet sich gegen B,
nicht gegen C; C widerspricht ihr also nicht — C widerspricht der **Entscheidung**, und dafür
braucht es neue Evidenz. Die gibt es nicht: alle drei Befunde dieses Dossiers (`f++` auf float,
`p.x++`, `++x;`) sind unter A lösbar.

**Bruch:** A minor (Typen ohne `Add<int, Self>` verlieren nichts, sie hatten nichts). **Vertrauen:
gemessen + gelesen.** **Regelort:** `06-operators.md:26-27` (der Satz wird umgeschrieben: „on a
type with `Add<int, Self>`" statt „on integer variables"; dazu OP-35). **Retiriert:** unter A
nichts; unter C `increment_has_the_classic_values.lyr`. **Hängt an:** OP-33 (Ausdruckswert),
OP-35 (float), OP-43 (Zuständigkeit 4.7/5.0), OP-8, OP-9.

### OP-8 — Worauf darf ein Inkrement stehen?

**Heute:** nur Local/Parameter; `p.x++`, `xs[0]++` sind `LYR-IR0001` (`r2b`, `r2c`); `p.x += 1`
läuft (`r2a`). **Aktenstand:** `PLAN.md:186-189`: die Frage ist **offen** und gehört in die
4.7-Runde (Position 12); `IR0001` ist dort ausdrücklich **keine** falsch benannte Grenze.

| Option | Preis |
|---|---|
| A — jedes gültige Zuweisungsziel, wie `+=` | `LowerIncDec` nimmt den lvalue-Pfad von `+=`; `06-operators.md:26` („variables") wird umgeschrieben |
| B — Einschränkung bleibt, Code wird `SEM` | **Zurückgezogen.** Die zweite Fassung stützte B auf eine verkehrt gelesene Quelle (§1.5). Solange die Runde nicht entschieden hat, ist `p.x++` „valid Lyric this implementation cannot lower" (`12-diagnostics.md:70-71`) — `IR0001` ist der richtige Code |
| C — entfällt (OP-7 C) | — |

**Empfehlung: A, in der 4.7-Runde.** Unter OP-7 A ist `p.x++` genau `p.x += 1` über `Add`, und
das läuft (für eingebaute Typen; für Interface-Typen erst mit OP-10 A). Zwei Schreibweisen mit
verschiedenen Zielregeln sind der einzige Grund, warum die Frage überhaupt offen ist.

**Bruch:** nein (erlaubt mehr). **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md:26`.
**Retiriert:** nichts. **Hängt an:** OP-7, OP-10 (Interface-Ziele), OP-43.

### OP-9 — Was darf als Ausdrucks-Statement stehen?

**Heute:** `06-operators.md:127`: „a call, an assignment, or `resume`" — drei Formen. Der Compiler
akzeptiert fünf: dazu `throw e;` und `x++;`, **aber nicht `++x;`** (`LYR-SEM0022`, `r2d`). Gelesen,
`SPEC-RUNDE.md:44-48`: derselbe Code, „den einzigen Konformanzfall … benutzt beide Schreibweisen
nur in Ausdrucksposition, und im Guide steht kein einziges Inkrement-Statement".

| Option | Vorbild | Preis |
|---|---|---|
| A — Liste erweitern (Aufruf, Zuweisung, `resume`, `throw`, Inkrement beide Schreibweisen) | — | Wahrheitspflege; Spec-Änderung |
| B — `++x;` ablehnen, begründet | Go (kein Präfix) | Heute Zufall der Lowering-Struktur |
| C — entfällt (OP-7 C) | | |

**Empfehlung: A — in der 4.7-Runde** (`PLAN.md:295-296`: „die Statement-Form beider
Schreibweisen … im selben Satz"). B nur, wenn OP-33 B gewinnt (dann fällt Präfix ganz).

**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.8 +
`docs/Grammar.md:428` + `SEM0022`-Text. **Retiriert:** nichts. **Hängt an:** OP-7, OP-33, OP-43.

### OP-10 — Wie funktioniert Compound Assignment — und welcher Pin fällt?

**Heute: drei Regeln** (§1.6). Eingebaut: jedes lvalue. Interface: nur Local/gefangene Variable.
Logisch: jedes lvalue. Normativ `06-operators.md:104-108`, **gepinnt
`compound_on_field_is_refused.lyr:19`**, in 4.6 bewusst dokumentiert (`:97-99`) — und durch
`:101-102` sowie durch `d17` (Index einmal ausgewertet) widerlegt.

| Option | Vorbild | Preis |
|---|---|---|
| A — eine Regel: jedes lvalue, auch über Interfaces; **Pin retirieren** | Kotlin, C#, Python | Lowering wertet Objekt/Index in ein Temp, ruft `get`/`add`/`set`. Der eingebaute und der `??=`-Pfad tun es bereits (`d17`) |
| B — Einschränkung bleibt; `:101` wird eingeschränkt | — | Zwei Zielregeln für eine Syntax bleiben; die Begründung „would evaluate twice" muss trotzdem weg, weil sie falsch ist |
| C — `AddAssign` usw. | Rust | Rusts Grund ist In-Place; trifft `string` (OP-28), nicht `Vec2`; vier bis zehn Interfaces |
| D — Compound streichen | — | Niemand |

**Empfehlung: A — als Retirierung eines Pins, nicht als Nachziehen einer Implementierung.** Die
zweite Fassung schrieb „keine semantische Notwendigkeit, sondern eine nicht nachgezogene
Implementierung". Die Regel ist gepinnt und bewusst; was falsch ist, ist ihre Begründung (`:107`),
weil `:101` das Gegenteil verspricht und der Compiler es auf zwei Pfaden hält. A heißt: Regel-PR,
der `:104-108` streicht, `compound_on_field_is_refused.lyr` retiriert und einen Zwilling
`compound_on_field_calls_once.lyr` (`since:`-gegated) setzt.

**Bruch:** nein (erlaubt mehr). **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md`
§6.5 + `docs/guide/07-interfaces.md:350-353`. **Retiriert: `compound_on_field_is_refused.lyr`.**
**Hängt an:** OP-21/OP-38 (Reihenfolge Ziel → Index → rechte Seite muss vorher stehen), OP-28,
OP-6 (`%=` usw.).

### OP-11 — Welchen Typ hat `a ??= b`?

**Heute: `?T`** (`p23`; neu `d15`: `let r: int = (o ??= 3)` ist `SEM0001 cannot assign '?int' to
'int'`). **Und das ist die allgemeine Regel, nicht die Ausnahme:** `(n += 1)` ist `int`, `(p.x =
3)` ist `int` (`d05`) — der Wert einer Zuweisung hat den Typ des Ziels. §6.5 sagt zum Ergebnistyp
nichts.

| Option | Vorbild | Preis |
|---|---|---|
| A — `?T`: Typ des Ziels, wie jeder Compound (heute) | C# für **Referenztypen**; Lyrics eigene Regel aus `d05` | Der Wert ist garantiert nicht null, der Typ sagt es nicht; `(o ??= 3)!` |
| B — `T`: benannte Ausnahme | **C# für Nullable-WERTTYPEN** (`int? o = null; int y = o ??= 3;` kompiliert — der Typ ist `T`, nicht `T?`) | `??=` wird der einzige Compound, dessen Wert nicht den Typ des Ziels hat; eine zweite Regel für eine Form |
| C — `void` (OP-3 B) | Rust, Swift | Die Frage entfällt |

**Empfehlung: C, andernfalls A.** Die zweite Fassung empfahl B und nannte C# als Vorbild für A —
**verkehrt herum**: für den relevanten Fall (`T?`) ist C# das Vorbild für B. Und B ist gemessen
die Ausnahme von einer Regel, die für alle anderen Compounds gilt. Wer B will, muss entweder die
Zielregel für alle Compounds ändern (OP-37) oder `??=` als benannte Ausnahme begründen — C#s
Präzedenz reicht dafür nicht gegen Rule 2. Die richtige Antwort auf „der Typ sagt nicht, dass `o`
jetzt einen Wert hat" ist Flussverengung von `o` **nach** dem Statement, nicht ein anderer
Ausdruckstyp.

**Bruch:** A nein; B minor. **Vertrauen: gemessen.** **Regelort:** `06-operators.md` §6.5
(Ergebnistyp-Satz fehlt ganz — OP-37). **Retiriert:** nichts. **Hängt an:** OP-3, OP-37.

### OP-12 — Wie wird `3 * v` schreibbar?

**Heute: gar nicht** (`q8_reverse.lyr`, `LYR-SEM0041`).

| Option | Vorbild | Preis |
|---|---|---|
| A — Orphan-Regel lockern: irgendein Typ der Konformanz lokal | Rust RFC 2451 | Kollisionen erst beim Import; `06-operators.md:40-42` regelt sie bereits (`SEM0083` an der Benutzungsstelle) |
| B — gespiegelte Methode (`mulRev`) | Python `__rmul__` | Zweiter Mechanismus |
| C — so lassen | — | `3 * v` bleibt `v * 3` |
| D — nur dem Modul erlauben, das den ARGUMENTTYP deklariert | — | Präzise Fassung von A |

**Empfehlung: D.** **Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:**
`04-modules.md` (Orphan-Regel) — anderes Gebiet. **Retiriert:** nichts. **Hängt an:**
Module/Extensions; dort stehen zwei ungelöste `extend`-Befunde: **`STATUS.md:2249-2254`** (`extend`
auf Array-Typ kompiliert und tut nichts) und **`:2255-2259`** (`extend` auf opaque Typ, Methoden
von nirgends erreichbar). Die zweite Fassung zitierte `:2225-2236` — dort stehen `lyrtest`-Isolation
und REPL-Re-Run.

### OP-13 — Wie wählt `as` sein Ziel, wenn ein Typ mehrere Konversionen hat?

**Heute: es kann keine mehreren geben** (`q13_into.lyr`, `LYR-SEM0085` + 2× `SEM0086`). Normativ
`06-operators.md:86-87`.

| Option | Vorbild | Preis |
|---|---|---|
| A — Auswahl nach dem erwarteten Typ | Rust `Into`, Haskell | Kollidiert mit OP-27 A |
| B — `fn into(marker: …)` | — | Dummy-Parameter |
| C — `From<T>` auf dem ZIEL | Rust bevorzugt `From` | Braucht statische Interface-Member (`PLAN.md:260`, P1-4); Überladung trennt mehrere `from` korrekt |
| D — so lassen | — | `Celsius as Fahrenheit` UND `as Kelvin` unmöglich |

**Empfehlung: C, mit A als Fallback.** **Bruch: major** (C). **Warnstufe 4.x:** `@Deprecated` auf
`Into<T>` ist unschreibbar (`r9c`, `PAR0042`) — OP-29. **Vertrauen: gemessen.** **Regelort:**
`06-operators.md` §6.4 + `05-interfaces.md` + `11-stdlib-contract.md`. **Retiriert:** nichts
in `06-operators/` (behauptet: kein `Into`-Fall dort; `ls` zeigt keinen). **Hängt an:** P1-4,
OP-29, OP-27, OP-42 (Into durch Interface-Wert, `d12`).

### OP-14 — Nimmt der `if`-Ausdruck Blöcke?

**Heute: nein** (`docs/Grammar.md:505`; `p14_ifblock.lyr`: fünf Diagnosen). `design/value-block.md:150`
führt es als offene Frage 2.

| Option | Vorbild | Preis |
|---|---|---|
| A — `IfExpr = 'if' '(' Expr ')' ( Expr \| ValueBlock ) 'else' ( Expr \| ValueBlock \| IfExpr )` | Kotlin, Scala, Rust | Parser nach Position, Sema nach Kontext — wie beim `match` |
| B — Diagnose reparieren, sonst lassen | — | Ankömmlinge aus Kotlin/Rust verstehen fünf Fehler nicht |
| C — allgemeiner Block-Ausdruck | Rust, Scala | Verworfen: `design/value-block.md:143`, **`PLAN.md:406`/`:414`** |

**Empfehlung: A** (B ist unabhängig davon nötig). Adaption des Tails: OP-32.
**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `02-grammar.md` §6.2 +
`06-operators.md` §6.9 + `docs/Grammar.md:505`. **Retiriert:** nichts. **Hängt an:** OP-15, OP-32,
`try`-Ausdruck (**`PLAN.md:264`**, P1-Position 8).

### OP-15 — Wo darf ein Value-Block sonst noch stehen?

**Heute:** `match`-Arm und Block-Lambda (`design/value-block.md:27-29`; `p13`/`q17`).

| Kandidat | Vorbild | Empfehlung |
|---|---|---|
| `if`-Zweig | Kotlin, Rust | **Ja** (OP-14) |
| `try`/`catch`-Zweig | Kotlin | **Ja**, mit dem `try`-Ausdruck (`PLAN.md:264`) |
| Funktionskörper | Rust, Scala | **Nein** — `design/value-block.md:148` |
| freier Block als Ausdruck | Rust, Scala | **Nein** — `PLAN.md:406` |
| `break value` | Zig, Rust | **Nein** — `PLAN.md:406`, `design/loop-labels.md` |

**Nicht gestellte Frage:** Ausdruckskörper für Funktionen (`fn f(): int = expr;`, Kotlin). Dritte
Körperform, Rule-2-Gespräch. Empfehlung: prüfen, nicht entscheiden.

**Bruch:** nein. **Vertrauen: gelesen.** **Regelort:** `02-grammar.md` §6.2 + `06-operators.md`
§6.9. **Retiriert:** nichts. **Hängt an:** OP-14, OP-32.

### OP-16 — Bleibt `never` auf Rückgabe- und `throws`-Position beschränkt?

**Heute: nicht beschränkt, und jede andere Position bricht den Compiler** (`LYR-CLI0020`, drei
Formen, `r6a-c`; Kontrolle `r6d`).

| Option | Vorbild | Preis |
|---|---|---|
| A — nur Rückgabe/`throws`, sonst `LYR-SEM`-neu | **stables Rust** (`!` nur als Rückgabetyp; sonst `never_type`-Gate, `Infallible` als Ersatz), **Zig `noreturn`** | Schließt einen Compilerabsturz |
| B — voller Bottom-Typ | **Swift `Never`** (unbewohntes Enum in jeder Typposition, `Result<T, Never>`, seit 5.9 `Equatable`/`Hashable`), Rust nightly | Sonderfälle im Typsystem; `design/throw-expression.md:126` und `PLAN.md:406` („`never` als allgemeiner Typ") lehnen es ab |

**Empfehlung: A — als 4.x-BUGFIX.** **Korrektur der Vorbildspalte:** die zweite Fassung führte
Swift unter A und Rust unter B — beides verkehrt. Swift ist das Beispiel für B, stables Rust
praktiziert A.

**Bruch:** minor. **Vertrauen: gemessen** (Absturz), **gelesen** (Designnotiz, PLAN). **Regelort:**
`03-types.md` + `appendix-a-diagnostics.md`; Bugfix-PR. **Retiriert:** nichts. **Hängt an:** typed
throws (`PLAN.md:261-263`, `throws never`).

### OP-17 — Bleibt `comptime` ein Präfixoperator?

**Heute:** ja, auf `UnaryExpr` (`p19`/`p20`); nicht in `docs/Grammar.md:81-85`.

| Option | Vorbild | Preis |
|---|---|---|
| A — so lassen | — | Klammern im häufigen Fall (`16-building.md:302-303`) |
| B — bis zur Zuweisungsstufe | — | `comptime a + f()` wird ein Comptime-Aufruf ohne dass es dasteht |
| C — `comptime { … }` daneben | Zig | Rule 2 |
| D — A + kontextuelle Tabelle | — | Doku |

**Empfehlung: A + D.** **Bruch:** nein. **Vertrauen: gemessen.** **Regelort:** `02-grammar.md`
§1.4 + `docs/Grammar.md:81-85`. **Retiriert:** nichts. **Hängt an:** OP-34, OP-44.

### OP-18 — Wie wird Überlaufprüfung ausgedrückt?

**Heute:** gar nicht. `03-types.md:49-64` §3.2 legt Wrapping fest — und **beschneidet die
Optionsmenge**: gelesen `:51-53` „overflow is not an error, not undefined, and **not
configurable**"; `:62-64` „*A future checked mode would be a new construct, never a change to
these operators.*" (frozen 2026-08-19). `PLAN.md:290` Position 20 führt `checked { … }`.

| Option | Vorbild | Preis |
|---|---|---|
| A — `checked { … }`-Block | C# | Ein neues Konstrukt — **aber ändert, was `+` im Block tut**. Ob das „a new construct" oder „a change to these operators" im Sinn von `:63` ist, muss §3.2 selbst beantworten, bevor A zulässig ist |
| B — eigene Operatoren `+\|` (sättigend), `+!` (prüfend) | Zig (`+%`, `+\|`) | Neue Token, `+` bleibt unverändert — **eindeutig „a new construct"**. `+%` ist überflüssig, weil `+` in Lyric schon wickelt |
| C — Methoden `addChecked`, `addSaturating` | Rust (`checked_add`, `saturating_add`) | Nur Bibliothek |
| ~~D — Compilerschalter pro Profil~~ | Rust `overflow-checks`, Zigs `+` | **Normativ ausgeschlossen** (`:53` „not configurable"). Die zweite Fassung verhandelte D als offen — sie zitierte §3.2 nur für „Wrapping" |

**Empfehlung: C, dann B — A erst, wenn §3.2 sagt, dass ein Block kein „change to these operators"
ist.** Was für alle gilt: ein Benutzer-`Add` darf nicht werfen (`r8b`, `SEM0042`), also ist
`checked` für Benutzertypen bedeutungslos (A ist strukturell eine Halbregel); und die
comptime-Faltung muss dasselbe tun wie die Laufzeit — heute wahr (`r16`, **`d02`, `d16`/`d16b`**,
auch für `MIN / -1`).

**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `03-types.md` §3.2 +
`06-operators.md` §6.1. **Retiriert:** nichts. **Hängt an:** Arithmetikgebiet, OP-22, OP-34.

### OP-19 — Wie lauten die Diagnosen, wenn ein Operator fehlt?

**Heute: zweigeteilt** (`p08_missing.lyr`): vier Operatoren nennen das Interface, sechs sagen nur
„not applicable". Dazu: lokales `Add` → Rat zeigt auf das, was dasteht (`q9`); `Into<int>` durch
Interface-Wert → Rat „give 'Into<int>' the conformance :: [Into<int>]" (`d12`, absurd); `p.x++` →
`IR0001` (**richtig**, solange die 4.7-Runde nicht entschieden hat, §1.5).

**Empfehlung:** „not applicable" bekommt den Div-Satzbau, sobald das Interface existiert (OP-6);
für Operatoren ohne Interface (`!`, `**`) sagt sie das; der Interface-Wert-Fall bekommt einen
eigenen Satz (OP-42). Die zweite Fassung wollte `IR0001` für `p.x++` umkodieren — zurückgezogen.

**Bruch:** nein. **Vertrauen: gemessen.** **Regelort:** `appendix-a-diagnostics.md` +
`12-diagnostics.md` §12.1. **Retiriert:** nichts. **Hängt an:** OP-6, OP-42.

### OP-20 — Zwei Fragen, die heute niemand stellt

**a) Operatoren auf Optionals.** `?T == ?T` ist `SEM0059` (normativ `:60`); **`PLAN.md:406`**
schließt „Ordnung auf Optionals" aus; `PLAN.md:257` P1-1 bringt `?T == ?T`. Bleibt Gleichheit die
einzige Operation, die durch ein Optional reicht? **Empfehlung: ja** (Swift: `==` auf `Optional`
definiert, `<` 2016 entfernt). Dazu die vierfache `null`-Frage aus `06-operators.md:70-74`.

**b) Ein Aufruf-Operator** (`Callable<A, R>`, Kotlin `invoke`, Scala `apply`). **Empfehlung: nein**
— zweiter Mechanismus für „Funktionswert".

**Bruch:** nein. **Vertrauen:** gemessen (a), gelesen (Nachtrag), behauptet (b). **Regelort:**
`06-operators.md` §6.2/6.3. **Retiriert:** nichts. **Hängt an:** Optional-Gebiet.

### OP-21 — Garantiert v5 Auswertung von links nach rechts?

**Heute: der Compiler tut es überall, festgeschrieben ist es nirgends.** Gemessen `r7_evalorder.lyr`
(binärer Operand, Argumente, Array-Literal, Struct-Initializer) und neu `d06` (Zuweisung mit
Index, Compound mit Index, Empfänger vor Argument, `&&`). Gegrept: kein Treffer in der Spec.

**Korrektur der Vorbildspalte.** Die zweite Fassung nannte „Rust, Go: links nach rechts,
überall". Falsch an der Stelle, auf die es ankommt: **Rust** wertet bei einer Zuweisung die
**rechte Seite zuerst**, dann das Ziel (Rust Reference, Assignment expressions; Compound auf
Primitiven ebenso). **Go** garantiert lexikalische Links-nach-rechts-Ordnung nur für Aufrufe,
Receive-Operationen und logische Binäroperatoren; Index- gegen Aufrufauswertung ist ausdrücklich
„not specified". „Wie Rust/Go" ist also keine Antwort; A muss die Fälle aufzählen.

| Option | Vorbild | Preis |
|---|---|---|
| A — L2R für Operanden, Argumente, Literale, Initializer; **Zuweisung: Ziel (Objekt, dann Index) VOR der rechten Seite; Empfänger vor Argumenten** | Java (JLS 15.7, 15.26.1), C# — beide werten das Ziel zuerst; Kotlin | Bindet den Optimierer; kostenlos, weil der Compiler es tut |
| B — unspezifiziert | C, C++ (vor 17), Zig, Go (teilweise) | `a + b` ist ein Methodenaufruf — unspezifiziert heißt: keine definierte Bedeutung |
| C — L2R für Argumente, Zuweisung wie Rust (rechts zuerst) | Rust | Zwei Regeln; widerspricht der Messung (`d06`) |

**Empfehlung: A, ausbuchstabiert** — der Zuweisungsteil steht als eigene Frage in OP-38, weil
er die eine Stelle ist, an der die Vorbilder auseinandergehen. **Muss VOR OP-10 A stehen.**

**Bruch:** nein. **Vertrauen: gemessen** (Lyric), behauptet (Java/C#/Rust/Go). **Regelort:**
`06-operators.md` (neuer §6.10). **Retiriert:** nichts. **Hängt an:** OP-10, OP-38, OP-4 C, OP-46.

### OP-22 — Darf ein Operator werfen?

**Heute: nein, unentschieden** (`r8b`, `SEM0042`; `core.lyr:358-362`). `core.lyr:376-380` sagt über
`Div`: „What a division by zero does is the type's own business; the built-in numerics panic" — der
Typ hat aber nur `panic`, weil er nicht werfen darf.

| Option | Vorbild | Preis |
|---|---|---|
| A — wurffrei festschreiben | Zig, Rust | Benutzer-`Div`/`Rem` bei `x / 0`: panic oder Sentinel |
| B — `throws`-polymorph | Swift `rethrows` | Braucht typed throws St. 3 (**`PLAN.md:263`**, P1-7); jeder `a + b` ein Wurfpunkt |
| C — nur `Div`/`Rem` | — | Sonderregel |

**Empfehlung: A, mit Satz in der Spec.** Preis: `Rem` (OP-6) muss `x % 0` beantworten — und für
die eingebauten Typen ist die Antwort gemessen ZWEIGETEILT (int: Panik; float: `NaN`, `d08`), was
der Doc-Kommentar sagen muss (OP-41).

**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.1 +
`11-stdlib-contract.md` + `core.lyr`. **Retiriert:** nichts. **Hängt an:** OP-6, OP-18, OP-41,
typed throws St. 3 (`PLAN.md:263`).

### OP-23 — Welche Frage ist ein Spec-Regel-PR, welche eine Doku-Änderung — und welcher Pin fällt?

| Frage | Regelort | Klasse | Retiriert |
|---|---|---|---|
| OP-1 | `06-operators.md:6-7` (steht) | keine Änderung | — |
| OP-2 | `02-grammar.md` §6.2 + `docs/Grammar.md` | Regel-PR | — |
| OP-3 | `06-operators.md:91` + §6.8 | Regel-PR, major | **`assignment_chains_right.lyr`** (A und B) |
| OP-4 | Tabelle + §6.2 | Regel-PR, minor | — |
| OP-5 / OP-42 | `06-operators.md:4-6` + §6.1 | Regel-PR (Konflikt Spec ↔ Compiler) | — |
| OP-6 | §6.1 („numeric-only" fällt), §6.7, Tabelle | Regel-PR, 4.7-Runde; `Index<K,V>` major | — (behauptet für Collections) |
| OP-7 | `06-operators.md:26-27` | Regel-PR, 4.7-Runde (A gesetzt) | unter C: **`increment_has_the_classic_values.lyr`** |
| OP-8 | `:26` („variables") | Regel-PR, 4.7-Runde | — |
| OP-9 | §6.8 + `docs/Grammar.md:428` | Regel-PR, additiv | — |
| OP-10 | §6.5 (`:101` vs `:107`) | Regel-PR, Korrektur | **`compound_on_field_is_refused.lyr`** |
| OP-11 / OP-37 | §6.5 (Ergebnistyp fehlt) | Regel-PR, Lücke | — |
| OP-12 | `04-modules.md` | Regel-PR, abzugeben | — |
| OP-13 | §6.4 + `05-interfaces.md` + `11-stdlib-contract.md` | Regel-PR, major | — |
| OP-14/15/32 | `02-grammar.md` §6.2 + §6.9 | Regel-PR, additiv | — |
| OP-16 | `03-types.md` + Diagnosen | Bugfix | — |
| OP-17 | `02-grammar.md` §1.4 | Doku | — |
| OP-18 | `03-types.md` §3.2 (+ Lesart von `:63`) | Regel-PR, additiv | — |
| OP-19 | `appendix-a-diagnostics.md` | Diagnose | — |
| OP-20 | §6.2/6.3 | Regel-PR | — |
| OP-21 / OP-38 | neuer §6.10 | Regel-PR, Lücke | — |
| OP-22 | §6.1 + `11-stdlib-contract.md` | Regel-PR, Lücke | — |
| OP-24 | §6.7 (steht) | Prüfpflicht | — |
| OP-25 / OP-45 | `06-operators.md:114-115` **widerspricht dem RELEASED Compiler** | Regel-PR, rückwirkend `since: 4.6.0` | — (Zwilling fehlt) |
| OP-26 / OP-36 | `05-interfaces.md` + `11-stdlib-contract.md` | Regel-PR, additiv | — |
| OP-27 | `:37-39` + `03-types.md` §3.1 | Regel-PR, Lücke | — |
| OP-28 | keiner | Messung | — |
| OP-29 | `05-interfaces.md` + `core.lyr` | Regel-PR, additiv | — |
| OP-30 | `12-diagnostics.md` §12.5 | Regel-PR, Prozess | — |
| OP-31 / OP-46 | keiner (Werkzeug) | Build | — |
| OP-33 | `:26` + §6.8 | Regel-PR | unter B: **`increment_has_the_classic_values.lyr`** (`:5-6` sind Ausdrucksformen) |
| OP-34 / OP-44 | §6.1 + comptime-Kapitel; neuer `SEM`-Code | Regel-PR, Lücke | — |
| OP-35 | `:26` („integer" vs. Compiler) | Regel-PR + `since:`-Zwilling | — |
| OP-39 | Suite | Prozess | (die Spalte selbst) |
| OP-40 | `03-types.md:36-38` + `02-grammar.md` | Regel-PR, Präzisierung | — |
| OP-41 | `03-types.md:56` + `06-operators.md:11-12` | Regel-PR, Präzisierung | — |
| OP-43 | `PLAN.md` | Prozess | — |

**Vier Punkte sind in der Spec beantwortet** (§6.5:91, §6.1:26, §6.5:104-108, §6.9:132-139).
**Drei Punkte widersprechen dem Compiler** — die zweite Fassung zählte zwei: §6.6:114-115
(Interpolation, OP-25, **released**), §6.5:101 vs :107 (OP-10), **§6.1:26 „integer variables" vs.
`f++` (OP-35)**. Dazu der Konflikt §6.1:4-6 (Operator = geschriebener Aufruf) vs. Interface-Wert
(OP-42).

**Bruch:** kein eigener. **Vertrauen: gelesen** (jede Zeile gegen die Datei geprüft; die
Suite per `ls`). **Hängt an:** allen.

### OP-24 — Welche Vorschläge berühren die `<`-Auflösungsregel?

**Heute:** eigene normative Regel, `06-operators.md:120-123`; gemessen `r17_ltambig.lyr` in beide
Richtungen. OP-1 B/C/D **bricht** sie; OP-6 `in` verlangt Lookahead über `in`; `**` berührt den
Lexer; OP-4 B hilft; OP-7 C berührt sie nicht.

**Empfehlung: die Regel bleibt; jeder Tokenvorschlag trägt „Wirkung auf §6.7".** **Bruch:** nein.
**Vertrauen: gelesen + gemessen.** **Regelort:** §6.7. **Retiriert:** nichts. **Hängt an:** OP-1,
OP-6.

### OP-25 — Ruft die Interpolation `Display` oder nicht?

**Heute: der Compiler ruft `Display`, das Feature ist in 4.6.0 released und im Guide
dokumentiert, die Spec-Version 4.6 verbietet es, und kein Konformanzfall deckt es.** Belege in
§1.9: `CHANGELOG.md:13, :64-65`; `docs/guide/02-values-and-types.md:84-88`; Commit `219b3108`
(2026-09-22); `TypeChecker.cs:2989-2991` (beruft sich auf §6.6 für das Gegenteil);
`06-operators.md:114-115`; `lyric-spec/README.md:45` („describes Lyric 4.6"); `ls 06-operators/`
ohne Display-Fall; Spec-Log ohne Regel-PR.

| Option | Vorbild | Preis |
|---|---|---|
| A — `Display` ist der Anker; §6.6 wird korrigiert, **rückwirkend `since: 4.6.0`** | Rust (`{}` ⇒ `Display`), C#, Kotlin, Swift | Diagnose zeigt auf `Display`; Spezifizierer auf `Display`-Loch bleibt ein Fehler (Guide `:87-88`, `TypeChecker.cs:2993-2995`) |
| B — die Spec gewinnt; Compiler lehnt ab | — | **Nimmt ein RELEASED Feature zurück** — major |
| C — nur mit Spezifizierer | — | Rule 2 |

**Empfehlung: A.** Aber die zweite Fassung nannte den eigentlichen Befund nicht: das ist ein
**spec-first-Prozessverstoß** — der Compiler-Kommentar beruft sich auf §6.6, §6.6 sagt das
Gegenteil, der Pin wanderte auf 4.6 (`lyric-spec` `c2943ee`) ohne die Regel. Wie das nachgeholt
wird, steht in OP-45.

**Bruch:** nein (A). **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md` §6.6.
**Retiriert:** nichts; **Zwilling fehlt und muss nachgetragen werden.** **Hängt an:** OP-45,
OP-42 (Interface-Wert im Loch läuft bereits, `d11`), String-/Formatgebiet (**`PLAN.md:370`**).

### OP-26 — Kommt `==` auf einem Struct umsonst?

**Heute: nein, und `hash()` auch nicht** (`r11`, `r19b`). `PLAN.md:258` P1-2 „designt".

| Option | Vorbild | Preis |
|---|---|---|
| A — strukturelle Ableitung auf Anforderung (`:: [Equatable<P>]` ohne Methode synthetisiert) | Swift | Regel „alle Felder konform" |
| B — `@Derive` | Rust | Zweiter Mechanismus; `PLAN.md:406` schließt `derive` aus |
| C — Werttypen immer gleichheitsfähig | Go, C# `record` | Kein Opt-in |
| D — so lassen | — | Zwei Methoden von Hand |

**Empfehlung: A.** **Diese Frage beantwortet nur Structs (und Enums mit Konformanzliste).** Für
Tupel und Arrays gibt es keine Deklarationsstelle, an der A andocken könnte — OP-36.

**Bruch:** nein. **Vertrauen: gemessen.** **Regelort:** `05-interfaces.md` + `11-stdlib-contract.md`.
**Retiriert:** nichts. **Hängt an:** `PLAN.md:257-258` P1-1/2, OP-36.

### OP-27 — Wie greifen Operatorauswahl und Kontexttyp ineinander?

**Heute:** ein unsuffigiertes Literal adaptiert auch über einen Benutzer-Operator
(`r14_ctx_usermul.lyr`: `v * 2` mit `Mul<float, Vec2>` läuft); normativ `06-operators.md:37-39`.
Offen: entscheidet der KONTEXTTYP mit, wenn ein Typ mehrfach konformiert?

| Option | Vorbild | Preis |
|---|---|---|
| A — Argumente entscheiden, Kontext nie (heute) | Rust (Overload-Auswahl nie rückwärts) | `let r: B = v * 2;` ist ein Typfehler |
| B — Kontexttyp als Tiebreaker NACH den Argumenten | **Swift** — das einzige tragfähige Vorbild. **Korrektur:** C# ist keins: die Überladungsauflösung nutzt den Zieltyp nie; C# 9 hat zielgetypte `new()`/`?:` als ARGUMENTE eingeführt | Zweistufige Auswahl; Constraint-Solver-Kosten (das ist Swifts echtes Problem, §2.1) |
| C — gleichrangig | Haskell | Kollidiert mit `SEM0085` |

**Empfehlung: A, aufgeschrieben.** Konflikt mit OP-13 A bleibt — Grund mehr für OP-13 C.
**Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:** `:37-39` + `03-types.md` §3.1.
**Retiriert:** nichts. **Hängt an:** OP-13, OP-6, OP-32.

### OP-28 — Was kostet ein Operator?

1. **Dispatch:** statisch per Konstruktion (`06-operators.md:44-47`, `PLAN.md:265-268`), weil der
   Interface-Wert den Operator nicht trägt — **OP-42 A öffnet den ersten dynamischen
   Operatorpfad**, und zwar genau den, den `Display` in der Interpolation heute schon geht (`d11`).
2. **OP-10 A:** Objekt/Index in ein Temp, drei Aufrufe — behauptet: entspricht `p.v = p.v + w`.
3. **`s += x` in einer Schleife** ist über `std.string.concat` (`:49-50`) O(n²) in kopierten Bytes
   (erschlossen, nicht zeitlich gemessen) — Rusts Grund für `AddAssign`, gültig für `string`, nicht
   für `Vec2`.

**Empfehlung:** `std.string.Builder` (Java/C#/Go), `+=` bleibt naiv; Messung nachholen vor OP-10 A.
**Bruch:** nein. **Vertrauen:** gelesen (1), behauptet (2), gemessen+erschlossen (3). **Regelort:**
keiner. **Retiriert:** nichts. **Hängt an:** OP-42, OP-10.

### OP-29 — Bekommt v5 einen `OnInterface`-Anker?

**Heute: nein** (`r9c`: `PAR0042`; Kontrolle `r9d`; `core.lyr:483-500` kennt `OnModule`, `OnType`,
`OnFunction`, `OnMethod`). Zwei Migrationswege des Dossiers (OP-6, OP-13) sind damit unschreibbar.

**Empfehlung: A — `OnInterface` als vierter Anker (C# `AttributeTargets.Interface`, Java,
Kotlin), VOR OP-6 und OP-13.** **Bruch:** nein. **Vertrauen: gemessen + gelesen.** **Regelort:**
`05-interfaces.md`/Attributkapitel + `core.lyr` + `PAR0042`-Text. **Retiriert:** nichts. **Hängt
an:** nichts — OP-6 und OP-13 hängen an ihm.

### OP-30 — Wer warnt, und wird die Warnung gehört?

**Heute:** Uhrenfamilie §12.5 (`12-diagnostics.md:74-91`: `SEM0107`–`SEM0110`); `WarningAnalyzer`
läuft nur über fehlerfreie Programme (`PLAN.md:192-195`) — eine Uhr schweigt in genau den Dateien,
die umzubauen sind.

| Option | Vorbild | Preis |
|---|---|---|
| A — Uhren wie `SEM0107`–`SEM0110` | die vier | Stumm neben jedem Fehler |
| B — immer laufende Klasse für **syntaktisch** entscheidbare Uhren | **C# (Analyzer über halbe Syntaxbäume)** — **Korrektur:** „Rust (Lints über `--keep-going`)" ist gestrichen; `--keep-going` ist ein cargo-Flag, das nach einem Fehler die ÜBRIGEN Crates weiterbaut, und rustc-Lints laufen nach dem Typcheck gar nicht auf einer Crate mit Typfehlern | Braucht die Zusage, dass eine Warnung aus einem Syntaxbaum nicht lügt |
| C — Uhren in `lyrfix --dry-run` | Go `go fix`, Rust `cargo fix` | Braucht OP-31 |

**Empfehlung: B für `x++`-in-Ausdruck und Zuweisung-in-Bedingung (syntaktisch), A für
Deprecations (brauchen Namensauflösung).** Steht damit nur noch auf dem C#-Vorbild — das reicht,
weil das Argument technisch ist (Syntaxbaum genügt), nicht vorbildgestützt.

**Bruch:** nein. **Vertrauen: gelesen.** **Regelort:** `12-diagnostics.md` §12.5. **Retiriert:**
nichts. **Hängt an:** OP-3, OP-33, OP-31.

### OP-31 — Was ist das Minimum-`lyrfix`, und wann entsteht es?

**Heute: existiert nicht** (`src/` ohne `Lyrfix`; `PLAN.md:343` führt es als ungebaute
Voraussetzung).

| Frage | Umbau | Mechanisch? |
|---|---|---|
| OP-33 B / OP-7 C | `x++;` → `x += 1;` | Statement-Form ja; **Ausdrucksform: OP-46** |
| OP-3 B | `a = b = 7` → `b = 7; a = b;`; `if (f = true)` → zwei Statements | nur ohne Schleifenkopf |
| OP-6 `Index<K,V>` | Signaturersetzung | ja |
| OP-13 `From` | Code wandert ins Zielmodul | nein — nur Meldung |

**Empfehlung: A — `lyrfix` als eigener Meilenstein vor den Uhren** (Go `go fix`, Rust `cargo fix`
entstanden vor dem Bruch). Minimum: parsen, Knotenformen ersetzen, über `Lyrfmt` zurückschreiben,
nicht-mechanische Fälle nur melden.

**Bruch:** nein. **Vertrauen: gelesen.** **Regelort:** keiner. **Retiriert:** nichts. **Hängt an:**
OP-3, OP-6, OP-13, OP-30, OP-33, OP-46.

### OP-32 — Nimmt der Tail eines Value-Blocks an der Kontextadaption teil?

**Heute: für den `match`-Arm-Block ja** (`r13b`); normativ `06-operators.md:137-139`.
**Empfehlung: A — eine Regel in §6.9: „arm or the tail of a value block".** `let x: float = if (c)
{ 1 } else { 2 };` ist unter A gültig. **Bruch:** nein. **Vertrauen: gemessen + gelesen.**
**Regelort:** §6.9. **Retiriert:** nichts. **Hängt an:** OP-14, OP-15, `try` (`PLAN.md:264`).

### OP-33 — `++` bleibt (OP-7 A): Ausdruck oder Statement?

**Heute:** Ausdruck, normativ `:26-27`, gepinnt `increment_has_the_classic_values.lyr:5-6`;
`x++;` geht, `++x;` ist `SEM0022` (`r2d`); `y = x++` und `f(x++)` laufen (`d14`).

| Option | Vorbild | Preis |
|---|---|---|
| A — beide Schreibweisen als Statement UND als Ausdruck (OP-9 A) | C, C#, Java | Wahrheitspflege; `a[i++] = i++` bleibt |
| B — **nur postfix, nur Statement, kein Wert** | Go | Falle verschwindet; Bruch: jedes `y = x++`, `f(i++)`; **retiriert `increment_has_the_classic_values.lyr`** |
| C — streichen (OP-7 C) | Swift | Major; retiriert denselben Fall |

**Empfehlung: B — das ist die eigentliche v5-Frage dieses Blocks.** OP-7 A (4.7) sagt, woher `+1`
kommt; B sagt für 5.0, dass es keinen Wert liefert. B ist der einzige Vorschlag, der die
Swift-Falle schließt, ohne den Operator zu entfernen, und er setzt sauber auf A auf (Go hat genau
diese Kombination: Inkrement ja, Wert nein). Was B kostet, steht in OP-46: die Ausdrucksform hat
keinen rein textuellen Umbau.

**Bruch:** B minor bis major (jede Ausdrucksverwendung). **Warnstufe 4.x:** Warnung auf `++`/`--`
in Ausdrucksposition — syntaktisch, Klasse OP-30 B. **Vertrauen: gemessen + gelesen.**
**Regelort:** `:26-27` + §6.8. **Retiriert: `increment_has_the_classic_values.lyr`** (die
Zeilen `:5-6` sind genau die Ausdrucksform). **Hängt an:** OP-7, OP-30, OP-31, OP-46, OP-39.

### OP-34 — Was darf ein `comptime`-Ausdruck rechnen, und rechnet er dasselbe wie die Laufzeit?

**(a) Benutzercode läuft** (`r15`, `r15e`; Kontrollen `r15d`, `r15c`; `16-building.md:299-300`).

**(b) Faltung == Laufzeit — jetzt bewiesen.** Die zweite Fassung stützte das auf `r16`, das unter
`comptime` nie eine negative Zahl teilte (`0 - 7 / 2` = `0 - (7/2)`). **Neu** `d02`: `comptime
((0 - 7) / 2)` = `-3`, `% 2` = `-1`, Laufzeit gleich; `d16`/`d16b`: `MIN / -1` = `MIN`, `MIN % -1`
= `0` in beiden Welten (`03-types.md:57-58`); `r16`: `MAX + 1` wickelt in beiden.

| Frage | Heute | Warum jetzt |
|---|---|---|
| Faltung garantiert oder Optimierung? — **und diagnostiziert sie, oder substituiert sie nur?** | Gemessen: substituiert; `1 / 0` ohne `comptime` kompiliert (`d09`) | OP-44 |
| Faltung == Laufzeit bei Überlauf? | Gemessen: ja (jetzt vollständig) | OP-18 kann es brechen |
| Benutzeroperator in `comptime`? | Gemessen: ja | Sandbox-Frage; steht in keiner Spec-Zeile |

**Empfehlung: A — festschreiben, was gilt** (Zig-Modell; Rust `const fn` wäre eine zweite
Funktionsfarbe). Bindet OP-18 an eine modusfeste Antwort. **Bruch:** nein. **Vertrauen: gemessen
(mit Kontrolle) + gelesen.** **Regelort:** §6.1 + comptime-Kapitel. **Retiriert:** nichts. **Hängt
an:** OP-17, OP-18, OP-22, OP-44.

---

## 3b. Fragen, die die Kritik an der zweiten Fassung offengelegt hat

Zwölf Punkte, OP-35 bis OP-46. Jeder ist selbst nachgemessen oder nachgelesen; wo die Kritik
selbst irrte (OP-40), steht es dabei.

### OP-35 — Ist `++`/`--` auf `float` erlaubt?

**Heute: der Compiler ja, die Spec nein.** Gemessen `d01_float_inc.lyr`: `var f = 1.5; f++;`
kompiliert, druckt `2.5`; Kontrolle `d01b`: `s++` auf `string` ist `LYR-SEM0003`. Dazu `d18`: `f
+= 1` auf `float` läuft (Literal adaptiert, `03-types.md:29-32`). Gelesen `06-operators.md:26`:
„on integer variables". Gelesen `SPEC-RUNDE.md:19-20`: `IsNumeric`. **Kein Konformanzfall deckt
den Fall** (einziger `++`-Fall: `increment_has_the_classic_values.lyr`, auf `int`).

| Option | Vorbild | Preis |
|---|---|---|
| A — die Spec gewinnt: nur Integer, `f++` wird `SEM0003` | Go (`x++` auf jedem numerischen Typ — also NICHT Go); keine der neun Sprachen beschränkt `++` auf Integer, wo es `++` gibt (C, C#, Kotlin, Go: float erlaubt) | Minor-Bruch für Programme, die `f++` schreiben; keine Vorbildstütze |
| B — der Compiler gewinnt: „on a numeric variable" | C, C#, Kotlin, Go | Ein Wort in `:26`; konsistent mit `f += 1` (`d18`) |
| C — unter OP-7 A: erlaubt, wo `x + 1` erlaubt ist | — | Fällt mit B zusammen: `float + 1` ist die Literaladaption von §3.1, `Add<int, Self>` für Benutzertypen |

**Empfehlung: B, formuliert als C** — „`++` steht, wo `x += 1` steht". Dann sagt die Spec, was der
Compiler tut, und OP-7 A ändert nichts daran. **Der Zwilling:** `increment_on_float.lyr`
(`//! since: 4.7.0` oder die Version des Regel-PRs), `var f = 1.5; f++;` → `2.5`.

**Bruch:** nein (B/C); minor (A). **Vertrauen: gemessen + gelesen.** **Regelort:**
`06-operators.md:26`. **Retiriert:** nichts. **Hängt an:** OP-7, OP-43.

### OP-36 — Bekommen payload-lose Enums, Tupel und Arrays ein strukturelles `==`?

**Heute: nein, alle drei `SEM0059`** (`d03a-c`, §1.3), obwohl `match` denselben Enum-Wert
vergleicht. Für Tupel und Arrays fehlt die Deklarationsstelle für eine Konformanzliste; die
Diagnose gibt dort folgerichtig keinen Rat. OP-26 A (Synthese über die Konformanzliste,
`PLAN.md:258`) beantwortet nur Struct und Enum.

| Option | Vorbild | Preis |
|---|---|---|
| A — payload-loser Enum ist per Bauart `Equatable` (ohne Liste); Enum mit Payload synthetisiert über die Liste (OP-26 A) | Swift (Enums ohne assoziierte Werte automatisch `Equatable`) | Erstes Opt-out-freies `==` in Lyric — vertretbar, weil `match` denselben Vergleich schon ohne Opt-in macht |
| B — Tupel und Arrays über **bedingte Konformanz in std**: `extend<T :: [Equatable<T>]> T[] :: [Equatable<T[]>]`, Tupel bis Arität n | Swift (`Array<E: Equatable>`, Tupel bis 6), Rust (`PartialEq` für Tupel/Arrays) | Braucht `PLAN.md:259` P1-3; Tupel brauchen eine Extension-Form auf Tupeltypen, die es nicht gibt (behauptet) |
| C — alles per Bauart (Go) | Go (Structs/Arrays vergleichbar, Slices nicht) | Verschenkt das Opt-in; Go schließt Slices aus, Lyric hätte kein Kriterium dafür |
| D — so lassen | — | `xs == ys` bleibt unschreibbar |

**Empfehlung: A für Enums, B für Arrays; Tupel als offene Teilfrage an das Werte-Gebiet**
(ob ein Tupeltyp eine Extension trägt). Die Regel in `PLAN.md:258` muss den Enum-Fall
ausdrücklich nennen: „ohne Payload ohne Liste, mit Payload über die Liste".

**Bruch:** nein (additiv). **Vertrauen: gemessen** (Lyric), behauptet (Swift/Rust/Go, Tupel-
Extension). **Regelort:** `05-interfaces.md` (Synthese) + `11-stdlib-contract.md` (bedingte
Konformanzen). **Retiriert:** nichts. **Hängt an:** OP-26, `PLAN.md:257-259` P1-1/2/3,
Werte-Gebiet (Tupel).

### OP-37 — Welchen Typ und Wert hat JEDE Zuweisungsform?

**Heute:** gemessen `d05` (§1.7): Wert = neuer Wert des Ziels, Typ = Typ des Ziels — für `=` auf
Feld und Element, für `+=`, und für `??=` (`?int`, `p23`/`d15`); `a += b += 1` ist gültig und
rechtsassoziativ. Gelesen `06-operators.md:91-92`: nur „Assignment is an expression,
right-associative"; kein Wort zu Typ, Wert oder Compounds als Kettenglieder. Gepinnt:
`assignment_chains_right.lyr:6` (nur `=`).

| Option | Vorbild | Preis |
|---|---|---|
| A — Satz in §6.5: „the value is the target's new value, of the target's type; compounds chain the same way" | C, C#, Java | Schreibt fest, was gilt; macht OP-11 A zur Regel |
| B — `void` (OP-3 B) | Rust, Swift, Scala | Der Satz entfällt; **retiriert `assignment_chains_right.lyr`**, und `a += b += 1` fällt |
| C — Wert = Typ der rechten Seite | — | Für `??=` wäre das `T` (OP-11 B); für `+=` über `Add<T, R>` mit `R ≠ Self` undefiniert — unbrauchbar |

**Empfehlung: folgt OP-3.** Unter B entfällt die Frage; sonst A. C ist genannt, um OP-11 B
seinen einzigen systematischen Unterbau zu nehmen.

**Bruch:** A nein; B siehe OP-3. **Vertrauen: gemessen + gelesen.** **Regelort:** §6.5.
**Retiriert:** unter B `assignment_chains_right.lyr`. **Hängt an:** OP-3, OP-11.

### OP-38 — Zuweisung: Ziel vor rechter Seite? Empfänger vor Argumenten?

**Heute:** gemessen `d06`: `xs[f()] = g()` → `f`, `g`; `xs[f()] += g()` → `f`, `g`; `r().m(f())` →
`r`, `f`, `m`. Der Compiler wertet Ziel (Objekt, Index) vor der rechten Seite und den Empfänger vor
den Argumenten. Festgeschrieben: nirgends.

| Option | Vorbild | Preis |
|---|---|---|
| A — Ziel zuerst (Objekt, dann Index), dann rechte Seite; Empfänger, dann Argumente, dann Aufruf | **Java** (JLS 15.26.1: Array-Referenz, Index, dann rechte Seite), **C#** (Ziel vor Wert) | Schreibt das Gemessene fest; die Voraussetzung für OP-10 A („ONCE" auf `:101` ist eine Reihenfolgezusage) |
| B — rechte Seite zuerst | **Rust** (Assignment expressions: assigned value first) | Widerspricht der Messung; Umbau des Lowerings ohne Nutzen |
| C — unspezifiziert zwischen Index und Aufruf | **Go** („not specified") | Ein `a + b`, das ein Aufruf ist, hätte in `xs[i()] += v()` keine definierte Bedeutung |

**Empfehlung: A, als Teil von OP-21 A, aber mit eigenem Satz** — weil die drei Vorbilder hier
auseinandergehen und „wie Rust/Go" beides falsch wäre.

**Bruch:** nein. **Vertrauen: gemessen** (Lyric), behauptet (Java/C#/Rust/Go). **Regelort:** neuer
§6.10. **Retiriert:** nichts. **Hängt an:** OP-21, OP-10, OP-46.

### OP-39 — Welche bestehenden Konformanzfälle retiriert jeder Bruch?

**Heute:** die zweite Fassung nannte keinen. Gelesen, `ls lyric-spec/conformance/cases/06-operators/`
(16 Fälle) und die drei betroffenen Dateien. Die Projektregel „ein Pin, dessen Regel fällt,
retiriert MIT ihr" ist Arbeitsregel des Maintainers (behauptet; nicht in `README.md` gelesen —
`appendix-a-diagnostics.md` führt nur retirierte Nummern).

| Vorschlag | Retiriert | Zeile | Zwilling, `since:`-gegated |
|---|---|---|---|
| OP-3 A oder B | `assignment_chains_right.lyr` | `:6` `a = b = 3;` | `assignment_has_no_value.lyr` (error) |
| OP-10 A | `compound_on_field_is_refused.lyr` | `:19` `h.v += Vec { x = 1 };` | `compound_on_field_calls_once.lyr` (run) |
| OP-33 B oder OP-7 C | `increment_has_the_classic_values.lyr` | `:5-6` `let post = i++; let pre = ++i;` | `increment_is_a_statement.lyr` (error auf `let post = i++;`) |
| OP-4 B | — | — | `chained_comparison_is_refused.lyr` (neu) |
| OP-35 B | — | — | `increment_on_float.lyr` (neu) |
| OP-25 A | — | — | `interpolation_calls_display.lyr` (neu, `since: 4.6.0`) |
| OP-6 `Index<K,V>` | keiner in `06-operators/`; Collections nicht geprüft | | |

**Empfehlung:** die Spalte wird Pflichtfeld jeder Regel-PR-Zeile in OP-23 (dort eingetragen).
**Bruch:** kein eigener. **Vertrauen: gelesen.** **Regelort:** Suite. **Hängt an:** OP-3, OP-10,
OP-33, OP-4, OP-35, OP-25.

### OP-40 — Wo hängt das negative Literal am Rand, und gilt die Faltung durch Klammern?

**Heute:** gemessen (§1.9): `-9223372036854775808`, `- 9223372036854775808` und
**`-(9223372036854775808)`** kompilieren zu `MIN`; `0 - 9223372036854775808` und das nackte
Literal sind `LYR-SEM0001`. Gelesen `03-types.md:36-38`: „A leading `-` folds into the literal,
so … `-9223372036854775808` are literals, not unary expressions." **Die Kritik irrte:** die Regel
steht in der Spec. Was nicht steht: (1) `docs/Grammar.md:91` `IntLit` und `:470` `Primary` kennen
kein Vorzeichen — die Faltung hat keine Produktion; (2) „leading" sagt nicht, ob Klammern dazwischen
dürfen, und der Compiler faltet durch sie hindurch (Erwartung `SEM0001`, **nicht getroffen**).

| Option | Vorbild | Preis |
|---|---|---|
| A — Faltung ist Sema-Regel: ein unäres `-` unmittelbar auf einem Literal, **auch in Klammern**, ist das negative Literal | Lyric heute (gemessen) | Ein Satz in §3.1 („through parentheses") + ein Satz in §6.2 (OP-2), wo `Unary` das erklärt |
| B — nur das unmittelbar folgende Token | **C#** (`-2147483648` nur als Token direkt nach dem Minus; `-(2147483648)` ist ein `long`) | Minor-Bruch (Klammerform); dafür lexikalisch beschreibbar |
| C — kein Sonderfall; Wrapping nach §3.2 | — | `-9223372036854775808` würde `SEM0001` — Rückschritt gegen die Spec |

**Empfehlung: A, aufgeschrieben.** Der Compiler tut es, es ist die benutzerfreundlichere Regel,
und sie gehört an die Stelle, an der OP-2 `Unary` definiert.

**Bruch:** nein. **Vertrauen: gemessen + gelesen** (Lyric), behauptet (C#). **Regelort:**
`03-types.md:36-38` + `02-grammar.md` §6.2. **Retiriert:** nichts. **Hängt an:** OP-2.

### OP-41 — Welche Semantik hat `%` auf float, und paniken Float-Divisionen durch null?

**Heute:** gemessen `d08`: `-7.5 % 2.0` = `-1.5` (`fmod`, Vorzeichen des Dividenden), `1.0 / 0.0` =
`Infinity`, `0.0 / 0.0` = `NaN`, `1.0 % 0.0` = `NaN`; kein Panic. Gelesen `03-types.md:55-56`:
„remainder follows the sign of the dividend. Division by zero is a panic" — unter der
Integer-Überschrift, ohne Float-Ausnahme; `06-operators.md:11-12`: „float per IEEE 754" — und
IEEE 754s `remainder` ist round-to-nearest, gäbe `+0.5`, was der Compiler nicht tut.

| Option | Vorbild | Preis |
|---|---|---|
| A — festschreiben: `%` auf float ist `fmod` (Vorzeichen des Dividenden, wie int); float-Division durch null folgt IEEE (Inf/NaN), nur Integer panikt | C, C#, Java, Rust (`%` auf `f64` = fmod-Semantik), Go | Zwei Sätze; „per IEEE 754" wird auf `+ - * /` und die Sonderwerte präzisiert |
| B — IEEE-`remainder` | niemand als Operator (Python `math.remainder` als Funktion) | Bruch; unüblich |
| C — Float-Division durch null panikt | niemand | Bruch; zerstört `Infinity`/`NaN`-Arithmetik |

**Empfehlung: A.** `Rem` (OP-6) übernimmt den Satz in den Doc-Kommentar: „the built-in integers
panic on zero, the built-in floats give NaN" — „wie bei `Div`" wäre unvollständig, weil `Div` für
beide Typfamilien verschieden ist.

**Bruch:** nein. **Vertrauen: gemessen + gelesen** (Lyric), behauptet (Fremdsprachen). **Regelort:**
`03-types.md:56` + `06-operators.md:11-12` + `core.lyr` (`Div`/`Rem`-Doc). **Retiriert:** nichts.
**Hängt an:** OP-6, OP-22.

### OP-42 — Trägt ein Interface-WERT den Operator — für alle Operator-Interfaces oder keins?

**Heute: für keins außer `Display`.** Gemessen (§1.2): `Add` (`r5`), `Equatable` (`d04`), `Ordered`
(`d10`), `Into` (`d12`) lehnen ab; der Methodenaufruf läuft je (Kontrollen); `Display` im f-String
dispatcht durch den vtable (`d11`; Quelle `TypeChecker.cs:2998-3003`). Gelesen
`06-operators.md:4-6`: Operator „resolved exactly as the written call would be" — das verspricht
den Operator durch den Wert. Gelesen `STATUS.md:2493`: „usable as VALUES".

| Option | Vorbild | Preis |
|---|---|---|
| A — jeder Operator dispatcht durch den Wert, wie `Display` heute | C# Generic Math nicht (statische Member), **Rust `dyn Trait` für `PartialEq`/`Display` ja, für `Add` nein** (by-value `self`) — Lyric hat dieses Hindernis nicht: `add` nimmt `this` als Fat Pointer | Erster dynamischer Operatorpfad (OP-28); Checkerarbeit: `CheckBinary`/`CheckCast` bekommen den `NamedRef { Interface }`-Zweig, den `CheckDisplayHole` hat |
| B — Operatoren verlangen einen konkreten Typ; `Display` bleibt Ausnahme | — | `:4-6` und `STATUS.md:2493` werden eingeschränkt; eine benannte Ausnahme für ein Interface |
| C — auch `Display` verlangt den konkreten Typ | — | Nimmt ein 4.6.0-Verhalten zurück (`d11`) |

**Empfehlung: A, als EINE Regel über `Add`/`Sub`/`Mul`/`Div`/`Equatable`/`Ordered`/`Into`/
`Indexable`/`Display`.** Die Maschinerie steht (`Display`), die Spec verspricht es, die Entscheidung
hat dafür bezahlt. Pro Interface eine eigene Antwort wäre Rule 2. Preis ehrlich: der Operator auf
einem Wert kostet einen vtable-Aufruf — genau das, was `a.add(b)` heute kostet.

**Bruch:** nein (erlaubt mehr). **Vertrauen: gemessen + gelesen.** **Regelort:** `06-operators.md`
§6.1 (ein Satz nach `:4-6`) + `appendix-a-diagnostics.md` (die absurde `Into`-Diagnose). **Retiriert:**
nichts. **Hängt an:** OP-5, OP-13, OP-25, OP-28.

### OP-43 — Sind OP-6/OP-7/OP-8/OP-9 v5-Fragen oder die entschiedene 4.7-Runde?

**Heute:** gelesen `PLAN.md:282` („entschieden, nicht gebaut"), `:292-298` (Richtung A; Zielregel
und Statement-Form „im selben Satz"; `Neg`/`Rem`/Bit/`Contains` „eine Runde, nicht zwei");
`:186-189` (`p.x++` gehört in diese Runde). Die zweite Fassung führte alle vier als offene
v5-Fragen und empfahl in OP-7 das Gegenteil der Richtung, ohne sie zu nennen.

| Option | Preis |
|---|---|
| A — die vier gehören der 4.7-Runde; v5 übernimmt nur, was Bruch ist: OP-33 B (kein Ausdruckswert), OP-6 `Index<K,V>`, OP-35 als Zwilling | Das Dossier verliert vier „Fragen" und gewinnt Aktentreue |
| B — v5 verhandelt sie neu | Widerspricht einer gesetzten Entscheidung ohne neue Evidenz — die Befunde dieser Runde (`f++`, `p.x++`, `++x;`) sind alle unter A lösbar |

**Empfehlung: A.** Die Rule-2-Begründung des Maintainers (kein zweiter Mechanismus für „plus
eins") richtet sich gegen `Inc`/`Dec`; sie trägt, und nichts in diesem Dossier widerlegt sie.

**Bruch:** kein eigener. **Vertrauen: gelesen.** **Regelort:** `PLAN.md` (Zuständigkeit).
**Retiriert:** nichts. **Hängt an:** OP-6, OP-7, OP-8, OP-9, OP-33, OP-35.

### OP-44 — Wird ein sicher panikender konstanter Ausdruck beim Kompilieren gemeldet?

**Heute: nein.** `let z = 1 / 0;` kompiliert und panikt zur Laufzeit (`d09`, `LYR-VM0002`, Exit
101); `comptime (1 / 0)` ist `LYR-CT0002` (`d09b`). Der Faltungspfad, den `comptime` benutzt,
sieht den Panic also — der Nicht-`comptime`-Pfad schweigt. Gelesen `03-types.md:56`: „Division by
zero is a panic" — eine Laufzeitaussage.

| Option | Vorbild | Preis |
|---|---|---|
| A — Fehler: ein konstanter Ausdruck, dessen Faltung sicher panikt, ist `LYR-SEM`-neu | **Rust** (`unconditional_panic`, deny-by-default), **C#** (`CS0020` „Division by constant zero"), **Go** (Compilefehler für konstante Division durch null) | Minor-Bruch für Programme, die absichtlich `1 / 0` schreiben — keins sinnvoll; die Regel gilt nur für die Operandenmenge, die `comptime` ohnehin faltet (Literale, `MIN / -1` bleibt erlaubt, weil er wickelt, `d16`) |
| B — Warnung | — | Konsistent mit der Uhrenfamilie; aber kein Programm wird durch die Warnung besser |
| C — nichts (Laufzeit) | Java, Lyric heute | Der Compiler weiß es und sagt es nicht |

**Empfehlung: A — und damit die Antwort auf OP-34s erste Zeile: Faltung eingebauter Operatoren
ist garantiert UND diagnostizierend.** Der Panic-Satz von §3.2 bleibt für den Laufzeitfall.

**Bruch:** minor. **Vertrauen: gemessen + gelesen** (Lyric), behauptet (Rust/C#/Go). **Regelort:**
`06-operators.md` §6.1 (Faltungszusage) + `appendix-a-diagnostics.md`. **Retiriert:** nichts.
**Hängt an:** OP-34, OP-18.

### OP-45 — Wie behandelt die Regelort-Tabelle ein Feature, das bereits GEGEN die Spec released ist?

**Heute:** die Display-Interpolation ist in v4.6.0 (2026-09-23) mit Guide-Text ausgeliefert,
während `lyric-spec` (Pin 4.6, `README.md:45`) das Gegenteil normiert (`06-operators.md:114-115`);
kein Regel-PR, kein Konformanzfall (Belege §1.9). Der Compiler-Kommentar
(`TypeChecker.cs:2989-2991`) zitiert §6.6 als Quelle für das, was §6.6 verbietet. Das ist ein
Verstoß gegen die eigene `spec-first`-Regel („Regel-PR mit `since:`-Gates zuerst, dann Zwilling,
dann Release+Pin" — Arbeitsregel, behauptet).

| Option | Vorbild | Preis |
|---|---|---|
| A — rückwirkender Regel-PR: §6.6 korrigiert, Konformanzfall `interpolation_calls_display.lyr` mit **`//! since: 4.6.0`**, Änderung im Spec-CHANGELOG als „describes what 4.6.0 shipped" | die eigene `48326f1`-Praxis („the grammar describes what 4.5 ships", `lyric-spec` log) | Ehrlich; die Spec bleibt Beschreibung eines Releases, nicht Fiktion |
| B — Compiler zurücknehmen, Spec behalten | — | Major-Bruch eines released Features |
| C — beides stehen lassen | — | Die Spec lügt über 4.6, und jede zweite Implementierung (Lyricpp) baut das Verbot nach |

**Empfehlung: A, und zwar vor jeder anderen §6.6-Arbeit** (Formatsprache, `PLAN.md:370`). Wer es
trägt: der Regel-PR ist Spec-Repo-Arbeit des Maintainers; das Dossier kann ihn nur benennen. Die
allgemeine Regel für OP-23: **eine Zeile „widerspricht dem Compiler" muss sagen, ob der Compiler
released ist — dann ist der Regel-PR rückwirkend und die Spec ist die Seite, die sich bewegt.**

**Bruch:** nein (A). **Vertrauen: gelesen** (CHANGELOG, Guide, Commit, TypeChecker, Spec, Suite,
Spec-Log). **Regelort:** `06-operators.md` §6.6 + Suite. **Retiriert:** nichts. **Hängt an:** OP-25,
OP-23.

### OP-46 — Wie sieht Diagnose und Umbau für `x++` in AUSDRUCKSposition aus (OP-33 B)?

**Heute:** `y = x++` und `f(x++)` laufen (`d14`); gepinnt `increment_has_the_classic_values.lyr:5-6`.
OP-31 nannte die Ausdrucksform „nicht rein textuell" und ließ es dabei.

| Option | Vorbild | Preis |
|---|---|---|
| A — Diagnose + `lyrfix` nur für die Statement-Form; Ausdrucksform nur melden | — | Der Benutzer baut `f(i++)` von Hand um |
| B — `lyrfix` rewritet die Ausdrucksform, wenn im umgebenden Statement **vor** dem `++` in L2R-Ordnung (OP-21/OP-38) kein Aufruf und keine andere Zuweisung steht: `y = x++` → `y = x; x += 1;`, `y = ++x` → `x += 1; y = x;`, `f(x++)` → `let t = x; x += 1; f(t);`; sonst melden | Go `gofix`-Praxis (Rewrites mit Guard) | Braucht OP-21 A als Garantie, sonst ist der Guard nicht begründbar |
| C — nur Diagnosetext | C# Analyzer ohne Codefix | Verschiebt alles auf den Benutzer |

**Diagnosetext (Vorschlag):** `LYR-SEM`-neu, Warnung in 4.x, Fehler ab 5.0: *„`x++` has no value —
it is a statement since 5.0: put `x += 1` on its own line and read `x` before it (old value) or
after it (new value)"*, mit Note auf die Stelle, die den Wert liest.

**Empfehlung: B.** Die Guard-Regel ist genau die Reihenfolgezusage von OP-21/OP-38: ohne sie kann
`lyrfix` nicht wissen, ob `g(h(), i++)` nach `let t = i; i += 1; g(h(), t)` dasselbe tut (es tut
es nur, wenn `h` `i` nicht liest — deshalb Guard und Meldung statt Rewrite).

**Bruch:** siehe OP-33. **Vertrauen: gemessen** (`d14`) + gelesen (Pin). **Regelort:** keiner
(Werkzeug) + `appendix-a-diagnostics.md`. **Retiriert:** mit OP-33. **Hängt an:** OP-21, OP-31,
OP-33, OP-38.

---

## 4. Was wir übernehmen sollten

| Von | Was | Wohin |
|---|---|---|
| **Rust / Swift / Scala** | Zuweisung liefert `void`/`Unit`, damit `if (b = true)` von selbst ein Typfehler wird | OP-3 |
| **Rust / Swift / Haskell** | Vergleichsoperatoren nicht-assoziativ | OP-4 |
| **Go** | `++` ohne Wert: nur postfix, nur Statement — auf der gesetzten 4.7-Richtung A aufgesetzt | OP-33 |
| **Java / C#** | Zuweisung: Ziel (Objekt, Index) VOR der rechten Seite; Empfänger vor Argumenten — **nicht** Rust (rechts zuerst), **nicht** Go (unspezifiziert) | OP-21, OP-38 |
| **Rust (RFC 2451)** | Orphan-Regel lockern | OP-12 |
| **Rust** | `From` auf dem Ziel statt `Into` | OP-13 |
| **Swift** | Strukturelle Ableitung von `Equatable`/`Hashable` über die Konformanzliste; Enums ohne Payload automatisch; Arrays bedingt konform | OP-26, OP-36 |
| **Rust / C#** | `Display` als Interpolationsanker — der Compiler tut es, die Spec zieht rückwirkend nach | OP-25, OP-45 |
| **Rust / C# / Go** | Konstante Division durch null ist ein Compilefehler | OP-44 |
| **C / C# / Java / Rust** | `%` auf float ist `fmod`; Float-Division durch null folgt IEEE | OP-41 |
| **.NET 7 Generic Math** | Form `Add<T, R>` (Ergebnistyp als Parameter) | OP-5 |
| **Rust `dyn Trait`-Praxis** | Operator durch den Interface-Wert, wie `Display` heute | OP-42 |
| **Kotlin** | `if` mit Blockzweigen; dieselbe Adaptionsregel für den Tail | OP-14, OP-32 |
| **Kotlin / Python** | `in` über `Contains<T>` (4.7-Runde) | OP-6 |
| **Python / C#** | Index mit beliebigem Schlüsseltyp | OP-6 |
| **stables Rust / Zig** | `never` nur in Rückgabe-/`throws`-Position | OP-16 |
| **C# / Java / Kotlin** | `OnInterface` als Attributanker | OP-29 |
| **Go / Rust** | Migrationswerkzeug vor dem Bruch | OP-31, OP-46 |
| **Rust (Bibliothek)** | `addChecked`/`addSaturating` statt Blockkonstrukt — §3.2 verbietet den Profilschalter | OP-18 |
| **Java / Go** | `std.string.Builder` statt `AddAssign` | OP-28 |
| **Zig** | Ein Operator wirft nicht — Lyric hat es schon | OP-22 |
| **C#** | Negatives Randliteral als Sonderfall — Lyric ist großzügiger (durch Klammern), das aufschreiben | OP-40 |

**Was Lyric richtig hat und v5 nicht anfassen sollte:** Bitoperatoren über den Vergleichen (§1.1),
nominale Bindung (§2.3), feste Tabelle (OP-1), `<`-Regel (OP-24), `Add<T, R>` (OP-5), `throw` als
Präfixausdruck, Faltung == Laufzeit **inklusive `MIN / -1`** (OP-34), Wrapping ohne Schalter (§3.2),
die Entscheidung gegen den allgemeinen Block-Ausdruck (`PLAN.md:406`), und **die für 4.7 gesetzte
Inkrement-Richtung** (OP-7 A).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2:**

1. OP-6 Bitoperatoren: sechs Interfaces für einen Anwendungsfall.
2. OP-7 B (`Inc`/`Dec`) — vom Maintainer bereits verworfen (`PLAN.md:294`).
3. OP-10 C (`AddAssign`): zwei Interfaces für „plus"; für `string` ist die Antwort ein Builder.
4. OP-11 B (`??=` liefert `T`): eine zweite Ergebnistyp-Regel für einen Compound (gemessene
   Regel `d05`: Typ des Ziels).
5. OP-12 B (`__rmul__`).
6. OP-15 Ausdruckskörper für Funktionen.
7. OP-26 B (`@Derive`) — `PLAN.md:406`.
8. OP-30 C (Uhren in `lyrfix`).
9. OP-42 B (eine Interface-Ausnahme `Display`): pro Interface eine Antwort statt einer Regel.
10. OP-14/15/32: `if`, `match`, `try` brauchen **eine** Value-Block-Regel.

**Mit gesetzten Entscheidungen (neu):**

- **OP-7 C widerspräche `PLAN.md:282/292-295`** (Inkrement aus `Add`, 4.7). Zurückgezogen; die
  v5-Frage ist OP-33.
- **OP-18 D widerspräche `03-types.md:51-53, 62-64`** („not configurable"; „new construct, never a
  change to these operators"). Gestrichen. Ob OP-18 A (`checked`-Block) ein „change to these
  operators" ist, muss §3.2 klären.
- **OP-13 A und OP-27 A widersprechen sich** (Kontexttyp entscheidet / entscheidet nie) — Grund für
  OP-13 C.

**Mit anderen Gebieten:**

| Frage | Kollidiert mit |
|---|---|
| OP-5 / OP-42 | Generics/Interfaces: `Self` (`PLAN.md:260` P1-4); Interface-Werte allgemein |
| OP-13 | setzt P1-4 voraus |
| OP-26 / OP-36 | P1-1/2/3 (`PLAN.md:257-259`); Werte-Gebiet (Tupel-Extension) |
| OP-12 | Module: Orphan-Regel in `04-modules.md`; `STATUS.md:2249-2254` (Array-`extend`), `:2255-2259` (opaque-`extend`) |
| OP-6 `Index<K,V>` | Collections (`PLAN.md:283-284`) |
| OP-14/15/32 | `try`-Ausdruck (`PLAN.md:264`) |
| OP-18 / OP-34 / OP-44 | Arithmetik, comptime |
| OP-20a | Optionals; vierfache `null`-Frage `06-operators.md:70-74` |
| OP-22 | typed throws St. 3 (`PLAN.md:263`) |
| OP-25 / OP-45 | String-/Formatgebiet (`PLAN.md:370`); **Prozess** (spec-first) |
| OP-29/30/31/46 | Migrationsapparat |
| OP-43 | `PLAN.md` — Zuständigkeit 4.7 vs. 5.0 |

**Mit der heutigen Doku und Spec:**

1. `docs/guide/07-interfaces.md:350` und `06-operators.md:12`: „`%` numeric-only" — fällt mit OP-6.
2. `06-operators.md:101` vs. `:107` — Selbstwiderspruch (OP-10); gepinnt durch
   `compound_on_field_is_refused.lyr`.
3. `06-operators.md:114-115` widerspricht dem **released** Compiler (OP-25/45).
4. `06-operators.md:26` „integer variables" widerspricht dem Compiler (`f++`, OP-35).
5. `06-operators.md:4-6` („resolved exactly as the written call") widerspricht dem Compiler für
   jeden Interface-Wert außer `Display` (OP-42).
6. `stdlib/std/core.lyr:516` „synthesized `Hashable`" — gibt es nicht (OP-26).
7. `STATUS.md:2493` „usable as VALUES" — gilt für den Methodenaufruf (OP-42).
8. `03-types.md:36-38` „leading `-` folds" — der Compiler faltet auch durch Klammern (OP-40).
9. `03-types.md:56` „Division by zero is a panic" — für float gemessen falsch, wenn der Satz
   nicht auf Integer eingeschränkt wird (OP-41).

---

## 6. Reihenfolge, die sich aus den Abhängigkeiten ergibt

1. **Prozess, rückwirkend, sofort:** OP-45 (Display-Regel-PR mit `since: 4.6.0`).
2. **Vor allem anderen, weil sechs Fragen daran hängen:** OP-29 (`OnInterface`), OP-30
   (Warnklasse), OP-31 (`lyrfix`).
3. **Bugfix:** OP-16 (`never` stürzt den Compiler ab).
4. **Die 4.7-Runde, wie gesetzt (OP-43):** OP-7 A, OP-8 A, OP-9 A, OP-6 (`Neg`, `Rem`, `Contains`),
   OP-35 (float-Satz + Zwilling), OP-41 (Float-Sätze für `Rem`), OP-22 (Wurffreiheit als Satz).
5. **Spec-Korrekturen ohne Programmbruch:** OP-2, OP-10 (Pin retirieren), OP-21/38, OP-27,
   OP-32, OP-34, OP-37, OP-40, OP-42 (der Interface-Wert-Satz — und der Compilerfix dazu).
6. **Additiv:** OP-12, OP-14/15, OP-26/36 (nach P1-1/2/3), OP-18 C, OP-28, OP-44.
7. **Die Brüche, jeder mit Uhr, `lyrfix`-Pfad und Retirierungszeile (OP-39):** OP-3, OP-33 (+OP-46),
   OP-6 `Index<K,V>`, OP-13, OP-4.

---

## 7. Nach der Kritik geändert

**Nachgeprüft — die Kritik hatte recht (Zitate):**

- `STATUS.md`: „usable as VALUES" steht auf **`:2493`**, der Block ist `:2478-2496` (nicht `:2483`
  / `:2479-2486`). Vierter Fehlversuch in Folge an dieser Stelle; jetzt gegen `sed -n` geprüft.
- `STATUS.md`: die `extend`-Befunde stehen auf **`:2249-2254`** (Array) und **`:2255-2259`**
  (opaque), nicht `:2225-2236` (dort `lyrtest`/REPL).
- `PLAN.md`: Ausschlussliste **`:406`** und **`:414`** (nicht `:371` — dort `?Struct`);
  f-String-Formatsprache **`:370`**; typed throws St. 3 **`:263`**, `try`-Ausdruck `:264`.
- `PLAN.md:186-189` **gegen den Wortlaut zitiert** — der Maintainer sagt, `IR0001` sei KEINE falsch
  benannte Grenze. OP-8 B zurückgezogen; `IR0001` ist bis zur 4.7-Entscheidung der richtige Code.
- `PLAN.md:282, :292-298` **verschwiegen** — Richtung A für das Inkrement gesetzt, Operator-
  Interfaces in derselben Runde. OP-7 empfiehlt jetzt A, die v5-Frage wandert nach OP-33; OP-6
  verliert seine Staffelung; neue OP-43 klärt die Zuständigkeit.

**Nachgeprüft — die Kritik hatte recht (Messungen, alle selbst wiederholt in `operatoren-r4/`):**

- `f++` auf `float` kompiliert (`d01`, `2.5`; Kontrolle `d01b` `string` → `SEM0003`). Dritter
  Spec-Compiler-Widerspruch; neue OP-35.
- `r16` bewies die comptime-Hälfte von `-7 / 2` nicht (`0 - 7 / 2`). Neu `d02`: `-3 / -1` in beiden
  Welten; dazu `d16`/`d16b` `MIN / -1` = `MIN` in beiden.
- Kein zusammengesetzter Typ hat `==` (`d03a-c`: Array, Tupel, payload-loser Enum je `SEM0059`).
  Neue OP-36.
- Interface-Wert-Lücke gilt für `Equatable` (`d04`), `Ordered` (`d10`), `Into` (`d12`); `Display`
  dispatcht (`d11`). §6.1:4-6 verspricht den Operator durch den Wert — Konflikt, nicht Lücke. Neue
  OP-42; OP-5 verweist darauf.
- Wert/Typ jeder Zuweisungsform (`d05`): Typ des Ziels, auch `a += b += 1`. Neue OP-37; OP-11
  dreht: A statt B, C# ist Vorbild für B.
- Reihenfolge Ziel/Index vor rechter Seite, Empfänger vor Argumenten (`d06`). Neue OP-38; OP-21
  zählt die Fälle auf.
- `let z = 1 / 0;` kompiliert und panikt erst zur Laufzeit; `comptime (1/0)` ist `CT0002` (`d09`,
  `d09b`). Neue OP-44.
- `%` auf float ist `fmod`; `1.0/0.0` = `Infinity`, `0.0/0.0` = `NaN`, `1.0 % 0.0` = `NaN`, kein
  Panic (`d08`). Neue OP-41.
- `xs[c.next()] += 5` und `boxes[c.next()].v ??= 9` werten `next` einmal (`d17`) — Stütze für OP-10.
- `y = x++`, `f(x++)` laufen (`d14`) — Stütze für OP-46.

**Nachgeprüft — die Kritik hatte recht (Aktenlage):**

- OP-10: die Regel ist gepinnt (`compound_on_field_is_refused.lyr:19`) und in 4.6 bewusst
  dokumentiert (`:97-99`, `:104-108`). Umformuliert von „nicht nachgezogene Implementierung" zu „Pin
  retirieren, weil `:101` und `:107` sich widersprechen".
- OP-25: released in 4.6.0 (`CHANGELOG.md:13, :64-65`), Guide `:84-88`, Commit `219b3108`,
  `TypeChecker.cs:2989-2991` beruft sich auf §6.6 für das Gegenteil, kein Display-Fall in
  `06-operators/`, kein Regel-PR im Spec-Log. „Vermutlich" gestrichen; neue OP-45 (rückwirkendes
  `since: 4.6.0`).
- OP-18 D ist normativ ausgeschlossen (`03-types.md:51-53, 62-64`). Gestrichen; A muss gegen `:63`
  geprüft werden.
- OP-39 neu: Retirierungsspalte (`assignment_chains_right.lyr`, `compound_on_field_is_refused.lyr`,
  `increment_has_the_classic_values.lyr`).
- OP-46 neu: Diagnosetext und Guard-Regel für die Ausdrucksform.

**Vergleichssprachen korrigiert:**

- **C# `??=`**: für `T?` (Nullable-Werttyp) hat `a ??= b` den Typ `T` — C# ist Vorbild für OP-11 B,
  nicht A.
- **Rust Zuweisung**: rechte Seite VOR dem Ziel (Assignment expressions); Compound auf Primitiven
  ebenso. Kein Vorbild für OP-21/38 A.
- **Go Reihenfolge**: L2R nur für Aufrufe/Receive/logische Operatoren; Index vs. Aufruf „not
  specified".
- **Swift `Never`**: vollwertiger Typ (Enum ohne Fälle, jede Typposition, 5.9 `Equatable`/`Hashable`)
  — Vorbild für OP-16 **B**. **Stables Rust `!`**: nur Rückgabeposition, sonst `never_type`-Gate —
  Vorbild für OP-16 **A**. Beide Spalten getauscht.
- **Swift „expression too complex"**: aus der Auflösung überladener Operatoren mit Literalen im
  Constraint-Solver, nicht aus `precedencegroup`. OP-1 C verliert das Kostenargument; OP-27 B
  gewinnt es.
- **Rust `--keep-going`**: cargo-Flag für übrige Crates; Lints laufen nicht auf fehlerhaften
  Crates. OP-30 B steht nur auf C#.
- **C# `if (b = true)`**: `CS0665` vom Compiler selbst (konstanter Fall); Analyzer nur für
  `if (b = c)`.
- **Scala Zuweisung**: Ausdruck vom Typ `Unit` — OP-3 B, nicht A.
- **C# 9**: zielgetypte `new()`/`?:` als Argumente; Überladungsauflösung nutzt den Rückgabetyp nie.
  OP-27 B hat nur Swift.

**Wo die Kritik irrte, gemessen oder gelesen, und die Aussage steht bleibt:**

- **Negatives Randliteral:** die Kritik schrieb, die Regel stehe „weder in Grammar.md §6.1 noch in
  03-types.md". Gelesen `03-types.md:36-38`: „A leading `-` folds into the literal, so …
  `-9223372036854775808` are literals". Die Regel steht. Was die Kritik richtig sah und was jetzt
  OP-40 ist: `Grammar.md` hat keine Produktion dafür, und die Faltung wirkt gemessen auch durch
  Klammern (`d13c`), was „leading" nicht sagt.
- **OP-18 B als „new construct":** die Kritik verlangte die Prüfung, ob `+%`/`+|` unter `:63` fallen.
  Geprüft: neue Token, die `+` unverändert lassen, sind eindeutig „a new construct"; `+%` ist
  dabei überflüssig, weil `+` in Lyric schon wickelt. Fraglich ist nur A (`checked`-Block) — das
  steht jetzt so da.
- **`MIN / -1`** (Hypothese der Kritik in `EXPECT.md`: .NET-Exception): gemessen `MIN` bzw. `0`,
  genau `03-types.md:57-58`, zur Laufzeit und unter `comptime`. Kein Befund; als Bestätigung von
  §3.2 in OP-34 aufgenommen.

**Struktur:** zwölf Fragen ergänzt (OP-35 bis OP-46); jede Frage trägt jetzt **Retiriert**; OP-23
hat die Spalte; §6 sortiert die 46 Fragen neu, mit der 4.7-Runde als eigener Stufe. Proben dieser
Runde: `operatoren-r4/d01`–`d19`, Erwartungen in `EXPECT.md`, zwei nicht getroffen (`d13c`
Klammerfaltung, `d12` Diagnosetext für `Into` — der Fehlercode war erwartet, der absurde Rat nicht).
