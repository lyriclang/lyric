# 01 — Ausführungsmodell und Laufzeit

Lebendes Dokument des Bereichs 1. Jede Frage L1–L12 bekommt einen Abschnitt; **entschieden** oder
**offen** steht in der Überschrift. Basis: `00-positionierung.md` (nativ über C-Backend,
Compiler bleibt C#, C#/Go-Klasse, stackful Koroutinen möglich).

## Bestandsaufnahme Lyric 4 (zur Erinnerung, was nicht übernommen wird)

`LyrValue` = `ulong Bits` + `object? Ref`, 16 Bytes je Wert; Objekt und Struct = `LyrValue[]`
(16 B je Feld) mit `structcopy`; String = .NET-`string`; Optional über Null/Marker (`??T`
unmöglich); Interface = Fat Pointer; Stack-VM mit ~23 Zyklen je Dispatch; gepoolte
`Frame`-Objekte, `MaxCallDepth` 1024; stackful Koroutinen über `CoroutineChain` (Frames werden
beim Yield eingesammelt); Handler-Tabellen je Funktion; kein eigener GC (.NET); JIT opt-in
(lehnt Closures, Rekursion, `yield`, Wurf, Enums ab); `.lyrbc` 4.0. Nichts davon überlebt Z1;
bewährt haben sich die Formen: stackful, Handler-Tabellen, Fat-Pointer-Interfaces.

## L1 — Speicherverwaltung: **entschieden** (2026-09-28)

**Tracing-GC, gestuft, nicht bewegend, präziser Heap mit konservativen Stacks.**

Randbedingungen, aus denen es folgt: der C-Backend liefert keine Stack-Maps; stackful
Koroutinen bedeuten viele Stacks; die C-ABI hält Zeiger außerhalb; die Zielklasse verlangt
Bump-Allokation inline.

| Achse | Entscheidung | Begründung |
|---|---|---|
| Wurzeln | **Stack konservativ** (Haupt- und jeder Koroutinen-Stack, wortweise, Innenzeiger zählen), **Heap präzise** über Typdeskriptoren | kein Shadow-Stack (5–10 % Prolog), kein Stack-Map-Problem; Präzedenz Ruby ≥2.7, JavaScriptCore |
| Bewegen | **nie** — die eine Tür, die zugeht | konservative Wurzeln, C-Innenzeiger und FFI verlangen sonst Pinning überall; „Zeiger sind stabil" macht jede FFI-Regel trivial |
| Heap-Organisation | **Immix-Mark-Region**: 32-KB-Blöcke, 256-B-Zeilen, Größenklassen, Large-Object-Space; Bump-Allokation in Zeilen; Sweep faul je Zeile | Bump-Geschwindigkeit ohne Bewegen; Fragmentierung über Zeilenrecycling |
| Generational | **ja, Stufe 3, über Sticky-Mark-Bits** (kein Kopieren; Mark-Bits bleiben stehen, junger Zyklus markiert ab geänderten alten Objekten) | verträgt sich mit nicht-bewegend |
| Nebenläufig | **Stufe 4** (Go-Hybrid-Barrier, paralleles Sweeping), Ziel Pausen im ms-Bereich; **Minor nach 5.0** | dieselbe Barrier-Stelle wie Stufe 3, keine Backend-Änderung |
| Finalizer | **nein** (klassisch: Wiederbelebung, Extra-Zyklus, undefinierte Reihenfolge) | Java deprecated, Go rät ab |
| Weak References | **ja**, mit **Rückruf nach dem Tod** (Java-`Cleaner`-Form: die Aktion sieht nur den rohen Handle, nie das Objekt) | das Netz für vergessene FFI-Handles; gratis im Tracing-GC |
| Ressourcenfreigabe (Sprache) | deterministisch: `defer` plus **typgebundene Scope-Freigabe** (Form entscheidet Bereich 5); Debug-Profil warnt beim Tod eines nicht freigegebenen Ressourcen-Objekts | Destruktoren auf Werttypen (RAII) sind eine Bereich-2-Tür |
| Refcount | **nein** als Grundmechanismus (Zählverkehr auf dem schnellen Code, atomar unter Threads, Zyklen als Sammler oder Nutzerbürde); opt-in „besitzende Werttypen" ist eine Bereich-2-Frage | Koka/Nim/Swift geprüft |

**Verworfen**: Boehm als Endzustand (konservativer Heap, Fragmentierung, langsame Allokation —
bleibt Stufe 1); Shadow-Stack (Prologkosten); bewegende Sammler G1/ZGC (Expertenwerk,
FFI-Pinning); MMTk (Rust im Runtime, Binding-Vertrag so groß wie ein eigener einfacher GC);
Perceus/ORC/ARC (siehe Refcount).

### Die Stufen

| Stufe | Inhalt | Im emittierten C sichtbar |
|---|---|---|
| 0 | **Runtime-Vertrag**: `lyr_alloc(desc, size)`; Typdeskriptor (Größe, Referenzfeld-Bitmap, VTable-Anker); `LYR_WRITE_BARRIER(obj, field, val)`; `LYR_SAFEPOINT()`; Stack-Registrierung (Thread- und Koroutinen-Stacks); `lyr_pin`/`lyr_unpin` als No-Op-Platzhalter der FFI-Regel; Weak-Handles mit Rückruf | Barrier-Makro, Poll-Makro, Deskriptor je Allokation — **ab Tag eins emittiert** |
| 1 | Boehm dahinter; Barrier und Poll leer | — |
| 2 | eigener GC: Immix-Heap, präzises Marking, konservativer Scan aller registrierten Stacks, Stop-the-World | Bump-Fast-Path inline (~5 Instruktionen), Slow-Path-Aufruf |
| 3 | Sticky-Mark-Bits generational; Barrier wird echt (Remembered-Set) | Barrier-Makro bekommt Körper |
| 4 | nebenläufiges Marking, paralleles Sweeping | Barrier-Körper ändert sich, Polls tragen Phasenwechsel |

**5.0 liefert Stufe 3.** Stufe 4 ist ein Minor.

## L6 — Threads im Runtime: **entschieden** (2026-09-28)

**Thread-fähig ab Vertrag 0, unabhängig davon, ob Bereich 6 Threads anbietet.** Drei Dinge,
die man nicht nachrüsten kann und in der Ein-Thread-Version nichts kosten:

1. **Allokation pro Thread** — jeder Thread besitzt seinen aktuellen Immix-Block; Bump-Pointer
   thread-lokal, kein Lock im Fast-Path.
2. **Wurzeln pro Thread** — eine Liste registrierter Stacks, nicht „der Stack".
3. **Safepoint-Polls im emittierten C** — Load und Branch auf ein globales Flag an
   Schleifenrücksprüngen und Funktionseingängen (~1 %). Stop-the-World hält jeden Thread an
   bekannter Stelle an; Signale sind mit einem C-Backend riskant (unbekannter Compilerzustand).
   Nebeneffekt: dieselben Polls tragen kooperative Koroutinen-Präemption oder ein Zeitbudget,
   falls L10 es will.

## L2 — Wertdarstellung: **entschieden** (2026-09-28)

**Ein Wert kostet, was er ist.** Unter L1 (nicht bewegend, präziser Heap, konservativer Stack):

| # | Wert | Darstellung | Verworfen |
|---|---|---|---|
| V1 | Skalare | C-Typen direkt: `int64_t`, `double`, `uint8_t` (bool), `uint32_t` (char = Code-Punkt); natürliche Ausrichtung | — |
| V2 | Struct | **C-Struct by value**, Felder inline, verschachtelt inline; Übergabe by value (clang reicht große Structs selbst per Zeiger) | Heap-Objekt mit Kopie (Lyric 4) |
| V3 | Klassenobjekt | **ein Wort Header**: Zeiger auf den Typdeskriptor; Mark-Bits in Immix-Seitenmetadaten; **Identitäts-Hash = Adresse** (nichts bewegt sich; nicht reproduzierbar zwischen Läufen — akzeptiert) | Java/C#-Header 12–16 B |
| V4 | Typdeskriptor | statisch je Typ: Größe, Referenz-Bitmap, Name/Modul, Anker für Interface-Tabellen; **Typidentität = Deskriptor-Zeiger** — Downcast/Typ-Pattern auf Interface-Werten ist ein Zeigervergleich | — |
| V5 | `?T` | Referenz-`T`: Null-Zeiger (Niche, 0 B); Enum: Niche im Tag; sonst `{T; bool}`. **`??T` ist darstellbar**; ob erlaubt, entscheidet Bereich 3 | — |
| V6 | Enum mit Nutzlast | **Tag + Union inline** (Rust); rekursive Nutzlast braucht `?` oder eine Klasse als Indirektion | Heap-Objekt je Variante |
| V7 | Interface-Wert | **Fat Pointer** {Daten, VTable}, 16 B; VTable statisch je (Typ, Interface); ein Struct wird beim Übergang **geboxt** (passt es in ein Wort, liegt es im Datenslot); bei Constraints wird monomorphisiert, dann gibt es keinen Interface-Wert | VTable im Header (nur Klassen); Swift-Inline-Puffer (Kopien je Übergabe, drei Wörter) |
| V8 | Closure | {Funktionszeiger, Umgebung}, 16 B; Umgebung Heap-Objekt, erstes Argument; freie Funktion = Closure mit Null-Umgebung. Capture-Semantik (Bereich 2) trägt beides: by-ref = Box, by-value = Kopie | — |
| V9 | String | **ein Wort**: Zeiger auf {Header, Länge, UTF-8-Bytes inline}; unveränderlich. Substrings als eigener **View-Typ** {Innenzeiger, Länge} — gratis und sicher, weil unbeweglich und Innenzeiger verstanden werden; ob die Sprache ihn hat: Bereich 3/10 | Go-{ptr,len} (16 B je Feld, String = View); SSO (verwirrt den Scan, Branches überall) |
| V10 | Array | {Header, Länge, Elemente inline}; **Struct-Elemente zusammenhängend** (`Vec3[]` ist ein Block); Referenz-Elemente sind Zeiger. Views {Innenzeiger, Länge} gratis — die 4.x-Absage an Slices beruhte auf einer VM ohne Innenzeiger | — |
| V11 | Tupel | anonymes C-Struct inline | — |
| V12 | Generics | **Monomorphisierung**, konkrete Layouts, kein Boxing; Sharing über Referenztypen (C#) als spätere Optimierung ohne Layoutänderung | Erasure (Java), Witness Tables (Swift) |

Größen: `bool` 1, `int` 8, `Vec3` 24, Referenz 8, Interface 16, `?int` 16, `?Vec3` 32,
`?Klasse` 8. Lyric 4: 16 für alles plus Heap je Struct.

**Folgen für andere Bereiche**: Innenzeiger und Typidentität öffnen Views und Typ-Patterns
(Bereich 3); Struct-Kopien sind Speicherkopien — ob große Structs eine Kostenregel bekommen,
ist Bereich 2.
## L3 — Aufrufkonvention und Stack: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| S1 | Lyric-Funktionen sind **C-Funktionen der Plattform-ABI** (SysV, Win64, AAPCS64); Structs by value, Tupel als Struct-Rückgabe; versteckte Parameter nur wo ein Feature sie braucht (Closure-Umgebung als erstes Argument, Fehlerslot nach L5). Jede exportierte Funktion ist ohne Adapter aus C aufrufbar. | eigene Konvention |
| S2 | Locals sind C-Locals; kein Frame-Objekt, kein Pool, kein Operandenstapel | — |
| S3 | **Überlauf = Stack-Pointer-Prüfung im Prolog** gegen eine thread-lokale Grenze (ein Vergleich, mit dem Safepoint-Poll verschmelzbar), **Guard-Page + Signal-Handler als Netz**; Überlauf ist eine **Panik mit Backtrace**, kein Segfault | Tiefenzähler (zählt Frames statt Bytes) |
| S4 | **`main` auf dem OS-Stack**, Größe per Linker-Flag auf **8 MB** auf allen Plattformen, Grenzen beim Start ermittelt (`pthread_getattr_np`/`VirtualQuery`). Begründung: ein laufzeiteigener Hauptstack kauft Uniformität und zahlt mit TEB-Umschreibung unter Windows (SEH, `__chkstk`) und Reibung in gdb/ASan/valgrind — ausgerechnet dort, wo man am meisten debuggt | laufzeiteigener Hauptstack (zuerst empfohlen, revidiert) |
| S5 | Tail Calls (`musttail`): Tür, kein Plan | — |

## L4 — Koroutinen im Runtime: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| K1 | **Feste Reservierung, träges Commit**: `mmap`/`VirtualAlloc` reserviert, Seiten werden beim Zugriff physisch; **256 KB Standard**, je Erzeugung konfigurierbar; Stacks nach dem Tod gepoolt. Unter Windows mit `PAGE_GUARD` und TEB-Update (`StackBase`/`StackLimit`/`DeallocationStack`), ASan-Annotation beim Wechsel | wachsende Stacks (Kopieren — unmöglich mit konservativem Scan und C-Zeigern in den Stack); segmentierte Stacks (Hot-Split, von Go und Rust verworfen) |
| K2 | **Eigene Assembler-Routine je ABI** (callee-saved Register + SP, ~20 Instruktionen): x86-64 SysV, x86-64 Win64, AArch64; ~10–20 ns je Wechsel | `ucontext` (Syscall, deprecated), Windows Fibers (nur dort) |
| K3 | Jede Koroutine registriert ihre Stackgrenzen (L6); suspendiert wird vom gesicherten SP bis zur Basis gescannt, die Register liegen dort | — |
| K4 | **Yield durch C-Frames ist erlaubt** (ein Callback läuft auf dem Stack der Koroutine; mechanisch harmlos). Die Laufzeit führt je Koroutine einen **Fremd-Frame-Zähler** (FFI-Eintritt/-Austritt); daran hängen zwei Regeln: **kein Abbruch und keine GC-Aufgabe** einer Koroutine mit Zähler > 0 ohne Fehler/Warnung (C-Frames lassen sich nicht abwickeln), **keine Thread-Migration** bei Zähler > 0 (TLS, GL-Kontexte, thread-gebundene Mutexe; Go pinnt während cgo). Nicht-reentrante Bibliotheken und gehaltene Locks während eines Yields sind dokumentierte Verantwortung, nicht prüfbar | Lua-Verbot „yield across C-call boundary" — würde jeden Callback-Event-Loop unbenutzbar machen |
| K5 | **asymmetrisch** als Primitiv (`yield` kehrt zum Resumer zurück); symmetrisch darüber baubar | — |
| K6 | **kooperativ**; Präemption über die Safepoint-Polls bleibt Tür | — |
| K7 | Die Laufzeit liefert nur Primitive: `create(fn, stackSize)`, `resume`, `yield`, `status`, TLS „aktuelle Koroutine"; Scheduler, Tasks, Channels, M:N sind **Bereich 6** | — |
| K7a | **Nachtrag (Bereich 10 I7)**: `close(co)` auf einer schwebenden Koroutine wickelt ihren Stack ab (Resume mit Abbruchsignal, `defer`/`using` laufen, kein weiteres `yield` erlaubt → Panik); Grundlage für `Coroutine<T> :: [Closeable]` | Python `GeneratorExit`, Kotlin Cancellation |

Gegenüber Lyric 4: dasselbe Modell (stackful, Helfer dürfen anhalten), billiger (kein
Frame-Einsammeln beim Yield), und neu: Yield aus einem Callback, der durch C läuft. Die eine
Tür, die zugeht: wachsende Stacks — wer viele tief rekursive Koroutinen will, konfiguriert die
Reservierung.

## L5 — Fehler-ABI: **entschieden** (2026-09-28)

**Fehlerrückgabe** (Status + Slot), wie Swift, Zig, Go. Erzwungen durch zwei frühere
Entscheidungen: `setjmp`/`longjmp` überspringt Frames (zerstört `defer`, undefiniert über
Koroutinen-Wechsel), tabellenbasiertes Unwinding kann keine C-Frames abwickeln — und K4 erlaubt
C-Frames mitten im Stack. Nebeneffekt, der Bereich 5 prägt: **ein Wurf kostet wie ein Return**;
die Zwillingsdoktrin (`OrThrow`-Paare, `TryParse`) verliert ihren Grund.

| # | Entscheidung | Verworfen |
|---|---|---|
| E1 | **Swift-Form in C**: der normale Rückgabewert bleibt der C-Rückgabewert; jede werfende Funktion bekommt einen **versteckten Zeigerparameter auf den Fehlerslot des Aufrufers**; Status = Slot nicht null. Nicht TLS (Zugriffskosten, Koroutinen teilen Threads). | Zig-Form (Status als Rückgabe, Wert per Out-Parameter — verunstaltet Signaturen, verliert Registerrückgabe); `Result`-Struct als Rückgabe |
| E2 | Fehlerwert = **Zeiger auf Heap-Objekt mit Typdeskriptor**; Typtest = Deskriptorvergleich (V4); Allokation nur beim Wurf | Inline-Fehlerwerte (machen `catch (e: T)` zum Layoutproblem) |
| E3 | Prüfstelle nach jedem werfenden Aufruf: `if (unlikely(err)) goto L_cleanup_N;` — Cleanup-Labels in umgekehrter `defer`-Reihenfolge, am Ende Weitergabe in den eigenen Slot (Zig-Emission) | — |
| E4 | `defer` beim Wurf läuft über die Cleanup-Kette; **wirft ein `defer` während der Weitergabe, gewinnt der erste Fehler, der zweite wird angehängt** (Java `addSuppressed`) — beantwortet SPEC-RUNDE 5 als ABI-Frage | Go (Panik im `defer` ersetzt) |
| E5 | **Paniken sind keine Fehler**: Index, Division durch null, Überlauf, Stacküberlauf → Meldung, Backtrace, Prozessende; Prozess-weiter Hook für Hosts. Ob es ein `recover` an Koroutinengrenzen gibt, entscheidet Bereich 5 — die ABI erlaubt es (Panik als Wurf einer nicht fangbaren Klasse an der Grenze) | Panik als gewöhnliche Exception |
| E6 | C hinein: `extern "C"` wirft nie; ein C-Status wird vom Aufrufer übersetzt (Bibliothekssache) | — |
| E7 | C hinaus: ein Wurf **darf C nicht durchqueren**. Standard: Callback-Typ ist nicht-werfend (Compiler erzwingt). Explizit: Wrapper fängt, legt den Fehler in der Koroutine ab, gibt C einen Status; nach Rückkehr aus C wird er weitergeworfen (Form: Bereich 11) | — |
| E8 | **Backtrace auf Fehlerobjekten nur im Debug-Profil** (libbacktrace / `RtlCaptureStackBackTrace`); Release nur, wenn der Typ es verlangt; Paniken immer mit Trace | Go/Rust-Linie |

## L7 — Was `.lyrbc` ersetzt: **entschieden** (2026-09-28)

**Die Quelle ist das Format.** Kein stabiles Binärmodul in 5.0; die Stabilitätszusage liegt auf
Sprache und Quelle, nicht auf Bytes (Go, Zig, Nim, Rust/crates.io). Dazu ein **Build-Cache je
Modul** — typisierte Schnittstelle, serialisiertes IR, erzeugtes C, Objektdatei, adressiert über
Inhalts-Hash — der **kein Format verspricht** und mit jeder Toolchain-Version verfallen darf.

| Fall | Antwort |
|---|---|
| Closed Source | **nicht bedient** (Maintainer: nicht wichtig). Ausweg: C-ABI, `lib.a` + `.lyr` mit `extern "C"`. Toolchain-gebundene Artefakte aus dem Cache (Rust-`.rlib`-Stufe): Tür |
| Schnelle Builds | der Cache |
| Plugins zur Laufzeit | native `.so`/`.dll` über die C-ABI, oder Lyric-Script (Z3) |
| Vorgebaute Pakete, Toolchain-übergreifend, Werkzeuge ohne Quelle | Quelle |
| **Ein Programm, zwei Ausführungsarten** (IR interpretieren: Sandbox, Hot-Reload) | **Tür**: das IR wird **interpretierbar gebaut** — serialisierbar, typisiert, selbstbeschreibend, ohne Backend-Annahmen im IR. Kostet Disziplin im Entwurf, **keine Laufzeit im kompilierten Code** (das C entsteht aus demselben IR). **Z4 bleibt**: Lyric-Script ist weiterentwickeltes Lyric 4, nicht interpretiertes Lyric 5 |

**Stabilität**: nie „ewig" — mit „5.0 ist der letzte Major" wäre das ein Versprechen ohne
Ausstieg, das genau einfriert, was sich in Minors entwickeln soll. **Stirbt**: das
`.lyrbc`-Kapitel für Lyric 5, der Verifier als Ladeprüfung, `lyrvm`, `lyrpack`. `.lyrbc` bleibt
das Format von Lyric-Script.

## L8 — C-Emission: **entschieden** (2026-09-28)

| # | Entscheidung | Verworfen |
|---|---|---|
| C1 | **C11** plus `__builtin_expect`, `__attribute__`, `restrict`, `_Thread_local`. **Kein MSVC** als Backend-Compiler; unter Windows `zig cc` oder clang | MSVC |
| C2 | **eine `.c` je Lyric-Modul**; Release mit **ThinLTO**, Debug ohne LTO | Unity-Build als Release-Option: Tür |
| C3 | generische Instanziierungen **whole-program gesammelt**, je Instanz eine Hash-benannte Cache-Einheit, einmal kompiliert (C hat kein COMDAT) | `static` je Einheit |
| C4 | Mangling `lyr_<modul>_<name>` + kurzer Typ-Hash bei Überladung/Instanz; lesbar in gdb und perf | — |
| C5 | Form: aus dem IR, Blöcke + `goto`, Werte in Locals; `if`/`while` wo der Block es hergibt; `__builtin_expect` auf Fehlerprüfungen | — |
| C6 | `#line` überall → gdb/lldb/perf/Sanitizer zeigen `.lyr`-Quelle | — |
| C7 | Runtime `liblyr.a`, **statisch gelinkt** → eine Binary; Debug-Variante mit ASan/UBSan als Profil | dynamische Runtime |
| C8 | **C-Compiler installiert voraussetzen**, Erkennung `zig cc` > clang > gcc, `zig cc` empfohlen; Bündelung: Tür | Bündelung in 5.0 |
| C9 | Cross-Build über das Ziel-Tripel von `zig cc` | — |
| C10 | Der Nutzer sieht das C nie, außer `lyric build --emit-c`; Cache unter `out/` | — |

## L9 — Laufzeitbibliothek: **entschieden** (2026-09-28)

**Die C-Schicht ist so dünn, wie es die Sicherheit erlaubt** — Schätzung 5–10 k Zeilen, die
Hälfte GC. Lyric-Code ist unter D so schnell wie das C daneben und hat Typen, Tests und den
GC-Vertrag umsonst (Go: kleine Runtime, Bibliothek in Go; Rust: `core` in Rust).

| In C | In Lyric |
|---|---|
| GC, Allokation, Weak-Refs, Deskriptoren (L1) | — |
| Stack-Reservierung, Kontextwechsel (L3/L4) | Scheduler, Tasks, Channels (Bereich 6) |
| Panik-Pfad, Backtrace-Erfassung (L5) | Fehlertypen, Formatierung |
| String-Layout, UTF-8-Validierung, Vergleich/Hash (SIMD-fähig), Zahl↔Text | Suche, Split, Case-Mapping |
| **keine Container** (Array-Layout ist Compiler-Sache) | `List`, `Map`, `Set` monomorphisiert |
| dünne Syscall-Hüllen: Datei, Socket, Prozess, Zeit, Zufall, Umgebung — kein Puffern | Reader/Writer, Pfade, JSON |
| Thread-Primitive, TLS, Atomics, Signal-Handler | — |

## L10 — Determinismus und Budget: **entschieden** (2026-09-28)

| | Entscheidung |
|---|---|
| Instruktions-/Zeitbudget | **nein** — Rolle von Lyric-Script. Die Safepoint-Polls bleiben; ein Budget darauf ist eine Tür für Hosts |
| Numerik | **deterministisch als Standard**: IEEE binary64/32 ohne Fast-Math (`-ffp-model=strict`, SSE2/NEON), definierte Integer-Semantik (Bereich 3). **Fast-Math, FMA-Kontraktion u. ä. per Flag/Profil zuschaltbar**, nie stillschweigend |
| „Never a process abort" | **aufgegeben**: Paniken beenden den Prozess (E5), mit Hook für Hosts; Isolation über Lyric-Script oder Kindprozess |

## L11 — Start und Binärgröße: **entschieden** (2026-09-28)

| | Ziel | Wie |
|---|---|---|
| Start | **< 5 ms** Hallo-Welt (Go ~1 ms, .NET-JIT 50–100 ms) | statisch gelinkt, kein JIT, GC-Heap träge, keine leere Modulinitialisierung |
| Größe | **< 2 MB** Hallo-Welt (Go ~2 MB, Rust ~300 KB) | Reachability whole-program auf Funktionsebene (nicht Erreichbares wird nicht emittiert), `-Os`/`-O2` je Profil, Debug-Info getrennt |
| Abhängigkeiten | **null** außer libc; optional statisch gegen musl (`zig cc`-Flag) | — |

## L12 — Debugging und Profiling: **entschieden** (2026-09-28)

| | Entscheidung |
|---|---|
| Debug-Info | DWARF/PDB über den C-Compiler, `#line` macht Lyric-Zeilen daraus; **gdb/lldb ab Tag eins**, kein eigener Debugger; der DAP wird ein Adapter über lldb-dap/gdb (Bereich 11) |
| Typen im Debugger | C-Structs sichtbar; **Pretty-Printer** (lldb/gdb-Python) für Strings, Optionals, Interfaces, wie Rust |
| Backtraces | libbacktrace / `RtlCaptureStackBackTrace`, symbolisiert; Koroutinen-Stacks als eigene Segmente („resumed from …") |
| Profiling | `perf`, Instruments, VTune, Tracy nativ — **kein eigener Profiler**; lesbare Namen (C4) |
| Sanitizer-Profil | `lyric build --profile asan` (ASan + UBSan, Koroutinen annotiert, GC-Fast-Path aus) |
| Panik-Ausgabe | Meldung + Backtrace mit `.lyr`-Positionen; Debug immer, Release bei Panik (E8) |

---

**Bereich 1 ist damit vollständig entschieden** (L1–L12, 2026-09-28).
