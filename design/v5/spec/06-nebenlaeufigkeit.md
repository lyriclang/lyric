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

## N2 — Koroutinen-API: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| A1 | **`Coroutine<Y, R = void>`** — yieldet `Y`, gibt `R` zurück; `throws E` am Typ bleibt (E10) | Python, Kotlin |
| A2 | **`next(): ?Y`** bleibt (mit O1 auch für `Coroutine<?T>`); **`result(): ?R`** null bis zur Erschöpfung. Nicht `Step<Y, R>` (Korpus ★W2): die `?Y`-Form gibt **`Coroutine<Y, R> :: [Iterator]` mit `Item = Y` gratis** (T6); `for (x in gen)` wird legal | — |
| A3 | `Coroutine<void>`: `next(): bool` bleibt (Schrittprozess, `yield;` je Frame) | Lyric 4 |
| A4 | **`isDone(): bool`** (wissbar ohne Ziehen); **kein `hasNext`** (entscheidet der Körper — Spec-Satz bleibt) | — |
| A5 | **`close()`**: die suspendierte `yield`-Stelle wirft `Cancelled` (ein `Error`), `defer` laufen über die normale Abwicklung; erneutes Yield nach gefangenem `Cancelled` paniert; auf fertiger No-op. „Aufgegeben ohne `close` läuft nichts" bleibt normiert; Debug-Profil warnt über das GC-Netz (L1) | Python `close()`, Lua 5.4 |
| A6 | Senden hinein (`resume co, v`): **nein** in 5.0 — Channels; Tür | Kotlin, C# |
| A7 | `Coroutine` konformiert **`Identity`** (M10): `same()`, Adress-Hash; Doppel-`spawn` erkennbar | — |
| A8 | Zweiter Treiber / Selbst-Resume / `yield` ohne Resumer bleiben dynamische Paniken (Färbung verworfen; N3 nimmt den schlimmsten Fall) | Lyric 4 |
| A9 | Erzeugungssyntax, Zucker: Bereich 8 | — |

## N3 — Generatoren gegen Tasks: **entschieden** (2026-09-29) — Warten ist kein `yield`

| | `yield v` (Generator) | `wait` (Task) |
|---|---|---|
| tut | übergibt einen Wert **an den Resumer** (Transfer entlang der Kette) | **parkt den laufenden Kontext, wo immer er steht**, beim Scheduler seines Threads — symmetrischer Wechsel, Resumer-Kette bleibt intakt |
| fortgesetzt von | dem Resumer (`next()`) | dem Scheduler, der zum gesicherten Kontext zurückwechselt — auch mitten in einem Generator in einem Task |

Ein Generator kann warten (`for (line in readLines(file))`), beliebig geschachtelt, **ohne
Färbung, ohne zwei Typen** — Go's implizites Verhalten als zwei benannte Primitive: die Laufzeit
bekommt **`park`/`unpark`** neben `resume`/`yield` (L4 K7 wächst um ein Paar). „yield suspends
the nearest running resume" bleibt wahr für `yield`; `VM0015` verschwindet. Ein Task ist **kein
`Coroutine<Wait>` mehr**, sondern ein Kontext des Schedulers (N4); `Wait` ist das Argument von
`wait`. Verworfen: Typ-Split (Kotlin `sequence`/`Flow`, C# `IEnumerable`/`IAsyncEnumerable`).

## N4 — Task-Modell: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| T1 | **`spawn(fn(): T throws E): Task<T> throws E`** — Handle mit Ergebnis und Fehlermenge (E10); läuft auf dem Scheduler des aktuellen Threads, `pool.spawn` wählt einen anderen. *Review 2026-10-05 (M6-20): `thread.spawn(task)` entfällt, `Thread.spawn` gibt selbst einen Task-Handle* | Kotlin, Swift |
| T2 | **`try task.await(): T`** parkt bis zum Ende; **Methode, kein Schlüsselwort** — keine Färbung | Kotlin |
| T3 | Zustände `Running`, `Done(T)`, `Failed(Error)` (*Review M6-16: die Menge steht nur statisch am Typ*), `Panicked(PanicInfo)`, `Cancelled`; `status()`, `isDone` | — |
| T4 | **Panik im Task** (E8): Zustand `Panicked`; `await()` darauf **paniert erneut** — eine Panik bleibt ein Bug, außer ein Aufseher schaut per `status()` ohne zu warten. Ein Server überlebt einen Request-Bug nur, wenn er es ausdrücklich so baut | Erlang Monitor |
| T5 | **Strukturierte Nebenläufigkeit als Hauptform**: `TaskScope` — Kinder enden vor dem Scope; erster Fehler bricht Geschwister ab (N9) und wird am Scope-Ende geworfen. `spawnDetached` für Fire-and-forget, beim Namen genannt | Trio, Kotlin `coroutineScope`, Swift `TaskGroup`, Java 21 |
| T6 | `main` ist ein Task auf dem Hauptscheduler; endet `main`, endet das Programm, detachte Tasks werden nicht abgewartet | Go |
| T7 | Kein impliziter „aktueller Task"-Kontext; Kontextwerte wandern als Parameter oder im Scope | Go `context` als Gegenbeispiel |

## N5 — Kommunikation: **entschieden** (2026-09-29) — alles Bibliothek über `park`/`unpark`

| # | Entscheidung | Vorbild |
|---|---|---|
| K1 | **`Channel<T>`** ungepuffert/gepuffert; `send` parkt bei voll, `recv(): ?T` bei leer; `close()`: `recv` nach dem Leeren `null`, `send` wirft `ChannelClosed`; innerhalb eines Threads und darüber hinweg (P1) | Go, Kotlin |
| K2 | **`select` als Bibliothek** mit Builder (`Select.on(c1) { … }.on(c2) { … }.timeout(d) { … }.run()`); kein Schlüsselwort; Bereich 8 darf Syntax darüberlegen | Kotlin DSL; Go-Statement verworfen |
| K3 | `sleep(Duration)` parkt; `Timer.after(d)` ist ein einmal feuernder Channel; `timeout` ein `select`-Fall | Go |
| K4 | **Typisierte Waker statt `interrupt()`**: `Signal` (einmalig), `Event`, `Semaphore` (*Review M6-31: zu 5.0 gehört nur `Semaphore`*); das globale sticky `interrupt()` und die Zwei-Rollen-Falle sind weg | Java, Kotlin |
| K5 | **Shutdown/Ctrl+C eigene Sache**: `os.signals(SIGINT): Channel<Signal>`; der Scheduler schluckt nie ein Signal | Go `signal.Notify` |
| K6 | `Mutex<T>`, `RwLock<T>`, `Once`, `Atomic<T>` (G4); auch auf einem Thread nötig, sobald ein Abschnitt einen `wait` enthält; `Mutex` parkt statt zu spinnen | Rust |
| K7 | alles in Lyric (L9) über `park`/`unpark` und Thread-Primitive; die Laufzeit kennt keinen Channel | — |

## N6 — Scheduler und I/O: **entschieden** (2026-09-29)

| # | Entscheidung | Vorbild |
|---|---|---|
| S1 | **Scheduler bleibt Lyric** über `park`/`unpark`, `poll(events, timeout)`, Timer (L9) | Lyric 4 |
| S2 | **Poller in C je Plattform**: epoll, kqueue, **AFD** unter Windows über eine eigene Anbindung (*Review M6-29; wepoll verworfen*) (Bereitschaftssemantik; IOCP's Completion-Modell würde die stdlib zweigleisig machen) | mio, libuv |
| S3 | **Reguläre Dateien** über Pool-Thread + notify (4.2-Modell von `std.io.stream`, jetzt für alle Datei-I/O); `io_uring` Tür | libuv, tokio |
| S4 | **Keine blockierenden Natives in der stdlib**: `sleep` parkt, DNS auf dem Pool-Thread, `file.bytes` über S3; ein blockierfähiges Native heißt so und existiert nur als Pool-Variante | — |
| S5 | **Host-Pump**: `Scheduler.step(): bool` und `Scheduler.run()` — die 4.x-Form | Lyric 4, Erato |
| S6 | ein Scheduler je Thread, kein Nesting; Host-Callback ist ein C-Aufruf, wartet er, läuft er als Task; `MaxReentryDepth` verschwindet | — |
| S7 | `std.task` steht in der Spec; kein Capability-Gating in Lyric 5 (Z3) | — |
## N7 — Speichermodell: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| P1 | **Happens-before** nur durch `Channel`-Senden/Empfangen, `Mutex` lock/unlock, `await()` auf dem Handle eines Threads, `Atomic`-Operationen, Thread-Start | Java JMM, Go |
| P2 | `Atomic<T>` sequenziell konsistent (C11 `seq_cst`); schwächere Ordnungen als Methoden: Tür | C11, Rust |
| P3 | Zugriff ohne Happens-before auf einen `var`-Ort, den ein anderer Thread schreibt: **Data Race, keine Zusage** (G3) | Go |
| P4 | keine Umordnung über Atomics und Locks (clang mit C11-Atomics) | — |
| P5 | Datenparallelität (`parallelMap`, Chunk-Schleifen) ist **Bibliothek** über einen Thread-Pool; kein `parallel for` in der Sprache | Rust rayon, Java streams |

## N8 — Präemption: **entschieden** (2026-09-29) — kooperativ

Die Invariante **„Code zwischen zwei Warteoperationen läuft ohne Zwischenschaltung von
Geschwister-Tasks desselben Threads"** bleibt (Kotlin, C#, Node, Lyric 4); ein `var`-Zähler
braucht keinen Lock ohne `wait` dazwischen. Aushungern durch einen rechnenden Task ist
dokumentiert; `yieldNow()` als expliziter Rescheduling-Punkt; Rat: CPU-Arbeit auf einen anderen
Thread. **Opt-in Zeitscheibe je Scheduler** über die Safepoint-Polls (L6): Tür. Verworfen: Go's
Präemption (Mutexe auch auf einem Thread nötig).

## N9 — Abbruch und Timeouts: **entschieden** (2026-09-29)

| # | Regel | Vorbild |
|---|---|---|
| X1 | **Kooperativ**: `task.cancel()` setzt eine Marke; am nächsten `wait`/`park` wird **`Cancelled`** geworfen (Mechanismus von `close()`, A5) — `defer`/`using` laufen, Zustand `Cancelled`; `await()` darauf wirft `Cancelled` | Kotlin, Trio |
| X2 | ein Task, der nie wartet, ist nicht abbrechbar (dokumentiert); `yieldNow()` ist Abbruchpunkt | Kotlin |
| X3 | `scope.cancel()` bricht alle Kinder ab; erster Fehler eines Kindes bricht Geschwister ab (T5) | Trio |
| X4 | **`withTimeout(d) { … }`** = Scope + Timer + Abbruch; Ergebnis `T throws TimedOut` | Kotlin |
| X5 | `Cancelled` ist ein fangbarer `Error` (Aufräumen); erneutes Warten danach wirft sofort wieder `Cancelled`, die Marke bleibt | Kotlin |
| X6 | unabbrechbare Abschnitte (`shield`): Tür | Trio |

## N10 — Thread-Sicherheit: **entschieden** (2026-09-29)

Laufzeit thread-sicher (L6); `string`, unveränderliche Structs, `let`-Felder, Enums frei
teilbar (G5); **Container und I/O-Handles nicht thread-sicher** — Teilen über `Mutex<T>` oder
Channels („ein Objekt gehört einem Thread, bis es übergeben wird"); globale `var` ohne
`Atomic`/`Mutex` in einem Programm mit Threads: **Warnung** (Lint, Bereich 11);
Modulinitialisierung einmal, thread-sicher (`Once`); Sync-Typen: `Mutex<T>`, `RwLock<T>`,
`Atomic<T>`, `Once`, `Channel<T>`.

## Review 2026-10-05 — Nachträge beim Bauen (M4–M8a)

Entscheidungen des Maintainers aus der Durchsicht der offenen Punkte vor dem Abschluss von M8a.
Die Kennungen sind die des Reviews; „Betrifft“ nennt, was die Zeile ändert oder schärft. Wo eine
ältere Zeile dieses Dokuments dem widerspricht, gilt die Zeile hier.

| # | Entscheidung | Betrifft |
|---|---|---|
| M6-3 | **Generator-Definition**: eine Funktion mit Ergebnis `Coroutine<…>` ist ein Generator, wenn sie selbst yieldet **oder** keinen Wert zurückgibt. `return make();` bleibt eine Fabrik, ein leerer Rumpf ist ein leerer Generator, und ein Rumpf, der nur über Helfer yieldet, ist einer | N2, 08 D11 |
| M6-9 | Ein `yield` in einer Hilfsfunktion wirft beim Schließen `Cancelled`: der Helfer deklariert `throws Cancelled`, sein Aufruf trägt `try`; ein Generator-Rumpf deckt es für die Helfer, die er ruft | N2 A5 |
| M6-10 | `close()` wirft den ersten Fehler weiter, den ein `defer` beim Abwickeln geworfen hat (05) | N2 A5 |
| M6-4 | **`main` deckt `Cancelled` selbst**, wie ein Generator-Rumpf; wartende Aufrufe behalten ihr `try`. Benannte Funktionen deklarieren weiter | N4 T6, N9 X1 |
| M6-26 | `main` wird nur dann ein Task, wenn das Programm warten kann; ein Programm, das nie wartet, lädt keinen Scheduler | N4 T6 |
| M6-11 | **Jedes Warten in einem abgebrochenen Task wirft**, auch wenn es nicht warten müsste (fertiger Task, freies Schloss, bereitliegender Wert). Aufräumen, das warten muss, braucht die `shield`-Tür (X6) | N9 X5 |
| M6-12 | **Deadlock**: hat ein Scheduler nichts Bereites, keinen Schläfer und keine Quelle, die wecken könnte (kein I/O-Interesse, kein Signal-Abo, kein zweiter lebender Thread), endet das Programm mit `LYR-RT0018` und dem Trace der wartenden Stelle. Mit laufenden Threads greift die Prüfung nicht | N6 |
| M6-13 | **Form des Scopes**: `using let scope = TaskScope.new();` — der Scope ist `Closeable`, sein `close()` wartet. Eine Lambda-Form daneben gibt es nicht | N4 T5 |
| M6-14 | `close()` des Scopes wirft den Kindfehler selbst, als `Error`. Ein Kind, das mit `Cancelled` endet, ist kein Fehler des Scopes | N4 T5 |
| M6-15 | Der Rumpf von `spawnDetached` darf nur `Cancelled` werfen; alles andere fängt er selbst | N4 T5 |
| M6-16 | `Task<T> throws E` trägt die Menge statisch am Typ (03); `status()` zeigt `Failed(Error)` | N4 T1, T3 |
| M6-17 | **Select nimmt den ersten bereiten Fall in Schreibreihenfolge.** Ein geschlossener Kanal ist immer bereit: wer `null` bekommt, hört auf, ihn zu wählen | N5 K2 |
| M6-18 | Select hat **nur Empfangsfälle und keinen Ergebniswert** — für 5.0 entschieden, keine Tür | N5 K2 |
| M6-19 | `Timer.after(d)` feuert durch **Schließen**: jeder Empfang gibt `null`, auch später noch | N5 K3 |
| M6-20 | `Thread.spawn` gibt den **Task-Handle** seines ersten Tasks; „join“ ist `await()`. Endet der erste Task, **bricht der Thread ab, was auf ihm noch lebt, und läuft, bis alle geendet haben** — `defer` laufen, Schlösser werden frei. `main` bleibt nach T6 | N1 G2, N7 P1 |
| M6-21 | **Panik im Rumpf eines Schlosses**: das Schloss wird frei und ist vergiftet; jeder weitere `lock`/`read`/`write` paniert mit `LYR-RT0019` und nennt die erste Panik. Ein `Once` in dieser Lage ist ebenfalls vergiftet | N1 G4, N5 K6 |
| M6-22 | Wirft der Rumpf eines `Once`, geht der Fehler an diesen Aufrufer, und der nächste `run` versucht es neu | N5 K6 |
| M6-23 | `Atomic<T>` trägt `int` und `bool`. **Referenzen (`Atomic<Klasse>`) kommen mit M11** — ein atomarer Referenz-Store braucht die Schreibbarriere des eigenen GC | N1 G4, N5 K6 |
| M6-24 | Der Rumpf von `lock` und `write` bekommt den **Ort** (`m.lock { &n => n += 1; }`, 03 T12); ein Guard-Typ entfällt. `read` gibt den Wert | N1 G4 |
| M6-27 | `pool.spawn` und `scope.spawn` nach `close()` sind eine Panik | N1 G2 |
| M6-28 | Der Signal-Kanal hat einen festen Puffer von 16; die Zustellung blockiert nie, ein voller Puffer verliert das Signal für diesen Kanal | N5 K5 |
| M6-29 | **Windows-Poller: eigene AFD-Anbindung** — ein Completion-Port je Scheduler, Wecken per `PostQueuedCompletionStatus`, Sockets per `IOCTL_AFD_POLL`. Gebaut mit M8b. wepoll kann ein wartendes `epoll_wait` nicht von außen wecken | N6 S2 |
| M6-31 | Von den Weckern aus K4 gehört **nur `Semaphore`** zu 5.0 (`std.sync`, M8b). Ein einmaliges Ereignis ist ein Kanal, der geschlossen wird. `Signal` ist der Name der Betriebssystem-Signale (10 Q9) | N5 K4 |
| M7-5 | **Der Scheduler benutzt die Klasse `Atomic` nicht**: er arbeitet mit std-internen atomaren Operationen auf eigenen Feldern (07 D4). `std.sync` importiert `std.task`, nicht umgekehrt; der Modulschnitt 10 Q10 gilt unverändert | N10 |
| — | Technische Wahl beim Bauen: `park` ist die Primitive der Laufzeit, „unpark“ das Wieder-bereit-Machen durch den Scheduler in Lyric. `Cancelled` lebt in `std.task`; der Compiler lädt das Modul für jedes Programm mit einer Koroutine | N5, 10 Q10 |

---

**Bereich 6 ist damit vollständig entschieden** (N1–N10, 2026-09-29).
