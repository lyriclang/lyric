# Fehlerbehandlung — Dossier für Lyric 5

**Fassung 3, nach der zweiten adversarischen Kritik.** Ersetzt Fassung 2 vollständig.

Gemessen gegen `HEAD = 6f6f029f` („Merge pull request #173 from lyriclang/release/v4.6.0-cut"),
Binaries `src/Lyrc/bin/Debug/net10.0/lyrc.dll` und `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`
(gebaut 2026-09-24), Spec-Clone `…\lyric-spec` auf `8f17c02` („Merge pull request #44 from
lyriclang/spec/v5-migration-warnings"), Arbeitskopie sauber.

> **Was diese Fassung anders macht.** Fassung 2 hat §12.5 aus Spec-PR #44 zitiert und §7.5 und §10
> **desselben PRs** nicht gelesen. Dadurch behauptete sie an drei Stellen (F7, F20, F21, F22) eine
> normative Lücke, die es nicht gibt, und führte in F32 zwei Regeln als „nirgends normativ", die
> Anhang A seit je trägt. Alle Spec-Zitate in dieser Fassung sind auf `8f17c02` **mit Zeile** neu
> nachgeschlagen; alle Quelltext-Zeilennummern erneut auf `6f6f029f`. Die Kostenmessung ist unter
> `LYRIC_JIT=1` wiederholt, und sie dreht eine Empfehlung um (F18).
>
> Proben: `…/scratchpad/v5-design/probes/fehler-rev3/n01…n10b` (neu, plus die des Kritikers, alle
> auf HEAD nachgefahren), `…/fehler-rev2/r01…r32b` und `…/fehler/p01…p42`, soweit zitiert.
>
> **Belegkonvention**: *gemessen* = Probe kompiliert und gelaufen; *gelesen* = Pfad:Zeile;
> *behauptet* = Sprachwissen ohne Beleg im Repo, so gekennzeichnet.

---

## 1. Ist-Stand

### 1.1 Der Vertrag, wie er dasteht

| Sache | Wo | Was |
|---|---|---|
| Drei Fehlerformen | `lyric-spec/spec/09-errors.md:3–8` | Wert (`?T`/`bool`) · Ausnahme (`throw`) · Panik |
| Doktrin „whether vs. why" | `09-errors.md:10–32`, `docs/guide/10-errors.md:9–46` | stille Form + `OrThrow`-Zwilling aus EINER Implementierung |
| Nur Klassen sind werfbar | `09-errors.md:34–38` | `Throwable` eingebaut, `fn message(): string`; Structs ausdrücklich nicht |
| Klausel | `docs/Grammar.md:213–216` | `[ 'throws' [ TypeExpr ] ]` — **ein** Typ oder keiner; auch auf bodylosen Deklarationen |
| `ThrowsSuffix` nur auf Coroutinentypen | `docs/Grammar.md:307,329–331`; gemessen `r20` | sonst `LYR-SEM0084` (`src/Lyric.Frontend/Sema/TypeChecker.cs:6161`) |
| `try`/`catch` als Statement | `docs/Grammar.md:395–397` | Grammatik erlaubt **null** Klauseln; Anhang A `PAR0023` (Z. 62) und `SEM0036` (Z. 138) verbieten sie |
| Catch-all zuletzt | `appendix-a-diagnostics.md:137` (`SEM0035`) | **normativ**, aber nur im Anhang, nicht in §9.3 |
| `throw` als **Ausdruck** hat den Typ `never` | `docs/Grammar.md:492,517–519`; `design/throw-expression.md:34–37` | seit 4.5; `never` als Rückgabetyp dort **entschieden**, die Diagnose für andere Positionen ausgelassen |
| `defer` als einziger Cleanup | `CONTRIBUTING.md:31–32`, `spec/07-statements.md:129–133` | kein `finally`; **block-scoped, einmal pro Schleifeniteration** — normativ |
| Werfender `defer` | `spec/07-statements.md:135–141` | **ausdrücklich unspezifiziert**, beide Hälften benannt, `LYR-SEM0110`, „5.0 settles it" |
| Panik läuft keine `defer` | `spec/07-statements.md:132–133`; gemessen `n01`, `n01c` | „a panic aborts, it does not unwind" |
| Fallengelassene Koroutine läuft keine `defer` | `spec/10-coroutines.md:89–93`, `07-statements.md:143–146` | normativ: „the garbage collector is not an exit path" |
| Handler-Tabelle | `docs/Bytecode.md:379–393` | `kind 0 = catch, 1 = finally`, `catchType`, `slot` (nur Index + 1) |
| Abwicklung | **gemessen `r10`**, `src/Lyric.Vm/Interpreter.cs:1207–1214` | Gleichheit **oder** Konformanz über die Dispatch-Tabelle |
| Panik-Katalog | `appendix-a-diagnostics.md:270–291`, `spec/12-diagnostics.md:36–41` | **alle 17** `LYR-VM` plus `LYR-CAP0001/0002`, mit Severity; „a code that appears in neither … does not exist" |

**Formatkosten, ehrlich sortiert** (Korrektur gegenüber Fassung 2, siehe „Nach der Kritik"):

- **Null `.lyrbc`-Kosten**: F2 (Liste in der Klausel), F3 (Klausel am Funktionstyp), F4
  (Substitution), F5 (`try` als Ausdruck), F13, F17, F25, F34, F35, F39. Die Klausel lebt nur in der
  Sema (`design/typed-throws.md:97–108`: „Lowering 0, Format bleibt 4.0").
- **Kosten an einer anderen Grenze als dem Format**: F9-B/F20-A (*suppressed*): damit die VM eine
  zweite Ausnahme an die erste **hängen** kann, braucht sie einen Schreibort in einem beliebigen
  Throwable. Ein eingebautes Interface ohne Speicher (`Throwable` verlangt nur `message()`,
  `09-errors.md:34–38`; `std.core.Exception` hat genau ein Feld, `stdlib/std/core.lyr:78–86`) kann
  nichts anhängen. Bleiben: ein verstecktes Feld in jeder Throwable-Klasse (Layout, also
  Types-Sektion, also Format), **oder** eine VM-Seitentabelle plus eine compiler-gebundene
  Abfrage — und jede compiler-gebundene Kante ist nach `spec/11-stdlib-contract.md:7–9` Punkt 1
  Spec-Vertrag. Fassung 2 schrieb „kostet ein Feld nur, wer es nutzt" — **das war behauptet und
  ist falsch**.
- **Formatkosten**: F12-B (ein Enum ist kein Referenzwert — `Interpreter.cs:1216–1222` baut den Fat
  Pointer aus einer Referenz, und `throw` trägt „type index + 1", `Bytecode.md:887–889`; Fassung 2
  nannte `Bytecode.md:392`, wo nur „slot index + 1" steht — ein Slot hat keine Bauart), F10-C
  (Spur), F40 (Interface-Sektion), F37-B (JIT-Handlerregionen sind VM-, nicht Formatarbeit — aber
  ungemessen).

### 1.2 Was gemessen funktioniert

| Probe | Ergebnis |
|---|---|
| `p01` typisierter Wurf + `catch (e: Boom)` + zwei `defer` | `body / d2 / d1 / caught` — LIFO, läuft beim Abwickeln |
| `p04` `defer`-Kette ohne Wurf vor `return` | `d3 / d2 / d1 / returned 7` |
| `r12` `defer` in einem **nackten Block** | `inner / BLOCK-DEFER / after / FN-DEFER` — block-scoped, wie `07-statements.md:129` |
| `p36` `defer` im Schleifenkörper | einmal pro Iteration, wie `07-statements.md:131–132` |
| `p39` `defer` liest einen mutablen Local | zum **Ausführungs**zeitpunkt |
| `r25` `var bool` als `errdefer`-Ersatz | `rollback` nur auf dem Fehlerpfad — kein Heap-Objekt |
| `n01`, `n01c` `panic` mit registriertem `defer`, im selben und im Caller-Frame | **kein** `DEFER`, Exit 101; Kontrolle `n01b` (`return`): `DEFER` läuft |
| `n02a` `catch (e: Boom) { throw e; }` in `fn … throws Boom` | kompiliert, `rethrow / main caught`, Exit 0 |
| `n02c` `catch (e) { throw e; }` in `fn … throws` (bar) | kompiliert, `main caught boom` |
| `n03` zwei `catch (_: Boom)` hintereinander | **keine Diagnose**, `first` |
| `n07` `catch (_: AppError)` (Interface) **vor** `catch (_: NotFound)` (Klasse) | **keine Diagnose**, `iface` — die zweite Klausel ist tot; Kontrolle `n07b` (Klasse zuerst): `class` |
| `n09` `b &&= false` | **kompiliert und läuft** — Anhang A (`IR0001`-Zeile) ist auch hier veraltet |
| `n10b` `spawn(job())` mit `Coroutine<Wait>` | läuft; `n10` mit `Coroutine<Wait> throws Boom`: `LYR-SEM0001` — der werfende Task ist per Typ ausgeschlossen |
| `p09` `throw` als Ausdruck, `fn fail(): never`, Narrowing danach | läuft |
| `r02` `fn f(): int { panic("x"); }` | kompiliert — `panic` ist im Fluss `never` |
| `r04` `fn fail(m: string): never { … }` | kompiliert — `never` ist als Rückgabetyp schreibbar |
| `r10` `catch` auf einem **Interface** | läuft (PR #169) |
| `r07` `catch (e: Box<int>)` auf `throws Box<int>` | fängt |
| `r14` `fn maybe(i: int): ?int throws Boom` | kompiliert und läuft — `?T` **und** werfend ist gültig |
| `r26` totes `try`, nie greifende `throws`-Klausel | keine Diagnose |
| `p13` Catch-all vor typisiertem `catch` | `LYR-SEM0035` |
| `r22` `try` ohne `catch` | `LYR-PAR0023` **und** `LYR-SEM0036` |
| `p14` deklariert `throws Boom`, wirft `Other` | `LYR-SEM0034` |
| `r27`/`p26b` Konformanz: Impl wirft mehr / weniger | `LYR-SEM0042` / erlaubt |
| `p16b` `main` mit `throws` | `LYR-SEM0021` |
| `p17` `panic` | Exit 101, `LYR-VM0011`, Backtrace über drei Frames |
| `p22` `std.os.exit` | läuft keine `defer` |
| `p29` `catch (_)` um eine Panik | fängt nicht |
| `r13b` Koroutine erschöpft gezogen | `defer` läuft |
| `p20/p42` Coroutine: `throws` am Typ, `yield` in geschützter Region | läuft |
| `p31` `text` vs. `textOrThrow` | die §9.0-Doktrin trägt |
| `p34/p35` `try` in der Definite Assignment | verlassende catches tragen bei |
| `p33`, `FMT1`, `FMT2` `lyrfmt --stdin` | druckt `catch (_: Boom)`, `fn f(): int throws Boom`, `Coroutine<int> throws Boom` (`AstFormatter.cs:321–325`) |
| `r01`, `n06` `defer boom();` | `warning[LYR-SEM0110]` — die Migrationsuhr läuft |

### 1.3 Die Löcher — gemessen

**(a) Der typisierte Wurf kollabiert bei ZWEI Fehlertypen auf `Throwable`, und es gibt keinen Weg
zurück.** Der schwerste Befund des Gebiets.

- `throws A, B` existiert nicht (`docs/Grammar.md:215`). Gemessen `n08`: DREI Diagnosen, die lauteste
  `LYR-SEM0051` *„'work' has no body; only standard-library modules may declare native functions"*,
  dann `PAR0016`, `PAR0025`. Siehe F26.
- Wer zwei Typen wirft, schreibt `throws` bar. Ein Aufrufer mit `catch (_: A) … catch (_: B)` deckt
  das **nicht** (`r32`): `LYR-SEM0034`, „may throw 'Throwable', which nothing handles". Kontrolle
  `r32b`: mit `catch (_)` kompiliert es, der typisierte Zweig greift zuerst.
- Im Catch-all ist der Typ endgültig weg: ein Typ-Pattern auf einem Interface-Wert ist ein
  Parsefehler (`p41`, `LYR-PAR0002`); geplant als v5-Feature **#13**
  (`docs/Befunde_und_Verbesserungen/lyric-v5-features.md:45` — Fassung 2 schrieb dreimal „#16";
  #16 sind Operator-Interfaces, Z. 48).

**(b) Ein Typparameter in der Klausel wird an der Aufrufstelle nicht substituiert.** `r31`:
`fn rethrowIt<E :: [Throwable]>(e: E): int throws E` plus `catch (_: Boom)` ist `SEM0034`.
`src/Lyric.Frontend/Sema/ExceptionAnalyzer.cs:251` — `_ => (true, null)`. Die stdlib umschifft es
(`stdlib/std/result.lyr:151–154`). Asymmetrie: das `catch` substituiert (`p18b`/`p18c`).

**(c) Lambdas dürfen nicht werfen — zwei Löcher.** Funktionstyp ohne Klausel (`r20`, `SEM0084`,
`TypeChecker.cs:6161`; der Grund in `stdlib/std/test.lyr:95–99` für das fehlende `assertThrows`);
werfender Lambda-Körper gegen wurffreien Typ (`r21`, `SEM0034`).

**(d) Ein werfender `defer` — Rückgabepfad.** `p02`: vor einem `return` läuft der werfende `defer`
**zweimal**, der vorher registrierte gar nicht. Kontrolle `p03` (Durchfall): einmal, „A" läuft
weiterhin nicht. `STATUS.md:2188–2195` führt es als Clock; `spec/07-statements.md:135–141` erklärt
den Fall **ausdrücklich für unspezifiziert** und benennt beide Hälften — „whether the stages
scheduled BEFORE the thrower still run, and what the exception does to the exit path it
interrupted". Uhr: `LYR-SEM0110` (`r01`, `n06`).

**(e) Ein werfender `defer` — Abwicklungspfad.** `r11`: `First` fliegt, ein `defer` wirft `Second`;
der `catch` sieht **SECOND**, `First` ist weg, der früher registrierte `defer` läuft nicht
(`Interpreter.cs:1197–1198`: „a second throw from the finally body replaces it").
**Korrektur zu Fassung 2**: das ist nicht „vorher nirgends notiert" — §7.5:137–139 nennt genau diese
Hälfte („what the exception does to the exit path it interrupted"). Neu ist nur die **Messung**, was
die Referenzimplementierung heute tut.

**(f) `defer { throw … }` innerhalb eines `try` killt den Compiler.** `r17`: `LYR-CLI0020: lowering:
block bb4 is already sealed`.

**(g) Eine entkommene Ausnahme meldet den TYP und sonst nichts.** `r06`: `panic [LYR-VM0010]: uncaught
exception of type 'Box<int>'` plus eine Frame-Zeile, `message()` nie gerufen
(`Interpreter.cs:584–585, 614–615`). Der Abwickler recycelt jeden durchsuchten Frame sofort
(`Interpreter.cs:1232–1236`).

**(h) Eine .NET-Ausnahme ist eine Panik.** `LYR-VM0016`, Exit 101 (`p28`); offen laut
`src/Lyric.Vm/DotnetBinding.cs:26–28`, `docs/guide/14-embedding.md:158–161`,
`appendix-a-diagnostics.md:290`.

**(i) Ein Enum hat vier Antworten.** Deklaration (`r09`) und Klausel (`r19`) kompilieren; `catch`
ist `IR0001` (`r18`); der Wurf ist ein **ICE** (`r08`, `CLI0020`, ir-verifier); `extend` ist
`SEM0030` (`r28`).

**(j) Die Deckungsrechnung rechnet auf der DEFINITION, nicht auf der Instanz.** `r06`:
`throws Box<int>` gegen nur `catch (e: Box<string>)` **kompiliert** und stirbt mit `VM0010`;
Kontrolle `r07` fängt. `ExceptionAnalyzer.cs:250`: `GenericInstance gi => (false, gi.Definition)`.

**(k) Der `defer` einer fallengelassenen Koroutine läuft nie — und das ist SPEZIFIZIERT.** `r13`
(fallengelassen: nichts) gegen `r13b` (erschöpft: läuft). **Korrektur zu Fassung 2**, die das als
„Leck" führte: `spec/10-coroutines.md:89–93` normiert „a chain abandoned mid-suspension — dropped,
collected — runs NOTHING … the garbage collector is not an exit path and does not become one here",
ebenso `07-statements.md:143–146`. Es ist eine bewusste Regel; was fehlt, ist ein Verb, das den
Ausgang **herbeiführt** (F22).

**(l) Stillschweigen bei toten Klauseln.** `r26` (totes `try`, tote Klausel): keine Diagnose.
**Neu, `n03`/`n07`**: eine **verdeckte** `catch`-Klausel — gleicher Typ zweimal, oder Interface vor
implementierender Klasse — ist ebenfalls still tot. Seit dem Interface-Catch ist die Klauselfolge
eine Subtypfrage; `SEM0035` prüft nur den Catch-all. Siehe F35.

**(m) Kein `errdefer`, aber billig.** `r25`: `var ok = false;` plus `defer { if (!ok) … }` — drei
Zeilen, keine Allokation.

**(n) Keine Ursache, keine Unterdrückten.** `std.core.Exception` hat ein Feld (`core.lyr:78–86`).

**(o) `never`: die Grammatik kennt es nicht, drei Positionen sind ICEs — und die Regel ist schon
ENTSCHIEDEN.** `docs/Grammar.md` führt `never` weder unter Keywords (64–77) noch `BuiltinType`
(318–323); der Compiler kennt es (`BuiltinTypes.cs:21`, `TypeFacts.cs:45`). `r04` (Rückgabe)
kompiliert; `r05` (Parameter), `r24c` (Elementtyp), `r24d` (Typargument) sind `LYR-CLI0020` („has
type void"); Kontrolle `r24e`. **Korrektur zu Fassung 2**: `design/throw-expression.md:34–37` hat
das entschieden — „`never` wird Builtin-Typname (§3.1 Tabelle), gültig als Rückgabetyp … `let x:
never`, `never[]`, `?never`, Typargument `never` werden in dieser Runde nicht eingeschränkt …
Empfehlung für die Spec: ‚a return type only', analog zu `void`, plus ein `LYR-SEM`-Fehler an jeder
anderen Stelle (nicht im Prototyp)". Die drei ICEs sind die **ausgelassene Hälfte** dieses
Entscheids. Siehe F28.

**(p) Eine Lyric-AUSNAHME sieht für einen Host aus wie eine Panik.** `Interpreter.cs:583–585,
612–615` wirft `LyricPanic` mit `VM0010`; `src/Lyric.Embedding/ScriptException.cs:23–26` übersetzt
jede `LyricPanic` außer dem Budgetstopp in `ScriptPanicException`. Siehe F23.

**(q) Wer v5-Syntax schreibt, bekommt eine Lawine.** `n08` (3), `r30` (`try?`: 5), `r29`
(`try … catch`-Ausdruck: 16), `r16` (Modul-`let`: 1, mit unerfüllbarem Rat). Siehe F26, F39.

**(r) Kein präzises Rethrow.** `n02b`: `catch (e) { throw e; }` in `fn work(): int throws Boom`,
dessen `try`-Körper nur `Boom` werfen kann, ist `SEM0034` („may throw 'Throwable'"). Kontrollen
`n02a` (typisierte Bindung) und `n02c` (`throws` bar) kompilieren. Siehe F34.

**(s) Ein Task kann sein Scheitern nicht melden.** `n10`: `spawn` mit `Coroutine<Wait> throws Boom`
ist `SEM0001`; `stdlib/std/task.lyr:10–12` sagt warum: „the scheduler has nobody to hand an
exception to. A panic anywhere ends the program". Siehe F36.

**(t) Der Wurf-/Fangpfad bleibt außerhalb des JIT — gemessen.** Siehe F24 (Tabelle) und F37.
`src/Lyric.Vm/Jit/JitCompiler.cs:712–726`: `throw` ist ein abgelehnter Opcode, und jeder Callee
muss selbst kompilieren, „not about speed" — eine kompilierte Frame zwischen Wurf und Handler bricht
die Kette. Folge: eine Funktion mit `try` um einen werfenden Aufruf wird unter `LYRIC_JIT=1`
**nicht** schneller; die stille `?T`-Form wird es um den Faktor 3.

### 1.4 Wo Spezifikation, Grammatik, Dokumentation und Compiler auseinandergehen

| Stelle | Geschrieben | Gemessen / gelesen |
|---|---|---|
| `catch` auf einem Interface | `spec/09-errors.md:61–63`, *Implementation limit*: „refused … `LYR-IR0001`" | **läuft** (`r10`) |
| Katalog `LYR-IR0001` | Anhang A: „currently covers `&&=`/`\|\|=` and a `catch` naming a specific interface" | **beides gebaut** (`n09` läuft, `r10` läuft) |
| Typvergleich beim Abwickeln | `docs/Bytecode.md:891–892`: „equality, not a subtype test" | Konformanzprüfung (`Interpreter.cs:1213–1214`, `r10`) |
| dieselbe Aussage im Quelltext | `Interpreter.cs:1175` (XML-Kommentar) | drei Zeilen über dem Gegenteil |
| `never` | `docs/Grammar.md` kennt es nicht | `BuiltinTypes.cs:21`, `TypeFacts.cs:45`; `r04`; **entschieden** in `design/throw-expression.md:34–37` |
| `TryStmt` | `docs/Grammar.md:395`: `{ CatchClause }` — null erlaubt | Anhang A `PAR0023` (Z. 62) und `SEM0036` (Z. 138) verbieten es; Parser refused (`r22`) — **die Grammatik widerspricht dem Anhang** |
| Wurf **aus** einem `catch` | nirgends normiert | von der Schwesterklausel nicht gefangen (`r15`) |
| Verdeckte `catch`-Klausel | nirgends normiert | still tot (`n03`, `n07`) |
| `std.test.fail` ist `void` | `stdlib/std/test.lyr:89–90`: „until the language lets a library function say `never`" | falsch seit `r04`; ebenso `BuiltinTypes.cs:57` („not nameable") |

**Nicht (mehr) in dieser Liste** (Korrektur zu Fassung 2): „Catch-all muss letzte Klausel sein" —
normativ in `appendix-a-diagnostics.md:137` (`SEM0035`); „`try` braucht ein `catch`" — normativ in
Z. 62 und 138. §12.2 (`12-diagnostics.md:36–41`) macht den Anhang zum Vertrag. Fassung 2 hat den
Anhang in derselben Tabelle als normativ behandelt (`IR0001`-Zeile) und zwei Zeilen tiefer nicht —
das war zweierlei Maß. Was bleibt, ist die **Grammatik**, die dem Anhang widerspricht.

---

## 2. Sprachvergleich

| Sprache | Mechanismus | Fehlermenge | Marker am Aufruf | Cleanup | Ursache / Spur | Bug vs. Fehler |
|---|---|---|---|---|---|---|
| **Lyric 4** | geprüfte Ausnahme, `throws [T]` | **ein** Typ oder alles | keiner | `defer` (block-scoped) | keine / keine | `panic` vs. `throw` |
| **Swift 6** | geprüfte Ausnahme, `throws(E)` | ein Typ (`any Error` Default, `Never` = wirft nicht) | `try` Pflicht, `try?` (kollabiert seit Swift 5), `try!` | `defer` (**scope-gebunden**, darf **nicht** werfen) | keine Standardspur | `fatalError`/Trap |
| **Java** | checked exceptions, `throws A, B` | Liste + Vererbungsbaum; **precise rethrow** seit 7 | keiner | `finally`, try-with-resources (`getSuppressed`) | `getCause()`, Stacktrace | `Error` vs. `Exception` |
| **C++98/03** | dynamische Spezifikation `throw(A, B)` | Liste, **zur Laufzeit** geprüft | keiner | RAII | keine | keine |
| **Kotlin** | unchecked, `try` ist Ausdruck | keine | keiner | `finally` **und** `use {}` | Stacktrace, `cause` | Konvention |
| **C#** | unchecked, Filter `when` | keine | keiner | `finally`, `using`; Iterator-`Dispose` läuft `finally` | `InnerException`; `throw;` behält die Spur, `throw e;` nicht | Konvention |
| **Rust** | `Result<T,E>` | `E` ist Typ, `From` | `?` — nicht erzwungen | `Drop` | `Error::source()` | `panic!` (`unwind`/`abort` als Profil) vs. `Result`; `JoinHandle::join() -> Result` |
| **Go** | `(T, error)` | `error`-Interface | `if err != nil` — nicht erzwungen | `defer` (funktions-scoped), **läuft beim Panic** | `%w` + `errors.Is/As` | `panic`/`recover`; Goroutine-Panic tötet den Prozess |
| **Zig** | Fehler-Union `E!T` | Fehlermengen, Vereinigung | `try`/`catch` Pflicht | `defer` + `errdefer` (**dürfen nicht** `return`/`try`) | Error Return Trace | `unreachable` vs. `error` |
| **Python** | unchecked | — | keiner | `finally`, `with`; `generator.close()` wirft `GeneratorExit` | `__context__`/`__cause__` (implizite Verkettung) | Konvention |
| **Sutter, P0709** | „static exceptions" | `std::error` (Code + Domäne) | `try` als **optionaler** Marker vorgeschlagen | RAII | Domäne + Code | Vertragsbrüche terminieren |

Alle Zeilen außer Lyric: *behauptet* (Sprachwissen), soweit nicht im Repo belegt.

### Was jede damit gewinnt — und bezahlt

**Swift 6** hat mit SE-0413 `throws(E)` — `design/typed-throws.md` Stufe 3. **Vier Punkte, an denen
Fassung 2 Swift falsch zitierte** (*behauptet*): (1) SE-0413 ersetzt `rethrows` nicht allgemein, weil
`throws(E)` die **Vereinigung** mehrerer werfender Closure-Parameter nicht schreiben kann — die Stelle,
an der F3 ohne F2 landet. (2) `try?` auf einem `T?` liefert seit **Swift 5 (SE-0230) `T?`**, nicht
`T??` — Swift ist der Präzedenzfall für den Kollaps, und `design/try-expression.md:113` hat genau das
übernommen („`try?`-Semantik (Optional-Kollaps) übernommen"). (3) Swifts `defer` ist **scope**-gebunden
wie Lyrics, nicht funktionsweit wie Gos. (4) Ein `defer`-Körper **darf in Swift nicht werfen** —
Übersetzungszeitfehler („errors cannot be thrown out of a defer body"). Das ist Option D in F7/F20, und
Fassung 2 führte sie mit Vorbild „—".

**Java** hat die Liste (`throws A, B`), Multi-Catch, **precise rethrow** (seit 7: `throw e` aus einem
`catch (Exception e)` wirft statisch die Menge des Körpers, wenn `e` effectively final ist — F34) und
try-with-resources mit *suppressed* (F20-A). Funktionale Schnittstellen **dürfen** `throws` tragen
(`Callable`, `ThrowingFn<T,R,E extends Exception>`); der Wrapping-Reflex kommt aus
`java.util.function`. **Korrektur zu Fassung 2 bei der ABI** (*behauptet*, JLS §13.4.21): Änderungen
an der `throws`-Klausel brechen die **Binär**kompatibilität **nicht** — checked exceptions sind ein
Quell-, kein ABI-Vertrag. Für F27/F40 ist das der Punkt: wer die Klausel versionsübergreifend prüfen
will, braucht sie **im Artefakt**, und Java hat sie dort nicht.

**Kotlin**: keine geprüften Ausnahmen; `try` als Ausdruck; strukturierte Nebenläufigkeit propagiert
das Scheitern eines Kind-Tasks an den Scope (F36-C). Zwei Cleanup-Mechanismen (`finally`, `use`).

**C#** ist in dieser Fassung an drei Stellen **herabgestuft**: Exception-Filter sind billig, weil .NET
in zwei Durchgängen abwickelt — Lyric wickelt in einem ab (`Interpreter.cs:1232–1236`). C# hat
**keine statische Deckungsrechnung**, taugt also nicht als Vorbild für F19-A (Fassung 2 führte es).
Ein fallengelassener Iterator läuft seine `finally`-Blöcke **nur bei `Dispose()`**, nie beim GC —
C# ist Vorbild für F22-**B** (explizites Schließen wickelt nur `finally` ab, keinen `catch`), nicht
für F22-C. Was C# richtig zeigt: `throw;` (Spur bleibt) gegen `throw e;` (Spur neu) — die Frage, die
F34 stellen muss, sobald F10 eine Spur einführt.

**Rust**: `Result`, `?`, `#[must_use]` als Warnung. `Termination` verlangt `E: Debug`. `!` ist in
stabilem Rust **nur als Rückgabetyp** zulässig; als allgemeiner Typ ist es das instabile
`never_type` — Rust ist damit Vorbild für F28-**A**, nicht B. `panic=unwind|abort` ist eine
**Profilwahl** (F33-C). `JoinHandle::join()` liefert `Result` (F36-B).

**Go**: Quelle von Lyrics `defer`. Panickt eine deferred-Funktion, laufen die übrigen; **beim
Doppel-Panic verkettet die Laufzeit beide** und druckt beide im Absturzbericht (`panic: first …
panic: second`) — nur `recover()` liefert die letzte. Go ist also **kein** Vorbild für „letzte gewinnt,
frühere verloren" (F20-B); dafür stehen Java-`finally` und C# (ein Wurf im `finally` verdrängt den
pendenden). Und Go **läuft die defers beim Panic** — F33. Gos `defer` ist funktions-scoped; Lyric
weicht bewusst ab (`07-statements.md:129`).

**Zig**: Fehlermengen, `errdefer`, Error Return Trace, `catch` als Ausdruck — und **`return`/`try` in
`defer`/`errdefer` ist ein Übersetzungszeitfehler** („cannot return from defer expression"). Zweites
Vorbild für F7-D/F20-D. Zigs `defer` ist block-scoped wie Lyrics.

**Python**: `except (A, B)` Multi-Catch; `generator.close()` wirft `GeneratorExit` **in** den
Generator (F22-B, die Variante mit sichtbarer Ausnahme); **PEP 342: der Generator-Finalizer ruft
`close()`** — Python, nicht C#, ist der Präzedenzfall für F22-C; `__context__` verkettet eine zweite
Ausnahme implizit an die erste **ohne Liste** — Vorbild für F20-A ohne F9-B.

**Sutter, P0709**: `throws` an der Funktion, `std::error` = Code + Domäne, zwei Wörter. **Korrektur**:
P0709 schlägt `try` als **optionalen** Aufruf-Marker vor und lehnt die Pflicht aus
Kompatibilitätsgründen ab (*behauptet*). Für F6-B bleiben damit **zwei** Sprachen mit erzwungenem
Marker (Swift, Zig), nicht drei. Die These bleibt: Vertragsbrüche terminieren.

*(Midori, „The Error Model", **behauptet**: abandonment für Bugs, geprüfte Ausnahmen für Erwartbares,
`throws` bewusst unparametrisiert — das stärkste Gegenargument zu F2/F3.)*

---

## 3. Designfragen

**F1–F32** aus Fassung 2, korrigiert. **F33–F41** neu (aus der Kritik, alle nachgemessen oder
nachgelesen). Jede Frage: Ist-Stand mit Beleg, Optionen (Name — Beschreibung — Vorbild — Preis),
Empfehlung, Bruchgrad, Vertrauen, Abhängigkeiten.

### F1 — Bleibt `throws` überhaupt die geprüfte Klausel?

**Heute**: ja, geprüft. `CONTRIBUTING.md:31`, `spec/09-errors.md:47–50`. Gemessen `p14`, `r27`, `r21`.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt geprüft und typisiert | Swift 6, Java | Funktionsfärbung |
| B | ungeprüft, Klausel nur Dokumentation | Kotlin, C# | Signaturen schweigen; das Dossier wird gegenstandslos |
| C | Fehler als Wert, `Result` als Primitiv | Rust, Go | zweite Propagationsform — Rule 2 |
| D | geprüft, unparametrisiert („kann fehlschlagen: ja/nein") | Midori (behauptet) | `catch` sagt nie, was es fängt |

**Empfehlung: A** — haltbar nur mit F2, F3, F19 und F34: eine Klausel, die bei zwei Typen kollabiert
(a), Lambdas aussperrt (c), bei generischen Werfbaren falsch rechnet (j) und ein `catch (e) { log;
throw e; }` in einer typisierten Funktion nicht erlaubt (r), hat Kotlins Problem ohne Kotlins Ausweg.
**Bricht**: nein. **Vertrauen**: gemessen. **Hängt an**: F2, F3, F19, F34.

---

### F2 — Darf eine Klausel MEHRERE Fehlertypen nennen?

**Heute**: nein (`n08`: `SEM0051` + `PAR0016` + `PAR0025`; `Grammar.md:215`). Wer zwei wirft,
schreibt `throws` bar, und kein typisierter `catch` deckt mehr (`r32`/`r32b`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `throws A, B` als Liste (Klauselseite); Deckung als Mengenoperation | Java 7; C++98 als Warnung | Grammatik + `ExceptionAnalyzer`; **braucht F19 und F25**, sonst erbt die Mengenrechnung den Deckungsfehler |
| B | benannte Fehlermengen als eigenes Konstrukt | Zig | zweites typartiges Ding — Rule 2 |
| C | ein Typ; wer mehrere hat, baut ein Trägerinterface | Lyric 4 + PR #169 | geht heute (`r10`), aber kein Zurück-Narrowen (`p41`) |
| D | wie C plus Typ-Pattern auf Interface-Werten | Kotlin `is`, Java `instanceof`-Pattern | löst (a) über das Pattern-Gebiet; v5-Feature **#13** |

**Empfehlung: A + D, A erst nach F19 und F25.** Aus C++98 mitnehmen: die Liste fiel dort wegen
Laufzeitprüfung (trifft Lyric nicht) und fehlender Template-Komposition (trifft Lyric: F17).
**Bricht**: nein. **Vertrauen**: gemessen. **Hängt an**: F19, F25, F35, F13, F27, F41, Pattern-Gebiet.

---

### F3 — Typed throws auf Funktionstypen und Lambdas

**Heute**: `fn(int) -> int throws Boom` als Parametertyp ist `SEM0084` (`r20`,
`src/Lyric.Frontend/Sema/TypeChecker.cs:6161` — Fassung 2 nannte 5964, dort steht Literal-Adaption);
werfender Lambda-Körper ist `SEM0034` (`r21`). `stdlib/std/test.lyr:95–99` nennt das erste als Grund
für das fehlende `assertThrows`. Design: `design/typed-throws.md` Stufen 1–3.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `throws E` am Funktionstyp, `E` Phantom-Parameter, Inferenz nur nach innen | Swift 6; Java `Callable`/`ThrowingFn` | Sema ~200 Zeilen, null Format. Bei **mehreren** werfenden Parametern braucht es die Vereinigung — F2 |
| B | zusätzlich `rethrows` | Swift (behält es) | zweiter Mechanismus |
| C | Fehlermenge aus dem Körper inferieren, auch ohne Kontext | Zig | öffentliche Signatur sagt nichts |
| D | nichts | Lyric 4 | Kotlins Diagnose ohne Ausweg |

**Empfehlung: A mit F2-A.** **Bricht**: nein. **Vertrauen**: gemessen (Ist), behauptet (SE-0413).
**Hängt an**: F2, F17, F25, F41.

---

### F4 — Substitution des Klausel-Typparameters an der Aufrufstelle

**Heute**: nein (`r31`; `ExceptionAnalyzer.cs:251`), das `catch` substituiert (`p18b`/`p18c`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | substituieren (Stufe 1, ~60 Zeilen) | Swift 6 | Bugfix |
| B | Typparameter in der Klausel verbieten | — | nimmt `Result.orThrow` die Signatur |

**Empfehlung: A, vor v5, in einem Zug mit F19** (dieselbe Methode `ThrownOf`,
`ExceptionAnalyzer.cs:243–251`). **Bricht**: nein. **Vertrauen**: gemessen. **Hängt an**: F19,
Generics-Gebiet.

---

### F5 — `try` als Ausdruck, `try?`, und was `try e` allein liefert

**Heute**: nur Statement (`r29`: 16 Diagnosen, `r30`: 5). Design: `design/try-expression.md`.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `try e catch (x: E) expr` mit einer Klausel, plus `try? e` → `?T` | Zig `catch`, Swift `try?` (Swift 5: kollabiert) | ~180 Zeilen; `try?` wirft den Grund weg; **F31 vorher** |
| B | volle Statement-Form als Ausdruck | Kotlin | 20-zeilige Ausdrücke |
| C | zusätzlich `try e` → `Result<T, E>` | Swift `Result { try … }`, Kotlin `runCatching` | Compiler an `std.result` gebunden; braucht F4 |
| D | nichts | Lyric 4 | vier Zeilen und ein `var` pro Verzweigung |

**Empfehlung: A in 4.7, C in 5.0, A erst nach F31.** **Neu**: A ist auch die Antwort auf F39 — ein
Modul-`let` kann heute nichts fangen, weil es auf Modulebene weder `try` noch Wertblock gibt (`n05`);
`try e catch (…) expr` ist ein Ausdruck und stünde dort. **Bricht**: nein. **Vertrauen**: gemessen.
**Hängt an**: F31, F39, F41, F4 für C.

---

### F6 — Braucht ein werfender Aufruf einen sichtbaren Marker?

**Heute**: nein; bewusst (`design/try-expression.md`, Vergleichstabelle).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt unmarkiert | Java, C#, Go und Rust in der Praxis | früher Ausstieg im Text unsichtbar |
| B | Pflicht-Marker `try f()` | **Swift, Zig** — zwei Sprachen; P0709 schlägt ihn nur **optional** vor (behauptet) | major |
| C | Editor-Inlay + `lyric explain` | — | wirkt nur im Editor |

**Empfehlung: A + C.** **Bricht**: B major. **Vertrauen**: gemessen (Lyric), behauptet (Rest).
**Hängt an**: F7, F20, F30.

---

### F7 — Was schuldet ein werfender `defer` dem Rest der Kette? (Rückgabe- und Durchfallpfad)

**Heute** (`p02`, `p03`): der vorher registrierte `defer` läuft nie; vor `return` läuft der werfende
zweimal. `STATUS.md:2188–2195`: Clock. **`spec/07-statements.md:135–141` erklärt den Fall ausdrücklich
für unspezifiziert** und benennt beide Hälften; `LYR-SEM0110` (`ExceptionAnalyzer.cs:279–303`) läuft.
Fassung 2 schrieb „§7.5 sagt dazu nichts" — falsch, §7.5 sagt genau, dass es nichts festlegt, und warum.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | restliche Kette läuft; die **erste** Ausnahme propagiert, spätere *suppressed* | Go (Kette läuft) + Java try-with-resources | braucht F9-B, und **F9-B hat verdeckte Kosten** (§1.1: Feld pro Throwable-Klasse oder compiler-gebundene Kante); Epilog-Route im Lowering |
| B | restliche Kette läuft; die **letzte** gewinnt | Java `finally`, C# (ein Wurf im `finally` verdrängt den pendenden); Python `__context__` als A ohne Liste | billig; verliert die Ursache — heutiges Verhalten auf dem Abwicklungspfad (`r11`) |
| C | Kette bricht ab (heutiger Rückgabepfad, festgeschrieben) | — | ein früh registriertes `close()` fällt genau im Fehlerfall aus |
| D | ein werfender `defer` ist ein **Übersetzungszeitfehler** | **Swift** („errors cannot be thrown out of a defer body"), **Zig** („cannot return from defer expression") — *behauptet* | verbietet `defer { w.flushOrThrow(); }`; der Ersatz ist die stille Form (`defer w.flush();`, §9.0 garantiert den Zwilling) oder mit F5-A `defer { try? w.flushOrThrow(); }` |

**Empfehlung: D — geändert gegenüber Fassung 2 (A).** Gründe, in dieser Reihenfolge:

1. D ist die **einzige** Option, die F7, F20 **und** den ICE aus (f) mit **einer** Regel schließt,
   ohne VM-Umbau, ohne F9-B und ohne dessen verdeckte Kosten.
2. Zwei der drei Sprachen, die dieses Dossier sonst als Lyrics nächste Vorbilder führt, tun genau
   das. Go tut es nicht — aber Gos `defer` ist eine Funktionsaufruf-Form mit `recover`, Lyrics ist ein
   Statement ohne.
3. Die Uhr passt: `SEM0110` steht heute an **jeder** Wurfstelle in einem `defer`-Körper und „retires
   WITH its rule" (`12-diagnostics.md:74–100`) — aus der Warnung wird der Fehler, der Ort ist derselbe.
4. Der Preis ist klein und **von §9.0 bereits bezahlt**: jede werfende Bibliotheksfunktion hat eine
   stille Zwillingsform, und ein Cleanup, dessen Fehlschlag jemanden interessiert, gehört ohnehin in
   den Rumpf, nicht in den `defer`.

Was D **nicht** entscheidet und mit A/B/C verschwindet: nichts — mit D gibt es keinen werfenden
`defer`, also keine Kette, die eine Ausnahme unterbrechen könnte. Der Doppellauf (`p02`) ist dann ein
Lowering-Bug ohne Sprachfrage dahinter. **Wechselwirkung mit F18**: fallen die stillen Zwillinge
(F18-B), braucht D zwingend F5-A (`try?` im `defer`). Diese Fassung empfiehlt F18-A, womit der Konflikt
entfällt.
**Bricht**: **minor** — jedes Programm, das heute `SEM0110` erhält, kompiliert nicht mehr; die Uhr
läuft seit 4.6.0. **Vertrauen**: gemessen (`p02`, `p03`, `r01`, `n06`), gelesen (§7.5, §12.5),
behauptet (Swift, Zig). **Hängt an**: F20 (dieselbe Antwort), F5-A, F18, F9.

---

### F8 — Cleanup nur auf dem Fehlerpfad (`errdefer`)

**Heute**: nicht vorhanden; Ersatz `var bool` + `defer { if (!ok) … }` (`r25`), keine Allokation.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `errdefer` | Zig | zweiter Cleanup-Mechanismus — Rule-2-ADR |
| B | `defer on throw { … }` | — | eine Form, zwei Modi |
| C | `unwinding(): bool` im `defer`-Körper | — | globale Zustandsfrage im Kern |
| D | nichts | Lyric 4, Go | drei Zeilen Zeremonie, keine Allokation |

**Empfehlung: D, B als Rückfall.** Mit F7-D wird `defer` strikt nicht-werfend; ein `errdefer`, der
selbst nicht werfen darf, ist dann reine Bequemlichkeit — und Kotlins zwei Mechanismen für eine
Frage sind die Warnung. **Bricht**: nein. **Vertrauen**: gemessen. **Hängt an**: F7.

---

### F9 — Trägt ein Throwable eine URSACHE?

**Heute**: nein (`core.lyr:78–86`, `09-errors.md:34–38`; `p15`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `Throwable` bekommt `cause(): ?Throwable` als Default-Methode (Default `null`) | Java `getCause`, Rust `source` | **Lesen** ist per Default-Methode gratis; **Schreiben** ist Sache der Klasse (ein Feld, das sie selbst deklariert) — kein VM-Schreibort nötig |
| B | zusätzlich `suppressed(): Throwable[]`, von der **VM** beim Abwickeln befüllt | Java try-with-resources | **nicht gratis** (§1.1): die VM braucht einen Schreibort in jedem Throwable — verstecktes Feld (Layout/Format) oder Seitentabelle + compiler-gebundene Kante (§11 Punkt 1) |
| C | Bibliothek: `std.error.Wrapped` | Go `%w` | kein Vertrag |
| D | nichts | Lyric 4 | ein Fehler aus drei Schichten ist ein Satz |

**Empfehlung: A. B entfällt mit F7-D/F20-D** — es gibt dann keine zweite Ausnahme, die die VM
anhängen müsste. Fassung 2 verkaufte B als „kostet ein Feld nur, wer es nutzt"; das war behauptet
und ist falsch, weil die VM, nicht die Klasse, schreibt. **Bricht**: nein. **Vertrauen**: gelesen,
gemessen (`p15`, `r11`). **Hängt an**: Interface-Gebiet (Default auf eingebautem Interface), F7, F20.

---

### F10 — Trägt ein Throwable eine SPUR, und was meldet ein entkommener Fehler?

**Heute**: keine Spur; Report ist Typname plus eine Frame-Zeile (`r06`; `Interpreter.cs:584–585,
614–615`); Frames sind beim Fund des Handlers recycelt (`Interpreter.cs:1232–1236`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A1 | `message()` im Report rufen | — | klein |
| A2 | Wurfstelle mitführen | — | Frames sind weg; dieselbe VM-Arbeit wie C |
| B | Spur beim `throw` erfassen | Java, C#, Kotlin | Allokation pro Wurf; ~0,5 µs heute (F24) mindestens verdoppelt |
| C | Error Return Trace im `debug`-Profil | Zig | beste Information pro Kosten; Profilfrage |
| D | nichts über A1 hinaus | Go | keine Herkunft |

**Empfehlung: A1 sofort, C als 5.0-Kandidat unter `debug`, F30-A als billigere Hälfte von A2.**
**Neu**: sobald eine Spur existiert, braucht F34 die Regel, ob `throw e` aus einem `catch` sie
behält (C# `throw;`) oder neu setzt (C# `throw e;`). **Bricht**: nein. **Vertrauen**: gemessen,
gelesen. **Hängt an**: F30, F34, F24, VM-Gebiet.

---

### F11 — Darf `main` werfen?

**Heute**: nein (`SEM0021`, `p16b`; `09-errors.md:52–54`). Aber `VM0010` ist aus Quelltext
erreichbar (`r06`, Loch j) und **exit-codet heute 101** — die Exit-Tabelle (`docs/Bytecode.md:1085–1094`)
vergibt `101` für die Panik, `1` für „never started", `2` für Aufruffehler, und sagt selbst: „`101`,
`1` and `2` collide with a program returning those values. Callers that need the distinction read
stderr."

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt verboten | Lyric 4 | Zeremonie in jedem `main` |
| B | `fn main(): int throws E` erlaubt; entkommene Ausnahme meldet `message()` (F10-A1) auf stderr mit Code `LYR-VM0010` und beendet mit **101** | Rust `fn main() -> Result` (Exit 1 dort) | **kein CLI-Bruch**: 101 ist heute schon der Exit einer entkommenen Ausnahme (`r06`); die Unterscheidung von der Panik ist der Code auf stderr — genau die Politik, die die Tabelle vorgibt |
| C | wie B mit eigenem Code (Vorschlag 102) | — | ein vierter reservierter Wert; kollidiert **genauso** mit einem `main`, das 102 zurückgibt; und die Doppelung bleibt: Loch (j) → 101, werfendes `main` → 102 |

**Empfehlung: B — geändert gegenüber Fassung 2 (102).** Fassung 2 nannte 102 „den einzigen Weg, der
‚lief das Programm überhaupt?' erhält"; das war überzogen: die Tabelle nimmt Kollisionen ausdrücklich
in Kauf, verweist auf stderr, und eine entkommene Ausnahme hat heute bereits 101. `E` muss nichts
zusätzlich erfüllen — `Throwable.message()` ist Pflicht (die frühere B/C-Trennung über `Display` ist
gegenstandslos). **Bricht**: nein. **Vertrauen**: gemessen (`p16`, `p16b`, `r06`), gelesen.
**Hängt an**: F10-A1, F23.

---

### F12 — Was ist werfbar: nur Klassen?

**Heute**: vier Antworten für ein Enum (Loch i), darunter ein ICE (`r08`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Klassen und Interfaces; Enums an der **Deklaration** refusen | Lyric 4 + PR #169 | schließt den ICE; Trägerklasse-plus-Kind bleibt (`stdlib/std/io/error.lyr:7–10`) |
| B | Enums werfbar | Zig, Rust | **Formatfrage, korrigiert**: nicht der Slot (`Bytecode.md:392` sagt nur „slot index + 1"), sondern der Fat Pointer beim Binden (`Interpreter.cs:1216–1222` braucht eine Referenz) und `throw`s „type index + 1" (`Bytecode.md:887–889`), das einen Klassentyp meint. Ob das ein Formatschnitt oder nur VM-Arbeit ist: **ungemessen** |
| C | Struct werfbar | — | verworfen (`09-errors.md:37–38`) |

**Empfehlung: A, sofort als Fehler** (kein laufendes Programm zu schützen; nur tote Deklarationen
`r09`/`r19` brechen). **Bricht**: minor. **Vertrauen**: gemessen. **Hängt an**: F2-B, Enum-Gebiet.

---

### F13 — Erschöpfung und tote Klauseln

**Heute**: keine Diagnose für totes `try` und tote Klausel (`r26`); **und keine für verdeckte
`catch`-Klauseln** (`n03`, `n07`) — letzteres ist F35.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Warnung „kann nie greifen" / „wirft nie" | Zig, Rust `unreachable_patterns` | braucht F2/F3/F19, sonst warnt sie falsch (`r06`) |
| B | zusätzlich Hinweis auf ein `catch (_)`, das nur wegen des Kollapses dasteht | — | nur mit F2 |
| C | nichts | Java | toter Fehlercode lebt |

**Empfehlung: A nach F2, F3, F19; F35 separat und früher.** **Bricht**: nein. **Vertrauen**: gemessen.
**Hängt an**: F2, F3, F19, F35.

---

### F14 — Gibt es irgendwo ein `recover`?

**Heute**: nein (`p29`; `07-statements.md:132–133`). `std.test` hat keine Panik-Behauptung (grep: 0);
`lyrtest` fängt `ScriptPanicException` (`src/Lyrtest/Program.cs:181–186`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt absolut; der Testläufer bekommt die Grenze | Rust `#[should_panic]` | braucht F29 |
| B | `recover` an einer Koroutinen-/Task-Grenze | Go, Erlang | zweiter Fangmechanismus |
| C | `catch_unwind` in `std` | Rust | dasselbe, versteckt |
| D | nichts | Lyric 4 | `assertPanics` unbaubar |

**Empfehlung: A.** Die Frage, ob eine Panik die `defer`-Kette läuft, ist **davon getrennt** — F33.
**Bricht**: nein. **Vertrauen**: gemessen, gelesen. **Hängt an**: F29, F33, F36.

---

### F15 — Fehler über die FFI-Grenze (Host → Lyric)

**Heute**: .NET-Ausnahme ist Panik `VM0016` (`p28`; `DotnetBinding.cs:26–28`,
`appendix-a-diagnostics.md:290`); `extern` trägt keine Klausel (`Grammar.md:184`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `extern` darf `throws HostError` deklarieren; nur dann Übersetzung, sonst Panik | C# P/Invoke `SetLastError`-Opt-in | ein stdlib-Typ, kein Format |
| B | **jede** Host-Ausnahme wird `HostError` | **Python-C-API** (`NULL` + gesetzte Exception, automatisch propagiert), **.NET-COM-Interop** (HRESULT → Exception) — **nicht JNI**, das nichts automatisch übersetzt (Fassung 2 falsch, *behauptet*) | heute gültige Programme brechen |
| C | bleibt Panik | Lyric 4 | die halbe geplante std erbt es |
| D | A plus Zuordnungstabelle | Go `syscall` | nie fertig |

**Empfehlung: A.** **Bricht**: nein. **Vertrauen**: gemessen, gelesen. **Hängt an**: F23, FFI-Gebiet.

---

### F16 — Bleibt `Result` reine Bibliothek?

**Heute**: ja (`stdlib/std/result.lyr:1–14`); `orThrow` leidet unter F4; kein `?`
(`lyric-v5-features.md:152`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Bibliothek; Brücken `orThrow` und (F5-C) `try e` | Swift, Kotlin | nichts Neues |
| B | Sprachanker | Kotlin | Kopplung, in §11 zu benennen |
| C | `?` auf `Result` | Rust | ausgeschlossen — Rule 2 |

**Empfehlung: A, B nur so weit F5-C es verlangt.** **Bricht**: nein. **Vertrauen**: gelesen.
**Hängt an**: F4, F5, F36 (ein Task-Ergebnis ist ein `Result`-Kandidat).

---

### F17 — `throws` in der Konformanz mit Mengen und Typparametern

**Heute**: exakt, Klausel eingeschlossen (`SEM0042`, `r27`; weniger erlaubt, `p26b`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Teilmenge modulo Substitution; Klausel-Typparameter an Interface-Membern | Swift 6; Java `ThrowingFn<T,R,E>` | teuerster Teil |
| B | konkrete Klauseln an Interface-Membern | Lyric 4 | `std.iter` mit werfenden Stufen unschreibbar — die Java-Bibliotheksfalle |
| C | Klausel zählt nicht zur Konformanz | — | bricht F1 |

**Empfehlung: B für 5.0, A als Nachzügler, mit dem Vermerk, dass B die Java-Falle nachbaut.**
**Bricht**: nein. **Vertrauen**: gemessen, behauptet (Java). **Hängt an**: F2, F3, F19.

---

### F18 — Was wird aus den `OrThrow`-Zwillingen?

**Heute**: gebaut und tragend (`p31`); doppelte API.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt; `try?` (F5) macht die stille Form für den Aufrufer entbehrlich | Lyric 4 | doppelte Oberfläche |
| B | nur die werfende Form; `try?` erzeugt die stille am Aufruf | Swift, Zig | **major**, und **das Kostenargument gegen B ist zurück** (F24): unter `LYRIC_JIT=1` ist die stille Form gemessen ~3× billiger, und jede Funktion mit `try` bleibt interpretiert (`JitCompiler.cs:712–726`). B würde **jeden** Dateizugriff aus dem JIT nehmen |
| C | nur die stille Form, Grund über `Result` | Go-nah | wirft §9.0 weg |

**Empfehlung: A — geändert gegenüber Fassung 2 (B).** Fassung 2 stützte B auf eine Interpreter-Messung
und nannte JIT „ungemessen", baute aber darauf. Gemessen (F24) kehrt sich das Verhältnis um. B wird
erst wieder diskutierbar, wenn F37 den Wurfpfad in den JIT holt. Ein `lyrfix`, das Fassung 2 als
Abhängigkeit nannte, **existiert nicht** (grep über `src/`, `STATUS.md`, `PLAN.md`, Guide: 0 Treffer).
**Bricht**: A nein. **Vertrauen**: gemessen. **Hängt an**: F5, F24, F31, F37.

---

### F19 — Vergleicht die Deckungsrechnung Generik-INSTANZEN oder nur Definitionen?

**Heute**: nur Definitionen (`ExceptionAnalyzer.cs:250`); `r06` kompiliert und stirbt mit `VM0010`,
`r07` fängt. Die VM entscheidet richtig (Typidentität der Instanz), die Sema nicht.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Deckung auf Instanzebene | Swift 6 (typed throws mit konkreten Instanzen); **Lyrics eigene VM** (`r07`). **Nicht C#** — C# hat keine statische Deckungsrechnung (Fassung 2 führte es; das war die Verwechslung von Laufzeit- und Übersetzungszeitprüfung, die §2 an C++98 kritisiert) | Bugfix in `ThrownOf`; interagiert mit F4 (Phantom-`E` hat keine Instanz) |
| B | generische Throwables verbieten | Zig | schließt das Loch mit einer Zeile; nimmt Trägerklassen die Form |
| C | Instanzebene, offene Parameter bleiben `Throwable` | Lyric 4 + A | kleinster Schritt |
| D | nichts | Lyric 4 | ein geprüftes System, das zur Laufzeit stirbt |

**Empfehlung: A mit F4 in einem Zug, vor F2-A.** **Bricht**: minor (nur heute fälschlich
Akzeptiertes). **Vertrauen**: gemessen. **Hängt an**: F4, F2, F13.

---

### F20 — Welche Ausnahme erreicht den `catch`, wenn ein `defer` wirft, während bereits eine fliegt?

**Heute**: die zweite; die erste ist weg; die restliche Kette läuft nicht (`r11`;
`Interpreter.cs:1197–1198`). **Normativ ist der Fall als offen erklärt** — `07-statements.md:135–141`
nennt „what the exception does to the exit path it interrupted" ausdrücklich. Fassung 2 schrieb
„Normativ steht dazu nichts"; falsch.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | erste propagiert, zweite *suppressed*, Kette läuft | Java try-with-resources; Python `__context__` (ohne Liste) | braucht F9-B **mit dessen Kosten** (§1.1); Abwickler muss anhängen statt ersetzen |
| B | zweite gewinnt (heute), Kette läuft | **Java `finally`, C#** (nicht Go — Gos Laufzeit verkettet beide Paniken und druckt beide, *behauptet*) | verliert die Ursache |
| C | werfender `defer` während einer Abwicklung ist eine Panik | — | `defer { w.flushOrThrow(); }` wird Zeitbombe |
| D | Übersetzungszeitfehler (= F7-D) | Swift, Zig | löst F7, F20 und (f) mit einer Regel |

**Empfehlung: D, als ein Entscheid mit F7.** Die drei Pfade müssen dieselbe Antwort bekommen; D gibt
allen dreien dieselbe: es gibt keinen werfenden `defer`. **Uhr**: `SEM0110` steht am richtigen Ort
(Wurfstelle im `defer`-Körper), und mit D ist ihr Text vollständig — der Fall „während bereits eine
fliegt" braucht keine eigene Aussage mehr. **Bricht**: minor (wie F7). **Vertrauen**: gemessen,
gelesen. **Hängt an**: F7, F9, F21.

---

### F21 — Ist „die Kette" die des Blocks oder der Funktion?

**Heute**: des Blocks, **und das steht normativ**: `07-statements.md:129–132` — „the enclosing block's
exit … a `defer` in a loop body runs once per iteration, at that iteration's end". Gemessen `r12`,
`p36`. Fassung 2 schrieb „was fehlt, ist der Satz in §7.5"; der Satz steht seit je dort. Gos `defer`
ist funktions-scoped; **Swifts und Zigs sind scope-gebunden** wie Lyrics (Fassung 2 ließ Swift weg).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt block-scoped (normativ, gebaut) | Swift, Zig, C++ RAII | nichts. Die Ordnung geschachtelter Ketten beim Abwickeln (innerste zuerst, jede vollständig) folgt aus „block's exit" plus „reverse scheduling order" — ein klarstellender Halbsatz ist alles, was §7.5 vertragen könnte |
| B | funktions-scoped | Go | major; Gos Schleifenfalle |
| C | beides (`defer fn { … }`) | — | Rule 2 |

**Empfehlung: A — und die Frage ist damit im Kern GESCHLOSSEN.** Ihr Ertrag bleibt: F7 darf sich
nicht auf Gos Autorität stützen, weil Lyric beim Geltungsbereich bereits abweicht; und mit F7-D ist
die Ordnung beim Abwickeln keine Frage mehr, weil keine Stufe werfen kann. **Bricht**: nein.
**Vertrauen**: gemessen, gelesen. **Hängt an**: F7, F20.

---

### F22 — Wann laufen die `defer` einer abgebrochenen Koroutine?

**Heute**: nie (`r13`), erschöpft: ja (`r13b`) — **und das ist normiert**: `spec/10-coroutines.md:89–93`
(„runs NOTHING … Cleanup that must happen belongs to whoever drives the coroutine to its end; the
garbage collector is not an exit path and does not become one here"), `07-statements.md:143–146`.
Fassung 2 behandelte (k) als unbemerktes Leck, wollte Option A „in §10 als Satz ergänzen" (steht
dort) und schloss C nur mit `STATUS.md:626–628` aus (§10 schließt es normativ aus).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt so; Cleanup ist Sache des Treibers (**normativ, §10:92–93**) | Lyric 4 | ehrlich; der Compiler prüft es nicht |
| B1 | `close` auf `Coroutine<T>`, das die Kette abwickelt und **nur `finally`-Regionen** (die `defer`) laufen lässt, keinen `catch` | **C# Iterator-`Dispose()`** (läuft `finally`, überspringt `catch`, nie beim GC) | ein Verb; die VM hat `Resume` mit Handlerkind-Unterscheidung (`Interpreter.cs:1191–1200`) — ein Durchgang, der `kind 0` ignoriert. Kein Typ, den das `catch` im Körper sehen könnte, also keine Änderung an der Throwability des Pulls (§10 „What stays static") |
| B2 | `close` wirft eine Ausnahme **in** den Körper (`CoroutineClosed`), die ein `catch` dort sehen kann | Python `generator.close()` → `GeneratorExit` | braucht einen Throwable-Typ, den jeder Koroutinenkörper implizit werfen kann — das ändert die statische Throwability jedes Pulls, die §10 gerade festschreibt |
| C | Laufzeit wickelt beim GC ab | **Python PEP 342** (Generator-Finalizer ruft `close()`) — **nicht C#** (Fassung 2 falsch) | nicht-deterministisch; **normativ ausgeschlossen** (§10:92–93) |
| D | `defer` im Koroutinenkörper verboten | — | nimmt `r13b` weg |

**Empfehlung: B1.** Die Präzisierung gegenüber Fassung 2 ist der Punkt, den die Kritik anmahnte:
**B1 löst nur die vorhandene Kette aus** und ist damit kein zweiter Mechanismus; B2 wäre einer (eine
implizite Ausnahme, die die statische Throwability aller Pulls ändert). A steht bereits, nichts zu
ergänzen. **Bricht**: nein (additiv). **Vertrauen**: gemessen, gelesen. **Hängt an**: Koroutinen-Gebiet,
F33 (derselbe Abwickler ohne `catch`), F36.

---

### F23 — Was sieht ein Host, wenn eine Lyric-AUSNAHME entkommt?

**Heute**: eine Panik (`Interpreter.cs:583–585, 612–615`; `ScriptException.cs:23–26`); Unterscheidung
nur am `Code`; `message()` erreicht den Host nie.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | eigene `ScriptThrowException : ScriptException` mit Typname, `message()`, Backtrace | Lua `lua_pcall`-Fehlerobjekt | eine Klasse; braucht F10-A1, Backtrace braucht F10-A2/F30 |
| B | nur den `Code` dokumentieren | Lyric 4 | Strings vergleichen |
| C | Lyric-Ausnahme durch einen Host-Callback bleibt Lyric-Ausnahme | (Callback-Runde, ungebaut) | ungemessen |
| D | Panik, und `main` darf nicht werfen | Lyric 4 | `Invoke` ruft nicht `main` |

**Empfehlung: A mit F10-A1, C als Vorbehalt.** **Bricht**: minor für Hosts, die auf
`ScriptPanicException` typprüfen. **Vertrauen**: gelesen. **Hängt an**: F10, F11, F15, F29.

---

### F24 — Was kostet ein Wurf — im Interpreter UND im JIT?

**Fassung 2 maß nur den Interpreter.** Neu gemessen, `r23`–`r23d`, 2 000 000 Iterationen, je 3 Läufe
im Wechsel, Wanduhr um den Prozess; **Maschine unter Last** (der Maintainer baut parallel), deshalb
sind die Absolutwerte höher als in Fassung 2 und nur die **Verhältnisse** belastbar:

| Probe | Bau | ohne JIT, min (Läufe) | `LYRIC_JIT=1`, min (Läufe) |
|---|---|---|---|
| `r23` werfend, 1 000 000 Würfe | `try { … } catch (_: Boom)` | 1789 (2703/2716/1789) | 1504 (2788/1881/1504) |
| `r23d` **Kontrolle**: identisch, 0 Würfe | dieselbe Funktion | 1151 (1232/1151/1157) | 1049 (1274/1390/1049) |
| `r23b` still, `?int` | `if (r == null)` | 1785 (3097/1785/1937) | **488** (744/492/488) |
| `r23c` Basislinie | leere Schleife | 765 (1069/765/852) | 277 (457/323/277) |

Ausgaben plausibel (`r23`/`r23b` `1000001000000`, `r23d` `1999999000000`).

**Drei Aussagen:**
1. **Interpreter**: ein Wurf kostet in der Größenordnung 0,3–0,6 µs (1789 − 1151 ≈ 640 ms / 10⁶,
   bei Last); die stille Form ist dort **nicht** billiger (1785 gegen 1789) — Fassung 2 bestätigt.
2. **JIT**: die stille Form ist **~3× billiger** als die werfende (488 gegen 1504) und **~2× billiger
   als dieselbe `try`-Funktion ohne einen einzigen Wurf** (488 gegen 1049). Die `try`-Probe wird
   unter JIT **gar nicht** schneller (1151 → 1049), die stille um den Faktor 3,7 (1785 → 488).
3. **Grund**, gelesen: `JitCompiler.cs:712–726` lehnt `throw` ab und verlangt, dass jeder Callee
   kompiliert — sonst lehnt der Caller ab („a compiled frame between a throw and its handler breaks
   the chain"). `maybeThrow` enthält `throw`, also bleibt der Aufrufer interpretiert; die
   `try`-Region selbst ist nicht das Hindernis, der werfende Callee ist es. Siehe F37.

| | Beschreibung | Preis |
|---|---|---|
| A | §9.0 als Lesbarkeitsentscheid schreiben | falsch unter JIT |
| B | §9.0 bekommt den Satz „die stille Form ist die billige, und zwar im JIT-Profil" | ehrlich; macht F18-B endgültig zum Kostenentscheid |
| C | Wurf-Zählung als Pin (Regression) | ein Test |
| D | Release-Profil zusätzlich messen | eine Messsession; Release ist weiter ungemessen |

**Empfehlung: B und C; D vor F10-B.** **Bricht**: nein. **Vertrauen**: **gemessen** (mit Kontrolle,
beide Profile), Release ungemessen. **Hängt an**: F18, F37, F10-B.

---

### F25 — Welchen statischen Typ hat `e` in `catch (e: A | B)`?

**Heute**: Form existiert nicht. Bindungsformen `09-errors.md:55–59`; Fat Pointer
`Interpreter.cs:1216–1222`. Keine Vererbung, keine Union-Typen.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `e: Throwable` | — | trivial; ohne F2-D nutzlos (`p41`) |
| B | anonymer Summentyp `A \| B` | Scala 3, TypeScript | neues Typkonstrukt — Rule 2 |
| C | kleinster gemeinsamer Interface-Obertyp | Java 7 lub | stiller Bruch bei neuer Konformanz |
| D | keine Multi-Catch-Form; `throws A, B` kommt, Fangseite bleibt eine Klausel pro Typ | — | löst (a) zur Hälfte, kostet nichts; **und die Fangseite hat schon heute eine unnormierte Reihenfolgefrage** (`n07`) — F35 muss VOR D entschieden sein, weil jede Liste in der Klausel die Verdeckung verschärft |

**Empfehlung: D, mit F35 zusammen.** **Bricht**: nein. **Vertrauen**: gemessen. **Hängt an**: F2,
F35, Pattern-Gebiet.

---

### F26 — Braucht jede v5-Syntax in 4.x eine gezielte „kommt in 5.0"-Diagnose?

**Heute**: nein (`n08`: 3; `r30`: 5; `r29`: 16; `r16`: 1 mit unerfüllbarem Rat). Gegenbeispiel:
`SEM0110` (`ExceptionAnalyzer.cs:299–303`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | eigene Diagnose pro geplanter Form | Rust `is unstable`, Swift `only available in` | ein Parser-Pfad pro Form |
| B | nur die lautesten Fehlablenkungen (`SEM0051` auf ein Komma; `SEM0034` auf Modulebene) | — | trifft die zwei Stellen, die in die Irre führen |
| C | nichts | Lyric 4 | 16 Diagnosen |

**Empfehlung: B sofort, A für `throws A, B` und `try?`.** **Bricht**: nein. **Vertrauen**: gemessen.
**Hängt an**: F2, F5, F39, Diagnostik-Gebiet.

---

### F27 — Ist das Erweitern einer `throws`-Klausel ein Bruch?

**Heute**: niemand prüft es (`spec/11-stdlib-contract.md:38–41`: nur die Deprecation-Politik).
Mechanik gemessen: `throws A` → `throws A, B` bricht typisierte Aufrufer (`r32`), Verkleinern ist
kompatibel (`p26b`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | §11 schreibt: Erweitern ist major, Verkleinern minor | Swift (Resilience); **Java als Gegenbeispiel** — nach JLS §13.4.21 ist die Klausel **kein** ABI-Vertrag, sondern Quellvertrag (*behauptet*; Fassung 2 schrieb das Gegenteil) | ein Absatz; hält nur, wer liest |
| B | wie A plus Werkzeugvergleich gegen die Vorversion | Rust `cargo-semver-checks`, .NET `ApiCompat` | braucht, dass das Artefakt die Klausel trägt — **F40** |
| C | Klausel kein Paketvertrag | — | wirft F1 weg |
| D | nichts | Lyric 4 | still brechende `std`-Erweiterung |

**Empfehlung: A für 5.0, B nach F40.** Der Java-Punkt ist der lehrreiche: eine Klausel, die nur in der
Quelle lebt, kann kein Werkzeug versionsübergreifend prüfen. **Bricht**: nein. **Vertrauen**:
gelesen, gemessen; Java behauptet. **Hängt an**: F2, F40, Paket-Gebiet.

---

### F28 — `never`: die ausgelassene Hälfte eines Entscheids

**Heute**: Rückgabeposition funktioniert (`r04`); Parameter/Element/Typargument sind ICEs (`r05`,
`r24c`, `r24d`); Grammatik kennt `never` nicht. **Und die Regel ist entschieden**:
`design/throw-expression.md:34–37` — Rückgabetyp gültig, alle anderen Positionen „in dieser Runde
nicht eingeschränkt", Empfehlung für die Spec „a return type only … plus ein `LYR-SEM`-Fehler an
jeder anderen Stelle (nicht im Prototyp)". Fassung 2 stellte das als neue Frage mit vier Optionen.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **den Entscheid zu Ende bauen**: `never` nur in Rückgabeposition, sonst `LYR-SEM`-Fehler; Grammatik und §3.1 nachziehen | **Rust `!`** (stabil nur als Rückgabetyp; `never_type` instabil), TypeScript | drei ICEs werden drei Diagnosen; `panic`/`fail` können `never` sagen |
| B | überall zulässig, echte Bottom-Repräsentation | Kotlin `Nothing` (nicht Rust — Fassung 2 falsch) | teuer; `Box<never>` bewohnbarer Name für Unbewohnbares |
| C | kein Typ, `diverges`-Schlüsselwort | — | Rückbau von `r04` |

**Empfehlung: A — als Bugfix-Posten (Anhang 3–5), keine v5-Designfrage.** Einziger offener Rest des
Entscheids: `?never` und `never[]` **in Rückgabeposition** kompilieren heute folgenlos (`r24a`,
`r24b`) — „a return type only" sollte sie einschließen oder ausschließen, ein Satz. **Bricht**: nein.
**Vertrauen**: gemessen, gelesen. **Hängt an**: Typ-Gebiet, Grammatik, `core.lyr:23`, `test.lyr:89–90`.

---

### F29 — Woran erkennt ein `assertPanics` die ERWARTETE Panik?

**Heute**: Behauptung fehlt; Test fällt durch Panik (`test.lyr:8`); `lyrtest` fängt
`ScriptPanicException` und `ScriptException` getrennt (`Program.cs:181–193`); eine entkommene
Ausnahme kommt als Panik mit `VM0010` an (F23).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Nachricht prüfen: `assertPanics(f, expected)` | Rust `#[should_panic(expected)]` | Tippfehler → grün-falsch |
| B | Laufzeit-Marker | — | braucht `recover` |
| C | `@Panics("…")` auf der Testfunktion | Rust `#[should_panic]` | ganzer Test muss panicken |
| D | keine; `assertThrows` (F3) reicht | — | `assert`/`unreachable` unprüfbar |

**Empfehlung: C mit A.** **Bricht**: nein. **Vertrauen**: gelesen; werfender Test abgeleitet.
**Hängt an**: F14, F23, F3, F33 (ob Paniken `defer` laufen, entscheidet, was ein Test hinterlässt).

---

### F30 — Was tun die Werkzeuge an einem Wurf?

**Heute**: nichts. DAP: „no exception filters" (`src/Lyric.Dap/DapServer.cs:182–189`); LSP: `Throws`
0 Treffer in `src/Lyric.Lsp/LspServer.cs` (Hover, SignatureHelp, InlayHint existieren: Z. 196, 232,
240 — keiner zeigt die Klausel); kein `lyrdoc`, kein `lyrfix`.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | first-chance-Halt im Debugger | C#/Java/Python | einziger Moment, in dem die Wurfstelle existiert; kein Format |
| B | Halt nur bei ungefangenen | .NET | zweiter Durchgang |
| C | LSP-Inlay/Hover mit Klausel | Rust-Analyzer, IntelliJ | Sema muss die Information herausgeben |
| D | nichts | Lyric 4 | unsichtbar |

**Empfehlung: A zuerst, C mit F6/F41, B danach.** **Bricht**: nein. **Vertrauen**: gelesen.
**Hängt an**: F6, F10, F41.

---

### F31 — Was liefert `try?` auf einer Funktion, die schon `?T` zurückgibt?

**Heute**: `r14` (`?int throws Boom`) ist gültig; `try?` gibt es nicht.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `try?` kollabiert; `null` heißt beides | **Swift 5 (SE-0230) — kollabiert** (Fassung 2 behauptete das Gegenteil; `design/try-expression.md:113` hat Swifts Kollaps übernommen) | stiller Informationsverlust |
| B | `try?` auf `?T`-Rückgabe ist ein Fehler | — | scharf; Form bleibt ausschreibbar |
| C | `?T`-Rückgabe darf in 5.0 nicht werfen | — | erzwingt §9.0 als Regel; bricht `r14` |
| D | `??T` | Swift 4 | nicht baubar ohne Typarbeit |

**Empfehlung: B, C als §9.0-Kandidat — Begründung neu.** Swift ist Präzedenz für A, nicht gegen sie;
das Argument für B ist **Lyric-eigen**: §9.0 sagt, ein Wert antwortet WHETHER und ein Wurf WHY, also
ist eine Funktion, die beides tut, schon doktrinwidrig, und `try?` darauf würde zwei Antworten in
ein `null` falten. **Bricht**: B nein, C minor. **Vertrauen**: gemessen; Swift behauptet.
**Hängt an**: F5, F18.

---

### F32 — Was davon wird in 5.0 normativ geschrieben?

**Heute — korrigiert**: von den drei Regeln aus Fassung 2 stehen **zwei im Anhang A**, der nach §12.2
normativ ist:

| Regel | Erzwungen als | Normativ? |
|---|---|---|
| `try` braucht ein `catch` | `PAR0023` + `SEM0036` (`r22`) | **ja** — `appendix-a-diagnostics.md:62, 138`; **aber `docs/Grammar.md:395` widerspricht** (`{ CatchClause }`) |
| Catch-all zuletzt | `SEM0035` (`p13`) | **ja** — Anhang A Z. 137; §9.3 erwähnt es nicht |
| Wurf aus einem `catch` wird von der Schwesterklausel nicht gefangen | Verhalten (`r15`) | **nein** |
| Verdeckte `catch`-Klausel | nichts (`n03`, `n07`) | **nein** — F35 |

Dazu §1.4: Interface-Catch (Spec und Anhang veraltet), `&&=` (Anhang veraltet, `n09`),
`Bytecode.md:891`, `Interpreter.cs:1175`, `never` in der Grammatik.

| | Beschreibung | Preis |
|---|---|---|
| A | Grammatik an den Anhang angleichen (`'try' Block CatchClause { CatchClause }`), Wurf-aus-`catch` und Verdeckung (F35) normieren, die veralteten Stellen korrigieren; Suite-Fälle | ein Spec-PR, spec-first |
| B | nur die Grammatik | `r15`-Verhalten und F35 bleiben in einer zweiten Implementierung frei |
| C | nichts | Suite beurteilt Unbeschriebenes |

**Empfehlung: A, ein Spec-PR vor jedem v5-Feature.** Die Doppeldiagnose `PAR0023`+`SEM0036` gehört
dazu: verbietet die Grammatik die Form, reicht der Parser. **Bricht**: nein. **Vertrauen**: gemessen,
gelesen. **Hängt an**: Spec, Suite, F28, F35.

---

### F33 — Läuft eine Panik die `defer`-Kette? *(neu)*

**Heute**: **nein, gemessen und normiert.** `n01` (Panik im selben Frame) und `n01c` (Panik im Callee,
`defer` im Caller): kein `DEFER`, Exit 101; Kontrolle `n01b` (`return`): `DEFER` läuft.
`07-statements.md:132–133`: „a panic aborts, it does not unwind". Der Budgetstopp ebenso, mit
Begründung: `appendix-a-diagnostics.md:266` — „a stop the program could catch, **or run a `defer`
behind**, would be one it could sit out"; `VmDiagnostics.cs:36–42` („arrives as a panic all the same").

Warum es eine Frage ist: `defer` ist der **einzige** Cleanup, und `STATUS.md:635–640` protokolliert
das Ressourcenleck über die Sandbox-Grenze, das „a budget stop, a guest panic" überlebt. Go — das
erklärte Vorbild dieses `defer` — **läuft** die defers beim Panic; Rust kennt `panic=unwind|abort`
als Profil. Fassung 2 setzte „Panik = abort" in F14 als gegeben.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt: Panik läuft nichts | Lyric 4; Rust `panic=abort`; Sutter/Midori (abandonment) | ein Guest-Panic hinterlässt offene Handles — **aber** das Leck ist laut `STATUS.md:637–640` eine Frage der VM-eigenen Handle-Tabelle (ThreadStatic), nicht des Abwicklers, und ein Host, der `dispose`t, ist die vorgesehene Antwort |
| B | Panik wickelt ab und läuft `defer` (nur `finally`-Regionen, kein `catch`) | **Go**; Rust `panic=unwind` | die `defer`-Körper laufen in einem **kaputten** Programm — genau das, was §9 „programming error" nennt; ein `defer`, der selbst panickt, braucht eine Doppel-Panik-Regel (Go hat eine); und der Budgetstopp **darf** es nicht (Anhang A:266 — ein `defer` könnte ihn aussitzen), also hätte die Sprache zwei Panik-Arten |
| C | Profilwahl (`debug`: abwickeln, `release`: abort) | Rust | Build v2 hat Profile; aber ein Programm, das im Debug-Profil aufräumt und im Release nicht, ist die Art Unterschied, die §12.1 („never a code's severity") gerade vermeidet |
| D | kein Abwickeln, aber die VM gibt beim Abbruch ihre Handle-Tabelle frei (Host-`Dispose`, `std.os.exit`) | — | löst das **Sandbox**-Leck, ohne dass Gast-Code läuft; kein Sprachentscheid |

**Empfehlung: A + D.** Das Sandbox-Leck ist real, aber es hängt nicht an der `defer`-Kette: Gast-Code
nach einer Panik laufen zu lassen, ist das Gegenteil des Sicherheitsversprechens (ein Skript, das im
`defer` weiterrechnet, sitzt den Budgetstopp aus — der Anhang sagt es). Die Antwort ist die
**VM-eigene** Freigabe beim Abbruch (D), die `STATUS.md:635–640` als offenen Posten der
ThreadStatic-Tabellen bereits benennt. B ist Gos Antwort für eine Sprache mit `recover`; Lyric hat
keins (F14). **Bricht**: nein. **Vertrauen**: **gemessen** (`n01`, `n01b`, `n01c`), gelesen (§7.5,
Anhang A:266, `STATUS.md`). **Hängt an**: F14, F22 (derselbe „nur `finally`"-Durchgang, falls B),
Sandbox-/Einbettungs-Gebiet.

---

### F34 — Gibt es ein präzises Rethrow? *(neu)*

**Heute**: nein. `n02b`: `catch (e) { throw e; }` in `fn work(): int throws Boom`, dessen `try`-Körper
nur `Boom` werfen kann, ist `SEM0034` („'throw' may throw 'Throwable'"). `e` ist `Throwable`
(`09-errors.md:56–57`), und `ThrownOf` sieht nur den statischen Typ (`ExceptionAnalyzer.cs:246`).
Kontrollen: `n02a` (`catch (e: Boom)`) und `n02c` (`throws` bar) kompilieren und laufen.

Warum es zählt: `catch (e) { log(e.message()); throw e; }` ist der häufigste Grund, überhaupt zu
fangen — und in einer typisierten Funktion geht er nicht. Und sobald F10 eine Spur einführt, muss die
Sprache sagen, ob ein Rethrow sie behält.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt; wer rethrowen will, fängt typisiert oder deklariert bar | Lyric 4 | typisierte Funktionen können nicht generisch loggen-und-weiterwerfen |
| B | **precise rethrow**: `throw e` mit einer unveränderten Catch-Bindung `e` wirft statisch die Wurfmenge des `try`-Körpers (abzüglich der von früheren Klauseln gefangenen) | **Java 7** | reine Sema: der `ExceptionAnalyzer` kennt die Menge des Körpers bereits; braucht „Bindung nicht neu zugewiesen" (Catch-Bindungen: `09-errors.md:58` „the binding has the declared type"; ob sie zuweisbar ist: **ungemessen**) |
| C | eigene Form `throw;` (bar) im `catch` | C# | zweite Wurfform — Rule-2-Frage; dafür ist die Spur-Regel syntaktisch sichtbar |
| D | mit F25/F2: `catch (e: A \| B)` deckt den typisierten Fall | — | löst nur die Fälle mit aufgezählter Menge, nicht `catch (e)` |

**Empfehlung: B**, und mit F10 die Regel „`throw e` aus einem `catch` **behält** die Spur" (das ist
C#s `throw;`-Semantik, ohne zweite Form; C#s `throw e;`-Reset ist eine bekannte Falle). B ist
additiv und macht F1-A erst glaubwürdig: eine geprüfte Klausel, die man beim Weiterwerfen verliert,
zwingt zu `throws` bar. **Bricht**: nein (mehr Programme kompilieren). **Vertrauen**: **gemessen**
(`n02a`, `n02b`, `n02c`), gelesen. **Hängt an**: F2, F10, F25, F35 (welche Klauseln „früher" greifen,
ist die Verdeckungsregel).

---

### F35 — Ist eine verdeckte `catch`-Klausel ein Fehler, eine Warnung oder nichts? *(neu)*

**Heute**: nichts. `n07`: `catch (_: AppError)` (Interface) vor `catch (_: NotFound)` (Klasse, die es
implementiert) — kompiliert ohne Diagnose, die zweite Klausel ist still tot (`iface`); Kontrolle
`n07b` (Klasse zuerst): beide erreichbar (`class`). `n03`: zwei `catch (_: Boom)` — keine Diagnose,
`first`. `SEM0035` (`SemaRules.cs:228`) prüft nur den Catch-all. Seit dem Interface-Catch (PR #169)
ist die Klauselfolge eine **Subtypfrage**, und die Konformanz ist statisch bekannt — die Prüfung ist
billig. `09-errors.md:55–56` sagt nur „the first matching clause wins".

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Fehler**, wie der Catch-all-Fall: eine Klausel, die eine frühere vollständig verdeckt (gleicher Typ; Klasse nach Interface, das sie implementiert; alles nach `Throwable`), ist `LYR-SEM` | **Java** („exception X has already been caught") | konsistent mit `SEM0035`; **bricht** heute kompilierende Programme mit toten Klauseln (minor) |
| B | Warnung | Rust `unreachable_patterns` | konsistent mit F13; die Klausel bleibt tot |
| C | nichts | Lyric 4 | still tot; mit F2-A/F25-D wird die Verdeckung mit jeder Liste wahrscheinlicher |

**Empfehlung: A, in 5.0; in 4.x als Warnung** (ein neuer Fehler auf heute gültigem Code ist ein
Spec-Wechsel, §12.1 Regel 1 erlaubt den Severity-Wechsel nur am Major). A und `SEM0035` sind
**dieselbe Regel** („eine spätere Klausel muss erreichbar sein"), die heute nur für ihren
Sonderfall geprüft wird — `SEM0035` sollte in ihr aufgehen, nicht daneben stehen. **Bricht**: minor.
**Vertrauen**: **gemessen** (`n03`, `n07`, `n07b`), gelesen. **Hängt an**: F25, F2, F13, F32, F34.

---

### F36 — Wie meldet ein Task sein Scheitern an seinen Spawner? *(neu)*

**Heute**: gar nicht, per Typ. `n10`: `spawn(job())` mit `fn job(): Coroutine<Wait> throws Boom` ist
`LYR-SEM0001` („cannot assign 'Coroutine<Wait> throws Boom' to 'Coroutine<Wait>'"); Kontrolle `n10b`
läuft. `stdlib/std/task.lyr:10–12`: „`spawn` takes the non-throwing coroutine type, because the
scheduler has nobody to hand an exception to. A panic anywhere ends the program". Bleibt: der Wert
(ein Task schreibt sein Ergebnis in geteilten Zustand) und die Panik (tötet alles — Go).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt: Tasks werfen nicht; das Scheitern ist ein Wert im Task | Lyric 4; **Go** (Goroutine-Panic tötet den Prozess; Fehler sind Werte) | jeder Task schreibt `try/catch` um seinen Rumpf — dieselbe Zeremonie wie F11-A für `main` |
| B | `spawn` liefert ein **Handle**; der Task darf werfen (`Coroutine<Wait> throws`), der Scheduler fängt am Pull und legt die Ausnahme ins Handle (`failure(): ?Throwable`, oder mit F16 `result(): Result<void, Throwable>`) | **Rust** `JoinHandle::join() -> Result` | reine Bibliothek: der Scheduler-`resume` steht in einem `try` (`p20` zeigt, dass der Pull die Wurfstelle ist); kein Sprachentscheid |
| C | strukturierte Nebenläufigkeit: ein scheiternder Task beendet `run()` mit dieser Ausnahme, die übrigen werden geschlossen (F22-B1) | **Kotlin** (Scope-Propagation) | braucht F22-B1 (Schließen) und macht `run()` werfend; klar, aber ein Fehler in einem Task nimmt alle mit |
| D | Panik im Task bleibt Prozess-Panik | Go | heute so; F33 entscheidet, ob dabei etwas aufräumt |

**Empfehlung: B als `std.task`-Erweiterung, C als Option auf dem Handle (`run()` bleibt
nicht-werfend; wer C will, fragt die Handles ab).** Kein Sprachentscheid, aber das Gebiet hat ohne
diese Antwort keine Fehlerbehandlung in seinem einzigen Nebenläufigkeitsmodell. **Bricht**: nein
(additiv; `spawn` ist heute `void` — ein Rückgabewert fehlt niemandem). **Vertrauen**: **gemessen**
(`n10`, `n10b`), gelesen. **Hängt an**: F16, F22, F33, Nebenläufigkeits-Gebiet.

---

### F37 — Bleibt der Wurf-/Fangpfad dauerhaft außerhalb des JIT? *(neu)*

**Heute**: ja, per Design. `JitCompiler.cs:712–726`: `throw` wird abgelehnt; jeder Callee muss
kompilieren, sonst lehnt der Caller ab, „and this is not about speed" — kompilierter Code hält keine
Interpreter-Frames, und eine kompilierte Frame zwischen Wurf und Handler bricht die Kette.
`JitCompiler.cs:778–783`: dasselbe für jede Implementierung eines virtuellen Slots. Gemessen (F24):
die `try`-Probe wird unter `LYRIC_JIT=1` nicht schneller (1151 → 1049), die stille um 3,7×.

Folge für die Doktrin: §9.0 verkauft die Wahl der Form als Lesbarkeit; im JIT-Profil ist sie ein
Kostenentscheid um den Faktor 3, und **jede** Funktion, die eine werfende Bibliotheksfunktion ruft,
zieht ihren gesamten Aufruferpfad in den Interpreter.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt; §9.0 bekommt den Kostensatz (F24-B); die stillen Zwillinge bleiben (F18-A) | Lyric 4 | ehrlich; der JIT nützt nur wurffreiem Code |
| B | der JIT kompiliert Handler-Regionen: eine Seitentabelle bildet kompilierte Rücksprungadressen auf `(function, block)` ab, sodass `Resume` die Handler-Tabelle auch für kompilierte Frames befragen kann | .NET/HotSpot (Unwind-Tabellen) | **VM-Arbeit, kein Format** — die Handlers-Sektion ist bereits pro Funktion über Blockbereiche (`Bytecode.md:379–393`); was fehlt, ist die Frame-Zuordnung im JIT. Umfang **ungemessen** |
| C | Format: Handler-Tabelle über Codeoffsets statt Blöcke | — | ein Formatschnitt für ein Problem, das B ohne löst |
| D | Konformanz-Referenzprofil für Kosten festlegen (Interpreter) | — | die Spec fixiert keine Kosten (§11 fixiert Verhalten); ein Referenzprofil wäre neu |

**Empfehlung: B als Posten der Optimierer-/VM-Runde (die laut Design-Runde 2026-09 ohnehin vor den
Stufen B–F liegt), bis dahin A.** D verneint: die Spec normiert keine Kosten, und sollte es nicht
anfangen. **Bricht**: nein. **Vertrauen**: **gemessen** (F24), gelesen. **Hängt an**: F18, F24, F10-C,
VM-Gebiet.

---

### F38 — Ist der Panik-Katalog normativ und geschlossen? *(neu)*

**Heute**: **ja — im Anhang, nicht in §9.4.** `09-errors.md:76–80` nennt fünf Maschinen-Paniken in
Prosa (zwei mit Code). `appendix-a-diagnostics.md:270–291` führt **alle 17** `LYR-VM`-Codes mit
Severity (`VM0001–0005` „refuse a start", der Rest `panic`), dazu `LYR-CAP0001` (E) und `LYR-CAP0002`
(panic) in A.8; `12-diagnostics.md:36–41`: „A code that appears in neither the appendix nor a
retirement row does not exist; adding one is a specification change"; §12.1 Regel 1: Severity ändert
sich nur am Major; „a second implementation must report the same code for the same construct"
(`12-diagnostics.md:4`). `src/Lyric.Vm/VmDiagnostics.cs` deckt sich mit dem Anhang (17 + 2).
Die Kritik schrieb „das Dossier fragt nirgends, welche Paniken eine zweite Implementierung liefern
MUSS" — die Hälfte davon ist beantwortet: der Katalog ist geschlossen und ein neuer Code ist eine
Spec-Änderung (Minor, nicht Bruch — §12.1 bindet nur Severity-Wechsel an das Major).

Was **offen** ist: der Anhang unterscheidet nicht zwischen **muss** und **darf**. Beispiele, gelesen:
`VM0004` — „the limit is quality of implementation … the code and the panic are the contract" (Z. 278);
`CAP0002` — „an implementation without the embedding API never emits it" (Z. 266); `VM0009` —
„reachable only for a module assembled without the loader's checks" (Z. 283); `VM0016` — nur mit
`extern "dotnet"` (Z. 290). Für Lyricpp/Erato 2 ist das die Frage: welche der 17 muss ein Runtime
**erreichen können**, damit ein Programm, das auf Code X hin panickt, dort **denselben** Code liefert?

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | bleibt: die Prosa je Zeile sagt implizit, was optional ist | Lyric 4 | eine zweite Implementierung liest 17 Prosazeilen und rät |
| B | eine **Reichweiten-Spalte** in A.9: `must` (Sprachpanik: VM0002, 0003, 0004, 0006, 0007, 0010, 0011, 0012, 0013–0015, 0017), `host` (CAP0001/0002, VM0016), `hand-built only` (VM0008, 0009) | Java JVMS (welche Exceptions eine JVM werfen **muss**) | ein Spec-PR ohne Regeländerung; die Suite pinnt die `must`-Zeilen |
| C | §9.4 zählt statt fünf alle Sprachpaniken auf und verweist für den Rest auf A.9 | — | nur Kosmetik, wenn B nicht kommt |

**Empfehlung: B, mit C zusammen.** Die Frage „ist ein neuer Code ein Bruch?" ist beantwortet: nein,
eine Spec-Änderung unterhalb des Majors (Konformanz pinnt Codes, nicht deren Abwesenheit).
**Bricht**: nein. **Vertrauen**: **gelesen** (Anhang A, §12.1–12.3, `VmDiagnostics.cs`). **Hängt an**:
Spec, Suite, Laufzeit-Gebiet (Lyricpp).

---

### F39 — Was passiert, wenn ein Modul-Initialisierer scheitert? *(neu)*

**Heute**: er darf nicht werfen, aber niemand sagt es so. `r16`: ein Modul-`let`, das eine werfende
Funktion ruft, ist `SEM0034` mit dem Rat „declare 'throws' on the enclosing function or wrap it in
try/catch" — auf Modulebene gibt es beides nicht. `n05`: ein Wertblock mit `try/catch` als
Initialisierer ist `PAR0002`/`PAR0016`/`PAR0027`/`PAR0025` (die Form existiert dort nicht).
`spec/04-modules.md:36–56` regelt Ordnung und `VM0017` (Lesen vor Initialisierung) und sagt zum Werfen
**nichts**. Ob die Regel „darf nicht werfen" eine Regel oder eine Lücke ist, ist unentschieden.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Regel**: ein Initialisierer darf nicht werfen; eigene Diagnose mit richtigem Rat („use the silent form or a helper with try/catch") statt `SEM0034`s unerfüllbarem | — | ein Diagnosetext (Teil von F26-B) und ein Satz in §4.3 |
| B | **Fangmöglichkeit**: mit F5-A steht `try e catch (…) expr` in Ausdrucksposition und damit im Initialisierer; die Regel A bleibt, wird aber ausschreibbar | Kotlin (`val x = try { … } catch …` auf Top-Level) | kostet nichts über F5-A hinaus |
| C | ein werfender Initialisierer ist eine Panik beim Laden | Java `ExceptionInInitializerError` | `VM0010`-Territorium; verschiebt einen statischen Fehler in die Laufzeit — gegen §9.2 („an uncaught, undeclared throw is a compile error") |
| D | Initialisierer dürfen `throws` deklarieren, und das Modul-Laden wird werfend | — | dann muss `main`s Aufrufer fangen — F11 in einer Form, die niemand will |

**Empfehlung: A jetzt, B mit F5-A.** **Bricht**: nein. **Vertrauen**: **gemessen** (`r16`, `n05`),
gelesen (§4.3). **Hängt an**: F5, F26, Modul-Gebiet.

---

### F40 — Wer trägt die `throws`-Klausel über die Paketgrenze — und in welcher Form? *(neu)*

**Heute**: **die Quelle, und nur sie.** Es gibt keine kompilierten Pakete: `docs/guide/16-building.md:88–89`
— „Every artifact is compiled on its own, whole, from its entry file. There is no link step"; Z. 316:
`"dependencies": { "geometry": "../geometry" }` sind Quellpfade. Ein „Paket-Header" existiert im
Baum nicht (grep `header` in `src/Lyrpack`, `src/Lyric.Build*`: 0; `STATUS.md` kennt nur den
**Modul**-Header). Fassung 2 ließ das „ungeprüft"; die Kritik verortete den Header-Generator in
`src/Lyric.Cli/Stub/` — **das Verzeichnis gibt es nicht**, und `lyrstub` (`src/Lyrstub/Program.cs:4–8`,
Namespace `Lyric.Cli.Stub`) ist „the executable a packed program IS", kein Signaturwerkzeug.

Was **geplant** ist (Design-Runde 2026-09, Stufe B, noch nicht begonnen): kompilierte Lyric-Bibliotheken
mit „Header in `api/` (bodylose Decls + Helfer mit Body), Bodies in `src/`, Header-Check, Sektion
Interface (4.1) im Modul". Die Bauteile, gelesen: die Grammatik erlaubt `throws` auf bodylosen
Deklarationen (`Grammar.md:213–216`, `( Block | ';' )`); `AstFormatter.cs:321–325` druckt die Klausel
(gemessen `FMT1`/`FMT2`); das `.lyrbc` trägt **keine** Klausel (`design/typed-throws.md:97–108`:
„Lowering 0, Format bleibt 4.0" — nur die Handlers-Sektion, `Bytecode.md:379–393`).

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | der **Header** (Quelltext, bodylose Decls) ist die Wahrheit; der Header-Check vergleicht Header gegen Body-Quelle; das `.lyrbc` wird für Klauseln nie befragt | C-Header; **Java** (Klausel nur in der Quelle, JLS §13.4.21 — *behauptet*) | billig; **aber** dann kann kein Werkzeug ein Paket ohne Quelle prüfen (F27-B), und ein Header, der von der Quelle abweicht, ist nur so falsch, wie der Check streng ist |
| B | die geplante **Interface-Sektion (4.1)** trägt Signaturen **einschließlich** der Klausel; der Header ist daraus ableitbar, nicht umgekehrt | .NET-Metadaten (Signatur im Assembly); Swift `.swiftinterface` | Formatschnitt 4.1 — **der ohnehin geplant ist**; die Klausel ist ein Feld pro Funktion. Damit ist F27-B baubar und Header/Binär können nicht auseinanderlaufen |
| C | beides, mit Prüfung auf Gleichheit | — | zwei Wahrheiten und ein Vergleich |

**Empfehlung: B — die Klausel gehört in die Interface-Sektion, sobald es sie gibt.** Gerade weil
`typed-throws.md` zu Recht sagt, dass die Klausel **keine Laufzeitrepräsentation** braucht, ist sie
sonst nirgends im Artefakt; Java zeigt, was das für Werkzeuge heißt. Bis Stufe B gilt A trivial (es
gibt nur Quelle). **Bricht**: nein (Format 4.1 ist additiv geplant). **Vertrauen**: **gelesen**
(Guide 16, Grammatik, Formatter gemessen, Design-Notiz, Design-Runde). **Hängt an**: F27, F2,
Paket-/Build-Gebiet (Stufe B), Bytecode-Gebiet (4.1).

---

### F41 — Welche Werkzeugarbeit müssen F2-A und F5 im selben Release liefern? *(neu)*

**Heute**, gemessen und gelesen:

- **`lyrfmt`** ist AST-basiert und druckt `throws T` auf Funktionen und Koroutinentypen
  (`AstFormatter.cs:321–325`; `FMT1`/`FMT2`). Eine Datei, die **nicht parst**, wird „reported and left
  untouched" (`docs/guide/18-formatting.md:27–28`, `lyrfmt --help`) — und `--check` beendet mit 1.
  Ein Feature, das der Parser kennt und der Formatter nicht rendert, würde also entweder die neue
  Syntax **zerstören** (wenn der AST-Knoten stumm ausgelassen wird) oder jede Datei mit ihr aus
  `--check` werfen.
- **LSP**: `Throws` kommt in `src/Lyric.Lsp/LspServer.cs` nicht vor; Hover (Z. 196), SignatureHelp
  (Z. 232), InlayHint (Z. 240) existieren und zeigen die Klausel nicht.
- **`lyrfix`**: **existiert nicht** (0 Treffer in `src/`, `STATUS.md`, `PLAN.md`, Guide). Fassung 2
  und die Kritik nannten es als Werkzeug für die `OrThrow`-Umstellung; mit F18-A entfällt der Bedarf.
- **Konformanzsuite** (Modus spec-first, Pin 4.6): sie pinnt Syntax und Diagnosen, keine Werkzeuge.

| | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Formatter im selben Release, verbindlich**: jeder neue AST-Knoten (`throws A, B`, `try? e`, `try e catch (…) expr`) bekommt seine `Doc`-Regel im selben PR; ein Fixture je Form in den Formatter-Tests | `rustfmt`/`gofmt` (Sprache und Formatter im selben Repo, ein Release) | drei `Doc`-Regeln, ~20 Zeilen je Form |
| B | LSP im selben Release | Rust-Analyzer (hinkt bewusst nach) | Hover/Signatur mit Klausel; darf nachziehen, weil ein fehlendes Hover nichts zerstört |
| C | ein `lyrfix` bauen | Rust `cargo fix` | nicht nötig ohne F18-B |
| D | nichts festlegen | — | ein Release, dessen Formatter seine eigene Syntax nicht schreibt |

**Empfehlung: A als Regel in `CONTRIBUTING.md` („ein Syntax-Feature ist fertig, wenn `lyrfmt` es
schreibt"), B im Folgerelease, C nicht.** Der Release-Check dafür ist mechanisch: jeder
Konformanzfall mit `since:`-Gate läuft durch `lyrfmt --check`. **Bricht**: nein. **Vertrauen**:
gemessen (`FMT1`, `FMT2`, `--help`), gelesen. **Hängt an**: F2, F5, F30-C, Werkzeug-Gebiet, Spec-Suite.

---

## 4. Was wir übernehmen sollten

Nach Vorbild sortiert.

| Von | Was | Wohin |
|---|---|---|
| **Swift 6** | `throws E` als Phantom-Typparameter, `throws never`; Inferenz für Lambdas nur nach innen | F3, F4 |
| **Swift 6 (Warnung)** | `rethrows` bleibt in Swift, weil `throws(E)` die Vereinigung nicht schreiben kann — Lyric braucht F2 | F3, F2 |
| **Swift, Zig** | **ein `defer`-Körper darf nicht werfen** — Übersetzungszeitfehler; eine Regel für drei Pfade und einen ICE | **F7, F20** |
| **Swift 5 (SE-0230)** | `try?` kollabiert `T??` auf `T?` — Präzedenz für den Kollaps, den `design/try-expression.md:113` übernahm; das Argument gegen ihn auf `?T`-werfenden Funktionen ist Lyrics eigenes (§9.0) | F31 |
| **Java 7** | `throws A, B`; **precise rethrow** (`throw e` aus `catch (e)` behält die Menge des Körpers); „exception already caught" als Fehler für verdeckte Klauseln | F2, **F34, F35** |
| **Java (Warnung)** | die Klausel ist nach JLS §13.4.21 **kein** ABI-Vertrag — deshalb kann kein Werkzeug sie aus einem Jar prüfen. Lyrics Interface-Sektion sollte sie tragen | **F27, F40** |
| **Java (positiv)** | funktionale Schnittstellen dürfen `throws` tragen: F3 + F17 funktionieren zusammen | F3, F17 |
| **C++98/03 (negativ)** | die Liste fiel wegen Laufzeitprüfung und fehlender Template-Komposition; der zweite Grund ist F17 | F2, F17 |
| **C# (Iterator-`Dispose`)** | explizites Schließen läuft **nur `finally`**, nie `catch`, nie beim GC — das Modell für `close` auf einer Koroutine | **F22-B1** |
| **C# (`throw;` vs `throw e;`)** | die Spur-Regel beim Rethrow muss ausgesprochen werden, sobald es eine Spur gibt; Lyric braucht dafür keine zweite Form | F34, F10 |
| **C# (Filter) — herabgestuft** | Exception-Filter sind in .NET billig wegen zweier Durchgänge; Lyric wickelt in einem ab | — |
| **Rust** | `!` nur in Rückgabeposition (stabil) — das Modell für den bereits gefällten `never`-Entscheid | F28 |
| **Rust** | `JoinHandle::join() -> Result` als Modell für ein Task-Handle mit Fehlerwert | **F36** |
| **Rust** | `#[should_panic(expected)]` | F29 |
| **Rust (negativ)** | kein `?` auf `Result` | F16 |
| **Rust / .NET (Werkzeug)** | `cargo-semver-checks` / `ApiCompat` — Klausel-Erweiterung ist ein Bruch, ein Werkzeug sagt es | F27 |
| **Go (Warnung, nicht Vorbild)** | Go läuft `defer` beim Panic und verkettet Doppel-Paniken — beides für eine Sprache **mit** `recover`; Lyric hat keins, und der Budgetstopp darf nicht aussitzbar sein | **F33**, F20 |
| **Python** | PEP 342 (Generator-Finalizer) ist das Vorbild für GC-Abwicklung — und §10 schließt es aus; `__context__` ist A-ohne-Liste für F20 | F22, F20 |
| **Kotlin** | strukturierte Nebenläufigkeit (Scope-Propagation) als Option auf dem Task-Handle; die Warnung zu geprüften Ausnahmen bei Lambdas; zwei Cleanup-Mechanismen als Rule-2-Warnung | F36, F1, F3, F8 |
| **JVMS** | ein Katalog, der sagt, welche Laufzeitfehler eine Implementierung **muss** — die Reichweiten-Spalte für A.9 | **F38** |
| **rustfmt / gofmt** | ein Syntax-Feature ist fertig, wenn der Formatter es schreibt | **F41** |
| **Sutter / Midori** | Bug/Fehler-Zweiteilung; Zahl der Mechanismen als Qualitätsmerkmal | F14, F33 |

**Die fünf, die ich zuerst bauen würde:**

1. **F19 + F4** (Instanzdeckung, Substitution — dieselbe Methode). Der einzige Posten, der aus gültigem
   Quelltext eine ungefangene Ausnahme zur Laufzeit produziert (`r06`).
2. **Die fünf ICEs** (Anhang) — mit F28 als bereits gefälltem Entscheid und F7-D, die den `defer`-ICE
   mit erledigt.
3. **F7-D/F20-D + F35** als **ein** Spec-PR mit F32: die `defer`-Regel, die Verdeckungsregel, die
   Grammatik-Angleichung, die veralteten Stellen. Alles Normierung, kein Feature.
4. **F2-A + F34** (`throws A, B` und precise rethrow) — nach 1 und 3, mit F41-A (Formatter) im selben
   Release.
5. **F10-A1 + F30-A + F11-B** (Report mit `message()`, first-chance-Halt, `main` darf werfen, Exit 101
   bleibt).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 („ein Mechanismus pro Konzept")**

| Frage | Art des Konflikts | Wie ich ihn auflöse |
|---|---|---|
| **F7-D/F20-D** | keiner — im Gegenteil: D nimmt `defer` die zweite Rolle (Fehlerquelle) und lässt ihm die eine (Cleanup) | Empfehlung |
| **F8** `errdefer` | zweiter Cleanup-Mechanismus | D (nichts): mit F7-D ist ein nicht-werfender `errdefer` reine Bequemlichkeit; `var bool` kostet nichts (`r25`) |
| **F14-B** `recover` | zweiter Fangmechanismus | abgelehnt; Bedarf in den Läufer (F29-C) |
| **F16-C** `?` auf `Result` | zweite Propagationsform | abgelehnt (`lyric-v5-features.md:152`) |
| **F2-B** Fehlermengen | zweites typartiges Konstrukt | deshalb F2-A |
| **F25-B** Summentyp | derselbe Einwand | deshalb F25-D |
| **F34-C** `throw;` bar | zweite Wurfform | deshalb F34-B (Sema-Regel, keine Syntax) |
| **F22-B2** `close` wirft in den Körper | ein impliziter Throwable-Typ, den jeder Körper werfen kann — ändert die Throwability aller Pulls | deshalb F22-B1 (nur `finally`-Regionen) |
| **F5-C** `try e` → `Result` | Compiler an stdlib-Typ | vertretbar (`Throwable`, `Display` sind Anker), in §11 zu benennen |
| **F33-B** Panik wickelt ab | zwei Panik-Arten (aussitzbar / nicht) | abgelehnt; D (VM-Freigabe) löst das Sandbox-Leck ohne Gast-Code |
| **F36-B** Task-Handle | keiner — Bibliothek | Empfehlung |

**Mit anderen Gebieten**

- **Generics**: F3/F4 (Phantom-`E`), F19 (was `GenericInstance` in der Sema trägt).
- **Pattern Matching**: F2-D ist deren Feature **#13**; ohne es bleibt `catch (e)` eine Einbahnstraße.
- **Interfaces**: F9-A (Default auf eingebautem `Throwable`), F17, **F35** (Verdeckung ist eine
  Konformanzfrage).
- **Typen**: F28 ist entschieden (Typ-Gebiet baut die Diagnosen); F25 fragt nach Union-Typen.
- **Optionals**: F31.
- **Koroutinen/Tasks**: F22-B1 (`close`), **F36** (Task-Handle), **F33** (Panik).
- **FFI/ABI/Einbettung**: F15, F23, **F33-D** (Freigabe beim Abbruch), Callback-Runde.
- **Laufzeit/VM**: F10-C, **F37** (JIT-Handlerregionen — die Optimierer-/VM-Runde), F22-B1
  (Durchgang ohne `catch`). Format: nur F12-B (ungemessen) und **F40** (Interface-Sektion 4.1,
  geplant).
- **Werkzeuge**: F30 (DAP, LSP), **F41** (Formatter im selben Release), F27-B (`lyrpack`-Vergleich,
  braucht F40); `lyrfix` existiert nicht.
- **Diagnostik**: F26, F32, **F35** (neuer Code), **F39** (Diagnosetext).
- **Pakete/Build**: F27, **F40** (Stufe B).
- **Spec**: F32 (Grammatik vs. Anhang, veraltete Stellen), **F38** (Reichweiten-Spalte A.9).

**Nicht-Konflikte, die wie welche aussehen**

- **`kind 1 = finally` im Bytecode** ist das Lowering-Mittel für `defer`, kein heimliches `finally`.
- **F2-A ist keine Vererbung**: eine Liste ist eine Menge. `Bytecode.md:891` („no inheritance … equality")
  ist veraltet; die Sache stimmt (keine Klassenvererbung; die VM-Konformanzprüfung ist eine
  Implementierungsfrage, kein Baum).
- **F7-D gegen §9.0**: „ein `defer` darf nicht werfen" verbietet keine werfende Bibliotheksfunktion —
  es verlangt im `defer` die stille Zwillingsform, die §9.0 ohnehin garantiert. Der Konflikt entstünde
  nur mit F18-B (Zwillinge weg), und F18-B ist nach F24 zurückgestellt.

---

## Anhang: Was vorher repariert gehört, unabhängig von v5

1. **ICE: `defer { throw … }` im `try`** — `r17`, `CLI0020: lowering: block bb4 is already sealed`.
   Mit F7-D wird aus dem ICE ein `SEM`-Fehler; bis dahin ein Lowering-Bug (`PLAN.md` §B).
2. **ICE: geworfenes `enum :: [Throwable]`** — `r08`. Schließt mit F12-A.
3. **ICE: `never` in Parameterposition** — `r05`.
4. **ICE: `never` als Elementtyp** — `r24c`.
5. **ICE: `never` als Typargument** — `r24d`; Kontrolle `r24e`. **3–5 sind die ausgelassene Hälfte
   von `design/throw-expression.md:34–37`**, kein Designentscheid.
6. **Veraltete Stellen** (§1.4): `spec/09-errors.md:61–63` (*Implementation limit*), Anhang A
   `IR0001`-Zeile (Interface-Catch **und** `&&=` gebaut — `r10`, `n09`), `docs/Bytecode.md:891–892`,
   `Interpreter.cs:1175`, `docs/Grammar.md` (`never`; `TryStmt` gegen `PAR0023`),
   `stdlib/std/test.lyr:89–90` und `BuiltinTypes.cs:57` („never not nameable").
7. **`SEM0034` sagt „wrap it in try/catch", während ein `try/catch` dasteht** (`r31`, `r32`, `n02b`)
   und verlangt auf Modulebene Unmögliches (`r16`). Teil von F26-B/F39-A.
8. **`SEM0051` feuert auf ein Komma in der Klausel** (`n08`). Teil von F26-B.
9. **Doppeldiagnose `PAR0023` + `SEM0036`** (`r22`) — mit F32-A eine.

---

## Nach der Kritik geändert

**Falsche Aussagen korrigiert (nachgelesen auf `8f17c02` / `6f6f029f`):**

- **F7, F20, (e)**: `spec/07-statements.md:135–141` erklärt den werfenden `defer` **ausdrücklich** für
  unspezifiziert, nennt beide Hälften, `SEM0110` und §12.5. „§7.5 sagt dazu nichts" war falsch; (e) ist
  nicht „vorher nirgends notiert", nur vorher nicht gemessen.
- **F21**: Block-Scoping **und** „once per iteration" stehen in `07-statements.md:129–132`. Die
  Handlungsempfehlung („den Satz hinschreiben") war leer; die Frage ist im Kern geschlossen.
- **F22, (k)**: `spec/10-coroutines.md:89–93` und `07-statements.md:143–146` normieren, dass eine
  fallengelassene Koroutine nichts läuft und der GC kein Ausgang ist. A steht schon; C ist normativ
  ausgeschlossen, nicht nur per `STATUS.md`. B in B1 (nur `finally`, C#-Iterator-`Dispose`) und B2
  (Ausnahme in den Körper, Python) aufgeteilt; B1 empfohlen.
- **F32**: `PAR0023` (Anhang A:62), `SEM0035` (:137), `SEM0036` (:138) sind normativ. Übrig: die
  Grammatik widerspricht dem Anhang, Wurf-aus-`catch` und Verdeckung (F35) sind unnormiert.
  „Sechs Divergenzen" neu gezählt: siehe §1.4 und Anhang 6.
- **F3**: `SEM0084` steht in `TypeChecker.cs:6161`, nicht 5964.
- **Typ-Pattern auf Interface-Werten ist v5-Feature #13**, nicht #16 (dreimal korrigiert).
- **F28**: `design/throw-expression.md:34–37` hat A bereits entschieden; F28 ist jetzt „die ausgelassene
  Hälfte bauen" (Bugfix-Posten), mit dem einzigen offenen Rest `?never`/`never[]` in Rückgabeposition.
- **§1.1 Formatkosten / F9-B / F20-A**: „kostet ein Feld nur, wer es nutzt" gestrichen. Die VM braucht
  einen Schreibort in jedem Throwable — verstecktes Feld (Format) oder Seitentabelle plus
  compiler-gebundene Kante (§11 Punkt 1). Das war behauptet und ist falsch.
- **F12-B**: `Bytecode.md:392` (Slot) ist kein Beleg; das Hindernis sind `Interpreter.cs:1216–1222`
  (Fat Pointer aus einer Referenz) und `throw`s „type index + 1" (`Bytecode.md:887–889`); ob Format:
  ungemessen.
- **F11**: „102 ist der einzige Weg" gestrichen. `Bytecode.md:1093–1094` nimmt Kollisionen in Kauf und
  verweist auf stderr; eine entkommene Ausnahme exit-codet heute 101 (`r06`). Empfehlung: B mit
  **101**, kein neuer Code.
- **F24/F18**: unter `LYRIC_JIT=1` neu gemessen (3 Läufe, beide Profile, Maschine unter Last): die
  stille Form ist ~3× billiger, die `try`-Probe wird nicht schneller; Grund `JitCompiler.cs:712–726`.
  **F18-Empfehlung von B auf A gedreht**; F37 neu.
- **F40 / Kritik**: die Kritik verortete den Header-Generator in `src/Lyric.Cli/Stub/` — das Verzeichnis
  gibt es nicht; `lyrstub` ist der Launcher gepackter Programme. Es gibt heute **keine** kompilierten
  Pakete und keinen Header (Guide 16:88–89, 316); der Header ist Stufe B. **Die Kritik hatte hier
  nur halb recht** — die Frage ist trotzdem gestellt (F40), weil die Interface-Sektion 4.1 geplant ist.
- **F38 / Kritik**: „das Dossier fragt nirgends, welche Paniken eine zweite Implementierung liefern
  MUSS" — halb richtig. Anhang A.9 führt alle 17 + 2 normativ, §12.2 macht den Katalog geschlossen,
  ein neuer Code ist eine Spec-Änderung unterhalb des Majors. **Was die Kritik nicht sah**: §9.4s
  „fünf" sind Prosa, nicht der Katalog. Offen ist nur die muss/darf-Spalte; als F38 gestellt.
- **§1.4 / `&&=`**: die Kritik-Probe `n09` zeigt, dass `&&=` **läuft** — die Aussage von Fassung 2
  („beides ist gebaut") **hält**, die Anhang-Zeile ist veraltet.

**Empfehlungen geändert:**

- **F7/F20: A → D** (werfender `defer` ist ein Übersetzungszeitfehler). Swift und Zig als Vorbilder
  nachgetragen; D schließt F7, F20 und den ICE (f) mit einer Regel, ohne F9-B und dessen Kosten.
  Folge: F8 → D, F9-B entfällt, F21 geschlossen.
- **F18: B → A** (Zwillinge bleiben), wegen der JIT-Messung.
- **F11: 102 → 101** (B).
- **F22: B → B1** (nur `finally`-Regionen).
- **F31**: Empfehlung B bleibt, Begründung umgedreht (Swift kollabiert; das Argument ist §9.0).
- **F25-D**: jetzt an F35 gekoppelt.
- **F28**: von Designfrage zu Bugfix-Posten.

**Sprachvergleiche korrigiert:**

- Swift 5 (SE-0230) kollabiert `try?`; Swifts `defer` ist scope-gebunden und darf nicht werfen.
- Zig: `return`/`try` in `defer` ist ein Compilerfehler.
- Rust: `!` stabil nur in Rückgabeposition — Vorbild für F28-A, nicht B.
- C#: kein Iterator-Finalizer (`finally` nur bei `Dispose`); keine statische Deckungsrechnung (aus
  F19-A gestrichen); `throw;` vs `throw e;`.
- Python PEP 342 ist das Vorbild für GC-Abwicklung, nicht C#.
- Go: Doppel-Paniken werden verkettet und beide gedruckt — kein Vorbild für „letzte gewinnt"; Java
  `finally`/C# sind es.
- JLS §13.4.21: `throws` ist kein ABI-Vertrag — Java ist Gegenbeispiel für F27-A, Lehre für F40.
- JNI übersetzt nichts automatisch; Python-C-API und .NET-COM-Interop sind die Vorbilder für F15-B.
- P0709: `try` als optionaler Marker — zwei Sprachen erzwingen ihn (Swift, Zig), nicht drei.
- Alle Sprachaussagen außer Lyric bleiben *behauptet*, so gekennzeichnet.

**Neun Fragen ergänzt, alle mit Messung oder Lesebeleg:** F33 (Panik und `defer`: `n01`/`n01b`/`n01c`),
F34 (precise rethrow: `n02a`/`n02b`/`n02c`), F35 (verdeckte `catch`-Klausel: `n03`/`n07`/`n07b`), F36
(Task-Scheitern: `n10`/`n10b`), F37 (JIT und Wurfpfad: `r23*` beide Profile), F38 (Panik-Katalog:
Anhang A.9, §12.1–12.3), F39 (Modul-Initialisierer: `r16`/`n05`), F40 (Klausel über die Paketgrenze:
Guide 16, Formatter `FMT1`/`FMT2`, Design-Runde), F41 (Werkzeuge im selben Release: `lyrfmt --help`,
`AstFormatter.cs:321`, `LspServer.cs`).

**Wo die Kritik nicht recht hatte, und die Aussage steht:** `&&=` ist gebaut (`n09`); der Katalog der
Paniken ist normativ (Anhang A.9), nur die Reichweite fehlt; einen Header-Generator gibt es nicht.

**Alle Proben**: `…/scratchpad/v5-design/probes/fehler-rev3/` (n01–n10b, mit Kontrollen n01b, n02a,
n02c, n07b, n10b), `…/fehler-rev2/` (r01–r32b) und `…/fehler/` (p01–p42), soweit zitiert.
