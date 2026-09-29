# Lyric 5 — Gebiet: Module, Sichtbarkeit, Namensräume

Dritte Fassung (nach der zweiten adversarischen Kritik), Stand der Messungen: 2026-09-27, gegen
den gebauten Debug-Stand (`src/Lyrc/bin/Debug/net10.0/lyrc.dll`, `src/Lyrvm/…/lyrvm.dll`,
`src/Lyric.Cli/…/lyric.dll`; Format 4.0).

Probenlage:
- `probes/module/` (`pNN`/`kNN`) — erste Runde;
- `probes/module-rev/` (`rNN`) — zweite Runde;
- `probes/module-review/` (`rrNN`) — die Proben des Kritikers, hier nicht als Beleg benutzt;
- **`probes/module-rev2/` (`qNN`)** — diese Fassung. Erwartungen vorab in
  `probes/module-rev2/ERWARTUNGEN.txt`, Lauf in `run.log`. **Jeden Kritikpunkt, der eine Messung
  betrifft, habe ich selbst nachgemessen**; wo unten `qNN` steht, ist das mein Lauf. Alle
  Erwartungen aus `ERWARTUNGEN.txt` sind eingetreten, bis auf eine, die *schlimmer* war
  (`q13b`, siehe MOD-44).

Belegstufen: **gemessen** (Probe kompiliert und gelaufen), **gelesen** (Pfad:Zeile), **behauptet**
(so gekennzeichnet).

---

## 1. Ist-Stand

### 1.1 Was die Regel sein soll

| Regel | Quelle |
|---|---|
| Eine Datei ist ein Modul; der Header ist optional | `docs/guide/12-modules.md:3`, `lyric-spec/spec/04-modules.md:5` |
| „Only `pub` declarations cross a module boundary." | `lyric-spec/spec/04-modules.md:21` |
| „`pub` exports. Everything else is visible only inside the module." | `docs/guide/12-modules.md:15` |
| Drei Importformen: selektiv, qualifiziert (letztes Segment), `as` | `lyric-spec/spec/04-modules.md:22-27`, `docs/Grammar.md:160-162` |
| Eine Extension kommt **mit dem Modul, nicht mit einem Namen** | `lyric-spec/spec/04-modules.md:29` |
| Ein Import zählt als benutzt, sonst warnt er (`LYR-SEM0072`) | `lyric-spec/spec/04-modules.md:29-33` |
| Ein Segment gehört genau einem Root (Projekt, std, native, Dependency) | `lyric-spec/spec/04-modules.md:10-17`, `docs/guide/12-modules.md:138` |
| Import-Zyklus ist ein Fehler (`LYR-RES0005`) | `lyric-spec/spec/04-modules.md:17` |
| Ein Zyklus hat **keine** Initialisierungsreihenfolge und wird darum abgelehnt | `lyric-spec/spec/04-modules.md:59` |
| Eine Bibliothek wurzelt in ihren `pub` **Funktionen** | `lyric-spec/spec/04-modules.md:180-190` (§4.6) |
| `::` steht nie in einem Modulpfad, `.` trennt Segmente | `docs/Grammar.md:139` |
| Sichtbarkeit kennt genau zwei Stufen | `src/Lyric.Frontend/Resolver/Symbol.cs:9-13` (`Visibility { Module, Public }`) |
| **Eine kleine Menge Namen ist ohne Import gebunden**: `panic` aus `std.core`, die f-String-/Operatorhelfer von `std.string`, `rawToChars` | `lyric-spec/spec/04-modules.md:135-142` (§4.4, „bound by the compiler without an import") |
| Ein Feld trägt syntaktisch **kein** `pub`; eine Methode und ein `static let` dürfen es | `docs/Grammar.md:237` (`Field = IDENTIFIER ':' TypeExpr [ '=' Expr ]`), `:213`, `:236` |
| Der Funktionsname im Bytecode ist `<module>.<fn>`, eine Methode `<module>.<type>.<method>` — **derselbe Punkt** | `src/Lyric.Frontend/Ir/Lowering/NameMangling.cs:21-37` |

### 1.2 Was der Compiler tut — gemessen

**Der zentrale Befund: `pub` wird auf dem qualifizierten Weg überhaupt nicht geprüft.**

| Probe | Programm | Ergebnis |
|---|---|---|
| `p03` | `fn hidden()` in `lib`, von außen `lib.hidden()` | **kompiliert, läuft, gibt 7** |
| `k03` | nicht-`pub` `let secretGlobal = 11;`, von außen `lib.secretGlobal` | **kompiliert, gibt 11** |
| `k02` | nicht-`pub` `struct Secret`, von außen `let s: lib.Secret = …;` **und** `lib.Secret { v = 3 }` | **kompiliert, gibt `5 3`** |
| `r05` | nicht-`pub` `static let HIDDEN` in einer `pub class`, von außen `lib.Counter.HIDDEN` | **kompiliert, gibt 5** |
| `r06` | nicht-`pub` `enum Mode`, von außen `lib.Mode.Fast` **und** `match (m) { lib.Mode.Fast => … }` | **kompiliert, gibt `fast`** |
| `r06` | fremdes Modul schreibt `extend lib.Secret { fn grafted() }` auf einen modulprivaten Typ | **kompiliert, gibt 70** |
| `r06` | nicht-`pub` `interface Hidden` als Konformanz **und** als Generics-Schranke `<T :: [lib.Hidden]>` | **kompiliert, gibt 4** |
| **`q06`** | nicht-`pub` **`extern "dotnet" fn cbrt`** in `lib`, von außen `lib.cbrt(8.0)` | **kompiliert, druckt 2** |
| `q06k` | Kontrolle: `import lib { cbrt };` | `LYR-RES0004: 'cbrt' is not public in 'lib'` |
| `k01`/`r01k` | dieselbe Funktion selektiv: `import lib { hidden };` | `LYR-RES0004` |

Also: **die selektive Form setzt die Regel durch, die qualifizierte nicht.** `pub` ist heute
keine Zugriffskontrolle, sondern eine Kontrolle darüber, welchen *Weg* man nehmen muss.

Die Betroffenenliste, vollständig nach dieser Runde: **Funktionen, Globals, Typen, Interfaces,
Überladungsmengen, `static let`, Enum-Varianten (Wert und Pattern), `::`-Konformanzlisten,
Generics-Schranken, `extend`-Ziele — und `extern`-Deklarationen** (`q06`; `docs/Grammar.md:170`
erlaubt `[pub] ExternDecl`). Die stdlib deklariert kein einziges `extern` (gemessen:
`grep -rn 'extern "' stdlib/std/*.lyr` = 0 Treffer — ihre Natives kommen über den nativen Root),
ein Benutzerprojekt mit eigenen Externs ist aber genau der Embedding-Fall.
**Ich halte die Liste nach wie vor nicht für abgeschlossen** — jede Runde hat eine Position
gefunden; MOD-25 (eine Implementierung) muss sie aus dem Symbolmodell ableiten, nicht aus
Proben.

Ursache, gelesen: `TypeChecker.MemberOfModule`
(`src/Lyric.Frontend/Sema/TypeChecker.cs:4044-4056`, Aufrufer `:3350`) schlägt mit
`mod.Members.LookupLocal(member)` nach und filtert nichts.

**Es gibt drei Implementierungen einer Regel, und nur eine ist die richtige Stelle.** Gelesen:

| Ort | Filtert? | Wer ruft |
|---|---|---|
| `TypeChecker.MemberOfModule` (`TypeChecker.cs:4044-4056`) | **nein** | der Compiler, bei jedem qualifizierten Zugriff |
| Überladungskandidaten (`TypeChecker.cs:1981-1986`) | **ja** (`f.Visibility == Public \|\| mod == _currentModule`) | der Compiler, bei einem qualifizierten Aufruf mit mehreren Kandidaten |
| `MemberFacts.OfModule` (`src/Lyric.Frontend/Sema/MemberFacts.cs:111-134`) | **ja** | **nur** `src/Lyric.Lsp/Analysis/CompletionProvider.cs:73` |

**Die halbe Prüfung ist schlimmer als keine.** Gemessen an zwei nicht-`pub` Überladungen
`h(int)`/`h(string)` (`r16`, bestätigt `p34`/`k09`/`k10`): `lib.h(1)` gibt 1, `lib.h("a")` ist
`LYR-SEM0001: cannot assign 'string' to 'int'`, mit `pub` auf beiden gibt es 2. Die
Kandidatenmenge ist leer, die Auflösung fällt auf „was `LookupLocal` zuerst liefert" zurück.

**Der Bruch reicht über Projektgrenzen** (`p37`): `geometry.secret()` einer Dependency gibt 99.

**Und er ist heute die einzige Art, Weißbox zu testen** (`q14`, bestätigt `r01`): Projekt mit
`{ "sourceRoot": "src", "testRoot": "tests" }`, `src/calc.lyr` mit nicht-`pub`
`fn internalDouble`, `tests/calc_test.lyr` mit `import calc; assertEq(calc.internalDouble(3), 6);`
→ `lyric test` meldet `PASS calc_test.doubles`, `lyric check .` meldet `2 modules ok`. Kontrolle
`q14k`: selektiv ist es `LYR-RES0004` und `0 test(s), 1 FAILED`. Gelesen, wie die
Testkompilation gebaut ist: `src/Lyrtest/Program.cs:100-108` sammelt die Dateien unter
`testRoot`, `:150` kompiliert **jede Datei einzeln** per `vm.CompileFile(file)` mit
`SourceRoot = project?.SourceRoot` — der Test ist eine eigene Kompilation mit dem Test als Entry
und `src/` als Root, also ein Fremder, der den sourceRoot importiert (siehe MOD-41).

**Die stdlib nutzt das Loch nicht** (gelesen): `grep -rh "^import std\.[a-z.]*;" stdlib/` findet
null bare Importe, es gibt genau einen Alias (`import std.string as strings;`), und die zwei über
ihn erreichten Namen sind `pub` (`stdlib/std/string.lyr:22`, `:639`).

#### Weitere gemessene Eigenschaften

| Nr. | Frage | Gemessen |
|---|---|---|
| `r07` | Import-Zyklus a→b→c→a | **genau ein** `LYR-RES0005: import cycle involving module 'a'` an `c.lyr` — ohne Pfad |
| **`q08`** | Entry **`app.lyr`** (kein Header) importiert `b`, `b` importiert `app` | `LYR-RES0005 … involving module 'b'` **plus `LYR-SEM0021: duplicate 'main' function`** an `app.lyr:3:1` — **ohne Note auf ein zweites Vorkommen.** Die Datei ist zweimal geladen: als `main` (Entry) und als `app` (Import). |
| **`q08k`** | Kontrolle: Entry heißt **`main.lyr`** | nur `LYR-RES0005 … 'main'` — `FindModule(["main"])` findet den Entry, es gibt keine Doppelladung |
| **`q08c`** | `app.lyr` mit `import app;` (Selbstimport über den Dateinamen) | `RES0005 … 'app'` **plus** `SEM0021` an derselben Zeile |
| **`q09`** | importiertes `lib.lyr` hat eine eigene `fn main` | `LYR-SEM0021: duplicate 'main' function` an `lib.lyr:1:1`, ohne Note; ein Modul kann nicht zugleich Skript und Bibliothek sein |
| **`q07`** | Modul `a` mit `pub struct b { pub fn c() }` **neben** Modul `a.b` mit `pub fn c()` — ein legales Programm | `error[LYR-CLI0020]: ir-verifier … a.b.c: duplicate function name — this is a defect in the compiler` |
| **`q07k`** | Kontrolle: der Struct heißt `bb` | kompiliert, druckt `1 2` |
| **`q13`** | `pub fn ident<T>` in `lib`, Entry instanziiert mit **modulprivatem** `struct Secret` | kompiliert, druckt 3; `lyrvm info` zeigt die Instanz als **`lib.ident<Secret>`** — Typname ohne Modul |
| **`q13b`** | zwei Module mit je **eigenem** privatem `Secret`, beide instanziieren `lib.ident` | **`LYR-CLI0020 … call to lib.ident<Secret>: arg 0 is val ty22, expected val ty0`** — die zweite Instanz wurde mit der ersten verwechselt; „a defect in the compiler" für ein legales Programm |
| `q13c` | Kontrolle: der zweite Typ heißt `Other` | kompiliert, druckt `3 7` |
| **`q04`** | Bibliothek `api.lyr` **ohne Header**, `lyrc build api.lyr` | `lyrvm info`: `main.helper`, `main.newBox` — **eine Bibliothek ohne Header heißt `main`** |
| `q04k` | dieselbe Datei mit `module api;` | `api.helper`, `api.newBox` |
| `r14`/`q04k` | Bibliothek mit `pub class Box { pub fn get() }`, `fn helper`, `pub fn newBox` | **zwei** Funktionen: `newBox` und **`helper`** (nicht `pub`); `Box.get` ist weg |
| **`q03`** | `panic("x")` ohne jeden Import | kompiliert, Exit 3 — **es gibt implizit gebundene Namen** |
| **`q03b`** | Benutzer-`fn panic(msg: string): int { return 9; }` im Entry, Aufruf `panic("x")` | kompiliert **wortlos**, druckt 9 — die Benutzerdeklaration verdeckt den Builtin still |
| **`q03c`** | `import mylib { panic };` mit `pub fn panic` in `mylib` | kompiliert **wortlos**, druckt 9 |
| **`q03d`** | Benutzer-`fn concat(a, b): string { return "X"; }`, dann `"a" + "b"` und `f"{1}-{2}"` | `ab`, `X`, `1-2` — der Operator bindet `std.string.concat` **per festem Namen** (`FunctionLowerer.cs:1871`, `:4805-4809`), ein Benutzer-`concat` erreicht ihn nicht |
| `r10/c` | `println("x")` ohne Import | `LYR-SEM0002: unknown identifier 'println'` — `println` gehört **nicht** zur impliziten Menge |
| **`q10`** | `import std.io.consol { println };` (Tippfehler im Segment) | `LYR-RES0003: cannot find module 'std.io.consol'` — **ohne Vorschlag** |
| `q10k` | Kontrolle: `g()` neben `fn f()` | `LYR-SEM0002 … note: did you mean 'f'?` — der Mechanismus existiert (`src/Lyric.Frontend/Sema/NameSuggestion.cs:6-16`), nur nicht für Modulpfade |
| **`q11`** | Hello World, `lyrc build --verbose` | `load std.string, std.core, std.iter, … 92.5 ms`, `resolve 8 modules 26.7 ms`, `check 8 modules 217.4 ms`, total 959.6 ms (Debug-Build). **Fünf stdlib-Module werden bei jedem Bau geparst und geprüft**; keine vorkompilierte Schnittstelle, kein Cache |
| **`q12`** | `import lib;` **und** `import lib { f };` im selben File | kompiliert, `f() + lib.g()` = 13 — `p15` (`RES0001`) gilt nur für die **identische** Form |
| `q12b` | dieselben zwei Importe, nur `f` benutzt | keine Warnung (der bare Import warnt ohnehin nie, `p13`) |
| `q12c` | `import lib as L;` + `import lib { f };`, nur `f` benutzt | `LYR-SEM0072: import 'L' is never used` |
| `q16` | `import lib { f }; import lib { g };` | kompiliert, 13 |
| `q16b` | bare + `as` + selektiv für dasselbe Modul, alle benutzt | kompiliert, 20 — **alle drei Formen koexistieren** |
| **`q05a`** | `let lib = 5;` **lokal** in `main` neben `import lib;` | kompiliert **wortlos**, Exit 5 |
| **`q05b`** | `let lib = 5;` **auf Top-Level** neben `import lib;` | `LYR-RES0001: 'lib' is already declared in this module` mit Note |
| `r11k` | `import string;` (verdeckt einen Builtin-Typnamen) | `LYR-SEM0077` **warnt** |
| **`q01`** | `struct S { pub x: int, }` | `LYR-PAR0011` + `PAR0026` („'pub' is a keyword and cannot be used as a name") + `PAR0031` — **ein Feld nimmt kein `pub`** |
| `q01k` | `struct S { x: int, pub fn f() }` | kompiliert, 4 — an einer **Methode** parst `pub` |
| **`q02`** | `interface I { pub fn f(): int; }` | kompiliert, 4 — `pub` am Interface-Member wird geschluckt |
| `p02` | Member eines `pub`-Typs von außen | **alle öffentlich** — Feld lesen *und schreiben*, Methode ohne `pub` aufrufbar |
| `p04` | `util.lyr` mit `module notutil;` | `LYR-RES0006`, gute Meldung |
| `p05` | `pub fn make(): Secret` mit modulprivatem `Secret` | kompiliert schweigend |
| `p06` | Re-Export (`mid` importiert `leaf { f }`, außen `mid.f()`) | `LYR-SEM0012` — kein Re-Export |
| `r04`/`r04b`/`r04k` | `pub type Id = leaf.Inner;` als Annotation / als Konstruktor / in einer Datei | läuft / `SEM0001`+`SEM0015` / **dieselben zwei Fehler** — Alias-Befund, kein Modulbefund |
| `r20/a` | `import lib { f as g };` | `LYR-PAR0016`/`0018` — die Form existiert nicht |
| `r10/a` | `std.io.console.println(…)` ohne Bindung | `LYR-SEM0002: unknown identifier 'std'` — ohne Hinweis |
| `r10/b` | `import std;` / `import std.io;` | `LYR-RES0003` — ohne Hinweis, obwohl `std` ein Root ist |
| `r13` | `extend int { fn thrice() }` in `lib` **ohne** `pub`, von außen genutzt | funktioniert, gibt 12 |
| `r03`/`r03b`/`r03c` | zwei Module erweitern `int` mit `twice`; selektiver Import ohne die Extension; nur eines | `LYR-SEM0044` / **derselbe** `SEM0044` / gibt 6 |
| `r02` | `import shapes.circle;` **und** `… as C;` | beide gebunden, 12/12 |
| `r09`/`r09b` | `import Lib;` auf Windows, Datei `lib.lyr`; mit Header | kompiliert (42) / `LYR-RES0006` mit dem Pfad `…\Lib.lyr`, **den es nicht gibt** |
| `r18/a,b,c` | `pub import`; `import` im Rumpf; `import` am Dateiende | `PAR0025` / `PAR0002` / funktioniert |
| `r17` | `pub(package)`, `package`, `internal` | dreimal `LYR-PAR0025` |
| `r12`/`r12k` | Capability-Bit über eine Zwischenbibliothek | `0x…01` / ohne sie `0x…00` |
| `r19` | `app.lyr` ohne Header; `hdr.lyr` mit `module totally.different.name;` | `entry main.main` / `entry totally.different.name.main` |
| `p13`/`p22` | unbenutzter Import | selektiv und `as` warnen, **bare nicht** |
| `p14` | `import std.string;` und dann `string` als Typ | `LYR-SEM0011` mit Hinweis auf `as` |
| `p15`/`p16` | identischer Doppelimport, Selbstimport | `LYR-RES0001` (+ `RES0005`) |
| `p17` | `shapes/circle.lyr` ohne `shapes.lyr` | funktioniert |
| `p23` | `import a.b;` + `import b;` | `LYR-RES0001` |
| `p26` | Extension ohne Import des deklarierenden Moduls | `LYR-SEM0012` |
| `p32` | `module type.match;` | `LYR-PAR0026` |
| `k07` | `@Deprecated` auf dem Modul-Header | `LYR-SEM0076` an jedem Import |
| — | `import con;` / `import nul;` (Windows-Gerätenamen) | **bewusst nicht gemessen** (Hängegefahr beim Öffnen von `con.lyr`) — siehe MOD-42 |

### 1.3 Die Stellen, an denen Lyric 4 still falsch oder inkonsistent ist

**(A) `pub` gilt nicht über den qualifizierten Weg.** Siehe 1.2, vollständige Liste inklusive
`extern`. Nur der selektive Import prüft (`Resolver.cs:234-238`).

**(B) Der Header der Entry-Datei ist ungeprüft, der Dateiname wird nie gelesen — und dieselbe
Datei kann zweimal Modul sein.** `lyric-spec/spec/04-modules.md:6` sagt, in einer Entry-Datei
komme der Name vom Dateinamen. Gemessen (`r19`, `q04`) stimmt das in keiner Richtung: ohne Header
heißt das Modul `main` — **auch eine Bibliothek** (`q04`: `main.newBox`) —, mit Header gewinnt
der Header. Ursache, gelesen: `Compilation.AddModule`
(`src/Lyric.Frontend/Resolver/Compilation.cs:105-108`) nimmt `name`, sonst den Header, sonst
wörtlich `["main"]`; der CLI-Pfad übergibt für die Entry-Datei `source.ModuleName`
(`SourceCompiler.cs:103`), und der ist für `lyrc build datei.lyr` null. Nur der Workspace-Pfad
(`lyric check`) leitet den Namen aus dem Pfad ab (`SourceCompiler.cs:360`,
`root.ModuleName`, mit dem Kommentar „The header wins over the caller's derivation") und das
Embedding aus dem Dateinamen (`src/Lyric.Embedding/LangVm.cs:252-254`). **Drei Aufrufer, drei
Ableitungen.**

Die Folge, die die zweite Fassung nicht gesehen hat (`q08`/`q08c`/`q08k`): ein Entry `app.lyr`
ist als `main` registriert; ein `import app;` irgendwo im Graphen findet unter `app` nichts
(`FindModule`, `Compilation.cs:167`), lädt **dieselbe Datei ein zweites Mal** als Modul `app`,
und der Benutzer bekommt `duplicate 'main' function` an einer Zeile ohne zweites Vorkommen —
plus eine Zyklusmeldung, die auf das falsche Modul zeigt. Mit `main.lyr` (`q08k`) passiert
nichts davon. Der Loader dedupliziert nach **Name**, nicht nach **Datei**.

Dazu der bekannte Hijack (`r08`): `module std.core;` in der Entry-Datei verdrängt das
Standardbibliotheksmodul, mit einem Fehlerregen in `…\stdlib\std\*.lyr` und keiner Meldung an der
verursachenden Zeile; die Entry-Datei ist registriert, **bevor** `LoadImportedModules()` die
wohlbekannten Module vorlädt (`Compilation.cs:242-245` vor `:153-158`).

**(C) `pub` steht an DREI Stellen in der Grammatik, wo es nichts bedeutet — und an einer VIERTEN
steht es nicht, wo es hingehört.** *Korrektur gegenüber der zweiten Fassung*, die „vier Stellen
mit `pub` ohne Wirkung" zählte und dazu „vor jedem Member" schrieb:
- **Methoden** (`docs/Grammar.md:213`, `FunctionDecl = [ 'pub' ] …` — gilt auch als
  `StructMember`/`ClassMember`): parst (`q01k`), wirkungslos (`p02`).
- **`static let`** (`Grammar.md:236`): parst, wirkungslos (`r05`), obwohl `Resolver.cs:108` die
  Sichtbarkeit **speichert** (`Vis(sb.IsPublic)`).
- **`extend`-Block** (`Grammar.md:169-170`): parst; `ExtendDecl.IsPublic` hat außer Dumper und
  Formatter keinen Leser (gelesen, Grep über `src/`).
- **Interface-Member** (`q02`, `p19`): parst, wirkungslos.
- **Felder: `pub` parst NICHT** (`q01`: drei Parse-Fehler; `Grammar.md:237`;
  `docs/Befunde_und_Verbesserungen/prototypes/17-member-visibility/README.md` sagt es
  ausdrücklich: „ein Feld kann syntaktisch kein `pub` tragen").

Für MOD-05 heißt das: der Migrationslauf ist **zweigeteilt** — Methoden und `static let`
bekommen einen *stillen Bedeutungswechsel* (Bestandscode trägt das Wort schon), Felder bekommen
*neue Grammatik* ohne Bestandscode. Das ist eine bessere Lage als in der zweiten Fassung
beschrieben: der gefährlichere Fall (Bedeutungswechsel) betrifft nur die Hälfte.

**(D) Die `pub`-Wurzelregel für Bibliotheken erfasst keine Member — und lässt Privates drin.**
`lyric-spec/spec/04-modules.md:186-188` sagt „the `pub` **functions**", der Code meint genau das
(`src/Lyric.Frontend/Ir/Lowering/ModuleLowerer.cs:206`). Gemessen (`q04k`, `r14`): nach dem Bau
stehen `api.newBox` **und `api.helper`** in der Funktionstabelle, `Box.get` ist weg. Der Host
ruft nach Namen (`docs/guide/14-embedding.md:29-34`) — er erreicht `helper` und nicht `Box.get`.

**(E) Es gibt kein Werkzeug, das die öffentliche Fläche zeigt.** Gelesen: `docs/Bytecode.md` und
`lyric-spec/spec/13-bytecode.md` kennen keinen Export-Begriff (`grep -ci export` = 0). Gemessen:
`lyrvm info` zeigt die Funktionstabelle, `lyric --help` kennt weder `doc` noch `api`, ein `lyrfix`
existiert im Baum nicht (`ls src/`). **Aber:** die Design-Runde Bibliotheken hat für Stufe B
„kompilierte Lyric-Bibliotheken mit Header in `api/`, Sektion Interface (Format 4.1)"
beschlossen (`scratchpad/v5-design/build-pakete.md:664-680`, BP-09). Dieser Header **ist** eine
Exportliste — MOD-24/A, MOD-02/D und MOD-12 in anderer Kleidung. Die zweite Fassung hat diesen
Beschluss nicht ein einziges Mal erwähnt; jetzt hängt MOD-37 daran.

**(F) Der Punkt ist Modultrenner UND Membertrenner — und das Mangling weiß es nicht.** Gelesen
`NameMangling.cs:21-22` (`ForFunction` = `<module>.<fn>`) und `:35-37` (`ForMethod` =
`<module>.<type>.<method>`); der Doc-Kommentar (`:9-11`) sagt „the module path is already unique
— one file is one module — so that suffices." Das stimmt nur, solange kein Typ so heißt wie ein
Untermodul: gemessen (`q07`) ist `a.b.c` einmal `Modul a → Typ b → Methode c` und einmal
`Modul a.b → Funktion c`, und der Verifier meldet ein Compiler-Defekt für ein legales Programm.
Dazu (`q13b`): die Instanz einer generischen Funktion trägt den **bloßen** Typnamen
(`lib.ident<Secret>`, `NameMangling.cs:79`, `named.Path[^1]`), also kollidieren zwei fremde,
modulprivate Typen gleichen Namens im Namen derselben Instanz — und weil der Lowerer die zweite
Instanz für die erste hält, ist die Meldung eine **Typverwechslung**, nicht ein Duplikat.

**(G) Kleinere Inkonsistenzen.**
- Die Warnung über unbenutzte Importe erfasst zwei von drei Formen (`p13` vs. `p22`;
  `WarningAnalyzer.cs:360-364`); die Spec macht den Unterschied nicht.
- Der Punkt im Modulpfad hat **eine** Hierarchiebedeutung, für Capabilities
  (`src/Lyric.Core/Capabilities.cs:96`, String-Präfix).
- Ein Typalias trägt den Namen über die Grenze, nicht die Bau-Fähigkeit (`r04`/`r04b`/`r04k`) —
  Alias-Befund, Gebiet Typen.
- Ein importiertes Modul darf keine `fn main` haben (`q09`), die Meldung sagt nicht, warum.
- Ein Modulpfad-Tippfehler bekommt keinen Vorschlag, ein Bezeichner-Tippfehler schon
  (`q10`/`q10k`).
- Ein Benutzer darf `panic` still verdecken (`q03b`/`q03c`); ein Import, der `string` verdeckt,
  warnt (`r11k`).

---

## 2. Sprachvergleich

| Sprache | Modul = | Sichtbarkeitsstufen | Default | Re-Export | Zyklen | Alias / selektiv / Glob |
|---|---|---|---|---|---|---|
| **Rust** | Datei **und** `mod`-Block, Baum je Crate | `pub`, `pub(crate)`, `pub(super)`, `pub(in p)`, privat | privat | `pub use` | **erlaubt** innerhalb der Crate, verboten zwischen Crates | `use a::b as c`, `use a::*` |
| **Go** | **Verzeichnis** (Package), nie eine Datei | 2, an der Groß-/Kleinschreibung; plus `internal/`-Verzeichnisregel | paketprivat | nur über Typalias | **verboten**, Compilerfehler | `import a "path"`; kein selektiver Import; **`import . "pkg"` ist ein Glob** (Dot-Import, verpönt) |
| **Swift** | **Build-Target**, viele Dateien | `open`, `public`, `package` (5.9), `internal`, `fileprivate`, `private`; **dazu die geklammerten Setter-Stufen `private(set)`/`internal(set)` seit Swift 1** | `internal` | kein offizielles | verboten zwischen Modulen | `import struct M.T`, kein Alias; `@testable import` |
| **C#** | Namensraum (dateiunabhängig) **plus** Assembly als Grenze | `public`, `internal`, `private`, `protected`, Kombis, `file` (C# 11) | je nach Kontext `private`/`internal` | nur `[assembly: TypeForwardedTo]` | **erlaubt** im Namensraum, verboten zwischen Assemblies | `using X = A.B;`, `global using`; `[InternalsVisibleTo]` für Tests |
| **Python** | Datei = Modul, Verzeichnis = Package | keine, nur `_name`-Konvention und `__all__` — **auch an Membern keine** | alles öffentlich | ja, per Re-Import in `__init__.py` | erlaubt, aber fragil | `from a import b as c`, `from a import *` |
| **OCaml** | Kompilationseinheit (Datei) + `module`-Ausdrücke | die **Signatur** (`.mli`) ist die Exportliste | ohne `.mli` alles sichtbar | `include` | **verboten** zwischen Einheiten | `open`, `module M = A.B` |
| **Java** | Package (Verzeichnis) + JPMS-Modul darüber | `public`, `protected`, package-private, `private`; JPMS `exports` | package-private | JPMS `exports` | erlaubt zwischen Packages, `requires`-Zyklen verboten | `import a.b.C`, `import a.b.*`, **kein Alias** |
| **Kotlin** | Package (verzeichnisunabhängig), `internal` = Compilation-Modul | `public`, `internal`, `protected`, `private` (top-level = Datei) | `public` | nur über `typealias` | erlaubt | `import a.b.C as D`; **Extensions sind Top-Level-Deklarationen und werden namentlich importiert/aliasiert** |
| **Zig** | Datei = Container, importiert als Wert | `pub` je **Deklaration**; Felder haben keine | Deklaration privat, Feld öffentlich | über einen `pub const` | erlaubt | `const x = @import("f.zig").y;`; `test "…" {}` steht **in der Datei** |
| *(Lyric 4)* | *Datei* | *2 (`pub` / nichts), Member immer öffentlich* | *modulprivat für Top-Level, öffentlich für Member* | *nein* | *verboten* | *`import a.b as c`, `import a.b { x }`, kein `{x as y}`, kein Glob* |

### Die entgegengesetzte Entscheidung: Go

Go hat **dieselbe** Antwort wie Lyric auf Zyklen (verboten) und die **entgegengesetzte** auf die
Einheit: ein Package ist ein Verzeichnis, nie eine Datei. Innerhalb eines Packages gibt es keine
Grenze. *Korrektur gegenüber der zweiten Fassung*, die schrieb, Go könne „die Importliste einer
Einheit lesen, ohne Dateien zu öffnen": Go **muss** die Quelldateien eines Packages öffnen, um
seine Importe zu lesen (`go list` parst die Importzeilen). Der Gewinn, den Pike in „Go at
Google" beschreibt, ist ein anderer: die Exportdaten eines Packages enthalten alles, was ein
Konsument transitiv braucht, also öffnet ein Bau nur die **direkten** Abhängigkeiten, nie
Header-Ketten (behauptet — aus dem Vortrag, nicht nachgeschlagen). Was Go dadurch gewinnt, ist
genau das, was Lyric 4 fehlt: eine Bibliothek aus fünf Dateien darf intern alles teilen und
exportiert trotzdem nur, was sie will. Was Go zahlt: keine Feinsteuerung innerhalb eines
Packages, Sichtbarkeit hängt an der Schreibweise. Die `internal/`-Regel ist das Pflaster, weil
zwei Stufen zu wenig sind. Und Gos Dot-Import (`import . "pkg"`) ist der Beweis, dass ein Glob in
einer Sprache mit lokal lesbarem Code so unbeliebt wird, dass die Style-Guides ihn verbieten —
das stärkste Argument gegen MOD-08/B.

### Die zweite gegenläufige Entscheidung: Zyklen in Rust und C#

Beide erlauben Zyklen innerhalb der Kompilationseinheit. Der Preis ist **nicht** eine fehlende
Initialisierungsreihenfolge für Globals: Rust verlangt für ein `static` einen const-auswertbaren
Initialisierer, weil es kein „life before main" will (Laufzeitinit ist über
`OnceLock`/`LazyLock` Normalfall); C#-Typinitialisierer laufen garantiert vor dem ersten Zugriff,
unbestimmt ist nur die relative Reihenfolge wechselseitig abhängiger Typen. Für Lyric folgt:
**Lyrics Verbot ist die Bezahlung für §4.3** (Modulkonstanten in Importreihenfolge), und die
Spec sagt den Preis (`lyric-spec/spec/04-modules.md:59`). Wer Zyklen erlaubt, muss für die
Globals eine neue Regel mitbringen. Siehe MOD-10.

### Wer Tests privilegiert

*Korrektur gegenüber der zweiten Fassung („vier der neun").* Tests bekommen bewusst
privilegierten Zugriff in **Rust** (Kindmodul in derselben Datei), **Go** (`_test.go` im selben
Package), **Swift** (`@testable import`), **Java** (package-private im selben Package), **C#**
(`[assembly: InternalsVisibleTo("Tests")]` existiert genau dafür), **Kotlin** (Test-Source-Sets
sehen `internal` desselben Moduls), **Zig** (`test "…" {}` steht in der Datei selbst); **Python**
hat keine Grenze, die Frage stellt sich nicht; **OCaml** hat keinen eingebauten Weg (ein Test
sieht, was die `.mli` zeigt). **Sieben von neun**, plus Python. Lyric wäre mit MOD-04 ohne
MOD-16 die einzige Sprache im Feld, die Weißbox-Tests *verbietet*.

### Was zu Lyrics Charakter passt

- **Rusts Stufenmodell** (`privat` / `pub(crate)` / `pub`) — drei Stufen, eine Achse.
- **OCamls Signaturgedanke** existiert in Lyric bereits als `opaque type` (`LYR-SEM0093`).
- **Gos Einheit = Paket** löst das Problem, das Lyric 4 wirklich hat.
- **Kotlins Extension-Modell** (Extension = importierbarer Name, aliasierbar) ist die billigste
  Antwort auf den Extension-Konflikt (`r03`) — *Korrektur*: die zweite Fassung hielt Kotlin
  für lösungslos.
- **Rusts Orphan Rule** ist die strukturelle Antwort auf denselben Konflikt.
- **Nicht** passt: Swifts `fileprivate`, Javas fehlender Import-Alias, Pythons
  Konvention-statt-Regel, C#' Kombinationsexplosion, Gos Dot-Import.

---

## 3. Designfragen

Jede Frage: Ist-Stand mit Beleg, Optionen (Vorbild, Preis), Empfehlung, Bruchgrad,
Abhängigkeiten, Belegstufe.

### MOD-01 — Was ist ein Modul: Datei, Verzeichnis oder deklarierte Einheit?

**Heute:** eine Datei (`lyric-spec/spec/04-modules.md:5`, `docs/guide/12-modules.md:3`). Ein
Zwischensegment braucht keine Datei (gemessen `p17`). **Aber** die Identität eines Moduls ist
heute sein *Name*, nicht seine *Datei*: dieselbe Datei kann zweimal Modul sein (`q08`, MOD-32).

| Option | Vorbild | Preis |
|---|---|---|
| **A: Datei bleibt Modul** | Rust (Dateiseite), OCaml, Zig | Keine Migration. Kapselung wird in MOD-02 gelöst; die Identitätsfrage in MOD-32. |
| **B: Verzeichnis ist Modul** | Go | Löst Kapselung und Zyklen in einem. Bricht jeden Modulnamen, jeden Import, die stdlib, `lyric.json`, das Bytecode-Namensschema und die Host-API. Milestone-groß. |
| **C: Datei = Modul, plus geschachtelte `module { … }`-Blöcke** | Rust | Zweiter Mechanismus, ein Modul zu bilden — Rule 2. |

**Empfehlung: A.** Die Datei als Modul ist überall eingebaut (Mangling, Host-`Compile`,
`lyric.json`, LSP). B bezahlt einen Umbau für ein Problem, das MOD-02 mit einer Stufe löst.
**Bruch:** nein. **Abhängig von:** MOD-02, MOD-03, MOD-32. **Belegstufe:** gemessen.

---

### MOD-02 — Was ist die Kapselungs-Einheit: die Datei oder das Paket?

**Heute:** die Datei. Was Datei 2 von Datei 1 braucht, muss `pub` sein und ist damit für jeden da.

**Was die Frage bemisst:** welche `pub`-Deklaration ist nur `pub`, weil eine Schwesterdatei sie
braucht? Gezählt (gelesen): **313 `pub fn` von 436 Top-Level-Funktionen in 25 stdlib-Dateien**,
also 123 modulprivate Helfer; über Modulgrenzen werden **42 verschiedene Namen** selektiv
importiert, **10** davon kommen in keinem Guide- und keinem Spec-Kapitel vor. Das ist eine obere
Schranke, kein Ergebnis — mehrere der zehn (`cos`, `tau`, `isNaN`) sind echte API. **Die exakte
Zahl ist ohne einen maschinenlesbaren API-Begriff nicht bestimmbar** (1.3 E, MOD-24).

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine zweite Stufe „sichtbar innerhalb des Roots"** (Schreibweise MOD-26) | Rust `pub(crate)`, Swift `package`, C#/Kotlin `internal` | Der Begriff „Root" existiert normativ (`04-modules.md:10-17`) und wird durchgesetzt. Kosten: Sema, Spec-Satz, Diagnosecode, Schlüsselwort. **Braucht MOD-17** (Root ohne Manifest) und **MOD-41** (der Testroot). |
| **B: `pub(module)`** — sichtbar im Modul und seinen Untermodulen (Präfix) | lose an Rusts `pub(in path)` | Braucht MOD-03/B: eine Untermodulbeziehung, die es nicht gibt (`p17`), und der Punkt bekäme eine dritte Bedeutung. |
| **C: nichts** | Zig kommt am nächsten | Kostet nichts. Eine Bibliothek aus mehreren Dateien bleibt ohne internes API. |
| **D: Exportliste je Paket** | OCaml `.mli`, Java JPMS — **und Lyrics eigener Beschluss BP-09** (`build-pakete.md:664-680`: Header mit Interface-Sektion) | Präzise, und die Liste ist, was ein Paketmanager veröffentlicht. Aber ein zweiter Ort für dieselbe Aussage neben `pub` (Rule 2) — es sei denn, die Liste wird **aus `pub` erzeugt**, nie geschrieben (MOD-37). |

**Empfehlung: A, ausdrücklich nicht B — nach MOD-24, und mit der Auflage, dass D nur als
*erzeugte* Liste existiert.** Der Root ist die Einheit, die ein Benutzer ausliefert, die
`lyric.json` benennt und die der Compiler kennt. Solange niemand sagen kann, *welche* `pub` nur
Nachbarschafts-`pub` sind, ist die Größe des Problems unbekannt.
**Bruch:** nein (additiv). **Abhängig von:** MOD-17, MOD-24, MOD-26, MOD-37, MOD-41, MOD-04,
MOD-14. **Belegstufe:** gelesen (Zählung), gemessen (Proben).

---

### MOD-03 — Hat der Modulpfad eine Hierarchie, oder ist der Punkt nur ein Zeichen?

**Heute: beides, je nach Frage.** Auflösung/Sichtbarkeit/Initialisierung: `a.b` ist ein Name ohne
Beziehung zu `a` (`r10/b`, `p17`). Capabilities: Präfixbaum (`src/Lyric.Core/Capabilities.cs:96`).
**Neu (`q07`):** im Bytecode-Namensschema ist der Punkt Modultrenner *und* Membertrenner, und
das kollidiert — MOD-31.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Punkt bleibt Name; Capabilities behalten ihre Präfixregel als benannte Ausnahme** | Go | Ein Spec-Satz. Nichts bricht. **Aber MOD-31 muss dann das Mangling trennen**, denn „der Punkt ist nur ein Zeichen" gilt im Bytecode gerade nicht. |
| **B: echte Hierarchie** — Elternmodul muss existieren | Rust, Java-Packages | Zwingt `shapes.lyr` neben `shapes/circle.lyr` (bricht `p17`). Kauft `pub(module)`, Facade, MOD-29-Antwort — und verbietet nebenbei „Typ `b` in `a` neben Modul `a.b`" nicht. |
| **C: Hierarchie ohne Existenzzwang** | Python PEP 420 | Ein Präfix, das niemand deklariert, kann nichts kapseln. |

**Empfehlung: A.** Die Präfixbedeutung ist genau eine (Capabilities) und dort richtig. Die
Bytecode-Kollision ist ein Mangling-Befund (MOD-31), keine Hierarchiefrage.
**Bruch:** nein (A), major (B). **Abhängig von:** MOD-02, MOD-31. **Belegstufe:** gemessen.

---

### MOD-04 — `pub` auf dem qualifizierten Weg durchsetzen

**Heute:** wird nicht geprüft — Funktionen, Globals, Typen, `static let`, Enum-Varianten (Wert
und Pattern), `::`-Listen, Generics-Schranken, `extend`-Ziele **und `extern`-Deklarationen**
(`p03`, `k03`, `k02`, `r05`, `r06`, `q06`), über Projektgrenzen (`p37`). Nur der selektive
Import prüft (`r01k`, `q06k`). Bei nicht-`pub` Überladungen verfälscht die halbe Prüfung die Wahl
(`r16`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: durchsetzen — Warnung ab 4.7, Fehler ab 5.0** | alle neun | Bricht jedes Programm, das die Lücke nutzt. Die stdlib nutzt sie nicht (gelesen). **Der eigene Testroot nutzt sie** (`q14`). Ohne MOD-16/MOD-41 warnt sie über Programme, die nach der Antwort richtig sein können. **Und eine Uhr, die nur einen Teil der Stellen warnt, lässt ab 5.0 den Rest ohne Vorwarnung brechen** — die Betroffenenliste ist in jeder Runde gewachsen (`r05`, `r06`, `q06`). |
| **B: durchsetzen ohne Uhr, im 5.0-Sprung** | — | Billiger im Compiler, teurer beim Benutzer. |
| **C: die Spec umschreiben, `pub` gilt nur selektiv** | keine | Ehrlich, aber `opaque type` verlöre seinen Sinn (`LYR-SEM0093` hängt daran, dass der Nachbar nicht qualifiziert konstruiert). |

**Empfehlung: A — gebündelt mit MOD-16/MOD-41 und NACH MOD-25.** Zwei Auflagen, beide aus
Messungen: (1) der erste Code, den die Warnung trifft, ist ein Weißbox-Test (`q14`); ob der falsch
ist, entscheidet MOD-16, und **sieben von neun** Vergleichssprachen geben Tests privilegierten
Zugriff (§2). (2) Die Warnung darf erst laufen, wenn die Stellenliste **aus dem Symbolmodell**
kommt (MOD-25/A) — eine Warnung, die `extern` vergisst, ist ab 5.0 ein stiller Bruch für jeden
Embedding-Host. `docs/Befunde_und_Verbesserungen/PLAN.md:360-363` verlangt einen Befund, der ohne
die offene Regel steht; das gilt für beide Auflagen.
**Wortlaut:** `'hidden' is not public in 'lib'` (wie `LYR-RES0004`), `until = 5.0`.
**Bruch:** major. **Abhängig von:** MOD-16, MOD-41, MOD-25, MOD-27. Voraussetzung für MOD-02,
MOD-05, MOD-06. **Belegstufe:** gemessen.

---

### MOD-05 — Member-Sichtbarkeit: Default privat?

**Heute:** jedes Member eines Typs ist öffentlich, lesend und schreibend (`p02`); ein nicht-`pub`
`static let` ist von außen lesbar, obwohl seine Sichtbarkeit gespeichert wird (`r05`,
`Resolver.cs:108`). **Syntaktisch zweigeteilt** (`q01`, `q01k`, `q02`): `pub` parst an einer
Methode, an `static let` und am Interface-Member, **nicht an einem Feld** (`Grammar.md:237`,
Prototyp 17 README). Vollständig durchdacht in `design/member-visibility.md` (Uhr in 4.7 laut
`PLAN.md:367`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: Default privat, `pub` exportiert, Root-Stufe dazwischen** | Rust (Felder privat), C# (Member privat), Swift (Member `internal`) | Der größte Bruch der Liste. Braucht zwei Minor-Zyklen und `lyrfix`. Erlaubt Invarianten. |
| **B: Default privat nur für Felder, Methoden brauchen `pub`** | **Zig** (Deklarationen nur mit `pub`, Felder immer sichtbar — die Spiegelung), Rust | Erklärbar: Feld = Repräsentation, Methode = Verhalten. Deckt die Invariante, nicht die API-Fläche. |
| **C: nur ein `readonly`-Feldmodifikator** | — | Löst „Invariante" ohne Bruch. Löst keine der anderen Folgen. |
| **D: nichts ändern** | **Python** (keine Member-Sichtbarkeit, alles öffentlich) | Kostet nichts. Lyric bleibt mit Python allein; alle anderen sieben haben eine Member-Stufe. |

*Korrektur gegenüber der zweiten Fassung:* dort stand „D hat überhaupt kein Vorbild". Falsch nach
der eigenen Tabelle — Python ist genau das Vorbild für „alle Member öffentlich".

**Empfehlung: A, wie `design/member-visibility.md` — mit Zusätzen, die die Messungen erzwingen:**
1. **Der Migrationslauf ist zweigeteilt.** Methoden und `static let`: das `pub` steht seit je in
   Bestandscode und bekommt plötzlich Wirkung — ein *stiller Bedeutungswechsel*, der eine eigene
   Warnung braucht („`pub` on this method has no effect today; from 5.0 it exports the member").
   Felder: **neue Grammatik** (`Field = [ 'pub' ] IDENTIFIER …`), kein Bestandscode, kein
   Bedeutungswechsel — nur die Default-Änderung. Die Uhr muss beide getrennt benennen.
2. Der Initializer-Fall ist derselbe wie `LYR-SEM0093` und sollte denselben Satz führen.
3. `static let` gehört dazu (`r05`): Sichtbarkeit gespeichert, Leser fehlt — billigster Fall.
4. `pub` auf `extend`-Blöcken und in Interfaces — MOD-15, MOD-21.
5. Reachability: Member sind heute keine Wurzeln (MOD-12).
**Bruch:** major. **4.x-Warnstufe:** 4.7 Warnung beim Fremdzugriff auf ein Member ohne `pub` +
Warnung am wirkungslosen `pub`; 4.8 mit Versionsnennung; 5.0 Fehler. **Abhängig von:** MOD-04,
MOD-02, MOD-21, MOD-22, MOD-27. **Belegstufe:** gemessen.

---

### MOD-06 — Ein privater Typ in einer öffentlichen Signatur

**Heute:** schweigend erlaubt (`p05`). Funktioniert nur, weil MOD-04 offen ist.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Fehler** | Java, Swift, Rust bis 1.73 (E0445/E0446) | Klar, lokal prüfbar. Bricht Code, sobald MOD-04/05 stehen. Ausweg: `pub` oder `opaque type`. |
| **B: Warnung** | Rust seit 1.74 (RFC 2145, `private_interfaces`/`private_bounds`, warn-by-default) | Eine Warnung über eine Signatur wird man nie los. Rust ging diesen Weg *nach* dem Fehler. |
| **C: erlauben, Typ „anonym exportieren"** | keines sauber (Swifts `some P` verlangt eine öffentliche Schranke) | In Lyric schon da: `opaque type`. Sonderweg wäre ein zweiter Mechanismus. |

**Empfehlung: A, zusammen mit MOD-04/05 in einer Release.** C existiert als Sprachmittel und ist
die Antwort für den Autor. **Neu:** die generische Variante — ein privater Typ als Typargument
einer `pub` generischen Funktion — ist **kein** Signaturfall, sondern ein Instanzfall und
gehört nach MOD-44.
**Bruch:** minor bis major. **4.x-Warnstufe:** mit MOD-05 in 4.7. **Abhängig von:** MOD-04,
MOD-05, MOD-44. **Belegstufe:** gemessen.

---

### MOD-07 — Re-Export / Facade-Module

**Heute:** unmöglich (`p06`: `LYR-SEM0012`; `r18/a`: `pub import` ist `PAR0025`). Ursache:
`MemberFacts.OfModule` schließt `ImportBindingSymbol` aus (`MemberFacts.cs:117-119`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: nichts** | Swift, Java ohne JPMS | Eine Bibliothek aus zehn Dateien zwingt zu zehn Importen. Eine Root-Stufe regelt, wer *hineinsieht*, nicht, unter welchem Namen es *herauskommt*. |
| **B: `pub import a.b { x };`** | Rust `pub use` | Eine neue Schreibweise über einer existierenden Form. Ein Name hat nicht mehr ein Zuhause; §4.3a muss sagen, was eine verteilte Überladungsmenge ist. |
| **C: `pub import a.b;`** | Python `__init__.py`, OCaml `include` | Bringt die Extensions mit (`04-modules.md:29`) — MOD-20. |
| **D: `pub type`-Weitergabe genügt** | Go, Kotlin (`typealias`) | Deckt Typen nur halb (`r04`/`r04b`/`r04k`: Name ja, Konstruktor nein). |

**Empfehlung: B, erst nach MOD-02, nur selektiv.** Mit einer Root-Stufe wird die Facade zur
notwendigen Form.
**Bruch:** nein. **Abhängig von:** MOD-02, MOD-20, MOD-23, §4.3a. **Belegstufe:** gemessen.

---

### MOD-08 — Importformen: `{ x as y }`, Glob, voll qualifizierter Pfad

**Heute:** drei Formen (`Grammar.md:160-162`). Es fehlen Alias je Name (`r20/a`), Glob, voller
Pfad ohne Bindung (`r10/a`). **Und die drei Formen sind für dasselbe Modul kombinierbar**
(`q12`, `q16b`) — MOD-38.

| Option | Vorbild | Preis |
|---|---|---|
| **A: `import a.b { x as y };`** | Rust, Python, Kotlin, C# | Rein additiv. Kauft: ein selektiv importierter Name, der lokal kollidiert, bekommt einen anderen — **und mit MOD-20 die Kotlin-Lösung für Extensions**. |
| **B: Glob `import a.b { * };`** | Java, C#, Rust, Python — **und Gos Dot-Import, dort verpönt** | Jede Namensauflösung hängt vom Inhalt eines fremden Moduls ab; eine neue `pub` Funktion dort bricht ein Programm hier. Go hat den Glob und die Style-Guides verbieten ihn: das ist das stärkste Argument gegen B. |
| **C: voll qualifizierte Pfade immer erlauben** | Java, C# | Braucht MOD-03/B. Zwei Wege (Rule 2). |
| **D: nichts** | Go (kein selektiver Import) | Qualifizierter Zugriff plus `as` bleibt der Weg. |

**Empfehlung: A, nicht B, nicht C** — A wird durch MOD-20 von „kleine Ergonomie" zu „Teil der
Extension-Antwort" aufgewertet.
**Bruch:** nein. **Abhängig von:** MOD-20, MOD-38, MOD-29. **Belegstufe:** gemessen.

---

### MOD-09 — Der Bindungsname ist das letzte Segment

**Heute:** `import a.b;` bindet `b` (`04-modules.md:24`); `import a.b; import b;` ist
`LYR-RES0001` (`p23`). `as` ergänzt die Bindung, statt sie zu ersetzen (`r02`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: bleibt, `as` ist die Antwort** | Rust, Go | Die Meldung nennt den Ausweg nicht — „use `as`" wäre die Verbesserung. |
| **B: bei Kollision den vollen Pfad verlangen** | Java, C# | Braucht MOD-03/B. |
| **C: Modulnamen in einen eigenen Namensraum** | OCaml | Zwei Namensräume statt einem; bricht `LYR-RES0001` und §4.3a. |

**Empfehlung: A plus die Hilfe in der Meldung.** **Bruch:** nein. **Abhängig von:** MOD-30.
**Belegstufe:** gemessen.

---

### MOD-10 — Import-Zyklen

**Heute:** verboten (`LYR-RES0005`, `r07`; `Resolver.cs:257-280`), begründet mit §4.3
(`04-modules.md:59`). **Die Meldung nennt bei drei Modulen keinen Pfad (`r07`) und zeigt bei
einer Entry-Datei, die nicht `main.lyr` heißt, auf das falsche Modul (`q08`) — MOD-40.**

| Option | Vorbild | Preis |
|---|---|---|
| **A: bleibt verboten** | Go, OCaml, F# | Zwei Typen in zwei Dateien, die einander erwähnen, sind unmöglich; der Ausweg (dritte Datei) muss der Benutzer selbst finden. |
| **B: innerhalb eines Roots erlaubt, Globals per Laufzeitprüfung** | Rust, C#, Java | Tauscht eine statische Garantie (`LYR-SEM0057`) gegen `LYR-VM0017` zur Laufzeit. |
| **B′: innerhalb eines Roots erlaubt, aber im Zyklus keine Modulkonstante mit nicht-literalem Initialisierer** | Rust (`static` braucht const-Initialisierer) | Behält die statische Garantie. **Preis, den die zweite Fassung untertrieben hat:** Lyric hat **keinen** Begriff „konstanter Initialisierer" — kein `const`, keine const-eval. Der einzige verwandte Begriff ist die Literalregel für Attributargumente (`Grammar.md:191-194`: Literal, optional signiert, oder ein `let`-Name, der an eines gebunden ist). B′ müsste diese Regel zur Sprachregel befördern — ein neuer Begriff, nicht nur „eine Regel und ein Diagnosecode". |
| **C: ganz erlauben** | Python | Halb initialisierte Module. |

**Empfehlung: A bleibt, bis MOD-02 steht; danach B′ — aber nur, wenn der Literalbegriff aus §4.7
wiederverwendet wird, nicht neu erfunden.** Ist er zu eng (kein `let x = List.new()` in einem
Zyklusmodul), bleibt A.
**Nicht gemessen:** ob `LYR-VM0017` im Zyklusfall greift (unerreichbar).
**Bruch:** nein (Lockerung). **Abhängig von:** MOD-02, MOD-40, Gebiet Laufzeit, Gebiet
Attribute. **Belegstufe:** gemessen (A), gelesen (B′-Preis).

---

### MOD-11 — Der Header und der Name der Entry-Datei

**Heute:** drei Ableitungen, keine liest den Dateinamen im CLI-Baupfad. Gemessen: ohne Header
heißt das Modul `main`, egal wie die Datei heißt (`r19`, `q04` — auch eine Bibliothek); mit
Header gewinnt der Header (`r19`); der Header darf `std.core` beanspruchen (`r08`, Fehlerregen in
stdlib-Dateien). **Und (`q08`/`q08c`): ein Entry, der nicht `main.lyr` heißt, wird von einem
`import <dateiname>;` ein zweites Mal geladen** — `duplicate 'main' function` an derselben Zeile,
Zyklusmeldung auf das falsche Modul. Kontrolle `q08k`: mit `main.lyr` nur `RES0005`. Gelesen:
`Compilation.cs:105-108` (`name`, Header, `["main"]`), `SourceCompiler.cs:103` (Entry ohne
Namen), `:360` (Workspace: Pfadname, Header gewinnt), `LangVm.cs:252-254` (Embedding:
Dateiname), `Compilation.cs:167` (Dedupe nach Name).

| Option | Vorbild | Preis |
|---|---|---|
| **A: der Dateiname entscheidet, auch für die Entry-Datei; ein abweichender Header ist `RES0006`** | Java (für `public` Top-Level-Klassen), Go | Konsistent mit importierten Dateien und mit dem Embedding-Pfad. **Bricht jede Entry-Datei ohne Header, die nicht `main.lyr` heißt**, und — schwerer — **jeden Bytecode-Funktionsnamen** einer so gebauten Bibliothek: `main.newBox` wird `api.newBox`, und ein Host, der `Call("newBox")` sagt, findet es unter anderem Modul (MOD-45). Braucht MOD-19. |
| **B: nur die Roots schützen** — ein Header, dessen erstes Segment einem fremden Root gehört, ist ein Fehler an dieser Zeile | Rust (Crate-Namen sind vergeben), Gos `internal/` | Minimale Reparatur des Hijacks. Sitzt in der Registrierungsreihenfolge (`Compilation.cs:242-245` vor `:153-158`). **Repariert die Doppelladung nicht.** |
| **E: der Loader dedupliziert nach Dateipfad** — eine Datei, die schon Modul ist, wird unter einem zweiten Namen nicht noch einmal geladen, sondern gemeldet („`app.lyr` is already loaded as module 'main'; name the file `main.lyr` or add a header") | jeder Linker (eine Objektdatei, ein Symbolraum) | Repariert `q08` **ohne** die Namensregel zu ändern. Ein Vergleich normalisierter Pfade im Loader plus ein Diagnosecode. Nichts, was heute richtig ist, bricht. |
| **C: A + B + E** | — | A für Konsistenz, B und E für die Meldungen. |
| **D: nichts** | — | Zwei stille Fallen bleiben (`r08`, `q08`). |

**Empfehlung: E und B sofort (4.7), A in 5.0 — und A bekommt dieselbe Uhr wie MOD-45.**
*Korrektur gegenüber der zweiten Fassung („C, mit B zuerst"):* B repariert nur den `std.*`-Hijack;
den gemessenen Doppel-Lade-Fall behebt erst E oder A. E ist die Reparatur, die A nicht
vorwegnimmt: sie sagt dem Benutzer, dass seine Datei zwei Namen hat, ohne zu entscheiden, welcher
der richtige ist. A gehört in 5.0, weil es Bytecode-Namen ändert (`q04`), und „Bruch: minor" aus
der zweiten Fassung war für A zu klein — siehe MOD-45.
**Bruch:** B nein, E nein, A **major am Host-Rand** (Bytecode-Namen), minor in der Sprache.
**4.x-Warnstufe:** A als Warnung in 4.7 („this file is `app.lyr` but the module is `main`; from
5.0 the file name decides — function names in the built module will change"). **Abhängig von:**
MOD-19, MOD-32, MOD-34, MOD-45. **Belegstufe:** gemessen.

---

### MOD-12 — Ist `pub` die Host-Oberfläche, und welche `pub` sind Wurzeln?

**Heute:** zwei Antworten, die nicht zusammenpassen (`q04k`, `r14`): Wurzeln sind nur `pub`
Top-Level-Funktionen (`ModuleLowerer.cs:206`, `04-modules.md:186-188`) — `Box.get` ist nach dem
Bau weg; die nicht-`pub` `helper` steht als `api.helper` in der Funktionstabelle und ist vom
Host nach Namen aufrufbar (`docs/guide/14-embedding.md:29-34`). **Und ohne Header heißt alles
`main.*`** (`q04`) — MOD-34.

| Option | Vorbild | Preis |
|---|---|---|
| **A: `pub` ist die Host-Oberfläche** — der Host kann nur aufrufen, was `pub` ist; `pub` Member `pub`er Typen sind Wurzeln | Swift/.NET `public` | Konsequent. Braucht entweder einen Export-Abschnitt im `.lyrbc` (Formatänderung, Spec 13, ältere Runtimes) oder eine Namensprüfung im Host-API, die bestehende Hosts bricht. **Die Interface-Sektion aus BP-09 (`build-pakete.md:664-680`) ist genau dieser Abschnitt** — A ist also nicht „MOD-24 in anderer Kleidung", sondern *bereits beschlossen und nicht gebaut*. |
| **B: `pub` ist nur ein Pruning-Hinweis** | heutiger Zustand | Ehrlich benennbar, aber `pub` in einer Bibliothek ist dann etwas anderes als `pub` in der Sprache. |
| **C: A, aber Member werden nicht Wurzeln** | Go, C-ABIs | Passt zur Host-API, die nur freie Namen kennt. Dann ist `pub` auf einer Methode einer Bibliothek eine Lüge, die stillschweigend weggeschnitten wird — `r14`. |

**Richtungskonflikt, der benannt gehört:** MOD-05/A macht Member privat (weniger `pub`), MOD-12/A
macht `pub` Member zu Wurzeln (mehr Wurzeln). Nettoeffekt ungemessen.

**Empfehlung: A — als dieselbe Entscheidung wie BP-09, nicht als eigene.** Die Sandbox ist das
Verkaufsargument der Runtime; „was der Host aufrufen darf" ist eine Sicherheitsaussage. Minimal
und sofort: eine `pub` Methode eines `pub` Typs, die weggeschnitten wird, gehört gemeldet.
**Bruch:** Sprache minor, **Format major** (Interface-Sektion). **Abhängig von:** MOD-05,
MOD-24, MOD-34, MOD-37, BP-09, Gebiet FFI/Embedding, Gebiet Bytecode. **Belegstufe:** gemessen.

---

### MOD-13 — Unbenutzte Importe: eine von drei Formen warnt nicht

**Heute:** selektiv und `as` warnen (`p22`, `q12c`), bare nicht (`p13`, `q12b`);
`WarningAnalyzer.cs:360-364` nennt es Absicht, die Spec (`04-modules.md:29-33`) nicht.

| Option | Vorbild | Preis |
|---|---|---|
| **A: alle drei Formen warnen** | Rust (`unused_imports`), Go (Fehler) — nicht C#, nicht Swift | Die Begründung im Code trägt nicht: bare und `as` binden dasselbe Symbol. **Mit MOD-38 muss „benutzt" für zwei Importe desselben Moduls definiert sein.** |
| **B: Spec an den Code anpassen** | — | Eine Ausnahme ohne Grund. |
| **C: Fehler** | Go | Zu hart ohne `lyrfix`. |

**Empfehlung: A.** **Bruch:** nein. **Abhängig von:** MOD-38. **Belegstufe:** gemessen.

---

### MOD-14 — Paket-Namensraum: darf ein Segment zwei Dinge bedeuten?

**Heute:** nein („A segment belongs to one root", `04-modules.md:13-16`; gemessen `p37`). Keine
Versionen.

| Option | Vorbild | Preis |
|---|---|---|
| **A: ein Segment, eine Version** | Go vor Modulen, Maven | Diamant ist Handarbeit. |
| **B: mehrere Versionen, Identität = (Paket, Version)** | Cargo, npm | Ein Typ aus v1 ≠ v2 (diagnostizierbar, rustc hängt die Ursache an). Echter Preis: Monomorphisierung und ein Namensschema, das die Version trägt — **und das Namensschema trägt heute nicht einmal das Modul eines Typarguments** (`q13b`, MOD-44). |
| **C: Major-Version im Pfad** | Go Modules (`/v2`) | Sichtbar statt magisch, null Compiler-Arbeit. |

**Empfehlung: A für v5, C als Tür.** **Bruch:** nein. **Abhängig von:** Gebiet
Toolchain/Paketmanager, MOD-23, MOD-31, MOD-44. **Belegstufe:** gemessen.

---

### MOD-15 — `pub` an Stellen, an denen es nichts bedeutet

**Heute:** drei Stellen mit wirkungslosem `pub` — Methode (`q01k`/`p02`), `static let` (`r05`),
`extend`-Block (gelesen), Interface-Member (`q02`) — und eine Stelle, an der es nicht parst:
das Feld (`q01`). *Korrektur:* die zweite Fassung zählte „vor jedem Member" als eine Stelle.

| Option | Vorbild | Preis |
|---|---|---|
| **A: `pub` ablehnen, wo es nichts bedeutet** (Interface-Member, `extend`-Block); an Methoden und `static let` mit MOD-05 Bedeutung geben; **an Feldern neu erlauben** | Rust (`pub` in `trait` ist ein Fehler) | Bricht Code mit wirkungslosem `pub` — die stdlib schreibt `pub extend T { pub fn m() }` durchgehend. Braucht `lyrfix`. Setzt MOD-21 voraus. |
| **B: `pub extend` bekommt eine Bedeutung** | keine | Widerspricht `04-modules.md:29`. |
| **C: nichts** | — | Eine Grammatik, die eine Unterscheidung schreibt, die es nicht gibt. |

**Empfehlung: A, in einem Zug mit MOD-05 und MOD-21.** **Bruch:** minor (mechanisch).
**4.x-Warnstufe:** 4.7 „`pub` has no effect here", 5.0 Fehler. **Abhängig von:** MOD-05, MOD-21,
MOD-27. **Belegstufe:** gemessen.

---

### MOD-16 — Wie erreicht ein Test etwas Modulprivates?

**Heute: nur durch das Loch aus MOD-04** (`q14`/`q14k`). Gelesen: `docs/guide/20-testing.md:52-53`
(„ordinary programs of your project: they import your modules through the `sourceRoot`") und
`src/Lyrtest/Program.cs:100-150` — jede Testdatei ist eine **eigene Kompilation** mit sich selbst
als Entry und `sourceRoot` als Root. **Sobald MOD-04 greift, ist Weißbox-Testen unmöglich.**

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine Importform, die die Stufe aufhebt** (`@testable import calc;`) | Swift | Vierte Importform (Rule 2); der Compiler muss wissen, dass er *für Tests* übersetzt. |
| **B: der Testroot gehört demselben Root wie der sourceRoot** | Go, Java, C# (`InternalsVisibleTo`), Kotlin | Keine neue Schreibweise. **Aber: nicht umsetzbar ohne einen Satz, der `lyric.json` als Paketgrenze über `sourceRoot` UND `testRoot` definiert** — heute ist der Root der Testkompilation `src/`, und die Testdatei liegt *außerhalb* (MOD-41). Deckt nur die Root-Stufe, nicht Modulprivates. |
| **C: der Test wohnt in der Datei** | Rust | Volle Weißbox. Rückzug von `20-testing.md:4-5` („nothing has to be stripped"). |
| **D: „teste nur die öffentliche Fläche"** | Gos Ratschlag | 123 modulprivate stdlib-Funktionen nur indirekt testbar. |

**Empfehlung: B, mit D als Haltung — und mit der Regel aus MOD-41 als Bestandteil, nicht als
Nachtrag.** *Korrektur:* die zweite Fassung hielt B für „ohne neue Regel"; gemessen (`q14`,
`lyric check` sagt `2 modules ok`, `lyric test` kompiliert getrennt) braucht B den Satz „ein
Manifest spannt EIN Paket über sourceRoot und testRoot auf; die Root-Stufe gilt in beiden".
**Bruch:** keiner mit MOD-04 zusammen; ein Bruch jedes Testprojekts ohne. **Abhängig von:**
MOD-02, MOD-04, MOD-17, MOD-41. **Belegstufe:** gemessen.

---

### MOD-17 — Was ist „der Root" für eine Kompilation ohne `lyric.json`?

**Heute: das Verzeichnis der Entry-Datei, undeklariert.** Gelesen: `SourceCompiler.cs:263-264`
(`options.SourceRoot ?? fallbackSourceRoot`), Fallback `source.BaseDirectory` (`:85`), CLI
`project?.SourceRoot ?? directory` (`src/Lyrc/Program.cs:307`). Alle Proben dieses Dossiers
laufen so.

| Option | Vorbild | Preis |
|---|---|---|
| **A: der Root ist das Verzeichnis der Entry-Datei, normativ** | Zig | Ein Spec-Satz. Dieselbe Datei, aus einem anderen Verzeichnis übersetzt, wechselt Root und Sichtbarkeit. **Gilt nur für manifestlose Baue** — mit Manifest ist der Root `sourceRoot`, und die Testkompilation hat ihren Entry außerhalb davon (MOD-41). |
| **B: ohne Manifest ist die Root-Stufe gleich `pub`** | — | Ein Programm, das ein Manifest bekommt, verliert Zugriffe. |
| **C: ohne Manifest gleich modulprivat** | — | Jede Probe und jedes Snippet bricht. |
| **D: Manifest wird Pflicht ab zwei Dateien** | Cargo, Go Modules | Bricht `docs/guide/01-getting-started.md`. |

**Empfehlung: A, ausdrücklich beschränkt auf den manifestlosen Fall.** Teilfragen: (i) Root-Stufe
in der stdlib für ganz `std`? — ja, eine Auslieferungseinheit. (ii) Für einen nativen Root? — ja
(`SourceCompiler.cs:268-271` behandelt beide gleich).
**Bruch:** nein. **Abhängig von:** nichts. Voraussetzung für MOD-02, MOD-16, MOD-41.
**Belegstufe:** gelesen.

---

### MOD-18 — Welche Module sind implizit geladen, welche Namen implizit gebunden, und ist das normativ?

**Heute: fünf Module werden immer geladen, und das steht in keiner Spec; eine kleine Namensmenge
ist implizit gebunden, und das steht in der Spec — aber ohne Verdeckungsregel.**
*Korrektur gegenüber der zweiten Fassung („es gibt keine Prelude", „einzige der neun ohne implizite
Namensmenge"):* das war falsch und in sich widersprüchlich (Go und Zig standen im selben Satz als
„ohne"). Gemessen (`q03`): `panic("x")` kompiliert und läuft ohne Import. Gelesen:
`lyric-spec/spec/04-modules.md:137` („bound by the compiler without an import": `panic`, die
f-String-/Operatorhelfer, `rawToChars`); `src/Lyric.Frontend/Resolver/BuiltinTypes.cs:62-68`
(`panic` ist ein Symbol der **Builtins-Tabelle**, die jede Modultabelle als Parent hat,
`Compilation.cs:122`); `FunctionLowerer.cs:1871`, `:4805-4809` (`concat`/`repeat`/`fromXxx`
werden **per festem Importnamen** gebunden, nie per Namenssuche). Ladeliste: `WellKnownModules`
(`Compilation.cs:145-147`): `std.string`, `std.core`, `std.iter`, `std.fmt`, `std.collections` —
in **jede** Kompilation (`:150-158`), was den Hijack aus MOD-11 überhaupt möglich macht.

Lyric hat damit **zwei** implizite Mengen: eine Ladeliste (Code, nicht Spec) und eine
Bindungsliste (Spec, ohne Verdeckungsregel). `println` gehört zu keiner (`r10/c`). Was passiert,
wenn ein Benutzer einen Bindungsnamen verdeckt, ist MOD-35.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Ladeliste UND Bindungsliste in die Spec, samt Verdeckungsregel; keine Prelude darüber hinaus** | Go (nur `builtin`-Namen, alles andere importiert), Zig | Ein Spec-Absatz, null Code — aber erst, wenn MOD-35 die Verdeckungsregel beantwortet hat. Macht den Hijack beschreibbar. |
| **B: eine deklarierte, versionierte Prelude** (`println`, `Result`, `Opt`, `List`) | Rust `std::prelude` (2015/2018/2021), Kotlin, Java `java.lang` | `import std.io.console { println };` steht heute in jedem Guide-Beispiel. Jeder Prelude-Name kann ein Programm brechen — Lyric hat keinen Editionsbegriff. Rule 2. |
| **C: Prelude aus `lyric.json`** | C# `global using`/`ImplicitUsings` | Eine Datei ist ohne ihr Projekt nicht lesbar; MOD-17 zeigt, dass es das Manifest oft nicht gibt. |
| **D: nichts** | — | Ein zweiter Implementierer kann Lyric nicht bauen. |

**Empfehlung: A für 4.x — nach MOD-35, nicht davor; B als offene Frage für das Gebiet stdlib.**
**Bruch:** A nein, B minor. **Abhängig von:** MOD-35, MOD-11, Gebiet stdlib. **Belegstufe:**
gemessen.

---

### MOD-19 — Modulpfad auf Dateipfad: Groß-/Kleinschreibung

**Heute: plattformabhängig, und die Diagnose lügt** (`r09`: `import Lib;` findet `lib.lyr` auf
Windows; `r09b`: `RES0006` druckt `…\Lib.lyr`, eine Datei, die es nicht gibt).

| Option | Vorbild | Preis |
|---|---|---|
| **A: exakte Gleichheit verlangen** | **Java (nur für `public` Top-Level-Klassen, JLS §7.6 — nicht-öffentliche dürfen beliebig heißen; also halb passend)**, Go (Pfade sind exakt) | Plattformunabhängig. Ein Verzeichnislisting pro Auflösung oder ein Pfadvergleich; ein Diagnosecode. |
| **B: Segmente normalisieren** | — | `MyModule` = `mymodule`, `RES0007` unerreichbar. |
| **C: Diagnose reparieren** — den Pfad drucken, der geöffnet wurde | — | Behebt die Lüge, nicht den CI-Bruch. |
| **D: nichts** | — | Der nächste Linux-Umsteiger findet es. |

**Empfehlung: A, und C sofort.** **Bruch:** A minor. **4.x-Warnstufe:** 4.7 „resolved 'Lib' to
file 'lib.lyr'; this does not compile on a case-sensitive file system". **Abhängig von:** MOD-11,
MOD-42 (welche Bezeichner überhaupt Dateinamen sein dürfen). **Belegstufe:** gemessen.

---

### MOD-20 — Wem gehört eine Extension, und wie löst ein Benutzer einen Konflikt auf?

**Heute: niemandem, und gar nicht** (`r03`: `LYR-SEM0044`; `r03b`: selektiver Import ohne die
Extension bringt sie trotzdem mit, `04-modules.md:29`; `r03c`: einzeln geht es). Zwei
Bibliotheken, die je für sich funktionieren, sind zusammen unbenutzbar.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Kohärenz-/Orphan-Regel** | Rust | Verhindert den Konflikt an der Wurzel. Bricht `extend int` in Hilfsmodulen — stdlib und Bestandscode. |
| **B: `import e1 { ping } without extensions;`** | — | Vierte Importform. Löst nur, wenn der Aufrufer eine Extension nicht braucht. |
| **C: Disambiguierung am Aufrufort** (`e1.twice(3)`) | Rust UFCS | Neue Schreibweise. |
| **F: Extension ist ein importierbarer Name** — `import e1 { twice };` bringt genau diese Extension, `import e1 { twice as twiceA };` benennt sie um; der bare Import bringt weiterhin alle | **Kotlin** (Extension-Funktionen sind Top-Level-Deklarationen, namentlich importiert, `import b.twice as twiceB` löst genau den Konflikt zweier gleichnamiger Extensions) | Keine neue Klausel, eine Ausweitung dessen, was `{ … }` benennen darf, plus MOD-08/A. **Widerspricht `04-modules.md:29`** („the methods come with the module, not with a name") — der Satz muss zu „…or with a name" werden. Und: eine Extension ohne Import ihres Moduls bleibt unsichtbar (`p26`), nur die *Auswahl* wird feiner. |
| **D: Warnung, letzte gewinnt** | — | Still falsche Programme. |
| **E: nichts** | heutiger Zustand | Der erste Benutzer mit zwei fremden Bibliotheken findet es. |

*Korrektur gegenüber der zweiten Fassung:* dort stand bei B „Swift/Kotlin haben das Problem, keine
Lösung". Für Kotlin ist das falsch — Kotlin hat genau die Lösung, die Lyric fehlt, und sie ist
billiger als B.

**Empfehlung: F, zusammen mit MOD-08/A; A als Frage für die Bibliotheksrunde.** F ist die
Kotlin-Antwort und braucht keine vierte Importform; sie kostet einen Satz in §4.2 und die
Alias-Form aus MOD-08. B ist damit überflüssig. C bleibt die eleganteste und teuerste Antwort.
**Bruch:** F nein (additiv; der bare Import verhält sich wie heute), A major. **Abhängig von:**
MOD-08, MOD-07, MOD-21, MOD-38. **Belegstufe:** gemessen.

---

### MOD-21 — Was bedeutet `pub` an einer Extension-Methode, nachdem MOD-05 Membern Bedeutung gibt?

**Heute: nichts, an beiden Stellen** (`r13`: Extension ohne jedes `pub` von außen aufrufbar;
`r20/b`: `pub extend S { pub fn extra() }` identisch). Die stdlib schreibt durchgehend
`pub extend T { pub fn m() }`. Konflikt: MOD-15/A lehnt das Block-`pub` ab, MOD-05/A gibt dem
Member-`pub` Bedeutung.

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine Extension kommt mit ihrem Modul; `pub` ist an beiden Stellen verboten** | die heutige Spec (`04-modules.md:29`), Swift | Mechanisch migrierbar. Eine Extension kann nicht modulprivat sein. **Mit MOD-20/F** heißt „kommt mit dem Modul": bare Import bringt alle, selektiver Import die genannten. |
| **B: die Methode bekommt ihr eigenes `pub`** | Rust (`impl`-Methoden), C# | Erlaubt modulprivate Extension-Helfer (`r13` zeigt, dass heute jede exportiert wird). Vierte Sichtbarkeitsachse. |
| **C: erbt die Sichtbarkeit des erweiterten Typs** | Swift (teilweise) | `extend int` — welche Sichtbarkeit hat `int`? Und `r06`: fremde private Typen sind erweiterbar. |

**Empfehlung: A.** Der Fall „interner Helfer" löst sich mit einer freien modulprivaten Funktion.
**Bruch:** minor (mechanisch). **4.x-Warnstufe:** 4.7 mit MOD-15. **Abhängig von:** MOD-05,
MOD-15, MOD-20. **Belegstufe:** gemessen.

---

### MOD-22 — Wie verkleinert eine Bibliothek ihre öffentliche Fläche?

**Heute: gar nicht sanft.** `@Deprecated { message, until }` (`docs/guide/15-attributes.md:249-300`,
`stdlib/std/core.lyr:500-508`) kennt nur „verschwindet" (`LYR-SEM0081`); am Modulheader
funktioniert es (`k07`). Kein Vehikel für „bleibt und hört auf, `pub` zu sein".

| Option | Vorbild | Preis |
|---|---|---|
| **A: ein Feld am `@Deprecated`** (`becomes = "internal"`), das bis `until` außerhalb warnt und danach die Sichtbarkeit senkt | **Kotlin `@Deprecated(level = WARNING/ERROR/HIDDEN)` — nur als Vorbild für „Stufen am selben Attribut"**; *Korrektur:* Kotlins Stufen senken keine Sichtbarkeit (ERROR macht jede Benutzung zum Fehler, HIDDEN entfernt die Deklaration aus der Auflösung und bleibt binärkompatibel); ein „becomes = internal" hat in Kotlin **kein** Gegenstück | Ein Feld in einem Struct, das es gibt, ein Warnpfad, den es gibt. `@Deprecated` bekommt eine zweite Bedeutung — die Meldung muss beide trennen. **Und die Mechanik ist neu, nicht abgeschrieben**: keine der neun senkt Sichtbarkeit über ein Attribut. |
| **B: Entfernen und neu anlegen** | Rust, C# | Jede Verkleinerung wird zu einer Umbenennung. |
| **C: eigenes Attribut** (`@Narrowing`) | Java `@Deprecated(forRemoval)` | Rule 2. |
| **D: nichts** | — | Für den größten geplanten Bruch (MOD-05) kein Werkzeug. |

**Empfehlung: A — mit dem Eingeständnis, dass Lyric hier ohne Vorbild arbeitet.** Der Ertrag ist
das eigene Migrationswerkzeug für MOD-02/MOD-05.
**Bruch:** nein (additiv). **Abhängig von:** MOD-02, MOD-05, MOD-26 (das Wort im Feld), Gebiet
Attribute (§4.7-Wertregeln). **Belegstufe:** gemessen (Ist), behauptet (Kotlin-Detail aus
Erinnerung an die Kotlin-Doku, nicht nachgeschlagen).

---

### MOD-23 — Ist die Capability-Anforderung Teil des veröffentlichten Vertrags?

**Heute: sie leckt korrekt durch, aber unsichtbar** (`r12`/`r12k`; `04-modules.md:157-176`).
Vor dem Bau sagt nichts, dass eine Dependency `fileAccess` verlangt.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Deklaration in `lyric.json`, vom Compiler geprüft** | Java JPMS `requires`, Manifeste | Zweite Aussage neben dem Import — muss geprüft werden. |
| **B: diagnostizieren** — „dein Programm trägt `fileAccess`, weil `filelib` → `std.io.file`" | `cargo tree -i`, `go mod why` | Ein Werkzeug, kein Sprachmittel. |
| **C: ein `pub import` übernimmt die Anforderung ausdrücklich** | — | Pflichtaufgabe von MOD-07/B. |
| **D: nichts** | — | Ein Sandbox-Host kann nicht sagen, welche Abhängigkeit welche Erlaubnis zieht. |

**Empfehlung: B jetzt, C mit MOD-07, A mit dem Paketmanager.** **Bruch:** nein. **Abhängig von:**
MOD-07, MOD-14, Gebiete Toolchain, FFI. **Belegstufe:** gemessen.

---

### MOD-24 — Womit sieht ein Maintainer die öffentliche Fläche?

**Heute: mit nichts** (`q04k`/`r14`: `lyrvm info` zeigt die Funktionstabelle; kein `doc`, kein
`api`, kein `lyrfix`; kein Export-Begriff im Format). **Aber BP-09 hat eine Interface-Sektion
beschlossen** (`build-pakete.md:664-680`) — das Modul-Dossier hatte diesen Beschluss nicht
gekannt.

| Option | Vorbild | Preis |
|---|---|---|
| **A: ein Export-Abschnitt im `.lyrbc`** | Java `module-info`, .NET-Metadaten, Swift `.swiftinterface` — **und BP-09** | Formatänderung. Macht `pub` zur Formataussage und MOD-12/A formulierbar. |
| **B: `lyric api <dir>`** aus der Quelle | `cargo public-api` | Nur ein Werkzeug. **Preis, den die zweite Fassung nicht sah:** ohne die Interface-Sektion ist B eine *zweite* Definition von „Fläche" — genau das, was MOD-25 vermeidet. B muss dieselbe Funktion drucken, die A ins Format schreibt. |
| **C: API-Diff als CI-Gate** | `cargo semver-checks`, `japicmp` | Baut auf B auf. |
| **D: `lyric doc`** | `cargo doc`, `godoc` | Eigenes Projekt; `DocumentationTable` existiert. |

**Empfehlung: B zuerst, aber als Textform DERSELBEN Liste, die A/BP-09 ins Format schreibt;
dann C; D unabhängig.** *Korrektur:* „B ist ein Nachmittag Arbeit" ist gestrichen — unbelegt, und
das Dossier hat solche Zahlen sonst gestrichen. B beantwortet die Frage, an der MOD-02 hängt.
**Bruch:** B/C/D nein, A major (Format). **Abhängig von:** MOD-25, MOD-37, BP-09.
Voraussetzung für MOD-02, MOD-12. **Belegstufe:** gemessen (Ist), gelesen (BP-09).

---

### MOD-25 — Soll die Sichtbarkeitsregel EINE Implementierung haben?

**Heute: drei, und die maßgebliche ist die falsche** (Tabelle in 1.2). Der Editor zeigt die
Regel (`CompletionProvider.cs:73` → `MemberFacts.OfModule`), der Compiler setzt sie nicht
durch, niemand hält die Antworten gegeneinander. Mit MOD-24 käme ein vierter Leser, mit
`lyrfmt`/`lyrfix` ein fünfter, mit MOD-43 (Rename, Find-Usages) weitere.

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine Funktion `Sees(from, symbol)`, die alle benutzen, plus ein Vergleichstest Compiler/LSP** | Roslyn (`IsAccessibleWithin`), rustc (`tcx.visibility`) | Eine Refaktorierung quer durch Sema, LSP, `lyric api`. **Und die Stellenliste** (welche Positionen fragen `Sees`?) kommt aus dem Symbolmodell — das ist die einzige Art, die Betroffenenliste aus MOD-04 abzuschließen. |
| **B: jede Stelle behält ihre eigene** | heute | Der Befund aus MOD-04 passiert wieder. |
| **C: eine Funktion, kein Vergleichstest** | — | Die halbe Miete. |

**Empfehlung: A, als erster Schritt von MOD-04.** **Bruch:** nein. **Abhängig von:** nichts.
Voraussetzung für MOD-04, MOD-05, MOD-24, MOD-43. **Belegstufe:** gelesen.

---

### MOD-26 — Welche Schreibweise bekommt die zweite Stufe?

**Heute: keine** (`r17`: `pub(package)`, `package`, `internal` — dreimal `PAR0025`). `Grammar.md`
kennt nur Modifikatoren ohne Argument; `type`, `opaque`, `extern` sind Kontext-Keywords
(`Grammar.md:180-181`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: `pub(package)` / `pub(root)`** | Rust `pub(crate)` | Erste Modifikatorkategorie mit Argument; öffnet die Tür zu `pub(in a.b)`, die MOD-03/A zuschreiben muss. |
| **B: eigenes Wort `package`** | Swift (`package`, 5.9) | Kontext-Keyword. **Begriffskollision im Projekt:** `lyric pack`/`Lyrpack`/`docs/guide/17-packaging.md:1-12` („Packing a program" = eine ausführbare Datei), während `build-pakete.md` „Paket" für die Abhängigkeitseinheit benutzt. Zwei Bedeutungen für einen Stamm — Rule 2 im Geist. |
| **C: eigenes Wort `internal`** | C#, Kotlin | Kontext-Keyword, keine Kollision, den meisten Programmierern bekannt. Sagt nicht, *wovon* intern — das sagt die Spec in einem Satz („internal to the root"). |
| **D: Exportliste am Modul** | OCaml, JPMS | MOD-02/D. |

*Korrektur gegenüber der zweiten Fassung:* die Begründung „Swift hat `package` gewählt, gerade
weil geklammerte Modifikatoren dort fremd wirken" war erfunden — Swift hat `private(set)`,
`internal(set)`, `fileprivate(set)` seit Swift 1. SE-0386 hat `package` gewählt, weil Swifts
Stufen alle Wörter sind, nicht wegen fehlender Klammerform (behauptet — aus Erinnerung an
SE-0386, nicht nachgeschlagen).

**Empfehlung: C (`internal`), nicht mehr B.** Der Ausschlag ist die Kollision: solange
`lyric pack` ein Executable baut und die Bibliotheksrunde „Paket" für den Root benutzt, ist ein
drittes `package` das Wort, das am meisten erklären muss. `internal` erklärt sich über C# und
Kotlin selbst und hält MOD-03/A frei von `pub(in …)`. Sollte das Gebiet Toolchain `pack` umbenennen
und „Paket = Root" festschreiben, wird B wieder gleichwertig.
**Bruch:** nein (additiv, Kontext-Keyword). **Abhängig von:** MOD-02, MOD-03, Gebiet
Toolchain (Wortwahl `pack`/`package`). **Belegstufe:** gemessen (Ist), gelesen (Kollision).

---

### MOD-27 — Was tut die Migration, wenn die Reparatur in fremdem Code liegt?

**Heute: es gibt keine Migration** (kein `lyrfix`). Die MOD-04-Warnung erscheint am Aufrufort,
die Behebung an der Deklaration — in einer Dependency (`p37`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: `lyrfix` fasst nur den eigenen sourceRoot an** | `cargo fix`, `go fix` | Für den Dependency-Fall bleibt „melde es dem Autor". |
| **B: Notausgang** (`--allow-legacy-visibility` oder Attribut) | Rust `#[allow]`, C# `#pragma`, Java `--add-opens` | Lyric hat keinen Unterdrückungsmechanismus; einen einzuführen ist eine Diagnostik-Grundsatzfrage. |
| **C: Vendoring** | Gos `vendor/` | Für eine Sprache ohne Paketmanager fast der Ist-Zustand. |
| **D: nichts** | — | Unmaintainte Dependency = stehengeblieben ab 5.0. |

**Empfehlung: A für `lyrfix`, C dokumentiert, B ans Gebiet Diagnostik.** **Bruch:** nein.
**Abhängig von:** MOD-04, MOD-05, MOD-15, Gebiete Diagnostik, Toolchain. **Belegstufe:** gemessen.

---

### MOD-28 — Wo darf ein `import` stehen?

**Heute: irgendwo auf Top-Level** (`Grammar.md:164`; `r18/c` am Dateiende gilt für alles
darüber; `r18/b` im Rumpf ist `PAR0002`). Keine Reihenfolge, keine Gruppierung.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Importe vor der ersten Deklaration** | Go (Fehler), Java, Python (Konvention) | Bricht Dateien, die es anders machen (ob jemand es tut: ungemessen). |
| **B: `lyrfmt` sortiert und gruppiert** | `rustfmt`, `goimports` | Kein Bruch. **Mit MOD-38:** `lyrfmt` muss zwei Importe desselben Moduls sortieren oder zusammenfassen — und Zusammenfassen ändert Bedeutung nicht (`q16b`), aber Lesbarkeit. |
| **C: blocklokale Importe** | Rust, Python | Rule 2, eine Auflösungsregel mehr. |
| **D: nichts** | heute | Die Datei, die man zweimal lesen muss. |

**Empfehlung: A und B** — ein Mechanismus. **Bruch:** A minor. **4.x-Warnstufe:** 4.7, 5.0
Fehler. **Abhängig von:** MOD-30, MOD-38, Gebiet Editor-Werkzeuge. **Belegstufe:** gemessen.

---

### MOD-29 — Gibt es eine Diagnose für einen ungebundenen Wurzelnamen?

**Heute: nein** (`r10`: `unknown identifier 'std'`; `import std;` ist `RES0003` ohne Hinweis).
Tippfehler **innerhalb** eines Pfads sind MOD-36.

| Option | Vorbild | Preis |
|---|---|---|
| **A: die Ablehnung erklärt den Weg** — „'std' is a root, not a module — write `import std.io.console { println };`" | **Rust** („help: consider importing this function"). *Korrektur:* **nicht Go** — der Go-Compiler (gc/types2) meldet nur `undefined: fmt`; der Importvorschlag kommt von gopls/goimports (behauptet — aus dem Kritikbeleg, nicht selbst nachgeschlagen) | Rein diagnostisch. |
| **B: voll qualifizierte Pfade erlauben** | Java, C# | MOD-08/C, Rule 2. |
| **C: nur den Root benennen** | — | Die halbe Hilfe. |
| **D: nichts** | heute | Die unbrauchbarste Antwort für einen Umsteiger. |

**Empfehlung: A.** **Bruch:** nein. **Abhängig von:** MOD-08, MOD-03, MOD-36. **Belegstufe:**
gemessen.

---

### MOD-30 — Darf ein lokaler Name eine Modulbindung verdecken?

**Heute: lokal ja, wortlos; auf Top-Level nein.** *Korrektur gegenüber der zweiten Fassung
(„`let lib = 5;` neben `import lib;` kompiliert wortlos"):* gemessen (`q05a`) gilt das nur für
eine **lokale** Bindung (Exit 5, keine Diagnose); ein **Top-Level** `let lib = 5;` ist
`LYR-RES0001: 'lib' is already declared in this module` (`q05b`). Lyric entscheidet den Fall
also bereits an einer der beiden Stellen; die Asymmetrie zu `LYR-SEM0077` (`r11k`, ein Import,
der `string` verdeckt, warnt) ist kleiner als dargestellt — sie betrifft nur Locals.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Warnung, im Wortlaut von `LYR-SEM0077`** | Rust (`unused_imports` schlägt oft mit zu) | Symmetrisch. Der Fall „Variable heißt absichtlich wie das Modul" wird lästig — dafür `as`. |
| **B: Fehler, wie auf Top-Level** | **C# CS0136** (*Korrektur:* ein **Fehler**, keine Warnung, und für lokale Scopes — gehört hierher, nicht unter A), Java (Typname bleibt erreichbar) | Konsistent mit `q05b`, bricht Programme, die heute legal sind. |
| **C: bewusst erlauben, hinschreiben** | Python, JavaScript | Die Asymmetrie zu `SEM0077` und zu `q05b` muss begründet werden. |
| **D: eigener Namensraum für Module** | OCaml | MOD-09/C. |

**Empfehlung: A.** Ein Warncode, derselbe Ausweg (`as`), kein Bruch — und die Meldung sollte
sagen, dass dasselbe auf Top-Level ein Fehler ist.
**Bruch:** nein. **Abhängig von:** MOD-09, MOD-35 (dieselbe Frage für Builtin-Namen).
**Belegstufe:** gemessen.

---

### MOD-31 — Ist der Modulpfad im Bytecode-Namensschema eindeutig? *(neu)*

**Heute: nein.** Gemessen (`q07`): Modul `a` mit `pub struct b { pub fn c() }` neben Modul `a.b`
mit `pub fn c()` — beides legal, beide importierbar — endet in
`error[LYR-CLI0020]: ir-verifier … a.b.c: duplicate function name — this is a defect in the
compiler`. Kontrolle (`q07k`, Struct heißt `bb`): druckt `1 2`. Gelesen:
`NameMangling.cs:21-22` (`ForFunction` = `<module>.<fn>`), `:35-37` (`ForMethod` =
`<module>.<type>.<method>`), `:47-48` (`ForExtension` = `<module>.<extend>.<target>.<method>`) —
der Punkt ist Modultrenner **und** Membertrenner; der Kommentar (`:9-11`) hält den Modulpfad für
ausreichend eindeutig. Die Host-API ruft nach genau diesen Namen (`docs/guide/14-embedding.md:29-34`,
`04-modules.md`, §4.3: „a host … settles by passing the full name").

| Option | Vorbild | Preis |
|---|---|---|
| **A: anderes Trennzeichen zwischen Modulpfad und Deklaration** (`a.b::c` oder `a.b/c`) | JVM (`/` zwischen Package-Segmenten, `.` nie im Binärnamen), .NET (`Namespace.Type::Method` im IL) | Löst es vollständig. **Formatfrage**: jeder mangled Name ändert sich, `Call("a.b.c")` im Host bricht, `lyrvm info`/Disassembly lesen anders. Major am Format. |
| **B: die Sprache verbietet ein Modul `a.b` neben einem Typ `b` in `a`** | keine — Java/Kotlin *erlauben* Klasse und Sub-Package gleichen Namens und lösen es kontextuell auf | Kein Formatbruch. Eine neue Regel mit einem Diagnosecode, die der Resolver prüfen kann (er kennt beide Namen). Preis: eine Bibliothek kann `shapes.lyr` mit `struct circle` nicht neben `shapes/circle.lyr` halten — selten, aber genau das Muster „Typ und sein Untermodul". |
| **C: nur Typ-Escaping** — Typnamen im Mangling bekommen die spitzen Klammern wie `<extend>`/`<globals>` (`a.<b>.c`) | Lyrics eigene Konvention (`NameMangling.cs:52-54`: „an identifier cannot contain them") | Nur Methodennamen ändern sich; freie Funktionen behalten `main.add`. Host-Aufrufe auf Methoden gibt es heute nicht (Member sind keine Wurzeln, MOD-12), also bricht kein Host. |
| **D: nichts** | — | Ein legales Programm bekommt „Defekt im Compiler". |

**Empfehlung: C sofort (kein Host-Bruch, weil Methoden heute nicht aufrufbar sind), B als
Spec-Satz dazu, A nicht.** C benutzt die Konvention, die das Mangling schon für `<extend>` hat;
sobald MOD-12/A Member zu Wurzeln macht, ist der Name ohnehin neu. Die Instanzfrage (`q13b`)
ist MOD-44 und braucht dieselbe Stelle.
**Bruch:** C nein am Host (heute), minor an Disassembly/`lyrvm info`; A major. **Abhängig von:**
MOD-03, MOD-12, MOD-44, Gebiet Bytecode. **Belegstufe:** gemessen.

---

### MOD-32 — Ist ein Modul durch seinen Namen oder durch seine Datei identifiziert? *(neu)*

**Heute: durch den Namen — dieselbe Datei darf zweimal Modul sein.** Gemessen (`q08`): Entry
`app.lyr` (Modul `main`) wird von `b.lyr` per `import app;` ein zweites Mal als `app` geladen;
Ergebnis `LYR-RES0005 … involving module 'b'` (der echte Zyklus wäre app↔b) plus
`LYR-SEM0021: duplicate 'main' function` an `app.lyr:3:1` ohne Note. `q08c` (`import app;` in
`app.lyr` selbst): dasselbe mit `'app'`. Kontrolle `q08k` (`main.lyr`): nur `RES0005`, weil
`FindModule(["main"])` den Entry findet. Gelesen: `Compilation.cs:165-181` — der Loader fragt
`FindModule(import.Path)` nach dem **Namen** und lädt sonst; einen Pfadvergleich gibt es nicht.
`AddModule` (`:113-119`, `RES0007`) fängt nur zwei Dateien mit *einem* Namen, nicht eine Datei
mit *zwei* Namen.

| Option | Vorbild | Preis |
|---|---|---|
| **A: der Dateiname entscheidet — dann kann es die Doppelladung nicht geben** (= MOD-11/A) | Java, Go, Lyrics eigener Embedding-Pfad (`LangVm.cs:252-254`) | Löst die Ursache. Preis: MOD-11/A samt Bytecode-Namensbruch (MOD-45). |
| **B: Dedupe nach Dateipfad im Loader, Fehler mit beiden Namen** (= MOD-11/E) | Linker | Löst die Wirkung, ohne die Namensregel zu entscheiden. Ein normalisierter Pfadvergleich, ein Diagnosecode. |
| **C: nichts** | — | „duplicate main" an einer Zeile ohne zweites Vorkommen bleibt. |

**Empfehlung: B jetzt, A in 5.0.** Und das ist der eigentliche Grund für MOD-11/A: nicht
Konsistenz mit der Spec, sondern dass ein Modul heute keine Identität hat, die der Loader prüfen
könnte.
**Bruch:** B nein. **Abhängig von:** MOD-11, MOD-40. **Belegstufe:** gemessen.

---

### MOD-33 — Ist `main` ein Modul- oder ein Programmname? *(neu)*

**Heute: beides, und das kollidiert.** Gemessen (`q09`): ein importiertes `lib.lyr` mit eigener
`fn main()` macht das Programm zu `LYR-SEM0021: duplicate 'main' function` an `lib.lyr:1:1`,
ohne Note auf die Entry-`main` und ohne den Satz „nur die Entry-Datei darf `main` haben". Gelesen:
`SemaRules.cs:103-118` sammelt `main` über **alle** Module und meldet ab dem zweiten — außer im
Workspace-Modus (`_singleProgram = false`, `SourceCompiler.cs:371`: „two roots may both declare
'main'"). Ein Modul kann also nicht zugleich Skript (`lyric run lib.lyr`) und Bibliothek sein.
Dazu: ohne Header heißt jede Entry-Datei `main` (MOD-11), also ist `main` **auch** ein Modulname.

| Option | Vorbild | Preis |
|---|---|---|
| **A: `main` gehört nur der Entry-Datei; eine `main` in einem importierten Modul ist ein Fehler mit genau diesem Satz** | Rust (`main` nur in der Binary-Crate-Wurzel), Go (`func main` nur in `package main`) | Heutiges Verhalten, ehrlich benannt: Meldung mit Note auf die Entry-`main`. Preis: kein Modul ist zugleich Skript und Bibliothek. |
| **B: `main` eines importierten Moduls wird ignoriert** | Python (`if __name__ == "__main__"`) | Ein Modul darf beides sein. Preis: eine Funktion, die je nach Rolle Wurzel oder toter Code ist; die Reachability (§4.6) muss die Rolle kennen; `lyric test` (jede Testdatei ist Entry) und der Workspace-Modus haben die Regel schon halb. |
| **C: `main` als Modulname abschaffen** — eine headerlose Entry-Datei heißt nach ihrer Datei (MOD-11/A) | — | Trennt Modulname und Programmname. Ist MOD-11/A. |

**Empfehlung: A als Diagnose sofort, C mit MOD-11/A.** B ist attraktiv für Skript-Bibliotheken,
aber es ist eine zweite Wurzelregel, und die Testkompilation zeigt, dass Lyric die Rolle
„Entry" heute an der Kompilation festmacht, nicht an der Datei — das sollte so bleiben.
**Bruch:** A nein (Diagnose), C = MOD-11/A. **Abhängig von:** MOD-11, MOD-41, Gebiet Testen.
**Belegstufe:** gemessen.

---

### MOD-34 — Woher bekommt eine Bibliothek ihren Modulnamen, wenn sie ohne Header gebaut wird? *(neu)*

**Heute: sie heißt `main`.** Gemessen (`q04`): `lyrc build api.lyr` ohne Header → `lyrvm info`
zeigt `main.helper`, `main.newBox`, `entry (library — no start section)`; mit `module api;`
(`q04k`) `api.helper`, `api.newBox`. *Korrektur gegenüber der zweiten Fassung:* die Aussage
„enthält nach dem Bau `api.newBox` und `api.helper`" galt nur, weil `r14` einen Header trug. Ein
Host, der `api.newBox` nach Namen ruft, findet in einer headerlosen Bibliothek nichts; und zwei
headerlose Bibliotheken heißen **beide** `main`. Die Host-API verhält sich anders:
`Compile(source, moduleName)` verlangt den Namen (`docs/guide/14-embedding.md:26`,
`LangVm.cs:240-245`), `CompileFile` nimmt den Dateinamen (`:252-254`).

| Option | Vorbild | Preis |
|---|---|---|
| **A: Header ist Pflicht für einen Bibliotheksbau** (eine Datei ohne `main` und ohne Header ist ein Fehler: „a library needs a name; add `module api;` or name the file") | Rust (`[lib] name`), .NET (Assembly-Name) | Keine stille `main`-Bibliothek mehr. Preis: ein Fehler für einen Bau, der heute gelingt. |
| **B: der Dateiname entscheidet** (= MOD-11/A) | Go, Java, Lyrics `CompileFile` | Konsistent über CLI und Embedding. Preis: MOD-45. |
| **C: `lyric.json` benennt die Bibliothek** (`name`-Feld, BP-03) | Cargo, npm | Nur mit Manifest; die manifestlose Bibliothek bleibt `main`. |
| **D: nichts** | — | Zwei Bibliotheken namens `main`. |

**Empfehlung: B, mit A als Übergangsdiagnose in 4.7** („this library is built as module 'main';
from 5.0 it is 'api'"). Das ist dieselbe Uhr wie MOD-11/A und MOD-45.
**Bruch:** B major am Host-Rand. **Abhängig von:** MOD-11, MOD-12, MOD-45, BP-03.
**Belegstufe:** gemessen.

---

### MOD-35 — Welche Namen sind implizit gebunden, und darf ein Benutzer sie verdecken? *(neu)*

**Heute: `panic` ist da, und jeder darf es still verdecken.** Gemessen: `q03` (`panic` ohne
Import, Exit 3), `q03b` (Benutzer-`fn panic(msg: string): int` im Entry verdeckt den Builtin
**wortlos**, druckt 9), `q03c` (`import mylib { panic };` verdeckt ihn **wortlos**, druckt 9),
`q03d` (ein Benutzer-`concat` erreicht den Operator **nicht**: `"a" + "b"` bleibt `ab`,
`f"{1}-{2}"` bleibt `1-2`). Gelesen: `panic` ist ein Symbol der Builtins-Tabelle
(`BuiltinTypes.cs:62-68`), Parent jeder Modultabelle (`Compilation.cs:122`) — eine
Moduldeklaration gewinnt per Lookup-Kette; `concat`/`repeat`/`fromXxx`/`rawToChars` werden im
Lowerer per festem Importnamen gebunden (`FunctionLowerer.cs:1871-1872`, `:1300`, `:4805-4809`),
nie per Namenssuche — sie sind **nicht** verdeckbar, aber per Header-Hijack (`r08`,
`module std.string;`) ersetzbar. Die Builtin-**Typnamen** (`int`, `string`, …) haben eine
Verdeckungswarnung (`LYR-SEM0077`, `r11k`), der Builtin-**Funktionsname** `panic` nicht.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Verdecken erlaubt, aber es warnt** — dieselbe Warnung wie `SEM0077`, für Deklaration und Import | Rust (`panic!` ist ein Makro, Schatten warnt nicht — kein Vorbild), **Go** (`vet` meldet Verschattung von Builtins wie `len`, der Compiler nicht), Python (still) | Symmetrisch zu `SEM0077`; ein Warncode. |
| **B: Verdecken ist ein Fehler** | Kotlin (kein Verdecken von `kotlin.*`-Top-Level? — **nein**, Kotlin erlaubt es; kein Vorbild) | Bricht jedes Programm mit einer eigenen `panic`. Ungemessen, ob es solche gibt. |
| **C: Verdecken erlaubt, still, und die Spec sagt es** | Python | Die Asymmetrie zu `SEM0077` bleibt. |
| **D: `panic` wird aus den Builtins genommen und importiert wie alles andere** | Zig (`@panic` ist ein Builtin mit `@`, unverdeckbar), Go (`panic` ist ein Builtin, verdeckbar) | Ehrlichste Form: keine implizite Namensmenge außer den Typen. Bricht jedes Programm, das `panic` ohne Import benutzt — die Spec sagt heute, das sei erlaubt (§4.4). |

**Empfehlung: A, und die Bindungsliste aus §4.4 bekommt den Satz „a module declaration or a
selective import of one of these names shadows the compiler binding and warns".** Die
Helfer-Namen (`concat` usw.) gehören ausdrücklich als *unverdeckbar* in denselben Absatz, mit
dem Hinweis, dass nur ein Header `module std.string;` sie ersetzt — und dass MOD-11/B genau das
verbietet. Das ist die Voraussetzung, die MOD-18/A übersprungen hatte.
**Bruch:** nein (Warnung). **Abhängig von:** MOD-18, MOD-11, MOD-30. **Belegstufe:** gemessen.

---

### MOD-36 — Bekommt ein falscher Modulpfad ein „did you mean"? *(neu)*

**Heute: nein.** Gemessen (`q10`): `import std.io.consol { println };` ist ein nacktes
`LYR-RES0003: cannot find module 'std.io.consol'`, obwohl `std.io.console` existiert. Kontrolle
(`q10k`): `unknown identifier 'g'` neben `fn f` bekommt `note: did you mean 'f'?` — der
Mechanismus existiert (`src/Lyric.Frontend/Sema/NameSuggestion.cs:6-16`), der Resolver
(`Resolver.cs:197`) benutzt ihn nicht. MOD-29 behandelt nur den ungebundenen **Root**-Namen.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Levenshtein über die Kandidaten des betroffenen Roots** — für `std.io.consol`: die Dateien unter `std/io/`; für ein Projektsegment: die Dateien unter dem sourceRoot; für ein unbekanntes erstes Segment: die Roots | Rust (`unresolved import … help: a similar path exists`), Python 3.12 („Did you mean: 'console'?" bei Modulen) | Ein Verzeichnislisting pro fehlgeschlagener Auflösung (nur im Fehlerfall). Der Kandidatenraum muss je Root definiert sein: Projekt, std, native (**dort gibt es keine Dateien**, nur registrierte Namen), Dependencies. |
| **B: nur den Root nennen** („'std.io.consol' is not in the standard library") | — | Halbe Hilfe. |
| **C: nichts** | — | Der Tippfehler bleibt ein Rätsel. |

**Empfehlung: A, mit derselben Funktion wie `NameSuggestion`.** Für native Roots ist der
Kandidatenraum die Namensliste, die der Host registriert hat.
**Bruch:** nein. **Abhängig von:** MOD-29, Gebiet Diagnostik. **Belegstufe:** gemessen.

---

### MOD-37 — Was kostet das Modulsystem pro Kompilation, und gibt es getrennte Übersetzung? *(neu)*

**Heute: alles wird bei jedem Bau geparst und geprüft, es gibt keine Modulschnittstelle.**
Gemessen (`q11`, Hello World, Debug-Build): `load std.string, std.core, std.iter, … 92.5 ms`,
`resolve 8 modules 26.7 ms`, `check 8 modules 217.4 ms`, `lower 3 functions 283.6 ms`, total
959.6 ms — für ein Programm mit einer Zeile werden fünf stdlib-Module (die Ladeliste aus MOD-18)
plus `std.io.console` und Abhängigkeiten geladen und typgeprüft. (Der Kritiker maß 348 ms gesamt
mit denselben Proportionen; absolute Zahlen sind Debug-Rauschen, die Proportion nicht.)
Gelesen: `SourceCompiler.cs:85-100` misst den Loader mit, cached aber nichts; BP-09
(`build-pakete.md:664-680`) hat für Stufe B beschlossen: „`.lyrbc`-Bibliothek + Header in
`api/`, Sektion Interface (Format 4.1), generische Teile als Quelle daneben".

**Das ist die Stelle, an der dieses Gebiet und die Bibliotheksrunde dieselbe Sache dreimal
benennen:** `pub` (Sprache), die Interface-Sektion (Format), `lyric api` (MOD-24/B). Wer sie
nicht als *eine* Liste mit drei Ausgaben definiert, hat die Fläche an drei Orten.

| Option | Vorbild | Preis |
|---|---|---|
| **A: die Interface-Sektion ist die serialisierte Antwort von `Sees`/`pub` — erzeugt, nie geschrieben; `lyric api` druckt sie; der Compiler liest sie für eine kompilierte Abhängigkeit statt deren Quelle** | Rust (`.rmeta`), OCaml (`.cmi` aus der `.mli` bzw. inferiert), Swift `.swiftmodule` | Ein Begriff, drei Formen. Preis: das Format bekommt die Sichtbarkeitsstufen (MOD-02: `pub`/`internal`/privat — **`internal` ist innerhalb eines Pakets sichtbar, muss also in der Sektion stehen, für Fremde aber unsichtbar sein**), und die generische Hälfte bleibt Quelle (BP-09). |
| **B: nur ein Cache geparster/geprüfter stdlib-Module ohne Formatänderung** | Python `.pyc`, Zig Cache | Spart die 92 + 217 ms, ändert keine Semantik. Preis: Cache-Invalidierung, und die Kapselungsfrage bleibt offen. |
| **C: nichts** | — | Jeder Bau prüft die stdlib neu; ab der ersten kompilierten Bibliothek (BP-09) muss die Sektion dann doch definiert werden — ohne Bezug zu `pub`. |

**Empfehlung: A, als Bedingung an BP-09 formuliert: keine Interface-Sektion, die nicht aus
`pub`/`internal` erzeugt ist.** B ist ein Nebenprodukt von A. Die Performanzfrage ist real, aber
das Argument ist die *eine Definition*, nicht die Millisekunden.
**Bruch:** Format major (BP-09 ohnehin). **Abhängig von:** MOD-02, MOD-24, MOD-25, MOD-12,
BP-09, Gebiet Bytecode. **Belegstufe:** gemessen (Zeiten), gelesen (BP-09).

---

### MOD-38 — Dürfen die drei Importformen für DASSELBE Modul kombiniert werden, und was ist dann „benutzt"? *(neu)*

**Heute: ja, alle drei, und die Spec sagt nichts.** Gemessen (`q12`): `import lib;` +
`import lib { f };` kompiliert (13); `q16`: zwei selektive Importe desselben Moduls kompilieren;
`q16b`: bare + `as` + selektiv nebeneinander, alle benutzbar (20). `p15` (`RES0001`) gilt nur
für die **identische** Form. Benutzt-Zählung (`q12b`, `q12c`): der bare Import warnt nie, der
`as`-Import warnt, wenn nur `f` benutzt wird — also zählt jede Bindung für sich, das *Modul* hat
keinen Benutzt-Status. `lyric-spec/spec/04-modules.md:22-33` regelt weder Kombination noch
Zählung.

| Option | Vorbild | Preis |
|---|---|---|
| **A: erlaubt, normativ, jede Bindung zählt für sich; `lyrfmt` fasst zusammen, wo es bedeutungsgleich ist** | Rust (`use a; use a::f;` ist erlaubt; `rustfmt` fasst zu `use a::{self, f}` zusammen), Python | Ein Spec-Satz; `lyrfmt` bekommt eine Regel (MOD-28/B). Lyric hat keine `{self, f}`-Form — Zusammenfassen heißt nur „sortieren, nebeneinander". |
| **B: ein Modul, ein Import** — die zweite Form ist `RES0001` | Go (ein Package einmal je Datei, sonst Fehler) | Ehrlich und einfach. Bricht `q12`/`q16b`-Programme (ungemessen, ob es welche gibt). Und MOD-20/F (Extensions namentlich) will genau `import e1; import e1 { twice as t };`. |
| **C: erlaubt, aber ein ungenutzter Teil warnt** — mit MOD-13/A zählt auch der bare Import | Rust `unused_imports` | Folgt aus MOD-13. |

**Empfehlung: A plus C.** Kombination bleibt erlaubt (MOD-20/F braucht sie), wird normativ,
und mit MOD-13/A warnt jede ungenutzte Bindung, auch die bare.
**Bruch:** nein. **Abhängig von:** MOD-13, MOD-20, MOD-28. **Belegstufe:** gemessen.

---

### MOD-39 — Wie geht die Sichtbarkeit mit der Embedding-API um? *(neu)*

**Heute: der Host gewinnt still, und der Host darf `std.core` sein.** Gelesen (nicht gemessen —
dafür bräuchte es einen Host-Bau, den dieses Dossier nicht macht): `LangVm.Compile(source,
moduleName)` (`LangVm.cs:240-245`) → `Build(ScriptSource.FromText(moduleName, …), moduleName)`
→ `SourceCompiler.Compile` → `AddModule(entry, source.ModuleName)` (`SourceCompiler.cs:103`) →
`Compilation.cs:105`: `name is not null ? name.Split('.') : header …`. **Ein Header in der
Host-Quelle, der dem übergebenen Namen widerspricht, wird ohne Diagnose ignoriert** —
`RES0006` gibt es nur im Import-Loader (`:174-177`). Und ein Host, der `Compile(src, "std.core")`
sagt, oder eine Host-Quelle mit `module std.core;` ohne Namen, verdrängt die Standardbibliothek
wie in `r08` — jetzt in der Sandbox, wo `panic` (`std.core`) und `concat` (`std.string`) per
festem Namen gebunden werden (MOD-35). MOD-11 stellt die Frage nur für die CLI.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Header und Parameter müssen übereinstimmen** (`RES0006` auch hier), und ein Host-Name unter einem fremden Root ist ein Fehler (= MOD-11/B im Embedding) | .NET (`AssemblyName` vs. Manifest — Mismatch ist ein Ladefehler) | Zwei Prüfungen an einer Stelle, die es schon gibt. Bricht Hosts, die heute einen Header mitgeben und einen anderen Namen übergeben (behauptet: keiner bekannt; Erato 2 ist C++ und lädt Bytecode, keine Quelle). |
| **B: der Parameter gewinnt, und die Doku sagt es** | heute | Ein Satz in `14-embedding.md`. Der `std.*`-Hijack bleibt. |
| **C: Header in Host-Quellen verbieten** | — | Zu hart: ein Host, der Dateien aus einem Projekt lädt, bekommt Header mit. |

**Empfehlung: A** — dieselbe Regel wie MOD-11/B, an der Stelle, die beide Pfade teilen
(`Compilation.AddModule`), nicht in `LangVm`.
**Bruch:** nein für bekannte Hosts (behauptet). **Abhängig von:** MOD-11, MOD-35, Gebiet
FFI/Embedding. **Belegstufe:** gelesen.

---

### MOD-40 — Welche Diagnose bekommt ein Zyklus, der durch die Entry-Datei läuft? *(neu)*

**Heute: die falsche.** Gemessen (`q08`): Entry `app.lyr` importiert `b`, `b` importiert `app`
— die Meldung sagt `import cycle involving module 'b'` an `app.lyr:1:1`; der Zyklus, den der
Benutzer geschrieben hat, ist app↔b, aber im Graphen steht die Datei zweimal (`main` und `app`),
und die DFS (`Resolver.cs:257-280`) findet die Rückkante an einem anderen Knoten. `q08c`
(Selbstimport über den Dateinamen): `'app'`. `r07` (a→b→c→a): ein Diagnostikum ohne Pfad.
Kontrolle `q08k`: mit `main.lyr` stimmt die Meldung. MOD-10 fragt nur nach dem Pfad, nicht nach
dem doppelten Knoten.

| Option | Vorbild | Preis |
|---|---|---|
| **A: den Pfad drucken** (`a → b → c → a`) und **die Datei je Knoten** — dann sieht der Benutzer, dass `app.lyr` zweimal vorkommt | Rust (`error[E0391]: cycle detected … which again requires …`), Go (`import cycle not allowed: a imports b imports a`) | Ein Stack in der DFS. Kein Bruch. Ohne MOD-32/B bleibt der Doppelknoten, wird aber sichtbar. |
| **B: MOD-32/B zuerst — dann gibt es den Doppelknoten nicht, und der Pfad reicht** | — | Die richtige Reihenfolge. |
| **C: nichts** | — | „involving module 'b'" für einen Zyklus, den `b` nicht verursacht hat. |

**Empfehlung: B, dann A.** **Bruch:** nein. **Abhängig von:** MOD-10, MOD-32. **Belegstufe:**
gemessen.

---

### MOD-41 — Was ist die Kapselungseinheit für den Testroot? *(neu)*

**Heute: der Test ist ein Fremder mit Sonderrechten, die aus einem Loch stammen.** Gemessen
(`q14`): `lyric test` kompiliert `tests/calc_test.lyr` getrennt, mit `src/` als Root; der
Test erreicht `calc.internalDouble` nur über MOD-04; `lyric check .` sagt `2 modules ok`
(Workspace: beide Dateien zusammen). Gelesen: `Lyrtest/Program.cs:100-108` (Dateien unter
`testRoot`), `:150` (`vm.CompileFile(file)` je Datei, `SourceRoot = project?.SourceRoot`);
`LangVm.CompileFile` (`:252-254`) nennt das Modul nach der Datei (`calc_test`). MOD-17/A („Root
= Verzeichnis der Entry-Datei") gilt ausdrücklich nur ohne Manifest; **mit** Manifest ist der
Root `src/`, und der Entry der Testkompilation liegt **außerhalb** des Roots. MOD-16/B („der
Testroot gehört demselben Root") ist damit heute nicht wahr und braucht eine Regel.

| Option | Vorbild | Preis |
|---|---|---|
| **A: `lyric.json` definiert EIN Paket über `sourceRoot` UND `testRoot`; die Root-Stufe (`internal`) gilt in beiden; `pub` gilt für Dritte** | Go (`_test.go` im Package), Kotlin (Test-Source-Set sieht `internal`), C# (`InternalsVisibleTo` — dort explizit, hier implizit) | Ein Satz im Manifestkapitel und eine Prüfung: „gehört der Entry zum Paket?" = liegt er unter `sourceRoot` oder `testRoot`. Preis: ein Test sieht `internal`, nicht Modulprivates (MOD-16/B-Grenze). |
| **B: der Testroot ist ein eigenes Paket** (BP-25/D: eigenes `lyric.json`) | Go (`package foo_test` als externes Testpaket) | Ehrlich Fremder — dann sieht ein Test nur `pub`, und Weißbox ist tot. Widerspricht MOD-16/B. |
| **C: `@testable import`** (MOD-16/A) | Swift | Die Stufe wird am Import aufgehoben, das Paket muss nicht definiert sein. Vierte Importform. |

**Empfehlung: A** — und der Satz gehört in dieselbe Spec-Änderung wie MOD-02/A, weil ohne ihn
MOD-16/B und MOD-17/A einander widersprechen.
**Bruch:** nein (mit MOD-04 zusammen). **Abhängig von:** MOD-02, MOD-16, MOD-17, BP-25, Gebiet
Testen. **Belegstufe:** gemessen.

---

### MOD-42 — Sind Windows-Gerätenamen als Modulsegmente ein Problem? *(neu)*

**Heute: unbekannt, und bewusst nicht gemessen.** `import con;`, `import nul;`, `import aux;`,
`import prn;`, `import com1;` bilden auf `con.lyr` usw. ab; auf Windows können solche Pfade auf
das Gerät zeigen (das Öffnen von `con.lyr` kann blockieren oder von der Konsole lesen). Ich habe
die Probe nicht gefahren, weil ein hängender Compiler im Maintainer-Checkout ein Risiko ist.
Gelesen: `docs/Grammar.md:139` erlaubt jeden `IDENTIFIER` als Segment; nichts im Loader
(`Compilation.cs:165-181`) oder in `SourceCompiler.BuildModuleLoader` filtert Segmente. MOD-19
behandelt nur Groß-/Kleinschreibung.

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine Liste reservierter Segmente, die auf keiner Plattform Dateinamen sein dürfen** (`con`, `prn`, `aux`, `nul`, `com1–9`, `lpt1–9`), Fehler beim Import und beim Header | Rust/Cargo (`cargo new con` wird abgelehnt: „cannot be used as a package name … reserved on Windows"), npm (`validate-npm-package-name`) | Zehn Zeilen und ein Diagnosecode. Preis: ein Modul darf nicht `con` heißen — auf Linux wäre es gegangen. |
| **B: plattformabhängig lassen** | — | Ein Projekt, das auf Linux `aux.lyr` anlegt, hängt oder scheitert auf Windows. Das Projekt entwickelt auf Windows und fährt CI auf Linux — der Fall kommt in der anderen Richtung. |
| **C: Dateinamen mit `\\?\`-Präfix öffnen** (Windows-Umgehung) | — | Pflaster im Loader; das Modul heißt trotzdem wie ein Gerät. |

**Empfehlung: A, mit MOD-19/A in einer Runde** („welche Bezeichner sind auf allen Plattformen
sichere Dateinamen": Schreibweise und Reservierungen).
**Bruch:** nein (kein bekanntes Programm; behauptet). **Abhängig von:** MOD-19. **Belegstufe:**
behauptet (nicht gemessen, bewusst).

---

### MOD-43 — Welche Werkzeugfragen außer `lyric api` stellt das Gebiet? *(neu)*

**Heute: der Editor beantwortet Sichtbarkeit anders als der Compiler, und die übrigen
Werkzeugfragen sind nicht gestellt.** Gelesen: `MemberFacts.OfModule` (`MemberFacts.cs:111-134`)
filtert nach Sichtbarkeit und hat genau einen Aufrufer (`CompletionProvider.cs:73`) — die
Vervollständigung zeigt `lib.hidden` **nicht**, der Compiler nimmt es. Für Rename eines
`pub`-Symbols über Modul- und Root-Grenzen, Go-to-Definition in eine Dependency-Root, Find-Usages
über den Root und „add import for `println`" (Import-Vervollständigung) habe ich im LSP-Baum
keine Stelle gefunden, die Sichtbarkeit oder Roots kennt (behauptet — Grep nach `Visibility` in
`src/Lyric.Lsp/` liefert nur die Completion; nicht als Feature-Inventar verifiziert). MOD-25
nennt nur die Completion.

| Option | Vorbild | Preis |
|---|---|---|
| **A: eine `Sees`-Funktion (MOD-25) und ein Root-Modell im LSP; Rename/Find-Usages/Go-to-Definition fragen beides** | Roslyn (Workspace + `IsAccessibleWithin`), rust-analyzer (Crate-Graph) | Folgt aus MOD-25 und MOD-17/MOD-41: der LSP muss wissen, welches Paket eine Datei ist. Preis: ein Root-Modell im LSP, das heute nur `lyric.json` liest. |
| **B: Import-Vervollständigung** („add `import std.io.console { println };`") | rust-analyzer, gopls (`goimports`) | Braucht einen Index aller `pub`-Namen je Root — genau die Liste aus MOD-24/MOD-37. |
| **C: Rename über Root-Grenzen verweigern** (ein `pub`-Symbol einer Dependency ist nicht deins) | `cargo fix`-Regel (MOD-27/A) | Die sichere Regel für Werkzeuge, die Dateien schreiben. |
| **D: nichts** | — | Der Editor lügt weiter in die eine Richtung (zeigt weniger, als geht), und nach MOD-04 in die andere (Rename über eine Grenze, die es dann gibt). |

**Empfehlung: A + C, B als Nutznießer von MOD-37.** **Bruch:** nein. **Abhängig von:** MOD-25,
MOD-17, MOD-41, MOD-37, MOD-27, Gebiet Editor-Werkzeuge. **Belegstufe:** gelesen (Completion),
behauptet (übrige Features).

---

### MOD-44 — Was bedeutet `pub` für generische Instanzen über Modulgrenzen? *(neu)*

**Heute: die Instanz heißt nach dem bloßen Typnamen, und zwei fremde Typen gleichen Namens
werden verwechselt.** Gemessen (`q13`): `pub fn ident<T>` in `lib`, instanziiert im Entry mit
**modulprivatem** `struct Secret` → kompiliert, druckt 3, `lyrvm info` zeigt
`lib.ident<Secret>` — eine Funktion *unter `lib`*, deren Name einen Typ nennt, den `lib` nicht
sehen darf. (`q13b`): ein zweites Modul mit **eigenem** privatem `Secret` instanziiert dieselbe
Funktion → `LYR-CLI0020 … call to lib.ident<Secret>: arg 0 is val ty22, expected val ty0` — der
Lowerer hält die zweite Instanz für die erste (die Meldung ist eine Typverwechslung, kein
Duplikat), und ein legales Programm wird als Compiler-Defekt gemeldet. Kontrolle (`q13c`, zweiter
Typ heißt `Other`): druckt `3 7`. Gelesen: `NameMangling.cs:76-79` schreibt Typargumente als
`named.Path[^1]` — ohne Modul; der Kommentar (`:12-14`) nennt nur `max<int>` gegen `max<float>`.
Folgen: (i) das ist ein Bug in 4.x, unabhängig von v5; (ii) eine kompilierte Bibliothek (BP-09)
kann diese Instanz nicht enthalten — sie entsteht erst beim Konsumenten, und ihr Name gehört
unter `lib`, obwohl `lib` gar nicht neu gebaut wird; (iii) mit MOD-04/06 ist die Frage, ob
`lib.ident<Secret>` die Sichtbarkeit von `Secret` verletzt.

| Option | Vorbild | Preis |
|---|---|---|
| **A: Typargumente im Instanznamen tragen ihr Modul** (`lib.ident<main.Secret>`), und für Builtins nichts | rustc (Symbol-Mangling v0 kodiert den vollständigen Pfad jedes Typarguments), .NET (`List<Ns.T>` im Metadaten-Namen) | Repariert `q13b`. Ein Formatbefund für Namen in `lyrvm info` (Instanznamen ändern sich), kein Host-Bruch (Instanzen sind keine Wurzeln). Braucht MOD-31s Trennzeichen, sonst ist `lib.ident<a.b>` wieder zweideutig. |
| **B: die Instanz gehört dem instanziierenden Modul** (`main.ident<Secret>` — der Name des Konsumenten) | C++ (Template-Instanzen liegen in der Übersetzungseinheit des Konsumenten, COMDAT) | Löst (ii) und (iii) mit: die Instanz eines privaten Typs lebt im Modul, das den Typ sieht, und eine Bibliothek trägt keine Konsumenteninstanzen. Preis: zwei Konsumenten instanziieren `lib.ident<int>` zweimal (Code-Duplikat), es sei denn, Builtin-Instanzen bleiben bei `lib`. |
| **C: private Typen als Typargumente einer fremden `pub` Generik verbieten** | keine — Rust erlaubt es (private Typ, öffentliche Generik, `private_interfaces` schweigt, weil die Instanz nicht exportiert wird) | Bricht ein Muster, das alle neun erlauben. |
| **D: nichts** | — | `q13b` bleibt ein „Defekt im Compiler" für ein legales Programm. |

**Empfehlung: A sofort als Bugfix in 4.x (die Sprache ändert sich nicht, nur ein interner Name),
B als Frage für BP-09.** Für die Sichtbarkeit gilt: die Instanz verletzt nichts, weil sie nicht
exportiert wird (Rusts Antwort) — das gehört als Satz in §4.6, sobald MOD-06/A den Signaturfall
regelt.
**Bruch:** A nein am Host (Instanznamen sind keine Wurzeln), minor an Disassembly/`lyrvm info`.
**Abhängig von:** MOD-31, MOD-06, MOD-14, BP-09, Gebiet Generics. **Belegstufe:** gemessen.

---

### MOD-45 — Wie migriert ein Programm, dessen Bytecode-Namen `main.*` bereits an einen Host verkauft sind? *(neu)*

**Heute: jede headerlose Datei baut zu `main.*`, und Hosts rufen nach Namen.** Gemessen (`q04`):
`lyrc build api.lyr` → `main.newBox`; mit Header `api.newBox` (`q04k`). Gelesen:
`docs/guide/14-embedding.md:29-34` — `instance.Call<long>("onUpdate", …)`, und §4.3 der Spec:
„a host calling by name … settles [an ambiguity] by passing the full name" — also `main.onUpdate`.
MOD-11/A („der Dateiname entscheidet") ändert jeden mangled Namen einer headerlosen Bibliothek
von `main.x` zu `<datei>.x`. Die zweite Fassung führte das unter „Bruch: minor". *Korrektur:*
das ist ein Host-API-Bruch — ein Host, der `Call("main.onUpdate")` sagt, findet nach dem Bau mit
5.0 nichts. Ob ein Host das heute tut: **ungemessen**; Erato 2 lädt Bytecode und ruft nach Namen
(Memo `erato-repo-and-register`), welche Namen, weiß ich nicht (behauptet).

| Option | Vorbild | Preis |
|---|---|---|
| **A: dieselbe Uhr wie MOD-12/MOD-34** — 4.7 warnt beim Bau („built as module 'main'; from 5.0 as 'api'"), `lyrvm info` zeigt beide Namen, 5.0 wechselt | Java 9 (JPMS: Warnungen über `--illegal-access` vor dem Wechsel), Go 1.11 (Module: zwei Releases parallel) | Ein Host hat zwei Minor-Releases Zeit, entweder einen Header zu setzen (dann ändert sich nichts) oder seine Aufrufe umzustellen. |
| **B: Host-Aufrufe ohne Modulpräfix auflösen** — `Call("onUpdate")` findet `<any>.onUpdate`, wenn eindeutig | .NET (`Type.GetMethod("Name")` ohne Namespace) | Entkoppelt den Host vom Modulnamen für die häufigste Form (`14-embedding.md:31` benutzt sie schon so). Preis: eine Ambiguitätsregel im Host-API, die es für Überladungen bereits gibt (§4.3). |
| **C: MOD-11/A nicht machen** | — | `main` bleibt der Name jeder headerlosen Datei; MOD-32/B muss dann allein die Doppelladung fangen. |

**Empfehlung: A und B zusammen.** B macht den Bruch für die meisten Hosts unsichtbar, A deckt
den Rest. MOD-11/A, MOD-34/B und dieser Punkt sind **eine** Uhr.
**Bruch:** major am Host-Rand ohne A+B; mit B minor. **Abhängig von:** MOD-11, MOD-34, MOD-12,
Gebiet FFI/Embedding. **Belegstufe:** gemessen (Namen), behauptet (Host-Nutzung).

---

## 4. Was wir übernehmen sollten

| Von | Was | Wofür in Lyric |
|---|---|---|
| **Rust** | `pub` / `pub(crate)` / privat — drei Stufen, eine Achse | MOD-02, MOD-05 |
| **C# / Kotlin** | `internal` als **Wort** für „innerhalb der Auslieferungseinheit" | MOD-26 (statt Swifts `package`, das im Projekt doppelt belegt ist) |
| **Kotlin** | Extensions sind importierbare, aliasierbare Namen | MOD-20/F, MOD-08/A |
| **Rust** | `pub use` als selektiver Re-Export | MOD-07, nach MOD-02 und MOD-20 |
| **Java / Swift / Rust (bis 1.73)** | „eine öffentliche Signatur darf nichts Privates nennen" | MOD-06/A |
| **Rust (seit 1.74)** | dass man diese Regel als Lint fahren kann, wenn der harte Fehler zu viel bricht | MOD-06, Gestaltung der Uhr |
| **Rust** | eine Instanz mit privatem Typargument verletzt nichts, weil sie nicht exportiert wird | MOD-44 |
| **rustc / .NET** | Typargumente im Instanznamen tragen ihren Pfad | MOD-44/A |
| **JVM / .NET** | Modulpfad und Deklaration haben verschiedene Trennzeichen im Binärnamen | MOD-31 |
| **Go / Java / C# / Kotlin** | Der Testroot gehört zur selben Einheit wie der Quellcode | MOD-16/B, MOD-41/A |
| **Rust** | Orphan Rule | MOD-20/A, Bibliotheksrunde |
| **Rust / OCaml / Swift** | die Schnittstelle einer Bibliothek ist eine **erzeugte** Datei (`.rmeta`, `.cmi`, `.swiftmodule`), nie eine geschriebene | MOD-37/A, als Bedingung an BP-09 |
| **Rust** | `#[allow(…)]` als Notausgang | MOD-27/B → Gebiet Diagnostik |
| **Kotlin** | Stufen am selben Attribut (`@Deprecated(level=…)`) — nur die Idee, nicht die Mechanik | MOD-22/A |
| **Roslyn** | eine Zugänglichkeitsfunktion für Compiler, IDE und Analyzer | MOD-25/A, MOD-43 |
| **Rust / Java / Kotlin** | API-Diff als CI-Gate | MOD-24/C |
| **Rust / Python / Kotlin / C#** | Alias je importiertem Namen | MOD-08/A |
| **Rust / Python 3.12** | „a similar path exists" für Modulpfade | MOD-36 |
| **Rust (`unresolved import` help)** | „consider importing …" statt `unknown identifier` — **nicht Go**, dessen Compiler nur `undefined:` sagt | MOD-29 |
| **Rust / Go** | Zykluspfad in der Meldung | MOD-40 |
| **Cargo / npm** | reservierte Windows-Namen werden abgelehnt | MOD-42 |
| **Go / .NET** | Linker-Dedupe nach Datei, Host-Aufruf ohne Präfix | MOD-32/B, MOD-45/B |
| **Go** | Die Kapselungseinheit ist größer als die Datei; unbenutzter Import ist ein Ereignis; Importe stehen oben | MOD-02, MOD-13, MOD-28 |
| **Go** | Dass ein Glob (`import .`) auch in einer Sprache, die ihn hat, verpönt ist | Ablehnung von MOD-08/B |
| **Rust** | `static` braucht einen const-Initialisierer — aber Lyric hat den Begriff nicht | MOD-10/B′, nur mit §4.7-Literalregel |
| **OCaml** | Abstrakter Typ als Kapselungsmittel | schon da als `opaque type` |
| **Nicht übernehmen** | Swifts `fileprivate`, Javas fehlender Alias, Pythons Konvention-statt-Regel, Glob-Importe, C#' Kombinationsstufen, geklammerte Modifikatoren, Swifts `package` (Kollision) | |

**Die Reihenfolge, in der ich es täte:**

1. **Zwei Bugfixes in 4.x, ohne Sprachänderung:** MOD-44/A (Instanzname trägt das Modul des
   Typarguments — `q13b` ist ein legales Programm mit „Defekt im Compiler") und MOD-31/C
   (Typname im Methodennamen escapen — `q07` ebenso). Beide sind Namensfragen im Lowerer und
   ändern keinen Host-Aufruf, weil Methoden und Instanzen keine Wurzeln sind.
2. **MOD-25** — die Sichtbarkeitsregel an einen Ort, mit dem Vergleichstest Compiler/LSP, und
   die Stellenliste aus dem Symbolmodell. Ohne sie ist jede MOD-04-Uhr unvollständig (drei
   Runden, drei neue Stellen).
3. **Die Diagnose-Ecke, alles ohne Bruch:** MOD-11/B und /E (Root-Schutz, Dedupe nach Datei),
   MOD-40 (Zykluspfad), MOD-33/A (Satz zu `main`), MOD-36 (Pfadvorschlag), MOD-13, MOD-19/C,
   MOD-29, MOD-30, MOD-35 (Warnung beim Verdecken von `panic`), MOD-42.
4. **MOD-41 und MOD-16 entscheiden, dann MOD-04 als Warnung** — in dieser Reihenfolge, in
   derselben Release.
5. **MOD-37 als Bedingung an BP-09, MOD-24/B als Textform derselben Liste** — damit die Frage
   von MOD-02 beantwortbar wird. Erst danach MOD-02 (`internal`), dann
   MOD-05/MOD-15/MOD-21/MOD-22 als eine Uhr (4.7), mit dem zweigeteilten Migrationslauf.
6. **MOD-11/A, MOD-34/B, MOD-45 als eine Uhr** für 5.0 (Dateiname entscheidet, Host-Aufruf ohne
   Präfix).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept):**

- **Kapselung hätte drei Formen**: `opaque type`, Modul-/Root-Sichtbarkeit (MOD-02/04),
  Member-Sichtbarkeit (MOD-05). Verteidigbar als Stufen einer Achse — muss in §4.2 stehen.
- **Die Fläche an drei Orten** (`pub`, Interface-Sektion aus BP-09, `lyric api`) ist nur dann
  kein Rule-2-Verstoß, wenn Sektion und Werkzeug **erzeugte** Formen derselben Funktion sind
  (MOD-37/A, MOD-24). Die zweite Fassung hat diesen Konflikt nicht gesehen, weil sie BP-09 nicht
  kannte.
- **MOD-26/B (`package`)** wäre ein drittes Wort auf dem Stamm „pack/Paket" im selben Projekt
  (`lyric pack` = Executable, „Paket" = Root in der Bibliotheksrunde). Deshalb jetzt C.
- **MOD-21/B wäre eine vierte Achse** (Extension-Member). Deshalb A.
- **MOD-20/F ändert einen normativen Satz** („the methods come with the module, not with a
  name" → „…or with a name"), statt eine vierte Importform zu bauen (B). Das ist Rule 2 in der
  richtigen Richtung: ein bestehender Mechanismus wird breiter, keiner kommt dazu.
- **MOD-07 (Re-Export)** ist ein zweiter Weg, unter welchem Namen etwas erreichbar ist; §4.3a
  muss die verteilte Überladungsmenge definieren.
- **MOD-08/C und MOD-29/B (volle Pfade)** — zweiter Weg. Dagegen, mit MOD-29/A und MOD-36 als
  Erklärpflicht.
- **MOD-18/B (Prelude)** — zweiter Weg an einen Namen. Nur A, und A erst nach MOD-35.
- **MOD-22/C (eigenes Attribut)** — zweiter Weg, eine Änderung anzukündigen. Deshalb A, mit dem
  Eingeständnis, dass die Mechanik ohne Vorbild ist.
- **MOD-10/B′** würde einen neuen Sprachbegriff („konstanter Initialisierer") einführen, wenn er
  nicht die §4.7-Literalregel wiederverwendet.
- **MOD-28/A und /B sind EIN Mechanismus.** MOD-38 gibt `lyrfmt` die Regel für doppelte Importe.

**Innerhalb dieses Gebiets:**

- **MOD-16/B gegen MOD-17/A**: „der Testroot gehört demselben Root" und „Root = Verzeichnis der
  Entry-Datei" widersprechen sich für die getrennte Testkompilation (`q14`). Aufgelöst durch
  MOD-41/A (ein Manifest spannt ein Paket über beide Verzeichnisse); ohne diesen Satz ist MOD-16/B
  nicht umsetzbar.
- **MOD-11/A, MOD-34/B, MOD-45 sind eine Entscheidung** (Dateiname entscheidet → Bytecode-Namen
  ändern sich → Host-Uhr). Die zweite Fassung führte MOD-11/A als „minor"; das galt nur für die
  Sprache.
- **MOD-11/B repariert nur den Hijack, nicht die Doppelladung** (`q08`). MOD-32/B (= MOD-11/E)
  ist die Reparatur, die vor A kommt.
- **MOD-05 gegen MOD-12** ziehen gegenläufig (weniger `pub` Member, mehr Wurzeln); ungemessen.
- **MOD-04 gegen MOD-16/MOD-41**: die Uhr trifft als erstes den Testroot.
- **MOD-04 gegen MOD-25**: eine Uhr, die nur einen Teil der Stellen warnt, bricht den Rest ab 5.0
  ohne Vorwarnung (`extern` kam erst in dieser Runde dazu). MOD-25 vor MOD-04.
- **MOD-31 gegen MOD-03/A**: „der Punkt ist nur ein Zeichen" gilt im Bytecode nicht — dort ist er
  zwei Trennzeichen. Das Mangling muss es wissen, die Sprache nicht.
- **MOD-44 gegen MOD-06**: der Signaturfall (Fehler) und der Instanzfall (erlaubt, nicht
  exportiert) brauchen zwei verschiedene Sätze in §4.2/§4.6.
- **MOD-02 gegen MOD-24/MOD-37**: die Frage „welche `pub` sind nur Nachbarschafts-`pub`" ist ohne
  Werkzeug unbeantwortbar.
- **MOD-18 gegen MOD-35**: die Ladeliste kann erst normativ werden, wenn die Bindungsliste eine
  Verdeckungsregel hat.

**Mit anderen Gebieten:**

- **Bibliotheken/Build (BP-09, BP-03, BP-25):** MOD-37 ist die Bedingung an die
  Interface-Sektion; MOD-34 an das `name`-Feld; MOD-41 an die Testroot-Regel. MOD-44/B fragt, ob
  eine kompilierte Bibliothek Konsumenteninstanzen tragen kann (sie kann nicht).
- **Bytecode/Format:** MOD-12/A, MOD-24/A, MOD-37 brauchen einen Export-Begriff; MOD-31 und
  MOD-44 ändern interne Namen (kein Host-Bruch, `lyrvm info` ändert sich).
- **FFI/Embedding:** MOD-39 (Header gegen Parameter, `std.*` im Host), MOD-45 (Host-Aufruf ohne
  Präfix), MOD-04 (`extern` gehört in die Liste), MOD-12.
- **Typen/Member:** MOD-05 (Feld-Pattern, Initializer außerhalb); der Alias-Befund (`r04b`/`r04k`).
- **Enums & Patterns, Interfaces & Konformanz:** MOD-04 muss Variantenpfad und alle drei
  `::`-Positionen mitnehmen (`r06`).
- **Generics/Monomorphisierung:** MOD-44 (Instanznamen), MOD-14/B (Versionen im Namen).
- **Toolchain:** `lyrfix` (MOD-05/15/21/27), `lyric api` (MOD-24), `lyrfmt` (MOD-28/38), das
  Wort `pack` (MOD-26), Windows-Namen (MOD-42).
- **Diagnostik:** MOD-27/B (`allow`), MOD-36 (Vorschläge im Resolver), MOD-40.
- **Editor-Werkzeuge:** MOD-43 (Root-Modell im LSP, Rename-Grenze), MOD-25 (Vergleichstest).
- **Laufzeit:** MOD-10/B′ und `LYR-VM0017` (ungemessen im Zyklusfall).
- **Testen:** MOD-16, MOD-41, MOD-33 (`main` in einer Testdatei ist Entry).
- **stdlib:** MOD-18/B (Prelude), MOD-20/A (Orphan Rule), MOD-35 (`panic` verdeckbar).
- **Attribute:** MOD-22/A (Feld am `@Deprecated`), MOD-10/B′ (Literalregel §4.7).

**Was in `lyric-v5-features.md` fehlt:** die Liste führt nur A3 #20 (Member-Sichtbarkeit).
MOD-04, MOD-02, MOD-07, MOD-08, MOD-11, MOD-15 und alle dreißig Fragen MOD-16 bis MOD-45 kommen
dort nicht vor — darunter zwei Compiler-Defekte für legale Programme (MOD-31, MOD-44).

---

## 6. Nach der Kritik geändert

**Nachgemessen und korrigiert (Kritik hatte recht):**

- **MOD-05 / 1.3 C:** „`pub` vor einem Member parst" galt nur für Methoden und `static let`;
  an einem **Feld** parst es nicht (`q01`: `PAR0011`+`PAR0026`+`PAR0031`; `Grammar.md:237`;
  Prototyp-17-README). Der Migrationslauf ist jetzt zweigeteilt beschrieben.
- **MOD-18:** „es gibt keine Prelude", „einzige der neun ohne implizite Namensmenge" war falsch
  und selbstwidersprüchlich. Gemessen (`q03`): `panic` ohne Import. Neu strukturiert in Ladeliste
  (Code) und Bindungsliste (Spec §4.4), mit MOD-35 als Voraussetzung.
- **MOD-12/MOD-24/1.2 r14:** „enthält `api.newBox` und `api.helper`" galt nur wegen des Headers;
  ohne Header heißt eine Bibliothek `main` (`q04`/`q04k`). Neu als MOD-34.
- **MOD-30:** „`let lib = 5;` neben `import lib;` kompiliert wortlos" gilt nur lokal (`q05a`);
  auf Top-Level ist es `RES0001` (`q05b`). Asymmetrie kleiner beschrieben; C# unter B.
- **MOD-04, Betroffenenliste:** `extern`-Deklarationen fehlten (`q06`/`q06k`). Und die Liste
  gilt jetzt ausdrücklich als nicht abgeschlossen — MOD-25 leitet sie aus dem Symbolmodell ab.
- **MOD-24/B:** „ein Nachmittag Arbeit" gestrichen; B ist ohne die Interface-Sektion eine zweite
  Definition von „Fläche".
- **§2 Go:** „ohne Dateien zu öffnen" korrigiert — Go liest die Importe aus den Quelldateien; der
  Gewinn ist, dass nur direkte Abhängigkeiten (Exportdaten) gelesen werden. Als „behauptet"
  gekennzeichnet.

**Vergleichssprachen korrigiert:**

- **MOD-05/D:** Python ist das Vorbild für „alle Member öffentlich"; „D hat kein Vorbild" war
  falsch.
- **MOD-26/B:** Swift hat `private(set)`/`internal(set)` seit Swift 1; die Begründung „keine
  Klammermodifikatoren" war erfunden. **Und die Empfehlung ist von B (`package`) auf C
  (`internal`) gewechselt**, wegen der Kollision mit `lyric pack` und „Paket".
- **MOD-20/B:** Kotlin **hat** die Lösung (Extensions namentlich importieren/aliasieren). Neue
  Option F, jetzt empfohlen; B ist überflüssig.
- **MOD-04:** „vier der neun" → sieben von neun (plus Python ohne Grenze), eigener Absatz in §2.
- **MOD-29/A:** Go gestrichen (der Compiler sagt nur `undefined:`); Rust bleibt.
- **MOD-30/A:** C# CS0136 ist ein Fehler für Locals → unter B.
- **MOD-22/A:** Kotlins Stufen senken keine Sichtbarkeit; das Vorbild trägt nur „Stufen am
  selben Attribut", die Mechanik ist ohne Vorbild.
- **MOD-19/A:** Java nur für `public` Top-Level-Klassen (JLS §7.6) — halb passend.
- **MOD-08/B:** Gos Dot-Import als Glob und als stärkstes Gegenargument aufgenommen.

**Neu aufgenommen (fünfzehn Fragen, MOD-31 bis MOD-45, jede in derselben Form):**
Bytecode-Namensschema (`q07`/`q07k`), Modulidentität und Doppelladung (`q08`/`q08c`/`q08k`),
`main` als Modul- oder Programmname (`q09`), Bibliotheksname ohne Header (`q04`/`q04k`),
implizit gebundene Namen und Verdeckung (`q03`/`q03b`/`q03c`/`q03d`), „did you mean" für
Modulpfade (`q10`/`q10k`), Kosten pro Kompilation und Interface-Sektion (`q11`, BP-09),
Kombination der Importformen (`q12`/`q12b`/`q12c`/`q16`/`q16b`), Embedding-API (gelesen),
Zyklusdiagnose durch den Entry (`q08`), Kapselungseinheit für den Testroot (`q14`/`q14k`),
Windows-Gerätenamen (bewusst nicht gemessen), Werkzeugfragen jenseits `lyric api` (gelesen),
generische Instanzen über Modulgrenzen (`q13`/`q13b`/`q13c` — **ein neuer Compiler-Defekt, den
die Kritik nur vermutet hatte**), Host-Bruch bei MOD-11/A (`q04`).

**Empfehlungen geändert:**

- **MOD-11:** von „C, mit B zuerst" zu „E (Dedupe nach Datei) und B sofort, A in 5.0 mit der
  Uhr aus MOD-45".
- **MOD-16/B:** braucht den Paketsatz aus MOD-41/A, sonst widerspricht es MOD-17/A.
- **MOD-26:** von B (`package`) zu C (`internal`).
- **MOD-10/B′:** Preis korrigiert — ein neuer Sprachbegriff, es sei denn, die §4.7-Literalregel
  wird wiederverwendet.
- **MOD-20:** von B zu F (Kotlin).
- **MOD-18/A:** erst nach MOD-35.
- **MOD-04:** zweite Auflage — Warnung erst mit der Stellenliste aus MOD-25.
- **§4 Reihenfolge:** zwei 4.x-Bugfixes (MOD-44/A, MOD-31/C) an Platz 1, vor allem anderen.

**Was die Kritik betraf und stehen geblieben ist:**

- **MOD-05/Zig:** Zig gehört zu B (Deklarationen brauchen `pub`, Felder nicht) — das stand
  richtig und bleibt; nur D hat jetzt Python als Vorbild.
- **1.2 Überladungshälfte, Zyklus ohne Pfad, `r19`-Befund, stdlib-Zählung:** unverändert, von
  der Kritik nicht angegriffen und in dieser Runde nicht neu gemessen (die Proben `r16`, `r07`,
  `r19` stehen unter `probes/module-rev/`).
- **MOD-17/A:** bleibt, jetzt ausdrücklich auf den manifestlosen Fall beschränkt — die Kritik
  hatte den Widerspruch zu MOD-16/B richtig gesehen, die Auflösung ist MOD-41, nicht eine
  Änderung von MOD-17.
- **MOD-03/A:** bleibt; die Kritik-Frage zum Mangling ist ein Lowerer-Befund (MOD-31), keine
  Hierarchiefrage der Sprache.
