# Lyric 5 — Gebiet: Testen, Messen, Abdeckung

Belege: `gemessen` = Probe gelaufen (unter `scratchpad/v5-design/probes/testen/`), `gelesen` =
Pfad:Zeile, `behauptet` = ausdrücklich so markiert. Fremdsprachen-Aussagen sind Faktenbehauptungen
über deren Werkzeuge, keine Messung.

Zeilenangaben in `STATUS.md` sind auf den Stand vom **2026-09-24** (2572 Zeilen, nach PR #173)
nachgeprüft. Die Fassung dieses Dossiers vor der Kritik zitierte einen Stand vor PR #172 und war
durchgehend um rund 19 Zeilen verschoben — für ein Dokument, das „gelesen = Pfad:Zeile" zur
Belegform erklärt, war das der Beleg selbst, der nicht mehr trug.

---

## 1. Ist-Stand

### 1.1 Was existiert

| Teil | Wo | Umfang |
|---|---|---|
| Runner `lyrtest` | `src/Lyrtest/Program.cs` (230 Zeilen) | sechs Optionsnamen: `--filter`, `--profile`/`--debug`/`--release`, `--stdlib`, `--version`, `--help` (gemessen, `--help`) |
| Marker `@Test` | `stdlib/std/test.lyr:17` — `pub struct Test :: [OnFunction] { }` | ein leeres Attribut, vom Compiler **nicht** gelesen |
| Assertions | `stdlib/std/test.lyr:23–93` | 9 Funktionen |
| Verb `lyric test` | `src/Lyric.Cli/Program.cs:38` | leitet nur weiter |
| Verb `lyric check` | `src/Lyrc/Program.cs:300–351` (`CheckProject`) | kompiliert **Quellwurzel und Testwurzel** als je eine Einheit |
| Konformanzsuite | `lyric-spec/conformance/`, Runner `tools/run_conformance.py` (153 Zeilen) | **178 Falldateien, 177/177 bestanden, 1 übersprungen** (gemessen, s. u.) |
| Goldens | `tests/Lyric.Tests.{Ir,Lexing,Parsing,Resolver}/GoldenTests.cs`, `Lyric.Tests.DocGen/ExtractorTests.cs` | C#/xUnit, `LYRIC_UPDATE_SNAPSHOTS=1` (CONTRIBUTING.md:137) |
| Benchmarks | `tools/Bench/Program.cs` | C#-Harness, läuft ganze Lyric-Programme in-process |

**Die Suitezahl, neu gemessen.** `find lyric-spec/conformance/cases -name '*.lyr' | wc -l` → **178**.
Der Referenzrunner gegen diesen Baum, mit den gebauten Debug-Binaries und
`--toolchain-version 4.6.0`, meldet **`177/177 passed, 1 skipped`**; übersprungen wird der eine Fall
mit `until:` (gemessen: `grep -rl "until:" cases | wc -l` → 1), 21 Fälle tragen `since: 4.6.0` und
laufen auf 4.6.0 alle. Die alte Angabe des Dossiers („170 Falldateien, 159 laufen, STATUS.md:47")
war doppelt falsch: die Zahl stammt aus **`STATUS.md:66`** („as 4.6.0 … the suite is 159/159"), wo
sie einen **früheren Stand innerhalb des 4.6-Zyklus** beschreibt, und `STATUS.md:47` sagt nichts
dergleichen. Nebenbefund: damit ist auch `STATUS.md:66` gegenüber dem Spec-Baum veraltet — die
Suite ist seither um 19 Fälle gewachsen.

Der Ablauf von `lyrtest` (gelesen, `src/Lyrtest/Program.cs:108–195`): alle `*.lyr` unter
`testRoot` rekursiv, `StringComparer.Ordinal` sortiert · **eine VM pro Datei** ·
`vm.CompileFile(file)` einmal · `module.Attributes.OnFunctions("Test")` · je Test
`vm.Instantiate(module).CallVoid(test)`.

### 1.2 Zwei Werkzeuge derselben Kette widersprechen sich — dreimal gemessen

Das ist der **größte Einzelbefund dieses Gebiets**, und die Fassung vor der Kritik hatte ihn nicht:
sie behauptete an drei Stellen, `tests/` werde „nur von `lyrtest` kompiliert". Das ist falsch.
`lyric check` kompiliert die Testwurzel ebenfalls — **als eine Einheit, mit pfadbasierten
Modulnamen**, während `lyrtest` Datei für Datei mit Basisnamen arbeitet. Der Quelltext von
`CheckProject` sagt die Absicht sogar ausdrücklich (gelesen, `src/Lyrc/Program.cs:337–339`):

> „The root is the one lyrtest uses, the named one or the conventional `tests/` — a project whose
> tests are only found by one of the two tools would be worse than none."

Genau das ist eingetreten, nur eine Ebene tiefer: beide Werkzeuge *finden* dieselben Dateien und
geben ihnen dann **verschiedene Modulwurzeln und verschiedene Namen**.

**(a) Die Testwurzel wird von `check` kompiliert.** Gemessen, `probes/testen/v2-r01/`: ein
`nosuchfunction()` in `tests/broken_tests.lyr`:

```
$ lyrc check .
…\tests\broken_tests.lyr:5:5: error[LYR-SEM0002]: unknown identifier 'nosuchfunction'
EXIT=1
```

KONTROLLE, derselbe Baum: `lyrc build src/main.lyr -o out.lyrbc` → Exit 0, 2770 Bytes. Die
Testdatei bricht also `check`, nicht `build`. Auch gelesen: `lyrc --help` („check [<dir>] Check a
whole project: its source root as one compilation, its test root as another") und
`docs/guide/20-testing.md:76–78` sagen es wörtlich. Das Dossier zitierte Guide 20 dreimal und
übersah den Absatz.

**(b) `check` erlaubt den Import zwischen Testdateien, `lyrtest` nicht.** Gemessen,
`v2-r02/`: `tests/helpers.lyr` mit `pub fn helperValue()`, `tests/use_tests.lyr` mit
`import helpers { helperValue }`:

```
$ lyrc check .      →  …\v2-r02: 3 modules ok        EXIT=0
$ lyrtest .         →  …\tests\use_tests.lyr:2:1: error[LYR-RES0003]: cannot find module 'helpers'
                       0 test(s), 1 FAILED           EXIT=1
```

**(c) Die beiden vergeben verschiedene Modulnamen.** Gemessen, `v2-r03/`:
`tests/alpha/shared_tests.lyr`, `tests/beta/shared_tests.lyr`, dazu `tests/zz_import_tests.lyr` mit
`import alpha.shared_tests { alphaValue }`:

```
$ lyrc check .      →  …\v2-r03: 4 modules ok        EXIT=0
$ lyrtest .         →  PASS shared_tests.one
                       PASS shared_tests.one
                       …zz_import_tests.lyr:2:1: error[LYR-RES0003]: cannot find module 'alpha.shared_tests'
                       2 test(s), 1 FAILED           EXIT=1
```

Und `--filter shared_tests.one` trifft beide (gemessen, derselbe Lauf).

**(d) Auch der geschriebene `module`-Header zählt verschieden.** Gemessen, `v2-r06/`:
`tests/bb_header_tests.lyr` mit `module deep.nested.name;`, dazu `tests/cc_user_tests.lyr` mit
`import deep.nested.name { shared }`:

```
$ lyrc check .      →  …\v2-r06: 3 modules ok        EXIT=0   (der Header gilt)
$ lyrtest .         →  PASS bb_header_tests.named              (der Basisname gilt)
                       …cc_user_tests.lyr:2:1: error[LYR-RES0003]: cannot find module 'deep.nested.name'
                       1 test(s), 1 FAILED           EXIT=1
```

KONTROLLE, im **Quellbaum** ist derselbe Widerspruch ein benannter Fehler (gemessen, `v2-r06b/`):
`src/thing.lyr` mit `module deep.nested.name;`, importiert als `thing` →
`error[LYR-RES0006]: this file was loaded as 'thing' but declares module 'deep.nested.name'`.
ZWEITE KONTROLLE: `lyrc check tests/bb_header_tests.lyr` als **Einzeldatei** → `ok`, Exit 0. Die
Stille ist also eine Eigenschaft des Einzeldatei-Pfads, den `lyrtest` benutzt, nicht ein Fehler von
`lyrtest`; der Diagnosecode für den Fall (`LYR-RES0006`) existiert bereits und wird nur nicht
gezogen.

**Folge für das ganze Gebiet.** T6 („Testwurzel als Modulwurzel") und T8 („Modulname aus dem Pfad")
sind damit **keine neuen Features mit einem Preis**, sondern die Beseitigung einer bereits
ausgelieferten Divergenz. Der Mechanismus existiert, läuft, ist getestet und liegt in derselben
Werkzeugkette — er wird von einem der beiden Werkzeuge nur nicht benutzt.

### 1.3 Was der Compiler am `@Test`-Vertrag prüft: nichts

Guide 20 sagt „a top-level function marked `@Test`, taking nothing and returning nothing"
(`docs/guide/20-testing.md:7`). **Keine Hälfte dieses Satzes wird geprüft** (gemessen, `v2-r16/`):

| Geschrieben | `lyrc check` | `lyrtest` |
|---|---|---|
| `@Test pub fn aa_returns_int(): int { return 7; }` | `2 modules ok` | **`PASS`** — der Rückgabewert wird still verworfen |
| `@Test fn bb_not_public(): void` (ohne `pub`) | `2 modules ok` | `PASS` — Sichtbarkeit ist irrelevant, Guide schreibt immer `pub` |
| `@Test pub fn cc_takes_a_param(n: int): void` | `2 modules ok` | `FAIL … [LYR-EMB0007] 'contract_tests.cc_takes_a_param' takes 1 argument(s), got 0` |

Die KONTROLLE ist die mittlere Spalte: `lyrc check` schweigt zu allen dreien. Die erste Zeile ist
der stille Fall — ein Test, dessen Autor den Rückgabewert für bedeutsam hielt, wird grün. Der
Fehler der dritten Zeile kommt aus der **Embedding-API**, nicht aus der Sprache
(`src/Lyric.Embedding/ScriptInstance.cs:157–160`), und erreicht den Benutzer als Testfehlschlag
statt als Compilerfehler.

Richtig geprüft wird nur die Platzierung: `@Test` auf einem Member ist `LYR-SEM0065`, auf einer
lokalen `fn` ein Parserkaskadenfehler (gemessen, `p11-placement/`), und auf einer **generischen**
Deklaration `LYR-SEM0067` (gemessen, `v2-r10/`, s. T27).

### 1.4 Der `@Test`-Name ist nicht Eigentum von `std.test`

Gemessen, `v2-r09/`. Eine Testdatei deklariert ihr **eigenes** Attribut, ohne jeden Bezug zu
`std.test`:

```lyr
import std.core { OnFunction };
pub struct Test :: [OnFunction] { }

@Test
pub fn my_own_marker(): void { println("this ran"); }
```

```
$ lyrtest .
this ran
PASS aa_own_tests.my_own_marker
PASS zz_control_tests.real_one          ← KONTROLLE, aus std.test
2 test(s), all passed
```

Der Grund ist gelesen: `module.Attributes.OnFunctions("Test")` (`src/Lyrtest/Program.cs:168`)
matcht auf einen **unqualifizierten Namen**, und `docs/guide/15-attributes.md:246` hält fest, dass
Attributnamen im kompilierten Modul unqualifiziert stehen („`System`, not `engine.ecs.System`").

Zwei Folgen: ein SDK, das ein eigenes `@Test` für etwas ganz anderes führt, bekommt seine
Funktionen von `lyrtest` ausgeführt — und **T7 Option B (`RequiresSignature<…>` auf dem
Attributstruct) schließt das Loch nicht**, solange der Runner nach dem Namen greift statt nach der
Konformanz.

### 1.5 Die sechs Löcher, die heute niemand umgehen kann

**(a) Kein Erwarten einer Panik oder einer Exception.** Zwei Messungen:

- `fn assertThrows(f: fn() -> void throws Boom): void` → `LYR-SEM0084` (gemessen,
  `p06-assertthrows/`). Genau der Grund, den `stdlib/std/test.lyr:95–99` selbst notiert.
- Eine Panik ist nicht fangbar: `try { panic("blown"); } catch (e: Exception)` kompiliert und
  **fängt nicht** — Exit 101, `panic [LYR-VM0011]` (gemessen).

Folge: jede Bereichsprüfung, jede Division durch Null, jedes `assert`, jedes erzwungene Auspacken
ist in Lyrics eigenem Testwerkzeug **unprüfbar**. `STATUS.md:650` und `:690` sagen dasselbe von der
anderen Seite: die Schutz-Pins liegen in der C#-Suite, „the real reason is that `std.test` has no
expect-a-panic assertion".

**(b) `std.test` kann sich nicht selbst testen.** `stdlib-tests/tests/test_tests.lyr` ist **ein**
Test, der prüft, dass die Assertions auf dem zutreffen, was sie behaupten (gelesen, Zeilen 13–25).
Kein einziger Negativpfad. Ein `assertEq`, das nie mehr paniken würde, hielte diese Suite grün —
und färbte jeden Test jedes Projekts grün.

**(c) Kein Teilen von Test-Hilfsmitteln — unter `lyrtest`.** Siehe 1.2(b): `lyrc check` kann es
bereits. Ein Helferfile in der Testwurzel wird von `lyrtest` still mit null Tests kompiliert, ohne
Warnung.

**(d) Namenskollision ist still.** Siehe 1.2(c).

**(e) Der Panik-Code wird verworfen.** Gemessen, `v2-r05/`. Derselbe Bereichsfehler:

```
lyrtest:     FAIL apple_tests.real_bug: index 7 is outside an array of length 3 in 'apple_tests.real_bug'
lyrvm run:   panic [LYR-VM0006]: index 7 is outside an array of length 3 in 'main.main'     (KONTROLLE, v2-r05/ctl/)
```

`lyrtest` gibt `panic.Message` aus (`src/Lyrtest/Program.cs:184`), `lyrvm run` den Code davor. Ein
Testfehlschlag trägt also keinen Diagnosecode — obwohl die Konformanzsuite genau darauf matcht
(`panic: LYR-VM0002`, gelesen `run_conformance.py:78`).

**(f) Der Runner-Prozess ist nicht isoliert — und das ist das schwerste Loch.** Gemessen,
`v2-r08-3/`, `v2-r08-0/`, `v2-r08-ctl/`. Ein Test, der `std.os.exit` ruft (gelesen,
`stdlib/std/os.lyr:20`: „Ends the process immediately with this code. No `defer` runs and no
`catch` applies"):

| Testcode | Ausgabe des Runners | Exit |
|---|---|---|
| `exit(3)` in `aa_exit_tests.calls_exit` | **nichts. Keine Ergebniszeile, keine Zusammenfassung.** | 3 |
| `exit(0)` an derselben Stelle | **vollständig leer** | **0** |
| KONTROLLE: `assertEq(1, 1)` statt `exit(…)` | `PASS aa_exit_tests.calls_exit` / `PASS aa_exit_tests.never_runs` / `PASS zz_later_tests.also_never_runs` / `3 test(s), all passed` | 0 |

Der `exit(0)`-Fall ist ein **grüner CI-Lauf, in dem kein einziger Test lief** — und nichts in der
Ausgabe sagt es. Die Isolationseinheit ist damit nicht die VM (T4), sondern der Prozess, und nichts
schützt ihn. T4 stellte diese Frage nicht; sie ist jetzt T28.

### 1.6 `assertEq` auf allem außer Skalaren und Strings

Die Fassung vor der Kritik maß `assertEq` nur auf einem **eigenen** Struct und schloss daraus, die
Konformanz-Synthese (4.7 #2) löse das Gebiet. Neu gemessen, `v2-r07/`, mit vier Formen in vier
Dateien und einer Kontrolldatei:

| Geschrieben | Ergebnis |
|---|---|
| `assertEq(1 + 1, 2)`, `assertEq("a" + "b", "ab")` | **KONTROLLE: `PASS`** |
| `assertEq(a, b)` auf `int[]` | `error[LYR-IR0001]: call to 'equals' on 'int[]'` · `note: this compiler version cannot lower it yet` — **gemeldet in `stdlib/std/test.lyr:29:10`** |
| `assertEq(a, b)` auf `?int` | dasselbe, `call to 'equals' on '?int'`, ebenfalls in `test.lyr:29:10` |
| `assertEq(a, b)` auf `List<int>` | zweimal `LYR-SEM0028` (`Equatable<List<int>>`, `Display`), an der **Aufrufstelle** |
| `assertEq(p, q)` auf eigenem Struct `Point` | zweimal `LYR-SEM0028` (gemessen, `p10-assertEq-conformance/`) |

Drei Dinge, die die alte Fassung nicht trennte:

1. **Der häufigste Vergleich einer jeden Testsuite — zwei Listen — ist heute nicht schreibbar.**
2. Bei `int[]` und `?int` landet die Diagnose **in einer Datei, die der Benutzer nicht besitzt**,
   ohne jeden Hinweis auf den auslösenden Test. Das ist eine eigene Fehlerklasse, nicht nur eine
   fehlende Konformanz.
3. **Konformanz-Synthese hilft `int[]` nicht** — ein Array ist kein nominaler Typ, und die Meldung
   sagt `IR0001`, also eine Lowering-Grenze, nicht eine Constraint-Grenze. `List<T>` bräuchte
   zusätzlich **bedingte Konformanz** (PLAN.md 4.7 #3, gelesen). K9 unten führt das jetzt getrennt.

Nebenbefund derselben Probe: `--filter` verhindert nicht, dass die nicht getroffenen Dateien
kompiliert werden — der Filter greift erst nach `CompileFile` (gelesen, `Program.cs:150` gegen
`:172`), also färbt ein Compilerfehler in einer ausgefilterten Datei den Lauf trotzdem rot
(gemessen, `v2-r07` mit `--filter dd_list`).

### 1.7 Weitere gemessene Eigenheiten

| Befund | Beleg |
|---|---|
| Ein Modul-`let` läuft **einmal pro Test** — das ist das heimliche Setup | gemessen, `p04-fail-and-globals/`: „module init ran" zweimal bei zwei Tests |
| `fail()` zählt nicht als `return` → `LYR-SEM0017` | gemessen, `p05-fail-never/`; bewusst, `stdlib/std/test.lyr:89–90` |
| Reihenfolge: Dateien ordinal, Tests in Deklarationsreihenfolge; kein Shuffle, kein Seed | gemessen, `p09-filter-order/` |
| `--filter` ist Teilstring, **case-sensitive** (`StringComparison.Ordinal`) | gemessen: `apple` trifft, `APPLE` → `LYR-CLI0009` |
| Tabellengetriebener Test: Fehlschlag nennt Funktion und Zeile, **nicht die Zeile der Tabelle**, und die Schleife bricht ab | gemessen, `p07-table/`: `FAIL table_tests.squares: expected 8, got 9` |
| Eine Datei, die nicht kompiliert, zählt in `failed`, nie in `total` → `0 test(s), 1 FAILED` | gemessen, `p07`/`p10`/`v2-r02` |
| Assertion-Fehlschlag und echter Bug sind **im Backtrace** unterscheidbar, in der Ergebniszeile nicht | gemessen, `v2-r05/`, s. u. |
| `lyrtest` nimmt **keine Datei**, nur ein Verzeichnis | gemessen: `LYR-CLI0001: no such directory` |
| Kein Timeout: `CallVoid(test)` bekommt keinen `ExecutionBudget` | gelesen, `src/Lyrtest/Program.cs:178` gegen `src/Lyric.Vm/ExecutionBudget.cs:26` |
| Ausgabe läuft ungefiltert durch, verschränkt mit den Ergebniszeilen | gelesen `Program.cs:143`, gesehen in `p04` und `v2-r09` |
| Keine Coverage, nirgends | `grep -ri coverage src stdlib docs` → nur „return coverage" in der Sema |
| Tests laufen fest unter `Capability.All` | gelesen, `src/Lyrtest/Program.cs:134`; `lyrtest --grant none` → `LYR-CLI0003: unknown argument` (gemessen), s. T23 |
| Ein Test, der eine Koroutine parkt und nie `run()` ruft, ist **grün und still** | gemessen, `v2-r13/`, s. T34 |
| Konformanzrunner: kein Timeout, keine Zeilenprüfung, keine Anzahlprüfung; `error:` ist Teilstring über den ganzen Diagnosetext | gelesen, `run_conformance.py:53,56–62,73` |

**Zur Unterscheidbarkeit** (gemessen, `v2-r05/`) — die alte Fassung sagte „ununterscheidbar", und
das war zu stark:

```
FAIL apple_tests.assertion_fails: expected 3, got 2
    in std.test.assertEq<int> (test.lyr:30)          ← Assertion: oberste Zeile in std.test
    in apple_tests.assertion_fails (apple_tests.lyr:5)
FAIL apple_tests.real_bug: index 7 is outside an array of length 3 in 'apple_tests.real_bug'
    in apple_tests.real_bug (apple_tests.lyr:12)      ← echter Bug: oberste Zeile im Benutzermodul
FAIL apple_tests.plain_fail: said so
    in std.test.fail (test.lyr:92)
FAIL apple_tests.assert_true_fails: must hold
    in std.core.assert (core.lyr:52)                 ← assertTrue zeigt std.core, nicht std.test
```

Unterscheidbar ist es — **nur nicht maschinell, nicht in der Ergebniszeile**, und für `fail` und
`assertTrue` zeigt die Spitze `std.test.fail` bzw. `std.core.assert`. Die tragfähige Aussage ist
die über den fehlenden Diagnosecode (1.5e), und die steht.

### 1.8 Wie die Testwurzel skaliert: gemessen, und es ist schlimmer als gedacht

Alle Zeitzahlen des alten Dossiers stammten aus 13 stdlib-Dateien bzw. einem generierten
80-Dateien-Projekt (`STATUS.md:437`, `:2230–2231`). Neu gemessen, `v2-r14/` und `v2-r14b/`: ein
generiertes Projekt mit 20 Quellmodulen und N Testdateien zu je zwei Tests, jede Testdatei
importiert ein Quellmodul.

| N Testdateien | Tests | `lyrtest .` | `lyrc check .` (dieselbe Wurzel) |
|---|---|---|---|
| 100 (KONTROLLE) | 200 | **4,9 s** | 0,68 s |
| 500 | 1000 | **18,4 s** | **1,06 s** |

`lyrtest` ist **linear in der Zahl der Dateien** — 4,3 s Arbeit auf 100, 17,8 s auf 500, abzüglich
rund 0,6 s Prozessstart. `lyrc check`, das dieselbe Wurzel als **eine** Kompilierung fährt, ist
über denselben Faktor 5 nahezu flach: 0,68 s → 1,06 s. Das sind **17×** auf 500 Dateien, gemessen,
an einer realen Wurzel, nicht geschätzt.

Das entscheidet T6 und T4 zusammen: die eine Kompilierung für die ganze Testwurzel ist nicht ein
Preis, den man für gemeinsame Hilfsmittel zahlt — sie ist ein **Beschleuniger um eine
Größenordnung**, den das Schwesterwerkzeug bereits fährt.

### 1.9 Das Muster

Die Werkzeugkette hat sich fünfmal in C# gebaut, was sie ihren Benutzern nicht anbietet: Goldens
(4× `GoldenTests.cs` + `ExtractorTests.cs`), Parametrisierung (`[Theory]`), Setup/Teardown
(Konstruktor/`Dispose`), Erwarten-einer-Ausnahme (`Assert.Throws<T>`), Überspringen
(Windows-Skips, `STATUS.md:946`). Und die Guide-Schnipsel werden von der C#-Suite kompiliert
(`docs/guide/15-attributes.md:296–297`) — Doc-Tests, die es für Lyric nicht gibt.

---

## 2. Sprachvergleich

Die Tabelle nennt, was das **Standardwerkzeug** der Sprache mitbringt; ein „(extern)" heißt, dass
die Form etabliert ist, aber als Bibliothek. Die Fassung vor der Kritik bepreiste diese Spalten
uneinheitlich — für Rust wurden externe Crates namentlich geführt, für Swift und pytest stand „—",
obwohl es dort dieselben etablierten Gegenstücke gibt. Wer die Tabelle als „was fehlt Lyric" liest,
unterschätzte dadurch, was in diesen Ökosystemen selbstverständlich ist. Das ist korrigiert.

| Sprache | Ort der Tests | Assertion | Erwarte-Fehler | Tabelle | Setup/Teardown | Parallel | Bench | Coverage | Property | Snapshot | Doc-Test |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **Rust** | `#[cfg(test)] mod` **im selben File** + `tests/` | `assert_eq!` (Makro, nennt beide Seiten, braucht `PartialEq`+`Debug`) | `#[should_panic(expected=…)]`, Test gibt `Result` zurück | `rstest` (extern) | `rstest` fixtures (extern) | **ja, Threads, Default** | `#[bench]` nur nightly; Criterion (extern) | `cargo llvm-cov` (extern) | `proptest`/`quickcheck` (extern) | `insta` (extern) | **ja, `cargo test`** |
| **Go** | `*_test.go` im Paket, nur unter `go test` | **keine** — `if got != want { t.Errorf }` | kein Mechanismus (Panik = `recover` von Hand) | `t.Run` Subtests, je Zeile ein Name | `t.Cleanup` (LIFO), `TestMain` | `t.Parallel()`, opt-in | **`BenchmarkXxx`, `b.N` im Standardwerkzeug** | **`-cover` im Standardwerkzeug** | **`FuzzXxx` seit 1.18, coverage-geführt** | — | **`Example`-Funktionen mit `// Output:`** |
| **Swift** (swift-testing) | eigene Datei, `@Test` auf freier Funktion | `#expect(a == b)` — **ein** Makro, zerlegt den Ausdruck | `#expect(throws: E.self) { }` | `@Test(arguments: […])`, je Argument eine Zeile | `@Suite` mit `init`/`deinit` | **ja, Default** | `measure` (XCTest) | Xcode/llvm-cov | `swift-testing` hat keins; `SwiftCheck` (extern) | `swift-snapshot-testing` (extern, etabliert) | — |
| **Python pytest** | `test_*.py` | schlichtes `assert`; pytest **schreibt den AST um** und erzeugt den Diff | `with pytest.raises(E, match=…)` | `@parametrize`, je Zeile eine ID | Fixtures mit Scopes, `yield` = Teardown | `pytest-xdist` (extern) | `pytest-benchmark` (extern) | `pytest-cov` (extern) | `hypothesis` (extern) | `syrupy` (extern) | `--doctest-modules` |
| **Haskell** (QuickCheck/HSpec) | beliebig, meist `test/` | Eigenschaft als Funktion: `prop_rev xs = reverse (reverse xs) == xs` | `evaluate … shouldThrow` (HSpec) | **ist das ganze Modell** | HSpec `before`/`after` | HSpec `parallel` | `criterion` (extern) | **HPC im Standardwerkzeug** (`-fhpc`, `cabal test --enable-coverage`, `stack test --coverage`) | **das Original: `Arbitrary` + Shrinking** | — | `doctest` (extern) |
| **Jest** | `*.test.js` | `expect(x).toBe(y)`, erweiterbare Matcher | `expect(fn).toThrow(…)` | `test.each([[…]])` | `beforeEach`/`afterEach`/`…All` | ja, **ein Worker-Prozess pro Datei** | — | **`--coverage` eingebaut** | — | **`toMatchSnapshot()`, `-u`, Inline-Snapshots** | — |
| **Elixir ExUnit** | `test/*_test.exs`, **in der Distribution** | `assert` ist ein Makro und zerlegt den Ausdruck | `assert_raise E, fn -> … end` | Comprehension oder `for` über Fälle | `setup` / `setup_all` mit Kontext-Map | `async: true` **je Testmodul** (Module nebenläufig zueinander, Tests darin sequenziell) | — | `mix test --cover` | `stream_data` (extern) | — | **`doctest MyModule`** |
| **C# xUnit** | eigenes Projekt | `Assert.Equal(…)` | `Assert.Throws<T>()` **gibt die Exception zurück** | `[Theory]` + `[InlineData]`/`[MemberData]` | **neue Instanz je Test**: Ktor = Setup, `Dispose` = Teardown | je Collection | BenchmarkDotNet (extern) | coverlet (extern) | FsCheck (extern) | Verify (extern) | — |
| **Lyric 4** | `tests/`; **`lyrtest` UND `lyric check` kompilieren sie, mit verschiedenen Modulwurzeln** (1.2) | 9 Funktionen; `assertEq` scheitert auf `int[]`, `?int`, `List<T>` und eigenen Structs (1.6) | **nichts** | **nichts** | **nichts** (nur ein Modul-`let` als Nebenwirkung) | nein | **nichts** in der Sprache | **nichts** | **nichts** | **nichts** | **nichts** |

Vier Zellen sind gegenüber der Fassung vor der Kritik korrigiert:

- **ExUnit, Spalte Parallel.** `async: true` steht auf dem Test-**Modul** (`use ExUnit.Case,
  async: true`) und erlaubt, dieses Modul nebenläufig zu anderen async-Modulen zu fahren; Tests
  **innerhalb** eines Moduls laufen sequenziell. Der eigene BEAM-Prozess je Test ist **Isolation**,
  nicht Parallelität. Das ist für T14 folgenreich: ExUnit ist damit der Präzedenzfall für genau das
  Korn, das dieses Dossier vorschlägt — parallel über Dateien/Module —, nicht der Gegenpol dazu.
- **Haskell, Spalte Coverage.** GHC bringt HPC mit; `cabal test --enable-coverage` und
  `stack test --coverage` sind die Standardwege. Das „—" war falsch und hat T19 einen Vorbildfall
  genommen.
- **Go, Spalte Snapshot.** `Example` + `// Output:` ist **kein** Snapshot-Mechanismus: die
  Erwartung ist von Hand in den Quelltext geschrieben, kein Werkzeug erzeugt oder aktualisiert sie,
  und ein `-u`-Pfad fehlt vollständig. Die Zelle doppelte ohnehin die Doc-Test-Spalte. Jetzt „—".
- **Bench/Property/Snapshot durchgehend.** Externe, aber etablierte Gegenstücke sind jetzt für
  alle Sprachen genannt statt nur für Rust.

### Die entgegengesetzten Entscheidungen

**Rust — Tests im selben File, mit Zugang zu Privatem.** Lyric hat das Gegenteil entschieden und
schreibt es in den Quelltext: „The Go shape, not the Rust shape: tests live in a directory of their
own … so `@Test` stays a TOOL-read attribute and no build rule hangs off it"
(`src/Lyrtest/Program.cs:11–14`). Rust gewinnt damit den Unit-Test auf nicht exportierten Elementen
und die Nähe von Test und Code. Rust zahlt: `#[cfg(test)]` ist ein Mechanismus für bedingte
Kompilierung **innerhalb der Sprache**, der Testcode liegt im Quellbaum, und für Integrationstests
braucht es trotzdem noch `tests/` — also zwei Orte. Lyrics Entscheidung ist gut und soll bleiben;
ihr **Preis** ist Loch 1.5(c) — aber nur unter `lyrtest`, denn `lyric check` fährt die Testwurzel
bereits als Modulwurzel (1.2b). Rust hat den Preis nicht, weil sein Testcode im Modulbaum liegt.

**Go — gar keine Assertions.** Bewusst: ein `if got != want { t.Errorf("…") }` erzwingt eine
Meldung, die der Autor geschrieben hat und die deshalb sagt, was zählt. Lyric hat sich für eine
Assertion-Bibliothek entschieden und trägt dafür Frage T1 und T22. Gos Preis ist Textmasse in jedem
Vergleich und `reflect.DeepEqual` für alles Zusammengesetzte. Bemerkenswert für 1.6: genau der
Fall, an dem Lyrics `assertEq` heute scheitert — zwei Listen —, ist der, für den Go sein
`reflect.DeepEqual` hat.

**Elixir/Jest — Isolation durch den Prozess.** ExUnit fährt jeden Test in einem eigenen
BEAM-Prozess: eigener Heap, und ETS-Tabellen wie verlinkte Prozesse sterben mit ihm. Jest fährt
jede Datei in einem eigenen Worker-Prozess — dasselbe Korn wie Lyrics VM pro Datei. Lyric hat
per-Instanz-Ressourcenbesitz 4.3.0 ausdrücklich abgelehnt (`STATUS.md:2226–2235`), und genau das
ist Loch 1.5(c)/T4. **Und beide lösen nebenbei das, was Lyric nicht löst**: ein Test, der in einem
eigenen Prozess `exit` ruft, nimmt nur diesen Prozess mit (1.5f). Was Elixir dafür zahlt: die ganze
Antwort ruht auf billigen Prozessen, die Lyric mit einer VM und Koroutinen auf einem Thread nicht
hat.

### Was zu Lyrics Charakter passt — und was nicht

| Idee | Passt, weil | Passt nicht, weil |
|---|---|---|
| Go: Subtests, `t.Cleanup`, `b.N`, `-cover`, Fuzzing im Standardwerkzeug | Lyric hat sich schon für die Go-Form entschieden | `t *testing.T` als Parameter widerspricht dem `@Test`-Vertrag „nimmt nichts" |
| Swift: `@Test(arguments:)`, `@Suite`, `.timeLimit`, Traits | Lyric hat die `@Test`-Schreibweise bereits; Traits sind Attributfelder | `#expect` ist ein Makro — Lyric hat keins und will keins |
| xUnit: neue Instanz je Test, `Assert.Throws<T>` gibt die Exception zurück, `[MemberData]` als benannter Lieferant | Lyric macht die frische Instanz schon; die Signatur ist die aus `test.lyr:98` | Konstruktor-als-Setup braucht Klassen mit Lebenszyklus |
| QuickCheck: `Arbitrary` mit **statischem** Member | Monomorphisierung macht es kostenlos; steht als 4.7-Fundament #4 in PLAN.md | braucht Konformanz-Synthese, sonst Generator je Typ von Hand; und `LYR-SEM0067` verbietet das Attribut auf einer generischen Deklaration (T27) |
| Jest: Snapshots | die Werkzeugkette hat es 5× selbst gebaut | `-u` ist eine Maschine zum Committen falscher Erwartungen |
| Jest/ExUnit: Prozess als Isolationseinheit | löst 1.5(f) mit, das nichts anderes löst | ein Kindprozess je Datei kostet Prozessstart und macht die Ausgabe zu einem Protokoll |
| pytest: AST-Rewriting der Assertion, Fixture-Injektion per Parametername | — | setzt Dynamik voraus, die eine monomorphisierte Sprache nicht hat |
| Rust: Doc-Tests | gehört schon auf die 4.7-Liste (#19) | jeder ` ``` `-Block wird Kompilationseinheit → braucht Opt-out |

---

## 3. Designfragen

### T1 — Woher kommt eine Assertion: N Funktionen oder ein zerlegendes Konstrukt?

**Heute:** neun Funktionen in `stdlib/std/test.lyr:23–93`. Und das gemessene Hauptproblem ist
nicht die Zahl, sondern die **Reichweite**: `assertEq` funktioniert auf Skalaren und `string`, und
scheitert auf `int[]`, `?int`, `List<int>` und jedem eigenen Struct (gemessen, 1.6 und `p10`).
`assertTrue(c, msg)` verlangt eine Meldung, `assertNull(v)` nimmt keine, `assertFalse` gibt es
nicht, `assertGreater` auch nicht (gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A — Funktionsmenge behalten und ergänzen | xUnit, XCTest | wächst nie zu Ende; jede Kombination eine Funktion; **löst 1.6 nicht** |
| B — **ein** compilergelesenes `assert expr`, das eine Vergleichsoperation zerlegt und beide Seiten nennt | Elixir, pytest, Swift `#expect` | der Compiler muss ein zweites Konstrukt kennen; §12 braucht einen Code; **und es muss beantworten, was `==` und `show` auf `int[]`, `?int` und `List<int>` heißen** |
| C — Matcher-Kette `expect(x).toBe(y)` | Jest | große API; braucht generische Methoden auf generischen Typen (seit PR #170 da); leistet die Benennung beider Seiten ebenso |
| D — keine, der Benutzer schreibt `if` + `fail` | Go | Textmasse, und `fail` ist heute nicht einmal ein Terminator (T22) |

**Empfehlung: B, mit A als kleinem Rest — aber erst, nachdem die Vergleichsfrage beantwortet ist.**
Die Begründung der alten Fassung („Ohne Makros ist B der einzige Weg") war zu stark: C leistet
dasselbe und kostet nur API-Fläche. Der echte Grund für B ist Rule 2 — das Konzept ist „ein Test
schlägt fehl und sagt warum", und B ist **ein** Konstrukt mit **einer** Regel, wie `@Deprecated`
das eine compilergelesene Attribut ist (`docs/guide/15-attributes.md:249`).

**Wichtiger ist die Reihenfolge.** B verschiebt 1.6 nur: ein zerlegendes `assert a == b` muss
beantworten, was `==` und `show` auf `int[]`, `?int` und `List<int>` bedeuten — heute `LYR-IR0001`
mitten in der stdlib bzw. `LYR-SEM0028`. Diese Frage beantwortet weder T1 noch K9, und sie gehört
vor die Wahl zwischen B und C.
**Bruch:** minor — die neun Funktionen bleiben, werden später `@Deprecated` (Migrationssurface:
566 `assertEq` und 321 `assertTrue` allein in `stdlib-tests/`, gemessen per grep; s. T30).
**Hängt an:** Konformanz-Synthese (4.7 #2), bedingte Konformanz (#3), `IR0001`-Grenzen auf Array
und Optional, Diagnostik-Gebiet (ein `LYR-`Code).

### T2 — Wie sagt ein Test „das hier muss schiefgehen"?

**Heute:** gar nicht. `fn(…) -> void throws E` als Parametertyp ist `LYR-SEM0084` (gemessen), eine
Panik ist nicht fangbar (gemessen, Exit 101). `stdlib/std/test.lyr:95–99` notiert es selbst.

| Option | Vorbild | Preis |
|---|---|---|
| A — `assertThrows<E :: [Throwable]>(f: fn() -> void throws E): E`, sobald typed throws Stufe 3 steht | xUnit `Assert.Throws<T>`, Swift `#expect(throws:)` | wartet auf 4.7 #7; deckt **nur** Exceptions, keine Paniken |
| B — Attribut-Merkmal `@Test { panics = "LYR-VM0006" }`; der Runner fängt die Panik ohnehin schon | Rust `#[should_panic(expected=…)]` | braucht **keine** Sprachänderung — nur `Program.cs:181` und den Code, den 1.5(e) heute wegwirft |
| C — Panik fangbar machen | — | zerstört die Trennung Panik/Exception; bricht §9 |
| D — beides lassen, Negativpfade in C# pinnen | Status quo | die Sprache kann ihre eigene Laufzeit nicht prüfen |

**Empfehlung: A **und** B.** Das sind nicht zwei Mechanismen für ein Konzept, sondern zwei
Konzepte: eine Exception ist ein Wert, den ein Programm behandeln darf; eine Panik ist das Ende des
Programms, und nur der Runner steht außerhalb davon. B ist billig und holt die Schutz-Pins aus der
C#-Suite zurück (`STATUS.md:650`). **Voraussetzung für B:** der Panik-Code muss in der
Ergebniszeile stehen (1.5e). **Nachtrag aus T28:** B muss auch sagen, was `panics = …` für einen
Test bedeutet, der nicht panikt, sondern `exit` ruft — dort gibt es keine Panik zu fangen.
**Bruch:** nein (additiv). **Hängt an:** typed throws Stufe 3 (PLAN.md 4.7 #7), T31 (Exit-Codes).

### T3 — Tabellengetriebene Tests

**Heute:** eine Schleife in einem Test. Gemessen (`p07`): `FAIL table_tests.squares: expected 8,
got 9` — die Zeilennummer der Tabelle steht nirgends, und die Schleife bricht bei der ersten
schlechten Zeile ab, die restlichen werden nie geprüft.

| Option | Vorbild | Preis |
|---|---|---|
| A — `@Case(1, 2)` mehrfach am Test; der Runner ruft je Zeile einmal | xUnit `[InlineData]`, Swift `@Test(arguments:)` | drei harte Hürden, alle gemessen/gelesen — s. u. |
| B — Subtests: der Test bekommt ein `Runner`-Objekt mit `run(name, fn)` | Go `t.Run` | bricht den Vertrag „nimmt nichts"; `t` muss durch jeden Helfer gefädelt werden; macht einen Test zu einem Baum |
| C — `@Cases(provider = "fnName")`: die genannte Funktion liefert die Fälle, der Runner ruft je Element | **xUnit `[MemberData]`/`[ClassData]`** | der Runner muss zwei Funktionen aufrufen und Werte marshallen |
| D — nichts | Status quo | jede Tabelle ist ein Test, der beim ersten Fehler aufhört |

**Die drei Hürden von A**, die die alte Fassung nicht nannte und die die „80 %"-Zahl ersetzen (sie
war unbelegt und ist gestrichen):

1. **`LYR-SEM0068`** — „`'@Case' sits on this declaration twice`" (gemessen, `v2-r15/dup.lyr`). A
   braucht eine ausdrückliche Ausnahme von dieser Regel, oder `@Cases` ist die Gruppe.
2. **`LYR-SEM0067`** — kein Attribut auf einer generischen Deklaration (gemessen, `v2-r10/`). Jede
   Tabelle ist damit zwingend monomorph; ein Fall über `List<T>` ist gar nicht formulierbar (T27).
3. **`lyric fmt` faltet gestapelte Attribute in eine Gruppe** (gemessen, `v2-r15/stacked.lyr`:
   `@Marker` + `@Case(1)` wird zu `@[Marker, Case(1)]`), und `docs/guide/15-attributes.md:179–181`
   hält fest: „the same attribute twice in one group is still the same attribute twice". Damit
   formt der Formatierer N Tabellenzeilen um — s. T29.

Dazu, gelesen: Attributargumente sind nur skalare Literale, Strings, Chars, Bools und
Enum-Unit-Varianten (`docs/guide/15-attributes.md:49–64,88–110`) — Structs gehen nicht. Und
`WithArg<T>` verlangt ein Feld auf dem Attributstruct (`LYR-SEM0095`, gemessen, `v2-r15`).

**Empfehlung: A für Skalare, C für den Rest, ausdrücklich nicht B.** C bekommt sein richtiges
Vorbild: xUnits `[MemberData]`/`[ClassData]` nehmen eine benannte Member-Funktion als Fallquelle —
exakt die Form, die C beschreibt, und ausgerechnet aus dem Framework, das die Werkzeugkette selbst
benutzt. (Die alte Fassung nannte „pytest `argvalues`"; das ist der zweite Positionsparameter von
`@pytest.mark.parametrize`, also eine an Ort und Stelle geschriebene Liste, kein benannter
Lieferant. pytests Lieferantenform ist der Hook `pytest_generate_tests` bzw. indirekte Fixtures.)
**Bruch:** nein. **Hängt an:** Attribut-Gebiet (`SEM0068`-Ausnahme, `SEM0067`, Arrays als
Attributwerte), T27, T29, T7 (darf ein Fall Parameter nehmen).

### T4 — Was ist die Isolationseinheit eines Tests?

**Heute:** frische *Instanz* je Test, eine *VM* je Datei, Ressourcen gehören der VM (gemessen,
`p03`+Kontrolle; `STATUS.md:2226–2235`). **Und der Prozess gehört niemandem** — s. T28, das die
Frage eine Ebene höher stellt und in der alten Fassung fehlte.

| Option | Vorbild | Preis |
|---|---|---|
| A — VM pro Test | ExUnit (Prozess pro Test) | 12× Kompilierung; Suite 3,9 s → geschätzt 50 s (`STATUS.md:462`) |
| B — per-Instanz-Ressourcenbesitz | — | genau das, was 4.3.0 abgelehnt hat; Rule 2: ist „VM beenden" und „Lauf darin beenden" ein Mechanismus oder zwei? |
| C — **einmal kompilieren, je Test in eine frische VM instanziieren** | — | nur die `LangVm`-Konstruktion, s. u. |
| D — lassen, dokumentieren | Status quo | eine gemessene Fehlerquelle bleibt stehen |

**Die Messung, die die alte Fassung für offen hielt, ist gelesen bereits entschieden.** Sie schrieb
„ob eine zweite VM ein von der ersten kompiliertes Modul annimmt, ist nicht gemessen — Native-
Registry und Importtabelle könnten VM-gebunden sein". Die Quelle sagt es:

- `LangVm.Instantiate` (`src/Lyric.Embedding/LangVm.cs:306–310`) reicht `module.Loaded` an
  `LoadedProgram.Load(module.Loaded, _natives, _options.Capabilities, budget, jit: …)`.
- `LoadedProgram.Load` (`src/Lyric.Vm/LoadedProgram.cs:72–100`) **liest** das `BytecodeModule` und
  baut `Prepared[]`, `DispatchTable`, die gebundenen Natives (`(natives ?? new
  NativeRegistry()).Bind(module)`) und `globals[]` **bei jedem Load neu**. Die VM-Bindung entsteht
  dort, nicht beim Kompilieren.
- `BytecodeModule` ist durchgehend `init`-only über `IReadOnlyList` (gelesen,
  `src/Lyric.Core/Bytecode/BytecodeModule.cs:14–66`) — es wird nicht mutiert.
- `ScriptModule` trägt **keine** Rückreferenz auf eine `LangVm` (gelesen,
  `src/Lyric.Embedding/ScriptModule.cs:14–52`).

Dazu ein Befund, der die Rechnung umdreht: **`vm.Instantiate(module)` läuft heute bereits einmal
pro Test** (gelesen, `src/Lyrtest/Program.cs:178`). Die gesamte Load-Arbeit — `Prepared`,
`DispatchTable`, Native-Bindung, Globals — ist also **schon pro Test bezahlt**. Option C kostet
zusätzlich nur die `LangVm`-Konstruktion, und `STATUS.md:437` sagt, die ist fast umsonst (2,46 s
gegen 2,55 s auf 80 Dateien).

Restrisiko, ehrlich benannt: das ist **gelesen, nicht gemessen** — eine Messung bräuchte einen
C#-Host, den dieses Dossier nicht bauen darf. Was noch zu prüfen wäre, ist eng: ob ein `Jit`-Kontext
oder ein Debug-Controller Zustand am Modul hinterlässt.

**Empfehlung: C bauen, und T28 zusammen mit T4 entscheiden.** Eine Empfehlung zur Isolationseinheit,
die den Prozess nicht nennt, ist unvollständig — und `std.os.exit` in einem Test macht aus einem
Lauf einen stummen Nulllauf (gemessen, 1.5f). Wenn die Antwort auf T28 „Kindprozess je Datei"
lautet, ist T4 mitentschieden; wenn sie „`exit` ist im Testlauf eine Panik" lautet, bleibt C die
Antwort auf T4.
**Bruch:** nein (strengere Isolation macht grüne Tests nicht rot).
**Hängt an:** T28 (Prozessisolation), T32 (Skalierung), T6 (Kompilationseinheit).

### T5 — Setup und Teardown

**Heute:** nichts Benanntes. De facto läuft der Initialisierer eines Modul-`let` **einmal pro
Test** (gemessen, `p04`) — das ist ein Setup, das niemand so genannt hat, das nicht abräumen kann
und dessen Reihenfolge nicht benennbar ist. Abräumen geht nur per `defer` im Test selbst.

| Option | Vorbild | Preis |
|---|---|---|
| A — nichts; `defer` plus eine Hilfsfunktion | Status quo | jede Datei wiederholt ihr Setup; kein `afterAll` |
| B — `@Setup` / `@Teardown` je Datei, `@SetupAll` / `@TeardownAll` je Lauf | Jest, ExUnit `setup`/`setup_all` | vier neue Attribute im Vokabular; Reihenfolge muss spezifiziert werden |
| C — ein Suite-Typ: Konstruktor = Setup, `close` = Teardown | xUnit | Lyric hat keine Vererbung; ein Testtyp bräuchte eine eigene Lebenszyklusregel |
| D — Fixtures als Parameter des Tests | pytest, Go | bricht „nimmt nichts"; pytest braucht dafür Namensinjektion |

**Empfehlung: B.** Attributmarkierte freie Funktionen sind der Mechanismus, den `@Test` schon
benutzt — das Vokabular zu erweitern ist kein zweiter Mechanismus. `@TeardownAll` ist außerdem die
einzige Stelle, an der ein Test heute etwas zuverlässig schließen könnte, was T4 nicht löst; und es
ist der natürliche Ort für die Nachbedingung aus T34 („keine offene Koroutine am Testende").
**Bruch:** nein. **Hängt an:** T7 (ein `@Setup` ist ein zweiter Attributvertrag), T34.

### T6 — Wo leben gemeinsame Test-Hilfsmittel?

**Heute — und das ist gegenüber der alten Fassung die wichtigste Korrektur:** unter `lyrtest`
nirgends, unter `lyric check` bereits überall. Gemessen, `v2-r02/`: `lyrc check .` → „3 modules ok",
`lyrtest .` → `LYR-RES0003`. Ein Helferfile in der Testwurzel wird von `lyrtest` still mit null
Tests kompiliert.

| Option | Vorbild | Preis |
|---|---|---|
| A — die Testwurzel wird **auch für `lyrtest`** Modulwurzel, als **eine** Kompilierung | **`lyric check` selbst** (`src/Lyrc/Program.cs:342–350`); Go (Helfer im selben Paket) | die VM-pro-Datei-Regel muss neu begründet werden — s. u. |
| B — ein eigenes `testHelperRoot` in `lyric.json` | — | ein dritter Wurzelbegriff neben `sourceRoot`/`testRoot` |
| C — `lyrtest` behält die Datei-für-Datei-Form, `lyric check` gibt seine auf | — | macht die Divergenz durch Rückbau weg und verliert 17× Geschwindigkeit (1.8) |
| D — lassen; Helfer gehören in den Quellbaum | Status quo | Testcode wird ausgeliefert — genau das, was `testRoot` verhindern sollte; **und die Divergenz bleibt ausgeliefert** |

**Empfehlung: A — und zwar gerahmt als „die beiden Werkzeuge in Übereinstimmung bringen", nicht als
neues Feature.** Drei Gründe, alle belegt:

1. Der Mechanismus **existiert und läuft** (gemessen, 1.2b/c). Es ist kein Neubau.
2. Er ist **17× schneller** auf 500 Dateien (gemessen, 1.8: 1,06 s gegen 18,4 s). Die alte Fassung
   bepreiste A als Kosten; gemessen ist es ein Gewinn um eine Größenordnung.
3. Der Quelltext von `CheckProject` nennt die Divergenz selbst als das, was nicht passieren soll
   (gelesen, `src/Lyrc/Program.cs:337–339`).

Was A **wirklich** kostet, und die alte Fassung falsch benannte: nicht die Kompilationseinheit,
sondern die **Laufzeiteinheit**. Wenn die ganze Testwurzel ein Kompilat ist, ist die Frage „VM pro
Datei" neu zu beantworten — das ist T4, und C dort (eine VM je Test aus einem Kompilat) passt
genau dazu.

Dazu die Warnung bei einer Testdatei ohne `@Test`: die Stille ist derselbe Fehlertyp wie die
`extend`-auf-Array-Stille (`STATUS.md` §Still open) — eine Deklaration, die nichts behauptet und
nichts sagt. **Aber sie hängt an A**: der Runner sieht nur `OnFunctions("Test")` und kann „bewusste
Hilfsdatei" nicht von „Testdatei, in der der Import vergessen wurde" unterscheiden, solange es
keine Hilfsdateien geben darf. Sie gehört deshalb **nicht** in die Liste der sprachfreien
Sofortmaßnahmen (§4 korrigiert).
**Bruch:** nein (additiv; Namen ändern sich über T8). **Hängt an:** T4, T8, T32, Modul-/Build-Gebiet.

### T7 — Wer prüft den `@Test`-Vertrag?

**Heute:** niemand. Gemessen (1.3): Parameter → Laufzeitfehler aus der Embedding-Schicht;
`: int` → **`PASS`**, Wert still verworfen; `lyrc check` schweigt zu beidem. **Und der Runner
greift nicht einmal nach `std.test.Test`, sondern nach dem unqualifizierten Namen `"Test"`**
(gemessen, 1.4).

| Option | Vorbild | Preis |
|---|---|---|
| A — der Compiler kennt `@Test` wie `@Deprecated` | Rust, Swift (dort Makros) | zweites Attribut im compilergelesenen Set; §15 nennt das Set einen Vertrag, der „by decision" wächst (`docs/guide/15-attributes.md:334–336`) |
| B — **ein allgemeiner Marker**: `pub struct Test :: [OnFunction, RequiresSignature<fn() -> void>] { }` | keine direkte; die Verallgemeinerung von `WithArg<T>` | braucht werfende/nicht-werfende Funktionstypen in einer Constraint-Position — **und eine zweite Hälfte, s. u.** |
| C — der Runner meldet es, wie heute | Status quo | der `: int`-Fall bleibt **still grün** |

**Empfehlung: B, aber nur als Paar.** `WithArg<T>` ist der Präzedenzfall: Konformanz entscheidet,
geprüft **an der Deklaration**, damit der Fehler bei dem landet, der das Attribut ausliefert, nicht
bei jedem, der es benutzt (`docs/guide/15-attributes.md:150–157`). B repariert dieselbe
Fehlerklasse für **jedes** Host-SDK-Attribut, nicht nur für Tests — das ist die Rule-2-förmige
Antwort.

**Die zweite Hälfte, die in der alten Fassung fehlte:** B schließt das Loch **nicht**, solange der
Runner auf `OnFunctions("Test")` matcht. Gemessen (1.4) nimmt er jeden `Test`-benannten Marker,
auch einen ohne die Konformanz. Der Runner muss also **prüfen, dass das Attribut die Konformanz
trägt** — was voraussetzt, dass die Konformanz im Modul steht und über die Attributzeile erreichbar
ist. Heute trägt `AttributeUse` Attributname, Zielart, Ziel, Zielname und Werte (gelesen,
`src/Lyric.Core/Bytecode/ModuleAttributes.cs:128–141`) — die Konformanzen des Attributtyps stehen
nicht darin. Das ist die Arbeit, die B wirklich kostet.
**Bruch:** minor — `@Test pub fn t(): int` hört auf zu kompilieren. 4.x: Warnung
(„`@Test` on a function that returns a value; the value is discarded").
**Hängt an:** Attribut-/Metadaten-Gebiet (Konformanzen in der Attributzeile), typed throws (darf
ein Test werfen?), T3/T15/T27 (dürfen `@Case`/`@Property` Parameter nehmen — dann ist der Vertrag
pro Attribut, nicht global).

### T8 — Wie heißt ein Test, und ist der Name eindeutig?

**Heute:** `modul.funktion`, Modulname = Dateibasisname **unter `lyrtest`**; unter `lyric check`
Modulname aus dem Pfad, und ein geschriebener `module`-Header gilt (gemessen, 1.2c/d).

| Option | Vorbild | Preis |
|---|---|---|
| A — Modulname aus dem Pfad (`alpha.shared_tests.one`) | Go (Paketpfad), pytest (Dateipfad), **`lyric check` selbst** | ändert jede Ergebniszeile und jeden `--filter` in bestehenden Skripten |
| B — doppelte Modulnamen in einer Testwurzel ablehnen | — | verbietet eine harmlose Ordnerstruktur; behebt die Divergenz nicht |
| C — lassen | Status quo | zwei Tests, ein Name; zwei Werkzeuge, zwei Namen |

**Empfehlung: A — und die Bewertung der alten Fassung dreht sich damit um.** Sie empfahl „A, wenn
das Modul-Gebiet ohnehin dahin geht, sonst B", und stufte A als „Bruch: minor" ein. Gemessen ist A
**bereits gebaut**, in `lyrc check`, über `ScriptSource.ModuleNameUnder(root, file)` (gelesen,
`src/Lyrc/Program.cs:376`, `src/Lyric.Frontend/Compiler/ScriptSource.cs:71–79`). A ist also nicht
eine Änderung mit minor-Bruch gegenüber einem konsistenten Ist-Stand, sondern die **Beseitigung
einer bestehenden Divergenz**: das Werkzeug, das die Namen heute schon richtig vergibt, ist
`check`; das, das sie falsch vergibt, ist `test`. B löst das nicht und verbietet zusätzlich etwas
Harmloses.
**Bruch:** minor gegenüber `lyrtest`-Skripten, **kein** Bruch gegenüber `lyric check`.
**Hängt an:** T6 (dieselbe Änderung), T26 (was der geschriebene Header dabei soll), Modul-Gebiet.

### T9 — Was kann man dem Runner zur Auswahl sagen?

**Heute:** `--filter <text>`, Ordinal-Teilstring auf `modul.funktion`, case-sensitive (gemessen);
kein Treffer ist ein Fehler, `LYR-CLI0009` (gemessen). Und der Filter greift **erst nach der
Kompilierung**, also färbt ein Compilerfehler in einer ausgefilterten Datei den Lauf rot (gemessen,
1.6). Sonst nichts.

| Option | Vorbild | Preis |
|---|---|---|
| A — exakte Form (`--exact`) und Glob | — | zwei Flags |
| B — Regex wie Gos `-run` | Go | Lyric hat kein Regex in der std (`design/stdlib-2.md` schließt es aus — und diese Frage ist offen, STATUS §Still open); der Runner zöge die ganze Bibliotheksentscheidung mit |
| C — Tags: `@Tag("slow")` + `--include`/`--exclude` | ExUnit `@tag`, swift-testing `.tags` | ein Attribut mehr; fällt fast mit T3/T5 zusammen |
| D — `--failed` (nur die letzten Fehlschläge) | pytest `--lf` | der Runner muss eine Ergebnisdatei schreiben — wohin? |

**Empfehlung: A + C + D, B nicht.** C ist das, was „lauf die schnellen" beantwortet, und das ist
die Frage, die in einem wachsenden Projekt täglich gestellt wird. D braucht eine Entscheidung über
einen Zustandsordner (`.lyric/`), die es sonst nirgends gibt — das gehört mit dem Build-Gebiet
abgestimmt. **Nachtrag:** wenn T6 A kommt und die Testwurzel eine Kompilationseinheit wird, ist die
Filter-nach-Kompilierung-Eigenheit automatisch weg — es gibt dann nur noch eine Kompilierung.
**Bruch:** nein. **Hängt an:** Bibliotheksfrage (Regex), Build-Gebiet (Zustandsordner), T6.

### T10 — Überspringen, Ignorieren, erwarteter Fehlschlag

**Heute:** nichts. `lyrtest --help` listet sechs Optionsnamen (gemessen); `std.test` hat neun
Funktionen und ein Struct (gelesen). Die Werkzeugkette selbst braucht es bereits und löst es in C#
(`STATUS.md:946`: „Windows skips: only the delivery differs").

| Option | Vorbild | Preis |
|---|---|---|
| A — `@Skip("Grund")`, Runner meldet `SKIP` | Rust `#[ignore]`, Jest `test.skip` | statisch; beantwortet „nur auf Linux" nicht |
| B — Laufzeit-`skip("Grund")`, vom Fehlschlag unterschieden | Go `t.Skip`, pytest `pytest.skip` | der Runner muss eine dritte Ausgangsart unterscheiden — heute nur Panik/Exception |
| C — beides | ExUnit (`@tag :skip` und Bedingungen) | — |
| D — `@ExpectedFailure` dazu | Jest `test.failing`, pytest `xfail` | vierter Zustand; bei falscher Benutzung eine Maschine zum Verstecken von Bugs |

**Empfehlung: C, D nicht.** Ein statisches Überspringen ist eine Entscheidung, ein Laufzeit-Skip
ist eine Tatsache über die Maschine. **Wichtige Folge:** die Zusammenfassungszeile bekommt drei
Zahlen, und ein Lauf, der alles übersprungen hat, darf nicht grün aussehen — dasselbe Argument, das
1.5(f) für den Nulllauf macht.
**Bruch:** nein. **Hängt an:** T12 (die Ausgabeform muss den dritten Zustand tragen), T31.

### T11 — Wird die Ausgabe eines Tests aufgefangen?

**Heute:** nein, sie läuft durch (gelesen `src/Lyrtest/Program.cs:143`; gemessen in `p04` und
`v2-r09`: „this ran" vor der `PASS`-Zeile).

| Option | Vorbild | Preis |
|---|---|---|
| A — je Test auffangen, nur bei Fehlschlag ausgeben | pytest (Default), Go (ohne `-v`), Jest | ein Test, der hängt, gibt gar nichts aus; Debugging per `println` braucht eine Ausnahme |
| B — durchlassen | Status quo | ein grüner Lauf ist nicht lesbar; ein paralleler Lauf wäre unlesbar |
| C — A mit `-s`/`--show-output` | pytest, Rust `--nocapture` | ein Flag |

**Empfehlung: C.** Kosten sind gering: der Runner übergibt der VM schon einen `Output`-Writer
(`Program.cs:143`) — Auffangen ist ein Writer-Tausch, keine Architektur. Es ist außerdem die
Vorbedingung dafür, dass T14 (Parallelität) überhaupt lesbar sein kann.
**Bruch:** minor (Ausgabe verschwindet aus grünen Läufen). **Hängt an:** T13 (ein hängender Test
darf nicht auch noch stumm sein).

### T12 — Maschinenlesbare Ausgabe

**Heute:** nur Text, Exit 0/1. Kein `--json`, kein JUnit-XML, keine Laufzeit je Test, kein
Panik-Code in der Fehlerzeile (1.5e, gemessen). Exit 1 steht gleichzeitig für „ein Test ist rot",
„eine Datei kompiliert nicht" und „der Filter traf nichts" (gelesen, `Program.cs:199–207`) — und
Exit **irgendwas** für „ein Test hat `exit()` gerufen" (gemessen, 1.5f).

| Option | Vorbild | Preis |
|---|---|---|
| A — `--json`, ein Ereignis pro Zeile | Go `-json` | eine Form mehr zu pflegen |
| B — JUnit-XML | Jest, pytest, praktisch jedes CI | ein Fremdformat im Baum |
| C — A, plus ein winziger Konverter nach B | — | zwei Teile, aber nur eine Wahrheit |
| D — nichts | Status quo | CI kann nur „grün/rot" |

**Empfehlung: C**, plus die Exit-Code-Frage, die jetzt eigenständig als T31 steht. Korrektur
gegenüber der alten Fassung: sie stützte C darauf, `lyrc --json` sei „unvollständig (PLAN.md §D)" —
gelesen, `docs/Befunde_und_Verbesserungen/PLAN.md:177`, trägt diese Zeile inzwischen **✅**,
erledigt mit PR #171 („Argumentfehler kamen als Textzeile in einen Strom, den jemand parst"). Das
macht C eher **stärker** — die eine Maschinenform der Werkzeugkette ist fertig und `lyrtest` ist
das Werkzeug, das sie nicht spricht —, aber die Begründung war falsch.

**Und C muss beantworten, was ein Ereignis trägt**, nicht nur welches Format es hat. Das ist T25:
die Ergebniszeile heißt `modul.funktion` und trägt weder Datei noch Zeile (gemessen, jede Probe).
**Bruch:** nein. **Hängt an:** Diagnostik-Gebiet (`--json`-Schema), T25 (Inhalt), T31 (Exit-Codes),
T10 (dritter Zustand).

### T13 — Zeitlimits

**Heute:** keine. `CallVoid(test)` bekommt keinen Budget-Parameter (`src/Lyrtest/Program.cs:178`).
Der Konformanzrunner hat ebenfalls keins (`lyric-spec/tools/run_conformance.py:53,73`). Ein Test in
einer Endlosschleife hängt CI, bis die Plattform abbricht.

| Option | Vorbild | Preis |
|---|---|---|
| A — ein Gesamtlimit für den Lauf, als **Wanduhr** | Go (`-timeout`, Default 10 min, druckt alle Stacks) | sagt nicht, welcher Test hing, wenn er nicht gedruckt wird; braucht einen zweiten Thread |
| B — `--timeout` je Test, als **Wanduhr** | swift-testing `.timeLimit`, pytest-timeout | ein langsamer Test wird plötzlich rot; ein blockierendes Native muss abbrechbar sein |
| C — `ExecutionBudget` je Test, als **Instruktionszähler** | die VM selbst | trifft den teuersten Fall **nicht**, s. u. |
| D — Kindprozess je Datei mit Prozess-Timeout | Jest, jedes CI | fällt mit T28 zusammen; löst beides auf einmal |

**Die alte Empfehlung war falsch, und zwar begrifflich.** Sie lautete „C, mit dem Budget, das die VM
schon hat", und nannte Gos `-timeout` und swift-testings `.timeLimit` als Vorbilder. Das sind
**Wanduhren**, C ist keine. Gelesen, `src/Lyric.Vm/ExecutionBudget.cs:13–18`:

> „COUNTED, NOT TIMED, and that is the point rather than a limitation … A wall-clock limit would be
> the other design — it needs a second thread, and it answers differently on a loaded machine than
> on a quiet one."

Ein Instruktionszähler stoppt genau den Fall **nicht**, den das Dossier „die teuerste Fehlerart"
nennt: ein Test, der in einem Native blockiert (`sleep`, Socket-Read, `process.wait`,
`console.readLine`), verbraucht keine Instruktion. Die alte Empfehlung empfahl also zwei
Mechanismen und nannte sie einen.

**Zweiter Fehler im selben Punkt:** ein erschöpftes Budget kommt als
`ScriptBudgetException : ScriptPanicException` (gelesen,
`src/Lyric.Embedding/ScriptException.cs:24–25, 63–66`) und läuft damit in `Program.cs:181–188` —
den Zweig, der den Diagnosecode wegwirft (1.5e). Der Benutzer erführe nicht einmal, dass das Budget
die Ursache war.

**Empfehlung: A als Wanduhr für den ganzen Lauf, dazu D prüfen; C nur als Zusatz, und dann mit
eigener Ausgangsart.** Begründung: die Wanduhr ist die einzige Form, die den blockierenden Fall
fängt, und A ist die billigste davon (ein Timer-Thread, der den Lauf abbricht und sagt, welcher
Test zuletzt begann). D ist teurer, löst aber T28 mit und gibt dem Timeout einen sauberen
Abbruchmechanismus — ein Kindprozess lässt sich töten, ein blockierendes Native im eigenen Prozess
nicht. C hat einen eigenen, kleineren Nutzen: es ist reproduzierbar und eignet sich für
„dieser Test darf höchstens N Instruktionen brauchen" als Regressionsschranke — aber dann muss der
`ScriptBudgetException`-Fall eine eigene Ausgangsart bekommen, nicht `FAIL` ohne Code.
Der Konformanzrunner braucht A ebenfalls, eine Zeile Python (`subprocess.run(…, timeout=…)`).
**Bruch:** minor (ein langsamer Test wird rot). **Hängt an:** T28 (Prozess), T12/T31 (die
Ausgangsart „Zeit abgelaufen" braucht Form und Code), VM-Gebiet (darf ein blockierendes Native
abgebrochen werden?).

### T14 — Parallelität und Reihenfolge

**Heute:** streng sequenziell; Dateien ordinal, Tests in Deklarationsreihenfolge (gemessen, `p09`).
Kein Shuffle, kein Seed. Gemessen kostet das linear: 18,4 s auf 500 Dateien (1.8).

| Option | Vorbild | Preis |
|---|---|---|
| A — sequenziell lassen | Status quo | eine wachsende Suite wird linear langsamer — gemessen, 1.8 |
| B — parallel über **Dateien/Module**, je Einheit eine VM auf einem eigenen .NET-Thread | **ExUnit (`async: true` je Modul)**, Jest (Worker pro Datei), Rust (Default) | **muss ausdrücklich benannt werden**: das bricht „single-threaded plus Koroutinen" nicht — der *Runner* ist ein Host, und mehrere VMs nebeneinander sind schon heute die Annahme (`STATUS.md:645` nennt Cross-VM-Isolation „argued, not tested") |
| C — Shuffle mit gedrucktem Seed | ExUnit (Default) | deckt Reihenfolgeabhängigkeit auf, die sonst erst in CI auffällt |

**Empfehlung: B als `--jobs`, C als `--shuffle`, beide opt-in.** Korrektur des Vorbilds:
**ExUnit ist der Präzedenzfall für genau dieses Korn** — sein `async: true` steht auf dem Modul und
fährt Module nebenläufig zueinander, während Tests innerhalb eines Moduls sequenziell bleiben. Die
alte Tabelle stellte ExUnit als „je Test ein Prozess" dar und machte es damit zum Gegenpol; das war
eine Verwechslung von Isolation und Parallelität.

Zweite Korrektur: Rusts `--shuffle`/`--shuffle-seed` sind **nicht stabil** — libtest verlangt
dafür `-Z unstable-options` auf nightly. Als gleichrangiges Vorbild neben ExUnits Default
überzeichnete das die Verbreitung. Das Vorbild für C ist ExUnit, Punkt.

B verlangt vorher, dass Cross-VM-Isolation nicht mehr nur behauptet ist. C ist billig und findet
genau die Fehlerklasse, die T4 offen lässt.
**Bruch:** nein. **Hängt an:** VM-Gebiet (Cross-VM-Isolation), T11 (Ausgabe auffangen), T34
(mehrere Scheduler nebeneinander), T32.

### T15 — Property-based Testing

**Heute:** nichts, und nicht ausdrückbar: ein Generator braucht ein **statisches**
Interface-Member (`static fn arbitrary(rng: Rng): Self`), das erst 4.7-Fundament #4 ist (gelesen,
PLAN.md:258–260). `std.random` hat keinen spezifizierten Algorithmus, ist also über Plattformen
nicht reproduzierbar (`lyric-v5-features.md:115`). **Und ein `@Property` darf heute nicht generisch
sein** (`LYR-SEM0067`, gemessen, T27) — ein Gesetz über `List<T>` ist also gar nicht formulierbar.

| Option | Vorbild | Preis |
|---|---|---|
| A — nichts | Status quo | die Fehlerklasse „das Gesetz gilt nicht" bleibt unprüfbar |
| B — QuickCheck-Modell: `Arbitrary`-Interface mit statischem Member, `@Property`-Test mit Parametern, Shrinking, gedruckter Seed | Haskell QuickCheck, Rust `quickcheck` | braucht #4 **und** Synthese (#2/#3); ein `@Property` nimmt Parameter — Ausnahme vom `@Test`-Vertrag (T7); und eine Antwort auf `SEM0067` (T27) |
| C — Hedgehog-Modell: Generatoren sind **Werte**, explizit übergeben, integriertes Shrinking | Hedgehog (Haskell), `proptest` (Rust) | keine Typauflösung nötig, also **ohne #4 baubar**; dafür schreibt der Benutzer den Generator immer hin |
| D — Fuzzing statt Property | Go 1.18 `FuzzXxx` | braucht Coverage-Instrumentierung (T19) |

**Empfehlung: C als Form, B als spätere Bequemlichkeit.** C ist der einzige Weg, der **vor** dem
4.7-Fundament baubar ist, und sein integriertes Shrinking ist ohnehin das bessere Modell — ein
Gegenbeispiel ohne Schrumpfung ist meist unlesbar. Sobald #4 und die Synthese stehen, liefert
`Arbitrary` die Default-Generatoren und C bleibt der Ausweg für alles Spezielle. **Und C umgeht
`SEM0067`**: ein Generator als Wert braucht keine generische Deklaration mit Attribut.
**Bruch:** nein. **Hängt an:** T27 (`SEM0067`), statische Interface-Member (4.7 #4),
Konformanz-Synthese (4.7 #2), reproduzierbares `std.random`, T7.

### T16 — Snapshot-Tests

**Heute:** nichts für Lyric. Die Werkzeugkette hat den Mechanismus **fünfmal** in C# gebaut
(`tests/Lyric.Tests.Ir/GoldenTests.cs:18` und die drei Geschwister, plus
`Lyric.Tests.DocGen/ExtractorTests.cs:19`), alle über `LYRIC_UPDATE_SNAPSHOTS=1`
(CONTRIBUTING.md:137–138).

| Option | Vorbild | Preis |
|---|---|---|
| A — `assertSnapshot(name, text)` + `lyrtest --update-snapshots` | Jest, `insta`, `swift-snapshot-testing` | Snapshots verrotten; `-u` ist eine Maschine zum Committen falscher Erwartungen |
| B — Inline-Snapshots, die den Quelltext zurückschreiben | Jest `toMatchInlineSnapshot`, `insta` | das Werkzeug editiert Benutzerquelltext — `lyric fmt` tut das zwar schon, aber mit einer Regel, nicht mit Daten; und es kollidiert mit T29 |
| C — Go-Form: die Erwartung steht im Doc-Kommentar (`// Output:`) | Go `Example` | **kein Snapshot** — die Erwartung ist von Hand geschrieben, nichts erzeugt oder aktualisiert sie; fällt mit T17 zusammen |
| D — nichts | Status quo | jeder baut sich `assertEq(actual, "…langer String…")` — und das geht heute, weil `string` die einzige zusammengesetzte Form ist, auf der `assertEq` funktioniert (gemessen, 1.6) |

**Empfehlung: A, mit C über T17 nebenbei, B nicht.** Preis ausdrücklich benennen: `--update` darf
**nie** Default sein, und der Diff muss durch ein Review. B gehört, wenn überhaupt, zu `lyrfix`.
Korrektur: C ist in der Vergleichstabelle nicht mehr als Snapshot-Mechanismus geführt — es ist ein
Doc-Test mit Ausgabeerwartung, mehr nicht.
**Bruch:** nein. **Hängt an:** stabiles `Display` → Konformanz-Synthese; Dateizugriff im Test (hat
er, `Capability.All` — aber s. T23).

### T17 — Doc-Tests

**Heute:** nichts — steht aber schon als 4.7 #19 auf der Liste (gelesen, PLAN.md:289). Auch hier
hat die Werkzeugkette es für sich gebaut: jeder Guide-Schnipsel wird von der Suite kompiliert
(`docs/guide/15-attributes.md:296–297`).

| Option | Vorbild | Preis |
|---|---|---|
| A — ` ``` `-Blöcke in `///` kompilieren **und** laufen lassen | Rust, Elixir `doctest` | jeder Block wird Kompilationseinheit; heutige Doku, die Pseudo-Code zeigt, bricht → Opt-out-Marker nötig (Rusts ` ```ignore `/` ```no_run `) |
| B — Ausgabe-Erwartung dazu (`// Output:`) | Go `Example` | zweite Direktive im Doc-Kommentar |
| C — Doctest-Form mit Prompt (`>>>`) | Python | eine REPL-Schreibweise als Testformat — Lyrics REPL wiederholt heute Nebenwirkungen (`STATUS.md:2237–2241`) |
| D — nur **kompilieren**, nicht laufen lassen | Rusts ` ```no_run ` als Default | fängt die Verrottung (umbenannte Funktion), nicht die falsche Antwort |

**Empfehlung: D als erste Scheibe, dann A+B.** D fängt die Fehlerklasse, die praktisch immer
zuschlägt, und braucht keine Ausgabeprüfung. Der **Bruch** ist bei D schon da: ein Doc-Kommentar
mit illustrativem Pseudo-Code hört auf zu bauen — also von Anfang an mit Opt-out-Marker und einer
4.x-Warnstufe („dieser Block würde nicht kompilieren").
**Bruch:** minor. **Hängt an:** DocGen, T12, T31 (ein Doc-Test-Fehlschlag ist kein Testfehlschlag
in derselben Datei).

### T18 — Benchmarks

**Heute:** nichts in der Sprache. `tools/Bench/Program.cs` ist ein C#-Harness, der ganze
Lyric-Programme in-process misst; sein eigener Doc-Kommentar hält fest, dass die angepassten
Spalten mit Format 3.6 unzuverlässig wurden, weil die Instruktionsauswahl die Baseline-Annahme
zerbrach (gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A — `@Bench`-Funktionen, der Runner besitzt die Schleife und skaliert die Iterationszahl | Go `BenchmarkXxx`/`b.N` | der Runner braucht eine monotone Uhr; ein Benchmark darf **nicht** im normalen `lyric test` laufen |
| B — nur `Monotonic`/`Stopwatch` in `std.time`, Harness Sache des Benutzers | Rust vor Criterion | jeder misst anders; Zahlen sind nicht vergleichbar |
| C — externes Werkzeug wie BenchmarkDotNet oder Criterion | C#, Rust, Haskell `criterion` | ein weiteres Binary; für ein Ein-Mann-Projekt zu viel |

**Empfehlung: A, in der Go-Form.** Nur dort bedeutet die Zahl über Maschinen hinweg etwas, und nur
dort ist eine Regressionsschranke in CI möglich. Braucht `Monotonic` in `std.time`, das
`lyric-v5-features.md:116` ohnehin als Lücke führt. Preis benennen: ein Benchmark-Harness
verspricht Rauschfreiheit, die er selten halten kann — Gos automatische `b.N`-Skalierung ist das
Minimum, das funktioniert; `tools/Bench` ist der hauseigene Beleg dafür, wie so etwas kaputtgeht.
**Nachtrag:** ein `@Bench` ist ein zweiter Attributvertrag mit einer anderen Signatur — also
genau der Fall, für den T7 B gebaut wäre.
**Bruch:** nein. **Hängt an:** `std.time` (`Monotonic`), T7, T12, T9 (Benchmarks als eigener Tag).

### T19 — Coverage

**Heute:** nichts, nirgends (`grep -ri coverage src stdlib docs` → nur „return coverage" in der
Sema). `lyric-v5-features.md:166` führt es unter „Werkzeuge".

| Option | Vorbild | Preis |
|---|---|---|
| A — Instrumentierung beim Lowering (ein Zähler je Basisblock), hinter einem Profil | Rust `-C instrument-coverage`, Jest/babel-istanbul, **GHC `-fhpc`** | das Gemessene ist nicht das Ausgelieferte; neuer IR-Pass |
| B — Zähler in der VM über die **Source-Map**, die das Format schon trägt | Go `-cover` (dort allerdings Rewriting), Python `sys.settrace` | Interpreter wird im Coverage-Modus langsamer; keine Aussage über nicht gelowerten Code; **die Source-Map trägt nur Datei und Zeile, keine Spalte** (gelesen, `BytecodeSourceMap.cs:11`) |
| C — nichts | Status quo | niemand weiß, was die 178 Konformanzfälle und die Suite wirklich abdecken |

**Empfehlung: B zuerst.** Die Source-Map existiert (`--no-source-map` schaltet sie ab), die VM
läuft ohnehin Instruktion für Instruktion, und es braucht weder einen IR-Pass noch eine
Formatänderung. A ist schneller, misst aber eine andere Form als die ausgelieferte. Coverage ist
außerdem die Vorbedingung für Go-artiges Fuzzing (T15 D). **Korrektur des Vorbildfelds:** Haskell
gehört hier zu den Sprachen, die Coverage im Standardwerkzeug haben (HPC), nicht zu denen ohne —
die alte Tabelle hatte dort ein „—".
**Bruch:** nein. **Hängt an:** Bytecode-/Source-Map-Gebiet (Spalten?), Profile.

### T20 — Was darf ein Konformanzfall festnageln?

**Heute** (gelesen, `lyric-spec/conformance/README.md:9–26` und `tools/run_conformance.py`):
`run`/`check`, `exit:`, `panic:`, `stdout:`, `error:` (wiederholbar), `warning:`, `since:`,
`until:`. Kein `stderr:`, keine Zeilen-/Spaltenangabe, kein Timeout (`run_conformance.py:53,73`).

**Drei Löcher neben dem, nach dem die alte Fassung fragte** — alle gelesen in
`run_conformance.py`:

1. **`error:` prüft Teilstrings und nie die ANZAHL** (Z. 56–62): ein Fall besteht auch dann, wenn
   der Compiler fünf weitere Fehler meldet. Es gibt kein `only`.
2. **`stdout:` wird ausschließlich im `run`-Modus geprüft** (Z. 69–72 gegen 82–86): ein `check`-
   oder `error:`-Fall kann keine Ausgabe festnageln — und `check` ohne `warning:` prüft nur, dass
   der Diagnosetext leer ist, nie stdout.
3. **Der Vergleich macht `rstrip("\n")`** (Z. 83), obwohl `conformance/README.md:15` „byte-exact,
   LF line ends" verspricht. Abschließende Leerzeilen sind also nicht pinnbar, und die README
   verspricht mehr, als der Runner hält.

| Option | Vorbild | Preis |
|---|---|---|
| A — `error: LYR-SEM0001 @ 7:5` und ein `only`-Zusatz | ui-Tests von rustc (`//~ ERROR` an der Zeile) | Fälle werden zerbrechlich gegen Diagnose-Umformulierungen |
| B — Timeout im Runner | jedes CI | eine Zeile Python |
| C — `stderr:` und `stdout:` in jedem Modus | — | zwei Direktiven mehr |
| D — Zeilenenden: entweder der Runner hält die Zusage oder die README wird korrigiert | — | eine Entscheidung, keine Arbeit |
| E — lassen: der Runner soll in 150 Zeilen nachbaubar bleiben (`conformance/README.md:4–5`) | — | die heutige Lockerheit bleibt |

**Empfehlung: B und D sofort, A und C als *optionale* Direktiven.** Ein fremder Runner, der `@ 7:5`
oder `only` ignoriert, kommt weiter zum richtigen Urteil — das ist genau der `since:`-Präzedenzfall,
und damit bleibt die 150-Zeilen-Zusage erhalten. D ist keine Arbeit, sondern eine Entscheidung, und
solange sie aussteht, widerspricht die Spezifikation ihrem eigenen Referenzrunner.
**Bruch:** nein. **Hängt an:** Spec-Prozess (spec-first), Diagnostik-Gebiet, T31.

### T21 — Läuft die Suite in beiden Profilen?

**Heute:** `lyrtest --release` funktioniert (gemessen, `p01`), Guide 20:55–57 nennt es „how a suite
notices an optimizer that changed an answer". Es ist ein Flag, das niemand zu benutzen gezwungen
ist.

| Option | Vorbild | Preis |
|---|---|---|
| A — Flag lassen | Status quo | der Optimierer wird nur geprüft, wenn jemand daran denkt |
| B — CI läuft beide, immer | die Werkzeugkette tut das für die Beispiele (PLAN.md §A: „80 Beispiele × 2 Profile") | doppelte Laufzeit — gemessen relevant, s. 1.8 |
| C — ein Test-Merkmal „dieser Test nagelt Optimiererverhalten fest" | — | dritter Testbegriff |

**Empfehlung: B für die eigene Suite, A für Benutzerprojekte, C nicht.** Die Projekt-eigene Praxis
ist schon B — sie gehört nur dokumentiert statt vorausgesetzt. **Nachtrag aus 1.8:** doppelte
Laufzeit ist bei linearer Skalierung (18,4 s auf 500 Dateien) keine Nebensache; B und T6 A gehören
zusammen entschieden, weil A die Verdopplung wieder bezahlt.
**Bruch:** nein. **Hängt an:** T12 (CI-Form), T6/T32 (Laufzeit).

### T22 — `fail()`, `never`, und die schiefe Assertionsmenge

**Heute:** `fail` gibt `void` zurück, nicht `never` — `fn pick(n: int): int { … fail("…"); }` ist
`LYR-SEM0017` (gemessen, `p05`). `stdlib/std/test.lyr:89–90` nennt den Grund: „until the language
lets a library function say `never`". Dazu die Schiefe: `assertLess` ja, `assertGreater` nein;
`assertTrue(c, msg)` verlangt eine Meldung, `assertNull(v)` nimmt keine; kein `assertFalse`
(gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| A — `never` als **Rückgabetyp** einer Bibliotheksfunktion zulassen (nicht als allgemeiner Typ) | Rust `!` als Rückgabe, Swift `Never`, Kotlin `Nothing` | **bricht einen ausdrücklichen Ausschluss**: „never als allgemeiner Typ" steht auf der Nein-Liste. Eine enge Ausnahme (nur Rückgabeposition, wirkt nur auf die Flussanalyse) ist aber genau das, was `panic` schon hat |
| B — die Menge von Hand vervollständigen | xUnit | wächst nie zu Ende, siehe T1 |
| C — T1 Option B macht die meisten überflüssig | Elixir, pytest | `fail` bleibt trotzdem kein Terminator |

**Empfehlung: A in der engen Form, zusammen mit C.** Der Ausschluss ist gegen `never` als *Typ*
gerichtet, nicht gegen `never` als *Rückgabeposition*; `panic` hat es bereits, und der einzige
Grund, warum `fail` es nicht hat, ist dass keine Bibliotheksfunktion es aufschreiben darf. Das ist
eine Inkonsistenz, keine Designentscheidung. **Muss als Bruch des Ausschlusses ausdrücklich
begründet werden**, wenn es in die Runde geht.
**Bruch:** nein (additiv). **Hängt an:** Typsystem-Gebiet (`never`-Ausschluss), T1, T30.

---

### T23 — Unter welchen Capabilities läuft ein Test? *(neu nach der Kritik)*

**Heute:** fest unter allen. Gelesen, `src/Lyrtest/Program.cs:132–134`:
`Capabilities = Capability.All`, mit der Begründung im Kommentar darüber („a test is the user's own
code run on their own machine, the same standing as 'lyric run'"). Guide 20:52–54 sagt dasselbe.
Gemessen: `lyrtest --grant none .` → `error[LYR-CLI0003]: unknown argument: --grant`, Exit 2 — es
gibt keinen Schalter.

Die Gegenprobe, die zeigt, dass der Mechanismus existiert (gemessen, `v2-r11/`): dasselbe Programm
unter `lyrvm run`:

```
$ lyrvm run needs_os.lyrbc --grant none
error[LYR-CAP0001]: module requires capability 'osAccess', which this runtime does not grant   EXIT=1
$ lyrvm run needs_os.lyrbc --grant all      (KONTROLLE)
windows                                                                                        EXIT=0
```

Und `lyric.json` hat **kein** Capability-Feld (gelesen, `src/Lyric.Core/ProjectFile.cs:40–78`:
Directory, Name, SourceRoot, TestRoot, Toolchain, NativeRoots, Dependencies, Warnings). Es gibt also
heute gar nichts, was ein Test erben könnte.

Für eine Sprache, deren **zweites erklärtes Ziel** ein kapabilitätenbasiertes Sandbox-Modell ist
(CLAUDE.md §Was Lyric ist), heißt das: **keine Testsuite prüft jemals, ob das Programm unter den
Rechten läuft, mit denen es ausgeliefert wird.**

| Option | Vorbild | Preis |
|---|---|---|
| A — lassen: `Capability.All`, ein Test ist eigener Code | Status quo | die eine Eigenschaft, die Lyric von einer gewöhnlichen Skriptsprache trennt, ist untestbar |
| B — `lyrtest --grant <liste>`, wie `lyrvm run` | **`lyrvm run --grant`** (gemessen, existiert) | ein Flag; beantwortet nicht, was der *Default* ist |
| C — `lyric.json` bekommt eine Capability-Deklaration, `lyrtest` nimmt sie als Default | Deno `--allow-*` im Manifest, Cargo-Features | ein neues Feld im Projektformat; es fehlt heute ganz, ist also Build-Gebiet |
| D — `@Test { capabilities = "file,net" }` je Test | swift-testing Traits | ein Test kann sich Rechte geben, die das Produkt nicht hat — genau verkehrt herum |
| E — eine Assertion „dieser Aufruf MUSS an `LYR-CAP0001` scheitern" | — | braucht T2 (Erwarte-Fehler) und einen fangbaren Ladefehler |

**Empfehlung: B sofort, C sobald das Build-Gebiet ein Feld dafür hat, E über T2 B.** B ist ein
Nachmittag und macht die Frage überhaupt stellbar. C ist die richtige Antwort auf „unter welchen
Rechten läuft mein Programm" — der Default eines Testlaufs soll das sein, was ausgeliefert wird,
nicht mehr. D ist ausdrücklich abzulehnen: ein Test darf Rechte **entziehen**, nie hinzufügen. E
ist die Assertion, die aus B und C erst einen Test macht statt einer Einstellung.
**Bruch:** minor bei C — ein Test, der heute unter `All` grün ist, wird rot, sobald das Projekt
weniger deklariert. Genau das ist der Punkt, und es braucht eine 4.x-Warnung („this test reaches
`osAccess`, which the project does not declare").
**Hängt an:** Build-Gebiet (Capability-Feld in `lyric.json`), T2 (Erwarte-Fehler), FFI-Gebiet.

### T24 — Wie testet man Lyric-Code, der gegen Host-Natives läuft? *(neu nach der Kritik)*

**Heute:** gar nicht isoliert. `lyrtest` reicht die `nativeRoots` des Projekts durch (gelesen,
`src/Lyrtest/Program.cs:137`), aber es gibt **keine Form, eine Host-Funktion oder ein `@System`-
Native für einen Test zu ersetzen** (Fake/Stub) oder zu behaupten, dass sie mit bestimmten
Argumenten gerufen wurde. Ein SDK-Autor, der die Lyric-Seite seines Vertrags prüfen will, schreibt
heute einen C#-Test — also genau das Muster, das 1.9 als Fehler benennt.

Technisch ist der Hebel da: `LoadedProgram.Load(module, natives, …)` nimmt die `NativeRegistry`
**als Parameter** (gelesen, `src/Lyric.Vm/LoadedProgram.cs:72–73, 88`), und die Bindung geschieht
pro Load, also heute schon pro Test (T4).

| Option | Vorbild | Preis |
|---|---|---|
| A — nichts; Host-Natives testet der Host in seiner Sprache | Status quo | der SDK-Autor kann seine Lyric-Seite nicht in Lyric prüfen |
| B — eine testeigene zweite Native-Registry: `@Fake`-markierte Lyric-Funktionen ersetzen gleichnamige Natives für die Dauer eines Tests | Jest `jest.mock`, Go (Interface + Fake) | der Runner muss zwei Registries mischen; ein Fake mit falscher Signatur muss ein Fehler sein, nicht ein stiller Ersatz |
| C — Natives hinter einem Lyric-Interface, der Test übergibt eine andere Implementierung | Go, C# (DI) | keine Werkzeugarbeit, aber es zwingt jedes SDK zu einer Indirektionsschicht |
| D — `lyrstub`-Weg: ein generierter Stub-Baum, gegen den Tests laufen | die Werkzeugkette hat `src/Lyrstub` bereits | ein Stub beweist, dass es kompiliert, nicht dass es stimmt |

**Empfehlung: C als Sprachantwort, B als Werkzeugantwort — und C zuerst.** C ist Rule-2-konform: es
braucht **keinen** neuen Mechanismus, nur Interfaces, die es gibt. B ist mächtiger und teurer, und
es hat eine unangenehme Eigenschaft — ein Fake, der nur im Test existiert, prüft nicht mehr den
ausgelieferten Pfad, und das ist genau die Kritik, die A an B richtet. Wenn B kommt, dann mit einer
Regel: ein Fake muss die Signatur des Originals tragen, und der Runner muss melden, welche Natives
gefälscht waren.
**Bruch:** nein (beides additiv). **Hängt an:** FFI-/Embedding-Gebiet (Registry pro Instanz), T7
(ein `@Fake` ist ein weiterer Attributvertrag), T23 (ein Fake ersetzt auch die Capability-Frage
nicht).

### T25 — Wie findet ein Editor einen Test? *(neu nach der Kritik)*

**Heute:** gar nicht. Drei Messungen und drei Lesungen:

- Die Ergebniszeile ist `modul.funktion` und trägt **weder Datei noch Zeile** (gemessen, jede
  Probe; `PASS shared_tests.one` zweimal für zwei verschiedene Dateien, `v2-r03`).
- Es gibt **kein `--list`/`--collect-only`**: `lyrtest --help` listet sechs Optionsnamen, keiner
  davon zählt auf (gemessen).
- Die Attributzeile im Modul trägt **keine Quellposition**: `AttributeUse` hat Attribute,
  TargetKind, Target, TargetName, Values (gelesen,
  `src/Lyric.Core/Bytecode/ModuleAttributes.cs:128–141`), und `BytecodeAttribute` ebenso
  (gelesen, `src/Lyric.Core/Bytecode/BytecodeModule.cs:90–99`).
- Die Source-Map gibt **Datei und Zeile, keine Spalte**, und zwar je (Funktion, Offset) — gelesen,
  `src/Lyric.Core/Bytecode/BytecodeSourceMap.cs:4,11,36`.
- Der LSP weiß **nichts** von Tests: `grep -rn '"Test"\|@Test\|testRoot' src/Lyric.Lsp/` → kein
  Treffer, und `CodeLens` kommt in `src/Lyric.Lsp/` nicht vor (gemessen per grep).

`vscode-lyric` und `jetbrains-lyric` existieren beide (v1.4.0). Ein Gutter-Icon, ein „Run this
test", ein Sprung zur Fehlstelle — nichts davon ist heute baubar.

| Option | Vorbild | Preis |
|---|---|---|
| A — `lyrtest --list --json`: je Test ein Ereignis mit Modul, Funktion, **Datei, Zeile** | Go `-json`, pytest `--collect-only -q`, Swift `swift test --list-tests` | die Position muss irgendwo herkommen — s. u. |
| B — der LSP liefert die Positionen, der Runner nur Namen; der Editor verbindet sie über den Namen | rust-analyzer (Runnables) | zwei Quellen, die auseinanderlaufen können; genau die Fehlerklasse aus 1.2 |
| C — die Attributzeile bekommt eine Quellposition im Modulformat | — | Formatänderung (`docs/Bytecode.md`), und sie nützt nur Werkzeugen |
| D — die Position der *Funktion* aus der Source-Map nehmen (erste Zeile ihres ersten Befehls) | — | ohne Formatänderung baubar; zeigt auf den Rumpf, nicht auf das `@Test`; **bricht unter `--no-source-map`** |

**Empfehlung: A, gespeist aus D, mit C als spätere Genauigkeit.** D reicht für das, was ein Editor
tatsächlich braucht — eine Zeile in der richtigen Datei —, und kostet keine Formatänderung. Der
Preis ist ehrlich zu nennen: unter `--no-source-map` gibt es keine Position, also muss `--list`
entweder das Profil erzwingen oder die Position als optional führen. C ist die saubere Antwort und
gehört ins Bytecode-Gebiet, nicht hierher.

**Was A tragen muss**, und das ist der eigentliche Inhalt dieser Frage: Modul, Funktion, Datei,
Zeile, Tags (T9 C), Skip-Grund (T10), und beim Ergebnis zusätzlich Dauer, Ausgangsart
(pass/fail/skip/timeout, T10/T13) und **Diagnosecode** (1.5e). T12 fragte nach dem *Format* und nie
nach dem *Inhalt*.
**Bruch:** nein. **Hängt an:** T12 (Format), T8 (der Name, der in der Zeile steht),
Editor-Gebiet, Bytecode-Gebiet (C).

### T26 — Was ist der Name eines Tests, wenn die Datei einen `module`-Header schreibt? *(neu nach der Kritik)*

**Heute:** der geschriebene Header wird von `lyrtest` **still verworfen**, der Dateibasisname
gewinnt. Gemessen, `v2-r06/`: `tests/bb_header_tests.lyr` mit `module deep.nested.name;` →
`PASS bb_header_tests.named`. `lyrc check` dagegen lässt den Header gelten (gemessen: ein
`import deep.nested.name` aus einer anderen Testdatei geht dort durch, unter `lyrtest` nicht).

Das ist exakt der Fehlertyp, den dieses Dossier selbst zum Maßstab macht — „eine Deklaration, die
nichts behauptet und nichts sagt" (T6, T8) —, und T8 stellte die Frage nicht.

**Der Diagnosecode dafür existiert schon.** KONTROLLE im Quellbaum (gemessen, `v2-r06b/`):
`src/thing.lyr` mit `module deep.nested.name;`, importiert als `thing` →
`error[LYR-RES0006]: this file was loaded as 'thing' but declares module 'deep.nested.name'`.
ZWEITE KONTROLLE: dieselbe Datei als **Einzeldatei** kompiliert (`lyrc check tests/…lyr`) → `ok`,
Exit 0. Die Stille gehört also dem Einzeldatei-Pfad, den `lyrtest` benutzt.

| Option | Vorbild | Preis |
|---|---|---|
| A — der Pfad gewinnt; ein abweichender Header ist `LYR-RES0006`, auch in der Testwurzel | **der Quellbaum selbst** (gemessen) | ein Header in einer Testdatei hört auf zu kompilieren — heute kompiliert er still |
| B — der Header gewinnt; der Pfad ist nur Fundort | Java (Paket im File), C# (Namespace) | zwei Dateien dürften denselben Modulnamen schreiben; die Kollision aus 1.2(c) bliebe |
| C — ein Header ist in einer Testwurzel schlicht verboten | — | eine Sonderregel für die Testwurzel, also ein zweiter Modulbegriff |
| D — lassen | Status quo | dritte Ausprägung der Divergenz aus 1.2 |

**Empfehlung: A.** Sie fällt mit T6 A und T8 A zusammen: sobald `lyrtest` die Testwurzel als eine
Kompilierung mit pfadbasierten Namen fährt, ist `LYR-RES0006` ohne jede neue Regel der Fehler, den
ein abweichender Header bekommt — der Code ist da, der Pfad ist da, nur das Werkzeug fährt ihn
nicht. C wäre ein zweiter Modulbegriff und verstößt gegen Rule 2.
**Bruch:** minor — eine Testdatei mit abweichendem Header hört auf zu kompilieren. 4.x: Warnung mit
demselben Text wie `RES0006`, bevor sie ein Fehler wird.
**Hängt an:** T6, T8, Modul-Gebiet (woher ein Modul seinen Namen hat).

### T27 — Darf ein Test generisch sein? *(neu nach der Kritik)*

**Heute:** nein. Gemessen, `v2-r10/`:

```
@Test pub fn generic_test<T>(): void { … }
→ error[LYR-SEM0067]: an attribute cannot sit on a generic declaration — there is one metadata
  row and as many instances as the program creates
```

Gleichlautend unter `lyrtest` und `lyrc check`. Die Regel ist auch gelesen:
`docs/guide/15-attributes.md:123–125` („neither a generic attribute struct nor a generic target is
allowed — the compiled module holds one row, and one row cannot stand for every instance").

Die Folge trifft drei andere Fragen: **jede Eigenschaft (T15) und jede Tabelle (T3) ist zwingend
monomorph**, und ein Gesetz über `List<T>` — „umdrehen zweimal ist Identität" — ist gar nicht
formulierbar. Zusammen mit 1.6 (`assertEq` scheitert auf `List<int>`) heißt das: die generischen
Teile der eigenen Standardbibliothek sind die am schlechtesten testbaren.

| Option | Vorbild | Preis |
|---|---|---|
| A — lassen; Tests werden über konkrete Instanzen geschrieben | Status quo, und es ist die einfache Antwort | `List<int>`, `List<string>`, `List<Point>` sind drei handgeschriebene Kopien |
| B — eine Ausnahme von `SEM0067` für **tool-gelesene** Attribute: eine Zeile je Instanziierung | — | die Begründung der Regel („eine Metadatenzeile") fällt; das Modulformat muss N Zeilen tragen können |
| C — `@Test`-Instanziierung im Attribut benennen: `@Test<int>` / `@Case<int>(…)` | C# `[Theory]` mit generischer Testmethode (xUnit kann das), Swift `@Test(arguments:)` über konkrete Typen | eine Typargumentliste in einer Attributzeile — neu im Format |
| D — ein Makro/Generator, der die Monomorphisierungen schreibt | Rust `macro_rules!` in Tests | Lyric hat kein Makrosystem und will keins |

**Empfehlung: A für jetzt, C als die Form, die es später sein müsste — B nicht.** A ist keine gute
Antwort, aber sie ist ehrlich: dreimal `assertEq` über drei konkrete Typen ist lesbar und
funktioniert. B zieht die Regel von `SEM0067` um, und die Regel ist richtig — eine Attributzeile
ist Metadaten am Programm, und ein generisches Programm hat sie noch nicht. C ist die einzige Form,
die beides hält: die Zeile bleibt eine, und sie nennt ihre Instanz. Sie gehört aber ins
Attribut-/Metadaten-Gebiet, nicht hierher; **dieses Gebiet liefert nur den Bedarf**.
**Bruch:** nein (A), major (B — es ändert die Formatzusage). **Hängt an:** Attribut-/Metadaten-
Gebiet, Generics-Gebiet, T3, T15.

### T28 — Wer schützt den Runner-Prozess vor dem Testcode? *(neu nach der Kritik)*

**Heute:** niemand. Gemessen mit Kontrolle, `v2-r08-*/` (s. 1.5f): ein Test, der `std.os.exit(0)`
ruft, macht aus dem Lauf einen **grünen, vollständig stummen Nulllauf** — Exit 0, keine Zeile
Ausgabe, kein späterer Test läuft. Mit `exit(3)`: Exit 3, ebenfalls ohne jede Zeile. Die Kontrolle
im selben Projekt mit `assertEq(1,1)` statt `exit(…)` meldet drei `PASS` und `3 test(s), all
passed`.

`std.os.exit` ist gelesen belegt (`stdlib/std/os.lyr:18–20`): „Ends the process immediately with
this code. No `defer` runs and no `catch` applies."

Das ist dieselbe Frage wie T4 (Isolationseinheit), eine Ebene höher — und **T4 stellte sie nicht**.
Es ist außerdem der Fall, den T13 wirklich braucht: ein blockierendes Native lässt sich im eigenen
Prozess nicht abbrechen, in einem Kindprozess schon.

| Option | Vorbild | Preis |
|---|---|---|
| A — `std.os.exit` ist in einer Testwurzel ein Sema-Fehler | — | eine Sonderregel für eine Verzeichnisrolle; die Sprache kennt die Rolle heute nicht |
| B — im Testlauf wird das Native umgeleitet: `exit(n)` panikt mit einem eigenen Code | Jest (`process.exit` wird im Worker abgefangen), pytest (`SystemExit` wird gefangen) | zwei Bedeutungen eines Natives, je nach Wirt — muss ausdrücklich spezifiziert werden |
| C — der Runner fährt jede Datei in einem **Kindprozess** | **Jest (Worker pro Datei)**, ExUnit (Prozess pro Test), Go (`go test` baut ein Binary je Paket) | Prozessstart je Datei; die Ausgabe wird ein Protokoll statt eines Stroms; auf 500 Dateien ist das gemessen relevant (1.8) |
| D — lassen, dokumentieren | Status quo | ein grüner CI-Lauf, in dem nichts lief — die teuerste Form von falsch |

**Empfehlung: B sofort, C als die Form, die auch T13 und T14 löst.** B ist billig, sichtbar und
fängt den gemessenen Fall vollständig: ein `exit` im Test wird ein `FAIL` mit eigenem Code, statt
den Lauf zu verschlucken. Es hat einen echten Preis — dasselbe Native bedeutet in zwei Wirten
Verschiedenes —, und der ist zu benennen: `lyrtest` ist ein **Host**, und ein Host darf ein Native
ersetzen; das ist genau die Eigenschaft, die die Registry aus T24 beschreibt, und kein zweiter
Mechanismus.

C ist die vollständige Antwort und macht drei Fragen auf einmal zu: T28 (Prozess), T13 (Wanduhr mit
Abbruch) und T14 (Parallelität über Prozesse statt Threads, womit Cross-VM-Isolation gar nicht
mehr behauptet werden muss). Sie kostet aber Prozessstart je Datei und ist deshalb mit 1.8 und T6 A
zusammen zu rechnen: **eine Kompilierung für die Wurzel, N Kindprozesse für den Lauf** ist eine
andere Architektur als heute, und sie will bewusst gewählt werden.

A ist abzulehnen: die Sprache kennt „Testwurzel" nicht und soll sie nicht kennen — das war die
ganze Begründung für `@Test` als tool-gelesenes Attribut (`src/Lyrtest/Program.cs:11–14`).
**Bruch:** nein bei B (ein Test, der heute den Lauf verschluckt, wird rot — das ist die Reparatur,
nicht der Bruch). **Hängt an:** T4, T13, T14, T24 (Native-Ersatz), T31 (welcher Exit-Code).

### T29 — Was tut `lyric fmt` mit einer Liste von `@Case`-Zeilen? *(neu nach der Kritik)*

**Heute** (gemessen, `v2-r15/stacked.lyr`): der Formatierer faltet gestapelte Attribute in eine
Gruppe.

```lyr
@Marker                 →      @[Marker, Case(1)]
@Case(1)
pub fn f(): void { }           pub fn f(): void { }
```

Und `docs/guide/15-attributes.md:179–187` hält fest: „the same attribute twice in one group is still
the same attribute twice", und „`lyric fmt` treats the group as *the* shape for two or more
attributes: stacked lines fold into one group … Which spelling you type is taste; what a formatted
file holds is one shape." Die Wiederholungsregel selbst ist `LYR-SEM0068` (gemessen).

T3 Option A will das Wiederholungsverbot aufweichen. Dann formt `lyric fmt` **N Tabellenzeilen um**
— aus `@Case(1,2)` / `@Case(3,4)` / `@Case(5,6)` wird `@[Case(1,2), Case(3,4), Case(5,6)]`, eine
Zeile, die ab einer Breite umbricht „one entry per line".

| Option | Vorbild | Preis |
|---|---|---|
| A — `@Case` ist vom Falten **ausgenommen**: eine Tabellenzeile bleibt eine Quellzeile | — | eine Ausnahme im Formatierer, der ausdrücklich keine Optionen hat („There are no style options. The shape is the tool's contract", gemessen, `lyrfmt --help`) |
| B — Falten erlauben; die **geschriebene Reihenfolge** ist normativ und der Index `[0]`, `[1]` folgt ihr | xUnit (Reihenfolge der `[InlineData]`), Swift `@Test(arguments:)` | der Formatierer darf umbrechen, aber nie umordnen — das muss als Zusage aufgeschrieben werden |
| C — die Fälle bekommen **Namen** statt Indizes: `@Case(name = "negativ", …)` und `modul.fn[negativ]` | pytest-IDs, Go `t.Run("name", …)` | ein Pflichtfeld mehr je Zeile; dafür ist der Bezeichner stabil gegen jede Umformatierung |
| D — Tabellen gehen über T3 C (benannter Lieferant), nicht über wiederholte Attribute | xUnit `[MemberData]` | die Frage stellt sich nicht — der Formatierer sieht nur ein Attribut |

**Empfehlung: B als Regel, C als Zusage für `--filter` und die Maschinenform, D als der Weg, der
die Frage am saubersten vermeidet.** Der harte Punkt, den T3 beantworten muss: **was ist der stabile
Bezeichner einer Tabellenzeile?** Ein Index über die geschriebene Reihenfolge ist stabil, solange
niemand umordnet — aber ein `--filter modul.fn[2]` in einem CI-Skript zeigt nach einem Einschub in
der Mitte auf einen anderen Fall, still. Ein Name (C) ist stabil und kostet Tippen. A ist
abzulehnen: eine Ausnahme im Formatierer, der ausdrücklich vertragsfrei von Optionen ist, ist
teurer als sie aussieht.
**Bruch:** nein. **Hängt an:** T3, T9 (`--filter`), T12/T25 (Maschinenform), Formatierer-Gebiet.

### T30 — Wie migriert eine bestehende Testsuite auf 5? *(neu nach der Kritik)*

**Heute:** die Frage ist für dieses Gebiet nirgends gestellt worden, obwohl sie der Auftrag der
ganzen Runde ist — 4.x-Warnungen, die den Wechsel erlauben.

Was gemessen und gelesen feststeht:

- **Die Migrationsfläche in der eigenen Suite**: 22 Testdateien in `stdlib-tests/tests/`, darin
  **566 `assertEq`** und **321 `assertTrue`** (gemessen per grep), dazu 7 `assertNotEq`, je 3
  `assertLess`/`assertClose`, je 2 `assertNull`/`assertNotNull`/`assertContains`.
- **Das Werkzeug für Deprecations existiert**: `@Deprecated { message, until }` aus `std.core`
  (gelesen, `stdlib/std/core.lyr:500–508`) ist das **eine** compilergelesene Attribut; jede Nutzung
  warnt mit `LYR-SEM0076`, und `until` ist eine Zusage, die der Compiler durchsetzt — ein Build mit
  einer Toolchain, die die Version erreicht hat, ist `LYR-SEM0081`.
- **Das Migrationswerkzeug existiert nicht**: `lyrfix` ist in PLAN.md:343 und
  `lyric-v5-features.md:61,163` vorgesehen, es gibt kein `src/Lyrfix` (gemessen, `ls src/`).

| Option | Vorbild | Preis |
|---|---|---|
| A — `std.test` wird versioniert: `std.test` bleibt, `std.test2` ist das Neue | Python `from __future__`, Go-Modulversionen | zwei Bibliotheken für ein Konzept — Rule-2-Verstoß im Lehrbuchformat |
| B — die neun Funktionen bekommen `@Deprecated { until = "5.0" }`, das Neue steht daneben | **die eigene Praxis** (gelesen, `std.core` nutzt es) | eine Übergangszeit, in der beides existiert — aber mit Ablaufdatum, das der Compiler durchsetzt |
| C — `lyrfix` schreibt `assertEq(a, b)` → `assert a == b` und `assertTrue(c, msg)` → `assert c` mechanisch um | `cargo fix`, `gofix`, `2to3` | `lyrfix` muss gebaut werden; und `assertTrue(c, msg)` verliert die Meldung, wenn das neue `assert` sie nicht nimmt |
| D — harter Schnitt in 5.0 | — | 887 Aufrufstellen allein im eigenen Baum, von Hand |

**Empfehlung: B als Rückgrat, C als Bequemlichkeit, A nicht.** B ist das Verfahren, das dieses
Projekt für genau diesen Zweck gebaut hat, und es hat den Zwang eingebaut, den der Maintainer in
der Oil-Analyse vermisst hat: `until` lässt den Build stehenbleiben, statt auf jemandes Gedächtnis
zu warten.

**Vier Dinge, die B beantworten muss**, und die dieses Gebiet liefern muss statt sie offen zu
lassen:

1. **Mit welchem `until`?** Wenn `until = "5.0"`, dann ist die 5.0-Toolchain selbst der Zwang —
   `LYR-SEM0081` beim ersten Build. Das ist scharf und richtig.
2. **Wer bricht zuerst?** Die eigene `stdlib-tests`-Suite mit 887 Aufrufen, nicht der Benutzer. Das
   ist gut: der Maintainer merkt es als Erster.
3. **Kann `lyrfix` `assertTrue(c, msg)` verlustfrei umschreiben?** Nur, wenn das neue `assert` eine
   optionale Meldung nimmt. Das ist eine Anforderung an T1 B, die dort heute nicht steht.
4. **`assertClose` hat kein Gegenstück** in einem zerlegenden `assert` — Fließkomma-Toleranz ist
   keine Vergleichsoperation. Diese eine Funktion bleibt, und das ist die richtige Antwort, nicht
   eine Ausnahme.

**Bruch:** minor über eine Übergangszeit, major ab `until`. **Hängt an:** T1 (die Zielform), T22
(`fail`), `lyrfix` (Werkzeug-Gebiet), Diagnostik (`SEM0076`/`SEM0081`).

### T31 — Welche Exit-Codes sind überhaupt frei? *(neu nach der Kritik)*

**Heute** (gelesen, `src/Lyric.Core/ExitCodes.cs`): der Rahmen ist eng und normativ.

| Code | Bedeutung | Beleg |
|---|---|---|
| 0 | Success | `ExitCodes.cs:15` |
| 1 | „Load, validation, compile or IO error: the program never started" | `:18` |
| 2 | Usage — „so a caller can tell a misuse from a broken file" | `:22` |
| 101 | Panik — „Not 1, so a caller can tell it from a regular `return 1;`" | `:26` |

Und die Datei hält selbst fest: „A program returning `101` itself is indistinguishable from a
panic; that is unavoidable once both travel through one byte channel" (`:9–10`).

`lyrtest` benutzt heute nur 0, 1 und 2 (gelesen, `Program.cs:199–207`, `:49–51`, `:71–72`). Exit 1
steht gleichzeitig für drei verschiedene Dinge (T12). **Und ein Test darf den Exit-Code des Runners
frei setzen** — `std.os.exit(3)` → Exit 3 (gemessen, 1.5f/T28).

Dazu der Zwang von außen: die Konformanzsuite matcht auf **Exit 101** für eine Panik (gelesen,
`run_conformance.py:76`: `expected_exit = 101 if spec["panic"] else spec["exit"]`), und ein Fall
mit `exit: 7` erwartet 7. Der Raum ist also nicht frei, er ist belegt.

| Option | Vorbild | Preis |
|---|---|---|
| A — `lyrtest`: 0 alles grün, 1 mindestens ein Test rot, 2 Usage, **3 Lauf nicht zustande gekommen** (Kompilierfehler, Filter ohne Treffer), **4 Zeitüberschreitung** | Go (`go test`: 1 für Fehlschlag, 2 für Bauprobleme), pytest (0–5, benannt) | zwei neue Zahlen im normativen Satz — `ExitCodes` ist „normative for every runtime" |
| B — nur 0/1 behalten, alles andere über `--json` unterscheiden | Jest | ein CI-Schritt ohne JSON-Parser kann „rot" nicht von „gar nicht gelaufen" trennen |
| C — pytests Weg: ein voller benannter Satz (0 ok, 1 Fehlschläge, 2 abgebrochen, 3 intern, 4 Usage, 5 nichts gesammelt) | pytest | ein Fremdsatz neben dem eigenen; kollidiert mit 101 |
| D — lassen | Status quo | Exit 1 heißt drei Dinge; Exit 3 heißt „ein Test hat `exit(3)` gerufen" |

**Empfehlung: A, mit zwei ausdrücklichen Zusagen.** Erstens: **101 bleibt frei** — `lyrtest` darf
es nie selbst zurückgeben, sonst kollidiert es mit der Panik-Bedeutung, auf die die Konformanzsuite
matcht. Zweitens: **`std.os.exit` in einem Test darf den Exit-Code des Runners nicht mehr setzen**
— das ist T28 B, und ohne sie ist jede Exit-Code-Zusage wertlos, weil Testcode sie überschreiben
kann (gemessen).

„5 nichts gesammelt" aus pytest ist erwägenswert und fällt mit T10 zusammen: ein Lauf, in dem alles
übersprungen wurde, und ein Lauf, in dem nichts gefunden wurde, dürfen nicht wie ein grüner Lauf
aussehen — heute tun sie es (`no tests` → Exit 0, gelesen `Program.cs:104,112`).
**Bruch:** minor für CI-Skripte, die heute auf „1 = irgendwas ist schief" prüfen. **Hängt an:**
T12, T13, T10, T28, Spec-Prozess (`ExitCodes` ist normativ, also Spec-Änderung).

### T32 — Wie skaliert die Testwurzel? *(neu nach der Kritik)*

**Heute:** linear in der Zahl der Dateien, weil jede einzeln kompiliert wird und jede den ganzen
`sourceRoot` mitzieht. Neu gemessen (1.8), mit Kontrolle:

| N Testdateien | Tests | `lyrtest .` | `lyrc check .` |
|---|---|---|---|
| 100 (KONTROLLE) | 200 | 4,9 s | 0,68 s |
| 500 | 1000 | **18,4 s** | **1,06 s** |

Die Zahlen der alten Fassung — 2,46 s gegen 2,55 s auf 80 Dateien (`STATUS.md:437`), 13 Kompilate
werden 166 (`STATUS.md:462`) — stimmen, aber sie belegen nur, dass **die VM** billig ist. Das
Kompilat ist es nicht, und bei 500 Dateien ist der Unterschied eine Größenordnung.

| Option | Vorbild | Preis |
|---|---|---|
| A — lassen | Status quo | eine Suite, die auf 500 Dateien wächst, kostet 18 s je Lauf; mit `--release` (T21 B) 36 s |
| B — **eine Kompilierung für die ganze Testwurzel** (= T6 A) | **`lyric check` selbst** (gemessen: 17× schneller) | die Laufzeiteinheit muss neu bestimmt werden (T4) |
| C — inkrementell: nur geänderte Dateien neu kompilieren, Rest aus einem Cache | Go (Build-Cache), `cargo` | ein Cacheverzeichnis und eine Invalidierungsregel — beides gibt es im Build-Gebiet noch nicht |
| D — parallel (= T14 B) | Jest, ExUnit, Rust | macht linear zu linear-durch-Kerne, nicht zu konstant |

**Empfehlung: B, und erst danach D.** B ist gemessen der große Hebel und wird ohnehin für T6
gebraucht; D allein teilt 18,4 s durch die Kernzahl und lässt die Wurzelursache stehen. C ist die
richtige Antwort für sehr große Bäume und gehört mit dem Build-Gebiet abgestimmt, nicht hier
entschieden.

**Was B für T4 bedeutet**, und deshalb gehören sie zusammen: wenn die Wurzel ein Kompilat ist, ist
„eine VM je Datei" keine natürliche Einheit mehr. Die natürliche Einheit wird dann „eine VM je
Test" (T4 C) — was gemessen fast nichts kostet, weil `Instantiate` ohnehin je Test läuft.
**Bruch:** nein. **Hängt an:** T6, T4, T14, T21, Build-Gebiet (Cache).

### T33 — Was darf ein Konformanzfall über die QUALITÄT einer Diagnose sagen? *(neu nach der Kritik)*

**Heute:** sehr wenig, und weniger als die README verspricht. T20 fragte nach Zeile und Spalte; die
drei benachbarten Löcher stehen jetzt dort ausgeschrieben (T20, „Drei Löcher"). Diese Frage ist die
allgemeinere Form: **was ist an einer Diagnose normativ?**

Gelesen, `run_conformance.py` und `conformance/README.md`:

| Aspekt | Heute prüfbar? | Beleg |
|---|---|---|
| Diagnosecode vorhanden | ja, als Teilstring über den ganzen Text | `run_conformance.py:56–62` |
| **Anzahl** der Diagnosen | **nein** | ebd. — kein `only` |
| Zeile/Spalte | nein | ebd. |
| Text der Meldung | faktisch ja (Teilstring), aber ungewollt | ebd. |
| Reihenfolge mehrerer Diagnosen | nein | ebd. — jede wird einzeln gesucht |
| `stdout` bei einem `check`- oder `error:`-Fall | **nein** | `:69–72` gegen `:82–86` |
| `stderr` getrennt von Diagnosen | nein — es gibt keine Direktive | README:9–26 |
| Abschließende Leerzeilen | **nein**, trotz „byte-exact" | `:83` (`rstrip("\n")`) gegen README:15 |
| Warnung **abwesend** („und sonst nichts") | nur im `check`-Modus ohne `warning:` | `:69–71` |

| Option | Vorbild | Preis |
|---|---|---|
| A — `only` für `error:`/`warning:`: „genau diese und keine weitere" | rustc ui-Tests (`.stderr`-Dateien sind vollständig) | jeder Fall wird empfindlich gegen eine zusätzliche Notiz — aber genau das ist der Punkt |
| B — `stderr:` als eigene Direktive, `stdout:` in jedem Modus | — | zwei Direktiven; der Runner wächst um ~15 Zeilen |
| C — Zeilenenden: `rstrip` raus oder README korrigieren | — | eine Entscheidung |
| D — vollständige Erwartungsdateien statt Direktiven (`case.lyr` + `case.stderr`) | rustc, `insta` | bricht die „ein Fall = eine Datei"-Zusage der README; und es ist ein Snapshot mit allen Verrottungsproblemen (T16) |
| E — lassen | Status quo | ein Fall besteht, obwohl der Compiler vier Fehler zu viel meldet |

**Empfehlung: A, B und C — alle drei als *optionale* Direktiven bzw. als eine Korrektur, D nicht.**
Der Maßstab ist die Zusage aus `conformance/README.md:4–5`: „any implementation can build one in an
afternoon — the reference runner … is under 150 lines". A, B und C zusammen kosten rund 20 Zeilen
Python und lassen einen fremden Runner, der sie ignoriert, weiter zum richtigen Urteil kommen —
genau wie `since:`. D bricht die Zusage und importiert die Snapshot-Verrottung.

C ist nicht verhandelbar: solange `rstrip("\n")` im Runner steht und „byte-exact" in der README,
widerspricht die Spezifikation ihrem eigenen Referenzwerkzeug. **Das ist ein Fehler im
Spec-Repo, kein Designbedarf** — und er gehört als solcher gemeldet, nicht hier entschieden.
**Bruch:** nein. **Hängt an:** Spec-Prozess (spec-first), Diagnostik-Gebiet, T20.

### T34 — Was schuldet ein Test der Nebenläufigkeit? *(neu nach der Kritik)*

**Heute:** nichts, und es fällt niemandem auf. Gemessen, `v2-r13/`:

```lyr
@Test pub fn aa_leaves_a_task(): void {
    spawn(worker());              // worker() yielded Wait.Sleep(5000)
    assertEq(pending(), 1);
}
@Test pub fn bb_sees_a_clean_scheduler(): void {   // KONTROLLE
    assertEq(pending(), 0);
}
```

```
PASS task_tests.aa_leaves_a_task
PASS task_tests.bb_sees_a_clean_scheduler
2 test(s), all passed          real 0m0,910s
```

Zwei Befunde in einer Messung. **Erstens**: ein Test, der eine Koroutine parkt und nie `run()`
ruft, ist grün und still — der Lauf endet sofort, die Koroutine läuft nie, und nichts sagt es.
**Zweitens** (die Kontrolle): der nächste Test derselben Datei sieht `pending() == 0`, weil der
Scheduler ein Modul-`let` ist (gelesen, `stdlib/std/task.lyr:85–94`) und ein Modul-`let` je
Instanz neu läuft (gemessen, `p04`). **Der Scheduler-Zustand ist also isoliert; was die Koroutine
festhält, ist es nicht** — ein Deskriptor gehört der VM, und die lebt für die ganze Datei. Das ist
dieselbe Klasse wie das gemessene Datei-Handle aus 1.5(c)/T4, aber mit einem geplanten Ablauf
daran.

`STATUS.md:418–430` dokumentiert genau diese Wartearten aus der anderen Richtung (ein `std.process`-
Kind, das auf Pool-Threads streamt, während die VM unter ihm entsorgt wird).

| Option | Vorbild | Preis |
|---|---|---|
| A — nichts | Status quo | ein Test, der einen Task vergisst, prüft nichts und sagt es nicht |
| B — Nachbedingung: `pending() != 0` am Testende ist ein Fehlschlag | Go (`goleak` von Uber, extern), Jest („A worker process has failed to exit gracefully") | der Runner muss `std.task` kennen — eine Bibliothek, die der Runner heute nicht kennt |
| C — `@TeardownAll`/`@Teardown` (T5 B) ist der Ort: der Benutzer schreibt die Nachbedingung | xUnit `Dispose` | jede Datei wiederholt sie; niemand schreibt sie |
| D — der Runner meldet es als **Warnung**, nicht als Fehlschlag | Jest (Default) | eine vierte Ausgangsart, oder eine Zeile neben dem Ergebnis |

**Empfehlung: D, mit C als dem Ort, an dem es zum Fehlschlag gemacht werden kann.** B ist richtig
in der Sache und falsch in der Schichtung: `lyrtest` darf nicht `std.task` kennen — sonst kennt es
morgen `std.io.net` und `std.process` auch, und der Runner wird eine Bibliotheksliste. D geht ohne
das: **der Runner fragt nicht die Bibliothek, sondern die VM**, wie viele Koroutinen beim
Instanzende noch leben — das ist eine Laufzeitfrage, keine Bibliotheksfrage, und die VM kann sie
beantworten.

**Folge für T4 und T14**, die diese Frage mitentscheidet: sobald mehrere VMs nebeneinander laufen
(T14 B), ist „wie viele Koroutinen leben noch" eine Frage je VM, und eine liegengelassene Koroutine
mit einem Deskriptor ist genau die Cross-VM-Isolation, die `STATUS.md:645` als „argued, not tested"
führt.
**Bruch:** nein (D ist eine Warnung). **Hängt an:** T4, T5, T14, VM-Gebiet (kennt die Instanz die
Zahl ihrer lebenden Koroutinen?), Nebenläufigkeits-Gebiet.

---

## 4. Was wir übernehmen sollten

**Aus Go — die Sprache, deren Form Lyric schon gewählt hat:**
- `b.N`-Benchmarks im **selben** Werkzeug wie die Tests (T18)
- `-cover` im selben Werkzeug (T19)
- `t.Cleanup` als `@Teardown` (T5)
- getrennte Exit-Bedeutungen und `-json` (T12, T31)
- ein Default-Gesamttimeout als **Wanduhr**, das alle Stacks druckt (T13)
- `Example` + erwartete Ausgabe als Doc-Test (T17) — **nicht** als Snapshot

**Aus Swift/swift-testing — die Sprache mit derselben `@Test`-Schreibweise:**
- `@Test(arguments:)` als Vorbild für `@Case` (T3)
- Traits als Attributfelder: `.timeLimit`, `.disabled`, `.tags` (T9, T10, T13)
- `@Suite` mit Lebenszyklus als Vorbild für `@Setup`/`@Teardown` (T5)

**Aus xUnit — dem Framework, das die Werkzeugkette selbst benutzt:**
- `Assert.Throws<T>()` **gibt die Exception zurück** — genau die Signatur, die
  `stdlib/std/test.lyr:98` schon aufgeschrieben hat (T2)
- **`[MemberData]`/`[ClassData]`: eine benannte Member-Funktion als Fallquelle** (T3 C) — das
  richtige Vorbild, das die alte Fassung bei pytest suchte und nicht fand
- neue Instanz je Test — hat Lyric bereits, es fehlt nur die Ressourcenhälfte (T4)

**Aus ExUnit — jetzt als das richtige Vorbild erkannt:**
- **`async: true` je Test-Modul**: parallel über Module, sequenziell darin (T14 B) — genau das
  Korn, das dieses Dossier vorschlägt
- Prozess je Test als Isolation — die Form, die 1.5(f) mitlöst (T28)
- Shuffle mit gedrucktem Seed als Default (T14 C) — **das** Vorbild, nicht Rusts instabiles
  `--shuffle`
- `doctest` als Standardwerkzeug, nicht als Plugin (T17)

**Aus Jest:**
- Worker-Prozess je Datei — die Form, die T13, T14 und T28 zusammen löst
- Snapshots, aber mit `--update` als bewusster Handlung, nie als Default (T16)
- „A worker process has failed to exit gracefully" als Warnform für T34

**Aus Haskell:**
- HPC: Coverage **im Standardwerkzeug** (T19) — ein Vorbildfall, den die alte Tabelle verschenkte
- Hedgehog statt QuickCheck: Generatoren als Werte mit integriertem Shrinking, baubar **vor** dem
  4.7-Fundament und ohne `SEM0067`-Problem (T15, T27)

**Aus dem eigenen Baum — die wichtigste Quelle dieses Gebiets:**
- **`lyric check` kompiliert die Testwurzel bereits als eine Einheit mit pfadbasierten
  Modulnamen** (T6, T8, T26) — gemessen 17× schneller (T32)
- **`lyrvm run --grant`** gibt es bereits; `lyrtest` hat es nicht (T23)
- **`LYR-RES0006`** ist der Code für „geladen als X, deklariert Y"; der Einzeldatei-Pfad zieht ihn
  nicht (T26)
- **`@Deprecated { until }`** ist das gebaute Migrationsverfahren (T30)

**Ausdrücklich nicht übernehmen:**
- pytests AST-Rewriting und Fixture-Injektion per Parametername — setzt Dynamik voraus
- Gos `t *testing.T` als Parameter — bricht den `@Test`-Vertrag und macht Tests zu einem Baum
- Rusts `#[cfg(test)]`-Tests im Produktionsfile — die Entscheidung ist getroffen und richtig
- Rusts instabiles `--shuffle` als Vorbild — es ist nightly-only
- Inline-Snapshots, die Quelltext umschreiben — das ist `lyrfix`-Gewalt, und es kollidiert mit T29
- vollständige `.stderr`-Erwartungsdateien in der Konformanzsuite — bricht „ein Fall, eine Datei"

**Die billigsten Posten zuerst** — sprachfrei, nur `src/Lyrtest/Program.cs`, `std/test.lyr` und
`run_conformance.py`:

1. **Panik-Code in der Ergebniszeile** (1.5e) — eine Zeile, und sie ist Voraussetzung für T2 B
2. **`exit` im Testlauf zur Panik umleiten** (T28 B) — schließt den gemessenen Nulllauf
3. **Wanduhr-Timeout für den Lauf** (T13 A) und eine Zeile `timeout=` im Konformanzrunner
4. **`@Test { panics = … }`** (T2 B)
5. **`@Skip` + Laufzeit-`skip`** (T10)
6. **`--json` mit Datei und Zeile je Test** (T12 + T25)
7. **getrennte Exit-Codes** (T31)
8. **`lyrtest --grant`** (T23 B)
9. **`--list`** (T25)

**Was hier nicht mehr steht**, und in der alten Fassung stand: „Warnung bei einer Testdatei ohne
`@Test`". Sie ist **nicht** sprachfrei — der Runner sieht nur `OnFunctions("Test")` und kann
„bewusste Hilfsdatei" nicht von „Testdatei, in der der Import vergessen wurde" unterscheiden.
Genau diese Unterscheidung stellt erst T6 A her. Der Posten hängt an T6.

---

## 5. Konflikte

### 5.1 Konflikte mit Sprachausschlüssen und Rule 2

| Nr. | Konflikt | Mit wem | Lage |
|---|---|---|---|
| K1 | **T1 B** — ein compilergelesenes `assert`, das einen Ausdruck zerlegt | dem Ausschluss „kein allgemeines Makrosystem" | **Kein Bruch**, wenn es **ein** Konstrukt mit **einer** Regel ist, wie `@Deprecated` das eine compilergelesene Attribut ist. Wird es erweiterbar, ist es ein Makrosystem mit anderem Namen — dann ADR. |
| K2 | **T4 B** — Ressourcen je Instanz freigeben | Rule 2 | `STATUS.md:2232–2234` stellt die Frage selbst: ist „diese VM beenden" und „diesen Lauf darin beenden" **ein** Mechanismus oder zwei? T4 C weicht ihr aus — und das ist jetzt begründet statt bloß bequem, weil die Quelle sagt, dass C baubar ist (gelesen). |
| K3 | **T14 B** — paralleler Runner über mehrere VMs | „single-threaded plus Koroutinen" | **Kein Bruch.** Die Regel gilt für die *Sprache*; der Runner ist ein Host, und mehrere VMs auf mehreren Threads sind die Annahme, die `STATUS.md:645` als „argued, not tested" führt. Muss trotzdem ausdrücklich so aufgeschrieben werden, sonst liest es jemand als Threads in Lyric. **T28 C (Kindprozesse) umgeht den Konflikt ganz.** |
| K4 | **T22 A** — `never` als Rückgabetyp | dem Ausschluss „never als allgemeiner Typ" | **Bruch, eng.** Begründung: der Ausschluss richtet sich gegen `never` in Variablen, Feldern und Typargumenten; `panic` trägt die Eigenschaft schon, nur darf keine Bibliotheksfunktion sie aufschreiben. Enge Form: nur Rückgabeposition, nur Wirkung auf die Flussanalyse. |
| K5 | **T15 B**, **T3 C** und **T3 A** — Tests mit Parametern | dem `@Test`-Vertrag „nimmt nichts" und T7 | Eine Property/ein Fall **muss** Parameter nehmen. Dann ist der Vertrag pro Attribut definiert (T7 B), nicht global — das macht T7 B zur **Voraussetzung** für T3 und T15. |
| K6 | **T9 B** — Regex-Filter | der offenen Bibliotheksfrage | `design/stdlib-2.md` schließt Regex aus, `lyric-v5-features.md` nimmt es rein (STATUS §Still open). Der Runner darf diese Frage nicht nebenbei entscheiden — deshalb Empfehlung gegen B. |
| K7 | **T3 A** — dasselbe Attribut mehrfach | `LYR-SEM0068` und dem Formatierer | Gemessen: `SEM0068` verbietet es, und `lyric fmt` faltet Stapel zu Gruppen, in denen die Wiederholung ausdrücklich dasselbe bleibt. A braucht **zwei** Ausnahmen, nicht eine. Siehe T29. |
| K8 | **T17 D** — Doc-Blöcke kompilieren | bestehender Doku | Jeder `///`-Block mit illustrativem Pseudo-Code hört auf zu bauen. Braucht Opt-out-Marker **und** eine 4.x-Warnstufe, sonst ist es ein stiller Massenbruch. |
| K9 | **`assertEq` auf allem Zusammengesetzten** (T1, T16, T15) | der Reihenfolge in PLAN.md | **Jetzt aufgeteilt, weil es drei verschiedene Grenzen sind (gemessen, 1.6):** eigener Struct und `List<T>` brauchen Konformanz-Synthese (4.7 #2) bzw. **zusätzlich bedingte Konformanz** (#3); `int[]` und `?int` brauchen **keine Synthese, sondern eine Lowering-Grenze** (`LYR-IR0001`), die unter §C als „restliche IR0001-Grenzen" läuft. Die Testrunde **nach** dem 4.7-Fundament zu legen ist Voraussetzung — aber #2 allein reicht nicht. |
| K10 | **T27** — `SEM0067` verbietet Attribute auf generischen Deklarationen | T3 und T15 | Die Regel ist richtig („eine Metadatenzeile"), und sie macht jede Tabelle und jede Eigenschaft monomorph. **Kein Bruch, sondern eine Grenze**, die T3 und T15 hinnehmen müssen — oder das Attribut-Gebiet muss T27 C liefern. |
| K11 | **T28 B** — `exit` bedeutet im Testlauf etwas anderes | Rule 2 | Ein Native, das je nach Wirt zwei Dinge tut, sieht nach zwei Mechanismen aus. **Kein Bruch**, wenn es als das aufgeschrieben wird, was es ist: `lyrtest` ist ein **Host**, und ein Host darf ein Native ersetzen — dieselbe Eigenschaft, die T24 beschreibt. Muss in der Spec stehen, nicht im Runner-Kommentar. |

### 5.2 Konflikte mit dem ausgelieferten Werkzeug-Ist-Stand

Die alte Fassung führte hier keinen einzigen Eintrag, obwohl der größte Konflikt dort liegt. Alle
vier sind gemessen.

| Nr. | Konflikt | Lage |
|---|---|---|
| W1 | **`lyric check` und `lyric test` widersprechen sich über die Modulwurzel** | `check` kompiliert die Testwurzel als eine Einheit, `test` Datei für Datei. Ein Import zwischen Testdateien ist unter `check` grün und unter `test` rot (gemessen, 1.2b). **Das ist heute ausgeliefert**, und der Quelltext von `CheckProject` nennt genau diesen Fall als das, was nicht passieren soll (gelesen, `src/Lyrc/Program.cs:337–339`). T6 A ist die Auflösung. |
| W2 | **…und über den Modulnamen** | `alpha.shared_tests` unter `check`, `shared_tests` unter `test` (gemessen, 1.2c). T8 A ist die Auflösung, und sie ist damit kein minor-Bruch gegenüber einem konsistenten Stand, sondern eine Reparatur. |
| W3 | **…und über den geschriebenen `module`-Header** | `check` lässt ihn gelten, `test` verwirft ihn still; der Code `LYR-RES0006` existiert und wird vom Einzeldatei-Pfad nicht gezogen (gemessen, 1.2d und `v2-r06b`). T26 A ist die Auflösung. |
| W4 | **Der Runner-Prozess kann von Testcode beendet werden** | `std.os.exit(0)` in einem Test → Exit 0, leere Ausgabe, kein Test gelaufen (gemessen mit Kontrolle, 1.5f). Das kollidiert mit **jeder** Zusage, die T12, T31 oder T21 über Exit-Codes und Vollständigkeit machen wollen: solange Testcode den Exit-Code setzen darf, ist keine davon haltbar. T28 B ist die Vorbedingung für T31. |

---

## Nach der Kritik geändert

**Falsche Behauptungen korrigiert (alle nachgemessen):**

- „`tests/`, nur `lyrtest` kompiliert sie" (§2-Tabelle, §1.1, Zitat aus `Program.cs:11–14`) war
  **falsch**. `lyrc check` kompiliert die Testwurzel ebenfalls — gemessen (`v2-r01`), gelesen
  (`src/Lyrc/Program.cs:300–351`, `docs/guide/20-testing.md:76–78`, `lyrc --help`). Neuer §1.2
  führt die Divergenz als größten Einzelbefund des Gebiets, mit drei Messungen und zwei Kontrollen.
- T6 („ein Testfile kann kein anderes importieren") und §1.4(c) galten nur für `lyrtest`. Gemessen
  (`v2-r02`): `check` → „3 modules ok", `lyrtest` → `LYR-RES0003`. T6 ist umgeschrieben: A ist
  keine Neuerung mit Preis, sondern die Beseitigung einer ausgelieferten Divergenz.
- T8 („Bruch: minor (A)") ist umgedreht: Option A ist in `lyrc check` **bereits implementiert**
  (gemessen `v2-r03`, gelesen `ScriptSource.ModuleNameUnder`). Empfehlung von „A, wenn das
  Modul-Gebiet ohnehin dahin geht, sonst B" auf **A** geändert.
- T13 („C, mit dem Budget, das die VM schon hat") war begrifflich falsch: `ExecutionBudget` ist
  **gezählt, nicht getaktet** (gelesen, `ExecutionBudget.cs:13–18`) und stoppt ein blockierendes
  Native nicht. Dazu: ein erschöpftes Budget kommt als `ScriptBudgetException : ScriptPanicException`
  (gelesen, `ScriptException.cs:24–25,63–66`) und landet im Zweig, der den Code wegwirft.
  Empfehlung neu: **Wanduhr (A), dazu T28 D prüfen; C nur als Zusatz mit eigener Ausgangsart.**
- T4 („die eine Messung, die diese Frage entscheidet, kostet eine Stunde") ist durch **Lesen**
  beantwortet: `LoadedProgram.Load` baut alles aus dem unveränderten `BytecodeModule` neu,
  `BytecodeModule` ist `init`-only, `ScriptModule` hat keine VM-Rückreferenz — und
  `vm.Instantiate` läuft heute schon je Test, also kostet C nur die `LangVm`-Konstruktion.
  Restrisiko eng benannt.
- „170 Falldateien, 159 laufen (STATUS.md:47)" war doppelt falsch. Gemessen: **178 Dateien,
  177/177 bestanden, 1 übersprungen**. Die 159 stammen aus `STATUS.md:66` und beschreiben einen
  früheren Stand im 4.6-Zyklus.
- **Alle STATUS.md-Zeilenangaben** sind neu nachgeschlagen (die alten waren um ~19 Zeilen
  verschoben, nach PR #172): `:437`, `:462`, `:645`, `:650`/`:690`, `:946`, `:2226–2235`.
- T12 („`lyrc --json` … PLAN.md §D nennt sie unvollständig") war **veraltet**: `PLAN.md:177` trägt
  ✅ (PR #171). Die Empfehlung wird dadurch stärker, die Begründung ist korrigiert.
- §1.5 „Assertion-Fehlschlag und echter Bug sind ununterscheidbar" war **zu stark**. Gemessen
  (`v2-r05`): die oberste Backtrace-Zeile unterscheidet sie (`std.test.assertEq<int>` gegen
  `apple_tests.real_bug`) — nur nicht maschinell, nicht in der Ergebniszeile, und nicht für `fail`
  und `assertTrue`. Die tragfähige Aussage (fehlender Diagnosecode) steht mit Kontrolle.
- §1.5 `assertEq` war **unterschätzt**. Neu gemessen (`v2-r07`) mit Kontrolle: `int[]` und `?int` →
  `LYR-IR0001` **in `stdlib/std/test.lyr:29`**, `List<int>` → 2× `SEM0028`, Skalare und Strings →
  `PASS`. Neuer §1.6; K9 ist in drei getrennte Grenzen aufgeteilt (Synthese ≠ bedingte Konformanz ≠
  Lowering-Grenze).
- T7 stand auf einer **falschen Voraussetzung**: der Runner matcht auf den unqualifizierten Namen
  `"Test"` (gelesen, `Program.cs:168`; gemessen, `v2-r09`: ein selbst deklariertes `Test`-Struct
  wird ausgeführt). Neuer §1.4; die Empfehlung B hat jetzt ihre zweite Hälfte.

**Was stehen bleibt, und warum:**

- Die Aussage, `assertEq` scheitere auf einem eigenen Struct mit zweimal `LYR-SEM0028` (`p10`),
  hält — sie ist jetzt nur nicht mehr der ganze Befund, sondern der mildeste Fall von vieren.
- T2, T5, T9, T10, T11, T16, T17, T18, T19, T21, T22 sind in Empfehlung und Bruchgrad unverändert;
  ergänzt wurden nur Querverweise auf die neuen Fragen und korrigierte Belege.
- Die Kritik nannte den Capability-Fehlercode `LYR-VM0001`; gemessen ist er **`LYR-CAP0001`**
  (`v2-r11`). T23 benutzt den gemessenen Code.

**Zwölf fehlende Designfragen eingearbeitet**, alle in derselben Form (Ist-Stand belegt, Optionen
mit Vorbild und Preis, Empfehlung, Bruchgrad, Abhängigkeiten):

T23 Capabilities im Test · T24 Test-Doubles für Host-Natives · T25 Wie ein Editor einen Test findet
· T26 Der geschriebene `module`-Header · T27 Darf ein Test generisch sein (`SEM0067`) · T28 Wer
schützt den Runner-Prozess (`std.os.exit`) · T29 `lyric fmt` und die `@Case`-Zeilen · T30 Migration
einer bestehenden Suite · T31 Welche Exit-Codes frei sind · T32 Wie die Testwurzel skaliert · T33
Was ein Konformanzfall über die Qualität einer Diagnose sagen darf · T34 Offene Koroutinen am
Testende.

**Vergleichssprachen korrigiert:**

- **ExUnit**: `async: true` steht auf dem Test-**Modul** (Module nebenläufig, Tests darin
  sequenziell); der Prozess je Test ist Isolation, nicht Parallelität. Folge: ExUnit ist jetzt das
  **Vorbild** für T14 B statt dessen Gegenpol.
- **Rust `--shuffle`**: nightly-only (`-Z unstable-options`), als gleichrangiges Vorbild gestrichen;
  T14 C nennt ExUnit.
- **Haskell, Coverage**: „—" war falsch — HPC (`-fhpc`, `cabal test --enable-coverage`,
  `stack test --coverage`) ist Standardwerkzeug. T19 hat den Vorbildfall zurück.
- **Go, Snapshot**: `Example` + `// Output:` ist kein Snapshot (keine erzeugte Erwartung, kein
  `-u`). Zelle jetzt „—"; T16 C entsprechend umformuliert.
- **Bench/Property/Snapshot durchgehend gleich bepreist**: externe, aber etablierte Gegenstücke
  sind jetzt für alle Sprachen genannt (`pytest-benchmark`, `swift-snapshot-testing`, `FsCheck`,
  `criterion`, `rstest`), nicht nur für Rust.
- **T3 C**: „pytest `argvalues`" gestrichen (das ist der zweite Positionsparameter von
  `parametrize`, kein benannter Lieferant); richtiges Vorbild ist **xUnit `[MemberData]`/
  `[ClassData]`**.
- **T6 A**: „pytest `conftest.py`" gestrichen (das liefert Fixtures und Hooks, nicht den Import
  zwischen Testdateien); Vorbild ist jetzt **`lyric check` selbst** und Go.

**Schwache Empfehlungen geschärft:**

- T1: die Begründung „der einzige Weg" ist gestrichen (C leistet dasselbe); stattdessen steht die
  Reihenfolgebedingung — erst `==`/`show` auf `int[]`, `?int`, `List<T>` beantworten.
- T3: die unbelegte „80 %"-Zahl ist gestrichen und durch **drei gemessene Hürden** ersetzt
  (`SEM0068`, `SEM0067`, `lyric fmt`).
- §4 „billigste Posten": „Warnung bei einer Testdatei ohne `@Test`" ist aus der Liste der
  sprachfreien Sofortmaßnahmen entfernt — sie hängt an T6 A. Neu in der Liste: `exit`-Umleitung,
  `--grant`, `--list`.
- §5: neuer Abschnitt **5.2 mit vier Konflikten gegen den ausgelieferten Werkzeug-Ist-Stand**
  (W1–W4), die die alte Fassung gar nicht führte. Dazu K10 (`SEM0067`) und K11 (`exit` je Wirt).

**Neue eigene Messungen, die die Kritik nicht hatte:**

- **Skalierung** (§1.8/T32): 500 Testdateien → `lyrtest` **18,4 s** gegen `lyrc check` **1,06 s**,
  mit Kontrolle bei 100 Dateien (4,9 s / 0,68 s). Das ist ein Faktor **17** und macht T6 A vom
  Kostenposten zum Beschleuniger.
- **`LYR-RES0006` existiert** (`v2-r06b`, mit zwei Kontrollen): der Header-Konflikt ist im
  Quellbaum ein benannter Fehler und im Einzeldatei-Pfad still. T26 braucht damit keine neue Regel.
- **`lyrvm run --grant` gegen `lyrtest --grant`** (`v2-r11`): der Capability-Mechanismus existiert
  im Schwesterwerkzeug, `lyric.json` hat kein Feld dafür (gelesen, `ProjectFile.cs:40–78`).
- **`lyric fmt` faltet gemessen** (`v2-r15`), und der Duplikatcode ist `LYR-SEM0068`, nicht nur
  eine Guide-Regel.
- **Offene Koroutine** (`v2-r13`, mit Kontrolle): grün und still, Scheduler-Zustand je Instanz
  isoliert, Ressourcen nicht.
- **Migrationsfläche gezählt**: 22 Dateien, 566 `assertEq`, 321 `assertTrue` in `stdlib-tests/`.
- **`--filter` greift erst nach der Kompilierung** (`v2-r07`): ein Compilerfehler in einer
  ausgefilterten Datei färbt den Lauf trotzdem rot.
