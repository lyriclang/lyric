# Erwartungen VOR dem Lauf

q1 (void-Aufrufe Tiefe 40, 20000x, KEIN yield): 90-150 ms. Wenn deutlich unter 152 ms,
   ist die Basislinie des Dossiers (`x_baseline_calls`, int-rekursiv) zu teuer und
   verschluckt die Yield-Kosten.
q2 (void-Aufrufe Tiefe 0, 20000x): 1-4 ms.
q3 = Wiederholung x_depth_deep: ~150 ms erwartet.
q4 = Wiederholung x_depth_shallow: ~11 ms erwartet.
   Ableitung: Yield@40 = q3-q1, Yield@0 = q4-q2. Erwartung: Yield@40 > Yield@0,
   weil 42 Frames kopiert werden. Das Dossier behauptet Yield@40 - Yield@0 ~ 0.

r1 interrupt() weckt einen Sleep-geparkten Task? ERWARTUNG: NEIN (wakeInterrupted
   raeumt nur scheduler.interrupted). Dossier NL6 sagt "der Interrupt, der alle weckt".
r2 dieselbe Koroutine zweimal spawnen: ERWARTUNG: kompiliert, laeuft, der Task wird
   pro Runde zweimal gestept -> mehr Schritte als ein Task haben duerfte.
r3 Benutzer-match ueber Wait, nicht erschoepfend: ERWARTUNG: Fehler (Erschoepfung
   gefordert) -> eine sechste Variante bricht Benutzercode.
r4 co.close()/co.status()/co.isDone(): ERWARTUNG: LYR-SEM0012.
r5 for (x in co): ERWARTUNG: LYR-SEM0007.
r6 verworfener Rueckgabewert: ERWARTUNG: kompiliert ohne Warnung (Dossier), aber
   dieser Branch heisst feat/v5-warning-clocks -> pruefen.
r7 Gleichheit zweier Coroutine-Werte (co1 == co2): ERWARTUNG: unklar. Wenn NEIN, ist
   NL5-A ("der Scheduler raeumt die uebrigen Eintraege ab") in Lyric nicht schreibbar.
r8 yield in einer Klassenmethode: ERWARTUNG: geht (keine Faerbung).
r9 Koroutine mit Ergebnis ueber next(): return 42 -> LYR-SEM0039.
