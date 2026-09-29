# 08 — `throw` als Ausdruck (Typ `never`)

Zugehöriger language-review-Punkt: **"`throw` ist kein Ausdruck (kein `never`-Typ in Ausdrucksposition)"** (MEDIUM). Unabhängig in Prototyp 01 gefunden (`Err(e) => throw e`).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 43 | 46 |
| `soll.lyr` | PROPOSAL | 40 |
| `vergleich.kt` | Kotlin (`Nothing`, `?: throw`) | 26 |
| `probe-never.lyr`, `probe-never2.lyr` | **Compiler-Absturz** (siehe Nebenbefund) | — |
| Lib-Variante | nicht möglich (Typsystem) | — |

## Ist-Stand
Drei Stellen, drei Umwege: im match-Ausdruck der Block-Arm `_ => { throw …; }` (funktioniert,
aber der Arm sieht anders aus als seine Nachbarn); für `x ?? throw` ein dreizeiliges
`let v = …; if (v == null) { throw …; } return v;` (das Narrowing macht es korrekt, aber der
Wert bekommt einen Namen, den niemand braucht); für den if-Ausdruck das if-Statement davor.

## Soll-Syntax und Grammatik
```
Primary   += ThrowExpr .
ThrowExpr  = 'throw' UnaryExpr .
ExprStmt   = Expr ';' .          (* schließt ein ThrowExpr ein; ThrowStmt entfällt *)
```
- **Eindeutig**: `throw` ist reserviert und beginnt heute nur `ThrowStmt` (`Parser.Statements.cs:38,224`);
  in Ausdrucksposition ist es `LYR-PAR0002`. Der Operand ist ein `UnaryExpr`, damit
  `throw e ?? f` nicht als `throw (e ?? f)` gelesen wird — Kotlin macht es genauso (throw bindet
  wie ein Präfix-Operator; `x ?: throw E()` ist die häufigste Form und braucht keine Klammern).
- **Typ** (§6.9, §9.4): `never`. Die Sema HAT den Typ bereits — `LyrType.cs:23,122 NeverType`,
  "the return type of panic; a bottom type, not nameable" — und `Flow.cs:18` behandelt einen
  never-typisierten ExprStmt als divergierend. Die Unifikationsregel (§6.9) braucht einen Satz:
  *ein Arm/Operand vom Typ never trägt nichts bei; sind alle never, ist der Ausdruck never.*
  `a ?? never` ist `T` (der rechte Operand kann `?T` nicht widern, weil er keinen Wert hat).
- **`panic`/`todo`/`unreachable`** (`core.lyr:23,44-60`) sind als `void` deklariert; die Sema
  typisiert den Aufruf trotzdem als never (sonst würde `Flow.cs:18` nicht greifen). Der
  Vorschlag macht das explizit: Rückgabetyp `never` in der Deklaration schreibbar (nur als
  Rückgabetyp und throws-Typ — Prototyp 06), §9.4 und §11 angleichen. Damit bekommen auch
  EIGENE Abbruch-Helfer (`fn fail(msg: string): never { panic("fatal: " + msg); }`) die
  Flow-Wirkung von `panic` — heute müssen sie `void` sein, und `if (bad) { fail("…"); }` narrowt
  nichts und zählt nicht als Exit (§7.3/§7.4/§7.7). Ein `never`-Körper muss auf jedem Pfad
  panicken/werfen/eine never-Funktion aufrufen (Coverage-Regel wie SEM0017, invertiert).
- **Flow** (§7.3, §7.7): unverändert — ein never-Ausdruck beendet den Pfad wie heute `throw;`.
- **Exception-Analyse** (§9.2): `throw e` in Ausdrucksposition ist eine Throw-Site wie das
  Statement; `ExceptionAnalyzer.AnalyzeExpr` bekommt den Fall.

## Bewertung
| | Ist | Soll | Kotlin |
|---|---|---|---|
| match-Arm | Block-Arm, 1 Z. (uneinheitlich) | 1 Z. | 1 Z. |
| `?? throw` | 3 Z. + Hilfsname | 1 Z. | 1 Z. |
| if-else | 2 Statements | 1 Ausdruck | 1 Ausdruck |

- **Fehlerklassen verhindert**: keine harte — es ist ein QOL-Feature. Indirekt: das
  `let v; if (v == null) throw; return v`-Muster verleitet dazu, `v` später noch zu verwenden;
  mit `?? throw` gibt es keinen zweiten Namen.
- **Aufwand**: Parser klein (~15 Z.: Primary-Fall, ThrowStmt → ExprStmt); Sema klein (~40 Z.:
  Typ never, Unifikationsregel, ExceptionAnalyzer-Fall); **Lowering: die Lücke ist schon da**
  (siehe Nebenbefund — never in Wert-Position stürzt heute ab; der Fix ist derselbe: einen
  never-Ausdruck lowern, dann `unreachable` emittieren und dem Aufrufer einen Dummy-Wert
  des erwarteten Typs zurückgeben, ~30 Z. in `LowerExprAs`); VM 0; stdlib: Signaturen von
  `panic`/`todo`/`unreachable` auf `never`.
- **Breaking**: nein (Minor). `throw e;` bleibt gültig (ExprStmt eines never-Ausdrucks).
  `never` als Rückgabetyp in Signaturen ist neu, ein Nutzer-Typ dieses Namens (Identifier)
  würde verdeckt — `never` sollte in die Liste der Builtin-Typnamen (§4, `BuiltinType`).
- **Wechselwirkungen**: Narrowing (`x ?? throw` narrowt nicht — die Bindung des Ergebnisses ist
  bereits `T`); Optionals (`?T ?? never` = `T`, s.o.); match (never-Arm zählt für Exhaustiveness
  wie jeder Arm); throws (Site wie Statement); Generics (`never` als Typargument? nein — nicht
  nennbar außer als Rückgabe-/throws-Typ); Prototyp 05 (Tail-Expression `throw e` im ValueBlock
  ergibt never; Block ist dann "exit").

## Empfehlung
**Sprachfeature, Minor, klein** — und der Lowering-Teil ist ohnehin fällig, weil `panic` in
Wert-Position heute den Compiler abstürzen lässt.

## Nebenbefund (Bug, HIGH — Absturz)
`return if (b) 1 else panic("no");` und `return env.get(id) ?? panic("x");` passieren die Sema
(never unifiziert) und stürzen das Lowering ab:
`InternalCompilationException: lowering: expression … produced no value` —
`FunctionLowerer.cs:1393` via `LowerIfExpr` (`:1727`) bzw. `LowerCoalesce` (`:2786`).
Repro: `probe-never.lyr`, `probe-never2.lyr`. Entweder die Sema lehnt never in Wert-Position ab
(dann ist §9.4 "returns never" nur halb wahr), oder das Lowering behandelt es (Vorschlag oben).
