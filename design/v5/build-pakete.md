# Lyric 5 — Gebiet: Projektmodell, Build, Pakete

Stand der Messung: Toolchain `4.6.0` (`src/Lyric.Core/ToolchainVersion.cs:22`), HEAD `6f6f029f`
auf `release/v4.6.0-cut`, Debug-Binaries aus `src/*/bin/Debug/net10.0`. Runde-1-Proben liegen
unter `…/scratchpad/v5-design/probes/build-pakete/` (`p1`–`p14`), die Proben dieser Überarbeitung
unter `…/scratchpad/v5-design/probes/build-pakete-rev2/` (`r1`–`r17`).

Belegarten: **gemessen** = Probe gelaufen · **gelesen** = Pfad:Zeile · **behauptet** = weder noch.

> **Zur ersten Fassung.** Eine adversarische Kritik hat sieben Aussagen angegriffen. Fünf davon
> waren falsch oder unvollständig und sind hier korrigiert; zwei halten und stehen unverändert,
> mit einem Satz dazu, warum. Alles, was diese Runde neu gemessen hat, trägt eine `r`-Probennummer.
> Der Abschnitt **6. Nach der Kritik geändert** listet die Änderungen.

---

## 1. Ist-Stand

### 1.1 Was da ist

| Teil | Wo | Was er kann |
|---|---|---|
| `lyric.json` | `src/Lyric.Core/ProjectFile.cs:276-311` | `name`, `sourceRoot`, `testRoot`, `nativeRoots`, `dependencies`, `toolchain`. Sechs Schlüssel, alle optional. JSON mit Kommentaren und Komma am Ende (`ProjectFile.cs:245-249`). |
| `build.lyr` | `stdlib/std/build.lyr` + `src/Lyrbuild/` | `executable`, `library`, `packed`, `option`, `flag`, `Profile`, `use`, `after()` — **und `addExecutable`, das als einziges Stück des stdlib eine `@Deprecated`-Uhr auf `5.0` trägt** (`stdlib/std/build.lyr:179-186`). Vollwertiges Lyric-Programm. |
| Artefaktfelder | `stdlib/std/build.lyr:60-109` | `optimize`, `sourceMap`, `debugInfo`, `denyWarnings`, `profileName` und **`output`** — alle aus dem Skript heraus frei setzbar (gemessen, `r6`/`r15`). |
| Konvention | `src/Lyrbuild/Program.cs:408-450` | `main.lyr` unter dem Source-Root ist **das eine** Programm; ohne `main.lyr` ist der Root eine Bibliothek und wird geprüft. |
| Profile | `src/Lyric.Frontend/Compiler/Profile.cs:26-30` | genau zwei: `debug`, `release`. |
| `lyrpack` | `src/Lyrpack/Program.cs:165-178` | Stub + Modul + Footer → eine Datei. Der Stub kommt aus `stubs/<RuntimeIdentifier>/` — dem **Wirts**-RID, nicht einem gewählten Ziel. Ausgeliefert ist hier nur `stubs/win-x64`. |
| Treiberverben | `src/Lyric.Cli/Program.cs:29-44` | `new`, `run`, `build`, `pack`, `fmt`, `test`, `check`, `disasm`, `repl`. **Kein** `clean`, `metadata`, `package`, `update`, `vendor`, `toolchain`. |

### 1.2 Die Struktur in einem Satz

Ein Projekt ist ein **Verzeichnis mit einer Datei, die sagt, wo die Module liegen**. Abhängigkeiten
sind **andere Verzeichnisse** — ausschließlich lokale Pfade, die beim Lesen existieren müssen
(gelesen, `ProjectFile.cs:394-414`). Es gibt keine Versionen, keinen Lockfile, keine Registry,
keinen Cache und keine kompilierte Bibliothek — jede Abhängigkeit wird als **Quelltext in jedes
Artefakt hineinkompiliert** (gelesen: `docs/guide/12-modules.md:74`, „Everything ends up in one
`.lyrbc`"; `BuildSession.CheckLibrary` schreibt nichts, `src/Lyrbuild/BuildSession.cs:196-224`).

Dieser eine Satz — **der ganze Abschluss ist ein Compile** — entscheidet mehr Fragen dieses
Gebiets als jede andere Eigenschaft, und die erste Fassung hat ihn zwar zitiert, aber nicht
angewandt. Er steht jetzt als eigene Frage in **BP-18** und kippt die Empfehlung von **BP-13**.

### 1.3 Was gemessen wurde — und was dabei auffiel

#### Belastbar und gut

- **Der flache Abschluss über die Abhängigkeiten funktioniert.** Ein Zyklus `a → b → a`
  terminiert und baut (gemessen, `p4`). Ein Segment, das zwei Abhängigkeiten verschieden
  belegen wollen, ist ein Fehler, der **beide Forderer mit vollem Pfad nennt** (gemessen, `p3`,
  `LYR-CLI0010`). Der Pin der Wurzel gilt für alle darunter — neu gemessen (`r12b`): Wurzel pinnt
  `geo` auf `geoA`, die Zwischenbibliothek fordert `geoB`, das Programm gibt `1` zurück, also
  `geoA`. Kein Fehler, keine Warnung.
- **Ein fehlendes Abhängigkeitsverzeichnis wird beim Lesen des Manifests genannt**, nicht später
  als „cannot find module" (gemessen, `p7`).
- **Das Artefakt ist byteweise reproduzierbar** — das ist der wichtigste neue Befund dieser Runde
  und die Grundlage, auf der BP-06, BP-08 und BP-15 überhaupt stehen können. Gemessen (`r10`):
  dasselbe Projekt zweimal gebaut, mit einer Sekunde Abstand → identischer SHA-256. Dasselbe
  Projekt aus einem **anderen Verzeichnis** gebaut → **derselbe** SHA-256, in `debug` wie in
  `release`. Der Kopf trägt `LYRB` plus die **Format**version `4`, keinen Zeitstempel; ein
  absoluter Pfad kommt in den Bytes nicht vor (gemessen, Hexdump). Auch das **gepackte
  Executable** reproduziert (gemessen, `r5`: zweimal gepackt, identischer SHA-256 über 601 373
  Bytes). Gemessen wurde innerhalb *einer* Toolchain-Fassung; ob zwei Toolchain-Fassungen
  dasselbe Modul erzeugen, ist hier nicht gemessen und soll es auch nicht.
- **Der Compiler ist schnell — warm.** Neu gemessen auf einer frischen Kopie der 400-Modul-Probe
  (`r8`, `p14`s Quellen, eigenes Verzeichnis):

  | Fall | Läufe | Spanne |
  |---|---|---|
  | 400 Module, ein Artefakt, Konvention | 5 | 823–1 158 ms |
  | 400 Module, **ein** Artefakt über `build.lyr` | 3 | 1 045–1 173 ms |
  | 400 Module, **drei** Artefakte über denselben Baum | 3 | 1 727–1 836 ms |
  | `lyric test` über denselben Baum (ein Testfile, das `m1` importiert) | 3 | 1 022–1 231 ms |
  | `lyric build` **und** `lyric test` hintereinander, wie eine CI | 1 | 1 931 ms |

  Zwei Dinge, die die erste Fassung nicht hatte. **Erstens: der Aufschlag für weitere Artefakte
  ist real, aber degressiv** — drei Artefakte kosten das 1,7-fache von einem, nicht das dreifache;
  der Grenzpreis je zusätzlichem Artefakt über 400 Module liegt bei rund 330 ms. **Zweitens: ein
  CI-Lauf kompiliert denselben Baum zweimal**, einmal für das Artefakt und einmal für die Tests,
  und zahlt damit rund 2 s statt 1 s. Beides gehört in die Cache-Frage und stand in der ersten
  Tabelle nicht.

  **Was hier NICHT gemessen ist: der kalte Fall.** Die Kritik berichtet 36,4 s für den ersten Lauf
  und 1,3–1,5 s für jeden weiteren auf derselben Probe. Reproduzieren lässt sich das in dieser
  Session nicht, weil die Toolchain-DLLs längst warm sind — meine erste Messung in einem frischen
  Verzeichnis lag schon bei 915 ms. Die Kritik hat aber in der Sache recht: **ein Minimum aus fünf
  Läufen verwirft genau den Lauf, den ein Cache adressiert** (kalter Checkout, CI-Runner, erster
  Bau des Tages). Die erste Fassung hat aus warmen Zahlen „der Preis ist klein" geschlossen; das
  ist für den warmen Fall belegt und für den kalten unbelegt. BP-08 trägt das jetzt.

#### Inkonsistent, unvollständig oder still falsch

Siebzehn Befunde. **11 ist korrigiert**, **13 ist zurückgezogen**, **2 und 14 sind geschärft**,
**15–17 sind neu**.

1. **Ein weggepinntes Abhängigkeitsprojekt wird trotzdem gelesen und wirkt weiter.**
   `ProjectFile.cs:143` liest das Manifest **bevor** Zeile 150-160 merkt, dass die Wurzel das
   Segment schon gepinnt hat, und Zeile 164 (`goto Descend`) steigt danach trotzdem in das
   übergangene Projekt hinab. Neu gemessen (`r1`): Wurzel pinnt `geo` auf `geoA`, die
   Zwischenbibliothek fordert `geoB`, und `geoB` trägt `"toolchain": "9.9"` → der Bau bricht mit
   `error[LYR-CLI0018]: … this project needs toolchain 9.9, and this is 4.6.0`. **Kontrolle**:
   dasselbe `geoB` ohne den `toolchain`-Schlüssel → baut, und das Programm gibt `1` zurück, also
   `geoA`s Wert. Von `geoB` wird kein Byte verwendet, und es bricht den Bau trotzdem.
   Zweite Folge (gemessen, `p2b`): die `nativeRoots` des übergangenen Projekts landen in der
   Tabelle. Cargos `[patch]` und Gos `replace` tun das Gegenteil: die ersetzte Quelle wird gar
   nicht erst angefasst. Der Kommentar an Zeile 152 („Pinned by the root, or the same project
   reached twice: nothing to decide") behandelt zwei Fälle als einen; für den zweiten ist das
   Absteigen richtig, für den ersten nicht.

2. **Zwei Artefakte überschreiben sich still — und die Kollision ist der AUSGABEPFAD, nicht der
   Name.** Die erste Fassung hat das als Namenskollision beschrieben; das ist zu eng. Neu
   gemessen (`r15`), drei Fälle:
   - *Gleicher Name, verschiedener `output`*: `executable("app", …)` zweimal, einmal nach
     `out/debug/a.lyrbc`, einmal nach `out/debug/b.lyrbc` → **baut sauber, zwei Dateien, keine
     Diagnose**. Ein doppelter Name ist heute also ein **legitimer** Zustand.
   - *Verschiedene Namen, gleicher `output`*: `alpha` und `beta`, beide nach
     `out/debug/same.lyrbc` → beide kompilieren, das Log zeigt **zweimal denselben Pfad**, und
     ausgeführt wird `beta`. Keine Diagnose.
   - Ohne `output` fallen beide Fälle zusammen, weil die Ableitung `out/<profil>/<name>.lyrbc`
     aus dem Namen kommt (gelesen, `stdlib/std/build.lyr:102-107`) — das ist der Fall, den `p5`
     in Runde 1 gemessen hat (`2737 bytes`, dann `2738 bytes`, ausgeführt wird der zweite).

   Die Regel, die 5.0 braucht, heißt deshalb **„zwei Artefakte dürfen nicht denselben
   Ausgabepfad haben"**, nicht „zwei Artefakte dürfen nicht denselben Namen haben". Siehe BP-15.

3. **`lyrbuild --help` führt das Build-Skript aus.** Neu gemessen (`r3`): ein `writeText` in
   `build()` legt die Datei an, auch beim reinen Hilfeaufruf — `EVIDENCE.txt` existiert nach
   `lyrbuild --help`, Exit 0. Gelesen: `src/Lyrbuild/Program.cs:66-71` ruft `DescribeOptions`,
   und dessen Kommentar sagt es offen („They exist only once the script has run, so it runs").
   Der Guide sagt „runs `build` to learn the options, collecting and compiling nothing"
   (`docs/guide/16-building.md`) — „collecting nothing" stimmt, „nichts passiert" nicht. Das ist
   derselbe Vertrauensfall wie `lyric build`, nur an der Stelle, an der ein Benutzer gerade
   herausfinden will, ob er dem Repository trauen kann.

4. **`--only <name>` kann ein `executable` nicht von seinem `packed`-Zwilling unterscheiden**,
   weil `packed(app)` den Namen erbt (gelesen, `stdlib/std/build.lyr:145-158`). Neu gemessen
   (`r15`): `lyric build --only app` baut `out/debug/app.lyrbc` **und** `out/debug/app.exe`,
   während das dritte Artefakt `other` korrekt ausbleibt.

5. **Dieselbe Bedingung bekommt zwei Diagnoseformen.** Neu gemessen (`r6b`/`r14b`) auf derselben
   Datei: `lyrbuild` meldet `warning: <pfad>: unknown key 'verison'` — nackte Zeile, kein Code
   (`src/Lyrbuild/Program.cs:79-81`); `lyrc check` meldet
   `warning[LYR-CLI0017]: <pfad>: unknown key 'verison'` (`src/Lyrc/Program.cs:302`). Ein Editor,
   der auf Codes filtert, sieht die Warnung des Build-Laufs nicht.

6. **`out/` wird nie aufgeräumt, und es gibt kein `clean`.** Ein Artefakt aus dem Build-Skript
   entfernt — die alte `.lyrbc` bleibt liegen (gemessen, `p6`). Der Treiber kennt kein
   `clean`-Verb (gelesen, `src/Lyric.Cli/Program.cs:29-44`, neu bestätigt über `lyric --help`).

7. **Das Build-Skript sieht weder die Abhängigkeiten des Projekts NOCH dessen eigenen
   Modulraum.** Die erste Fassung nannte nur die Hälfte. `ScriptOptions`
   (`src/Lyrbuild/Program.cs:216-217`) setzt `StdlibRoot` und `SourceRoot = <Projektverzeichnis>`
   — **nicht** den `sourceRoot` des Projekts, keine `DependencyRoots`, keine `NativeRoots`.
   Gemessen (`p8`): `import geo` im `build.lyr` → `LYR-RES0003`, dasselbe in `src/main.lyr` →
   baut. Neu gemessen (`r11`), und das ist der schärfere Fall: ein `build.lyr`, das ein eigenes
   Modul aus `src/` benutzen will, kommt auf **keinem** Weg heran —
   - `import shared` → `error[LYR-RES0003]: cannot find module 'shared'`
   - `import src.shared` → `error[LYR-RES0006]: this file was loaded as 'src.shared' but
     declares module 'shared'`

   Das Skript lebt damit in einem **dritten** Modulraum, der weder der des Projekts noch durch
   Pfadbastelei erreichbar ist, sobald die Datei einen `module`-Kopf trägt. BP-11 stellt die
   Frage jetzt so.

8. **Der `name` einer Bibliothek wird nie mit dem Segment verglichen, unter dem sie eingebunden
   wird.** Neu gemessen (`r17`): `"dependencies": { "banana": "../geo" }`, die Bibliothek heißt
   `geo` und liefert `src/banana.lyr` mit `module banana;` → baut, Programm gibt `7` zurück. Der
   `name: "geo"` spielt keine Rolle. Ohne passende Datei: `LYR-RES0003`; mit Datei, aber altem
   Modulkopf: `LYR-RES0006` (gemessen, `p11`). **Eine Bibliothek hat damit keine Identität** —
   nur eine Menge von Wurzel-Dateinamen, die sie beansprucht.

9. **Eine Bibliothek mit zwei Wurzelmodulen braucht zwei Einträge auf dasselbe Verzeichnis.**
   Neu gemessen (`r17`): `{"banana": "../geo", "mathx": "../geo"}` baut, das Programm gibt `10`
   zurück (7 + 3). Ein „Paket" ist also kein Ding, sondern eine Handvoll Segmente.

10. **`LYRIC_PROFILE` ist still, `--profile` ist streng.** Neu gemessen (`r17`):
    `LYRIC_PROFILE=staging` baut kommentarlos nach `out/debug/`, Exit 0; `--profile staging` →
    `error[LYR-CLI0003]: --profile: unknown profile 'staging' (expected debug or release)`,
    Exit 2. Der Kommentar in `Profile.cs:41-45` nennt den Grund („a library has nowhere to
    complain"), aber der Aufrufer von `Profile.Default` hätte einen.

11. **KORRIGIERT — nicht die Datei löst die Namensregel aus, sondern der Schlüssel `name`.**
    Die erste Fassung schrieb: „Ohne `lyric.json` wird der Verzeichnisname ungeprüft zum
    Artefaktnamen, während er mit `lyric.json` einer strengen Modulnamensregel unterliegt." Das
    ist falsch. Gelesen: `ProjectFile.cs:267` setzt `var name = Path.GetFileName(directory);` als
    Vorgabe, und `ReadName` (`:326-340`) läuft nur im `case "name"` (`:278-280`). Der Guide sagt
    es ebenso (`docs/guide/12-modules.md:112`: „Without it the directory's name serves.").
    Neu gemessen (`r4`): Verzeichnis `9bad-name` **mit** `lyric.json {"sourceRoot":"src"}` baut
    nach `out/debug/9bad-name.lyrbc`, Exit 0. **Kontrolle** (`r4b`): dasselbe Verzeichnis mit
    `"name": "9bad-name"` → `error[LYR-CLI0010]: 'name' is '9bad-name', which is not a module
    name`. Der ungeprüfte Fall ist damit der **häufigere**, nicht der seltenere: jedes Projekt
    ohne `name`-Schlüssel fällt darunter, mit Datei oder ohne. BP-15 ist entsprechend korrigiert.

12. **Die Ableitung `out/<profil>/<name>.lyrbc` steht zweimal**: in Lyric
    (`stdlib/std/build.lyr:102-107`) und in C# (`src/Lyrbuild/Program.cs:425-426`). Slice 3 hat
    das Modell ausdrücklich nach Lyric geholt; der Konventionspfad ist nicht mitgekommen.
    **Dazu neu**: die Ableitung ist ohnehin nur die Vorgabe — `Artifact.output`
    (`stdlib/std/build.lyr:88`) ist aus dem Skript frei setzbar, und der Wert wird nicht geprüft
    (gemessen, `r6`: `a.output = "out/custom/one.lyrbc";` → die Datei landet dort). Jede Aussage
    über „wo das Artefakt landet" muss diesen Überschreiber verrechnen.

13. **ZURÜCKGEZOGEN.** Die erste Fassung behauptete: „`lyrpack` liegt im Quellbau nicht neben
    `lyrbuild`, also scheitert `packed(app)` dort mit `LYR-CLI0005`." Das war ein Artefakt der
    Messmethode. Neu gemessen (`r5`) über den **ausgelieferten** Treiber:
    `dotnet src/Lyric.Cli/bin/Debug/net10.0/lyric.dll build` auf einem Projekt mit `packed(app)`
    gelingt, Exit 0, und schreibt `out/debug/app.lyrbc` (2 734 B) **und** `out/debug/app.exe`
    (601 373 B); in diesem `bin`-Verzeichnis liegen `lyrpack.dll`, `lyrpack.exe` und `stubs/`.
    **Kontrolle** (`r5b`): nur der Direktaufruf von `src/Lyrbuild/bin/Debug/net10.0/lyrbuild.dll`
    erzeugt `error[LYR-CLI0005]: lyrpack not found: …\src\Lyrbuild\bin\Debug\net10.0\lyrpack.exe
    (set LYRIC_PACK)` — ein Aufruf, den niemand ausliefert, und die Meldung nennt mit
    `LYRIC_PACK` selbst den Ausweg (gelesen, `src/Lyric.Core/Tool.cs:29`). Kein Befund.

14. **GESCHÄRFT — ein Artefakt mit fremdem Entry bekommt fremden Code untergeschoben.** Die erste
    Fassung nannte das „halbgar". Es ist ein stiller Fehlbau. Neu gemessen (`r9`):
    `projA/build.lyr` deklariert `executable("neighbour", "../projB/src/main.lyr")`;
    `projB/src/main.lyr` schreibt `import helper`, und **beide** Projekte haben ein
    `src/helper.lyr`. Der Bau gelingt ohne jede Diagnose, und das Programm druckt
    **`A's helper`**. **Kontrolle**: `projB` selbst gebaut druckt `B's helper` — bei identischer
    Artefaktgröße (2 778 Bytes beide Male). Gelesen: `BuildSession.OptionsFor` nimmt
    `project?.SourceRoot`, also den Root des *bauenden* Projekts
    (`src/Lyrbuild/BuildSession.cs:242`). Das ist dieselbe Klasse wie Befund 1 und gehört in die
    Liste der sofort warnbaren Fälle, in der es bisher nicht stand.

15. **NEU — eine Warnung im Manifest einer Abhängigkeit ist vollständig stumm.** Gemessen
    (`r6a`): `"verison"` statt `"version"` in `../dep/lyric.json` erzeugt beim Bau des
    Konsumenten **keine Zeile**, in keinem Werkzeug. **Kontrolle** (`r6b`): derselbe Tippfehler
    im Manifest der Wurzel warnt sofort. Gelesen: `Resolve` reicht `Warnings = root.Warnings`
    durch (`ProjectFile.cs:194`) und verwirft die Warnlisten aller Manifeste des Abschlusses. Der
    Kommentar direkt an dem Feld sagt selbst, warum das falsch ist: „ignoring them silently is
    how a typo becomes an afternoon" (`ProjectFile.cs:77`). Siehe BP-21.

16. **NEU — „die Wurzel entscheidet" gilt für `dependencies`, aber nicht für `nativeRoots`.**
    Gemessen (`r12`): Wurzel mit `nativeRoots { "nat": "../sdkA" }` plus eine Abhängigkeit mit
    `nativeRoots { "nat": "../sdkB" }` → harter `error[LYR-CLI0010]: 'nat' is a native root at
    …sdkB here and at …sdkA in …app\lyric.json; a segment belongs to one root` — **ohne** den Rat
    („name it in the project's own lyric.json to decide"), den die `dependencies`-Kollision gibt,
    und ohne jede Auflösungsmöglichkeit. **Kontrolle** (`r12b`): dieselbe Form mit
    `dependencies` statt `nativeRoots` → baut, die Wurzel gewinnt, keine Warnung. Gelesen:
    `ProjectFile.cs:173-177` wirft, während `:150-160` für `dependencies` die Wurzel gewinnen
    lässt. Die erste Fassung behandelte die Pin-Regel in Befund 1, BP-03 und BP-04 ausführlich
    und erwähnte `nativeRoots` nur als Leck. Siehe BP-22.

17. **NEU — ein Projekt hat keine Grenze, und `out/` gehört dem Arbeitsverzeichnis.** Gemessen
    (`r13`): in `outer/vendor/geo/` (ein Verzeichnis voller `.lyr`-Dateien, ohne eigenes
    Manifest) ausgeführtes `lyric build` kompiliert **`outer`s** `src/main.lyr` — das Programm
    druckt `OUTER` — und schreibt das Artefakt nach `outer/vendor/geo/out/debug/outer.lyrbc`.
    Einstieg aus dem einen Projekt, Ausgabeverzeichnis aus dem cwd. **Kontrolle** (`r13b`):
    bekommt `vendor/geo` ein eigenes `lyric.json`, baut es sich selbst und druckt `VENDOR`.
    Gelesen: `Discover` (`ProjectFile.cs:86-98`) läuft ohne Abbruchbedingung bis zur
    Dateisystemwurzel; `BuildByConvention` nimmt den Einstieg aus `project.SourceRoot`
    (`Program.cs:415`) und den Ausgabepfad aus `directory`, dem cwd (`Program.cs:425`). Siehe
    BP-23.

---

## 2. Sprachvergleich

> Acht Aussagen der ersten Fassung über Vergleichssprachen waren falsch oder überzeichnet. Sie
> sind hier korrigiert; die Korrekturen sind mit **⟲** markiert, weil zwei von ihnen eine
> Empfehlung kippen.

| Sprache | Manifest | Auflösung | Pinnen | Build-Programm? | Cache | Hauptpreis |
|---|---|---|---|---|---|---|
| **Rust/Cargo** | `Cargo.toml` (TOML) | SemVer-Unifikation, mehrere Majors nebeneinander | `Cargo.lock` | `build.rs` mit eigenen `[build-dependencies]` | Fingerprints in `target/`, `cargo clean` | riesige Oberfläche: Features, Resolver-Versionen, Editionen |
| **Go modules** | `go.mod` (eigene Syntax) | MVS — Maximum der Minima, kein Solver | ⟲ **der `require`-Graph in `go.mod`**; `go.sum` ist eine Prüfsummen-DB (Integrität), kein Pin | **keines, mit Absicht** | inhaltsadressiert in `$GOCACHE` | Modulpfad ist eine URL; Umbenennen = Bruch |
| **Deno** | `deno.json` (Import-Map) | JSR/npm-Specifier, Import-Map | `deno.lock` mit Integritäts-Hashes, `--frozen` | nein — `deno task` ist ein Runner | globaler Modul-Cache | Ökosystem brauchte am Ende doch eine Registry |
| **npm** | `package.json` | ⟲ **flache, gehobene `node_modules`; verschachtelt nur bei Versionskonflikt** | `package-lock.json` | `scripts` inkl. `postinstall` bei der *Installation* | `~/.npm` | Duplikate als Ausnahme, Diamond-Bugs, Install-Zeit-Skripte als Lieferkettenloch |
| **Swift SPM** | `Package.swift` — **Swift-Code**, ⟲ **auf macOS gesandboxt (`sandbox-exec`), auf Linux/Windows nicht; `--disable-sandbox` überall** | SemVer über Git-Tags | `Package.resolved` | Manifest deklariert nur; Build-Plugins separat | `.build/` | Manifest ist ein Programm: Kaltstart, `swift-tools-version:`-Rituale |
| **Nix** | `flake.nix` (Nix-Sprache) | keine — jede Eingabe exakt gepinnt | `flake.lock` (Hashes) | jede Ableitung ist ein Skript, aber hermetisch | inhaltsadressierter Store, global | eine ganze Sprache Lernkosten; Hermetik verlangt auch den Compiler zu besitzen |
| **Bazel** | `BUILD` in Starlark — ⟲ **keine E/A, Rekursion VERBOTEN (und im Bazel-Dialekt auch `while`)** | `MODULE.bazel`, MVS-artig | `MODULE.bazel.lock` | Deklaration ohne E/A, Aktionen im Sandkasten | lokal + **remote** Cache | enormer Zuschnitt; jedes Ziel muss beschrieben werden |
| **Zig** | `build.zig.zon` (ZON) + `build.zig` — **Zig-Programm** | **keine**: URL + Inhalts-Hash, Diamanten sind dein Problem | Hash **im Manifest**, kein zweiter Lockfile | ja, baut einen Step-Graph, der danach parallel und cachend läuft | `.zig-cache` + globaler Cache, inhaltsadressiert | API des Build-Programms wandert; keinerlei Versionsauflösung |

### Was jede konkret löst — und was davon zu Lyric passt

**Rust/Cargo.** `Cargo.toml` trennt Metadaten (`[package] name/version/edition`) von Kanten
(`[dependencies]`, `[dev-dependencies]`, `[build-dependencies]`). Der Resolver vereinheitlicht
semver-kompatible Anforderungen und erlaubt mehrere inkompatible Majors nebeneinander — möglich,
weil Rust Symbole pro Crate-Instanz mangelt. **Benannte Profile mit `inherits`** gibt es seit
1.57, ebenso Profil-Überschreibungen pro Abhängigkeit. `build.rs` ist ein echtes Programm mit
eigener Abhängigkeitsliste, das über stdout-Direktiven zum Compiler spricht.

⟲ **Editionen, richtig dargestellt.** Die erste Fassung schrieb, Editionen bedeuteten „zwei
Sprachregeln in einem Compiler, für immer; jede spätere Frage muss zweimal beantwortet werden".
Das ist überzeichnet, und da dieser Satz das tragende Argument gegen Option B in BP-13 war, hat
die Überzeichnung die Runde entschieden. Rusts Editionspolitik **begrenzt ausdrücklich, was eine
Edition ändern darf**: keine Änderungen am Typsystem, an der Trait-Auflösung oder an der
Standardbibliothek. Alle Editionen teilen *eine* Bibliothek und *einen* mittleren IR, Crates
verschiedener Editionen arbeiten ohne Adapter zusammen, und `cargo fix --edition` automatisiert
die Migration. Die Kosten fallen **pro Oberflächenregel** an, nicht pro Sprachfrage.

Das ändert das Argument gegen Editionen — es verschwindet aber nicht, es wird ein anderes, und
zwar ein stärkeres: **Lyrics 5.0-Brüche sind gar keine Oberflächenregeln.** Die vier Uhren, die
laufen, heißen (gelesen, `STATUS.md:49-51`) „ein zweites Binden eines Namens in einem Scope",
„eine nicht-`mut`-Methode schreibt `this`", „ein Feld über eine unveränderliche Strukturbindung
geschrieben", „ein `defer`-Körper, der werfen kann". Das sind Regeln darüber, **was ein Programm
bedeutet** — Mutabilität, Bindung, Werfen —, nicht darüber, wie es geschrieben wird. Eine Edition
in Rusts begrenzter Form könnte keine einzige davon tragen. Eine Edition, die sie tragen dürfte,
wäre zwei Semantiken in einem Compiler; und weil Lyric den ganzen Abschluss in *ein* Compile zieht
(BP-18), träfen sich die beiden Semantiken **innerhalb eines Moduls**, dort wo ein Wert aus einer
4er-Datei in eine 5er-Datei läuft. Rust hat dieses Problem nicht, weil eine Editionsgrenze immer
eine Crate-Grenze ist und die gemeinsame Oberfläche in beiden Editionen identisch typisiert wird.
**Das ist das Argument gegen Editionen, das hält** — und es ist ein anderes als das, das in der
ersten Fassung stand.

**Go modules.** Der Import-Pfad **ist** die Identität (`github.com/x/y`), der Major steht im Pfad
(`…/v2`), deshalb kann es keine Namenskollision geben und zwei Majors sind schlicht zwei Module.
MVS wählt das Maximum der Minima — deterministisch, ohne Solver. ⟲ **Der Pin ist der
`require`-Graph in `go.mod`** (seit dem Graph-Pruning in 1.17 führt das Hauptmodul alle relevanten
Versionen auf); `go.sum` ist eine **Prüfsummendatenbank**, die Integrität sichert, keine
Versionsauswahl. `replace` gilt **nur im Hauptmodul**; das einer Abhängigkeit wird ignoriert —
genau Lyrics Pin-Regel, nur konsequenter, weil das ersetzte Modul dann auch nicht weiter
mitspricht (Befund 1).

⟲ **GOTOOLCHAIN, richtig dargestellt.** Zwei Ungenauigkeiten an der wichtigsten Stelle der ersten
Fassung. (a) Seit Go 1.21 gibt es in `go.mod` eine **eigene `toolchain`-Direktive** neben der
`go`-Direktive; die `go`-Zeile nennt die minimale Sprach-/Toolchain-Version, die `toolchain`-Zeile
die zu verwendende. (b) Heruntergeladen und umgeschaltet wird **nur**, wenn `GOTOOLCHAIN` auf
`auto` (die Vorgabe) oder `<name>+auto` steht; mit `GOTOOLCHAIN=local` **verweigert** das
`go`-Kommando stattdessen. BP-13 empfahl genau diesen Mechanismus — und der entscheidende Punkt,
den die erste Fassung überging, steht in BP-18: **Go darf das, weil jedes Modul für sich übersetzt
wird und die `go`-Zeile pro Modul gilt.** Lyric hat diese Grenze nicht.

**Go ist zugleich die Sprache mit der entgegengesetzten Entscheidung** in der Frage, die Lyric mit
`build.lyr` beantwortet hat: **Go hat mit Absicht kein Build-Skript.** `go build` führt keinen
Fremdcode aus; Codegenerierung ist `go:generate` und wird *von Hand* gestartet; bedingte
Übersetzung läuft über Dateinamens-Suffixe und `//go:build`-Zeilen. Gewinn: jedes Go-Projekt baut
auf genau eine Art, ein Editor kann den Build ohne Ausführung vollständig modellieren, `go build`
auf einem fremden Checkout ist kein Vertrauensakt, und Reproduzierbarkeit ist trivial. Preis:
keine generierten Quellen im Build, keine Versionsstempel ohne `-ldflags`, und jede Sonderlocke
wird zu einem Makefile daneben.

**Deno.** Zeigt zwei Dinge. Erstens: ein Konfigurationsfile als **Daten** plus ein *Task-Runner*
(`deno task`) reicht für fast alles, was Leute von einem Build-Skript wollen. Zweitens: `deno
compile` ist exakt `lyric pack` — eine ausführbare Datei aus Runtime plus Programm, mit denselben
Plattform- und Größenkosten. Deno startete ohne Manifest (URLs direkt im Code) und hat am Ende
doch `deno.json` + `deno.lock` + Registry gebraucht; ein nützliches Gegenbeispiel für „wir
brauchen keine Identität".

⟲ **npm.** Die erste Fassung beschrieb verschachtelte `node_modules` als *den*
Auflösungsmechanismus. Seit npm 3 ist die Vorgabe eine **flache, gehobene** `node_modules`;
verschachtelt wird nur dort, wo Versionsforderungen kollidieren. Duplikate sind die **Ausnahme**,
nicht das Layout. Die Lehre bleibt: wo Duplikate erlaubt sind, ist ein Diamant nie ein Fehler —
und der Preis ist eine Fehlerklasse, in der zwei Kopien einer Bibliothek ihre eigenen Typen nicht
wiedererkennen. Dazu `postinstall` — Code-Ausführung beim *Installieren*; der Grund, warum npm
heute ein `--ignore-scripts` hat. *Für Lyric:* das Duplikat-Modell scheidet aus
(Ganzprogramm-Compile, ein flacher Modulraum), aber die npm-Geschichte ist das Argument dafür,
Code-Ausführung an so wenige Kommandos wie möglich zu binden.

⟲ **Swift SPM.** Das `Package.swift` ist ein Programm in der eigenen Sprache — Lyrics Modell —
und es **deklariert nur**, es baut nicht. Die erste Zeile `// swift-tools-version:5.9` sagt,
welche **Manifest-Semantik** gilt (nicht: welche Toolchain). Ziele (`target`) haben **einzelne
Abhängigkeitskanten**, nicht nur das Projekt als Ganzes. **Der Sandkasten ist aber nicht das, was
die erste Fassung daraus gemacht hat**: SwiftPM sandboxt die Manifest-Auswertung nur auf macOS
(über `sandbox-exec`); auf Linux und Windows läuft `Package.swift` ohne Sandkasten, und
`--disable-sandbox` gibt es überall. BP-10 stützte seine Empfehlung B auf Swift als
Existenzbeweis; der Beweis ist plattformbedingt und damit schwächer als dargestellt — **Bazel
bleibt der tragende Beleg**, weil Starlark die Eigenschaft in der Sprache hat statt im Wirt.
*Passt trotzdem zu Lyric:* die Trennung „deklarieren" von „tun".

**Nix.** Der radikalste Reproduzierbarkeitsansatz: eine Ableitung ist eine reine Funktion ihrer
Eingaben, das Ergebnis liegt unter einem Hash im Store, mehrere Versionen koexistieren trivial.
`flake.lock` pinnt jede Eingabe per Hash. Preis: eine eigene Sprache, langsame Auswertung, und
Hermetik, die erst greift, wenn man auch Compiler und libc kontrolliert. *Für Lyric übernehmbar
ist genau eine Idee:* der **Ausgabepfad trägt die Eingaben**.

⟲ **Bazel.** `BUILD`-Dateien sind Starlark: eine Python-Teilmenge **ohne E/A**, in der Rekursion
**verboten** ist — nicht „unbeschränkt", sondern gar nicht; Bazels Dialekt kennt zusätzlich keine
`while`-Schleifen. Die erste Fassung hat die Garantie schwächer formuliert, als sie ist, und
benutzt genau diese Eigenschaft, um die Cachebarkeit der Ladephase zu begründen — das Argument
ist also **stärker**, nicht schwächer. Alles, was wirklich etwas tut, ist eine *Aktion* mit
deklarierten Ein- und Ausgaben, gesandboxt, lokal oder remote gecacht. Preis: man muss jedes Ziel
hinschreiben, und die Allgemeinheit für zwanzig Sprachen kostet die Einfachheit für eine.

**Zig.** Lyrics engster Verwandter. `build.zig` ist ein Zig-Programm, `build.zig.zon` das Manifest
mit `.name`, `.version`, `.minimum_zig_version` und `.dependencies` als **URL plus Inhalts-Hash**.
Es gibt **keine Registry, keinen Solver und keinen Lockfile** — der Hash im Manifest *ist* der
Pin. Build-Optionen kommen über `b.option(...)` und `-Dname=wert`, wortgleich zu Lyrics
`option`/`flag` und `-D`. Der Unterschied: `build.zig` baut einen **Step-Graph** mit echten
Kanten, den der Runner parallel abarbeitet und **inhaltsadressiert cacht**, und `zig build run` /
`test` / `install` sind Schritte in diesem Graph statt Verben im Treiber. **Und das Merkmal, das
die erste Fassung ausgerechnet beim engsten Verwandten übergangen hat: Cross-Compilation.**
`zig build -Dtarget=…` ist bei Zig der Normalfall, nicht die Ausnahme; die Artefakte mehrerer
Ziele koexistieren. Siehe BP-19. *Passt zu Lyric:* `version` + `minimum_zig_version`, Hash statt
Lockfile, keine Auflösung, `-D`-Optionen. *Noch nicht da:* der Graph, der Cache, die Parallelität,
das Ziel.

⟲ **Perl 6 ist kein Beispiel für einen missglückten Stichtag.** Die erste Fassung führte es in
BP-13 Option A neben Python 2→3 als „abschreckendes Beispiel" auf. Perl 6 war nie ein
Migrationsziel für Perl-5-Code; es wurde zu Raku umbenannt und als **eigene Sprache**
weitergeführt, während Perl 5 unabhängig weiterlief. Es ist das Beispiel dafür, **keinen**
Stichtag zu haben — das Gegenteil dessen, wofür es zitiert wurde. Python 2→3 allein trägt das
Argument, und es trägt es gut.

⟲ **`.swift-version` ist kein Toolchain-Pin der Sprache.** Die erste Fassung führte es in BP-13
Option C in einer Reihe mit `GOTOOLCHAIN` und `rust-toolchain.toml` auf. `.swift-version` ist eine
Konvention von **Drittwerkzeugen** (swiftenv, Swiftly); weder SwiftPM noch Xcode lesen sie. Swifts
eigene In-Manifest-Versionsangabe ist `// swift-tools-version:`, und die wählt die
**Manifest-Semantik**, nicht die Toolchain — was die erste Fassung an anderer Stelle selbst
richtig sagte und hier vermischte. Damit bleiben für Option C zwei Vorbilder statt drei: Go und
rustup.

---

## 3. Designfragen

Jede Frage: Ist-Stand mit Beleg · Optionen mit Vorbild und Preis · Empfehlung · Bruch · Abhängigkeiten.

**BP-01 bis BP-17** stammen aus der ersten Fassung (BP-01, BP-03/04, BP-08, BP-10, BP-11, BP-13,
BP-15 und BP-16 sind überarbeitet). **BP-18 bis BP-33** sind neu.

---

### BP-01 — Bleibt das Manifest JSON?

**Heute:** JSON mit Kommentaren und Komma am Ende, gelesen über `System.Text.Json` mit
`CommentHandling = Skip` und `AllowTrailingCommas` (gelesen, `ProjectFile.cs:245-249`).
`lyric-v5-features.md` wirft TOML für „`lyric.json` v3" in den Raum (gelesen, Abschnitt B3).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: JSONC bleibt | Deno `deno.jsonc`, VS Code | „JSON mit Kommentaren" ist kein Standard; jedes fremde Werkzeug, das die Datei liest, muss denselben Parser-Schalter kennen |
| B: TOML | Cargo, Poetry | braucht `std.toml` *vor* dem Wechsel; ein Jahr lang zwei Leser (v2 JSON, v3 TOML) = zweiter Mechanismus; der Dateiname `lyric.json` müsste mit |
| C: ZON-artig, in Lyric-Syntax | Zig `build.zig.zon` | eine Datei, die *wie* Lyric aussieht, aber kein Lyric ist, oder Lyric-Literale parsen ohne Compiler — beides Arbeit am Frontend |
| D: das Manifest ist `build.lyr` (kein separates File) | — | genau das, was M37 abgeschafft hat: ein Editor müsste ein Programm ausführen, um den Modulraum zu kennen |

**Empfehlung: A, und die Schlüsselliste wird normativ — aber NICHT in `docs/Grammar.md`.** Die
erste Fassung versprach hier „ein Kapitel in der Spezifikation (siehe BP-16), damit die
Schlüsselliste normativ ist", und BP-16 wählte dann Option C: *nur* die Auflösung normativ, das
Dateiformat nicht. Zwei Fragen, zwei Antworten, derselbe Gegenstand — das war ein Widerspruch,
und er ist hier aufgelöst: **das Manifest bekommt ein eigenes normatives Dokument nach dem
Vorbild von `docs/Bytecode.md`**, das ausdrücklich normativ ist und aus `lyric-spec`
(`spec/13-bytecode.md`) gespiegelt wird (gelesen, `docs/Bytecode.md:1-10`). Damit hat ein zweites
Runtime (Lyricpp/Erato 2) etwas zum Implementieren, ohne dass die Grammatik ein Dateiformat
beschreiben muss. BP-16 entscheidet nur noch, was davon in die **Grammatik** gehört.

Ein Formatwechsel dagegen kostet eine Migration und liefert Ästhetik; das Gebiet hat mit BP-03,
BP-05 und BP-13 größere Schulden.

**Bruch:** nein (bei A). B/C wären *major*.
**Hängt ab von:** BP-16 (welches Dokument), Gebiet Standardbibliothek (`std.toml` existiert nicht).

---

### BP-02 — Hat ein Projekt eine Version?

**Heute: nein.** `"version": "1.2.3"` ist ein unbekannter Schlüssel und erzeugt eine Warnung
(gemessen, `p1`; neu bestätigt an `"verison"`, `r6b`). Die sechs bekannten Schlüssel stehen in
`ProjectFile.cs:276-311`.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: kein `version` | heute | ein Lockfile, ein Paket-Tarball, ein `@Deprecated until`-Bezug und jede Fehlermeldung „welche Fassung von X?" haben nichts zum Anfassen |
| B: `version` optional, SemVer, rein informativ | Zig `.version` (ohne Auflösung) | ein Feld, das nichts erzwingt, kann abdriften — bis jemand `lyric package` baut |
| C: `version` Pflicht für Bibliotheken | Cargo, npm | jedes bestehende Bibliotheks-`lyric.json` wird ungültig → Warnphase nötig |

**Empfehlung: B jetzt, C in 5.0 für Bibliotheken.** Eine Version ist die billigste Voraussetzung
für alles Spätere (BP-05, BP-06, BP-17, BP-28) und kostet heute einen `case`-Zweig. Pflicht wird
sie erst, wenn es etwas gibt, das sie liest.

**Bruch:** B nein (additiv, die Warnung verschwindet sogar). C *minor* — 4.7 warnt bei einer
Bibliothek ohne `version`, 5.0 lehnt ab.
**Hängt ab von:** BP-05, BP-06, BP-17, BP-28.

---

### BP-03 — Wem gehört das Segment: dem Konsumenten oder der Bibliothek?

**Heute: dem Konsumenten, und der `name` der Bibliothek wird nie verglichen.** Neu gemessen
(`r17`): `{"banana": "../geo"}` lädt `<geo>/src/banana.lyr`, obwohl die Bibliothek sich `geo`
nennt; das Programm läuft und gibt `7` zurück. Fehlt die Datei → `LYR-RES0003`; trägt sie einen
anderen `module`-Kopf → `LYR-RES0006` (gemessen, `p11`). Eine Bibliothek mit zwei Wurzelmodulen
braucht zwei Einträge auf dasselbe Verzeichnis (gemessen, `r17`: `banana` + `mathx` auf `../geo`,
Programm gibt `10` zurück).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt wie es ist | — | eine Bibliothek hat keine Identität; ein Tippfehler im Segment ist ein „cannot find module" statt „so heißt sie nicht" |
| B: Segment **muss** dem `name` der Abhängigkeit entsprechen, geprüft beim Manifest-Lesen | Go (Import-Pfad = Identität) | eine Bibliothek darf dann nur *ein* Wurzelsegment beanspruchen — heute darf sie mehrere (gemessen, `r17`) |
| C: B, plus ein ausdrücklicher Umbenenner: `{"banana": {"path": "../geo", "as": "geo"}}` | Cargo `foo = { package = "bar" }` | das Wert-Schema wird polymorph (String oder Objekt) — mehr Parser, aber die Form, die BP-04, BP-05 und BP-06 ohnehin brauchen |
| D: `name` + `exports`-Liste: die Bibliothek sagt, welche Wurzelsegmente sie anbietet | Node `exports`, Swift `products` | neues Feld, neue Prüfung; dafür ist ein Paket dann *ein Ding* mit einer erklärten Oberfläche |

**Empfehlung: C, mit D als Erweiterung — aber die Prüfung bleibt WARNUNG, bis BP-04s Umbenenner
existiert.** Das ist gegenüber der ersten Fassung umgedreht, und die Kritik hat den Grund
geliefert: BP-03 wollte die Prüfung für 5.0 und BP-04 schob den Umbenenner, der *innerhalb* einer
Abhängigkeit wirkt, auf „eine eigene Stufe nach v5.0". Damit hätte 5.0 die Regel geliefert, die
Builds bricht, ohne das Mittel, sie zu erfüllen: wer zwei Bibliotheken mit dem Wurzelmodul `json`
hat, bekommt einen Fehler mehr und kein Werkzeug dazu. **Reihenfolge deshalb: 4.7 warnt bei
Segment ≠ `name`; der Umbenenner aus BP-04 landet; erst die Release DANACH macht die Warnung zum
Fehler.** Eine Regel, die bricht, kommt nie vor ihrem Ausweg.

**Bruch:** *minor* als Warnung, *major* erst nach BP-04. Programme mit gleichlautenden Segmenten
merken nichts.
**Hängt ab von:** BP-02, **BP-04 (Reihenfolge!)**, BP-26.

---

### BP-04 — Was passiert, wenn zwei Bibliotheken dasselbe Segment wollen?

**Heute: harter Fehler.** `LYR-CLI0010` nennt beide Forderer mit vollem Pfad und rät „name it in
the project's own lyric.json to decide" (gemessen, `p3`). Der Rat funktioniert nur, wenn eine der
beiden *gemeint* ist — zwei verschiedene Bibliotheken, die beide ein Wurzelmodul `json` haben,
sind unlösbar, weil der Pin nur *eine* Wurzel benennen kann.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt harter Fehler | — | zwei unabhängige Bibliotheken mit gleichem Wurzelnamen können nie im selben Programm sein; bei einem wachsenden Ökosystem eine Zeitbombe |
| B: Umbenennen im Wurzelprojekt (`as`, siehe BP-03 C), wirksam **innerhalb** der Abhängigkeit | Cargo `package =` | die importierenden Dateien der Abhängigkeit schreiben `import json` — der Umbenenner muss also innen wirken. Das ist echte Arbeit im Resolver: eine Segment-Tabelle **pro Projekt** statt einer flachen |
| C: mehrfache Kopien erlauben | npm (⟲ als Ausnahme, nicht als Layout) | Ganzprogramm-Compile + Monomorphisierung: zwei Kopien = zwei Typuniversen, ein `Point` aus Kopie 1 passt nicht in Kopie 2. Die Fehlermeldung dafür ist unerklärbar |
| D: Identität so wählen, dass Kollisionen unmöglich sind (Pfad-als-Name) | Go | der Import im Quelltext würde zu `import github_com_x_y.shapes` o. ä. — hässlich, und bricht jede bestehende Datei |

**Empfehlung: B, und ZUSAMMEN MIT BP-03, nicht danach.** Die flache Tabelle ist heute genau das,
was das Modell einfach macht (`ProjectFile.cs:109-123` begründet es: „FLAT, because the module
namespace is flat"). Sie pro Projekt aufzubohren ist eine Resolver-Änderung, kein Manifest-Feld —
und sie ist die Voraussetzung dafür, dass BP-03s Prüfung überhaupt erfüllbar ist. Solange B nicht
steht, bleibt BP-03 bei der Warnung. **Für 5.0 in jedem Fall:** die Diagnose soll sagen, dass es
*keine* Lösung gibt, wenn die beiden Wurzeln verschiedene Projekte sind, statt einen Rat zu geben,
der dann nicht hilft.

**Bruch:** nein (additiv).
**Hängt ab von:** BP-03; Gebiet *Module & Sichtbarkeit* — ein Umbenenner, der innen wirkt, ist
eine Regel über Importauflösung.

---

### BP-05 — Woher darf eine Abhängigkeit kommen?

**Heute: ausschließlich ein lokaler Pfad**, der beim Lesen existieren muss (gelesen,
`ProjectFile.cs:394-414`; gemessen, `p7`: fehlendes Verzeichnis = `LYR-CLI0010`). Nichts wird
geholt, nichts gecacht. **Folge, die dieses Gebiet an mehreren Stellen entlastet:** jede
Abhängigkeit liegt heute in einem Baum, den der Maintainer selbst kontrolliert.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt Pfad-only | — | jede fremde Bibliothek ist ein Git-Submodul oder ein Kopierjob von Hand; ein Ökosystem entsteht so nicht |
| B: Pfad + `git` + `rev` | Cargo `git`/`rev`, SPM | braucht `git` auf der Maschine (oder eine eigene Implementierung), einen Cache und ein Verzeichnislayout dafür |
| C: Pfad + `url` (Tarball) + `hash` | **Zig** `build.zig.zon` | braucht HTTP + Entpacken + Hashing in der Toolchain — und damit die Bibliotheksfrage aus `STATUS.md` §Still open (HTTP in die std oder nicht) |
| D: B/C + ein Registry-**Index** (Namen → URL), kein Dienst | Go-Proxy, Cargo-Index | jemand muss den Index betreiben; die Design-Runde hat einen Dienst ausdrücklich ausgeschlossen |

**Empfehlung: C, dann B.** Der Tarball-plus-Hash ist die kleinste Form, die *ohne* Fremdwerkzeug
und *ohne* zweiten Pin-Mechanismus auskommt (BP-06), und die, die Zig mit vergleichbarem Zuschnitt
gewählt hat. `git`+`rev` danach. Registry-Index nicht vor einem Ökosystem, das ihn braucht.
Dazu gehören **drei Werkzeuge, die es noch nicht gibt**: `lyric package` (BP-28 definiert, was
hineinkommt), `lyric update`, und die Schalter aus BP-31 (`--offline`, `--locked`).

**Bruch:** nein (additiv; Pfad bleibt).
**Hängt ab von:** BP-02 (`version`), BP-03 (Objektform), BP-26 (was mit `nativeRoots` passiert),
BP-28 (Dateiliste), BP-31, Gebiet Standardbibliothek (HTTP/TLS, Kompression, Hashing) und Gebiet
Capabilities (was darf der Fetcher).

---

### BP-06 — Wie wird gepinnt: Lockfile, Hash im Manifest, oder gar nicht?

**Heute: gar nicht.** Ein Pfad ist ein veränderliches Verzeichnis; nichts hält fest, was gebaut
wurde. Es gibt keine Datei, die einen Stand beschreibt (gemessen, `p6`: in `out/` liegen nur
`.lyrbc`, keine Metadaten).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: separater Lockfile `lyric.lock` | Cargo, npm, Deno, Nix | zweite Datei, zweite Wahrheit, die ewige Frage „einchecken oder nicht"; verlangt `--frozen`/`--locked` als weitere Schalter |
| B: **Hash im Manifest**, pro Abhängigkeit | **Zig** | keine zweite Datei, kein `--frozen`, der Diff zeigt die Änderung an der Stelle, an der sie steht. Preis: das Manifest ist dann *auch* ein Lockfile — ein Update ist ein Werkzeugaufruf (`lyric update`) |
| C: nichts pinnen | heute | nicht reproduzierbar, sobald etwas anderes als ein Pfad erlaubt ist |

**Empfehlung: B.** Das ist Rule 2 auf das Pinnen angewandt: ein Mechanismus für „was genau wird
gebaut". Pfad-Abhängigkeiten bekommen keinen Hash — ein Pfad ist dein eigener Baum. Der Hash deckt
genau die Quellen, die von außen kommen.

**Was B trägt, und was die erste Fassung nicht wusste:** die Artefakte sind byteweise
reproduzierbar (gemessen, `r10`/`r5`, siehe §1.3 und BP-29). Ein Hash über die *Eingaben* ist
damit überhaupt erst sinnvoll — wäre die Ausgabe nichtdeterministisch, hätte ein Eingabe-Hash
nichts zu versprechen. Der Algorithmus gehört ins normative Manifest-Dokument (BP-01), nicht in
den Compiler.

**Bruch:** nein (additiv, greift erst mit BP-05).
**Hängt ab von:** BP-05, BP-28 (was in den Hash eingeht), BP-29.

---

### BP-07 — Braucht v5 eine Versionsauflösung?

**Heute: nein, weil es keine Versionen gibt.** Der Abschluss ist eine reine Segmenttabelle;
ein Konflikt ist ein Fehler, kein Auflösungsproblem (gelesen, `ProjectFile.cs:124-196`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: keine Auflösung — exakte Pins, Diamant ist Handarbeit | **Zig**, Nix | bei drei Bibliotheken, die dieselbe vierte verlangen, editiert man das Wurzelmanifest. Skaliert schlecht ab ~20 Paketen |
| B: MVS | **Go** | deterministisch, kein Solver, erklärbar. Braucht Versionen (BP-02) und ein Verständnis von SemVer-Kompatibilität, das ohne ABI-Prüfung eine Behauptung bleibt |
| C: SemVer-Unifikation mit Solver | Cargo, npm | ein Solver, Fehlermeldungen, die niemand liest, und die Möglichkeit mehrerer Majors — die Lyric wegen Monomorphisierung nicht nutzen kann |

**Empfehlung: A, ausdrücklich und begründet.** Solange „ein Segment, eine Wurzel" gilt — und die
Ganzprogramm-Kompilation (BP-18) macht alles andere teuer — kauft ein Solver nichts: er darf am
Ende genau eine Fassung wählen, und bei einem Widerspruch muss er dieselbe Fehlermeldung
schreiben, die heute schon steht. Wenn der Diamant später weh tut, ist **B (MVS)** der nächste
Schritt, nicht C. Das ist bewusst die Gegenposition zu Cargo und npm.

**Bruch:** nein.
**Hängt ab von:** BP-04, BP-02, BP-18.

---

### BP-08 — Inkrementeller Build und Cache: ja oder nein?

**Heute: nein.** Jeder Lauf kompiliert jedes Artefakt vollständig neu und schreibt die Datei neu
(gelesen, `BuildSession.cs:173-191` — kein Zeitstempel-, Hash- oder Cache-Zweig; gemessen, `p1b`:
zweiter Lauf, identische Ausgabe, kein „up to date").

**Was es kostet — vollständiger als in der ersten Fassung** (alles gemessen, `r8`; die Tabelle in
§1.3 hat die Einzelwerte):

| Fall | warm |
|---|---|
| 400 Module, ein Artefakt | 0,8–1,2 s |
| 400 Module, drei Artefakte über denselben Baum | 1,7–1,8 s (**1,7×**, nicht 3×) |
| `lyric test` über denselben Baum | 1,0–1,2 s |
| CI: `build` **und** `test` | 1,9 s — **zwei volle Kompilationen desselben Baums** |
| kalt | **nicht gemessen**; die Kritik berichtet 36 s auf derselben Probe |

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts tun | heute | der warme Fall ist billig; der kalte ist unvermessen, und ein CI zahlt ihn bei jedem Lauf |
| B: Fingerprint pro Artefakt in `out/<profil>/.fingerprint/` (Quelldateien + Hashes + Optionen + Toolchain-Version) | Cargo | der Compiler muss melden, *welche* Dateien er gelesen hat; ein falscher Fingerprint ist ein stiller Fehlbau — die schlimmste Fehlerklasse, die ein Build haben kann |
| C: inhaltsadressierter Cache, Ausgabepfad trägt den Hash | Zig, Nix, Go | dasselbe Problem plus Cache-Verwaltung (`lyric clean --cache`), dafür ist ein veraltetes Artefakt strukturell unmöglich |
| D: **den Compile teilen statt ihn zu wiederholen** — mehrere Artefakte über demselben Baum, und `build`+`test`, in einem Prozess mit einem geladenen Modulgraphen | Bazel (eine Ladephase, viele Aktionen), Zig (ein Step-Graph) | greift genau die zwei gemessenen Mehrfachkompilationen ab, ohne je einen Fingerprint zu vertrauen. Preis: `BuildSession.cs:159-161` ist eine sequentielle Schleife über unabhängige Compiles; sie zu einem gemeinsamen Graphen zu machen ist Frontend-Arbeit |

**Empfehlung: A für 5.0 — aber nicht mehr „weil der Preis klein ist", sondern mit einer
genannten Auslösebedingung; und D vor B.** Die erste Fassung schloss aus warmen Zahlen eines
*einzigen* Artefakts, der Preis sei klein. Das ist für den warmen Einzelfall belegt und sonst
nicht. Was die neuen Messungen zeigen: der teure Fall ist nicht „nochmal dasselbe bauen", sondern
**denselben Baum mehrfach im selben Lauf kompilieren** — drei Artefakte, oder `build` plus `test`.
Genau das löst **D**, und zwar ohne die Fehlerklasse zu kaufen, die B mitbringt (ein falscher
Fingerprint ist ein stiller Fehlbau, und dieses Gebiet hat davon schon drei: Befund 1, 2, 14).

**Die Auslösebedingung, damit die Entscheidung nachprüfbar bleibt:** B/C werden wieder
aufgemacht, sobald *eine* dieser Zahlen erreicht ist — (a) ein kalter CI-Lauf über 10 s, (b) ein
warmer Lauf des größten realen Projekts über 5 s, (c) kompilierte Bibliotheken (BP-09) existieren,
weil sich dann das Verhältnis Quelle/Artefakt ändert. Ohne eine dieser Zahlen bleibt es bei A+D.

**Bruch:** nein.
**Hängt ab von:** BP-09, BP-15, BP-29 (ohne reproduzierbare Ausgaben ist C sinnlos), BP-30.

---

### BP-09 — Gibt es eine kompilierte Bibliothek?

**Heute: nein.** Eine Bibliothek wird *geprüft* und schreibt nichts (gelesen,
`BuildSession.cs:196-224`; `docs/guide/16-building.md`: „A library has no `build.lyr`, because
there is nothing to build"). Jede Abhängigkeit geht als Quelltext in jedes Artefakt. Die
Design-Runde hat Stufe B — „kompilierte Lyric-Bibliotheken mit Header in `api/`, Sektion Interface
(Format 4.1)" — beschlossen und nicht gebaut (gelesen, Memo `design-round-2026-09-libraries`).

**Der Haken, der überall fehlt:** **Monomorphisierung.** Eine generische Funktion kann nicht
vorkompiliert werden — sie existiert erst, wenn der Konsument die Typargumente einsetzt. Rust
löst das, indem eine `.rlib` **MIR mitliefert**; C++ löst es mit Templates im Header. Eine
kompilierte Lyric-Bibliothek muss also entweder (a) ihre generischen Teile als Quelle oder IR
mitliefern, oder (b) generische `pub`-Oberfläche verbieten. (b) würde `List<T>` aus jeder
Bibliothek verbannen — praktisch unbrauchbar. Also (a), und damit ist die „kompilierte
Bibliothek" ein *Bündel* aus Modul plus IR, kein reines `.lyrbc`.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt Quelle | Zig (Pakete sind Quelle), C-Header-only | ehrlich, einfach, und jede Änderung einer Bibliothek kostet den vollen Neubau jedes Konsumenten. Der Quelltext ist immer da — für eine proprietäre Bibliothek ein Ausschlussgrund |
| B: `.lyrbc`-Bibliothek + Header in `api/`, generische Teile als Quelle daneben | Rust `.rlib` (MIR), Swift `.swiftmodule` | Formatarbeit (Interface-Sektion), eine ABI-Zusage, ein Header-Check — und die ehrliche Ansage, dass der generische Teil weiterhin Quelle ist |
| C: IR-Bibliothek (alles als IR, nichts als Bytecode) | .NET-Assembly, JVM-Class | der IR ist heute ausdrücklich *kein* stabiles Format (`docs/Bytecode.md` beschreibt das Modul, nicht den IR); ihn zu stabilisieren ist ein eigener Major |

**Empfehlung: B, und der Monomorphisierungs-Haken gehört ausdrücklich in die Entscheidung.** Der
Gewinn ist *Kapselung und Verteilbarkeit*, nicht Bauzeit — die generische Hälfte wird weiterhin
bei jedem Konsumenten neu monomorphisiert. **Und B ist der Punkt, an dem BP-18 sich entspannt:**
eine vorkompilierte Bibliothek ist die einzige Form, in der eine Abhängigkeit *nicht* durch den
Parser des Konsumenten muss — womit eine Toolchain-Auswahl pro Projekt (BP-13 C) zum ersten Mal
leisten könnte, was sie bei Go leistet. Für den generischen Rest gilt das weiterhin nicht.

**Bruch:** nein (additiv).
**Hängt ab von:** Gebiet Bytecode/Format (Interface-Sektion, Format 4.1), Gebiet Generics,
BP-17, BP-18.

---

### BP-10 — Was ist ein Build-Skript: Programm, Sandkasten, Graph oder Daten?

**Heute: ein Programm mit allen Rechten.** `Capabilities = Capability.All` (gelesen,
`src/Lyrbuild/Program.cs:336`); es darf schreiben und Prozesse starten, und `lyrbuild --help` sagt
das selbst („A build script runs with every capability: it may write files and start processes,
like make or cmake"). **Und `--help` führt es aus** (gemessen, `r3`). Die gute Hälfte ist schon
da: *während* `build()` läuft, wird nichts kompiliert — `finish()` übergibt danach (gelesen,
`stdlib/std/build.lyr:191-205`, `BuildSession.cs:31-35`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt wie es ist | make, cmake, **Zig** | `lyric build` und sogar `lyric build --help` auf einem fremden Checkout sind Vertrauensakte; ein Editor kann den Build nie ohne Ausführung kennen |
| B: `build()` läuft **gesandboxt** (lesen im Projekt, kein Schreiben, kein Prozess); alles, was *tut*, wird als **Schritt deklariert** und vom Runner ausgeführt | **Bazel** (Starlark ohne E/A, Rekursion verboten), Zig (Step-Graph), Swift SPM (⟲ Sandkasten nur auf macOS) | bricht jedes Skript, das `writeText` in `build()` aufruft — und genau das steht als Beispiel im Guide. Braucht neue Formen: `generated(...)`, `step(...)`, `after()` wird ein Schritt |
| C: gar kein Skript, nur Daten + Task-Runner | **Go**, Deno `deno task` | jede nichttriviale Generierung wandert in ein Makefile daneben — also aus dem Werkzeug heraus |
| D: Optionen ins Manifest, Skript bleibt sonst wie es ist | — | löst nur `--help`, lässt den Rest |

**Empfehlung: B, mit D als 4.x-Vorarbeit — und mit einer Form, die die erste Fassung in einer
Klammer versteckt hat.** B löst drei Probleme gemeinsam: `--help` führt nichts mehr aus, ein
Editor kann den Build lesen (BP-27), und ein Cache (BP-08 C/D) wird überhaupt erst denkbar. Dass
`finish()` heute schon nach `build()` läuft, zeigt, dass die Trennung gedacht ist.

**Was B kostet und die erste Fassung nicht verrechnet hat:** die Begründung für BP-17 B lautete,
es mache den generierten Quelltext „überflüssig" — das trifft nur den **Options**-Fall.
Codegenerierung aus einem Schema, einer Tabelle oder einer Fremddatei bleibt übrig und hätte unter
B **gar keinen Weg mehr**. Genau diesen Preis nennt das Dossier bei Gos Option C („jede
Sonderlocke wird zu einem Makefile daneben") und verrechnete ihn bei der eigenen Empfehlung
nicht. Die fehlende Form heißt bei Swift **build tool plugin** und bei Bazel **Aktion mit
erklärten Ein- und Ausgaben**. Sie gehört ausdrücklich in B:

> `generated(name, inputs: [...], outputs: [...], run: ...)` — vom Skript **deklariert**, vom
> Runner **ausgeführt**, mit erklärten Ein- und Ausgaben, damit der Lauf cachebar und der Editor
> lesbar bleibt. Ohne diese Form ist B kein Ersatz für A, sondern eine Amputation.

**Kurzfristig, unabhängig davon:** `--help` sollte laut sagen, dass es das Skript startet.

**Bruch:** **major.** 4.7 warnt bei jedem Aufruf mit Wirkung in `build()` („dies wird in 5.0 ein
Schritt sein"); der Nachweis ist die Capability, die der Aufruf verlangt — die kennt der Compiler.
5.0 entzieht die Capabilities.
**Hängt ab von:** Gebiet Capabilities/Sandbox, BP-08, BP-11, BP-17, BP-27.

---

### BP-11 — Welchen Modulraum hat ein `build.lyr`, und darf es Abhängigkeiten haben?

**Die Frage ist gegenüber der ersten Fassung erweitert**, weil der Ist-Stand enger ist als dort
beschrieben. `ScriptOptions` setzt `StdlibRoot` und `SourceRoot = <Projektverzeichnis>` (gelesen,
`src/Lyrbuild/Program.cs:216-217`) — **nicht** den `sourceRoot` des Projekts. Ein `build.lyr`
sieht also weder die `dependencies` noch die `nativeRoots` **noch den eigenen Modulraum**.
Gemessen (`r11`): `import shared` → `LYR-RES0003`; `import src.shared` → `LYR-RES0006`, weil die
Datei `module shared;` deklariert. Gemessen (`p8`): `import geo` im Skript → `LYR-RES0003`,
dasselbe in `src/main.lyr` → baut. **Ein Skript, das aus einer eigenen Quelle unter `src/` etwas
generieren will, ist schon heute auf Pfadbastelei angewiesen** — und die funktioniert nicht,
sobald die Datei einen `module`-Kopf trägt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | — | ein Skript darf nur `std` benutzen; alles andere wird kopiert. Der Fall aus `r11` bleibt unlösbar |
| B: das Skript sieht die `dependencies` und den `sourceRoot` des Projekts | — | zwei Zeilen; aber Build- und Laufzeit-Abhängigkeiten sind dann dieselben, was bei BP-10 B kollidiert, und das Skript kann sich selbst in den Baum kompilieren, den es baut |
| C: eigenes `buildDependencies` **und** ein eigener, erklärter Skript-Root (z. B. `build/` neben `build.lyr`) | **Cargo** `[build-dependencies]` + `build.rs`-Quellbaum | zwei Felder mehr, eine Tabelle mehr im Abschluss — dafür ist der Modulraum des Skripts eine erklärte Sache statt eines Nebeneffekts von `ScriptOptions` |
| D: C, plus das Skript darf den `sourceRoot` des Projekts **lesend** sehen | Swift build tool plugin (Eingaben erklärt) | der Fall aus `r11` wird lösbar, ohne dass Skript und Programm denselben Graphen teilen |

**Empfehlung: C, mit D als Erweiterung — und B weiterhin nicht.** Die Trennung ist der Punkt: was
das Skript benutzt, landet nicht im Programm, und was das Programm benutzt, muss das Skript nicht
sehen. Aber die erste Fassung hat die eigentliche Frage nicht gestellt: **welchen Modulraum hat
ein `build.lyr` überhaupt?** Heute keinen erklärten, und `buildDependencies` allein beantwortet
das nicht. Deshalb gehören beide Hälften in eine Entscheidung: eine eigene Abhängigkeitstabelle
**und** ein eigener Wurzelort für die Module des Skripts.

**Bruch:** nein (additiv). D ist ein Verhaltenswechsel für Skripte, die heute mit Pfadbastelei
arbeiten — es macht mehr möglich, nichts unmöglich.
**Hängt ab von:** BP-10, BP-25 (dieselbe Frage für Tests).

---

### BP-12 — Zwei feste Profile oder benannte Profile?

**Heute: genau zwei, fest verdrahtet.** `Profile.Named` kennt `debug` und `release` und sonst
nichts (gelesen, `Profile.cs:51-56`). Neu gemessen (`r17`): `--profile staging` →
`error[LYR-CLI0003]`, Exit 2; `LYRIC_PROFILE=staging` → baut still nach `out/debug/`, Exit 0.
Dieselbe Situation, zwei Antworten.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: zwei bleiben | Go (nur `-gcflags`), heute | jeder Sonderfall (ein Release mit Debug-Info, ein Profiling-Build) ist eine Flag-Kombination, die man sich merken muss — **und die es im Build heute gar nicht gibt**, siehe BP-24 |
| B: benannte Profile in `lyric.json`, mit `inherits` | **Cargo** `[profile.x] inherits = "release"` | das Profil ist Projekteigenschaft, also **ohne Skript lesbar** — passt zur Doktrin „`lyric.json` wird gelesen, nie ausgeführt". Preis: `Profile.Named` wird tabellengetrieben, und jedes Werkzeug muss das Projekt kennen, bevor es ein Profil auflösen kann |
| C: benannte Profile im `build.lyr` | Zig (`b.standardOptimizeOption`) | ein Editor müsste das Skript ausführen, um zu wissen, was `--profile staging` heißt — gegen BP-10 |
| D: Profil-Überschreibungen pro Abhängigkeit | Cargo `[profile.dev.package.foo]` | setzt getrennt kompilierte Abhängigkeiten voraus (BP-09); heute sinnlos, weil alles ein Compile ist |

**Empfehlung: B, zusammen mit BP-24 — sie sind zwei Hälften einer Frage.** Ein benanntes Profil
im Manifest ist die Antwort auf „dieses Projekt hat drei Bauarten"; ein Feldschalter auf der
Kommandozeile ist die Antwort auf „dieser eine CI-Lauf will release, aber keine Warnungen".
Beides zu haben ist kein zweiter Mechanismus, sondern eine **Vorrangregel**, und die muss
hingeschrieben werden (BP-24). **Und `LYRIC_PROFILE` mit unbekanntem Namen muss warnen** — der
Kommentar „a library has nowhere to complain" (`Profile.cs:41-45`) beschreibt `Profile.Default`
als statisches Feld, nicht die Werkzeuge, die es lesen.

**Bruch:** B nein (additiv). Die Warnung bei `LYRIC_PROFILE` ist ein *minor*-Verhaltenswechsel —
4.7 warnt, und mehr braucht es nie.
**Hängt ab von:** BP-09 (für D), **BP-24**.

---

### BP-13 — Toolchain-Pinnung: wie kommt ein 4er-Projekt über einen 5er-Compiler?

> **Die wichtigste Frage des Gebiets — und die, deren Empfehlung sich durch die Kritik geändert
> hat.** Die erste Fassung empfahl „C, ausdrücklich gegen B" auf zwei Grundlagen, von denen eine
> falsch und die andere unbeantwortet war: eine überzeichnete Darstellung der Editionskosten
> (siehe §2) und die nie gestellte Frage, was eine Toolchain-Auswahl *pro Projekt* in einer
> Sprache leisten kann, die den ganzen Abschluss in **ein** Compile zieht (jetzt **BP-18**).

**Heute ist `toolchain` nur ein Minimum**: `ToolchainVersion.Satisfies` prüft `current >= required`
und nichts sonst (gelesen, `src/Lyric.Core/ToolchainVersion.cs`). Es gibt **keine Obergrenze, kein
exaktes Pinnen und keine Sprach-Edition**. Am Tag, an dem `lyric 5.0` installiert wird, wird
**jedes** 4er-Projekt gegen die 5er-Sprache kompiliert. Und das Minimum wirkt auch aus Projekten
heraus, die gar nichts beitragen (gemessen, `r1`: das weggepinnte `geoB` mit `"toolchain": "9.9"`
bricht den Bau).

**Was das Projekt bereits entschieden hat** (gelesen, `STATUS.md:40-51`): „THE OPEN RULE QUESTIONS
ARE ANSWERED, AND THE ANSWER IS 'WITH v5'" — bis dahin läuft nur eine **Warnung**. Vier Uhren
laufen (`LYR-SEM0107`–`LYR-SEM0110`). Das ist faktisch bereits das Stichtagsmodell mit Vorlauf.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: **Stichtag mit Uhren** — jede 5.0-Regel warnt ab 4.x, `lyrfix` migriert, 5.0 ist die Sprache | Python 2→3 (⟲ **nicht** Perl 6, siehe §2) | alle migrieren gleichzeitig. Verkraftbar, **solange jede Abhängigkeit im eigenen Baum liegt** — und genau das ist heute so (gelesen, `ProjectFile.cs:394-414`: nur lokale Pfade). `lyrfix` muss jeden Bruch können |
| B: **Editionen** — ein Compiler trägt beide Dialekte | **Rust** (2015/2018/2021/2024) | ⟲ Rusts Kosten sind begrenzt (kein Typsystem, keine Trait-Auflösung, keine stdlib; ein IR, eine Bibliothek, `cargo fix --edition`) — **aber genau diese Grenze macht B für Lyric unbrauchbar**: Lyrics vier 5.0-Uhren sind Regeln über Mutabilität, Bindung und Werfen, also Semantik, nicht Oberfläche. Eine Edition, die sie tragen dürfte, träfe auf ihre Gegenedition **innerhalb eines Moduls** (BP-18) |
| C: **Toolchain-Auswahl** — `toolchain` darf exakt oder als Bereich pinnen, der Treiber besorgt und startet die passende | **Go** `GOTOOLCHAIN` (⟲ eigene `toolchain`-Direktive; nur bei `GOTOOLCHAIN=auto`), rustup `rust-toolchain.toml` (⟲ **nicht** `.swift-version`) | braucht einen Downloader, einen Cache und ein Verzeichnis. **Und leistet in Lyric nicht, was es bei Go leistet**: Go übersetzt jedes Modul für sich, Lyric nicht (BP-18). C kann einem *unveränderten* Projekt Zeit kaufen; es kann keine zwei Sprachfassungen in einem Programm nebeneinander bringen |
| D: nichts — es bleibt beim Minimum | heute | 5.0 ist ein Flag Day, und zwar ein unangekündigter |

**Empfehlung: A **und** C zusammen, B abgelehnt — aus einem anderen Grund als in der ersten
Fassung.**

1. **A ist das Modell, das bereits läuft**, und es ist heute billig, weil es kein Ökosystem gibt,
   das nicht mitziehen kann: jede Abhängigkeit ist ein Pfad im eigenen Baum. Der Preis eines
   Stichtags steigt mit BP-05, nicht vorher.
2. **C ist kein Ersatz für A, sondern die Bremse daneben.** Es kauft einem Projekt, das nicht
   migrieren will, Zeit — der Treiber startet die 4er-Toolchain, und `lyric 5.0` auf der Maschine
   ist kein Ereignis mehr. Was C ausdrücklich **nicht** kann, muss dabeistehen: eine 4er-Abhängigkeit
   und ein 5er-Projekt gehen nicht, weil sie in einem Compile landen. Darum ist der Pin eine
   Eigenschaft der **Wurzel** und gilt für den ganzen Abschluss; eine Abhängigkeit, deren Pin dem
   der Wurzel widerspricht, ist ein Fehler, der beide Pins nennt.
3. **B fällt**, aber nicht mehr wegen „zwei Sprachregeln für immer" — dieses Argument war
   überzeichnet. Es fällt, weil Lyrics Brüche **semantisch** sind und die Editionsgrenze bei
   Lyric **innerhalb eines Moduls** läge. Eine Edition, die nur Oberfläche ändern darf (Rusts
   Regel), trägt keine einzige der vier laufenden Uhren.

**Konkret für 5.0:**
1. `toolchain` bekommt neben dem Minimum eine **Obergrenze** oder ein exaktes Pin
   (`">=4.5 <5"` oder `{"min": "4.5", "max": "4"}` — die Form ist BP-01s Frage).
2. Der Pin gilt nur in der **Wurzel** (Gos `replace`-Regel, konsequent angewandt); der einer
   Abhängigkeit wird nicht gelesen, sondern gegen den der Wurzel **geprüft** — und ein
   weggepinntes Projekt gar nicht erst gelesen (Befund 1).
3. Ein 4er-Projekt ohne Obergrenze wird von `lyric 5.0` **mit einer Warnung** gebaut, die sagt,
   was zu tun ist.
4. **Ab 4.7** schreibt `lyric new` die Obergrenze mit, und `lyric build` warnt bei einem Projekt
   ohne `toolchain`-Schlüssel. Das ist die Uhr, die heute noch nicht läuft.
5. `lyric toolchain install/use` als eigenes Verb, wenn (1)–(4) stehen.
6. **`addExecutable` ist die einzige Uhr des Gebiets, die schon auf 5.0 steht** (gelesen,
   `stdlib/std/build.lyr:179-186`; `grep -rn "until" stdlib/std --include=*.lyr` liefert genau
   diesen einen `@Deprecated`-Treffer). 5.0 entfernt sie — und `lyrfix` muss den Aufruf
   umschreiben können (`addExecutable(entry, output)` → `executable(name, entry)` plus
   `artifact.output = output`), sonst ist der erste Bruch der Sprache ausgerechnet einer im
   Build-Skript.

**Bruch:** *minor* (die Schlüsselerweiterung ist additiv; die Warnung ändert kein Programm). Der
eigentliche Bruch ist 5.0 selbst, und diese Frage entscheidet, ob er weh tut.
**Hängt ab von:** **BP-18** (die Voraussetzung), BP-09 (entspannt BP-18 teilweise), BP-33 (welche
`std` eine gepinnte Toolchain mitbringt), BP-05 (ab wann A teuer wird), `lyrfix`.

---

### BP-14 — Workspaces: ein Repository, mehrere Projekte

**Heute: kein Begriff davon.** Jedes Verzeichnis mit `lyric.json` ist ein eigenes Projekt mit
eigenem `out/`. Ein `build.lyr` darf per `../` in Nachbarverzeichnisse greifen — und das ist
**kein Komfortmangel, sondern ein stiller Fehlbau** (neu gemessen, `r9`, Befund 14): das fremde
Programm wird gegen den Modul-Root des *bauenden* Projekts kompiliert und bekommt fremde Module
untergeschoben, ohne jede Diagnose.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts — ein Repository, viele Projekte, jedes für sich | Zig | `lyric test` über alle Mitglieder muss eine Schleife in der Shell sein; jedes Projekt braucht die Abhängigkeitspfade der anderen von Hand |
| B: `"workspace": ["a", "b"]` — ein `out/`, ein Build, ein Testlauf | **Cargo** `[workspace]`, **Go** `go.work`, npm `workspaces` | neuer Begriff, neue Regeln (welches `lyric.json` gewinnt, wohin geht `out/`), und der Abschluss muss Mitglieder von Abhängigkeiten unterscheiden. **Setzt BP-23 voraus**: ohne eine Projektgrenze ist nicht entscheidbar, wer Mitglied ist |
| C: die Lücke schließen — ein Artefakt mit fremdem Entry bekommt den Source-Root *seines* Projekts | — | kleine Korrektur an `BuildSession.OptionsFor` (`BuildSession.cs:242`), semantisch heikel: welches Projekt „gehört" zu einem Entry? |
| D: ein fremder Entry ist ein **Fehler** | Go (ein Paket baut nur im eigenen Modul) | die einfachste Regel, und sie macht `r9` unmöglich. Preis: wer heute so baut, muss auf eine Pfadabhängigkeit umstellen |

**Empfehlung: D für 5.0, B danach und nur mit BP-23.** Die erste Fassung empfahl „A, und die Lücke
wenigstens benennen". Nach `r9` ist das zu wenig: ein Bau, der fremden Code unterschiebt und
Erfolg meldet, gehört nicht benannt, sondern abgestellt. **D ist die Regel ohne Restrisiko** — und
sie ist ab sofort warnbar, ohne jede Designentscheidung: ein `entry`, der nicht unter dem
`sourceRoot` des bauenden Projekts liegt, bekommt ab 4.7 eine Warnung, 5.0 einen Fehler, und der
Ausweg steht in der Meldung (eine Pfadabhängigkeit, oder ein Workspace, wenn es ihn dann gibt).

**Bruch:** D ist *minor* (4.7 warnt, 5.0 lehnt ab). B nein.
**Hängt ab von:** **BP-23** (Projektgrenze), BP-05.

---

### BP-15 — Artefakte: Kollisionen, `--only`, `clean`, die doppelte Ableitung

**Heute**, alles gemessen bzw. gelesen — und gegenüber der ersten Fassung an **zwei** Stellen
korrigiert:

- **Die Kollision ist der Ausgabepfad, nicht der Name** (gemessen, `r15`, Befund 2): gleicher
  Name mit verschiedenem `output` baut sauber und ist legitim; verschiedene Namen mit gleichem
  `output` überschreiben sich still, und ausgeführt wird der zweite. `Artifact.output` ist frei
  setzbar (`stdlib/std/build.lyr:88`; gemessen, `r6`).
- **Der ungeprüfte Name hängt am fehlenden `name`-Schlüssel, nicht am fehlenden `lyric.json`**
  (gemessen, `r4`/`r4b`, Befund 11): `ProjectFile.cs:267` nimmt den Verzeichnisnamen als Vorgabe,
  `ReadName` läuft nur im `case "name"`.
- `--only app` trifft `executable` **und** `packed` (gemessen, `r15`).
- `out/` wird nie aufgeräumt, kein `clean`-Verb (gemessen `p6`, gelesen `Lyric.Cli/Program.cs:29-44`).
- Die Ableitung `out/<profil>/<name>` steht zweimal (`stdlib/std/build.lyr:102-107` und
  `src/Lyrbuild/Program.cs:425-426`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: Einzelkorrekturen, **korrigiert**: (1) **zwei Artefakte mit demselben Ausgabepfad sind ein Fehler**; (2) `--only` nimmt `kind:name`, oder `packed` bekommt einen eigenen Namen; (3) `lyric clean`; (4) die Ableitung nur noch in `std.build`; (5) **der aus dem Verzeichnis abgeleitete Name wird geprüft wie ein geschriebener** | Cargo (`cargo clean`, `--bin`/`--lib`) | fünf kleine Änderungen; nur (1) ist ein Bruch, und ein engerer als in der ersten Fassung — er trifft nicht doppelte Namen, sondern doppelte Ziele |
| B: Ausgabepfad trägt den Fingerprint (`out/<hash>/`), `clean` wird überflüssig | Nix, Zig, Go | siehe BP-08 — und ein Pfad, den man nicht vorhersagen kann, ist für ein `cp out/release/app dist/` unbrauchbar. Bräuchte einen `latest`-Link |
| C: nichts | — | ein Build, der zweimal Erfolg meldet und einmal liefert, bleibt drin |

**Empfehlung: A in der korrigierten Form.** Zu (5): die Prüfung darf den Bau nicht brechen, wo sie
heute keinen bricht — **4.7 warnt** („der Verzeichnisname `9bad-name` ist kein Modulname; setze
`name`"), 5.0 lehnt ab; damit trifft die Regel ihren tatsächlichen Auslöser. Zu (1): die
Fehlermeldung nennt **beide Artefakte und den gemeinsamen Pfad**, nicht den Namen, sonst ist sie
über den `output`-Fall unverständlich. Zu (2): `packed(app, "app_dist")` ist die sauberere Lösung
als ein Präfix im `--only`, weil ein eigener Name auch den eigenen Ausgabepfad mitbringt und damit
(1) erfüllt.

**Bruch:** *minor* für (1) und (5) — 4.7 warnt, 5.0 lehnt ab. Der Rest additiv.
**Hängt ab von:** BP-30 (ein atomarer Schreibvorgang gehört in dieselbe Ecke), BP-19 (ein
zweites Ziel wäre eine dritte Achse im Ausgabepfad).

---

### BP-16 — Steht das Projektmodell in der Spezifikation?

**Heute: nein.** `grep -in` über `docs/Grammar.md` nach `lyric.json`, `sourceRoot`, `dependenc`,
`toolchain` liefert **genau einen** Treffer, und der steht in der Mirror-Fußnote an Zeile 3
(neu gemessen). Die sechs Schlüssel, der flache Abschluss, die Pin-Regel, `LYR-CLI0010`/`0018` —
alles compiler-only. Das steht gegen die eigene Doktrin „spec-first".

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt außerhalb — Projektmodell ist Werkzeugsache | Go (Modulmechanismus in der Doku, nicht in der Spec), Zig | ehrlich, hält die Spec klein. Preis: ein zweites Runtime (Lyricpp/Erato 2) hat nichts, wogegen es baut |
| B: eigenes Kapitel in `Grammar.md` mit normativen Schlüsseln, Auflösungsregeln und Diagnosecodes | JPMS im JLS-Umfeld | die Grammatik beschriebe ein Dateiformat, das keine Grammatik hat |
| C: nur die **Auflösung** normativ, das Dateiformat nicht | — | die halbe Schuld getilgt — aber dann fehlt der Schlüsselliste weiterhin jede Norm, und BP-01 verspricht sie |
| D: **C für die Grammatik, plus ein eigenes normatives Dokument fürs Manifest**, nach dem Muster von `docs/Bytecode.md` | `docs/Bytecode.md` (gelesen, `:9`: „This document is normative"), gespiegelt aus `lyric-spec` | ein drittes Dokument zu pflegen. Dafür: die Grammatik bleibt Sprache, das Manifest bekommt eine Norm, und beide sind spec-first |

**Empfehlung: D — und damit ist der Widerspruch zu BP-01 aufgelöst.** Die erste Fassung wählte
hier C und versprach in BP-01 gleichzeitig eine normative Schlüsselliste; das ging nicht
zusammen. D gibt beiden recht: **in die Grammatik** gehört, was Programmverhalten bestimmt — die
Auflösungsregel und `LYR-RES0006` („loaded as X, declares Y", gemessen, `r11`) sind Sprachregeln,
die heute nur im Compiler stehen. **In ein eigenes normatives Dokument** gehört das Manifest: die
sechs (dann mehr) Schlüssel, ihre Typen, die Pin-Regel, die Fehlercodes. Der Präzedenzfall
existiert und funktioniert seit Format 1.0.

**Bruch:** nein.
**Hängt ab von:** BP-01, BP-03, BP-22, BP-23 (alles, was normativ werden soll, muss vorher
entschieden sein).

---

### BP-17 — Wie kommen Build-Optionen ins Programm? (bedingte Kompilierung)

**Heute: nur über generierten Quelltext.** `option("version", …)` liefert dem *Skript* einen
String; damit das Programm ihn sieht, schreibt das Skript eine `.lyr`-Datei — so steht es als
Beispiel im Guide (`docs/guide/16-building.md`). Es gibt keine `#[cfg]`-artige Form, keine
Feature-Flags, kein `comptime`, das eine Build-Option lesen kann.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt bei generiertem Quelltext | — | funktioniert, schreibt aber in den Quellbaum — genau die Sorte Skript, die BP-10 B verbietet |
| B: `comptime buildOption("name")` — der Compiler liefert den Wert als Literal | Zig `@import("build_options")`, D `version =` | `comptime` bekommt eine Quelle, die nicht im Quelltext steht; der Reproduzierbarkeitssatz muss von „dieselbe Quelle" zu „dieselbe Quelle *und dieselben Optionen*" werden |
| C: Feature-Flags wie Cargo | Cargo, C `#ifdef` | ein **zweiter** Mechanismus neben `comptime` — Rule 2 verletzt; dazu Feature-Unification, Cargos eigener Dauerschmerz |
| D: Dateinamens-Konvention (`x_linux.lyr`) | **Go** | kein neuer Mechanismus in der Sprache, aber einer im Modulsystem — und er beantwortet nur *Plattform*, nicht *Option* |

**Empfehlung: B — aber ohne die Behauptung, es mache A überflüssig.** B benutzt den Mechanismus,
den es schon gibt, und ist die Form, die Zig mit demselben `-D`-Modell gewählt hat. Was die erste
Fassung überzogen hat: B deckt den **Options**-Fall ab, nicht die Codegenerierung aus einem
Schema, einer Tabelle oder einer Fremddatei. Für die bleibt A übrig — und unter BP-10 B hätte A
keinen Weg mehr. Deshalb sind B **und** die erklärte `generated(...)`-Form aus BP-10 zusammen die
Antwort; einzeln ist jede von beiden eine Lücke.

Der Preis von B ist ein ehrlicher Satz in der Doku: ein `comptime`-Wert ist reproduzierbar *bei
gleichen Build-Optionen*. Das ist auch die Stelle, an der BP-29 einen Haken bekommt — die heute
gemessene Byte-Reproduzierbarkeit gilt dann nur noch bei gleicher Optionsmenge.

**Bruch:** nein (additiv). 4.x kann B liefern und den generierten-Quelltext-Weg im Guide
zurückstufen.
**Hängt ab von:** Gebiet `comptime`/Metaprogrammierung, **BP-10** (die `generated`-Form),
BP-29.

---

### BP-18 — Welche Sprachfassung spricht ein gemeinsamer Compile? *(neu)*

**Die Voraussetzung, die BP-13 kippt.** Sie stand in der ersten Fassung als Zitat im Ist-Stand und
wurde nie als Frage gestellt.

**Heute:** jede Abhängigkeit geht als **Quelltext** in dasselbe Compile (gelesen,
`docs/guide/12-modules.md:74`: „Everything ends up in one `.lyrbc`. There is no separate
compilation step per file and no link step."). Gemessen (`r12b`, `r17`): die Module einer
Abhängigkeit werden vom Parser des Konsumenten gelesen und landen in dessen Modul. Es gibt keine
Übersetzungseinheit unterhalb des Artefakts, also auch keine Grenze, an der zwei Sprachfassungen
aufeinandertreffen könnten, ohne sich zu berühren.

**Daraus folgt dreierlei, und alles drei ist unbequem:**

1. **Eine Toolchain-Auswahl pro Projekt kann nicht leisten, was sie bei Go leistet.** Gos
   `go`-Zeile gilt pro Modul, weil jedes Modul für sich übersetzt wird. Bei Lyric gilt *eine*
   Sprachfassung für den ganzen Abschluss.
2. **Editionen müssten innerhalb eines Moduls gelten**, also pro Datei — und dann treffen sich
   zwei Semantiken an jeder Funktionsgrenze. Rust hat dieses Problem nicht: dort ist eine
   Editionsgrenze immer eine Crate-Grenze, und die gemeinsame Oberfläche wird in beiden Editionen
   identisch typisiert (das darf eine Edition nämlich nicht ändern).
3. **Der Fall „meine Abhängigkeit fordert 4.x, mein Projekt will 5.x" hat heute keine Lösung** und
   kann unter keiner der BP-13-Optionen eine bekommen, solange (1) gilt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: **eine Fassung für den ganzen Abschluss**, ausdrücklich festgehalten | Ganzprogramm-Compiler generell; Zig (Pakete sind Quelle, eine Zig-Version) | ehrlich und einfach. Preis: jede Abhängigkeit muss mit der Sprachfassung der Wurzel übersetzbar sein — also migrieren alle gemeinsam (BP-13 A) |
| B: **Edition pro Datei**, Semantik darf sich unterscheiden | — | zwei Semantiken in einem Modul; ein Wert, der aus einer 4er-Datei in eine 5er-Datei läuft, unterliegt zwei Regeln. Unerklärbar, und die Fehlermeldungen dafür sind nicht schreibbar |
| C: **Edition pro Datei, aber nur Oberfläche** (Syntax, Namensauflösung), nie Semantik | **Rust** (Editionspolitik), Swift `swift-tools-version` (fürs Manifest) | tragbar — nur trägt es Lyrics tatsächliche 5.0-Brüche nicht: die vier laufenden Uhren sind Mutabilitäts-, Bindungs- und `throw`-Regeln (gelesen, `STATUS.md:50-52`) |
| D: **eine echte Übersetzungseinheit unterhalb des Artefakts einführen** (kompilierte Bibliothek, BP-09 B) | Rust `.rlib`, Swift `.swiftmodule`, .NET | erst das schafft die Grenze, an der zwei Fassungen koexistieren könnten. Preis: Format- und ABI-Arbeit, und der generische Teil bleibt trotzdem Quelle (BP-09) |

**Empfehlung: A festhalten und aufschreiben; D als das, was die Frage später öffnet.** Die Regel
lautet: **eine Sprachfassung je Artefakt, gewählt von der Wurzel.** Sie gehört als Satz in das
normative Manifest-Dokument (BP-16 D), weil sie erklärt, warum ein `toolchain`-Pin nur in der
Wurzel wirkt (BP-13) und warum Editionen nicht kommen (BP-13 B). C ist ausdrücklich nicht
abgelehnt, weil es schlecht wäre — es ist abgelehnt, weil es nichts löst, was Lyric zu lösen hat.

**Bruch:** nein (es ist die Beschreibung des Ist-Stands, nicht seine Änderung).
**Hängt ab von:** nichts. **Es hängt ab davon:** BP-04, BP-07, BP-09, **BP-13**, BP-33.

---

### BP-19 — Cross-Compilation: deklariert ein Projekt Ziele? *(neu)*

**Heute: nein, und die Frage ist kleiner als bei Zig — aber nicht null.**

Gelesen: `lyrpack` löst den Stub aus `stubs/<RuntimeInformation.RuntimeIdentifier>/` neben dem
Binary auf (`src/Lyrpack/Program.cs:165-178`), also dem **Wirts**-RID; `--stub <path>` bzw.
`$LYRIC_STUB` sind die einzigen Hebel. Gelesen: weder `lyrbuild --help` noch `lyric --help` kennen
ein `--target` (neu gemessen, beide Hilfen ausgegeben). Ausgeliefert ist hier `stubs/win-x64`,
eines.

**Was die Frage entschärft:** das `.lyrbc` selbst ist **plattformunabhängig** — der Kopf trägt
`LYRB` plus die Formatversion und keinen RID (gemessen, `r10`, Hexdump), und ein Modul läuft auf
jeder VM desselben Formats. Cross-Compilation betrifft bei Lyric also **nur das gepackte
Executable**, nicht das Artefakt. Das ist ein echter struktureller Vorteil gegenüber Zig und Go
und gehört ausdrücklich hingeschrieben, statt als „noch nicht da" zu gelten.

**Was offenbleibt:** `out/<profil>/<name>` kennt genau zwei Achsen. Ein zweites Ziel träfe
dieselbe Datei — und weil zwei Artefakte mit demselben Ausgabepfad sich heute still überschreiben
(BP-15), wäre das genau der Fehler aus Befund 2.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts — `packed` ist immer für den Wirt, Cross-Packen ist `--stub` von Hand | heute | ein Release für drei Plattformen ist drei Maschinen oder drei Handgriffe; und `--stub` ist heute nirgends als Cross-Mechanismus dokumentiert |
| B: `packed(app).target = "linux-x64"` als Artefaktfeld; der Ausgabepfad bekommt das Ziel **nur wenn eines genannt ist** (`out/<profil>/<ziel>/<name>`) | **Zig** `-Dtarget`, Go `GOOS`/`GOARCH` | eine dritte Achse im Ausgabepfad, aber eine optionale; und der Stub für ein fremdes Ziel muss dasein — also braucht B entweder ein Stub-Archiv in der Toolchain oder einen Download (BP-05/BP-31) |
| C: `lyric build --target <rid>` als globaler Schalter | Go | ein Bau, ein Ziel; mehrere Ziele sind mehrere Läufe, und `out/` muss sie trotzdem trennen |
| D: Ziele im Manifest (`"targets": ["win-x64", "linux-x64"]`) | Cargo `[target.…]` teilweise | das Projekt behauptet etwas über Plattformen, das niemand prüfen kann, solange kein Stub dafür da ist |

**Empfehlung: B, und A ausdrücklich dokumentieren, bis B da ist.** B ist die kleinste Form, die
die Wahrheit abbildet: das Ziel ist eine Eigenschaft **eines Artefakts** (nämlich des gepackten),
nicht des Baus und nicht des Projekts — genau deshalb gehört es als Feld auf `Artifact` und nicht
als Manifestschlüssel. Die Pfadachse kommt nur dazu, wenn ein Ziel genannt ist, damit der heutige
Pfad unverändert bleibt. Und der erste Satz der Doku muss lauten: **ein `.lyrbc` ist bereits
portabel; zu packen ist die Plattformsache.**

**Bruch:** nein (additiv).
**Hängt ab von:** BP-15 (Pfadkollision), BP-05/BP-31 (woher ein fremder Stub kommt), BP-26
(native Hälften sind pro Plattform).

---

### BP-20 — Werden Diagnosen aus fremdem Quelltext gedämpft? *(neu)*

**Heute: nein, gar nicht.** Gemessen (`r6`): eine `@Deprecated`-Warnung, die **innerhalb** einer
Abhängigkeit deklariert ist, erscheint im Bau des Konsumenten mit dem Pfad der Abhängigkeit —
`warning[LYR-SEM0076]: 'stale' is deprecated: use fresh()` plus `note: declared deprecated here —
…\dep\src\dep.lyr:3:1`. Und gemessen (`r6`, `denyWarnings = true` im Skript des Konsumenten):
genau daran fällt der Bau um — `error[LYR-CLI0016]: app: 1 warning denied — the artifact asks for
none`. Ein CI, das keine Warnungen duldet, ist damit von den Deprecations **fremder** Bibliotheken
abhängig.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — alle Warnungen zählen gleich | heute | eine Bibliothek, die intern veraltet, bricht die Builds ihrer Konsumenten. Ein Konsument kann nichts tun außer die Bibliothek zu ändern |
| B: **Warnungen aus fremdem Quelltext werden gedämpft** (nicht gezeigt oder als Notiz), Grenze = Abhängigkeitswurzel | **Cargo** `--cap-lints allow` für alles, was nicht das eigene Paket ist | eine echte Warnung in einer Pfadabhängigkeit im eigenen Baum wird dann auch still — und im Monorepo ist das der Normalfall |
| C: B, aber die Grenze ist „nicht mein `sourceRoot`", mit `--warn-deps` zum Aufmachen | — | zwei Regeln statt einer, aber die Grenze ist die, die der Benutzer meint |
| D: nur `denyWarnings` zählt fremde Warnungen nicht mit; gezeigt werden sie weiter | — | die kleinste Änderung, und sie trifft genau den gemessenen Schaden aus `r6` |

**Empfehlung: D für 5.0, C danach.** D behebt den Schaden (ein fremdes `@Deprecated` darf meinen
Bau nicht umbringen), ohne Information zu verstecken — und Information zu verstecken ist bei einem
Ganzprogramm-Compile riskanter als bei Cargo, weil der fremde Quelltext **wirklich mitkompiliert
wird**: eine Warnung über ihn ist eine Warnung über mein Artefakt. C ist die vollständige Antwort,
braucht aber die Unterscheidung „Abhängigkeit" vs. „mein Baum", die im Monorepo strittig ist —
also erst, wenn BP-05 externe Quellen eingeführt hat und die Grenze eine natürliche ist.

**Ausdrücklich nicht gedämpft** werden Diagnosen über das **Manifest** einer Abhängigkeit (BP-21):
ein Tippfehler dort ist kein fremder Quelltext, sondern eine Konfiguration, die meinen Abschluss
verändert.

**Bruch:** D ist ein *minor*-Verhaltenswechsel (ein Bau, der heute rot ist, wird grün) — das ist
die richtige Richtung und braucht keine Uhr, aber einen Changelog-Satz.
**Hängt ab von:** BP-05 (Grenze), BP-21 (Gegenstück), BP-24 (wer `denyWarnings` setzt).

---

### BP-21 — Gehört die Warnliste des ganzen Abschlusses ins Projekt? *(neu)*

**Heute: nein — nur die der Wurzel.** Gelesen: `Resolve` gibt `Warnings = root.Warnings` zurück
(`ProjectFile.cs:194`) und verwirft die Warnlisten aller anderen Manifeste des Abschlusses.
Gemessen (`r6a`): `"verison"` statt `"version"` in `../dep/lyric.json` erzeugt in **keinem**
Werkzeug eine Zeile. **Kontrolle** (`r6b`): derselbe Tippfehler im Manifest der Wurzel warnt
sofort. Der Kommentar am Feld sagt selbst, warum das falsch ist: „ignoring them silently is how a
typo becomes an afternoon" (`ProjectFile.cs:77`).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | ein Tippfehler in einem Bibliotheksmanifest ist unsichtbar, bis er als „cannot find module" auftaucht — genau der Fall, den `ProjectFile.Resolve` an anderer Stelle vermeidet (gemessen, `p7`) |
| B: alle Warnungen des Abschlusses, mit Zuordnung („in `../dep/lyric.json`, erreicht über `dep`") | Cargo (Manifest-Warnungen aller Pfadpakete), Go (`go mod` meldet jedes Modul) | die Liste kann bei vielen Abhängigkeiten lang werden; und sie wiederholt sich bei jedem Bau |
| C: B, aber nur für Pfadabhängigkeiten; geholte Pakete schweigen | — | passt zu BP-20s Logik (fremder Code wird gedämpft), aber ein Manifest ist kein Code |
| D: B, und ein unbekannter Schlüssel in einer Abhängigkeit ist ein **Fehler**, kein Warnhinweis | — | zu hart: der Toleranzgrund („a file written for a later version has to stay readable", `ProjectFile.cs:306-308`) gilt für fremde Manifeste erst recht |

**Empfehlung: B, mit Zuordnung und Entdopplung.** Ein Manifest ist Konfiguration, die **meinen**
Abschluss bestimmt — anders als Quelltext einer Abhängigkeit (BP-20). Die Zuordnung ist der
eigentliche Inhalt der Änderung: „unknown key 'verison' in `…/dep/lyric.json`, reached as `dep`
from `…/app/lyric.json`". Technisch ist es ein Feld mehr im `Manifest`-Record und ein Anhängen
statt eines Verwerfens an `ProjectFile.cs:194`.

**Bruch:** nein (neue Warnungen ändern kein Programm; ein Bau mit `--deny-warnings` könnte
umfallen — siehe BP-20 D, das genau diese Klasse aus der Zählung nimmt bzw. nicht).
**Hängt ab von:** BP-20 (Abgrenzung), BP-05 (Zuordnung bei geholten Paketen).

---

### BP-22 — Gilt „die Wurzel entscheidet" für alle Segmentquellen? *(neu)*

**Heute: nein, nur für `dependencies`.** Gemessen (`r12`): Wurzel mit
`nativeRoots { "nat": "../sdkA" }` plus Abhängigkeit mit `nativeRoots { "nat": "../sdkB" }` →
harter `error[LYR-CLI0010]` („a segment belongs to one root"), **ohne** den Rat, den die
`dependencies`-Kollision gibt, und ohne Auflösungsmöglichkeit. **Kontrolle** (`r12b`): dieselbe
Form mit `dependencies` → baut, die Wurzel gewinnt, keine Warnung, das Programm liefert den Wert
der Wurzelwahl. Gelesen: `ProjectFile.cs:173-177` wirft; `:150-160` lässt die Wurzel gewinnen.

Das ist eine Asymmetrie ohne erklärten Grund. Sie ist außerdem praktisch die falsche Richtung:
**wer weiß, wo das SDK auf dieser Maschine liegt, ist die Wurzel**, nicht eine Bibliothek, die
einen Pfad aus ihrem eigenen Checkout mitbringt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt asymmetrisch | heute | ein SDK, das zwei Bibliotheken verschieden verorten, ist unlösbar; die Meldung nennt nicht einmal einen Ausweg |
| B: **Symmetrie — die Wurzel gewinnt auch bei `nativeRoots`** | Go `replace` (gilt im Hauptmodul für alles), Cargo `[patch]` | ein stiller Sieg mehr; ein Fehler wird zu einem Verhalten, das man bemerken können muss → Hinweiszeile |
| C: Symmetrie in die andere Richtung — auch `dependencies` kollidieren hart, und der Pin muss ausdrücklich sein (`"pin": {...}`) | Nix (alles exakt) | konsequent und sehr laut; bricht jeden heutigen Pin, der implizit funktioniert (gemessen, `r12b`) |
| D: A behalten, aber die Meldung bekommt denselben Rat wie die `dependencies`-Kollision | — | die 20-Minuten-Version: der Fehler bleibt, der Benutzer weiß wenigstens, dass ein Eintrag in der Wurzel ihn löst — **was er heute nicht tut** |

**Empfehlung: B, und D sofort.** B ist die Regel, die der Kommentar an `ProjectFile.cs:109-123`
ohnehin schon beschreibt („THIS project's own entry overrides whatever a dependency says, which is
how a root pins a version everybody under it then shares") — sie steht dort für `dependencies` und
gilt für `natives` nicht, ohne dass irgendwo stünde, warum. D ist die Sofortmaßnahme, weil eine
Fehlermeldung ohne Ausweg heute schon falsch ist: sie sagt „a segment belongs to one root" und
verschweigt, dass die Wurzel ihn benennen darf.

Zwei Dinge gehören dazu: (1) der Sieg der Wurzel bekommt eine **Hinweiszeile** (gilt dann auch für
`dependencies`, wo er heute vollständig still ist — gemessen, `r12b`); (2) Befund 1 wird
mitgelöst, denn ein weggepinntes Projekt darf seine `nativeRoots` gar nicht erst beitragen.

**Bruch:** *minor* (ein Bau, der heute mit `LYR-CLI0010` scheitert, gelingt danach). Die neue
Hinweiszeile ist additiv.
**Hängt ab von:** Befund 1 (dieselbe Codestelle), BP-16 (die Regel gehört normativ),
BP-26 (was ein natives Paket überhaupt mitbringen darf).

---

### BP-23 — Wo endet ein Projekt, und wem gehört `out/`? *(neu)*

**Heute: es endet nirgends, und `out/` gehört dem Arbeitsverzeichnis.** Gemessen (`r13`): in
`outer/vendor/geo/` (Verzeichnis voller `.lyr`-Dateien, kein eigenes Manifest) ausgeführtes
`lyric build` kompiliert **`outer`s** `src/main.lyr` — das Programm druckt `OUTER` — und schreibt
nach `outer/vendor/geo/out/debug/outer.lyrbc`. **Kontrolle** (`r13b`): mit eigenem `lyric.json`
baut `vendor/geo` sich selbst und druckt `VENDOR`. Gelesen: `Discover`
(`ProjectFile.cs:86-98`) läuft ohne Abbruchbedingung bis zur Dateisystemwurzel; der Einstieg kommt
aus `project.SourceRoot` (`Program.cs:415`), der Ausgabepfad aus dem cwd (`Program.cs:425`).

Zwei getrennte Fehler in einem Befund: **(a)** die Suche nach oben hat keine Grenze, **(b)** der
Ausgabeort ist eine Eigenschaft des Arbeitsverzeichnisses statt des Projekts.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | ein `lyric build` im falschen Verzeichnis baut schweigend etwas anderes, als man meint, und legt `out/` dorthin. Ohne Antwort ist BP-14 (Workspaces) nicht entscheidbar |
| B: **`out/` liegt beim Projekt**, die Suche bleibt unbegrenzt | Cargo (`target/` an der Workspace-Wurzel) | behebt (b); (a) bleibt: ein Bau vier Ebenen tiefer baut weiter das Projekt oben — aber wenigstens landet das Artefakt dort, wo es hingehört |
| C: B, **plus eine Grenze für die Suche**: sie endet an einem `.git`-Verzeichnis oder der Dateisystemwurzel | **Go** (das nächste `go.mod` IST die Grenze), Cargo (Workspace-Wurzel) | `.git` als Marker ist eine Werkzeug-Annahme in einer Sprachfrage; dafür ist es die Grenze, die Benutzer meinen |
| D: C, und der Bau **nennt das gefundene Projekt**, wenn cwd ≠ Projektverzeichnis | npm (`npm run` nennt das Paket), Cargo | eine Zeile mehr Ausgabe, und `r13` wäre nie unbemerkt geblieben |

**Empfehlung: D (also B + C + die Zeile).** (b) ist der klare Fehler und billig zu beheben: `out/`
gehört neben `lyric.json`, weil `out/` zum Projekt gehört — alles andere macht `lyric clean`
(BP-15) und jeden Cache (BP-08) unentscheidbar. Bei (a) ist Gos Antwort die richtige: **das
nächste gefundene Manifest ist die Grenze** — das ist bereits so, weil `Discover` beim ersten
Treffer aufhört; was fehlt, ist die *untere* Grenze für den Fall, dass gar keines dazwischenliegt.
`.git` ist dafür der pragmatische Marker. Und die Zeile aus D ist die eigentliche Absicherung: ein
Bau, der sagt, welches Projekt er gerade baut, macht `r13` zu einem Zweizeiler statt zu einem
Nachmittag.

**Bruch:** *minor* für (b) — ein Skript, das aus einem Unterverzeichnis baut und `./out/` erwartet,
findet es woanders. 4.7 warnt („das Artefakt landet ab 5.0 unter `<projekt>/out/`"), 5.0 zieht um.
**Hängt ab von:** nichts. **Es hängt ab davon:** **BP-14**, BP-15, BP-08, BP-30.

---

### BP-24 — Ersetzen benannte Profile die Feldschalter, oder ergänzen sie sie? *(neu)*

**Heute: es gibt im Build gar keine Feldschalter.** Gemessen (`r14`):
`lyric build --deny-warnings` → `error[LYR-CLI0003]: unknown argument: --deny-warnings`, Exit 2.
**Kontrolle** (`r14b`): `lyrc check --deny-warnings` auf derselben Quelle →
`warning[LYR-CLI0017]` und die Warnung wird gezählt. Gelesen: `lyrc` hat `--deny-warnings`
(`src/Lyrc/Program.cs:138-139`) und laut Hilfe auch `--optimize`/`--source-map`/`--debug-info`;
`lyrbuild --help` listet **keinen einzigen** davon — nur `--profile`, `--debug`, `--release`, `-D`,
`--only`, `--print-path`, `--stdlib`. Dabei behauptet der Kommentar, der die Profile definiert,
das Gegenteil: „Every field stays individually overridable on a command line, so a profile is a
starting point and not a wall" (gelesen, `Profile.cs:10-12`).

Das ist eine Zusage, die genau dem Werkzeug fehlt, das Artefakte baut. Ein CI-Bau, der „release,
aber keine Warnungen" will, hat heute **keinen** Weg, der ohne `build.lyr` auskommt — und mit
`build.lyr` ist es ein Feld im Skript (gemessen, `r6`: `app.denyWarnings = true` greift), also
eine Änderung am Repository für eine Eigenschaft eines einzelnen Laufs.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — wer Felder will, schreibt ein `build.lyr` | heute | eine Eigenschaft eines *Laufs* wird ins Repository geschrieben; und `Profile.cs:10-12` bleibt eine falsche Zusage |
| B: `lyrbuild` bekommt dieselben Feldschalter wie `lyrc` | Cargo (`--release` + `RUSTFLAGS`), Go (`-gcflags`) | sechs Schalter mehr in einem zweiten Parser; die Vorrangregel muss hingeschrieben werden |
| C: nur benannte Profile (BP-12 B), keine Feldschalter | Bazel (`--config=…`) | jede Variante braucht einen Namen im Manifest — auch die einmalige; und ein CI-Sonderfall wird zu einem Commit |
| D: **B und C**, mit erklärter Vorrangregel | Cargo (Profil im Manifest, Flags auf der Zeile) | zwei Wege zum selben Feld — aber *ein* Mechanismus mit einer Reihenfolge, nicht zwei Mechanismen |

**Empfehlung: D, mit dieser Reihenfolge, von schwach nach stark:**

1. das **Profil** (eingebaut oder benannt, BP-12 B) setzt alle Felder,
2. das **Skript** darf sie pro Artefakt überschreiben (`artifact.denyWarnings = true` — das ist
   heute schon so, gemessen `r6`),
3. der **Kommandozeilenschalter** gewinnt über beides, und der Bau **nennt**, welches Feld er
   überschrieben hat.

Damit hält `Profile.cs:10-12` wieder Wort, und der CI-Fall ist `lyric build --release
--deny-warnings` ohne eine Zeile im Repository. Dass die Zeile gewinnt, ist die Regel, die zum
strengen Optionsparser passt („eine Option, die akzeptiert wird und nichts tut, ist die, die einen
Nachmittag kostet" — `Program.cs:101-102`).

**Bruch:** nein (additiv). Der Sonderfall ist ein Skript, das sich heute auf sein eigenes
`denyWarnings` verlässt — es verliert gegen den Schalter, und die genannte Überschreibung macht
das sichtbar.
**Hängt ab von:** **BP-12** (zwei Hälften einer Frage), BP-20 (was `denyWarnings` zählt).

---

### BP-25 — Darf eine Testwurzel eigene Abhängigkeiten haben? *(neu)*

**Heute: nein — es ist dieselbe flache Tabelle.** Gelesen: `testRoot` ist einer der sechs Schlüssel
(`ProjectFile.cs:298-300`), und `Resolve` baut genau *eine* Segmenttabelle
(`ProjectFile.cs:124-196`); nichts unterscheidet Test- von Programmabhängigkeiten. Gemessen
(`r8`): eine Datei unter `tests/` importiert `m1` aus dem `sourceRoot` und wird mit dem ganzen
Baum kompiliert — der Testlauf kostet dieselbe volle Kompilation wie der Bau (1,0–1,2 s gegen
0,8–1,2 s). Ein Test-Hilfspaket landet damit im Segmentraum des Programms.

**Was den Schaden heute begrenzt:** eine Abhängigkeit ist ein Pfad im eigenen Baum, und ein
Modul, das niemand importiert, wird auch nicht kompiliert. Der Preis ist also ein belegter
Segmentname und ein Eintrag im Manifest — nicht Code im Artefakt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt eine Tabelle | heute | ein Test-Hilfspaket belegt ein Segment im Programm; sein Name kann mit einer echten Abhängigkeit kollidieren (BP-04), obwohl das Programm es nie sieht |
| B: `testDependencies`, nur von `lyric test` zum Abschluss hinzugefügt | **Cargo** `[dev-dependencies]`, npm `devDependencies` | ein zweiter Abschluss, der gerechnet werden muss; ein Segment, das in beiden vorkommt und verschieden zeigt, muss ein Fehler sein |
| C: B **und** `buildDependencies` (BP-11 C) als dieselbe Form: drei benannte Tabellen | Cargo (drei Tabellen) | drei Abschlüsse; dafür ist „wer sieht was" eine erklärte Sache statt eines Zufalls |
| D: die Testwurzel ist ein eigenes Projekt mit eigenem `lyric.json` | Go (`_test`-Pakete sind eigene Pakete) | kein neues Feld, aber ein zweites Manifest je Projekt und eine Zirkularität (die Tests hängen vom Projekt ab, das sie enthält) |

**Empfehlung: A jetzt, B mit BP-05, und dann in der Form von C.** Solange jede Abhängigkeit ein
Pfad im eigenen Baum ist, kostet A einen Segmentnamen — das ist zu wenig für ein neues Feld.
Sobald BP-05 externe Quellen bringt, kippt es: dann zieht ein Test-Hilfspaket eine Fremdquelle in
den Abschluss des *Programms*, samt Hash, samt Toolchain-Anforderung (Befund 1 zeigt, dass eine
unbenutzte Abhängigkeit den Bau brechen kann) — und das ist genau der Grund, aus dem Cargo
`[dev-dependencies]` hat. B und BP-11s `buildDependencies` sollten dann **dieselbe Form**
bekommen, damit es eine Regel ist und nicht zwei.

**Bruch:** nein (additiv).
**Hängt ab von:** **BP-05** (der Auslöser), BP-11 (gleiche Form), BP-04 (Segmentkollision),
BP-08 D (der doppelte Compile von `build` und `test`).

---

### BP-26 — Was ist ein natives Paket, und wie wird es ausgeliefert? *(neu)*

**Heute: `nativeRoots` reist mit, und niemand fragt, woher die Bibliothek kommt.** Gelesen:
`ProjectFile.cs:120-122` — „A dependency's own `nativeRoots` join the table too: an SDK that ships
as a project brings its native segments along." Gemessen (`r12`): sie reisen tatsächlich mit, und
eine Kollision mit der Wurzel ist ein harter Fehler (BP-22). Gemessen (`p2b`): sie reisen sogar
aus einem **weggepinnten** Projekt mit (Befund 1). Was **nirgends** steht: wie die native Hälfte
auf die Maschine kommt, wer sie baut, für welche Plattform, und was beim Laden passiert.

Ein Sechstel des Manifests beschreibt damit eine Sache, deren Verteilung nicht entschieden ist —
und BP-05 (Tarball + Hash) würde sie mitziehen, ohne dass geklärt wäre, was im Tarball liegt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: **eine geholte Abhängigkeit darf keine `nativeRoots` deklarieren** — Fehler beim Lesen | — | das Manifest sagt die Wahrheit: natives ist lokal. Preis: ein SDK kann kein Paket sein, sondern bleibt ein Pfad / Submodul |
| B: der Tarball trägt vorgebaute Bibliotheken pro Plattform (`native/<rid>/`) | npm (prebuilds), Python-Wheels | Binärartefakte in der Auslieferung: Hash deckt sie, aber niemand kann sie prüfen; und für jedes Ziel eine (BP-19) |
| C: das Paket trägt Bauanweisungen, der Konsument baut die native Hälfte | npm `node-gyp`/`postinstall`, Cargo `build.rs` + `cc` | genau das Lieferkettenloch, gegen das dieses Dossier bei npm argumentiert — Code-Ausführung beim Installieren |
| D: ein natives Paket ist ein **eigener Begriff** mit eigener Auflösung (Systempaket, `pkg-config`-artig) | Zig (`linkSystemLibrary`), Cargo `links` + `*-sys`-Konvention | ein zweiter Paketbegriff; dafür wird die Plattformfrage dort beantwortet, wo sie hingehört |

**Empfehlung: A für 5.0, ausdrücklich als Regel — B danach, C nie.** A ist keine Einschränkung,
sondern das Aufschreiben dessen, was heute allein funktioniert: `nativeRoots` zeigt auf ein
Verzeichnis dieser Maschine, und ein geholtes Paket weiß nichts über diese Maschine. Heute ist das
**still** — ein geholtes Paket würde seine `nativeRoots` einfach in die Tabelle legen, und der
Fehler käme später und woanders. Als Regel ist es ein Satz im Manifest-Dokument (BP-16 D) und eine
Prüfung an der Stelle, an der BP-05 die Quelle kennt.

C ist ausgeschlossen aus demselben Grund, aus dem BP-10 dem Skript die Rechte nimmt: Code-Ausführung
beim Beschaffen ist die Klasse, die npm ein `--ignore-scripts` gekostet hat.

**Bruch:** nein — die verbotene Form existiert heute nicht, weil es keine geholten Pakete gibt.
**Hängt ab von:** **BP-05**, BP-19 (pro Plattform), BP-22 (wem das Segment gehört), BP-28 (was im
Tarball liegt), Gebiet FFI.

---

### BP-27 — Braucht v5 eine maschinenlesbare Projektauskunft? *(neu)*

**Heute: nein.** Gelesen: der Treiber kennt neun Verben, keines heißt `metadata` oder `list`
(`src/Lyric.Cli/Program.cs:29-44`; neu bestätigt über `lyric --help`). `lyrbuild` hat **kein**
`--json` (gemessen, `grep '"--json"' src/Lyrbuild/Program.cs` liefert nichts; `lyrpack` hat eines).
Die einzige maschinenlesbare Ausgabe des Builds ist `--print-path`, eine Zeile (gelesen,
`src/Lyrbuild/Program.cs:255-270`).

BP-10 begründet den Sandkasten damit, dass „ein Editor den Build lesen können soll" — und stellt
die Frage nach der Schnittstelle nicht. Ohne sie muss jeder Client — auch Lyricpp/Erato 2 —
`lyric.json` samt Kommentaren und Komma-am-Ende selbst parsen **und die Abschlussregel nachbauen**
(`ProjectFile.Resolve`, 70 Zeilen mit drei Kollisionsfällen).

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — jeder Client parst selbst | heute | die Abschlussregel existiert dann in zwei Implementierungen und driftet; genau die Klasse, die `Profile.cs:12-15` für Profile ausdrücklich verhindern will („A second copy would drift") |
| B: `lyric metadata --json` — Wurzeln, aufgelöster Abschluss, Artefakte, Profile, Optionen | **`cargo metadata`**, **`go list -json`**, `swift package dump-package` | ein stabiles JSON-Schema ist eine Kompatibilitätsoberfläche wie jede andere; es muss versioniert werden |
| C: B, aber **ohne** das Skript auszuführen — nur was `lyric.json` hergibt | Go (`go list` führt nichts aus) | Artefakte und Optionen fehlen, weil sie aus `build.lyr` kommen; für einen Editor ist das die Hälfte |
| D: B, und das Skript läuft **gesandboxt** (BP-10 B), damit auch Artefakte und Optionen drinstehen | Swift SPM (`dump-package` wertet das Manifest aus), Bazel (`query`) | hängt an BP-10 |

**Empfehlung: C sofort, D als Zielbild.** C ist billig und heute schon vollständig implementierbar
— es ist genau das, was `ProjectFile.Resolve` bereits rechnet, in JSON geschrieben —, und es löst
das dringendste Problem: **ein zweites Runtime muss die Abschlussregel nicht nachbauen, sondern
kann sie fragen.** D kommt mit BP-10 B dazu und macht die Auskunft vollständig. Das Schema gehört
in dasselbe normative Dokument wie das Manifest (BP-16 D), weil es dieselbe Sache von der anderen
Seite beschreibt.

Die Auskunft sollte außerdem die Warnungen des ganzen Abschlusses tragen (BP-21) und sagen, welche
Felder ein Schalter überschrieben hat (BP-24).

**Bruch:** nein (additiv).
**Hängt ab von:** BP-10 (für D), BP-16 (wo das Schema steht), BP-21, BP-24; Gebiet
Editor-Werkzeuge.

---

### BP-28 — Was trägt ein Paket außer `name` und `version`? *(neu)*

**Heute: nichts.** Gelesen: sechs Schlüssel (`ProjectFile.cs:276-311`) — `name`, `sourceRoot`,
`testRoot`, `nativeRoots`, `dependencies`, `toolchain`. Kein `license`, kein `description`, kein
`repository`, und vor allem **keine Einschluss-/Ausschlussliste**. Gemessen (`r6b`): jeder weitere
Schlüssel ist eine Warnung.

**Warum das eine Blockade ist und nicht Kosmetik:** BP-05 verlangt ein `lyric package`, und BP-06
pinnt dessen Ergebnis per Hash. Beides ist **nicht definierbar**, solange nicht feststeht, welche
Dateien in den Tarball kommen. Ein Hash über „das Verzeichnis" hängt an `out/`, an `.git/`, an
Editor-Dateien und an der Testwurzel — und ändert sich, ohne dass sich das Paket ändert.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: „alles außer `out/` und `.git/`" als feste Regel, kein Feld | Go (das Modul-Zip nimmt alles, was nicht ignoriert ist) | einfach und ohne Feld; nimmt aber Testdaten, Benchmarks und Zufallsdateien mit — und der Hash reagiert auf jede davon |
| B: `include`/`exclude` im Manifest, Vorgabe = `sourceRoot` + Manifest + Lizenzdatei | **Cargo** `include`/`exclude`, npm `files` | zwei Felder mehr; dafür ist der Inhalt des Pakets eine erklärte Sache, und der Hash ist definiert |
| C: eine eigene Datei (`.lyricignore`) | npm `.npmignore` | eine Datei mehr, und die ewige Frage, wie sie sich zu `.gitignore` verhält |
| D: B plus die informativen Felder `license`, `description`, `repository`, `authors` | Cargo `[package]`, npm | vier Felder, die nichts erzwingen — aber ohne `license` ist ein Paket nicht weitergebbar |

**Empfehlung: D, mit einer *ausdrücklichen* Vorgabe statt „alles".** Die Vorgabe lautet:
`lyric.json`, der `sourceRoot`-Baum, `build.lyr` wenn vorhanden, `README*` und `LICENSE*` — und
**nicht** `testRoot`, **nicht** `out/`. Das ist die Menge, die ein Konsument braucht (er
kompiliert die Quelle, er führt die Tests der Bibliothek nicht aus), und sie ist klein genug, dass
ein Hash darüber etwas bedeutet. `include`/`exclude` sind die Abweichung davon, nicht die Regel.

Der Unterschied zu Go ist bewusst: Gos Modul-Zip nimmt alles, weil `go.sum` ohnehin über das Zip
geht und das Zip nie von Hand erzeugt wird. Bei Zigs Modell (BP-05 C, BP-06 B) steht der Hash im
Manifest des Konsumenten, also muss er stabil gegen Dinge sein, die den Konsumenten nichts angehen.

**Bruch:** nein (additiv; jedes Feld ist heute eine Warnung, die dann verschwindet).
**Hängt ab von:** **BP-05** (wofür), **BP-06** (der Hash), BP-02 (`version`), BP-25 (`testRoot`
fliegt raus), BP-26 (was mit der nativen Hälfte).

---

### BP-29 — Ist ein Artefakt byteweise reproduzierbar — und soll das eine Zusage sein? *(neu)*

**Heute: ja, gemessen — aber nirgends zugesagt.** Das ist der einzige neue Befund dieser Runde,
der ein Gewinn ist.

Gemessen (`r10`): dasselbe Projekt zweimal gebaut (mit einer Sekunde Abstand, `out/` dazwischen
gelöscht) → identischer SHA-256. Dasselbe Projekt **aus einem anderen Verzeichnis** gebaut →
**derselbe** SHA-256, in `debug` wie in `release`. Gemessen (`r5`): auch das **gepackte
Executable** reproduziert (601 373 Bytes, identischer SHA-256). Gemessen (Hexdump): der Kopf ist
`LYRB` + Formatversion `4`, kein Zeitstempel; ein absoluter Pfad kommt in den Bytes nicht vor.

**Was damit NICHT gemessen ist:** ob zwei verschiedene Toolchain-Fassungen dasselbe Modul erzeugen
— und das sollen sie auch nicht, weil ein Compiler-Fix die Bytes ändern darf.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt eine Tatsache, keine Zusage | heute | die Eigenschaft kann jederzeit unbemerkt verlorengehen — ein Zeitstempel im Kopf, ein absoluter Pfad in der Source-Map, eine Iterationsreihenfolge über eine Hashtabelle, und niemand merkt es |
| B: **Zusage plus Test**: „gleiche Quelle, gleiche Optionen, gleiche Toolchain ⇒ gleiche Bytes", mit einem Konformanzfall, der zweimal aus zwei Verzeichnissen baut und vergleicht | Go (`-trimpath`, reproducible builds), Rust (arbeitet daran), Nix | ein Test mehr, und eine Zusage, die man einhalten muss: jede Quelle von Nichtdeterminismus wird dann zu einem Fehler statt zu einer Kuriosität |
| C: B, plus die Zusage steht in `docs/Bytecode.md` bzw. im Manifest-Dokument | — | Spec-Arbeit; dafür können BP-06, BP-08 und BP-15 sich darauf berufen |

**Empfehlung: C — und zwar noch in 4.x, nicht erst in 5.0.** Die Eigenschaft ist **heute** da und
kostet nichts; was fehlt, ist ein Test, der sie festhält. Ohne ihn ruhen drei Empfehlungen dieses
Gebiets auf einer unbeobachteten Zufälligkeit: der Hash aus BP-06, der Fingerprint aus BP-08 B/C
und die Frage aus BP-15, ob ein Artefakt veraltet ist. Ein Konformanzfall, der zweimal aus
verschiedenen Verzeichnissen baut und die SHA-256 vergleicht, ist eine halbe Stunde Arbeit und
fängt jede künftige Regression.

**Der Haken, der dazugehört:** mit BP-17 B (Build-Optionen als `comptime`-Werte) wird aus „gleiche
Quelle" ein „gleiche Quelle **und** gleiche Optionen". Die Zusage muss von Anfang an so formuliert
sein, sonst muss sie später abgeschwächt werden.

**Bruch:** nein.
**Hängt ab von:** nichts. **Es hängt ab davon:** BP-06, BP-08, BP-15, BP-17.

---

### BP-30 — Gleichzeitigkeit und Abbruch: gibt es eine Sperre auf `out/`? *(neu)*

**Heute: nein, und kein atomarer Schreibvorgang.** Gelesen: `BuildSession.Write`
(`src/Lyrbuild/BuildSession.cs:253-269`) legt das Verzeichnis an und ruft `File.WriteAllBytes` —
kein Temporärfile, kein Rename, keine Sperre; `grep -i lock` über `BuildSession.cs` liefert
nichts. Ein abgebrochener Bau kann also eine **halb geschriebene** `.lyrbc` hinterlassen, und ein
Leser, der die Datei währenddessen öffnet, sieht sie halb.

**Gemessen, und milder als erwartet** (`r16`): vier Durchgänge zu je **acht gleichzeitigen**
`lyrbuild`-Prozessen auf dasselbe `out/` — **kein einziger Fehlschlag**, und das Ergebnis lief
danach korrekt. Auch zwei gleichzeitige Bauten über die 400-Modul-Probe (drei Durchgänge)
gelangen beide. Das Schreibfenster ist wenige Millisekunden lang; die Kollision ist selten, nicht
unmöglich. **Erwartung vor dem Lauf war ein `IOException` („used by another process") in
mindestens einem Fall — sie ist nicht eingetreten, und das gehört hingeschrieben, statt eine
Behauptung zu stützen, die die Messung nicht trägt.**

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt | heute | selten, aber nicht nie; und der abgebrochene Bau bleibt in jedem Fall als halbe Datei liegen, was `lyric run` mit einer Formatfehlermeldung quittiert statt mit „unvollständig" |
| B: **atomar schreiben** — Temporärdatei im selben Verzeichnis, dann `File.Move(…, overwrite: true)` | Go, Cargo, praktisch jeder Build | zehn Zeilen; beseitigt die halbe Datei vollständig und die Kollision fast |
| C: B **plus eine Sperre auf `out/<profil>/`**, auf die ein zweiter Bau **wartet** und dabei sagt, worauf | **Cargo** (Sperre auf `target/`, „Blocking waiting for file lock") | eine Sperrdatei, die bei einem Absturz aufgeräumt werden muss; dafür ist der parallel arbeitende Maintainer im Monorepo kein Sonderfall mehr |
| D: jeder Bau in sein eigenes Verzeichnis (`out/<hash>/`) | Nix, Zig | siehe BP-08 C / BP-15 B — löst es strukturell, kostet den vorhersagbaren Pfad |

**Empfehlung: B sofort, C wenn BP-08 D (geteilter Compile) oder ein Cache kommt.** B ist
unstrittig, billig und beseitigt die einzige Folge, die auch ohne Gleichzeitigkeit eintritt: der
abgebrochene Bau. C wird erst wichtig, wenn ein Lauf mehr als nur die Ausgabedatei anfasst — mit
einem Cache oder einem geteilten Modulgraphen ist ein zweiter gleichzeitiger Lauf dann kein
Glücksspiel mehr, sondern ein Korruptionsrisiko.

**Bruch:** nein.
**Hängt ab von:** BP-08 (ab wann C nötig wird), BP-15/BP-23 (wo `out/` liegt und was darin lebt).

---

### BP-31 — Offline bauen, und was checkt man dafür ein? *(neu)*

**Heute: die Frage stellt sich nicht — es gibt kein Netz.** Gelesen: eine Abhängigkeit ist ein
lokaler Pfad, der beim Lesen existieren muss (`ProjectFile.cs:394-414`); der Treiber kennt kein
`vendor`, kein `update`, kein `--offline`, kein `--locked` (`src/Lyric.Cli/Program.cs:29-44`).
Heute ist **jeder** Bau offline, und das ist ein Vorteil, den BP-05 aufgibt.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: es bleibt bei Pfaden, die Frage entfällt | heute | siehe BP-05 A: kein Ökosystem |
| B: mit BP-05 kommen `--offline` (holt nichts, scheitert wenn etwas fehlt) und `--locked` (scheitert, wenn das Manifest sich ändern müsste) | Cargo `--offline`/`--locked`/`--frozen`, Deno `--frozen` | zwei Schalter und ein Cache-Verzeichnis, das eine Aufräumfrage mitbringt |
| C: B plus `lyric vendor` — die Quellen in den eigenen Baum kopieren und einchecken | `cargo vendor`, `go mod vendor` | ein drittes Layout neben Pfad und Cache; und eingecheckte Fremdquellen sind ein Review-Problem |
| D: gar kein Cache — geholte Quellen landen immer im Projektbaum (`deps/`), eingecheckt oder ignoriert, nach Wahl | Zig (globaler Cache) / npm (`node_modules` im Projekt) | ein Layout weniger zu erklären; dafür wird jedes Projekt groß, und derselbe Tarball liegt zehnmal |

**Empfehlung: B **zusammen mit** BP-05, nicht danach — und C nicht, bis jemand es braucht.**
Der Fehler, den Cargo und npm beide gemacht haben, ist, das Holen zuerst zu bauen und die
Offline-Schalter nachzurüsten; danach gibt es Jahre, in denen ein CI-Lauf heimlich Netz braucht.
Wenn BP-05 kommt, kommen `--offline` und `--locked` in derselben Release.

Zwei Regeln gehören dazu, beide aus Zigs Modell: **ein Hash, der nicht passt, ist ein harter
Fehler** — nie ein Neuholen; und **ein Bau, der etwas holen müsste, sagt es, bevor er es tut**
(die einzige Stelle, an der `lyric build` Netz berührt, muss benennbar sein).

`lyric vendor` (C) ist ausdrücklich zurückgestellt: es ist die Antwort auf „unser CI hat kein
Netz", und die Antwort darauf heißt zuerst `--offline` plus ein Cache, den man mitnehmen kann.

**Bruch:** nein (additiv).
**Hängt ab von:** **BP-05**, BP-06 (der Hash), BP-28 (was geholt wird), Gebiet Capabilities.

---

### BP-32 — Soll es eine Konvention für mehrere Programme geben? *(neu)*

**Heute: nein, genau `main.lyr`.** Gelesen: `BuildByConvention` sucht `Path.Combine(sourceRoot,
"main.lyr")` und sonst nichts; existiert es nicht und liegen `.lyr`-Dateien unter dem Root, ist
das Projekt eine **Bibliothek** (`src/Lyrbuild/Program.cs:421-443`). Ein zweites Programm braucht
also ein `build.lyr` — und dann sind alle Artefakte darin, auch das erste.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: bleibt — das zweite Programm ist der Punkt, an dem ein `build.lyr` nötig wird | Go (`main` je Verzeichnis, aber kein Build-File), heute | eine Konvention, eine Ausnahme; wer ein zweites Werkzeug hinzufügt, schreibt das erste noch einmal hin |
| B: `src/bin/*.lyr` sind je ein Programm, benannt nach der Datei | **Cargo** `src/bin/*.rs` | ein **vierter** Weg zu sagen, was gebaut wird (neben Dateiargument, `main.lyr` und `build.lyr`) — Rule 2 wird schwächer, nicht stärker |
| C: B, aber `main.lyr` entfällt dafür — nur noch `src/bin/` | — | bricht jedes bestehende Projekt für einen ästhetischen Gewinn |
| D: A, und `lyric new` erzeugt für eine App **kein** `build.lyr` mehr; der Guide zeigt am Beispiel „zweites Werkzeug", wann eins dazukommt | — | eine Template-Änderung und ein Guide-Absatz; kein Mechanismus mehr, einer weniger in der Praxis |

**Empfehlung: D.** Damit ist auch die Empfehlung aus §5 entscheidbar, die in der ersten Fassung
frei in der Luft hing: `lyric new` legt heute für eine App **beide** Projektformen gleichzeitig an
— `templates/app/lyric.json` **und** ein `templates/app/build.lyr`, das genau das tut, was die
Konvention ohnehin täte (gelesen, beide Dateien; das `build.lyr` enthält eine Zeile
`executable("__name__", "src/main.lyr")`). Das ist Rule 2 im Template verletzt, und zwar an der
Stelle, an der ein neuer Benutzer zum ersten Mal hinsieht.

B ist verlockend und wird trotzdem abgelehnt: Lyric hat **drei** Wege, zu sagen was gebaut wird;
ein vierter macht das Problem größer. Cargo kann sich `src/bin/` leisten, weil es kein
Build-Skript als dritten Weg hat (`build.rs` deklariert keine Ziele).

**Bruch:** nein — die Template-Änderung betrifft nur neue Projekte.
**Hängt ab von:** nichts. **Löst:** den Rule-2-Konflikt aus §5.

---

### BP-33 — Ist die Standardbibliothek an die Toolchain gebunden? *(neu)*

**Heute: ja, und es ist kein Manifestschlüssel.** Gelesen: `StdlibRoot` kommt aus `--stdlib`, sonst
`$LYRIC_STDLIB`, sonst dem Verzeichnis neben dem Binary
(`src/Lyric.Frontend/Parsing/StdlibLoader.cs:19`, `src/Lyrc/Program.cs:477`,
`src/Lyrbuild/Program.cs:503`). Die sechs Manifestschlüssel enthalten nichts dergleichen
(`ProjectFile.cs:276-311`) — ein Projekt kann also **nicht** sagen, welche `std` es will.

**Warum das zu BP-13 gehört:** eine Toolchain-Auswahl (BP-13 C) ist nur dann eine Antwort, wenn
die alte Toolchain auch ihre alte `std` mitbringt — sonst würde ein 4er-Projekt mit 4er-Sprache
gegen eine 5er-Bibliothek gebaut, und der Bruch käme durch die Hintertür.

**Optionen**

| Option | Vorbild | Preis |
|---|---|---|
| A: `std` gehört zur Toolchain, immer | **Go** (die Standardbibliothek *ist* die Toolchain), Zig | einfach, und es macht BP-13 C tragfähig. Preis: eine neue `std`-Funktion erfordert eine neue Toolchain — es gibt keinen Weg, „nur die Bibliothek" zu aktualisieren |
| B: `std` ist eigenständig versioniert und im Manifest wählbar | Python (stdlib + PyPI-Backports), .NET (`netstandard`) | zwei Versionsachsen; und jede Sprachregel, die eine `std`-Form voraussetzt, muss beide kennen |
| C: A, plus ein `@Since`-artiger Marker in `std`, damit ein Aufruf einer zu neuen Funktion **als solcher** gemeldet wird | Rust (`#[stable(since)]`), Swift `@available` | ein Attribut mehr und eine Pflege-Disziplin; dafür sagt der Fehler „diese Funktion gibt es ab 5.1" statt „unknown identifier" |

**Empfehlung: A, ausdrücklich festgehalten — und C, sobald BP-13 C steht.** A ist der Ist-Stand
und die richtige Wahl: `std` ist bei Lyric source-first und wird mit dem Compiler ausgeliefert;
zwei Achsen wären ein zweiter Mechanismus für dieselbe Frage („welche Sprache spricht dieser
Bau"), also Rule 2. Die Folge gehört aber hingeschrieben: **wer eine alte Toolchain pinnt, pinnt
seine `std` mit** — und eine Abhängigkeit, die eine neuere `std`-Funktion benutzt, scheitert dann.
Ohne C ist diese Meldung ein `LYR-SEM0002` „unknown identifier", also die schlechteste Fassung
einer Nachricht, die eigentlich „upgrade die Toolchain" heißt.

`$LYRIC_STDLIB` bleibt, was es ist: ein Diagnose-Schalter derselben Klasse wie `LYRIC_PROFILE` und
`LYRIC_JIT` — und sollte nach derselben Logik wie dort (BP-12) sagen, wenn er greift.

**Bruch:** nein (A ist der Ist-Stand). C ist additiv.
**Hängt ab von:** **BP-13**, BP-18, Gebiet Standardbibliothek.

---

## 4. Was wir übernehmen sollten

Nach Sprache geordnet, mit der Frage, in die es gehört.

| Von | Was | Frage |
|---|---|---|
| **Zig** | `version` + `minimum_zig_version` im Manifest; **Hash statt Lockfile**; keine Versionsauflösung; `-D`-Optionen als Erstklassiges; **ein Hash, der nicht passt, ist ein Fehler, kein Neuholen**; `-Dtarget` als normaler Fall | BP-02, BP-05, BP-06, BP-07, BP-17, BP-19, BP-31 |
| **Go** | die Standardbibliothek **ist** die Toolchain; `replace` gilt nur im Hauptmodul, und das ersetzte Projekt spricht dann auch nicht mehr mit; `go list -json` als Auskunft; das nächste Manifest ist die Projektgrenze; MVS als *Reserve* | BP-33, Befund 1, BP-22, BP-27, BP-23, BP-07 |
| **Cargo** | benannte Profile mit `inherits`, im Manifest statt im Skript; `[build-dependencies]` und `[dev-dependencies]` getrennt; `cargo clean`; Umbenennen einer Abhängigkeit (`package =`); **`--cap-lints` für fremden Quelltext**; **eine Sperre auf `target/`**; `include`/`exclude`; `cargo fix --edition` als Vorbild für `lyrfix` | BP-12, BP-11, BP-25, BP-15, BP-03/04, BP-20, BP-30, BP-28, BP-13 |
| **Bazel** | die Ladephase kennt keine E/A **und keine Rekursion** — deshalb ist sie cachebar; alles, was wirkt, ist eine **Aktion mit erklärten Ein- und Ausgaben** | BP-10, BP-08, BP-17 |
| **Swift SPM** | das Manifest **deklariert** und baut nicht; `build tool plugin` als die Form für Codegenerierung mit erklärten Eingaben; `swift-tools-version` als Semantik-Wahl für die Build-Datei selbst (⟲ **nicht** `.swift-version`) | BP-10, BP-17, BP-01 |
| **Nix** | der Ausgabepfad trägt die Eingaben; ein veraltetes Artefakt ist dann strukturell unmöglich | BP-08, BP-15, BP-30 |
| **Deno** | ein Konfigurationsfile als **Daten** plus ein Task-Runner deckt das meiste ab, was Leute vom Skript wollen; `deno compile` = `lyric pack`, gleiche Kosten | BP-10, BP-19 |
| **npm** (als Warnung) | Code-Ausführung an möglichst wenige Kommandos binden — `postinstall` ist der Grund, warum BP-26 C ausscheidet; Duplikate lösen Diamanten und schaffen eine unerklärbare Fehlerklasse | BP-10, BP-26, BP-04 |
| **Rust** (als Warnung *und* als Vorbild) | die Editionspolitik ist **begrenzt** und deshalb tragbar — und genau diese Grenze zeigt, dass sie Lyrics semantische Brüche nicht tragen kann; `#[stable(since)]` als Form für „diese Funktion gibt es ab X" | BP-13, BP-18, BP-33 |

**Die vier, die ich zuerst bauen würde** (gegenüber der ersten Fassung um eine erweitert und
umsortiert):

1. **BP-29 + BP-30 B** — die Reproduzierbarkeit festhalten (Zusage + ein Konformanzfall) und
   atomar schreiben. Zusammen unter einem Tag Arbeit, und alles Spätere (BP-06, BP-08, BP-15)
   ruht darauf. Es ist außerdem das einzige Stück dieses Gebiets, das **heute schon richtig ist**
   und nur unbeobachtet.
2. **Die stillen Fehlbauten** — Befund 1 (weggepinntes Projekt wirkt weiter), Befund 2/BP-15
   (gleicher Ausgabepfad überschreibt still), Befund 14/BP-14 D (fremder Entry bekommt fremde
   Module). Drei Fälle, in denen der Bau Erfolg meldet und etwas anderes liefert, als dasteht.
   Keiner braucht eine Regelentscheidung.
3. **BP-13 + BP-18** — die Sprachfassung des Abschlusses aufschreiben und die `toolchain`-Obergrenze
   liefern. Sie entscheidet, ob 5.0 ein Stichtag oder ein Übergang wird, und ihre 4.x-Uhr läuft
   heute noch nicht. **Und die einzige Uhr des Gebiets, die schon läuft — `addExecutable` bis
   5.0 — braucht einen `lyrfix`, der sie bedienen kann.**
4. **BP-02/03/06/28** (Version, Identität, Hash, Paketinhalt) — vier Manifest-Entscheidungen, die
   zusammen aus einem Verzeichnis ein Paket machen, alle additiv. **BP-03 erst mit BP-04s
   Umbenenner scharf schalten**, sonst liefert 5.0 eine Regel ohne Ausweg.

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 („ein Mechanismus pro Konzept"):**

- **Drei Wege, zu sagen, was gebaut wird** — Dateiargument, Konvention, `build.lyr` —, und
  `lyric new` legt für eine App **beide Projektformen gleichzeitig** an (`templates/app/lyric.json`
  *und* `templates/app/build.lyr`, dessen einzige Zeile `executable("__name__", "src/main.lyr")`
  ist — gelesen). Der Guide begründet die Konvention als Vereinfachung; das Template macht sie
  sofort wieder zunichte. **Entschieden in BP-32 D:** `lyric new` schreibt für eine App kein
  `build.lyr` mehr, der Guide zeigt am zweiten Programm, wann eins dazukommt. Und ein **vierter**
  Weg (`src/bin/*.lyr`) wird ausdrücklich abgelehnt.
- **Zwei Formen von „wo landet das Artefakt"** — die Ableitung in `std.build` (Lyric,
  `stdlib/std/build.lyr:102-107`) und die in `Program.cs:425-426` (C#). Slice 3 hat das Modell
  ausdrücklich nach Lyric geholt; der Konventionspfad ist zurückgeblieben. **Dazu neu:** es gibt
  noch eine dritte Form, nämlich `Artifact.output`, die beide überschreibt und nicht geprüft wird
  (gemessen, `r6`).
- **`lyric.json` wird gelesen, `build.lyr` wird ausgeführt** — das ist die Trennung, auf der BP-10,
  BP-12 und BP-27 aufbauen. Sie ist heute an einer Stelle durchbrochen: `lyrbuild --help` führt
  das Skript aus (gemessen, `r3`).
- **BP-06:** ein separater Lockfile *wäre* ein zweiter Mechanismus fürs Pinnen neben dem Hash im
  Manifest. Deshalb Zigs Form.
- **BP-17 C** (Feature-Flags) wäre ein zweiter Mechanismus neben `comptime` — deshalb abgelehnt.
- **BP-24** sieht aus wie ein zweiter Mechanismus (Profil im Manifest *und* Schalter auf der
  Zeile) und ist keiner, solange die **Vorrangregel** hingeschrieben ist. Ohne sie wäre es einer.
- **BP-33 B** (eigenständig versionierte `std`) wäre eine zweite Achse für „welche Sprache spricht
  dieser Bau" — abgelehnt.

**Ein Widerspruch der ersten Fassung, jetzt aufgelöst:** BP-01 versprach eine normative
Schlüsselliste in der Spezifikation, BP-16 wählte „nur die Auflösung normativ, das Dateiformat
nicht". Beides ging nicht zusammen. **BP-16 D** löst es: die Auflösungsregel in die Grammatik, das
Manifest in ein eigenes normatives Dokument nach dem Muster von `docs/Bytecode.md`.

**Mit „spec-first":** das gesamte Projektmodell ist compiler-only (BP-16). `grep -in` über
`docs/Grammar.md` nach `lyric.json`, `sourceRoot`, `dependenc`, `toolchain` liefert genau einen
Treffer, und der steht in der Mirror-Fußnote an Zeile 3 (gemessen). Das ist kein Detail:
`LYR-RES0006` („loaded as X, declares Y", gemessen `r11`) ist eine Regel über Programme.

**Mit anderen Gebieten:**

| Gebiet | Konflikt |
|---|---|
| **Capabilities/Sandbox** | BP-10 verlangt, dem Build-Skript Rechte zu **entziehen**. Das kollidiert mit `lyrbuild --help`s eigenem Satz („A build script runs with every capability") und mit jedem Skript, das Quelltext generiert — weshalb BP-10 B ohne die `generated(...)`-Form eine Amputation wäre. BP-31 fügt hinzu: der Fetcher braucht Netz, und zwar als einziger Teil des Baus. |
| **Generics/Monomorphisierung** | BP-09: eine kompilierte Bibliothek kann ihre generische Oberfläche nicht vorkompilieren. Entweder sie liefert Quelle/IR mit, oder `pub`-Generics sind in Bibliotheken verboten. Diese Frage gehört *beiden* Gebieten — und sie ist zugleich der Grund, warum BP-18 sich mit BP-09 nur **halb** entspannt. |
| **Standardbibliothek** | BP-05 (URL-Abhängigkeiten) braucht HTTP, Entpacken und Hashing — also die offene Bibliotheks-Umkehr aus `STATUS.md` §Still open. BP-33 bindet `std` an die Toolchain und macht damit jede `std`-Erweiterung zu einer Toolchain-Frage. |
| **Bytecode/Format** | BP-09 braucht die Interface-Sektion (Format 4.1). BP-06 braucht einen festgelegten Hash-Algorithmus. **BP-29 gehört als Zusage in `docs/Bytecode.md`** — die Reproduzierbarkeit ist eine Eigenschaft des Formats und seines Serialisierers. |
| **Module & Sichtbarkeit** | BP-03/04: „Segment = Identität" und ein Umbenenner, der *innerhalb* einer Abhängigkeit wirkt, sind Regeln über Importauflösung. BP-11 und BP-18 kommen dazu: welchen Modulraum hat ein `build.lyr`, und gibt es eine Übersetzungseinheit unter dem Artefakt? |
| **`comptime`/Metaprogrammierung** | BP-17 B erweitert die `comptime`-Quelle um Build-Optionen und schwächt den Reproduzierbarkeitssatz von „dieselbe Quelle" auf „dieselbe Quelle und dieselben Optionen" — was BP-29s Zusage von Anfang an so formuliert haben muss. |
| **Diagnostik** | BP-20 (fremde Warnungen dämpfen), BP-21 (fremde Manifestwarnungen zeigen) und Befund 5 (zwei Warnformen für dieselbe Bedingung) sind drei Fragen desselben Gebiets: **welche Diagnose gehört wem**. |
| **Werkzeuge/CLI** | BP-15 (`lyric clean`, `--only kind:name`), BP-13 (`lyric toolchain`), BP-05 (`lyric package`, `lyric update`), BP-27 (`lyric metadata --json`), BP-31 (`--offline`, `--locked`), BP-24 (die Feldschalter im Build) fügen Verben und Schalter hinzu; das Gebiet CLI entscheidet die Form. |
| **Editor-Werkzeuge** | BP-27 ist die Schnittstelle, die BP-10s Begründung („ein Editor soll den Build lesen können") voraussetzt und die erste Fassung nicht gestellt hat. Ohne sie baut jeder Client `ProjectFile.Resolve` nach. |

**Eine Beobachtung zum Schluss, die keine Frage ist:** von den siebzehn Ist-Stand-Befunden brauchen
**neun** keine Regelentscheidung, sondern nur den Entschluss, die Uhr zu starten —
gleicher Ausgabepfad (2), `--help` führt aus (3), `--only` trifft beide (4), die zwei Warnformen
(5), `LYRIC_PROFILE` schweigt (10), der ungeprüfte abgeleitete Name (11), die doppelte Ableitung
(12), der fremde Entry (14), das stumme Abhängigkeitsmanifest (15). Dazu kommen zwei, die eine
*kleine* Entscheidung brauchen: das weggepinnte Projekt (1) und die `nativeRoots`-Asymmetrie (16).
Das sind Uhren, die heute starten könnten, nach derselben Logik, mit der der Maintainer am
2026-09-24 alle anderen Uhren auf „jetzt" gestellt hat (gelesen, `STATUS.md:40-47`).

---

## 6. Nach der Kritik geändert

**Falsche Aussagen korrigiert (5):**

- **Befund 11 korrigiert.** Nicht die Datei `lyric.json` löst die strenge Modulnamensregel aus,
  sondern der Schlüssel `name` (gemessen `r4`/`r4b`, gelesen `ProjectFile.cs:267` +
  `:278-280` + `:326-340`, bestätigt in `docs/guide/12-modules.md:112`). Der ungeprüfte Fall ist
  damit der häufigere. **BP-15 A (5)** ist entsprechend umformuliert.
- **Befund 13 zurückgezogen.** `packed(app)` gelingt über den ausgelieferten Treiber (gemessen
  `r5`: `app.lyrbc` + `app.exe`); nur der Direktaufruf von `lyrbuild.dll` scheitert (Kontrolle
  `r5b`), und dessen Meldung nennt mit `LYRIC_PACK` selbst den Ausweg. Messartefakt, kein Befund.
- **§1.3 „der Preis ist klein" relativiert.** Die Zahlen sind warme Zahlen; das Minimum aus fünf
  Läufen verwirft genau den Lauf, den ein Cache adressiert. Der kalte Fall ist hier **nicht**
  gemessen und als solcher gekennzeichnet. **BP-08** trägt jetzt eine genannte Auslösebedingung
  statt einer Schlussfolgerung aus einem Messfenster.
- **§1.1 um `addExecutable` ergänzt.** Es trägt `@Deprecated { until = "5.0" }`
  (`stdlib/std/build.lyr:179-186`) und ist der **einzige** `until`-Treffer eines `@Deprecated` im
  gesamten stdlib (gemessen, `grep`). Ein Dossier über den 4→5-Schnitt darf die einzige bereits
  gestellte Uhr seines Gebiets nicht übersehen — **BP-13** nennt sie jetzt samt `lyrfix`-Pflicht.
- **`Artifact.output` überall verrechnet.** Das Feld ist aus dem Skript frei setzbar und wird
  nicht geprüft (gemessen `r6`, `r15`); dasselbe gilt für `denyWarnings` (gemessen `r6`). Befund
  2, Befund 12, **BP-15** und **BP-19** sind darauf umgestellt: **die Kollision ist der
  Ausgabepfad, nicht der Name.**

**Zwei Kritikpunkte, bei denen die erste Fassung hält:**

- **Die Belege aus `STATUS.md` waren nicht nachprüfbar** — die zitierten Zeilen haben sich mit dem
  Branchwechsel verschoben. Die *Aussagen* halten: die v5-Entscheidung steht jetzt an
  `STATUS.md:40-47`, die vier Uhren an `:50-52`, M37 Slice 3 an `:170-180`. Der Kern von **BP-16**
  ist neu gemessen und stimmt: `grep -in` über `docs/Grammar.md` liefert genau einen Treffer, und
  der steht in der Mirror-Fußnote.
- **Befund 14 war „unterverkauft"** — richtig, und er ist geschärft: `r9` zeigt, dass der Nachbar
  fremden Code untergeschoben bekommt (`A's helper` statt `B's helper`, bei identischer
  Artefaktgröße). Er steht jetzt bei den stillen Fehlbauten und hat mit **BP-14 D** eine Regel
  statt einer Benennung.

**Vergleichssprachen korrigiert (8, alle mit ⟲ markiert):** Swift-SPM-Sandkasten ist macOS-only;
Gos `toolchain`-Direktive ist von der `go`-Zeile getrennt und schaltet nur bei
`GOTOOLCHAIN=auto` um; Rusts Editionspolitik ist ausdrücklich **begrenzt** (kein Typsystem, keine
Trait-Auflösung, keine stdlib, ein IR, `cargo fix --edition`); npm hat seit v3 eine flache,
gehobene `node_modules`; `go.sum` ist eine Prüfsummen-DB, der Pin ist der `require`-Graph;
`.swift-version` ist eine Drittwerkzeug-Konvention und kein SwiftPM-Mechanismus; Perl 6 ist das
Beispiel für **keinen** Stichtag, nicht für einen gescheiterten; Starlark **verbietet** Rekursion,
es begrenzt sie nicht.

**Schwache Empfehlungen ersetzt (7):**

- **BP-13** — Empfehlung geändert: **A **und** C zusammen**, B abgelehnt aus einem neuen Grund
  (Lyrics 5.0-Brüche sind semantisch, und die Editionsgrenze läge innerhalb eines Moduls). Die
  Voraussetzung steht jetzt als eigene Frage in **BP-18**.
- **BP-01 vs. BP-16** — der direkte Widerspruch ist aufgelöst: **BP-16 D**, ein eigenes normatives
  Manifest-Dokument nach dem Muster von `docs/Bytecode.md`, plus die Auflösungsregel in der
  Grammatik.
- **BP-08** — neu gemessen (drei Artefakte: 1,7×, nicht 3×; `test` ist eine zweite volle
  Kompilation; CI = 1,9 s) und um Option **D** erweitert (den Compile teilen statt ihn zu
  wiederholen), die vor B steht. Die Entscheidung „A" hat jetzt eine nachprüfbare
  Auslösebedingung.
- **BP-15** — auf den Ausgabepfad umgestellt, und die Namensprüfung auf ihren echten Auslöser.
- **BP-11** — erweitert um die eigentliche Frage: **welchen Modulraum hat ein `build.lyr`?**
  Gemessen (`r11`): weder den eigenen `sourceRoot` noch einen erreichbaren Ersatz.
- **BP-10 + BP-17** — der übersehene Preis ist verrechnet: B deckt Optionen ab, nicht
  Codegenerierung; die fehlende Form (`generated(...)` mit erklärten Ein- und Ausgaben) ist jetzt
  Bestandteil von BP-10 B statt einer Klammer.
- **BP-03/BP-04** — Reihenfolge umgedreht: BP-03 bleibt Warnung, bis BP-04s Umbenenner existiert.
  Eine Regel, die bricht, kommt nicht vor ihrem Ausweg.

**Sechzehn fehlende Designfragen eingearbeitet:** BP-18 (Sprachfassung eines gemeinsamen
Compiles), BP-19 (Cross-Compilation), BP-20 (Diagnosen aus fremden Quellen), BP-21 (stumme
Manifestwarnungen), BP-22 (`nativeRoots` und die Pin-Regel), BP-23 (Projektgrenze und wem `out/`
gehört), BP-24 (Feldschalter im Build), BP-25 (Test-/Entwicklungsabhängigkeiten), BP-26 (was ein
natives Paket ist), BP-27 (`lyric metadata --json`), BP-28 (Paketmetadaten und Dateiliste), BP-29
(Reproduzierbarkeit des Artefakts), BP-30 (Gleichzeitigkeit und Abbruch), BP-31 (Offline und
Vendoring), BP-32 (mehrere Programme nach Konvention), BP-33 (`std` und die Toolchain).

**Drei neue Ist-Stand-Befunde aus dieser Runde:** 15 (das Manifest einer Abhängigkeit warnt nicht),
16 (`nativeRoots` kennt die Pin-Regel nicht), 17 (ein Projekt hat keine Grenze, und `out/` gehört
dem cwd). Dazu ein **positiver** Befund, den die erste Fassung nicht hatte: die Artefakte sind
byteweise reproduzierbar (BP-29) — und genau darauf ruhen BP-06, BP-08 und BP-15.
