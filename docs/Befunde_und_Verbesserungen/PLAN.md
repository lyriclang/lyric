# Der Plan — eine Liste

Zusammenführung von fünf Quellen: dem Bug-Hunt (146 Einträge), dem Usability-Review (117), dem
Evolution-Team (94 plus `deliverables/new-features/roadmap.md`), den Regelfragen, die der Sweep
aufgeworfen hat (`SPEC-RUNDE.md`), und seit dem 2026-09-23 `lyric-v5-features.md`.

**Lesart.** „gemessen" heißt: ein Repro lief gegen den Integrationsstand und hat geantwortet.
„behauptet" heißt: es steht in einem Bericht und ist nicht nachgefahren. Alles ohne Marke ist
Fund, nicht Arbeit. **NEU** heißt: steht in `lyric-v5-features.md` und stand vorher in keiner
Liste hier.

**4.6 ist eine Stabilisierung, kein Feature-Release** (Maintainer, 2026-09-23). Der Tag fällt
erst, wenn die Fehler unten weg sind — B, C, D und E. Features beginnen bei 4.7. Der Baum trägt
die Versionsnummer seit dem 2026-09-23, der Tag ist bewusst noch nicht gesetzt.

**5.0 wird gesammelt, nicht geplant** (Maintainer, 2026-09-22). Der Abschnitt unten ist eine
Ablage mit Uhren, kein Meilenstein.

**Offene Regelfragen werden mit v5 zusammen entschieden; bis dahin entsteht ausschliesslich eine
WARNUNG, die auf den Wechsel hinweist** (Maintainer, 2026-09-24). Das beantwortet jede Frage, die
in dieser Liste „nirgends entschieden" heisst, auf dieselbe Weise: nicht jetzt, und bis dahin
kein stilles Verhalten. Es raeumt damit **E** ab — die zwei Posten dort sind keine Bugfixes mehr,
sondern zwei Uhren — und es setzt alle Uhren der Ablage unten in Gang, statt sie auf 4.7 zu
vertagen. Eine Warnung aendert kein Programm, also vertraegt sie sich mit einer Stabilisierung.

---

## 4.6 — was schon drin ist

> Die Abschnitte hießen bis zum 2026-09-23 „4.5". Der Versionsbump lief mitten durch diese Liste:
> was hier steht, ist der Inhalt der `v4.6.0`-Sektion im `CHANGELOG`, ausgeliefert ist davon
> nichts, weil nicht getaggt ist.

Gemessen gegen `integration/v4.5`, Repro-Korpora unter `scratchpad/repro/`.

**Aus dem Sweep** (auf `main`, PR #161): do-while-Sprungziele in der Handler-Range · Monomorphisierung
ohne Modulpräfix · `if (c) 1 else 2.5` · Struct-Aliasing an drei Bindungspunkten · lvalue-Regel für
`++`/`--`, Lambda-Bodies und Block-Arme · Narrowing nach Zuweisung · Destructuring als Call-Site ·
§5.4 Extension vor Default · werfender `defer` auf dem Durchfall-Pfad. **16 Repros, alle grün.**

**Aus M37** (auf `main`): Profile · `lyric.json` v2 · `std.build` v2 · Projektverben · und drei
CLI-Funde nebenbei: `--grant none` greift, Option vor der Datei, `lyrvm … -- --grant`.

**Aus den vier Branches** (PR #162): Pattern-Compiler mit neun geschlossenen Defekten · f-String
rendert `Display` · `throw` als Ausdruck · Labels · Value-Block · `try` in der Definite Assignment ·
`defer` im if-Körper · `std.result` · Iterator-Terminatoren · Container · `comptime` ·
`extern "dotnet"` · `parseInt`/`powInt` ohne stillen Überlauf.

**Spec-Zwilling**: `lyric-spec#38` retiriert die zwei Pattern-Limits in §7.6. **Muss vor #162 landen**,
sonst kann die Suite den Stand nicht beurteilen.

---

## 4.6 — was noch reingehört, bevor getaggt wird

**Das hier ist die vollständige Bedingung für den Tag.** Jeder Posten von B bis E ist ein Fehler,
kein Feature; ein stabiles 4.6 heißt, dass keiner davon mehr offen ist. Zwei Einträge in **C**
stehen in `lyric-v5-features.md` als P1-*Fundament* (generische Methoden auf generischen Typen,
die restlichen `IR0001`-Grenzen) — sie sind beides, und sie landen hier, nicht in 4.7.

**Stand 2026-09-24**: **B** und **C** sind leer. Offen sind **D** und die Warnstufen, die aus
**E** und der Ablage geworden sind.

### ~~A. Der Verifier läuft an der falschen Stelle (zuerst)~~ — **erledigt, 2026-09-23**

Der Befund war richtig und **größer als beschrieben**. Zwei Hälften, beide gemessen:

*Die Reihenfolge.* Inliner, ScalarReplacement, Devirtualizer und `Reachability.Prune` liefen vor
`IrVerifier.VerifyOrThrow`. Belegt an einem echten Fall (`two_extensions_overload_by_parameters`):
mit abgeschaltetem Optimierer fand die alte Position den Defekt, mit eingeschaltetem **nicht** —
der Inliner spleißte beide Rümpfe in ihren einzigen Aufrufer, das Pruning löschte die Originale,
das Modul verifizierte sauber. Jetzt läuft der Verifier hinter dem Lowering **und** hinter den
Pässen (letzteres nur, wenn wirklich einer lief), und die Meldung nennt den Lauf.

*Der Schalter.* Das eigentliche Loch: **jeder** CI-Job baut `--configuration Release`, und
`VerifiesIr` war `#if DEBUG`. Die CI hat auf keinem Pfad durch `SourceCompiler` verifiziert — nicht
die Tooling-Tests, nicht die Konformanz-Suite, nicht die Beispiele. Gerettet hat das nur, dass
**alle 92** direkten `ModuleLowerer.Lower`-Aufrufe in Tests `verify:` explizit setzen; davon
verifizierten 78 ausschließlich hinter dem Optimierer. `LYRIC_VERIFY_IR` überschreibt jetzt beide
Richtungen, in allen drei Workflows an.

*Korrekturen an der Vorlage oben.* Die Beispiele sind **80** Dateien, nicht 47. Angefasst wurde
**ein** Test, nicht fünf — der, dessen Regel fiel (`Only_a_debug_build_runs_the_verifier`).

**Ertrag: ein echter Defekt.** Zwei `extend`-Überladungen auf einem Typ im selben Modul bekamen
denselben gemangelten Namen (`main.<extend>.Box.tell`) — freie Funktionen und Methoden holen sich
seit 3.0 einen Überladungssuffix, `ExtensionTable` nicht. Auch ohne Verifier sichtbar: zwei
ununterscheidbare `call`s im IR-Dump. Gefixt; die Überladungsmenge spannt laut §4.3a über die
Blöcke, wird also über `ExtensionRegistry.MethodsFor` gerechnet, nicht über den Block-Scope.

Messung im Zielzustand: 5497 Tests grün mit Verifier an *und* aus · 159/159 Konformanz ·
80 Beispiele × 2 Profile ohne Befund.

*Offen geblieben*: der Bytecode-Reader typisieren (Bug-Hunt P1-19) — der Verifier deckt das jetzt
in der CI ab, im ausgelieferten Compiler weiterhin nicht.

### B. Prozessabbrüche — Exit 134/141 statt Diagnose oder Panik

| Fund | Ort |
|---|---|
| Tiefenlimit fehlt im rekursiven Abstieg (Parser, Typ-Parser, `CheckBinary`) — tötet auch `lyrls`/`lyrdbg` | Parser.cs, TypeChecker.cs |
| `lyrc build -o ""` → unbehandelte `ArgumentException` (**gemessen, weiterhin offen**) | Lyrc/Program.cs:99 u. a. |
| `string * n` / `arrrep` mit großem n → `ArgumentOutOfRange` bzw. OOM statt `VM0006` | NativeRegistry.cs:335 |
| Global-Vorwärtsbezug → `InvalidOperationException` in `ldfld`; `SEM0057` nicht transitiv | TypeChecker.cs:1451 |
| Reentranz Skript→Host→Skript umgeht `MaxCallDepth` → CLR-StackOverflow | ScriptInstance.cs:226 |
| Terminal-Injection: ESC/BEL/OSC roh auf stderr | DiagnosticEngine.cs:92 |
| Rest-ICEs aus gültigem Code (`let x = [[]]`, `o += 1` auf narrowed `?T`, `defer { throw }` in `try`) | FunctionLowerer.cs |

`Lyrc/Program.cs:33` fängt `InternalCompilationException` nicht — eine ICE ist heute ein Stacktrace
ohne Code und ohne Position, und im JSON-Modus zerstört sie die Ausgabe.

### C. Sema-Löcher, die die Branches offen gelassen haben

- ~~**Erschöpfung über ein Tupel**~~ — **erledigt** (PR #169). Es gab für Tupel gar keinen Fall in
  der Abdeckungsrechnung. Entschieden wird jetzt, wo die Unterscheidungen in EINER Spalte liegen;
  zwei testende Spalten verlangen weiter ein `_`, weil `(A, true)` neben `(B, false)` jede Spalte
  für sich deckt und `(A, false)` offen lässt — und ein *akzeptiertes* Loch wäre schlimmer als ein
  abgelehnter Match, da das Lowering bei „erschöpfend" den Test des letzten Arms weglässt. Der
  vollständige Fall ist Maranget; halb davon ist schlechter als nichts.
- ~~**Generische Methode auf generischem Typ** (`Result<T,E>.map<U>`)~~ — **erledigt** (PR #170,
  `lyric-spec#42`). Die zweiseitige Substitution existierte, und der Interface-Zwilling
  (`Iterator<int>.map<string>`) benutzte sie seit je; nur der konkrete Pfad hat nie danach
  gefragt. Der zurückgenommene erste Versuch war richtig zurückgenommen: Aufrufstelle, Argumente
  und Rückgabetyp müssen alle drei beide Seiten gebunden haben, zwei davon ergeben ein Modul, das
  übersetzt und falsch rechnet. §8.4 nennt jetzt die Regel samt Kollisionsordnung (die Methode
  gewinnt). Die freien Umwege in der std (`mapList`, `result.map`) bleiben gültig — sie
  abzulösen ist eine API-Änderung und gehört nach 4.7.
- ~~**Lambda kann keine `throws`-Klausel tragen**~~ — **falsch einsortiert, kein Loch.** Die
  Grammatik kennt für Lambdas keine `throws`-Klausel (§2 `Lambda`), also existiert die Form nicht.
  Der Aufruf eines Werfers im Lambda ist `SEM0034`, und `SEM0084` gehört zum Funktions*typ*. Das
  ist **typed throws Stufe 3** und gehört nach 4.7, Position 7.
- **`mut fn` wird auf Klassen nicht erzwungen, auf Structs schon.** **Gemessen, und die Aussage
  stimmt**: eine nicht-`mut`-Methode darf auf einer Klasse `this` schreiben, auf einem Struct ist
  es `SEM0019`. Unangenehm daran ist nicht der Guide, sondern §5: der exakte Signaturvergleich für
  Interface-Konformanz nimmt `mut` auf, also ist es Teil des Vertrags **und bedeutet auf Klassen
  nichts**. Erzwingen lehnt Programme ab, die seit je kompilieren → **Warnstufe in 4.7, Wirkung in
  5.0** (Maintainer, 2026-09-24). Der `let`-Struct-Posten unten hängt nicht daran.
- ~~**Feld und Methode teilen einen Namensraum** (`RES0001`)~~ — **kein Loch, eine fehlende
  Spec-Zeile.** Der Compiler verbietet es bereits, und das bleibt so (Maintainer, 2026-09-24).
  Begründung, gemessen: `c.get` ohne Klammern löst in Lyric **als Feld** auf („'C' has no field
  'get'"). Java und Rust dürfen es erlauben, weil dort eine Methode nicht `obj.method` heißt —
  Java braucht `obj::method`, Rust einen Pfad. Lyric hat Funktionswerte und baut sie in 4.7 aus;
  sobald `c.size` auch die Methode bezeichnen kann, ist es mehrdeutig. Go verbietet es mit
  derselben Begründung. **Offen: die Regel steht in keiner Spec.**
- ~~**`p.field ??= x`**, **`&&=`/`||=`**~~ — **erledigt** (PR #169, `lyric-spec#41`). Alle drei
  stehen in §2, also existieren sie; von neun Kombinationen aus Operator und Ziel funktionierte
  EINE. Der Kurzschluss ist jetzt Spec-Text und gemessen: die rechte Seite läuft genau dann, wenn
  sie muss. §6.5 führte `&&=`/`||=` ausdrücklich als Implementierungsgrenze — die Konformanz-Suite
  hat den Fix abgefangen, bevor er ohne Spec-Zwilling landen konnte.
- ~~**`catch` auf einem Interface**~~ — **erledigt** (PR #169), und **ohne Formatänderung**. Die
  Ablehnung stand auf der Lesart, die Handler-Tabelle könne keinen Konformanztest ausdrücken; sie
  muss es nicht, die Dispatch-Tabelle beantwortet es.
- ~~**Interface-Wert erfüllt seine eigene Constraint nicht**~~ — **erledigt** (PR #169), zwei
  Hälften: die Sema las nur die *deklarierten* Interfaces (bei einem Interface dessen Eltern), und
  das Lowering prüfte den geschriebenen statt des substituierten Empfängertyps.
- ~~**`rawArrayAlloc<T>(n)`** (~15 VM-Zeilen), billigster Posten der Liste~~ — **erledigt, und die
  Schätzung war falsch.** Generische Natives gab es nicht: `ModuleLowerer` übersprang jede Funktion
  mit Typparametern, *bevor* der Native-Zweig kam. Der Mechanismus lag aber eine Ebene tiefer
  bereit — `ImportTable.Intern` schlüsselt nach Name UND Signatur, weil `coroutineIsDone` seit je
  eine Zeile pro Coroutine-Signatur bekommt. Eine bodiless generische `fn` ist jetzt ein Template,
  die Aufrufstelle substituiert und interniert. **`[first, ..rest]` lowert damit** (Kopie, kein
  View — die Sprache hat keine Slices). Was daran hängen bleibt: `List<?T>`/`Map<K,?V>` können das
  Native jetzt benutzen, gebaut ist ihr Backing-Array damit noch nicht.

### D. Diagnostik

`IR0001` trägt Semantikfehler, die keine Implementierungsgrenze sind (fehlendes Pflichtfeld,
`p.x++`, `x == null` auf Nicht-Optional) — §12.1 reserviert den Code für gültiges Lyric. Dazu:
keine Deduplizierung, `SEM0058` vergiftet nicht, `SEM0052` schlägt das Geschriebene vor, Sema
läuft auf Parser-Recovery-Knoten, `--json` unvollständig, `--verbose`-Zeiten ×100 auf Linux.

**Eine Warnung, die einen Fehler erklärt, wird genau dann unterdrückt, wenn sie gebraucht wird**
(**NEU**, gemessen 2026-09-24). `WarningAnalyzer` läuft nur über ein Programm ohne Fehler, und
das ist **richtig so** — eine Warnung aus einer halben Tabelle ist eine Vermutung im Tonfall der
Gewissheit. Die Folge trifft aber genau die Fälle, für die manche Warnung gedacht ist:
`LYR-SEM0077` warnt vor einem Import, der einen eingebauten Typnamen verschattet, und schwieg,
während derselbe Import `RES0002` auslöste. Hier war die Auflösung der Fehler und ist gefixt; die
FORM des Problems bleibt und lohnt einen Blick, welche Warnungen sonst noch ihren eigenen Fehler
erklären würden.

~~**Die Meldung eines Lowering-Abbruchs kann am falschen Ort stehen**~~ — **erledigt** (PR #170).
Die Stelle ist die einzige in der Typtabelle, die einen geschriebenen Typ aufgibt, und sie wird
aus jeder Position erreicht, in der ein Typ stehen kann; sie nannte den häufigsten Aufrufer statt
den, den sie hatte. Wer das `IR0001` einer generischen Methode las, suchte nach einem Feld, das
nicht im Programm stand. Sie nennt jetzt den Typ.

### E. Zwei Regelfragen — beantwortet mit „v5, und bis dahin eine Warnung"

Der Maintainer-Entscheid vom 2026-09-24 nimmt beiden Posten den Bugfix-Charakter: die Regel faellt
mit v5, 4.6 bekommt die Uhr. **Gemessen am 2026-09-24**, beide Male am Integrationsstand:

- **Shadowing** (`SPEC-RUNDE` 4): `let x = 1; let x = 2; return x;` liefert **1**. Die zweite
  Bindung bekommt einen eigenen Slot, den niemand liest — auch wenn sie den Typ wechselt
  (`let x = "two";` und `return x` prüft weiter gegen `int`). Das einzige Signal heute ist ein
  `LYR-SEM0071` „'x' is never used" auf der **zweiten** Deklaration, was die Lage genau verkehrt
  herum beschreibt. Ablehnen oder Rust-Shadowing — beides ist besser als das; die Uhr sagt das,
  ohne die Antwort vorwegzunehmen. Prototyp 10 liegt vor.
- **Werfender `defer`** (`SPEC-RUNDE` 5): **gemessen, und beide Hälften sind in EINEM Programm
  sichtbar.** Ein Zähler in einem werfenden `defer` vor einem `return` steht danach auf **2**
  statt 1 (die Region deckt die Abwicklung an der Return-Stelle mit ab), und ein vorher
  registrierter `defer` daneben läuft **gar nicht** — Go liefe ihn. Die zweite Hälfte ist die
  offene Frage, die erste haengt an ihrer Antwort: der Ausweg ist eine Epilog-Route hinter der
  Region, mit dem Rückgabewert in einem synthetischen Local, und die baut man einmal, nicht
  zweimal. Also auch hier: Uhr jetzt, Regel mit v5.

---

## 4.7 — die nächste Feature-Runde

Zwei Quellen, abgeglichen am 2026-09-23: die elf Positionen des Evolution-Teams und
`lyric-v5-features.md`. Das Ergebnis des Abgleichs in einem Satz: **die alte Liste war nicht
falsch, aber sie war eine Auswahl** — sie kannte die Ergonomie-Hälfte kaum, die FFI-Hälfte gar
nicht und die Standardbibliothek überhaupt nicht.

Die **Reihenfolge** kommt aus der neuen Liste, und ihre Begründung trägt: A1 ist das Fundament,
an dem die halbe Standardbibliothek hängt. Ohne statische Interface-Member gibt es kein
`FromJson`, kein generisches `sum`, kein `parse<T>`; ohne bedingte Konformanz kein `Display` auf
einem Container.

### Das Fundament (P1) — hier zuerst

| # | Feature | Stand | Hängt an |
|---|---|---|---|
| 1 | `?T == ?T` | designt | — |
| 2 | Konformanz-Synthese (Equatable/Hashable/Ordered/Display), Swift-Modell ohne `derive` | designt | 1 |
| 3 | Bedingte Konformanz (`extend<T :: [Display]> List<T> :: [Display]`) | designt | 2 |
| 4 | **Statische Interface-Member und ein `Self`-Typ** (`static fn parse(s: string): ?Self`, `Default`, `Zero`/`One`) | **NEU** | — |
| 5 | typed throws St. 1 (Substitution an der Aufrufstelle — Bugfix) | designt | — |
| 6 | typed throws St. 2 (Inferenz von `E`, `throws never`) | designt | 5 |
| 7 | typed throws St. 3 (werfende Funktionstypen und Lambdas) | designt | 6 |
| 8 | `try`-Ausdruck (`try e catch (x: E) …`, `try? e`) | designt | Value-Block ✅ |

**4 ist der einzige P1-Posten, den die alte Liste gar nicht kannte**, und der teuerste Fund des
Abgleichs. Durch die Monomorphisierung kostet er zur Laufzeit nichts: aufrufbar nur über eine
Constraint (`T.parse(s)`), nie über einen Interface-Wert — der hat kein `Self`. Ohne ihn bleibt
die halbe `std.serial`/`std.json`-Liste unten unbaubar.

Die zwei P1-Posten **generische Methoden auf generischen Typen** und **restliche `IR0001`-Grenzen**
stehen in der neuen Liste ebenfalls als Fundament — hier bleiben sie unter **C**, weil sie Fehler
sind und 4.6 stabil wird.

### Ergonomie (P2/P3)

| # | Feature | Stand |
|---|---|---|
| 9 | Raw-/Mehrzeilen-Strings | Prototyp 19 |
| 10 | Named arguments | Prototyp 12 |
| 11 | `@NonExhaustive` | designt, Andockstelle benannt |
| 12 | **Inkrement abgeleitet statt eingebaut** (`SPEC-RUNDE` 1) | entschieden, nicht gebaut |
| 13 | **Indexierung verallgemeinern**: `Index<K,V>`/`IndexSet<K,V>`, damit `m["k"] = v` geht | **NEU** |
| 14 | **Collection-Literale für eigene Typen** über `FromLiteral` (`let m: Map<…> = {"a": 1}`) | **NEU** |
| 15 | **Struct-Update**: `p with { x = 3 }` | **NEU** |
| 16 | **Typ-Patterns auf Interface-Werten**: `match (shape) { c: Circle => … }` | **NEU** |
| 17 | **Enum-Reflexion per Synthese**: `E.variants()`, `E.fromName("…")` | **NEU** |
| 18 | **`comptime` ausbauen**: `embed("file")`/`embedBytes`, comptime-Tabellen | **NEU** |
| 19 | **Doc-Tests**: Code in `///` läuft unter `lyric test` | **NEU** |
| 20 | **`checked { … }`** für überlaufgeprüfte Arithmetik | **NEU** |

Zu **12**: der Maintainer hat die Richtung gesetzt — `++`/`--` folgen entweder aus `Add<T, R>`
(`x.add(1)`, verlangt eine Konformanz mit `int` als `other`) oder bekommen eigene `Inc`/`Dec`-
Interfaces. Empfehlung steht auf A (Rule 2: kein zweiter Mechanismus für „plus eins"). Im selben
Satz zu klären: die Statement-Form beider Schreibweisen (§6.8 lässt heute keine zu, akzeptiert aber
`x++;`) und worauf ein Inkrement stehen darf (heute nur auf einem Local, `p.x++` ist `IR0001`).
Die neue Liste hängt daran die **übrigen Operator-Interfaces** (`Neg`, `Rem`, Bit-Operatoren, `in`
über `Contains<T>`) — eine Runde, nicht zwei.

Zu **16**: geht ohne Typ-Tags auf Werten, weil der Fat Pointer seine Tabelle kennt. Das ist der
Grund, warum es hier stehen darf und nicht in der Ablage.

Zu **20**: §3.2 kündigt das Konstrukt bereits an („future checked mode … new construct"). Es ist
damit kein Vorschlag, sondern eine Einlösung.

### Braucht eine Entscheidung, bevor es überhaupt geplant wird

| # | Position | Warum nicht einfach einreihen |
|---|---|---|
| 21 | **Slices und Views** (`xs[a..b]` als `Span<T>`, Ranges als Werte) | 💥 Revidiert die Regel „Ranges sind kein Wert". Daran hängt, dass `[first, ..rest]` heute **kopiert** — genau die Einschränkung, die beim `rawArrayAlloc`-Posten oben schon aufgeschlagen ist. |
| 22 | **`extern "dotnet"` Stufe 2** (Arrays, Optionals, Structs, Instanzmethoden, Handles, Exceptions als `HostError`, Callbacks) | P1, und der größte Teil der Bibliotheksliste unten baut darauf auf. Ohne diese Entscheidung ist B nicht planbar. |
| 23 | **`extern "C"`** mit `std.ffi` und Capability-Bit `ffiAccess` | P3, `abi.md` Stufe 2. |
| 24 | **Worker-Isolates** (eine VM pro Worker, Austausch nur über Nachrichten) | 📜 bricht „single-threaded" und verlangt nach Rule 2 ein ADR mit 30 Tagen. |

---

## Die Standardbibliothek — neu in dieser Liste, und sie braucht zuerst eine Entscheidung

`lyric-v5-features.md` legt erstmals eine Bibliotheksplanung vor (Abschnitt B, sechs Gruppen von
`std.core` bis `std.test`). Sie steht hier absichtlich **nicht** als Tabelle: sie ist zu groß für
diese Liste und hängt an einer einzigen Frage.

**Die Frage.** `stdlib-2.md` hat Regex, HTTP-Client, Zeitzonen, Kompression und AES/RSA
**ausdrücklich aus der std ausgeschlossen** — „nimm `extern "dotnet"`". Die neue Liste
**revidiert das** mit dem Argument, .NET bringe alles davon mit, die Module würden dünne
Lyric-Hüllen über Natives und liefen unter den vorhandenen Capability-Bits.

Das ist eine Umkehr einer getroffenen Entscheidung, keine Ergänzung. Sie ist **nirgends
entschieden** und gehört vor jede Bibliotheksarbeit. An ihr hängt unter anderem `std.net.tls`,
womit sie den offenen TLS-Faden aus `STATUS.md` mitentscheidet.

Was unabhängig davon gilt: die P1-Gruppen `std.core`, `std.collections`, `std.iter`,
`std.option`/`std.result` hängen fast vollständig an den Fundament-Positionen 1–4 oben. **Vor
denen ist Bibliotheksarbeit nicht möglich**, egal wie die Frage ausgeht.

---

## Werkzeuge — ebenfalls neu

Aus `lyric-v5-features.md` Abschnitt D, hier vollständig, weil keins davon irgendwo sonst steht:

- **ein Paketmanager** mit Versionen, Lockfile und Registry (heute nur lokale Pfade)
- **`lyrfix`** als Migrationswerkzeug für die 5.0-Brüche — die Ablage unten setzt es voraus
- Ausdrücke im Debugger-`evaluate`
- ein Profiler
- Coverage

---

## Gesammelt für den Major — keine Planung, eine Ablage

Jede Position braucht eine 4.x-Warnstufe, bevor sie greifen kann. Die Uhren sind der eigentliche
Inhalt dieser Liste.

**Alle Uhren sind um eine Minor verschoben**: sie standen auf 4.6, und 4.6 nimmt keine Features
mehr auf. `lyric-v5-features.md` nennt für den größten Bruch von sich aus 4.7 — die beiden Listen
sind sich also einig.

**Und sie starten jetzt** (Maintainer, 2026-09-24): eine Warnung ist kein Feature, sie ändert kein
Programm, und je früher eine Uhr läuft, desto weniger Code muss `lyrfix` später anfassen. Was eine
Warnung heute tragen kann, ist die Frage, die dabei zu beantworten ist — sie muss einen Befund
haben, der sich ohne die Antwort auf die Regel stellen lässt, sonst warnt sie über Programme, die
nachher richtig sind.

| Position | Bruch | Uhr muss starten |
|---|---|---|
| Member-Sichtbarkeit (Default privat) | groß | **4.7** als Warnung |
| stdlib: Überladung statt Typ-Suffixe (`abs`/`absInt`) | groß | **4.7** als `@Deprecated` |
| `try e` → `Result<T, E>` | additiv, aber Anker | nach dem `try`-Ausdruck |
| f-String-Formatsprache spec-fixiert (Python/Rust statt .NET) | mittel | **4.7** |
| **`?Struct` als Wert** (`SPEC-RUNDE` 2) | mittel | — |
| **`let`-Struct-Felder** (`SPEC-RUNDE` 3) | klein | — |
| **Parameter vs. `let`** (`SPEC-RUNDE` 6) | klein | — |
| **`mut` auf Klassenmethoden erzwingen** | mittel | 4.7 als Warnung |
| **`mut struct`** — ein Struct ist unveränderlich, sofern nicht anders erklärt | groß | in Prüfung |
| **Deprecations wirklich entfernen** (**NEU**): freie Iterator-Terminatoren, `listContains`, `keys(m)`, die Zeitfunktionen in `std.os`, `addExecutable` | groß | Uhren laufen bereits — braucht `lyrfix` |
| **`spawn` liefert ein Handle** (**NEU**) | mittel | — |

Zur f-String-Zeile kommt aus der neuen Liste ein Detail dazu, das die Position erst vollständig
macht: ein **`Format`-Interface**, damit eigene Typen Specifier verstehen. Ohne das ist die
festgeschriebene Formatsprache nur für eingebaute Typen eine.

Die Zeile **Deprecations entfernen** ist die einzige der Ablage, deren Uhren schon laufen — die
Warnungen stehen seit 4.5 im Code. Was fehlt, ist das Werkzeug: `lyrfix` (siehe Werkzeuge oben).

**Die drei `SPEC-RUNDE`-Posten sind EINE Frage**, und `mut struct` beantwortet sie zusammen
(Maintainer-Idee, 2026-09-24; in Prüfung). Ist ein Struct unveränderlich, sofern es nicht
`mut struct` heißt, dann ist „geteilt" von „kopiert" nicht mehr unterscheidbar (Posten 2 entfällt,
und `struct Node { next: ?Node }` bleibt legal — der andere Weg hätte ihn unendlich groß gemacht),
ein `let`-Struct hat keine schreibbaren Felder (3), und die Parameterfrage stellt sich nicht mehr
(6). Es hängt an `p with { x = 3 }` (4.7 Position 15): ohne Update-Ausdruck ist ein
unveränderliches Struct nicht benutzbar, nur ertragbar. Es könnte Geschwindigkeit BRINGEN —
`structcopy` existiert gegen Aliasing, und was nicht mutieren kann, darf geteilt werden. Preis:
jedes Struct mit `mut fn` braucht das Schlüsselwort, also ein 5.0-Bruch mit mechanischer
Migration. Vorbild sind F#-Records, Scala-`case class` und Kotlin-Data-Classes; C# hat den
umgekehrten Default genommen und mit `readonly struct` nachgebessert.

Die drei aus `SPEC-RUNDE` hängen zusammen und sind bisher **nirgends entschieden**: `?Struct` teilt
heute statt zu kopieren (§13 zählt `?Struct` in keiner seiner beiden Listen auf), ein `let`-Struct
lässt seine Felder schreiben, und ein Parameter ist eine unveränderliche Bindung mit veränderlichen
Feldern — letzteres ausdrücklich getestet, ersteres nur, weil die Prüfung nicht bis zur Wurzel läuft.
An `?Struct` hängt außerdem, ob `struct Node { next: ?Node }` weiter läuft.

**Bewusst nicht in der Ablage** (Begründungen in der Team-Roadmap): nestbare Optionals, `Option<T>`
als Enum, `?`-Operator auf `Result`, Vererbung, `finally`, Threads, `never` als allgemeiner Typ,
Ordnung auf Optionals, Block-Ausdruck für jeden Block, `break value`, `derive`-Schlüsselwort.

`lyric-v5-features.md` führt dieselbe Liste kürzer und **legt zwei Posten dazu**: ein allgemeines
**Makrosystem** und **Laufzeit-Reflexion** (Werte tragen keinen Typ-Tag; Synthese und `comptime`
decken den Bedarf). Der erste ist bemerkenswert — ein Makro-System war eins der vier Dinge, die die
Feature-Branches im September *vorgeführt* haben. Es bleibt bei der Vorführung.

Umgekehrt **fehlen** in der neuen Liste fünf Ausschlüsse, die hier stehen: `never` als allgemeiner
Typ, Ordnung auf Optionals, Block-Ausdruck für jeden Block, `break value`, `derive`. Das ist
vermutlich Auslassung und keine Revision — aber es steht nirgends, also gilt hier weiter die
ältere, ausdrückliche Begründung.

---

## Was als Nächstes ansteht

1. ~~`lyric-spec#38` mergen~~ — gelandet (2026-09-23): die zwei retirierten Pattern-Limits **und**
   die Grammatik, die 4.5 wirklich beschreibt. Dann PR #162.
2. ~~**A** (Verifier-Reihenfolge)~~ — gelandet (2026-09-23), samt dem Defekt, den sie zutage
   gefördert hat. Ab hier misst jede Runde gegen eine CI, die das IR wirklich prüft.
3. ~~**B** (Prozessabbrüche) als eigene Sweep-Runde~~ — gelandet (PR #167, #168).
4. ~~**C**~~ — gelandet (PR #162, #169, #170). Sieben Posten, davon zwei, die keine Löcher waren.
5. **D** (Diagnostik) — der letzte Fehler-Abschnitt vor dem Tag.
6. **Die Warnstufen**: aus **E** und der Ablage, in einer Runde, mit einem Spec-PR für die neuen
   Codes. Sie sind kein Tag-Blocker im selben Sinn wie D — eine fehlende Warnung macht kein
   Programm falsch —, aber sie sind das, was der Entscheid vom 2026-09-24 verlangt.
7. Dann `v4.6.0` taggen. Kein Feature vorher.
8. Danach 4.7, in der Reihenfolge der neuen Liste: erst das Fundament (1–8), dann Ergonomie.
9. Parallel zu 8: die vier Entscheidungen aus der Tabelle „braucht eine Entscheidung" und die
   Bibliotheksfrage. Sie blockieren nichts an 4.6, aber alles, was danach kommt.

### Offen und nirgends entschieden

Der Abgleich hat vier Dinge hinterlassen, die niemand beantwortet hat. **Die Regelfragen der
Sprache sind seit dem 2026-09-24 beantwortet** — v5, bis dahin eine Warnung —; diese vier sind es
nicht, weil sie keine Sprachregeln sind, sondern Zuschnitt:

1. **Die Bibliotheks-Umkehr** — Regex, HTTP, TLS, Zeitzonen, Kompression, Krypto in die std oder
   weiter draußen? `stdlib-2.md` sagt draußen, `lyric-v5-features.md` sagt rein.
2. **Slices** — die Regel „Ranges sind kein Wert" halten oder fallen lassen?
3. **Worker-Isolates** — ein ADR aufsetzen oder den Posten streichen?
4. **Wohin mit dieser Datei.** `lyric-v5-features.md` sagt über sich selbst, sie liege „außerhalb
   der Repositories", weil CONTRIBUTING Rule 1 für Ideen nach v1 GitHub-Issues vorsieht und kein
   Roadmap-Dokument im Repo. Sie liegt aber im Repo. Entweder wandert sie in Issues mit dem Label
   `idea`, oder Rule 1 bekommt eine ausdrückliche Ausnahme für den `Befunde_und_Verbesserungen`-
   Ordner. Beides ist vertretbar; der heutige Zustand ist keins von beidem.
