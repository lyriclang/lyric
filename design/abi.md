# C- und .NET-ABI / FFI für Lyric — Analyse und Design

Stand: 2026-09-22, Basis dc32100c (v4.4.1), Autor: macro-abi (Evolution-Team 3).
Prototyp Stufe 1: `extern "dotnet"` auf Branch `worktree-agent-ab3c14434f8eda027`, Beispiel `examples/ffi/dotnet.lyr`.

## 1. Ist-Stand: wie kommt fremder Code heute nach Lyric?

Nur in **einer Richtung** — der Host bietet dem Skript etwas an. Aus einem eigenständigen Programm (`lyric run`, lyrpack) führt kein Weg zu libc oder .NET außer `std.process` (Kindprozess starten, Text parsen).

| Weg | Ort | Mechanik | Grenzen |
|---|---|---|---|
| Stdlib-Natives | `stdlib/std/*.lyr` (bodylose `pub fn`), `src/Lyric.Vm/NativeRegistry.cs` (2 700 Zeilen, fest verdrahtet) | Import-Zeile `<modul>.<fn>` + Signatur im Bytecode (§13 Imports), Bindung im Loader (`NativeRegistry.Bind`), Capability-Bound pro Import (`CapabilityTable.RequiredForImport`) | Nur die Runtime-Autoren können Natives hinzufügen |
| Native Roots | Guide 14, `HostOptions.NativeRoots`, `Compilation.IsNative` | SDK liefert `.lyr`-Deklarationen, Host `RegisterNative(name, delegate)`; Structs werden geflattet (`ImportTable.ImportShape`), Struct-Rückgabe über Out-Buffer, 0 Allokationen | Nur im Embedding; „the root decides, not the file“ (Skript kann sich keine Natives erklären) |
| `RegisterFunction`/`RegisterType<T>` | `Lyric.Embedding/LangVm.cs`, `HostFunction.cs`, `Marshal.cs` | Signatur aus Delegate → synthetisches `host`-Modul; Host-Objekte als opaker Typ (Host-Tag 0x47 §13), Methoden als Natives mit Receiver = Parameter 0; `DynamicInvoke` | Marshalling nur Skalare/string/Host-Typ; Bug-Hunt: `FromLyric` liefert für Objekt/Array/Optional still 0, `Convert.ChangeType` verlustbehaftet, Host-Objekt ohne Typprüfung, Reentranz umgeht `MaxCallDepth` |
| Capability-Bit 3 | `Lyric.Core/Capabilities.cs:23` `HostAccess` „std.dotnet — host access through reflection“; Spec §4.5/§13 „reserved“ | Seit 1.0 reserviert, nie belegt | `std.dotnet` existiert nicht |
| lyrpack/lyrstub | `docs/Pack.md` | Byte-Kopie Stub + Modul + Footer; „not linking“ | Keine Bündelung fremder Bibliotheken vorgesehen; Footer hat ein reserviertes Feld |

**Schlussfolgerung:** Die Architektur ist für eine ABI vorbereitet — Import-Zeilen sind symbolisch („forward-open“, §13), Bindung passiert im Loader, das Bit existiert. Was fehlt, ist die **Deklarationsform im Programm** und ein **Binder**, der nicht vom Host von Hand befüllt wird.

## 2. Nützlichkeit: welche Anwendungsfälle brauchen eine ABI?

| Anwendungsfall | Heute möglich? | Umständlichkeit heute | Beispiel |
|---|---|---|---|
| **Systembibliotheken** (libc `getpid`/`strlen`, sqlite, zlib, curl) | Nein (eigenständig); im Embedding nur, wenn der Host es vorher registriert | Umweg `std.process`: `sqlite3 db.sqlite "select …"` starten, stdout parsen — kein Typ, kein Fehlercode, pro Aufruf ein Prozess | zlib: heute unmöglich, `std.encoding` müsste es nativ nachbauen |
| **GUI** (GTK, Win32, Avalonia) | Nein | — | Ein GUI-Toolkit über P/Invoke ist ohne Callbacks (Stufe 3) und Handles (Host-Objekte) nicht nutzbar |
| **.NET-Ökosystem** (HttpClient, System.Text.Json, EF, NuGet) | Nein aus Lyric; ja über einen C#-Host mit `RegisterFunction` | Für jede Methode eine Delegate-Registrierung in C#; ohne Instanzen (nur statische Funktionen und opake Handles mit Methoden) | Nach Stufe 1: `extern "dotnet" fn tempPath(): string = "System.IO.Path::GetTempPath";` — eine Zeile, kein Host |
| **Lyric als Skriptsprache in .NET-Hosts** | **Ja** (Guide 14, ADR-001; 196 Programme paritätisch laut Bug-Hunt) | Angemessen; Marshalling-Bugs (Marshal.cs) sind Fehler, kein Mangel des Modells | Engine-Mods |
| **Lyric-Bibliotheken aus C/.NET aufrufen** (Export-Richtung) | Ja per Embedding (`instance.Call<long>("f", …)`), nein als Delegate/`UnmanagedCallersOnly` | Der C#-Aufrufer schreibt Marshalling selbst; kein Typ auf der C#-Seite (`object?[]`) | Stufe 3 |
| **Performance-Hotspots** (Matrix, Hash, Kompression) | Nein | JIT (`Compile = true`) deckt Arithmetik, nicht Bibliotheken | `extern "dotnet"` auf `System.Numerics`/`System.IO.Hashing` deckt viel ohne C |

**Gesamtbewertung:** Eine ABI **lohnt sich**, und zwar zuerst die .NET-Seite: sie kostet keinen neuen Opcode, kein Format, keine Marshalling-Maschinerie (die VM ist .NET) und öffnet sofort das ganze BCL/NuGet-Angebot für eigenständige Programme (heute die größte Lücke der stdlib: Hash, Kompression, HTTP-Client, TLS, Regex). Die C-ABI folgt als P/Invoke-Spezialfall auf derselben Deklarationsform.

## 3. Sprachcharakter: Capabilities, deterministische Numerik, keine Nullreferenzen, Wertsemantik, „never a process abort“

Eine C-ABI durchbricht alle vier; eine .NET-ABI nur zwei (Nullreferenzen, Abstürze durch Exceptions). Die Vereinbarung:

1. **Capabilities bleiben die Grenze.** `extern "dotnet"` ⇒ `hostAccess` (Bit 3, existiert), `extern "C"` ⇒ neues Bit 5 `ffiAccess` (Name mit stdlib-redesign abgestimmt, camelCase wie `fileAccess`). Ein Host, der nichts gewährt, lädt ein solches Modul nicht (`LYR-CAP0001`, geprüft). Dokumentierte Ausnahme vom Sandbox-Versprechen: **mit `ffiAccess` sind Abstürze in fremdem Code erlaubt** — der Grant ist die Unterschrift dafür; `hostAccess` bleibt abbruchfrei, weil .NET-Exceptions gefangen werden.
2. **Keine Nullreferenzen:** `string` null → `""` (umgesetzt), Referenztypen kommen nur als `?T` (Optional ↔ null) oder als opaker Host-Typ, der nie null ist (Marshal wirft heute bereits bei null-Host-Objekt).
3. **Deterministische Numerik:** Marshalling ist breitenexakt (`int`=Int64, `int32`=Int32, …), nie `Convert.ChangeType`; `float32` ↔ `Single` ohne Erweiterung; keine implizite Konvertierung — eine falsche Breite ist ein Ladefehler mit Kandidatenliste, keine Anpassung.
4. **Wertsemantik-Split:** Structs kreuzen **per Kopie** (geflattet, wie Native Roots heute), Klassen nur als Handle (Host-Objekt), Arrays per Kopie (`uint8[]` ist laut stdlib-redesign „der Bytepuffer der stdlib“ und ein .NET-Array im Host — `cbuf` = Kopie an der Grenze, Pinning nur innerhalb eines Aufrufs).
5. **Kein rohes Pointer-Modell:** `CPtr<T>` ist ein opaker Wert ohne Arithmetik; Dereferenzieren nur über `std.ffi`-Funktionen (`read`, `write`, `slice`) innerhalb eines `unsafe`-Blocks; Ownership als Typparameter/Attribut (`owned`/`borrowed`) mit `defer free(p)` als Muster — kein Borrow-Checker (CONTRIBUTING Regel 2: GC bleibt der einzige Speichermechanismus; FFI-Speicher ist „host-owned“ und liegt außerhalb der Sprache).

### Vergleichstabelle FFI (1 = schlecht … 5 = sehr gut)

| System | Sicherheit | Ergonomie | Tooling (Bindings-Generator) | Portabilität | Laufzeitkosten | GC/Coroutinen-Interaktion | Lehre für Lyric |
|---|---|---|---|---|---|---|---|
| C# P/Invoke `DllImport` (Reflection-Marshalling) | 2 (Marshalling-Fehler = Speicherfehler) | 4 | 3 (ClangSharp) | 4 (`NativeLibrary`, RID-Suche) | 3 (Stub pro Aufruf) | 4 (GC-Pinning explizit `fixed`) | Deklaration + Symbolname + Signatur = genau das Modell für `extern` |
| C# `LibraryImport` (Source-Generator, .NET 7+) | 3 | 4 | 3 | 4 | 5 (kein IL-Stub zur Laufzeit, AOT-fähig) | 4 | Für NativeAOT-Stubs (lyrstub) Pflicht: Reflection-Marshalling entfällt |
| C# function pointers / `UnmanagedCallersOnly` | 2 | 3 | — | 4 | 5 | 3 (kein Übergang in GC-Code aus fremden Threads ohne Vorsicht) | Export-Richtung (Stufe 3) |
| Rust `extern "C"` + bindgen + `unsafe` | 4 (unsafe-Block sichtbar, Ownership im Typ) | 3 | 5 (bindgen) | 5 | 5 | n/a | `unsafe`-Block als **einzige** Stelle mit Pointer-Zugriff; Ownership im Typ |
| Zig `@cImport`/translate-c | 3 | 5 (Header direkt importieren) | 5 (eingebaut) | 5 | 5 | n/a | Attraktiv, aber setzt einen C-Parser im Compiler voraus — für Lyric zu groß |
| Go cgo | 3 | 2 | 3 | 3 | 1 (Stack-Wechsel ~100 ns, Goroutinen blockieren M) | 2 | Warnung: Coroutinen + fremde Threads sind teuer; Lyric ist single-threaded — ein Native-Call blockiert die ganze VM (heute schon so) |
| Swift C-Interop (Module Maps), C++-Interop | 4 | 5 | 5 (Importer im Compiler) | 3 | 5 | 4 (ARC-Bridging) | Typisierte Importer, aber Compiler-Komplexität |
| Kotlin/Native cinterop | 3 | 4 | 4 | 3 | 4 | 3 | Def-Dateien = generierte Deklarationen (Stufe 4 `lyrbind`) |
| Python ctypes / cffi | 2 | 4 | 3 (cffi API-Modus) | 4 | 2 | 3 | ctypes-Dynamik = das Gegenteil von spec-first; nicht folgen |
| Java FFM (Panama) + jextract | 4 (MemorySegment mit Bounds + Lifetime/Arena) | 3 | 5 (jextract) | 4 | 4 | 4 | **Arena/Lifetime-Modell** für `CBuf`/`CPtr`: bounds-checked Segmente statt roher Zeiger |
| Lua C-API / LuaJIT FFI | 2 | 3 / 5 | 2 | 4 | 3 / 5 | 3 | LuaJIT `ffi.cdef` = Deklaration als String im Programm — Lyrics `extern`-Form ist die typisierte Fassung |
| WebAssembly Component Model / WIT | 5 (typisierte ABI-Beschreibung, keine Zeiger) | 4 | 5 (wit-bindgen) | 5 | 3 (Kopien) | 5 | **Marshalling-Regeln als Tabelle** (WIT-Canonical-ABI) — Spec-Vorbild für §13-Erweiterung |

## 4. Design: ein ABI-System in vier Stufen

### Stufe 1 — .NET-ABI (`extern "dotnet"`, prototypisiert)
- **Syntax (Grammatik §3.1):** `ExternDecl = 'extern' STRING 'fn' IDENTIFIER '(' [ParamList] ')' [':' TypeExpr] ['=' STRING] ';'` — `extern` kontextuell (new-features: kein Bruch für Bezeichner), Symbol `"Type::Method"` Pflicht bei „dotnet“ (assembly-qualifiziert erlaubt: `"System.Net.Dns, System.Net.NameResolution::GetHostName"`). Kein `@Host`-Attribut: ein Attribut, das eine bodylose Funktion legal macht, wäre kein „describes; does nothing“ mehr (Guide 15) — die Deklarationsform ist ehrlicher.
- **Sema:** `LYR-SEM0098` unbekannte ABI, `LYR-SEM0099` Symbolform/Generics/`throws`/Typ, der nicht kreuzt. Stufe-1-Typmenge: Skalare (alle Breiten), `bool`, `char`, `string`, `void` als Rückgabe.
- **Lowering:** Import-Zeile `dotnet:<Type>::<Method>` (`NameMangling.ForExtern`), Capability `hostAccess` ins Modul (auch ungenutzt — dieselbe Regel wie ein ungenutzter `import std.io.file`). Kein neuer Opcode: `callnative` wie heute.
- **VM:** `DotnetBinding.TryBind` im `NativeRegistry.Bind`-Fallback: Typ auflösen (`Type.GetType`, dann geladene Assemblies), genau eine `public static`-Überladung mit der Wire-Signatur (Tags → CLR-Typen exakt), Ambiguität = Ladefehler mit Kandidaten, `MethodInfo.Invoke` (Folgeschritt: `Expression.Compile`/`DynamicMethod` für Hot Paths, `LibraryImport`-artige Stubs für AOT). Explizite Registrierung unter demselben Namen gewinnt vor Reflection (Host kann ein Symbol umdeuten oder sperren).
- **Marshalling-Regeln pro Lyric-Typ ↔ .NET (Zieltabelle für §13, Stufe 1 fett = umgesetzt):** **int→Int64, int8/16/32→SByte/Int16/Int32, uint→UInt64, uint8/16/32→Byte/UInt16/UInt32, float→Double, float32→Single, bool→Boolean, char→Char (Code-Punkt > U+FFFF = Panik, nie gesplittet), string→String (null→"")**, `?T`→`Nullable<T>` bzw. Referenz-null, `T[]`→`T[]` per Kopie (Rückgabe `ReadOnlySpan<T>` per Kopie), Struct→Felder geflattet (wie Native Roots; Rückgabe über Out-Buffer), Klasse→Host-Handle (Tag 0x47; Instanzmethoden über `extern "dotnet" fn (this: Handle)…` = Receiver Parameter 0), Enum (Unit) → .NET-Enum per Tag-Namen, Callback `fn(A)->R` → `Func<A,R>`-Delegate mit Reentranz-Guard und Budget-Vererbung (Bug-Hunt-Fund 15), Exception → **Stufe 1: Panik `LYR-VM0016`**, Ziel: `throws HostError` (Klasse `kind: HostErrorKind, typeName, message`, Muster wie `IoError` — stdlib-redesign: Result entsteht per `attempt(...)`, nicht als Native-Rückgabe).
- **Capability:** `hostAccess` (Bit 3). Ein feineres `host:<assembly>` wäre kein Bit mehr, sondern ein String-Grant; Vorschlag: Bit 3 gewährt alles, ein Host filtert zusätzlich über die Registrierung/`HostOptions.AllowedAssemblies` (Ladefehler statt Bit). Spec §4.5 Tabelle Bit 3: „`std.dotnet`“ → „`extern "dotnet"` declarations“.
- **Tooling:** Formatter und AST-Dump umgesetzt; LSP ohne Änderung (Hover zeigt die Signatur); DAP: ein Step über einen Extern-Aufruf verhält sich wie ein Native.
- **lyrpack:** Assemblies aus dem Prozess (`System.*`) sind im Stub enthalten; fremde Assemblies (NuGet) müssten neben die Executable gelegt oder im Footer (reserviertes Feld → Version 2 mit Anhang „assemblies“) gebündelt werden. NativeAOT-Stub: Reflection auf getrimmte Typen schlägt fehl → `lyrpack` braucht eine `rd.xml`/`TrimmerRootAssembly`-Liste aus den `dotnet:`-Imports des Moduls (lyrpack liest das Modul dafür — Bruch mit „packing is not verification“, aber nur lesend).

### Stufe 2 — C-ABI über P/Invoke/`NativeLibrary` (`extern "C"`)
- `extern "C" fn strlen(s: CStr): uint = "libc:strlen";` — Symbol `"<lib>:<name>"`, `lib` wird über `NativeLibrary.Load` mit RID-Suchpfad aufgelöst (`libc`, `libz.so.1`, `zlib1.dll`); Import-Zeile `c:libc:strlen`.
- Typen in `std.ffi` (stdlib-redesign: PascalCase, eigenes Modul, nur `std.core`-Import): `CInt`/`CLong`/`CSize` (Aliase auf Breiten je Plattform — Spec fixiert die Tabelle pro RID), `CStr` (nullterminiert, Kopie aus/nach `string`, UTF-8), `CBuf` (Länge + Zeiger, aus `uint8[]` per Kopie, `borrowed` nur für die Dauer des Aufrufs gepinnt), `CPtr<T>` (opak, kein Arithmetik-Operator), `CFn<…>` (Callback, Stufe 3).
- `unsafe { … }`-Block: einzige Stelle, in der `CPtr`-Zugriffe (`ffi.read<T>(p)`, `ffi.write`, `ffi.free`) und Aufrufe von `extern "C"` erlaubt sind; Sema-Fehler außerhalb. Kein Ownership-Checker; Konvention `owned`/`borrowed` als Attribut-Vokabular (`@Owned`) auf Parametern ist Doku, keine Semantik — ehrlich zu „Attribute tun nichts“.
- Capability `ffiAccess` (Bit 5, neu, §4.5/§13 Tabelle wachsen „nur durch Hinzufügen“). Dokumentierte Regel: mit Grant sind Abstürze in fremdem Code möglich; ohne Grant kein Laden.
- Marshalling per `LibraryImport`-artigen Stubs (generiert zur Ladezeit über `DynamicMethod`, unter AOT über eine vorab generierte Stub-Tabelle im Stub-Build) — Kosten und AOT-Pfad sind der Hauptaufwand dieser Stufe.
- Coroutinen: ein C-Aufruf blockiert die VM (single-threaded); Callbacks aus fremden Threads sind verboten (Guard: Thread-ID prüfen, sonst Panik).

### Stufe 3 — Export-Richtung
`pub extern fn onTick(dt: float): void { … }` → im Embedding als typisiertes Delegate abrufbar (`instance.Export<Action<double>>("onTick")`, generiert per `Expression.Lambda` mit Marshalling nach derselben Tabelle), im Stub als `UnmanagedCallersOnly`-Einsprung für C-Hosts (Stub exportiert `lyric_call(name, args…)`). Bytecode: Attribut-Zeile oder Flag im Funktionseintrag, damit Reachability die Funktion als Wurzel behält (wie attributierte Funktionen heute).

### Stufe 4 — `lyrbind`
Generator (Stufe-3-Modell aus design/macros.md): aus einer .NET-Assembly per Reflection (`public static`-Methoden, später Typen als Handles) und aus C-Headern per ClangSharp `.lyr`-Dateien mit `extern`-Deklarationen unter `gen/`; aufrufbar aus build.lyr. Mit Filter (`--type System.Math --type System.IO.Path`) statt „alles“.

### Bytecode-Bedarf, Spec, Breaking
- **§13:** kein neuer Opcode. Imports-Abschnitt: Namensschema `<abi>:<symbol>` als Konvention dokumentieren (wie die Out-Buffer-Konvention heute); Capabilities-Tabelle Bit 3 belegt, Bit 5 `ffiAccess` neu (Stufe 2). Reader unverändert (Namen sind Strings).
- **§4.5:** Tabelle ergänzen; Satz „a compiler records the bits its imports imply“ → „… its imports and extern declarations imply“.
- **§3.1 Grammatik, §12/Anhang A:** `ExternDecl`; `SEM0098/0099`, `VM0016`; Stufe 2: `unsafe`-Block (§7), `CAP`-Regel für ffi.
- **§11 stdlib-Vertrag:** `std.ffi` (Stufe 2), `HostError` (Folgeschritt zu Stufe 1).
- **Breaking:** Stufe 1 nein (Minor: kontextuelles `extern`, reserviertes Bit belegt, Formatversion bleibt 4.0, alte Module laden unverändert). Stufe 2 Minor (neues Bit ist „nur hinzugefügt“; ältere Runtimes lehnen solche Module korrekt ab). `unsafe` als Schlüsselwort: kontextuell möglich (`unsafe {`), sonst Major.

## 5. Prototyp Stufe 1

**Commit** „abi: extern "dotnet" binds a public static .NET method by reflection (stage 1 prototype)“ (91b3a96f) auf `worktree-agent-ab3c14434f8eda027`.

Dateien: `src/Lyric.Frontend/AST/Declarations.cs` (`ExternSpec`), `Parsing/Parser.Declarations.cs`, `Sema/TypeChecker.cs` (`CheckExtern`), `Ir/Lowering/ModuleLowerer.cs`, `Ir/Lowering/NameMangling.cs` (`ForExtern`), `src/Lyric.Core/Capabilities.cs` (`DotnetPrefix`), `src/Lyric.Vm/DotnetBinding.cs` (neu), `src/Lyric.Vm/NativeRegistry.cs` (Fallback in `Bind`), `src/Lyric.Vm/VmDiagnostics.cs` (`HostCallFailed`), Formatter/Dumper/AstChildren, `docs/Grammar.md`, `docs/guide/14-embedding.md` („Reaching into .NET from the script“), `examples/ffi/dotnet.lyr`, `tests/Lyric.Tests.Vm/ExternDotnetTests.cs` (13 Tests).

Beispiel (`lyric run examples/ffi/dotnet.lyr`):
```
cbrt(27) = 3.0000000000000004
clamp(99, 0, 10) = 10
tmp = /tmp/
host = YogaG10-OW
' ' is white: true, 'x' is white: false
HOME set: true
```
`lyrvm run app.lyrbc --grant none` → `error[LYR-CAP0001]: module requires capability 'hostAccess', which this runtime does not grant`; Disassembly: `capabilities: 0x8`, `import dotnet:System.Math::Cbrt(f64) -> f64`. Fehlerfälle: `extern "c"` → `LYR-SEM0098`; `int[]`/`?int` → `LYR-SEM0099`; `System.Math::NoSuch` → `LYR-VM0005 … has no public static method 'NoSuch'`; `System.Math::Sqrt(string)` → „no overload (String) -> Double; it has: (Double) -> Double“; `System.Int32::Parse("zz")` → `panic [LYR-VM0016]: host call 'System.Int32::Parse' threw FormatException: …`.

Funktioniert: Deklaration, Sema-Prüfung, Import-Zeile, Capability-Bit + Grant-Prüfung, Reflection-Bindung mit Überladungswahl, Marshalling int-Breiten/float/bool/char/string, assembly-qualifizierte Typen, Host-Registrierung gewinnt, Formatter idempotent, alle bestehenden Suiten grün.
Aufteilung mit stdlib-redesign: nativ in der stdlib bleiben Digests, UTF-8, Base64/Hex, Zeit, Datei/Netz/Prozess (alles ohne hostAccess); über `extern "dotnet"` im Nutzercode: Kompression (System.IO.Compression), HTTP-Client (System.Net.Http), Regex, Zeitzonen (TimeZoneInfo), Kryptografie jenseits Digests — dafür ist vor 5.x kein stdlib-Modul geplant.

Fehlt: Optional/Array/Struct/Handle-Marshalling, Instanzmethoden, Exceptions → `throws HostError`, Callbacks, `Expression.Compile`-Stubs, `std.dotnet`/`HostError` in der stdlib, NativeAOT-Trimmerliste in lyrpack, Stufe 2 (libc über `NativeLibrary` — nicht begonnen), Spec-Text.
