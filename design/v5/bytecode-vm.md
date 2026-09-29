# Bytecode-Format und VM — Dossier für Lyric 5 (Fassung 2 nach Kritik)

Gebiet: `.lyrbc` 4.0, Stack-VM, Reader/Verifier, Optimierer, JIT, Backtraces, Ressourcengrenzen,
numerische Semantik, Registerfrage.
Stand gemessen gegen `lyrvm 4.6.0 (.lyrbc 4.0)` / `lyrc 4.6.0`, Debug-Binaries, HEAD `6f6f029f`.
Spec-Spiegel geprüft: `docs/Bytecode.md` und `lyric-spec/spec/13-bytecode.md` sind unter dem
`sync:body`-Marker byteidentisch (diff leer, 2026-09-28).

Proben: `…/scratchpad/v5-design/probes/bytecode-vm/` (Lauf 1, 2026-09-24),
`…/bytecode-vm-r2/` und `…/bytecode-vm-review/` (Kritik-Läufe, hier nachgelaufen),
`…/bytecode-vm-r3/` (neu, 2026-09-28: Koroutinen-Backtrace, Handler-Regionen, Integer-Semantik).
Erwartungen standen vor jedem Lauf in `ERWARTUNG.txt` des jeweiligen Ordners.

Konfidenz-Legende: **gemessen** = selbst gelaufen; **gelesen** = Pfad:Zeile; **behauptet** =
Sprachwissen ohne Beleg im Baum (gilt für alle Aussagen über Fremdsprachen).

---

## 0. Kurzfassung

1. **Der Validierungsvertrag der Spec ist für die Sandbox-Zusage zu schwach — und die Laufzeit
   erfüllt ihn.** §6 (`docs/Bytecode.md:973-994`) definiert „validate completely" als BC0001–BC0006
   plus Indexregel und sagt wörtlich, Feldzugriff sei danach „an unchecked array access" (:987).
   Der Reader tut genau das. Handgebaute Module, die `verify` mit `ok` passieren, töten den Prozess
   trotzdem: falscher Typ im Slot, nie beschriebener Slot, 2-Feld-Layout über 1-Feld-Objekt.
   Exit auf Windows **0xE0434352**, nicht 127 (die Zahl in Fassung 1 war die MSYS-Abbildung).
2. **`lyrvm verify` beantwortet §8.7 nicht.** Ein Modul mit unbekanntem Capability-Bit ist für
   `verify` `ok` und für `run` `LYR-CAP0001` — mit leerem Namen. Die Spec legt die Bit-Prüfung dem
   *Reader* zu (`:216-219`), der Code dem Loader (`LoadedProgram.cs:76-81`).
3. **Ein 58-Byte-Modul allokiert beliebig viel Speicher.** `maxStack` ist bis `int.MaxValue`
   gültig (`ByteReader.cs:68-75`), der Rahmen wird daraus dimensioniert (`Interpreter.cs:1579`).
   `maxStack = 100 000 000` → 1,6 GB, still, exit 14.
4. **Die Spec definiert keine numerische Semantik.** Wrap, Division durch null, MIN/−1, Shift ≥
   Breite, NaN→int, −0.0: alles ist im Interpreter entschieden (`Interpreter.cs:1309-1346`,
   `:1453-1485`), nichts davon steht in §5. `docs/Bytecode.md:9-10` verspricht ein zweites Runtime
   „from this document alone".
5. **Der Release-Backtrace paart Namen und Zeilen falsch**, flach *und* durch Koroutinen-Ketten
   hindurch (`main.gen.<body> (bt_coro.lyr:2)` — Zeile 2 liegt in `inner`).
6. **`match` über ein Enum kostet 6 Instruktionen pro Arm** und bekommt weder die Fusion (die
   Konstante reist durch einen Slot) noch den JIT (Enums abgelehnt). Das zentrale Sprachmerkmal
   seit M36 läuft am langsamsten Pfad.
7. **`2 + 3` wird nicht gefaltet**, auch im Release nicht; es gibt drei IR-Pässe und keinen
   für Konstanten, keinen für Blockzusammenlegung, keinen für Slot-Kompaktion.
8. Die Registerfrage bleibt entschieden (`STATUS.md:1331-1357`). Sektions-id 0 ist frei und ladbar
   — Wasms Custom-Section-Slot liegt unbelegt bereit.

---

## 1. Ist-Stand mit Belegen

### 1.1 Das Format in einem Absatz

Magic `LYRB`, `u16` major + `u16` minor, dann Sektionen `{id u8, len uleb, payload}` in streng
aufsteigender id-Reihenfolge, jede höchstens einmal (`docs/Bytecode.md:86-90`, `:111-112`;
Reader: `BytecodeReader.cs:76-80`). 14 ids vergeben (`:128-145`). Unbekannte major → Ablehnung,
unbekannte minor → toleriert; eine minor darf nur hinzufügen, was ein Modul nicht benutzen muss —
Kompatibilität **pro Modul** (`:114-126`).

Ausführung: Stack-Maschine, Operandenstack an jeder Blockgrenze leer (`:630-636`), Sprünge auf
Blockindizes (`:638-641`), Typ-Tag im Instruktionsstrom, nicht im Wert (`:647-649`;
`LyrValue.cs:5-9`).

**Gemessen** (Lauf 1, `build3.py`, plus Kritik-Proben nachgelaufen 2026-09-28):

| Probe | Erwartung | Ergebnis | Konfidenz |
|---|---|---|---|
| major 5 | Ablehnung | `LYR-BC0002`, exit 1 | gemessen |
| minor 99 bei major 4 | toleriert | läuft, exit 14 | gemessen |
| unbekannte id 40 am Ende | übersprungen | läuft, exit 14 | gemessen |
| id 40 zwischen 4 und 5 | Ablehnung | `LYR-BC0005` | gemessen |
| **id 0 als erste Sektion** (`sec0.lyrbc`) | ? | `verify ok`, `run` exit 14 | gemessen |
| **id 255 als letzte** (`sec255.lyrbc`) | ? | `verify ok` | gemessen |
| id 0 zweimal (`sec00.lyrbc`) | Ablehnung | `LYR-BC0005: section id 0 is out of order` | gemessen |
| Capability-Bit 63, **`verify`** | Ablehnung (§8.7) | **`ok`, exit 0** | gemessen |
| Capability-Bit 63, **`run`** (ohne und mit `--grant all`) | Ablehnung | `LYR-CAP0001: module requires capability ''` | gemessen |

Die Versionsversprechen halten. Zwei Korrekturen zu Fassung 1: die Aufsteigend-Regel ist **kein
Mindestwert** — id 0 und 255 sind heute frei und ladbar; und die Capability-Ablehnung ist eine
Lade-, keine Verify-Prüfung. Der leere Name kommt aus `CapabilityTable.Describe`
(`Capabilities.cs:103-113`): fünf bekannte Flags, alles andere ergibt `""`.

### 1.2 Was der Reader prüft — und was nicht

**Strukturell und relational** ist er gründlich. Positivkontrolle (`noimpl.lyrbc`): `mkiface`
ohne Impls-Zeile → `LYR-BC0004: 'C' has no impl row for 'I'`, wie `:848-849` verspricht.
Truncation (`trunc9`, `trunc20`) → `LYR-BC0003` mit Byteposition; `trunc3` (Start-Sektion fehlt)
→ `ok`, korrekt: eine Bibliothek (`:1129-1130`).

**Typfluss** prüft er nicht. `ValidateStack` (`BytecodeReader.cs:1220-1274`) rechnet pro Block
nur Tiefen: `pops`/`pushes`, Vergleich gegen `maxStack`, Tiefe 0 am Terminator. Kein Typvektor,
keine Slot-Typen. `ValidateArithmeticTag` (`:1047-1082`) prüft, ob das *Tag im Strom* zur
Operation passt — nicht, ob der Wert, der dort liegt, dieses Tag hat.

**Das ist konform zur Spec**, und das ist der Befund: §6 (`:973-994`) zählt auf, was „validate
completely" heißt — sechs Codes, die Indexregel für `newobj/ldfld/stfld`, die Fused-Regeln — und
sagt für Felder „Field access at runtime is then an unchecked array access" (`:987`). Reader und
Spec stimmen überein. Was zu schwach ist, ist der **Vertrag**, nicht seine Erfüllung. (Fassung 1
hatte `:973` als „beschreibt eine Laufzeit, die es nicht gibt" gelesen — falsch gelesen; die
Kritik hat recht.)

Handgebaute Module, alle `verify ok`:

| Probe | Was sie tut | Ergebnis `run` | Windows-Exit | Konfidenz |
|---|---|---|---|---|
| `confuse.lyrbc` | zwei `string` mit `add i64` | exit 0, **stille falsche Antwort** (String hat `Bits = 0`) | 0 | gemessen |
| `nullref.lyrbc` | `i64` in `&C`-Slot, dann `ldfld` | `Unhandled exception. System.InvalidOperationException: null object reference` (`LyrValue.cs:130`) | **0xE0434352** | gemessen |
| `oob.lyrbc` | 1-Feld-Objekt durch 2-Feld-Layout | `System.IndexOutOfRangeException` | 0xE0434352 | gemessen |
| `uninit.lyrbc` | Slot 0 als `&C` deklariert, **nie beschrieben**, `ldloc 0; ldfld` | wie `nullref` | 0xE0434352 | gemessen |
| `uninit_ctl.lyrbc` | dasselbe mit `newobj; stloc 0` davor | exit 0 | 0 | gemessen |
| `control.lyrbc` | Gerüst von `confuse` mit echten `i64` | exit 14 | 14 | gemessen |
| `cross.lyrbc` | zwei Catch-Regionen `[0,2)` und `[1,3)`, **kreuzend** | `verify ok`, exit 0 | 0 | gemessen |
| `catchstruct.lyrbc` | `catchType` = Struct-Eintrag | `verify ok` | 0 | gemessen |
| `catchslot.lyrbc` | `catchType` = `&C`, Bindeslot ist `i64` | `verify ok` | 0 | gemessen |
| `nested.lyrbc` | Kontrolle, sauber verschachtelt | `verify ok` | 0 | gemessen |
| `maxstack_100m.lyrbc` (57 B) | `maxStack = 100 000 000` | läuft 0,99 s, exit 14 — **1,6 GB allokiert** (`Interpreter.cs:1579`, 16 B/Slot) | 14 | gemessen |
| `maxstack.lyrbc` (58 B) | `maxStack = 2 000 000 000` | `verify ok`; `run` > 60 s (Kritik-Messung, nicht wiederholt: 32 GB) | — | Kritik gemessen |
| `slotbomb.lyrbc` (4 MB) | 4 Mio. Slot-Typen | `verify ok` in 1,2 s | 0 | gemessen |

**`uninit` ist die Probe, die den Plan von Fassung 1 kippt**: alle Slot-Typen sind konsistent, ein
Typvektor pro Block findet nichts. Was fehlt, ist entweder Definite Assignment über den CFG (ein
Fixpunkt — genau das, was die JVM mit `top` in der StackMap trägt) **oder** ein definierter
Anfangswert für Slots. Die Spec regelt nur Felder (`:783-784`: „No field is ever uninitialized",
Referenzen → null) und String-Globals (`:423`); zu Slots schweigt sie.

Und weil ein Feld laut `:784` legal die Null-Referenz trägt, ist **Null-Dereferenz aus einem
regulären Modul erreichbar**, ganz ohne uninitialisierten Slot: `newobj C; ldfld C.0; ldfld D.0`
bei `class C { d: D }`. `LYR-VM0007` heißt zwar `NullDereference` (`VmDiagnostics.cs:49`), gilt
aber nur für `optget` auf `none` (`Interpreter.cs:1011`); `ldfld` auf null wirft
`InvalidOperationException` (`LyrValue.cs:129-130`).

Der Exit-Code: PowerShell `$LASTEXITCODE` = **−532462766 = 0xE0434352** für alle drei
Absturzproben (gemessen); 127 war die Abbildung der MSYS-Bash. Linux zeigt 134 (SIGABRT;
`STATUS.md:1368-1369` nennt „exit 134/141" für dieselbe Fehlerklasse). §8.2 (`:1084-1094`) kennt
weder das eine noch das andere.

Was das Sandbox-Modell daraus macht: `docs/Bytecode.md:224-227` sagt, die Capability-Prüfung
existiere für „hand-built bytes — which is who the check exists for". Für Capabilities gilt das
(`NativeRegistry.cs:180-190`, Nutzungs-Bound gemessen in `abi.md:108`). Für Typen gilt es nicht.
Und die .NET-Array-Grenzprüfung hat `oob` aufgefangen — **Lyricpp in C++ hätte an derselben Stelle
einen echten Out-of-Bounds-Read.** `STATUS.md:2183-2186` führt den Posten als Werkzeuglücke; er
ist ein Sandbox-Posten.

### 1.3 Der Optimierer

Drei Pässe im IR (`IrPasses.cs`: `Inline`, `ScalarReplacement`, `Devirtualize`), Fusion im
Emitter (`Emit/Fusion.cs`). `debug` = alle aus, `release` = alle an (`Profile.cs:22-29`).

Belegte Wirkung (`STATUS.md:86-105`, gemessen dort): der Debug-Preis ist höchstens 2,7× auf
struct-lastigen Schleifen. Die **drei Regressionen** (`STATUS.md:106-113`), im Quelltext bestätigt:

1. `ForwardLocals` forwarded über Blockgrenzen: `storeCount` ist funktionsglobal
   (`ScalarReplacement.cs:104-120`), die Korrektheit lehnt sich an Definite Assignment der Sema
   (`:92-99`). Der Fund ist ein **Performance-Fund**, kein Korrektheitsfund — Fassung 1 hatte
   einen Dominanztest empfohlen; der ändert an 13-statt-11 nichts. Die Reparatur ist „nur
   blockintern oder nur Struct-Werte forwarden" (`STATUS.md:110-111`).
2. Der eingespleißte `call` führt 15 Instruktionen aus, wo der echte 11 ausführt.
3. Blockinterne Kopien (`stloc 13; ldloc 13`) bleiben stehen.

**Eigene Messungen** (Lauf 1 und 2026-09-28):

| Fund | Beleg | Konfidenz |
|---|---|---|
| Kein Blockzusammenlegen: `bb4: br bb6` überlebt den Release-Bau | `hot_rel` Disassembly | gemessen |
| **Keine Konstantenfaltung**: `fn main(): int { return 2 + 3; }` → `const 2; const 3; add i64; retval` auch mit `--release` | `fold_rel` Disassembly | gemessen |
| **Keine Slot-Kompaktion**: `main.main` in `hot_rel` trägt `l2, l3, l4 (string)`, die keine Instruktion referenziert; `bb5` liegt außerhalb der Flussreihenfolge | `hot_rel` Disassembly | gemessen |
| **`match`-Arm = 6 Instruktionen**: `const i64 k; stloc n; ldloc tag; ldloc n; eq i64; condbr` — die Konstante reist durch einen Slot, also greift die Fusionsregel „Temps auf dem Stack" (`Fusion.cs:64-66`) nie, kein `brcmpk eq` | `enummatch_noinl` Disassembly `main.score` | gemessen |
| Fusion im Interpreter: 4,95 s → 6,14 s ohne (`--no-fusion`), 20 Mio. Runden = **1,24×**; unter `--jit` 0,17 s / 0,15 s — im Rauschen | Lauf 1, min aus 3 | gemessen (Lauf 1) |
| `acc = (acc + i) & 1023` bleibt sechs Stack-Instruktionen | `hot_rel` bb2 | gemessen |

Die Fusion ist eine Interpreter-Optimierung, keine Format-Verbesserung — unter dem JIT ist sie
unsichtbar. Der `match`-Befund ist der wichtigste dieser Tabelle: das Sprachmerkmal, das M36
vollständig gemacht hat, erreicht keine der beiden Beschleunigungen.

### 1.4 Der JIT

IL-Emission pro Funktion, faul beim ersten Aufruf, Ablehnung normal (`JitCompiler.cs:29-36`).
Abgelehnt: Closures, Exceptions, **Enums**, Rekursion, schmale Integerbreiten (`:35-36`). Enums
heißt: jedes `match`.

Gemessen: `hot` 20 Mio. Runden 4,95 s interpretiert, 0,17 s mit `--jit` (Lauf 1); 2026-09-28:
`hot_rel --jit` **155 ms gesamt** bei 140–146 ms für ein leeres Programm. Die Schleife kostet
kompiliert praktisch nichts. Opt-in (`STATUS.md:2505-2508`).

**Drei Bedingungen, die Fassung 1 übersehen hat** (Kritik, gelesen bestätigt):

- **Budget oder Debugger ⇒ interpretiert.** `IExecutionPolicy.AllowsCompiledCode`
  (`Interpreter.cs:17-26`): „compiled code has no instruction boundaries … a budget cannot
  count it … that is not a limitation to be worked around later but the contract".
  `BudgetPolicy` (`:103-106`): `AllowsCompiledCode => false`. Der Einstieg prüft
  `debug is null && budget is null` (`:248-249`). **Der Host, für den die Sandbox das Produkt ist
  — Erato mit Budget für Modder-Skripte — bekommt vom JIT nichts.**
- **NativeAOT ⇒ `Compile` still ignoriert** (`HostOptions.cs:65-67`). Kein csproj im Baum setzt
  `PublishAot` oder `PublishReadyToRun` (grep leer; `Directory.Build.props:18` setzt nur
  `InvariantGlobalization`) — heute also nicht akut, aber ein Profilfeld, das je nach
  Host-Laufzeit nichts tut, braucht mindestens eine Diagnose.
- **Kompilierte Rahmen fehlen im Backtrace** (`Interpreter.cs:257-268`: „A BACKTRACE OF ONE
  LINE, and that is the honest cost of compiling"), gemessen unten.

Die Ablehnungsliste liegt vor (`JitContext.cs:71-82`, `ScriptInstance.cs:42`) und `lyrvm` hat
keine Option, sie zu drucken (`lyrvm --help`, gemessen). Der Guide kennt den JIT an genau einer
Stelle: `docs/guide/11-coroutines.md:146`, als Wand.

### 1.5 Backtraces

**Flach** (`bt.lyr`, `main → middle → inner`, Indexfehler in `inner`, Lauf 1):

| Bau | Ausgabe |
|---|---|
| `--release` | `in 'main.main'` / `in main.main (bt.lyr:4)` — **Name des Aufrufers, Zeile des Eingespleißten** |
| `--release --no-inline` / `--debug` | drei korrekte Rahmen |
| `--release --jit` | `in main.main` **ohne Zeile** |
| `--no-inline --jit` | `in 'main.inner'` / `in main.main` — Name und Rahmen widersprechen sich |

**Durch eine Koroutinen-Kette** (`bt_coro.lyr`, `main → drive → resume → gen.<body> → inner`,
neu 2026-09-28; Erwartung: Debug 4 Rahmen, Release gespleißt, JIT unklar):

| Bau | Ausgabe |
|---|---|
| `--debug` | `in main.inner (:2)` / `in main.gen.<body> (:8)` / `in main.drive (:14)` / `in main.main (:19)` — **4 Rahmen, korrekt, Chain-Rahmen im Resumer eingespleißt** |
| `--release` | `in 'main.gen.<body>'` / `in main.gen.<body> (bt_coro.lyr:2)` / `in main.main (bt_coro.lyr:14)` — Zeile 2 liegt in `inner`, Zeile 14 in `drive` |
| `--release --no-inline` | wie `--debug` |
| `--debug --jit` | `in 'main.inner'` / `gen.<body> (:8)` / `drive (:14)` / `main (:19)` — **der kompilierte `inner`-Rahmen fehlt**, der Rest steht |
| `--release --jit` | wie `--release` |

Der logische Stack ist der physische, wie `STATUS.md:841-846` verspricht; die Fehlzuordnung des
Inliners geht mit durch. Neu gegenüber `STATUS.md:121-126`: unter `--jit` verschwindet nicht
„alles unterhalb", sondern **genau der kompilierte Rahmen** — die interpretierten darüber bleiben,
weil der Chain-Body ein `yield` enthält und der JIT Ketten ablehnt (`STATUS.md:848`).

Tiefenpanik: `LYR-VM0004` bei `MaxCallDepth = 1024` (`Interpreter.cs:139`) druckt **1026 Zeilen**
(Lauf 1, `deep.err`). Keine Kürzung.

### 1.6 Numerische Semantik: gemessen, nicht spezifiziert

`intsem.lyr` und `num.lyr` (Release; Erwartung vorher notiert, alle getroffen):

| Ausdruck | Ergebnis | Implementierung |
|---|---|---|
| `MAX + 1` | `-9223372036854775808` (wrap) | `Interpreter.cs:1309` `unchecked` |
| `MIN / -1` | `-9223372036854775808` (wrap) | `:1323-1325` „Lyric wraps as every other integer operation does" |
| `MIN % -1` | `0` | `:1325` |
| `1 << 65` | `2` (mod Breite) | `:1335-1337` |
| `MIN >> 63` | `-1` (arithmetisch) | `:1339-1340` |
| `1 / 0` | `panic [LYR-VM0002]: division by zero`, exit 101 | `:1315-1317` |
| `NaN as int` | `0` | `:1453-1458` „WASM's trunc_sat behaviour" |
| `±Inf as int`, `1e300 as int` | saturiert auf MIN/MAX | `:1461-1465` |
| `0.0 * -1.0` | `-0` | IEEE |
| `NaN == NaN` | `false` | IEEE |

**Nichts davon steht in `docs/Bytecode.md` §5** (`:673-735`): weder Wrap noch Division durch null,
noch MIN/−1, Shift-Modulo, Sättigung, `f32` in einfacher Genauigkeit (`:1301-1302`). `:34` verweist
auf „the same overflow and comparison rules", die nirgends stehen. Der Kommentar in `:1456-1457`
sagt selbst, wofür das definiert wurde: „so the same `.lyrbc` gives the same result on every
runtime" — aber im Code, nicht im Dokument, aus dem das zweite Runtime gebaut werden soll (`:9-10`).

### 1.7 Was 4.0 nicht hat

| Fehlend | Beleg | Konfidenz |
|---|---|---|
| Typprüfung des Codes beim Laden | §1.2 | gemessen |
| Anfangswert eines Slots / Definite Assignment | `uninit.lyrbc`; Spec `:783-784` nur Felder | gemessen + gelesen |
| Ressourcengrenzen im Reader | `maxstack_100m`; `ByteReader.cs:68-75` cap = `int.MaxValue` | gemessen + gelesen |
| Numerische Semantik in der Spec | §1.6 | gelesen |
| Verschachtelungsregel und Typregel für Handler | `cross`, `catchstruct`, `catchslot`; `ValidateHandlers` `:1145-1187` prüft Bereiche, Indizes, Selbstenthalt, finally-ohne-Typ | gemessen + gelesen |
| Pruning von Typtabelle, Impls, Strings | `Reachability.Prune` löscht Funktionen und Importe (`Reachability.cs:22-27, 44-60`). Gemessen: `fn main(): int { return 0; }` → **37 Typen, 39 Strings, 1356 B bei 4 B Code** (`lyrvm info empty_rel`, nachgemessen) | gemessen |
| Slot-/Block-Kompaktion nach den Pässen | §1.3 | gemessen |
| Konstantenfaltung, Blockzusammenlegen | §1.3 | gemessen |
| Modulidentität | §2 kennt Magic, Version, Sektionen; kein Name, kein Hash | gelesen |
| Benannte Fremd-Sektionen | id 0/255 frei, aber jede id höchstens einmal (`:89`, `BytecodeReader.cs:76-80`) | gemessen |
| Assembler / Textformat | `Disassembler.cs` existiert, kein `Assembler` im Baum | gelesen |
| Sektionsgrößen in `lyrvm info` | `STATUS.md:2276-2277` | gelesen |
| Deoptimierung | JIT lehnt vorher ab | gelesen |
| **Bytecode-seitige** Konformanzfälle | `lyric-spec/conformance/`: **178 `.lyr`-Fälle** in 10 Kapiteln, `README.md:30`: „Cases test the LANGUAGE". Handgebaute Module, Reader-Ablehnungen, Exit-Codes fehlerhafter Module: **null Fälle** | gelesen |
| Fuzzing des Readers | grep `fuzz` → nur `TotalityTests` in Parsing/Sema | gelesen |
| Getrennte Übersetzung | `docs/guide/16-building.md:88`: „compiled on its own, whole … There is no link step"; Imports sind Natives (`:236-263`), Aufrufraum = Imports + eigene Funktionen (`:751`) | gelesen |
| Budget als Zusage für kompilierten Code | `Interpreter.cs:105` | gelesen |
| Auslieferungsmodus der Binaries | kein R2R/AOT im Baum | gelesen |

Namenssache mit Regelpotenzial: die Handler-Tabelle kennt `kind 1 = finally` (`:389`), `defer`
lowert genau dorthin (`FunctionLowerer.cs:922-925`), `CONTRIBUTING.md:32` sagt „`defer` only (no
`finally`)". Kein Regelbruch, aber der offene Fehler „werfendes `defer` läuft auf dem RETURN-Pfad
zweimal" (`STATUS.md:2188-2196`, Uhr `LYR-SEM0110`, Spec §12.5) sitzt an dieser Naht.

### 1.8 Korrekturen an bestehenden Notizen

- `PLAN.md:104` führt unter **B** noch `string * n` / `arrrep` → OOM. **Gemessen erledigt**
  (Lauf 1): beide geben `LYR-VM0006` mit exit 101 und Zeile. (`PLAN.md:60-62` sagt zwar „B … sind
  leer", die Tabelle steht aber noch.) Was stattdessen unter B gehört: die drei Prozessabbrüche
  aus §1.2 — derselbe Fehlertyp aus dem Modul statt aus der Quelle.
- Fassung 1 zitierte `STATUS.md` mit Zeilen, die auf HEAD nicht auflösen. Korrekt (HEAD `6f6f029f`):
  Register-Gate **1331**, Kostenmodell ~6 ns **1359-1361**, Reader typisiert nicht **2183**,
  Worker-Isolates **2175**, Sektionsgrößen **2276**, Export-Roots **2399**, JIT opt-in **2505**,
  Faktor zehn **1850**, `for-in`-Indexschleife **2460-2464**, drei Regressionen **106-113**,
  vierter Fund **121-126**, Fusion-ist-Kodierung **1185-1187**, gespleißter Callee in Ketten **845**.

---

## 2. Sprachvergleich

Alle Fremdaussagen: **behauptet** (Sprachwissen). Korrigiert gegenüber Fassung 1: CLR-Zeile
(kein HotSpot-Deopt), CPython (JIT ist opt-in), Backtrace-Vorbilder, BEAM-Identität, Wasm
`name`-Sektion, .NET-PDB.

| Sprache | Maschine | Persistiert | Prüfung beim Laden | Kompilierung | Was Lyric daraus lernt |
|---|---|---|---|---|---|
| **Lua 5.4** | Register (A/B/C) | `luac`-Chunk, nicht portabel | keine; Handbuch warnt vor fremden Chunks, `load` hat `mode` nur zum Ablehnen | Interpreter | Wert trägt Tag → Laufzeitprüfungen überall; Integer-Division durch 0 ist ein Fehler, Float nicht: die Semantik steht im **Handbuch** |
| **LuaJIT** | Register, eigenes Bytecode | ja, ungeprüft | keine | Trace-JIT mit Guards/Side-Exits, Rückfall in den Interpreter | Komplexität einer Größenordnung, die Lyric nicht hat |
| **Wren** | Stack | **keines** | entfällt | Interpreter | die Frage dieses Dossiers existiert dort nicht; der Preis der Gegenentscheidung |
| **CPython** | Stack | `.pyc`, Magic pro Bytecode-Änderung | keine; handgebaute Code-Objekte stürzen ab | adaptiver Interpreter (3.11), Copy-and-Patch-JIT **opt-in** (`--enable-experimental-jit`, ab 3.13 `PYTHON_JIT=0/1`) | Backtrace kollabiert Wiederholungen: „[Previous line repeated N more times]" |
| **JVM** | Stack | `.class`, major/minor strikt | **voller Verifier**; ab Classfile 50 `StackMapTable` → linearer Durchlauf, `top` markiert uninitialisierte Slots; `max_stack`/`max_locals` sind `u2`, Code < 64 KB | HotSpot getiert, **echte Deoptimierung** | Stack-Maps sind teuer; die Grenzen im Format sind billig; `MaxJavaStackTraceDepth=1024` kappt ohne Marker |
| **CLR** | Stack (CIL) | PE + Metadaten | Verifier existierte für Teilvertrauen; .NET Core hat CAS aufgegeben | Tiered Compilation, OSR (ab .NET 7), **keine** HotSpot-artige Deopt; Guarded Devirtualization fällt über eingebauten Pfad zurück | `add` ohne Tag, Typ aus dem Stack geschlossen; `.locals init` nullt Locals; Handler-Regionen müssen **lexikalisch verschachtelt** sein (ECMA-335 II.12.4.2.7) |
| **WebAssembly** | Stack, strukturiert | `.wasm`, Sektionen, **Custom Section id 0 mit Name** | vollständige Validierung in einem Durchlauf mit Soundness-Beweis; Locals sind nullinitialisiert (nicht-defaultable Locals seit function-references: lineares Init-Tracking ohne Fixpunkt); **Traps benannt, keine UB**; **numerische Semantik vollständig definiert** (`trunc_sat`, Shift mod Breite, Division durch 0 = Trap) | Engine-Sache (Tiering) | Implementierungsgrenzen im JS-API-Anhang; Fuel/Epoch sind **wasmtime-Konfiguration**, nicht Format; `name`-Custom-Section trägt Namen, **keinen Hash** |
| **BEAM** | Register | `.beam`, IFF-Chunks, unbekannter Chunk übersprungen; Identität = **Modul-MD5** (`beam_lib:md5/1`, `module_info(md5)`); `Dbgi` ist Debug-Info | Loader prüft und transformiert | BeamAsm beim Laden, **an** seit OTP 24 | Reductions sind Scheduler-Buchhaltung, nicht Format; Hot Code Loading hält alte/neue Version, Migration per `code_change` |
| **Dart** | — | Bytecode 2020 entfernt; Kernel `.dill` + AOT-Snapshots | — | Entwicklung JIT, Auslieferung AOT | Achse ist der *Zweck*, nicht ein Flag |
| **Go** | — | — | — | AOT | Backtrace elidiert nach 100 Rahmen pro Goroutine: „...additional frames elided..." |
| **Rust** | — | — | — | AOT | Backtrace elidiert **nichts**; Stack-Overflow bricht ohne Rahmen ab |

**Die entgegengesetzte Entscheidung, zweimal.**

*Wren* hat kein persistiertes Format. Die ganze Frage dieses Dossiers existiert dort nicht. Lyric hat
sich anders entschieden — `lyric pack`, Auslieferung ohne Quelle, Erato-2-Erweiterungen als
Bytecode hängen daran — und die Entscheidung ist richtig. Aber sie kostet genau das, was §1.2 und
§1.7 auflisten.

*Lua und CPython* erklären Bytecode zur vertrauenswürdigen Eingabe. Lua sagt es im Handbuch;
Lyric sagt in `:224-227` das Gegenteil und hält es nur für Capabilities ein.

**Was zu Lyrics Charakter passt:**

- **JVMs Typvektor, vereinfacht durch die Leer-Invariante** (`:632`): der Eintrittszustand eines
  Blocks ist durch die Slot-Typen vollständig beschrieben — kein Stack-Map-Aufwand, ein linearer
  Durchlauf pro Block. **Aber nur für Typen**: Initialisierung braucht entweder JVMs `top`-Fixpunkt
  oder Wasms Antwort (Nullinit + definierter Trap). Letztere ist die billigere und passt zu
  `:784`, wo Felder schon so definiert sind.
- **Wasms Trap-Disziplin und Wasms numerische Vollständigkeit.** Lyric hat beides zur Hälfte:
  für Arrays (`:800-807`) und im Interpreter-Code — nicht im Dokument.
- **Wasms Custom Section (id 0, benannt)** — die id ist bei Lyric frei.
- **CILs Verschachtelungsregel für Handler.** Die Spec verlangt „innermost first" (`:398-399`),
  woraus Verschachtelung folgt; der Reader prüft es nicht.
- **JVMs `u2`-Grenzen** als Vorbild für Ressourcengrenzen im Format, Wasms „must support at least"
  als Formulierung.
- **BEAMs Modul-MD5** für Identität; **Darts Zweck-Achse** für den JIT; **Gos Rahmen-Elision** und
  **Pythons Wiederholungs-Kollaps** für Backtraces.
- **Nicht**: LuaJITs Trace-JIT, CLRs Verzicht auf den Verifier, CPythons Magic-pro-Version,
  Lua/CPythons Wert-Tag, BEAMs Hot-Code-Migration (Host-Frage).
- **Koroutinen**: Lyrics Rahmenkette (`:934-967`) liegt bei Lua (eigener Stack, yield in
  beliebiger Tiefe, C-Grenze als Wand). Richtige Verwandtschaft; Lyrics Backtrace ist sogar besser,
  weil er Chain und Resumer in **einer** Liste zeigt, wo Luas `traceback` pro Thread endet.

---

## 3. Designfragen

Form je Frage: Heute (belegt) · Optionen mit Vorbild und Preis · Empfehlung · Bricht
(nein/minor/major + 4.x-Warnstufe) · Hängt ab von.

### 3.1 Sandbox und Validierung

#### B1 — Typisiert der Reader den Instruktionsstrom?

**Heute.** Nein, und §6 verlangt es nicht (`:973-994`; `:987` „unchecked array access"). Gemessen:
`confuse`, `nullref`, `oob`, `uninit`, `catchslot` sind `verify ok`; drei töten den Prozess mit
0xE0434352. Reader und Spec stimmen überein; **der Vertrag ist zu schwach**.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: abstrakter Interpreter mit Fixpunkt über den CFG (Typ + Init) | JVM vor Classfile 50 | Ladezeit, 600–900 LOC (behauptet) |
| B: **Typvektor in einem Durchlauf pro Block** (Eintrittszustand = Slot-Typen), Initialisierung **nicht** geprüft, sondern über **B16** entschärft (Nullinit + benannter Trap) | JVM `StackMapTable` ohne Maps; Wasm-Locals | 250–400 LOC (behauptet); **§6 und der Code-Katalog ändern sich** (`LYR-BC0007` „type discipline") — spec-first, also Spec-PR zuerst |
| C: Bytecode als vertrauenswürdige Eingabe erklären, Hash/Signatur statt Prüfung | Lua, CPython | `:224-227` wird zurückgenommen; die Sandbox gilt nur für selbst erzeugte Module |
| D: Laufzeitprüfungen im Interpreter | Lua `TValue` | Wert müsste ein Tag tragen — bricht den Charakter frontal |

**Empfehlung: B + B16.** B allein ist unvollständig (`uninit` findet sie nicht) — das ist die
Korrektur gegenüber Fassung 1. Mit B16 (jeder Slot startet am Nullwert seines Typs, Null-Dereferenz
ist `LYR-VM`-Panik) wird Definite Assignment zur *Optimierungsfrage* (Check elidieren), nicht zur
Sicherheitsfrage — und der Fixpunkt entfällt. C ist unvereinbar mit „Capability-Sandbox als
Host-Grenze": Erato 2 lädt fremde `.lyrbc`. D bricht den Charakter.

**Bricht.** minor — nur fehlerhafte Module; **kein `lyrc`-Modul wird abgelehnt** ist *behauptet*,
nicht gemessen, und muss es werden: Prüfer über die 178 Konformanzfälle × 2 Profile + 80 Beispiele
(`PLAN.md:88-89` hat die Zahlen). Formatbytes unverändert; **Spec §6 ändert sich** (neuer Code,
neue Regel), also Spec-PR mit `since:`-Gate zuerst.
**4.x-Warnstufe:** kein neuer Umgebungsschalter (Fassung 1 hatte `LYRIC_STRICT_BC` vorgeschlagen —
das wäre der dritte neben `LYRIC_JIT`/`LYRIC_PROFILE`, `Profile.cs:40-41`, und genau der
Parallelmechanismus, den B5 abbaut). Stattdessen: `lyrvm verify --strict` in 4.7, in der CI an
wie `LYRIC_VERIFY_IR` (`PLAN.md:78`); in 5.0 ist strict die Vorgabe des Readers.

**Hängt ab von** B16 (Anfangswert), B21 (was der Prüfer über Typen annehmen darf), B26 (Handler-Typen).

---

#### B16 — Was ist der Anfangswert eines Slots, und wer prüft Definite Assignment? *(neu)*

**Heute.** Die Spec schweigt zu Slots; Felder starten am Nullwert, Referenzen als null
(`:783-784`), String-Globals leer (`:423`). Gemessen: `uninit.lyrbc` → `verify ok`, `run` →
`InvalidOperationException` (`LyrValue.cs:129-130`), 0xE0434352. Kontrolle `uninit_ctl` → exit 0.
Und: Null-Dereferenz ist auch **ohne** uninitialisierten Slot erreichbar, weil ein Referenzfeld
legal null trägt (`:784`); `LYR-VM0007` gilt nur für `optget` (`Interpreter.cs:1011`).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Slots starten am Nullwert ihres Typs** (wie Felder), Null-Dereferenz in `ldfld/stfld/callvirt/ldelem…` ist benannte Panik (`LYR-VM0007` erweitern oder `VM0018`) | Wasm (Locals nullinit, Traps), CIL `.locals init`, Lua (nil) | ein Null-Check pro Referenzzugriff — den macht .NET heute schon, nur als Exception; für Lyricpp ein echter Vergleich (~1 Zyklus) |
| B: Definite Assignment über den CFG im Reader, Slots ohne Anfangswert | JVM (`top` in StackMap-Frames, Fixpunkt) | Fixpunkt; Spec muss „uninitialisiert" als Zustand definieren; B1 wird zu Option A |
| C: Lineares Init-Tracking ohne Fixpunkt | Wasm function-references (nicht-defaultable Locals) | funktioniert nur mit **strukturiertem** Kontrollfluss; Lyric hat freie Blocksprünge (B13) |

**Empfehlung: A.** Sie ist die Regel, die für Felder schon gilt, sie macht den Fixpunkt
überflüssig, sie ist die Wasm-Antwort, und sie definiert einen Fehlschlag, der heute ein Absturz
ist. Der Sema-seitige Definite-Assignment-Check bleibt für *Quellcode* (`ScalarReplacement.cs:92-99`
lehnt sich an ihn); das Format braucht ihn nicht.

**Bricht.** nein — `lyrc` beschreibt Slots vor dem Lesen (Sema-Garantie), also ändert sich für
kein ehrliches Modul etwas; Spec-Text (§4 Execution model, §5 Objects) wächst um einen Absatz.
**4.x:** sofort: die Exception in eine Panik verwandeln ist eine Zeile in `LyrValue.AsObject`
oder an den Zugriffsstellen; der Spec-Absatz kann in 4.7 mit.

**Hängt ab von** B1, B2.

---

#### B2 — Was passiert, wenn der Interpreter trotzdem stolpert?

**Heute.** Unbehandelte CLR-Ausnahme, Exit **0xE0434352** (Windows, gemessen) bzw. 134 (Linux,
`STATUS.md:1368-1369`), .NET-Stacktrace auf stderr. §8.2 (`:1084-1094`) kennt keinen Code dafür.
`Lyrvm/Program.cs:171` fängt nur IO-Ausnahmen. Im Embedding erreicht sie den Host.

**Optionen.** (1) jede aus dem Loop entkommende Laufzeit-Ausnahme wird Panik `101` mit
`LYR-VM`-Code „internal runtime fault" — Vorbild Wasm (jeder Fehlschlag ist ein benannter Trap);
(2) eigener Exit-Code `102` — Vorbild JVM `hs_err`; (3) nichts, weil B1/B16 den Fall unerreichbar
machen sollen.

**Empfehlung: (1) und B1/B16.** Ein Fehler im Prüfer selbst bleibt möglich, und eine Sandbox, die
den Wirt töten kann, ist keine. Ein Code lässt sich in `lyrtest` und CI zählen.

**Bricht.** nein (ein Zustand, den es heute als Absturz gibt, bekommt einen Namen). **4.x:**
sofort, kein Warnzyklus. **Hängt ab von** B1; berührt CLI (Exit-Tabelle) und Embedding.

---

#### B17 — Welche Ressourcengrenzen erzwingt der Reader? *(neu)*

**Heute.** Keine außer `count ≤ int.MaxValue` (`ByteReader.cs:68-75`). Gemessen: `maxStack =
100 000 000` in 57 Byte → `verify ok`, `run` allokiert **1,6 GB** (`Interpreter.cs:1579`:
`new LyrValue[Math.Max(Source.MaxStack, 1)]` pro Rahmen, 16 B/Slot) und endet still mit exit 14;
`2 000 000 000` → `verify ok`, `run` > 60 s (Kritik). 4 Mio. Slot-Typen → `verify ok` in 1,2 s.
Die Spec sagt sogar, ein Runtime „may size its frame from it" (`:284-285`). `MaxCallDepth = 1024`
(`Interpreter.cs:139`) und `MaxReentryDepth = 32` (`:177`) sind VM-Konstanten, nicht Formatregeln.
**Ein 58-Byte-Modul kann den Wirt mit Speicher töten** — Sandbox-Frage, und in Lyricpp ohne
.NET-OOM-Schutz schärfer.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Grenzen im Format**: `maxStack`, `slotCount`, `blockCount` ≤ 65 535, `codeLength` ≤ 2²⁴, Sektionslänge ≤ 2³⁰, plus „ein Runtime muss mindestens X annehmen" | JVM `u2` für `max_stack`/`max_locals`; Wasm JS-API-Anhang „Implementation Limitations" | eine Tabelle in §6, ein Code (`LYR-BC0008` „limit exceeded"); kein ehrliches Modul in der Nähe (**behauptet**, messbar über die Suite) |
| B: Grenzen als Runtime-Konfiguration, Spec sagt „implementation-defined" | Lua `LUAI_MAXSTACK` | zwei Runtimes, zwei Antworten; der Host muss sie kennen |
| C: nichts, Speicher ist Sache des Hosts | CPython | die Sandbox-Zusage gilt dann nicht für Speicher |

**Empfehlung: A.** Die Zahlen kosten nichts, und `maxStack` als `u16`-Bereich ist genau das, was
die JVM seit 1995 tut. Dazu die Rahmenzahl: `MaxCallDepth` bleibt Runtime-Sache, aber §8
(Runner-Vertrag) sollte sie als Mindestzusage nennen, sonst ist eine Tiefenpanik auf Lyricpp bei
einer anderen Zahl.

**Bricht.** nein für ehrliche Module (zu messen); Spec §6 wächst — spec-first. **4.x:** Reader kann
die Grenzen in 4.7 als Warnung melden und in 5.0 ablehnen; die Allokation kann sofort gedeckelt
werden (Runtime-Sache).
**Hängt ab von** B1 (gleicher Prüfdurchlauf), B22 (Budget ist die Zeit-, dies die Raumgrenze).

---

#### B18 — Was muss `lyrvm verify` beantworten, damit §8.7 stimmt? *(neu)*

**Heute.** §8.7 (`:1132-1141`): `verify` „answers whether this runtime would accept it — format
validation (§6) and import binding". §Capabilities: „a reader older than a bit rejects the module"
(`:216-219`), „Enforcement happens at load time" (`:221-222`). Gemessen: `cap63` → `verify ok`,
`run` → `LYR-CAP0001` mit leerem Namen. Die Prüfung sitzt in `LoadedProgram.Load`
(`LoadedProgram.cs:76-81`) und `NativeRegistry.Bind` (`:180-190`), nicht im Reader. `verify`
bindet Importe — gegen welchen Grant? Gegen keinen: der Grant ist ein `run`-Parameter.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Unbekanntes Bit ist ein Reader-Fehler** (`LYR-BC`-Code: „capability bit 63 is unknown to this reader"), wie `:216-219` es beschreibt; Grant-Prüfung bleibt im Loader | Wasm (unbekannter Import-Name scheitert bei der Instanziierung, nicht bei der Validierung — die Trennung ist dieselbe) | eine Verschiebung; Meldung nennt das Bit statt `''` |
| B: `verify --grant <list>` beantwortet zusätzlich „würde *dieser* Grant reichen" | — | ein Flag; §8.7 muss den Grant nennen |
| C: nichts; §8.7 umformulieren („format and binding, not capabilities") | — | die Konformanzfrage bleibt halb beantwortet |

**Empfehlung: A + B.** A macht die Spec wahr, die es schon so sagt; B macht `verify` zu dem
Werkzeug, das ein Host vor dem Laden fremder Module braucht. Die Meldung `''` ist unabhängig davon
eine Zeile in `Describe` (`Capabilities.cs:103-113`): unbekannte Bits als Zahl nennen.

**Bricht.** nein. **4.x:** sofort. **Hängt ab von** B10; vom Sandbox-/Embedding-Gebiet (neue Bits:
`abi.md:37, 76` plant Bit 5 `ffiAccess`).

---

#### B26 — Was prüft der Reader an der Handler-Tabelle, und wird er gefuzzt? *(neu)*

**Heute.** `ValidateHandlers` (`BytecodeReader.cs:1145-1187`) prüft Funktions-, Block-, Typ- und
Slot-Index, `start < end`, Handler außerhalb der eigenen Region, finally ohne Typ/Slot — genau die
Liste in `:404-406`. Gemessen `verify ok`: **kreuzende** Regionen `[0,2)`/`[1,3)` (`cross`),
`catchType` = Struct (`catchstruct`), `catchType` `&C` mit `i64`-Bindeslot (`catchslot`). Die Spec
sagt „innermost region first" (`:398-399`), was Verschachtelung voraussetzt, ohne sie zu fordern.
Fuzz-Tests über den Reader: keine (grep; nur `TotalityTests` in Parsing/Sema).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Verschachtelungsregel**: zwei Regionen einer Funktion sind disjunkt oder ineinander; Reihenfolge innen-vor-außen wird geprüft, nicht nur verlangt | CIL (ECMA-335: lexikalisch verschachtelt), Wasm EH (strukturiert per Konstruktion); **nicht** JVM (beliebige Überlappung erlaubt) | O(h²) pro Funktion, h klein |
| B: **Typregel**: `catchType` muss ein Klassen-Layout sein (die Sprache wirft keine Structs), Bindeslot-Typ = `catchType` (braucht B1) | — | trivial, sobald B1 da ist |
| C: Fuzz-Harness über `BytecodeReader` als nächtlicher Lauf, nicht als Gate | SharpFuzz (.NET), Wasm-Engines (alle fuzzen ihre Validatoren), Go `testing.F` | Infrastruktur; findet Abstürze, nicht Semantikfehler |
| D: nichts | — | `cross` ist heute gültig und undefiniert |

**Empfehlung: A + B in 5.0, C ab 4.7.** `lyrc` erzeugt per Konstruktion verschachtelte Regionen
(`FunctionLowerer.cs:922-925`, **behauptet**, zu messen mit dem strikten Prüfer). Der Reader ist
die Angriffsfläche der Sandbox; er wird nicht gefuzzt.

**Bricht.** nein für ehrliche Module; Spec `:404-406` wächst um zwei Punkte. **4.x:** Warnung in
`verify --strict`. **Hängt ab von** B1 (Slot-Typ), dem Fehler-Gebiet (§7.5, `defer`-Uhr).

---

### 3.2 Format

#### B8 — Wie wächst das Format nach Lyric 5?

**Heute.** ids müssen streng aufsteigen und höchstens einmal vorkommen (`:89`, `:111-112`;
`BytecodeReader.cs:76-80`). **Korrektur zu Fassung 1:** id 0 und id 255 sind heute frei und
ladbar (`sec0`, `sec255` → `verify ok`; `sec00` → BC0005 wegen Wiederholung, nicht wegen Position).
Es gibt keine Instanz, die ids vergibt: Toolchain und Erato könnten beide 15 nehmen.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Benannte Custom-Sektion mit id 0**, Payload beginnt mit einem Namen; mehrfach erlaubt | Wasm Custom Section (id 0, benannt, mehrfach), BEAM IFF-Chunks | die Regel „höchstens einmal" bekommt für id 0 eine Ausnahme; die Aufsteigend-Regel bleibt (alle id-0-Sektionen stehen am Anfang) |
| B: Nummern weiter vergeben, Registrierungsdatei im Spec-Repo | — | nur die Toolchain kann eintragen |
| C: alles in Attribute (Sektion 11) | — | Attribute sind Strukturwerte, keine Byteblöcke |

**Empfehlung: A.** Erato 2 mit eigenem Runtime und Erweiterungen als Bytecode ist der Fall. Dass
die id frei ist, macht es zu einer Minor-Änderung ohne Umbau.

**Bricht.** nein — additiv; ein 4.0-Leser überspringt id 0 (gemessen). **4.x:** 4.7. **Hängt ab
von** Embedding/FFI (wer schreibt hinein), Paketgebiet.

---

#### B9 — Hat ein Modul eine Identität?

**Heute.** Nein: Magic, Version, Sektionen (`:94-101`). `lyrvm info` zeigt Dateiname, Format,
Zähler, `entry` — nichts zur Herkunft (gemessen).

**Optionen.** (A) Sektion mit Modulname, Toolchain-Version, **Inhalts-Hash** der übrigen Sektionen
— Vorbild **BEAMs Modul-MD5** (`beam_lib:md5/1`, `module_info(md5)`), .NET MVID (Fassung 1 nannte
`Dbgi` und Wasms `name`-Sektion; beides falsch: `Dbgi` ist Debug-Info, `name` trägt Namen, keinen
Hash; das Wasm-Kernformat hat keine Identität); (B) abgesetzte Signatur, vom Host geprüft —
JAR-Signing, Sigstore; (C) nichts, Identität ist Sache des Pakets (`docs/Pack.md`).

**Empfehlung: A jetzt, B als Host-Frage offenlassen.** Ein Hash beantwortet `info`, die Cache-
und die Reload-Frage (B28) für 32 Byte. Eine Signatur ist eine Vertrauenskette, die niemand
verteilt.

**Bricht.** nein (überspringbare Sektion, oder B8-Custom-Sektion `lyric.identity`). **Hängt ab
von** Paket/`lyric.json` v2 (Name und Toolchain stehen dort; das Modul trägt die Kopie).

---

#### B10 — Capabilities: Bitset oder benannte Menge?

**Heute.** `uleb128`-Bitset, 5 Bits (`:204-227`). Gemessen: Bit 63 wird beim **Laden** abgelehnt,
Meldung `module requires capability ''`; `verify` sieht es nicht (B18).

**Optionen.** (A) Bitset behalten, Namensliste daneben, damit ein unbekanntes Bit benannt werden
kann; (B) Liste von Strings — Android-Permissions, Wasm-Import-Namen; Preis: Union-Regel im
Embedding wird Mengenarbeit statt `|`; (C) Bitset lassen, Meldung reparieren (`capability bit 63
(unknown to this runtime)`).

**Empfehlung: C jetzt, A für v5, und die Bit-Vergabe im Spec-Repo führen** (`abi.md:76` will Bit 5).

**Bricht.** nein. **4.x:** sofort (C). **Hängt ab von** B18, Sandbox-Gebiet.

---

#### B12 — Trägt jede Instruktion ihr Typ-Tag?

**Heute.** Ja (`:647-649`; `LyrValue.cs:5-9`). Kostenmodell ~6 ns pro Instruktion, egal welche
(`STATUS.md:1359-1361`).

**Optionen.** (A) lassen; (B) spezialisierte Opcodes (`addi64`) — JVM, Lua; spart ein Byte,
verdoppelt Tabellen in Reader, Disassembler, Verifier, JIT; (C) kein Tag, Typ aus dem Stack
geschlossen — CIL; macht den Interpreter von einer Inferenz abhängig, die B1 gerade erst baut.

**Empfehlung: A.** Das Tag ist, was Interpreter ohne Wertetag und JIT ohne Inferenz möglich macht
(`JitCompiler.cs:23-29`: „every slot carries its type … a typed IL local").

**Bricht.** nein für A; major für B/C. **Hängt ab von** B1, B4.

---

#### B13 — Freie Blocksprünge oder strukturierter Kontrollfluss?

**Heute.** Frei (`:638-641`, `:753-754`). Kein Phi: blockübergreifende Werte reisen durch Slots.

**Optionen.** (A) lassen; (B) Wasm-artig (`block/loop/if`, `br` auf Label-Tiefe) — Validierung
ohne CFG, lineares Init-Tracking (B16-C) wird möglich, irreduzible Schleifen unausdrückbar; Preis:
Relooper im Emitter, alle Pässe strukturerhaltend, der Inliner spleißt heute Blöcke ein.

**Empfehlung: A.** Die Leer-Invariante liefert die Validierung billiger; B16-A liefert die
Initialisierung ohne Struktur. Der Preis fiele im gerade gebauten Optimierer an.

**Bricht.** major, falls je. **Hängt ab von** B1, B4, B16.

---

#### B21 — Definiert die Spec die numerische Semantik, die ein zweites Runtime bitgleich liefern muss? *(neu)*

**Heute.** Nein (§1.6). Der Interpreter hat eine vollständige Antwort (`Interpreter.cs:1309-1346`,
`:1453-1485`), das Dokument keine; `:34` verweist auf Regeln, die nicht existieren. `:9-10`
verspricht Nachbaubarkeit aus dem Dokument. C# und C++ antworten auf `NaN as int` und `MIN / -1`
verschieden (C++: UB).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **§5a „Numeric semantics"**, normativ: Wrap auf Breite; Division/Rem durch 0 = `LYR-VM0002`; `MIN/−1` = `MIN`, `MIN%−1` = 0; Shift-Zähler mod Breite, `shr` arithmetisch für signed; `conv` float→int = `trunc_sat`, NaN → 0; `f32` in einfacher Genauigkeit; IEEE-754 für alles Übrige inkl. −0.0 und NaN ≠ NaN; Vergleiche von Strings by value | Wasm-Spec (vollständig), JLS §15.17 | ein Kapitel Text, null Code — die Werte stehen im Interpreter |
| B: „implementation-defined" mit Mindestliste | Lua-Handbuch, C | zwei Runtimes, zwei Antworten; Konformanzfälle (`intsem`, `num`) unmöglich |

**Empfehlung: A — vor B20**, denn eine Konstantenfaltung, die anders rechnet als die VM, ist
eine falsche Antwort, und die einzige Quelle der Wahrheit ist heute ein `switch` in C#.

**Bricht.** nein (beschreibt, was gilt). **4.x:** Spec-PR sofort; `intsem.lyr`/`num.lyr` werden
Konformanzfälle mit `since: 4.7`. **Hängt ab von** nichts. Vorbedingung für B20, B11/B15d.

---

#### B25 — Gibt es getrennte Übersetzung? *(neu)*

**Heute.** Nein. „Every artifact is compiled on its own, whole, from its entry file. There is no
link step" (`docs/guide/16-building.md:88`; `docs/Pack.md:23` „no linker"). Imports sind Host-
Natives (`:236-263`), der Aufrufraum ist Imports + eigene Funktionen (`:751`). Jedes Modul trägt
seine eigene Kopie der std-Typen (gemessen: 37 in `empty`, `fold`, `hot`). Zwei Module, die Erato
nebeneinander lädt, teilen **kein** Layout — Werte kreuzen nur als Skalare, Strings und
Host-Objekte (`0x47`, `:609`; `abi.md:14`: `FromLyric` liefert für Objekt/Array/Optional 0).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Whole-Program bleibt**, und die Spec sagt es: „a module references nothing outside itself except imports"; Duplikate über B3 (Pruning) klein halten | Wasm (ein Modul, Imports = Host), Lua-Chunks | Bibliotheken werden als Quelle verteilt (M37 tut das); Layout-Identität zwischen Modulen ist kein Thema, weil es keine gemeinsamen Werte gibt |
| B: Modul-Import zweiter Art (Funktion aus `.lyrbc`), Link beim Laden, Layout-Gleichheit strukturell | Wasm-GC (strukturelle Typen), JVM (nominal über Classloader), BEAM (Module unabhängig, Werte sind Terme) | ein Linker; eine zweite Identitätsregel; Versionierung von Layouts; ein neuer Import-Kind = Minor |
| C: Bytecode-Pakete mit Header, Quelle nicht nötig (Designrunde 2026-09 nennt „kompiliertes Lyric mit Header") | .NET-Assemblies | wie B plus Metadaten für Sema (Signaturen, Generics) — das ist ein Interface-Format neben dem Bytecode |

**Empfehlung: A für 5.0, ausdrücklich in der Spec.** B/C erst, wenn ein gemessener Fall (N Mods
teilen eine Bibliothek und die Duplikation tut weh) da ist — und dann als eigenes Gebiet. Die
Formulierung schuldet das Dokument dem zweiten Runtime jetzt schon.

**Bricht.** nein. **Hängt ab von** B3, Paketgebiet (M37, Stufen B–F).

---

### 3.3 Interpreter und Optimierer

#### B3 — Wird die Typtabelle gepruned?

**Heute.** Nein: `Reachability.Prune` löscht Funktionen und Importe (`Reachability.cs:22-27,
44-60`); Typen, Impls, Strings, Names, OpaqueFields bleiben. Gemessen (nachgemessen 2026-09-28):
`fn main(): int { return 0; }` → **37 Typen, 39 Strings, 1356 B bei 4 B Code**; `hot` trägt
`DedupIterator<string>` und 51 weitere Namen.

**Optionen.** (A) Erreichbarkeit auf Typen ausdehnen (Wurzeln: Slot-/Feld-/Signaturtypen der
behaltenen Funktionen, `mkiface`-Ziele, `catchType`, `throw`-Typen, Attributziele — Falle `:467-469`:
eine attributierte Funktion ist ein unsichtbarer Aufrufer) und Impls/Strings/Names mitziehen —
ProGuard/R8, .NET IL-Trimming, `wasm-opt`; (B) nur String-Pool; (C) lassen.

**Empfehlung: A.** 337× Metadaten gegen Code; Erato lädt viele kleine Module; die Analyse läuft
schon, nur mit zu wenig Wurzeln.

**Bricht.** nein („FORMAT-NEUTRAL", `Reachability.cs:16-17`). **4.x:** Patch; Golden-Test auf
Modulgröße. **Hängt ab von** Paketgebiet (`pub`-Wurzeln, `STATUS.md:2399-2404`), B24.

---

#### B4 — Register, Stack, oder Stack mit Slot-Fusion?

**Heute.** Stack; Entscheid `STATUS.md:1331-1357` (Zählschleife 3 = Parität, maskierter
Akkumulator 9 gegen ~4, Array-Lesen 13 gegen ~5). Gemessen bestätigt: `(acc + i) & 1023` bleibt
sechs Instruktionen; **und `match` bekommt gar keine Fusion** (6 pro Arm, §1.3) — ein Fall, den
`STATUS.md:1343-1346` nicht kannte.

**Optionen.** (A) Fusions-Auswahl erweitern: Zwischenwerte in Slots, Ketten fusionierter Formen —
**genau `STATUS.md:1348-1353`**, eine Regel in `Emit/Fusion.cs`, kein neues Byte; plus B19 für
`match`; (B) Drei-Adress-Format in 5.0 — Lua/BEAM/Dalvik; jeder Leser, Disassembler, Verifier,
Inliner, JIT lernt eine zweite Kodierung; Format-Major; (C) Superinstruktionen — CPython 3.11.

**Empfehlung: A + B19, messen, dann erst wieder fragen.** Die Messung, die B rechtfertigt, gibt es
nicht, und A ist die Änderung, nach der man sie machen kann. Neu: der `match`-Fall ist der, an dem
zu messen ist, nicht der Akkumulator.

**Bricht.** nein für A (`:766-767`); major für B. **Hängt ab von** B12, B19.

---

#### B19 — Braucht das Format eine Tabellenverzweigung, oder muss das `match`-Lowering fusionsfähig werden? *(neu)*

**Heute.** `match` hat keinen Opcode (`:837-839`): `enumtag`, dann Vergleiche. Gemessen
(`enummatch_noinl`, `main.score`, 3 Arme): pro Arm `const i64 k; stloc n; ldloc tag; ldloc n;
eq i64; condbr` — **6 Instruktionen**, linear, die Konstante geht durch einen Slot; die
Fusionsregel „Temps auf dem Stack" (`Fusion.cs:64-66`) greift nicht, kein `brcmpk eq`. Im Release
wird `score` eingespleißt, die Form bleibt. Der JIT lehnt Enums ab (`JitCompiler.cs:35-36`). Ein
n-armiges Match kostet heute ~6n Dispatches (~6 ns je, `STATUS.md:1359`).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **Lowering reparieren**: Tag in einen Slot, Konstante auf dem Stack lassen → `brcmpk eq i64 tag, k -> arm, next` = **1 Instruktion pro Arm**, mit den 3.6-Opcodes | — (Lyrics eigene Fusion) | eine Änderung im Pattern-Lowering, kein Formatbyte; von 6n auf n |
| B: `brtable`/`switch`-Opcode: `uleb count`, `count × block`, `default` | JVM `tableswitch`, Wasm `br_table`, CIL `switch`, Lua (hat keinen) | ein Opcode (Minor, per-Modul-Regel), Reader/Disasm/JIT lernen ihn; O(1) statt O(n); lohnt ab n ≈ 8 (behauptet) |
| C: beides, A zuerst | — | — |

**Empfehlung: C — A sofort, B nach Messung.** A ist eine Nachmittagsänderung mit Faktor 6 auf dem
Pfad, den M36 zum Zentrum der Sprache gemacht hat. B ist die erste Format-Erweiterung, die eine
Messung rechtfertigen könnte — dieselbe Regel wie B4. Dazu gehört B15a: der JIT muss Enums
annehmen, sonst läuft `match` auf **beiden** langsamen Pfaden.

**Bricht.** nein für A; minor für B (neuer Opcode, ältere Runtimes lehnen korrekt ab, `:116-121`).
**4.x:** A in 4.7. **Hängt ab von** B4, B15a, Enum-/Pattern-Gebiet (Lowering-Form).

---

#### B20 — Wo liegt Konstantenfaltung, wo Dead-Code-Elimination? *(neu)*

**Heute.** Nirgends. Gemessen: `return 2 + 3;` → `const 2; const 3; add; retval` auch im Release.
`IrPasses.cs` kennt Inline, ScalarReplacement, Devirtualize. Kein Blockzusammenlegen (`bb4: br
bb6`). Der JIT (RyuJIT) faltet, was er kompiliert — der Interpreter zahlt 3 Dispatches für eine
Konstante.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **IR-Pass „Fold + SimplifyCFG"**: Konstanten falten **mit exakt der VM-Semantik** (Wrap, kein Falten von `/0`, `trunc_sat`), leere/br-only-Blöcke zusammenlegen, unerreichbare Blöcke löschen | LLVM `instcombine`/`simplifycfg`, jeder Compiler; CPython faltet im Compiler | ein Pass; **B21 vorher**, sonst zwei Wahrheiten; ~150 LOC (behauptet) |
| B: Faltung in der Sema (`comptime`/`const`-Pfad existiert für Konstanten, `design/macros.md:75`) | Zig `comptime` | vermischt Typprüfung und Optimierung; der Pfad ist für Deklarationen gedacht |
| C: lassen, auf den JIT verweisen | — | der metered/debugged Pfad (B22) ist interpretiert und zahlt |

**Empfehlung: A, nach B21.** Und die Regel dazu ausdrücklich: **gefaltet wird nur, was die VM
genauso rechnet** — `1 / 0` bleibt stehen und panikt zur Laufzeit; `MIN / -1` wird zu `MIN`.

**Bricht.** nein (formatneutral). **4.x:** Patch. **Hängt ab von** B21, B14.

---

#### B24 — Kompaktiert der Emitter Slot-Tabelle und Blockreihenfolge? *(neu)*

**Heute.** Nein. Gemessen `hot_rel` `main.main`: Slots `l2, l3, l4 (string)` ohne Referenz, `bb5`
zwischen `bb4` und `bb6` außerhalb der Flussreihenfolge. Folgen: DebugInfo-Namen für tote Slots,
JIT-Lokalzahl, Rahmen-Allokation (`Interpreter.cs:1578`), Modulgröße (B3).

**Optionen.** (A) Emitter-Pass: unreferenzierte Slots entfernen, Rest renumerieren (DebugInfo
folgt), Blöcke in RPO — jeder Backend; (B) im IR nach den Pässen; (C) lassen.

**Empfehlung: A im Emitter**, aus demselben Grund wie die Fusion (`STATUS.md:1185-1187`:
Kodierungsfragen an einem Ort).

**Bricht.** nein. **4.x:** Patch. **Hängt ab von** B14c, B3.

---

#### B14 — Wo liegen die Optimierungen, und was fehlt?

**Heute.** Drei IR-Pässe, Fusion im Emitter, Verifier-Reihenfolge seit PR #165 repariert
(`PLAN.md:64-91`). Offen: drei Regressionen (`STATUS.md:106-113`) und die Funde §1.3.

| | Frage | Vorbild | Empfehlung |
|---|---|---|---|
| a | `ForwardLocals` — **nur blockintern oder nur Struct-Werte forwarden** (korrigiert: kein Dominanztest; die Korrektheit steht auf Sema-DA, `ScalarReplacement.cs:92-99`; der Fund ist Performance, `STATUS.md:110-111`) | — | ja, billig |
| b | Blockzusammenlegen + Sprung-Threading | → B20 | ja |
| c | Blockinterne Kopien: Peephole im Emitter | BEAM-Loader, CPython-Compiler | im Emitter (Rule 2, `STATUS.md:1185-1187`) |
| d | `for (x in array)` als Indexschleife | `STATUS.md:2460-2464` | ja, eine Slice |
| e | Ablehnungsliste des JIT abrufbar (`lyrvm run --jit --jit-report`) | Daten liegen (`JitContext.cs:71-82`) | ja |
| f | `match`-Lowering | → B19 | ja, zuerst |

**Bricht.** nichts. **4.x:** alles Patch-fähig. **Hängt ab von** B19, B20, B24.

---

### 3.4 JIT und Auslieferung

#### B5 — Bleibt der JIT opt-in?

**Heute.** Ja: `--jit`, `LYRIC_JIT`, `HostOptions.Compile` (`STATUS.md:2505-2508`). Gemessen:
Größenordnung (§1.4). **Drei Ausschlüsse**, die Fassung 1 fehlten: Budget ⇒ interpretiert,
Debugger ⇒ interpretiert (`Interpreter.cs:17-26`, `:248-249`), NativeAOT ⇒ still ignoriert
(`HostOptions.cs:65-67`).

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: opt-in lassen | **CPython** (`--enable-experimental-jit`, `PYTHON_JIT`) — Korrektur: Fassung 1 sagte „niemand" | Status quo; zwei Mechanismen für „wie schnell" |
| B: **an das Profil binden**: `release` ⇒ „darf kompilieren", die Policy entscheidet (Budget/Debugger bleiben interpretiert) | Dart (Zweck-Achse), Lyrics eigene Profilachse | Release-Backtrace dünner → B6 zuerst; **für den Budget-Host ändert sich nichts** (B22) |
| C: getiert nach Aufrufzahl, immer an | HotSpot, CLR, BeamAsm | Start, Speicher, Differenztestlast |
| D: AOT: IL offline, neben das `.lyrbc` | ReadyToRun, Dart-Snapshots | zweite Pipeline; und die Hosts, für die es zählt (Budget), profitieren nicht |

**Empfehlung: B, ehrlich beschriftet.** „`release` schaltet den JIT ein" ist falsch formuliert;
richtig ist „`release` erlaubt Kompilierung, wo niemand zuschaut". Das ist die Regel, die
`Interpreter.cs:17-26` schon hat, nur mit einem Schalter weniger. Dazu Pflicht: ein Host, dessen
`Compile` ignoriert wird (NativeAOT, Budget), bekommt eine Diagnose oder einen `Refusals`-Eintrag
„metered"/„no IL emitter", sonst ist es ein Profilfeld, das lügt.

**Bricht.** minor — beobachtbar in Backtrace-Tiefe und Koroutinen-Wand (`guide/11:146`).
**4.x-Warnstufe:** `--jit` bleibt; `release` schaltet zusätzlich; einmalige Meldung beim ersten
Release-Lauf; `--no-jit` als Profilfeld. **Hängt ab von** B6, B22, B23, Werkzeug-Gebiet.

---

#### B22 — Wie zählt kompilierter Code ein Ausführungsbudget? *(neu)*

**Heute.** Gar nicht, per Vertrag: „the budget counts instructions, and compiled code executes
none" (`Interpreter.cs:105`); „a budget is a safety promise, and a promise that only sometimes
holds is not one" (`:24-26`). Budget = gezählt, nicht getaktet, für Replays (`ExecutionBudget.cs:13-17`);
Hosts kalibrieren aus `Consumed` (`:19-20`). Das Budget reicht in resumte Ketten
(`STATUS.md:847-848`). **Folge: „sandboxed ⇒ interpretiert" — B5 ist für Erato mit Budget leer.**

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: Regel behalten, sichtbar machen (`Refusals`: „metered") | — | der Host, der Speed am nötigsten hat, bekommt sie nicht |
| B: **JIT emittiert Zähler an Rückkanten und Aufrufen**, Budget-Einheit wird „Block-Eintritte" statt Instruktionen — deterministisch bleibt es | wasmtime Fuel (Zähler im Code), BeamAsm-Reductions (Zähler in kompiliertem Code) | Einheit ändert sich → Hosts, die `Consumed` kalibriert haben, brauchen neue Zahlen (**minor**); ~1 ns pro Rückkante (behauptet); Debugger bleibt draußen |
| C: Budget zeitbasiert für kompilierten Code | Lua `debug.sethook` count | verwirft „counted, not timed" — die Replay-Zusage |

**Empfehlung: A in 4.x, B mit ADR für 5.0.** B ist die Bedingung, unter der B5 für den
Sandbox-Host überhaupt etwas bedeutet; er verändert eine dokumentierte Zusage (Instruktionen), also
ADR und `since:`-Gate. C ist ausgeschlossen.

**Bricht.** A nein; B minor (Einheit). **4.x:** A sofort. **Hängt ab von** B5, B15a, Nebenläufigkeitsgebiet
(Ketten).

---

#### B23 — Welcher Auslieferungsmodus gilt für `lyrvm`, `lyrc` und den Pack-Stub? *(neu)*

**Heute.** CLR-JIT für alles; kein csproj setzt `PublishReadyToRun` oder `PublishAot` (grep leer,
nur `InvariantGlobalization`, `Directory.Build.props:18`). Gemessen 2026-09-28: leeres Programm
**140–146 ms**, `--version` 110 ms, `read` eines 1,4-KB-Moduls 16,9 ms, `hot --jit` 155 ms
gesamt. Die Startkosten sind **CLR-Start + JIT des Readers/Interpreters**, nicht das Modul.
`STATUS.md:1846-1850` kennt den Effekt („off by a factor of ten"). Fassung 1 hatte daraus B15f
(Snapshot-Format) gemacht — falsche Kostenstelle.

**Optionen.**

| | Vorbild | Preis |
|---|---|---|
| A: **ReadyToRun** für `lyrvm`/`lyrc`/`lyric` | .NET-Standard | Größere Binaries; der IL-JIT bleibt verfügbar; Start sinkt (behauptet: −50–100 ms) |
| B: NativeAOT für die Werkzeuge | — | **kein IL-Emitter → Lyrics JIT ist weg** (`HostOptions.cs:65-67`); B5 wird leer |
| C: NativeAOT nur für den Pack-Stub (`lyrstub`, `Pack.md`) | Dart AOT | ausgelieferte Programme laufen interpretiert — im Widerspruch zu „ship compiled" (`HostOptions.cs:55-56`) |
| D: nichts | — | 140 ms pro Aufruf für ein Werkzeug, das oft startet (`lyrtest`, Editor) |

**Empfehlung: A für die Werkzeuge; für den Stub erst entscheiden, wenn B5 entschieden ist** —
Stub-AOT und Release-JIT schließen sich aus, und das gehört als Konflikt aufgeschrieben.

**Bricht.** nein (Build-Konfiguration). **4.x:** A jederzeit. **Hängt ab von** B5, Paket-/CLI-Gebiet.

---

#### B15 — Fragen, die niemand stellt, weil es das Feature nicht gibt

| | Frage | Wer hat es (korrigiert) | Was es Lyric bringt |
|---|---|---|---|
| a | **Deoptimierung**: darf der JIT spekulieren und zurückfallen? | HotSpot, V8, LuaJIT (Side-Exits); **nicht** die CLR (OSR ist die Gegenrichtung; Guarded Devirt hat einen eingebauten Fallback) | ohne sie muss der JIT vorher ablehnen — heute Enums = jedes `match`. Mindestziel ohne Deopt: Enums annehmen (B19) |
| b | **Instrumentierung im Format** (Zähler pro Block) | JaCoCo (Bytecode-Rewriting), CPython `sys.monitoring` (3.12), Go `-cover` (Compiler); **nicht** Wasm (`--instrument` existiert nicht; binaryen hat `--instrument-locals/-memory` fürs Debugging) | `lyrtest` Coverage; heute kein Ort, kein Rewriting-Werkzeug |
| c | **Ausführungsbudget als Format-Zusage** | **niemand** — Fuel/Epoch sind wasmtime-Engine-Konfiguration, Reductions sind Scheduler; kein Format trägt ein Budget | umformulieren: nicht Sektion, sondern **§8 Runner-Vertrag**: „a conformant runtime offers an instruction budget" — Konformanzpflicht statt Formatbit |
| d | **Bytecode-seitige Konformanzfälle** | Wasm spec tests (`.wast` mit `assert_invalid`/`assert_trap`), JVM TCK; **nicht** CLR (kein öffentliches TCK) | die 178 Sprachfälle existieren (`lyric-spec/conformance/`); was fehlt sind `assert_invalid`-Fälle für den Reader und Exit-Codes fehlerhafter Module — die gibt es nur mit B11 |
| e | **`resume` durch fremde Rahmen** | Lua `lua_yieldk` (Continuation) | die Wand (`guide/11:146`); Sprachfrage, nicht Formatfrage |
| f | ~~Warm-Start/Snapshot~~ → **B23** | — | gestrichen: die Kostenstelle ist CLR-Start, nicht das Modul |

---

### 3.5 Diagnose

#### B6 — Was schuldet ein Backtrace unter Optimierung?

**Heute, gemessen** (§1.5): Release paart Aufrufername mit Callee-Zeile, flach und durch Ketten
hindurch; `--jit` lässt den kompilierten Rahmen aus; `--debug`/`--no-inline` korrekt.
`STATUS.md:121-126` kennt den Rahmenverlust; die **Fehlzuordnung** steht nirgends.

**Optionen.** (A) Inline-Kette ins Format: überspringbare Sektion, SourceMap-Zeile verweist auf
eine Kette — Vorbild **DWARF `DW_TAG_inlined_subroutine`** (Fassung 1 nannte auch .NET-PDB-
Sequenzpunkte — falsch: die bilden IL-Offsets auf Zeilen ab, Inlining-Info liegt in JIT-
Laufzeitdaten); (B) Rahmen beschriften `main.main [inlined main.inner]` mit beiden Zeilen — braucht
dieselben Daten wie A; (C) im Release nicht inlinen — verschenkt §1.3; (D) Deoptimieren — zu teuer.

**Empfehlung: A, plus Schattenrahmen-Index für den JIT** (Name + Zeile der obersten Funktion).
Einzige Option, die den Zustand behebt statt beschriftet.

**Bricht.** nein (überspringbare Sektion). **4.x:** Pin, der beide Maschinen und die Ketten prüft.
**Hängt ab von** B5, B27, DAP-Gebiet.

---

#### B27 — Was schuldet ein Backtrace unter Koroutinen? *(neu)*

**Heute, gemessen** (`bt_coro`): Debug 4 korrekte Rahmen, Chain-Rahmen im Resumer eingespleißt
(logischer = physischer Stack, `STATUS.md:841-846`); Release wiederholt die Inliner-Fehlzuordnung;
`--jit` lässt nur den kompilierten Rahmen aus, die Kette bleibt sichtbar.

**Optionen.** (A) Splice behalten, B6 beheben — dann ist es hier mit behoben; (B) Kettengrenze
markieren (`in main.gen.<body> (:8) [resumed by main.drive]`) — Vorbild: Python-Generatoren
(gewöhnliche Rahmen), Lua (`traceback` pro Thread endet an der Grenze — schlechter); (C) pro Chain
eigener Backtrace — Lua; verliert den Resumer.

**Empfehlung: A, B optional.** Die gemessene Form ist besser als Luas; der Name `<body>` markiert
die Grenze schon.

**Bricht.** nein. **Hängt ab von** B6, Nebenläufigkeitsgebiet.

---

#### B7 — Wie kürzt ein Backtrace?

**Heute.** Gar nicht: 1026 Zeilen bei `MaxCallDepth = 1024` (gemessen).

**Optionen** (Vorbilder korrigiert): (A) Kopf + Fuß, Mitte elidiert mit Zähler — **Go**
(„...additional frames elided..." nach 100 pro Goroutine); (B) Wiederholungen kollabieren —
**Python** („[Previous line repeated N more times]"; Fassung 1 schrieb Rust — Rust elidiert
nichts, ein Stack-Overflow bricht ohne Rahmen ab); (C) harte Kappung ohne Marker — Java
(`MaxJavaStackTraceDepth=1024`); (D) konfigurierbar.

**Empfehlung: A + B.** Bei einer Tiefenpanik ist die Wiederholung die Information.

**Bricht.** nein (stderr). **4.x:** sofort. **Hängt ab von** Diagnostik-Gebiet.

---

### 3.6 Werkzeuge und Konformanz

#### B11 — Gibt es ein Textformat, das zurückliest?

**Heute.** Nein: `Disassembler.cs` schreibt, nichts liest. Diese Proben sind Python.

**Korrektur zu Fassung 1:** Die Konformanz-Suite **existiert** — 178 `.lyr`-Fälle
(`lyric-spec/conformance/cases/`, 10 Kapitel), Runner < 150 Zeilen, Exit-/Panic-/stdout-exakt.
Vom Referenz-`lyrc` kompiliert sind das die Module, gegen die Lyricpp laufen muss. Was fehlt, sind
**Bytecode-seitige** Fälle: handgebaute Module, Reader-Ablehnungen, Exit-Codes fehlerhafter
Module — `README.md:30`: „Cases test the LANGUAGE".

**Optionen.** (A) `lyrvm asm <file.lyrasm>` als Umkehrung des Disassemblers, round-trip-getestet
— Wasm `.wat`/`assert_invalid`, Jasmin, `ilasm`; (B) nichts, Bytecode-Tests bleiben C#/Python;
(C) Parser nur in der Testbibliothek.

**Empfehlung: A, sobald Lyricpp konkret ist; sonst C — und in beiden Fällen ein
`conformance/bytecode/`-Ordner mit `.lyrbc`-Fällen (`//! verify: LYR-BC0006` als Kopfform),
weil die Sprachfälle die Reader-Frage nicht stellen.** `docs/Bytecode.md:9-10` ist ungeprüft,
solange kein Fall den Reader testet.

**Bricht.** nein. **Hängt ab von** Lyricpp/Erato 2, B21 (numerische Fälle).

---

#### B28 — Welche Identität hat ein Modul über `ScriptInstance.Reload` hinweg? *(neu)*

**Heute (gelesen).** `Reload()` = `_vm.Instantiate(_vm.CompileFile(path))`
(`ScriptInstance.cs:59-66`): neue `LoadedProgram`, neuer `JitContext` (einer pro Programm,
`LoadedProgram.cs:100-104`), alte Instanz bleibt gültig (`:47-48`), Modulkonstanten neu, Host-
Objekte überleben (`:49-50`). Alte `DynamicMethod`s hängen am alten Kontext und werden mit ihm
eingesammelt (behauptet: `DynamicMethod` ist collectible). Skript-Objekte alter Layouts kann der
Host nicht halten — `FromLyric` liefert für Objekt/Array/Optional 0 (`abi.md:14`); nur Host-Objekte
(`0x47`) kreuzen. **Nicht gemessen**: bräuchte einen Embedding-Host; Lesen reicht für die Frage.

**Optionen.** (A) „Reload = neue Welt", dokumentiert; Identität über B9-Hash, damit ein Host
sieht, ob sich etwas geändert hat — Vorbild Lua `dofile`; (B) Zustandsmigration
(`pub fn migrate(old): new`) — Vorbild Erlang `code_change`, V8 Live Edit; enorm, und Host-Sache
(Erato-Editor); (C) zwei Versionen gleichzeitig (BEAM old/current).

**Empfehlung: A + B9.** Migration ist eine Host-Frage über Host-Objekte; das Format schuldet
nur die Antwort „ist das dasselbe Modul".

**Bricht.** nein. **Hängt ab von** B9, Embedding-Gebiet.

---

## 4. Was wir übernehmen sollten

Nach Wert geordnet.

1. **Wasms Trap- und Nullinit-Disziplin** (B16, B2): jeder Slot startet definiert, jeder
   Fehlschlag hat einen Namen und einen Exit-Code, ein Modul kann den Wirt nicht töten. Lyric hat
   die Regel für Felder (`:783-784`) und Arrays (`:800-807`); sie gehört auf alles. Das ist der
   Posten, der B1 ohne Fixpunkt möglich macht.
2. **JVMs Typvektor ohne Stack-Maps** (B1), getragen von der Leer-Invariante — und **§6 im
   Spec-Repo zuerst**, weil der heutige Vertrag den Absturz lizenziert.
3. **Wasms numerische Vollständigkeit** (B21): §5a schreiben, was `Interpreter.cs:1309-1485`
   längst tut. Null Code, und die Bedingung für Konstantenfaltung (B20) und für Lyricpp.
4. **JVMs `u2`-Grenzen** (B17): `maxStack`/`slotCount`/`blockCount` gedeckelt, im Format.
5. **Lyrics eigene 3.6-Fusion auf `match`** (B19-A): von 6 auf 1 Instruktion pro Arm, kein
   Formatbyte — vor jeder Registerdiskussion.
6. **Wasms Custom Section auf der freien id 0** (B8) und **BEAMs Modul-MD5** (B9).
7. **DWARFs Inline-Kette** (B6), flach und durch Ketten; **Gos Elision + Pythons
   Wiederholungs-Kollaps** (B7).
8. **Darts Zweck-Achse für den JIT** (B5), ehrlich formuliert als „darf kompilieren, wo niemand
   zuschaut", mit Diagnose, wo es ignoriert wird — und B22 als ADR, damit der Sandbox-Host nicht
   leer ausgeht.
9. **CILs Verschachtelungsregel für Handler** und **Fuzzing des Readers** (B26).
10. **R8/IL-Trimming auf die Typtabelle** (B3) und **Slot-/Block-Kompaktion** (B24).
11. **Wasms `assert_invalid`-Fälle** (B11/B15d): ein `conformance/bytecode/`-Ordner neben den 178
    Sprachfällen.
12. **ReadyToRun** für die Werkzeuge (B23).

Ausdrücklich **nicht**: LuaJITs Trace-JIT, CLRs Verzicht auf den Verifier, CPythons
Magic-pro-Version, Lua/CPythons Wert-Tag, BEAMs Hot-Code-Migration im Format, ein Budget-Bit im
Format (niemand hat es; es ist ein Runner-Vertrag), ein Snapshot-Format (falsche Kostenstelle).

---

## 5. Konflikte

**Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept):**

- **B5 löst einen Konflikt auf** — `--jit`/`LYRIC_JIT`/`HostOptions.Compile` neben
  `--profile`/`LYRIC_PROFILE` sind zwei Mechanismen für „wie schnell". Aber nur, wenn die Policy-
  Regel (Budget/Debugger ⇒ interpretiert) *im Profil dokumentiert* wird; sonst ist ein Profilfeld,
  das bei Erato nichts tut, ein dritter Mechanismus.
- **B1-4.x darf keinen Umgebungsschalter einführen** (Fassung 1: `LYRIC_STRICT_BC`). `verify
  --strict` und `LYRIC_VERIFY_IR` in der CI reichen.
- **B8** braucht eine benannte Ausnahme von „höchstens einmal" für id 0 — in der Spec, nicht im
  Reader.
- **B14c/B24** (Peephole und Kompaktion im Emitter): Kodierung an einem Ort,
  `STATUS.md:1185-1187`.
- **B11** legt einen zweiten Eingang zum Bytecode; verteidigbar nur als Test- und
  Konformanzwerkzeug, in der ADR eingegrenzt.
- **B16** ändert nichts an „Cleanup nur `defer`", aber die `finally`-Region (`:389`,
  `FunctionLowerer.cs:922-925`) bleibt der Ort, an dem `LYR-SEM0110` (§12.5) entschieden wird —
  wer §7.5 beantwortet, entscheidet mit, ob das Format eine eigene `defer`-Region bekommt.
- **B22-B** verändert die dokumentierte Budget-Einheit — ADR-Pflicht.

**Innerhalb des Gebiets:**

- **B5 gegen B22 gegen B23-C**: „ship compiled" (`HostOptions.cs:55-56`) gilt nicht für den
  Budget-Host und nicht für einen NativeAOT-Stub. Drei Aussagen, die sich heute widersprechen und
  in einem Satz zusammengeführt werden müssen: *Kompiliert wird, wo kein Budget, kein Debugger und
  ein IL-Emitter ist; überall sonst interpretiert, und das steht im `Refusals`.*
- **B1 gegen B16**: B ohne B16 ist unvollständig (`uninit`); B16 ohne B1 lässt `confuse`/`oob`
  stehen. Nur zusammen.
- **B20 gegen B21**: Faltung vor Spezifikation erzeugt zwei Wahrheiten.

**Mit anderen Gebieten:**

- **Sandbox/Embedding**: B1, B2, B16, B17, B18 entscheiden, was `Capability.None` bedeutet.
  Solange ein 58-Byte-Modul den Wirt mit Speicher oder einem Typfehler töten kann, ist die
  Capability-Grenze eine Zusage über die Sprache, nicht über die Datei.
- **Nebenläufigkeit**: B22 (Budget in Ketten), B27 (Backtrace durch Ketten), B15e (yield durch
  fremde Rahmen). Worker-Isolates (`STATUS.md:2175`, `lyric-v5-features.md:69`) bräuchten eine VM
  pro Worker — Rule-2-ADR dort, nicht hier.
- **Pakete/`lyric.json` v2**: B9 (keine doppelte Wahrheit), B25 (Whole-Program bleibt, oder das
  Paketgebiet baut einen Linker — dann ist es eine Formatfrage).
- **Werkzeuge/CLI**: B2 (Exit-Tabelle `:1084-1094`), B7, B14e, B18-B (`verify --grant`), B23.
- **Debugger/DAP**: B6-Daten müssen auch der DAP lesen.
- **Enums/Patterns**: B19-A ist eine Lowering-Änderung im Pattern-Gebiet.
- **Fehler**: B26 (Handler-Regeln), die `defer`-Uhr.
- **Spec-Repo**: B1, B16, B17, B21, B26 sind **Spec-Änderungen zuerst** (spec-first-Modus);
  B8, B9 sind Format-Minor mit Spec-Text; alles andere ist formatneutral.

---

## 6. Nach der Kritik geändert

Jeder Punkt der Kritik (`_kritik-3.json`, `bytecode-vm`), selbst nachgeprüft.

**Falsche Aussagen — übernommen:**

| Punkt | Nachprüfung | Änderung |
|---|---|---|
| `:973` „beschreibt eine Laufzeit, die es nicht gibt" | gelesen `:973-994`: §6 definiert „completely" abschließend, `:987` lizenziert den unchecked access | §1.2, B1: Befund umformuliert — der **Vertrag** ist zu schwach, nicht die Erfüllung; „Bricht" führt jetzt die Spec-§6-Änderung |
| „ids nur oberhalb von 14" | gemessen `sec0`/`sec255` ok, `sec00` BC0005 | §1.1, §1.7, B8: id 0/255 frei; Ausnahme nur für „höchstens einmal" |
| Bit 63 „Ladeprüfung ✅" als Verify-Aussage | gemessen `verify ok`, `run` CAP0001, auch mit `--grant all` | §1.1-Tabelle getrennt; **B18 neu** |
| „exit 127" | gemessen PowerShell −532462766 = 0xE0434352 | §1.2, B2: echte Zahl; 127 als MSYS-Abbildung benannt |
| „keine Konformanz-Suite" | gelesen `conformance/README.md`, 178 Fälle gezählt | §1.7, B11, B15d: Suite existiert, Lücke sind Bytecode-Fälle |
| STATUS-Zeilen lösen nicht auf | grep auf HEAD | alle Zitate neu aufgelöst (§1.8 Liste) |
| B5-A „niemand" | Sprachwissen: CPython opt-in | B5 korrigiert |

**Falsche Vergleiche — übernommen:** CLR ohne HotSpot-Deopt (B15a, Tabelle); B7-Vorbilder
(Go/Python/Java/Rust) neu zugeordnet; B15c „niemand hat es im Format" und Umformulierung als
Runner-Vertrag; B9 BEAM-MD5 statt `Dbgi`, Wasm `name` ohne Hash; B6 nur DWARF; B15b kein Wasm
`--instrument`; B15d kein CLR-TCK.

**Schwache Empfehlungen — übernommen:**

- B1-B unvollständig (`uninit`): nachgemessen; **B16 neu**, B1 = B + B16, LOC ausdrücklich
  „behauptet".
- `LYRIC_STRICT_BC` gestrichen; „kein lyrc-Modul wird abgelehnt" als behauptet markiert mit
  Messvorschrift.
- B14a Dominanztest → „blockintern oder nur Structs" (`ScalarReplacement.cs:92-99`,
  `STATUS.md:110-111` gelesen).
- B5: Budget-/Debugger-Ausschluss (`Interpreter.cs:17-26`, `:248-249`) und NativeAOT
  (`HostOptions.cs:65-67`) eingearbeitet; Empfehlung umformuliert; **B22 neu**.
- B15f gestrichen; **B23 neu** mit eigener Startzeitmessung (140–146 ms; `read` 16,9 ms).
- B7/B6 nur flach: **B27 neu** mit eigener Kettenmessung (`bt_coro`, 5 Bauten).
- B4 übersieht `match`: **B19 neu**, gemessen 6 Instruktionen pro Arm (`enummatch_noinl`).

**Fehlende Fragen — alle eingearbeitet:** B16 (Slot-Anfangswert), B17 (Ressourcengrenzen), B18
(`verify`-Vertrag), B19 (Tabellenverzweigung/`match`), B20 (Faltung/DCE, `fold_rel` nachgemessen),
B21 (numerische Semantik, `intsem`/`num` gemessen), B22 (Budget im JIT), B23 (Auslieferungsmodus),
B24 (Kompaktion, `hot_rel` nachgelesen), B25 (getrennte Übersetzung), B26 (Handler/Fuzzing, vier
neue Proben), B27 (Koroutinen-Backtrace), B28 (Reload-Identität, gelesen).

**Nicht übernommen (die Aussage hält):**

- Keine. Jeder Punkt der Kritik hat sich unter Nachmessung oder Nachlesen bestätigt. Eine
  Präzisierung: die Kritik sagt, unter `--jit` gehe „jeder Rahmen unterhalb" verloren
  (`STATUS.md:121-126`); gemessen an der Kette verschwindet **nur der kompilierte Rahmen**, die
  interpretierten darüber bleiben (§1.5) — das ist enger und für B6 günstiger.

**Eigene Korrektur ohne Kritik:** `LYR-VM0007` heißt `NullDereference`, gilt aber nur für
`optget` (`VmDiagnostics.cs:49`, `Interpreter.cs:1011`) — der Code, den B16 braucht, existiert
schon halb.
