# Lyric 5 — Gebiet: Überladung und Namensauflösung

Stand der Messung: **2026-09-27, dritte Runde nach der zweiten Kritik**, gegen
`src/Lyrc/bin/Debug/net10.0/lyrc.dll` und `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`, Checkout
`6f6f029f`. Jede Aussage über Lyric 4 trägt eine der drei Marken **gemessen** (Probe kompiliert
und gelaufen), **gelesen** (Pfad:Zeile) oder **behauptet**.

Proben: `scratchpad/v5-design/probes/overloading/` — `p01…p29` (erste Runde), `q01…q18`
(zweite), `n01…n15` (dritte; `n01…n11` stammen vom Kritiker und wurden **alle neu gelaufen**,
`n12…n15` sind neu). Jede Überraschung hat einen Kontrolllauf; die Erwartung stand vor dem Lauf.

---

## 1. Ist-Stand

### 1.1 Wo die Regel steht

Die Grammatik sagt zu diesem Gebiet nichts (`docs/Grammar.md:9-10`, gelesen: „the syntax only").
Der Vertrag steht in `lyric-spec/spec/04-modules.md:69-133` (§4.3a, gelesen) und im Guide
`docs/guide/03-functions.md:41-124`. §4.3a ist die Pflicht, die `STATUS.md:2510-2524` (gelesen,
`:2521-2523`: „the second mechanism has to be given its rules explicitly, in the spec, at the same
time as the feature") beim Zulassen des zweiten Mechanismus auferlegt — und sie ist eingelöst:
§4.3a ist präziser als der Compiler.

Implementierung, gelesen gegen `6f6f029f`:

| Aufgabe | Ort |
|---|---|
| Kandidatenmenge bilden | `src/Lyric.Frontend/Sema/TypeChecker.cs:1963-2003` (`OverloadCandidates`) |
| Auswahl | `TypeChecker.cs:2065-2127` (`SelectOverload`) |
| Rang | `TypeChecker.cs:2132-2147` (`OverloadFit.CompareTo`), `:2150-2181` (`FitOf`) |
| Mengenbildung im Scope | `src/Lyric.Frontend/Resolver/SymbolTable.cs:36-53` (`TryDeclare`), `:58-66` (`OverloadsLocal`), `:96-104` (`Overloads`) |
| Selektiver Import einer Menge | `src/Lyric.Frontend/Resolver/Resolver.cs:227-246` (`ResolveSelective`) |
| Extension-Einzelsuche (kein Set) | `TypeChecker.cs:3871-3889` (`ExtensionMember`, „the first one wins", `SEM0044`) |
| Redeklarationsprüfung | `TypeChecker.cs:302-351` (`CheckOverloadSets`), `:353-362` (`SameParameters`) |
| Name im Bytecode | `src/Lyric.Frontend/Ir/Lowering/NameMangling.cs:68-72`, `ModuleLowerer.cs:199-201` (Suffix), `:181-186` (Importname ohne Suffix), `:1124-1140` (`OverloadSuffixFor`) |
| Host-Aufruf | `src/Lyric.Embedding/ScriptInstance.cs:191-213` (`ResolveOverload`, matcht `ParamCount`) |

### 1.2 Was gemessen wurde

**Erste Runde** (`p01…p29`), unverändert gültig:

| # | Probe | Ergebnis | Bewertung |
|---|---|---|---|
| 1 | `p01_baseline` | `describe(int)/(string)/(int,int)` wählen korrekt | wie dokumentiert |
| 2 | `p02_constraints` | zwei generische Kandidaten, nur einer erfüllbar → `SEM0086` ambig | Loch |
| 2b | `p02b_control` | ein Kandidat → `SEM0028` | der Checker *kann* Constraints, der Fit *fragt nicht* |
| 3 | `p03_returnonly` | nur Rückgabetyp verschieden → `SEM0085` | wie §4.3a:83-85 |
| 4 | `p04_params` | `f(int[])` neben `f(params int[])` → `SEM0085` | wie §4.3a:85-87 |
| 5 | `p05_numeric` | `k(2)` mit `(int64)`/`(float)` → ambig | Loch: kein Konversionsrang |
| 6 | `p06_typeargs` | `f<int>(3)` wählt `f(int)`, `<int>` still ignoriert | still falsch |
| 7 | `p07_value`, `p07b_novalue` | Wertposition wählt; ohne Kontext `SEM0089` | wie §4.3a:123-126 |
| 8 | `p08_localmerge`, `p09_twomodules` | Import und lokale `fn` verschmelzen; zwei Module = eine Menge | Entscheidung, nirgends festgehalten |
| 9 | `p09b_collide`, `p10_localcollide` | gleiche Parameter aus zwei Modulen → `SEM0086` **und** `SEM0085` mit Span in der Bibliotheksdatei | Doppelmeldung, falsche Schuldzuweisung |
| 10 | `p11_qualified` | `mlibx.area(3)` qualifiziert → korrekt | repariert seit dem UDP-Fund (`STATUS.md:985-993`) — **aber nur dieser Zweig**, s. n04 |
| 11 | `p12_iface` | Interface-Member überladen → `SEM0088` | wie spezifiziert |
| 12 | `p14_lambda` | nur im Lambda-Parameter verschieden → ambig | bewusste Regel |
| 13 | `p15_optional` | `f(int)`/`f(?int)`: `f(3)`→int, `f(null)`→opt | korrekt |
| 14 | `p16_extension`, `p28_crossext` | Extension-Überladung auf Struct, auch über Module | funktioniert — **auf Struct**, s. n15n |
| 15 | `p17_exttie` | eigener `m(?int)`, Extension `m(int)`, `m(3)` → Extension gewinnt | widerspricht §5.4 |
| 16 | `p18_extdup`, `p18b_extdupcall` | zwei identische `extend`-Blöcke: stumm ohne Aufruf; `SEM0044`+`SEM0086` mit | Loch + Doppelmeldung |
| 17 | `p19_ifacedefault`, `p29_twodefaults` | Defaults überladen nicht mit (`SEM0042`); zwei Defaults `SEM0043` nach Namen | zweite Auflösung |
| 18 | `p20_static` | `Id.of(7)`/`Id.of("seven")` | funktioniert |
| 19 | `p21_deprecated` | `@Deprecated` auf einer Überladung warnt nur an deren Aufruf | trägt OVL-18 nur teilweise |
| 20 | `p22_genericrecv`, `p22b_control` | `Box<string>.put(2)` wählt `put(T)`, meldet Zuweisungsfehler | still falsch |
| 21 | `p23_defaults`, `p24_params` | Regel 3 und 4 greifen — als Ja/Nein | s. OVL-23 |
| 22 | `p25_mute`, `p25b_control` | kaputtes Argument, Auswahl scheitert → nur `SEM0087` | verschluckte Diagnose |
| 23 | `p26_ifaceparams`, `p26b_concrete` | `f(A)`/`f(B)` ambig; `f(A)`/`f(C)` wählt | wie C#, akzeptabel |
| 24 | `p27_nowiden` | `let n = 2; k(n)` → `SEM0087` | keine Erweiterung von Variablen (§3.7, `spec/03-types.md:179-181`) |

**Zweite Runde** (`q01…q18`), unverändert gültig:

| # | Probe | Ergebnis | Folge |
|---|---|---|---|
| q01 | `q01_ascast` | `k(2 as int64)` → `i64` | es GIBT einen Cast (OVL-16) |
| q02 | `q02_defcount`, `q02b_control` | `f(a,b=0)` neben `f(a,b=0,c=0)`, `f(1)` → `SEM0086`; Kontrolle „two" | OVL-23 — **und das ist genau C#s Verhalten (CS0121)**, s. §2 |
| q03 | `q03_never`, `q03b_control` | `g(panic(…))` → `SEM0086` für `(never)`; Kontrolle Sema OK, dann `LYR-CLI0020` | OVL-27 |
| q04 | `q04_mangle`, `q04b_swapped` | `main.f(Point)` und `main.f(Point)#1`; nach Umsortierung dieselben Namen, andere Funktionen | ABI-Defekt (OVL-14) |
| q05 | `q05_vis`, `q05b`, `q05c` | Member-Zweig: private Überladung gewinnt (`PRIVATE-exact`); Member-Sichtbarkeit generell nicht erzwungen | OVL-19 |
| q06/q07 | `q06_before`, `q06b_after`, `q07_rettype`, `q07b_control` | neue Überladung lenkt `h(2)` still von `float` auf `INT` um; Ergebnistyp ändert sich mit | OVL-25 |
| q08 | `q08_native` | Natives außerhalb der stdlib sind `SEM0051` | OVL-26 nur lesbar |
| q09 | `q09_opmethod`, `q09b_operator`, `q09c_control` | `V :: [Mul<?int, V>]`: `v.mul(2)` läuft, `v * 2` ist `SEM0003` | zwei Algorithmen, messbar verschieden (OVL-15) |
| q10–q12 | `q10_mut`, `q11_static`, `q12_throws` | alle `SEM0085` | OVL-21 |
| q13 | `q13_trailing` | Block-Form zwischen zwei Callback-Typen → `SEM0086` „a lambda" | OVL-22 |
| q14 | `q14_*`, `q14b_*` | Tiefe 18/20/22/24 mit zwei Überladungen: 1,40 / 3,30 / 12,60 / 49,71 s; ohne: ≤ 1,53 s | OVL-20 |
| q15b | `q15b_overload` | `f(a, b=7, params xs)` gegen `f(a,b,c)`, `f(1,2,3)` → „B" | Regeln 3+4 zusammen |
| q16 | `q16_substcollide` | `Box<int64>` mit `put(T)`/`put(int64)`, `put(2)` → läuft, „T" | OVL-28 |
| q17 | `q17_externovl`, `q17b_control` | zwei `extern "dotnet"` desselben Symbols, verschiedene Parameter → läuft (3 / 2.5) | `extern` überlädt (OVL-26) |
| q18 | `q18_neverarg`, `q18b_neverlet` | `never` in Wertposition → `LYR-CLI0020`, auch ohne Überladung | Lowering-Defekt |

**Dritte Runde** (`n01…n15`, diese Session; Erwartung stand vor dem Lauf):

| # | Probe | Erwartung | Ergebnis | Folge |
|---|---|---|---|---|
| n01 | `n01_lambda_nonfn` | `SEM0086` | `f(int)` neben `f(fn(int)->int)`, `f(x => x)` → **`SEM0086` „ambiguous for (a lambda)"** | ein Lambda stimmt auch dort nicht ab, wo die Parameter-ART allein entscheidet — OVL-33 |
| n01b | `n01b_control` | „fn" | nur `f(fn(int)->int)` → „fn" | Kontrolle |
| n02 | `n02_null` | `SEM0086` | `f(?int)`/`f(?string)`, `f(null)` → `SEM0086` „for (null)" | OVL-32 |
| n03 | `n03_emptyarray` | `SEM0087 <error>[]` | `f(int[])`/`f(string[])`, `f([])` → **`SEM0087: no 'f' takes (<error>[])`** — genau die Meldung, die §4.3a:107-110 verbietet | Giftregel sieht nicht in Zusammensetzungen — OVL-12, OVL-30 |
| n03b | `n03b_control` | „ints" | ein Kandidat → „ints" | Kontrolle: der erwartete Typ trägt das Literal |
| n04 | `n04_selimport_vis` | „priv-exact" | Modul `mpv` mit `pub fn m(?int)` und nicht-`pub` `fn m(int)`; `import mpv { m }; m(3)` → **`priv-exact`** | OVL-29 |
| n04b | `n04b_qualified` | „pub-opt" | `mpv.m(3)` → **`pub-opt`** | **das Ergebnis hängt vom Importstil ab** — der Defekt, den `STATUS.md:985-993` als behoben führt, lebt im anderen Zweig weiter |
| n04c | `n04c_privonly` | `RES0004` | nur private `m` → `RES0004` „not public" | Kontrolle: für freie Funktionen ist `pub` Vertrag |
| n04d | `n04d_privfirst` | `RES0004` | private zuerst, `pub` danach → **`RES0004`, obwohl ein öffentliches `m` existiert** | `IsPublic(found)` prüft nur das ERSTE Symbol (`Resolver.cs:238-239`) |
| n05 | `n05_into2` | `SEM0085`+`SEM0086` | `struct W :: [Into<int>, Into<string>]` mit zwei `fn into()` → **`SEM0085` an der Deklaration UND `SEM0086: 'into' is ambiguous for ()`** am Cast `w as string` | `as` ist eine zielgewählte Auflösung, die die Sprache selbst hat — OVL-31 |
| n05b | `n05b_control` | „w" | eine Konformanz → „w" | Kontrolle |
| n06 | `n06_extend_inst` | ? | `extend Box<int>` → `SEM0047` („plain named type in v1") | Extensions auf Instanzen gibt es nicht; keine Überladungsfrage |
| n07 | `n07_trace` | Trace nennt Suffix | Panic-Trace: **`in main.g(int) (n07_trace.lyr:1)`** | der gemangelte Name ist Nutzeroberfläche — OVL-36 |
| n08 | `n08_generic_arity` | `SEM0086` | `f<T>(T)` neben `f<T,U>(T)`, `f<int,int>(1)` → `SEM0086`, Notes **zweimal „(T)"** | Typparameter-Arität trennt nicht, die Anzeige zeigt sie nicht — OVL-34 |
| n09 | `n09_valuegeneric` | `SEM0089` | `apply<T>(describe)` → `SEM0089` mit beiden Signaturen | Wertposition gegen `T`: kein erwarteter Funktionstyp, korrekt abgelehnt |
| n10 | `n10_generic_rename` | `SEM0086` | `f<T>(T)` neben `f<U>(U)` → an der Deklaration angenommen, jeder Aufruf `SEM0086` (Notes „(T)", „(U)") | zwei Funktionen, die kein Aufruf je trennt, sind keine Redeklaration — OVL-34 |
| n11 | `n11_call_on_T` | `SEM0087 (T)` | `fn h<T>(x: T) { return k(x); }` mit `k(int)`/`k(string)` → **`SEM0087: no 'k' takes (T)`** | Auflösung VOR der Monomorphisierung — OVL-35 |
| n11b | `n11b_control` | „int" | `h(x: int)` → „int" | Kontrolle |
| n12 | `n12_enumvariant` | Fehler | `f(Opt<int>)`/`f(Opt<string>)`, `f(Opt.Some(7))` → **`SEM0063`** „write its type arguments" | Kontrolle n12b (ein Kandidat) → „int": die Variante braucht den erwarteten Typ, der Probelauf hat keinen — OVL-30 |
| n13 | `n13_structlit` | Fehler | `f(Box<int>)`/`f(Box<string>)`, `f(Box { v = 1 })` → **`SEM0026`** „expects 1 type argument(s), got 0 … or use it where the type is known" | Kontrolle n13b → „int": dieselbe Form — OVL-30 |
| n14 | `n14_nullcast` | `SEM0006` | `f(null as ?int)` → `SEM0006` (§3.6 kennt keinen Cast nach `?T`) | der einzige Ausweg für n02 ist ein `let` (n14b → „opt-int") — OVL-32 |
| n15j | `n15j_stdcore_show` | `SEM0044` | eigene `extend int { fn show() }` **ohne Import von std.core**, `n.show()` → **`SEM0044`** gegen `std.core`s `Display.show` | std.core ist in der Menge ohne Importzeile — OVL-40 |
| n15k | `n15k_stdonly_show` | „3" | ohne eigene Extension → „3" | Kontrolle: std.core ohne Import erreichbar |
| n15l | `n15l_nocall_show` | stumm | dieselbe Kollision ohne Aufruf → stumm | wie `p18_extdup` |
| n15m | `n15m_stdcore_show_arg` | Auswahl | eigene `show(int)` neben std.core `show()`; `n.show()` → **`SEM0014: expects 1 argument(s), got 0`** | **auf einem Primitiv gibt es KEINE Überladungsmenge** — der erste sichtbare Treffer wird allein geprüft |
| n15n | `n15n_prim_twoown` | Auswahl | zwei eigene Blöcke `twice()`/`twice(int)` auf `int`, `n.twice(5)` → **`SEM0014`** | Kontrolle ohne std.core: der Befund ist der Empfängertyp, nicht die Fremdheit |
| n15o | `n15o_struct_twoown` | 6 / 30 | dieselben zwei Blöcke auf `struct S` → **6 / 30** | Kontrolle: auf Struct funktioniert es. Ursache gelesen: `OverloadCandidates` fragt `TypeFacts.SymbolOf(ReceiverTypeOf(mem))` (`TypeChecker.cs:1990`), das für `PrimitiveType` `null` liefert (`TypeFacts.cs:97-102`) → `default: return []` (`:2002`); die Einzelsuche läuft über `BuiltinSymbol` (`:3420-3429`) in `ExtensionMember`, „the first one wins" (`:3871-3889`) |

### 1.3 Die Konversionsordnung — sieben Arme, und die Giftregel ist flach

`IsAssignable` (`TypeChecker.cs:6023-6042`, gelesen) sagt in sieben Armen ja: (1) `from.IsError ||
to.IsError`; (2) `from is NeverType` („panic(...) fits anywhere", `:6026`); (3) `LyrType.Equal`;
(4) `T`→`?T`, `null`→`?T`; (5) nicht-werfende Koroutine → werfende; (6) Literal-Anpassung an
einen primitiven Typ; (7) `T`→`I` bei Konformanz.

Arm 2 ist auswahlrelevant (`q03_never`). **Arm 1 ist es auch, und anders als in der zweiten Runde
behauptet**: die Giftregel in `SelectOverload` (`:2088-2097`) fragt `t.IsError`, und `IsError` ist
`this is ErrorType` (`src/Lyric.Frontend/Sema/LyrType.cs:65`, gelesen) — sie sieht **nicht in
zusammengesetzte Typen**. Ein leeres Array-Literal ohne erwarteten Typ wird `<error>[]`, das ist
kein `ErrorType`, also läuft es in `FitOf`, wo Arm 1 gegen `int[]` nicht greift (die Elemente
werden über `LyrType.Equal` verglichen) — Ergebnis `SEM0087: no 'f' takes (<error>[])`
(gemessen `n03_emptyarray`). Genau die Meldung, die §4.3a:107-110 als das benennt, was die
Giftregel verhindern soll.

Was bleibt richtig: die Konversionsgitter-Arithmetik von C++ und C# hat Lyric nicht. Eine
`int`-Variable wird nie zu `float` (`p27_nowiden`; §3.7 `spec/03-types.md:179-181`, gelesen).

**Zusatzbefund** (`q18_neverarg`, `q18b_neverlet`): ein `never`-Wert in Wertposition ist auch ohne
Überladung kaputt — `let x: int = panic("boom");` bricht mit `LYR-CLI0020`, einem Compilerdefekt
nach `spec/12-diagnostics.md:70-72` (gelesen).

### 1.4 Wo der Compiler von der Spec abweicht

| Stelle | Spec sagt | Compiler tut |
|---|---|---|
| §5.4 (`spec/05-interfaces.md:126-127`, gelesen): „own member, then visible extension method, then a default method" | eigener Member vor Extension, unbedingt | Extension gewinnt, wenn sie besser passt (`p17_exttie`); §4.3a:101-102 macht daraus einen Tiebreak. Zwei Sätze, die einander widersprechen |
| §5.1 (`spec/05-interfaces.md:43-46`, gelesen): „a written `v.mul(2.0)` resolves exactly where `v * 2.0` does" | eine Auflösung | gemessen falsch: `q09_opmethod` läuft, `q09b_operator` ist `SEM0003` |
| §4.3a:113-117: zwei Extensions mit gleichen Parametern sind `SEM0044` | Deklarationsregel | nur an einer Aufrufstelle (`p18` vs `p18b`, `n15l`) |
| §4.3a:113-114: „Extensions of one name are one set" | für jeden Typ | **nicht für Primitive** (`n15n` `SEM0014` gegen `n15o` 6/30): auf `int`, `string`, … gibt es keine Menge, der erste sichtbare Treffer gewinnt |
| §4.3a:107-111 (Giftregel) | ein Argument, das nicht typisiert, wird für sich gemeldet, nie `<error>` in `SEM0087` | greift nur für einen **nackten** `ErrorType` (`LyrType.cs:65`); `<error>[]` läuft durch (`n03`). *Die zweite Runde beschrieb die Grenze als „meldet Fehler, liefert trotzdem Typ" (`p25`) — das ist ein zweiter, anderer Fall; beide stehen jetzt in OVL-12* |
| §4.3a:87-89: „a selective import brings the whole set" | die Menge, die das Modul exportiert | die **ganze** Menge inklusive nicht-`pub` Überladungen (`n04`), während der qualifizierte Zweig `pub` filtert (`n04b`); umgekehrte Deklarationsreihenfolge verweigert den Import ganz (`n04d`). Dazu §4.2:21 (gelesen): „Only `pub` declarations cross a module boundary" |
| §4.3a:91-102 (fünf Regeln) | nennen Constraints, Typargumente, Substitution, Sichtbarkeit nicht | vier Löcher (`p02`, `p06`, `p22`, `q05`) |
| §4.3a:96 Regel 1 | ein Zähler | trifft `never` nicht (`q03`) |
| §4.3a:99 Regel 3 („needs no default arguments") | liest sich wie ein Zähler | ist ein Flag (`TypeChecker.cs:2179-2180`; `q02_defcount`) — **und das ist auch C#s Regel**, s. §2 |
| §4.3a:83-85 („told apart by what they TAKE, never by what they give back") gegen §3.6/3 (`spec/03-types.md:167-169`, gelesen): `v as T` „IS the call `v.into()`, resolved like any method" | zwei Sätze | `w as string` bei zwei `Into<T>`-Konformanzen: `SEM0085` (der Rückgabetyp trennt nicht) **und** `SEM0086 'into' is ambiguous for ()` (`n05`). §3.6 verspricht eine Auflösung, die §4.3a ausschließt; die Meldung nennt ein `into`, das der Nutzer nie schrieb |
| §8.1 (`spec/08-generics.md:3-24`, gelesen: Monomorphisierung) sagt nicht, **wann** ein überladener Aufruf im generischen Rumpf aufgelöst wird | — | vor der Instanziierung: `k(x)` mit `x: T` ist `SEM0087 no 'k' takes (T)` (`n11`) |

### 1.5 Sechs Auswahlalgorithmen und drei Antworten auf eine Sichtbarkeitsfrage

Rule 2 („ein Mechanismus pro Konzept", `CONTRIBUTING.md:25-40`) ist für Überladung bewusst
gelockert (`STATUS.md:2510-2524`). „Welche von mehreren Implementierungen meint dieser Ausdruck"
wird heute an **sechs** Stellen mit sechs Regeln beantwortet — *die zweite Runde zählte fünf und
übersah die in-Sprache-Form der Rückgabetyp-Auswahl*:

| Mechanismus | Ort (gelesen) | Regel | Fehlercode |
|---|---|---|---|
| Aufruf mit Argumenten | `TypeChecker.cs:2065-2181` | 5-stufiger Fit-Vektor | `SEM0086`/`SEM0087` |
| Operator | `TypeChecker.cs:2588-2620` (`ArithmeticConformance`) | zwei Durchgänge: `Equal`, dann `LiteralAdaptsTo` — kennt weder `T`→`?T` noch `T`→`I` | `SEM0083` |
| **`v as T` über `Into<T>`** | `spec/03-types.md:167-169`; gemessen `n05` | **das Ziel `T` wählt die Konformanz** — die einzige Auflösung der Sprache, die über den Rückgabetyp geht | `SEM0006`, bei zwei Konformanzen `SEM0085`+`SEM0086` |
| Konformanz → vtable-Zeile | §5.4:133-137, pro Interface-Instanz nach Signatur | eigene Regel | `SEM0042`/`SEM0043` |
| `extern "dotnet"` | `docs/guide/14-embedding.md:141-146` | Lyric-Signatur wählt die .NET-Überladung über Parameterliste **und Rückgabetyp** | Ladefehler |
| Host-Aufruf | `ScriptInstance.cs:191-213` | nur `ParamCount` über Textpräfix `name(` | `LYR-EMB0008` |

**Und die Sichtbarkeit einer Überladungsmenge wird an drei Stellen dreimal anders beantwortet**
(gelesen): der modul-qualifizierte Zweig von `OverloadCandidates` filtert
`Visibility.Public` (`TypeChecker.cs:1982-1986`); der Member-Zweig filtert nicht (`:1990-2001`,
gemessen `q05_vis`); und `ResolveSelective` (`Resolver.cs:232-246`) prüft `IsPublic(found)` nur
für das **erste** Symbol des Namens und importiert danach „all the same" alle Überladungen
(gemessen `n04`, `n04d`). *Die zweite Runde kannte nur die ersten beiden.*

Zur Host-Seite: `ResolveOverload` gibt bei `found.Count == 1` den Treffer und wirft bei `> 1`
`LYR-EMB0008` mit beiden vollen Namen (`:202-208`); die Deklarationsreihenfolge entscheidet dort
nie.

### 1.6 Was die Auswahl kostet

Gemessen (`q14_*`): `g(g(…g(1)…))` mit zwei Überladungen, Tiefe 18/20/22/24 → 1,40 / 3,30 / 12,60 /
49,71 s; ohne Überladung ≤ 1,53 s. Ursache gelesen (`TypeChecker.cs:2080-2086`): jedes Argument
wird unter `_de.Mute()` probetypisiert, der Gewinner vom Aufrufer noch einmal; nichts merkt sich
das Ergebnis. Lyric zahlt Swifts Preis ohne Swifts Abbruchmeldung.

### 1.7 Werkzeuge

`src/Lyric.Lsp/Protocol/LspMessages.cs:177-179` (gelesen): „Always 0: Lyric has no overloading";
`SignatureHelpProvider.cs:60-62` liefert genau eine Signatur. Rename und Find-References filtern
auf **ein** `FunctionSymbol` (`RenameProvider.cs:85`, `ReferenceProvider.cs:99`, gelesen) —
s. OVL-38. DocGen-Anker kollidierten bis 4.0.1 (`STATUS.md:994-996`, gelesen).

---

## 2. Sprachvergleich

*Sieben Aussagen der zweiten Runde waren falsch und sind hier korrigiert; sie stehen einzeln in
§6.* Quellen: ECMA-334 (`dotnet/csharpstandard`, `standard-v7/standard/expressions.md`,
§12.6.4.2-3, gelesen); Kotlin-Spec „Overload resolution" (kotlinlang.org/spec, gelesen); JLS
§15.12.2.2 und Rust-Referenz aus dem Gedächtnis (**behauptet**, wo nicht anders vermerkt).

| Sprache | Was trennt Überladungen | Wie gewählt wird | Preis | Für Lyric brauchbar |
|---|---|---|---|---|
| **C#** | Parameterliste inkl. `ref`/`out`/`in`; nicht der Rückgabetyp. **Benannte Argumente seit C# 4.0** (2010) | „better function member": erst Anwendbarkeit — ein generischer Kandidat, dessen (inferierte) Typargumente die Constraints verletzen, ist **nicht anwendbar** (ECMA-334 §12.6.4.2, gelesen; **so erst seit C# 7.3**, davor wurden Constraints NACH der Auswahl geprüft und ein gewählter Kandidat mit verletztem Constraint war ein Fehler — Lyrics `p02b`-Folgefehler; behauptet aus der C#-7.3-Release-Historie). Benannte Argumente filtern die Anwendbarkeit. Dann paarweise bessere Konversion, dann Tiebreaks: nicht-generisch vor generisch; Normalform vor `params`-Expansion; **Default-Argumente als FLAG** — „all parameters of Mv have a corresponding argument whereas default arguments need to be substituted for at least one optional parameter in Mx, then Mv is better" (§12.6.4.3, gelesen). Zwei Kandidaten, die beide Defaults brauchen, sind **CS0121** — `q02_defcount` verhält sich wie C#. Lambdas: keine Phasen; eine implizit typisierte Lambda konvertiert nur in einen Delegattyp, gegen dessen Parametertypen ihr **Rumpf** bindet — pro Kandidat (Roslyn bindet Nicht-Lambda-Argumente **einmal** vor der Auflösung und nur Lambdas je Kandidat neu, mit Cache; behauptet aus Roslyn-Kenntnis). Extension-Methoden erst, wenn keine Instanzmethode anwendbar ist. Default-Interface-Member sind **nie** Member der implementierenden Klasse — `x.d()` auf einem Klassen-Empfänger findet sie nicht | das längste Kapitel der Spec; Ambiguitätsmeldungen schwer lesbar; eine neue Bibliotheks-Überladung kann Quellcode brechen; der 7.3-Wechsel kam als Bruch ohne Warnstufe | Vorlage für OVL-01…03, OVL-06; **Vorbild für OVL-13/C (Rumpf pro Kandidat), nicht für B**; **Vorbild für OVL-23/A (Flag), nicht für B**; **kein Vorbild für OVL-08/B** |
| **Java** | Parameterliste; keine Defaults, keine benannten Argumente | drei Phasen (ohne Boxing/Varargs, mit Boxing, mit Varargs), innerhalb „most specific" über Subtyping. **Eine implizit typisierte Lambda ist „not pertinent to applicability"** (JLS §15.12.2.2, behauptet) — sie nimmt an der Anwendbarkeit nur über ihre Form (Arität) teil, der Rumpf kommt danach. Eine in der Klasse deklarierte Methode beschattet einen single-static-import (JLS §6.4.1). Interface-Defaults sind gewöhnliche Member | ohne Defaults ist Überladung der einzige Weg zu optionalen Parametern → Teleskop-Überladungen | **Vorbild für OVL-13/B** (phasenweise, Arität vor Rumpf), OVL-10/B |
| **Kotlin** | Parameterliste, plus Default- und benannte Argumente | „Tower": Member schlägt lokale Extension schlägt importierte — strikte Priorität, Warnung „shadowed by a member". „For every non-lambda argument … type inference is performed. **Lambda arguments are excluded**, as their type inference needs the results of overload resolution" (gelesen). Most-specific über `Xk <: Yk`; bei Gleichstand: „For each candidate we **count the number of default parameters not specified** in the call … the candidate with the least number … is a more specific candidate" (gelesen). `Nothing`: die **einzige** `Nothing`-Regel der Auflösung betrifft den **Empfänger** („any receiver of type Nothing is deemed not applicable for any member callables", gelesen); als Argument gilt die gewöhnliche Subtyp-Regel, und `Int`/`String` sind gegenseitig kein Subtyp → Ambiguität. Interface-Defaults sind gewöhnliche Member ohne Rangstufe | Turmstufen sind viel Regelwerk | **Vorbild für OVL-07 und OVL-23/B (Zähler)**; **kein Vorbild für OVL-27/B** (dort ist `f(TODO())` ambig — Lyrics heutiges Verhalten); ob die Lambda-Arität in Kotlin mitentscheidet, ist aus der Spec-Stelle nicht belegt (behauptet: ja, über die Form) |
| **Swift** | Parametertypen, Argument-Labels und der Rückgabetyp; `extension`-Methoden eines konkreten Typs stehen in derselben Menge wie die im Haupttyp | Constraint-Solver über den ganzen Ausdruck. `Never` ist ein **unbewohntes Enum, kein Bodentyp**: `f(fatalError())` konvertiert nicht nach `Int`, der Aufruf ist ein Typfehler (behauptet) | „expression too complex"; Compile-Zeiten | Labels: ja (OVL-06). Rückgabetyp-Auswahl: nein. Extension-Punkt: einziges Vorbild für OVL-07/A. **Kein Vorbild für OVL-27/B** |
| **C++** | Parameterliste; Templates über Partial Ordering; Concepts entfernen Kandidaten; `using`-Deklaration bringt Funktionen in einen Scope, wo sie mit den dort deklarierten eine Menge bilden | „best viable function" über Konversionsränge; Zugriffsprüfung **nach** der Auflösung; ADL nur bei `f(x)` und Operatoren, nie bei `x.f()` | ADL-Überraschungen, Template-Fehlerwände | Rangidee (OVL-01), `using` = Vorbild für OVL-10/A; Zugriff-nach-Auswahl = Warnung gegen OVL-19/C |
| **Ada** | Parameterliste, benannte Assoziation und der Rückgabetyp | „complete context" | Zwei-Pass-Auflösung | punktuell: Lyric hat den Rückgabetyp bereits an **zwei** Stellen (Wertposition §4.3a:123-126, `as` §3.6/3) |
| **Rust** | **keine Funktionsüberladung.** `use foo::bar;` neben lokalem `fn bar()` ist E0255 | ein Name, eine Funktion; Trait-Methoden über Autoderef; `<T as Trait>::m`. `!` coerct in jeden Typ, aber **es gibt keine Auswahl, an der `!` teilnehmen könnte** | Builder statt Defaults | E0255 = Vorbild für OVL-10/C; `T::parse` für OVL-05/C; **kein Vorbild für OVL-27/B** |
| **Julia** | Laufzeittypen aller Argumente | Typgitter | Dispatch zur Laufzeit | strukturell unmöglich; Kontrast |

**Die Lehre**, korrigiert: Sprachen mit Überladung *und* benannten Argumenten (Swift, Ada, Kotlin,
**und C# seit 4.0**) brauchen weniger Überladungen, weil der Aufrufer die Absicht hinschreibt.
**Java** ist die Sprache, die weder Defaults noch benannte Argumente hat und deshalb mit
Teleskop-Überladungen zahlt. Lyric hat Defaults, aber keine benannten Argumente
(`docs/Grammar.md:499`) und keine vollständigen Auflösungsregeln — das ist nicht „die schlechteste
der drei Positionen" (die zweite Runde besetzte die Positionen falsch), aber eine, die keine
Vergleichssprache einnimmt: Defaults ohne Namen.

---

## 3. Designfragen

Jede Frage: Ist-Stand mit Beleg, Optionen mit Vorbild und Preis, Empfehlung, Bruchgrad,
4.x-Warnstufe, Abhängigkeiten. Die Konformanzfall-Bilanz jeder Antwort steht gesammelt in OVL-37.

### OVL-01 — Konversionsrang oder nur „exakt / nicht exakt"?

**Heute** (gelesen `TypeChecker.cs:2175-2177`): `converted++` für jedes nicht exakte Argument.
Gemessen `p05_numeric`: `k(2)` mit `k(int64)`/`k(float)` ist `SEM0086`.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | `k(2)` bleibt ambig; jede numerische Familie in der std kollidiert (OVL-18) |
| B: Rang je Argument, paarweise (exakt < Literal < `T`→`?T` < `T`→`I`) | C#, C++ | eine Ordnungsrelation mehr; „besser hier, schlechter dort" muss ambig sein |
| C: Rang nur für Literal-Anpassung, **mit Familienpräferenz** (Ganzzahlliteral → Ganzzahlfamilie, dann schmalster Träger) | C# | eine Regel mit zwei Achsen |
| D: C ohne Familienpräferenz | — | löst `p05` nicht: `int64` und `float` sind verschiedene Familien |

**Empfehlung: C, Tür zu B offen.** `p26_ifaceparams` bleibt bewusst ambig. `T`→`?T` rankt
schlechter als jede Literal-Anpassung. **Bruch: minor. Warnstufe: keine.**
**Hängt an**: OVL-18, OVL-27, OVL-32 (`null` zwischen `?T`), Gebiet Typen.

### OVL-02 — Filtern Constraints die Kandidatenmenge?

**Heute: nein** (gelesen `:2169-2171`; gemessen `p02`/`p02b`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute; **C# vor 7.3** | constraint-getrennte Überladungen unmöglich; bedingte Konformanz (`lyric-v5-features.md:29`) unbenutzbar |
| B: nicht erfüllende Kandidaten entfernen | **C# seit 7.3** (ECMA-334 §12.6.4.2), C++20, Swift | Inferenz muss vor die Auswahl (heute `:3104-3108` danach); multipliziert die Kurve aus §1.6 (OVL-20) |
| C: Constraints als Rangstufe | — | halb gar: `p02b`-Folgefehler bleibt |

**Empfehlung: B, nach OVL-20.** C# zeigt, dass der Wechsel A→B als Bruch ohne Warnstufe
ausgeliefert wurde — Lyric hat §12.5, also muss es das nicht. **Bruch: minor. Warnstufe: keine
nötig** (heute ambige Aufrufe werden gültig). **Hängt an**: OVL-20, OVL-04, Gebiet Generics.

### OVL-03 — Filtern explizit geschriebene Typargumente?

**Heute: nein** (gelesen: `FitOf` liest `call.TypeArguments` nirgends; gemessen `p06`/`p06b`;
Grammatik `docs/Grammar.md:499`). Und `n08`: `f<int,int>(1)` gegen `f<T>` und `f<T,U>` ist
`SEM0086` — die Arität der Typparameter wird ebenfalls nicht gelesen.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | geschriebene Absicht wird verworfen |
| B: Kandidaten mit abweichender Typparameter-Arität entfernen | C#, Java | trivial, deckt `p06` und `n08` |
| C: B plus Substitution der geschriebenen Argumente vor dem Fit | C#, Rust-Turbofish | mit OVL-04 **eine** Substitutionsregel; erbt OVL-28 |

**Empfehlung: C.** **Bruch: minor. Warnstufe: §12.5-Form** („hier stehen Typargumente und
mehrere Kandidaten; ob sie die Auswahl beeinflussen, entscheidet 5.0"). **Hängt an**: OVL-04,
OVL-24, OVL-28, OVL-34.

### OVL-04 — Wird der Empfängertyp vor dem Fit substituiert?

**Heute: nein** (gelesen `:2152`; gemessen `p22`/`p22b`; `q16` läuft heute und würde umgelenkt).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | ein generischer Member schluckt Aufrufe, die er nicht annehmen kann |
| B: Instanz vor dem Fit einsetzen | C#, Kotlin, Swift | kann zwei Kandidaten gleich machen (OVL-28) |

**Empfehlung: B, als v5-Frage.** **Bruch: minor für `p22`, major-verdächtig für `q16`.
Warnstufe: §12.5-Form** an jeder Stelle, an der die Substitution zwei Kandidaten gleich macht.
**Hängt an**: OVL-28, OVL-03, OVL-35.

### OVL-05 — Darf der Rückgabetyp oder der Kontext wählen?

**Heute: an zwei Stellen — nicht einer.** §4.3a:91 („By its arguments alone") und Guide
`03-functions.md:71`. Ausnahme 1: Wertposition (§4.3a:123-126, `p07`). **Ausnahme 2, die die
zweite Runde nicht kannte**: `v as T` bei `Into<T>` (§3.6/3, `spec/03-types.md:167-169`, gelesen:
„the cast IS the call `v.into()`, resolved like any method") — das Ziel `T` wählt. Gemessen `n05`:
zwei `Into<T>`-Konformanzen sind `SEM0085` an der Deklaration (der Rückgabetyp trennt nicht) und
`SEM0086 'into' is ambiguous for ()` am Cast. §3.6 und §4.3a widersprechen einander; heute kann
ein Typ nur **ein** `Into<T>` tragen, und §3.6 sagt es nicht.

std-Rechnung: `parseInt(string): ?int` (`stdlib/std/string.lyr:492`) und `parseFloat(string):
?float` (`:628`) sind unter einem Namen nicht vereinbar (`p03`). An der `extern`-Grenze wählt der
Rückgabetyp bereits mit (`docs/guide/14-embedding.md:141-146`, gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A: nie über den Rückgabetyp; **§3.6/3 wird auf eine Konformanz je Typ festgeschrieben** | C#, Java, Kotlin | `parseInt`/`parseFloat` behalten zwei Namen; `Into<int>` und `Into<string>` auf einem Typ bleiben unmöglich, aber ausgesprochen |
| B: Rückgabetyp entscheidet, wenn der Kontext ihn nennt | Ada, Swift | bidirektionale Auswahl — der Swift-Solver |
| C: A plus statische Interface-Member mit `Self` (`T.parse(s)`) | Rust (`str::parse::<T>`), `lyric-v5-features.md:31` | **ein DRITTER zielgewählter Weg** neben Wertposition und `as` — die Rule-2-Frage, die die zweite Runde für OVL-06/16 stellte und hier nicht |
| D: C, und `as`/`Into<T>` wird **auf denselben Mechanismus zurückgeführt**: `Into<T>` als Interface mit `Self`-Rückgabe, mehrere Konformanzen erlaubt, `as T` wählt die Instanz wie eine Wertposition | — | löst den §3.6/§4.3a-Widerspruch in eine Richtung auf; Preis: OVL-31 muss vorher beantwortet sein |

**Empfehlung: C, aber nur mit der ausdrücklichen Rule-2-Antwort, dass Wertposition, `as` und
`T.parse` **eine** Regel sind („wo der Kontext einen Typ nennt, wählt er die Instanz eines
`Self`-tragenden Members") — sonst D.** B bleibt der teuerste Posten. *Die zweite Runde empfahl C
ohne die Frage zu stellen, ob C ein dritter Mechanismus ist; er ist einer, solange die Spec die
drei nicht als eine Regel formuliert.* **Bruch: nein. Warnstufe: keine.** **Hängt an**: OVL-31,
OVL-18, OVL-26, Gebiet Interfaces/Generics.

### OVL-06 — Benannte Argumente

**Heute: gibt es nicht** (`docs/Grammar.md:499`; `lyric-v5-features.md:39` P2).

| Option | Vorbild | Preis |
|---|---|---|
| A: keine | Java, Rust, C++ | Builder/Options-Structs; Überladungszahl steigt |
| B: Namen **filtern** die Anwendbarkeit, keine Rangstufe | C# (§12.6.2.2, gelesen), Kotlin | Parameternamen werden Teil der Anwendbarkeit; die Spec muss sie als Vertrag benennen |
| C: Namen sind Teil der Identität | Ada, Swift | Umbenennen ist ein Bruch |
| D: C mit getrenntem Label | Swift | eine Syntaxstufe mehr |

**Empfehlung: B in 5.0, D offengehalten.** Was in die Spec muss: dass ein Parametername ab dann
veröffentlicht ist. **Bruch: nein. Warnstufe: keine.** **Hängt an**: OVL-23, OVL-16/D, OVL-32.

### OVL-07 — Eigener Member vs. Extension: Priorität oder Tiebreak?

**Heute: Tiebreak** (§4.3a:101-102; `:2143-2146`; gemessen `p17_exttie`), §5.4 sagt das Gegenteil.

| Option | Vorbild | Preis |
|---|---|---|
| A: Tiebreak, §5.4 korrigieren | Swift | ein `import` ändert die Bedeutung eines Aufrufs — und `std.core` braucht nicht einmal den Import (OVL-40) |
| B: strikte Priorität plus Warnung „Extension wird verdeckt" | Kotlin, C# | `p17` meldet Typfehler statt still die Extension zu nehmen |
| C: Priorität nur bei gleicher Arität | — | Halbregel |

**Empfehlung: B**, ausdrücklich **auch gegen `std.core`** (OVL-40). **Bruch: minor→major**
(4.6-Fund „§5.4 Extension vor Default", `PLAN.md:39`). **Warnstufe: §12.5-Form.**
**Hängt an**: OVL-08, OVL-24, OVL-40, Gebiet Interfaces.

### OVL-08 — Gehören Default-Methoden in die Kandidatenmenge?

**Heute: nein, zwei Auflösungen** (`OverloadCandidates :1990-2001` sammelt keine Defaults;
gemessen `p19`, `p29`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute; **C#** (Default-Interface-Member sind nie Member der Klasse — `x.d()` auf Klassen-Empfänger findet sie nicht) | „welche Methode meint `x.d(…)`" hat zwei Antworten; ein Typ kann eine Default-Methode nicht um eine Überladung ergänzen |
| B: Defaults als **gewöhnliche Kandidaten**, `SEM0043` nur bei gleicher Parameterliste | **Java, Kotlin** (Defaults sind geerbte Member ohne Rangstufe) | vtable trägt weiterhin einen Slot pro Name; die Wahl fällt statisch |
| B': B plus eigene Rangstufe „nach eigenem Member und Extension" | **kein Vorbild** — Lyric-eigen, als Fortsetzung von Regel 5 | eine Rangstufe mehr; begründbar nur, wenn OVL-07/B Extensions strikt vor Defaults stellt, dann ist es keine Rangstufe, sondern eine Priorität |
| C: B und Interface-Member überladbar (OVL-09) | — | Formatfrage |

**Empfehlung: B, mit OVL-07/B als Priorität statt Rangstufe.** *Die zweite Runde nannte C# als
Vorbild für B' — falsch: C# ist das Vorbild für A.* **Bruch: minor. Warnstufe: keine.**
**Hängt an**: OVL-07, OVL-09.

### OVL-09 — Dürfen Interface-Member überladen?

**Heute: nein** (§4.3a:78-81; `:302-351`; gemessen `p12`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | einzige Stelle ohne Überladung |
| B: Slot-Identität Name + Parameterliste | C#, Java | Formatbruch; Konformanzprüfung schlüsselt schon nach Signatur (§5.4:133-137) |
| C: Name + Arität | — | halbe Lösung |

**Empfehlung: B, wenn das Format ohnehin angefasst wird.** **Bruch: major (Format). Warnstufe:
keine möglich.** Pin `overloaded_member_refused.lyr` (`SEM0088`) retiriert mit der Regel (OVL-37).
**Hängt an**: Gebiet Bytecode, OVL-14, OVL-26.

### OVL-10 — Verschmelzen Importe Überladungsmengen, oder verdecken sie?

**Heute: sie verschmelzen** (`SymbolTable.cs:36-53`, `:72-78`; gemessen `p08`, `p09`, `p10`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | C++ `using` | eine Bibliothek kann Aufrufe umlenken (OVL-25) |
| B: lokale Deklarationen verdecken importierte ganz | C# (`using static`), Java JLS §6.4.1, Kotlin | `p08` bricht |
| C: A, aber Kollision = Importfehler; importiert verliert Gleichstand gegen lokal | Rust E0255 | zwei Regeln |

**Empfehlung: C.** **Bruch: minor. Warnstufe: §12.5 am Import.** **Hängt an**: OVL-11, OVL-24,
OVL-25, OVL-29.

### OVL-11 — Duplikat an der Deklaration oder am Aufruf?

**Heute: uneinheitlich, einmal gar nicht** (`p18`/`p18b`, `n15l`; §4.3a:113-117 formuliert eine
Deklarationsregel).

**Pin-Lage, gelesen**: `lyric-spec/conformance/cases/04-modules/two_extensions_of_one_signature_refused.lyr`
ist `//! check`, `//! error: LYR-SEM0044`, **mit `b.tell(2)` im Rumpf** — er pinnt die
Aufruf-Form. Ob der Runner den Span mitprüft oder nur den Code: **behauptet, nur den Code**. Ein
Fix, der an der Deklaration meldet, lässt den Pin dann grün (der Code erscheint weiterhin); ein
Fix, der die Aufrufmeldung *entfernt* und nur an der Deklaration meldet, ebenso. Ein Fix, der
`SEM0044` in `SEM0085` umbenennt, retiriert ihn — und braucht einen `since:`-gegateten Nachfolger.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | Bibliothek liefert zwei identische Extensions, Fehler beim Nutzer |
| B: alles an der Deklaration | Rust (Kohärenz), C# | fremde Module, die der Nutzer nie zusammenbringt, kollidieren |
| C: Deklaration im selben Modul, Aufruf bei verschiedenen | — | zwei Regeln, ein Satz Begründung |

**Empfehlung: C, Doppelmeldung abstellen, Pin bleibt grün** (`SEM0044` behält den Code, die
Deklarationsmeldung im selben Modul bekommt ihn ebenfalls oder `SEM0085` — dann Pin-Zwilling mit
`since:`). **Bruch: minor. Warnstufe: erst Warnung, dann Fehler.** **Hängt an**: OVL-10, OVL-12,
OVL-37, OVL-40.

### OVL-12 — Diagnostik: verschluckt, doppelt, falsch adressiert — und `<error>[]`

**Heute vier gemessene Probleme.**

1. **Verschluckt.** `p25_mute`: ein Argument, das einen Fehler meldet und trotzdem einen Typ liefert,
   verliert seine Diagnose unter `_de.Mute()` (`:2082-2086`).
2. **`<error>` in `SEM0087`.** `n03_emptyarray`: `f([])` → `no 'f' takes (<error>[])`. Ursache
   gelesen: `IsError => this is ErrorType` (`LyrType.cs:65`) sieht nicht in Zusammensetzungen; und
   `SelectOverload` typisiert **ohne erwarteten Typ** (`:2082-2086`), also kann ein
   kontextabhängiges Literal gar nicht typisieren. *Die zweite Runde beschrieb nur Fall 1.*
3. **Doppelt.** `SEM0044`+`SEM0086` (`p18b`), `SEM0085`+`SEM0086` (`p10`), `SEM0043`+`SEM0001` (`p29`),
   `SEM0085`+`SEM0086` (`n05`).
4. **Falsch adressiert.** `p10`: Span in `mlibx.lyr`, Text „declared twice in module 'main'".

| Option | Vorbild | Preis |
|---|---|---|
| A: Diagnosen jedes Probelaufs puffern, die des Gewinners ausspielen | Swift, Rust `probe` | exponentiell in Zeit UND Speicher ohne OVL-20 |
| B: nach `SEM0086`/`0087` die Argumente einmal ungemutet nachprüfen | — | deckt Fall 1; **deckt Fall 2 NICHT** — `[]` typisiert auch ungemutet nicht ohne erwarteten Typ |
| C: Ausgabe-Disziplin: eine Auswahl-Diagnose pro Aufrufstelle | Rust | Disziplin |
| D: Probetypisierung **mit dem Parametertyp des jeweiligen Kandidaten** als erwartetem Typ (nur für Argumentformen, die ihn brauchen — OVL-30) | Roslyn (Lambdas pro Kandidat), Java (Poly-Ausdrücke) | pro Kandidat ein Lauf für diese Formen; die Kurve aus §1.6 wächst um den Faktor Kandidaten, aber nur für Literale, nicht für Teilausdrücke |
| E: **tiefe Giftregel** — `IsError` rekursiv über `ArrayOf`, `Optional`, Tupel, Instanzen | — | drei Zeilen; `n03` wird zu „`[]` braucht einen erwarteten Typ" statt `<error>[]`; löst den Aufruf **nicht**, macht ihn ehrlich |

**Empfehlung: E + B als 4.x-Fix (billig, macht §4.3a:107-110 wahr), D + C für 5.0 zusammen mit
OVL-30, A erst nach OVL-20.** *Die zweite Runde empfahl B allein und übersah, dass B `n03` nicht
erreicht.* Pin `a_broken_argument_reports_itself.lyr` (`SEM0002`, since 3.0.1) bleibt grün.
**Bruch: nein (E/B/C), minor (D: heute abgelehnte Aufrufe werden gültig). Warnstufe: entfällt.**
**Hängt an**: OVL-20, OVL-30, OVL-39, OVL-02.

### OVL-13 — Nehmen Lambdas an der Auswahl teil?

**Heute: nein** — `argumentTypes[i]` bleibt `null` (`:2085`), `FitOf` überspringt (`:2163`
`continue`, gelesen). §4.3a:119-121. Gemessen `p14`, `q13`, und **`n01`: `f(int)` neben
`f(fn(int)->int)`, `f(x => x)` → `SEM0086`, obwohl nur EIN Kandidat einen Funktionsparameter hat.**
Das Loch ist größer als „nur im Callback-Typ verschieden": jede Überladung, die neben einem
Nicht-Funktionsparameter steht, ist mit Lambda unaufrufbar.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | s. o. |
| E: **Parameter-ART filtert**: ein Lambda passt in keinen Nicht-Funktionsparameter — Kandidat fällt raus | jede Sprache mit Überladung (bei C#/Java über die Konvertierbarkeit) | **null Kosten, zirkularitätsfrei**, deckt `n01` vollständig; deckt `p14`/`q13` nicht |
| B: Arität des Lambdas stimmt ab, Rumpf ungeprüft | **Java** (JLS §15.12.2.2: implizit typisierte Lambda „not pertinent to applicability", Form zählt) | zirkularitätsfrei; wirkungslos in der Block-Form (Arität immer 1, OVL-22) |
| C: volle Lambda-Inferenz pro Kandidat | **C#/Roslyn** (Rumpf bindet gegen jeden Delegattyp, mit Cache), Swift | exponentiell bei Schachtelung — §1.6 |
| D: B plus geschriebene Parametertypen nehmen voll teil | C#, Kotlin | trivial; in der Block-Form nicht schreibbar |

**Empfehlung: E sofort (4.x, keine Sprachentscheidung: ein Lambda IN einen `int`-Parameter war
nie gültig), B + D für 5.0, OVL-22 in derselben Runde.** *Die zweite Runde nannte für B „Kotlin,
C# (Phase 1)": C# hat keine Phasen und ist das Modell von C; das Phasenmodell ist Java. Kotlin
schließt Lambdas aus der Inferenz aus (gelesen) — ob die Form mitzählt, ist unbelegt.*
**Bruch: minor (heute ambige Aufrufe werden gültig). Warnstufe: keine.**
**Hängt an**: OVL-22, OVL-33, OVL-20, Gebiet Lambdas.

### OVL-14 — Manglingschema und Host-Auflösung

**Heute** (gelesen `NameMangling.cs:64-72`, `ModuleLowerer.cs:1124-1140`): Anzeigeform der
Parameterliste plus Ordinal nach **Deklarationsposition**. Gemessen `q04`/`q04b`: nach
Umsortierung bedeuten dieselben Namen andere Funktionen. Host: `ScriptInstance.cs:191-213`.

**Wer den Namen liest** (OVL-36, hier nur die Bilanz): Panic-Trace (`n07`: `in main.g(int)`),
DAP-Frames (`DebugController.cs:461,469`: `frame.Fn.Source.Name`, gelesen), VM-Fehlertexte
(`Interpreter.cs:535,677,727,806,813,854`, gelesen), `LYR-EMB0008` (`:205-208`), DocGen-Anker
(`STATUS.md:994-996`), und ein fremder Loader (behauptet: erato2/Lyricpp liest `.lyrbc`;
`STATUS.md:897-900` belegt nur einen DLL-Loader).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | — | Name hängt an Schreibweise und Reihenfolge |
| B: kanonisches Mangling plus getrenntes Anzeigefeld | C++ Itanium, .NET | Format wächst; Demangler nötig |
| B': Ordinal durch positionsunabhängigen Tiebreak (voll qualifizierter Typname im Suffix, nur bei Kollision) ersetzen | — | kleinster Schritt; **ändert Namen, die Traces, Debugger, Docs und Hosts heute zeigen** — nur dort, wo die Anzeigeform kollidiert, also selten |
| C: typisierte Host-Auflösung | JNI, .NET `GetMethod(name, types)` | Embedding-API wächst |

**Empfehlung: B' für 4.x, B für 5.0, C für die Host-API.** *Die zweite Runde buchte B' als
„repariert Namen, die nachweislich falsch sind" ohne Konsumenten — sie stehen jetzt in OVL-36, und
B' bleibt minor, weil es nur Namen ändert, die heute schon `#1` tragen.*
**Bruch: major (B/C), minor (B'). Warnstufe: keine für B'.** **Hängt an**: OVL-36, OVL-41,
OVL-09, OVL-26, Gebiet Bytecode/Embedding.

### OVL-15 — Ein Auswahlalgorithmus oder sechs?

**Heute sechs** (§1.5). *Die zweite Runde zählte fünf; die sechste ist `as` über `Into<T>`.*

| Option | Vorbild | Preis |
|---|---|---|
| A: alle sechs in der Spec nebeneinander | — | sechs Regeln driften |
| B: Operator auf den Aufruf-Fit zurückführen (`a * b` = `a.mul(b)`) | Rust, Kotlin `operator fun` | heute abgelehnte Operatorausdrücke werden gültig (`q09b`) |
| B': B, und `as` über `Into<T>` auf **dieselbe** Regel wie die Wertposition (OVL-05/D) | — | zwei der sechs verschwinden |
| C: B, `extern` an denselben Rang | — | .NET wählt über Rückgabetyp; nicht erreichbar |

**Empfehlung: B', sonst B.** `spec/05-interfaces.md:43-46` ist heute unwahr (`q09`/`q09b`).
**Bruch: minor, additiv mit Specänderung (§5.1, §6.1, §3.6). Warnstufe: entfällt; `since:`-Gate.**
Pin `a_second_conformance_answers_a_member_call.lyr` bleibt grün (er pinnt die Member-Form, die
heute schon geht). **Hängt an**: OVL-01, OVL-05, OVL-31, Gebiet Operatoren.

### OVL-16 — Ausweg, eine Überladung zu erzwingen?

**Heute: ja für Zahlen** (`q01_ascast`; §3.6 `spec/03-types.md:156-172`); **nein für `null`**
(`n14_nullcast`: `null as ?int` ist `SEM0006`) und nicht für Interfaces (`p26`). Ausweg ist jeweils
ein `let` (`n14b`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | Zwischenvariable |
| B: explizite Typargumente (= OVL-03/C) | C#, Rust | nur generisch gegen nicht-generisch |
| C: qualifizierte Aufrufform | Rust UFCS | `::` ist die Interface-Liste (`Grammar.md:139`) |
| D: Askription `(e: T)` / `e as! T` — behauptet, konvertiert nicht | Rust `let x: T`, Scala | neue Syntax; `(e: T)` kollidiert mit OVL-06; **`as` konvertiert mit Laufzeitsemantik (`03-types.md:160-165`), Askription nicht** — zwei Dinge unter einem Wort wären Rule 2 |

**Empfehlung: B + A, D offengehalten — und OVL-32 zeigt, dass `null` der erste echte Fall für D
ist.** **Bruch: nein. Warnstufe: s. OVL-03.** **Hängt an**: OVL-03, OVL-06, OVL-32, Gebiet Typen.

### OVL-17 — Was schulden die Werkzeuge?

**Heute: nichts** (§1.7). Rename/Find-References: OVL-38.

| Option | Vorbild | Preis |
|---|---|---|
| A: LSP zeigt alle Signaturen, `activeSignature` = gewählte | LSP-Standard | Kandidatenmenge muss ins Modell |
| B: A plus Auswahl-Begründung im Hover | rust-analyzer | mehr Modelldaten |
| C: nichts | heute | schlechter als keine Signature Help |

**Empfehlung: A, plus OVL-38/B (Rename = ganze Menge).** **Bruch: nein.** **Hängt an**: OVL-12,
OVL-20, OVL-38.

### OVL-18 — Was muss stehen, bevor die std ihre Typsuffixe aufgibt?

**Heute** (gelesen `stdlib/std/math.lyr:22,35,85,166,176,273`, `string.lyr:22,29,492,628`;
`docs/guide/13-standard-library.md:304`; `lyric-v5-features.md:58`; `PLAN.md:368` Uhr 4.7).

| Paar | Körper? | nach Umbenennung | braucht |
|---|---|---|---|
| `abs(float)` nativ + `absInt(int)` Körper → `abs` | gemischt | Native behält Importname ohne Suffix (`ModuleLowerer.cs:181-186`), Körper `std.math.abs(int)` (`:199-201`); keine Kollision | OVL-25 |
| `min` nativ + `minInt` → `min` | gemischt | wie oben; `min(a, 2.0)` mit `a: int` → `SEM0087` (`p27`) | Doku, OVL-25 |
| `clamp` + `clampInt` | beide Körper | funktioniert | OVL-25 |
| `fromInt` + `fromFloat` → `from` | **beide nativ** | **unmöglich aus ZWEI Gründen** (gelesen; *die zweite Runde zählte drei*): (1) `NativeRegistry._natives` ist `Dictionary<string, Native>` (`src/Lyric.Vm/NativeRegistry.cs:25,52`), Bindung über `import.Name` (`:166`), Signaturabweichung = Ladefehler (`:193-197`); (2) der Namensindex `ImportTable._byName`/`TryFind` (`ImportTable.cs:46,62,116`), über den `CallHelper` (`FunctionLowerer.cs:4805-4809`) die Helfer `std.string.fromInt`/`fromFloat` findet (`:4927,4933`; normativ `spec/04-modules.md:135-140` §4.4). **Nicht** die Importzeilen selbst: `Intern` schlüsselt nach Name UND Signatur (`ImportTable.cs:119-124,132-134`), zwei Natives eines Namens sind bereits zwei Zeilen | OVL-26 |
| `parseInt` + `parseFloat` → `parse` | — | unmöglich (`p03`) | OVL-05/C |
| `int64`/`float32`-Überladungen | — | ambig bei Literalen (`p05`) | OVL-01/C |
| **Extensions auf `string`/`int`** (z. B. `padStart`) überladen | — | **unmöglich**: auf Primitiven gibt es keine Überladungsmenge (`n15n`) | **OVL-40** |

**Empfehlung: Lackmustest der Runde; erst nach OVL-01/C, OVL-05/C, OVL-12, OVL-25, OVL-26,
OVL-40.** **Bruch: major. Warnstufe: `@Deprecated` trägt `absInt(2)`, nicht `abs(2.0)`
(`q06b`, `q07`) — das ist OVL-25.**

### OVL-19 — Filtert Sichtbarkeit die Kandidatenmenge? (Member-Zweig)

**Heute: nein im Member-Zweig** (`:1990-2001`; gemessen `q05`, `q05b`, `q05c`: Member-Sichtbarkeit
wird generell nicht erzwungen). Der Importweg steht in **OVL-29**, weil er anders liegt: dort ist
`pub` heute schon Vertrag.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | private Hilfsüberladung lenkt Aufrufe um |
| B: unzugänglicher Kandidat verlässt die Menge | C#, Java, Kotlin, Rust | Nutzer sieht den unsichtbaren besseren Kandidaten nicht |
| C: bleibt in der Menge, Zugriff danach Fehler | C++ | die C++-Falle |
| D: B plus Note „ein weiterer Kandidat ist hier nicht sichtbar" | — | eine Zeile |

**Empfehlung: D.** **Bruch: major für Member**, weil er mit „Member-Sichtbarkeit (Default privat)"
zusammenfällt (`PLAN.md:367`, Uhr 4.7). **Warnstufe: §12.5-Form, Text „hier gewinnt ein Kandidat,
der nicht öffentlich ist" — und der Text muss den Importweg (OVL-29) einschließen.**
**Hängt an**: OVL-29, OVL-24, OVL-25, OVL-10.

### OVL-20 — Was kostet die Auswahl, und was sagt die Spec zu?

**Heute: exponentiell** (§1.6). Kein Code für „will not finish typing".

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | Swifts Preis ohne Meldung; OVL-02/B, OVL-12/A/D, OVL-13/C verschärfen |
| B: **Argumente einmal binden, vor der Auswahl; nur kontextabhängige Formen (Lambda, OVL-30-Literale) pro Kandidat, mit Cache** | **Roslyn** (Argumente werden EINMAL gebunden; nur Lambdas je Delegattyp neu, gecached) | Reihenfolge umkehren, nicht nur cachen — *die zweite Runde beschrieb den Cache als das Tragende; tragend ist die Reihenfolge* |
| C: Schranke plus Diagnose „expression too complex" | Swift | gültige Programme werden ungültig; Grenze wird Spec-Zahl |
| D: B plus **Implementierungs-Invariante** „jeder Teilausdruck wird einmal typisiert" — im Compiler als Assertion, **nicht** als Spec-Zusage | — | *die zweite Runde wollte „linear in Kandidaten und Ausdrucksgröße" in die Spec; das gibt keine Vergleichssprache, und OVL-02/B bricht es sofort (Kandidaten × Constraints)* |

**Empfehlung: D — B plus Invariante im Compiler, C als Notausgang; die Spec sagt nichts über
Kosten, weil sie es nicht einhalten könnte.** Voraussetzung: OVL-39 (welche Seitentabellen der
Probelauf schreibt). **Bruch: nein. Warnstufe: entfällt.** **Hängt an**: OVL-39, OVL-02, OVL-12,
OVL-13, OVL-30.

### OVL-21 — Trennen `mut`, `static` oder `throws`?

**Heute: nein, alle `SEM0085`** (`q10`, `q11`, `q12`; §4.3a:83-85). Uhren: `PLAN.md:374` (`mut` auf
Klassenmethoden, läuft seit 4.6 als `SEM0108`) und `:375` (`mut struct`, `SEM0109`) — *die zweite
Runde zitierte `:371,373`, das sind „`?Struct` als Wert" und „Parameter vs. `let`"*.

| Option | Vorbild | Preis |
|---|---|---|
| A: nur die Parameterliste | heute, C#, Java | `static fn of(int)` und `fn of(int)` unvereinbar, obwohl kein Aufruf sie verwechselt |
| B: Empfängermodus trennt, `mut`/`throws` nicht | — | Ausnahme in „die Parameterliste und nichts sonst"; Bytecode trennt schon (`ModuleLowerer.cs:288-289`) |
| C: `mut` trennt | Rust `Index`/`IndexMut` (zwei Traits, keine Überladung) | der Kontext wählt |
| D: `throws` trennt | — | Ausgabeseite |

**Empfehlung: B; A für `mut` und `throws`, ausdrücklich in §4.3a.** **Bruch: minor (B).
Warnstufe: keine.** Pin `overload_same_parameters_refused.lyr` bleibt grün (er pinnt den
Rückgabetyp-Fall). **Hängt an**: Gebiet Werte (`PLAN.md:374-375`), Gebiet Fehler, OVL-14.

### OVL-22 — Trailing-Lambda-Form in der Auswahl

**Heute: gar nicht** (`Grammar.md:499-500,511-514`; gemessen `q13`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | überladene Callback-APIs in der Block-Form unaufrufbar |
| B: optionale Parameterliste in der Block-Form | Kotlin, Swift | eine Grammatikregel |
| C: Block-Form bei überladenen Zielen verboten | — | zweitklassige Form |
| D: einziger einstelliger Callback gewinnt | — | deckt `q13` nicht |

**Empfehlung: B + D, C Notausgang; OVL-13/E hilft hier ebenfalls** (ein Block passt in keinen
Nicht-Funktionsparameter). **Bruch: nein. Warnstufe: keine.** **Hängt an**: OVL-13, OVL-33.

### OVL-23 — Regel 3: Zähler oder Flag?

**Heute: Flag** (`:2179-2180`; `q02`). **Und das ist C#s Regel** (ECMA-334 §12.6.4.3, gelesen:
„default arguments need to be substituted for at least one optional parameter"); C# meldet
`M(1)` gegen `M(int, int=0)`/`M(int, int=0, int=0)` als CS0121. Der Zähler ist **Kotlins** Regel
(Spec, gelesen: „count the number of default parameters not specified … the least number … is
more specific"). *Die zweite Runde schrieb C# den Zähler zu — falsch.*

| Option | Vorbild | Preis |
|---|---|---|
| A: Flag | heute, **C#** | wachsende Konfigurations-API (`connect(host)`, `connect(host, port=…)`, …) ist beim kürzesten Aufruf immer ambig |
| B: Zähler, kleiner gewinnt | **Kotlin** | `f(1)` wählt „two"; wer das andere meinte, schreibt `f(1, 0, 0)` — oder, mit OVL-06, `f(1, c = 0)` |
| C: B vor `Generic` | — | keine Begründung |
| D: Regel 3 als Filter | — | Defaults neben Überladung unbrauchbar |

**Empfehlung: B, neu begründet.** Der Preis von B („wer das andere meinte") ist genau C#s Grund für
das Flag — aber C# hat benannte Argumente, mit denen der Aufrufer den längeren Kandidaten
adressieren kann; Lyric hat sie heute nicht, und bis OVL-06 kommt, ist `f(1, 0, 0)` der einzige
Weg. B ist trotzdem richtig, weil A das Muster „weniger Parameter = spezifischer" verbietet, das
ohne benannte Argumente die **einzige** Form einer wachsenden API ist. Wenn OVL-06/B kommt, ist B
harmlos. **Bruch: minor. Warnstufe: keine.** **Hängt an**: OVL-06, OVL-01, OVL-41.

### OVL-24 — Diagnoseklasse der 4.x-Warnungen

**Heute** (gelesen `spec/12-diagnostics.md:74-110`): §12.5 Migrationswarnungen `SEM0107…0110`,
alle mit 5.0 entschieden; drei Eigenschaften (`:93-103`). `PLAN.md:352` (gelesen: „Jede Position
braucht eine 4.x-Warnstufe, bevor sie greifen kann" — *die zweite Runde zitierte `:355-357`, das
ist „Alle Uhren sind um eine Minor verschoben"*) und `:359-363`: „Und sie starten jetzt".

| Option | Vorbild | Preis |
|---|---|---|
| A: gewöhnliche Warnungen | — | keine Zusage, keine Retirierung |
| B: weitere §12.5-Fragen mit eigenen Codes und Konformanzfällen, die die WARNUNG pinnen | die vier bestehenden | Wortlaute ohne Antwort |
| C: B in eigener Uhr | `PLAN.md:366-378` | Buchhaltung |
| D: keine Warnungen | — | widerspricht `PLAN.md:352` |

**Empfehlung: B.** Formulierbar ohne Antwort: OVL-03, OVL-04/28, OVL-07, OVL-10, OVL-19/29
(„hier gewinnt ein Kandidat, der nicht öffentlich ist — über Import oder Member"), OVL-35. Nicht
als §12.5: OVL-25 (eingetretene Umlenkung), OVL-29 für freie Funktionen (Bugfix gegen §4.2:21 —
gewöhnliche Warnung, dann Fehler). **Bruch: nein.** **Hängt an**: alle genannten, OVL-37.

### OVL-25 — Ist das Hinzufügen einer Überladung ein Bruch?

**Heute: ja, und nichts sagt es** (`q06`/`q06b`/`q07`). Schärfste Form: eine Extension in
`std.core`, die ohne Importzeile in jeder Menge steht (OVL-40).

| Option | Vorbild | Preis |
|---|---|---|
| A: additiv = minor | C#, Java | Minor kann jeden Aufruf umlenken |
| B: major, wenn umlenkbar | — | jede Bequemlichkeits-Überladung wartet auf die Major |
| C: A plus gewöhnliche Warnung „trifft seit Version X ein anderes Ziel" (`@Since`) | — | braucht `@Since`; rauschfrei |
| D: A plus `lyrfix` | — | zwei Bibliotheksversionen |

**Empfehlung: C, in §4.3a ausgesprochen.** Keine §12.5-Warnung. **Bruch: nein.** **Hängt an**:
OVL-18, OVL-40, OVL-24, `lyrfix`.

### OVL-26 — `extern`/nativ in einer Überladungsmenge?

**Heute: `extern` ja und es trägt** (`q17`; gelesen `NameMangling.cs:30-31`, `ModuleLowerer.cs:181-183`,
`NativeRegistry.cs:171-173` Reflexion). **Nativ: Sema ja, ABI nein, aus zwei Gründen** (OVL-18-Zeile:
`_natives`-Dictionary; `_byName`/`TryFind` + `CallHelper`). Die Importzeilen selbst tragen zwei
Signaturen unter einem Namen (`Intern`, `ImportTable.cs:119-124`). Guide `14-embedding.md:141-146`:
.NET wählt über Rückgabetyp.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | `from` unmöglich, ohne dass es irgendwo steht |
| B: Importname bekommt den Überladungssuffix | — | Host-Native-Schlüssel ändern sich; `CallHelper` braucht Signatur |
| C: §4.3a verbietet Überladung für körperlose Deklarationen | — | `from` nie |
| D: C für Natives, A für `extern`, plus §4.4-Satz | — | zwei ABIs, belegbar verschieden |
| E: B für Natives, Rückgabetyp-Widerspruch in §11 benannt | — | die einzige, die OVL-18 freigibt |

**Empfehlung: E wenn OVL-18 in 5.0, sonst D.** **Bruch: major (B/E). Warnstufe: C/D Warnung an
der zweiten körperlosen Deklaration.** **Hängt an**: OVL-18, OVL-14, OVL-41.

### OVL-27 — Wie rankt `never`?

**Heute: passt überall gleich** (`:6026`; `q03`). Lowering-Defekt davor (`q18`, `q18b`).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute; **Kotlin** (`Nothing` als Argument: gewöhnliche Subtyp-Regel → ambig) | Meldung nennt `never`, das niemand schrieb |
| B: `never` zählt bei der Rangbestimmung nicht mit | **kein Vorbild** — Rust hat keine Überladung, Kotlins einzige `Nothing`-Regel betrifft den Empfänger, Swifts `Never` konvertiert nicht | dritter Zustand im Zähler ist eine Lyric-Erfindung |
| C: immer ambig, eigene Diagnose „ein Argument, das nie zurückkehrt, wählt keine Überladung — schreib den Typ hin" | **Kotlin und Swift lehnen den Fall ab** (Swift: Typfehler, Kotlin: Ambiguität) | `f(panic(…))` bleibt bei überladener `f` unschreibbar — und das ist in beiden Vorbildern so |
| D: B, Aufruf entfällt | — | falsch: die Auswahl entscheidet die übrigen Diagnosen |

**Empfehlung: C, mit B als Lyric-eigenem Vorschlag nur dann, wenn ein konkreter std-Fall ihn
braucht — bisher gibt es keinen.** *Die zweite Runde empfahl B mit drei Vorbildern; alle drei
sind falsch.* Erst das Lowering (4.x-Bug). **Bruch: nein (C ändert eine Meldung). Warnstufe:
keine.** **Hängt an**: OVL-01, Gebiet Laufzeitmodell.

### OVL-28 — Substitution macht zwei Überladungen gleich

**Heute: die Frage stellt sich nicht** (`q16` läuft, „T"). Die billige Fassung ohne Empfänger steht
in **OVL-34** (`n10`, `n08`): dort ist Gleichheit bis auf Umbenennung ein reiner
Deklarationscheck ohne Unifikation.

| Option | Vorbild | Preis |
|---|---|---|
| A: am Aufruf ambig | heute für Nicht-Generika | `Box<int64>` unbenutzbar |
| B: an der Deklaration: „für irgendeine Instanziierung gleich" ist `SEM0085` | C# CS0695 (Interfaces), Rust Overlap | Unifikation nötig |
| C: der konkretere gewinnt (Herkunft des Parameters) | C# (§4.3a:97-98 sagt es) | Rangstufe erinnert Herkunft |
| D: A, B als Warnung | — | — |

**Empfehlung: C, B als §12.5-Warnung; OVL-34 vorher.** **Bruch: minor→major.** **Hängt an**:
OVL-04, OVL-34, OVL-03.

### OVL-29 — Filtert Sichtbarkeit beim SELEKTIVEN Import, und darf der Importstil das Ergebnis ändern? *(neu)*

**Heute: nein, und ja.** Gemessen `n04_selimport_vis`: `import mpv { m }; m(3)` → `priv-exact`;
`n04b_qualified`: `mpv.m(3)` → `pub-opt`. Kontrolle `n04c` (nur privat) → `RES0004`;
`n04d` (privat zuerst, `pub` danach) → `RES0004` **obwohl ein öffentliches `m` existiert**.
Ursache gelesen `Resolver.cs:232-246`: `found = LookupLocal(name)` ist das erste Symbol,
`IsPublic(found)` prüft nur dieses, danach `overloads.Select(… ImportBindingSymbol …)` für alle.
§4.2:21 (gelesen): „Only `pub` declarations cross a module boundary." §4.3a:87-89: „a selective
import brings the whole set" — gemeint ist die exportierte Menge, gesagt ist „whole". Das ist
genau der Defekt, den `STATUS.md:985-993` für den qualifizierten Zweig als behoben führt
(„overload resolution depended on the import style") — er lebt im Importzweig weiter.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | Importstil entscheidet die Auswahl; Deklarationsreihenfolge entscheidet, ob der Import überhaupt geht |
| B: `ResolveSelective` importiert **nur die `pub`-Teilmenge**; `RES0004` nur, wenn sie leer ist | jede Sprache mit Sichtbarkeit (C#, Java, Kotlin, Rust `pub use`) | `n04` druckt danach `pub-opt`, `n04d` kompiliert. Ein Programm, das heute läuft, ändert sein Ziel — aber es lief gegen §4.2:21 |
| C: B, plus die Note aus OVL-19/D auch am Import („eine weitere Überladung ist nicht öffentlich") | — | eine Zeile |

**Empfehlung: C, als 4.x-Bugfix mit gewöhnlicher Warnung vorab** — nicht als 5.0-Frage: für freie
Funktionen ist `pub` heute Vertrag (`n04c`), also gibt es keinen offenen Ausgang, den §12.5
schützen müsste. Die Warnung „hier gewinnt ein importierter Kandidat, der nicht öffentlich ist"
kostet eine Bedingung in `SelectOverload` und deckt die eine Minor, in der der Fix reift.
**Bruch: minor** (Bugfix gegen §4.2; Ziele ändern sich nur dort, wo heute Privates gewinnt).
**Warnstufe: gewöhnliche Warnung, eine Minor, dann Fix.** Pin: keiner fällt; neuer Fall
`04-modules/a_selective_import_brings_the_public_set.lyr` (`run`, `since:` die Fix-Version) und
ein `check`-Zwilling für `n04d`. **Hängt an**: OVL-19 (derselbe Warntext), OVL-10, OVL-37.

### OVL-30 — Welche Argumentformen brauchen den erwarteten Typ, und wie stimmen sie im Probelauf ab? *(neu)*

**Heute: der Probelauf läuft ohne erwarteten Typ** (`:2082-2086`, gelesen), und jede Form, die
ihn braucht, scheitert vor der Auswahl:

| Form | ohne Überladung | mit zwei Kandidaten | Beleg |
|---|---|---|---|
| leeres Array-Literal `[]` | typisiert über den Parametertyp (`n03b` → „ints") | `SEM0087 (<error>[])` | gemessen `n03` |
| `null` | `f(null)` → `?T` (`p15`) | `SEM0086 for (null)` — beide `?T` passen gleich | gemessen `n02` |
| Enum-Variante ohne Typargumente `Opt.Some(7)` | typisiert (`n12b` → „int"; `CheckTargetOfCall` `:2204-2206` nennt genau diesen Fall) | `SEM0063` „write its type arguments" | gemessen `n12` |
| Struct-Literal ohne Typargumente `Box { v = 1 }` | typisiert (`n13b`) | `SEM0026` „… or use it where the type is known" | gemessen `n13` |
| Lambda | typisiert über den Parametertyp | kein Votum (`:2163`) | gemessen `p14`, `n01` |
| `panic(…)`/`throw` | `never` | passt überall gleich | gemessen `q03` |
| Map-Literal | — | **gibt es nicht** (`Grammar.md:502`: nur `ArrayLit`) | gelesen |

Sechs Formen, sechs verschiedene Ausgänge (`SEM0087`, `SEM0086`, `SEM0063`, `SEM0026`, kein
Votum, Gleichstand). §4.3a nennt nur das Lambda.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben; §4.3a listet die Formen und sagt, dass sie nicht abstimmen | — | ehrlich; sechs Meldungen für ein Konzept bleiben |
| B: **eine Klasse „kontextabhängiges Argument"** (Lambda, `[]`, `null`, typarglose Variante/Literal, `never`): stimmt nicht ab, aber **filtert nach Form** — `[]` passt nur in Array-Parameter, `null` nur in `?T`, eine Variante nur in ihr Enum, ein Lambda nur in `fn(…)` | **Java** „poly expressions" (JLS §15.2: Lambda, Methodenreferenz, generischer Aufruf, bedingter Ausdruck sind kontextabhängig und „not pertinent to applicability", aber ihre Form filtert) | eine Regel statt sechs; `n03` wird gültig (beide Kandidaten sind Arrays → ambig mit ehrlicher Meldung), `n12` wählt (`Opt<int>` vs `Opt<string>` — nur die Form `Opt` filtert, dann ambig), `n01` wählt |
| C: B, und danach Probetypisierung **mit dem Parametertyp des Kandidaten** für die verbleibenden (OVL-12/D) | Roslyn (Lambdas), Java (Phase 2 mit Zieltyp) | `n03`/`n12`/`n13` werden entscheidbar; pro Kandidat ein Lauf für diese Formen |
| D: Ausweg-Syntax statt Auflösung (OVL-16/D Askription) | Rust `let x: T` | verschiebt das Problem zum Aufrufer |

**Empfehlung: B in 5.0 mit einem Satz in §4.3a, C dort, wo B ambig bleibt und die std es
braucht (OVL-18 hat heute keinen solchen Fall), D für `null` (OVL-32).** Die Meldung für eine
kontextabhängige Form muss die Form nennen, nicht `<error>`. **Bruch: minor (heute abgelehnte
Aufrufe werden gültig; ein heute gewählter kann sich nicht ändern, weil heute keiner gewählt
wird). Warnstufe: keine.** Pins: `a_broken_argument_reports_itself` bleibt grün; neue
`check`-Fälle je Form. **Hängt an**: OVL-12, OVL-13, OVL-32, OVL-27, OVL-20, OVL-37.

### OVL-31 — Ist `v as T` über `Into<T>` die in-Sprache-Ausnahme zur Regel „nie über den Rückgabetyp"? *(neu)*

**Heute: ja, und die Spec weiß es nicht.** §3.6/3 (`spec/03-types.md:167-169`, gelesen): „`v as T`
where the type of `v` conforms to `Into<T>`: the cast IS the call `v.into()`, resolved like any
method (chapter 6's operator rule)." §4.3a:83-85: „told apart by what they TAKE, never by what
they give back." Gemessen `n05_into2`: `struct W :: [Into<int>, Into<string>]` mit zwei `fn into()`
→ `SEM0085` an der zweiten Deklaration **und** `SEM0086: 'into' is ambiguous for ()` am Cast
`w as string`, obwohl das Ziel `string` eindeutig ist. Kontrolle `n05b` (eine Konformanz) → „w".
Zwei Folgen: (1) ein Typ kann heute nur **ein** `Into<T>` tragen, und §3.6 sagt es nirgends;
(2) die Meldung nennt `into`, das der Nutzer nie schrieb, und `()` als Argumentliste eines Casts.

Vergleich: §5.1 (`05-interfaces.md:43-46`) erlaubt `Mul<Vec2,Vec2>` **und** `Mul<float,Vec2>` auf
einem Typ, weil der Operator über den rechten Operanden — ein Argument — wählt. `Into<T>` hat
kein Argument; sein Unterscheider ist der Rückgabetyp. Das „chapter 6's operator rule" in §3.6 ist
deshalb falsch zitiert: die Operatorregel wählt über ein Argument, das `into()` nicht hat.

| Option | Vorbild | Preis |
|---|---|---|
| A: **ein `Into<T>` je Typ**, in §3.6 ausgesprochen; `SEM0085` bekommt einen Text für diesen Fall („`Into<int>` and `Into<string>` need two `into` with one parameter list — a type converts to one target") | C# (eine `implicit operator` je Zielpaar ist erlaubt — also **nicht** A; C# ist Vorbild für B) | ehrlich, billig; `W` kann nicht nach `int` und `string` konvertieren |
| B: `Into<T>` ist die eine Stelle, an der der **Rückgabetyp trennt** — `into(): int` und `into(): string` sind zwei Slots, `as T` wählt nach `T` | **C#** (`implicit operator int(W)` neben `implicit operator string(W)`), Rust (`impl From<W> for i32` neben `impl From<W> for String` — wählt über den Zieltyp) | eine Ausnahme in §4.3a:83-85 und in `CheckOverloadSets` (Konformanz-Member mit `Self`-Rückgabe); der vtable-Slot ist pro Interface-INSTANZ ohnehin getrennt (§5.4:133-137) |
| C: B, und die Ausnahme wird zur **Regel für alle `Self`-rückgebenden Interface-Member** — dieselbe Regel, die OVL-05/C für `T.parse` braucht | Rust (`FromStr`, `From`, `Default` — alle über den Zieltyp) | die Wertpositions-Regel (§4.3a:123-126), `as` und `T.parse` werden **eine** Regel: „ein Member, dessen Instanz nur der Rückgabetyp bestimmt, wird vom Kontext gewählt" |

**Empfehlung: C — und die Antwort gehört in §4.3a (als benannte Ausnahme mit Verweis), in §3.6
(Korrektur des Operatorregel-Verweises) und in §5.1 (zweite Konformanz eines Interfaces).** A ist
der Notausgang, wenn die Interface-Runde `Self` nicht liefert. **Bruch: minor** (heute abgelehnte
Deklarationen werden gültig; `n05b` ändert sich nicht). **Warnstufe: keine.** Pins:
`overload_same_parameters_refused` bleibt grün (freie Funktionen); neuer `run`-Fall
`03-types/two_into_conformances_choose_by_target.lyr`. **Hängt an**: OVL-05, OVL-15, Gebiet
Typen (§3.6), Gebiet Interfaces (§5.1).

### OVL-32 — Wie rankt `null` zwischen zwei `?T`-Kandidaten, und welchen Ausweg hat der Aufrufer? *(neu)*

**Heute: gleich, und der Ausweg ist ein `let`.** Gemessen `n02_null`: `f(?int)`/`f(?string)`,
`f(null)` → `SEM0086 for (null)`; `n14_nullcast`: `f(null as ?int)` → `SEM0006` (§3.6 kennt keinen
Cast nach `?T`; die Meldung schlägt sogar `Into<?int>` auf `null` vor); `n14b`: `let x: ?int =
null; f(x)` → „opt-int". OVL-16 behandelte nur Zahlen und Interfaces.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute; **C#** (`f(null)` mit `f(int?)`/`f(string)` ist CS0121, Ausweg `(int?)null`); **Kotlin** (ambig, Ausweg `null as Int?`) | Lyric hat den Ausweg beider Vorbilder nicht: `as` konvertiert nicht nach `?T` |
| B: `null as ?T` wird in §3.6 als **vierter Fall** erlaubt (identity, kein Laufzeitcode) | Kotlin | `as` bekommt einen Fall ohne Konversion — das ist die Askriptionsfrage aus OVL-16/D durch die Hintertür |
| C: Askription (OVL-16/D) — `(null: ?int)` oder `null as! ?int` | Rust `None::<i32>`, Scala | neue Syntax; sauber getrennt von `as` |
| D: benannte Argumente (OVL-06) — trennen den Fall nicht, da beide `x` heißen könnten | — | kein Ausweg |

**Empfehlung: C, mit B als Übergang nur dann, wenn OVL-16/D abgelehnt wird — und dann muss §3.6
sagen, dass dieser eine `as`-Fall keine Konversion ist.** `null` ist die erste Stelle, an der
Lyric eine Askription **braucht** statt nur wünscht. **Bruch: nein.** **Warnstufe: keine.**
**Hängt an**: OVL-16, OVL-30, OVL-06, Gebiet Typen (§3.6), Gebiet Optionals.

### OVL-33 — Darf ein Lambda-Argument einen Kandidaten passieren, dessen Parameter kein Funktionstyp ist? *(neu)*

**Heute: ja.** Gemessen `n01`: `f(x: int)` neben `f(g: fn(int)->int)`, `f(x => x)` →
`SEM0086 ambiguous for (a lambda)`; Kontrolle `n01b` → „fn". Gelesen `TypeChecker.cs:2163`:
`if (argumentTypes[i] is not { } argument) continue;` — ein Lambda stimmt nirgends ab, auch nicht
dort, wo die Parameter-ART allein entscheidet. §4.3a:119-121 begründet mit Zirkularität; ein
Filter nach Art ist nicht zirkulär, weil er den Rumpf nicht ansieht.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | jede Überladung neben einem Nicht-Funktionsparameter ist mit Lambda unaufrufbar |
| B: **Parameter-ART filtert**: Kandidat fällt raus, wenn der Parameter kein `FnType` ist (und, mit OVL-22, wenn ein Block-Argument kein einstelliges `fn` trifft) | C#, Java, Kotlin (dort über Konvertierbarkeit implizit) | null Kosten; deckt `n01`, nicht `p14` |
| C: B plus Arität (OVL-13/B) | Java | s. OVL-13 |

**Empfehlung: B sofort als 4.x-Fix — es gibt keine Gegenposition: ein Lambda in einem
`int`-Parameter war nie gültig, also kann kein Programm sein Ziel ändern.** OVL-13 bleibt die
5.0-Frage für den Rest. **Bruch: nein** (heute `SEM0086`, danach gültig; kein Programm wechselt
das Ziel). **Warnstufe: keine.** Pin: neuer `run`-Fall
`04-modules/a_lambda_passes_only_a_function_parameter.lyr`, `since:` Fix-Version.
**Hängt an**: OVL-13, OVL-22, OVL-30.

### OVL-34 — Wann sind zwei generische Parameterlisten „gleich"? *(neu)*

**Heute: nach Symbol, also nie.** Gelesen `TypeChecker.cs:353-362`: `SameParameters` vergleicht
mit `LyrType.Equal`, das `TypeParamType` nach Symbol vergleicht (§3.7: „by symbol for … type
parameters", `spec/03-types.md:174-177`). Gemessen `n10`: `fn f<T>(x: T)` neben `fn f<U>(x: U)`
kompiliert an der Deklaration und ist an **jedem** Aufruf `SEM0086` (Notes „(T)" und „(U)");
`n08`: `f<T>(T)` neben `f<T,U>(T)` ebenso, auch mit `f<int,int>(1)`, Notes **zweimal „(T)"**.
Zwei Funktionen, die kein Aufruf je trennen kann, sind heute keine Redeklaration — und die
Kandidatenanzeige (`DisplayParameters`, `:2199-2200`) zeigt weder Typparameter noch Constraints
(`p02`: zweimal „(T)" trotz verschiedener Constraints).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | stumme Deklaration, jeder Aufruf tot |
| B: **Alpha-Äquivalenz** an der Deklaration: Parameterlisten gleich bis auf Umbenennung der Typparameter → `SEM0085` | C# (CS0111: „already defines a member with the same parameter types" — Typparameter positionell), Java (Erasure macht es noch strenger) | ein Vergleich über Positionen statt Symbole; keine Unifikation |
| C: B, und **Typparameter-Arität trennt** (`f<T>` und `f<T,U>` sind verschieden, gewählt über OVL-03/B) | C# (Arität ist Teil der Signatur: `F<T>` und `F<T,U>` sind zwei Methoden) | ohne OVL-03/B bleiben sie an jedem Aufruf ambig — C muss mit OVL-03 kommen |
| D: C, und **Constraints trennen** (`f<T::[A]>` neben `f<T::[B]>`) | C++20 Concepts (Subsumption), Rust (kein Overlap ohne Spezialisierung) | erst mit OVL-02 nutzbar; Spezifität zwischen Constraints braucht eine Ordnung (Subsumption) |

**Empfehlung: C, mit D nur so weit, wie OVL-02 sie trägt (Constraints trennen, aber zwei
Kandidaten, die beide passen, sind ambig — keine Subsumption).** Dazu zwei Anzeige-Pflichten:
`SEM0086`/`SEM0087`-Notes zeigen Typparameter (`f<T>(T)`) und Constraints (`f<T :: [Named]>(T)`).
**Bruch: minor** (heute stumm akzeptierte Deklarationen werden `SEM0085`; kein laufendes Programm
betroffen, weil kein Aufruf sie trennt). **Warnstufe: eine Minor als Warnung.** Pin:
`overload_same_parameters_refused` bleibt; neuer `check`-Fall
`08-generics/renamed_type_parameters_are_one_signature.lyr`. **Hängt an**: OVL-28 (die teure
Fassung), OVL-03, OVL-02, OVL-12 (Anzeige).

### OVL-35 — Löst ein überladener Aufruf mit `T`-typisiertem Argument vor oder nach der Monomorphisierung auf? *(neu)*

**Heute: vor.** Gemessen `n11`: `fn h<T>(x: T): string { return k(x); }` mit `k(int)`/`k(string)`
→ `SEM0087: no 'k' takes (T)`; Kontrolle `n11b` (`h(x: int)`) → „int". Gelesen: `FitOf` sieht
`argument = T` und `wanted = int`, `IsAssignable` sagt nein; ein Constraint-Member wäre der Weg
(§8.1, `spec/08-generics.md:3-24`: Monomorphisierung, „constrained calls are DIRECT calls after
monomorphization"). §4.3a und §8 sagen zur Reihenfolge nichts.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben — Auflösung vor der Instanziierung, `T` passt nur in `T`-Parameter oder Constraint-Member | **C#**, **Rust** (Trait-Bound entscheidet, nicht der konkrete Typ), Swift | ehrlich zur Monomorphisierung als Implementierung, nicht Semantik; `k(x)` braucht ein Interface |
| B: Auflösung **nach** der Instanziierung (pro `T`) | **C++** (Templates, two-phase lookup) | ein generischer Rumpf ist erst am Aufruf prüfbar; die Fehlerwand von C++; widerspricht §8.1:14 („generic function is not a function value") im Geist |
| C: A, aber `SEM0087` sagt, dass `T` ein Constraint braucht („`k` takes `int` or `string`; `x: T` needs `T :: [SomeInterface]` — or an overload `k<T>(T)`") | — | Diagnosearbeit |

**Empfehlung: A + C, und ein Satz in §4.3a: „a type parameter is a type of its own for the
choice; the body of a generic function is checked once, before any instantiation."** B wäre der
größte Bruch des Gebiets und der einzige, der eine Vergleichssprache nur in C++ hat. **Bruch:
nein.** **Warnstufe: keine.** Pin: neuer `check`-Fall
`08-generics/a_type_parameter_chooses_no_overload.lyr` (`SEM0087`). **Hängt an**: OVL-04, OVL-02,
Gebiet Generics (§8.1).

### OVL-36 — Welche Oberflächen zeigen den gemangelten Namen, und wer außer Lyrvm liest ihn? *(neu)*

**Heute, gemessen und gelesen:**

| Oberfläche | Beleg | zeigt |
|---|---|---|
| Panic-Trace | gemessen `n07_trace`: `panic [LYR-VM0011]: boom / in main.g(int) (n07_trace.lyr:1)` | den gemangelten Namen mit Suffix |
| VM-Fehlertexte | gelesen `src/Lyric.Vm/Interpreter.cs:535,677,727,806,813,854`: `frame.Fn.Source.Name` | dito |
| Debugger (DAP) | gelesen `src/Lyric.Vm/Debugging/DebugController.cs:461,469`: `DebugFrameInfo(…, frame.Fn.Source.Name, …)` | dito, im Stack-Panel |
| Host-API | gelesen `ScriptInstance.cs:205-208`: `LYR-EMB0008` nennt „full name" und verlangt ihn zurück | der Host **schreibt** den Namen hin |
| DocGen | gelesen `STATUS.md:994-996`: Anker `#fn-close`, `#fn-close-2` — DocGen schlüsselt nach Name **und Ordinal**, nicht nach Suffix | ein zweites, eigenes Schema |
| lyrtest-Ausgabe | behauptet: nennt Testfunktionen beim Quellnamen; nicht gemessen |
| Profiler | gelesen: es gibt keinen (`src/` hat kein Profiler-Projekt; `PLAN.md:345` führt ihn als Wunsch) |
| fremder Loader | **behauptet** (Sitzungsgedächtnis): erato2 hat ein eigenes Runtime „Lyricpp", das `.lyrbc` liest; `STATUS.md:897-900` belegt nur einen DLL-Loader in erato2 — der Anker der Kritik trägt die Behauptung nicht |

Ein Wechsel nach OVL-14/B' ändert die Namen, die alle acht zeigen — B' nur dort, wo heute schon
`#1` steht; B überall, wo überladen wird.

| Option | Vorbild | Preis |
|---|---|---|
| A: der gemangelte Name ist die Anzeigeform, überall | heute; Lua (`function <file:line>`), Python (`f`) | jede Änderung am Mangling ist eine Änderung an Traces, Debugger und Docs |
| B: **getrenntes Anzeigefeld** im Format (Quellname + Parameterliste als Text), der gemangelte Name ist nur Schlüssel | .NET (Metadaten-Name vs. `MethodBase.ToString()`), C++ (Demangler) | ein Feld je Funktion; Trace, DAP und `LYR-EMB0008` lesen das Anzeigefeld, Host-API und Loader den Schlüssel |
| C: A, aber das Schema ist in §11/§13 als **Vertrag** festgeschrieben (Suffix-Grammatik, Ordinal-Regel), sodass ein fremder Loader es nachbauen kann | — | bindet OVL-14 an heute; der Ordinal-Defekt (`q04b`) wird Vertrag |

**Empfehlung: B zusammen mit OVL-14/B in 5.0; bis dahin B' (OVL-14) und ein Satz in §11, dass
der volle Name kein stabiler Vertrag ist.** Ohne B ist jeder Mangling-Wechsel ein Bruch in acht
Oberflächen; mit B ist er einer im Format. **Bruch: major (Format) für B; nein für den
§11-Satz.** **Warnstufe: keine möglich (Formatseite).** **Hängt an**: OVL-14, OVL-41, Gebiet
Bytecode (§13), Gebiet Embedding (§11), Gebiet Werkzeuge.

### OVL-37 — Welcher Konformanzfall fällt, welcher kommt, mit welchem `since:`-Gate? *(neu)*

**Heute gepinnt** (gelesen `lyric-spec/conformance/cases/`, Köpfe): zehn Fälle berühren §4.3a —
`04-modules/`: `overload_same_parameters_refused` (`SEM0085`, 3.0.0), `params_does_not_separate_an_overload`
(`SEM0085`, 3.0.0), `two_extensions_of_one_signature_refused` (`SEM0044`, Aufruf-Form),
`overload_exact_beats_conversion` (`run`, 3.0.0, Regel 1), `static_methods_overload` (`run`, 3.0.1),
`two_extensions_overload_by_parameters` (`run`, 3.0.1, **auf `struct Box`**),
`a_broken_argument_reports_itself` (`SEM0002`, 3.0.1); `05-interfaces/`: `overloaded_member_refused`
(`SEM0088`), `a_second_conformance_answers_a_member_call` (`run`), `two_conformances_from_two_own_overloads`
(`run`, 3.0.1). **Nicht gepinnt**: Regeln 2–5, `SEM0086`, `SEM0087` (als Aufruf ohne Fehlerargument),
`SEM0089`, die Wertposition, die Sichtbarkeit, das Mangling. Der spec-first-Modus
(Sitzungsgedächtnis: Regel-PR mit `since:` zuerst, dann Zwilling, dann Release+Pin) verlangt die
Antwort je Frage:

| Frage | fällt | kommt (`since:` = Version der Regel) |
|---|---|---|
| OVL-01/C | — | `run`: ganzzahliges Literal wählt Ganzzahlfamilie |
| OVL-02/B | — | `run`: Constraint entfernt Kandidaten |
| OVL-03/C | — | `run`: Typargumente wählen; **Warnpin** (§12.5) vorher |
| OVL-04/B | — | `run`: Empfänger substituiert; Warnpin vorher |
| OVL-05/C, OVL-31/C | — | `run`: `T.parse`; `run`: zwei `Into<T>` |
| OVL-06/B | — | `run`+`check`: benannte Argumente filtern |
| OVL-07/B | — | `check`: Extension verdeckt (Warnung); `run`: Member gewinnt; Warnpin vorher |
| OVL-08/B | — | `run`: Default überladen mit |
| OVL-09/B | **`overloaded_member_refused` retiriert MIT der Regel** | `run`: Interface-Überladung |
| OVL-10/C | — | `check`: Kollision am Import |
| OVL-11/C | `two_extensions_of_one_signature_refused` bleibt grün, wenn der Code `SEM0044` bleibt (Span-Prüfung: behauptet, nein) | `check`: Deklarations-Form im selben Modul |
| OVL-12/E+B | `a_broken_argument_reports_itself` bleibt | `check`: `[]` meldet sich selbst |
| OVL-13/B+D, OVL-33/B | — | `run`: Art filtert; `run`: Arität wählt |
| OVL-14/B', OVL-36/B | — | Format-Fall im Bytecode-Gebiet (§13) |
| OVL-15/B | `a_second_conformance_answers_a_member_call` bleibt | `run`: `v * 2` mit `Mul<?int,V>` |
| OVL-19/D, OVL-29/C | — | `run`: private Überladung verlässt die Menge (Member; Import); Warnpin vorher |
| OVL-21/B | `overload_same_parameters_refused` bleibt | `run`: `static` neben Instanz |
| OVL-22/B | — | `run`: Block-Form mit Parameterliste |
| OVL-23/B | — | `run`: weniger Defaults gewinnt |
| OVL-25/C | — | `check`: Umlenkungswarnung |
| OVL-26/E | — | Format-Fall (§13) |
| OVL-27/C | — | `check`: eigene Diagnose |
| OVL-28/C | — | `run`: konkreter gewinnt nach Substitution; Warnpin vorher |
| OVL-30/B | — | `check`/`run` je Form |
| OVL-32/C | — | `run`: Askription |
| OVL-34/C | — | `check`: Umbenennung = eine Signatur |
| OVL-35/A | — | `check`: `T` wählt keine Überladung |
| OVL-40 | **`two_extensions_overload_by_parameters` bleibt (Struct); für Primitive fehlt der Pin, den §4.3a:113-114 verspricht** | `run`: Extension-Überladung auf `int` |

**Empfehlung: die Tabelle ist die Antwort; jede Frage, die im ADR eine Antwort bekommt, bekommt
in derselben PR ihre Zeile mit `since:`.** Zwei Regeln fallen auf: nur OVL-09 retiriert einen
Pin, und die fünf Warnpins (§12.5) müssen vor ihren `run`-Fällen kommen. **Bruch: nein
(Prozess).** **Hängt an**: alle.

### OVL-38 — Was tun Rename und Find-References mit einer Überladungsmenge? *(neu)*

**Heute: sie sehen EINE Funktion.** Gelesen `src/Lyric.Lsp/Analysis/RenameProvider.cs:85`:
`if (!ReferenceEquals(ReferenceProvider.Target(bound), symbol)) continue;` — Filter auf ein
`FunctionSymbol`; `:112` dasselbe für Importklauseln. Ein Rename benennt also **eine** Überladung
und ihre gebundenen Aufrufe um und **spaltet die Menge** — die Importklausel `import m { f }`
wird mitbenannt (`:106-117`), obwohl sie die ganze Menge meint, und die anderen Überladungen
verlieren den Import. Ambige oder Wertpositions-Referenzen ohne `BindRef` fallen auf
`Binding.Resolve` zurück (`ReferenceProvider.cs:99`: `model.Types.RefOf(path[i]) ?? model.Binding.Resolve(path[i])`),
also auf die erste Funktion des Namens. Nicht gemessen (LSP-Sitzung nötig): **behauptet**, dass
das Ergebnis ein nicht kompilierendes Programm ist, wenn eine Menge über einen selektiven Import
kommt.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | Rename spaltet Mengen; Find-References findet nur die gebundene Überladung |
| B: **Rename = die ganze Menge** (alle Deklarationen des Namens im Scope plus alle Aufrufe plus Importklauseln), Find-References über die Menge mit Markierung „gebunden an diese" | **C#/Roslyn** (Rename auf Überladung fragt „rename overloads?", Default ja), rust-analyzer (keine Überladung; Trait-Impls werden mitbenannt) | die Kandidatenmenge muss im Modell stehen (OVL-17) |
| C: B, aber Rename einer einzelnen Überladung als **explizite Option** („nur diese") | Roslyn | ein Dialog mehr |

**Empfehlung: C, gehört zu OVL-17.** Ein Rename, der Mengen spaltet, ist kein Werkzeug, sondern
ein Bug. **Bruch: nein.** **Warnstufe: entfällt.** **Hängt an**: OVL-17, OVL-10, OVL-29.

### OVL-39 — Welche Seitentabellen schreibt der Probelauf unter `_de.Mute()`, und überschreibt der echte Lauf jede? *(neu)*

**Heute, gelesen** (`src/Lyric.Frontend/Sema/TypeResult.cs`): der Probelauf ruft `CheckExpr` auf
jedes Nicht-Lambda-Argument (`TypeChecker.cs:2082-2086`); `CheckExpr` schreibt in

| Tabelle | Schreibart | Verhalten beim Zweitlauf |
|---|---|---|
| `_types` (`SetType`, `:24,35`) | `_types[expr] = type` — **überschreibt** | letzter Lauf gewinnt |
| `_refs` (`BindRef`, `:25,51`) | überschreibt | letzter Lauf gewinnt |
| `_throwingPulls` (`:98,100`), `_captures` (`:131,133`), `_typeArguments` (`:189,192`), `_operatorCalls` (`:171`) | überschreibt | dito |
| `_exhaustiveMatches` (`:33,125`), `_boxed` (`:150,152`), `_rebound` (`:204,206`) | **`HashSet.Add` — additiv, nie gelöscht** | ein Eintrag aus dem Probelauf bleibt, auch wenn der echte Lauf ihn nicht setzen würde |
| Diagnosen | `_de.Mute()` verwirft | s. OVL-12 |

Der Probelauf typisiert **ohne erwarteten Typ** (OVL-30); der echte Lauf des Gewinners typisiert
**mit** dem Parametertyp. Für die additiven Mengen heißt das: ein `MarkBoxed` oder
`MarkMatchExhaustive`, das nur im kontextlosen Lauf gesetzt wurde, überlebt. Ob das heute einen
messbaren Fehler erzeugt: **behauptet, nein** — die drei Mengen hängen an Symbolen und
Match-Knoten, deren Entscheidung nicht vom erwarteten Typ abhängt; **aber es steht nirgends**, und
jede Tabelle, die OVL-20/B memoisiert, erbt die Frage. Ein Rest aus dem Probelauf eines
**Verlierers** kann es nicht geben, weil der Probelauf nicht pro Kandidat läuft — noch nicht
(OVL-12/D, OVL-13/C würden das ändern).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben; die Invariante „Zweitlauf überschreibt alles" wird nicht geprüft | heute | stiller Fehler, sobald eine Tabelle additiv und kontextabhängig wird |
| B: **Probelauf schreibt in einen Scratch-`TypeResult`**, der nach der Auswahl verworfen wird; der Gewinner schreibt in den echten | Roslyn (Bindung in einen `BindingDiagnosticBag`/temporären Binder, dann Übernahme), rustc (`probe` mit Snapshot/Rollback der Inferenztabellen) | ein Objekt pro Aufruf mit Überladung; Voraussetzung für OVL-20/B (Memoisierung des Scratch-Ergebnisses) und für OVL-12/D (pro Kandidat) |
| C: B, und der Zweitlauf entfällt: das Scratch-Ergebnis des Gewinners wird **übernommen** statt neu gerechnet | rustc (commit des Snapshots) | genau OVL-20/B; verlangt, dass der Probelauf mit erwartetem Typ läuft (OVL-30/C), sonst ist das Ergebnis ein anderes |

**Empfehlung: B als Voraussetzung für OVL-20 und OVL-12/D; C ist OVL-20/B selbst.** Das ist eine
Implementierungsfrage — aber eine, ohne die zwei Sprachfragen (OVL-20, OVL-12) nicht
spezifizierbar sind. **Bruch: nein.** **Hängt an**: OVL-20, OVL-12, OVL-30.

### OVL-40 — Stehen `std.core`-Extensions ohne Import in jeder Kandidatenmenge — und gibt es auf Primitiven überhaupt eine Menge? *(neu)*

**Heute: ja, und nein.** Gelesen `src/Lyric.Frontend/Resolver/Compilation.cs:72-84` (`Sees`):
„'std.core' is always visible without an import." Gemessen `n15k`: `n.show()` ohne Import →
„3" (std.core `Display.show`, `stdlib/std/core.lyr:106-110`); `n15j`: eigene `extend int { fn
show() }` **ohne Import** → `SEM0044` gegen std.core; `n15l`: ohne Aufruf stumm. Eine in einer
Minor hinzugefügte `std.core`-Extension steht damit in jeder Kandidatenmenge auf diesem Typ, ohne
dass eine Importzeile es ankündigt — die schärfste Form von OVL-25.

**Und schärfer als die Kritik es sah, in eine andere Richtung:** gemessen `n15m` (eigene
`show(int)` neben std.core `show()`): `n.show()` → **`SEM0014: expects 1 argument(s), got 0`** —
keine Auswahl, der erste sichtbare Treffer wird allein geprüft. Kontrolle `n15n` (zwei eigene
Blöcke `twice()`/`twice(int)` auf `int`, ohne std.core im Spiel) → `SEM0014`; Kontrolle `n15o`
(dieselben Blöcke auf `struct S`) → 6 / 30. Ursache gelesen: `OverloadCandidates` fragt
`TypeFacts.SymbolOf(ReceiverTypeOf(mem))` (`TypeChecker.cs:1990`), das für `PrimitiveType` `null`
ist (`TypeFacts.cs:97-102`) → `default: return []` (`:2002`); die Einzelsuche läuft über
`BuiltinSymbol` (`:3420-3429`) in `ExtensionMember`, dessen Kommentar es sagt: „The first one
wins" (`:3871-3889`). **§4.3a:113-114 („Extensions of one name are one set") gilt auf `int`,
`string`, `float`, `bool`, `char` nicht.** Der Pin `two_extensions_overload_by_parameters` läuft
auf `struct Box` und sieht es nicht. Für OVL-18 heißt das: keine `string`-Extension der std
kann je überladen werden.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben | heute | `std.core` kann jeden Aufruf umlenken; auf Primitiven gewinnt die Registrierungsreihenfolge |
| B: **Primitive bekommen dieselbe Menge** (`OverloadCandidates` über `BuiltinSymbol`) — reiner Bugfix gegen §4.3a:113-114 | — | eine Zeile; `n15m`/`n15n` werden Auswahl statt `SEM0014`. Ein Programm, das heute läuft, kann sein Ziel ändern, wo heute der erste Treffer zufällig passte und ein besserer existierte |
| C: `std.core` ist sichtbar, aber seine Extensions **verlieren jeden Gleichstand** gegen eigene und importierte (Rangstufe unter Regel 5) | Kotlin-Turm (importierte Extension unter lokaler) | eine Rangstufe, die an der Herkunft hängt; `n15j` wird zur Wahl der eigenen statt `SEM0044` |
| D: B, und OVL-07/B gilt **auch gegen `std.core`** („Member gewinnt strikt"); Gleichstand zwischen `std.core` und eigener Extension bleibt `SEM0044` | Kotlin | ehrlich: wer `show()` auf `int` neu definiert, kollidiert mit `Display` und soll es wissen |
| E: D, und `std.core`-Extensions sind nur sichtbar, wo ein **Interface** sie verlangt (Display für f-Strings), nicht als freie Member | — | `n15k` bricht (`n.show()` ohne Import); dafür kann `std.core` nichts umlenken |

**Empfehlung: D — B sofort als 4.x-Fix (kein offener Ausgang: §4.3a sagt es), die
`std.core`-Antwort in OVL-07 mit.** E ist der Notausgang, wenn OVL-25 für `std.core` nicht
tragbar ist. **Bruch: minor für B** (Ziele ändern sich nur, wo heute die Reihenfolge zufällig
entschied); **nein für D**. **Warnstufe: für B eine Minor gewöhnliche Warnung „auf einem
Primitiv steht eine zweite Extension dieses Namens, die heute nicht gewählt wird".** Pin: neuer
`run`-Fall `04-modules/two_extensions_overload_on_a_builtin.lyr`. **Hängt an**: OVL-07, OVL-11,
OVL-18, OVL-25.

### OVL-41 — Host-Seite: wie ruft ein Host eine `params`-Funktion oder eine mit Defaults? *(neu)*

**Heute: gar nicht.** Gelesen `ScriptInstance.cs:191-213`: `ResolveOverload` matcht
`candidate.ParamCount == argumentCount` — die **volle** Stelligkeit der kompilierten Funktion.
Default-Argumente und ein `params`-Schwanz sind Aufrufstellen-Transformationen (§4.3a:85-87,
§7.1): die kompilierte Funktion trägt alle Parameter, ein `params xs: int[]` einen
Array-Parameter. Ein Host, der `f(1)` für `fn f(a: int, b: int = 0)` ruft, bekommt
`LYR-EMB0006` („no function 'f' taking 1 argument(s)"; behauptet aus dem Code `:210-212`, nicht
gemessen — ein Host-Programm ist in dieser Umgebung nicht baubar); ein `params`-Aufruf müsste das
Array selbst packen, und §11 sagt nicht, ob er das kann (`MarshalArguments`, `:215`; gelesen, dass
ein Array-Parameter marshalt wird: behauptet). *Die zweite Runde nannte nur Defaults.* Und §11
sagt nirgends zu, dass eine Lyric-Überladung für den Host **erreichbar** ist — `LYR-EMB0008`
verlangt den vollen Namen, den OVL-14 als instabil zeigt.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleiben; §11 sagt „der Host ruft die volle Stelligkeit, Defaults und `params` sind Quelltext-Komfort" | JNI (kein Default, kein Vararg-Komfort), Lua C-API | ehrlich, billig; der Host schreibt `f(1, 0)` und packt das Array |
| B: der Host bekommt die **Aufrufstellen-Transformation nachgebaut**: fehlende Argumente → Defaults (die Konstanten müssen im Format stehen), überzählige → Array | .NET Reflection (`Invoke` mit `Type.Missing`), Python C-API (kwargs) | Defaults ins Format (heute Aufrufstellen-Inlining, nicht in der Funktion); `params`-Packen im Marshaller |
| C: A für 4.x, B mit OVL-14/C (typisierte Auflösung) in 5.0 | — | zwei Schritte |

**Empfehlung: A jetzt in §11 (das ist ein fehlender Satz, keine Entscheidung), B nur mit OVL-14/C
und nur, wenn ein Host-Fall es verlangt — Erato ist der einzige Host, und er ruft über Bytecode
(behauptet).** **Bruch: nein für A; major für B (Format trägt Defaults).** **Warnstufe:
entfällt.** **Hängt an**: OVL-14, OVL-36, OVL-23, Gebiet Embedding (§11), Gebiet Bytecode (§13).

---

## 4. Was wir übernehmen sollten

| Von wem | Was | Wofür |
|---|---|---|
| **C#** | Kandidaten mit verletzten Constraints (seit 7.3) und unpassender Typargument-Arität vor dem Ranking entfernen | OVL-02, OVL-03, OVL-34 |
| **C#** | paarweise bessere Konversion mit Familienpräferenz für Literale | OVL-01 |
| **C#** | benannte Argumente filtern die Anwendbarkeit | OVL-06 |
| **C#** *(als Kontrast)* | Default-Tiebreak als **Flag** — CS0121 bei zwei Kandidaten mit Defaults; **das ist Lyrics heutige Regel** | OVL-23/A |
| **C#** | `implicit operator` je Zieltyp: der Zieltyp wählt die Konversion | OVL-31/B |
| **C#/Roslyn** | Argumente einmal binden, Lambdas pro Kandidat mit Cache | OVL-20/B, OVL-13/C |
| **C#/Roslyn** | Rename fragt „overloads mitbenennen?" | OVL-38 |
| **C#** *(als Warnung)* | der 7.3-Wechsel kam als Bruch ohne Warnstufe | OVL-02, OVL-24 |
| **Kotlin** | Member schlägt Extension strikt, Warnung „shadowed" | OVL-07, OVL-40 |
| **Kotlin** | **Zahl** der nicht angegebenen Defaults, weniger gewinnt | OVL-23/B |
| **Kotlin** | Lambdas aus der Inferenz ausschließen, bis die Auflösung steht | OVL-13 (Grenze, nicht Vorbild für Arität) |
| **Kotlin** | Parameterliste in der Block-Form | OVL-22 |
| **Kotlin, Swift** *(als Vorbild fürs Ablehnen)* | `Nothing`/`Never` wählt keine Überladung — beide lehnen den Fall ab | OVL-27/C |
| **Java** | JLS §15.12.2.2: implizit typisierte Lambda „not pertinent to applicability", Form filtert | OVL-13/B, OVL-30/B, OVL-33 |
| **Java** | JLS §6.4.1: deklarierte Methode beschattet single-static-import; Defaults sind gewöhnliche Member | OVL-10/B, OVL-08/B |
| **Swift** | Labels; Parameterliste in der Trailing-Closure-Form | OVL-06, OVL-22 |
| **Swift** *(als Warnung)* | der Solver, und Lyric hat die Kurve schon | OVL-05/B, OVL-20 |
| **Rust** | E0255 am Import | OVL-10/C |
| **Rust** | `T::parse`, `From<T>`/`FromStr`: der Zieltyp wählt die Instanz | OVL-05/C, OVL-31/C |
| **Rust** | Operator = Interface-Methode | OVL-15 |
| **Rust, C#** | Auflösung im generischen Rumpf **vor** der Instanziierung, über den Bound | OVL-35/A |
| **rustc** | `probe` mit Snapshot/Rollback der Inferenztabellen | OVL-39/B |
| **C++** | `using`-Verschmelzung (Vorbild für den Ist-Zustand) | OVL-10/A |
| **C++** *(als Warnung)* | Zugriffsprüfung nach der Auflösung; Two-Phase-Lookup in Templates | OVL-19/C, OVL-35/B |
| **Julia** *(als Kontrast)* | Multiple Dispatch | Begründung für statische Auswahl |

**Sofortmaßnahmen (4.x, keine Sprachentscheidung nötig)** — die zweite Runde nannte fünf, es sind
**neun**, und für jede steht dabei, welcher Pin betroffen ist:

1. **OVL-12/E + B** — tiefe Giftregel (`LyrType.cs:65` rekursiv) und ungemutetes Nachprüfen
   (`p25`, `n03`). Pin `a_broken_argument_reports_itself` bleibt grün.
2. **OVL-11** — stumme doppelte Extension melden (`p18`, `n15l`). Pin
   `two_extensions_of_one_signature_refused` bleibt grün, solange `SEM0044` der Code bleibt
   (Span-Prüfung des Runners: behauptet, nein — **vor dem Fix prüfen**).
3. **OVL-14/B'** — Mangling-Ordinal ersetzen (`q04`/`q04b`). Kein Pin; die acht Oberflächen aus
   OVL-36 zeigen danach andere Namen, wo heute `#1` steht.
4. **OVL-27/Lowering** — `LYR-CLI0020` für `never` in Wertposition (`q18`/`q18b`).
5. **§5.1:43-46 korrigieren** — der Satz ist unwahr (`q09`/`q09b`).
6. **OVL-29/C** — `ResolveSelective` importiert die `pub`-Teilmenge (`n04`, `n04d`); eine Minor
   Warnung vorab. Kein Pin fällt; neuer `run`-Fall.
7. **OVL-33/B** — Parameter-ART filtert Lambdas (`n01`). Kein Pin fällt; neuer `run`-Fall.
8. **OVL-40/B** — Primitive bekommen eine Überladungsmenge (`n15n`); eine Minor Warnung vorab.
   Pin `two_extensions_overload_by_parameters` bleibt; neuer `run`-Fall auf `int`.
9. **OVL-41/A** — der §11-Satz, dass der Host die volle Stelligkeit ruft.

**Nicht in dieser Liste**: OVL-04 (`q16` läuft heute), OVL-19 für Member (Uhr 4.7), OVL-34
(eine Minor Warnung, dann Fehler — aber mit OVL-03 zusammen).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2** (`CONTRIBUTING.md:25-40`)

- Überladung ist der zugelassene zweite Mechanismus mit Pflicht (`STATUS.md:2510-2524`). Keine der
  41 Fragen fügt einen dritten hinzu — **wenn** OVL-05/C, OVL-31 und die Wertposition als **eine**
  Regel formuliert werden („der Kontext wählt die Instanz eines `Self`-tragenden Members"). Ohne
  diesen Satz hätte Lyric **drei** zielgewählte Wege (Wertposition, `as`, `T.parse`); *die zweite
  Runde stellte die Frage für OVL-06/16, nicht für OVL-05.* OVL-15/B' nimmt einen Mechanismus weg.
- **OVL-06** (benannte Argumente) und **OVL-16/D + OVL-32/C** (Askription: `as` konvertiert,
  Askription behauptet) bleiben die Grenzfälle; `null` ist der erste Fall, der die Askription
  braucht.
- **OVL-24** ist ein Rule-2-Fall in der Diagnostik (§12.5 ist die Klasse).
- **OVL-09** bricht eine Begründung, keine Regel (`STATUS.md:2387-2393`).
- **OVL-30/B** ersetzt sechs Ausgänge für ein Konzept („kontextabhängiges Argument") durch einen.

**Mit dem Charakter von Lyric**

- Alle Auswahl bleibt statisch; OVL-35/A macht es ausdrücklich.
- **OVL-05/B** (Rückgabetyp wählt) würde `03-functions.md:71` kippen — abgelehnt, ehrlich nur mit
  OVL-20.
- **OVL-09, OVL-14, OVL-26, OVL-36, OVL-41/B** sind Formatbrüche und gehören in **eine**
  Format-Entscheidung.
- **OVL-19/29** kollidieren mit „privat heißt privat": für freie Funktionen heißt es das schon
  (`n04c`), und der Importzweig bricht es (`n04`); für Member heißt es das nicht (`q05c`).
- **OVL-40** kollidiert mit „was in Lyric geht, wird in Lyric geschrieben" (Sitzungsgedächtnis
  std-source-first): solange Primitive keine Überladungsmenge haben, kann die std auf `string`
  nicht überladen.

**Mit anderen Gebieten**

| Gebiet | Konflikt |
|---|---|
| Generics/Constraints | OVL-02 (Prüfung vor der Auswahl), OVL-28 (Unifikation), OVL-34 (Alpha-Äquivalenz, Arität), OVL-35 (§8.1-Satz zur Reihenfolge). |
| Interfaces | OVL-07/08 schreiben §5.4 um; OVL-15 macht §5.1 wahr; **OVL-31** verlangt in §5.1 die zweite `Into<T>`-Konformanz und in §3.6 die Korrektur des Operatorregel-Verweises. |
| Typen/Konversionen | OVL-16/32: `as` vs. Askription; OVL-31: `Into<T>` als zielgewählt; OVL-30: kontextabhängige Literale. |
| Operatoren | OVL-15: `T`→`?T` und `T`→`I` werden für Operatoren neu zugelassen. |
| Bytecode/VM, Embedding | OVL-09, OVL-14, OVL-26, OVL-36 (Anzeigefeld), OVL-41 (Defaults im Format) — eine Formatfrage. |
| Laufzeitmodell | OVL-27 hängt am Lowering-Defekt `LYR-CLI0020`. |
| Module/Sichtbarkeit | OVL-19 (Member, Uhr 4.7 `PLAN.md:367`), OVL-29 (Import, Bugfix gegen §4.2:21), OVL-40 (`std.core` ohne Import). |
| Werte/Mutabilität, Fehler | OVL-21 (`PLAN.md:374-375`, typed throws). |
| Diagnostik | OVL-24 (§12.5-Fragen), OVL-12 (sechs Meldungen → eine Klasse), OVL-27/C (neue Diagnose). |
| Standardbibliothek | OVL-18 scheitert heute an OVL-26 **und OVL-40**. |
| Werkzeuge | OVL-17, OVL-38 (Rename spaltet Mengen), `lyrfix`. |
| Spec-Prozess | OVL-37: jede Antwort braucht ihre Pin-Zeile; nur OVL-09 retiriert einen Pin. |

---

## 6. Nach der Kritik geändert

**Korrigiert, weil die Kritik recht hatte (jeweils selbst nachgemessen oder nachgelesen):**

- **§1.5 / OVL-19 → OVL-29**: die Sichtbarkeit hat drei Antworten, nicht zwei — `ResolveSelective`
  (`Resolver.cs:232-246`) prüft nur das erste Symbol. Gemessen `n04` (`priv-exact`) gegen `n04b`
  (`pub-opt`), `n04c`, `n04d`. Für freie Funktionen ist es ein 4.x-Bugfix gegen §4.2:21, kein
  Major — OVL-19 gilt nur noch für Member.
- **§1.3 / §1.4 / OVL-12**: die Giftregel ist flach (`LyrType.cs:65`); `f([])` ist `SEM0087
  (<error>[])` (`n03`). OVL-12/B deckt das nicht; neue Optionen D (Probetypisierung mit
  Kandidatentyp) und E (tiefe Giftregel), Empfehlung E + B für 4.x.
- **§1.5 / OVL-05 / OVL-15 → OVL-31**: sechs Auswahlstellen, nicht fünf; `v as T` über `Into<T>`
  (§3.6/3) ist zielgewählt, `n05` zeigt `SEM0085`+`SEM0086 'into' is ambiguous for ()`. OVL-05/C
  ist ein dritter zielgewählter Weg, bis die Spec die drei als eine Regel fasst.
- **OVL-26 / OVL-18**: zwei Gründe, nicht drei — `Intern` schlüsselt nach Name UND Signatur
  (`ImportTable.cs:119-124,132-134`); nur `_byName`/`TryFind` und `NativeRegistry._natives` sind
  nach Namen geschlüsselt.
- **OVL-13 → OVL-33**: `n01` zeigt, dass ein Lambda auch gegen einen `int`-Parameter nicht
  abstimmt (`:2163`); der Parameter-ART-Filter ist die billigste Option und fehlte.
- **Anker**: `PLAN.md:355-357` → **`:352`** (Warnstufen-Pflicht); `PLAN.md:371,373` → **`:374,375`**
  (`mut` auf Klassenmethoden, `mut struct`).
- **OVL-28 → OVL-34**: die billige Fassung (Alpha-Äquivalenz, Typparameter-Arität) für freie
  generische Funktionen fehlte; `n10`, `n08` gemessen, Notes zeigen zweimal „(T)".
- **Sprachvergleich, sieben Korrekturen**: (1) C#-Default-Tiebreak ist ein **Flag** (ECMA-334
  §12.6.4.3, gelesen), der Zähler ist **Kotlin** (Spec, gelesen) — OVL-23 neu begründet;
  (2) C# hat benannte Argumente seit 4.0, die „Lehre" war falsch besetzt; (3) OVL-27/B hat **kein**
  Vorbild — Rust hat keine Überladung, Kotlins `Nothing`-Regel betrifft nur den Empfänger
  (gelesen), Swifts `Never` konvertiert nicht — Empfehlung jetzt C; (4) OVL-08/B: C# ist Vorbild
  für A (Default-Interface-Member sind keine Klassenmember), Java/Kotlin für „gewöhnlicher
  Member ohne Rangstufe"; (5) OVL-13: C# ist das Modell von C (Rumpf pro Kandidat), das
  Phasenmodell ist Java (JLS §15.12.2.2); (6) C# entfernt Constraint-Verletzer erst seit 7.3,
  davor Lyrics `p02b`-Verhalten — als Bruch ohne Warnstufe geliefert; (7) OVL-20/B: bei Roslyn
  trägt die Reihenfolge (Argumente einmal binden), nicht der Cache.
- **OVL-14/B'**: die Konsumenten der Namen stehen jetzt in OVL-36 (Trace gemessen `n07`, DAP und
  VM-Texte gelesen, DocGen, Host-API; Loader behauptet).
- **OVL-20/D**: „linear in Kandidaten und Ausdrucksgröße" ist keine haltbare Spec-Zusage (OVL-02/B
  bricht sie); jetzt Implementierungs-Invariante „jeder Teilausdruck einmal typisiert".
- **OVL-11 und Sofortmaßnahme 2**: der Pin `two_extensions_of_one_signature_refused` pinnt die
  Aufruf-Form; der Fix muss ihn grün lassen oder mit ihm retirieren. Jede Sofortmaßnahme nennt
  jetzt ihren Pin; OVL-37 tut es für alle Antworten.

**Ergänzt (dreizehn neue Fragen, alle in derselben Form):** OVL-29 (selektiver Import und
Importstil), OVL-30 (kontextabhängige Argumentformen, mit Tabelle `n03`/`n02`/`n12`/`n13`),
OVL-31 (`as` über `Into<T>`), OVL-32 (`null` zwischen `?T`, Ausweg gemessen `n14`/`n14b`),
OVL-33 (Parameter-ART filtert Lambdas), OVL-34 (generische Parameterlisten gleich?), OVL-35
(Auflösung vor/nach Monomorphisierung, `n11`/`n11b`), OVL-36 (Oberflächen des gemangelten Namens),
OVL-37 (Pin-Bilanz je Antwort), OVL-38 (Rename/Find-References), OVL-39 (Seitentabellen unter
Mute, gelesen `TypeResult.cs`), OVL-40 (`std.core` ohne Import **und** keine Überladungsmenge
auf Primitiven — `n15j…o`), OVL-41 (Host und `params`/Defaults).

**Neu gefunden, jenseits der Kritik:** auf Primitiven gibt es **keine** Überladungsmenge für
Extensions (`n15m`, `n15n` `SEM0014` gegen `n15o` 6/30; Ursache `TypeFacts.cs:97-102` →
`TypeChecker.cs:2002`). §4.3a:113-114 verspricht sie für alle Typen, der Pin prüft nur `struct`.
Das trifft OVL-18 (keine `string`-Extension kann überladen) und macht OVL-40/B zur Sofortmaßnahme.

**Stehengelassen, weil die Kritik nicht traf oder die Sache anders liegt:**

- Die Kritik stützte „erato2s Loader liest kompilierte Artefakte" auf `STATUS.md:897-900`; die
  Stelle belegt einen **DLL**-Loader (MZ-Probe um `LoadLibrary`), nicht einen `.lyrbc`-Leser. Die
  Lyricpp-Behauptung bleibt als „behauptet" in OVL-36, der Anker trägt sie nicht.
- Die Kritik nannte Kotlin als Vorbild für OVL-13/B (Arität). Die Kotlin-Spec schließt Lambdas
  aus der Inferenz vor der Auflösung aus (gelesen); ob die Form mitzählt, steht dort nicht. Java
  ist das belegte Vorbild, Kotlin bleibt „behauptet".
- Die Kritik las `std.core`-Extensions als „in jeder Kandidatenmenge" (OVL-25-Schärfe). Gemessen
  stimmt die Sichtbarkeit (`n15j`/`n15k`), aber auf Primitiven — den einzigen Typen, die
  `std.core` erweitert — gibt es **keine** Kandidatenmenge, in der sie stehen könnten; die Gefahr
  ist dort nicht Umlenkung durch besseren Fit, sondern „erster Treffer gewinnt" (`n15m`).
- Die Kritik sagte, OVL-19/D sei „auf den Member-Zweig zugeschnitten". Das stimmt, und statt D zu
  dehnen, ist der Importweg eine eigene Frage (OVL-29), weil sein Bruchgrad und seine Warnklasse
  anders sind (Bugfix gegen §4.2:21, gewöhnliche Warnung) als beim Member-Zweig (Uhr 4.7, §12.5).
- Für OVL-23 bleibt die Empfehlung B, obwohl das Vorbild wechselt: C#s Flag hat als Begründung die
  benannten Argumente, die Lyric nicht hat; Kotlin zählt und hat sie auch. Die Empfehlung steht auf
  Lyrics Lage, nicht auf einem Vorbild, und sagt das jetzt.
