# Lyric 5 — Designkorpus

Material für die Designrunde zu Lyric 5: Lyric 4 Gebiet für Gebiet gegen etablierte Sprachen
verglichen, jede Behauptung über Lyric 4 belegt (`gelesen` mit Pfad:Zeile) oder gemessen
(`gemessen` mit Probe), Vergleichsaussagen adversarisch geprüft. Der Korpus bereitet
Entscheidungen vor; er trifft keine. Jede Frage trägt Optionen, eine Empfehlung, den Bruchgrad
und die 4.x-Warnstufe, die sie bräuchte — das ist der Test, den Rule 1 verlangt.

## Aufbau

| Datei | Inhalt |
|---|---|
| `AGENDA.md` | 18 Entscheidungsblöcke in Sitzungsreihenfolge (§1), Begründung der Reihenfolge (§2), 25 Widersprüche zwischen Gebieten (§3), Uhrentabelle aller 246 Bruchfragen (§4), sofort Entscheidbares (§5), vollständige Zuordnung der Fragen-IDs zu Blöcken (§6) |
| `LUECKEN.md` | Vollständigkeitskritik: Sprachencheckliste (A), Lücken zwischen den Gebieten (B), zwanzig unbequeme Fragen (C) |
| `<gebiet>.md` | 24 Gebietsdossiers: Ist-Stand mit Belegen, Vergleichstabelle, Designfragen mit Optionen/Vorbild/Preis/Empfehlung/Bruchgrad/Abhängigkeiten, „Was wir übernehmen sollten", „Konflikte", „Nach der Kritik geändert" |
| `probes/` | die Probeprogramme, aus denen die `gemessen`-Belege stammen |

966 Fragen, davon 62 major und 184 minor. ID-Konvention pro Gebiet (`W`, `OPT`, `G`, `IF`,
`OVL`, `EP`, `F`, `FN`, `NL`, `MOD`, `OP`, `S`, `COL`, `META`, `L`, `FFI-F`, `CLI`, `BP`, `T`,
`E`, `D`, `B`, `SL`, `SK`).

## Basis und Vorbehalt

- Compiler: `lyrc`/`lyrvm`, Debug-Build vom 2026-09-24 aus `fix/c-sema-holes @ d8acd0ea` plus
  Arbeitsbaum; für die Revisionen der ersten Runde ein Build von 18:33 desselben Tages mit den
  Codes `LYR-SEM0106..0110`. Alle Messungen laufen gegen diese Binaries.
- Repo: `main` bewegte sich WÄHREND der Erstellung von `d8acd0ea` über `fix/c-sema-holes`,
  `fix/generic-method-on-generic-type`, `fix/d-diagnostics`, `feat/v5-warning-clocks` bis
  `release/v4.6.0-cut @ 6f6f029f` (PR #173). Ein `gelesen`-Beleg nennt Pfad:Zeile des Standes,
  den der jeweilige Agent vorfand; Dossiers mit Stichtag-Vermerk haben den Wechsel bemerkt
  und nachgezogen, die übrigen nicht notwendigerweise. Vor einer Entscheidung, die an einer
  Zeilennummer hängt, die Stelle gegen `main` prüfen.
- Spec: Clone `lyric-spec`, Stand mit PR #44 (§3.4a, §12.5).
- Die vier Migrationswarnungen `LYR-SEM0107..0110` (4.6, PR #172) sind Uhren, keine Antworten;
  die Antworten fällt diese Runde.

## Methode

Je Gebiet drei Stufen: Dossier → adversarische Kritik (falsche Behauptungen über Lyric 4,
falsche Aussagen über Vergleichssprachen, fehlende Fragen) → Revision. Danach zwei
Querschnittsagenten über alle 24: Agenda und Lückenkritik. 2026-09-24 bis 2026-09-28.
