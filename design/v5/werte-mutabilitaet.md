# Lyric 5 — Gebiet: Wertsemantik und Mutabilität

Belege: `gemessen` = eigenes Programm kompiliert und gelaufen, `gelesen` = Pfad:Zeile,
`behauptet` = ohne Beleg, ausdrücklich so markiert.

Proben:
- erste Runde `…/scratchpad/v5-design/probes/werte/` (`pNN`),
- zweite Runde `…/probes/werte-rev/` (`rNN`),
- Proben des Kritikers `…/probes/werte-review/` (`qNN`, `fsharp_check.fsx`) — **alle in dieser
  Fassung selbst nachgelaufen**, Kopien und Protokoll in `…/probes/werte-rev3/`,
- Proben dieser Fassung `…/probes/werte-rev3/` (`nNN`, `run.sh`).

Spec-Zitate aus dem Clone `C:/Users/Olivier/CLionProjects/lyric-spec`. Compiler:
`src/Lyrc/bin/Debug/net10.0/lyrc.dll`, Repo-HEAD `6f6f029f` („release/v4.6.0-cut"); §3.4a, §12.5
und `LYR-SEM0106–0110` sind in diesen Binaries enthalten.

> **Was diese Fassung gegenüber der zweiten ändert, in einem Satz:** Die zweite Fassung hat den
> einzigen Ort, dem sie eine Garantie zuschrieb — `this` in einer Struct-Methode — nie über einen
> Aufruf und nie zweistufig geprüft. Gemessen (q02a, q16, q17) gilt `mut` **eine Ebene tief und
> nicht für Aufrufe**, auf Structs wie auf Klassen. Damit ist der Kern von `mut fn` offen, nicht
> nur sein Rand; die Uhren SEM0108 und SEM0109 laufen an dieser Lücke vorbei, und die drei
> Empfehlungen W5, W6 und W19 waren ohne die Frage nicht implementierbar. Sie stehen jetzt auf
> **W34** (Transitivität) und **W35** (was `this` ist). Dazu kommen elf weitere Fragen (W36–W46),
> sieben korrigierte Sprachvergleiche und eine Zählung des 5.0-Bruchs, die vorher geraten war.

---

## 1. Ist-Stand

### 1.1 Was die Doku verspricht

| Quelle | Aussage | Status |
|---|---|---|
| `docs/guide/05-structs-and-classes.md:5-6` | „A **struct** is a value. Assigning it copies. A **class** is a reference. Assigning it shares." | gelesen; im Normalfall gemessen wahr (p33) |
| `docs/guide/02-values-and-types.md:16` | „`let` binds the *name*, not the contents." | gelesen |
| `docs/guide/05-structs-and-classes.md:61` | „`mut` on a method of a struct means the receiver is **written back** to the caller's value." | gelesen — **und gemessen falsch als Beschreibung** (q18, n03): `this` ist eine Referenz auf den Ort des Aufrufers, kein Copy-in/Copy-out. Siehe Befund 13 und W35 |
| `docs/guide/05-structs-and-classes.md:63-66` | Auf einer Klasse „it is not, yet — such a method compiles and warns (`LYR-SEM0108`), and 5.0 enforces it there too." | gelesen; die Uhr schweigt auf dem Aufrufpfad (q17) |
| `docs/guide/05-structs-and-classes.md:70` | „`static let` attaches a **constant** to a type." | gelesen; gemessen keine Konstante (r03) |
| `lyric-spec/spec/07-statements.md:5-9` | „Immutability is per binding: a `let` of a class value still permits `mut` method calls through it … ANY assignment to a `let` — first or otherwise — is `LYR-SEM0019`." | gelesen |
| `lyric-spec/spec/07-statements.md:16-19` | „A second binding of one name in ONE scope is unspecified, and carries a migration warning (`LYR-SEM0107`)." | gelesen |
| `lyric-spec/spec/03-types.md:96-119` (§3.4a) | Auf einer Klasse ist `mut` „unspecified, and the reference implementation enforces nothing"; `let p = P { … }; p.x = 5;` „compiles"; „A parameter is an immutable binding by the same rule". „**5.0 settles what a struct is**", Kandidat `mut struct`. | gelesen |
| `lyric-spec/spec/03-types.md:88-95` | „An initializer settles every field." Ausgelassenes Feld ohne Default ist `LYR-SEM0106`. | gelesen, r01 gemessen |
| `lyric-spec/spec/03-types.md:72-73` | Arrays: „the value is built with `[x] * n`" — die Spec-Bauform für ein Array fester Länge. | gelesen; **gemessen erzeugt sie n Aliasse eines Struct-Werts** (q03, n04). Befund 14, W36 |
| `lyric-spec/spec/12-diagnostics.md:74-105` (§12.5) | Vier offene Fragen, alle „settled together with **5.0**"; ein Migrationscode „retires WITH its rule". | gelesen |
| `lyric-spec/spec/13-bytecode.md:195-196` | „A struct must not contain itself as a field, directly or indirectly. Recursion through a class, an array or an interface is permitted; those are references." | gelesen — **die Regel**, nicht nur die Begründung; `?T` steht nicht in der Erlaubnisliste. W23 |
| `lyric-spec/spec/13-bytecode.md:879-886` | `structcopy` rekursiv über Structs, flach über alles andere; „terminates without cycle detection because **a struct cannot contain itself**"; emittiert bei „initialization, assignment, argument, return, field and element assignment". | gelesen; die Array-Repetition fehlt in der Liste (W36) |
| `lyric-spec/spec/03-types.md:77` | „closures capture by reference" | gelesen, p9 gemessen |
| `lyric-spec/spec/03-types.md:84-86` | Generics sind monomorphisiert. | gelesen |
| `lyric-spec/spec/05-interfaces.md:141-142` | „`extend T { … }` adds methods to any visible type … such method-only blocks are unrestricted." | gelesen; ein fremdes Modul darf einem Struct ein `mut fn` geben (n09 gemessen). W38 |

### 1.2 Was der Compiler tut

Die eine Regel — `src/Lyric.Frontend/Sema/SemaRules.cs:356-395` (`IsMutableLvalue`/`IsFieldMutable`),
gelesen:

```
IdentifierExpr id -> RefOf(id) is LocalSymbol { IsMutable: true }
MemberExpr m      -> IsFieldMutable(m)
IndexExpr ix      -> IsIndexableTarget(TypeOf(ix.Target))       // :362 — der TYP entscheidet
```

und in `IsFieldMutable` (`:380-389`):

```
kind == Class   -> true                                   // Klassenfelder sind IMMER schreibbar
kind == Struct  -> m.Target is not ThisExpr || _thisMut   // :389
```

Es läuft **keine Rekursion zur Wurzel der Zugriffskette**, und die Prüfung sieht **nur
Zuweisungen** — ein Methodenaufruf wird nirgends gegen `_thisMut` gehalten. *(Korrigiert. Die
zweite Fassung schrieb: „Der einzige Ort, an dem `mut` etwas verspricht, ist `this` in einer
Struct-Methode." Das Versprechen gilt genau eine Ebene tief und nur für die Zuweisung selbst:
`this.v = 9` ist `SEM0019` (q02b), `this.inner.x = 9` ist es nicht (q16), und `this.bump()` mit
`bump` als `mut fn` ist es auch nicht (q02a). Befund 12.)*

Daneben die 4.6-Prüfung — `SemaRules.cs:321-346` (`NoteWhereFiveSettlesIt`), gelesen, „SEPARATE
FROM `IsMutableLvalue` ON PURPOSE" (`:315-318`):

```
if (target is not MemberExpr m) return;                            // :323 — ein AUFRUF kommt nie hier an
case Class  when m.Target is ThisExpr && !_thisMut                 -> LYR-SEM0108
case Struct when m.Target is IdentifierExpr id
              && RefOf(id) is LocalSymbol { IsMutable: false } or ParameterSymbol
                                                                   -> LYR-SEM0109
```

`target is MemberExpr` (`:323`) und `m.Target is IdentifierExpr` (`:337`) sind zusammen der ganze
Geltungsbereich beider Uhren: **eine Ebene, und nur Zuweisungen.** Daraus folgen Befund 3 (SEM0109
einstufig) und Befund 12 (beide Uhren blind für den Aufrufpfad).

### 1.3 Messungen

Alle Zeilen gemessen, mit Kontrolllauf wo überraschend. `pNN`/`rNN` = frühere Runden, unter den
4.6-Binaries gültig; `qNN` = Proben des Kritikers, selbst nachgelaufen; `nNN` = diese Fassung.

| # | Probe | Ergebnis |
|---|---|---|
| p33 | `var a = S{v=1}; var b = a; b.v = 9;` | **KONTROLLE:** `a.v = 1` — Wertsemantik im Normalfall |
| p6 | `struct W2 { n: S }`, `var b = a; b.n.v = 9;` | **KONTROLLE:** `a.n.v = 1` — verschachtelt kopiert korrekt |
| p1 / r08 | `let s = St{v=1}; s.v = 2;` | kompiliert, druckt 2, `LYR-SEM0109` |
| p16 / r15 | globales `let G = S{v=1}; G.v = 9;` | 9, **ohne Warnung** |
| p14 / r13 | `fn f(s: S) { s.v = 9; }` | erlaubt (Aufrufer sieht 1), `SEM0109` |
| p14 | `fn takesOpt(o: ?S) { o!.v = 9; }` | Aufrufer sieht **9** — `?S` ist ein Alias |
| p2 / p6 / p43 / p52 | `?S → ?S` in Struct-Feld, Local, Klassenfeld, Array | überall geteilt |
| p2 | `var y: ?S = x; y!.v = 7;` | `x.v = 1` — die Umhüllung `S → ?S` kopiert |
| **n10** | `var o: ?S = …; if (let c = o) { c.v = 9; }` | `o!.v = 1` — **die Narrowing-Bindung kopiert**, `SEM0109` feuert; `o!.v = 9` daneben aliast (r11). Zwei Formen, zwei Antworten |
| p7 / p35 | `var e = xs[0]; e.v = 9;` / `var ys = xs; ys[0].v = 9;` | Lesen aus dem Array kopiert / das Array ist eine Referenz |
| p8 | `var t = c.s; t.v = 9;` | Lesen aus dem Klassenfeld kopiert |
| p15 | `struct A { xs: int[] }`, `var b = a; b.xs[0] = 9;` | `a.xs[0] = 9` — flache Kopie, wie spezifiziert |
| p9 | Closure schreibt `s.v` | 9 — per Referenz gefangen |
| p11 | `let v = V{x=1}; v.shift(9);` (`mut fn`) | 10 — `mut fn` durch `let`, stumm |
| p11 / p36 | `xs[0].shift(9)`, `h.s.shift(9)`, `o.i.bump()` | landen alle |
| p17 / r12 / r16 | `make().shift(9)` / `make().x = 9` | kompilieren, landen nirgends, `make()` **läuft** |
| p10 / r14 / p10b | Klassenmethode ohne `mut` schreibt `this.v` / dasselbe als Struct | `SEM0108` / **KONTROLLE:** `SEM0019` |
| **q02b** | **KONTROLLE:** `struct S { fn bad() { this.v = 9; } }` | `LYR-SEM0019` — die direkte Ebene ist geprüft |
| **q02a** | `struct S { mut fn bump(); fn viaThis() { this.bump(); } fn viaInner() { this.inner.bump2(); } }`, `var s`/`let t` | **kompiliert ohne Fehler und ohne Warnung**; `s.v = 2`, `s.inner.x = 2`, `t.v = 2`. Dazu `SEM0075` auf `var s` |
| **q16** | `struct S { fn deep() { this.inner.x = 9; } }`, `let s; s.deep();` | **kompiliert ohne Diagnose**, `s.inner.x = 9` |
| **q17** | `class C { mut fn bump(); fn viaThis() { this.bump(); } }`, `let c; c.viaThis();` | **kein `SEM0108`**, Exit 2 (geschrieben) |
| **q18** | `mut fn bump() { this.v = 5; println(G.s.v) }` über ein Klassenfeld; `mut fn half() { this.v = 5; throw … }` | mitten im Aufruf liest der Alias **5**; nach `throw` bleibt `t.v = 5` — **`this` ist eine Referenz**, kein Copy-in/Copy-out |
| **n03** | `mut fn bump() { this.v = 5; G.s.other(); println(this.v) }` (`other` ist `mut fn`, setzt 7) | **7** — Reentranz über den Alias ist im laufenden `mut fn` sichtbar |
| p53 / p50 | Klasse implementiert `mut fn` eines Interfaces ohne `mut`, und umgekehrt | `SEM0042` beidseitig |
| p25 / r26 | `var s; let b: Bump = s; b.bump();` | Boxen **kopiert**; `mut fn` durch `let`-Interface erlaubt |
| p4 / p19 / p19b | `struct Node { next: ?Node }` / `{ n: N }` / `{ xs: N[] }` | läuft / `SEM0056` / läuft |
| p3 / p48 / r14 | `let x = 1; let x = 2;` | druckt 1 (erste Bindung gewinnt), `SEM0107` |
| p12 / p37 / p28 | Felddefault mit Seiteneffekt / Global im Default / Geschwisterfeld | zweimal ausgewertet / 42 / `SEM0002` |
| p13 | `a == b` auf einem Struct | `SEM0059` |
| p23 / p30 | `var g` auf Modulebene / `mut fn` frei | `PAR0027` / `SEM0023` |
| p42 | `fn f(v: V) { var w = v; w.x = 9; }` | 10 — der `var`-Umweg funktioniert |
| r01 / r01b | fehlendes Feld ohne Default / Kontrolle | `SEM0106` / kompiliert |
| r02 | `let o = O{i=I{x=1}}; o.i.x = 9;` | 9, **keine Warnung** |
| r03 | `static let ORIGIN`, `P.ORIGIN.x = 9` in einer Funktion, gelesen in einer anderen | 0 → 9, **keine Warnung** |
| r04 | `match (e) { E.A(inner) => inner.v = 9 }` | `SEM0109`, Nutzlast bleibt 1 |
| r05 | werfender Felddefault | `SEM0034` an der Felddeklaration („enclosing function") |
| r06 | `var b = a; b!.shift(9)` auf `?V` | `a!.x = 10` — Alias getroffen, stumm |
| r07 | `let g` mit `Indexable<int>`, `g[0] = 9` | bleibt stehen, stumm |
| r09 / r10 / r11 | `let c: C; c.s.x = 9` / `let xs: S[]; xs[0].x = 9` / `let o: ?S; o!.x = 9` | 9 / 9 / 9 — alle stumm |
| r17 / r18 / r24 / r25 | `p with {…}` / `S { v }` / `mut struct S` / `class C { let id: int }` | `PAR0016` / `SEM0052`+`SEM0045` / parst nicht / `PAR0011` |
| r19 / r22 / r19b | Auswertungsreihenfolge | explizite Felder in Schreibreihenfolge, danach Defaults in Deklarationsreihenfolge; Default eines gesetzten Feldes läuft nicht |
| r20 | `pass<S>(a)` dann `b.x = 9` | `a.x = 1` — generischer Durchlauf kopiert |
| r21 | Struct in Tupel, Tupel kopiert, destrukturiert | Kopien (Ziel-Seite) |
| **q04** | `var s; let t = (s, 2); s.v = 9; let (a, _) = t;` und `let e = E.A(u); u.v = 9;` | `a.v = 1`, `i.v = 1` — **Tupel- und Nutzlast-Konstruktion kopieren, von der Quellseite beobachtbar** |
| r23 | `extern "dotnet" fn takes(s: S)` | `SEM0099` — Structs kreuzen die Grenze nicht |
| r27 / r27b / r28 / r29 | `SEM0075` bei Feldschreibung / ohne / tief / durch `!` | schweigt / feuert / schweigt / schweigt |
| **n11** | `var a; a.bump();` (`mut fn`) und `var b; b.peek();` (nicht-`mut`) | **kein** `SEM0075` auf `a`, `SEM0075` auf `b` — ein `mut fn`-Aufruf zählt als Mutation, ein nicht-`mut`-Aufruf nicht (Erwartung vorher notiert, beide getroffen) |
| **q03** | `var ys = [S{v=1}] * 3; ys[0].v = 9;` | `ys[1].v = ys[2].v = 9` — **n Aliasse eines Werts**; `var zs = [z] * 2` → `z.v = 1`, `zs[1].v = 9` |
| **q03c** | **KONTROLLE:** `var xs = [s, s]; xs[0].v = 9;` | `xs[1].v = 1`, `s.v = 1` — das Literal kopiert jedes Element |
| **n04** | `[S{v=1}] * 3; ys[0].bump();` / `[1] * 3; ns[0] = 9` | `ys[1].v = 2` — auch der `mut fn` trifft alle / **KONTROLLE:** `ns[1] = 1` |
| **q19** | `List<S>`: `l[0].v = 9;` / `S[]`: `xs[0].v = 9;` | `l[0].v` bleibt **1** (stiller No-Op) / `xs[0].v = 9` |
| **n06** | `l[0].bump();` auf `List<S>`; `l.get(0)` | bleibt 1 / Kopie — `Indexable.get` liefert ein Temporary |
| **q06** | `for (e in xs) { e.v = 9; }` (Array von `S`) | `xs` unverändert; `SEM0109` mit Text „'let' pins the name" |
| **n01** | `for (e in l)` über `List<S>`; `for (e in xs) { e.bump(); }`; `let (a, _) = t; a.v = 9;` | Kopie + `SEM0109` / Kopie, **stumm** / Kopie + `SEM0109` |
| **n10** | `if (let (a, _) = t) { a.v = 9; }` | Kopie + `SEM0109`. `let (d, _) = t else {…};` parst nicht (`PAR0002`) — nicht weiter gemessen |
| **n07** | `struct Oops :: [Throwable]`, `catch (e: Oops) { e.v = 9; }` | `LYR-IR0001` „catching a non-class type (only classes and interfaces are throwable)" — **es gibt keine Struct-`catch`-Bindung**; `SEM0109` feuert trotzdem auf der Zeile |
| **q07** / **q07b** | `let s; s.v += 1; s.v++;` / `var s; s.v++;` | `+=` kompiliert (`SEM0109`); `++` ist **`LYR-IR0001` „increment/decrement target (only parameters and locals)"** — und `SEM0109` feuert auf der nicht-lowerbaren Zeile mit |
| **q10** | `b?.shift(9)` (`mut fn`, `void`) auf `?V` | `LYR-IR0001` „'?.' with a call that returns nothing" |
| **n05** | `let r = b?.shiftRet(9);` (`mut fn`, gibt `int`) | **lowert und trifft den Alias**: `a!.x = 10`, `b!.x = 10`, `r = 10` |
| **q21** | `extend S { mut fn bump() }` im selben Modul, `let s; s.bump();` | `s.v = 2`, stumm |
| **n09** | `shapes.lyr: pub struct S`; `main.lyr: import shapes { S }; extend S { mut fn bump() }` | **kompiliert, `s.v = 2`, stumm** — ein fremdes Modul gibt dem Struct ein `mut fn` |
| **n02** | `struct K :: [Hashable<K>]`; `var k: ?K; m.set(k!, 100); alias!.v = 2;` und via `[K{v=5}] * 2` | `m.get(K{v=1}) = 100`, `m.get(K{v=2}) = null`; ebenso 500/null — **der Schlüssel wird beim Einfügen kopiert** (Argumentkopie) |
| **q22** / **n08** | `var s: S; s.v = 1; s.w = 2;` / **KONTROLLE:** `var s: S; s = S{…}; s.v = 5;` | `LYR-SEM0018` „possibly unassigned" — **feldweise Erstinitialisierung gibt es nicht** / kompiliert, Exit 7 |
| **fsharp_check.fsx** | F# 10 (`dotnet fsi`): `[<Struct>] type S = val mutable x … member this.Bump()`; `let s = S(1); s.Bump()` | **kompiliert ohne Fehler und Warnung, `s.x` bleibt 1**; `let mutable m` → 2. F# kopiert still, wie C# |

### 1.4 Wo Lyric 4 inkonsistent, unvollständig oder still falsch ist

**1. §7.1 ist zum Struct unvollständig — mehr nicht.** `07-statements.md:5-9` handelt von der
Zuweisung *an die Bindung*; „Immutability is per binding" steht zwei Zeilen davor. §3.4a
(`03-types.md:112-117`, gelesen) hält `let p; p.x = 5` seit 4.6 als unspezifiziert fest. *(Der
bewusste Test dagegen: `tests/Lyric.Tests.Sema/MutabilityTests.cs:83-92`, gelesen.)*

**2. `?Struct` ist ein Alias, und das steht in keiner Liste.** §13 zählt auf, was `structcopy`
teilt (class, array, interface) und kopiert (struct) — `?Struct` steht in keiner
(`13-bytecode.md:879-886`). Zwei Codestellen entscheiden still „geteilt":
`src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:3038` (`from is not IrOptionalType` schaltet die
Kopie ab, wenn die Quelle schon `?S` ist) und `src/Lyric.Vm/Interpreter.cs:1162-1163`
(`TypeTag.Struct` rekursiert, `Optional` nicht), beide gelesen. Gemessen: `?S → ?S` teilt (p2, p6,
p14, p43, p52), `S → ?S` kopiert (p2) — **und `if (let c = o)` kopiert ebenfalls** (n10), während
`o!` aliast (r11). Es ist **die einzige der vier §3.4a-Fragen ohne Uhr** *(präzisiert; die zweite
Fassung schrieb an einer Stelle „die einzige Frage dieses Gebiets ohne Uhr" und widersprach damit
ihrer eigenen W33-Liste)*.

**3. Der `LYR-SEM0109`-Zeiger deckt seine Frage nur einstufig ab.** Gemessen:

| Ort der Wurzel | Schreibung | Läuft? | SEM0109? | Probe |
|---|---|---|---|---|
| `let`-Local, einstufig | `o.x = 9` | ja | **ja** | r08 |
| Parameter, einstufig | `s.x = 9` | auf der Kopie | **ja** | r13 |
| Pattern-/Schleifen-/`if let`-Bindung | `inner.v = 9`, `e.v = 9`, `a.v = 9`, `c.v = 9` | **nein** (Kopie) | **ja** | r04, q06, n01, n10 |
| `let`-Local, zweistufig | `o.i.x = 9` | ja | nein | r02 |
| `let`-Local über `!` | `o!.x = 9` | ja | nein | r11 |
| `static let` / Modul-`let` | `P.ORIGIN.x = 9` / `G.v = 9` | ja, global | nein | r03, r15 |
| Array-Element | `xs[0].x = 9` | ja | nein | r10 |
| Index auf einem Struct | `g[0] = 9` | ja | nein | r07 |
| Temporary | `make().x = 9` | nein | nein | r12 |
| **`this` in nicht-`mut` Methode, zweistufig** | `this.inner.x = 9` | **ja** | **nein** | **q16** |
| **`mut fn`-Aufruf auf `this` aus nicht-`mut` Methode** | `this.bump()` | **ja** | **nein** | **q02a** |

Ursache gelesen, `SemaRules.cs:323` und `:337`: `MemberExpr` mit `IdentifierExpr`-Ziel, sonst
nichts. Die Uhr läuft auf demselben Loch wie die Regel. Sie warnt außerdem an Orten, an denen die
Schreibung heute schon wirkungslos ist (r04, q06, n01, n10).

**4. `mut` gilt auf Structs, gilt nicht auf Klassen, ist auf Klassen aber erzwungen, sobald ein
Interface es verlangt** (p10/r14 mit `SEM0108`; p10b `SEM0019`; p53/p50 `SEM0042` beidseitig;
`03-types.md:99-101` gelesen).

**5. Eine Schreibung auf einem Temporary ist ein stiller No-Op** — `make().shift(9)` (p17) und
`make().x = 9` (r12/r16, `make()` läuft). **Und ein Temporary ist häufiger, als die zweite Fassung
dachte:** `l[0]` auf jedem `Indexable`-Klassencontainer ist eines (q19, n06; Befund 15).

**6. Ein `mut fn` durch eine `let`-Bindung ändert den Wert** (p11) — der Fall, den
`MutabilityTests.cs:85-93` (gelesen) als Begründung nennt, die alte Einschränkung zu löschen.

**7. Eine zweite Bindung desselben Namens im selben Scope wird still verworfen** (p3, p48), seit
4.6 mit `SEM0107` (r14).

**8. `struct Node { next: ?Node }` läuft aus Versehen und verletzt eine Formatregel.**
*(Verschärft.)* `FindStructCycle` (`src/Lyric.Frontend/Sema/TypeChecker.cs:5993-6010`, gelesen)
überspringt Felder, deren Typ kein `NamedType` ist; `?Node` fällt durch. Die zweite Fassung zitierte
nur die Begründung `13-bytecode.md:881-882`. **Die Regel steht in `13-bytecode.md:195-196`**
(gelesen): „A struct must not contain itself as a field, directly or indirectly. Recursion through
a class, an array or an interface is permitted; those are references." `?T` steht nicht in der
Erlaubnisliste — der Compiler verletzt eine Formatregel, nicht nur einen Begründungssatz. W23 muss
beide Stellen anfassen.

**9. Ein globales `let` schützt nichts, ein `static let` erst recht nicht** (p16/r15, r03). Der
Guide nennt `static let` eine Konstante (`05-structs-and-classes.md:70`) — sie ist keine.

**10. Die Feldkurzform `S { v }` ist belegt** — als Aufruf mit Trailing-Lambda gelesen (r18);
Muster punnen (`docs/Grammar.md:555`), Initializer nicht (`:495`).

**11. Ein `IndexExpr` ist schreibbar, sobald der *Typ* indexierbar ist** (`SemaRules.cs:362`;
r07). Was daraus bei einem **Klassen**container mit Struct-Elementen folgt, ist Befund 15.

**12. `mut` ist nicht transitiv — auf Structs wie auf Klassen.** *(Neu; von der Kritik gefunden,
selbst nachgemessen.)* Eine nicht-`mut` Struct-Methode darf `this.bump()` rufen (`bump` ein
`mut fn`), darf `this.inner.bump2()` rufen und darf `this.inner.x = 9` schreiben — alles ohne
Fehler und ohne Warnung, und alles landet, auch durch `let` (q02a: `t.v = 2`; q16: `s.inner.x = 9`).
Auf einer Klasse dasselbe ohne `SEM0108` (q17). Kontrolle q02b: die direkte Zuweisung `this.v = 9`
ist `SEM0019`. Ursache gelesen: `SemaRules.cs:389` prüft nur `m.Target is ThisExpr`, und
`:323` lässt Aufrufe gar nicht erst in die Uhr. **Damit bedeutet `mut fn` heute: „schreibt ein
Feld von `this` direkt" — nicht „ändert `this`".** Ein Programm kann SEM0108 und SEM0109 vollständig
befolgen und unter W6 A bzw. W34 A trotzdem brechen. **Und `SEM0075` schiebt genau hier in die
Falle:** auf `var s`, dessen einzige Mutationen über `s.viaThis()` laufen, empfiehlt der Hinweis
`let` (q02a); nach dem Wechsel warnt nichts, die Mutation bleibt. Gemessen n11 ist der Hinweis
dabei mit der Sema konsistent — ein direkter `mut fn`-Aufruf zählt als Mutation, ein nicht-`mut`
Aufruf nicht; falsch ist nicht der Hinweis, sondern die Sema-Annahme, ein nicht-`mut` Aufruf
mutiere nicht. Fällt die Transitivitätslücke (W34), stimmt der Hinweis von selbst.

**13. `this` in einem `mut fn` ist eine Referenz auf den Ort des Aufrufers.** *(Neu.)* Mitten im
Aufruf liest ein Alias über ein Klassenfeld bereits den neuen Wert (q18: 5), ein zweites `mut fn`
über den Alias schreibt in dasselbe `this` (n03: 7), und nach einem `throw` bleibt die
Teilschreibung stehen (q18: `t.v = 5`). Der Guide-Satz „written back to the caller's value"
(`05-structs-and-classes.md:61`) beschreibt Copy-in/Copy-out und trifft nicht zu. Die zweite
Fassung hat ihn dreimal ungeprüft übernommen (§1.1, §1.3 „Rückschreiben", W5) — sie hätte ihn
als „gelesen, nicht gemessen" führen müssen. W35.

**14. `[x] * n` erzeugt n Aliasse eines Struct-Werts.** *(Neu.)* `[S{v=1}] * 3; ys[0].v = 9` setzt
alle drei (q03), `ys[0].bump()` ebenso (n04); mit einer Variablen als `x` wird **einmal** kopiert
und dann n-mal geteilt (`z.v = 1`, `zs[1].v = 9`). Kontrollen: `[s, s]` kopiert jedes Element
(q03c), `[1] * 3` ist harmlos (n04). Das ist die Bauform, die die Spec für Arrays empfiehlt
(`03-types.md:72-73`), und sie steht nicht in §13's Emissionsliste. Von keiner Deklaration aus
sichtbar. W36.

**15. `l[0].v = 9` auf einem `Indexable`-Klassencontainer ist ein stiller No-Op.** *(Neu.)*
`List<S>`: `l[0].v = 9` lässt `l[0].v` bei 1, `l[0].bump()` ebenso (q19, n06); `S[]` schreibt in
place. `l[0]` ist das Ergebnis von `Indexable.get`, also ein Temporary (Befund 5) — dasselbe
Aussehen wie ein Array-Element, die entgegengesetzte Wirkung. W37.

**16. Feldweise Erstinitialisierung gibt es nicht.** *(Neu, klein.)* `var s: S; s.v = 1; s.w = 2;`
ist `SEM0018` „possibly unassigned" (q22); die Kontrolle `s = S{…}` danach `s.v = 5` läuft (n08).
Definite Assignment (§7.7) kennt nur ganze Bindungen. Kein Fehler, aber ein Satz für die Spec.

**17. Bindungsformen schreiben in Kopien und warnen mit dem falschen Text.** *(Neu.)*
`for (e in xs)` (q06), `for (e in l)` (n01), `let (a, _) = t` (n01), `if (let (a, _) = t)`,
`if (let c = o)` (n10), `match`-Bindung (r04): überall Kopie, überall `SEM0109` mit „'let' pins the
name" — für eine Schleifen- oder Pattern-Bindung gibt es kein `var`. Ein `mut fn` auf der
Schleifenvariablen (n01) ist stumm. Eine Struct-`catch`-Bindung existiert nicht (n07, `IR0001`).
W39.

**18. `p.x++` ist nicht lowerbar, `p.x += 1` schon** (q07/q07b: `LYR-IR0001` „only parameters and
locals"), und **`o?.mutate()` ist es nur, wenn `mutate` etwas zurückgibt** — dann trifft es den
Alias (q10 `IR0001` für `void`; n05 landet für `int`). W40, W41.

---

## 2. Sprachvergleich

| Sprache | Wert vs. Referenz | Unveränderlichkeit per Default | Update-Ausdruck | Rekursiver Werttyp | Preis |
|---|---|---|---|---|---|
| **Swift** | `struct`/`enum` Wert, `class` Referenz; **`Array`/`Dictionary`/`Optional` sind selbst Werttypen** | Ja für `let`: rekursiv unveränderlich; `mutating func` nur auf einer `var`; **`self` in einer nicht-`mutating` Methode ist `let`**, ein `mutating`-Aufruf darauf ist ein Fehler | nein (Idiom `var c = p; c.x = 3`) | verboten („value type cannot have a stored property that recursively contains it"); `indirect enum` oder Klassen-Box | CoW in der stdlib, ARC, und die Exklusivitätsregel: **statisch** für Locals und `inout` auf Locals, **dynamisch** (`swift_beginAccess`) für Klassen-Properties, Globale/Statics und Escaping-Closure-Captures *(korrigiert; die zweite Fassung zählte `inout`-Argumente pauschal zur Laufzeit)*; seit 5.9 `borrowing`/`consuming`/`~Copyable` |
| **Rust** | alles Wert; Teilen über `&`/`&mut` | Ja: Bindungen unveränderlich ohne `mut`; Borrow-Checker schließt *veränderliches* Aliasing aus | `Point { x: 3, ..p }` | nur über `Box`/`Rc` | Borrow-Checker und Lifetimes |
| **C#** | `class` Referenz, `struct` Wert; **`this` in einer Struct-Methode ist `ref this`** | Nein; nachgebessert mit `readonly struct` (7.2), `readonly`-Membern (8), `init` (9), `record struct` (10). **Ein `readonly`-Member, das ein nicht-`readonly` Member ruft, bekommt eine defensive Kopie und Warnung CS8656** | `with` (Records 9; Structs 10) | nur über Klassen | Der mutable-struct-Fallstrick: **`let`-artige Orte (`readonly`-Feld, `in`-Parameter) rufen mutierende Methoden still auf einer Kopie** |
| **F#** | Records Referenzen, `[<Struct>]` Werte | Ja für Records; `mutable` pro Feld. **`[<Struct>]`-Member, die `this` schreiben, sind auf einem `let` aufrufbar und wirken still auf einer Kopie** — gemessen `fsharp_check.fsx` (F# 10): `let s = S(1); s.Bump()` kompiliert ohne Fehler und Warnung, `s.x` bleibt 1; `let mutable m` → 2 *(korrigiert: die zweite Fassung schrieb „aufrufbar nur auf einer veränderlichen Instanz" und nannte F# das beste Vorbild für W5 A — das Gegenteil ist gemessen)* | `{ p with X = 3 }` | Records rekursiv (Referenz) | Strukturelle Gleichheit kostet unbemerkt; Shadowing nur im Rumpf |
| **OCaml** | alles Referenz, Unveränderlichkeit macht das unbeobachtbar | Ja, `mutable` pro Feld | `{ p with x = 3 }` | ja | keine Layout-Kontrolle |
| **Scala** | überwiegend Referenz; `AnyVal`, `opaque type` | Ja für `case class`; `var`-Felder seit je | `p.copy(x = 3)` | ja | Allokation; `copy` ist eine Methode |
| **Kotlin** | überwiegend Referenz; `@JvmInline value class` | `val`/`var` pro Property | `p.copy(x = 3)` | ja | `copy` umgeht Konstruktor-Invarianten |
| **Go** | Structs/Arrays Werte; **Slices, Maps, Channels, Funktionswerte teilen ohne Stern** | Nein, kein Mechanismus | nein | nur `*T` | Aliasing ohne Marker bei vier Typkonstruktoren; `==` auf Structs nur, wenn alle Felder vergleichbar sind |
| **C** (nur W27) | — | — | — | — | Initializer-Ausdrücke „indeterminately sequenced" (C11 §6.7.9p23) — das Vorbild für „Reihenfolge unspezifiziert" |

### Go, die Gegenposition

Go teilt bei Slices, Maps, Channels und Funktionswerten ohne Stern — dieselbe Sorte Lücke wie
Lyrics `?Struct` (Befund 2) und `[x] * n` (Befund 14), nur bei anderen Typkonstruktoren. Go ist
nicht der Maßstab für W2; der Maßstab steht in Abschnitt 4 als eigene Forderung.

### Was zu Lyrics Charakter passt

- **F#-Records / Scala-`case class` / Kotlin-`data class`**: unveränderlich per Default plus
  Update-Ausdruck. Kein Borrow-Checker, keine Laufzeitkosten. Die nächste Nachbarschaft — als
  Aussage über die **Typformen**, nicht die Sprachen.
- **Swift** ist das **einzige** statische Vorbild für „`mutating` nur auf einem veränderlichen
  Ort" *(korrigiert: F# fällt weg, gemessen)*. Swifts Regel ruht auf der Exklusivitätsregel; Lyric
  übernähme sie ohne dieses Fundament — nach W35 (`this` ist eine Referenz, Reentranz sichtbar)
  ist das eine benannte Entscheidung, kein Nulltarif.
- **C#** ist das Vorbild für **W35** (`this` ist `ref this`) und die Warnung für W34: die defensive
  Kopie plus CS8656 ist genau das, was Lyric nicht tun soll — nicht still kopieren, sondern ablehnen.
- **Rust**: `Point { x: 3, ..p }` hängt an Move-Semantik; ohne Ownership ist die F#-Form richtiger.

---

## 3. Designfragen

> **Lesehilfe zur Uhrenspalte.** Drei Fragen tragen seit 4.6 eine Migrationswarnung
> (`LYR-SEM0107/0108/0109`, `appendix-a-diagnostics.md:198-200`, `12-diagnostics.md:86-91`,
> gelesen). „Uhr läuft" heißt: gemessen feuert sie an den genannten Orten; „teilweise" heißt: an
> gemessenen Orten schweigt sie; „fehlt" heißt: zu bauen. §12.5 legt fest, dass ein Migrationscode
> **mit** seiner Regel retiriert wird (`12-diagnostics.md:99-101`) — SEM0107/0108/0109 tragen
> **nicht** die 5.0-Fehler (W32).

### W1 — Ist ein Struct unveränderlich, sofern es nicht `mut struct` heißt?

Die Ankerfrage; W2, W3, W4, W5, W19, W24, W25, W34, W38 hängen daran. §3.4a nennt `mut struct`
als „the candidate on the table" (`03-types.md:115`, gelesen).

**Heute:** jedes Struct ist mutierbar; `mut` markiert die Methode, die ein Feld von `this`
**direkt** schreibt (`SemaRules.cs:389`; p10b, q02b) — und nur das (Befund 12). `mut struct` parst
nicht (r24).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Status quo | C# (vor `readonly struct`) | Befunde 1–6, 9, 12–15 bleiben offen; `let` bedeutet auf einem Struct nichts |
| B | `struct` unveränderlich, `mut struct` opt-in | F#-Records, Scala `case class`, Kotlin `data class` | 5.0-Bruch: jedes Struct mit `mut fn` braucht das Wort. Unbenutzbar ohne W7. Erzwingt W24, W25, W34, W38 |
| C | Unveränderlich pro Feld (`mut x: int`) | OCaml, F# `mutable` | Ein Typ ist nie ganz das eine; zwei Achsen |
| D | Keine Unveränderlichkeit, Aliasing nur im Typ sichtbar | Go (nur Structs) | Verlangt einen Referenz-Typkonstruktor, den Lyric nicht will |

**Empfehlung: B**, mit zwei Korrekturen an `plan_at_dossier.md:321-330` (gelesen): (1) „Posten 2
entfällt" gilt nur für unveränderliche Structs — für `mut struct` bleibt die `?Struct`-Lücke exakt
wie heute; (2) „könnte Geschwindigkeit bringen" ist zu messen (`STATUS.md:1806`: Scalar Replacement
60,6 ns/56 B → 18,2 ns/0 B — der Gewinn läge bei den Fällen, die *entkommen*; Optimierer-Gebiet).

**Bruch: major — als Regel; gemessen klein im eigenen Korpus (W42):** 8 Structs in der stdlib,
0 mit `mut fn`, 0 mit Feldschreibung von außen; Konformanzsuite 178 Fälle, 47 mit Struct, 0 mit
Struct + `mut fn`, **1** grüner Fall, der seine Bedeutung ändert
(`03-types/a_var_struct_binding_does_not_warn.lyr:11`); Guide: 4 `mut fn`, alle auf Klassen, **1**
Snippet mit Feldschreibung (`05-structs-and-classes.md:16`, die Kopie-Demo). Was **nicht** zählbar
ist: Benutzercode und Pakete. „major" bleibt, weil eine Bedeutung wechselt, nicht weil viel bricht.

**Uhr: fehlt, und das Kriterium hat drei Teile.** *(Korrigiert; die zweite Fassung hatte zwei.)*

| Kriterium | Folge |
|---|---|
| nur `mut fn` an der Deklaration | warnt zu wenig |
| `mut fn` **plus** Feldzuweisungen im eigenen Modul | entscheidbar pro Kompilation, aber ein Paket-Konsument bricht |
| **plus `extend`-Blöcke in fremden Modulen, die ein `mut fn` hinzufügen** (n09 gemessen; `05-interfaces.md:141-142`) | ohne diesen Teil ist das Kriterium unentscheidbar: der Eigentümer hält den Typ für unveränderlich, ein Konsument mutiert ihn |

**Empfehlung zum Kriterium:** das mittlere **plus** eine Warnung an **jeder** Schreib- und
`extend`-Stelle außerhalb des deklarierenden Moduls (die W19/W38-Uhr). So braucht keine Seite
Gesamtprogramm-Wissen.

**Hängt ab von:** W7, W19, W24, W25, W34, W38, W42, Sichtbarkeitsgebiet, Optimierer-Gebiet.

---

### W2 — Hat ein `?Struct` Wertsemantik?

**Heute:** nein — `?S → ?S` teilt (p2, p6, p14, p43, p52), `S → ?S` kopiert (p2), **`if (let c = o)`
kopiert** (n10), `o!` aliast (r11). Belegt in `FunctionLowerer.cs:3038` und
`Interpreter.cs:1162-1163` (gelesen). §13 sagt nichts. **Die einzige der vier §3.4a-Fragen ohne
Uhr** (`12-diagnostics.md:86-91`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `?T` ist ein Wert, wenn `T` einer ist: `Coerce` kopiert durch das Optional, `CopyStruct` rekursiert | Swift (`Optional` ist ein Enum, also Werttyp) | Die Kopie wird unbegrenzt tief: `struct Node { next: ?Node }` kopiert die ganze Kette; Swift verbietet rekursive Werttypen — dann ist W14 mitentschieden |
| B | `?Struct` bleibt geteilt, Mutation durch `!` verboten | — | Schließt nicht den `mut fn`-Pfad (W30) und nicht `?.` (W41) |
| C | W1=B macht die Frage für unveränderliche Structs gegenstandslos; für `mut struct` gilt A **mit** Verbot der Rekursion (`mut struct Node { next: ?Node }` → `SEM0056`) | Swift für den mutierbaren Teil, F#-Records für den Rest | Zwei Regeln, nur eine je sichtbar. **Setzt W23 voraus** (§13:195-196 und :881-882 fallen) und **W44** (Identität unbeobachtbar — sonst ist „darf teilen" falsch) |

**Empfehlung: C.** Spec-Satz: *„Für ein unveränderliches Struct ist die Unterscheidung nicht
beobachtbar, und die Implementierung darf teilen. Für ein `mut struct` kopiert `structcopy` durch
das Optional hindurch — an jeder Bindungsstelle, auch beim Narrowing —, und ein `mut struct` darf
sich nicht über `?T` selbst enthalten."* Dazu W30 (`!`-Aufruf), W41 (`?.`-Aufruf), W22, W36.

**Bruch: minor.** **Uhr: fehlt und ist zu bauen — die dringendste des Gebiets** neben W34: eine
Warnung an einer Zuweisung oder einem `mut fn`-Aufruf hinter `!`/`?.`, dessen Wurzel eine andere
Bindung ist.

**Hängt ab von:** W1, W14, W22, W23, W30, W36, W41, W44.

---

### W3 — Reicht `let` bis in die Felder?

**Heute:** nein (p1, r08, r15; `SemaRules.cs:380-395`). §3.4a: unspezifiziert, Termin 5.0.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `let` greift bei Werttypen rekursiv durch; bei Klassen bleibt es beim Namen | Swift | Bricht `let v; v.x = 9`. Verlangt W19 |
| B | `let` pinnt nur den Namen; Spec an den Compiler angleichen | heutiger Stand | „unveränderlicher Wert" hat dann keinen Ausdruck |
| C | Fällt unter W1=B weg | F#-Records, Scala `case class` | `var s: S` heißt nur noch „Name neu bindbar" |

**Empfehlung: C**, A als Rückfallposition.

**Zu `LYR-SEM0075` — nur halb entwarnt.** *(Korrigiert.)* Für **direkte** Feldschreibungen schweigt
der Hinweis (r27, r28, r29; Kontrolle r27b), und ein direkter `mut fn`-Aufruf zählt als Mutation
(n11). **Aber** auf `var s`, dessen einzige Mutationen über eine nicht-`mut` Methode laufen, die
intern ein `mut fn` ruft, feuert er (q02a) — und nach dem Wechsel auf `let` warnt nichts, die
Mutation bleibt (`t.v = 2`). Das ist kein Fehler des Hinweises, sondern Befund 12: die Sema hält
`viaThis()` für nicht-mutierend. **Mit W34 A verschwindet der Fall von selbst** (dann darf
`viaThis` `bump` nicht rufen). Bis dahin gilt die Entwarnung nur für direkte Schreibungen.

**Bruch: minor bis major.** **Uhr: läuft seit 4.6 als `LYR-SEM0109` — nur einstufig und nur für
Zuweisungen** (Befund 3, 12). 4.7-Arbeit: die stummen Zeilen der Tabelle in §1.4 Befund 3, inkl.
der zwei neuen (`this.inner.x`, `this.bump()`).

**Hängt ab von:** W1, W4, W19, W20, W34, Diagnostik-Gebiet.

---

### W4 — Ist ein Parameter dasselbe wie ein `let`?

**Heute:** unveränderliche Bindung mit veränderlichen Struct-Feldern (`MutabilityTests.cs:80-82,
103-111`; r13 mit `SEM0109`; `03-types.md:116-117`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Ein Parameter **ist** ein `let` | Swift | Bricht den getesteten Fall; der `var`-Umweg (p42) kostet einen `structcopy` (§13:884-886) |
| B | `mut`-Parameter | Rust, C# | Zweite Schreibweise neben `var w = v;` — Rule 2; spart die Kopie |
| C | Status quo | — | „unveränderlich, außer in den Feldern" |

**Empfehlung: A**, mit der Kopie als benanntem Preis; W31 entscheidet mit. Für die **schreibende**
Übergabe an den Aufrufer siehe **W38** — B löst sie nicht, sie macht nur das Local veränderlich.

**Bruch: klein.** **Uhr: läuft seit 4.6** (r13), einstufig. **Hängt ab von:** W1, W3, W19, W31, W38.

---

### W5 — Wer darf ein `mut fn` rufen?

**Heute:** jeder. Durch `let` (p11), auf einem Temporary (p17), durch `let`-Interface (p25/r26,
Boxen kopiert), Array-Element und Klassenfeld (p11), `?Struct` (r06), **aus einer nicht-`mut`
Methode auf `this`** (q02a, q17), **über `?.`** (n05), **auf einer Schleifenvariablen** (n01),
**auf `l[0]` eines Klassencontainers — wirkungslos** (n06).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Ein `mut fn` ist nur auf einem **veränderlichen Ort** aufrufbar | **Swift** (`mutating` nur auf `var`; `self` in nicht-`mutating` ist `let`) — **das einzige statische Vorbild** *(korrigiert: F# gemessen gestrichen, es kopiert still wie C#)* | Bricht `let v; v.shift(…)`. **Nicht implementierbar vor W19 und W34**: „veränderlicher Ort" ist ohne W19 undefiniert, und `this` in einer nicht-`mut` Methode ist ohne W34 keins von beiden |
| B | Zusätzlich: nur auf einem `mut struct` deklarierbar — **auch in einem `extend`-Block** (W38) | F#-Records/Scala-`case class` haben das Problem nicht, weil ihre Felder unveränderlich sind | Folgt aus W1=B. Berührt `Indexable.set` (W25) |
| C | Status quo | — | `mut fn` ist ein Kommentar |

**Empfehlung: A + B**, ausdrücklich mit: `mut fn` auf einem Temporary abgelehnt (W17), auf `this`
aus einer nicht-`mut` Methode abgelehnt (W34), auf `l[0]` eines Klassencontainers abgelehnt (W37).
**C#s defensiv-kopierende Antwort (CS8656, F# ebenso) ist das Gegenmodell:** nicht still kopieren,
sondern ablehnen.

**Bruch: minor.** **Uhr: fehlt** — gemessen stumm bei p11, p17, p25, r06, q02a, q17, n01, n05, n06.

**Hängt ab von:** W1, W3, W6, W19, W25, W30, W34, W37, W38, W41.

---

### W6 — Was bedeutet `mut` auf einer Klassenmethode?

**Heute:** an einer Stelle Pflicht (`SEM0042`, p53/p50), sonst bedeutungslos; `03-types.md:99-101`.
`class C { fn m() { this.v = 9; } }` warnt `SEM0108` (p10, r14) — **aber `fn m() { this.bump(); }`
mit `bump` als `mut fn` warnt nicht und schreibt** (q17).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `mut` erzwingen: eine Klassenmethode, die `this` schreibt **oder ein `mut fn` auf `this` ruft**, muss `mut` heißen | Rust (`&mut self`) | Mechanischer Bruch; das Wort bleibt Dokumentation, solange `let` bei Klassen den Namen pinnt |
| B | `mut` **und** `let` greifen: `let c: C` verbietet `c.mutate()` | C++ `const`, Rust `&self` | Kippt §7.1's Ausnahmesatz — eigene Runde |
| C | `mut` auf Klassen löschen | — | Nicht möglich (p53) |

**Empfehlung: A**, B ausdrücklich offen protokollieren.

**Bruch: minor** (A), **major** (B). **Uhr: läuft seit 4.6 als `LYR-SEM0108` — nur für direkte
Zuweisungen.** *(Korrigiert; die zweite Fassung schrieb „vollständig, nichts mehr zu bauen".)*
Der Aufrufpfad `this.bump()` (q17) ist stumm, Ursache `SemaRules.cs:323`. **Dieselbe 4.7-Arbeit
wie bei SEM0109** (W34).

**Hängt ab von:** Interface-Gebiet (`SEM0042`), W32, W34.

---

### W7 — Gibt es einen Update-Ausdruck `p with { x = 3 }`?

**Heute:** nein (`PAR0016`, r17). Vorgemerkt `plan_at_dossier.md:226`, Bedingung von `mut struct`
(`:326`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `p with { x = 3 }`, `with` kontextuell | C# `with`, F# `{ p with X = 3 }` | Ein Schlüsselwort; nach `with` ist `{` eindeutig. Verlangt W29 |
| B | `p.copy(x = 3)` | Scala, Kotlin | Braucht benannte Argumente; Sichtbarkeitsproblem (Kotlin) |
| C | `S { x = 3, ..p }` | Rust | `..` hätte drei Bedeutungen |

**Empfehlung: A.** Teilfragen: nicht für `class`; Enum-Varianten später; keine tiefen Pfade in
5.0; Defaults laufen nicht neu. **Neu:** `with` ist unter W37 B das Idiom für Struct-Elemente in
Klassencontainern: `l[0] = l[0] with { v = 9 }`.

**Bruch: nein.** **Hängt ab von:** W1, W27, W29, W37.

---

### W8 — Was bedeutet eine zweite Bindung desselben Namens?

**Heute:** unspezifiziert mit Uhr (p3, p48, p32, p49; `SEM0107` r14; §7.1:16-19).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Im selben Scope ablehnen; verschachteltes Shadowing bleibt | Swift, C# (CS0128), Go | Bricht still falsche Programme |
| B | Rust-Shadowing | Rust; F# nur im Rumpf | Idiomatisch für `let n = parse(n);`; erzwingt Modul-/Block-Unterscheidung |
| C | Status quo | — | die dritte, stille Antwort |

**Empfehlung: A** — wegen `LYR-SEM0097` (Pattern: zweimal derselbe Name → Fehler,
`appendix-a-diagnostics.md:190`); zwei Bindungsformen mit entgegengesetzter Antwort wären Rule 2.
Modul-Scope unter B: immer ablehnen, wie F#.

**Bruch: minor.** **Uhr: läuft seit 4.6.** **Hängt ab von:** Pattern-Gebiet, Modul-Gebiet, W4, W21.

---

### W9 — Zwei Schlüsselwörter für Wert und Referenz, oder eins?

**Heute:** `struct`/`class` mit identischem Layout (`13-bytecode.md:185-193`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Beibehalten | Swift, C# | — |
| B | Ein `type` plus Attribut | F# `[<Struct>]` | Verschiebt eine Grundeigenschaft in ein Attribut. Lyrics Attribute „describe; [they] do nothing" (`docs/guide/15-attributes.md:20`, gelesen; `14-embedding.md:407-408`: „no attribute in this vocabulary means anything to the compiler") — **mit einer Ausnahme:** `@Deprecated` erzeugt `LYR-SEM0076` (`appendix-a-diagnostics.md:214`, gelesen). Ein Attribut, das ein Layout trägt, wäre die zweite und eine ganz andere *(korrigiert: die zweite Fassung schrieb „ausdrücklich nicht semantisch tragend" ohne Fundstelle)* |
| C | Nur Referenzen | Scala, Kotlin, OCaml | Beide Vorbilder haben Werttypen nachgerüstet |

**Empfehlung: A**, begründet mit C's eigenem Preis, nicht mit Geschwindigkeit. **Bruch: nein.**

---

### W10 — Kann ein Klassenfeld unveränderlich sein?

**Heute:** nein (`docs/Grammar.md:237`; r25).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `let`/`var` in Feldposition | Swift, Kotlin, C# `readonly` | Neue Syntax, kein neuer Mechanismus |
| B | `immutable class` | — | grobkörnig |
| C | Nichts | Go | Keine Invariante; kein sicherer Map-Schlüssel aus einer Klasse |

**Empfehlung: A**; Default in 5.0 veränderlich, `let` opt-in. **Bruch: nein.** **Hängt ab von:**
Sichtbarkeitsgebiet, Konformanz-Synthese, W24, W46.

---

### W11 — Bekommt ein Werttyp Gleichheit geschenkt?

**Heute:** nein (`SEM0059`, p13).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Automatisch strukturell | F#, Scala, Kotlin | Referenzvergleich bei Klassen-/Array-Feld — still falsch |
| A′ | Automatisch, nur wenn alle Felder vergleichbar | Go, Rust `derive` | Kippt bei jeder Feldergänzung still |
| B | Synthese auf deklarierte Konformanz | Swift | Eine Zeile pro Typ, sichtbar |
| C | Status quo | Rust ohne `derive` | Handarbeit |

**Empfehlung: B.** **Bruch: nein.** **Hängt ab von:** Konformanz-Synthese, W10, W16, W44.

---

### W12 — Was ist ein Felddefault?

**Heute:** beliebiger Ausdruck, pro Konstruktion (p12), sieht Globale (p37), keine Geschwister
(p28); ausgelassenes Feld `SEM0106` (r01); werfender Default `SEM0034` an der Deklaration (r05).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Beibehalten | Swift, C#, Kotlin | Reihenfolge wird Vertrag (W27) |
| B | Nur `comptime`-Konstanten | Rust, Go | Verliert `Instant.now()`-Defaults |
| C | Konstruktorfunktion | Rust `Default` | Braucht Konstruktoren |

**Empfehlung: A**; Teilfrage „darf ein Default werfen": **a** (nein, Status quo). Diagnostik-Befund:
`SEM0034` nennt eine „enclosing function", die es an einem Feld nicht gibt. **Neu (Befund 16):** die
Spec sollte sagen, dass ein Struct nur als Ganzes initialisiert wird — feldweise Erstinitialisierung
ist `SEM0018` (q22).

**Bruch: nein.** **Hängt ab von:** `throws`-Gebiet, Diagnostik, W27.

---

### W13 — Feldkurzform im Initializer: `S { v }`

**Heute:** `SEM0052`+`SEM0045` (r18); Pattern punnt (`Grammar.md:555` vs `:495`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Belassen, Asymmetrie benennen | — | Tipparbeit |
| A′ | „nach `TypePath` ist `{` immer ein Initializer" | Rust | Nimmt der Trailing-Lambda-Regel eine Position |
| B | Andere Schreibweise | — | drittes Aussehen |
| C | Trailing-Lambda dort aufgeben | — | verliert mehr |

**Empfehlung: A**, als Abwägung. **Bruch: nein.**

---

### W14 — Was bricht einen Typzyklus?

**Heute:** Klasse, Array — und unbeabsichtigt `?T` (p4, p19, p19b; `TypeChecker.cs:5993-6010`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `?Struct` bricht nicht mehr (`SEM0056`) | Swift | Bricht laufenden Code |
| B | `?Struct` ist der gesegnete Indirektionspunkt | OCaml, Rust `Box` | Nur tragbar unter W1=B/W2=C; verlangt W23 **an beiden Stellen** |
| C | Eigener Marker | Swift `indirect` | Neues Wort für einen abgedeckten Fall |

**Empfehlung: B**, gekoppelt an W2=C und W23. Fällt W1, dann A. **Bruch: nein** (B), **major** (A).

---

### W15 — Eine Closure fängt ein Struct per Referenz

**Heute:** ja (p9; `03-types.md:77`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Beibehalten, Fallstrick benennen | C#, Swift ohne Capture-List | Ein Struct hinter einer Closure ist anders |
| B | Werttypen per Wert fangen | Swift `[s]`, Rust `move` | Zwei Capture-Regeln |
| C | Unter W1=B gegenstandslos | F#-Records | — |

**Empfehlung: A** (und damit C). **Bruch: nein.** **Hängt ab von:** W1, W22.

---

### W16 — Bleibt die Kopie flach, und ist das eine Verletzung des Maßstabs?

**Heute:** flach (p15; `13-bytecode.md:880-882`), keine Schreibweise fordert oder verhindert eine
Kopie.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Beibehalten, **plus** Spec-Satz „unveränderlich heißt flach unveränderlich" | Swift, C#, Go | Der Fallstrick bleibt — **aber er steht im Typ des Feldes** |
| A′ | A plus Deklarationshinweis bei jedem Struct mit referenztypigem Feld | `go vet` | **Trifft `struct Buffer { data: uint8[] }` — den häufigsten nützlichen Fall — und ist nicht zum Schweigen zu bringen** (Kritik, zutreffend) |
| B | Tiefe Kopie | — | unbezahlbar |
| C | Referenztypige Felder verbieten | — | verbietet `Buffer` |
| D | `Copy`/`Clone` sichtbar | Rust | setzt Moves voraus (`CONTRIBUTING.md:33`: GC only) |

**Empfehlung: A — zurück von A′, mit einer schärferen Begründung.** *(Korrigiert, gegen die zweite
Fassung und gegen die Kritik.)* Der Maßstab in Abschnitt 4 verlangt, dass **aus dem Typ** ablesbar
ist, ob geteilt wird. `xs: int[]` sagt „Array", und ein Array ist per §3 eine Referenz — der Maßstab
ist **erfüllt**, nicht verletzt. Verletzt ist er dort, wo der Typ einen **Wert** verspricht und die
Bindung einen Alias liefert: `?S` (Befund 2) und **`S[]` aus `[x] * n`** (Befund 14). Das sind die
zwei Stellen, nicht drei; beide werden geschlossen (W2, W36). A′'s Hinweis ist damit nicht nur
lästig, sondern unbegründet.

**D ausdrücklich abgelehnt.** **Bruch: nein.** **Hängt ab von:** W2, W36, W24.

---

### W17 — Schreibung auf einem Temporary (`mut fn` und Feldzuweisung)

**Heute:** `make().shift(9)` (p17) und `make().x = 9` (r12/r16) kompilieren, `make()` läuft —
**und `l[0].v = 9` / `l[0].bump()` auf jedem `Indexable`-Klassencontainer ist derselbe Fall**
(q19, n06).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Ablehnen | Swift („cannot use mutating member on immutable value") | Bricht Code, der nichts bewirkt — außer dem Seiteneffekt des Empfängers (r16). **Trifft jedes `l[i].field = …` auf `List<Struct>`** (W37) |
| B | Beibehalten | — | Stiller No-Op |

**Empfehlung: A, über die Uhr** (Warnung 4.7, Fehler 5.0; `plan_at_dossier.md:293/296`).

**Bruch: ungemessen.** *(Korrigiert; die zweite Fassung schrieb „minor".)* Im eigenen Korpus: 0
Vorkommen (stdlib schreibt keine Struct-Elemente durch `get`; W42). In Benutzercode ist
`l[i].x = …` ein gewöhnliches Muster, das heute still nichts tut — jede Ablehnung findet dort
Bugs, keine Features. **Uhr: fehlt.** **Hängt ab von:** W5, W19, W32, W37.

---

### W18 — Veränderlicher Modulzustand

**Heute:** `var` auf Modulebene `PAR0027` (p23), aber ein Modul-`let`-Struct ist feldweise
schreibbar (p16/r15, stumm).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `let`-only beibehalten, Lücke schließen; Modulzustand ist ein `let` auf eine Klasse | — | Eine Zeile mehr; im Text sichtbar |
| B | `var`-Globale | Go, C#, Kotlin | zweiter Weg |
| C | Status quo | — | Regel ohne Wirkung |

**Empfehlung: A.** **Bruch: minor.** **Uhr: fehlt** (`SemaRules.cs:338`: ein Global ist weder
`LocalSymbol` noch `ParameterSymbol`). **Hängt ab von:** W3, W19, W20.

---

### W19 — Was ist die Wurzel einer Zugriffskette?

**Die Frage, ohne die W1, W3, W4, W5, W17, W18, W34 und W37 nicht implementierbar sind.**

**Heute:** keine Rekursion zur Wurzel (`SemaRules.cs:356-395`). Gemessen: r02, r09, r10, r11,
r12/r16, r04, r03, r07 (zweite Fassung), **q16, q02a, q17, q19, n06, q03** (diese).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Vollständige Rekursion zur Wurzel; Klassenfelder und **`T[]`-Elemente** eröffnen eine neue Wurzel; Aufrufergebnisse sind kein Ort | **Kein Vorbild exakt.** *(Korrigiert.)* Swift für Struct-Felder, Klassen-Properties und `self`; **nicht** für Arrays (in Swift ist `Array` ein Werttyp — `let xs = [S()]; xs[0].v = 9` ist ein Fehler) und **nicht** für die Interface-Box (`let p: any P = S(); p.mutate()` ist abgelehnt). C# für die Box (ein geboxtes Struct ist eine Kopie) | Elf Fälle statt einer. Dafür **ist** es die Regel |
| B | Rekursion nur über Struct-Felder | — | Unterscheidet sich bei `!`, Aufruf und `this` |
| C | Status quo | — | Die Uhren versprechen Vollständigkeit (`03-types.md:119`) und liefern eine Ebene |

**Empfehlung: A**, mit dieser Tabelle als Spec-Text — **elf** Zeilen *(die zweite Fassung kündigte
sieben an und hatte neun)*:

| Wurzel-/Zwischenglied | Setzt die Kette fort? | Begründung / Probe |
|---|---|---|
| Feld eines **Structs** | ja, rekursiv | Der Wert gehört der Wurzel (r02) |
| **`this` in einer nicht-`mut` Methode** | **ja — und `this` ist dort ein unveränderlicher Ort** | **neu**; W34 (q16, q02a); Swift: `self` ist `let` |
| **`this` in einem `mut fn`** | ja — `this` ist ein veränderlicher Ort (Referenz, W35) | q18, n03 |
| Feld einer **Klasse** | nein — neue Wurzel, schreibbar | §7.1 (r09) |
| Element eines **`T[]`** | nein — neue Wurzel, schreibbar | Das Array ist eine Referenz (p35, r10) |
| **Index auf einem `Indexable`-Klassentyp** (`l[0]`) | **nein — gar kein Ort** (Ergebnis von `get`) | **neu**; W37 (q19, n06); Ablehnung wie Aufruf |
| **`!`** / **`?.`** auf einem Optional | ja, rekursiv | Sonst ist W2 wieder offen (r11, r06, n05); W30, W41 |
| Ergebnis eines **Aufrufs** | nein — kein Ort | W17 (r12) |
| **Pattern-/Schleifen-/`if let`-Bindung** | ja — unveränderliche Bindung | W21, W39 (r04, q06, n01, n10) |
| **`static let`** / **Modul-`let`** | ja — unveränderliche Bindung | W20, W18 (r03, r15) |
| **Interface-Box** | nein — beim Boxen wird kopiert | p25/r26; C#-Modell |
| **Index auf einem Struct** (`g[0] = …`) | folgt der Wurzel von `g` | W26 (r07) |

**Bruch: major.** **Uhr: teilweise** — `SEM0109` deckt Zeile 1 einstufig und Zeile 9 ab; alle
anderen sind 4.7-Arbeit. **Hängt ab von:** W1, W2, W3, W4, W5, W17, W20, W21, W26, W34, W35, W37,
W39, W41.

---

### W20 — Ist `static let` eine Bindung im Sinne von W3?

**Heute:** global schreibbar (r03), keine Warnung; Guide sagt „constant" (`05:70`);
`docs/Grammar.md:236`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Dieselbe Regel wie jedes `let` (W3/W19) | Swift `static let` | Kostenlos mit W3 |
| B | Eigene, schärfere Regel: immer unveränderlich | **C# `static readonly` eines Struct-Typs: `Vec2.Zero.x = 1` ist CS1650** — der Compiler verweigert die Feldschreibung durch ein `readonly`-Feld *(korrigiert: Java `static final` gestrichen — es pinnt nur die Referenz, das ist Lyrics heutige Lage r03, nicht die schärfere Regel)* | Zwei Regeln für eine Schreibweise |
| C | Status quo | — | Eine Konstante, die keine ist |

**Empfehlung: A**, B als Rückfall; mindestens den Guide-Satz korrigieren. **Bruch: minor.**
**Uhr: fehlt** (`P.ORIGIN` ist ein `MemberExpr`). **Hängt ab von:** W3, W18, W19.

---

### W21 — Ist eine Pattern-Bindung ein schreibbarer Ort?

**Heute:** schreibbar, landet nirgends, warnt (r04). Verallgemeinert auf alle Bindungsformen in
**W39**.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Unveränderliche Bindung; Schreibung in 5.0 abgelehnt | Swift `case .a(let inner)`, Rust `ref`/`ref mut` explizit | Nichts geht verloren |
| B | Bindet den Ort, schreibt zurück | Rust `ref mut` | Neuer schreibbarer Ort |
| C | Status quo | — | Warnt und tut nichts |

**Empfehlung: A.** Diagnostik: `SEM0109`'s Text „5.0 settles what a struct is" ist hier falsch —
es gibt nichts zu settlen. **Bruch: minor.** **Uhr: läuft, falscher Text.** **Hängt ab von:** W3,
W17, W19, W39.

---

### W22 — Kopiert `structcopy` durch Tupel, Enum-Nutzlast und Closure-Umgebung?

**Heute, gemessen — und beobachtbar.** *(Korrigiert.)* Die zweite Fassung hatte r21 in die
falsche Richtung gemessen (Kopie einer Kopie) und „unbeobachtbar" geschlossen. Von der
**Quellseite** ist es beobachtbar (q04): `let t = (s, 2); s.v = 9; let (a, _) = t;` → `a.v = 1`;
`let e = E.A(u); u.v = 9;` → im `match` `i.v = 1`. **Beide Konstruktoren kopieren heute.**
Closure-Umgebung: geteilt (p9; `03-types.md:77`). Generischer Durchlauf: kopiert (r20).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | §13 zählt abschließend auf: alles, was nicht „class, array, interface" ist, kopiert — Tupel und Nutzlast eingeschlossen | Swift | **Kostet nichts, weil es bereits so ist (gemessen)** — nicht „weil unbeobachtbar" |
| B | Nur das Beobachtbare benennen | heutiger Stand | Die Lücke wandert mit |
| C | Tupel/Nutzlast teilen ausdrücklich | — | Widerspricht der Wertsemantik **und** dem gemessenen Verhalten |

**Empfehlung: A.** Spec-Satz: *„Ein Strukturwert, der in einen Tupelplatz, eine Enum-Nutzlast oder
eine Closure-Umgebung gebunden wird, wird kopiert wie an jeder anderen Bindungsstelle — mit der
Ausnahme der Closure-Umgebung (§3, capture by reference)."* Die Zeile schreibt das gemessene
Verhalten fest.

**Bruch: nein** (bereits so). **Hängt ab von:** W1, W2, W7, W15, Collections-Gebiet.

---

### W23 — Welcher Satz fällt: §13's Zyklenregel oder `struct Node { next: ?Node }`?

**Zwei Stellen, nicht eine.** *(Korrigiert.)* Die Regel `13-bytecode.md:195-196` („must not contain
itself … Recursion through a class, an array or an interface is permitted") und die Begründung
`:881-882` („terminates without cycle detection because a struct cannot contain itself"). Gemessen
(p4) verletzt der Compiler die Regel.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Beide Sätze bleiben, das Programm fällt (= W14 A) | Swift | Rekursive Werttypen gibt es nicht mehr |
| B | Das Programm bleibt; **beide** Stellen werden umgeschrieben: `:195-196` nimmt `?T` in die Erlaubnisliste („… through a class, an array, an interface **or an optional** is permitted"), `:881-882` begründet neu („the recursion enters struct fields only; an optional field is one word and is not entered — for a `mut struct`, whose copy enters the optional, self-containment through `?T` is refused, `LYR-SEM0056`") | OCaml, Rust `Box` | Zwei Spec-Stellen, die zusammen geschrieben werden müssen |
| C | Beides bleibt | heutiger Stand | §13 widerspricht sich selbst und dem Compiler |

**Empfehlung: B**, gekoppelt an W2=C und W14 B. **Bruch: nein** (B). **Hängt ab von:** W2, W14.

---

### W24 — Wie mischen sich `struct` und `mut struct`?

**Heute:** die Frage existiert nicht (r24). Flache Kopie (p15).

| Fall | Optionen | Empfehlung |
|---|---|---|
| Unveränderliches Struct mit `mut struct`-Feld | (a) verboten; (b) erlaubt; (c) beim Lesen kopiert | **(a)** |
| `mut struct` mit unveränderlichem Feld | (a) erlaubt; (b) verboten | **(a)** |
| Unveränderliches Struct mit Klassen-/Array-Feld | (a) erlaubt, flach; (b) verboten | **(a)** — „unveränderlich" heißt flach (W16) |
| Interface-Feld | wie Klassenfeld | erlaubt, flach |
| **`extend` aus einem fremden Modul mit `mut fn`** *(neu)* | (a) nur auf `mut struct`; (b) frei wie heute | **(a)** — folgt aus W5 B; W38 |
| **`[x] * n` mit `mut struct`-Element** *(neu)* | (a) n Kopien; (b) Aliasse | **(a)** — W36 |

**Vorbild:** Swift und F#-Records: `let`-Struct mit Klassen-Property erlaubt die Mutation des
Objekts. **Bruch: nein** für sich, **major** als Teil von W1. **Hängt ab von:** W1, W2, W16, W36,
W38.

---

### W25 — Trägt ein Typparameter Veränderlichkeit? Und `mut struct` für Interfaces?

**Heute:** monomorphisiert (`03-types.md:84-86`), generischer Durchlauf kopiert (r20).
`mut fn set` in `Indexable<T>` (`stdlib/std/collections.lyr:23`). **Gemessen (grep): kein Struct in
der stdlib implementiert `Indexable`** — nur `List<T>` (Klasse, `:41`).

**(a)** Schranke `mut T`? **A: keine** (Swift). **(b)** Muss ein `Indexable`-Struct `mut struct`
sein? **A: ja.** **Bruch: minor — gemessen 0 Implementierer in der stdlib**; bei Benutzern
unbekannt. **Hängt ab von:** W1, W5, W26, W37, Interface-, Generics-, Collections-Gebiet.

---

### W26 — Warum ist ein `IndexExpr` auf einem Struct ein veränderlicher Ort?

**Heute:** der Typ entscheidet (`SemaRules.cs:362`; r07).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Folgt der Wurzel (W19) | Swift `subscript set` braucht `var`; C# Indexer auf `readonly struct` | Bricht `let g; g[0] = …` |
| B | Status quo | — | `let` bedeutet dort nichts |
| C | Indexierbare Structs verbieten | — | verbietet eine Bauform |

**Empfehlung: A.** **Bruch: minor.** **Uhr: fehlt.** **Hängt ab von:** W5, W19, W25, W37.

---

### W27 — In welcher Reihenfolge werden Initializer-Felder und Defaults ausgewertet?

**Heute, gemessen (r19, r22, r19b):** (1) explizite Felder in **Schreibreihenfolge**; (2) Defaults
**danach**; (3) Defaults untereinander in **Deklarationsreihenfolge**; (4) der Default eines
gesetzten Feldes läuft **nicht**. Die Spec sagt nichts (Suche über `lyric-spec/spec/`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Gemessenes festschreiben | **Rust** (Reference, „Evaluation order of operands": Struct-Ausdrücke werten Felder in Schreibreihenfolge) *(korrigiert: Rust gehört hierher, nicht zu C)*. **C# ist gespiegelt:** Feldinitialisierer laufen **zuerst** (im Konstruktor, Deklarationsreihenfolge), Objektinitialisierer **danach**, und der Initialisierer eines explizit gesetzten Feldes läuft **trotzdem** — Punkt 2 ist umgekehrt, Punkt 4 hat kein Gegenstück *(korrigiert: die zweite Fassung nannte es „dieselbe Zweiteilung")* | Zwei Regeln zu merken |
| B | Alles in Deklarationsreihenfolge | Swift | Bruch; eine Regel |
| C | Unspezifiziert | **C** (C11 §6.7.9p23: „indeterminately sequenced") *(korrigiert: Rust gestrichen)* | Das Loch, das dieses Dossier sonst schließt |

**Empfehlung: B.** **Bruch: minor.** **Uhr: fehlt.** **Hängt ab von:** W12, W7.

---

### W28 — Was bedeutet das Gebiet für Einbettung und `extern`?

**Heute:** kein Struct kreuzt die `extern "dotnet"`-Grenze (`SEM0099`, r23; `TypeChecker.cs:485-486`);
die Embedding-API kennt nur Host-Handles und Primitive (`src/Lyric.Embedding/HostTypeBuilder.cs`,
`HostFunction.cs`, gelesen).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Grenze bleibt; `mut struct` ändert nichts an `.lyrbc`, API, Handles | — | Kostenlos heute |
| B | Structs kreuzen als **Kopie** | C# `struct` über P/Invoke (blittable, by value) *(korrigiert: Swift `@frozen` gestrichen — es ist ein ABI-Layout-Versprechen für Library Evolution, keine Aussage über Kopie vs. Alias)* | Der Host sieht einen Schnappschuss |
| C | Structs kreuzen als Alias | — | Jede Garantie endet an der Grenze |

**Empfehlung: A jetzt, B festgelegt für später.** Die **Paketgrenze** ist eine eigene Frage: **W43**.
**Bruch: nein.** **Hängt ab von:** W1, W2, W43, FFI-, Bytecode-Gebiet.

---

### W29 — Ist `p with { … }` ein Primärausdruck?

**Heute:** `with` ist ein Bezeichner (r17).

| Teilfrage | Empfehlung |
|---|---|
| Kettbar? | ja, linksassoziativ |
| Direkt nachstellbar? | als Argument ja; vor `.` Klammern |
| Bindungsstärke | schwächer als Postfix, stärker als `??` |
| Respektiert Sichtbarkeit? | **ja, zwingend** (Kotlins nachgezogene Regel) |
| Privates Feld ausgelassen? | erlaubt, wird kopiert |

**Vorbild:** C# `with`. **Bruch: nein.** **Hängt ab von:** W7, Sichtbarkeits-, Operatoren-Gebiet.

---

### W30 — Was tut ein `mut fn` durch `!` auf einem `?Struct`?

**Heute:** trifft den Alias (r06). Für `?.` siehe **W41**.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Folgt aus W2=C: für `mut struct` kopiert `?Struct`, der Aufruf trifft die Kopie der Bindung | Swift | Nur, wenn die Regel den Aufruf einschließt |
| B | Folgt aus W5: `b!` ist kein veränderlicher Ort | Swift auf `let` | Verbietet auch den legitimen Fall auf `var` |
| C | Status quo | — | Bleibt auch unter W2=B offen |

**Empfehlung: A**, mit dem Zusatz im W2-Satz. **Bruch: minor.** **Uhr: fehlt.** **Hängt ab von:**
W2, W5, W19, W41.

---

### W31 — Braucht die Sprache ein Kostenmodell für große Werte?

**Heute:** nur §13's Emissionsregel und die Vec2-Zahl (`STATUS.md:1806`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Nichts | Go | Kosten durch Messen |
| B | Guide-Regel „ab ~N nimm eine Klasse" plus Messung | C# (16-Byte-Richtlinie) | Eine Zahl, die altert |
| C | `in`/`ref readonly` **oder `inout`/`ref`** (lesend wie schreibend, W38) | C#, Rust `&T`/`&mut T`, Swift `inout` | Zweiter Mechanismus neben `class`; braucht Aliasing-Regel |
| D | CoW in der stdlib | Swift | Referenzzählung |

**Empfehlung: B**, C abgelehnt (beide Richtungen; die schreibende steht jetzt in W38). **Bruch:
nein.** **Confidence:** der 200-Byte-Fall ungemessen. **Hängt ab von:** W1, W4, W38, Optimierer.

---

### W32 — Welche Diagnosecodes tragen die neuen Verbote?

**Heute:** alles `LYR-SEM0019` (p10b). §12.5: Migrationscodes retirieren mit ihrer Regel.

| Verbot | Heute | Empfehlung |
|---|---|---|
| Feldschreibung durch unveränderliche Bindung (W3/W4/W39) | `SEM0109` | eigener Code, Fix im Text |
| `mut fn` auf nicht-veränderlichem Ort (W5) | stumm | eigener Code |
| Schreibung/`mut fn` auf Temporary, inkl. `l[0]` (W17/W37) | stumm | eigener Code, Text nennt `l[0] = l[0] with {…}` |
| `mut fn` ohne `mut struct` deklariert, auch in `extend` (W1/W5 B/W38) | n/a | eigener Code (Deklarationsfehler) |
| **nicht-`mut` Methode ruft `mut fn` auf `this` oder schreibt `this.a.b`** (W34) | **stumm** | **`SEM0019` mit eigenem Text** — dieselbe Regel wie die direkte Zeile, jetzt transitiv |
| Klassenmethode schreibt `this` ohne `mut` (W6 A) | `SEM0108` | `SEM0019` |
| `++`/`--` auf einem Feld (W40) | `IR0001` | wird lowerbar; sonst derselbe Code wie `+=` |

**Vorbild:** Rust (E0594/E0596), Swift. **Empfehlung: B** (vier neue Codes + `SEM0019` für W6 A und
W34). **Bruch: nein.** **Hängt ab von:** W1, W3–W6, W17, W21, W34, W37, W39, W40, Diagnostik.

---

### W33 — In welcher Reihenfolge laufen die Warnstufen, und was deckt 4.6 nicht ab?

`lyrfix` existiert nicht (`plan_at_dossier.md:279, 284, 319`). **Was 4.6 leistet — korrigiert:**

| Frage | Code | Status |
|---|---|---|
| W8 | `SEM0107` | vollständig (r14, p3, p48) |
| W6 | `SEM0108` | **nur direkte Zuweisungen** — der Aufrufpfad (q17) ist stumm *(korrigiert)* |
| W3/W4 | `SEM0109` | nur einstufig, nur Zuweisungen (Befund 3) |
| W21/W39 | `SEM0109` | feuert auf allen Bindungsformen (r04, q06, n01, n10), mit falschem Text |

**Ohne Uhr — die Liste, jetzt elf Posten** *(die zweite Fassung hatte sieben)*:

1. **W34** — Transitivität: `this.bump()` und `this.a.b = …` aus nicht-`mut` Methoden, auf Struct
   **und** Klasse (q02a, q16, q17). **Betrifft den Kern von `mut fn` — vor W2 zu bauen.**
2. **W2** — `?Struct`-Aliasing (Feldschreibung und `mut fn` hinter `!`, r06/r11).
3. **W36** — `[x] * n` mit Struct-Element (q03, n04).
4. **W37** — `l[0].x = …` / `l[0].mutate()` auf `Indexable`-Klassencontainer (q19, n06).
5. **W41** — `mut fn` über `?.` (n05).
6. **W38** — `extend` aus fremdem Modul mit `mut fn` (n09).
7. **W5** — `mut fn` auf nicht-veränderlichem Ort (p11, p25, n01).
8. **W17** — Schreibung/`mut fn` auf Temporary (p17, r12).
9. **W19/W3** — die stummen Wurzeln: zweistufig (r02), `!` (r11), `static let` (r03), Modul-`let`
   (r15), Array-Element (r10), Index-auf-Struct (r07).
10. **W21/W39** — Bindungsformen: Uhr läuft, Text falsch; `mut fn` auf Schleifenvariable stumm (n01).
11. **W27** — Auswertungsreihenfolge, falls B.

**Zu `SEM0075` — nur für direkte Schreibungen entwarnt** (W3). Der Aufrufpfad (q02a) schließt sich
mit W34.

**Reihenfolge:** 4.7: die elf Uhren, **beginnend mit W34 und W2** (W34, weil sie den Kern trifft
und SEM0108/SEM0109 nachweislich vorbeilaufen; W2, weil sie am längsten still bleibt). 4.7: `lyrfix`
für die mechanischen Fälle (W1-Schlüsselwort, W4-`var`-Umweg) mit dem Default aus **W45**. Erst
dann 5.0, alles zusammen (§12.5).

**Bruch: nein.** **Hängt ab von:** allen Positionen, Werkzeuge-, Diagnostik-Gebiet.

---

### W34 — Ist `mut` transitiv? *(neu)*

**Die Frage, die die zweite Fassung nie gestellt hat, und die den Kern von `mut fn` betrifft.**

**Heute:** nein. Eine nicht-`mut` Struct-Methode darf `this.bump()` rufen (`bump` ein `mut fn`),
`this.inner.bump2()` rufen und `this.inner.x = 9` schreiben — ohne Fehler, ohne Warnung, und es
landet auch durch `let` (q02a, q16 gemessen). Auf einer Klasse dasselbe ohne `SEM0108` (q17).
Kontrolle: `this.v = 9` direkt ist `SEM0019` (q02b). Ursache gelesen: `SemaRules.cs:389`
(`m.Target is ThisExpr` — eine Ebene) und `:323` (`target is not MemberExpr` — Aufrufe erreichen
die Uhr nie); für einen `mut fn`-Aufruf auf `this` gibt es **keinen** Check. **`mut fn` bedeutet
heute „schreibt ein Feld von `this` direkt", nicht „ändert `this`."** Folge: SEM0075 schiebt genau
diesen Fall in ein `let` (q02a), und W5, W6, W19 sind ohne die Antwort nicht implementierbar.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **`mut` ist transitiv.** In einer nicht-`mut` Methode ist `this` ein unveränderlicher Ort (W19-Zeile): `this.bump()`, `this.inner.bump2()`, `this.inner.x = 9` sind Fehler; erlaubt bleibt, was auf einem `let` erlaubt ist (Klassenfelder, `T[]`-Elemente — W19). Gilt auf Structs und, unter W6 A, auf Klassen | **Swift** (`self` in nicht-`mutating` ist `let`; ein `mutating`-Aufruf darauf ist „cannot use mutating member on immutable value"). **C#** als Gegenmodell: ein `readonly`-Member, das ein nicht-`readonly` Member ruft, kopiert `this` defensiv und warnt CS8656 — still, statt abzulehnen | Bricht jede nicht-`mut` Methode, die heute ein `mut fn` ruft (im eigenen Korpus: 0 auf Structs, W42; auf Klassen unter W6 A ungezählt). Verlangt W19 (Definition „Ort") und W35 (`this` ist eine Referenz — also ist der Ort *der* Ort) |
| B | Nur direkte Feldschreibungen zählen; Aufrufe frei — der Status quo, ehrlich dokumentiert: „`mut` markiert eine Methode, die ein Feld von `this` **direkt** zuweist" | — | `mut fn` bleibt ein Kommentar für alles Indirekte; `let` auf einem Struct ist dann durch jede nicht-`mut` Methode umgehbar — W3 und W5 werden hohl |
| C | `mut` wird **inferiert** (transitiv, vom Compiler berechnet), das Wort entfällt | — (Rust verlangt `&mut self` explizit; kein Vorbild) | Interface-Konformanz (`SEM0042`, §5.1) vergleicht Signaturen — ein inferiertes `mut` steht in keiner Signatur; Rule 2 gegen zwei Mechanismen |

**Empfehlung: A.** Ohne Transitivität ist keine der Regeln W3/W5/W6/W19 mehr als eine Ebene wert,
und die Uhren SEM0108/SEM0109 laufen an genau dieser Stelle vorbei — ein Programm kann sie
vollständig befolgen und in 5.0 trotzdem brechen. **Spec-Satz:** *„In einer Methode ohne `mut` ist
`this` ein unveränderlicher Ort. Eine Methode, die `this` ändert — durch eine Zuweisung an ein Feld
oder ein Feld eines Feldes, oder durch einen `mut fn`-Aufruf auf `this` oder einem Struct-Feld von
`this` — heißt `mut`."* Die Klassenfeld- und Array-Ausnahmen folgen aus W19, nicht aus einer
eigenen Regel.

**Bruch: minor** (Struct-Seite; gemessen 0 im eigenen Korpus) — **plus W6 A's Bruch auf Klassen.**
**Uhr: fehlt für beide Familien** — SEM0108 und SEM0109 brauchen den Aufrufpfad und die zweite
Ebene; das ist die erste 4.7-Arbeit des Gebiets (W33). **Confidence:** gemessen (q02a, q02b, q16,
q17), Ursache gelesen.

**Hängt ab von:** W5, W6, W19, W35, W32.

---

### W35 — Ist `this` in einem `mut fn` eine Referenz oder Copy-in/Copy-out? *(neu)*

**Heute: eine Referenz auf den Ort des Aufrufers.** Gemessen (q18): mitten im `mut fn` liest ein
Alias über ein Klassenfeld bereits den neuen Wert; nach einem `throw` aus dem `mut fn` bleibt die
Teilschreibung stehen. Gemessen (n03): ein `mut fn`, das über den Alias ein zweites `mut fn` auf
dasselbe Struct ruft, sieht danach dessen Schreibung in seinem eigenen `this`. Der Guide-Satz
„written back to the caller's value" (`05-structs-and-classes.md:61`, gelesen) beschreibt
Copy-in/Copy-out und ist als Beschreibung falsch. **Die zweite Fassung hat ihn dreimal ungeprüft
übernommen.**

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Referenz, festgeschrieben.** *„`this` in einem `mut fn` bezeichnet den Ort des Aufrufers; Schreibungen sind sofort sichtbar — durch jeden Alias, und nach einem `throw`."* Guide 05:61 korrigieren | **C#** (`this` in einer Struct-Methode ist `ref this`), Rust `&mut self` | Reentranz ist beobachtbar, sobald ein Alias existiert — unter W1=B/W2=C nur noch für `mut struct` hinter Klassenfeld, `T[]`-Element oder `?` (W2). Keine Exklusivitätsregel: derselbe Zustand wie bei einer Klasse, und so zu benennen |
| B | **Copy-in/Copy-out**, formal wie Swift `inout` | Swift (`inout`; die Exklusivitätsregel garantiert, dass niemand den Unterschied sieht) | Ein `structcopy` pro `mut fn`-Aufruf (hin und zurück) — der Posten, den W1 einsparen will; ohne Exklusivitätsregel sieht ein Alias **veraltete** Werte, und eine Teilschreibung vor `throw` verschwindet — beides ist eine echte Semantikänderung gegenüber heute. Braucht genau die Regel, die §4 als „nicht übernehmen" protokolliert |
| C | Unspezifiziert lassen | — | Der Guide-Satz bleibt falsch, und W34 A hat keinen definierten Ort |

**Empfehlung: A.** Sie beschreibt, was ist, kostet nichts, und macht W34's „Ort" eindeutig. **Was
protokolliert gehört:** *„Ein `mut fn` kann Schreibungen beobachten, die während des Aufrufs durch
einen anderen Alias auf denselben Wert erfolgen. Das ist dieselbe Lage wie bei einer Klasse; die
Sprache verspricht keine Exklusivität."* Unter W1=B ist das für unveränderliche Structs
gegenstandslos.

**Bruch: nein** (beschreibt das Gemessene). **Uhr: keine nötig.** **Confidence:** gemessen (q18,
n03); Guide-Satz gelesen.

**Hängt ab von:** W2, W19, W34.

---

### W36 — Kopiert `[x] * n` das Struct n-mal oder teilt es n Slots? *(neu)*

**Heute: es teilt.** `[S{v=1}] * 3; ys[0].v = 9` → alle drei 9 (q03); `ys[0].bump()` ebenso (n04);
mit einer Variablen als `x` wird einmal kopiert und n-mal geteilt (`z.v = 1`, `zs[1].v = 9`).
Kontrollen: `[s, s]` kopiert jedes Element (q03c), `[1] * 3` ist harmlos (n04). Die Spec empfiehlt
die Bauform (`03-types.md:72-73`: „the value is built with `[x] * n`"), und §13's Emissionsliste
(`:884-886`) nennt „element assignment", nicht die Repetition. **Von keiner Deklaration aus
sichtbar; verletzt den Maßstab aus Abschnitt 4 (der Typ `S[]` verspricht Werte, die Slots sind
Aliasse).**

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Die Repetition kopiert n-mal** — ein `structcopy` pro Slot, `[x] * n` kommt in §13's Emissionsliste | Swift `Array(repeating:count:)` (kopiert den Wert), Rust `[x; n]` (verlangt `Copy`, sonst `Clone`) | n Kopien beim Bau — was der Leser ohnehin annimmt. Unter W1=B nur für `mut struct` beobachtbar |
| B | Teilen dokumentieren | — | Ein Array, dessen Elemente keine Werte sind; kein Vorbild |
| C | `[S] * n` für Struct-Elemente ablehnen | Rust für nicht-`Copy` ohne `Clone` | Verbietet die Spec-Bauform für den Normalfall |

**Empfehlung: A.** Spec-Satz zu §13: *„`[x] * n` bindet `x` in n neue Orte; ein Strukturwert wird
für jeden kopiert."* Und W24 bekommt die Zeile.

**Bruch: minor** (Programme, die auf das Teilen bauen, sind mit hoher Wahrscheinlichkeit Bugs —
gemessen ist der Fall in stdlib, Suite und Guide nicht vorhanden, W42). **Uhr: fehlt** — Warnung
in 4.7 an `[x] * n` mit Struct-Elementtyp, deren Slots später feldweise geschrieben werden; unter
W1=B eingeschränkt auf `mut struct`. **Confidence:** gemessen (q03, q03c, n04), Spec gelesen.

**Hängt ab von:** W1, W2, W16, W19, W24.

---

### W37 — Was bedeutet `l[0].v = 9` auf einem `Indexable`-Klassencontainer? *(neu)*

**Heute: ein stiller No-Op.** `List<S>`: `l[0].v = 9` lässt den Wert bei 1 (q19), `l[0].bump()`
ebenso (n06), während `xs[0].v = 9` auf `S[]` in place schreibt. `l[0]` ist das Ergebnis von
`Indexable.get` — ein Temporary (W17) —, und `SemaRules.cs:362` erlaubt die Schreibung, weil der
*Typ* indexierbar ist. **Dasselbe Aussehen wie ein Array-Element, die entgegengesetzte Wirkung** —
und W19's Zeile „Arrayelement" der zweiten Fassung galt stillschweigend für jeden Container.
Betrifft jedes Struct-Element in jedem Collections-Klassentyp der stdlib (`List`, `Deque`, `Map`-Werte).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Writeback-Synthese:** `l[0].v = 9` ⇒ `tmp = l.get(0); tmp.v = 9; l.set(0, tmp)`; ebenso für `l[0].mutate()` | Swift (Subscript-Accessoren mit `modify`/Writeback — genau so mutiert Swift ein Werttyp-Element einer Collection) | Zwei Aufrufe statt einem; ein zweiter Mechanismus (Accessor-Synthese) neben Feldzuweisung und `with`; verlangt, dass `get`/`set` ein Paar sind — `Indexable` verspricht das nicht (`collections.lyr:19-24`) |
| B | **Ablehnen:** `l[0]` ist ein Aufrufergebnis, also kein Ort (W17 A, W19-Zeile). Das Idiom ist `l[0] = l[0] with { v = 9 }` (W7) oder `var e = l[0]; …; l[0] = e;` | Rust ohne `IndexMut`; C# (`list[0].X = 9` auf einem Struct-Element ist CS1612 „cannot modify the return value … because it is not a variable") | **Bricht die Symmetrie zu `S[]`** — aber ehrlich: die Symmetrie ist heute schon gebrochen, nur still. Unter W1=B betrifft es ohnehin nur `mut struct`-Elemente |
| C | Status quo | — | Ein häufiges Muster in Collection-Code tut nichts |

**Empfehlung: B**, mit dem `with`-Idiom im Text der Diagnose (W32) — und A als spätere Ergänzung,
falls die Zählung in Benutzercode sie rechtfertigt. C#s CS1612 ist das direkte Vorbild: dieselbe
Sprache, dieselbe Bauform, dieselbe Antwort.

**Bruch: ungemessen** — im eigenen Korpus 0 (W42), in Benutzercode findet jede Ablehnung einen
Bug. **Uhr: fehlt** (`l[0].v` ist ein `MemberExpr` mit `IndexExpr`-Ziel, `:337` verlangt
`IdentifierExpr`). **Confidence:** gemessen (q19, n06), Ursache gelesen.

**Hängt ab von:** W7, W17, W19, W25, W26, Collections-Gebiet.

---

### W38 — Wie mutiert eine freie Funktion ein Struct des Aufrufers, und wer darf ein `mut fn` anfügen? *(neu)*

**Heute:** gar nicht — der Parameter kopiert (p14/r13). Die einzigen Wege: ein `mut fn` auf dem
Typ, eine Klasse, oder `s = f(s)`. **Und ein `mut fn` darf jeder anfügen:** `extend S { mut fn … }`
im selben Modul (q21) **und aus einem fremden Modul** (n09 gemessen: `import shapes { S };
extend S { mut fn bump() }` kompiliert, `s.v = 2` durch `let s`, keine Diagnose). Spec:
„method-only blocks are unrestricted" (`05-interfaces.md:141-142`, gelesen). W1's Warnkriterium
„eigenes Modul" und W24 sahen `extend` nicht.

**(a) Die schreibende Übergabe** — W31 C behandelte nur die lesende (`in`/`ref readonly`):

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Keine.** Ein Struct wird von außen nur durch ein `mut fn` seines Typs geändert, oder ersetzt (`s = f(s)`, `with`) | F#-Records, Scala `case class` (neuen Wert zurückgeben); Go (Pointer — nicht übertragbar) | „in place von außen" gibt es nicht; ein `mut fn` gehört zum Typ (oder zu einem `extend` — siehe (b)) |
| B | `inout`/`ref`/`&mut`-Parameter | Swift `inout`, C# `ref`, Rust `&mut` | Zweiter Mechanismus neben `class` (Rule 2); ohne Exklusivitätsregel entstehen Aliasse auf Locals, die W2/W35 heute nicht kennen |
| C | `mut`-Parameter (W4 B) | Rust `fn f(mut v: V)` | Löst es nicht — macht nur das Local veränderlich, der Aufrufer sieht nichts |

**Empfehlung: A**, protokolliert; B ausdrücklich abgelehnt (mit W31 C).

**(b) `extend` mit `mut fn` in einem fremden Modul:**

| Option | Beschreibung | Preis |
|---|---|---|
| a | Nur auf einem `mut struct` — folgt aus W5 B ohne neue Regel: „ein `mut fn` ist nur auf einem `mut struct` deklarierbar", ob im Typ oder im `extend` | Der Eigentümer entscheidet die Veränderlichkeit; ein Konsument kann sie nicht nachträglich einführen. **Bricht** n09-artigen Code |
| b | Frei wie heute | Ein Struct ist unveränderlich, bis irgendwer irgendwo ein `extend` schreibt — W1 wird unentscheidbar |

**Empfehlung: a.** Dazu W1's Kriterium: eine Warnung **am `extend`-Block** außerhalb des
deklarierenden Moduls, wenn er ein `mut fn` an ein Struct ohne `mut struct` fügt.

**Bruch: minor** (a; gemessen 0 im eigenen Korpus). **Uhr: fehlt** (n09 stumm). **Confidence:**
gemessen (q21, n09), Spec gelesen.

**Hängt ab von:** W1, W4, W5, W24, W31, Modul-Gebiet.

---

### W39 — Sind Schleifen-, `if let`-, Destrukturierungs- und `catch`-Bindungen unveränderliche Bindungen? *(neu)*

**Heute:** alle Bindungsformen binden eine **Kopie** und warnen `SEM0109` mit dem Text „'let' pins
the name" — für den es kein `var` gibt. Gemessen: `for (e in xs) { e.v = 9; }` (q06), `for (e in l)`
über `List<S>` (n01), `let (a, _) = t` (n01), `if (let (a, _) = t)` und `if (let c = o)` auf `?S`
(n10 — **die Narrowing-Bindung kopiert, `o!` aliast**), `match`-Bindung (r04). Ein `mut fn` auf der
Schleifenvariablen ist **stumm** und wirkungslos (n01). Eine Struct-`catch`-Bindung **gibt es
nicht** — `struct Oops :: [Throwable]` ist `IR0001` „only classes and interfaces are throwable"
(n07), also fällt `catch` aus der Frage. `let (d, _) = t else {…}` parst nicht (`PAR0002`, n10;
`Grammar.md:372` führt die Form) — nicht weiter gemessen. Spec §7.7:207 sagt nur, die
Schleifenvariable sei „assigned inside the body" (Definite Assignment, nicht Mutabilität).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Jede Bindungsform ist eine unveränderliche Bindung** (W21 verallgemeinert): Feldschreibung und `mut fn` darauf in 5.0 abgelehnt; die Diagnose nennt den Fix: „kopiere zuerst: `var e2 = e;`" | Swift (`for e in` bindet `let`; `for var e in` ist opt-in), Rust (`for e in xs` per Wert; `iter_mut()` für den Ort) | Nichts geht verloren — die Schreibung landet heute nirgends. **Der klassische stille Fehler** (Schleife, die nichts tut) wird ein Fehler |
| B | `for (var e in xs)` — opt-in veränderliche Schleifenvariable | Swift `for var` | Zweite Schreibweise für `var e2 = e;` in der ersten Zeile des Rumpfs |
| C | Die Schleifenvariable bindet den **Ort** (`xs[i]`) | Rust `iter_mut()`, C++ `for (auto& e : xs)` | Neuer Referenzbegriff für Locals; kollidiert mit W2/W35 |

**Empfehlung: A.** Dazu zwei Diagnostik-Punkte: (1) `SEM0109`'s Text an diesen Orten ist doppelt
falsch — „'let' pins the name" (es gibt kein `var`) und „5.0 settles what a struct is" (die Zeile
tut heute nichts) — eigener Text oder eigener Code (W32); (2) der `mut fn`-Aufruf auf der
Schleifenvariablen braucht W5's Uhr. Für W2 ist n10 ein weiterer Beleg: `if let` kopiert, `!`
teilt — zwei Antworten für eine Frage.

**Bruch: minor** (nur Code, der nichts tut — über die Uhr). **Uhr: läuft für Zuweisungen mit
falschem Text; fehlt für `mut fn`.** **Confidence:** gemessen (q06, n01, n07, n10, r04).

**Hängt ab von:** W2, W3, W5, W19, W21, Pattern-Gebiet, Diagnostik.

---

### W40 — Ist `p.x++` Teil der Sprache? *(neu)*

**Heute:** `s.v += 1` läuft (q07, mit `SEM0109` auf `let`); `s.v++` ist `LYR-IR0001`
„increment/decrement target (only parameters and locals) — this compiler version cannot lower it
yet" — auf einem `var`-Struct gemessen (q07b), und die Meldung nennt Klassenfelder als ebenfalls
ausgeschlossen. `SEM0109` feuert auf der nicht-lowerbaren Zeile mit (q07). Die Spec verspricht
für `xs[i] += 1` die einmalige Auswertung des Empfängers (`06-operators.md:102`, gelesen).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `++`/`--` werden auf jedem veränderlichen Ort lowerbar (Feld, Element), mit einmaliger Empfängerauswertung wie `+=` | C#, Go (`p.x++` als Statement) | Lowering-Arbeit; **keine** Spracherweiterung — `IR0001` sagt selbst „cannot lower it yet" |
| B | `++` nur auf Locals, festgeschrieben | — | Eine Regel, die nur eine Implementierungslücke adelt |
| C | `++`/`--` streichen | Swift (entfernt in 3.0), Rust (nie) | Rule 2 spricht dafür (`+= 1` reicht); bricht jede `i++`-Schleife |

**Empfehlung: A**, und die Regel: **`++` auf einem Feld zählt als Feldschreibung** im Sinne von
W3/W19/W32 — was `SEM0109` heute schon so hält. C ist die Rule-2-Antwort, aber ein 5.0-Bruch für
ein Detail; in eine spätere Runde.

**Bruch: nein** (additiv). **Uhr: n/a.** **Confidence:** gemessen (q07, q07b), Spec gelesen.

**Hängt ab von:** Operatoren-Gebiet, W3, W19, W32.

---

### W41 — Was tut `o?.mutate()` auf einem `?Struct`? *(neu)*

**Heute: zwei Antworten.** `b?.shift(9)` mit `void`-Rückgabe ist `LYR-IR0001` „'?.' with a call
that returns nothing" (q10); `b?.shiftRet(9)` mit `int`-Rückgabe **lowert und trifft den Alias**
(n05: `a!.x = 10`, `b!.x = 10`). `?.` ist also heute ein Schreibpfad — abhängig vom Rückgabetyp.
W30 kannte nur `!`.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | `?.` folgt W19's `!`-Zeile: die Kette setzt sich fort; auf einer `var` mit `mut struct` mutiert der eigene Wert (unter W2=C), auf einer unveränderlichen Wurzel abgelehnt | Swift (`a?.mutate()` mutiert in place auf einer `var`, ist auf einem `let` abgelehnt) | Nur, wenn W2's Spec-Satz `?.` neben `!` nennt |
| B | `mut fn` durch `?.` ablehnen | — | Verliert ein legitimes Idiom |
| C | Status quo (`void`-Lücke, Alias bei Rückgabe) | — | Das Verhalten hängt am Rückgabetyp |

**Empfehlung: A.** Die `void`-Lücke ist unabhängig davon eine IR-Lücke (Optionals-Gebiet).

**Bruch: minor** (dieselbe Klasse wie W2/W30). **Uhr: fehlt** (n05 stumm). **Confidence:**
gemessen (q10, n05).

**Hängt ab von:** W2, W5, W19, W30, Optionals-Gebiet.

---

### W42 — Wie groß ist der 5.0-Bruch von W1 in Zahlen? *(neu)*

**Gemessen (grep über die drei Korpora; Muster in `…/probes/werte-rev3/` reproduzierbar):**

| Korpus | Structs | mit `mut fn` | Feldschreibung von außen (nicht `this.`) | `Indexable`-Struct |
|---|---|---|---|---|
| **stdlib** (`stdlib/std/**/*.lyr`) | **8** — `build.Profile`, `core.Deprecated`, `core.NonExhaustive`, `encoding.DecodeAttempt`, `io.net.Packet`, `test.Test`, `time.Duration`, `time.Instant` | **0** — alle ~60 `mut fn` stehen in Klassen; `build.lyr:92 use(profile: Profile)` ist ein `mut fn` auf **`class Artifact`**, das `Profile` als Parameter nimmt *(der „Kandidat" der Kritik ist keiner)* | **0** — der einzige Treffer `build.lyr:185 artifact.output = …` ist ein Klassenfeld | **0** — nur `List<T>` (Klasse) |
| **Konformanzsuite** (`lyric-spec/conformance/cases`, 178 `.lyr`) | 47 Dateien | **0** | **3** Zeilen: die zwei `SEM0109`-Fälle selbst (`03-types/a_field_write_through_an_immutable_binding_warns.lyr:14`, `a_var_struct_binding_does_not_warn.lyr:11`) und ein abgelehnter (`06-operators/compound_on_field_is_refused.lyr:19`) | 0 |
| **Guide** (`docs/guide/*.md`) | 34 Deklarationen | **0** — die 4 `mut fn` (05:50, 13:266, 15:318, 15:322) stehen alle auf Klassen | **1** — `05-structs-and-classes.md:16` `b.x = 99;` auf `var b` (die Kopie-Demo) | 0 |
| **C#-Tests** | — | — | `MutabilityTests.cs:83-111` testet `let`/Parameter-Feldschreibung **absichtlich** | — |

**Das Muster ist grob** (Zeilenanfang `a.b = …`, `+=`, `-=`; erfasst keine `xs[0].v = …` und keine
Schreibungen in Ausdrucksposition). Für den eigenen Korpus reicht es: **unter W1=B ändert genau ein
grüner Konformanzfall seine Bedeutung, ein Guide-Snippet bricht, die stdlib bleibt unberührt.**

**Was nicht zählbar ist:** Benutzercode und Pakete. Für sie gilt die W1-Uhr.

| Folge | Vorher | Jetzt |
|---|---|---|
| W1 „Bruch: major" | geraten | **major als Regel, gemessen klein im Korpus** — bleibt major, weil eine Bedeutung wechselt |
| W25 „Bruch: minor" | geraten | **gemessen 0 Implementierer** in der stdlib |
| W17/W37 „Bruch: minor" | geraten | **ungemessen** außerhalb des Korpus; im Korpus 0 |
| W34 | — | gemessen 0 auf Structs im Korpus |

**Empfehlung:** die Zählung als Skript in den Plan aufnehmen und **vor** jeder Uhr wiederholen —
sie ist die einzige Zahl, die aus „major" eine Migrationsaufwandsschätzung macht.

**Bruch: nein** (Messung). **Confidence:** gemessen (grep), mit dem genannten Vorbehalt.

**Hängt ab von:** W1, W17, W25, W34, W37.

---

### W43 — Was passiert an der Paketgrenze (Build v2)? *(neu)*

**Heute, gelesen:** der `.lyrbc`-Header trägt `magic`, `version.major`, `version.minor` — **keine
Sprach- oder Spec-Version** (`13-bytecode.md:104-108`). Ein unbekanntes Major wird abgelehnt, ein
unbekanntes Minor toleriert (`:124`, `:125-135`). `mut struct` ändert §13 nicht (W28): `structcopy`
und das Struct-Layout bleiben. **Behauptet:** ein 4.x-`.lyrbc` einer Bibliothek, die intern Felder
ihrer `let`-Structs schreibt, läuft unter einer 5.0-VM unverändert — niemand hat das gemessen, weil
es keine 5.0-VM gibt; aus §13 folgt es. Das Paket-Design (Quelle + nativ + kompiliertes Lyric mit
Header; `design-round-2026-09-libraries`) kennt in `build-pakete.md` keine Spec-Version im Header
(gelesen: kein Treffer).

**Die Lücke:** ein 4.x-Paket exportiert `struct S` mit `mut fn bump()` — im 5.0-Header ohne das
Wort `mut struct`. Ein 5.0-Konsument unter W5 B darf `s.bump()` nicht rufen (kein `mut struct`),
und der Paket-Code mutiert intern Werte, die für den Konsumenten unveränderlich sind. **Ein
4.x-Paket ist ein `mut struct`-Produzent ohne das Wort.**

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | Der 5.0-Compiler liest ein 4.x-Header und behandelt jedes Struct mit `mut fn` als `mut struct` (dasselbe Kriterium wie `lyrfix`), mit **einer** Warnung beim Linken | Rust Editions (pro Crate, mischbar) | Braucht ein Sprachversionsfeld im Paket-Header, sonst weiß der Compiler nicht, was er liest; und das Kriterium hat W1's Lücke (Feldschreibung von außen ist nicht im Header) |
| B | **Kein Mischen über die Major-Grenze:** ein 5.0-Programm verlangt 5.0-Pakete; da Pakete Quelle enthalten, heißt das „neu kompilieren" — die Ablehnung nennt Paket und Version | Swift (Module-Stabilität nur innerhalb einer Major), Go (`go.mod`-Sprachversion) | Ein Sprachversionsfeld im Paket-Header (additiv); Paket-Autoren müssen einmal nachziehen |
| C | Nichts | — | Ein stiller `mut struct`-Konsument |

**Empfehlung: B**, mit dem Sprachversionsfeld als 4.7-Arbeit im Build-Gebiet. Ein Major ist der
eine Moment, in dem „neu kompilieren" zumutbar ist; A kauft Bequemlichkeit mit einer Heuristik, die
W1 selbst als unvollständig kennt.

**Bruch: nein** (additives Feld; die Ablehnung kommt mit 5.0). **Confidence:** Header gelesen;
das Laufzeitverhalten behauptet.

**Hängt ab von:** W1, W5, W28, Build-Gebiet, Bytecode-Gebiet.

---

### W44 — Ist die Identität eines unveränderlichen Struct-Werts je beobachtbar? *(neu)*

W2=C erlaubt „die Implementierung darf teilen". Das gilt nur, solange **nichts** in der Sprache,
der stdlib, dem Debugger oder der Reflexion „geteilt" von „kopiert" unterscheidet.

**Heute:** kein Identitätsvergleich auf Structs — `==` braucht `Equatable` (p13); ein
Referenzvergleich-Operator ist für Structs nicht gefunden (**behauptet**, nicht erschöpfend
geprüft). `opaque type`/`TypeTag.Host` sind Host-Handles, keine Structs (Memory-Notiz, nicht
gemessen). Reflexion (`E.variants()` u. ä.) ist für Enums geplant, nicht gebaut (behauptet). Der
DAP-Debugger existiert (`tests/Lyric.Tests.Dap`); ob er Adressen zeigt: **behauptet: nein**, nicht
geprüft. LSP-Hover: zeigt Typen, keine Orte (behauptet).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Spec-Satz:** *„Ein unveränderlicher Strukturwert hat keine Identität. Keine Operation der Sprache, der Standardbibliothek, des Debug-Protokolls oder einer künftigen Reflexion unterscheidet einen geteilten von einem kopierten unveränderlichen Wert."* Debugger und Hover zeigen bei Structs **Werte**, bei Klassen **Objekte** (mit Identität) | Swift (Werttypen haben keine Identität; `===` gibt es nur für Klassen), Rust (`ptr::eq` ist bewusst auf Referenzen) | Bindet künftige Werkzeuge: kein Adress-Display für Structs, kein Identitätshash in `Hashable` — beides billig, weil heute nicht vorhanden |
| B | Identität zulassen (z. B. Referenzgleichheit für Debugging) | — | Dann ist Teilen beobachtbar und W2=C muss „kopiert" heißen — die tiefe Kopie kommt zurück |

**Empfehlung: A**, als Bedingung von W2=C aufgeschrieben. **Werkzeug-Konsequenz:** der Debugger
(DAP) zeigt an einer Struct-Bindung den Wert, an einer Klassen-Bindung eine Objekt-Id; der Hover
zeigt `S` gegen `C` — mehr nicht, und das reicht, weil die Sprache selbst nichts weiter verspricht.

**Bruch: nein.** **Confidence:** behauptet (keine Identitätsoperation gefunden; nicht erschöpfend).

**Hängt ab von:** W2, W11, W46, Werkzeuge-, Metaprogrammierungs-Gebiet.

---

### W45 — Was wählt `lyrfix` als Default, wenn es eine W19-Kette nicht entscheiden kann? *(neu)*

W33 sagte „nicht mechanisch fixbar" — ein Werkzeug braucht trotzdem eine Regel pro Fall.
**Das Werkzeug existiert nicht** (`plan_at_dossier.md:319`); alles hier ist **behauptet** im Sinne
eines Vorschlags.

**Der Fall:** `let o = O{…}; o.i.x = 9;` — heute landet die Schreibung in `o`. Unter W1=B/W3=C ist
sie ein Fehler. Zwei Fixes: (i) `let` → `var` **und** `struct O` → `mut struct O` (erhält das
Verhalten, braucht den Typ im selben Build); (ii) `var tmp = o; tmp.i.x = 9;` (kompiliert, **ändert
das Verhalten** — die Schreibung landet nicht mehr in `o`).

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Invariante: `lyrfix` ändert nie das Verhalten.** Ist der Typ im Build: Fix (i), beide Edits zusammen. Sonst: kein Edit, ein Marker-Kommentar in festem Format (`// lyrfix: W19 — 'o.i.x' writes through an immutable root; the type 'O' is not in this build`) plus Diagnose-Liste | `cargo fix` (nur `MachineApplicable`-Vorschläge), `rustfix` | Manche Programme bleiben rot; dafür ist jeder grüne Fix korrekt |
| B | Immer (ii) — kompilierbar machen, Absicht raten | — | Stille Verhaltensänderung: der Fehler, den dieses Projekt seit drei Sweeps schließt |
| C | Nie Ketten anfassen, nur markieren | — | Verschenkt Fix (i), der sicher ist |

**Empfehlung: A.** Zwei Regeln für den Plan: **jede** 5.0-Ablehnung bekommt ein Feld „lyrfix:
mechanisch / bedingt / nie", und die Uhrenwarnungen tragen dieselbe Klassifikation im Text.

**Bruch: nein.** **Confidence:** behauptet (Werkzeug nicht vorhanden).

**Hängt ab von:** W1, W3, W19, W33, W42, Werkzeuge-Gebiet.

---

### W46 — Ein `mut struct` als `Map`-Schlüssel oder `Set`-Element *(neu)*

W10 stellte die Frage nur für Klassen. Für ein `mut struct` lautet sie: ist der Schlüssel eine
Kopie, oder — über die `?Struct`-/`[x] * n`-Aliasse — ein geteilter Wert, dessen Hash sich nach dem
Einfügen ändern kann?

**Heute: eine Kopie, gemessen (n02).** `var k: ?K = K{v=1}; var alias = k; m.set(k!, 100);
alias!.v = 2;` → `m.get(K{v=1}) = 100`, `m.get(K{v=2}) = null`; ebenso über `[K{v=5}] * 2`. Der
Schlüssel wird beim Einfügen kopiert, weil das **Argument** kopiert (§13:884-886 „argument";
`Map.set(key: K, …)` nimmt ihn als Parameter, `collections.lyr:633`). Der Alias erreicht die Map
nicht.

| Option | Beschreibung | Vorbild | Preis |
|---|---|---|---|
| A | **Festschreiben:** *„Eine Collection hält Kopien ihrer Strukturwerte; die Argumentkopie ist die Garantie."* Nichts zu bauen | Swift (`Dictionary` kopiert Werttyp-Schlüssel), Rust (Move in die Map) | Keiner. Die Gefahr bleibt bei **Klassen**-Schlüsseln — W10 |
| B | `mut struct` als `Hashable` verbieten | — | Verbietet, was gemessen sicher ist |

**Empfehlung: A.** Ein Satz in §11 (stdlib contract) neben W22's Satz — dieselbe Regel, ein
Ort mehr.

**Bruch: nein.** **Confidence:** gemessen (n02), Spec gelesen.

**Hängt ab von:** W2, W10, W22, W36, Collections-Gebiet.

---

## 4. Was wir übernehmen sollten

| Woher | Was | Wohin |
|---|---|---|
| **F#-Records / Scala-`case class` / Kotlin-`data class`** | Unveränderlich per Default für Werttypen — Aussage über Typformen | W1 |
| **F#** | `with` als Kopie-mit-Änderung | W7 |
| **Swift** | `let` greift bei Werttypen rekursiv durch — mit dem Vorbehalt der Exklusivitätsregel, die Lyric nicht übernimmt | W3, W19 |
| **Swift** | Ein Parameter ist ein `let` | W4 |
| **Swift** | `mutating` nur auf einem veränderlichen Ort — **das einzige statische Vorbild** *(F# gestrichen, gemessen)* | W5, W17 |
| **Swift** | **`self` ist in einer nicht-`mutating` Methode `let`** — Transitivität | **W34** |
| **C#** | **`this` in einer Struct-Methode ist `ref this`** — Referenz, kein Copy-in/Copy-out | **W35** |
| **C#** | CS8656 (defensive Kopie + Warnung) und CS1612 (`list[0].X = 9` abgelehnt) — **als Gegenmodell und als Vorbild**: nicht still kopieren, sondern ablehnen | W34, **W37** |
| **Swift / Rust** | `Array(repeating:)` kopiert / `[x; n]` verlangt `Copy` — die Repetition kopiert | **W36** |
| **Swift** | Synthese folgt der deklarierten Konformanz | W11 |
| **Swift** | Rekursive Werttypen brauchen einen benannten Indirektionspunkt | W14, W23 |
| **Swift** | Werttypen haben keine Identität; `===` nur für Klassen | **W44** |
| **Swift / Kotlin** | `let`/`var` als Feldmodifikator auf Klassen | W10 |
| **Swift / F#** | „Unveränderlich" heißt flach — im Text | W16, W24 |
| **C#** | `with` als Primärausdruck mit Sichtbarkeit | W7, W29 |
| **C#** | `static readonly` eines Struct-Typs verweigert die Feldschreibung (CS1650) | W20 B |
| **C#** | Als Warnung: die gespiegelte Zweiteilung der Auswertungsreihenfolge | W27 |
| **C#** | 16-Byte-Richtlinie | W31 |
| **Rust** | Struct-Ausdrücke in Schreibreihenfolge — spezifiziert | W27 A |
| **Rust / Swift** | Ein Diagnosecode pro Regel | W32 |
| **`cargo fix`** | Nur maschinell sichere Vorschläge anwenden | **W45** |
| **Go** | `==` nur, wenn alle Felder vergleichbar — als Option A′ | W11 |
| **C** | Als Gegenposition: „indeterminately sequenced" | W27 C |

**Der Maßstab für W2 und W36 — als eigene Forderung:**

> **An jeder Stelle im Programmtext ist aus dem Typ allein ablesbar, ob eine Bindung den Wert
> kopiert oder ihn teilt.**

Lyric 4 verletzt ihn an **zwei** Stellen *(korrigiert gegen beide Vorfassungen)*: `?S` (Befund 2)
und `S[]` aus `[x] * n` (Befund 14) — an beiden verspricht der Typ einen Wert und die Bindung
liefert einen Alias. **Nicht** verletzt ist er bei `struct A { xs: int[] }`: der Feldtyp `int[]`
sagt „Referenz", und §3 definiert Arrays so. W2 und W36 schließen beide Stellen; W16 schuldet nur
den Satz „flach".

**Nicht übernehmen, mit Begründung:**

- **F# `[<Struct>]`-Member als Vorbild** — gemessen kopiert F# still (`fsharp_check.fsx`).
- **Rusts `Copy`/`Clone`** (W16 D) — Moves, Ownership; `CONTRIBUTING.md:33`.
- **Swifts `borrowing`/`consuming`/`~Copyable`** — derselbe Grund.
- **Swifts Exklusivitätsregel** — Lyric übernimmt W3/W5/W34 ohne sie; **W35 protokolliert die
  Folge** (Reentranz sichtbar, wie bei einer Klasse).
- **Swift `inout` / C# `ref` / Rust `&mut`** (W38 B, W31 C) — zweiter Mechanismus neben `class`.
- **Swift `@frozen`** — ABI-Layout, keine Kopie-Aussage; gestrichen aus W28.
- **Java `static final`** — pinnt nur die Referenz; gestrichen aus W20.
- **`mut`-Parameter** (W4 B), **Capture-Listen** (W15 B), **Gos Pointer** (W9 D), **„Alles
  Referenz"** (W9 C) — wie zuvor.
- **Swifts Subscript-Writeback** (W37 A) — vorerst nicht: Accessor-Synthese wäre ein zweiter
  Mechanismus neben `with`; erst, wenn die Zählung in Benutzercode ihn rechtfertigt.

---

## 5. Konflikte

### Mit `CONTRIBUTING.md` Rule 2

| Vorschlag | Lage |
|---|---|
| W1 (`mut struct`) | Kein Konflikt — ersetzt einen Mechanismus |
| W7 (`with`) | Grenzfall ohne W1, kein Konflikt mit W1 — und **unter W37 B das einzige Idiom** für Struct-Elemente in Klassencontainern; stärkster Grund, beide zusammen zu entscheiden |
| W10 (`let`-Feld) | Kein Konflikt |
| W8 B | Konflikt mit `SEM0097` |
| W4 B (`mut`-Parameter), **W38 B (`inout`)**, W31 C | Konflikt — zweiter Mechanismus; abgelehnt |
| W16 D | Konflikt mit „GC only" |
| W6 B | Spec-Bruch (§7.1), eigene Runde |
| **W34 C (`mut` inferiert)** | Konflikt — zwei Mechanismen (Wort und Inferenz) und keine Signatur für `SEM0042`; abgelehnt |
| **W37 A (Writeback-Synthese)** | Konflikt — Accessor-Synthese neben `with`; zurückgestellt |
| **W39 B (`for var`)** | Konflikt — zweite Schreibweise für `var e2 = e;`; abgelehnt |
| **W40 C (`++` streichen)** | Rule 2 spricht **dafür**; als Bruch für ein Detail zurückgestellt |
| W24, W32 | Kein Konflikt; Pflichtteile |

### Mit anderen Gebieten

| Gebiet | Berührung |
|---|---|
| **Sichtbarkeit** | W1/W10 ohne Invariante, solange Felder öffentlich; W29 |
| **Konformanz-Synthese** | W11; W10; W16 |
| **Optionals** | W2, W14, W30, **W41** (`?.`-Lücke für `void` ist eine IR-Lücke dort); n10: `if let` kopiert, `!` teilt |
| **Pattern Matching** | W8/`SEM0097`; W13; W21; **W39** (alle Bindungsformen) |
| **Interfaces** | W6/`SEM0042`; W5; W25; **W38** (`extend` mit `mut fn`, §5.5) |
| **Module** | **W38** (`extend` aus fremdem Modul), W8 Modul-Scope |
| **Generics** | W25 |
| **Collections** | W25, W26, **W37** (`Indexable.get` als Temporary — betrifft `List`, `Deque`, `Map`-Werte), **W46** (Schlüsselkopie), W22 |
| **Operatoren** | **W40** (`++` auf Feldern), W29 |
| **`throws`** | W12; **W35** (Teilschreibung vor `throw` bleibt — festzuschreiben) |
| **Diagnostik** | W32; W21/W39 Texte; die elf Uhren (W33); `SEM0034`-Text; **`SEM0075` auf dem Aufrufpfad** (W3/W34) |
| **Optimierer/VM** | W1, W4, W31 — eine fehlende Messung; **W35** (Referenz-`this` ist die Voraussetzung dafür, dass `mut fn` keinen `structcopy` kostet) |
| **Bytecode/Format** | W28; W23 (§13:195-196 **und** :881-882); **W36** (`[x] * n` in die Emissionsliste); **W43** (kein Sprachversionsfeld im Header) |
| **Build/Pakete** | **W43** — Sprachversionsfeld, keine Mischung über die Major-Grenze |
| **FFI / Embedding** | W28 |
| **Werkzeuge** | `lyrfix` (**W45**: Invariante „ändert nie das Verhalten"); Debugger/LSP (**W44**: Wert vs. Objekt) |
| **Metaprogrammierung** | **W44** — Reflexion darf Struct-Identität nicht sichtbar machen |

### Innerer Widerspruch der Vorlage

`plan_at_dossier.md:321-324` und `03-types.md:115-118` sagen, unter `mut struct` werde „shared"
von „copied" ununterscheidbar und `?Struct` höre auf, eine dritte Frage zu sein. Für ein
`mut struct` stimmt das nicht — dort bleibt die Lücke (W2), und dazu `[x] * n` (W36) und `?.`
(W41). Der Satz gehört bei der 5.0-Arbeit präzisiert.

---

## 6. Nach der Kritik geändert

**Kritikpunkte geprüft und übernommen (alle selbst nachgemessen):**

- **`mut` ist nicht transitiv** (q02a, q16, q17; Kontrolle q02b). §1.2's Satz vom „einzigen Ort"
  korrigiert; Befund 12; **W34 neu**; W5 („nicht implementierbar vor W34"), W6 („Uhr nur für
  direkte Zuweisungen", statt „nichts mehr zu bauen"), W19 (Zeile `this`), W33 (Status SEM0108)
  korrigiert. **Das ist der schwerste Fehler der zweiten Fassung.**
- **`this` ist eine Referenz** (q18, n03). Guide 05:61 „written back" als gemessen falsch markiert;
  Befund 13; **W35 neu**; Reentranz protokolliert.
- **W22's Argument** war falsch gemessen (Ziel- statt Quellseite); q04 zeigt: Tupel- und
  Nutzlast-Konstruktion kopieren, beobachtbar. Empfehlung A bleibt mit der Begründung „bereits so".
- **W23** nennt jetzt beide Spec-Stellen (`13:195-196` Regel, `:881-882` Begründung) mit
  Formulierungsvorschlag.
- **`SEM0075`** nur für direkte Schreibungen entwarnt (q02a); n11 zeigt, dass der Hinweis mit der
  Sema konsistent ist — der Fehler ist die Transitivitätslücke, W34 schließt ihn mit.
- **W2 „einzige ohne Uhr"** auf „die einzige der vier §3.4a-Fragen" präzisiert.
- **W9 B** mit Fundstellen (`guide/15:20`, `14:407-408`) und der Ausnahme `SEM0076`.
- **`Indexable.get` ist ein Temporary** (q19, n06): **W37 neu**; W17's Bruch „ungemessen"; W19
  trennt `T[]`-Element von `Indexable`-Index.
- **`[x] * n` aliast** (q03, n04; Kontrollen q03c, `[1]*3`): Befund 14, **W36 neu**, W24-Zeile.
- **F# `[<Struct>]`** gemessen (`fsharp_check.fsx`, F# 10): kopiert still. Aus W5, §2, §4, §6
  gestrichen; Swift bleibt das einzige Vorbild, C# CS8656 als Gegenmodell.
- **Swift für W19** stimmt nicht bei Array (Werttyp) und Interface-Box; Tabelle „kein Vorbild
  exakt", C# für die Box. Elf Zeilen statt „sieben"/neun.
- **C# in W27** ist gespiegelt (Feldinitialisierer zuerst, Default läuft trotzdem); **Rust** gehört
  zu A (Reference: Schreibreihenfolge); C bekommt **C** (C11 §6.7.9p23) als Vorbild.
- **Swift `@frozen`** (W28), **Java `static final`** (W20; ersetzt durch C# CS1650), **Swifts
  Exklusivitätsregel** (§2: statisch für Locals/`inout` auf Locals, dynamisch für Klassen-Properties,
  Globale, Escaping-Captures) korrigiert.
- **W1's Warnkriterium** um `extend` aus fremden Modulen ergänzt (n09 gemessen).

**Kritikpunkte geprüft und zurückgewiesen oder eingeschränkt:**

- **„Lyric 4 verletzt den Maßstab an DREI Stellen"** — nein, an **zwei**. `struct A { xs: int[] }`
  verletzt ihn nicht: der Maßstab verlangt Ablesbarkeit *aus dem Typ*, und `int[]` sagt „Referenz"
  (§3). Die Kritik hat hier recht, dass W16 A′'s Hinweis `Buffer` trifft und nicht abschaltbar ist
  — aber die Konsequenz ist, den Hinweis zu **streichen** (zurück zu A), nicht ihn zu retten.
- **„Ein grober grep findet einen Kandidaten (`std.build.Profile`)"** — falsch: `build.lyr:92
  use(profile: Profile)` ist ein `mut fn` auf **`class Artifact`** mit `Profile` als Parameter.
  Gemessen: **0** stdlib-Structs mit `mut fn`. Die Zählung steht in W42.
- **„Struct-`catch`-Bindung"** als Teil der Bindungsfrage — gibt es nicht: ein Struct ist nicht
  throwable (n07, `IR0001`). Aus W39 herausgenommen, als Messung vermerkt.
- **W9 B „unbelegt"** — teilweise: „describes; does nothing" steht wörtlich im Guide (15:20). Was
  fehlte, war die Fundstelle und die Ausnahme `SEM0076`; beides ergänzt, die Option und ihr Preis
  bleiben.

**Neue Designfragen (dreizehn):** W34 (Transitivität), W35 (`this` als Referenz), W36 (`[x] * n`),
W37 (`Indexable`-Klassencontainer), W38 (freie Funktion / `extend` mit `mut fn`), W39
(Bindungsformen), W40 (`p.x++`), W41 (`?.`), W42 (Bruch in Zahlen), W43 (Paketgrenze), W44
(Identität), W45 (`lyrfix`-Default), W46 (`mut struct` als Schlüssel).

**Neue eigene Befunde, die die Kritik nicht hatte:**

- `if (let c = o)` auf `?S` **kopiert**, `o!` **aliast** (n10) — W2 hat zwei Antworten für eine
  Frage.
- `b?.shiftRet(9)` mit Rückgabewert **lowert und trifft den Alias** (n05), nur die `void`-Form ist
  `IR0001` — `?.` ist heute ein Schreibpfad, abhängig vom Rückgabetyp (W41).
- Ein Map-Schlüssel wird beim Einfügen **kopiert** — auch durch `?K`- und `[x]*n`-Aliasse (n02);
  W46 ist gemessen unproblematisch.
- Feldweise Erstinitialisierung ist `SEM0018` (q22, Kontrolle n08) — Befund 16.
- `SEM0075` zählt einen direkten `mut fn`-Aufruf als Mutation (n11, Erwartung vorher notiert).
- Der Bruch in Zahlen: stdlib 8/0/0, Suite 178/47/0/1, Guide 4 `mut fn` (alle Klassen)/1 Snippet.

**Empfehlungen geändert:** W16 A′ → A (mit korrigiertem Maßstab); W17 Bruch „minor" → „ungemessen";
W19 Vorbildspalte; W20 B Vorbild; W27 Vorbilder; W28 B Vorbild; W33 Reihenfolge beginnt mit **W34
und W2** und zählt elf statt sieben Uhren; W1 „major" jetzt mit gemessener Korpusgröße; W25 „minor"
jetzt mit gemessen 0 Implementierern.
