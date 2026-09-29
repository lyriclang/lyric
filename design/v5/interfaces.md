# Lyric 5 — Gebiet: Interfaces und Polymorphie

Stand: 2026-09-27, dritte Fassung nach der zweiten adversarischen Kritik.
Gemessen gegen den Debug-Build von `main` (`6f6f029f`, 2026-09-24, Baum nennt sich 4.6.0).
Proben: `…/scratchpad/v5-design/probes/interfaces/` (erste Runde `p01`–`p41`),
`…/probes/interfaces/rv/` (`rv01`–`rv34`, `multi/`, `multi2/`),
`…/probes/interfaces/rev2/` (`q01`–`q18`, `vis/`, `shadow/`, `bconly/`),
`…/probes/interfaces/audit3/` (Kritikerproben `n01`–`n50`, **alle in dieser Runde selbst neu
gelaufen**), `…/probes/interfaces/rev3/` (diese Runde: `r01`–`r09`, `r07/`, Erwartungen vorab in
`rev3/ERWARTUNG-rev3.txt`).

**Zur Belegtreue.** Jede Zeilenangabe ist gegen `6f6f029f` gemessen und trägt, wo sie trägt, den
zitierten Satz mit. Der Spec-Klon `…\lyric-spec` steht auf `8f17c02` (Suite: 178 Fälle, davon 21 mit
`since: 4.6.0`, gezählt). Confidence-Stufen: **gemessen** (Programm gebaut und gelaufen),
**gelesen** (Pfad:Zeile), **behauptet** (weder noch — steht dann ausdrücklich dabei).

---

## 0. Kurzfassung

Lyrics Interface-System ist für seine Größe konsistent, aber es hat **zwei** Beschränkungen darüber,
welche Interfaces Werte sein dürfen, und nur eine davon hat einen Namen:

1. `LYR-SEM0082` — ein generischer Interface-Member braucht einen Body und kann nicht überschrieben
   werden (`src/Lyric.Frontend/Sema/TypeChecker.cs:529-538` und `:698-714`). Sauber hergeleitet.
2. **Unbenannt, und in der zweiten Fassung falsch formuliert**: nicht „ein memberloses Interface",
   sondern **ein Interface, dessen ganze Kette keinen Slot hat**, darf kein Wert sein. Gemessen
   `audit3/n01`: `interface Base { fn f(): int; } interface Sub :: [Base] { }` — `Sub` hat keinen
   eigenen Member, ist als Wert legal und druckt `7`. Gelesen: `SlotNames`
   (`src/Lyric.Frontend/Ir/Lowering/TypeTable.cs:676`) läuft **zuerst** über
   `Conformance.ParentsOf`, dann über die eigenen Member; erst eine leere Gesamtliste wirft
   (`TypeTable.cs:644`: *„declares no methods; an empty interface has nothing to dispatch on"*).
   Die Note *„this compiler version cannot lower it yet"* steht **nicht** dort, sondern ist die
   Kategorie aller Lowering-Refusals (`src/Lyric.Frontend/Ir/Lowering/LoweringDiagnostics.cs:28`,
   `Category`). Kontrollen wie gehabt: `rv32` (als Constraint) und `q01` (Konformanz ohne
   Wertgebrauch) laufen. Confidence: **gemessen** + **gelesen**.

Was sichtbar fehlt: kein `Self`, keine statischen Member, keine assoziierten Typen, kein Downcast,
keine bedingte Konformanz. Was **nicht** sichtbar fehlt, ist das Teurere: derselbe Wert antwortet
unterschiedlich, je nachdem über welchen von **drei** Empfängerpfaden er angefasst wird — den
syntaktisch konkreten (`C { }.greet()`), den Interface-Wert (`g.greet()`) und den
**monomorphisierten** (`fn f<T :: [Greeter]>(x: T) { x.greet() }`). Die zweite Fassung kannte nur
zwei.

Die Befunde dieses Durchgangs (§1.3), in der Reihenfolge ihres Gewichts:

- **Die Spec sagt, was in der vtable-Zeile steht, und der Compiler tut es nicht.**
  `lyric-spec/spec/05-interfaces.md:126-130` (§5.4): *„own member, then visible extension method,
  then a default method … The chosen target stands in the compiled module — a vtable row holds
  function indices, and the runtime searches nothing."* Gemessen (`p23`, `lyrc lower`): die Zeile
  ist `impl ty0 :: ty1 [f2, f1]` mit `f1 = main.Greeter.greet` (der Default); die Extension
  `main.<extend>.C.greet` ist `f3` und steht **nicht** in der Zeile. Befund 2 ist damit keine
  „halbierte Regel", sondern ein **Spec-Verstoß** — und die Suite pinnt die 4.6-Regel nicht (kein
  Fall mit `since: 4.6.0` erwähnt `extend`, gezählt).
- Ein Compiler-Defekt ist eine **Familie**: Argumentformung passiert vor `call`, nicht vor
  `callvirt` und nicht im monomorphisierten Aufruf (Default-Argumente **und** `params`, vier
  Messungen).
- Ein Struct wird beim Übergang zum Interface-Wert kopiert und danach geteilt — im Lowering
  **benannt und begründet** (`FunctionLowerer.cs:3041-3046`), in der Spec **nicht**; `mkiface`
  selbst alloziert nichts. Die zweite Fassung nannte das „geboxt" und „unbenannt" — beides ungenau.
- `docs/Bytecode.md:364-365` verbietet den Struct links in der Impls-Zeile, `lyrc` schreibt ihn
  dorthin, der Reader prüft es nicht.

---

## 1. Ist-Stand

### 1.1 Was gebaut ist

| Mechanismus | Verhalten | Beleg |
|---|---|---|
| `::`-Liste auf struct/class/enum | deklariert Konformanz; Enum geht | gemessen `p22`; `docs/Grammar.md:230-264` |
| `::`-Liste auf `interface` | deklariert **Eltern**, Implikation, keine Wertkonvertierung | `docs/Grammar.md:286-296`; `lyric-spec/spec/05-interfaces.md:112-115` |
| Mehrere Eltern | seit 2.16; Diamant erlaubt; zwei Eltern mit gleichem Namen → `SEM0079` | gemessen `p17`; `docs/guide/07-interfaces.md:118-130` |
| Chain-Member neu deklarieren | refused, `SEM0079` | gemessen `p18` |
| **Chain-Prefix-Slotlayout** | die Slots des Elternteils sind die **ersten** Slots des Kindes | gemessen `q17`; gelesen `TypeTable.cs:676-681` |
| Default-Methoden | eigener Member gewinnt; Default dispatcht virtuell | gemessen `p20` |
| **Zwei Defaults gleichen Namens aus zwei Interfaces** | Konformanz wird **angenommen**; `C{}.greet()` ist `SEM0043` (Benutzungsdiagnose, `TypeChecker.cs:3910`); **`let a: I1 = C{}` antwortet `i1`, `let b: I2 = C{}` antwortet `i2`** | gemessen `audit3/n02a`/`n02b` |
| Mehrfachkonformanz | seit 3.0; ein Typ konformiert mehrfach mit verschiedenen Argumenten | gemessen `p05` |
| **Doppelte Konformanz über Deklarationen hinweg** | `struct T :: [I]` + `extend T :: [I]` ist **eine** Konformanz, absichtlich (`lyric-spec/spec/05-interfaces.md:30-36`, Fall `extend_repeating_a_conformance_is_one.lyr`); ein **Body** im zweiten Block wird **still verworfen** | gemessen `audit3/n09`/`n09b`/`n09c` + `lyrc lower`: eine Zeile `impl ty1 :: ty0 [f1]` |
| Generisches Interface als Wert | jede Instanz ist ein eigener Typeintrag | gemessen `q18` |
| Operator-Interfaces | `Add`/…/`Equatable`/`Ordered`/`Into`; Zweideutigkeit → `SEM0083` | gemessen `p38` |
| `extend T :: [I]` | Konformanz außerhalb der Deklaration | `docs/Grammar.md:297-298` |
| **`extend` auf ein Interface** | Sema nimmt es an, Lowering: `LYR-IR0001: interface 'Shape' has no method 'describe'` | gemessen `audit3/n06` |
| Orphan-Regel | `SEM0041`: Ziel **oder** ein Interface im Modul; inhärente `extend` unbeschränkt | `TypeChecker.cs:657-668`; gemessen `orphan/` |
| Objektsicherheit, Regel 1 | generischer Member braucht Body, ist nicht überschreibbar | `TypeChecker.cs:529-538`; gemessen `p11`/`p12`, `audit3/n41` (auch per `extend`: `SEM0082`) |
| Objektsicherheit, Regel 2 (**unbenannt**) | ein Interface **ohne Slot in der ganzen Kette** darf kein Wert sein | gemessen `rv30`/`q02` gegen `rv32`/`q01`/`audit3/n01`; `TypeTable.cs:644`, `:676` |
| Statische Member | `PAR0041` (Parser) | gemessen `p02`; `docs/Grammar.md:294-296` |
| Interface-Overloads | `SEM0088` | gemessen `p33` |
| Interface-Wert | Fat Pointer: Referenz + Index des konkreten Typs; **`mkiface` alloziert nichts** | `docs/Bytecode.md:844-859`; `src/Lyric.Vm/Interpreter.cs:634-639` (*„No allocation and no layout change"*) |
| VM-Dispatch | dichte Matrix `new int[]?[size, size]`, `size` = **alle** Typeinträge | `src/Lyric.Vm/DispatchTable.cs:52-54` |
| `catch` auf Interface | seit 4.6, über `Implements`; auch der **Großelternteil** fängt | gemessen `p24`, `audit3/n21` (`catch (e: Throwable)` fängt `NotFound :: [AppError :: [Throwable]]`) |
| Interface-Wert erfüllt seine **eigene** Constraint | ja | gemessen `p16`/`rv22`; `TypeChecker.cs:3725-3743` |
| Interface-Wert erreicht Eltern-Member | ja | gemessen `rv20` |
| **Constraint-Pfad sieht keine Extension** | `x.extra()` in `fn f<T :: [Greeter]>` ist `SEM0027: type parameter 'T' has no member 'extra' (no constraint provides it)` | gemessen `audit3/n28b` |
| Signaturvergleich | exakt, **außer**: `throws` kontravariant (`p29`/`p29b`) **und Default-Argumente werden nicht verglichen** | gemessen `audit3/n30`: Implementierung ohne den Default des Interface-Members konformiert und läuft |
| `mut` | Teil des Konformanzvertrags | gemessen `p14` |
| `?Interface` | lowert zu `?dyn tyN`, Narrowing greift | gemessen `q06`/`q07` |
| **Überladung mit Interface-Parametern** | exakter Treffer gewinnt (`f(Circle)` vor `f(Shape)`); zwei Interface-Kandidaten für einen Typ, der beide erfüllt: `SEM0086` mit zwei Notes | gemessen `audit3/n03`, `rev3/r02`; Kontrolle `r02b` (statischer Typ entscheidet) |
| **Heterogenes Literal** | `[Circle{}, Square{}]` ohne Kontext: `SEM0009`; mit `Shape[]`-Kontext geht es | gemessen `audit3/n04`, `p39` |
| **Interface-Wert über die FFI-Grenze** | `extern "dotnet" fn take(x: Shape)` → `SEM0099` | gemessen `rev3/r03`; `docs/guide/14-embedding.md:152-155` |
| **Interface-Wert in einer Task** | Box wird geteilt (`s=0 b=1`); die VM ist ein Thread | gemessen `rev3/r05`; `stdlib/std/os.lyr:41-42`, `:55` |
| **Kosten eines Interface-Aufrufs** | im Interpreter **nicht messbar** gegen den direkten Aufruf; im Release-Profil wird er devirtualisiert **und** geinlint | gemessen `rev3/r06` (Zahlen in IF-42) |
| Devirtualisierung | löst über **dieselbe** Impls-Zeile auf | `src/Lyric.Frontend/Ir/Devirtualizer.cs:42-45` |
| `lyrdbg` zeigt den konkreten Typ | `Shape (Circle)` | `src/Lyric.Vm/Debugging/ValueRenderer.cs:75-83` |

**Impls-Zeilen entstehen bedarfsweise**, nicht pro deklarierter Konformanz (gemessen `rv20`/`rv21`,
`bconly/`). `docs/Bytecode.md:364` (*„the same pair twice is an error"*) ist eine Eindeutigkeitsregel,
die der Reader durchsetzt (`BytecodeReader.cs:1124-1126`, `seen.Add((impl.Type, impl.Interface))`);
und weil die Sema **vor** dem Emitter per Instanzliste dedupliziert (`TypeChecker.cs:3918-3922`:
*„the same instance reached twice … is one conformance, not two"*), kann eine doppelte Deklaration
im selben Modul die Reader-Regel nie erreichen. Confidence: **gemessen** + **gelesen**.

### 1.2 Was es nicht gibt (gemessen, nicht vermutet)

| Fehlt | Heutige Antwort | Probe |
|---|---|---|
| `Self`-Typ | `LYR-RES0002` + Folgemeldung mit `<error>` im Text | `p13` |
| statische Interface-Member | `LYR-PAR0041` | `p02` |
| assoziierte Typen | Parse-Fehler, 6 Folgediagnosen | `p37` |
| Felder im Interface | Parse-Fehler, 11 Folgediagnosen | `p15` |
| Downcast `iface as Concrete` | `LYR-SEM0006` mit unbefolgbarem Vorschlag | `p08`/`rv26` |
| Typ-Pattern `c: Circle =>` | `LYR-PAR0002` | `p26` |
| Upcast auf das Eltern-Interface | `LYR-SEM0001` | `p04` |
| `==` auf Interface-Werten | `LYR-SEM0059` — **auch mit `interface Shape :: [Equatable<Shape>]`** | `p09`/`q09`, `audit3/n38` |
| `==` auf optionalen Interface-Werten | `LYR-SEM0059`, Rat führt im Kreis | `q08` |
| Varianz | `LYR-SEM0001` | `p36` |
| bedingte Konformanz | `LYR-SEM0047`, Entwurf liegt vor | `design/conditional-conformance.md` |
| Konformanz-Synthese | `LYR-SEM0020`, Entwurf liegt vor | `design/conformance-synthesis.md` |
| expliziter Super-Aufruf | `LYR-SEM0055` mit unbefolgbarem Vorschlag | `p10` |
| Skalar als Interface-Wert | `LYR-SEM0001` | `p31` |
| slotloses Interface als Wert | `LYR-IR0001` (§0) | `rv30`/`q02` |
| **Elementtyp-Join** für `[Circle{}, Square{}]` | `LYR-SEM0009` | `audit3/n04` |
| unabhängige Kopie aus einem Interface-Wert | keine Form | `q16` |
| privat gehaltene Konformanz | keine Form | `vis/`, `rev3/r07` |
| `extend` auf ein Interface | angenommen, dann `IR0001` | `audit3/n06` |
| Interface über `extern` | `SEM0099` | `rev3/r03` |
| `sealed interface` | existiert nicht | — |
| „implement all members" / „find implementations" | `lyrls` kündigt keines an | `src/Lyric.Lsp/LspServer.cs:1014-1033` |
| Rename über die Konformanzbeziehung | Referenzen per Symbol-Identität; Implementierer sind eigene Symbole | gelesen `src/Lyric.Lsp/Analysis/ReferenceProvider.cs:44-50`, siehe IF-47 |

### 1.3 Fünf Befunde

**(1) Argumentformung passiert vor `call`, aber weder vor `callvirt` noch im monomorphisierten
Aufruf — eine Familie, kein Einzelfall.** Gemessen, sieben Programme:

| Programm | Antwort |
|---|---|
| `g.greet()` über `Greeter`-Wert, Default am Interface-Member (`p30`) | `CLI0020: … CallVirt needs 2 value(s) but the stack holds 1` |
| dasselbe auf dem konkreten Empfänger (`p30b`) | `hi world` |
| `l.log("a","b","c")` über `Log`-Wert, `params xs: string[]` (`audit3/n07`) | `CLI0020: … block at 0 ends with 2 value(s) on the stack, expected 0` |
| Kontrolle: konkret `3`, Interface-Wert mit explizitem Array `2` (`n07b`) | läuft |
| `viaT(C{})` mit `fn viaT<T :: [Greeter]>(x: T) { x.greet() }`, Implementierung **ohne** Default (`rev3/r04`) | **`CLI0020: lowering: call to 'greet' … passes 0 argument(s) but parameter 'who' has no default (in 'main.viaT<C>')`** — dritter Pfad, dritter Absturz |
| dasselbe, Implementierung **wiederholt** den Default (`r04c`) | `hi world` |
| `viaT(g)` mit `g: Greeter` (`r04b`) | `CLI0020` (callvirt) |
| `C{}.greet()` direkt, Implementierung ohne Default (`r04d`) | `SEM0014: call expects 1 argument(s), got 0` — die einzige ehrliche Antwort |

Was das zeigt: die Sema liest den Default am **Interface**-Member (sonst wäre `r04` ein `SEM0014`
wie `r04d`), das Lowering liest ihn an der **Implementierung** (sonst liefe `r04`), und der
`callvirt`-Pfad liest ihn **gar nicht**. Drei Leser, drei Antworten. Confidence: **gemessen**.

**(2) Die vtable-Zeile hält den Default, obwohl §5.4 die Extension verlangt — und der dritte Pfad
antwortet wie die Zeile.**

| Ausdruck | Antwort | Probe |
|---|---|---|
| `C { }.greet()` | `extension` | `p23` |
| `let g: Greeter = C { }; g.greet()` | `default` | `p23` |
| `viaT(C { })` mit `fn viaT<T :: [Greeter]>(x: T)` — **T = C, konkret** | **`default`** | `audit3/n28` |
| `viaT(g)` mit `g: Greeter` | `default` | `audit3/n28` |
| `x.extra()` im generischen Körper, `extra` nur per `extend` | `SEM0027` | `audit3/n28b` |

Gelesen: `lyric-spec/spec/05-interfaces.md:126-130` — *„own member, then visible extension method,
then a default method … The chosen target stands in the compiled module — a vtable row holds
function indices"*; `CHANGELOG.md:39-40`: *„the resolution order the specification states now holds
at run time too"*. Gemessen (`lyrc lower p23`): `impl ty0 :: ty1 [f2, f1]`, `f1 = main.Greeter.greet`.
Die Zeile hält den Default. Die 4.6-Änderung hat also den syntaktisch konkreten Pfad repariert und
die Zeile nicht angefasst; und der monomorphisierte Pfad liest gar keine Zeile, sondern das
Constraint-Memberset, in dem Extensions nicht vorkommen (`n28b`). Kontrollen wie gehabt (`p23b`–`p23d`,
`rv01d`/`rv01r`, `shadow/` über drei Module). Confidence: **gemessen** + **gelesen**.

**(3) Ein Struct wird beim Übergang zum Interface-Wert kopiert und danach geteilt — benannt im
Lowering, unbenannt in der Spec, verboten im Format.** Gemessen (`p27`, `p41`, `rv02`, `q16`): `let b:
Bump = s; b.bump()` lässt `s.n` bei `0`; Interface→Interface teilt; `dup(a)` mit `a: Bump` teilt,
`dup(s)` mit `s: S` kopiert. Gelesen, **korrigiert gegenüber der zweiten Fassung**:
`src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3041-3046` — *„The way behind an interface is a
binding point too: a struct is copied there, or the interface value would share the slot array with
its source and a mutation through the interface would hit the original."* Das ist eine bewusste,
begründete Lowering-Entscheidung. Die „Box" ist das `structcopy`, das diese Regel emittiert;
`mkiface` selbst alloziert nichts (`Interpreter.cs:634-639`). Was fehlt, ist allein der Spec-Satz —
und `docs/Bytecode.md:356`/`:364-365` (*„A type that is neither class nor enum must not appear on the
left"*) sagt das Gegenteil dessen, was `lyrc` schreibt (`rv02`: `impl ty0 :: ty1` mit `struct ty0 S`),
ohne dass `ValidateImpls` (`BytecodeReader.cs:1102-1140`) es prüft — dort wird nur ein **Interface**
links verboten (`:1118-1122`). Confidence: **gemessen** + **gelesen**.

**(4) Eine Constraint wird für nicht-nominale Typargumente nicht geprüft — und das kompiliert
heute auch Programme, die laufen.** `Satisfies` (`TypeChecker.cs:3702-3722`) endet mit `_ => true`.
Gemessen (`rv29`, `q03`–`q05`): `println([1,2,3])`, `println((1,2))`, `println(opt)`, `println(f)`
scheitern als `IR0001` **in** `console.lyr:51`. Kontrolle `rv31` (nominal): `SEM0028` an der
Aufrufstelle. **Neu, `audit3/n50`**: `fn id<T :: [Display]>(x: T): T { return x; }` mit
`id([1, 2, 3])` **kompiliert und druckt 3** — der Body ruft kein Constraint-Member, also fällt nichts
ins `IR0001`. Die Prüfung nachzuholen bricht also nicht nur Programme, die heute schon scheitern.
Confidence: **gemessen** + **gelesen**.

**(5) Die Konformanzprüfung lässt zwei Defaults still auseinanderlaufen.** `interface I1 { fn
greet() { "i1" } } interface I2 { fn greet() { "i2" } } class C :: [I1, I2] { }` wird **angenommen**;
`C{}.greet()` ist `SEM0043` (`TypeChecker.cs:3910`, `DefaultMember`, eine Benutzungsdiagnose), aber
`let a: I1 = C{}` antwortet `i1` und `let b: I2 = C{}` antwortet `i2` (gemessen `audit3/n02a`/`n02b`).
`lyric-spec/spec/05-interfaces.md:127-129` spricht von einer *„ambiguity error … asking for an
explicit override"* — im Absatz über den **konkreten** Typ; die Wertpfade sind ungeregelt. Ein Name,
zwei Funktionen, gewählt vom statischen Typ — **ohne Extension**. Confidence: **gemessen** + **gelesen**.

### 1.4 Drei bestätigte alte Befunde

- **Eine Elternliste schluckt eine zweite Instanz still** (`p03` gegen `p05`; `Conformance.cs:38`, `:58`;
  `STATUS.md:2309`).
- **`extend int[] :: [I]` kompiliert und tut nichts** (`p06`; `TypeChecker.cs:633-637`; `STATUS.md:2249`).
  Neu daneben: **`extend Shape { … }` auf ein Interface** kompiliert in der Sema und fällt im
  Lowering (`audit3/n06`) — dieselbe Still-dann-`IR0001`-Form, siehe IF-35.
- **`mut` wird auf Klassen nicht erzwungen, seit PR #172 läuft eine Uhr** (`SEM0108`, `p14`).

### 1.5 Fünf Kleinigkeiten, die ein Muster ergeben

1. **Drei Diagnosen schlagen etwas vor, das die Sprache nicht hergibt** (`SEM0055`→`static`;
   `SEM0006`→`Into<Circle>` auf einem Interface; `SEM0059` auf `?Shape`→„narrow it first").
2. **`pub` parst an drei Stellen und bedeutet nichts**: am Interface-Member (`p28`), am
   `extend`-Block (`vis/b`), **und am Interface-Member eines nicht exportierten Interfaces**
   (`rev3/r07`: `interface Hidden { pub fn h(): int; }` ohne `pub` am Interface — der Konsument kann
   `T{}.h()` rufen und `T` durch `fn viaHidden<X :: [Hidden]>` schicken, aber `Hidden` nicht
   importieren: `RES0004: 'Hidden' is not public in 'lib'`). Siehe IF-44.
3. **Drei Memberlisten, zwei Recovery-Verträge** (`q12`–`q14`, `p15`, `rv34`).
4. **`docs/Bytecode.md:891` ist veraltet** (Gleichheit statt Subtyptest; gemessen `p24`, `n21`).
5. **`lyrfmt` bricht `::`-Listen nie um** (`AstFormatter.cs:408-413`; `q15`: 122 Zeichen).

### 1.6 Der Mechanismus, den dieses Dossier benutzen muss

`docs/guide/19-diagnostics.md:53-78`: **Migrationswarnung** (`SEM0107`–`SEM0110`), Aufnahmekriterium
`:66-68` — feuert auf ein Programm, dessen Bedeutung sich **bei jeder** möglichen Antwort ändert —,
Rücknahmeregel `:77-78`. Jede 4.x-Warnstufe unten ist als **Migrationsuhr** oder **Defektwarnung**
markiert. Neu in dieser Fassung: eine dritte Kategorie ist nötig, die **Bruchwarnung** — die Antwort
steht fest, aber das Programm ist heute legal und läuft (Beispiel `n50`); sie ist keine Uhr (nimmt
eine Antwort vorweg) und keine Defektwarnung (nichts ist heute falsch). Ob sie eine eigene Familie
bekommt, ist eine Frage des Diagnostik-Gebiets; hier wird sie nur benannt.

### 1.7 Was davon Spec ist und was Implementierung

Die zweite Fassung nannte das Spec-Repo nirgends. Gelesen: `lyric-spec` ist normativ, Modus
spec-first (Regel-PR mit `since:`-Gate zuerst, dann der Compiler, dann Release und Pin);
`conformance/README.md:21` beschreibt das Gate; `STATUS.md:215` (*„the spec pin moves to 4.5 after
this release, last, as it always does"*). Für dieses Gebiet heißt das:

| Klasse | Fragen | Was zuerst wandert |
|---|---|---|
| **Spec-Satz ändert sich** | IF-01 (§5.3:112-115 *„does not convert"* + Fall `no_value_upcast.lyr`), IF-02 (§5.3:114 *„implication holds for the implementing type, not for fat pointers"*), IF-03 (§5.3 + `Bytecode.md:854`), IF-04/05/06/09/10/17/18/19/20, IF-13 (§5.4:126-130 — **je nach Antwort**), IF-14 (§5.3 + `Bytecode.md:356/364`), IF-33 (§5.1:30-36), IF-34 (§5.4:127-129), IF-40 | Regel-PR in `lyric-spec` mit `since: 4.7.0`/`5.0.0`, Zwilling in `conformance/cases/05-interfaces/`, `no_value_upcast.lyr` **retiriert** (nicht umgedeutet) |
| **Implementierung erfüllt die Spec nicht** | Befund 2 (§5.4 sagt Extension, Zeile hält Default), IF-36 (Argumentformung), IF-22 (Constraint-Prüfung), IF-10, IF-11 A, IF-35 | Bugfix; die Spec hat den Satz schon. Für Befund 2 braucht die Suite **erst einen Fall** — heute pinnt sie die 4.6-Regel nicht |
| **reine Implementierung** | IF-28, IF-29, IF-30, IF-32, IF-41, IF-42, IF-47 | keine Spec-Bewegung |

Reihenfolge, wenn IF-02 als 4.7-Bugfix eine Regel ändert, die §5.3 heute als Absicht formuliert:
**erst** der Spec-PR (§5.3:114 wird zu „implication holds for fat pointers too"), mit einem
`since: 4.7.0`-Fall, **dann** der Compiler, **dann** Release, und der Pin wandert zuletzt. Ein
Bugfix, der einer normativen Absicht widerspricht, ist kein Bugfix, sondern eine Regeländerung mit
Bugfix-Größe — das ist die eine Stelle, an der die zweite Fassung „Bugfix" zu leichtfertig sagte.
Confidence: **gelesen**.

---

## 2. Sprachvergleich

### 2.1 Übersicht

Korrigiert gegenüber der zweiten Fassung in den Zeilen **Swift** (Objektsicherheit) und **C#**
(`static abstract`).

| Sprache | Konformanz | Nachrüstbar | Defaults | `Self`/assoz. Typen | Objektsicherheit | Downcast | Preis |
|---|---|---|---|---|---|---|---|
| **Rust** | `impl Trait for T` | ja, Kohärenz/Orphan | ja | beides | dyn-Kompatibilität: keine generischen Methoden, `Self` nur als Empfänger, statische per `where Self: Sized` ausgenommen; **`dyn Trait<T>` war immer erlaubt** | `Any` + `TypeId` | zwei Welten; Upcasting brauchte eine vtable-Layouterweiterung (1.86) |
| **Swift** | `extension T: P` | ja (`@retroactive` seit 6) | ja, Protocol-Extensions | beides, `some`/`any` | **SE-0309 (5.7)**: jedes Protokoll ist `any P`-fähig; ein Member ist am Existential erreichbar, **sofern `Self`/assoz. Typen nur in kovarianter Position stehen** (Rückgabe, Tupel, `Optional`, `Array`) — dort auf die obere Schranke gelöscht; blockiert nur nicht-kovariante Vorkommen (Parameter). Statische Requirements: über `type(of: x)` am Metatyp (gelesen, außerhalb des Proposal-Texts — **behauptet**) | `as?`, `case let c as T` | die Falle: Extension-Member statisch, Requirement dynamisch |
| **Go** | strukturell | Paket des Typs | nein | nein | keine Typparameter auf Methoden | `v.(T)`, Typ-Switch | itab-Suche am dynamischen Typ |
| **Haskell** | `instance` | ja, Orphan-Warnung | ja | Typfamilien | Klassen sind keine Typen | `Data.Typeable` | globale Kohärenz |
| **C#** | `class C : I` | nein | ja seit C# 8, nicht im Memberset der Klasse | `static abstract` seit C# 11 | **korrigiert**: ein Interface mit `static abstract` ohne most-specific-Implementierung darf **nicht als Typargument** stehen (CS8920: *„cannot be used as a type argument"*, Proposal `static-abstracts-in-interfaces.md`, gelesen); **als Typ einer Variablen oder eines Parameters bleibt es erlaubt**, der statische Member ist dort unerreichbar — das ist IF-05 **Option B**, nicht B' | `is`/`as` | Vererbung *und* Interfaces; DIM = Lyrics Befund 2 als Designentscheidung |
| **Kotlin** | `class C : I` | nein | ja | nein | keine | `is`/`as` | Kollision zweier Supertypen **muss an der Deklaration** überschrieben werden; Member gewinnt gegen Extension (Warnung) |
| **Scala 3** | `trait` + `given` | ja | ja, Linearisierung | Typmember ja, `Self` nein | keine | Patterns | zwei Mechanismen |
| **Julia** | keine | — | — | — | — | Laufzeittypen | siehe 2.2 |

### 2.2 Die zwei Gegenentscheidungen

**Go** (strukturell, itab-Suche) und **Julia** (Multi-Dispatch, Wert trägt Typ) — unverändert aus
der zweiten Fassung; für Lyric beide der falsche Tausch (`guide/07:192-196`; `STATUS.md:2329`).

### 2.3 Fallen, die andere schon getreten haben — und die zwei, die Lyric nachbaut

- **C#: DIM + Extension-Methode.** Existiert nur der Default-Interface-Member und eine passende
  Extension-Methode, kompilieren `c.M()` (Extension) und `((I)c).M()` (DIM) beide und antworten
  verschieden. Das ist Lyrics Befund 2, Zeichen für Zeichen.
- **Die zweite nachgebaute Falle, korrigiert: es ist nicht die einzige.** Lyric lässt eine Klasse
  zu zwei Interfaces mit gleichnamigen Defaults konformieren und wählt dann am Interface-Wert per
  statischem Typ (`audit3/n02b`). **Kotlin** verlangt den Override an der **Deklaration** (`class C :
  A, B` mit zwei `foo`-Defaults ist ohne `override fun foo()` ein Fehler); **C#** löst es mit
  expliziter Interface-Implementierung, die Wahl steht dann im Code. Lyrics `SEM0043` ist eine
  Benutzungsdiagnose; die Deklaration geht durch. Diese Falle steckt im Konformanzrecht, nicht in
  `extend` — und IF-13 B repariert sie **nicht** mit.
- **Swift**: zwei Dispatch-Regeln (Extension statisch, Requirement dynamisch).
- **Kotlin**: Member gewinnt gegen Extension, Extension wird still verschattet — mit **Warnung**,
  nicht Fehler, und zwar aus dem Grund, den IF-29 misst: die Kollision entsteht dort, wo keiner der
  beiden Autoren steht.
- **Rusts `#[derive]`** setzt Bounds implizit; `design/conformance-synthesis.md:72-75` lehnt das ab.
- **Javas kovariante Arrays**: Lyric refused (`p36`) und soll dabei bleiben.
- **Scala 3**: Traits *und* Typklassen — Rule 2 mit Namen.
- **Kotlin**: eine überschreibende Funktion darf Default-Argumente nicht wiederholen, sie erbt sie.
  C#: der statische Typ entscheidet. Lyric: keine Regel, dafür drei Leser (Befund 1).

---

## 3. Designfragen

Zwanzig aus der ersten Fassung, zwölf aus der ersten Kritik (IF-21–IF-32), sechzehn aus der zweiten
(IF-33–IF-48).

### IF-01 — Konvertiert ein Interface-Wert auf sein Eltern-Interface?

**Heute:** Nein. `let n: Named = l;` ist `LYR-SEM0001` (gemessen `p04`); der Wert antwortet aber auf
`name()` (`rv20`). Normativ: `lyric-spec/spec/05-interfaces.md:112-115` und der Fall
`conformance/cases/05-interfaces/no_value_upcast.lyr` (`//! error: LYR-SEM0001`).

Was trägt: das Chain-Prefix-Slotlayout (`q17`) und die vorhandene O(1)-Matrix
(`DispatchTable.cs:5-15`, `:31-43`). **Die Kostenrechnung ist in dieser Fassung ein drittes Mal
korrigiert**, jetzt mit Zahlen:

- **Die Matrix ist quadratisch und zählt alle Typeinträge, nicht nur Interfaces.**
  `DispatchTable.cs:52-54`: `var size = module.Types.Count; var rows = new int[]?[size, size];`.
  Gemessen (`lyrvm info`): Hello-World 48 Typen → 2 304 Zellen; `examples/bank.lyr` 50 → 2 500;
  `shapes` 54; `inventory` 56. Bei 8 Bytes pro Referenzzelle sind das ~18–25 KB pro geladenem
  Modul heute; **bei 2 000 Typeinträgen 32 MB** (Extrapolation, **behauptet**). „Die Matrix wächst
  nicht" war falsch: sie wächst mit jedem Typeintrag quadratisch, siehe IF-41.
- **Jeder materialisierte Vorfahr wird ein Typeintrag mit voller Slotliste.** Gemessen
  `rev3/r08a`/`r08b`: `class K :: [C]`, `C :: [B]`, `B :: [A]`; nur `let c: C` → **39** Typen; plus
  `let b: B; let a: A` → **41**, und `lyrc lower` zeigt `interface ty2 B`, `interface ty3 A` mit den
  Zeilen `impl ty1 :: ty2 [f1, f2]`, `impl ty1 :: ty3 [f1]`. Unter B kostet jeder Vorfahr also
  **einen Typeintrag und eine Zeile**, nicht nur eine Zeile — siehe IF-48.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt verboten | heute, normativ | `Labeled[]` ist kein `Named[]`; jede Elternschnittstelle braucht den konkreten Typ zurück |
| B: impliziter Upcast | Rust 1.86, Swift, C# | Vollständigkeitsregel im Emitter und Format (wo (T, Kind), auch (T, Vorfahr)); ein Opcode über die Matrix; **pro Vorfahr ein Typeintrag plus eine Zeile** (r08); Spec-PR gegen §5.3:112-115, `no_value_upcast.lyr` retiriert |
| C: expliziter Upcast (`l as Named`) | — | Drittes `as` |

**Empfehlung: B, mit der ehrlichen Rechnung aus r08 und IF-41.** Der Grund gegen den Upcast war
nie ein Laufzeitgrund; der Preis ist Modulgröße, und er ist gemessen klein, solange Ketten kurz
sind.
**Bricht: nein** (additiv). **Hängt ab von:** Bytecode/VM-Gebiet, IF-21, IF-41, IF-48, §1.7
(Spec zuerst).

### IF-02 — Erfüllt ein Interface-Wert eine Constraint auf sein Eltern-Interface?

**Heute:** Nein. `fn describe<T :: [Named]>(x: T)` nimmt `Tag` (`p32b`) und einen `Named`-Wert
(`rv22`), aber keinen `Labeled`-Wert: `SEM0028` (`p32`). **Neu gemessen (`audit3/n05`)**: mit
`interface Shape :: [Display]` ist `s.show()` auf dem `Shape`-Wert erlaubt, `println(s)` ist
`SEM0028: type 'Shape' does not satisfy constraint 'Display'`. Ursache: `DeclaredInterfaceNodes`
(`TypeChecker.cs:3933-3939`) hat keinen `InterfaceDecl`-Fall; Kommentar `:3946-3948` nennt es
Absicht — und die Absicht steht **auch in der Spec**: `05-interfaces.md:114` *„implication holds for
the implementing type, not for fat pointers"*. Der Grund im Kommentar (*„nothing above it"*) ist
gemessen falsch (`rv20`, `q17`: Chain-Prefix).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute, normativ | Regel für konkrete Typen, nicht für Werte; `Equatable<Shape>` als Elternteil liefert nie `==` (IF-16) |
| B: Implikation gilt auch für Interface-Werte | Haskell, Rust, Swift | Ein `InterfaceDecl`-Fall; Kommentar zurück; **Spec-PR gegen §5.3:114 mit `since: 4.7.0`-Fall** |

**Empfehlung: B, in 4.7 — korrigiert: nicht als Bugfix, sondern als Regeländerung in
Bugfix-Größe** (§1.7). Der normative Satz existiert; er fällt, weil sein Grund gemessen nicht
trägt.
**Bricht: nein.** **Hängt ab von:** §1.7 (Spec-Pfad zuerst). **IF-16 folgt daraus.**

### IF-03 — Kann man aus einem Interface-Wert wieder herauskommen?

Unverändert: **B** (Typ-Pattern in `match` + `if let`), kein `as?`. `Bytecode.md:854` retiriert.
**Bricht: nein.** **Hängt ab von:** Pattern-Gebiet, Bytecode-Gebiet, IF-24, IF-38 (ohne Join-Typ
entsteht die heterogene Menge nur mit Annotation).

### IF-04 — Braucht v5 einen `Self`-Typ?

**Heute:** Nein (`p13`). Ersatz: Generik-Idiom `struct Point :: [Equatable<Point>]`.

**Korrigiert nach der Kritik:** Option B („`Self` nur in Rückgabeposition") wurde als „halb"
verworfen — dabei ist genau diese Hälfte das, was **Swift SE-0309** am Wert erreichbar macht:
kovariantes `Self` wird auf die obere Schranke gelöscht, nur nicht-kovariante Vorkommen sind
blockiert. Option Cs Regel war **strenger** als das zitierte Vorbild.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt beim Generik-Idiom | heute, Haskell, Scala 3 (F-Bounded) | `Clone`, `Default`, `Parse`, `Zero` nicht ausdrückbar; Tippfehler `S :: [Equatable<T2>]` ist eine gültige, falsche Konformanz |
| B: `Self` nur kovariant (Rückgabe) | **Swift SE-0309**, die Hälfte, die am Wert bleibt | `Clone`/`Default` gehen; `Equatable` (Parameterposition) nicht |
| C: volles `Self`, Objektsicherheitsregel **nach SE-0309**: ein Member mit `Self` in **nicht-kovarianter** Position ist am Wert nicht erreichbar, kovariantes `Self` wird zum Interface-Typ gelöscht | Swift 5.7, Rust | Dritte Objektsicherheitsregel, aber die **Swift-genaue**, nicht die strengere aus der zweiten Fassung |
| D: C + `Equatable`/`Ordered`/`Hashable` auf `Self` | Swift | Bricht jede Konformanz |

**Empfehlung: C in der SE-0309-Fassung, ohne D in derselben Release.** D **muss** in 5.0 mit
(`STATUS.md:2496`), nur wenn `Self` in 4.7 existiert.
**Bricht:** C nein, D **major**. **4.x-Warnstufe für D:** Defektwarnung ab 4.8. **Hängt ab von:**
IF-05, Generics-Gebiet, §1.7.

### IF-05 — Statische Interface-Member?

**Heute:** `PAR0041` (`p02`), `docs/Grammar.md:294-296`.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt verboten | heute | kein `FromJson`, `parse<T>`, `Default` (`PLAN.md:266-269`) |
| B: erlaubt; Interface bleibt Wert, statischer Member dort unerreichbar | **Swift 5.7 und C# 11** — korrigiert: C# verbietet nur das **Typargument** (CS8920), nicht den Typ | Objektsicherheitsregel aus IF-04 deckt es |
| B': constraint-only | Rust ohne `where Self: Sized` (**nicht** C#) | ein Member streicht das ganze Interface aus der Wertwelt |
| C: über den Wert aufrufbar | — | Typ-Deskriptor → verletzt „kein Typ-Tag" |

**Empfehlung: B — und die Vorbildzeile ist korrigiert.** Die zweite Fassung behauptete „C# hat B'
gewählt" und nannte C# 11 in §4 als „Gegenprobe". Gelesen (Proposal): das Verbot gilt nur für den
Typ**argument**; als Variablen-/Parametertyp bleibt das Interface erlaubt. Swift **und** C# stützen B.
`PLAN.md:266-269` hat es bereits entschieden.
**Bricht: nein.** **Hängt ab von:** IF-04.

### IF-06 — Assoziierte Typen?

Unverändert: **A** (bleibt bei Typparametern), mit IF-26 D als stärkstem Gegenargument in der
Preisspalte. **Bricht: nein.** **Hängt ab von:** stdlib, IF-26.

### IF-07 — Bedingte Konformanz und generische `extend`-Ziele

**Heute:** `SEM0047`. Entwurf: `design/conditional-conformance.md`.

**Korrigiert:** die Vorbildzelle für B lautete `impl<T: Display> Display for Vec<T>` — dieses impl
existiert in Rusts std **nicht** und ist für Nutzer wegen der Orphan-Regel **unmöglich** (`Display`
und `Vec` sind beide fremd); es ist das kanonische Gegenbeispiel. Der reale Beleg für die Form ist
`impl<T: Debug> Debug for Vec<T>` in std selbst. Derselbe Fehler steht in
`design/conditional-conformance.md:116` (gelesen) und gehört dort korrigiert.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | kein `Display` auf `List<T>` |
| B: `extend<T :: [Display]> List<T> :: [Display]` | Rust `impl<T: Debug> Debug for Vec<T>` | Parameter vor dem Ziel, Linearität, Überlappungsverbot |
| C: B + Array-/Tupel-Ziele | Swift `extension Array where …` | löscht den `for-in`-Sonderfall (`iter.lyr:6-8`) |
| D: `where` hinter dem Ziel | Swift | neues Wort |

**Empfehlung: B jetzt, C danach.** **Bricht: nein / minor.** **Hängt ab von:** Generics, IF-22, IF-35
(ein Interface als `extend`-Ziel ist die Blanket-Form desselben Mechanismus).

### IF-08 — Konformanz-Synthese

Unverändert: **A** (Konformanz ohne Body = Synthese, Swift). **Hängt ab von:** `?T == ?T`, IF-04,
IF-19 (Marker überspringen — Regel jetzt „slotlose Kette").

### IF-09 — Darf ein Interface seine eigenen Member überladen?

Unverändert: **B** (Slots per Name+Signatur). **Hängt ab von:** IF-10, IF-32.

### IF-10 — Trägt die Elternliste Interface-Instanzen?

Unverändert: **A** (Closure über Instanzen; heutiges Verhalten ist still falsch). **Hängt ab von:** IF-26.

### IF-11 — Welche Ziele darf `extend` haben?

**Heute:** einfache benannte Typen. Array- und opaque-Ziel still (`p06`), **Interface-Ziel still bis
zum Lowering** (`audit3/n06`, siehe IF-35).

**Empfehlung: C ohne D, A sofort**, zusammen mit IF-22 A und **IF-35** — die Regel *jedes Ziel, das
die Spec nicht trägt, ist ein Fehler — keins ist ein Nichts* gilt für Interface-Ziele mit.
**Bricht: minor.** **4.x:** Defektwarnung. **Hängt ab von:** IF-07, IF-22, IF-35.

### IF-12 — Bleibt die Orphan-Regel?

Unverändert: **C** (A + Ausnahme für das Wurzelmodul), vor dem Paketmanager. Präzisiert: Kohärenz ist
**nicht nur** eine Frage zwischen Modulen — im selben Modul ist die doppelte Konformanz per Spec
eine (IF-33). **Hängt ab von:** Modul-Gebiet, IF-21, IF-27, IF-33.

### IF-13 — Eine Auflösungsregel für **drei** Empfängerpfade

**Heute:** drei Antworten (Befund 2): konkret `extension`, Wert `default`, monomorphisiert `default`
(`p23`, `audit3/n28`). Die Spec (§5.4:126-130) verlangt, dass die vtable-Zeile das Ergebnis der
konkreten Auflösung trägt — sie tut es nicht (`lyrc lower p23`: `[f2, f1]`, `f1` = Default). Der
monomorphisierte Pfad liest keine Zeile, sondern das Constraint-Memberset ohne Extensions
(`n28b`, `SEM0027`).

| Option | Vorbild | Preis |
|---|---|---|
| A: die Zeile nimmt, was der konkrete Empfänger nimmt (**= heutige Spec §5.4**) | die Spec selbst; keine der acht Sprachen | Ein Programm, das sich auf den Default über den Wert verlässt, ändert **still** sein Verhalten; und der monomorphisierte Pfad muss dann ebenfalls Extensions sehen (`SEM0027` fällt) — sonst bleiben zwei Antworten |
| B: die Zeile gewinnt überall; eine Extension darf einen Default **nicht** verschatten — **Fehler** | Kotlin (Member gewinnt), verschärft | Fehler nur im Konsumentenbuild, den keiner der beiden Autoren sieht (`shadow/`, IF-29); Spec-PR gegen §5.4 |
| B': wie B, aber Verschattung ist eine **Warnung** mit zwei Fundstellen, kein Fehler | **Kotlin** wörtlich (*„extension is shadowed by a member"*) | Der konkrete Pfad wechselt seinen Wert (`extension`→`default`) — nach einer Uhr, nicht still; die 4.6-Regel fällt |
| C: bleibt, Spec schreibt drei Regeln auf | Swift, C# | drei Regeln für einen Punkt |

**Empfehlung: B', nicht mehr B — mit Begründung, die die zweite Fassung nicht abgewogen hat.** B
macht aus dem orphan-legalen Drei-Modul-Fall einen Build-Fehler, der nur beim Konsumenten
erscheint; Kotlin hat für genau diesen Fall die Warnung gewählt. B' gibt allen drei Pfaden dieselbe
Antwort (die Zeile, und der monomorphisierte Pfad liest ohnehin nur Constraint-Member), warnt dort,
wo der Konflikt sichtbar wird, mit beiden Fundstellen (IF-29 B), und schreibt niemandem in fremde
Module. Der Preis ist ehrlich benannt: der syntaktisch konkrete Pfad wechselt nach der Uhr von
`extension` auf `default` — das ist die Rücknahme der 4.6-Regel, und sie ist eine **Spec-Änderung**
(§5.4:126-130 sagt heute A). A bleibt die Antwort ohne Vorbild, und sie verlangt zusätzlich, dass
Extensions im generischen Körper sichtbar werden — eine Erweiterung der Constraint-Semantik, die
niemand bestellt hat. Ausdrücklich: **B/B' repariert Befund 5 nicht mit** (zwei Defaults ohne
Extension), das ist IF-34.
**Bricht:** B' **minor** (Uhr + Warnung; der Wert wechselt nur in Programmen, die die Warnung
überstanden haben).
**4.x-Warnstufe:** ab 4.7 Warnung an der Aufrufstelle im Konsumentenbuild mit zwei Notes.
**Migrationsuhr** (unter A/B'/C ändert sich Bedeutung bzw. Kompilierbarkeit verschieden).
**Hängt ab von:** Bytecode/VM-Gebiet, IF-29, IF-30, IF-34, IF-37, §1.7 (Spec-PR gegen §5.4). **Vorher,
unabhängig von der Antwort:** ein Konformanzfall für die 4.6-Regel — die Suite pinnt sie heute nicht.

### IF-14 — Was ist ein Struct hinter einem Fat Pointer?

**Heute:** eine **Kopie beim Übergang, geteilt danach** — im Lowering entschieden und begründet
(`FunctionLowerer.cs:3041-3046`), in der Spec ungenannt, vom Format verboten
(`Bytecode.md:356`/`:364-365`) und vom Reader nicht geprüft (`BytecodeReader.cs:1118-1122`).
**Korrigiert:** die zweite Fassung nannte das „unbenannt" und „geboxt"; beides trägt nicht —
`mkiface` alloziert nichts (`Interpreter.cs:634-639`), die „Box" ist das `structcopy` der
Bindungsregel, und die Regel ist im Quelltext benannt. Die Frage ist rein die Spec-Lücke.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt, und die Spec schreibt es hin: *ein Struct hinter einem Interface-Wert ist eine Bindungsstelle; er wird dort kopiert und danach von allen Interface-Werten geteilt* | Swift (`any P` kopiert Werte in die Existential-Box) | `Bytecode.md:356`, `:364-365`, `:845`, `:856` umschreiben; Reader prüft danach, was gilt; `mut fn` im Interface erreicht das Original nie |
| B: Struct mit `mut`-Member darf kein Interface-Wert sein | — | streicht Programme, die heute laufen; **braucht ein eigenes Argument, das die zweite Fassung nicht lieferte** |
| B': kein Struct als Interface-Wert | Format wörtlich | streicht mehr; kein Spec-Umbau |
| C: Interface→Interface kopiert, wenn Struct | Swift (Wertsemantik in Existentials) | Laufzeitzweig nach Typ in jeder Zuweisung |

**Empfehlung: A allein — korrigiert von „B + A".** A ist der billigere und konsistentere Weg,
weil der Mechanismus entschieden ist; B hat kein Argument über „Iteratoren sind Klassen" hinaus,
und das ist eine Gewohnheit. Pflicht bei A: entscheiden, ob `Bytecode.md:364-365` fällt oder der
Reader es durchsetzt — beides zugleich darf nicht bleiben. C kollidiert mit `SPEC-RUNDE.md:57`
(`?Struct` teilt ebenfalls); beide gehören in einen Satz.
**Bricht: nein** (A beschreibt, was läuft). **4.x-Warnstufe:** keine hier; die Uhr fängt bei IF-23 an.
**Hängt ab von:** `SPEC-RUNDE.md:57`, `:82`, IF-23, IF-24, IF-45 (Tasks teilen dieselbe Box).

### IF-15 — Default-Argumente auf Interface-Membern: welche Politik?

**Heute:** keine Regel, drei Leser (Befund 1). Der Default wird beim Signaturvergleich **nicht**
verglichen (`audit3/n30`); die Sema liest ihn am Interface-Member (`r04` ist kein `SEM0014`), das
Lowering an der Implementierung (`r04` stürzt, `r04c` läuft), `callvirt` gar nicht (`p30`, `r04e`).

| Option | Vorbild | Preis |
|---|---|---|
| A: Default lebt am Interface-Member, Implementierung darf ihn nicht wiederholen; alle drei Pfade lesen ihn dort | **Kotlin** | Eine Prüfung; stdlib durchsehen; **IF-36** muss dann die Formung an der Aufrufstelle für alle Pfade liefern |
| B: der statische Typ entscheidet | C# | dieselbe Falle wie IF-13, in klein |
| C: keine Default-Argumente auf Interface-Membern | Rust | Sonderfall im Parameter-Recht |

**Empfehlung: A**, und IF-36 als Voraussetzung. **Bricht: minor** (wiederholte Defaults refused).
**4.x:** Defektwarnung an der wiederholten Angabe. **Hängt ab von:** IF-36, IF-40, Funktions-Gebiet
(benannte Argumente, `lyric-v5-features.md:39`).

### IF-16 — Interface-Werte vergleichen

**Heute:** `a == b` auf Interface-Werten ist `SEM0059` (`q09`) — **auch dann, wenn das Interface
`Equatable<Shape>` als Elternteil trägt** (`audit3/n38`). Kontrolle `rev3/r01` (ohne `==`):
`a.equals(b)` auf zwei `Shape`-Werten läuft über den Chain-Prefix und druckt `true`. Ursache gelesen:
die Operatorsuche liest Konformanzen über `DeclaredInterfaceNodes` (`TypeChecker.cs:3933-3939`),
das keinen `InterfaceDecl`-Fall hat — **exakt IF-02**.

**Korrigiert:** die zweite Fassung machte B von IF-04 (`Self`) abhängig und nannte Rust/Swift als
Vorbild. Beides falsch: das Idiom ist heute schreibbar und scheitert am IF-02-Loch; und in Rust
macht `trait Shape: PartialEq` (`Self` in Argumentposition) `dyn Shape` **nicht** dyn-kompatibel
(E0038), in Swift verliert `any Shape` bei `protocol Shape: Equatable` das `==` — beide liefern
Gleichheit nur in **generischem** Code, was Lyric mit `T :: [Equatable<T>]` schon hat. Lyric kann
hier **mehr** als die Vorbilder, weil `Equatable<Shape>` mit dem Interface als Argument keinen
`Self`-Bezug hat und objektsicher ist (`r01` zeigt es).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | heterogene Mengen nur über `enum` |
| B: `interface Shape :: [Equatable<Shape>]` liefert `==` — **folgt aus IF-02 B** | keines der Vorbilder kann das auf Werten | nichts über IF-02 hinaus |
| C: `===` | C#, Kotlin | zweiter Gleichheitsbegriff → Rule 2 |

**Empfehlung: B als Folge von IF-02, ohne `Self`.** Keine eigene v5-Entscheidung mehr.
**Bricht: nein.** **Hängt ab von:** IF-02, IF-25.

### IF-17 — `sealed interface`?

Unverändert: **A + C** (`enum` ist die geschlossene Menge, `@NonExhaustive` das Gegenstück).
**Hängt ab von:** Enum-Gebiet, IF-28.

### IF-18 — Varianz

Unverändert: **A für v5**, B benannt und zurückgestellt. **Hängt ab von:** Generics, IF-26, IF-38.

### IF-19 — Sind Marker-Interfaces ein eigener Mechanismus?

**Heute — korrigiert:** die Regel, wie die zweite Fassung sie in die Spec schreiben wollte
(„memberlos"), ist gemessen falsch: `interface Sub :: [Base] { }` ist memberlos, als Wert legal und
läuft (`audit3/n01`). Das Kriterium ist die **leere Slotliste der ganzen Kette** (`TypeTable.cs:676`
läuft erst über die Eltern, `:644` wirft bei leerer Gesamtliste). Die „yet"-Note ist die generische
Lowering-Kategorie (`LoweringDiagnostics.cs:28`), kein Versprechen an dieses Interface.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt Implementierungslücke | heute, wörtlich | ein `IR0001` mit generischem „yet" für etwas, das niemand bauen will |
| B: Regel in der Spec, **richtig formuliert**: *ein Interface, dessen Kette keinen Slot hat, ist kein Wert*; eigener `SEM`-Code | Rust (`Send`/`Sync` mit Namen), Java (`Serializable` als Negativlektion) | Spec-Satz, Code; Synthese (IF-08) überspringt slotlose Ketten |
| C: `marker interface` | — | neues Wort |

**Empfehlung: B in der korrigierten Formulierung.** **Bricht: nein.** **Hängt ab von:** Attribut-,
Diagnostik-Gebiet, IF-08.

### IF-20 — Was dürfen Interface-Member außer Methoden sein?

Unverändert: **A** (nur Methoden), Recovery über IF-32. **Hängt ab von:** IF-05, IF-32.

### IF-21 — Wer emittiert die Impls-Zeile, wenn Konformanz und Wertgebrauch in verschiedenen Modulen liegen?

**Heute:** keine vorkompilierte Bibliothek (`16-building.md:88`); `bconly/`: ein `.lyrbc` lässt sich
bauen, aber nicht importieren (`RES0003`); das eigenständig übersetzte `shapes.lyrbc` enthält nur
`impl ty1 :: ty0`, keine (Tag, Named)-Zeile.

**Korrigiert in der Vorbildspalte:** ein Java-Class-File listet nur seine **direkten**
Superinterfaces und eigenen Methoden; itables baut die JVM beim Linken aus dem transitiven
Abschluss — Java ist Vorbild für **C**, nicht B. Das B-Vorbild ist **.NET** (der C#-Compiler
emittiert transitive `InterfaceImpl`-Zeilen). Confidence für beide: **behauptet** (Kenntnis der
Formate, nicht in dieser Runde gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A: Bibliotheken bleiben Quelle | heute | kein Binärpaket |
| B: alle Zeilen deklarierter Konformanzen, transitiv | **.NET** | pro Vorfahr **ein Typeintrag plus eine Zeile** (r08, IF-48); = Vollständigkeitsregel von IF-01 B |
| C: Konsument ergänzt beim Laden | **Java** (itable beim Linken), Go (itab) | ein Linkschritt, den `16-building.md:88` nicht hat |
| D: Header trägt die Konformanzen | Paketentwurf | zwei Wahrheiten |

**Empfehlung: B, zusammen mit IF-01, vor dem Paketmanager.** **Bricht: nein** heute, **major** nach dem
ersten Binärpaket. **Hängt ab von:** IF-01, IF-12, IF-41, IF-48, Modul-/Paket-, Bytecode-Gebiet.

### IF-22 — Was bedeutet eine Constraint für ein nicht-nominales Typargument?

**Heute:** nichts (Befund 4). **Korrigiert:** A bricht **nicht nur** Programme, die heute scheitern —
`audit3/n50` (`id([1, 2, 3])` mit `T :: [Display]`) kompiliert und druckt 3. Unter A ist das ein
neuer `SEM0028`. Die Zeile „Defektwarnung entfällt" war nach dem eigenen §1.6-Kriterium falsch.

| Option | Vorbild | Preis |
|---|---|---|
| A: ohne Konformanz `SEM0028` an der Aufrufstelle | Rust, Swift, Go, Haskell | vier Zeilen in `Satisfies`; **bricht `n50`-artige Programme** |
| B: A + Konformanzen aus IF-07 C | Swift | dann funktioniert `println([1,2,3])` |
| C: bleibt permissiv | heute | die Julia-Fehlerklasse |

**Empfehlung: A in 4.7 mit Warnung, Fehler in 5.0; B mit IF-07 C.** **Bricht: minor.**
**4.x-Warnstufe:** ab 4.7 „dieses Argument erfüllt die Constraint nicht; 5.0 lehnt es ab" —
**Bruchwarnung** (§1.6: Antwort fest, Programm heute legal). **Hängt ab von:** IF-07, IF-11.

### IF-23 — Gilt `mut` für einen `let`-gebundenen Empfänger?

Unverändert: **A** (`SEM0109` auf den `mut fn`-Aufruf ausdehnen; `rv16` gegen `rv17`;
`SemaRules.cs:321-345`). Migrationsuhr. **Hängt ab von:** `SPEC-RUNDE.md:82`, `:150`, IF-14.

### IF-24 — Wie bekommt ein Programm aus einem Interface-Wert eine unabhängige Kopie?

**Heute:** gar nicht (`q16`). **Empfehlung angepasst an IF-14 A:** **B** (der Downcast aus IF-03
liefert den Struct, die Kopie ist die gewöhnliche). A galt nur unter IF-14 B/B', die nicht mehr
empfohlen sind. **Bricht: nein.** **Hängt ab von:** IF-14, IF-03.

### IF-25 — Was ist `?Shape`?

Unverändert: **A** (leere Referenz, Typindex bedeutungslos; Satz in `Bytecode.md:819-823`;
`SEM0059`-Vorschlag reparieren). **Hängt ab von:** Optionals-Gebiet, IF-14, IF-16.

### IF-26 — Was kostet ein generisches Interface als Wert?

**Heute:** eine Typeintrags-Familie pro Instanz (`q10`: 48 Einträge im Hello-World, 3 Iterator-
Instanzen à 23 Slots; `q18`). **Korrigiert in der Vorbildspalte von C:** `dyn Trait<T>` war in Rust
**immer** erlaubt; die dyn-Kompatibilität verbietet generische **Methoden** (= `SEM0082`), nicht
generische Traits. C# 11 betrifft statische Member, nicht Generizität. **C hat kein Vorbild.**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute, C++ | jede erreichbare Instanz kostet einen Eintrag mit Slotliste — **und eine Zeile und Spalte in der Matrix** (IF-41) |
| B: nicht materialisierte Einträge streichen | Rust | Erreichbarkeitsdurchgang; kollidiert mit IF-01 B/IF-21 B |
| C: generisches Interface nur als Constraint | **keines** | bricht `Iterator<T>` als Rückgabetyp |
| D: assoziierte Typen | Rust, Swift | ein Eintrag statt drei |

**Empfehlung: A, gemessen und aufgeschrieben; B an IF-01/IF-21 gekoppelt.** **Bricht: nein (A), major (C).**
**Hängt ab von:** IF-01, IF-06, IF-21, IF-41.

### IF-27 — Kohärenz für inhärente `extend`-Blöcke

Unverändert: **C plus B als Notausgang** (`multi2/`: `SEM0044` erst beim Konsumenten). **Bricht: minor.**
**Hängt ab von:** Modul-Gebiet, IF-12.

### IF-28 — Werkzeugunterstützung für Interfaces

Unverändert: **B + C** (`ImplementationProvider`, `CodeActionProvider`, `Doc.Group` in
`InterfaceListDoc`). Neu daneben: **IF-47** (Rename über die Konformanzbeziehung), das die zweite
Fassung nicht prüfte. **Hängt ab von:** Editor-Gebiet, IF-08, IF-17, IF-47.

### IF-29 — Welche 4.7-Warnungen sind mechanisch nachziehbar?

Unverändert: **B** (Warnungen ohne Fix-it nennen beide Fundstellen; `DiagnosticNote` mit Span,
`TypeChecker.cs:712-713`). IF-13 B' ist jetzt genau diese Form. **Hängt ab von:** Diagnostik-,
Editor-Gebiet, IF-13, IF-23.

### IF-30 — Stimmen Devirtualisierer und vtable-Zeile überein?

**Heute:** ja für den Wertpfad (`Devirtualizer.cs:42-45` liest `module.Impls`). **Präzisiert:** der
Satz „eine Quelle der Wahrheit" gilt **nicht** für den monomorphisierten Pfad — dort wird keine
Zeile gelesen, sondern das Constraint-Memberset (`n28b`). Gemessen zusätzlich (`rev3/r06`): im
Release-Profil wird der Benchmark-`callvirt` devirtualisiert **und** geinlint (`lyrc lower --release`:
0 `callvirt`, `viaIface` verschwindet; `--debug`: 1 `callvirt`).
**Empfehlung: A, und der Spec-Satz benennt beide Pfade:** *die Impls-Zeile ist die Quelle des
Interface-Dispatch; ein Constraint-Aufruf ist kein Dispatch, sondern eine monomorphisierte
Auflösung über die Constraint-Member.* **Hängt ab von:** IF-13, IF-37.

### IF-31 — Kann eine Konformanz privat sein?

Unverändert: **A** (Konformanz ist global, `pub extend` wird Fehler). **Neu:** die
Interface-Seite (`pub` am Interface, am Member) ist IF-44. **Hängt ab von:** Modul-Gebiet, IF-12,
IF-21, IF-44.

### IF-32 — Recovery-Vertrag für Interface- und `extend`-Körper

Unverändert: **B**. **Hängt ab von:** Diagnostik-Gebiet, IF-09, IF-20.

---

### IF-33 — Doppelte Konformanzdeklaration im selben Modul: Fehler, oder welcher Body gewinnt?

**Heute:** die **Deklaration** ist per Spec eine Konformanz, absichtlich:
`lyric-spec/spec/05-interfaces.md:30-36` — *„Everything reached twice by other routes stays ONE
conformance, deliberately: … an `extend` block declaring a conformance the type also declares
itself … a library adopting a conformance that a downstream module had added by `extend` must not
break that module's build"*; Fall `extend_repeating_a_conformance_is_one.lyr` (`since: 3.5.0`, mit
**leerem** `extend`-Body). Gemessen `audit3/n09`: still. **Was die Spec nicht regelt, ist der
Body**: `n09b` (`struct T :: [I] { fn f() { 1 } }` + `extend T :: [I] { fn f() { 2 } }`) kompiliert
ohne Diagnose, `lyrc lower` zeigt **eine** Zeile `impl ty1 :: ty0 [f1]`, die Funktion des
`extend`-Blocks wird nicht emittiert, beide Aufrufwege drucken 1. `n09c` (zwei `extend T :: [I]`,
zweiter mit einem Nicht-Slot-Member `g`): still. Ursache: `InterfacesOf` dedupliziert per
Instanzliste (`TypeChecker.cs:3918-3922`); `STATUS.md:2305-2308` deckt nur die Wiederholung
**innerhalb einer** Liste. Rust: `E0119 conflicting implementations`. Confidence: **gemessen** +
**gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — eine Konformanz, der Body des zweiten Blocks ist eine gewöhnliche Extension (Member gewinnt, still) | heutige Spec §5.1 | ein `fn f` mit derselben Signatur wie der Member existiert still zweimal — dieselbe Form wie zwei Extensions, die `SEM0044` refused |
| B: doppelte Deklaration ist ein Fehler | Rust E0119 | bricht das Bibliotheks-Adoptionsargument der Spec (§5.1:34-36) |
| C: doppelte Deklaration bleibt eine; ein **Body**, der einen schon gefüllten Slot desselben Typs füllt, ist ein Fehler | — | eine Prüfung an der Stelle, die heute dedupliziert; das Adoptionsargument überlebt (der Downstream-Block ist leer oder wird geleert) |

**Empfehlung: C.** Die Spec hat die Deklarationshälfte bewusst entschieden, und der Grund trägt.
Die Body-Hälfte ist eine Lücke: ein stiller zweiter Körper ist genau die Form, die `SEM0044` für
zwei Extensions verbietet. **Bricht: minor** (stille zweite Körper werden Fehler). **4.x:**
Defektwarnung ab 4.7. **Hängt ab von:** IF-12, IF-27, §1.7 (Spec-Satz ergänzen).
Confidence: **gemessen**.

### IF-34 — Zwei Interfaces mit gleichnamigen Defaults: Auflösung an der Deklaration oder an der Benutzung?

**Heute:** Konformanz wird angenommen; `C{}.greet()` ist `SEM0043` (Benutzung); `I1`-Wert und
`I2`-Wert antworten verschieden (Befund 5, `audit3/n02a`/`n02b`). Spec §5.4:127-129 nennt die
Mehrdeutigkeit nur für den konkreten Typ. Confidence: **gemessen** + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | ein Name, zwei Funktionen, gewählt vom statischen Typ — ohne Extension |
| B: Kollision muss an der **Deklaration** aufgelöst werden (eigener Member), sonst `SEM0043` dort | **Kotlin** (*„must override"*), C# (explizite Implementierung) | Programme, die nur die Wertpfade benutzen, brechen; die stdlib ist zu prüfen (behauptet: kein Treffer erwartet) |
| C: A, aber die Wertpfade werden ebenfalls `SEM0043` | — | Halbe Regel: der Typ konformiert, aber kein Pfad kann den Member erreichen |

**Empfehlung: B.** Es ist die Regel, die `SEM0079` für Eltern schon durchsetzt („zwei Eltern, ein
Name, zwei Deklarationen"), eine Ebene tiefer. **Bricht: minor.** **4.x:** Defektwarnung an der
`::`-Liste ab 4.7 — Anker im Modul des Typs, `lyrfix` kann den Override-Rumpf nicht raten, nennt
aber beide Interfaces. **Hängt ab von:** IF-09 (mit Signatur-Slots wird „Name" zu „Signatur"),
IF-13, §1.7.

### IF-35 — Darf `extend` ein Interface als Ziel haben?

**Heute:** die Sema nimmt `extend Shape { fn describe(): string { … this.area() … } }` an, das
Lowering antwortet `LYR-IR0001: interface 'Shape' has no method 'describe'` (gemessen `audit3/n06`).
Dieselbe Still-dann-`IR0001`-Form wie `extend int[]` (IF-11), dort aber schon in der Sema still.
Confidence: **gemessen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: refused in der Sema, mit Satz | — | ein `SEM`-Code; die Sprache verliert nichts, was sie hat |
| B: erlaubt als Blanket-Extension: jeder konforme Typ bekommt den Member; am Wert per Chain erreichbar | **Swift** (Protocol Extension), **Rust** (`impl<T: Shape> Ext for T`) | zwei Dispatch-Regeln (die Swift-Falle §2.3) **oder** ein Slot für einen Member, den kein Implementierer schreibt — also ein Default ohne Interface-Deklaration → Rule 2 gegen Default-Methoden |
| C: B, aber nur als Zucker für einen Default-Member außerhalb des Interface-Bodys (bekommt einen Slot, orphan-gebunden ans Interface-Modul) | — | ein zweiter Ort für Defaults |

**Empfehlung: A sofort**, B nicht: Lyric hat Default-Methoden **mit** Slot, und Swifts Protocol
Extension ist die Sprache, die die zwei-Dispatch-Regeln-Falle erfunden hat. Wer einen Member auf
allen Implementierern will, schreibt den Default ins Interface. **Bricht: nein** (kompiliert heute
nicht). **Hängt ab von:** IF-11, IF-07 (die Blanket-Form, falls sie je kommt, ist dort).

### IF-36 — Gehört die Argumentformung in eine Phase vor allen drei Aufrufformen?

**Heute:** Argumentformung (Default-Auffüllung, `params`-Sammlung) passiert vor `call`, nicht vor
`callvirt`, und im monomorphisierten Aufruf gegen den falschen Leser (Befund 1: `p30`, `n07`, `r04`,
`r04b`, `r04e`; Kontrollen `p30b`, `n07b`, `r04c`, `r04d`). Ab 4.7 kommen benannte Argumente dazu
(`lyric-v5-features.md:39`, P2, „Prototyp existiert"). Confidence: **gemessen** + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: pro Feature reparieren (Default jetzt, `params` jetzt, benannte später) | heute | die nächste Argumentform wiederholt den Defekt; drei Leser bleiben drei |
| B: **eine** Formungsphase in der Sema, die aus der aufgelösten Zielsignatur die vollständige positionsbasierte Argumentliste baut, **bevor** das Lowering zwischen `call`, `callvirt` und Monomorphisierung unterscheidet | Roslyn (bound call mit vollständiger Argumentliste vor dem Emit), rustc (Defaults gibt es nicht — deshalb hat es den Fall nicht) | ein Umbau, der IF-15 A voraussetzt (**welche** Signatur ist die Quelle des Defaults: die Interface-Deklaration) |

**Empfehlung: B, als 4.7-Bugfix, mit IF-15 A als Regel darüber.** Ein Modul, das sich nicht
zurücklesen lässt, ist kein Design; dass es an drei Stellen verschieden scheitert, zeigt, dass die
Reparatur nicht an den Stellen liegt. **Bricht: nein.** **Hängt ab von:** IF-15, Funktions-Gebiet
(benannte Argumente müssen durch dieselbe Phase).

### IF-37 — Welche Auflösungsregel gilt im monomorphisierten Körper?

**Heute:** nur Constraint-Member sind sichtbar (`SEM0027`, `audit3/n28b`), und damit immer der
Interface-Default, nie die Extension — auch wenn `T` konkret ist und die Extension am konkreten Typ
sichtbar wäre (`n28`: `viaT(C{})` druckt `default`). Es ist ein dritter Pfad mit eigener Regel, und
IF-30s „eine Quelle der Wahrheit" gilt für ihn nicht: er liest keine Impls-Zeile. Confidence:
**gemessen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt: Constraint-Member, nichts anderes; die Antwort ist die des Interface-Defaults | **Rust** (im generischen Körper existieren nur die Bound-Member), Swift (generischer Kontext) | unter IF-13 A liefe der Pfad auseinander; unter B' stimmt er automatisch mit der Zeile überein |
| B: nach Monomorphisierung gilt die Auflösung des konkreten Typs (Extensions sichtbar) | C++ (Templates, ADL) | Duck-Typing durch die Hintertür: ein generischer Körper kompiliert für `C` und nicht für `D`; die Constraint verliert ihre Bedeutung als Vertrag |

**Empfehlung: A, ausdrücklich in die Spec**, als dritter Satz neben §5.4 und IF-30: *im generischen
Körper ist ein Constraint-Aufruf die Auflösung gegen die Constraint, und er antwortet, was die
Impls-Zeile des Typargments antwortet.* Das ist zugleich ein Argument für IF-13 B' und gegen A.
**Bricht: nein.** **Hängt ab von:** IF-13, IF-30, Generics-Gebiet.

### IF-38 — Gibt es einen Join-Typ für heterogene Literale?

**Heute:** `[Circle{}, Square{}]` ohne Annotation ist `SEM0009: array elements must share a type`
(gemessen `audit3/n04`); mit `Shape[]`-Kontext geht es (`p39`). Kein Spec-Satz zu einer
Inferenz über gemeinsame Konformanzen. Confidence: **gemessen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: nie inferiert, nur kontextuell (heute) | **Rust** (`vec![a, b]` mit zwei Typen ist ein Fehler; `Box<dyn T>` muss geschrieben werden), Go | jede heterogene Sammlung trägt ihre Annotation; ehrlich und billig |
| B: Join über die **eindeutige** gemeinsame Konformanz | — | Bei `Circle :: [Shape, Named]` und `Square :: [Shape, Named]` gibt es zwei Kandidaten → Mehrdeutigkeit; mit Elternketten (IF-01) wird der Join zur Suche nach der kleinsten oberen Schranke |
| C: Join wie in Sprachen mit Vererbung (LUB) | Java, C#, Kotlin | braucht eine Subtyp-Ordnung, die Lyric ohne Vererbung nicht hat; mit IF-01 B gäbe es eine über Interfaces — und mehrere Eltern machen die LUB uneindeutig |

**Empfehlung: A, mit dem Satz in der Spec**: *ein Interface-Typ wird nie inferiert, nur
angenommen.* Die Fehlermeldung darf den Kontext vorschlagen („annotate the array as `Shape[]`").
**Bricht: nein.** **Hängt ab von:** IF-01 (macht B/C erst denkbar), IF-18.

### IF-39 — Wie rangiert die Interface-Konvertierung im Overload-Recht?

**Heute, gemessen:** `f(Shape)` neben `f(Circle)` wählt für `Circle{}` den exakten Treffer
(`audit3/n03`: `circle`, für den `Shape`-Wert `shape`). `f(Shape)` neben `f(Named)` mit `Circle ::
[Shape, Named]` ist **`SEM0086: 'f' is ambiguous for (Circle) — no candidate fits better than
another`** mit zwei Notes (`rev3/r02`; `TypeChecker.cs:2118`); Kontrolle `r02b`: mit einem
`Shape`-Wert `shape`, mit einem `Named`-Wert `named`. Also: exakt schlägt Konvertierung, zwei
Konvertierungen sind gleichrangig, die Deklarationsreihenfolge entscheidet **nicht**. Confidence:
**gemessen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt: exakt > Interface-Konvertierung; zwei Konvertierungen gleichrangig → Fehler | **C#**, Kotlin (ohne Vererbung ist die Rangfolge trivial) | mit IF-01 B wird „Kind-Interface vor Eltern-Interface" eine dritte Stufe, die heute niemand definiert hat |
| B: A + mit IF-01: Konvertierung auf das nähere Interface ist besser als auf den Vorfahren | C# (spezifischere Konvertierung gewinnt), Java | eine Ordnung über die Elternkette — Diamant/mehrere Eltern machen sie partiell |
| C: keine implizite Konvertierung im Overload-Recht; Interface-Parameter nehmen nur Interface-Werte | — | bricht jeden Aufruf `f(Circle{})` mit `f(x: Shape)` |

**Empfehlung: A jetzt aufschreiben, B mit IF-01 entscheiden — im Overloading-Gebiet.** Der Satz für
die Spec: *eine Interface-Konvertierung ist eine Stufe schlechter als der exakte Typ, und alle
Interface-Konvertierungen sind gleichrangig.* **Bricht: nein.** **Hängt ab von:** Overloading-Gebiet,
IF-01, IF-18.

### IF-40 — Ist ein Default-Argument Teil der Konformanzsignatur?

**Heute:** nein. `interface Greeter { fn greet(who: string = "world") }` mit `struct G :: [Greeter]
{ fn greet(who: string) }` konformiert und läuft (`audit3/n30`). Der direkte Aufruf `G{}.greet()`
ist dann `SEM0014` (`rev3/r04d`), der Aufruf über die Constraint ein Compiler-Absturz (`r04`), der
über den Wert ebenfalls (`p30`). §1.1 behauptete „exakt" — das war falsch. Confidence: **gemessen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: nein, und die Implementierung **erbt** den Default (darf ihn nicht wiederholen) | **Kotlin** | = IF-15 A; der Signaturvergleich bleibt, wie er ist, und IF-36 liest den Default an einer Stelle |
| B: ja, exakt: Default muss wiederholt werden und übereinstimmen | — | Wiederholung an jeder Implementierung; C#s Falle (statischer Typ) vermieden, aber um den Preis von Redundanz, die `lyrfix` schreiben müsste |
| C: ja, aber der statische Typ entscheidet (Defaults sind Aufrufstellen-Sache) | **C#** | die Falle aus IF-13, in klein |

**Empfehlung: A** (identisch mit IF-15 A, hier als Signaturfrage gestellt, damit §1.1 richtig steht).
**Bricht: minor** (wiederholte Defaults). **Hängt ab von:** IF-15, IF-36.

### IF-41 — Wie skaliert die dichte Dispatch-Matrix?

**Heute, gelesen:** `DispatchTable.cs:52-54` — `new int[]?[size, size]` mit `size =
module.Types.Count`, also **alle** Typeinträge in beiden Dimensionen, nicht Typen × Interfaces.
**Gemessen** (`lyrvm info`): `hello` 48, `stats` 49, `bank` 50, `interfaces` 51, `shapes` 54,
`inventory` 56 Einträge; davon im Hello-World 3 Interfaces (`q10`). Zellen: 2 304 bis 3 136; bei
8 Bytes pro Referenz **18–25 KB** pro geladenem Modul. Extrapolation, **behauptet**: 500 Einträge
→ 2 MB; 2 000 → 32 MB; die Zahl der Interface-Instanzen (`q18`, IF-26) und die Vorfahr-Einträge unter
IF-01 B (IF-48) treiben `size`. Wann sie kippt, hängt vom Paketmodell ab: ein Modul, das eine
Bibliothek mit hundert generischen Interface-Instanzen einbindet, zahlt sie **auch dann**, wenn es
sie nicht benutzt (`q10`: drei Iterator-Instanzen im Hello-World).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt dicht, `size` = alle Typen | heute | quadratisch in der Typzahl; heute unmerklich, ohne Grenze aufgeschrieben |
| B: dicht, aber Typen × **Interfaces** (zweiter Index nur über Interface-Einträge) | JVM (itable pro Klasse, nur ihre Interfaces) | eine Indirektion (Interface-Id → Spaltenindex), zur Ladezeit; Zellen = Typen × Interfaces, im Hello-World 48 × 3 = 144 statt 2 304 |
| C: Zeilen pro Typ als kleines Array seiner Interfaces (sparse) | Go (itab-Cache), .NET (Interface-Map pro Typ) | eine Suche pro `callvirt` oder ein Cache — genau das, was `DispatchTable.cs:12-14` (*„neither checks nor hashes"*) vermeiden wollte |

**Empfehlung: B, wenn IF-01 B/IF-21 B kommen — vorher nicht.** B behält O(1) und den Ladezeit-Bau,
streicht nur den Faktor „alle Typen" in der zweiten Dimension. Aufgeschrieben gehört in
`Bytecode.md` oder das VM-Design: *die Matrix ist Typen × Interface-Einträge, gebaut beim Laden;
eine Zeile pro (Typ, Interface)-Paar.* Confidence: **gelesen** + **gemessen** (Zahlen), **behauptet**
(Extrapolation). **Bricht: nein** (VM-intern). **Hängt ab von:** IF-01, IF-21, IF-26, IF-48,
Bytecode/VM-Gebiet.

### IF-42 — Was kostet ein Interface-Aufruf gegenüber dem direkten?

**Heute, gemessen** (`rev3/r06`: 3 Mio. Aufrufe `area()` je Variante, `std.os.nowNanos`, Empfänger
kommt als **Parameter** an, also ohne `mkiface` in der Schleife; jede Variante zweimal):

| Konfiguration | direkt | Interface-Wert | `callvirt` im Code? |
|---|---|---|---|
| `--debug`, Interpreter | 1 141 / 1 343 ms | 1 247 / 1 082 ms | ja (1, `lyrc lower --debug`) |
| `--release`, Interpreter | 1 138 / 1 166 ms | 1 065 / 1 171 ms | **nein** — devirtualisiert und geinlint (`viaIface` verschwindet) |
| `--debug`, `--jit` | 262 / 305 ms | 264 / 260 ms | ja |
| `--release`, `--jit` | 34 / 24 ms | 23 / 23 ms | nein |

Also: **kein messbarer Unterschied** in allen vier Konfigurationen — im Interpreter liegt die
Schleife selbst bei ~0,4 µs pro Iteration, und der Matrixzugriff verschwindet darin; im
Release-Profil gibt es den `callvirt` gar nicht mehr, weil der Inliner `viaIface` in `main` zieht und
das dort sichtbare `mkiface` den Devirtualisierer greifen lässt (`Devirtualizer.cs:7-11`). Was
**nicht** gemessen ist: der Struct-Übergang (`structcopy` an der Bindungsstelle, IF-14) und ein
Empfänger mit vielen Slots. `mkiface` selbst ist eine Bitoperation (`Interpreter.cs:634-639`).
Confidence: **gemessen** (Laufzeit, 5 % Streuung zwischen Wiederholungen) + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts tun; die Zahl steht jetzt im Dossier | — | — |
| B: einen Benchmark in `lyrtest`-Form neben `examples/` legen, damit IF-01/IF-03 (neue Opcodes) gegen eine Basis gemessen werden | Rust (`criterion` in-tree), Go (`testing.B`) | eine Datei und eine CI-Zeile; Zahlen, die sich beim nächsten VM-Umbau von selbst melden |

**Empfehlung: B**, klein. Die eigentliche Erkenntnis ist, dass die Kostenfrage bei Lyric nicht am
Dispatch, sondern an der **Modulgröße** (IF-26, IF-41, IF-48) hängt. **Bricht: nein.** **Hängt ab von:**
Testen-Gebiet, Bytecode/VM-Gebiet.

### IF-43 — Welche IF-Antworten sind Spec-Änderungen, und in welcher Reihenfolge wandert der Pin?

**Heute:** §1.7, vollständig. Die zweite Fassung nannte weder `lyric-spec` noch die Suite.
Gelesen: `lyric-spec` auf `8f17c02`, 178 Fälle, 21 mit `since: 4.6.0`, 24 unter
`05-interfaces/`; darunter `no_value_upcast.lyr` (IF-01), `chain_implies_conformance.lyr`,
`extend_repeating_a_conformance_is_one.lyr` (IF-33), **keiner** für die 4.6-Regel „Extension schlägt
Default" (IF-13). Confidence: **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: jede Regeländerung dieses Gebiets geht spec-first: Regel-PR mit `since:`, Zwilling, dann Compiler, dann Release, Pin zuletzt | der Modus des Projekts (`STATUS.md:215`) | IF-02 und IF-13 dauern eine Runde länger als ein Bugfix — und sind ehrlicher |
| B: Bugfix-große Regeländerungen (IF-02) gehen als Bugfix, Spec zieht nach | — | genau die Form, die der Modus verbietet; §5.3:114 ist normativ |

**Empfehlung: A, ohne Ausnahme.** Konkret für 4.7: ein Spec-PR mit vier Sätzen (IF-02 §5.3:114,
IF-13 §5.4:126-130 in der B'-Fassung, IF-33 §5.1 Body-Regel, IF-34 §5.4 Deklarationsregel) und den
vier Zwillingen, **plus ein Fall, der die 4.6-Regel überhaupt erst pinnt**, bevor sie geändert wird —
sonst gibt es keinen roten Lauf, der beweist, dass die Änderung ankommt. **Bricht: nein.** **Hängt ab
von:** allen Spec-Fragen aus §1.7.

### IF-44 — Was bedeutet `pub` am Interface und an seinen Membern?

**Heute, gemessen (`rev3/r07`)**: Modul `lib` mit `interface Hidden { pub fn h(): int; }` (**nicht**
`pub`) und `pub class T :: [Hidden] { fn h() { 5 } }`, `pub fn viaHidden<X :: [Hidden]>(x: X)`. Der
Konsument importiert `T` und `viaHidden`: `T{}.h()` druckt 5, `viaHidden(T{})` druckt 5; `import lib
{ Hidden }` ist `RES0004: 'Hidden' is not public in 'lib'`. Also: ein Typ konformiert zu einem nicht
exportierten Interface, die Konformanz wirkt nach außen (Member erreichbar, Constraint erfüllbar), nur
der **Name** ist unsichtbar; `pub` am Member parst und bedeutet nichts (`p28`). Unter v5-Bruch #20
(`lyric-v5-features.md:57`, Member standardmäßig privat) ist ungeregelt, was aus `T.h` wird.
Confidence: **gemessen** + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: Interface-Member sind immer öffentlich (das Interface ist ein Vertrag; `pub` am Member wird Fehler); ein Typ darf zu einem nicht-`pub` Interface konformieren, und der erfüllende Member ist öffentlich, auch wenn v5 Member privat macht | **Rust** (Trait-Methoden sind so sichtbar wie der Trait; `impl` erzwingt sie; private Traits als „sealed"-Idiom), Haskell | ein Satz, ein Fehler; und die Sealed-Trait-Form bekommt Lyric gratis: ein nicht-`pub` Interface kann nur im eigenen Modul konformiert werden — **falls** IF-12 die Orphan-Regel so liest (heute: der Typ oder das Interface muss im Modul stehen — ein fremder Typ könnte also nicht, ein eigener schon) |
| B: `pub` am Member steuert die Sichtbarkeit des Slots; nicht-`pub` Member sind nur im Interface-Modul aufrufbar | — | ein Slot, der von außen nicht aufrufbar, aber im Fat Pointer vorhanden ist; und Implementierer außerhalb des Moduls können ihn nicht schreiben — es sei denn, er hat einen Default → ein zweiter Weg zu „sealed" |
| C: bleibt (parst, bedeutet nichts) | heute | die dritte stille Bedeutung von `pub` (§1.5.2) |

**Empfehlung: A, vor der Member-Sichtbarkeit.** Regel in einem Satz: *ein Interface-Member ist
öffentlich, ein Member, der einen erfüllt, auch; die Sichtbarkeit eines Interfaces regelt, wer es
nennen kann, nicht, wer es erfüllt.* **Bricht: minor** (`pub` am Interface-Member wird Fehler).
**4.x:** Defektwarnung ab 4.7. **Hängt ab von:** Modul-Gebiet (#20), IF-12, IF-31.

### IF-45 — Dürfen Interface-Werte über `spawn` wandern, und was bedeutet die geteilte Box dann?

**Heute, gemessen (`rev3/r05`)**: `let b: Bump = s;` in eine Task gegeben (`spawn(worker(b))`,
`worker` ruft `b.bump()` nach einem `yield`), `run()`, danach `s=0 b=1`. Die Task trifft die Box,
nicht das Original — genau IF-14, über die Task-Grenze. Gelesen: `stdlib/std/os.lyr:41-42`, `:55`
— *„The VM itself is single-threaded"*; Tasks sind Koroutinen (`guide/13:155-175`), es gibt keine
Isolation, die ein Interface-Wert verletzen könnte, und keine Kanäle mit Eigentumsübergang. Was
zwei Tasks teilen, teilen sie wie zwei Bindungen im selben Thread. Confidence: **gemessen** + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: keine Regel — Tasks sind Koroutinen, Interface-Werte sind Werte wie alle anderen | heute; Lua, Python-Generatoren | Wenn v5 (`lyric-v5-features.md:57`, #23: *„`spawn` liefert ein Handle"*) je Threads oder Isolation einführt, ist die geteilte Struct-Box die erste Datenrasse |
| B: die Spec sagt es ausdrücklich: *ein Interface-Wert trägt Referenzsemantik, auch über einen Struct; eine Task, die ihn erhält, teilt ihn* | Go (Interface-Werte sind Referenzen auf die Kopie) | ein Satz in §5.3 und im Koroutinen-Kapitel |
| C: Interface-Werte über Structs dürfen nicht in eine Task | — | ein Typtest zur Laufzeit an `spawn` — und kein Grund, solange die VM ein Thread ist |

**Empfehlung: B, ein Satz, mit Verweis auf IF-14 A.** Die Nebenläufigkeitsfrage ist in diesem Gebiet
keine eigene; sie wird eine, wenn das Nebenläufigkeitsgebiet Isolation beschließt — dann gehört
IF-14 dort neu gestellt. **Bricht: nein.** **Hängt ab von:** IF-14, Nebenläufigkeits-Gebiet.

### IF-46 — Können Interface-Werte die FFI-/Host-Grenze überschreiten?

**Heute, gemessen (`rev3/r03`)**: `extern "dotnet" fn take(x: Shape): int` ist `LYR-SEM0099:
parameter 'x' of extern 'take' has type 'Shape', which does not cross the "dotnet" boundary —
scalars, bool, char and string do`. Gelesen: `docs/guide/14-embedding.md:152-155` (nur Skalare,
`bool`, `char`, `string`, `void`; Optional/Array/Struct sind `SEM0099`); `MakeInterfaceValue`
(`FunctionLowerer.cs:3081-3088`) kennt class/struct/enum. Ein Host kann heute kein Lyric-Interface
implementieren und keinen Fat Pointer entgegennehmen; die Richtung Host→Skript für Typen ist
`HostTypeBuilder` (`src/Lyric.Embedding/HostTypeBuilder.cs`, nicht gelesen — **behauptet**, dass er
keine Interface-Konformanz anbietet). Confidence: **gemessen** + **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt: Interfaces bleiben im Skript; der Host sieht Skalare, Strings, flache Structs | heute | ein Host-Callback, der „irgendein `Shape`" will, bekommt ein Handle (`opaque type`) und ruft per Skriptfunktion zurück — der bestehende Callback-Entwurf (Queue + Lyric-Handler) |
| B: ein Host-Typ kann zu einem Lyric-Interface konformieren (Host liefert eine Impls-Zeile) | C#-Hosting von Lua/Wren (Foreign Classes), Godot GDExtension | die Impls-Zeile bekäme Funktionsindizes auf Host-Funktionen — das Format kennt Imports als Callables (`Bytecode.md:357-358`: *„imports first, then functions"*), also **trägt das Format es schon**; die Sema braucht eine Deklarationsform (`extern class`?) |
| C: ein Fat Pointer kreuzt als (Objekt-Handle, Typindex) und der Host ruft per Slot zurück | — | der Host müsste Slots kennen; das ist ein zweites Marshalling neben Struct-Flattening |

**Empfehlung: A für v5, B als benannte Frage des FFI-Gebiets** — mit der Beobachtung, dass das
Format B billig macht (Imports sind Callables) und die Sprache teuer (eine neue Deklarationsform).
**Bricht: nein.** **Hängt ab von:** FFI-Gebiet, Bytecode-Gebiet, IF-21.

### IF-47 — Folgt `rename` in `lyrls` der Konformanzbeziehung?

**Heute, gelesen:** `src/Lyric.Lsp/Analysis/ReferenceProvider.cs:44-50` sammelt Fundstellen per
**Referenzgleichheit des Symbols** (*„Reference equality throughout: symbols are identity
objects"*; `if (!ReferenceEquals(Target(bound), symbol)) continue;`), `RenameProvider.cs:29-58`
baut die Edits aus genau dieser Liste. Ein Implementierer-`fn f` ist ein eigenes `FunctionSymbol`,
nicht das des Interface-Members — also folgt ein Rename auf dem Interface-Member **keinem**
Implementierer, keinem `extend`-Block, und umgekehrt folgt ein Rename auf dem Implementierer nicht
dem Interface. Der Kommentar in `RenameProvider.cs:15-18` benennt die Folge selbst: *„a rename that
misses a site does not fail — it corrupts"* (hier: die Konformanz bricht als `SEM0042` nach dem
Rename). **Nicht gemessen** (kein LSP-Lauf in dieser Runde). Confidence: **gelesen**.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt; der Compiler meldet nach dem Rename `SEM0042` | heute | ein Rename, das ein Programm zuverlässig kaputt macht, in dem Gebiet, dessen ganze Idee „ein Name, ein Vertrag" ist |
| B: Rename folgt der Konformanzbeziehung in beide Richtungen (Interface-Member ↔ alle erfüllenden Member ↔ Default-Body) | Rust-Analyzer, Roslyn, IntelliJ (alle drei) | der Index „wer erfüllt Slot X" — derselbe, den IF-28 B für „find implementations" braucht; `RecordConformanceImpl` (`TypeChecker.cs:815`) schreibt ihn schon |
| C: B, aber refused, wenn eine Fundstelle außerhalb des Projekts liegt | die bestehende Projekt-Regel des `RenameProvider` (`:15-19`) | keine neuen Regeln; nur die Liste wird vollständig |

**Empfehlung: B mit C**, als Teil von IF-28 — dieselbe Tabelle, zweiter Konsument. **Bricht: nein.**
**Hängt ab von:** Editor-Gebiet, IF-28.

### IF-48 — Wie viele Typeinträge kostet die Vollständigkeitsregel (IF-01 B / IF-21 B)?

**Heute, gemessen (`rev3/r08a`/`r08b`)**: eine Kette `A ← B ← C`, `class K :: [C]`. Nur `let c: C`
materialisiert: **39** Typen, nur `interface ty0 C` als Kettenmitglied. Zusätzlich `let b: B; let a:
A`: **41** Typen, `interface ty2 B`, `interface ty3 A`, drei Impls-Zeilen. Jeder Vorfahr, der eine
Zeile bekommt, ist **ein Typeintrag mit voller Slotliste** (`rv20`: `Named` fehlt heute ganz; `q17`:
Slotlisten der Eltern). IF-01 rechnete „die Sektion gewinnt Zeilen, keine Felder" — die
Types-Sektion gewinnt **Einträge**. Für `Iterator<T>` mit drei Instanzen (`q10`: `int`, `float`,
`string`) und drei Elterngenerationen: 3 × (1 + 3) = **12 Einträge statt 3**, jeder mit seiner
Slotliste (bei `Iterator` 23 Slots, die Eltern kürzer) — Arithmetik über den gemessenen Mechanismus,
die Zahl selbst **behauptet**. Und jeder Eintrag ist eine Zeile **und Spalte** der Matrix (IF-41).
Confidence: **gemessen** (Mechanismus) + **behauptet** (Hochrechnung).

| Option | Vorbild | Preis |
|---|---|---|
| A: B aus IF-01/IF-21 wörtlich: jeder Vorfahr jeder materialisierten Instanz wird Eintrag und Zeile | .NET | linear in Instanzen × Kettenlänge; quadratisch in der Matrix |
| B: Vollständigkeit nur für die Zeilen, die Types-Sektion trägt Vorfahren als **Verweis** (Kind-Eintrag nennt seine Eltern-Ids, Slotliste steht nur einmal — sie ist ohnehin das Präfix) | Java (Class-File nennt Superinterfaces per Index, ohne deren Methoden) | ein neues Feld im Interface-Eintrag → **Formatänderung**; dafür kostet ein Vorfahr nur eine Id |
| C: A, aber IF-26 B (nicht materialisierte Instanzen streichen) davor | Rust | das Erreichbarkeitsproblem aus IF-26 |

**Empfehlung: A, solange die Zahl klein ist, und B als benannte Formatoption, falls IF-41 kippt.**
Die drei Entscheidungen IF-01, IF-21, IF-41, IF-48 sind **eine** Rechnung und gehören ins
Bytecode-Gebiet als ein Satz. **Bricht: nein** (A), **major** (B, Format). **Hängt ab von:** IF-01,
IF-21, IF-26, IF-41, Bytecode-Gebiet.

---

## 4. Was wir übernehmen sollten

| Von | Was | Wohin |
|---|---|---|
| **Swift** | Konformanz ohne Body = Synthese | IF-08 |
| **Swift** | `@retroactive` | IF-12 |
| **Swift SE-0309** | Existential für jedes Protokoll; **kovariantes `Self` wird auf die obere Schranke gelöscht**, nur nicht-kovariante Vorkommen sind blockiert — die genaue Regel, nicht die strengere | IF-04, IF-05 |
| **Swift** | Negativlektion: zwei Dispatch-Regeln an einem Punkt — auch der Grund gegen `extend` auf Interfaces | IF-13, IF-35 |
| **Rust** | `impl<T: Bound>` vor dem Ziel; `impl<T: Debug> Debug for Vec<T>` als **richtiges** Beispiel | IF-07 |
| **Rust** | dyn-Kompatibilität als eigenes Kapitel | IF-04, IF-05, IF-19 |
| **Rust 1.86** | Trait-Upcasting als Warnung vor den Layoutkosten | IF-01 |
| **Rust** | Im generischen Körper existieren nur Bound-Member | IF-37 |
| **Rust** | `vec![a, b]` inferiert keinen Trait-Typ | IF-38 |
| **Rust** | E0119 als Vorbild für die **Body**-Hälfte der doppelten Konformanz, nicht für die Deklaration | IF-33 |
| **Rust** | private Traits als „sealed"-Idiom | IF-44 |
| **Go** | Typ-Switch als Form des Downcasts | IF-03 |
| **C# 11** | `static abstract`: **Vorbild für B** (nur das Typargument ist verboten) — korrigiert | IF-05 |
| **C#** | Explizite Interface-Implementierung: Kollisionen an der Deklaration lösen | IF-09, IF-34 |
| **C#** | Overload-Rangfolge: exakt vor Konvertierung, spezifischere Konvertierung vor allgemeiner | IF-39 |
| **C#** | Member-Hiding braucht `new` | IF-13 |
| **Kotlin** | Überschreibende Funktion wiederholt keine Default-Argumente | IF-15, IF-40 |
| **Kotlin** | Member gewinnt gegen Extension — **als Warnung**, weil die Kollision beim Konsumenten entsteht | IF-13 B' |
| **Kotlin** | Zwei Supertypen mit einem Default: **must override an der Deklaration** | IF-34 |
| **Kotlin** | Selektiver Import entscheidet über sichtbare Erweiterungen | IF-27 |
| **Haskell** | Superklassenkontext, Instanzen in der Liste | IF-10 |
| **Haskell** | Globale Kohärenz | IF-12, IF-31 |
| **Java** | `Serializable` als Negativlektion | IF-19 |
| **Java / .NET** | Class-File nennt Superinterfaces per Index (Option B in IF-48); .NET emittiert transitive Zeilen (IF-21 B) | IF-21, IF-48 |
| **JVM** | itable pro Klasse nur über ihre Interfaces — Typen × Interfaces, nicht Typen × Typen | IF-41 |
| **Roslyn** | Eine gebundene Aufrufform mit vollständiger Argumentliste vor dem Emit | IF-36 |
| **Rust-Analyzer / Roslyn / IntelliJ** | „find implementations", „implement members", **Rename über die Konformanz** | IF-28, IF-47 |
| **Scala 3** | Negativlektion Traits + Typklassen; F-Bounded als Preis von „kein `Self`" | IF-04, IF-06 |

**Gestrichen gegenüber der zweiten Fassung:** „C# 11 als Gegenprobe" (IF-05 — C# ist ein Beleg);
„Rust `impl<T: Display> Display for Vec<T>`" (unmöglich); „Java `.class` trägt die vollständige
Methodentabelle" (nur direkte Superinterfaces); „Rust vor den dyn-Erweiterungen" für IF-26 C (`dyn
Trait<T>` war immer erlaubt); „Rust `trait Shape: PartialEq` / Swift" als Vorbild für `==` auf
Interface-Werten (beide liefern es nur generisch).

**Was Lyric behalten soll:** Objektsicherheit als Herleitung (`TypeChecker.cs:529-538`), mit der
zweiten Regel jetzt richtig formuliert (slotlose Kette); Operatoren als Interface-Methoden; die
`::`-Deklaration als Vorsatz; **eine Quelle der Wahrheit für den Wert-Dispatch** (Impls-Zeile, drei
Konsumenten) — mit dem Zusatz, dass der Constraint-Pfad keine ist und es sagen muss (IF-37); keine
Vererbung; und **`Equatable<Shape>` mit einem Interface als Argument** — eine Form, die Rust und
Swift auf Werten nicht liefern und die nach IF-02 B kostenlos ist.

---

## 5. Konflikte

**Mit `CONTRIBUTING.md` Rule 2:**

| Frage | Konflikt | Bewertung |
|---|---|---|
| IF-06 assoziierte Typen | zweiter Weg, einen Typ festzulegen | Ablehnung; ADR, wenn je |
| IF-17 `sealed interface` | zweite geschlossene Fallmenge | Ablehnung |
| IF-16 C `===` | zweiter Gleichheitsbegriff | Ablehnung |
| IF-03 C `as?` | dritter Weg neben Pattern und `Into` | Ablehnung |
| IF-04 D | zwei `Equatable`-Formen | nur mit Frist |
| IF-21 D | zwei Quellen für Konformanzen | Ablehnung |
| IF-30 B | zweite Dispatch-Quelle | Ablehnung |
| IF-32 C | zweiter Trenner | Ablehnung |
| **IF-35 B/C** | `extend` auf Interface = zweiter Ort für Defaults | **Ablehnung**, A |
| **IF-44 B** | `pub` am Slot = zweiter Weg zu „sealed" | **Ablehnung**, A |
| **IF-13 A** | Extensions im generischen Körper = Duck-Typing neben der Constraint (IF-37 B) | **Ablehnung**, B' |

**Mit anderen Designentscheiden:**

- **„Ein Wert trägt keinen Typ-Tag"** (`STATUS.md:2329`) — IF-03 ist kein Bruch; IF-14 C wäre einer.
- **`lyric-spec/spec/05-interfaces.md:112-115`** („does not convert … implication holds for the
  implementing type, not for fat pointers") — retiriert mit IF-01 B und IF-02 B, spec-first (§1.7).
- **`05-interfaces.md:126-130`** (§5.4: die Zeile hält die konkrete Auflösung) — **heute verletzt**
  (Befund 2); IF-13 B' ändert den Satz, A erfüllte ihn. Beides braucht erst einen Suite-Fall.
- **`05-interfaces.md:30-36`** (doppelte Konformanz ist eine) — bleibt; IF-33 C ergänzt die Body-Hälfte.
- **`docs/Grammar.md:294-296`** — retiriert mit IF-05.
- **`docs/Bytecode.md:854`** — retiriert mit IF-03.
- **`docs/Bytecode.md:356`, `:364-365`** — heute falsch (Struct links); mit IF-14 A fällt die Zeile
  **oder** der Reader setzt sie durch.
- **`docs/Bytecode.md:845`, `:856`** („object reference") — präzisiert mit IF-14 A.
- **`docs/Bytecode.md:819-823`** — IF-25 A ergänzt den Interface-Fall.
- **`docs/Bytecode.md:891`** — bereits falsch (`p24`, `n21`).
- **`design/conditional-conformance.md:116`** — Rust-Beispiel unmöglich, korrigieren.

**Mit anderen Gebieten dieser Runde:**

| Gebiet | Berührung |
|---|---|
| Generics | IF-04, IF-05, IF-07, IF-18, IF-22, IF-26, **IF-37** (der monomorphisierte Pfad hat seine eigene Auflösungsregel) |
| Structs & Wertsemantik | IF-14, IF-23, IF-24, **IF-45** mit `SPEC-RUNDE.md:57/82/150` in einem Satz |
| Optionals | IF-25 |
| Pattern/Match | IF-03, IF-17, **IF-38** |
| **Overloading** | **IF-39** (Interface-Konvertierung ist eine Stufe; zwei sind gleichrangig) |
| **Funktionen/Parameter** | **IF-36** (eine Formungsphase für Default, `params`, benannte Argumente), IF-15, IF-40 |
| Bytecode/VM | IF-01, IF-03, IF-09, IF-14, IF-21, IF-25, IF-26, IF-30, **IF-41**, **IF-42**, **IF-48** — die vier letzten sind eine Rechnung |
| Module & Pakete | IF-12, IF-21, IF-27, IF-31, **IF-33**, **IF-44** — vor dem Paketmanager |
| **Nebenläufigkeit** | **IF-45** — keine eigene Frage, solange die VM ein Thread ist; wird eine mit Isolation |
| **FFI/Embedding** | **IF-46** — das Format trägt Host-Konformanzen (Imports sind Callables), die Sprache nicht |
| Diagnostik | IF-32, IF-29, IF-19, §1.5.1, **§1.6** (dritte Kategorie „Bruchwarnung", `n50`) |
| Editor-Werkzeuge | IF-28, **IF-47** |
| **Spec/Konformanzsuite** | **IF-43**, §1.7 — jede Regeländerung spec-first; die 4.6-Regel ist ungepinnt |
| stdlib | IF-04 D, IF-07, IF-08, IF-26 |
| Fehler/`throws` | kontravarianter `throws`-Vergleich (`p29b`); `catch` fängt den Großelternteil (`n21`) |
| Testen | IF-42 B (Benchmark in-tree) |

**Was ohne v5 in 4.x gehört** (Fehler, keine Entscheidungen — **korrigiert um die Spec-Pflicht**):
Befund 1 als Familie (IF-36, mit IF-15 A als Regel), IF-02 (**spec-first**, weil §5.3:114 normativ
ist), IF-10, IF-11 A mit IF-22 A (letzteres mit Bruchwarnung wegen `n50`) und IF-35 A, IF-23, §1.5.1,
§1.5.4, IF-31s Warnung, IF-32, **ein Suite-Fall für die 4.6-Regel vor jeder IF-13-Bewegung**,
IF-33 C als Defektwarnung.

---

## 6. Nach der Kritik geändert

**Korrigiert, weil die Kritik recht hatte — jede Messung selbst wiederholt (`audit3/` komplett neu
gelaufen, Ergebnisse identisch):**

- **§0/§1.1/IF-19: „memberlos" → „ohne Slot in der ganzen Kette"** (`n01` druckt 7; `TypeTable.cs:676`
  vor `:644`). Die „yet"-Note ist die generische Kategorie (`LoweringDiagnostics.cs:28`), nicht ein
  Satz in `TypeTable.cs`.
- **Befund 2 / IF-13: drei Empfängerpfade, nicht zwei** (`n28`, `n28b`). Und **schärfer, selbst
  gefunden**: `lyric-spec/spec/05-interfaces.md:126-130` verlangt, dass die Zeile die konkrete
  Auflösung trägt; `lyrc lower p23` zeigt den Default in der Zeile. Befund 2 ist ein Spec-Verstoß;
  die Suite pinnt die 4.6-Regel nicht. Empfehlung von B (Fehler) auf **B' (Kotlins Warnung)**, mit
  der Abwägung, die fehlte: der Fehler erschiene nur beim Konsumenten.
- **Befund 1 / IF-15: Familie „Argumentformung"** — `params` (`n07`, `CLI0020`), Default über den
  Constraint-Pfad (`r04`, dritter Absturz mit eigener Meldung), Kontrollen `r04c`–`r04e`. Neue Frage
  IF-36 an der gemeinsamen Ursache; IF-40 für die Signaturfrage.
- **Befund 3 / IF-14: „unbenannt" und „geboxt" gestrichen.** `FunctionLowerer.cs:3041-3046` benennt
  und begründet die Regel; `mkiface` alloziert nichts. Empfehlung von „B + A" auf **A allein**.
- **IF-16: Abhängigkeit IF-02 statt IF-04** (`n38`: `SEM0059` trotz `Equatable<Shape>`; Kontrolle
  `r01`: `equals` läuft, druckt `true`). Rust/Swift als Vorbild gestrichen — beide liefern `==` nur
  generisch. IF-16 ist keine v5-Frage mehr.
- **§2.3: „einzige Stelle" → zwei.** Zwei Defaults gleichen Namens laufen still auseinander (`n02a`/`n02b`);
  neue Frage IF-34 mit Kotlins „must override".
- **§1.1 Signaturvergleich: zweite Ausnahme** — Default-Argumente werden nicht verglichen (`n30`).
- **IF-22: A bricht laufende Programme** (`n50` druckt 3). Warnstufe ergänzt; dafür in §1.6 die
  dritte Kategorie „Bruchwarnung" benannt.
- **IF-12/IF-27/IF-31: doppelte Konformanz im selben Modul ist still** (`n09`/`n09b`/`n09c`) —
  aber, **selbst gelesen und gegen die Kritik präzisiert**: die Deklarationshälfte ist per Spec
  (§5.1:30-36) absichtlich eine Konformanz, mit Bibliotheks-Adoption als Grund; offen ist nur der
  **Body**. Neue Frage IF-33, Empfehlung C statt Rusts E0119.
- **IF-05 / §2.1 / §4: C# 11 ist Vorbild für B, nicht B'** — Proposal gelesen: nur das Typargument
  ist verboten (CS8920). „Gegenprobe" gestrichen.
- **§2.1 Swift / IF-04: SE-0309 genau** — kovariantes `Self` wird gelöscht und bleibt erreichbar;
  Option B war fälschlich als „halb" verworfen, Option C strenger als das Vorbild. C jetzt in der
  SE-0309-Fassung. Die Metatyp-Behauptung zu statischen Requirements steht außerhalb des Proposals
  und ist als **behauptet** markiert.
- **IF-26 C: kein Rust-Vorbild** (`dyn Trait<T>` war immer erlaubt); C# 11 ist die falsche Analogie.
- **IF-21: Java ist Vorbild für C, .NET für B** (Class-File nennt nur direkte Superinterfaces).
- **IF-07 / `design/conditional-conformance.md:116`: `impl<T: Display> Display for Vec<T>` ist das
  kanonische unmögliche impl**; richtig ist `Debug for Vec<T>`.
- **IF-01: Kostenrechnung ein drittes Mal** — die Matrix ist `Types × Types` (`DispatchTable.cs:52-54`),
  gemessen 48–56 Einträge → 2 304–3 136 Zellen; jeder Vorfahr wird ein Typeintrag (`r08a`/`r08b`: 39 → 41).

**Sechzehn Fragen ergänzt:** IF-33 (doppelte Konformanz, Body), IF-34 (zwei Defaults, Deklaration),
IF-35 (`extend` auf Interface), IF-36 (Argumentformung als Phase), IF-37 (monomorphisierter Pfad),
IF-38 (Join-Typ), IF-39 (Overload-Rang), IF-40 (Default in der Signatur), IF-41 (Matrix-Skalierung,
mit Zahlen), IF-42 (Laufzeitkosten, gemessen in vier Konfigurationen: kein messbarer Unterschied;
Release devirtualisiert und inlint), IF-43 (Spec-first-Pfad, §1.7), IF-44 (`pub` am Interface,
`r07`), IF-45 (Tasks, `r05`), IF-46 (FFI, `r03`), IF-47 (Rename, gelesen), IF-48 (Typeinträge, `r08`).

**Wo die Kritik unrecht oder nur halb recht hatte — die Aussage bleibt oder wird präzisiert:**

- **„Doppelte Konformanzdeklaration: Fehler wie E0119?"** — Die Kritik stellt die Frage, als sei sie
  offen; die Deklarationshälfte ist in `05-interfaces.md:30-36` **entschieden** und begründet
  (Bibliotheks-Adoption), mit Suite-Fall. Nur die Body-Hälfte ist offen (IF-33). Die Kritik hat
  recht, dass das Dossier die Frage nicht hatte; sie irrt in der Annahme, Rusts Antwort sei die
  Kandidatin.
- **„`mkiface` alloziert nichts — die Box ist das `structcopy`"** — richtig, übernommen. Aber die
  Konsequenz „IF-14 argumentiert aus unbenannt, was nicht trägt" ändert die **Frage** nicht: der
  Spec-Satz fehlt weiterhin, und `Bytecode.md:364-365` widerspricht dem Compiler weiterhin. Die
  Empfehlung ist geändert (A), der Befund nicht.
- **„Statische Requirements sind über `type(of: x)` am Existential-Metatyp aufrufbar"** — das
  Proposal SE-0309 sagt dazu nichts (gelesen); die Aussage ist plausibel, aber in dieser Runde nicht
  belegt und steht als **behauptet** in §2.1. Die Kritik hat sie als Tatsache vorgetragen.
- **„Das Dossier hat 32 Fragen und keine einzige Laufzeitzahl"** — richtig; die Zahl steht jetzt
  (IF-42), und sie sagt: der Dispatch kostet nichts Messbares, die Modulgröße ist der Preis. Das
  bestätigt die Richtung von IF-26/IF-41/IF-48, nicht die Sorge um Opcodes in IF-01/IF-03.
- **„n38 läuft und druckt `true`"** — die Datei, wie sie im Probenordner liegt, kompiliert **nicht**
  (`SEM0059` an `a == b`); der Kritiker hat offenbar eine Variante ohne `==` laufen lassen.
  Nachgemessen als `r01`: ohne `==` druckt sie `true`. Der Schluss der Kritik hält, die Probe hielt
  nicht.
- **„Java (`.class` trägt die vollständige Methodentabelle)"** und **„.NET emittiert transitive
  InterfaceImpl-Zeilen"** — beides übernommen, aber in dieser Runde nicht gelesen; als **behauptet**
  markiert, wie es die Belegpflicht verlangt — die Kritik hat diese Stufe nicht gesetzt.

**Von selbst gefunden, unabhängig von der Kritik:**

- **§5.4 der Spec wird vom Compiler verletzt** (Befund 2 ist ein Spec-Verstoß, keine offene Regel),
  und **die Suite pinnt die 4.6-Regel nicht** (kein `since: 4.6.0`-Fall nennt `extend`).
- **Der Constraint-Pfad hat für Default-Argumente einen eigenen dritten Absturz** (`r04`: das Lowering
  liest den Default an der Implementierung, die Sema am Interface).
- **`catch` fängt den Großelternteil** (`n21`, neu gemessen) — `Bytecode.md:891` ist damit doppelt veraltet.
- **§1.7 / IF-43**: die Tabelle, welche Antworten Spec sind — und der Befund, dass die zweite Fassung
  „Bugfix in 4.7" für IF-02 sagte, obwohl `05-interfaces.md:114` normativ das Gegenteil sagt.
