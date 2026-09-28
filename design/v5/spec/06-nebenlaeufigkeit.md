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

## N1 — Grundmodell: Koroutinen, Threads, Isolates: **offen**
## N2 — Koroutinen-API: Status, Schließen, Senden, Ergebnis, Identität: **offen**
## N3 — Generatoren gegen Tasks: die Sync/Async-Spaltung: **offen**
## N4 — Task-Modell: Handle, Ergebnis, Abbruch, strukturierte Nebenläufigkeit: **offen**
## N5 — Kommunikation: Channels, `select`, Timer, typisierter Waker: **offen**
## N6 — Scheduler und I/O: nicht-blockierend, blockierende Natives, Host-Pump: **offen**
## N7 — Parallelität: Speichermodell oder Nachrichten: **offen**
## N8 — Präemption: **offen**
## N9 — Abbruch und Timeouts: **offen**
## N10 — Thread-Sicherheit von Laufzeit und Bibliothek: **offen**
