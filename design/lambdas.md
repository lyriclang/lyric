# Lambdas und Closures in Lyric — Analyse, Prototypen, Vergleich

Stand: 2026-09-22, Basis v4.4.1 (dc32100c), Branch `worktree-agent-a1c2eb789de86ba9d`.
Autor: pattern-lambda (Evolution-Team 3). Abgestimmt mit stdlib-redesign (Iterator-Ergonomie)
und new-features (typed throws, ValueBlock).

Beispiele: `examples/lambdas/shorthand.lyr` (die drei Schreibweisen nebeneinander) und
`examples/lambdas/destructuring.lyr` (Patterns in Parameter und Schleifenkopf) — beide laufen,
die erwartete Ausgabe steht im Dateikopf. Tests: `tests/Lyric.Tests.Vm/LambdaFormTests.cs`.

## 1. Ist-Stand, gemessen statt behauptet

Alle Aussagen mit lauffähigen Proben belegt (`scratchpad/team3/pl-probes/l1…l4`).

| Fähigkeit | 4.4.1 | Beleg |
|---|---|---|
| `(x: int): int => …`, Block-Body, `return`-Inferenz | ja | l1 |
| Parametertyp aus dem Kontext (`apply((n) => n * 2, 21)`) | **ja** | l1 |
| Generischer Parameter aus einem anderen Argument (`twice((n) => n*3, 2)`) | **ja** | l2 |
| Rückgabetyp bindet ein offenes `U` (`map((n) => n * 10)`) | **ja** | l3 |
| Zweistellige Lambdas ohne Annotation (`fold(…, (acc, n) => acc + n)`) | **ja** | l1/l3 |
| Capture: `let` by value, `var` als Zelle (geteilt) | ja | l1 |
| Kurzsyntax ohne Klammern (`x => …`) | nein | — |
| Trailing-Lambda (`xs.map { it * 2 }`) | nein | — |
| Implizites `it` | nein | — |
| Parameter-Destructuring (`((k, v)) => …`) | nein | — |
| Pattern im for-Kopf (`for ((k, v) in …)`) | nein | — |
| Werfende Funktionstypen (`fn(T) -> U throws E`) | nein (SEM0084) | Review |
| Generische Funktion als Wert | nein | l1 |
| Method-Reference (`string.length`, `obj.method`) | nein | l1 |
| Rekursives Lambda | nein | l1 |

**Korrektur einer verbreiteten Annahme.** Der Review nannte „Inferenz aus Kontext" als Lücke und
stdlib-redesign vermutete, ihre Tests erzwängen `(n: int) =>` und `map<int>(…)`. Beides trifft
nicht zu: die dreiphasige Inferenz (TypeChecker.cs:2746-2794 — erst Nicht-Lambda-Argumente, dann
Lambdas gegen substituierte Parameter, zuletzt offene `U` aus dem Body) leistet das bereits.
Die echte Grenze ist eng und dokumentiert (§7.3): **ein Lambda nimmt an der Überladungswahl nicht
teil**, also müssen die übrigen Argumente die Kandidaten trennen. stdlib-redesign hat das
gegengeprüft und als Nicht-Blocker vermerkt.

## 2. Implementiert (Commit `8cd174b7`)

### 2.1 Bare Lambda `x => …`

```
Lambda = ( '(' [ LambdaParam { ',' LambdaParam } ] ')' [ ':' TypeExpr ] | IDENTIFIER ) '=>' ( Expr | Block ) .
```
Ein Parameter, keine Klammern, keine Annotation — der Typ kommt aus dem Kontext wie bei `(x) =>`.
**Eindeutig**, mit genau einer Ausnahme, die der Parser kennt: in einem **Match-Guard** steht vor
`=>` der letzte Operand des Guards, nicht ein Lambda-Parameter (`x if y => …`). Dafür trägt der
Parser `_inGuard`, das an jeder Klammer/Klammerebene zurückgesetzt wird — `x if xs.any(y => y > 0) => …`
behält also sein Lambda. Sonst war `IDENT =>` überall ein Parsefehler.

### 2.2 Trailing-Lambda und implizites `it`

```
TrailingLambda = '{' ( Expr | { Statement } ) '}' .     (* hinter Callee, Member oder Call *)
```
Ein Block direkt hinter einem Aufrufziel ist dessen **letztes Argument**; sein einziger Parameter
heißt `it`. `f(a) { … }` hängt an die Argumentliste an, `f { … }` ist der Aufruf mit nur diesem
Argument. Erwartet der Kontext `fn() -> R`, wird `it` fallen gelassen (`run { 7 }`); erwartet er
mehr als einen Parameter, ist es SEM0045 mit dem Hinweis auf die lange Form.

**Kollision mit §6.8 (Struct-Initializer) — gelöst, ohne die Regel zu ändern.** Die Entscheidung
fällt bereits am Primary: `IsStructInitAhead` nimmt `Name { }` und `Name { feld = … }`; alles
andere in Klammern hinter einem Aufrufziel erreicht die Postfix-Schleife und ist dort ein
Trailing-Lambda. Preis: genau eine Fehlermeldung ändert sich — ein Aufruf-Statement ohne `;`,
gefolgt von einem Block, hieß „expected ';'" und liest sich jetzt als Trailing-Lambda. Kotlin und
Swift zahlen denselben Preis und haben kein `;`, das fehlen könnte.

**Semikolon.** Ein Statement, das auf der `}` eines Trailing-Lambda endet, braucht kein `;` —
dieselbe Regel, nach der ein Block-Arm eines `match` kein `,` braucht. Eines darf stehen.

**Body.** Enthält der Block auf seiner eigenen Ebene ein `;` oder beginnt er mit einem
Statement-Schlüsselwort, ist er ein Statement-Block (wie `=> { … }`); sonst hält er **einen
Ausdruck** und das Lambda liefert ihn. Landet der ValueBlock von new-features (Tail-Expression),
fällt der zweite Fall mit ihm zusammen und `{ let a = 1; a * 2 }` wird zusätzlich möglich.

**Warum `it` und nicht `$0` oder `_`.** `_` ist in Lyric das Wildcard-Pattern — Scalas
`xs.map(_ * 2)` wäre in `match`-Armen zweideutig und in `let (_, b) = t;` doppelt belegt. `$0`
verlangt eine Lexer-Änderung und sieht neben f-String-Löchern (`f"{x}"`) wie eine zweite
Interpolationssprache aus. `it` ist ein gewöhnlicher Name: ein eigener Parameter oder eine Lokale
namens `it` verdeckt ihn, und es ist **kein Schlüsselwort** (in der stdlib kommt `it` als Name
nirgends vor — von stdlib-redesign geprüft).

### 2.3 Parameter-Destructuring und Patterns im for-Kopf

```
LambdaParam = ( IDENTIFIER | TuplePattern ) [ ':' TypeExpr ] .
ForInStmt   = 'for' '(' ( IDENTIFIER | TuplePattern ) 'in' Expr ')' Block .
```
`((k, v)) => …` — die **äußere** Klammer ist die Parameterliste, die innere das Muster; `(k, v) =>`
bleibt zwei Parameter (Prototyp 14). `for ((k, v) in entries(m))` ersetzt die Pflichtzeile
`let (k, v) = kv;`. Beide Stellen erlauben nur **irrefutable** Muster — ein Schleifenkopf und ein
Parameter haben keinen Pfad für einen Fehlschlag; ein testendes Muster ist LYR-SEM0098 und die
Diagnose nennt `let … else` im Körper als Ausweg. Das Lowering ist der Pattern-Compiler mit
`assumeMatch: true`: ein `LoadLocal` aus dem Parameter-/Elementslot, dann Bindungen.

### 2.4 Nebenbefund behoben: `return match (…)` mit lauter verlassenden Armen

Der lyriclings-ICE („match expression produced no value"): ein `match`-Ausdruck, dessen Arme alle
Block-Arme mit `return` sind, hat jetzt den Typ `never` (Sema) und wird für den Effekt gelowert,
ohne Ergebnis-Slot (`LowerReturn` siegelt mit `unreachable`). Passt zu new-features' `never`-Regel.

## 3. Designs ohne Prototyp (mit Aufwand und Vergleich)

### 3.1 Method-References `Type.method` / `obj.method` als Funktionswert

```lyr
xs.forEach(println)                 // heute: (x) => println(x)
over(parts).map(fromInt)            // heute: (n) => fromInt(n)
lines.map(string.trim)              // ungebundene Methode: fn(string) -> string
people.sortBy(Person.name)          // Feld-Referenz als fn(Person) -> string
let f = counter.increment;          // gebundene Methode: fn() -> void, 'counter' ist gefangen
```
Freie Funktionen sind bereits Werte (§7.x „Functions as values"), Methoden nicht. Vorschlag:
1. **Ungebunden** `Type.method` → `fn(Type, P…) -> R` (Rust `str::trim`, Java `String::trim`,
   C# ist hier schwächer: Methodengruppen brauchen einen Delegattyp).
2. **Gebunden** `obj.method` in Wertposition → `fn(P…) -> R` mit `obj` als Capture (Python, C#,
   Kotlin `obj::method`). Lowering: ein synthetisches Lambda mit einem Capture — der
   LambdaTable-Weg existiert.
3. **Feld-Referenz** `Type.field` → `fn(Type) -> F` (Kotlin `Person::name`, Scala `_.name`).
Überladung: wie bei freien Funktionen entscheidet der erwartete Typ, sonst Fehler (§7.x).
Aufwand: Sema ~120 Z. (MemberExpr in Wertposition), Lowering ~80 Z. (Thunk-Synthese), Parser 0.
Falle (aus Kotlin/C#): jede Auswertung von `obj.method` erzeugt eine neue Instanz — Gleichheit
von Funktionswerten darf nicht versprochen werden. **Minor, empfohlen** (stdlib-redesign nennt
`forEach(println)`, `map(fromInt)`, `sortBy(string.length)` als häufigste Ein-Zeilen-Lambdas).

### 3.2 Werfende Funktionstypen `fn(T) -> U throws E`

Heute: eine `throws`-Funktion ist kein Wert (SEM0084), ein werfendes Lambda passt nirgends hin;
der Parser parst die Klausel bereits (`ParseType(allowThrows: true)`), sie landet nur im falschen
Knoten (Bug-Hunt P2-20). Vorschlag, abgestimmt mit new-features (die typed throws bauen):
- `FnType` bekommt ein `Throws`-Feld (`null` = wirft nicht). Zuweisbarkeit **einseitig**: eine
  nicht-werfende Funktion ist überall zulässig, wo eine werfende erwartet wird, nicht umgekehrt —
  genau die Regel, die §10 für Coroutinen schon hat.
- Ein Lambda-Körper, der wirft, bekommt den Typ aus dem Kontext; ohne Kontext ist die Klausel
  anzuschreiben: `(x: int): int throws Boom => …`.
- Aufruf über einen werfenden Funktionswert zählt in der Exception-Analyse wie ein Aufruf der
  Funktion (`ExceptionAnalyzer`: Callee-Typ statt Callee-Symbol lesen).
- `rethrows`-Ersatz: ein Typparameter in der Klausel (`fn map<T, U, E>(f: fn(T) -> U throws E): … throws E`)
  — das ist der Punkt, an dem dieses Design an new-features' typed throws andockt.
Vergleich: Swift (`(Int) throws -> Int`, `rethrows`; seit Swift 6 typed throws `throws(E)`),
Kotlin (keine checked exceptions — jede Funktion „wirft"), Rust (kein throws; `Result` im
Rückgabetyp, also im Funktionstyp enthalten), Zig (`fn(T) E!U`, Fehlermenge ableitbar mit `!`),
C# (kein throws im Typ), Java (`throws` ist Teil der Methodensignatur, aber
`Function<T,R>` kann nicht werfen — der Grund, warum Java-Streams `try/catch` in jedem Lambda
tragen). Empfehlung: **Swift/Zig-Modell mit einem Typparameter**, Aufwand Sema ~200 Z. zusätzlich
zu typed throws, Lowering 0 (die Klausel ist eine reine Typaussage). **Minor**, §8.3, §9.2, §10.

### 3.3 Generische Funktion als Wert

`let g = ident;` mit `fn ident<T>(v: T): T` ist heute abgelehnt — richtig, denn `fn(T) -> T` ist
kein Typ ohne Instanziierung. Vorschlag: **Instanziierung an der Wertstelle**, wie Rust und C#:
`let g: fn(int) -> int = ident;` wählt `ident<int>`; `let g = ident<int>;` schreibt es aus.
Nicht vorgeschlagen: Rank-2-Polymorphie (Haskell `forall`), die die Monomorphisierung sprengt.
Aufwand: Sema ~80 Z. (erwarteten Typ gegen die Signatur unifizieren, Instanz anfordern),
Lowering: die InstanceTable liefert die Instanz bereits. **Minor, empfohlen.**

### 3.4 Rekursive Lambdas

`let fact = (n: int): int => if (n <= 1) 1 else n * fact(n - 1);` — `fact` ist im Initializer noch
nicht gebunden (§7.1, und das ist gut so: `let x = x;` soll nichts Eigenes meinen). Optionen:
(a) **nichts tun** — benannte lokale Funktionen gibt es nicht, aber eine freie Funktion tut es;
(b) `let rec f = …` (OCaml/F#) — ein Schlüsselwort für einen seltenen Fall;
(c) Selbstreferenz über einen Y-Kombinator in der stdlib — unlesbar.
Empfehlung: **(a), dokumentieren**; Rust (`fn` innen), Kotlin (lokale `fun`), Swift (lokale
`func`) lösen es über lokale Funktionen — das wäre der bessere Folgevorschlag („lokale `fn`",
eigener Eintrag, Minor).

### 3.5 Capture-Semantik: dokumentieren, nicht ändern

Heute (§7.x, verifiziert): Captures entstehen **bei der Lambda-Erzeugung**; ein `let` wird als
Wert gefangen, ein `var` als **geteilte Zelle** (Schreiben im Lambda ist außen sichtbar und
umgekehrt); ohne Captures gibt es keine Umgebung. Das ist Kotlin/Swift-`var`-Semantik und
JavaScripts `let`-Semantik; Rust unterscheidet `move`/by-ref explizit, Python hat `nonlocal`,
C# fängt immer die Variable, Go fing bis 1.22 die Schleifenvariable (die klassische Falle).
**Lücken, die zu schließen sind:** (1) `RecordCaptures` übersah `DestructuringStmt` — mit dem
neuen `LetPatternStmt` laufen beide Formen über `AddPatternBindings`, der Walker kennt sie jetzt;
(2) `x++` auf einen gefangenen `var` ist IR0001 (Bug-Hunt P1-16) — ein Lowering-Fall, der über
`StoreValue` läuft statt über `StoreLocal`; gehört zu den Zellen, nicht zu den Patterns.
**Kein Sprachvorschlag** — Lyric braucht kein `move`: es gibt keine Lebensdauern, und eine Kopie
erzwingt man durch eine `let`-Zwischenbindung.

## 4. Vergleich pro Thema (Kurzfassung, Details in §2/§3)

| Thema | Rust | Swift | Kotlin | C#/TS | Lyric jetzt |
|---|---|---|---|---|---|
| Kurzform | `\|x\| x*2` | `{ $0*2 }` | `{ it*2 }` | `x => x*2` | **`x => x*2` + `{ it*2 }`** |
| Trailing | nein | ja | ja | nein | **ja** |
| Destructuring im Parameter | `\|(a,b)\|` | Tupel-Parameter | `{ (a,b) -> }` | TS: `({a,b}) =>` | **`((a,b)) =>`** |
| Destructuring im for | ja | ja | ja | C# nein, TS ja | **ja** |
| Werfende Funktionstypen | via Result | `throws`/typed | alle | nein | Design |
| Method-Reference | `Type::m` | `Type.m` | `obj::m` | Methodengruppe | Design |
| Generische fn als Wert | ja (instanziiert) | ja | ja | ja | Design |
| Capture-Modell | explizit `move` | Capture-Liste | implizit | implizit | implizit, Zelle für `var` |

## 5. Offene Punkte

1. Trailing-Lambda mit **mehreren** Parametern (`fold(0) { acc, n => acc + n }`, Kotlin-Form) —
   bewusst nicht in Runde 1: die Parameterliste im Block braucht eine eigene Regel und kollidiert
   mit dem Ein-Ausdruck-Body. Empfehlung: nach dem ValueBlock nachziehen.
2. `it` in **verschachtelten** Trailing-Lambdas verdeckt das äußere `it` (Kotlin-Verhalten). Die
   Diagnose sollte das benennen, wenn jemand das äußere meint.
3. Werfende Funktionstypen hängen an new-features' typed throws — Design hier, Umsetzung dort.
