# Erwartungen VOR dem Lauf (Revision 2, nach der Kritik)

## Nachpruefung der Kritik
A1 q1..q4 je 6 Laeufe, Minimum. Erwartung: q1 (void-Aufrufe T40) 150-165, q2 (T0) 5-9,
   q3 (Yield T40) 170-185, q4 (Yield T0) 12-16. Folgerung erwartet: Yield@40 ~ 0.7 us,
   Yield@0 ~ 0.35 us, Faktor ~2. Der Dossier-Satz "1 ms auf 20000" faellt dann.
A2 x_baseline_calls (int-Version) 3 Laeufe: erwartet DEUTLICH ueber q1, weil `return deep(n-1)`
   plus `s += ...` mehr tut als der void-Zwilling. Das ist die Kontrolle fuer "falsche Basislinie".
B  k_handmade_channel 5 Laeufe: Erwartung Streuung 8000-15000, kein konstanter Wert.
C  r1: interrupt() weckt NUR den Interrupt-Task; der Sleep-Task schlaeft bis zum Ende.
D  r3: nicht erschoepfendes match ueber Wait -> LYR-SEM0050.
E  r7: co1 == co2 -> LYR-SEM0059 (kein Equatable).
F  r2: spawn(co); spawn(co) -> akzeptiert, pending=2. r11: geschachteltes run -> LYR-VM0014-Panic.
G  r12: std.io.net.accept aus main -> LYR-VM0013-Panic.
H  r14: spawn ohne run -> Exit 0, keine Warnung, nichts laeuft.
I  r15: Coroutine<void>.next() -> bool; Coroutine<?int>.next() -> LYR-SEM0080.
J  r16: resume einer werfenden Koroutine in einer nicht-werfenden fn -> LYR-SEM0034.

## Neue Messungen fuer die fehlenden Fragen
K  s1_dns_blocks: connect zu einem Namen, den es nicht gibt, waehrend ein Ticker laeuft.
   Erwartung: der Ticker steht fuer die ganze Namensaufloesung (DNS ist synchron im Native).
L  s2_file_blocks: file.text auf eine ~40 MB Datei in einer Task, Ticker daneben.
   Erwartung: Ticker steht fuer die Lesedauer (>20 ms), also blockiert std.io.file den Scheduler.
M  s3_memory: N geparkte Tasks, Tiefe 0 vs Tiefe 40, Peak-Working-Set des Prozesses.
   Erwartung: Tiefe 40 kostet spuerbar mehr je Task als Tiefe 0; ich rechne mit
   mehreren hundert Byte je Frame, also grob 5-20 KB je Task bei Tiefe 40.
N  s4_sleeper_scale: 200 vs 2000 gleichzeitig faellige Sleeper, Zeit fuer die Weckrunde.
   Erwartung: quadratisch, also Faktor ~100 bei Faktor 10 mehr Sleepern.
O  s5_module_global: zwei Tasks, ein Modul-`let` -> geteilt, kein Task-eigener Platz.
   Erwartung: geteilt (ein Zaehler zaehlt beide).
P  s6_defer_yields_in_task: eine Task, deren defer `yield Wait.Sleep` tut.
   Erwartung: laeuft; damit kann eine Aufraeumarbeit erneut parken.
Q  s7_chain_depth: Rekursion in einer Koroutine bis Tiefe 200000, dann yield.
   Erwartung: entweder eine Lyric-Panic mit Code oder ein CLR-StackOverflow (harter Abbruch).
R  s8_task_identity_workaround: laesst sich eine Task heute ueber ein Feld identifizieren?
   Erwartung: nur ueber einen selbstgebauten Wrapper, nicht ueber die Koroutine selbst.
