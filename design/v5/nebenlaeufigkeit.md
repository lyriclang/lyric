# Lyric 5 — Gebiet: Koroutinen und Nebenläufigkeit (Fassung 3)

Stand der Messungen: 2026-09-27, gegen `lyrc.dll` / `lyrvm.dll` / `lyrtest.dll` aus
`bin/Debug/net10.0` (4.6.0-Stand). Proben:

| Verzeichnis (unter `…/scratchpad/v5-design/probes/`) | Herkunft |
|---|---|
| `nebenlaeufigkeit/` | erste Runde |
| `nebenlaeufigkeit-rev2/` | zweite Fassung (Erwartungen in `ERWARTUNGEN.md`) |
| `nebenlaeufigkeit-rev3/` | die adversarische Kritik (p1–p7) |
| **`nebenlaeufigkeit-rev4/`** | **diese Fassung**: p1–p7 selbst nachgefahren, dazu n1, n3, n4, n6, n7 (Erwartungen vor dem Lauf in `ERWARTUNGEN.md`) |

Alle Zeitangaben stammen aus einem **Debug-Build**. Absolute Zahlen sind wertlos, die *Verhältnisse*
sind die Aussage. Jede Zahl steht mit ihrer Streuung oder ist als Einzellauf gekennzeichnet.

Belegarten: **gemessen** (Programm kompiliert und gelaufen), **gelesen** (Pfad:Zeile), **behauptet**
(nichts davon — steht ausdrücklich dabei).

**Normative Quelle ist `lyric-spec/spec/`** (Clone neben `lyric/`, seit 2.0 normativ, spec-first).
Der Guide (`docs/guide/`) ist Doku, nicht Spec. Die zweite Fassung hat den Guide mehrfach „die Spec"
genannt; das ist durchgehend korrigiert, und jede Sprachregel wird aus `spec/10-coroutines.md`
zitiert.

---

## 1. Ist-Stand

### 1.1 Die Koroutine selbst — der stärkste Teil der Sprache

Was Lyric 4 hier hat, haben sehr wenige Sprachen: **stackful, ungefärbt, statisch typisiert,
mit typisierten Ausnahmen am Zugpunkt.**

| Eigenschaft | Belegt |
|---|---|
| `yield` ist in **jeder** Funktion erlaubt | gelesen `lyric-spec/spec/10-coroutines.md:54-58` (§10a); gemessen `o_yield_in_lambda.lyr` |
| Auch in einer Klassenmethode und im Rumpf einer `Iterator<int>.next()`-Implementierung | gemessen `r8_yield_in_method.lyr`, `r10_yield_in_iterator_next.lyr` |
| Keine Funktionsfärbung — und die Spec hat die Alternative **ausdrücklich verworfen**: „Weighed and refused: a `yields T` clause on helper signatures — it would move this panic to compile time, and it would colour every yielding helper the way `async` colours callers" | gelesen `spec/10-coroutines.md:72-77` |
| Genau drei Opcodes: `mkcoro 0x79`, `resume 0x7A`, `yield 0x7B` | gelesen `docs/Bytecode.md:942-944` |
| Werfbarkeit ist Teil des **Typs** und trägt in jeder Typposition | gelesen `spec/10-coroutines.md:100-116`; gemessen `v_throws_positions.lyr` |
| Eine werfende Koroutine passt nicht in einen nicht-werfenden Slot | gemessen `w_plain_slot_rejects_throwing.lyr` → `LYR-SEM0001` |
| Der throws-Vertrag wird am **Zugpunkt** erzwungen | gemessen `r16_throwing_resume_in_plain_fn.lyr` → `LYR-SEM0034` |
| Generisch über den Elementtyp | gemessen `j_generic_coroutine.lyr` |
| Yield in einem `defer`-Body funktioniert und parkt unter dem Scheduler erneut | gemessen `n_yield_in_defer.lyr`, `s9_defer_yields_in_task.lyr` |
| **Eine Koroutine hat Referenzsemantik**: „Copying the value copies a reference to the same suspended state; two holders drive one coroutine" | gelesen `docs/guide/11-coroutines.md:102-103` — deshalb ist NL18-B keine Semantikänderung |

**Der Preis des dynamischen Yields sind vier Panics statt vier Compilerfehlern** — alle gemessen,
normativ in `spec/10-coroutines.md:60-83` (§10a Regeln 1–5) und `appendix-a-diagnostics.md:287-289`:

| Fall | Diagnose | Probe |
|---|---|---|
| `yield` ohne laufendes `resume` (Regel 1) | `LYR-VM0013`, Exit 101 | `c_yield_no_resume.lyr`; folgenreicher `r12_accept_outside_task.lyr` (§1.5) |
| Zweiter Treiber / Selbst-Resume (Regel 5) | `LYR-VM0014` | `g2_resume_running.lyr`, `r11_selfspawn_nested_run.lyr` |
| Dynamischer Yield mit falschem Typ (Regel 3) | `LYR-VM0015` | `i_dynamic_mismatch.lyr`; **und rev4 `p1_nested_gen_wait.lyr`** — ein wartender Helfer in einem Generator, der aus einer Task gezogen wird (§1.2, NL31) |
| Tiefe | `LYR-VM0004` „call depth exceeded 1024 frames" | gemessen `s7_chain_depth.lyr`: 1000 trägt, 2000 panikt — kein CLR-StackOverflow |

**Die 1024 sind keine Eigenschaft der Kette.** *Korrektur gegenüber der zweiten Fassung*
(„Kettentiefe über 1024"). Gelesen `src/Lyric.Vm/Interpreter.cs:139` (`MaxCallDepth = 1024`), `:526`
(`_outerFrames = outer + frames.Count + 1` vor jedem Native-Aufruf) und `:533`
(`if (outer + frames.Count >= MaxCallDepth)`): die Grenze zählt die **gesamte Interpretertiefe des
Threads**, einschließlich der Frames des Treibers (`main → run → step → …`) und geschachtelter Runs.
Eine Kette, die aus Tiefe *d* resumed wird, hat 1024 − *d* Luft. `s7_chain_depth` kann das nicht
unterscheiden; für NL29 ist es der Unterschied zwischen einer Konstante und einer Rechnung. Dazu,
gelesen `Interpreter.cs:160-177`: geschachtelte Runs (ein Native, das ins Skript zurückruft) sind
eine *zweite* Größe, `MaxReentryDepth = 32`, vom Maintainer gemessen und halbiert.

#### Was ein Yield kostet — heiß und kalt

Heiß (eine Kette, 200 000-mal hintereinander; `a1`–`a4` der rev2, fünf Runden, Median): Yield in
Tiefe 0 **0,25–0,37 µs**, in Tiefe 40 **0,5–1,2 µs**, also rund **20 ns je zusätzlich kopiertem
Frame**. Die Aufrufe selbst kosten bei Tiefe 40 das Sechsfache der Yields (1414 gegen 243 ms).

**Kalt — neu gemessen, weil die Kritik zu Recht sagte, dass die 20 ns aus einem heißen Cache
stammen** (`n7_cold_copy_d40`, `n7_cold_copy_d0`, `n7c_calls40_noyield`: 10 000 Tasks, jede 20 Runden
`Wait.Now`, drei verschränkte Läufe; Erwartung vor dem Lauf: 60–200 ns je Frame):

| Probe | Läufe (ms) | je Runde (200 000 Runden) |
|---|---|---|
| Yield aus Tiefe 0 | 1495 / 1531 / 1510 | 7,5 µs (Scheduler-Umlauf) |
| 40 Aufrufe **ohne** Kopie, Yield aus Tiefe 0 | 4150 / 4243 / 4280 | +13,6 µs → **340 ns je Aufruf** (heiß: 177) |
| 40 Aufrufe **mit** Kopie, Yield aus Tiefe 40 | 4927 / 4931 / 5066 | +3,5–3,9 µs → **86–98 ns je kopiertem Frame** |

**Ein Frame kostet kalt das Vier- bis Fünffache von heiß** (≈ 90 gegen 20 ns), ein Aufruf das
Doppelte. Eine Suspendierung aus Tiefe 40 kostet mit 10 000 verstreuten Tasks rund **4 µs**, nicht
0,85. Das ist die Zahl für NL29 — die Übertragung der heißen Zahl in der zweiten Fassung war eine
Behauptung und um Faktor vier daneben.

### 1.2 Was der Koroutinen-API fehlt

`Coroutine<T>` hat **genau ein** Mitglied, `next`. Normativ: „`next` is a built-in member, not a
method a type declares — the same standing as `length` on an array" (gelesen
`spec/10-coroutines.md:31-32`); im Typchecker ist es `if (baseType is CoroutineOf co && mem.Member ==
"next")` (gelesen `src/Lyric.Frontend/Sema/TypeChecker.cs:3374`; *die zweite Fassung zitierte dreimal
`:3226-3240` — das ist die Typargument-Inferenz `LYR-SEM0060`, falscher Anker*). Alles andere endet
in `LYR-SEM0012`.

Es gibt **kein** `close()`, **kein** `status()`, **kein** `throwInto()`, **kein** `send()`. Dazu:

- **`next()` hat drei Formen** (gelesen `spec/10:25-31`, gemessen `r15_void_coroutine_next.lyr`): `?T`;
  `bool` für `Coroutine<void>`; `Coroutine<?T>` verweigert `next()` (`LYR-SEM0080`). NL11, NL23.
- **Eine Abfrage „fertig?" ohne Ziehen gibt es absichtlich nicht**: „there is deliberately NO query
  that answers done without pulling: whether another value comes is decided by the body running …
  the reason no generator API (Python, JavaScript, C#) has one" (gelesen `spec/10:33-35`). *Die
  zweite Fassung empfahl in NL13-D ein `status()`, ohne diesen Satz zu kennen. NL13 ist neu.*
- **Keine Gleichheit, aber Identität.** Gemessen `r7_coroutine_equality.lyr`: `co1 == co2` →
  `LYR-SEM0059`; Folge `r2_double_spawn.lyr`: Doppel-`spawn` wird angenommen, `pending = 2`. Aber
  Referenzsemantik existiert (gelesen `guide/11:102-103`) — es fehlt der Vergleich, nicht die
  Identität. NL18.
- **Werte fließen nur heraus**: „send values (`resume co, v`) do not exist" (gelesen `spec/10:39`).
- **Kein Ergebnistyp**: `LYR-SEM0039` (gelesen `spec/10:20-21`, gemessen `z_coroutine_result.lyr`).
- **Nach einem Wurf ist eine Koroutine erschöpft** — gemessen rev4 `p3_throw_then_pull.lyr`: `first 1`,
  `caught boom`, danach `co.next()` → `null`, kein Panic. Die Spec sagt es nicht als Satz; es folgt
  aus zweien: die Ausnahme „leaves the resume or next() that was running it" (`spec/10:36-38`), und
  ein Frame-Exit „by an exception unwinding the chain" ist ein Exit (`spec/10:89-90`) — der Körper
  ist zu Ende, also `null`. Ableitbar, nicht geschrieben. NL34.
- **Ein aufgegebener Chain räumt nicht auf** — gemessen `d_dropped_defer.lyr`; normativ *und
  begründet*: „a chain abandoned mid-suspension — dropped, collected — runs NOTHING … the garbage
  collector is not an exit path and does not become one here" (gelesen `spec/10:89-93`). *Die zweite
  Fassung forderte in NL13, „der Grund gehört in die Spec" — er steht dort.*
- **`Coroutine<T>` ist kein `Iterable<T>`**: `LYR-SEM0007` (gemessen `b_forin.lyr`), obwohl `next()`
  absichtlich `Iterator<T>.next()` nachgebaut ist (gelesen `guide/11:89`).
- **Ein Generator kann nicht warten.** Gemessen rev4 `p1_nested_gen_wait.lyr`: eine Task zieht einen
  `Coroutine<int>` per `next()`; ein Helfer im Generatorkörper yieldet `Wait.Sleep(10)` →

  ```
  got 1
  panic [LYR-VM0015]: yield in 'main.waitABit' does not match what the running coroutine yields — the value's type must be the chain's element type
      in main.waitABit (p1_nested_gen_wait.lyr:5)
      in main.gen.<body> (p1_nested_gen_wait.lyr:10)
      in main.worker.<body> (p1_nested_gen_wait.lyr:18)
      in std.task.step (task.lyr:154) …
  ```

  Exit 101. Kontrolle `p1b_control.lyr` ohne den Helfer: `got 1`, `got 2`, `done`, Exit 0. Ursache
  normativ: „A yield suspends the NEAREST running resume of its own chain" (gelesen `spec/10:63-66`,
  §10a Regel 2). Lyric hat damit **genau die Sync-/Async-Iterator-Spaltung**, die C# per Typ trennt —
  nur als Laufzeit-Panic. *Die zweite Fassung schrieb in §2.2, Lyric brauche die Trennung nicht;
  das war falsch.* NL31. Die Panic nennt die **Typen nicht** (weder `Wait` noch `int`); der Backtrace
  zeigt beide Resume-Körper, sagt aber nicht, welcher „the running coroutine" ist — NL43.
- **Eine suspendierte Koroutine hält ihren tiefsten Stack auf Lebenszeit** (gelesen
  `src/Lyric.Vm/CoroutineChain.cs:9-13`). Bytes: §1.6.

### 1.3 Der Scheduler — `std.task`

276 Zeilen Lyric über **zwei** Natives: `poll` (gelesen `stdlib/std/task.lyr:42`) und `interrupt`
(`:48`), registriert `src/Lyric.Vm/NativeRegistry.cs:982` bzw. `:1176`. Der Modulkopf sagt „ONE
native" (`task.lyr:4-6`).

| Fakt | Beleg |
|---|---|
| Ein Task ist ein `Coroutine<Wait>` | gelesen `task.lyr:98` |
| `Wait`: `Now`, `Sleep(int)`, `Readable(int)`, `Writable(int)`, `Interrupt` | gelesen `task.lyr:18-33` |
| Ein Task parkt in **genau einer** Spalte — `step()` ist ein `match` mit fünf Armen | gelesen `task.lyr:153-174` |
| `spawn(task: Coroutine<Wait>): void` — kein Handle | gelesen `task.lyr:98-100` |
| `spawn` nimmt den nicht-werfenden Typ | gemessen `f_spawn_throwing.lyr` → `LYR-SEM0001` |
| **Ein** globaler Scheduler als Modulglobale | gelesen `task.lyr:85-94` |
| `poll` privat, `interrupt` `pub` | gelesen `task.lyr:42,48` |
| `std.task` verlangt `osAccess` | gelesen `src/Lyric.Core/Capabilities.cs:64-67`, `spec/04-modules.md:164,169`; **gemessen** rev4 p6: `--grant none` und `--grant file,net` → `LYR-CAP0001` |
| Der asynchrone I/O-Stapel yieldet `std.task.Wait` von innen | gelesen `net.lyr:121,150,181,208,269,303`, `stream.lyr:143,173`, `process.lyr:128,155` — **zehn** Yield-Stellen |
| Round-Robin, keine Prioritäten, keine Namen | gelesen `task.lyr:129-149` |
| **`std.task` steht nicht in der Spec.** `spec/11-stdlib-contract.md` hat keinen Abschnitt dazu | gelesen (grep über `lyric-spec/spec/`: nur `04-modules.md:164,169`, `appendix-a:285`) — NL38 |

*Zur Zählung: die zweite Fassung schrieb sieben Yield-Stellen, die Kritik zwölf. Beides ist falsch —
die Kritik hat die Kommentarzeilen `net.lyr:10` und `process.lyr:9` mitgezählt. Gelesen sind es
zehn `yield Wait.Readable/Writable(…)` außerhalb von `task.lyr` (net 6, stream 2, process 2).*

**Die Löcher, gemessen — und eines ist kleiner, als die zweite Fassung schrieb:**

**(a) Keine typisierte Warteform auf eine Bedingung — aber ein untypisierter, globaler, sticky
Waker existiert.** Die zweite Fassung schrieb „Es gibt KEINE Form ‚weck mich, wenn jemand mich
weckt'". Als Absolutum falsch; die Kritik hat es gemessen, ich habe es nachgefahren:

- rev4 `p7_interrupt_as_waker.lyr`: Consumer parkt auf `Wait.Interrupt`, Producer ruft nach jedem
  Push `interrupt()` → `consumer got 0/10/20 at +50/+93/+124ms`, **`parks = 3`, null Leerrunden**,
  `run` kehrt bei +124 ms zurück. Gegenprobe `k_handmade_channel.lyr` über `Wait.Now`: rund **10⁴
  Leerrunden** (fünf Läufe 8 796–16 712).
- rev4 `p4_interrupt_sticky.lyr`: `interrupt()` **vor** `spawn(watcher)` → `watcher woke at +14ms`.
  Gelesen `task.lyr:44-48`: „With nobody parked the interrupt is REMEMBERED and taken by the next
  task to park". Sticky.

Preis: untypisiert (jeder `Wait.Interrupt`-Parker wacht, `wakeInterrupted` `task.lyr:207-212`), es
ist **der Shutdown-Pfad der Anwendung** (gelesen `task.lyr:29-32`), und **solange jemand parkt,
schluckt der Scheduler Ctrl+C** (gelesen `task.lyr:37-39,49-53`, `NativeRegistry.cs:978-980`). Eine
Bibliothek, die ihn als Channel-Waker nimmt, deaktiviert Ctrl+C. NL33; NL3 muss sagen, was aus ihm wird.

**(b) Ein blockierender Native hält die ganze Welt an.** Gemessen `e3_blocking_abs.lyr`: `os.sleep(300)`
friert den Scheduler 337 ms ein; Kontrolle `Wait.Sleep(300)`: Ticker bei +6 ms. Weitere, gemessen:
`std.io.file.bytes` 2 MB zehnmal — 418 ms (`s3_file_blocks.lyr`); `std.io.net.connect` mit DNS —
150 ms (`s1_dns_blocks.lyr`; gelesen `netConnectStart` → `ResolveHost` → `Dns.GetHostAddresses`,
`NativeRegistry.cs:1747,1757,1675`). Gelesen: `file.lyr:42-69` durchweg Natives; `console.lyr:73,78,81`.
`process.wait` gehört **nicht** dazu (yieldet, `process.lyr:155`).

**Aber `std.io.stream` blockiert NICHT — und die zweite Fassung hat es übersehen, obwohl sie
`stream.lyr:35` selbst zitiert hat.** Gelesen `stream.lyr:43-46`: „Nothing here blocks: a read
starts on a pool thread and answers `null` with the would-block kind until it lands, and the waiting
is this module yielding on the file's notify descriptor. A regular file is not selectable on any
platform — `select()` calls one ready whatever its state — which is why a handle carries that
descriptor at all." Umsetzung gelesen `NativeRegistry.cs:2393-2411` (`StartRead`: `Task.Run(async …
ReadAsync …)`, dann `Poke(state.Notify)`) und `:2501` (`streamNotifyFd`) — *die Kritik zitiert
`:2055-2062`, das ist der Prozess-Pump `StartPump`, derselbe Mechanismus, anderer Ort*. Seit
**4.2.0**, gelesen `CHANGELOG.md:702-705`: „a file HANDLE, and it waits without blocking the thread …
a program with tasks, where one blocking read stalls every other task."

Gemessen rev4 `p2_stream_in_task.lyr` (zehnmal 2 MB über `stream.readSome`; Erwartung: Ticks
während des Lesens):

```
tick 0 at +13ms
reader enters stream at +42ms
tick 1 at +119ms … tick 7 at +295ms
reader leaves stream at +975ms, bytes 20971520
```

Der Ticker läuft **während** des Lesens (Kritik: +77…+252 gegen +36…+531 — schnellere Maschine,
gleiche Form). Der 77-ms-Sprung zwischen „enters" und tick 1 ist ein Einzellauf; als Ursache
(Anlauf des ersten Pool-Lesevorgangs) **behauptet**.

Folge: **der E/A-Threadpool, den die zweite Fassung in NL22 als Rule-2-Ausnahme für v5 vorschlug,
existiert seit 4.2.0**, gebaut am 2026-08-27 — ein Pool-Thread, der keinen `LyrValue` anfasst und
über einen Deskriptor zurückmeldet. NL22 ist neu; NL35 fragt, ob der Mechanismus zur öffentlichen
Host-Schnittstelle wird.

**(c) `run()` ist ein globaler Drain, kein Bereich** (gemessen `l2_nested_run_steals.lyr`,
`l_reentrant_run.lyr`, `r11_selfspawn_nested_run.lyr` → `LYR-VM0014`).

**(d) Eine Panik in einer Task reißt alles mit** (gemessen `m_task_panic.lyr`, Exit 101).

**(e) Ein Deskriptor ist ein roher `int`, ein erfundener weckt sofort** (gemessen `t_bogus_fd.lyr`;
bewusst, gelesen `NativeRegistry.cs:1094-1102`). **Aber Deskriptornummern werden nie wiederverwendet**:
gelesen `NativeRegistry.cs:1439` (`private long _nextSocketFd`, Instanzfeld je Registry), `:1461`,
`:2011`, `:2362` (`++_nextSocketFd` für Sockets, Kinder und Dateien — **ein** monotoner Zähler). Das
ABA-Problem, das die Kritik als Frage stellt, kann innerhalb einer VM nicht auftreten — NL40 ist
durch Lesen beantwortet. Und: **der outward-Cast `l as int` auf einen opaken `Listener` kompiliert aus
Benutzercode ohne Warnung** (gemessen: rev4 `n3_broadcast_wake.lyr` kompiliert sauber) —
`Wait.Readable(l as int)` kann heute jeder schreiben, nicht nur `std.io.net`. NL14.

**(f) Der Interrupt weckt nur `Wait.Interrupt`-Parker** (gemessen `r1_interrupt_vs_parked.lyr`). Eine
auf Sleep/Readable/Writable parkende Task ist von außen unerreichbar (NL6).

**(g) Wecken je Deskriptor ist ein Broadcast.** Gelesen `task.lyr:252-254`: „two tasks may wait on one
descriptor, and both wake when it readies". Gemessen rev4 `n3_broadcast_wake.lyr` (Erwartung: beide
wachen): zwei Tasks `yield Wait.Readable(l as int)` auf demselben Listener, ein Client verbindet →

```
client connected at +118ms
waiter 1 woke at +120ms
waiter 2 woke at +120ms
wakes = 2
```

Thundering Herd, by design. NL41.

**(h) Die Sleeper-Spalte ist quadratisch.** Gemessen `u1_sleepers*.lyr`: 250 → 81 ms, 500 → 289,
1 000 → 1 218, 2 000 → 3 767, **4 000 → 19 358 ms**; Kontrolle `u2_control_now.lyr` (4 000 über
`Wait.Now`): 15/16/20 ms. Gelesen: `wakeSleepers` „quadratic … which is small" (`task.lyr:227-231`);
`nearestDeadline` linear (`:214-225`); `toArray()` je Runde (`:197`); `wakeWaiters` mit `removeAt`
(`:265-276`); `Socket.Select` O(n) (`NativeRegistry.cs:1112,1143`). Auch beim Abräumen sichtbar: rev4
`n6_select_300` — 299 tote Deskriptoren wecken kostet ~200 ms (`WATCHDOG +3094` → `run returned
+3291`), bei 69 sind es 21 ms (Einzelläufe).

**(i) `Select` hat auf dieser Plattform keine sichtbare Obergrenze bei 300.** Gemessen rev4
`n6_select_70.lyr`, `n6_select_300.lyr` (Erwartung vor dem Lauf: unsicher): N Listener mit je einem
geparkten Acceptor, Client verbindet zum **letzten** →

```
N=70:  client connected at +207ms — acceptor 69 got connection at +242ms
N=300: client connected at +206ms — acceptor 299 got connection at +211ms
```

Windows, .NET 10, Einzelläufe. Ob `Socket.Select` auf Unix (dort über `poll()`) dieselbe Freiheit
hat, ist **behauptet**. Gelesen: eine `SocketException` im `Select` wird **verschluckt** und als
„nichts bereit" beantwortet (`NativeRegistry.cs:1116-1120` Sofortpfad, `:1145-1150` blockierend, mit
der Begründung, nur `Dispose` löse das aus). NL42.

**(j) Eine CPU-gebundene Task blockiert wie ein Native.** Gemessen rev4 `n4_cpu_bound.lyr`
(Erwartung: Ticker steht bis zum Schleifenende): 20 Mio. Iterationen ohne Yield →

```
tick 0 at +14ms
cruncher starts at +35ms
cruncher ends at +5219ms
tick 1 at +5219ms
```

5,2 s Stillstand (Debug, Einzellauf). „One thread, no preemption" (gelesen `task.lyr:4`,
`guide/13:149`) gilt für Lyric-Schleifen wie für Natives — die zweite Fassung behandelte nur
Natives. NL37.

### 1.4 Die Einbettung — was der Host heute kann, und was nicht

*Korrektur gegenüber der zweiten Fassung*, die schrieb, ein Host könne „eine Koroutine weder bauen
noch schrittweise treiben" und die Nebenläufigkeit sei „für einen Host mit Frame-Schleife heute
unbenutzbar". Beides ist überzogen:

- **Ein Host kann Koroutinen schrittweise treiben — heute.** `ScriptInstance.Call<TResult>(function,
  ExecutionBudget?, args)` (gelesen `src/Lyric.Embedding/ScriptInstance.cs:85-94`) ruft jede
  Lyric-Funktion; das Muster `Runner.step(): bool` (gelesen `guide/11:189-214`) zieht gespeicherte
  Koroutinen je Aufruf einen Schritt. Ein Host, der je Frame `step()` ruft, hat einen Frame-Scheduler
  über `Coroutine<T>` — auch über `Coroutine<Wait>`, denn `Wait` ist `pub` (`task.lyr:18`).
- **Ein Native kann nach Lyric zurückrufen.** Gelesen `Interpreter.cs:160-177` („a NESTED RUN is real
  CLR stack — Execute, its loop, the host's delegate", `MaxReentryDepth = 32`), `:194-196`,
  `ExecutionBudget.cs:20-22`, `spec/10:78-81` (Regel 4 nennt „a native function calling back into
  script"). Was ein Native nicht kann: dass ein Yield **unter ihm** suspendiert. *Die zweite Fassung
  machte daraus „ein Native kann nicht nach Lyric zurückrufen" — falsch, und darauf stand die
  Begrenzung von NL3.*
- **Was fehlt, ist enger:** `run()` drained (gemessen `e4`: Rückkehr bei +319 ms), es gibt keinen
  nicht-blockierenden Rundeneintritt, `poll` ist privat (`task.lyr:42`). Der Befund heißt „kein Pump
  für `std.task`", nicht „kein Pump für Koroutinen". NL9.
- **Die STATUS-Notiz, auf die sich die zweite Fassung in NL9 zweimal stützte, handelt vom Debugger.**
  Gelesen `STATUS.md:1959-1972`: A14, „the debugger reaches an invoked function — RELEASED as v2.7.0";
  „a controller a host can drive without a second thread" ist der `DebugController` an einem
  Haltepunkt. Es gibt **keinen** früheren Datenpunkt zu einem Host-Pump; beide Befunde („Gegen C" /
  „Für C") sind gestrichen.
- **Ein Budget schaltet den JIT ab.** Gelesen `Interpreter.cs:248-249`: „a debugger or a budget means
  the interpreter, per IExecutionPolicy". NL44.
- `ExecutionBudget` ist **gezählt statt getaktet**, sein Ablauf ein **Panic** (gelesen
  `ExecutionBudget.cs:12-17,58-62`). NL30.

### 1.5 Die Bibliotheksoberfläche sagt nicht, wer nur in einer Task laufen darf

Gemessen `r12_accept_outside_task.lyr`: `net.accept(l)` aus `main` → `panic [LYR-VM0013]: yield
outside a running resume in 'std.io.net.accept'`, Exit 101 — obwohl der Benutzer nirgends `yield`
geschrieben hat. Kontrolle `r13_accept_in_task.lyr` läuft. Weder Signatur (gelesen `net.lyr:112`)
noch Attribut noch Warnung sagen es. NL20.

Gemessen `s9_defer_yields_in_task.lyr`: ein `defer`-Rumpf in einer Task parkt (200 ms), der Scheduler
läuft weiter. NL24. Gemessen `s8_module_global.lyr`: Modulglobale sind von allen Tasks geteilt. NL27.

### 1.6 Speicher je suspendierter Task

Gemessen (`t1`/`t2`/`t3_*_hold`, je 3 Läufe, maximaler Working Set): Tiefe 0 **≈ 0,53 KB je Kette**,
Tiefe 40 **≈ 8,4 KB** — rund **200 Byte je gehaltenem Frame** (Debug). 10 000 parkende Verbindungen
in Tiefe 40 ≈ 84 MB. Der Wasserstand bleibt (gelesen `CoroutineChain.cs:9-13`).

### 1.7 JIT und Yield

Gelesen `src/Lyric.Vm/Jit/JitCompiler.cs`: die Datei erwähnt `yield` **kein einziges Mal**; der Opcode
fällt in `default: return false` (`:679`). **Ein Aufrufer, dessen Callee nicht kompiliert, wird
ebenfalls abgelehnt** (`:718-725`: „THE CALLEE HAS TO COMPILE TOO … keeps every compiled call inside
compiled code"). Ein kompilierter Frame wäre eine Yield-Wand (`spec/10a` Regel 4, `appendix-a:287`).

Gemessen rev4 `p5_jit_helper_yield.lyr` (Erwartung: gleiches Ergebnis mit `--jit`, weil abgelehnt
statt panikt): ohne und mit `--jit` → `worker computed 6`, `done`, Exit 0. **Kein Panic, aber auch
keine Kompilierung** — der gesamte Task-Pfad bleibt interpretiert, und niemand sieht, welche
Funktion warum. NL36.

### 1.8 Tests: die Scheduler-Globale leckt NICHT zwischen Tests einer Datei

Die Kritik fragte, ob ein Test die Tasks sieht, die ein früherer Test derselben Datei gespawnt und
nie gezogen hat — der Scheduler ist eine Modulglobale, und `lyrtest` nutzt eine VM je Datei (gelesen
`STATUS.md:2226-2229`, `guide/20:44-46`). Gelesen `guide/20:39-41`: jeder Test läuft „in a **fresh
instance**: module state cannot leak between tests". Gemessen rev4 `n1_isolation/` (`lyrtest .`,
Erwartung: alle PASS):

- `iso.lyr`: Test a spawnt ohne `run`, prüft `pending() == 1`; Test b prüft `pending() == 0` → beide PASS.
- Kontrolle `iso2.lyr`, reihenfolgeunabhängig: zwei Tests spawnen je einen, prüfen `pending() == 1`
  (geteilt sähe der zweite 2) → beide PASS.

Antwort: **nein**, `std.test` muss den Scheduler nicht zurücksetzen. Was bleibt, ist die virtuelle
Uhr (NL28-B'), die an `now()` in `task.lyr:66-69` hängt. NL39.

### 1.9 Wo Spezifikation und Implementierung auseinandergehen

| Stelle | Was steht da | Was gemessen/gelesen ist |
|---|---|---|
| `task.lyr:112-113` | `pending()` = „How many tasks the scheduler holds" | die laufende Task fehlt (gemessen `l_reentrant_run.lyr`) |
| `task.lyr:30-32` | „Every parked task wakes on one interrupt" | nur die auf `Wait.Interrupt` (gemessen `r1`) |
| `task.lyr:4-6` | „ONE native" | zwei (`:42`, `:48`) |
| `task.lyr:230-231` | quadratisch, „which is small" | 4 000 Sleeper: 19,4 s; Kontrolle 15 ms |
| `PLAN.md:377` | „`spawn` liefert ein Handle" — mittel | trifft (NL1) |
| `PLAN.md:376` | „Uhren laufen bereits" (`std.os`-Zeitfunktionen) | **keine läuft**: `os.lyr` ohne `@Deprecated` (gelesen); `os.nowMillis()` ohne Warnung (gemessen) |
| `guide/11:89` | `next()` wie `Iterator<T>.next()` „on purpose" | keine Konformanz (`SEM0007`); Form in zwei Fällen anders |
| `guide/13:149` | „One thread, no preemption" | stimmt — und weder Natives noch Lyric-Schleifen (5,2 s, `n4`) sind davon ausgenommen; der Satz sagt es nicht |
| `spec/11-stdlib-contract.md` | — | **`std.task` hat keinen Spec-Abschnitt** (gelesen) |
| `spec/10:22-38` | `next()` nach Ende → `null`; Wurf verlässt den Pull | „nach einem Wurf erschöpft" steht nirgends, ist gemessen (`p3`) und ableitbar |
| `NativeRegistry.cs:1116-1120,1145-1150` | `SocketException` im `Select` = „nichts bereit" | ein Fehler, der nicht von `Dispose` kommt, wird verschluckt (gelesen; nicht provoziert — dass es einen anderen Auslöser gibt, ist **behauptet**) |

---
## 2. Sprachvergleich

Neun Vergleichseinträge der zweiten Fassung waren falsch oder schief (Swift, Rust, POSIX, C#,
Python, Unity, Haskell, Erlang, Kotlin). Alle sind korrigiert und unten markiert. Die Tabelle ist
Sekundärliteratur — keine Zeile darin ist gemessen; sie ist „gelesen" im Sinn von Sprachdoku und
Standardwissen, und wo ich mir nicht sicher bin, steht es dabei.

### 2.1 Die Tabelle

| Sprache | Einheit | Färbung | Scheduler | Handle / Ergebnis | Abbruch | Kanäle / select | Echte Parallelität | Preis |
|---|---|---|---|---|---|---|---|---|
| **Lyric 4** | stackful Koroutine | **keine** | `std.task`, in Lyric, global | keins (`spawn → void`) | keiner | keine (ein globaler sticky Interrupt-Waker) | nein; ein E/A-Pool für Dateien und Kinder (4.2) | Yield-Fehler sind Panics; Generatoren können nicht warten; Sleeper-Spalte quadratisch |
| **Lua 5.4** | stackful Koroutine | keine | keiner | `resume` gibt `(ok, err\|werte)` | `coroutine.close` | keine | nein | jedes Framework baut den Scheduler selbst |
| **Go** | Goroutine (M:N) | keine | Runtime, preemptiv seit 1.14 | keins | `context.Context` von Hand | `chan`, `select`, `time.After` | ja, Shared Memory | Data Races, Goroutine-Lecks, `context`-Klempnerei |
| **Erlang/OTP** | Prozess, isolierter Heap | keine | BEAM, preemptiv per Reduktionen | `Pid`, Monitore, Links | `exit/2`; `exit(Pid, kill)` untrappbar | `!` / `receive … after` | ja, ohne Shared Memory | Nachrichten werden kopiert — **außer Binaries > 64 Byte, die referenzgezählt geteilt werden** (korrigiert); ETS als Hintertür |
| **Haskell (GHC)** | grüner Thread, stackful | keine | RTS, M:N, preemptiv **per Allokation — eine Schleife, die nicht allokiert, wird ohne `-fno-omit-yields` nie unterbrochen** (korrigiert) | `ThreadId`, `Async` | `throwTo`, `mask`, `bracket`, `timeout` | `MVar`, `STM` mit `retry`/`orElse` | ja, STM | asynchrone Ausnahmen sind ein eigenes Korrektheitskapitel |
| **C#/.NET** | `Task<T>` (stackless) | `async` färbt | Threadpool, `SynchronizationContext` | `Task<T>`, `await` | `CancellationToken`, kooperativ, ohne Einwurf | `Channels`, `IAsyncEnumerable`, `Task.WhenAny` | ja | Färbung; `async void`; zwei Fehlerformen; `UnobservedTaskException` meldet **fehlgeschlagene, nie beobachtete** Tasks (korrigiert: nicht „nie gestartete") |
| **Rust** | `Future` (stackless), Executor extern | `async` färbt | keiner in der Sprache | `JoinHandle<T>` | Drop | `mpsc`, `tokio::select!` | ja, `Send`/`Sync` | Cancellation safety; **`std::ops::Coroutine`/`CoroutineState` sind nightly-only, nie stabil ausgeliefert** (korrigiert) |
| **Swift 6** | `Task<Success, Failure>` | `async` färbt | Runtime-Threadpool | `task.value` — **nicht werfend bei `Failure == Never`, werfend bei `Failure == any Error`; Kriterium ist Gleichheit, nicht Konformanz (`Never` konformiert zu `Error`)** (korrigiert) | kooperativ, `Task.isCancelled` | `AsyncStream`; kein `select` | Aktoren, `Sendable` | Färbung; `Sendable` kostete einen Sprachmodus |
| **Kotlin** | Coroutine (stackless, CPS) | `suspend` färbt | Bibliothek + Dispatcher | `Job` / `Deferred<T>` | `CancellationException`, `NonCancellable` | `Channel`, `select {}`, `withTimeout` | ja | Färbung; kein Suspend über eine fremde Frame-Grenze; **Stacktraces sind seit Jahren per Stack-Trace-Recovery und `DebugProbes` repariert — der Preis ist, dass beides Opt-in ist und Laufzeit kostet** (korrigiert) |
| **JavaScript** | Promise / Generator | `async` färbt | Event-Loop | `Promise` | keiner (`AbortSignal`) | keine | Worker-Isolates | zwei Generatorwelten (`function*` / `async function*`) |
| **Python** | Generator / `asyncio.Task` | `async` färbt | `asyncio` | `Task`, `await task` | `task.cancel()` → `CancelledError` | `asyncio.Queue`; **`asyncio.wait(FIRST_COMPLETED)` und `as_completed` sind die select-Form** (korrigiert: nicht „kein select") | GIL je Interpreter (3.12), `concurrent.interpreters` (3.14), free-threaded Builds | Ökosystem zweimal; **`RuntimeWarning: coroutine … was never awaited`** ist das Vorbild für NL26 |
| **Zig** | — | Richtung: `Io` als Parameter | keiner | — | — | — | Threads | Async seit 0.11 nicht verfügbar |
| **OCaml 5** | Effect Handler | keine | keiner; Handler dynamisch | Continuation | `discontinue` | — | Domains | Effekte nicht im Typsystem; Continuations one-shot |

### 2.2 Was jede Sprache konkret beiträgt

**Lua 5.4.** `coroutine.status`, `coroutine.close` (lässt `<close>`-Variablen laufen), bidirektionale
Werte. Für Lyric: `close` ist die Antwort auf den aufgegebenen Chain (§1.2, NL13). `status` dagegen
steht gegen `spec/10:33-35` — Lua kann es sich leisten, weil Lua keinen statischen Typ am Pull hat;
Lyrics Grund gegen eine „fertig?"-Abfrage gilt in Lua nicht weniger, Lua hat ihn nur nicht gezogen.

**Go.** `go f()` gibt nichts zurück — dieselbe Entscheidung wie `spawn`, mit demselben Ergebnis.
`select` mit `default` und `time.After` ist die kleinste Form für Timeout plus Mehrfachwarten (NL5).
`fatal error: all goroutines are asleep — deadlock!` ist das Vorbild für NL17-D. Gos netpoll weckt
je Deskriptor **eine** Goroutine, nicht alle — das ist die Gegenposition zu Lyrics Broadcast (NL41).

**Erlang.** Isolierte Heaps, „let it crash", Supervisor — was Lyric 4 nicht hat (§1.3d). *Korrektur:*
Nachrichten werden kopiert, **aber Binaries über 64 Byte („refc binaries") werden referenzgezählt
geteilt** — genau die Puffer, die bei NL15 die Kopierkosten ausmachen würden. Die Nuance gehört in
die NL15-Preisspalte: „kopieren" ist auch in Erlang nicht absolut. Reduktionen (Preemption nach N
Aufrufen) sind das Vorbild für NL37. `exit(Pid, kill)` für NL24.

**Haskell (GHC).** Das einzige ausgelieferte System mit grünen, stackful, ungefärbten Threads *und*
Handle. `throwTo` ist NL6-B, mit der vollen Rechnung: `mask`, `uninterruptibleMask`, `bracket`
(→ NL24). *Korrektur:* die Preemption per Allokation hat das bekannte Loch, dass eine Schleife ohne
Allokation nie unterbrochen wird (ohne `-fno-omit-yields`); GHC beweist also **nicht**, dass Preemption
in einer Einthread-Laufzeit „einfach" geht — es beweist, dass ein Sicherungspunkt-Modell ein
Loch hat, wenn die Sicherungspunkte an etwas anderem hängen als an Instruktionen. Das ist ein
Argument *für* Erlangs Reduktionen und für Lyrics Instruktionszähler (NL37).

**C# — die Wirtssprache.** `CancellationToken` (NL6-C/NL19-C: kooperativer Abbruch ohne Einwurf,
kein Frame wirft, der es nicht deklariert). `SynchronizationContext` als Frame-Pump-Vorbild (NL9).
`System.Threading.Channels` (NL4-A, mit `Complete()`/`Completion`). *Korrektur zu §2.2 der zweiten
Fassung:* `IAsyncEnumerable<T>` neben `IEnumerable<T>` ist **nicht** eine Folge, die Lyric erspart
bleibt — Lyric hat exakt diese Spaltung (§1.2, gemessen `p1`), nur ohne Typ: ein `Coroutine<int>`,
der in einer Task wartet, panikt. C# hat den Fall in den Typ gezogen; Lyric muss entscheiden, ob es
ihn in den Typ zieht, umleitet oder als Panic dokumentiert (NL31). *Korrektur zu NL26-D:*
`TaskScheduler.UnobservedTaskException` feuert für **fehlgeschlagene** Tasks, deren Ausnahme niemand
beobachtet hat — ein nie gestarteter `Task` löst nichts aus. Das Vorbild passt zu NL2, nicht NL26.

**Rust.** Drop als Abbruch (NL6-D); **cancellation safety** als der stärkste Einwand gegen NL5-A (→
NL25); `Waker: Send + Clone`, damit ein Timer, ein E/A-Ereignis oder ein fremder Thread wecken kann —
und **genau das tut Lyrics notify-Deskriptor seit 4.2** (§1.3b): ein Pool-Thread pokt eine
Self-Pipe, `poll` sieht sie. Rusts Waker und Lyrics notify-fd sind derselbe Mechanismus unter
verschiedenen Namen (NL3, NL35). *Korrektur:* `std::ops::Coroutine` und `CoroutineState::{Yielded,
Complete}` sind **nightly-only** (`#![feature(coroutines, coroutine_trait)]`), nie in stabilem Rust
ausgeliefert. Die zweite Fassung stellte sie neben Pythons ausgeliefertes `Generator[Y,S,R]` als
Vorbild für NL1-B und NL23-A. Als Beleg für eine Sprachentscheidung zählt ein instabiles Feature
anders — NL23-A steht deshalb jetzt auf Python (`StopIteration.value`) und JS (`{value, done}`)
und nennt Rust als Entwurf, nicht als Auslieferung.

**Swift.** Bereich + Handle + kooperativer Abbruch sind komplett übernehmbar, weil sie nichts mit
Färbung zu tun haben (NL1, NL2, NL7). *Korrektur, zum zweiten Mal:* die zweite Fassung schrieb,
`task.value` sei „nur werfend, wenn `Failure: Error`". Das ist selbst falsch — `Never` konformiert
zu `Error`, nach diesem Kriterium wäre auch `Task<T, Never>.value` werfend. Tatsächlich:
`extension Task where Failure == Never { var value: Success { get async } }` und
`extension Task where Failure == any Error { var value: Success { get async throws } }`. Das
Kriterium ist **Gleichheit** mit `Never` bzw. `any Error`. NL2-B' (Werfbarkeit im Handle-Typ) trägt
weiterhin — Lyric hat dafür den `throws`-Suffix, der genau diese Zweiteilung ausdrückt — aber die
Begründung ist neu geschrieben.

**Kotlin.** Die beste API-Form: `Job`, `Deferred`, `coroutineScope`, `withTimeout`, `Channel`,
`select`, `NonCancellable`, `CoroutineName`, `CoroutineContext`, `runTest` mit virtueller Uhr. Die
Fremdframe-Grenze hat Lyric exakt (`spec/10a` Regel 4); die Lambda-Grenze nicht (gemessen).
*Korrektur:* „schlechte Stacktraces" als Preis ist veraltet — kotlinx.coroutines hat Stack-Trace-
Recovery und `DebugProbes`/`-Dkotlinx.coroutines.debug`; die zweite Fassung nannte in NL28 selbst den
Kotlin-Coroutine-Debugger als Vorbild, beides zugleich ging nicht. Der ehrliche Preis: beides ist
Opt-in und kostet Laufzeit.

**JavaScript.** `gen.throw`, `gen.return` (NL12-C, NL13); Worker-Isolates (NL15). Und die
Generator-Spaltung `function*` / `async function*` mit `for await` — die Form, die Lyric heute als
Panic hat (NL31).

**Python.** `send`/`throw`/`close`; `TaskGroup`, `timeout`, `shield`. `asyncio.wait(…,
return_when=FIRST_COMPLETED)` und `as_completed` sind die select-Form (korrigiert). Subinterpreter
(PEP 684/734) als Isolate-Vorbild (NL15). **`RuntimeWarning: coroutine 'f' was never awaited`** — die
Laufzeitwarnung für eine gebaute, nie getriebene Koroutine — ist das genaue Vorbild für NL26-D und
ersetzt dort das falsche C#-Vorbild.

**Unity.** *Korrektur:* Unity-Koroutinen werden je Frame genau **einen** `MoveNext` weit gestept,
nicht mit einem Millisekundenbudget. Das Vorbild belegt NL9-B (`step(): bool`), nicht NL9-A
(`runFor(millis)`); die zweite Fassung hatte es der falschen Option zugeordnet.

**POSIX.** *Korrektur zu NL22:* `select()`/`poll()` **nehmen** reguläre Dateien an und melden sie
immer als bereit; nur `epoll_ctl` verweigert sie (`EPERM`). Die zweite Fassung schrieb „nicht
select()-bar" — die Folge (kein Warten möglich) stimmt, der Mechanismus nicht, und
`stream.lyr:45-46` sagt es richtig.

**Zig, OCaml 5.** Unverändert: Zigs `Io`-Parameter ist die ehrlichste Antwort auf NL8-C, die ich nicht
empfehle. OCaml 5 bestätigt, dass ungefärbte Effekte ohne Effekttypen ausgeliefert werden können
(`Effect.Unhandled` zur Laufzeit), `discontinue` ist NL12-C, one-shot Continuations sind
`LYR-VM0014`. **Aber**: OCaml ist für NL16 nicht der Beleg — der Beleg ist Lyrics eigene,
begründete Spec-Entscheidung `spec/10:72-77`. Die zweite Fassung hat OCaml zitiert und die eigene
Spec nicht; das ist umgedreht.

---
## 3. Designfragen

Reihenfolge: NL1–NL3 sind das Fundament. NL18–NL30 kamen mit der zweiten Fassung, NL31–NL44 mit
dieser. Jede Frage trägt: Ist-Stand mit Beleg, Optionen mit Vorbild und Preis, Empfehlung,
Bruchgrad, Abhängigkeiten.

---

### NL1 — Liefert `spawn` ein Handle, und hat eine Koroutine einen Ergebnistyp?

**Heute.** `spawn(task: Coroutine<Wait>): void` (gelesen `task.lyr:98-100`). Kein Ergebnistyp:
`LYR-SEM0039` (gelesen `spec/10:20-21`, gemessen `z_coroutine_result.lyr`). Keine Gleichheit
(gemessen `r7` → `LYR-SEM0059`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `spawn` gibt ein `Task`-Handle **ohne** Ergebnistyp (`isDone()`, `cancel()`) | Go `WaitGroup`-Ersatz | Ergebnisse laufen weiter über geteilten Zustand |
| B | `Coroutine<Y, R>`; `Coroutine<T>` = Zucker für `Coroutine<T, void>`; `spawn → Task<R>`, `join(): R` | Python `Generator[Y,S,R]`, C# (Generator und `Task<T>` getrennt); Rust nur als Nightly-Entwurf | Neue Sprachform; zweiter Typ an `mkcoro`/`resume` im Bytecode (Formatbruch); NL23 mitzuentscheiden |
| C | Ergebnistyp am Funktionsclause: `fn work(): Coroutine<Wait> -> int` | — | Dritte Stelle für einen Rückgabetyp; im Feld/Parameter nicht ausdrückbar |

**Empfehlung: B.** A lässt die Hälfte für immer im geteilten Zustand; C wiederholt den Fehler, den
der `throws`-Suffix schon einmal korrigiert hat (`spec/10:118-123`: die Werfbarkeit „vanished at the
first indirection" — genau dasselbe passiert einem Ergebnistyp am Clause).

**Bricht: mittel** (`PLAN.md:377` trifft): Bytecodeformat, jede Host-Signatur mit `Coroutine`, jede
Benutzersignatur unter dem Zucker, jedes `next()` (NL23). **Warnstufe 4.x:** sobald ein Handle
existiert — „Task-Handle verworfen" (NL2) und „nie gezogen" (NL26). **Konformanz (NL38):** neue Fälle
`since: 5.0.0` für den zweiten Typparameter; `next_on_void_answers_bool.lyr` wird retiriert, wenn NL23-A kommt.

**Hängt ab von:** Bytecode-Format; Generics (Default-Typargument — `docs/Grammar.md:218` kennt keins);
**NL23**; **NL18**.

---

### NL2 — Wohin geht die Ausnahme einer Task?

**Heute.** Nirgendwohin: `spawn` verlangt den nicht-werfenden Typ (gemessen `f_spawn_throwing.lyr`),
Begründung `task.lyr:10-12`. Eine Panik beendet das Programm (gemessen `m_task_panic.lyr`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Jede Task legt `try` um ihren Körper; Fehler werden `println` |
| B | `spawn` nimmt die werfende Koroutine; Ausnahme im `Task<R>` gespeichert, bei `join()` erneut geworfen | Kotlin `Deferred.await()`, C# `await task` | Ohne `join` verschluckt — braucht eine Warnung „nie gejoined"; **C#s `UnobservedTaskException` ist genau diese Warnung** (das Vorbild, das die zweite Fassung fälschlich NL26 zuordnete) |
| B' | Wie B, Werfbarkeit **im Handle-Typ**: `Task<R>` gegen `Task<R> throws E` | Swift: `value` ist `async` bei `Failure == Never`, `async throws` bei `Failure == any Error` | Ein Typ mehr; dafür weiß `join()` statisch, ob es werfen kann |
| C | B plus Supervisor-Haken `spawn(co, onError:)` | Erlang `spawn_monitor` | Zweiter Mechanismus neben `join` (Rule 2) |
| D | Ein Fehler bricht den Bereich ab | Kotlin `coroutineScope`, Swift `TaskGroup` | Setzt NL7 voraus |

**Empfehlung: B', mit NL7 zu D erweitert.** *Begründung neu geschrieben:* Swift trennt die beiden
Fälle nicht über Konformanz, sondern über **Typgleichheit** (`Failure == Never` / `== any Error`).
Lyric hat für genau diese Zweiteilung bereits einen Mechanismus, der in jeder Typposition trägt: den
`throws`-Suffix (gelesen `spec/10:100-116`). `Task<R> throws E` ist keine neue Sprachform. C ist
Erlangs Antwort, *weil* Erlang keine Ausnahmen über Prozessgrenzen hat.

**Bricht: minor** (`spawn` wird permissiver). **Warnstufe 4.x:** „Task-Handle verworfen — eine
Ausnahme dieser Task sähe niemand", sobald NL1 gelandet ist.

**Hängt ab von:** NL1; **NL19**; typed throws für Funktionstypen (gelesen `lyric-v5-features.md:32`).

---

### NL3 — Die fehlende Warteform: auf eine Bedingung warten

**Heute.** `Wait` kennt `Now`, `Sleep`, `Readable`, `Writable`, `Interrupt` (gelesen `task.lyr:18-33`).
Eine **typisierte** Form „weck mich, wenn jemand mich weckt" fehlt. **Was es gibt** (§1.3a, gemessen
`p7`, `p4`): `interrupt()`/`Wait.Interrupt` als globaler, untypisierter, sticky Broadcast-Waker ohne
Busy-Loop — mit dem Preis, dass er der Shutdown-Pfad ist und Ctrl+C schluckt. **Und es gibt die
Form, die die std für Dateien und Kinder benutzt** (§1.3b, gelesen `stream.lyr:43-46`,
`NativeRegistry.cs:2393-2411`): ein notify-Deskriptor, den ein Pool-Thread pokt und auf dem die Task
`Wait.Readable` parkt.

**Die Prämisse der zweiten Fassung war falsch.** Sie schrieb, ein Native könne „nicht nach Lyric
zurückrufen", und begrenzte den Waker deshalb auf „nur aus Lyric". Gelesen `Interpreter.cs:160-177`,
`ExecutionBudget.cs:20-22`, `spec/10:78-81`: ein Native **kann** Lyric rufen (geschachtelter Run,
Tiefe ≤ 32); es darf nur nicht darunter suspendieren. Ein Native könnte also `wake(w)` rufen. **Aber
das hilft nicht, und der Grund ist ein anderer:** in einer Einthread-VM läuft ein Native nur, weil
eine Task es gerade ruft — es gibt nichts, das *nebenher* wecken könnte. Ein Weckruf von außen (Pool-
Thread, Timer, Fenster) kann die VM nur an **einer** Stelle erreichen: in `poll`, während der
Scheduler blockiert. Deshalb ist der notify-Deskriptor die richtige Form für Fremdwecken — und der
Lyric-interne Waker braucht ihn gar nicht.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen; Channels als Native | — | „std ist source-first" ins Gegenteil |
| B | `Wait.Signal(int)` mit `wake(id)` | Futex | Rohe IDs, wie `Readable(int)` |
| C | **Opaquer Waker**: `opaque type Waker`, `yield Wait.Signal(w)`, `wake(w)` aus Lyric — `wake` schiebt die Parker direkt in `ready` (der Scheduler ist Lyric, dasselbe Modul) | Rust `Waker`, Kotlin `Continuation`, Haskell `MVar` | Sechste `Wait`-Variante → `@NonExhaustive`-Gate (gemessen `r3` → `LYR-SEM0050`) |
| C' | **Waker = notify-Deskriptor**: `newWaker()` legt ein Self-Pipe-Paar an, `yield Wait.Readable(w)`, `wake(w)` pokt es — keine neue Variante, und ein Host-Thread kann es auch | **Lyrics eigenes `std.io.stream` seit 4.2** | Ein OS-Objekt je Waker — für 10 000 Channels 10 000 Sockets; und `Select` skaliert linear (§1.3h) |
| C'' | **C für In-Process, C' für Fremdwecken — als EIN Typ**: ein `Waker` hat eine Lyric-Warteliste und *optional* einen notify-Deskriptor; `wake` aus Lyric braucht keinen, `wake` von einem Host-Thread geht über einen **VM-weiten** notify-Deskriptor plus eine von `poll` geleerte Weckliste | Rust (`Waker` ist der eine Typ für beide Seiten), libuv `uv_async_t` (ein Handle je Loop) | Ein Native mehr (`wakeFromHost`) oder eine Erweiterung von `poll`; die Weckliste ist geteilter Zustand zwischen Pool-Thread und VM — genau ein Lock, wie `state.Gate` in `StartRead` heute |
| D | Channels direkt in den Scheduler | Go | Jede Primitive ein Scheduler-Eingriff |

**Empfehlung: C'' — und der Unterschied zur zweiten Fassung ist, dass NL3 damit die allgemeine
Antwort ist, nicht nur die für In-Process.** Mit einem Waker werden Channel, `join`, `select`,
Semaphore, Timeout **gewöhnliches Lyric** (gemessener Gegenwert: 10⁴ Leerrunden → 0, `k` gegen `p7`).
Der Fremdwecken-Pfad ist keine Erfindung, sondern die Verallgemeinerung dessen, was `StartRead` +
`Poke` + `streamNotifyFd` heute für Dateien tun — einmal je VM statt einmal je Datei. Das macht
**NL14-B (`Descriptor`) und NL3-C (`Waker`) zu einer Typfrage** (NL35).

**Was aus `interrupt()` wird (NL33):** es bleibt der Signal-Sitz. Als Waker für Bibliotheken ist es
nach C'' nicht mehr nötig und sollte in der Doku ausdrücklich davon abgeraten werden — der Grund
ist gemessen: es schluckt Ctrl+C.

**Bricht: minor bis major — solange `@NonExhaustive` fehlt.** Gemessen `r3_wait_match_partial.lyr` →
`LYR-SEM0050`: ein Benutzer-`match` über `Wait` muss heute erschöpfend sein, also bricht jede neue
Variante jedes Benutzer-`match`. Mit `@NonExhaustive` (gelesen `lyric-v5-features.md:47`, P2 🟡) ist es
additiv; C' allein wäre es *ohne* das Gate, aber zum Preis eines OS-Objekts je Waker.

**Hängt ab von:** `@NonExhaustive` (Gebiet Enums); `opaque type` (**seit 1.15**, gelesen
`CHANGELOG.md:1004-1006` — 3.8 stellte nur die Uhr gegen den inward-Cast; *korrigiert*); NL14; NL33;
NL35.

---

### NL4 — Channels: Sprachform oder Bibliothek?

**Heute.** Nichts. Gelesen `lyric-v5-features.md:139`: `Channel<T>` als P1 🟡.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `Channel<T>` als generische Klasse in `std.task`, auf NL3 gebaut | Kotlin `Channel`, C# `System.Threading.Channels`, Python `asyncio.Queue` | Keine Syntax |
| B | Operatoren (`ch <- v`) | Go | Zweite Syntax für einen Aufruf; Scheduler in der Grammatik |
| C | Mailbox je Task | Erlang | Nur eine Warteschlange je Task; setzt NL1/NL18 voraus |

**Empfehlung: A.** C nur, wenn NL15 kommt — dann ist es ein zweiter Mechanismus neben A (Rule 2),
zusammen zu entscheiden. Varianten (Kotlins vier, C# bestätigt): Rendezvous, gepuffert, unbegrenzt,
conflated; `Complete()`/`Completion` für das Ende.

**Bricht: nein.** **Hängt ab von:** NL3; NL25; bedingte Konformanz (`Channel<T> :: [Display]`).

---

### NL5 — `select` und Timeout: darf eine Task in mehreren Spalten parken?

**Heute.** Nein — `step()` schiebt in genau eine Liste (gelesen `task.lyr:157-173`). Ein
`Wait.Readable(fd)` kann keinen Timeout haben.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `Wait.Any(Wait[])` — mehrere Spalten, der erste Wecker gewinnt, der Scheduler räumt ab und sagt, welcher | Go `select`, Erlang `receive … after`, C# `Task.WhenAny`, **Python `asyncio.wait(FIRST_COMPLETED)`** (korrigiert) | Abräumen braucht Identität (NL18); wirft NL25 auf; **vervielfacht die Broadcast-Wakes aus NL41** |
| B | `select { … }` als Grammatik | Go, Kotlin | Grammatik für einen Wert |
| C | Nur Timeout-Varianten (`ReadableUntil`) | — | Kombinatorische Explosion |
| D | `withTimeout(ms) { … }` über NL3 + NL6 | Kotlin, Haskell `timeout` | Braucht Abbruch |

**Empfehlung: A, D obendrauf.** **Bricht: minor bis major** (`@NonExhaustive`-Bedingung, gemessen `r3`);
`Any` ist rekursiv — ob geschachtelt, gehört in die Spec. **Hängt ab von:** NL3, NL18, NL25, NL41, NL6 für D.

---

### NL6 — Abbruch

**Heute.** Keiner. Kein `cancel`, kein `close`. Eine liegengelassene Koroutine räumt nicht auf
(gemessen `d_dropped_defer.lyr`; normativ `spec/10:89-93`). Eine auf Sleep/Readable/Writable
parkende Task ist unerreichbar (gemessen `r1`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Flag von Hand | Go vor `context` | Parkende Tasks unerreichbar |
| B | Kooperativ mit Ausnahme: `cancel()` setzt ein Flag; am nächsten Suspendierungspunkt wird `Cancelled` eingeworfen; `defer` räumt; landet im `join()` | Kotlin, Python, Haskell `throwTo` | Braucht NL12-C; **kollidiert mit typed throws (NL19)**; braucht NL24 |
| C | Kooperativ mit Abfrage: `isCancelled()` | Swift, Go, **C# `CancellationToken`** | Parkende Task muss geweckt werden (NL3); jede Schleife braucht einen Prüfpunkt |
| D | Hart: Chain verworfen, nur `defer` läuft (`close`) | Lua `coroutine.close`, JS `gen.return()`, Rust Drop | Die Task erfährt nichts |

**Empfehlung: B als Task-Ebene, D als Koroutinen-Ebene — nach NL19.** Zwei Ebenen, nicht zwei
Mechanismen. C ist die ehrlichere Wahl, wenn NL19 das Typloch nicht akzeptiert; mit NL27 reist der
Token ohne Signaturverbreiterung. **Rule 2 / Spec:** `Cancelled` darf ein `catch (e: Exception)` nicht
fressen (Kotlin und Python mussten beide nachbessern).

**Bricht: major** (Einstufung hängt an NL19: `throws Cancelled` in jeder yieldenden Signatur, oder
`spawn`/`step` ändern den Typ — `step` zieht heute ohne `throws`, gelesen `task.lyr:153-154`).

**Hängt ab von:** **NL19**, NL1, NL18, NL3, NL12, NL13, **NL24**, SPEC-RUNDE §5 (werfender `defer`,
gelesen `SPEC-RUNDE.md:120-124`; Uhr `LYR-SEM0110` seit 4.6, gelesen `STATUS.md:50-52`).

---

### NL7 — Strukturierte Nebenläufigkeit

**Heute.** Keine (gemessen `l2`, `r11`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | Go | Task-Lecks |
| B | Bereichsform `taskScope(fn(s: Scope) -> void)`; kehrt zurück, wenn alle Kinder fertig sind; Kind-Fehler bricht Geschwister ab | Swift `withTaskGroup`, Kotlin `coroutineScope`, Python `TaskGroup` | `spawn` ohne Bereich auf einen Wurzelbereich — **NL26** fragt, was der am Ende von `main` tut |
| C | Implizite Elternschaft | Kotlin `Job`-Hierarchie | Unsichtbar; `main` hat keinen Elternteil |

**Empfehlung: B, `spawn` auf einem Wurzelbereich als Kompatibilitätsform.** Beantwortet nebenbei `l2`.
**Bricht: minor** (Bedeutung von `run()` in einer Task). **Warnstufe 4.x:** `run()` aus einer Task.
**Hängt ab von:** NL1, NL2, NL6, NL8, NL18, NL26, NL27; werfende Funktionstypen.

---

### NL8 — Wem gehört der Scheduler?

**Heute.** Einem Modul (gelesen `task.lyr:85-94`); `poll` privat (`:42`); der I/O-Stapel yieldet
`std.task.Wait` von innen (zehn Stellen, §1.3).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Global bleiben | Go, JS | `run()` bleibt global |
| B | Scheduler als Wert mit dynamischem „aktuellem" | Kotlin, Python `get_event_loop`, C# `SynchronizationContext` | Impliziter Kontext |
| C | Scheduler als Parameter jeder I/O-Signatur | Zig `Io` | Nimmt die Ungefärbtheit zurück |
| D | A + `poll` wird `pub` | — | Bereitschaftskonvention (`-1`, toter fd = ready) wird Vertrag |

**Empfehlung: A + D, gegen B und C.** D erst nach NL21 (sonst zementiert es `Select`). **Bricht: nein.**
**Hängt ab von:** NL7, NL9, NL21, **NL32** (ob der Scheduler ohne `osAccess` einen Kern hat).

---

### NL9 — Kann ein Host den Scheduler pumpen?

**Heute — enger als die zweite Fassung schrieb.** Ein Host kann **Koroutinen** heute schrittweise
treiben: `ScriptInstance.Call<T>(function, budget, args)` (gelesen `ScriptInstance.cs:85-94`) ruft je
Frame eine Lyric-seitige `step()` nach dem Muster `guide/11:189-214`; `Wait` ist `pub`, ein
Lyric-seitiger Frame-Scheduler über `Coroutine<Wait>` ist schreibbar. Was fehlt, ist **für
`std.task`**: `run()` drained (gemessen `e4`), es gibt keinen nicht-blockierenden Rundeneintritt, und
`poll` ist privat (`task.lyr:42`) — der eigene Scheduler kommt nicht an die OS-Bereitschaft. Das sind
zwei Lyric-Zeilen, nicht „eine andere Maschine". *Die STATUS-Notiz `:1959-1972`, auf die sich beide
Befunde der zweiten Fassung stützten, handelt vom Debugger (A14) — gestrichen.*

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `pub fn runFor(budget): bool` in `std.task` | libuv `UV_RUN_NOWAIT` | Zweiter Eintritt neben `run()`, vertretbar, wenn `run()` = `runFor(∞)`; **Millisekunden vs. Instruktionen — NL30** |
| B | `pub fn step(): bool` — eine Runde, `poll` mit Timeout 0, nie blockierend | **Unity-Koroutinen (ein `MoveNext` je Frame — korrigiert: das ist B, nicht A)**, JS-Tick | Der Host taktet; Deadlines nur so genau wie sein Takt |
| C | Host-seitig `ScriptInstance.Pump(budget)` | Wren + eigener Scheduler | **Unnötig**: `Call` *ist* der Pump; C wäre eine zweite Oberfläche für dasselbe |
| D | Nichts | — | Wer die Engine besitzt, besitzt die Schleife nicht |

**Empfehlung: B jetzt (zwei Lyric-Zeilen, kein Formatbruch), A mit NL30, C nicht.** B ist zugleich der
Test-Treiber aus NL28-C' — ein Mechanismus, zwei Zwecke. `run()` wird `while (step()) {}` plus
Blockieren, wenn nichts bereit ist.

**Bricht: nein.** **Warnstufe 4.x:** keine; B ist additiv und sofort machbar. **Hängt ab von:** NL8,
NL30, NL32 (ein sandboxter Host ohne `osAccess` kann `std.task` gar nicht laden), NL44.

---

### NL10 — Blockierende Natives

**Heute.** Sie halten alles an, ohne Diagnose. Gemessen: `os.sleep(300)` 337 ms (`e3`); `file.bytes`
2 MB ×10 418 ms (`s3`); `net.connect` mit DNS 150 ms (`s1`). Gelesen: ganz `file.lyr:42-69`,
`console.lyr:73,78,81`, `os.lyr:56`. **Nicht** blockierend: `std.io.stream` (Pool + notify-fd,
gemessen `p2`), `process.wait` (yieldet). Und eine Lyric-Schleife ohne Yield blockiert genauso (5,2 s,
`n4`) — NL37.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen, dokumentieren | — | Anfänger stolpern garantiert |
| B | Threadpool für blockierende Natives | **`std.io.stream` seit 4.2** (gelesen `stream.lyr:43-46`), Node, Tokio `spawn_blocking` | Für Dateien **gebaut**; für DNS wäre es `Dns.GetHostAddressesAsync` + notify-fd — derselbe Bau |
| C | Die blockierenden Formen yielden, wenn ein Resume läuft (`std.time.sleep` → `Wait.Sleep`) | gevent, aber richtig gebaut | Zwei Verhalten unter einem Namen — muss in die Spec |
| D | Compiler-Warnung: blockierender Native in einem Körper, der auch yieldet | Clippy `await_holding_lock` | Nur lokal |

**Empfehlung: C für `sleep`, B für DNS (derselbe Bau wie `stream`), D als Netz, `std.io.file` nach
NL22.** *Korrektur:* die zweite Fassung hielt C für DNS für die Antwort; B ist billiger, weil der
Mechanismus existiert (`StartRead`-Form, `NativeRegistry.cs:2393-2411`). **Warnstufe 4.x:** die
`@Deprecated`-Uhr auf den `std.os`-Zeitfunktionen **läuft nicht** (gelesen `os.lyr`; gemessen: keine
Warnung) — sie muss gestellt werden, mit „blockiert den Scheduler" als Begründung.

**Bricht: minor** (C ändert das Verhalten eines Aufrufs innerhalb eines Resumes). **Hängt ab von:**
NL22, NL20 (D ist dieselbe Maschinerie), NL37, Gebiet Standardbibliothek.

---
### NL11 — `Coroutine<T>` und `Iterator<T>`: zwei Mechanismen für dasselbe?

**Heute.** Zwei Mechanismen, bekannt (`guide/11:89` „on purpose"), nicht austauschbar (`LYR-SEM0007`,
gemessen `b_forin.lyr`). Die Signaturen sind nur im Normalfall gleich (`bool` für `void`, `SEM0080`
für `?T`; gelesen `spec/10:25-31`).

**Und — neu, gemessen `p1`:** ein Generator, der in einer Task gezogen wird, **kann nicht warten**
(`LYR-VM0015`, normativ `spec/10:63-66`). In Python, JS und C# ist ein Generator ein Iterator — aber
alle drei haben für den *wartenden* Generator einen **zweiten** Typ (`async def`/`__aiter__`,
`async function*`/`for await`, `IAsyncEnumerable`). Die Konformanz macht `for (x in fibonacci())`
möglich; sie macht `for (x in gen())` in einer Task mit wartendem Generator **nicht** lauffähig.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Zwei Wege ohne ADR |
| B | `Coroutine<T> :: [Iterator<T>]` per bedingter Konformanz, `T` weder `void` noch Optional | Python, JS, C# `IEnumerator` | Bedingte Konformanz (gelesen `lyric-v5-features.md:29`, P1 🟡, ungebaut); **deckt nur nicht-wartende Generatoren** — sonst verspricht sie das Python-Modell und liefert die Hälfte |
| C | `Iterator<T>` über Koroutinen definieren | — | Bricht `std.iter` |
| D | Adapter `iterOf(co)` | — | Dritter Name |

**Empfehlung: B — mit dem ausgeschriebenen Satz, dass sie nur nicht-wartende Generatoren abdeckt,
und NL31 als die Frage, was mit den wartenden geschieht.** Es wird nichts entfernt; die Doppelung
hört auf zu kosten.

**Bricht: nein.** **Hängt ab von:** bedingte Konformanz (ungebaut); generische Methoden
(`lyric-v5-features.md:28`); **NL23**; **NL31**.

---

### NL12 — Werte in die Koroutine hinein

**Heute.** „send values (`resume co, v`) do not exist" (gelesen `spec/10:39`); `ResumeExpr = 'resume'
UnaryExpr` (gelesen `docs/Grammar.md:490`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Kein Einwerfen (NL6) |
| B | `resume c, v`, dritter Typparameter | Python `Generator[Y,S,R]`, Lua | Fast immer `S = void`, aber überall geschrieben |
| C | Nur hineinwerfen: `co.throwInto(e)` | JS/Python `gen.throw()`, OCaml `discontinue` | Löst NL6, nicht Anfrage/Antwort |
| D | Kein Sprachmittel; Channel (NL4) | Go | Umständlicher, typisiert |

**Empfehlung: C plus D.** **Bricht: nein.** **Hängt ab von:** NL6, NL19, NL4. **Konformanz:** ein
neuer Fall „throwInto trifft den Suspendierungspunkt" `since: 5.0.0`; `spec/10:39` bleibt.

---

### NL13 — `close()`: die aufgegebene Koroutine — und warum `status()` NICHT

**Heute.** Kein `close()` (gelesen `TypeChecker.cs:3374`, einziges Mitglied `next`). Ein aufgegebener
Chain lässt `defer`s liegen (gemessen `d_dropped_defer.lyr`), normativ und begründet in
`spec/10:89-93`: „the garbage collector is not an exit path and does not become one here". *Die
zweite Fassung schrieb, C sei auszuschließen „und der Grund gehört in die Spec" — er steht dort.*

**Und: `status()` steht gegen die Spec.** Gelesen `spec/10:33-35`: „there is deliberately NO query
that answers done without pulling: whether another value comes is decided by the body running, so
such a query cannot be answered without advancing — the reason no generator API (Python, JavaScript,
C#) has one". *Die zweite Fassung empfahl D (`status()`), mit `ChainState` als Feld
(`CoroutineChain.cs:22-28`) als Argument. Ein Feld ist kein Argument gegen eine Sprachregel.* Was ein
`status()` leisten könnte, ist genau das, was die Spec ablehnt (`Suspended` sagt nicht, ob ein Wert
kommt), plus zwei Dinge, die anders billiger sind: `Running` (Selbst-Resume erkennen — das erledigt
NL18-D am `spawn`) und `Done` nach beobachtetem Ende (das weiß der Aufrufer schon).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Ein Socket im `defer` einer abgebrochenen Task leckt bis zum VM-Ende |
| B | `co.close()`: wickelt den suspendierten Chain ab, alle `defer`s laufen, danach erschöpft | Lua `coroutine.close`, JS `gen.return()`, Python `gen.close()`, C# `IAsyncDisposable` | Der Mechanismus existiert (Ausnahme-Abwickeln); braucht Identität, wenn `close` suchen muss (NL18) |
| C | GC-Finalizer | C# Finalizer | **Von der Spec ausgeschlossen** (`spec/10:92-93`) |
| D | B plus `status(): CoroutineState` | Lua `coroutine.status` | **Von der Spec ausgeschlossen** (`spec/10:33-35`) — oder die Spec ändert diesen Satz, und dann muss die Empfehlung gegen seinen Grund antreten |

**Empfehlung: B, ohne `status()`.** `close()` widerspricht der Spec nicht: es ist ein *Pull mit
Abwicklung*, kein Blick ohne Ziehen. Was `close()` auf einer Task mit Ergebnis liefert, klärt NL23;
was es auf einer Task liefert, die geworfen hat, klärt NL34.

**Bricht: nein.** **Hängt ab von:** SPEC-RUNDE §5, NL6, **NL24** (`close()` auf einem parkenden
`defer` kehrt nie zurück — gemessen `s9`), NL18, NL34. **Konformanz:** neuer Fall „close runs the
defers" `since: 5.0.0`; `defers_fire_when_the_body_exits.lyr` bleibt.

---

### NL14 — Wartegründe: rohe `int`s und ein geschlossenes Enum

**Heute.** `Wait.Readable(int)` nimmt jede Zahl; ein erfundener Deskriptor weckt sofort (gemessen
`t_bogus_fd.lyr`; bewusst, gelesen `NativeRegistry.cs:1094-1102`). **Deskriptornummern werden nie
wiederverwendet** (gelesen `NativeRegistry.cs:1439,1461,2011,2362` — ein monotoner Instanzzähler);
ein Zahlendreher trifft also einen toten oder einen fremden lebenden Deskriptor, nie einen
recycelten (NL40). **Der outward-Cast `l as int` auf `Listener` kompiliert aus Benutzercode ohne
Warnung** (gemessen, `n3` kompiliert) — jeder kann heute auf `std.io.net`s Deskriptoren parken. Und
`Wait` ist geschlossen: ein Benutzer-`match` muss erschöpfend sein (gemessen `r3` → `LYR-SEM0050`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Zahlendreher = stiller Busy-Loop |
| B | `opaque type Descriptor`; `Wait.Readable(Descriptor)`; nur `std.io.*` stellt welche her | Rust `OwnedFd` | `opaque`-über-`opaque` (`STATUS.md:1090-1091`, §Still open); der outward-Cast müsste dann *auch* für `Listener → int` gesperrt oder gewarnt sein, sonst bleibt die Hintertür |
| C | `Wait.Signal(Waker)` aus NL3 für In-Process; Fremdwecken über den VM-weiten notify-Deskriptor (NL3-C'') | Rust `Waker` | **Waker und Descriptor werden ein Typ — NL35** |
| D | `Wait` als Interface | — | Dispatch am heißesten Punkt |

**Empfehlung: B plus C, mit der Frage aus NL35, ob es ein Typ wird.** **Bricht: minor bis major**:
(1) die **zehn** Yield-Stellen in der std (net 6, stream 2, process 2 — gelesen; *die zweite Fassung
schrieb sieben, die Kritik zwölf*), alle in der std; (2) die `@NonExhaustive`-Bedingung (gemessen `r3`).

**Hängt ab von:** NL3, NL35, NL40, `@NonExhaustive`, `opaque`-über-`opaque`.

---

### NL15 — Worker-Isolates

**Heute.** Nicht vorhanden; offen vermerkt (gelesen `PLAN.md:313` — Posten 24, „bricht
‚single-threaded' und verlangt nach Rule 2 ein ADR"; `PLAN.md:446` — „ein ADR aufsetzen oder den
Posten streichen?"; `lyric-v5-features.md:69`). *Die zweite Fassung zitierte `PLAN.md:280,413` —
das sind `@NonExhaustive` und „fünf Ausschlüsse"; falsche Anker.*

**Was die Laufzeit kann:** Deskriptortabellen gehören seit 4.3.0 der VM (gelesen `STATUS.md:613-616`).
**Was prozessweit bleibt:** „the interrupt SIGNAL half — `_signalPending`, the handler, the listening
count, the self-pipe" (gelesen `STATUS.md:623`, *nicht `:613-615`*; im Code `NativeRegistry.cs:1504`
`private static int _signalPending`). Darauf baut `std.task` seinen Shutdown-Pfad — für N Isolate
ist offen, wessen Ctrl+C welcher Worker sieht.

**Ein Isolate bricht nicht „kein geteilter Speicher"** — es ist dessen konsequenteste Form. Es bricht
das Wort „single-threaded" in Rule 2. **Und dieses Wort ist seit 4.2.0 ohnehin nicht mehr wörtlich
wahr**: `std.io.stream` und `std.process` lassen Pool-Threads laufen, die keinen `LyrValue` anfassen
(§1.3b). Der Maintainer hat die Regel also schon einmal als „kein geteilter veränderlicher Speicher"
gelesen.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Streichen | — | Ein Kern in einer Welt mit 8–16 |
| B | Isolate mit kopierten Nachrichten | JS Worker, Erlang, CPython `concurrent.interpreters` | Kopierbarkeit definieren (Structs ja, Klassen nein, Koroutinen nein, Handles nein) — faktisch `Sendable`. **Erlang zeigt den Preis ehrlich: Binaries > 64 Byte werden dort nicht kopiert, sondern referenzgezählt geteilt** — große Puffer sind der Fall, an dem reines Kopieren wehtut |
| C | Isolate mit unveränderlichen Nachrichten | — | Setzt `mut struct` voraus |
| D | Nur der Host parallelisiert | heute | Der Gast skaliert sich nicht |

**Empfehlung: `mut struct` als Gate, Ziel offen, bis dahin D.** Stand `mut struct`: „with v5", bis
dahin Warnung `LYR-SEM0109` (gelesen `STATUS.md:40-52`; `PLAN.md:375` meint die Uhr, nicht die
Regel). **Bricht: nein**, aber Rule 2 wird umformuliert — und diese Umformulierung ist nach 4.2
nur noch das Nachtragen dessen, was gebaut ist.

**Hängt ab von:** NL4, `mut struct` (Gate), NL27, der prozessweite Interruptpfad, Gebiet Einbettung.

---

### NL16 — Bleibt der dynamische Yield ein Laufzeitfehler?

**Heute.** Ja, vierfach (gemessen §1.1). **Und die Spec hat die Alternative bereits gewogen und
verworfen**: „Weighed and refused: a `yields T` clause on helper signatures — it would move this
panic to compile time, and it would colour every yielding helper the way `async` colours callers,
which is the disease door C exists to avoid" (gelesen `spec/10:72-77`). *Die zweite Fassung führte
OCaml 5 als Beleg und zitierte die eigene Sprachentscheidung nicht — falsche Reihenfolge.*

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen — **Spec-Stand** | `spec/10:72-77`; OCaml 5 bestätigt | Fällt erst im Test auf |
| B | Effektsystem / `yields T` | Koka | **Von der Spec verworfen** |
| C | Lokaler Lint | — | Helfer über drei Ebenen entgeht ihm |
| D | Dokumentationsattribut `@MayYield` | — | siehe NL20 — muss gegen `spec/10:72-77` argumentieren |

**Empfehlung: A, und zwar als das, was es ist — die bestehende Spec-Entscheidung.** Investiert wird in
Diagnose (NL20-D, NL43). **Bricht: nein.** **Hängt ab von:** NL20.

---

### NL17 — Stillstand, Fairness, Sichtbarkeit

**Heute.** Round-Robin (gelesen `task.lyr:129-149`); Backtrace nennt die Funktion, nicht die Task
(gemessen `m`, `r11`); „quietly and indefinitely" für Interrupt-only (gelesen `task.lyr:126-128`);
keine Statistik.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Ein Busy-Loop ist unsichtbar |
| B | Task-Namen bei `spawn` | Erlang `register`, Kotlin `CoroutineName` | Ein Feld je Task |
| C | `stats()`: Runden, Leerrunden, Parkzahlen je Spalte, **und die Schrittdauer je Task** (damit eine CPU-gebundene Task — 5,2 s in `n4` — von einem blockierenden Native unterscheidbar ist, NL37) | Go `schedtrace` | Nur Zahlen |
| D | Stillstandsmeldung | Go „all goroutines are asleep" | Erst mit NL3/NL14 entscheidbar; `Wait.Interrupt` ausgenommen |

**Empfehlung: B und C jetzt, D mit NL3.** C hat einen gemessenen Zweck: die quadratische
Sleeper-Spalte (§1.3h) bemerkt man nur, wenn Parkzahlen sichtbar sind. **Bricht: nein** (Überladung
`spawn(co, name)`). **Hängt ab von:** NL3, NL28, NL21, NL37.

---

## 3b. Ergänzte Designfragen der zweiten Fassung (NL18–NL30), nach der Kritik korrigiert

### NL18 — Hat eine Koroutine bzw. eine Task eine Identität?

**Heute — präziser als in der zweiten Fassung.** Gleichheit fehlt (gemessen `r7` → `LYR-SEM0059`),
**Identität existiert**: „Copying the value copies a reference to the same suspended state; two
holders drive one coroutine" (gelesen `guide/11:102-103`). Folgen: Doppel-`spawn` angenommen
(`r2`), mit geschachteltem `run()` ein Absturz (`r11` → `LYR-VM0014`); NL5-A, NL6, NL7, NL17 sind
ohne Identität nicht schreibbar.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Alles darüber unschreibbar |
| B | `Coroutine<T>` bekommt `Equatable`/`Hashable` über die Chain-Referenz | Lua, JS, Erlang `Pid` | **Keine Semantikänderung** — Referenzsemantik gibt es schon; sichtbar wird, was ist. Muss in die Spec, damit `==` nicht als Strukturvergleich gelesen wird |
| C | Identität am `Task<R>`-Handle | Swift `Task`, Kotlin `Job` | Koroutine bleibt ohne `==`; `close()` (NL13) kann dann keine Koroutine in einer Liste finden |
| D | C plus: `spawn` weist eine bereits eingereihte/laufende Koroutine zurück | Erlang, .NET `Task.Start` | Eine Prüfung je `spawn`; braucht B oder C |

**Empfehlung: B und C und D.** *Geändert gegenüber der zweiten Fassung (C plus D, „`Coroutine<T>`
bleibt ein reiner Wert").* Ein reiner Wert war es nie; die Lücke, die die zweite Fassung selbst unter
NL18 benannte (`close()` braucht Gleichheit), ist das Argument **für** B, nicht für C. C kommt
obendrauf, weil ein Task mehr ist als seine Koroutine (Name, Bereich, Ergebnis).

**Bricht: nein** (B macht `LYR-SEM0059` zu einem Ergebnis; D: minor). **Warnstufe 4.x:** Doppel-`spawn`
derselben Koroutine im selben Rumpf ist lokal entscheidbar. **Hängt ab von:** NL1, Konformanz-Synthese
(`lyric-v5-features.md:30`).

---

### NL19 — Wie verträgt sich ein eingeworfenes `Cancelled` mit typed throws?

**Heute.** Der throws-Vertrag wird am Zugpunkt erzwungen (gemessen `r16` → `LYR-SEM0034`; normativ
`spec/10:108-110`), `step` zieht ohne `throws` (gelesen `task.lyr:153-154`). Ein eingeworfenes
`Cancelled` ließe jeden Frame zwischen Yield und Resume werfen, der nichts deklariert.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Jeder yieldende Helfer trägt `throws Cancelled` | Java `InterruptedException` | Färbung auf der throws-Achse — **und genau das, was `spec/10:72-77` für `yields T` verworfen hat, unter anderem Namen** |
| B | `Cancelled` umgeht den Typchecker: fangbar, nicht deklarationspflichtig | Kotlin, Python, Haskell | Ein ausdrückliches Loch; Spec-Satz nötig |
| C | Kein Einwerfen; Abfrage `isCancelled()` | C# `CancellationToken`, Go, Swift | Kein Loch; parkende Task muss geweckt werden (NL3), jede Schleife braucht einen Prüfpunkt |
| D | Abwickeln statt Ausnahme: `close()` | Rust Drop, Lua | Die Task erfährt nichts |

**Empfehlung: B mit engem Spec-Satz, C als ernste Alternative.** Der Spec-Satz: (i) vom
`throws`-Vertrag ausgenommen; (ii) `catch (e: Exception)` fängt es nicht; (iii) ein `catch (e:
Cancelled)` ohne Weiterwurf ist erlaubt — NL24 sagt, was der Bereich dann tut. **Bricht: unter B
nein, unter A major.** **Hängt ab von:** NL6, NL12-C, NL2, NL24, NL38 (das ist eine Spec-Regel, kein
Bibliotheksdetail).

---

### NL20 — Woran erkennt ein Aufrufer, dass eine Funktion nur in einer Task laufen darf?

**Heute: an nichts** (gemessen `r12`: `net.accept` aus `main` → `LYR-VM0013`, ohne dass der Benutzer
`yield` schrieb; Signatur gelesen `net.lyr:112`; nur Prosa `net.lyr:5-7`).

**Der Rahmen, den die zweite Fassung nicht kannte:** `spec/10:72-77` verwirft eine `yields T`-Klausel,
weil sie „every yielding helper" färbt. Jede Option hier muss sagen, warum sie **nicht** diese
Klausel ist.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | Spec-Stand | Panic, der ein `yield` nennt, das der Benutzer nie schrieb |
| B | Doku-Attribut `@MayYield`, geprüft nur wo es steht, **ohne** Pflicht für Aufrufer; `lyrdoc` und Editor-Clients zeigen es | Rust `#[must_use]`-Geist | **Nicht die verworfene Klausel**: sie verpflichtet keinen Aufrufer — muss aber in `spec/10:72-77` als Zusatz stehen, sonst liest es jemand als Färbung durch die Hintertür |
| C | B plus Ausbreitung durch den Aufrufgraphen als Warnung | — | **Das ist die verworfene Klausel als Warnung** — ein Aufrufer bekommt eine Meldung, weil sein Callee wartet. Nur mit Spec-Änderung |
| D | Bessere `LYR-VM0013`-Meldung: „diese Funktion wartet; rufe sie aus einer Task auf" | — | Billig; löst nichts vorher |

**Empfehlung: D sofort, B mit v5 und einem Zusatz in `spec/10:72-77`, C nicht** (*geändert: die zweite
Fassung führte C als Ausblick; gegen den Spec-Eintrag ist C nicht haltbar, ohne ihn zu ändern*).

**Bricht: nein.** **Warnstufe 4.x:** D. **Hängt ab von:** NL16, NL10-D, NL38, Gebiet Metaprogrammierung.

---
### NL21 — Welche Bereitschafts-Primitive trägt v5, und für wie viele Tasks?

**Heute.** `poll` = `Socket.Select`, O(n) (gelesen `NativeRegistry.cs:1112,1143`); Lyric-Seite
`toArray()` je Runde (`task.lyr:197`), `nearestDeadline` linear (`:214-225`), `wakeSleepers`
quadratisch (`:227-231`), `wakeWaiters` mit `removeAt` (`:265-276`). Gemessen §1.3h: 4 000 Sleeper
19,4 s, Kontrolle `Wait.Now` 15 ms; 299 tote Deskriptoren abräumen ~200 ms (`n6`). **Kein
`Select`-Limit bei 300 auf Windows** (gemessen `n6_select_300`), Unix **behauptet**. **`std.collections`
hat keinen Heap** (gelesen, grep über `collections.lyr`: kein Treffer für `heap`/`priority`) — B
schreibt ihn, rund 40 Zeilen Lyric.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen, Zielgröße ≤ ~200 | heute | „Server" ist dann kein Anwendungsfall |
| B | Nur die Lyric-Seite: Deadline-**Heap** (neu zu schreiben), Freilisten statt `removeAt`, fd-Arrays über Runden halten | jeder Event-Loop | `Select` bleibt O(n) |
| C | B plus Native mit epoll/kqueue/IOCP | libuv, `mio`, .NET `SocketAsyncEngine` | Drei Plattformpfade |
| D | C plus Zielgröße in der Spec, Lasttest ohne CI-Gate | Go, Erlang | Wanduhr-Tests streuen (Commit 5303e250) |

**Empfehlung: B sofort, C mit v5, D als Aussage.** Der Satz für v5: „die Scheduler-Politik ist Lyric,
die Bereitschaftsabfrage ist ein Native". **Bricht: B nein; C nein für Quelltext**, Konvention
ändert sich (NL8-D). **Hängt ab von:** NL8-D, NL5, NL29, NL41 (Broadcast vervielfacht die Wakes), NL42.

---

### NL22 — `std.io.file` neben `std.io.stream`: bleibt die synchrone Form?

**Heute — die zweite Fassung war hier falsch.** Sie schrieb „Heute. Gar nicht." und schlug einen
Rule-2-Ausnahme-Threadpool vor. **Das ist seit 4.2.0 gebaut** (gelesen `stream.lyr:43-46`,
`NativeRegistry.cs:2393-2411,2501`, `CHANGELOG.md:702-705`; gemessen `p2`: Ticks laufen während
zehnmal 2 MB gelesen werden). Was blockiert, ist **`std.io.file`** (gemessen `s3`: 418 ms), und das
ist **Absicht**: „`std.io.file` reads a file in one call and holds the thread while it does. That is
right for a config file and wrong for two things: a file too large to hold, and a program with
tasks" (gelesen `CHANGELOG.md:702-705`). Die Capability-Tabelle sagt dasselbe: `std.io.stream`
trägt `fileAccess` und holt `osAccess` über `std.task`, „a program reading a config file must not
pay for a scheduler it never starts" (gelesen `Capabilities.cs:55-58`).

Die echte Frage: **bleibt `std.io.file` als bewusst synchrone Form neben `std.io.stream`, und woran
sieht eine Task, dass sie die falsche nimmt?**

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `file` bleibt synchron und wird **markiert** (NL20-B / NL10-D) | Node `readFileSync` neben `readFile` | Ehrlich, billig; eine Task, die `file.text` ruft, hält weiter alles an — aber sie wird gewarnt |
| B | `file.*` delegiert **innerhalb eines Resumes** an `stream` (NL10-C-Form) | gevent | Zwei Verhalten unter einem Namen; `file` müsste `osAccess` erben — bricht die „config file"-Zusage aus `Capabilities.cs:55-58` |
| C | `file.*` entfernen, nur `stream` | — | Bricht jedes Config-Programm und die Capability-Zusage |
| D | Eigener Threadpool für `file.*` | — | **Existiert schon — für `stream`**; ein zweiter wäre Rule 2 |

**Empfehlung: A.** *Geändert: die zweite Fassung empfahl „A jetzt, C [Threadpool] mit v5" und eine
Rule-2-Umformulierung dafür — der Threadpool ist gebaut, die Umformulierung dafür hat der Maintainer
am 2026-08-27 stillschweigend beantwortet.* Was NL15 an Rule 2 ändert, gilt hier mit — aber NL22
wartet nicht mehr darauf.

**Bricht: nein.** **Warnstufe 4.x:** NL10-D für `file.*` in einem yieldenden Körper — „use
`std.io.stream` inside a task". **Hängt ab von:** NL10, NL20, NL35 (ob der notify-Mechanismus
öffentlich wird).

---

### NL23 — Was liefern `next()` und `resume`, wenn eine Koroutine mit einem ERGEBNIS endet?

**Heute drei Formen** (gelesen `spec/10:25-31`, gemessen `r15`). `resume` auf erschöpft panikt
(`spec/10:22-24`). Nach einem Wurf: erschöpft (gemessen `p3`, NL34).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `next()` wird `enum Step<Y, R> { Yielded(Y), Returned(R) }` | **Python `StopIteration.value`, JS `{value, done}`** (Rusts `CoroutineState` ist Nightly — korrigiert) | `while (co.next() != null)` fällt; jede Schleife matcht |
| B | Zweites Mitglied `result(): ?R`, gültig erst wenn erschöpft | JS getrennt gelesen, C# `Task.Result` | Zwei Aufrufe; `result()` vor dem Ende — `null`? Panic? — und **`spec/10:33-35` verbietet eine Abfrage ohne Ziehen**, `result()` wäre eine |
| C | `resume` auf erschöpft liefert `R` | Lua | Dynamische Unterscheidung in einer statischen Sprache; ändert einen Panic |
| D | `R` nur auf Task-Ebene | — | `Coroutine<Y,R>` wäre ein Parameter, den nur `std.task` liest |

**Empfehlung: A, `bool`-Form aufgeben.** A macht aus drei Formen und einem Verbot einen Mechanismus —
der eine Ort, an dem wirklich etwas verschwindet. B scheitert an `spec/10:33-35`. **Bricht: major.**
**Warnstufe 4.x:** Warnung auf `Coroutine<void>.next()` als `bool`; `lyrfix` für die `?T`-Schleife.
**Konformanz (NL38):** `next_on_void_answers_bool.lyr` und `next_on_optional_yield_is_refused.lyr`
retirieren; `next_pulls_values_then_null.lyr` wird zu `next_pulls_values_then_returned`. **Hängt ab
von:** NL1, NL11, NL13, NL34, Gebiet Enums.

---

### NL24 — Abbruch während des Abwickelns; Aufräumarbeit, die nicht endet

**Heute.** Ein `defer` parkt unter dem Scheduler (gemessen `s9`: 200 ms). `LYR-SEM0110` läuft seit 4.6
(gelesen `STATUS.md:50-52`), die Regel ist bis v5 offen (gelesen `SPEC-RUNDE.md:120-124`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Nichts | — | Hängende Aufräumarbeit ist ein Leck |
| B | Nicht-abbrechbare Region: während eines `defer`-Abwickelns kein zweites `Cancelled` | Kotlin `NonCancellable`, Haskell `uninterruptibleMask` | Ein impliziter Zustand je Task |
| C | Aufräumfrist am **Bereich** (NL7) | Erlang `kill`, Kubernetes grace period | Ein Zeitwert; „hart verworfen" = Deskriptoren lecken bis `Dispose` |
| D | `kill()` als zweiter Mechanismus | Erlang | Rule 2; Lyric hätte Deskriptorlecks |

**Empfehlung: B als Regel, C am Bereich, D nicht.** Plus der Spec-Satz für „`Cancelled` gefangen und
weitergelaufen": der Bereich wartet, `join()` sieht kein `Cancelled`. **Bricht: nein.** **Hängt ab
von:** NL6, NL13, NL19, NL7, SPEC-RUNDE §5.

---

### NL25 — Was passiert bei `Wait.Any` mit den verlierenden Armen?

**Heute.** Frage existiert nicht (eine Spalte). Mit NL5-A sofort: `readSome` yieldet `Wait.Readable`
und liest **danach** (gelesen `net.lyr:169-181`, `stream.lyr:135-144`); ein Arm, der nach dem Lesen
aufgegeben wird, verliert Bytes.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Nichts sagen | Rust vor der `select!`-Doku | Rusts bekanntester Fußangel-Ruf |
| B | Spec-Regel: ein Arm wird nur am Suspendierungspunkt aufgegeben; eine std-Funktion verbraucht zwischen letztem Yield und Ergebnis keinen Zustand | Rusts „cancellation safe" als Regel | Jede wartende std-Funktion prüfen — **alle zehn Stellen liegen in der std** |
| C | Zwei markierte Klassen von Armen | — | Partielles `Any` |
| D | Kein Aufgeben; Verlierer puffern | Go gepufferte Channels, Erlang Mailbox | Ein Puffer je Arm |

**Empfehlung: B als Regel, D für Channels.** Spec-Satz: „Ein `Wait`-Arm, der in einem `Wait.Any`
verliert, wird an seinem Suspendierungspunkt aufgegeben; eine Funktion, die zwischen ihrem letzten
Yield und ihrem Ergebnis beobachtbaren Zustand verbraucht, darf nicht in einem `Any` stehen."
**Bricht: nein.** **Hängt ab von:** NL5, NL4, NL18, NL38.

---

### NL26 — `spawn` ohne `run()`

**Heute.** Gemessen `r14_spawn_without_run.lyr`: `pending = 1`, Exit 0, kein Schritt, keine Warnung.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | Go | Ein Programm, das erfolgreich nichts tut |
| B | Compilezeit-Warnung: `spawn` ohne erreichbares `run` in `main` | — | Nur für `main` sicher |
| C | `main` zieht einen impliziten Wurzelbereich leer | Python `asyncio.run`, Swift `@main` | Bedeutungsänderung ohne Quelltextänderung; ein Server-Task hält `main` fest |
| D | Laufzeitwarnung am Ende von `main`: „N Tasks wurden nie gezogen" | **Python `RuntimeWarning: coroutine 'f' was never awaited`** (korrigiert — C#s `UnobservedTaskException` gehört zu NL2), Node „unsettled top-level await" | Eine Prüfung beim Herunterfahren |

**Empfehlung: D jetzt, B für `main`, C nicht.** NL7-B muss festlegen, ob der Wurzelbereich am Ende
**wartet** (C) oder **meldet** (D). **Bricht: D/B nein, C major.** **Warnstufe 4.x:** D. **Hängt ab
von:** NL7, NL1, NL17-C.

---

### NL27 — Bekommt eine Task eigenen Zustand?

**Heute.** Nein (gemessen `s8`). Jede Vergleichssprache hat einen (Kotlin `CoroutineContext`, Swift
`@TaskLocal`, Go `context.Value`, Erlang Prozesswörterbuch, C# `AsyncLocal<T>`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Parameter durchreichen | Go vor `context` | Klempnerei |
| B | `TaskLocal<T>`, vom Bereich vererbt | Swift `@TaskLocal` | Impliziter Kontext — deklariert und typisiert, anders als NL8-B |
| C | Untypisierte Karte | Erlang | Erlang rät selbst ab |
| D | Feld am Handle | — | Helfer ohne Handle kommen nicht heran |

**Empfehlung: B nach NL7.** Unter NL19-C reist der Abbruch-Token hierüber. Unter NL15: Modulglobale
je Isolat kopiert oder verboten — in die Spec. **Bricht: nein.** **Hängt ab von:** NL7, NL15, NL19, NL18.

---

### NL28 — Debuggen und Testen

**Heute, Debugger.** Backtrace nennt Funktion und Scheduler, nicht die Task (gemessen `m`, `r11`).
Frames suspendierter Chains liegen vor (gelesen `CoroutineChain.cs:31-37`), nicht auf dem CLR-Stack.
**Heute, Tests.** `std.test` ist nur Assertions (gelesen `test.lyr:23-91`); keine virtuelle Uhr
(`now()` ist der Native, `task.lyr:66-69`). **Aber die Scheduler-Globale ist je Test frisch**
(gemessen `n1`, §1.8) — `std.test` muss nichts zurücksetzen.

| Debugger | Form | Vorbild | Preis |
|---|---|---|---|
| A | Nichts | — | Ein Fehler in einer von 500 Tasks ist nicht lokalisierbar |
| B | Backtrace nennt die Task (Name NL17-B, Id NL18) | Erlang, **Kotlin Stack-Trace-Recovery/`DebugProbes` (Opt-in, kostet Laufzeit — korrigiert)** | Ein Feld je Task |
| C | B plus: Debugger listet suspendierte Chains | Kotlin-Debugger, Go-Dump | DAP braucht „Stack, der nicht der aktuelle ist" |
| D | Haltepunkte in suspendierten Chains | — | Teuer, kleiner Nutzen |

| Tests | Form | Vorbild | Preis |
|---|---|---|---|
| A' | Wanduhr | heute | Flaky (Commit 5303e250) |
| B' | Virtuelle Uhr: Zeitquelle injizierbar | Kotlin `runTest` | Ein Einstiegspunkt in `now()` |
| C' | Einzelschritt = NL9-B `step()` | Swift serieller Executor | Umsonst mit NL9-B |
| D' | Deterministische Reihenfolge | Loom, FoundationDB | Eigenes Projekt |

**Empfehlung: B; B' + C'.** **Bricht: nein.** **Hängt ab von:** NL17-B, NL18, NL9-B, NL27, NL39.

---

### NL29 — Was kostet eine suspendierte Task an Speicher — und was das Kopieren

**Heute, gemessen.** ≈ 200 Byte je gehaltenem Frame (§1.6); Wasserstand bleibt (gelesen
`CoroutineChain.cs:9-13`). Die Grenze ist **die Thread-Tiefe 1024 minus der Tiefe des Treibers**
(gelesen `Interpreter.cs:139,526,533` — *korrigiert: keine Kettenkonstante*).

**Und der Copy ist kalt teurer, als die zweite Fassung annahm.** Sie stützte NL29-C auf „~20 ns je
Frame" aus §1.1 — eine Kette, heiß. Neu gemessen (`n7`, 10 000 verstreute Ketten, §1.1): **86–98 ns
je Frame**, Faktor 4–5; eine Suspendierung aus Tiefe 40 kostet ~4 µs. Das ist die Zahl, an der
„Kopieren statt Halten" zu messen ist — und sie beschreibt schon **heute** den Copy (der Chain
kopiert bei jeder Suspendierung, gelesen `CoroutineChain.cs:10-11`); Option C fügt dem die
Allokation hinzu.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Wasserstand bleibt | heute | 8 KB je Verbindung, die einmal tief war |
| B | Schrumpfen bei flacher Suspendierung | Go (wachsende/schrumpfende Stacks) | Heuristik; Wachsen allokiert |
| C | Kopieren in dichten Puffer, Array freigeben | Lua, Wren | **~90 ns je Frame kalt** (gemessen) plus Allokation je Suspendierung — bei 100 000 Suspendierungen/s aus Tiefe 40 sind das ~0,4 s je Sekunde (Debug) allein für die Kopie, **behauptet** für Release |
| D | Zahl in der Doku, `stats()`-Eintrag | Go `MemStats` | Macht es sichtbar |

**Empfehlung: D jetzt; C nur mit einer Messung, die Allokation und Kopie *zusammen* im kalten Regime
zeigt — die liegt noch nicht vor.** *Geändert: die zweite Fassung schrieb „die Messung aus §1.1
spricht dafür"; sie sprach für nichts, weil sie das falsche Regime maß.* Und in die Spec: „die
Interpretertiefe ist auf 1024 Frames je Thread begrenzt; eine Kette hat 1024 minus die Tiefe ihres
Treibers" — eine Rechnung, keine Konstante.

**Bricht: nein.** **Hängt ab von:** NL21, Gebiet Bytecode/VM.

---

### NL30 — `runFor` gegen das Instruktionsbudget

**Heute.** `ExecutionBudget`: gezählt, nicht getaktet (`ExecutionBudget.cs:12-17`); Ablauf ist ein
Panic (`:58-62`); teilbar (`:20-23`, `Reset()` `:52-54`); erreichbar über `ScriptInstance.Call`
(`:85-94`). **Und ein Budget bedeutet Interpreter** (gelesen `Interpreter.cs:248-249`) — NL44.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `runFor(millis)` | — (Unity ist B, nicht A — korrigiert) | Zwei Währungen für „genug für jetzt"; Budget-Ablauf tötet das Skript |
| B | `runFor(instructions)` — gezählt | `ExecutionBudget` selbst | Kalibrieren über `Consumed`; **deterministisch — und interpretiert (NL44)** |
| C | Erschöpfbares, fortsetzbares Budget neben dem tödlichen | .NET `CancellationToken` neben `Thread.Abort` | Zweiter Budgettyp — zwei echte Absichten |
| D | Kein Budget, `step()` macht eine Runde (NL9-B) | JS-Tick | Frist = Rundenlänge, unbegrenzt |

**Empfehlung: D jetzt (mit NL9-B), B plus C mit v5 — und der Preis von B/C steht jetzt in der
Tabelle: ein Budget schaltet den JIT ab.** Spec-Satz: ein Budget-Ablauf während `runFor` beendet
das Skript, nicht den Frame, solange C nicht existiert. **Bricht: nein.** **Hängt ab von:** NL9,
NL28-C', NL44, Gebiet Einbettung.

---
## 3c. Nach der Kritik ergänzte Designfragen (NL31–NL44)

Vierzehn Fragen, die die Kritik als fehlend benannt hat. Drei davon (NL39, NL40, NL42) sind durch
Messung oder Lesen bereits so weit beantwortet, dass die Frage kleiner wird — sie stehen trotzdem,
mit dem Befund.

---

### NL31 — Asynchrone Generatoren: wie wartet eine lazily gezogene Sequenz?

**Heute.** Gemessen `p1_nested_gen_wait.lyr` (rev4, §1.2): ein `Coroutine<int>`, der in einer Task
per `next()` gezogen wird, panikt mit `LYR-VM0015`, sobald ein Helfer unter ihm `Wait.Sleep` yieldet.
Kontrolle `p1b` ohne Warten läuft. Normativ `spec/10:63-66` (§10a Regel 2: „A yield suspends the
NEAREST running resume of its own chain … Each pull drives exactly one chain one step"). Ein
Generator, dessen Erzeugung auf E/A wartet — Zeilen aus einem Socket, Datensätze aus einem
Kindprozess — ist heute in einer Task **nicht schreibbar**, außer als Klasse mit `next()`, deren
`next()` selbst yieldet (das geht: gemessen `r10_yield_in_iterator_next.lyr`, weil der Yield dann die
*Task*-Kette trifft, nicht eine Generator-Kette). Das ist die Sync-/Async-Spaltung von C#, Python
und JS — als Panic.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen, in Spec und NL11 **ausdrücklich** als Panic dokumentiert; wartende Sequenzen sind `Iterator<T>`-Klassen mit yieldendem `next()` | Lyric heute (`r10` zeigt den Weg) | Der elegante Weg (Generator) und der wartende Weg (Klasse) sind zwei Mechanismen für „lazy Sequenz" — Rule 2 |
| B | **Delegation**: ein `Wait`-Yield, der eine Nicht-`Wait`-Kette trifft, wird **durchgereicht** zum nächsten Resume, dessen Kette `Wait` yieldet; der Generator bleibt suspendiert, seine Task parkt, beim Wecken läuft der Generator weiter | JS `yield*`, Python `yield from` (dort explizit; hier implizit nach Typ) | Regel 2 wird „NEAREST running resume **whose element type matches**" — eine dynamische Suche in der Resume-Kette; und was, wenn *zwei* Ketten `Wait` yielden? Die innere gewinnt — muss in die Spec. Ein Generator, der aus `main` gezogen wird, panikt weiter (kein `Wait`-Resume) |
| C | Eigener Typ `AsyncIterator<T>` / `Coroutine<T, waits Wait>` | C# `IAsyncEnumerable`, Python `__aiter__`, JS `async function*` | Zwei Generatorwelten — genau die Spaltung, die die Ungefärbtheit vermeiden sollte, jetzt am Generator statt an der Funktion |
| D | `Step<Y, R>` aus NL23 um `Waiting(Wait)` erweitern: `next()` auf einem Generator in einer Task kann `Waiting` liefern, der Aufrufer yieldet es weiter | — | Jeder Generator-Verbraucher muss `Waiting` behandeln — das ist C mit anderem Namen |

**Empfehlung: B, weil es die einzige Form ist, die keinen zweiten Typ und keine Färbung einführt —
aber mit einem Preis, der in die Spec muss.** B ändert §10a Regel 2 von „nearest" zu „nearest whose
element type matches", und das ist eine echte Regeländerung mit einem neuen Konformanzfall
(`since: 5.0.0`) und der Retirierung des `VM0015`-Falls für diesen einen Pfad. Die Rechnung ist
aufrichtig gestellt: heute ist das Verhalten ein Panic, der zu nichts gut ist — kein Programm
verlässt sich darauf. Der Fall, in dem B *nicht* eindeutig ist (zwei `Wait`-Ketten geschachtelt),
ist heute `LYR-VM0014`-Territorium (`run()` in einer Task, gemessen `l2`) und wird durch NL7
entschärft. A ist die ehrliche Rückfalloption und sollte in jedem Fall in `guide/11` stehen, bis B
gebaut ist.

**Bricht: B major** (Regel 2 der §10a); A nein. **Warnstufe 4.x:** die `VM0015`-Meldung (NL43) sagt
für diesen Fall „a `Wait` reached a `Coroutine<int>` — a generator pulled inside a task cannot wait;
make the waiting an `Iterator<T>` with a yielding `next()`".

**Hängt ab von:** NL11 (die Konformanz deckt sonst nur die Hälfte), NL23 (Form von `next()`), NL38,
NL43.

---

### NL32 — Scheduler hinter `osAccess`: darf ein sandboxter Gast Tasks benutzen?

**Heute.** `std.task` verlangt `osAccess` (gelesen `Capabilities.cs:64-67`: „The scheduler's poll
blocks the thread on the OS clock — sleeping is the same question asked slowly, so it takes the same
bit"; normativ `spec/04-modules.md:164,169`). Gemessen rev4 p6: `--grant none` und `--grant file,net`
→ `LYR-CAP0001`, das Modul lädt nicht. Ein Game-Mod ohne `osAccess` — der Hauptfall der Einbettung
(`CLAUDE.md` §Was Lyric ist) — kann **keine** Task spawnen, auch keine mit `Wait.Now` allein, und
keinen `Coroutine<Wait>` schreiben, weil `Wait` im gesperrten Modul liegt.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen; ein Mod schreibt seinen Scheduler über `Coroutine<T>` selbst | heute (`guide/11:189-214` zeigt das Muster) | Jeder Host erfindet `Wait` neu; `std.io.net` ist für den Mod ohnehin gesperrt |
| B | `std.task` zerfällt: ein **capability-freier Kern** (`Wait`, `spawn`, `step`, `Wait.Now`, Ready-Spalte, `Wait.Signal`) und ein `osAccess`-Teil (`poll`, `Wait.Sleep`, `Readable`, `Writable`, `run` mit Blockieren) | Zig `Io` in der Richtung; Deno-Permissions | Zwei Module für einen Scheduler — oder ein Modul, dessen Capability je Funktion gilt (das kennt Lyric nicht: Capability ist je Modul, `Capabilities.cs`) |
| C | B, und die **Uhr kommt vom Host**: `Wait.Sleep` im Kern, die Zeitquelle ist ein `RegisterFunction` des Hosts (Frame-Zeit statt OS-Zeit) | Unity `Time.deltaTime`, Bevy | Ein Native, das nicht die std stellt — das ist das Host-Modul-Muster (`HostModuleSource`, gelesen `LangVm.cs`), also nichts Neues |
| D | Ein neues Capability-Bit `taskAccess` unterhalb von `osAccess` | — | „a new bit would be a contract change for every older runtime" (`Capabilities.cs:61-62`) |

**Empfehlung: B mit C — und zwar so, dass der Kern das Modul ist, das ein Host-Scheduler ohnehin
braucht (NL9-B, NL28-C').** Die Begründung in `Capabilities.cs:64-67` gilt für `poll`, nicht für
`Wait.Now` und eine Warteliste: eine Ready-Spalte fragt das OS nichts. Die Trennung entlang
„fragt das OS / fragt es nicht" ist dieselbe, die `std.io.stream` gegen `std.io.file` zieht
(`Capabilities.cs:55-58`). D ist von der std selbst ausgeschlossen.

**Bricht: minor** (Importpfade: `std.task { Wait, spawn }` muss weiter funktionieren — der Kern
behält den Namen, der OS-Teil bekommt einen neuen, oder der Kern re-exportiert). **Hängt ab von:**
NL8, NL9, NL3 (der Waker gehört in den Kern), NL30, NL38 (die Capability-Tabelle ist Spec:
`spec/04-modules.md:164`), Gebiet Module/Capabilities.

---

### NL33 — Was wird aus dem existierenden globalen Waker `interrupt()`?

**Heute.** Gemessen `p7` (rev4): `interrupt()`/`Wait.Interrupt` funktioniert als Waker ohne
Leerrunden (3 Parks, 0 Leerrunden, +124 ms). Gemessen `p4`: sticky (+14 ms nach einem `interrupt()`
vor dem `spawn`). Gelesen `task.lyr:44-48` („REMEMBERED and taken by the next task to park"),
`:29-32` (der Shutdown-Sitz), `:37-39,49-53` und `NativeRegistry.cs:978-980` (Ctrl+C wird
geschluckt, solange jemand parkt), `:1174-1180` (`interrupt` = `RaiseInterrupt`, „What makes a
‚quit' command and a signal one mechanism instead of two"). Und er ist **pub** — jede Bibliothek
kann ihn rufen und fährt damit den Shutdown-Pfad der Anwendung.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen; `interrupt()` bleibt der eine Waker, Bibliotheken benutzen ihn | heute (`p7` zeigt, dass es geht) | Untypisiert, Broadcast, sticky — und **Ctrl+C ist tot, solange ein Channel-Consumer parkt**. Ein Server mit einem Channel kann nicht mehr per Signal beendet werden |
| B | `Wait.Interrupt` wird zu einem **vordefinierten Waker** (`std.task.signal: Waker`), `interrupt()` = `wake(signal)`; das Ctrl+C-Schlucken bleibt an diesem einen Waker | Go `signal.Notify(ch)` (ein Channel je Signal), Erlang `trap_exit` | Der Sonderfall (Ctrl+C-Handler, sticky) sitzt an einem Waker statt an einer Variante — `poll`s `wantInterrupt` wird „ist jemand auf `signal` geparkt" |
| C | `interrupt()` bleibt, `Wait.Signal(Waker)` kommt daneben — zwei Mechanismen | — | Rule 2; und Bibliotheken werden weiter `interrupt()` nehmen, weil es da ist |
| D | `interrupt()` wird nicht mehr `pub`; nur Ctrl+C und der Host (`LangVm`) dürfen es | — | Ein „quit"-Kommando kann sich nicht mehr selbst beenden — genau der Fall, für den es gebaut wurde (`task.lyr:44-46`) |

**Empfehlung: B.** `Wait.Interrupt` bleibt als Name (Kompatibilität), ist aber definiert als „parke
auf dem Signal-Waker"; `interrupt()` bleibt `pub` und weckt ihn. Was verschwindet, ist der Anreiz,
ihn als Channel-Waker zu missbrauchen — weil es einen typisierten gibt. Die Sticky-Eigenschaft
gehört zum Signal-Waker und **nicht** zu allgemeinen Wakern (ein Channel-Waker, der sticky ist,
weckt einmal zu viel); das muss in die Spec.

**Bricht: nein** (B ist eine Umdefinition ohne Quelltextwirkung; `wakeInterrupted`, `task.lyr:207-212`,
wird der Wake des Signal-Wakers). **Warnstufe 4.x:** ein Doku-Satz in `task.lyr:44-48`: „not a
general waker — a task parked here swallows Ctrl+C". **Hängt ab von:** NL3, NL14, NL15 (der
prozessweite Signalpfad: welcher Worker sieht Ctrl+C).

---

### NL34 — Zustand nach einem Wurf

**Heute.** Gemessen rev4 `p3_throw_then_pull.lyr`: `first 1`, `caught boom`, dann `co.next()` →
`null` — erschöpft, kein Panic, kein zweiter Wurf. Ableitbar aus `spec/10:36-38` (die Ausnahme
verlässt den Pull) und `spec/10:89-90` (Exit „by an exception unwinding the chain"), aber nirgends
gesagt. `ChainState` kennt `Done`, aber kein `Failed` (gelesen `CoroutineChain.cs:22-28`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Erschöpft, wie gemessen; Spec-Satz nachtragen | Python (`StopIteration` nach einer Ausnahme), JS (`done: true`) | `Task.join()` (NL2) auf einer geworfenen Task liefert die gespeicherte Ausnahme — die kommt aus dem Handle, nicht aus dem Chain; `close()` (NL13) auf einer geworfenen Koroutine tut nichts (nichts mehr abzuwickeln — die `defer`s liefen beim Wurf) |
| B | Dritter Zustand `Failed`, sichtbar über `Step<Y,R>` (NL23) als `Threw(e)` | — | Die Ausnahme würde zweimal beobachtbar (am Pull und als Zustand) — und `spec/10:33-35` verbietet Zustandsabfragen ohne Ziehen |
| C | `resume` nach Wurf panikt (wie `resume` nach Ende), `next()` bleibt `null` | Lua („cannot resume dead coroutine") | Das ist heute schon so (gelesen `spec/10:22-24`; `next()` gemessen) — A und C sind dieselbe Antwort |

**Empfehlung: A (= C), als Spec-Satz.** Der Satz: „A body that throws has exited; the coroutine is
exhausted exactly as after a run-through, and its `defer`s ran while the exception unwound." Für
NL2: `join()` unterscheidet „durchgelaufen" (liefert `R`), „geworfen" (wirft `E`) und „abgebrochen"
(wirft `Cancelled`, NL19) — drei Ausgänge, ein Aufruf; für NL13: `close()` auf einer erschöpften
Koroutine ist ein No-op, nicht ein Fehler.

**Bricht: nein** (Dokumentation des gemessenen Verhaltens). **Konformanz:** ein Fall
`next_after_throw_answers_null.lyr`, `since: 4.0.0` (das Verhalten besteht seit der stackful-Umstellung —
**behauptet**, dass es seit 4.0 so ist; gemessen nur auf 4.6). **Hängt ab von:** NL2, NL13, NL23, NL38.

---

### NL35 — Wird der notify-Deskriptor zur öffentlichen Host-/Native-Schnittstelle?

**Heute.** `std.io.stream` und `std.process` lösen „eine abgeschlossene Host-Operation weckt eine
Task" über Pool-Thread → `Poke(notify)` → notify-fd → `Wait.Readable` (gelesen `stream.lyr:43-46,143,173`;
`NativeRegistry.cs:2393-2411` `StartRead`, `:2053-2075` `StartPump`, `:2501` `streamNotifyFd`,
`:2209` `procNotifyFd`). Gemessen `p2`: es funktioniert. Der Mechanismus ist **privat**: `StartRead`,
`Poke` und die `FileState`/`ChildState`-Tabellen sind Registry-intern; ein Host-Future, ein Timer,
ein Fenster-Ereignis eines Hosts haben keinen Weg, eine Task zu wecken — außer über einen echten
Socket. *Die zweite Fassung erklärte genau diesen Fall in NL3 für unlösbar („geht weiter leer aus")
— er ist gelöst, nur nicht öffentlich.*

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Privat lassen | heute | Jeder Host-Wecker ist ein Socket-Hack |
| B | `LangVm.RegisterAwaitable(): HostWaker` — der Host bekommt ein Objekt mit `Signal()` (threadsicher), das Skript `yield Wait.Readable(w.fd)` bzw. `Wait.Signal(w)` | libuv `uv_async_send`, Rust `Waker::wake` von einem fremden Thread | Ein Self-Pipe-Paar je `HostWaker` (wie heute je Datei); `Select` sieht jeden |
| C | **Ein** notify-Deskriptor je VM plus eine gelockte Weckliste; `HostWaker.Signal()` schreibt in die Liste und pokt den einen Deskriptor; `poll` leert die Liste und nennt die Waker bereit | libuv (ein `uv_async_t` genügt für viele Sender), .NET `SynchronizationContext.Post` | `poll`s Antwortformat (`[now, readyFd...]`) bekommt Waker-Ids statt nur Deskriptoren — oder Waker *sind* Deskriptoren in derselben Nummernfolge (`_nextSocketFd` ist ohnehin ein VM-weiter Zähler für Sockets, Kinder, Dateien) |
| D | Der Host ruft `wake(w)` per `ScriptInstance.Call` — ein Native, das zurückruft | Interpreter.cs:160-177 erlaubt es | **Funktioniert nur, wenn die VM nicht in `poll` blockiert** — sonst wartet der Host-Thread auf den Lock, den der Skript-Thread hält. Und die VM ist „one thread" (`ExecutionBudget.cs:25`): ein zweiter Thread darf `Call` nicht rufen |

**Empfehlung: C — und damit werden NL3-C, NL14-B und diese Frage EIN Typ:** ein `Waker`/`Descriptor`
ist eine Nummer aus `_nextSocketFd`, hat optional einen OS-Socket dahinter (Netz), optional einen
Pool-Thread (Datei, Kind), optional einen Host (`HostWaker`), und für In-Process-Wecken gar nichts —
`wake` aus Lyric pusht direkt in `ready`. `poll` kennt eine Kategorie mehr („von der Weckliste
bereit"), sonst nichts. D ist ausdrücklich abzulehnen, und der Grund steht im Repo: eine
Einthread-Laufzeit, in die ein zweiter Thread ruft, ist der Fehler, den 4.3.0 mit „disposing from a
third thread" gerade erst sauber gemacht hat (`STATUS.md:629-630`).

**Bricht: nein** (Host-API additiv; `poll`-Format intern). **Hängt ab von:** NL3, NL14, NL21 (C
hält die Deskriptorzahl klein — ein Deskriptor je VM statt je Waker), NL32 (ein `HostWaker`
braucht keinen `osAccess` — er fragt das OS nicht), Gebiet Einbettung.

---

### NL36 — JIT × Yield: bleibt yield-fähiger Code für immer interpretiert?

**Heute.** Gelesen `JitCompiler.cs`: kein `yield`-Fall, `default: return false` (`:679`); ein Aufrufer
eines nicht kompilierten Callees wird ebenfalls abgelehnt (`:718-725`). Gemessen `p5 --jit`: läuft
unverändert — abgelehnt, nicht panikt. Folge: **jede Funktion, von der aus ein Yield erreichbar ist,
und jeder ihrer Aufrufer bis hinauf zu `main`, bleibt interpretiert.** In einem Server, dessen
Handler `readSome` rufen, ist das der gesamte Anwendungscode. Ein kompilierter Frame wäre eine
Yield-Wand (`spec/10:78-81` Regel 4). Niemand sieht, welche Funktion warum interpretiert bleibt.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Akzeptieren, dokumentieren: „der JIT ist für reine Rechenfunktionen" | heute | Server-Code ist nie schnell; und der `--jit`-Schalter tut dann für ein Task-Programm **nichts**, ohne es zu sagen |
| B | Der JIT kompiliert **Blätter** unter einem yieldenden Pfad weiter (reine Helfer), nur der Pfad selbst bleibt interpretiert — das erfordert, dass ein kompilierter Frame von einem interpretierten gerufen werden darf, ohne dass die Ausnahme-Kette bricht (`:718-725` nennt das als Grund der Ablehnung) | HotSpot (OSR, gemischte Frames) | Ausnahmen müssen durch kompilierte Frames unwinden — heute der Grund des Verbots |
| C | Kompilierte Frames werden **suspendierbar**: der JIT emittiert für jeden Callsite einen Zustandspunkt | LuaJIT (kann C-Frames nicht, Lua-Frames schon), Kotlin (CPS im Compiler) | Das ist die Zustandsmaschine zurück, nur im JIT — die größte VM-Arbeit des Gebiets |
| D | A plus **Sichtbarkeit**: `lyrvm run --jit --jit-report` nennt je Funktion „compiled / declined: reaches yield in X / declined: callee Y declined" | .NET `DOTNET_JitStdOutFile`, V8 `--trace-opt` | Ein Report; ändert nichts, macht die Grenze sichtbar |

**Empfehlung: D sofort, B für v5 prüfen, C nicht.** Das Gebiet Laufzeit/VM hat die Ausnahme-über-
kompilierte-Frames-Frage vermutlich schon auf dem Tisch (`:718-725` ist eine Begründung, kein
Naturgesetz) — **behauptet**, nicht gelesen, dass dort ein Posten steht. Was das Nebenläufigkeits-
gebiet beiträgt: Regel 4 muss bleiben (ein kompilierter Frame ist eine Wand), und B verletzt sie
nicht, weil ein Blatt nicht yieldet.

**Bricht: nein.** **Warnstufe 4.x:** D. **Hängt ab von:** Gebiet Laufzeit/VM (JIT), NL44 (Budget
schaltet ihn ohnehin ab), NL38 (Regel 4).

---

### NL37 — Rechenintensive Tasks: Preemption oder Blockierer?

**Heute.** Gemessen rev4 `n4_cpu_bound.lyr`: eine Lyric-Schleife ohne Yield hält den Scheduler 5,2 s
(Debug, 20 Mio. Iterationen); der Ticker steht wie bei einem blockierenden Native. Normativ
`task.lyr:4` „no preemption", `guide/13:149`. Der Instruktionszähler existiert: `ExecutionBudget`
(`ExecutionBudget.cs`) zählt jede Instruktion, wenn ein Budget gesetzt ist — und schaltet dann den
JIT ab (`Interpreter.cs:248-249`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Kein automatischer Yield; eine CPU-Task ist ein Blockierer wie ein Native | heute; Lua, JS (Event-Loop), Python asyncio | Ein Anfänger, der in einer Task rechnet, hält den Server an — und sieht es nicht |
| B | **Automatischer Yield-Punkt** alle N Instruktionen, wenn ein Resume läuft (Reduktionen) | Erlang (2 000 Reduktionen), Go ≥ 1.14 (async preemption) | Ein Zähler in der heißen Schleife **immer**, nicht nur mit Budget; und Regel 4: der Yield darf nur treffen, wenn kein nativer Frame dazwischenliegt — sonst Panic mitten in fremdem Code. Und: ein Yield an einer Stelle, die der Programmierer nicht geschrieben hat, ist ein Suspendierungspunkt, den NL25 nicht kennt — jede Invariante zwischen zwei Anweisungen wird unterbrechbar. **Das ist der Punkt, an dem Preemption das Modell „ein Task läuft bis er yieldet" bricht**, und das Modell ist der Grund, warum Lyric keine Mutexe braucht |
| C | `Wait.Now` **explizit** in Rechenschleifen, plus Diagnose: `stats()` (NL17-C) weist die Schrittdauer je Task aus, und eine Warnung „task X ran 5 219 ms without yielding" | JS („long task" im Profiler), Python asyncio `debug=True` („Executing … took 5.2 seconds") | Kooperativ bleibt kooperativ; der Programmierer muss es tun — aber er erfährt es |
| D | B nur unter Budget (wo der Zähler ohnehin läuft) | — | Zwei Verhalten je nachdem, ob ein Host ein Budget gab — Rule 2 |

**Empfehlung: C — ausdrücklich gegen B, mit der Begründung in der Spec.** Preemption ist nicht ein
Feature mehr, sondern ein anderes Modell: sobald ein Task an einer ungeschriebenen Stelle
suspendiert werden kann, braucht jeder geteilte Zustand (Modulglobale, §1.5) eine Sperre — Go
liefert deshalb Mutexe und einen Race-Detector, Erlang isoliert Heaps. Lyric hat weder und braucht
weder, **solange** Suspendierungspunkte geschrieben stehen. Der Preis von C ist gemessen (5,2 s) und
mit NL17-C sichtbar; asyncio hat denselben Weg gewählt und meldet lange Schritte im Debug-Modus.

**Bricht: nein.** **Warnstufe 4.x:** die Schrittdauer-Warnung ist sofort machbar (eine Uhr um
`task.next()` in `step`, `task.lyr:154`). **Hängt ab von:** NL17-C, NL44 (der Zähler ist der
Budget-Zähler), NL38.

---

### NL38 — Spec-first und Konformanz: welche Empfehlung ändert welche Regel?

**Heute.** Das Projekt arbeitet spec-first (Regel-PR mit `since:`-Gates, dann Zwilling, dann
Release+Pin). `lyric-spec/spec/10-coroutines.md` ist normativ; `conformance/cases/10-coroutines/`
hat 14 Fälle (gelesen, `ls`), im Format `//! run`, `//! since: 4.0.0`, `//! panic: LYR-VM` (gelesen
`yield_outside_a_resume_panics.lyr:1-3`). **`std.task` hat keinen Spec-Abschnitt** (gelesen: grep
über `spec/` trifft nur `04-modules.md:164,169` und `appendix-a:285`) — alles, was dieses Dossier
über `Wait`, `spawn`, `run` sagt, ist heute Bibliotheksvertrag, kein Sprachvertrag. Die zweite
Fassung hat die Spec kein einziges Mal zitiert; drei ihrer Empfehlungen liefen gegen normative Sätze.

**Die Tabelle, die die Kritik verlangt hat:**

| Empfehlung | Spec-Regel, die sich ändert | Neuer Konformanzfall (`since:`) | Retiriert |
|---|---|---|---|
| NL1-B `Coroutine<Y, R>` | `spec/10:20-21` (bare `return;`, `SEM0039`) wird zu „returns `R`" | `a_coroutine_returns_a_value` 5.0.0 | — (`bare_return_mid_body_is_the_run_through_exit` bleibt für `R = void`) |
| NL23-A `Step<Y, R>` | `spec/10:25-31` (`?T`, `bool`, `SEM0080`) | `next_answers_yielded_or_returned` 5.0.0 | `next_on_void_answers_bool`, `next_on_optional_yield_is_refused`, `next_pulls_values_then_null` |
| NL13-B `close()` | `spec/10:89-93` bekommt den Satz „`close()` is the one exit a driver can force; the collector still is not" | `close_runs_the_defers` 5.0.0 | — |
| NL13 kein `status()` | `spec/10:33-35` **bleibt** | — | — |
| NL12-C `throwInto` | `spec/10:39` bleibt (kein send); neuer Satz für `throwInto` | `throw_into_lands_at_the_yield` 5.0.0 | — |
| NL16-A | `spec/10:72-77` **bleibt** | — | — |
| NL20-B `@MayYield` | `spec/10:72-77` bekommt den Zusatz „a documentation marker binds no caller and is not this clause" | — (Attribut ist Metaprogrammierung) | — |
| NL20-C | **abgelehnt**, weil es die verworfene Klausel wäre | — | — |
| NL31-B Delegation | `spec/10:63-66` Regel 2: „nearest … whose element type matches" | `a_wait_passes_through_a_generator` 5.0.0 | `VM0015` für diesen Pfad — der Fall `i_dynamic_mismatch` bleibt für echte Typfehler |
| NL34-A | `spec/10:36-38` bekommt „a body that throws has exited" | `next_after_throw_answers_null` 4.0.0 (**behauptet**, dass es seit 4.0 gilt) | — |
| NL19-B `Cancelled` | `spec/09-errors.md` (typed throws) bekommt die Ausnahme | `cancelled_is_not_a_declared_throw` 5.0.0 | — |
| NL29 Tiefe | `spec/10` bekommt „1024 frames per thread, minus the driver's depth" | — (nicht CI-tauglich) | — |
| NL3/NL5/NL14 neue `Wait`-Varianten | kein Spec-Kapitel für `std.task` — **muss erst entstehen** (`spec/11-stdlib-contract.md`) | — | — |
| NL33 Signal-Waker sticky | dito | — | — |
| NL37 keine Preemption | dito: „a task runs until it yields; no instruction boundary is a suspension point" | — | — |

**Optionen** für den `std.task`-Vertrag:

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | `std.task` bleibt Bibliotheksvertrag außerhalb der Spec | heute | Jede Empfehlung hier ist dann „nur Bibliothek" — und `Wait` ist der Typ, den der gesamte I/O-Stapel von innen yieldet |
| B | Ein Abschnitt in `spec/11-stdlib-contract.md`: `Wait`-Varianten und ihre Bedeutung, `spawn`/`run`/`step`-Vertrag, „no preemption", der Signal-Waker, Broadcast oder exklusiv (NL41) | die bestehenden §11-Einträge | Ein Spec-PR **vor** jeder std-Änderung — das ist der Modus, kein Preis |
| C | `Wait` wandert als Sprachtyp in `spec/10` | — | Ein Bibliothekstyp in der Sprachspec; und `std.task` verlangt `osAccess`, ein Sprachtyp darf das nicht (NL32) |

**Empfehlung: B, und die Tabelle oben ist der Inhalt des ersten Spec-PRs dieses Gebiets.** Die
Reihenfolge ist nicht verhandelbar: kein `since: 5.0.0`-Fall darf existieren, bevor die Regel steht,
und der Spec-Pin wandert zuletzt. Was dieses Dossier an Spec-Änderungen *nicht* vorschlägt, ist
genauso Ergebnis: `spec/10:33-35` (kein `status()`), `:72-77` (kein `yields T`), `:89-93` (kein GC-Exit).

**Bricht: nein** (Prozess). **Hängt ab von:** allem — es ist die Klammer.

---

### NL39 — Test-Isolation des Schedulers (durch Messung beantwortet)

**Heute.** Gemessen rev4 `n1_isolation/` (§1.8): ein Test, der spawnt und nie zieht, hinterlässt dem
nächsten Test **derselben Datei** nichts — `pending() == 0`; Kontrolle reihenfolgeunabhängig (zwei
Tests spawnen je einen, beide sehen 1). Gelesen `guide/20:39-41` („fresh instance: module state
cannot leak"), `STATUS.md:2226-2229` (eine VM je Datei — geteilt sind *Ressourcen*, nicht Modulzustand).

Die Frage der Kritik — „muss `std.test` den Scheduler je Test zurücksetzen, bevor eine virtuelle
Uhr Sinn hat?" — ist damit **nein**. Was bleibt, ist kleiner:

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Nichts; die frische Instanz genügt | heute (gemessen) | Eine **Ressource** (Socket, Kind), die ein Test-Task hält, leckt in den nächsten Test — das ist der bekannte `STATUS.md:2226` Befund, nicht ein Scheduler-Befund |
| B | Die virtuelle Uhr (NL28-B') braucht einen Einstiegspunkt in `now()` (`task.lyr:66-69`) — je Instanz, was mit der frischen Instanz umsonst ist | Kotlin `runTest` | Ein `std.test.withVirtualClock(fn)` oder ein Modul-Setter |
| C | Ein `std.test`-Helfer `runTasks()`, der `run()` mit Stillstandserkennung (NL17-D) kapselt: ein Test, dessen Tasks nie fertig werden, schlägt fehl statt zu hängen | pytest-asyncio `timeout` | Braucht NL17-D oder eine Wanduhr-Frist — und die Wanduhr ist der Feind (5303e250) |

**Empfehlung: B, C mit NL17-D.** **Bricht: nein.** **Hängt ab von:** NL28, NL17-D, `STATUS.md:2226`
(Ressourcen je Test — Gebiet Testen).

---

### NL40 — Deskriptor-Wiederverwendung (durch Lesen beantwortet)

**Heute.** Warter werden by value geweckt (gelesen `task.lyr:252-254,265-276`). **Aber Nummern werden
nie wiederverwendet**: gelesen `NativeRegistry.cs:1439` (`private long _nextSocketFd` — Instanzfeld,
je Registry, je VM), `:1461` (Socket), `:2011` (Kind), `:2362` (Datei): jeder Handle nimmt
`++_nextSocketFd` aus **einem** monotonen Zähler. Ein geparkter, geschlossener fd wird beim nächsten
`poll` als tot erkannt und der Warter geweckt (`:1094-1102`, gemessen `t_bogus_fd`); die Nummer
bekommt nie wieder jemand. Das ABA-Szenario der Kritik (neuer Socket weckt alten Warter) **kann
innerhalb einer VM nicht auftreten.**

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen; der monotone Zähler **wird Spec-Satz** („a descriptor number is never reused within a VM") | heute | Ein `long` läuft nach 9·10¹⁸ Handles über — nicht in diesem Jahrhundert |
| B | Generation/Epoche im `Descriptor`-Typ (NL14-B) | Vulkan-Handles, Entity-IDs mit Generation | Löst ein Problem, das der Zähler schon löst |

**Empfehlung: A.** Der einzige Fall, in dem die Frage wiederkommt, ist NL15 (zwei VMs, zwei Zähler,
eine Nachricht, die einen Deskriptor trägt) — und dort ist die Antwort „Handles sind nicht
sendbar" (NL15-B), nicht eine Generation. **Bricht: nein.** **Hängt ab von:** NL14, NL15, NL38.

---

### NL41 — Thundering Herd: Broadcast oder exklusives Wecken je Deskriptor?

**Heute.** Gelesen `task.lyr:252-254`: „two tasks may wait on one descriptor, and both wake when it
readies — whoever runs second sees the state the first one left". Gemessen rev4 `n3_broadcast_wake`:
zwei Parker auf einem Listener, ein Client, **beide** wachen (+120 ms), `wakes = 2`. Mit
`net.accept` heißt das: der zweite Acceptor bekommt `-1` von `netAccept` und parkt erneut
(`net.lyr:112-123`) — korrekt, aber eine Leerrunde je überzähligem Warter. Mit NL5-A (`Wait.Any`)
parkt eine Task in mehreren Spalten und wird N-mal öfter geweckt; mit NL3 gilt dieselbe Frage für
`wake(w)` bei mehreren Parkern auf einem Waker.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Broadcast bleibt; Spec-Satz „every waiter wakes, and re-checks" | heute; POSIX `select` (weckt alle); Haskell `MVar` **nicht** (FIFO, einer) | N Acceptor-Tasks auf einem Listener → N−1 Leerrunden je Verbindung. Bei einem Acceptor je Listener (die Norm) kostet es nichts |
| B | Exklusiv: der **erste** Parker in Parkreihenfolge wird geweckt, die übrigen bleiben | Go netpoll (eine Goroutine je fd-Ereignis), Linux `EPOLLEXCLUSIVE`, Erlang-Mailbox (eine Empfängerin) | Wenn der Geweckte das Ereignis nicht verbraucht (er liest 10 Bytes von 100, oder er wird abgebrochen — NL25), schlafen die anderen weiter, obwohl Daten da sind — das ist das „lost wakeup"-Problem, das Broadcast per Definition nicht hat |
| C | Broadcast für Deskriptoren (Readiness ist level-triggered, ein Re-Check ist billig), **exklusiv für Waker** (`wake(w)` weckt einen, `wakeAll(w)` alle — die Channel-Semantik) | Rust `Notify::notify_one` / `notify_waiters`, Kotlin `Channel` (ein Empfänger je Wert) | Zwei Regeln — aber für zwei verschiedene Dinge: ein Deskriptor ist ein Zustand (level), ein Waker ist ein Ereignis (edge). Das ist nicht Rule 2, das ist der Unterschied zwischen Bedingung und Ereignis |

**Empfehlung: C, mit beiden Sätzen in der Spec (NL38).** A für Deskriptoren ist richtig, weil der
Re-Check in Lyric ohnehin die Schleife um jedes Yield ist (`net.lyr:112-123`, `stream.lyr:135-144` —
jede wartende std-Funktion ist `while (true) { try; if (wouldBlock) yield; }`); B dort einzuführen
kaufte lost wakeups für einen Fall, der nicht die Norm ist. Für Waker ist die Frage umgekehrt:
`Channel.receive` mit zwei Consumern und einem Wert **muss** exklusiv sein, sonst nimmt der zweite
einen Wert, der nicht da ist (NL25-D).

**Bricht: nein** (A ist Ist-Stand; C betrifft nur den neuen Waker). **Hängt ab von:** NL3, NL4, NL5
(`Any` vervielfacht die Broadcast-Wakes — der Preis von A skaliert mit der Armzahl), NL25, NL38.

---

### NL42 — Select-Grenzen und verschluckte Fehler

**Heute.** Gemessen rev4 `n6_select_300`: 300 geparkte Listener, der Client verbindet zum letzten, der
Acceptor wacht nach 5 ms — **keine Obergrenze bei 300 auf Windows/.NET 10**. Unix **behauptet**
(.NET implementiert `Socket.Select` dort über `poll()`, was keine `FD_SETSIZE`-Grenze hätte — nicht
gelesen, nicht gemessen). Gelesen `NativeRegistry.cs:1116-1120`: eine `SocketException` im
Sofort-`Select` → „naming nothing ready is the truth"; `:1145-1150`: im blockierenden `Select` →
„Answer the CLOCK and nothing else: the next turn resolves" — mit der Begründung, nur `Dispose` löse
das aus. Wenn ein anderer Grund eine `SocketException` wirft (ein Deskriptor, der zwischen zwei
Runden ungültig wurde — **behauptet**, dass das vorkommt), parkt die Task **bis zur nächsten
Runde**, und wenn nichts anderes die nächste Runde auslöst, für immer.

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen; die 300 gemessen als Aussage in die Doku | heute | Unix ungemessen; der `SocketException`-Fall bleibt eine Annahme über `Dispose` |
| B | Die `SocketException` **klassifizieren**: `Dispose` → Uhr; alles andere → jeden Deskriptor einzeln prüfen (`Poll(0)`) und die kaputten als tot nennen | libuv (ein `EBADF` wird dem Handle zugeordnet) | Ein zweiter Pfad im Native; O(n) im Fehlerfall, was in Ordnung ist |
| C | NL21-C (epoll/kqueue/IOCP) — dort ist ein kaputter Deskriptor ein Ereignis, kein Wurf | — | Erst mit NL21-C |
| D | Eine Zielgröße **mit Unix-Messung** in NL21-D („10 000 Deskriptoren auf Windows, Linux, macOS") | Go, Erlang | Ein Lasttest je Plattform, ohne CI-Gate |

**Empfehlung: B jetzt (die Annahme „nur Dispose" ist unbelegt und ihr Preis ist eine ewig parkende
Task), D mit NL21.** **Bricht: nein.** **Warnstufe 4.x:** B ist eine Native-Härtung ohne
Sprachwirkung. **Hängt ab von:** NL21, NL17-D (eine Stillstandsmeldung würde den ewig parkenden
Fall wenigstens sichtbar machen).

---

### NL43 — Diagnose von `LYR-VM0015`

**Heute.** Gemessen `p1`: `panic [LYR-VM0015]: yield in 'main.waitABit' does not match what the
running coroutine yields — the value's type must be the chain's element type`. Die Meldung nennt
**weder** den yieldeten Typ (`Wait`) **noch** den Kettentyp (`int`). Der Backtrace zeigt beide
Resume-Körper (`main.gen.<body>`, `main.worker.<body>`), sagt aber nicht, welcher „the running
coroutine" ist. Gelesen `VmDiagnostics.cs:88-90`: die Diagnose kennt den Grund („which chain it
meets is a runtime fact"), die Meldung trägt ihn nicht. Der Typ ist der Laufzeit bekannt:
`CoroutineChain` hält `yieldTag`/`yieldType` (gelesen `CoroutineChain.cs:20-21`).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | So lassen | — | Der Benutzer aus `p1` sieht „main.waitABit" und versteht nicht, warum ein `Wait` falsch ist |
| B | Beide Typen: „yield of `Wait` in 'main.waitABit' reached a `Coroutine<int>` (resumed in 'main.worker.<body>')" | Rust (E0308 nennt expected/found), C# CS0029 | Der Yield-Typ ist am Yield-Opcode statisch bekannt (der Compiler typt ihn, `spec/10:67-70`), der Kettentyp im Chain — beide liegen vor |
| C | B plus der Satz für den NL31-Fall: „a generator pulled inside a task cannot wait — make the waiting an `Iterator<T>` whose `next()` yields" | — | Ein Sonderfall im Meldungstext (Yield-Typ ist `std.task.Wait`) — vertretbar, weil es *der* Fall ist, den ein Anfänger trifft |

**Empfehlung: C, als 4.x.** Dasselbe Argument wie NL20-D für `VM0013`: die Meldung ist der einzige
Ort, an dem die Sprache dem Benutzer sagt, was er falsch gemacht hat, und beide Typen liegen vor.

**Bricht: nein** (Meldungstext; die Konformanz prüft `//! panic: LYR-VM`, nicht den Text — gelesen
`yield_outside_a_resume_panics.lyr:3`). **Warnstufe 4.x:** sofort. **Hängt ab von:** NL31, NL20-D,
Gebiet Diagnostik.

---

### NL44 — Budget schaltet den JIT ab: ist der deterministische Frame immer der interpretierte?

**Heute.** Gelesen `Interpreter.cs:247-249`: „Compiled code IS the whole call and needs no frame.
Only an unwatched run reaches it: a debugger or a budget means the interpreter, per
IExecutionPolicy." Gelesen `ExecutionBudget.cs:12-17`: gezählt statt getaktet, „which is what a
replay needs". Folge: NL30-B/C (gezähltes Budget für den Host-Pump) bedeutet **jeder Frame ist
interpretiert** — für eine Engine, die einen JIT einschaltet, um Skripte schnell zu machen, ist
das Budget der Schalter, der ihn wieder ausschaltet. Und NL36 sagt: der Task-Pfad ist ohnehin
interpretiert. Für einen Host, der Tasks pumpt, ändert das Budget also **nichts** an der
Geschwindigkeit — für einen Host, der reine Rechenfunktionen mit Budget ruft, halbiert es sie
(**behauptet** — der JIT-Faktor ist in diesem Dossier nicht gemessen).

| | Form | Vorbild | Preis |
|---|---|---|---|
| A | Akzeptieren: Budget = Interpreter, dokumentieren | heute | Ein Host muss zwischen „schnell" und „begrenzt" wählen |
| B | Der JIT bekommt einen Budget-Zähler: jeder Rücksprung und jeder Aufruf dekrementiert | HotSpot Safepoints, Go async preemption (Signal), WebAssembly fuel (`wasmtime` `consume_fuel`) | Ein Dekrement je Schleifenrücksprung in kompiliertem Code — wasmtime misst dafür ~10–30 % (**behauptet**) |
| C | Ein **getaktetes** Budget für kompilierten Code (Timer-Thread setzt ein Flag, Safepoint prüft es) | Go | Zweite Währung; nicht deterministisch — genau das, was `ExecutionBudget.cs:12-17` ablehnt |
| D | Budget zählt nur an Yield-Punkten und Aufrufen, nicht je Instruktion — dann kann der JIT es tragen, weil Aufrufe ohnehin über den Kontext gehen (`JitCompiler.cs:686-690`: „Asking the context costs one indirection") | — | Ein `while (true) {}` ohne Aufruf entgeht ihm — der Fall, für den das Budget gebaut wurde (`ExecutionBudget.cs:8-11`) |

**Empfehlung: A für v5, B als Posten für das Gebiet Laufzeit/VM — und NL30 trägt den Preis jetzt in
seiner Tabelle.** D fällt an seiner eigenen Begründung. Für das Nebenläufigkeitsgebiet ist die
Folge klar: der Host-Pump (NL9-B `step()`) sollte **ohne** Budget benutzbar sein — eine Runde ist
durch die Task-Zahl begrenzt, nicht durch Instruktionen, und ein Host, der Determinismus braucht,
setzt das Budget und zahlt den Interpreter.

**Bricht: nein.** **Hängt ab von:** NL30, NL36, Gebiet Laufzeit/VM (JIT), Gebiet Einbettung.

---
## 4. Was wir übernehmen sollten

Nach Hebel sortiert.

1. **Von Kotlin und Swift: Handle, Bereich, Abbruch — die API-Form** (NL1, NL2, NL7). Sie hängt
   nicht an Färbung. `Task<R>` / `Task<R> throws E` (Swift trennt per Typgleichheit `Failure ==
   Never` / `== any Error`; Lyric hat den `throws`-Suffix), `join()`, Bereich.

2. **Von Rust — und vom eigenen `std.io.stream`: der Waker ist der notify-Deskriptor** (NL3, NL14,
   NL35). Rusts `Waker: Send + Clone` und Lyrics Pool-Thread → `Poke` → `Wait.Readable` sind
   derselbe Mechanismus. Ein Typ für In-Process-Wecken (direkt in `ready`), Fremdwecken (ein
   VM-weiter notify-Deskriptor plus Weckliste) und OS-Deskriptoren. Gemessener Gegenwert: 10⁴
   Leerrunden → 0 (`k` gegen `p7`). *Geändert: NL3 ist jetzt die allgemeine Antwort, nicht nur die
   für In-Process — die Begrenzung der zweiten Fassung stand auf einer falschen Prämisse.*

3. **Von Lua 5.4: `close()` — ohne `status()`** (NL13). `close()` ist ein Pull mit Abwicklung und
   verstößt nicht gegen `spec/10:33-35`; `status()` tut es.

4. **Von Python und JS: `Step<Y, R>` statt dreier `next()`-Formen** (NL23). Der Ort, an dem wirklich
   ein Mechanismus verschwindet. Rust nur als Nightly-Entwurf zitiert.

5. **Von Python, JS und C#: der Generator IST ein Iterator — für die nicht-wartende Hälfte** (NL11),
   **und die Delegation eines `Wait`-Yields durch den Generator für die wartende** (NL31-B). C# hat
   für die Spaltung zwei Typen; Lyric kann sie mit einer Regeländerung in §10a Regel 2 vermeiden.

6. **Von Go: `select` als `Wait.Any`** (NL5), mit Rusts cancellation-safety-Satz **vorher** (NL25),
   und Gos exklusivem Wecken **nur für Waker**, nicht für Deskriptoren (NL41).

7. **Von C#: `CancellationToken` als ernste Alternative zum Einwurf** (NL6-C/NL19-C).

8. **Von Erlang und CPython: das Argument für Isolate** (NL15) — mit Erlangs eigener Nuance, dass
   große Binaries nicht kopiert werden. Rule 2 wird von „single-threaded" auf „kein geteilter
   veränderlicher Speicher" umformuliert — **was 4.2.0 faktisch schon getan hat** (NL22).

9. **Von Haskell und Kotlin: die nicht-abbrechbare Region** (NL24).

10. **Von der eigenen Spec: nichts tun bei NL16** — `spec/10:72-77` ist die Begründung, OCaml nur die
    Bestätigung. NL20-B nur mit einem Zusatz dort; NL20-C nicht.

11. **Von Python: die Warnung** (NL10, NL26 — `RuntimeWarning: coroutine … was never awaited`), und
    von asyncio der Debug-Modus für lange Schritte (NL37-C).

12. **Von Kotlin: die virtuelle Test-Uhr** (NL28) — die Scheduler-Isolation je Test gibt es schon
    (gemessen `n1`).

13. **Von Unity: `step()` je Frame** (NL9-B) — nicht `runFor(millis)`.

14. **Von Erlang: Reduktionen NICHT übernehmen** (NL37) — Preemption bricht das Modell, das Lyric
    Mutexe erspart. Stattdessen Sichtbarkeit.

**Ausdrücklich NICHT übernehmen:** `async`/`await`; Gos Shared-Memory-Goroutinen; Zigs `Io`-Parameter;
ein Effektsystem oder `yields T` (von der Spec verworfen); Javas checked `InterruptedException`;
deterministische Simulation; ein `status()`; Preemption; ein Host-Thread, der in die VM ruft (NL35-D).

---

## 5. Konflikte

**Mit `CONTRIBUTING.md` Rule 2:**

| Konflikt | Lage |
|---|---|
| `Coroutine<T>` neben `Iterator<T>` (NL11) | Besteht heute; die Konformanz lindert, entfernt nichts — **und deckt nur nicht-wartende Generatoren** (gemessen `p1`) |
| Drei `next()`-Formen (NL23) | Der eigentliche Rule-2-Befund; `Step<Y,R>` macht eins daraus |
| **`interrupt()` neben `Wait.Signal(Waker)`** (NL33) — **neu** | Heute existiert ein Waker, untypisiert und sticky; ein zweiter typisierter daneben ist Rule 2. Antwort: `Wait.Interrupt` wird ein vordefinierter Waker |
| **Generator-Klasse mit yieldendem `next()` neben Generator-Koroutine** (NL31) — **neu** | Für wartende Sequenzen ist heute nur die Klasse möglich; B (Delegation) macht die Koroutine wieder zum einen Weg |
| `run()` neben `step()`/`runFor` (NL9, NL30) | `run()` = Schleife über `step()` plus Blockieren; eine Funktion |
| `close()` neben `cancel()` (NL6, NL13) | Zwei Ebenen |
| `cancel()` neben `kill()` (NL24-D) | Abgelehnt |
| Mailbox neben Channel (NL4-C / NL15) | Zusammen entscheiden |
| **Worker-Isolates gegen „single-threaded"** (NL15) | Der Buchstabe bricht, die Eigenschaft nicht — **und der Buchstabe ist seit 4.2.0 nicht mehr wörtlich wahr** (`std.io.stream`, `std.process` mit Pool-Threads). NL22 braucht die Umformulierung **nicht mehr**; sie ist Nachtrag |
| `Cancelled` als Ausnahme (NL19) | Zwei Spec-Sätze; kein Rule-2-Konflikt |
| Ablageplatz je Task gegen „kein unsichtbarer Kontext" (NL27 / NL8-B) | Scheinkonflikt, begründet |
| **Broadcast für Deskriptoren, exklusiv für Waker** (NL41) — **neu** | Zwei Regeln für zwei Dinge (Zustand / Ereignis); in die Spec |
| **Capability-freier Task-Kern neben `osAccess`-Teil** (NL32) — **neu** | Zwei Module oder ein Modul mit Re-Export — dieselbe Trennung, die `stream`/`file` schon ziehen |

**Mit der Spec (spec-first, NL38):** drei Empfehlungen der zweiten Fassung liefen gegen normative
Sätze — `status()` gegen `spec/10:33-35`, asynchrone Generatoren gegen `:63-66`, `@MayYield`/NL20-C
gegen `:72-77`. Diese Fassung hält `:33-35` und `:72-77` und schlägt für `:63-66` eine
Regeländerung mit `since:`-Gate vor (NL31-B). Die vollständige Tabelle steht in NL38.

**Mit anderen Gebieten:**

| Gebiet | Was dort entschieden sein muss |
|---|---|
| Typen/Werte | `mut struct` — Gate für NL15 (Stand `STATUS.md:40-52`: „with v5", Warnung `SEM0109`) |
| Generics | bedingte Konformanz (NL11, ungebaut), generische Methoden, Konformanz-Synthese (NL18) |
| Enums/Pattern | `@NonExhaustive` — harte Vorbedingung für NL3/5/14/33 (gemessen `r3`); `Step<Y,R>` |
| Fehlerbehandlung | werfende Funktionstypen (NL7); **NL19**; SPEC-RUNDE §5 |
| Bytecode/VM | zweiter Typparameter an `mkcoro`/`resume` (NL1); **NL29** (Copy kalt ~90 ns); **NL36** (JIT × Yield); **NL44** (Budget im JIT) |
| Einbettung | NL9 (`step()`), NL30, **NL35** (`HostWaker`), **NL32** (Kern ohne `osAccess`) |
| Module/Capabilities | **NL32** — die Capability-Tabelle ist Spec (`spec/04-modules.md:164`) |
| Standardbibliothek | Bibliotheks-Umkehr; `std.http`/`std.net.tls` brauchen NL5, NL6; **NL22** ist nur noch eine Markierungsfrage |
| Diagnostik | NL20-D, **NL43** (`VM0015` mit Typen), NL10-D, NL37-C (lange Schritte), NL36-D (JIT-Report) |
| Testen | NL28-B'; **NL39** (Isolation gemessen — nur die Uhr fehlt) |
| Editor/Debugger | NL28-B/C |
| **Spec** | **NL38** — `std.task` braucht einen Abschnitt in `spec/11`, bevor irgendeine `Wait`-Änderung gebaut wird |

**Mit Rule 1:** Dieses Dossier liegt im Scratchpad. Posten daraus gehen als Issues, oder es gilt
die offene Frage zu `lyric-v5-features.md` (`STATUS.md` §Still open).

---

## 6. Reihenfolge, die aus den Abhängigkeiten folgt

1. **Spec zuerst (NL38):** ein `std.task`-Abschnitt in `spec/11`; die Zusätze zu `spec/10` (NL34,
   NL29-Tiefe, NL20-B-Zusatz). Ohne das ist nichts hier ein Sprachvertrag.
2. **Gate-Fragen aus anderen Gebieten:** `@NonExhaustive` (NL3/5/14/33), bedingte Konformanz (NL11),
   `mut struct` (NL15), typed throws für Lambdas (NL7).
3. **NL19** — `Cancelled` gegen typed throws. Entscheidet NL6, NL5-D, NL7, NL12, NL24.
4. **NL18** (Identität — B *und* C) und **NL23** (`Step<Y,R>`), **NL34** (nach dem Wurf) — vor NL1.
5. **NL1**, **NL2**.
6. **NL3 + NL14 + NL35 als EINE Typfrage** (Waker = Descriptor) mit **NL33** (was aus `interrupt()`
   wird) → **NL4** → **NL5** mit **NL25** vorher und **NL41** (Broadcast/exklusiv).
7. **NL13** (`close`, kein `status`) → **NL6** → **NL24**.
8. **NL31** (Delegation) mit **NL11** — dieselbe Regel 2 der §10a.
9. **NL7** mit **NL26**, **NL27**.
10. **NL32** (Kern ohne `osAccess`) mit **NL9-B** (`step()`) und **NL28-C'** — ein Modul, drei Zwecke.
11. **NL21** + **NL29** + **NL42** — Skalierung, Speicher, Fehlerpfad.
12. **NL30** + **NL44** + **NL36** — Budget, JIT, Yield: gemeinsam mit dem Gebiet Laufzeit/VM.
13. **NL15** (Isolate) — nach `mut struct`; **NL22** wartet nicht mehr darauf.
14. **Sofort machbar, unabhängig von v5:** NL20-D (bessere `VM0013`-Meldung), **NL43** (`VM0015`
    mit beiden Typen — der billigste Posten mit dem größten Nutzen, den diese Runde gefunden hat),
    NL26-D (Laufzeitwarnung „nie gezogen"), NL18-Warnung (Doppel-`spawn`), NL10-Uhr (`std.os`-Zeit),
    NL21-B (Deadline-Heap — **ein Heap muss erst geschrieben werden**, `std.collections` hat keinen),
    NL9-B (`step(): bool`, zwei Lyric-Zeilen), NL37-C (Warnung „lief N ms ohne Yield"), NL33-Doku
    („kein allgemeiner Waker"), NL42-B (`SocketException` klassifizieren), NL36-D (JIT-Report).

---

## Nach der Kritik geändert

**Wo die Kritik recht hatte — korrigiert:**

- **NL22 komplett neu.** `std.io.stream` ist seit 4.2.0 nicht-blockierend (Pool-Thread + notify-fd +
  `Wait.Readable`; gelesen `stream.lyr:43-46,143,173`, `NativeRegistry.cs:2393-2411,2501`,
  `CHANGELOG.md:702-705`; gemessen `p2`: Ticks laufen während 20 MB gelesen werden). Die
  Rule-2-Ausnahme, die die zweite Fassung forderte, ist gebaut. Die Frage ist jetzt: `std.io.file`
  bleibt bewusst synchron und wird markiert.
- **NL3 auf neuer Prämisse.** „Ein Native kann nicht nach Lyric zurückrufen" war falsch
  (`Interpreter.cs:160-177`, `ExecutionBudget.cs:20-22`, `spec/10:78-81`). Der richtige Grund, warum
  Fremdwecken über einen Deskriptor läuft, ist ein anderer: in einer Einthread-VM erreicht ein
  Weckruf von außen die VM nur in `poll`. NL3-C'' vereint Waker und notify-Deskriptor; NL3 ist
  jetzt die allgemeine Antwort, nicht nur die für In-Process.
- **NL9 neu.** Die STATUS-Notiz `:1959-1972` handelt vom Debugger (A14), nicht von einem
  Scheduler-Pump — beide darauf gebauten Befunde gestrichen. Ein Host kann Koroutinen heute per
  `ScriptInstance.Call` + `Runner.step()` treiben; was fehlt, ist `std.task.step()` und ein
  öffentliches `poll`. Unity belegt B (`step()`), nicht A (`runFor(millis)`). C (Host-Pump) ist
  unnötig.
- **Der sticky Interrupt-Waker** (gemessen `p7`, `p4`) steht in §1.3a und ist NL33. „Es gibt KEINE
  Form" ist gestrichen.
- **Asynchrone Generatoren** (gemessen `p1`/`p1b`: `LYR-VM0015`) sind §1.2, NL11-Einschränkung und
  NL31; der C#-Satz „Lyric braucht die Trennung nicht" ist gestrichen — Lyric hat sie als Panic.
- **Anker korrigiert:** `TypeChecker.cs:3374` (nicht `:3226-3240`); `PLAN.md:313,446` (nicht `:280,413`);
  `STATUS.md:623` (nicht `:613-615`); `opaque type` seit **1.15** (`CHANGELOG.md:1004-1006`), nicht 3.8.
- **Spec statt Guide.** Jede Sprachregel aus `lyric-spec/spec/10-coroutines.md` zitiert; der Guide
  heißt nicht mehr „die Spec". NL38 trägt die Spec-Änderungstabelle.
- **NL13:** `status()` gestrichen — `spec/10:33-35` verweigert es begründet. Der GC-Grund steht in
  `spec/10:89-93`.
- **NL16/NL20:** `spec/10:72-77` („yields T refused") ist der Beleg, OCaml nur Bestätigung; NL20-C
  (Ausbreitung) fällt, weil es die verworfene Klausel als Warnung wäre; NL20-B braucht einen Zusatz.
- **NL29:** die 20 ns waren heiß. Kalt neu gemessen (`n7`, 10 000 Ketten): **86–98 ns je Frame**,
  Faktor 4–5; NL29-C bekommt die richtige Zahl und wird nicht mehr empfohlen, sondern zur Messung.
  Die 1024 sind Thread-Tiefe minus Treibertiefe, keine Kettenkonstante (`Interpreter.cs:139,526,533`).
- **NL18:** Referenzsemantik existiert (`guide/11:102-103`); B ist Sichtbarmachen, keine
  Semantikänderung — Empfehlung B *und* C *und* D.
- **Vergleichssprachen:** Swift (Gleichheit `== Never`/`== any Error`, nicht Konformanz), Rust
  (`Coroutine` nightly-only), POSIX (`select` nimmt Dateien, `epoll` nicht), C# (`UnobservedTaskException`
  → NL2, nicht NL26), Python (`asyncio.wait(FIRST_COMPLETED)`; `never awaited`-Warnung → NL26),
  Unity (→ NL9-B), Haskell (Allokations-Loch), Erlang (refc binaries), Kotlin (Stack-Trace-Recovery).
- **Vierzehn Fragen ergänzt (NL31–NL44):** asynchrone Generatoren, Scheduler hinter `osAccess`, der
  existierende Waker, Zustand nach Wurf, notify-fd als Host-Schnittstelle, JIT × Yield, Preemption,
  Spec-first/Konformanz, Test-Isolation, Deskriptor-Wiederverwendung, Thundering Herd,
  Select-Grenzen, `VM0015`-Diagnose, Budget × JIT — jede mit Beleg, Optionen, Empfehlung, Bruchgrad.
- **NL10:** DNS wird über den bestehenden Pool-Mechanismus (B) gelöst, nicht über C.
- **NL30:** Preis „Budget = Interpreter" in der Tabelle.
- **Yield-Stellen: zehn**, nicht sieben.

**Wo die Kritik nicht recht hatte — die Stelle bleibt, mit Begründung im Text:**

- **Zwölf `Wait.Readable`-Stellen:** gelesen sind es **zehn** — die Kritik hat die Kommentarzeilen
  `net.lyr:10` und `process.lyr:9` mitgezählt (§1.3).
- **`NativeRegistry.cs:2055-2062` als Beleg für den Stream-Pool:** das ist `StartPump` für
  Kindprozesse; der Stream-Leser ist `StartRead` `:2393-2411`. Derselbe Mechanismus, falscher Anker
  (§1.3b).
- **„VM0015 nennt … noch die zwei beteiligten Resumes":** der Backtrace nennt beide Resume-Körper
  (`main.gen.<body>`, `main.worker.<body>`, gemessen `p1`); was fehlt, sind die beiden Typen und
  welcher Körper „the running coroutine" ist (NL43).
- **Test-Isolation:** die Prämisse „ein Test sieht die Tasks des vorigen" ist gemessen **falsch**
  (`n1` + reihenfolgeunabhängige Kontrolle: frische Instanz je Test setzt die Modulglobale zurück,
  `guide/20:39-41`). Die Frage schrumpft auf die virtuelle Uhr (NL39).
- **ABA / Deskriptor-Wiederverwendung:** gelesen `NativeRegistry.cs:1439,1461,2011,2362` — ein
  monotoner Instanzzähler für Sockets, Kinder und Dateien; Nummern werden nie wiederverwendet. Die
  Generation ist nicht nötig (NL40).
- **Select-Grenze:** gemessen 300 geparkte Listener ohne Grenze (Windows); die Frage ist für diese
  Plattform beantwortet, für Unix offen (NL42).
- **„Ein Native kann `wake(w)` rufen":** stimmt — aber es hilft nicht, weil in einer Einthread-VM
  nichts nebenher läuft, das rufen könnte; die richtige Fassung der Frage ist NL35 (Fremdwecken
  über `poll`), und die Kritik hat sie in ihren fehlenden Fragen selbst gestellt.
- **Kotlin „beides zugleich geht nicht":** richtig — aufgelöst zugunsten der Recovery, mit dem
  ehrlichen Preis (Opt-in, Laufzeit).
- **`process.wait` als Blockierer** (aus der ersten Kritikrunde): bleibt gestrichen — es yieldet
  (`process.lyr:155`).

**Messungen dieser Fassung (rev4, alle mit Erwartung vor dem Lauf):** p1/p1b, p2, p3, p4, p5
(±`--jit`), p6 (`--grant`), p7 nachgefahren — alle Befunde der Kritik bestätigt; neu n1 (Isolation,
mit Kontrolle), n3 (Broadcast), n4 (CPU-bound), n6 (Select 70/300), n7 (Kaltkopie, mit Kontrolle
n7c).
