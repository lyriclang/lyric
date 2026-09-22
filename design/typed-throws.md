# Typed throws: `throws E` mit Typparameter, `throws` auf Funktionstypen, werfende Lambdas

**Status:** designt (Sema-schwer; Teilstufe 1 ist ein Bugfix und sofort machbar)
**Version:** Minor (4.5 Stufe 1–2, 4.6 Stufe 3) — additiv, heute gültige Programme haben keinen Typparameter in `throws`
**Spec:** §4 (Prosa „coroutine types only" streichen), §7.3 (Lambdas), §8.3 (Inferenz von E), §9.2 (Klausel mit Typparameter, `never`), §10 (Funktionstyp mit Suffix), Appendix A (SEM0084 einschränken) · **Guide:** 10
**Abhängigkeiten:** `design/throw-expression.md` (`never` als schreibbarer Typ ist implementiert — `throws never` ≡ keine Klausel). Voraussetzung für stdlib-redesigns `assertThrows<E>` und `Result.orThrow(): T throws E`.
**Abgestimmt mit:** stdlib-redesign (Anker `assertThrows<E :: [Throwable]>(f: fn() -> void throws E): E`; sie bauen bis dahin `assertThrows(f: fn() -> void throws Throwable)`), pattern-lambda (liefert das Design der werfenden Funktionstypen aus ihrer Lambda-Sicht; die Sema-Umsetzung hängt an diesem Dokument)

## Motivation

Eine Pipeline-Stufe muss sich heute für EINE Signatur entscheiden (Prototyp 06):

- Die werfende Variante (`Coroutine<int> throws Exception`) nimmt beide Quellen an — dafür wird
  `evens(numbers())` werfend, obwohl nichts werfen kann, und `main` braucht ein `try/catch`, das **toter
  Code** ist. Ein späterer echter Fehler aus anderer Quelle wird von diesem toten catch verschluckt.
- Die nicht-werfende Variante nähme `parsed(…)` nicht an (§10: Zuweisung nur non-throwing → throwing).
- Für **Funktionswerte** ist es schlimmer: ein Lambda hat keine throws-Klausel
  (`ExceptionAnalyzer.cs:11,201` — ein Lambda-Body wird mit `Permit.None` analysiert), also muss eine
  `mapInts`-Stufe eine werfende Operation auf `?int` oder ein Sentinel abbilden. **Der Grund geht
  verloren**, gegen §9.0 („ein Throw beantwortet WARUM").

Dazu ein **Bug**: `throws E` mit Typparameter wird an der Aufrufstelle nie substituiert
(`ExceptionAnalyzer.cs` — `ThrownOf` liefert für einen `TypeParamType` `(any, null)`, also „statisch
unbekannt"), und ein typisiertes `catch` deckt deshalb nie.

## Syntax

**Keine Grammatikänderung.** Alle drei Formen parsen heute schon:

```lyr
fn evens<E>(input: Coroutine<int> throws E): Coroutine<int> throws E   // FunctionDecl §3.1 — parst
fn each<E>(xs: int[], f: fn(int) -> void throws E): void throws E      // TypeExpr §4 — parst, SEM0084
fn sure(): int throws never                                            // never seit 4.5 ein Typname
```

Was fehlt, ist ausschließlich Sema. `LYR-SEM0084` („'throws' belongs to a coroutine type") muss von
„überall außer Coroutinen verboten" auf „überall außer Funktions- und Coroutinentypen verboten" gelockert
werden.

**Vorsicht bei der Reichweite** (§4): ein Funktionstyp reicht so weit rechts wie möglich, also gehört das
`throws` in `fn(int) -> void throws E[]` zum Funktionstyp; ein Array werfender Funktionen braucht Klammern:
`(fn(int) -> U throws E)[]`. Das ist konsistent mit der bestehenden Regel und gehört in §4.

## Semantik

### Stufe 1 — Substitution an der Aufrufstelle (Bugfix, klein)

Die Klausel des Callees wird mit der Instanz-Belegung des Aufrufs substituiert, **bevor** der
`ExceptionAnalyzer` die Site prüft. Die Belegung liegt bereits vor (`TypeResult` hält sie für die
Monomorphisierung); `ThrownOf` muss sie konsultieren, statt einen `TypeParamType` pauschal als
„unbekannt" zu behandeln. Damit deckt `catch (e: ParseError)` einen Aufruf von
`fn parse<E>(…) throws E`, der mit `E = ParseError` instanziiert wurde. **~60 Zeilen, sofort machbar,
behebt einen gemeldeten Bug.**

### Stufe 2 — Inferenz von `E` aus Werten (§8.3 Schritt 3)

Strukturell, wie jede andere Typargument-Inferenz: `Coroutine<int> throws Exception` gegen
`Coroutine<int> throws E` bindet `E = Exception`; **kein Suffix bindet `E = never`**. Die Constraint
`E :: [Throwable]` ist implizit (`never` erfüllt sie trivial — es gibt keinen Wert, der sie verletzen
könnte).

Damit wird die Pipeline-Stufe einmal geschrieben und beide Quellen passen:

```lyr
fn evens<E>(input: Coroutine<int> throws E): Coroutine<int> throws E { … }

let a = evens(numbers());          // E = never  → kein try nötig
let b = evens(parsed(lines));      // E = ParseError → try oder throws
```

### Stufe 3 — Werfende Funktionstypen und Lambdas (§7.3, §8.3 Schritt 5)

Ein Lambda an einem Parameter vom Typ `fn(T) -> U throws E` wird mit **offenem E** geprüft; sein `E` ist
der Typ seiner ungefangenen Throw-Sites: eine Klasse → diese; mehrere verschiedene → `Throwable`; keine
→ `never`. **Damit dürfen Lambdas erstmals werfen** — und nur dort, wo der Parametertyp es erlaubt. Ein
Lambda an `fn(T) -> U` (ohne Suffix) bleibt wie heute: es darf nicht werfen.

**Assignability** (§10-Regel, auf Funktionstypen ausgedehnt): `fn(T) -> U throws never` ⊂
`fn(T) -> U throws E` ⊂ `fn(T) -> U throws Throwable`. Einseitig, wie bei Coroutinen: eine werfende
Funktion passt **nicht** in einen nicht-werfenden Slot — genau das Loch, durch das die Forderung
verschwinden würde.

### Lowering

**Die Werfbarkeit hat keine Laufzeitrepräsentation** (Handler-Tabellen sind pro Funktion, §9.3), also
**null Lowering-Arbeit** — mit einer Entscheidung: `E` darf **nicht** Teil des Instanzschlüssels der
Monomorphisierung sein. `evens<never>` und `evens<ParseError>` haben identischen Code und identisches
Layout; `E` ist ein **Phantom-Parameter**, der nur die Analyse steuert. Andernfalls verdoppelt jede
Pipeline-Stufe ihren Code pro Fehlertyp. (Das ist die einzige Stelle mit Aufwandsfolge, und sie gehört
in die Spec, nicht in die Implementierung.)

### Bytecode

Keine Änderung, Format bleibt 4.0.

## Wechselwirkungen

- **Generics:** `E` als Phantom-Parameter (s.o.) — muss in §8 stehen, sonst raten zwei Implementierungen verschieden.
- **Coroutinen:** §10 „Assignment is one-directional" bleibt wörtlich gültig, wird nur auf Funktionstypen ausgedehnt.
- **Interfaces (§5.1):** ein Interface-Member `fn each<E>(f: fn(T) -> void throws E): void throws E`
  braucht **generische Interface-Member**. Zu prüfen: die Konformanzprüfung verlangt exakte
  Signaturgleichheit inklusive throws-Klausel (`LYR-SEM0042`) — mit Typparametern in der Klausel muss
  „Teilmenge" modulo Substitution entschieden werden. **Der teuerste Teilaspekt; Kandidat, ihn in Stufe 3
  auszuklammern** (Interface-Member ohne throws-Typparameter belassen).
- **`try?`** (`design/try-expression.md`) über einen Aufruf mit `E = never`: Warnung „nothing to catch",
  kein Fehler.
- **`never`:** implementiert. `throws never` ≡ keine Klausel — die Spec sollte beide Schreibweisen als
  denselben Typ definieren, damit Konformanz und Assignability nicht zwei Fälle brauchen.
- **Pattern/Lambda (pattern-lambda):** ihre Closure-Kurzsyntax erbt die Klausel-Inferenz; kein Konflikt.
- **Formatter/LSP:** Signatur-Rendering muss `throws E` auf Funktionstypen ausgeben (TypeFacts.Display
  kann das für Coroutinen schon).

## Breaking

Nein (Minor). Heute gültige Programme haben keinen Typparameter in `throws` und kein `throws` auf einem
Funktionstyp (beides ist SEM0084 bzw. wirkungslos).

## Aufwandsschätzung

| Ebene | Stufe 1 | Stufe 2 | Stufe 3 |
|---|---|---|---|
| Lexer/Parser | 0 | 0 | 0 |
| Sema | ~60 Z. (Substitution im ExceptionAnalyzer) | ~80 Z. (strukturelle Inferenz, `never`-Bindung) | ~120 Z. (Lambda-Klausel-Inferenz) + ~40 Z. (Assignability) |
| Lowering | 0 | 0 (Phantom-Parameter-Entscheidung) | 0 |
| VM/Bytecode | 0 | 0 | 0 |
| stdlib | — | — | `assertThrows`, `Iterator.next()` mit `throws E`, werfende `map`/`filter` |

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| **Swift 6** | `func f() throws(ParseError)`, `throws(Never)` ≡ non-throwing; `rethrows` ist die ältere Kurzform | **Nächstes Vorbild.** Swift führte typed throws erst 2024 ein und hat `rethrows` nur, weil es zuerst kam — Lyric braucht kein `rethrows`, weil `throws E` es abdeckt und präziser ist. Swifts `Never` ist genau Lyrics `never`. |
| **Rust** | `Result<T, E>` — der Fehlertyp ist ein Typparameter des **Rückgabetyps**; `?` propagiert über `From` | Kein Klausel-System; dafür ist jede Stufe generisch über E „umsonst". Preis: `Box<dyn Error>` und `thiserror`/`anyhow` als Ökosystem-Pflicht. |
| **Zig** | `!T` mit inferiertem Error-Set; `fn f() E!T` mit explizitem Set; Sets sind Vereinigungen | **Die ausdrucksstärkste Form**: der Compiler infert die Fehlermenge aus dem Body und vereinigt sie über Aufrufe. Lyrics „mehrere Typen → `Throwable`" ist die grobe Variante davon. |
| **Java** | `throws E` mit Typparameter ist möglich (`<E extends Exception> void f() throws E`), wird für „sneaky throws" missbraucht | Warnung: Javas Form ohne Inferenz an der Aufrufstelle ist genau Lyrics heutiger Bug. |
| **Kotlin/C#/Go/TS** | keine geprüften Klauseln | Go: `error` als Rückgabewert, jede Stufe schreibt `if err != nil`. |

Swift:
```swift
func map<T, U, E: Error>(_ xs: [T], _ f: (T) throws(E) -> U) throws(E) -> [U] {
    var out: [U] = []
    for x in xs { out.append(try f(x)) }   // wirft E oder gar nicht
    return out
}
let doubled = map([1, 2]) { $0 * 2 }       // E = Never → kein try nötig
```
Zig:
```zig
fn parseAll(lines: [][]const u8) ![]u16 {   // Fehlermenge INFERIERT aus dem Body
    var out = …;
    for (lines) |l| try out.append(try parsePort(l));
    return out.items;
}
```

**Fallen, die Lyric vermeidet:** (1) Javas Typparameter ohne Substitution an der Aufrufstelle — genau der
Bug, den Stufe 1 behebt; (2) Rusts Zwang, für jede Kombination einen Fehler-Enum oder `Box<dyn Error>` zu
schreiben; (3) Swifts `rethrows` als zweiter Mechanismus neben typed throws — Lyric bekommt nur einen;
(4) Zigs inferierte Sets sind mächtig, machen aber die Signatur einer öffentlichen API unlesbar
(`!T` sagt nichts) — Lyric bleibt bei **geschriebenen** Klauseln, mit `E` als benanntem Parameter.

**Empfehlung: Swift-6-Modell** (`throws E` mit Typparameter, `throws never` als ausgeschriebene
Nicht-Werfbarkeit, kein `rethrows`), ergänzt um Zigs Erkenntnis, dass **Inferenz nur nach innen** gehen
darf: ein Lambda inferiert seine Klausel, eine benannte Funktion schreibt sie. Reihenfolge: **Stufe 1
sofort** (Bugfix), Stufe 2 in 4.5, Stufe 3 in 4.6 (wegen der Interface-Member-Frage).

## Offene Fragen

1. Mehrere Throw-Sites unterschiedlicher Typen in einem Lambda → `Throwable` (Vorschlag) oder Fehler?
   Zig würde die Vereinigung bilden; Lyric hat keine Vereinigungstypen, also `Throwable`.
2. Generische **Interface-Member** mit throws-Typparameter — in Stufe 3 oder ausklammern? (Vorschlag: ausklammern.)
3. `never` als allgemeiner Typ (Rust `!`) — bleibt bei „nur Rückgabe- und throws-Typ" (siehe `design/throw-expression.md`).
4. Soll `throws` auf einem **Feld**typ (`handler: fn(int) -> void throws E`) erlaubt sein? Ja, folgt aus §4,
   aber `E` muss dann Typparameter des Typs sein — Interaktion mit dem Phantom-Parameter prüfen.

## Spec-Diff

§4:
```diff
-A `throws` suffix is valid on a coroutine type and nowhere else.
+A `throws` suffix is valid on a coroutine type and on a FUNCTION type, and nowhere else. On a
+function type it says what a call of that value may throw; the suffix binds to the function
+type, which reaches as far right as it can, so an array of throwing functions is written
+`(fn(int) -> U throws E)[]`.
```
§9.2:
```diff
 A function declares what it throws …
+The clause may name a TYPE PARAMETER (`fn evens<E>(…): … throws E`). At a call the clause is
+substituted with that call's binding BEFORE the site is checked, so a typed `catch` covers it;
+`throws never` is the written form of "does not throw" and is the same type as no clause at
+all. The parameter is a PHANTOM: throwability has no runtime representation, so `E` is not part
+of an instance's identity and no instance is duplicated for it.
```
§8.3: „Schritt 3 … a `throws` suffix takes part structurally: `Coroutine<int> throws Exception` against `Coroutine<int> throws E` binds `E = Exception`, and no suffix binds `E = never`."
§7.3: „A lambda checked against a function type WITH a throws clause may throw: its own clause is the type of its uncaught throw sites — one class, `Throwable` for several, `never` for none."
Appendix A: SEM0084-Text auf „not a coroutine or function type" ändern.
