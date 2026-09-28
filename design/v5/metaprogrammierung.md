# Lyric 5 — Gebiet: Attribute, `comptime`, Synthese

Basis: Arbeitskopie `C:/Users/Olivier/CLionProjects/lyric`, Toolchain meldet sich als **4.6.0**
(gemessen, aus dem `LYR-SEM0081`-Text). Alle Messungen mit den vorhandenen Debug-Binaries.
Proben der ersten Runde unter `…/scratchpad/v5-design/probes/meta/` und `…/probes/audit/`,
Proben dieser Überarbeitung unter `…/scratchpad/v5-design/probes/meta-rev/`.

**Belegstufen** — jede Aussage über Lyric 4 trägt eine:
`gemessen` = kompiliert und/oder ausgeführt, Probe benannt · `gelesen` = Pfad:Zeile ·
`behauptet` = weder noch, ausdrücklich so markiert.

**Eine Korrektur der Aufgabenstellung vorweg:** die Vorgabe nennt „`@Deprecated` ohne
`until`-Feld“. Das ist seit 2.13 falsch. `until` existiert, ist spezifiziert und wird
durchgesetzt — gemessen (`probes/meta/a3_until_past.lyr`, in dieser Runde erneut in
`probes/meta-rev/dep/`):

```
@Deprecated { message = "use renew", until = "1.0" }
→ error[LYR-SEM0081]: this was kept until 1.0 and the toolchain is 4.6.0 — remove it, or move the promise out
```

Feld deklariert in `stdlib/std/core.lyr:500-511` (gelesen), Durchsetzung in
`src/Lyric.Frontend/Sema/DeprecationPromise.cs:35-56` (gelesen), dokumentiert in
`docs/guide/15-attributes.md:276-302`. Der offene Punkt in `STATUS.md:2411-2416`
(„`@Deprecated` needs an `until` field before the patch train starts“) ist **erledigt und im
STATUS nicht nachgezogen** — eine STATUS-Leiche, kein Designproblem.

---

## 1. Ist-Stand

### 1.1 Attribute — der Mechanismus

| Eigenschaft | Stand | Beleg |
|---|---|---|
| Ein Attribut **ist** ein Struct | ja, keine eigene Deklarationsform | gelesen, `docs/guide/15-attributes.md:24-39` |
| Platzierung entscheidet **Konformanz**, nicht der Name | `OnModule`/`OnType`/`OnFunction` | gelesen, `stdlib/std/core.lyr:483,486,489` |
| Marker-Konformanz **erbt über die Elternkette** | eigenes `interface Marker :: [OnFunction]` reicht | **gemessen**, `meta-rev/m10_markerchain.lyr` → `ok` |
| Positionsform, genau ein Wert | `:: [WithArg<T>]`, seit 3.9 | gelesen, `stdlib/std/core.lyr:495` |
| `WithArg<T>` reist ebenfalls die Elternkette | seit 3.9.1, `LYR-SEM0095` | gelesen, `CHANGELOG.md:941-945` |
| Gruppe `@[A, B(…)]` | seit 3.9, `fmt` macht sie zur Normalform | gelesen `docs/Grammar.md:152-157`; **gemessen** `meta-rev/m6_fmt.lyr` |
| Werte | Zahl, String, Char, Bool, Unit-Variante, benannter `let` | gemessen, `LYR-SEM0066` |
| Attribut tut **nichts** | inert, Abschnitt 11 ist „skippable“ | gelesen, `docs/Bytecode.md:463-466` |
| Einziges **compilergelesenes** Attribut | `@Deprecated` | gelesen, `docs/guide/15-attributes.md:249-338` |
| `@Test` liest der **Runner**, nicht der Compiler | `module.Attributes.OnFunctions("Test")` | gelesen, `src/Lyrtest/Program.cs:168` |
| Attributierte Funktion überlebt DCE | ja, auch `--release`, auch privat | **gemessen**, `meta-rev/m13_privroot.lyr` (1 Treffer im `lower --release`) gegen Kontrolle `m13b_control.lyr` (0 Treffer) |
| Host-API kann Feldwerte **nach Namen** lesen | `AttributeUse.Value(string)` über Abschnitt 12 | gelesen, `src/Lyric.Core/Bytecode/ModuleAttributes.cs:144-152` |

**Gemessene Zielmenge** (Proben `probes/meta/b4*`–`b5*`, in dieser Runde `meta-rev/m2`–`m5`, `m14`):

| Ziel | Verdikt | Code |
|---|---|---|
| Top-Level-Funktion, Struct, Class, Enum, Modulkopf | erlaubt | — |
| Member (Methode, Feld, `static let`, extend-Methode) | **nur `@Deprecated`** | `LYR-SEM0065` (gemessen, `m14_member.lyr`) |
| generische Deklaration | verboten | `LYR-SEM0067` |
| Interface | verboten: „an attribute cannot sit on an interface“ | `LYR-PAR0042` (gemessen, `m2_dep_interface.lyr`) |
| globales `let` | verboten: „…on a global binding“ | `LYR-PAR0042` (gemessen, `m4_dep_globallet.lyr`) |
| Typalias | verboten: „…on a type alias“ | `LYR-PAR0042` (gemessen, `m3_dep_alias.lyr`) |
| `extend`-Block | verboten: „…on an extend block“ | `LYR-PAR0042` (gemessen, `m5_dep_extend.lyr`) |
| Parameter | verboten | `LYR-PAR0038` |
| Enum-**Variante** | verboten — aber mit **Parser-Rauschen** statt der Familienmeldung | `LYR-PAR0026: expected enum variant name, got AtIdentifier` |
| Rückgabewert, Modul-Assembly | existiert nicht | |

### 1.2 `comptime` — gebaut, und mehr als die Designnotiz behauptet

`design/macros.md:4` nennt `comptime` einen Prototyp auf einem Worktree-Branch. Das ist überholt
(gelesen): der Code steht in `main` (`src/Lyric.Core/ComptimeRunner.cs`,
`src/Lyric.Frontend/Ir/Lowering/ComptimeTable.cs`, `src/Lyric.Vm/VmComptimeRunner.cs`), die
Grammatik führt ihn (`docs/Grammar.md:491`) — und er funktioniert (gemessen,
`probes/meta/a1_comptime.lyr`):

```
let FIB = comptime fib(30);        → Disassembly: const i64 832040;  fib nicht mehr im Modul
println(f"{comptime (60*60*24)}")  → const i64 86400
```

Gemessene Grenzen:

| Fall | Verdikt | Probe |
|---|---|---|
| `string`, `bool`, `char`, `float`, Ganzzahl | erlaubt | `c1_types.lyr` |
| `int[]`, Struct, **Enum-Variante** | `LYR-SEM0100` „…which the compiled module has no way to hold as a literal“ | `meta-rev/m9_ctarray.lyr` |
| Lokal / Parameter in der Site | `LYR-SEM0100` | `g1_comptime_fnname.lyr` |
| Panik in der Auswertung, **drei Frames tief** | `LYR-CT0002 … panicked [LYR-VM0011]: too big`, Span = Site, **kein innerer Frame** | `meta-rev/m7_backtrace.lyr` |
| Capability nötig (`std.io.file.exists`) | `LYR-CT0002 … module requires capability 'fileAccess'` | `d2_cap.lyr` |
| Endlosschleife | `LYR-CT0002 … did not finish within the compile-time budget`, **ohne Zahl** | `meta-rev/m8_budget.lyr` |
| `let comptime = 2; comptime + 3` | bleibt ein Name (kontextuell), kompiliert | `probes/meta/` |
| **`check --emit` wertet aus, `check` nicht** | `check` → `ok`; `check --emit` → `LYR-CT0002` | **gemessen**, `meta-rev/m7`, `m8` |

Der letzte Punkt ist neu und präzisiert L6: die Trennung verläuft nicht zwischen `check` und
`build`, sondern zwischen „mit Runner“ und „ohne Runner“, und `--emit` ist der Schalter
(gelesen, `lyrc --help`: „check: emit the bytes and load them, writing neither“).

### 1.3 Was in diesem Gebiet **gar nicht existiert**

| Feature | Stand | Beleg |
|---|---|---|
| Konformanz-Synthese | nicht gebaut; `struct Coord :: [Equatable<Coord>, Hashable<Coord>, Display] { }` → **drei** `LYR-SEM0020` | gemessen, `audit/e1_synth.lyr`; Design in `design/conformance-synthesis.md` |
| `embed()` / `embedBytes()` | kein Treffer im ganzen Repo | `grep` über `src/`, `stdlib/`, `docs/Grammar.md` |
| Enum-Reflexion (`E.variants()`, `E.fromName()`) | kein Treffer | ebd. |
| Doc-Tests | `src/Lyrtest/Program.cs` kennt nur `@Test` | gelesen |
| **Unterdrückung einer Warnung** | kein Schalter, kein Pragma, kein `lyric.json`-Eintrag | **gemessen**, `lyrc --help` (nur `--deny-warnings`, das Gegenteil) |
| `lyrfix` | existiert nicht | gemessen, `lyric --help` |
| Attribut-Vervollständigung im LSP | Trigger ist nur `.` | gelesen, `src/Lyric.Lsp/LspServer.cs:1018` |
| Laufzeitreflexion | bewusst nein — Wert trägt keinen Typ-Tag | gelesen, `STATUS.md:2296-2299` |
| Makrosystem | bewusst nein | gelesen, `design/macros.md:35`, `PLAN.md:375-377` |

**Aber**: das Rohmaterial für Doc-Tests liegt vor. Der Parser bindet `///`-Blöcke an die
Deklaration darüber (gelesen, `src/Lyric.Frontend/Parsing/ParsedModule.cs:14-31`), und die
Testsuite kompiliert schon heute jedes Snippet des Guides (gelesen,
`tests/Lyric.Tests.Embedding/GuideTests.cs:8,20,76`) — als C#-Test, hartkodiert auf `docs/guide`,
und sie **führt nichts aus**.

**Und**: der LSP sieht Attributnamen bereits für *Rename* und *Referenzen* (gelesen,
`src/Lyric.Lsp/Analysis/NameSpans.cs:35` — `AttributeNode => Valid(attribute.NameSpan)`, benutzt
von `RenameProvider` und `ReferenceProvider`). Was fehlt, ist nur die Entdeckungsrichtung:
Vervollständigung.

### 1.4 Die Löcher — wo Lyric 4 inkonsistent, unvollständig oder still falsch ist

**L1 — Attributnamen kollidieren still, und der Host-API fehlt das Unterscheidungsmerkmal.**
Zwei Module, jedes mit `pub struct Tag :: [OnFunction]`, beide benutzt (gemessen,
`probes/meta/coll/`):

```
attribute @Tag {kind = "x"} -> fn main.one
attribute @Tag {level = 7}  -> fn main.two
```

Das **Format** unterscheidet sie (die Zeile trägt `type` als Index in die Typentabelle, gelesen,
`docs/Bytecode.md:434`). Die **Host-API wirft den Index weg**: `ModuleAttributes.Of` baut
`AttributeUse` aus `module.Types[row.Type].Name` und reicht den Index nicht durch (gelesen,
`src/Lyric.Core/Bytecode/ModuleAttributes.cs:31-44`, `AttributeUse` ab `:108`). Ein Host kann zwei
gleichnamige Attribute aus verschiedenen SDKs **nicht auseinanderhalten** — nur an den Feldnamen
raten.

*Korrektur gegenüber der ersten Fassung:* die erste Fassung schrieb, `docs/guide/15-attributes.md:246`
*behaupte* Eindeutigkeit. Das ist eine Fehllektüre. Der Guide sagt das Gegenteil, offen:
„Attribute names are unqualified in the compiled module: `System`, not `engine.ecs.System`.
An SDK owns its attribute names the way it owns its native names“ (gelesen, 15:245-247), und
`ModuleAttributes.cs:11-13` dokumentiert die Kollision ebenso ausdrücklich („two same-named
attribute types from different modules land under one name here“). **Der Befund L1 bleibt, das
Belegzitat war falsch gewählt:** die Lücke ist eine bewusst dokumentierte Einschränkung, kein
gebrochenes Versprechen — was sie nicht harmloser macht, aber anders einordnet.

**L2 — `lyric test` führt jedes Attribut namens `Test` aus, egal wer es deklariert.**
Gelesen: `src/Lyrtest/Program.cs:168` fragt `module.Attributes.OnFunctions("Test")`. Gemessen:
eine Datei, die `std.test` **nicht importiert** und ein eigenes `pub struct Test :: [OnFunction] { }`
deklariert, wird ausgeführt:

```
FAIL mine.notReallyATest: but the runner ran me
1 test(s), 1 FAILED
```

Kontrolllauf ohne das Attribut: `0 test(s), all passed` (`probes/meta/tst/`). Das ist L1, im
mitgelieferten Werkzeug eingelöst.

**L3 — `OnMethod` ist ausgelieferte, unbenutzbare Vokabel.** `stdlib/std/core.lyr:527-530`
exportiert das Interface „so a library can name it today“ (gelesen). Gemessen
(`meta-rev/m14_member.lyr`): ein Attribut `:: [OnMethod]` an einer Methode ist
`LYR-SEM0065: '@Other' cannot sit on a member — only '@Deprecated' may` — **wortgleich** mit dem
Fehler für ein Attribut, das `OnFunction` deklariert. Der Marker hat null Wirkung.

**L3a (neu, gemessen) — `OnMethod` kann seine eigene Uhr nicht stellen.** Die erste Fassung
schlug vor, `OnMethod` mit `@Deprecated { until = "5.0" }` auslaufen zu lassen. Das ist
**syntaktisch unmöglich**: `OnMethod` ist ein Interface, und ein Attribut auf einem Interface ist
`LYR-PAR0042` (gemessen, `meta-rev/m2_dep_interface.lyr`). Die Deprecation-Uhr der Sprache
erreicht genau die Deklarationsarten nicht, die die 5.0-Liste ersetzen will. Siehe META-24.

**L4 — `@NonExhaustive` verspricht eine Regel, die es nicht gibt, und niemand trägt es.**
`stdlib/std/core.lyr:532-536` sagt „the same contract Rust's `#[non_exhaustive]` states“
(gelesen). Gemessen: ein `match` über ein `@NonExhaustive`-Enum in einem anderen Modul, ohne
`_`-Arm, kompiliert anstandslos (`probes/meta/ne/`).

*Schärfung gegenüber der ersten Fassung (gemessen):* `grep -rn NonExhaustive stdlib/ docs/guide/`
liefert **genau einen** Treffer — die eigene Deklaration `stdlib/std/core.lyr:536`. Auch
`Wait` und `IoErrorKind`, die zwei Enums, die der eigene Doc-Kommentar nennt, tragen es **nicht**.
Der Bruch entsteht also nicht, wenn die REGEL kommt, sondern wenn std seine Enums das Attribut
**tragen** lässt. Eine Warnung über `_`-lose `match`-Ausdrücke warnt heute über nichts.

**L5 — Zwei Begriffe von „Wert zur Compile-Zeit“, die sich nicht decken** (gemessen):

| | Attributwert (`SEM0066`) | `comptime`-Ergebnis (`SEM0100`) |
|---|---|---|
| Zahl, String, Char, Bool | ja | ja |
| **Unit-Enum-Variante** | **ja** | **nein** |
| benannter `let` | ja | (Namen im Rumpf erlaubt) |
| berechneter Ausdruck | nein | ja — das ist der Zweck |

`@Retry { limit = comptime (1 + 2) }` ist `LYR-SEM0066`, mit exakt derselben Meldung wie
`@Retry { limit = 1 + 2 }` (Kontrolllauf, `a2b_attr_expr.lyr`): die Meldung erwähnt `comptime`
nicht einmal.

**L6 — Ohne Runner ist die Prüfung grün, wo der Build rot ist.** Gemessen (`meta-rev/m7`, `m8`):
`lyrc check` sagt `ok`, `lyrc check --emit` und `lyrc build` melden `LYR-CT0002`. Das ist die
gewollte Folge von „der Editor führt nichts aus“ (gelesen, `design/macros.md:70,75`).

*Ergänzung gegenüber der ersten Fassung:* für den Fall „kein Runner“ existiert **bereits ein
Diagnose-Code**, `LYR-CT0001` (gelesen, `design/macros.md:70,77`; ein Test prüft ihn,
`docs/Befunde_und_Verbesserungen/deliverables/macro-abi/examples/ComptimeTests.cs:172`). Die in
META-11 A vorgeschlagene Note ist damit billiger als veranschlagt — der Code ist vergeben, er
erreicht nur den `check`-Pfad nicht.

**L7 — Der `comptime`-Bruch ist gemessen, und seine Diagnose führt in die Irre.**

```lyr
fn comptime(n: int): int { return n + 1; }
… comptime(k) …
→ error[LYR-SEM0100]: 'k' is a local of the enclosing function and cannot be used
                      in a 'comptime' expression — only module-level names can
```

(gemessen, `g1_comptime_fnname.lyr`; Kontrolllauf mit `plusOne` statt `comptime`: `ok`.)

**L8 — Kleinkram in den Diagnosen.** `@Tag(5) { m = 1 }` (beide Formen zugleich) →
`LYR-PAR0042: an attribute must be followed by the declaration it applies to` (gemessen,
`meta-rev/m12_bothforms.lyr`). `docs/guide/15-attributes.md:159` kennt den Satz dafür
(„`@On(…) { … }` is not a spelling“); der Compiler sagt ihn nicht. Und die Enum-Variante aus 1.1
fällt aus der `PAR0042`-Familie heraus.

**L9 (neu, gemessen) — die Deprecation-Uhr bricht den Build des Konsumenten, nicht den des
Eigentümers.** `probes/meta-rev/dep/`: ein Modul `expired` mit `@Deprecated { until = "1.0" }`
auf `stale`. Der Konsument importiert **nur `fresh`** und ruft `stale` nie auf:

```
…/expired.lyr:5:1: error[LYR-SEM0081]: this was kept until 1.0 and the toolchain is 4.6.0
```

Der Fehler steht in einer Datei, die der Konsument nicht besitzt, hat keinen Ausweg, und er
erscheint **schon beim Import des Moduls** — nicht erst bei der Benutzung. Kontrolllauf mit
Benutzung (`consumer2.lyr`): zusätzlich `LYR-SEM0076` als Warnung an der Aufrufstelle, der
`SEM0081`-Fehler bleibt. Der Doc-Kommentar der Implementierung setzt ausdrücklich voraus, die
Meldung lande „on the maintainer preparing the release“ (gelesen,
`src/Lyric.Frontend/Sema/DeprecationPromise.cs:15-18`) — mit Paketen aus Quelltext (v5-Liste,
Stufe A) stimmt diese Voraussetzung nicht mehr.

**L10 (neu, gemessen) — es gibt keine Form, eine Warnung bewusst hinzunehmen.** `lyrc --help`
kennt `--deny-warnings` (macht sie strenger) und sonst nichts; kein `@nowarn`, kein `#[allow]`,
kein `NoWarn` in `lyric.json` (gelesen, `docs/guide/16-building.md:305-318` listet die Schlüssel).
`LYR-SEM0076`/`LYR-SEM0081` werden unbedingt gemeldet (gelesen,
`src/Lyric.Frontend/Sema/DeprecationPromise.cs:41,53`). Ein Shim, der die alte Form kapselt,
warnt über sich selbst.

**L11 (neu, gemessen) — `LYR-CT0002` hat keinen Backtrace.** `meta-rev/m7_backtrace.lyr`:
`comptime level1(5)` → `level2` → `level3` → `panic("too big")`. Die Diagnose nennt die Site und
den Text, **keine einzige Zeile aus den drei gerufenen Funktionen**. `design/macros.md:75` führt
„Backtrace als Notes der Diagnose“ selbst als Erweiterung, `:107` als „Was fehlt“ (gelesen).

**L12 (neu, gemessen) — die `LYR-PAR0042`-Meldung verspricht mehr, als `LYR-SEM0065` hält.**
Der Satz lautet: „a function, a struct, a class, an enum, **a member of one**, or the module
header carries one“ — und unmittelbar danach verweigert `LYR-SEM0065` jedes Member-Attribut außer
`@Deprecated` (beides gemessen, `m2`–`m5` und `m14`). Zwei Diagnosen derselben Familie
widersprechen einander.

**L13 (neu, gemessen) — `lyric fmt` schreibt die Attribut-Schreibweise schon heute um.**
`meta-rev/m6_fmt.lyr`: `@A` + `@B { n = 1 }` untereinander wird zu `@[A, B { n = 1 }]`.
`lyrfmt --check` „exit 1 when any would [change]“ (gelesen, `lyrfmt --help`) ist ein CI-Gate.
Jede Migration, die der Formatter übernimmt, macht die CI rot, bevor jemand migrieren wollte.

**L14 (neu, gemessen) — die Determinismus-Zusage deckt Gleitkomma-Intrinsics nicht.**
`docs/guide/16-building.md:294-296` sagt dem Nutzer: „A site is deterministic. The language's
arithmetic is specified to the bit and the only non-deterministic draws are gated, so the same
source yields the same literal on every machine — a build stays reproducible.“ Gemessen
(`meta-rev/m1_ctfloat.lyr`, Disassembly `lyric disasm m1.lyrbc`):

```
let S: float = comptime sin(1.0);                  → const f64 0.8414709848078965
let P: float = comptime pow(1.0000001, 100000.0);  → const f64 1.0100501665850403
```

`sin`, `pow`, `log`, `tan`, `asin` … sind capability-freie Intrinsics ohne Rumpf (gelesen,
`stdlib/std/math.lyr:20,39,42,50`), gebunden an `Math.Sin`/`Math.Pow`/`Math.Log` der
Host-Laufzeit (gelesen, `src/Lyric.Vm/NativeRegistry.cs:626,629,658`). Die einzige
Nichtdeterminismus-Sperre ist eine Liste mit **einem** Eintrag, `std.random.secureRandom`
(gelesen, `src/Lyric.Vm/VmComptimeRunner.cs:35`). Die Spezifikation sagt über Gleitkomma nur das
**Speicherformat** (gelesen, `docs/Bytecode.md:80,441,665-666`); über Ergebnisse transzendenter
Funktionen sagt sie nichts. Kontrolllauf (`m1b_rtfloat.lyr`, dieselben Aufrufe zur Laufzeit):
identische Werte — der comptime-Pfad ist also derselbe Host-Aufruf, nur früher.

**Was ich damit *nicht* gemessen habe** und deshalb nicht behaupte: dass zwei Maschinen
verschiedene Bits liefern. Ich habe eine Maschine. Gemessen ist: der Wert kommt aus der
Host-Mathematik der **kompilierenden** Maschine, und weder Spec noch Runner sagen etwas über
seine Portabilität. Die Zusage in Guide 16 ist damit **unbelegt**, nicht widerlegt. Siehe META-37.

**L15 (neu, gemessen) — `lyric test` sieht nur die öffentliche Oberfläche.** Projektprobe
`meta-rev/proj/`: ein Testmodul, das eine nicht-`pub` Funktion des Quellmoduls importiert, ist
`LYR-RES0004: 'hidden' is not public in 'mathy'`; Kontrolllauf mit nur dem `pub`-Import:
`1 test(s), all passed`. Lyric fährt damit heute bereits das **Rust-Modell** (der Test ist ein
fremdes Modul), festgeschrieben in `docs/guide/20-testing.md:51-54`. Das ist der Maßstab, an dem
sich ein Doc-Test messen lassen muss — siehe META-28.

---

## 2. Sprachvergleich

> Die Vergleichszeilen dieser Fassung sind gegenüber der ersten an sechs Stellen korrigiert; was
> korrigiert wurde, steht in §6.

### 2.1 Attribute / Annotationen

| Sprache | Form | Ziele | Wer liest | Preis |
|---|---|---|---|---|
| **C#** | Klasse `: Attribute`, `[AttributeUsage(Targets, AllowMultiple, Inherited)]` — das Attribut beschreibt sich **selbst** mit einem Attribut | Bitmaske über ~15 Ziele inkl. Parameter, Rückgabewert, Assembly, Generic-Parameter | Laufzeit-Reflexion, **oder** inerte Metadaten ohne Ausführung (`System.Reflection.Metadata`, `MetadataLoadContext`) | Reflexionspfad verlangt AOT-/Trimming-Steuer (`[DynamicallyAccessedMembers]`) |
| **Java** | `@interface`, `@Target({METHOD,TYPE})`, `@Retention(SOURCE/CLASS/RUNTIME)`, `@Inherited` | breit | je nach `@Retention`: Compiler, Classfile oder Laufzeit; `RUNTIME` ist **reines Datum im Classfile**, von `javap`/Spring/Annotation-Processor lesbar | drei Lebensdauern = drei Vokabeln für eine Sache |
| **Rust** | `#[attr]`; benutzerdefinierte nur als **proc-macro**, also codeerzeugend; inerte nur über `register_tool` | Items, Statements, `match`-Arme, Struct-Felder, Funktionsparameter — **Attribute auf beliebigen Ausdrücken sind bis heute instabil** (`stmt_expr_attributes`) | der Compiler bzw. das Makro | ein Attribut ist kein Datum, sondern ein Programm — ungesandboxt im Compiler |
| **D** | UDAs: `@(42) @("hi") @MyType(1) int x;` — **beliebiger** CTFE-Ausdruck, beliebiger Typ, an fast jeder Deklaration; gelesen mit `__traits(getAttributes, …)` | breit | **Compile-Zeit**-Code | keine Namensklasse, keine Zielprüfung: `@(42)` ist ein gültiges Attribut auf allem |
| **Swift** | feste eingebaute Menge (`@available`, `@main`, `@objc`); **benutzerdefinierte Attribut-Schreibweisen seit 5.1** über `@propertyWrapper`, `@resultBuilder` (5.4), `@dynamicMemberLookup`, `@dynamicCallable`; freie Attribut-**Semantik** erst mit Makros (5.9) | je Attribut definiert | Compiler | die Schreibweise ist erweiterbar, die Bedeutung nicht — der Nutzer wählt aus vier Formen |
| **Zig** | **gibt es nicht**. Modifikatoren (`pub`, `export`, `inline`, `align`, `callconv`) sind Syntax | — | — | kein Weg, einem Host etwas über eine Deklaration mitzuteilen, außer über ein exportiertes Symbol |
| **Nim** | Pragmas `{.inline, deprecated: "…".}` — und ein Pragma **darf ein Makro sein** | breit | Compiler oder Makro | wie Rust: Daten und Codeerzeugung teilen sich eine Syntax |
| **Scala** | `@deprecated(msg, since)`, `@nowarn`; Annotationen als Klassen | breit | Compiler / JVM-Laufzeit | JVM-Reflexion als Fundament |
| **Lyric 4** | Struct + Marker-Interface; `@Name { f = v }` oder `@Name(v)` mit `WithArg<T>` | Funktion, Struct/Class/Enum, Modulkopf; Member nur `@Deprecated` (gemessen) | ein **externer** Leser (Host, `lyrtest`) über Abschnitt 11 | die schmalste Zielmenge der Liste |

**Lyrics Position — präzisiert.** Die erste Fassung schrieb, Lyric sei „das einzige System der
Liste, in dem ein benutzerdefiniertes Attribut garantiert nur Datum ist und von einem externen
Leser konsumiert wird“. Das ist zu stark: eine Java-Annotation mit `@Retention(RUNTIME)` ist
ebenfalls inertes Datum im Classfile, und ein C#-Attribut liegt als Metadaten im Assembly und ist
ohne Ausführung lesbar. Das **tatsächliche** Alleinstellungsmerkmal ist schwächer und präziser:

> Der Leser braucht weder Reflexion noch ein geladenes Typsystem noch eine Laufzeit — die Zeilen
> stehen in einem selbstbeschreibenden, als Ganzes überspringbaren Abschnitt (gelesen,
> `docs/Bytecode.md:425-477`), und die Entscheidung, ob fremde Bytes überhaupt geladen werden,
> fällt **vor** dem Binden (gelesen, `ModuleAttributes.cs:7-10`).

So formuliert trägt es. Die Zielmenge bleibt die **schmalste** der Liste — schmaler als alles
außer Zig, das gar keine hat.

**Die entgegengesetzte Entscheidung: D.** D lässt als Attribut jeden CTFE-Ausdruck jeden Typs zu
und gibt dem Compile-Zeit-Code (nicht einem Host) den Lesezugriff. Gewinn: `@(Name("id"))
@(Range(0,10)) int x;` — ein Validator, ein ORM oder ein Serializer wird zur Bibliothek, ohne dass
die Sprache etwas davon wissen muss. Preis: es gibt keinen Weg, die *Menge* der Attribute zu
kennen oder zu prüfen, und ein Host außerhalb des Compilers sieht davon nichts. Lyric tauscht
genau andersherum: eine winzige Wertsprache, dafür eine Zeile im `.lyrbc`, die jeder lesen kann,
ohne zu kompilieren.

### 2.2 Deprecation

| Sprache | Form | Versionsfeld | Wird es erzwungen? | Fix-it | **Unterdrückbar?** |
|---|---|---|---|---|---|
| **Lyric 4** | `@Deprecated { message, until }` | `until`, verglichen gegen die **Toolchain-Version** (gelesen, `DeprecationPromise.cs:49-56`) | **ja** — `LYR-SEM0081` an der Deklaration (gemessen) | nein | **nein** (gemessen, L10) |
| **Swift** | `@available(*, deprecated, message:, renamed:)` — versionslos beim Wildcard `*`; versionierte `deprecated:`/`obsoleted:` verlangen eine **benannte Plattform**: `@available(macOS, deprecated: 10.15, obsoleted: 11.0)` | pro Plattform | ja — `obsoleted` ist **Fehler** | **ja**, `renamed:` erzeugt ein anwendbares Fix-it | ja (`@available`-Kontext, `#if`) |
| **C#** | `[Obsolete(msg, error: bool)]`, seit .NET 5 `DiagnosticId`, `UrlFormat` | nein | nur per `error: true` | nein | **ja** — `#pragma warning disable`, `<NoWarn>`, pro ID |
| **Rust** | `#[deprecated(since = "1.2.0", note = "…")]` | `since` | **nein** — rein dokumentarisch, rustc vergleicht nichts | nein | ja (`#[allow(deprecated)]`) |
| **Scala** | `@deprecated(message, since)` | `since` | nein | nein | ja (`@nowarn`) |
| **Nim** | `{.deprecated: "msg".}` | nein | nein | nein | ja (`--warning[Deprecated]:off`) |
| **D** | `deprecated("msg")` als Storage-Class; `-de` macht daraus einen Fehler | nein | global per Schalter | nein | ja (Schalter) |
| **Julia** | `Base.@deprecate old new` erzeugt einen **Laufzeit**-Shim | nein | nein | — | ja (`--depwarn=no`) |

**Korrigiertes Fazit.** Die erste Fassung schrieb „Lyric ist hier schon besser als Rust, Scala,
Nim und C#“. Auf der Achse **Erzwingung** stimmt das. Auf der Achse, die darüber entscheidet, ob
eine Uhr *lebbar* ist, stimmt es gegen C# **nicht**: C# hat `DiagnosticId` plus
`#pragma warning disable` plus `<NoWarn>`, also einen Weg für den Aufrufer, die Warnung bewusst
hinzunehmen. Lyric hat davon nichts (L10). Und Swifts Versionsvergleich ist **pro Plattform** —
die Frage, gegen welche Version `until` vergleicht, wenn mehrere Toolchains oder Ziele im Spiel
sind, umgeht Lyrics einzelner String stillschweigend (gelesen: verglichen wird immer gegen
`ToolchainVersion.Value`, `DeprecationPromise.cs:49`).

Die zwei fehlenden Spalten sind damit benannt: **Fix-it** (META-17) und **Unterdrückung**
(META-22). `lyric-v5-features.md:61` verlangt ein `lyrfix` für die 5.0-Migration — die
Datenquelle dafür fehlt ebenso wie das Ventil.

### 2.3 Compile-Zeit-Auswertung

| Sprache | Form | Was darf herauskommen | Sandbox | I/O |
|---|---|---|---|---|
| **Lyric 4** | `comptime e` (Präfix, kontextuell) | Skalar, `bool`, `char`, `string` | **Capability.None + Instruktionsbudget**, `secureRandom` per Import-Name verweigert | keins |
| **Zig** | `comptime` an Parametern, Variablen, Blöcken; Typen sind Werte | fast alles, inkl. Typen | kein I/O; Quota per `@setEvalBranchQuota` | `@embedFile` über den Build-Graph |
| **Rust** | `const fn`, `const { }`-Blöcke, `const`-Items | fast alles (Structs, Arrays, Enums); `panic!` wird Compile-Fehler; **const-eval druckt einen Auswertungs-Stack** | kein I/O | `include_str!`/`include_bytes!`/`env!` als eingebaute Makros |
| **D** | CTFE — **jede** Funktion, sobald der Kontext eine Konstante verlangt, ohne Markierung | Arrays, Structs, Strings | kein I/O | `import("datei")` als String-Import, nur über `-J`-Pfad |
| **Nim** | `const`, `static:`-Block, NimVM | fast alles | **keine** — `staticRead` liest Dateien, `staticExec` startet **Shell-Kommandos** | beliebig |
| **Scala 3** | `inline def`, `transparent inline`, `scala.compiletime.constValue` | Werte und Typen | Makro-Code läuft im Compiler | über Makros beliebig |
| **C#** | nur `const`-Felder (Primitiv/String/Enum) + Konstantenfaltung; darüber **Source Generators** | Konstanten; Generatoren erzeugen Dateien | Generatoren laufen **ungesandboxt** im Compiler | beliebig |
| **Julia** | `@generated`, Makros zur Parse-Zeit | alles | keine | beliebig |

**Die entgegengesetzte Entscheidung: Nim.** `staticExec("git rev-parse HEAD")` zur Compile-Zeit
ist in Nim eine Zeile. Gewinn: Versionsstempel, Codegenerierung aus einem Schema, Konfiguration
aus der Umgebung — alles ohne Build-System. Preis: ein Build ist weder reproduzierbar noch
vertrauenswürdig; `nimble install` kann beim Kompilieren beliebigen Code ausführen.

**Lyrics Position — korrigiert.** Die erste Fassung schrieb, `Capability.None` plus Budget plus
spezifizierte Bit-Arithmetik hieße, „ein `comptime`-Wert ist auf jeder Maschine derselbe“, und
machte das zum Hauptargument gegen Nim. Diese Zusage ist für **Gleitkomma-Intrinsics unbelegt**
(L14, gemessen): `comptime sin(1.0)` backt das Ergebnis der Host-Mathematik ein, die Spec sagt
über transzendente Ergebnisse nichts, und die Sperrliste des Runners hat einen Eintrag. Was
**bleibt** und weiterhin gegen Nim trägt: keine Dateien, keine Prozesse, keine Umgebung, kein
Netz, kein `secureRandom`, und ein Budget statt einer Zeit. Das ist immer noch die schärfste
Sandbox der Liste — nur eben eine Sandbox, keine Bitgleichheitszusage. Siehe META-37.

Bemerkenswert: **D braucht für CTFE gar keine Markierung**. Wo der Kontext eine Konstante
fordert, wird ausgewertet. Gewinn: null Syntax. Preis: ob eine Zeile den Compiler 3 ms oder 30 s
kostet, sieht man ihr nicht an.

### 2.4 Synthese / Derive

| Sprache | Form | Erweiterbar? | Regeln sichtbar? | Preis |
|---|---|---|---|---|
| **Swift** | Konformanz ohne Body → `Equatable`, `Hashable`, `Codable`, `CaseIterable`, `RawRepresentable`; **`Comparable` nur für ENUMS** (SE-0266, Swift 5.3, und nur wenn alle Payloads `Comparable` sind) — **nie für Structs** | **nein**, geschlossene Compiler-Liste | in der Sprachdoku | ein Nutzer kann keine eigene Ableitung schreiben |
| **Rust** | `#[derive(…)]`; die **std-Derives** (`Clone`, `Copy`, `Debug`, `PartialEq`, `Eq`, `PartialOrd`, `Ord`, `Hash`, `Default`) sind **Compiler-Builtins** (`rustc_builtin_macros`), nur *benutzerdefinierte* Derives sind proc-macros | **ja**, jede Crate kann ein `derive` anbieten | nein — `cargo expand` nötig | fügt Bounds implizit hinzu (`T: Clone` auch wo unnötig); **ungesandboxtes Makro nur für die Erweiterbarkeit**, nicht für den std-Teil |
| **Scala 3** | `case class C(…) derives Eq, Show` + `Mirror.ProductOf[C]` | **ja**, `derived`-Methode auf dem Typeclass-Companion, rein in der Sprache | ja, die Ableitung ist gewöhnlicher Scala-Code | Typebenen-Programmierung, schwer zu lesen |
| **C#** | `record` / `record struct` | nein | ja, spezifiziert | alles-oder-nichts — aber **schmäler als oft behauptet**: `Deconstruct` entsteht nur für **Positionsrecords**; `record C { public int X { get; init; } }` bekommt `Equals`, `GetHashCode`, `ToString`, `with`, aber kein `Deconstruct` |
| **Kotlin** | `data class` | nein | ja | dito |
| **D** / **Nim** | von Hand über `__traits(allMembers)` bzw. `fieldPairs` + Mixin/Makro | ja | nein | Bibliotheks-Code erzeugt Code |
| **Go** | `==` strukturell eingebaut, kein Hash-Interface | — | ja | ein Slice-Feld macht den ganzen Typ unvergleichbar — sichtbar erst an der Verwendung |
| **Zig** | keine; `inline for (@typeInfo(T).@"struct".fields)` in der Bibliothek (seit 0.14 kleingeschriebene Tags) | ja | nein | Fehlermeldungen kommen aus dem Innern von `std` |

Für Lyric ist der interessante Neuzugang gegenüber `design/conformance-synthesis.md:128-135`
**Scala 3**: dort ist die Ableitung *bibliothekserweiterbar* und trotzdem kein Makro — der
Compiler liefert nur den `Mirror` (Feldnamen und Feldtypen als Typebene), die Ableitungsregel ist
gewöhnlicher Code. Das ist der Mittelweg zwischen Swifts geschlossener Tabelle und Rusts
proc-macros, und der einzige in der Liste, der `ToJson`/`FromJson` nicht zu einer weiteren
Compiler-Zeile macht.

**Wichtig für META-14:** Swift ist **kein** Präzedenzfall für ein synthetisiertes `Ordered` auf
einem **Struct**. Wer die Vierertabelle mit `Ordered` für Structs will, entscheidet das ohne
Vorbild in der Liste — Rusts `#[derive(PartialOrd, Ord)]` auf Structs ist das nächstliegende,
und das ist ein Builtin-Derive, kein bodyloser Konformanzeintrag.

### 2.5 Reflexion über Enums, `embed`, Doc-Tests

| | Enum-Aufzählung | Datei einbetten | Doc-Tests |
|---|---|---|---|
| **Lyric 4** | keine | keine | keine (aber `///` ist geparst, und der Guide wird kompiliert) |
| **Rust** | nichts in `std`; `strum` derived `EnumIter`/`FromStr` | `include_str!`, `include_bytes!` | **`cargo test` führt jeden ```-Block in `///` aus**; `ignore`, `no_run`, `should_panic`, `#`-versteckte Zeilen — und jeder Block ist eine **fremde Crate**, sieht also nur `pub` |
| **Swift** | `CaseIterable` → `allCases`, synthetisiert | nein (Ressourcen über SwiftPM) | nein |
| **C#** | `Enum.GetValues`, `Enum.Parse` — Laufzeitreflexion | Embedded Resources im Assembly | nein |
| **Zig** | `@typeInfo(E).@"enum".fields` zur Compile-Zeit | **`@embedFile("…")`** | `test`-Blöcke im Quelltext; autodoc zeigt sie als Beispiele |
| **D** | `__traits(allMembers, E)` | `import("datei")` (nur mit `-J`) | **documented unittests**: ein `unittest`-Block hinter `///` wird kompiliert, ausgeführt **und** in die Doku gedruckt — **im Modul**, sieht also alles |
| **Julia** | `instances(E)`; volle Laufzeitreflexion | `read` zur Laufzeit | `Documenter.jl` führt ```jldoctest-Blöcke aus (Paket, nicht Kern) |
| **Nim** | `EnumType.low..EnumType.high`, `$` eingebaut | `staticRead` | `runnableExamples` — kompiliert, ausgeführt, in die Doku gedruckt, **im Modul** |

Bemerkenswert: **drei** Sprachen der Liste (Rust, D, Nim) haben Doc-Tests im Kern-Werkzeug — und
sie zerfallen in zwei Lager, die sich in der **Sichtbarkeit** unterscheiden, nicht im Werkzeug.
Rust: fremde Crate, nur `pub`, der Doc-Test ist damit ein Benutzbarkeits-Test. D/Nim: im Modul,
sieht alles, der Doc-Test ist damit ein Beispiel. Lyric fährt für `lyric test` heute schon das
Rust-Lager (gemessen, L15) — wer das D-Modell für Doc-Tests wählt, führt eine **zweite**
Sichtbarkeitsregel ein. Siehe META-28.

---

## 3. Designfragen

### META-01 — Attributnamen sind unqualifiziert und kollidieren still

**Heute:** die Zeile trägt einen Typindex (gelesen, `docs/Bytecode.md:434`), die Host-API
reduziert ihn auf den unqualifizierten Typnamen (gelesen,
`src/Lyric.Core/Bytecode/ModuleAttributes.cs:31-44`). Gemessen: zwei `Tag` aus zwei Modulen,
beide als `@Tag` im Modul (`probes/meta/coll/`); `lyric test` führt ein fremdes `@Test` aus
(`probes/meta/tst/`). Die Einschränkung ist **dokumentiert**, nicht verschwiegen (gelesen,
`docs/guide/15-attributes.md:245-247`, `ModuleAttributes.cs:11-13`).

**Optionen**
- **A — Zeile trägt den qualifizierten Namen** (`engine.ecs.System`). Vorbild: Java
  (Classfile hält den Binärnamen), C# (`Namespace.Attribute`). Preis: Formatbruch (4.0 → 5.0),
  jeder Host-Aufruf `OnFunctions("Test")` wird zu `OnFunctions("std.test.Test")`, ein Erato-Host
  muss mit.
- **B — Zeile bleibt unqualifiziert, aber `AttributeUse` reicht den Typindex durch** und bekommt
  ein `Module`-Feld aus einer neuen Herkunftsspalte. Preis: eine additive Formatspalte; die
  API-Frage ist gelöst, die *Quelltext*-Frage (zwei `Tag` im selben Modul importiert) bleibt beim
  Importsystem.
- **C — nichts ändern, aber den Werkzeugen eine Prüfung geben**: `lyrtest` verlangt, dass das
  `Test` aus `std.test` stammt, sonst Warnung. Preis: löst nur `lyrtest`, nicht das Muster.
- **D — Kollision ist ein Fehler**: zwei verschiedene Attributtypen gleichen Namens in einem
  Modul → Diagnose. Preis: hilft nicht über Modulgrenzen, wo der Host liest.

**Empfehlung: A für das Format, B für die API — zusammen.** Der qualifizierte Name kostet Bytes
im Stringpool und sonst nichts, und er ist die einzige Option, die L2 wirklich schließt. Die
API-Änderung allein reicht nicht, weil `lyrtest` und ein fremder Host nach einem *Namen* fragen,
nicht nach einem Index.

*Korrektur der Begründung:* die erste Fassung stützte A auf die Behauptung, Guide 15:246 verspreche
schon heute Eindeutigkeit. Tut er nicht (siehe L1). Die Begründung ist damit rein prospektiv —
**„wir wollen, dass es so wird“, nicht „es steht schon so da“** — und das ist ein schwächeres,
aber ehrliches Argument.

**Bricht:** major (Format 5.0 + jede Host-Abfrage). **4.x-Warnstufe:** `lyrtest` warnt, wenn das
gefundene `Test` nicht aus `std.test` kommt; der Compiler warnt bei zwei gleichnamigen
Attributtypen in einem Modul.
**Hängt ab von:** Modulsystem, Embedding-Gebiet, Bytecode-Gebiet. Die Übergangsfrage steht als
eigene Frage in META-36.

---

### META-02 — Welche Ziele nimmt ein Attribut in v5?

**Heute:** Funktion, Struct, Class, Enum, Modulkopf. Member nur für `@Deprecated`
(`LYR-SEM0065`, gemessen `m14_member.lyr`). Interface, `extend`, Typalias, globales `let`,
Parameter, Enum-Variante: alle verboten (gemessen, `m2`–`m5`, Tabelle in 1.1). Begründung im
Fehlertext: „the module format has no member rows for anything else“.

**Optionen**
- **A — bleiben wie heute.** Vorbild: niemand. Preis: `std.cli` mit `@Flag` pro Feld
  (v5-Liste B5), `@Skip`/`@Rename` für JSON-Synthese und `@Bench`-Varianten sind nicht baubar.
- **B — Felder und Enum-Varianten dazu**, mit Zeilen im Format (Zielkind 3 = Feld, 4 = Variante,
  Ziel = Typindex + Feldindex). Vorbild: C# `AttributeTargets.Field`, Java `ElementType.FIELD`.
  Preis: **siehe Kostenkorrektur unten**, plus Sema-Arbeit und die Frage, ob eine Feldzeile bei
  Monomorphisierung eindeutig ist (`SEM0067` verbietet generische Ziele).
- **C — B plus Methoden** (der heute versprochene `OnMethod`-Sitz). Preis: eine Methodenzeile
  braucht einen stabilen Methodenindex, den das Format nicht hat, solange Devirtualisierung und
  Inlining darüberlaufen.
- **D — Ziele als Bitmaske statt als drei Interfaces.** Vorbild: C# `[AttributeUsage]`, Java
  `@Target`. Preis: entweder ein selbstanwendendes Attribut (C#-Rekursion) oder ein neues
  Schlüsselwort. **Gegenargument neu gemessen:** ein SDK kann heute schon seinen eigenen Marker
  bauen, weil die Konformanz die Elternkette hochreist (`meta-rev/m10_markerchain.lyr` → `ok`) —
  die drei Interfaces sind also nicht nur hässlich, sondern erweiterbar.

**Kostenkorrektur (die erste Fassung war hier falsch).** Sie schrieb „Bricht: nein (additiv), wenn
die neuen Zeilenarten skippable bleiben“. Das stimmt nicht: `docs/Bytecode.md:471` verlangt
wörtlich „A reader must reject: **an unknown target kind** …“. Eine neue `targetKind` 3/4 ist
nicht zeilenweise überspringbar — ein 4.x-Leser, der Abschnitt 11 überhaupt **liest** (und das
ist jeder Host, der Attribute liest, also der ganze Zweck), lehnt das **gesamte Modul** ab.

Wie teuer das ist, hängt an einer Auslegungsfrage, die dem **Bytecode-Gebiet** gehört, nicht
diesem hier. Beide Lesarten stehen in derselben Datei:

| Lesart | Beleg | Folge |
|---|---|---|
| **Minor**, wie 3.4 | `docs/Bytecode.md:47-52`: die 3.4-ConstValue-Erweiterung war „the first change to this format that a 3.3 reader cannot ignore … and rejects the module“ — und sie kam als Minor, ausdrücklich verteidigt: „the module that uses the new thing is the module that needs the new runtime“ | 4.x bleibt gültig; **jedes Modul mit Feldattributen ist für jeden 4.x-Host unladbar**, Erato eingeschlossen |
| **Major** | `docs/Bytecode.md:117`: „What it may not do is change the meaning or the shape of something a reader of the previous minor already reads — that is a major“ | Format 5.0, alle Leser müssen mit |

**Empfehlung: B — aber mit der Kostenzeile, die die erste Fassung verschwiegen hat.**
Feldattribute sind die Voraussetzung für META-15 und für `std.cli`, und beide stehen auf der
v5-Liste. Was dazugehört und vorher entschieden sein muss: **das Embedding-Gebiet muss zustimmen**,
genau wie bei META-01 — denn ein Modul mit Feldattributen ist für jeden bestehenden Host
unlesbar. Methoden (C) erst, wenn der Methodenindex anderweitig gebraucht wird. D **nicht**.

**Bricht:** mindestens „Modul mit dem neuen Zielkind lädt auf keinem 4.x-Host“ (gelesen,
`docs/Bytecode.md:471`); ob das ein Format-Minor oder -Major ist, entscheidet das Bytecode-Gebiet.
**4.x-Warnstufe:** keine im Quelltext nötig — die Warnung gehört in die **Release-Notiz** und in
`lyric.json`/`lyric pack` („dieses Modul braucht einen 5.x-Host“).
**Hängt ab von:** Bytecode/Format-Gebiet, Embedding-Gebiet, Generics-Gebiet.

---

### META-03 — `OnMethod` und `@NonExhaustive`: die zwei Anker einlösen oder zurückziehen

**Heute:** beide in `stdlib/std/core.lyr:527-536` als „An anchor for the compiler (4.5)“
(gelesen). Gemessen: `OnMethod` bewirkt nichts (`LYR-SEM0065` wie jedes andere Attribut,
`m14_member.lyr`), `@NonExhaustive` bewirkt nichts (fremder `match` ohne `_` kompiliert), und
**niemand trägt `@NonExhaustive`** — ein Treffer im ganzen Repo, die Deklaration selbst
(gemessen, `grep -rn NonExhaustive stdlib/ docs/guide/`).

**Optionen**
- **A — beide einlösen.** `OnMethod` mit META-02 C, `@NonExhaustive` als Regel in der
  Exhaustiveness-Prüfung. Vorbild: Rust `#[non_exhaustive]`. Preis: siehe Uhrenkorrektur.
- **B — beide zurückziehen**, bis das Feature da ist. Preis: ein `pub`-Entfernen in `std.core`
  ist selbst ein Bruch (minor).
- **C — `@NonExhaustive` einlösen, `OnMethod` zurückziehen.** `@NonExhaustive` hat einen echten
  Bedarf (`Wait`, `IoErrorKind` dürfen wachsen), `OnMethod` hat keinen, den META-02 nicht besser
  löst.

**Empfehlung: C bleibt** — eine ausgelieferte Vokabel, die der Compiler refusiert, ist schlimmer
als eine fehlende. Aber **die zugehörige 4.x-Stufe war in der ersten Fassung an beiden Enden
unbrauchbar, und das ist der eigentliche Befund:**

1. **Die vorgeschlagene Warnung warnt über nichts.** „Ab 4.7 warnt ein fremder `match` über ein
   `@NonExhaustive`-Enum ohne `_`-Arm“ — es gibt kein solches Enum (gemessen). Die Uhr muss an
   das **Setzen** des Attributs gekoppelt werden: *std-Enums bekommen `@NonExhaustive` in 4.7,
   die Regel greift in 5.0.* Der Bruch entsteht beim Setzen, nicht beim Regeln, und die Uhr muss
   dort stehen, wo der Bruch entsteht.
2. **`OnMethod` kann seine eigene Uhr nicht stellen.** `@Deprecated` auf `pub interface OnMethod`
   ist `LYR-PAR0042` (gemessen, `m2_dep_interface.lyr`) — die eigene Zielmengen-Tabelle in §1.1
   sagt es vier Seiten vorher, die erste Fassung hat es trotzdem vorgeschlagen. Für `OnMethod`
   braucht es einen anderen Weg als die Uhr der Sprache: **CHANGELOG-Eintrag in 4.7, Entfernung
   im Major.** Das ist zugleich der Anlass für META-24.

**Bricht:** `@NonExhaustive` einlösen = major für jeden, der das Attribut dann trägt (heute:
niemand, gemessen). `OnMethod` entfernen = minor.
**4.x-Warnstufe:** 4.7 setzt das Attribut auf die std-Enums, die es brauchen, und **warnt dort**
(„dieser `match` braucht ab 5.0 einen `_`-Arm“); `OnMethod` bekommt keine Attributzeile, sondern
einen CHANGELOG-Eintrag.
**Hängt ab von:** Pattern-/Exhaustiveness-Gebiet, META-24.

---

### META-04 — Darf ein Attribut zweimal auf einem Ziel stehen?

**Heute:** nein, immer. `LYR-SEM0068: '@Route' sits on this declaration twice` (gemessen,
`meta-rev/m11_dup.lyr`), ohne Opt-in.

**Optionen**
- **A — bleiben.** Preis: `@Route("/a") @Route("/b")`, `@Case(1) @Case(2)` (tabellengetriebene
  Tests, v5-Liste B6) sind nicht schreibbar; Ersatz ist ein Array-Feld, das die Wertsprache nicht
  kann (META-05).
- **B — Opt-in über ein Markerinterface**, `:: [Repeatable]`, genau wie `WithArg<T>` die
  Positionsform freischaltet. Vorbild: C# `AllowMultiple = true`, Java `@Repeatable`.
  **Preis — korrigiert, siehe unten.**
- **C — immer erlaubt.** Preis: kippt die heutige Diagnose, und ein Host, der `.Single()`
  aufruft, bricht still.

**Kostenkorrektur (die erste Fassung war hier falsch).** Sie schrieb, „das Format kann es schon“
und belegte das mit dem Ordnungssatz „die Attribute eines Ziels in der Reihenfolge, in der sie
stehen“ (`docs/Bytecode.md:460`) — und hörte **elf Zeilen vor der Regel auf, die es verbietet**:

> `docs/Bytecode.md:471-473` (gelesen): „A reader must reject: … **the same (targetKind, target,
> type) triple twice** …“

Wiederholbare Attribute verlangen also, eine **reader-must-reject-Regel zu streichen**. Die
Kostenfrage ist dieselbe wie bei META-02 und gehört demselben Gebiet: nach der 3.4-Präzedenz
(`docs/Bytecode.md:47-52`) ein Minor, dessen Preis pro Modul anfällt; nach dem Wortlaut von
`:117` ein Major. Was in keinem Fall stimmt: „kostet ein Interface“ und „4.x-Warnstufe: keine“.

**Empfehlung: B bleibt, mit korrigiertem Preis.** Der `WithArg`-Präzedenzfall („nichts wird
positionell/wiederholbar aus Versehen“) ist genau richtig, und ein SDK kann den Marker heute
schon über seine eigene Kette erreichen (gemessen, `m10_markerchain.lyr`). Aber der Vorschlag
gehört in **dieselbe Formatrunde wie META-02 und META-05**, nicht in eine Nebenzeile — denn
alle drei verschieben, was ein 4.x-Leser akzeptiert.

**Bricht:** eine Formatregel fällt; Einstufung beim Bytecode-Gebiet (siehe META-02).
**4.x-Warnstufe:** keine im Quelltext; dieselbe Release-Notiz wie META-02.
**Hängt ab von:** Bytecode-Gebiet, META-02, META-05.

---

### META-05 — Was darf in einer Attributzeile stehen?

**Heute:** Zahl (mit Vorzeichen), String, Char, Bool, Unit-Enum-Variante, benannter `let` auf
einen davon. Kein `null` (`LYR-SEM0096` + `SEM0066`, gemessen), kein Array, kein Struct, kein
berechneter Ausdruck, keine Typreferenz.

**Optionen**
- **A — bleiben.** Preis: `@Route { methods = ["GET", "POST"] }` unschreibbar; ein SDK codiert
  Listen als komma-getrennte Strings, also Parsing zur Laufzeit statt Typprüfung.
- **B — Arrays von Literaltypen dazu.** Vorbild: C# (Attributargumente dürfen Arrays von
  Konstanten sein), Java. Preis: eine neue `ConstValue`-Form (Länge + n Werte); die Formatregel
  „ein Wert pro Feld“ bleibt. **Dies ist der einzige der drei Formatvorschläge, dessen Wirkung
  sich auf Module beschränkt, die ihn benutzen** — genau der 3.4-Fall, Wort für Wort.
- **C — B plus verschachtelte Structs aus Literaltypen.** Preis: die Zeile wird ein Baum; die
  Regel „die Position IST der Feldindex“ (`docs/Bytecode.md:457`) muss rekursiv werden.
- **D — Typreferenzen** (`@Handler { for = Damage }`). Vorbild: C# `typeof(T)`, Java `Class<?>`.
  Preis: ein Typindex in einer Zeile ist billig, aber er setzt voraus, dass der Typ überlebt —
  eine DCE-Wurzel für Typen, die nur Attributziele sind (vgl. META-27).

**Empfehlung: B jetzt, D prüfen, C nein.** Arrays sind die einzige Lücke, die sich im
Bibliotheksentwurf wirklich meldet (`std.cli`, Routing, tabellengetriebene Tests); verschachtelte
Structs wären ein Format-Baum für einen Bedarf, den niemand belegt hat. **Nebenbedingung:** die
v5-Liste will Raw- und Mehrzeilen-Strings ausdrücklich „für Regex, SQL und **Hilfetexte in
Attributen**“ (gelesen, `lyric-v5-features.md:8`) — das gehört in dieselbe Runde, weil es
dieselbe Wertsprache erweitert.

**Bricht:** neuer `ConstValue`-Tag; ein 4.x-Leser, der Abschnitt 11 liest, lehnt **ein Modul ab,
das ihn benutzt** — und nur so eines (gelesen, `docs/Bytecode.md:47-52`). Das ist der billigste
der drei Formatvorschläge und der einzige, bei dem „additiv“ trägt.
**4.x-Warnstufe:** keine.
**Hängt ab von:** Bytecode-Gebiet, Syntax-Gebiet (Raw-Strings).

---

### META-06 — `comptime` als Attributwert: eine Wertsprache oder zwei?

**Heute:** zwei, und sie überschneiden sich nur teilweise (L5). `@Retry { limit = comptime (1+2) }`
→ `LYR-SEM0066`, mit derselben Meldung wie ohne `comptime` (gemessen, Kontrolllauf gemacht).
`design/macros.md:80(a)` führt es als nicht gebauten Folgeschritt (gelesen).

**Optionen**
- **A — zusammenführen:** ein Attributargument darf eine `comptime`-Site sein; ihr ausgewerteter
  Wert wird die Zeile. Vorbild: D (jeder CTFE-Ausdruck ist ein UDA), Zig. Preis: die
  Attributauflösung wandert **hinter** die comptime-Auswertung, also hinter die Sema — heute
  läuft `AttributeValues.Resolve` davor. Und ein Lauf **ohne Runner** kann die Zeile nicht mehr
  bilden: `lyrc check` (ohne `--emit`) müsste dann `LYR-CT0001` melden statt `ok` zu sagen
  (gemessen, dass beide Pfade heute existieren: `m7`, `m8`).
- **B — zusammenführen ohne `comptime`-Wort:** ein Attributargument ist *implizit* comptime, wie
  D. Preis: `@Retry { limit = expensive() }` kostet unsichtbar Compile-Zeit — genau die
  D-Eigenschaft, die §2.3 als Preis benennt.
- **C — getrennt lassen, aber die Meldung reparieren:** `SEM0066` nennt `comptime` und sagt, dass
  es hier (noch) nicht geht. Preis: null, löst aber nur die Irreführung.
- **D — getrennt lassen und die kleinere Lattice angleichen:** `comptime` darf eine Unit-Variante
  liefern (heute `SEM0100`), damit beide Mengen dieselben *Typen* haben. Preis: eine
  `ConstValue`-Form, die das Format seit 3.4 kennt.

**Empfehlung: A, und D als Teil davon.** Eine Sprache mit einem Compile-Zeit-Auswerter und einer
Stelle, die Compile-Zeit-Werte fordert, hat keine Entschuldigung dafür, dass die beiden einander
nicht kennen — das ist Rule 2 von der anderen Seite: **zwei Mechanismen für ein Konzept**. B
nicht: die Explizitheit `comptime` ist genau das, was Lyric von D unterscheidet.

**Preis, den A neu sichtbar macht:** A macht `lyric check` ohne Runner *schwächer* — die
Attributzeile existiert dann im Editor nicht. Das hängt an META-11 und an META-30 (wird dieselbe
Site zweimal ausgewertet?).

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine; C ist sofort machbar und sollte in 4.7
landen, egal wie A entschieden wird.
**Hängt ab von:** Compiler-Pipeline (Reihenfolge Sema → comptime → Attributauflösung), META-11,
META-30.

---

### META-07 — `comptime`: welche Ergebnistypen?

**Heute:** Skalar, `bool`, `char`, `string` (gemessen). `int[]`, Struct und Enum-Variante alle
`LYR-SEM0100` (gemessen, `meta-rev/m9_ctarray.lyr`).

**Optionen**
- **A — bleiben.** Preis: Lookup-Tabellen, das Hauptbeispiel der Designnotiz
  (`design/macros.md:30`), gehen nur als String; `design/macros.md:107` nennt es selbst als Lücke.
- **B — Arrays von Literaltypen.** Vorbild: Zig, Rust `const`, D CTFE. Lowering: `newarr` + Stores
  statt eines `Const` (`design/macros.md:80(b)` hat den Weg). Preis: der Wert ist kein Literal
  mehr, sondern ein Initialisierungs-Prolog — die Zusage „die Site wird zur Konstante“ wird
  „die Site wird zu einem Wert, der ohne Laufzeitarbeit entsteht“, und das ist nicht dasselbe.
- **C — B plus Structs aus Literaltypen.** Preis: dito, plus Feldreihenfolge.
- **D — Enum-Varianten** (heute `SEM0100`), siehe META-06 D. Preis: minimal.
- **E — Typen als comptime-Werte** (Zig). Preis: **groß** — Typen wären Werte, die
  Monomorphisierung bekäme eine zweite Quelle, und Lyrics Typprüfung liefe der Auswertung
  hinterher. Das ist die Zig-Falle: Fehlermeldungen aus dem Innern der Bibliothek.

**Empfehlung — geändert: D und B zusammen, C/E nein.** Die erste Fassung sagte „D sofort, B
danach“ und machte damit ihre eigene META-10-Empfehlung unbaubar: `embedBytes` braucht einen
Array-Wert im Modul, und den gibt es ohne B nicht (gemessen: `int[]` ist `LYR-SEM0100`). Entweder
rückt B nach vorn — die Empfehlung hier — oder META-10 liefert zunächst **nur `embed` als
`string`**. Was nicht geht: beides gleichzeitig versprechen. **E ist die Grenze des Gebiets**:
Typen als Werte wären das Makrosystem durch die Hintertür (gelesen, `PLAN.md:375-377`).

Die offene Teilfrage, die B mitbringt und die META-31 stellt: einmal pro Prozess gebaut oder pro
Auswertung?

**Bricht:** nein (alles additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** Lowering/IR-Gebiet (B), Enum-Gebiet (D), META-10, META-31.

---

### META-08 — `comptime` als echtes Schlüsselwort

**Heute:** kontextuell (gelesen, `docs/Grammar.md:491`). Der Bruch ist gemessen und die Diagnose
führt in die Irre (L7).

**Optionen**
- **A — echtes Schlüsselwort in 5.0.** Vorbild: Zig (`comptime` ist reserviert), Rust (`const`).
  `design/macros.md:78` empfiehlt es selbst. Preis: jeder Bezeichner `comptime` bricht.
- **B — kontextuell bleiben und die Diagnose reparieren.** Preis: ein Sonderfall im Fehlerpfad,
  den jeder künftige kontextuelle Präfix wieder braucht.
- **C — kontextuell bleiben und das Präfix *nicht* öffnen, wenn ein Name `comptime` sichtbar
  ist.** Vorbild: keiner, und das ist ein Warnsignal. Preis: die Bedeutung eines Präfixes hängt
  vom Scope ab — unlesbar.

**Empfehlung: A.** Ein Wort, das eine Auswertungsstufe eröffnet, sollte kein Bezeichner sein
können; `type`, `throws`, `extern` sind kontextuell, weil sie *Deklarationen* eröffnen und
eindeutig positioniert sind — `comptime` steht mitten im Ausdruck. Bis 5.0: B als Warnung.

**Bricht:** minor (Bezeichner `comptime`). **4.x-Warnstufe:** ab 4.7 warnt jede Deklaration
namens `comptime`: „wird in 5.0 ein Schlüsselwort“. Diese Warnung braucht ein Ventil — siehe
META-22.
**Hängt ab von:** Lexer/Grammatik-Gebiet, META-22.

---

### META-09 — Wie weit reicht `comptime`: Site, Parameter, Block, Funktion?

**Heute:** nur `comptime UnaryExpr`, nur mit Modulnamen im Rumpf (gemessen: Lokale und Parameter
sind `SEM0100`). `design/macros.md:80(c)` hält `comptime fn` für unnötig (gelesen).

**Optionen**
- **A — bleiben.** Preis: eine Funktion, die *nur* zur Compile-Zeit sinnvoll ist, ist nicht als
  solche markierbar und landet im Modul, wenn jemand sie zur Laufzeit ruft.
- **B — `comptime fn`**: nur zur Compile-Zeit aufrufbar. Vorbild: Rust `const fn` (umgekehrt:
  *auch* zur Compile-Zeit), Zig `comptime`-Parameter. Preis: zwei Funktionsarten; Rule 2 fragt.
- **C — `comptime { … }`-Block.** Vorbild: Nim `static:`, Rust `const { }`. Preis: braucht eine
  Regel, welche Namen er ins Modul legt — nahe an Codegenerierung.
- **D — comptime-Parameter** (Zig): `fn Vec(comptime n: int)`. Preis: das ist die Typebene aus
  META-07 E, nur anders geschrieben.

**Empfehlung: A.** Die Site ist der explizite, lokale, nachvollziehbare Ort; B ist eine Lösung
ohne belegtes Problem, C und D führen zur Typebene. **Das ist die Frage, bei der Lyric am ehesten
versehentlich zu Zig wird.**

**Bricht:** nein. **Hängt ab von:** nichts.

---

### META-10 — `embed()` / `embedBytes()`: die Sandbox-Ausnahme und der Build-Graph

**Heute:** existiert nicht (kein Treffer im Repo). Auf der v5-Liste als Posten 18
(gelesen, `lyric-v5-features.md:18`) mit der Notiz „die Sandbox braucht dafür eine Lese-Ausnahme
für Projektdateien“. Gemessen ist die Gegenprobe: `comptime exists("C:/Windows")` ist
`LYR-CT0002 … requires capability 'fileAccess'`.

**Die eigentliche Frage ist nicht die Sandbox, sondern die Inkrementalität.** Wenn
`embed("x.txt")` den Inhalt in das Modul schreibt, muss eine Änderung an `x.txt` den Build neu
auslösen.

**Optionen**
- **A — eingebaute Form `embed(<Stringliteral>)`, kein `comptime` nötig.** Vorbild: Zig
  `@embedFile`, Rust `include_str!`. Pfad relativ zur Quelldatei, der Compiler trägt die Datei in
  die Abhängigkeitsliste des Moduls ein. Preis: eine neue eingebaute Funktion.
- **B — `comptime readProjectFile("x")` mit einem neuen Capability-Bit.** Preis: ein Bit, das zur
  Laufzeit nie gesetzt ist — ein Sonderfall im Capability-Modell.
- **C — explizite Deklaration in `lyric.json`**, wie D's `-J`. Vorbild: D. Preis: eine
  Konfigzeile; Gewinn: die Menge der lesbaren Dateien steht im Projekt, nicht im Quelltext.
- **D — gar nicht; `build.lyr` schreibt eine `.lyr`-Datei mit dem String.** Vorbild: Lyrics
  Stufe 3 (`design/macros.md:83`). Preis: eine Datei pro Konstante, Binärdaten als Quelltext.

**Empfehlung: A + C, aber mit einer Reihenfolge-Korrektur.** Die erste Fassung empfahl A+C und
gleichzeitig unter META-07 „B danach“ — damit ist `embedBytes` unbaubar, weil das Modul keinen
Array-Wert halten kann (gemessen, `m9_ctarray.lyr`). Es gibt genau zwei kohärente Wege:

1. **META-07 B rückt vor** (empfohlen): `embed` liefert `string`, `embedBytes` liefert einen
   Array-Wert, beide in derselben Runde.
2. **Stufe 1 liefert nur `embed`** als `string`, `embedBytes` wartet auf META-07 B — dann muss
   das hier stehen und nicht als Selbstverständlichkeit mitlaufen.

**B ausdrücklich nicht**: ein Capability-Bit, das zur Laufzeit nie existiert, macht das Modell
unehrlich.

**Konflikt mit Rule 2:** `embed` ist eine zweite Compile-Zeit-Form neben `comptime`. Begründung:
`comptime` wertet *Lyric-Code* aus, `embed` liest *Bytes*, und der Grund, warum das nicht dasselbe
ist, steht in der Sandbox. Trotzdem: **ADR-Kandidat.**

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine.
**Hängt ab von:** META-07 B, META-31, Build-/Projekt-Gebiet (`lyric.json`, Inkrementalität),
Capability-Gebiet.

---

### META-11 — Ohne Runner grün, mit Runner rot

**Heute:** gemessen — `lyrc check` sagt `ok` für Programme, die unter `build` und unter
`check --emit` `LYR-CT0002` produzieren (`m7`, `m8`). Absicht: „der Editor führt nichts aus“
(gelesen, `design/macros.md:70,75`).

**Optionen**
- **A — bleiben, aber es sagen.** Eine Note am Ende des `check`-Laufs: „n `comptime`-Sites nicht
  ausgewertet; `lyric build` oder `check --emit` entscheidet“. **Preis: geringer als in der
  ersten Fassung veranschlagt** — der Diagnose-Code existiert bereits: `LYR-CT0001` ist genau
  „ein Build ohne Runner“ (gelesen, `design/macros.md:70,77`, geprüft in
  `…/macro-abi/examples/ComptimeTests.cs:172`). Er muss nur den `check`-Pfad als *Note* statt als
  Fehler erreichen.
- **B — `check` wertet immer aus.** Preis: `check` und der Editor divergieren statt `check` und
  `build` — verschiebt das Problem.
- **C — der LSP bekommt einen Runner** und zeigt den Wert im Hover. Vorbild: `zls` teilweise,
  rust-analyzer wertet `const` aus. Preis: der Editor führt Code aus, den der Nutzer gerade tippt
  (`design/macros.md:75` lehnt es bewusst ab), und mit `embed` (META-10) läse er auch Dateien.
- **D — Caching pro Site-Hash**, damit C bezahlbar wäre (`design/macros.md:80(d)`).

**Empfehlung: A.** Die Trennung ist richtig; was fehlt, ist dass sie **sichtbar** ist. Ein Nutzer
darf nicht erst beim Build erfahren, dass sein Editor eine Klasse von Fehlern strukturell nicht
sieht. Konkret: `LYR-CT0001` als Note, und `lyrc --help` soll bei `check` sagen, was `--emit`
zusätzlich prüft.

**Bricht:** nein. **Hängt ab von:** CLI-/LSP-Gebiet, META-30 (Caching), META-06 A.

---

### META-12 — Ist das Compile-Zeit-Budget Sprache oder Implementierung?

**Heute:** ein Instruktionsbudget, gemessen wirksam („did not finish within the compile-time
budget“, `m8_budget.lyr`) — **ohne Angabe der Zahl und ohne Angabe des Verbrauchs**. Ob die Zahl
spezifiziert ist, steht weder in `docs/Grammar.md` noch in `docs/Bytecode.md` (gelesen; kein
Treffer) — sie ist ein Implementierungsdetail.

**Optionen**
- **A — Implementierungsdetail bleiben.** Preis: dasselbe Programm baut auf einer Maschine und
  nicht auf der anderen, sobald jemand das Budget anpasst.
- **B — spezifizierte Untergrenze** („mindestens N Instruktionen“). Preis: **macht nur das
  GELINGEN unterhalb von N portabel**; oberhalb bleibt „baut hier, baut dort nicht“ ausdrücklich
  erlaubt — als Antwort auf das Reproduzierbarkeits-Argument reicht das nicht.
- **C — pro Datei einstellbar.** Vorbild: Zig `@setEvalBranchQuota`. Preis: eine Stellschraube im
  Quelltext, die nichts über die Bedeutung des Programms sagt.
- **D (neu) — exakte Zahl in der Spec.** Preis: eine Zahl, die irgendwann zu klein ist und dann
  nur im Major wachsen darf.
- **E (neu) — die Diagnose nennt Budget und Verbrauch** („1 000 000 Instruktionen verbraucht,
  Budget 1 000 000“). Vorbild: Zigs `@setEvalBranchQuota`-Fehler nennt die Quota. Preis: null.

**Empfehlung — geändert: E immer, dann D oder B.** Die erste Fassung empfahl B allein, und die
Kritik trifft: eine Untergrenze macht nur das Gelingen portabel. **E ist die Bedingung für alles
Weitere** — ohne eine Zahl in der Meldung kann niemand eine Site gegen das Budget optimieren, und
ohne das ist auch B nutzlos. Ob danach D (exakt) oder B (Untergrenze) gilt, ist eine
Spec-Entscheidung; ich neige zu **D**, weil „reproduzierbar“ sonst eine Eigenschaft mit
Sternchen ist. Die Granularität ist eine eigene Frage: META-30.

**Bricht:** nein. **Hängt ab von:** Spec-Gebiet, Diagnostik-Gebiet, META-30.

---

### META-13 — Synthese: implizit, mit Klausel, oder per Attribut?

**Heute:** gar nicht. Gemessen: `struct Coord :: [Equatable<Coord>, Hashable<Coord>, Display]`
ohne Bodies → drei `LYR-SEM0020` (`audit/e1_synth.lyr`).
`design/conformance-synthesis.md:156` empfiehlt das Swift-Modell (gelesen).

**Optionen**
- **A — implizit (Swift):** Konformanz ohne Body → synthetisiert, geschlossene Liste. Preis:
  „nichts geschrieben“ und „automatisch erzeugt“ sehen im Quelltext gleich aus.
- **B — Klausel (Scala 3):** `struct Coord :: [Ordered<Coord>] derives Equatable, Hashable`.
  Preis: ein neues Wort — `PLAN.md:377` listet `derive` ausdrücklich unter „bewusst nicht“.
  Gewinn: **bibliothekserweiterbar**, wenn die Ableitungsregel gewöhnlicher Lyric-Code über eine
  Compiler-gelieferte Feldbeschreibung ist. Dann ist `ToJson` keine Compiler-Zeile mehr.
- **C — Attribut (Rust):** `@Derive { … }`. Preis: bricht „ein Attribut beschreibt und tut
  nichts“ frontal; `design/conformance-synthesis.md:41` hat es aus diesem Grund verworfen.
- **D — Typ-Modifier (C#/Kotlin `record`/`data class`).** Preis: alles-oder-nichts — und in C#
  **schmäler als gedacht**, weil `Deconstruct` nur für Positionsrecords entsteht.

**Empfehlung — geändert, weil die erste Fassung sich selbst widersprach.** Sie empfahl A
(geschlossen), gleichzeitig META-15 B (Feldattribute, die der **Compiler** liest) und META-16 B
(Namensklasse, weil die Menge compilergelesener Attribute wächst). Das geht zusammen nicht auf:
META-15 B bläht genau die Menge auf, deren Wachstum META-16 als Notfall behandelt, und META-13
schrieb selbst, die Frage „soll eine Bibliothek eine eigene Ableitung anbieten können“ gehöre
**vor** den Bau von A — und empfahl dann A.

Es gibt zwei kohärente Pakete, und die Wahl zwischen ihnen ist **die wichtigste Entscheidung des
ganzen Gebiets**:

| Paket | Inhalt | Preis |
|---|---|---|
| **Swift-Paket** | META-13 A + META-14 A + META-15 A/D + META-16 A | Per-Feld-Steuerung fällt weg; `std.serial` taugt nur Lyric-zu-Lyric oder braucht `CodingKeys` von Hand; die Menge compilergelesener Attribute bleibt bei 2 und META-16 wird **unnötig** |
| **Scala-Paket** | META-13 B + META-14 C + META-15 C + META-16 A | `@Skip`/`@Rename` bleiben **inert** und werden von **Bibliothekscode** gelesen; ein neues Wort (`derives`); braucht eine Feldbeschreibung zur Compile-Zeit — und damit einen Teil von META-07/E, den ich sonst ablehne |

**Meine Empfehlung: das Swift-Paket für 5.0, und die Scala-Frage ausdrücklich als offen
protokollieren** — nicht, weil Scala schlechter wäre, sondern weil sein Fundament (statische
Interface-Member, `Self`, eine Compile-Zeit-Feldbeschreibung) ohnehin nicht steht und 5.0 sonst
daran hängt. Was das kostet: `std.serial` startet ohne Per-Feld-Steuerung. Was es spart: die
Menge der compilergelesenen Attribute wächst **nicht**, und damit fällt META-16 als Dringlichkeit
weg. **Die Mischung aus beiden Paketen ist die einzige Variante, die beide Preise zahlt.**

**Bricht:** nein (A ist additiv: was heute kompiliert, hat die Methoden geschrieben, und
geschriebene gewinnen). **4.x-Warnstufe:** keine.
**Hängt ab von:** Interface-/Generics-Gebiet (statische Interface-Member, bedingte Konformanz),
Optional-Gebiet (`?T == ?T`, heute `LYR-SEM0059`), META-14, META-15, META-16.

---

### META-14 — Welche Interfaces werden synthetisiert, und ist die Liste offen?

**Heute:** keine. Design: vier (`Equatable`, `Hashable`, `Ordered`, `Display`), später
`ToJson`/`FromJson` (gelesen, `design/conformance-synthesis.md:62-64`). Die v5-Liste ergänzt
`Debug`, `Clone`, `Default` (gelesen, `lyric-v5-features.md:3,30`).

**Optionen**
- **A — geschlossene Vierertabelle.** Vorbild: Swift — **aber nur teilweise**: Swift synthetisiert
  `Comparable` seit SE-0266 (5.3) **nur für Enums**, und nur wenn alle Payloads `Comparable` sind;
  **für Structs nie**. Für `Ordered<Coord>` auf einem Struct gibt es in der Liste keinen
  bodylosen Präzedenzfall — das nächstliegende ist Rusts `#[derive(Ord)]`, ein Builtin-Derive.
  Preis: jede weitere Zeile ist Compiler-Arbeit, und `Ordered` auf Structs ist eine **eigene**
  Entscheidung mit eigener Begründungspflicht (Feldreihenfolge als lexikographische Ordnung).
- **B — Vier plus `Debug`, `Default`, `Clone`.** Preis: `Clone` auf Klassen ist eine
  Tiefenkopie-Entscheidung mit Zyklenfrage (gelesen, `design/conformance-synthesis.md:170-172`);
  `Default` braucht statische Interface-Member.
- **C — offene Liste (Scala 3).** Siehe META-13 B.

**Empfehlung: A in 5.0 ausliefern, `Debug` und `Default` als geplante Zeilen benennen, `Clone`
und `ToJson` an META-13 hängen** — mit der Korrektur, dass **`Ordered` für Structs kein
abgeschriebener Swift-Fall ist**, sondern eine Zeile, die Lyric selbst begründen muss. Die
Reihenfolge aus `conformance-synthesis.md:159-160` (Equatable+Hashable, dann Display, dann
Ordered) bleibt gut und setzt `Ordered` ohnehin ans Ende, wo die Begründung hingehört.

**Bricht:** nein. **Hängt ab von:** META-13, statische Interface-Member, bedingte Konformanz.

---

### META-15 — Per-Feld-Steuerung der Synthese

**Heute:** unmöglich — ein Attribut kann nicht auf einem Feld stehen (gemessen, `LYR-SEM0065`,
`m14_member.lyr`). `design/conformance-synthesis.md:168` fragt „darf ein Feld ausgeschlossen
werden?“ und schlägt „nein“ vor (gelesen).

**Das ist die schärfste Kopplung des ganzen Gebiets.** Für `Equatable`/`Hashable` ist „nein“
vertretbar (Rust macht es auch so). Für `ToJson`/`FromJson` ist es **unbrauchbar**: ohne
`@Rename("user_id")`, `@Skip` und `@Default` produziert eine JSON-Synthese Feldnamen, die zu
keinem fremden Schema passen, und die `std.serial`-Planung hängt daran (gelesen,
`lyric-v5-features.md:101-102`).

**Optionen**
- **A — nein, nie.** Preis: `std.json`-Synthese taugt nur für Lyric-zu-Lyric.
- **B — Feldattribute (META-02 B) plus eine geschlossene Menge, die der COMPILER liest**
  (`@Skip`, `@Rename`, `@Default`). Vorbild: Rust `#[serde(…)]`, Swift `CodingKeys`. Preis: die
  Menge compilergelesener Attribute wächst von 1 auf 4+ — und damit wird META-16 aus einer Frage
  eine Pflicht.
- **C — Feldattribute, aber die Synthese-Regel ist Bibliothekscode** (META-13 B): die Ableitung
  liest die Zeilen selbst, die Attribute bleiben **inert**. Vorbild: Scala 3. Preis: setzt die
  offene Liste voraus — also das Scala-Paket aus META-13.
- **D — Swifts Weg: ein von Hand geschriebener `CodingKeys`-Enum** statt Feldattributen.
  Vorbild: Swift. Preis: Boilerplate zurück, aber keine neuen Compiler-Attribute und **keine
  Formatarbeit** (META-02 B fiele weg).

**Empfehlung — geändert: die Frage ist nicht eigenständig entscheidbar.** Sie ist die
Kehrseite von META-13. Wer dort das Swift-Paket wählt, wählt hier **D** (oder A); wer das
Scala-Paket wählt, wählt hier **C**. **B ist die Variante, die beide Preise zahlt**: Formatarbeit
*und* wachsende Compiler-Sonderfälle. Die erste Fassung empfahl B, und das war der innere
Widerspruch, den die Kritik zu Recht benannt hat.

Folgt man META-13 (Swift-Paket für 5.0): **D**, mit `C` als das, was das Scala-Paket später
ermöglichen würde.

**Bricht:** nein (additiv). **Hängt ab von:** META-13 (entscheidet diese Frage mit), META-02,
META-16.

---

### META-16 — Der compilergelesene Satz: geschlossen, benannt, oder offen?

**Heute:** genau ein Element, `@Deprecated` (gelesen, `docs/guide/15-attributes.md:249-338`).
Guide 15:334-338: „grows by decision rather than by convention“. `@Inline` wurde ausdrücklich
abgelehnt, mit Messung (gelesen, `STATUS.md:2444-2449`).

**Die Kandidatenliste — nachgezählt.** Die erste Fassung schrieb „die Menge wächst von 1 auf 5+“
und zählte dabei Attribute mit, die der Compiler gar nicht liest:

| Kandidat | Wer läse es | Beleg |
|---|---|---|
| `@Deprecated` | **Compiler** | gelesen, Guide 15:249-338 |
| `@NonExhaustive` | **Compiler**, wenn META-03 C kommt | gelesen, `stdlib/std/core.lyr:532-536` |
| `@Test` | **Runner** | gelesen, `src/Lyrtest/Program.cs:168` |
| `@Bench` | Runner (dasselbe Muster) | v5-Liste B6 |
| `@Command`/`@Flag` | `std.cli`, also **Bibliothek/Runner** | v5-Liste B5 |
| `@Skip`/`@Rename` | Compiler **nur unter META-15 B**; unter META-15 C/D: Bibliothek bzw. gar nicht | — |

**Die Menge wächst also auf 2, nicht auf 5+ — es sei denn, META-15 B wird gewählt.** Damit
kippt die Dringlichkeit, auf der die erste Fassung ihre Platz-1-Empfehlung gebaut hat.

**Optionen**
- **A — geschlossen bleiben** (`@Deprecated` + `@NonExhaustive`). Preis: META-15 B fällt —
  was nach der korrigierten META-13/15-Empfehlung ohnehin die Linie ist.
- **B — eine Namensklasse `@!Deprecated`, `@!Skip`.** Vorbild: keines direkt; am nächsten Zigs
  `@builtin`-Präfix und Rusts `#![…]`-vs-`#[…]`. Gewinn: „inert“ bleibt eine *sichtbare*
  Eigenschaft. Preis: **ein Major-Bruch an der einzigen Attributzeile, die heute jede Codebasis
  trägt** — und `lyric fmt` würde ihn automatisch schreiben, was L13/META-33 zu einem CI-Problem
  macht.
- **C — offen lassen**, die Liste wächst per Entscheidung. Preis: „ein Attribut beschreibt und tut
  nichts“ (Guide 15:20-22) wird von Jahr zu Jahr falscher, ohne dass ein Datum sagt, wann sie
  kippte.
- **D — Trennung nach Leser statt nach Namen**: `std.core`-Attribute liest der Compiler, alles
  andere ist inert. Preis: die Grenze ist ein Modulname — und META-01 sagt, dass Modulnamen in der
  Zeile gar nicht stehen. **Nach META-01 A stünden sie drin** — dann wird D billig.

**Empfehlung — geändert: A, und B nur, wenn META-15 B gewählt wird.** Die erste Fassung setzte B
auf Platz 1 der Baureihenfolge. Das war der teuerste Vorschlag des Dossiers mit der dünnsten
Begründung: ein Major-Bruch an `@Deprecated` — der einzigen Zeile, die jede Codebasis trägt —
für eine Menge, die ohne META-15 B von 1 auf 2 wächst. **Zwei Elemente rechtfertigen kein Sigil.**

Wenn die Menge je wirklich wächst (Scala-Paket mit compilergelesenen Feldattributen, `@Inline`
kehrt zurück, `@Command` wandert in den Compiler), dann ist B richtig — und **dann** gilt der
Satz der ersten Fassung, dass es später teurer wird. Bis dahin ist die richtige Antwort:
**Guide 15:20-22 präzisieren** („ein Attribut beschreibt und tut nichts — mit genau zwei
benannten Ausnahmen, hier aufgezählt“), und D als billige Option im Auge behalten, falls META-01 A
kommt.

**Bricht:** A: nein. B: major (`@Deprecated` → `@!Deprecated`).
**4.x-Warnstufe:** bei A keine. Bei B: ab 4.7 beide Schreibweisen, Warnung bei der alten — **und
`lyric fmt` darf sie NICHT automatisch umschreiben**, solange beide gültig sind (L13, META-33).
**Hängt ab von:** META-13, META-15, META-03, META-33, Lexer (`@!` als Token — `AT_LBRACKET` zeigt,
dass der Lexer solche Adjazenzregeln kann, `docs/Grammar.md:61`).

---

### META-17 — `@Deprecated` ausbauen: `renamed`, Diagnose-ID, `since`

**Heute:** `message` + `until`, beides gemessen wirksam. Kein `renamed`, keine Diagnose-ID, kein
`since` (gelesen, `stdlib/std/core.lyr:500-511`).

**Optionen**
- **A — bleiben.** Preis: `lyrfix` (v5-Liste Posten 24) hat keine Datenquelle und muss seine
  Regeln hartkodieren.
- **B — `renamed: string` dazu.** Vorbild: Swift `@available(…, renamed: "newName")` mit
  anwendbarem Fix-it. Preis: ein Feld; der Compiler muss prüfen, dass der Name existiert, sonst
  ist der Fix-it eine Lüge.
- **C — B plus `since: string`** (rein dokumentarisch). Vorbild: Rust, Scala. Preis: ein Feld, das
  der Compiler nicht liest.
- **D — B plus `id: string`** für selektive Unterdrückung. Vorbild: C# `DiagnosticId` +
  `UrlFormat` (.NET 5). Preis: setzt voraus, dass Lyric überhaupt eine Unterdrückungsform hat —
  **die gibt es nicht (gemessen, L10), und sie ist jetzt eine eigene Frage: META-22.**

**Empfehlung: B. C nein. D zusammen mit META-22 — nicht davor und nicht danach.** `renamed` ist
die einzige Erweiterung mit einem belegten Abnehmer (`lyrfix`), und sie macht aus der
Deprecation-Uhr ein Migrationswerkzeug statt einer Mahnung. D ohne META-22 ist ein Feld ohne
Leser; META-22 ohne D ist eine Unterdrückung ohne Granularität.

**Bricht:** nein (additiv, Feld mit Default `""`). **Hängt ab von:** Toolchain-Gebiet (`lyrfix`,
Fix-it-Format), META-22.

---

### META-18 — Enum-Reflexion per Synthese

**Heute:** existiert nicht (kein Treffer). Auf der v5-Liste als Posten 15 (gelesen,
`lyric-v5-features.md:15`): `E.variants()`, `E.fromName("…")`.

**Optionen**
- **A — Synthese eines `CaseIterable`-artigen Interfaces:** `enum E :: [Enumerable<E>]` ohne Body
  → `static fn variants(): E[]`, `static fn fromName(s: string): ?E`, `fn name(): string`.
  Vorbild: Swift `CaseIterable`. Preis: braucht **statische Interface-Member und `Self`**
  (v5-Liste A1 #4) — ohne die geht es nicht. Und `variants()` liefert ein Array: ohne META-07 B
  ist das ein Laufzeit-Aufbau, kein Literal.
- **B — `comptime`-Weg:** die Bibliothek baut die Tabelle per `comptime`. Preis: `comptime` kann
  heute weder Arrays noch Enum-Werte liefern (gemessen, META-07) und kennt die Variantenliste
  nicht.
- **C — Generator über `build.lyr`** (gelesen, `design/macros.md:83`). Preis: eine Datei pro Enum,
  die veraltet, sobald jemand eine Variante hinzufügt.
- **D — Host liest es aus den Zeilen.** Geht heute **teilweise**: die Names-Section trägt die
  Variantennamen für Typen, die eine Attributzeile referenziert (gelesen, `docs/Bytecode.md:478-484`;
  gemessen: `names ParseErrorKind.Empty($tag)`). Preis: nur für den Host, nicht für Lyric-Code.

**Empfehlung: A.** Es ist eine Zeile mehr in derselben Synthesetabelle (META-14), es kostet zur
Laufzeit wenig, weil die Monomorphisierung die Liste einmal hinlegt, und es ist der einzige Weg,
der `fromName` **exhaustiv** macht. Nur: **ohne statische Interface-Member geht gar nichts**, und
das ist eine Frage des Interface-Gebiets.

**Bricht:** nein. **Hängt ab von:** Interface-Gebiet (statische Member, `Self`), META-13/14,
META-07 B.

---

### META-19 — Wie sieht ein Leser, was synthetisiert wurde?

**Heute:** die Frage stellt sich nicht, weil es keine Synthese gibt.
`design/conformance-synthesis.md:106-107` benennt sie im Voraus (gelesen): „Go to definition“ auf
eine synthetisierte Methode hat kein Ziel; DocGen soll „synthesized“ vermerken.

**Optionen**
- **A — nichts.** Preis: die eine Sache, die alle Makrosysteme falsch machen, macht Lyric auch.
- **B — `lyric expand <datei>` druckt die synthetisierten Bodies als Lyric-Quelltext.** Vorbild:
  `cargo expand` (Drittwerkzeug), Xcode „Expand Macro“ (eingebaut, Swift 5.9). Preis: ein
  CLI-Verb, und die Bodies müssen als AST existieren — was `conformance-synthesis.md:91-93`
  ohnehin vorsieht.
- **C — LSP-Hover zeigt den Body**, Go-to-definition springt auf die Konformanzliste. Preis:
  LSP-Arbeit.
- **D — DocGen vermerkt „synthesized“.** Preis: klein, deckt nur die Doku.

**Empfehlung: B, und es ist billiger als es klingt** — der AST liegt vor, der Formatter kann ihn
drucken (er tut es für `comptime` schon, gelesen `design/macros.md:75`). C und D danach. **Und
dasselbe Verb löst META-29 mit**: wenn `lyric expand` die comptime-Sites mit ihren ausgewerteten
Werten druckt, hat der Nutzer denselben Einblick in die andere Compile-Zeit-Maschinerie.

**Bricht:** nein. **Hängt ab von:** META-13, CLI-Gebiet, META-29.

---

### META-20 — Doc-Tests

**Heute:** keine. Aber: `///`-Blöcke sind geparst und an ihre Deklaration gebunden (gelesen,
`src/Lyric.Frontend/Parsing/ParsedModule.cs:14-31`), und die Suite kompiliert bereits jedes
Guide-Snippet (gelesen, `tests/Lyric.Tests.Embedding/GuideTests.cs:8,76`) — als C#-Test, ohne es
auszuführen.

**Optionen**
- **A — Rust-Modell:** jeder Fence-Block in `///` wird zu einem eigenen Programm kompiliert und
  ausgeführt; Attribute am Fence (`ignore`, `no_run`, `should_panic`); `#`-Zeilen sind im Test,
  aber nicht in der Doku; **der Block sieht nur `pub`**. Preis: jeder Block ist eine eigene
  Kompilation — die `lyrtest`-Isolationsfrage aus `STATUS.md` („13 Kompilate werden 166“) kommt
  hier potenziert.
- **B — D-Modell (documented unittests):** ein `@Test`-Block direkt **hinter** einem
  `///`-Kommentar wird zum Beispiel in der Doku UND bleibt ein gewöhnlicher Test. Vorbild: D, Nim
  `runnableExamples`. Preis: kein neuer Ausführungspfad — aber der Beispielcode steht *neben* der
  Doku, nicht *in* ihr. **Und: D/Nim führen im Modul aus, Lyric-Tests laufen heute außerhalb.**
- **C — nur kompilieren, nicht ausführen** (was die Suite heute für den Guide tut), erweitert auf
  Nutzerprojekte: `lyric check --docs`. Preis: fängt Syntax- und Typfehler, keine falschen
  Ergebnisse.
- **D — gar nicht.**

**Empfehlung: B, und C als Vorstufe — mit einer Konsequenz, die die erste Fassung nicht benannt
hat.** Lyric fährt heute bereits das **Rust-Lager**: ein Test ist ein fremdes Modul und sieht nur
`pub` (gemessen, L15, `meta-rev/proj/`; festgeschrieben in `docs/guide/20-testing.md:51-54`).
Wenn ein Doc-Test das D/Nim-Modell bekommt (im Modul, sieht alles), hat Lyric **zwei
Sichtbarkeitsregeln für Testcode**. Weil B den Doc-Test *als `@Test` im Testbaum* realisiert,
erbt er die bestehende Regel automatisch — das ist der eigentliche Grund, warum B hier besser ist
als A, und er ist stärker als das Isolationsargument. Der Preis, den man dabei akzeptiert: ein
Doc-Beispiel kann keine privaten Details zeigen. Die zugehörigen offenen Punkte
(Capabilities, `comptime` im Doc-Test) sind META-28.

**Bricht:** nein (additiv). **Hängt ab von:** Test-/Toolchain-Gebiet (`lyrtest`-Isolation),
DocGen-Gebiet, META-28.

---

### META-21 — Positionsargumente: `WithArg<T>` oder benannte Argumente?

**Heute:** `:: [WithArg<T>]` erlaubt **genau einen** Positionswert (gemessen: `@Tag(5)` ohne
`WithArg` ist `LYR-SEM0094`). N-äre Positionsargumente wurden in der Attributrunde abgelehnt
(gelesen, `STATUS.md:1017-1019`). Beide Formen zugleich ist ein Fehler mit irreführender Meldung
(L8, gemessen `m12_bothforms.lyr`).

**Prämissenkorrektur — und sie kippt die Empfehlung.** Die erste Fassung schrieb, `AttrArgs` sei
„eine Parallelwelt zur Struct-Initialisierung *und* zum Aufruf“, ein „dritter
Argumentmechanismus“, und baute darauf ihr Rule-2-Argument. Das ist falsch (gelesen):

```ebnf
AttrArg         = IDENTIFIER '=' Expr .      docs/Grammar.md:160
StructInitField = IDENTIFIER '=' Expr .      docs/Grammar.md:494
```

Dieselbe Produktion. Und der Guide sagt es ausdrücklich: „The block after the name is the
**struct initializer**, restricted to what can be written into the compiled module“ (gelesen,
`docs/guide/15-attributes.md:47-48`). Die Klammerform **ist** die Struct-Initialisierung.
Eigenständig ist allein die `( Expr )`-Form aus `WithArg` — ein Mechanismus, kein dritter.

**Optionen**
- **A — bleiben.** Preis: ein Attribut mit zwei sinnvollen Pflichtfeldern muss die Blockform
  schreiben. Gewinn: null Bruch, und die Schreibweise bleibt die des Structs, der es ist.
- **B — benannte Argumente der Sprache nutzen** (v5-Liste A2 #7, `connect(host: "h", port: 80)`,
  gelesen): die Attributzeile wird `@Retry(limit: 3, label: "x")`. Vorbild: C#, Swift. Preis:
  **major an jeder Attributzeile des Ökosystems** — und, neu gesehen, zwei Schreibweisen für
  dieselbe Sache nebeneinander: `@Retry(limit: 3)` mit `:` gegen `Retry { limit = 3 }` mit `=`
  für denselben Struct. Es **entfernt keinen Mechanismus**, es tauscht die Struct-Init-Schreibweise
  gegen die Aufruf-Schreibweise.
- **C — n-äre Positionsform über `WithArgs<A, B>`.** Preis: genau das, was die Attributrunde
  abgelehnt hat, weil die Feldreihenfolge dann eine SDK-Zusage wird (siehe META-26).

**Empfehlung — geändert: A, plus die Meldung aus L8 reparieren.** Mit der korrigierten Prämisse
fällt die Begründung für B, und was bleibt, ist ein Major-Bruch ohne Gewinn. Rule 2 zeigt jetzt
**gegen** B: `@Retry { limit = 3 }` und `Retry { limit = 3 }` sind heute dieselbe Schreibweise für
dieselbe Sache — das ist die Rule-2-konforme Lage, und B würde sie auflösen.

Wenn benannte Argumente kommen, ist die richtige Frage eine andere und kleiner: darf die
`WithArg`-Klammerform `@On(Event.Damage)` dann auch `@On(event: Event.Damage)` heißen? Antwort:
nein — sie füllt ein Feld, dessen Name gar nicht in der Zeile steht (gelesen,
`docs/guide/15-attributes.md:62-66`).

**Bricht:** A: nein. B: major (jede Attributzeile).
**4.x-Warnstufe:** bei A keine.
**Hängt ab von:** Syntax-Gebiet (benannte Argumente), Formatter, META-26.

---

### META-22 — Wie schweigt ein Aufrufer eine Diagnose ab, die er bewusst eingeht?

**Heute:** gar nicht. Gemessen: `lyrc --help` kennt `--deny-warnings` (macht Warnungen strenger)
und keinen einzigen Schalter in die andere Richtung; `lyric.json` hat die Schlüssel `name`,
`sourceRoot`, `nativeRoots`, `dependencies` und sonst nichts, was Diagnosen beträfe (gelesen,
`docs/guide/16-building.md:305-318`). `LYR-SEM0076` (Warnung an der Aufrufstelle) und
`LYR-SEM0081` (Fehler an der Deklaration) werden unbedingt gemeldet (gelesen,
`src/Lyric.Frontend/Sema/DeprecationPromise.cs:41,53`).

**Warum das jetzt eine Frage ist:** die 5.0-Migration produziert Warnungen in Serie — der Branch,
auf dem dieses Dossier entsteht, heißt `feat/v5-warning-clocks`. Ein Shim, der die alte Form
kapselt und intern aufruft, **warnt über sich selbst**, und es gibt keinen Weg, das abzustellen.
Dasselbe gilt für META-08 (jede Deklaration namens `comptime` warnt ab 4.7) und für META-03
(jeder `match` über ein frisch markiertes std-Enum).

**Optionen**
- **A — Attribut am Aufrufer**: `@Allow { code = "LYR-SEM0076" }` auf der Funktion, die die alte
  Form bewusst benutzt. Vorbild: Rust `#[allow(deprecated)]`, Scala `@nowarn`. Preis: ein
  compilergelesenes Attribut — also genau das, was META-16 zu begrenzen versucht, und es bräuchte
  Member- und Statement-Ziele, die es nicht gibt (META-02).
- **B — Pragma/Kommentar-Direktive** `// lyric:allow LYR-SEM0076` für die nächste Zeile.
  Vorbild: C# `#pragma warning disable`, ESLint. Preis: eine zweite Sprache im Kommentar — und
  der Formatter müsste sie kennen, sonst verschiebt er sie weg.
- **C — Eintrag in `lyric.json`**: `"allow": ["LYR-SEM0076"]` projektweit, optional pro
  `sourceRoot`-Zweig. Vorbild: C# `<NoWarn>`, `.eslintrc`. Preis: grobkörnig — wer eine Zeile
  ausnimmt, nimmt alle aus; dafür kostet es **keine Sprachänderung**.
- **D — CLI-Schalter** `--allow <code>`. Preis: nicht im Projekt festgehalten, jeder Aufrufer
  muss ihn kennen; CI und Editor divergieren.
- **E — gar nicht, aber `until` respektiert eine Migrationsfrist**: solange `until` in der
  Zukunft liegt, ist `SEM0076` eine Note statt einer Warnung. Preis: löst nur die
  Deprecation-Familie, nicht `comptime` oder `@NonExhaustive`.

**Empfehlung: C für 4.x, A oder B als 5.0-Frage — und die Granularität ist die eigentliche
Entscheidung.** C ist sofort baubar, braucht keine Syntax und deckt den Fall, für den die
Unterdrückung *jetzt* gebraucht wird: eine Codebasis migriert über mehrere Releases und will die
CI grün halten. A/B lösen den Fall „diese eine Zeile ist Absicht“, den ein Shim braucht, und
setzen META-17 D (Diagnose-ID pro Zeile) voraus. **Was nicht geht: D allein** — eine
Unterdrückung, die nicht im Projekt steht, ist keine.

**Geltungsbereich, der mitentschieden werden muss:** Zeile, Funktion oder Modul. Vorschlag:
C wirkt auf das Projekt, A/B auf die **Deklaration**, an der sie steht (nicht auf eine Zeile) —
weil eine Zeile im Formatter wandert, eine Deklaration nicht.

**Bricht:** nein (additiv). **4.x-Warnstufe:** keine — dies *ist* die Warnstufe für alles andere.
**Hängt ab von:** Diagnostik-Gebiet, Build-/Projekt-Gebiet, META-17 D, META-02 (Ziele für A).

---

### META-23 — Wer trägt `LYR-SEM0081`, wenn eine Quell-Abhängigkeit eine abgelaufene Zusage enthält?

**Heute:** der **Konsument**, und zwar ohne Ausweg. Gemessen (`probes/meta-rev/dep/`): ein
importiertes Modul mit `@Deprecated { until = "1.0" }` bricht den Build des Konsumenten mit einem
Fehler, der in eine Datei zeigt, die der Konsument nicht besitzt — **auch dann, wenn er das
deprecatete Symbol gar nicht importiert**, allein weil er das Modul importiert. Verglichen wird
gegen die Toolchain-Version, unbedingt (gelesen, `DeprecationPromise.cs:49-56`). Der
Implementierungskommentar nimmt ausdrücklich an, der Fehler lande „on the maintainer preparing
the release“ (gelesen, `DeprecationPromise.cs:15-18`).

**Warum das jetzt eine Frage ist:** mit Paketen aus **Quelltext** (v5-Liste, Stufe A; `lyric.json`
kennt `dependencies` bereits, gelesen `docs/guide/16-building.md:313`) ist der Fremdcode-Fall der
Normalfall, nicht die Ausnahme. Eine Bibliothek, die ihre eigene Uhr auf 5.0 stellt, macht jedes
Konsumentenprojekt unbaubar, sobald 5.0 erscheint — auch wenn der Konsument nichts falsch gemacht
hat.

**Optionen**
- **A — die Uhr gilt nur unterhalb des eigenen `sourceRoot`.** Der Konsument sieht `SEM0076`
  (Warnung an der Benutzung), nie `SEM0081`. Vorbild: keines direkt; am nächsten Rusts Regel, dass
  `deprecated` in Dependencies nur warnt. Preis: die Zusage wird erst beim *Eigentümer* fällig —
  was genau die Absicht des Doc-Kommentars ist. Der Konsument erfährt nichts, bis die Bibliothek
  eine neue Version veröffentlicht.
- **B — Pin je Abhängigkeit**: `"dependencies": { "geo": { "path": "…", "asOf": "4.6" } }` — die
  Uhr der Abhängigkeit wird gegen `asOf` verglichen, nicht gegen die Toolchain. Vorbild: Rusts
  `edition` pro Crate, C#s `<LangVersion>` pro Projekt. Preis: ein Feld mehr, und der Konsument
  kann die Uhr einer fremden Bibliothek beliebig lange anhalten.
- **C — die Zusage wird im Paket-Header eingefroren**: ein kompiliertes Paket trägt die
  Toolchain-Version, unter der es gebaut wurde; die Uhr vergleicht dagegen. Preis: gilt nur für
  kompilierte Pakete, nicht für Quellabhängigkeiten — also genau nicht für den Fall, der die
  Frage stellt.
- **D — bleiben, plus META-22**: der Konsument unterdrückt `SEM0081` projektweit. Preis: eine
  Unterdrückung für einen **Fehler**, nicht für eine Warnung — das ist eine andere Kategorie, und
  wer `SEM0081` abschalten kann, hebt die ganze Zusage auf.

**Empfehlung: A, und B als Option für den Sonderfall.** A stellt die Regel wieder her, die der
Doc-Kommentar ohnehin beschreibt: die Zusage kommt bei dem fällig, der sie gegeben hat. Der
Konsument bekommt die Warnung (`SEM0076`) und damit die Information, ohne die Sanktion. B ist die
Antwort für den Fall, dass ein Projekt eine fremde Bibliothek bewusst über ihre Frist hinaus
verwendet — aber B ohne A verschiebt nur die Frage, und **D ist gefährlich**, weil es den
Unterschied zwischen Warnung und Fehler einebnet.

**Bricht:** A ändert eine heute wirksame Diagnose von Fehler zu Warnung für fremden Code — für
den Eigentümer bleibt alles gleich. Einstufung: **minor** (ein Fehler wird milder, nichts wird
strenger).
**4.x-Warnstufe:** keine nötig; die Änderung macht Builds grüner, nicht röter.
**Hängt ab von:** Build-/Projekt-Gebiet (`dependencies`, `sourceRoot`), Paket-Gebiet, META-22.

---

### META-24 — Wie setzt man eine Deprecation-Uhr auf ein Interface, einen Typalias, ein globales `let` oder einen `extend`-Block?

**Heute:** gar nicht. Alle vier sind `LYR-PAR0042`, `@Deprecated` eingeschlossen (gemessen,
`meta-rev/m2`–`m5`; die Meldungen unterscheiden die vier Fälle sogar namentlich: „on an
interface“, „on a type alias“, „on a global binding“, „on an extend block“).

**Warum das jetzt eine Frage ist:** die 5.0-Liste will genau solche Deklarationen ersetzen —
`Indexable<T>` → `Index<K,V>` und `IndexSet<K,V>` (Posten 9), neue Operator-Interfaces (Posten 16),
ein `Format`-Interface (Posten 22), alles gelesen aus `lyric-v5-features.md`. Und `OnMethod` soll
laut META-03 C verschwinden — auch ein Interface (L3a). **Die Uhr der Sprache erreicht die Dinge
nicht, die 5.0 ersetzen will.**

**Optionen**
- **A — META-02 um Zielkinder erweitern**: Interface, Typalias, globales `let`, `extend` bekommen
  Attributzeilen. Vorbild: C# `AttributeTargets.Interface`, Java `ElementType.TYPE`. Preis: derselbe
  Formatpreis wie META-02 B — neue `targetKind`-Werte, die ein 4.x-Leser ablehnt
  (gelesen, `docs/Bytecode.md:471`) — und für `extend`/globales `let` gibt es im Format **gar
  keine Zeile**, auf die ein Ziel zeigen könnte.
- **B — eine zweite Form, die kein Attribut ist**: ein Modifier `deprecated interface Foo`.
  Vorbild: D (`deprecated` ist eine Storage-Class, kein Attribut). Preis: **Rule 2 frontal** —
  zwei Mechanismen für „das geht weg“, und die beiden werden über kurz oder lang uneins.
- **C — die Uhr verlässt den Quelltext**: eine Liste in `lyric.json` oder eine Datei
  `deprecations.json`, die Namen und Fristen führt. Vorbild: keines in dieser Liste; am nächsten
  ein API-Baseline-File (Roslyn `PublicAPI.Shipped.txt`). Preis: die Zusage steht nicht mehr
  neben dem, was sie betrifft — genau der Zustand, den `DeprecationPromise.cs:9-12` als das
  ursprüngliche Problem beschreibt („it lived in a release note“).
- **D — Doc-Kommentar-Konvention plus CHANGELOG**, wie es META-03 für `OnMethod` vorschlägt.
  Preis: nichts wird erzwungen; die Uhr ist wieder Gedächtnis.
- **E — gar nichts, und 5.0 entfernt diese Deklarationen ohne Uhr.** Preis: der größte Bruch der
  Liste (`Indexable<T>`) käme ohne Vorwarnung im Quelltext — nur in der Release-Notiz.

**Empfehlung: A für Interface und Typalias, D für `extend` und globales `let`.** Interface und
Typalias sind benannte Deklarationen mit einem Typindex bzw. einem Namen, auf den eine Zeile
zeigen kann; `extend` und ein globales `let` haben keinen stabilen Zielbegriff im Format, und ein
Format-Zielkind dafür zu erfinden, ist teurer als der Nutzen. **Wichtig:** A gehört damit in
dieselbe Formatrunde wie META-02 und trägt denselben Preis — das muss zusammen entschieden
werden, nicht nebeneinander. **B ausdrücklich nicht**, aus Rule 2.

**Bricht:** wie META-02 (neues Zielkind, 4.x-Leser lehnt das Modul ab).
**4.x-Warnstufe:** dieselbe Release-Notiz wie META-02; im Quelltext keine.
**Hängt ab von:** META-02, META-03, Bytecode-Gebiet, Embedding-Gebiet.

---

### META-25 — Erbt ein Attribut über Konformanz?

**Heute:** die Frage kann sich nicht stellen, weil ein Attribut auf einem Interface `LYR-PAR0042`
ist (gemessen, `m2_dep_interface.lyr`). Klassenvererbung gibt es nicht (`class D : B` ist ein
Parserfehler). **Aber die Marker-Konformanz reist die Elternkette bereits**: ein eigenes
`interface Marker :: [OnFunction]` macht `struct Tag :: [Marker]` zu einem gültigen
Funktionsattribut (gemessen, `meta-rev/m10_markerchain.lyr` → `ok`), und die `WithArg<T>`-Zusage
tut dasselbe seit 3.9.1 (gelesen, `CHANGELOG.md:941-945`, `LYR-SEM0095`).

**Warum das jetzt eine Frage ist:** ein ECS-SDK will schreiben „jeder Typ, der `Renderable`
erklärt, ist eine Komponente“, ohne dass jeder Nutzer `@Component` abschreibt. C# hat dafür
`[AttributeUsage(Inherited = true)]`, Java `@Inherited`. Lyric hat die Frage nicht gestellt — sie
steht in keiner Designnotiz (gelesen: kein Treffer für „Inherited“ in `design/`, `docs/`).

**Optionen**
- **A — nein, nie.** Ein Attribut beschreibt genau die Deklaration, über der es steht. Vorbild:
  Rust (Attribute erben nicht). Preis: das SDK schreibt „setze `@Component` auf jede Komponente“
  in seine Doku und prüft es zur Laufzeit; jeder Nutzer schreibt die Zeile ab.
- **B — ja, über die Interface-Elternkette**, wenn META-24 A Interfaces Attributzeilen gibt: der
  Host sieht `@Component` auf jedem Typ, der ein Interface erklärt, das die Zeile trägt. Vorbild:
  Java `@Inherited` (dort nur über Klassenvererbung, nicht über Interfaces — Lyric hätte hier den
  **stärkeren** Fall, weil Konformanz sein einziger Vererbungsbegriff ist). Preis: die
  Attributzeilen des Moduls sind nicht mehr die Wahrheit — der Host muss die Konformanzketten
  auflösen, und die stehen in der Typentabelle, nicht in Abschnitt 11. **Das bricht die eine
  Eigenschaft, die §2.1 als Lyrics Alleinstellungsmerkmal benennt: der Leser braucht kein
  geladenes Typsystem.**
- **C — Opt-in am Attribut**, `:: [Inherited]` als weiteres Markerinterface, und der **Compiler**
  schreibt die Zeile auf jeden Konformer aus. Vorbild: C# `Inherited = true`, aber mit Lyrics
  Mechanismus. Preis: ein Marker mehr; dafür bleibt Abschnitt 11 vollständig und
  selbstbeschreibend — der Host liest weiter nur Zeilen.
- **D — das SDK löst es über ein Markerinterface statt über ein Attribut**: `Renderable` erbt
  von `Component`, und der Host fragt die Konformanz ab statt das Attribut. Preis: der Host
  braucht dafür eine Konformanzabfrage über das Format, die es heute nicht gibt — aber das ist
  eine Frage des Embedding-Gebiets, nicht dieses hier.

**Empfehlung: C, falls META-24 A kommt; sonst A.** C ist die einzige Option, die den Wunsch
erfüllt, ohne die Selbstbeschreibung des Abschnitts aufzugeben: der Compiler **materialisiert**
die geerbte Zeile, statt den Host raten zu lassen. Der Preis ist Zeilen-Aufblähung (n Konformer =
n Zeilen), und der ist in Bytes messbar und damit ehrlich. **B ausdrücklich nicht**, weil es den
Leser zwingt, das Typsystem zu rekonstruieren.

**Bricht:** nein (C ist additiv: ein neuer Marker, mehr Zeilen). **4.x-Warnstufe:** keine.
**Hängt ab von:** META-24 A (sonst gibt es keine Interface-Zeile zum Erben), META-02,
Embedding-Gebiet.

---

### META-26 — Ist die Feldreihenfolge eines Attribut-Structs Teil seines öffentlichen Vertrags?

**Heute:** im **Format** ja, in der **Host-API** teilweise nein. Gelesen:

- `docs/Bytecode.md:455-458`: „A row is complete: one value per field of the attribute type, in
  field declaration order … **The position IS the field index**, which is why no index is stored“.
- `docs/Bytecode.md:471-477`: ein Leser muss „a value count differing from the struct's field
  count“ und „a value tag differing from the field tag at its position“ ablehnen.
- `docs/guide/15-attributes.md:62-66`: `WithArg<T>` füllt „the attribute's **FIRST** field“.
- Gegenlauf: `AttributeUse.Value(string field)` erlaubt dem Host den Zugriff **nach Namen**, über
  Abschnitt 12 (gelesen, `src/Lyric.Core/Bytecode/ModuleAttributes.cs:144-152`).

**Die Gefahr, präzise:** ein SDK, das zwei Felder **umsortiert**, ändert still die Bedeutung jeder
`@On(x)`-Stelle — denn `WithArg` füllt das erste Feld, und das ist nach der Umsortierung ein
anderes. Ein Host, der `Values[0]` liest, bricht mit; ein Host, der `Value("limit")` liest, ist
sicher. Ein **hinzugefügtes** Feld ist dagegen unkritisch, solange es angehängt wird und einen
Literal-Default hat: der Compiler füllt es (`docs/Bytecode.md:455-457`), und `valueCount` wird
gegen den `fieldCount` **desselben Moduls** geprüft, also konsistent.

Nirgends steht das jemandem geschrieben: `docs/guide/15-attributes.md` sagt dem SDK-Autor nichts
über Feldreihenfolge als Vertrag (gelesen, kein Treffer).

**Optionen**
- **A — nichts festlegen.** Preis: der Fehler bleibt still und trifft die Nutzer des SDKs, nicht
  seinen Autor.
- **B — Zusatzregel im Guide, unverbindlich**: „Felder eines Attribut-Structs nur anhängen; die
  Reihenfolge ist Teil des Vertrags, sobald `WithArg<T>` deklariert ist“. Vorbild: Protobuf-Regel
  „Feldnummern nie wiederverwenden“. Preis: null, wirkt aber nur auf Leser der Doku.
- **C — der Compiler warnt**, wenn ein `pub` Attribut-Struct mit `WithArg<T>` seine
  Feldreihenfolge ändert. Preis: braucht eine Baseline über Versionen hinweg — die gibt es nicht
  und sie wäre ein eigenes Werkzeug (API-Baseline).
- **D — `WithArg<T>` nennt das Feld**: `WithArg<int, "limit">` statt „erstes Feld“. Vorbild:
  keines direkt. Preis: ein Stringparameter in einem Typ; dafür verschwindet die
  Reihenfolgeabhängigkeit vollständig.
- **E — Hosts auf `Value(name)` verpflichten**: der Guide 14 (Embedding) sagt, `Values[i]` sei
  kein stabiler Zugriff. Preis: null; löst die Hostseite, nicht die `WithArg`-Seite.

**Empfehlung: B + E sofort, D prüfen.** B und E kosten nichts und schließen die beiden Hälften der
Lücke (SDK-Autor, Host-Autor). **D ist die eigentlich saubere Lösung** — sie macht aus einer
Positionszusage eine Namenszusage und passt zu Lyrics Linie, dass nichts aus Versehen positionell
wird (der `WithArg`-Doc-Kommentar sagt genau das, gelesen `stdlib/std/core.lyr:491-494`) — aber
sie bricht jede `WithArg<T>`-Deklaration und gehört damit an META-21/5.0. C nein: eine Baseline
ist ein eigenes Projekt.

**Bricht:** B/E: nein. D: minor (jede `WithArg<T>`-Deklaration, nicht jede Benutzung).
**4.x-Warnstufe:** bei B/E keine.
**Hängt ab von:** Embedding-Gebiet (Guide 14), META-21, META-05 (wenn Arrays kommen, wird die
Zeile länger, die Positionsregel aber nicht anders).

---

### META-27 — Darf `--release` Attributzeilen entfernen?

**Heute:** nein, und das ist eine Formatpflicht. Gelesen, `docs/Bytecode.md:467-469`: „A compiler
must keep an attributed function alive: the row is a promise that the index is valid, and the host
is a caller the reachability analysis cannot see — the same standing as the entry point.“
Gemessen (`meta-rev/m13_privroot.lyr` gegen Kontrolle `m13b_control.lyr`): eine **private**,
attributierte, nirgends gerufene Funktion überlebt `lyrc lower --release` samt DCE; ohne das
Attribut verschwindet sie.

**Die Folge:** jedes Attribut hält toten Code in jedem `lyric pack`-Binary am Leben, und ein SDK
kann das nicht abstellen. Wer `@Test`-artige Marker großzügig verteilt, verschiebt Code in die
Auslieferung. Für `lyrtest` ist das gewollt (der Testbaum wird ohnehin nie gepackt, gelesen
`docs/guide/20-testing.md:3-5`); für ein `@Command`-SDK mit hundert Handlern ist es Gewicht, das
niemand wollte.

**Optionen**
- **A — bleiben.** Preis: der Host darf jede Zeile aufrufen, und das Binary trägt alles. Ehrlich,
  aber teuer.
- **B — `lyric.json` nennt die Attribute, die ein Host tatsächlich liest**:
  `"hostAttributes": ["System", "Command"]` — alles andere verliert seinen Wurzelstatus, die
  Zeilen bleiben (sie sind Datum), die Funktionen fallen. Vorbild: Rusts `--cfg`-gesteuerte
  Codepfade, .NET `TrimmerRootDescriptor`. Preis: die Formatzusage „die Zeile ist ein Versprechen,
  dass der Index gültig ist“ (`docs/Bytecode.md:467`) **gilt dann nicht mehr** — eine Zeile könnte
  auf eine entfernte Funktion zeigen. Das ist ein Formatbruch, kein Konfigschalter.
- **C — wie B, aber die Zeile fällt mit**: was kein Host liest, verliert Zeile *und* Funktion.
  Preis: die Konfiguration entscheidet, was im Modul steht — zwei Builds desselben Quelltexts
  haben verschiedene Abschnitte 11. Dafür bleibt die Formatzusage intakt.
- **D — ein Marker am Attribut**, `:: [Advisory]`: eine Zeile, die *keine* DCE-Wurzel ist, und
  deren Ziel verschwinden darf. Preis: zwei Sorten Attributzeilen im Format; ein Host muss mit
  einem ungültigen Index rechnen — derselbe Bruch wie B, nur pro Attribut statt pro Projekt.
- **E — `lyric pack` meldet es**: keine Verhaltensänderung, aber der Pack-Lauf sagt, wie viele
  Funktionen nur wegen einer Attributzeile leben. Preis: null.

**Empfehlung: E sofort, C für 5.0 prüfen, B und D nicht.** E kostet nichts und macht das Problem
messbar, bevor jemand es löst — ohne Zahl weiß niemand, ob es eines ist. **C ist die einzige
Option, die die Formatzusage nicht aufweicht**: wenn eine Zeile fällt, fällt sie ganz, und was
im Modul steht, bleibt wahr. Der Preis (zwei Builds, zwei Abschnitte 11) ist derselbe, den
`--no-debug-info` schon hat (gelesen, `docs/guide/16-building.md:327-331`). B und D machen den
Index unzuverlässig, und das ist die eine Eigenschaft, auf der die ganze Host-Geschichte steht.

**Bricht:** E: nein. C: der Modulinhalt hängt an einer Konfiguration — für Leser additiv, für
Reproduzierbarkeit eine neue Eingabe. Einstufung: **minor**.
**4.x-Warnstufe:** keine; E kann in 4.7.
**Hängt ab von:** Build-/Projekt-Gebiet, Bytecode-Gebiet, Embedding-Gebiet.

---

### META-28 — Sieht ein Doc-Test die privaten Namen seines Moduls, unter welchen Capabilities läuft er, und darf er `comptime` enthalten?

**Heute:** es gibt keine Doc-Tests. Aber der Maßstab steht: `lyric test` kompiliert den Testbaum
als **eigene Kompilation** gegen den `sourceRoot` (gelesen, `lyrc --help`: „check [<dir>]: its
source root as one compilation, its test root as another“; `docs/guide/20-testing.md:51-54`), und
gemessen (`meta-rev/proj/`) ist ein privater Name dort `LYR-RES0004: 'hidden' is not public in
'mathy'`; Kontrolllauf mit nur dem `pub`-Import: `1 test(s), all passed`. Capabilities: ein Test
hat „the whole standard library and every capability“ (gelesen, `docs/guide/20-testing.md:51-54`).

**Die drei Teilfragen, die META-20 offengelassen hat:**

**(a) Sichtbarkeit.** Rust kompiliert jeden Doc-Test als **fremde Crate** — nur `pub`, und genau
das macht ihn zum Benutzbarkeits-Test. D und Nim führen ihn **im Modul** aus und sehen alles.
META-20 empfiehlt das D/Nim-Werkzeug-Modell; die Sichtbarkeitsfolge steht dort nicht.

- **A — nur `pub`** (Rust, und Lyrics heutige Testregel). Preis: ein Doc-Beispiel kann private
  Details nicht zeigen. Gewinn: **eine** Sichtbarkeitsregel für allen Testcode.
- **B — alles** (D, Nim). Preis: zwei Regeln; ein Beispiel in der Doku kann Code zeigen, den der
  Leser nicht schreiben kann.

**Empfehlung: A** — weil META-20 B den Doc-Test als gewöhnlichen `@Test` realisiert und damit die
bestehende Regel erbt, statt eine zweite zu erfinden.

**(b) Capabilities.** Ein `@Test` hat heute alles (gelesen). Ein Doc-Test im Guide eines
fremden Pakets ist derselbe Code auf einer fremden Maschine.

- **A — wie ein Test: alles.** Preis: `lyric test` auf einem frisch geklonten Paket führt
  fremden Code mit vollem Zugriff aus — was `lyric run` auch tut, also konsistent.
- **B — `Capability.None` plus das, was `lyric.json` erklärt.** Vorbild: Lyrics eigener
  `comptime`-Runner. Preis: viele Beispiele (Datei lesen, Zeit) laufen nicht mehr.

**Empfehlung: A, ausdrücklich und dokumentiert.** Inkonsistenz wäre teurer als die Gefahr, und
die Gefahr ist dieselbe wie bei `lyric test` heute. Aber sie **gehört hingeschrieben**.

**(c) `comptime` im Doc-Test.** Ein Doc-Test ist ein Programm; ein Programm darf `comptime`.

- **A — ja, ohne Sonderregel.** Preis: ein Doc-Test kann am Budget scheitern, und die Diagnose
  zeigt in einen Doc-Kommentar — ein Span, den der Parser heute schon führt (gelesen,
  `ParsedModule.cs:14-31`).
- **B — nein.** Preis: eine Sonderregel ohne belegten Grund.

**Empfehlung: A.**

**Bricht:** nein (alles additiv). **Hängt ab von:** META-20, Test-/Toolchain-Gebiet,
Capability-Gebiet, META-12/META-30 (Budget im Doc-Test).

---

### META-29 — Bekommt `LYR-CT0002` einen Backtrace der Compile-Zeit-Auswertung?

**Heute:** nein. Gemessen (`meta-rev/m7_backtrace.lyr`): `comptime level1(5)` → `level2` →
`level3` → `panic("too big")` meldet

```
m7_backtrace.lyr:8:14: error[LYR-CT0002]: 'comptime' expression could not be evaluated:
                        the module's constant initializer panicked [LYR-VM0011]: too big
```

— die Site, der Text, **keine Zeile aus den drei gerufenen Funktionen**. Das Material ist da:
das Auswertungsmodul trägt die Source-Map, und `design/macros.md:75` nennt die Erweiterung selbst
(„Backtrace als Notes der Diagnose ausgeben“), `:107` führt sie unter „Was fehlt“ (beides
gelesen). Die erste Fassung hat aus derselben Zeile die Array-Lücke übernommen, diese aber nicht.

**Warum das jetzt eine Frage ist:** ohne Backtrace ist eine fehlschlagende `comptime`-Auswertung
in **fremdem Bibliothekscode** nicht debugbar — der Nutzer sieht seine eigene Zeile und einen
Text, den eine Funktion drei Ebenen tiefer geschrieben hat. Zig und rustc drucken beide einen
const-eval-Stack.

**Optionen**
- **A — Notes mit dem vollen Stack.** Vorbild: rustc (`note: inside \`f\``, verschachtelt), Zig
  (Aufrufkette mit Quellzeilen). Preis: die Diagnose wird länger; das Auswertungsmodul muss die
  Source-Map behalten, auch wenn `--no-source-map` gesetzt ist (gelesen, das ist heute eine
  Profil-Option, `lyrc --help`).
- **B — nur der innerste Frame** („panicked in `level3` at …:2“). Preis: bei Rekursion nutzlos,
  sonst fast so gut wie A für die Hälfte der Arbeit.
- **C — der Stack hinter einem Schalter** (`--explain-comptime`). Preis: der Nutzer muss wissen,
  dass es ihn gibt.
- **D — `lyric expand` druckt die Site samt Auswertung** (siehe META-19 B). Preis: ein Werkzeug
  statt einer Diagnose; hilft nicht in der CI-Ausgabe.

**Empfehlung: A, mit B als Mindestmaß.** Die Kosten sind klein und der Gewinn ist die
Debugbarkeit, an der `design/macros.md:43-56` selbst alle Makrosysteme misst — ein Lyric, das
seine Compile-Zeit-Fehler nicht lokalisieren kann, verliert genau das Argument, mit dem es gegen
Makros argumentiert. **Die Source-Map-Frage gehört mitentschieden**: das Auswertungsmodul ist ein
Compiler-Internum, seine Source-Map sollte nicht an einem Nutzerprofil hängen.

**Bricht:** nein (mehr Notes an einer bestehenden Diagnose). **4.x-Warnstufe:** keine; sofort
machbar.
**Hängt ab von:** Diagnostik-Gebiet, Lowering-Gebiet (Source-Map des Auswertungsmoduls),
META-19.

---

### META-30 — Ist das Compile-Zeit-Budget pro Site, pro Modul oder kumulativ — und wird dieselbe Site zweimal ausgewertet?

**Heute:** unklar, und die Meldung sagt nichts dazu. Gemessen (`meta-rev/m8_budget.lyr`):
„the module's constant initializer did not finish within the compile-time budget“ — der Text sagt
„the module's constant initializer“, also klingt es nach **pro Auswertungsfunktion**, aber
`design/macros.md:70-72` beschreibt „pro Site `Invoke(index, budget)`“ (gelesen), also **pro
Site**. Ob die Summe über alle Sites begrenzt ist, steht nirgends. **Behauptet:** es ist pro Site,
und es gibt keine Gesamtgrenze — das ist aus dem Designtext abgeleitet, nicht gemessen; eine
Messung dafür bräuchte 200 billige Sites und eine Zeitmessung, und ihr Ergebnis wäre nicht
eindeutig interpretierbar.

**Warum das jetzt eine Frage ist:** META-12 fragt nur, ob die *Zahl* in die Spec gehört. Die
**Granularität** entscheidet aber darüber, ob ein Modul mit 200 billigen Sites baut oder an der
Summe stirbt — und sie ist die Voraussetzung für das Caching, das META-11 D als Option nennt: ein
Cache pro Site-Hash setzt voraus, dass das Budget pro Site zählt und dass zwei identische Sites
dasselbe Ergebnis haben dürfen.

**Optionen**
- **A — pro Site, keine Gesamtgrenze.** Vorbild: Zig (`@setEvalBranchQuota` gilt pro
  Auswertung). Preis: n Sites × Budget ist die Obergrenze für einen Build; ein Modul kann den
  Compiler beliebig lange beschäftigen, ohne eine Grenze zu verletzen.
- **B — pro Modul kumulativ.** Vorbild: rustcs `const_eval_limit` war historisch pro Auswertung,
  nicht kumulativ — also kein direktes Vorbild. Preis: eine Site, die für sich harmlos ist, kann
  an einer anderen scheitern; das Fehlerbild wandert mit dem Editieren.
- **C — beides: pro Site eine Grenze, pro Modul eine höhere.** Preis: zwei Zahlen in der Spec,
  zwei Fehlerbilder — aber es ist die einzige Kombination, die sowohl Einzelexzesse als auch
  Summen abfängt.
- **D — pro Site, plus Deduplizierung**: zwei syntaktisch identische Sites im selben Modul werden
  einmal ausgewertet und teilen das Ergebnis. Vorbild: Rusts const-Promotion, Scalas
  `inline`-Caching. Preis: setzt voraus, dass eine Site **rein** ist — was sie per Konstruktion
  ist (Capability.None, keine Lokalen), also billig. Gewinn: die Grundlage für META-11 D.

**Empfehlung: A + D, mit der Zahl in der Meldung (META-12 E).** A ist das, was gebaut ist
(behauptet) und was Zig macht; D ist billig, weil Reinheit ohnehin erzwungen wird, und es ist die
Vorstufe des Editor-Cachings. C wäre korrekter, aber zwei Zahlen brauchen zwei Begründungen, und
für die zweite fehlt der belegte Bedarf. **Was in jedem Fall entschieden werden muss, bevor
META-11 D gebaut wird:** wird dieselbe Site über *Compilerläufe hinweg* gecacht (dann braucht der
Cache einen Schlüssel, der die Toolchain-Version enthält — sonst überlebt ein alter
`sin`-Wert einen Toolchain-Wechsel, siehe META-37).

**Bricht:** nein. **Hängt ab von:** META-12, META-11 D, META-37, Spec-Gebiet.

---

### META-31 — Welchen Typ liefert `embed()`, woran hängt `embedBytes()`?

**Heute:** beide existieren nicht. Gemessen ist die Sperre, die `embedBytes` heute unmöglich
macht: ein `int[]`-Ergebnis einer `comptime`-Site ist `LYR-SEM0100` — „which the compiled module
has no way to hold as a literal“ (`meta-rev/m9_ctarray.lyr`). Das Format kennt im
`ConstValue`-Vokabular keine Sequenz (gelesen, `docs/Bytecode.md:437-446`: Integer, Char, f32/f64,
Bool, String, Enum-Tag — mehr nicht).

**Warum das jetzt eine Frage ist:** META-10 empfiehlt `embed` **und** `embedBytes`, META-07
vertagt Arrays. Ohne Array-Werte im Modul gibt es kein `embedBytes`. Und selbst mit ihnen ist
offen, **was** entsteht.

**Optionen für `embed` (Text)**
- **A — `string`.** Vorbild: Zig `@embedFile` (dort ein Array, aber als String benutzbar), Rust
  `include_str!`. Preis: keiner — der Stringpool kann es heute (gelesen,
  `docs/Bytecode.md:443`). Kodierung muss festgelegt werden: UTF-8, und eine ungültige Sequenz ist
  ein Compile-Fehler.

**Optionen für `embedBytes`**
- **B — `byte[]` als Array-`ConstValue`** (META-07 B + META-05 B). Preis: eine neue Formatform;
  das Array ist ein **Heap-Objekt pro Programmstart**, gebaut im `<globals>`-Prolog — bei einer
  2-MB-Datei sind das 2 MB Kopierarbeit beim Start und 2 MB im Modul.
- **C — ein eigener Modulabschnitt „Blobs“**, auf den ein Wert als Index zeigt; die VM mappt ihn
  ohne Kopie. Vorbild: Zigs `@embedFile` liefert ein Compile-Zeit-Array, das im Datensegment
  landet; C#-Embedded-Resources. Preis: ein neuer Abschnitt (skippable, also der billigste
  Formatzug der Liste) **und** ein Werttyp, der auf ihn zeigt — also doch eine Formänderung im
  Typsystem, es sei denn, das Ergebnis ist ein `opaque type`-Handle.
- **D — kein `embedBytes`, nur `embed` als `string`**, Binärdaten über Base64 im Quelltext.
  Preis: 33 % mehr Bytes und eine Laufzeit-Dekodierung — das ist die heutige Notlösung, nur
  ausgesprochen.

**Empfehlung: A für `embed`. Für `embedBytes`: C prüfen, B als Rückfallposition, D nein.** C ist
architektonisch richtig (Bytes gehören nicht in den Ausführungsprolog) und formatseitig billig,
weil ein neuer Abschnitt als Ganzes überspringbar ist — im Gegensatz zu META-02/04 ist das der
Zug, den `docs/Bytecode.md:114-118` ausdrücklich als Minor erlaubt. Der Haken ist der Werttyp;
`opaque type` plus ein `bytes(handle): byte[]`-Zugriff wäre der Weg, und das gehört dem
FFI-/Laufzeit-Gebiet.

**Zeitliche Bedingung:** solange META-07 B nicht da ist, liefert Stufe 1 **nur `embed`**. Das muss
in der META-10-Empfehlung stehen, nicht als Fußnote.

**Bricht:** A: nein. B: neuer `ConstValue`-Tag (wie META-05). C: neuer Abschnitt, skippable —
minor nach `docs/Bytecode.md:114-118`.
**4.x-Warnstufe:** keine.
**Hängt ab von:** META-10, META-07 B, META-05 B, Bytecode-Gebiet, Laufzeit-/FFI-Gebiet.

---

### META-32 — Kennt das Werkzeug Attribute?

**Heute:** halb. Gelesen: der LSP führt `AttributeNode` in `NameSpans` (`:35`), und die Datei
sagt, wofür: „references underline the name, a rename edits exactly it“ — **Rename und
Referenzen funktionieren also auf Attributnamen** (`src/Lyric.Lsp/Analysis/NameSpans.cs:6-19`,
benutzt von `RenameProvider.cs` und `ReferenceProvider.cs`). Was fehlt: die
**Vervollständigung**. Die Trigger-Zeichen sind `["."]` (gelesen,
`src/Lyric.Lsp/LspServer.cs:1018`), `@` ist keines, und im ganzen LSP gibt es keinen
attributbezogenen Completion-Pfad (gelesen, `grep -rniE "attribute" src/Lyric.Lsp/` liefert
außer `NameSpans` und zwei Kommentaren nichts).

**Warum das jetzt eine Frage ist — und warum sie kein Komfortthema ist.** Das Attributvokabular
einer Lyric-Codebasis kommt aus **fremden SDKs**: `@System` von `engine.ecs`, `@Command` von
`std.cli`, `@Test` von `std.test`. Es gibt kein Schlüsselwort, das es aufzählt, und kein
`std.core`-Kapitel, das es vollständig führt. **Vervollständigung ist der einzige Weg, es zu
entdecken.** Das Dossier plant LSP-Arbeit für `comptime` (META-11 C) und für die Synthese
(META-19 C) und hat diese Frage nicht gestellt.

**Optionen**
- **A — `@` als Trigger, Vervollständigung der sichtbaren Attribut-Structs.** Ein Kandidat ist
  ein `pub` Struct, das einen Marker für die Position erklärt, in der der Cursor steht (Funktion,
  Typ, Modulkopf — und die Position ist syntaktisch bekannt). Vorbild: Java/C#-IDEs, die genau
  das seit 20 Jahren tun. Preis: eine Completion-Quelle mehr; die Kandidatenmenge steht in der
  Sema (Konformanzliste je Struct).
- **B — A plus Feldnamen nach `{`.** Preis: ein zweiter Kontext, aber er ist derselbe wie
  Struct-Init — und den kann der LSP schon (gelesen, `StructInitField` steht in `NameSpans`).
  **Das ist der Gewinn der META-21-Korrektur**: weil die Attributklammer die Struct-Init-Form
  *ist*, kostet die Feldvervollständigung nichts Neues.
- **C — Hover zeigt den Doc-Kommentar des Attributs samt Zielmenge** („`@Deprecated` — sitzt auf
  Modul, Typ, Funktion; Felder `message`, `until`“). Preis: klein; die Zielmenge steht in der
  Konformanzliste.
- **D — nichts, aber `lyric` bekommt ein Verb `lyric attributes [<modul>]`**, das die sichtbaren
  Attribut-Structs auflistet. Preis: hilft in der CLI, nicht im Editor.
- **E — nichts.** Preis: das Attributvokabular ist nur über die SDK-Doku auffindbar.

**Empfehlung: A + B + C, in dieser Reihenfolge, und vor META-11 C.** Das ist billige,
hochwirksame Werkzeugarbeit an einer Stelle, wo die Sprache **keine** Alternative bietet — anders
als bei `comptime`, wo der Nutzer immerhin ein Schlüsselwort kennt. D als Beigabe für Skripte.

**Bricht:** nein. **4.x-Warnstufe:** keine; sofort machbar, ohne jede Sprachentscheidung.
**Hängt ab von:** Editor-/LSP-Gebiet. **Wird leichter durch:** META-01 A (qualifizierte Namen
machen die Kandidatenliste eindeutig), META-21 A (die Klammerform bleibt Struct-Init).

---

### META-33 — Gehört eine Schreibweisen-Migration in `lyric fmt` oder in `lyrfix`?

**Heute:** `lyric fmt` schreibt Attributzeilen bereits um — gemessen (`meta-rev/m6_fmt.lyr`):
`@A` und `@B { n = 1 }` untereinander werden zu `@[A, B { n = 1 }]`. Das ist eine reine
Normalform, **bedeutungserhaltend**. `lyrfmt --check` schreibt nichts und „exit 1 when any
[file] would [change]“ (gelesen, `lyrfmt --help`) — also ein CI-Gate. `lyrfix` existiert nicht
(gemessen, `lyric --help`); die v5-Liste verlangt es für Posten 24 (gelesen).

**Warum das jetzt eine Frage ist:** zwei Vorschläge dieses Dossiers geben dem **Formatter** eine
**bedeutungsändernde** Umschreibung: META-16 B (`@Deprecated` → `@!Deprecated`) und, in der
ersten Fassung, META-21 B (Klammerform statt Blockform). Beides in einer Übergangszeit, in der
beide Schreibweisen gültig sind. Die Folge, die §5 der ersten Fassung nicht führte: **eine CI,
die noch die alte Schreibweise fährt, wird von `lyric fmt --check` rot, bevor jemand migrieren
wollte** — das Gate feuert auf eine Migration, die der Nutzer nicht angestoßen hat.

**Optionen**
- **A — der Formatter macht nur Normalform, nie Migration.** Jede Umschreibung, die die
  *Bedeutung* oder die *Gültigkeit unter einer anderen Version* ändert, gehört in `lyrfix`.
  Vorbild: `gofmt` gegen `go fix`, `rustfmt` gegen `cargo fix`, `clang-format` gegen
  `clang-tidy --fix`. **Jede Vergleichssprache der Liste trennt das.** Preis: ein zweites
  Werkzeug — das aber ohnehin gebaut wird.
- **B — der Formatter migriert, aber `--check` ignoriert Migrationsregeln.** Preis: zwei Klassen
  von Regeln in einem Werkzeug, und `lyric fmt` ohne `--check` macht mehr als `--check` prüft —
  das widerspricht „The shape is the tool's contract“ (gelesen, `lyrfmt --help`).
- **C — der Formatter migriert erst, wenn die alte Form ungültig ist** (also in 5.0). Preis:
  in der Übergangszeit hilft er nicht, und danach ist die Umschreibung keine Migration mehr,
  sondern Fehlerbehebung.
- **D — `lyrfix` ist ein Modus des Formatters** (`lyric fmt --fix-to 5.0`). Preis: ein Werkzeug,
  zwei Verträge; `--check` bleibt sauber, weil der Modus explizit ist. Gewinn: keine zweite
  Binary.

**Empfehlung: A, und D als Kompromiss, wenn keine zweite Binary gewollt ist.** Die Trennung
„Normalform ist automatisch, Migration ist ein Verb“ ist in allen Vorbildern dieselbe, und sie
ist der Grund, warum `--check` in einer CI überhaupt benutzbar ist. **Konkret heißt das: die
4.x-Warnstufen von META-16 und META-08 dürfen `lyric fmt` NICHT benutzen** — die erste Fassung
hat das bei META-16 und META-21 genau so vorgeschlagen, und es ist Rule 2 gegen den eigenen
Vorschlag: ein Werkzeug, zwei unvereinbare Verträge.

**Bricht:** nein. **4.x-Warnstufe:** keine — dies ist eine Regel *über* Warnstufen.
**Hängt ab von:** Toolchain-/Formatter-Gebiet, META-16, META-08, META-17 B (`renamed` ist die
Datenquelle für `lyrfix`).

---

### META-34 — Gibt es ein std-Vokabular für Modulidentität?

**Heute:** einen Marker, kein Vokabular. `OnModule` existiert (gelesen,
`stdlib/std/core.lyr:483`), und er hat Nutzer: `Deprecated` selbst deklariert ihn (gelesen,
`stdlib/std/core.lyr:500`), und der Guide zeigt zweimal dasselbe Beispiel eines *selbstgebauten*
`Plugin`-Attributs (gelesen, `docs/guide/14-embedding.md:393-395`, `15-attributes.md:197-199`).
Ein **std-Struct für Modulidentität** gibt es nicht (kein Treffer in `stdlib/`).

*Korrektur an der Kritik:* die Kritik behauptete, `OnModule` sei „der einzige der drei Marker ohne
einen einzigen Nutzer in der stdlib“. Das stimmt nicht — `Deprecated` deklariert ihn. Die
dahinterliegende Frage bleibt trotzdem offen und fehlte.

**Warum das eine Frage ist:** Guide 14 begründet `OnModule` damit, dass „ein Mod-Loader Name und
Version liest, bevor er etwas ausführt“ — und jeder Host erfindet dafür sein eigenes Attribut.
Erato 2 lädt Erweiterungen als Bytecode (Gedächtnis-Notiz; **behauptet**, nicht in diesem Repo
belegt), und jede Erweiterung wird ihre Identität anders schreiben als jede andere. Gleichzeitig
sagt `lyric.json` bereits `name` (gelesen, `docs/guide/16-building.md:313`) — es gibt also schon
eine Quelle, die aber nicht ins Modul wandert.

**Optionen**
- **A — `std.core` liefert `@Module { name, version, apiLevel }`.** Vorbild: Javas
  `module-info.java`, .NET `AssemblyVersionAttribute`, `package.json`. Preis: ein std-Struct,
  das der Compiler **nicht** liest (also kein META-16-Problem), aber das jeder Host als
  gemeinsame Sprache benutzen kann.
- **B — der Compiler schreibt die Zeile aus `lyric.json`**: `name` und `version` landen
  automatisch als Modulzeile. Vorbild: .NET (MSBuild schreibt die Assembly-Attribute).
  Preis: eine Zeile, die im Quelltext nicht steht — ein Host sieht sie, der Leser der Datei nicht.
  Und `lyric.json` kennt heute keine `version` (gelesen: nur `name`, `sourceRoot`, `nativeRoots`,
  `dependencies`), das müsste dazu.
- **C — A plus B**: das std-Struct existiert, und der Compiler füllt es aus `lyric.json`, wenn
  der Quelltext es nicht selbst schreibt. Preis: zwei Wege zu einer Zeile — Rule 2 fragt.
- **D — nichts; jeder Host definiert seins.** Preis: der Status quo, und jede Erweiterung
  schreibt ihre Identität anders.

**Empfehlung: A, ohne B.** Ein std-Vokabular kostet drei Felder und gibt jedem Host dieselbe
Frage-Antwort-Form; der Compiler bleibt heraus, die Zeile bleibt im Quelltext sichtbar, und die
Beziehung zu `lyric.json` ist bewusst lose: **`lyric.json` beschreibt den Build, `@Module`
beschreibt das Artefakt**, und ein Paket, das aus mehreren Modulen besteht, hat eine
`lyric.json` und mehrere `@Module`-Zeilen. B würde diese Unterscheidung einebnen und eine Zeile
erzeugen, die niemand geschrieben hat.

**Bricht:** nein (ein neues `pub struct` in `std.core`). **4.x-Warnstufe:** keine.
**Hängt ab von:** stdlib-Gebiet, Embedding-Gebiet, Build-/Projekt-Gebiet.

---

### META-35 — Was soll `LYR-PAR0042` eigentlich versprechen?

**Heute:** einen Satz, den die nächste Diagnose widerlegt. Gemessen (`meta-rev/m2`–`m5`, `m14`):

```
error[LYR-PAR0042]: an attribute cannot sit on an interface — a function, a struct, a class,
                    an enum, A MEMBER OF ONE, or the module header carries one
error[LYR-SEM0065]: '@Other' cannot sit on a member — only '@Deprecated' may:
                    the module format has no member rows for anything else
```

Die `PAR0042`-Familie verspricht Member-Attribute, `SEM0065` verweigert sie. Dazu fällt die
Enum-Variante ganz aus der Familie (`LYR-PAR0026: expected enum variant name, got AtIdentifier`,
gemessen). L8 der ersten Fassung listete drei Diagnose-Nickligkeiten und diese nicht, obwohl es
dieselbe Sorte ist.

**Warum das jetzt eine Frage ist:** META-02 B und META-24 A verschieben genau diese Grenze — der
Satz muss ohnehin neu geschrieben werden, und dann sollte klar sein, **was er beschreibt**: die
Menge der Stellen, an denen ein `@` syntaktisch stehen darf, oder die Menge der Ziele, die ein
Attribut annehmen kann.

**Optionen**
- **A — `PAR0042` beschreibt die Syntax, `SEM0065` die Semantik**, und der `PAR0042`-Satz zählt
  nur auf, wo ein `@` *stehen* darf; welches Attribut dort *zulässig* ist, sagt die Sema. Dann
  muss „a member of one“ raus, solange `SEM0065` gilt — oder es bleibt drin, wenn META-02 B die
  Member öffnet. Preis: null, reine Textarbeit, aber sie muss mit META-02 synchron bleiben.
- **B — eine Diagnose statt zwei**: `PAR0042` verschwindet, und jede Zielverletzung ist
  `SEM0065` mit dem jeweiligen Ziel im Text. Vorbild: Rust meldet Attributfehler durchgängig aus
  einer Phase. Preis: der Parser muss die Zeile dann anders aufsammeln (er bricht heute früh ab),
  und Parserfehler haben bessere Recovery.
- **C — der Satz nennt die Quelle**: „…which markers this attribute declares decides where it may
  sit — `@Other` declares `OnMethod`, and members carry no rows yet“. Preis: die Meldung wird
  länger; dafür erklärt sie den Mechanismus statt eine Liste aufzuzählen, und genau der
  Mechanismus ist das, was ein Neuling hier nicht kennt.
- **D — nichts ändern.** Preis: zwei widersprüchliche Sätze bleiben stehen.

**Empfehlung: A + C, und die Enum-Variante in die Familie holen.** Der Satz soll sagen, wo ein
`@` stehen darf (A), und die Meldung soll auf den Marker zeigen, der es entscheidet (C) — das ist
die Regel der Sprache („placement is decided by conformance, not by the name“, gelesen
`stdlib/std/core.lyr:478-479`) und sie steht heute in keiner Fehlermeldung. B nein: die Recovery
des Parsers ist ein echtes Argument, und `@Retry(3, 4)` zeigt, dass sie schon einmal
nachgebessert wurde (gelesen, `CHANGELOG.md:946-948`).

**Bricht:** nein (Diagnosetexte). **4.x-Warnstufe:** keine; sofort machbar, mit einer
Synchronisationspflicht gegenüber META-02.
**Hängt ab von:** META-02, META-24, Diagnostik-Gebiet.

---

### META-36 — Wie erreicht die META-01-Migration einen Host, der nicht neu übersetzt wird?

**Heute:** die Frage stellt sich noch nicht, weil Namen unqualifiziert sind. Gelesen, wie ein
Host heute fragt: `module.Attributes.OnFunctions("Test")`, wörtlicher Namensvergleich
(`src/Lyric.Core/Bytecode/ModuleAttributes.cs:57-60`, `Ordinal`).

**Warum das eine Frage ist:** META-01 A sagt, jeder Aufruf `OnFunctions("Test")` werde zu
`OnFunctions("std.test.Test")`. Was in der ersten Fassung fehlt, ist die Übergangszeit. **Ein
Erato-Plugin, das als 4.x-Bytecode auf der Platte liegt, trägt den unqualifizierten Namen für
immer** — es wird nie neu übersetzt, weil niemand mehr die Quellen hat oder weil der Autor weg
ist. Der Host muss also dauerhaft beide Formen bedienen, und das gehört in die Empfehlung.

**Optionen**
- **A — 5.0 matcht auf den qualifizierten Namen, Punkt.** Alte Module fallen aus. Preis: jedes
  4.x-Artefakt im Feld ist tot, sobald der Host auf 5.0 geht.
- **B — der Host matcht auf beides**: `OnFunctions(name)` vergleicht den vollen Namen **und** das
  letzte Segment. Vorbild: keines sauberes; am nächsten .NETs Typname-Auflösung mit und ohne
  Namespace. Preis: die Kollision aus L1 kommt durch die Hintertür zurück — zwei SDKs mit `Tag`
  matchen beide auf `"Tag"`.
- **C — zwei Methoden**: `OnFunctions(qualifiedName)` ist die neue, strenge Form;
  `OnFunctionsByShortName(name)` ist die alte, ausdrücklich als unsicher dokumentiert und für
  Altbestand gedacht. Vorbild: .NETs `Type.GetType(name)` gegen `AssemblyQualifiedName`. Preis:
  zwei Methoden in der Host-API — aber die Unsicherheit steht im Namen, und ein Host entscheidet
  bewusst.
- **D — die Minor-Version des Moduls entscheidet**: liest der Host ein 4.x-Modul, matcht er
  kurz; liest er ein 5.x-Modul, matcht er qualifiziert. Vorbild: keines. Preis: das Verhalten
  einer API hängt an einem Header-Feld — für den Host-Autor unsichtbar und schwer zu testen.
- **E — `lyrfix`-artiges Nachschreiben der Bytes**: ein Werkzeug qualifiziert die Namen in einem
  bestehenden `.lyrbc`. Preis: setzt voraus, dass die Herkunft im Modul steht — tut sie nicht
  (das ist ja das Problem), also nur mit einer Zuordnungstabelle von Hand.

**Empfehlung: C, und sie gehört in die META-01-Empfehlung, nicht in eine Fußnote.** Sie macht die
Übergangsentscheidung zu einer, die der **Host-Autor** trifft und sieht, statt sie in der
Bibliothek zu verstecken. **B ausdrücklich nicht**: eine stille Kurzform-Übereinstimmung stellt
genau den Zustand wieder her, den META-01 beseitigen will, und dann ist der ganze Formatbruch
umsonst. D nein: unsichtbares Verhalten.

**Zusatz, der mitentschieden werden muss:** `lyrtest` ist selbst so ein Host. Er sollte in 5.0
auf `"std.test.Test"` strikt matchen — und damit ist L2 geschlossen, was der eigentliche Zweck
der Übung war.

**Bricht:** major (Host-API), wie META-01. **4.x-Warnstufe:** die 4.7-Warnung aus META-01
(`lyrtest` warnt bei fremdem `Test`) ist zugleich die Ankündigung dieser Umstellung.
**Hängt ab von:** META-01, Embedding-Gebiet (Erato).

---

### META-37 — Gilt die Determinismus-Zusage für Gleitkomma-Intrinsics?

**Heute:** die Zusage steht im Guide, die Absicherung nicht im Code. Gemessen
(`meta-rev/m1_ctfloat.lyr`, Disassembly): `comptime sin(1.0)` backt `const f64
0.8414709848078965` ins Modul; Kontrolllauf zur Laufzeit (`m1b_rtfloat.lyr`) liefert denselben
Wert, also ist es derselbe Host-Aufruf. Gelesen: `sin`/`pow`/`log` sind rumpflose Deklarationen
(`stdlib/std/math.lyr:20,39,42,50`), gebunden an `Math.Sin`/`Math.Pow`/`Math.Log`
(`src/Lyric.Vm/NativeRegistry.cs:626,629,658`); die Sperrliste des Compile-Zeit-Runners hat einen
Eintrag, `std.random.secureRandom` (`src/Lyric.Vm/VmComptimeRunner.cs:35`); die Spec sagt über
Floats nur das Speicherformat (`docs/Bytecode.md:80,441,665-666`). Die Zusage selbst:
„the same source yields the same literal on every machine“ (`docs/guide/16-building.md:294-296`).

**Nicht gemessen und deshalb nicht behauptet:** dass zwei Maschinen verschiedene Bits liefern.
Gemessen ist nur, dass die Bits aus der Host-Mathematik kommen und dass nichts sie festnagelt.
Die Zusage ist **unbelegt**, nicht widerlegt — und eine unbelegte Zusage in der Nutzer-Doku ist
für sich schon der Befund.

**Warum das eine Designfrage ist und keine Fußnote:** §2.3 dieses Dossiers macht die
Determinismus-Eigenschaft zum Hauptargument gegen Nim und zur Nebenbedingung für `embed`
(META-10). META-30 will darauf ein Caching bauen. Wenn die Eigenschaft für einen Teil der
Standardbibliothek nicht gilt, müssen alle drei es wissen.

**Optionen**
- **A — Zusage einschränken.** Guide 16 sagt: „bitgenau für die Arithmetik der Sprache; die
  Ergebnisse von `std.math`-Transzendenten folgen der Host-Mathematik“. Preis: die Eigenschaft
  verliert ihre Schlagkraft — aber sie wird wahr.
- **B — Transzendente zur Compile-Zeit verbieten**, wie `secureRandom`: der Runner lehnt
  `std.math.sin` & Co. ab, `sqrt`/`floor`/`ceil`/`round`/`abs`/`min`/`max` bleiben (die sind in
  IEEE-754 **exakt spezifiziert**). Vorbild: keines direkt; am nächsten Rusts Zurückhaltung, `f64`
  -Transzendente in `const fn` zu stabilisieren — genau aus diesem Grund. Preis: `comptime`
  verliert einen plausiblen Anwendungsfall (Tabellen trigonometrischer Werte), und die Liste der
  gesperrten Namen wächst von 1 auf ~12.
- **C — eine eigene, spezifizierte Implementierung** für die Compile-Zeit (korrekt gerundete
  Transzendente in Lyric oder als eingebauter Algorithmus). Vorbild: Javas `StrictMath`, das
  genau dieses Problem löst — `Math` darf schnell sein, `StrictMath` muss reproduzierbar sein.
  Preis: teuer, und die Compile-Zeit läge dann neben der Laufzeit (`comptime sin(x)` ≠ `sin(x)`,
  was die Grundzusage „`comptime e` hat denselben Wert wie `e`“ bricht, gelesen
  `design/macros.md:69`).
- **D — die Spec schreibt korrekte Rundung vor**, für Compile-Zeit **und** Laufzeit. Vorbild:
  Javas `StrictMath` als Pflicht statt als Alternative. Preis: der Host muss liefern; .NET tut es
  für Transzendente **nicht** zu, also hieße das eine eigene Implementierung in der VM.
- **E — nichts ändern, Zusage stehen lassen.** Preis: eine Zusage in der Nutzer-Doku, für die
  niemand einstehen kann.

**Empfehlung: A sofort (4.7, reine Textarbeit), dann B für 5.0 prüfen.** A ist die ehrliche
Antwort und kostet nichts. B ist die Antwort, die die Eigenschaft *rettet* statt sie zu
relativieren, und der Preis ist kleiner als er klingt: wer eine Sinustabelle zur Compile-Zeit
will, kann sie mit `embed` (META-10) aus einer erzeugten Datei holen, und genau dafür ist
`build.lyr` da. **C und D nicht**: eine zweite Mathematik bricht die Grundzusage von `comptime`
(C) oder verlangt eine eigene libm (D).

**Zusammenhang mit META-30:** wenn ein Site-Cache über Compilerläufe hinweg existiert, muss sein
Schlüssel die Toolchain- **und** die Host-Laufzeitversion enthalten — sonst überlebt ein alter
`sin`-Wert ein .NET-Update. Bei B entfällt das Problem.

**Bricht:** A: nein (Doku). B: minor — ein Programm, das heute `comptime sin(…)` schreibt, baut
nicht mehr. **4.x-Warnstufe:** bei B ab 4.7 eine Warnung an jeder `comptime`-Site, die eine
Transzendente erreicht.
**Hängt ab von:** Spec-Gebiet, Skalare-Gebiet (IEEE-754-Zusagen), stdlib-Gebiet (`std.math`),
META-30, META-10.

---

## 4. Was wir übernehmen sollten

| Von | Was | Warum es zu Lyric passt |
|---|---|---|
| **Swift** | `@available(… renamed:)` mit anwendbarem Fix-it | Lyrics `until` ist auf der Erzwingungsachse schon voraus; `renamed` ist der eine fehlende Schritt, und `lyrfix` braucht die Datenquelle (META-17) |
| **Swift** | Konformanz ohne Body → Synthese, geschriebene Methode gewinnt | kein neues Wort, kein codeerzeugendes Attribut (META-13). **Aber nicht als Präzedenzfall für `Ordered` auf Structs** — den synthetisiert Swift nie (META-14) |
| **Swift** | `CaseIterable` | Enum-Reflexion als Synthesezeile statt als Laufzeitfeature (META-18) |
| **C#** | `#pragma warning disable` / `<NoWarn>` / `DiagnosticId` | die Spalte, die Lyrics Deprecation-Tabelle fehlt: ein Ventil für den Aufrufer (META-22, META-17 D) |
| **C#** | `AllowMultiple` als Opt-in | über ein Markerinterface, genau wie `WithArg` (META-04) |
| **C#/Java** | `Inherited` / `@Inherited` | die Frage, die Lyric nie gestellt hat — und Lyrics Antwort darauf ist besser als beider (META-25 C: der Compiler materialisiert die Zeile) |
| **Java** | qualifizierter Annotationsname im Classfile | schließt L1/L2 (META-01) |
| **Java** | `StrictMath` neben `Math` als Modell: „schnell“ und „reproduzierbar“ sind zwei Zusagen | benennt genau die Lücke, die L14 gemessen hat (META-37) |
| **Scala 3** | `derives` + `Mirror`: Ableitung als **Bibliothekscode** über eine Compiler-gelieferte Feldbeschreibung | der einzige Weg zu `ToJson`/`Encoder`/`Decoder` ohne wachsende Compiler-Tabelle — **als Paket zu entscheiden, nicht als Einzelfrage** (META-13) |
| **Zig** | `@embedFile` als eingebaute Form mit Build-Graph-Abhängigkeit | löst META-10 ohne ein Capability-Bit zu verbiegen |
| **Zig** | die Quota-Meldung nennt die Quota | ohne Zahl ist eine Site nicht optimierbar (META-12 E) |
| **D** | Einbettungswurzeln müssen erklärt werden (`-J`) | macht die Sandbox-Ausnahme zu einer Projektaussage statt zu einer Compiler-Ausnahme (META-10 C) |
| **D / Nim** | documented unittests / `runnableExamples`: ein Testblock, der auch Beispiel ist | Doc-Tests ohne zweiten Ausführungspfad (META-20) — **aber mit Lyrics Sichtbarkeitsregel, nicht mit D's** (META-28) |
| **Rust** | `include_str!`-Semantik: Pfad relativ zur Quelldatei, Datei in der Abhängigkeitsliste | Inkrementalität ist die eigentliche Frage bei `embed` |
| **Rust** | const-eval druckt einen Auswertungs-Stack | `LYR-CT0002` zeigt heute nur die Site (META-29) |
| **Rust** | Zurückhaltung bei `f64`-Transzendenten in `const fn` | dieselbe Vorsicht, aus demselben Grund (META-37 B) |
| **Rust/Go** | `cargo fix` neben `rustfmt`, `go fix` neben `gofmt` | Normalform und Migration sind zwei Verträge (META-33) |
| **C#** | `record`-`ToString`-Format `P { X = 1 }` | round-trip-fähig; `conformance-synthesis.md:133` hat es schon gewählt |

**Was wir NICHT übernehmen:**
- **Zig `comptime`-Typen als Werte** — das Makrosystem durch die Hintertür, und es verlegt
  Fehlermeldungen ins Innere der Bibliothek (gelesen, `PLAN.md:375-377`).
- **Nim `staticExec`/`staticRead`** — vernichtet die Sandbox, die Lyrics Compile-Zeit auszeichnet.
  (Was sie **nicht** auszeichnet, ist Bitgleichheit transzendenter Funktionen — L14.)
- **Rust `#[derive]` als Attribut** — ein Attribut, das Code erzeugt, ist genau das, was die
  Attributregel verbietet. *Präzisierung:* Rusts **std**-Derives sind Compiler-Builtins, keine
  proc-macros; der Preis „ungesandboxtes Makro“ gilt nur für die Erweiterbarkeit.
- **C#-Laufzeitreflexion** — setzt Typ-Tags auf Werten voraus; die AOT-/Trimming-Steuer ist der
  Beleg, dass die Rechnung später kommt.
- **D-UDAs mit beliebigen Typen** — ohne Zielprüfung und ohne Vokabular.
- **Swifts Plattform-Versionen in `@available`** — Lyric hat eine Toolchain-Version und keine
  Plattformmatrix; was es stattdessen braucht, ist eine Antwort für **Abhängigkeiten** (META-23).

---

## 5. Konflikte

### Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept)

| Frage | Konflikt | Meine Lesart |
|---|---|---|
| META-10 `embed()` | zweite Compile-Zeit-Form neben `comptime` | **ADR-pflichtig.** `comptime` bekommt bewusst keine Capability; eine Lese-Ausnahme *in* `comptime` wäre die Aufweichung, `embed` als eigene Form hält die Grenze sichtbar. |
| META-09 `comptime fn` | zweite Funktionsart | **Empfehlung nein**, genau aus Rule 2. |
| META-15 / META-16 compilergelesene Attribute | „ein Attribut beschreibt und tut nichts“ (Guide 15:20-22) gegen eine wachsende Ausnahmeliste | **Korrigiert:** die Menge wächst auf **2**, nicht auf 5+ — `@Test`, `@Bench`, `@Command`/`@Flag` liegen bei Werkzeugen bzw. Bibliotheken (gelesen, `src/Lyrtest/Program.cs:168`). Nur META-15 B ließe sie auf 4+ wachsen. **Empfehlung: das Swift-Paket, damit sie bei 2 bleibt, und Guide 15:20-22 präzisieren.** |
| META-13/14/15 als Paket | die erste Fassung empfahl A + A + B, was sich gegenseitig aufhebt | **Entweder Swift-Paket (13 A, 14 A, 15 D, 16 A) oder Scala-Paket (13 B, 14 C, 15 C, 16 A).** Die Mischung zahlt beide Preise: Formatarbeit *und* wachsende Compiler-Sonderfälle. |
| META-13 B (`derives`) | `PLAN.md:377` listet `derive` unter „bewusst nicht“ | Die Ablehnung galt dem *Rust*-`derive` (Attribut erzeugt Code). Scalas `derives` ist eine Konformanzklausel, deren Regel Bibliothekscode ist. **Die alte Begründung deckt den neuen Vorschlag nicht** — die Frage ist offen, nicht entschieden. |
| META-21 B (Attributargumente = benannte Argumente) | — | **Umgedreht gegenüber der ersten Fassung.** `AttrArg = IDENTIFIER '=' Expr` und `StructInitField = IDENTIFIER '=' Expr` sind dieselbe Produktion (gelesen, `docs/Grammar.md:160,494`), und der Guide sagt es (15:47-48). Rule 2 zeigt jetzt **gegen** B: heute ist die Attributklammer die Struct-Init-Schreibweise, B würde zwei Schreibweisen für denselben Struct nebeneinanderstellen (`:` und `=`). |
| META-24 B (`deprecated` als Modifier) | zweiter Mechanismus für „das geht weg“ | **Empfehlung nein**, genau aus Rule 2 — auch wenn er die Interface-Lücke am billigsten schlösse. |
| META-33 (`fmt` migriert) | ein Werkzeug, zwei Verträge: Normalform (automatisch) und Migration (bedeutungsändernd) | **Rule 2 gegen den eigenen Vorschlag.** Die erste Fassung gab `lyric fmt` bei META-16 und META-21 eine bedeutungsändernde Umschreibung — und `lyric fmt --check` ist ein CI-Gate, das dann rot wird, bevor jemand migrieren wollte (gemessen, L13). Migration gehört in `lyrfix`. |
| META-25 C (`Inherited`-Marker) | ein vierter Marker neben `OnModule`/`OnType`/`OnFunction`/`WithArg`/`Repeatable` | Kein Konflikt: alle nutzen **denselben** Mechanismus (Konformanz entscheidet), und der reist die Elternkette (gemessen, `m10_markerchain.lyr`). Das ist Rule-2-konform — eine Liste von Markern ist ein Mechanismus, keine fünf. |

### Mit anderen Gebieten

- **Bytecode/Format — der Kern des Gebiets, und die erste Fassung hat ihn unterschätzt.** Drei
  Vorschläge verschieben, was ein 4.x-Leser akzeptiert, und sie sind **nicht gleich teuer**:

  | Vorschlag | Was der 4.x-Leser tut | Einstufung |
  |---|---|---|
  | META-05 B (Array-`ConstValue`) | lehnt **nur Module ab, die es benutzen** | der 3.4-Fall, Wort für Wort (gelesen, `docs/Bytecode.md:47-52`) — **minor** |
  | META-02 B / META-24 A (neue `targetKind`) | lehnt jedes Modul ab, das ein Feld-/Varianten-/Interface-Attribut trägt (gelesen, `docs/Bytecode.md:471`) | Einstufung beim Bytecode-Gebiet; **der Preis fällt in jedem Fall an** |
  | META-04 B (Repeatable) | dito — die Regel „the same (targetKind, target, type) triple twice“ muss **gestrichen** werden (gelesen, `docs/Bytecode.md:472-473`) | dito |
  | META-31 C (Blob-Abschnitt) | überspringt einen unbekannten Abschnitt | **minor**, der billigste Zug (gelesen, `docs/Bytecode.md:110-118`) |

  **META-02, META-04 und META-24 gehören in eine Runde und brauchen denselben Embedding-Abgleich
  wie META-01** — die erste Fassung sah ihn nur für META-01 vor.

- **Embedding/Host-API:** META-01 bricht `OnFunctions(name)` für jeden Host, Erato eingeschlossen
  (gelesen, `ModuleAttributes.cs:57-60`), und META-36 sagt, wie die Übergangszeit aussieht.
  META-02/04/24 machen Module für bestehende Hosts **unladbar**. META-25 und META-27 ändern, was
  ein Host in Abschnitt 11 findet. **Das Embedding-Gebiet ist an fünf Fragen Mitentscheider, nicht
  an einer.**
- **Interfaces/Generics:** META-18 und META-14 (`Default`) sind ohne **statische Interface-Member
  und `Self`** nicht baubar (gelesen, `lyric-v5-features.md:4`). Wenn das Interface-Gebiet dagegen
  entscheidet, fallen hier zwei Fragen weg.
- **Optionals:** META-13/14 hängen an `?T == ?T` (heute `LYR-SEM0059`).
  `conformance-synthesis.md:76-78` sagt, beide müssen **zusammen** ausgeliefert werden.
- **Build/Projekt:** META-10 C (Einbettungswurzeln), META-22 C (`allow`-Liste), META-23 B
  (Pin je Abhängigkeit), META-27 C (was ein Host liest), META-34 (`@Module` gegen `lyric.json`)
  — **fünf Vorschläge wollen etwas in `lyric.json`.** Das Build-Gebiet sollte sie als ein Paket
  sehen, damit die Datei nicht zur Halde wird.
- **Diagnostik:** META-22 (Unterdrückung), META-29 (Backtrace), META-12 E (Zahl in der Meldung),
  META-35 (`PAR0042`-Text), L5/L7/L8. Das ist das Gebiet mit den meisten „sofort machbar“-Posten.
- **Spec:** META-12 (Budget), META-37 (Determinismus-Zusage) — beide ändern, was die
  Spezifikation **verspricht**, nicht was sie beschreibt. Beide sind Spec-first-Arbeit.
- **Editor/LSP:** META-32 (Attribut-Vervollständigung) ist die billigste hochwirksame Arbeit des
  Gebiets und hängt an **keiner** Sprachentscheidung.
- **Toolchain:** META-17 B (`renamed`) ist die Datenquelle für `lyrfix`; META-19 (`lyric expand`)
  und META-20 (Doc-Tests) sind CLI-Verben; META-33 sagt, welches Werkzeug was darf.
- **Pattern/Exhaustiveness:** META-03 (`@NonExhaustive` einlösen) ist deren Arbeit.
- **Test/Toolchain:** META-20 und META-28 stoßen auf die offene `lyrtest`-Isolationsfrage
  (`STATUS.md`).

### Interne Reihenfolge — geändert

Die erste Fassung setzte META-16 (Namensklasse `@!`) auf Platz 1. Das war der teuerste Vorschlag
des Dossiers — ein Major-Bruch an `@Deprecated`, der einzigen Zeile, die jede Codebasis trägt —
mit der dünnsten Begründung: die Menge compilergelesener Attribute wächst ohne META-15 B von 1 auf
2, nicht auf 5+ (nachgezählt in META-16). **Neue Reihenfolge:**

1. **Kostenlos und ohne Entscheidung** — reine Fehlerbehebung und Werkzeug (unten aufgezählt),
   plus **META-32** (Attribut-Vervollständigung) und **META-29** (Backtrace).
2. **META-13/14/15/16 als ein Paket entscheiden** (Swift oder Scala). Alles Weitere in der
   Synthese hängt daran, und META-16 wird dadurch beantwortet statt vorweggenommen.
3. **Eine Formatrunde**: META-05 B (billig), META-02 B, META-04 B, META-24 A — mit dem
   Embedding-Gebiet, in einem Beschluss, weil alle drei letzteren denselben Preis tragen.
4. **Die Uhr lebbar machen**: META-22 (Unterdrückung), META-23 (Abhängigkeiten), META-17 B
   (`renamed`), META-33 (fmt gegen lyrfix). Ohne diese vier produziert die 5.0-Migration
   Warnungen, die niemand abstellen kann — und der Branch heißt bereits
   `feat/v5-warning-clocks`.
5. **META-01 + META-36** (qualifizierte Namen, Übergang) — eigene Formatrunde, mit dem
   Embedding-Gebiet.
6. **`comptime` ausbauen**: META-07 B+D, dann META-10/META-31, META-12 E, META-30.
7. Der Rest.

**Sofort machbar, ohne jede Entscheidung** (reine Fehlerbehebung, alles in 1.4 belegt):
`SEM0066` soll `comptime` erwähnen (L5); `lyrc check` soll die nicht ausgewerteten Sites melden —
der Code `LYR-CT0001` existiert bereits (L6); die `comptime`-Beschattung soll sich nennen (L7);
`@Tag(5) { … }` soll den Satz aus Guide 15:159 sagen und die Enum-Variante in die
`PAR0042`-Familie (L8); der `PAR0042`-Satz und `SEM0065` sollen einander nicht widersprechen
(L12, META-35); `LYR-CT0002` soll Budget und Verbrauch nennen (META-12 E) und einen Backtrace
tragen (L11, META-29); `guide/16-building.md:294-296` soll die Determinismus-Zusage einschränken
(L14, META-37 A); `OnMethod` soll entweder wirken oder verschwinden (L3) — per CHANGELOG, nicht
per `@Deprecated`, weil das nicht geht (L3a); `STATUS.md:2411-2416` ist erledigt und gehört
gestrichen.

---

## 6. Nach der Kritik geändert

**Falsche Aussagen über Lyric 4 korrigiert (alle nachgemessen oder nachgelesen):**

- **META-04 B**: „das Format kann es schon … Bricht: nein (additiv)“ war falsch. Das zitierte
  `docs/Bytecode.md:460` ordnet nur; `:472-473` verbietet „the same (targetKind, target, type)
  triple twice“. Wiederholbare Attribute streichen eine reader-must-reject-Regel. Preis neu
  ausgewiesen, mit beiden Lesarten (3.4-Präzedenz `:47-52` gegen den Wortlaut `:117`) und der
  Zuständigkeit beim Bytecode-Gebiet. Der Kritiker sagt „Format-Major“ — das ist **eine** von zwei
  belegbaren Lesarten, und ich schreibe beide hin statt eine zu behaupten.
- **META-02**: „additiv, keine Warnstufe nötig“ war irreführend. `docs/Bytecode.md:471` verlangt,
  einen unbekannten `targetKind` abzulehnen; jedes Modul mit Feldattributen ist für jeden
  attributlesenden 4.x-Host unladbar. Kostenzeile eingefügt, Embedding-Gebiet als Mitentscheider
  ergänzt.
- **META-03**: `@Deprecated` auf `pub interface OnMethod` ist **unmöglich** — gemessen,
  `LYR-PAR0042` (`meta-rev/m2_dep_interface.lyr`). Der Vorschlag „die Sprache kann ihre eigene Uhr
  benutzen“ ist gestrichen und durch CHANGELOG + Major-Entfernung ersetzt; der Befund steht jetzt
  als L3a und als eigene Frage (META-24).
- **META-03/L4**: `@NonExhaustive` trägt **niemand**, auch in std nicht (gemessen, ein Treffer:
  die Deklaration). Die vorgeschlagene 4.7-Warnung hätte über nichts gewarnt. Die Uhr ist jetzt an
  das **Setzen** des Attributs gekoppelt.
- **§2.3/§4/L14**: die Determinismus-Zusage ist für Gleitkomma-Intrinsics **unbelegt** — gemessen
  (`comptime sin(1.0)` → `const f64 0.8414709848078965`), gelesen (`NativeRegistry.cs:629`
  bindet `Math.Sin`; die Sperrliste `VmComptimeRunner.cs:35` hat einen Eintrag; die Spec regelt
  nur das Speicherformat). **Schärfer als die Kritik:** die Zusage steht nicht nur in
  `design/macros.md:74`, sondern in der **Nutzer-Doku**, `docs/guide/16-building.md:294-296`.
  Neue Frage META-37. Was ich **nicht** behaupte: dass zwei Maschinen abweichen — ich habe eine.
- **META-21/§5**: „`AttrArgs` ist ein dritter Argumentmechanismus“ war falsch.
  `docs/Grammar.md:160` und `:494` sind dieselbe Produktion, und
  `docs/guide/15-attributes.md:47-48` sagt es ausdrücklich. Damit fällt das Rule-2-Argument, und
  die **Empfehlung ist von B auf A gedreht**.
- **META-01**: die Behauptung, Guide 15:246 verspreche Eindeutigkeit, war eine Fehllektüre — der
  Guide schreibt die Einschränkung offen hin (15:245-247), `ModuleAttributes.cs:11-13` ebenso.
  Befund L1 bleibt, Begründung als prospektiv gekennzeichnet.
- **L6**: `LYR-CT0001` existiert bereits für „kein Runner“ (`design/macros.md:70,77`). Die
  META-11-A-Note ist damit billiger als veranschlagt. Zusätzlich gemessen und ergänzt: die Grenze
  liegt bei `--emit`, nicht bei `check` gegen `build`.

**Vergleichssprachen korrigiert:**

- **Swift `@available`**: mit dem Wildcard `*` nur die versionslose Kurzform; versionierte
  `deprecated:`/`obsoleted:` verlangen eine **benannte Plattform**. Der Punkt ist nicht
  kosmetisch — Swifts Vergleich ist **pro Plattform**, Lyrics `until` vergleicht unbedingt gegen
  `ToolchainVersion.Value` (gelesen, `DeprecationPromise.cs:49`).
- **Swift-Attribute**: benutzerdefinierbare Attribut-**Schreibweisen** gibt es seit 5.1
  (`@propertyWrapper`), 5.4 (`@resultBuilder`), plus `@dynamicMemberLookup`/`@dynamicCallable` —
  nicht erst mit Makros (5.9). Was Makros brachten, war freie Attribut-**Semantik**.
- **Rust-Attributziele**: Attribute auf beliebigen **Ausdrücken** sind instabil
  (`stmt_expr_attributes`); stabil sind Items, Statements, `match`-Arme, Struct-Felder,
  Funktionsparameter. „fast alles, inkl. Ausdrücke und Statements“ war überzogen — ausgerechnet in
  der Zelle, gegen die Lyrics Zielmenge verglichen wird.
- **Swift `Comparable`**: nur für **Enums** (SE-0266, 5.3), nie für Structs. META-14 kann
  `Ordered` für Structs daher **nicht** mit Swift belegen; die Zeile ist jetzt als eigene
  Begründungspflicht markiert.
- **Rust-Derives**: die std-Derives sind **Compiler-Builtins** (`rustc_builtin_macros`), nur
  benutzerdefinierte sind proc-macros. Die Preisspalte „ungesandboxtes Makro“ gilt für die
  Erweiterbarkeit, nicht für das, was Lyric übernähme.
- **C# `record`**: `Deconstruct` entsteht nur für **Positionsrecords**. Das Alles-oder-nichts ist
  schmäler als behauptet.
- **Zig `@typeInfo`**: seit 0.14 `@typeInfo(T).@"struct".fields` / `.@"enum".fields`.
- **§2.1 „Lyrics Position“**: die Alleinstellungsbehauptung war überzogen — Javas
  `@Retention(RUNTIME)` und C#-Metadaten sind ebenfalls extern lesbares Datum. Ersetzt durch die
  tragfähige Formulierung: der Leser braucht **weder Reflexion noch ein geladenes Typsystem**,
  weil die Zeilen in einem selbstbeschreibenden, überspringbaren Abschnitt stehen
  (`docs/Bytecode.md:425-477`).
- **§2.2 Fazit**: „besser als … C#“ ist auf der Erzwingungsachse richtig, auf der
  **Unterdrückungsachse** falsch. Die Tabelle hat jetzt eine sechste Spalte, und C# gewinnt sie.

**Schwache Empfehlungen ersetzt:**

- **META-16** von „B, Platz 1 der Reihenfolge“ auf **A, und B nur falls META-15 B** — mit einer
  Nachzählung, welche Attribute der **Compiler** liest (2, nicht 5+; `@Test` liest der Runner,
  gelesen `src/Lyrtest/Program.cs:168`).
- **META-13/14/15** von drei einzeln plausiblen, zusammen widersprüchlichen Empfehlungen auf
  **zwei kohärente Pakete** (Swift / Scala) mit einer klaren Empfehlung (Swift für 5.0) und der
  ausdrücklichen Feststellung, dass die Mischung beide Preise zahlt.
- **META-07/META-10** in Reihenfolge korrigiert: `embedBytes` ist ohne Array-Werte unbaubar
  (gemessen, `LYR-SEM0100`). Entweder META-07 B rückt vor, oder Stufe 1 liefert nur `embed` als
  `string` — das steht jetzt in beiden Fragen.
- **META-21** von B auf **A** gedreht (Prämisse widerlegt).
- **META-12** von „B“ auf **„E immer, dann D oder B“**: eine Untergrenze macht nur das *Gelingen*
  portabel; ohne eine Zahl in der Meldung ist eine Site gar nicht erst optimierbar.
- **META-03** 4.x-Stufe komplett ersetzt (beide Hälften waren unbrauchbar).
- **META-02/04** aus dem Kostentopf von META-05 herausgenommen.

**Sechzehn fehlende Fragen eingearbeitet** (META-22 bis META-37): Unterdrückung einer Diagnose ·
Deprecation in Quell-Abhängigkeiten · Uhr auf Interface/Alias/`let`/`extend` · Attributvererbung
über Konformanz · Feldreihenfolge als Vertrag · `--release` und Attribut-DCE-Wurzeln ·
Doc-Test-Sichtbarkeit, Capabilities, `comptime` · Backtrace in `LYR-CT0002` · Budget-Granularität
und Doppelauswertung · Typ von `embed`/`embedBytes` · Attribute im Werkzeug (LSP) · `fmt` gegen
`lyrfix` · std-Vokabular für Modulidentität · was `LYR-PAR0042` verspricht · META-01-Migration für
alte Hosts · Determinismus für Gleitkomma.

**Wo die Kritik selbst irrte (Belegzitat korrigiert, Aussage bleibt):**

- **`OnModule` habe „keinen einzigen Nutzer in der stdlib“** — falsch: `Deprecated` deklariert ihn
  (gelesen, `stdlib/std/core.lyr:500`), und der Guide zeigt ihn zweimal
  (`14-embedding.md:393-395`, `15-attributes.md:197-199`). Die dahinterliegende Frage (fehlt ein
  **std-Vokabular** für Modulidentität?) war trotzdem offen und ist jetzt META-34.
- **META-04 B sei „ein Format-Major“** — das ist eine von zwei belegbaren Lesarten. Die andere
  steht in derselben Datei: der 3.4-Zug hatte exakt dieselbe Wirkung und kam als **Minor**
  (`docs/Bytecode.md:44-52`). Ich schreibe beide hin und weise die Einstufung dem Bytecode-Gebiet
  zu, statt eine Zahl über ein nicht durchverfolgtes System zu behaupten.

**Neue eigene Messungen dieser Runde** (`probes/meta-rev/`): `m1_ctfloat` + Kontrolle
`m1b_rtfloat` (Gleitkomma-Determinismus) · `m2`–`m5` (Uhr auf Interface/Alias/`let`/`extend`) ·
`m6_fmt` (Formatter schreibt Attributzeilen um) · `m7_backtrace` (kein Frame in `CT0002`) ·
`m8_budget` (keine Zahl) · `m9_ctarray` (`embedBytes`-Sperre) · `m10_markerchain` (Marker erben
über die Elternkette) · `m11_dup` (`SEM0068`) · `m12_bothforms` (`PAR0042`) · `m13_privroot` +
Kontrolle `m13b_control` (Attribut hält toten Code) · `m14_member` (`SEM0065`, `OnMethod` wirkungslos)
· `dep/` (Uhr trifft den Konsumenten) · `proj/` + Kontrolle (Testsichtbarkeit).
