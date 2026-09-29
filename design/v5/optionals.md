# Lyric 5 — Gebiet „Optionals und Abwesenheit"

**Messstand.** Repo `lyric` auf `6f6f029f402a2f6907f0e34497155ec3e59891d9` (HEAD, sauber;
Merge PR #173 „release/v4.6.0-cut"). Gemessen mit den vorgefundenen Debug-Binaries
`src/Lyrc/bin/Debug/net10.0/lyrc.dll` und `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`, nichts gebaut.
Spezifikation: eigener Clone `../lyric-spec` (Kapitel `spec/01`–`13`, Konformanzfälle unter
`conformance/cases/`).

Proben: `…/scratchpad/v5-design/probes/optionals/` (`p00`–`p52`, erste Runde),
`…/probes/optionals-rev2/` (`q01`–`q29`, zweite Runde), `…/probes/optionals-audit/`
(`n01`–`n37`, Proben des Kritikers, nachgeprüft) und `…/probes/optionals-rev3/` (`r01`–`r25`,
diese Runde; Erwartungen vor dem Lauf in `ERWARTUNG.txt`, jede überraschende Messung mit
Kontrolllauf). Alle Zeilenangaben dieser Fassung sind gegen `6f6f029f` neu gezogen — die zweite
Fassung hatte in `STATUS.md`, `PLAN.md` und `Bytecode.md` durchweg falsche Zeilen (§7).

Konventionen: **gemessen** = Programm kompiliert und gelaufen; **gelesen** = Pfad:Zeile in
Spec, Guide oder Quelltext; **behauptet** = weder noch, und so markiert.

---

## 1. Ist-Stand

### 1.1 Was die Sprache heute hat

| Form | Status | Beleg |
|---|---|---|
| `?T` als einzige eingebaute Abwesenheit | ja | `docs/guide/09-optionals.md:3`, Spec `03-types.md:68-69` |
| `??T` verboten | ja, aber **erst nach der Sema**, an zwei Stellen | `src/Lyric.Frontend/Ir/Lowering/TypeTable.cs:842-845` (`OptionalOf`), IR-Verifier; gemessen `p02c`, `q08` |
| `??T` **geschrieben** | Lexer-Kaskade: fünf Fehler ab `PAR0002 … got QuestionQuestion`, kein Wort von Nestung | gemessen `r15` |
| `x!` Force-Unwrap | ja, Panik `LYR-VM0007`, Exit 101, mit Rahmen und Zeilen | gemessen `q18` |
| `x!` als **Zuweisungsziel** | nein (`LYR-SEM0019`); `y!.v = 9` als Pfad **ja** | gemessen `r08`, `q06` |
| `a ?? b`, `a ??= b` | ja, rechte Seite faul; `a ?? panic(…)` geht (`NeverType`) | gemessen `p00`, `p04`, `r11`; `TypeChecker.cs:6026` |
| `a ?? return 0` | nein — `return` ist eine Anweisung (`PAR0002`) | gemessen `r20` |
| `x?.member`, `x?.method()` mit Rückgabewert | ja, Kette flacht ab | gemessen `p09` |
| `x?.method()` mit `void`-Rückgabe | **nein** (`LYR-IR0001`) | gemessen `q07` |
| Narrowing an `!= null` | ja — nur auf nackten Identifiern, nur `LocalSymbol`/`ParameterSymbol` | `TypeChecker.cs:1642` (`NullCompared`), `:2879` (`DeclaredType`); Spec `07-statements.md:90-95` sagt es genau so |
| Narrowing-Formen | Vergleich, `&&`/`||`, Früh-Exit (`return`/`throw`/`break`/`continue`/`panic`), if-Ausdruck, `while`; **nicht** `!(x == null)`, **nicht** nach einem Join | Spec `07-statements.md:97-108`; gemessen `n13a`, `n13c`, `n23`, `r21` (ja), `r07`, `r06` (nein) |
| Narrowing in Lambdas | wird **geerbt**, ein veraltetes Narrowing paniked zur Laufzeit — **so spezifiziert und gepinnt** | Spec `07-statements.md:121-125`; `conformance/cases/07-statements/narrowing_stale_in_a_lambda_panics.lyr`; gemessen `q13` |
| Genarrowte Lesung im Bytecode | ein `optget` **pro Lesung** | gemessen `r22` (`lyrc lower`: 3 Lesungen → 3 `optget`) |
| `if (let x = opt)`, `while (let …)`, `let … else` | ja, bindet ausgepackt | gemessen `p14`, `p15`; Grammatik `docs/Grammar.md:372-378`, Spec `02-grammar.md:403-409` |
| … im Guide | in **keinem** Kapitel (`grep -ln 'if (let' docs/guide/*.md` → leer) | gemessen |
| `match` über `?T` mit `null`-Arm, Erschöpfung | ja (`LYR-SEM0050`); Or-Pattern `5 \| null` matcht | gemessen `p16`, `p17`, `r24` |
| Muster matchen **durch** das Optional hindurch | ja | gemessen `q21`, `q22`, `q28` |
| implizite Default-Initialisierung auf `null` | nein (`SEM0018`, `SEM0106`) | gemessen `p06`, `q01` |
| Parameter `x: ?int = null` / `= 7` | ja; **ohne** Default ist `f()` `SEM0014` | gemessen `r10`, `n08b`, `r10d` |
| `fn f<T>(x: ?T = null)` | `f<int>()` ja; `f()` → `SEM0060` (T nicht ableitbar) | gemessen `r10b`, `r10c` |
| Überladung `f(int)` neben `f(?int)` | ja; `f(null)` → `?int`, `f(5)` → `int` | gemessen `r09`, `r09b` |
| `f(?int)` neben `f(?string)`, `f(null)` | `LYR-SEM0086` mit beiden Kandidaten | gemessen `r09c` |
| `T → ?T` Koerzierung: Zuweisung, Argument, Rückgabe, Struct-Init, Array-Literal, `??`-Operand | ja, alle | gemessen `r23`, `q20` |
| … an Tupel-Elementen | **nein** (`SEM0001`) | gemessen `q27`; `STATUS.md:2284` |
| … in Funktionstypen / Interface-Rückgaben / Array-Elementen | **nein** — keine Varianz | gemessen `r12` (`SEM0001`), `r13` (`SEM0042`), `r14` (`SEM0001`) |
| Nutzer-Enum `enum Opt<T> { Some(T), None }` mit `Opt<?int>` | **kompiliert und matcht** | gemessen `r04`; Grammatik `docs/Grammar.md:259-263` |
| `?T == ?T`, `?T == T` | nein (`SEM0059`, `SEM0003`) | gemessen `p03a`, `p03b` |
| `null == null` | `LYR-IR0001: 'null' in a position without an expected type` | gemessen `r03`; Kontrolle `r03b` (`a == null`) grün |
| Ordnung auf `?T` | nein (`SEM0003`) | gemessen `p03c` |
| `xs?[0]` | existiert nicht (Parserfehler) | gemessen `p18` |
| `o?.f = v` | nein (`SEM0019`) | gemessen `p19` |
| `if (optBool)` | nein (`SEM0004`) | gemessen `p20` |
| `x as ?T`, `?T as x` | beide refused (`SEM0006`) mit unerfüllbarem Vorschlag | gemessen `q16`, `q17` |
| `extend ?int { … }` | **wird still ignoriert** — kein `SEM0047`, der Aufruf scheitert mit `SEM0012` | gemessen `r19`; Kontrolle `r19b` (`extend int`) druckt `4`; `TypeChecker.cs:629-637` |
| `?T` über die Host-Grenze | hinein ausdrücklich nein, heraus ungeprüft | gelesen `src/Lyric.Embedding/Marshal.cs:36`, `:55-57`, `:91-105` |
| `?T` im Debugger | `null` oder der innere Wert unter dem Typnamen `?T` | gelesen `src/Lyric.Vm/Debugging/ValueRenderer.cs:43-46`, `:109-110` |
| `std.option` als freie Funktionen | `map`/`andThen`/`filter`/`zip`/`contains`/`toArray`/`iter`/`expect` — **kein** `isSome`/`isNone`, ausdrücklich | `stdlib/std/option.lyr:11-12`, `:20-21` |
| `assertEq(a, null)` mit `a: ?int` | `LYR-IR0001: call to 'equals' on '?int'` **in `stdlib/std/test.lyr:29`** | gemessen `r05`; Kontrolle `r05b` grün |

Das Narrowing ist die Ergonomie-Entscheidung von Lyric 4, und die Flussanalyse ist sorgfältig
und — anders als die zweite Fassung dieses Dossiers behauptete — **vollständig spezifiziert**:
Spec `07-statements.md:87-125` zählt auf, was beweist (genau eine Form), wo der Beweis gilt
(fünf Stellen), wie er komponiert, was ihn beendet, dass `match` nicht narrowt, und was in
Lambdas gilt. Die zweite Fassung hat §7.4 nicht gelesen und deshalb Befund P als unentdecktes
Loch geführt. Er ist eine dokumentierte, gepinnte Entscheidung — was ihn nicht richtig macht,
aber zu einer anderen Frage (OPT-24).

### 1.2 Wo Spezifikation, Guide und Implementierung auseinandergehen

**A. Die Nicht-Nestungsregel hat drei Dokumente und drei Antworten.**

| Dokument | Was es sagt |
|---|---|
| `docs/guide/09-optionals.md:19` | „Optionals do not nest: `??T` does not exist. **Wrapping an optional again leaves it at one level.**" — Idempotenz |
| Spec `03-types.md:68-69` | „the grammar refuses the nesting, and **no inference produces it** — `null` is the one empty value at every depth" — entsteht nie |
| Compiler | `fn wrap<T>(x: T): ?T` mit `?int` → `LYR-IR0001` in `TypeTable.OptionalOf` (`TypeTable.cs:842-845`), mit dem falschen Zusatz „cannot lower it yet" (`p02c`); `class Box<T> { v: ?T }` als `Box<?int>` → **`LYR-CLI0020`** im IR-Verifier (`q08`) |

Der Guide verspricht also etwas, das der Compiler nicht tut, und die Spec behauptet, der Fall
trete nicht ein. Der `q08`-Weg ist der ernstere: ein ICE-Code, und der IR-Verifier lief bis
2026-09-23 nur im Debug-Build (`PLAN.md`, Posten A). `docs/Bytecode.md:818` verlangt vom
*Leser*, einen inneren `0x42` abzulehnen — ohne Verifier hätte erst die VM beim Laden
gemeckert. Der falsche Notiztext ist derselbe, den 4.2.2 an sechs anderen Optional-Stellen
korrigiert hat (`STATUS.md:662`).

**B. `?T` erfüllt jede Constraint — still.** Gemessen mit Kontrolle (`p36`/`p36b`):
`show<D>(…)` ohne Konformanz → `LYR-SEM0028` (richtig); `show<?C>(…)` → Sema schweigt,
Lowering `LYR-IR0001`. Ursache `TypeChecker.cs:3702-3722`, `Satisfies` endet auf `_ => true`;
für `OpaqueRef` wurde der Default eine Zeile darüber (`:3720`) geschlossen. **Drei**
stdlib-Eintrittspunkte, alle gemessen: `println(a)` → `IR0001` in `stdlib/std/io/console.lyr:51`
(`p45`); `h<?string>` gegen `Hashable` (`p37`); **`assertEq(a, null)` → `IR0001` in
`stdlib/std/test.lyr:29`** (`r05`, neu — `assertEq<T :: [Equatable<T>, Display]>`). Der
dritte widerspricht `design/conditional-conformance.md:162-163`, das behauptet, `assertEq`
rendere Optionals selbst.

**C. Die Standard-Container können keine Optionals halten.** `List<?int>.empty()` →
`IR0001` in `stdlib/std/collections.lyr:54`; `Map<string, ?int>` → `:604`; `Map<?string, int>`
→ `:603` (`p12d`, `p13b`, `p32`; Kontrolle `p12e` grün). Backings sind `(?T)[]` bzw.
`(?K)[]`/`(?V)[]` (`collections.lyr:43`, `403`, `549-551`, `857`, `912`).

**Was die zweite Fassung dazu falsch hatte:** `rawArrayAlloc` ist **nicht** `pub`
(`stdlib/std/core.lyr:32-36`: „NOT `pub`: every slot holds whatever the runtime leaves
there"; `src/Lyric.Vm/NativeRegistry.cs:378-379`: „the declaration is private: the compiler
emits the fill, and nothing else may see it"). Gemessen `r01`: `import std.core
{ rawArrayAlloc }` → `LYR-RES0004`. `collections.lyr` kann das Native aus Lyric-Quelltext
**nicht** rufen; nur der Compiler emittiert es für `[first, ..rest]`. Siehe OPT-03, OPT-34.

**D. `?void` ist grammatikalisch erlaubt und tötet den Compiler.** `Grammar.md:306-308`,
Spec `03-types.md:68` schließt nichts aus. `fn f(): ?void { return null; }` baut (`p21`);
`let v = f(); if (v == null)` → `LYR-CLI0020 … composite type over void` (`p21b`);
`return g();` → `CLI0020 … produced no value` (`p21c`). Spec `06-operators.md:82` sagt für
`?.` „the result is optional" — bei einer `void`-Methode wäre das `?void`.

**E. Der Typdrucker verliert die Klammern, die die Grammatik verlangt.** `Grammar.md:338-339`:
`?T[]` optionales Array, `(?T)[]` Array von Optionals. Gemessen `p50`: „cannot assign
'?int[]' to '?int[]' — two different types share this name …" (frei erfundene Erklärung).
Ursache `src/Lyric.Frontend/Sema/TypeFacts.cs:133` + `:141`; der Fix hat seinen Präzedenzfall
in `:138-140` (`ArrayOf { Element: FnType }` wird geklammert). **Derselbe Defekt im Debugger**:
`ValueRenderer.cs:195-196` druckt `Optional => "?" + …`, `Array => … + "[]"` — ein `(?int)[]`
heißt im Variablenfenster `?int[]`. Gelesen, nicht gemessen (ein DAP-Lauf braucht einen Client).

**F. `o += 1` auf einem genarrowten `?int` erzeugt kaputtes IR.** `q03` → `LYR-CLI0020 …
operand types differ: t4 is ?i64, t5 is i64`; Kontrolle `q04` (`o = o + 1`) druckt `2`;
Ausweg `o = o! + 1` → `SEM0005` (`q05`). Keine Compound-Form mit Workaround. `PLAN.md:108`
führt es als Rest-ICE.

**G. `?Struct` teilt, außer die Quelle ist nicht optional.** `p44c` (Kontrolle, nicht
optional): Wert; `p44b` (Feld `?S`): geteilt; `p44` (`var y: ?S = x` mit nicht-optionalem
`x`): kopiert; `q06` (`?S` nach `?S`): geteilt. Regel: kopiert wird nur beim **Einpacken**
(`FunctionLowerer.cs:3038`). `Interpreter.CopyStruct` rekursiert nur bei `TypeTag.Struct`
(`src/Lyric.Vm/Interpreter.cs:1161-1163`); `LyrValue.Some` (`src/Lyric.Vm/LyrValue.cs:113-114`
— **`Some`, nicht `Wrap`**, wie die zweite Fassung schrieb) gibt einen Referenzwert
unverändert zurück, ein `?Struct` ist derselbe `LyrValue[]`.

**H. Vier Wege zu fragen „ist das null?" — zwei warten auf die Monomorphisierung, zwei
nicht.** In `fn f<T>(x: T)`: `x == null` und `x ?? fb` kompilieren und stimmen (`p52`); `x!`
→ `SEM0005` und `match … null` → `SEM0029` an der Deklaration (`p52b`, `p52c`).
`STATUS.md:2212-2219`, `:669`.

**I. `x!` auf einem genarrowten Wert ist ein Fehler** (`SEM0005`, `p31`, `q05`); trifft
`std.option.zip` (`p40`).

**J. Ein Modul-`let` wird nie genarrowt** (`p27b` → `SEM0003`); `DeclaredType` kennt nur
`LocalSymbol`/`ParameterSymbol` (`TypeChecker.cs:2879-2884`); ein Modul-`let` ist unveränderlich
(`PAR0027`, `p27`).

**K. `null` bekommt in einem Tupel-Literal keinen Kontext** (`q27` → `SEM0001 … '(null, int,
string)'`); Kontrollen `q20` (Array-Literal) und `p39` (if-Ausdruck) propagieren.
`WidenAgainstNull` (`TypeChecker.cs:4666-4670` — **so heißt die Funktion**, nicht `Unify`)
hebt `null`+`T` zu `?T`, das Tupel-Literal typt bottom-up. `STATUS.md:2284`, `:1108`.

**L. `??` bindet schwächer als `||`, `&&` und `==`** (`Grammar.md:441-459`: Stufen 13/14/15;
`p42`, `p41`).

**M. `null` als Iterator-Ende** — `for (x in (?int)[])` → `SEM0091` (`p11`),
`Coroutine<?int>.next()` → `SEM0080` (`p47`), `resume` erlaubt (`p46b`). `STATUS.md:2447-2451`,
`:2061`.

**N. Der Guide beschreibt die Sprache unvollständig und sagt eine Form zu, die es nicht
gibt.** `09-optionals.md:39` nennt `while` und `&&`, nicht die Beschränkung auf Namen, nicht
den Früh-Exit, nicht den if-Ausdruck — alles, was Spec `07-statements.md:97-108` hat. **Kein
Guide-Kapitel** zeigt `if (let …)`, `while (let …)` oder `let … else` (`grep -ln 'if (let'
docs/guide/*.md` → leer; ebenso `while (let`). Und `09-optionals.md:48` sagt
`x?.method()` als Anweisung zu (Befund Q).

**O. `design/optional-equality.md:49`** nennt `a == 5` auf `?int` „ebenfalls SEM0059";
gemessen ist es `SEM0003` (`p03b`).

**P. Ein veraltetes Narrowing in einer Closure paniked — spezifiziert und gepinnt.**
*(Korrigiert: die zweite Fassung nannte es „unsound" und „unentdeckt".)* Gemessen `q13`:

```lyr
var x: ?int = 1;
var f: fn() -> int = () => 0;
if (x != null) { f = () => x + 100; }
x = null;
println(f"{f()}");     // panic [LYR-VM0007] in main.main.<lambda1> — ohne ein `!` im Quelltext
```

Spec `07-statements.md:121-125`: „A lambda body is checked under the narrowing in force at its
creation. A read of a narrowed variable compiles to a **checked unwrap** … the read panics as
`LYR-VM0007`, exactly like `!` on an empty optional. The guarantee narrowing gives is memory
safety, not proof persistence." Konformanzfall
`narrowing_stale_in_a_lambda_panics.lyr` pinnt genau `q13`. Es ist also keine Lücke in der
Analyse, sondern eine Entscheidung: Speichersicherheit statt Beweiserhalt. Was bleibt, ist die
Frage, ob Lyric 5 bei ihr bleibt (OPT-24) — und der Preis ist gemessen: `r22` zeigt ein
`optget` pro Lesung, also läuft der Check auch dort, wo kein Lambda im Spiel ist (OPT-50).

**Q. Die Anweisungsform von `?.` existiert nicht.** `p?.act();` mit `act(): void` →
`LYR-IR0001: '?.' with a call that returns nothing` (`q07`), obwohl Guide `09-optionals.md:48`
und Spec `06-operators.md:82` sie zusagen.

**R. Ein `defer`, das die Variable schreibt, beendet das Narrowing zu früh** (`q24` →
`SEM0003`; Kontrolle `q29` grün, druckt `2`, `bye`, `1`).

**S. Die Host-Grenze ist asymmetrisch.** Hinein: `Marshal.cs:36`, `:55-57` (ehrlich). Heraus:
`FromLyric` hat vor der `switch` nur Guards für `Void` und `Host` (`:63-80`), kein
`TypeTag.Optional`; `_ => value.AsI64` (`:91-105`). **Behauptet** bleibt, ob die Embedding-API
eine `?T`-Rückgabe überhaupt bis dorthin lässt — nicht gemessen, ein Lauf bräuchte einen Build.

**T. `IR0001` auf gültigem Code — die Liste ist länger als die zweite Fassung zählte.**
Sie nannte drei (`??T`-Inferenz, Constraint-Loch, `?.` auf `void`). Gemessen sind es mindestens
fünf Eintrittsstellen: dazu **`null == null`** (`r03`: „'null' in a position without an
expected type", Sema lässt es durch — `TypeChecker.cs:4670`, Kommentar „the null/null case
too: there the `Equal` above already decided" —, das Lowering wirft) und **`assertEq(a, null)`**
in `test.lyr:29` (`r05`). Spec `12-diagnostics.md` §12.1 reserviert `IR0001` für gültiges Lyric.

**U. `extend ?int { … }` wird still verschluckt.** *(Neu.)* `r19`: das Extend-Block kompiliert
ohne Diagnose, der Aufruf `a.isPos()` scheitert mit `SEM0012: '?int' has no member 'isPos'`.
Kontrolle `r19b` (`extend int`) druckt `4`. Ursache `TypeChecker.cs:629-637`: ein Target, das
`Resolve` nicht liefert, wird ohne `SEM0047` übersprungen; der Kommentar zählt „generic
instance, array, tuple, alias" auf — ein Optional ist nicht dabei und fällt durch. Ein
Anwender, der Swifts `extension Optional` erwartet, bekommt keine Antwort, sondern einen
Fehler an der falschen Stelle.

**V. Diagnosen ohne Wegweiser.** `p.v` auf `?P` → `SEM0012: '?P' has no member 'v'` ohne Hinweis
auf `?.` oder Narrowing (`r16`); `xs[0]` auf `?int[]` → `SEM0007: '?int[]' is not indexable —
it must implement 'Indexable<T>'` — der Hinweis führt in die Irre (`r17`); `??int` geschrieben →
fünf Folgefehler ab `PAR0002` (`r15`). Und zwei Codes tragen je zwei Bedeutungen: `SEM0005`
ist „`??` auf Nicht-Optional" (`r18b`) **und** „cannot force-unwrap non-nullable" (`q05`);
`SEM0059` ist „`!= null` auf Nicht-Optional" (`r18a`) **und** „`==` is not defined for `?int`"
(`p03a`).

---
## 2. Sprachvergleich

### 2.1 Übersicht

| Sprache | Darstellung | nestet | Unwrap-**Bindung** | Flussnarrowing | Force | Coalesce | Chaining | `?.` auf `void` | Ordnung |
|---|---|---|---|---|---|---|---|---|---|
| **Swift** | `Optional<T>` = Enum in der stdlib, `T?` ist Zucker | **ja** (`Int??`) | `if let x = o`, `guard let`, Kurzform `if let x` (5.7) — neue, verschattende Bindung | nein (die Bindung ist der Beweis) | `o!` trappt | `??` | `?.`, flacht ab | `p?.act()` hat Typ **`Void?`** (`()?`); `if p?.act() != nil` ist ein Idiom | nur `==` (SE-0121 entfernte `<`) |
| **Rust** | `Option<T>`, gewöhnliches Enum | **ja** | `if let Some(x)`, `let … else`, `while let` | nein | `unwrap()`/`expect()` | `unwrap_or` | keins | n/a | `None < Some(_)` |
| **Kotlin** | `T?` = Flag auf dem Typ, JVM-`null` | **nein** (idempotent) | `o?.let { }` | **ja** (Smart Cast) auf lokalen `val`/`var`; **nicht** auf einer `var`, die ein Lambda **verändert** (eine nur lesende Capture ist erlaubt); nicht auf offenen/`var`-Properties | `!!` wirft NPE | `?:` (Elvis) — rechts darf `return`/`throw` stehen | `?.` | `p?.act()` hat Typ **`Unit?`** | keine |
| **C#** | zwei Mechanismen: `Nullable<T>` für Werte, NRT (Compile-Zeit) für Referenzen | nein (`int??` illegal) | `is { } y`, `is T y`, `is var y` | **ja**, auch auf Feldern/Properties, `[NotNullWhen]` | `!` nur Compile-Zeit | `??`, `??=` — rechts darf `throw` stehen | `?.`, `?[ ]`; `o?.f = v` erst seit C# 14 | `o?.M();` **nur als Anweisung**, kein Ausdruckstyp | „lifted" `<` → `false` |
| **Dart** | `T?` seit 2.12 | nein | `if (x != null)` (Promotion) | **ja**, lokal; seit 3.2 auch private **final** Felder | `!` wirft | `??`, `??=` | `?.`, `?..`, `?[ ]` | `void`-Rückgabe bleibt `void` — *behauptet* | keine |
| **TypeScript** | `T \| null \| undefined` (Union) | n/a | — | **ja**, auch Pfade; in Closures seit **5.4** erhalten, wenn nach der Closure-Erzeugung keine Zuweisung mehr folgt (davor: zurückgesetzt) | `!` nur Compile-Zeit | `??`, `??=` | `?.`, `?.[ ]`, `?.()` | `void \| undefined` | keine |
| **Zig** | `?T` eingebaut, Nullpointer als Niche | ja | Capture `if (o) \|x\|`, `orelse` | nein | `o.?` paniken | `orelse` — rechts darf `return` stehen | keins | n/a | keine |
| **Haskell** | `Maybe a` — ADT | **ja** | `case`, `do` | nein | `fromJust` | `fromMaybe` | keins | n/a | `Nothing < Just _` |

*Korrigiert gegenüber der zweiten Fassung:* die Spalte „`?.` auf `void`" ist neu, und sie
kippt OPT-23 (dort stand „die Antwort aller vier Sprachen" für die Anweisungsform — Swift und
Kotlin tun das Gegenteil). TypeScript-Closures: 5.4 („Preserved Narrowing in Closures Following
Last Assignments"), nicht „zurückgesetzt". Kotlin: nur eine **verändernde** Capture beendet
den Smart Cast.

### 2.2 Wer die entgegengesetzte Entscheidung getroffen hat

**Rust und Haskell** haben keine eingebaute Abwesenheit: `Option<T>`/`Maybe a` sind gewöhnliche
ADTs. Was sie gewinnen, ist genau die Liste der Befunde A, B, C, D, Q, T:

- Komposition ist gratis: `Vec<Option<T>>`, `HashMap<K, Option<V>>`, `Iterator<Item = Option<T>>`,
  `Option<Option<T>>` — kein Backing-Problem (C), kein `??T` (A), kein Constraint-Loch (B).
- Kein `?void`, kein `?.`-auf-`void`: `Option<()>` ist ein gewöhnlicher Typ.
- Die Bibliothek darf mitreden: `flatten`, `transpose`, `zip` sind Methoden. *(Korrigiert:
  die zweite Fassung begründete Lyrics freie Funktionen damit, generische Methoden auf
  generischen Typen fehlten. Sie fehlen nicht mehr — `PLAN.md:121` führt es als erledigt,
  PR #170; gemessen `r02`: `class Box<T> { fn map<U>(…) }` kompiliert und druckt `n=2`. Die
  Begründung ist heute eine andere: `?T` ist kein Nominaltyp, `extend ?T` wird still
  verschluckt (Befund U) — siehe OPT-49.)*
- `Iterator<Option<T>>` existiert, weil `Option<Option<T>>` die Ebenen auseinanderhält.

Was sie zahlen: jeder Zugriff ist syntaktisch teuer, es gibt kein Narrowing, die Darstellung
muss die Nische selbst finden (Rusts Niche-Optimierung). Lyric bekommt die Nische geschenkt
(OPT-25).

**Kotlin** trifft dieselbe Entscheidung wie Lyric (`T?` idempotent) und zahlt denselben Preis:
`Map<K, V?>.get(k)` unterscheidet „Schlüssel fehlt" nicht von „Wert ist null";
`containsKey` ist der Ausweg. **Swift, das nestet, kann es** (`[String: Int?]` → `Int??`).

*Korrigiert:* die zweite Fassung schrieb „Lyric 4 kann heute beides nicht". Das stimmt nur für
`?T`. Ein **Nutzer-Enum** kann es heute schon: `enum Opt<T> { Some(T), None }` mit `Opt<?int>`
kompiliert und matcht (`r04`, druckt `some -1`). Damit hat Lyric 4 den Rust-Weg *nebenher*
offen — `Map<K, Opt<V>>` würde die zwei Ebenen unterscheiden, sobald `Map` Optionals hält.
`PLAN.md:404` schließt `Option<T>` nur **als stdlib-Enum** aus, nicht als Sprachmöglichkeit.
Das ist eine Rule-2-Frage, die bisher niemand gestellt hat (OPT-38).

### 2.3 Was einzeln lohnt

| Von | Was | Warum es zu Lyric passt |
|---|---|---|
| **Zig** | `if (o) \|x\| { }` als Capture — Unwrap auf **jedem Ausdruck** | Lyric hat die Form (`if (let x = o)`) und dokumentiert sie in keinem Guide-Kapitel |
| **Dart 3.2** | Promotion nur auf **privaten finalen** Feldern | Zeigt, was OPT-07 B2 erst bauen müsste (`q09`, `q10`, `q25`) |
| **Kotlin** | Smart Cast endet nur an einer `var`, die ein Lambda **verändert** | Vorbild für OPT-24 **B**, nicht A |
| **TypeScript 5.4** | Narrowing bleibt in Closures erhalten, wenn danach keine Zuweisung folgt | Zweites Vorbild für OPT-24 B — dieselbe Regel, von der Zuweisungsseite formuliert |
| **Kotlin / C# / Zig** | `?: return`, `?? throw`, `orelse return` — Ausstieg auf der rechten Seite | Lyric hat `a ?? panic(…)` (`r11`), aber kein `a ?? return` (`r20`); OPT-43 |
| **Swift / Kotlin** | `Void?` / `Unit?` — `?.` auf eine `void`-Methode hat einen Typ | Zwei Vorbilder für OPT-23 B; C# ist das einzige für A |
| **C#** | `?[ ]`; `[NotNullWhen]` | Schließt `p18`; Narrowing über Aufrufgrenzen |
| **Swift** | `extension Optional` | Die Antwort auf OPT-49, wenn `?T` ein Extend-Ziel werden soll |
| **Swift** | `flatMap` vs. `map` als benannte Unterscheidung | `std.option.andThen` macht es so |
| **Rust** | `expect("…")` als Standardform | `std.option.expect` existiert |
| **Kotlin** | Keine Ordnung auf Nullables, `==` schon | Bestätigt `design/optional-equality.md` |

**Zwei Fallen, die Lyric bewusst nicht hat und nicht bekommen darf:** C#' „lifted `<`" und
TypeScripts zwei Abwesenheiten.

---
## 3. Designfragen

50 Fragen. OPT-01 bis OPT-35 aus der zweiten Fassung (korrigiert, wo die Kritik recht hatte;
mit Vermerk, wo sie es nicht hatte), OPT-36 bis OPT-50 nach der Kritik ergänzt.

### OPT-01 — Wo lebt die Nicht-Nestungsregel, und welche der drei Antworten gilt?

**Heute:** drei Dokumente, drei Antworten (Befund A). Guide `09-optionals.md:19` verspricht
**Idempotenz** („leaves it at one level"); Spec `03-types.md:68-69` sagt, es **entstehe nie**;
der Compiler wirft `IR0001` (`TypeTable.cs:842-845`, `p02c`) oder `CLI0020` (`q08`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Regel in die Sema, eigener `SEM`-Code, beide Würfe bleiben als Assertion | C#, Kotlin-Typfehler bei expliziter Nestung — **aber**: A nimmt die Guide-Zusage zurück | Sema an vier Stellen (Typauflösung, Substitution beim `q08`-Weg, Rückgabetyp-Inferenz, Feldtyp-Substitution). **Guide-Posten + Spec-Satz + Konformanz-Zwilling** (spec-first) |
| **B** Idempotenz: `?(?T)` **ist** `?T` | **Kotlin**, TypeScript — **und der eigene Guide** (`09-optionals.md:19`) | Keine Fehlermeldung; `Map<K,?V>` verliert „Schlüssel fehlt" ↔ „Wert null" still — aber das ist heute schon die Kotlin-Lage, und der Nutzer-Enum bleibt der Ausweg (OPT-38) |
| **C** Nestung erlauben | Swift, Rust, Zig, Haskell | Bruch mit `Grammar.md:341`, Spec `03-types.md:68`, `PLAN.md:404`; `Bytecode.md:604`/`:818` → Formatbump |

**Empfehlung: A — und ehrlich als Rücknahme einer Zusage geführt, nicht als Bugfix.**
*(Korrigiert.)* Die zweite Fassung nannte A einen reinen Bugfix. Er ist es nicht: der Guide
hat Idempotenz **zugesagt**, und A macht daraus einen Fehler. Das ist ein Guide-/Spec-Posten
mit Konformanz-Zwilling. Warum trotzdem A und nicht B: B löst `wrap<T>(x: T): ?T` mit `T = ?int`
durch Verschweigen — der Aufrufer bekommt ein `?int` zurück und weiß nicht, ob die innere
oder die äußere Ebene leer ist. In Kotlin ist das erträglich, weil die Sprache es nie anders
versprochen hat; in Lyric ist es an genau der Stelle unerträglich, an der `Map.get` es braucht
(OPT-04). **C** ist keine „einzige Option" mehr (die zweite Fassung nannte sie so, §5): der
Nutzer-Enum aus `r04` unterscheidet die Ebenen heute schon.

**Bricht:** minor **für die Guide-Zusage** (ein Programm, das sich auf „leaves it at one
level" verließ, kompiliert heute ohnehin nicht — gemessen `p02c` — die Zusage war also nie
einlösbar; der Bruch ist dokumentarisch, nicht in laufendem Code). Für den `q08`-Weg nicht
belegt (ICE → Sprachfehler).
**4.x-Warnstufe:** keine; der falsche Notiztext bleibt ein 4.6-Bugfix.
**Hängt ab von:** OPT-02, OPT-03, OPT-26, OPT-38.
**Confidence:** gemessen (`p02c`, `q08`), gelesen (Guide `:19`, Spec `:68-69`).

---

### OPT-02 — Was passiert, wenn ein Typparameter mit `?U` instanziiert wird?

**Heute:** je nach Position anders. `fn wrap<T>(x: T): ?T` mit `?int` → `T = ?int`, `??int`
→ `IR0001` (`p02c`); `fn take<T>(x: ?T)` mit `?int` → `T = int`, abgeschält (`p22`);
`take<?int>(a)` explizit → `IR0001` an der Deklaration (`p24`); `Box<?int>` über ein
`?T`-Feld → `CLI0020` (`q08`). **Neu gemessen:** `fn f<T>(x: ?T = null)` mit `f<int>()`
kompiliert (`r10b`), `f()` ohne Typargument → `SEM0060` „no argument determines it" (`r10c`) —
der Default trägt nicht zur Inferenz bei.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `?U` als Typargument **verboten**, wenn der Parameter irgendwo unter `?` steht — auch in Feldtypen | eigen | `List<?int>` bleibt unmöglich (OPT-03); fängt `q08` |
| **B** `?U` als Typargument generell verboten | — | Killt auch `id<?int>` und OPT-15 |
| **C** Idempotent substituieren | Kotlin | Semantik von `Map.get` fällt still |
| **D** Nesten (OPT-01 C) | Swift, Rust | siehe OPT-01 |

**Empfehlung: A, mit echtem Diagnosetext, ausdrücklich über Feldtypen.** Ein Zusatz zur
Inferenz: `f<T>(x: ?T = null)` ohne Argument darf `T` nicht raten (`SEM0060` ist richtig),
und dieser Satz gehört in die Spec neben `08-generics`.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-01, OPT-36. **Betrifft:** Generics-Gebiet.
**Confidence:** gemessen.

---

### OPT-03 — Wie hält ein Container Optionals?

**Heute:** gar nicht (Befund C). Backings `(?T)[]` (`collections.lyr:43`, `403`, `549-551`,
`857`, `912`). Und — *korrigiert* — das Native, das ein `T[]`-Backing bauen könnte, ist für
Lyric-Quelltext **unsichtbar**: `rawArrayAlloc` ist privat (`core.lyr:32-36`,
`NativeRegistry.cs:378-379`, gemessen `r01` → `RES0004`); nur der Compiler emittiert es für
`[first, ..rest]`. `PLAN.md:171` sagt „`List<?T>`/`Map<K,?V>` können das Native jetzt
benutzen" — aus Quelltext können sie es **nicht**.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Backing `T[]` plus Belegungsmarkierung (`Map` hat `states: int[]`, `collections.lyr:553`) | C# `Dictionary` (Freiliste über `entries`) — **nur für die Freiliste**, nicht fürs Slot-Leeren (siehe unten) | Braucht ein uninitialisiertes `T[]` (B) **und** eine Sichtbarkeitsregel dafür (OPT-34). GC-Retention gepoppter Elemente ist **Lyrics** Preis, nicht C#s |
| **B** `rawArrayAlloc<T>(n)` aus Quelltext nutzbar | Rusts `MaybeUninit`, Zigs `undefined` | Existiert als Native, ist aber privat; die Sichtbarkeitsregel ist der Preis (OPT-34) |
| **B′** Ein **container-internes** Native-Paar: Backing anlegen **und Slot leeren** (`default`-`LyrValue` schreiben) | .NET `Dictionary.Remove` (setzt `entry.value = default!`, wenn `IsReferenceOrContainsReferences<TValue>()`); Lyrics eigenes `rawArrayAlloc`, das genau solche Slots erzeugt | Zwei Natives statt eines; dafür **keine Retention** — das Leeren, das C# mit `default(T)` kann und Lyric-Quelltext nicht, kann die VM |
| **C** Nestung (OPT-01 C) | Swift, Rust | siehe OPT-01 |
| **D** `?T` als Elementtyp bleibt verboten, aber Fehler **an der Aufrufstelle** statt `IR0001` in der stdlib | — | Ehrlich, Fähigkeit fehlt weiter |
| **E** Nutzer-Enum als Elementtyp (`List<Opt<T>>`) dokumentieren | Rust | Funktioniert heute für Enums (`r04`), löst OPT-04, kostet Rule-2-Klärung (OPT-38) |

**Empfehlung: A über B′, plus D als Sofortmaßnahme in 4.6.** *(Korrigiert: „A über B, B
existiert bereits" war zirkulär — A braucht B, B ist unsichtbar, die Sichtbarkeit braucht
OPT-34 B, und OPT-34 empfahl D, also das Verschwinden von B.)* Die aufgelöste Form: die
Container brauchen nicht ein allgemeines uninitialisiertes `T[]`, sondern ein Backing, dessen
belegte Slots gültig sind und dessen freie Slots die VM leeren kann. Das ist B′, es braucht
keine Sichtbarkeitsregel (die Natives gehören dem Container-Modul, wie `Map.states` heute)
und es beseitigt die Retention, die A mit B allein hätte.

*Zur Vorbildspalte, korrigiert:* die zweite Fassung nannte C#' `Dictionary` als Präzedenzfall
für „Belegungsmarkierung über nicht-nullbarem Backing mit GC-Retention als Preis". Seit .NET
Core 3.0 leert `Remove` den Slot (`entry.value = default!`), sobald `TValue` Referenzen
enthält. C# **kann** das, weil jedes `T` ein `default(T)` hat; Lyric-Quelltext kann es nicht,
die VM aber schon (`rawArrayAlloc` schreibt genau diesen Wert). Das trägt B′.

**Bricht:** nein (additiv).
**4.x-Warnstufe:** keine; D gehört in 4.6 (Posten D des PLAN).
**Hängt ab von:** OPT-01/02, OPT-34, OPT-38. *(Gestrichen: „generische Methoden auf
generischen Typen" — erledigt, PR #170, `r02`.)*
**Confidence:** gemessen (`p12d`, `p13b`, `p32`, `r01`, `r02`), gelesen (`core.lyr:32-36`,
`NativeRegistry.cs:378-379`, `PLAN.md:165-171`).

---

### OPT-04 — Wie unterscheidet `Map<K,?V>.get` „Schlüssel fehlt" von „Wert ist null"?

**Heute:** stellt sich nicht, der Typ baut nicht (OPT-03). Sobald er baut, ist `get(key): ?V`
mit `V = ?int` mehrdeutig — es sei denn, Optionals nesten **oder der Wert ist ein Enum**
(`r04`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** `containsKey` ist die Antwort | Kotlin | Stille Falle; zwei Hashes pro Frage |
| **B** `getEntry(k): ?(K, V)` neben `get` | C# `TryGetValue`, Rust `Entry` | Zweite Frage, kein zweites `get` — Rule 2 trägt es, wenn es so benannt ist |
| **C** Nestung (OPT-01 C) | Swift | siehe OPT-01 |
| **D** `get` wirft | Python `d[k]` | Häufigster Lookup wird werfend |
| **E** `Map<K, Opt<V>>` mit Nutzer-Enum | Rust | Existiert heute schon für Enum-Werte (`r04`); die Frage ist Rule 2 (OPT-38) |

**Empfehlung: A plus B als `getEntry`, und E als dokumentierter Ausweg statt als Verbot.**
`getEntry` folgt der Tupel-Rückgabe von `Map.entries()` (`collections.lyr:713`).

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-01, OPT-03, OPT-38.
**Confidence:** gelesen (`collections.lyr:620`, `713`), gemessen (`r04`).

---

### OPT-05 — `?T == ?T` und `?T == T`

**Heute:** `?int == ?int` → `SEM0059` (`p03a`); `?int == int` → `SEM0003` (`p03b`;
`design/optional-equality.md:49` sagt SEM0059 — falsch); `null == null` → **`IR0001`**
(`r03`, Befund T). Der Vergleich gegen `null` ist die Präsenzfrage (`CheckEquatable`,
`TypeChecker.cs:2858-2860` → `CheckNullTest`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Beides erlauben, Kleene-frei (`null == null` → `true`) | Kotlin; `design/optional-equality.md` | ~30 Z. Sema + ~40 Z. Lowering; `?T == T` verschluckt eine Abwesenheit als `false` |
| **B** Nur `?T == ?T` | Rust | `a != null && a! == 5` bleibt — und `a!` ist nach dem Narrowing selbst ein Fehler |
| **C** Weiter beides refused | Status quo | `assertEq(parseInt("x"), null)` bleibt unbaubar (OPT-46) |

**Empfehlung: A**, mit dem Satz, dass `?T == T` **nicht** narrowt, und mit einer Regel für
das kontextfreie `null == null` (OPT-22): es ist der eine Fall, in dem beide Seiten `NullType`
sind, und A sagt dann `true` — die Sema muss ihn typen, nicht ans Lowering durchreichen.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-22, OPT-31, OPT-46. *(Korrigiert: `PLAN.md:257` führt `?T == ?T` als
4.7-Posten **#1**, die Konformanz-Synthese als **#2** mit „Hängt an: 1" — nicht umgekehrt.)*
**Confidence:** gemessen.

---

### OPT-06 — Ordnung auf Optionals

**Heute:** `a < b` für `?int` → `SEM0003` (`p03c`). `STATUS.md:592` hält fest, dass das die
Abwesenheit des SEM0059-Satzes an dieser Stelle ist, kein Loch.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Keine Ordnung, festschreiben | Kotlin, Dart, TS, Swift (SE-0121) | Kollidiert mit der Konformanz-Synthese (OPT-31) |
| **B** `null` ist das Minimum | Rust, Haskell | Stille Umsortierung |
| **C** „Lifted" | C# | `a < b` und `a >= b` gleichzeitig falsch |

**Empfehlung: A**, ausdrücklich, mit dem Satz für ein synthetisiertes `compare` (OPT-31).
`PLAN.md:405` schließt „Ordnung auf Optionals" bereits aus.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-05, OPT-31.
**Confidence:** gemessen.

---

### OPT-07 — Worauf wirkt Narrowing: Namen oder Pfade?

**Heute:** nur `LocalSymbol`/`ParameterSymbol` (`TypeChecker.cs:2879-2884`), nur nackte
Identifier (`:1642-1647`). Spec `07-statements.md:90-93` sagt es normativ: „Nothing else
narrows — not a field, not an index, not a function call." Gemessen: `b.v` nicht (`p01`),
`this.v` nicht (`p30`), Modul-`let` nicht (`p27b`). Und es gibt in Lyric 4 keine
unveränderlichen Pfade: `let p = P {…}; p.v = null;` kompiliert ohne Diagnose (`q09`); ein
Aufruf schreibt zwischen Test und Gebrauch (`q10`, `q25`: druckt `-1` mitten im „genarrowten"
Block); auf Structs nur `warning[LYR-SEM0109]` (`q11`); nicht-`mut`-Methode schreibt `this`
→ `SEM0108` (`q12`). `Grammar.md:237` gibt Feldern keinen Mutabilitätsmarker.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen, dokumentieren, Diagnose mit Wegweiser („binde es: `let v = b.v`") | Status quo, Spec §7.4 | Der häufigste Optional-Fall in echtem Code ist ein Feld |
| **B1** Modul-`let` narrowen | — | Trivial sicher (`PAR0027`); ein `switch`-Fall |
| **B2** Pfad-Narrowing auf Feldern | Dart 3.2 (private **final**), C#, TS | Braucht `final`-Äquivalent **und** Sichtbarkeit **und** Invalidierung (OPT-24); gemessen unsound ohne alle drei (`q25`) |
| **C** Kein Narrowing, nur Capture-Form | Zig | Bricht praktisch jedes Programm |
| **D** Volles Pfad-Narrowing | TypeScript | TS garantiert nichts; Lyric garantiert |

**Empfehlung: A jetzt, B1 als Bugfix, B2 nur mit dem Werte-/Mutabilitätsgebiet.** B1 ändert
den Spec-Satz „local or parameter identifier" (`07-statements.md:90-91`) — ein Spec-Posten
mit Zwilling, klein. B2 ist verfrüht, solange `SEM0108`/`SEM0109` selbst sagen „5.0 settles".

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** §3.4a (Struct-/Klassen-Mutabilität), OPT-24, OPT-08, OPT-42.
**Confidence:** gemessen.

---

### OPT-08 — Überlebt ein Narrowing eine Zuweisung mit einem nicht-null-Wert?

**Heute:** nein. `if (x != null) { x = 5; x + 1 }` → `SEM0003` (`p25`); `CheckAssign`
entfernt nur (`TypeChecker.cs:2860`: „a reassignment drops the narrowing"). Spec
`07-statements.md:114`: „An assignment to the variable ends its narrowing." Die Join-Frage
(Zuweisung **etabliert** ein Narrowing, `r06`) ist OPT-40.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | Status quo, Spec §7.4 | `x = 5` macht `x` „wieder vielleicht null" |
| **B** Typ des zugewiesenen Ausdrucks einsetzen: nicht-optional → Narrowing bleibt; `?T` → endet | TS, C#, Kotlin | Spec-Satz ändern; Schleifen-Soundness bleibt (`CheckWhile` prüft je Iteration) |
| **C** Zuweisung an genarrowte Variable verbieten | — | absurd |

**Empfehlung: B** — ohne die Behauptung, es heile Befund F mit (der Compound-ICE sitzt im
Lowering, `q03`/`q04`/`q05`). Zusatz: `defer { x = null; }` beendet zu früh (Befund R, OPT-24 D).

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-07, OPT-24, OPT-40.
**Confidence:** gemessen.

---

### OPT-09 — Wie viele Unwrap-Bindungsformen gibt es, und wo stehen sie?

**Heute vier:** (1) Narrowing; (2) `if (let x = e)` / `while (let …)` auf jedem Ausdruck
(`p14`); (3) `let x = e else { … }` (`p15`); (4) `match` mit `null`-Arm, Bindung am inneren
Typ (`q28`). Grammatik: `docs/Grammar.md:372` (`LetPatternStmt … [ 'else' Block ]`),
`:376-378` (`LetCondition`), Prosa `:415-417`; Spec `02-grammar.md:403-409`. **Im Guide: keine
der Formen 2 und 3 in irgendeinem Kapitel** (`grep -ln 'if (let' docs/guide/*.md` → leer;
*korrigiert: die zweite Fassung sagte „Kapitel 09 nennt nur Form 1", die Lücke ist der ganze
Guide*).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Alle vier behalten, als Familie benennen, **eigener Guide-Abschnitt** (Kapitel 09 oder das Pattern-Kapitel), Konformanzfall pro Form **vor** dem Guide | Swift (`if let` + `guard let` + `switch`), Rust | Vier Formen für ein Konzept; die Rule-2-Begründung (Zweig / Zwang zum Verlassen / Mustervergleich / kein neuer Name) muss hingeschrieben werden |
| **B** Narrowing streichen | Zig | Größter Bruch des Gebiets |
| **C** Form 2/3 streichen | — | Feld-/Aufrufergebnisse hätten keine Unwrap-Form |
| **D** Swifts `if (let x)`-Kurzform | Swift 5.7 | Fünfte Form mit Verschattung; `SEM0107` führt Zweitbindungen gerade als offen |

**Empfehlung: A, und zwar mit Konformanzpins zuerst.** *(Korrigiert: „ein Guide-Absatz" war
zu klein.)* Die Formen 2 und 3 haben Grammatik und Spec-Produktionen, aber kein
Guide-Kapitel; ob die Konformanzsuite sie pinnt, ist ungeprüft (`ls conformance/cases/07-*`
zeigt Narrowing-Fälle, keinen mit `let … else` im Namen — gelesen, nicht erschöpfend).
Reihenfolge: Pin, dann Guide.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-32; Pattern-Gebiet.
**Confidence:** gemessen (`p14`, `p15`, `q28`, grep), gelesen (Grammatik, Spec 02).

---

### OPT-10 — Was bedeutet `x!` auf etwas, das nicht `?T` ist?

**Heute:** `SEM0005` — auch auf genarrowten Variablen (`p31`, `q05`), im generischen Körper an
der Deklaration (`p52b`). Und `SEM0005` ist zugleich der Code für `??` auf Nicht-Optional
(`r18b`) — die Familie ist OPT-44.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Fehler bleiben | Swift | Defensives `!` bestraft; Befund-F-Stelle ohne Ausweg |
| **B** Auf genarrowtem Wert: Warnung | Kotlin (`!!` auf Non-Null warnt) | Ein wirkungsloses `!` im Code |
| **C** Im generischen Körper wie `??` warten | — | Fehler erst an der Instanziierung |
| **D** `??`/`null`-Pattern streng | — | Bricht jeden generischen Körper (`STATUS.md:2218`: „NOT additive either way") |

**Empfehlung: B für den Narrowing-Fall, C für den generischen Fall.**

**Bricht:** B/C nein; D major. **4.x-Warnstufe:** für D §12.5.
**Hängt ab von:** OPT-02, OPT-07, OPT-44, OPT-45.
**Confidence:** gemessen.

---

### OPT-11 — Ist ein fehlgeschlagener Force-Unwrap eine Panik oder eine Exception?

**Heute:** Panik `LYR-VM0007`, Exit 101, mit Rahmen und Zeilen (`q18`). Spec
`06-operators.md:80`: „a panic that names nothing".

| Option | Vorbild | Preis |
|---|---|---|
| **A** Panik bleibt | Swift, Rust, Zig | Host kann Gastfehler nicht in Fehlerwert wandeln |
| **B** Werfen | Kotlin, Dart | Zweiter Ausnahmebegriff neben `throws` (Rule 2) |
| **C** Panik nennt den Ausdruck | Rust `expect` | Ausdruckstext zur Laufzeit → Formatfrage (OPT-26) |
| **D** `x! "msg"` | — | Zweite Schreibweise neben `std.option.expect` |

**Empfehlung: A + C**, mit einem Satz für den **eingesetzten** Unwrap: bei `q13` paniked ein
`!`, das nirgends steht — und das ist seit Spec `07-statements.md:121-125` **so gewollt**. Dann
muss die Meldung sagen: „genarrowte Lesung von `x`, Beweis aus Zeile N veraltet".

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-24, OPT-26, OPT-50, Fehlerbehandlungs-Gebiet, Embedding (OPT-30).
**Confidence:** gemessen.

---

### OPT-12 — Wie weit reicht `?.`?

**Heute:** `x?.feld`, `x?.methode()` mit Rückgabewert (`p09`). Nicht: `p?.act()` mit `void`
(`q07`, `IR0001`), `xs?[0]` (`p18`), `o?.f = v` (`p19`, `SEM0019`), `h?.fnFeld()` (`p43`,
`SEM0062`).

| Option | Vorbild | Preis |
|---|---|---|
| **A0** `p?.act()` als Anweisung | alle sieben `?.`-Sprachen (mit verschiedenem Typ, OPT-23) | Ergebnisregel, siehe OPT-23 |
| **A** `?[ ]` | C#, Dart, TS | Lookahead nach `?`; ein Tabelleneintrag |
| **B** `o?.f = v` | Dart, Swift, C# 14 | Zuweisung, die still nichts tut |
| **C** `h?.fnFeld()` | TS `f?.()` | Member-Gebiet |
| **D** So lassen | — | Guide-Zusage bleibt gebrochen |

**Empfehlung: A0 zuerst, A ja, B nein, C ans Member-Gebiet.**

**Bricht:** additiv. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-23, OPT-13, OPT-14.
**Confidence:** gemessen.

---
### OPT-13 — Präzedenz von `??`

**Heute:** Stufe 15, unter `||` (14), `&&` (13), `==` (12) (`Grammar.md:441-459`). `x ?? true
|| true` = `x ?? (true || true)` (`p42`); `a ?? 0 == 5` ist ein Typfehler (`p41`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | C# (`??` unter `\|\|`) | `a ?? 0 == 5` bleibt ein Fehler — immerhin ein Fehler, keine stille Fehllesung |
| **B** `??` über die Vergleiche (zwischen `as`/`..` und `<`) | Swift (`NilCoalescingPrecedence` > `ComparisonPrecedence`), Kotlin (Elvis > Comparison) | **Grammatikänderung**: `Grammar.md:441-459` ist Sprachvertrag, Spec Kapitel 06 hat die Tabelle, `tests/Lyric.Tests.Parsing/golden/logical_comparison.ast` pinnt die Kette → Spec-PR mit Konformanz-Zwilling zuerst (Modus spec-first), dann Compiler |
| **C** Klammerpflicht neben `\|\|`/`&&`/`==` | — | Sonderregel ohne Vorbild |

**Empfehlung: B — als Grammatikposten, nicht als Diagnoseposten.** *(Korrigiert: die zweite
Fassung führte nur eine „gewöhnliche Warnung" als Bruchfolge.)* Die Messung, die die
Empfehlung trägt, ist neu gezogen (`grep -rn '??' | grep -v '??='`, ohne `bin/`/`obj/`):

| Korpus | `??`-Codezeilen | Kommentarzeilen mit `??` | Codezeilen mit `==`/`!=`/`\|\|`/`&&` daneben |
|---|---|---|---|
| `stdlib/` + `examples/` | 9 | — | **0** |
| `tests/` (Fixtures/Golden) | 5 | 1 (`Lyric.Tests.Ir/golden/lowering/optionals.lyr:3`) | **2**: `Parsing/golden/logical_comparison.lyr:1` (`a < b == c && d \|\| e ?? f`) und `Lexing/golden/operators.lyr:1` (die Operatorliste) |
| `lyric-spec/conformance/` | 4 | 2 (`literal_adaptation_contexts.lyr:4`, `optional_elements_do_not_iterate.lyr:5`) | **0** |

*(Korrigiert: die zweite Fassung zählte 6 Konformanz-Codezeilen und 1 Test-Treffer. Die
zwei Test-Treffer sind beide Golden-Dateien, deren Zweck die Kette bzw. die Tokenliste ist,
kein laufendes Programm.)* Grenze: Zeilen-Grep, kein Parse, nur Erstanbieter-Code.

**Bricht:** minor (Programme mit `?bool`-Coalesce neben `||`/`&&`/`==`); zwei Goldens ändern
sich.
**4.x-Warnstufe:** gewöhnliche Warnung ab 4.7 an jedem `??`, dessen Operand ein
`||`/`&&`/`==` ist (die Antwort steht fest, also keine §12.5-Warnung, `12-diagnostics.md:93-96`).
Der Umschreiber ist `lyrfix` aus `PLAN.md:343` (OPT-28).
**Hängt ab von:** Spec-Kapitel 06 (Präzedenztabelle), OPT-28.
**Confidence:** gemessen.

---

### OPT-14 — `?void` (und welche Typen `?` tragen dürfen)

**Heute:** grammatikalisch erlaubt (`Grammar.md:306-308`), Spec schweigt, Sema akzeptiert,
Compiler stirbt zweimal verschieden mit `CLI0020` (`p21b`, `p21c`). `Bytecode.md:614`:
„`void` is valid only as a return type, never as a slot, field or value type."

| Option | Vorbild | Preis |
|---|---|---|
| **A** `?void` in der Sema refused, eigener Code | C# (`void?` gibt es nicht; `void` ist kein Wert) | Kollidiert mit Spec `06-operators.md:82` für `p?.act()` — außer OPT-23 A |
| **B** `?void` als Einheitstyp `?()`: ein Wert, der da ist oder nicht | **Swift** (`Void?` = `()?`), **Kotlin** (`Unit?`) — *korrigiert: die zweite Fassung nannte hier „kein Vorbild"* | Setzt einen **Einheitswert** voraus. Lyric hat keinen: `void` ist kein Wert (`Bytecode.md:614`), es gibt kein `()`-Literal. B führt also einen neuen Werttyp ein (Rule 2) **und** braucht eine Slot-Darstellung → Formatfrage |
| **C** `?void` lowert zu `bool` | Lyrics `Coroutine<void>.next()` (`STATUS.md:2061`) | Ein Typ, der etwas anderes ist, als er heißt |
| **D** So lassen | — | ICE aus gültigem Code (`PLAN.md`, Posten B) |

**Empfehlung: A — mit der ehrlichen Begründung.** Nicht „B hat kein Vorbild" (es hat zwei),
sondern: B's Vorbilder haben einen Einheitswert (`()` in Swift, das `Unit`-Singleton in
Kotlin), und `?void` ist dort nur die Nullbarkeit dieses Werts. Lyric hat den Wert nicht;
ihn einzuführen, damit `?void` einen Sinn bekommt, ist ein neuer Mechanismus für einen
Grenzfall. C# ist die Sprache ohne Einheitswert, und sie sagt A.

**Bricht:** nein (heute ICE). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-23 (Reihenfolge OPT-23 → OPT-14 bleibt), OPT-02, OPT-26.
**Confidence:** gemessen.

---

### OPT-15 — Erfüllt `?T` eine Constraint?

**Heute:** ja, still (`Satisfies`, `TypeChecker.cs:3702-3722`, `_ => true`); drei
stdlib-Eintrittspunkte gemessen (`p45`, `p37`, `r05`), Kontrolle `p36b` zeigt `SEM0028` für
Klassen.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `Optional => false` in `Satisfies` | derselbe Fix wie `OpaqueRef` eine Zeile darüber (`:3720`) | `?T :: [Display]` bleibt unmöglich; der `as`-Vorschlag wird unerfüllbar (OPT-27); `assertEq` auf `?T` bleibt unmöglich bis OPT-46 |
| **B** `?T` erfüllt, wenn `T` erfüllt, geliftet | Rust (nur mit explizitem `impl`) | Bedingte Konformanz; `Display` dort mit **nein** beantwortet (`conditional-conformance.md:160`) |
| **C** Gezielte Ausnahme `Equatable` über OPT-05 | — | schleust `?T` in Constraint-Positionen |

**Empfehlung: A.** Bugfix mit Präzedenzfall; behebt drei Symptome. Was A **nicht** löst, ist
der dritte Eintrittspunkt: `assertEq(a, null)` wird dann `SEM0028` statt `IR0001` — sauberer,
aber weiter unbenutzbar (OPT-46).

**Bricht:** nein. **4.x-Warnstufe:** keine; 4.6 Posten C.
**Hängt ab von:** bedingte Konformanz, OPT-16, OPT-27, OPT-31, OPT-46.
**Confidence:** gemessen.

---

### OPT-16 — Wie zeigt man ein `?T` an?

**Heute:** `f"{opt}"` → `SEM0006` (`p02`); `println(opt)` → `IR0001` in `console.lyr:51`
(`p45`). `conditional-conformance.md:160-163`: „`?T :: [Display]` … ENTSCHIEDEN: nein",
mit dem Ausweg `std.option.showOptional(o, ifNone)` „für 4.5" — den es in `option.lyr` nicht
gibt (gelesen: die acht Funktionen aus §1.1).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Beide refused, ein Code und Text (folgt aus OPT-15 A) | Rust (`Option` ist `Debug`, nicht `Display`) | Debug-Wunsch braucht `?? default` |
| **B** `?T` rendert, wenn `T` rendert | Kotlin, Dart, TS | Abwesenheit verschwindet im Text; Designnotiz sagt nein |
| **C** Getrennte `Debug`-Konformanz | Rust | Formatsprache + Konformanz-Synthese |

**Empfehlung: A jetzt, C verbindlich vor der Konformanz-Synthese (OPT-31), und
`showOptional` nachliefern, weil die Designnotiz es zusagt.**

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-15, OPT-31, OPT-46, Formatierungs-Gebiet.
**Confidence:** gemessen, gelesen.

---

### OPT-17 — Implizite Default-Initialisierung und „später initialisiert"

**Heute:** keine. `var x: ?int;` lesen → `SEM0018` (`p06`); Feld weggelassen → `SEM0106`
(`q01`); `v: ?int = null` explizit geht (`p48`). Spec `03-types.md:88-95`.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | Rust, Zig | `= null` an jedem optionalen Feld |
| **B** `?T`-Felder ohne Initializer sind `null` | Swift, C# | Weggelassen ≠ vergessen — die Klasse Fehler, die `SEM0106` fängt |
| **C** `late` | Dart | Zweiter Mechanismus neben `?T` + `!` |

**Empfehlung: A, festgeschrieben.** Dieselbe Frage für **Parameter** ist OPT-36.

**Bricht:** nein. **4.x-Warnstufe:** keine. **Hängt ab von:** OPT-36.
**Confidence:** gemessen.

---

### OPT-18 — Ist ein `?Struct` ein Wert?

**Heute:** teilt, außer beim Einpacken (Befund G; `LyrValue.Some`, `LyrValue.cs:113-114`;
`Interpreter.cs:1161-1163`; `FunctionLowerer.cs:3038`). `PLAN.md:402`: „An `?Struct` hängt
außerdem, ob `struct Node { next: ?Node }` weiter läuft."

| Option | Vorbild | Preis |
|---|---|---|
| **A** `?T` ist ein Wert, wenn `T` einer ist | Swift | `struct Node { next: ?Node }` wird unendlich → `SEM0056`; **bricht laufenden Code**; eine Kopie pro Zuweisung |
| **B** `?Struct` ist eine Referenz; `y!.v = 9` verboten | — | `y!.v = 9` wird Fehler; rekursive Structs bleiben |
| **C** So lassen | — | Stille Aliasing-Falle ohne Regel |

**Empfehlung: B, entschieden auf der §3.4a-Uhr** (OPT-35), nicht hier.

**Bricht:** B minor, A major. **4.x-Warnstufe:** keine eigene Uhr (OPT-35).
**Hängt ab von:** §3.4a, OPT-25, OPT-35, OPT-45, OPT-47.
**Confidence:** gemessen.

---

### OPT-19 — `null` als Iterator-Ende

**Heute:** `next(): ?T`; `Iterator<?T>` gibt es nicht (`SEM0091`, `p11`; `SEM0080`, `p47`).
`STATUS.md:2447-2451`, `:2061`; `Bytecode.md:953`.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | — | `(?T)[]` nicht iterierbar |
| **B** `for (x in array)` als Indexschleife lowern | `STATUS.md` schlägt es selbst vor | Zweiter Lowering-Pfad; eine Allokation weniger |
| **C** Protokoll ändern (`hasNext`/Paar) | Java, C#, Python | Formatänderung (OPT-26) |
| **D** Nestung | Rust | siehe OPT-01 |
| **E** `Iterator<Opt<T>>` mit Nutzer-Enum | Rust | Geht heute für Enum-Elemente (`r04`); OPT-38 |

**Empfehlung: B jetzt, A für den Rest, E als dokumentierten Ausweg.** *(Korrigiert: „Nestung
ist die einzige Option, die OPT-03/04/19 zusammen löst" — E löst sie ebenfalls, ohne Format.)*

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-01, OPT-26, OPT-38; Koroutinen-Gebiet.
**Confidence:** gemessen.

---

### OPT-20 — Gibt es einen Propagations-Operator für Optionals?

**Heute nicht.** `PLAN.md:404-405` schließt „`?`-Operator auf `Result`" aus. `let … else
{ return null; }` existiert (`p15`). Und — neu — `a ?? panic(…)` geht (`r11`), `a ?? return 0`
nicht (`r20`, `PAR0002`); die Zwischenform ist OPT-43.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nicht einführen | — | `andThen` + Lambdas |
| **B** Postfix `?` mit Früh-Return `null` | Rust | Vierte Bedeutung von `?`; Rule 2 gegen `throws` |
| **C** `let … else { return null; }` dokumentieren | — | existiert |

**Empfehlung: C, A festschreiben — aber erst nach OPT-43**, das die billigere Zwischenform
(`?? return`) prüft.

**Bricht:** nein. **Hängt ab von:** OPT-43, Fehlerbehandlungs-Gebiet.
**Confidence:** gelesen (`PLAN.md:404-405`), gemessen (`p15`, `r11`, `r20`).

---

### OPT-21 — Wie druckt der Compiler `(?T)[]`?

**Heute:** wie `?T[]` (Befund E, `p50`, `p49`; `TypeFacts.cs:133`, `:141`; Präzedenzfall
`:138-140`). **Und der Debugger ebenso** (`ValueRenderer.cs:195-196`, gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| **A** `TypeFacts.Display` und `ValueRenderer.TypeName` klammern, wo die Grammatik klammert | eigene Codebasis | Ein `case` je Stelle; ein Golden |
| **B** „share this name"-Erklärung nur, wenn belegt | — | eine Bedingung |

**Empfehlung: A an beiden Stellen + B.**

**Bricht:** nein. **4.x-Warnstufe:** keine; 4.6 Posten D.
**Hängt ab von:** OPT-29, OPT-47.
**Confidence:** gemessen (Compiler), gelesen (Debugger).

---

### OPT-22 — Welchen Typ hat `null` allein, und darf er in Meldungen auftauchen?

**Heute:** `NullType` (`LyrType.cs:121`), gedruckt als `"null"` (`TypeFacts.cs:148`).
`let x = null` → `SEM0010` (`q15`); `[null, null]` → `SEM0010` (`q19`); Tupel → Leck in der
Meldung (`q27`); **`null == null` → `IR0001`** (`r03`, Kontrolle `r03b` grün) — die Sema
entscheidet bei `WidenAgainstNull` (`TypeChecker.cs:4666-4670`) bewusst nicht („the null/null
case too: there the `Equal` above already decided") und gibt den Vergleich ohne Typ ans
Lowering. Lyric hat außerdem einen Bottom-Typ `NeverType` (`LyrType.cs:122`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** `null` behält einen nicht nennbaren Typ, der nie gedruckt wird; **plus die Regel: ein kontextfreies `null` in einem Vergleich hat den Typ `?never`-artig „leer" und `null == null` ist `true`** | Swift (Meldung nennt `Optional<…>`) | Jede `Display`-Stelle mit `NullType` darin; die Vergleichsregel gehört zu OPT-05 |
| **B** `null` ist der Bottom-Typ | Kotlin `Nothing?`, TS | `[null, null]` kompiliert als Wert, mit dem man nichts tun kann; zweiter Bottom-Typ |
| **C** So lassen | — | Leck in Meldungen, `IR0001` auf `null == null` |
| **D** `NullType` nur drucken, wo er der ganze Typ ist | — | Halbe Maßnahme |

**Empfehlung: A, mit Befund K zuerst — und mit der Vergleichsregel, die die zweite Fassung
nicht hatte.** *(Korrigiert: „der einzige gemessene Leckfall verschwindet mit Befund K" —
`r03` ist ein zweiter Fall, und er verschwindet nicht mit K.)* Die Regel: zwei `NullType`
in `==`/`!=` sind ein konstanter Vergleich (`true`/`false`), die Sema typt ihn `bool` und
warnt „always true"; kein Lowering-Fall.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** Befund K, OPT-05, OPT-33.
**Confidence:** gemessen, gelesen.

---

### OPT-23 — Was ergibt `x?.m()`, wenn `m` `void` liefert?

**Heute:** `IR0001` (`q07`); Guide `:48` und Spec `06-operators.md:82` sagen die Form zu.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Anweisungsform als Sonderfall ohne Ergebnistyp: `p?.act();` erlaubt, `let x = p?.act();` Fehler | **C#** (`o?.M();` nur als Statement) — *korrigiert: **nur** C#; Dart behauptet* | `?.` hat zwei Ergebnisregeln je Position; Spec-Satz nötig; kein `?void` |
| **B** Ergebnis `?void` als Einheitstyp | **Swift** (`Void?`, `if p?.act() != nil`), **Kotlin** (`Unit?`) — *korrigiert: zwei Vorbilder, nicht „keines"* | Braucht einen Einheitswert und eine Slot-Darstellung (`Bytecode.md:614`) → OPT-14 B, Formatfrage |
| **C** Ergebnis `bool` | Lyrics `Coroutine<void>.next()` | Ausdruck liefert etwas anderes, als er heißt |
| **D** So lassen | — | Zusage gebrochen |

**Empfehlung: A — auf der korrigierten Grundlage.** Nicht „die Antwort aller vier Sprachen"
(das war erfunden), sondern: Swift und Kotlin bekommen `Void?`/`Unit?` geschenkt, weil sie
einen Einheitswert haben; Lyric müsste ihn erst bauen, und `if (p?.act() != null)` ist der
einzige Gewinn — ein Idiom, das die Frage „war `p` da?" mit einem Aufruf vermischt. A ist
die C#-Antwort, sie kostet einen Spec-Satz (`06-operators.md:82`: „in statement position the
form has no result") und keinen Formatbump. Die Reihenfolge OPT-23 → OPT-14 bleibt.

**Bricht:** nein. **4.x-Warnstufe:** keine; 4.6/4.7-Bugfix.
**Hängt ab von:** OPT-14, OPT-12.
**Confidence:** gemessen (`q07`); Vergleichsangaben gelesen aus den Sprachreferenzen
(Swift/Kotlin/C#/TS), Dart **behauptet**.

---

### OPT-24 — Was beendet ein Narrowing außer einer Zuweisung — und bleibt die Staleness-Regel?

**Heute** (*korrigiert, grundlegend*): Spec `07-statements.md:114-115` (Zuweisung, Blockende
für Früh-Exit) und **`:121-125` („Lambdas and staleness")** sind normativ, und
`narrowing_stale_in_a_lambda_panics.lyr` **pinnt** den Fall `q13`. Die zweite Fassung führte
(a) als unentdecktes Soundness-Loch; es ist eine dokumentierte Entscheidung: der genarrowte
Lesezugriff ist ein **checked unwrap** (`r22`: ein `optget` pro Lesung), und ein veralteter
Beweis paniked „exactly like `!` on an empty optional". Die vier Kandidaten:

| Fall | Heute | Bewertung |
|---|---|---|
| **(a)** Closure fängt `var`, Zuweisung danach (`q13`) | kompiliert, paniked `VM0007`; Kontrolle `q14` druckt `101` | **spezifiziert und gepinnt** |
| **(b)** Aufruf schreibt über Alias (`q10`, `q25`) | für Locals irrelevant, tödlich für OPT-07 B2 | — |
| **(c)** `yield` zwischen Test und Gebrauch (`q23`) | Narrowing bleibt | sound |
| **(d)** `defer`-Rumpf schreibt (`q24`, Kontrolle `q29`) | endet an der `defer`-Zeile | zu früh |

| Option | Vorbild | Preis |
|---|---|---|
| **A** Jede Capture einer genarrowten `var` beendet das Narrowing | — (*korrigiert: Kotlin ist **kein** Vorbild für A; Kotlin lässt lesende Captures zu*) | Restriktivste Form; `q14` (lesende Capture, keine spätere Zuweisung) würde refused |
| **B** Nur eine `var`, die **nach** der Capture noch zugewiesen wird, verliert das Narrowing in der Closure | **Kotlin** (Smart Cast fällt nur bei verändernder Capture), **TypeScript 5.4** (erhalten, wenn keine Zuweisung folgt) | Vorabsammlung der Zuweisungsstellen jeder `var` vor `CheckFunction` — ein Vorwärtslauf über den Funktionskörper, kein zweiter Sema-Pass; Parameter sind unveränderlich (`n14b`: `SEM0019`), `let`-Captures sicher (`n14a`) — betroffen sind nur `var`-Locals |
| **C** Closure erbt den genarrowten **Wert** | — | zwei Capture-Regeln (Spec `07-statements.md:82-84`: `var` als Zelle) |
| **D** `defer`-Rümpfe ans Blockende ziehen | — | eigener Posten, behebt (d) |
| **E** Staleness-Regel behalten (Status quo, Spec §7.4) | **Lyrics eigene Spec** | Ein Programm ohne `!` kann panikeln; jeder genarrowte Lesezugriff bleibt ein `optget` (OPT-50) |

**Empfehlung: B + D — als Spec-Änderung mit Rücknahme eines Konformanzfalls.** Warum nicht E:
die Regel ist ehrlich formuliert („memory safety, not proof persistence"), aber ein Programm,
das an einem Unwrap stirbt, den niemand geschrieben hat, ist genau das, was `?T` verhindern
soll — und B kostet nur `var`-Locals, die nach einer Capture zugewiesen werden. Warum nicht A:
es verwirft lesende Captures ohne Not, und kein Vorbild tut das. Der Bruch von B ist genau der
Konformanzfall: er pinnt heute die Panik, morgen pinnt er die Ablehnung.

**Bricht:** minor (B): `q13`-artige Programme werden refused; `q14` bleibt grün. Ein
Konformanzfall wird ersetzt → **spec-first**.
**4.x-Warnstufe:** gewöhnliche Warnung ab 4.7 („diese `var` wird nach der Capture zugewiesen —
das Narrowing gilt in der Closure nicht"); keine §12.5-Warnung (Antwort steht fest).
**Hängt ab von:** OPT-07, OPT-08, OPT-11, OPT-40, OPT-50.
**Confidence:** gemessen (`q13`, `q14`, `q23`, `q24`, `q29`, `r22`, `n14a`, `n14b`), gelesen
(Spec `07-statements.md:114-125`, Konformanzfall).

---
### OPT-25 — Wie ist ein `?T` zur Laufzeit dargestellt, und was kostet es?

**Heute** (gelesen): `LyrValue` = `ulong Bits` + `object? Ref` (`LyrValue.cs:18-21`), kein
Typ-Tag. `None => default` (`:109`); `Some` (`:113-114`): Referenz trägt sich selbst, Skalar
bekommt `SomeMarker`; `IsSome => Ref is not null` (`:116`); `Unwrap()` (`:120`). `?int` kostet
kein Wort, keine Allokation; `?Struct` boxt nicht (derselbe `LyrValue[]`, Befund G);
`Bytecode.md:820-824` schreibt es vor („must not reserve a bit pattern as null"). Gemessen
(`r22`): der Lesezugriff auf einen genarrowten Wert kostet ein `optget` pro Lesung.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Darstellung festschreiben | `Bytecode.md:820-824` | Eine spätere getaggte VM erbt die Garantie |
| **B** Eigenes Tag / eigene Zelle | Rust-Enum, Java `Optional` | widerspricht „Werte tragen keinen Typ-Tag" |
| **C** Belegungsmarkierung aus OPT-03 A **ist** `?T` | — | `T[]` kann keine Abwesenheit tragen |
| **D** Belegungsmarkierung ist eine zweite, container-interne Abwesenheit | C# `Dictionary` (`next`-Feld) | Rule-2-Frage im Speichermodell — tragbar, wenn hingeschrieben |

**Empfehlung: A, und D ausdrücklich entscheiden.** Mit OPT-03 B′ ist D eine
Implementierungsdatenstruktur eines Bibliotheksmoduls, wie `Map.states` heute.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-03, OPT-18, OPT-26, OPT-34, OPT-50.
**Confidence:** gelesen (`LyrValue.cs:18-120`, `Interpreter.cs:1152-1166`, `Bytecode.md:809-824`),
gemessen (`r22`).

---

### OPT-26 — Was von diesem Gebiet ist eine Bytecode-Formatänderung?

**Heute** (gelesen, `docs/Bytecode.md`): `:604` Typ-Tag `0x42`; **`:614`** „`void` is valid
only as a return type, never as a slot" (*korrigiert: `:613` ist leer*); `:809-818` Opcodes
`0x60`–`0x63` und „An optional does not nest. A reader must reject an inner type tagged
`0x42`"; `:819-824` Darstellung; `:953` lenient pull; `:144` DebugInfo (Slot-Namen).

| Empfehlung | Format? |
|---|---|
| OPT-01 A | nein |
| Nestung (OPT-01 C / 02 D / 03 C / 19 D) | **ja** (`:818`, `:604`) |
| OPT-03 A/B′ | nein — Natives, `int[]`-Feld |
| OPT-05 A, OPT-12 A, OPT-23 A | nein |
| OPT-14 B / OPT-23 B (`?void`) | **ja** (`:614`) |
| OPT-11 C (Ausdruckstext in der Panik) | ja, wenn der Text mitreist (DebugInfo `:144` trägt nur Slot-Namen) |
| OPT-19 C | **ja** (`:953`) |
| OPT-24 B, OPT-40, OPT-50 (weniger `optget`) | nein — Lowering-Entscheidung, Opcodes bleiben |

| Option | Vorbild | Preis |
|---|---|---|
| **A** Alle Empfehlungen formatfrei wählen | `PLAN.md:150-153` („ohne Formatänderung") | Nestung, `?void`, Iterator-Protokoll fallen |
| **B** Bump für 5.0 | — | Reader, Writer, Stub, DAP, Spec Kapitel 13 |

**Empfehlung: A.** Jede empfohlene Änderung ist formatfrei; das gehört in Kapitel 13
festgehalten.

**Bricht:** nein. **Hängt ab von:** OPT-01, OPT-11, OPT-19, OPT-23.
**Confidence:** gelesen.

---

### OPT-27 — Was macht `as` mit einem `?T`?

**Heute:** beide Richtungen `SEM0006` mit dem Vorschlag „give '?int' the conformance
`:: [Into<int>]'`" (`q16`, `q17`), der nach OPT-15 A dauerhaft unerfüllbar ist; Spec
`06-operators.md:86-87`.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `T → ?T` ist nur Koerzierung; `as` bleibt gesperrt, mit ehrlichem Text | Kotlin: `val b: Int? = a` ist die Weiterung; `a as Int?` ist ein **unsicherer Cast** (wirft `ClassCastException`), `as?` der sichere, `is` der Typtest — *korrigiert: die zweite Fassung nannte `as` einen Typtest*; der Punkt (kein Konvertierungsprotokoll im Spiel) bleibt | Diagnosetext |
| **B** `x as ?T` als erlaubte, wirkungslose Weiterung | C# `(int?)a`, Dart | Zwei Schreibweisen (Rule 2) |
| **C** `x as? T` als geprüfter Abwärtscast | Swift | Lyric hat keine Laufzeit-Typtests auf Klassen |
| **D** So lassen | — | Sackgassen-Vorschlag |

**Empfehlung: A, Text fällt mit OPT-15 A.**

**Bricht:** nein. **Hängt ab von:** OPT-15, OPT-39.
**Confidence:** gemessen.

---

### OPT-28 — Auf welcher Uhr läuft jede Änderung, und was schreibt ein Werkzeug um?

**Heute:** `Migration(…)` in `SemaRules.cs:351-355`; Spec `12-diagnostics.md:74-106`; vier
Codes `SEM0107`–`SEM0110` (gemessen `q11`, `q12`); „settled together with 5.0" (`:81-83`).
**`lyrfix` gibt es nicht** (`ls tools/`: `Bench`, `DocGen`; `lyrc --help` ohne Fix-Aktion;
kein `CodeAction` in `src/Lyric.Lsp/`), **aber `PLAN.md:343` führt es bereits als
Migrationswerkzeug für alle 5.0-Brüche** („die Ablage unten setzt es voraus"; `:376`, `:384`
verweisen darauf). *(Korrigiert: die zweite Fassung erfand ein Einmalwerkzeug für OPT-13 und
ignorierte den Posten.)*

| Klasse | Welche |
|---|---|
| Keine Warnung (additiv / Fehler→Fehler) | OPT-02, 03, 04, 05, 06, 07/B1, 08, 09, 10, 12, 14, 15, 16, 17, 19, 20, 21, 22, 23, 25, 26, 27, 29, 30, 31, 32, 34, 36, 37, 38, 39, 40, 41, 42, 44, 46, 47, 48, 49, 50 |
| Gewöhnliche Warnung (Antwort steht fest) | OPT-13 (Präzedenz), OPT-24 B (Capture + spätere Zuweisung), OPT-35 (Überbrückung), OPT-01 A (Guide-Zusage — Warnung ist hier nicht möglich, weil der Fall heute nicht kompiliert; es bleibt ein Doku-Posten) |
| §12.5-Migrationswarnung (Antwort offen) | OPT-18/35 (`?Struct`, auf der §3.4a-Uhr), OPT-33 B falls gewählt, OPT-45 (`x!` als lvalue, hängt an §3.4a) |

| Option | Vorbild | Preis |
|---|---|---|
| **A** Klasse je Empfehlung vor der Planung | Spec §12.5 | Buchhaltung |
| **B** `lyrfix` als das Werkzeug aus `PLAN.md:343` bauen; OPT-13 ist sein **erster** rein syntaktischer Fall | Go `go fix`, `cargo fix`; `lyrfmt` liefert den Roundtrip | Ein Werkzeug in `tools/` — geplant, nicht neu |
| **C** Quick-Fixes im LSP | TS, Kotlin, Dart | erreicht nur IDE-Nutzer |
| **D** Nichts | — | Handarbeit bei jedem Bruch |

**Empfehlung: A verbindlich; B als Beitrag zum geplanten `lyrfix`, nicht daneben; C später.**

**Bricht:** nein. **Hängt ab von:** allen.
**Confidence:** gemessen (`tools/`, `lyrc --help`, grep, `q11`, `q12`), gelesen
(`SemaRules.cs:351-355`, Spec §12.5, `PLAN.md:343`).

---

### OPT-29 — Was sieht der Editor?

**Heute** (gelesen `src/Lyric.Lsp/`): Hover zeigt den **deklarierten** Typ eines Locals
(`HoverProvider.cs:44-57`, `:97`); Vervollständigung hinter `?T` leer
(`CompletionProvider.cs:106-123`, `Optional` fällt in `_ => null`); kein `CodeAction`;
Hover erbt Befund E (`HoverProvider.cs:15`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Hover zeigt deklariert + genarrowt | TS, Kotlin | `SemanticModel` exportiert `_narrowed` nicht |
| **B** Vervollständigung bietet Member von `T`, schlägt `?.` vor | Dart, Kotlin, TS | ein `switch`-Fall |
| **C** Quick-Fix-Provider | C#, Kotlin, rust-analyzer | neuer Provider |
| **D** Nichts | — | OPT-07 A empfiehlt eine Diagnose, die kein Werkzeug ausführt |

**Empfehlung: B als Bugfix, A mit OPT-07, C mit OPT-28 B.**

**Bricht:** nein. **Hängt ab von:** OPT-07, OPT-13, OPT-16, OPT-21, OPT-28, OPT-42.
**Confidence:** gelesen, gemessen (grep).

---

### OPT-30 — Was passiert mit einem `?T` an der Host-Grenze?

**Heute** (gelesen `Marshal.cs`): hinein `EMB0001` (`:55-57`), `null` refused (`:36`);
heraus **kein** `TypeTag.Optional`-Fall — vor der `switch` nur Guards für `Void` (`:63-70`)
und `Host` (`:72-79`), dann `_ => value.AsI64` (`:91-105`). **Behauptet** (so markiert): ob die
Embedding-API eine `?T`-Rückgabe überhaupt bis `FromLyric` lässt — nicht gemessen.

| Option | Vorbild | Preis |
|---|---|---|
| **A** In beide Richtungen gesperrt, ein Code | — | Host liest keine optionale Rückgabe |
| **B** `?T` ↔ `Nullable<T>` / `null`-Referenz | C# | zwei Abbildungen je `T` |
| **C** `LyrOptional<T>`-Handle | Rust FFI | dritte Darstellung |
| **D** `?opaque` vom Host | — | formatfrei (`LyrValue.cs:113`) |

**Empfehlung: A jetzt (fehlender `case`), B als Ziel, D fällt mit B ab.**

**Bricht:** nein. **Hängt ab von:** OPT-15, OPT-02, Embedding-Gebiet.
**Confidence:** gelesen; Heraus-Richtung **nicht gemessen**.

---

### OPT-31 — Blockiert ein `?T`-Feld die Konformanz-Synthese?

**Heute:** Design (`design/conformance-synthesis.md:76-78`, `:188-194`), **4.7-Posten #2,
„Hängt an: 1" (`PLAN.md:258`; #1 ist `?T == ?T`, `:257`)** — *korrigiert*. Die Notiz
beantwortet `equals`; `hash` ungefragt; `compare` widerspricht OPT-06 A; `show` widerspricht
OPT-16 A.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `equals`/`hash` ja, `compare`/`show` refused **am Feld** | Rust `derive(PartialOrd)` | Typ mit einem `?T`-Feld ist nicht `Ordered`/`Display` |
| **B** Synthese gibt `null` intern Position und Text | — | zwei Semantiken |
| **C** `compare`/`show` warten auf OPT-06/16; Synthese in zwei Etappen | Swift | zwei Etappen |
| **D** OPT-16 C (`Debug`) zuerst | Rust | koppelt 4.7 an die Formatsprache |

**Empfehlung: A für `compare`, C für `show`; `hash` = Hash des inneren Werts, `null` ein
fester Wert — hingeschrieben.**

**Bricht:** nein. **Hängt ab von:** OPT-05, OPT-06, OPT-16, OPT-46.
**Confidence:** gelesen.

---

### OPT-32 — Wie weit matcht ein Muster durch ein Optional hindurch?

**Heute:** vollständig transparent — Literal (`q21`), Feldmuster (`q22`), Bindung am inneren
Typ (`q28`), Erschöpfung (`p17`). **Neu gemessen:** das Or-Pattern `5 | null` kompiliert und
matcht (`r24`, druckt `five-or-null`) — die zweite Fassung führte es als offen. Spec
`07-statements.md:117-119`: „`match` does not narrow the scrutinee … the proof travels through
the binding". Das Kotlin-/C#-Modell, nirgends als solches benannt.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen, festschreiben: Muster auf `?T` matcht `null` oder das Muster auf `T`; Or-Patterns dürfen Ebenen mischen | Kotlin, C# | ein Satz in Spec und Guide |
| **B** Explizite Schicht (`some(5)`, `none`) | Rust, Swift | bricht jedes `match` über `?T` |
| **C** A plus „nicht-null"-Muster | Swift `.some`, C# `is not null` | neue Form für etwas, das ein Bindungsarm kann |
| **D** Feld-/Range-Muster auf `?T` verbieten | — | bricht `q22` |

**Empfehlung: A, hingeschrieben — inklusive des gemessenen Or-Pattern-Falls.**

**Bricht:** nein. **Hängt ab von:** OPT-09, Pattern-Gebiet.
**Confidence:** gemessen.

---

### OPT-33 — Wie heißt die Abwesenheit in Lyric 5?

**Heute:** `null` (`Grammar.md:75`, `:112`, `:563`). Guide `09-optionals.md:3`: „there is no
null reference"; Spec `03-types.md:69`: „the one empty value at every depth".

| Option | Vorbild | Preis |
|---|---|---|
| **A** `null` behalten | Kotlin, Dart, C#, TS | Der Guide-Satz muss erklärt werden |
| **B** `none` | Swift (`.none`), F#, OCaml, Rust `None` | Bricht jedes Programm. *Korrigiert:* es stimmt **nicht** „mit `std.option` überein" — `option.lyr:11-12`, `:20-21` haben **kein** `isSome`/`isNone`, ausdrücklich |
| **C** `nil` | Swift-Literal, Go, Ruby, Lua | *Korrigiert:* Gos `nil` ist **untypisiert** (vordeklarierter Bezeichner, Typ aus dem Kontext); die „nil interface"-Falle entsteht, weil ein Interface mit typisiertem Nil-Zeiger ungleich `nil` ist — das ist ein Argument gegen Gos Interface-Darstellung, nicht gegen das Wort |
| **D** `nothing` | Julia, Haskell | längstes Wort fürs häufigste Literal |

**Empfehlung: A, mit hingeschriebener Begründung** (Guide-Satz nachziehen auf die Spec-Formel).

**Bricht:** A nein; B major. **4.x-Warnstufe:** B §12.5 + `lyrfix`.
**Hängt ab von:** OPT-22.
**Confidence:** gelesen.

---

### OPT-34 — Wer darf das uninitialisierte `T[]` sehen — und wie bekommt die stdlib es überhaupt?

**Heute** (*korrigiert, die Frage stand verkehrt herum*): `rawArrayAlloc<T>(n): T[]` ist
**privat** (`core.lyr:32-36`; `NativeRegistry.cs:378-379`: „the compiler emits the fill, and
nothing else may see it"; gemessen `r01` → `RES0004`). Sichtbar ist es für **niemanden** im
Quelltext; der Compiler emittiert es für `[first, ..rest]`. Die zweite Fassung führte es als
`pub`-Namen, dessen Verschwinden „minor" bräche, und empfahl eine Deprecation-Warnung, die
für niemanden feuern kann. Die reale Frage: **wie kommt `collections.lyr` an ein Native, das
heute privat ist?**

| Option | Vorbild | Preis |
|---|---|---|
| **A** `rawArrayAlloc` wird `pub` | — | Jedes Programm baut `Entity[]` mit acht Werten, die kein Initializer gebaut hat — Widerspruch zu Spec `03-types.md:95` und `SEM0106` |
| **B** Sichtbar nur für stdlib-Container-Module | Rust `unsafe` + `pub(crate)`; Java `Unsafe`. *Korrigiert:* Zig ist **kein** Vorbild — `undefined` hinterlässt keine Typ-Spur | Ein Sichtbarkeitsmechanismus („nur diese Module"), den Lyric nicht hat |
| **C** `unsafe`-Marker | Rust, Zig | Neues Sprachkonzept für eine Funktion |
| **D** Container-interne, nicht-generische Natives (`listAlloc`/`listClear`, `mapAlloc`/…), die Länge, Belegung **und Slot-Leeren** zusammen verwalten; `rawArrayAlloc` bleibt privat und compiler-emittiert | C# `Dictionary` (allokiert `entries` intern, leert bei `Remove`) | Ein Native-Paar pro Container statt eines generischen; **kein** neuer Sichtbarkeitsmechanismus |
| **E** Status quo: Container bleiben auf `(?T)[]` | — | OPT-03 bleibt ungelöst |

**Empfehlung: D.** Es ist OPT-03 B′ von der Sichtbarkeitsseite: die Natives gehören dem
Modul, das sie deklariert, wie `rawArrayAlloc` heute `std.core` gehört — Lyrics vorhandene
Regel („nicht `pub`") reicht, kein neuer Mechanismus.

**Bricht:** nein (nichts Sichtbares verschwindet; `grep rawArrayAlloc stdlib/` findet nur die
Deklaration).
**4.x-Warnstufe:** keine (*korrigiert: die Deprecation-Warnung der zweiten Fassung hätte
niemanden erreicht*).
**Hängt ab von:** OPT-03, OPT-25, Modul-Gebiet.
**Confidence:** gemessen (`r01`, grep), gelesen (`core.lyr:32-36`, `NativeRegistry.cs:374-379`,
`PLAN.md:165-171`, `Interpreter.cs:1113`).

---

### OPT-35 — Welche Uhr gilt für `?Struct`?

**Heute:** zwei Uhren für dieselbe Sachfrage — `SEM0109` (`q11`), `SEM0108` (`q12`); Spec
`12-diagnostics.md:81-83`: alle vier Fragen zusammen mit 5.0; `:103-104`: retiriert mit der
Regel.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `?Struct` ist ein Spezialfall der §3.4a-Frage, kein eigener Code | Spec §12.5 | Die Aliasing-Falle (`q06`) warnt bis 5.0 nicht |
| **B** Fünfte §12.5-Frage `SEM0111` | die vier Codes | Der Maintainer hat auf vier begrenzt |
| **C** Jetzt entscheiden (OPT-18 B) | — | nimmt §3.4a vorweg |
| **D** Nichts | — | Falle bleibt still |

**Empfehlung: A, mit gewöhnlicher Warnung als Überbrückung** („dieses `?Struct` teilt seinen
Wert mit `x`"). Reihenfolge: §3.4a → OPT-18 → OPT-07 B2 → OPT-45.

**Bricht:** nein. **4.x-Warnstufe:** gewöhnliche Warnung ab 4.7.
**Hängt ab von:** OPT-18, OPT-07, OPT-25, OPT-28, OPT-45.
**Confidence:** gemessen, gelesen.

---
### OPT-36 — Sind „optionaler Parameter" und „optionaler Typ" ein Konzept oder zwei?

**Heute:** zwei, unabhängig. `Grammar.md:221`: `Param = … TypeExpr [ '=' Expr ]`. Gemessen:
`fn f(x: ?int = null)`, `f()` → `-1`, `f(5)` → `5` (`r10`); `fn f(x: ?int = 7)`, `f()` → `7`
(`n08b`); **`fn f(x: ?int)` ohne Default, `f()` → `SEM0014: call expects 1 argument(s), got 0`**
(`r10d`) — ein `?T`-Parameter ist **nicht** implizit weglassbar. Generisch: `fn f<T>(x: ?T =
null)`, `f<int>()` grün (`r10b`), `f()` → `SEM0060` „no argument determines it" (`r10c`). Spec
`04-modules.md:84-86`: ein Default ist eine Aufrufstellen-Transformation und trennt keine
Überladungen; gemessen `r25`: `f(x: ?int = null)` neben `f()` koexistieren (verschiedene
Listen), `f()` wählt die Nullstellige (Regel 3, „needs no default").

| Option | Vorbild | Preis |
|---|---|---|
| **A** Zwei Konzepte, festschreiben: `?T` sagt „darf leer sein", `= e` sagt „darf fehlen"; kein implizites `= null` | **Swift**, **Kotlin** (beide verlangen `= nil`/`= null` ausdrücklich), Rust (kein Default) | `= null` an jedem weglassbaren `?T`-Parameter — dieselbe Boilerplate wie OPT-17 A, mit derselben Begründung |
| **B** `x: ?T` ohne Default ist implizit `= null` | **TypeScript** (`x?: T` ist genau das) | Weglassen und Vergessen werden ununterscheidbar; ein `?T`-Parameter, der *bewusst* verlangt wird, ist nicht mehr schreibbar |
| **C** Eigene Syntax `x?: T` | TypeScript | Dritte Schreibweise für „vielleicht nicht da" (Rule 2) |

**Empfehlung: A, in einem Satz neben OPT-17 A** — beide Fragen haben dieselbe Antwort und
denselben Grund: Weglassen muss von Vergessen unterscheidbar bleiben (`SEM0106`, `SEM0014`).
Für die Inferenz: `f<T>(x: ?T = null)` ohne Argument bleibt `SEM0060`; ein Default ist kein
Inferenzbeitrag, und das gehört in `08-generics`.

**Bricht:** nein (Status quo, festgeschrieben). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-17, OPT-02, OPT-37.
**Confidence:** gemessen (`r10`, `r10b`, `r10c`, `r10d`, `r25`, `n08b`), gelesen
(`Grammar.md:221`, Spec `04-modules.md:84-86`).

---

### OPT-37 — Überladung zwischen `T` und `?T`

**Heute:** erlaubt und aufgelöst. `f(int)` neben `f(?int)`: `f(null)` → 2, `f(5)` → 1, `f(o)`
mit `o: ?int` → 2 (`r09`, `r09b`); `f(?int)` neben `f(?string)`, `f(null)` → `SEM0086` mit
beiden Kandidaten (`r09c`). Regel: Spec `04-modules.md:92-102` — exakter Typ vor Zuweisbarkeit
(Regel 1: „fewest arguments that had to convert"). `T → ?T` ist dort eine **Konvertierung**,
und `null` passt „exakt" auf jedes `?T` — deshalb ist `r09c` mehrdeutig, und das ist richtig.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen und **hinschreiben**: `?T` steht in der Spezifitätsordnung des Overloading-Gebiets als „eine Konversion entfernt von `T`"; `null` ist zu jedem `?T` exakt, also mehrdeutig bei zwei `?`-Kandidaten | C# (`int` vor `int?`; `f(null)` bei `int?`/`string?` ist CS0121), Kotlin (dito) | Ein Absatz in `04-modules` §4.3a und im Overloading-Dossier |
| **B** `T`/`?T`-Überladung verbieten | — | Kein Vorbild; nimmt `f(int)`+`f(?int)` als bewussten API-Stil |
| **C** `null` bevorzugt den „kleinsten" `?T` | — | Keine Ordnung auf Typen; Willkür |

**Empfehlung: A.** Die Sprache tut heute das Richtige; es steht nur nirgends, dass `?T` in
der Ordnung eine Stufe unter `T` liegt.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** Overloading-Gebiet (Spezifitätsordnung), OPT-39.
**Confidence:** gemessen (`r09`, `r09b`, `r09c`), gelesen (Spec `04-modules.md:92-102`).

---

### OPT-38 — Nutzerdefinierte Option-Enums

**Heute:** möglich. `enum Opt<T> { Some(T), None }`, `Opt<?int>.Some(inner)` kompiliert und
matcht (`r04`, druckt `some -1`); `Grammar.md:259-263` (Payload-Varianten). `PLAN.md:404`
schließt `Option<T>` **als stdlib-Enum** aus; `CONTRIBUTING.md` Rule 2 sagt „ein Mechanismus
pro Konzept" — und hier hat Lyric 4 zwei: `?T` (eingebaut, nicht nestend) und jeden
Nutzer-Enum, der nestet. `Opt<?int>` ist genau die Nestung, die `??T` verbietet.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Erlauben und **benennen** als den vorgesehenen Ausweg für zwei Ebenen (`Map<K, Opt<V>>`, `Iterator<Opt<T>>`) — kein stdlib-`Option`, aber ein Spec-Satz, dass ein Enum eine zweite Ebene tragen darf | Rust (`Option` ist ein gewöhnliches Enum), Swift (`Optional` ist ein Enum) | Zwei Wege für „vielleicht" — vertretbar, weil der zweite kein Sprachmechanismus ist, sondern ein Enum wie jedes andere |
| **B** Verbieten: ein Enum darf keine Variante mit genau einem Payload und einer leeren Variante haben | — | Absurd: das ist die Form jedes Zustandsenums |
| **C** stdlib-`Option<T>` doch einführen | Rust | Das, was `PLAN.md:404` ausschließt — zwei Abwesenheiten in der Bibliothek |
| **D** Schweigen (Status quo) | — | Jeder findet es selbst, und niemand weiß, ob es erlaubt bleibt |

**Empfehlung: A.** Rule 2 fragt nach Mechanismen, nicht nach Datentypen; ein Nutzer-Enum ist
kein zweiter Mechanismus, sondern die Anwendung des einen (Enums). Was Lyric zusagen muss:
`Opt<?int>` bleibt gültig, weil `?int` als **Typargument an ein Enum** kein `??T` ist. Dieser
Satz fehlt in OPT-02 A und muss dort stehen („verboten, wo es zu `??` führt" — ein
Enum-Payload führt nicht dorthin).

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-01, OPT-02, OPT-03 E, OPT-04 E, OPT-19 E.
**Confidence:** gemessen (`r04`), gelesen (`Grammar.md:259-263`, `PLAN.md:404`).

---

### OPT-39 — Koerzierungstiefe von `T → ?T`: nur oben, oder Varianz?

**Heute:** nur an der obersten Stelle. Koerzierungsstellen, alle gemessen grün (`r23`, `q20`):
Zuweisung, Argument, Rückgabe, Struct-Initializer, Array-Literal-Element, `??`-Operand;
if-Ausdrucksarme (`p39`). **Nicht**: Tupel-Elemente (`q27`, Befund K); Funktionswerte
(`fn() -> int` nach `fn() -> ?int` → `SEM0001`, `r12`); Interface-Rückgaben (`get(): int`
gegen `get(): ?int` → `SEM0042`, `r13`); Array-Werte (`int[]` nach `(?int)[]` → `SEM0001`,
`r14`). Spec `03-types.md:31` zählt Literal-Adaptionsstellen; eine Koerzierungsliste für `?T`
gibt es nicht.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nur oberste Stelle, **Liste** in der Spec (mit Tupel nach Befund K), keine Varianz | **C#** (`Func<int>` ist nicht `Func<int?>`), Go | `let g: fn() -> ?int = h` braucht eine Lambda-Hülle; Interface-Implementierung muss `?int` schreiben |
| **B** Rückgabe-Kovarianz für Funktionswerte und Interface-Methoden (`R` ≤ `?R`) | **Kotlin**, **Swift**, **TypeScript** (alle drei) | Eine Varianzregel im Typsystem, wo es keine gibt (keine Vererbung, `Bytecode.md`: keine Tags); Lowering müsste an der Aufrufstelle einpacken oder die Signatur umschreiben |
| **C** Array-Kovarianz `T[]` ≤ `(?T)[]` | Java (unsound), C# (unsound) | Unsound bei Schreibzugriff — beide Vorbilder bereuen es |

**Empfehlung: A, mit der Liste — und B ausdrücklich ablehnen, C erst recht.** Lyric hat kein
Subtyping, und `T → ?T` ist heute eine Koerzierung an Wertstellen, keine Subtyp-Relation. B
würde die eine Koerzierung zu einer Typrelation machen, die in jede Signaturprüfung reicht;
dafür lohnt ein Lambda-Wrapper nicht.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** Befund K, OPT-27, OPT-37, Interface-Gebiet.
**Confidence:** gemessen (`r12`, `r13`, `r14`, `r23`, `q20`, `q27`, `p39`).

---

### OPT-40 — Join-Narrowing: etabliert eine Zuweisung ein Narrowing?

**Heute:** nein. `var x: ?int = null; if (x == null) { x = 5; } x + 1` → `SEM0003` (`r06`),
obwohl `x` nach dem Join sicher nicht-null ist; Kontrolle `r06b` (Früh-Return statt Zuweisung)
druckt `6`. Ursache `TypeChecker.cs:1483-1489`: nach `if`/`else` bleibt nur der **Schnitt**
der Narrowings, die beide Zweige *tragen*; `CheckAssign` (`:2860`) **entfernt** nur. Kotlin,
TypeScript und C# narrowen hier (alle drei: eine Zuweisung eines nicht-nullbaren Werts
etabliert den Fakt). Spec `07-statements.md:90`: „Exactly one form" beweist — die Zuweisung ist
keine.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | Spec §7.4 | Das häufigste „Defaulting"-Muster (`if (x == null) { x = default; }`) funktioniert nicht ohne `!` oder `??` |
| **B** Zuweisung eines nicht-optionalen Werts **etabliert** ein Narrowing; am Join gilt der Schnitt wie heute — dann hält `r06` (then: `x = 5` etabliert; else: `x != null` etabliert) | **Kotlin**, **TypeScript**, **C#** | Spec-Satz „Exactly one form" wird „zwei Formen"; OPT-08 B ist derselbe Mechanismus (Typ des Werts eintragen statt `Remove`) |
| **C** Nur OPT-08 B (Erhalten), nicht Etablieren | — | `r06` bleibt rot: im then-Zweig gab es nichts zu erhalten |

**Empfehlung: B — zusammen mit OPT-08 B, es ist eine Änderung.** `CheckAssign` trägt statt
`Remove` den Typ des zugewiesenen Ausdrucks ein; der Join bleibt der Schnitt. Kein
Konformanzfall pinnt das Gegenteil: `narrowing_follows_the_guard.lyr` (gelesen) pinnt nur
den Früh-Exit (`if (v == null) { return 0; } return v + v;`), keinen Join nach Zuweisung —
B ist also additiv auch gegenüber der Suite.

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-08, OPT-24, OPT-41.
**Confidence:** gemessen (`r06`, `r06b`), gelesen (`TypeChecker.cs:1483-1489`, `:2860`).

---

### OPT-41 — Welche Bedingungsformen narrowen, und warum `!` nicht?

**Heute:** Spec `07-statements.md:90-108` **zählt auf** (*korrigiert gegenüber der Kritik: die
Spec zählt sehr wohl; der Guide nicht*): genau eine Beweisform (direkter `==`/`!=` gegen
`null`), fünf Geltungsorte (if-Zweig, if-Ausdruck, `while`, rechts von `&&`/`||`, nach
Früh-Exit durch `return`/`throw`/`break`/`continue`/`panic`). Gemessen bestätigt: `continue`
(`n13a`), `throw` (`n13c`), `break` (`r21`), if-Ausdruck (`n23`), `||`-else (`p26`);
**nicht**: `!(x == null)` (`r07` → `SEM0003`). Die Negation fehlt in der Liste und in der
Implementierung (`NullCompared`, `TypeChecker.cs:1642-1647`, sieht nur `BinaryExpr`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Liste normativ lassen, `!` **ergänzen**: `!(e)` tauscht then-/else-Fakten | Kotlin, TS, C#, Dart (alle narrowen unter `!`) | Ein Fall in `NullCompared`: Negation vertauscht die Faktenpaare; Spec-Satz |
| **B** Liste lassen, `!` nicht | Status quo | `!(x == null)` ist ungewöhnlich, aber `!(a == null \|\| b == null)` ist es nicht |
| **C** Guide zieht die Liste nach | — | Reine Doku, kostet nichts, fehlt heute (Befund N) |

**Empfehlung: A + C.** Die Spec ist vollständig; der Guide sagt ein Fünftel davon. `!` ist
die eine Lücke mit vier Vorbildern und einem Einzeiler.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-40, OPT-24.
**Confidence:** gemessen (`r07`, `r21`, `n13a`, `n13c`, `n23`, `p26`), gelesen (Spec
`07-statements.md:90-108`).

---

### OPT-42 — Diagnosequalität der häufigsten Optional-Fehler

**Heute** (Befund V, alle gemessen): `p.v` auf `?P` → `SEM0012: '?P' has no member 'v'`
ohne Hinweis (`r16`); `xs[0]` auf `?int[]` → `SEM0007: … must implement 'Indexable<T>'` —
irreführend (`r17`); `??int` geschrieben → fünf Fehler ab `PAR0002 … got QuestionQuestion`,
kein Wort von Nestung (`r15`); `extend ?int` → keine Diagnose, Fehler erst am Aufruf (`r19`).
Zum Vergleich: `SEM0059`/`SEM0005` auf Nicht-Optional (`r18a`, `r18b`) haben gute Texte und
Notizen („declare it '?int' if it may be absent").

| Option | Vorbild | Preis |
|---|---|---|
| **A** Jede Diagnose auf einem `?T`-Empfänger trägt die Notiz „narrow it, or use `?.`" (Member, Index, Aufruf, Operator); `SEM0007` auf `?T[]` nennt das Optional statt `Indexable` | Kotlin („Only safe (?.) or non-null asserted (!!.) calls are allowed"), TypeScript („Object is possibly 'null'"), Dart | Eine Notiz je Stelle; ein `Optional`-Fall vor dem `Indexable`-Fall |
| **B** `??T` im Quelltext bekommt **eine** Meldung: „`??T` is not a type — optionals do not nest" | — | Lexer liefert `QuestionQuestion`; der Typparser braucht einen Fall dafür, der die Kaskade abbricht |
| **C** `extend ?T` → `SEM0047` mit dem Satz aus OPT-49 | — | Ein Fall in `CheckExtensionBlocks` (`TypeChecker.cs:629-637`) |
| **D** Nichts | — | Der häufigste Anfängerfehler des Gebiets bleibt ohne Wegweiser |

**Empfehlung: A + B + C — 4.6/4.7, Posten D.**

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-07 A, OPT-29 C, OPT-44, OPT-49.
**Confidence:** gemessen.

---

### OPT-43 — `??` mit Kontrollfluss-Ausstieg rechts

**Heute:** `a ?? panic("…")` kompiliert (`r11`, druckt `5`) — `NeverType` passt überall
(`TypeChecker.cs:6026`). `a ?? return 0` → `PAR0002: expected an expression, got Return`
(`r20`): `return`, `throw`, `break`, `continue` sind Anweisungen (`Grammar.md`, Statements).
`let x = a else { return 0; }` ist die einzige Ausstiegsform, und nur an Bindungsstellen.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen: `panic` ja (Ausdruck), `return`/`throw` nein; `let … else` ist die Form | Swift (`?? fatalError()` geht, `?? return` nicht — `guard let` ist die Form) | Mitten im Ausdruck (`f(x ?? return null)`) gibt es keine Form; das ist Swifts Lage |
| **B** `return`/`throw`/`break`/`continue` als Ausdrücke vom Typ `never`, nur rechts von `??` | **Kotlin** (`?: return`, `?: throw`), **C#** (`?? throw`), **Zig** (`orelse return`) | `Never`-typisierte Anweisungsausdrücke — eine Grammatikänderung (`Expr` bekommt `ReturnExpr`); `PLAN.md:405` schließt „`never` als allgemeiner Typ" aus, hier wäre es nur an einer Stelle |
| **C** Postfix `?` (OPT-20 B) | Rust | Vierte Bedeutung von `?` |

**Empfehlung: A, ausdrücklich — und OPT-20 A folgt daraus.** Der Gewinn von B ist ein
Ausdruck statt zwei Zeilen; der Preis ist ein `never`-Typ an Anweisungen, den `PLAN.md:405`
gerade ausgeschlossen hat. `a ?? panic(…)` bleibt als die eine Ausstiegsform im Ausdruck
(sie ist ein Aufruf, kein Sonderfall); für `return` ist `let … else` da. Was fehlt, ist der
Guide-Satz, dass es so ist.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-20, Fehlerbehandlungs-Gebiet (`try?`).
**Confidence:** gemessen (`r11`, `r20`), gelesen (`TypeChecker.cs:6026`, `PLAN.md:405`).

---

### OPT-44 — Codes mit zwei Bedeutungen

**Heute:** `SEM0005` = „`??` on 'int' — never null" (`r18b`) **und** „cannot force-unwrap
non-nullable 'int'" (`q05`); `SEM0059` = „`!= null` on 'int'" (`r18a`) **und** „`==` is not
defined for `?int`" (`p03a`). `PLAN.md` Posten D hat die Nicht-Optional-Fälle 2026-09-24 auf
diese Codes gelegt (`:172`); Spec `12-diagnostics.md` §12.1 fordert einen Code je Regel
(Regel 2: retiriert, nicht umgewidmet).

| Option | Vorbild | Preis |
|---|---|---|
| **A** Eigener Code für die Familie „Optional-Operator auf Nicht-Optional" (`!`, `??`, `??=`, `?.`, `== null`) — **ein** Code, fünf Texte | Kotlin (eine Warnung „unnecessary safe call", eine „unnecessary non-null assertion") | Ein neuer Code; `SEM0005`/`SEM0059` behalten je eine Bedeutung |
| **B** Je Operator ein Code | — | fünf Codes für eine Regel |
| **C** So lassen | — | Appendix A muss zwei Sätze unter einem Code führen |

**Empfehlung: A.** Mit OPT-05 A fällt die zweite Bedeutung von `SEM0059` ohnehin (`?T == ?T`
wird legal); danach trägt `SEM0059` nur noch den Nicht-Optional-Fall — und `SEM0005` sollte
ihm folgen. OPT-10 B (Warnung auf genarrowtem `!`) ist dann die **Warnungs**-Variante
desselben Codes.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-05, OPT-10, Diagnostik-Gebiet (Appendix A).
**Confidence:** gemessen (`r18a`, `r18b`, `q05`, `p03a`), gelesen (`PLAN.md:160`).

---

### OPT-45 — `x!` als lvalue

**Heute:** `x! = 5` → `SEM0019` (`r08`), aber `y!.v = 9` schreibt durch das Optional
(`q06`, `p44`, Befund G). `x!` ist also ein Pfad-**Präfix**, kein Pfad-**Ziel**. Grammatik
`Grammar.md:443`: postfix `!` steht in Stufe 1 neben `.` und `[ ]`, die beide Pfade bilden.

| Option | Vorbild | Preis |
|---|---|---|
| **A** `x!` ist nur Leseoperator; `y!.v = 9` wird **auch** verboten (konsistent mit OPT-18 B) | — | Bricht `y!.v = 9` (heute grün); mit OPT-18 B ohnehin |
| **B** `x!` ist lvalue: `x! = 5` schreibt den inneren Wert, panikt bei `null` | **Swift** (`o! = v` ist erlaubt, trappt bei `nil`) | Eine Zuweisung, die paniken kann; für `?Struct` bedeutet es, dass durch das Optional geschrieben wird — das ist OPT-18 A |
| **C** So lassen (asymmetrisch) | — | Regel steht nirgends |

**Empfehlung: A, entschieden mit OPT-18 auf der §3.4a-Uhr.** Die Asymmetrie ist ein Symptom
von Befund G: `y!.v = 9` funktioniert nur, weil `?Struct` eine Referenz ist. Wer OPT-18 B
wählt, muss `y!.v = 9` verbieten, und dann ist `x! = 5` konsequent ebenfalls verboten; wer
OPT-18 A wählt, bekommt Swifts B.

**Bricht:** A minor (`y!.v = 9`), gekoppelt an OPT-18.
**4.x-Warnstufe:** dieselbe wie OPT-35 (gewöhnliche Warnung auf `y!.v = …` über `?Struct`).
**Hängt ab von:** OPT-18, OPT-35, §3.4a.
**Confidence:** gemessen (`r08`, `q06`, `p44`).

---

### OPT-46 — Optionals in der Test-Bibliothek

**Heute:** `assertEq(a, null)` mit `a: ?int` → `LYR-IR0001: call to 'equals' on '?int'` in
`stdlib/std/test.lyr:29` (`r05`; Kontrolle `r05b` grün). `assertEq<T :: [Equatable<T>,
Display]>` (`test.lyr:28`) — `?T` erfüllt still (Befund B), das Lowering fällt.
`design/conditional-conformance.md:162-163` behauptet, `assertEq` rendere Optionals selbst
und schreibe `null` als Wort — **gemessen falsch**. Ein `?T`-Ergebnis ist in `std.test`
heute nur über `assertTrue(x == null, …)` oder `assertEq(x ?? sentinel, …)` prüfbar.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nach OPT-05 A: `assertEq` über `?T` funktioniert, weil `?T == ?T` legal ist — plus eine `Display`-Ausnahme **in der Funktion** (`showOptional`), wie die Designnotiz es meint | Rust (`assert_eq!(x, None)` über `PartialEq` + `Debug`) | Wartet auf OPT-05 **und** braucht `show` für `?T` in der Test-Ausgabe — also OPT-16 C oder eine Sonderfunktion |
| **B** Eigene Assertion jetzt: `assertNull(o)`, `assertSome(o): T` (liefert den Wert, panikt sonst) | JUnit `assertNull`, Kotlin `assertNotNull` (liefert den Wert) | Zwei Funktionen in `test.lyr`; **vor** OPT-05 schreibbar, weil `o == null` und `o!` heute gehen |
| **C** So lassen | — | Der häufigste Testfall des Gebiets (`assertEq(parseInt("x"), null)`) ist nicht schreibbar |

**Empfehlung: B jetzt (4.6, stdlib-Posten), A mit OPT-05.** B ist kein zweiter Mechanismus:
`assertSome` ist die Test-Form von `let … else`. Und der dritte stdlib-Eintrittspunkt von
Befund B gehört in die 4.6-Liste (§5), neben `console.lyr:51` und `collections.lyr`.

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-05, OPT-15, OPT-16; Test-Gebiet.
**Confidence:** gemessen (`r05`, `r05b`), gelesen (`test.lyr:28-31`,
`conditional-conformance.md:160-163`).

---

### OPT-47 — Wie zeigt der Debugger ein `?T`?

**Heute** (gelesen, `src/Lyric.Vm/Debugging/ValueRenderer.cs`; nicht gemessen — ein
DAP-Lauf braucht einen Client): `TypeTag.Optional` → `"null"` wenn `!IsSome`, sonst der
innere Wert unter dem Typnamen `?T` (`:43-46`); Kinder eines vorhandenen `?Struct` sind die
Kinder des inneren Werts (`:109-110`). `?T` ist also **nicht** als eigene Ebene sichtbar —
ein `?int = 5` sieht aus wie `5: ?int`. Ein geteiltes `?Struct` (Befund G) zeigt in zwei
Variablen dieselben Kinder, ohne Hinweis auf die Teilung. `(?int)[]` heißt `?int[]`
(`:195-196`, OPT-21). `lyrc --debug-info` hält Slot-Namen (`Bytecode.md:144`).

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen, festschreiben: der Debugger zeigt den Wert, nicht die Hülle | Kotlin/IntelliJ (`String?` zeigt den String oder `null`), TS/VS Code | Ein `?int` und ein `int` mit demselben Wert sind nur am Typnamen zu unterscheiden — das ist die Kotlin-Lage und sie stört niemanden |
| **B** Hülle sichtbar (`Some(5)`) | Rust/LLDB-Formatter | Lärm für die häufigste Anzeige |
| **C** A plus Referenz-Identität für `?Struct`: derselbe `LyrValue[]` bekommt dieselbe `variablesReference` | C# (Objekt-IDs im Watch) | `DebugModel` müsste Referenzen deduplizieren; heute je Zugriff neu (`DapServer.cs:562`) — behauptet, nicht gelesen im Detail |

**Empfehlung: A + OPT-21 A (Klammern) — und C zusammen mit OPT-18**, weil die Sichtbarkeit
der Teilung genau dann zählt, wenn §3.4a entschieden ist.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-18, OPT-21, Editor-/Werkzeug-Gebiet.
**Confidence:** gelesen (`ValueRenderer.cs:43-46`, `:109-110`, `:195-196`, `Bytecode.md:144`);
C **behauptet** im Detail.

---

### OPT-48 — Serialisierung eines `?T`-Felds

**Heute:** `stdlib/std/json.lyr` kennt `JsonValue.Null` als **Wert** („the JSON `null` — the
value, not an absent optional", `:45`), bietet `asInt(): ?int` usw. (`:54-116`) — Abwesenheit
**beim Lesen** ist `?T` — und `serialize(value: JsonValue)` (`:125`); es gibt keine
Serialisierung von Nutzertypen, also auch keine Regel für ein `?T`-Feld. Die Frage ist
OPT-04 auf der Datenseite: „Schlüssel fehlt" vs. „Schlüssel ist `null`" beim Lesen, und
„weglassen" vs. „`null` schreiben" beim Schreiben.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Regel festschreiben, bevor es eine Nutzertyp-Serialisierung gibt: lesen — fehlender Schlüssel **und** JSON-`null` werden beide zu `null: ?T` (`Map.get`-Analogie); schreiben — `null` wird als Schlüssel **weggelassen**; wer JSON-`null` schreiben will, nimmt `JsonValue.Null` | Kotlin `kotlinx.serialization` (`explicitNulls = false` ist genau das), Swift `Codable` (`decodeIfPresent`; `encodeIfPresent`) | Roundtrip verliert die Unterscheidung — dieselbe, die `?T` sprachlich nicht hat; **konsistent** mit OPT-01 A |
| **B** `null` wird als JSON-`null` geschrieben | Go `encoding/json` (`omitempty` optional), Rust `serde` (Default) | Ein `?T`-Feld erzeugt immer einen Schlüssel; „fehlend" ist nicht ausdrückbar |
| **C** Dreiwertig: `Opt<?T>`-artiges Feld unterscheidet fehlend/null/Wert | Rust `serde` mit `Option<Option<T>>`, TS `undefined` vs `null` | Das ist OPT-38 (Nutzer-Enum) auf der Datenseite — möglich, aber nicht Default |

**Empfehlung: A als Regel im stdlib-Gebiet, C als benannter Ausweg über OPT-38.** Die Regel
gehört hingeschrieben, bevor die erste Nutzertyp-Serialisierung (Konformanz-Synthese oder
`derive`-Ersatz) sie still trifft.

**Bricht:** nein (es gibt noch nichts zu brechen). **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-04, OPT-38, OPT-31; stdlib-Gebiet.
**Confidence:** gelesen (`json.lyr:45`, `:54-133`).

---

### OPT-49 — Methoden auf `?T`: kann `?T` ein `extend`-Ziel sein?

**Heute:** nein — **und die Sprache sagt es nicht.** `extend ?int { fn isPos(): bool … }`
kompiliert ohne Diagnose, der Aufruf scheitert mit `SEM0012` (`r19`, Befund U); Kontrolle
`extend int` druckt `4` (`r19b`). Ursache `TypeChecker.cs:629-637`: ein nicht auflösbares
Target wird ohne `SEM0047` übersprungen. Spec `05-interfaces.md:141`: „`extend T { … }` adds
methods to any visible type, builtins included" — ob `?T` ein „visible type" ist, sagt sie
nicht. Generische Methoden auf generischen Typen existieren (`r02`), also wäre `opt.map(f)`
statt `map(opt, f)` heute schreibbar, **wenn** `?T` ein Extend-Ziel wäre. *(Die zweite
Fassung begründete die freien Funktionen in `std.option` mit dem fehlenden Feature; der
wirkliche Grund ist, dass `?T` kein Nominaltyp ist.)*

| Option | Vorbild | Preis |
|---|---|---|
| **A** `?T` ist kein Extend-Ziel; `SEM0047` sagt es (OPT-42 C); `std.option` bleibt frei | Kotlin (keine Extension auf `T?` **als Typ** — aber `fun T?.foo()` als Receiver-Typ geht) | Ehrlich, billig; `map(opt, f)` bleibt die Form |
| **B** `extend<T> ?T { … }` als generisches Extend über dem Optional-Konstruktor | **Swift** (`extension Optional where Wrapped: …`), Kotlin (`fun <T> T?.orZero()`) | Ein zweiter Extend-Zielbegriff (Typkonstruktor statt Nominaltyp); Methodenauflösung auf `Optional` in `MemberLookup`; und die Frage, ob `opt.map(f)` mit `?.` kollidiert (`opt?.map` ≠ `opt.map`) |
| **C** `std.option` als Methoden über einen versteckten Nominaltyp | — | Zwei Darstellungen desselben Typs (Rule 2) |

**Empfehlung: A jetzt, B als eigene Frage im Interface-/Extend-Gebiet.** B ist attraktiv
(Swift zeigt, wie viel `extension Optional` trägt), aber es ist eine Änderung am
Extend-Zielbegriff, nicht am Optional. Was dieses Gebiet schuldet, ist der Fehler (`r19`) und
der Satz in der Spec.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-42, Interface-/Extend-Gebiet, Generics-Gebiet.
**Confidence:** gemessen (`r19`, `r19b`, `r02`), gelesen (`TypeChecker.cs:629-637`, Spec
`05-interfaces.md:141`).

---

### OPT-50 — Kosten des Narrowings im Bytecode

**Heute:** gemessen `r22` (`lyrc lower`): drei genarrowte Lesungen im selben Block → **drei
`optget`** (`Bytecode.md:816`), plus ein `optissome` für den Test. Das ist Spec
`07-statements.md:122` wörtlich („a read of a narrowed variable compiles to a checked
unwrap") und die Voraussetzung der Staleness-Regel (OPT-24 E): nur weil jede Lesung prüft,
darf ein veralteter Beweis paniken statt Speicher zu korrumpieren. Befund F zeigt die
Kehrseite: das Lowering liest an der Compound-Stelle den **deklarierten** Typ, wo die Sema den
genarrowten geschrieben hat (`q03`) — die Stelle, an der der Unwrap eingefügt wird, ist im
Lowering pro Ausdrucksform verteilt, nicht an einem Ort.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Ein `optget` pro Lesung (Status quo), festschreiben | Spec §7.4 | Ein Check pro Zugriff — billig (`optget` ist ein `Ref`-Vergleich, `LyrValue.cs:116-120`), aber es sind *n* Checks für einen Beweis; und jeder kann paniken |
| **B** Ein `optget` pro Region: beim Eintritt in den genarrowten Block wird in einen Schatten-Slot `T` ausgepackt, Lesungen gehen dorthin | Kotlin (Smart Cast ist ein Cast einmal am Beweis; JVM lädt danach ohne Check), C# | Braucht OPT-24 B (sonst liest eine Closure den Schatten-Slot und sieht die spätere Zuweisung nicht — genau der Fall, den `q13` heute per Panik fängt); Schreibzugriffe im Block müssen beide Slots treffen (OPT-08/40) |
| **C** A, aber der Optimierer streicht redundante `optget` zwischen zwei Zuweisungen | — | Ein IR-Pass, formatfrei; der Panik-Fall bleibt für die erste Lesung erhalten |

**Empfehlung: A jetzt festschreiben, C als Optimierer-Posten, B nur mit OPT-24 B.** Es ist
keine Formatfrage (Opcodes bleiben), aber eine Lowering-Frage, die OPT-24 und OPT-11 C
(welchen Text die Panik trägt) bindet: mit A nennt die Panik die Lesung, mit B den
Blockeintritt.

**Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** OPT-24, OPT-11, OPT-25, OPT-26; Optimierer-Runde (`PLAN.md`, VM-Befunde).
**Confidence:** gemessen (`r22`), gelesen (Spec `07-statements.md:121-125`, `Bytecode.md:816`,
`LyrValue.cs:116-120`).

---
## 4. Was wir übernehmen sollten

Geordnet nach Ertrag pro Aufwand.

1. **Kotlin / TypeScript 5.4: das Narrowing endet in einer Closure nur, wenn die `var`
   danach noch zugewiesen wird.** Ersetzt die Staleness-Regel der Spec (§7.4), die ein
   Programm ohne `!` an einem Force-Unwrap sterben lässt; kostet nur `var`-Locals mit
   späterer Zuweisung. *(OPT-24 B, Befund P)*
2. **C#: `x?.m()` als Anweisung ohne Ergebnistyp** — nicht als Konsens verkauft, sondern als
   die Antwort der einen Sprache ohne Einheitswert. *(OPT-23 A, Befund Q)*
3. **Zig / Swift: die Capture-Formen als Antwort auf alles, was kein Name ist** — Lyric hat
   sie, kein Guide-Kapitel und kein Konformanzfall kennt sie. Pin zuerst, dann Guide.
   *(OPT-09)*
4. **Rust: `?T` erfüllt nur, was ein `impl` sagt** → `Satisfies` liefert `false`; drei
   stdlib-Eintrittspunkte fallen. *(OPT-15, OPT-16, OPT-27, OPT-46)*
5. **TypeScript / C# / Kotlin: Zuweisung eines nicht-optionalen Werts erhält **und
   etabliert** ein Narrowing.** Eine Änderung in `CheckAssign`, zwei Fragen. *(OPT-08, OPT-40)*
6. **Kotlin / TS / C# / Dart: `!` narrowt.** Ein Fall in `NullCompared`. *(OPT-41)*
7. **.NET `Dictionary`: das Backing leert freie Slots** — in Lyric kann das nur die VM, also
   container-interne Natives statt eines `pub rawArrayAlloc`. *(OPT-03 B′, OPT-34 D)*
8. **Rust / Swift: ein Nutzer-Enum trägt die zweite Ebene.** Geht heute (`r04`); es fehlt der
   Satz, dass es gewollt ist. *(OPT-38, OPT-04 E, OPT-19 E)*
9. **C# / Dart: `?[ ]`.** *(OPT-12)*
10. **Kotlin / C#: Muster sehen durch das Optional hindurch, Or-Patterns mischen Ebenen.**
    Ist gemessen so; fehlt der Satz. *(OPT-32)*
11. **Kotlin: Gleichheit auf Optionals, keine Ordnung** — schon 4.7-Posten #1. *(OPT-05, OPT-06)*
12. **Swift / Kotlin: `??` über die Vergleiche** — als Grammatikposten mit Spec-Zwilling und
    dem geplanten `lyrfix`. *(OPT-13, OPT-28)*
13. **Kotlin / TS / Dart: Diagnosen mit Wegweiser** („narrow it, or use `?.`"); der Editor
    kennt Optionals. *(OPT-42, OPT-29)*
14. **JUnit / Kotlin: `assertNull` / `assertSome`** — heute schreibbar, vor OPT-05. *(OPT-46)*
15. **Rust: `Debug` neben `Display`**, terminiert vor der Konformanz-Synthese. *(OPT-16, OPT-31)*

**Was wir bewusst nicht übernehmen:** Rust/Swift/Haskell-Nestung (Hauptentscheidung +
Formatbump — aber nicht mehr „die einzige Option", OPT-38 ist der Ausweg); Swifts `Void?` /
Kotlins `Unit?` (setzen einen Einheitswert voraus, den Lyric nicht hat); Kotlins `?: return`
(`never` an Anweisungen, `PLAN.md:405`); Rusts `?`-Propagation; C#' „lifted `<`";
TypeScripts zweite Abwesenheit; Kotlins/Swifts/TS' Rückgabe-Kovarianz für `?T`; Java/C#'
Array-Kovarianz; TypeScripts implizites `x?: T`; Darts `late`; Swifts `if let x`-Kurzform;
Rusts `unsafe`; ein anderes Schlüsselwort als `null`; Swifts `extension Optional` (vorerst —
eigene Frage im Extend-Gebiet).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 („ein Mechanismus pro Konzept"):**

- **Die Rule-2-Tabelle führt „Abwesenheit" nicht auf.** Die Regel „`?T` ist die einzige Form"
  steht in Spec `03-types.md:68`, Guide `09:3`, `PLAN.md:404` — nirgends in `CONTRIBUTING.md`.
- **Zwei Wege für „vielleicht"**: `?T` und jeder Nutzer-Enum (`r04`). Das ist tragbar — der
  Enum ist kein Sprachmechanismus —, aber es muss hingeschrieben werden, und OPT-02 A muss
  den Enum-Payload ausdrücklich ausnehmen. *(OPT-38)*
- **Zwei Konzepte hinter „optionaler Parameter"**: `?T` (darf leer sein) und `= e` (darf
  fehlen). Sie sind zwei und sollen es bleiben; TypeScripts `x?: T` wäre die Verschmelzung.
  *(OPT-36)*
- **Das Auspacken hat vier Formen** ohne ADR. *(OPT-09)*
- **Zwei Abwesenheitsdarstellungen im Speichermodell** (`?T` und die Belegungsmarkierung) —
  tragbar als Bibliotheksdatenstruktur, mit OPT-03 B′/OPT-34 D ohne neuen Mechanismus.
- **Zwei Codes mit je zwei Bedeutungen** (`SEM0005`, `SEM0059`). *(OPT-44)*
- Abgelehnt, weil zweiter Mechanismus: OPT-04 B als zweites `get` (stattdessen `getEntry`),
  OPT-11 D (`x! "msg"`), OPT-17 C (`late`), OPT-20 B / OPT-43 B (`?`-Propagation,
  `never`-Anweisungen), OPT-14 B (Einheitswert), OPT-34 C (`unsafe`), OPT-35 B (fünfte Uhr),
  OPT-36 C (`x?: T`), OPT-49 C (versteckter Nominaltyp).

**Mit ausdrücklich ausgeschlossenen Punkten:**

- **Nestung** (OPT-01 C / 02 D / 03 C / 04 C / 19 D): `PLAN.md:404`, Spec `03-types.md:68`,
  `Grammar.md:341`. Nicht empfohlen. *(Korrigiert: nicht mehr „die einzige Option, die
  OPT-03/04/19 zusammen löst" — der Nutzer-Enum tut es ohne Format.)*
- **`Option<T>` als stdlib-Enum** (`PLAN.md:404`): bleibt ausgeschlossen; OPT-38 A erlaubt
  den Nutzer-Enum, führt keinen stdlib-Typ ein.
- **Ordnung auf Optionals** (`PLAN.md:405`): OPT-06 bestätigt; OPT-31 zeigt, dass die
  Konformanz-Synthese den Ausschluss nicht mitträgt.
- **`never` als allgemeiner Typ** (`PLAN.md:405`): OPT-43 B abgelehnt.
- **Die vier §12.5-Fragen zusammen mit 5.0** (`12-diagnostics.md:81-83`): OPT-18/35/45
  ordnen sich ein.
- **Spec §7.4 Staleness-Regel + Konformanzfall**: OPT-24 B nimmt sie zurück — der einzige
  Posten dieses Gebiets, der einen bestehenden Konformanzfall ersetzt. Modus spec-first.
- **Guide `09:19` Idempotenz-Zusage**: OPT-01 A nimmt sie zurück — dokumentarisch, nicht im
  laufenden Code (der Fall kompiliert heute nicht).

**Mit anderen Gebieten:**

| Frage | Gebiet |
|---|---|
| OPT-02, OPT-03, OPT-15, OPT-38, OPT-39 | Generics / Monomorphisierung / bedingte Konformanz |
| OPT-05, OPT-06, OPT-16, OPT-31, OPT-46, OPT-48 | Konformanz-Synthese, Formatierung, Test, stdlib |
| OPT-07, OPT-18, OPT-35, OPT-45 | **Werte-/Mutabilitätsgebiet** (§3.4a) — entscheidet **vor** diesem Gebiet |
| OPT-11, OPT-30 | Fehlerbehandlung, Embedding |
| OPT-12 C, OPT-49 | Member-/Extend-Gebiet |
| OPT-13, OPT-43 | Grammatik / Operatoren (Präzedenztabelle, `never`) |
| OPT-19, OPT-24 (c) | Iteratoren, Koroutinen |
| OPT-20, OPT-43 | Fehlerbehandlung (`try?`) |
| OPT-22, OPT-33, OPT-42, OPT-44 | Lexik, Diagnostik (Appendix A) |
| OPT-25, OPT-26, OPT-50 | Laufzeitmodell, Bytecode, Optimierer |
| OPT-28, OPT-29, OPT-47 | Toolchain (`lyrfix`, LSP, DAP) |
| OPT-03, OPT-04, OPT-34, OPT-48 | Standardbibliothek |
| OPT-32, OPT-09 | Pattern-Matching |
| OPT-36, OPT-37 | Funktionen, Overloading |

**Was in 4.6/4.7 gehört, nicht in 5.0** (alles Fehler, alle gemessen): der falsche Notiztext
und der zweite Pfad (OPT-01), `?void` als ICE (OPT-14), `p?.act()` als `IR0001` (OPT-23),
**`null == null` als `IR0001`** (OPT-22, neu), das Constraint-Loch mit **drei** Eintrittspunkten
— `console.lyr:51`, `collections.lyr`, **`test.lyr:29`** (OPT-15, OPT-46, neu) —, der
`as`-Vorschlagstext (OPT-27), der Typdrucker im Compiler **und im Debugger** (OPT-21), das
kaputte IR bei `o += 1` (Befund F), das zu frühe `defer`-Ende (Befund R), **`extend ?int` ohne
Diagnose** (OPT-49, neu), die `??int`-Kaskade und die Diagnosen ohne Wegweiser (OPT-42, neu),
die leere Vervollständigung hinter `?T` (OPT-29), der fehlende `TypeTag.Optional`-Fall in
`Marshal.FromLyric` (OPT-30), `assertNull`/`assertSome` (OPT-46).

---

## 6. Nach der Kritik geändert

**Kritikpunkte, die ich nachgeprüft habe und übernommen habe (jeweils Beleg):**

- **`rawArrayAlloc` ist privat** (`core.lyr:32-36`, `NativeRegistry.cs:378-379`, `r01` →
  `RES0004`). OPT-34 stand verkehrt herum; jetzt: wie bekommt die stdlib Zugang. OPT-03 nicht
  mehr zirkulär (A über B′ = container-interne Natives, die auch Slots leeren).
- **Generische Methoden auf generischen Typen existieren** (`PLAN.md:121`, `r02`). Abhängigkeit
  aus OPT-03 gestrichen; §2.2 begründet die freien Funktionen jetzt richtig (OPT-49).
- **Alle STATUS-/PLAN-/Bytecode-Zeilen neu gezogen**: `STATUS.md:592`, `:662`, `:2061`,
  `:2212-2219`, `:2284`, `:2447-2451`; `PLAN.md:108`, `:121`, `:165-171`, `:172`, `:257-258`,
  `:343`, `:404-405`; `Bytecode.md:614`.
- **4.7-Posten**: `?T == ?T` ist #1, Konformanz-Synthese #2 (OPT-05, OPT-31).
- **`std.option` hat kein `isNone`** (`option.lyr:11-12`, `:20-21`); OPT-33 B korrigiert.
- **`null == null` → `IR0001`** (`r03`, Kontrolle `r03b`): Befund T, OPT-22 mit Vergleichsregel,
  4.6-Liste.
- **Drei Dokumente, drei Antworten zur Nestung** (Guide `:19` Idempotenz, Spec `:68-69`
  „entsteht nie", Compiler `IR0001`/`CLI0020`). OPT-01 A ist eine Rücknahme einer Zusage,
  so geführt.
- **Nutzer-Enum nestet** (`r04`). „Einzige Option" gestrichen; OPT-38 neu; E-Optionen in
  OPT-03/04/19.
- **Namen**: `LyrValue.Some` (nicht `Wrap`), `WidenAgainstNull` (nicht `Unify`).
- **OPT-13-Zahlen**: Konformanz 4 Codezeilen + 2 Kommentare; Tests 5, davon 2 mit Vergleichen
  (beide Goldens). Fazit hält; die Klasse ist jetzt „Grammatikänderung mit Spec-Zwilling".
- **`if (let …)` fehlt im ganzen Guide und in der Konformanzsuite** (grep beides leer).
  OPT-09: Pin zuerst, dann Abschnitt.
- **Vergleichssprachen**: Swift `Void?` / Kotlin `Unit?` (OPT-23, OPT-14); .NET `Dictionary`
  leert Slots (OPT-03); TypeScript 5.4 (§2.1, OPT-24); Kotlin `as`/`as?`/`is` (OPT-27); Go
  `nil` untypisiert (OPT-33); Zig `undefined` ohne Typspur (OPT-34); Kotlin verbietet nur
  **verändernde** Captures (OPT-24: Vorbild für B).
- **OPT-28**: `lyrfix` ist `PLAN.md:343`, kein Einmalwerkzeug.
- **OPT-24**: Empfehlung von A auf B; „zweiter Pass" gestrichen — ein Vorwärtslauf über die
  Zuweisungsstellen reicht (Parameter unveränderlich `n14b`, `let` sicher `n14a`).
- **OPT-30**: „behauptet" für die Heraus-Richtung bleibt und ist so markiert.

**Kritikpunkte, die ich geprüft habe und bei denen die ursprüngliche Aussage hält:**

- **„Weder Guide 09 noch Spec §7 zählen die Narrowing-Formen auf"** — für die Spec falsch:
  `07-statements.md:90-108` zählt Beweisform, fünf Geltungsorte, Komposition und Invalidierung
  vollständig auf. Nur der Guide ist lückenhaft. OPT-41 fragt daher nur nach `!` und dem Guide.
- **OPT-13-Zahl für `stdlib/`+`examples/`**: 9 Codezeilen, 0 mit Vergleich — neu gezählt,
  bestätigt (die `bin/`-Kopien unter `examples/embedded-host/` sind ausgeschlossen).
- **OPT-14 B „Kotlins `Unit?` wird zu nichts gelowert"** — stimmt weiter; was neu ist, ist die
  Rolle von `Unit?` als Vorbild für OPT-23 B, nicht für OPT-14 B (`bool`-Lowering).

**Was ich selbst gefunden habe, was die Kritik nicht sah:**

- **Befund P ist spezifiziert und gepinnt** (Spec `07-statements.md:121-125`,
  `narrowing_stale_in_a_lambda_panics.lyr`). Die zweite Fassung nannte es ein unentdecktes
  Soundness-Loch; es ist eine Entscheidung „memory safety, not proof persistence". OPT-24 ist
  damit eine Spec-Rücknahme mit Konformanz-Zwilling, und OPT-50 (ein `optget` pro Lesung,
  `r22`) ist ihr gemessener Preis.
- **`extend ?int` wird still verschluckt** (`r19`, Kontrolle `r19b`; `TypeChecker.cs:629-637`).
  Befund U, OPT-49, OPT-42 C.
- **Der Debugger hat denselben Klammerfehler wie der Typdrucker** (`ValueRenderer.cs:195-196`).
- **Or-Pattern `5 | null` matcht** (`r24`) — die offene Unterfrage aus OPT-32 ist gemessen.
- **`design/conditional-conformance.md:162-163`** („`assertEq` rendert Optionals selbst") ist
  gemessen falsch (`r05`), und das dort zugesagte `showOptional` existiert nicht.

**15 Fragen ergänzt:** OPT-36 (Parameter-Default vs. `?T`), OPT-37 (Überladung `T`/`?T`),
OPT-38 (Nutzer-Option-Enum), OPT-39 (Koerzierungstiefe / Varianz), OPT-40 (Join-Narrowing),
OPT-41 (Bedingungsformen, `!`), OPT-42 (Diagnosequalität), OPT-43 (`??` mit Ausstieg),
OPT-44 (Codes mit zwei Bedeutungen), OPT-45 (`x!` als lvalue), OPT-46 (`std.test`),
OPT-47 (Debugger), OPT-48 (Serialisierung), OPT-49 (`extend ?T`), OPT-50 (Kosten des
Narrowings).

**Neue Proben:** `r01`–`r25` (34 Dateien inkl. Kontrollen) in `probes/optionals-rev3/`,
Erwartungen vorab in `ERWARTUNG.txt`; eine Erwartung war falsch (`r19`: erwartet Ablehnung,
gemessen stille Akzeptanz — daraus Befund U).

---

## 7. Nach der Kritik geändert — Kurzfassung

- OPT-34 umgedreht (Native ist privat), OPT-03 entzirkelt (B′: container-interne Natives).
- OPT-24 von A auf B; Befund P als Spec-Regel + Konformanzfall erkannt, nicht als Loch.
- OPT-23/OPT-14: Swift/Kotlin-Vorbilder für `Void?`/`Unit?` eingetragen; Empfehlung A bleibt,
  Begründung ist jetzt der fehlende Einheitswert.
- OPT-01 als Rücknahme der Guide-Zusage geführt; OPT-38 (Nutzer-Enum) neu; „einzige Option"
  gestrichen.
- OPT-13 als Grammatikposten mit Spec-Zwilling; Messzahlen korrigiert; OPT-28 an `PLAN.md:343`.
- OPT-22 mit `null == null`; OPT-05/31 Posten-Nummern; OPT-33 ohne `isNone`; Go/Zig/Kotlin
  korrigiert.
- Alle Zeilenangaben neu; Funktionsnamen korrigiert; „behauptet" nur an OPT-30, OPT-47 C,
  Dart-Zelle in §2.1.
- 15 Fragen ergänzt (OPT-36–OPT-50), vier Befunde ergänzt (T, U, V, Debugger in E).
