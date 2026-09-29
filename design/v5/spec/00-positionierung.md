# 00 — Positionierung und Zielbild

Entschieden 2026-09-28 (Maintainer, im Dialog). Erster Bereich der Lyric-5-Designrunde; alles
Weitere baut darauf. Die Regeln aus `CONTRIBUTING.md` und `STATUS.md` §Design decisions gelten
für Lyric 5 nicht — sie haben Lyric 4 getragen und werden hier nicht zitiert, sondern neu
entschieden.

## Entscheidungen

| # | Frage | Entscheidung |
|---|---|---|
| Z0 | Was ist unverhandelbar? | **Statisch typisiert.** Sonst nichts. |
| Z1 | Zielplattform der Laufzeit | **D — nativ.** Der Compiler bleibt ein C#-Werkzeug (Frontend wie heute), das Backend emittiert **C**; ein C-Compiler (`zig cc`, clang, gcc) erzeugt die Binary. Ein Lyric-5-Programm ist eine native Binary ohne Laufzeitinstallation, kein `.lyrbc`, keine .NET-Assembly. |
| Z2 | Was heißt Performance, messbar? | **Schneller als Python, Richtung C#/Go** (Faktor 1,2–3 zu C auf Alltagscode als Ziel), **Tür zur C-Klasse offen** — der C-Backend hält sie auf. Ein Interpreter, wie gut auch immer, erreicht das nicht; die Klasse setzt kompilierten Maschinencode voraus. |
| Z3 | Primärer Einsatz | **Applikation, CLI, Server.** Die Rollen Sandbox, Instruktionsbudget, Hot-Reload und Einbettung als Mod-Sprache gehen an Lyric-Script. Lyric 5 darf deshalb, was eine Sandbox nie erlaubt hätte: Threads, direkte FFI, rohe Zeiger, falls spätere Bereiche es wollen. |
| Z4 | Die zwei Flavors | **Lyric 5** ist das neue Lyric. **Lyric-Script** ist Lyric 4 auf der bestehenden C#-VM, **darf sich weiterentwickeln, um skriptartiger (python-ähnlicher) zu werden**, mit eigener Roadmap. Script-Module in Lyric-5-Programmen sind eine Zusatzfähigkeit (Bereich 11), keine Voraussetzung. |
| Z5 | Ökosystem-Anker | **C-ABI ist die Muttersprache** der FFI; .NET-Interop ist keine Kernfähigkeit. Die Standardbibliothek setzt auf einer eigenen C-Schicht auf, nicht auf der BCL. |
| — | Release-Politik | **5.0 ist auf absehbare Zeit der letzte Major** (Maintainer, präzisiert 2026-09-29: ein 6 ist nicht ausgeschlossen, nur nicht in Sicht). Patches und Minors folgen, seltener als in 2.x–4.x. |
| — | Migration | **Harter Cut.** Migrationswerkzeug, wo mechanisch möglich; manuelle Migration akzeptabel (Korpus: stdlib, Beispiele, Erato). Keine 4.x-Warnstufen für 5. |
| — | Erato | Abnehmer, nicht Voraussetzer. |

## Begründung

Drei Aussagen zeigen gemeinsam auf D: die Tür zur C-Klasse soll offen bleiben (auf der CLR
endet sie bei NativeAOT, also in der C#-Klasse), die Identität soll Lyrics eigene sein (eine
.NET-Assembly mit fremder Laufzeit ist es nicht), und **stackful Koroutinen** — Lyric 4s
Nebenläufigkeitsmodell ohne Funktionsfärbung — gibt es auf der CLR nicht, auf einer eigenen
Laufzeit für den Preis einer Assembler-Routine. Go beweist, dass Stackful in der Zielklasse geht;
Java 21 hat es nachgerüstet, weil gefärbte Bibliotheken die Plattform gespalten hatten.

Der Weg ist für einen KI-unterstützten Solo-Entwickler tragbar, weil das Schwere geborgt bleibt:
das Frontend existiert, der Optimierer ist clang/gcc, die Portabilität ist die von C. Was
selbst gebaut wird, ist die Laufzeitbibliothek in C (Strings, Container, I/O), die Koroutinen,
die Fehler-ABI und der GC — der GC gestuft (Bereich 1).

Die Flavor-Aufteilung ist keine Verlegenheit, sondern die Antwort auf zwei unvereinbare Rollen:
die 4.x-VM kann Sandbox, Budget und Hot-Reload und wird nie schnell; die native Sprache wird
schnell und kann keine Laufzeitgrenze ziehen. Jede Rolle bekommt die Laufzeit, die sie kann.

## Verworfene Alternativen

| Option | Warum nicht |
|---|---|
| **A — CLR als Ziel** (IL-Emission, RyuJIT, NativeAOT) | Der billigste Weg zur Zielklasse — Optimierer, GC, Wertlayout geschenkt; F#, IronPython, ClojureCLR als Präzedenz. Verworfen wegen: kein Stackful, Identität als .NET-Assembly, Laufzeitabhängigkeit (framework-abhängig: DLL-Verzeichnis; AOT: mehrere MB mit .NET-Innenleben), Tür zur C-Klasse faktisch zu. Bleibt die Referenz dafür, was D **mindestens** erreichen muss. |
| **B — eigene VM in C#, Register + typisierte Werte** | Faktor 3–5 zu heute, Lua-Klasse; verfehlt Z2. Lyric 4 mit besserem Motor. |
| **C — eigene native VM mit eigenem JIT** | Ohne JIT Lua-Klasse; mit JIT ein Lebenswerk (LuaJIT). |
| **E — WebAssembly** | Cranelift-Qualität und Sandbox eingebaut, aber WasmGC, Stack-Switching und Interop 2026 nicht tragfähig. Als zweites Backend später denkbar; der C-Backend schließt es nicht aus. |
| **LLVM-IR statt C** | Mehr Kontrolle, aber DWARF, Unwinding und Cross-Builds selbst; C gibt `#line`-Debugging und jeden Compiler gratis. Tür bleibt offen. |

## Was das den späteren Bereichen vorgibt

- **Bereich 1 (Laufzeit)**: GC gestuft — Runtime-Schnittstelle zuerst (Allokation, Typdeskriptoren, Barrier-Hooks, Pinning, Wurzeln), Boehm dahinter, dann eigener Tracing-GC mit **präzisem Heap und konservativem Stack** (C-Compiler liefern keine Stack-Maps; Alternative Shadow-Stack kostet 5–10 %), generational über Sticky-Mark-Bits als Folge. Stackful Koroutinen mit eigenen Stacks, vom GC gescannt.
- **Bereich 2/3 (Werte, Typen)**: Inline-Layout ist möglich und zu entscheiden — Structs als echte C-Structs, ungeboxte Generics per Monomorphisierung; kein `LyrValue` mehr.
- **Bereich 5 (Fehler)**: C hat keine Exceptions; `setjmp/longjmp` verträgt sich schlecht mit GC und Koroutinen. Die natürliche Form ist die **Fehlerrückgabe-ABI** (Zig, Go, Swift unter der Haube) — `throws` kann syntaktisch bleiben und dazu kompilieren.
- **Bereich 6 (Nebenläufigkeit)**: Stackful ist verfügbar; Threads sind es auch. Neu zu entscheiden, nicht mehr durch die Laufzeit begrenzt.
- **Bereich 11 (Werkzeuge, Interop)**: `extern "C"` ist keine FFI, sondern die ABI selbst. Script-Nesting über CoreCLR-Hosting (opt-in, zieht .NET in den Prozess) oder eine native Script-VM — auch als spätere Neuschreibung der 4.x-VM in Lyric 5.
- **Entwicklungsumgebung**: Laufzeitarbeit unter Linux/WSL2 (ASan, UBSan, valgrind, perf, gdb — für GC- und Koroutinen-Fehler unverzichtbar), Repo im WSL-Dateisystem, Windows als gleichrangiges CI-Ziel ab Tag eins, `zig cc` als C-Compiler auf beiden Seiten (Cross-Build, keine MSVC-Abhängigkeit).

## Offen, an andere Bereiche verwiesen

- GC-Design im Detail, Refcount als Alternative (Bereich 1).
- Was Lyric-Script wird — eigene Roadmap, nicht Teil dieser Runde.
- Ob und wie Lyric 5 die CLR hosten darf (Bereich 11).
