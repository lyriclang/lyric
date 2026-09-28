# Tagesordnung Lyric 5 — Entscheidungsblöcke, Reihenfolge, Widersprüche, Uhren

Stand: 2026-09-28, main @ 6f6f029f (4.6 geschnitten). Material: 24 Gebietsdossiers unter
`scratchpad/v5-design/*.md`, Kompaktkatalog `_kompakt-20.json` plus vier inline gelieferte Gebiete.
Gezählt: **966 Designfragen** (nicht ~880 — die Dossiers sind seit der Kritikrunde gewachsen),
davon **62 major, 184 minor, 720 ohne Bruch**. Jede ID steht in genau einem der **18 Blöcke** (§6),
geprüft per Skript (`_agenda_blocks.py`: 966 zugeordnet, 0 fehlend, 0 doppelt).

**ID-Konvention.** Zwei Gebiete nummerieren `F1…`: Fehlerbehandlung bleibt `F1…F41`, das
FFI-Gebiet wird hier durchgehend `FFI-F1…FFI-F34` geschrieben. Alle anderen Präfixe wie in den
Dossiers (`W`, `L`, `B`, `E`, `G`, `T`, `S`, `NL`, `FN`, `OPT`, `OVL`, `MOD`, `META`, `CLI`, `SK`,
`COL`, `OP`, `IF`, `EP`, `BP`, `D`, `SL`).

**Wie lesen.** §1 ist die Sitzungsfolge. §2 begründet sie. §3 ist der wertvollste Teil: Stellen, an
denen zwei Dossiers dieselbe Frage verschieden beantworten — die müssen im Block *ausgesprochen*
werden, sonst entscheidet der, der zuletzt liest. §4 ist die Uhrentabelle (alle 246 Bruchfragen).
§5 listet, was vorweglaufen kann, ohne eine Sitzung zu brauchen. §6 ist die vollständige Zuordnung.

---

## §1 Blockliste in Sitzungsreihenfolge

| # | Block | Rolle | Fragen (major/minor) | Kern-Entscheid der Sitzung | Braucht vorher |
|---|---|---|---|---|---|
| B01 | **Migrationsapparat und Spec-Prozess** | FUNDAMENT 0 | 64 (1/9) | Was IST eine Uhr? §12.5-Familie und `@Deprecated` als EIN Mechanismus (D19); Unterdrücker (D3/SL-17/SK-24/META-22); `lyric fix` gespeist aus `replacement` (D9/CLI-19); JSON-Strom reparieren (CLI-29 F16) und versionieren (CLI-30/CLI-11); Attribute auf `extern` (FFI-F25); Spec-first-Ritual pro Block (IF-43/NL38/OP-23/G46). | — |
| B02 | **Wertsemantik: struct, mut, Kopie, Ort** | FUNDAMENT | 67 (5/26) | `mut struct` (W1/L2), Referenz-`this` (W35), transitives `mut` (W34), die Elf-Zeilen-Ortstabelle (W19), Kopierfamilie (L33/W22/L32), `?Struct` (W2/OPT-18), `[x]*n` (W36/COL-15). SEM0108/0109 laufen am Kern vorbei — erste Arbeit ist W34. | B01 |
| B03 | **Fehler, defer, Panik, typed throws** | FUNDAMENT | 47 (1/7) | Werfender `defer` wird Übersetzungszeitfehler (F7-D, schließt SEM0110); `throws A, B` + Instanzdeckung (F2/F19/F4); `try` als Ausdruck (F5); Panik läuft nichts (F33); Zwillinge (F18) gegen SL-04. | B01 |
| B04 | **Interface-/Generics-Fundament: Self, statische Member, bedingte Konformanz, Synthese** | FUNDAMENT | 73 (4/6) | PLAN-4.7-Fundament P1-1…4 in EINER Sitzung: `Self` (G02/IF-04/G32), statische Member (G03/IF-05), bedingte Konformanz + `extend`-Form (G05/G47/IF-07), Synthese-Paket Swift (META-13…16), Skalar-Boxing (L4), Zahl-Abstraktion (SK-10/11). | B01, B02 |
| B05 | **Auflösung: Dispatch, Überladung, Namen, Defaults, Literal-Adaption** | STAMM | 74 (10/24) | Eine Auswahlregel statt sechs (OVL-15/31/05), Constraints vor dem Ranking (OVL-02/G35), Member vor Extension (OVL-07/IF-13), Sichtbarkeit filtert (OVL-19/29), Defaults als Vertrag (FN36/IF-15/FN02), benannte Argumente (FN01/OVL-06), Literal-Adaption (SK-06/23/32), Lackmustest Typsuffixe (OVL-18/SL-06). | B02, B04 |
| B06 | **Module, Sichtbarkeit, Namensräume** | STAMM | 49 (7/10) | `pub` auf dem qualifizierten Weg (MOD-04), Member-Default privat (MOD-05), Kapselungseinheit Paket inkl. Testroot (MOD-02/16/41), Modulname = Datei (MOD-11/32/34/45), Orphan-/Kohärenzregel (IF-12/27/33), Prelude (MOD-18/S19). | B01, B04 |
| B07 | **Projektmodell, Build, Pakete, Toolchain** | STAMM | 42 (1/9) | Build-Skript als Graph mit `generated(...)` (BP-10/17), Hash im Manifest (BP-05/06/31), Toolchain-Pin in der Wurzel (BP-13/18), Interface-Sektion/kompilierte Bibliothek (BP-09 ↔ MOD-37), normatives Manifest-Dokument (BP-01/16), EIN `capabilities`-Schlüssel für fünf Gebiete. | B06, B01 |
| B08 | **Bytecode-Format, Validierung, Modulidentität, Attribute, comptime** | STAMM | 56 (10/7) | Typvektor + Nullinit (B1/B16, spec-first), Ressourcengrenzen (B17), Modul-Hash (B9/B28), Custom Section id 0 (B8), EINE Formatrunde für Attribut-Zielkinder (META-02/04/24), qualifizierte Attributnamen (META-01/36), `embed` (META-10/31), Impls-Rechnung (IF-01/21/41/48), Mangling (OVL-14/36). | B04, B06 |
| B09 | **Laufzeit und VM: Budget, Tiefe, Speicher, JIT, Optimierer, Backtrace** | STAMM | 61 (0/3) | Speicherbudget + Arbeitsgebühr (L7/L24/L31), Tiefe pro Thread (L5/L30/FN31), Allokations-/Trap-Vertrag (L19/L37/B2), JIT-Politik (B5/B22/L8/L20/NL44/E11), engine-unabhängiger Backtrace (B6/B7/L20), Optimierer nach Numerik-Spec (B20 nach B21), match-Fusion (B19). | B02, B08, B12 (B21) |
| B10 | **Sandbox, Capabilities, FFI, Einbettung** | STAMM | 59 (0/16) | `hostAccess` ist ein Bypass (FFI-F1/33), eine Marshalling-Tabelle + eine Fehlerregel (FFI-F2/20/21), Host-Fehler als Wert (FFI-F5), Handle-Modell (FFI-F22 ↔ L17 ↔ SL-20), Reentranz (FFI-F24), Callbacks (FFI-F16 ↔ NL35), Capability-Ableitung pro Native (SL-11), Werkzeuge unter der Standalone-Regel (E21/T23/CLI-16/35/36). | B01, B03, B09 |
| B11 | **Koroutinen, Tasks, Nebenläufigkeit** | STAMM | 40 (5/4) | Task-Handle + Ergebnistyp (NL1/2), Waker als EIN Typ (NL3/14/35), Abbruch gegen typed throws (NL6/19/24), strukturierte Nebenläufigkeit (NL7), Host-Pump `step()` (NL9), Isolates nur mit `mut struct`-Gate (NL15), Koroutinen-Cleanup (F22/F36). | B03, B02, B14 (@NonExhaustive) |
| B12 | **Skalare, Numerik, Überlauf, Determinismus** | BLATT | 39 (3/14) | Überlaufprüfung — Block, Bibliothek oder Profil (SK-02/03 ↔ OP-18 ↔ L12), `char` keine Zahl (SK-04), Literal-Bereich (SK-08/13/28/35), NaN/-0.0-Vertrag (SK-20/38 ↔ SL-26), Float-Determinismus (SK-39/L44/META-37), numerische Semantik als Spec §5a (B21). | B04, B05 |
| B13 | **Operatoren, Zuweisung, Ausdrucksformen, Lambdas, Funktionswerte** | BLATT | 43 (4/3) | Zuweisung wird void (OP-3/FN26), `++` als Statement (OP-33/46), Value-Block für if/try (OP-14/15/32 ↔ F5), Auswertungsreihenfolge (OP-21/38), Operator durch Interface-Wert (OP-42), Lambda-Formen und Rahmenbindung (FN07/24/39), `in`-Operator (COL-31). | B03, B04, B05 |
| B14 | **Abwesenheit und Muster: Optionals, Narrowing, Enums, Pattern Matching** | BLATT | 68 (1/11) | Nicht-Nestung + Nutzer-Enum als Ausweg (OPT-01/38), Narrowing-Regeln (OPT-07/08/24/40), `??`-Präzedenz (OPT-13), bloße Namen im Pattern (EP-01/36/40), Erschöpfungsmatrix (EP-03/24/28/39), `..`-Pflicht (EP-10), Präsenz-Pattern im ?T-Payload (EP-35/19), @NonExhaustive (EP-06/META-03). | B02, B04, B06 |
| B15 | **Collections, Iteration, Bibliotheksumfang, I/O** | BLATT | 48 (6/7) | Keine Slices (COL-02), Range als Wert (COL-03), `Index<K,V>` (COL-07), Iterator-Protokoll: Koroutine als Iterator vs `Step<Y,R>` (COL-26 ↔ NL23), Adapter als Extensions (COL-12), zwei Kettenwelten (COL-35/SL-09), Reader/Writer + Handles (SL-03/20/22/23), Ringe und Paketmanager (SL-01/02). | B04, B11, B02 |
| B16 | **Strings, Zeichen, Formatierung, Unicode** | BLATT | 31 (2/7) | Eigene Formatsprache mit Compile-Zeit-Prüfung (S05/06/07/08/20), Layout vs Presentation (S09), `debug()` (S10 ↔ OPT-16), Raw/Mehrzeilen (S12/13/25), native compare/hash (S04), Unicode-Ort und Case-Tabelle (S16/28/SL-12). | B04, B01 (S27 lyric fix), B15 (S03) |
| B17 | **CLI, Treiber, Profile, Testrunner** | BLATT | 61 (1/15) | Kommandozeile rechts vom Ziel (CLI-24), Optionskern mit Phasenspalte (CLI-5/10/23), Testroot als eine Kompilation (T6/T8/T26), Prozessschutz und Isolationseinheit (T28/T4), Testvokabular gegen SL-15 (T5/T15/T16), Doc-Tests (T17 ↔ E31), Exit-Codes (T31/CLI-32). | B01, B06, B07, B10 |
| B18 | **Editor, Debugger, REPL, Formatter, Diagnosequalität** | BLATT | 44 (1/6) | Analysemodell (E1/E17), Evaluate (E3/E5), Panik-Stopp (E4/E34), Lauschmodus (E16/E22), Formatter ohne Optionen als Versionsvertrag (E6/E29/E30), `lyric doc` (E12/E31/E32), Recovery-Lawine (D7/D32), Mehrfach-Spans und Terminalhygiene (D8/D10/D11). | B01, B17 |

Blockgrößen zwischen 31 und 74 Fragen; die vier Fundamentblöcke tragen 11 der 62 Major-Brüche,
zusammen mit B05/B08 sind es 31 — dort ist der Bruch, dort gehört die Zeit hin.

---

## §2 Reihenfolge und Begründung

### 2.1 Vier Schichten

**Fundament 0 — B01.** Vor jeder Uhr muss stehen, was eine Uhr ist. Heute existieren zwei
Familien für „das ändert sich mit 5.0“ (`@Deprecated{until}` an Deklarationen; SEM0107–0110 an
Regeln ohne Deklaration — D19), kein Unterdrücker (D3/SL-17; SEM0107–0110 sind nicht abschaltbar
und zählen gegen `--deny-warnings`, D20), kein `lyrfix` (grep über `src/`: 0 Treffer; PLAN.md:343
setzt es voraus — COL-38, G02, S27, OP-31 hängen daran), ein JSON-Strom, den eine Warnung des
eigenen Programms zerbricht (CLI-29 F16), keine CLI-Spezifikation, gegen die man überhaupt „Bruch“
sagen könnte (CLI-30), und eine Deklarationsform (`extern`), die kein Attribut trägt (FFI-F25,
LYR-PAR0042). Jeder spätere Block stellt Uhren; ohne B01 sind sie alle „Warnung, und in 5.0 bricht
es“. B01 ist zugleich die Sitzung, in der die Spec-first-Regel pro Block festgeschrieben wird
(IF-43, NL38, OP-23/45, G46, T20/T33): welcher Pin fällt, welcher kommt, mit welchem `since`.

**Fundament — B02, B03, B04.** Das sind die drei Stellen, an denen 5.0 die Sprache ändert und an
denen andere Blöcke *nicht entscheidbar* sind, solange sie offen sind:
- B02 (`mut struct`) ist Gate für NL15 (Isolates), COL-15/W36 (`[x]*n`), OPT-18/35/45 (`?Struct`),
  IF-14/23/24 (Struct hinter Fat Pointer), EP-16/26 (Pattern-Bindung), FN16/40 (Capture), L2/L32
  (Kopierstellen). Zwei Uhren laufen seit 4.6 daran vorbei (W34) — das ist die erste 4.7-Arbeit.
- B03 (typed throws Stufen 1–3, `defer`) ist Gate für NL2/NL6/NL19 (Task-Fehler, Cancelled),
  COL-27 (werfende Ketten), FN14/37 (werfende Funktionstypen), OP-22 (werfende Operatoren),
  EP-29 (catch als Pattern), T2 (assertThrows-Lambda), SL-04 (drei Namen), F5 ↔ OP-14/15/32
  (Value-Block). SEM0110 retiriert mit F7-D.
- B04 (Self / statische Member / bedingte Konformanz / Synthese) ist das 4.7-Fundament aus
  PLAN.md P1-1…4 und Gate für SK-10/11 (Zahl-Abstraktion), S09/S10/S17 (Formatierung, Boxing),
  COL-12/20/30 (Adapter, Gleichheit), OPT-15/16/31, NL11/18 (Koroutine als Iterator, Identität),
  SL-07/08/09/14 (Zahlentypen, Display, Serialisierung), T15/T16 (Property, Snapshots über
  stabiles Display), OP-5/13/42 (Operator-Interfaces).

**Stamm — B05…B11.** Regeln, die Fundamente voraussetzen und selbst viele Blätter tragen. Die
Reihenfolge darin: B05 (Auflösung) direkt nach B04, weil Constraints und Self die Kandidatenmenge
ändern; B06 (Module) vor B07 (Build) und B08 (Format), weil Kapselungseinheit (MOD-02/37) und
Export-Begriff (MOD-12/24) entscheiden, was BP-09 (Interface-Sektion) und der Bytecode-Name
(MOD-31/44) überhaupt bedeuten; B08 vor B09, weil B1/B16 (Typvektor + Nullinit) den
Validierungsvertrag setzen, auf dem B09 die Sandbox-Zusagen (L19/L37/B2) baut; B09 vor B10, weil
Speicherbudget (L7) und Trap-Typ (L19) die Hälfte der FFI-Antworten sind (FFI-F23/F24, F33); B11
zuletzt im Stamm, weil es B03 (Cancelled), B02 (Isolates), B14 (@NonExhaustive für Wait-Gründe,
gemessen r3 → SEM0050) und B04 (bedingte Konformanz für NL11) braucht.

**Blätter — B12…B18.** Entscheidbar, sobald der Stamm steht; untereinander nur lose gekoppelt
(B15 ↔ B16 über S03/COL-41, B17 ↔ B18 über E23/E24/E31/D-Codes). B12 (Numerik) ist als Blatt
eingeordnet, hat aber einen Rückweg in den Stamm: B21 (numerische Semantik als Spec §5a) muss vor
B20 (Konstantenfaltung, B09) stehen — deshalb wird B21 in B12 entschieden, aber vor B09
*ausgeliefert* (siehe 2.2).

### 2.2 Abhängigkeitstabelle

| Block | Braucht (Entscheid vorher) | Gibt frei | Rückweg / Vorgriff |
|---|---|---|---|
| B01 | — | alles | Liefert sofort: F16-Reparatur, Unterdrücker, `kind`-Feld, CLI-Spec-Grenze |
| B02 | B01 | B04 (IF-14/L4), B05 (OVL-21 mut), B11 (NL15), B14 (OPT-18), B15 (COL-15) | Spec-Selbstwiderspruch 03:77 vs 07:82 (W15/L32) VOR der Uhr klären |
| B03 | B01 | B05 (FN14), B11, B13 (Value-Block), B14 (EP-29), B15 (COL-27), B17 (T2), B10 (FFI-F5) | SL-04 ↔ F18 hier entscheiden, nicht in B15 |
| B04 | B01, B02 | B05, B12, B13, B14, B15, B16, B08 (G33) | G33-Messung (4.x-lyrbc gegen neues std.core) vor G02 |
| B05 | B02, B04 | B13 (FN01 Syntax), B12 (SK-06), B15 (SL-06/OVL-18), B06 (OVL-19/29) | OVL-20/39 (Auswahlkosten) sind Compiler-, nicht VM-Fragen |
| B06 | B01, B04 (IF-12/27) | B07, B08, B14 (EP-06/31), B17 (T6/T8/T26), B18 (E8/E25) | MOD-04 nur mit MOD-25 (eine Implementierung) und MOD-41 (Testroot) |
| B07 | B06, B01 (BP-13) | B08 (BP-09 ↔ IF-21), B10 (`capabilities`-Schlüssel), B17 (CLI-8/21/38, T23-C), B18 (E21-C/E32) | BP-29 (Reproduzierbarkeits-Test) sofort — Hash und Fingerprint ruhen darauf |
| B08 | B04, B06 | B09, B10 (Header-Felder FFI-F10/29), B17 (T7 Konformanzen im Modul), B05 (OVL-09/14) | EINE Formatrunde 4.1: META-02/04/24, B8, B9, B17, W43, NL1, F40, OVL-14 |
| B09 | B02, B08, B21 (aus B12) | B10, B11 (NL29/30/36/44), B13 (FN17), B18 (E11) | B21 vorziehen: Faltung darf nicht anders rechnen als die VM |
| B10 | B01 (FFI-F25), B03, B09 | B11 (NL32/35), B15 (SL-20, COL-37), B17 (T23/T24), B18 (E21) | FFI-F1/F7/F28 VOR der Bibliotheks-Umkehr (SL-01/SL-13) |
| B11 | B02, B03, B04, B14 | B15 (NL11/23/31), B17 (T34, T14) | @NonExhaustive (META-03) in 4.7 ist harte Vorbedingung |
| B12 | B04, B05, B01 (SK-24) | B09 (B21), B16 (SK-18/33/34), B15 (SK-16 ↔ COL-06) | §3.2 muss sagen, ob ein Block „a change to these operators“ ist |
| B13 | B03, B04, B05 | B14 (EP-9), B15 (COL-17/31) | OP-21 (Auswertungsreihenfolge) vor OP-10 |
| B14 | B02, B04, B06 | B11 (@NonExhaustive), B15 (OPT-19, COL-36), B16 (S29) | EP-39 (Spec nennt Scrutinee-Formen) vor EP-03 |
| B15 | B04, B11, B02, B14 | B16 (S03), B17 (SL-15) | COL-34 (Spec kennt `+`/`*` auf Arrays nicht) vor COL-15 |
| B16 | B04, B01 (S27), B15 | B17 (S25/S30 Golden-Stabilität) | S22 (f-String-Lowering) vor S18-D |
| B17 | B01, B06, B07, B10, B03 | B18 (E23/E24) | F16/F17 (JSON) und T28 (exit im Test) sind Sperren vor jeder Zusage |
| B18 | B01, B17 | — | Migrationsuhren im Editor (E9) sofort; keine Fixes an SEM0107–0110 (E28) |

### 2.3 Was die Reihenfolge NICHT ist

Sie ist keine Release-Reihenfolge. Aus jedem Block fallen 4.7-Posten (§5) und 5.0-Posten; die
4.7-Arbeit beginnt mit B01 (Apparat) und W34 (B02), nicht mit dem ersten Blatt. Und sie ist keine
Gewichtung: B16 hat 31 Fragen und mit S05 den zweitteuersten Einzelbruch der Runde (jedes f-String
mit Spezifizierer).

---

## §3 Widersprüche zwischen Gebieten

Kriterium: zwei Dossiers empfehlen für dieselbe Frage Unvereinbares, oder eine Empfehlung setzt
voraus, was eine andere ausschließt. Nicht aufgeführt: bloße Abhängigkeiten und Fragen, die ein
Dossier ausdrücklich an ein anderes abgibt. **Die fünf wichtigsten sind mit ★ markiert.**

| # | Frage | Seite A (IDs, Gebiet) | Seite B (IDs, Gebiet) | Warum es zählt | Wo entscheiden |
|---|---|---|---|---|---|
| ★W1 | **Überlaufprüfung: Block, Bibliothek oder Profil-Schalter?** | SK-02 A + SK-03 A + SK-40 (Skalare): `checked { }` als C#-Block, §3.2 hat es zugesagt | OP-18 (Operatoren): C (Bibliothek `addChecked`), dann B (Zig-Operatoren); A erst, wenn §3.2 sagt, ein Block sei kein „change to these operators“; **D (Profil) normativ ausgeschlossen** (03-types.md:51-53 „not configurable“) — gegen L12 (Laufzeit): C **plus Profil-Schalter `overflow-checks`**, Verhalten pro Paket beim Kompilieren fixiert (Rust crate-weise) | Drei Gebiete, drei Konstrukte; L12 empfiehlt genau das, was OP-18 als normativ verboten liest; comptime muss dasselbe rechnen (OP-34/SK-26). | B12, mit B08/B09 anwesend (Opcodes/Flag) |
| ★W2 | **Was liefert `Coroutine.next()` — und ist eine Koroutine ein Iterator?** | COL-26 B (Collections): `Coroutine<T> :: [Iterator<T>]`, „die Signatur stimmt bereits, absichtlich“ (`next(): ?T`); NL11 B ebenso | NL23 A (Nebenläufigkeit): `next()` wird `enum Step<Y,R>`, `bool`-/`?T`-Form aufgegeben, zwei Pins retirieren, **major** | Mit Step<Y,R> stimmt die Signatur nicht mehr; COL-26 „fast umsonst“ wird zu einer Adapterfrage. OPT-19 B und COL-10 A (?T bleibt Ende) hängen mit dran. | B15 (alle vier IDs dort) |
| ★W3 | **Fängt eine Closure ein `let`-Struct als Kopie oder Alias? Die Spec sagt beides.** | W15 A (Werte): Referenz beibehalten, Fallstrick benennen — beruft sich auf `spec/03-types.md:77` | L32 A (Laufzeit): `LoadCaptured` kopiert, 4.7-Bugfix mit since-Fall — beruft sich auf `spec/07-statements.md:82` „a let is captured as its value“; gemessen R3e: heute Alias | Beide Fassungen zitieren normative Sätze, die sich widersprechen. Bevor eine Uhr läuft, muss der Spec-PR sagen, welcher Satz fällt (FN16/L22 Capture-Modell hängt daran). | B02 |
| ★W4 | **Doc-Tests: kompilieren durch `lyric doc` oder ausführen durch `lyric test`?** | E31 A (Editor): Blöcke werden von `lyric doc` kompiliert, „B nicht — die Annotationssprache (`ignore`/`no_run`) ist ein zweiter Mechanismus neben `@Test`“ | T17 D→A+B (Testen): `lyric test` kompiliert, dann führt es aus, mit Opt-out-Marker und 4.x-Uhr; META-20 B (Meta): Doc-Test ist ein gewöhnlicher `@Test` und erbt RES0004 | Beide berufen sich auf Rule 2 und kommen zu Gegenteilen; PLAN.md:289 (#19) will Ausführung. | B17 mit B18 |
| ★W5 | **Bekommt `float` ein `Hashable`, und was tun NaN/-0.0?** | SL-26 A (stdlib): **kein** Hashable für Gleitkomma, Absicht schreiben; `float32/64` hashen „gar nicht“; EP-22 B (Patterns) beruft sich auf dieselbe Absage in `core.lyr:539-546` | SK-38 A + SK-20 A (Skalare): `equals` bleibt IEEE, **hash und compare normalisieren** (-0.0→0.0, NaN-Muster), `total_cmp` — setzt Hashable<float> voraus; SK-21 „nicht gratis“ | Map<float,_> ja oder nein ist eine Bibliotheksfrage mit Spec-Satz (§6.2 „from ONE method“); COL-39 D (binarySearch braucht totale Ordnung) hängt daran. | B12, SL-26 aus B04 dazuholen |
| W6 | Welcher Typ indiziert ein Array? | COL-06 A: nur `int`, „keine implizite Weitung, der Index ist keine Ausnahme“ | SK-16 B (5.0): jeder Ganzzahltyp indiziert (Go/C#), `uint` als Größe ist der natürliche Fall; SK-31 C gibt dem Shift-Count dieselbe Antwort | Direkt unvereinbar; der ICE (CLI0020) fällt in beiden Fällen 4.7. | B15 (SK-16 dorthin verschoben), SK-31 folgt in B12 |
| W7 | Bleiben die `OrThrow`-Zwillinge, oder fällt die stille Form? | F18 A (Fehler): Zwillinge bleiben — unter `LYRIC_JIT=1` ist die stille Form ~3× billiger, Wurfpfad bleibt interpretiert; F16 A: `Result` bleibt Bibliothek | SL-04 B (stdlib): eine werfende Funktion pro Operation, `try? e`/`try e → Result` liefern die Antwortform; stille Zwillinge nur, wo `null` die ganze Wahrheit ist; braucht ADR (K1) | SL-04 ist major und setzt F5-C voraus; F18 hat es gerade mit Messung verworfen. | B03 (SL-04 dort einsortiert) |
| W8 | `Self` für `Equatable`: Pflicht oder Parameter bleibt? | IF-04 D (Interfaces): `Equatable/Ordered/Hashable` auf `Self` „muss in 5.0 mit“ (STATUS.md:2496), Warnung ab 4.8 | G02 A/D + G32 B (Generics): `Self` nur für Hashable/Ordered, `Equatable<T>` behält den Parameter für immer (Mehrfachkonformanz `Tag :: [Equatable<Tag>, Equatable<int>]` seit 3.0) | Entscheidet, ob `lyrfix` `Equatable<X>` mit X≠Self als „nicht migrierbar“ melden muss; G33 (Formatkompatibilität) hängt daran. | B04 |
| W9 | `Debug` als zweites Interface oder `debug()` in `Display`? | OPT-16 C (Optionals): getrennte `Debug`-Konformanz (Rust), „verbindlich vor der Synthese“; META-14: `Debug` als geplante Synthesezeile | S10 C (Strings): `debug()` als Default-Methode **in** `Display`, „kein zweites Interface“ (Rule 2); SL-31 A ebenso (Default-Mitglied `showNested`) | Rule 2 wird von beiden Seiten beansprucht; entscheidet, wie `{x:?}` und Container rendern. | B04 (Synthese), S10/OPT-16 dazuholen |
| W10 | Wie schweigt man eine Warnung ab — welcher Mechanismus? | SL-17 C2 / SK-24 A / EP-32 C: Attribut `@Allow{warning=…}` an Funktion und Modul | META-22 C (4.x): Allow-Liste in `lyric.json`, A/B (5.0) an der Deklaration; D3 D (Diagnostik): Quelltext-Direktive im `expect`-Stil, nur für spec-gelistete Codes, meldet sich, wenn überflüssig; D20 D: gruppenweise | Fünf Gebiete, drei Mechanismen für ein Konzept — die eigentliche Rule-2-Kollision von B01. Ohne Entscheid startet keine Uhr sauber (SK-24, META-08, EP-01 setzen es voraus). | B01 |
| W11 | Darf ein Host die Map-Reihenfolge randomisieren? | L23 C (Laufzeit): Default-Seed fest, **Host darf randomisieren** (`HostOptions.HashSeed`, Python-Vorbild umgekehrt gepolt) | COL-28 A+D (Collections): „C lehne ich ab: eine Sprache, deren zweites Produkt die Einbettung ist, baut keine Zufallsquelle in die Iterationsreihenfolge“ | HashDoS-Schutz gegen Replay-Determinismus; beide haben das Sandbox-Argument. | B15 |
| W12 | Toolchain-Verwaltung: Verb oder Nicht-Ziel? | CLI-18: A+D, „C (Multiplexer) ausdrücklich als Nicht-Ziel; ein persönliches Lernprojekt braucht keinen Versionsmultiplexer“ | BP-13 A+C: `lyric toolchain install/use` als Verb, Obergrenze im Pin, 4er-Projekt über 5er-Compiler | Ohne Verb ist BP-13 C („Bremse für Projekte, die nicht migrieren“) Handarbeit; mit Verb ist es das zwölfte Binary. | B07 mit B17 |
| W13 | Darf `--help` das Build-Skript ausführen? | CLI-3 C + CLI-37 (CLI): `--help` darf das Skript ausführen (der Mensch hat es angefordert), nur Completion nie | BP-10 B (Build): `build()` läuft gesandboxt, „`--help` führt nichts mehr aus“; D als 4.x-Vorarbeit (Optionen ins Manifest) | Gleiche Messung (r3/F22), gegensätzliche Regel; entscheidet, woher `-D`-Optionen in der Hilfe kommen. | B07, CLI-3/37 dazuholen |
| W14 | Testvokabular: Setup/Teardown, Property, Snapshots | T5 B: `@Setup/@Teardown/@SetupAll/@TeardownAll`; T15 C: Generatoren als Werte in `std.test`; T16 A: `assertSnapshot` + `--update-snapshots` | SL-15: Setup/Teardown **ablehnen** („ein Test ist eine Funktion“, `test.lyr:15-17`); Property und Snapshots in den **zweiten Ring** | Derselbe Gegenstand, gegenteilige Antworten in einer Sitzung — beide Dossiers liegen in B17. | B17 |
| W15 | Was ist ein Handle: Klasse, Zahl in Tabelle oder Index-Ressource? | SL-20 A (stdlib): Handles werden **Klassen** (`class File { fd }`), Identität = Referenz, damit `Reader`/`Closeable` konformierbar; COL-37 B will `extend` auf opaque | L17 C (Laufzeit): Zahlen bleiben, Tabelle wird Sub-Arena; FFI-F22 D+C (FFI): WIT-`resource` — Identität über Index, Lebensdauer über Arena; Memory „opaque type + TypeTag.Host, keine Formatänderung“ | Drei Modelle für einen Wert, den `defer f.close()`, die Sandbox-Arena und die Host-Grenze alle sehen. | B10 (Modell), B15 (SL-03 Reader baut darauf) |
| W16 | Benannte Argumente: `:` oder `=`? | FN01 A: `f(name: value)` mit Vor-Filter; OVL-06 B: Namen filtern | META-21 A: Attributargumente sind Struct-Initialisierer mit `=` — „`@Retry(limit: 3)` mit `:` neben `Retry { limit = 3 }` wäre die Aufspaltung“; FN26 B'/OP-3 B machen `=` frei | Eine Schreibweise für „Name = Wert“ oder zwei; hängt an OP-3. | B05 (Regel) + B13 (Syntax) |
| W17 | Slices/Views und Map-Literale gegen die eigene Aktenlage | COL-02 B: keine Slices, kein View („zweiter Sequenztyp“); COL-13 B' ohne Map-Form; S03 A ebenso | `lyric-v5-features.md:42` und `PLAN.md:310` fordern wörtlich `Span<T>`; `:43` fordert `let m: Map<string,int> = {"a": 1}`; L1 C (gepackte Arrays) und SL-25 B rechnen mit Byte-Views | Das Projekt hat das Gegenteil zweimal aufgeschrieben; die Runde muss die Akte ändern oder die Empfehlung. | B15 (mit Rule-1-Frage: wo landet die v5-Liste) |
| W18 | Ist `lyric.json` JSON oder JSONC? | BP-01 A: „bleibt JSON“, Schlüsselliste in eigenem normativen Dokument | CLI-38 A: das Kapitel heißt „`lyric.json` ist JSONC“, Kommentare und nachlaufende Kommata ausdrücklich; `lyric env --json` als strikter Ausweg; CLI-15: die Vorlage schreibt heute `//` | Klein, aber dieselbe Datei mit zwei Namen für ihr Format. | B07 |
| W19 | Callbacks aus fremden Threads | FFI-F16 D: Guard (Thread-ID) als Sprachregel, Queue als Host-Fähigkeit; `design/abi.md:78` verbietet | Designrunde 2026-09: „Callbacks = Queue + Lyric-Handler“; NL35 C: Waker/Descriptor als EIN Typ, Fremdwecken über notify-fd | Zwei Dokumente, zwei Antworten — vor FFI-F3-B (Callbacks über die Grenze) auflösen. | B10 mit B11 |
| W20 | „single-threaded“ und mehrere VMs | NL15: Isolates nur mit `mut struct`-Gate, Rule-2-Satz umformulieren; B22/B27 (Bytecode): Worker-Isolates brauchen ADR | T14 B: Runner fährt mehrere VMs auf Threads („kein Bruch, der Runner ist ein Host“ — STATUS.md:645 „argued, not tested“); T28 C: Kindprozesse umgehen es | Ob Cross-VM-Isolation eine Zusage ist, entscheidet, ob `--jobs` ohne Prozesse zulässig ist. | B11, T14/T28 dazuholen |
| W21 | JIT × Budget × Profil | B5 B: `release` kompiliert „wo niemand zuschaut“, `--no-jit` als Profilfeld; B22 B (mit ADR): Zähler im kompilierten Code | L8 C nur mit L20 (engine-unabhängiger Backtrace); NL44 A: der deterministische Frame ist der interpretierte, `step()` ohne Budget; E11 A: Debug-Profil = Interpreter als Design-Entscheid | Intern benannt (B5 gegen B22 gegen B23-C): ein Profilfeld, das beim Budget-Host nichts tut, lügt. | B09 |
| W22 | Darf `extend` Arrays/Tupel/opaque als Ziel haben? | G20 A + IF-11 C: nur nominale Ziele in v5, Arrays/Tupel refusen (B danach) | COL-14 „B, mit C als Ziel“: Array-Oberfläche über `extend int[]` (heute stiller No-Op, strukturell ohne Träger); COL-37 B: opaque darf `Iterable` sein; OPT-49 B: `extend ?T` als eigene Frage | Drei Gebiete, drei Zielmengen; MOD-20/OP-12 (Orphan) ziehen mit. | B04 (IF-11/G20), COL-14/37 dazuholen |
| W23 | Wie sieht `lyric fix` aus? | CLI-19 D (=A `lyric fix`) + D9 B: die **Diagnose trägt die Ersetzung**, ein Verb, kein zweites Binary; SL-28 A | OP-31 A: Minimum-`lyrfix` **parst, ersetzt Knotenformen, schreibt über Lyrfmt zurück**; META-33 A: eigenes Werkzeug (D als Kompromiss); S27 B+D: eigenes Verb | Diagnose-getrieben und AST-getrieben sind zwei Bauweisen; S05 Klasse 3 und W45 („mechanisch/bedingt/nie“) brauchen die Antwort. | B01 |
| W24 | `@Bench` | T18 A + SL-15: `@Bench` in Go-Form in `lyric test`, 5.0 | CLI-22: „`bench` vertagt — das Vorbild stützt keine Attributbindung“ | Klein; T7 B (Attributvertrag pro Attribut) ist die Voraussetzung. | B17 |
| W25 | `params`-Spread: Kopie oder Weiterleitung? | FN05 B: `*arr`-Spread **kopiert** (Kotlin) | COL-19 A: `params` leitet **ohne Kopie** weiter — als dritte Zeile der Teilungstabelle COL-33 | Zwei Formen, zwei Regeln; COL-33 will EINE Tabelle. | B15 |

**Prozessverstöße, keine Widersprüche, aber in B01 zu protokollieren:** OP-25/OP-45
(Display-Interpolation in 4.6.0 gegen §6.6 released, kein Regel-PR); FFI B2 (`extern` steht in
keinem normativen Kapitel außer Grammatik/Anhang); G18/G21/G37/G38 (Konformanzverstöße gegen
geltende Regeln, heute rot); T33-C (`rstrip` im Referenzrunner gegen „byte-exact“); IF-13 (§5.4
heute verletzt, Suite pinnt die 4.6-Regel nicht); EP-39 (Implementierung der Spec voraus bei einer
soundness-tragenden Prüfung); OPT-24 (einziger Posten, der einen bestehenden Konformanzfall ersetzt).

**Konvergenzpunkte (viele Frager, ein Mechanismus — im genannten Block als EINE Entscheidung):**
- `capabilities` in `lyric.json`: FFI-F27, MOD-23, CLI-35, E21-C, T23-C, META-10 C, META-22 C,
  META-27 C, BP-10 — das Build-Dossier stellt die Frage selbst nicht. → B07.
- Modul-Header/Format-Minor 4.1: FFI-F10 (ABI), FFI-F29 (Plattform), B8 (Custom Section id 0),
  B9 (Hash), B17 (Grenzen), W43 (Sprachversion), OVL-14 (Mangling), IF-21/BP-09
  (Interface-Sektion), META-02/04/24 (Zielkinder), NL1 (zweiter Typparameter an `mkcoro`),
  F40 (`throws` in der Interface-Sektion), META-31 C (Bytes-Sektion). → B08, eine Runde.
- Maschinenstrom: CLI-11, D15, D18, D21, D30, T12, T25, T31, E2, E24, CLI-29, CLI-32. → B01.
- Attribute-Anker `OnInterface`: OP-29 (vor OP-6/OP-13), META-24, META-03. → B08.

---

## §4 Uhrentabelle — alle 246 Fragen mit Bruch minor/major

Vier Uhren laufen seit 4.6 (LYR-SEM0107 Rebinding, SEM0108 `mut` auf Klassenmethode, SEM0109 Feldschreibung durch `let`-Struct, SEM0110 werfender `defer`). Sie sind Uhren, keine Antworten — die Tabelle sagt, welche Frage sie tragen und wo sie den Kern verfehlen (W34: beide laufen am Aufrufpfad vorbei). „4.7“ heißt: nächstes Minor, nach dem Entscheid im Block. „keine Uhr“ nennt den Grund (Format, Bugfix gegen geltende Spec, additiv, Laufzeitverhalten).

| Block | ID | Bruch | Frage | Uhr startet | Was die Warnung sagt / Bedingung |
|---|---|---|---|---|---|
| B01 | META-23 | minor | Wer trägt `LYR-SEM0081`, wenn eine Quell-Abhängigkeit eine abgelaufene Zusage enthält? | keine Uhr: Ruecknahme | SEM0081 beim Konsumenten wird Warnung statt Fehler — ein Fehler weniger, nichts zu warnen. |
| B01 | BP-13 | minor | Toolchain-Pinnung: wie kommt ein 4er-Projekt über einen 5er-Compiler? | 4.7 | lyric new schreibt Obergrenze; lyric build: 'project declares no toolchain bound — a 5.0 compiler will refuse it'; addExecutable braucht lyrfix. |
| B01 | CLI-11 | **major** | Maschinenlesbare Ausgabe | 4.7 Uebergangsform, 4.8–4.9 Uhr | 4.7: --message-format=json-document / jsonl, --json = json-document. 4.8: '`--json` will mean jsonl in 5.0 — pass --message-format=json-document to keep the document form'. F16/F17 VORHER. |
| B01 | CLI-32 | minor | Eine Leiter fuer Exit-Codes, nicht ein Paar | 4.7 Ankuendigung, Wechsel 5.0 | Ein Exit-Code kann nicht warnen: jeder Fehler druckt seine Kennung; CHANGELOG + Tabelle in der CLI-Spec (CLI-30). |
| B01 | D1 | minor | Bleibt IR0001 ein Sammelcode? | keine Uhr: Codes since-gegated | IR0001 wird gespalten; Pins tragen since. |
| B01 | D2 | minor | Wie viele Ausgaenge braucht die Lowering-Ablehnung? | keine Uhr: Codes | Zwei Reporter plus §12.5-Familie fuer 'unentschieden' (p.x++). |
| B01 | D4 | minor | Bleibt --deny-warnings alles-oder-nichts? | keine Uhr: A' nach D20 | lyrbuild bekommt --deny-warnings erst, wenn D20 sagt, was Migrationswarnungen dabei tun — sonst ist der Schalter sofort rot. |
| B01 | D15 | minor | Was ist der maschinenlesbare Ausgabevertrag? | = CLI-11 | Vier Pflichtfelder (kind, truncated, spans[], replacement) in 4.7 mitnehmen, sonst einzeln nachgeruestet. |
| B01 | D18 | minor | Ist panic [CODE] eine Diagnose? | 4.7 additiv | `kind: panic` im JSON; --json ignoriert die Panik nicht mehr stumm. |
| B01 | D30 | minor | Welchen Exit-Code bekommt was? | 4.7 Ankuendigung wie CLI-32 | Exit 3 fuer 'Warnung unter --deny-warnings'; gekappter Lauf gibt den schwersten Befund. |
| B02 | W1 | **major** | Ist ein Struct unveraenderlich, sofern es nicht `mut struct` heisst? | 4.7, dreiteilig | An der Deklaration: 'struct S has mut fn / field writes — declare `mut struct S` for 5.0'; an jeder Schreib-/extend-Stelle ausserhalb des Moduls: 'S is not mut struct — write refused in 5.0'. |
| B02 | W2 | minor | Hat ein `?Struct` Wertsemantik? | 4.7 auf der §3.4a-Uhr | SEM0109-Erweiterung: 'write through ?Struct aliases today, copies in 5.0' (auch `!` und `?.`). |
| B02 | W3 | **major** | Reicht `let` bis in die Felder? | laeuft (SEM0109) direkt; 4.7 Aufrufpfad | 'field write through a let binding'; W34 ergaenzt this.bump()/this.inner.x. |
| B02 | W4 | minor | Ist ein Parameter dasselbe wie ein `let`? | 4.7 | 'field write on a parameter: a parameter is a let in 5.0'. |
| B02 | W5 | minor | Wer darf ein `mut fn` rufen? | 4.7 | 'mut fn called on a let receiver / temporary — refused in 5.0'; keine defensive Kopie (CS8656 als Gegenmodell). |
| B02 | W6 | minor | Was bedeutet `mut` auf einer Klassenmethode? | laeuft (SEM0108) direkt; 4.7 Aufrufpfad | 'mut on a class method has no meaning in 5.0'; Aufrufpfad ist W34. |
| B02 | W8 | minor | Was bedeutet eine zweite Bindung desselben Namens? | laeuft (SEM0107) | Rebinding-Uhr; retiriert mit A (SEM0097 bleibt Fehler). |
| B02 | W17 | minor | Schreibung auf einem Temporary (mut fn und Feldzuweisung) | 4.7 | 'write to a temporary struct value is lost — error in 5.0'. |
| B02 | W18 | minor | Veraenderlicher Modulzustand | 4.7 = W3 | 'field write through a module-level let'. |
| B02 | W19 | **major** | Was ist die Wurzel einer Zugriffskette? | 4.7: Elf-Zeilen-Tabelle als Spec | Die Uhr ist die Vereinigung von W3/W5/W17/W37-Texten; jede Zeile traegt 'lyrfix: mechanisch/bedingt/nie' (W45). |
| B02 | W20 | minor | Ist `static let` eine Bindung im Sinne von W3? | 4.7 | 'field write through static let — refused in 5.0' (CS1650). |
| B02 | W21 | minor | Ist eine Pattern-Bindung ein schreibbarer Ort? | 4.7 Textfix | SEM0109 nennt heute 'let' falsch; 'pattern binding is not a writable place in 5.0'. |
| B02 | W24 | **major** | Wie mischen sich struct und mut struct? | keine eigene Uhr: folgt W1 | Fuenf Zeilen Spec-Text. |
| B02 | W25 | minor | Traegt ein Typparameter Veraenderlichkeit? Und mut struct fuer Interfaces? | 4.7 | 'mut fn through a type parameter / interface value — 5.0 requires mut struct'. |
| B02 | W26 | minor | Warum ist ein IndexExpr auf einem Struct ein veraenderlicher Ort? | 4.7 | 'index write on a struct-typed temporary has no effect — error in 5.0'. |
| B02 | W27 | minor | Auswertungsreihenfolge von Initializer-Feldern und Defaults | keine Uhr praktikabel: Spec-Satz | Auswertungsreihenfolge in Schreibreihenfolge festschreiben (Rust); Doku 4.7. |
| B02 | W30 | minor | Was tut ein mut fn durch `!` auf einem ?Struct? | 4.7 mit W2 | 'mut fn through `!` on ?Struct'. |
| B02 | W34 | minor | Ist `mut` transitiv? | 4.7 — ERSTE Arbeit des Gebiets | SEM0108 UND SEM0109 auf den Aufrufpfad: 'this.bump() writes a field from a non-mut fn — refused in 5.0'. |
| B02 | W36 | minor | Kopiert `[x] * n` das Struct n-mal oder teilt es n Slots? | 4.7 mit COL-15 | '[x] * n shares one struct'. |
| B02 | W37 | minor | Was bedeutet `l[0].v = 9` auf einem Indexable-Klassencontainer? | 4.7 | 'l[0].v = 9 on a class container writes to a copy and is lost — in 5.0 an error; use `l[0] = l[0] with { v = 9 }`'. |
| B02 | W38 | minor | Wie mutiert eine freie Funktion ein Struct des Aufrufers, und wer darf ein mut fn anfuegen? | 4.7 | 'extend S { mut fn ... } outside the declaring module — refused unless S is mut struct'. |
| B02 | W39 | minor | Sind Schleifen-, if-let-, Destrukturierungs- und catch-Bindungen unveraenderliche Bindungen? | 4.7 Textfix | SEM0109 an Schleifen-/if-let-/catch-Bindungen doppelt falsch; eigener Text/Code (W32). |
| B02 | W41 | minor | Was tut `o?.mutate()` auf einem ?Struct? | 4.7 mit W2 | 'o?.mutate() on ?Struct mutates an alias today'; void-Luecke ist IR-Posten (Optionals). |
| B02 | L2 | **major** | Was ist ein struct — und wo wird eine Kopie genommen? | = W1 | Dieselbe dreiteilige Uhr. |
| B02 | L3 | minor | Was bedeutet let für ein Struct? | 4.7 = W3/L2 | 'let struct: fields are read-only in 5.0'. |
| B02 | L32 | minor | Struct beim Einfangen in eine Closure: Kopie oder Alias? | keine Uhr moeglich: 4.7 Bugfix mit since-Fall | Alias-Nutzung ist statisch nicht erkennbar; Spec (07:82) hat sie nie erlaubt — Verhaltensaenderung im CHANGELOG. |
| B02 | L33 | minor | Kopiert structcopy durch ?Struct- und Interface-Felder? | 4.7 = W2 | structcopy durch ?Struct: Warnung auf Schreibpfad, Kopie 5.0; Interface-Feld bleibt Referenz (Wortlaut). |
| B02 | OPT-18 | minor | Ist ein ?Struct ein Wert? | 4.7 auf der §3.4a-Uhr (nach B02) | Erweiterung von SEM0109: 'write through an optional struct aliases today; 5.0 copies'. |
| B02 | OPT-45 | minor | x! als lvalue | 4.7 mit OPT-18 | 'x!.f = v writes through the alias; 5.0 treats x! as a copy' — dieselbe §3.4a-Uhr. |
| B02 | EP-16 | minor | Ist eine Pattern-Bindung veränderlich? | 4.7 auf der Werte-Uhr | 'pattern binding is immutable in 5.0' (A3 #23). |
| B02 | COL-15 | minor | [e] * n mit einem geteilten Element | 4.7 an der AUFRUFSTELLE, nach COL-34 | '[seed] * n shares one struct across n slots — 5.0 copies (arrayOf)'; arrayFilled im selben Zug. |
| B03 | F7 | minor | Werfender `defer` auf Rückgabe- und Durchfallpfad | laeuft (SEM0110) | SEM0110 steht schon an jeder Stelle; retiriert MIT F7-D: 'a defer must not throw; move the throwing call out of the defer' wird 5.0 Fehler. |
| B03 | F12 | minor | Was ist werfbar: nur Klassen? | 4.7 als Fehler, ohne Uhr | Geworfenes Enum ist heute ICE, nie gueltig gewesen: 'only a class can be thrown'. |
| B03 | F19 | minor | Deckungsrechnung auf Instanzen oder Definitionen? | 4.7 mit F4 | 'this call may throw E (instantiated clause) — catch it or declare it'; Fehler 5.0. r06 stirbt heute zur Laufzeit. |
| B03 | F20 | minor | Welche Ausnahme erreicht den catch, wenn ein defer wirft, während eine fliegt? | laeuft (SEM0110) | Dieselbe Uhr wie F7; ein Entscheid. |
| B03 | F23 | minor | Was sieht ein Host, wenn eine Lyric-Ausnahme entkommt? | 4.7 Host-Doku + Schalter | Hostseitig: 'uncaught Lyric exception escaped Execute — 5.0 surfaces it as ScriptException with code'; HostOptions-Schalter, Default 5.0 umgedreht. |
| B03 | F35 | minor | Verdeckte catch-Klausel: Fehler, Warnung oder nichts? | 4.7 | 'catch (e: E) is hidden by the earlier catch for Base — unreachable'; Fehler 5.0; SEM0035 geht darin auf. |
| B03 | OP-16 | minor | Bleibt never auf Rückgabe- und throws-Position beschränkt? | keine Uhr: Bugfix | never in Wertposition war nie gueltig (CLI0020 heute). |
| B03 | SL-04 | **major** | Bleiben drei Namen pro fehlbarer Operation? | erst nach try-Ausdruck; dann @Deprecated{until=6.0} | Auf die fuenf OrErr-Namen: 'hexDecodeOrErr — use try hexDecode'; stille Zwillinge NUR nach dem F18-Entscheid (Widerspruch W7). |
| B04 | G02 | **major** | Bekommt v5 einen Self-Typ? | 4.8, NUR mit lyrfix; §12.5 greift nicht | 'Hashable<Coord> / Ordered<Coord>: write Hashable (Self) — 5.0 requires it'; unter D bleibt Equatable<T>; lyrfix meldet Equatable<X>, X≠Self, als nicht migrierbar. |
| B04 | G10 | minor | Was die Constraint-Liste akzeptieren darf: Interface, Zirkel, Wiederholung | 4.7 §12.5 mit neuem Code | 'constraint list entry `int` is not an interface / repeated / circular — error in 5.0' (nicht SEM0078 dehnen). |
| B04 | G20 | minor | Generische extend-Ziele: Arrays, Tupel, opaque | 4.7 = IF-11 | 'extend on array/tuple/opaque target is ignored today — error in 5.0'. |
| B04 | G24 | minor | Ist das :: am Typparameter dieselbe Liste wie am Typ? | 4.7 §12.5 | '`::` on a type parameter accepts only constraints — this entry is refused in 5.0'. |
| B04 | G32 | **major** | Mehrfachkonformanz gegen Self | = G02 | Unter B keine Uhr; unter A Bruchliste + Spec-Zeile + lyrfix-Meldung. |
| B04 | IF-04 | **major** | Braucht v5 einen Self-Typ? | C 4.7 additiv; D 4.8 Defektwarnung | 'Equatable<Coord>: write Equatable (Self) — 5.0 requires it' NUR falls D; unter G02-D bleibt Equatable<T> und die Uhr gilt Hashable/Ordered. |
| B04 | IF-07 | minor | Bedingte Konformanz und generische extend-Ziele | 4.7 B additiv; Uhr = IF-11 | Deklarationsseitige bedingte Konformanz existiert; extend-Form kommt dazu. |
| B04 | IF-11 | minor | Welche Ziele darf extend haben? | 4.7 Defektwarnung | 'extend target int[] / opaque / interface is silently ignored today — error in 5.0'. |
| B04 | IF-22 | minor | Was bedeutet eine Constraint fuer ein nicht-nominales Typargument? | 4.7 Bruchwarnung, 5.0 Fehler | 'constraint on a non-nominal type argument (int[], (int,int)) is not checked — refused in 5.0'. |
| B04 | SL-09 | **major** | Ein Aufrufstil, oder geschriebene Regel für zwei? | 4.7 @Deprecated{until=5.0} mit replacement | 'listContains(l, x) — use l.contains(x)'; std.result/std.option sofort auf Methoden. |
| B05 | G35 | minor | Sind Constraints Teil der Anwendbarkeit eines Überladungskandidaten? (neu) | 4.7 = OVL-02 | 'constraint-violating candidate excluded in 5.0'; Regel-PR mit since-Gate. |
| B05 | IF-13 | minor | Eine Aufloesungsregel fuer drei Empfaengerpfade | 4.7 Migrationsuhr, VORHER Suite-Fall fuer 4.6-Regel | Zwei Notes: 'vtable row of S.m resolves to the default today; 5.0 resolves to extension m (spec §5.4)'. |
| B05 | IF-15 | minor | Default-Argumente auf Interface-Membern: welche Politik? | 4.7 Defektwarnung | 'default argument repeated on the implementation — the interface member owns it in 5.0'. |
| B05 | IF-34 | minor | Zwei Interfaces mit gleichnamigen Defaults: Aufloesung an der Deklaration oder Benutzung? | 4.7 Defektwarnung an der ::-Liste | 'X and Y both default m — T must override m in 5.0'. |
| B05 | IF-40 | minor | Ist ein Default-Argument Teil der Konformanzsignatur? | 4.7 = IF-15 | Signaturfrage, derselbe Text. |
| B05 | OVL-01 | minor | Konversionsrang oder nur exakt/nicht exakt? | 4.7 §12.5, erst nach Zaehlung | 'overload choice changes in 5.0: T→?T ranks below literal adaptation — call picks f(int) instead of f(?int)'. |
| B05 | OVL-02 | minor | Filtern Constraints die Kandidatenmenge? | 4.7 §12.5 nach OVL-20 | 'candidate g<T :: [X]> violates its constraint; 5.0 excludes it before ranking — call resolves to h'. |
| B05 | OVL-03 | minor | Filtern explizit geschriebene Typargumente? | 4.7 §12.5 | 'explicit type arguments filter candidates in 5.0; this call changes from f<T> to g<T,U>'. |
| B05 | OVL-04 | **major** | Wird der Empfaengertyp vor dem Fit substituiert? | 4.7 §12.5, nach Zaehlung | 'receiver type is substituted before ranking in 5.0 — choice changes'. v5-Frage, kein Patch. |
| B05 | OVL-07 | **major** | Eigener Member vs. Extension: Prioritaet oder Tiebreak? | 4.7 §12.5 | 'own member `m` shadows extension `m` — in 5.0 the member wins' (Kotlin shadowed), auch gegen std.core (OVL-40). |
| B05 | OVL-08 | minor | Gehoeren Default-Methoden in die Kandidatenmenge? | 4.7 mit OVL-07 | 'default method `m` is outranked by extension `m` today; 5.0 ranks it as member'. |
| B05 | OVL-09 | **major** | Duerfen Interface-Member ueberladen? | keine Uhr moeglich: Format | Slot-Identitaet Name+Signatur ist Formatbruch; Pin overloaded_member_refused retiriert MIT der Regel. |
| B05 | OVL-10 | minor | Verschmelzen Importe Ueberladungsmengen oder verdecken sie? | 4.7 §12.5 | 'imports merge overload sets in 5.0 — `f` from a and b becomes one set; this call is ambiguous'. |
| B05 | OVL-11 | minor | Duplikat an der Deklaration oder am Aufruf? | keine Uhr: Diagnoseort | Doppelmeldung 4.7 abstellen; Code bleibt SEM0044. |
| B05 | OVL-13 | minor | Nehmen Lambdas an der Auswahl teil? | E sofort (4.7); 4.8 §12.5 fuer B+D | 'a lambda argument participates in overload choice in 5.0 — this call changes to f(fn(int))'. |
| B05 | OVL-15 | minor | Ein Auswahlalgorithmus oder sechs? | 4.8 §12.5 mit since-Gate | '`as` over Into<T> picks by target type; 5.0 has one rule for value position, `as` and T.parse'. |
| B05 | OVL-18 | **major** | Was muss stehen, bevor die std ihre Typsuffixe aufgibt? | 4.8 @Deprecated{until=5.0} | 'absInt is deprecated: use abs (overloaded)'. Vorher OVL-01/05/12/25/26/40 — der Lackmustest. |
| B05 | OVL-19 | **major** | Filtert Sichtbarkeit die Kandidatenmenge? (Member-Zweig) | 4.7 §12.5 (PLAN.md:367) | 'non-pub member reached through a member path — private in 5.0'; Text schliesst den Importweg (OVL-29) ein. |
| B05 | OVL-21 | minor | Trennen mut, static oder throws zwei Ueberladungen? | 4.7 | 'overloads that differ only in `mut`/`throws` form one set in 5.0 — rename one'. |
| B05 | OVL-23 | minor | Regel 3: Zaehler oder Flag? | 4.7 §12.5 (selten) | '5.0 prefers the candidate with fewer omitted defaults — this call changes'. |
| B05 | OVL-26 | **major** | Duerfen extern und native Deklarationen Teil einer Ueberladungsmenge sein? | 4.7 §12.5, nur mit OVL-18 | 'extern/native `f` joins the overload set in 5.0 — call is ambiguous'. |
| B05 | OVL-28 | **major** | Was gilt, wenn die Substitution zwei Ueberladungen gleich macht? | 4.7 §12.5 (B), C in 5.0 | 'after substitution f<int>(int) and f(int) coincide — ambiguous in 5.0'; OVL-34 billige Fassung vorher. |
| B05 | OVL-29 | minor | Filtert Sichtbarkeit beim SELEKTIVEN Import, und darf der Importstil das Ergebnis aendern? (neu) | 4.7 gewoehnliche Warnung, 4.8 Bugfix | 'selective import of non-pub `f` — refused from 4.8 (§4.2)'. Kein §12.5: pub auf freien Funktionen ist heute Vertrag. |
| B05 | OVL-30 | minor | Welche Argumentformen brauchen den erwarteten Typ, und wie stimmen sie im Probelauf ab? (neu) | 4.8 | 'literal/lambda/null argument: applicability uses the expected type in 5.0' — Meldung nennt die Form, nie <error>. |
| B05 | OVL-31 | minor | Ist v as T ueber Into<T> die in-Sprache-Ausnahme zur Regel 'nie ueber den Rueckgabetyp'? (neu) | 4.8 §12.5 | '`v as T` with several Into<T> conformances: 5.0 chooses by target as named exception in §4.3a'. |
| B05 | OVL-34 | minor | Wann sind zwei generische Parameterlisten gleich? (neu) | 4.7 | 'generic parameter lists <T> and <U> are the same list — duplicate declaration in 5.0'. |
| B05 | OVL-40 | minor | Stehen std.core-Extensions ohne Import in jeder Kandidatenmenge, und gibt es auf Primitiven ueberhaupt eine Menge? (neu) | 4.7 Bugfix + gewoehnliche Warnung | 'extension on primitive `int` shadows/duplicates X'; Pin auf int; std.core-Antwort in OVL-07. |
| B05 | FN04 | **major** | Defaults an Interface-Membern | Befund 2 sofort; Uhr via FN36 | Ein weggelassener Default ueber einen Interface-Wert erzeugt heute ein kaputtes Modul — 4.7 Fehler. Reihenfolge FN36 → FN02-B → FN04-B. |
| B05 | FN23 | minor | params nach einem Default | 4.7 (B bis FN01) | '`params` after a defaulted parameter is unreachable — refused in 5.0'. |
| B05 | FN36 | **major** | Sind Default-Argumente Teil des Interface-Vertrags | 4.7 §12.5, nach FN46-Zaehlung | 'implementation declares its own default for interface member m — in 5.0 the interface default is the contract'. |
| B05 | SK-06 | minor | Bleibt Literal-Adaption eine Liste von Positionen? | 4.7 B additiv; C/D keine Uhr | Mehr Adaptionspositionen sind additiv; der v5-Wechsel zu comptime_int aendert nur Fehler zu Erfolg. |
| B05 | SK-23 | minor | Wie vertraegt sich Literal-Adaption mit Generics-Inferenz (Rueckfluss aus dem Bindungsziel)? | 4.8 mit SK-06 | 'literal adapts to the binding target in 5.0 (comptime_int) — inference result changes here'. |
| B05 | OP-13 | **major** | Wie wählt as sein Ziel bei mehreren Konversionen? | 4.8 nach OP-29 (OnInterface) | '`as` finds two conversions (Into<A>, Into<B>) — 5.0 picks by context; annotate'; Deprecation auf Into ohne OP-29 unschreibbar. |
| B05 | SL-06 | minor | Typsuffixe oder Überladung? | 4.7 Migrationswarnung; 4.8 @Deprecated (OVL-18) | 'int literal adapts to float here — in 5.0 abs(2) selects the int overload (result type changes)'. |
| B06 | IF-27 | minor | Kohaerenz fuer inhaerente extend-Bloecke | 4.7 | 'inherent extend of foreign type T outside its module — coherence rule in 5.0'. |
| B06 | IF-31 | minor | Kann eine Konformanz privat sein? | 4.7 vor MOD-05 | 'conformance of private type is public / pub on conformance has no effect'. |
| B06 | IF-33 | minor | Doppelte Konformanzdeklaration im selben Modul: Fehler, oder welcher Body gewinnt? | 4.7 Defektwarnung | 'T conforms to X twice with two bodies — 5.0 refuses (one conformance)'. |
| B06 | IF-44 | minor | Was bedeutet pub am Interface und an seinen Membern? | 4.7 mit MOD-05 | 'pub on an interface member has no effect / becomes private in 5.0'. |
| B06 | MOD-04 | **major** | pub auf dem qualifizierten Weg durchsetzen | 4.7, NUR gebuendelt mit MOD-25 + MOD-41 | 'lib.x reaches a non-pub name by the qualified path — private in 5.0'. Ohne MOD-41 trifft die Uhr als erstes den eigenen Testroot; ohne MOD-25 warnt sie nur an einem Teil der Stellen. |
| B06 | MOD-05 | **major** | Member-Sichtbarkeit: Default privat? | 4.7, zwei Texte | (1) 'method/static let without pub becomes private in 5.0' (stiller Bedeutungswechsel), (2) Feld-Sichtbarkeit ist neue Grammatik (additiv, keine Uhr). |
| B06 | MOD-06 | minor | Privater Typ in oeffentlicher Signatur | 4.7 mit MOD-04/05 | 'private type T in the public signature of f — error in 5.0'. |
| B06 | MOD-11 | **major** | Header und Name der Entry-Datei | 4.7 E+B sofort; 4.8 Uhr | Sofort: Header-Hijack von std.* und Doppelladung refusen. Uhr (dieselbe wie MOD-34/MOD-45): 'entry file is module `main` today; 5.0 names it after the file — bytecode names main.* become <file>.*'. |
| B06 | MOD-12 | **major** | Ist pub die Host-Oberflaeche, welche pub sind Wurzeln? | 4.7 Diagnose; Uhr mit BP-09 | Sofort: 'pub method X was cut by reachability'. Die Export-Regel selbst haengt an BP-09 (Interface-Sektion). |
| B06 | MOD-15 | minor | pub an Stellen, an denen es nichts bedeutet | 4.7 mit MOD-05 | 'pub has no effect here — error in 5.0'. |
| B06 | MOD-19 | minor | Gross-/Kleinschreibung im Modulpfad | 4.7 C, Fehler 5.0 | 'module path `Foo.bar` differs from the file name in case — refused in 5.0'. |
| B06 | MOD-21 | minor | pub an einer Extension-Methode nach MOD-05 | 4.7 mit MOD-05 | 'pub on an extension method: takes effect in 5.0 (default private)'. |
| B06 | MOD-28 | minor | Wo darf ein import stehen? | 4.7 | 'import after a declaration — 5.0 requires imports first'. |
| B06 | MOD-34 | **major** | Woher bekommt eine Bibliothek ihren Modulnamen ohne Header? (neu) | 4.7 Uebergangsdiagnose, 4.8 Uhr | 'library file without module header is loaded as `main`; 5.0 derives the name from lyric.json name/path'. Dieselbe Uhr wie MOD-11/MOD-45. |
| B06 | MOD-37 | **major** | Was kostet das Modulsystem pro Kompilation, gibt es getrennte Uebersetzung? (neu) | keine Uhr: Architektur | Bedingung an BP-09; kein Nutzercode betroffen. |
| B06 | MOD-44 | minor | Was bedeutet pub fuer generische Instanzen ueber Modulgrenzen? (neu) | keine Uhr: 4.7 Bugfix | Instanz mit privatem Typargument ist heute 'defect in the compiler'; A repariert, §4.6-Satz. |
| B06 | MOD-45 | **major** | Wie migriert ein Programm, dessen Bytecode-Namen main.* an einen Host verkauft sind? (neu) | 4.8 (mit MOD-11) | Hostseitig: 'OnFunctions("main.x"): bytecode names change in 5.0; `lyrvm info` prints the mapping'. Eine Uhr fuer MOD-11/MOD-34/MOD-45. |
| B07 | BP-02 | minor | Hat ein Projekt eine Version? | 4.7 optional (B); 4.8 Uhr fuer C | 'library has no version — required for packaged libraries in 5.0'. |
| B07 | BP-03 | minor | Wem gehört das Segment: dem Konsumenten oder der Bibliothek? | 4.7 Warnung; Fehler erst eine Release NACH BP-04 | 'segment json is claimed by two dependencies / not owned by dependency root'; ohne BP-04-Umbenenner keine Loesung fuer den Nutzer. |
| B07 | BP-10 | **major** | Was ist ein Build-Skript: Programm, Sandkasten, Graph oder Daten? | 4.7 D + Warnung | 'build() writes files / starts processes — in 5.0 build() is sandboxed; declare generated(...)'; --help sagt laut, dass es das Skript startet. |
| B07 | BP-12 | minor | Zwei feste Profile oder benannte Profile? | 4.7 additiv | 'LYRIC_PROFILE=x names no profile' warnt; benannte Profile brechen nichts. |
| B07 | BP-14 | minor | Workspaces: ein Repository, mehrere Projekte | 4.7 warnbar ohne Designentscheid | 'entry src/other/main.lyr lies outside this project's sourceRoot — error in 5.0'. |
| B07 | BP-15 | minor | Artefakte: Kollisionen, --only, clean, die doppelte Ableitung | 4.7 | 'directory name 9bad-name is not a module name — set `name`' (5.0 lehnt ab); Artefakt-Kollisionen nennen den gemeinsamen Pfad. |
| B07 | BP-20 | minor | Werden Diagnosen aus fremdem Quelltext gedämpft? (neu) | keine Uhr: Ruecknahme | Fremdes @Deprecated bricht den Bau nicht mehr (D) — Severity-Aenderung, dokumentiert. |
| B07 | BP-22 | minor | Gilt 'die Wurzel entscheidet' für alle Segmentquellen? (neu) | 4.7 D (Hinweiszeile); 4.8 Uhr fuer B | 'nativeRoots of dependency X are overridden by the root'; weggepinntes Projekt traegt nichts bei. |
| B07 | BP-23 | minor | Wo endet ein Projekt, und wem gehört out/? (neu) | 4.7 | 'out/ is placed outside the project directory — 5.0 puts it next to lyric.json'. |
| B07 | SL-29 | minor | std.build beim Kompilieren ablehnen: woran erkennt der Compiler ein Build-Skript? | 4.7 | 'std.build imported outside a build compilation — refused in 5.0' (Flag pro Kompilat, nicht pro Datei). |
| B08 | G33 | **major** | .lyrbc-Kompatibilität, wenn G02 Interface-Signaturen ändert | keine Uhr: Messung vor G02 | 4.x-lyrbc mit Equatable<X> unter geaendertem std.core laden — Ergebnis entscheidet Formatzusage. |
| B08 | IF-21 | **major** | Wer emittiert die Impls-Zeile bei getrennten Modulen? | keine Uhr: Format/Paket | Mit IF-01 und BP-09; kein Nutzercode. |
| B08 | META-01 | **major** | Attributnamen sind unqualifiziert und kollidieren still | 4.7 lyrc + Host-Schalter (META-36) | 'attribute `Test` resolved unqualified; 5.0 records `std.test.Test` — hosts matching by short name must switch'. |
| B08 | META-02 | **major** | Welche Ziele nimmt ein Attribut in v5? | keine Uhr: Formatrunde | Additiv im Format; ein Modul mit neuen Zielkindern ist fuer 4.x-Hosts unladbar — CHANGELOG + Ladefehler mit Versionsnennung. |
| B08 | META-04 | **major** | Darf ein Attribut zweimal auf einem Ziel stehen? | keine Uhr: Formatrunde | Repeatable per Marker; alte Hosts lesen 'same triple twice' als Fehler — mit META-02 in EINER Formatrunde. |
| B08 | META-05 | minor | Was darf in einer Attributzeile stehen? | keine Uhr: additiv | Arrays in Attributzeilen (B); nur Module, die sie benutzen, brauchen den neuen Reader. |
| B08 | META-08 | minor | `comptime` als echtes Schlüsselwort | 4.7, braucht META-22 | '`comptime` is a reserved word in 5.0 — rename this identifier'. |
| B08 | META-24 | **major** | Wie setzt man eine Deprecation-Uhr auf ein Interface, einen Typalias, ein globales `let` oder einen `extend`-Block? | keine Uhr: Formatrunde mit META-02 | OnInterface/OnTypeAlias sind neue Zielkinder (Format-Minor); bis dahin kann ein Interface KEINE Deprecation tragen — jede Uhr auf ein Interface (META-03, OP-13) wartet hierauf. |
| B08 | META-26 | minor | Ist die Feldreihenfolge eines Attribut-Structs Teil seines öffentlichen Vertrags? | keine Uhr vor 5.0 | B+E sofort (Doku fuer SDK- und Host-Autor); D (Namens- statt Positionszusage) faellt mit META-21 in 5.0 — dann Uhr auf WithArg<T>. |
| B08 | META-27 | minor | Darf `--release` Attributzeilen entfernen? | keine Uhr: E sofort | --release entfernt nichts; Messung zuerst. |
| B08 | META-31 | minor | Welchen Typ liefert `embed()`, woran hängt `embedBytes()`? | keine Uhr: additiv | embed() neu; embedBytes erst mit META-07 B. |
| B08 | META-36 | **major** | Wie erreicht die META-01-Migration einen Host, der nicht neu übersetzt wird? | 4.7 Host-Schalter | HostOptions.AttributeNames = Short / Qualified, Default Short in 4.x, Qualified in 5.0; lyrtest matcht ab 5.0 strikt auf std.test.Test. |
| B08 | OVL-14 | **major** | Welches Manglingschema, und wie ruft ein Host eine Ueberladung? | keine Uhr: Format | B' 4.x: Anzeige des gemangelten Namens als 'nicht stabil'; B mit 5.0-Format. |
| B08 | OVL-36 | **major** | Welche Oberflaechen zeigen den gemangelten Namen, und wer ausser Lyrvm liest ihn? (neu) | keine Uhr: §11-Satz | 'the full mangled name is not a stable contract' sofort; B mit OVL-14 in 5.0. |
| B08 | MOD-31 | minor | Ist der Modulpfad im Bytecode-Namensschema eindeutig? (neu) | keine Uhr: 4.7 Bugfix | Compilerdefekt fuer legale Programme (Modul a.b vs Typ b in a). Escaping im Emitter, Spec-Satz dazu. |
| B08 | B1 | minor | Typisiert der Reader den Instruktionsstrom? | 4.7 lyrvm verify --strict + CI; 4.8 Ladewarnung | 'module fails strict verification (unchecked field access / uninit slot) — refused by the 5.0 reader'. Spec-PR zuerst. |
| B08 | B13 | **major** | Freie Blocksprünge oder strukturierter Kontrollfluss? | keine Uhr: A bleibt | Freie Blockspruenge bleiben; Frage geschlossen. |
| B09 | L1 | minor | Kostet ein Array weiter 16 Byte pro Element? | keine Compiler-Uhr; 4.7 @Deprecated auf elementtyp-agnostische Natives | Gepackte Arrays (C) sind 5.0-Material; K9: FFI-Natives, die T[] roh lesen, bekommen 4.7 eine Uhr. |
| B09 | B5 | minor | Bleibt der JIT opt-in? | 4.7 | release schaltet den JIT zusaetzlich, einmalige Meldung 'release profile compiles hot functions; --no-jit keeps the interpreter'; Budget/Debugger ⇒ interpretiert. |
| B09 | B22 | minor | Wie zählt kompilierter Code ein Ausführungsbudget? | keine Uhr: ADR + since-Gate | Budget-Einheit im kompilierten Code aendert eine dokumentierte Zusage; Host-Verhalten, kein Quelltext. |
| B10 | FFI-F2 | minor | Eine Host-Grenze oder vier — und eine Fehlerregel oder vier? | 4.7 HostOptions.StrictMarshalling (aus) | 'value crossed the host boundary by silent fallback (AsI64) — 5.0: HostError'; 5.0 Default an. |
| B10 | FFI-F5 | minor | Wie kommt ein Host-Fehler in die Sprache? | 4.7 lyrc-Warnung, Ratchet erst nach FFI-F25 | 'extern without throws: host exceptions abort — declare `throws HostError` for 5.0'. Nur lyrc-Warnung, weil PAR0042 jedes Attribut auf extern verbietet. |
| B10 | FFI-F9 | minor | Die Export-Richtung: gibt es sie, und ist sie typisiert? | 4.7 Schalter, 5.0 Default | HostOptions.StrictMarshalling: 'export argument not marshallable — 5.0 refuses'. Es gibt kein Host-Log; der Schalter ist die Uhr. |
| B10 | FFI-F13 | minor | Wann wird gebunden, und was heißt das für AOT? | 4.7 lyrc | 'extern X is declared but never called: pruned today, bound at load in 5.0'. Aendert emittierte Bytes — deshalb angekuendigt, nicht still. |
| B10 | FFI-F20 | minor | Was ist ein `char` an der Grenze, und wer prüft die EINGEHENDE Richtung? | keine Uhr: direkt 4.7 | Wert ist heute schon kaputt (Surrogat-char). Laufzeitpanik in 4.7 ist ehrlicher als eine Warnung an derselben Stelle. |
| B10 | FFI-F21 | minor | Was ist ein Lyric-`string` auf dem Draht — Folge von Unicode-Skalaren oder von UTF-16-Einheiten? | keine Uhr: direkt 4.7 | Unpaarige Surrogathaelfte stuerzt heute ein Stdlib-Native ab (Exit 127). Pruefung an der Grenze sofort; HostError sobald FFI-F5 steht. |
| B10 | FFI-F24 | minor | Was passiert bei Reentranz Skript → Host → Skript? | keine Uhr: direkt 4.7 | Reentranz umgeht MaxCallDepth und stirbt per CLR-StackOverflow. Tiefe zaehlen ist Bugfix, keine Regelaenderung. |
| B10 | FFI-F26 | minor | Was gilt, wenn ein Host ein `dotnet:`-Symbol überschreibt, das das Modul ÜBERLADEN hat? | 4.7 additiv, Alt-API warnt | 'RegisterNative by name overrides an overloaded symbol — register with signature; name-only removed in 5.0'. |
| B10 | FFI-F27 | minor | Sieht ein Konsument VOR dem Bauen, dass eine Abhängigkeit `hostAccess` verlangt? | 4.7 lyric info (B), 4.8 Manifest-Uhr | 'dependency X needs hostAccess but the manifest does not declare it — 5.0 requires the key'. |
| B10 | FFI-F33 | minor | Braucht `hostAccess` selbst eine Bestätigung? | 4.7 CLI-Uhr | lyrvm: '--grant host without --allow-host-bypass: in 5.0 this is an error'. Braucht FFI-F25 nicht. |
| B10 | NL32 | minor | Scheduler hinter osAccess: darf ein sandboxter Gast Tasks benutzen? | keine Uhr: additiv | Task-Kern ohne osAccess ist ein neues Modul; osAccess-Teil bleibt. |
| B10 | S24 | minor | Wie sieht ein String an der Host-/FFI-Grenze aus? | keine Compiler-Uhr | = FFI-F21 (HostError sobald FFI-F5); HostOptions.StrictMarshalling. |
| B10 | CLI-16 | minor | Capabilities auf der Kommandozeile | 4.7 nach CLI-29 | '`fileAccess` is an alias of `file` — removed in 5.0'; --deny nur mit CLI-36. |
| B10 | CLI-17 | minor | Darf `lyric build` fremden Code ausfuehren? | 4.7 | 'build.lyr runs with every capability; 5.0 grants file,process by default — declare buildCapabilities'. |
| B10 | CLI-35 | minor | Wie deklariert ein Buildskript seinen Rechtebedarf? | 4.7 mit CLI-17 | 'build.lyr uses capability net but declares none — 5.0 fails at the requesting line'. |
| B10 | T23 | minor | Unter welchen Capabilities läuft ein Test? | 4.7 --grant (B); 4.8 Uhr fuer C | 'test uses capability file not declared in lyric.json capabilities — 5.0 runs tests with the manifest set'. |
| B11 | NL1 | **major** | Liefert spawn ein Handle, und hat eine Koroutine einen Ergebnistyp? | 4.7 sobald Handle existiert | 'result of spawn is discarded — the task is never joined'; Konformanzfall since 5.0.0. |
| B11 | NL2 | minor | Wohin geht die Ausnahme einer Task? | 4.7 mit NL1 | 'task body throws E but nobody joins it — the error is lost' (bis Task<R> throws E existiert). |
| B11 | NL3 | **major** | Die fehlende Warteform: auf eine Bedingung warten | keine Uhr: additiv | Wait.Signal(Waker) neu; interrupt() bekommt Doku-Satz 'not a general waker — swallows Ctrl+C' (NL33). |
| B11 | NL5 | **major** | select und Timeout: darf eine Task in mehreren Spalten parken? | keine Uhr: additiv | Wait.Any neu; Spec-Satz zu verlierenden Armen (NL25) vorher. |
| B11 | NL6 | **major** | Abbruch | keine Uhr: additiv nach NL19 | cancel() neu; Spec-Satz 'catch (e: Exception) does not catch Cancelled'. |
| B11 | NL7 | minor | Strukturierte Nebenlaeufigkeit | 4.7 | 'run() called from inside a task — nested schedulers are refused in 5.0 (structured concurrency)'. |
| B11 | NL10 | minor | Blockierende Natives | 4.7 @Deprecated | 'std.os.sleep blocks the whole VM — use task.sleep'; DNS ueber Pool-Thread, file nach NL22. |
| B11 | NL14 | **major** | Wartegruende: rohe ints und ein geschlossenes Enum | 4.7 @Deprecated auf int-Form | 'Wait.Readable(fd: int) — use the Descriptor type'; @NonExhaustive (META-03) ist Vorbedingung. |
| B11 | NL18 | minor | Hat eine Koroutine bzw. eine Task eine Identitaet? | 4.7 | 'the same coroutine is spawned twice in this body' (Identitaet). |
| B12 | L11 | minor | Wie weit reicht deterministische Numerik? | 4.7 Doku (A); 4.8 @Deprecated fuer std.random (E) | 'random() without seed — use a seeded generator (5.0)'. |
| B12 | L14 | minor | Hat float32 eine eigene Darstellung? | keine Uhr: A = keine eigene Darstellung | Doku-Satz; Formatierung (SK-18) entscheidet die Ausgabe. |
| B12 | META-37 | minor | Gilt die Determinismus-Zusage für Gleitkomma-Intrinsics? | 4.7 Text (A); 4.8 Warnung falls B | 'comptime evaluates sin(): the result depends on the compiling machine — 5.0 refuses transcendental intrinsics at compile time'. |
| B12 | SK-04 | **major** | Ist char eine Zahl? | 4.7, braucht SK-24 (@Allow) | 'arithmetic on `char` — write `c as int`; refused in 5.0'. |
| B12 | SK-05 | minor | Adaptiert ein Ganzzahlliteral an char? | 4.7 mit SK-04 | 'integer literal adapts to `char` — write `65 as char` or a char literal; refused in 5.0'. |
| B12 | SK-08 | minor | Duerfen Literale still runden? | 4.7 | 'float literal 0.1f32 is not exactly representable / overflows — refused in 5.0 (Go rule)'. |
| B12 | SK-12 | minor | Bleibt as das Wort fuer vier verschiedene Dinge? | 4.7 nach SK-29/30 | '`x as int8` truncates — use toInt8Truncating/Clamping in 5.0'; Into behaelt `as` (B+C+D). |
| B12 | SK-13 | **major** | Darf eine Konstante still ueberlaufen? | 4.7 (B), Fehler 5.0 (A) | 'constant expression overflows int8 — error in 5.0' — mit SK-35 ein Posten. |
| B12 | SK-17 | minor | Was bedeutet unaeres - auf einem vorzeichenlosen Typ? | 4.7 | 'unary minus on unsigned wraps — refused in 5.0; write `0 - x` or convert'. |
| B12 | SK-20 | minor | Was antwortet Ordered<float> auf NaN, und was Hashable<float>? | keine Uhr: Bug sofort, Regel 5.0 | Ordered<float>.compare widerspricht Equatable heute; 4.7 Bugmeldung, 5.0 Normalisierung (SK-38) — Laufzeitverhalten, CHANGELOG. |
| B12 | SK-28 | minor | Muss der Wert eines suffigierten Literals in seine Suffix-Breite passen? | 4.7 | '200i8 does not fit int8 (today wraps to -56) — error in 5.0'; 300i8 'defect in the compiler' sofort in den Sweep. |
| B12 | SK-29 | minor | Was ist float as char? | keine Uhr: Bug | float as char liefert heute 0 fuer jeden Wert; 4.7 Fehler (C), nie gueltig gewesen. |
| B12 | SK-35 | **major** | Werden konstant faltbare Paniken zur Compile-Zeit gemeldet? | 4.7 Warnung, Fehler 5.0 | 'this constant expression always panics (1/0) — error in 5.0'; intMin / -1 ausdruecklich nicht (wrappt per §3.2). |
| B12 | SK-38 | minor | Was tun -0.0 und NaN-Varianten im Equatable/Hashable/Ordered-Vertrag? | keine Compiler-Uhr: CHANGELOG | hash/compare von -0.0/NaN normalisieren ist Laufzeitverhalten; Doku 4.7, Verhalten 5.0, Konformanzfall since 5.0. |
| B12 | OP-44 | minor | Wird ein sicher panikender konstanter Ausdruck beim Kompilieren gemeldet? | 4.7 = SK-35 | 'constant expression always panics — error in 5.0'. |
| B12 | EP-23 | minor | Ist ein leeres, umgedrehtes oder überlappendes Range-Pattern diagnostizierbar? | 4.7 | 'range pattern 10..1 never matches — error in 5.0'; Ordnungsregel in der Spec. |
| B12 | COL-04 | minor | Welchen Elementtyp darf eine Range haben? (Float-Bug) | 4.7 Warnung, 5.0 Fehler | 'float range truncates its bounds silently — only integer ranges in 5.0'. |
| B13 | FN26 | minor | Ist Zuweisung ein Ausdruck | 4.7 = OP-3-Uhr | 'assignment used as a value — void in 5.0'; 4.7 nur in Bedingung, 4.8 ueberall in Wertposition. |
| B13 | OP-3 | **major** | Ist eine Zuweisung ein Ausdruck? | 4.7 nur Bedingung (D); 4.8 Wertposition | 'assignment in a condition — did you mean ==? (void in 5.0)'; dann 'assignment used as a value — void in 5.0'; retiriert assignment_chains_right. |
| B13 | OP-4 | minor | Was passiert bei a < b < c? | 4.7 | 'a < b < c chains comparisons — non-associative in 5.0, parenthesize'. |
| B13 | OP-6 | **major** | Welche Operator-Interfaces kommen dazu — und ist das eine v5-Frage? | 4.7-Runde additiv; Index<K,V> = COL-07-Uhr | Neg/Rem/Contains brauchen keine Uhr; nur Index<K,V> ersetzt. |
| B13 | OP-7 | minor | Woher kommt +1? — Aktenstand: entschieden, aus Add | keine Uhr: 4.7 wie gesetzt | Inkrement aus Add; Ausdruckswert ist OP-33. |
| B13 | OP-33 | **major** | ++ bleibt (OP-7 A): Ausdruck oder Statement? | 4.7 = OP-46-Text | 'x++ used as an expression — a statement since 5.0'. |
| B13 | OP-46 | **major** | Wie sieht Diagnose und Umbau für x++ in AUSDRUCKSposition aus (OP-33 B)? | 4.7 Warnung, 5.0 Fehler | Wortlaut: 'x++ has no value — it is a statement since 5.0: put x += 1 on its own line and read x before it (old value) or after it (new value)'. |
| B14 | META-03 | **major** | `OnMethod` und `@NonExhaustive`: die zwei Anker einlösen oder zurückziehen | 4.7 std-Enums, Regel 5.0 | std-Enums tragen @NonExhaustive ab 4.7: 'match on non-exhaustive enum X has no `_` arm — required in 5.0'. OnMethod: CHANGELOG, faellt im Major. |
| B14 | OPT-01 | minor | Wo lebt die Nicht-Nestungsregel, und welche der drei Antworten gilt? | keine Uhr: dokumentarisch | ??T kompiliert heute nicht; Guide-Zusage (Idempotenz) wird zurueckgenommen, kein Code betroffen. |
| B14 | OPT-13 | minor | Praezedenz von ?? | 4.7 gewoehnliche Warnung + lyrfix | '`??` binds tighter than `<` in 5.0 — parenthesize' (Grammatikposten mit since-Zwilling). |
| B14 | OPT-24 | minor | Was beendet ein Narrowing ausser einer Zuweisung - und bleibt die Staleness-Regel? | 4.7 gewoehnliche Warnung | 'narrowing of `x` ends here because the closure captures it; in 5.0 it survives unless `x` is assigned later'. Ersetzt einen Konformanzfall (spec-first). |
| B14 | EP-01 | minor | Wie wird ein Variantenname im Pattern von einer Bindung unterschieden? | 4.7 Warnung + NameSuggestion sofort | '`Circel` in this pattern is a binding, not the variant Circle — 5.0 resolves bare names by type'; vorher Regel in §7.6 schreiben. |
| B14 | EP-10 | minor | Wie sagt ein Pattern, dass es Felder auslässt? | 4.7 | 'struct pattern omits fields w, h — write `..` in 5.0'; null betroffene Zeilen gemessen. |
| B14 | EP-27 | minor | Array-Patterns: Slice, Trägertyp, Zeugen | keine Compiler-Uhr: Doku + Konformanzfall | Array-Pattern-Rest bindet eine Kopie (C); Traegertyp bleibt T[]. |
| B14 | EP-28 | minor | Was ist die Abbruchgrenze der Erschöpfungsmatrix? | keine Uhr: Sweep | 64er-Schranke miscompiled heute; B sofort: nur in Richtung 'nicht erschoepfend' irren. |
| B14 | EP-31 | minor | Wer darf eine Variante und ihre Felder im Pattern nennen? | 4.7 | 'pattern names private field/variant of a foreign module — refused in 5.0'. |
| B14 | EP-36 | minor | Dürfen Konstanten im Pattern stehen? | 4.7 mit EP-01 | 'bare name refers to constant K — mark it (`let`-Marker for bindings) in 5.0'. |
| B14 | EP-40 | minor | Wo wird die Auflösung bloßer Namen normiert, und gilt sie in Pattern und Ausdruck gleich? | 4.7 mit EP-01 | C sofort: Regel spec-first aufschreiben; A in 5.0 mit derselben Uhr. |
| B14 | EP-44 | minor | Werkzeug-Ergonomie jenseits des Zeugen: Completion und Formatter | keine Uhr: Werkzeug | Completion/Formatter; A ist die billigste Haelfte von EP-01. |
| B15 | FN05 | minor | params: Aliasing und Spread | 4.7 | 'params array is aliased by the callee; 5.0 spread (`*arr`) copies — copy explicitly if you rely on sharing'. |
| B15 | COL-03 | minor | Wird eine Range ein Wert? | 4.7 | 'iter.range(a, b) — ranges are values in 5.0: write a..b'; retiriert range_is_a_loop_head_not_a_value (since 3.3.0). |
| B15 | COL-07 | **major** | Indexierung ueber beliebige Schluessel: m["k"] | 4.7 | 'Indexable<T> is replaced by Index<K,V> / IndexSet<K,V> in 5.0 — implement both'; kleine Oberflaeche (std: List<T>). |
| B15 | COL-08 | **major** | Wo wohnen Iterator, Iterable, Indexable, und stehen sie im Vertrag? | 4.7 — ohne lyrfix Handmigration | 'import std.iter for Iterator: Iterator, Iterable, Indexable live in std.core from 5.0'. |
| B15 | COL-35 | **major** | Soll die std zwei parallele Kettenwelten behalten? | 4.7 @Deprecated{until=5.0} mit replacement | 'mapList(l, f) — use collect(over(l).map(f))'; sortList* bleibt. |
| B15 | COL-40 | minor | Soll LYR-SEM0007 gespalten werden? | keine Uhr: Diagnosecode | D-Noten 4.7 ('has next() but is not an Iterator'); Spaltung in 5.0 mit since-Faellen. |
| B15 | SL-03 | minor | Bekommt Lyric 5 Reader/Writer/Seek? | keine Uhr: additiv | Reader/Writer neu; die Handle-Aenderung ist SL-20. |
| B15 | SL-10 | minor | Modulschnitt und Namensgesetz | 4.7 @Deprecated mit replacement | Nur Namen, deren Suffix die EINGABE beschreibt; slice NICHT deprecaten. |
| B15 | SL-20 | **major** | Handle-Darstellung: opaque=int, Klasse, oder opaque mit Konformanz? | keine Compiler-Uhr: CHANGELOG + Konformanzfall | Handle-Gleichheit wird Referenzidentitaet (A); `f == g` auf Handles ist statisch nicht auffindbar; Native-Signaturen bleiben int (formatneutral). |
| B15 | SL-23 | minor | Text über Bytes: wo liegt die Dekodierschicht? | 4.7 @Deprecated{until=5.0} | 'LineReader — use Utf8Reader(reader).lines()'; Utf8Error mit Offset statt ''. |
| B15 | NL23 | **major** | Was liefern next() und resume, wenn eine Koroutine mit einem Ergebnis endet? | 4.7 Warnung + lyrfix | 'Coroutine<void>.next() answers bool today; 5.0 answers Step<Y,R> — match on Yielded/Returned'; Pins next_on_void_answers_bool und next_on_optional_yield_is_refused retirieren. |
| B15 | NL31 | **major** | Asynchrone Generatoren: wie wartet eine lazily gezogene Sequenz in einer Task? | keine Compiler-Uhr: VM0015-Text (NL43) | Laufzeitfehler heute; 4.7 Meldung nennt beide Typen und den Ausweg; Regelaenderung §10a Regel 2 mit since 5.0.0. |
| B15 | S03 | minor | Gibt es `s[i]` und `s[a..b]`, und nimmt `substring` ein Ende oder eine Laenge? | 4.7 @Deprecated{until=5.0} | 'substring(start, count) — use slice(start, end)'; Bereichsregel aus S35 kommt mit dem neuen Namen. |
| B16 | SL-12 | minor | Wie tief geht Unicode? | = S16 | std.unicode nur im zweiten Ring. |
| B16 | S05 | **major** | Welche Formatsprache? | 4.7 Klasse 3 pro Loch | 'format spec `X` on a signed hole renders the 64-bit pattern today and -FF in 5.0'; '`.2` on a float renders 32 today and 3.14 in 5.0'. Klasse 1 stumm gleich, Klasse 2 lyric fix. |
| B16 | S06 | minor | Wann wird ein Spezifizierer geprueft? | C 4.7 (Laufzeit); 4.8 Warnung fuer A | 'format spec `{x:zz}` is not parseable — compile error in 5.0'. |
| B16 | S07 | **major** | Wie schreibt man Breite und Ausrichtung, und wie richten Zahlen sich aus? | 4.7 mit S05 | 'numeric hole with width aligns left today, right in 5.0 — write `<` to keep'. |
| B16 | S14 | minor | Die Escape-Menge, `\x`, und rohe Bytes | 4.7 | '\x80 above 0x7F is a code point, not a byte — restricted to 0..0x7F in 5.0; write \u{80}'. |
| B16 | S15 | minor | `{{`, `}}` und die f/nicht-f-Asymmetrie | 4.7 | 'lone `}` in an f-string — error in 5.0; write `}}`'. |
| B16 | S16 | minor | Wo lebt Unicode? | 4.7 @Deprecated{until=5.0} | 'isAlpha — use isAsciiAlpha (or std.unicode.isAlphabetic)'; toUpper analog; Unicode-Version der Tabelle in die Spec. |
| B16 | S18 | minor | Bleiben `+` und `*` auf Strings? | 4.8 nur nach S22 | D (Warnung vor `s + s` in Schleifen) erst, wenn das f-String-Lowering nicht mehr dasselbe tut. |
| B16 | S20 | minor | In welcher Einheit zaehlt die Breite eines Format-Spezifizierers? | keine Compiler-Uhr: CHANGELOG | Breiteneinheit UTF-16 → Skalar ist Laufzeitverhalten; Doku 4.7, Wechsel 5.0 mit S07. |
| B17 | CLI-3 | minor | Was tut `lyric <verb> --help`? | keine Uhr: Werkzeugfix 4.7 | Hilfe ist kein Vertrag; F1-Defekte sofort. |
| B17 | CLI-7 | minor | Strenge und Wortlaut beim Optionsparsen | 4.7 | 'option --x is unknown and ignored — error in 5.0'; fehlender Wert bekommt LYR-CLI0002. |
| B17 | CLI-9 | minor | Umgebungsvariablen, Selbstauskunft, und wo eine benutzerweite Vorgabe hingehoert | 4.7 | 'LYRIC_PROFILE=x is not a profile — ignored'; benutzerweite Ebene ist additiv. |
| B17 | CLI-23 | minor | Ist eine Option phasengebunden, und wer dupliziert sie? | 4.7 | '--release is a compile option; `run` ignores it here' (Phasentabelle); F5 Bug sofort. |
| B17 | CLI-24 | **major** | Wem gehoert die Kommandozeile hinter dem Ziel — dem Programm oder der Toolchain? | 4.7 — teuerste CLI-Uhr | Auf JEDER Option rechts vom Ziel: '--jit after the target belongs to the program in 5.0; write it before the target'. Testfall fuer CLI-29. |
| B17 | CLI-28 | minor | Eine Eingabekonvention fuer stdin | 4.7 | `-` wird stdin; lyrfmt --stdin: 'use `-`; --stdin is removed in 5.0'. |
| B17 | CLI-33 | minor | Eine Regel fuer `--` | 4.7 | '`--` ends the option list in 5.0 (POSIX); here it meant X' — in `run` wird `--` vom Pflichttrenner zum Ausweg (CLI-24). |
| B17 | T1 | minor | Woher kommt eine Assertion: N Funktionen oder ein zerlegendes Konstrukt? | 4.7 nach T30, @Deprecated{until=5.0} | 'assertEq(a, b) — use assert(a == b)'; assertClose bleibt; assert nimmt optionale Meldung (Bedingung an T1-B). |
| B17 | T7 | minor | Wer prüft den `@Test`-Vertrag? | 4.7 an der Deklaration | '@Test on a function with parameters violates the attribute contract (OnFunction, no args)'. |
| B17 | T8 | minor | Wie heißt ein Test, und ist der Name eindeutig? | 4.7 Ankuendigung, 4.8 Wechsel | 'test names become path-based (alpha.shared_tests) in 4.8 — same as lyric check'. Reparatur einer Divergenz. |
| B17 | T11 | minor | Wird die Ausgabe eines Tests aufgefangen? | keine Uhr: Ausgabe | 4.7 --capture opt-in; 4.8 Default an mit Hinweis in der Zusammenfassungszeile. |
| B17 | T13 | minor | Zeitlimits | 4.7 opt-in, 5.0 Default | 'default wall-clock limit (10 min) applies from 5.0 — set --timeout to keep'. |
| B17 | T17 | minor | Doc-Tests | 4.7 Warnung ab dem ersten Slice | 'this /// code block would not compile — mark it ```lyr,ignore or fix it'. |
| B17 | T26 | minor | Was ist der Name eines Tests, wenn die Datei einen `module`-Header schreibt? | 4.7 mit T8 | Derselbe Text wie LYR-RES0006: 'loaded as X, declares Y' als Warnung, ab 4.8 Fehler. |
| B17 | T30 | minor | Wie migriert eine bestehende Testsuite auf 5? | 4.7 (Rueckgrat ist @Deprecated{until}) | Zuerst bricht die eigene stdlib-tests-Suite (887 Aufrufe) — gewollt. |
| B17 | T31 | minor | Welche Exit-Codes sind überhaupt frei? | 4.7 Ankuendigung | Exit-Codes koennen nicht warnen; 'no tests collected' bekommt Exit 5 ab 5.0, Kennung ab 4.7 gedruckt; 101 bleibt frei. |
| B18 | E5 | minor | REPL: Replay, persistente VM, oder streichen? | keine Uhr: Werkzeug | 4.x: Klassifikator aus dem Parser, Warnungen rendern, --grant, :help ehrlich; B/C 5.x. |
| B18 | E6 | minor | Formatter: eine Form ohne Optionen — und was der AST verliert | keine Uhr: Formatvertrag (E30) | Kommentar-Haelfte von C aendert die Ausgabe — CHANGELOG, Formatter-Ausgabe gehoert zur Version. |
| B18 | E10 | minor | Profiler: anschließen, umbenennen, oder streichen? | 4.7 Kollision sofort; Alias-Uhr | '--profile is renamed --profile-instructions (LYRIC_PROFILE means build profile) — alias removed in 5.0'. |
| B18 | E16 | minor | Attach an eigenständige Programme: Lauschmodus | 4.8 Hinweis | 'launch attaches through --debug-listen from 5.x; runInTerminal becomes the default form'. |
| B18 | D8 | minor | Gibt es eine Fehlerobergrenze? | 4.7 opt-in, 5.0 Default | '--max-errors defaults to N from 5.0; JSON carries `truncated`'. |
| B18 | D11 | minor | Wie sieht die Ausgabe aus? | keine Uhr: nicht vertraglich | Terminalform; C+D 4.7. |
| B18 | D25 | **major** | Ueberlebt Severity.Info den Major? | 4.7 'reserviert' in §12; 5.0 streichen | Lyric.Core: [Obsolete] auf Severity.Info in 4.7 (Host-API), Entfernung 5.0. |

246 Zeilen. Zählung: 62 major, 184 minor.

**Lesart der Spalte „Uhr startet“ als Fahrplan.** 4.7 trägt die Fundament-Uhren (B01-Apparat, W34/W1-Familie, F7/F19, MOD-04/05 gebündelt, SEM0107–0110 fortgeschrieben) und alle Bugfixes gegen geltende Spec. 4.8 trägt, was ein Werkzeug voraussetzt (G02 nur mit lyrfix, CLI-11 nach F16, OVL-18 nach OVL-01/05/12/25/26/40, IF-04-D nur falls entschieden). „Keine Uhr möglich“ (Format: OVL-09/14/36, IF-21, META-02/04/24; Laufzeit: SK-38, S20, SL-20, L32) ist der Grund, warum diese Posten in den CHANGELOG und in Konformanzfälle mit `since:` gehören — nicht in eine Warnung, die nichts zu warnen hätte.

---

## §5 Sofort entscheidbar — vorweglaufen ohne Sitzung

Kriterium: blockiert nichts, bricht nichts (additiv, Bugfix gegen geltende Spec, oder reine
Text-/Doku-Arbeit), und die Empfehlung im Dossier ist eindeutig. Drei Klassen:

- **[K]** Konformanzverstoß / Bug gegen eine Regel, die schon gilt — Fix mit since-Fall, keine Uhr.
- **[A]** Additiver Minor (neue Diagnose, neues Native, neuer Schalter, Doku), formatneutral.
- **[S]** Spec-first-Textarbeit: einen Satz schreiben, der heute nur im Compiler steht.

Reihenfolge innerhalb einer Zeile = Reihenfolge im Dossier. Was hier steht, ist NICHT aus dem
Block herausgenommen — der Block bestätigt es nur noch.

| Block | Sofort (4.7) | Warum |
|---|---|---|
| B01 | [K] CLI-29 F16/F17 (Warnung zerbricht JSON-Strom) · [K] T33-C (`rstrip` im Runner) · [K] G46 (rote Fälle G18/G21/G37/G38 in die Suite) · [A] D18/D21 `kind`-Feld · [A] D26 Notizart · [A] D27 sechs Regex-Invarianten über 228 Codes · [S] OP-45 (Display-Interpolation `since: 4.6.0` nachziehen) · [S] IF-43 (4.6-Regel §5.4 pinnen) · [S] CLI-30 Grenze „was ist versioniert“ · [A] FFI-F25 (Attribute auf `extern`) · [A] W42/FN46 Zählungen im Baum · [S] NL38/OP-23/OP-39-Tabellen · [A] E9 (Uhren im Editor rendern) | Alles Vorbedingung für Uhren; nichts davon entscheidet eine Regel. F16 ist die Sperre vor dem gesamten Migrationspfad. |
| B02 | [K] L32 (`LoadCaptured` kopiert `let`-Struct — nach Klärung ★W3) · [S] COL-34 (`+`/`*` auf Arrays ins Operatorkapitel) · [S] W22/W35 (gemessene Sätze festschreiben) · [A] W34 als Uhrenerweiterung von SEM0108/0109 auf den Aufrufpfad · [S] W12/W13/W9/W44 | W34 ist keine Designfrage — die laufenden Uhren treffen den Kern nicht (SemaRules.cs:323/337/389). |
| B03 | [K] F28 (`never` ICE) · [K] FN39 (`defer { return }` tötet den Compiler, exit 127 auch bei `check`) · [K] F12 (geworfenes Enum) · [K] OP-16 (never in Wertposition) · [S] F32 (Spec-PR Grammatik vs Anhang, PAR0023+SEM0036) · [S] F21/F38 (Kette; Panik-Katalog mit Reichweiten-Spalte) · [A] F10-A1, F30-A, F39-A · [A] G36-B (throws E am Aufruf wenigstens ablehnen) · [S] F41 als CONTRIBUTING-Regel | Fünf ICEs in einer Ecke; keine ist eine Designfrage. |
| B04 | [K] G13/G41 (`>>` im Parser + Formatter-Roundtrip) · [K] G18 (Absturz → Diagnose mit Position, §12.4) · [K] G21 (sechs panickende Koroutinen-Formen, fehlendes Konstruktorargument) · [K] G37 (`Box<void>` CLI0020) · [K] G38 (`extend Bag` IR0001) · [K] OPT-15 (`?T` erfüllt jede Constraint still) · [A] IF-02 (Regeländerung in Bugfix-Größe, spec-first) · [A] IF-35 A / IF-11 A (stille `extend`-Ziele refusen) · [A] L4 A (Diagnose mit Regel im Text) · [A] SK-10 Display für `uint` und alle Breiten · [A] SL-07 A · [A] SL-09 std.result/std.option auf Methoden (geht seit 4.6) · [S] META-16 A (Guide 15:20-22 „genau zwei Ausnahmen“) | Vier davon sind §12.1/§12.4-Verstöße von 4.6, keine v5-Arbeit. |
| B05 | [K] OVL-33 (Lambda gegen int-Parameter stimmt ab) · [K] OVL-40 B (Überladungsmenge auf Primitiven) · [K] OVL-29 C (selektiver Import filtert nicht) · [K] IF-36 (Argumentformung vor callvirt, Default/params) · [A] OVL-11 Doppelmeldung · [A] OVL-12 E+B (`<error>[]` in SEM0087) · [A] OVL-13 E · [A] FN03 Meldung · [A] FN28 B+D · [A] SK-32 B (reihenfolgeabhängige Adaption) · [A] SK-07 A (Tupel-Kontext) · [S] OVL-35 A+C, OVL-41 A (§11-Satz), OVL-25 C (§4.3a-Satz) | Alles Reparatur an einer Spec, die präziser ist als der Compiler. |
| B06 | [K] MOD-31 C (Bytecode-Name `a.b` vs `b` in `a`) · [K] MOD-44 A (Instanz mit privatem Typargument) · [K] S19 C (falsche Import-Warnung) · [A] MOD-11 E+B (Header-Hijack von `std.*`, Doppelladung) · [A] MOD-33 A, MOD-19 C, MOD-42, MOD-36, MOD-40 B, MOD-29, MOD-13 A, MOD-38 A+C, MOD-32 B, MOD-17 A · [A] MOD-25 A (EINE Sichtbarkeitsfunktion — erster Schritt von MOD-04) · [A] IF-33/IF-34 Defektwarnungen | Zwei Compilerdefekte für legale Programme; MOD-25 ist Vorbedingung, kein Entscheid. |
| B07 | [K] BP-29 C (Reproduzierbarkeits-Konformanzfall: zweimal bauen, SHA-256 vergleichen — Hash/Fingerprint ruhen darauf) · [A] BP-30 B / CLI-27 (Sperre auf `out/`) · [A] BP-14 D (Entry außerhalb sourceRoot warnt) · [A] BP-15 (Kollisionen nennen den Pfad) · [A] BP-21 B, BP-22 D, BP-23, BP-02 B · [A] BP-27 C (`lyric project --json`) · [A] BP-32 D (Template verliert `build.lyr`) · [A] BP-12/CLI-9 (`LYRIC_PROFILE` unbekannt warnt) · [A] W43 (Sprachversionsfeld) · [A] SL-29 B | Neun Befunde sind „ohne jede Regelentscheidung sofort warnbar“ (Dossier §Ist-Stand). |
| B08 | [K] B2 (Interpreter-Stolpern → Exit-Code statt 0xE0434352) · [K] B16 (Exception → benannte Panik, eine Zeile) · [K] B18 (`verify` meldet Bit 63 als `''`) · [A] B17 (Allokation deckeln: 57 Byte allokieren 1,6 GB) · [A] B3 (Typtabelle prunen, 337× Metadaten) · [A] B8/B9 (Custom Section id 0, Modul-Hash) · [A] B26-C (Reader fuzzen) · [A] META-06 C, META-11 A, META-12 E (Zahl in CT0002), META-27 E, META-29, META-35 A+C · [K] S23 Meldung („code points“ zählt UTF-16) · [S] S31 A, OPT-26 A, D31 · [S] B25 (Whole-Program in die Spec) | Der Reader ist die Angriffsfläche der Sandbox und wird nicht gefuzzt; jeder Posten ist formatneutral oder Minor per Bytecode.md:114-118. |
| B09 | [A] B7 (Backtrace kürzen, Wiederholung als Information) · [A] B19 A (match-Arm 6→1 Instruktion, „Nachmittagsänderung“) · [A] B14 f, B24, COL-09 B, S22 C · [A] L5 B+D / L30 A+C / FN31 A+C / FN47 A (Tiefe: Zahl unter der Thread-Grenze, Meldung nennt beide) · [A] L19 D / L37 B+C (ein Trap-Typ) · [K] NL43 C (VM0015 nennt beide Typen — „billigster Posten mit größtem Nutzen“) · [A] FN48 B, L20 E, B6-Pin (beide Maschinen und Ketten) · [S] L35 A, L44 A + R3i, L41 A, B23 A, E11 A | Messungen liegen vor; nichts ändert eine Zusage. |
| B10 | [A] FFI-F4 A (opaker Alias kreuzt — erlaubt nur, was heute abgelehnt wird) · [K] FFI-F24 A (Reentranz zählt gegen MaxCallDepth) · [K] FFI-F20/F21 A (eingehende Richtung prüfen) · [A] FFI-F17 (Kandidatenliste filtern) · [A] FFI-F1 A + `host` in `lyrvm --help` · [A] FFI-F33 A, FFI-F27 B, FFI-F32 B, FFI-F30 C+A · [S] FFI-F23 A, FFI-F34 A, FFI-F31 A, FFI-F18/F10/F29 (ein Spec-PR §13) · [A] L27 A+C, SL-34 A, SL-11 A′ (eine Capability-Ableitung statt zwei — Rule 2 im Compiler) · [A] T23 B (`--grant` an `lyrtest`), E21 A, CLI-36 A, MOD-39 A, G34 A, OPT-30 A | Drei Prozessabbrüche unter `hostAccess` sind heute gemessen; `design/abi.md:37` ist falsch. |
| B11 | [A] NL9 B (`step()`, zwei Lyric-Zeilen) · [A] NL17 B+C, NL21 B, NL42 B, NL26 D · [S] NL33 Doku-Satz, NL34 Spec-Satz, NL22 A, NL16 A, NL20 D, NL13 B, NL39/NL40 (gemessen) · [A] F22 B1 · [A] SL-24 Doku-Vertrag | Vier Fragen sind durch Messung schon beantwortet; der Rest ist additiv am gebauten Scheduler. |
| B12 | [K] SK-29 (`float as char` = 0) · [K] SK-28 (300i8 „defect in the compiler“) · [K] SK-16 D / COL-06 ICE (Index-Slot) · [K] SK-20 Bug melden · [K] COL-04 Warnung (Float-Range) · [S] B21 A (numerische Semantik als §5a — VOR B20) · [S] SK-30 A, SK-36 A, SK-39 B, SK-34 B, L44 A, L14 A, OP-41 A, META-37 A · [A] SK-09 (Meldungen zusammenlegen), SK-41 A, SK-14 A/B, SK-15 g, EP-37 A (Grammatik zieht nach) | Die Literal-/Slottyp-Familie hat vier Fälle im Release-Profil still fehltypisiert. |
| B13 | [S] OP-2 B (fünf Produktionen für drei Waisen), OP-21 A + OP-38 A (Auswertungsreihenfolge, VOR OP-10), OP-40 A, OP-24, OP-32 A, OP-17, OP-19 · [A] OP-8/OP-9 (4.7-Runde wie gesetzt), OP-35 (`increment_on_float` since-gegated) · [K] FN24 A (ICE → Diagnose für Rahmenkonstrukte im Lambda) · [A] FN07/FN32 (Kapitel + examples), FN09/FN49 (Satz), FN41, FN45 A, FN19 A, FN21 A, FN12 A, FN15 A, FN10 B · [S] COL-17 (Spec-interner Widerspruch :101 vs :107) | Grammatik und Spec haben drei unangeschlossene Produktionen; das ist Textarbeit. |
| B14 | [A] OPT-42 A+B+C, OPT-29 B, OPT-21 A+B, OPT-22 A (Befund K), OPT-44 A · [K] OPT-14 A (`?void` ICE), OPT-23 A (`p?.act()` IR0001) · [S] OPT-05/06 A, OPT-17/36 A, OPT-33 A, OPT-37 A, OPT-39 A, OPT-43 A, OPT-49 A, OPT-09 Pins · [A] OPT-46 B, OPT-08/40 B · [K] EP-13 ICE, EP-28 B (Miscompile bei 64) · [A] EP-02 A (unreachable arm), EP-24 B, EP-33 A, EP-34 A, EP-38 A · [S] EP-39 A (VOR EP-03), EP-40 C, EP-41 A, EP-14 A, EP-15 A, EP-20 · [A] META-03 (std-Enums @NonExhaustive in 4.7) · [A] COL-36 B (irrefutable Pattern im Schleifenkopf) | Keiner dieser Posten ist formatrelevant (OPT-26). |
| B15 | [K] COL-06 ICE, COL-30 (IR0001 → SEM0001) · [A] COL-09 B, COL-11 B+C (stille Null-Schleife), COL-23 D, COL-40 D, COL-41 B+D · [S] COL-10 A, COL-16, COL-18 A, COL-24 A, COL-28 A, COL-39 B, COL-20 B · [A] SL-19 A (Map iterierbar), SL-27 A (Native-Namensvertrag), SL-05 C, SL-35 A, SL-22 A, SL-25 A, SL-16 C · [A] NL11 B, OPT-19 B · [A] S03 E (`slice(start,end)` mit Uhr) | Die Suite hat für LYR-SEM0007 null Fälle — vor COL-06/07/22 gehören Fälle hinein. |
| B16 | [K] S02 A (astrales char-Literal) · [K] S04 A (natives compare UND hash — 5,2 s / 2,7 s bei n=40 000) · [K] S08 C (`{x:{w}}` heute still falsch) · [K] S13 Folgefehler-Lawine · [K] S17 A (println kennt nur `int`) · [K] S23 Meldung · [A] S06 C, S14 A, S21 A, S30 B, S12/S13 A (additiv), S26 A · [S] S25 A, S28 A, SL-33 A, SK-33/SK-34 B, SK-18 B, SK-19 A, L38 A+B | S04 ist der praxisrelevanteste Bugfix der Runde (jedes `Map<string,V>`). |
| B17 | [K] T6 A / T8 A / T26 A (`check` und `test` widersprechen sich über Wurzel, Namen, Header — 17× schneller) · [K] T28 B (`exit` im Test macht den Lauf grün und stumm) · [K] CLI-39 F6, CLI-10 F5, CLI-23 · [A] T2 A+B, T4 C, T9 A+C+D, T10 C, T12 C, T19 B, T21 B, T24 C, T25 A/D, T27 A, T29 B, T32 B, T34 D, SL-32 A · [A] CLI-2 C (Verben), CLI-5 B, CLI-6 B (symmetrische Negation), CLI-13 A, CLI-14 A+C, CLI-15 B, CLI-18 D, CLI-20 A+C, CLI-25 A (Zahl), CLI-31 B, CLI-34 A, CLI-37 A+B · [A] E24 A, META-28 | Vier Werkzeug-Ist-Stand-Befunde sind ausgelieferte Divergenzen, keine Features. |
| B18 | [K] E10 (LYRIC_PROFILE-Kollision) · [K] E5 (Klassifikator aus dem Parser, `type Meters = int;` → 5 Fehler) · [K] D32 B (Recovery-Lawine im Editor: zwölf Fehler aus einem `:`) · [A] E1 A, E3 A+D, E4 A über E34, E6 A+D, E7 B, E8 A, E12 A, E13 A, E14 A, E17 B, E18 A, E19 A, E20 B+A, E22 A, E25 A/C, E26 B, E27 A, E29 B, E32 A, E33 A · [A] D5 B, D6 A+D, D7 D, D10 C, D11 C+D, D12 A+C, D16 B+C+D, D24 C, D27 B+C, D29 C · [A] IF-28 B+C, IF-32 B, IF-47, G40 A, META-32 A+B+C · [S] E30 A (Formatter-Ausgabe gehört zur Version) | Werkzeugfixes ohne Vertragsänderung; E30 ist der eine Satz, der fehlt. |

---

## Anhang A — Vorschläge, die den Charakter berühren und AUSDRÜCKLICH begründet werden müssen

| Regel / Ausschluss | Vorschlag | Begründung im Dossier | Status |
|---|---|---|---|
| Ein Mechanismus pro Konzept | SK-02 A `checked { }` (zweite Antwort auf `+`, lexikalisch) | „Modus auf einem Mechanismus, von §3.2 zugesagt“ | ADR-pflichtig (★W1) |
| Ein Mechanismus | D22 benannte Warnungsgruppen (zweiter Namensraum neben Codes) | vier Codelisten wären vier Mechanismen | ADR |
| Ein Mechanismus | D3/SL-17/META-22 Unterdrückung (zweiter Weg zum Schweigen neben `_`) | `_` ändert das Programm, Unterdrückung nur die Meldung; SEM0071 ausgenommen | ADR (W10) |
| Ein Mechanismus | CLI-16 `--deny` neben `--grant` | dieselbe Bitmaske in Gegenrichtung | Satz im ADR |
| Ein Mechanismus | META-10 `embed()` als zweite Compile-Zeit-Form neben `comptime` | `comptime` bleibt capability-frei, `embed` hält die Grenze sichtbar | ADR |
| Ein Mechanismus | S11 Laufzeit-`format` neben f-Strings; S12/S13 drei Literalformen | verschiedene Fragen; rein lexikalische Schalter | Spec-Satz |
| Ein Mechanismus | OVL-05 C Kontext wählt Instanz (Wertposition, `as`, `T.parse` als EINE Regel) | sonst drei zielgewählte Wege | nur mit ausdrücklicher Rule-2-Antwort |
| Ein Mechanismus | L36 C `become` (Tail Call opt-in) | nur bei Bedarf, Empfehlung bleibt A | ADR falls je |
| Ein Mechanismus | T1 B compilergelesenes `assert` (zerlegt den Ausdruck) | kein Makro, solange EIN Konstrukt mit EINER Regel | ADR, wenn erweiterbar |
| `never` nicht als allgemeiner Typ | T22 A (`fail(): never`), OP-16 A, F28 A | nur Rückgabeposition, nur Flussanalyse; `panic` hat es schon | eng begründen |
| Kein Option-Enum neben `?T` | OPT-38 A Nutzer-Enum als „benannter Ausweg“ (`Opt<?int>` kompiliert heute) | Rule 2 fragt nach Mechanismen, nicht Datentypen; OPT-02 A muss den Payload ausnehmen | Spec-Satz |
| single-threaded + Koroutinen | NL15 Isolates; T14 B mehrere VMs auf Threads; 4.2.0 Pool-Threads | „der Buchstabe bricht, die Eigenschaft nicht“ — Umformulierung „kein geteilter veränderlicher Speicher“ | ADR (W20) |
| GC als einziger Speichermechanismus | FFI-F11 B Arena (nicht A roher CPtr); W16 D Copy/Clone abgelehnt | Arena ist Lebensdauer als Wert, bounds-geprüft | ADR für extern "C" |
| Kein `unsafe` | FFI-F11 A abgelehnt; L13 B′ (kein Wort `unsafe`) gegen `design/abi.md:75` | STATUS.md:2469 trifft rohe Zeiger stärker als Bounds-Checks | FFI-Gebiet entscheidet |
| Panik nicht fangbar | F14 A; T2 B (Runner fängt, weil außerhalb) | Runner ist ein Host | Spec-Satz (T28 B: Host darf Native ersetzen) |
| Keine Vererbung | IF-01 B Upcast Interface→Eltern-Interface; IF-17 sealed abgelehnt | Implikation über Konformanzkette, kein Subtyp | spec-first (05-interfaces.md:112-115 retiriert) |
| Kein `finally` | F8 D (kein errdefer), F33 A (Panik läuft nichts) | F7-D macht `defer` nicht-werfend, `var bool` kostet nichts | — |
| Ordnung auf Optionals | OPT-06 A (nein) — aber OPT-31: die Synthese trägt den Ausschluss nicht mit | Satz für synthetisiertes `compare` | Spec-Satz |
| Capability-Sandbox als Host-Grenze | FFI-F1: `hostAccess` ist gemessen ein Bypass; SL-13 B (`extern "dotnet"`-Batterien) abgelehnt | eine extern-gestützte Stdlib nähme jedes Programm aus der Tabelle | B10, vor der Bibliotheks-Umkehr |
| Spec-first | B1/B16/B17/B21/B26, OPT-24, COL-12/21, IF-01/13, EP-05/39, NL38, OP-45 | Regel-PR vor Code; ein Pin, dessen Regel fällt, retiriert mit ihr | B01 als Ritual |

## Anhang B — Rule-1-Fäden (Ideen, die der Maintainer ablegen muss, nicht dieses Dokument)

`lyric-v5-features.md` im Repo gegen Rule 1 (STATUS.md:2177-2180; SL K7, MOD-Konflikte, COL W17);
Dossiers liegen im Scratchpad (NL Rule-1-Zeile). Offene Scala-Frage (`derives`, META-13 B),
COW-Arrays (COL-33 D), `OrderedMap` (L23 E / COL-28 D), `std.unicode` im zweiten Ring (S16/SL-12),
Worker-Isolates (NL15), Registry-Index (BP-05 B), Trace-JIT und Snapshot-Format (B15 gestrichen).

---

## §6 Blöcke mit Fragen-IDs — vollständig

Jede der 966 IDs genau einmal (Skript-geprüft). Innerhalb eines Blocks nach Gebiet gruppiert; Fehlerbehandlung = `F…`, FFI = `FFI-F…`.

| Block | Fragen | IDs |
|---|---|---|
| **B01** Migrationsapparat und Spec-Prozess (FUNDAMENT 0) | 64 | W32 W33 W42 W45 · L28 · F26 F32 F41 · FFI-F25 · G46 · IF-29 IF-43 · META-17 META-22 META-23 META-33 · OVL-24 OVL-37 · FN33 FN46 · MOD-27 · BP-13 · SK-24 · OP-23 OP-29 OP-30 OP-31 OP-39 OP-45 · OPT-28 · EP-21 EP-32 · COL-38 · SL-17 SL-28 · NL38 · S27 · CLI-11 CLI-19 CLI-29 CLI-30 CLI-32 · T20 T33 · E2 E9 E28 · D1 D2 D3 D4 D9 D13 D14 D15 D18 D19 D20 D21 D22 D23 D26 D28 D30 |
| **B02** Wertsemantik: struct, mut, Kopie, Ort (FUNDAMENT) | 67 | W1 W2 W3 W4 W5 W6 W7 W8 W9 W10 W11 W12 W13 W14 W15 W16 W17 W18 W19 W20 W21 W22 W23 W24 W25 W26 W27 W28 W29 W30 W31 W34 W35 W36 W37 W38 W39 W40 W41 W44 W46 · L2 L3 L15 L18 L22 L32 L33 L34 L42 · IF-14 IF-23 IF-24 · FN16 FN40 · OPT-07 OPT-18 OPT-35 OPT-45 · EP-16 EP-26 EP-43 · COL-15 COL-19 COL-29 COL-33 COL-34 |
| **B03** Fehler, defer, Panik, typed throws (FUNDAMENT) | 47 | L10 L26 · F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 F13 F14 F15 F16 F17 F18 F19 F20 F21 F23 F25 F27 F28 F29 F30 F31 F33 F34 F35 F38 F39 · G36 · FN14 FN20 FN37 FN39 · OP-16 OP-22 · EP-17 EP-29 · SL-04 · CLI-12 · T22 |
| **B04** Interface- und Generics-Fundament: Self, statische Member, bedingte Konformanz, Synthese (FUNDAMENT) | 73 | L4 L21 · G02 G03 G04 G05 G06 G07 G08 G09 G10 G11 G12 G13 G14 G15 G16 G17 G18 G19 G20 G21 G22 G24 G25 G27 G31 G32 G37 G38 G39 G41 G42 G45 G47 · IF-04 IF-05 IF-06 IF-07 IF-08 IF-10 IF-11 IF-16 IF-17 IF-18 IF-19 IF-20 IF-22 IF-25 IF-35 IF-37 IF-38 · META-13 META-14 META-15 META-16 META-18 META-19 · FN13 · SK-10 SK-11 SK-22 · OP-5 OP-26 OP-36 OP-42 · EP-07 · SL-07 SL-08 SL-09 SL-14 SL-26 SL-31 |
| **B05** Aufloesung: Dispatch, Ueberladung, Namensauflösung, Defaults, Literal-Adaption (STAMM) | 74 | G26 G35 G43 · IF-01 IF-02 IF-03 IF-09 IF-13 IF-15 IF-30 IF-34 IF-36 IF-39 IF-40 · OVL-01 OVL-02 OVL-03 OVL-04 OVL-05 OVL-06 OVL-07 OVL-08 OVL-09 OVL-10 OVL-11 OVL-12 OVL-13 OVL-15 OVL-16 OVL-17 OVL-18 OVL-19 OVL-20 OVL-21 OVL-22 OVL-23 OVL-25 OVL-26 OVL-27 OVL-28 OVL-29 OVL-30 OVL-31 OVL-32 OVL-33 OVL-34 OVL-35 OVL-38 OVL-39 OVL-40 · FN01 FN02 FN03 FN04 FN22 FN23 FN27 FN28 FN34 FN35 FN36 FN38 FN44 · SK-06 SK-07 SK-23 SK-32 SK-37 · OP-13 OP-27 · OPT-37 · EP-05 EP-42 · SL-06 |
| **B06** Module, Sichtbarkeit, Namensraeume (STAMM) | 49 | IF-12 IF-27 IF-31 IF-33 IF-44 · META-34 · MOD-01 MOD-02 MOD-03 MOD-04 MOD-05 MOD-06 MOD-07 MOD-08 MOD-09 MOD-10 MOD-11 MOD-12 MOD-13 MOD-14 MOD-15 MOD-16 MOD-17 MOD-18 MOD-19 MOD-20 MOD-21 MOD-22 MOD-24 MOD-25 MOD-26 MOD-28 MOD-29 MOD-30 MOD-32 MOD-33 MOD-34 MOD-35 MOD-36 MOD-37 MOD-38 MOD-40 MOD-41 MOD-42 MOD-43 MOD-44 MOD-45 · OP-12 · S19 |
| **B07** Projektmodell, Build, Pakete, Toolchain (STAMM) | 42 | W43 · BP-01 BP-02 BP-03 BP-04 BP-05 BP-06 BP-07 BP-08 BP-09 BP-10 BP-11 BP-12 BP-14 BP-15 BP-16 BP-17 BP-18 BP-19 BP-20 BP-21 BP-22 BP-23 BP-24 BP-25 BP-26 BP-27 BP-28 BP-29 BP-30 BP-31 BP-32 BP-33 · SL-02 SL-18 SL-21 SL-29 · CLI-8 CLI-21 CLI-26 CLI-27 CLI-38 |
| **B08** Bytecode-Format, Validierung, Modulidentitaet, Attribute, comptime (STAMM) | 56 | F40 · G23 G29 G33 · IF-21 IF-26 IF-41 IF-48 · META-01 META-02 META-04 META-05 META-06 META-07 META-08 META-09 META-10 META-11 META-12 META-21 META-24 META-25 META-26 META-27 META-29 META-30 META-31 META-35 META-36 · OVL-14 OVL-36 OVL-41 · MOD-31 · B1 B2 B3 B8 B9 B10 B11 B12 B13 B16 B17 B18 B25 B26 B28 · SK-25 · OPT-26 · EP-08 EP-25 EP-30 · S23 S31 · D31 |
| **B09** Laufzeit und VM: Budget, Tiefe, Speicher, JIT, Optimierer, Backtrace (STAMM) | 61 | L1 L5 L6 L7 L8 L16 L19 L20 L24 L25 L30 L31 L35 L36 L37 L39 L40 L41 L43 · F24 F37 · G01 G28 G30 G44 · IF-42 · FN17 FN18 FN30 FN31 FN42 FN43 FN47 FN48 · B4 B5 B6 B7 B14 B15 B19 B20 B22 B23 B24 B27 · SK-26 SK-36 · OP-28 · OPT-25 OPT-50 · EP-18 EP-45 · COL-09 COL-32 · NL29 NL30 NL36 NL44 · S22 · E11 |
| **B10** Sandbox, Capabilities, FFI, Einbettung (STAMM) | 59 | L9 L13 L17 L27 L29 · FFI-F1 FFI-F2 FFI-F3 FFI-F4 FFI-F5 FFI-F6 FFI-F7 FFI-F8 FFI-F9 FFI-F10 FFI-F11 FFI-F12 FFI-F13 FFI-F14 FFI-F15 FFI-F16 FFI-F17 FFI-F18 FFI-F19 FFI-F20 FFI-F21 FFI-F22 FFI-F23 FFI-F24 FFI-F26 FFI-F27 FFI-F28 FFI-F29 FFI-F30 FFI-F31 FFI-F32 FFI-F33 FFI-F34 · G34 · IF-46 · FN29 · MOD-23 MOD-39 · OPT-30 · COL-37 · SL-11 SL-13 SL-30 SL-34 · NL32 NL35 · S24 · CLI-16 CLI-17 CLI-35 CLI-36 · T23 T24 · E21 |
| **B11** Koroutinen, Tasks, Nebenlaeufigkeit (STAMM) | 40 | F22 F36 · IF-45 · FN25 · SL-24 · NL1 NL2 NL3 NL4 NL5 NL6 NL7 NL8 NL9 NL10 NL12 NL13 NL14 NL15 NL16 NL17 NL18 NL19 NL20 NL21 NL22 NL24 NL25 NL26 NL27 NL28 NL33 NL34 NL37 NL39 NL40 NL41 NL42 NL43 · E15 |
| **B12** Skalare, Numerik, Ueberlauf, Determinismus (BLATT) | 39 | L11 L12 L14 L44 · META-37 · B21 · SK-01 SK-02 SK-03 SK-04 SK-05 SK-08 SK-09 SK-12 SK-13 SK-14 SK-15 SK-17 SK-20 SK-21 SK-27 SK-28 SK-29 SK-30 SK-31 SK-35 SK-38 SK-39 SK-40 SK-41 SK-42 · OP-18 OP-34 OP-41 OP-44 · EP-22 EP-23 EP-37 · COL-04 |
| **B13** Operatoren, Zuweisung, Ausdrucksformen, Lambdas, Funktionswerte (BLATT) | 43 | FN07 FN08 FN09 FN10 FN11 FN12 FN15 FN19 FN21 FN24 FN26 FN32 FN41 FN45 FN49 · OP-1 OP-2 OP-3 OP-4 OP-6 OP-7 OP-8 OP-9 OP-10 OP-11 OP-14 OP-15 OP-17 OP-19 OP-20 OP-21 OP-24 OP-25 OP-32 OP-33 OP-35 OP-37 OP-38 OP-40 OP-43 OP-46 · COL-17 COL-31 |
| **B14** Abwesenheit und Muster: Optionals, Narrowing, Enums, Pattern Matching (BLATT) | 68 | META-03 · OPT-01 OPT-02 OPT-03 OPT-04 OPT-05 OPT-06 OPT-08 OPT-09 OPT-10 OPT-11 OPT-12 OPT-13 OPT-14 OPT-15 OPT-16 OPT-17 OPT-20 OPT-21 OPT-22 OPT-23 OPT-24 OPT-27 OPT-29 OPT-31 OPT-32 OPT-33 OPT-34 OPT-36 OPT-38 OPT-39 OPT-40 OPT-41 OPT-42 OPT-43 OPT-44 OPT-47 OPT-48 OPT-49 · EP-01 EP-02 EP-03 EP-04 EP-06 EP-09 EP-10 EP-11 EP-12 EP-13 EP-14 EP-15 EP-19 EP-20 EP-24 EP-27 EP-28 EP-31 EP-33 EP-34 EP-35 EP-36 EP-38 EP-39 EP-40 EP-41 EP-44 · COL-36 · S29 |
| **B15** Collections, Iteration, Bibliotheksumfang, I/O (BLATT) | 48 | L23 · FN05 FN06 · SK-16 · OPT-19 · COL-01 COL-02 COL-03 COL-05 COL-06 COL-07 COL-08 COL-10 COL-11 COL-12 COL-13 COL-14 COL-16 COL-18 COL-20 COL-21 COL-22 COL-23 COL-24 COL-25 COL-26 COL-27 COL-28 COL-30 COL-35 COL-39 COL-40 · SL-01 SL-03 SL-05 SL-10 SL-16 SL-19 SL-20 SL-22 SL-23 SL-25 SL-27 SL-35 · NL11 NL23 NL31 · S03 |
| **B16** Strings, Zeichen, Formatierung, Unicode (BLATT) | 31 | L38 · SK-18 SK-19 SK-33 SK-34 · COL-41 · SL-12 SL-33 · S01 S02 S04 S05 S06 S07 S08 S09 S10 S11 S12 S13 S14 S15 S16 S17 S18 S20 S21 S25 S26 S28 S30 |
| **B17** CLI, Treiber, Profile, Testrunner (BLATT) | 61 | META-20 META-28 · OPT-46 · SL-15 SL-32 · CLI-1 CLI-2 CLI-3 CLI-4 CLI-5 CLI-6 CLI-7 CLI-9 CLI-10 CLI-13 CLI-14 CLI-15 CLI-18 CLI-20 CLI-22 CLI-23 CLI-24 CLI-25 CLI-28 CLI-31 CLI-33 CLI-34 CLI-37 CLI-39 · T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18 T19 T21 T25 T26 T27 T28 T29 T30 T31 T32 T34 · E23 E24 E31 |
| **B18** Editor, Debugger, REPL, Formatter, Diagnosequalitaet (BLATT) | 44 | G40 · IF-28 IF-32 IF-47 · META-32 · E1 E3 E4 E5 E6 E7 E8 E10 E12 E13 E14 E16 E17 E18 E19 E20 E22 E25 E26 E27 E29 E30 E32 E33 E34 · D5 D6 D7 D8 D10 D11 D12 D16 D17 D24 D25 D27 D29 D32 |

Summe: 966 Fragen in 18 Blöcken.
