# Lyric 5 — Gebiet: Funktionen, Lambdas, Aufrufkonventionen

Stand 2026-09-27, **Fassung 3 nach der zweiten adversarischen Kritik**. Gemessen gegen den Baum im
Haupt-Checkout (Toolchain 4.6.0, Format 4.0) mit den vorgebauten Debug-Binaries; Zeitmessungen mit
Release-Bytecode.

Proben:
- Runde 1: `…/scratchpad/v5-design/probes/funktionen/p00…p76` (+ `funktionen-review/`)
- Runde 2: `…/probes/funktionen-rev2/q01…q29` (Erwartungen in `ERWARTUNGEN.txt`)
- Kritik-Audit (Kritiker): `…/probes/funktionen-audit/` (`a…`, `n…`, `c_…`, `t_…`)
- Runde 3 (diese Fassung): `…/probes/funktionen-rev3/r01…r22, t_base/t_direct/t_ind/t_virt`,
  Erwartungen vorab in `funktionen-rev3/ERWARTUNGEN.txt`. Jede Zahl aus dem Audit, die diese
  Fassung übernimmt, ist in Runde 3 **selbst nachgemessen** (die `c_`-Dateien des Audits wurden
  dafür unverändert neu gebaut und gelaufen).

Jede Aussage über Lyric 4 ist markiert mit **gemessen** (Probe genannt), **gelesen** (Pfad:Zeile)
oder **behauptet**. Aussagen über Vergleichssprachen sind Sprachwissen ohne Probe; die in Fassung 2
falschen stehen korrigiert in §6.

---

## 0. Die Befunde, die vor jeder Designfrage stehen

| # | Befund | Art | Beleg |
|---|---|---|---|
| 1 | **Vier rahmengebundene Konstrukte im Lambda, vier verschiedene Antworten.** `this` → `CLI0020: lowering: 'this' reached lowering outside an instance method`; impliziter Feldzugriff → **andere** ICE `CLI0020: ir: type not lowerable`; `break`/`continue` → `CLI0020: lowering: 'break' outside a loop`; `yield` → **keine Diagnose**, `VM0013` zur Laufzeit. Korrekt nur `return` (lambda-lokal). | Designlücke (→ FN24) | gemessen q09, q09b, q10, q10b, q11/q11b; Kontrollen q09c, q15 |
| 2 | **Weggelassener Default an einem Interface-Aufruf erzeugt ein kaputtes Modul.** Debug: Reader („CallVirt needs 3 value(s) but the stack holds 2"), Release: Verifier. Direktaufruf, Static und Interface-Aufruf **mit** Argument laufen. | stille Fehlkompilierung | gemessen p57, p61; Kontrolle r02e (auch mit Impl-Default: CLI0020) |
| 3 | **`lyrc check` ohne `--emit` sagt zu Befund 2 „ok"; `check --emit` fängt ihn.** Gemessen r03d (`check c_p61` → ok, exit 0) und r03e (`check c_p61 --emit` → CLI0020, exit 1). `--emit` existiert genau dafür (gelesen `src/Lyrc/Program.cs:245–251`: „A program can type-check and lower in silence and still produce a module the loader refuses … `--emit` writes nothing"). **Korrektur an Fassung 2:** die Sema-ICEs aus Befund 1 sieht `check` sehr wohl (r03/r03b: `check c_q09`/`c_q10` → CLI0020, exit 1), weil `check` das Lowering einschließt (gelesen `src/Lyric.Frontend/Compiler/SourceCompiler.cs:28–32`) und der LSP genau `SourceCompiler.Check` ruft (gelesen `src/Lyric.Lsp/Analysis/AnalysisService.cs:553`). Unsichtbar für `check` bleiben nur: Emit/Reader-Defekte ohne `--emit` (Befund 2) und die Laufzeitpanik bei `yield` (r03c: `check c_q11b` → ok). | Diagnostik-Loch, kleiner als behauptet | gemessen r03–r03e |
| 4 | **`it`: die Doku regelt die innere Lokale richtig und schweigt zur äußeren.** Innere Lokale `it` verdeckt das implizite `it` (q01 → 6, so sagt es `design/lambdas.md:109–110`); eine **äußere** Lokale `it` wird verdeckt und bekommt `SEM0071` (q02 → 3 + Warnung). | Doku unvollständig | gemessen q01, q02 |
| 5 | **Der Guide kennt drei ausgelieferte Formen nicht — die Beispiele schon.** Bare Lambda, Trailing-Lambda, `it` stehen in `docs/Grammar.md:511–513`, laufen (p11, q18, r22) und kommen in keinem Guide-Kapitel vor (`grep -rn "trailing lambda\|bare lambda\|{ it " docs/guide/*.md` → leer; `docs/guide/03-functions.md:144–160` zeigt nur `(n: int) => …`). **Korrektur an Fassung 2:** `examples/lambdas/shorthand.lyr:1–6` erklärt alle drei Formen im Kopfkommentar und zeigt sie (`:44` bare, `:47` trailing, `:64` Kette); `lyrfmt` lässt `x => x * 3` und `run { it + 7 }` unverändert (gemessen r22). Die Lücke ist ein fehlendes **Kapitel**, nicht fehlende Lehre überhaupt. | Prozess **und** Designfrage (→ FN32) | gelesen, gemessen r22 |
| 6 | **`params` × Generics: zwei getrennte Löcher.** Deklaration kompiliert (q03); `count(1,2,3)` ist `SEM0060` + 3× `SEM0001` (Inferenz, q04); `count<int>(1,2,3)` ist `IR0001` (Lowering, q05). | zwei Implementierungsgrenzen | gemessen q03–q05 |
| 7 | **Auf der Iterator-Kette überleben Interface-Dispatch UND indirekter Ruf — pro Element je einer.** **Korrektur an Fassung 2:** „4 `callvirt` + 2 `callind`" (q18) ist die Summe über **zwei** Ketten und zwei `sum`-Aufrufe in derselben Probe. Die saubere Zahl: eine Kette `over(xs).map { it * 2 }` + ein `sum`, Release → `callvirt=2 callind=2 mkclosure=1 newobj=2` in `main.main`, die Schleife einmal geschält; pro Element **1 `callvirt`** (`loadfield t9, ty22#0` → `callvirt ty0#0`) **+ 1 `callind`** (`loadfield t9, ty22#1` → `callind`). Ursache unverändert: das Adapterobjekt bleibt als Interface-Wert von der Skalar-Ersetzung ausgeschlossen. | gemessen r06 (q18 reproduziert 4/2/3/5; a11 2/2/1/2) |
| 8 | **`defer { return 3; }` bringt den COMPILER mit einem CLR-Stack-Overflow um** — `lyrc build` **und** `lyrc check`, exit 127, keine `CLI0020`, kein Code, keine Position. `defer { return; }` (void) und `defer { break; }` sind `CLI0020: lowering: block bb3 is already sealed`. Kontrolle r11h (`defer { println }`) läuft. Ein Nutzerfehler (Steuerfluss aus einem `defer`) endet als Prozesstod; §B von `PLAN.md` gilt als leer (gelesen `STATUS.md:1690–1696`), ist es aber nicht. | **Prozessabbruch**, neu (→ FN39) | gemessen r11b (zweimal, Stack in `TypeTable.Lower`/`LowerReturn`), r11g, r11c |
| 9 | **Unter `--jit` wird ein direkter Ruf 4–5× schneller, ein `callind` gar nicht.** 3 M Aufrufe, Release: `t_direct` 1.83–1.92 s interpretiert / **0.41 s** `--jit`; `t_ind` 2.22–2.45 s / **2.22 s** `--jit`; `t_virt` (im Release devirtualisiert, 0 `callvirt` in `main`) 1.85–1.90 s / 1.88 s `--jit`. Der JIT lehnt Closures ab (gelesen `src/Lyric.Vm/Jit/JitCompiler.cs:35–36`: „Still declined: closures, exceptions, enums, recursion, and the narrow integer widths"); warum `t_virt` nichts gewinnt, ist **behauptet** (vermutlich die Interface-Wert-Konstruktion), nicht durchverfolgt. Kein FN der Fassung 2 kannte den JIT. | Leistungsbefund (→ FN42, FN43) | gemessen r14 (je 3 Läufe), Startup 0.27 s, Basisschleife ohne Ruf 1.41–1.44 s |

**Befunde 1–3 und 8 sind Fehler, keine v5-Fragen** — mit der Einschränkung, dass Befund 1 und 8
zuerst eine **Regel** brauchen (FN24, FN39), sonst repariert man Symptome und lässt `yield` still.

---

## 1. Ist-Stand

### 1.1 Was die Signatur kann

```ebnf
FunctionDecl = [ 'pub' ] [ 'static' ] [ 'mut' ] 'fn' IDENTIFIER [ GenericParams ]
               '(' [ ParamList ] ')' [ ':' TypeExpr ] [ 'throws' [ TypeExpr ] ] ( Block | ';' ) .
Param        = [ 'params' ] IDENTIFIER ':' TypeExpr [ '=' Expr ] .
```
— gelesen `docs/Grammar.md:213–221`; „`params` may appear on the last parameter only, whose type
must be an array" gelesen `docs/Grammar.md:224–225`.

| Eigenschaft | Verhalten | Beleg |
|---|---|---|
| Default-Argument | erlaubt an jedem Parameter; ein Pflichtparameter **nach** einem Default ist `LYR-SEM0025` — **plus zwei Folgefehler `SEM0045`**, wenn ein Trailing-Lambda den Kontext verliert | gemessen p15, r09 (`SEM0025 SEM0045 SEM0045`) |
| Default-Auswertung | **an der Aufrufstelle, bei jedem Aufruf, nur für weggelassene Argumente** — `f()` mit `a = side("a"), b = side("b")` druckt `a b`; `f(1)` druckt `b`; `f(side("x"))` druckt `x b`. Reihenfolge: **Argumente links nach rechts, dann fehlende Defaults in Deklarationsreihenfolge** — nirgends spezifiziert (→ FN44) | gemessen r16, p16 + `lyrc lower` |
| Default-Ausdruck | beliebiger Aufruf; **Bezug auf früheren Parameter ist `LYR-IR0001`** mit falscher Meldung | gemessen q27, p16 |
| Default darf ein Lambda sein | ja: `fn apply(v: int, f: fn(int)->int = (n: int) => n + 1)` → 42 | gemessen q14 |
| Default über Modulgrenze | trägt, **im selben Compilerlauf** | gemessen `modtest/`; gelesen `docs/guide/12-modules.md:74` |
| Default an Interface-Member | Grammatik erlaubt es; die **Konformanzprüfung ignoriert Defaults vollständig**: Default nur im Interface (r02b), nur in der Implementierung (r02c), **widersprüchliche** Defaults „Hi"/„Yo" (r02d) — alles kompiliert; der Direktaufruf `s.greet("B")` nimmt den Default des **statischen Typs** (r02c → `HiB`, r02d → `YoB`). Nur der Aufruf **über den Interface-Wert** mit weggelassenem Default ist Befund 2 (r02e) | gemessen r02b–r02e (→ FN04, FN36) |
| `params` mit 0 Argumenten | leeres Array | gemessen p17 |
| `params` mit fertigem Array | **übergibt dasselbe Array**, Callee-Schreiben ist beim Aufrufer sichtbar | gemessen p76 |
| `params` im Baum | **stdlib: 0**, `stdlib-tests/`: 0, `examples/`: **1** (`examples/stats.lyr:8`, `fn sum(params nums: int[])`, gerufen `sum(3, 7, 1, 9, 4)` — kein Array-Durchreichen), `tests/`: 1 Parser-Golden (`fn_variadic.lyr`) | gemessen `grep -rn "params "` (→ FN05, FN23, FN46) |
| `params` **nach** einem Default | erlaubt, aber unerreichbar: `f()`→100, `f(5)`→500, `f(5,6)`→501, `f(5,6,7)`→502 | gemessen q13 (→ FN23) |
| **Default + Trailing-Lambda** | **möglich, wenn der `fn`-Parameter VOR den Defaults steht.** `fn each(f: fn(int)->int, n: int = 2)`: `each { it + 1 }` → 3, `each((k: int) => k + 1)` → 3, `each((k: int) => k + 1, 5)` → 6. **Das Trailing-Lambda bindet an den nächsten unbesetzten Parameter**, nicht an den letzten: `each(5) { it + 1 }` ist `SEM0001` „cannot assign 'int' to 'fn(int) -> int'" + 2× `SEM0045` (r01b). Die umgekehrte Reihenfolge `fn each(n: int = 2, f: …)` ist `SEM0025` (r01c = q12). **Korrektur an Fassung 2**, die die Kombination für unschreibbar erklärt hatte | gemessen r01, r01b, r01c (→ FN22, FN35) |
| `params` generisch | zwei Löcher, Befund 6 | gemessen q03–q05 |
| benannte Argumente | existieren nicht; `connect(host: "h", port: 80)` zerfällt in **19 Diagnosen mit 7 verschiedenen Codes** (`SEM0014 SEM0002 PAR0002 PAR0008 PAR0008 PAR0016 PAR0016 SEM0022 PAR0002 PAR0016 PAR0016 PAR0046 SEM0022 …`). **Korrektur an Fassung 2:** „sieben Folgefehler" war die Zahl der Codes | gemessen r05 |
| `f(port = 8080)` | kompiliert nur bei sichtbarer veränderlicher Lokale `port` (p42); sonst `SEM0002` (q06) | gemessen p42, q06 |
| Zuweisung ist ein **Ausdruck** | `let y = (x = 5);` druckt 5 | gemessen q07 (→ FN26) |
| Argument-Auswertungsreihenfolge | links nach rechts | gemessen p36 |
| Struct-Argument | kopiert; Schreiben im Callee trägt `SEM0109` (§12.5-Uhr) | gemessen q26 |
| `main` | genau `fn main(): int` / `fn main(args: string[]): int`; `void`/`throws` → `SEM0021` | gemessen p53–p55 |
| unbenutzter Rückgabewert | still verworfen | gemessen p69 |
| Feld vom Funktionstyp | `h.f(1)` ruft das Feld (→ 2); `let g = h.f; g(10)` → 11 — **der Feldwert ist ein gewöhnlicher Funktionswert ohne `this`**. Feld **und** Methode `f` im selben Typ: `RES0001: 'f' is already declared in this type — only two FUNCTIONS may share a name` — **die Namensraum-Regel ist als Diagnose vorhanden** | gemessen r17, r17b (→ FN12, FN45) |

### 1.2 Überladung (v3.0)

Vier Regeln in Reihenfolge — exakt vor adaptierend · konkret vor Typparameter · ohne Default vor
mit Default vor variadisch · eigene Methode vor Extension (gelesen `docs/guide/03-functions.md:74–84`).
Gemessen: konkret schlägt generisch (p63), `f(int)` schlägt `f(int, int = 1)` (p43).

**Ein Lambda nimmt an der Wahl nicht teil** — gelesen `docs/guide/03-functions.md:99`; gemessen q21
(Stelligkeit 1 vs. 2 → `SEM0086 … (a lambda)`), **r08b** (gleiche Stelligkeit, Rückgabetyp `int`
vs. `bool`, Trailing `{ it > 1 }` → `SEM0086`) und **r08c** (dasselbe mit **annotiertem** Rückgabetyp
`(n: int): bool => n > 1` → **trotzdem `SEM0086`**). Dabei **inferiert** der Checker den Rückgabetyp
eines Lambdas aus dem Rumpf sehr wohl: `fn apply<T, U>(x: T, f: fn(T) -> U): U` mit
`(n: int) => n * 2`, `n => n * 2` und `{ it * 2 }` → 6 6 6, `U` kommt aus dem Rumpf (gemessen r08).
Der Checker **kennt** den Rückgabetyp, **nutzt** ihn aber nicht zur Wahl (→ FN28, FN38).

### 1.3 Lambdas

| Form | Beispiel | Status |
|---|---|---|
| Paren-Lambda | `(n: int): int => { return n + 1; }` | läuft (p11) |
| Bare Lambda | `x => x * 3` | läuft, nicht im Guide, **im Beispiel** `shorthand.lyr:44` (p11, r22) |
| Trailing-Lambda + `it` | `run { 7 }`, `over(xs).map { it*2 }` | läuft, nicht im Guide, im Beispiel `:47, :64` (p11, q18) |
| Parameter-Destructuring | `((a, b)) => a + b` | läuft (p12) |
| Block-Tail im **Paren**-Lambda | `(n: int) => { let d = n*2; d + 1 }` → läuft | gemessen r20c; gelesen `docs/guide/03-functions.md:158` |
| Block-Tail im **Trailing**-Lambda | `run(5) { let d = it * 2; d + 1 }` → **`SEM0046` + `PAR0016`** — ein Trailing-Block mit `;` ist ein Statement-Block ohne Tail (`HoldsStatements`) | gemessen r20c, r20; gelesen `design/lambdas.md:74–80` („nach dem Merge mit dem ValueBlock entfällt diese Unterscheidung") |

Grammatik gelesen `docs/Grammar.md:511–513`: ein Trailing-Lambda „is that call's last **argument**,
a lambda whose one parameter is the implicit `it`" — die Grammatik spricht von Argument, nicht von
Parameter; **an welchen Parameter es bindet, steht nirgends** (gemessen r01: nächster freier).

Was **nicht** geht:

| Fehlt | Diagnose heute | Beleg |
|---|---|---|
| Trailing-Lambda mit 2 Parametern | 12 Diagnosen, `SEM0045` an Position 4 | gemessen q08 |
| Trailing als Statement mit bloßem Bezeichner | `SEM0022` + Parsefehler; als Methodenaufruf läuft es | gemessen p25, p29 |
| Default im Lambda | `PAR0002`, 12 Folgefehler | gemessen p67 |
| `params` im Lambda | `PAR0002`/`PAR0013` | gemessen p68 |
| Rekursives Lambda | `SEM0002`; Umweg `var f` läuft | gemessen p08, p34 |
| Lokale `fn` | `PAR0002` | gemessen p09 |
| `throws` am Lambda | Form existiert nicht | gelesen `docs/Grammar.md:483–487` |
| `break`/`continue`, `this`, impliziter Feldzugriff im Lambda | ICEs (Befund 1) — **auch von `check` gemeldet** (r03, r03b) | gemessen q09, q09b, q10, r03 |
| `yield` im Lambda | kompiliert lautlos, `VM0013` zur Laufzeit; `check` sagt ok | gemessen q11, q11b, r03c |
| Tail-Ausdruck nach Statement im Trailing-Block | `SEM0046` | gemessen r20c |

### 1.4 Funktionstypen und Funktionswerte

`FunctionType = 'fn' '(' [ TypeExpr {',' TypeExpr} ] ')' '->' TypeExpr .` — gelesen
`docs/Grammar.md:314`. Kein `throws`, keine Parameternamen, keine Defaults, kein `params`.

| Fall | Ergebnis | Beleg |
|---|---|---|
| freie Funktion als Wert | läuft | p11 |
| überladener Name als Wert | erwarteter Typ wählt | gelesen guide 03:112–124 |
| `fn`-Wert im Feld, Array, als Rückgabe | läuft | p23, p66, r17 |
| `?fn(int) -> int` | läuft | p72 |
| werfende Funktion als Wert | `SEM0037` | p06 |
| werfender Funktionstyp | `SEM0084` | p05 |
| Koroutinen-Funktion als Wert | läuft | p51 |
| Lambda mit `yield` | kompiliert ohne Diagnose; mit erwartetem Typ `fn() -> Coroutine<int>` `SEM0046` | q11, q11c |
| generische Funktion als Wert | `SEM0052` | p07 |
| Parameternamen im Funktionstyp | `PAR0011`/`PAR0015` | p70 |
| `==` auf Funktionswerten | `SEM0059` | p24 |
| **Varianz** | **invariant**: `let f: fn() -> Speaker = mk` (mk: `fn() -> S`, `S :: [Speaker]`) ist `SEM0001 cannot assign 'fn() -> S' to 'fn() -> Speaker'`; `let g: fn(S) -> int = take` (take: `fn(Speaker) -> int`) ebenso. **Ein Lambda an Ort und Stelle** mit erwartetem Typ `fn() -> Speaker` und Rumpf `return S {…}` **läuft** (r10b → 7, 3): der erwartete Typ fließt in den Rumpf, die Konversion passiert am `return` | gemessen r10, r10b (→ FN37) |
| `fn`-Wert über die Host-Grenze | `EMB0001` | gelesen `src/Lyric.Embedding/Marshal.cs:56–58` |

**Die Asymmetrie:** ein Effekt im *Rückgabetyp* (`Coroutine<int>`) überlebt die Umwandlung in
einen Wert, ein Effekt in einer *Klausel* (`throws`) nicht — mit der Grammatik-Begründung
`docs/Grammar.md:329–331`, die für Funktionswerte gerade nicht gilt. Für ein **Lambda** gibt es die
Koroutinen-Form gar nicht (q11: `yield` macht es nicht zur Koroutine). → FN14, FN25.

### 1.5 Methodenwert — vier Schreibweisen, vier Fehler

| Schreibweise | heute | Beleg |
|---|---|---|
| `obj.method` | `IR0001: 'Counter' has no field 'get'` | q22 |
| `Type.method` | `SEM0055` | q23 |
| `Type.staticMethod` | `IR0001: member access '.zero' on '<?>'` | q24 |
| `ifaceValue.method` | `IR0001: member access '.speak' on 'Speaker'` | q25 |

Drei tragen `IR0001`, den Code für *gültiges Lyric, das das Backend nicht kann*. Und: `h.f` als
**Feld** vom Funktionstyp läuft (r17), Feld und Methode gleichen Namens sind `RES0001` (r17b) — die
Auflösung `obj.name` ist also heute eindeutig **Feld**, und die Methode ist im selben Namensraum
verboten (→ FN12, FN45).

### 1.6 Aufrufkonvention, gemessen am Bytecode

Gelesen `docs/Bytecode.md:915–925`: ein Funktionswert ist ein Paar (Umgebungsreferenz,
Funktionsindex); „a closure without captures carries no reference and costs no allocation";
`callind` „passes it as argument 0".

```
mkclosure main.main.<lambda0> (no captures)   ← keine Allokation
newobj <env:main.main>                        ← eine Umgebung PRO Closure-Erzeugung
newobj <cell>                                 ← gefangenes 'var' → Heap-Zelle
```

Gemessene Kosten: gefangenes `var` ist im ganzen Rumpf eine Zelle (p20); jede Closure-Erzeugung
allokiert ihre eigene Umgebung (p20, q16 → 1 2 1); Schleifen-Capture pro Iteration (p65 → 0 1 2);
nicht entkommende Closure zahlt beides auch im Release (q28); **Globale werden nicht gefangen**:
`let base` im Lambda ist `mkclosure f1 (no captures)` + `ldglobal g0` (r07b), und veränderliche
Globale gibt es nicht (`var counter` auf Modulebene ist `PAR0027`, r07); **zweistufiges Capture**
funktioniert (n07 → 1 2); **eine gefangene Zelle überlebt `yield`** (r12: `var n` in einer
Koroutine, von `inc` gefangen, über zwei `yield` → 1 2). → FN16, FN27, FN40.

### 1.7 Optimierung, JIT, Rekursion

| Frage | Messung |
|---|---|
| Inlining | ja im Release (`__inl_`-Locals, p41, q17), Budget 24 IR-Instruktionen (gelesen `Inliner.cs:45`) |
| `callind` inline / devirtualisiert | nein / nein (gelesen `Inliner.cs:28`, `Devirtualizer.cs:3–17`; gemessen q29) |
| `callvirt` devirtualisiert | im einfachen Fall ja — q17 und **t_virt**: 0 `callvirt` in `main` im Release |
| Iterator-Kette | pro Element 1 `callvirt` + 1 `callind` (Befund 7, r06) |
| **Zeit pro Aufruf (Interpreter, Release, 3 M Iterationen, 3 Läufe)** | Basisschleife ohne Ruf 1.41–1.44 s · direkt (inlined) 1.83–1.92 s · devirtualisiert 1.85–1.90 s · **`callind` 2.22–2.45 s**. Abzüglich 0.27 s Start: ≈ 0.38 µs/Iteration Basis, ≈ 0.53 µs direkt, ≈ 0.68 µs indirekt — **ein `callind` kostet in der Größenordnung 130–190 ns mehr als der eingelagerte Ruf, gut ein Viertel der Iteration**. (Das Audit maß auf einer schnelleren Maschine 35–50 ns bei ≈ 250 ns/Iteration — dasselbe Verhältnis: ein Sechstel bis ein Viertel, kein Vielfaches.) | gemessen r14 |
| **Zeit unter `--jit`** | direkt **0.41 s** (≈ 46 ns/Iteration), `callind` 2.22 s (unverändert), devirtualisiert 1.88 s (unverändert). Der JIT lehnt Closures ab (gelesen `JitCompiler.cs:35–36`); `q16`, `n16` (VM0004) und `a11` (20) liefern unter `--jit` dieselben Ergebnisse (r15) | gemessen r14, r15 (→ FN43) |
| Tail Call | keiner; 100 000 → `VM0004` (p39) |
| Rekursionstiefe | `private const int MaxCallDepth = 1024` (gelesen `Interpreter.cs:139`), **thread-weit** über verschachtelte Läufe via `[ThreadStatic] _outerFrames` (gelesen `Interpreter.cs:141–157`, Commit `a27990c9`); **zweites Limit `MaxReentryDepth = 32`** für verschachtelte Host-Rückrufe (gelesen `Interpreter.cs:160–177`, „MEASURED, then halved"; Panik `Interpreter.cs:192–195`; `tests/Lyric.Tests.Embedding/ReentrancyTests.cs`); `lyrvm --help` kennt keine Tiefenoption (gemessen) |
| Panik-Ausgabe | `down(5000)`: **stderr 1026 Zeilen** (Kopf + 1023× `in main.down` + `println<string>`-Frame + `in main.main`), stdout 1022 Zeilen Programmausgabe; die Panik meldet `call depth exceeded 1024 frames in 'std.io.console.println<string>'`. **Korrektur an Fassung 2** (2048 war stdout+stderr). Über ein `var`-Lambda: `main.main.<lambda1>` ≈ 1024× (r19) | gemessen r04, r19 |
| `@Inline` | „documented No" (gelesen `STATUS.md:2472–2477`) |

---

## 2. Sprachvergleich

### 2.1 Benannte Argumente und Defaults

| Sprache | Benannt | Default darf sein | Wo lebt der Default | Preis |
|---|---|---|---|---|
| **Swift** | Pflicht-Label, `_` schaltet ab | beliebig, an der Aufrufstelle | Generator im Callee-Modul | Label ist Teil des Namens → Umbenennen bricht Aufrufer |
| **Kotlin** | optional; seit 1.4 positional nach benannt an eigener Position | beliebig, darf frühere Parameter nennen | `$default`-Bridge mit Bitmaske | `override` darf keinen Default setzen |
| **C#** | optional | nur Konstante | in den Aufrufer einkompiliert | Versionsfalle |
| **Python** | frei, `*`/`/` | beliebig | einmal bei `def` | `def f(x=[])` |
| **Ruby** | Keyword-Argumente | beliebig, frühere Parameter | Callee | zwei Parameterwelten |
| **Dart** | `{int x = 1}` / `[int x = 1]`, `required` | nur Konstante | Callee | zwei Optionalitäts-Syntaxen |
| **Scala** | optional | beliebig | `f$default$n` | Überladung × Defaults berüchtigt |
| **Java** | **gar nicht** — keine Default-Argumente, keine benannten | — | — | Überladungs-Teleskope von Hand |
| **Haskell** | gar nicht | — | — | Records/`Default`-Klassen |
| **Rust** | gar nicht | — | — | Builder |

**Rust** (kein Default, kein Name, keine Variadik): Auflösung ohne Überladung, `f(a, b)` verbirgt
nichts; bezahlt mit Builder-Typen — dem Options-Struct-Idiom, das Lyric hat (gelesen
`docs/Befunde_und_Verbesserungen/prototypes/12-named-arguments/README.md`). **Swift**: Labels sind
Pflicht; Parameternamen sind ab Tag 1 Vertrag (→ FN34).

**Trailing-Lambda-Bindung, weil FN22/FN35 sie braucht:** **Kotlin** bindet ein Trailing-Lambda an
den **letzten** Parameter, und nur wenn der einen Funktionstyp hat (`fun each(f: (Int)->Int,
n: Int = 2)` mit `each { }` ist ein Typfehler, `fun each(n: Int = 2, f: …)` mit `each { }` geht).
**Swift** (SE-0286, seit 5.3) scannt **vorwärts**: das Trailing-Closure bindet an den ersten noch
unbesetzten Parameter, der strukturell eine Funktion sein kann, und überspringt Default-Parameter,
die es nicht sind. Lyrics gemessenes Verhalten (r01: nächster unbesetzter Parameter, kein
Überspringen — r01b) ist **Swift ohne die Überspringregel**.

### 2.2 Variadische Parameter

| Sprache | Form | Fertiges Array übergeben | Aliasing |
|---|---|---|---|
| **C#** | `params T[]` | ja | Callee bekommt das Array des Aufrufers (Referenztyp) |
| **Java** | `T...` | ja | dito |
| **Kotlin** | `vararg` | Spread `*arr` | **Spread kopiert** |
| **Swift** | `Int...` | gar nicht | **die Frage entfällt, weil `Array` ein Werttyp (COW) ist** — nicht, weil Swift sie gelöst hätte |
| **Python** | `*args` | `f(*xs)` | Tupel, unveränderlich |
| **Zig** | `anytype`-Tupel | Tupel ist der Wert | comptime |
| **Rust / Dart / Haskell** | keine | — | — |

**Korrektur an Fassung 2:** die Spalte verglich Referenz-Arrays (C#/Java) mit einem Wert-Array
(Swift), ohne es zu sagen. Für FN05 ist das der entscheidende Unterschied: Lyrics Arrays sind
Referenzen (gemessen p76), also hat Lyric die C#-Frage, nicht die Swift-Nichtfrage.

### 2.3 Lambda-Kurzformen

| Sprache | Kurzform | Implizit | Trailing | Mehrere kurz |
|---|---|---|---|---|
| **Kotlin** | `{ x -> x*2 }` | `it` | ja, auch als Statement | `{ a, b -> }` |
| **Swift** | `{ $0 * 2 }` | `$0…` | ja, mehrere seit 5.3 | `$0, $1` |
| **Scala** | `_ * 2` | `_` | nein | `_ + _` |
| **C#** | `x => x*2` | nein | nein | `(a, b) =>` |
| **Rust** | `\|x\| x*2` | nein | nein | `\|a, b\|` |
| **Ruby** | Block, `Proc`, `lambda`; `_1`/`it` | `_1`/`it` | Block immer trailing | `\|a, b\|` |
| **Haskell** | `\x -> x*2`, Sections | nein | nein | `\a b ->` |
| **Python** | `lambda x: e` | nein | nein | `lambda a, b:` |
| **Lyric 4** | `x => x*2`, `{ it*2 }` | `it` | ja, außer bloßer Bezeichner in Statement-Position | nein |

Rubys Dreiteilung (Block/`Proc`/`lambda`; die Grenze, die weh tut, liegt zwischen `Proc` und
`lambda`) ist der Rule-2-Gegenfall; Scalas `_`-Reichweite die Warnung, die `design/lambdas.md:106`
schon gezogen hat.

### 2.4 Methodenwerte, generische Funktionswerte, werfende Funktionstypen

| Sprache | Methodenwert | Generische fn als Wert | Werfender Funktionstyp |
|---|---|---|---|
| **Kotlin** | `obj::m`, `Type::m` | ja | entfällt (keine checked exceptions) |
| **Swift** | `obj.m`, curried `Type.m` | ja | `throws`, `rethrows`, **`throws(E)` seit 6** |
| **C#** | Methodengruppe → Delegat, auch `Type.Static`, `obj.Ext` | ja | entfällt |
| **Rust** | `str::trim` | ja (instanziiert) | Fehler im Rückgabetyp |
| **Java** | `String::trim` | ja | `Function<T,R>` kann keine geprüfte Ausnahme werfen |
| **Scala** | Eta-Expansion | ja | entfällt |
| **Dart** | Tear-off, Gleichheit spezifiziert | ja | entfällt |
| **Haskell** | jede Funktion ist ein Wert | **rank-1 Let-Polymorphismus — für Funktionsbindungen (`let f x = …`) oder mit Signatur; ein argumentloses `let f = show` fällt unter die Monomorphism Restriction und wird NICHT generalisiert** (Korrektur an Fassung 2) | Effekt im Typ (`IO a`, `Either e a`) |
| **Zig** | `*const fn`, `&S.m` | comptime-Instanz passt in Zeiger | `E!T` |
| **Lyric 4** | keine (vier Fehler) | nein (`SEM0052`) | nein (`SEM0084`/`SEM0037`) |

Javas Fall ist die Warnung; Lyric steht dort, nur strenger. Zig-Funktionswerte können keine
Closures sein (Kontext als Zeiger) — der Gegenentwurf zu §1.6. Haskell: der Effekt vollständig im
Typ — die Reinform dessen, was FN14 sucht. Die Monomorphism-Restriction-Korrektur betrifft FN13-C:
das Gegenmodell „Wert bleibt polymorph, jede Verwendung instanziiert" gilt in Haskell nur für
Funktionsbindungen — was FN13-C eher schwächt als stärkt.

### 2.5 Lokale Funktionen, Rekursion, Inlining

| Sprache | Lokale Funktion | Rekursives Lambda | Garantierter Tail Call | `inline` |
|---|---|---|---|---|
| Swift | `func` innen | via lokale `func` | nein | Hinweis |
| Kotlin | `fun` innen | via lokale `fun` | **`tailrec`** | **`inline fun`** (reified, nicht-lokales `return`) |
| C# | lokale Funktion | ja | nein | Hinweis |
| Rust | `fn` innen, **fängt nichts** | via `fn` | nein | Hinweis |
| Scala | `def` innen | ja | **`@tailrec`** geprüft | Hinweis |
| Python | `def` innen | ja | nein, Limit ~1000, `sys.setrecursionlimit` | nein |
| Haskell | `let`/`where` | ja | ja, ohne Marker | Pragma |
| Scheme | `define` innen | ja | ja, ohne Marker (R7RS) | nein |
| Ruby | `def` in `def` → **Methode** | via Methode/`Proc` | **nicht per Default** — MRI hat TCO als Compile-Option (`tailcall_optimization: true`), standardmäßig aus (Korrektur an Fassung 2) | nein |
| Zig | keine verschachtelten `fn` | via Struct-Namespace | nein | `inline fn` |
| Lyric 4 | nein | nein (Umweg `var`) | nein, 1024 | „documented No" |

Kotlins `inline fun` bleibt das stärkste Gegenargument zum `@Inline`-Nein; Haskell/Scheme sind das
Gegenmodell zu `tailrec`. Ruby ist als Gegensatz zu Kotlin **kein sauberes Beispiel** mehr, weil
die Fähigkeit existiert und nur nicht eingeschaltet ist.

---

## 3. Designfragen

> Jede Frage: Ist-Stand mit Beleg · Optionen mit Vorbild und Preis · Empfehlung · Bruchgrad ·
> Abhängigkeiten. FN01–FN34 aus Fassung 2 (korrigiert), FN35–FN49 neu nach der zweiten Kritik.

### FN01 — Benannte Argumente

**Heute:** existieren nicht (gemessen r05: 19 Diagnosen, 7 Codes). `f(port = 8080)` kompiliert nur
bei sichtbarer veränderlicher Lokale (p42), sonst `SEM0002` (q06); Ursache: Zuweisung ist ein
Ausdruck (q07) → FN26.

| Option | Vorbild | Preis |
|---|---|---|
| A: `f(name: value)`, benannt nach positional, nur an deklarierten Funktionen | Swift/Kotlin-Mischung, Prototyp 12 | Parameternamen der stdlib werden Vertrag (FN34); Auflösung braucht einen Vor-Filter |
| B: gar nicht, Options-Struct | Rust, Haskell, Java | +5 Zeilen pro Options-API; vertauschte gleichtypige Argumente bleiben still |
| C: Pflicht-Labels | Swift | größter Bruch überhaupt |
| D: `f(name = value)` | Kotlin, C#, Python | nur nach FN26 |

Prototyp 12 (gelesen `docs/Befunde_und_Verbesserungen/prototypes/12-named-arguments/README.md:33–42`
— **Pfadkorrektur** an Fassung 2): Vor-Filter statt fünfter Stufe (Z. 37), `params` nicht benennbar
(Z. 33), nur deklarierte Funktionen (Z. 33–35), Namen ohne Bedeutung für die Redeklaration
(Z. 38–39), Namen als Vertrag (Z. 42). Das praktische Argument **für** A: `lyrls` hat keine
Parameternamen-Hints (gelesen `InlayHintProvider.cs:13–15`), also sieht man an
`connect("a", 3, false, 80)` gar nichts.

**Empfehlung: A mit `:` und Vor-Filter**, `=` in FN26 entscheiden. **Bricht:** nein.
**Hängt an:** FN26, FN28, FN34, FN35 (die Bindungsregel muss vor benannten Argumenten stehen).

### FN02 — Wo lebt ein Default: Aufrufer oder Aufgerufener?

**Heute:** beim Aufrufer, bei jedem Aufruf, nur für weggelassene Argumente (gemessen r16, p16 +
`lower`). Die C#-Versionsfalle existiert heute **nicht** (ein Compilerlauf, gelesen
`docs/guide/12-modules.md:74`); sie entsteht mit dem Paketmodell der Designrunde 2026-09.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt aufruferseitig | C# | mit dem Paketmodell Versionsfalle; FN03 gesperrt; FN04 bleibt kaputt |
| B: Callee-seitiger Generator pro Default | Swift, Kotlin, Scala | eine Funktion pro Default; drei Folgefragen: Export-Roots (gelesen `Reachability.cs:24–26`), Inlinebarkeit über Importe (gelesen `Inliner.cs:28`), Kosten an nicht-`pub`-Funktionen (**behauptet** gratis); **und die Auswertungsreihenfolge** (FN44) |
| C: Bitmasken-Bridge | Kotlin | zweite Aufrufkonvention — Rule-2-Geruch |

**Empfehlung: B**, nach Beantwortung der Folgefragen und **vor** dem Paketmodell, mit der Zusage
aus FN44 (Argumente, dann Defaults in Deklarationsreihenfolge) — ein Callee-Generator darf die
gemessene Reihenfolge nicht ändern. **Bricht:** nein (Quelle), additiv (`.lyrbc`).
**Hängt an:** Module/ABI, Build/Pakete, FN27, FN36, FN44.

### FN03 — Was darf in einem Default stehen?

**Heute:** beliebiger Aufruf ja, früherer Parameter `IR0001` mit falscher Meldung (q27).

| Option | Vorbild | Preis |
|---|---|---|
| A: frühere Parameter erlaubt | Kotlin, Ruby, Scala | mit FN02-B gratis |
| B: nur Konstanten | C#, Dart | bricht p16 |
| C: bleibt | — | falsche Meldung |

**Empfehlung: A.** 4.x: Meldung reparieren. **Bricht:** nein. **Hängt an:** FN02, FN27, FN44.

### FN04 — Defaults an Interface-Membern *(korrigiert)*

**Heute — gemessen, nicht „zu entscheiden":** die Konformanzprüfung **ignoriert Defaults
vollständig** (r02b/r02c/r02d): Interface „Hi" und Implementierung „Yo" koexistieren, der
Direktaufruf nimmt den Default des **statischen Typs** (r02d: `s.greet("B")` → `YoB`), der
Interface-Aufruf ohne das Argument ist Befund 2 (r02e). Die „C#-Überraschung" aus Option A ist
beim Direktaufruf **heute schon der Zustand**.

| Option | Vorbild | Preis |
|---|---|---|
| A: Default füllen vor dem Dispatch, Interface-Deklaration gewinnt | C# | der statische Typ entscheidet — heute schon so (r02d), nur ohne Diagnose |
| B: Default nur am Interface, Implementierung darf keinen setzen; Callee-Generator füllt | **Kotlin** (`override` ohne Default) | **braucht zuerst eine Konformanzdiagnose** (FN36) — sonst entscheidet der Generator still, welcher Wert gewinnt |
| C: Defaults an Interface-Membern verbieten | **Kotlin/Dart** (nicht Java — Java kennt gar keine Defaults; Korrektur an Fassung 2) | jede Default-Ergonomie über Interfaces fällt weg |

**Empfehlung: B, aber in der Reihenfolge FN36 → FN02-B → FN04-B.** 4.x: Befund 2 sofort (Fehler,
kein Feature). **Bricht: major**, nicht minor — jede Implementierung mit **eigenem** Default
(r02c) ändert sich, nicht nur die, die ihn wiederholt (p57). Vorher über `examples/`, `stdlib/`,
`stdlib-tests/` zählen (FN46). **Hängt an:** FN36, FN02, Interfaces (Typsystem).

### FN05 — `params`: Aliasing und Spread *(Bruchgrad korrigiert)*

**Heute:** `total(arr)` reicht dasselbe Array durch (p76). **Im Baum:** stdlib 0, stdlib-tests 0,
examples 1 (`stats.lyr:8`, ohne Array-Durchreichen), tests 1 Parser-Golden (gemessen grep).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | C#, Java | zwei Semantiken, kein Zeichen |
| B: Spread Pflicht, kopiert | Kotlin `*arr` | ein Zeichen; **bricht im Baum null Aufrufstellen** (gemessen) |
| C: Spread Pflicht, reicht durch | — | Lyric-eigen |
| D: Variadik raus | Rust, Dart, Haskell | `println`-artige APIs unbequem; **bricht im Baum eine Signatur** (`examples/stats.lyr`), keine stdlib-Signatur (gemessen; Korrektur an Fassung 2, die „heutige stdlib-Signaturen brechen" behauptete) |

**Empfehlung: B.** Lyric hat sonst kein Aliasing, das der Text verschweigt (Structs kopieren, q26).
**Bricht: minor bis null** nach der gemessenen Zählung — Fassung 2 sagte „major" ohne Messung.
4.x: §12.5-Uhr an jedem `f(arr)` (→ FN33), die im Baum **nie feuert** (FN46). **Hängt an:**
Arrays/Slices, FN23, FN46.

### FN06 — Generisches `params`

**Heute:** zwei Löcher (Befund 6). Optionen unverändert: A (Inferenzregel + Lowering), A' (nur
Lowering, `T` explizit; Zig/Rust), B (verbieten), C (aufschieben).

**Empfehlung: A in zwei Schritten** — erst A', dann die Inferenzregel als eigene Entscheidung
(„alle Argumente derselbe Typ, sonst Fehler mit Nennung der ersten beiden"). **Bricht:** nein.
**Hängt an:** Generics §8.3, `rawArrayAlloc`.

### FN07 — Der Satz der Lambda-Schreibweisen

**Heute drei** (p11, q18, r22) plus Destructuring (p12); der Guide kennt nur die erste, das
Beispiel `shorthand.lyr` alle drei, `lyrfmt` behält alle (r22). **Dazu eine gemessene Asymmetrie
im Rumpf:** Block-Tail läuft im Paren-Lambda (r20c: `(n: int) => { let d = n*2; d + 1 }`), nicht im
Trailing-Block (`run(5) { let d = it*2; d + 1 }` → `SEM0046`), weil ein Trailing-Block mit `;`
ein Statement-Block ist (gelesen `design/lambdas.md:74–80`; entfällt mit dem ValueBlock).

| Option | Vorbild | Preis |
|---|---|---|
| A: alle drei behalten, **ein** Konzept in drei Abkürzungsstufen | Kotlin, Swift | Rule 2 verlangt Begründung **und Kapitel** (FN32) |
| B: Bare streichen | — | die verbreitetste Form überhaupt fällt |
| C: Trailing streichen | C#, Rust | nimmt die Kette weg |

**Empfehlung: A** mit Spezifikationssatz, Kapitel (FN32) und dem ValueBlock, der die Rumpf-Asymmetrie
beseitigt. **Bricht:** nein. **Hängt an:** ValueBlock, FN32, FN35.

### FN08 — Trailing-Lambda mit mehreren Parametern

**Heute:** 12 Diagnosen, die richtige (`SEM0045`) an Position 4 (q08). Optionen: A `{ acc, n => … }`
(Kotlin), B `$0/$1` (Swift; Rule-2-Bruch), C bleibt einstellig.

**Empfehlung: A nach dem ValueBlock.** §D-Posten: 12 Meldungen für einen Fehler. **Bricht:** nein.

### FN09 — `it`: Bindung und Verschattung

**Heute:** innerstes `it` gewinnt in beide Richtungen (q01 → 6, q02 → 3 + `SEM0071`, p33 → 22);
`design/lambdas.md:109–110` nennt nur den inneren Fall. **Neu (r20b):** `it` ist ein gewöhnlicher
Name, den ein inneres Paren-Lambda **fängt** (`run(5) { let g = (n: int) => { return it + n; }; return g(1); }` → 6) → FN49.

| Option | Vorbild | Preis |
|---|---|---|
| A: festschreiben — innerstes `it` gewinnt, gewöhnlicher Name, fangbar | Kotlin | äußere Lokale `it` ist eine Falle; Diagnose muss sie benennen |
| B: `it` Schlüsselwort | — | bricht Programme mit Variable `it` (stdlib: keine, gelesen `design/lambdas.md:110`) |
| C: `it` nur ohne `it` in Sicht | — | verbietet `map { it }` in `forEach { }` |

**Empfehlung: A** + Pflicht-Hinweis in `SEM0071` für ungenutztes `it`. 4.x: `design/lambdas.md`
**ergänzen** (äußerer Fall, Capture-Fall). **Bricht:** nein. **Hängt an:** FN41, FN49.

### FN10 — Trailing-Lambda in Statement-Position

**Heute:** `r.each { }` läuft (p29), `each { }` ist `SEM0022` + Parsefehler (p25; `_allowStructInit`,
gelesen `design/lambdas.md:87–95`). Optionen: A bleibt, B `Name {` am Statement-Anfang wird
Trailing (Kotlin, Swift), C alles als Methode.

**Empfehlung: B** — es ändert nur die Diagnose eines heute schon fehlerhaften Programms.
**Bricht:** nein. **Hängt an:** ValueBlock, §6.8.

### FN11 — Darf ein Lambda schreiben, was eine Funktion schreiben darf?

**Heute nein** (p67, p68, p70). Optionen: A Lambda = Funktion minus Name (Python, Dart), B minimal
+ `throws` (Kotlin, Swift, Rust), C nur Diagnosen.

**Empfehlung: B.** Defaults im Lambda hießen Defaults im **Typ** — das zieht **FN37** nach sich.
**Bricht:** nein. 4.x: Recovery-Kaskaden nach §D.

### FN12 — Methodenwert: eine Schreibweise, vier Fehler

**Heute:** vier Ablehnungen (§1.5); **und** die Namensraum-Regel ist als Diagnose vorhanden:
Feld und Methode gleichen Namens sind `RES0001` (r17b), `h.f` ist der **Feldwert** ohne `this`
(r17). **Korrektur an Fassung 2:** die Regel „Feld und Methode teilen einen Namensraum" muss nicht
erst spezifiziert werden — sie wird geprüft; was fehlt, ist der Satz, dass `obj.name` bei einer
**Methode** ein gebundener Wert **mit** `this` wäre, während ein Feld keins bekommt (→ FN45).

| Option | Vorbild | Preis |
|---|---|---|
| A: alle vier bauen | Kotlin, Swift | neue Closure pro Auswertung (FN19); `ifaceValue.m` zahlt auf dem teuersten Pfad (FN17) |
| B: nur `obj.m` | Python | häufigster Fall geht ohnehin (freie Funktion) |
| C: `obj::m` | Kotlin, Java | `::` ist vergeben (gelesen `docs/Grammar.md:139`) |
| D: eine Diagnose statt vier | — | billig, ehrlich |

**Empfehlung: A ohne ungebundene Feld-Referenz; `ifaceValue.m` erst nach FN17.** Vorher die
`this`-Regel aus FN45. **Bricht:** nein. **Hängt an:** FN17, FN19, FN45, Diagnostik.

### FN13 — Generische Funktion als Wert

**Heute:** `SEM0052` (p07). Optionen: A Instanziierung an der Wertstelle (Rust, C#, Kotlin), B bleibt,
C polymorpher Wert (Haskell rank-1 — **nur für Funktionsbindungen**, siehe §2.4), D rank-2.

**Empfehlung: A** (gelesen `design/lambdas.md:184`: „Sema ~80 Z."). C ist für Lyric
ausgeschlossen, weil ein Funktionswert ein **Index** ist (gelesen `docs/Bytecode.md:915`) — und
selbst Haskell generalisiert den argumentlosen Fall nicht. **Bricht:** nein.

### FN14 — Werfende Funktionstypen

**Heute:** `SEM0084`/`SEM0037` (p05, p06); Koroutine als Wert läuft (p51). Optionen: A `FnType` mit
`Throws`, einseitige Zuweisbarkeit (Swift `throws(E)`, Zig, Haskell), B `rethrows`-Schlüsselwort,
C bleibt (Java).

**Empfehlung: A** = typed throws Stufe 3 (gelesen `PLAN.md:263`, `PLAN.md:132`). **Bricht:** nein.
**Hängt an:** typed throws St. 1–2; **FN37** (einseitige Zuweisbarkeit ist eine Varianzregel).

### FN15 — Lokale Funktionen und rekursive Lambdas

**Heute:** lokale `fn` `PAR0002` (p09), rekursives Lambda `SEM0002` (p08), Umweg `var f` (p34).
Optionen: A lokale `fn` mit Capture (Swift, Kotlin, C#, Scala, Python, Haskell; Rust ohne
Capture), B `let rec`, C nichts (Zig).

**Empfehlung: A mit Capture.** **Bricht:** nein. **Hängt an:** FN16, FN24, FN41.

### FN16 — Capture-Modell

**Heute:** `let` kopiert, `var` wird Zelle, Umgebung pro Closure-Erzeugung (p20, q16, q28);
Globale werden **nicht** gefangen (r07b: `ldglobal`), zweistufig geht (n07), **Zelle überlebt
`yield`** (r12). Optionen: A festschreiben (Kotlin, Swift, JS), B Capture-Liste (Swift/C++), C
`move`, D `var`-Capture verbieten (Zig).

**Empfehlung: A**, in der Spezifikation die beobachtbaren Sätze: gefangene `var` ist im ganzen
Rumpf eine Indirektion; zwei Lambdas teilen die Zelle, nicht die Umgebung; **eine gefangene Zelle
ist Teil des Koroutinenzustands** (FN40). **Bricht:** nein. **Hängt an:** FN24, FN30, FN40.

### FN17 — Funktionswerte optimieren *(mit Zeitmessung)*

**Heute:** `callind` weder inline noch devirtualisiert (q29); `callvirt` im einfachen Fall ja (q17,
t_virt); auf der Kette pro Element 1 `callvirt` + 1 `callind` (Befund 7). **Zeit (r14):** ein
`callind` kostet ≈ 130–190 ns über dem eingelagerten Ruf bei ≈ 530 ns Iteration — ein Viertel,
kein Vielfaches. **Unter `--jit`** (Befund 9) wird der direkte Ruf 4–5× schneller und der
`callind` gar nicht: dort ist das Verhältnis **≈ 16×**.

| Option | Vorbild | Preis |
|---|---|---|
| A: Closure-Devirtualisierung bei genau einer `mkclosure`-Definition | derselbe Pass-Zweig wie `mkiface` (gelesen `Devirtualizer.cs:5–10`) | löst q29, **nicht** die Kette (dort steht `loadfield`) |
| A+: Feld-Weiterleitung für Objekte in alleinigem Besitz, die nur als Interface-Wert disqualifiziert sind | HotSpot | Eingriff in `ScalarReplacement`; **Gewinn im Interpreter ≈ 130–190 ns pro Element und Stufe** (r14), im JIT der Unterschied zwischen „läuft" und „läuft nicht" |
| B: `inline fn` | Kotlin | bricht `@Inline`-Nein; nicht-lokales `return` |
| C: nichts | — | Kette bleibt, JIT bleibt draußen |
| D: `fn map<F>(f: F)` monomorphisiert | Rust | zweiter Übergabeweg — Rule 2 |

**Empfehlung: A und A+, B nicht — abgenommen gegen einen Benchmark (FN42), nicht gegen
Instruktionszahlen.** **Korrektur an Fassung 2:** „teuerster Pfad" war ohne Zeitmessung; gemessen
ist er im Interpreter ein Viertel teurer, im JIT ausgeschlossen. Das zweite ist das stärkere
Argument für A+: es macht die Kette **JIT-fähig** (FN43), nicht nur ein Viertel billiger.
**Bricht:** nein. **Hängt an:** Optimierer-Runde, FN12, FN30, FN42, FN43.

### FN18 — Rekursionstiefe und Tail Calls *(Zahlen korrigiert)*

**Heute:** 1024 Frames (gelesen `Interpreter.cs:139`), kein TCO (p39), Panik druckt jeden Frame:
**stderr 1026 Zeilen** (r04; nicht 2048 — das war stdout+stderr), gemeldet im innersten Frame
(`println<string>`), und über ein Lambda ≈ 1024× `main.main.<lambda1>` (r19).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt, Limit konfigurierbar (FN31), Trace gekürzt (erste N + letzte N + „… ×n") | Python | rekursive Algorithmen bleiben begrenzt |
| B: geprüftes `tailrec` | Kotlin, Scala | Schlüsselwort für einen Fall, den `while` löst |
| C: automatisches TCO | Scheme, Haskell (Ruby: nur als Option) | Traces lückenhaft |

**Empfehlung: A jetzt (mit FN31, FN48), B später.** **Bricht:** nein.

### FN19 — Identität und Gleichheit von Funktionswerten

**Heute:** `SEM0059` (p24). Optionen: A bleibt (Swift), B Referenzgleichheit (Dart), C nur
capture-frei. **Empfehlung: A** mit Begründungssatz (Paar Umgebung/Index, Umgebung pro Auswertung).
**Bricht:** nein. **Hängt an:** FN12.

### FN20 — `main`

**Heute:** zwei Formen (p53–p55). **Empfehlung: B (darf werfen, Runtime mappt auf Exit-Code; Rust,
Swift) + C (`void`-`main`; C#, Kotlin, Dart).** **Bricht:** nein. **Hängt an:** typed throws, CLI.

### FN21 — Unbenutzter Rückgabewert

**Heute:** still (p69). Optionen: A `@MustUse` (Rust `#[must_use]`), B generell warnen (Swift
`@discardableResult` umgekehrt), C nichts. **Empfehlung: A.** **Bricht:** nein.
**Korrektur an Fassung 2:** „`@Deprecated` bekommt ohnehin ein `until`-Feld" — das Feld **existiert
seit 3.x** (gelesen `docs/guide/15-attributes.md:284–301`, `LYR-SEM0081`; `CHANGELOG.md` `until =
"3.0"`, `"4.0"`). Die Andockstelle ist da, nichts ist „ohnehin" zu bauen.

### FN22 — Defaults × Trailing-Lambda *(Prämisse korrigiert)*

**Heute: die Kombination existiert**, sobald der `fn`-Parameter vor den Defaults steht (r01:
`fn each(f: fn(int)->int, n: int = 2)` → 3 3 6). Was nicht geht: `each(5) { }` (r01b, `SEM0001`),
weil positionale Argumente ab Parameter 0 füllen und das Trailing an den **nächsten freien**
Parameter bindet; und die Reihenfolge Default-vor-`fn` (r01c, `SEM0025`). `repeat(times: int = 3)
{ … }` ist also nur in **dieser** Parameterreihenfolge unschreibbar — `repeat(f, times = 3)` geht.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — `fn`-Parameter zuerst, Trailing bindet an den nächsten freien | **Swift ohne Überspringregel** (gemessener Stand) | `each(5) { }` bleibt unschreibbar; die Regel steht nirgends (FN35) |
| B: „Pflicht nach Default" erlauben, sobald benannte Argumente da sind | Kotlin | teuer, und **nicht nötig**, um Default + Trailing zu bekommen |
| C: Trailing bindet an den **letzten** `fn`-Parameter, Defaults davor dürfen positional gefüllt werden | **Kotlin** (letzter Parameter), **Swift** (Vorwärtsscan mit Überspringen) | `each(5) { }` wird schreibbar ohne benannte Argumente; eine Regel, die von der Art des Parameters abhängt |
| D: Defaults an Trailing-Funktionen verbieten | — | schreibt eine Einschränkung fest, die niemand will |

**Empfehlung: C, als Bindungsregel in FN35 — nicht B.** **Korrektur an Fassung 2:** B stand auf der
falschen Prämisse, dass Default + Trailing unmöglich sei. Der billige Schritt ist, die gemessene
Regel zu spezifizieren und das Überspringen von Default-Parametern zu erlauben; benannte Argumente
sind dafür nicht nötig. **Bricht:** nein (heute Fehler). **Hängt an:** FN35, FN01 (nur für den
benannten Weg), FN23.

### FN23 — `params` nach einem Default

**Heute:** erlaubt, unerreichbar (q13). **Im Baum: keine Signatur trägt beides** — gemessen (grep:
ein `params` in `examples/stats.lyr:8` ohne Default; stdlib 0). **Korrektur an Fassung 2:**
„behauptet, vermutlich keine" ist jetzt gemessen.

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | C#, Java | toter Zustandsraum |
| B: verbieten | Swift (Labels trennen) | **bricht im Baum nichts** (gemessen) |
| C: nur benannt erreichbar | Kotlin | = A ohne FN01 |
| D: `params` vor Defaults | — | dreht die Reihenfolge |

**Empfehlung: B bis FN01, danach C.** **Bricht:** minor → nach Messung **null im Baum**. 4.x:
§12.5-Uhr an der Deklaration, die im Baum nie feuert (FN46). **Hängt an:** FN01, FN05, FN46.

### FN24 — Was ist an den Lambda-Rahmen gebunden, und wer prüft es? *(Begründung neu)*

**Heute:** fünf Konstrukte, fünf Antworten (Befund 1). **Korrektur an Fassung 2:** die Begründung
„Sema, weil `check`/LSP das Lowering nicht sehen" ist **falsch** — `check` schließt das Lowering
ein (gelesen `SourceCompiler.cs:28–32`), der LSP ruft `SourceCompiler.Check` (gelesen
`AnalysisService.cs:553`), und `check c_q09`/`c_q10` liefern exit 1 mit `CLI0020` (r03, r03b).

| Option | Vorbild | Preis |
|---|---|---|
| A: **Spezifikationssatz + Sema-Regel**: ein Lambda ist ein Rahmen; `return` bindet an ihn, `this` wird gefangen, `break`/`continue`/`yield` außerhalb eines eigenen Loops/einer eigenen Koroutine sind **Sema-Fehler** | Kotlin (ohne `inline`), Swift, C# (CS0139) | Rahmen-Stack in der Sema |
| B: nicht-lokale Steuerung, wo das Lambda inline verschwindet | Kotlin `inline fun` | zieht FN17-B nach sich |
| C: ICEs einzeln beheben | — | `yield` bleibt still |

**Empfehlung: A — mit der richtigen Begründung:** (1) eine ICE ist **keine Diagnose** — sie hat
keinen eigenen Code, keine Note, keinen Spann im Quelltext und fordert einen Bug-Report für ein
Nutzerfehler-Programm; (2) `yield` wird von **keiner** Schicht gefangen (r03c: `check` sagt ok,
Laufzeit `VM0013`); (3) die Sema ist die Schicht, deren Meldungen `lyrls` als Squiggle zeigt, eine
`CLI0020` erscheint dort als Compilerdefekt. **Und ein sechstes Konstrukt gehört dazu: `defer`**
(FN39). **Bricht:** nein. 4.x: sofort. **Hängt an:** FN16, FN25, FN15, FN39, Diagnostik.

### FN25 — Darf ein Lambda eine Koroutine sein? *(Vorbilder korrigiert)*

**Heute:** `yield` im Lambda kompiliert lautlos (q11), panikt beim Aufruf (q11b), mit erwartetem
Koroutinentyp `SEM0046` (q11c).

| Option | Vorbild | Preis |
|---|---|---|
| A: `{ yield … }` ist ein Koroutinen-Literal, der Rumpf entscheidet | **Python allein** (dort macht `yield` sogar ein `lambda` zum Generator) | ein Schlüsselwort tief im Rumpf ändert den Typ |
| B: `yield` im Lambda ist ein Sema-Fehler; Generatoren sind deklarierte Funktionen | **C#** (CS1621: `yield` in Lambda/anonymer Methode verboten), Kotlin (`sequence { }` als Bibliotheksform), Rust | Generator nicht inline schreibbar |
| C: eigene Syntax (`gen { }`) | **JS `function*`** (Arrow-Funktionen können keine Generatoren sein) | zweiter Lambdabegriff |

**Korrektur an Fassung 2:** C# und JS standen bei A; C# verbietet genau das, JS ist explizite
Syntax. Die Vorbildlage dreht sich: A hat nur Python, B hat C#. **Zur Begründung:** Fassung 2
schrieb „Lyrics Lambda-Typisierung läuft umgekehrt, der Kontext gibt den Typ". Zu einfach —
**Parametertypen** kommen aus dem Kontext, der **Rückgabetyp fließt aus dem Rumpf heraus** (r08:
`U` wird aus `{ it * 2 }` inferiert). A wäre also mechanisch nicht fremd; der Grund gegen A bleibt,
dass ein `yield` den **Typ** (Koroutine statt Funktion) und nicht nur den Rückgabetyp ändert.

**Empfehlung: B, als Teil von FN24.** **Bricht:** nein. **Hängt an:** FN24, Koroutinen.

### FN26 — Ist Zuweisung ein Ausdruck? *(Vorbild korrigiert, Bruch gemessen)*

**Heute:** ja (q07). **Im Baum:** grobe Zählung `\(\s*ident\s*=[^=>]` über `examples/`, `stdlib/`,
`stdlib-tests/` → **0 echte Treffer** (gemessen; Regex, nicht Parser — Restunsicherheit).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt Ausdruck | C, C++, Java, C#, JS | `=` für FN01-D verbrannt; p42-Falle bleibt |
| B: Zuweisung wird **Anweisung** | Rust (`=` liefert `()`), Go, Python, **Kotlin** (Zuweisung ist kein Ausdruck: `val y = (x = 5)` ist „Assignments are not expressions") | `let y = (x = 5)` und `while ((l = next()) != null)` verschwinden |
| B': bleibt Ausdruck vom Typ `void` | **Swift allein** (Typ `()`) — **Korrektur an Fassung 2:** Kotlin gehört zu B, nicht B' | kleinster Eingriff; `if (x = 5)`, `f(port = 5)` werden Typfehler |
| C: nur in Argumentposition verboten | — | Sonderregel |

**Empfehlung: B'.** **Bricht:** nach Zählung **minor bis null**; vorher mit Parser statt Regex
zählen (FN46). 4.x: §12.5-Uhr (FN33). **Hängt an:** FN01, Statements/Ausdrücke.

### FN27 — Was darf ein Default-Ausdruck sein, und was darf er fangen? *(Option A korrigiert)*

**Heute:** Lambda als Default läuft (q14), früherer Parameter `IR0001` (q27). **Globale werden
nicht gefangen** — `ldglobal`, kein Capture (r07b); veränderliche Globale existieren nicht (r07,
`PAR0027`).

| Option | Vorbild | Preis |
|---|---|---|
| A: beliebiger Ausdruck, darf frühere Parameter nennen, darf eine Closure sein — sie fängt **die früheren Parameter**, sonst nichts (Globale sind Ladebefehle, keine Captures) | Kotlin, Scala, Ruby | mit FN02-B natürlich; die Closure entsteht im Callee |
| B: capture-frei | — | schließt `= (n) => n * factor` aus |
| C: nur Konstanten | C#, Dart | bricht q14, p16 |

**Empfehlung: A mit FN02-B**, plus Spezifikationssatz, **wer die Allokation zahlt**. **Bricht:**
nein. **Hängt an:** FN02, FN03, FN30, FN44.

### FN28 — Trägt ein Lambda-Argument seine Stelligkeit in die Überladungswahl? *(Option C neu bewertet)*

**Heute nein** (q21). **Und:** gleiche Stelligkeit, verschiedener Rückgabetyp ist ebenfalls
`SEM0086` — mit Trailing (r08b) **und** mit annotiertem Rückgabetyp `(n: int): bool =>` (r08c),
obwohl der Checker den Rückgabetyp aus dem Rumpf inferieren kann (r08).

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | — | `map`/`fold`-Paare unbaubar |
| B: Stelligkeit zählt | Kotlin, Swift | löst q21, **nicht r08b** |
| C: Stelligkeit + Typisierung des Lambdas gegen jeden Kandidaten gleicher Stelligkeit, dann Filter nach inferiertem Rückgabetyp | **Swift** (Closures werden pro Kandidat typisiert), C# | Zwei-Phasen-Auflösung; **die Inferenz existiert schon (r08)** — was fehlt, ist, sie pro Kandidat zu wiederholen und Fehler beim Rumpf-Typisieren als „passt nicht" zu werten |
| D: bessere Diagnose | — | löst nur Verständlichkeit |

**Empfehlung: B sofort, C als eigene Entscheidung, D in jedem Fall.** **Korrektur an Fassung 2:**
C war als „teuer und schwer zu erklären" abgetan, ohne zu sagen, warum die vorhandene Inferenz nicht
reicht. Sie reicht mechanisch; der Preis ist, dass ein Rumpf gegen zwei Kontexte typisiert wird und
ein Typfehler im Rumpf dann **still ein Kandidat weniger** statt eine Meldung ist — das ist die
C#-Lektion, und sie ist ein Diagnostik-Preis, kein Implementierungs-Preis. → FN38 entscheidet.
**Bricht:** nein. **Hängt an:** FN01, FN38, Diagnostik.

### FN29 — Funktionswerte über die Einbettungsgrenze *(Reentranz korrigiert)*

**Heute:** `EMB0001` (gelesen `Marshal.cs:56–58`). **Korrektur an Fassung 2:** der Reentranz-Befund
(`PLAN.md:106`) ist **erledigt** — `STATUS.md:1690–1696` führt „script→host→script reentrancy"
unter B als leer, Commit `a27990c9` hat `_outerFrames` eingeführt (thread-weite Zählung, gelesen
`Interpreter.cs:141–157`) **und** `MaxReentryDepth = 32` (gelesen `Interpreter.cs:160–177`,
Test `ReentrancyTests.cs`). `PLAN.md:106` ist die nicht durchgestrichene Alt-Liste.

| Option | Vorbild | Preis |
|---|---|---|
| A: `fn`-Werte bleiben draußen; Callbacks über Queue + Lyric-Handler | Designrunde 2026-09, Lua, Zig | Host hält kein Lambda |
| B: opaker Handle auf einen Funktionswert | Lua `luaL_ref`, Python | drei Folgefragen: Lebensdauer der Zelle, **Capability beim späteren Ruf**, Zählung gegen `MaxCallDepth` **und `MaxReentryDepth`** |
| C: .NET-Delegat | C#-Interop | jeder Rückruf ist ein verschachtelter Lauf und zählt gegen 32 (FN47) |

**Empfehlung: A jetzt, B als eigene Runde** — die Reentranz ist kein Hindernis mehr, die
Capability-Frage ist es. **Bricht:** nein. **Hängt an:** FFI, Capabilities, FN31, FN47.

### FN30 — Darf eine nicht entkommende Closure ohne Allokation auskommen? *(Vorbild korrigiert)*

**Heute nein, auch im Release** (q28); capture-frei kostet keine Allokation, aber den `callind`
(q29). **Unter `--jit`** ist eine Funktion mit Closure gar nicht kompilierbar (Befund 9).

| Option | Vorbild | Preis |
|---|---|---|
| A: Zusage in der Spezifikation | HotSpot/Go (ohne Zusage) | Versprechen, das jede Optimiereränderung bricht |
| B: keine Zusage, Pass bauen (FN17-A+) | Rust, C# | Kosten bleiben unvorhersagbar |
| C: Sprachform für die nicht entkommende Closure | **C# `static` lambda — nur als prüfbare Markierung**: `static` verbietet das Capture (CS8820), **es ändert das Allokationsverhalten nicht** — jedes nicht fangende C#-Lambda wird einmal allokiert und in einem statischen Feld gecacht, mit oder ohne `static` (Korrektur an Fassung 2) | Marker, damit der Compiler tut, was er selbst herausfinden kann |

**Empfehlung: B, C nicht.** **Bricht:** nein. **Hängt an:** FN17, FN16, FN43, Optimierer-Runde.

### FN31 — Wo wird die Rekursionstiefe konfiguriert? *(beide Limits genannt)*

**Heute:** nirgends. **Zwei** Größen: `MaxCallDepth = 1024` (Interpreter-Frames, thread-weit über
verschachtelte Läufe via `_outerFrames` — **die Frage „global oder pro Invoke" ist im Code
entschieden: global pro Thread**, gelesen `Interpreter.cs:141–157`) und `MaxReentryDepth = 32`
(verschachtelte Läufe = CLR-Stack, gelesen `Interpreter.cs:160–177`). `ExecutionBudget` kennt nur
Instruktionen (gelesen `src/Lyric.Vm/ExecutionBudget.cs:26–53`).

| Option | Vorbild | Preis |
|---|---|---|
| A: Eigenschaft des Laufs — `lyrvm --max-depth` + Feld in `ExecutionBudget` | Python, JVM `-Xss` | Programm läuft je nach Aufruf verschieden tief |
| B: Eigenschaft des Moduls | — | Format-Frage bei gepinntem Format |
| C: nur Embedding-API | Lua | CLI-Nutzer kann nichts tun |
| D: bleibt 1024 | heute | Tiefe > 1024 unschreibbar |

**Empfehlung: A und C für `MaxCallDepth`; `MaxReentryDepth` bleibt Konstante, wird aber
dokumentiert (FN47).** `--max-depth` bewegt **nur** die Frame-Zahl; wer 4096 konfiguriert und 33-fach
zurückruft, stirbt bei 32 — das muss die Meldung sagen. **Bricht:** nein. **Hängt an:** FN18, FN29,
FN47, VM, CLI.

### FN32 — Werkzeuge für die drei Lambda-Formen *(Befundlage korrigiert)*

**Heute:** Guide-Kapitel fehlt (Befund 5); **Beispiel** `shorthand.lyr` lehrt alle drei Formen
(gelesen `:1–6, :44, :47, :64`); **Formatierer** behält Einzelformen (r22) und bricht nur die
**Kette** falsch (q20); **Editor** ohne Parameternamen-Hints (gelesen `InlayHintProvider.cs:13–15`);
**Trace** nennt `main.main.<lambda1>` (r19 → FN48).

| Option | Vorbild | Preis |
|---|---|---|
| A: fertig = Kapitel + Formatiererregel + Editor | Rust, Go | blockiert Auslieferung an Werkzeugarbeit |
| B: Kapitel + Formatiererregel, **`examples/` zählt als Lehre mit** | Kotlin, Swift | ein Nutzer sieht die Form und bekommt sie stabil gesetzt |
| C: nur Kapitel | Pipeline-Regel | Kettenformatierung bleibt |
| D: nichts | — | Befund 5 |

**Empfehlung: B**, mit `examples/` als Teil der Fertigstellung — Fassung 2 tat so, als gäbe es nur
Grammatik und Guide. Das einzige echte Werkzeugloch ist die Kettenformatierung. **Bricht:** nein.

### FN33 — Welche Entscheide bekommen eine §12.5-Migrationswarnung? *(Reihenfolge ohne Datenbasis korrigiert)*

**Heute:** vier Uhren laufen (`SEM0107`–`SEM0110`, gelesen `STATUS.md:50–56`,
`docs/guide/19-diagnostics.md:59–78`; Form gelesen `SemaRules.cs:339–344`).

| Kandidat | Meldet ein Programm, dessen Bedeutung sich ändert, egal wie? | **Feuert im Baum** (gemessen) | Urteil |
|---|---|---|---|
| FN26/FN01 — Zuweisung als Wert | ja | 0 (Regex) | aufnehmen |
| FN05 — `f(arr)` an `params` | ja | 0 | aufnehmen |
| FN23 — `params` nach Default | ja | 0 | aufnehmen |
| FN36 — abweichender Default in der Implementierung | ja (r02c/r02d sind legal und ändern sich unter FN04-B) | nicht gezählt (**behauptet**: wenige) | **aufnehmen — neu** |
| FN04 Interface-Aufruf ohne Default | nein (ICE) | — | Bugfix |
| FN24/FN25/FN39 | nein (ICE/Panik/Prozesstod) | — | sofort |
| additive Formen | nein | — | nicht |

**Empfehlung: vier Uhren.** **Korrektur an Fassung 2:** die Reihenfolge „FN26 zuerst, weil FN01 an
ihr hängt" war eine Priorität ohne Daten; nach Zählung feuern drei der vier im Baum **nie** — sie
sind nach `STATUS.md:50–56` zulässig, aber ihre Reihenfolge ist egal. Die einzige mit echten
Treffern ist voraussichtlich FN36, und die ist zuerst zu zählen (FN46). Rücknahmeregel: eine Uhr
retiriert mit ihrer Frage. **Bricht:** nein. **Hängt an:** FN46, `lyric-spec` §12.5.

### FN34 — Parameternamen als Vertrag

**Heute:** kein Vertrag (p01, p70); Prototyp 12 Z. 42 sagt, dass sich das ändert. Optionen: A nichts,
B Konformanz-Suite pinnt öffentliche Parameternamen, C Umbenennungs-Uhr (Kotlin `ReplaceWith`),
D Lint. **Empfehlung: B mit FN01, D als einmalige Durchsicht.** **Bricht:** nein.

---

### FN35 — An welchen Parameter bindet ein Trailing-Lambda? *(neu)*

**Heute:** an den **nächsten unbesetzten** Parameter (r01: `each { it + 1 }` füllt `f`, `n`
bleibt Default); `each(5) { it + 1 }` ist `SEM0001` + 2× `SEM0045` (r01b), weil `5` zuerst `f`
füllt. Die Grammatik sagt nur „that call's last argument" (gelesen `docs/Grammar.md:511–512`) —
**die Bindungsregel steht nirgends.**

| Option | Vorbild | Preis |
|---|---|---|
| A: nächster freier Parameter (Ist-Stand festschreiben) | Swift ohne Überspringregel | `repeat(5) { }` bleibt unschreibbar ohne benannte Argumente; jede std-API mit Default muss den `fn`-Parameter vorne führen |
| B: **letzter** Parameter, und er muss einen Funktionstyp haben | **Kotlin** | `fn each(f, n = 2)` mit `each { }` wird ein Fehler — bricht r01, das heute läuft |
| C: Vorwärtsscan: nächster freier Parameter, **der eine Funktion sein kann**; Default-Parameter anderer Typen werden übersprungen | **Swift SE-0286** | `each(5) { }` bindet `5` an … `f`? Nein: positionale Argumente füllen weiter ab 0, also braucht C auch die Regel „ein positionales Argument überspringt einen `fn`-Parameter, wenn ein Trailing folgt" — eine Zwei-Richtungs-Regel |
| D: Trailing bindet an den **letzten `fn`-Parameter**, positionale Argumente füllen die übrigen der Reihe nach | Kotlin-nah, Lyric-eigen | `fn repeat(times: int = 3, f: …)` wird schreibbar (mit SEM0025-Ausnahme für den letzten `fn`-Parameter, wenn er trailing gefüllt wird) **und** `fn each(f, n = 2)` bleibt gültig; eine Regel, die von der Art des Parameters abhängt |

**Empfehlung: D.** Es ist die einzige Option, die r01 (läuft heute) **und** `repeat(5) { }` (der
Kotlin-Idiomfall) bedient, ohne benannte Argumente vorauszusetzen; der Preis (Regel nach
Parameter-Art) ist derselbe, den Kotlin und Swift zahlen. Der Satz gehört in `docs/Grammar.md`
neben „last argument". **Bricht:** nein (D erweitert A). **Konfidenz:** gemessen (r01, r01b, r01c).
**Hängt an:** FN22, FN01, FN28.

### FN36 — Sind Default-Argumente Teil des Interface-Vertrags? *(neu)*

**Heute:** nein — die Konformanzprüfung ignoriert sie (r02b: Default nur im Interface; r02c: nur
in der Implementierung; r02d: „Hi" vs. „Yo"; alles läuft, der Direktaufruf nimmt den Default des
statischen Typs). Es gibt **keine Diagnose** für eine abweichende Implementierungs-Deklaration.

| Option | Vorbild | Preis |
|---|---|---|
| A: Default gehört zum Vertrag — die Implementierung darf keinen deklarieren; Fehler an der Deklaration | **Kotlin** („An overriding function is not allowed to specify default values") | jede Implementierung mit eigenem Default (r02c) wird ein Fehler → Bruch |
| B: Default darf wiederholt, aber nicht abweichend sein (gleicher Ausdruck) | — | „gleicher Ausdruck" ist syntaktischer Vergleich — brüchig |
| C: Defaults sind kein Vertrag, der statische Typ entscheidet, **dokumentiert** | C# | die Überraschung wird Regel; r02d bleibt legal |
| D: Defaults nur am Interface **oder** nur an der Implementierung, nie an beiden | — | halbe Regel |

**Empfehlung: A, aber als §12.5-Uhr zuerst (FN33), Fehler mit 5.0** — Voraussetzung für FN04-B,
sonst entscheidet der Callee-Generator still. **Bricht:** major (jede Implementierung mit eigenem
Default); im Baum zu zählen (FN46). **Konfidenz:** gemessen (r02b–r02e). **Hängt an:** FN04, FN02,
FN33, FN46, Interfaces.

### FN37 — Sind Funktionstypen invariant, und soll das so bleiben? *(neu)*

**Heute:** invariant — `fn() -> S` passt nicht an `fn() -> Speaker`, `fn(Speaker) -> int` nicht an
`fn(S) -> int` (r10, 2× `SEM0001`). Ein **Lambda an Ort und Stelle** mit erwartetem Typ läuft
(r10b), weil der erwartete Typ in den Rumpf fließt und die Konversion am `return` passiert — ein
benannter Funktionswert bekommt diese Chance nicht. Interface-Werte sind Lyrics **einziger** Subtyp.

| Option | Vorbild | Preis |
|---|---|---|
| A: invariant bleibt; der Umweg ist ein Lambda | Go (Funktionstypen invariant), Zig | jeder Callback mit Interface-Rückgabe braucht einen Wrapper |
| B: Rückgabe kovariant, Parameter kontravariant — nur über Interface-Werte | C# (Delegatvarianz seit 2.0/4.0), Kotlin (`out`/`in` an Funktionstypen), Swift, Scala | der Aufruf durch den Wert braucht die Konversion **am Rand** (`mkiface` beim Rückgabewert, Rückkonversion beim Parameter): ein Thunk pro Konversion oder eine Konvention, die der Interpreter kennt — im Bytecode ist ein Funktionswert ein **Index** (gelesen `docs/Bytecode.md:915`), kein Adapter |
| C: nur Rückgabe-Kovarianz | — | halbe Regel, die die Parameter-Frage später erneut stellt |

**Empfehlung: A bis FN14, dann B zusammen mit der einseitigen `throws`-Zuweisbarkeit** — FN14-A
**ist** bereits eine Varianzregel (nicht-werfend passt, wo werfend erwartet wird), und zwei
Varianzregeln an demselben Typ gehören in eine Entscheidung. Der Bytecode-Preis (Thunk) ist zu
messen. **Bricht:** nein (additiv). **Konfidenz:** gemessen (r10, r10b). **Hängt an:** FN14, FN11,
Typsystem (Subtyp), Bytecode.

### FN38 — Darf der inferierte Rückgabetyp eines Lambdas die Überladung entscheiden? *(neu)*

**Heute:** nein — `apply(3) { it > 1 }` gegen `fn(int)->int` / `fn(int)->bool` ist `SEM0086`
(r08b), **auch mit annotiertem** Rückgabetyp (r08c), obwohl `U` aus dem Rumpf inferiert wird (r08).
FN28-B (Stelligkeit) löst das nicht: beide Kandidaten sind einstellig.

| Option | Vorbild | Preis |
|---|---|---|
| A: Lambdas wählen nicht; nur ein annotierter Rückgabetyp zählt (r08c wird eindeutig, r08b nicht) | — | billig: die Annotation steht im Text; `{ it > 1 }` bleibt mehrdeutig |
| B: Lambda wird gegen jeden Kandidaten gleicher Stelligkeit typisiert; Kandidaten, bei denen der Rumpf nicht zum Rückgabetyp passt, scheiden aus | **Swift**, C# | Rumpf wird n-mal typisiert; ein Typfehler im Rumpf wird zu „kein Kandidat" mit einer Meldung, die alle Versuche nennen muss |
| C: Rückgabetyp zählt nur, wenn der Rumpf **ohne Kontext** typisierbar ist (Bare/Paren mit annotierten Parametern) | — | zwei Klassen von Lambdas in der Auflösung — schwer zu erklären |

**Empfehlung: A sofort (mit FN28-B), B als Entscheidung der Überladungsrunde** — mit der Zusage,
dass die Meldung bei null Treffern jeden Kandidaten und seinen Rumpf-Fehler nennt. Ohne die zweite
Hälfte ist B die C#-Falle. **Bricht:** nein (macht Mehrdeutiges eindeutig). **Konfidenz:** gemessen
(r08, r08b, r08c). **Hängt an:** FN28, FN35, Überladung, Diagnostik.

### FN39 — Was ist ein `defer`-Block im Rahmenmodell? *(neu, mit Befund 8)*

**Heute, gemessen:**

| Konstrukt im `defer` | Ergebnis | Probe |
|---|---|---|
| Lesen einer später geänderten `var` | sieht den **Endwert** (2) — kein Kopieren, kein eigener Rahmen | r11 |
| `return 3;` (int-Funktion) | **Compiler-Stack-Overflow**, exit 127, `build` **und** `check`, kein Code | r11b (2×) |
| `return;` (void-Funktion) | `CLI0020: block bb3 is already sealed` | r11g |
| `break` in einer Schleife | `CLI0020: block bb6 is already sealed` | r11c |
| `this` in einer Methode | läuft (druckt, exit 4) | r11e |
| `yield` in einer Koroutine | **läuft**: 1, 9, `end` — der `defer` läuft beim Ende des Koroutinenrumpfs und darf yielden | r11f2; Kontrolle r11i |
| `defer` in einem Lambda | bindet an das Lambda: `before body d after 1` | r11d |

`DeferStmt = 'defer' ( Block | Expr ';' )` (gelesen `docs/Grammar.md:392`); die §12.5-Uhr `SEM0110`
gilt nur für `throw` im `defer` (gelesen `docs/guide/19-diagnostics.md:64`); `PLAN.md:108` kennt
„`defer { throw }` in `try`" als Rest-ICE, **nicht** `return`/`break`.

| Option | Vorbild | Preis |
|---|---|---|
| A: **`defer` ist kein Rahmen, sondern Code des umgebenden Rahmens, der am Ende läuft**; `return`/`break`/`continue` darin sind **Sema-Fehler**, `this`/`yield`/Zugriff auf Lokale gelten wie im Rumpf | **Zig** (`defer` darf kein `return` enthalten: „cannot return from defer expression") | eine Sema-Regel; schreibt r11/r11e/r11f2 fest |
| B: `defer` ist ein eigener Rahmen wie ein Lambda (`return` verlässt den `defer`) | **Go** (`defer` ist ein Funktionsaufruf; ein `return` darin verlässt die deferred Funktion, nicht die umgebende) | ein `return` ohne Wirkung, den niemand will; `yield` im `defer` wäre dann FN25 |
| C: `return` im `defer` **überschreibt** das Ergebnis | Swift? nein (verboten); Java `finally` (überschreibt, berüchtigt) | die Java-Falle |

**Empfehlung: A (Zig), als Teil der Rahmenregel FN24** — `defer` ist das sechste Konstrukt dort. Und
**sofort, 4.x:** Befund 8 ist ein Prozesstod aus einem Nutzerfehler; er gehört nach `PLAN.md` §B,
das als leer gilt (gelesen `STATUS.md:1690–1696`). **Bricht:** nein (heute ICE/Absturz).
**Konfidenz:** gemessen (r11–r11i). **Hängt an:** FN24, Statements, Koroutinen, Diagnostik.

### FN40 — Überlebt eine gefangene Zelle ein `yield`? *(neu)*

**Heute:** ja — `var n` in einer Koroutine, von einem Lambda gefangen und über zwei `yield` hinweg
inkrementiert, liefert 1 2 (r12). Weder FN16 noch das Koroutinen-Kapitel formulieren das als
Zusage; `docs/guide/11-coroutines.md:146` sagt nur, dass ein nativer/JIT-Frame zwischen `yield`
und `resume` eine Wand ist.

| Option | Vorbild | Preis |
|---|---|---|
| A: **Spec-Satz**: die Umgebungszelle ist Teil des Koroutinenzustands; eine Closure über eine Koroutinen-Lokale sieht nach `resume` denselben Wert | Python (Generator-Frame hält seine Locals), C# (Zustandsmaschine hebt Locals in Felder), Kotlin | Lyricpp (das zweite Runtime) **muss** es nachbauen — Zellen leben im Heap, nicht im Frame |
| B: Implementierungsdetail, keine Zusage | — | ein Programm wie r12 kann in Lyricpp anders laufen |

**Empfehlung: A.** Die Zelle ist heute ohnehin ein Heap-Objekt (`newobj <cell>`, §1.6) — die Zusage
kostet den Interpreter nichts und schützt genau den Fall, den ein Frame-basiertes zweites Runtime
falsch machen könnte. **Bricht:** nein. **Konfidenz:** gemessen (r12). **Hängt an:** FN16,
Koroutinen, Lyricpp-Konformanz.

### FN41 — Verschattet ein Lambda-Parameter still? *(neu)*

**Heute:** `let n = 1; let f = (n: int) => n + 1;` → keine `SEM0107`, keine Warnung, Ausgabe 2
(r13). Kontrolle r13c: eine Block-Lokale `n` über einer äußeren `n` ist ebenfalls warnungsfrei
(nur `SEM0071`) — konsistent mit `docs/guide/19-diagnostics.md:61`: `SEM0107` gilt für **einen**
Scope, „shadowing an *enclosing* scope … stays legal". Ein Lambda-Parameter ist also ein neuer
Scope. Für `it` gilt dasselbe in Richtung innen (q02: äußere Lokale `it` wird verdeckt, `SEM0071`).

| Option | Vorbild | Preis |
|---|---|---|
| A: Lambda-Parameter = neuer Scope, Verschatten legal und still (Ist-Stand festschreiben) | Kotlin (Warnung „name shadowed", aber legal), Rust, Swift | eine Falle bei `it` und bei gleichnamigen Parametern bleibt still |
| B: Lambda-Parameter zählt für die Shadowing-Uhr wie eine zweite Bindung im selben Scope | — | widerspricht der Uhr-Definition (ein Scope) |
| C: Verschatten legal, aber **Warnung** (kein §12.5), für Parameter **und** `it` | Kotlin (`NAME_SHADOWING`-Warnung) | Lärm in Code, der `x` überall nennt; abschaltbar? |

**Empfehlung: A, und die Antwort in die Shadowing-Entscheidung (`SPEC-RUNDE` 4) eintragen** —
nicht als neue Uhr, weil sich die Bedeutung nicht ändert, egal wie 5.0 entscheidet. Für `it`
FN09-A (Hinweis in `SEM0071`). **Bricht:** nein. **Konfidenz:** gemessen (r13, r13c, q02).
**Hängt an:** FN09, Scoping (`SPEC-RUNDE` 4).

### FN42 — Was kostet ein `callind` in Zeit, und wogegen wird FN17-A+ abgenommen? *(neu)*

**Heute, gemessen (r14, Release, 3 M Aufrufe, je 3 Läufe, Start 0.27 s abgezogen):** Basisschleife
≈ 0.38 µs/Iteration, direkt (inlined) ≈ 0.53 µs, devirtualisiert ≈ 0.54 µs, **`callind` ≈ 0.68 µs**.
Ein `callind` kostet ≈ 130–190 ns über dem eingelagerten Ruf — **gut ein Viertel** der Iteration,
kein Vielfaches (das Audit maß auf schnellerer Maschine 35–50 ns von ≈ 250 ns, ein Sechstel). Auf
der Iterator-Kette sind es pro Element **zwei** solcher Rufe (Befund 7): grob 250–350 ns pro Element
und Stufe im Interpreter. **Unter `--jit`:** direkt 46 ns/Iteration, `callind` unverändert 0.68 µs
— Faktor ≈ 16.

| Option | Vorbild | Preis |
|---|---|---|
| A: Abnahme über **Instruktionszahlen** (`callvirt`/`callind` in `main` = 0) | heutige Praxis der Optimierer-Befunde | misst das Mittel, nicht den Zweck |
| B: Abnahme über einen **Benchmark** (`over(xs).map{}.filter{}` über 1 M Elemente, Interpreter **und** `--jit`), Ziel in ns pro Element | Go (`benchstat`), Rust (`criterion`) — jeder Optimierer-PR trägt eine Zahl | ein Benchmark-Harness, das es nicht gibt; Lärm auf einer Maschine, die parallel baut |
| C: kein Ziel | — | „teuerster Pfad" bleibt Behauptung |

**Empfehlung: B**, Ziel: Kette ≤ 1.5× der handgeschriebenen Schleife im Interpreter, und
**JIT-kompilierbar** — das zweite ist die eigentliche Messlatte, weil der JIT die Kette heute ganz
ablehnt. **Bricht:** nein. **Konfidenz:** gemessen (r14). **Hängt an:** FN17, FN43, Optimierer-Runde,
VM.

### FN43 — Closures, `callind` und Tiefe unter `--jit` *(neu)*

**Heute:** Ergebnisse identisch (r15: q16 → 1 2 1, n16 → `VM0004`, a11 → 20), **Leistung nicht**:
der JIT lehnt Closures ab (gelesen `JitCompiler.cs:35–36`), also läuft jede Funktion mit
`mkclosure`/`callind` interpretiert (Befund 9: `t_ind` 2.22 s mit und ohne `--jit`); die
Ablehnung ist per Funktion und kostet „speed, never correctness" (gelesen ebd. 30–32). Kein FN der
Fassung 2 nannte den JIT; FN17/FN30 sprachen nur vom IR-Optimierer.

| Option | Vorbild | Preis |
|---|---|---|
| A: Semantik-Zusagen (FN16, FN24, FN31, FN40) gelten für Interpreter **und** JIT; die Differenzialtests sind der Beweis | heutige JIT-Praxis (gelesen `JitCompiler.cs:31–33`: „differential tests holding the line") | jede neue Zusage braucht einen Differenzialtest |
| B: Leistung: FN17-A+ muss die Kette so weit auflösen, dass **kein `callind` in `main` bleibt** — dann kompiliert der JIT sie | — | die Optimierer-Runde bekommt ein JIT-Ziel, nicht nur ein Interpreter-Ziel |
| C: der JIT lernt `callind` (Aufruf über Index + Umgebung als Argument 0) | HotSpot (indirekte Rufe mit Inline-Cache) | ein Opcode mehr im JIT; ohne Devirtualisierung bleibt er ein indirekter Ruf |

**Empfehlung: A und B; C als eigene JIT-Frage.** Wer misst: das Optimierer-Milestone mit dem
Benchmark aus FN42, beide Modi. **Bricht:** nein. **Konfidenz:** gemessen (r14, r15), gelesen
(`JitCompiler.cs`); warum `t_virt` nichts gewinnt, **behauptet**. **Hängt an:** FN17, FN42, VM/JIT.

### FN44 — In welcher Reihenfolge und wie oft werden Defaults ausgewertet? *(neu)*

**Heute:** bei jedem Aufruf neu, nur für weggelassene Argumente, **Argumente zuerst (links nach
rechts), dann fehlende Defaults in Deklarationsreihenfolge** (r16: `f()` → a b; `f(1)` → b;
`f(side("x"))` → x b; p36 für Argumente). Nirgends spezifiziert.

| Option | Vorbild | Preis |
|---|---|---|
| A: Zusage = Ist-Stand (Argumente, dann Defaults in Deklarationsreihenfolge) | Kotlin (Defaults in Deklarationsreihenfolge, im Callee), Swift | FN02-B muss die Reihenfolge erhalten: der Generator läuft **nach** allen Argumenten |
| B: Deklarationsreihenfolge **gemischt** (Default von `a` vor Argument `b`, wenn `a` fehlt) | Scala (`f$default$1` wird vor dem Aufruf, aber in Parameterreihenfolge gerufen — je nach Aufrufstelle) | ändert r16 (`f(side("x"))` → würde `a`? nein, `a` ist gegeben — der Fall ist `f(1, c = …)` mit fehlendem `b`: heute „Argumente, dann b", unter B „1, dann b, dann c") |
| C: nicht spezifizieren | — | FN02-B darf sie still ändern |

**Empfehlung: A**, und mit FN03 die Ergänzung: darf ein Default einen früheren Parameter nennen,
sieht er dessen **Argument oder ausgewerteten Default**. **Bricht:** nein. **Konfidenz:** gemessen
(r16). **Hängt an:** FN02, FN03, FN27.

### FN45 — Was bedeutet `h.f`, wenn `f` ein Feld vom Funktionstyp ist? *(neu)*

**Heute:** `h.f(1)` ruft das Feld (→ 2), `let g = h.f` ist der Feldwert (→ 11, r17); ein Feld
bekommt kein `this`. Feld und Methode gleichen Namens sind `RES0001` (r17b) — die Mehrdeutigkeit
ist an der Deklaration ausgeschlossen. Die Aufrufsyntax ist von einem Methodenaufruf nicht
unterscheidbar.

| Option | Vorbild | Preis |
|---|---|---|
| A: `obj.name` ist Feld **oder** Methode (RES0001 hält sie getrennt); ein Methodenwert (FN12) ist ein gebundener Wert **mit** `this`, ein Feldwert nicht — als Satz in der Spezifikation | Kotlin (`obj::m` vs. Feld — Kotlin verlangt `(obj.f)(1)` für ein Feld vom Funktionstyp, wenn eine Methode `f` existiert), Python | nichts heute; FN12 bekommt seine Regel |
| B: Felder vom Funktionstyp brauchen `(h.f)(1)` | Kotlin (bei Kollision) | bricht r17 (läuft heute) |
| C: Methoden dürfen Felder verdecken | JS | die Falle, die RES0001 heute verhindert |

**Empfehlung: A.** **Bricht:** nein. **Konfidenz:** gemessen (r17, r17b). **Hängt an:** FN12, FN19,
Typen/Member.

### FN46 — Welche Zählung ist die verbindliche Vorbedingung vor einer §12.5-Uhr? *(neu)*

**Heute:** die vier bestehenden Uhren wurden vor der Aufnahme über `examples/`, `stdlib/`,
`stdlib-tests/` gemessen (gelesen `STATUS.md:50–56`). Fassung 2 verlangte das für ihre drei
Kandidaten und führte es nicht aus; gemessen jetzt (grep): `params`-Aliasing 0, `params` nach
Default 0, Zuweisung als Wert 0 (Regex). Abweichende Interface-Defaults (FN36): **nicht gezählt**.

| Option | Vorbild | Preis |
|---|---|---|
| A: `examples/` + `stdlib/` + `stdlib-tests/` per **grep** | heutige Praxis | Regex zählt `x => ` als Zuweisung; Falsch-Negative bei mehrzeiligen Aufrufen |
| B: dieselben Bäume **plus die Konformanz-Suite** (`lyric-spec`), per **Sema-Lauf mit der Regel im Probemodus** (die Uhr einmal als Fehler bauen und zählen) | Rust (`crater`), Go (`gofmt -d` über den Baum) | ein Lauf pro Uhr; braucht die Regel vor der Zählung — was ohnehin die Reihenfolge ist |
| C: nur stdlib | — | zählt nicht, was Nutzer schreiben |

**Empfehlung: B**, festgehalten als Aufnahmebedingung in §12.5. **Bricht:** nein. **Konfidenz:**
gemessen (Zählungen), gelesen (`STATUS.md`). **Hängt an:** FN33, FN36, `lyric-spec` §12.5.

### FN47 — Ist `MaxReentryDepth = 32` eine Spec-Zusage oder ein Implementierungsdetail? *(neu)*

**Heute:** Konstante (gelesen `Interpreter.cs:177`, „MEASURED, then halved": 64 überlebt, 96 stirbt,
also 32), Panik `CallDepthExceeded` mit eigener Meldung („re-entry nested 32 runs deep …", gelesen
ebd. 192–195), Test `ReentrancyTests.cs:44–66`. **Kein Guide-Kapitel nennt die Zahl** (`grep -rn
-i reentr docs/guide/` leer). Ein Host, der 33-fach verschachtelt zurückruft, bekommt heute eine
Panik, die er nirgends angekündigt fand.

| Option | Vorbild | Preis |
|---|---|---|
| A: Konstante bleibt, **dokumentiert** in Guide 14 (Embedding) und in der Panikmeldung mit dem Rat aus dem Kommentar („write the recursion inside the script") | Lua (`LUAI_MAXCCALLS` compile-time, dokumentiert) | Host kann nichts tun — was der Kommentar für richtig hält |
| B: Feld in der Embedding-API (`ExecutionBudget.MaxReentry`) | — | ein Host stellt 200 ein und stirbt am CLR-Stack ohne Panik — genau der alte Befund |
| C: Spec-Zusage („mindestens 32") | — | bindet Lyricpp (anderer Stack, andere Zahl) |

**Empfehlung: A** — und **nicht** in `ExecutionBudget`, weil die Zahl den CLR-Stack schützt, den
kein Host vergrößern kann; für Lyricpp gilt eine eigene, gemessene Zahl. `--max-depth` (FN31)
bewegt sie nicht, und die Meldung sagt das. **Bricht:** nein. **Konfidenz:** gelesen. **Hängt an:**
FN31, FN29, Embedding-Guide.

### FN48 — Wie wird ein Trace über 1000 anonyme Lambda-Frames lesbar, und ist `<lambda1>` stabil? *(neu)*

**Heute:** die Panik nennt `main.main.<lambda1>` ≈ 1024-mal mit Zeile (r19); der Name ist generiert
und steht in keinem Quelltext; die Nummer ist die Reihenfolge der Lambdas in der Funktion (q09:
`<lambda0>`, r19: `<lambda1>` — das zweite Lambda in `main`). Ob der Name über Releases stabil ist,
sagt nichts; Pins der Konformanz-Suite, die Panik-Texte vergleichen, hängen daran.

| Option | Vorbild | Preis |
|---|---|---|
| A: Name der **Bindung**, wenn das Lambda direkt einer `let`/`var` zugewiesen wird (`main.main.f`), sonst `<lambda n>` | Kotlin (`main$lambda-0`, aber `invoke` in Traces), Swift (`closure #1 in main()`), C# (`<Main>b__0_0`, Roslyn-intern, **nicht stabil**) | `f = …` in r19 ist eine `var`-Neuzuweisung — der Name wäre `f`, hilfreich; zwei Lambdas an eine `var` teilen den Namen |
| B: `<lambda n>` bleibt, plus **Zeile:Spalte** (heute nur Zeile) und Trace-Kürzung (FN18-A) | Python (`<lambda>` + Zeile) | keine Stabilitätszusage nötig |
| C: Name = Aufruferfunktion + Zeile, **spezifiziert** | — | bindet die Nummerierung |

**Empfehlung: B, mit dem Satz, dass `<lambda n>` **kein** Pin-fähiger Text ist** — Pins vergleichen
Codes und Positionen, nicht generierte Namen. A ist nett, aber es hängt an FN15 (lokale `fn` hat
einen Namen; wer einen Namen im Trace will, deklariert eine). **Bricht:** nein. **Konfidenz:**
gemessen (r19, q09). **Hängt an:** FN18, FN32, Diagnostik, Konformanz-Suite.

### FN49 — Was ist `it` in einem Paren-Lambda innerhalb eines Trailing-Lambdas? *(neu)*

**Heute:** ein gewöhnlicher Name, der wie jede Bindung von inneren Lambdas **gefangen** wird:
`run(5) { let g = (n: int) => { return it + n; }; return g(1); }` → 6 (r20b). Die Form ohne
`return` (r20) scheitert nur an der Trailing-Block-Regel (Statement-Block ohne Tail, r20c), nicht
an `it`.

| Option | Vorbild | Preis |
|---|---|---|
| A: `it` ist ein Parameter des Trailing-Lambdas und wird gefangen wie jeder Parameter — Ist-Stand festschreiben | Kotlin (`it` ist ein Parameter, innere Lambdas fangen ihn; ein inneres Trailing-Lambda hat sein eigenes `it`) | nichts |
| B: `it` ist nur im äußersten Block sichtbar | — | bricht r20b |

**Empfehlung: A**, als ein Satz in FN09/`design/lambdas.md`. **Bricht:** nein. **Konfidenz:**
gemessen (r20b, p33). **Hängt an:** FN09, FN16.

---

## 4. Was wir übernehmen sollten

| Von | Was | Warum hier |
|---|---|---|
| **Kotlin** | Callee-seitiger Default und „`override` setzt keinen Default" | FN02, FN03, FN04, FN36 |
| **Kotlin** | Trailing-Lambda bindet an den **letzten** Funktionsparameter | FN35-D macht `repeat(5) { }` schreibbar ohne benannte Argumente |
| **Kotlin** | `{ a, b -> … }` | FN08 |
| **Kotlin** | `*arr`-Spread mit Kopie | FN05 — im Baum bricht es nichts (gemessen) |
| **Kotlin/Swift** | Lambda-Stelligkeit trennt Überladungen | FN28 |
| **Swift** | Closures werden pro Kandidat typisiert | FN38-B — die Inferenz existiert schon (r08) |
| **Swift** | `throws(E)` im Funktionstyp, einseitige Zuweisbarkeit | FN14 — zusammen mit der Varianz aus FN37 |
| **Swift** | Zuweisung ist `void`-wertig | FN26-B' (Swift allein; Kotlin ist B) |
| **C#** (Delegatvarianz), **Kotlin** (`in`/`out`) | Varianz nur über den Subtyp, den die Sprache hat | FN37-B |
| **C#** | `yield` im Lambda ist verboten (CS1621) | FN25-B hat damit ein imperatives Vorbild |
| **Zig** | `defer` darf kein `return` enthalten | FN39-A |
| **Python / C#** | Generator-Locals überleben `yield` als Zusage | FN40 |
| **Haskell** | Effekt vollständig im Typ | FN14 |
| **Rust/C#/Swift/Kotlin/Scala/Python/Haskell** | lokale Funktionen | FN15 |
| **Rust** | `#[must_use]` | FN21 |
| **Go/Rust** | Benchmark pro Optimierer-PR | FN42 |
| **Python** | `<lambda>` + Zeile, kein stabiler Name | FN48 |
| **Lua** | dokumentierte compile-time Reentranzgrenze | FN47 |
| **Zig** (Gegenentwurf) | Funktionswert ohne Umgebung | FN16-D, FN30 |

**Nicht übernehmen:** Rubys Block/`Proc`/`lambda` · Scalas `_` · Swifts Pflicht-Labels · Kotlins
`inline fun` mit nicht-lokalem `return` · Rusts `Fn`-Dreiteilung · Pythons Default bei der
Deklaration · Pythons „`yield` macht das Lambda zum Generator" · C#' `static`-Lambda als
Allokationsmarker (es ist keiner) · Javas `finally`, das ein `return` überschreibt (FN39-C) ·
Kotlins B-Variante der Trailing-Bindung ohne Erweiterung (bräche r01).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2:**

1. Drei Lambda-Schreibweisen (FN07) — ein Konzept, drei Stufen, braucht Satz **und** Kapitel
   (FN32); und der Rumpf hat heute zwei Regeln (Tail im Paren-Block ja, im Trailing-Block nein,
   r20c), die der ValueBlock auf eine bringt.
2. Methodenwerte (FN12) neben Lambdas — vertretbar, mit der `this`-Regel aus FN45.
3. `inline fn` (FN17-B) — abgelehnt; dasselbe für FN24-B.
4. Benannte Argumente (FN01) als drittes Mittel neben Position und Options-Struct.
5. Zwei Wege zu einem Default (FN02) — B ersetzt, ergänzt nicht.
6. `params` nach Default (FN23) — toter Zustandsraum, im Baum leer.
7. **Zwei Tiefenlimits** (FN31/FN47) sind kein Rule-2-Verstoß — sie messen zwei Dinge (Frames vs.
   CLR-Stack) — aber sie brauchen **eine** Meldung, die beide nennt.
8. **Zwei Varianzregeln** an einem Typ (FN14-A und FN37-B) — müssen eine Entscheidung sein.

**Mit anderen Gebieten:**

| Konflikt | Gegenseite |
|---|---|
| FN14 ist typed throws St. 3; FN37 hängt daran | Fehlerbehandlung, Typsystem |
| FN26 (Zuweisung `void`) | Statements/Ausdrücke, Operatoren |
| FN05-B mit Slices/Views | Arrays |
| FN06 Inferenzregel | Generics §8.3 |
| FN12/FN45 `this`-Bindung, RES0001 | Typen/Member |
| FN15/FN41 Shadowing | Scoping (`SPEC-RUNDE` 4) |
| FN02-B Export-Roots, Inliner über Importe, FN44 Reihenfolge | Module/ABI, Build/Pakete |
| FN16/FN30/FN40 Zellen, Koroutinenzustand | Werte/Veränderlichkeit, Koroutinen, **Lyricpp** |
| FN17/FN30/FN42/FN43 | **VM/Optimierer/JIT** — die Optimierer-Runde bekommt ein JIT-Ziel |
| FN25/FN39 `yield`/`defer` | Koroutinen, Statements |
| FN29/FN31/FN47 | FFI/Embedding, VM |
| FN32/FN48 | Editor-Werkzeuge, CLI, Diagnostik |
| FN33/FN34/FN36/FN46 | Diagnostik, Spec/Konformanz, stdlib |
| FN36 (Defaults als Vertrag) | Interfaces/Konformanz |

**Mit dem Prozess:** Befunde 1–3 und **8** sind Fehler. Befund 8 (Compiler-Stack-Overflow aus
`defer { return }`) widerspricht „B ist leer" (gelesen `STATUS.md:1690–1696`) — `PLAN.md:108` nennt
nur `defer { throw }`. Befund 3 ist kleiner als Fassung 2 sagte: eine CI mit `check --emit` fängt
Befund 2, und der Guide nennt den Schalter (gelesen `docs/guide/01-getting-started.md:42`: „`lyric
check <file> --emit` runs that"). Was bleibt, ist ein Diagnostik-Posten: `check` ohne `--emit`
sagt „ok" zu einem Programm, das `build` ablehnt — der Guide erklärt es, der Befehl warnt nicht.

---

## 6. Nach der Kritik geändert

**Falsch bestätigt und korrigiert (Kritiker hatte recht) — je nachgemessen:**

- **FN22** — Default + Trailing ist **schreibbar**, wenn der `fn`-Parameter vorn steht (r01 → 3 3 6);
  das Trailing bindet an den nächsten freien Parameter (r01b). Option B (Kotlin-Weg über benannte
  Argumente) gestrichen als Empfehlung; neue Bindungsfrage **FN35**, Empfehlung D.
- **FN04** — die Konformanzprüfung ignoriert Defaults **messbar** (r02b/c/d), der Direktaufruf nimmt
  den Default des statischen Typs. Option C bekommt Kotlin/Dart statt Java (Java kennt keine
  Defaults). Bruchgrad **major** statt minor. Neue Vorfrage **FN36** (Konformanzdiagnose), die vor
  FN02-B/FN04-B stehen muss.
- **FN24 / Befund 3** — `check` sieht das Lowering (r03/r03b: `CLI0020`, exit 1; gelesen
  `SourceCompiler.cs:28–32`, `AnalysisService.cs:553`); `check --emit` fängt Befund 2 (r03e).
  Begründung für die Sema-Regel neu: eine ICE ist keine Diagnose, `yield` fängt niemand.
- **FN29 / FN31** — Reentranz ist erledigt (`STATUS.md:1690–1696`, Commit `a27990c9`,
  `_outerFrames` thread-weit); zweites Limit `MaxReentryDepth = 32` aufgenommen → **FN47**.
- **FN18** — Trace: 1026 stderr-Zeilen (nicht 2048), Panik im `println`-Frame (r04).
- **FN05 / FN23** — `params` im Baum gezählt: stdlib 0, examples 1 ohne Durchreichen, stdlib-tests 0.
  FN05-B bricht nichts, FN05-D bricht keine stdlib-Signatur; FN23 „behauptet" → gemessen.
- **FN01 / §1.1** — 19 Diagnosen mit 7 Codes (r05), nicht „sieben Folgefehler".
- **§1.7 / FN17** — 4/2 gehörte zu zwei Ketten; saubere Zahl 2/2 für eine Kette (r06), pro Element
  1 + 1. **Neu: Zeitmessung** (r14): ein Viertel im Interpreter, Faktor ≈ 16 unter `--jit`.
- **FN21** — `until` existiert seit 3.x (`docs/guide/15-attributes.md:284–301`).
- **FN27** — Globale werden nicht gefangen (r07b `ldglobal`), veränderliche gibt es nicht (r07
  `PAR0027`); Option A auf „frühere Parameter" reduziert.
- **FN25** — Rückgabetyp fließt aus dem Rumpf (r08: `U` inferiert); die Begründung „Kontext gibt den
  Typ" präzisiert.
- **FN28** — gleiche Stelligkeit, anderer Rückgabetyp ist `SEM0086` auch **mit Annotation** (r08c);
  Option C neu bewertet, → **FN38**.
- **Befund 5 / FN32** — `examples/lambdas/shorthand.lyr` lehrt alle drei Formen; `lyrfmt` behält sie
  (r22). Fertigstellungsbedingung zählt `examples/` mit.
- **§1.3** — q12 ist `SEM0025` + 2× `SEM0045` (r09).
- **Pfad** Prototyp 12: `docs/Befunde_und_Verbesserungen/prototypes/12-named-arguments/README.md`.

**Vergleichssprachen korrigiert:**

- **Kotlin**: Zuweisung ist eine **Anweisung**, kein Ausdruck → FN26-B, nicht B'. B' hat nur Swift.
- **C#**: `yield` in Lambdas ist **verboten** (CS1621) → Vorbild für FN25-B, nicht A. **JS**
  `function*` ist explizite Syntax, Arrow-Funktionen sind keine Generatoren → FN25-C. **Python**
  erlaubt `yield` sogar in `lambda` → allein bei A.
- **Java** kennt keine Default-Argumente → kein Vorbild für FN04-C; Kotlin/Dart ersetzt.
- **Haskell** rank-1: nur für Funktionsbindungen oder mit Signatur; die Monomorphism Restriction
  hält ein argumentloses `let f = show` monomorph (§2.4, FN13-C).
- **Ruby**: MRI hat TCO als Compile-Option (`tailcall_optimization: true`), standardmäßig aus —
  „nein" nur als „nicht per Default" (§2.5).
- **C# `static` lambda** ändert das Allokationsverhalten nicht; nicht fangende Lambdas werden
  ohnehin einmal allokiert und gecacht (FN30-C).
- **Swift** `Array` ist ein Werttyp (COW) — die Aliasing-Spalte in §2.2 vergleicht deshalb
  Ungleiches; für FN05 ist das der entscheidende Unterschied.

**Was gegen die Kritik stehen bleibt:**

- **FN17 Empfehlung A + A+** bleibt — mit neuer, stärkerer Begründung: nicht „teuerster Pfad",
  sondern „einziger Pfad, den der JIT ablehnt" (Befund 9).
- **FN24-A (Sema-Regel)** bleibt — nur die Begründung ist neu.
- **FN33 Uhren FN26/FN05/FN23** bleiben zulässig (nach `STATUS.md:50–56` darf eine Uhr nie feuern);
  gestrichen ist nur die Priorität ohne Datenbasis; **FN36** kommt dazu.
- **§1.1 Default-Auswertung „an der Aufrufstelle, bei jedem Aufruf"** bleibt (r16 bestätigt es
  und präzisiert die Reihenfolge).

**Neue Designfragen (15), FN35–FN49**, jede mit Ist-Stand, Optionen, Empfehlung, Bruchgrad,
Konfidenz und Abhängigkeiten — davon **eine mit neuem Befund** (FN39/Befund 8: `defer { return }`
bringt den Compiler um) und **zwei mit Zeitmessung** (FN42, FN43).

**Belegstand dieser Fassung:** 40 Proben in `funktionen-rev3/` (r01–r22 mit Varianten,
t_base/t_direct/t_ind/t_virt), Erwartungen vorab in `ERWARTUNGEN.txt`; **Abweichungen von der
Erwartung:** r11b (Prozesstod statt Diagnose), r20 (`SEM0046` statt 6 — Trailing-Block ohne Tail,
Kontrolle r20b/r20c), r14/`--jit` (t_ind und t_virt ohne Gewinn — Kontrolle: Basisschleife,
Startup, `JitCompiler.cs:35`). Sechs Kontrollläufe (r01c, r02e, r10b, r11h, r11i, r13c, r20c).
**Behauptet** bleiben: warum `t_virt` unter `--jit` nichts gewinnt; die Zahl der abweichenden
Interface-Defaults im Baum (FN36); die Kosten eines Default-Generators an nicht-`pub`-Funktionen
(FN02). (Dass der Guide `check --emit` nenne, war zuerst „behauptet" und ist jetzt gelesen:
`docs/guide/01-getting-started.md:42`.)
