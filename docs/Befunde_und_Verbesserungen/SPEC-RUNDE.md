# Regelfragen aus dem Sweep

Was beim Bug-Sweep als **Entscheidung** aufgefallen ist, nicht als Lücke. Jeder Eintrag hat
dieselbe Form: was gemessen wurde, was die Spec heute sagt, und was daran zu entscheiden ist.

Nichts hier ist umgesetzt. Die Sammelstelle existiert, damit die Spec-Runde nach dem Sweep aus
Befunden besteht statt aus Erinnerung.

---

## 1. Inkrement: woher kommt `+1`?

**Entschieden (Maintainer, 2026-09-22): abgeleitet, nicht eingebaut.** `++`/`--` sollen entweder

- **A** aus `Add<T, R>` folgen — `x++` wird zu `x.add(1)`, und dafür muss eine Konformanz mit
  `int` als `other`-Typ existieren; oder
- **B** eigene `Inc`/`Dec`-Interfaces bekommen (ggf. zu einem zusammengefasst).

**Gemessen.** Heute ist beides keins von beidem: `CheckUnary`/`CheckPostfix` verlangen schlicht
`TypeFacts.IsNumeric`, es läuft kein Nutzercode. `+` geht über `Add`, `++` nicht — zwei Wege für
dieselbe Rechnung, und der zweite ist für eigene Typen verschlossen.

**Abwägung.**

- **A** fügt keinen Mechanismus hinzu, sondern nimmt einen weg: `++` ist dann Zucker über `+`, und
  `CONTRIBUTING.md` §Rule 2 („ein Mechanismus pro Konzept") ist damit auf der Seite von A. Die
  Anforderung ist `Add<int, T>` auf `T` — `R` muss `T` sein, sonst lässt sich das Ergebnis nicht
  zurückschreiben. Ein Typ mit `Add<Vec2, Vec2>`, aber ohne `Add<int, Vec2>`, bekommt kein `++`,
  und das ist die richtige Antwort statt einer stillen.
- **B** kann etwas, das A nicht kann: ein Typ, für den *schreiten* sinnvoll ist, *addieren* aber
  nicht — ein Cursor, ein Handle, ein Datum ohne Arithmetik. Dafür sind es zwei Arten zu sagen
  „plus eins", was genau die Doppelung ist, die Rule 2 verbietet. Nach `CONTRIBUTING.md` wäre B
  ein paralleler Mechanismus und bräuchte ADR plus 30 Tage.

**Empfehlung: A.** Für die Fälle, die nur B bedient, ist ein benannter Aufruf (`next()`) ohnehin
klarer als ein Operator. Wenn B kommen soll, gehört ein ADR dazu — nicht weil die Regel formal
ist, sondern weil genau diese Doppelung das vorige Sprachprojekt gekostet hat.

**Zwei Löcher hängen an derselben Entscheidung** und müssen im selben Satz beantwortet werden,
sonst verspricht die Spec mehr als der Compiler kann:

- **Die Statement-Form.** §6.8 lässt „a call, an assignment, or `resume`" zu — danach wäre *keine*
  der beiden Schreibweisen ein gültiges Statement. Gemessen: `x++;` wird akzeptiert, `++x;` ist
  `LYR-SEM0022`. Beide erzeugen denselben Code — dieselbe `LowerIncDec`, Unterschied nur, welcher
  Temp zurückgegeben wird, und den liest in Statement-Position niemand. Nichts hält den Zustand
  fest: der einzige Konformanzfall (`06-operators/increment_has_the_classic_values.lyr`) benutzt
  beide Schreibweisen nur in Ausdrucksposition, und im Guide steht kein einziges
  Inkrement-Statement. §6.8 braucht eine vierte Form, mit beiden Schreibweisen.
- **Das Ziel.** `LowerIncDec` ruft `ResolveLocalTarget`: Inkrement geht heute nur auf einem Local.
  `p.x++` ist `LYR-IR0001` (eigener Fund, TASKLIST P3 §24 — ein Semantikfehler unter dem
  Implementation-Limit-Code). Der Satz, der die Form aufnimmt, muss sagen, worauf sie stehen darf.

Betroffen: §6.1, §6.8, §11 (Operator-Interfaces), Grammar §5/§6.1.

---

## 2. Hat ein `?Struct` Wertsemantik?

**Gemessen.** `struct S { v: int } struct W { n: ?S }` — nach `var b = a; b.n!.v = 9;` steht in
`a.n!.v` ebenfalls 9. Ebenso lokal: `var y: ?S = x; y!.v = 9;` ändert `x`. Zwei Hälften:
`Coerce` (FunctionLowerer) kopiert nicht, wenn die Quelle `?S` ist, und `Interpreter.CopyStruct`
rekursiert nur in Felder mit Tag `Struct`, nicht in `Optional`-of-`Struct`.

**Was die Spec sagt.** §13 zählt auf, was `structcopy` teilt (class, array, interface) und was es
kopiert (struct). **`?Struct` steht in keiner der beiden Listen.** Die Implementierung hat die
Lücke zugunsten „geteilt" entschieden, ohne dass es jemand entschieden hat.

**Zu entscheiden.** Entweder `?T` ist ein Wert, wenn `T` einer ist — dann kopieren `Coerce` und
`CopyStruct` durch das Optional hindurch — oder `?Struct` bleibt eine Referenz, und dann muss die
Mutation durch `!` auf einem Struct-Optional verboten werden, weil sie sonst weiter still am
Original landet. Swift beantwortet es mit „Wert, und `!` auf einem lvalue ist selbst ein lvalue".

**Eine Folge hängt daran.** §13 verbietet Selbstrekursion eines Structs. `FindStructCycle`
(TypeChecker) überspringt `?S`, also läuft `struct Node { v: int, next: ?Node }` heute. Wird `?S`
ein Wert, ist dieser Typ unendlich groß und muss `LYR-SEM0056` werden — was Code bricht, der
heute funktioniert. Die beiden Fragen sind eine.

Betroffen: §3.3, §13 (structcopy, Types), Guide 5 und 9.

---

## 3. Sind die Felder eines `let`-gebundenen Structs schreibbar?

**Gemessen.** `let s = St { v = 1 }; s.v = 2;` kompiliert und druckt 2. Dasselbe über `s.inc()`
mit `mut fn`.

**Was die Spec sagt.** §7.1: „`let` binds immutably … ANY assignment to a `let` … is
`LYR-SEM0019`", mit dem Klassenfall als ausdrücklicher Ausnahme („the REFERENCE is immutable, the
object is the object's business"). Zum Struct sagt sie nichts. Ein Struct hat Wertsemantik (§3.4),
seine Felder **sind** die Bindung — der Ausnahmesatz trägt hier also nicht.

**Warum das keine Lücke ist, sondern eine Frage.** Es steht ein *bewusster* Test dagegen:
`MutabilityTests.A_struct_parameter_field_is_writable` erlaubt `fn f(v: V) { v.x = 9; }` mit der
Begründung, die Änderung treffe die Kopie und der Aufrufer sehe sie nicht. Ein Parameter ist damit
heute eine unveränderliche Bindung mit schreibbaren Struct-Feldern. Ob ein `let`-Local anders
liegt als ein Parameter — das ist die Entscheidung. Gleich behandeln heißt: beide schreibbar
(heutiger Stand, §7.1 stimmt dann nicht) oder beide nicht (bricht den Parameter-Fall).

Betroffen: §7.1, §3.4.

---

## 4. Was bedeutet eine zweite Bindung desselben Namens?

**Gemessen.** `let x = 1; let x = 2; println(x)` kompiliert und druckt **1**: die zweite Bindung
landet nicht in der Symboltabelle, alle Referenzen binden an die erste, der Initializer der
zweiten wird aber ausgewertet. `CheckBinding` ignoriert den Rückgabewert von `scope.TryDeclare` —
dasselbe Muster, das 4.4.1 für Pattern-Bindungen als `LYR-SEM0097` geschlossen hat.

**Was die Spec sagt.** §7.1 legt Shadowing nicht fest.

**Zu entscheiden.** Ablehnen (wie `SEM0097` im Pattern) oder Rust-Shadowing (die zweite Bindung
gewinnt). **Beides ist besser als heute** — der heutige Zustand ist keine der beiden Antworten,
sondern die dritte, stille. Liegt als **Prototyp 10** vor und gehört in die Prototyp-Runde.

Betroffen: §7.1.
