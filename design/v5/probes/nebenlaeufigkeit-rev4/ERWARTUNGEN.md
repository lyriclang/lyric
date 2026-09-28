# Erwartungen VOR dem Lauf (rev4, Nachpruefung der adversarischen Kritik)

## Nachlauf der Kritik-Proben (rev3), von mir neu gefahren
p1  Task treibt Coroutine<int>; Helfer im Generatorkoerper yieldet Wait.Sleep(10).
    Erwartung: "got 1", dann panic LYR-VM0015, Exit 101. Kontrolle p1b: got 1, got 2, done, Exit 0.
p2  Reader zehnmal 2 MB ueber stream.readSome in einer Task, Ticker (20 ms) daneben.
    Erwartung: Ticks laufen WAEHREND des Lesens (stream yieldet auf notify-fd); mind. 4 Ticks vor "leaves".
p3  Koroutine wirft beim zweiten resume, danach next(). Erwartung: null ("erschoepft"), kein Panic.
p4  interrupt() VOR spawn(watcher). Erwartung: watcher wacht < 50 ms (sticky, task.lyr:47).
p5  yield in Helfer unter Rechenhelfer, ohne und mit --jit. Erwartung: beide Male "worker computed 6",
    weil der JIT jede Funktion ablehnt, deren Pfad eine nicht-kompilierbare erreicht (JitCompiler.cs:679,725).
p6  p1b.lyrbc mit --grant none und --grant file,net. Erwartung: LYR-CAP0001 (std.task = osAccess).
p7  Consumer parkt Wait.Interrupt, Producer ruft interrupt() nach jedem Push.
    Erwartung: 3 Werte, parks == 3, run kehrt ~ +100-140 ms zurueck.

## Neue Messungen
n1  lyrtest: Test a spawnt ohne run und prueft pending()==1; Test b in DERSELBEN Datei prueft pending()==0.
    Erwartung: beide PASS (Guide 20:40 "fresh instance": Modulglobale je Test neu).
    Wenn b faellt ("expected 0, got 1"): der Scheduler leckt zwischen Tests einer Datei.
n3  Zwei Tasks yielden Wait.Readable(l as int) auf DEMSELBEN Listener; ein Client verbindet einmal.
    Erwartung: BEIDE drucken "woke" (task.lyr:253-254 Broadcast), wakes = 2. Nebenfrage: kompiliert
    `l as int` ausserhalb von std.io.net ueberhaupt (opaque outward cast)?
n4  Task mit 20-Mio-Schleife OHNE yield, Ticker 20 ms daneben.
    Erwartung: tick 0 vor der Schleife, tick 1 erst NACH "cruncher ends" (keine Preemption, task.lyr:4).
n6  N Listener, je ein Acceptor-Task geparkt; Client verbindet zum LETZTEN; Watchdog schliesst nach 3 s alle.
    N=70 und N=300. Erwartung UNSICHER: entweder "acceptor N-1 got connection" bei ~+100 ms (kein
    Select-Limit in .NET), oder der Acceptor wacht nie und erst der Watchdog beendet run (Limit 64).
n7  10 000 Tasks, jede 20 Runden Wait.Now aus Tiefe 40 (n7_d40) bzw. Tiefe 0 (n7_d0). 3 Laeufe je.
    Erwartung: (d40 - d0) / (10000*20*40 Frames) liegt DEUTLICH ueber den 20 ns aus §1.1 — ich rechne
    mit 60-200 ns je Frame, weil 10 000 kalte Ketten den Cache nicht halten. Wenn es bei ~20 ns bleibt,
    hatte das Dossier mit der Uebertragung recht.
Erwartung n7c: zwischen d0 (~1500) und d40 (~4950); die Differenz d40-n7c ist der reine Kopieranteil von 8 Mio Frames.
