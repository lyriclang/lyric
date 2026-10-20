# 14 — Website, Book und Doku

Der Bereich, den 11 T8 vorgemerkt hat. Phasen: **A** Inhalt, **B** Aussehen, **C** Technik —
**entschieden** 2026-10-20 (Maintainer, im Dialog); **D** Umsetzungsplan, **E** Einordnung in 13 —
ebenfalls **entschieden** 2026-10-20. Die heutige Seite
(`tools/DocGen`, `docs.yml`, lyric.oil1i.de) ist kein Ausgangspunkt: DocGen dokumentiert die
4.x-`stdlib/`, nicht `stdlib5/`, rendert den 4.x-Guide und veröffentlicht `/nightly/`, den Kanal,
den 11 T5 abgeschafft hat.

## A — Inhalt: **entschieden** (2026-10-20)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| W1 | **Zwei Leser**: wer Lyric prüft (in einer Minute verstehen, was es ist), und wer es benutzt (nachschlagen). Die Startseite gehört dem ersten, alles andere dem zweiten | Go, Zig; verworfen: eine Seite für alle |
| W2 | **Bereiche**: Startseite, Download (der Mirror aus 11 T8: dieselben Archive, Hashes, Signaturen), Book, std-Referenz (`lyric doc`), Spec, Diagnose-Katalog (G9), Releases/Changelog | Türen und Verworfenes unten |
| W3 | **Versionen**: je Release eine eingefrorene Fassung (`5.0`, `5.1` …) und `dev`; kein `nightly` | 11 T5; Zig (Doku je Release und Master) |
| W4 | **Nur Englisch** | Spec, std und Diagnosen sind englisch; eine Übersetzung ist ein Dauerauftrag |
| W5 | **Lyric-Script bekommt eine eigene Seite** im eigenen Repo; diese verlinkt sie nur | 00 Z4: zwei Sprachen auf einer Seite verwischen die Aufteilung |
| W6 | **Lernen: „The Lyric Book“, Referenz: Spec, std, Katalog.** Das Book wird von vorn nach hinten gelesen, die Kapitel bauen aufeinander auf, dazwischen Projektkapitel (ein Kommandozeilenwerkzeug, ein Server); jedes Snippet läuft in der Suite. Es **ersetzt** Tour und Guide: zwei erzählende Texte über dieselbe Sprache wären derselbe Inhalt zweimal. Den Leser aus W1 bedient die Startseite | Rust (Book, Reference, std); verworfen: Gos Tour als eigene Ebene, der Guide nach Bereichen (12 Q2, hiermit geändert) |
| W7 | **Volltextsuche, statisch**: der Index entsteht beim Bau, gesucht wird im Browser — über Book, std, Spec und Diagnose-Codes (Werkzeug: C6) | kein Server; verworfen: nur Namen und Codes |
| W8 | **Doc-Comments beschreiben, was ein Element tut und wie man es benutzt — nie, warum es so entschieden wurde.** Form unten | rustdoc, Go (erster Satz), Swift (Komplexität) |

### W8 — Form eines Doc-Comments

| Teil | Regel |
|---|---|
| Erster Satz | Ein Satz, der sagt, was das Element tut („Creates …“, „Returns …“, „Appends …“). Er steht allein in Übersichten und Suchtreffern |
| Beschreibung | Verhalten, Randfälle, Komplexität, wo sie zählt |
| `# Panics` | wann das Element paniken kann |
| `# Errors` | was in `throws` steht und wann es geworfen wird |
| `# Examples` | ausführbarer Code, läuft als Doc-Test (11 E7) |
| `# Safety` | nur an `unsafe` (M14) |
| Modul-Doc | Überblick, die wichtigsten Typen, ein Beispiel für den typischen Gebrauch |
| Erlaubte Verweise | andere API-Elemente (`[List.push]`), Normen (RFC 4648), Codes, die ein Nutzer sieht (`RT0002`, verlinkt auf den Katalog) |
| Verboten | Entscheidungs-IDs (`(10 C3)`), Meilensteine und Slices, Reviews, Spec-Pfade, „verworfen“, Geschichte, Begründungen, Vergleiche mit anderen Sprachen |

Das gilt für `///`; für alle übrigen Kommentare gilt es schon (`CONTRIBUTING.md` §Code style:
„no milestone or decision references“). Gezählt vom Wächter
(`CommentRatchetTests`, K1) am 2026-10-20, Kommentarzeilen mit Bezug: `stdlib5` `///` 547,
`stdlib5` `//` 114, `runtime/` 277, `src/Lyric5*` 392, `src/Lyric.Frontend` 1632,
`tests/Lyric5.Tests` 391 — der Stand je Datei in `tooling/comment-ratchet.toml`.

**Uhren**: ein **Wächter als Ratchet** (die Zahl je Bereich darf nur sinken) sofort, als eigener PR während M12a; ein
**Aufräumblock** zwischen M12b und M13a — `stdlib5` in die Form oben, `runtime/`, der C#-Code und die Tests ohne
Entscheidungsbezüge, am Ende steht der Ratchet auf 0 und wird ein harter Wächter; die
`# Examples` der std mit dem Doc-Test-Läufer in M13a.

### Türen und Verworfenes

| Was | Stand |
|---|---|
| Playground | **Tür** — braucht einen Server, der fremden Code ausführt, oder einen Wasm-Backend (00 E); öffnet sich mit einem davon |
| Benchmarks-Seite | **Tür** — öffnet sich, wenn 5.0 die Ratchets aus 13 hält; die Zahlen kommen aus `bench/` |
| Logo (Zeichen neben der Wortmarke) | **Tür** — öffnet sich mit einem Entwurf, den der Maintainer will |
| Blog, News | verworfen — die Release-Notes sind die Nachrichten; ein Blog ist ein Ideenspeicher mit Datum |
| Community-, Team-, Sponsorenseiten | verworfen — persönliches Projekt |
| Tour | verworfen — W6 |
| Übersetzungen | verworfen — W4 |

## B — Aussehen: **entschieden** (2026-10-20)

Erarbeitet an einem Mix-and-Match-Mockup (Startseite, Book-Seite, std-Seite; elf Dimensionen
mit je zwei bis vier Optionen). Die Richtung: **nüchtern-technisch** (Go, Zig) mit einem kleinen
editorialen Anteil (Rust Book) — der bleibt in der Textbreite und den Notizboxen des Books.

| # | Entscheidung | Verworfen |
|---|---|---|
| B1 | **Charakter**: nüchtern-technisch; das Editoriale nur in der Textbreite (Fließtext höchstens ~68 Zeichen) und den Notizboxen | Produktseite mit Verläufen und Hero-Grafik (Bun, Deno); Serif-Überschriften und -Fließtext |
| B2 | **Wortmarke** `Lyric`: Inter Bold, Laufweite −0,03 em, kein Zeichen | Mono klein (`lyric`), Serif kursiv |
| B3 | **Farbe**: ein Akzent, **Violett** — hell `#6440c4`, dunkel `#b29aff`; neutraler Grund hell `#ffffff`/Text `#16181d`/gedämpft `#535a69`/Linie `#e1e4e9`/Fläche `#f4f5f7`, dunkel `#111317`/`#e7e9ee`/`#a3aab6`/`#2b2f37`/`#181b21` | Blau, Petrol, Ziegel; eine Palette mit mehreren Akzenten |
| B4 | **Hell und dunkel**: folgt dem System, dazu ein Umschalter | nur System |
| B5 | **Schrift**: Inter für Überschriften und Text, JetBrains Mono für Code | IBM Plex Mono, Fira Code; Serif |
| B6 | **Dichte kompakt** (Text 15 px, Abstände auf 0,75 der Grundteilung), **Ecken 0 px** | normal, luftig; 4 und 10 px |
| B7 | **Kopfzeile als Werkzeugleiste**: Wortmarke mit Versionswahl, Navigation (Book, std, Spec, Diagnostics, Download), Suche, GitHub | schlichte Zeile ohne Suche; zentrierter Masthead |
| B8 | **Startseite**: links Headline, ein Absatz, zwei Knöpfe (Get started, Read the Book), rechts ein echtes Programm; darunter drei Kernaussagen (native Binaries, Nebenläufigkeit ohne Färbung, Fehler in der Signatur) und drei Einstiege (Book, std, Spec) | Headline groß über dem Code; Download zuerst |
| B9 | **Code-Blöcke flach**: Fläche ohne Rahmen und Titelzeile; Hervorhebung zurückhaltend — Schlüsselwörter im Akzent, Typen fett, Texte und Zahlen ocker, Kommentare gedämpft und kursiv | mit Rahmen, Dateiname und Kopierknopf; immer dunkel |
| B10 | **Layout**: Referenz in drei Spalten (Navigation, Text, „On this page“), Book in zwei. Breit läuft die Navigation mit und scrollt für sich. **Unter 900 px** wird sie eine Leiste — links, wo man ist, rechts „Contents“ —, aufgeklappt eine Box mit eigener Scrollleiste (höchstens etwa die halbe Bildschirmhöhe); der Text beginnt direkt darunter. „On this page“ fällt unter 1150 px weg | die Navigation über dem Text ausgeklappt (lange Wege auf dem Telefon) |

## C — Technik: **entschieden** (2026-10-20)

| # | Entscheidung | Vorbild / Verworfenes |
|---|---|---|
| C1 | **Ein Aussehen, zwei Erzeuger**: `lyric doc` (11 E7, M13a) rendert die std-Doku — und jedes Nutzerpaket — aus `///`; ein schlankes `tools/Site` (C#, Markdig) rendert Startseite, Book, Spec, Katalog und Releases. Beide teilen Vorlage, CSS und Hervorhebung; `lyric doc` ohne die Site-Navigation, wenn es ein Nutzerpaket dokumentiert. `tools/DocGen` geht darin auf | rustdoc + mdBook mit einem Thema; verworfen: ein fremder Generator (Zola, mdBook, Astro — ein zweites Vorlagensystem, Node oder Rust in der CI), die Seite in Lyric 5 (Markdown-Parser und Hervorhebung in Lyric — **Tür**, öffnet sich, wenn die std Markdown bekommt) |
| C2 | **Hervorhebung beim Bau über den Lexer des Compilers**, kein JS | eine Grammatik; verworfen: highlight.js, die TextMate-Grammatik (`tooling/textmate`) als zweite Quelle |
| C3 | **Quellen**: das Book in diesem Repo (`docs/book/`), seine Snippets in der Suite gegen den Compiler; die Spec in `lyric-spec`, gelesen am Commit aus `spec.pin`; der Katalog aus `diagnostics.toml` (G9); Releases aus `CHANGELOG.md` | Regel, Text und Compiler auf einem Stand |
| C4 | **Versionen**: `/` ist das letzte Stable, darunter `/5.0/`, `/5.1/` …, eingefroren beim Tag; `/dev/` neu bei jedem Push auf `main`. Vor 5.0 nur `/dev/` (und `/` leitet dorthin). Die Versionswahl liest eine Liste (`versions.json`), keine Seite muss für eine neue Version umgeschrieben werden | W3; DocGens `VersionIndex` |
| C5 | **Die 1.x–4.x-Seite bleibt als Legacy erreichbar**: alle `vX.Y.Z/`-Verzeichnisse und ihre `versions.json` ziehen unverändert nach `/legacy/` (ihre Links sind relativ, der Versionswähler leitet seine Basis aus `../versions.json` ab — beides trägt den Umzug); `/legacy/` bekommt eine Übersicht. Die neue Seite nennt sie als **„Lyric 1.0.0–4.5.0 (Legacy)“** — als Eintrag der Versionswahl und als Link im Fuß. Die alten Pfade `/vX.Y.Z/…` leiten auf `/legacy/vX.Y.Z/…` (eine `404.html`, die den Pfad umschreibt). `/nightly/` (der nie veröffentlichte 4.6-Schnitt) fällt weg | verworfen: die Seite an `lyric-script` geben — Lyric-Script beginnt bei 1.0.0 neu und hat mit diesen Versionen nichts zu tun |
| C6 | **Volltextsuche mit Pagefind**: läuft nach dem Bau über das HTML, schreibt einen gestückelten Index je Version, sucht im Browser. Das Binary ist versioniert und hash-geprüft wie `zig` (`tooling/pagefind-version`) | ein eigener Volltextindex (lunr-Art) lädt den ganzen Index; Pagefind lädt nur die Stücke einer Anfrage |
| C7 | **Schriften selbst ausgeliefert** (Inter, JetBrains Mono, beide OFL, mit Lizenz). **JS nur für** den Umschalter hell/dunkel, die Navigation bei schmalem Fenster, die Versionswahl und die Suche; ohne JS geht alles außer Suche und Versionswahl. **Kein Tracking, keine Cookies** | ein Abruf bei Google Fonts gibt Google die IP-Adresse jedes Lesers (DSGVO) |
| C8 | **CI**: jeder PR baut die Seite (Linkprüfung als Test; die Book-Snippets in der Suite); ein Push auf `main` veröffentlicht `/dev/`, ein Tag seine Version — wie `dev.yml` und `release.yml` | heute: `docs.yml` nur auf Tag oder von Hand |
| C9 | **Hosting**: die Seite auf **GitHub Pages** unter **`lyric.oil1i.de`** (bleibt); der **Download-Mirror auf dem VPS des Maintainers** unter eigenem Namen (`dl.lyric.oil1i.de`): Archive, `SHA256SUMS`, minisign-Signaturen, die Kanalliste für `lyric toolchain`. Der Release-Job lädt per rsync über einen eingeschränkten Schlüssel hoch. Die Unversehrtheit hängt an den Signaturen, nicht am Server; `lyric toolchain` fällt auf GitHub zurück (11 T8) | zwei unabhängige Hosts: fällt einer aus, bleibt der andere; verworfen: alles auf Pages (1 GB, 100 GB Verkehr im Monat — die Archive sprengen das über die Jahre), alles auf dem VPS (Betrieb für die ganze Seite) |

**Der VPS** (Uhr: M15, mit der Distribution — bis dahin gibt es keine Archive, die ein Mirror
spiegeln müsste) wird mit dem Maintainer eingerichtet und gehärtet, bevor er etwas ausliefert (Plan
in D): nur Schlüssel-Login, kein Root über SSH, Firewall auf 22/80/443, automatische
Sicherheitsupdates, ein statischer Webserver mit TLS, ein Deploy-Benutzer ohne Shell, der nur
in das Mirror-Verzeichnis schreiben kann.

## D — Umsetzungsplan: **entschieden** (2026-10-20)

Zwei Spuren: **K** räumt die Kommentare auf (W8), **S** baut die Seite (A–C). Was andere
Meilensteine liefern, steht dort und wird hier nur benutzt: `diagnostics.toml` und die Doku-Lints
(`missing-docs`, `broken-doc-link`) aus M12a S3/S7, `lyric doc` und die Doc-Tests aus M13a, die
Archive aus M15, der Inhalt des Books aus M17, die Version `5.0` aus M18.

### Architektur

| # | Entscheidung | Warum — und warum nicht die Alternativen |
|---|---|---|
| P1 | **Ein Renderer: `src/Lyric5.Doc`**, eine Bibliothek mit Thema (Layout, CSS, JS, Schriften als eingebettete Ressourcen), Markdown, Hervorhebung und den Seitenformen (Book-Seite, Referenzseite, Startseite). `lyric5` benutzt sie für `lyric doc`, `tools/Site` für die ganze Seite | C1 verlangt ein Aussehen; zwei Renderer laufen auseinander. Verworfen: `lyric doc` gibt JSON aus und ein externer Renderer macht HTML — ein Nutzerpaket bekäme dann aus einem Binary keine Doku (E7) |
| P2 | **Vorlagen als C#-Funktionen**, die HTML schreiben (wie DocGens `Template.cs`), keine Template-Sprache | NativeAOT: Razor braucht Laufzeitkompilierung, Scriban ist eine Abhängigkeit für vier Seitenformen |
| P3 | **Markdown mit Markdig** (heute schon im Baum). **Risiko**: Markdig unter NativeAOT im `lyric5`-Binary — S1 misst Warnungen und Größe; schlägt es fehl, rendert `lyric doc` über einen eigenen CommonMark-Teil (Absätze, Code, Listen, Links, Überschriften) | ein eigener Parser für das Ganze wäre ein Projekt für sich |
| P4 | **Hervorhebung** über den Lexer aus `Lyric.Frontend`: Tokenarten → CSS-Klassen (`kw`, `ty`, `str`, `num`, `com`), die Farben aus B9 im CSS. Nur Lyric wird hervorgehoben; TOML, Shell, C bleiben einfarbig | C2; eine Grammatik. Weitere Sprachen wären fremde Grammatiken für Nebensachen |
| P5 | **Book-Format**: Markdown unter `docs/book/`, die Reihenfolge in `docs/book/SUMMARY.md` (mdBook-Form). Snippet-Arten wie rustdoc: ` ```lyric ` übersetzt und läuft, `no_run` übersetzt nur, `compile_fail` muss mit dem genannten Code scheitern (` ```lyric,compile_fail=LYR-SEM0111 `). Ein Snippet ohne `fn main` wird in eines gelegt; Importe schreibt es selbst. Erwartete Ausgabe als Block `output` direkt darunter | mdBook und rustdoc sind bekannt; die Fehlerart mit Code prüft mehr als rustdocs bloßes `compile_fail` |
| P6 | **URLs**, je Version stabil: `/<v>/book/<kapitel>/`, `/<v>/std/<modul>/<Typ>/` mit Ankern `#method.push`, `/<v>/spec/<kapitel>/`, `/<v>/diagnostics/<CODE>/`, `/<v>/releases/`. `lyric explain` und das LSP-`codeDescription.href` (G9) zeigen auf `/<v>/diagnostics/<CODE>/` der eigenen Version, `dev` auf `/dev/` | eine URL, die ein Werkzeug druckt, muss halten; Verzeichnisse mit `index.html` wie heute |
| P7 | **JS als drei kleine Dateien** ohne Framework (Thema, Navigation, Versionswahl) plus Pagefinds UI; die Seiten funktionieren ohne JS (C7) | — |

### Slices

| Slice | Inhalt | Artefakt, Prüfung | Größe |
|---|---|---|---|
| **K1** Ratchet | Ein Test in `Lyric5.Tests` zählt die Kommentare mit Entscheidungsbezug je Bereich (`stdlib5` `///`, `stdlib5` `//`, `runtime/`, `src/Lyric5*`) gegen `tooling/comment-ratchet.toml`; rot, wenn eine Zahl steigt, mit den neuen Fundstellen; die Zahlen sinken mit jedem Aufräum-PR | ein neuer Kommentar mit `(10 C3)` macht den Test rot (Kontrolle) | S |
| **K2a** std I | die `///` von `core`, `iter`, `collections`, `string`, `fmt`, `math`, `hash` in W8-Form: erster Satz, Beschreibung, `# Panics`, `# Errors`; Querverweise als `[Typ.member]` | **die emittierte C der std bleibt bis auf die `#line`-Angaben gleich** (Beweis: nur Kommentare geändert — die Zeilennummern verschieben sich mit); `broken-doc-link` grün | M |
| **K2b** std II | dasselbe für die übrigen Module (io, fs, net, process, http, json, task, sync, time …) und die `//` in `stdlib5` | wie K2a | M |
| **K2c** Laufzeit, Compiler, Tests | `runtime/`, `src/Lyric5*` und `tests/Lyric5.Tests` ohne Entscheidungsbezüge (etwa 1060 Zeilen; geteilt 2026-10-20, als der Wächter Frontend und Tests mitzählte) | Bau und Suiten unverändert grün; der Wächter dieser Bereiche auf 0 | M |
| **K2d** Frontend | `src/Lyric.Frontend` ohne Entscheidungsbezüge (etwa 1630 Zeilen); danach steht der Ratchet überall auf 0 und wird ein harter Wächter | wie K2c; der Wächter rot bei einem neuen Fund | M |
| **S1** Renderer und Thema | P1–P4, P7: `Lyric5.Doc` mit Layout, CSS aus B1–B10, Schriften, JS; Startseite (B8) und Releases (aus `CHANGELOG.md`) über `tools/Site`; Ausgabe in ein Verzeichnis je Version; der AOT-Test aus P3 | Goldens je Seitenform; Linkprüfung; **keine externe Anfrage** (kein `http(s)` in `<link>`, `<script>`, `@font-face`); Kontrast jedes Farbpaars aus B3 ≥ 4,5:1 als Test | M |
| **S2** Book-Gerüst | P5: `SUMMARY.md`, die zweispaltige Seite (B10), Weiter/Zurück, die Snippet-Prüfung in der Suite; zwei echte Kapitel (Getting started, A first program) | jedes Snippet läuft; ein falsches `output` wird rot (Kontrolle) | M |
| **S3** Spec und Katalog | die Spec aus `lyric-spec` am `spec.pin`, der Katalog aus `diagnostics.toml` (eine Seite je Code, P6) | jeder Code des Katalogs hat eine Seite; `lyric explain` nennt die URL | S |
| **S4** Suche | C6: Pagefind versioniert und hash-geprüft, ein Index je Version, das Suchfeld der Kopfzeile (B7) | eine Suche nach einem std-Namen, einem Code und einem Wort aus dem Book trifft | S |
| **S5** Versionen und Legacy | C4, C5: `versions.json` mit dem Eintrag Legacy; ein Skript, das auf `gh-pages` die `vX.Y.Z/` nach `/legacy/` zieht, die Übersicht schreibt, `/nightly/` entfernt, die `404.html` mit der Umleitung anlegt. **Der Lauf auf `gh-pages` geschieht auf das Wort des Maintainers** (wirkt außerhalb des Repos) | jede alte Seite hat ihr Ziel unter `/legacy/` (Prüfung über den Baum); Stichproben alter Links leiten richtig | S |
| **S6** CI und Abschied von DocGen | C8: `site.yml` (PR baut und prüft, `main` → `/dev/`, Tag → Version); `/` leitet bis 5.0 auf `/dev/`; `docs.yml`, `tools/DocGen`, `Lyric.Tests.DocGen` fallen | `/dev/` live mit Startseite, Book-Gerüst, Spec, Katalog, Suche, Legacy | S |
| **in M13a** | `lyric doc` auf `Lyric5.Doc`: `///` aus der Sema des Lyric-5-Frontends, die Referenzseite mit drei Spalten, Intra-Doc-Links, die Doc-Tests, die `# Examples` der std; die std-Doku geht in die Seite | „Doc-Site der std“ (13, M13a) ist die std unter `/dev/std/` | — |
| **in M15** | der Mirror (C9): VPS härten, `dl.lyric.oil1i.de`, Upload im Release-Job, die Kanalliste für `lyric toolchain` | `lyric toolchain install dev` lädt vom Mirror, fällt auf GitHub zurück | — |
| **in M17/M18** | M17: der Inhalt des Books. M18: `/5.0/` eingefroren, `/` zeigt auf das Stable | — | — |

**Reihenfolge**: K1 sofort (während M12a) → M12a, M12b → K2a, K2b, K2c, K2d → S1 → S2, S3, S4 (in
beliebiger Folge) → S5 → S6 → M13a. S5 vor S6: die erste Veröffentlichung der neuen Seite darf die
alte nicht überschreiben.

### Tests

Goldens je Seitenform (`LYRIC_UPDATE_SNAPSHOTS` wie überall); eine Linkprüfung über die ganze
gebaute Seite mit Ankern; der Wächter gegen externe Anfragen; der Kontrasttest über die Palette;
die Snippets des Books und (ab M13a) die Doc-Tests in der Suite; für K2 die unveränderte C der std
(ohne `#line`).
Nicht automatisch: das Verhalten bei schmalem Fenster — S1 und S2 prüfen es von Hand bei 360,
900 und 1440 px und legen Bildschirmfotos in den PR.

### Umfang

`Lyric5.Doc` etwa 2000 Zeilen C# und 600 CSS/JS, `tools/Site` etwa 800, die Book-Prüfung etwa
500, K1 etwa 150; K2 schreibt rund 3350 Kommentarzeilen um, ohne Code. Dazu fallen DocGen und
seine Tests (etwa 1700 Zeilen). Zusammen **L an der oberen Kante**: K1, S3–S6 klein, K2a–K2d, S1, S2 mittel.

### Reflexion

- **Rust** (rustdoc und mdBook): Vorbild für die Doc-Form (W8), die Snippet-Arten (P5) und die
  Teilung Book/Referenz. Rust hat dafür zwei Werkzeuge mit zwei Themen; hier teilen beide einen
  Renderer (P1).
- **Go** (pkg.go.dev): ein Dienst mit Datenbank, der Paketdoku erzeugt — verworfen, das ist Betrieb.
  `go doc` im Terminal entspricht `lyric doc`.
- **Zig**: die Sprachreferenz als eine HTML-Seite, die std-Doku als Wasm-App im Browser — schnell
  gebaut, ohne JS aber leer; verworfen wegen C7.
- **Swift** (DocC): JSON plus eine Single-Page-App — dieselbe Ablehnung, schwerer.
- **Sphinx**: ein Werkzeug für alles, aber eigene Markup-Sprache und Plugin-Welt; Markdown und ein
  eigener Renderer bleiben kleiner.

## E — Einordnung in 13: **entschieden** (2026-10-20)

| # | Entscheidung | Verworfen |
|---|---|---|
| E1 | **M12c „Doku-Grundlage und Website“**, Größe L, zwischen M12b und M13a: K2a–K2d und S1–S6. Artefakt: `/dev/` live, der Kommentar-Wächter auf 0 | alles in M13a — es wüchse von M auf XL, mehr als 100 % über seiner Größe (CONTRIBUTING) |
| E2 | **K1 sofort**, als eigener PR während M12a | K1 erst mit M12c — bis dahin wüchsen die Funde mit M12a und M12b weiter |
| E3 | **M12a S11** schreibt sein Kapitel als Markdown-Entwurf eines Book-Kapitels unter `docs/book/`; S2 rendert es | in den 4.x-Guide schreiben, den es nach W6 nicht mehr gibt |
| E4 | Die übrigen Stücke bleiben in ihren Meilensteinen: `lyric doc` und die Doc-Tests in M13a, der Mirror in M15, der Inhalt des Books in M17, `/5.0/` in M18 | — |
