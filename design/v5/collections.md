# Lyric 5 — Gebiet: Arrays, Ranges, Indexierung, Iteration

Zweite Fassung, nach adversarischer Kritik. Alle Messungen und alle Zeilenangaben sind auf den
**heutigen** Bäumen neu erhoben:

- `lyric` @ `6f6f029f` (Merge von `release/v4.6.0-cut`) — die Vorfassung stand auf `c4aa6e48`,
  und `src/Lyric.Frontend/Sema/TypeChecker.cs` ist seither um 204 Zeilen gewachsen. **Alle
  TypeChecker-Zeilen der Vorfassung waren dadurch verschoben.**
- `lyric-spec` @ `8f17c02` — die Vorfassung stand auf `ae24385`; §7.1 hat seither den
  `LYR-RES0001`-Absatz bekommen, also sind auch die §7.2-Zeilen verschoben.
- Binaries: `src/Lyrc/bin/Debug/net10.0/lyrc.dll`, `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`.
- Proben dieser Runde: `…/scratchpad/v5-design/probes/collections-r2/` (r01–r25, b1–b3).

Belegarten: **gemessen** = Programm kompiliert und gelaufen · **gelesen** = Pfad:Zeile ·
**behauptet** = ungeprüft, und dann steht das Wort da.

---

## 1. Ist-Stand

### 1.1 Was es gibt

| Ding | Form | Beleg |
|---|---|---|
| Array-Typ | `T[]`, feste Länge, Länge im **Wert** | `docs/Grammar.md:306,317`, `lyric-spec/spec/03-types.md:71-75` |
| `int[3]` | refüsiert, `LYR-PAR0043` | `spec/03-types.md:72` (gelesen) |
| Array-Literal | `[a, b, c]` — das **einzige** Collection-Literal | `docs/Grammar.md:502` |
| Array-Bau | `[x] * n`, `arrayOf(n, f)`, `arrayFilled(n, x)` | `stdlib/std/collections.lyr:1271,1283` |
| Array-Ops | `+` (Konkat), `*` (Wiederholung), `.length`, `[i]` | `src/Lyric.Frontend/Sema/TypeChecker.cs:2543-2544` (gelesen), gemessen r01 |
| Index | nur `[i]`, Bounds-**Panik** `LYR-VM0006`, Exit 101 | gemessen (r02) |
| Range | `a..b` / `a..=b`, **nur im Schleifenkopf** | `docs/Grammar.md:382-383,423-426`, `spec/03-types.md:79` |
| for-in | Range, Array, String, `Iterable<T>`, `Iterator<T>` | `spec/07-statements.md:52-56` |
| Schleifenvariable | nur `IDENTIFIER` oder `TuplePattern` | `docs/Grammar.md:382` (gelesen), gemessen r09 |
| Iterator | `mut fn next(): ?T`, `null` = Ende | `stdlib/std/iter.lyr:21-25` |
| Iterable | `fn iter(): Iterator<T>`, frischer Cursor je Aufruf | `stdlib/std/iter.lyr:421-424` |
| Indexable | `get(int): T` / `mut set(int, T)` — **nur `int`-Index** | `stdlib/std/collections.lyr:19-24` |
| `Iterator`-Member | **28 = `next` + 13 Adapter + 14 Terminatoren**, also **27 Default-Methoden** | `stdlib/std/iter.lyr:21-285`, gezählt |
| Freie std-Funktionen **auf `T[]`** | **acht**, in drei Modulen | s. u. |

**Korrektur der Vorfassung (Kritiker hat recht).** Die Vorfassung schrieb „Array-Oberfläche der
std: genau VIER freie Funktionen". Das stimmt **innerhalb von `collections.lyr`**
(`arrayOf:1271`, `arrayFilled:1283`, `sortArray:1320`, `slice:1372`), nicht für die std. Über
alle Module (gelesen, `grep -n "^pub fn .*T\[\]" stdlib/std/*.lyr`):

| Funktion | Modul:Zeile | was sie tut |
|---|---|---|
| `arrayOf<T>(n, f)` | `collections.lyr:1271` | baut über eine `List<T>` |
| `arrayFilled<T>(n, value)` | `collections.lyr:1283` | **ist** `[value] * n` |
| `sortArray<T :: [Ordered<T>]>(xs)` | `collections.lyr:1320` | über `sortList` |
| `slice<T>(xs, from, to)` | `collections.lyr:1372` | Kopie, Panik bei falschen Grenzen |
| `over<T>(xs): Iterator<T>` | `iter.lyr:438` | **die meistbenutzte Array-Funktion der Sprache** |
| `compact<T>(xs: (?T)[]): Iterator<T>` | `iter.lyr:464` | der Weg um `LYR-SEM0091` herum |
| `collectArray<T>(source): T[]` | `iter.lyr:959` | `return source.toArray();` — quadratisch |
| `toArray<T>(o: ?T): T[]` | `option.lyr:87` | null oder ein Element |

Die Vorfassung hat mit `over` ausgerechnet die Funktion vergessen, mit der jede Kette beginnt.

### 1.2 Was Lyric 4 in diesem Gebiet still falsch oder inkonsistent tut

**(A) Float-Ranges schneiden ihre Grenzen ab — schweigend.** Der Sema-Check verlangt nur
„matching numerics" (`src/Lyric.Frontend/Sema/TypeChecker.cs:2907` — *nicht* 2789 wie die
Vorfassung schrieb; `TypeFacts.IsNumeric` schließt `float` ein,
`src/Lyric.Frontend/Sema/TypeFacts.cs:27-30`). Die vier Range-Adapter tragen aber `i64`/`u64`
(`src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1310-1330`), also wird `1.5` zu `1`.

```
for (x in 1.5..4.5)  ->  1 2 3            (gemessen, r22, auf HEAD 4.6.0 neu bestätigt)
for (x in 0.0..0.5)  ->  null Durchläufe  (gemessen, r22)
Kontrolle im selben Programm: f"{0.5}" -> 0.5, f"{0.5+0.75}" -> 1.25
```

Weder Spec noch Guide erwähnen Float-Ranges; §7.2 (`spec/07-statements.md:52-56,64-66`)
spezifiziert die Form und die leeren Fälle, nie den Elementtyp. **Ein stiller Rechenfehler,
kein Designspielraum.**

**(B) Ein Index, der kein `int` ist, erzeugt einen Compiler-ICE.** `CheckIndex`
(`TypeChecker.cs:3028`) prüft `TypeFacts.IsInteger` in Zeile **3032** — und das schließt `char`
und alle Breiten ein (`TypeFacts.cs:21-25`). Die Vorfassung nannte 2910-2916; das war der Baum
`c4aa6e48` und selbst dort die falsche Stelle. Das Lowering emittiert keine Konversion:

| Index-Typ | Ergebnis | Beleg |
|---|---|---|
| `int`, `int64` | läuft | gemessen |
| `char` | `LYR-CLI0020` „loadelem index t6 is char, expected i64 … defect in the compiler" | gemessen (r23) |
| `int32` | `LYR-CLI0020` „… is i32, expected i64" | gemessen (r23b) |
| `bool` | sauberes `LYR-SEM0007` | gemessen (Vorfassung, p05) |
| **Kontrolle**: `l.get(i)` mit `int32` | sauberes `LYR-SEM0001` | gemessen (Vorfassung, p20b) |

Die Kontrolle zeigt, wo das Loch sitzt: die `[]`-Zuckerform ersetzt die normale
Zuweisbarkeitsprüfung durch `IsInteger` und verliert damit beides — die Konversion **und** die
Fehlermeldung.

**(C) `[e] * n` teilt das Element — auch für Structs. Die Vorfassung hat das Gegenteil
behauptet, und sie lag falsch.** Die genaue Regel, gemessen:

> `[e] * n` wertet `e` einmal aus, kopiert den Wert **einmal** in das Literal, und lässt dann
> **alle n Slots auf diese eine Kopie zeigen.** Die Quelle ist sicher, die Slots sind es nicht.

```
struct Vec2 { x: float, y: float }
let seed = Vec2{x=1.0, y=2.0};
var c = [seed] * 2;  c[0].x = 7.0;   ->  c[0].x c[1].x == 7 7      (gemessen, r01)

Vier Kontrollen im selben Programm, alle mit Wertsemantik:
  [seed, seed]           -> 8 1     (Literal, derselbe Wert zweimal)
  arrayOf(2, (i) => …)   -> 8 1
  var f = seed; f.x=6.0  -> 6 1     (seed unberührt)
  [seed] + [seed]        -> 5 1     (Konkatenation kopiert)

Und die Quelle bleibt heil:
  var seed = V{x=1}; var c = [seed] * 3; c[0].x = 7;
  -> slots 7 7 7, quelle 1                                          (gemessen, r25)
```

`class`-Elemente teilen ebenfalls (`7 7`, gemessen r19) — dort ist es aber **konsistent**, weil
eine Klasse auch bei gewöhnlicher Zuweisung teilt (Kontrolle im selben Programm: `9`). Für
Structs ist `*` die **einzige Stelle der Sprache**, an der ein Struct geteilt wird. Das
`let`-Binding hilft nicht: `let c = [seed] * 2; c[0].x = 7.0;` kompiliert und druckt `7 7`
(gemessen, r18) — `LYR-SEM0109` („Feldschreibung durch eine unveränderliche Struct-Bindung",
seit 4.6, `docs/guide/19-diagnostics.md:63`) greift für ein Array-Element nicht.

**Und die std wäscht die Falle.** `arrayFilled<T>(n, value)` ist wörtlich
`return [value] * n` (`collections.lyr:1283-1288`, gelesen) — gemessen liefert
`arrayFilled(2, seed)` dieselben `9 9` (r01). Jede Warnung oder jedes Verbot, das an der
`*`-Schreibstelle hängt, geht an `arrayFilled` vorbei, weil die Schreibstelle in der stdlib
liegt. **Die Vorfassung führte `arrayOf` als Ausweg und `arrayFilled` als harmlose Bauform,
ohne zu bemerken, dass die zweite die erste ist.**

*Gegenprobe, die eine naheliegende Vermutung erledigt hat*: `std.collections.slice` baut auch
über `[xs[from]] * (to - from)` (`collections.lyr:1378`), ist aber **sicher** — es überschreibt
die Slots 1..n einzeln, und Slot 0 hält eine Kopie, nicht die Quelle. Gemessen (r24): Schreiben
in `s[0]` und `s[1]` lässt `xs` bei `1 2 3`.

**(D) Absteigende Ranges laufen null mal, ohne Warnung.** `for (n in 3..0)` — null Durchläufe.
Und das ist **normativ zugesagt**: „a range with `lo > hi` is empty, `a..a` is empty, `a..=a` is
one element" (`spec/07-statements.md:64-65`, gelesen). Es gibt keine Gegenrichtung: kein
`downTo`, kein `step`, kein `rev`.

**(E) Ein `Iterator`-Wert im for-in wird still verbraucht.** Gleiche Syntax, zwei Semantiken:
zwei Schleifen über `over(xs)` geben 3 und 0 Durchläufe, zwei über eine `List` geben 3 und 3
(gemessen, Vorfassung p18). Der Grund steht im Lowering: der `Iterable`-Zweig ruft `iter()` und
bekommt einen frischen Cursor, der direkte `Iterator`-Zweig nicht
(`FunctionLowerer.cs:1242-1271`, gelesen).

**(F) Mutation während der Iteration ist undefiniert und still.** `ListIterator` hält Liste +
Index und fragt bei jedem `next` die Länge neu (`collections.lyr:353-368`): Wachsen gibt 3 statt
2 Durchläufe, Schrumpfen überspringt ein Element (gemessen, Vorfassung p11). Kein
`ConcurrentModification`, kein Snapshot, keine Regel in der Spec.

**(G) `Map<K,V>` ist nicht `Iterable`, `Set<T>` schon.** `for (kv in m)` ist `LYR-SEM0007`; man
schreibt `entries(m)`. `Set<T> :: [Iterable<T>]` steht in `collections.lyr:910`. Zwei ähnliche
Container, zwei Regeln.

**(H) `xs[i] += 1` geht, `xs[i]++` nicht.** Compound-Assignment auf einem Element wertet den
Index **einmal** aus (gemessen, Vorfassung p08) — und das ist seit 4.6 **normativ zugesagt**:
„The target is evaluated ONCE however often the form reads it: `xs[next()] ??= v` calls `next`
once, the same promise `xs[i] += 1` makes." (`spec/06-operators.md:101-102`, gelesen).
`xs[0]++` ist `LYR-IR0001`. Und drei Zeilen weiter **widerspricht sich dasselbe Kapitel**: auf
einem Feld oder Element ist ein interface-gestütztes Compound `LYR-SEM0003`, weil „the shorthand
would evaluate the object or the index twice" (`spec/06-operators.md:104-108`, gelesen). Die
Begründung ist durch den eigenen Satz drei Zeilen darüber erledigt. **Das ist ein
spec-interner Widerspruch, keine Messfrage** — die Vorfassung zitierte nur die zweite Hälfte.

**(I) `Indexable`, `Iterable`, `Iterator` sind Sprachanker, stehen aber in keinem Vertrag.**
§11 (`spec/11-stdlib-contract.md:25-30`) zählt die Anker auf — `Display`, `Equatable`,
`Hashable`, `Ordered`, `Add/Sub/Mul/Div`, `Into`, `WithArg` — **alle in `std.core`**. Die drei
Interfaces, an denen `for-in` und `[]` hängen, stehen nicht dabei, und sie wohnen in **zwei
verschiedenen Modulen** (`std.iter`, `std.collections`).

**Korrektur der Vorfassung (Kritiker hat recht).** Sie schrieb, die drei kämen „im ganzen
Spec-Text nur in §7.2-Prosa und im Diagnose-Anhang vor". Falsch. Gelesen auf `8f17c02`:

- `spec/05-interfaces.md:85-86` — `fn map<U>(f: fn(T) -> U): Iterator<U>` als das
  Musterbeispiel der Monomorphisierungsregel.
- `spec/05-interfaces.md:99-104` — **die normative Heimat genau der Schranke**, die die
  Vorfassung allein einem Quelltextkommentar (`iter.lyr:276-282`) zugeschrieben hat, inklusive
  `fn chunks(): Iterator<T[]>` beim Namen: „demands an instance for the next element type …
  the compilation does not terminate".
- `spec/08-generics.md:75` — `fn find<T>(it: Iterator<T>)` als Beispiel einer generischen
  Interface-Instanz.

**Folge, die die Vorfassung verfehlt hat: COL-12 und COL-21 sind spec-first-Eingriffe in §5.2a,
keine Bibliotheksfragen.** Das steht jetzt in §5.

**(J) Ein `Iterator<T>`-WERT kostet ~15 KB — einmal, nicht pro Elementtyp, und aus einem
anderen Grund als behauptet.** Die Zahlen der Vorfassung reproduzieren sich; ihre **Deutung war
behauptet und ist jetzt gemessen falsch.**

| Form | debug | release | Funktionen im Modul | Code |
|---|---:|---:|---:|---:|
| `Counter` konkret benutzt | 2 973 B | 2 129 B | **4** | 123 B |
| `let c: Iterator<int> = Counter{}` | **17 760 B** | **16 225 B** | **97** | 4 607 B |

(gemessen r15c/r15d; `lyrvm info`.) Die Vorfassung sagte: „Die ~29 Default-Methoden werden für
die Instanz materialisiert." Drei Proben zeigen, dass das die Kosten nicht erklärt:

| Probe | Inhalt | debug |
|---|---|---:|
| r15a | eigenes Interface, **0** Default-Methoden, als Wert | 2 999 B |
| r15b | eigenes Interface, **13 triviale** Defaults (`return 1;`), als Wert | 3 538 B |
| r15e | eigenes Interface, 3 Defaults, die **Adapterklassen** zurückgeben, als Wert | 3 484 B |
| r15c | `std.iter.Iterator<int>` als Wert | **17 760 B** |

13 triviale Defaults kosten 539 B, also ~41 B pro Methode. Drei Adapterklassen kosten 485 B.
**Weder die Defaults noch die Adapter erklären die 14,7 KB.** Die Auszählung der 97 Funktionen
tut es:

```
66  std.iter.Iterator<X>.<default>   = 22 nicht-generische Defaults  ×  3 Elementtypen
27  std.iter.<Adapter>Iterator<X>.next = 9 Adapterklassen            ×  3 Elementtypen
 4  main.main, Counter.next, println, string.show
```

Die drei Elementtypen sind `int`, **`float` und `string`** — und `float` und `string` kommen im
Programm nicht vor. Von den 27 Defaults haben 5 eigene Typparameter (`map`, `flatMap`, `zip`,
`zipWith`, `fold`) und damit keinen Slot; die übrigen 22 brauchen einen, also muss jeder gefüllt
werden, also wird der Rückgabetyp jedes nicht-generischen Adapters erreichbar, also kommt dessen
`next()` mit. Und das passiert für **jede Instanz von `Iterator<…>`, die in der Kompilation
existiert**, nicht für die, die das Programm benutzt.

*Kontrolllauf, der die Zuschreibung entscheidet* (Erwartung vorher hingeschrieben: wenn die
Kosten pro benutztem Elementtyp anfallen, wächst ein zweiter um ~50 %): ein zweites Interface,
`Iterator<string>`, im selben Programm kostet **272 B und eine einzige Funktion mehr**
(18 032 B, 98 Funktionen, gemessen r15f) — weil `Iterator<string>` längst materialisiert war.

**Das ändert COL-12.** Option C („Instanziierung faul machen, nur benutzte Slots
materialisieren") wäre die richtige Antwort, wenn die Slots das Problem wären. Sie sind es nur
mittelbar: das Problem ist, dass **ein Slot existiert**. Option B (Extensions) entfernt den
Slot und damit die Kette; Option C kann das nicht.

*Gegenkontrolle der Vorfassung, die bestehen bleibt*: die bloße Konformanz `:: [Iterator<int>]`
kostet **nichts** (r15d deklariert sie und ist 2 973 B). Es ist der WERT.

**(K) for-in kostet ungefähr das Doppelte einer Indexschleife — aber die Tabelle der
Vorfassung war zwei Messungen in einem Gewand.** Neu gemessen, 3 000 × 1 000 Elemente, dieselbe
Rumpfrechnung, Wanduhr inkl. ~0,1 s VM-Start, zwei Läufe je Zelle (b1–b3):

| Schleifenform | release | debug |
|---|---|---|
| `while (i < xs.length) { total += xs[i]; }` | 1,07 / 0,83 s | 0,84 / 0,93 s |
| `for (i in 0..1000) { total += xs[i]; }` | 1,40 / 1,33 s | 1,77 / 1,75 s |
| `for (x in xs) { total += x; }` | 1,66 / 1,58 s | 1,72 / 1,59 s |

**Der Faktor ~1,7–1,9 zwischen Handindexschleife und Array-for-in bestätigt sich.** Die
Gleichsetzung der beiden for-in-Formen gilt in **debug** (1,76 gegen 1,66 s, innerhalb der
Streuung), in **release nicht** (1,37 gegen 1,62 s) — die Vorfassung hatte sie in der falschen
Spalte gleichgesetzt.

Und die Vorfassung präsentierte das zusammen mit `tools/Bench` aus `STATUS.md:96,98`
(`forin_range` 73,2 ns release / 116,8 ns debug; `forin_array` 154,3 ns / 140,4 ns) als **eine**
Belegkette. Das sind zwei verschiedene Messungen: **meine Range-Form indiziert zusätzlich ein
Array**, die Bench-Form nicht. Sie dürfen nebeneinanderstehen, nicht ineinander.

`STATUS.md:2460-2464` nennt die Indexschleifen-Lowerung bereits als lohnend; hier ist die Zahl.

**(L) Den quadratischen Weg gibt es unter ZWEI Namen, nicht einem.**
`Iterator.toArray()` (`iter.lyr:263-272`) kopiert bei jedem Anhängen, weil `T[]` fest ist, und
sagt das im Doc-Kommentar. `std.iter.collectArray<T>(source)` (`iter.lyr:959-961`) ist
`return source.toArray();` — **derselbe quadratische Weg, zweiter Name, wortgleicher
Doc-Kommentar** (gelesen, beide Stellen). Der lineare Weg heißt `std.collections.collect`
(`collections.lyr:375`) und liefert eine `List<T>`. **Drei Namen, zwei Komplexitäten** — und
`collectArray` ist ausgerechnet der Name, den COL-14 vergeben würde.

**(M) Kleinkram, der zusammen ein Muster ergibt.** `xs.length` ist eine Property, `s.length()`
eine Methode mit Import (absichtlich, weil O(n)). `==` ist für Arrays `LYR-SEM0059` (gemessen,
r06). Arrays rendern nicht im f-String (`docs/guide/02-values-and-types.md:103`).
`[first, ..rest]` **kopiert** (gemessen, r21: `rest[0] = 99` lässt `xs[1]` bei 2).
`params` leitet ein übergebenes Array **ohne Kopie** weiter, mit Spread-Argumenten baut es ein
frisches (gemessen, r20: whole `99`, spread `1`). `(0..3)` in Klammern ist im Schleifenkopf
erlaubt, in Wertposition `LYR-SEM0090` (gemessen, Vorfassung p02b/p03b).

**(N) `extend int[] { … }` wird angenommen und tut nichts — und jetzt wissen wir warum.**
Der Block erzeugt keine Diagnose; erst der Aufruf ist `LYR-SEM0012` (gemessen, r14). Die
Vorfassung stellte das fest und fragte nicht weiter. Die Antwort steht in
`TypeChecker.cs:623-656` (gelesen):

```
629  if (block.Target is null)
634      if (_binding.Resolve(block.Decl.Target) is not (null or ErrorSymbol))
635          Report("LYR-SEM0047", "extend target must be a plain named type in v1
636                  (no generic, array, tuple or function targets)");
637      continue;
643  var thisType = block.Target.Kind == TypeSymbolKind.Builtin
644      ? TypeFacts.FromBuiltinName(block.Target.Name) : new NamedRef(block.Target);
```

`string` ist ein **benanntes Builtin-Symbol** mit Member-Tabelle (Zeile 643-644), also wirkt
`extend string`. `int[]` und `(int, int)` sind **strukturelle Typen ohne Symbol** — `Resolve`
gibt `null`, die Bedingung in 634 ist falsch, und der Block fällt durch `continue` ohne einen
Laut. `LYR-SEM0047` **nennt „array, tuple" im eigenen Meldungstext und kann für sie nie feuern.**
Gemessen, drei Fälle:

| Form | Ergebnis |
|---|---|
| `extend List<int> { … }` | `LYR-SEM0047` (gemessen, r16a) |
| `extend (int, int) { … }` | **schweigt, kompiliert** (gemessen, r16b) |
| `extend int[] { … }` | **schweigt, kompiliert** (gemessen, r14) |
| `extend string { … }` | funktioniert (gemessen, r16c) |

Das entscheidet COL-14: die Frage ist nicht „darf man auf `T[]` extenden", sondern „bekommt ein
struktureller Typ einen Träger für Member".

### 1.3 Was ausdrücklich schon entschieden ist

- „Ranges sind kein Wert" — `docs/Grammar.md:423-426`, `spec/03-types.md:79`. Konformanzfall:
  `lyric-spec/conformance/cases/07-statements/range_is_a_loop_head_not_a_value.lyr`
  (`since: 3.3.0`) — **genau einer** (gelesen, ausgezählt).
- `string` ist nicht indexierbar (O(n) pro Codepoint) — `spec/03-types.md:73-75`.
- `Iterator<?T>` existiert nicht, `LYR-SEM0091` — `spec/07-statements.md:58-62`,
  `STATUS.md:2446-2458`.
- `unsafe` zum Entfernen der Bounds-Checks: **dokumentiertes Nein mit Messung** —
  `STATUS.md:2466-2471`: „a compare and a branch INSIDE a dispatch that costs ~23 cycles. It
  buys about one percent."
- `enumerate`/`chunks` bleiben freie Funktionen — **normativ** in `spec/05-interfaces.md:99-104`,
  nicht nur `iter.lyr:276-282`.
- Set-Iterationsreihenfolge ist „UNSPECIFIED" — aber nur im **stdlib-Doc**
  (`collections.lyr:907-908`), nicht in der Spec.
- Slices: **offen**, `STATUS.md:2173`, `PLAN.md:310` (Position 21) und `PLAN.md:445` (Frage 2).
  Prototyp 09 (`docs/Befunde_und_Verbesserungen/prototypes/09-slices/README.md`) empfiehlt
  Kopie-Semantik **und hält ausdrücklich fest, dass §7.2 bleibt**.

---

## 2. Sprachvergleich

Die Vorfassung hatte elf sachliche Fehler über die Vergleichssprachen. Sie sind hier korrigiert;
wo die Korrektur eine Empfehlung verschiebt, steht es dabei.

### 2.1 Die Achsen

| | **Lyric 4** | **Rust** | **Go** | **C#** | **Swift** | **Python** | **Kotlin** | **Zig** | **Scala** |
|---|---|---|---|---|---|---|---|---|---|
| Länge im Typ? | nein (Wert) | **ja** `[T;N]` | **ja** `[3]int` | nein | nein | nein | nein | **ja** `[N]T` | nein |
| Array-Semantik | Referenz | Wert (Copy/Move) | **Wert** (kopiert bei Zuweisung) | Referenz | Wert (COW) | Referenz | Referenz | Wert | Referenz |
| primäre Sequenz | `T[]` fest + `List<T>` | `Vec<T>` + `&[T]` | `[]T` (Slice) | `T[]` + `List<T>` + `Span<T>` | `Array<T>` | `list` | `List`/`Array` | `[]T` + `ArrayList` | `Seq`-Hierarchie |
| Slice / View | **fehlt** | `&[T]` View | `s[a:b]` View **mit** Aliasing | `Span<T>` View / `..` auf Array kopiert | `ArraySlice` (geteilt, versetzte Indizes) | `xs[a:b]` **Kopie** | `subList` View / `sliceArray` Kopie | `xs[a..b]` View | `.view` lazy |
| Range als Wert | **nein** | ja, `Range<T>` — `Iterator` **nur für `T: Step`** | nein | ja, `System.Range` (C# 8) | ja, `Range<Bound>` | ja, `range`-Objekt | ja, **`IntRange` *und* `IntProgression`** | **nein — `0..n` steht NUR im Schleifenkopf** | ja, `Range <: Seq` |
| Rückwärts/Schritt | **fehlt** | `.rev()`, `.step_by(n)` | manuell | `Enumerable.Reverse` | `stride(from:to:by:)` | `range(a,b,-1)` | `downTo`/`step`/`until` — **Extensions, keine Methoden** | manuell | `Range.by(-1)` |
| Index-Schlüssel | **nur `int`** | beliebig (`Index<Idx>`) | `int` / Map-Key eingebaut | beliebig (`this[K]`, mehrstellig) | beliebig (assoziierter `Index`) | beliebig (`__getitem__`) | beliebig (`operator get`) | `usize` | beliebig (`apply`) |
| Iterator-Protokoll | `next(): ?T` | `next() -> Option<T>` | `for … range` + ab 1.23 `iter.Seq` (Push) | `MoveNext()` **Methode** + `Current` **Property** + `Reset()` | `next() -> Element?` | `__next__` + `StopIteration` | `hasNext()` + `next()` | keins | `hasNext()`+`next()` |
| Adapter wohnen | **Default-Methoden am Interface** | Provided methods am Trait | Pakete `slices`/`maps`/`iter` | **Extension-Methoden** (LINQ) | Protocol Extensions | Builtins + `itertools` | **Extension-Funktionen** | — | Methoden der Hierarchie |
| Collection-Literal | nur `[…]` für Array | **`[1,2,3]` Array-Literal**, `vec![]` Makro für `Vec` | Composite Literals | `[1,2,3]` für **vier** Routen (C# 12) | `[1,2]`, `["k":1]` via `ExpressibleBy…Literal` | `[]`, `{}`, `()` | `listOf`, `mapOf` | `.{…}` | `List(…)`, `Map(…)` |
| Mutation beim Walken | undefiniert, still | vom Borrow-Checker verboten | **Slice: definiert** (Range-Ausdruck einmal ausgewertet); **Map: Reihenfolge unspezifiziert, Regeln benannt** | `InvalidOperationException` | COW: Kopie | `RuntimeError` bei dict, still bei list | `ConcurrentModificationException` | — | undefiniert |
| for-in nimmt | 5 Formen, 3 Compiler-Sonderfälle | `IntoIterator` | eingebaut | **Muster**, kein Interface | `Sequence` | `__iter__` | `Iterator`-Konvention | Slices + Ranges | `Iterable` |

### 2.2 Je Sprache: was sie tut, was sie zahlt, was zu Lyric passt

**Rust — die Gegenentscheidung auf zwei Achsen.** Länge im Typ (`[T; N]` mit const generics),
Slice `&[T]` ist ein View, `Index<Idx>` ist generisch über den Schlüssel, also gibt `xs[1..3]`
ein Slice und `map[&key]` einen Wert mit **einem** Mechanismus.
*Korrektur*: `Range<T>` ist für **jedes** `T` ein gewöhnlicher Struct; `Iterator` kommt aus einer
Blanket-Impl für `T: Step` (ganzzahlige Grenzen und `char`). Die Achsentabelle der Vorfassung
schrieb „ja, `Range<T>: Iterator`" und widersprach damit ihrem eigenen COL-04 drei Seiten
später. *Zweite Korrektur*: `[1, 2, 3]` ist in Rust ein vollwertiges **Array-Literal mit Länge
im Typ**; `vec![]` ist nur das Makro für den wachsenden Typ. In einem Dossier, dessen COL-01
genau „Länge im Typ oder im Wert" heißt, war das Weglassen eine Verzerrung.
*Preis*: Borrow-Checker, vier Sequenzformen, `Option<Option<T>>`.
*Für Lyric*: `Index<Idx>`, `IntoIterator` als **die eine** Schleifenschnittstelle, und die Regel
`[x; n]` nur für `Copy`.

**Go — die andere Gegenentscheidung, und die lehrreichere.** `[3]int` hat die Länge im Typ und
Wertsemantik; benutzt wird das Slice `[]T` = (Zeiger, len, cap), ein View. *Preis*: zwei
Sequenztypen und das `append`-Aliasing, das von `cap` abhängt und nicht im Typ steht — genau
davor warnt Prototyp 09.
*Korrektur 1*: „Mutation beim Walken: undefiniert (Map: zufällig)" war überzeichnet. Die
Go-Spezifikation wertet den Range-Ausdruck bei Slices **genau einmal** aus, ein `append` in der
Schleife verlängert sie also beweisbar nicht — das ist **definiert**. Für Maps ist die Spec
explizit und begrenzt: die **Reihenfolge** ist unspezifiziert, ein während der Iteration
erzeugter Eintrag darf erscheinen oder fehlen, ein entfernter erscheint nicht mehr. Go ist damit
kein Beleg für „undefiniert, aber speichersicher", sondern das Beispiel dafür, **den Spielraum
auszusprechen** — also genau das, was COL-16 B eigentlich will.
*Korrektur 2*: bei `iter.Seq` — `func(yield func(V) bool)` — sagt der `bool`, den `yield`
**zurückgibt**, dem Produzenten, dass der **Konsument** abbrechen will. Das Ende der Folge ist
schlicht die Rückkehr der Seq-Funktion. Der bool beantwortet den frühen Abbruch, **nicht die
Erschöpfung**. Die Vorfassung las ihn als Ende-Signal und zog daraus „exakt der Ausweg aus
Lyrics `?T`-Sackgasse" — die Schlussfolgerung beruhte auf der falschen Lesart.

**C# — der nächste Verwandte.** Arrays sind Referenztypen fester Länge mit der Länge im Wert
(wie Lyric). `System.Index`/`System.Range` sind Werte; `a[1..3]` auf einem Array **kopiert**, auf
einem `Span<T>` ist es ein View — dieselbe Syntax, zwei Semantiken, sichtbar am Typ. Indexer
`this[K]` nehmen beliebige Schlüssel, auch mehrere.
*Korrektur 1*: **`foreach` ist musterbasiert, nicht schnittstellenbasiert.** Jeder Typ mit einem
zugänglichen `GetEnumerator()`, dessen Ergebnis `MoveNext()` und `Current` hat, funktioniert —
ohne jedes Interface; seit C# 9 zählt auch eine **Extension**-`GetEnumerator()`. Die Folgerung
der Vorfassung („ein verbrauchter Enumerator kann gar nicht zweimal in einem `foreach` stehen",
„strukturell unmöglich") wird von der Sprache **nicht erzwungen**; sie hält nur, weil
`IEnumerator<T>` zufällig kein `GetEnumerator` mitbringt, und eine einzeilige Extension hebelt
sie aus. **Das Beispiel, das die Regel wirklich erzwingt, ist Rusts `IntoIterator`.**
*Korrektur 2*: `IEnumerator` hat `MoveNext()`, die **Property** `Current` und `Reset()`. Die
tragende Gefahr ist „Zustand über zwei Zugriffe verteilt", nicht „zwei Methoden".
*Korrektur 3*: Collection Expressions (C# 12) bauen aus `[…]` **vier** Familien: Arrays und
`Span`/`ReadOnlySpan`; die Schnittstellen `IEnumerable<T>`/`IReadOnlyCollection<T>`/
`IReadOnlyList<T>`/`ICollection<T>`/`IList<T>`; Typen mit `CollectionBuilder`-Attribut; **und
jeden Typ mit parameterlosem Konstruktor und passendem `Add`**. `CollectionBuilder` ist nur der
Opt-in für unveränderliche Typen. Die Vorfassung nahm die schmalste Route als Vorlage und
verschenkte damit die, die `List<T>` in Lyric sofort bedienen würde (Konstruktor + `push`).
*Für Lyric*: `Range` als Wert plus Kopie beim Array, der Modifikationszähler, und die
**Add-Route** als `FromLiteral`-Vorlage.

**Swift — die Protokoll-Variante.** `Collection` hat einen assoziierten Index-Typ; für `String`
ist er ein opakes `String.Index` — dieselbe Begründung wie Lyrics Verbot auf Strings, aber im
Typsystem gelöst. `ArraySlice<T>` teilt Speicher und behält die **ursprünglichen** Indizes
(`slice[0]` crasht). `IteratorProtocol.next() -> Element?` ist genau Lyrics Protokoll — und
Swift kann `Optional<Optional<T>>`.
*Korrektur*: „assoziierte Typen machen Protokolle nicht mehr als Werte verwendbar" ist
**veraltet**. Seit Swift 5.7 (SE-0309) sind Protokolle mit assoziierten Typen als Existential
verwendbar (`any Collection`), und primäre assoziierte Typen (SE-0346) erlauben
`any Collection<Int>`. Geblieben ist der Aufwand des Öffnens und der Verlust statischer
Garantien. **COL-07 D trug damit ein zu hohes Preisschild.**

**Python — die Warnung.** Slices kopieren, `range` ist ein Objekt (lazy, indizierbar, `in` in
O(1)), negative Indizes zählen von hinten, `__getitem__` nimmt jeden Schlüssel — und `[[0]*3]*2`
hat exakt Lyrics Falle. Eine erschöpfte Generator-Schleife schweigt, wie Lyrics (E).
*Korrektur*: `xs[-1]` auf einer **leeren** Liste wirft `IndexError`. Was nie knallt, ist ein
**kleiner negativer Index in eine nichtleere Liste** — der Off-by-one, der als gültiger Zugriff
vom anderen Ende durchgeht. Genau der trägt die Empfehlung, negative Indizes nicht zu übernehmen.

**Kotlin — die Ergonomie-Vorlage, und die Vorfassung hat sie falsch beschrieben.**
*Korrektur*: **keines** von `downTo`, `until`, `step`, `reversed()` ist eine Methode auf
`IntRange`. `downTo` und `until` sind infix-**Extensions auf `Int`**, die eine
`IntProgression`/`IntRange` **erzeugen**; `step` ist eine infix-Extension auf `IntProgression`;
`reversed()` eine Extension auf `IntProgression`. Zwei Folgen:
(a) COL-05s „braucht COL-03" ist nur halb wahr — gebraucht wird der Range-**Rückgabetyp**, nicht
der Range-Empfänger;
(b) Kotlin trennt bewusst **`IntRange` von `IntProgression`**, weil eine gesteppte oder
absteigende Folge ein **anderer Typ** ist als `a..b`. Genau diese Unterscheidung ebnete COL-03 C
ein, ohne zu sagen, was dann `(0..10).step(2).contains(3)` bedeutet.
Die Adapter sind Extension-Funktionen, nicht Interface-Defaults — deshalb kostet ein eigener
`Iterator` nichts. *Preis*: zwei parallele Kettenwelten (`Iterable` eager / `Sequence` lazy) sind
eine echte Doppelung. **Die hat Lyric bereits** (s. COL-35).

**Zig — der einzige Verbündete Lyrics bei COL-03, und die Vorfassung hat ihn weggeschrieben.**
*Korrektur*: die Achsentabelle sagte „Range als Wert: nein (nur Slice-Syntax)". Zigs
`for (0..n) |i|` ist **kein Slice-Ausdruck**, sondern eine Range-Form, die ausschließlich im
Schleifenkopf steht und nirgends sonst — **exakt Lyrics heutige Regel**. Für eine Frage, deren
Empfehlung „Regel umstoßen" lautet, ist das der wichtigste Gegenzeuge.
Länge im Typ (`[N]T`), Slice `[]T` ist ein Wert aus (Zeiger, Länge), und es gibt **gar kein**
Iterator-Protokoll: `for (xs, 0..) |x, i|` gibt den Index als zweiten Capture dazu. *Preis*:
keine generischen Ketten. *Für Lyric*: der Index-Capture ist die eleganteste `enumerate`-Form
der neun Sprachen und umgeht die Monomorphisierungsgrenze vollständig.

**Scala — das Warnschild.** Eine Hierarchie für alles, `apply`/`update` mit beliebigen Typen,
`Range` ist ein `Seq`, `.view` macht jede Kette lazy. *Preis*: die Collections-Bibliothek ist
zweimal komplett umgebaut worden (2.8 `CanBuildFrom`, 2.13 `IterableOps`). *Für Lyric*: die
Warnung davor, die Ketten-API über eine Typhierarchie generisch machen zu wollen.

---

## 3. Designfragen

41 Fragen. COL-01…COL-22 sind die der Vorfassung, korrigiert; COL-23…COL-41 sind die, die sie
nicht gestellt hat. Vier Fragen tragen **keine** Empfehlung — sie stehen in §6.

---

### COL-01 — Bleibt die Länge im Wert, oder bekommt der Typ sie?

**Heute.** `T[]` hat keine Länge im Typ; `int[3]` ist `LYR-PAR0043` (`spec/03-types.md:71-73`,
gelesen).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | C#, Swift, Python, Kotlin | keine Compile-Zeit-Bounds-Elision; `arrayOf` bleibt die Bau-Form |
| B | `[T; N]` mit Wertparametern dazu | Rust, Zig, Go | verlangt Wertparameter in Generics, zweiter Array-Typ → Rule 2 |
| C | A, aber `comptime`-Länge für den Optimierer nutzen | — | kein Sprachbruch, rein interner Gewinn |

**Empfehlung: A, mit C als Optimierernotiz.** Const generics sind ein Generics-Feature; sie
zahlen sich erst aus, wenn es SIMD oder `unsafe` gibt — beides dokumentiert draußen
(`STATUS.md:2466-2471`). Und COL-32 zeigt: die Elision ist ohnehin nicht der Hebel.

**Bricht:** nein. **Hängt an:** Generics-Gebiet. **Konfidenz:** gelesen.

---

### COL-02 — Slices: gibt es sie, und kopieren sie oder teilen sie?

**Heute.** Es gibt sie nicht. Ersatz ist `std.collections.slice(xs, from, to)`
(`collections.lyr:1372-1386`, Kopie, Panik bei falschen Grenzen). `[first, ..rest]` **kopiert**
(gemessen, r21) — laut `PLAN.md:162` genau deshalb, weil es keine Slices gibt. Offen in
`STATUS.md:2173`, `PLAN.md:310` (Position 21) und `PLAN.md:445` (Frage 2).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nichts, nur die std-Funktion | Status quo | `..rest` kopiert weiter |
| B | `xs[a..b]` als Sprachsyntax, Ergebnis **`T[]` (Kopie)** | Python, C# auf Arrays | O(n) pro Slice; keine Aliasing-Überraschung; Prototyp 09 durchgerechnet (~110 Z.) |
| C | `xs[a..b]` als **View** `Span<T>` | Go, Rust, Zig | zweiter Sequenztyp (Rule 2), `Span` muss überall angenommen werden, wo `T[]` steht |
| D | B jetzt, C später als `asSpan()` | C# | zwei Formen, aber die teilende trägt ihren Namen |

**Empfehlung: B.** Der Grund ist Rule 2: ein View ist ein zweiter Sequenztyp und infiziert jede
Signatur. Wer O(1)-Fenster braucht, hat `skip`/`take` — kopierfrei und lazy. Offene Formen
`[..b]`, `[a..]`, `[..]` mit aufnehmen (Prototyp 09 §Soll).

**Das Projekt hat zweimal das Gegenteil aufgeschrieben, und die Vorfassung nannte es nicht:**
`lyric-v5-features.md:42` (Posten 10) fordert wörtlich „`xs[a..b]` als View ohne Kopie
(`Span<T>`), Ranges als Werte, `(0..n).map(...)`"; `PLAN.md:310` (Position 21) sagt dasselbe.
Wer B nimmt, streicht Posten 10 halb. Das gehört in §5 und steht dort jetzt.

**Bricht:** nein (additiv). **Hängt an:** COL-03 — und die beiden können **nicht beide**
„übernimm Prototyp 09" heißen, s. COL-03. **Konfidenz:** gemessen (r21) + gelesen.

---

### COL-03 — Wird eine Range ein Wert?

**Heute.** Nein, ausdrückliche Regel mit eigener Diagnose: `docs/Grammar.md:423-426`,
`spec/03-types.md:79`, `LYR-SEM0090`. Ersatz sind `std.iter.range` und `rangeInclusive`
(`iter.lyr:443-458`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | Regel halten; Index-Klammer bekommt eine zweite Sonderposition | Status quo + **Zig** (`0..n` steht nur im Schleifenkopf) + Prototyp 09 | `..` an drei Stellen mit drei Bedeutungen und nirgends ein Wert |
| B | **ein** `Range<T>`-Wert, `:: [Iterable<T>]` | Rust (`T: Step`), Swift, C#, Scala | `(0..n).map(f)` geht; `iter.range`/`rangeInclusive` müssen weg (Rule 2) |
| C | zwei Typen: `Range<T>` (`a..b`) und `Progression<T>` (gesteppt/absteigend) | **Kotlin** | zwei Typen statt einem — aber `(0..10).step(2)` hat dann eine Antwort |

**Korrektur gegenüber der Vorfassung.** Sie empfahl „C: `Range<int>` mit `downTo`/`step`/`rev`
als Methoden, Vorbild Kotlin". Kotlin macht das **nicht** so (s. 2.2): die vier sind Extensions,
und Kotlin **trennt** `IntRange` von `IntProgression`, weil eine gesteppte Folge ein anderer Typ
ist. Die alte Option C war eine Kotlin-Empfehlung, die Kotlin widerspricht, und sie ließ
`(0..10).step(2).contains(3)` offen.

**Empfehlung: B, und `step`/`downTo` als COL-05 separat entscheiden.** Ein `Range<int>` ersetzt
`iter.range`/`rangeInclusive` (ein Mechanismus statt zwei) und macht COL-02 und COL-05 zu
gewöhnlichen Fragen. Die ursprüngliche Begründung war „eine Range hat keine Repräsentation" —
mit einem Struct aus zwei Zahlen plus `inclusive`-Flag hat sie eine. **Aber:** Zig ist der
Gegenzeuge, und seine Regel ist Lyrics. Wer A nimmt, ist in Gesellschaft; er muss dann nur
aufhören, so zu tun, als sei `iter.range` etwas anderes als ein Range-Wert ohne Syntax.

**Kosten an der Konformanz-Suite, ausgezählt:** genau **ein** Fall retiriert
(`conformance/cases/07-statements/range_is_a_loop_head_not_a_value.lyr`, `since: 3.3.0`), von
178 (gelesen). Drei Spec-Stellen brauchen ein `since: 5.0`-Gate: `03-types.md:79`,
`07-statements.md:52-56`, `Grammar.md:423-426`.

**Bricht:** minor (`LYR-SEM0090` verschwindet; heute abgelehnte Programme kompilieren).
**4.x-Uhr:** ab 4.7 `@Deprecated(until = "5.0")` auf `iter.range`/`rangeInclusive` — **`until`
ist ein `string`**, `stdlib/std/core.lyr:504-509` (gelesen); die Vorfassung schrieb dreimal
`until = 5.0` als Zahlenliteral, das kompiliert nicht.
**Hängt an:** COL-02, COL-05, COL-10. **Konfidenz:** gelesen + gemessen (Suite ausgezählt).

---

### COL-04 — Welchen Elementtyp darf eine Range haben? (Float-Bug)

**Heute.** Jeden „numeric" inkl. `float` (`TypeChecker.cs:2907`, `TypeFacts.cs:27-30`).
Float-Grenzen werden still auf `i64` abgeschnitten (gemessen mit Kontrolllauf, r22, auf HEAD
4.6.0 bestätigt). `char` funktioniert korrekt. Die Spec sagt zum Elementtyp nichts.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nur ganzzahlige Typen und `char`; float → `LYR-SEM0003` | Rust (`Range<f64>` ist kein `Iterator`, weil `f64: !Step`), Kotlin, Go | `for (x in 0.0..1.0)` muss umgeschrieben werden — heute rechnet es falsch |
| B | Float-Ranges korrekt mit Schrittweite 1.0 | Scala `Range.Double` (**deprecated**), `numpy.arange` | akkumulierender Rundungsfehler, Elementanzahl nicht vorhersagbar |
| C | Float-Ranges nur mit explizitem `step` | Swift `stride(from:to:by:)` | braucht COL-05 und COL-03 zuerst |

**Empfehlung: A, sofort als Bugfix in 4.x.** Der einzige Punkt dieses Dossiers, der **heute
rechnet und falsch rechnet**. Wer Float-Schritte will, schreibt
`for (i in 0..n) { let x = lo + (i as float) * step; }`.

**Bricht:** minor. **4.x-Uhr:** ab 4.7 Warnung, Fehler in 5.0. **Hängt an:** nichts.
**Konfidenz:** gemessen.

---

### COL-05 — Rückwärts und Schrittweite

**Heute.** Gar nicht. `3..0` läuft null mal, und das ist **normativ zugesagt**
(`spec/07-statements.md:64-65`, gelesen). `stepBy` gibt es als Iterator-Adapter
(`iter.lyr:70-74`), aber nicht auf einer Range, weil es keinen Range-Wert gibt.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nichts; `while` ist der Weg | Zig, Go | jede Rückwärtsschleife ist vier Zeilen und ein Off-by-one-Risiko |
| B | `downTo`/`step`/`until` als **freie oder Extension-Funktionen, die einen `Progression<T>` erzeugen** | **Kotlin, korrekt gelesen** | braucht COL-03 B (den Rückgabetyp), **nicht** den Range-Empfänger; zweiter Folgentyp |
| C | `rev()`/`stepBy()` als Iterator-Adapter, plus `iter.rangeDown` | Rust | `rev()` verlangt ein `DoubleEnded`-Protokoll — zweiter Mechanismus |
| D | `for (i in 0..n) reversed` als Schlüsselwort | — | neue Syntax für einen Spezialfall |

**Empfehlung: B**, zusammen mit COL-03 B. `for (i in n downTo 0 step 2)` ist die lesbarste Form
und kostet nichts, wenn der Compiler die Form im Schleifenkopf weiterhin speziell lowert.
C braucht ein zweites Iterator-Protokoll und bleibt draußen.

**Die Zusatzforderung der Vorfassung fällt.** Sie wollte „eine Warnung, wenn eine Range mit
konstanten Grenzen `lo > hi` hat — das ist immer ein Tippfehler". Zwei Einwände:
(a) `spec/07-statements.md:64-65` **definiert die leere Schleife ausdrücklich** — eine Warnung
ist machbar, braucht aber einen Spec-Zusatz, den die Vorfassung nicht benannt hat;
(b) „immer ein Tippfehler" ist falsch, sobald `a` und `b` aus `static let`-Konstanten kommen und
die leere Schleife die gewollte Antwort ist. **Wenn die Warnung kommt, dann nur für
Literal-Grenzen** — und mit Spec-Satz.

**Bricht:** nein. **Hängt an:** COL-03. **Konfidenz:** gelesen.

---

### COL-06 — Welcher Typ darf ein Index sein? (ICE-Bug)

**Heute.** Sema: „integer" inkl. `char` und aller Breiten (`TypeChecker.cs:3032`,
`TypeFacts.cs:21-25`). Lowering: nur `i64`. Alles dazwischen ist `LYR-CLI0020` (gemessen, r23 /
r23b, auf HEAD bestätigt); die Kontrolle mit explizitem `.get(i)` liefert sauber `LYR-SEM0001`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nur `int`; alles andere `LYR-SEM0001` wie jedes andere Argument | Zig (`usize`), Go | `xs[i32Wert]` braucht `as int` — konsistent mit dem Rest der Sprache |
| B | jede Ganzzahlbreite, das Lowering konvertiert | C#, Java | implizite Weitung an genau einer Stelle der Sprache |
| C | wie B, plus `char` | Status quo (nur kaputt) | `xs['a']` ist niemandes Absicht |

**Empfehlung: A.** Lyric hat sonst keine implizite numerische Weitung; der Index darf keine
Ausnahme sein. **Und unabhängig von der Entscheidung: der ICE muss in 4.7 fallen** —
`CheckIndex` soll die normale Zuweisbarkeitsprüfung gegen `int` fahren statt `IsInteger`.

**Bricht:** nein für gültigen Code. **Hängt an:** nichts. **Konfidenz:** gemessen.

---

### COL-07 — Indexierung über beliebige Schlüssel: `m["k"]`

**Heute.** `Indexable<T>` kennt nur `int` (`collections.lyr:19-24`); `m["k"]` ist `LYR-SEM0007`.
`lyric-v5-features.md:41` (Posten 9) fordert `Index<K,V>`/`IndexSet<K,V>`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Go | `m.get(k)!` statt `m[k]` |
| B | `Index<K, V>` + `IndexSet<K, V>` **ersetzen** `Indexable<T>` | Rust (`Index`/`IndexMut`), Kotlin | Bruch für jeden eigenen `Indexable`-Typ; `Index<int, T>` ist die Array-Form |
| C | beide nebeneinander | — | zwei Mechanismen für eine Klammer → Rule 2 |
| D | assoziierter `Index`-Typ am `Collection`-Interface | Swift | verlangt assoziierte Typen; löst COL-02 und `String.Index` mit |

**Empfehlung: B**, mit D als Fernziel. Wichtig ist **ersetzen**: `Indexable<T>` wird
`Index<int, T>`. Zwei Interfaces (lesen/schreiben getrennt) statt einem, damit ein nur lesbarer
Container kein `set` erfinden muss.

**Der Preis von D ist niedriger, als die Vorfassung schrieb**: seit Swift 5.7/SE-0309 sind
Protokolle mit assoziierten Typen als Existential verwendbar. Was bleibt, ist der Aufwand des
Öffnens, nicht eine Sperre.

**Was B ohne COL-23 nicht kann:** `Index<K,V>.get(k): V` kann nicht ausdrücken, dass ein Element
fehlt. Die Vorfassung baute die Schnittstelle neu, ohne die Frage zu stellen. Sie steht jetzt in
COL-23.

**Bricht:** major. **4.x-Uhr:** ab 4.7 `Index`/`IndexSet` einführen, `Indexable` per
`@Deprecated(until = "5.0")`. **Hängt an:** Operator-Gebiet, COL-08, COL-23.
**Konfidenz:** gelesen.

---

### COL-08 — Wo wohnen `Iterator`, `Iterable`, `Indexable`, und stehen sie im Vertrag?

**Heute.** `Iterator`/`Iterable` in `std.iter`, `Indexable` in `std.collections`, keines in §11
(`spec/11-stdlib-contract.md:25-30`, gelesen).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen, nur §11 ergänzen | — | Modul-Asymmetrie bleibt |
| B | alle drei nach `std.core`, §11 ergänzt sie | Rust (`core::ops`, `core::iter`) | Bruch aller Imports; `std.iter` behält die Adapter |
| C | B, aber `std.iter`/`std.collections` re-exportieren | C# | braucht Re-Export im Modulsystem, das es nicht gibt |

**Empfehlung: B.** Ein Interface, an dem eine **Sprachform** hängt, gehört dorthin, wo `Display`
und `Add` stehen.

**Der Migrationsweg hat ein Loch, das die Vorfassung nicht sah:** sie stützte die Import-Migration
auf `lyrfix`. **`lyrfix` existiert nicht** — `src/` enthält 17 Projekte, keins davon `Lyrfix`
(gelesen, `ls src/`), und `PLAN.md:343` führt es als erst zu bauendes v5-Werkzeug. Wenn es nicht
gebaut wird, ist B eine Handmigration jeder `import std.iter { Iterator }`-Zeile. Das ist
machbar, aber es ist eine andere Entscheidung. S. COL-38.

**Bricht:** major (Imports). **4.x-Uhr:** ab 4.7 in `std.core` deklariert, in `std.iter` als
Alias mit `@Deprecated(until = "5.0")`. **Hängt an:** Modul-Gebiet, COL-38.
**Konfidenz:** gelesen.

---

### COL-09 — Wie lowert `for (x in array)`?

**Heute.** Über eine `ArrayIterator<T>`-Instanz mit `next(): ?T`
(`src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs:1273-1286`), also eine Allokation pro
Schleife und pro Element ein `callvirt` plus `optissome`/`optget`. Gemessen: ~1,7–1,9× einer
Handindexschleife (1.2 K). `STATUS.md:2460-2464` nennt die Alternative bereits.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | die idiomatische Schleife bleibt die langsame |
| B | Arrays als Indexschleife lowern | Go, Zig, C# (`foreach` über Array ist eine `for`-Schleife) | **ersetzt** den bestehenden Array-Zweig; macht `(?T)[]` im for-in möglich |
| C | B, plus dasselbe für `List<T>` per Devirtualisierung | C# (Struct-Enumerator) | verlangt Struct-Iteratoren oder Escape-Analyse |

**Korrektur der Vorfassung.** Sie nannte B „ein vierter Sonderpfad in `LowerForIn`". `BuildIterator`
(`FunctionLowerer.cs:1228`) hat bereits vier Zweige — `Iterable` (1242), Array (1273), String
(1292), Range (1310) — plus den direkten `Iterator`-Wert. **B ersetzt den Array-Zweig, es kommt
keiner dazu.** Und COL-22 B will alle vier auf einen reduzieren. Die beiden Empfehlungen ziehen
gegeneinander; das steht jetzt in §5.

**Empfehlung: B, unabhängig von v5.** Keine Semantikänderung, eine Allokation und eine
Optional-Verpackung pro Element weniger, und `LYR-SEM0091` fällt für Arrays nebenbei.

**Bricht:** nein. **Hängt an:** COL-22 (Richtungskonflikt). **Konfidenz:** gemessen + gelesen.

---

### COL-10 — Das Iterator-Protokoll: bleibt `?T` das Ende?

**Heute.** `next(): ?T`, `null` = Ende (`iter.lyr:21-25`). Folge: `Iterator<?T>` existiert nicht
(`LYR-SEM0091`, `spec/07-statements.md:58-62`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | `?T` bleiben | Swift (`next() -> Element?`) | `Iterator<?T>` bleibt unmöglich; mit COL-09 B betrifft das nur eigene Iteratoren |
| B | `hasNext()` + `next()` | Kotlin, Java, Scala; C# mit `MoveNext()` + Property `Current` + `Reset()` | Zustand über zwei Zugriffe verteilt |
| C | **Push-Iteration**: `fn each(f: fn(T) -> bool): void` | Go 1.23 `iter.Seq` | kein Iterator-Objekt, keine Allokation; **Ketten werden schwerer** (`zip` braucht zwei Push-Quellen) |
| D | Sentinel-Struct `Step<T> { done: bool, value: T }` | — | verlangt einen Wert für `T` auch am Ende — es gibt kein `default(T)` |

**Zwei Korrekturen am Kostenvergleich der Vorfassung.**
(a) Sie begründete A damit, dass Gos Push-Iteration „das Ende als bool-Rückgabewert" habe. Das
ist eine Verwechslung: der bool sagt **Abbruch**, nicht **Erschöpfung** (s. 2.2). Die eigene
Optionstabelle formulierte es richtig; der Fließtext widersprach ihr.
(b) Sie schrieb, C brauche „Koroutinen an jeder Kreuzung", und nahm das als Sperre. **Lyric hat
Koroutinen** (`docs/guide/11-coroutines.md`, `spec/10-coroutines.md`), und sie sind seit 4.0
stackful. Das ist kein Argument gegen C, es ist eins dafür — und es führt direkt zu COL-26.

**Empfehlung: A** — aber aus dem richtigen Grund: die Kette ist seit 2.17/4.5 gebaut, `map`/`zip`
liegen als Default-Methoden vor, und C würde sie komplett umbauen. Der Preis von A ist mit
COL-09 B klein. **Was dazugehört:** §7.2 nach COL-09 B neu formulieren — der Array-Fall ist dann
erlaubt, und `LYR-SEM0091` schrumpft auf eigene Iteratoren.

**Bricht:** nein (A). **Hängt an:** COL-09, COL-26, Optional-Gebiet. **Konfidenz:** gelesen.

---

### COL-11 — `Iterator`-Wert und `Iterable`-Wert im selben `for`

**Heute.** Beides erlaubt, mit verschiedener Semantik und ohne Diagnose (gemessen, Vorfassung
p18).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Python | eine stille Null-Schleife, die wie ein leerer Container aussieht |
| B | `for-in` nimmt nur `Iterable<T>`; `Iterator<T> :: [Iterable<T>]` mit `iter(): this` | **Rust `IntoIterator`** (`Iterator: IntoIterator<IntoIter=Self>`) | dasselbe Verhalten, aber die Regel steht im Typ statt im Sonderfall |
| C | Warnung, wenn derselbe `Iterator`-Wert zweimal in einem Schleifenkopf steht | — | Flussanalyse für einen Spezialfall |
| D | Iteratoren im Schleifenkopf ganz verbieten | — | bricht jede Kette |

**Korrektur der Begründung.** Die Vorfassung stützte B und C auf „C#s `foreach` nimmt nur
`IEnumerable`". Das ist **falsch** — C#s `foreach` ist musterbasiert, und seit C# 9 genügt eine
Extension-`GetEnumerator()` (s. 2.2). Das Vorbild, das die Regel wirklich erzwingt, ist **Rusts
`IntoIterator`**.

**Und C ergänzt B nicht, C repariert, was B liegen lässt.** Mit `Iterator :: [Iterable]` und
`iter(): this` läuft die zweite Schleife **immer noch null mal** — B macht aus zwei Sonderfällen
einen Mechanismus, es macht die stille Null-Schleife nicht unmöglich. Nur C meldet sie. Die
Vorfassung begründete C, als sei sie ein Zusatz zu B; sie ist der einzige Teil, der (E) behebt.

**Empfehlung: B + C**, mit dieser Begründung. C ist billig: sie muss nur „derselbe lokale `let`
steht zweimal in einem Schleifenkopf ohne Neuzuweisung dazwischen" sehen.

**Bricht:** nein. **Hängt an:** COL-08. **Konfidenz:** gemessen + gelesen.

---

### COL-12 — Wo wohnen die Adapter: Default-Methoden oder Extensions?

**Heute.** 27 Default-Methoden auf `Iterator<T>` (`iter.lyr:21-285`, gezählt: `next` + 13
Adapter + 14 Terminatoren = 28 Member). Gemessen (1.2 J): 2 973 B → 17 760 B, sobald ein
`Iterator<T>`-Wert im Modul steht. **Die Ursache ist gemessen:** 22 nicht-generische Defaults
brauchen je einen Slot, jeder Slot muss gefüllt werden, also wird der Rückgabetyp jedes
nicht-generischen Adapters erreichbar — und das für **jede** `Iterator`-Instanz der Kompilation
(`int`, `float`, `string`), nicht für die benutzte.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Rust (provided methods) | ~15 KB in jedem Modul mit einem Iterator-Wert |
| B | Adapter als **Extensions auf dem Interface** | Kotlin, C# LINQ, Swift Protocol Extensions | verlangt „Extension auf einem Interface" (gibt es nicht); dafür bezahlt nur, wer benutzt |
| C | A, aber Instanziierung faul machen | Rust | **löst es nicht** — das Problem ist, dass ein Slot *existiert*, nicht dass er materialisiert wird |
| D | Adapter als freie Funktionen zurück | Status quo vor 2.17 | 2.17 war genau die Gegenbewegung |

**Empfehlung: B.** Die Vorfassung sagte „B, bis dahin C". **C fällt** — die Messung in 1.2 J
zeigt, dass faule Materialisierung an der Ursache vorbeigeht. B löst drei Dinge: die Größe, die
Constraint-Frage (`sum` für `T :: [Add]` wird eine bedingte Extension) und die
Monomorphisierungsgrenze bei `enumerate` (eine generische Extension hat keinen Slot).

**Das ist spec-first, nicht Bibliothek.** Die Schranke steht normativ in
`spec/05-interfaces.md:99-104` und nennt `fn chunks(): Iterator<T[]>` beim Namen. Jede
Verschiebung der Adapter in Extensions braucht dort einen Satz. Die Vorfassung führte COL-12 als
Bibliotheksfrage und nannte in §5 nur „Interface-Gebiet".

**Bricht:** nein für Aufrufer. **Hängt an:** Interface-Gebiet (Extensions auf Interfaces;
bedingte Konformanz = `PLAN.md:259`, Position 3), **§5.2a der Spec**.
**Konfidenz:** gemessen (Ursache) + gelesen (Spec).

---

### COL-13 — Collection-Literale für eigene Typen

**Heute.** `[…]` baut ausschließlich ein Array; `let l: List<int> = [1,2,3];` ist `LYR-SEM0001`.
Ein Map- oder Set-Literal gibt es nicht. `lyric-v5-features.md:43` (Posten 11).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Go | `List<int>.of([1,2,3])` — eine Kopie mehr, aber ehrlich |
| B | `FromLiteral`-Interface: der Kontexttyp wählt | **C# 12**, Swift `ExpressibleByArrayLiteral` | ein Literal hat keinen Typ aus sich heraus; ohne Kontext bleibt es Array |
| B' | B, aber über die **Add-Route**: parameterloser Konstruktor + `push` | **C# 12, die vierte Route** | bedient `List<T>` und `Set<T>` ohne statische Interface-Member |
| C | B, plus `{k: v}` für Maps | Swift, Python | `{` am Statement-Anfang kollidiert mit Block und Struct-Init (`Grammar.md:428`) |
| D | Makros | Rust `vec![]` | allgemeines Makrosystem ausdrücklich draußen (`PLAN.md:409-410`) |

**Korrektur.** Die Vorfassung nahm `CollectionBuilder` als Vorlage — das ist die **schmalste**
der vier C#-Routen und der Opt-in für unveränderliche Typen. Die Route, die `List<T>` in Lyric
sofort bedienen würde, ist Konstruktor + `Add` (hier: `push`). Sie ist als B' aufgenommen.

**Empfehlung: B' zuerst, B als Verallgemeinerung, ohne C.** B' braucht **keine** statischen
Interface-Member und ist damit nicht an `PLAN.md:260` (Position 4) gekettet — das war der
teuerste Abhängigkeitsposten der Vorfassung. `{k: v}` bleibt draußen, weil die Grammatik an
dieser Stelle schon einmal kämpft.

**Konflikt, den die Vorfassung verschwieg:** `lyric-v5-features.md:43` fordert **wörtlich**
`let m: Map<string,int> = {"a": 1}` **und** `let s: Set<int> = [1, 2]`. Die Empfehlung „ohne C"
ist die Ablehnung der Map-Form. Steht jetzt in §5.

**Bricht:** nein. **Hängt an:** B' an nichts; B an statischen Interface-Membern
(`PLAN.md:260`). **Konfidenz:** gelesen.

---

### COL-14 — Die Array-Oberfläche der Standardbibliothek

**Heute.** Vier freie Funktionen in `collections.lyr`, vier weitere über `iter.lyr` und
`option.lyr` (1.1). Kein `contains`, `indexOf`, `reverse`, `fill`, `copy`, `binarySearch`, kein
`sortArrayBy`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Status quo | jede Array-Operation kostet eine Materialisierung |
| B | ein Array-Paket (`std.array`) mit ~15 freien Funktionen | Go `slices`, .NET `Array` | Bibliotheksarbeit, kein Sprachrisiko |
| C | B als **Extensions**, damit `xs.indexOf(v)` geht | Kotlin, C# | **braucht zwei Sprachänderungen**, s. u. |

**Empfehlung: B, mit C als Ziel — nicht umgekehrt.** Die Vorfassung empfahl C mit B als
Rückfall und berief sich darauf, dass „`extend string` funktioniert". Die Frage, warum
`extend string` wirkt und `extend int[]` nicht, hat sie nicht gestellt. **Sie ist jetzt
beantwortet** (1.2 N): `string` ist ein benanntes Builtin-**Symbol** mit Member-Tabelle
(`TypeChecker.cs:643-644`), `int[]` ist ein **struktureller Typ ohne Träger**, und
`LYR-SEM0047` kann für ihn gar nicht feuern (`TypeChecker.cs:629-638`). C verlangt damit:

1. ein Symbol oder eine Member-Tabelle für strukturelle Typen — ein Typsystem-Eingriff, kein
   Bibliotheksposten;
2. Typparameter an `ExtendDecl` (`extend<T> T[] { … }`) — die Produktion hat keine
   (`docs/Grammar.md:297`, gelesen).

Ohne (1) ist C nicht machbar, und (1) ist deutlich teurer, als die Vorfassung angenommen hat.
**B ist die Form, die sicher kommt.**

**Mindestumfang in jedem Fall:** `contains`, `indexOf`, `reversed`, `fill`, `copyInto`,
`binarySearch`, `sortBy`, `sortByKey`, `first`/`last`, `isEmpty`. **Und `collect`/`toArray`/
`collectArray` brauchen EINEN Namen mit EINER Komplexität** — heute sind es drei Namen und zwei
Komplexitäten (1.2 L), und der quadratische Weg heißt ausgerechnet `collectArray`.

**Unabhängiger 4.x-Posten:** `extend int[]` und `extend (int,int)` müssen `LYR-SEM0047` melden
statt zu schweigen (gemessen, r14/r16b).

**Bricht:** nein. **Hängt an:** bedingte Konformanz (`contains` braucht `Equatable`), COL-39
(Ordnungsvertrag für `binarySearch`), COL-12. **Konfidenz:** gemessen + gelesen.

---

### COL-15 — `[e] * n` mit einem geteilten Element

**Heute.** `[e] * n` wertet `e` einmal aus, kopiert es einmal und lässt **alle n Slots auf diese
eine Kopie zeigen** — **auch für Structs** (gemessen mit vier Kontrollen, r01/r25). Die
Vorfassung behauptete das Gegenteil („Structs haben Wertsemantik und dürfen weiter — die Falle
betrifft `class` und `T[]`"). Das war falsch und hat die Empfehlung getragen.
`arrayFilled(n, value)` **ist** `[value] * n` (`collections.lyr:1283-1288`, gemessen r01: `9 9`).
`spec/06-operators.md` kennt das Wort „array" nicht (gelesen, `grep` über die Datei: null
Treffer) — die Semantik von `+` und `*` auf Arrays steht **nirgends**, s. COL-34.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen, dokumentieren | Python (identische Falle) | die Falle bleibt |
| B | Warnung, wenn das Element **irgendein** zusammengesetzter Typ ist (Struct, Klasse, Array) | — | trifft `arrayFilled` **nicht**, weil die Schreibstelle in der stdlib liegt |
| C | `*` verbieten für alles außer Skalaren; `arrayOf(n, f)` ist der Weg | Rust (`[x; n]` verlangt `Copy`) | Bruch; `arrayFilled` muss auf `arrayOf` umgebaut werden |
| D | `*` kopiert je Slot | — | `n` Kopien statt einer; für Klassen ist „kopieren" nicht definiert |
| E | **`*` ganz streichen**, `arrayOf(n, f)` und `arrayFilled(n, x)` sind die Bauformen | — | `[0] * 3` verschwindet aus jedem Beispiel und aus `spec/03-types.md:73` |

**Empfehlung: C, und `arrayFilled` im selben Zug auf `arrayOf` umbauen.** Die alte Empfehlung C
(„Structs dürfen weiter") ließ den Bug genau dort stehen, wo er gemessen auftritt. Die richtige
Frage war die, die die Vorfassung nicht stellte: **ist `*` überhaupt die richtige Bauform?**

E ist die ehrlichere Antwort und wäre meine, wenn `[0] * n` nicht in `spec/03-types.md:73` als
**die** Bauform benannt stünde. C hält die Form für Skalare, wo sie unproblematisch und lesbar
ist, und schneidet genau den Fall weg, der still falsch ist.

**Für D gilt:** „kopiert je Slot" ist für Structs wohldefiniert und für Klassen nicht — es gäbe
kein `Clone`. D wäre also zwei Regeln in einem Operator.

**Bricht:** minor. **4.x-Uhr:** 4.7 warnt **an der Schreibstelle UND in `arrayFilled`** (die
Warnung dort muss an der Aufrufstelle landen, nicht an der Deklaration — sonst ist sie
wirkungslos), 5.0 verbietet. **Hängt an:** COL-33 (Teilungsregel), COL-34 (wo die Semantik
steht), Typsystem-Gebiet (`mut struct`, `PLAN.md:375`: „läuft seit 4.6").
**Konfidenz:** gemessen.

---

### COL-16 — Mutation während der Iteration

**Heute.** Undefiniert und still (gemessen, 1.2 F). Spec sagt nichts; das stdlib-Doc sagt für
`Set` „Iteration order is UNSPECIFIED" (`collections.lyr:907-908`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Scala | still falsche Programme |
| B | spezifizieren als „undefiniert, aber speichersicher" | **Go, richtig gelesen**: Go sagt für Slices, dass der Range-Ausdruck einmal ausgewertet wird, und für Maps genau drei Regeln | wenigstens steht es dann da — **und Go zeigt, wie genau man werden kann** |
| C | Modifikationszähler, Panik bei Veränderung | Java, C#, Python (dict) | ein `int` pro Container, ein Vergleich pro `next` |
| D | Snapshot-Iteration | Java `CopyOnWrite` | O(n) Speicher pro Schleife |

**Korrektur.** Die Vorfassung führte Go als Beleg für „undefiniert, aber speichersicher". Go ist
das **Gegenteil**: es spricht den Spielraum aus und begrenzt ihn. Damit ist B stärker, als die
Vorfassung annahm — B heißt nicht „wir schreiben Undefiniertheit hin", sondern „wir schreiben
hin, was gilt und was nicht".

**Empfehlung: C für die std-Container, B als Spec-Satz nach Gos Vorbild für eigene.** Der Preis
ist ein Feldvergleich pro `next`; in einer Sprache mit einem `callvirt` pro Element nicht
messbar.

**Bricht:** nein (was still schiefging, panikt dann). **Hängt an:** stdlib-Gebiet, COL-28.
**Konfidenz:** gemessen + gelesen.

---

### COL-17 — `xs[i]++`, und Compound auf Elementen

**Heute.** `xs[i] += 1` wertet den Index einmal aus — **normativ zugesagt** in
`spec/06-operators.md:101-102` (gelesen) und gemessen. `xs[i]++` ist `LYR-IR0001`.
Interface-gestütztes `+=` auf einem Element ist `LYR-SEM0003`, begründet mit „the shorthand
would evaluate the object or the index twice" (`spec/06-operators.md:104-108`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | drei Regeln für eine Klammer, keine im Guide |
| B | `xs[i]++` erlauben | C#, Java, Go, Kotlin | `LowerIncDec` muss lvalues können — dieselbe Arbeit, die `p.x++` braucht |
| C | `++`/`--` ganz streichen | Python, Rust, Swift (ab 3.0) | Bruch; kollidiert mit `SPEC-RUNDE` 1 |
| D | B, plus interface-gestütztes Compound auf Elementen (Empfänger und Index in Temps) | Kotlin (`plusAssign`) | die Spec-Begründung fällt — sie ist schon gefallen |

**Empfehlung: B + D.** **Und der Beleg ist spec-intern, nicht messtechnisch.**
`spec/06-operators.md:101-102` sagt „The target is evaluated ONCE … the same promise `xs[i] += 1`
makes", und **drei Zeilen später** begründet 104-108 `LYR-SEM0003` mit „would evaluate the object
or the index twice". Das Kapitel widerspricht sich selbst. Die Vorfassung zitierte nur die
zweite Hälfte und präsentierte ihre Messung als das entscheidende Gegenargument — sie ist eine
Bestätigung, nicht der Beleg. **Der Widerspruch muss spec-first aufgelöst werden, bevor D
gebaut wird.**

**Bricht:** nein. **Hängt an:** Operator-Gebiet (`SPEC-RUNDE` 1). **Konfidenz:** gelesen +
gemessen.

---

### COL-18 — Mehrdimensionale Arrays

**Heute.** Nur jagged: `int[][]` ist ein Array von Array-Referenzen. Die Zeilen können
verschieden lang sein, und `[[0]*3]*2` teilt sie (COL-15).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Go, Rust, Java, Kotlin | eine Indirektion pro Dimension |
| B | echtes `int[,]` mit gemeinsamer Ablage | C#, Fortran, numpy | zweiter Array-Typ → Rule 2; braucht mehrstellige Indexer |
| C | A, plus eine `Grid<T>`-Bibliotheksklasse über einem flachen Array | Rust `ndarray`, Zig | reine Bibliothek; verlangt COL-07 B für `g[x, y]` |

**Empfehlung: A, C wenn jemand es braucht.** C#s `int[,]` ist in C# selbst der langsamere Weg.

**Bricht:** nein. **Hängt an:** COL-07. **Konfidenz:** gelesen.

---

### COL-19 — `params` leitet weiter, ohne zu kopieren

**Heute.** `fn poke(params xs: int[])`, aufgerufen mit einem Array, schreibt in das Array des
Aufrufers; mit Spread-Argumenten bekommt der Callee ein frisches (gemessen, r20: `99` gegen `1`).
Die Spec beschreibt die Auswahl (`spec/07-statements.md:43-45`), nicht das **Aliasing**.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen, aber hinschreiben | C# (identisch, identisch dokumentiert) | der Callee weiß nicht, ob er ein eigenes Array hat |
| B | immer kopieren | — | O(n) auch dort, wo heute nichts kopiert wird |
| C | `params` impliziert unveränderliche Elemente | — | verlangt eine Unveränderlichkeitsregel für Arrays, die es nicht gibt |

**Empfehlung: A** — aber **nur als Teil von COL-33**. Isoliert ist A eine Dokumentationsaufgabe;
zusammen mit COL-02 (Kopie) und `[first, ..rest]` (Kopie) ist es die dritte von drei Antworten
auf dieselbe Frage.

**Bricht:** nein. **Hängt an:** COL-33. **Konfidenz:** gemessen.

---

### COL-20 — Gleichheit, Ordnung, Darstellung von Arrays

**Heute.** `xs == ys` ist `LYR-SEM0059` (gemessen, r06), `f"{xs}"` ist `LYR-SEM0006`
(`docs/guide/02-values-and-types.md:103`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Go (`slices.Equal`) | jede Testassertion über Arrays ist eine Schleife |
| B | `Equatable`/`Display`/`Hashable` bedingt auf `T[]` | Rust, C#, Kotlin | verlangt bedingte Konformanz (`PLAN.md:259`) |
| C | freie Funktionen `arrayEquals`, `showArray` | Go | ein zweiter Aufrufstil neben `==` |

**Empfehlung: B für `Equatable` und `Display`, `Hashable` NUR mit COL-30.** Die Vorfassung
reichte `Hashable` mit durch. Das ist die Frage, ob ein `T[]` ein Map-Schlüssel sein darf, und
sie hat eine eigene Antwort — s. COL-30. Nebenbefund, gemessen: `int[]` erfüllt die Schranke
`K :: [Hashable<K>]` **heute schon** und scheitert erst beim Lowering (`LYR-IR0001`, r07c).

**Bricht:** nein. **Hängt an:** bedingte Konformanz, COL-29, COL-30.
**Konfidenz:** gemessen + gelesen.

---

### COL-21 — Index und Element zugleich (`enumerate`)

**Heute.** Nur als freie Funktion `enumerate(over(xs))` plus Tupel-Destructuring im Kopf
(gemessen, r09). Als Methode unmöglich — **normativ**, `spec/05-interfaces.md:99-104`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | drei Aufrufe für den häufigsten Schleifenkopf der Welt |
| B | zweiter Capture im Schleifenkopf: `for (x, i in xs)` | **Zig** | neue Syntax im `ForInStmt`; kein neuer Typ, kein Interface, keine Monomorphisierungsfrage |
| C | `enumerate` als generische Extension, sobald COL-12 B steht | Rust, Kotlin `withIndex` | löst sich mit COL-12 |
| D | assoziierte Typen | Swift, Rust | großes Typsystem-Feature |

**Empfehlung: C, B nur wenn C nicht kommt.** Unverändert — **aber es ist spec-first**: jede
dieser Antworten braucht einen Satz in `spec/05-interfaces.md:99-104`, weil die Schranke dort
steht und `chunks` dort beim Namen genannt wird. Die Vorfassung schrieb die Schranke einem
Quelltextkommentar zu.

**Und B ist weniger neu, als die Vorfassung dachte:** die Grammatik erlaubt im Schleifenkopf
bereits `TuplePattern` (`docs/Grammar.md:382`, gelesen), und `for ((i, v) in …)` läuft
(gemessen, r09). B ist eine Erweiterung der vorhandenen Destrukturierung, keine fremde Form.
S. COL-36.

**Bricht:** nein. **Hängt an:** COL-12, COL-36, §5.2a. **Konfidenz:** gemessen + gelesen.

---

### COL-22 — Was `for-in` überhaupt annimmt

**Heute.** Fünf Formen, drei davon Compiler-Sonderfälle (`FunctionLowerer.cs:1228-1340`). `Map`
ist nicht dabei, `Set` schon (`collections.lyr:910`), `Deque` absichtlich nicht
(`docs/guide/13-standard-library.md:126`: „without iteration: a queue is drained, not walked").
`Coroutine<T>` ist nicht dabei (gemessen, r04). `?T[]` ist abgelehnt.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | die Liste der Sonderfälle wächst mit jedem Builtin |
| B | Sonderfälle auf **einen** reduzieren: alles über `Iterable`, Builtins bekommen eine eingebaute Konformanz | Kotlin, Swift, Scala | verlangt Konformanzen auf Builtin-Typen — und **`extend int[]` schweigt heute** (1.2 N) |
| C | `Map :: [Iterable<(K,V)>]` ergänzen | Python, Go, C#, Kotlin, Rust | ein Standardweg statt drei benannten |
| D | A, aber die Sonderfälle in der Spec benennen | — | ehrlich, ändert nichts |

**Empfehlung: B + C — mit einem ausgesprochenen Konflikt zu COL-09.** COL-09 B will den
Array-Zweig durch eine **Indexschleife** ersetzen (schneller, aber ein Sonderfall mehr im Geiste);
COL-22 B will alle Zweige auf `Iterable` reduzieren (ein Mechanismus, aber der langsame). Beide
sind gut begründet und **sie ziehen gegeneinander**. Die Vorfassung führte sie nebeneinander,
ohne das zu sagen.

Auflösung, die ich vorschlage: **B als Sprachregel, COL-09 B als Lowering-Optimierung auf dem
einen Pfad.** Die Sprache kennt dann nur `Iterable`; der Compiler darf `for (x in array)` zur
Indexschleife falten, weil er die Konformanz selbst gebaut hat. Das ist genau das, was Kotlin
mit `IntRange` macht.

C braucht eine Entscheidung, die die Vorfassung nicht traf: **welche Sicht ist die Standardsicht,
und in welcher Reihenfolge?** S. COL-28.

**Bricht:** nein (additiv). **Hängt an:** COL-08, COL-09 (Konflikt), COL-12, COL-28, COL-26.
**Konfidenz:** gemessen + gelesen.

---

### COL-23 — Ist ein Index außerhalb der Grenzen eine Panik oder ein fangbarer Fehler?

**Heute, gemessen (r02, r02c).** `xs[5]` auf `[1,2,3]` ist
`panic [LYR-VM0006]: index 5 is outside an array of length 3`, Exit 101, **nicht fangbar**. Und:
ein `try { xs[i] } catch (e: Throwable)` **kompiliert ohne Diagnose** und fängt trotzdem nichts —
das Programm panikt durch den Catch hindurch. Die Sprache trennt Panik und Exception
ausdrücklich (`docs/guide/10-errors.md:5-7`, gelesen), aber ein `catch`, das für einen Panikpfad
nie feuern kann, wird nicht bemerkt. Einen nicht-panischen Zugriff gibt es nicht:
`Indexable.get(index: int): T` (`collections.lyr:19-24`) kann „fehlt" nicht ausdrücken; das
einzige `getOr` der std sitzt auf `Map` (`collections.lyr:678`).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Swift (Trap), Rust (`panic!`) | kein Weg, einen Zugriff abzusichern, außer die Länge vorher zu prüfen |
| B | A, plus `Index<K,V>` bekommt `getOrNull(k): ?V` neben `get(k): V` | **Rust** (`get() -> Option<T>` neben `[]`) | ein zweiter Name pro Zugriff — aber beide sagen im Namen, was sie tun |
| C | `[]` darf werfen, typed `throws IndexError` | C# (fangbare `IndexOutOfRangeException`) | jeder Zugriff wird zu einer `throws`-Stelle; jede Schleife braucht `try` oder `throws` |
| D | A, aber der Compiler meldet ein `catch`, das nur Paniken umschließt | — | Flussanalyse; behebt nicht die Absicherung, nur die Illusion |

**Empfehlung: B + D.** B ist Rusts Aufteilung und passt exakt zu Lyrics eigener Doktrin „ein
Wert antwortet *ob*, ein Wurf antwortet *warum nicht*" (`docs/guide/10-errors.md:11-16`,
gelesen): „ist da was?" ist `?T`, also gehört der absichernde Zugriff zu `?T` und nicht zu
`throws`. C widerspricht dieser Doktrin direkt und macht jede Indexschleife zu einem
`throws`-Kontext.

D ist ein billiger, eigenständiger 4.x-Posten: ein `catch`, dessen `try`-Block keinen
`throws`-Aufruf enthält, ist heute **stumm** (gemessen) und sollte eine Warnung sein.

**Bricht:** nein (B ist additiv). **Hängt an:** COL-07 B (die Schnittstelle, in die `getOrNull`
gehört), Optional-Gebiet. **Konfidenz:** gemessen.

---

### COL-24 — Ist `T[]` invariant, und was ist das Idiom für „ein Array von irgendetwas, das X kann"?

**Heute, gemessen (r03, r03b).** Invariant: `let ss: Shape[] = cs;` mit `cs: Circle[]` und
`Circle :: [Shape]` ist `LYR-SEM0001: cannot assign 'Circle[]' to 'Shape[]'`. Das ist **sicher**
— kein Java-`ArrayStoreException`. Zwei Kontrollen im selben Programm laufen: ein direkt als
`Shape[]` gebautes Array, und eine generische Signatur `fn showAllG<T :: [Shape]>(xs: T[])`
nimmt `Circle[]` an. Folge: **`fn showAll(xs: Shape[])` ist mit keinem konkreten Array
aufrufbar.**

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | invariant lassen; generische Signaturen sind das Idiom | Rust (`&[T]` invariant, `impl Trait`-Parameter) | jede Bibliotheksfunktion über „irgendwas mit X" wird generisch — mehr Monomorphisierung |
| B | Kovarianz für Lesezugriffe, `readonly T[]` als eigener Typ | C# (`IReadOnlyList<out T>`), Kotlin (`Array<out T>`) | zweiter Array-Typ → Rule 2; und `T[]` bleibt invariant, also zwei Regeln |
| C | Kovarianz ohne Schranke | **Java** (`Object[] o = strArr`) | Laufzeitprüfung bei jedem Schreiben, `ArrayStoreException` — Javas anerkannter Konstruktionsfehler |
| D | Varianz-Annotationen am Typsystem (`Array<out T>`) | Kotlin, Scala | großes Generics-Feature |

**Empfehlung: A, und es explizit hinschreiben.** Die heutige Antwort ist richtig; sie ist nur
nirgends als Antwort formuliert. Der Satz, der fehlt: **„Ein Array ist invariant. Eine Funktion
über eine Fähigkeit nimmt `<T :: [X]>(xs: T[])`, nicht `X[]`."** Das gehört in §3.3 und in den
Guide. B ist erst interessant, wenn COL-02 ohnehin einen zweiten Sequenztyp bringt — tut es
nach der Empfehlung nicht.

**Bricht:** nein. **Hängt an:** Generics-Gebiet (D), COL-02 (B). **Konfidenz:** gemessen.

---

### COL-25 — Muss ein Iterator freigegeben werden, wenn die Schleife früh `break`t?

**Heute, gemessen (r12).** Ein `break` lässt den Iterator **an seiner Stelle stehen**: nach
`for (v in it) { if (v == 3) break; }` liefert eine zweite Schleife über `it` noch `4 5`. Es gibt
kein `close()`, kein `Disposable`, und das Protokoll aus `next(): ?T` (`iter.lyr:21-25`) hat
keinen Platz für ein Ende von außen. Für `over(xs)` ist das harmlos. Für einen Zeilen-, Socket-
oder Verzeichnis-Iterator ist es ein gehaltenes Handle — und Lyric verkauft sich über
Capabilities und hat `std.io.file`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Rust (`Drop` erledigt es — hat Lyric nicht) | ein Handle bleibt offen, bis der GC läuft; der Zeitpunkt ist unbestimmt |
| B | `Iterator` bekommt `fn close(): void {}` mit leerem Default; `for-in` ruft es bei jedem Verlassen | **C#** (`IEnumerator : IDisposable`, `foreach` ruft `Dispose` garantiert) | ein Slot mehr am Interface — und der kostet nach 1.2 J gemessen etwas; `break`/`return`/Wurf müssen ihn alle treffen |
| C | ein eigenes `CloseableIterator`-Interface | Python (`gen.close()`) | zwei Iterator-Interfaces → Rule 2 |
| D | die Frage der Bibliothek überlassen: ein Datei-Iterator materialisiert vorher | Go 1.23 hat für `range`-over-func eine **definierte Stop-Semantik** | keine Streams über große Dateien |

**Empfehlung: B, aber erst nach COL-12 B.** Solange die Adapter Default-Methoden mit Slots sind,
kostet jeder weitere Slot messbar (1.2 J). Als Extension-Welt ist `close()` der einzige
Kernmember neben `next()`, und `for-in` kann ihn garantiert aufrufen — genau C#s Konstruktion.
D ist die ehrliche Zwischenlösung und heute die faktische: `std.io.file` liest ganze Dateien.

**Bricht:** nein (leerer Default). **Hängt an:** COL-12, COL-10, stdlib/IO-Gebiet.
**Konfidenz:** gemessen.

---

### COL-26 — Soll `Coroutine<T>` ein `Iterator<T>` sein?

**Heute, gemessen (r04, r04b).** Nein. `for (x in co)` mit `co: Coroutine<int>` ist
`LYR-SEM0007: 'Coroutine<int>' is not iterable`, und `let it: Iterator<int> = co;` ist
`LYR-SEM0001`. **Obwohl die Formen identisch sind** — der Guide sagt es selbst:
„`co.next()` … answers `?T` — the value, or `null` once the body has run to its end. … **The name
and shape are `Iterator<T>.next()`'s on purpose.**" (`docs/guide/11-coroutines.md:85-88`,
gelesen). Koroutinen sind seit 4.0 stackful, und ein `Coroutine<?T>` ist bereits
`LYR-SEM0080` — dieselbe Sackgasse wie `LYR-SEM0091`, dieselbe Ursache.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | Lyric hat zwei identische Protokolle und verbindet sie nicht |
| B | `Coroutine<T> :: [Iterator<T>]` eingebaut | **C# (`yield return` ⇒ `IEnumerator<T>`), Python (Generator ist Iterator), Kotlin (`sequence { yield }`)** | `Coroutine<void>`/`Coroutine<?T>` müssen ausgenommen werden (`next()` hat dort eine andere Form) |
| C | B, plus `Coroutine<T> :: [Iterable<T>]`? | — | **nein** — eine Koroutine ist ein Cursor, kein Container; `iter()` könnte keinen frischen liefern |
| D | eine `Iterator`-Implementierung in der std, die eine Koroutine hält | Status quo bauen könnte man | eine Klasse, die `next()` an `next()` weiterreicht — Rule 2 in Reinform |

**Empfehlung: B.** Das ist in C#, Python und Kotlin **der** Weg, von Hand geschriebene
`next()`-Implementierungen zu vermeiden, und er ist hier fast umsonst: die Signatur stimmt
bereits, absichtlich. Was er löst:

- **COL-12s 15-KB-Problem an der Wurzel**: wer heute einen eigenen Iterator baut, zahlt die
  Interface-Wert-Kosten. Mit B schreibt er eine Koroutine und zahlt sie nicht — der `for-in` über
  eine Koroutine kann ein eigener Lowering-Pfad sein.
- **COL-21**: `enumerate` als Koroutine ist drei Zeilen und braucht keine Monomorphisierung über
  wechselnde Elementtypen.
- **COL-10**: das Hauptargument gegen Push-Iteration („Ketten brauchen Koroutinen an jeder
  Kreuzung") ist entkräftet, wenn Koroutinen erstklassig in der Kette stehen.

**Das Wort „Koroutine" kommt in der Vorfassung nicht ein einziges Mal vor.** Das war ihr größtes
Loch: das Gebiet diskutiert Iteration über 22 Fragen und übersieht, dass die Sprache einen
zweiten, vollständigen Iterationsmechanismus hat.

**Bricht:** nein (additiv). **Hängt an:** Koroutinen-Gebiet (`spec/10-coroutines.md`), COL-10,
COL-12, COL-22. **Konfidenz:** gemessen + gelesen.

---

### COL-27 — Was tut eine Kette mit einer werfenden Funktion?

**Heute, gemessen (r11, r11b).** Gar nichts — es geht nicht.
`over(xs).map(parse)` mit `fn parse(s: string): int throws Exception` ist
`LYR-SEM0037: 'parse' declares 'throws' and cannot be used as a value — function types carry no
throws information; call it directly`. Kontrolle mit einer nicht werfenden Funktion: läuft. Die
Reparatur ist bereits geplant: **typed throws Stufe 3, „werfende Funktionstypen und Lambdas"**,
`PLAN.md:263` (Position 7), abhängig von Stufe 2, die von Stufe 1 abhängt.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | jede fehlbare Umwandlung verlässt die Kette; `map(parse)` ist nie schreibbar |
| B | werfende Funktionstypen, die Kette propagiert `E` bis zum Terminator | **Swift** (`try map`/`rethrows`), Java (`Stream` + checked exceptions: das anerkannte Ärgernis) | die Ausnahme wird beim **Terminator** geworfen, an einer Stelle, die die Quelle nicht mehr nennt — die bekannte Härte lazy Ketten |
| C | werfende Funktionen nur in **eager** Terminatoren (`forEach`, `fold`), nicht in lazy Adaptern | Kotlin (`Sequence` lässt es zu, aber die Ausnahme kommt beim Terminator — dieselbe Falle) | zwei Klassen von Adaptern; der Benutzer muss wissen, welche |
| D | `map` über `?U` statt `throws`: `mapNotNull`, und der Grund geht verloren | Rust-Stil ohne `?` | passt zur „ob/warum"-Doktrin, verliert aber den Grund |

**Empfehlung: B, und das Stack-Trace-Problem beim Namen nennen.** Lyrics Doktrin sagt: wo ein
Grund zählt, wird geworfen (`docs/guide/10-errors.md:11-16`). Eine Kette darf davon keine
Ausnahme sein. Aber der Preis ist real: die Ausnahme aus `map(parse)` entsteht im Terminator,
und ein Backtrace, der `count()` zeigt, hilft niemandem. **Wenn B kommt, braucht die Diagnose
einen Hinweis auf den Adapter, aus dem sie kam** — das ist ein Posten für das Fehler-Gebiet, kein
Nebensatz.

**Und COL-12 verschiebt die Frage:** wandern die Adapter in Extensions, steht die
`throws`-Klausel an der Extension, nicht am Interface-Member. Die Reihenfolge ist also:
typed throws St. 1–3 → COL-12 → COL-27.

**Bricht:** nein. **Hängt an:** typed throws St. 3 (`PLAN.md:263`), COL-12, Fehler-Gebiet.
**Konfidenz:** gemessen + gelesen.

---

### COL-28 — Ist die Iterationsreihenfolge von `Map` und `Set` festgelegt?

**Heute, gemessen (r05).** Hash-Reihenfolge, **deterministisch über Läufe**, **nicht**
Einfügereihenfolge:

```
Set<int>, eingefügt 50 3 17 99 1 64 8   ->  64 17 50 3 99 1 8    (beide Läufe identisch)
Map<string,int>, eingefügt zeta alpha mu beta  ->  mu beta alpha zeta
Kontrolle: dasselbe Array  ->  50 3 17 99 1 64 8
```

Das stdlib-Doc sagt „Iteration order is UNSPECIFIED" (`collections.lyr:907-908`, gelesen) — die
**Spec** sagt nichts. Und `Set<T> :: [Iterable<T>]` steht seit je (`collections.lyr:910`), also
**können Programme heute schon von der Hash-Reihenfolge abhängen**, und nichts hält sie davon ab.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | unspezifiziert lassen, aber **in die Spec** schreiben | **Go, richtig gelesen**: Reihenfolge unspezifiziert, Regeln zur Mutation ausbuchstabiert | Programme, die heute darauf bauen, sind ab dann nachweislich falsch — heute sind sie nur unglücklich |
| B | Einfügereihenfolge zusichern | **Python 3.7+** (`dict` ordnet), PHP, JS `Map` | eine zusätzliche Verkettungsliste pro Container; jede Löschung wird teurer |
| C | absichtlich randomisieren, damit niemand sich verlässt | **Go** (Map-Iteration startet an zufälliger Stelle), Rust (`RandomState`) | nicht reproduzierbare Testausgaben; braucht eine Zufallsquelle, die das Sandbox-Modell sehen würde |
| D | A, plus eine `LinkedMap`/`LinkedSet` für den Fall, dass jemand Ordnung braucht | Java (`LinkedHashMap`) | zwei Container pro Konzept |

**Empfehlung: A + D.** A ist das Minimum und heute überfällig, weil `Set` bereits iterierbar ist
— die Zusage „unspezifiziert" kommt für `Set` nicht zu spät, sie ist nur bisher nur im
Doc-Kommentar gelandet und nicht in der Spec. **C lehne ich ab**: eine Sprache, deren zweites
Produkt die Einbettung ist, sollte keine Zufallsquelle in die Iterationsreihenfolge einbauen.
D ist reine Bibliothek und kommt, wenn jemand fragt.

**Und COL-22 C hängt daran:** `Map :: [Iterable<(K,V)>]` braucht diesen Satz, bevor es gebaut
wird.

**Bricht:** nein. **Hängt an:** COL-16, COL-22 C, stdlib-Gebiet. **Konfidenz:** gemessen +
gelesen.

---

### COL-29 — Wie beobachtet man Aliasing überhaupt?

**Heute, gemessen (r06, r06b).** Gar nicht. `==` auf `int[]` ist
`LYR-SEM0059: '==' is not defined for 'int[]'`, und auch für eine Klasse ohne `Equatable` ist
`==` `LYR-SEM0059` — **Lyric hat keinen Referenz-Identitätsvergleich, für keinen Typ.** Ein
Programm kann heute nicht feststellen, ob zwei `T[]`-Werte dasselbe Array sind. Das ist die
Frage, die zwischen COL-15 (`*` teilt) und COL-20 (`==` soll strukturell werden) hindurchfällt.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | die Teilungsregeln aus COL-33 sind unbeobachtbar und damit untestbar |
| B | `Equatable` auf `T[]` strukturell (COL-20 B) **und sonst nichts** | Rust (`==` strukturell, `ptr::eq` für Identität — also *beides*) | `==` beantwortet dann „gleicher Inhalt"; „dasselbe Array" bleibt unbeantwortbar |
| C | ein Identitätsoperator/`isSame(a, b)` für Referenztypen | C# `ReferenceEquals`, Java `==`, Python `is`, Swift `===` | ein Begriff mehr in der Sprache — aber er benennt etwas, das es schon gibt |
| D | C, plus: strukturelles `==` auf einem Array, das sich selbst enthält, **panikt oder terminiert per Tiefenlimit** | Python (`RecursionError`), Rust (überlauft) | eine Regel für einen seltenen Fall, die man aber schreiben muss, sobald B kommt |

**Empfehlung: B + C + D.** C ist die kleinste Ergänzung mit dem größten Gewinn: sie macht die
Teilungsregel aus COL-33 **testbar**, und ohne sie kann `std.test` nicht prüfen, was COL-15 und
COL-19 behaupten. D ist kein Extra, sondern die Pflichtantwort zu B: `int[][]`, das sich selbst
enthält, ist heute baubar, und ein strukturelles `==` darüber muss eine definierte Antwort haben.

**Bricht:** nein (additiv). **Hängt an:** COL-20, COL-33, Operator-Gebiet. **Konfidenz:**
gemessen.

---

### COL-30 — Darf ein `T[]` ein `Map`-Schlüssel sein?

**Heute, gemessen (r07, r07c, r07d).** Halb — und das ist schlimmer als ein klares Nein.
`Map<int[], string>` **besteht die Constraint-Prüfung** `K :: [Hashable<K>]` und scheitert erst
im Lowering, mitten in der stdlib:

```
stdlib/std/collections.lyr:667: error[LYR-IR0001]: call to 'equals' on 'int[]'
  note: this compiler version cannot lower it yet
fn takesHashable<K :: [Hashable<K>]>(k: K) mit int[]  ->  LYR-IR0001 "call to 'hash' on 'int[]'"
Kontrollen: int als Schluessel laeuft; Map<int,string> laeuft.
```

Ein Fehler, der den Benutzer in eine stdlib-Zeile schickt, ist kein Nein, sondern ein Loch.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | klares Nein: `T[]` erfüllt `Hashable` nicht, `LYR-SEM0001` an der Aufrufstelle | **C#, Java** (Arrays hashen absichtlich per Identität, nicht strukturell) | `Map<int[], V>` braucht einen Wrapper-Struct |
| B | Ja, strukturell — wie COL-20 B es durchreicht | **Rust** (`[T; N]: Hash`, weil es ein **Wert** ist) | ein mutierter Schlüssel zerstört die Map still; Rusts Begründung gilt für Lyric **nicht**, weil `T[]` eine Referenz ist |
| C | Ja, aber per **Identität** statt Inhalt | C#/Java-Default | `[1,2]` und `[1,2]` sind verschiedene Schlüssel — fast nie das Gewollte |
| D | Ja für einen unveränderlichen Array-Typ, wenn es je einen gibt | Rust `Box<[T]>`, Java `List.of` | zweiter Sequenztyp → Rule 2 |

**Empfehlung: A, und der `LYR-IR0001` muss in 4.7 zu einem `LYR-SEM0001` an der Aufrufstelle
werden.** Rusts Ja hängt daran, dass `[T; N]` ein **Wert** ist; Lyrics `T[]` ist eine Referenz
mit schreibbaren Slots, also gilt die C#/Java-Begründung. **Das korrigiert COL-20 B**, das
`Hashable` stillschweigend mitgab.

**Bricht:** nein (heute geht es ohnehin nicht). **Hängt an:** COL-20, bedingte Konformanz.
**Konfidenz:** gemessen.

---

### COL-31 — Soll `x in xs` als Mitgliedschaftstest existieren?

**Heute, gemessen (r08).** Nein, und es ist nicht einmal parsbar: `if (2 in xs)` gibt
`LYR-PAR0002: expected an expression, got In` — `in` ist ein **Schlüsselwort-Token**, das die
Grammatik nur im `ForInStmt` kennt (`docs/Grammar.md:382`, gelesen). Der Ersatz ist
`listContains`/`Set.contains`; für `T[]` gibt es gar nichts (COL-14).
`lyric-v5-features.md:47` (Posten 16) führt „`in` über `Contains<T>`" als P3.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nie; `xs.contains(v)` ist die Form | Rust (`.contains`), Go, Java | ein Wort mehr, aber keine Grammatikfrage |
| B | `in` als Operator über `Contains<T>`, **und der Parser trennt die beiden Vorkommen am Kontext** | Python, Kotlin, C# (`is`-artig) | `for (x in xs)` und `if (x in xs)` benutzen dasselbe Token an Stellen, die der Parser unterscheiden muss — machbar, weil der Schleifenkopf eine eigene Produktion ist |
| C | ein anderes Wort für den Operator (`contains` als Infix) | Kotlin (`in` **ist** `contains`, infix) | zwei Schreibweisen für eine Sache |
| D | `in` als Operator, und `for` bekommt ein anderes Wort (`for (x of xs)`) | JS | bricht jede Schleife der Sprache |

**Empfehlung: B — aber die Entscheidung gehört hierher, nicht ins Operator-Gebiet.** Die
Vorfassung schob „`in` über `Contains`" in die Gebietstabelle zum Operator-Gebiet. Die
**Grammatikkollision** ist die Frage dieses Gebiets, weil sie entscheidet, ob `if (x in xs)` je
parsbar ist. Sie ist lösbar: `ForInStmt` ist eine eigene Produktion (`Grammar.md:382`), und ein
`in` in Ausdrucksposition steht nie an derselben Stelle. Aber sie muss **beantwortet** werden,
bevor das Operator-Gebiet `Contains<T>` entwirft — und COL-14 hängt daran, weil `contains` sonst
eine Funktion ohne Operator ist.

**Bricht:** nein (additiv). **Hängt an:** Grammatik, Operator-Gebiet (`Contains<T>`), COL-14.
**Konfidenz:** gemessen + gelesen.

---

### COL-32 — Kann der Compiler den Bounds-Check in der kanonischen Schleife entfernen?

**Heute.** Es gibt keine Bounds-Check-Elimination (gelesen: `grep -il "boundscheck|bounds
check|rangecheck"` über `src/Lyric.Frontend/` und `src/Lyric.Vm/` findet nichts). **Aber die
Frage ist bereits beantwortet, und zwar negativ** — `STATUS.md:2466-2471` (gelesen), im
dokumentierten Nein zu `unsafe`:

> „What an `unsafe` would remove — the bounds check in `ldelem`, the panic in `optget` — is a
> compare and a branch INSIDE a dispatch that costs ~23 cycles. **It buys about one percent.**"

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | nichts; das Projekt hat gemessen, dass es ~1 % bringt | die eigene Messung | die letzten ~1 % bleiben liegen |
| B | Elision für `for (i in 0..xs.length) { xs[i] }` als Sonderform | Java HotSpot, .NET RyuJIT | Analyse für einen Spezialfall, für ~1 % |
| C | A, und stattdessen COL-09 B: die Allokation und die Optional-Verpackung entfernen | Go, Zig | das ist der gemessene Faktor ~1,8, nicht 1 % |

**Empfehlung: A, und die Frage als beantwortet abhaken.** Der Kritiker hatte recht, dass die
Vorfassung die Elision nicht gestellt hat; aber die Annahme dahinter — „sie schließt den
gemessenen Faktor 2" — ist durch `STATUS.md:2466-2471` widerlegt. **Der Faktor 2 sitzt in der
Allokation und im `callvirt`, nicht im Bounds-Check.** COL-09 B ist der Hebel, COL-32 nicht.

Das macht das dokumentierte Nein zu `unsafe` **billiger** zu halten, nicht teurer: wenn der
Bounds-Check 1 % kostet und die Iteratorallokation 80 %, hat `unsafe` nie ein Argument gehabt.

**Bricht:** nein. **Hängt an:** COL-09. **Konfidenz:** gelesen (die Messung ist die des
Projekts, nicht meine — ich habe sie nicht nachgemessen).

---

### COL-33 — Was ist die EINE Regel dafür, ob ein `T[]` geteilt wird?

**Heute geben vier Stellen drei Antworten** (alle gemessen):

| Form | teilt? | Beleg |
|---|---|---|
| `f(a)` mit `fn f(params xs: int[])`, ganzes Array | **ja** — der Callee schreibt durch | r20 (`whole array: 99`) |
| `f(a[0], a[1], …)`, Spread-Form | nein — frisches Array | r20 (`spread: 1`) |
| `[first, ..rest]` im Pattern | nein — Kopie | r21 (`xs[1]=2` bei `rest[0]=99`) |
| `[e] * n`, Slots untereinander | **ja** — eine Kopie, n Verweise | r01, r25 |
| `xs[a..b]` | existiert nicht | COL-02 |

COL-02 empfiehlt Kopie, COL-19 empfiehlt „teilen, aber hinschreiben", COL-15 empfiehlt „`*` für
zusammengesetzte Elemente verbieten". **Jede entscheidet für sich.** Die Vorfassung stellte die
Frage nie.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | pro Form entscheiden und jede dokumentieren | C# (dieselbe Lage, dieselbe Dokumentation) | vier Regeln, die man sich merken muss |
| B | **eine Regel: ein `T[]`, das einen Ausdruck verlässt, ist frisch — außer der Schreiber sagt anders** | Go (Slices teilen, aber sichtbar über `cap`; Gegenbeispiel), **Swift** (COW: alles ist Wert, Teilen ist unsichtbar und ungefährlich) | `params` mit ganzem Array müsste kopieren → O(n) an jeder variadischen Aufrufstelle |
| C | B, plus ein Wort, das das Teilen benennt (`share xs` / `&xs`) | Rust (`&`), Go (Slice-Ausdruck) | ein Referenzbegriff in einer Sprache, die sonst keinen hat |
| D | Wertsemantik für `T[]` mit Copy-on-Write | **Swift**, C# `ImmutableArray` | großer Laufzeit-Eingriff (Refcount neben dem GC); löst COL-15, COL-24 B, COL-30 auf einen Schlag |

**Empfehlung: A, mit B als Zielbild und D als die Frage, die das Typsystem-Gebiet stellen muss.**
Ich empfehle A, weil B an `params` scheitert: die kopierfreie Weitergabe eines ganzen Arrays ist
das, wofür `params` da ist, und eine O(n)-Kopie an jeder `println(args)`-Stelle ist ein hoher
Preis für eine seltene Verwechslung.

**Aber A darf nicht heißen „so lassen":** die vier Regeln müssen an **einer** Stelle der Spec
stehen, nebeneinander, als Tabelle. Heute stehen zwei davon nirgends (`*` und `params`), und
genau deshalb konnte die Vorfassung das Struct-Aliasing für abwesend erklären.

D gehört zu `mut struct` (`PLAN.md:375`, „läuft seit 4.6") und ist die einzige Antwort, die alle
vier Fälle auf einmal erledigt. Sie ist zu groß für dieses Gebiet, aber sie muss im
Typsystem-Gebiet auftauchen.

**Bricht:** nein (A). **Hängt an:** COL-02, COL-15, COL-19, COL-29 (ohne Identitätsvergleich ist
keine dieser Regeln testbar), Typsystem-Gebiet. **Konfidenz:** gemessen.

---

### COL-34 — Wo steht die Semantik von `+` und `*` auf Arrays?

**Heute: nirgends.** `grep -n "array" lyric-spec/spec/06-operators.md` liefert **null Treffer**
(gelesen). §3.3 (`spec/03-types.md:73`) erwähnt `[x] * n` genau einmal, beiläufig, als Bauform
(„the value is built with `[x] * n`") — ohne zu sagen, ob das Element kopiert oder geteilt wird.
Im Compiler stehen die beiden Regeln in `TypeChecker.cs:2543-2544` (gelesen). Der Guide erwähnt
sie in Kapitel 2, ohne Semantik.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | das gemessene Struct-Aliasing ist weder Bug noch Feature, sondern **unspezifiziert** — und damit ist COL-15 keine Designfrage, sondern ein Loch |
| B | §6 bekommt einen Abschnitt „Array-Operatoren": `+` (Konkatenation, frisches Array), `*` (Wiederholung, und **was mit dem Element passiert**) | Python (dokumentiert die `*`-Falle ausdrücklich), Go (kein `+` auf Slices, dafür `append` spezifiziert) | ein Spec-Abschnitt, mehr nicht |
| C | B, plus die Regeln aus COL-33 in derselben Tabelle | — | derselbe Abschnitt, größer |

**Empfehlung: C, und zwar VOR COL-15.** Das ist die wichtigste Erkenntnis, die die Vorfassung
verpasst hat: sie behandelte das Struct-Aliasing als „kein Wort im Guide". **Das größere Loch
ist, dass das Operatorkapitel die beiden Array-Operatoren gar nicht kennt.** Eine Warnung oder
ein Verbot auf `*` (COL-15) ist spec-first nicht durchsetzbar, solange die Spec nicht sagt, was
`*` tut. Reihenfolge: COL-34 → COL-15.

**Bricht:** nein (Spec-Ergänzung, die das Vorhandene beschreibt — es sei denn, man schreibt beim
Aufschreiben etwas anderes hin, als gemessen wird; dann ist es COL-15).
**Hängt an:** COL-15, COL-33. **Konfidenz:** gelesen + gemessen.

---

### COL-35 — Soll die std zwei parallele Kettenwelten behalten?

**Heute: ja, und niemand hat es entschieden** (alles gelesen in `stdlib/std/collections.lyr`):

| eager, List-förmig | lazy, auf `Iterator<T>` |
|---|---|
| `mapList:1298` | `Iterator.map` |
| `sortList:810`, `sortListBy:819`, `sortListByKey:1314` | — |
| `groupBy:1400` | — |
| `maxBy:1331`, `minBy:1351` | `minValue`/`maxValue` (`iter.lyr:964,980`) |
| `listContains:1418` | `Iterator.any`/`find` |
| `listIndexOf:1430` | `Iterator.position` |
| `collect:375` (linear) | `toArray` / `collectArray` (quadratisch, 1.2 L) |

**Das ist Kotlins `Iterable`/`Sequence`-Spaltung**, die die Vorfassung in 2.2 selbst „eine echte
Doppelung" nennt — bereits in Lyric eingebaut. COL-12 fragt, **wo** die lazy Adapter wohnen, und
COL-14, **was dem Array fehlt**; ob die eager Hälfte existieren soll, fragt keine der beiden.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | Kotlin (und es ist dort der meistkritisierte Teil der stdlib) | zwei Namen pro Operation, und der Benutzer rät, welcher schneller ist |
| B | die eager Hälfte streichen; `List` wird über `over(l)` und `collect` bedient | Rust (`Vec` hat `.iter()`, keine eigene `map`) | jeder `mapList`-Aufruf wird zu `collect(over(l).map(f))` — länger, aber ein Mechanismus |
| C | die lazy Hälfte auf Terminatoren beschränken, eager für alles andere | Java vor 8 | dreht 2.17 um |
| D | A, aber die eager Namen mit `@Deprecated(until = "5.0")` versehen und die Regel hinschreiben: „`List`-Operationen gibt es über `over`" | — | Uhr statt Bruch |

**Empfehlung: D in 4.7, B in 5.0.** Rule 2 ist eindeutig: `mapList(l, f)` und
`collect(over(l).map(f))` sind zwei Mechanismen für ein Konzept. Und `listContains`/`keys(m)`
**stehen bereits auf der 5.0-Entfernungsliste** (`PLAN.md:376`, gelesen) — die Richtung ist also
schon eingeschlagen, nur nicht als Prinzip ausgesprochen.

`sortList*` ist die Ausnahme: Sortieren ist von Natur aus eager und braucht wahlfreien Zugriff.
Es bleibt, und es bleibt List-förmig.

**Bricht:** major (jeder eager Name verschwindet). **4.x-Uhr:** 4.7 deprecatet.
**Hängt an:** COL-12, COL-14, COL-38 (ohne `lyrfix` ist das viel Handarbeit), stdlib-Gebiet.
**Konfidenz:** gelesen.

---

### COL-36 — Darf die Schleifenvariable ein Pattern sein?

**Heute, gemessen (r09) und gelesen.** Die Grammatik erlaubt genau zwei Formen:
`ForInStmt = 'for' '(' ( IDENTIFIER | TuplePattern ) 'in' ( RangeExpr | Expr ) ')' Block .`
(`docs/Grammar.md:382`). `for ((i, v) in enumerate(over(xs)))` läuft; `for (P { x, y } in ps)`
ist `LYR-PAR0002` + `LYR-PAR0021`. M36 hat Pattern Matching gerade fertiggestellt, und COL-22 C
braucht `for ((k, v) in m)` — das geht bereits.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen: Identifier und Tupel | Status quo | `for (p in pts) { let x = p.x; … }` — eine Zeile mehr |
| B | jedes **irrefutable** Pattern (Tupel, Struct-Destrukturierung) | **Rust** (`for Point { x, y } in pts`), Swift, Python | der Schleifenkopf muss die Pattern-Produktion aufrufen; Irrefutabilität muss geprüft werden |
| C | B, plus **refutable** Patterns als stiller Filter (`for (Some(x) in opts)`) | **Python** hat es nicht; **Rust** hat dafür `while let` und lehnt es für `for` ab | ein Filter, der aussieht wie eine Bindung — die Schleife überspringt still |
| D | B, plus refutable Patterns mit einem Wort, das den Filter benennt (`for (case Some(x) in opts)`) | **Swift** (`for case .some(let x) in opts`) | ein Schlüsselwort mehr, aber der Filter steht da |

**Empfehlung: B, und C ausdrücklich ablehnen.** B ist die natürliche Fortsetzung dessen, was
M36 gebaut hat, und die Grammatik muss dafür nur `TuplePattern` durch `IrrefutablePattern`
ersetzen. **C lehne ich ab aus demselben Grund, aus dem Rust es ablehnt**: eine Schleife, die
Elemente still überspringt, sieht aus wie eine, die alle nimmt — das ist dieselbe Fehlerklasse
wie Lyrics (E). D ist die ehrliche Variante, falls jemand den Filter wirklich will; sie gehört
ins Pattern-Gebiet, nicht hierher.

**Das ändert COL-21 B:** der Zig-Capture `for (x, i in xs)` ist dann keine fremde Syntax mehr,
sondern ein Sonderfall neben einer vorhandenen Destrukturierung — was ihn billiger macht, aber
auch entbehrlicher, weil `for ((i, x) in enumerate(xs))` schon läuft.

**Bricht:** nein (additiv). **Hängt an:** Pattern-Gebiet, COL-21, COL-22 C.
**Konfidenz:** gemessen + gelesen.

---

### COL-37 — Kann ein Host-Objekt (`opaque type`) `Iterable` oder `Indexable` sein?

**Heute, gemessen (r17, r17b).** Nein, und zwar doppelt:

```
pub opaque type Handle = int;
extend Handle :: [Iterator<int>] { pub mut fn next(): ?int { … } }
for (v in h)          ->  LYR-SEM0007: 'Handle' is not iterable
Kontrolle: extend Handle { fn describe() }  ->  Aufruf ist LYR-SEM0012
```

`extend` auf einem opaken Alias hängt **überhaupt keine** Member an. Lyrics zweites Produkt ist
die Einbettung (`docs/guide/14-embedding.md`), und `opaque type` + `TypeTag.Host` ist der
Handle-Mechanismus. COL-08 schiebt die drei Schnittstellen nach `std.core`, „damit eine zweite
Implementierung von Lyric sie findet" — **fragt aber nicht, ob ein Host-Objekt sie erfüllen
kann.** Das ist genau das, was ein Host für Sammlungen aus seiner Welt braucht.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen; der Host reicht Arrays herüber | — | jede Host-Sammlung wird materialisiert, bevor das Skript sie sieht |
| B | `extend <opaque> :: [I]` wirken lassen | Rust (`impl Trait for Newtype`), Haskell `newtype` + Instanz | der opake Alias braucht eine Member-Tabelle — dasselbe Loch wie `extend int[]` (1.2 N), nur mit einem Symbol, das es schon gibt |
| C | B, plus `extern "dotnet"`-Natives dürfen `next()` sein | C# `IEnumerator` direkt einbinden | hängt an `extern "dotnet"` Stufe 2 (`PLAN.md:311`, Position 22) |
| D | ein eigener Host-Iterator-Mechanismus | — | zweiter Iterationsmechanismus → Rule 2 |

**Empfehlung: B jetzt, C mit `extern "dotnet"` Stufe 2.** B ist der kleinere Teil des Lochs aus
1.2 N: `Handle` **hat** ein Symbol (es ist ein deklarierter Alias), im Gegensatz zu `int[]`. Dass
`extend` dort nicht greift, ist ein Fehler und kein Entwurf.

**Bricht:** nein. **Hängt an:** Modul-/Opaque-Gebiet, `extern "dotnet"` Stufe 2, COL-08.
**Konfidenz:** gemessen.

---

### COL-38 — Was kostet jede Empfehlung die Konformanz-Suite, und was, wenn `lyrfix` nie gebaut wird?

**Heute, ausgezählt** (`lyric-spec` @ `8f17c02`): **178 Fälle**, davon **111 mit `since:`-Gate**.
Verteilung: 01-lexical 5, 03-types 29, 04-modules 24, 05-interfaces 24, 06-operators 16,
07-statements 39, 08-generics 16, 09-errors 7, 10-coroutines 16, 11-stdlib 2.

Was dieses Gebiet berührt:

| Empfehlung | betroffene Fälle | Spec-Kapitel mit `since: 5.0`-Gate |
|---|---|---|
| COL-03 B (Range wird Wert) | **1** retiriert (`07-statements/range_is_a_loop_head_not_a_value.lyr`, `since: 3.3.0`) | §3.3, §7.2, Grammar §Expr |
| COL-04 A (Float-Range) | 0 heute; braucht **neue** Fälle | §7.2 (Elementtyp, heute ungeschrieben) |
| COL-06 A (Index-Typ) | 0 (`SEM0007` hat **keinen** Fall in der Suite) | §3.3 oder §6, heute ungeschrieben |
| COL-07 B (`Index<K,V>`) | 0 direkt; §11 muss die Anker umschreiben | §11 |
| COL-15 C (`*` verbieten) | 0 — weil §6 die Operatoren nicht kennt (COL-34) | §6 (neu), §3.3 |
| COL-22 B/C | `SEM0091`: 1 Fall; `PAR0043`: 1; `VM0006`: 1 | §7.2 |
| COL-35 (eager Hälfte) | 11-stdlib hat **2** Fälle — die Suite deckt die std praktisch nicht | §11 |

**Das ist die wichtigste Zahl des Abschnitts: die Konformanz-Suite schützt dieses Gebiet kaum.**
`SEM0007` — der Code, an dem `for-in` und `[]` hängen — hat **null** Fälle. Wer COL-06, COL-07
oder COL-22 baut, hat keine Suite, die ihn fängt. **Vor jeder dieser Änderungen gehören Fälle in
die Suite, nicht danach.**

**Und `lyrfix` existiert nicht.** `src/` enthält 17 Projekte, keins davon `Lyrfix` (gelesen,
`ls src/`); `PLAN.md:343` führt es als erst zu bauendes v5-Werkzeug, und `PLAN.md:376` macht das
Entfernen der bereits laufenden Deprecations davon abhängig. Was passiert ohne es:

| | Option | Preis |
|---|---|---|
| A | `lyrfix` bauen, bevor die 4.7-Uhren gestellt werden | ein Werkzeug, das noch niemand geplant hat |
| B | Brüche auf die beschränken, die eine Handmigration erlauben | **COL-08** (jeder Import) und **COL-35** (jeder eager Name) fallen dann weg oder werden sehr teuer |
| C | die Uhren stellen und hoffen | genau das, was bei Oil schiefgegangen ist |

**Empfehlung: A, und COL-08 sowie COL-35 ausdrücklich davon abhängig machen.** COL-07 hat eine
kleinere Oberfläche (eigene `Indexable`-Implementierungen; in der std genau `List<T>`) und geht
auch von Hand.

**Bricht:** — **Hängt an:** allem. **Konfidenz:** gelesen + ausgezählt.

---

### COL-39 — Ist `sortArray` stabil, und was ist der Ordnungsvertrag?

**Heute, gemessen (r13b).** Stabil — aber nirgends zugesagt. `sortArray` geht über `sortList`
(`collections.lyr:1320-1328`) und dessen `mergeRuns` (`collections.lyr:855`). Probe mit den
Schlüsseln `1,0,1,0,1` und den Etiketten `A,B,C,D,E`:

```
sortListByKey -> B D A C E     (k=0: B vor D; k=1: A vor C vor E -- Eingabereihenfolge erhalten)
```

Ein Merge-Sort ist stabil, wenn man ihn richtig schreibt; dass er es hier tut, ist gemessen, dass
er es tun **muss**, steht nirgends. Und `Ordered<T>` sagt nicht, ob es eine **totale** Ordnung
zusichert — `binarySearch` (COL-14) braucht das.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | C# `Array.Sort` (**instabil**, und sagt es), Go `sort.Slice` (instabil, `SliceStable` daneben) | wer auf Stabilität baut, baut auf ein Implementierungsdetail |
| B | Stabilität **zusichern** | **Java** (`Collections.sort`), Python (`sorted`), Rust (`sort` stabil, `sort_unstable` daneben) | bindet an einen Merge-Sort; ein späterer Introsort wäre ein Bruch |
| C | B, plus ein `sortUnstable` für den Fall, dass jemand Speicher sparen will | **Rust**, Go | zwei Namen — aber beide sagen im Namen, was sie tun |
| D | `Ordered<T>` normativ als **totale Ordnung** erklären (irreflexiv, transitiv, total) | Rust (`Ord` gegen `PartialOrd`), Haskell | eine Zusage, die `float` mit `NaN` **verletzt** — dann braucht `float` eine Sonderregel |

**Empfehlung: B + D, mit der `NaN`-Frage ausdrücklich beantwortet.** B ist billig, weil die
Implementierung es schon tut. D ist die Pflicht, sobald COL-14 `binarySearch` bringt: eine binäre
Suche auf einer nicht-totalen Ordnung liefert falsche Antworten statt langsame. Und `float` ist
der Fall, an dem D scheitert — `Ordered<float>` mit `NaN` ist keine totale Ordnung. **Entweder
`NaN` ist in `Ordered<float>` verboten (Panik), oder `Ordered` sichert nur eine partielle
Ordnung zu und `binarySearch` verlangt mehr.** Das ist eine Frage des Numerik-/Operator-Gebiets,
aber sie wird hier fällig.

**Bricht:** nein (B/D sind Zusagen über Vorhandenes; D könnte `float` brechen).
**Hängt an:** COL-14, Operator-/Numerik-Gebiet. **Konfidenz:** gemessen + gelesen.

---

### COL-40 — Soll `LYR-SEM0007` gespalten werden?

**Heute, gelesen** (`lyric-spec/spec/appendix-a-diagnostics.md:109`): **ein Code für vier
verschiedene Ursachen** —

> „Not indexable / not iterable: index not an integer, a type without `Indexable<T>`, a `for` over
> a type without `Iterable<T>`/`Iterator<T>`, or an index on `string` (refused by design —
> codepoint positions cost O(n))."

COL-06 und COL-07 zerlegen genau diese vier Ursachen auf zwei neue Schnittstellen. Und die
Meldungen sind heute schon verschieden gut: `'Coroutine<int>' is not iterable — it must implement
'Iterable<T>' or 'Iterator<T>' from std.iter` (gemessen, r04) nennt den Ausweg;
`'Handle' is not iterable` (gemessen, r17) auch — aber beide sagen demselben Benutzer dasselbe,
obwohl im einen Fall der Typ ein perfektes `next()` hat (COL-26) und im anderen ein `extend`
ignoriert wurde (COL-37).

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | so lassen | — | ein Code für vier Ursachen; der LSP kann keine Quickfix-Unterscheidung anbieten |
| B | vier Codes: Index-Typ, kein `Index<K,V>`, kein `Iterable`, `string`-Index | **Rust** (E-Codes sind feinkörnig), Swift | vier Einträge im Anhang, vier `since:`-Gates; alte Codes müssen retiriert werden |
| C | zwei Codes: Indexierung und Iteration | — | die Halbierung, die COL-06/COL-07 ohnehin vollziehen |
| D | A, aber jede Ursache bekommt eine eigene **Note** | Rust (`note:`/`help:`) | billig, sofort machbar, kein Bruch |

**Empfehlung: C mit B-Noten, und D sofort in 4.7.** C spiegelt genau die Trennung, die COL-06
und COL-07 bauen: nach `Index<K,V>` ist „kein Index" eine andere Frage als „nicht iterierbar".
D ist unabhängig und heute fällig: `'X' is not iterable` sollte sagen, **warum nicht** — „hat ein
`next()`, ist aber kein `Iterator`" (COL-26) ist eine andere Meldung als „hat nichts".

**Das ist der Teil, den die Vorfassung schuldig blieb:** sie benutzte Diagnosequalität an
anderer Stelle selbst als Maßstab und lieferte für die eigenen Umbauten keine
Diagnostikhälfte — welche Codes entstehen, welche Note darin steht, was der LSP sieht.

**Bricht:** minor (Codes retirieren). **Hängt an:** COL-06, COL-07, COL-26, COL-37,
Diagnostik-Gebiet. **Konfidenz:** gelesen + gemessen.

---

### COL-41 — Bleibt `for (c in s)` über Codepoints?

**Heute, gemessen (r10, r10b) und gelesen.** Ja, Codepoints — `spec/07-statements.md:53-54`
sagt „a string (yielding `char` code points)". Gemessen mit einer kombinierenden Folge:

```
"e\u{0301}x"  ->  3 Codepoints: 101 769 120   (e, combining acute, x)
s.length()    ->  3
```

Also **zwei Grapheme, drei Durchläufe**. Und das Lowering materialisiert vorab das ganze
`char[]` über `std.string.rawToChars` (`FunctionLowerer.cs:1292-1308`, gelesen) — eine
O(n)-Allokation, mit dem ausdrücklichen Grund, dass ein `charAt`-Iterator die Schleife quadratisch
machen würde (Kommentar ebenda). **Das wiederholt COL-09s Allokationsargument, aber die Lösung
von COL-09 B greift hier nicht**, weil ein Index in einen String O(n) ist — genau der Grund für
`spec/03-types.md:73-75`.

| | Option | Vorbild | Preis |
|---|---|---|---|
| A | Codepoints, materialisiert | Status quo, Go (`range` über String gibt Runes) | O(n) Allokation pro Schleife; ein Graphem kann mehrere Durchläufe sein |
| B | Codepoints, aber **streamend** dekodiert statt vorab materialisiert | **Go** (`range` dekodiert UTF-8 im Lauf, ohne Allokation) | ein UTF-8-Dekodierer im Lowering; dafür null Allokation und O(1) Speicher |
| C | Graphem-Cluster statt Codepoints | **Swift** (`for c in str` gibt `Character` = Graphem-Cluster) | braucht die Unicode-Cluster-Tabellen in der Laufzeit — groß, und sie veralten mit jeder Unicode-Version |
| D | A, plus benannte Sichten: `s.codepoints()`, `s.graphemes()`, `s.bytes()` | **Rust** (`.chars()`, `.bytes()`, `unicode-segmentation` extern) | drei Namen, aber jeder sagt, was er tut; `graphemes()` darf extern bleiben |

**Empfehlung: B + D.** B ist reine Compilerarbeit ohne Sprachänderung und entfernt exakt die
Allokation, die COL-09 B für Arrays entfernt — Go macht genau das. D ist Swifts Lektion, ohne
Swifts Preis: Lyric sollte **nicht** stillschweigend Grapheme liefern (C), aber es sollte den
Unterschied benennen, damit niemand `length()` für „Zeichen" hält. `graphemes()` gehört in ein
Paket, nicht in die std.

**Was C kostet, hat Swift bezahlt** und würde Lyrics Einbettungsversprechen widersprechen: die
Cluster-Tabellen sind mehrere hundert KB und müssten in jede eingebettete Laufzeit.

**Bricht:** nein (B/D additiv). **Hängt an:** String-Gebiet, COL-09.
**Konfidenz:** gemessen + gelesen.

---

## 4. Was wir übernehmen sollten

**Aus Rust** — `IntoIterator` als **die eine** Schleifenschnittstelle (COL-11 B; das ist das
Vorbild, nicht C#s musterbasiertes `foreach`); `Index<K, V>` statt `Indexable<T>` (COL-07); die
Regel `[x; n]` nur für kopierbare Elemente (COL-15); `get() -> Option` neben `[]` (COL-23 B);
stabiles `sort` mit `sort_unstable` daneben (COL-39 C); die Ablehnung refutabler Patterns im
`for`-Kopf (COL-36).

**Aus C#** — `Range` als Wert bei gleichzeitiger **Kopie**-Semantik von `array[a..b]` (COL-02,
COL-03); die **Add-Route** der Collection Expressions als `FromLiteral`-Vorlage (COL-13 B', nicht
`CollectionBuilder`); der Modifikationszähler (COL-16 C); `IEnumerator : IDisposable` mit
garantiertem `Dispose` im `foreach` (COL-25 B); und die Entscheidung, Arrays **nicht** strukturell
zu hashen (COL-30 A).

**Aus Kotlin** — Adapter als Extensions statt Interface-Defaults (COL-12 B; gemessen der einzige
Weg, der die 15 KB löst); und `downTo`/`step`/`until` als Erzeuger eines **eigenen**
Progression-Typs, nicht als Methoden auf `Range` (COL-05 B).

**Aus Go** — die Warnung, nicht die Konstruktion, bei View-Slices (COL-02); die **Genauigkeit**,
mit der Go Mutation beim Walken spezifiziert, statt sie „undefiniert" zu nennen (COL-16 B);
streamende UTF-8-Dekodierung im `range` über Strings (COL-41 B); und `iter.Seq` als
Gegenentwurf, den man kennen sollte — wobei sein `bool` den **Abbruch** meldet, nicht das Ende
(COL-10).

**Aus Zig** — der Index-Capture `for (xs, 0..) |x, i|` als Rückfall für `enumerate` (COL-21 B).
**Und, wichtiger: Zig ist Lyrics einziger Verbündeter bei COL-03** — `0..n` steht dort
ausschließlich im Schleifenkopf, genau wie in Lyric. Wer die Regel umstößt, verliert den einzigen
Zeugen dafür, dass sie tragbar ist.

**Aus Swift** — der assoziierte Index-Typ als Fernziel (COL-07 D, und seit 5.7 **billiger**, als
die Vorfassung annahm); `for case` statt stiller Filter (COL-36 D); und die Warnung, was
Graphem-Cluster in der Laufzeit kosten (COL-41 C).

**Aus C#, Python und Kotlin gemeinsam** — der Generator **ist** der Iterator (COL-26). Lyric hat
Koroutinen mit der passenden Signatur und verbindet sie nicht. Das ist die billigste große
Verbesserung dieses Gebiets.

**Aus Python** — nur als Gegenbeispiel: negative Indizes nicht (aber der Grund ist der *kleine*
negative Index, nicht `xs[-1]` auf leer), `[[0]*3]*2` nicht, die stille erschöpfte Schleife nicht.

**Die vier Dinge, die vor jeder v5-Entscheidung passieren sollten** (alles 4.x-Arbeit, keine
Designfrage):

1. der Index-ICE (COL-06) — `LYR-CLI0020` auf `char` und `int32`, gemessen auf HEAD;
2. der Float-Range-Abschnitt (COL-04) — rechnet und rechnet falsch;
3. die Indexschleifen-Lowerung für Arrays (COL-09 B) — gemessener Faktor ~1,8;
4. **die schweigende `extend`-Deklaration** (1.2 N) — `extend int[]` und `extend (int,int)`
   müssen `LYR-SEM0047` melden, und `extend <opaque>` muss wirken (COL-37).

Und ein fünfter, der keine Zeile Compiler braucht: **Konformanzfälle für `LYR-SEM0007`.** Es gibt
heute null (COL-38).

---

## 5. Konflikte

### Mit CONTRIBUTING Rule 2 („ein Mechanismus pro Konzept")

- **COL-02 C (View-Slices)** wäre ein zweiter Sequenztyp neben `T[]` → Empfehlung B.
- **COL-07 C** (Index und Indexable nebeneinander) ist der Rule-2-Verstoß → „ersetzen".
- **COL-03 B** entfernt einen Mechanismus (`iter.range`/`rangeInclusive`) → Rule 2 **zugunsten**.
- **COL-12 B** ist nur dann eine Doppelung, wenn die Defaults bleiben. Sie müssen mit umziehen.
- **COL-05 C** (`rev()` allgemein) braucht ein zweites Iterator-Protokoll → abgelehnt.
- **COL-25 C** (zwei Iterator-Interfaces) → abgelehnt zugunsten von B.
- **COL-26 D** (eine Adapterklasse, die `next()` an `next()` weiterreicht) → Rule 2 in Reinform.
- **COL-35** ist der größte offene Rule-2-Verstoß des Gebiets: die std hat **zwei vollständige
  Kettenwelten** (eager/List und lazy/Iterator), und niemand hat das entschieden.
- **COL-33 D** (COW) würde vier Regeln auf eine reduzieren — Rule 2 zugunsten, aber zu groß.

### Mit bereits getroffenen Entscheidungen und mit der eigenen Aktenlage

- **COL-03 widerspricht `docs/Grammar.md:423-426` und `spec/03-types.md:79` direkt.** Die in
  `STATUS.md:2173`, `PLAN.md:310` und `PLAN.md:445` offen geführte Frage; hier mit „ja,
  revidieren" beantwortet. Kosten: **ein** Konformanzfall, drei Spec-Stellen.
- **COL-02 B widerspricht `lyric-v5-features.md:42` (Posten 10) und `PLAN.md:310`
  (Position 21).** Beide fordern **wörtlich** den View (`Span<T>`). Die Vorfassung nannte in §5
  nur STATUS.md und Grammar.md. **Das Projekt hat das Gegenteil zweimal aufgeschrieben.**
- **COL-13 „B, ohne C" widerspricht `lyric-v5-features.md:43` (Posten 11)**, das
  `let m: Map<string,int> = {"a": 1}` **wörtlich** fordert. Die Vorfassung führte den Konflikt
  nicht auf, obwohl §5 sonst jede Kollision listet.
- **COL-02 und COL-03 können nicht beide „übernimm Prototyp 09" heißen.** Prototyp 09
  (`docs/Befunde_und_Verbesserungen/prototypes/09-slices/README.md`) hält ausdrücklich fest:
  „§7.2 bleibt: eine Range ist kein Wert, sie kommt an ZWEI Stellen vor". COL-02 zitiert ihn
  zustimmend, COL-03 kippt genau diesen Satz. **Auflösung:** wird die Range ein Wert, ist
  Prototyp 09s Grammatikvorschlag (`SliceRange` als eigene Produktion in der Index-Klammer)
  **überflüssig** — `xs[r]` mit `r: Range<int>` ist dann ein gewöhnlicher Indexaufruf über
  `Index<Range<int>, T[]>`. Es gilt also: **COL-03 B ⇒ Prototyp 09s Grammatik fällt weg, seine
  Kopie-Semantik bleibt.** Wer COL-03 A nimmt, nimmt Prototyp 09 ganz.
- **COL-09 B und COL-22 B ziehen gegeneinander.** COL-09 B ersetzt den Array-Zweig durch eine
  Indexschleife (`FunctionLowerer.cs:1273-1286` — es ist der **vorhandene** Zweig, nicht ein
  fünfter); COL-22 B will alle Zweige auf `Iterable` reduzieren. Auflösung in COL-22: **B als
  Sprachregel, COL-09 B als Lowering-Optimierung auf dem einen Pfad.**
- **COL-12 und COL-21 sind spec-first, nicht Bibliothek.** Die Monomorphisierungsschranke steht
  normativ in `spec/05-interfaces.md:99-104` und nennt `fn chunks(): Iterator<T[]>` beim Namen.
  Die Vorfassung schrieb sie einem Quelltextkommentar zu und nannte in §5 nur „Interface-Gebiet".
- **COL-17 D widerspricht `spec/06-operators.md:104-108` — und das Kapitel widerspricht sich
  selbst.** Zeile 101-102 sagt „evaluated ONCE … the same promise `xs[i] += 1` makes", Zeile
  106-108 begründet `LYR-SEM0003` mit „would evaluate the object or the index twice". **Der
  Konflikt ist spec-intern und braucht keine Messung.**
- **COL-05s Warnung (`lo > hi`) kollidiert mit `spec/07-statements.md:64-65`**, das die leere
  Schleife ausdrücklich definiert. Eine Warnung braucht einen Spec-Zusatz; die Vorfassung
  forderte sie ohne. Und „immer ein Tippfehler" gilt nicht für konstante Grenzen aus `static let`.
- **COL-15 setzt COL-34 voraus.** Solange `spec/06-operators.md` die Array-Operatoren nicht
  kennt (null Treffer für „array", gelesen), ist das gemessene Struct-Aliasing **unspezifiziert**
  — weder Bug noch Feature —, und ein Verbot ist spec-first nicht begründbar.
- **COL-15 kollidiert mit der Wertsemantik-Runde.** `mut struct` „läuft seit 4.6"
  (`PLAN.md:375`). Gemessen greift `LYR-SEM0109` für ein Array-Element nicht (r18).
- **COL-08 und COL-35 hängen an `lyrfix`, das nicht existiert** (`ls src/`; `PLAN.md:343`,
  `PLAN.md:376`). Ohne es sind beide Handmigrationen.
- **COL-20 B gab `Hashable` still mit durch.** COL-30 korrigiert das: Rusts Ja gilt für einen
  **Wert**, Lyrics `T[]` ist eine Referenz mit schreibbaren Slots.
- **COL-08 berührt die Bibliotheks-Umkehrfrage** aus `PLAN.md:290-301` / `PLAN.md:443-444`, die
  offen ist.

### Mit anderen Gebieten

| Gebiet | Was dort entschieden werden muss, damit hier etwas geht |
|---|---|
| Generics | assoziierte Typen (COL-07 D, COL-21 D), Wertparameter (COL-01 B), bedingte Konformanz (COL-12, COL-14, COL-20, COL-30), Varianz (COL-24) |
| Interfaces | **Extensions auf Interfaces** (COL-12, COL-25), statische Member (COL-13 B), Member-Tabelle für strukturelle Typen (COL-14 C) |
| Operatoren | `Index`/`IndexSet` (COL-07); `++` aus `Add` (COL-17); **`in` über `Contains` — aber die Grammatikfrage gehört hierher** (COL-31); Identitätsvergleich (COL-29) |
| Optionals | ob `??T` jemals fällt (COL-10); `getOrNull` als Form (COL-23 B) |
| Fehler | typed throws St. 3 = werfende Funktionstypen (COL-27, `PLAN.md:263`); stummes `catch` um Panikpfade (COL-23 D) |
| Koroutinen | **`Coroutine<T> :: [Iterator<T>]`** (COL-26) — die größte Einzelverbesserung dieses Gebiets |
| Strings | `s[a..b]` bleibt draußen (COL-02); streamende Dekodierung und benannte Sichten (COL-41) |
| Pattern Matching | irrefutable Patterns im Schleifenkopf (COL-36); `[first, ..rest]` würde mit COL-02 zum Slice |
| Typsystem | `mut struct`/Wertsemantik (COL-15); COW für `T[]` (COL-33 D) |
| Einbettung | `extend` auf `opaque type` (COL-37); `extern "dotnet"` St. 2 |
| Diagnostik | Spaltung von `LYR-SEM0007` und die Noten (COL-40) |
| Werkzeuge | **`lyrfix`** (COL-38) — COL-08 und COL-35 hängen daran |
| Spec/Suite | Fälle für `LYR-SEM0007` (null heute); §6-Abschnitt für Array-Operatoren (COL-34) |

---

## 6. Was wir nicht wissen

Die Vorfassung lieferte 22 Fragen und 22 Empfehlungen. Eine Trefferquote von 22/22 ist bei einem
Gebiet, dessen zentrale Entscheidung seit Monaten offen geführt wird, selbst ein Befund. Hier
stehen die vier Fragen, bei denen ich **keine** Empfehlung habe, und drei Dinge, die ich nicht
gemessen habe.

**Ohne Empfehlung:**

1. **COL-02 gegen COL-03 — welche Grammatik gilt am Ende?** Ich habe eine Auflösung
   vorgeschlagen (§5), aber sie ist eine Ableitung, keine Messung. Ob `xs[r]` mit einem
   `Range<int>` als gewöhnlicher `Index`-Aufruf so gut liest wie Prototyp 09s eigene Produktion,
   weiß ich nicht — das entscheidet sich beim Schreiben der Grammatik, nicht hier.
2. **COL-33 — die eine Teilungsregel.** Ich empfehle A („vier Regeln, aber an einer Stelle"),
   weil B an `params` scheitert. Das ist ein Kompromiss aus Ratlosigkeit, kein Entwurf. Die
   richtige Antwort ist vermutlich D (COW), und die ist zu groß für dieses Gebiet.
3. **COL-35 — die eager Hälfte.** Ich empfehle „4.7 deprecaten, 5.0 streichen", aber ich habe
   **nicht** gemessen, wie viel Code in `examples/`, `stdlib-tests/` und Erato daran hängt. Ohne
   diese Zahl ist die Empfehlung eine Meinung.
4. **COL-39 D — `Ordered<float>` mit `NaN`.** Ich weiß, dass eine totale Ordnung dort bricht, und
   ich habe keine Antwort, die nicht in ein anderes Gebiet gehört.

**Nicht gemessen, obwohl es die Sache entscheiden würde:**

- **Woher kommen `Iterator<float>` und `Iterator<string>` in 1.2 J?** Die Auszählung zeigt, dass
  ein `Iterator<int>`-Wert alle drei Instanzen materialisiert. **Warum** die std zwei weitere
  instanziiert, habe ich nicht verfolgt (vermutlich `sumFloat` und eine String-Kette in
  `std.iter` selbst). Das entscheidet, ob COL-12 B die 15 KB ganz entfernt oder nur zum Teil.
  Ein `lyrc lower` mit einem Abzug der Typtabelle klärt es.
- **Was COL-35 kostet** (s. o.).
- **Ob COL-09 B tatsächlich den Faktor 1,8 schließt.** Ich habe gemessen, dass der Faktor da ist
  und dass der Bounds-Check ihn nicht erklärt (`STATUS.md:2466-2471`, Projektmessung). Dass eine
  Indexschleifen-Lowerung ihn schließt, ist **abgeleitet** — eine Prototyp-Messung fehlt.

**Eine Behauptung, die ich als Behauptung kennzeichne:** dass `sortList`s `mergeRuns` stabil sein
**muss** und nicht nur zufällig stabil **ist**, habe ich nicht aus dem Algorithmus verifiziert —
ich habe eine Probe mit fünf Elementen laufen lassen (r13b). Das ist ein Hinweis, kein Beweis.

---

## 7. Nach der Kritik geändert

**Gemessen und korrigiert (Kritiker hatte recht):**

- **COL-15 / 1.2 C:** `[e] * n` teilt das Element **auch für Structs** (gemessen r01: `7 7`; vier
  Kontrollen zeigen Wertsemantik überall sonst). Die Vorfassung behauptete das Gegenteil und
  baute die Empfehlung darauf. Empfehlung neu gefasst, plus die exakte Regel (Quelle wird einmal
  kopiert, n Slots teilen die Kopie — r25) und eine Gegenprobe, die `std.collections.slice`
  entlastet (r24).
- **`arrayFilled` IST `[value] * n`** (`collections.lyr:1283-1288`, gemessen `9 9`). Jede
  Warnung an der `*`-Schreibstelle geht daran vorbei. In 1.2 C und COL-15 aufgenommen.
- **1.2 I:** `Iterator` steht sehr wohl im Spec-Text — `05-interfaces.md:85-86,99-104`,
  `08-generics.md:75`. **§5.2a ist die normative Heimat der Monomorphisierungsschranke und nennt
  `chunks` beim Namen.** Folge: COL-12 und COL-21 sind spec-first; in §5 aufgenommen.
- **1.1:** „genau vier freie Funktionen" gilt nur für `collections.lyr`. Die std hat **acht**;
  `over` (`iter.lyr:438`) fehlte ausgerechnet.
- **1.2 L:** der quadratische Weg hat **zwei** Namen — `toArray()` und `collectArray`
  (`iter.lyr:959-961`), wortgleicher Doc-Kommentar. Drei Namen, zwei Komplexitäten.
- **Zählung:** 28 Member = `next` + 13 Adapter + 14 Terminatoren = **27** Defaults, nicht 13+16
  bzw. ~29.
- **Alle Zeilenangaben neu erhoben** auf `lyric@6f6f029f` und `lyric-spec@8f17c02`. TypeChecker:
  „matching numerics" 2907, `CheckIndex` 3028, `IsInteger` 3032, Pfad `src/Lyric.Frontend/Sema/`.
  PLAN.md liegt unter `docs/Befunde_und_Verbesserungen/`. Deque: `guide/13:126`.
- **`@Deprecated(until = "5.0")`** — `until` ist ein `string` (`core.lyr:504-509`). Dreimal
  korrigiert.

**Neu gemessen, weil die Deutung die Sache entscheidet:**

- **1.2 J:** die Zahlen stimmen, die **Deutung war falsch**. 13 triviale Defaults kosten 539 B,
  3 Adapterklassen 485 B — die 14,7 KB kommen aus **22 Slot-pflichtigen Defaults × 3
  Elementtypen + 9 Adapterklassen × 3**, und zwei der drei Elementtypen kommen im Programm nicht
  vor. Ein zweiter benutzter Elementtyp kostet **272 B** (r15f). **Folge: COL-12 Option C fällt**
  — faule Materialisierung geht an der Ursache vorbei.
- **1.2 K:** neu gemessen. Faktor ~1,7–1,9 bestätigt. Die Gleichsetzung der beiden for-in-Formen
  gilt in **debug**, nicht in release. Und die eigene Tabelle und `tools/Bench` messen **nicht
  dasselbe** (meine Range-Form indiziert zusätzlich) — sie stehen jetzt nebeneinander, nicht
  ineinander.
- **1.2 N:** die Ursache gefunden. `string` ist ein Builtin-**Symbol** mit Member-Tabelle,
  `int[]` ein struktureller Typ ohne Träger; `LYR-SEM0047` nennt „array, tuple" im eigenen Text
  und **kann für sie nie feuern** (`TypeChecker.cs:629-638`, gemessen r14/r16a/r16b/r16c).
  **Folge: COL-14 C ist deutlich teurer als angenommen — Empfehlung auf B mit C als Ziel gedreht.**

**Vergleichssprachen korrigiert:** C# `foreach` ist musterbasiert (COL-11s Begründung auf Rusts
`IntoIterator` umgestellt); `IEnumerator` hat `MoveNext()`, Property `Current`, `Reset()`; C# 12
hat **vier** Literal-Routen (COL-13 B' aufgenommen); Go wertet den Slice-Range **einmal** aus und
spezifiziert Map-Iteration genau (COL-16 B gestärkt); Gos `iter.Seq`-bool meldet **Abbruch**,
nicht Ende (COL-10s Kostenvergleich); Kotlins `downTo`/`step`/`until`/`reversed` sind
**Extensions**, und Kotlin trennt `IntRange` von `IntProgression` (COL-03 C ersetzt, COL-05 B
neu gefasst); Rusts `Range<T>: Iterator` gilt nur für `T: Step`, und `[1,2,3]` ist ein
Array-Literal; Swifts Existential-Sperre ist seit 5.7 gefallen (COL-07 D billiger); **Zig ist
Lyrics Verbündeter bei COL-03**, nicht ein Slice-Fall; Pythons `xs[-1]` wirft auf einer leeren
Liste.

**19 fehlende Designfragen eingearbeitet** (COL-23 bis COL-41), jede mit belegtem Ist-Stand,
Optionen mit Vorbild und Preis, Empfehlung, Bruchgrad und Abhängigkeiten. 17 davon sind neu
gemessen (r02–r25). Die wichtigste: **COL-26 — `Coroutine<T>` hat gemessen dieselbe
`next(): ?T`-Form wie `Iterator<T>`, der Guide sagt „on purpose" (`11-coroutines.md:85-88`), und
die beiden sind nicht verbunden** (`LYR-SEM0007`, `LYR-SEM0001`). Das Wort „Koroutine" kam in der
Vorfassung nicht vor.

**Schwache Empfehlungen nachgezogen:** COL-11 C ist kein Zusatz zu B, sondern das Einzige, was
(E) behebt; COL-09 B ersetzt einen Zweig und zieht gegen COL-22 B (aufgelöst); COL-05s
`lo > hi`-Warnung kollidiert mit `07-statements.md:64-65` und ist nicht „immer ein Tippfehler";
COL-17s Beleg ist der **spec-interne Widerspruch** 101-102 gegen 106-108, nicht die Messung;
COL-20 B gibt `Hashable` nicht mehr still mit durch (COL-30); COL-02 und COL-13 führen ihre
Konflikte mit `lyric-v5-features.md` jetzt in §5.

**Neu: §6 „Was wir nicht wissen"** — vier Fragen ohne Empfehlung, drei ungemessene Punkte und
eine ausdrücklich als *behauptet* gekennzeichnete Aussage.
