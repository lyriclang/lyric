Erwartungen VOR dem Lauf (operatoren-r3):
c01 comptime ((0-7)/2) und ((0-7)%2): erwarte -3 / -1 (Trunkierung). r16 hat das NICHT gemessen (dort 0-(7/2)).
c02 MIN / -1 und MIN % -1 zur Laufzeit: Hypothese .NET OverflowException -> VM-Absturz oder Panik; Kontrolle MIN/2 = -4611686018427387904.
c03 comptime MIN / -1: erwarte CT0002 oder MIN.
c04 1/0 mit Literalen (kein comptime): erwarte Laufzeit-Panik, kein Compilefehler. Kontrolle comptime (1/0) -> CT0002.
c05 Wert einer Zuweisung auf Feld/Element, Wert von (n += 1): erwarte 3 / 5 / 1 (kompiliert).
c06 Reihenfolge xs[f()] = g(), xs[f()] += g(), r().m(f()): erwarte f vor g, r vor f.
c07a [1,2]==[1,2]: erwarte SEM0059; c07b (1,2)==(1,2): unbekannt; c07c Enum ohne Payload ==: erwarte ok.
c08 let m = -9223372036854775808: erwarte Fehler Literal zu gross, oder ok.
c10 1<<64: erwarte 1; 1 << -1: erwarte MIN (1<<63).
c31 5 mit Postfix-Bang: erwarte SEM-Fehler.
c35 f-String-Loch mit Spezifizierer auf Display-Typ: erwarte Fehler.
c50 while ((l = next()) != null): erwarte kompiliert.
Rest: Replikate der Dossier-Messungen mit den dort genannten Codes.
c60 Value-Block-Tail im match-STATEMENT: erwarte SEM0022 (Dossier).
c61 f++ auf float: Spec sagt "integer variables"; SPEC-RUNDE sagt IsNumeric -> Hypothese: KOMPILIERT (Spec-Verstoss). Kontrolle: s++ auf string -> SEM0003.
c62 xs[c.next()] ??= 9 : Spec :101 verspricht next einmal -> erwarte "next" genau einmal, danach xs[1] == 9.
c63 "ab" * 3: erwarte "ababab".
c70 -7.5 % 2.0: erwarte -1.5 (fmod, Vorzeichen des Dividenden); 1.0/0.0: erwarte inf oder Panik (Spec sagt "division by zero is a panic" ohne float-Ausnahme); 0.0/0.0: NaN oder Panik.
c71 == durch Equatable<V>-Interface-Wert: Hypothese wie OP-5 (SEM0059/SEM0003), Kontrolle a.equals(b) laeuft.
