# Lyric 5 — Gebiet: Diagnostik, Warnungen, Migration

**Messstand.** Alle Messungen dieser Fassung laufen gegen
`src/Lyrc/bin/Debug/net10.0/lyrc.dll`, `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`,
`src/Lyrbuild/bin/Debug/net10.0/lyrbuild.dll` und `src/Lyrtest/bin/Debug/net10.0/lyrtest.dll`.
Der Compiler meldet 4.6.0. Spec-Checkout `C:/Users/Olivier/CLionProjects/lyric-spec`, HEAD
`8f17c02` (Merge #44, „spec/v5-migration-warnings"). Proben der ersten Fassung liegen unter
`scratchpad/v5-design/probes/diagnostik/`, die Nachmessungen dieser Fassung unter
`scratchpad/v5-design/probes/diagnostik3/`.

**Wichtigste Korrektur gegenüber der ersten Fassung.** Der Stand, gegen den gemessen wird, hat
seit 4.6 eine **Migrationswarnungs-Familie** (`LYR-SEM0107`–`SEM0110`) mit eigenem Spec-Kapitel
§12.5. Die erste Fassung kannte sie nicht und hat deshalb D14 („Wie schaltet ein Benutzer eine
5.0-Regel probeweise ein? — **heute: gar nicht**") falsch gestellt: sie ist bereits gebaut, als
**immer an**, ohne Schalter, ohne Ausnahme, und sie zählt gegen `--deny-warnings`. Alles, was in
dieser Fassung mit D19–D21 dazukommt, folgt daraus.

---

## 1. Ist-Stand

### 1.1 Das Modell, das funktioniert

Vier Severities (`src/Lyric.Core/Severity.cs:8-21`): `Error`, `Warning`, `Info`, `Hint`. Die
Severity gehört dem **Code**, nicht dem Aufrufer — Spec §12.1 Regel 1. Ein Diagnostic ist
`(Code, Severity, Span, Message, Notes?)`; eine Notiz ist `(Location, Message)` und sonst nichts
(`src/Lyric.Core/DiagnosticNote.cs:12`, Doc-Kommentar `:3-11`) [gelesen].

> **Korrektur.** Die erste Fassung zitierte `DiagnosticNote.cs:52-55` und `:47-48`. Die Datei hat
> **16 Zeilen** [gemessen, `wc -l`]. Inhaltlich stimmten beide Zitate, die Zeilennummern waren
> erfunden. In einem Dossier, dessen Autorität auf Pfad:Zeile beruht, ist das kein Detail; alle
> Belegstellen dieser Fassung sind neu nachgeschlagen.

Was **gut** ist und v5 nicht anfassen sollte:

| Was | Beleg |
|---|---|
| Ein Wächtertest hält den Katalog | `tests/Lyric.Tests.Cli/DiagnosticCatalogueTests.cs:97-116` liest jedes `LYR-*`-Literal aus `src/**/*.cs` und vergleicht mit `appendix-a-diagnostics.md` [gelesen] |
| §12.1 Regel 1 ist maschinell erzwungen | `DiagnosticCatalogueTests.cs:126-152`: kein Code darf an zwei Severities gemeldet werden [gelesen] |
| Zurückgezogene Nummern dokumentiert | `appendix-a-diagnostics.md` §A.11, drei Retirement-Zeilen [gemessen: 3 Zeilen der Form „Retired…"] |
| Erschöpfungsmeldung nennt die fehlenden Muster | gemessen (`p14_match.lyr`): `SEM0050: match on 'Shape' is not exhaustive — no arm matches 'Square(_)', 'Tri(_, _)'` |
| „did you mean" für Identifier **und** Member, und Schweigen bei Gleichstand | gemessen (`p12_typo`, `p13_two`); frisch bestätigt in `q20_fieldinc.lyr`: `note: did you mean 'P'?` |
| Terminal-Injection geschlossen | `DiagnosticEngine.Printable`, `DiagnosticEngine.cs:75-110` — **ein Zeichen raus für ein Zeichen rein**, damit die Caret-Zeile die Spalten des Originals zählt [gelesen] |
| Speculative-Scope existiert schon | `DiagnosticEngine.Mute()`, `:37-67` — Aufzeichnung aus, für Überladungsauflösung [gelesen] |
| Notizen über Dateigrenzen | gemessen (`cross/`): Warnung in `lib.lyr` aus einem Lauf über `main.lyr` |
| Panik trägt Code + Backtrace | gemessen (`q25_panic.lyr`): `panic [LYR-VM0002]: division by zero` + Frame, Exit 101 |
| Tiefenlimit statt Prozessabbruch | gemessen (`q22_longline.lyr`, 120 000 `+`): `SEM0105`, 192 Ebenen, kein Absturz |

**Nicht mehr in dieser Tabelle: „Katalog vollständig und in Sync, null Differenz in LEX, PAR,
RES, SEM, VM, CLI, BC, CAP, EMB".** Die Messung war zirkulär. Die neun Bereiche sind Zeichen für
Zeichen der Alternativsatz der Prüfregex (`DiagnosticCatalogueTests.cs:34`). `LYR-CT0001` und
`LYR-CT0002` sind emittierte Konstanten (`src/Lyric.Frontend/Compiler/SourceCompiler.cs:560,564`)
und stehen in **keiner** Spec [gemessen: `grep -rn "CT0001\|CT0002" lyric-spec/spec/` → null
Treffer]. „Null Differenz" ist nur wahr, wenn man vorher genau die Differenz aus der Menge
herausdefiniert.

### 1.2 Was inkonsistent, unvollständig oder still falsch ist

**(a) `IR0001` erzählt eine Geschichte, die oft nicht stimmt.** Genau ein Code für die ganze
Phase, bewusst (`LoweringDiagnostics.cs:5-19`), mit einer festen Notiz *„this compiler version
cannot lower it yet"* (`LoweringDiagnostics.cs:28`). Gemessen (`q21_fieldinc.lyr`, mit Kontrolle
`q21b_control.lyr` → `ok`):

```
q21_fieldinc.lyr:5:5: error[LYR-IR0001]: increment/decrement target (only parameters and locals)
    p.x++;
    ^^^
  note: this compiler version cannot lower it yet
```

Die Notiz ist hier **falsch**: laut `docs/Befunde_und_Verbesserungen/PLAN.md:184-187` ist §6.1
(„auf ganzzahligen *Variablen*") eine offene **Regel**frage, keine Implementierungsgrenze — und
zwar ausdrücklich: *„`p.x++` gehört nicht hierher … die Frage, worauf ein Inkrement stehen darf,
[ist] offen und keine Grenze, die falsch benannt wäre"* [gelesen]. Der Leser wartet auf ein
Release, das nie kommt. Emissionsstelle: `FunctionLowerer.cs:5015`. Die Nachricht ist außerdem
eine Substantivgruppe ohne Verb und ohne den einen nützlichen Satz („schreib `p.x += 1`").

**Die Größenordnung war um Faktor drei falsch.** Die erste Fassung sprach von „37
Emissionsstellen, davon 8 NeverNull und 29 Default-Notiz". Gezählt [gemessen]:

| Form | Anzahl | Wo |
|---|---|---|
| `throw NotSupported(` | **80** | ausschließlich `FunctionLowerer.cs` |
| `new UnsupportedConstructException` | **25** | `TypeTable.cs` 18, `InstanceTable.cs` 3, `GlobalTable.cs` 2, `ModuleLowerer.cs` 1, `DeclaredTypes.cs` 1 |
| `ReportUnsupported` mit literaler Meldung | **3** | `ModuleLowerer.cs:225`, `:479`, `:1086` |
| `ReportUnsupported` als Trichter (`ex.Message`) | 7 | `ModuleLowerer.cs` — keine eigenen Stellen, sondern die Auffangpunkte |

**Summe ≈ 108 Stellen, nicht 37.** Die acht `NeverNull`-Stellen stimmen
(`FunctionLowerer.cs:1990, 2585, 3003, 3100, 3119, 3162, 3205, 3288`) [gemessen]. „29
Default-Notiz" sind in Wahrheit **rund hundert**. Damit fällt jede Kostenrechnung der ersten
Fassung, die an der 37 hing.

**Stichprobe statt Schätzung.** Alle 80 Meldungsformen in `FunctionLowerer.cs` gelesen und nach
Meldungstext eingeteilt — die Einteilung ist meine Lesung, nicht gemessen [gelesen]:

| Art | grob | Beispiele |
|---|---|---|
| **Defensive Invariante** — die Sema hat das schon geprüft; feuert nur bei einem Loch oder Compilerfehler | ~34 | `'X.iter' was not lowered` (1252), `iterating a value that is not an object` (1406), `is not an enum` (2319), `call to 'm' (no declaration)` (3691) |
| **Echte Grenze** | ~30 | `range expression` (1628), `attribute 'X'` (1633), `'+' on arrays` (1860), `an 'if' expression whose both branches diverge` (1948), `'params x' whose type is not an array` (4472), `type parameter 'T'` (5079) |
| **`NeverNull`** — Ablehnung, kein Release hilft | 8 | `'??' on a non-optional` (3119) |
| **Programmfehler, gehört in die Sema** | ~5 | `unknown field 'f' in a pattern` (2685), `initializer omits field 'w'` (3531) |
| **Offene Regel** | 2 | `{what} target (only parameters and locals)` (5015, 5019) |

Die 18 `TypeTable.cs`-Stellen sind fast durchweg Typ- und Monomorphisierungsgrenzen, also
strukturell [gelesen]. Die drei literalen `ModuleLowerer`-Stellen sind: *„'main' takes either no
parameters or exactly one 'string[]'"* (ein Programmfehler), *„the monomorphization does not
terminate"* (eine echte, dauerhafte Grenze mit einer ausgezeichneten Meldung) und *„'T'
implements 'I', but its 'm' is not lowerable"* (eine Lowering-Lücke) [gelesen].

**(b) Der Spec-Eintrag zu `IR0001` ist bereits wieder veraltet.** `appendix-a-diagnostics.md`
§A.5 sagt, `IR0001` decke „currently `&&=`/`||=` and a `catch` naming a specific interface".
Beides ist seit PR #169 gefixt (`PLAN.md:149-155`); `p.x++` steht dort nicht [gelesen +
gemessen]. Das ist kein Fehler des Autors — es ist die **strukturelle Eigenschaft eines
Sammelcodes**: sein Inhalt ist nicht spezifizierbar und driftet mit jedem Release.

**(c) Die Sema läuft auf Parser-Recovery-Knoten. REPRODUZIERT, zweimal.** `STATUS.md:2261` führt
das als *„unreproduced"*, `PLAN.md:177` als *„offen, nicht reproduziert … Vier Versuche ergaben
je eine Sema-Meldung, die zum Programm passte"* [gelesen].

*Probe `p9c_spaced.lyr`* — ein einziges Zeichen zu viel:

```
fn f(x: ?int): ? ?int { return x; }
```
```
p9c_spaced.lyr:1:1: error[LYR-SEM0051]: 'f' has no body; only standard-library modules may declare native functions
p9c_spaced.lyr:1:18: error[LYR-PAR0011]: expected a type, got Question
```

Die Funktion **hat** einen Rumpf. Kontrolle (`p9b_control.lyr`, `?int` statt `? ?int`): `ok`. Die
erste Zeile, die der Leser sieht, ist die falscheste — weil die Sortierung nach Position geht und
Spalte 1 vor Spalte 18 kommt.

*Probe `q20_fieldinc.lyr`* — frisch nachgemessen, `:` statt `=` im Struct-Initialisierer:
**zwölf Fehler** aus einem Zeichen, darunter `SEM0017 not all code paths of 'main' return a
value` (`main` gibt zurück), `SEM0045` zweimal über ein Trailing-Lambda, das niemand geschrieben
hat, `SEM0002 unknown identifier 'x'` über einen Feldnamen und `SEM0022 expression statement has
no effect`. Kontrolle (`q21_fieldinc.lyr`, `=` statt `:`): genau **ein** Fehler, und zwar der
richtige. Ursache: `Typ {` ist zugleich Initialisierer und Trailing-Lambda (`Grammar.md:500`);
die Rückfallinterpretation ist *strukturell gültig* und die Sema prüft sie, als wäre sie gemeint.

Ein einfacher Parse-Fehler (`let a = 1 +;`) erzeugt dagegen **genau einen** Fehler (gemessen,
`p2_recovery.lyr`). Die Regel lautet also: Recovery, die zu einem **anderen gültigen Baum** führt,
vergiftet nicht — sie lügt.

**(d) Folgefehler stehen vor ihrer Ursache.** Gemessen (`p14_match.lyr`): `SEM0017 not all code
paths of 'area' return a value` (Zeile 3) vor `SEM0050 match … is not exhaustive` (Zeile 4).
Kontrolle: vollständiger Match → beide weg. Die Sortierung
(`src/Lyric.Core/DiagnosticsComparer.cs:5-14`) ist Datei → Span-Start → Span-Ende → Code. Keine
Severity, keine Phase, keine Kausalität. Errors, Warnings und Hints stehen in **einem** Strom
nach Position gemischt (gemessen, `q23_hint.lyr`: `hint` Zeile 2 vor `warning` Zeile 3).

**Und die Ordnung ist nicht einmal total.** `DiagnosticEngine.SortedSnapshot` (`:69-74`) benutzt
`List<T>.Sort` — in .NET ausdrücklich **instabil** — mit einem Comparer, dessen letzter Schlüssel
der Code ist. Zwei Diagnosen mit gleicher Datei, gleichem Span und gleichem Code haben eine
implementierungsdefinierte Reihenfolge [gelesen]. Golden-Tests und Konformanzfälle hängen daran.
Siehe **D24**.

**(e) Es gibt keine Unterdrückung — und das kostet gerade eine Deprecation-Uhr.**
`docs/guide/19-diagnostics.md:7-9`: *„no flag turns a warning into something else"*. Kein
`--allow`, kein `#[allow]`, kein `@Suppress`, kein `// lyric: ignore`. `lyrc --help` bestätigt:
nur `--deny-warnings`, global, alles-oder-nichts [gemessen]. Hints zählen dabei **nicht** mit —
gemessen (`q23_hint.lyr`: 1 Hint + 1 Warnung → `error[LYR-CLI0016]: 1 warning denied by
--deny-warnings`, Exit 1).

Der Preis steht im Quelltext, `stdlib/std/iter.lyr:871-873` [gelesen]:

> *„`@Deprecated` cannot mark them yet, because the warning would fire inside the standard
> library's own tests, which still exercise both forms."*

Damit ist eine geplante 5.0-Entfernung **ohne laufende Uhr**. Nachgeprüft, alle fünf Posten aus
`PLAN.md:361` — unverändert gegenüber der ersten Fassung [gemessen, `grep -n Deprecated`]:

| Posten | `@Deprecated`? | Beleg |
|---|---|---|
| `addExecutable` | **ja**, `until = "5.0"` | `stdlib/std/build.lyr:179` |
| freie Iterator-Terminatoren | **nein** | `stdlib/std/iter.lyr:872` (Kommentar statt Attribut) |
| `listContains` | **nein** | `stdlib/std/collections.lyr` — kein `@Deprecated` in der Datei |
| `keys(m)` | **nein** | dito |
| `std.os`-Zeitfunktionen | **nein** | `stdlib/std/os.lyr` — kein `@Deprecated` in der Datei |

**Eine von fünf Uhren läuft.** `PLAN.md:368-369` gilt für genau einen Posten.

**(f) `until` wird an der Verwendungsstelle verschwiegen.** Gemessen (`p5c_dep_future.lyr`,
`until = "99.0"`): die Warnung lautet Wort für Wort dieselbe wie ohne `until`
(`'old' is deprecated: use renew`). Die einzige handlungsleitende Information — *wann* es weg ist
— steht nur in der Deklaration (`stdlib/std/core.lyr:504-509`). Die Deklarationsprüfung selbst
ist gut (`SEM0081`, gemessen: *„this was kept until 1.0 and the toolchain is 4.6.0"*).

**(g) `--json` gilt für den Einzeldatei-Pfad, nicht für die Toolchain — und in CI läuft der
andere.** Gemessen, jeweils mit Kontrolllauf:

| Lauf | Ausgabe | Exit |
|---|---|---|
| `lyrbuild proj` (Kontrolle) | `SEM0071`-Warnung als Text, absoluter Pfad, Artefakt geschrieben | 0 |
| `lyrbuild proj --json` | `error[LYR-CLI0003]: unknown argument: --json — try 'lyrbuild --help'` | **2** |
| `lyrbuild proj --deny-warnings` | derselbe `CLI0003` | **2** |
| `lyrtest proj --json` | `unknown argument: --json — try 'lyrtest --help'` | **2** |
| `lyrvm run q25.lyrbc --json` | `panic [LYR-VM0002]: division by zero` als **Text** | 101 |
| `lyrvm run bad.lyrbc --json` | `error[LYR-BC0001]: not a .lyrbc file …` als **Text** | 1 |
| `lyrvm info q25.lyrbc --json` (Kontrolle) | echtes JSON | 0 |
| `lyrc check … --json --quiet` (Kontrolle) | sauberes JSON auf stderr, stdout leer | 0 |

Im Quelltext: `src/Lyrbuild/BuildSession.cs:184` und `:218` rufen ausschließlich
`result.Diagnostics.RenderText(error)` — es gibt keinen JSON-Pfad. `denyWarnings` existiert dort
nur als **Artefaktfeld** aus dem Profil (`BuildSession.cs:11,112,226-231`), nicht als Schalter
[gelesen]. `lyrvm --help` verspricht *„Diagnostics (and 'info') as JSON"* und bedient nur `info`.
`lyrfmt` und `lyrpack` geben `--json` in ihrer Hilfe an [gemessen].

> **Korrektur.** Die erste Fassung schrieb „dieselbe Bugform … eine Binärdatei weiter". Es sind
> **mindestens drei** Binaries, und die schlimmste ist die, die in CI läuft.

**(h) Die Panik hat ein fünftes Format.** `panic [LYR-VM0002]: …` folgt nicht
`severity[CODE]: message`. „panic" ist keine der vier Severities. Ein Werkzeug, das Diagnostik
parst, erkennt sie nicht als solche [gemessen]. Appendix A führt 16 `panic`-Zeilen ohne Severity
[gemessen].

**(i) Zwei Codes existieren laut Spec nicht.** `LYR-CT0001`/`LYR-CT0002`
(`SourceCompiler.cs:560,564`, emittiert `:194` und `:215`) stehen weder in der Bereichstabelle
§12.1 noch in Appendix A [gemessen: null Treffer im Spec-Checkout]. §12.2: *„A code that appears
in neither the appendix nor a retirement row does not exist."* **Und der Wächtertest sieht sie
nicht**: seine Regex ist auf `LYR-(LEX|PAR|RES|SEM|IR|CLI|BC|CAP|VM|EMB)` begrenzt
(`DiagnosticCatalogueTests.cs:34`) — `CT` steht nicht im Alternativsatz [gelesen]. Siehe **D16**.

**(j) ~~Spec-Tippfehler `CLI0018`~~ — erledigt.** Die erste Fassung meldete
`12-diagnostics.md:72` schreibe `CLI0018`, wo `CLI0020` gemeint ist. Der Befund war echt
(`git show 8512430^:spec/12-diagnostics.md`) und ist in `8512430` mitbehoben worden. Gemessen am
aktuellen Spec-HEAD: `grep -n CLI0018 spec/12-diagnostics.md` → **null Treffer**; Zeile 72 lautet
„`CLI0020` is the compiler being wrong." **Steht nicht mehr offen.**

**(k) Rendering.** Gemessen und im Quelltext:

| Befund | Beleg |
|---|---|
| Mehrzeiliger Span → **ein** Caret | `DiagnosticEngine.cs:143-146`: `diagPos.Line == endPos.Line ? Math.Max(Length,1) : 1` [gelesen] |
| Keine Farbe, überhaupt keine | `TerminalOutput.cs:15` *„The display is plain ASCII"* |
| Quellzeile wird ungekürzt ausgegeben | gemessen (`q22_longline.lyr`): 240-KB-Zeile → **480 KB stderr** (Quellzeile + Caret-Zeile, beide in voller Länge) |
| Pfaddarstellung ist **pro Datei**, nicht pro Position | gemessen (`cross/`): Lauf über `cross/main.lyr`; die Warnung in `lib.lyr` trägt in der **Kopfzeile** den absoluten Pfad, die Erfolgszeile `cross/main.lyr: ok` bleibt relativ. `lyrbuild` druckt auch die Einstiegsdatei absolut. |
| JSON ohne Zeilenumbruch am Ende | gemessen (`od -c` der letzten vier Bytes: `" } ] }`) |
| `--json` unterdrückt die Erfolgszeile nicht, `--json --quiet` schon | gemessen |

> **Korrektur.** Die erste Fassung schrieb „Pfad im Kopf relativ, in der Notiz absolut" und
> benannte damit die falsche Regel. Die Regel ist **pro Datei**: die Einstiegsdatei behält den
> Pfad, wie er übergeben wurde, jede geladene Datei bekommt den aufgelösten. Folge für D11-C:
> „immer so, wie der Benutzer es angab" hat für eine importierte Datei keine erste Option — der
> Benutzer hat sie nie angegeben.

**(l) Nur *eine* Warnfamilie verschwindet, sobald ein Fehler existiert — und das ist ein Zufall
der Emissionsstelle.** Gemessen, mit Kontrolle:

| Probe | Inhalt | Ausgabe |
|---|---|---|
| `q14_err_plus_unused.lyr` | unbenutztes Local + `return nosuch;` | **nur** `error[LYR-SEM0002]` |
| `q14b_control.lyr` | dasselbe, `return 0;` | `warning[LYR-SEM0071]` + `ok` |
| `q13_err_plus_migwarn.lyr` | doppeltes `let x` + `return nosuch;` | **beides**: `warning[LYR-SEM0107]` **und** `error[LYR-SEM0002]`, Exit 1 |
| `q13b_control.lyr` | dasselbe, `return x;` | dieselbe Warnung, dann `ok` |

`PLAN.md:190-197` nennt die Unterdrückung **beabsichtigt** und begründet sie gut („eine Warnung
aus einer halben Tabelle ist eine Vermutung im Tonfall der Gewissheit") [gelesen]. Aber sie gilt
nur für die `WarningAnalyzer`-Familie (`SEM0071`–`SEM0075`). Die Migrationsfamilie sitzt in
`TypeChecker.cs:1211` und `SemaRules.cs:331,339` und läuft weiter [gelesen]. Der Unterschied ist
heute **keine Entscheidung**, sondern folgt daraus, in welcher Datei der Aufruf steht.

**(m) Keine Obergrenze.** Gemessen: 30 unbekannte Identifier → 30 Fehler.

**(n) Keine Toter-Code-Warnung für Deklarationen.** Gemessen (`p17`): eine nie gerufene private
`fn` und ein nie benutzter `struct` → `ok`, kein Wort.

**(o) Keine maschinenlesbaren Korrekturen.** Das JSON trägt `code`, `severity`, `file`,
`start`/`end` (Zeile, Spalte, Offset), `notes`, `message` — **keinen** Ersetzungstext, keine
Anwendbarkeitsstufe, **keine `kind`**, keine Zielversion [gemessen an `q13b_control.lyr --json`].
Der LSP hat keinen `CodeAction`-Pfad (gesucht in `src/Lyric.Lsp/`: null Treffer) und setzt nur
`RelatedInformation` und `Tags` (`DiagnosticMapper.cs:54-76`); `CodeDescription` wird nicht
gesetzt [gelesen]. **`lyrfix` existiert nicht** (kein Projekt in `src/`; 17 Projekte, gemessen);
`PLAN.md:328` und `:369` setzen es voraus.

**(p) Keine Deduplizierung auf Engine-Ebene.** Der Fix aus PR #171 ist ein `.Distinct()` an der
Kandidatenliste, daneben ein `_reportedThrows`-Set (`TypeChecker.cs:31`). Pro Stelle, nicht
einmal.

**(q) Kein `explain`.** Weder `lyrc explain LYR-SEM0071` noch eine URL im Diagnostic [gemessen an
`lyrc --help`]. Der Katalog liegt im **Spec-Repo**, das ein Benutzer der Toolchain nicht hat —
und der Wächtertest findet ihn nur, wenn ein Klon danebensteht; sonst gibt er still grün zurück
(`DiagnosticCatalogueTests.cs:52-64`, `:100` `return;`) [gelesen].

**(r) Eine Severity ohne einen einzigen Code.** `Severity.Info` kommt in `src/` **genau einmal**
vor, und zwar als Abbildung im LSP (`DiagnosticMapper.cs:110`), nirgends als Emission [gemessen,
`grep -rn "Severity.Info" src/`]. Appendix A: **194 E, 1 E², 12 W, 1 H, 0 I**, dazu 16
Panik-Zeilen ohne Severity und 3 Retirement-Zeilen — 227 Zeilen insgesamt [gemessen].

**(s) Genau eine Meldungsregel ist Test.** `tests/Lyric.Tests.Core/DiagnosticTextTests.cs:46`
(`No_string_literal_cites_a_document`) verbietet `§` und `.md` in Meldungsliteralen. Mehr nicht
[gelesen, 70 Zeilen].

---

## 2. Sprachvergleich

Acht Korrekturen gegenüber der ersten Fassung stehen unter der Tabelle. Drei Sprachen sind neu:
**Kotlin**, **Java** und **GHC** — alle drei, weil die erste Fassung eine Zelle mit einem Vorbild
belegt hat, das die Eigenschaft nicht hat.

| Sprache | Codes | Unterdrückung | Fix-its | Migration | Preis |
|---|---|---|---|---|---|
| **Rust** | `E0308`, `rustc --explain E0308`, Error-Index im Web — **mit Stabilitätszusage** | Lint-Level `allow/warn/deny/forbid` als Attribut **und** `-A/-W/-D/-F`; Lint-Gruppen; `--cap-lints` für Dependencies | JSON trägt `suggested_replacement` + `applicability`; `cargo fix` wendet `MachineApplicable` an, iterativ | **Editionen**: `rust-2021-compatibility`-Lintgruppe warnt in der alten Edition, `cargo fix --edition` fixt, dann Schalter umlegen | riesige Lint-Ontologie; „welcher Level gilt hier" ist eine eigene Lernaufgabe; Lint-Namen sind ein zweiter Namensraum neben den Codes. **`#[deprecated]` hat auf stable nur `since` und `note`** — kein anwendbarer Ersatztext, und `cargo fix` migriert Deprecations nicht |
| **Clang** | Keine Nummern, **Gruppennamen**: `-Wunused-variable`; `-fdiagnostics-show-option` hängt den **Schalter** an die Meldung (per Default an). **Keine URLs** | `-Wno-X`, `-Werror=X`, `-Wno-error=X`, `#pragma clang diagnostic push/ignored/pop` — **regionsbasiert**, gilt bis zum `pop` | Fix-it-Hints in der Meldung; `-fdiagnostics-parseable-fixits`; `-serialize-diagnostics` | `__attribute__((deprecated("msg")))`, Feature-Makros | `-ferror-limit` (Default 20) kappt; die Gruppenhierarchie ist historisch gewachsen und nicht orthogonal; `push/pop` ist genau der Mechanismus, mit dem man eine ganze Datei stummschaltet |
| **C# / Roslyn** | `CS0168`, `CA1822`, `SYSLIB0001`. **URL nur, wo ein Attribut sie mitbringt**: `UrlFormat` der Obsoletionen (`aka.ms/dotnet-warnings/{0}`), `HelpLinkUri` am Analyzer-Descriptor. Compilerdiagnosen (`CS…`) tragen in der Ausgabe **keine** | `#pragma warning disable/restore`, `[SuppressMessage]`, `<NoWarn>`, `<WarningsAsErrors>`, **`.editorconfig` je Diagnostic-ID** | `CodeFixProvider` liefert Quickfixes an IDE **und** `dotnet format`; SARIF via `/errorlog` | `[Obsolete(msg, DiagnosticId = "SYSLIB0001", UrlFormat = "…/{0}")]` — **jede Deprecation hat ihren eigenen Code** und ist einzeln abschaltbar; „warning waves" staffeln nach `<AnalysisLevel>`. **Kein `since`** — `ObsoleteAttribute` kennt nur `Message`, `IsError`, `DiagnosticId`, `UrlFormat` | Severity ist **nicht** Eigenschaft des Codes, sondern der Konfiguration — genau Lyrics Gegenentscheid; Analyzer-Ökosystem schwer zu überblicken |
| **Java** *(neu)* | `javac` hat keine Codes; die Lint-Kategorien haben Namen (`-Xlint:deprecation`, `-Xlint:removal`) | `@SuppressWarnings("deprecation")` an Deklaration, Parameter oder lokaler Variable — **die feinste Anbringung der Liste**; `-Xlint:-name` global | keine im Compiler; IDEs liefern sie | **`@Deprecated(since="9", forRemoval=true)`** — beide Felder, die Lyrics D13 will, und `-Xlint:removal` macht die Entfernungsankündigung zu einer eigenen, separat schaltbaren Kategorie | ohne Codes ist eine Warnung nur über ihre Kategorie adressierbar; die Kategorien sind grob |
| **Kotlin** *(neu)* | keine stabilen Codes; Warnungen über Namen | `@Suppress("DEPRECATION")` an Ausdruck, Anweisung, Deklaration oder Datei — Reichweite wählbar | **`ReplaceWith` ist IDE-anwendbar**: die Deprecation trägt den Ersatzausdruck als Text mit Importen | **`@Deprecated(message, ReplaceWith("newCall(x)"), level = WARNING\|ERROR\|HIDDEN)`** — die Stufe ist Teil der Deklaration; `WARNING → ERROR → HIDDEN` ist eine Uhr im Attribut | `ReplaceWith` ist ein Mini-Sprachfragment im String und wird nicht typgeprüft; ohne IDE passiert nichts |
| **Swift** | Keine stabilen Codes für den Benutzer; „educational notes" als Prosa | `-warnings-as-errors`; **seit 6.x benannte Warnungsgruppen mit `-Wwarning <group>` / `-Werror <group>` (SE-0443)** — eine Warnung ist dort seitdem einzeln in ihrer *Behandlung* veränderbar, ohne Nummern | Fix-its sind erstklassig und werden von Xcode direkt angewandt | **`-enable-upcoming-feature X`** pro Feature, mit **Migrationsmodus**: die Regel ist noch nicht scharf, der Compiler warnt mit anwendbaren Fix-its. `@available(*, deprecated, renamed:)` trägt den Ersatznamen | Gruppen sind gröber als Codes: eine Gruppe ist zitierbar, eine einzelne Regel nicht. Die Feature-Matrix wird selbst zu einem Konfigurationsproblem |
| **GHC** *(neu)* | **`[GHC-83865]`**, seit 9.6, mit Stabilitätszusage und einer URL je Code (`errors.haskell.org/messages/GHC-83865`) | `-Wno-x`, `-Werror=x`, `{-# OPTIONS_GHC -Wno-x #-}` je Datei; `-Wdefault`, `-Wall`, `-Weverything` als Stufen | Vorschläge in der Prosa, nicht maschinell anwendbar | `{-# DEPRECATED f "msg" #-}`; seit 9.8 mit Kategorie, so dass eine Warnfamilie eigen schaltbar ist | die Codes kamen spät und decken noch nicht alles; die Fehlertexte sind lang |
| **TypeScript** | `TS2322`, numerisch — **de facto stabil, aber ausdrücklich kein öffentlicher Vertrag**: Nummern und Texte dürfen sich zwischen Releases ändern | `// @ts-expect-error` (**Fehler, wenn der Fehler ausbleibt**), `// @ts-ignore` — **zeilenweise, die einzige echte Zeilenreichweite der Liste**; `tsconfig` schaltet Regelfamilien | `tsserver` liefert Codefixes; `tsc` selbst wendet nichts an | `strict`-Familie einzeln einschaltbar; neue Checks kommen als opt-in-Flag und werden später Default | **keine Warnungen** — alles ist Fehler oder unsichtbar; `@ts-ignore` verbirgt auch den nächsten, unbeabsichtigten Fehler, deshalb existiert `@ts-expect-error` |
| **Go** | Keine Codes. Compiler-Fehler in Prosa. **Keine Farbe, kein Farbschalter** | **Keine Unterdrückung, weil es nichts zu unterdrücken gibt**: der Compiler kennt keine Warnungen. Unbenutzte Variable und unbenutzter Import sind **Fehler**. Lints leben in `go vet` / `staticcheck` außerhalb | `gopls` Quickfixes; `go fix` historisch für API-Wanderungen | Deprecation ist eine **Konvention**: `// Deprecated: …`, die der Compiler ignoriert und `gopls`/`staticcheck` lesen | Compiler bricht nach **10** Fehlern ab (`too many errors`; `-gcflags=-e` hebt es auf). Die Konventions-Deprecation hat keinerlei Zwang |
| **Zig** | Keine Codes | **Keine**, vorsätzlich: der Compiler hat keine Warnungen, nur Fehler. Unbenutztes Local ist ein Fehler; eine nie mutierte `var` ist ein Fehler. Verwerfen nur explizit: `_ = x;` | `zig fmt`, keine allgemeinen Fix-its | keine gestaffelte Migration; Bruch kommt als Bruch | Prototyping ist zäh — jede halbfertige Zeile bricht den Build. Genau der Preis, den Zig bewusst zahlt |
| **Elm** | **Keine Codes**, bewusst. Betitelte Abschnitte (`-- TYPE MISMATCH ---`), Prosa, Hinweise mit Links in den offiziellen Guide | **Keine Warnungen** im Compiler; Linting ist `elm-review`, ein eigenes Werkzeug mit eigenen Fix-its | `elm-review --fix` außerhalb des Compilers | keine | Eine Meldung ist nicht zitierbar, nicht abschaltbar, nicht maschinell gruppierbar. Dafür ist sie für Menschen die beste der Liste |

### Was an der ersten Fassung falsch war

1. **Rust `#[deprecated]` + `cargo fix` ist kein Vorbild für einen anwendbaren `replacement`.** Auf
   stable gibt es nur `since` und `note`; ein `suggestion`-Feld existiert hinter dem instabilen
   `deprecated_suggestion` und wird nur std-intern benutzt. `cargo fix` wendet
   `MachineApplicable`-Vorschläge an, und die gewöhnliche Deprecation-Warnung trägt keinen. Das
   Vorbild für diese Zelle ist **Kotlin** (`ReplaceWith`) oder **Swift** (`renamed:`).
2. **C# hat kein `since`.** Das naheliegende Vorbild ist **Java**
   `@Deprecated(since=, forRemoval=)` plus `-Xlint:removal` — es hat beide Felder, die D13 will.
3. **Clang druckt keine URLs.** `-fdiagnostics-show-option` hängt den *Schalter* an
   (`[-Wunused-variable]`) und ist per Default an. Das Vorbild für „URL je Code" ist **GHC** oder
   Roslyns `HelpLink`.
4. **`#pragma clang diagnostic` ist regions-, nicht zeilenbasiert.** Die Eigenschaft, mit der
   D3-C argumentiert („zeilenweise Reichweite ist eng und damit schwer zu missbrauchen"), hat nur
   die TypeScript-Form. Der Clang-Vergleich stützt das Gegenteil.
5. **Swift ist seit 6.2 nicht mehr das Beispiel für „ohne Codes nicht gezielt abschaltbar".**
   SE-0443 bringt benannte Warnungsgruppen mit `-Wwarning`/`-Werror` je Gruppe. Die erste Fassung
   nannte die Gruppen in der Spalte „Unterdrückung" selbst und widersprach sich in der
   Preis-Spalte. Das ist wichtig, weil es eine **dritte Achse** öffnet, die Lyric offensteht:
   benannte Gruppen **neben** stabilen Codes, nicht Codes *oder* Prosa.
6. **C#s `aka.ms`-URL gilt nur für die Obsoletion-Familie**, nicht für die Codes des Compilers.
   „C# hat es gelöst" stimmt für die Teilmenge, die per Attribut ihre URL selbst mitbringt.
7. **Go hat keinen Farbschalter.** Für die Farb-Zelle in D11 tragen nur Clang und rustc.
8. **TypeScript ist kein Beleg für „Codes sind ein Vertrag".** Das Team sagt ausdrücklich zu,
   dass Fehlernummern und -texte kein öffentlicher Vertrag sind. Tragfähig sind **Rust**
   (E-Codes im Error-Index) und **GHC** (seit 9.6 mit Zusage).

### Die Gegenentscheidung

**Zig und Go** haben Lyrics zentrale Entscheidung umgedreht: *es gibt keine Warnungen.* Was
gemeldet wird, bricht. Beide argumentieren gleich — eine Warnung, die man ignorieren kann, wird
ignoriert, und ein Projekt sammelt sie zu Tausenden an. Zig zieht die Linie am schärfsten: genau
die beiden Befunde, die Lyric als `SEM0071` (Warnung) und `SEM0075` (Hint) führt, sind dort
**Fehler**. Was sie gewinnen: kein Lint-Level-System, kein `--deny-warnings`, keine
`#pragma`-Ontologie. Was sie bezahlen: man kann eine Zeile nicht auskommentieren, ohne drei
weitere anzufassen.

**Elm** hat die andere Achse umgedreht: keine Codes. Der Preis ist für Lyric disqualifizierend —
eine Konformanzsuite kann nichts anheften, ein Editor nichts gruppieren, ein zweiter
Implementierer nichts nachbauen. Lyrics §12.1 hat hier richtig entschieden.

**C#** ist die interessanteste Spannung: dort ist die Severity **Konfiguration**, nicht
Eigenschaft des Codes. Genau das verbietet Lyrics §12.1 Regel 1. C# gewinnt damit, dass eine
Organisation `CA1822` zum Fehler erklären kann, ohne den Compiler zu ändern; es bezahlt damit,
dass „was bedeutet `CA1822`" nicht mehr beantwortbar ist, ohne die Build-Konfiguration zu kennen.
Für Lyrics spec-first-Charakter ist Regel 1 richtig — aber sie schließt **nicht** aus, dass man
eine Warnung an einer Stelle *stummschaltet*, ohne ihre Severity zu ändern.

**Kotlin und Java** sind die Sprachen, die Lyrics konkretes Migrationsproblem am nächsten gelöst
haben, und beide fehlten. Kotlin steckt die **Stufe** in die Deklaration
(`WARNING → ERROR → HIDDEN`) statt in einen Compilerschalter; Java steckt die **Frist** hinein
(`since`, `forRemoval`) und macht die Entfernungsankündigung zu einer eigenen Lint-Kategorie. Das
sind zwei verschiedene Antworten auf D13, und beide sind billiger als ein Schaltersystem.

**Die dritte Achse, die keiner Zelle der ersten Fassung aufgefallen ist.** Swift (Gruppen), Clang
(Gruppen), GHC (Gruppen **und** Codes) und Java (Kategorien) zeigen, dass „Code" und „adressierbare
Einheit" nicht dasselbe sein müssen. Lyric hat 228 Codes und **null** Gruppen. Jede Frage dieses
Dossiers, die „Codeliste" als Preis nennt (D3, D4-B, D22), wird billiger, sobald es einen
Gruppennamen gibt, den die Spec führt — siehe **D22**.

---

## 3. Designfragen

**D1–D18** standen in der ersten Fassung; mehrere sind nach der Kritik umgeschrieben.
**D19–D32** sind neu.

### D1 — Bleibt `IR0001` ein Sammelcode?

**Heute:** genau ein Code für die ganze Lowering-Phase, vorsätzlich
(`LoweringDiagnostics.cs:5-19`), Spec §12.1 nennt es „deliberately the ONE code of the area".
**≈108 Emissionsstellen** [gemessen, Aufschlüsselung in 1.2a], davon 80 in einer einzigen Datei.
Der Spec-Eintrag (§A.5) listet als Inhalt zwei Fälle, die beide gefixt sind, und kennt `p.x++`
nicht [gemessen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Sammelcode behalten | heute | Der Katalogeintrag ist nicht pflegbar. Konformanz kann nur „irgendeine Grenze" anheften, nicht welche. Der Benutzer bekommt eine Nummer, die nichts identifiziert. |
| **B** `IR0001` behalten, jede Stelle bekommt eine **stabile Grenzkennung** in der Notiz (`limit: increment-target`) | Clang-Gruppennamen, GHC-Warnkategorien, Roslyn `DiagnosticId` am `Obsolete` | Zwei Identifikatoren pro Meldung. Dafür kann die Spec den Kennungssatz führen, ohne Nummern zu verbrennen, und Kennungen dürfen verschwinden, weil sie nie Codes waren. Bei ~108 Stellen ist das eine Kennung pro Stelle — nur lohnend, wenn vorher **D** die Menge halbiert. |
| **C** Jede Grenze bekommt einen eigenen `IR####` | Rust `E0xxx` | Genau das Problem, das §12.1 vermeiden will. Bei ~108 Stellen bis zu ~108 Nummern, von denen ein guter Teil sterben wird. **Nicht tragbar** — und die erste Fassung hielt es nur für tragbar, weil sie bei 37 rechnete. |
| **D** `IR0001` **teilweise auflösen**: die defensiven Invarianten werden zu `CLI0020` (Compilerfehler), die Programmfehler zu `SEM`, die offene Regel zu einer Migrationswarnung | PR #171 hat drei Fälle genau so verschoben; §12.4 zieht die Linie `IR0001` ↔ `CLI0020` bereits | Arbeit pro Stelle, und die Stichprobe sagt: ~34 defensive, ~5 Sema, 2 offene Regel. Der Rest (~38 echte Grenzen + 8 `NeverNull` + 18 `TypeTable`) bleibt. |

**Empfehlung: D, dann B — aber ohne die Behauptung, der Restsatz werde klein.** Nach der
Stichprobe (1.2a) bleiben **rund 60 echte Grenzen**, nicht „ein paar". Was **D** trotzdem bringt,
ist nicht die Zahl, sondern die **Ehrlichkeit jeder einzelnen Meldung**: eine defensive Invariante
unter `IR0001` sagt dem Benutzer „warte auf ein Release", obwohl sie in Wahrheit „dieser Compiler
ist kaputt" heißt — das ist genau der Fall, für den §12.4 `CLI0020` vorsieht. **B** danach ist ein
Satz von ~60 Kennungen; das ist eine Tabelle, keine Nummernvergabe, und sie darf schrumpfen.

**Bricht:** minor (Codes wandern IR→SEM/CLI; Konformanzfälle müssen umgehängt werden).
**4.x-Warnstufe:** keine nötig — ein Fehler bleibt ein Fehler, nur der Code ändert sich. Der
Wechsel gehört in einen Spec-PR mit `since:`-Gate.
**Hängt an:** D2 (die drei Ausgänge), D19 (`p.x++` ist ein Migrationsfall), Generics
(Monomorphisierungsgrenzen bleiben echte Grenzen).

---

### D2 — Wie viele Ausgänge braucht die Lowering-Ablehnung?

**Heute:** zwei. Die Default-Notiz *„this compiler version cannot lower it yet"*
(`LoweringDiagnostics.cs:28`) hängt an ~100 Stellen; die Ausnahme `NeverNull`
(`LoweringDiagnostics.cs:36`) an 8 [gemessen]. Der Mechanismus für eine abweichende Notiz ist
also da und wird nicht durchgezogen.

> **Korrektur.** Die erste Fassung empfahl „zwei Reporter: `ReportLimit` und `ReportRefusal`".
> Das löst den gemessenen Fall nicht. `p.x++` ist **weder** Grenze **noch** Ablehnung — §6.1 ist
> eine offene Regelfrage (`PLAN.md:184-187`), also genau die Familie, für die seit 4.6 §12.5
> existiert. Es fehlt ein **dritter** Ausgang.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Default beibehalten, Ausnahmen pflegen | heute | Jede neue Stelle erbt die Lüge, bis jemand hinschaut. Bei ~100 Stellen hat niemand hingeschaut. |
| **B** Notiz **pflichtig**: `ReportUnsupported` ohne Notiz-Argument gibt es nicht mehr | Rust: jede Diagnose trägt ihren Grund explizit | ~108 Call-Sites anfassen, einmal. Danach kann keine Stelle mehr versehentlich „warte auf ein Release" sagen. Teuer, aber mechanisch. |
| **C** Drei Reporter: `ReportLimit` (Zukunftsversprechen), `ReportRefusal` (kein Release hilft), `ReportCompilerBug` (→ `CLI0020`) | §12.4 zieht die Linie `IR0001` ↔ `CLI0020` schon | Zwingt die Frage an jeder Stelle und deckt die drei Arten aus der Stichprobe. Deckt **nicht** den vierten Fall: die offene Regel. |
| **D** Wie C, plus: ein Fall, dessen Regel offen ist, gehört **gar nicht** ins Lowering, sondern als Migrationswarnung in die Sema | §12.5, bereits gebaut | `p.x++` würde dann nicht `IR0001` mit falscher Notiz, sondern `SEM01xx` mit „5.0 entscheidet, worauf ein Inkrement stehen darf". Kostet, dass jemand jeden Kandidaten prüft — nach der Stichprobe sind es zwei. |

**Empfehlung: C + D.** Die Unterscheidung „Grenze vs. Ablehnung vs. Compilerfehler" ist genau
die, die §12.4 zwischen `IR0001` und `CLI0020` schon zieht; sie gehört auch **innerhalb** von
`IR0001` gezogen. Der vierte Fall (offene Regel) gehört in die Familie, die es seit 4.6 gibt —
sonst hat Lyric zwei Orte für „das entscheidet 5.0", und einer davon lügt.
**Bricht:** minor (zwei Fälle wechseln von `IR0001` zu einem Sema-Code).
**Hängt an:** D1, D19.

---

### D3 — Gibt es eine Unterdrückung im Quelltext?

**Heute:** nein. Kein `allow`, kein `pragma`, kein Kommentar, kein Attribut
(`19-diagnostics.md:7-9`) [gelesen]. Zwei nachgewiesene Kosten:

1. Eine 5.0-Deprecation kann nicht gesetzt werden, weil die Warnung in den eigenen Tests feuern
   würde (`stdlib/std/iter.lyr:872`) — **eine von fünf Uhren läuft** (1.2e).
2. **Neu, und schwerer:** vier Migrationswarnungen feuern seit 4.6 auf Bestandscode, sind nicht
   abschaltbar, nicht ausnehmbar, und zählen gegen `--deny-warnings` [gemessen, siehe D20].
   `examples/objects.lyr:19` musste dafür auf `mut fn advance()` geändert werden — die Datei
   kommentiert es selbst: *„This example is what the warning found when it was first switched
   on."* [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nichts | Zig, Go, Elm | Die Uhr startet zuletzt statt zuerst. Für `iter` heißt das: die Tests müssen vorher vollständig wandern, und die alte Form verliert ihre Testabdeckung. Für die Migrationsfamilie heißt es: `--deny-warnings` ist für die 4.x-Linie aufgegeben. |
| **B** Attribut am Nutzer: `@Allow { code = "LYR-SEM0076" }` auf Deklaration/Block | Java `@SuppressWarnings` (Deklaration, Parameter, Local), Kotlin `@Suppress` (Ausdruck bis Datei), C# `[SuppressMessage]` | Ein zweiter Mechanismus neben `@Deprecated` — Rule 2 will eine Begründung. Aber es ist **dasselbe** Attributsystem, kein neues. Java zeigt die feinste brauchbare Anbringung. Gefahr: `@Allow { code = "LYR-SEM0071" }` wird zum Wegwerfen von Befunden. |
| **C** Kommentardirektive `// lyric: allow LYR-SEM0076` für **eine** folgende Zeile | **TypeScript `@ts-ignore` — das einzige echte Zeilen-Vorbild der Liste** (Clangs `push/pop` ist regionsbasiert und taugt hier nicht als Beleg) | Erfordert, dass der Lexer Kommentare an Knoten hängt. Zeilenweise Reichweite ist eng und damit schwer zu missbrauchen. |
| **D** Wie C, aber in der **erwartenden** Form: `// lyric: expect LYR-SEM0076` — **Fehler**, wenn die Warnung ausbleibt | TypeScript `@ts-expect-error` | Der beste Teil des TS-Designs: eine abgelaufene Unterdrückung meldet sich selbst und verrottet nicht. **Neuer Preis, den die erste Fassung übersah:** was, wenn der Lauf die Warnung gar nicht gesucht hat? Der `WarningAnalyzer` läuft nicht, sobald irgendein Fehler existiert [gemessen, 1.2l] — ein `expect LYR-SEM0071` würde dann auf **jedem** fehlerhaften Programm zusätzlich feuern. TypeScript hat das Problem nicht, weil es die Kategorie Warnung nicht kennt. |
| **E** Benannte **Gruppen** statt Codeliste: `// lyric: allow deprecation` | Swift SE-0443, Clang `-Wno-x`, Java `-Xlint`, GHC | Eine Gruppe ist stabiler als ein Code und kürzer zu schreiben. Kostet einen zweiten Namensraum neben den Codes — genau der Preis, den Rust bezahlt. Siehe **D22**. |

**Empfehlung: D, beschränkt auf einen in der Spec aufgezählten Codesatz, mit einer ausdrücklichen
Regel für „nicht gesucht".** Nicht jeder Code darf unterdrückt werden — `SEM0076` (deprecated) ja,
`SEM0071` (unused) nein, denn dafür gibt es bereits `_` (`19-diagnostics.md:40`). Die Spec führt
die Spalte „unterdrückbar" im Appendix. Die Regel, die die erste Fassung fehlte: **ein `expect`
meldet nur, wenn der Lauf die Warnung überhaupt suchen konnte** — also nicht, wenn die zugehörige
Analyse übersprungen wurde (siehe D5/D22). Ohne diese Klausel ist D unbrauchbar.

**Rule-2-Kollision, ausdrücklich:** Das ist ein **zweiter Mechanismus** neben `_` für „absichtlich
ungenutzt". Die Begründung, die der ADR tragen muss: `_` ändert das *Programm* (der Name ist weg),
eine Unterdrückung ändert nur die *Meldung* und muss dort greifen, wo der Name gebraucht wird.
Wenn das nicht überzeugt, ist Option A die ehrliche Alternative — dann aber mit der Konsequenz,
`--deny-warnings` für 4.x aufzugeben (D20).

**Bricht:** nein (rein additiv). **Warnstufe:** entfällt.
**Hängt an:** D13 (Deprecation-Codes), D20 (Migrationsfamilie), D22 (Gruppen), D9 (`lyrfix`).

---

### D4 — Bleibt `--deny-warnings` alles-oder-nichts?

**Heute:** ein globaler Schalter **an `lyrc`, `lyrfmt` und `lyrpack`** — und **nicht** am
Projektbau: `lyrbuild proj --deny-warnings` antwortet `error[LYR-CLI0003]: unknown argument`,
Exit 2 [gemessen, mit Kontrolllauf]. Am Projektbau ist `denyWarnings` ein **Artefaktfeld** aus
dem Profil (`BuildSession.cs:11,112,226-231`) [gelesen]. Hints zählen nicht mit [gemessen:
1 Hint + 1 Warnung → „1 warning denied"]. Abschluss ist `CLI0016`, die Severities bleiben
unverändert — das ist ein sauberer Entwurf.

> **Korrektur.** Die erste Fassung behandelte `--deny-warnings` als existierenden CI-Schalter.
> **Für den Pfad, den CI benutzt, existiert er nicht.** Die ganze Frage ist für `lyrbuild` heute
> gegenstandslos, bevor sie beantwortet wird.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Ein Projekt mit einer unvermeidbaren Warnung kann `--deny-warnings` gar nicht führen — und seit 4.6 hat **jedes** Projekt mit einer zweiten Bindung oder einer Klassenmethode ohne `mut` eine. |
| **A′** Erst einmal `--deny-warnings` **an `lyrbuild` und `lyrtest` überhaupt geben** | trivial | Die Vorbedingung für alles andere in dieser Zeile. Ohne sie beantwortet man eine Frage für ein Werkzeug, das in CI niemand aufruft. |
| **B** `--deny-warnings --except LYR-SEM0107` | Clang `-Wno-error=X`, Swift `-Wwarning <group>` | Erste Kante einer Lint-Level-Ontologie. Aber: die Severity ändert sich nicht, nur die Exit-Code-Politik — §12.1 Regel 1 bleibt unangetastet. **Der Fall, der heute real weh tut** (D20). |
| **C** `--cap-warnings`: Warnungen aus **importierten** Modulen zählen nie | Rust `--cap-lints` für Dependencies | Löst „fremder Code warnt, meiner nicht" ohne Codeliste. **Braucht einen Begriff von „fremd", den Lyric nicht hat** — siehe D28. |
| **D** Hints in einen eigenen Schalter (`--deny-hints`) und aus der Default-Ausgabe heraus | Roslyn: `IDE`-Diagnosen sind standardmäßig nur in der IDE sichtbar | Hints sind heute im CLI-Standardstrom [gemessen]. Bei **einem** Hint-Code im ganzen Katalog (1.2r) ist der Gewinn klein — siehe D25. |

**Empfehlung: A′ zuerst, dann B, C erst nach D28.** Die Reihenfolge ist die Korrektur: `A′` ist
Voraussetzung, `B` ist der Fall, der seit 4.6 real blockiert, und `C` löst ein Problem, dessen
Begriff („fremd") noch nicht definiert ist. `D` verschiebt sich hinter D25 — bei einem einzigen
Hint-Code lohnt kein eigener Schalter.

**Bricht:** `A′` nein (additiv), `B` nein, `D` minor (Hints verschwinden aus der CLI-Ausgabe).
**Hängt an:** D20 (die vier nicht abschaltbaren Migrationswarnungen), D28 („fremd"), D25 (Hints),
D30 (Exit-Code).

---

### D5 — Bleibt die Warnungsunterdrückung bei Fehlern, wie sie ist?

**Heute:** die Unterdrückung gilt **nicht für Warnungen allgemein**, sondern für die
`WarningAnalyzer`-Familie. Gemessen, mit Kontrolle (Tabelle 1.2l): ein unbenutztes Local
verschwindet neben einem Fehler, eine Migrationswarnung nicht.

> **Korrektur.** Die erste Fassung schrieb „Warnungen verschwinden, sobald irgendein Fehler
> existiert" und stellte D5-B (*„ein Warnungssatz, der nicht von der Typtabelle abhängt, läuft
> immer"*) als Vorschlag dar. Es ist **bereits eine Ist-Eigenschaft** — nur hat sie niemand
> aufgeschrieben, und sie folgt daraus, in welcher Datei der `Report`-Aufruf steht
> (`WarningAnalyzer.cs` vs. `TypeChecker.cs`/`SemaRules.cs`) [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Die Trennung ist ein Zufall der Codeorganisation. Ein Refactor, der `SEM0107` in den `WarningAnalyzer` verschiebt, ändert stillschweigend das beobachtbare Verhalten — und kein Test fängt es. |
| **B** Die Trennung wird **entschieden und in der Spec festgehalten**, pro Code | Roslyn: syntaktische Analyzer laufen auf kaputten Bäumen | Eine Spalte im Appendix, 13 Zeilen zu füllen. Die Entscheidung selbst ist Arbeit: welcher Befund ist eine Namens-, welcher eine Typaussage. Siehe **D22**. |
| **C** Warnungen laufen immer, werden aber als „möglicherweise Folge eines Fehlers" markiert | Clang druckt Warnungen neben Fehlern | Die Markierung ist selbst eine Behauptung, die der Compiler nicht belegen kann. |

**Empfehlung: B.** Die Begründung von `PLAN.md:192-193` („eine Warnung aus einer halben Tabelle
ist eine Vermutung im Tonfall der Gewissheit") trifft genau die *typabhängigen* Warnungen und
keine anderen — `SEM0072` (ungenutzter Import), `SEM0076` (deprecated), `SEM0077` (Import
verschattet einen eingebauten Namen) und die vier Migrationswarnungen sind Namensbefunde. Die
Entscheidung gehört ins Appendix, nicht in die Dateiaufteilung.
**Bricht:** nein (mehr Ausgabe, kein abgelehntes Programm).
**Hängt an:** D7 (läuft die Sema überhaupt?), D3-D (`expect` braucht die Antwort), **D22**.

---

### D6 — Ist die Reihenfolge der Diagnosen überhaupt Vertrag?

**Heute:** Datei → Span-Start → Span-Ende → Code, ordinal (`DiagnosticsComparer.cs:5-14`), über
`List<T>.Sort` — **instabil** (`DiagnosticEngine.cs:69-74`) [gelesen]. Gemessen: der Folgefehler
`SEM0017` (Zeile 3) steht vor seiner Ursache `SEM0050` (Zeile 4); der lügende `SEM0051`
(Spalte 1) vor dem Parse-Fehler, der ihn erklärt (Spalte 18).

> **Korrektur.** Die erste Fassung schrieb „Bricht: nein — Reihenfolge ist nicht spezifiziert"
> und empfahl dann „B … sonst A, plus D", um zwei Sätze später zu sagen, der gemessene Fall sei
> „kein Sortierproblem". Die Tabelle täuschte eine Entscheidung vor, die gar nicht anstand. Die
> Frage, die ansteht, ist die davor: **ist die Ordnung Vertrag?** — siehe **D24**. Was hier
> bleibt, ist der Folgefehler.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Position, wie heute | Clang, Roslyn | Die erste Zeile ist regelmäßig die falscheste. Bei `p9c` und `p14` gemessen — aber die Ursache ist in beiden Fällen ein **Folgefehler**, kein Sortierfehler. |
| **B** Phase zuerst, dann Position | rustc (Parse-Fehler brechen ab, bevor die Typprüfung läuft) | Der Leser sieht die Ursache oben. Preis: die Ausgabe springt in der Datei. Löst `p9c`, nicht `p14`. |
| **C** Severity zuerst, dann Position | — | Löst keinen der gemessenen Fälle (beide waren Errors). |
| **D** Position behalten, **Folgefehler unterdrücken**: `SEM0017` schweigt, wenn im selben Rumpf ein nicht erschöpfender `match` steht | rustc hängt abgeleitete Befunde als Note an | Verlangt, dass die Sema weiß, welcher Befund aus welchem folgt. Bei `SEM0017`←`SEM0050` lokal wissbar; im Allgemeinen nicht. Eine Handvoll bekannter Paare, keine Theorie. |

**Empfehlung: A behalten, plus D für die bekannten Paare.** Sortierung heilt keine Duplikate.
Der gemessene Schmerz (zwölf Fehler aus einem Zeichen, ein `SEM0017` über eine Funktion, die
zurückgibt) kommt aus D7 und aus Folgefehlern, nicht aus der Ordnung. **B** wird von D7-B
mitentschieden und braucht hier keine eigene Antwort.
**Bricht:** hängt an D24. **Achtung:** Golden-Tests hängen an der Reihenfolge.

---

### D7 — Läuft die Sema weiter, wenn der Parser repariert hat?

**Heute:** ja, und sie lügt dabei. **Reproduziert, zweimal** (1.2c), gegen `STATUS.md:2261`
(„unreproduced") und `PLAN.md:177` („nicht reproduziert"). Der zweite Fall liefert zwölf Fehler
aus einem Zeichen, mit Kontrolllauf.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Jeder Tippfehler, der in einen *anderen gültigen Baum* zurückfällt, erzeugt eine Lawine erfundener Semantikfehler. |
| **B** Bei einem Parse-Fehler wird die Sema gar nicht erst gestartet | rustc, Go | Ein Programm mit einem Syntaxfehler und zwanzig Typfehlern braucht zwei Durchläufe. **Gemessen relevant:** `lyrls` ruft denselben Einstiegspunkt wie `lyrc check` (`AnalysisService.cs:478` → `SourceCompiler.CheckProject`) [gelesen] — was `lyrc check` auf einem kaputten Puffer tut, veröffentlicht der LSP. B nimmt dem Editor also genau das, was er beim Tippen zeigt. |
| **C** Knoten aus Recovery werden **vergiftet**; die Sema überspringt jeden Teilbaum, der einen trägt | Roslyn (`IsMissing` an Tokens, Fehlertypen propagieren) | Die richtige Antwort, und die teuerste: jeder Recovery-Punkt markiert, jeder Sema-Pfad respektiert. |
| **D** Wie C, aber nur auf **Deklarationsebene**: eine Deklaration mit Parse-Fehler wird nicht typgeprüft | — | Deckt beide gemessenen Fälle (`SEM0051` an der Signatur, die Lambda-Lawine im Rumpf) und kostet eine Markierung pro Deklaration statt pro Knoten. **Offene Anschlussfrage:** ist das als `DiagnosticEngine.Mute()`-Scope ausdrückbar, der bereits existiert? Siehe **D32**. |

**Empfehlung: D**, mit C als Fernziel. `B` schließt den LSP-Pfad aus, und das ist jetzt **belegt**
statt behauptet: der LSP fährt denselben Compiler.
**Bricht:** nein — es verschwinden nur Fehler, die das Programm nicht hat.
**Hängt an:** die Grammatikfrage, ob `Typ {` mit einem Trailing-Lambda kollidieren darf
(`Grammar.md:500`) — die saubere Antwort dort macht D7 halb überflüssig. Und **D32**.

---

### D8 — Gibt es eine Fehlerobergrenze?

**Heute:** nein, gemessen (30 unbekannte Identifier → 30 Fehler). Eine 240-KB-Quellzeile erzeugt
**480 KB stderr** [gemessen, `q22_longline.lyr`].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Keine | heute, Roslyn | Ein kaputter Header kann tausende Meldungen erzeugen; die erste scrollt raus. |
| **B** Harte Grenze mit Abschlusszeile | Go (10), Clang (`-ferror-limit`, 20) | Wer alle sehen will, braucht eine Flagge. Gos Grenze bei 10 gilt als zu niedrig (golang/go#5142). |
| **C** Grenze **pro Deklaration** | — | Trifft den gemessenen Pathologiefall genauer: die zwölf Fehler aus `q20_fieldinc.lyr` verteilen sich auf **neun Codes in einer Deklaration** [gemessen]. Ein Cap pro Deklaration kappt die Lawine und lässt den ehrlichen 30-Fehler-Lauf stehen. |

**Die drei Anschlussfragen, die die erste Fassung nicht gestellt hat:**

1. **Exit-Code.** Bleibt ein gekappter Lauf 1, oder bekommt er etwas Eigenes? Siehe D30.
2. **JSON.** Ein gekapptes Dokument ist syntaktisch gültig und stillschweigend unvollständig. Es
   braucht ein `truncated`-Feld, sonst zählt ein Konsument falsch. Siehe D21/D15.
3. **Zählen Warnungen und Hints mit?** Heute gibt es `ErrorCount` und `WarningCount`, keine
   Hint-Zählung (`DiagnosticEngine.cs:11-13`) [gelesen]. Ein Cap, der nur Errors zählt, lässt
   eine Warnungslawine durch.

**Empfehlung: C mit `--max-errors` als globaler Zweitgrenze**, statt B allein. Die erste Fassung
tat C als „Symptombehandlung" ab; die Messung sagt das Gegenteil — die Achse, auf der sich die
Fehler häufen, ist die Deklaration, nicht die Datei. Die drei Anschlussfragen gehören in dieselbe
Entscheidung, sonst baut man den Cap zweimal.
**Bricht:** minor (eine Ausgabe kann kürzer werden). **Warnstufe:** entfällt; die Abschlusszeile
sagt, dass gekappt wurde.
**Hängt an:** D7 (ohne D7 ist der Cap das Pflaster), D30, D21.

---

### D9 — Tragen Diagnosen anwendbare Korrekturen?

**Heute:** nein. JSON kennt `notes` mit `message` und Ort, keinen Ersetzungstext [gemessen]. Kein
`CodeAction` im LSP (null Treffer in `src/Lyric.Lsp/`). `lyrfix` existiert nicht, wird aber von
`PLAN.md:328,369` vorausgesetzt. Zugleich **weiß** der Compiler die Antwort schon: „did you mean
'count'?", „name it '_'", „use executable(name, entry)".

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nichts | Elm (Fixes leben in `elm-review`) | `lyrfix` müsste jede Migration als eigenes Muster neu implementieren — ein zweiter Parser und eine zweite Wahrheit über die Sprache. |
| **B** Notiz bekommt ein optionales `replacement: (Span, string)` plus `applicability: safe / needs-review` | Rust `suggested_replacement` + `applicability`; Clang Fix-it-Hints; Kotlin `ReplaceWith` | Ein Feld am Kernrecord, ein Feld im JSON, ein LSP-`CodeAction`-Anbieter. Setzt voraus, dass eine Notiz eine **Art** hat — siehe **D26**. |
| **C** Vorschläge nur im LSP, nicht im JSON | Swift/Xcode | CI kann nichts anwenden, `lyrfix` bleibt eigenständig. |

> **Korrektur.** Die erste Fassung schrieb, `lyrfix` werde „dann ein 200-Zeilen-Treiber". Das ist
> unbelegt und vermutlich um eine Größenordnung daneben: `cargo fix` braucht
> Überlappungserkennung zwischen Vorschlägen derselben Datei, eine Fixpunktschleife mit
> Abbruchgarantie, eine Prüfung auf schmutzige Arbeitskopie und eine Entscheidung, **welche
> Dateien es anfassen darf** — den letzten Punkt kann Lyric heute nicht beantworten, weil es
> keinen Paketbegriff gibt (D28). Die Kostenaussage ist gestrichen; die Empfehlung bleibt.

**Empfehlung: B, und zwar vor 5.0, nicht danach.** Das ist die **einzige** Frage dieses Gebiets,
an der ein anderes Gebiet hart hängt: jeder 5.0-Bruch aus `PLAN.md:352-361` braucht ein Werkzeug,
und ohne `replacement` baut man dieses Werkzeug zweimal. Rust hat genau diese Reihenfolge gewählt
— erst `MachineApplicable`, dann Editionen.
**Bricht:** nein (JSON-Feld additiv).
**Hängt an:** **D26** (eine Notiz braucht eine Art), D28 (was darf `lyrfix` anfassen), D13, D20.

---

### D10 — Ein Span oder mehrere beschriftete Spans?

**Heute:** ein Hauptspan plus Notizen mit Ort. Ein mehrzeiliger Span bekommt **ein einziges
Caret** — `diagPos.Line == endPos.Line ? Math.Max(Span.Length, 1) : 1`
(`DiagnosticEngine.cs:143-146`) [gelesen]. Eine Notiz kann **nicht** an eine Spalte in derselben
Zeile zeigen, ohne eine zweite Kopfzeile zu erzeugen (`RenderNotes`, `:155-175`) [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | „cannot assign 'int' to 'Rect'" zeigt auf das Argument und nennt weder Parameter noch Funktion. Der Leser sucht die Signatur selbst. |
| **B** Mehrere **beschriftete** Spans pro Diagnose, im selben Snippet gerendert | rustc, Clang | Der Renderer wird deutlich größer: mehrere Zeilen, Gutter mit Zeilennummern, Unterstreichungen mit Labels. Und die `Printable`-Invariante (ein Zeichen raus für ein Zeichen rein) muss für jede zusätzliche Zeile gelten. |
| **C** Nur mehrzeilige Spans korrekt rendern (erste Zeile + `…` + letzte) | — | Billig, behebt die schlechteste Form, ohne den Renderer umzubauen. |

**Empfehlung: C jetzt, B als eigener Slice.** `B` ist der größte Sprung in wahrgenommener
Qualität, den dieses Gebiet zu bieten hat, und der einzige Posten hier, der wirklich Arbeit ist.
**Bricht:** nein (Textausgabe ist nicht spezifiziert; JSON bekommt `spans[]` additiv).
**Achtung:** alle Golden-Tests. **Hängt an:** D26 (ein beschrifteter Span ist eine Notiz mit Art),
D27 (Textinvarianten).

---

### D11 — Wie sieht die Ausgabe aus?

**Heute (gemessen/gelesen):** keine Farbe (`TerminalOutput.cs:15`), keine Zeilenkürzung (480 KB
für **eine** Diagnose), Pfaddarstellung **pro Datei** verschieden, JSON ohne abschließenden
Zeilenumbruch, Erfolgszeile auf stdout auch unter `--json` — aber `--json --quiet` ist sauber
[gemessen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Farbe mit `NO_COLOR`/`CLICOLOR_FORCE` und `--color auto\|always\|never` | **Clang, rustc** (nicht Go — die Go-Toolchain hat keinen Farbschalter und färbt nicht) | **Teurer als die erste Fassung behauptete.** `DiagnosticEngine.Printable` trägt die Invariante „ONE character out for one character in", damit die Caret-Zeile die Spalten des Originals zählt (`DiagnosticEngine.cs:83-90`) [gelesen]. ANSI-Sequenzen **in** die Quellzeile einzufügen bricht jede Längenrechnung des Renderers. Die Färbung muss **um** den ausgegebenen Text herumgelegt werden, in einer Schicht über `Printable`, nicht darin. |
| **B** Zeilenkürzung um den Caret herum, mit `…` | Clang `-fmessage-length`, rustc | Dieselbe Invariante: der Caret muss mitwandern, und die Spaltenzahl in der Kopfzeile bleibt die echte. Kürzen und Färben dürfen sich nicht gegenseitig verschieben. |
| **C** Pfaddarstellung vereinheitlichen | jede | **Nicht zwei Zeilen Code, und die erste Option der ersten Fassung existiert nicht.** Die Regel ist pro Datei: die Einstiegsdatei behält den übergebenen Pfad, jede importierte den aufgelösten [gemessen]. „Immer so, wie der Benutzer es angab" ist für eine importierte Datei nicht definiert — sie wurde nie angegeben. Die umsetzbaren Optionen sind: **immer absolut**, oder **immer relativ zum Projektwurzel/Arbeitsverzeichnis**. |
| **D** ~~`--json` schaltet die Erfolgszeile ab~~ → **`--json` impliziert `--quiet`** | TypeScript `--pretty false` | `--quiet, -q` existiert bereits (`lyrc --help`) und tut genau das [gemessen]. Die Frage ist eine Zeile Treiberlogik, kein Entwurf — **und Rule 2 verlangt es**: zwei Schalter für dasselbe Schweigen wären ein zweiter Mechanismus. |

**Empfehlung: C und D zuerst, A und B als eigener kleiner Slice.** `C` ist eine Entscheidung
(absolut oder projektrelativ), `D` ist eine Zeile. `A` und `B` teilen sich dieselbe
Renderer-Invariante und gehören zusammen gebaut — „ein halber Tag für alle vier" war zu
optimistisch.
**Bricht:** `C` minor (jede Golden-Ausgabe mit Pfad), sonst nein, solange `never` bei umgeleitetem
stderr der Default ist.
**Hängt an:** D10 (derselbe Renderer), D23 (gilt es für alle Werkzeuge?).

---

### D12 — Kann man einen Code nachschlagen?

**Heute:** nur im Spec-Repo (`appendix-a-diagnostics.md`, 227 Zeilen) [gemessen], das ein
Benutzer der Toolchain nicht hat. Kein `explain`, keine URL in der Meldung, **kein
`CodeDescription` im LSP** — `DiagnosticMapper.Map` setzt `Code`, `Message`,
`RelatedInformation`, `Tags` und sonst nichts (`DiagnosticMapper.cs:54-63`) [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** `lyrc explain LYR-SEM0050` liest eine mitgelieferte Katalogdatei | `rustc --explain` | Der Katalog muss ins Paket. **Wie er dorthin kommt, ist eine eigene Frage** — siehe **D29**, denn D16-C lehnt genau die Kopplung ab, die A voraussetzt. |
| **B** URL in der Meldung | **GHC** (`[GHC-83865]` → `errors.haskell.org/messages/GHC-83865`), Roslyn `HelpLinkUri` — **nicht Clang**: `-fdiagnostics-show-option` hängt den Warnungsschalter an, keine URL, und ist per Default an | Eine URL in jeder Zeile ist Lärm. `DiagnosticNote`s Doc-Kommentar warnt ausdrücklich, dass Meldungen „in zwei Richtungen altern" (`DiagnosticNote.cs:7-8`) [gelesen] — und eine URL altert genauso. |
| **C** Nur `codeDescription.href` im LSP | LSP 3.16 | Im Editor klickbar, im Terminal unsichtbar. Billig und ohne Lärm. Setzt eine erreichbare URL voraus, also eine veröffentlichte Katalogseite. |

**Empfehlung: A + C, nach D29.** `A` bringt der Kommandozeile, was `rustc --explain` bringt, und
`C` dem Editor, ohne eine Zeile Terminalausgabe zu verlängern. `B` fällt aus dem Grund weg, den
das Projekt selbst schon aufgeschrieben hat — **aber GHC zeigt, dass es geht**, wenn die URL aus
dem Code mechanisch folgt und nicht im Meldungstext steht.
**Bricht:** nein. **Hängt an:** **D29**.

---

### D13 — Wie sieht `@Deprecated` in v5 aus?

**Heute (gemessen und gelesen):**
- `message` trägt die Meldung, `until` die Version (`stdlib/std/core.lyr:500-510`).
- Angebracht an `[OnModule, OnType, OnFunction]` — **nicht an Membern**. `OnMethod` ist als
  Anker deklariert (`core.lyr:527-530`: *„An anchor for the compiler (4.5) … honoured once the
  attribute rows carry members"*) und noch nicht geehrt [gelesen].
- `SEM0081` an der **Deklaration**, wenn `until` erreicht ist — gut und gemessen.
- `SEM0076` an der Verwendung — nennt `until` **nicht** [gemessen, `p5c`].
- Alle Deprecations teilen **einen** Code, `SEM0076`.
- Kein `renamed`/`replacement`, kein `since`.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Migration bleibt Handarbeit. |
| **B** `until` in die Verwendungswarnung übernehmen | trivial | Keiner. Sollte sowieso passieren. |
| **C** `replacement = "executable(name, entry)"` als **anwendbarer** Vorschlag | **Kotlin `ReplaceWith("…")`** (IDE-anwendbar), **Swift `@available(*, deprecated, renamed:)`** — **nicht Rust**: `#[deprecated]` hat auf stable nur `since` und `note`, und `cargo fix` migriert Deprecations nicht | Verlangt D9 und D26. Zahlt sich bei jeder der fünf 5.0-Entfernungen aus. Kotlins Preis: der Ersatztext ist ein ungeprüftes Sprachfragment im String. |
| **D** `id = "LYR-DEP0003"`: jede Deprecation bekommt ihren eigenen Code | **C# `ObsoleteAttribute.DiagnosticId`** (`SYSLIB0001…`) | Macht Deprecations einzeln unterdrückbar (D3), zählbar, verlinkbar. Preis: ein zweiter Nummernraum — bei C# genau die bekannte Reibung. **Alternative ohne Nummernraum: eine benannte Gruppe** (D22). |
| **E** `since = "4.5"` **und `forRemoval`** | **Java `@Deprecated(since=, forRemoval=true)` + `-Xlint:removal`** — **nicht C#**: `ObsoleteAttribute` kennt nur `Message`, `IsError`, `DiagnosticId`, `UrlFormat` | Java hat beide Felder, die D13 will, **und** macht die Entfernungsankündigung zu einer eigenen schaltbaren Kategorie. Reine Deklaration, kein Compilerschalter. |
| **F** **Stufe in der Deklaration**: `level = warning \| error \| hidden` | **Kotlin `DeprecationLevel`** | Die Uhr läuft im Attribut statt im Compiler. Ein Bibliotheksautor zieht sie selbst an, ohne dass der Benutzer einen Schalter lernt. Kollidiert mit §12.1 Regel 1 (Severity gehört dem Code) — es sei denn, jede Stufe hat ihren eigenen Code. |

**Empfehlung: B + C + E jetzt; D nur, wenn D3 in der beschränkten Form kommt; F ablehnen.** `F`
ist elegant, bricht aber Regel 1 — mit `until` hat Lyric dieselbe Wirkung schon, nur an eine
Version statt an eine Stufe gebunden, und das ist die härtere und ehrlichere Zusage. `E` sollte
Javas **beide** Felder nehmen: `since` allein ist Dokumentation, `forRemoval` ist die Aussage, auf
die ein Werkzeug reagieren kann.

**Neue Anschlussfrage, die aus der Anbringung folgt:** `@Deprecated` kann Module, Typen und
Funktionen markieren, aber **keine Regel** — keine Member-Sichtbarkeit, kein `mut struct`, kein
`defer`-Verhalten. Genau dafür gibt es seit 4.6 §12.5. Ob das ein Mechanismus oder zwei sind,
steht in **D19**.

**Bricht:** nein (alle Felder haben Defaults).
**Hängt an:** D9 (Vorschläge), D26 (Notizart), D3 (Unterdrückung), **D19**, D22.

---

### D14 — Bekommt die Migrationsfamilie einen Schalter — rückwirkend?

> **Vollständig neu gestellt.** Die erste Fassung fragte „Wie schaltet ein Benutzer eine
> 5.0-Regel probeweise ein? — **heute: gar nicht**" und warnte in §5, die Frage müsse *vor* der
> ersten 4.x-Warnstufe entschieden sein, „sonst wird sie als *immer an* gebaut und muss
> zurückgenommen werden". **Sie ist bereits als *immer an* gebaut**, in demselben Binary, gegen
> das gemessen wird.

**Heute [gemessen und gelesen]:** vier Migrationswarnungen, ohne Schalter, ohne Ausnahme:

| Code | Was | Emissionsstelle |
|---|---|---|
| `LYR-SEM0107` | zweite Bindung eines Namens in einem Scope | `TypeChecker.cs:1211` |
| `LYR-SEM0108` | nicht-`mut`-Methode, die `this` auf einer **Klasse** schreibt | `SemaRules.cs:331` |
| `LYR-SEM0109` | Feldschreibung durch eine unveränderliche **Struct**-Bindung | `SemaRules.cs:339` |
| `LYR-SEM0110` | `defer`-Rumpf, der werfen kann | `ExceptionAnalyzer.cs:299` |

Spec-Familie §12.5 (`lyric-spec/spec/12-diagnostics.md:74-102`), Appendix-Zeilen 198–201,
Guide-Abschnitt (`docs/guide/19-diagnostics.md`). §12.5 nennt drei Eigenschaften und eine
Rückzugsregel: *„It retires WITH its rule"* — bei 5.0 wird der Code zurückgezogen, nicht zum
Fehler der neuen Regel umgewidmet [gelesen]. `PLAN.md:374-375` führt `SEM0108` und `SEM0109` als
**„läuft seit 4.6"**.

Gemessen (`q1_rebind.lyr`, `lyrc build`):

```
q1_rebind.lyr:3:5: warning[LYR-SEM0107]: 'x' is already bound in this scope — this binding is
  unreachable today, and 5.0 settles which of the two a later use names
  note: previous binding — q1_rebind.lyr:2:5
  note: a migration warning: the program is legal today, and 5.0 makes it a different program
```

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen: immer an, ohne Schalter | heute | Warnungsmüdigkeit, und in CI: `--deny-warnings` ist für jede betroffene Codebasis rot (gemessen, D20). Der Beleg, dass es Bestandscode trifft, steht im eigenen Repo: `examples/objects.lyr:19`. |
| **B** **`--upcoming <name>=off\|warn\|error`**, pro Regel, dazu `upcoming` in `lyric.json` | **Swift `-enable-upcoming-feature`** mit Migrationsmodus | Jede Regel einzeln; ein Projekt migriert Regel für Regel und hält CI grün. Kostet eine Featureliste, die Spec-Text ist. **Rückwirkend** bedeutet: die vier bestehenden Codes bekommen Namen und einen Default. |
| **C** **Editionen**: `lyric.json` nennt `"language": "5"` | Rust | Mächtiger und deutlich teurer: zwei Semantiken im selben Compiler, für immer. Für Lyrics Größe unverhältnismäßig. |
| **D** Nur die Stufe `off` nachrüsten, kein `error` | — | Löst den CI-Schmerz, ohne eine zweite Semantik zu erzeugen: `off` ändert nur die Meldung, nie das Programm. **Damit bleibt §12.5s Zusage intakt** („NOTHING ELSE CHANGES"). Kein Probelauf der neuen Regel möglich. |
| **E** Nichts am Schalter, aber `--deny-warnings` ausnehmen (D4-B / D20) | — | Die minimale Antwort: die Warnung bleibt sichtbar, blockiert aber CI nicht. Keine Sprachänderung. |

**Empfehlung: D jetzt, B mit 5.0-Vorlauf — und `C` ausdrücklich ablehnen.** `D` ist die einzige
Option, die §12.5s eigene Zusage nicht verletzt: sobald ein `upcoming`-Name die Stufe `error`
erreicht, hat der Compiler zwei Semantiken, und dann ist der Unterschied zu Rust-Editionen allein
die **Befristung** — eine Selbstverpflichtung, kein Mechanismus. Wenn `B` kommt, braucht der ADR
die harte Klausel: **jeder `upcoming`-Name hat eine Version, in der der Compiler ihn ablehnt.**

**Bricht:** `D` nein (nur Meldung), `B` nein (opt-in), `C` major.
**Warnstufe:** *diese Frage war die Warnstufe — sie ist schon gebaut.* Was bleibt, ist der
Rückbau: bekommen `SEM0107`–`SEM0110` rückwirkend Namen und eine Abschaltstufe?
**Hängt an:** D9 (ohne anwendbare Vorschläge ist der Migrationsmodus nur eine höflichere
Warnung), **D19**, **D20**, **D21**, D22.

---

### D15 — Was ist der maschinenlesbare Ausgabevertrag?

**Heute (gemessen):** ein JSON-Dokument am Ende auf stderr, ohne abschließenden Zeilenumbruch;
kein Schema-Feld; `lyrvm` ignoriert `--json` für Load-Fehler und Paniken vollständig; `lyrbuild`
und `lyrtest` **weisen die Flagge zurück** (`CLI0003`, Exit 2); die Erfolgszeile geht als Klartext
auf stdout, außer mit `--quiet`; die Panik trägt ein fünftes Format.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Dokument am Ende, wie heute | TypeScript, Go | Ein langer Lauf zeigt nichts, bis er fertig ist. Für `lyrc` egal, für `lyrbuild` über zwanzig Artefakte nicht. |
| **B** **NDJSON**: eine Diagnose pro Zeile, am Ende eine Summenzeile | Rust `--error-format=json` | Der Konsument kann streamen; ein abgebrochener Lauf liefert trotzdem, was er hatte. Bricht jeden heutigen Parser. |
| **C** Dokument behalten, `"version": 1` ergänzen, **`lyrbuild`/`lyrtest`/`lyrvm` nachziehen**, Panik als reguläre Diagnose mit `"kind": "panic"` | — | Additiv bis auf die Panikform. Löst (g) und (h). **Der teuerste Teil ist nicht das Schema, sondern die drei Werkzeuge** — siehe D23. |
| **D** SARIF als zweites Format | Roslyn `/errorlog` | Anschluss an GitHub Code Scanning ohne eigenen Konverter. Rule-2-verdächtig — aber es ist ein *Ausgabeformat*, kein Sprachmechanismus. |

**Empfehlung: C jetzt, B mit 5.0.** `C` schließt die gemessenen Löcher. `B` ist die richtige
Endform und darf auf den Major warten, weil es bricht. `D` nur, wenn jemand es braucht.
**Neue Pflichtfelder, die C mitnehmen muss:** `kind` (D21), `truncated` (D8), `spans[]` (D10),
`replacement`/`applicability` (D9).
**Bricht:** `C` minor (Panikformat), `B` major.
**4.x-Warnstufe:** `--json=stream` als opt-in ab 4.x, Default-Wechsel mit 5.0.
**Hängt an:** **D23**, D21, D8, D9, D10.

---

### D16 — Wie wird die Vollständigkeit des Katalogs erzwungen?

> **Vollständig neu gestellt.** Die erste Fassung sagte „Es gibt keinen Test, der das erzwingt —
> es ist Disziplin" und empfahl als Option B „ein Test liest jede `LYR-*`-Konstante und vergleicht
> mit Appendix A". **Der Test existiert und ist genau diese Empfehlung.** So, wie D16 dastand,
> hätte man den Wächter ein zweites Mal gebaut und den Fehler, den er nicht sieht, erneut nicht
> gesehen.

**Heute [gelesen, `tests/Lyric.Tests.Cli/DiagnosticCatalogueTests.cs`, 153 Zeilen]:**

- `Every_code_the_implementation_emits_stands_in_the_appendix` (`:97-116`) liest jedes
  `LYR-*`-String-Literal aus `src/**/*.cs` und vergleicht mit `appendix-a-diagnostics.md`.
- `No_code_is_reported_at_two_severities` (`:126-152`) erzwingt §12.1 Regel 1 und braucht keinen
  Spec-Checkout.
- Der Klassenkommentar nennt, was der Test beim Schreiben fand: sieben Codes ohne Spec, drei davon
  mit zwei oder drei unverwandten Regeln unter einer Nummer.

**Drei Löcher, alle belegt:**

1. **Der Bereichssatz ist geschlossen.** Die Regex lautet
   `LYR-(LEX|PAR|RES|SEM|IR|CLI|BC|CAP|VM|EMB)\d{4}` (`:34`) — **`CT` fehlt**. `LYR-CT0001` und
   `LYR-CT0002` sind für den Wächter unsichtbar, und genau sie sind der Fehler, der schon
   passiert ist [gelesen + gemessen].
2. **Nur eine Richtung.** Der Test prüft *emittiert → Appendix*, nie *Appendix → emittiert*. Ein
   Code, der im Katalog steht und nirgends mehr gemeldet wird, fällt nicht auf.
3. **Stiller Pass.** Ohne Spec-Checkout gibt der Test `return;` zurück (`:100`) — grün, nicht
   übersprungen. Der Kommentar (`:48-50`) nennt das Absicht und verweist auf „the CI job that
   matters"; ob dieser Job existiert, ist nicht in der Datei prüfbar.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Zwei der drei Löcher haben bereits Schaden angerichtet. |
| **B** Bereichssatz **öffnen**: `LYR-[A-Z]{2,3}\d{4}` und bei unbekanntem Bereich **rot** werden | — | Fünf Zeichen. Fängt `CT` und jeden künftigen Bereich. Der Test wird beim ersten Lauf rot — das ist der Punkt. |
| **C** Gegenrichtung ergänzen: ein dokumentierter, nie emittierter Code ist ein Befund | Rust: der Error-Index wird gegen die Emission geprüft | Braucht eine Ausnahmeliste für zurückgezogene Nummern — die gibt es schon (§A.11). |
| **D** Stillen Pass beseitigen: echtes `Skip`, plus ein CI-Gate, das den Spec-Checkout erzwingt | — | Die Disziplin wird sichtbar statt vorausgesetzt. |
| **E** Codes **nur** noch aus generierten Konstanten, die aus der Spec erzeugt werden | — | Spec-first bis zur letzten Konsequenz. Koppelt den Build an das Spec-Repo — und das ist genau der Schritt, den D12-A braucht. Siehe **D29**. |

**Empfehlung: B + C + D.** Alle drei sind klein, und zusammen machen sie aus dem Wächter, der
existiert, den Wächter, der die Sache hält. `E` gehört nicht hierher, sondern nach D29, wo der
Widerspruch zwischen „der Katalog muss ins Paket" und „koppelt zwei Repos" ausgetragen wird.
**Bricht:** nein. **Hängt an:** **D29**, D31 (`CT` als Bereich).

---

### D17 — Warnt v5 vor totem Code?

**Heute:** nein für Deklarationen. Gemessen (`p17`): eine nie gerufene private `fn` und ein nie
benutzter `struct` → `ok`. Gewarnt wird über Locals, Loop-, Catch- und Pattern-Bindungen
(`SEM0071`), ungenutzte Importe (`SEM0072`) und unerreichbare Anweisungen (`SEM0073`)
(`19-diagnostics.md:30-34`) [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nichts | heute | Eine tote private Funktion überlebt jeden Refactor. |
| **B** Warnung für nicht-`pub` Deklarationen ohne Nutzung | Rust `dead_code`, Roslyn `IDE0051` | Falschpositive bei attributtragenden Funktionen — aber die kennt der Compiler (`15-attributes.md:225-227`: eine Funktion mit Attribut wird nie als tot entfernt). Die Ausnahme ist bereits formuliert. |
| **C** Fehler statt Warnung | Go (nur Importe/Locals), Zig | Für Deklarationen zu hart: während der Entwicklung schreibt man den Aufrufer nach der Funktion. |

**Empfehlung: B.** Der Sonderfall, der es schwer machen würde, ist bereits entschieden.
**Neu, nach D14:** ein Kandidat für `--upcoming dead-code=warn` **nur, wenn D14-B kommt** — mit
D14-D (nur `off`) ist eine neue Warnung schlicht eine neue Warnung, und sie trifft jede
Codebasis am Tag der Einführung.
**Bricht:** nein (Warnung), minor mit `--deny-warnings`.
**Hängt an:** D4, D14, D22 (gehört sie in eine abschaltbare Gruppe?).

---

### D18 — Ist `panic [CODE]` eine Diagnose?

**Heute:** nein, es ist ein fünftes Format neben den vier Severities [gemessen]. Es trägt Code,
Meldung und Backtrace, aber keine Severity, keinen Span im Diagnose-Sinn, und es geht an `--json`
vorbei [gemessen: `lyrvm run --json` druckt Klartext]. Appendix A führt 16 `panic`-Zeilen ohne
Severity [gemessen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Ein Werkzeug braucht zwei Parser für dieselbe Toolchain. |
| **B** Panik wird `error[LYR-VM0002]` mit Frames als Notizen | — | Das Wort „panic" verschwindet — es ist aber genau das Wort, das „das Programm lief und brach ab" von „es wurde abgelehnt" trennt. |
| **C** Eigene fünfte Kategorie, aber **im selben Vertrag**: `"kind": "panic"` im JSON, Text bleibt | — | Ehrlich zu beidem: die Form bleibt lesbar, der Strom wird einheitlich. **Dasselbe `kind`-Feld, das D21 für Migrationswarnungen braucht** — ein Mechanismus, zwei Nutzer. |

**Empfehlung: C.** Der Unterschied Übersetzung/Laufzeit ist echt und sollte sichtbar bleiben; was
nicht bleiben darf, ist dass `--json` ihn stumm ignoriert. Dass `kind` zugleich D21 bedient, ist
das Rule-2-Argument dafür.
**Bricht:** minor (JSON-Konsumenten sehen ein neues Feld).
**Hängt an:** D15, **D21**, D23.

---

### D19 — Sind §12.5-Migrationswarnungen und `@Deprecated` ein Mechanismus oder zwei? *(neu)*

**Heute:** zwei Familien, die dasselbe Konzept tragen — *„das hier ändert sich mit 5.0"*:

| | `@Deprecated` → `SEM0076`/`SEM0081` | §12.5 → `SEM0107`–`SEM0110` |
|---|---|---|
| Anbringung | Attribut an Modul, Typ, Funktion (`core.lyr:500`) | keine — der Compiler kennt die Regel |
| Frist | `until` als Versionsstring, erzwungen (`SEM0081`) | die Zielversion steht im Meldungstext |
| Adressierbar | ein Code für alle | ein Code pro Frage |
| Unterdrückbar | nein | nein |
| Rückzug | die Deklaration verschwindet mit dem Major | *„It retires WITH its rule"* (§12.5) |

Beide [gelesen]. Der Grund für die Doppelung ist **strukturell und nicht behebbar durch
Weglassen**: `@Deprecated` ist ein Attribut an einer **Deklaration** und kann deshalb eine
**Regel** — Member-Sichtbarkeit, `mut struct`, `defer`-Semantik, worauf ein Inkrement stehen darf
— überhaupt nicht markieren. Es gibt nichts, woran man es schreiben könnte.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Zwei Familien, getrennte Regeln, so wie heute | Java trennt `@Deprecated` (Deklaration) von `-Xlint`-Kategorien (Regeln) | Ehrlich, aber Rule 2 verlangt die Begründung im ADR. Und der Leser sieht zwei Meldungsformen für dieselbe Sorge. |
| **B** **Eine** Familie mit zwei Anbringungsarten: „Migrationsbefund", entweder aus einem Attribut oder aus einer Regel | Kotlin: `@Deprecated` **und** `-Xsuppress`-Kategorien tragen dieselbe Stufenlogik | Ein Mechanismus, eine Spalte im Appendix, ein `kind` im JSON (D21), eine Unterdrückungsregel (D3). Kostet, dass `SEM0076` und `SEM0107`–`SEM0110` gemeinsame Felder bekommen: Zielversion, Ersatz, Gruppe. |
| **C** `@Deprecated` **auf Regeln ausdehnen**, etwa `@Deprecated` an einer Sprachkonstruktion | — | Geht nicht: es gibt keinen Ort im Programm, an dem „Struct-Felder sind ab 5.0 unveränderlich" steht. Verworfen. |
| **D** Die Migrationsfamilie in `@Deprecated` **auflösen**, sobald `OnMethod` geehrt wird | — | Deckt `SEM0108` (Methode) vielleicht, nicht `SEM0107` (zweite Bindung) und nicht `SEM0110` (`defer`). Verworfen. |

**Empfehlung: B.** Die gemeinsamen Felder sind genau die, die dieses Gebiet ohnehin braucht:
**Zielversion** (D13-E, D21), **Ersatz** (D9, D13-C), **Gruppe** (D22), **unterdrückbar ja/nein**
(D3, D20). Wenn beide Familien sie tragen, ist es ein Mechanismus mit zwei Anbringungen; wenn
nur eine sie trägt, hat Lyric zwei halbe.

**Rule-2-Kollision, ausdrücklich:** das ist die **real ausgelieferte** Kollision dieses Gebiets —
die erste Fassung nannte in §5 nur D3 und D14 und hat diese übersehen. Der ADR muss sie benennen,
egal welche Option gewinnt.

**Bricht:** `B` nein, wenn die neuen Felder Defaults haben.
**Hängt an:** D13, D20, D21, D22, D9.

---

### D20 — Darf eine Migrationswarnung unterdrückt werden — und was passiert einem `denyWarnings`-Projekt beim Sprung auf 4.6? *(neu)*

**Heute [gemessen, mit Kontrolllauf]:**

| Lauf | Ergebnis |
|---|---|
| `lyrc build q1_rebind.lyr --deny-warnings` | `error[LYR-CLI0016]: 1 warning denied by --deny-warnings`, **Exit 1** |
| `lyrc build q1_rebind.lyr` (Kontrolle) | Warnung, Artefakt geschrieben, Exit 0 |
| `lyrbuild proj --deny-warnings` | `error[LYR-CLI0003]: unknown argument`, **Exit 2** — den Schalter gibt es dort nicht |

Die vier Warnungen sind **nicht abschaltbar** (D3 existiert nicht), **nicht ausnehmbar** (D4-B
existiert nicht) und zählen voll gegen den CI-Schalter, wo es ihn gibt. Dass sie realen
Bestandscode treffen, belegt das Repo selbst: `examples/objects.lyr:19` wurde auf
`mut fn advance()` geändert, mit dem Kommentar *„This example is what the warning found when it
was first switched on"* [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** Nicht unterdrückbar, `--deny-warnings` bleibt | Zig, Go | Für die gesamte 4.x-Linie ist `--deny-warnings` in jedem betroffenen Projekt aufgegeben. Das ist eine Entscheidung; sie muss nur getroffen und geschrieben sein. |
| **B** Migrationswarnungen zählen **nie** gegen `--deny-warnings` | Rust `--cap-lints` für die Dependency-Seite; Javas `-Xlint:removal` als eigene Kategorie | Eine Zeile im Zähler, kein Sprachmechanismus. Die Warnung bleibt sichtbar. **Preis: die Warnung verliert ihren einzigen Zwang** — wer sie ignoriert, merkt nichts. |
| **C** Einzeln ausnehmbar: `--deny-warnings --except LYR-SEM0107` | Clang `-Wno-error=X`, Swift `-Wwarning <group>` | Erste Kante einer Lint-Ontologie, aber ohne Severity-Änderung. Verlangt, dass die Spec sagt, welche Codes ausnehmbar sind. |
| **D** Als **Gruppe** ausnehmbar: `--deny-warnings --except migration` | Swift SE-0443, Java `-Xlint:removal`, GHC-Kategorien | Kürzer als eine Codeliste und stabil gegen neue Mitglieder der Familie. Verlangt D22. |
| **E** Im Quelltext unterdrückbar (D3) | TypeScript, Kotlin, Java | Die feinste Lösung, und die teuerste: sie braucht D3 vollständig. |

**Empfehlung: D, mit B als Übergang bis D22 steht.** Die Familie ist als Gruppe schon definiert —
§12.5 zählt sie auf — und eine Gruppe ist genau das, was ein CI-Schalter ausnehmen können sollte.
`A` ist die ehrliche Alternative, aber dann muss `PLAN.md`/`STATUS.md` ausdrücklich sagen, dass
`--deny-warnings` für 4.x nicht mehr geführt werden kann. **Diese Frage muss beantwortet sein,
bevor D4-A′ `--deny-warnings` an `lyrbuild` gibt** — sonst schaltet man einen Schalter scharf, der
sofort rot ist.

**Bricht:** `B`/`D` nein (weniger Ablehnung). **Warnstufe:** entfällt.
**Hängt an:** D3, D4, D14, D19, **D22**, D30.

---

### D21 — Woran erkennt ein Werkzeug eine Migrationswarnung im JSON? *(neu)*

**Heute [gemessen, `lyrc check q13b_control.lyr --json --quiet`]:**

```json
{"code":"LYR-SEM0107","severity":"warning","file":"…","start":{…},"end":{…},
 "notes":[{"file":"…","start":{…},"end":{…},"message":"previous binding"},
          {"message":"a migration warning: the program is legal today, and 5.0 makes it a different program"}],
 "message":"'x' is already bound in this scope — …"}
```

Kein `kind`, keine `family`, keine Zielversion als Feld. Ein Konsument muss den **Notiztext
string-matchen** oder eine Codeliste fest verdrahten. Beides bricht beim nächsten Release —
§12.1 Regel 3 sagt ausdrücklich, der Meldungstext dürfe besser werden.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Jedes Werkzeug, das Migrationswarnungen zählen oder ausblenden will, verdrahtet vier Nummern. Beim fünften Mitglied bricht es still. |
| **B** `"kind": "migration"` am Diagnostic | **Dasselbe Feld, das D18-C für `"kind": "panic"` braucht** | Ein Feld, zwei Nutzer — das Rule-2-Argument. Kostet eine Aufzählung in der Spec, die geschlossen bleiben muss. |
| **C** `"settledIn": "5.0"` zusätzlich | Java `forRemoval` + `since`; C# `SYSLIB`-Obsoletion trägt ihre Version im Text, nicht im Feld | Das handlungsleitende Datum wird maschinenlesbar. Ein Werkzeug kann sagen „drei Befunde, die dein nächster Major bricht". Auch D13-B (das verschwiegene `until`) profitiert. |
| **D** `"group": "migration"` statt `kind` | Swift/GHC/Java-Kategorien | Allgemeiner: dieselbe Spalte trägt später `deprecation`, `unused`, `unreachable`. Siehe D22. |

**Empfehlung: B + C + D — aber als *ein* Entwurf.** `kind` beantwortet „was für ein Ding ist
das" (Diagnose, Panik), `group` beantwortet „zu welcher Familie gehört es" (migration,
deprecation, unused), `settledIn` beantwortet „ab wann". Drei Felder, drei verschiedene Fragen;
sie einzeln nachzurüsten ist der Weg, auf dem ein Schema unordentlich wird.

**Bricht:** nein (additiv; Konsumenten, die die Felder nicht kennen, ignorieren sie).
**Hängt an:** D15 (Vertrag), D18 (`kind`), **D22** (`group`), D13-E (`since`/`settledIn`), D23.

---

### D22 — Welcher Warncode steht auf welcher Seite, und wer hält das fest? *(neu)*

**Heute:** implizit entschieden, durch die Dateizugehörigkeit des `Report`-Aufrufs, und nirgends
aufgeschrieben. Gemessen (1.2l): `SEM0071` schweigt neben einem Fehler, `SEM0107` nicht. Im
Quelltext liegt der Unterschied zwischen `WarningAnalyzer.cs` und
`TypeChecker.cs`/`SemaRules.cs`/`ExceptionAnalyzer.cs` [gelesen].

Zu entscheiden sind **13 Zeilen** — 12 `W` und 1 `H` in Appendix A [gemessen]:

| Code | Was | Namens- oder Typbefund? |
|---|---|---|
| `SEM0071` | Bindung nie benutzt | Namensbefund, aber aus einer vollständigen Referenztabelle |
| `SEM0072` | Import nie benutzt | Namensbefund |
| `SEM0073` | unerreichbare Anweisung | Flussbefund |
| `SEM0075` (H) | `var` nie neu zugewiesen | Namens-/Flussbefund |
| `SEM0076` | `@Deprecated` verwendet | Namensbefund |
| `SEM0077` | Import verschattet einen eingebauten Typnamen | **Namensbefund — und der Fall, der `PLAN.md:195-197` aufgefallen ist** |
| `SEM0103`, `SEM0104` | (Appendix A) | zu prüfen |
| `SEM0107`–`SEM0110` | Migrationsfamilie | läuft heute schon immer |
| `CLI0017` | (Appendix A) | Treiberbefund, keine Sema |

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Ein Refactor, der einen Report in eine andere Datei schiebt, ändert das beobachtbare Verhalten. Kein Test fängt es, und die Spec sagt nichts. |
| **B** Spalte im Appendix: „läuft auch bei Fehlern" | — | 13 Entscheidungen, danach eine Zeile Spec. Prüfbar durch einen Test wie den Katalogwächter. |
| **C** Benannte **Gruppen** einführen, und die Eigenschaft hängt an der Gruppe | **Swift SE-0443, Clang `-W…`, Java `-Xlint:…`, GHC-Kategorien** | Eine Gruppe trägt dann *drei* Eigenschaften auf einmal: läuft-bei-Fehlern, unterdrückbar (D3), ausnehmbar (D20). Preis: ein zweiter Namensraum neben den Codes — genau Rusts bekannte Reibung. |
| **D** Alle Warnungen laufen immer, die Analyse wird fehlertolerant gemacht | Roslyn | Die teuerste Antwort, und sie widerspricht `PLAN.md:192-193` aus gutem Grund. |

**Empfehlung: C, mit B als Ergebnis davon.** Die Gruppe ist die Einheit, die dieses Gebiet an
vier Stellen braucht (D3, D4-B, D20, D22) und heute an jeder einzeln durch eine Codeliste ersetzt
wird. **Vier Codelisten sind vier Mechanismen; eine Gruppentabelle ist einer.** Das ist das
Rule-2-Argument dafür, den zweiten Namensraum zu akzeptieren — Lyric hat 228 Codes und null
Gruppen, und jede Frage, die „Codeliste" als Preis nennt, wird damit billiger.

**Bricht:** nein (Gruppennamen sind additiv).
**Hängt an:** D3, D4, D5, D20, D27 (ein Test hält die Spalte).

---

### D23 — Gilt der Diagnostikvertrag für den Compiler oder für die Toolchain? *(neu)*

**Heute:** für `lyrc` — und je nach Frage noch für ein oder zwei weitere. `src/` enthält **17
Projekte**, darunter 13 Werkzeuge [gemessen, `ls src/`]. Stand `--json` [gemessen]:

| Werkzeug | `--json` |
|---|---|
| `lyrc` | ja, vollständig für Diagnosen |
| `lyrfmt`, `lyrpack` | in der Hilfe angekündigt: *„Diagnostics as JSON on stderr"* |
| `lyrvm` | angekündigt (*„Diagnostics (and 'info') as JSON"*), **nur `info` bedient** |
| `lyrbuild`, `lyrtest` | **zurückgewiesen**: `CLI0003`, Exit 2 |
| `lyrls`, `lyrdbg`, `lyrrepl`, `lyrstub` | nicht betrachtet |

`ExitCodes` (`src/Lyric.Core/ExitCodes.cs:3-7`) nennt sich selbst *„normativ für jede
Runtime"* und liegt in dem Projekt, das `lyrc`, `lyrvm` und `lyric` teilen [gelesen] — die
Exit-Codes sind also schon Toolchain-weit gedacht, die Ausgabeform nicht.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Der Vertrag gilt dem **Compiler**; jedes Werkzeug entscheidet selbst | Go: `go vet` und `go build` haben verschiedene Ausgaben | Ehrlich, aber dann muss die Hilfe von `lyrvm` aufhören, etwas zu versprechen, was sie nicht hält. |
| **B** Der Vertrag gilt der **Toolchain**: wer Diagnosen druckt, druckt sie so, und `--json` bedeutet überall dasselbe | Rust: `rustc` und `cargo` teilen `--message-format` | Der Nutzen ist genau da, wo es heute fehlt: CI ruft `lyrbuild` und `lyrtest`, nicht `lyrc`. Preis: vier bis sechs Werkzeuge nachziehen. |
| **C** Wie B, plus ein **Test über die Ausgabeformen** — der Katalogwächter für die Toolchain | `DiagnosticCatalogueTests` als Muster | Erzwingt es statt es zu hoffen. Ein Test, der jedes Werkzeug mit `--json` auf ein kaputtes Eingabeprogramm wirft und prüft, dass etwas Parsebares herauskommt, ist klein. |

**Empfehlung: B + C.** Die Frage ist nicht theoretisch: **D4 und D15 sind heute nur für den
Einzeldatei-Pfad beantwortet, den in CI niemand benutzt.** Ein Vertrag, der für das Werkzeug gilt,
das man von Hand aufruft, und nicht für das, das die Pipeline aufruft, ist der falsche Weg herum.
**Bricht:** nein (additiv), außer dort, wo ein Werkzeug heute Text druckt und dann JSON druckt.
**Hängt an:** D15, D11, D21, D30.

---

### D24 — Ist die Reihenfolge der Diagnosen Teil des Vertrags? *(neu)*

**Heute:** **nicht total, und nicht spezifiziert.** `DiagnosticEngine.SortedSnapshot`
(`:69-74`) ruft `List<T>.Sort` — in .NET ausdrücklich ein **instabiler** Introsort — mit
`DiagnosticsComparer`, dessen Schlüssel Datei → Span-Start → Span-Ende → Code sind
(`DiagnosticsComparer.cs:5-14`) [gelesen]. Zwei Diagnosen mit gleicher Datei, gleichem Span und
gleichem Code haben eine **implementierungsdefinierte** Reihenfolge. Golden-Tests und
Konformanzfälle hängen daran.

Der Fall ist nicht konstruiert: gemessen in `q20_fieldinc.lyr` stehen `SEM0045` zweimal an
derselben Position mit verschiedenen Meldungen (Spalte 15) — die Spans unterscheiden sich dort
noch (`^` vs. `^^^^`), aber die Emission zweier gleicher Codes an einer Stelle ist genau das,
was `_reportedThrows` (`TypeChecker.cs:31`) an anderer Stelle verhindern muss.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Ausdrücklich **unspezifiziert** erklären | rustc gibt keine Ordnungsgarantie | Ehrlich. Dann dürfen Konformanzfälle die Reihenfolge nicht prüfen, und Golden-Tests müssen sortiert vergleichen. Das ist Arbeit an den Tests, nicht am Compiler. |
| **B** Ordnung **total** machen: Tie-Breaker auf den Meldungstext, ordinal | — | Zwei Zeilen. Danach ist die Ausgabe reproduzierbar, auch über Plattformen. Der Meldungstext ist allerdings §12.1 Regel 3 — er darf besser werden, und dann ändert sich die Reihenfolge. |
| **C** Total über einen **Emissionszähler** (stabile Sortierung nach Einfügereihenfolge) | Roslyn behält die Reihenfolge innerhalb einer Phase | Ein `int` pro Diagnose. Total, deterministisch, und unabhängig vom Meldungstext. `List<T>.Sort` bleibt, der Comparer bekommt einen letzten Schlüssel. |
| **D** Reihenfolge wird normativ: §12 beschreibt sie | — | Bindet einen zweiten Implementierer an eine Darstellungsentscheidung. Für eine Konformanzsuite bequem, für die Sprache zu viel. |

**Empfehlung: C, und in §12 als *deterministisch, aber nicht normativ* festhalten.** Das ist die
Kombination, die Golden-Tests trägt, ohne einem zweiten Implementierer die Reihenfolge
vorzuschreiben: derselbe Compiler gibt zweimal dasselbe aus, ein anderer darf anders sortieren.
`B` ist schlechter, weil es die Ordnung an einen Text bindet, der sich ändern darf.
**Bricht:** nein (heute ist nichts zugesichert).
**Hängt an:** D6, D10 (mehr Spans, mehr Gleichstände), D27.

---

### D25 — Überlebt `Severity.Info` den Major? *(neu)*

**Heute:** `Severity.Info` kommt in `src/` **genau einmal** vor, und das ist eine Abbildung im
LSP (`DiagnosticMapper.cs:110`), keine Emission [gemessen]. Appendix A hat **null** `I`-Zeilen,
**eine** `H`-Zeile (`SEM0075`) und **zwölf** `W`-Zeilen bei 227 Katalogzeilen [gemessen]. Die
erste Fassung öffnete mit *„Vier Severities … was gut ist und v5 nicht anfassen sollte"* — eine
Severity ohne einen einzigen Code ist aber kein Modell, sondern eine leere Verzweigung.

`ToSeverity` im LSP ist absichtlich total und wirft bei einer unbekannten Severity
(`DiagnosticMapper.cs:114-118`, Kommentar: *„A new one is a decision about how an editor should
draw it"*) [gelesen] — das ist gute Arbeit, macht aber die leere Verzweigung nicht voller.

| Option | Vorbild | Preis |
|---|---|---|
| **A** Alle vier behalten | LSP hat vier | Eine Severity, die nie auftritt, ist eine Zusage an einen zweiten Implementierer, die niemand einlöst. Die Frage „was ist der Unterschied zwischen Info und Hint" hat heute keine Antwort im Katalog. |
| **B** `Info` streichen, drei Severities | Clang (error/warning/note), Go (nur error) | Ein Enum-Wert weniger, eine LSP-Abbildung weniger, eine Spec-Zeile weniger. Bricht jeden Embedder, der über die Severity schaltet — **major**. |
| **C** `Info` **füllen**: ein Satz Codes, der neutral berichtet (etwa „diese Datei wurde aus dem Cache geladen", „N Instanzen monomorphisiert") | Roslyn `IDE`-Diagnosen | Gibt der Severity einen Inhalt. Aber neutrale Information gehört nach stdout und in `--verbose`, nicht in den Diagnosestrom. |
| **D** `Info` **und** `Hint` zu einer Severity zusammenlegen | Swift kennt `note` als einzige Unterstufe | Zwei leere bzw. fast leere Stufen werden eine, die einen Code hat. Ebenfalls major. |

**Empfehlung: B mit 5.0, und bis dahin ausdrücklich als „reserviert, nicht benutzt" in §12
markieren.** Die halbe Skala trägt heute **einen** Code; wenn D4-D die Hints außerdem aus der
CLI-Ausgabe nimmt, bleibt von zwei Stufen keine sichtbare übrig. Entweder man füllt sie oder man
streicht sie — offenlassen ist die einzige Option, die einen zweiten Implementierer in die Irre
führt.
**Bricht:** `B` major (Embedding-API; `Severity` ist öffentlich in `Lyric.Core`).
**4.x-Warnstufe:** eine Notiz in §12 und im Guide; kein Code betroffen, weil es keinen gibt.
**Hängt an:** D4-D (Hints in der CLI), D17 (falls neue Hints kommen), Embedding-Gebiet.

---

### D26 — Was ist eine Notiz? *(neu)*

**Heute:** `(Location, Message)` und sonst nichts (`DiagnosticNote.cs:12`). Der Doc-Kommentar
beschreibt zwei Verwendungen — *„another PLACE that belongs to the same finding … or a remark with
no place at all"* (`:3-8`) — aber nichts im Typ unterscheidet sie. Der LSP macht aus **jeder**
Notiz `relatedInformation`, uniform, und hängt eine ortlose Notiz an den Span der Diagnose
(`DiagnosticMapper.cs:78-103`) [gelesen]. Gemessen an `SEM0107`: die erste Notiz ist ein Ort
(`previous binding`), die zweite eine Kategorie (`a migration warning: …`) — im JSON
unterscheidbar nur daran, dass die eine `file`/`start`/`end` hat.

rustc, Clang und Roslyn unterscheiden **`note`** (ein anderer Ort), **`help`** (was zu tun ist)
und **`suggestion`** (anwendbarer Text). Lyric hat eines für alles.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | D9 (`replacement`) und D2 (Grenze vs. Ablehnung in der Notiz) brauchen beide, dass eine Notiz eine **Art** hat. Ohne sie wird das Feld an alle Notizen gehängt und ist an den meisten sinnlos. |
| **B** `DiagnosticNote` bekommt `Kind: Place \| Help \| Category \| Suggestion` | rustc, Clang, Roslyn | Ein Enum am Record. Der Renderer darf pro Art anders zeichnen (`note:` / `help:` / `= `), der LSP darf `Suggestion` zu einer `CodeAction` machen statt zu `relatedInformation`. Preis: jede bestehende Notiz muss eine Art bekommen — mechanisch, aber überall. |
| **C** Nur `Suggestion` als eigener Typ neben `DiagnosticNote` | Clang trennt Fix-it-Hints von Notes | Kleiner Eingriff, löst D9. Lässt die Vermischung von Ort und Kategorie bestehen — die gemessen bereits da ist (`SEM0107`). |
| **D** Art **implizit** aus `Location`: mit Ort = `Place`, ohne = `Help` | heute faktisch | Kostet nichts und ist heute schon die halbe Wahrheit. Aber: die Kategorienotiz von `SEM0107` (*„a migration warning: …"*) ist ortlos und trotzdem kein `help` — sie sagt nicht, was zu tun ist. Die Abbildung ist falsch. |

**Empfehlung: B.** Es ist die Vorbedingung von D9 und D2 und räumt zugleich auf, was `SEM0107`
heute schon vermischt. **Was der LSP damit macht, gehört in dieselbe Entscheidung**: `Suggestion`
wird `CodeAction`, `Place` bleibt `relatedInformation`, `Category` wird an die Meldung angehängt
oder verworfen — heute wird alles uniform zu `relatedInformation`, und ein Editor zeigt dem
Benutzer „a migration warning: …" als anklickbaren Ort, der auf die Warnung selbst zeigt.

**Bricht:** nein, wenn `Kind` einen Default hat; die JSON-Notiz bekommt ein additives Feld.
**Hängt an:** **D9** (blockiert davon), D2, D10, D12-C, D21.

---

### D27 — Welche Meldungsqualität wird maschinell erzwungen? *(neu)*

**Heute: genau eine Regel.** `tests/Lyric.Tests.Core/DiagnosticTextTests.cs:46`
(`No_string_literal_cites_a_document`) verbietet `§` und `.md` in Meldungsliteralen; der
Klassenkommentar erklärt warum (fünf Meldungen nannten `Sprache.md`, das seit langem
`docs/Grammar.md` heißt, und zitierten §10 und §11 eines Dokuments mit sieben Abschnitten)
[gelesen, 70 Zeilen]. Mehr wird nicht geprüft.

Die erste Fassung kritisierte die Prosa (*„Substantivgruppe ohne Verb"*, `IR0001`) und übersah,
dass das Gate dafür bereits existiert und billig erweiterbar ist — **und dass es auf alle 228
Codes wirkt**, während jede Renderer-Arbeit aus D10/D11 nur die Darstellung verbessert.

| Kandidat-Invariante | Prüfbar? | Preis |
|---|---|---|
| Kein Schlusspunkt am Meldungsende | trivial | Golden-Tests ändern sich einmal. |
| Kein `LYR-`-Code im Meldungstext | trivial | Der Code steht schon in der Kopfzeile. |
| Meldung beginnt klein und enthält ein Verb | Verb-Prüfung ist Heuristik | Falschpositive; als Warnung im Test, nicht als Fehler. |
| Längengrenze (etwa 120 Zeichen bis zum Gedankenstrich) | trivial | `SEM0107`s Meldung ist heute 118 Zeichen — die Grenze wäre knapp, aber haltbar. |
| Meldung nennt das schuldige Ding beim Namen (enthält `'…'`) | trivial, mit Ausnahmeliste | `IR0001`s `increment/decrement target (only parameters and locals)` fällt durch — zu Recht. |
| Meldung sagt den nächsten Schritt, oder eine Notiz tut es | nur mit D26 (`Kind = Help`) | Nach D26 mechanisch: „jede `Error`-Diagnose hat entweder `'…'` im Text oder eine `Help`-Notiz". |

| Option | Vorbild | Preis |
|---|---|---|
| **A** Geschmack, wie heute | Elm (eine Person mit sehr gutem Geschmack) | Funktioniert, solange eine Person schreibt. `IR0001` zeigt, dass es an ~100 Stellen nicht funktioniert hat. |
| **B** Vier bis sechs Invarianten als Test, Fehler | — | Billig, wirkt auf alle Codes, fängt den nächsten Fall vor dem Review. Preis: einmal ~30 Meldungen umschreiben. |
| **C** Wie B, plus eine **Stilregel in `CONTRIBUTING.md`** für das, was kein Test fangen kann | Rust hat eine Diagnostikfibel | Der Test hält die Form, der Text hält die Absicht. |

**Empfehlung: B + C, und vor D10/D11.** Es ist der billigste Posten dieses Gebiets mit der
größten Reichweite: sechs Regex-Regeln wirken auf 228 Codes, eine Renderer-Überarbeitung wirkt
auf die Darstellung von jedem — aber nur, wenn der Text darunter etwas taugt.
**Bricht:** nein (nur Meldungstexte, §12.1 Regel 3 erlaubt das ausdrücklich).
**Hängt an:** D26 (für die letzte Invariante), D24 (Golden-Tests).

---

### D28 — Was heißt „fremd", wenn es keine Paketgrenze gibt? *(neu)*

**Heute:** eine Warnung aus einem importierten Modul erreicht den Benutzer und ist von einer
eigenen nicht unterscheidbar. Gemessen: Lauf über `cross/main.lyr`, Warnung
`C:\…\cross\lib.lyr:2:9: warning[LYR-SEM0071]`, Erfolgszeile `cross/main.lyr: ok` — dieselbe
Ausgabe, derselbe Zähler, beide gegen `--deny-warnings`.

Rusts `--cap-lints` funktioniert, weil Cargo **Pfad-Abhängigkeiten von Registry-Abhängigkeiten
unterscheidet**. Lyric hat keinen Paketmanager; `PLAN.md` führt ihn unter „Werkzeuge — ebenfalls
neu" [gelesen]. Was es gibt: `sourceRoot` in `lyric.json` und `CompilerOptions.SourceRoot`
(`SourceCompiler.cs:491`) [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** „Fremd" = **außerhalb des `sourceRoot`** aus `lyric.json` | — | Heute definierbar, ohne neuen Begriff. Aber: ein Monorepo mit zwei Wurzeln zählt die eigene zweite Wurzel als fremd. |
| **B** „Fremd" = **stdlib** | Go: `go vet` prüft die Standardbibliothek nicht mit | Die einzige Grenze, die heute unstrittig existiert (`--stdlib <dir>`, `$LYRIC_STDLIB`). Deckt aber nicht den Fall, der wehtut: eine fremde Bibliothek, die nicht die stdlib ist. |
| **C** „Fremd" = **ein Paket, das nicht dieses ist** | Rust `--cap-lints`, Cargo | Die richtige Antwort, und sie ist **vor dem Paketmanager nicht definierbar**. |
| **D** Gar keine Grenze, stattdessen eine **Codegruppe** ausnehmen (D22) | Swift/Java | Löst den praktischen Fall (Deprecations aus einer Bibliothek) ohne jeden Paketbegriff. Löst nicht den Fall „fremde Bibliothek hat unbenutzte Locals". |

**Empfehlung: B jetzt, A als Übergang, C erst mit dem Paketmanager — und D4-C bis dahin
zurückstellen.** Die erste Fassung empfahl `--cap-warnings` als Hauptantwort auf D4; sie ist
heute **nicht implementierbar**, weil der Begriff fehlt. `D` löst den realen Schmerz früher und
billiger.
**Bricht:** nein.
**Hängt an:** D4-C, D22, D9 (was darf `lyrfix` anfassen — dieselbe Grenze), Paket-/Build-Gebiet.

---

### D29 — Wie kommt der Katalog ins Paket, ohne die Kopplung, die D16 ablehnt? *(neu)*

**Der Widerspruch, den die erste Fassung in sich trug:** D12-A sagt *„der Katalog muss ins
Paket"*, D16-C lehnt *„koppelt den Build an das Spec-Repo"* ab. Das ist derselbe Schritt, einmal
empfohlen, einmal verworfen.

**Heute:** der Katalog liegt in `lyric-spec/spec/appendix-a-diagnostics.md`. Der Wächtertest
sucht ihn an zwei Orten — `../lyric-spec` und `spec-repo/` — und gibt bei Fehlanzeige **still
grün** zurück (`DiagnosticCatalogueTests.cs:52-64`, `:100`) [gelesen]. Für die Kapitel 02 und 13
gibt es bereits einen **Spiegelmechanismus** (Mirror-Job), für den Appendix nicht — der
Klassenkommentar sagt das ausdrücklich: *„the spec-mirror job diffs chapters 02 and 13, so the
appendix is outside what it can see"* [gelesen].

| Option | Vorbild | Preis |
|---|---|---|
| **A** **Kopieren beim Release-Schnitt**: der Appendix wird ein Artefakt, das der Tag mitnimmt | — | Keine Build-Kopplung. Ein Schritt in der Release-Checkliste, die es schon gibt. Preis: zwischen zwei Releases kann die Kopie altern — genau die Drift, die §12.2 verhindern soll. |
| **B** **Generieren**: der Build erzeugt den Katalog aus den Quelltextkonstanten und prüft ihn gegen die Spec | — | Immer aktuell. Koppelt den Build an das Spec-Repo — der Schritt, den D16-C verwirft. Und er dreht spec-first um: die Implementierung würde die Wahrheit liefern. |
| **C** **Spiegeln**, wie Kapitel 02/13 heute | der bestehende Mirror-Job | Der Mechanismus existiert bereits und ist genau für dieses Problem gebaut. Der Appendix wandert in den Spiegel, der Wächtertest liest die gespiegelte Kopie statt eines Nachbar-Checkouts — **und kann dann nicht mehr still grün werden**, weil die Datei immer da ist. |
| **D** Katalog als **veröffentlichte Webseite**, `lyrc explain` holt nichts, der LSP verlinkt | GHC (`errors.haskell.org`) | Kein Paketinhalt, keine Kopplung. Aber `lyrc explain` funktioniert offline nicht — und das ist der halbe Wert von `rustc --explain`. |

**Empfehlung: C, und damit ist D12-A und D16-D zugleich gelöst.** Der Spiegel ist der
Mechanismus, den das Projekt für genau diese Frage schon hat; ihn für ein drittes Dokument zu
benutzen ist **kein** zweiter Mechanismus. Er beseitigt den stillen Pass (D16-3) als Nebenwirkung,
weil die Datei dann Teil des Repos ist. `D` zusätzlich, für die LSP-URL (D12-C).

**Rule-2-Anmerkung:** `A` und `C` nebeneinander wären zwei Wege, denselben Text zu transportieren.
Wenn `C` kommt, fällt `A` aus der Release-Checkliste.
**Bricht:** nein.
**Hängt an:** **D12**, **D16**, Spec-Repo-Ritual.

---

### D30 — Welchen Exit-Code bekommt was? *(neu)*

**Heute [gelesen, `src/Lyric.Core/ExitCodes.cs`]:** `0` Erfolg, `1` *„Load, validation, compile
or IO error: the program never started"*, `2` Usage, `101` Panic. Die Datei nennt sich
**normativ für jede Runtime**.

**Gemessen, mit Kontrolle:**

| Lauf | Exit |
|---|---|
| `lyrc check q14_err_plus_unused.lyr` (echter Typfehler) | **1** |
| `lyrc build q1_rebind.lyr --deny-warnings` (Programm in Ordnung, eine Warnung abgelehnt) | **1** |
| `lyrbuild proj --json` (Flagge gibt es nicht) | 2 |
| `lyrvm run q25.lyrbc` (Panik) | 101 |

**CI kann „das Programm ist kaputt" nicht von „das Programm ist in Ordnung und hat eine Warnung"
unterscheiden.** Beides ist 1. Das ist genau der Unterschied, den ein Build-Gate braucht: das
eine heißt „nichts gebaut", das andere „gebaut, aber Politik verletzt" — und im zweiten Fall
**existiert das Artefakt**.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Ein Skript, das zwischen beiden unterscheiden will, muss die Ausgabe parsen. Mit `--json` geht das; ohne nicht. |
| **B** Eigener Code für „Warnungen abgelehnt", etwa `3` | — | Ein Byte, ein Spec-Satz. Bricht jedes Skript, das heute `!= 0` prüft — also keines. Bricht Skripte, die `== 1` prüfen. |
| **C** `--deny-warnings` gibt weiterhin 1, aber der **gekappte** Lauf (D8) bekommt einen eigenen | Clang: `-ferror-limit` ändert den Exit-Code nicht | Löst die kleinere Hälfte. |
| **D** Gar keine neuen Codes; stattdessen ist `--json` die Antwort auf jede Unterscheidungsfrage | Rust: `cargo` gibt 101 für alles Fehlerhafte, die Details stehen im JSON | Konsistent mit D15/D23. Preis: jedes CI-Skript braucht `--json` und einen Parser. |

**Empfehlung: B.** Die Unterscheidung ist real und einzeilig, und `ExitCodes.cs` ist der Ort, an
dem sie hingehört — die Datei begründet bereits, warum `2` von `1` getrennt ist („so a caller can
tell a misuse from a broken file"); dasselbe Argument trägt `3`.
**Anschluss aus D8:** was ein gekappter Lauf zurückgibt, gehört in dieselbe Entscheidung. Mein
Vorschlag: den Code des schwersten enthaltenen Befunds, plus `truncated` im JSON — die Kappung ist
keine eigene Kategorie, sie ist eine unvollständige Antwort.
**Bricht:** minor (ein Skript, das auf `== 1` prüft).
**Hängt an:** D4, D8, D20, D23.

---

### D31 — Wohin zeigt eine Diagnose aus einer `comptime`-Auswertung? *(neu)*

**Heute [gelesen, `SourceCompiler.cs:185-220`, `:550-566`]:** `LYR-CT0001` (kein Evaluator) und
`LYR-CT0002` (Auswertung fehlgeschlagen) tragen beide `site.Span` bzw. `sites[i].Span` — den Span
des **`comptime`-Ausdrucks**, nie den der ausgewerteten Quelle. Der Doc-Kommentar nennt die Phase
ausdrücklich eigen: *„Neither sema nor lowering: the program was fine and lowered fine, and what
failed was running a piece of it early."* Die Auswertung schreibt eine Source-Map mit, *„so a
panic inside a site names the line it happened on"* (`:206`) — **diese Information existiert also
und wird in der Diagnose nicht benutzt**: `CT0002` hängt `why` als Text an die Meldung.

Im `lyrc`-Pfad ist `CT0001` unerreichbar (die Treiber, die eine Runtime besitzen, reichen die VM
herein), für einen Embedder ohne VM nicht [gelesen]. Beide Codes stehen in keiner Spec (1.2i).

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen: der Span ist die `comptime`-Stelle | heute | Bei einer Panik drei Ebenen tief zeigt die Meldung auf `comptime f(x)` und der Grund steht als Prosa dahinter. Der Benutzer sucht selbst. |
| **B** Span der **ausgewerteten Quelle**, wenn die Source-Map ihn kennt | Rust: ein Fehler in einer `const fn` zeigt in die `const fn` | Die Information liegt vor (`SourceMapContext`, `:205-207`). Preis: die Diagnose zeigt in eine Funktion, die der Benutzer nicht „gerade jetzt" aufgerufen hat. |
| **C** **Beides als Notizkette**: Hauptspan an der `comptime`-Stelle, Notizen mit den Frames | rustc: „in this expansion of…"; Lyrics eigene Panikform macht es zur Laufzeit schon (`in main.main (…)`) | Die ehrlichste Form, und sie braucht nur D26 (`Kind = Place`). Die Panik zur Laufzeit druckt ihre Frames bereits — hier wären es dieselben. |
| **D** `CT` wird ein **Bereich in §12.1**, mit eigener Spanregel | — | Die Phase ist real; ein Bereich, der sie benennt, ist die Alternative zu „diese zwei Codes existieren nicht". |

**Empfehlung: C + D.** `D` ist die Antwort auf die Zusatzfrage aus D16 („gehört `CT` als Bereich
in §12.1?") — ja, weil die Alternative eine Unwahrheit ist: die Phase ist weder Sema noch
Lowering, und beide Codes existieren. `C` macht aus der Textprosa eine Kette, die ein Werkzeug
lesen kann, und benutzt die Source-Map, die schon geschrieben wird.
**Und die Frage darunter, die gleich mitentschieden gehört:** gilt für eine
Auswertungsdiagnose derselbe Dedup- und Sortiervertrag (D24) wie für die übrigen? Heute läuft sie
durch dieselbe `DiagnosticEngine`, also ja — aber nirgends steht es.
**Bricht:** nein (`CT` ist heute undokumentiert; ihn zu dokumentieren ist additiv).
**Hängt an:** **D16** (der Wächter sieht `CT` nicht), D26, D24, Metaprogrammierungs-Gebiet.

---

### D32 — Was macht `lyrls`, während der Puffer nicht parst? *(neu)*

**Heute [gelesen]:** `lyrls` ruft denselben Einstiegspunkt wie `lyrc check` —
`AnalysisService.cs:478` → `SourceCompiler.CheckProject`. Was `lyrc check` auf einem kaputten
Puffer tut, veröffentlicht der LSP. Die gemessene Lawine aus 1.2c (zwölf Fehler aus einem `:`)
erscheint also **im Editor, beim Tippen, in genau dem Moment, in dem der Puffer halbfertig ist**.

Die erste Fassung verwarf D7-B mit dem Argument, der LSP sei *„das Werkzeug, in dem halbfertige
Syntax der Normalfall ist"* — ohne eine einzige Messung am LSP. Das Argument stimmt, ist jetzt
belegt, und es schneidet in beide Richtungen: derselbe Compiler, dieselbe Lawine.

**Und der Mechanismus, den D7 braucht, existiert bereits.** `DiagnosticEngine.Mute()`
(`:37-67`) hält die Aufzeichnung an, zählt verschachtelt und ist für spekulative
Überladungsauflösung gebaut [gelesen]. D7-D („eine Deklaration mit Parse-Fehler wird nicht
typgeprüft") ist genau so ein Scope.

| Option | Vorbild | Preis |
|---|---|---|
| **A** So lassen | heute | Der Editor zeigt beim Tippen erfundene Semantikfehler. Das ist der sichtbarste Qualitätsmangel dieses Gebiets, weil er bei jeder Tastenanschlagfolge auftritt. |
| **B** D7-D als `Mute()`-Scope um die Typprüfung einer Deklaration mit Parse-Fehler | Roslyn `IsMissing`; Lyrics eigenes `Mute` | Billig, weil der Mechanismus steht. **Die Falle: `Mute` unterdrückt die Aufzeichnung, also auch `ErrorCount`** (`DiagnosticEngine.cs:11-13`) — und an `ErrorCount` hängt `Ok`, an `Ok` hängt der Exit-Code. Eine gemutete Deklaration darf den Lauf nicht grün machen; der Parse-Fehler selbst wurde vorher gemeldet und hält ihn rot. Das ist zu prüfen, nicht anzunehmen. |
| **C** Eigener Mechanismus: die Deklaration wird markiert und übersprungen, ohne `Mute` | — | Vermeidet die `ErrorCount`-Falle, baut aber einen zweiten Unterdrückungsweg — Rule-2-verdächtig, weil `Mute` genau das tut. |
| **D** LSP bekommt **eigene** Regeln: er filtert Sema-Diagnosen aus Dateien mit Parse-Fehlern | TypeScript `tsserver` priorisiert Syntaxdiagnosen | Löst es nur im Editor, nicht in `lyrc check`. Zwei Wahrheiten über dasselbe Programm. |

**Empfehlung: B, mit der `ErrorCount`-Prüfung als ausdrücklichem Teil des Slices.** Der
Mechanismus steht, und ihn zu benutzen ist das Rule-2-konforme Vorgehen. Was nicht passieren
darf: dass eine gemutete Deklaration einen Lauf grün macht — deshalb gehört ein Testfall
„Parse-Fehler in einer Deklaration → Exit 1, aber nur ein Fehler" in denselben Slice.
**Bricht:** nein — es verschwinden nur Fehler, die das Programm nicht hat.
**Hängt an:** **D7** (dieselbe Frage, andere Seite), D30 (Exit-Code), Editor-Gebiet.

---

## 4. Was wir übernehmen sollten

| Von | Was genau | Warum es zu Lyric passt |
|---|---|---|
| **Kotlin `ReplaceWith`** *(neu — die erste Fassung schrieb hier fälschlich Rust)* | Die Deprecation trägt den **Ersatzausdruck** als anwendbaren Text | Rust kann das auf stable nicht, und `cargo fix` migriert keine Deprecations. Kotlin (und Swift `renamed:`) sind die echten Vorbilder für **D13-C**. |
| **Java `@Deprecated(since=, forRemoval=)` + `-Xlint:removal`** *(neu)* | **Beide** Felder, die D13 will, und die Entfernungsankündigung als eigene schaltbare Kategorie | C# hat kein `since`. Java hat genau Lyrics Problem gelöst: eine Uhr in der Deklaration plus ein Schalter für die Ankündigung. **(D13-E, D20-D)** |
| **Swift SE-0443 Warnungsgruppen** *(neu bewertet)* | Benannte Gruppen **neben** den Codes, mit `-Wwarning <group>` / `-Werror <group>` | Die dritte Achse, die Lyric offensteht: vier Fragen dieses Dossiers (D3, D4-B, D20, D22) verlangen heute je eine Codeliste. Eine Gruppentabelle ersetzt alle vier. **(D22)** |
| **Rust `suggested_replacement` + `applicability`** | Das JSON-Feld und die Anwendbarkeitsstufe; `cargo fix` als iterativer Treiber | `lyrfix` wird dadurch klein statt groß. Ohne das baut man die Sprachkenntnis ein zweites Mal. **(D9)** |
| **Rust `--explain`** | Katalog wird mitgeliefert und ist auf der Kommandozeile nachschlagbar | Der Katalog existiert bereits und liegt am falschen Ort. **(D12, D29)** |
| **GHC-Codes mit URL** *(neu — die erste Fassung schrieb hier fälschlich Clang)* | `[GHC-83865]` mit `errors.haskell.org/messages/GHC-83865`, seit 9.6 mit Stabilitätszusage | Clang druckt keine URLs. GHC zeigt, wie eine URL aus dem Code **mechanisch folgt**, statt im Meldungstext zu stehen und mit ihm zu altern. **(D12-C)** |
| **Swift `-enable-upcoming-feature` + Migrationsmodus** | Pro Regel opt-in, Stufe `warn` mit Fix-its vor der Stufe `error` | Die Antwort auf die Frage, die Lyric bereits als „immer an" gebaut hat. **(D14)** |
| **C# `ObsoleteAttribute.DiagnosticId`** | Jede Deprecation trägt ihre eigene Kennung | Der genaue Grund, warum `stdlib/std/iter.lyr:872` heute keine Uhr starten kann — **wenn** man einen zweiten Nummernraum will. Sonst: eine Gruppe. **(D13-D, D3)** |
| **TypeScript `@ts-expect-error`** | Eine Unterdrückung, die **selbst meldet**, wenn sie überflüssig geworden ist — und die **einzige** echte Zeilenreichweite der Liste | Verhindert, dass die Unterdrückung aus D3 zu totem Ballast wird. Clangs `push/pop` taugt dafür nicht, es ist regionsbasiert. **(D3-D)** |
| **Clang** | `-ferror-limit`, Zeilenkürzung, Fix-it-Hints | Die Terminalhygiene, die heute komplett fehlt. **(D8, D11)** |
| **rustc / Clang** | Mehrere beschriftete Spans in einem Snippet; `note`/`help`/`suggestion` als **verschiedene Arten** | Der größte Qualitätssprung — und die Notizart ist die Vorbedingung für D9. **(D10, D26)** |
| **Roslyn** | Syntaktische Analyzer laufen auf kaputten Bäumen; `IsMissing` an Tokens | Beides zugleich: D5/D22 (welche Warnung läuft bei Fehlern) und D7-C (vergiftete Knoten). |
| **Zig** | Die Strenge selbst — unbenutzt ist ein Fehler, `_ =` ist der einzige Ausweg | Lyric hat die `_`-Regel bereits. Der Vergleich zeigt: Lyric ist hier **weicher** als zwei der Vergleichssprachen, nicht strenger. |
| **Elm** | Prosa-Qualität: ein Satz, der sagt, was zu tun ist | Lyric kann das teilweise (`SEM0050`, `SEM0081`, `SEM0107`). Wo es fehlt, fällt es auf — und **D27** macht daraus einen Test statt einer Hoffnung. |

**Nicht übernehmen:**

- **C#s Severity-als-Konfiguration** — bricht §12.1 Regel 1 und macht Konformanz unmöglich.
- **Elms Codelosigkeit** — macht die Spec unanschreibbar.
- **Rust-Editionen** — zwei Semantiken für immer. **Mit einer Einschränkung, die die erste
  Fassung sich selbst unterlaufen hat:** D14-B (`--upcoming <name>=error`) ist, sobald ein Name
  `error` erreicht, ebenfalls eine zweite Semantik im selben Compiler. Der Unterschied ist allein
  die **Befristung**, und die ist eine Selbstverpflichtung, kein Mechanismus. Wer D14-B will,
  schreibt die harte Klausel in den ADR: *jeder `upcoming`-Name hat eine Version, in der der
  Compiler ihn ablehnt.* Wer sie nicht schreiben will, nimmt D14-D (nur `off`).
- **Kotlins `DeprecationLevel`** — die Stufe in der Deklaration bricht §12.1 Regel 1, und `until`
  leistet dasselbe härter.

---

## 5. Konflikte

### Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept)

1. **D19 — die real ausgelieferte Kollision.** Seit 4.6 tragen **zwei** Familien dasselbe
   Konzept („das hier ändert sich mit 5.0"): `@Deprecated`/`SEM0076` mit `until` an einer
   Deklaration, und `SEM0107`–`SEM0110` ohne jede Deklaration. Der Grund ist strukturell —
   `@Deprecated` ist `[OnModule, OnType, OnFunction]` (`stdlib/std/core.lyr:500`) und kann eine
   **Regel** gar nicht markieren. **Die erste Fassung hat diese Kollision übersehen**, weil sie
   die Familie nicht kannte. Sie gehört in den ADR, egal wie D19 entschieden wird.
2. **D3 — Unterdrückung neben `_`.** Lyric hat für „absichtlich ungenutzt" bereits einen
   Mechanismus: den Namen `_` (`19-diagnostics.md:40`). Die Begründung, die der ADR tragen muss:
   `_` ändert das *Programm*, eine Unterdrückung nur die *Meldung*, und für `SEM0076` gibt es
   kein `_`-Äquivalent — die Bibliothek *muss* ihren eigenen alten Namen nennen. Mitigation: die
   Direktive gilt nur für einen in der Spec aufgezählten Satz; `SEM0071` ist ausdrücklich nicht
   darin.
3. **D22 — Gruppen neben Codes.** Ein zweiter Namensraum, wie bei Rust. Das Gegenargument ist
   zugleich das Rule-2-Argument **dafür**: vier Fragen (D3, D4-B, D20, D22) lösen heute jede für
   sich mit einer eigenen Codeliste. Vier Codelisten sind vier Mechanismen; eine Gruppentabelle
   ist einer.
4. **D14 — Feature-Schalter.** Ein Schalter, der Sprachregeln verändert, ist eine zweite
   Sprachversion im selben Compiler; siehe die Klausel oben in §4.
5. **D15/D18/D21 — `kind` im JSON.** Panik (D18) und Migrationswarnung (D21) brauchen dasselbe
   Feld. Es einmal zu entwerfen ist Rule-2-konform, es zweimal nachzurüsten nicht.
6. **D29 — Katalogtransport.** „Beim Release kopieren" und „spiegeln wie Kapitel 02/13" wären
   zwei Wege für denselben Text. Wenn der Spiegel kommt, fällt die Kopie aus der Checkliste.
7. **D32 — `Mute` vs. ein zweiter Unterdrückungsweg.** `DiagnosticEngine.Mute()` existiert. D7-D
   darüber zu bauen ist Rule-2-konform; ein eigener Markierungsweg wäre es nicht.
8. **D11-D — `--json` und `--quiet`.** Zwei Schalter für dasselbe Schweigen. `--quiet` existiert
   bereits; `--json` soll es implizieren, nicht duplizieren.
9. **D15-B/D — NDJSON und SARIF.** Zwei Formate nebeneinander. Empfehlung ist Ablösung mit 5.0,
   nicht Koexistenz; SARIF nur, wenn jemand es tatsächlich braucht.

### Mit dem Charakter

- **Kein Konflikt** mit GC, Vererbung, `throws`/`defer`, Single-Thread, fehlendem Typ-Tag,
  Capabilities. Dieses Gebiet berührt keinen davon.
- **spec-first bleibt gewahrt**, wenn D1 (Grenzkennungen), D3 (Spalte „unterdrückbar"), D13
  (`since`/`replacement`), D21 (`kind`/`group`/`settledIn`), D22 (Gruppentabelle), D24 (Ordnung),
  D25 (`Info`) und D31 (`CT` als Bereich) je einen Spec-PR **vor** dem Compiler bekommen. Der
  Wächtertest aus D16 ist der Mechanismus, der das erzwingt statt es zu hoffen — **sobald sein
  Bereichssatz offen ist.**

### Mit anderen Gebieten

| Konflikt | Betroffen |
|---|---|
| D7/D32 lösen sich halb auf, wenn die Grammatik `Typ {` nicht mehr mit Trailing-Lambdas kollidieren lässt (`Grammar.md:500`) | **Syntax** |
| D1/D2/D19: `p.x++` ist keine Lowering-Grenze, sondern eine offene Regel (`PLAN.md:184-187`) und gehört in die Migrationsfamilie | **Operatoren/Semantik** |
| D9 ist Vorbedingung für **jeden** 5.0-Bruch aus `PLAN.md:352-361` | **alle** |
| D14 ist **nicht mehr** Vorbedingung, sondern Rückbau: die Warnstufe läuft seit 4.6 | **Planung** |
| D13s `replacement` für die vier stillstehenden std-Deprecations berührt die std-API | **Standardbibliothek** |
| D28 („fremd") ist vor dem Paketmanager nicht definierbar; D4-C hängt daran, und D9 auch | **Pakete/Build** |
| D23 (Vertrag für die Toolchain) betrifft `lyrbuild`, `lyrtest`, `lyrvm`, `lyrfmt`, `lyrpack`, `lyrls`, `lyrdbg`, `lyrrepl`, `lyrstub` | **CLI/Werkzeuge** |
| D25 (`Severity.Info` streichen) bricht die öffentliche Embedding-API | **Embedding** |
| D29 (Katalog spiegeln) berührt das Spec-Ritual und den Mirror-Job | **Spec-Repo** |
| D31 (`CT`-Bereich, Span aus der Auswertung) | **Metaprogrammierung** |
| D32 (LSP zeigt beim Tippen erfundene Fehler) | **Editor-Werkzeuge** |
| Die Monomorphisierungsgrenzen bleiben auch nach D1 echte `IR0001` (18 Stellen in `TypeTable.cs`) | **Generics** |

### Offene Tatsachenlage, die der Maintainer kennen sollte, bevor er plant

1. **Die Migrationsfamilie ist gebaut und läuft ungebremst.** `SEM0107`–`SEM0110`, §12.5,
   Guide-Abschnitt, Retirement-Regel — alles da [gemessen + gelesen]. Sie ist **nicht
   abschaltbar**, **nicht ausnehmbar**, und `lyrc --deny-warnings` wird davon rot [gemessen].
   Der eigene Beispielcode musste angepasst werden (`examples/objects.lyr:19`). **D20 ist damit
   die dringendste Frage dieses Gebiets**, und sie war in der ersten Fassung nicht gestellt.
2. **`--deny-warnings` existiert am Projektbau nicht.** `lyrbuild proj --deny-warnings` →
   `CLI0003`, Exit 2 [gemessen, mit Kontrolle]. Ebenso `--json` an `lyrbuild` und `lyrtest`.
   **Der Pfad, den CI benutzt, hat weder den Schalter noch das Format.** Jede Antwort auf D4 und
   D15, die das nicht voranstellt, beantwortet die falsche Frage.
3. **Vier von fünf Deprecation-Uhren laufen nicht** (1.2e, belegt). `PLAN.md:361,368-369` geht
   vom Gegenteil aus. Der Grund ist kein Versäumnis, sondern D3 — es *geht* nicht ohne.
4. **„Sema läuft auf Parser-Recovery-Knoten" ist reproduziert**, zweimal, mit Kontrolle (1.2c).
   `STATUS.md:2261` und `PLAN.md:177` können von „nicht reproduziert" auf „gemessen" gesetzt
   werden. Und **der LSP fährt denselben Compiler** (`AnalysisService.cs:478`) [gelesen] — die
   Lawine erscheint im Editor.
5. **`LYR-CT0001`/`CT0002` stehen in keiner Spec** (1.2i) **und der Wächtertest kann sie nicht
   sehen**: seine Regex kennt `CT` nicht (`DiagnosticCatalogueTests.cs:34`) [gelesen]. Das ist
   der Fehler, den der Test verhindern sollte, und er ist genau außerhalb seines Sichtfelds.
6. **Der Wächtertest gibt ohne Spec-Checkout still grün zurück** (`:100`) [gelesen]. Ob der
   CI-Job, den sein Kommentar voraussetzt, existiert, ist aus der Datei nicht prüfbar — das wäre
   nachzusehen.
7. **Die Diagnoseordnung ist nicht total** (`List<T>.Sort` + Comparer mit Code als letztem
   Schlüssel) [gelesen]. Golden-Tests hängen daran.
8. **`Severity.Info` hat null Codes** [gemessen]. Die „vier Severities" sind faktisch zweieinhalb.
9. **`lyrc build q1 --deny-warnings` und ein echter Typfehler geben beide Exit 1** [gemessen].
   CI kann „kaputt" nicht von „in Ordnung, aber Warnung" unterscheiden.
10. **~~`12-diagnostics.md:72` nennt `CLI0018`~~ — erledigt** (Commit `8512430`). Die erste
    Fassung führte es als offen; es ist behoben [gemessen am Spec-HEAD `8f17c02`].

---

## Nach der Kritik geändert

**Falsches korrigiert (nachgemessen, alle bestätigt):**

- **`IR0001`-Emissionsstellen: 37 → ≈108.** Gezählt: 80 `throw NotSupported(` (alle in
  `FunctionLowerer.cs`), 25 `new UnsupportedConstructException`, 3 literale
  `ReportUnsupported`. Die 8 `NeverNull`-Stellen stimmten; „29 Default-Notiz" waren rund hundert.
  Alle Kostenrechnungen in D1 und D2, die an der 37 hingen, sind ersetzt — **inklusive einer
  echten Stichprobe** über alle 80 Meldungsformen statt der Behauptung, der Restsatz werde klein.
- **D16 war ein No-op.** Der Test, den die erste Fassung als Option B empfahl, existiert
  (`DiagnosticCatalogueTests.cs`, 153 Zeilen, zwei Fakten). D16 ist vollständig neu geschrieben
  auf die drei Löcher, die er tatsächlich hat: geschlossener Bereichssatz (`CT` unsichtbar), nur
  eine Prüfrichtung, stiller Pass ohne Spec-Checkout.
- **„Katalog in Sync, null Differenz" war zirkulär** und ist aus der Tabelle „was gut ist"
  entfernt: die neun genannten Bereiche sind Zeichen für Zeichen der Alternativsatz der Prüfregex.
- **D14 war überholt.** Die Migrationsfamilie (`SEM0107`–`SEM0110`, §12.5) ist seit 4.6 gebaut,
  als „immer an". D14 ist von „wie führt man es ein" auf „bekommt es rückwirkend einen Schalter"
  umgestellt; die §5-Warnung („muss vor der ersten Warnstufe entschieden sein") ist gestrichen.
- **„Warnungen verschwinden bei jedem Fehler" war falsifiziert.** Gemessen mit Kontrolle: nur die
  `WarningAnalyzer`-Familie schweigt, die Migrationsfamilie nicht — und der Unterschied ist heute
  ein Zufall der Emissionsstelle. 1.2l und D5 sind neu geschrieben, D22 ist daraus entstanden.
- **„Eine Binärdatei weiter" waren drei.** `lyrbuild` und `lyrtest` weisen `--json` und
  `--deny-warnings` mit `CLI0003`, Exit 2 zurück [gemessen, mit Kontrolle]; `lyrbuild` hat
  überhaupt keinen JSON-Pfad (`BuildSession.cs:184,218`). D4 und D15 sind entsprechend
  umgestellt, D23 ist daraus entstanden.
- **Der `CLI0018`-Spec-Tippfehler ist behoben** (Commit `8512430`) und aus der Liste offener
  Punkte entfernt.
- **„Pfad im Kopf relativ, in der Notiz absolut" benannte die falsche Regel.** Gemessen: die
  Regel ist **pro Datei**. D11-C hatte deshalb eine Option, die für importierte Dateien nicht
  existiert; sie ist ersetzt.
- **Zwei erfundene Belegstellen.** `DiagnosticNote.cs:52-55` und `:47-48` — die Datei hat 16
  Zeilen. Alle Belegstellen dieser Fassung sind neu nachgeschlagen.

**Vergleichssprachen korrigiert (acht Zellen):** Rust `#[deprecated]` hat keinen anwendbaren
Ersatz (→ Kotlin/Swift); C# hat kein `since` (→ Java); Clang druckt keine URLs (→ GHC/Roslyn);
`#pragma clang diagnostic` ist regions-, nicht zeilenbasiert (→ nur TypeScript); Swift hat seit
6.2 benannte Warnungsgruppen (SE-0443); C#s `aka.ms`-URL gilt nur für die Obsoletion-Familie; Go
hat keinen Farbschalter; TypeScript sagt ausdrücklich **keine** Codestabilität zu (→ Rust/GHC).
**Drei Sprachen neu in der Tabelle: Kotlin, Java, GHC.**

**Empfehlungen geschärft:** D1 (keine Behauptung mehr, der Rest werde klein), D2 (dritter und
vierter Ausgang), D3-D (Regel für „der Lauf hat die Warnung gar nicht gesucht"), D4 (`A′` zuerst:
den Schalter überhaupt geben), D6 (die Tabelle täuschte eine Entscheidung vor; die echte Frage ist
D24), D8 (Cap pro Deklaration, plus Exit-Code, `truncated`, Zählweise), D9 (die
„200-Zeilen"-Kostenaussage gestrichen), D11 (A und B sind nicht trivial — die
`Printable`-Invariante; D reduziert auf „`--json` impliziert `--quiet`"), D13 (Kotlin/Java statt
Rust/C#; Option F ergänzt und abgelehnt), §4 (die Editions-Ablehnung wurde von D14 unterlaufen —
Klausel ergänzt).

**Vierzehn Designfragen ergänzt:** D19 (`@Deprecated` vs. §12.5 — ein Mechanismus oder zwei),
D20 (Unterdrückbarkeit der Migrationsfamilie und `denyWarnings` beim Sprung auf 4.6), D21
(`kind`/`group`/`settledIn` im JSON), D22 (welche Warnung läuft bei Fehlern — pro Code, und
Gruppen als Träger), D23 (Vertrag für Compiler oder Toolchain), D24 (ist die Reihenfolge Vertrag —
sie ist heute nicht einmal total), D25 (überlebt `Severity.Info` den Major), D26 (bekommt
`DiagnosticNote` ein `Kind`), D27 (welche Meldungsinvarianten werden Test), D28 (was heißt
„fremd" ohne Paketbegriff), D29 (wie kommt der Katalog ins Paket ohne die abgelehnte Kopplung),
D30 (Exit-Codes: `--deny-warnings` und ein Typfehler sind beide 1), D31 (Span einer
`comptime`-Diagnose, und `CT` als Bereich), D32 (was `lyrls` beim Tippen tut, und ob D7-D ein
`Mute`-Scope ist).
