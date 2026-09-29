# 12 — Migration 4 → 5

Lebendes Dokument des Bereichs 12. Fragen R1–R7, je **entschieden** oder **offen**. Basis:
00 (harter Cut, Migrationswerkzeug wo mechanisch, manuelle Migration akzeptabel, keine
4.x-Warnstufen), 07 (Editionen, `lyric fix --edition`), 09 A11 (`@Deprecated.replacement`),
10 SL-28 (mechanisch vs. semantisch), 11 G7/W1 (`lyric fix` als Verb über Diagnose-Fixes),
`../cli.md` CLI-19, `../diagnostik.md` D13/D14/D19–D22 (4.x-Migrationsfamilie — für 5
gegenstandslos, aber lehrreich).

## Bestandsaufnahme: was migriert werden muss

**Der Korpus in Lyric 4** (Stand 4.6): `stdlib/std` 19 Module, ~8 400 Zeilen; `examples/` 22
Programme plus Unterordner (`ffi`, `lambdas`, `macros`, `patterns`, `embedded-host`);
`stdlib-tests/` 22 Dateien; Guide 21 Kapitel, jedes Snippet kompiliert von der Suite;
Konformanzsuite im Spec-Repo (~158 Fälle, `since:`-Gates bis 4.6); Golden-Tests im Compiler
(97 Parsing, 27 Sema, 19 Lowering — **die fallen mit dem Compiler-Umbau ohnehin**); Erato
(eigene Runtime Lyricpp, C++ — Abnehmer, nicht Voraussetzer).

**Was sich für ein 4.x-Programm ändert** (aus 00–11, nach Art sortiert):

| Art | Änderungen |
|---|---|
| **Mechanisch** (Textersatz mit Typwissen) | `module`-Kopf fällt (07); `params` → `...`; `inout` → `&`; `throws A, B` → `throws [A, B]`; `catch (e: A \| B)` → `catch (e in [A, B])`; `opaque type` → Ein-Feld-Struct; `Equatable<T>` → `Equatable`; `Indexable<T>` → `Index<K>`; `Iterator<T>` → `Iterator<Item = T>`; freie Terminatoren/`listX`/`keys(m)` → Methoden; `xOrThrow(…)` → `x(…)`, stille Formen → `try? x(…)`; `absInt` → `abs`; `parseInt(s)` → `int.parse(s)` mit `try?`; `std.io.file.text` → `fs.readText`; `std.os.nowMillis` → `Instant.now()`; Modulpfade (`std.io.file` → `std.fs`); `combineHash` → `Hasher`; `println` braucht `import std.io`; `@Deprecated`-Ersetzungen; `@Repr`→`@Layout`-artige Attributnamen; `str` → `StringView` |
| **Semantisch** (Regel ändert Bedeutung; Werkzeug meldet, Mensch entscheidet) | Felder unveränderlich außer `var` (02: „jedes geschriebene Feld bekommt `var`" — mechanisch bestimmbar, aber Absicht prüfen); `abs(-3)` int statt float (Literal-Adaption); Überlauf = Panik statt Wrap (T2) — jede Stelle, die auf Wrap baute, braucht `+%`; `string.length()` in Bytes statt Codepoints (S1); `s[i]` = `char` an Byte i; `Map`-Iterationsreihenfolge je Lauf anders (K2); Ganzzahl-Weitung implizit, `int`→`float` nie; `as` nur numerisch; `Clone` tief (K5); `==` nur `Equatable` (kein `===`); `try` Pflicht an jeder werfenden Stelle; `for` über werfende Iteratoren mit `try`; `main` darf werfen; Panik unfangbar außer Task-Grenze; `spawn` gibt `Task`; Capabilities/`--grant` weg; `extern "dotnet"` weg (FFI neu über C) |
| **Strukturell** (kein Werkzeug) | Sichtbarkeit `internal` als Default statt `pub`-Ratchet; Pakete mit `lyric.toml` statt `lyric.json`; `build.lyr` definiert nichts mehr (P1/BS1); Tests im Paket; Prelude; Bytecode/`lyrpack`/VM-Werkzeuge entfallen; Embedding-API neu (H1–H8) |

## Fragen

Reihenfolge: R1 (Werkzeugform) → R2 (Regelkatalog) → R3 (Reihenfolge der Korpora) → R4 (Spec
und Suite) → R5 (Repo/Org) → R6 (4.x-Linie und Lyric-Script) → R7 (Umsetzungsplan — das
Ergebnis der ganzen Runde).

## R1 — Das Werkzeug: `lyric fix --from-4`: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| F1 | **Das 4.x-Frontend (Parser + Sema, C#) eingefroren als Bibliothek im 5er-`lyric`**, nur für `fix --from-4`: liest 4.x mit vollem Typwissen (Empfängertyp für `abs`, Schreibstellen für `var`), wendet die Regeln aus R2 an, druckt 5er-Syntax | `go fix`, `cargo fix`; verworfen: toleranter 5er-Parser mit 4-Modus (zwei Grammatiken, kein 4-Sema), Textregeln (`abs(` ohne Typ nicht entscheidbar), Migrator bei Lyric-Script (driftet mit Z4 weg) |
| F2 | **Form**: `lyric fix --from-4 [pfad]` schreibt um (`--dry-run`, `--json` nach G8), konvertiert `lyric.json` → `lyric.toml`, entfernt Modulköpfe, schlägt das Layout vor; **mechanische** Regeln schreiben, **semantische** werden `needsReview`-Notizen mit Erklärung und Vorschlag (SL-28) | — |
| F3 | **Kein Versprechen, dass das Ergebnis kompiliert** — danach `lyric check`; Maßstab ist der `examples/`-Lauf (R3). Verfallsdatum: eine Major-Linie, danach eingefroren | — |

## R2 — Der Regelkatalog: **offen**

Vollständige Liste aus den Bereichen 0–11, je Regel: Erkennung, Ersetzung, Art (mechanisch /
Review / Hand), Konformanzfall (vorher/nachher). Wo Sema-Wissen nötig ist (Typ des Empfängers
für `abs`, Feldschreibungen für `var`), läuft `fix --from-4` auf dem 4.x-Sema-Modell.

## R3 — Reihenfolge der Korpora: **offen**

Vorschlag: `std` wird **neu geschrieben** (nicht migriert — Regel D, neue Module, neue Grenze);
Guide wird neu geschrieben (Spec 5.0 als Quelle); `examples/` migriert (Testfall für `fix`);
Konformanzsuite neu (R4); Erato bleibt auf Lyricpp/4.x, bis es will. Was `fix --from-4` am
Ende wirklich abdeckt, misst der `examples/`-Lauf.

## R4 — Spec 5.0 und Konformanzsuite: **offen**

Die Dokumente `spec/00–12` sind die Grundlage der Spec 5.0 (normativ: Grammatik, Typsystem,
Fehler, Nebenläufigkeit, Module, Metaprogrammierung, Syntax, stdlib-Verträge §11, CLI-Kapitel
C10, Diagnostik G-Reihe, FFI-Typtabelle X2). Suite: `since:`-Gates werden auf `5.0` gesetzt,
4.x-Fälle retirieren mit ihren Regeln; Reihenfolge spec-first bleibt (Regel-PR → Zwilling →
Release + Pin). Wer schreibt die Spec — aus den Bereichsdokumenten generiert oder von Hand?

## R5 — Repository und Org: **offen**

Lyric 5 im bestehenden `lyric`-Repo (Branch, dann `main`) oder neues Repo `lyric5`? Was mit dem
4.x-Code (Compiler C#: wird Basis des 5er-Frontends; VM: wird Lyric-Script); Spec-Repo-Zweig;
Clients (Tree-sitter-Repo neu); Erato unberührt. Versionsnummer der ersten Release: `5.0.0`.

## R6 — Die 4.x-Linie und Lyric-Script: **offen**

4.x endet nicht — es **wird** Lyric-Script (Z4): letzte Release der 4er-Linie unter dem Namen
`lyric`, erste unter `lyric-script`; eigene Roadmap, eigene Versionierung (1.0 oder 4.7?);
was aus 5 zurückfließt (Formatsprache, Lints, Diagnostik) ist Sache dieser Roadmap.

## R7 — Der Umsetzungsplan: **offen**

Das eigentliche Ergebnis der Runde (Maintainer: „einen detailreichen Plan, nach dem Lyric 4
stückweise auf 5 gebracht wird"). Meilensteine mit konkretem Artefakt je Schritt (CONTRIBUTING
Rule 3): Reihenfolge Runtime-Bring-up (Boehm-Stufe) → C-Backend für einen Sprachkern →
Wertmodell/Typsystem → Fehler-ABI → Koroutinen/Scheduler → std-Kern → Module/Manifest →
Metaprogrammierung → volle std → Werkzeuge → Migration → 5.0. Welche Teile parallel, was der
erste lauffähige Meilenstein ist (Hello World nativ), wo die Messpunkte (Z2) sitzen. Eigenes
Dokument `13-umsetzungsplan.md`?
