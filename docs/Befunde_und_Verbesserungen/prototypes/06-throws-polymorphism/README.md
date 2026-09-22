# 06 — Polymorphie über Werfbarkeit (`throws E` mit Typparameter)

Zugehöriger language-review-Punkt: **"Keine Polymorphie über Werfbarkeit (kein `rethrows`, kein `throws E` mit Typparameter)"** (HIGH). Schließt den Nebenbefund aus Prototyp 01 (`ExceptionAnalyzer.cs:218-227`) ein.

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 6 | 58 |
| `soll.lyr` | PROPOSAL | 52 |
| `vergleich.swift` | Swift 6 typed throws `throws(E)`, `Never` | 34 |
| `probe-null-array.lyr` | Nebenprobe (Bug, siehe unten) | — |
| Lib-Variante | nicht möglich: die Werfbarkeit ist Typ-Information, die keine Bibliothek abstrahieren kann | — |

## Ist-Stand
Eine Pipeline-Stufe muss sich für EINE Signatur entscheiden. Die werfende (`Coroutine<int> throws
Exception`) nimmt beide Quellen an — dafür wird `evens(numbers())` werfend, obwohl nichts werfen
kann, und `main` braucht einen `try/catch`, der **toter Code** ist ("cannot happen"). Die
nicht-werfende Signatur nähme `parsed(…)` nicht an (§10: Zuweisung nur non-throwing → throwing).
Für Funktionswerte ist es schlimmer: ein Lambda hat keine throws-Klausel (`ExceptionAnalyzer.cs:11,201`),
also muss `mapInts` eine werfende Operation auf `?int` oder ein Sentinel abbilden — **der Grund
geht verloren**, gegen §9.0 ("ein Throw beantwortet WARUM").

## Soll-Syntax und Grammatik
- **Signatur** `fn evens<E>(input: Coroutine<int> throws E): Coroutine<int> throws E` — parst heute
  (`throws` + TypeExpr, `FunctionDecl` §3.1); nur die Sema verlangt einen Throwable-Typ (SEM0030).
- **Coroutine-Typ** `Coroutine<T> throws E` — `ThrowsSuffix` existiert (§4).
- **Funktionstyp** `fn(int) -> U throws E` — `TypeExpr = … [ ThrowsSuffix ]` parst das HEUTE
  schon; `LYR-SEM0084` ("valid on a coroutine type and nowhere else") lehnt es ab. **Keine
  Grammatikänderung nötig**, nur §4's Prosa ("coroutine types only") und §10 "Throwability of a
  pull" werden auf Funktionstypen ausgedehnt. Vorsicht bei `fn(int) -> void throws E[]`: der
  Funktionstyp reicht so weit rechts wie möglich (§4), also gehört das `throws` zum Funktionstyp
  — konsistent, aber ein Array werfender Funktionen braucht Klammern `(fn(int) -> U throws E)[]`.
- **Inferenz** (§8.3 Schritt 3): strukturell — `Coroutine<int> throws Exception` gegen
  `Coroutine<int> throws E` bindet `E = Exception`; kein Suffix bindet `E = never`.
  **`never`** ist seit §9.4 der Typ von `panic`; er wird als throws-Typ schreibbar (`throws never`
  ≡ keine Klausel), sonst nirgends. Constraint `E :: [Throwable]` ist implizit (never erfüllt sie).
- **Lambda-Argumente** (§8.3 Schritt 5): ein Lambda an `fn(T) -> U throws E` wird mit offenem E
  geprüft; sein E ist der Typ seiner ungefangenen Throw-Sites (eine Klasse; mehrere verschiedene
  → `Throwable`; keine → `never`). Damit dürfen Lambdas erstmals werfen — nur dort, wo der
  Parametertyp es erlaubt; ein Lambda an `fn(T) -> U` bleibt wie heute.
- **Substitution an der Aufrufstelle** (§9.2): die Klausel des Callees wird mit der inferierten
  Belegung substituiert, bevor der ExceptionAnalyzer die Site prüft — genau die Stelle
  `ExceptionAnalyzer.cs:218-227`, die heute "Typparameter → any" sagt. Die Analyse muss dafür
  die Instanz-Belegung des Aufrufs kennen (`TypeResult` hält sie für die Monomorphisierung).
- **Spec**: §9.2 (Klausel mit Typparameter, never), §8.3 (Inferenz von E), §10 (Funktionstyp
  mit Suffix), §4 (Prosa "coroutine types only" streichen), §7.3 (Lambdas dürfen unter einem
  throws-E-Kontext werfen).

## Bewertung
| | Ist | Soll | Swift |
|---|---|---|---|
| nicht-werfende Quelle durch die Stufe | 6 Z. inkl. totem catch | 1 Z. | 1 Z. |
| werfende Operation in `mapInts` | Grund verloren (`?int`), 3 Z. Lambda | Grund erhalten, 1 Z. | 1 Z. |
| Anzahl Stufen-Varianten pro Pipeline | 2 (oder alles werfend) | 1 | 1 |

- **Fehlerklassen verhindert**: toter catch, der später eine echte Ausnahme aus anderer Quelle
  verschluckt; Sentinel/Optional statt Grund (Fehlerursache geht verloren — der Fehler wird als
  "fehlender Wert" fehlinterpretiert); Duplikation jeder Stufe in einer werfenden und einer
  nicht-werfenden Variante, die auseinanderlaufen.
- **Aufwand**: Lexer 0, Parser 0 (!), Sema **groß**: Inferenz von E (strukturell, ~80 Z.),
  never als Bindung, Lambda-Klausel-Inferenz (~120 Z.), ExceptionAnalyzer-Substitution (~60 Z.),
  Assignability `fn … throws never` ⊂ `fn … throws E` (§10-Regel auf Funktionstypen, ~40 Z.);
  Lowering: **die Werfbarkeit hat keine Laufzeitrepräsentation** (Handler-Tabellen sind pro
  Funktion, §9.3), also 0 — außer die Monomorphisierung muss `never`-Instanzen als eigene
  Instanzen zählen (sie sollten es nicht: E beeinflusst keinen Layout, nur die Analyse →
  "phantom parameter", nicht Teil des Instanzschlüssels); VM 0; stdlib: `Iterator<T>.next()`
  könnte `throws E` bekommen (stdlib-review ist informiert).
- **Breaking**: nein (Minor). Heute gültige Programme haben keinen Typparameter in throws.
- **Wechselwirkungen**: Generics (E im Instanzschlüssel? s.o. — offene Frage mit Aufwandsfolge);
  Coroutinen (§10 "Assignment is one-directional" bleibt: `throws never` ⊂ `throws E`);
  Interfaces (§5.1 "implementation clause ⊂ interface clause": ein Interface-Member
  `fn each<E>(f: fn(T) -> void throws E): void throws E` braucht generische Member — heute
  sind Interface-Member nicht generisch? zu prüfen); Optionals/match: keine; `try?` (Prototyp
  04) über einen Aufruf mit E = never ist eine Warnung, kein Fehler.
- **Offene Semantikfragen**: (1) Mehrere Throw-Sites unterschiedlicher Typen in einem Lambda →
  `Throwable` (Vorschlag) oder Fehler? (2) `never` als schreibbarer Typname: nur nach `throws`
  (Vorschlag) oder allgemein (Rust `!`)? Allgemein würde §3.1-Tabelle und Assignability
  berühren — nicht in Runde 1. (3) Swift-`rethrows` als Kurzform? Nein — `throws E` deckt es ab
  und ist präziser (Swift hat rethrows nur, weil typed throws erst später kam).

## Empfehlung
**Sprachfeature, Minor, aber Sema-schwer** — das teuerste Feature der bisherigen Liste, und das
mit dem größten Hebel für die stdlib (`std.iter` kann dann werfende Quellen adaptieren, ohne jede
Stufe zu verdoppeln). Reihenfolge: zuerst Substitution an der Aufrufstelle (behebt den Bug aus
Prototyp 01, kleiner Schritt), dann Inferenz von E aus Coroutine-Typen, zuletzt Lambdas.

## Nebenbefund (Bug, aus `probe-null-array.lyr`)
`let b: (?int)[] = [null] * 3;` → `LYR-SEM0001: cannot assign 'null[]' to '?int[]'`, während
`let a: (?int)[] = [null, 1];` kompiliert. Zwei Punkte: (1) der Kontext propagiert in Array-
Literale (§3.1), aber nicht durch `*` (Array-Wiederholung) — für das Idiom `[x] * n` (§3.3 nennt
es als DIE Form, ein Array zu bauen) fehlt damit die Null-Initialisierung eines Optional-Arrays;
Umgehung `let hole: ?int = null; [hole] * n`. (2) Die Diagnose schreibt `?int[]` für `(?int)[]`
— nach §4 ist `?int[]` aber ein OPTIONALES ARRAY; die Typanzeige (`TypeFacts.Display`) klammert
nicht. Beides als `bug` in der Taskliste (das zweite an diagnostics).
