# 21 — Nestbare Optionals (`??T`) vs. `Option<T>` als stdlib-Enum (MAJOR)

Zugehöriger language-review-Punkt: **"MAJOR-Vorschlag: nestbare Optionals (oder `Option<T>` als Enum) — `?` schachtelt nicht, und die Rechnung kommt in Generics an"**; language-review empfiehlt, Variante B zu prüfen. Verwandt: stdlib-review "`List<?T>` und `Map<K, ?V>` sind nicht instanziierbar".

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 8 — Index-Schleife, Wrapper-Struct `Slot` | 34 |
| `probe-nested.lyr` | `firstOf<T>` mit `T = ?int` → `LYR-IR0001 "nested optional"` (Lowering, nicht Sema) | 8 |
| `lib-variante.lyr` | **läuft**, exit 0 — `Option<T>`-Enum, nach VIER Umgehungen (siehe Bugs) | 60 |
| `probe-optional-payload.lyr`, `probe-generic-method-optarg.lyr` | Bug-Repros (siehe unten) | — |
| `soll.lyr` | PROPOSAL Variante A (`??T`) zur Gegenüberstellung | 24 |
| `vergleich.rs` | Rust `Option<Option<T>>`, Kotlin als Kommentar (kollabiert wie Lyric) | 22 |

## Ist-Stand
(1) `for (x in slots)` über `(?int)[]` ist `LYR-SEM0091` → Index-Schleife. (2) `firstOf<T>(xs: T[]): ?T`
mit `T = ?int` wird NICHT still kollabiert, sondern im **Lowering** abgelehnt (`IR0001 "a nested
optional '??T'"`; die Sema schweigt — Fehlerort korrekt in der Nutzerdatei, aber spät). Die
generische Funktion ist damit für optionale Elementtypen unbenutzbar; Umgehung Wrapper-Struct.
(3) `List<?int>` ist nicht instanziierbar (stdlib-review) → `List<Slot>`. Der Wrapper-Struct
`Slot { value: ?int }` ist die durchgängige Umgehung: er versteckt die Absenz eine Ebene tiefer,
wo `?` sie nicht mehr sieht — genau die Hebung, die `Some(…)` in Rust ausdrückt, von Hand.

## Variante B (stdlib `Option<T>`) — was die Lib-Variante zeigt
`Option<T>` als Enum ist heute schreibbar und unterscheidet `None` von `Some(null)` (Ausgabe
`a: Some … b: None`); ein Iterator-Protokoll `next(): Option<T>` funktioniert für `T = ?int`;
`List<Option<?int>>` ist instanziierbar. **Aber**: Enums mit optionalem Payload treffen heute
auf vier Compiler-Defekte (unten), sodass die Lib-Variante nur mit `Some(_)`-Armen, ohne
Methoden auf `Option<?int>` und mit vorgebundenen Payload-Werten läuft. Vor jeder stdlib-
Entscheidung für B müssen diese Defekte behoben sein — sie treffen auch `Result<T, E>` mit
`T = ?U` (stdlib-review Major 1).

Preis von B (language-review hat ihn benannt): zwei Absenzformen (`?T` und `Option<T>`) im
selben Programm, Bruch aller `next()`-Implementierungen (5.0 mit Uhr), und die Brücke
`toOptional()` kollabiert wieder. Gewinn: kein Laufzeitumbau, Format 4.0 bleibt.

## Variante A (`??T` als Typ) — Skizze in `soll.lyr`
`null` = äußerste Absenz; Zuweisung `?T → ??T` hebt (Widerung §3.7 auf `T = ?U` ausgedehnt);
`x != null` schält eine Ebene. Laufzeit: ein `?T` ist heute Wert + Sentinel/Tag im Slot, `??T`
braucht einen zweiten Tag → Layout-Änderung → **Format-Major** (§13). A löst (1)–(3) ohne
zweite Absenzform und ohne stdlib-Bruch — aber nur, wenn ein Format-Major ohnehin ansteht.
Kotlin (kollabiert) und C# (`Nullable<Nullable<T>>` verboten) zeigen, dass man mit dem Kollaps
leben kann; Rust/Swift/Zig zeigen, dass Generics ohne ihn einfacher sind.

## Bewertung
| | Ist | B (`Option<T>`) | A (`??T`) | Rust |
|---|---|---|---|---|
| `firstOf` über `(?int)[]` | abgelehnt (IR0001) | `Option<?int>` | `??int` | `Option<Option<T>>` |
| `List<?int>` | Wrapper-Struct | `List<Option<?int>>` | `List<?int>` | `Vec<Option<T>>` |
| Absenzformen | 1 | **2** | 1 | 1 |
| Format-Änderung | — | nein | **ja (Major)** | — |
| stdlib-Bruch | — | `Iterator.next()` (5.0) | keiner | — |

- **Fehlerklassen verhindert**: Wrapper-Structs, die die Absenz verstecken (`Slot`), und die
  Unmöglichkeit generischer Container über `?T` — heute kein stiller Bug (der Compiler lehnt ab),
  sondern eine Ausdrucksgrenze.
- **Aufwand**: B: stdlib ~80 Z. (`Option<T>` + Kombinatoren) + Migration `Iterator` (jede
  Konformanz) — und VORHER die vier Compiler-Fixes; A: Sema (Assignability, Narrowing-Ebenen,
  Exhaustiveness) ~200 Z., Lowering/VM (zweiter Tag, Layouts) groß, Format-Major.
- **Breaking**: B ja (Iterator-Protokoll, 5.0); A ja (Format).
- **Empfehlung**: Erst die vier Defekte beheben (sie sind unabhängig von A/B nötig, sobald
  irgendein Enum ein `?T`-Payload trägt). Dann B **nur**, wenn `Iterator<T>` der einzige
  Protokollpunkt ist, an dem `?T` als Absenzsignal in Generics ankommt (language-reviews
  Bedingung) — die Lib-Variante legt nahe: ja, plus `Map.get`/`List.pop`-artige Rückgaben, die
  aber `?T` behalten können, solange `T` selbst nicht optional ist. A nur mit einem ohnehin
  anstehenden Format-Major.

## Nebenbefunde — Enum mit optionalem Payload: vier Defekte (`Option<?int>`)
1. **Exhaustiveness** (`TypeChecker.cs:4288`, `IsIrrefutable`: `BindingPattern` über `Optional` →
   false): `match (o) { Some(v) => …, None => … }` über `enum O { Some(?int), None }` ist
   `LYR-SEM0050 "missing Some"`; `v` wird als `int` gebunden (die §7.6-Regel für `?E`-Scrutinees
   greift im SUB-Pattern). `Some(null)` + `Some(v)` zusammen zählen ebenfalls nicht als Abdeckung
   (`VariantCovered`). Nur `Some(_)` deckt. → `probe-optional-payload.lyr`.
2. **`Some(null)` als Sub-Pattern** ist `IR0001 "nested LiteralPattern"` (bekannte Grenze,
   language-review) — zusammen mit 1 gibt es KEINE Schreibweise, die `Some(null)` von
   `Some(v)` im match trennt.
3. **Methoden eines generischen ENUMS mit optionalem Typargument werden nicht gelowert**:
   `Option<?int>.Some(3).isSome()` → `IR0001 "call to 'isSome' (external or bodiless)"`
   (`FunctionLowerer.cs:3914`, `TryResolveFunction` findet die Instanz nicht); dieselbe Methode
   auf einer generischen KLASSE `Box<?int>.get()` funktioniert. → `probe-generic-method-optarg.lyr`.
4. **Payload-Kontext nach Substitution**: `Option<?int>.Some(null)` → `IR0001 "'null' in a
   position without an expected type"`; `Option<?int>.Some(7)` → **IR-Verifier-Absturz**
   `newvariant field 0 is i64, expected ?i64` (`IrVerifier.cs:291`). Die Widerung `int → ?int`
   fehlt, wenn der Payload-Typ erst durch Substitution optional wird — dieselbe Familie wie
   language-reviews "konkreter Struct-Wert an Interface-Parameter einer generischen Instanz".
   Umgehung: Wert vorher an ein `?int`-Lokal binden.
