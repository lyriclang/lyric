# Lyric 5 — Gebiet: FFI, ABI und Einbettung

Stand 2026-09-24 (zweite Fassung, nach adversarischer Kritik). Basis: Arbeitskopie `HEAD`
nach `6f6f029f` (Tree nennt sich 4.6.0, Format 4.0).
Alle Messungen mit den vorgebauten Debug-Binaries (`lyrc.dll`, `lyrvm.dll`), Standardprofil
`debug` sofern nicht anders gesagt. Proben der ersten Runde unter
`scratchpad/v5-design/probes/ffi/` (`p01`…`p27`), der Kritik unter `probes/review-ffi/`
(`r01`…`r26`), dieser Runde unter `probes/ffi-rev2/` (`q01`…`q35`).

Belegarten: **gemessen** = Programm kompiliert und gelaufen · **gelesen** = Pfad:Zeile ·
**behauptet** = weder noch. Eine ungekennzeichnete Behauptung ist ein Fehler.

> **Warnung zu Zeilenangaben.** `STATUS.md` und `PLAN.md` ändern sich unter der Hand — der
> Maintainer committet parallel im selben Checkout. Alle Zeilen unten sind am 2026-09-24
> nachgeschlagen; wo eine Zeile wandert, gilt das Zitat, nicht die Zahl.

---

## 0. Was die Kritik an der ersten Fassung umgeworfen hat

Fünf Aussagen der ersten Fassung waren falsch, und zwei davon trugen eine Empfehlung:

1. `hostAccess` ist **keine Vereinigung** der anderen Bits, sondern ein **Bypass**. Die erste
   Fassung hatte die widerlegende Kontrollmessung selbst im Text stehen und die falsche
   Folgerung daneben.
2. Die `extern`-Typmenge zu erweitern ist **nicht** „die vorhandene Maschinerie freischalten“ —
   die .NET-Binderseite hat für `?T`, `T[]`, Struct und Host-Typ **keinen einzigen Fall**.
3. Die Kostenmessung stand auf einer Uhr mit **15–16 ms Auflösung** und im **falschen Modus**
   (Interpreter statt JIT).
4. „Ein Import ist kein Wert“ war mis-skopiert: die Grenze ist das **Modul**, und `extern` ist
   eine **zweite, davon unabhängige** Grenze.
5. „Never a process abort“ ist unter `hostAccess` **heute schon gebrochen**, nicht erst mit
   einem künftigen `ffiAccess`.

Dazu kam beim Nachmessen ein Befund, den weder die erste Fassung noch die Kritik hatte:
ein `extern` kann einen **Lyric-`string` mit einer alleinstehenden Surrogathälfte** erzeugen,
und der Absturz kommt später, in einem Stdlib-Native, als **roher CLR-Stacktrace mit Exit 127**
(B15).

Was die Kritik **falsch** hatte, steht bei den betroffenen Punkten mit einem Satz dabei
(B7, B13, F4, §2-Go, §5-F11).

---

## 1. Ist-Stand

### 1.1 Es gibt vier Grenzen, nicht eine

| Weg | Wer erklärt die Signatur | Typmenge | Gate | Beleg |
|---|---|---|---|---|
| **Stdlib-Native** (`fn f(…): T;` in `stdlib/std/*.lyr`) | die Stdlib-Datei | Skalare, `bool`, `char`, `string`, `?T`, `T[]`, `?T[]`, Structs geflattet, Host-Typ | Modulname → Bit | `NativeRegistry.cs:68-94`, `Capabilities.cs:51-70` (gelesen) |
| **Native Root** (Host-SDK-Verzeichnis + `RegisterNative`) | eine `.lyr`-Datei des Hosts | wie oben, **plus opake Aliase** | das Verzeichnis, kein Bit | `HostOptions.cs:31`, `ModuleLowerer.cs:641-650` (gelesen) |
| **`RegisterFunction` / `RegisterType<T>`** | das C#-Delegate | **nur** Skalare, `string`, `char`, registrierter Host-Typ | kein Bit | `Marshal.cs:45-58` (gelesen) |
| **`extern "dotnet"`** (seit 4.5) | das Programm selbst | **nur** Skalare, `bool`, `char`, `string`, `void` | `hostAccess` (Bit 3) | `TypeChecker.cs:487` (gelesen), `q29`/`q30` (gemessen) |

Das ist bereits heute ein Rule-2-Verstoß: **vier Mechanismen für „fremder Code trifft Lyric“,
mit vier Typtabellen, vier Fehlerpolitiken und vier Gates.**

**Korrektur gegenüber der ersten Fassung.** Dort stand, `extern` sei „der engste“ Mechanismus.
Das stimmt für die **Typmenge** und ist für die **Macht** genau verkehrt: `extern` erreicht
jede `public static`-Methode des laufenden Prozesses **ohne Zutun eines Hosts**, während die
anderen drei Wege eine Registrierung oder ein SDK-Verzeichnis voraussetzen, die ein Host
kontrolliert. Gemessen: `q01` schreibt und liest eine Datei und liest `%USERNAME%` unter
`--grant host`, ohne dass irgendein Host irgendetwas registriert hätte; `q13` beendet den
Prozess. Der engste Typkanal ist zugleich der weiteste Machtkanal — das ist die eigentliche
Schieflage des Gebiets.

### 1.2 Was `extern "dotnet"` gemessen kann

| Probe | Ergebnis |
|---|---|
| `System.Math::Cbrt`, `System.IO.Path::GetTempPath` | funktioniert (`p01`) |
| `int8/16/32`, `uint8/16/32`, `uint`, `float32`, `bool`, `char` | alle breitenexakt (`p06`, `p25`) |
| Zwei `extern` auf **dasselbe** .NET-Symbol mit verschiedenen Signaturen | **zwei Import-Zeilen desselben Namens** `dotnet:System.Math::Abs`, jede mit ihrer Signatur (`q24`, gemessen — `lyrvm info` zeigt beide) |
| Zwei `extern` mit **demselben Lyric-Namen** (Überladung seit 3.0) | funktioniert (`p03`) |
| `pub extern` in einem Modul, direkt importiert und **gerufen** | funktioniert, `direct=3` (`q06`, gemessen) |
| Assembly-qualifizierter Typ (`System.Net.Dns, System.Net.NameResolution::GetHostName`) | funktioniert, auch im gepackten Binary (`p24app.exe` erneut gelaufen: `host=… cbrt=3`, gemessen) |
| Rückgabetyp weggelassen ⇒ `void` | funktioniert (`p25`) |
| Symbol weggelassen (`extern "dotnet" fn f(): string;`) | `LYR-SEM0099` — Symbol ist bei `"dotnet"` Pflicht (`q35`, gemessen) |
| `string`-Rückgabe `null` ⇒ `""` | bestätigt (`p06`) |
| Code-Punkt > U+FFFF als `char`-**Argument** | Panik `LYR-VM0016` statt Split (`p26`) |
| Surrogathälfte als `char`-**Rückgabe** | **kommt ungeprüft durch** (`q20`, gemessen; siehe B14) |
| .NET-Exception | Panik `LYR-VM0016`, **nur Lyric-Frames** (`q26`, gemessen) |
| Kapazität über Import-Kette | `hostAccess` wandert von Bibliotheksmodul zu Programm, **auch wenn nie gerufen** (`q25`, gemessen) |

**Korrektur gegenüber der ersten Fassung.** Dort war die Zeile „`pub extern` … funktioniert
(`p21`)“ mit `p21` belegt. `probes/ffi/p21_pubextern.lyr` enthält aber `apply(cbrt, 8.0)` und
erzeugt deshalb gar kein `.lyrbc` — die Probe ist nie gelaufen. Die **Behauptung** stimmt
trotzdem; sie ist jetzt mit `q06` belegt (eigene Probe, `wrapx.lyr` + Aufruf ohne Funktionswert).
Der Beleg war falsch, nicht die Aussage.

Stufe 1 tut, was `docs/guide/14-embedding.md:135-200` verspricht. Die Befunde stehen alle
**darum herum**.

### 1.3 Die Löcher

**B1 — `hostAccess` ist ein Bypass, keine fünfte Kapazität und keine Vereinigung.
(gemessen, mit zwei Kontrollen — der schärfste Befund, jetzt richtig benannt)**

```
q01  extern File::WriteAllText + ReadAllText + Environment::GetEnvironmentVariable
     lyrvm run q01.lyrbc --grant host   →  schreibt, liest, liest %USERNAME%      exit 0
q02  KONTROLLE dieselbe Datei über std.io.file.text
     lyrvm run q02.lyrbc --grant host   →  LYR-CAP0001: requires 'fileAccess'     exit 1
     lyrvm run q02.lyrbc --grant file   →  liest sie                              exit 0
q16  KONTROLLE std.os.exit(7)
     lyrvm run q16.lyrbc --grant host   →  LYR-CAP0001: requires 'osAccess'       exit 1
     lyrvm run q16.lyrbc --grant os     →  exit 7
q13  extern System.Environment::Exit(7)
     lyrvm run q13.lyrbc --grant host   →  exit 7, kein defer, keine Diagnose
```

Bit 3 gewährt `fileAccess` **nicht** — es **umgeht die Tabelle**. Dieselbe Wirkung über
`std.io.file` bzw. `std.os` wird unter `--grant host` verweigert; über `extern` läuft sie durch.
Der Unterschied ist nicht kosmetisch: eine „Vereinigung“ wäre ein dokumentierbarer
Generalschlüssel, ein Bypass ist ein Loch neben dem Schloss.

Was **doch** gesagt wird: `design/abi.md:68` schlägt ausdrücklich vor, „Bit 3 gewährt alles, ein
Host filtert zusätzlich über die Registrierung/`HostOptions.AllowedAssemblies` (Ladefehler statt
Bit)“ (gelesen). Die erste Fassung schrieb „und niemand sagt das“ — falsch. Richtig ist: es steht
in einer **Designnotiz**, und in keinem Dokument, das ein Benutzer oder ein Host zu Gesicht
bekommt — nicht in der Spec (`spec/04-modules.md:165` führt Bit 3 weiter als Gate für
`std.dotnet`, ein Modul, das es nicht gibt; `design/abi.md:15` sagt das selbst), nicht in
`docs/Bytecode.md:213` („reserved“), nicht in `lyrvm --help` (B9).

**B2 — Die normative Spezifikation kennt `extern` fast gar nicht. (gelesen, schärfer als zuvor)**
`extern` kommt in `lyric-spec/spec/` in **genau zwei** Dateien vor: `02-grammar.md` (Form) und
`appendix-a-diagnostics.md` (`PAR0044`:81, `SEM0099`:192, `VM0016`:**290**).
Nicht drin: `04-modules.md` (Kapazitätstabelle, Zeile 165 = `std.dotnet`), `11-stdlib.md`,
und — entscheidend — **`13-bytecode.md` erwähnt weder `extern` noch `dotnet`**. Die
Namenskonvention `dotnet:<Typ>::<Methode>`, die `NameMangling.ForExtern` schreibt
(`NameMangling.cs:30-31`) und die `DotnetBinding.TryBind` liest
(`DotnetBinding.cs:37-47`), steht **nirgends normativ**.
Eine zweite Runtime kann aus der Spezifikation heraus keinen `extern`-Import binden.
`design/abi.md:77-82` listet genau diese Spec-Änderungen als offen — sie sind es noch.

**B3 — Vier Typmengen, und die Erweiterung ist NICHT billig. (gelesen, Korrektur)**
Die erste Fassung schrieb: „Die Maschinerie für Arrays und Optionals existiert schon … Stufe 2
ist das Freischalten einer vorhandenen.“ **Das ist falsch, und es ist der teuerste Irrtum der
ersten Fassung**, weil daran eine Empfehlung hing.

Vorhanden ist nur die **IR-/Bytecode-Seite** (`DeclaredTypes.Lower`, `ImportShape`). Die
**.NET-Binderseite hat davon nichts**:

- `DotnetBinding.ClrTypeOf` (`DotnetBinding.cs:138-154`) kennt I8..U64, F32, F64, Bool, Char,
  String — und gibt für **alles andere `null`**, was zu
  `Unbound(import, "parameter N has a type that does not cross the \"dotnet\" boundary")` führt
  (`DotnetBinding.cs:55-57`).
- `ToClr` (`:156-178`) und `ToLyric` (`:180-196`) haben **keinen Fall** für Optional, Array,
  Struct oder Host.

Der Grund ist strukturell: bei einem **Native** trägt der **Host** das Marshalling in seinem
registrierten Delegate. Bei `extern` gibt es **keinen Host**. `?T`/`T[]` an der `extern`-Tür
freizuschalten erzeugt Module, die der Binder beim Laden ablehnt. Stufe 2 ist echte neue
Maschinerie, kein Schalter.

**B4 — Der opake Handle ist genau dort verboten, wofür er erfunden wurde. (gemessen + gelesen)**
`opaque type Handle = int; extern "dotnet" fn h(x: Handle): int …` → `LYR-SEM0099`
(`q29`, gemessen); der **einfache** Alias `type Meters = float` geht in derselben Datei durch.
Blockiert wird es allein von `CrossesHostBoundary` (`TypeChecker.cs:487`:
`type is PrimitiveType { Kind: var kind } && (kind != PrimitiveKind.Void || asReturn)`).
Das Lowering kann es längst: der `extern`-Zweig läuft durch dieselbe Bahn wie ein Native
(`ModuleLowerer.cs:158` `if (!compilation.IsNative(module) && function.Extern is null) continue;`)
und ruft `LocalAliases`/`ResolveLocalAliases` (`:165`, `:173`), deren Kommentar sagt: „plain and
opaque aliases resolve alike here“ (`ModuleLowerer.cs:641-644`).

**B5 — Über die Host-Grenze läuft kein `defer`. (gemessen, mit Kontrolle)**

| Probe | `defer` lief | gefangen |
|---|---|---|
| `q26` .NET-Exception aus `Int32::Parse` | **nein** | nein (Panik `LYR-VM0016`, Exit 101) |
| `q27` KONTROLLE: Lyric-`throw` einer `Throwable`-Klasse | **ja** (`defer ran` / `caught`) | ja |
| `q13` `Environment::Exit` | **nein** | nein (kein Diagnosekanal überhaupt) |

Lyric hat kein `finally`; `defer` ist der einzige Cleanup-Mechanismus. Die FFI-Grenze ist die
eine Stelle, an der er nicht greift — und zugleich die einzige, an der man Ressourcen hält,
die der GC nicht kennt.

**B6 — Ein Symbol ist eine `public static`-Methode und sonst nichts. (gemessen)**
- Statische **Property**: nur mit handgeschriebenem Accessor — `System.Environment::get_NewLine`
  geht (`q32`, `len=2`), `…::NewLine` nicht (`q33`, `LYR-VM0005`).
- Statisches **Feld**: `System.Diagnostics.Stopwatch::Frequency` → `LYR-VM0005` (`q34`), und
  `…::get_Frequency` ebenfalls (`q11`, erste Fassung) — `Frequency` ist ein Feld, kein Property.
- Kein Konstruktor, keine Instanzmethode, keine generische Methode
  (`DotnetBinding.cs:64-70` filtert `!m.IsGenericMethodDefinition` und nur
  `BindingFlags.Public | BindingFlags.Static`), kein `out`/`ref`, kein Enum.

Folge: `System.Math` und `System.IO.Path` sehen gut aus, weil sie zufällig statisch sind. Alles,
was ein Objekt ist — `HttpClient`, `Regex`, `TimeZoneInfo`, `ZipArchive` — ist unerreichbar.
Das sind exakt die Module, die `design/stdlib-2.md:422-425` mit „nimm `extern "dotnet"`“ aus der
Standardbibliothek ausgeschlossen hat. **Der Ausweg trägt nicht.**

**B7 — Es gibt ZWEI Funktionswert-Grenzen, und die erste ist das Modul.
(gemessen, mit Kontrolle — Korrektur)**

Die erste Fassung nannte das „ein Import ist kein Wert“ und belegte es mit einem Native und
einem `extern`. Ihre Kontrolle (`p22`) war eine Funktion im **selben** Modul — deshalb hat sie
die eigentliche Grenze nicht gesehen. Neu gemessen, mit drei Kontrollen:

| Probe | `apply(f, x)` mit | Meldung |
|---|---|---|
| `q04` **KONTROLLE** | gewöhnliche Lyric-Funktion, **selbes Modul** | läuft (`12`) |
| `q03` | `import std.math { cbrt }` — gewöhnliche Lyric-Funktion **mit Rumpf** (`stdlib/std/math.lyr:334`) | `LYR-IR0001: reference to 'cbrt' (only parameters, locals and constants)` |
| `q05` | eigenes Modul `mylib2`, `pub fn dbl` mit Rumpf | dieselbe Meldung |
| `q09` | `import std.math { sqrt }` — Stdlib-**Native** (`math.lyr:20`) | dieselbe Meldung |
| `q10` | `import wrapx { cbrt }` — `pub extern` | dieselbe Meldung |
| `q07` | `math.cbrt` modulqualifiziert | `LYR-IR0001: member access '.cbrt' on '<?>'` |
| `q08` | `extern` im **selben** Modul | **andere** Meldung: `reference to 'cbrt' as a value` |

Zwei verschiedene Diagnosetexte ⇒ zwei verschiedene Grenzen:

1. **Modulgrenze**: *jede* importierte Funktion ist kein Wert — mit Rumpf, Native oder `extern`,
   selektiv oder qualifiziert. Das ist die große, und sie trifft die **ganze** Stdlib:
   `map(xs, cbrt)` ist heute unschreibbar, egal ob `cbrt` nativ ist.
2. **Deklarationsformgrenze**: `extern` ist zusätzlich auch im **eigenen** Modul kein Wert.

Hier hatte die Kritik recht gegen die erste Fassung, war aber selbst zu grob: sie schrieb
„Nicht ‚Import‘ ist die Grenze, sondern MODUL“ — `q08` zeigt, dass es **beides** ist.

**B8 — Das Bit überzeichnet, die Bindung unterprüft. (gemessen, jetzt genau)**

| Probe | extern gerufen? | `capabilities` im Modul | `imports`-Zeile | `lyrvm verify` |
|---|---|---|---|---|
| `q17` (Symbol ist Müll, unbenutzt) | nein | `0x…8` | **weg** (`imports 1` = nur `rawPrintln`) | **ok** |
| `q18` (Symbol ist Müll, benutzt) | ja | `0x…8` | da | `LYR-VM0005` |
| `q25` (fremdes Modul importiert, nie gerufen) | nein | `0x…8` | weg | — (`--grant none` ⇒ `CAP0001`) |

Das Bit wird aus der **Deklaration** geschrieben (`ModuleLowerer.cs:972-973`:
`needed |= CapabilityTable.RequiredForImport(NameMangling.ForExtern(spec, name))`), die
**Import-Zeile** wird danach vom **Compiler** wegoptimiert (`Reachability.Prune`,
`src/Lyric.Frontend/Ir/Reachability.cs:22-28`: „Deletes unreachable functions and imports“).
Die VM sieht das Symbol also nie und **kann nicht darüber warnen**. Und: gebunden wird
**eifrig beim Laden** — `q18` verweigert, bevor irgendein `println` läuft.
Zum Kompilierzeitpunkt ist das Symbol ein Kommentar: `lyrc check q18_used_bogus.lyr` → `ok`
(gemessen).

**B9 — `--grant host` steht nicht in der Hilfe. (gelesen + gemessen)**
`src/Lyrvm/Program.cs:223` listet `file,net,os,process; all; none`; `lyrvm --help` gibt genau das
aus (gemessen). `host` funktioniert (`q01`, gemessen) und fehlt. `Capabilities.cs:128` nimmt
`"host" or "hostAccess"` entgegen.

**B10 — Die Kostenmessung der ersten Fassung war unbrauchbar. (gemessen, Korrektur)**

*Die Uhr.* `probes/ffi/p15_cost.lyr` und `p20_cost2.lyr` messen mit
`System.Environment::get_TickCount64`. Deren Auflösung, gemessen (`q11`, zwei Läufe à fünf
Proben): **15, 16, 16, 15, 16** bzw. **16, 16, 15, 16, 15 ms**. Bei 10⁶ Iterationen und zwei
Zeitstempeln je Messung ist der Quantisierungsfehler bis **±31 ns je Aufruf** — größer als der
behauptete Effekt von 65 ns. Die Zahlen der ersten Fassung (141/156/156, 219/219/203,
250/281/297) sind Vielfache eines Ticks.

*Die neue Uhr.* `System.Diagnostics.Stopwatch::GetTimestamp`, gegen die ms-Uhr kalibriert
(`q11`): **4 999 991 bzw. 5 005 455 Ticks je 500 ms ⇒ 10 MHz, 1 Tick = 100 ns**.

*Die neue Messung* (`q12`, n = 5·10⁶ je Schleife, 20 000 Aufwärmiterationen, je drei Läufe;
Debug-Toolchain, Debug-Profil):

| Schleifenkörper | Interpreter (ns/Iter.) | **JIT** (`--jit`, ns/Iter.) |
|---|---:|---:|
| `acc = acc + 1.0` (leer) | 153 · 135 · 141 | **2.5 · 3.2 · 6.8** |
| `+ plain(2.0)` (Lyric-Funktion) | 341 · 345 · 395 | **68 · 74 · 63** |
| `+ sqrt(2.0)` (Stdlib-Native) | 208 · 212 · 216 | **59 · 59 · 56** |
| `+ xsqrt(2.0)` (`extern System.Math::Sqrt`) | 253 · 245 · 249 | **111 · 120 · 105** |

Daraus, als Aufschlag über der leeren Schleife:

| | Interpreter | JIT |
|---|---:|---:|
| Lyric-Aufruf | ≈ 190–250 ns | ≈ **58–70 ns** |
| Native-Aufruf | ≈ 60–75 ns | ≈ **52–56 ns** |
| `extern`-Aufruf | ≈ 105–115 ns | ≈ **100–115 ns** |
| Reflection-Anteil (extern − native) | ≈ 35–40 ns | ≈ **46–61 ns** |

**Die Pointe, die die erste Fassung genau verkehrt hatte:** im Interpreter kostet ein
**Lyric**-Funktionsaufruf mehr als ein `extern`-Aufruf (190–250 gegen 105–115 ns) — die eigene
Dispatch-Schleife verdeckt die Grenze. Unter `--jit` dreht sich das um: der Lyric-Aufruf fällt
auf 58–70 ns, der `extern`-Aufruf bleibt bei 100–115, und ein Schleifendurchlauf kostet 3 ns.
Der Grenzübergang ist dann das **~35-Fache** eines Schleifendurchlaufs und **~1,7-mal** so teuer
wie ein gewöhnlicher Funktionsaufruf. Und `HostOptions.Compile` ist ausdrücklich der
Auslieferungsmodus: „develop on the interpreter … and ship with this on“
(`src/Lyric.Embedding/HostOptions.cs:54-56`, gelesen).

Ein `Expression.Compile`-Stub entfernt den Reflection-Anteil: **46–61 von 100–115 ns, also
~45–55 % der Aufrufkosten** — nicht 20 %, und nicht „eine Optimierung ohne Adressaten“.

**B11 — Es gibt keine Export-Richtung, und die Rückgabe ist still falsch. (gelesen)**
Kein `UnmanagedCallersOnly`, kein `Export<T>` in `src/`. Ein Host ruft
`instance.Call<T>(name, object?[])` — namentlich und untypisiert.
`Marshal.FromLyric` (`src/Lyric.Embedding/Marshal.cs:94-108`) hat **keinen Fall** für
`Array`, `Optional`, `Ref` oder `Enum` und fällt auf `_ => value.AsI64`; der anschließende
`wanted.IsInstanceOfType(boxed)`-Test ist dann erfüllt, weil ein `long` eben ein `long` ist.
Ein Host, der `Call<long>("f")` auf eine Funktion mit Rückgabe `int[]` ruft, bekommt **die
rohen Bits einer Referenz, stillschweigend**. Die Gegenrichtung `ToLyric` verweigert dieselben
Tags ausdrücklich mit `LYR-EMB0001` (`Marshal.cs:57-59`). Asymmetrisch, und die falsche Seite
ist die stille.
*(Nicht gemessen: ein Host-Testprogramm bräuchte `dotnet build`, was in diesem Checkout
verboten ist.)*

**B12 — Von Stufe 2 aufwärts existiert nichts. (gemessen)**
`extern "C"` → `LYR-SEM0099: unknown ABI "C" — this compiler binds "dotnet"` (`q30`).
Kein `std.ffi`, kein `ffiAccess` (`Capabilities.cs:8-32` endet bei Bit 4), kein `unsafe`,
kein `lyrbind` (nur `design/abi.md:83`). `unsafe` und `extern` sind heute **freie Bezeichner**
(`q31`: `let unsafe = 1; let extern = 2;` → `3`) — ein kontextuelles `unsafe { … }` bricht also
nichts, ein echtes Schlüsselwort schon.

**B13 — Packen: kein Trimming, kein AOT, also funktioniert Reflection. (gelesen, Beleg korrigiert)**
Die erste Fassung belegte das mit `build/publish.proj:134-136` („lehnt `PublishTrimmed`
grundsätzlich ab“). Das ist die **falsche Datei**: der `RejectTrimming`-Target begründet sich
mit dem **gemeinsamen Publish-Verzeichnis der Toolchain-Binäries** („the binaries share one
directory … the last publish overwrites the shared assemblies“, `publish.proj:136`) und nennt
die Lösung selbst („would need one directory per binary“, `:132`). **`Lyrstub` steht gar nicht
in der `@(Binary)`-Liste** (`publish.proj:70-93`: Lyric.Cli, Lyrc, Lyrvm, Lyrrepl, Lyrbuild,
Lyrpack, Lyrfmt, Lyrtest, Lyrls, Lyrdbg, Lyric.Embedding).

Die **Folgerung hält trotzdem**, mit dem richtigen Beleg: `src/Lyrstub/Lyrstub.csproj` setzt
weder `PublishTrimmed` noch `PublishAot` und nennt drei RIDs (`win-x64;linux-x64;osx-arm64`,
`:13`). Gemessen: `p24app.exe` (601 581 Bytes, assembly-qualifiziertes `extern`) läuft erneut
und druckt `host=… cbrt=3`. `Lyrpack/Program.cs:214` sagt: „The executable runs the program with
every capability“ — im gepackten Programm ist `hostAccess` also ohnehin geschenkt. Die Sandbox
existiert nur im Embedding und unter `lyrvm --grant`.

**B14 — Die EINGEHENDE Richtung prüft nichts. (gemessen, mit Kontrolle)**
`extern "dotnet" fn toChar(v: int32): char = "System.Convert::ToChar"` mit 55296 liefert klaglos
einen Lyric-`char` mit Code 55296 = 0xD800, eine alleinstehende Surrogathälfte (`q20`, exit 0).
**KONTROLLE** `q21`: `55296 as char` in gewöhnlichem Lyric →
`panic [LYR-VM0012]: char value 55296 is not a Unicode codepoint (valid: 0..0x10FFFF, excluding
the surrogate range 0xD800..0xDFFF)`, exit 101.
Ursache: `DotnetBinding.ToLyric`, `case TypeTag.Char: return LyrValue.FromI64((char)produced!)`
(`DotnetBinding.cs:190`) — **ohne Prüfung**, während `ToClr` die Gegenrichtung sehr wohl prüft
(`:164-170`). Die `extern`-Grenze bricht eine **normative** Invariante still:
`lyric-spec/spec/03-types.md:19` sagt „`char` — one Unicode scalar value (a code point, never a
UTF-16 unit)“ und `:161`, dass ein Wert außerhalb `LYR-VM0012` panikt **an der Umwandlung**.

**B15 — Der vergiftete String stürzt später ab, als CLR-Stacktrace.
(gemessen, mit Kontrolle — neuer Befund dieser Runde)**

```
q22  extern System.Char::ToString(toChar(55296))   →  ein Lyric-string mit einer Surrogathälfte
     s.length()                                    →  1
     s.charAt(0)                                   →  Unhandled exception. System.ArgumentException:
                                                      Found a high surrogate char without a following
                                                      low surrogate at index: 0 …
                                                        at System.Char.ConvertToUtf32(String, Int32)
                                                        at Lyric.Vm.NativeRegistry.CodepointAt(…)
                                                          NativeRegistry.cs:1248
                                                      EXIT=127
q23  KONTROLLE dieselbe Kette mit 65 ('A')          →  len=1 c0=65, exit 0
```

Das ist gravierender als B14: die Grenze **vergiftet einen gewöhnlichen Lyric-Wert**, und der
Absturz passiert **anderswo**, in einem Stdlib-Native, **ohne LYR-Code, ohne Position, ohne
Panik-Rahmen, mit rohem CLR-Stacktrace**. `spec/03-types.md:20` sagt „`string` — an immutable
sequence of code points“; `spec/13-bytecode.md:89` sagt, Strings liegen als UTF-8 im Modul.
Eine alleinstehende Surrogathälfte ist **weder** ein Code-Punkt **noch** in UTF-8 darstellbar.
Der `extern`-Kanal erzeugt also einen Wert, den das Format nicht serialisieren könnte.

**B16 — Der ABI-Name ist ein Versprechen an EINE Runtime. (gelesen + gemessen)**
`dotnet:` steht im Modul (`q24`, `lyrvm info` zeigt `dotnet:System.Math::Abs` zweimal). Eine
zweite Lyric-Runtime — und es ist eine geplant, C++-seitig — kann so ein Modul niemals binden.
Es gibt keine Aushandlung, keine Versionierung, keinen Fallback, und im Bytecode-Kapitel der
Spec steht das Präfix nicht einmal (B2). `docs/Bytecode.md:262` nennt das Prinzip
„forward-open“, aber nur für *neuere* Runtimes derselben Familie.

---

## 2. Sprachvergleich

Die Tabelle der ersten Fassung enthielt sechs sachliche Fehler. Sie sind hier korrigiert und
unten einzeln benannt.

| System | Deklarationsform | Wer schreibt sie | Zeigermodell | Fehlerkanal | Export | Preis |
|---|---|---|---|---|---|---|
| **C# `DllImport`** | `static extern` + Attribut | Mensch | roh (`IntPtr`, `unsafe`) | Rückgabewert; `SetLastError=true` schaltet `Marshal.GetLastPInvokeError` frei | `UnmanagedCallersOnly` | Marshalling-Stub: im JIT zur Laufzeit, unter NativeAOT **von ILC beim Kompilieren**; einzelne Marshalling-Formen und reflection-gesteuerte Pfade klemmen unter AOT, P/Invoke als solches nicht |
| **C# `LibraryImport`** (.NET 7+) | `static partial` + Attribut | Mensch, Stub vom Source-Generator | dito | dito | dito | engere Typmenge, `StringMarshalling` muss man hinschreiben; dafür trimmbar und ohne Laufzeit-Stubgenerierung |
| **Rust** | `unsafe extern "C" { fn … }` | `bindgen` aus dem Header | roh, Besitz im **Typ** (`CString`/`CStr`, Lifetimes) | Rückgabewert; `unsafe` sichtbar | `#[unsafe(no_mangle)] pub extern "C"`, `cbindgen` | kein GC — das Typsystem trägt, was der Mensch sonst falsch macht |
| **Zig** | **keine** — `translate-c` übersetzt den Header | das Werkzeug (früher der Compiler) | roh, aber Slices statt `ptr+len` | Rückgabewert | `export fn` | komplexe Makros scheitern; siehe Gegenentscheidung unten |
| **Java FFM / Panama** (JEP 454, final JDK 22) | `FunctionDescriptor` + `Linker.downcallHandle` zur Laufzeit | `jextract` aus dem Header | **`MemorySegment` + `Arena`**: Bounds und Lebensdauer werden **geprüft** | Exception aus dem `MethodHandle`; `Linker.Option.captureCallState` für `errno`/`GetLastError` | `Linker.upcallStub` | ohne `jextract` sehr redselig; `MethodHandle`-Indirektion; `--enable-native-access` |
| **Swift** | **keine** — Clang-Importer im Compiler, Header/Modulemap wird ein Swift-Modul | der Compiler | `UnsafePointer<T>`, `withUnsafeBytes` (Skope) | Rückgabewert; Objective-C-`NSError**` wird zu `throws` **importiert** | `@_cdecl` | ein C- **und** C++-Frontend im Compiler; enorme ABI-Arbeit |
| **Go cgo** | C-Code **im Kommentar** über `import "C"` | Mensch | roh; Go-Zeiger dürfen nicht in C-Speicher (`cgocheck`), `cgo.Handle` als Ausweg | Rückgabewert + `errno` | `//export` | Stackwechsel; die Goroutine wird ans M gebunden, `sysmon` nimmt dem M das P ab — ein langer Aufruf blockiert einen **OS-Thread**, nicht den Scheduler; Cross-Compile braucht eine C-Toolchain |
| **Lua C-API** | **keine** — `int f(lua_State*)`, alles über einen Stack | der Host, in C | roh (`lua_touserdata`) | `lua_error` / `pcall` | jede C-Funktion ist beides | jedes Argument wird **von Hand** geprüft; null Typsicherheit — dafür in einem Nachmittag implementierbar |
| **LuaJIT FFI** | `ffi.cdef[[ … ]]` — C-Deklaration als **String zur Laufzeit** | Mensch, per Copy-Paste aus dem Header | roh | `ffi.errno()`; **kein typisierter** Kanal | — | ein C-Parser in der Runtime; LuaJIT-only; dafür oft schneller als ein Lua-Aufruf |
| **WASM Component Model / WIT** | WIT-IDL, sprachneutral | `wit-bindgen` | **keine Zeiger** — `resource`-Handles als Indizes in eine Tabelle; owned/borrowed im Typ | `result<T, E>` im Typ | symmetrisch, dieselbe IDL | **Kopien an jeder Grenze** (Strings, Listen); Tooling noch in Bewegung |

**Sechs Korrekturen gegenüber der ersten Fassung** (alle: Vergleichssprache, **gelesen/gewusst**,
nicht in Lyric messbar):

1. **C# `DllImport` ≠ „AOT-feindlich“.** P/Invoke ist unter NativeAOT unterstützt; ILC erzeugt
   die Marshalling-Stubs **beim Kompilieren**. Was klemmt, sind einzelne Marshalling-Formen und
   reflection-gesteuerte Pfade. `LibraryImport` ist ergonomischer und trimmbarer — aber der
   Kontrast „DllImport = AOT-feindlich“ trägt nicht, und **F13-C baute genau darauf auf**.
2. **`SetLastError`, sonst UB — falsch.** Ohne `SetLastError` gibt es schlicht keinen Fehlerkanal
   außer dem Rückgabewert. Das ist kein Undefined Behavior; UB entsteht aus falschem Marshalling,
   was die Preis-Spalte getrennt nennt.
3. **Zig trägt `translate-c` nicht „auf ewig“ im Compiler.** In den 0.14-/0.15-Runden ist
   `@cImport` zugunsten eines **Build-Schritts** (`translate-c` als Werkzeug) abgekündigt worden,
   genau damit der Compiler das C-Frontend nicht dauerhaft trägt. Das macht Zig zum **Vorbild
   für F12-A** (Generator als Werkzeug), nicht zum Gegenbild.
4. **translate-c verwirft nichts still.** Für nicht übersetzbare Deklarationen erzeugt es
   `@compileError`-Stubs bzw. opake Typen; der Fehler fällt an der **Benutzungsstelle** an.
5. **Go cgo blockiert keinen Scheduler-Thread.** Die Goroutine wird ans M gebunden, aber `sysmon`
   nimmt dem M das P ab und gibt es einem anderen M; die Laufzeit startet dafür neue Threads.
   Ein langer cgo-Aufruf blockiert einen **OS-Thread**. Für Lyric ist der richtige Beleg ein
   anderer: **Lyric ist single-threaded und hat gar kein zweites M** — ein blockierender
   Grenzübergang hält die ganze VM an, und das ist heute schon so (`design/abi.md:43`).
6. **LuaJIT-FFI hat einen Fehlerkanal.** `ffi.errno()`; was fehlt, ist ein **typisierter** Kanal.

### Die Gegenentscheidungen, ausdrücklich

**Zig** hat entschieden, dass die FFI-Deklaration **gar nicht in der Sprache steht**. Der Header
ist die Deklaration.
*Gewinn:* null Duplikation, null Drift, kein Generator, den jemand vergisst laufen zu lassen.
*Preis:* ein C-Präprozessor und -Parser, die jemand pflegen muss — und genau deshalb hat Zig sie
**aus dem Compiler heraus** in einen Build-Schritt bewegt. Für Lyric wäre ein C-Frontend im
Compiler zu groß; ein **Werkzeug** ist es nicht (F12).

**Lua** hat entschieden, dass die Grenze **untypisiert** ist: eine einzige C-Signatur für jede
native Funktion, alles über einen Stack.
*Gewinn:* die Grenze braucht nie eine Marshalling-Tabelle, wird nie inkompatibel, und jeder Host
kann sie implementieren. Lua hat FFI nie „ausbauen“ müssen.
*Preis:* `luaL_checkinteger` in jeder Zeile, jeder Fehler erst zur Laufzeit. Das ist das exakte
Gegenteil von spec-first — und es ist bemerkenswert, dass Lyrics **Embedding-Seite**
(`RegisterFunction` mit `DynamicInvoke`) diesem Modell näher steht als dem eigenen.

**Rust** hat gegen GC entschieden und bezahlt FFI-Sicherheit mit dem Typsystem. Lyric hat GC und
kann das nicht kopieren — was übrig bleibt, ist die **Sichtbarkeit**: `unsafe` als Ort, an dem
der Kontrakt vom Menschen getragen wird, nicht als Optimierung.

**Panama** hat entschieden, dass Lebensdauer ein **Wert** ist (`Arena`) und jeder Zugriff
bounds-geprüft. Das ist das einzige Modell in der Tabelle, das zu „GC ist der einzige
Speichermechanismus“ passt, ohne zu lügen.

### Was zu Lyrics Charakter passt

- **WIT-Resource-Handles**: Indizes in eine Tabelle, owned/borrowed im Typ, keine Zeiger im
  Programm. Das ist genau `opaque type` + `TypeTag.Host`, was Lyric **schon hat**.
- **Panamas Arena**: Lebensdauer als Wert, Zugriff geprüft.
- **WITs Marshalling-Tabelle als Normtext**: eine kanonische Tabelle Typ ↔ Wire, in der
  Spezifikation, nicht im Binder — **mit einer gemeinsamen Fehlerregel** (F2).
- **`LibraryImport`s Source-Generator**: die Bindung entsteht **vor** dem Lauf.
- **jextract/bindgen/`translate-c` als Werkzeug**, nicht als Compiler-Feature — billiger, und
  `design/macros.md:83` hat den Andockpunkt (`build.lyr` schreibt Dateien) schon.
- **Swifts Import-Regel für Fehlerkonventionen**: eine Deklaration wird **werfend importiert**,
  ohne dass die Aufrufstelle etwas anderes schreiben muss (F5).

---

## 3. Designfragen

### F1 — Was ist `hostAccess`: ein Bit, ein Generalschlüssel oder ein Bypass?

**Heute:** ein **Bypass**, gemessen mit zwei Kontrollen (B1). `--grant host` erlaubt Datei-,
Umgebungs- und Prozessende-Zugriff über `extern`, während dieselben Wirkungen über
`std.io.file`/`std.os` unter demselben Grant **verweigert** werden (`q02`, `q16`).
Spec `spec/04-modules.md:165` führt Bit 3 als Gate für `std.dotnet` — ein Modul, das nicht
existiert (`design/abi.md:15`, gelesen). `docs/Bytecode.md:213` sagt „reserved“.
`design/abi.md:68` **schlägt vor**, Bit 3 solle alles gewähren (gelesen) — das ist eine
Designnotiz, keine Zusage an irgendjemanden.

| Option | Vorbild | Preis |
|---|---|---|
| **A — ehrlich dokumentieren, Verhalten unverändert:** `hostAccess` heißt „dieser Grant umgeht die Kapazitätstabelle“; `--help`, Guide 14 und §4.5 sagen es; `--grant host` bleibt, wie es ist | Deno `--allow-ffi` (ausdrücklich „this is unsafe“) | Doku und Hilfetext; **keine Zeile Logik** |
| **B — Symbol-/Assembly-Allowlist:** `HostOptions.AllowedAssemblies`/`AllowedSymbols`; ohne Eintrag lädt das Modul nicht | Java FFM `--enable-native-access=<modul>`; Deno `--allow-read=<pfad>` | ein String-Grant neben einer Bit-Maske; bereits als Vorschlag notiert (`design/abi.md:68`) |
| **C — abgeleitete Bits:** der Binder kennt eine Tabelle (`System.IO.*` ⇒ `fileAccess`, …) und **addiert** sie zu den geforderten Bits | SELinux-artige Typisierung | die Tabelle ist nie vollständig; `System.Reflection` umgeht sie in einer Zeile |
| **D — `--grant host` impliziert alle Bits** | Python `ctypes` | **Verhaltensänderung** in `CapabilityTable.Parse` (`Capabilities.cs:128`), und sie macht die Lage **schlechter**: heute verweigert `--grant host` immerhin `std.io.file` — D nimmt diese Reststrenge weg, ohne das Loch zu schließen |

**Empfehlung: A jetzt (zusammen mit B9), B für 5.0. C und D ablehnen.**

**Das ist eine Umkehr gegenüber der ersten Fassung**, die „A — `--grant host` impliziert alle
Bits, Doku, keine Zeile Logik“ als eine der drei billigsten Verbesserungen führte. Zwei Fehler
darin: es ist **keine Doku** (es ist `Capabilities.cs:128`), und es ist die **falsche Richtung**.
Der `extern`-Pfad umgeht die Bits ohnehin (B1); wer `--grant host` mächtiger macht, verliert die
letzte Stelle, an der der Grant noch etwas verweigert, und schließt nichts.

C ist die verführerische Option und die falsche: eine Präfix-Tabelle ist per Konstruktion
unvollständig, und eine Sandbox, die *fast* hält, ist schlimmer als eine, die ehrlich sagt, was
sie nicht hält. B ist kein zweiter Kapazitätsmechanismus, wenn man es richtig schneidet: die
Bits bleiben **das** Modell im Modul, die Allowlist ist eine **Ladeentscheidung des Hosts**,
genau wie `NativeRoots` heute eine Ladeentscheidung über Verzeichnisse ist.
**Bruch:** A nein (Doku). B nein (neues Host-Feld, Default „alles“). D wäre **minor** und
unerwünscht.
**4.x-Warnstufe:** `lyrc` warnt bei einem `extern`-Symbol unter `System.IO.`/`System.Net.`/
`System.Diagnostics.Process`/`System.Environment`, dass es unter `hostAccess` **ohne** das
passende Bit läuft. Das ist eine Compiler-Warnung (der Compiler sieht das Symbol, die VM nicht —
B8), und sie braucht keine Uhr, weil sie nichts verbietet.
**Hängt an:** F33, Kapazitätsgebiet, Embedding-Gebiet.

### F2 — Eine Host-Grenze oder vier — und eine Fehlerregel oder vier?

**Heute:** vier Wege, vier Typmengen (1.1, gelesen). `?string` kreuzt als Native (gemessen,
`p14`), als `extern` nicht (gemessen, `q29`/`p12`).

Und, was die erste Fassung übersah: **vier Fehlerpolitiken in derselben Richtung** (alle gelesen):

| Eintrittspunkt | Richtung | Was bei einem Typ passiert, der nicht kreuzt |
|---|---|---|
| `Marshal.ToLyric` | Host → Lyric | `LYR-EMB0001`, ausdrücklich (`Marshal.cs:57-59`) |
| `Marshal.FromLyric` | Lyric → Host | **still** `_ => value.AsI64` (`Marshal.cs:94-108`) |
| `DotnetBinding.ToClr` | Lyric → Host | Panik `LYR-VM0016` (`DotnetBinding.cs:164-170`) |
| `DotnetBinding.ToLyric` | Host → Lyric | **prüft gar nicht** (`:180-196`; gemessen: B14, B15) |

| Option | Vorbild | Preis |
|---|---|---|
| **A — eine Marshalling-Tabelle UND eine Fehlerregel, vier Eintrittspunkte** | WIT Canonical ABI | ein Refactoring: `CrossesHostBoundary`, `DeclaredTypes.Lower`, `HostFunction.TypeOf` und `Marshal` lesen dieselbe Tabelle; **und** die vier Fehlerwege werden einer |
| **B — `extern` wird der Grundmechanismus, Native Roots und `RegisterFunction` werden Spezialfälle** (`extern "host"`) | Lua: eine Grenze, ein Modell | größerer Umbau; `RegisterFunction` ist die bequeme Host-API und darf nicht sterben |
| **C — so lassen, nur `extern` auf die Native-Root-Menge heben** | — | drei Tabellen statt vier; und nach B3 ist es **nicht billig** |

**Empfehlung: A, und B als Formulierung obendrauf.** Gegenüber der ersten Fassung präzisiert:
eine gemeinsame **Tabelle** ohne eine gemeinsame **Fehlerregel** vereinheitlicht die Hälfte, die
weniger weh tut — B14/B15 sind Fehlerregel-Befunde, keine Tabellen-Befunde. Die Tabelle **und**
die Regel gehören in die Spezifikation (§13), nicht in vier C#-Dateien. Rule 2 verlangt hier
keine Abschaffung — vier *Eintrittspunkte* sind in Ordnung, vier *Semantiken* sind es nicht.
**Bruch:** die Tabelle nein (Erweiterung); die **Fehlerregel minor** — `Marshal.FromLyric` hört
auf, still zu lügen (B11), und `DotnetBinding.ToLyric` beginnt zu prüfen (B14/B15). Beides
nimmt einem Host eine falsche Antwort weg.
**Hängt an:** F20, F21, Bytecode-/Format-Gebiet (§13 bekommt Normtext).

### F3 — Welche Typen kreuzen?

**Heute:** Skalare, `bool`, `char`, `string`, `void` (`TypeChecker.cs:487`, gemessen `q29`,
`q30`). Das ist Stufe 1 aus `design/abi.md:64`.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Native-Root-Menge:** `+ ?T, T[], Struct geflattet, Host-Typ, opaker Alias` | die IR-Seite ist gebaut (`DeclaredTypes.Lower`) | **nicht billig** (B3): `ClrTypeOf`/`ToClr`/`ToLyric` brauchen vier neue Fälle **plus** die Frage, wem ein Array gehört und wie ein Host-Typ überhaupt entsteht, wenn kein Host registriert hat (F22) |
| **A′ — nur opaker Alias** (die Teilmenge ohne neue Binderfälle) | Lyrics eigenes `opaque type` | eine Zeile Sema (F4); der Wire-Typ bleibt `int` |
| **B — A plus Instanzen und Callbacks** (Stufe 2 vollständig) | C# P/Invoke, Panama-Upcalls | Callbacks brauchen F8 (Funktionswerte) **und** F16 (fremde Threads) **und** F23/F24 (Budget, Reentranz) |
| **C — WIT-Vollmenge** (Records, Varianten, Listen, Resources) | Component Model | eine IDL-Denkweise, die Lyric nicht hat |

**Empfehlung: A′ sofort, A gestaffelt, B nicht vor F16/F22/F23/F24.**
**Korrektur gegenüber der ersten Fassung**, die A „fast nichts — Freischaltung, keine neue
Maschinerie“ nannte und als zweitbilligste Verbesserung führte. Gemessen/gelesen ist das falsch
(B3). Die einzige wirklich billige Teilmenge ist der **opake Alias**, weil er auf dem Draht ein
`int` ist und deshalb **keinen** neuen Binderfall braucht.
Die Reihenfolge innerhalb von A, nach steigendem Preis: opaker Alias (kein neuer Fall) →
`?T` über `Nullable<T>`/Referenz-null (ein Fall je Richtung) → `T[]` per Kopie (Besitzfrage) →
Struct geflattet (Out-Buffer-Konvention) → Host-Typ (braucht F22).
**Bruch:** nein (alles Erweiterung). **4.x:** A′ kann in 4.7.
**Hängt an:** F4, F8, F20, F21, F22.

### F4 — Darf ein opaker Alias die Grenze kreuzen?

**Heute:** nein (gemessen, `q29`), obwohl das Lowering es kann (`ModuleLowerer.cs:158`, `:165`,
`:173`, `:641-644`, gelesen). Der einfache Alias geht durch (`q29`, dieselbe Datei).

| Option | Vorbild | Preis |
|---|---|---|
| **A — erlauben, Layout des Underlying** | WIT `resource` (Index in eine Tabelle); Lyrics eigene `std.io.net.Socket` | eine Bedingung in `CrossesHostBoundary`; kein neuer Binderfall, weil der Wire-Typ das Underlying ist |
| **B — verbieten und die Grammatik-Prosa korrigieren** | — | nimmt Lyric das einzige Werkzeug, mit dem ein Handle nicht fälschbar ist |
| **C — erlauben, aber `@Owned`/`@Borrowed` als Vokabular verlangen** | Rust `CString`/`CStr`, WIT own/borrow | Attribute „beschreiben und tun nichts“ (Guide 15) — und auf einem `extern` **darf heute gar kein Attribut stehen** (F25, gemessen) |

**Empfehlung: A.**
**Korrektur gegenüber der ersten Fassung**, die schrieb: „Das ist ein **Bug**, kein Design: die
Spezifikation verspricht es.“ Das ist **überzogen**. Der Satz (`docs/Grammar.md:190-191`,
normativ `lyric-spec/spec/02-grammar.md:221-222` und `spec/03-types.md:143`) sagt: „in a
**native** signature the alias resolves to the underlying“. `extern` ist seit 4.5 eine **eigene
Deklarationsform**, keine native Signatur im Vokabular der Spec — und die Spec sagt über
`extern`-Typmengen überhaupt nichts (B2). Die Empfehlung bleibt trotzdem **A**, aus einem
anderen Grund: es ist eine **Design-Inkonsistenz** zwischen zwei Deklarationsformen, die
dasselbe Lowering benutzen, und die teurere Seite (der Handle) ist die verbotene. Die
Kostenschätzung hält (geprüft, s. o.).
**Bruch:** nein. **4.x:** direkt in 4.7, ohne Uhr — es erlaubt nur, was heute abgelehnt wird.
**Hängt an:** Typ-Gebiet (`opaque type`), F3, F22.

### F5 — Wie kommt ein Host-Fehler in die Sprache?

**Heute:** als Panik `LYR-VM0016` (gemessen, `q26`); `extern` darf kein `throws` deklarieren
(`TypeChecker.cs:462-464`, gemessen über `q30`-Variante). `spec/appendix-a-diagnostics.md:290`
nennt das ausdrücklich „stage 1“ und sagt, die Abbildung auf `throws` sei nicht entschieden.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Panik bleiben lassen** | Zig `@panic` bei unerwartetem C-Fehler | eine `Regex`-Bibliothek über `extern` kann einen Syntaxfehler nicht melden, nur sterben |
| **B — `extern … throws HostError`**, Klasse mit `kind`, `typeName`, `message` | Java FFM (Exception aus dem `MethodHandle`); Swift (Objective-C-Fehlerkonvention wird **werfend importiert**) | ein neuer Stdlib-Typ; `throws`-Inferenz muss ihn kennen; typed throws St. 1–3 (PLAN 4.7, #5–7) ist die Vorbedingung |
| **C — `?T`/`Result` zurückgeben, `attempt(…)`** | Rust (Fehler im Rückgabewert) | jede Signatur wird zwei Signaturen; widerspricht „Antwortform als Suffix“ (`stdlib-2.md:113`) |
| **D — B, aber `throws` am `extern` **optional**: ohne Klausel weiter Panik | Swift `rethrows` bzw. die **Import**-Regel, nicht `try!` | zwei Verhalten für eine Deklarationsform — Rule 2 gespannt, aber migrierbar |

**Empfehlung: D, mit B als Zielzustand.** Ohne D bricht jede bestehende `extern`-Deklaration,
sobald Host-Fehler werfbar werden; mit D wandert der Code deklarationsweise. Der Zielzustand ist
B, weil ein Host-Fehler *derselben* Sorte ist wie `IoError` — eine gewöhnliche Weltlage, kein
Programmierfehler.
**Vorbild korrigiert:** die erste Fassung nannte für D „Swift (`throws` vs. `try!`)“. Das passt
nicht: `try!` ist eine **Aufrufstellen**-Form, die einen werfenden Aufruf in eine Falle
verwandelt; D beschreibt eine optionale Klausel an der **Deklaration**. Das Swift-Gegenstück ist
die Import-Regel für Objective-C-Fehlerkonventionen (eine Deklaration wird werfend importiert)
oder `rethrows`.
**Bruch:** minor (eine bislang panikende Stelle beginnt zu werfen, sobald `throws` dransteht).
**4.x-Warnstufe — und hier ist das Problem:** die Ratchet-Mechanik des Projekts ist
`@Deprecated { message, until }` plus Build-Fehler, wenn die Version eintrifft. **Auf einem
`extern` darf kein Attribut stehen** (`LYR-PAR0042`, gemessen, F25). Eine reine `lyrc`-Warnung
geht („dieses `extern` ohne `throws` darf in 5.0 werfen“), die **projektübliche** Uhr nicht.
Das muss F25 vorher lösen, sonst ist die Migrationsstufe nicht mit den Mitteln des Projekts
beschreibbar.
**Hängt an:** typed throws (PLAN 4.7 #5–7), F6, **F25**, F32.

### F6 — Läuft `defer` über die Host-Grenze?

**Heute:** nein (gemessen `q26`, Kontrolle `q27`). Lyric hat kein `finally`. Bei
`Environment::Exit` läuft nicht einmal die Panik-Maschinerie (`q13`).

| Option | Vorbild | Preis |
|---|---|---|
| **A — mit F5 erledigt:** ein Host-Fehler wird ein `throw`, und `defer` läuft wie immer | Lyrics eigener Fehlerpfad | löst nur den Exception-Fall, nicht Budget-Stopp, nicht Marshalling-Panik, nicht `Exit`/`FailFast` |
| **B — `defer` läuft auf **jedem** Abwickelpfad, Panik eingeschlossen** | **Go: `defer`s laufen beim Panic-Unwinding IMMER** | die Panik verliert ihre Eigenschaft „nichts läuft mehr“ — und genau die ist als **Sicherheitseigenschaft** dokumentiert: `ExecutionBudget.Charge` (`ExecutionBudget.cs:58-61`) sagt „A catchable stop would be one a hostile script could sit out“ |
| **C — ein eigener Cleanup-Pfad nur für Host-Ressourcen** (`using`-artig) | C# `using`, Python `with` | ein zweiter Cleanup-Mechanismus neben `defer` — Rule 2 direkt verletzt |

**Empfehlung: A, und B ausdrücklich ablehnen.** Aber: solange F5 offen ist, muss die Doku sagen,
dass `defer` an dieser Grenze nicht greift — heute sagt sie es nirgends. Und eine
**Marshalling**-Panik (`p26`, B14) sollte gar keine Panik sein, sondern derselbe Host-Fehler wie
F5: ein Code-Punkt außerhalb der BMP ist ein Konvertierungsfehler, kein Programmierfehler der VM.
**Vorbild präzisiert:** die erste Fassung schrieb „Go (`recover` sieht die `defer`-Kette)“ und
rechnete B den Preis von `recover` zu. Das vermischt zwei Dinge: in Go laufen `defer`s beim
Panic-Unwinding **immer**, und `recover` ist eine zweite, unabhängige Fähigkeit. Man kann B
(defer läuft) also **ohne** recover haben. Der Preis von B folgt daher nicht aus Go, sondern
aus Lyrics **eigener** Budget-Zusage (`ExecutionBudget.cs:58-61`) — und die trägt ihn allein.
**Bruch:** nein. **4.x:** Doku-Satz in Guide 14 und ein Hinweis in `LYR-VM0016`.
**Hängt an:** F5, F23, Fehler-Gebiet, Koroutinen-Gebiet.

### F7 — Was ist ein Symbol?

**Heute:** eine `public static`-Methode. Property nur über `get_X` von Hand (gemessen, `q32`/
`q33`); statisches Feld gar nicht (gemessen, `q34`); keine Instanz, kein Konstruktor, keine
Generics (`DotnetBinding.cs:64-70`).

| Option | Vorbild | Preis |
|---|---|---|
| **A — Symbolgrammatik erweitern:** `Typ::Methode`, `Typ::.field`, `Typ::.prop`, `Typ::new`, `Typ#methode` (Instanz, Receiver = Parameter 0) | C# `DllImport`-EntryPoint ist auch nur ein String; Kotlin cinterop-`.def` | eine Mini-Sprache im String, die niemand prüft, bis geladen wird (F30) |
| **B — typisierte Deklarationsform statt String:** `extern "dotnet" type HttpClient = "System.Net.Http.HttpClient";` plus `extern fn (this: HttpClient) …` | Swift-Importer; WIT `resource` | mehr Grammatik, aber der Compiler kann prüfen, was er sieht; braucht **F22** (wer hält die Instanz am Leben?) |
| **C — so lassen und alles Nicht-Statische über `lyrbind` generieren lassen** | bindgen/jextract | verlagert das Problem in ein Werkzeug, das es noch nicht gibt — und der Generator kann nur ausdrücken, was A oder B erlauben |

**Empfehlung: B für Typen und Instanzen, A für den Rest (Feld, Property, Konstruktor) — aber B
erst nach F22.** Ohne Instanzen ist `extern "dotnet"` genau das, was gemessen wurde:
`System.Math` und `System.IO.Path`. Damit trägt `stdlib-2.md:422-425` („Regex/HTTP/Kompression
nimm über die Host-ABI“) **nicht**, denn alle fünf sind Objekte.
**Bruch:** nein (Erweiterung). **4.x:** A direkt; B nicht vor F22.
**Hängt an:** F3, **F22**, F25 (die Uhr, falls eine Symbolform je zurückgenommen wird),
Typ-Gebiet.

### F8 — Wann ist eine Funktion ein Wert?

**Heute:** nur innerhalb ihres eigenen Moduls, und ein `extern` nicht einmal dort (B7,
sieben Messungen mit Kontrolle).

**Das ist die Korrektur, die am meisten ändert.** Die erste Fassung schrieb „Ein Import ist kein
Wert“ und schlug drei Optionen vor, die **alle** an der falschen Stelle ansetzten. `map(xs, cbrt)`
scheitert nicht, weil `cbrt` nativ ist — `cbrt` hat einen Rumpf (`stdlib/std/math.lyr:334`).
Es scheitert, weil es **importiert** ist.

| Option | Vorbild | Preis |
|---|---|---|
| **A — jede Funktion ist überall ein Wert**, Modulgrenze eingeschlossen | Lua, Python, C# (Delegate), Rust (`fn`-Item als Wert) | das eigentliche Loch im Lowering: ein Funktionswert braucht einen stabilen Index über Modulgrenzen; heute kennt die Aufrufstelle nur den Namen. Plus: `Reachability.Prune` braucht den Wert als Wurzel |
| **A′ — A, aber `extern`/Native bleiben ausgenommen** | — | löst `map(xs, cbrt)` (der peinliche Fall) und lässt die FFI-Grenze in Ruhe; zwei Regeln statt einer |
| **B — Thunk um den Import** | — | löste **nur** den `extern`-Fall und lässt `map(xs, cbrt)` stehen; die erste Fassung hielt das für die Lösung |
| **C — so lassen; Callbacks nur über explizite `CFn`-/Delegate-Typen** | Rust `extern "C" fn` als eigener Typ | `map(xs, cbrt)` bleibt unschreibbar |

**Empfehlung: A.** Es ist kein FFI-Feature, sondern ein Loch im Lowering, das FFI nur besonders
sichtbar macht — und es ist **größer**, als die erste Fassung dachte. Die FFI-Seite (`extern`
als Wert) ist die kleinere Hälfte und kann nachkommen.
**Bruch:** nein (alles, was heute kompiliert, kompiliert weiter).
**4.x — Terminierung, präzise gelesen (Korrektur):** die erste Fassung schrieb, F8 „steht unter
PLAN §C als Bug, nicht als Feature. Gut — dann kommt er früh.“ Das hält so nicht.
`PLAN.md:59` und `PLAN.md:271-273` sagen zweimal, die „restlichen `IR0001`-Grenzen“ **sollten**
unter **C** bleiben, „weil sie Fehler sind und 4.6 stabil wird“. Der **Abschnitt C selbst**
(`PLAN.md:113-165`) enthält **keinen solchen Posten**, und `PLAN.md:60-62` erklärt B–E für
**leer** und die Tag-Bedingung für erfüllt. Die einzige Terminierung für Funktionswerte steht
als Nebensatz: `PLAN.md:143` — „Lyric hat Funktionswerte und baut sie in **4.7** aus“.
Ehrlich: **Absicht zweimal formuliert, Posten nirgends gelistet, Datum 4.7.**
**Hängt an:** Lowering/IR-Gebiet, F3 (Callback als kreuzender Typ), F16.

### F9 — Die Export-Richtung: gibt es sie, und ist sie typisiert?

**Heute:** es gibt sie nicht (gelesen: kein `UnmanagedCallersOnly`, kein `Export<T>` in `src/`).
Ein Host ruft `instance.Call<T>(name, object?[])`, und die Rückgabe eines `int[]` kommt als
`value.AsI64` durch — **still falsch** (`Marshal.cs:94-108`, gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| **A — Bugfix zuerst:** `FromLyric` verweigert `Array`/`Optional`/`Ref`/`Enum` mit `LYR-EMB0001`, wie `ToLyric` es tut | die eigene Gegenrichtung | ein Host, der sich heute auf die Zahl verlässt, bricht — aber er verlässt sich auf Müll |
| **B — typisierter Export:** `pub extern fn onTick(dt: float): void { … }` ⇒ `instance.Export<Action<double>>("onTick")` | C# `UnmanagedCallersOnly`; Go `//export`; `cbindgen` | ein Flag im Funktionseintrag (Reachability-Wurzel), plus `Expression.Lambda`-Marshalling |
| **C — C-Einsprung im Stub:** `lyric_call(name, args…)` als `UnmanagedCallersOnly` | Rust `cbindgen`, Swift `@_cdecl` | verlangt NativeAOT im Stub, das es heute nicht gibt (B13) |

**Empfehlung: A sofort (Bugfix), B für 5.0, C nicht vor einem AOT-Stub.** B ist die Hälfte, die
Lyric zur *Bibliothek* macht statt nur zur *Skriptsprache* — und der Grund, warum `pub extern`
heute nur „Import“ heißen kann.
**Bruch:** A minor (ein Host bekommt einen Fehler statt einer Zahl), B nein.
**4.x-Warnstufe — korrigiert:** die erste Fassung schrieb „A kann sofort als **Warnung** im
Host-Log laufen“. **Es gibt kein Host-Log.** `Lyric.Embedding` hat keinen Diagnosekanal;
`HostOptions.Output`/`Error` (`HostOptions.cs:81-85`) sind ausdrücklich „Where a **script**
writes“. Eine Warnstufe braucht also zuerst eine Festlegung: entweder ein neues Feld auf
`HostOptions` (`Diagnostics`/`OnWarning`) oder — billiger — ein Schalter
`HostOptions.StrictMarshalling`, der A sofort als Fehler schaltet und per Default aus ist, bis
5.0 ihn umdreht. Ohne diese Festlegung ist die Warnstufe nicht umsetzbar.
**Hängt an:** Embedding-Gebiet, Bytecode-Gebiet (Export-Flag), F2 (gemeinsame Fehlerregel).

### F10 — Ist der ABI-Name Teil des Portabilitätsvertrags?

**Heute:** `dotnet:` steht im Modul (`q24`, gemessen); es gibt genau eine ABI, der Vergleich ist
case-sensitiv (`TypeChecker.cs:445` `spec.Abi != "dotnet"`, gemessen `q30`). Eine zweite Runtime
kann ein solches Modul nie binden, und die Konvention steht nicht einmal in der Spec (B2).

| Option | Vorbild | Preis |
|---|---|---|
| **A — so lassen, und aussprechen:** ein Modul mit `dotnet:`-Import ist an die .NET-Runtime gebunden, Punkt | Go cgo (ein cgo-Programm ist an eine C-Toolchain gebunden) | zwei Klassen von `.lyrbc` — portable und nicht; das sollte dann im **Header** stehen, nicht nur im Import-Namen |
| **B — abstrakte ABI:** `extern "host"` statt `"dotnet"`, jede Runtime bindet auf ihre Weise | **Panama `SymbolLookup`** (eine Suchstrategie, die der Host stellt) | funktioniert nur für Symbole, die überall existieren — `System.Math::Cbrt` gibt es in C++ nicht unter dem Namen |
| **C — beide:** `"dotnet"`/`"c"` als konkrete ABIs, `"host"` als abstrakte, die der Host auflöst | Panama `Linker` vs. `SymbolLookup` | drei ABI-Namen, drei Regeln |
| **D — Fallback-Ketten:** `extern "dotnet|c" fn …` mit zwei Symbolen | — | ein Modul, das auf zwei Runtimes verschieden rechnet — genau die Sorte Falle, die eine Spec verbieten sollte |

**Empfehlung: A, aber sichtbar.** Der Modul-Header sollte sagen „dieses Modul braucht die ABI
*X*“, so wie er heute sagt, welche Bits es braucht — dann ist die Ablehnung durch eine zweite
Runtime eine **Ladediagnose** statt eines Bindungsfehlers tief drin. C ist nachrüstbar, ohne A
zu widerrufen. D ausschließen.
**Vorbild korrigiert:** die erste Fassung nannte für B „Java `System.loadLibrary`“. Das ist
schief: `System.loadLibrary` lädt eine **plattformspezifische JNI-Bibliothek nach
Namenskonvention** und abstrahiert keine ABI. Das passende Java-Vorbild ist `Linker`/
`SymbolLookup` aus Panama — so steht es in C bereits richtig.
**Bruch:** nein (Header wächst nur durch Hinzufügen, wie die Kapazitätsbits).
**4.x:** nichts. **Hängt an:** F29 (Plattform gehört in denselben Header), Bytecode-Gebiet.

### F11 — `extern "C"`: braucht es `unsafe`, und welches Speichermodell?

**Heute:** existiert nicht (gemessen, `q30`). `unsafe` ist ein freier Bezeichner (gemessen,
`q31`). `STATUS.md:2466-2470` dokumentiert ein **anderes** `unsafe` als „No“: das zum Entfernen
von Bounds-Checks.

| Option | Vorbild | Preis |
|---|---|---|
| **A — `CPtr<T>` opak, Zugriff nur in `unsafe { }` über `std.ffi.read/write/slice`** | Rust | neues kontextuelles Schlüsselwort (bricht nicht, `q31`); **beliebiger Prozessspeicher steht offen** |
| **B — Arena/Segment:** `std.ffi.Arena` mit `alloc`, Segmente mit Länge, jeder Zugriff bounds-geprüft; `defer arena.close()` | **Java FFM** | Prüfkosten pro Zugriff; dafür kein `unsafe` nötig und die Sandbox bleibt eine Aussage |
| **C — Handle-Tabelle wie WIT:** gar keine Zeiger im Programm, nur Indizes; der Binder besitzt den Speicher | WASM Component Model | schließt genau die Fälle aus, für die man C ruft (Zero-Copy-Puffer) |
| **D — kein `extern "C"`, Punkt.** Alles Native über .NET (das kann P/Invoke bereits) | — | C-Bibliotheken bleiben erreichbar — über eine C#-Zwischenschicht, die jemand schreiben muss |

**Empfehlung: B. A nur mit einem eigenen ADR, und ohne die Abgrenzung der ersten Fassung.**
Panamas Arena ist das einzige Modell, das zu „GC ist der einzige Speichermechanismus“ passt,
ohne zu lügen: die Lebensdauer ist ein **Wert** im Programm, `defer` schließt sie, und ein
Zugriff nach dem Schließen ist ein Fehler statt Speicherkorruption.

**Korrektur gegenüber der ersten Fassung.** Dort stand: „das dokumentierte `unsafe`-Nein
betrifft Bounds-Checks, nicht FFI-Skopen. Wer die beiden verwechselt, lehnt F11 mit einer
Begründung ab, die auf F11 nicht zutrifft.“ Das stimmt für **B** und **nicht für A**. Der Kern
der dokumentierten Begründung ist nicht die Kosten-Nutzen-Rechnung, sondern dieser Satz
(`STATUS.md:2469`, gelesen): „`Capability.None` means nothing if a script may read past an
array.“ Dieses Argument trägt auf A **mindestens genauso** — bei einem rohen `CPtr` mit
`read`/`write` steht nicht nur ein Array-Ende offen, sondern der ganze Prozessraum. Die
Abgrenzung gilt also **nur für B**, und sie sollte auch nur für B behauptet werden.
**Bruch:** nein (`unsafe` kontextuell, neues Bit nur additiv).
**4.x:** ein `ffiAccess`-Bit kann schon in 4.7 reserviert werden, wie Bit 3 seit 1.0 — **aber
nur, wenn F1 vorher beantwortet ist**, sonst wiederholt Bit 5 den Fehler von Bit 3.
**Hängt an:** F1, F12, F18, F33, Stdlib-Gebiet (`std.ffi`).

### F12 — Wer schreibt die Bindungen?

**Heute:** der Mensch, von Hand, ohne Prüfung bis zur Ladezeit (gemessen: `q18` kompiliert,
`lyrc check` sagt `ok`, `lyrvm verify` scheitert). `lyrbind` existiert nur in `design/abi.md:83`.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Werkzeug `lyrbind`,** liest eine .NET-Assembly per Reflection bzw. C-Header per ClangSharp, schreibt `gen/*.lyr` | bindgen, jextract, **und Zigs `translate-c` seit es ein Build-Schritt ist** | ein Generatorlauf, den jemand vergisst; generierter Code im Repo |
| **B — im Compiler** (`@cImport`-artig) | Swift; Zig **bis 0.13** | ein C-/CLR-Frontend im Compiler auf ewig — und Zig ist gerade dabei, genau das rückgängig zu machen |
| **C — `comptime`:** eine `comptime`-Funktion liest die Assembly und erzeugt Deklarationen | LuaJIT `ffi.cdef`, D `mixin` | genau das allgemeine Makrosystem, das ausgeschlossen ist |
| **D — gar nichts, von Hand** | C# DllImport | die Lage von heute; skaliert bis etwa zwanzig Symbolen |

**Empfehlung: A.** `design/macros.md:83` hat den Andockpunkt schon benannt (build.lyr schreibt
Dateien vor dem Bau, `gen/` als `sourceRoot`-Zweig, Kopfkommentar, `lyric fmt`). Wichtig: **mit
Filter**, nicht „alles“ — jextracts Erfahrung ist, dass ein ungefiltertes Bindungsmodul
unbenutzbar groß wird. Zig ist hier jetzt **Vorbild**, nicht Gegenbild (§2).
**Bruch:** nein. **4.x:** kann jederzeit kommen, es ist ein Werkzeug, keine Sprachregel.
**Hängt an:** Werkzeug-/Build-Gebiet, F7 (was der Generator ausdrücken kann), F30.

### F13 — Wann wird gebunden, und was heißt das für AOT?

**Heute:** eifrig beim Laden (gemessen, `q18`), aber nur für **erreichbare** Imports — ein
unerreichbares `extern` steht **gar nicht mehr im Modul** (gemessen, `q17`: `imports 1`,
`capabilities 0x…8`, `verify: ok`). Kein Trimming, kein NativeAOT im Stub
(`Lyrstub.csproj`, gelesen), also funktioniert Reflection im gepackten Binary (gemessen,
`p24app.exe` erneut).

| Option | Vorbild | Preis |
|---|---|---|
| **A — so lassen** (Reflection zur Ladezeit) | LuaJIT ffi | kein AOT, kein Trimming |
| **B — Stubs zur Ladezeit** (`Expression.Compile`/`DynamicMethod`) | C# `DllImport`-IL-Stub im JIT | kauft **46–61 ns je Aufruf ≈ 45–55 % der Grenzkosten unter `--jit`** (gemessen, B10); AOT bleibt zu |
| **C — Stubs zur Bauzeit:** `lyrc` schreibt eine Stub-Tabelle ins Modul bzw. `lyrpack` erzeugt einen passenden Stub | **C# `LibraryImport`** (Source-Generator) und **ILC unter NativeAOT** | lyrpack muss das Modul **lesen** — Bruch mit „packing is not verification“ (`design/abi.md:70`), aber nur lesend |
| **D — B für den Interpreter, C für lyrpack/AOT** | .NET selbst (JIT-Stub und ILC-Stub existieren nebeneinander) | zwei Pfade, die dieselbe Tabelle erfüllen müssen |

**Empfehlung: D.** B lohnt sich messbar (B10), und zwar im Auslieferungsmodus, nicht nur
theoretisch. C kommt, wenn NativeAOT ein Ziel wird.
**Vorbild korrigiert:** die erste Fassung baute C auf „DllImport = AOT-feindlich“. Das ist falsch
(§2, Korrektur 1) — P/Invoke läuft unter NativeAOT, ILC erzeugt die Stubs beim Kompilieren. C
begründet sich nicht aus einem Mangel von P/Invoke, sondern daraus, dass **Reflection über
`Type.GetType` und `GetMethods`** unter Trimming/AOT nicht überlebt.

**Teilempfehlung „alle deklarierten externs binden, nicht nur die erreichbaren“ — an die
richtige Komponente gehängt (Korrektur).** Die erste Fassung schrieb „ab 4.7 warnt `lyrvm
verify`“. **So nicht baubar**: die VM sieht das Symbol nie (gemessen, `q17`). Das Pruning
passiert im **Compiler** (`Reachability.cs:22-28`). Also:
- Die Warnung ist eine **`lyrc`**-Aufgabe: „dieses `extern` ist unerreichbar und wird nicht
  gebunden; sein Kapazitätsbit bleibt trotzdem stehen“.
- „Alle binden“ heißt: `Reachability.Prune` hört auf, `extern`-Imports zu entfernen. Das ändert
  die **emittierten Bytes** jedes betroffenen Moduls — also auch die Byte-Identität gleicher
  Eingaben, die das Projekt anderswo zusichert. Die erste Fassung nannte das „minor“; das
  **unterschätzt** es. Richtig: ein Format-/Reproduzierbarkeits-Thema, das mit dem
  Bytecode-Gebiet abgestimmt gehört.
**Bruch:** Warnung nein. „Alle binden“: **minor am Verhalten, aber Byte-Identität betroffen**.
**Hängt an:** Build-/Pack-Gebiet, Optimierer-Gebiet (Reachability), F30, F34.

### F14 — Darf die Standardbibliothek `extern "dotnet"` benutzen?

**Heute:** nein — die Stdlib-Natives gehen über `NativeRegistry`, und `design/stdlib-2.md:422-425`
schließt Regex, HTTP, Zeitzonen, Kompression, AES/RSA aus der std aus mit der Begründung
„nimm `extern "dotnet"`“. `STATUS.md:2167-2174` führt diese **Umkehr** als eine der vier
Entscheidungen, „none of them answered anywhere“ (gelesen; Zeile wandert, Zitat gilt).

| Option | Vorbild | Preis |
|---|---|---|
| **A — Stdlib bleibt kapazitätssauber:** was `hostAccess` bräuchte, kommt nicht in die std | Go (alles in der std ist nativ implementiert) | `std.net.tls`, `std.regex`, `std.zip` gibt es nie; der offene TLS-Faden bleibt offen |
| **B — dünne Lyric-Hüllen über .NET-Natives**, unter den **vorhandenen** Bits (`networkAccess` für HTTP, `fileAccess` für Zip), **nicht** über `extern` | .NET selbst; Python (`zlib`, `ssl` sind C-Module der std) | jedes Modul braucht Natives im `NativeRegistry` — Laufzeitcode, kein Nutzercode |
| **C — Stdlib-Module benutzen `extern "dotnet"`** und ziehen damit `hostAccess` | — | **verboten, sobald F1 zeigt, was `hostAccess` ist**: `import std.regex` würde einen Grant fordern, der die Kapazitätstabelle **umgeht** — und gemessen (`q25`) wandert das Bit auch dann mit, wenn nie gerufen wird |
| **D — C, aber die Stdlib bekommt ein eigenes, engeres Bit** | — | ein fünftes Gate für dieselbe Sache; Rule 2 |

**Empfehlung: B, und C ausdrücklich ausschließen.** Dieser Punkt ist der Grund, warum F1 vor der
Bibliotheksfrage beantwortet werden muss. Mit dem korrigierten F1 ist das Argument **stärker**
als in der ersten Fassung: `hostAccess` ist nicht bloß ein grober Generalschlüssel, sondern ein
**Bypass** — eine `extern`-gestützte Stdlib würde jedes Programm, das sie importiert, aus der
Kapazitätstabelle herausnehmen.
**Beleg korrigiert:** die erste Fassung nannte `PLAN.md:284-300` für die Bibliotheks-Umkehr.
Dort steht die Ergonomie-Tabelle. Richtig sind `STATUS.md:2167-2174` und
`design/stdlib-2.md:422-425`.
**Bruch:** nein. **4.x:** nichts. **Hängt an:** F1, F28 (eine `extern`-gestützte Stdlib wäre
per Konstruktion untestbar), Stdlib-Gebiet.

### F15 — Was kostet ein Grenzübergang, und wie schnell muss er sein?

**Heute gemessen** (B10, `q12`, Stopwatch-Uhr, n = 5·10⁶, je drei Läufe):
unter `--jit` kostet ein `extern`-Aufruf **100–115 ns** über einem Schleifendurchlauf von
**2,5–6,8 ns**; ein Stdlib-Native **52–56 ns**; ein gewöhnlicher Lyric-Aufruf **58–70 ns**.

| Option | Vorbild | Preis |
|---|---|---|
| **A — nichts tun** | Go cgo (das ist teurer und Go lebt damit) | in einer Pro-Frame-Schleife über 10 000 Aufrufe sind 110 ns = 1,1 ms — ein Sechstel eines 60-fps-Frames |
| **B — `Expression.Compile` bei erster Bindung** | .NET-JIT-Stub | entfernt **46–61 ns ≈ 45–55 %** der Aufrufkosten; ein paar hundert Zeilen, kein Sprachzug |
| **C — der JIT kennt Extern-Aufrufe direkt** | LuaJIT FFI | der JIT kompiliert Native-Calls bereits (gemessen: Native fällt von 208 auf 56 ns) — der `extern`-Pfad tut es offenbar nicht im selben Maß; hier läge der größere Hebel |

**Empfehlung: B, und C prüfen. A verwerfen.**
**Das ist eine Umkehr gegenüber der ersten Fassung**, die „B, wenn das Profil es zeigt, sonst A“
empfahl mit der Begründung: „Wer aus einer Lyric-Schleife heraus eine C-Bibliothek 10⁶-mal ruft,
hat ein Schleifenproblem, kein FFI-Problem.“ Das war eine Interpreter-Aussage. Im
Auslieferungsmodus (`HostOptions.Compile`, „ship with this on“, `HostOptions.cs:54-56`) gibt es
**kein Schleifenproblem mehr**, das den Grenzübergang verdeckt: er kostet das ~35-Fache eines
Schleifendurchlaufs und **mehr als ein gewöhnlicher Funktionsaufruf**. Der Reflection-Anteil ist
kein Rundungsfehler, sondern die Hälfte.
Außerdem bemerkenswert und in der ersten Fassung nicht gesehen: **im Interpreter ist ein
`extern`-Aufruf billiger als ein Lyric-Funktionsaufruf** (105–115 gegen 190–250 ns). Wer FFI im
Interpreter misst, misst die Dispatch-Schleife, nicht die Grenze.
**Bruch:** nein. **Hängt an:** JIT-/Optimierer-Gebiet, F13-B, F34.

### F16 — Callbacks aus fremden Threads: Verbot oder Queue?

**Heute:** die Frage ist **beantwortet, aber widersprüchlich**, und die erste Fassung nannte sie
fälschlich „gar nicht gestellt“.
`design/abi.md:78` (gelesen): „Callbacks aus fremden Threads sind **verboten** (Guard: Thread-ID
prüfen, sonst Panik)“.
Die Designrunde 2026-09 (Memory: „Callbacks = Queue + Lyric-Handler“) sagt **etwas anderes**.
Dazu: `ExecutionBudget` ist ausdrücklich „not thread-safe, which is the runtime's standing
contract: one thread“ (`ExecutionBudget.cs:24`, gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| **A — verbieten, Guard mit Thread-ID, sonst Panik** | die eigene Notiz `abi.md:78` | jede GUI- und jede async-Bibliothek ist damit unbenutzbar — sie rufen aus einem Pool-Thread zurück |
| **B — Queue:** der fremde Thread legt den Aufruf in eine Queue, der VM-Thread arbeitet sie ab | .NET `SynchronizationContext`; Node `uv_async_send` | der fremde Thread muss **warten** oder der Callback ist asynchron — eine C-API, die einen Rückgabewert erwartet, bekommt ihn nicht rechtzeitig |
| **C — den aufrufenden Thread an die VM anhängen** | **Panama**: ein `upcallStub` attacht den nativen Thread an die JVM | setzt eine mehr-Thread-fähige VM voraus — Lyric hat keine |
| **D — beides: Guard als Default, Queue als opt-in des Hosts** | — | zwei Regeln, aber die zweite ist eine Host-Entscheidung, keine Sprachregel |

**Empfehlung: D — A als Sprachregel (Default), B als Host-Fähigkeit.** Der Guard ist das, was
die VM selbst garantieren kann; die Queue braucht einen Scheduler, und der gehört ins
Koroutinen-Gebiet. **Das gehört entschieden, bevor F3-B gebaut wird**, sonst existieren zwei
Antworten in zwei Dokumenten.
**Vorbild korrigiert:** die erste Fassung nannte „Panama hat `Arena.ofShared`“ als Antwort auf
diese Frage. `Arena.ofShared()` regelt die **Lebensdauer und den Mehr-Thread-Zugriff auf einen
`MemorySegment`** — mit Upcalls aus fremden Threads hat es nichts zu tun. Panamas Antwort darauf
ist C (der Stub attacht den Thread).
**Bruch:** nein (heute existiert nichts). **4.x:** nichts.
**Hängt an:** Koroutinen-/Scheduler-Gebiet, F3-B, F23, F24.

### F17 — Variadische Symbole

**Heute:** unerreichbar, aber aus dem falschen Grund. Eine `params`-Methode ist eine gewöhnliche
`public static`-Methode mit einem Array-Parameter und scheitert an `ClrTypeOf`
(`DotnetBinding.cs:138-154`, gelesen) — nicht an einer Variadik-Regel.

| Option | Vorbild | Preis |
|---|---|---|
| **A — verbieten und dokumentieren** | WIT verbietet es; Go cgo kann es nicht | eine `printf`-artige C-API braucht eine Hülle |
| **B — feste Arität je Deklaration** (`extern … fn printf2(f: string, a: int, b: int)`) | Rust `...` in `extern` erlaubt es, `bindgen` erzeugt es | pro Arität eine Deklaration; auf der .NET-Seite ist es ohnehin nur ein Array |
| **C — `params`-Überladungen zulassen, sobald `T[]` kreuzt (F3)** | C# selbst | dann ist es kein Variadik-Feature, sondern ein Array-Feature |

**Empfehlung: A für `extern "C"` (Normtext), C für `"dotnet"` als Folge von F3.**
**Was die erste Fassung nicht sagte und sagen muss:** `params`-Überladungen tauchen heute schon
in der **Kandidatenliste** der Fehlermeldung auf (`DotnetBinding.cs:74-80` sammelt alle
`GetMethods`-Treffer mit dem Namen), obwohl sie nie bindbar sind. Ein Benutzer liest also
Kandidaten, die er nicht wählen kann. Das gehört entweder gefiltert oder markiert.
**Bruch:** nein. **4.x:** die Filterung/Markierung der Kandidatenliste ist eine
Diagnose-Verbesserung, sofort machbar.
**Hängt an:** F3, Diagnostik-Gebiet.

### F18 — Struct-Layout an einer C-Grenze

**Heute:** stellt sich nicht — Lyrics Structs kreuzen **geflattet** (`ModuleLowerer.cs:638-642`,
gelesen), also sieht der Binder Skalare.

| Option | Vorbild | Preis |
|---|---|---|
| **A — geflattet bleiben, `extern "C"` nimmt keine Structs** | die eigene Native-Konvention | eine C-API mit `struct sockaddr` braucht eine Hülle in C# |
| **B — `@Repr("C")` als Attribut mit Wirkung** | Rust `#[repr(C)]` | das **zweite** compilergelesene Attribut (Guide 15: „describes; does nothing“) — und auf `extern` dürfen heute gar keine stehen (F25) |
| **C — Layout in der Spec je RID fixieren** (Padding, Alignment, Endianness) | WIT Canonical ABI; Swifts ABI-Stabilität | Normtext, und er muss pro Plattform stimmen |

**Empfehlung: A jetzt, C als Normtext **bevor** `extern "C"` kommt.** Das ist Spec-Arbeit (§13),
keine Implementierungsfrage, und sie gehört in denselben Spec-PR wie F2 und F10.
**Bruch:** nein. **4.x:** nichts. **Hängt an:** F11, F2, Bytecode-Gebiet.

### F19 — Versionierte Bindungen und Bibliotheksauflösung

**Heute:** ebenfalls **beantwortet, aber nur in einer Designnotiz** — die erste Fassung nannte
es „gar nicht gestellt“ und zitierte zwei Sätze später dieselbe Stelle.
`design/abi.md:73` (gelesen): „`lib` wird über `NativeLibrary.Load` mit RID-Suchpfad aufgelöst
(`libc`, `libz.so.1`, `zlib1.dll`)“.

| Option | Vorbild | Preis |
|---|---|---|
| **A — `NativeLibrary.Load` und fertig** | die Notiz | .NET-Verhalten ist kein Vertrag: eine zweite Runtime (C++) hat kein `NativeLibrary` und keine RID-Tabelle |
| **B — Suchordnung in der Spec** (Name, SONAME, plattformübliche Präfixe/Suffixe, Suchpfade, in dieser Reihenfolge) | Kotlin cinterop-`.def`; Panama `SymbolLookup.libraryLookup` | Normtext pro Plattform, und er muss gepflegt werden |
| **C — die Bibliothek gehört ins Projekt, nicht in die Deklaration** (`lyric.json` nennt sie, `extern` nennt nur das Symbol) | Kotlin cinterop; `Cargo.toml` `links` | koppelt FFI ans Projektsystem — was F27 ohnehin verlangt |

**Empfehlung: B als Normtext, C als Ort.** A ist der Ist-Stand einer Notiz und kein Vertrag.
**Bruch:** nein (existiert nicht). **4.x:** nichts.
**Hängt an:** F11, F27, F29, Bytecode-/Spec-Gebiet.

### F20 — Was ist ein `char` an der Grenze, und wer prüft die EINGEHENDE Richtung? (neu)

**Heute:** niemand. Gemessen mit Kontrolle (B14): `extern … : char = "System.Convert::ToChar"`
mit 55296 liefert klaglos einen Lyric-`char` mit Code 0xD800; `55296 as char` panikt
`LYR-VM0012`. Ursache `DotnetBinding.cs:190` (`(char)produced!`, keine Prüfung), während
`ToClr` (`:164-170`) die Gegenrichtung sehr wohl prüft. Normativ gebrochen:
`spec/03-types.md:19` („one Unicode scalar value … never a UTF-16 unit“) und `:161`.

| Option | Vorbild | Preis |
|---|---|---|
| **A — eingehend prüfen, Verstoß ist `LYR-VM0012`** (dieselbe Diagnose wie `as char`) | die eigene Regel | eine Bedingung in `ToLyric`; eine bisher stille Stelle beginnt zu panieren |
| **B — eingehend prüfen, Verstoß ist ein Host-**Fehler** (`LYR-VM0016`/`HostError`)** | Java FFM (Konvertierungsfehler ist eine Exception aus dem Handle) | braucht F5; unterscheidet sauber „das Skript hat Mist gebaut“ von „die Außenwelt hat Mist geliefert“ |
| **C — ersetzen statt prüfen** (Surrogat → U+FFFD) | Web-Plattform (`USVString`) | still und verlustbehaftet — genau die Sorte Antwort, die Lyric sonst ablehnt |
| **D — so lassen und die Invariante abschwächen** (`char` darf eine UTF-16-Einheit sein) | .NET, Java | bricht `spec/03-types.md:19` und jede Kodierungszusage |

**Empfehlung: B, mit A als Übergang.** Die Richtung, die der Host bestimmt, ist ein
**Weltlage**-Fehler, kein Programmierfehler — deshalb B, sobald F5 steht. Bis dahin A, weil eine
laute falsche Antwort besser ist als eine stille. **C und D ablehnen.** Dass `ToClr` prüft und
`ToLyric` nicht, ist kein Design, sondern eine vergessene Hälfte.
**Bruch:** **minor** — ein Programm, das heute eine Surrogathälfte durchreicht, panikt danach.
Das ist genau der Fall, den man brechen will.
**4.x-Warnstufe:** keine sinnvolle. Der Wert ist heute schon kaputt; eine Warnung könnte nur zur
Laufzeit und an derselben Stelle stehen, an der A die Panik setzt. Ehrlicher: direkt A.
**Hängt an:** F2 (gemeinsame Fehlerregel), F5, F21.

### F21 — Was ist ein Lyric-`string` auf dem Draht? (neu)

**Heute:** ungeprüft durchgereicht (`DotnetBinding.cs:184-186`: `produced as string ?? ""`), mit
der in B15 gemessenen Folge: eine alleinstehende Surrogathälfte kommt in einen Lyric-`string`,
`length()` sagt 1, und `charAt(0)` stürzt als **unbehandelte CLR-`ArgumentException`, Exit 127,
ohne LYR-Code** ab (`NativeRegistry.cs:1248`). Kontrolle mit 'A': exit 0.
Normativ: `spec/03-types.md:20` „an immutable sequence of **code points**“;
`spec/13-bytecode.md:89` „UTF-8 without BOM“.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Folge von Unicode-Skalaren, an der Grenze geprüft**; ein `string` mit unpaariger Surrogathälfte kreuzt nicht | WIT (`string` ist gültiges Unicode, Kopie an der Grenze); Rust `String` | eine Prüfung pro String-Rückgabe — O(n) an einer Grenze, die ohnehin kopiert |
| **B — Folge von UTF-16-Einheiten** (die .NET-Wahrheit) | .NET, Java, JavaScript | bricht `spec/03-types.md:20`, bricht `13-bytecode.md:89` (UTF-8 kann es nicht darstellen), und macht `length()` mehrdeutig |
| **C — ersetzen** (unpaarige Hälfte → U+FFFD) | Web-Plattform, `String.toWellFormed()` | still; aber es ist die einzige Option, die **nie** abstürzt und keine Signatur ändert |
| **D — so lassen** | — | gemessener Prozessabsturz ohne Diagnose (B15) |

**Empfehlung: A, mit derselben Fehlerregel wie F20 (also `HostError`, sobald F5 steht).**
D ist gemessen unhaltbar. B widerspricht zwei normativen Sätzen **und** dem Format. C ist
verführerisch, weil es billig ist, aber es verwandelt einen Datenfehler in stille
Datenkorruption — und die geplante zweite (C++-)Runtime müsste dieselbe Ersetzung
bitgenau nachbauen, sonst rechnen zwei Runtimes verschieden.

**Warum das über FFI hinausgeht:** F2 fordert eine Marshalling-Tabelle als Normtext. Diese
Tabelle ist ohne die **Kodierungsfrage** nicht schreibbar: sie entscheidet, was `CStr` in Stufe 2
bedeutet (UTF-8 mit Prüfung? WTF-8? roh?) und ob eine zweite Runtime die Tabelle überhaupt
erfüllen kann.
**Bruch:** **minor** (ein bisher stiller Absturz wird eine Diagnose).
**4.x:** A kann sofort als Prüfung kommen — der heutige Zustand ist ein Absturz ohne Code, kein
Verhalten, auf das sich jemand verlassen kann.
**Hängt an:** F2, F5, F20, Strings-Gebiet, Bytecode-Gebiet.

### F22 — Wer hält ein Host-Objekt am Leben, und bleibt Identität erhalten? (neu)

**Heute:** unbeantwortet, und das steht so im Projekt: `STATUS.md:2160-2162` (gelesen; Zeile
wandert) — „**The open question to answer before E4**: the lifetime and identity of a host object
across the boundary — does the host keep it alive or the VM? That is the one place in M10 where
I have no answer yet, and it belongs asked before E4 starts.“
Technisch: `TypeTag.Host` (0x47) trägt den registrierten Typnamen inline
(`spec/13-bytecode.md:619`, gelesen); `Marshal.FromLyric` prüft beim Zurückreichen
`wanted.IsInstanceOfType(host)` und wirft sonst `LYR-EMB0003` (`Marshal.cs:84-91`, gelesen).

| Option | Vorbild | Preis |
|---|---|---|
| **A — der Host besitzt, die VM hält eine schwache Referenz** | COM-artiges „der Erzeuger befreit“ | ein Skript kann ein totes Handle halten; jeder Zugriff braucht eine Lebendprüfung |
| **B — die VM besitzt: das Handle ist eine starke Referenz, der GC entscheidet** | die Lage heute (`value.Ref`) | ein Skript, das ein Handle vergisst, hält eine `HttpClient`-Instanz am Leben; Lyric hat kein `dispose` |
| **C — Arena/Skope wie bei F11-B:** das Handle gehört einem Skopus, `defer scope.close()` | **Java FFM `Arena`**; C# `using` | eine dritte Lebensdauer-Form neben GC und `defer` — aber sie ist dieselbe wie F11-B, also **eine**, nicht zwei |
| **D — Handle-Tabelle mit Indizes, Identität über den Index** | **WIT `resource`** | Identität ist per Konstruktion erhalten und fälschungssicher; kostet eine Tabelle pro VM |

**Empfehlung: D für die Identität, C für die Lebensdauer — und beides zusammen mit F11-B
entscheiden, weil es dasselbe Modell ist.** B ist der Ist-Stand und beantwortet die Frage nach
Identität gar nicht: zwei Marshalling-Runden über dasselbe .NET-Objekt liefern heute dieselbe
Referenz, aber nichts sagt das zu, und für eine zweite Runtime ohne CLR-Referenzen ist es nicht
formulierbar.
**Warum es hier stehen muss:** F3 will Host-Typen kreuzen lassen, F7 will Instanzen. Beide sind
ohne diese Antwort **nicht entscheidbar**, und `opaque type` als Handle ist damit nur halb
entschieden.
**Bruch:** nein, solange nichts zugesichert ist — und genau das ist das Problem.
**4.x:** nichts baubar, bevor die Frage beantwortet ist.
**Hängt an:** F3, F7, F11, Typ-Gebiet, Embedding-Gebiet (E4).

### F23 — Wie wird ein Grenzübergang gegen das Ausführungsbudget verrechnet? (neu)

**Heute:** als **eine Instruktion**. `ExecutionBudget` ist ausdrücklich „COUNTED, NOT TIMED, and
that is the point rather than a limitation: the same program with the same budget stops at the
same instruction on every machine“ (`ExecutionBudget.cs:13-17`, gelesen). Ein
`extern "dotnet" fn sleep(ms: int32) = "System.Threading.Thread::Sleep"` kostet also eine
Instruktion, egal ob 1 ms oder 60 s; ein hängendes `File::ReadAllText` auf einem Netzlaufwerk
ebenso.

| Option | Vorbild | Preis |
|---|---|---|
| **A — so lassen und aussprechen:** das Budget schützt gegen Schleifen, nicht gegen fremden Code | die eigene Notiz („a budget cannot count it“ steht schon für kompilierten Code, `HostOptions.cs:55`) | ein Host, der ein Mod mit `hostAccess` **und** einem Budget lädt, glaubt geschützt zu sein und ist es nicht |
| **B — ein Grenzübergang kostet einen Pauschalbetrag** (z. B. 1000 Instruktionen) | — | deterministisch und reproduzierbar (die Eigenschaft bleibt), aber die Zahl ist willkürlich und misst nichts |
| **C — zweites, zeitbasiertes Budget** (`WallClockBudget`) | Deno `--v8-flags`; Erlang-Reduktionen + Timer | braucht einen zweiten Thread — genau das, was `ExecutionBudget.cs:15-17` als die abgelehnte Alternative benennt; und es macht Läufe nicht-reproduzierbar |
| **D — `CancellationToken` an die Grenze reichen**, der Host bricht den Host-Aufruf ab | .NET selbst (jede `Async`-API); Panama hat es nicht | nur für Symbole, die einen Token nehmen — `Thread::Sleep` nimmt keinen |

**Empfehlung: A sofort als Normtext, B als Option des Hosts, C und D ablehnen.**
Der Satz, der heute fehlt und in §4.5 oder Guide 14 gehört: **„Ein `extern`-Aufruf ist eine
Instruktion. Ein Budget begrenzt, wie viel ein Skript *rechnet*, nicht wie lange es *wartet*.
Wer fremden Code mit `hostAccess` lädt, hat auch die Wartezeit zugestanden.“** Das ist unbequem
und ehrlich; C wäre bequem und würde die dokumentierte Reproduzierbarkeit opfern.
Die Formulierung „a stop a script could sit out“ (`ExecutionBudget.cs:58-61`) braucht dann einen
Zusatz: sie gilt für den **Stopp**, nicht für den **Aufruf** — ein Skript, das in fremdem Code
steht, sitzt heute alles aus.
**Bruch:** nein (Normtext über bestehendes Verhalten).
**4.x:** der Satz kann sofort in Guide 14.
**Hängt an:** F1, F16, F24, Embedding-Gebiet, Koroutinen-Gebiet.

### F24 — Was passiert bei Reentranz Skript → Host → Skript? (neu)

**Heute:** ein **Prozessabbruch**, und er ist bekannt. `PLAN.md:112` (gelesen) führt unter
Abschnitt B („Prozessabbrüche — Exit 134/141 statt Diagnose oder Panik“):
„Reentranz Skript→Host→Skript umgeht `MaxCallDepth` → CLR-StackOverflow | ScriptInstance.cs:226“.
Die Stelle ist `ScriptInstance.Invoke` (`ScriptInstance.cs:226-235`, gelesen) — der Weg, auf dem
ein Host in ein Skript hineinruft, auch aus einem Native heraus.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Reentranztiefe zählen**, `MaxCallDepth` gilt über die Grenze hinweg | Lua (`LUAI_MAXCCALLS` zählt C-Ebenen mit) | ein Zähler auf der VM statt auf dem Frame-Stack; billig |
| **B — Reentranz verbieten**: ein Native darf nicht ins Skript zurückrufen | WIT (keine synchronen Reentranz-Aufrufe im Basisprofil) | tötet Callbacks (F3-B) und damit jede Bibliothek, die einen Handler nimmt |
| **C — Reentranz erlauben, aber in eine Queue** (wie F16-B) | Node, jede Event-Loop | Callbacks werden asynchron; eine C-API mit Rückgabewert geht nicht |
| **D — so lassen** | — | gemessener, notierter Prozessabbruch an genau der Grenze, die F3-B und F9-B verbreitern würden |

**Empfehlung: A, und zwar **bevor** F3-B oder F9-B gebaut werden.** Das ist ein existierendes
Loch **genau an dieser Grenze**, und beide Ausbauten machen es breiter: F9-B (typisierter Export)
gibt dem Host einen bequemen Weg hinein, F3-B (Callbacks) gibt dem Skript einen Weg, den Host
dazu zu bringen.
**Anmerkung zu §5 der ersten Fassung:** dort wurde „Never a process abort“ nur für ein künftiges
`ffiAccess` diskutiert. Es ist **heute** an zwei Stellen gebrochen: hier (gelesen, `PLAN.md:112`)
und über `Environment::Exit`/`FailFast` (gemessen, `q13`/`q14`).
**Bruch:** A ist **minor** — ein Host, der sich heute auf unbegrenzte Reentranz verlässt, bekommt
eine Panik statt eines Absturzes. Das ist die gewünschte Richtung.
**4.x:** A gehört nach `PLAN.md` ohnehin unter B; es ist ein Bugfix, keine Sprachregel.
**Hängt an:** F3-B, F9-B, F16, F23.

### F25 — Wie bekommt eine einzelne `extern`-Deklaration eine Uhr? (neu)

**Heute:** **gar nicht.** Gemessen (`q19`): `@Deprecated { message = "use cbrt2" }` auf einem
`extern` →
`LYR-PAR0042: an attribute cannot sit on an extern declaration — a function, a struct, a class,
an enum, a member of one, or the module header carries one`.

`extern` ist damit die **einzige vom Benutzer geschriebene Deklarationsform, die den
Migrationsmechanismus des Projekts nicht tragen kann** — `@Deprecated { message, until }` plus
Ratchet-Build-Fehler, wenn die Version eintrifft.

**Das trifft den Auftrag ins Zentrum.** Der Auftrag lautet: Lyric 4 Stück für Stück auf einen
Stand bringen, der den Wechsel auf 5 erlaubt, **über Warnungen und Deprecation**. F5, F7, F10,
F13, F20 und F31 planen alle Änderungen an `extern` — und keine davon ist mit den heutigen
Mitteln als Uhr an der einzelnen Deklaration beschreibbar.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Attribute auf `extern` erlauben** (nur `@Deprecated`, oder allgemein) | die eigene Regel für jede andere Deklarationsform | Parser + `PAR0042`-Bedingung; die Begründung für das Verbot ist nicht dokumentiert, also muss sie erst gefunden werden |
| **B — nur eine Modul-weite Uhr:** `@Deprecated` am Modulkopf deckt alle `extern` darin | die vorhandene Modulkopf-Regel (`PAR0042` nennt sie) | zu grob: ein Modul mit zwanzig `extern` bekommt eine Uhr für alle |
| **C — reine `lyrc`-Warnung ohne Attribut**, per Regel im Compiler | Rusts `future_incompat`-Lints | geht sofort und deckt die **Compiler**-Seite; deckt **nicht** die Benutzerseite (ein Nutzer kann sein eigenes `extern` nicht als veraltet markieren) |
| **D — die Uhr hängt am ABI-Namen, nicht an der Deklaration** (`extern "dotnet@1"`) | Versionierte WIT-Welten | löst Sprachwechsel, nicht Symbolwechsel |

**Empfehlung: A, und zwar zuerst — vor F5, F7, F13 und F20.** Ohne A hat dieses Gebiet keinen
Migrationspfad, und die Antwort auf jede Bruchfrage unten lautet dann zwangsläufig „C, eine
Warnung, und in 5.0 bricht es“. Mit A wird `extern` eine Deklarationsform wie jede andere, und
der Ratchet gilt.
**Bruch:** nein (es erlaubt, was heute abgelehnt wird).
**4.x:** direkt in 4.7 — es ist die **Vorbedingung** für die Warnstufen der anderen Fragen.
**Hängt an:** F5, F7, F13, F20, F31, Metaprogrammierungs-/Attribut-Gebiet.

### F26 — Was gilt, wenn ein Host ein `dotnet:`-Symbol überschreibt, das das Modul überladen hat? (neu)

**Heute:** eine fünfte Semantik an der Grenze, und sie ist nur im Guide erwähnt.
`docs/guide/14-embedding.md:163` (gelesen): „A host that has registered a native under the same
`dotnet:` name wins over reflection: a registration is a decision, reflection is the default.“
Dieselbe Zusage in `DotnetBinding.cs:18-21` (gelesen).

Gemessen (`q24`): zwei `extern` auf `System.Math::Abs` erzeugen **zwei Import-Zeilen mit
demselben Namen**, unterschieden allein durch die Signatur.
Gelesen (`NativeRegistry.cs:166`): `Bind` sucht mit `_natives.TryGetValue(import.Name, …)` —
**nur über den Namen**. Und `:193-196`: passt die Signatur nicht, wird
`ImportsNotBound: native '…' has a different signature than the module expects` geworfen —
es gibt **keinen Rückfall auf Reflection**.

Folge: eine Host-Registrierung unter `dotnet:System.Math::Abs` deckt genau eine der beiden
Zeilen, und das Modul **lädt nicht mehr**.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Registrierung nach Name UND Signatur schlüsseln** | die Import-Tabelle selbst | `RegisterNative` bekommt die Signatur mitgeteilt — hat sie im Delegate ohnehin |
| **B — bei Signatur-Fehlschlag auf Reflection zurückfallen** | — | „eine Registrierung ist eine Entscheidung“ gilt dann nur manchmal — schlimmer als der Fehler |
| **C — eine Registrierung sperrt den Namen vollständig:** jede Zeile dieses Namens, deren Signatur nicht passt, ist ein **Ladefehler mit einer Erklärung** statt einer Signaturmeldung | Java FFM `--enable-native-access` (ein Modul ist erlaubt oder nicht) | ehrlich, aber der Host kann ein überladenes Symbol nicht teilweise ersetzen |
| **D — so lassen** | — | eine kryptische Signaturmeldung für einen Fall, den niemand dokumentiert hat |

**Empfehlung: A, mit C als Fehlermeldung, wenn A nicht greift.**
**Warum das wichtiger ist, als es aussieht:** diese Registrierung ist die **einzige vorhandene
Naht**, an der ein Host ein Symbol abfangen, sperren oder für einen Test ersetzen kann. F1-B
(Allowlist) und F28 (Testbarkeit) hängen beide daran. Die erste Fassung erwähnte sie überhaupt
nicht.
**Bruch:** A **minor** (eine Host-API bekommt einen Parameter; alte Aufrufe müssen eine Signatur
nennen oder behalten die Namens-Semantik als Default).
**4.x:** A kann sofort additiv kommen (`RegisterNative(name, signature, delegate)` neben dem
alten Überzug).
**Hängt an:** F1-B, F28, Embedding-Gebiet.

### F27 — Sieht ein Konsument vor dem Bauen, dass eine Abhängigkeit `hostAccess` verlangt? (neu)

**Heute:** nein. Gemessen (`q25`): ein Programm, das `wrapx { cbrt }` **importiert und nie
ruft**, trägt `capabilities 0x…8` und wird unter `--grant none` abgelehnt. Das Bit wandert also
über die Import-Kette (`ModuleLowerer.cs:972-973`, gelesen), und zwar unabhängig von
Erreichbarkeit.
4.5 hat das Projektsystem gebracht — `lyric.json` mit `sourceRoot`, `testRoot`, `nativeRoots`,
`dependencies` (`src/Lyric.Core/ProjectFile.cs:40-78`, gelesen). **Es gibt dort keinen
Kapazitätsschlüssel**: `grep -i "capab\|grant" src/Lyric.Core/ProjectFile.cs` findet nichts
(gemessen). Das Wort `lyric.json` kam in der ersten Fassung nicht vor.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Kapazitätsmanifest:** ein Paket deklariert in `lyric.json`, welche Bits es zieht; `lyrbuild` warnt, wenn ein Import mehr zieht als deklariert | npm `permissions`-Vorschläge; **Deno `deno.json`**; Android-Manifest | zwei Wahrheiten (Manifest und Bytecode) — die Prüfung muss sie abgleichen, sonst driften sie |
| **B — nur eine Warnung beim Auflösen:** `lyrbuild` sagt beim Ziehen einer Abhängigkeit, welche Bits sie mitbringt; kein Manifest | `cargo` zeigt `build.rs`-Skripte an | eine Zeile Ausgabe, keine neue Datei, keine Drift — und keine Zusage |
| **C — `lyrc check` meldet die Bits des Ergebnisses** (schon heute in `lyrvm info`, nur nicht im Compiler) | — | verschiebt die Frage auf nach den Bau |
| **D — nichts** | — | ein Paket kann `hostAccess` in jedes Programm ziehen, das es importiert, ohne dass es irgendwo steht |

**Empfehlung: B jetzt, A für 5.0.** B ist billig und hat keine Drift: die Wahrheit bleibt der
Bytecode, die Ausgabe ist eine Ableitung. A ist die richtige Endform, aber nur, wenn das Manifest
**geprüft** wird (sonst ist es Dokumentation, die lügt) — und diese Prüfung ist genau das, was
`ModuleLowerer.cs:972-973` schon rechnet.
**Bruch:** B nein. A **minor** (ein Paket ohne Manifest braucht einen Default „alles erlaubt“).
**4.x:** B sofort.
**Hängt an:** F1, Build-/Pakete-Gebiet, CLI-Gebiet.

### F28 — Wie testet man Code hinter einem `extern`? (neu)

**Heute:** gar nicht isoliert. `lyrtest` baut eine `LangVm` mit `Capability.All` und ruft
**nirgends** `RegisterNative` (`src/Lyrtest/Program.cs:132-145`, gelesen — die Naht existiert auf
`LangVm.cs:164`, aber der Testläufer reicht sie nicht durch). Jede `extern`-gestützte Funktion
trifft also im Test die echte .NET-Umgebung: die echte Uhr, das echte Dateisystem, den echten
Rechnernamen.

| Option | Vorbild | Preis |
|---|---|---|
| **A — `lyrtest` bekommt eine Naht:** ein Testmodul darf `dotnet:`-Symbole durch Lyric-Funktionen ersetzen | Python `unittest.mock.patch`; Go: Interface + Fake | eine Ersetzung zur Ladezeit, und sie braucht F26 (Schlüssel nach Signatur), sonst deckt sie nur eine Überladung |
| **B — `extern` nie direkt benutzen, immer hinter einer Lyric-Funktion**, und die Hülle testen | Hexagonale Architektur; Go | Konvention statt Mechanismus; die Hülle selbst bleibt ungetestet |
| **C — der Testläufer verweigert `hostAccess`** und ein Test, der es braucht, markiert sich | `#[ignore]`/`@Tag("integration")` | trennt Einheit von Integration, ersetzt aber nichts |
| **D — so lassen** | — | jede `extern`-gestützte Funktion ist per Konstruktion ungetestet |

**Empfehlung: A, und C als Ergänzung.** B ist gute Praxis und keine Antwort.
**Warum es F14 direkt berührt:** wenn die Standardbibliothek `extern` benutzen dürfte (F14-C),
wäre ein Stdlib-Modul per Konstruktion untestbar — das ist ein **zusätzliches** Argument gegen
F14-C, unabhängig von der Kapazitätsfrage.
**Bruch:** nein (`lyrtest` bekommt eine Fähigkeit dazu).
**4.x:** A hängt an F26; C geht sofort.
**Hängt an:** F26, F14, Test-Gebiet.

### F29 — Gehört die Zielplattform in den Modulkopf? (neu)

**Heute:** nein, und es fällt erst auf der Zielmaschine auf.
`Microsoft.Win32.Registry`, `System.Management` oder eine assembly-qualifizierte NuGet-Referenz
existieren nicht überall; ein `.lyrbc` mit solchen `dotnet:`-Imports ist an ein Betriebssystem
gebunden. Gemessen: ein nicht auflösbarer Typ gibt `LYR-VM0005` **beim Laden** (`q18`), nicht
beim Bauen (`lyrc check` → `ok`). Und `lyrpack` erzeugt Stubs für **drei RIDs**
(`Lyrstub.csproj:13`: `win-x64;linux-x64;osx-arm64`, gelesen) — ein Paket kann also für eine
Plattform gebaut werden, auf der sein `extern` nie bindet.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Plattformbedingung in den Header**, wie F10 es für die ABI vorschlägt: „dieses Modul braucht `win-x64`“ | .NET RIDs; Cargo `[target.'cfg(windows)']` | der Header wächst; eine zweite Runtime muss die RID-Namen kennen |
| **B — bedingte Deklaration in der Sprache** (`@Platform("windows") extern …`) | Kotlin `expect`/`actual`; C# `[SupportedOSPlatform]` | eine Bedingung im Typsystem — und auf `extern` dürfen heute keine Attribute stehen (F25) |
| **C — Plattform ins Projekt** (`lyric.json` nennt die Zielplattformen des Pakets) | Cargo, npm `os` | dieselbe Naht wie F27; keine Sprachänderung |
| **D — so lassen: Ladefehler tief drin** | — | ein Benutzer erfährt es, wenn das Programm startet, nicht wenn es gebaut wird |

**Empfehlung: A für das Modul, C für das Paket — ein Spec-PR mit F10.** B ablehnen: eine
Plattformbedingung in der Sprache ist der Anfang eines Präprozessors.
**Bruch:** nein (Header wächst additiv, wie die Kapazitätsbits).
**4.x:** nichts nötig; C kann vorher als Warnung kommen.
**Hängt an:** F10, F19, F27, F30, Bytecode-Gebiet.

### F30 — Ist ein Bau reproduzierbar, dessen Korrektheit von der Maschine abhängt, auf der er LÄUFT? (neu)

**Heute:** der Compiler prüft ein `extern`-Symbol **nie**. Gemessen: `lyrc check
q18_used_bogus.lyr` → `ok`, obwohl `No.Such.Type::Nope` nicht existiert; `lyrc build` schreibt
2 805 Bytes; erst `lyrvm verify`/`run` gibt `LYR-VM0005`. `lyrc --help` kennt keinen
Prüfschalter (gemessen).

| Option | Vorbild | Preis |
|---|---|---|
| **A — optionales `lyrc --check-externs`** gegen benannte Assemblies; CI-Gate | C#: der Compiler prüft `DllImport` **nicht**, aber `LibraryImport` schon (Source-Generator zur Bauzeit) | der Compiler muss Assemblies laden — er ist heute frei davon, und ein Compiler, der fremde DLLs lädt, ist ein Compiler mit fremdem Code darin |
| **B — Pflichtprüfung** | Swift/Zig (der Header ist beim Bauen da) | ein Bau schlägt fehl, weil die **Baumaschine** eine Assembly nicht hat — schlimmer als heute |
| **C — `lyrbind` erzeugt die Deklaration und beglaubigt sie** (Hash der Assembly im Kopfkommentar) | bindgen mit `--allowlist`; `cargo` `links` | verlagert die Prüfung ins Werkzeug (F12) und lässt den Compiler frei |
| **D — so lassen** | C# `DllImport` | „`lyrc build: ok`“ sagt über die `extern`-Hälfte eines Programms nichts aus |

**Empfehlung: C als Hauptweg, A als CI-Gate, B ablehnen.** Die Frage, die dahintersteht und
beantwortet gehört: **was ist die Aussage von `lyrc build: ok`?** Heute lautet sie „die Lyric-
Hälfte stimmt“. Das ist vertretbar — aber es steht nirgends, und ein Benutzer liest „ok“.
Mindestens gehört das in Guide 14 und in die Hilfe.
**Bruch:** nein (alles additiv). **4.x:** A und C jederzeit.
**Hängt an:** F12, F13, F29, Build-/CLI-Gebiet.

### F31 — Was gilt, wenn die Symbolzeichenkette leer bleibt? (neu)

**Heute:** die Grammatik macht sie **optional** — `ExternDecl = 'extern' STRING 'fn' IDENTIFIER
'(' [ParamList] ')' [':' TypeExpr] [ '=' STRING ] ';'` (`spec/02-grammar.md:208-209`, gelesen) —
und `NameMangling.ForExtern` hat den Fallback `$"{spec.Abi}:{spec.Symbol ?? functionName}"`
(`NameMangling.cs:30-31`, gelesen). Der Checker lehnt die Form für `"dotnet"` aber **immer** ab
(`TypeChecker.cs:453-457`; gemessen `q35`: `LYR-SEM0099`).

Es gibt also einen Grammatikzweig, den kein heutiger Compiler je erreicht.

| Option | Vorbild | Preis |
|---|---|---|
| **A — reserviert für ABIs, bei denen der Lyric-Name DAS Symbol ist** (`extern "C" fn strlen(…)`) | **Rust** (`extern "C" { fn strlen(…); }` ohne `#[link_name]`); C# `DllImport` ohne `EntryPoint` | die nützlichste Lesart, und sie erklärt, warum die Grammatik so aussieht — aber sie steht nirgends |
| **B — aus der Grammatik streichen**, bis eine ABI sie braucht | „was nicht in Grammar.md steht, existiert nicht“ | ein Grammatik-**Bruch** (Major), und man nimmt sich die natürliche Form für `extern "C"` |
| **C — sofort für `"dotnet"` erlauben**: der Lyric-Name ist der Methodenname, der Typ kommt woanders her | — | es gibt kein „woanders“ — ein .NET-Symbol braucht den Typ (`DotnetBinding.cs:41-47`) |

**Empfehlung: A, und es aufschreiben — bevor `extern "C"` kommt.** Ein optionaler
Grammatikzweig ohne Bedeutung ist genau die Sorte Unschärfe, die eine Spec beseitigen soll. Der
Satz gehört in `spec/02-grammar.md` neben die Regel: „Ob das Symbol weggelassen werden darf,
entscheidet die ABI; `"dotnet"` verlangt es.“
**Bruch:** A nein (Normtext über bestehendes Verhalten). B wäre **major**.
**4.x:** der Spec-Satz kann sofort, zusammen mit B2.
**Hängt an:** F11, F19, Spec-Gebiet.

### F32 — Wie kommt die Host-Seite eines Fehlers zum Benutzer? (neu)

**Heute:** gar nicht. Gemessen (`q26`):
```
panic [LYR-VM0016]: host call 'System.Int32::Parse' threw FormatException:
                    The input string 'nichtzahl' was not in a correct format.
    in main.indirect (q26_hosttrace.lyr:3)
    in main.main (q26_hosttrace.lyr:6)
```
Typname und Message kommen durch, der **.NET-Stack ist weg**, und `--verbose` ändert daran
nichts (gemessen). Bei `FailFast` ist es umgekehrt: der CLR-Stack kommt, der LYR-Code fehlt
(`q14`). Bei B15 gibt es **nur** den CLR-Stack.

| Option | Vorbild | Preis |
|---|---|---|
| **A — `HostError` trägt den Host-Stack als Feld**, sichtbar erst, wenn jemand ihn liest | Java (`getCause().getStackTrace()`); .NET `InnerException` | braucht F5; und ein Stacktrace als Wert in einer GC-Sprache hält Frames am Leben |
| **B — Schalter:** `--verbose` bzw. `LYRIC_HOST_TRACE=1` hängt den .NET-Stack an die Panik | Rusts `RUST_BACKTRACE` | eine Zeile in der Panik-Formatierung; kein Sprachzug, kein Typ |
| **C — nie zeigen:** die Host-Seite ist Implementierungsdetail | WIT (der Fehler ist `result<T,E>`, nichts dahinter) | eine `FileNotFoundException` aus drei Ebenen .NET ist ohne Stack nicht diagnostizierbar |
| **D — immer zeigen** | — | jede Panik wird fünfzig Zeilen lang |

**Empfehlung: B jetzt, A mit F5.** B ist billig, ändert keine Signatur und löst den häufigsten
Fall (ein Entwickler sucht, welche .NET-Ebene geworfen hat). A ist die Endform, sobald ein
Host-Fehler ein Wert ist.
**Bruch:** nein (Zusatzausgabe hinter einem Schalter).
**4.x:** B sofort.
**Hängt an:** F5, F20, F21, Diagnostik-Gebiet.

### F33 — Braucht `hostAccess` selbst eine Bestätigung? (neu)

**Heute:** nein, und der Grant steht nicht einmal in der Hilfe (B9). Gemessen sind drei
Wirkungen, die ein Host beim Wort „host access through reflection“ (`Capabilities.cs:23`) nicht
erwartet:
- Dateizugriff ohne `fileAccess` (`q01` vs. Kontrolle `q02`),
- Umgebungsvariablen ohne `osAccess` (`q01` vs. Kontrolle `q16`),
- **Prozessende** ohne jede andere Kapazität (`q13`: exit 7, kein `defer`; `q14`: CLR-Abbruch).

Damit ist `hostAccess` ein Grant, den ein Host, der eine Sandbox will, praktisch **nie** geben
kann — und `design/abi.md:37` behauptet das Gegenteil: „`hostAccess` bleibt abbruchfrei, weil
.NET-Exceptions gefangen werden“. Gemessen ist das falsch: `Environment::Exit` wirft nichts,
es endet.

| Option | Vorbild | Preis |
|---|---|---|
| **A — nur dokumentieren** (F1-A) | — | ehrlich, ändert nichts |
| **B — zweite Bestätigung:** `lyrvm --grant host` verlangt zusätzlich `--allow-host-abort`; ein Host verlangt ein zweites Feld auf `HostOptions` | Deno `--allow-ffi` ist von `--allow-all` getrennt und heißt „unsafe“; Java `--enable-native-access` | zwei Schalter für eine Sache — aber sie sagen zwei verschiedene Dinge („reflection“ und „mein Prozess darf sterben“) |
| **C — Prozessende abfangen:** die VM installiert einen Handler / verbietet `Environment::Exit` per Symbol-Denylist | — | eine Denylist ist genauso unvollständig wie F1-C: `FailFast`, `Process::Kill`, `Marshal`… |
| **D — ein eigenes Bit für „darf den Prozess beenden“** | — | ein sechstes Bit für etwas, das kein Modul ausdrücken kann |

**Empfehlung: B, mit A als Sofortmaßnahme.** C und D sind Sicherheiten, die behaupten statt zu
tragen (dasselbe Argument wie bei F1-C). B ist ehrlich: der Grant heißt dann, was er ist.
Und `design/abi.md:37` muss korrigiert werden — „abbruchfrei“ ist gemessen falsch.
**Bruch:** B ist **minor** für die CLI (ein bestehender `--grant host`-Lauf braucht einen zweiten
Schalter) und nein für das Embedding (neues Feld, Default wie heute, bis 5.0).
**4.x-Warnstufe:** `lyrvm` warnt bei `--grant host` ohne den zweiten Schalter, bevor es in 5.0
ein Fehler wird. Das ist eine **CLI**-Uhr und braucht F25 nicht.
**Hängt an:** F1, F11 (`ffiAccess` darf den Fehler nicht wiederholen), F23, CLI-Gebiet.

### F34 — Wie verhält sich die Grenze zum Optimierer? (neu)

**Heute:** es gibt **keine Regel**, und die Sicherheit ist zufällig.
Gelesen: in `src/Lyric.Frontend/Ir/` kommt weder `IsPure` noch `HasSideEffect` vor (gemessen per
grep). Gemessen (`q28`, `--release`): zwei Aufrufe von
`System.Diagnostics.Stopwatch::GetTimestamp` um eine Rechenschleife herum liefern
**verschiedene** Werte (`gleich=false`) — also keine Deduplizierung. Aber das folgt aus dem
Fehlen einer Reinheitsanalyse, nicht aus einer Zusage.
Gleichzeitig **entfernt** der Optimierer `extern`-Imports (`Reachability.Prune`, gemessen `q17`),
und `PLAN.md:65-96` beschreibt, dass der Verifier lange nach dem Optimierer lief und das
Probleme machte.

| Option | Vorbild | Preis |
|---|---|---|
| **A — Normtext: ein `extern`-Aufruf ist ein Seiteneffekt.** Nie umordnen, nie deduplizieren, nie eliminieren, auch wenn das Ergebnis ungenutzt ist | C `volatile`; Rust: ein `extern "C" fn` ist implizit nicht `const` | verschenkt Optimierung an Symbolen, die wirklich rein sind (`Math::Cbrt`) |
| **B — Reinheit deklarierbar:** `@Pure extern …` erlaubt CSE und Elimination | Rust `#[must_use]`/`const fn`; GCC `__attribute__((pure))` | ein compilergelesenes Attribut (Guide 15) — und auf `extern` dürfen heute keine stehen (F25); außerdem eine Zusage, die der Benutzer falsch geben kann |
| **C — eine kleine Allowlist im Compiler** (`System.Math::*` gilt als rein) | JIT-Intrinsics | dieselbe Unvollständigkeit wie F1-C |
| **D — so lassen** | — | die Regel ist implizit, und der nächste Optimierer-Slice kann sie ohne Absicht brechen |

**Empfehlung: A als Normtext, B nicht vor F25 und nicht ohne guten Grund.** Der Satz, der heute
fehlt: **„Ein Aufruf über eine Host-Grenze ist ein beobachtbarer Effekt. Der Optimierer darf ihn
nicht entfernen, nicht verdoppeln und nicht gegen einen anderen verschieben.“** Zwei Aufrufe von
`get_TickCount64` **müssen** zwei Aufrufe bleiben — heute bleiben sie es aus Versehen.
Und: das Pruning (B8, F13) ist die eine Ausnahme, die der Normtext dann ausdrücklich nennen muss
— sie entfernt eine **Deklaration**, keinen Aufruf.
**Bruch:** nein (Normtext über bestehendes Verhalten; A verbietet nur künftige Optimierungen).
**4.x:** der Satz kann sofort in §13/Guide.
**Hängt an:** F13, F15, Optimierer-Gebiet, Bytecode-Gebiet.

---

## 4. Was wir übernehmen sollten

| Von | Was | Wofür in Lyric 5 |
|---|---|---|
| **WIT / Canonical ABI** | die Marshalling-Tabelle **und die Fehlerregel** als Normtext, nicht als Binder-Code | §13 bekommt eine Tabelle Lyric-Typ ↔ Wire ↔ Host **plus** eine Regel, was bei einem Typ passiert, der nicht kreuzt (F2, B2) |
| **WIT `resource`** | owned/borrowed **Handles als Indizes**, Identität über den Index | F22 — die offene Frage des Gebiets; `opaque type` + `TypeTag.Host` ist die halbe Antwort |
| **WIT `string`** | ein String an der Grenze ist **gültiges Unicode**, Kopie inklusive | F21 — der gemessene Absturz (B15) ist genau das, was WIT ausschließt |
| **Java FFM `Arena`** | Lebensdauer als **Wert**, Zugriff bounds-geprüft, `defer arena.close()` | das Speichermodell für `extern "C"` **und** für Host-Objekte (F11, F22) |
| **Java FFM `upcallStub`** | ein Callback **attacht** den fremden Thread, statt ihn zu ignorieren | F16 — das Vorbild, das die erste Fassung mit `Arena.ofShared` verwechselt hat |
| **C# `LibraryImport` / ILC** | Marshalling **vor** dem Lauf erzeugen, nicht in ihm | F13-C/D; und der Grund ist Reflection, nicht P/Invoke |
| **jextract / bindgen / Zigs `translate-c`** | Generator als **Werkzeug mit Filter**, nicht im Compiler | `lyrbind` (F12) — Zig ist hier Vorbild, seit es den Schritt herausgelöst hat |
| **Swift** | eine Deklarationsform, die der **Compiler versteht**, statt eines Strings; und Fehlerkonventionen werden **werfend importiert** | F7 (`extern "dotnet" type X = "…"`), F5 |
| **Rust** | `unsafe` als **Ort**, nicht als Optimierung — und `extern` ohne `#[link_name]` heißt „der Name IST das Symbol“ | F11, F31 |
| **Deno / Panama `--enable-native-access`** | der Grant nennt **was**, nicht nur **ob**; und der gefährliche Grant heißt so | F1-B, F33 |
| **Lua (`LUAI_MAXCCALLS`)** | die Reentranztiefe wird **mitgezählt** | F24 — das Loch, das F3-B und F9-B verbreitern würden |
| **Deno `deno.json`** | Berechtigungen stehen im **Projekt**, nicht nur im Lauf | F27 — `lyric.json` hat heute keinen Kapazitätsschlüssel |
| **Go cgo (als Warnung)** | ein blockierender Grenzübergang ist ein Laufzeit-Ereignis | aber **nicht** „das M wird gepinnt“: für Lyric zählt, dass es **single-threaded** ist und gar kein zweites M hat (F16, F23) |
| **Lua (als Warnung)** | eine untypisierte Grenze wird nie besser | `RegisterFunction` mit `DynamicInvoke` ist heute Lyrics Lua-Ecke; sie sollte die Tabelle **und die Fehlerregel** aus F2 lesen |

### Die billigsten echten Verbesserungen, in dieser Reihenfolge

Die Liste der ersten Fassung hielt an zwei von drei Punkten nicht. Neu, nachgemessen:

1. **F25** (`@Deprecated` auf `extern` erlauben) — Parser plus eine `PAR0042`-Bedingung. **Ohne
   sie hat dieses ganze Gebiet keinen Migrationspfad**, und jede Warnstufe unten fällt auf
   „reine `lyrc`-Warnung, in 5.0 bricht es“ zurück. Deshalb Platz 1, obwohl es keine FFI-Frage
   ist.
2. **F4 / F3-A′** (opaker Alias an der `extern`-Grenze) — eine Bedingung in `CrossesHostBoundary`
   (`TypeChecker.cs:487`). Der Wire-Typ ist das Underlying, also **kein neuer Binderfall**; das
   Lowering ist schon dafür geschrieben (`ModuleLowerer.cs:158`, `:641-644`, gelesen).
3. **F20 + F21** (eingehende Richtung prüfen) — zwei Bedingungen in `DotnetBinding.ToLyric`.
   Sie schließen einen gemessenen stillen Invariantenbruch (B14) und einen gemessenen
   **Prozessabsturz ohne Diagnose** (B15).
4. **F1-A + B9** (`hostAccess` als das benennen, was es ist; `host` in die Hilfe) — Doku und ein
   Hilfetext. **Nicht** die Variante der ersten Fassung („`--grant host` impliziert alle Bits“):
   die ist Logik (`Capabilities.cs:128`) und macht die Lage schlechter.
5. **F34** (ein Satz Normtext über Optimierer und Grenze) — kein Code, aber er sichert ein
   Verhalten, das heute nur aus Versehen stimmt.

Was **nicht** mehr auf dieser Liste steht: **F3-A** in voller Breite. Die Maschinerie läuft
nicht „nur nicht an dieser Tür“ — sie existiert auf der Binderseite gar nicht (B3).

---

## 5. Konflikte

### Mit CONTRIBUTING Rule 2 (ein Mechanismus pro Konzept)

- **Vier Host-Grenzen existieren bereits** (1.1) — der Verstoß ist nicht ein Vorschlag, er ist
  der Ist-Stand. Und es sind nicht nur vier Typtabellen, sondern **vier Fehlerpolitiken** (F2).
  F2 repariert das, statt einen fünften hinzuzufügen.
- **Die Host-Überschreibung ist eine fünfte Semantik** (F26), sie steht nur im Guide, und sie
  ist zugleich die **einzige Naht**, an der ein Host ein Symbol abfangen kann. Sie gehört
  spezifiziert, nicht abgeschafft.
- **F1-B (Symbol-Allowlist)** sieht aus wie ein zweiter Kapazitätsmechanismus. Die Abgrenzung,
  die ich für tragfähig halte: die **Bits** bleiben das Modell im Modul, die Allowlist ist eine
  **Ladeentscheidung des Hosts** — dieselbe Sorte wie `NativeRoots` heute. Wer das nicht kauft,
  nimmt F1-A und lebt mit einem dokumentierten Bypass.
- **F5-D (`throws` optional am `extern`)** ist zwei Verhalten für eine Form. Migrationsstufe,
  nicht Design — **aber die Uhr, die das sagen müsste, ist heute nicht schreibbar** (F25,
  gemessen).
- **F6-C (`using` für Host-Ressourcen) wäre ein echter zweiter Cleanup-Mechanismus.** Ablehnen.
- **F11-B (Arena) und F22-C (Skopus für Host-Objekte) sind DASSELBE Modell** und müssen als
  eines entschieden werden, sonst sind es zwei.
- **F34-B (`@Pure`) wäre ein zweites compilergelesenes Attribut.** Nur mit sehr gutem Grund.

### Mit dem Charakterprofil

- **GC als einziger Speichermechanismus.** `extern "C"` bringt Speicher, den der GC nicht kennt.
  F11-B (Arena) verletzt das nicht — der Speicher liegt außerhalb der Sprache, und sein Besitzer
  ist ein gewöhnlicher Lyric-Wert mit `defer`. F11-A (roher `CPtr`) dagegen **verletzt** es.
  **Und zwar mit genau der Begründung, die `STATUS.md:2469` gegen `unsafe` führt** —
  „`Capability.None` means nothing if a script may read past an array“ — die auf einen rohen
  Zeiger noch stärker zutrifft als auf einen Bounds-Check. Die erste Fassung erklärte diese
  Begründung pauschal für „eine andere Frage“; das gilt für B und **nicht** für A (F11).
- **„Never a process abort" — heute schon gebrochen, an zwei Stellen, ohne `ffiAccess`:**
  1. `extern "dotnet" fn hardExit(code: int32) = "System.Environment::Exit"` unter `--grant host`
     → Exit 7, kein `defer`, keine Diagnose (gemessen, `q13`; Kontrolle `q15`: gewöhnliche
     `panic` → `LYR-VM0011`, Exit 101; Kontrolle `q16`: `std.os.exit` braucht `osAccess` und
     wird unter `--grant host` verweigert). `FailFast` liefert einen CLR-Stacktrace, Exit 35
     (`q14`).
  2. Reentranz Skript → Host → Skript umgeht `MaxCallDepth` → CLR-StackOverflow
     (`PLAN.md:112`, gelesen; Stelle `ScriptInstance.cs:226`).
  3. Und, neu gemessen, ein **dritter** Weg ohne jede Absicht: ein vergifteter String aus einem
     `extern` lässt ein **Stdlib-Native** mit einer unbehandelten CLR-Exception abstürzen,
     Exit 127 (B15, `q22`; Kontrolle `q23`).
  `design/abi.md:37` behauptet „`hostAccess` bleibt abbruchfrei, weil .NET-Exceptions gefangen
  werden“ — **gemessen falsch**, und es gehört korrigiert, bevor jemand einen Host darauf baut.
- **Kein `finally`.** B5 ist genau die Stelle, an der das weh tut. F5 löst den Exception-Fall;
  `Exit`/`FailFast` und der Budget-Stopp bleiben, und das ist bei letzterem Absicht
  (`ExecutionBudget.cs:58-61`).
- **Deterministische Numerik.** F20/F21 sind dieselbe Zusage für Text: ein `char` ist ein
  Unicode-Skalar (`spec/03-types.md:19`), ein `string` eine Folge davon (`:20`). Die
  `extern`-Grenze hält beides heute nicht (gemessen).
- **`unsafe` ist als No dokumentiert** (`STATUS.md:2466-2470`) — für Bounds-Checks, mit Messung.
  F11-**B** ist eine andere Frage. F11-**A** ist es nicht.

### Mit anderen Gebieten

| Gebiet | Konflikt |
|---|---|
| **Standardbibliothek** | Die offene „Bibliotheks-Umkehr“ (`STATUS.md:2167-2174`, `design/stdlib-2.md:422-425`) rechnet mit `extern "dotnet"` als Ausweg. F7 zeigt: der Ausweg trägt heute nicht (keine Instanzen, gemessen). F1 zeigt: er wäre eine Sandbox-**Umgehung**, nicht nur eine Kapitulation. F28 zeigt: er wäre untestbar. **F1, F7 und F28 müssen vor der Bibliotheksentscheidung beantwortet sein.** |
| **Fehler / typed throws** | F5 hängt vollständig an PLAN 4.7 #5–7. Vorher ist `HostError` nicht formulierbar — und F20/F21/F32 hängen an F5. |
| **Metaprogrammierung / Attribute** | **F25 ist der Flaschenhals des ganzen Gebiets**: ohne Attribute auf `extern` gibt es keine Uhr an einer Deklaration, und der Auftrag lautet ausdrücklich „Warnings und deprecation“. |
| **Lowering / IR** | F8 ist größer als gedacht: die Grenze ist das **Modul**, nicht der Import (gemessen). `PLAN.md:59` und `:271-273` wollen die „restlichen `IR0001`-Grenzen“ unter **C**; Abschnitt C listet keinen solchen Posten und ist für leer erklärt (`:60-62`); das einzige Datum für Funktionswerte ist **4.7** (`:143`). |
| **Koroutinen / Scheduler** | F16 (Callbacks aus fremden Threads) ist **beantwortet und widersprüchlich**: `design/abi.md:78` sagt „verboten“, die Designrunde 2026-09 sagt „Queue“. F23 (Budget) und F24 (Reentranz) gehören dazu. Kein Worker-Isolate-Vorschlag: die Queue liefe auf dem VM-Thread. |
| **Bytecode / Format** | F2, F10, F18, F21, F29, F31 und F34 wollen alle in §13/§4.5. Additiv, Format bleibt 4.0 — aber es ist **ein** Spec-PR, nicht sieben. F13 („alle binden“) ist der eine Punkt, der die **emittierten Bytes** ändert, und der gehört getrennt verhandelt. |
| **Build / Pakete** | F27 (Kapazitätsmanifest), F29 (Plattform), F30 (`--check-externs`) sind alle Projektsystem-Fragen, die 4.5 aufgeworfen und nicht beantwortet hat. `lyric.json` hat heute keinen Kapazitätsschlüssel (gelesen). |
| **Testen** | F28: `lyrtest` reicht die einzige Registrierungsnaht nicht durch (gelesen). Jede `extern`-gestützte Funktion ist heute per Konstruktion ungetestet. |
| **Diagnostik** | F32 (Host-Stack), F17 (unbindbare `params`-Kandidaten in der Fehlerliste), B15 (ein Absturz **ganz ohne** LYR-Code). |
| **Spec-Prozess** | B2 ist ein spec-first-Verstoß, der bereits **ausgeliefert** ist: 4.5 hat `extern` gebaut, ohne §4.5 und §13 nachzuziehen — `extern` steht in der normativen Spec **nur** in `02-grammar.md` und `appendix-a-diagnostics.md` (gemessen per grep). Das sollte vor 5.0 repariert werden, unabhängig davon, wie die Fragen ausgehen. |

---

## 6. Nach der Kritik geändert

**Falsche Aussagen korrigiert (jede neu gemessen oder nachgelesen):**

- **B1/F1**: `hostAccess` ist ein **Bypass**, keine „Vereinigung aller Bits“ — gemessen mit zwei
  Kontrollen (`q01`/`q02`/`q16`). Die erste Fassung hatte die widerlegende Kontrolle selbst im
  Text. Die Empfehlung ist **umgekehrt**: nicht „`--grant host` impliziert alle Bits“ (das ist
  Logik in `Capabilities.cs:128` und macht es schlechter), sondern dokumentieren + B9.
- **B1**: „und niemand sagt das“ gestrichen — `design/abi.md:68` sagt es; neu formuliert als
  „in keinem Dokument, das ein Benutzer oder Host sieht“.
- **B3/F3-A**: „die Maschinerie existiert schon, nur nicht an dieser Tür“ **zurückgenommen**.
  `DotnetBinding.ClrTypeOf/ToClr/ToLyric` haben für Optional, Array, Struct, Host **keinen
  Fall**; bei einem Native trägt der Host das Marshalling, bei `extern` gibt es keinen Host.
  Neue Option **A′** (nur opaker Alias) als die tatsächlich billige Teilmenge.
- **B7/F8**: mis-skopiert. Neu gemessen mit sieben Proben: die Grenze ist das **Modul** (jede
  importierte Funktion, auch eine mit Rumpf), **und zusätzlich** ist `extern` auch im eigenen
  Modul kein Wert — zwei verschiedene `IR0001`-Texte (`q03`–`q10`). Die Kritik war hier
  ihrerseits zu grob: es ist beides.
- **B10/F15**: die Uhr (`get_TickCount64`) hat **15–16 ms** Auflösung (gemessen, `q11`) und
  konnte 65 ns nicht tragen. Neu gemessen mit `Stopwatch::GetTimestamp` (10 MHz, kalibriert),
  n = 5·10⁶, je drei Läufe, **interpretiert und `--jit`**. Ergebnis: unter JIT kostet der
  Grenzübergang 100–115 ns bei 2,5–6,8 ns je Schleifendurchlauf, der Reflection-Anteil ist
  **45–55 %**. Die Empfehlung ist **umgekehrt** (B statt „B, wenn das Profil es zeigt“). Neuer
  Befund: im Interpreter ist ein `extern`-Aufruf **billiger** als ein Lyric-Funktionsaufruf.
- **F13**: die Warnstufe gehört zu **`lyrc`**, nicht zu `lyrvm verify` — ein unerreichbares
  `extern` steht gar nicht im Modul (gemessen, `q17`: `imports 1`, `verify: ok`). „Alle binden“
  ändert die **emittierten Bytes**; „minor“ war zu milde.
- **§5 / F33**: „Mit `ffiAccess` kann fremder Code abstürzen“ — **heute schon**, mit Bit 3:
  `Environment::Exit` (gemessen, `q13`), `FailFast` (`q14`), Reentranz (`PLAN.md:112`), und neu
  auch B15. `design/abi.md:37` („abbruchfrei“) ist gemessen falsch.
- **B13**: `RejectTrimming` begründet sich mit dem **gemeinsamen Publish-Verzeichnis**, und
  `Lyrstub` steht nicht in `@(Binary)`. Folgerung hält, Beleg ausgetauscht (`Lyrstub.csproj`).
- **F4**: „die Spezifikation verspricht es“ **abgeschwächt** — der Satz sagt „in a **native**
  signature“, und `extern` ist seit 4.5 eine eigene Deklarationsform. Empfehlung unverändert A,
  Begründung jetzt „Design-Inkonsistenz“ statt „Bug“.
- **F16 / F19**: **nicht** „gar nicht gestellt“ — `design/abi.md:78` (Thread-Verbot) und
  `design/abi.md:73` (`NativeLibrary.Load`, `libz.so.1`) beantworten beide. Beide neu als
  **Konflikt** formuliert (Notiz gegen Designrunde bzw. Notiz gegen Vertrag).
- **§1.1**: „`extern` ist der engste“ präzisiert — engste **Typmenge**, weiteste **Macht**.
- **§5/F8**: PLAN-Lesart korrigiert — Absicht steht zweimal (`:59`, `:271-273`), Abschnitt C
  listet keinen Posten, C ist für leer erklärt (`:60-62`), einziges Datum ist 4.7 (`:143`).
- **§1.2**: die `p21`-Zeile hatte keinen Beleg (die Datei kompiliert nicht). Neu gemessen als
  `q06`; die Aussage stimmt.
- **Zeilenangaben korrigiert**: `VM0016` → `appendix-a-diagnostics.md:290` (nicht 286);
  `unsafe`-Nein → `STATUS.md:2466-2470`; Host-Objekt-Lebensdauer → `STATUS.md:2160-2162`;
  Bibliotheks-Umkehr → `STATUS.md:2167-2174` und `stdlib-2.md:422-425` (nicht `PLAN.md:284-300`);
  `std.dotnet` existiert nicht → `abi.md:15`; `Expression.Compile` → `abi.md:66`/`113`;
  „packing is not verification“ → `abi.md:70`; Stufe-1-Typmenge → `abi.md:64`; „abbruchfrei“ →
  `abi.md:37`.

**Vergleichssprachen korrigiert (sechs in §2, vier bei einzelnen Fragen):**

- C# `DllImport` ist **nicht** AOT-feindlich (ILC erzeugt die Stubs beim Kompilieren); F13-C baute
  darauf auf und ist neu begründet (Reflection, nicht P/Invoke).
- `SetLastError` fehlt ⇒ **kein Fehlerkanal**, nicht UB.
- Zig hat `translate-c` **aus dem Compiler herausgelöst** — damit ist Zig Vorbild für F12-A, nicht
  Gegenbild; und translate-c verwirft nichts still (`@compileError`-Stubs).
- Go cgo blockiert einen **OS-Thread**, nicht den Scheduler (`sysmon` nimmt dem M das P ab); für
  Lyric ist der richtige Beleg „single-threaded, kein zweites M“.
- LuaJIT-FFI hat `ffi.errno()` — was fehlt, ist ein **typisierter** Kanal.
- F10-B: `System.loadLibrary` abstrahiert keine ABI → Panama `SymbolLookup`.
- F5-D: `try!` ist eine **Aufrufstellen**-Form → Swifts Objective-C-Import-Regel bzw. `rethrows`.
- F6-B: in Go laufen `defer`s beim Panic-Unwinding **immer**; `recover` ist unabhängig. Der Preis
  von B folgt aus Lyrics eigener Budget-Zusage (`ExecutionBudget.cs:58-61`), nicht aus Go.
- F16: `Arena.ofShared` regelt Segment-Lebensdauer, nicht Upcalls → Panamas `upcallStub` attacht
  den Thread.

**Fünfzehn Designfragen ergänzt** (F20–F34), jede mit belegtem Ist-Stand, Optionen mit Vorbild und
Preis, Empfehlung, Bruchgrad und Abhängigkeiten:
F20 `char` eingehend · F21 `string` auf dem Draht · F22 Lebensdauer/Identität eines Host-Objekts ·
F23 Budget und Abbruch · F24 Reentranz · F25 Uhr für ein `extern` · F26 Host-Überschreibung einer
Überladung · F27 Kapazitätsmanifest im Projekt · F28 Testbarkeit · F29 Zielplattform ·
F30 Reproduzierbarkeit / `--check-externs` · F31 leere Symbolzeichenkette · F32 Host-Stacktrace ·
F33 Bestätigung für `hostAccess` · F34 Optimierer und Grenze.
F16.1–F16.4 der ersten Fassung sind zu eigenständigen Fragen geworden (F16, F17, F18, F19), weil
zwei davon Konflikte statt Lücken sind.

**Ein Befund, den weder die erste Fassung noch die Kritik hatte:**
**B15** — ein `extern` kann einen Lyric-`string` mit einer unpaarigen Surrogathälfte erzeugen;
`length()` sagt 1, `charAt(0)` stürzt mit einer **unbehandelten CLR-`ArgumentException` ab,
Exit 127, ohne LYR-Code, ohne Position** (`NativeRegistry.cs:1248`). Kontrolle mit `'A'`: exit 0.
Das ist ein dritter, unbeabsichtigter Weg zum Prozessabbruch und die Begründung für F21.

**Die Liste der billigsten Verbesserungen ist neu**, weil zwei der drei alten Punkte nicht
hielten. Neu an Platz 1: **F25**, weil ohne eine Uhr an der `extern`-Deklaration der Auftrag
(„Warnings und deprecation“) für dieses ganze Gebiet nicht erfüllbar ist.
