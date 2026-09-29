# Erwartungen VOR dem Lauf (rev3, adversarische Pruefung)

- p1_nested_gen_wait: Task treibt Coroutine<int>; im Generatorkoerper yieldet ein Helfer Wait.Sleep.
  Erwartung: "got 1", dann panic LYR-VM0015 (Wait trifft die int-Kette), Exit 101.
- p1b_control: derselbe Generator ohne den Wait-Helfer. Erwartung: got 1, got 2, done, Exit 0.
- p2_stream_in_task: Reader liest 2 MB zehnmal ueber std.io.stream.readSome in einer Task; Ticker daneben.
  Erwartung: Ticks laufen WAEHREND des Lesens weiter (stream yieldet Wait.Readable auf notify-fd),
  also tick 1..4 deutlich vor "reader leaves".
- p3_throw_then_pull: Koroutine wirft beim zweiten resume; danach co.next(). Erwartung unsicher:
  entweder null (Done) oder Panic VM0014 (State blieb Running). Beides ist ein Befund.
- p4_interrupt_sticky: interrupt() VOR spawn eines Wait.Interrupt-Tasks. Erwartung laut task.lyr:47:
  watcher wacht sofort (< 50 ms). Sonst haengt run() -> timeout.
- p5_jit_helper_yield: Helfer mit yield unter einem reinen Rechenhelfer; einmal ohne, einmal mit --jit.
  Erwartung ohne --jit: "worker computed 6", done. Mit --jit: unbekannt — entweder gleich, oder VM0013
  (compiled frame ist eine Wand).
- p6_grant_none: p1b mit --grant none bzw. --grant file. Erwartung: Ladefehler, weil std.task osAccess braucht.
- p7_interrupt_as_waker: Producer schlaeft 3x30 ms, pusht je einen Wert und ruft interrupt(); Consumer parkt
  auf Wait.Interrupt statt Wait.Now zu drehen. Erwartung: 3 Werte, parks == 3 (oder 2, wenn ein sticky
  Interrupt einen Park spart), KEIN Leerlauf, Gesamtzeit ~90-110 ms. Gegenprobe ist k_handmade_channel
  der rev2 (~10^4 Leerrunden).
