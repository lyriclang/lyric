# Laufzeitmodell und Speicher — Dossier für die v5-Designrunde

Stand 2026-09-28, **dritte Fassung** nach der zweiten adversarischen Kritik (`_kritik-3.json`,
Schlüssel `laufzeitmodell`). Der Überarbeitungslauf vom 27.09. brach vor dem Zusammenfügen ab;
diese Datei ist die vollständige, kontrollgemessene Fassung vom 28.09. Gemessen gegen
`src/Lyrc/bin/Debug/net10.0/lyrc.dll` und `src/Lyrvm/bin/Debug/net10.0/lyrvm.dll`
(Debug-Binaries, Format 4.0). Proben:

- `…/scratchpad/v5-design/probes/laufzeit/` — erste Runde (P1…P14)
- `…/scratchpad/v5-design/probes/laufzeit-review/` — Proben des ersten Kritikers (R1…R10)
- `…/scratchpad/v5-design/probes/laufzeit-rev2/` — zweite Runde (Q1…Q15)
- `…/scratchpad/v5-design/probes/laufzeit-adv/` — Proben des zweiten Kritikers (a…e)
- `…/scratchpad/v5-design/probes/laufzeit-rev3/` — **diese Runde (R3a…R3j, R3q9)**
- `…/scratchpad/v5-design/probes/laufzeit-rev4/` — **Kontrollrunde 2026-09-28**: R3a, R3c, R3e,
  R3f, R3g, R3h, R3i und Q6b nachgefahren, Erwartungen vorher in `ERWARTUNG.txt`; alle
  bestätigt, eine Zeitmessung präzisiert (§1.2, `fib`)

Belegarten: **gemessen** = ein Programm lief · **gelesen** = Pfad:Zeile · **behauptet** = weder noch.
Jede Messung dieser Runde hat ihre Erwartung im Kopf der Probendatei stehen, vor dem Lauf
geschrieben, und jede überraschende Messung hat einen Kontrolllauf daneben. Wo der Kritiker
gemessen hat, habe ich seine Probe **nachgefahren**, nicht übernommen.

Die zweite Fassung verwies auf L24–L31 und auf einen Abschnitt „Nach der Kritik geändert", die
es in der Datei nicht gab — die Datei endete bei L23. Diese Fassung enthält L1–L44 vollständig,
die Abschnitte „Was wir übernehmen sollten" und „Konflikte" und den Änderungsabschnitt am Ende.
Zwei Empfehlungen der zweiten Fassung hingen an dem fehlenden Text (L10 an L26, L9 an L27);
beide sind hier neu begründet.

---

## 1. Ist-Stand

### 1.1 Der Wert

`LyrValue` ist ein `readonly struct` aus zwei Feldern: `ulong Bits` und `object? Ref`
(`src/Lyric.Vm/LyrValue.cs:20-21`). Auf x64 sind das **16 Byte, für jeden Typ gleich** —
ein `bool`, ein `uint8`, ein `float` und eine Klassenreferenz belegen denselben Slot. Das ist eine
**Beobachtung auf x64**, keine Zusage (L41): die Spezifikation sagt zur Größe eines Werts nichts.

| Form | Darstellung | Beleg |
|---|---|---|
| Skalar | `Bits`, immer auf 64 Bit erweitert, `Normalize` stellt die Breite wieder her | LyrValue.cs:136-149 |
| `string` | `Ref` ist ein .NET-`string`: UTF-16, unveränderlich, per Referenz geteilt, **2 B pro Einheit plus Objektkopf** | LyrValue.cs:34, 127; gemessen R3j (§1.2) |
| Host-Objekt | `Ref`, ein beliebiges .NET-Objekt, „the VM never looks into" | LyrValue.cs:37-45 |
| Objekt, Struct, Array | `Ref` ist ein `LyrValue[]`, ein Slot pro Feld bzw. Element | LyrValue.cs:47-49 |
| **Enum mit Payload** | **ein gewöhnliches `LyrValue[]`, Slot 0 trägt den Tag, die Payload-Felder folgen** — „an enum needs no representation of its own" | Interpreter.cs:1015-1026 (`newvariant`); Format.cs:348-356 |
| **Tupel** | ein `LyrValue[]` wie ein Struct ohne Namen (**behauptet** — nicht direkt gelesen; gemessen ist nur das Kopierverhalten beim Destrukturieren, R3f) | — |
| Interface-Wert | Fat Pointer: `Ref` = Instanz, `Bits` = Index des konkreten Typs | LyrValue.cs:70-75 |
| Closure | Fat Pointer: `Ref` = Umgebung, `Bits` = Funktionsindex **+1** | LyrValue.cs:87-91 |
| Koroutine | `Ref` = `CoroutineChain`, ungetaggt wie alles andere | LyrValue.cs:102-106 |
| `?T`, leer | `default` — beide Felder null | LyrValue.cs:109 |
| `?T`, belegt, Referenz | der Wert selbst; die Referenz ist die Anwesenheitsmarke | LyrValue.cs:112-114 |
| `?T`, belegt, Skalar | `Bits` plus ein global geteilter `SomeMarker` — **keine Allokation** | LyrValue.cs:52-57, 112-114 |

Die Typ-Tags stehen im Instruktionsstrom, nicht im Wert (`src/Lyric.Core/Bytecode/Format.cs:131-192`),
und die Spezifikation macht die Monomorphisierung ausdrücklich zur Voraussetzung dafür
(`lyric-spec/spec/03-types.md:84-86`).

Der Zugriff auf einen zusammengesetzten Wert ist ein **ungeprüfter Cast**:
`AsObject => (LyrValue[])(Ref ?? throw …)` (`LyrValue.cs:130-131`). Das ist der Satz, an dem L1
hängt: jede Darstellung, die kein `LyrValue[]` ist, muss vor diesem Cast unterschieden werden. Und es
ist der Satz, an dem L37 hängt: eine `InvalidOperationException` und eine `InvalidCastException`
sind hier als .NET-Ausnahmen möglich, nicht als Panik.

### 1.2 Der Speicher

Der .NET-GC ist der einzige Mechanismus **für den Heap**: kein `free`, kein Refcount, kein
Borrow-Checker, kein `unsafe` (`STATUS.md:2466-2470`). Die Sprache hat **keine Oberfläche auf den
GC**: kein `collect`, keine Heap-Größe, keine Statistik, keine schwachen Referenzen, keine
Finalizer. Belege: `grep -rn "WeakReference" src/` ist leer (gemessen), `grep -rn "GCSettings\|GC\.Collect\|GCHeapHardLimit" src/`
ist leer (gemessen) — **die VM erbt die GC-Konfiguration des Prozesses und rührt sie nie an**
(L39). `stdlib/` deklariert kein Native, das den GC anspricht (gelesen, `src/Lyric.Vm/NativeRegistry.cs`).

**Für Ressourcen gilt das nicht.** Eine Datei, ein Socket, ein Kindprozess liegt in einer
`Dictionary` der `NativeRegistry` und damit außerhalb des GC; `close` ist ein manuelles `free`,
und für genau die knappen Dinge ist es das einzige (§1.5).

**Ein Array kostet 16 Byte pro Element, unabhängig vom Elementtyp — und ein `string` kostet zwei.**
Gemessen (P5/P5b und **R3j**, Peak Working Set während des Laufs gesampelt, gegen den Leerlauf):

| Programm | Einheiten | Peak WS | minus Leerlauf | pro Einheit |
|---|---:|---:|---:|---:|
| `int[]` (P5) | 20 000 000 | 333,5 MB | 310 MB | **15,5 B** |
| `uint8[]` (P5b) | 20 000 000 | 335,0 MB | 308 MB | **15,4 B** |
| `char[]` per `['a'] * n` (R3j) | 16 777 216 | 283,5 / 284,7 MB | 257 MB | **15,3 B** |
| `string` per 24-facher Verdopplung `s = s + s` (R3j) | 16 777 216 | 91,5 / 92,7 MB | 65 MB | 2 B Endwert (32 MB) **plus** die Verdopplungsreste, die der GC noch nicht eingesammelt hat |
| Leerlauf (R3j ohne Argument) | — | 27,0 / 26,9 MB | — | — |

Ein Bytepuffer ist also **16× größer als nötig**, ein `int64[]` doppelt so groß, und ein `char[]`
**achtmal** so groß wie der `string` mit demselben Inhalt. Das trifft die Module, die die
v5-Liste als P1 führt: `std.bytes`, `std.io`, `std.regex`, `std.crypto`, `std.compress` — und es
heißt, dass **`utf8Encode` heute die teuerste Darstellung erzeugt, die es gibt** (L38).

**Die Struct-Kopie ist tief für nackte Struct-Felder und flach für alles andere — auch für
`?Struct`- und Interface-Felder.** Gemessen (P1 und **R3a**, Erwartung vorher notiert, Kontrolle
`plainField`):

| Feld im Struct | nach `var b = a; b.<feld>… = 99` liest `a` | Beleg |
|---|---|---|
| Skalar | **1** (kopiert) | P1 |
| Struct | **1** (rekursiv kopiert) | P1, R3a `plainField = 1` |
| Array | **99** (geteilt) | P1 |
| Klasse | **99** (geteilt) | P1 |
| **`?Struct`** — `b.opt!.n = 99` | **99 (geteilt)** | **R3a `optField = 99`** |
| **Interface-Wert mit Struct dahinter** — `d2.iface.hoch()` | **1 (geteilt)** — `d1.iface.wert()` liest die Mutation | **R3a `ifaceField = 1`** |

`CopyStruct` rekursiert ausschließlich über `FieldTypes[i].Tag == TypeTag.Struct`
(`Interpreter.cs:1161-1163`); ein `?Struct`-Feld trägt den Tag Optional und fällt durch, ein
Interface-Feld ist ein Fat Pointer und fällt durch. Der Kommentar darüber nennt genau das
(„shallow across everything else: a field of class, array or interface type … shares it",
`Interpreter.cs:1145-1147`) — **`?Struct` nennt er nicht**, und der Guide nennt keine der Grenzen
(`docs/guide/05-structs-and-classes.md:5`). Das ist L33. Die zweite Fassung hatte den Kritiker
hier korrigiert („die Familie, die §1.7 jetzt vollständig auflistet"); die Liste war um zwei
Mitglieder zu kurz.

**Ein Enum-Payload und ein Tupel-Element sind beim Binden Werte, und deshalb ist ihr Teilen
unbeobachtbar.** Gemessen (**R3f**, Erwartung vorher notiert, Kontrolle Klassen-Payload):

| Fall | Ergebnis | kopiert? |
|---|---|---|
| `let b = Box.Voll(z); z.hoch(); lese(b)` — das Einpacken in den Variant | `0` | ja |
| `match (b) { Box.Voll(q) => q.hoch() }; lese(b)` — die `match`-Bindung | `0` | ja |
| `let (tz, _) = t; tz.hoch(); let (tz2, _) = t; tz2.wert()` — Destrukturieren | `0` | ja |
| KONTROLLE: Klassen-Payload `KBox.Voll(kz); kz.hoch()` | `1` | nein (Referenz) |

Ein Enum-Wert und ein Tupel sind Referenzen auf ein `LyrValue[]` — `let e2 = e1` teilt das Array
—, aber jeder Weg, an die Struct-Payload zu kommen, ist eine Bindung, und `BindLocal` kopiert bei
`type is IrStructType` (`FunctionLowerer.cs:2914-2917`). Ohne Bindung gibt es keinen Schreibpfad
in die Payload, also ist die geteilte Darstellung nicht zu beobachten. **Das ist konsistent und
steht nirgends** (L34).

**Wo die Kopie entschieden wird — fünf Stellen, nicht drei.** `Interpreter.cs:628-631` **führt**
`structcopy` nur aus („The compiler decided where to copy; here it is only copied"). Entschieden
wird es in `src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs`:

| Stelle | Bedingung | Zeile |
|---|---|---|
| `BindLocal` | `type is IrStructType && !_fresh.Contains(value)` | 2914-2917 |
| `Coerce`, Zielseite | `target is IrStructType && from is not IrOptionalType && !_fresh` | 3036-3039 |
| `Coerce`, Interface-Weg | `source is IrStructType && … && from is not IrOptionalType && !_fresh` | 3041-3048 |
| **`LoadCaptured`** (Closure-Umgebung) | **keine Kopie** — der Slot wird geladen | **366-380** |
| **`CopyStruct`** (VM, die Tiefe der Kopie) | rekursiv nur bei `Tag == Struct` | **Interpreter.cs:1152-1166** |

Die ersten drei prüfen den **statischen Quelltyp**: sobald der schon `?T` oder schon ein Interface
ist, greift keine (Befund A). Die vierte kopiert **gar nicht** (R3e, unten), die fünfte kopiert
**zu flach** (R3a, oben). Wer L2 mit „drei Lowering-Stellen, ~60 LOC" ansetzt, repariert drei
von fünf.

**Closures: EINE Regel, und sie steht in der Spec — aber in zwei Fassungen, und für Structs
bricht die Implementierung sie.** Die zweite Fassung schrieb „zwei verschiedene Antworten auf
dieselbe Frage, keine davon geschrieben" und nannte die `for`-Bindung „eine Eigenschaft der
`for`-Lowerung". Das war falsch, und der Kritiker hat es mit einer Probe gezeigt, die ich
nachgefahren habe:

| Probe | Ergebnis | was es zeigt |
|---|---|---|
| Q1 `while`, die `var i` gefangen | `3 3 3` | die **Zelle** wird gefangen — `spec/07-statements.md:82-85`: „a `var` as the variable itself — the enclosing scope and the closure share one cell" |
| Q1 `for (j in 0..3)`, `j` gefangen | `0 1 2` | `j` ist ein **`let`** — gemessen **R3c**: `j = 5` im Rumpf ist `LYR-SEM0019` „not a mutable lvalue" — und ein `let` „is captured as its value" (ebd.) |
| Q1 KONTROLLE frisches `let` im Rumpf | `0 1 2` | dieselbe Regel |
| **R3e** `let l = Zaehler{n=0}; let f = () => l.wert(); l.hoch(); f()` | **`1`** | **ein gefangenes `let`-Struct ist ein ALIAS**, obwohl die Spec „as its value" sagt |
| R3e KONTROLLE `let k = m; m.hoch(); k.wert()` | `0` | die gewöhnliche Bindung kopiert |
| R3e `var v = …; let g = () => v.wert(); v.hoch(); g()` | `1` | `var` gefangen = Zelle geteilt, per Spec richtig |

Also: **eine** Regel (`let` als Wert, `var` als Zelle), von der Spec geschrieben, von `for` per
`let`-Bindung erfüllt. Die zweite Fassung hatte damit eine falsche Prämisse, und ihre Optionen
L22/B und L22/E („`while` gleichziehen", Go 1.22) beruhten darauf; sie sind gestrichen. Was
tatsächlich offen ist: (a) `spec/03-types.md:77` sagt pauschal „closures capture by reference",
§7.3 sagt „`let` als Wert" — zwei Kapitel, zwei Formulierungen (L22); (b) für ein `let`-**Struct**
verhält sich die Implementierung nach 03:77 und nicht nach 07:82 — ein **Spec-Bruch**, keine
Lücke (L32). Unberührt bleibt: die Umgebung hält genau die gefangenen Symbole
(`FunctionLowerer.cs:366-398`), und ohne Captures gibt es keine Umgebung und keine Allokation
(ebd. 393-397).

**Was eine Kopie kostet — neu gemessen, mit den Zahlen des Kritikers daneben.** Q9 = P14
(`r3q9_copy.lyr`): ein 8-Feld-Struct aus einem Array lesen, ein Feld ändern, zurückschreiben —
**zwei** `structcopy` pro Durchlauf — gegen dieselbe Schleife mit einer Klasse. Diese Runde:
Wallclock, **drei Läufe je Zelle**, Debug-Binaries, Leerlauf `q9_leer` 367/391/371 ms (≈375
abgezogen). Die zweite Fassung hatte „Minimum aus neun verschachtelten Läufen, Grundlast 270 ms"
und daraus „Klasse 17×" gemacht; der Kritiker fand 4–5×. Beide Zahlen stehen unten.

| Zelle | R3 (3 Läufe, ms) | R3 netto | Kritiker netto | 2. Fassung netto |
|---|---|---:|---:|---:|
| Struct, Interpreter | 2897 / 2912 / 3262 | ~2520 | ~1590 | 1339 |
| Klasse, Interpreter | 2036 / 1994 / 2043 | ~1620 | ~1060 | 969 |
| Struct, `--jit` | 3030 / 3035 / 2967 | ~2590 | ~1570 | 1352 |
| Klasse, `--jit` | 629 / 600 / 588 | ~215 | ~235 | 57 |

Was **robust** ist, über alle drei Messanordnungen: die Struct-Schleife bekommt unter `--jit`
**keinen** Gewinn (0,97× / 0,99× / 0,99×); die Klassen-Schleife bekommt einen (≈7,5× / ≈4,5× /
„17×"); Struct gegen Klasse unter `--jit` ist **eine Größenordnung** (≈12× / ≈6,5× / „≥10×").
Was **nicht** robust ist, ist jede exakte Zahl — sie hängt an Grundlast und Rechnerlast, und die
zweite Fassung durfte „mehrfach reproduziert" nicht schreiben. Diese Fassung sagt: *eine
Größenordnung, zwischen 6× und 12×*, und alle Zahlen sind Debug-Wallclock.

Gelesen dazu: `src/Lyric.Vm/Jit/JitCompiler.cs:30-36` zählt auf, was der Emitter beherrscht,
und **was er ablehnt: „closures, exceptions, enums, recursion, and the narrow integer widths"** —
`structcopy` steht auf keiner der Listen und fällt unter `default: return false`
(`JitCompiler.cs:679-680`, ganze Funktion). **Rekursion lehnt sich selbst ab**: `CodeFor` markiert
eine Funktion als versucht, bevor sie kompiliert ist; erreicht die Kompilierung sie erneut,
antwortet sie „no code" und der Aufrufer lehnt ab — „a recursive helper does not compile today"
(`JitCompiler.cs:721-724`; Guide 14:308 sagt es dem Benutzer). Gemessen (**R3h**): `fib(27)`
Interpreter 646/598/664 ms, `--jit` 727/693/694 ms — **kein Gewinn**. Kontrolle rev4
(2026-09-28, ruhigere Maschine): Interpreter 282/281/328 ms, `--jit` 275/275/259 ms — die
Differenz liegt im Rauschen (±10 %); dass der JIT *langsamer* wäre (rev3), hält die Kontrolle
nicht, dass er nichts gewinnt, hält — und das ist der Punkt, nicht die Richtung des Rauschens. Die zweite Fassung hat die Rekursion nirgends geführt; sie betrifft L5, L20 und L35.

### 1.3 Der Stack

Ein expliziter Frame-Stack im Heap, nicht die CLR-Rekursion (`Interpreter.cs:125-127`), Frames
pro Funktion gepoolt (ebd. 129-136, Pool bei 1556-1602). **Zwei Grenzen, und sie SIND gekoppelt**
— die zweite Fassung schrieb „nichts verbindet sie", und das war falsch:

- `MaxCallDepth = 1024`, eine `const` (`Interpreter.cs:139`). Sie zählt **nicht** `frames.Count`
  eines Laufs, sondern `outer + frames.Count` (`Interpreter.cs:533`), und `outer` ist
  `_outerFrames`, ein `[ThreadStatic]`-Feld, das vor jedem Native-Aufruf auf
  `outer + frames.Count + 1` gesetzt wird (ebd. 526-528): **die Frames aller äußeren Läufe auf
  diesem Thread**. Der Kommentar sagt, warum: „script to host to script never reached 1024 and
  took the process down with a stack overflow instead" (ebd. 146-151). Die Tiefe ist also eine
  Eigenschaft des **Threads**, nicht der VM und nicht des Laufs — zwei VMs auf einem Thread
  teilen einen Zähler (L30).
- `MaxReentryDepth = 32` für verschachtelte Läufe (`Interpreter.cs:158-177`), ebenfalls
  thread-statisch, weil ein verschachtelter Lauf **echten CLR-Stack** kostet. Gemessen vom
  Projekt selbst (64 überlebt, 96 stirbt) und halbiert. **Beide Grenzen paniken mit demselben
  Code `LYR-VM0004`** (`TooDeep()` bei :192-194; :533-535) — der Text unterscheidet sie
  („re-entry nested 32 runs deep" gegen „call depth exceeded"), der Code nicht, und
  `appendix-a-diagnostics.md:278` beschreibt nur den Rekursionsfall („The limit is quality of
  implementation (the reference allows 1024 frames); the code and the panic are the contract").

Gemessen (Q15 = P4): Tiefe 1020 läuft, 1500 panikt, Exit 101, 1026 Ausgabezeilen ohne Elision.
**Gemessen (R3g): ein echter Tail Call kostet einen Frame.** `return down(n - 1, acc + 1)` panikt
bei 1500 mit `LYR-VM0004` in **beiden** Engines (1026 Zeilen) — `Interpreter.cs:137-138` sagt
„There is no tail-call optimization" und der JIT lehnt Rekursion ohnehin ab. Das ist L36.

**Gemessen (R3h): eine Panik in rekursivem Code hat unter `--jit` den vollen Backtrace** — fünf
positionierte Frames `in main.tief (r3h_jitrec.lyr:7)` … — während die Kontrolle Q6b
(nichtrekursiv, `--jit`) auf `in main.main` ohne Position schrumpft. Der Grund ist die Ablehnung
der Rekursion: eine rekursive Funktion ist per Konstruktion interpretiert. Folge für L20: das
„Einzeiler"-Problem trifft nur nichtrekursiven Code; für L5: jede Tiefenpanik ist interpretiert.

**Der Frame-Pool ist eine unbeschränkte Freiliste pro Funktion**, „bounded by the deepest
simultaneous recursion ever seen" (`Interpreter.cs:1559-1561`); `Rent` allokiert frisch, wenn die
Liste leer ist, `Recycle` legt zurück (ebd. 1567-1602). **Ein Panik-Lauf gibt seine Frames nicht
zurück** — „A panic abandons its frames to the GC instead of recycling them — the backtrace is
built from them after the loop has left" (ebd. 1563-1565). Für einen Host, der viele panikende
Läufe fährt, heißt das: kein Leck, aber jeder Panik-Lauf reallokiert danach alle tiefen Frames
neu. **Nicht gemessen** — ein Panik-Lauf beendet `lyrvm`, die Messung braucht einen Host (L43).

**Eine suspendierte Koroutine liegt im Heap, eine laufende nicht.** `CoroutineChain.Saved` hält
die Frames zwischen zwei Pulls (`src/Lyric.Vm/CoroutineChain.cs:33-38`). Gemessen (P8): 900
ineinander resumierte Koroutinen laufen, 1200 paniken — verschachtelte `resume` stapeln auf
**einem** Interpreter-Stack. Gemessen (Q7b): fallengelassene Ketten lassen den Pool nicht wachsen
(400 000 und 1 600 000 Runden, Peak WS 33,5–34,5 MB in beiden Zweigen, Zeit im Rauschen).

### 1.4 Die Sandbox

Fünf Bits, eine `[Flags] enum Capability : ulong` (`src/Lyric.Core/Capabilities.cs:8-33`),
Zuordnung Modul → Bit in einer Tabelle (ebd. 51-71). Zwei Prüfungen, zusammen dicht:
`LoadedProgram.Load` weist ab, was **mehr deklariert als gewährt** ist (`LoadedProgram.cs:77-82`),
`NativeRegistry.Bind` weist ab, was **mehr benutzt als deklariert** (`NativeRegistry.cs:180-188`).
Gemessen (Q12b): `import std.io.file` unter `--grant none` stirbt beim Laden mit `LYR-CAP0001`.

**Was die Sandbox nicht kann:**

| Lücke | Beleg |
|---|---|
| Keine Speichergrenze. `ExecutionBudget` zählt **Instruktionen** (`ExecutionBudget.cs:63-70`); `[1] * n` ist eine Instruktion | Interpreter.cs:963-987 |
| **Und die Schranke, die es gibt, ist keine.** `arrrep` prüft nur `count > int.MaxValue / source.Length`. Gemessen (Q5): `[1] * 2 000 000 000` = **32 GB** läuft auf dieser 31,5-GB-Maschine durch, Exit 0 | gemessen Q5 |
| **Und wenn sie greift, greift der Fehlervertrag nicht.** Gemessen (Q5c): `[1] * int.MaxValue` → `Out of memory.`, keine Panik, Exit `0xE0434352` | gemessen Q5c; Kontrolle Q5b: `LYR-VM0006`, Exit 101 |
| **Eine Instruktion hat keine obere Kostengrenze.** `secureRandom(n)` ist eine Instruktion für bis zu 1 MiB CSPRNG (`NativeRegistry.cs:596-605`, Kappung `1 << 20`); `readAll` liest bis EOF ohne Grenze (ebd. 322-326); `s + s` ist ein Native-Aufruf `std.string.concat` (`FunctionLowerer.cs:1871`, `NativeRegistry.cs:341`) und verdoppelt in 31 Runden auf 2^31 UTF-16-Einheiten; `arrrep` bis 2^31 Elemente | gelesen; L31 |
| Keine Feinheit. `fileAccess` heißt das ganze Dateisystem | Capabilities.cs:12-13 |
| Keine Abschwächung zur Laufzeit | LoadedProgram.cs:73-82 |
| Das Budget ist nur über die Embedding-API erreichbar; `lyrvm --help` kennt keinen Schalter | gemessen |
| Ein Budget **schaltet den JIT ab** (`AllowsCompiledCode`), gemessener Code ist langsam **und ohne Backtrace-Positionen** | Interpreter.cs:246-266; guide 14:285-302 |
| `lyrvm --help` dokumentiert vier von fünf Bits (`file,net,os,process`); der Parser kennt auch `host`/`hostAccess` | gemessen (`--help`); `src/Lyrvm/Program.cs:223`; `src/Lyric.Core/Capabilities.cs:125-128` |

**Was die zweite Fassung als „Modelllücke" führte und was es wirklich ist.** Gemessen (Q12,
`--grant none`): `readLine()` liest stdin, `isInteractive()` fragt die Umgebung,
`secureRandom(4)` zieht Entropie — alle drei ungegattet. **Das ist richtig gemessen und falsch
gerahmt.** Gelesen: `NativeRegistry.cs:314-316` — „No capability: reading stdin, like writing
stdout, is part of the process rather than an access decision. A host that wants to forbid it
passes an empty reader"; `CreateDefault(output, error, input)` injiziert den Reader (ebd.
275-282); `stdlib/std/random.lyr:22-24` — „Capability-free like the rest of the module: the
source reaches no file, no clock and no network — an embedded host loses nothing by a guest that
can draw good dice". Beides sind **dokumentierte Entscheidungen mit Host-Mitigation**. Was
tatsächlich inkonsistent ist: **`isInteractive` liest `Console.IsInputRedirected` und
`Console.IsOutputRedirected` direkt** (ebd. 333-335) und ignoriert den injizierten Reader/Writer —
ein Host, der einen leeren Reader übergibt, bekommt trotzdem die Antwort des Prozesses. Das ist
L27, und es ist eine Doku- plus eine Kleinreparaturfrage, keine Sandbox-Lücke.

Dass das Budget *gezählt* und nicht *getaktet* ist, ist eine ausdrückliche Entscheidung
(`ExecutionBudget.cs:13-16`), der Stop ist eine Panik, damit ein feindliches Skript ihn nicht
aussitzt (ebd. 56-63), und das Budget ist ein **Objekt**, weil `Consumed` die einzige Art ist, an
eine sinnvolle Zahl zu kommen (ebd. 17-23) — ein Argument, das für ein Speicherbudget genauso
gilt (L24). Guide 14:266 sagt ausdrücklich: „What it bounds is the script's own loops" — die
Arbeit eines Natives bindet es nicht (L31).

### 1.5 Ressourcen

Eine Datei ist ein **`opaque type File = int`** — ein Schlüssel in `Dictionary<long, FileState> _files`
(`stdlib/std/io/stream.lyr:41`, **`NativeRegistry.cs:2344-2347`** — die zweite Fassung zitierte
`:1440-1470`, das ist die **Socket**-Tabelle `Dictionary<long, Socket> _sockets`, :1438-1441;
die Aussage war richtig, die Stelle falsch). Der Schlüssel wächst monoton (`_nextFileKey`),
`streamClose` antwortet auf einen unbekannten oder geschlossenen Schlüssel still (ebd. 2581-2584).
Damit liegt das Handle **außerhalb des GC**, nichts gibt es frei außer `close` oder
`LangVm.Dispose`; Guide 14: „A script has no obligation to close what it opens", „There is no
finalizer" (`docs/guide/14-embedding.md:475, 488`).

**Gemessen (P7):** 5000 `open` ohne `close` → Handlecount ~200 → **10231**.
**Gemessen (Q14, mit Kontrolle):** ein `defer` läuft **nicht** bei einer Panik — per Spec
(`spec/07-statements.md:130-132`) und als Sicherheitseigenschaft begründet (`ExecutionBudget.cs:56-61`).
Dieselbe Spec-Stelle: ein werfender `defer` ist unspezifiziert (`LYR-SEM0110`, 5.0 entscheidet);
und `spec/10-coroutines.md:89-93`: „suspension is not an exit … the garbage collector is not an
exit path" — die Handles einer suspendierten Koroutine bleiben offen, **egal was L10 entscheidet**.

Zusagen aus Guide 14, die das Gebiet erbt: „Two VMs never share a descriptor" (ebd. 494-496),
`Dispose` darf von einem anderen Thread laufen (ebd. 504-507). Nicht zugesagt: was gilt, wenn
zwei VMs dasselbe **Host-Objekt** halten (L29).

### 1.6 Numerik

Operatoren festgeschrieben: Wickeln im Zweierkomplement, `/ 0` panikt, `min / -1` wickelt
(`spec/03-types.md:49-64`); `float → int` trunkiert und **sättigt**, `NaN → 0` (ebd. 163-164).

**Die Bibliothek ist zur Hälfte deterministisch.** Gelesen (`NativeRegistry.cs:607-657`):
`sqrt`, `abs`, `floor`, `ceil`, `round` (ToEven), `min`, `max` sind IEEE-754-vorgeschrieben bzw.
korrekt gerundet und damit bitgleich; **transzendent und plattformdefiniert** sind `pow` (625),
`sin` (628), **`cos` (630)**, `tan` (632), `log2` (644), `log10` (646), `asin`, `acos`, `atan`,
`atan2` (648-654), `log` (657). `nextGaussian` rechnet `sqrt(0.0 - 2.0 * log(u1)) * cos(tau * u2)`
(`stdlib/std/random.lyr:120`): gefährdet sind **`log` UND `cos`** — die zweite Fassung nannte
beide in §1.6 und vergaß `cos` dann in L11/D. Korrigiert.

**Gemessen (P12):** Interpreter und JIT rechnen bitgleich über `f32`- und `f64`-Ketten.
**Gemessen (R3i, neu):** `0.1 * 10.0 - 1.0` ist in beiden Engines `0` für `float` **und**
`float32` — ein kontrahiertes FMA hätte `5.55e-17` bzw. `-1.49e-8` geliefert. Der JIT emittiert
typisierte `float`-Locals und `Ldc_R4` (`JitCompiler.cs:945, 1158`); dass RyuJIT `a*b+c` nicht
implizit fusioniert, ist eine Eigenschaft von .NET (**behauptet**, nicht im Repo belegbar; die
Messung zeigt es für diesen Fall). Für das zweite Runtime (Lyricpp, C++) ist das die relevante
Frage, weil C++-Compiler per Default kontrahieren dürfen (L44).

**Map-Reihenfolge.** Gemessen (Q4): byteidentisch über Läufe, Prozesse und Engines, obwohl
„UNSPECIFIED" (`stdlib/std/collections.lyr:544-546`); `string.hash` ist FNV-1a ohne Seed
(`stdlib/std/core.lyr:300-316`). HashDoS-Fläche — aber **nicht** der einzige Weg, Host-Zeit ohne
Capability zu verbrennen (§1.4, L31). Siehe L23.

### 1.7 Wo Spezifikation, Guide und Implementierung auseinandergehen

| # | Befund | Beleg | Art |
|---|---|---|---|
| A | **Die Wertsemantik hat ein Loch, und es ist eine Familie von fünf.** Kopiert wird beim ersten Einpacken eines NACKTEN Structs. Nicht kopiert: (1) Binden/Herauslesen/Übergeben eines `?Struct`, (2) dasselbe für einen Interface-Wert, (3) **das Einfangen eines `let`-Structs in eine Closure**, (4) **ein `?Struct`-Feld in der Struct-Kopie**, (5) **ein Interface-Feld in der Struct-Kopie** | Q13, Q2, **R3e, R3a**, je mit Kontrolle; FunctionLowerer.cs:2914-2917, 3036-3048, 366-380; Interpreter.cs:1161-1163 | (1)(2)(4)(5) still falsch gegen Guide 05; **(3) Spec-Bruch gegen 07:82** |
| B | **`let` macht ein Struct nicht unveränderlich** | P13; spec/03-types.md:97-121, `LYR-SEM0109` | benannt, 5.0 entscheidet |
| C | **Ein Skalar darf konform sein, aber kein Interface-Wert werden**, mit einer Diagnose, die die Regel nicht nennt | P3/P3b/P3c; spec/05-interfaces.md:107-111 | unvollständig gemeldet |
| D | **`float32` hat keine eigene Darstellung im f-String** | Q8/P12b; spec/11:18-24 | Spec-Lücke |
| E | **§3.1-Kontext erreicht `[x] * n` nicht** | P5b | Lücke |
| F | **1025 Frame-Zeilen** ohne Elision | Q15, R3g | Ergonomie |
| G | **Kopiertiefe im Guide nicht gesagt** — und im VM-Kommentar ohne `?Struct` | P1, R3a; guide 05:5; Interpreter.cs:1145-1147 | Doku-Lücke |
| H | **Backtrace hängt an der Engine — für nichtrekursiven Code** | Q6/Q6b, **R3h** | im Quelltext benannt, in Spec/Guide nicht |
| I | **OOM verlässt die VM als .NET-Ausnahme** | Q5c; src/Lyric.Vm/VmHost.cs:36-58, src/Lyric.Embedding/LangVm.cs:268-283 | Vertragslücke |
| J | **`isInteractive` ignoriert die injizierten Streams** — die Capability-Freiheit von Konsole und `secureRandom` dagegen ist dokumentierte Entscheidung | NativeRegistry.cs:314-316, 333-335; random.lyr:22-24 | kleine Inkonsistenz; **keine** Modelllücke |
| K | **Zwei Grenzen, ein Code.** Rekursionstiefe und Re-Entry-Tiefe paniken beide als `LYR-VM0004`; Anhang A beschreibt nur die erste | Interpreter.cs:192-194, 533; appendix-a:278 | Spec-Lücke |
| L | **Der JIT lehnt Rekursion ab**, und das steht im Guide, aber nirgends als Vertrag | JitCompiler.cs:721-724; guide 14:308; R3h | Guide-Satz, kein Vertrag |
| M | **`spec/03:77` und `spec/07:82` beschreiben Captures verschieden** („by reference" / „`let` as its value") | gelesen | Formulierungslücke |

A und B haben seit 4.6.0 eine Uhr (`LYR-SEM0107`…`0110`, `spec/12-diagnostics.md:88-91`), und
§3.4a nennt `mut struct` als „the candidate on the table" (`spec/03-types.md:114-121`).

---

## 2. Sprachvergleich

Sechs Zellen der zweiten Fassung waren falsch oder schief (Go-Stackgröße, Go-Interface-Modell,
Go-Boxing, C# `checked`-Formen, Swift- und C#-Gleichheit, Java-Treeification); sie sind hier
korrigiert und im Schlussabschnitt benannt.

| Sprache | Speicher | Wertdarstellung | Stack / Rekursion | Ressourcen-Ende | Grenze für fremden Code | Ganzzahl-Überlauf |
|---|---|---|---|---|---|---|
| **Lua 5.4** | inkrementeller Mark-Sweep, optional generationell; **Allokator kommt vom Host** (`lua_newstate`) | `TValue` = Union + expliziter Tag, 16 B | Coroutine = eigener `lua_State` mit wachsendem Stack; C-Ebene bei `LUAI_MAXCCALLS` (200); **Proper Tail Calls garantiert** (`return f(x)`) | `__gc`-Metamethode auf Userdata = echter Finalizer | Host lädt nur die Bibliotheken, die er will; ein Allokator, der `NULL` gibt, wird zu `LUA_ERRMEM`, über `lua_pcall` fangbar | wickelt (64-Bit-Integer seit 5.3) |
| **Wren** | Mark-Sweep mit `initialHeapSize`/`minHeapSize`/`heapGrowthPercent`; Allokator vom Host | NaN-Boxing, 8 B | Fibers mit eigenem, wachsendem Stack | `finalize`-Callback auf Foreign Classes | Modul-Loader-Hook + Foreign-Methoden-Binder | wickelt nicht — nur `double` |
| **MicroPython** | Mark-Sweep über einen **festen Heap**; `gc.collect()`, `gc.mem_free()` **in der Sprache**; kein Refcount | getaggte Zeiger, Small-Int im Zeiger | C-Stack-Prüfung vom Port gesetzt, vom Programm nicht änderbar | optional `MICROPY_ENABLE_FINALISER` | keine; die Grenze ist das Gerät | Bignum |
| **CPython** | Refcount **plus** Zyklen-GC; `MemoryError` fangbar | `PyObject` mit Typzeiger | `sys.setrecursionlimit` (1000), `RecursionError` fangbar; **kein** TCO, ausdrücklich abgelehnt | Refcount macht `close` bei azyklischen Objekten deterministisch | keine ernsthafte | Bignum |
| **Java** | generationell; `-Xmx` → `OutOfMemoryError` fangbar | Primitive untagged, Objekte mit Header; Generics gelöscht → Boxing (`Integer`-Cache −128…127) | `-Xss`, `StackOverflowError` fangbar | Finalisierung seit JEP 421 zur Entfernung deprecated, nicht entfernt; `Cleaner` | `SecurityManager` JEP 486 dauerhaft abgeschaltet | wickelt; `Math.addExact` als Funktion |
| **Go** | präziser nebenläufiger GC, `GOGC`, **`GOMEMLIMIT`**; OOM fatal | Interface = **(itab, data)** — ein Zeiger auf eine pro (Interface, Typ) vorberechnete Methodentabelle; Lyric speichert stattdessen einen **Typindex** und löst bei `callvirt` auf (`LyrValue.cs:70-75`): **gleiche Form, anderer Mechanismus**. Beim Einpacken boxt Go — **außer** für Ganzzahlwerte 0…255 jeder Breite (`convT16/32/64` greifen seit Go 1.15 auf `runtime.staticuint64s`), Ein-Byte-Werte und Null-Größen-Werte | Goroutine-Stack startet bei **2 KB (seit 1.4; 8 KB war 1.2/1.3), seit 1.19 adaptiv** und wächst durch Kopieren bis 1 GB; **kein** TCO | `defer` läuft **auch auf Panic**, `recover` fängt; `SetFinalizer`, `AddCleanup` (1.24), `weak.Pointer` (1.24) | keine | wickelt |
| **C#/.NET** | generationell, `GC.Collect`, `GCSettings.LatencyMode`, `GCHeapHardLimit` per runtimeconfig; `WeakReference<T>` | Structs mit Layout; Generics reifiziert; `Span<T>`, `stackalloc`, `unsafe` | Stack-Overflow ist ein **nicht fangbarer Prozessabbruch**; `.tail`-Präfix in IL existiert, C# emittiert ihn nicht | Finalizer + `IDisposable`/`using` | CAS abgeschafft | **drei Formen**: `checked { }`-Block, `checked(expr)`-Ausdruck, und der **compilerweite Schalter** `<CheckForOverflowUnderflow>` / `/checked` |
| **Erlang/BEAM** | ein Heap pro Prozess; `max_heap_size` **tötet** | alles getaggt | Prozess-Stack wächst; **Proper Tail Calls** (die Sprache lebt davon) | Prozess = Arena | **Reduction-Counting präemptiv; BIFs zahlen Reductions proportional zur Arbeit** (`length/1` zahlt pro Element) | Bignum |
| **WebAssembly (Wasmtime)** | lineares Speicherobjekt mit `maximum`; `memory.grow` scheitert als `-1` | untagged, statisch typisiert | Engine-Limit; Trap mit Backtrace; **`return_call` (Tail-Call-Proposal, Phase 4 seit 2023; V8 seit 2023, Wasmtime seit 2024 per Default)** | Host besitzt alles Externe | **Fuel-Metering im kompilierten Code** | wickelt |
| **Swift** | ARC, kein Tracing-GC | Structs mit Layout; **Copy-on-Write** für Puffer | kein TCO garantiert | `deinit` deterministisch | keine | **trapt** per Default; `&+` wickelt |

**Gleichheit für Structs — richtig zugeordnet.** Die zweite Fassung nannte „Go, C#, Swift" als
Vorbild für ein *automatisches* `==`. Nur **Go** tut das (und verbietet es per Compile-Fehler für
Structs mit Slice-/Map-/Func-Feld). **Swift** synthetisiert `Equatable`/`Hashable` **nur, wenn
der Typ die Konformanz deklariert** (`struct P: Equatable {}` ohne Body) — das ist opt-in per
Deklaration, also Rusts `derive`-Familie, nicht Gos. **C#** gibt einem gewöhnlichen `struct`
reflexionsbasiertes `ValueType.Equals` (langsam), aber **`a == b` ist ein Compile-Fehler
(CS0019)**; nur `record struct` (C# 10) synthetisiert `==`. C# ist damit ebenfalls opt-in, und
zwar per Typ-Schlüsselwort. Siehe L21.

**Java `HashMap` — richtig zitiert.** JEP 180 (Tree-Bins) verlangt **kein** `Comparable`: bei nicht
vergleichbaren Schlüsseln ordnet `tieBreakOrder` über Klassenname und `identityHashCode` und baut
den Baum trotzdem — mit O(log n) nur, wenn die Schlüssel vergleichbar sind, sonst degradiert der
Baum zur Liste in Baumform. Lyric hat weder `identityHashCode` noch Laufzeit-Typinformation
(§1.1), also **keine** dieser Rückfallebenen. Siehe L23.

**Die entgegengesetzte Entscheidung: Erlang.** Ganzzahlen wachsen statt zu wickeln; ein Heap pro
Prozess; die Grenze ist Präemption, nicht Verweigerung — und **Reductions zählen Arbeit, nicht
Instruktionen**: ein BIF, das eine Liste durchläuft, zahlt pro Element. Das ist das Vorbild, das
L31 braucht und das die zweite Fassung nicht hatte.

**Was direkt passt:**

- **Go** ist Lyrics nächster Verwandter (Fat-Pointer-Form, Wickeln, GC, `defer`) — mit den
  genannten Unterschieden: itab gegen Typindex, Small-Value-Ausnahme beim Boxing (das nähere
  Vorbild für L4/B als Javas `Integer`-Cache, weil es ohne Cache-Objekt auskommt).
- **Lua und Wren**: der Host stellt den Allokator; Lua macht den Allokationsfehler fangbar (L19)
  und garantiert Tail Calls (L36 — als Gegenbeispiel: Lua zahlt mit „(...tail calls...)" im
  Traceback).
- **C#** liefert für `checked` **alle drei Formen** — und damit auch das Vorbild für den
  Build-Schalter, den die zweite Fassung nur Rust zuschrieb (L12).
- **Java** liefert `StrictMath`; **Erlang** liefert die arbeitsproportionale Zählung (L31);
  **Wasm** liefert Fuel im kompilierten Code (L8), `memory.grow → -1` (L19) und das
  Tail-Call-Proposal als **opt-in-Instruktion** statt als Compiler-Magie (L36).
- **Swift** liefert CoW (L18) und das Konformanz-ohne-Body-Muster (L21).
- **V8, JVM, .NET, Wasmtime** liefern Positionen in optimiertem Code (L20).

---

## 3. Designfragen

L1–L23 sind die Fragen der zweiten Fassung, überarbeitet; L24–L31 waren dort angekündigt und
fehlten; L32–L44 sind die Fragen, die der zweite Kritiker als fehlend nachgewiesen hat. Am Ende
von §3 steht die Zuordnung seiner Fragen zu den Nummern.

### L1 — Kostet ein Array weiter 16 Byte pro Element?

**Heute:** gemessen 15,3–15,5 B/Element für `int[]`, `uint8[]` **und `char[]`** (P5/P5b, R3j);
ein Slot ist ein `LyrValue` (LyrValue.cs:20-21). Ein `string` kostet 2 B/Einheit (R3j).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | `std.bytes`, `std.io`, `std.crypto`, `std.compress` mit 16× aufgeblähten Puffern; `utf8Encode` macht aus 2 B/Zeichen 16 B/Byte |
| B: nur `uint8[]` packen, hinter demselben Typ | Java `byte[]` | Sonderdarstellung im `ldelem`/`stelem`-Pfad; Verifier; Fallunterscheidung im heißesten Pfad |
| C: alle Skalar-Arrays packen (`int8[]`…`float64[]`, `bool[]`, `char[]`) | C# `T[]`, Go Slices, Wasm | derselbe Eingriff, breiter |
| D: separater `Bytes`-Typ über `TypeTag.Host` | heutiges `opaque type File = int` | kein VM-Eingriff, aber ein **zweiter** Array-Mechanismus — Rule 2 |

**Die Bruchkante ist eine additive Formatänderung**, nicht „kein Bruch": `ldelem`/`stelem` tragen
keinen Operanden (`docs/Bytecode.md:791-792`), `Interpreter.cs:926-940` castet ungeprüft — also
neue bzw. parametrisierte Opcodes mit Versions-Bump; `newarr` trägt den Elementtyp schon
(`Bytecode.md:790`). Ein Laufzeit-Typtest pro Zugriff ist der Tag, den das Wertmodell abgeschafft
hat: nein.

Zusätzlich zu beantworten: `(?uint8)[]` bleibt `LyrValue[]` (der `SomeMarker` hat in einem
gepackten Puffer keinen Platz, `LyrValue.cs:52-57`); generische `T[]` sind unter Monomorphisierung
kein Sonderfall; `arrcat`/`arrrep`/Literale erzeugen die Darstellung; der `Iterator`-Vertrag
(`next(): ?T`) gewinnt beim Puffer, nicht beim Durchlauf; **Host-Natives, die `LyrValue[]`
empfangen**, sehen etwas anderes — L42.

**Empfehlung: C über neue Opcodes, mit B als erster Messung.** **D nicht** — ein Bytepuffer,
der kein Array ist, ist die parallele Mechanik, an der Oil gestorben ist.

**Erwartete Komplexität:** B ~400–700 LOC, zwei bis drei Sessions; C danach ~300 LOC.
**Bricht:** additive Formatänderung; sprachlich nichts; die echte Bruchkante ist die FFI-Oberfläche.
**4.x-Warnstufe:** keine in der Sprache; Deprecation für elementtyp-agnostische Natives.
**Hängt ab von:** FFI-Gebiet, Bytecode-Gebiet, L7, L38, L42.

---

### L2 — Was ist ein `struct` — und wo wird eine Kopie genommen?

**Heute:** ein Struct ist ein Wert beim ersten Einpacken, und **fünf** Wege umgehen die Kopie
(§1.7 A): `?Struct` gebunden/gelesen/übergeben (Q13), Interface-Wert gebunden/gelesen/übergeben
(Q2), **`let`-Struct in eine Closure gefangen (R3e)**, **`?Struct`-Feld in der Kopie (R3a)**,
**Interface-Feld in der Kopie (R3a)**. Ursachen gelesen: drei Lowering-Stellen mit statischem
Quelltyp (`FunctionLowerer.cs:2914-2917, 3036-3048`), `LoadCaptured` ohne Kopie (ebd. 366-380),
`CopyStruct` nur über `Tag == Struct` (`Interpreter.cs:1161-1163`). Gemessen (R3q9, drei
Messanordnungen): die Struct-Schleife bekommt unter `--jit` keinen Gewinn (0,97–0,99×), die
Klassen-Schleife 4,5–7,5×; unter `--jit` liegt eine Größenordnung dazwischen. Eine Funktion,
die ein Struct bindet, wird nie kompiliert (`JitCompiler.cs:679-680`).

| Option | Vorbild | Preis |
|---|---|---|
| A: `structcopy` an **jedem** Bindepunkt, Quelltyp egal — **plus** Kopie beim Fangen (L32) **plus** Rekursion durch `?Struct` in `CopyStruct` (L33) | C# `Nullable<T>` | **Fünf** Stellen (nicht drei, nicht 60 LOC): drei Guards fallen, `LoadCaptured` kopiert bei `let`-Struct, `CopyStruct` rekursiert bei Optional-über-Struct. Jede `?Struct`- und Interface-Bindung allokiert; `struct Node { next: ?Node }` kopiert eine Kette |
| B: **`mut struct`** — unveränderlich, außer es heißt so | F#-Records, Scala `case class`, C# `readonly record struct` | Für alles Unveränderliche entfällt die Frage; braucht `p with { … }`. 5.0-Bruch |
| B+A: `mut struct`, und für `mut struct` die Kopie an jedem Bindepunkt | — | Die einzige Kombination, die die ganze Familie schließt |
| C: `?Struct` verbieten | — | Verbietet Listen und Bäume aus Structs. Nein |
| D: Copy-on-Write | Swift | zweiter Speichermechanismus — Rule 2 |

**Was B+A für Schreibpfade durch `?T` bedeutet — die Lücke, die die zweite Fassung offenließ.**
Gemessen (Q13): `w!.n = 99` schreibt heute in das gewrappte Struct. Unter A wäre das Auspacken
in einer Bindung eine Kopie — `let x = w!; x.n = 99` schreibt in die Kopie, richtig. Aber
`w!.n = 99` **ist keine Bindung**, es ist ein Lvalue-Pfad, und die Regel muss lauten: **nur eine
rvalue-Bindung kopiert; ein Feldpfad (`w!.n`, `a.opt!.n`, `xs[i].n`) ist ein Write-through**,
wie heute bei `xs[i].n = …` auf einem Struct-Array (P13 zeigt, dass Elementmutation zurückschreibt).
Das ist genau Gos Regel für `p.next.x = …` über einen Zeiger; C# verbietet den Fall über
`Nullable<T>` (`w.Value.n = 99` ist ein Compile-Fehler, weil `Value` ein rvalue ist). Lyric muss
sich entscheiden, und ich empfehle Gos Lesart, weil sie das heute gemessene Verhalten erhält:
`!` als Lvalue-Segment ist ein Write-through, `!` als rvalue-Bindung kopiert.

**Empfehlung: B plus A für `mut struct`, mit der Write-through-Regel für Lvalue-Pfade.** Die
Regel in einem Satz: *ein Struct ist ein Wert; ein `mut struct` wird an jedem rvalue-Bindepunkt
kopiert — Bindung, Argument, Rückgabe, Capture, Auspacken, Feld einer Kopie —, ein Lvalue-Pfad
schreibt durch, und ein gewöhnliches Struct braucht keine Kopie.* A allein ist die ehrliche
kleine Lösung — aber sie hat **fünf** Stellen, und der Capture-Fall ist sofort zu reparieren,
weil er ein Spec-Bruch ist (L32).

**Erwartete Komplexität:** A ~150 LOC (fünf Stellen, Tests für jede). B: 1200–1800 LOC, drei
bis fünf Sessions plus Bibliotheksmigration.
**Bricht:** **major** (jedes Struct mit `mut fn` oder geschriebenem Feld braucht das Wort).
**4.x-Warnstufe:** `LYR-SEM0108`/`0109` stehen; 4.7 ergänzt eine Warnung an jeder `mut fn` eines
`struct`.
**Hängt ab von:** `p with { … }` (Ergonomie-Gebiet, v5-Liste #12); L21; L28; L32; L33; L35
(der JIT-Satz); VM-Gebiet.

---

### L3 — Was bedeutet `let` für ein Struct?

**Heute:** nichts für den Wert. Gemessen (P13): `let l = Zaehler{n=0}; l.hoch();` liefert `1`;
`spec/03-types.md:110-113` nennt es eine Konsequenz „with no obvious reading"; `LYR-SEM0109` seit
4.6.0. Und gemessen (R3e): ein `let`-Struct in einer Closure ist ein Alias — `let` schützt auch
dort nichts.

| Option | Vorbild | Preis |
|---|---|---|
| A: `let` verbietet `mut fn` und Feldschreiben | Rust `let` vs `let mut` | Zwei Unveränderlichkeitsbegriffe unter einem Wort |
| B: fällt mit `mut struct` weg (L2/B) | F#, C# `readonly struct` | — |
| C: so lassen, in §7 dokumentieren | Go | `let` heißt dann „die Bindung" |

**Empfehlung: B**, hilfsweise A. **Bricht:** klein bis mittel. **4.x-Warnstufe:** `LYR-SEM0109`.
**Hängt ab von:** L2, L32.

---

### L4 — Darf ein Skalar ein Interface-Wert werden?

**Heute:** nein. Gemessen (P3/P3c): `extend int :: [Sized]` angenommen, `7.size()` und der
Constraint-Pfad funktionieren, `let s: Sized = n;` ist `LYR-SEM0001` „cannot assign" ohne die
Regel im Text. Regel: `spec/05-interfaces.md:107-111`; Ursache: `FromInterface` nimmt `instance.Ref`,
bei einem Skalar null (LyrValue.cs:70-75).

**Die Vorfrage der zweiten Fassung ist jetzt gemessen**, mit den Mitteln, die der Kritiker zu
Recht verlangt hat: `grep -rn ": Display\b\|Display\[\]\|: Equatable<" stdlib/ --include=*.lyr`
liefert **zwei** Treffer, beide Kommentare (`stdlib/std/core.lyr:94, 99`), die die Ablehnung als
**Entscheidung** dokumentieren: „A value held through the interface (`let d: Display = 5;`)
stays rejected: a fat pointer needs a …". Die stdlib selbst packt **nirgends** einen Skalar in
einen Interface-Wert; alle zwölf `extend <skalar> :: […]` (ebd. 106-231) bedienen den
Constraint-Pfad. Erato ist nicht im Checkout — dort **behauptet**: ein Spiel-Register arbeitet
mit Handles und Structs, nicht mit `Display`-Sammlungen.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen, **eigene Diagnose** mit der Regel im Text | — | Eine Zeile |
| B: Boxing beim Einpacken, **mit Gos Ausnahme** (Ganzzahlen 0…255 und `bool` aus einer statischen Tabelle, kein Cache-Objekt) | **Go `staticuint64s`** — nicht Javas `Integer`-Cache, der ein Objekt pro Wert hält | Eine Allokation pro Einpacken jenseits von 0…255 |
| C: `LyrValue` auf drei Felder | — | 24 B statt 16 — nein |
| D: `extend <skalar>` verbieten | — | verbietet den funktionierenden Constraint-Pfad — nein |

**Empfehlung: A, und zwar als 4.7-Fix; B nicht, bis ein Anwendungsfall aus Erato oder einer
Anwendung ihn zeigt.** Die Messung zeigt keinen Bedarf, und `core.lyr:94-99` zeigt, dass die
Grenze gewollt ist. Was fehlt, ist nur die Diagnose.

**Bricht:** nein. **4.x-Warnstufe:** keine. **Hängt ab von:** L15, L21.

---

### L5 — Bleibt `MaxCallDepth` eine Konstante von 1024?

**Heute:** `private const int MaxCallDepth = 1024` (`Interpreter.cs:139`), weder vom Host noch
von der CLI setzbar. Gemessen (Q15, R3g): 1020 läuft, 1500 panikt, Exit 101, 1025 Frame-Zeilen;
**ein Tail Call zählt** (R3g). Gemessen (R3h): jede Tiefenpanik ist interpretiert, weil der JIT
Rekursion ablehnt — der Backtrace ist immer vollständig. **Und die Grenze ist thread-weit**:
`outer + frames.Count >= MaxCallDepth` (`Interpreter.cs:533`) zählt die Frames aller äußeren Läufe
mit (§1.3, L30) — die zweite Fassung wusste das nicht, und ihre Option B setzte eine Entkopplung
voraus, die es nicht gibt.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Ein rekursiver Baumdurchlauf über 2000 Knoten ist unmöglich |
| B: konfigurierbar — **als Obergrenze der VM unter der Thread-Grenze**: `HostOptions.MaxCallDepth` und `lyrvm --max-depth`, beide ≤ dem thread-weiten Zähler | Python `sys.setrecursionlimit`, Java `-Xss` | Ein Feld — aber die Zahl bedeutet „diese VM darf höchstens so viele Frames beitragen", nicht „so tief darf der Thread"; wer sie über die Thread-Grenze setzt, bekommt trotzdem die Thread-Grenze |
| C: wachsender Frame-Stack, Grenze nur über Speicher | Go, Lua, Wren | Die Frames liegen im Heap; die „Grenze" wäre L7. Ohne L7 und L19 ersetzt es eine Diagnose durch ein OOM |
| D: B **und** wiederholte Frames im Backtrace zusammenfassen | Python „[Previous line repeated N more times]" | Klein, sofort sichtbar |

**Nach unten** bricht B fremden Code (Bibliotheken, die mit 1024 auskommen) mit `LYR-VM0004` an
einer Stelle, die der Bibliotheksautor nie gesehen hat; die Diagnose muss den gesetzten Wert
nennen.

**Empfehlung: B + D jetzt, mit der Thread-Grenze als ausdrücklichem Deckel; C nur mit L7 und
L19.** Und L30 zuerst, weil B ohne ein klares Wort darüber, wem der Zähler gehört, eine Zahl mit
zwei Bedeutungen ist.

**Bricht:** nein; gesenkt bricht es fremden Code. **4.x-Warnstufe:** keine. **Hängt ab von:**
L30 (zuerst), L7, L19, L6, L36.

---

### L6 — Was kostet eine rekursive Koroutine?

**Heute:** dasselbe wie eine rekursive Funktion. Gemessen (P8): 900 laufen, 1200 paniken.
Suspendierte Frames im Heap (`CoroutineChain.cs:33-38`), laufende auf **einem** Frame-Stack.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen, dokumentieren | Python | Ein rekursiver Generator ist bei ~1024 Ebenen am Ende |
| B: Grenze pro Kette | — | Ob Ketten-Frames wirklich nicht auf dem CLR-Stack liegen, ist unbelegt; und die Thread-Grenze (L30) gilt ohnehin |
| C: fällt mit L5/C weg | Lua | — |

Für **gleichzeitig suspendierte** Ketten gibt es keine Grenze außer dem Speicher (L25).

**Empfehlung: A bis L5, dann C.** **Bricht:** nein. **Hängt ab von:** L5, L25, L30.

---

### L7 — Bekommt die Sandbox eine Speichergrenze?

**Heute:** nein. Gemessen (Q5): 32 GB laufen durch; (Q5c): darüber rohe OOM (L19).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Ein Mod mit Budget 1000 kann den Host-Prozess killen |
| B: **`MemoryBudget` neben `ExecutionBudget`**, in Bytes, belastet an jeder Allokationsstelle: `newobj`, `newarr`, `arrrep`, `arrcat`, `newvariant`, `structcopy`, Closure-Umgebung, Frame-Pool-Miss, **`std.string.concat` und die f-String-Kette, `fromInt`/`fromFloat`, jedes `Bytes()`- und String-Rückgabe eines Natives (`readAll`, `secureRandom`, `utf8Encode`)** | Erlang `max_heap_size`, Go `GOMEMLIMIT`, Wasm `memory.maximum` | Ein Zähler an ~12 Stellen, nicht 6. Zählt **allokiert**, nicht **lebend** |
| C: Host stellt den Allokator | Lua, Wren | im .NET-GC nicht machbar |
| D: eine VM pro Gast mit eigenem Heap | Erlang, Worker-Isolates | die saubere und die teuerste Antwort |
| E: nur die Einzelallokation deckeln (`maxAllocationBytes`) — **an denselben ~12 Stellen**, sonst ist es keine | Wasm `memory.maximum` | Billig; fängt die gemessenen 32 GB **und** die Verdopplung `s = s + s` erst, wenn Strings dabei sind — die zweite Fassung hatte sie vergessen, und ihr E hätte den einfachsten Fall nicht gefangen |

**Empfehlung: B, mit E als Sofortmaßnahme — beide über die vollständige Liste — und L24 als
Bestandteil.** Die Liste ist der Punkt: ein Budget, das `newarr` zählt und `concat` nicht, ist
eine Einladung. Und L31 gehört dazu: ein Byte-Zähler löst das Speicherproblem, nicht das
Arbeitsproblem (`secureRandom(1 MiB)` ist eine Instruktion **und** 1 MiB — das Budget muss beides
sehen).

**Bricht:** nein. **Hängt ab von:** L1, L19, L24, L25, L31, L38, Embedding-Gebiet.

---

### L8 — Wird das Budget von außen erreichbar, und bleibt es JIT-feindlich?

**Heute:** nur über die Embedding-API; ein Budget schaltet den JIT ab (`Interpreter.cs:246-250`).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | `lyric run` eines fremden Skripts kann nichts begrenzen |
| B: `lyrvm run --budget N` und `--memory N` | Deno `--allow-*` | Zwei Schalter |
| C: **Fuel-Metering im kompilierten Code**, ein Zähler pro Basisblock | Wasmtime `Store::set_fuel` — nicht V8 (Präemption), nicht LuaJIT (fällt auf den Interpreter zurück) | Garantie wird „innerhalb eines Blocks" grob; braucht eine statische Kostenfunktion |
| D: zusätzlich Wanduhr | Go `context` | Rule 2 |

**Der Einwand bleibt:** C legt den gemeteten Lauf auf die Engine ohne Backtrace-Positionen (Q6b)
— **für nichtrekursiven Code**; rekursiver bleibt ohnehin interpretiert (R3h, L35). Wer C will,
entscheidet L20 mit.

**Empfehlung: B jetzt, C für 5.0 nur mit L20. D nicht.** **Bricht:** nein. **Hängt ab von:**
L20, L35, VM/JIT-Gebiet, CLI-Gebiet.

---

### L9 — Bleiben Capabilities fünf Bits für ganze Subsysteme?

**Heute:** ja (`Capabilities.cs:8-33, 51-71`), geprüft beim Laden und gegen die Importe
(`LoadedProgram.cs:77-82`, `NativeRegistry.cs:180-188`), für den Lauf unveränderlich.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | `fileAccess` heißt das ganze Dateisystem |
| B: **parametrisierte Capabilities** — `fileAccess:read=/data,write=/saves`, Host-Seite, Prüfung im Native | Deno `--allow-read=/tmp`, WASI Preopens | Kein Formatbruch |
| C: Objekt-Capabilities (`Dir`-Handle statt Bit) | E, Pony, WASI | ändert jede I/O-Signatur; Milestone-groß |
| D: Abschwächung zur Laufzeit | **Deno `Deno.permissions.revoke()`** | Eine Prüfung an einer Grenze |

Javas Scheitern (JEP 411/486) folgte aus Stack-Walking-Policy, nicht aus der Unmöglichkeit von
Abschwächung; Deno liefert sie, weil die Prüfung an genau einer Syscall-Grenze sitzt.

**Die Vorbedingung für D, neu gefasst.** Die zweite Fassung sagte: „L27 sagt, dass nicht jede
Außenwirkung durch `NativeRegistry` geht (`std.io.console`, `secureRandom`)". Das stimmt so
nicht — **beides geht durch `NativeRegistry`** (`NativeRegistry.cs:314-335, 596-605`); es ist
nur kein Bit davor, **per Entscheidung** (§1.4). D ist damit *machbar*: jede Außenwirkung sitzt
in einem Native. Was L27 klären muss, ist, ob Konsole und Entropie unter „Außenwirkung" fallen
sollen — und die Antwort des Quelltexts ist nein.

**Empfehlung: B für 5.0; D offen, mit Deno als Vorbild, und ohne die falsche Vorbedingung.**
**Bricht:** nein. **Hängt ab von:** L27, Embedding-Gebiet, stdlib-Gebiet.

---

### L10 — Wer schließt, was ein Skript öffnet?

**Heute:** die VM, und sonst niemand. Handle = `opaque type … = int` in `_files`
(`stream.lyr:41`, `NativeRegistry.cs:2344-2347`), kein Finalizer (guide 14:488). Gemessen (P7):
5000 offene Dateien bleiben offen. Gemessen (Q14): `defer` läuft nicht bei Panik (Spec 07:130-132).
**Und unabhängig von jeder Option hier:** die Handles einer **suspendierten Koroutine** bleiben
offen, „the garbage collector is not an exit path" (`spec/10-coroutines.md:89-93`).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen — die VM ist die Arena | Erlang | eine Größe zu grob (`lyrtest` pro Datei, `STATUS.md:2226-2236`; Server-Skript leckt bis Neustart) |
| B: `defer` läuft **auch** auf dem Panik-Pfad | Go, Java `finally` | kehrt `ExecutionBudget.cs:56-61` um („no defer gets to run afterwards" ist dort eine Sicherheitseigenschaft); nur mit einem **bezifferten** Restbudget (L26) — und es hilft **nicht** für Koroutinen |
| C: `with (let f = open(p)) { … }` | Python, C# `using` | zweiter Cleanup-Mechanismus — Rule 2 |
| D: **Sub-Arena** in der Embedding-API: `vm.Scope()` gibt ein `IDisposable`, das nur die darin geöffneten Handles schließt | Erlang-Prozess, Wasm-Store | löst `lyrtest` und den Server-Fall ohne Sprachänderung; **ein** Mechanismus, zwei Granularitäten — die Antwort auf die Rule-2-Frage in `STATUS.md:2231-2234` |
| D′: D **plus** CLI-Gegenstück — ein `lyrvm run` ist eine Arena pro Lauf | — | ohne das adressiert D die gemessenen Standalone-Fälle nicht |
| E: Finalizer | CPython, Lua, Go | Guide 14:486-492 begründet das Nein; die Begründung hält |

**Empfehlung: D′ allein. B nur, wenn L26 ein Restbudget beziffert, das der Maintainer tragen
will — und ich empfehle es nicht.** Die zweite Fassung empfahl „D′ plus B unter L26" und L26
existierte nicht. Mit L26 ausgeschrieben zeigt sich: B kauft für den Panik-Pfad einer
*Funktion* etwas, das D′ ohnehin liefert (die Arena schließt), zahlt mit einer Regelumkehr in
der Spec und einem zweiten Budget, und lässt den Koroutinenfall offen. D′ deckt alle drei Fälle
— Panik, Budget, suspendierte Kette — mit einem Mechanismus.

**Bricht:** D′ nein. B **minor** (Regeländerung mit `since:`-Konformanzfall).
**4.x-Warnstufe:** keine. **Hängt ab von:** L26, L17, Fehler-Gebiet, Nebenläufigkeits-Gebiet.

---

### L11 — Wie weit reicht „deterministische Numerik"?

**Heute:** bis zum Operator (§3.2, §3.6) und in der Bibliothek bis `sqrt/abs/floor/ceil/round/min/max`;
transzendent und plattformdefiniert sind `pow`, `sin`, `cos`, `tan`, `log`, `log2`, `log10`,
`asin`, `acos`, `atan`, `atan2` (`NativeRegistry.cs:625-657`). Interpreter und JIT bitgleich (P12,
R3i). `nextGaussian` braucht **`log` und `cos`** (`random.lyr:120`).

| Option | Vorbild | Preis |
|---|---|---|
| A: §11 sagt „transzendente Funktionen sind plattformdefiniert" **mit Liste** | Go, C, Rust | kostenlos, ehrlich |
| B: `std.math.strict` mit portierter fdlibm-Ebene (`sin`, `cos`, `exp`, `log`, `pow`…) | Java `StrictMath` | zweites Modul, nicht zweiter Operator |
| C: `std.math` wird strikt | — | jedes Spiel zahlt |
| D: **`log`, `exp`, `sin` UND `cos` in Lyric** implementieren | — | Die zweite Fassung nannte nur `log` und `exp` und hätte `nextGaussian` **nicht** deterministisch gemacht, weil `cos` transzendent bleibt (`NativeRegistry.cs:630`). Mit `cos` ist D eine halbe fdlibm-Portierung (Argumentreduktion, Minimax-Polynome) — also B unter anderem Namen |
| E: `nextGaussian` auf **Inverse-CDF mit ganzzahliger Tabelle** umstellen — **keine** Transzendente im Pfad | — | Ändert die Zahlenfolge; ~120 LOC |
| F: die Zusage im Modulkopf zurückziehen | — | ehrlich, repariert nichts |

**Empfehlung: A für 4.7; E für `std.random` in 5.0 (das schließt die Lücke des Moduls
vollständig, weil danach weder `log` noch `cos` im Pfad sind); B nur bei Bedarf; D gestrichen.**
Ziggurat bleibt gestrichen (Schwanz und Keil brauchen `log`/`exp`).

**Bricht:** E **minor** (gepinnte Zahlenfolgen). **Hängt ab von:** stdlib-Gebiet, L23, L44.

---

### L12 — Kommt `checked { … }`?

**Heute:** nein; §3.2 kündigt „a new construct" an (`spec/03-types.md:62-64`). v5-Liste #17.

| Option | Vorbild | Preis |
|---|---|---|
| A: nichts | Go, Lua | Handprüfung |
| B: `checked { … }`-Block | C# Block-Form | Sema-Flag; **Format-Zuwachs** (Opcodes `add.ovf`… oder Flag) |
| C: Funktionen `addChecked(a,b): ?int`, `addSaturating`, `addWrapping` | Rust `checked_add`, Java `Math.addExact` | keine Sprachänderung |
| D: `Checked<int>`-Typ | — | schlechter als B |
| B+C+Schalter | **Rust** (`-C overflow-checks`) **und C#** (`/checked`, `<CheckForOverflowUnderflow>`) — die zweite Fassung schrieb den Schalter nur Rust zu; C# hat ihn ebenso, plus die Ausdrucksform `checked(a + b)` | — |

**Empfehlung: C als Sprachform, plus ein Profil-Schalter `overflow-checks`.** Zwei Dinge, die
die zweite Fassung nicht sagte:

1. **Der Schalter ist ein Format-Zuwachs**, nicht „kein Zuwachs": ein Modul, das bei jedem `+`
   paniken soll, braucht entweder eigene Opcodes oder ein Flag am vorhandenen. Der Zuwachs
   wandert vom Parser ins Format; er verschwindet nicht. Ehrlich: **additiv, mit Versions-Bump**.
2. **Pakete.** M37-Pakete liefern kompiliertes Lyric. Eine mit Checks gebaute Bibliothek panikt
   in Code, den der App-Autor nicht sieht; eine ohne Checks gebaute schweigt in einer geprüften
   App. Rust hat dasselbe (crate-weise) und lebt damit. Regel: **das Verhalten ist beim
   Kompilieren des Moduls fixiert, und `lyric.json` des Pakets nennt es** — kein Versuch, es
   nachträglich umzuschalten.

**Bricht:** nein (additiv, Format-Bump). **Hängt ab von:** Typ-Gebiet, stdlib, Build-Gebiet,
Bytecode-Gebiet, Spec §3.2.

---

### L13 — Gibt es in Lyric 5 das Wort `unsafe`?

**Heute:** zwei Papiere. `STATUS.md:2466-2470`: Nein, mit Messung (~1 %). `design/abi.md:41, 75`:
`unsafe`-Block als einzige Stelle für `CPtr`-Zugriffe plus `ffiAccess`.

| Option | Vorbild | Preis |
|---|---|---|
| A: kein `unsafe`, keine C-ABI | — | kein zlib/sqlite/GTK standalone |
| B: `unsafe` nur für FFI, nie für Bounds-Checks | **C#** (`unsafe` = Zeiger; FFI ohne `unsafe`) — **nicht Rust** (dessen `unsafe` auch `get_unchecked` freischaltet) | genau eine Bedeutung |
| B′: kein `unsafe`-Wort; `extern "c"`-Block plus `ffiAccess` | C# `LibraryImport`, Java Panama (`--enable-native-access`) | vermeidet das Wort ganz |
| C: `unsafe` für beides | — | von `STATUS.md` widerlegt |

**Empfehlung: B′, nach der Bibliotheksfrage aus `PLAN.md`.**
**Bricht:** reserviert **major**, kontextuell nichts, B′ nichts. **Hängt ab von:** FFI-Gebiet.

---

### L14 — Hat `float32` eine eigene Darstellung?

**Heute:** nein. Gemessen (Q8): `float32 0.1` → `0.10000000149011612`; Wert als `f32` gespeichert
(`LyrValue.cs:33, 126`), `fromFloat` bekommt `f64`. §11 spricht nur über `float` — Lücke.

| Option | Vorbild | Preis |
|---|---|---|
| A: `fromFloat32` als Native, vom f-String gewählt, §11 sagt es | C# `Single.ToString()`, Go `FormatFloat(…, 32)` | klein |
| B: §11 ergänzen: „rendert als der `float`" | — | macht eine Eigenschaft zur Regel, die niemand will |
| C: `float32` streichen | — | nein |

**Empfehlung: A.** **Bricht:** **minor**. **Hängt ab von:** Formatierungs-Gebiet, L44.

---

### L15 — Darf man einen Interface-Wert wieder herunterprüfen?

**Heute:** nein (v5-Liste #13). Der Fat Pointer trägt den Typindex (`LyrValue.cs:70-75`).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Einbahnstraße |
| B: `match (x) { c: Circle => … }` auf Interface-Werten, Exhaustiveness nur über `_` | Go `switch v.(type)` | Vergleich auf dem Typindex; kein Wert wird getaggt |
| C: B plus `Any` | C#/Java `object` | Boxing (L4), dynamisch an einer Stelle |

**Empfehlung: B.** Ein herausgeprüftes Struct ist heute ein Alias (§1.7 A) — das Pattern braucht
eine Kopie oder `mut struct` (L2). **Bricht:** nein. **Hängt ab von:** L2, L4, Pattern-Gebiet.

---

### L16 — Bleibt ein Frame drei Objekte?

**Heute:** Frame, Slots, Operandenstack, gepoolt (`Interpreter.cs:129-136, 1556-1602`); 0 B pro
Aufruf nach dem Pool; Panik-Läufe geben nicht zurück (ebd. 1563-1565; L43).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | gemessen, funktioniert |
| B: ein zusammenhängender Wertestack | Lua, Wren, CPython, JVM, Wasm | kollidiert mit stackful Coroutines (Frames als Objekte sind der Grund, warum 4.0 sie bauen konnte) |
| C: gepackte Slot-Arrays | — | nur mit Messung |

**Empfehlung: A.** Der JIT-Satz (L35) ist die größere Zahl. **Bricht:** nein.
**4.x-Warnstufe:** keine. **Hängt ab von:** VM-Gebiet, L25, L43.

---

### L17 — Sind Handles Zahlen in einer Tabelle oder GC-Objekte?

**Heute:** Zahlen. `opaque type File = int` über `Dictionary<long, FileState> _files`
(`stream.lyr:41`, **`NativeRegistry.cs:2344-2347`**), Sockets über `_sockets` (:1438-1441);
Schlüssel monoton, kein ABA; `streamClose` auf Unbekanntes ist ein No-op (:2581-2584).
`TypeTag.Host` existiert daneben (`Format.cs:192`).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | POSIX fd | Tabellenzugriff; kein GC-Bezug (L10) |
| B: Handles als `TypeTag.Host` | C# `SafeHandle`, Lua Userdata | ohne Finalizer nur der Tabellenzugriff gewonnen |
| C: so lassen, Tabelle wird zur Sub-Arena (L10/D′) | Erlang, Wasm-Store | löst das eigentliche Problem |

**Empfehlung: C.** **Bricht:** nein. **Hängt ab von:** L10, L40 (Handle = int ist stdlib-Quelle
und damit für beide Runtimes gleich).

---

### L18 — Sagt der Guide, wie tief eine Struct-Kopie geht?

**Heute:** nein (guide 05:5). Gemessen (P1, R3a): tief über nackte Struct-Felder, flach über
Array, Klasse, **`?Struct`** und **Interface**.

| Option | Vorbild | Preis |
|---|---|---|
| A: dokumentieren — Guide 05 + Spec §3.4, **mit `?Struct` und Interface in der Liste** | C#, Go | vier Sätze |
| B: tief kopieren naiv | — | unbegrenzt teuer |
| B′: tief mit Copy-on-Write | Swift | Refcount pro Puffer — Rule 2 |
| C: Array-Feld verbieten | — | nein |

**Empfehlung: A — und was A über `?Struct` sagt, entscheidet L33.** B′ als bewusstes Nein.
**Bricht:** nein. **Hängt ab von:** L2, L33.

---

### L19 — Was verspricht die Laufzeit, wenn die Allokation fehlschlägt?

**Heute: nichts.** `VmHost.Execute` (`src/Lyric.Vm/VmHost.cs:36-58`) und `LangVm.Run` (`src/Lyric.Embedding/LangVm.cs:268-283`)
fangen nur `LyricPanic` und `LyricRuntimeException`; `grep -rn "OutOfMemory" src/` ist leer.
Gemessen (Q5c): `Out of memory.`, Exit `0xE0434352`; Kontrolle Q5b: `LYR-VM0006`, Exit 101.
**Und OOM ist nicht die einzige Ausnahmeart, die entkommt** — L37 listet die übrigen.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Ein Host bekommt eine Ausnahme aus einer Assembly, die er nicht referenziert |
| B: jede Allokationsstelle fängt `OutOfMemoryException` → `LYR-VM00xx` mit Backtrace | Lua `LUA_ERRMEM`, CPython `MemoryError`, Java | ~12 Stellen (dieselbe Liste wie L7/B); Bemühens-, keine Totalitätszusage |
| C: Allokation scheitert als Wert | Wasm `memory.grow → -1` | färbt jede Allokation ein — zu viel |
| D: `Execute` deklariert „keine .NET-Ausnahme verlässt diese Methode" und wickelt Unbekanntes in `LYR-VM0000 internal` | Wasmtime | eine `catch`-Klausel; Stack Overflow bleibt ausgenommen |
| E: die VM ist nach OOM **vergiftet** | Erlang, Go | ein Flag |

**Empfehlung: B + D + E, als Vertrag in drei Sätzen** — (a) keine .NET-Ausnahme verlässt
`Execute`, außer Stack Overflow, und der wird benannt; (b) OOM wird zur Panik; (c) danach ist
die VM vergiftet. **D deckt auch L37 ab** und ist deshalb der Satz, der zuerst kommen sollte.

**Erwartete Komplexität:** ~150–250 LOC. **Bricht:** nein. **4.x-Warnstufe:** keine; in 4.7.
**Hängt ab von:** L7, L37, Embedding-Gebiet, Fehler-Gebiet.

---

### L20 — Muss ein Backtrace unabhängig von der Ausführungs-Engine sein?

**Heute: nein, für nichtrekursiven Code.** Gemessen (Q6/Q6b): ohne `--jit` drei positionierte
Frames, mit `--jit` `in main.main` ohne Position. **Gemessen (R3h): rekursiver Code hat unter
`--jit` den vollen Backtrace**, weil er nie kompiliert wird (L35). Der Interpreter nennt den
Einzeiler selbst „the honest cost of compiling" (`Interpreter.cs:255-262`).

| Option | Vorbild | Preis |
|---|---|---|
| A: in der Spec festschreiben, dass ein kompilierter Frame keine Position hat | — | ehrlich; Feld-Absturzberichte wertlos |
| B: Positionstabelle pro kompilierter Funktion, beim Wurf aufgelöst | .NET PDB, Wasm DWARF, V8 | Speicher pro Funktion; heißer Pfad unberührt |
| C: Schattenstapel mit Namen | LuaJIT | Kosten im heißen Pfad |
| D: Deopt und Wiederholung | V8 | bei Nebenwirkungen nicht wiederholbar — nein |
| E: ein gemeteter Lauf bleibt interpretiert, als Regel | heute, ausgesprochen | beantwortet L8/C mit nein |

**Empfehlung: B für 5.0, E bis dahin.** Zusatz aus R3h: solange der JIT Rekursion ablehnt, ist
der Einzeiler auf nichtrekursiven Code beschränkt — ein Trost, kein Argument, und er fällt weg,
sobald L35/B kommt. **Bricht:** nein (Tests, die den Einzeiler pinnen, brechen).
**Hängt ab von:** L8, L35, VM/JIT-Gebiet.

---

### L21 — Bekommt ein `struct` memberwise `==` und `hash` hergeleitet?

**Heute: nein.** Gemessen (Q3): `LYR-SEM0059` mit vorbildlichem Text. Die Darstellung macht die
Herleitung trivial (`LyrValue.cs:47-49`); `Hashable<K>` hat `Equatable<T>` als Elternteil
(`spec/11:26-28`).

**Vorbilder, richtig zugeordnet (§2):** **Go** automatisch (mit Compile-Fehler für unvergleichbare
Felder); **Swift** synthetisiert nur bei **deklarierter** Konformanz ohne Body; **C#** nur für
`record struct`, gewöhnliche Structs haben kein `==` (CS0019); **Rust** `#[derive]`. Drei von
vier sind opt-in.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | sechs Zeilen Handarbeit pro Struct; ein unveränderlicher Wert ohne Gleichheit ist ein halbes Feature |
| B: automatisch für jedes Struct mit konformen Feldern | **Go allein** | Gleichheit entsteht ungewollt und ändert sich still |
| C: opt-in per Attribut `@Derive(Equatable, Hashable)` | Rust | **Ein neuer Mechanismus**: Attribute markieren heute (`spec/11:28-29` „attribute markers"; `abi.md:75` „ehrlich zu Attribute tun nichts"), sie erzeugen keinen Code. Codegenerierung durch Attribute ist Rule-2-pflichtig — die zweite Fassung nannte es „kein neuer" und lag falsch |
| **C′: opt-in per Konformanz OHNE Body** — `struct P :: [Equatable<P>, Hashable<P>]` mit leerem `extend`, und die Sema synthetisiert `equals`/`hash` feldweise, wenn alle Felder konform sind, sonst Diagnose mit dem ersten nicht konformen Feld | **Swift** (`struct P: Equatable {}`) | **Kein** neuer Mechanismus: die `::`-Liste und `extend` existieren; neu ist nur, dass ein fehlender Body bei diesen zwei Interfaces eine Herleitung bedeutet statt eines Fehlers. Randfall Array-/Klassenfeld: keine Herleitung, wie Go |
| D: nur `Equatable` | — | halbiert den Nutzen |

**Empfehlung: C′.** Es ist, was die v5-Liste selbst vorsieht — `docs/Befunde_und_Verbesserungen/lyric-v5-features.md:30`
führt als #3 (P1) „Konformanz-Synthese für `Equatable`, `Hashable`, `Ordered`, `Display` … Swift-Stil,
kein `derive`" —, es ist Swifts Modell, es benutzt Lyrics eigene Konformanz-Syntax, es
erzeugt keinen Attribut-Codegenerator, und es passt zur Kopierregel: flach über Arrays und
Klassen heißt, dort entsteht auch keine Gleichheit.

**Erwartete Komplexität:** ~200–300 LOC (Sema: leerer `extend` auf diesen zwei Interfaces →
generierte Methoden im Lowering; Diagnose). **Bricht:** nein. **4.x-Warnstufe:** keine.
**Hängt ab von:** L2, Interface-Gebiet, Metaprogrammierungs-Gebiet (nur falls doch C).

---

### L22 — Fängt eine Closure die Variable oder den Wert, und was bekommt ein Schleifenrumpf?

**Heute: eine Regel, geschrieben, in zwei Formulierungen.** `spec/07-statements.md:82-85`: „a
`let` is captured as its value, a `var` as the variable itself — the enclosing scope and the
closure share one cell". `spec/03-types.md:77`: „closures capture by reference". Gemessen (Q1,
R3c): `while` mit gefangener `var` → `3 3 3` (Zelle); `for (j in …)` → `0 1 2`, weil `j` ein
`let` ist (`j = 5` ist `LYR-SEM0019`). **Keine Asymmetrie zwischen `for` und `while`** — die
zweite Fassung hatte das behauptet und Optionen darauf gebaut; beide (B „`while`-Scope pro
Durchlauf", E „Go 1.22") sind gestrichen, weil ihre Prämisse falsch war.

Was bleibt: (a) 03:77 und 07:82 widersprechen sich im Wortlaut; 07:84 rettet sich mit „for a
`let` the two readings cannot be told apart" — **und das ist für ein Struct falsch** (R3e, L32).
(b) Der klassische Fehlerfall (gefangene `var` in einer Schleife geschrieben) ist ungewarnt.

| Option | Vorbild | Preis |
|---|---|---|
| A: 03:77 an 07:82 angleichen: „a `var` is captured by reference, a `let` by value" — und 07:84 streichen, weil es für Structs nicht stimmt | — | zwei Sätze |
| D: Warnung „`var` gefangen und in einer Schleife geschrieben" | Gos `loopclosure`-Vet, ESLint `no-loop-func` | ~80 LOC Sema; fängt den echten Fehlerfall |
| C: Capture immer by value | Java, C++ `[=]` | bricht `zaehler += 1` in Closures (P6) — nein |

**Empfehlung: A sofort, D für 4.7.** Die Allokationsfrage der zweiten Fassung (eine Zelle pro
Deklaration gegen pro Durchlauf) stellt sich nicht: die Regel bleibt.
**Bricht:** nein. **Hängt ab von:** L32 (das Struct-Capture), Funktionen-Gebiet.

---

### L23 — Ist die Iterationsreihenfolge einer `Map`/`Set` reproduzierbar, und soll sie es sein?

**Heute: ja, und die Spec sagt „UNSPECIFIED".** Gemessen (Q4): byteidentisch über Prozesse und
Engines; FNV-1a ohne Seed (`core.lyr:300-316`), offene Adressierung (`collections.lyr:547-575`),
Doku „UNSPECIFIED" (ebd. 544-546).

Zwei Korrekturen an der zweiten Fassung: (1) HashDoS ist **nicht** „der einzige bekannte Weg",
mit `Capability.None` und Budget die Host-Zeit zu verbrennen — `secureRandom(1048576)`,
`readAll`, `arrrep`, `s + s` sind je eine Instruktion für unbegrenzte Arbeit (§1.4, L31). HashDoS
ist der **subtilere** Weg, weil er den Host trifft, der Gastdaten in seine *eigene* Map legt.
(2) Java verlangt für Tree-Bins **kein** `Comparable` (§2); und Lyric könnte `Ordered<K>` unter
Monomorphisierung zur Laufzeit ohnehin nicht erkennen — die zweite Fassung räumte das im Nebensatz
ein und empfahl D trotzdem. D ist gestrichen.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Eigenschaft ungenannt, Angriffsfläche ungenannt |
| B: Reihenfolge zugesagt | — | friert Hash und Sondierung als Vertrag ein; HashDoS wird Zusage |
| C: **Seed pro VM, Default konstant, Host darf randomisieren** — `HostOptions.HashSeed` (null = fest), `lyrvm --hash-seed random` | **Python `PYTHONHASHSEED`** (Default randomisiert, setzbar), hier umgekehrt gepolt; Rust/Go/.NET randomisieren immer | Determinismus per Default bleibt; ein Host mit Gastdaten schaltet um. Die Reihenfolge bleibt UNSPECIFIED, damit niemand sie pinnt. `string.hash` bekommt einen Seed-Parameter aus einem VM-Global — ~60 LOC plus Seed-Quelle |
| E: C plus `OrderedMap` | Python-dict, JS `Map` | zwei Typen, zwei Fragen — kein Rule-2-Verstoß |

**Empfehlung: C, und die Doku sagt beides: „deterministisch mit dem Default-Seed, unspezifiziert
als Vertrag, randomisierbar vom Host".** Das behält, was L11 verteidigt, und gibt dem
eingebetteten Fall den Schalter, den Python hat.

**Bricht:** C mit Default-Seed nein; ein Host, der randomisiert, bricht bewusst.
**4.x-Warnstufe:** keine nötig, weil der Default sich nicht ändert. **Hängt ab von:** stdlib,
L11, L27 (Seed-Quelle), L31.

---

### L24 — Wie liest ein Host ein Speicherbudget ab? *(angekündigt, jetzt ausgeführt)*

**Heute:** es gibt kein Speicherbudget, und es gibt kein Vorbild in der VM für eine Ablesung
außer `ExecutionBudget.Consumed` — „a host calibrates its number from `Consumed`, and there is
no other way to arrive at one" (`ExecutionBudget.cs:17-23, 49-51`). Für Speicher hat der Host
heute nur .NET selbst (`GC.GetAllocatedBytesForCurrentThread`, `GC.GetTotalMemory` — .NET-API,
nicht im Repo; **behauptet**, dass ein Host damit den Zuwachs eines Laufs schätzen kann, aber mit
seinen eigenen Allokationen vermischt).

| Option | Vorbild | Preis |
|---|---|---|
| A: keine Ablesung; das Budget ist eine Zahl | — | Niemand kann die Zahl kalibrieren — genau das Argument, das `ExecutionBudget` als Objekt begründet |
| B: **`MemoryBudget` als Objekt mit `Limit`, `Consumed`, `Peak`, `Reset()`** — dieselbe Form wie `ExecutionBudget` | `ExecutionBudget.cs`; Wasm `Store::fuel_consumed` | ein Zähler an den ~12 Stellen aus L7/B; `Consumed` zählt allokiert, `Peak` das Maximum innerhalb eines Laufs |
| C: Host misst selbst über .NET | .NET-API | vermischt mit Host-Allokationen; keine Panik |
| D: Skript-sichtbar `std.runtime.memory()` | MicroPython `gc.mem_free()` | nein — ein Gast soll seinen Käfig nicht vermessen |

**Empfehlung: B, gleichzeitig mit L7.** Ein Budget, das man nicht ablesen kann, ist eine Zahl,
die man rät. **Bricht:** nein. **Hängt ab von:** L7, L25, Embedding-Gebiet.

---

### L25 — Was hält die VM selbst, und zählt es gegen ein Budget? *(angekündigt, jetzt ausgeführt)*

**Heute:** vier Dinge, keines dokumentiert als Zusage: der **Frame-Pool** pro Funktion,
unbeschränkt, „bounded by the deepest simultaneous recursion ever seen"
(`Interpreter.cs:1559-1561`); der **Argumentpuffer-Pool** (`arguments.Rent`, ebd. 521, 544, 662,
713); die **suspendierten Ketten** (`CoroutineChain.Saved`, `CoroutineChain.cs:33-38`), die dem
Pool entzogen sind; die **Ressourcentabellen** (`_files`, `_sockets`). Gemessen (Q7b): 1,6 Mio.
fallengelassene Ketten lassen Peak WS bei 34 MB — der Pool wächst nicht mit der Rundenzahl.

| Option | Vorbild | Preis |
|---|---|---|
| A: ungenannt lassen | — | ein Speicherbudget, das die VM nicht kennt, ist ungenau um genau diese Größen |
| B: **dokumentieren als Eigenschaft**: „die VM hält höchstens so viele Frames wie die tiefste je gesehene Rekursion, plus die suspendierten Ketten" | `Interpreter.cs:1559-1561` sagt es schon | drei Sätze in Guide 14 |
| C: Pool deckeln | Lua (`luaM_shrink` beim GC) | zusätzliche Logik für einen Fall, den Q7b nicht zeigt |
| D: Frame-Pool-Misses gegen `MemoryBudget` zählen | — | ehrlich und billig: ein Miss ist eine Allokation |

**Empfehlung: B jetzt, D mit L7.** **Bricht:** nein. **Hängt ab von:** L7, L16, L43.

---

### L26 — Wie sähe ein Cleanup-Restbudget aus, und lohnt es? *(angekündigt, jetzt ausgeführt)*

**Heute:** ein Panik lässt keinen `defer` laufen — Spec (`07:132`) und Sicherheitsargument
(`ExecutionBudget.cs:56-61`: „no `defer` gets to run afterwards. A catchable stop would be one a
hostile script could sit out"). L10/B würde das umkehren und braucht deshalb eine Antwort auf:
wie viele Instruktionen, wer setzt sie, was passiert bei Erschöpfung?

| Option | Vorbild | Preis |
|---|---|---|
| A: **kein Restbudget, kein `defer` auf Panik** — Cleanup ist Sache der Arena (L10/D′) | Erlang (der Prozess stirbt, der Supervisor räumt) | konsistent mit Spec und Sicherheitsargument; die Handles einer suspendierten Kette sind ohnehin nur so erreichbar |
| B: festes Restbudget aus `HostOptions.CleanupBudget` (Default 0 = heutiges Verhalten); die `defer`-Kette läuft darunter; **Erschöpfung ist eine zweite Panik, die nichts mehr laufen lässt**, und alle offenen Handles bleiben bis `Dispose` | Go `defer` auf Panic — ohne Budget, weil Go keine Sandbox verspricht | ein zweites Budget-Objekt; eine Spec-Regeländerung mit `since:`-Fall; ein Host, der 0 lässt, hat nichts gewonnen; einer, der 10 000 setzt, hat einem feindlichen Skript 10 000 Instruktionen nach dem Stop geschenkt |
| C: `defer` läuft auf Panik nur ohne Budget (Standalone), nie unter Budget | — | zwei Verhalten für ein Konstrukt — Rule 2 |

**Empfehlung: A.** Ausgeschrieben zeigt B, dass es teuer ist (zweites Budget, Regeländerung) und
wenig kauft (die Arena schließt ohnehin, die Koroutinen bleiben). L10 ist damit „D′ allein".
**Bricht:** nein. **Hängt ab von:** L10.

---

### L27 — Was garantieren Konsole, `isInteractive` und `secureRandom` in einer eingebetteten VM? *(angekündigt, jetzt ausgeführt)*

**Heute:** stdin/stdout/stderr **gehören dem Host**, per Injektion (`NativeRegistry.CreateDefault(output,
error, input)`, `NativeRegistry.cs:275-282`) und per Kommentar (ebd. 314-316: „part of the process
rather than an access decision. A host that wants to forbid it passes an empty reader"). Entropie
ist capability-frei per Modulentscheidung (`random.lyr:22-24`). **Inkonsistent ist allein
`isInteractive`**: es liest `Console.IsInputRedirected && …IsOutputRedirected` (ebd. 333-335) —
den Prozess, nicht die injizierten Streams; ein Host mit `StringReader` bekommt die Antwort des
Terminals. Guide 14 sagt zu Konsole und Entropie nichts (gelesen, `docs/guide/14-embedding.md`,
kein Treffer für `readLine`/`secureRandom`).

| Option | Vorbild | Preis |
|---|---|---|
| A: **den Vertrag in Guide 14 und §4.5 schreiben**: „Konsole und Entropie sind keine Capability; der Host besitzt die Streams und entscheidet über sie" | Lua (`io` ist eine Bibliothek, die der Host lädt oder nicht) | drei Sätze |
| B: neues Bit `consoleAccess` | Deno hat keins; WASI hat `stdin` als Preopen | ein Bit für etwas, das der Host schon über die Injektion steuert — Rule 2 |
| C: **`isInteractive` aus den injizierten Streams ableiten** (der Host sagt es, Default: `Console`-Abfrage) | — | ~10 LOC; ein Parameter in `CreateDefault` |

**Empfehlung: A + C.** Die zweite Fassung nannte das „Modelllücke" und die Vorbedingung für
L9/D; beides zurückgenommen. **Bricht:** nein. **Hängt ab von:** L9, L23 (Seed-Quelle), Embedding.

---

### L28 — Wer führt die Migration 4.x → 5.0 aus? *(angekündigt, jetzt ausgeführt)*

**Heute:** Warnungen mit `since:`-Gates (`LYR-SEM0107`…`0110`, `spec/12:88-91`), Profile seit
M37, **kein Fixer-Werkzeug** (gemessen: `grep -rln '"fix"' src/Lyric.Cli src/Lyrc` leer; kein
`--fix` in Guide oder README). Was 5.0 nach diesem Dossier verlangt: `mut struct` an jedem
Struct mit `mut fn`/Feldschreiben (L2), ggf. Tests, die `float32`-Ausgaben (L14) oder
Backtrace-Einzeiler (L20) pinnen.

| Option | Vorbild | Preis |
|---|---|---|
| A: Warnungen, der Benutzer editiert | C# (Analyzer ohne Fixer) | für `mut struct` in der stdlib und Erato: Handarbeit an jeder Stelle, die `LYR-SEM0109` nennt |
| B: **`lyrc --fix` für die rein mechanischen Umschreibungen** (Schlüsselwort einfügen, wo die Warnung steht) | `go fix`, `cargo fix`, `rustfix` | ein Rewriter über Spans; nur für Warnungen, deren Fix eindeutig ist (`mut struct`: ja; `defer`-Throw: nein) |
| C: 5.0 akzeptiert 4.x-Module im Kompatibilitätsmodus | Python 2/3 (Gegenbeispiel) | zwei Sprachen — nein |

**Empfehlung: B, nur für `mut struct`, als 4.7-Werkzeug.** Ein Bruch, den ein Werkzeug
mechanisch reparieren kann, ist ein kleinerer Bruch. **Bricht:** nein. **Hängt ab von:** L2,
CLI-Gebiet, Diagnostik-Gebiet.

---

### L29 — Was gilt, wenn zwei VMs dasselbe Host-Objekt sehen? *(angekündigt, jetzt ausgeführt)*

**Heute:** ein Host-Objekt ist ein beliebiges .NET-Objekt in `Ref` (`LyrValue.cs:37-45`), ohne
VM-Stempel; Guide 14:494-496 sagt „two VMs never share a **descriptor**" und schweigt über
Host-Objekte. Nichts hindert einen Host, dasselbe Objekt an zwei VMs zu geben; die VM „never
looks into" es, also gibt es auch nichts, was sie prüfen könnte.

| Option | Vorbild | Preis |
|---|---|---|
| A: **dokumentieren**: „ein Host-Objekt gehört dem Host; die VM garantiert nichts über seine Sichtbarkeit in anderen VMs — Nebenläufigkeit und Lebensdauer sind Sache des Hosts" | Lua `lightuserdata` | drei Sätze |
| B: VM-gestempelte Handles (Wrapper mit Besitzer-Id, geprüft an jeder Native-Grenze) | — | eine Allokation pro Übergabe und eine Prüfung pro Aufruf für einen Fall, den der Host selbst verursacht |
| C: verbieten | — | nicht durchsetzbar |

**Empfehlung: A.** **Bricht:** nein. **Hängt ab von:** Embedding-Gebiet, Nebenläufigkeits-Gebiet.

---

### L30 — Wem gehört die Tiefengrenze, wenn Läufe ineinander laufen? *(angekündigt, jetzt ausgeführt)*

**Heute:** `MaxCallDepth` zählt thread-weit über verschachtelte Läufe: `_outerFrames`
(`[ThreadStatic]`, `Interpreter.cs:142-157`) wird vor jedem Native-Aufruf auf
`outer + frames.Count + 1` gesetzt (ebd. 526-528) und im inneren Lauf mitgezählt (ebd. 533).
Zwei VMs auf einem Thread (Host-Callback aus VM A ruft VM B) teilen **einen** Zähler — das ist
kein Versehen, sondern die Reparatur eines Prozessabsturzes (ebd. 146-151). `MaxReentryDepth = 32`
ist ebenfalls thread-statisch. **Beide paniken mit `LYR-VM0004`** (ebd. 192-194, 533-535);
`appendix-a:278` beschreibt nur die Rekursion. Welcher Lauf panikt: der, der den Aufruf macht,
also der **innerste** (gelesen: die Prüfung sitzt im `call`-Pfad des laufenden Interpreters).
Wie der Backtrace über die VM-Grenze aussieht: die Panik verlässt den inneren `Execute` als
`LyricPanic`, durchquert den Host-Delegaten und — wenn der Host sie nicht fängt — den äußeren
`Execute`; ob der äußere Lauf seine Frames anhängt, ist **behauptet: nein** (nicht gemessen,
braucht einen Host).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen, **dokumentieren**: die Tiefe ist eine Eigenschaft des Threads | — | Guide 14 gewinnt einen Absatz |
| B: Tiefe pro VM (L5/B) | Python `setrecursionlimit` (pro Interpreter) | **allein nicht machbar**: was geschützt wird, ist der CLR-Stack, und der ist pro Thread. Eine VM-Zahl kann nur ein Deckel *unter* der Thread-Grenze sein |
| C: **eigener Code für Re-Entry** (`LYR-VM00xx re-entry too deep`), Anhang A nennt beide Grenzen | — | eine Konstante, eine Anhangszeile |
| D: Backtrace über die Grenze: der Host-Delegat wird als Frame `in <host> engine.tick` eingefügt | Lua (`[C]`-Frames im Traceback) | kleine Ergänzung in `HostFunction.Bridge`; braucht Messung |

**Empfehlung: A + C sofort; L5/B nur mit der Formulierung „Deckel unter der Thread-Grenze"; D
mit dem Embedding-Gebiet.** **Bricht:** C ist ein neuer Code — Spec-Änderung, additiv.
**Hängt ab von:** L5, L6, Embedding-Gebiet, Diagnostik-Gebiet.

---

### L31 — Was kostet eine Instruktion maximal, und soll das Budget Arbeit statt Instruktionen zählen? *(neu)*

**Heute:** das Budget zählt Instruktionen (`ExecutionBudget.cs:63-70`), und Guide 14:266 sagt
ausdrücklich: „What it bounds is the script's own loops" — ein Native ist eine Instruktion. Was
eine Instruktion leisten darf, gelesen: `arrrep` bis `int.MaxValue` Elemente
(`Interpreter.cs:963-987`; gemessen 32 GB, Q5); `secureRandom` bis 1 MiB CSPRNG plus ein
`LyrValue[]` von 16 MiB (`NativeRegistry.cs:596-605`, `Bytes()` :2736); `readAll` bis EOF (ebd.
322-326); `std.string.concat` unbegrenzt (`FunctionLowerer.cs:1871`, `NativeRegistry.cs:341`) —
`s = s + s` 31-mal ist 2^31 Einheiten unter ~100 Instruktionen; f-Strings sind Ketten aus
`concat` (`FunctionLowerer.cs:4824-4876`). Sortieren und Map-Operationen sind dagegen Lyric-Code
und zahlen pro Instruktion — die stdlib ist hier ehrlicher als die Natives. Ein Gast mit Budget
1000 hat also GiB an Host-Arbeit, ohne eine Map anzufassen.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen, Guide-Satz bleibt | Wasmtime-Fuel (1 pro Instruktion, Host-Calls kosten, was der Host will) | ehrlich, und der Host bleibt ungeschützt |
| B: **arbeitsproportionale Gebühr**: ein Native oder Opcode, das `n` Elemente oder Bytes bewegt, belastet `1 + n / K` Instruktionen (K etwa 64); gilt für `arrrep`, `arrcat`, `concat`, `Bytes()`, `readAll`, `secureRandom`, `utf8Encode/Decode`, `fromInt/Float` | **Erlang** (BIFs zahlen Reductions proportional; `length/1` pro Element) | ~10 Stellen mit einer `Charge(n)`-Überladung; die Zusage „gleiche Instruktion auf jeder Maschine" bleibt, weil `n` deterministisch ist |
| C: nur das Speicherbudget (L7) | — | fängt Bytes, nicht CPU-Zeit (Sortieren einer riesigen Kette in einem Native, CSPRNG) |
| D: pro Native eine feste Obergrenze (`secureRandom` hat 1 MiB; `readAll` bekommt eine) | — | halbe Antwort; ein 1-MiB-Native in einer Schleife ist immer noch 1 MiB pro Instruktion |

**Empfehlung: B, zusammen mit L7/B.** Die Zusage der Sandbox ist „der Host bekommt seinen Thread
zurück", und die gilt nur, wenn die Instruktion, die gezählt wird, auch die Arbeit ist, die
getan wird. Die Determinismus-Eigenschaft bleibt, weil die Gebühr aus der Eingabe folgt.

**Erwartete Komplexität:** ~120 LOC plus Tests, die die Gebühr an jeder Stelle messen.
**Bricht:** nein — ein Budget, das heute reicht, reicht danach nur noch für weniger Arbeit; der
Host kalibriert neu über `Consumed`. **4.x-Warnstufe:** keine; Changelog-Zeile.
**Hängt ab von:** L7, L23, L24, Embedding-Gebiet.

---

### L32 — Wird ein Struct beim Einfangen in eine Closure kopiert oder aliasiert? *(neu)*

**Heute: aliasiert, gegen die Spec.** Gemessen (R3e, Erwartung vorher notiert, Kontrolle
`let k = m` → 0): `let l = Zaehler{n=0}; let f = () => l.wert(); l.hoch(); f()` liefert **1**.
`spec/07-statements.md:82` sagt „a `let` is captured as its value"; `LoadCaptured`
(`FunctionLowerer.cs:366-380`) lädt den Slot ohne `CopyStructValue`. Für Skalare ist der
Unterschied unbeobachtbar (07:84), für ein Struct mit `mut fn` nicht. Das ist die **vierte
Kopierstelle**, und sie fehlt.

| Option | Vorbild | Preis |
|---|---|---|
| A: **`LoadCaptured` kopiert ein `let`-Struct** (wie `BindLocal`) | die Spec selbst (07:82); C++ `[=]` | eine Bedingung, ~15 LOC; eine Allokation pro Capture eines Structs — nur bei `let`, eine `var` ist ohnehin eine Zelle |
| B: Spec an die Implementierung angleichen („by reference, auch für `let`") | 03:77 | macht `let` für Structs noch schwächer als L3 schon beschreibt |
| C: fällt mit `mut struct` für unveränderliche Structs weg; für `mut struct` gilt A | L2/B+A | — |

**Empfehlung: A in 4.7 als Bugfix**, weil es ein Spec-Bruch und kein Designraum ist; C ist der
5.0-Zustand. **Bricht:** **minor** — ein Programm, das die Aliasierung nutzt, ändert sein
Verhalten; die Spec hat es nie erlaubt. Konformanzfall mit `since:`.
**Hängt ab von:** L2, L22.

---

### L33 — Kopiert `structcopy` durch `?Struct`- und Interface-Felder? *(neu)*

**Heute: nein.** Gemessen (R3a): `var b = a; b.opt!.n = 99` → `a.opt!.n` liest 99; `d2.iface.hoch()`
→ `d1.iface.wert()` liest 1; Kontrolle `plainField = 1`. `CopyStruct` rekursiert nur bei
`Tag == Struct` (`Interpreter.cs:1161-1163`). **Damit ist `struct Node { next: ?Node }` heute
eine Referenzstruktur mit Wertkopf**: die Kopie eines Knotens teilt die ganze Kette. Der
VM-Kommentar nennt Klasse, Array und Interface als flach (ebd. 1145-1147) — `?Struct` nennt er
nicht; das Verhalten ist ein Durchfallen, keine Entscheidung.

| Option | Vorbild | Preis |
|---|---|---|
| A: **so lassen und schreiben**: „tief über nackte Struct-Felder; flach über Klasse, Array, Interface **und `?Struct`**" | — | ehrlich; `?Node` bleibt ein versteckter Zeiger, und L2/C (`?Struct` erlauben) muss sagen, dass eine Kopie die Kette teilt |
| B: **tief auch über `?Struct`** — `CopyStruct` rekursiert bei `Tag == Optional` mit Struct-Innentyp (der Feldtyp-Eintrag muss den Innen-Index tragen — **behauptet**, dass `BytecodeType` für Optional den Innentyp führt; zu prüfen im Bytecode-Gebiet) | C# `Nullable<T>` (Wertsemantik durchgängig) | eine Kette wird O(n) kopiert — das **ist** die Bedeutung einer Wertliste; wer Teilen will, nimmt eine Klasse |
| C: `?Struct`-Feld verbieten | — | verbietet Bäume aus Structs — nein |
| D: Interface-Feld tief kopieren | — | nein: ein Interface-Wert ist eine Referenzform; die Kopie fand beim Einpacken statt, und zwei Struct-Kopien, die dasselbe eingepackte Objekt teilen, sind konsistent mit „Interface = Referenz" |

**Empfehlung: B für `?Struct`, A-Wortlaut für Interface — und der Satz gehört in §3.4.** Ohne B
ist „ein Struct ist ein Wert" für jede rekursive Datenstruktur falsch, und genau dort wird es
gebraucht. **Bricht:** B **minor** (ein Programm, das die geteilte Kette nutzt, ändert sich).
**Hängt ab von:** L2, L18, Bytecode-Gebiet.

---

### L34 — Was ist ein Enum mit Payload und ein Tupel, und wann kopiert ihre Struct-Payload? *(neu)*

**Heute:** ein Variant ist ein `LyrValue[]` mit dem Tag in Slot 0 (`Interpreter.cs:1015-1026`),
ein Tupel ein `LyrValue[]` (**behauptet**, nicht direkt gelesen). Gemessen (R3f): das Einpacken
in einen Variant kopiert das Struct (0), die `match`-Bindung kopiert (0), die Destrukturierung
eines Tupels kopiert (0), Kontrolle Klassen-Payload teilt (1). `let e2 = e1` teilt das Array —
aber **ohne Bindung gibt es keinen Schreibpfad in die Payload**, also ist das Teilen
unbeobachtbar. Konsistent; steht nirgends (§1.1 der zweiten Fassung führte beide Formen nicht).

| Option | Vorbild | Preis |
|---|---|---|
| A: **dokumentieren** in §3.4: „Enum-Werte und Tupel sind Referenzformen; eine Struct-Payload wird an jeder Bindung kopiert, deshalb ist ihr Teilen unbeobachtbar" | Rust (Enum by value, Payload by move) | drei Sätze |
| B: Payload beim Enum-Kopieren tief kopieren | — | Kosten ohne beobachtbaren Gewinn |

**Empfehlung: A.** Ein Fall, in dem die Implementierung richtig ist und nur der Text fehlt.
**Bricht:** nein. **Hängt ab von:** L2, L33 (falls L2/A die `match`-Bindung ändert, muss R3f
grün bleiben — Konformanzfall).

---

### L35 — Bleibt es dabei, dass der JIT Rekursion ablehnt? *(neu)*

**Heute: ja.** `JitCompiler.cs:721-724`: `CodeFor` markiert vor dem Kompilieren, eine rekursiv
erreichte Funktion antwortet „no code", der Aufrufer lehnt ab — „a recursive helper does not
compile today". Guide 14:308 nennt es in der Ablehnungsliste. Gemessen (R3h, Kontrolle rev4): `fib(27)`
Interpreter 598–664 ms gegen `--jit` 693–727 ms (rev3) bzw. 281–328 ms gegen 259–275 ms (rev4) —
**kein Gewinn**, die Differenz ist Rauschen; und der Backtrace einer Panik in
rekursivem Code ist unter `--jit` vollständig, weil nichts kompiliert wurde. Folgen: „develop
interpreted, ship compiled" ist für rekursive Algorithmen (Baumdurchlauf, Parser, Quicksort)
eine **leere Zusage**; L5-Paniken sind immer interpretiert; L20s Einzeiler trifft nur
nichtrekursiven Code.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen; die Ablehnungsliste (Closures, Exceptions, Enums, Rekursion, schmale Breiten, `structcopy`) steht **im Guide als Vertrag**, nicht in der Spec — die Spec kennt keinen JIT, und Lyricpp hat keinen | heute | ehrlich; rekursiver Code bleibt Interpreter-Geschwindigkeit |
| B: **Rekursion kompilieren**: ein Aufruf einer Funktion, die gerade kompiliert wird, geht über `JitContext.Call` (die Late-Binding-Form, die `JitCompiler.cs:686-690` ohnehin nimmt) statt über die Ablehnung; die Exceptions-Begründung (:713-720) bleibt, weil `throw` weiter abgelehnt wird | jede JVM; V8 | eine Änderung in `CodeFor` (Status „compiling" statt „tried") plus Tests für gegenseitige Rekursion; und **L20 wird dringender**, weil Tiefenpaniken dann Einzeiler werden — es sei denn, kompilierter Code zählt seine Tiefe mit (`_nesting`-artig), was er heute nicht tut |
| C: Rekursion kompilieren, aber mit Tiefenzähler im kompilierten Prolog | — | ein Inkrement/Dekrement pro Aufruf; behält `LYR-VM0004` |

**Empfehlung: C für 5.0 im VM-Gebiet, mit L20/B zusammen; A-Wortlaut sofort in Guide 14 („what
is compiled" ist ein Vertrag des Guides, mit Version).** Die Spec bleibt JIT-frei.
**Bricht:** nein. **Hängt ab von:** L5, L20, L30 (der Tiefenzähler), VM/JIT-Gebiet.

---

### L36 — Soll 5.0 Tail Calls garantieren? *(neu)*

**Heute: nein, gemessen.** R3g: `return down(n - 1, acc + 1)` panikt bei 1500 mit `LYR-VM0004`
in beiden Engines, 1026 Zeilen; `Interpreter.cs:137-138`: „There is no tail-call optimization,
so a missing base case surfaces here". Die 1024-Grenze ist damit eine Entscheidung *gegen* TCO,
nicht nur eine Zahl.

| Option | Vorbild | Preis |
|---|---|---|
| A: **kein TCO**, ausgesprochen | Go, C#, Python (Guido: ausdrücklich abgelehnt), Java | rekursive Iteration braucht eine Schleife; der Backtrace bleibt vollständig; `defer` und Handler eines Frames bleiben eindeutig |
| B: **Proper Tail Calls garantiert** (`return f(x)` ersetzt den Frame) | Lua, Scheme, Erlang | drei Kosten: der Backtrace verliert Frames (Lua: „(...tail calls...)"); ein Frame mit ausstehendem `defer` **kann nicht** ersetzt werden (Go hat deshalb keins) — also gilt die Garantie nur ohne `defer`, `try` und Koroutinen-Grenze, was „garantiert" zu „manchmal" macht; das Budget bleibt deterministisch, aber die Tiefenpanik verschwindet an genau den Stellen, an denen sie eine unendliche Schleife anzeigte |
| C: **expliziter Tail Call** `become f(x)` (nur in Tail-Position, nur ohne `defer`/`try` im Frame, sonst Compile-Fehler) | Clojure `recur`, Rust `become` (RFC), **Wasm `return_call`** (opt-in-Instruktion) | ein Schlüsselwort und ein Opcode; die Garantie ist lokal und prüfbar; der Backtrace zeigt eine markierte Zeile |

**Empfehlung: A für 5.0, C als einzige akzeptable Form, falls der Bedarf kommt.** Implizites TCO
verträgt sich nicht mit `defer` und mit einer Diagnose, die eine fehlende Abbruchbedingung
meldet; explizites TCO ist Wasms Antwort und passt zu einer Sprache, die Mechanismen benennt.
**Bricht:** A nein; C additiv (neues Schlüsselwort — kontextuell). **Hängt ab von:** L5, L35,
Kontrollfluss-Gebiet, Bytecode-Gebiet.

---

### L37 — Welche .NET-Ausnahmen außer OOM können `Execute` verlassen? *(neu)*

**Heute:** gelesen, mehrere. `AsObject` wirft `InvalidOperationException` bei null
(`LyrValue.cs:130-131`), `AsCoroutine` ebenso (:104-105); der Cast `(LyrValue[])Ref` wirft
`InvalidCastException`, wenn `Ref` etwas anderes hält — ein Host-Native, das ein falsch
geformtes `LyrValue` zurückgibt, erreicht das; `CopyStruct` ist gegen Index-Fehler durch
`i < copy.Length` geschützt (`Interpreter.cs:1161`); `arrrep` fängt den Overflow selbst (:975-980).
**`HostFunctionException : Exception`** (`src/Lyric.Embedding/HostFunction.cs:184`, geworfen :127-131) ist **weder**
`LyricPanic` **noch** `LyricRuntimeException` und verlässt `LangVm.Run` (`src/Lyric.Embedding/LangVm.cs:268-283`)
ungefangen — das ist so gewollt („a host … gets its own exception type back"), aber es ist eine
.NET-Ausnahme aus `Execute`. `StackOverflowException` ist in .NET nicht fangbar. Was für
**Verifier-geprüfte** Bytes gilt: „the loader validated slot and block indices, call targets,
stack balance and maximum depth" (`Interpreter.cs:118-120`) — die Null-Referenz und der falsche
Cast sind danach nur über Host-Natives erreichbar. Wer das für fremde Bytes beweist: heute der
Verifier, ohne dass die Spec die Liste der unerreichbaren Zustände führt.

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | ein Host sieht gelegentlich `InvalidCastException` aus einer Assembly, die er nicht kennt |
| B: L19/D — `catch (Exception)` in `Execute` → `LYR-VM0000 internal` mit Backtrace, **außer** `HostFunctionException` (die ist die Ausnahme des Hosts und geht durch) | Wasmtime (`Trap`) | eine Klausel; die Liste oben wird zur Testliste |
| C: Native-Rückgaben validieren (`Ref is LyrValue[]` gegen den deklarierten Rückgabetyp) | — | eine Prüfung pro Native-Rückgabe; fängt den Host-Fehler an der Grenze statt später |

**Empfehlung: B + C.** B ist derselbe Satz wie L19/D; C ist die Grenze, an der der Fehler
entsteht. **Bricht:** nein. **Hängt ab von:** L19, Embedding-Gebiet.

---

### L38 — Was kostet ein `string`, und wie verhält er sich zu `char[]` und `uint8[]`? *(neu)*

**Heute:** ein `string` ist ein .NET-`string` (`LyrValue.cs:34, 127`): UTF-16, unveränderlich,
per Referenz geteilt, **2 B pro Einheit**. Gemessen (R3j): 2^24 Zeichen als `string` per
Verdopplung → Peak 92 MB (Endwert 32 MB, Rest Verdopplungsmüll); als `char[]` → 284 MB
(15,3 B/Einheit); Leerlauf 27 MB. `s + s` ist ein Native (`std.string.concat`,
`FunctionLowerer.cs:1871`), das einen neuen String allokiert; ein f-String ist eine Kette aus
`concat` (ebd. 4824-4876) — **k Teile heißt k−1 Allokationen mit wachsendem linken Operanden**,
also O(k · Länge). `utf8Encode`/`Decode` und jedes `Bytes()` sind eine **Kopie pro
Grenzübertritt** in ein `LyrValue[]` (`NativeRegistry.cs:2736`) — von 1 B pro Byte auf 16 B.
Die 16×-Rechnung von L1 gilt also für `uint8[]` gegen `byte[]`; gegen `string` ist der Faktor 8.

| Option | Vorbild | Preis |
|---|---|---|
| A: **dokumentieren**: Kosten pro Einheit, Konkatenation allokiert, f-String-Kette, Kopie an jeder Bytes-Grenze | Go (Strings sind unveränderlich; `strings.Builder`) | ein Absatz in Guide 07 |
| B: f-String als **eine** Allokation (ein `concatN`-Native mit Array oder eine Builder-Lowerung) | C# `string.Concat(params)`, Java `StringConcatFactory` | ein Native oder ein Lowering-Pfad; ~80 LOC |
| C: `uint8[]` packen (L1) — dann ist `utf8Encode` 1 B pro Byte | — | L1 |

**Empfehlung: A + B, und C über L1.** Strings sind die häufigste Datenform, und das Dossier
hatte sie übersehen. **Bricht:** nein. **Hängt ab von:** L1, L7 (die Liste), L31, Strings-Gebiet.

---

### L39 — Welche GC-Konfiguration erbt die VM, und ist die GC-Pause eine Frage dieses Gebiets? *(neu)*

**Heute:** die VM setzt nichts (gemessen: `grep -rn "GCSettings\|GC\.\|GCHeapHardLimit" src/`
leer) — sie erbt Workstation/Server, Concurrent, `LatencyMode` und ein etwaiges `GCHeapHardLimit`
vom **Prozess**, also vom Host; kein Skript sieht oder setzt etwas. Was pro Frame allokiert, gelesen:
Closure-Umgebung nur bei Captures (`FunctionLowerer.cs:393-397`), `?T` auf Skalar **nichts**
(`LyrValue.cs:52-57`), `structcopy` ein `LyrValue[]` (`Interpreter.cs:1158`), Interface-Wrap eines
Structs eine Kopie (`FunctionLowerer.cs:3046-3047`), f-String k−1 Strings (L38), Frame 0 B nach
dem Pool, Native-Rückgabe von Arrays eine Kopie. **Nicht gemessen:** eine Allokationsrate pro
Spiel-Frame — dafür fehlt ein Erato-Lauf im Checkout (**behauptet**, dass Erato ein Frame-Budget
von 16 ms hat und die Pause dort die Frage ist, nicht die Grenze).

| Option | Vorbild | Preis |
|---|---|---|
| A: **Sache des Hosts, dokumentiert**: Guide 14 sagt, welche GC-Knöpfe der Prozess hat und dass die VM keinen dreht; die Allokationsquellen oben werden gelistet | Lua (Allokator vom Host), Wren (Heap-Parameter vom Host) | ein Absatz |
| B: `HostOptions.GcLatency` → `GCSettings.LatencyMode` | — | **unehrlich pro VM**: der GC ist pro Prozess; eine VM-Option, die einen Prozessknopf dreht, lügt über ihre Reichweite |
| C: Skript-API | MicroPython | nein |
| D: **Allokationsrate messen**, bevor irgendetwas entschieden wird: ein synthetischer Frame (Closure-Umgebungen, `structcopy`, f-Strings) unter `GC.GetAllocatedBytesForCurrentThread` — im Embedding-Gebiet | — | eine Session; Vorbedingung für jede Latenzaussage |

**Empfehlung: A + D.** Das Dossier stellt die Latenzfrage, es beantwortet sie nicht — und sagt,
was gemessen werden muss. **Bricht:** nein. **Hängt ab von:** L2 (weniger `structcopy` = weniger
Allokation), L38, Embedding-Gebiet.

---

### L40 — Was in diesem Gebiet ist Spec (beide Runtimes) und was Implementierungsartefakt? *(neu)*

**Heute:** die Spec hat den Ort dafür — `spec/12-diagnostics.md:50` „12.4 Implementation limits,
and the implementation's own failures" — und `appendix-a:278` macht es für die Tiefe vor: „The
limit is quality of implementation (the reference allows 1024 frames); the code and the panic are
the contract." Für den Rest ist die Einordnung nicht geschrieben. Sortiert nach dem, was gelesen
ist:

| Eigenschaft | Spec | Artefakt | Beleg |
|---|---|---|---|
| Wickeln, Sättigung, `/ 0` | ✓ | | 03:49-64, 163-164 |
| `defer` läuft nicht auf Panik | ✓ | | 07:132 |
| `LYR-VM0004` als Code; 1024 als Zahl | Code ✓ | Zahl | appendix-a:278 |
| Map-Reihenfolge | „UNSPECIFIED" | die Stabilität | collections.lyr:544-546 |
| Handle = `int`-Schlüssel | ✓ (stdlib-Quelle, `stream.lyr:41`) | die Dictionary | — |
| 16 B pro Wert, 2 B pro String-Einheit | | ✓ | §1.1 |
| Frame-Pool, Panik verwirft Frames | | ✓ | Interpreter.cs:1556-1602 |
| OOM-Verhalten | **fehlt** (L19) | heute rohe Ausnahme | Q5c |
| JIT-Ablehnungsliste | | ✓ (Guide) | guide 14:304-309 |
| Capture-Regel | ✓ | Struct-Bruch (L32) | 07:82 |
| Keine FMA-Kontraktion | **fehlt** (L44) | gemessen R3i | — |

Das zweite Runtime (Lyricpp, C++, Erato 2 — **behauptet** aus den Projektnotizen, nicht im
Checkout) hat keinen .NET-GC, keinen RyuJIT und keine `Dictionary`; es teilt die stdlib-Quelle
und den Bytecode. Alles in der Artefakt-Spalte darf dort anders sein; alles in der Spec-Spalte
nicht.

| Option | Vorbild | Preis |
|---|---|---|
| A: **§12.4 bekommt die Tabelle**: was QoI ist, mit dem Wert der Referenz in Klammern | appendix-a:278 als Muster | eine Tabelle; jede neue Grenze (L5/B, L7) trägt sich dort ein |
| B: nichts | — | Lyricpp rät |

**Empfehlung: A, und L19 und L44 tragen ihre Sätze dort ein.** **Bricht:** nein.
**Hängt ab von:** Spec-Gebiet, L19, L44, allen Grenzen dieses Dossiers.

---

### L41 — Wie verhält sich die Wertdarstellung außerhalb x64? *(neu)*

**Heute:** `LyrValue` = `ulong` + `object?` (`LyrValue.cs:20-21`); 16 B ist eine **Messung auf
x64** (P5: 15,5 B/Element). Auf 32-Bit-.NET ist die Referenz 4 B — 12 B mit 8-Byte-Ausrichtung
des `ulong` also 16 B mit Padding oder 12 B gepackt (**behauptet**; nicht gemessen, kein
32-Bit-Ziel im Repo). Big-Endian: `Bits` ist ein `ulong` in Hostordnung, `DoubleToUInt64Bits`
ist endian-neutral; die `.lyrbc`-Byteordnung ist eine Bytecode-Frage. NativeAOT: der JIT wird
ignoriert, „every script is interpreted" (`src/Lyric.Embedding/HostOptions.cs:65-68`, guide 14:323-327).

| Option | Vorbild | Preis |
|---|---|---|
| A: **„16 B" ist eine Beobachtung, keine Zusage** — L1/L7 rechnen für x64, und das steht dabei; ein `MemoryBudget` zählt, was die VM auf **ihrer** Plattform allokiert (`sizeof`-basiert), und bleibt damit ehrlich | — | ein Satz in §12.4 (L40) |
| B: Größe zusagen | — | bindet Lyricpp an .NETs Layout — nein |

**Empfehlung: A.** **Bricht:** nein. **Hängt ab von:** L1, L7, L40, Bytecode-Gebiet.

---

### L42 — Sieht ein Native das Original oder eine Kopie eines Struct-Werts? *(neu)*

**Heute, drei Wege:** (1) **Native Roots** flatten ein Struct zu seinen Feldern — „A struct
parameter crosses as its fields", Rückgabe über einen Puffer der Laufzeit (guide 14:171-192): per
Konstruktion eine Kopie, der Host kann nichts mutieren. (2) **Rohe `NativeRegistry`-Natives**
(`Func<LyrValue[], LyrValue>`, `src/Lyric.Embedding/HostFunction.cs:113`) bekommen den Argumentpuffer; ein
Struct-Argument darin ist ein `LyrValue` mit `Ref` auf ein `LyrValue[]` — ob das die Kopie aus
der Argument-Coercion ist (Q13-Kontrolle: nacktes Struct als Argument an eine Lyric-Funktion wird
kopiert) oder das Original, ist für Natives **behauptet: Kopie**, weil dieselbe `Coerce`-Stelle
das Argument lowert; nicht gemessen, weil `lyrvm` keine Host-Natives lädt. (3) **`extern "dotnet"`**:
Struct-Marshalling „fehlt" (`abi.md:113`); `abi.md:40` plant „Structs kreuzen per Kopie".

| Option | Vorbild | Preis |
|---|---|---|
| A: **Regel schreiben, für alle drei Wege**: „ein Native empfängt eine Kopie und darf sie halten, aber nichts, was es schreibt, erreicht das Skript" — und im Embedding-Gebiet **messen**, dass (2) sie einhält | Wasm Component Model (Canonical ABI: Werte werden gelowert, nie geteilt) | ein Test mit einem mutierenden Native |
| B: Natives dürfen Structs mutieren (Referenz) | Lua Userdata | bricht die Wertsemantik an der Grenze — nein |

**Empfehlung: A.** **Bricht:** nein. **Hängt ab von:** L1 (gepackte Puffer kreuzen anders), L2,
FFI-Gebiet, Embedding-Gebiet.

---

### L43 — Was kostet ein Panik-Lauf den Frame-Pool? *(neu)*

**Heute:** „A panic abandons its frames to the GC instead of recycling them — the backtrace is
built from them after the loop has left. That loses pool entries, never correctness; the next
call allocates fresh ones" (`Interpreter.cs:1563-1565`). Kein Leck; aber ein Host, der viele
panikende Läufe fährt (`lyrtest`, ein Server, der Fehler pro Anfrage meldet), zahlt nach jeder
Panik die Frames der tiefsten Kette neu — drei Allokationen pro Frame (`Rent`, ebd. 1573-1579).
**Nicht gemessen**: `lyrvm` endet mit der Panik; die Messung braucht einen Host mit vielen
`Run`-Aufrufen (Embedding-Gebiet).

| Option | Vorbild | Preis |
|---|---|---|
| A: so lassen | — | Reallokation pro Panik; zählt unter L7/B als Allokation |
| B: **nach dem Bau des Backtrace recyceln** — die Frames sind nach dem Loop-Austritt noch erreichbar (der Backtrace liest sie), danach können sie zurück; ausgenommen Frames, die in einer Kette hängen | — | ~20 LOC im `catch`-Pfad; braucht die Messung als Nachweis |

**Empfehlung: B im VM-Gebiet, nach der Messung.** **Bricht:** nein. **Hängt ab von:** L7, L16,
L25, Embedding-Gebiet.

---

### L44 — Welche Determinismus-Zusage gilt für Float-Arithmetik unter `--jit` und im zweiten Runtime? *(neu)*

**Heute:** gemessen (P12): f32/f64-Ketten bitgleich zwischen Interpreter und JIT — mit den vier
Grundoperationen. Gemessen (**R3i**): `0.1 * 10.0 - 1.0` ist `0` in beiden Engines für `float`
und `float32` — **keine FMA-Kontraktion** (mit FMA: `5.55e-17` bzw. `-1.49e-8`). Der JIT
emittiert typisierte `float`/`double`-Locals (`JitCompiler.cs:945, 1158`); dass RyuJIT nicht
implizit fusioniert und `float` nicht in `double`-Zwischenpräzision rechnet, ist .NET-Verhalten
(**behauptet**, durch R3i für diesen Fall gestützt). **Die Spec sagt dazu nichts** — und ein
C++-Compiler darf `a*b+c` per Default kontrahieren (GCC `-ffp-contract=fast` auf x86-64 ohne
`-std=c++XX`; ARM64 fusioniert häufig): das zweite Runtime kann heute konform sein und andere
Bits liefern.

| Option | Vorbild | Preis |
|---|---|---|
| A: **§3.2 bekommt den Satz**: „`float`/`float32`-Operationen sind IEEE-754 binary64/binary32 **pro Operation**, ohne Kontraktion und ohne erweiterte Zwischenpräzision; `float32` wird nach jeder Operation gerundet" | Java `strictfp` (seit 17 immer), Wasm (verbietet Kontraktion ausdrücklich) | ein Satz; für Lyricpp heißt er `-ffp-contract=off` |
| B: nichts | — | zwei Runtimes, zwei Ergebnisse |

**Empfehlung: A, sofort; R3i wird Konformanzfall.** **Bricht:** nein. **Hängt ab von:** L11,
L14, L40, Spec-Gebiet.

---

### Zuordnung der Kritikerfragen (keine eigene Designfrage)

Die sechzehn „fehlenden Fragen" des zweiten Kritikers, damit keine verloren geht: Struct beim
Capture → **L32**; `structcopy` durch `?Struct`/Interface-Felder → **L33**; Darstellung von Enum
und Tupel, Payload-Kopie → **L34**; Kosten einer Instruktion, Arbeit statt Instruktionen →
**L31**; `MaxCallDepth` bei zwei VMs auf einem Thread, `LYR-VM0004`-Unterscheidbarkeit → **L30**;
JIT lehnt Rekursion ab → **L35**; Tail Calls → **L36**; weitere .NET-Ausnahmen aus `Execute` →
**L37**; Kosten eines `string` gegen `char[]`/`uint8[]` → **L38**; GC-Konfiguration und
GC-Pause → **L39**; Spec gegen Implementierungsartefakt, zweites Runtime → **L40**; Wertdarstellung
außerhalb x64 → **L41**; Struct-Wert an einem Native → **L42**; Frame-Pool bei Panik-Läufen →
**L43**; `float32`/FMA unter `--jit` → **L44**; `isInteractive`/stdin-Vertrag → **L27**.

---

## 4. Was wir übernehmen sollten

Sortiert nach Vorbild, mit der Frage, in der es landet. Nichts davon ist ein neues Konzept
neben einem bestehenden; wo eine Übernahme Rule 2 berührt, steht es dabei.

| Vorbild | Was | Wohin | Preis / Rule-2-Bezug |
|---|---|---|---|
| **Erlang** | Reductions zahlen **Arbeit**, nicht Instruktionen (`length/1` pro Element) | L31/B: `Charge(n)` an ~10 Natives und Opcodes | ~120 LOC; das bestehende Budget bleibt der eine Mechanismus, es zählt nur ehrlicher |
| **Erlang** | der Prozess ist die Arena, der Supervisor räumt | L10/D′, L26/A: Sub-Arena `vm.Scope()`, kein `defer` auf Panik | die Rule-2-Frage aus `STATUS.md:2231-2234` wird mit „ein Mechanismus, zwei Granularitäten" beantwortet — braucht ein ADR |
| **Lua** | Allokationsfehler ist ein fangbarer Fehler (`LUA_ERRMEM`) | L19/B: OOM → `LYR-VM00xx` | ~12 Stellen |
| **Lua** | `[C]`-Frames im Traceback | L30/D: Host-Delegat als Frame im Backtrace | klein, braucht Messung mit Host |
| **Wasmtime** | jede Engine-Störung verlässt den Lauf als **ein** `Trap`-Typ | L19/D, L37/B: keine .NET-Ausnahme verlässt `Execute` außer `HostFunctionException` und Stack Overflow | eine `catch`-Klausel |
| **Wasm** | `memory.maximum` als harte Einzelgrenze | L7/E: `maxAllocationBytes` über die **vollständige** Liste inkl. Strings | Sofortmaßnahme, kein Ersatz für L7/B |
| **Wasm** | Kontraktion verboten, pro Operation gerundet | L44/A: Satz in §3.2 | ein Satz; Lyricpp kompiliert mit `-ffp-contract=off` |
| **Wasm** | `return_call` als **opt-in-Instruktion** | L36/C: `become f(x)`, nur falls Bedarf | neues Schlüsselwort — kontextuell, additiv; nicht vor einem Bedarf |
| **Wasmtime** | Fuel im kompilierten Code | L8/C, nur mit L20/B | kollidiert heute mit „a metered call is never compiled" (Guide 14) — §5 |
| **Go** | `staticuint64s`: kleine Werte boxen nicht | L4/B, nur bei nachgewiesenem Bedarf | ohne Cache-Objekt; heute kein Bedarf gemessen |
| **Go** | `loopclosure`-Vet | L22/D: Warnung „`var` gefangen und in Schleife geschrieben" | ~80 LOC Sema |
| **Go** | Write-through über einen Pfad, Kopie bei Bindung | L2: Lvalue-Pfad `w!.n = …` schreibt durch, rvalue-Bindung kopiert | die Regel, die B+A überhaupt erst widerspruchsfrei macht |
| **Go** | `GOMEMLIMIT` | L7/B: `MemoryBudget` | ~12 Stellen |
| **C#** | `checked` in drei Formen — der **Schalter** ist das Vorbild | L12: Profil-Schalter `overflow-checks` plus Funktionen | Format-Zuwachs, additiv; Paketregel wie Rusts crate-weise |
| **C#** | `Nullable<T>` hat durchgängige Wertsemantik | L33/B: `CopyStruct` rekursiert durch `?Struct` | O(n) für Ketten — das ist die Bedeutung einer Wertliste |
| **Swift** | Konformanz ohne Body = Synthese | L21/C′ | kein neuer Mechanismus; Sonderfall für genau zwei Interfaces in §5 benennen |
| **Swift** | Copy-on-Write | L18/B′: **als bewusstes Nein** | zweiter Speichermechanismus |
| **Python** | `PYTHONHASHSEED` | L23/C: fester Default-Seed, Host darf randomisieren | ~60 LOC; umgekehrt gepolt, damit Replay Default bleibt |
| **Python** | `sys.setrecursionlimit`; „[Previous line repeated N more times]" | L5/B+D | B nur als Deckel unter der Thread-Grenze (L30) |
| **Java** | `StrictMath` als **zweites Modul**, nicht zweiter Operator | L11/B, nur bei Bedarf | Portierung von fdlibm |
| **Java** | JEP 180 als Gegenbeispiel: Tree-Bins brauchen `identityHashCode`, das Lyric nicht hat | L23: D gestrichen | — |
| **Rust** | `-C overflow-checks` pro Crate, Verhalten beim Kompilieren fixiert | L12: Paketregel | — |
| **Rust** | `cargo fix` | L28/B: `lyrc --fix` nur für `mut struct` | Rewriter über Spans |
| **.NET** | PDB-artige Positionstabellen für kompilierten Code | L20/B | Speicher pro Funktion, heißer Pfad unberührt |
| **.NET** | GC-Knöpfe sind **Prozess**-Knöpfe | L39/A: die VM dreht keinen, Guide 14 sagt es | ein Absatz |
| **Deno** | parametrisierte Permissions, `revoke()` | L9/B, L9/D | B ohne Formatbruch |
| **Lyric selbst** | `ExecutionBudget` als Objekt mit `Consumed` | L24/B: `MemoryBudget` in derselben Form | dieselbe Begründung (`ExecutionBudget.cs:17-23`) |
| **Lyric selbst** | `appendix-a:278` „the code and the panic are the contract" | L40/A: §12.4-Tabelle Spec gegen QoI | eine Tabelle |

**Was wir ausdrücklich nicht übernehmen:** Gos automatisches `==` (L21/B — Gleichheit entsteht
ungewollt); Rusts `unsafe` mit Bounds-Check-Freischaltung (L13/C — `STATUS.md:2466-2470`);
Lua/Schemes implizites TCO (L36/B — verträgt sich nicht mit `defer` und mit der Tiefendiagnose);
V8s Deopt-und-Wiederholung (L20/D); MicroPythons Skript-sichtbaren GC (L24/D, L39/C); Finalizer
jeder Herkunft (L10/E — Guide 14:486-492 hält); einen dritten `LyrValue`-Slot (L4/C).

---

## 5. Konflikte

Konflikte zwischen Empfehlungen dieses Dossiers, mit den Projektregeln und mit anderen Gebieten.
Jeder hat eine vorgeschlagene Auflösung; wo sie ein ADR braucht, steht es.

| # | Konflikt | Auflösung |
|---|---|---|
| K1 | **L32/A (Capture kopiert, 4.7-Bugfix) gegen L2/B (`mut struct`, 5.0).** Wer 4.7 die Kopie einbaut, ändert dieselbe Stelle in 5.0 noch einmal (für ein unveränderliches Struct entfällt sie) | A trotzdem zuerst: es ist ein Spec-Bruch (07:82), kein Designraum. B subsumiert A; die 4.7-Zeile wird in 5.0 zur Bedingung „nur bei `mut struct`" |
| K2 | **L33/B (tief durch `?Struct`) gegen die Kosten rekursiver Strukturen** und gegen v5-Liste #23 („Wertsemantik für `?Struct`", `lyric-v5-features.md:60`), das die Kopie beim *Binden* meint, nicht die Tiefe der *Feld*-Kopie | beides ist dieselbe Regel, in §3.4 in einem Satz: *ein `?Struct` ist ein Wert wie das Struct darin — beim Binden und in der Kopie*. Wer eine geteilte Kette will, nimmt eine Klasse. Der Preis O(n) wird im Guide genannt |
| K3 | **L31/B (Arbeitsgebühr) und L7/B (`MemoryBudget`) gegen die Determinismuszusage des Budgets** („stops at the same instruction on every machine", `ExecutionBudget.cs:13-16`) — Bytes sind plattformabhängig (L41) | die **Gebühr** zählt Elemente/Einheiten (`n` folgt aus der Eingabe, deterministisch); das **Speicherbudget** zählt Bytes und ist ausdrücklich QoI (§12.4, L40). Zwei Zähler, zwei Zusagen; kein Rule-2-Verstoß, weil sie verschiedene Fragen beantworten (Zeit gegen Platz) |
| K4 | **L8/C (Fuel im JIT) gegen Guide 14:289-302** („A metered call is never compiled … The budget's promise is the reason") und gegen L20 | C nur mit L20/B **und** einer Neuformulierung der Zusage („innerhalb eines Basisblocks"); bis dahin gilt L20/E (gemetert = interpretiert), und das ist die Empfehlung |
| K5 | **L10/D′ (Sub-Arena) gegen die offene Rule-2-Frage in `STATUS.md:2231-2234`** („is ‚end this VM' and ‚end this run inside it' one mechanism or two?") | D′ antwortet „ein Mechanismus, zwei Granularitäten" — das ist eine Behauptung, die ein **ADR** braucht, nicht ein Nebensatz. Ohne ADR bleibt A (die VM ist die Arena) |
| K6 | **L23/C (Host-Randomisierung des Hash-Seeds) gegen L11 und den Replay-Vertrag** (`ExecutionBudget.cs:13-16`, Determinismus als Sprachziel) | Default-Seed fest → Replay bleibt Default; ein Host, der randomisiert, verzichtet **ausdrücklich** auf Replay, und die Doku sagt es an der Option |
| K7 | **L36/C (`become`) gegen Rule 2** — eine zweite Aufrufform | nur bei nachgewiesenem Bedarf, mit ADR; die Empfehlung ist A (kein TCO). C steht als *einzige* Form da, die überhaupt in Frage käme |
| K8 | **L5/B (Tiefe pro VM) gegen L30 (Tiefe pro Thread)** — zwei VMs auf einem Thread mit verschiedenen Deckeln | die VM-Zahl ist ein **Deckel unter** der Thread-Grenze; die Diagnose nennt beide Zahlen; ohne L30/A+C (Doku + eigener Re-Entry-Code) kommt B nicht |
| K9 | **L1/C (gepackte Skalar-Arrays) gegen die FFI-Oberfläche** (`Bytes()`/`ToBytes`, `NativeRegistry.cs:2736-2746`; Native Roots; `abi.md:40` „`uint8[]` … ein .NET-Array im Host") und gegen die Formatstabilität, die 4.6 gehalten hat (Format 4.0) | Formatbump ist 5.0-Material; Natives, die `LyrValue[]` elementtyp-agnostisch lesen, bekommen in 4.7 eine Deprecation. Gehört ins FFI- und ins Bytecode-Gebiet, nicht allein hierher |
| K10 | **L12 (Profil-Schalter `overflow-checks`) gegen M37-Pakete** (kompiliertes Lyric mit Header) und gegen den Wunsch nach *einem* Konstrukt | das Verhalten ist beim Kompilieren des Moduls fixiert und steht im Paket-`lyric.json`; **kein** `checked { }`-Block daneben (Sprachform sind Funktionen, L12/C) |
| K11 | **L21/C′ (Konformanz ohne Body) gegen §5.1** (Konformanz vergleicht Signaturen exakt; ein leerer `extend` ist heute ein Fehler wegen fehlender Methoden) | ein benannter Sonderfall für genau `Equatable`/`Hashable` (später `Ordered`, `Display` per v5-Liste #3) in §5; alles andere bleibt Fehler. Kein Attribut-Codegenerator |
| K12 | **L19/E (vergiftete VM nach OOM) gegen Guide 14:497-501** („A disposed VM still computes, but opens nothing") — zwei Nach-Fehler-Zustände | **einer**: „vergiftet" ist derselbe Zustand wie „disposed" (rechnet nichts mehr / rechnet noch — das muss entschieden werden); Empfehlung: vergiftet = jeder weitere `Run` ist `ScriptException`, weil ein Heap nach OOM nicht mehr vertrauenswürdig ist. Embedding-Gebiet |
| K13 | **L40 (Spec gegen Artefakt) gegen das zweite Runtime**, das nicht im Checkout liegt (Lyricpp — **behauptet** aus den Projektnotizen) | die Tabelle in §12.4 wird geschrieben, **bevor** Lyricpp sie braucht; jede Zeile mit dem Wert der Referenz in Klammern. Was Lyricpp heute tut, kann dieses Dossier nicht messen |
| K14 | **L13/B′ (kein `unsafe`-Wort) gegen `design/abi.md:75`** (plant den `unsafe { }`-Block als einzige Stelle für `CPtr`-Zugriffe) | Designdokument-Konflikt, im **FFI-Gebiet** zu entscheiden; dieses Dossier trägt nur bei: `unsafe` darf **nie** Bounds-Checks entfernen (`STATUS.md:2466-2470`) |
| K15 | **L26/A (kein `defer` auf Panik) gegen `LYR-SEM0110`** (werfender `defer`, 5.0 entscheidet) und gegen das Fehler-Gebiet | A ändert an der Panik-Regel nichts; was ein *werfender* `defer` auf dem normalen Pfad tut, ist die Frage des Fehler-Gebiets. Kein Widerspruch, aber dieselbe Spec-Stelle (07:130-132) — zusammen ändern |
| K16 | **L2/A (Kopie an jedem rvalue-Bindepunkt) gegen L34** (Enum-Payload/Tupel: Bindung kopiert schon; R3f) | R3f wird Konformanzfall; L2/A darf die `match`-Bindung nicht ein zweites Mal kopieren (`_fresh`-Logik) |
| K17 | **L27/C (`isInteractive` aus injizierten Streams) gegen `NativeRegistry.cs:314-316`** („part of the process rather than an access decision") | kein Widerspruch: C macht die Antwort *zum injizierten Stream* konsistent, es führt kein Bit ein. Nur A+C, nie B |

**Konflikte mit anderen Gebieten, die dort entschieden werden müssen:** Bytecode-Gebiet (L1
Opcodes, L12 Flag/Opcodes, L33/B Innentyp im Feldeintrag, L36/C Opcode); FFI-Gebiet (L1, L13,
L42); Embedding-Gebiet (L7, L8, L10/D′, L19, L24, L29, L30/D, L37, L39/D, L43); Fehler-Gebiet
(L10, L19, L26); Nebenläufigkeits-Gebiet (L6, L10 Koroutinen-Handles, L29); stdlib-Gebiet (L11,
L23, L31 Natives); Strings-Gebiet (L38); Interface-/Pattern-Gebiet (L15, L21); Spec-Gebiet (L40,
L44). Wo dieses Dossier eine Empfehlung gibt, die dort anders fällt, gilt die des Gebiets, das
den Mechanismus besitzt.

---

## 6. Nach der Kritik geändert

**Falsche Aussagen korrigiert (nachgeprüft, nicht übernommen):**

- §1.2/L22: „zwei Antworten, keine geschrieben" war falsch. Eine Regel (`let` als Wert, `var` als
  Zelle, `spec/07:82-85`), `for` bindet per `let` (gemessen R3c: `LYR-SEM0019`). L22/B und L22/E
  gestrichen; L22 fragt jetzt nach dem Wortlaut-Widerspruch 03:77/07:82 und der Warnung.
- §1.3/L30: „nichts verbindet die beiden Stäcke" war falsch. `_outerFrames` koppelt `MaxCallDepth`
  thread-weit über verschachtelte Läufe (`Interpreter.cs:142-157, 526-535`). L5/B ist jetzt ein
  Deckel unter der Thread-Grenze; L30 ausgeführt; beide Grenzen teilen den Code `LYR-VM0004` (neu
  als Befund K).
- §1.7 A / L2: die Familie hat **fünf** Mitglieder, nicht drei — `?Struct`-Feld und Interface-Feld
  in der Kopie (nachgemessen R3a) und das Capture eines `let`-Structs (nachgemessen R3e, ein
  Spec-Bruch). L2/A hat fünf Stellen (~150 LOC), nicht „drei, ~60". L2 sagt jetzt, was ein
  Schreibpfad durch `?T` unter B+A bedeutet (Lvalue = Write-through).
- §1.2/L2 Zahlen: „Klasse 17×, ≥10×, mehrfach reproduziert" zurückgenommen. Neu gemessen (drei
  Läufe je Zelle, R3q9): Klassen-JIT-Gewinn ≈7,5× hier, ≈4,5× beim Kritiker; Struct/Klasse unter
  JIT ≈12× hier, ≈6,5× beim Kritiker; robust ist nur „Struct 0,97–0,99×, Klasse eine
  Größenordnung". Alle drei Messanordnungen stehen nebeneinander.
- §1.4/§1.7 J / L27: Konsole und `secureRandom` sind dokumentierte Entscheidungen mit
  Host-Mitigation (`NativeRegistry.cs:314-316`, `random.lyr:22-24`), keine Modelllücke. Die
  echte Inkonsistenz ist `isInteractive` (ebd. 333-335). L9/D hat damit nicht mehr die falsche
  Vorbedingung.
- §1.5/L10/L17: Datei-Tabelle ist `NativeRegistry.cs:2344-2347`; `:1438-1441` ist die
  Socket-Tabelle.
- Dokumentstruktur: L24–L31 und „Nach der Kritik geändert" existieren jetzt. L10 hängt nicht mehr
  an einem nicht existierenden L26 — L26 ausgeschrieben ergibt: L10/B lohnt nicht, D′ allein.
- §2/L1/L2/L5/L20: der JIT lehnt **Rekursion** ab (`JitCompiler.cs:721-724`, gemessen R3h:
  `fib(27)` ohne Gewinn, voller Backtrace unter `--jit`). Neu als L35 und Befund L.

**Vergleichssprachen korrigiert:**

- L21: Swift synthetisiert nur bei deklarierter Konformanz (opt-in); C# `==` auf `struct` ist
  CS0019, nur `record struct` synthetisiert. Nur Go ist automatisch. Empfehlung von C
  (Attribut = neuer Codegenerierungsmechanismus, Rule-2-pflichtig — die zweite Fassung nannte
  ihn fälschlich „kein neuer") auf **C′** (Konformanz ohne Body, Swifts Modell, ohne neuen
  Mechanismus).
- L23: Java verlangt für Tree-Bins kein `Comparable` (`tieBreakOrder`); und Lyric könnte
  `Ordered<K>` zur Laufzeit nicht erkennen. D gestrichen; Empfehlung jetzt C mit **festem
  Default-Seed** und Host-Schalter (Python-Modell, umgekehrt gepolt). HashDoS ist nicht „der
  einzige Weg" (→ L31).
- L12/§2: C# hat `checked`-Block, `checked(expr)` **und** den Compiler-Schalter — das Vorbild für
  den Profil-Schalter war falsch allein Rust zugeschrieben. Neu: der Schalter ist ein
  Format-Zuwachs; das Paketproblem (geprüfte Bibliothek in ungeprüfter App) ist benannt und mit
  Rusts crate-weiser Regel beantwortet.
- §2 Go: Stack 2 KB seit 1.4 (8 KB war 1.2/1.3), adaptiv seit 1.19; itab ist eine Methodentabelle,
  Lyric ein Typindex — gleiche Form, anderer Mechanismus; Go boxt Ein-Byte-Werte nicht
  (`staticuint64s`) — das ist jetzt das Vorbild für L4/B statt Javas `Integer`-Cache.

**Schwache Empfehlungen nachgebessert:**

- L11: D nannte `log`/`exp` und vergaß `cos` — `nextGaussian` wäre plattformabhängig geblieben.
  D gestrichen; E (Inverse-CDF, ganzzahlig, keine Transzendente) ist die Empfehlung für
  `std.random`.
- L7: die Allokationsliste hatte Strings, f-Strings, `fromInt` und Native-Rückgaben vergessen;
  E („Sofortmaßnahme") hätte `s = s + s` nicht gefangen. Liste jetzt ~12 Stellen, in L7 und L19
  identisch.
- L4: die „Vorfrage" ist gemessen (grep über `stdlib/`): null Skalar-in-Interface-Bindungen, und
  `core.lyr:94-99` dokumentiert die Ablehnung als Entscheidung. Empfehlung A ist damit begründet,
  nicht vertagt.
- L5: B setzt keine Entkopplung mehr voraus, die es nicht gibt.
- L10: siehe oben; L26 beziffert das Restbudget (Default 0, zweite Panik, Handles bleiben) und
  kommt zu dem Schluss, dass es nicht lohnt.

**Neue Fragen (L31–L44)** mit Messungen R3a, R3c, R3e, R3f, R3g, R3h, R3i, R3j, R3q9 — jede mit
Erwartung vor dem Lauf im Probenkopf, überraschende Ergebnisse mit Kontrolle (`plainField`,
`frischeKopie`, `klassePayload`, Q6b als Backtrace-Kontrolle, `leer` als Speicherkontrolle).

**In der Zusammenführung vom 2026-09-28 zusätzlich:**

- Kontrollrunde rev4: R3a, R3c, R3e, R3f, R3g, R3h, R3i und Q6b nachgefahren, Erwartungen vorher
  notiert (`probes/laufzeit-rev4/ERWARTUNG.txt`), alle Ergebnisse bestätigt. Präzisiert:
  `fib(27)` ist unter `--jit` nicht langsamer, sondern im Rauschen — „kein Gewinn" bleibt.
- Zwei Zitate korrigiert: das `host`-Bit parst `src/Lyric.Core/Capabilities.cs:125-128` (nicht
  `Program.cs:223`, das ist nur die `--help`-Zeile); `HostFunction.cs`, `LangVm.cs`,
  `HostOptions.cs` liegen unter `src/Lyric.Embedding/`, `VmHost.cs` unter `src/Lyric.Vm/`.
- L45 war keine Designfrage, sondern eine Zuordnungsliste; sie steht jetzt unnummeriert am Ende
  von §3. Das Dossier hat 44 Fragen.
- Die Abschnitte „Was wir übernehmen sollten" (§4) und „Konflikte" (§5) fehlten in beiden
  vorherigen Fassungen und sind neu.
- L21/C′ ist mit der v5-Liste abgeglichen (`lyric-v5-features.md:30`, #3 „Swift-Stil, kein
  `derive`") — die Empfehlung war schon vorher richtig, jetzt hat sie den Beleg.
- Gos Nicht-Boxing gilt für Ganzzahlwerte 0…255 jeder Breite (`convT16/32/64`), nicht nur für
  Ein-Byte-Werte; L4/B entsprechend.

**Nicht übernommen, mit Begründung:**

- Die Kritik nennt bei §1.7 A „Kopiert wird an genau einem Übergang" als unvollständig. Der Satz
  bleibt richtig — es gibt genau einen Übergang, der kopiert (das erste Einpacken eines nackten
  Structs); unvollständig war die **Liste der Übergänge, die nicht kopieren**. So steht es jetzt.
- Die Kritik rahmt Konsole/Entropie als „dokumentierte Entscheidung". Übernommen — aber die
  Messung Q12 bleibt im Dossier, weil die Entscheidung in einem Quelltextkommentar steht und
  nicht in Guide 14; das ist L27/A.
- Die Kritik fragt, ob „16 B pro Wert" eine Zusage sei. Antwort L41: nein, und die Zahlen in
  L1/L7 sind ausdrücklich x64-Messungen. Der Ist-Stand wurde nicht geändert, nur gekennzeichnet.

