# Lyric 5 — Gebiet: Generics, Constraints, Inferenz

**Messbasis (Runde 3, nach der zweiten Kritik).** Checkout `C:/Users/Olivier/CLionProjects/lyric`
auf `6f6f029f` — *Merge pull request #173 from lyriclang/release/v4.6.0-cut*, sauber. Binaries
`lyrc.dll`/`lyrvm.dll`/`lyrfmt.dll` vom 2026-09-24 (Debug, net10.0). Proben dieser Runde:
`…/scratchpad/v5-design/probes/generics-rev4/` (**q01–q15**, je mit Erwartung im `gen.sh`-Kommentar
**vor** dem Lauf, Ausgaben unter `out/`). Proben der Runde 2 (`…/probes/generics-rev/`, r04–r63)
und der Kritik (`…/probes/generics-review/rev3/`, n01–n52) werden zitiert, wo diese Runde sie
**nachgemessen** hat; wo nicht, steht die n-Nummer mit dem Vermerk „Kritik, nicht nachgemessen".

**Zwei Warnungen zur Messbasis.**
1. **Zeitmessungen auf dieser Maschine sind verrauscht.** Der Maintainer arbeitet parallel im
   selben Checkout; r41 streute in fünf sequenziellen Läufen zwischen 932 und 1331 ms. Alle
   Zeitangaben unten sind Minima aus fünf Läufen, phasenweise aus `lyrc build --verbose`, und
   tragen eine Unsicherheit von grob ±30 %. Byte-Zahlen sind deterministisch.
2. **Belegpflicht.** Jede Aussage über Lyric 4 ist entweder *gemessen* (Probe kompiliert und
   gelaufen), *gelesen* (Pfad:Zeile) oder ausdrücklich *behauptet*.

**Versionslage, gelesen.** `git tag --list 'v4.*'` endet auf `v4.5.0`; `STATUS.md:13` — *"THE
CONDITION FOR THE TAG IS MET (2026-09-24) … Cutting the tag is the maintainer's call"*.
**Ausgeliefert ist 4.5.0**; 4.6 ist geschnitten und ungetagt.

---

## 1. Ist-Stand

### 1.1 Was das Modell festlegt

Monomorphisierung ist die einzige Möglichkeit, die der VM bleibt: `STATUS.md:2326-2328`
(gelesen) — *"Generics: monomorphization. The only option that fits this VM — C# reifies and
needs a JIT, Java erases and pays with boxing; both presuppose that the runtime knows types, and
a Lyric value carries no type tag."* Und `STATUS.md:2329-2331`: *"A value carries no type tag.
Every opcode carries its tag in the instruction stream … From that follows the fat-pointer
pattern shared by interfaces, closures and coroutines: a reference plus a word in `LyrValue`."*

> **Korrektur am Vergleich, nicht am Entscheid.** „C# … needs a JIT" ist als Vergleichsaussage
> zu grob: NativeAOT expandiert reifizierte C#-Generics vorab, ohne JIT. Was C# braucht, ist
> **Laufzeit-Typinformation** — und *die* schließt `STATUS.md:2329` aus. Der Entscheid trägt.

Die Spezifikation zieht vier Folgerungen (`lyric-spec/spec/08-generics.md:10-27`, gelesen):
constrained calls sind direkte Aufrufe; eine generische Funktion ist kein Wert (`LYR-SEM0052`);
Instanziierung ist bedarfsgetrieben; **jede Instanziierungskette muss endlich sein**. §8.1 nennt
für die polymorphe Rekursion **`LYR-IR0001`** (`:20-25`) und dehnt es auf den *"member-shaped
twin — a non-generic interface member whose result type demands an unbounded instance chain"*
aus (`:25-27`, *"refused the same way"*).

**Gemessener Preis der Monomorphisierung — Bytes, mit Kontrolle (Runde 2, unverändert gültig).**
Aufrufstellen konstant, nur die Zahl der *verschiedenen Typargumente* variiert:

| Rumpf | Probe (Instanzen / Kontrolle) | Bytecode | Δ | je Instanz |
|---|---|---:|---:|---:|
| klein (6 Zeilen) | r22 (8) / r23 (1) | 3245 / 2665 B | 580 B / 7 | **83 B** |
| klein | r37 (50) / r38 (1) | 10638 / 6243 B | 4395 B / 49 | **90 B** |
| klein | r35 (200) / r36 (1) | 40160 / 21871 B | 18289 B / 199 | **92 B** |
| groß (11 Zeilen) | r39 (50) / r40 (1) | 17552 / 6395 B | 11157 B / 49 | **228 B** |
| groß | r41 (400) / r42 (1) | 135566 / 43215 B | 92351 B / 399 | **231 B** |

Linear; die Konstante ist die Rumpfgröße, nicht eine Zahl der Sprache.

**Compilezeit — NEU gemessen, phasenweise, und die alte Zahl ist zurückgezogen.**
Runde 2 behauptete *~1.1 ms je Instanz* aus einer Wanduhr-Messung best-of-3 (1290 vs 850 ms).
Gemessen jetzt mit `lyrc build --verbose` (das Werkzeug existiert und Runde 2 hat es nicht
benutzt), r41 gegen r42, fünf sequenzielle Läufe, Minima:

| Phase | r41 (400 Inst.) | r42 (1 Inst.) | Δ | Δ je Instanz |
|---|---:|---:|---:|---:|
| check | 148 ms | 140 ms | ≈ 8 ms | ≈ 0.02 ms |
| lower (401 vs 2 Funktionen) | 199 ms | 162 ms | ≈ 37 ms | ≈ 0.09 ms |
| **verify** | 167 ms | 71 ms | **≈ 96 ms** | **≈ 0.24 ms** |
| emit | 228 ms | 174 ms | ≈ 54 ms | ≈ 0.14 ms |
| total | 932 ms | 721 ms | ≈ 210 ms | **≈ 0.5 ms** |

Die Kritik hat auf einer ruhigeren Maschine ≈ 0.3 ms je Instanz gemessen (lower 16 / verify 66 /
emit 33 ms für 399); die Größenordnung deckt sich, **1.1 ms reproduziert nicht** — die alte Zahl
enthielt Prozessstart und Rauschen. Zwei Folgen: (a) alle Extrapolationen in G28 sind neu
gerechnet (20 000 Instanzen ≈ **6–10 s**, nicht 22 s); (b) **die teuerste Achse ist der
Verifier**, nicht die Senkung — in beiden Messungen kostet `verify` je Instanz das Zwei- bis
Vierfache von `lower`. Siehe G28 und G44.

### 1.2 Was heute funktioniert (gemessen)

| Form | Probe | Ergebnis |
|---|---|---|
| Generische Methode auf generischem Typ, `Box<T>.map<U>` | r10 | ✅ (3024 B, `3!`) — normativ `spec/08-generics.md:95-113` |
| Statische generische Methode auf generischem Typ | r61 | ✅ |
| Interface-Wert erfüllt seine eigene Constraint | r62 | ✅ |
| Generische Default-Methode auf generischem Interface über Constraint | r63 | ✅ |
| **Default-Methode eines generischen Interface über einen Interface-WERT, zwei Instanzen** | **q13** | ✅ `Src<int>`/`Src<string>`, `pair()` liefert `42 2` (3169 B) |
| **Deklarationsseitige bedingte Konformanz** `struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>]` | **q03** | ✅ kompiliert (3167 B), dispatcht `true false` |
| … dieselbe mit unerfülltem `T` | **q03b** | ✅ refused `SEM0028: type 'Plain' does not satisfy constraint 'Equatable<Plain>' on 'T'` — **an der Instanz** (8:11), mit dem substituierten Constraint |
| Generische Funktion, Inferenz aus Argumenten | p01, r22c | ✅ |
| `U` aus dem Lambda-Rückgabetyp | p04, r10 | ✅ |
| F-Bound `<K :: [Equatable<K>]>` | p16 | ✅ |
| Mehrfachkonformanz `Tag :: [Equatable<Tag>, Equatable<int>]` | r32 | ✅ (`true true false`) |
| Constraint durch `extend X :: [I]` erfüllt | p33 | ✅ |
| Interface als Typargument | p38 | ✅ |
| Generisches Enum, `match` | p32b/c | ✅ |
| Alias auf eine Instanz | p43 | ✅ |
| Generische Funktion als Wert | p12 | ✅ refused `SEM0052`, guter Satz |
| `extern` + Typparameter | r34b | ✅ refused `SEM0099` |
| **Opaque als Typargument, Grundfall** | **q12c** | ✅ `Box<Meters>`, `b.v as int` → 3 |

**Interaktionsmatrix — jetzt in zwei Spalten, weil eine Spalte gelogen hat.** Runde 2 prüfte je
Feature nur *„Feature IN generischer Funktion"* mit konkreten Typen. Die Kritik hat recht: die
Form *„Typparameter IM Feature"* ist eine andere Zeile, und sie ist nicht überall grün.

| Generics × | Feature in generischer Fn (konkret) | Typparameter IM Feature |
|---|---|---|
| `throws` | ✅ r50, **q05c** (`throws Boom` in `risky<T>`, `catch (b: Boom)` → Exit 2) | ❌ **q05**: `fn risky<T, E :: [Throwable]>(…): T throws E` wird deklariert, aber `risky<int, Boom>(…)` in `try … catch (b: Boom)` → `SEM0034: call to 'risky' may throw 'Throwable', which nothing handles`. **E wird an der Aufrufstelle nicht substituiert.** Kontrolle **q05d**: `catch (t: Throwable)` entlastet (Exit 9) — der Aufruf wird auf die *Constraint* geweitet. → **G36** |
| `defer` | ✅ r51 | — (kein Typparameter im `defer` möglich) |
| `comptime` | ✅ r52 | — |
| `opaque type` | ✅ r53, q12c | ⚠ **q12**: `Box<Meters> as Box<int>` → `SEM0006` (Cast refused, `Into`-Route genannt); **q12d/q12e**: `Box<Meters>` + `Box<int>` = 2117 B gegen zweimal `Box<int>` = 2061 B → **zwei Instanzen mit identischem Layout** (56 B Aufschlag = ein Rumpf `get`). → **G45** |
| Überladung | ✅ r54 (konkret schlägt generisch, §4.3a Regel 2) | ❌ **q04/q04b/q04c**: zwei generische Kandidaten, die sich **nur in Constraints** unterscheiden, sind **immer** `SEM0086` — auch wenn nur einer anwendbar ist. → **G35** |
| Koroutinen | ❌ r12c/d, r56, r58, r59 | ❌ **q07**: generische Methode auf **nicht-generischer** Klasse — sechste Form, `VM0013` |
| `void` als Typargument | — | ❌ **q09**: `?Box<void>` → `LYR-CLI0020: field #0 'v' is void — this is a defect in the compiler`. Kontrolle q09c `?Box<int>` ✅. → **G37** |

**Constraints werden an der Deklaration geprüft** (p24 `SEM0027`, p25 `SEM0003`, p26 `SEM0059`;
Kritik n03 bestätigt: `x.foo()` in uninstanziiertem Rumpf → `SEM0027`). Das ist der
Rust/Swift/C#-Pfad und **beim Umbau zu verteidigen**.

### 1.3 Was fehlt

| Fehlend | Beleg | Folge |
|---|---|---|
| `Self`-Typ | p07 → `RES0002` | `std.core` schreibt den Selbsttyp als Parameter (`stdlib/std/core.lyr:155,167,175`) |
| Statische Interface-Member | p08 → `PAR0041`; `Parser.Declarations.cs:711-714` (gelesen) meldet und *liest den Member trotzdem weiter* | kein `parse<T>`, kein `FromJson` |
| Assoziierte Typen | `docs/Grammar.md:217-218` | `Iterator<T>` statt `Iterator` mit `Item` |
| **Bedingte Konformanz — nur die `extend`-Form fehlt.** Runde 2 schrieb *„Feature existiert nicht"*; **falsch**: die deklarationsseitige Form (`struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>]`) **existiert und dispatcht** (q03, q03b) und ist in `design/conformance-synthesis.md:72-73` (gelesen) sogar vorgeschrieben (*"Generische Typen brauchen die Constraint geschrieben"*). Was fehlt: `extend<T :: [I]> List<T> :: [I]` (p09 → `PAR0011`), `extend<T> List<T>` (**q06b** → `PAR0011`), `extend List<int>` (p09b/n09b → `SEM0047`) | `println(xs)` für fremde Container unmöglich | 
| Varianz, alle Formen | r09 (unsichere Richtung) **und q01 (sichere Richtung)** → beide `SEM0001`; r33 `?Circle`→`?Shape`; p13 Arrays/Instanzen | strikt invariant — **und die Spec sagt es so**: `spec/03-types.md:176-186` §3.7 (gelesen) *"no subtyping between declared types, and no variance"* (G22 korrigiert) |
| Höhere Kinds | — | kein `collect<C<_>>()` |
| Wertparameter | r47 → `PAR0009` | kein `[T; N]` |
| Parametrisierte Aliase | p44 → `PAR0011` | `type Result<T> = …` geht nicht |
| Default-Typargumente | p46 → `PAR0009` | — |
| Kontext-Inferenz am Aufruf | p05, **q11b** → `SEM0060` + `SEM0001` (Regel 6, `spec/08-generics.md:87-91`) | `let xs: int[] = zero();` refused |
| Partielle Typargumentliste | p06 → `SEM0026` | — |
| Typargumente im Pattern-Pfad | p32 → `PAR0002` | — |
| Instanziierte Funktion als Wert | p45 → `SEM0052` | kein `map(ident<int>)` |
| `throws E` mit Typparameter (substituiert) | **q05** → `SEM0034` | Generics × typisierte Fehler ist **nicht** grün |

### 1.4 Was still oder falsch ist — die Befunde

**(a) Ein aus dem Typparameter GEBAUTES Typargument wird nicht substituiert.** r22a:
`inner<T[]>([x])` in `outer<T>` → `IR0001: type parameter 'T' reached lowering unsubstituted`,
gemeldet an der Deklaration von `inner` (1:13). Kontrolle r22c (flaches `T`) ✅. Ursache
gelesen: flaches `t is TypeParamType p ? bound : t` in `FunctionLowerer.cs:3778`, `:4691`;
die tiefe `SubstituteType` (`:5135`) steht daneben; derselbe flache Test im
„nicht konkret"-Wächter `InstanceTable.cs:150-152`. `IR0001` ist hier der **richtige** Code
(`spec/12-diagnostics.md:70-72`: gültiges Lyric, das die Implementierung nicht senkt).

**(b) Divergente Monomorphisierung: ZWEI Wächter, drei Ausgänge.** Runde 2 kannte nur einen
Wächter (`InstanceTable.cs:45` `MaxInstances = 20_000`, `Guard` bei `:176-186`) und schrieb,
`STATUS.md:1303` gelte *„für diese Form nicht"*. Die Kritik hat recht: es gibt einen zweiten.

| Form | Probe | Ausgang |
|---|---|---|
| **Klassen-Methode**, `It<T>.deep` konstruiert `It<T[]>` und ruft sich darauf | r31, **q15** (nachgemessen) | ❌ **Absturz**: `Stack overflow` in `TypeFacts.Display` ← `TypeTable.Intern` ← `ResolveFieldAccess`, Exit 127, **stderr, kein Code, keine Position** |
| **Interface-Member** mit Ergebnistyp `It<T[]>` (die Form aus `STATUS.md:1303` und `spec/08-generics.md:25-27`) | **q02** (nachgemessen, = n07) | ✅ **Diagnose**, aber **ohne Position**: `error[LYR-IR0001]: the monomorphization does not terminate: every round asks for further type instances …` — das ist der **Rundenwächter** `ModuleLowerer.cs:57` `MaxLoweringRounds = 100`, Meldung `:478-483` mit `default`-Span (gelesen) |
| Instanzzähler `MaxInstances` | — | wird in **keiner** gemessenen Form erreicht (`Guard` prüft `_pending.Count` in `Request`; der Absturz passiert in `LowerAll`) |

`STATUS.md:1303` (*"that is now a diagnostic instead of a hang"*) **gilt für die
Interface-Form** — Runde 2 hat das falsch verallgemeinert. Für die Klassen-Methoden-Form gilt es
nicht. Und: `spec/12-diagnostics.md:52-62` §12.4 (gelesen) verlangt *"What IS required is that
the limit be reported and not crashed into"* — der r31/q15-Absturz ist damit ein
**§12.4-Konformanzverstoß**, nicht bloß ein Bug. → G18 (neu gestellt).

**(c) Die Constraint-Liste akzeptiert alles.** r45 `<T :: [int]>`, r46 `<T :: [Plain]>` filtert
nicht (liefert 2), r04 `<T :: [A1, A1]>` läuft. Kontrolle n02 (Kritik, nicht nachgemessen):
mit einem *Interface* als Constraint filtert sie (`SEM0028`).

> **Belegsatz korrigiert.** Runde 2 schrieb *„Die Grammatik sagt dazu nichts"*. Falsch:
> `docs/Grammar.md:139` (gelesen) — *"`::` introduces an interface list and never appears in a
> module path"* — ist eine Bedeutungsaussage für **jedes** `::`; und `:281-283` nennt die drei
> Regeln (*"every entry names an interface, the chain is acyclic, an entry may not repeat an
> earlier one"*) ausdrücklich als *"Semantic rules, not syntactic ones"* — **für die
> Elternliste**. Was stimmt: `docs/Grammar.md:218` erlaubt syntaktisch jeden `TypeExpr`, und die
> drei Regeln sind für die Constraint-Liste **nicht** implementiert. Der Befund bleibt, der Satz
> über die Grammatik war falsch. `SEM0078`s Geltungsbereich (`appendix-a-diagnostics.md:172`)
> nennt die Constraint-Liste nicht.

**(d) `f<Box<int>>(x)` ist ein Parserfehler — und der Leerzeichen-Workaround überlebt den
Formatter nicht.** r43 `take<Box<int>>(inner)` → `SEM0052`-Kaskade; r44 `take<Box<int> >(inner)`
✅. Ursache: `LooksLikeCallTypeArguments` (`Parser.cs:250-289`, gelesen) kennt `TokenKind.Shr`
nicht; `SkipTypeArgs` (`:641`) schon. **NEU gemessen q14/q14b:** `lyrfmt` (in place) schreibt
`take<Box<int> >(inner)` zu `take<Box<int>>(inner)` um — `AstFormatter.cs:1047-1051` (gelesen)
druckt `<`, Argumente, `>` ohne Rücksicht auf ein angrenzendes `>` — und **die formatierte Datei
kompiliert nicht mehr** (`SEM0052` ×3). Die Fläche des Bugs ist Parser **und** Formatter; G13-C
(„Leerzeichen dokumentieren") ist damit keine Option.

**(e) `<error>` leckt in eine Diagnose.** p07 `SEM0042: … expected '<error>'` nach `RES0002`.

**(f) Monomorphisierte Koroutinen verlieren ihren Rahmen — SECHS Formen, ein Fundort.**
r12c/r12d (generische Fn), r59 (generische Fn, konkretes `Coroutine<int>`), r56/r58 (Methode
eines generischen Typs), **q07** (generische Methode auf nicht-generischer Klasse, nachgemessen):
alle `panic [LYR-VM0013]: yield outside a running resume`. Kontrollen r11c, r57 ✅.
**Fundort, gelesen (aus der Kritik übernommen und nachgelesen):** `InstanceTable.cs:232-236`
`LowerAll` baut `new FunctionLowerer(p.Decl, …, p.Substitution, …)` **ohne** `coroutineYield`;
der nicht-generische Pfad benutzt `FunctionLowerer.ForCoroutineBody` (`FunctionLowerer.cs:305-310`,
`coroutineYield: yieldType`). Der Instanz-Lowerer weiß nie, dass er eine Koroutine senkt. **Die
Reparatur ist ein Konstruktorargument** — das ändert die Empfehlung in G21.

**(g) NEU: `extend` auf einem generischen Typ ohne Argumente fällt mit falschem Code an
falscher Stelle.** **q06** (nachgemessen, = n09): `extend Bag { … }` auf `class Bag<T :: [A1]>`
passiert Parser und Sema und fällt in der Senkung als
`q06_…lyr:3:1: error[LYR-IR0001]: generic type 'Bag' needs 1 type argument(s), got 0` — Position
ist die **Klassendeklaration**, nicht das `extend`; und `IR0001` ist laut §12.1 für *gültiges*
Lyric — das hier ist ungültiges. Richtig wäre `SEM0047` am `extend` (wie bei `extend Bag<int>`,
n09b). → G38.

**(h) NEU: `a<b>(c)` mit drei `int`s ist ein Aufruf mit Typargumenten.** **q10** (nachgemessen):
`let r = a<b>(c);` → `SEM0013: 'int' is not callable`. Kontrolle **q10c** `a < b` → `true`. §6.3
löst zugunsten des Aufrufs auf, sobald der Folger `(` ist, unabhängig davon, ob `a` generisch
ist. → G41.

**(i) NEU: Phantom-Typparameter sind still erlaubt.** **q08** (nachgemessen): `class Tagged<T>
{ n: int }` und `fn f<T>(n: int)` kompilieren, `Tagged<string>`/`Tagged<int>` und
`f<bool>`/`f<string>` laufen (Exit 3, 2095 B) — ohne Warnung. → G39.

### 1.5 Inferenz: erste Bindung gewinnt (Entscheidung, keine Lücke)

p29: `same(1, s)` → `SEM0001` am zweiten Argument, Spec-Regel 3 (`spec/08-generics.md:65-72`).
Kein LUB. Jede Rückwärtsinferenz (G11) zieht daran.

### 1.6 Die 4.x→5-Brücke: Migrationswarnungen (§12.5)

`spec/12-diagnostics.md:76-104` (gelesen): eine Migrationswarnung *"does not presume the
answer"*, *"names the version"*, *"retires WITH its rule"*. Vier laufen (`SEM0107`–`SEM0110`,
`STATUS.md:50-56`). Konsequenz: eine Warnuhr in 4.x nur, wo der Befund **ohne** die Antwort
formulierbar ist.

### 1.7 Was NICHT existiert und trotzdem vorausgesetzt wird: `lyrfix`

Gelesen: `src/` enthält `Lyrfmt`, `Lyrpack`, `Lyrtest`, `Lyrdbg`, `Lyrls`, `Lyrstub`,
`Lyrbuild`, `Lyrrepl` — **kein `Lyrfix`**. `docs/Befunde_und_Verbesserungen/PLAN.md:343`: *"`lyrfix`
als Migrationswerkzeug für die 5.0-Brüche — die Ablage unten setzt es voraus"*;
`lyric-v5-features.md:163`. Runde 2 nannte den `until`-Posten als Vorbedingung von G02/G20/G32
und **das fehlende Werkzeug nicht**. Korrigiert: `lyrfix` ist eine Vorbedingung jeder
Major-Empfehlung in diesem Dossier und steht als solche in §5.

---

## 2. Sprachvergleich

| Sprache | Modell | `Self` / statische Member | Bedingte Konformanz | Varianz | HKT | Preis |
|---|---|---|---|---|---|---|
| **Rust** | Monomorphisierung | `Self` + assoziierte Typen | `impl<T: Debug> Debug for Vec<T>` (**nicht** `Display` — siehe unten), Kohärenz + Orphan-Regel | berechnet über alle Parameter (`fn(T)` kontravariant in `T`) — aber **kein nominales Subtyping zwischen Typen**, also nur über Lebenszeiten beobachtbar | nein (GATs) | Codegröße, Compilezeit; `recursion_limit` (Default 128) |
| **Swift** | Witness-Tables, Spezialisierung ist Optimierung | `Self` + `associatedtype` + `static`; Existential-Schranke vor 5.7 galt **`Self`/assoziierten Typen**, nicht `static` an sich | `extension Array: … where Element: …` | Funktionstypen **mit Reabstraction-Thunk**; Collections kovariant via eingebautem Upcast | nein | Laufzeitkosten, Metadaten |
| **C#** | Reifiziert; NativeAOT expandiert vorab | `static abstract` seit C# 11; F-Bound `where T : INumber<T>` | constrained extension methods seit 3.0; **seit 7.3 sind Constraints Teil der Anwendbarkeit** | deklarationsseitig `in`/`out`, nur Interfaces/Delegates; Delegate-Varianz kostenlos **nur wegen uniformer Referenzrepräsentation** | nein | Laufzeit-Typinfo |
| **Haskell** | Dictionary-Passing | `a` in Signaturen | `instance Show a => Show [a]` | keine | ja | Dictionaries, Kohärenz |
| **Scala** | Erasure + Subtyping | `this.type` | `given` | `+A`/`-A` | ja | implicits |
| **Go** | GC-Shape-Stenciling | nein | nein | Interface-Zuweisbarkeit | nein | keine generischen Methoden |
| **OCaml** | uniform, Tag-Bit | Funktoren | Funktoren | Annotationen | Funktoren | Immediates + Boxing |
| **Zig** | comptime | keine Interfaces | — | — | — | Fehler im Bibliotheksinneren |

**Korrekturen an Runde 2 (sieben, jede von der Kritik angestoßen und hier geprüft):**

1. **Swift, kovariante Arrays: „kopiert, plattformunabhängig, kein Bridging" war falsch.**
   *Behauptet* (Kenntnis der stdlib-Quelle `ArrayCast.swift`, nicht hier verifizierbar): der
   schnelle Pfad von `_arrayForceCast` steht unter `#if _runtime(_ObjC)` und **reinterpretiert**
   für Klassen-Elementtypen den nativen Puffer als Zieltyp (O(1), „deferred type check");
   elementweise Kopie ist der Rückfall. Sicher ist der Upcast nicht „weil kopiert", sondern
   weil Array **Wertsemantik** hat — kein Aliasing. G07-D ist entsprechend umformuliert: Swifts
   Preis ist nicht die O(n)-Kopie; Lyrics Preis wäre sie, weil Lyric-Arrays Referenzen sind.
2. **Rust als Vorbild für Funktions-Kontravarianz (G22) trägt nicht.** Rust hat kein nominales
   Subtyping; `fn(&Circle)` kann nie stehen, wo `fn(&dyn Shape)` erwartet wird — die Frage
   existiert dort nicht. Die einschlägigen Präzedenzfälle sind Swift (Konversion mit
   Reabstraction-Thunk), Kotlin, C#-Delegates, TypeScript `strictFunctionTypes` — **und alle vier
   lehnen r09s Richtung ab** (sie ist unsicher, siehe G22).
3. **Swift SE-0309 und statische Anforderungen: `static` ≠ `Self`.** Vor 5.7 hieß die Sperre
   *"can only be used as a generic constraint because it has Self or associated type
   requirements"*. `static func configure()` hat ein Existential nie blockiert; blockiert war
   `static func make() -> Self` — wegen `Self`. Runde 2s *„Swift vor 5.7 = Option B"* beschrieb
   einen Zustand, den Swift so nicht hatte. G03-A wird dadurch weder stärker noch schwächer; die
   Swift-Zeile trägt die Unterscheidung nur nicht.
4. **`impl<T: Display> Display for Vec<T>` existiert in Rusts std nicht** — `Vec<T>` ist `Debug`
   (mit `T: Debug`), nicht `Display`, und die Orphan-Regel verbietet dem Nutzer, es
   nachzurüsten. Aus `design/conditional-conformance.md:41` als Syntaxbeispiel übernommen; als
   Aussage über Rust falsch. Korrigiert.
5. **C# und Constraints in der Überladung (G26/G35): Runde 2 hat die Hälfte übernommen, die
   Lyric schon hat.** Seit C# 7.3 werden Kandidaten, deren Constraints das Argument nicht
   erfüllt, **vor** dem Ranking entfernt („improved overload candidates"); nur unter den
   verbleibenden gilt „kein Ranking, Gleichstand = CS0121". Die Filterhälfte fehlt Lyric
   (q04b).
6. **Rust hat keine stabilen Generatoren** — `fn f<T>() -> impl Iterator` ist kein Generator.
   Das nächste stabile Analogon für G21 ist generisches `async fn` (Zustandsmaschine je
   Instanziierung). Korrigiert.
7. **`type_length_limit` ist kein Vorbild mehr** (*behauptet*, Release-Stand unsicher): seit den
   1.7x-Releases nur noch wirksam, wenn explizit gesetzt. `recursion_limit` (128) bleibt der
   Präzedenzfall — und zwar mit §12.4s Untergrenze **128** in derselben Größenordnung.

**Was stehen bleibt (aus Runde 2, geprüft):** Rusts Varianz über alle Parameter; `>>` als
C++11-Problem (Java seit 5); Swift ohne feste Generics-Tiefengrenze; C# braucht
Laufzeit-Typinfo, keinen JIT; C# 3.0 als näherer Präzedenzfall für `extend`; Gos
Interface-Zuweisbarkeit; OCamls Immediates.

**Die entgegengesetzte Entscheidung.** Go verbietet generische Methoden; Lyric hat dieselbe
Einsicht (*"a member with type parameters of its own gets NO vtable slot"*, `STATUS.md:1295-1296`,
normativ `spec/05-interfaces.md:83-95` §5.2a und `spec/08-generics.md:101-107`) und die andere
Antwort.

---

## 3. Designfragen

### G01 — Bleibt Monomorphisierung das einzige Modell?

**Heute.** Ja (`STATUS.md:2326-2331`, `spec/08-generics.md:5-8`). Preis linear (§1.1), ≈ 0.3–0.5 ms
Compilezeit je Instanz, davon der größte Teil im Verifier.

**Optionen.**
- **A — bleibt.** Vorbild Rust. Preis: Codegröße; keine getrennte Übersetzung (G23); polymorphe
  Rekursion verboten.
- **B — Dictionary-Fallback für Zeiger-Shapes.** Vorbild Go. Preis: zweiter Mechanismus, bricht
  `STATUS.md:2329`.
- **C — uniforme Repräsentation.** Vorbild OCaml. Preis: Gegenteil der VM-Entscheidung.

**Empfehlung: A.** Dazu Instanzbudget (G28), Deduplikation (G30), getrennte Übersetzung (G23).
**Bricht:** nein. **Hängt an:** G23, G28, G30, G44.

---

### G02 — Bekommt v5 einen `Self`-Typ?

**Heute.** Nein (`RES0002`, p07; `<error>`-Leck §1.4e). Ersatz F-Bound (`stdlib/std/core.lyr:155`).
`STATUS.md:1727-1729`: P1.

**Optionen.**
- **A — `Self` im Interface-Körper**, im Interface-Wert verboten. Vorbild Rust/Swift. Preis:
  Regel, wo `Self` steht; **Mehrfachkonformanz fällt** (G32).
- **B — F-Bound bleibt.** Vorbild C#. Preis: Typname doppelt; `struct A :: [Equatable<B>]`
  ungehindert.
- **C — beides als Zucker.** Rule 2.
- **D — `Self` einführen, F-Bound behalten für `X ≠ Self`.** Preis: Rule 2 formal, mit
  benennbarer Grenze (zwei Konzepte).

**Der Preis von A.** r32 + `spec/05-interfaces.md:20-23`: `Tag :: [Equatable<Tag>, Equatable<int>]`
ist Feature seit 3.0 und mit `Self`-`Equatable` nicht mehr schreibbar.

**Empfehlung: A für `Hashable`/`Ordered`, D für `Equatable`.** Wer A pur will, schreibt den
Feature-Rückbau in die Bruchliste und ändert `spec/05-interfaces.md:20-23` mit.
**Bricht: major.** **§12.5 greift NICHT** (eine Warnung auf `Equatable<T>` wäre die Antwort).
**Vorbedingungen (NEU vollständig):** `@Deprecated`+`until` **und `lyrfix`, das es nicht gibt**
(§1.7). `lyrfix` schreibt `Equatable<Coord>` → `Equatable` um, solange der Parameter der eigene
Typ ist, und **meldet** `Equatable<X>` mit `X ≠ Self` als nicht migrierbar.
**Hängt an:** G03, G04, G32, G33, G42; Konformanz-Synthese.

---

### G03 — Statische Interface-Member?

**Heute.** Nein: `docs/Grammar.md:291-292`; p08 → `PAR0041`, gemeldet in
`Parser.Declarations.cs:711-714` (gelesen) — *und der Member wird danach trotzdem gelesen*
(*"Read on either way"*). **Vorarbeit, die Runde 2 ignoriert hat (Kritik, nachgelesen):**
`docs/Befunde_und_Verbesserungen/TASKLIST_2.md:617-629` (Befund) und `:1575-1587` (*"Prototyp 13:
statische Interface-Anforderungen"*, Aufwandsschätzung *"Parser 3, Sema ~120, Lowering ~30, VM 0"*,
Muster `Zero<T>` ohne `Self`). Der Prototyp liegt laut `:1586` unter einem `/tmp/…`-Pfad einer
anderen Maschine und ist hier **nicht messbar**; seine Zeilenangabe `:652` ist gegen den heutigen
Baum tot (dort steht `ParseExtend`). Die Schätzung ist die einzige Zahl, die es zu dieser Frage
gibt, und sie gehört zitiert statt neu hergeleitet.

**Optionen.**
- **A — statische Member, nur über Constraint aufrufbar.** Vorbild C# 11 (`static abstract`);
  Swift (statische Anforderung ohne `Self` war nie Existential-Schranke, mit `Self` seit 5.7
  ebenfalls nicht). Preis: zwei Aufrufwege; Interface-Wert verweigert mit eigener Diagnose. **Das
  ist genau Prototyp 13 (b).**
- **B — Interface mit statischen Membern ist kein Existential.** Vorbild: **keine** ausgewachsene
  Sprache (die Swift-Zuschreibung aus Runde 2 war falsch). Preis: Zweiteilung.
- **C — nein.** Preis: `parse<T>`, `FromJson` unbaubar.

**Empfehlung: A, entlang Prototyp 13.** Offen bleibt, ob mit `Self` (G02) oder im
`Zero<T>`-Muster ohne — der Prototyp wählt das Muster ohne `Self`; wenn G02-A/D kommt, ist
`static fn parse(s: string): ?Self` die bessere Form. Die beiden sind **eine** Lieferung.
**Bricht:** nein. **Hängt an:** G02, G42.

---

### G04 — Assoziierte Typen statt Interface-Typparameter?

**Heute.** Nicht gestellt; `Iterator<T>` (`stdlib/std/iter.lyr:21`).
**Optionen.** A Parameter bleiben (Go, C#) · B assoziierte Typen zusätzlich (Rust/Swift; Rule 2,
ADR, `I.Item == int`-Syntax) · C statt Parameter (kassiert `Add<T, R>`, `STATUS.md:1413-1414`).
**Empfehlung: A für v5.** **Bricht:** nein. **Hängt an:** G02, G32.

---

### G05 — Bedingte Konformanz: Syntax, Umfang — und der Vorläufer, der schon existiert

**Heute — NEU und anders als Runde 2.**
- **Deklarationsseitig existiert sie.** q03: `struct Pair<T :: [Equatable<T>]> ::
  [Equatable<Pair<T>>]` kompiliert und dispatcht; q03b: unerfülltes `T` → `SEM0028` **an der
  Instanz**, mit dem substituierten Constraint `Equatable<Plain>`. Das ist Rusts
  `impl<T: PartialEq> PartialEq for Pair<T>`, nur am Typ statt in einem `impl`-Block —
  und `design/conformance-synthesis.md:72-73` schreibt genau diese Form vor.
- **Was fehlt, ist die nachträgliche `extend`-Form auf fremden Containern:** p09
  `extend<T :: [I]> List<T> :: [I]` → `PAR0011`; q06b `extend<T> Bag<T>` → `PAR0011`; n09b
  `extend Bag<int>` → `SEM0047`; q06 `extend Bag` → `IR0001` an der falschen Stelle (§1.4g).

**Optionen.**
- **A — Parameter vor dem Ziel**, `extend<T :: [Display]> List<T> :: [Display]`. Vorbild Rust
  (`impl<T: Bound> Trait for Type<T>`). Begründet parsebar (`design/conditional-conformance.md:39-42`).
  **Und die Semantik ist vorgegeben**: dieselbe Unifikation und dieselbe Diagnose wie q03b —
  `SEM0028` an der Instanz mit substituiertem Constraint.
- **B — `where` hinter dem Ziel.** Vorbild Swift. Preis: neues Schlüsselwort, zweite
  Constraint-Stelle (G09).
- **C — Compiler-Sonderregel je Container.** Abgelehnt (`design/conditional-conformance.md:7`).

**Empfehlung: A, ausgerichtet am Vorläufer q03.** Die `extend`-Form darf keine zweite Semantik
bekommen — was `struct Pair<T :: […]> :: […]` an der Instanz meldet, meldet
`extend<T :: […]> List<T> :: […]` genauso. Der unbedingte Grundfall `extend<T> List<T>` (G38)
und `extend List<int>` gehören in denselben Zug.
**Bricht:** nein. **Hängt an:** G06, G20, G26, G35, G38, G47.

---

### G06 — Bedingte Methoden ohne Konformanz?

**Heute.** Nicht vorhanden (Grammatik wie G05). `List<int>.sum()` ist eine freie Funktion.
**Optionen.** A ja, `extend<T :: [Ordered<T>]> List<T> { fn sort() }` — Vorbild C# 3.0 constrained
extension methods, dann Rust `impl<T: Ord> Vec<T>`; Preis: Methodensuche wertet Constraints aus,
Diagnose nennt den Grund · B nein — Preis: Iterator-Kette bricht am letzten Glied.
**Empfehlung: A — aber nicht ohne G35 und G26.** Gemessen q04/q04b: schon heute sind zwei
generische Kandidaten, die sich nur in Constraints unterscheiden, *immer* mehrdeutig. Bedingte
Methoden würden diesen Bug zur Normalform machen.
**Bricht:** nein. **Hängt an:** G05, **G35**, G26.

---

### G07 — Varianz bei nominalen Instanzen und Arrays

**Heute.** Strikt invariant (p13); **spezifiziert** in `spec/03-types.md:183-186` §3.7 (*"no
subtyping between declared types, and no variance"*). Ausweg: Interface-Elementtyp (p38).

**Optionen.**
- **A — invariant bleiben**, wie §3.7. Vorbild Go, Rust (nominal). Preis: `fn draw(xs: Shape[])`
  nimmt kein `Circle[]`; der Aufrufer deklariert `Shape[]`.
- **B — deklarationsseitige Varianz.** Vorbild C#/Kotlin/Scala. Preis: bei monomorphisierten
  Layouts ist `Box<Circle>` → `Box<Shape>` eine **Kopie**, kein Cast — weil ein `Circle` eine
  nackte Referenz und ein `Shape` ein fat pointer ist (`STATUS.md:2329-2331`, `IrInst.cs:73-76`
  `MakeInterface` *"attaches the concrete type … to an object reference"*, gelesen).
- **C — kovariante Arrays mit Laufzeitprüfung.** Java. Ausgeschlossen (kein Laufzeit-Typtest).
- **D — kovariante Arrays wie Swift.** **Korrigiert:** Swifts Upcast ist sicher, weil Swift-Arrays
  **Wertsemantik** haben (kein Aliasing), nicht „weil kopiert"; auf Apple-Plattformen ist er für
  Klassen-Elemente O(1) (*behauptet*, §2 Korrektur 1). Lyric-Arrays sind Referenzen — hier
  **wäre** die Konversion eine O(n)-Kopie plus `mkiface` je Element, oder unsicher. Der Preis, den
  D in Lyric zahlt, ist also nicht Swifts Preis, sondern ein höherer.

**Empfehlung: A.** D ist bewusst abgelehnt, mit dem richtigen Grund: Lyric hat weder
Wertsemantik-Arrays noch eine uniforme Repräsentation, die den Upcast billig machte.
**Bricht:** nein. **Hängt an:** G08, G22.

---

### G08 — Höhere Kinds?

**Heute.** Nicht vorhanden (`docs/Grammar.md:218`).
**Optionen.** A nein (Rust, Swift, C#, Go) · B ja (Haskell, Scala; Konstruktor-Unifikation).
**Empfehlung: A, schriftlich.** `collect` über `FromIterator<T>`-Constraint auf dem Ergebnistyp
(G11). **Bricht:** nein. **Hängt an:** G11.

---

### G09 — `::`-Liste oder `where`-Klausel?

**Heute.** `::` an zwei Stellen, `docs/Grammar.md:139` gibt die eine Bedeutung („interface list").
**Optionen.** A `::` überall · B `where` zusätzlich (Rule 2) · C `where` statt `::` am
Typparameter (major).
**Empfehlung: A für v5.** Wenn G04 je kommt, C neu stellen. **Bricht:** A nein, C major.
**Hängt an:** G04.

---

### G10 — Was die Constraint-Liste akzeptieren darf: Interface, Zirkel, Wiederholung

**Heute.** r45, r46, r04: alles akzeptiert, nichts geprüft. Belege korrigiert (§1.4c): die
Bedeutung steht in `docs/Grammar.md:139` und `spec/08-generics.md:37`; die drei Regeln stehen in
`docs/Grammar.md:281-283` für die Elternliste und in `spec/05-interfaces.md:28-30`; `SEM0078`
(`appendix-a-diagnostics.md:172`) nennt Eltern- und Konformanzliste, nicht die Constraint-Liste.

**Optionen.** A alle drei Regeln importieren (Regel-PR, `since:`-Gate, Warnung 4.7, Fehler 5.0)
· A′ nur Interface-Teil · B Nicht-Interfaces mit Bedeutung (Typgleichheit) — zweite Bedeutung
für dieselbe Klammer · C lassen.
**Empfehlung: A über einen neuen Code** (G24-C), nicht durch Dehnen von `SEM0078`. **§12.5
greift** (der Befund ist ohne die Antwort formulierbar). **Bricht: minor.**
**Konformanzfälle (G46):** je Regel einer (`constraint_must_name_an_interface`,
`constraint_list_does_not_repeat`, `constraint_chain_is_acyclic`), `since: 5.0.0`, in 4.7 als
Warnfall.
**Hängt an:** G24, G46.

---

### G11 — Inferenz aus dem Kontext der Aufrufstelle?

**Heute.** Nein; Regel 6 (`spec/08-generics.md:87-91`); p05, **q11b** (`let s: string[] =
make();` → `SEM0001` + `SEM0060`). Konstruktionen sind die Ausnahme (§8.2).

**Optionen.** A bleiben (C#) · B erwartete Typ bindet, was offen bleibt (Rust/Swift/Haskell;
zweite Richtung, Regel 3 gefährdet) · C1 nur bei leerer Argumentliste · C2 wenn nach allen
Argumenten ein Parameter offen bleibt, bindet der erwartete Typ ihn **zuletzt**; Empfänger zählt
als Argument; danach `SEM0060`.
**Empfehlung: C2** — deckt `empty()`, `xs.collect()`, `T.parse(s)`. **Aber mit G43 zusammen:** ob
der Kontext an der **Wahl** zwischen Überladungen teilnimmt, ist eine eigene Frage, und die
Antwort dort ist heute „nein" (q11).
**Bricht:** nein. **Hängt an:** G03, G08, G12, **G43**.

---

### G12 — Partielle Typargumentlisten

**Heute.** p06 → `SEM0026`. **Optionen.** A bleiben · B `_` (Rust) · C kürzere Liste (still,
positionsabhängig). **Empfehlung: B.** **Bricht:** nein. **Hängt an:** G13.

---

### G13 — `>>` in der Typargumentliste eines Aufrufs — Parser UND Formatter

**Heute.** r43 ❌ / r44 ✅ (Leerzeichen); Ursache `Parser.cs:250-289` ohne `Shr`, `:641` mit.
**NEU gemessen q14/q14b:** `lyrfmt` normalisiert `> >` zu `>>` (`AstFormatter.cs:1047-1051`), und
die Ausgabe kompiliert nicht mehr (`SEM0052` ×3). Der Workaround aus Runde 2 überlebt genau den
Schritt nicht, den jeder Editor beim Speichern macht.

**Optionen.**
- **A — `Shr` im Lookahead behandeln** wie in `SkipTypeArgs`. Vorbild C++11 (Java seit 5, C#,
  Rust). Preis: eine `case`-Zeile im Parser, ein Satz in Grammar §6.3 zur Tokenspaltung — **und
  ein Test, dass `lyrfmt`-Ausgabe kompiliert** (Roundtrip). Der Formatter selbst bleibt richtig,
  sobald der Parser `>>` liest.
- **B — Turbofish.** Ausgeschlossen (`::` hat schon zwei Bedeutungen).
- **C — lassen, Leerzeichen dokumentieren.** **Keine Option mehr:** der Formatter entfernt das
  Leerzeichen.

**Empfehlung: A, als Regel-PR + Fix, mit Formatter-Roundtrip-Test.** Die Fläche ist nicht „eine
`case`-Zeile", sondern Parser + Grammar-Satz + Roundtrip-Test; LSP-Completion, die Typargumente
einfügt, gehört geprüft (nicht gemessen). Diagnose mitreparieren („ist ein Typ, kein Wert" ist
die falsche Erklärung).
**Konformanzfall (G46):** `nested_type_arguments_close_with_shr`, `since:` mit dem Fix.
**Bricht:** nein. **Hängt an:** G12, G40.

---

### G14 — Typargumente im Pattern-Pfad

**Heute.** p32 → `PAR0002`; Kontrollen `Res.Ok(_)`, `Ok(_)` ✅. **Optionen.** A erlauben (vierte
Folgerzeile in §6.3) · B refusen mit Hinweis · C lassen. **Empfehlung: A.** **Bricht:** nein.
**Hängt an:** G13, Pattern-Gebiet.

---

### G15 — Parametrisierte Typaliase

**Heute.** p44 → `PAR0011`; `docs/Grammar.md:176`. **Optionen.** A einführen (Rust `type Result<T>`)
· B nein. **Empfehlung: A transparent**, opaque getrennt. **Bricht:** nein.

---

### G16 — Default-Typargumente

**Heute.** p46 → `PAR0009`. **Optionen.** A nein · B ja, an Typen. **Empfehlung: A, schriftlich** —
mit dem Preis aus G32 (Rusts `PartialEq<Rhs = Self>` wäre der Ausweg). **Bricht:** nein.
**Hängt an:** G12, G27, G32.

---

### G17 — Instanziierte Funktion als Wert

**Heute.** p45 `let f = ident<int>;` → `SEM0052` + Folgefehler. **Optionen.** A erlauben (Rust
`ident::<i32>`, C#, Swift; `InstanceTable.Request` existiert) · B Diagnose reparieren · C auch
generische Form (bricht `spec/08-generics.md:14-16`). **Empfehlung: A.** **Bricht:** nein.
**Hängt an:** G13.

---

### G18 — Divergente Instanzkette: welcher Wächter, welcher Code, welche Position — und §12.4

> **Neu gestellt.** Runde 2 empfahl *„Typtiefe ≤ 32 als §8.1-Regel, mit Zahl"*. Das kollidiert
> mit `spec/12-diagnostics.md:52-62` §12.4 doppelt (gelesen): *"The depth is not specified and is
> not part of the contract"* und *"A conforming implementation accepts nesting of at least 128"*.
> Eine Regel „≤ 32" im Vertrag wäre ein Verstoß gegen beide Sätze. Zurückgezogen.

**Heute: drei Wächter, drei Ausgänge, ein Absturz.**
1. `InstanceTable.Guard` (`:176-186`), `MaxInstances = 20_000`, `IR0001` **mit** Span — in keiner
   gemessenen Form erreicht.
2. `ModuleLowerer` Rundenwächter (`:57` `MaxLoweringRounds = 100`, `:478-483`), `IR0001` **ohne**
   Position (`default`-Span) — **q02** (Interface-Member-Form) landet hier.
3. **Kein** Wächter für die Klassen-Methoden-Form: **q15** (= r31) → CLR-Stack-Overflow in
   `TypeFacts.Display` unter `TypeTable.Intern`, Exit 127, stderr. Nach §12.4 (*"reported and not
   crashed into"*) ein **Konformanzverstoß**; §12.4 hat für den Check bereits `SEM0105`
   (`TypeChecker.cs:203`, `MaxNesting`, gelesen) und für den Parser `PAR0045`
   (`appendix-a:82`, `:202`, beide *since 4.6*).
4. Dazu §1.4a: die **flache** Substitution fängt r30 (echte polymorphe Rekursion, `IR0001`) und
   r22a (endliche Kette, fälschlich) mit demselben Fehler.

**Der Reihenfolge-Befund aus Runde 2, präzisiert.** *„Die flache Substitution ist die einzige
Terminierungswache"* stimmt **nur für die Klassen-Methoden-Form**; für die Interface-Form wacht
der Rundenzähler bereits (q02). Wer §1.4a zuerst tief macht, verwandelt r30 in q15 — für
Klassen-Methoden. Die Reihenfolge bleibt, ihre Begründung ist enger.

**Optionen.**
- **A — ein Code, eine Position, drei Auslöser.** Alle drei Wächter melden `IR0001` (§8.1 nennt
  ihn) **mit Position und Kette** (G31); der Rundenwächter bekommt den Span der zuletzt
  angeforderten Instanz; `TypeTable.Intern`/`TypeFacts.Display` bekommen eine Schachtelungsgrenze
  im Sinne von §12.4 (implementation-defined, ≥ 128), die als Diagnose endet statt als Stack.
  Vorbild Rust `recursion_limit` (128) — dieselbe Größenordnung wie §12.4s Untergrenze.
  Preis: die Tiefe ist **kein** Vertragsbestandteil, also kein Konformanzfall über die Zahl —
  nur über das Verhalten („wird gemeldet").
- **B — `TypeFacts.Display` iterativ machen, beim Instanzzähler bleiben.** Preis: 20 000 Instanzen
  entstehen (≈ 6–10 s, G28), bevor der Wächter greift; der Text nennt eine Zahl statt einer Kette.
- **C — Typtiefe als §8.1-Regel mit Zahl.** **Ausgeschlossen** durch §12.4.

**Empfehlung: A, in dieser Reihenfolge:** (1) Rundenwächter und Intern-Rekursion bekommen
Position + §12.4-konforme Grenze; der q15-Absturz wird zur Diagnose — das ist ein
Konformanz-Fix, kein Regel-PR (§12.4 gilt seit 4.6); (2) Instanzzähler als Netz, sichtbar (G28);
(3) **erst dann** tiefe Substitution (§1.4a). Die Frage „gilt r31 als §12.4-Verstoß?" ist mit ja
zu beantworten und in den Konformanzfall `divergent_instance_chain_is_reported` zu gießen
(`since: 4.6.0`, weil §12.4 seit 4.6 gilt — **heute rot**).
**Bricht:** nein. **Hängt an:** G28, G29, G31, G46.

---

### G19 — Typ-Sets und eingebaute Constraints

**Heute.** Nicht vorhanden; `std.math` mit Suffixen (`lyric-v5-features.md:58`).
**Optionen.** A Interfaces auf eingebaute Typen (C# 11 `INumber<TSelf>`; braucht G02) · B Typ-Sets
wie Go (zweiter Mechanismus) · C bleiben. **Empfehlung: A.** **Bricht:** nein. **Hängt an:** G02, G03.

---

### G20 — Generische `extend`-Ziele: Arrays, Tupel, opaque

**Heute.** `SEM0047` (p09b); stilles Loch `extend int[] :: [Iterable<int>]` (`STATUS.md:2250`;
Kritik n06 bestätigt: kompiliert, tut nichts — nicht nachgemessen); n46 `extend Meters`
akzeptiert, n46b Konformanz wirkt nicht (`SEM0001`) — nicht nachgemessen.
**Optionen.** A nur nominale generische Ziele, Arrays/Tupel refusen mit echter Diagnose · B auch
Arrays/Tupel (Swift; berührt §3.3) · C lassen.
**Empfehlung: A in v5, B danach.** Das stille Akzeptieren sofort schließen. **§12.5 greift.**
**Bricht: minor.** **Vorbedingung:** `lyrfix` existiert nicht (§1.7) — für einen *minor* mit
Warnuhr verzichtbar, für G02 nicht. **Hängt an:** G05, G38.

---

### G21 — Darf eine monomorphisierte Funktion eine Koroutine sein?

**Heute: sie darf, kompiliert, panikt — sechs Formen** (§1.4f; q07 nachgemessen). Fundort
gelesen: `InstanceTable.cs:232-236` gegen `FunctionLowerer.cs:305-310`. Spezifiziert:
`spec/08-generics.md:68` bindet `Coroutine<T>`.

**Optionen.**
- **A — reparieren: die Instanz erbt die Koroutinen-Eigenschaft.** Vorbild C# (generischer
  Iterator `IEnumerable<T> F<T>()`), Rust **`async fn`** (Zustandsmaschine je Instanziierung —
  nicht `impl Iterator`, Rust hat keine stabilen Generatoren). Preis: ein Konstruktorargument
  (`coroutineYield`) im Instanzpfad plus der Rahmenaufbau, den `ForCoroutineBody` schon macht.
- **B — refusen.** Vorbild niemand. Preis: `std.iter` ohne generische Adapter.
- **C — lassen.** Ausgeschlossen.

**Empfehlung: A, direkt — ohne die Zwischenstufe „bis dahin refusen".** Runde 2 empfahl, die
Form bis zum Fix mit `IR0001` abzulehnen. Das war falsch dimensioniert: die Ablehnung und die
Reparatur fassen **dieselbe Stelle** an (`LowerAll` muss wissen, dass es eine Koroutine senkt —
ob es dann refused oder `coroutineYield` durchreicht, ist derselbe Befund). Eine Zwischenstufe
kostet einen zweiten Patch für nichts.
**Bricht:** nein. **Konformanzfälle (G46):** **sechs**, je Form eine — generische Fn mit
`Coroutine<T>`, generische Fn mit konkretem `Coroutine<int>`, Methode eines generischen Typs
(beide Rückgabeformen), **generische Methode auf nicht-generischer Klasse (q07)**, und
`T = string` als zweite Instanz — jeder `run`, `since: 4.6.1` oder 4.7. **Hängt an:** Koroutinen-Gebiet.

---

### G22 — Varianz von Funktionstypen und `?T` — neu gestellt, weil Runde 2 die falsche Richtung gemessen hat

> **Was falsch war.** r09 gibt `(c: Circle) => …` dort hin, wo `fn(Shape) -> int` erwartet wird.
> Wäre das erlaubt, riefe `apply(g, Square {})` `g` mit einem `Square` auf — **unsicher**.
> Kontravarianz erlaubt die *Gegenrichtung*. Runde 2 führte eine unsichere Konversion als
> „kostenlose Reibung" und empfahl sie. Zurückgezogen.
> **Was auch falsch war:** *„refused, ohne dass eine Begründung existiert"* — die Regel steht in
> `spec/03-types.md:176-186` §3.7 (gelesen): Zuweisbarkeit verlangt Gleichheit, *"no variance"*,
> *"an interface value does not convert to another interface type, parent included"*.
> **Und:** *„ein Funktionswert ist ein fat pointer, Kontravarianz kostet null"* — der
> Funktionswert schon, sein **Argument** nicht: ein `Circle` ist eine nackte Referenz, ein
> `Shape` ein fat pointer (`STATUS.md:2329-2331`; `IrInst.cs:73-76` `MakeInterface`;
> `docs/Bytecode.md:854-856`, gelesen). Die *sichere* Konversion `fn(Shape)->int` →
> `fn(Circle)->int` braucht einen Thunk, der je Aufruf `mkiface` einfügt. Dasselbe Argument, mit
> dem G07 `Box<Circle>`→`Box<Shape>` ablehnt.

**Heute.** Gemessen **q01** (sichere Richtung: `fn(Shape)->int` an `fn(Circle)->int`) → `SEM0001`;
Kontrolle q01c (exakt) ✅; r09 (unsichere Richtung) → `SEM0001`; r33 `?Circle`→`?Shape` → `SEM0001`.
**Strikt invariant, spezifiziert (§3.7), und mit Repräsentationsgrund.**

**Optionen.**
- **A — invariant lassen, §3.7 unverändert.** Vorbild Rust (keine nominale Subtyping-Frage),
  Go (Funktionstypen invariant). Preis: `fn draw(f: fn(Circle) -> int)` nimmt kein `(s: Shape) =>
  …` — der Nutzer schreibt `(c: Circle) => g(c)`, **und genau das ist der Thunk, von Hand**.
- **B — sichere Kontravarianz mit Compiler-Thunk.** Vorbild Swift (Reabstraction-Thunks).
  Preis: eine Zuweisbarkeitsregel in §3.7 (Vertragsänderung), eine implizite Funktionshülle je
  Konversion, und eine Zuweisung, die still Code erzeugt — gegen „was kostet, steht da".
- **C — B plus `?T` kovariant.** Preis: `?Circle`→`?Shape` ist im Nicht-Null-Zweig dieselbe
  `mkiface`-Konversion; nicht kostenlos, nur klein.
- **D — A, schriftlich mit Grund.** §3.7 sagt „no variance"; der Repräsentationsgrund (nackte
  Referenz vs. fat pointer) gehört als Satz dazu, damit die Frage nicht in zwei Jahren als
  „Nein ohne Grund" wieder aufgemacht wird — so wie in diesem Dossier.

**Empfehlung: D.** Die Reibung bei Callbacks ist real, aber der Thunk ist eine Zeile Lambda,
und die Sprache zeigt ihn dann. B wäre ein Regel-PR gegen §3.7 mit stiller Codeerzeugung.
**Bricht:** nein. **Hängt an:** G07, Typ-Gebiet (§3.7).

---

### G23 — Wie verlässt eine generische Deklaration ein kompiliertes Paket?

**Heute.** Nirgends beantwortet (`docs/Bytecode.md`, `docs/guide/17-packaging.md`: null Treffer).
**Optionen.** A nicht benutzbar (std müsste als Quelle reisen) · B generische Rümpfe als IR
(Rust `.rlib`/MIR; Formatversion) · C als Quelle mit Header (C++-Header, `.swiftinterface`) ·
D feste Instanzmenge (C++ explicit instantiation).
**Empfehlung: C für v5, B später; A verwerfen.** **Bricht:** nein. **Hängt an:** G01, G29, G33.

---

### G24 — Ist das `::` am Typparameter dieselbe Liste wie am Typ?

**Heute.** Nein, dreifach nicht (r04, r45, r46; `spec/05-interfaces.md:28-30`;
`appendix-a:172`). Bedeutung laut `docs/Grammar.md:139` **eine**.
**Optionen.** A eine Liste, drei Regeln, `SEM0078` gedehnt · B zwei Listen dokumentiert · C
eigener Code mit `SEM0078`s Text.
**Empfehlung: A über C.** **Bricht: minor. §12.5 greift.** **Hängt an:** G10, G46.

---

### G25 — Darf ein Typparameter einen äußeren verdecken?

**Heute.** Ja, still (r20; `InstanceTable.cs:163-164`; `spec/08-generics.md:105-106`).
**Optionen.** A still · B Warnung (C# `CS0693`) · C Fehler (Rust `E0403`; widerspricht der Spec).
**Empfehlung: B.** **Bricht:** nein. **Hängt an:** G40 (Rename muss wissen, welches `T`).

---

### G26 — Wie ranken zwei generische Kandidaten gegeneinander?

**Heute — NEU und ein Ist-Befund, kein Zukunftsproblem.** `spec/04-modules.md:91-104` (gelesen):
*"By its arguments alone"*, fünf Rangregeln, keine vergleicht Constraints;
`spec/08-generics.md:93-94` Regel 7: Constraints werden *"after all binding is done"* geprüft.
Gemessen:
- **q04**: `g<T :: [A1]>` neben `g<T>` — `g(Impl {})` **und** `g(5)` sind `SEM0086`, obwohl bei
  `g(5)` nur einer anwendbar ist.
- **q04b**: `g<T :: [A1]>` vs `g<T :: [A2]>`, Argument erfüllt nur A2 → `SEM0086`.
- **q04c**: `g<T :: [A1]>` neben `g(x: string)`, `g(5)` → `SEM0028`, nicht `SEM0087`: der
  generische Kandidat wird **gewählt** und **danach** an der Constraint geprüft.

Runde 2 schrieb *„heute können sich zwei generische Kandidaten kaum nur in Constraints
unterscheiden"* — sie können, mit freien Funktionen, und sind **immer** mehrdeutig.

**Optionen.**
- **A — stärkere Constraint gewinnt** (Teilmengenrelation). Vorbild Rust-Spezialisierung
  (instabil seit 2015). Preis: sechste Rangregel, die nicht die Argumente ansieht.
- **B — Gleichstand bleibt Gleichstand — ABER erst nach der Filterung aus G35.** Vorbild C# ≥ 7.3
  vollständig: Constraints entfernen Kandidaten (Anwendbarkeit), unter den Verbleibenden kein
  Ranking (CS0121). Preis: q04-Fall mit `int` und q04b lösen sich auf; ein `T`, das **beide**
  erfüllt, bleibt `SEM0086`.
- **C — Reihenfolge entscheidet.** Ausgeschlossen.

**Empfehlung: B, mit G35 als Vorfrage — ohne G35 ist B die Bestätigung eines Bugs als Design.**
**Bricht:** nein (was heute `SEM0086` ist, löst sich teils auf). **Hängt an:** **G35**, G05, G06, G46.

---

### G27 — Wertparameter?

**Heute.** r47 → `PAR0009`. **Optionen.** A nein (Swift, Go, C#, Java) · B nur `int` (Rust
`const N`) · C `comptime`-Parameter (Zig). **Empfehlung: A, schriftlich.** **Bricht:** nein.
**Hängt an:** G16, G18, G28.

---

### G28 — Instanzbudget: worauf, wo, wer sieht es — mit neu gemessenen Zahlen

**Heute.** `MaxInstances = 20_000` (`InstanceTable.cs:45`), nie erreicht (§1.4b).
**Gemessen (§1.1, phasenweise):** 231 B und **≈ 0.3–0.5 ms** je Instanz bei großem Rumpf, davon
`verify` ≈ 0.17–0.24 ms, `emit` ≈ 0.08–0.14, `lower` ≈ 0.04–0.09.
**Behauptet (lineare Extrapolation):** 20 000 Instanzen ≈ 4.6 MB und **≈ 6–10 s** (Runde 2: 22 s —
zurückgezogen); 4 000 ≈ 0.9 MB und **≈ 1.2–2 s**. Das Argument *„eine Zahl, die kein Mensch
abwartet"* trägt bei 6–10 s nur noch halb.

**Optionen.**
- **A — Instanzzahl, kleinere Zahl.** Preis: Rumpfgröße unbekannt; und die Zahl bringt wenig,
  solange der Wächter ohne Position meldet (G18).
- **B — Typ-Schachtelungstiefe als Regel.** **Nur als §12.4-Grenze** (implementation-defined,
  ≥ 128), nicht als Vertragszahl. Fängt die divergente Form, nicht die große.
- **C — Zeit oder Bytes.** Zeit nicht deterministisch; Bytes rumpfabhängig; `type_length_limit`
  ist kein Vorbild mehr (§2 Korrektur 7).
- **D — B als Grenze, A als Netz, Zähler sichtbar** im Build-Bericht (`--verbose` zeigt heute
  *"lower 401 functions"* — die Instanzzahl steht faktisch schon da).

**Empfehlung: D — aber OHNE neue Zahl, bis `stdlib/` gemessen ist.** Runde 2 schlug 4 000 aus
einer falschen Konstante vor. Die ehrliche Reihenfolge: (1) Position und Kette in die Diagnose
(G18/G31) — das ist der eigentliche Mangel, nicht die Höhe; (2) `lyrc build --verbose` mit einer
Instanzzeile (*"lower 401 functions (399 instances)"*); (3) `stdlib/` und `examples/` messen;
(4) dann eine Zahl. **Und die Achse benennen:** der Verifier kostet je Instanz das Zwei- bis
Vierfache der Senkung — wer Compilezeit spart, spart sie dort (G44).
**Bricht:** nein. **Hängt an:** G18, G44, G01, G27, G30.

---

### G29 — Wie heißt eine Instanz im Bytecode, und ist das Teil des Formats?

**Heute.** Name reist mit (`main.once<int>`, r12c/q07); gebaut in `InstanceTable.cs:140-145` aus
`Qualify` + `TypeFacts.Display`; `docs/Bytecode.md` schweigt; `nameIndex uleb128` ohne Grenze.
**Optionen.** A Namensbildung ins Format (Rust `v0`, Swift) · B reines Debug-Artefakt · C lassen.
**Empfehlung: A, minimal** — drei Sätze in §13: qualifizierter Name + Typargumente; modulweit
eindeutig; Länge durch die §12.4-Schachtelungsgrenze gedeckelt (**nicht** durch eine
§8.1-Zahl — Korrektur gegenüber Runde 2). **Bricht:** nein. **Hängt an:** G18, G23, G33.

---

### G30 — Instanz-Deduplikation nach Layout?

**Heute.** Nein: r22/r23 (8 layoutgleiche Structs, 580 B), r41/r42 (92 351 B); **q12d/q12e**:
`Box<Meters>` und `Box<int>` sind zwei Instanzen (+56 B) trotz identischem Layout (§1.2).
Kritik n11/n11c (nicht nachgemessen): zwei Aufrufstellen derselben Instanz werden dedupliziert
(`_byKey`, `InstanceTable.cs:49`, gelesen) — die Deduplikation nach **Name** existiert.

**Optionen.** A nicht deduplizieren · B nach Layout-Signatur (Go GC-Shape, C# kanonischer
Code — rein statisch) · C nur Rümpfe, die ihre Typparameter nicht nennen.
**Empfehlung: C in v5, B als Messfrage — aber C setzt G39 voraus.** Runde 2 hat übersehen: „ein
Rumpf, der `T` nicht nennt" **ist** der Phantom-Fall (q08), und ob der erlaubt, gewarnt oder
verboten ist, ist nirgends entschieden. C dedupliziert dann genau die Instanzen, die G39
vielleicht verbietet. Reihenfolge: G39 vor G30-C. Und **G45**: `Box<Meters>`/`Box<int>` teilen
das Layout, aber nicht die Identität — B muss sagen, ob der Rumpf geteilt wird und der Name
nicht.
**Bricht:** nein. **Hängt an:** G01, G29, **G39**, **G45**.

---

### G31 — Trägt eine Diagnose die Instanziierungskette?

**Heute.** Nein: r22a, r30 zeigen auf die Deklaration; q02 zeigt **gar keine** Position.
**Optionen.** A Notenstapel (Rust `required by …`, gekappt bei 5) · B an die Aufrufstelle · C lassen.
**Empfehlung: A, als §12-Element.** **Bricht:** nein. **Hängt an:** G18, G21, G24, §1.4a.

---

### G32 — Mehrfachkonformanz gegen `Self`

**Heute.** Feature (r32, `spec/05-interfaces.md:20-23`). **Optionen.** A kassieren · B `Self` für
`Hashable`/`Ordered`, Parameter für `Equatable` · C drittes Interface. **Empfehlung: B**; falls A:
Bruchliste, Spec-Zeile, **`lyrfix` (das nicht existiert, §1.7) meldet `Equatable<X>` mit `X ≠
Self` als nicht migrierbar**. **Bricht: major.** **Hängt an:** G02, G16, G04, G47.

---

### G33 — `.lyrbc`-Kompatibilität, wenn G02 Interface-Signaturen ändert

**Heute.** Nicht gestellt (`docs/Bytecode.md:188-192`). **Behauptet:** kein Formatbruch, nur
Inhalt — zu messen, nicht zu behaupten. **Optionen.** A Formatversion, 4.x refusen · B mischbar
(Rule 2) · C Mischung auf Modulebene refused. **Empfehlung: A, vor G02, nach einer Messung.**
**Bricht: major.** **Hängt an:** G02, G32, G23, G29.

---

### G34 — Generics über die Host-Grenze

**Heute.** Lyric → Host: `SEM0099` (r34b). Gegenrichtung: nirgends (`docs/guide/14-embedding.md`,
`spec/11-stdlib-contract.md`: null Treffer). **Optionen.** A Host sieht nur Instanzen · B Host
fordert Instanziierung an (Compiler in der Laufzeit) · C Instanzliste im Header.
**Empfehlung: A, in §11 geschrieben.** **Bricht:** nein. **Hängt an:** G01, G23.

---

### G35 — Sind Constraints Teil der Anwendbarkeit eines Überladungskandidaten? *(neu)*

**Heute: nein — Wahl zuerst, Constraint danach.** Gelesen `spec/04-modules.md:91` (*"By its
arguments alone"*), `spec/08-generics.md:93-94` (Regel 7, *"after all binding is done"*).
Gemessen q04 (`g(5)` gegen `g<T :: [A1]>`/`g<T>` → `SEM0086`, obwohl `int` A1 nicht erfüllt),
q04b (`SEM0086`, obwohl nur A2 passt), q04c (`SEM0028` statt `SEM0087`: gewählt, dann verworfen).
Kontrolle n02 (Kritik): ohne Überladung filtert die Constraint (`SEM0028`).

**Optionen.**
- **A — ja: ein Kandidat, dessen Constraints das inferierte Mapping nicht erfüllt, ist nicht
  anwendbar.** Vorbild C# ≥ 7.3 („improved overload candidates"), Rust (Trait-Bounds sind Teil
  der Auflösung). Preis: die Constraint-Prüfung wandert **vor** die Rangregeln, also einmal je
  Kandidat statt einmal je Aufruf; Regel 7 und §4.3a werden umformuliert; `SEM0028` bleibt für
  den Fall, dass *kein* Kandidat passt (dann als `SEM0087` mit dem Grund je Kandidat).
- **B — nein, wie heute; §4.3a bekommt einen Satz, der es sagt.** Vorbild: C# < 7.3. Preis: q04b
  bleibt für immer mehrdeutig; G06 (bedingte Methoden) wird unbrauchbar, weil zwei
  `extend`-Blöcke mit verschiedenen Constraints immer kollidieren.
- **C — A nur für `extend`-Kandidaten, nicht für freie Funktionen.** Preis: zwei Regeln.

**Empfehlung: A.** q04b ist ein Bug, kein Design — ein Kandidat, der das Argument nicht nehmen
kann, darf keinen Gleichstand erzeugen. Konkrete Erwartung: q04b → 2; q04 `g(5)` → 2, `g(Impl)`
→ bleibt `SEM0086` (beide anwendbar, Rangregeln gleich — das ist G26); q04c `g(5)` → `SEM0087`
mit *"'g<T :: [A1]>': int does not satisfy A1; 'g(string)': int is not string"*.
**Bricht: minor** (Programme, die heute `SEM0086` melden, kompilieren; kein laufendes Programm
ändert seine Bedeutung — aber Regel-PR, `since:`-Gate). **Konformanzfälle (G46):**
`unmet_constraint_removes_the_candidate`, `constraint_is_part_of_applicability`.
**Hängt an:** G26, G06, G43.

---

### G36 — Darf `throws E` einen Typparameter nennen, und wird `E` an der Aufrufstelle substituiert? *(neu)*

**Heute: halb.** Gemessen **q05**: `fn risky<T, E :: [Throwable]>(…): T throws E` wird
**deklariert und akzeptiert**; `risky<int, Boom>(…)` in `try … catch (b: Boom)` → `SEM0034: call
to 'risky' may throw 'Throwable', which nothing handles`. **q05d**: `catch (t: Throwable)`
entlastet (Exit 9). Kontrolle **q05c**: konkretes `throws Boom` in `risky<T>` → Exit 2 ✅.
Kritik n15 (nicht nachgemessen): ohne Constraint → `SEM0030: 'E' in 'throws' does not implement
'Throwable'` — die Deklarationsseite ist also geprüft, die Aufrufseite substituiert **nicht**.
Die Interaktionsmatrix aus Runde 2 hatte `throws` mit r50 grün; r50 prüft nur `throws Boom`.
Gelesen `spec/09-errors.md:42-44` §9.2: eine Klausel nennt *"a specific throwable type, or
`throws` bare"* — der Typparameter ist in der Spec **nicht vorgesehen**; der Compiler akzeptiert
ihn trotzdem an der Deklaration. Spec und Compiler divergieren, ohne dass einer von beiden die
Frage entschieden hat.

**Optionen.**
- **A — ja, mit Substitution.** `risky<int, Boom>` wirft `Boom`; `catch (b: Boom)` entlastet.
  Vorbild Swift typed throws `throws(E)` mit generischem `E` (SE-0413), Rust `Result<T, E>`.
  Preis: die `throws`-Klausel ist Teil der Signatur, die bei der Instanziierung substituiert
  wird — heute offenbar nur der Rückgabetyp; Inferenz von `E` aus einem `throw e`-Argument teilt
  G11s Reihenfolgefrage.
- **B — nein: `throws E` ist ein Fehler an der Deklaration** mit eigenem Code (*"a `throws`
  clause names a concrete type"*). Preis: generische Adapter, die den Fehlertyp ihres Callbacks
  weiterreichen (`map<T, U, E>(f: fn(T) -> U throws E)`), sind unschreibbar — und das ist die
  Grundform jeder `std.iter`-Kette mit Fehlern.
- **C — heute: akzeptieren und auf die Constraint weiten.** Preis: die Deklaration verspricht
  `E`, der Aufruf bekommt `Throwable` — ein stiller Bedeutungswechsel, den keine Diagnose nennt.

**Empfehlung: A.** C ist der Ist-Zustand und ein stiller Fehler (Programm kompiliert mit
`catch Throwable`, aber mit dem falschen Vertrag). Bis A steht, ist **B als Zwischenform
akzeptabel**, weil sie ehrlich ist — anders als bei G21 fassen Ablehnung und Fix hier
verschiedene Stellen an (Sema-Deklarationsprüfung vs. Substitution im Aufruf).
**Bricht:** nein (A: was heute `SEM0034` ist, kompiliert; B: minor). **Konformanzfall (G46):**
`throws_substitutes_its_type_parameter` (`run`, `since:` mit A). **Hängt an:** G11,
Fehlerbehandlungs-Gebiet (typed throws Stufe 2).

---

### G37 — Ist `void` ein gültiges Typargument? *(neu)*

**Heute: Sema ja, Verifier nein — `CLI0020`.** Gemessen **q09**: `let _: ?Box<void> = null;` →
`error[LYR-CLI0020]: ir-verifier (after the lowering): malformed IR … field #0 'v' is void — this
is a defect in the compiler`. Kontrolle q09c `?Box<int>` ✅. Nach `spec/12-diagnostics.md:70-72`
ist `CLI0020` *"the compiler being wrong"* — und das stimmt hier: die Sema hätte es
entscheiden müssen.

**Optionen.**
- **A — nein: `void` ist kein Typargument**, `SEM`-Code am Typargument (*"'void' is not a value
  type and cannot be a type argument"*). Vorbild C# (`void` ist kein Typargument; `Task` vs
  `Task<T>` als Folge), Go (kein `void`). Preis: `Result<void, E>` für wertlose Operationen
  braucht einen Unit-Typ oder ein zweites `Result`.
- **B — ja: `void` ist ein Unit-Typ mit Layout 0.** Vorbild Rust `()`, Swift `Void = ()`. Preis:
  ein Feld vom Typ `void`, ein Array `void[]`, ein `?void` — jede Repräsentationsfrage der VM
  neu; `docs/Bytecode.md` kennt `void` nur als Rückgabe (*behauptet*, nicht durchsucht).
- **C — nein, aber mit `unit`-Struct in `std.core`** (`struct Unit {}`) als der Typ, den man
  meint. Preis: ein Name mehr; kein Formatbruch.

**Empfehlung: A jetzt (Konformanz-Fix, kein Regel-PR: `CLI0020` darf nicht sein), C als
Bibliotheksantwort.** B ist ein VM-Thema und gehört nicht in dieses Gebiet.
**Bricht:** nein (heute `CLI0020`). **Konformanzfall (G46):** `void_is_not_a_type_argument`
(`since:` mit dem Fix). **Hängt an:** Typ-Gebiet, Bytecode-Gebiet (falls B).

---

### G38 — Was bedeuten `extend<T> List<T>` und `extend Bag`? *(neu)*

**Heute.** **q06b** `extend<T> Bag<T> { … }` → `PAR0011` + 5 Folgefehler (Parser kennt keine
Typparameter am `extend`). **q06** `extend Bag { … }` auf `class Bag<T :: [A1]>` → passiert
Sema, fällt in der Senkung: `IR0001: generic type 'Bag' needs 1 type argument(s), got 0` an
**3:1 (Klassendeklaration)**. n09b `extend Bag<int>` → `SEM0047` am `extend` ✅ (richtige Stelle,
richtiger Code). Der Grundfall der generischen Erweiterung fehlt also nicht nur — er ist an
einer Form falsch abgelehnt.

**Optionen.**
- **A — `extend<T> List<T> { … }` ist die unbedingte generische Erweiterung**, dieselbe Grammatik
  wie G05 ohne Constraint; `extend Bag` (ohne Argumente) ist `SEM0047` **am `extend`** mit dem
  Satz *"'Bag' is generic — write `extend<T> Bag<T>`"*. Vorbild Rust `impl<T> Vec<T>`, Swift
  `extension Array`. Preis: keiner über G05 hinaus.
- **B — `extend Bag` bedeutet `extend<T> Bag<T>`** (Kurzform). Vorbild Swift (`extension Array`
  ohne `<Element>`). Preis: zwei Schreibweisen für dieselbe Erweiterung (Rule 2), und die
  Kurzform hat keinen Namen für `T` im Rumpf.
- **C — lassen.** Ausgeschlossen: `IR0001` für ungültiges Lyric an der falschen Position
  verstößt gegen §12.1.

**Empfehlung: A.** Das `SEM0047`-Ziel am `extend` ist sofort zu reparieren (Konformanz-Fix, die
Regel existiert: *"extend target must be a plain named type"*); die Grammatik kommt mit G05.
**Bricht:** nein. **Konformanzfall (G46):** `extend_of_a_generic_type_names_its_parameters`
(refused-Fall heute; `run`-Fall mit G05). **Hängt an:** G05, G20.

---

### G39 — Darf ein Typparameter ungenutzt sein (Phantom-Typen)? *(neu)*

**Heute: ja, still.** Gemessen **q08**: `class Tagged<T> { n: int }` und `fn f<T>(n: int)`
kompilieren; `Tagged<string>`, `Tagged<int>`, `f<bool>`, `f<string>` laufen (Exit 3), keine
Warnung. Der ungenutzte **Funktions**-Parameter ist nur explizit instanziierbar (sonst
`SEM0060`, Regel 6) — also ist er faktisch immer sichtbar; der ungenutzte **Typ**-Parameter
ist es nicht.

**Optionen.**
- **A — erlaubt, still.** Vorbild C#, Swift (Phantom-Typen sind ein Idiom: `Id<User>`,
  `Meters<Unit>`). Preis: G30-C dedupliziert dann genau diese Instanzen — und ein Nutzer, der
  `Tagged<A>` und `Tagged<B>` als *verschiedene* Typen will, bekommt sie (Sema), nur mit einem
  geteilten Rumpf (Lowering) — was in Ordnung ist, solange G29 den Namen nicht teilt.
- **B — verboten an Typen, erlaubt an Funktionen.** Vorbild Rust `E0392` + `PhantomData`. Preis:
  das Idiom `Id<User>` braucht ein `PhantomData`-Äquivalent, das Lyric nicht hat; und Rusts
  Grund (Varianz/Drop-Check) existiert in Lyric nicht.
- **C — Warnung.** Preis: eine Warnung auf ein Idiom, das der Nutzer wollte.

**Empfehlung: A, ausdrücklich in §8 geschrieben** — *ein Typparameter darf ungenutzt sein; zwei
Instanzen mit verschiedenen Argumenten sind verschiedene Typen, auch wenn ihr Rumpf derselbe
ist.* Damit ist G30-C erlaubt und definiert (Rumpf geteilt, Typ nicht). B löst ein Problem, das
Lyric nicht hat.
**Bricht:** nein. **Hängt an:** G30, G45 (dieselbe Frage bei opaque).

---

### G40 — Was tun die Werkzeuge mit Instanzen: `lyrfmt`, LSP-Hover, Rename, `lyrdbg`? *(neu)*

**Heute.**
- **`lyrfmt`** (gemessen q14/q14b): schreibt `> >` zu `>>` und erzeugt nicht kompilierbaren
  Code. Ansonsten druckt es Typargumentlisten aus dem AST (`AstFormatter.cs:1047-1051`).
- **LSP-Hover** (gelesen `src/Lyric.Lsp/Analysis/HoverProvider.cs:84-90`): *"at a use site of a
  generic it is SUBSTITUTED: the parameter reads `int` where the declaration says `T`"* — Hover
  zeigt die **Instanz** am Aufruf, die **Vorlage** an der Deklaration. Nicht gemessen.
- **Rename** (gelesen `Lyric.Lsp:216-220`, Prepare/Rename existieren): was ein Rename eines
  verdeckten `T` (G25) trifft, ist **nicht gemessen** — *behauptet:* symbolbasiert, also das
  innere oder äußere je nach Cursor, ohne Warnung.
- **`lyrdbg`** (gelesen `src/Lyric.Dap/DapServer.cs:474-477`): Breakpoints werden **nach Zeile**
  gebunden; *behauptet:* ein Breakpoint auf `once<T>` trifft jede Instanz, weil jede dieselbe
  Zeile trägt (Backtrace q07: `main.once<int> (…:2)`).

**Optionen.**
- **A — Instanzen sind für Werkzeuge unsichtbar** (Hover/Breakpoint auf der Vorlage, Rename
  auf dem Symbol), Backtrace zeigt den Instanznamen. Vorbild C# (Debugger zeigt `F<int>` im
  Stack, Breakpoint auf der Vorlage), Rust (`rust-analyzer` Hover zeigt substituiert). Preis:
  „Breakpoint nur in `once<string>`" gibt es nicht — Bedingung statt Instanz.
- **B — Instanzen adressierbar** (Breakpoint auf `once<string>`, Hover mit Instanzliste). Preis:
  DAP-Erweiterung, Namensbildung wird Vertrag (G29).

**Empfehlung: A, als Satz im Werkzeug-Kapitel — plus der Formatter-Roundtrip-Test aus G13.**
**Bricht:** nein. **Hängt an:** G13, G25, G29; Editor-Gebiet.

---

### G41 — §6.3-Fehlalarm: `a<b>(c)` mit drei `int`s *(neu)*

**Heute.** Gemessen **q10**: `let r = a<b>(c);` → `SEM0013: 'int' is not callable` — der Parser
liest Typargumente, sobald `<…>` von `(` gefolgt ist (`Parser.cs:250-289`, `LooksLikeCallTypeArguments`).
Kontrolle q10c `a < b` ✅. Die Diagnose erklärt nichts über die Ursache.

**Optionen.**
- **A — bleiben, Diagnose reparieren:** *"read as a call with type arguments `a<b>(c)`; write
  `(a < b) > (c)` for a comparison"*. Vorbild C# (dieselbe Regel, dieselbe Falle, gute Diagnose
  CS0119-Familie). Preis: die Sprache hat einen Fehlalarm, der einen Satz braucht.
- **B — Rückfall auf den Vergleich, wenn `a` kein generisches Symbol ist.** Vorbild: niemand
  wörtlich (Rust vermeidet die Frage per Turbofish). Preis: der Parser braucht Symbolwissen,
  oder die Sema muss den AST umbauen — beides eine Schicht zu tief.
- **C — Turbofish.** Ausgeschlossen (G13).

**Empfehlung: A.** `(a < b) > c` ist ohnehin `bool > int` und selbst ein Typfehler; der reale
Fall ist selten und die Diagnose ist die ganze Reparatur.
**Bricht:** nein. **Hängt an:** G13.

---

### G42 — Default-Methoden-Rümpfe generischer Interfaces unter Vtable-Dispatch *(neu)*

**Heute.** Gemessen **q13**: `interface Src<T> { fn v(): T; fn pair(): T[] { … } }`, zwei
Konformierer (`Src<int>`, `Src<string>`), Aufruf über **Interface-Werte** → `42 2`. Der
Default-Rumpf läuft also je Interface-**Instanz** korrekt substituiert (mindestens einer je
`T`). Ob er je **Konformierer** oder je Interface-Instanz gesenkt wird, ist nicht gemessen
(*behauptet:* je Konformierer, weil `this` typisiert ist — der Backtrace-Name wäre die Probe).
r63 prüft nur den Constraint-Pfad.

**Warum das mit G02/G03 zur Frage wird.** Ein Default-Rumpf, der `Self` (G02) oder
`T.parse(s)` (G03) benutzt, hat **keinen** Konformierer als Empfänger, wenn er über einen
Interface-Wert aufgerufen wird — der Wert kennt nur den Slot. Läuft dann ein monomorphisierter
Rumpf je Konformierer (dann funktioniert `Self`, aber es gibt so viele Rümpfe wie Konformierer)
oder ein geteilter (dann ist `Self` im Rumpf verboten)?

**Optionen.**
- **A — Default-Rumpf je Konformierer**, `Self` und statische Anforderungen erlaubt. Vorbild
  Swift (Witness je Konformierer), Rust (Default-Methoden monomorphisiert je `impl`). Preis:
  Codegröße × Konformierer; heute schon vermutlich so (*behauptet*).
- **B — geteilter Rumpf, `Self`/statische Anforderungen im Default-Rumpf verboten**, wenn das
  Interface als Wert benutzbar sein soll. Vorbild C# (`static abstract` in Default-Implementation
  nicht über den Wert aufrufbar). Preis: eine Regel, welche Default-Rümpfe „wert-tauglich" sind.

**Empfehlung: erst messen (Backtrace-Name eines Default-Rumpfs), dann A**, weil A das ist, was
die Vtable-Zeile je Konformierer ohnehin nahelegt. G02 und G03 sind ohne diese Antwort nicht
lieferbar.
**Bricht:** nein. **Hängt an:** G02, G03.

---

### G43 — Nimmt der erwartete Typ an der WAHL zwischen Überladungen teil? *(neu)*

**Heute: nein.** Gemessen **q11**: `fn make<T>(x: T): T` neben `fn make(): int`, `let s: string
= make();` → die konkrete wird gewählt (§4.3a *"by its arguments alone"*: nur sie nimmt null
Argumente) und dann `SEM0001: cannot assign 'int' to 'string'`. Kontrolle q11c ✅. Gemessen
**q11b** (nur generisch, `make<T>(): T[]`): `SEM0060` — Regel 6, kein Kontext.

**Optionen.**
- **A — nein, auch mit G11-C2:** der Kontext bindet erst **nach** der Wahl den offenen
  Parameter des gewählten Kandidaten. Vorbild C# (Rückgabetyp ist nie Teil der Auflösung).
  Preis: `fn make(): int` und `fn make<T>(): T` nebeneinander sind praktisch unbenutzbar für
  `T ≠ int` — die konkrete gewinnt nach Regel 2 immer.
- **B — der Kontext ist ein Argument der Auflösung** (Rückgabetyp zählt). Vorbild Rust
  (Trait-Auflösung über den erwarteten Typ, `collect`), Haskell. Preis: §4.3a verliert *"by its
  arguments alone"*; Regel 3 („erste Bindung") bekommt einen zweiten Anfang.

**Empfehlung: A, ausdrücklich als Satz zu G11-C2** — *der Kontext wählt nicht, er bindet.* B
ist die Rust-Inferenz und die deutlich teurere; G11-B hat sie abgelehnt, G43 darf sie nicht
durch die Hintertür holen.
**Bricht:** nein. **Hängt an:** G11, G35.

---

### G44 — Welche Achse kostet wirklich: Verifier, Impls-Sektion, Funktionstabelle? *(neu)*

**Heute, gemessen (§1.1):** je Instanz `verify` ≈ 0.17–0.24 ms, `emit` ≈ 0.08–0.14, `lower`
≈ 0.04–0.09, `check` ≈ 0.02. **Der Verifier ist die teuerste Achse** — je Instanz das Zwei- bis
Vierfache der Senkung. **Nicht gemessen:** wie die Impls-Sektion (Vtable-Zeilen je Instanz eines
generischen Interface — `Iterator<int>` vs `Iterator<string>`), die Ladezeit der VM und die
Funktionstabelle mit der Instanzzahl wachsen. *Behauptet:* Impls wächst mit (Konformierer ×
Interface-Instanz), nicht mit der Funktionsinstanzzahl; die Funktionstabelle linear.

**Optionen.**
- **A — Verifier je Instanz beibehalten.** Preis: Compilezeit wächst mit der Instanzzahl
  hauptsächlich dort.
- **B — Verifier einmal je Vorlage, plus Layout-Prüfung je Instanz.** Vorbild: Rusts MIR-Borrowck
  läuft auf der Vorlage, nicht je Instanz (Instanzen werden nur codegeneriert). Preis: der
  Verifier prüft gesenkten IR, und eine Instanz ist gesenkter IR — die Vorlage existiert in
  dieser Form nicht; B setzt voraus, dass der Verifier substitutionsinvariant ist, was zu
  beweisen wäre.
- **C — Verifier nur in Debug-Builds** (Build v2 hat Profile). Preis: ein Release-Build ohne
  Netz gegen Compiler-Fehler — genau die Sorte, die q09 gefangen hat (`CLI0020`).

**Empfehlung: A für v5, mit einer Messreihe vor jeder Änderung** (Impls, Ladezeit,
Funktionstabelle — drei Proben mit `--verbose` und einem Loader-Timing). B ist eine
Compiler-Architekturfrage außerhalb dieses Gebiets; C ist gefährlich.
**Bricht:** nein. **Hängt an:** G28, G01; Bytecode-/VM-Gebiet.

---

### G45 — Opaque-Typen als Typargumente *(neu)*

**Heute.** Gemessen **q12c**: `Box<Meters>` kompiliert, `b.v as int` → 3. **q12**: `Box<Meters> as
Box<int>` → `SEM0006` (Cast refused, `Into`-Route genannt) — konsistent mit
`docs/Grammar.md:187-188` (*"an explicit `as` to exactly the underlying type and back is the one
crossing"*): `Box<int>` ist nicht der Grundtyp von `Box<Meters>`. **q12d/q12e**: `Box<Meters>` und
`Box<int>` sind **zwei Instanzen** (+56 B) mit identischem Layout.

**Optionen.**
- **A — wie heute:** zwei Instanzen, kein Crossing auf Instanzebene. Vorbild Rust (Newtype in
  `Vec<Meters>` ist ein anderer Typ als `Vec<i32>`; kein Cast). Preis: `Box<Meters>` →
  `Box<int>` verlangt `map(v => v as int)` — bei Wertsemantik korrekt, bei Referenzsemantik eine
  Kopie.
- **B — Instanz-Crossing erlauben**, `Box<Meters> as Box<int>`, weil das Layout identisch ist.
  Preis: `as` bekommt eine zweite Bedeutung (strukturell statt „exakt der Grundtyp"), und ein
  `Box<Meters>`-Wert wäre danach über zwei Namen erreichbar — aliasing über die opaque Grenze,
  die genau das verhindern soll.
- **C — A, plus G30-B teilt den Rumpf** (ein gesenkter `get`, zwei Typen). Preis: G29 muss den
  Namen eindeutig halten.

**Empfehlung: A, mit C als Deduplikationsziel.** Die Grenze ist gemessen und richtig: opaque
ist Identität, und Identität ist nicht layoutgleich. Gehört als Satz in §3 (opaque) und §8.
**Bricht:** nein. **Hängt an:** G30, G39, Typ-Gebiet (opaque-über-opaque-Posten).

---

### G46 — Welche `since:`-gegateten Konformanzfälle begleiten die Empfehlungen? *(neu)*

**Heute.** `lyric-spec/conformance/cases/08-generics/` enthält **16** Fälle (gelesen, Liste:
`a_method_may_be_generic_on_top_of_its_type` (since 4.6.0), `polymorphic_recursion_is_refused`,
`monomorphized_instances_are_distinct`, `generic_function_is_not_a_value`, …). Kein Fall deckt
Koroutinen, `throws E`, `void`, Constraints in der Überladung, `>>`, Phantome, opaque als
Typargument. Runde 2 nannte einen einzigen Fall (G21) — und dem fehlte die sechste Form.

**Die Liste, je Empfehlung (Form: `//! run|refused`, `//! since:`):**

| Frage | Fall | Art | `since:` | heute |
|---|---|---|---|---|
| G10/G24 | `constraint_must_name_an_interface`, `constraint_list_does_not_repeat`, `constraint_chain_is_acyclic` | refused | 5.0.0 (Warnung 4.7) | grün-falsch |
| G13 | `nested_type_arguments_close_with_shr` (`take<Box<int>>(x)`) + Formatter-Roundtrip | run | mit Fix | rot |
| G18 | `divergent_instance_chain_is_reported` (Klassen-Methoden-Form q15) | refused, **mit Position** | 4.6.0 (§12.4 gilt) | **rot (Absturz)** |
| G18 | `interface_member_chain_is_reported_with_position` (q02) | refused mit Position | mit Fix | halb (ohne Position) |
| G21 | sechs Fälle (§G21) | run | mit Fix | **rot (Panik)** |
| G26/G35 | `unmet_constraint_removes_the_candidate` (q04b → 2), `two_applicable_generics_tie` (q04 `g(Impl)` → SEM0086) | run / refused | mit Regel-PR | rot / grün |
| G36 | `throws_substitutes_its_type_parameter` (q05 → Exit 2) | run | mit Fix | rot |
| G37 | `void_is_not_a_type_argument` | refused (SEM) | mit Fix | rot (CLI0020) |
| G38 | `extend_of_a_generic_type_names_its_parameters` (q06 → SEM0047 am extend) | refused | mit Fix | rot (IR0001, falsche Stelle) |
| G39 | `unused_type_parameter_is_a_distinct_type` (q08) | run | 5.0.0 | grün |
| G45 | `opaque_argument_makes_a_distinct_instance` (q12 refused, q12c run) | beide | 5.0.0 | grün |
| G03/G42 | `static_requirement_is_reached_through_a_constraint`, `…_not_through_a_value` | run / refused | mit G03 | — |

**Empfehlung:** die Fälle, die **heute rot** sind und deren Regel **schon gilt** (G18-Absturz
unter §12.4, G21 unter §8.3, G37 unter §12.1), gehören **vor** jede v5-Arbeit in die Suite —
sie sind Konformanzverstöße von 4.6, keine Designfragen. Die Test-Disziplin des Projekts
(Erwartung vor dem Lauf, rot **und** grün prüfen) gilt für jeden.
**Bricht:** nein. **Hängt an:** alle genannten.

---

### G47 — Deklarationsseitige bedingte Konformanz vs. `extend`-Form: eine Semantik, und was bei beiden? *(neu)*

**Heute.** Deklarationsseitig gemessen (q03, q03b): Unifikation an der Instanz, `SEM0028` mit
substituiertem Constraint. `extend`-Form: nicht parsebar (p09, q06b). Ob ein Typ **beides**
deklarieren darf — `struct Pair<T :: [Equatable<T>]> :: [Equatable<Pair<T>>]` **und**
`extend<T :: [Ordered<T>]> Pair<T> :: [Equatable<Pair<T>>]` — ist unentscheidbar, solange die
zweite Form nicht existiert; `spec/05-interfaces.md:28-30` (§5.1, *"A list may not repeat
itself"*, `SEM0078`) gilt heute für die Liste **eines** Typs bzw. **eines** `extend`.

**Optionen.**
- **A — eine Konformanz je (Typ, Interface-Instanz), egal woher:** die zweite Deklaration ist
  `SEM0078` (Wiederholung), auch mit anderer Bedingung. Vorbild Rust (Kohärenz: ein `impl` je
  Trait-Typ-Paar, überlappende `impl`s sind E0119 — auch mit verschiedenen Bounds, solange sie
  überlappen können). Preis: `Pair<T>` kann nicht „`Equatable`, wenn `T` `Equatable`" **und**
  „`Equatable` auf anderem Weg, wenn `T` `Ordered`" sein — was auch niemand braucht.
- **B — mehrere Konformanzen mit disjunkten Bedingungen**, Auflösung an der Instanz. Vorbild
  Rusts Spezialisierung (instabil). Preis: Disjunktheit ist mit Interface-Ketten nicht trivial
  entscheidbar; G26-A durch die Hintertür.
- **C — die `extend`-Form darf nur ergänzen, was der Typ nicht selbst deklariert** (A, mit
  Diagnose, die auf die Typdeklaration zeigt).

**Empfehlung: A (= C mit guter Diagnose), und die `extend`-Form übernimmt q03bs Diagnoseform
wörtlich** — Unifikation an der Instanz, `SEM0028` mit substituiertem Constraint, Position an
der Instanz plus Kette (G31). Zwei Semantiken für „bedingt konform" wären Rule 2.
**Bricht:** nein. **Hängt an:** G05, G32, G26.

---

## 4. Was wir übernehmen sollten

| Von | Was | Wohin |
|---|---|---|
| Swift / Rust | `Self` im Interface-Körper | G02 (`Hashable`, `Ordered`); `Equatable` Sonderfall (G32) |
| Rust | `PartialEq<Rhs = Self>` — Default statt Streichung | G32, Preis von G16-A |
| C# 11 (+ Swift, korrekt eingeordnet) | statische Interface-Member über Constraint, nie über Wert | G03, G19, G42 — **entlang Prototyp 13** |
| **C# ≥ 7.3, vollständig** | Constraints filtern Kandidaten **vor** dem Ranking; danach kein Ranking | **G35**, G26 |
| C# 3.0 | constrained extension methods als `extend`-nahes Vorbild | G06 |
| Rust | `impl<T: Bound> Trait for Type<T>`, Kohärenz, keine Spezialisierung | G05, **G47** — und **der Vorläufer existiert schon (q03)** |
| Rust | `_` in Typargumentlisten | G12 |
| Rust `recursion_limit` (128) **und §12.4 (≥ 128)** | Grenze als Implementierungsgrenze mit Diagnose, **nicht** als Vertragszahl | G18, G28 |
| Rust | Notenstapel `required by …` | G31 |
| Rust `async fn` (nicht `impl Iterator`) | Zustandsmaschine je Instanziierung | G21 |
| Swift typed throws `throws(E)` | generisches `E`, substituiert am Aufruf | **G36** |
| C# | `void` ist kein Typargument | **G37** |
| C# / Swift | Phantom-Typparameter erlaubt, Instanzen verschieden | **G39** |
| C# / rust-analyzer | Werkzeuge zeigen substituiert am Aufruf, Vorlage an der Deklaration | **G40** |
| Rust `.rlib` / C++-Header | generische Rümpfe reisen mit dem Paket | G23 |
| Rust `v0` / Swift | Instanznamen spezifiziert | G29 |
| C# `CS0693` | Warnung für verdeckten Typparameter | G25 |
| Go / C# | Layout-geteilter Code, rein statisch | G30 (nach G39) |
| C++11 | `>>` spalten — **im Parser und im Formatter-Roundtrip** | G13 |
| Rust | parametrisierte Aliase | G15 |
| **Niemandem** | Varianzannotationen, HKT, Spezialisierung, comptime-Duck-Typing, kovariante Arrays mit Laufzeitprüfung, Wertparameter, **Funktions-Kontravarianz mit stillem Thunk** | G07, G08, G22, G27 |

**Reihenfolge (aus den Abhängigkeiten, gegenüber Runde 2 verschoben):**

0. **Konformanzverstöße von 4.6 zuerst, als Fixes ohne Regel-PR:** G21 (Koroutine — ein
   Konstruktorargument), G18-Schritt 1 (q15-Absturz → Diagnose mit Position, §12.4), G37
   (`CLI0020` → SEM), G38 (`SEM0047` am `extend`). Jeder mit seinem `since:`-Fall (G46).
1. **G35 als Regel-PR** (Constraints = Anwendbarkeit) — Vorbedingung von G26, G06 und damit der
   ganzen Bibliotheksschiene.
2. G13 (`>>`, Parser + Formatter-Roundtrip), G10/G24 (Regel-PR + Warnuhr), §1.4e.
3. G18-Schritte 2–3 (sichtbarer Zähler, dann tiefe Substitution), G28 ohne neue Zahl, G31.
4. G36 (`throws E`) mit dem Fehlerbehandlungs-Gebiet.
5. **G02 + G03 + G42** — mit G33 gemessen, G32 entschieden, **`lyrfix` gebaut** (§1.7).
6. G05 + G38 + G06 + G47 + G26 — bedingte Konformanz/Methoden auf dem Vorläufer q03.
7. G11 (C2) + G43, G12, G15, G17, G25, G39, G40, G41, G45 — additiv.
8. G19 auf G02/G03. G23, G29, G34, G44 mit der Paket-/Format-Runde.
9. Ausdrücklich nicht in v5: G04, G07, G08, G09-C, G16, G22-B, G27, G30-B.

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2:**
- G02-D nimmt eine benennbare Doppelung in Kauf (`Self` vs `Equatable<X>` für `X ≠ Self`).
- G04, G09-B, G19-B bräuchten ADRs — nicht in v5.
- G24 löst eine bestehende Rule-2-Verletzung auf (`::` zweimal verschieden geprüft).
- G33-B wäre eine neue.
- **G38-B** (Kurzform `extend Bag`) wäre eine neue — abgelehnt.
- **G47-B** (zwei Konformanzen mit Bedingungen) wäre eine neue — abgelehnt.
- **G22-B** (stiller Thunk) wäre ein zweiter Weg, eine Funktion anzupassen, neben dem Lambda —
  abgelehnt.

**Mit anderen Gebieten:**
- **Koroutinen (G21):** mitzeichnen; der Fix ist ein Konstruktorargument im Instanzpfad.
- **Fehlerbehandlung (G36):** `throws E` mit Typparameter ist heute halb — Deklaration geprüft,
  Aufruf nicht substituiert. Typed throws Stufe 2 muss die Substitution mitliefern; die
  Interaktion war in Runde 2 fälschlich grün.
- **Typen/§3.7 (G22, G45):** „no variance" ist Vertrag; der Repräsentationsgrund gehört als
  Satz dazu. Opaque als Typargument: Instanz-Identität ist keine Layout-Frage.
- **Überladung/§4.3a (G35, G43):** *"by its arguments alone"* muss um „und ihre Constraints"
  ergänzt werden — und **nicht** um den Rückgabetyp.
- **Konformanz-Synthese:** G02 ändert Signaturen; G47 sagt, dass die `extend`-Form die
  Diagnose der Deklarationsform übernimmt; `design/conformance-synthesis.md:72-73` und G05
  müssen dieselbe Constraint-Schreibweise verlangen.
- **Optionals:** `?T`-Varianz (G22-C) — abgelehnt, dort zu vermerken.
- **Operatoren:** `Add<T, R>`; `Self` ersetzt den linken Operandentyp.
- **Pattern-Matching:** G14.
- **Bytecode/Format:** G23, G29, G33, G44 (Impls-Wachstum ungemessen), G37-B (falls je).
- **Embedding:** G34.
- **Diagnose (G18, G31, G37, G38):** vier Stellen, an denen der Code oder die Position heute
  falsch ist; §12.4 macht eine davon zum Konformanzverstoß.
- **Werkzeuge (G13, G40):** `lyrfmt` erzeugt aus gültigem Code ungültigen; Hover/Rename/
  Breakpoints auf Instanzen sind ungeschrieben. **`lyrfix` existiert nicht** und ist
  Vorbedingung von G02/G32.

**Mit der Spezifikation (spec-first) — Divergenzen, jede mit Regel-PR oder Konformanz-Fix:**

| Spec sagt | Compiler tut | Frage | Art |
|---|---|---|---|
| §8.1: Kette endlich, `IR0001`; §12.4: *"reported and not crashed into"* | Klassen-Methoden-Form **stürzt ab** (q15); Interface-Form ohne Position (q02) | G18 | **Konformanz-Fix** |
| §8.3: `Coroutine<T>` bindet strukturell | sechs Formen panicken (q07 u. a.) | G21 | **Konformanz-Fix** |
| §12.1: `CLI0020` nur für Compiler-Fehler | `Box<void>` → `CLI0020` statt SEM (q09) | G37 | **Konformanz-Fix** |
| §12.1: `IR0001` nur für gültiges Lyric; `SEM0047` für falsche `extend`-Ziele | `extend Bag` → `IR0001` an der Klasse (q06) | G38 | **Konformanz-Fix** |
| §8.2: Constraint-Liste trägt Interfaces; §5.1: keine Wiederholung | alles akzeptiert (r04, r45, r46) | G10, G24 | Regel-PR (Code-Geltungsbereich) |
| §4.3a: *"by its arguments alone"*; §8.3 Regel 7: Constraint danach | q04/q04b/q04c — Spec und Compiler stimmen überein, **und die Regel ist falsch** | G35 | Regel-PR |
| Grammar §6.3: drei gleichwertige Folger | `(`-Folger scheitert an `>>`; Formatter erzeugt den Fehler | G13 | Regel-PR (Tokenspaltung) + Fix + Roundtrip-Test |
| §3.7: *"no variance"* | tut es (q01, r09, r33) — **Spec und Compiler stimmen** | G22 | Satz zum Grund, kein Regel-PR |
| §9.2 `spec/09-errors.md:42-44` (gelesen): eine `throws`-Klausel nennt *"a specific throwable type, or `throws` bare"* — ein Typparameter ist nicht vorgesehen | Deklaration mit `throws E` akzeptiert, Aufruf weitet auf `Throwable` (q05) | G36 | Regel-PR |
| §13/`docs/Bytecode.md`: schweigt zu Instanznamen | Namen reisen | G29 | Regel-PR |
| §11: schweigt zu Generics über die Host-Grenze | `SEM0099` eine Richtung | G34 | Regel-PR |

---

## 6. Nach der Kritik geändert

**Kritikpunkte geprüft — übernommen (nachgemessen oder nachgelesen):**

- **G22 komplett neu.** r09 misst die *unsichere* Richtung (gelesen: `apply(f: fn(Shape)->int,
  s: Shape)` mit `(c: Circle) => …`); die sichere Richtung (q01) ist ebenfalls `SEM0001`; §3.7
  (`spec/03-types.md:176-186`) sagt „no variance"; ein `Circle` ist eine nackte Referenz, ein
  `Shape` ein fat pointer (`STATUS.md:2329-2331`, `IrInst.cs:73-76`, `Bytecode.md:854-856`) —
  die sichere Konversion braucht einen Thunk. Empfehlung B → **D** (invariant, mit Grund).
  Rust als Vorbild gestrichen (kein nominales Subtyping); die vier echten Präzedenzfälle
  lehnen r09s Richtung ab.
- **Compilezeit neu gemessen** mit `lyrc build --verbose`, fünf sequenzielle Läufe: ≈ 0.5 ms je
  Instanz (Kritik: ≈ 0.3 auf ruhigerer Maschine); **1.1 ms zurückgezogen**; `verify` ist die
  teuerste Achse. G28: „22 s" → „6–10 s", „4 000" → **keine neue Zahl vor der `stdlib`-Messung**.
- **G18 neu gestellt.** „Typtiefe ≤ 32 als §8.1-Regel" kollidiert mit §12.4
  (`spec/12-diagnostics.md:52-62`: Tiefe nicht Vertrag, Untergrenze 128, `PAR0045`/`SEM0105`
  seit 4.6, `TypeChecker.cs:203`). Zweiter Wächter gefunden und gemessen (q02:
  `ModuleLowerer.cs:57`, `:478-483`, `IR0001` ohne Position). Der q15/r31-Absturz ist ein
  §12.4-Konformanzverstoß. `STATUS.md:1303` gilt für die Interface-Form — Runde 2 hatte es
  falsch verallgemeinert. Reihenfolge-Begründung auf die Klassen-Methoden-Form eingeengt.
- **§1.3 bedingte Konformanz:** die deklarationsseitige Form **existiert** (q03 `true false`,
  q03b `SEM0028` an der Instanz) und ist in `design/conformance-synthesis.md:72-73`
  vorgeschrieben. G05 richtet die `extend`-Form daran aus; **G47** neu.
- **§1.4c Grammatik-Satz korrigiert:** `docs/Grammar.md:139` gibt die Bedeutung von `::`,
  `:281-283` die drei Regeln (Elternliste). Der Befund bleibt.
- **G26 ist ein Ist-Befund:** q04/q04b/q04c — Constraint-only-Überladungen sind immer
  `SEM0086`, und die Constraint wird nach der Wahl geprüft. **G35** neu als Vorfrage; G26-B nur
  noch mit G35. C#-Vergleich vervollständigt (7.3: Constraints = Anwendbarkeit).
- **Interaktionsmatrix zweispaltig:** `throws E` mit Typparameter ist **rot** (q05 `SEM0034`,
  q05d weitet auf `Throwable`, q05c Kontrolle grün) → **G36**.
- **G13:** `lyrfmt` normalisiert `> >` zu `>>` und erzeugt nicht kompilierbaren Code (q14/q14b,
  `AstFormatter.cs:1047-1051`). Option C gestrichen; Fläche = Parser + Grammar + Roundtrip-Test.
- **G38 neu:** `extend Bag` → `IR0001` an der Klasse (q06); `extend<T> Bag<T>` → `PAR0011` (q06b).
- **G21:** Fundort gelesen (`InstanceTable.cs:232-236` vs `FunctionLowerer.cs:305-310`); sechste
  Form gemessen (q07); Zwischenstufe „bis dahin refusen" gestrichen — Fix und Ablehnung fassen
  dieselbe Stelle an. Rust-Beispiel `impl Iterator` → `async fn`.
- **`lyrfix` existiert nicht** (`src/` gelesen; `PLAN.md:343`, `lyric-v5-features.md:163`) → §1.7,
  Vorbedingung in G02/G32/§5.
- **G03:** Vorarbeit zitiert (`TASKLIST_2.md:617-629`, `:1575-1587`, Prototyp 13 mit
  Aufwandsschätzung); Swift-Zeile korrigiert (`static` ≠ `Self`; „Swift vor 5.7 = Option B" war
  falsch). Der Prototyp liegt auf einer anderen Maschine und ist hier nicht messbar; die
  `:652`-Zeilenangabe ist tot (heute `:711-714`, und der Member wird nach `PAR0041` weitergelesen).
- **G30-C** setzt jetzt **G39** (Phantome, q08 gemessen) voraus.
- **Swift-Arrays** (§2 Korrektur 1, G07-D): Upcast sicher wegen Wertsemantik; O(1)-Pfad auf
  ObjC-Plattformen (*behauptet*); Lyrics Preis wäre höher als Swifts.
- **`impl<T: Display> Display for Vec<T>`** existiert nicht; `type_length_limit` kein Vorbild.

**Kritikpunkte geprüft — NICHT oder nur teilweise übernommen, mit Grund:**

- *„G22-B … Neu zu stellen als: Thunk je Konversion oder Ablehnung"* — übernommen als B vs D,
  aber die Empfehlung ist **D (Ablehnung)**, nicht der Thunk: eine Zuweisung, die still Code
  erzeugt, widerspricht dem Charakter der Sprache; das Lambda **ist** der Thunk, sichtbar.
- *„G18 … meldet SEM0105"* — nicht übernommen: `SEM0105` ist laut `appendix-a:202` der
  **Check**-Code („nested deeper than this implementation's check walks"); die Instanzkette ist
  ein Senkungsphänomen, und §8.1 nennt dafür `IR0001`. Empfehlung: **ein** Code (`IR0001`) mit
  Position für alle drei Wächter, die Tiefe als §12.4-Grenze ohne Zahl im Vertrag.
- *„G28 … Erst mit --verbose messen, dann eine Zahl vorschlagen"* — gemessen; **keine** Zahl
  vorgeschlagen, weil die Rumpfgröße der `stdlib` unbekannt ist. Die Kritik hat 0.3 ms, dieses
  Dossier 0.5 ms auf einer geteilten Maschine — die Größenordnung ist dieselbe, die Konstante
  bleibt maschinenabhängig, und deshalb ist die Position in der Diagnose der eigentliche
  Mangel, nicht die Höhe des Budgets.
- *„n38c … soll SEM0087 melden"* — übernommen als Erwartung in G35, **aber** nur nach dem
  Regel-PR: heute stimmen Spec (§4.3a, §8.3 Regel 7) und Compiler überein; es ist kein Bug des
  Compilers, sondern eine falsche Regel.
- *„Sind die drei Wächter einer?"* — beantwortet mit „ein Code, drei Auslöser, eine Position";
  nicht „ein Wächter", weil Instanzzahl und Rundenzahl verschiedene Divergenzen fangen.
- Die Kritik-Proben n02, n06, n11, n15, n46 wurden **nicht** nachgemessen und sind so
  gekennzeichnet; alles, was eine Empfehlung trägt, ist mit q-Proben nachgemessen.

**Vierzehn fehlende Designfragen eingearbeitet:** **G35** Constraints als Anwendbarkeit ·
**G36** `throws E` · **G37** `void` als Typargument · **G18** (neu gestellt) drei Wächter, Code,
Position, §12.4 · **G38** `extend<T> List<T>` / `extend Bag` · **G39** Phantom-Typparameter ·
**G40** Werkzeuge · **G41** §6.3-Fehlalarm · **G42** Default-Rümpfe unter Vtable-Dispatch ·
**G43** Kontext × Überladung · **G44** Verifier/Impls/Funktionstabelle · **G45** opaque als
Typargument · **G46** Konformanzfälle je Empfehlung · **G47** deklarationsseitig vs `extend`.

**Was stehen bleibt und warum.** §1.4a (flache Substitution, r22a/r22c), §1.4e (`<error>`),
die Byte-Messreihe (deterministisch, mit Kontrolle), die Empfehlungen A bei G01, G04, G08, G09,
G12-B, G14, G15, G17, G19, G20, G23-C, G24, G25-B, G27, G29, G31, G32-B, G33, G34 — keine davon
hing an einer der widerlegten Aussagen. G02-D und G32-B bleiben, mit `lyrfix` als benannter,
heute nicht existierender Vorbedingung.
