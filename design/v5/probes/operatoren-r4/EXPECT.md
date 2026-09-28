Erwartungen VOR dem Lauf (operatoren-r4, Nachpruefung der Kritik + neue Faelle):
d01  f++ auf float: Kritik sagt kompiliert, 2.5. Erwarte: kompiliert, 2.5. d01b s++ auf string: SEM0003.
d02  comptime ((0-7)/2), ((0-7)%2): erwarte -3 / -1; Laufzeit -3 / -1.
d03a [1,2]==[1,2]: erwarte SEM0059. d03b (1,2)==(1,2): erwarte SEM0059. d03c Color.Red==Color.Red: Kritik sagt SEM0059; erwarte SEM0059.
d04  == auf Equatable<V>-Wert: erwarte SEM0003 (wie Add). d04b a.equals(b): laeuft, "eq".
d05  (p.x = 3) -> 3, (xs[0] = 5) -> 5, (n += 1) -> 1, a += b += 1 -> a=3 b=2.
d06  xs[f()] = g(): f, g. xs[f()] += g(): f, g. r().m(f()): r, f, m. t() && u(): t, u.
d07  let m = -9223372036854775808: erwarte kompiliert, druckt MIN. d07b 9223372036854775808: SEM0001.
d08  -7.5 % 2.0 -> -1.5; 1.0/0.0 -> Infinity (kein Panic); 0.0/0.0 -> NaN; 1.0 % 0.0 -> NaN.
d09  let z = 1/0: kompiliert, Laufzeit-Panik VM0002. d09b comptime (1/0): CT0002.
d10  < auf Ordered<V>-Wert: erwarte SEM0003 (Konsistenz mit Add/Equatable). d10b a.compare(b) < 0 laeuft, "lt".
d11  f"{d}" auf d: Display (Interface-Wert): erwarte KOMPILIERT und druckt "got V" (TypeChecker.cs:2998-3003).
d12  a as int auf a: Into<int>-Wert: unbekannt; Hypothese SEM0006. d12b a.into() laeuft, 7.
d13  "- 9223372036854775808" mit Leerzeichen: wenn Lexer-Sonderfall -> SEM0001; wenn Parser/Sema-Faltung -> kompiliert, MIN.
d13b 0 - 9223372036854775808: erwarte SEM0001 (Literal zu gross, keine Sonderbehandlung).
d13c -(9223372036854775808): erwarte SEM0001, wenn Sonderfall nur auf direkt anliegendem Literal.
d14  y = x++, f(x++): kompilieren (Konformanzfall), Ausgabe 1 / 2 / 3.
d15  let r: int = (o ??= 3): erwarte SEM-Fehler (?int nach int), weil ??= heute ?T liefert.
d16  MIN / -1 zur Laufzeit: Spec §3.2 sagt WRAPS zu MIN, MIN % -1 = 0; Hypothese der Kritik: .NET-Exception. d16b comptime MIN / -1: MIN oder CT0002.
d17  xs[c.next()] ??= 9 (Kritik c62): erwarte "next" genau einmal, dann 9.
d18  f += 1 auf float: erwarte kompiliert, 2.5 (Literal adaptiert) — Stuetze fuer OP-35.
d19  v += V{..} auf Local ueber Add: erwarte kompiliert, "add", 3 (Kontrolle fuer OP-10: Local-Pfad geht).
