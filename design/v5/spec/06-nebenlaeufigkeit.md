# 06 — Nebenläufigkeit

Lebendes Dokument des Bereichs 6. Fragen N1–N10, je **entschieden** oder **offen**. Basis:
`01-laufzeit.md` L4 (stackful Koroutinen mit eigenen Stacks, asymmetrisch, kooperativ, Yield
durch C-Frames, Fremd-Frame-Zähler; Primitive `create/resume/yield/status`), L6 (Laufzeit
thread-fähig ab Vertrag 0: Allokation je Thread, Wurzeln je Thread, Safepoint-Polls),
`05-fehler.md` E8 (Panik-Isolation an der Koroutinengrenze), E10 (Task-Ergebnis trägt Fehler).

## Bestandsaufnahme Lyric 4

Aus `../nebenlaeufigkeit.md` (44 Fragen, Fassung 4), Spec §10, `stdlib/std/task.lyr`:

- **Die Koroutine**: stackful, ungefärbt, statisch typisiert, `throws` am Typ, am Zugpunkt
  erzwungen — „was sehr wenige Sprachen haben". `yield` in jeder Funktion, auch in Methoden,
  Lambdas, `defer`. Die Spec hat eine `yields`-Klausel **ausdrücklich verworfen** (Färbung).
  Preis: vier Paniken statt Compilerfehler (`yield` ohne `resume`, zweiter Treiber,
  Typfehler beim dynamischen Yield, Tiefe). Gemessen: Yield heiß 0,25 µs, kalt aus Tiefe 40
  rund 4 µs (Frame-Kopie — unter L4 entfällt sie).
- **`Coroutine<T>` hat genau ein Mitglied**, `next()` (`?T`; `bool` für `void`; `Coroutine<?T>`
  verweigert). Kein `close()`, kein `status()`, kein `send`, kein Ergebnistyp, keine
  Gleichheit; „fertig?" ohne Ziehen absichtlich nicht; nach einem Wurf erschöpft; ein
  aufgegebener Chain räumt nicht auf (normiert, „GC ist kein Exit-Pfad"); **kein
  `Iterable`**, obwohl `next()` dem Iterator nachgebaut ist.
- **Ein Generator kann nicht warten**: ein Helfer im Generatorkörper, der `Wait` yieldet,
  paniert (`VM0015`) — „yield suspends the nearest running resume" ergibt die Sync/Async-
  Iterator-Spaltung, die C# per Typ trennt, als Laufzeitpanik.
- **Scheduler `std.task`**: 276 Zeilen Lyric über zwei Natives (`poll`, `interrupt`); ein Task
  ist ein `Coroutine<Wait>` (`Now`, `Sleep`, `Readable(fd)`, `Writable(fd)`, `Interrupt`);
  `spawn` ohne Handle, nur nicht-werfend; ein globaler Scheduler, Round-Robin, keine
  Prioritäten; `std.task` verlangt `osAccess`; **steht nicht in der Spec**. Der einzige
  Waker ist `interrupt()` — untypisiert, global, sticky, zugleich der Shutdown-Pfad, und
  solange jemand darauf parkt, schluckt der Scheduler Ctrl+C. Ein handgebauter Channel über
  `Wait.Now` dreht ~10⁴ Leerrunden.
- **Blockierende Natives halten die Welt an** (`os.sleep`, `file.bytes`, DNS in `connect`);
  `std.io.stream`, Sockets und Prozesse yielden dagegen (Pool-Thread + notify-fd).
- Kein Threading (Rule 2), keine Isolates, keine Channels, kein `select`, keine
  Abbruch-Token, keine strukturierte Nebenläufigkeit; Koroutinen-Schachtelung über
  `MaxReentryDepth = 32`.

## N1 — Grundmodell: **entschieden** (2026-09-29) — Threads explizit, Koroutinen je Thread, ein Heap

**Modell C** (C#, Kotlin): der Normalfall ist single-threaded und yieldend — Lyric 4 —,
Parallelität ist ein expliziter Schritt, die Race-Hypothek trifft nur, wer ihn geht.

| # | Regel |
|---|---|
| G1 | **Ein Scheduler je Thread**; Koroutinen gehören dem erzeugenden Thread und **migrieren nicht** (TLS über Yields, thread-gebundene C-Bibliotheken; Kotlin-Dispatcher-Form). Work-Stealing: Tür |
| G2 | `Thread.spawn(fn)` startet einen Thread mit eigenem Scheduler; Hauptthread hat den ersten; Thread-Pool als Bibliothek |
| G3 | **Ein Heap** (L1/L6), Referenzen überschreiten Threads. **Data Races sind Programmfehler ohne Zusage** — auch nicht Speichersicherheit (Fat Pointer und Inline-Structs werden zerrissen geschrieben; Go's Vertrag, ausgesprochen). Guide-Satz; **`--profile tsan`** (ThreadSanitizer über den C-Backend) findet sie. Java's Zusage (Speicher heil) wäre nur mit Kosten an jeder Referenzschreibung zu haben — verworfen |
| G4 | Bibliothek gibt die sicheren Wege: `Channel<T>` (empfohlen), `Mutex<T>` (gibt `T` nur innerhalb `lock { }` her — Rusts Disziplin ohne Compiler), `Atomic<int>`, `Once`. Kein `Send`/`Sync` (ohne Ownership nicht prüfbar) |
| G5 | Unveränderliche Daten (Structs ohne `var`, Strings, `let`-Felder) sind **frei teilbar** — Dividende von M2 |
| G6 | I/O yieldet je Thread-Scheduler (N6); ein blockierender Aufruf blockiert seinen Thread, nicht das Programm |
| G7 | Isolate als **Muster**: `Isolate.spawn(fn)` = Thread mit Scheduler, nur über Channels erreichbar — keine zweite Laufzeit |

| Verworfen | Warum |
|---|---|
| A · ein Thread + Koroutinen (Lyric 4, Lua, Node) | ein Kern — verfehlt Z2/Z3 |
| B · M:N migrierend (Go, Java 21) | Race-Hypothek auf jedem Programm; jede Laufzeitstruktur feinkörnig thread-sicher; Migration gegen TLS und C-Affinität; Go's Scheduler ist zehn Jahre Arbeit |
| D · Isolates (Dart, JS Workers) | Kopie je Nachricht, große geteilte Daten unmöglich, für Spiele unbrauchbar; zweiter Heap-Begriff — als Muster unter C enthalten |

## N2 — Koroutinen-API: Status, Schließen, Senden, Ergebnis, Identität: **offen**
## N3 — Generatoren gegen Tasks: die Sync/Async-Spaltung: **offen**
## N4 — Task-Modell: Handle, Ergebnis, Abbruch, strukturierte Nebenläufigkeit: **offen**
## N5 — Kommunikation: Channels, `select`, Timer, typisierter Waker: **offen**
## N6 — Scheduler und I/O: nicht-blockierend, blockierende Natives, Host-Pump: **offen**
## N7 — Parallelität: Speichermodell oder Nachrichten: **offen**
## N8 — Präemption: **offen**
## N9 — Abbruch und Timeouts: **offen**
## N10 — Thread-Sicherheit von Laufzeit und Bibliothek: **offen**
