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
## L3 — Aufrufkonvention und Stack: **offen**
## L4 — Koroutinen im Runtime: **offen**
## L5 — Fehler-ABI: **offen**
## L7 — Was `.lyrbc` ersetzt: **offen**
## L8 — C-Emission: **offen**
## L9 — Laufzeitbibliothek: **offen**
## L10 — Determinismus und Budget: **offen**
## L11 — Start und Binärgröße: **offen**
## L12 — Debugging und Profiling: **offen**
