# Lücken im Fragenkatalog für Lyric 5

Vollständigkeitskritik über alle 24 Gebietsdossiers (20 in `_kompakt-20.json`, 4 inline: Laufzeit,
Bytecode/VM, stdlib, Editor-Werkzeuge). Stand 2026-09-28, Repo `main @ 6f6f029f`.

**Was vorliegt:** 966 Fragen (825 in den 20 Kompaktgebieten, 141 in den vier Inline-Gebieten),
je mit Empfehlung, Bruchgrad und `haengtAn`. Der Katalog ist tief, wo er ist. Was fehlt, ist
nicht Tiefe, sondern (A) einige Alltagsfähigkeiten, die kein Gebiet für sich beansprucht,
(B) die Ebene ÜBER den Gebieten, und (C) die Fragen, die nur Schmerz versprechen.

**Methode:** Kompaktkatalog vollständig gelesen; in die Dossiers gegriffen, wo der Katalog nicht
reichte (Grep über alle 24 Dateien nach ~60 Stichworten, Kontext gelesen); Repo-Fakten am
Checkout geprüft (`docs/Grammar.md`, `docs/guide/`, `stdlib/std/`, Tags, CI, `CONTRIBUTING.md`,
`STATUS.md` §Still open/§Design decisions, `PLAN.md`, `lyric-v5-features.md`). Nichts gebaut,
nichts ausgeführt. Belegstatus je Punkt: **gelesen** (Datei/Zeile), **gezählt** (Grep/Git),
**behauptet** (nicht geprüft).

Jede Lücke ist als beantwortbare Frage formuliert. Wo eine bestehende Frage NAHE liegt, steht sie
dabei, damit die Lücke nicht als Doppelung gelesen wird.

---

## A. Sprachencheckliste — was ein Benutzer selbstverständlich erwartet

Reihenfolge Rust, Swift, Go, C#, Kotlin, Python, Zig, Scala. Was eine Sprache erwartet und der
Katalog schon hat, steht in einer Zeile; die Lücken sind nummeriert. Eine Lücke steht bei der
Sprache, bei der sie zuerst auffällt, und wird nicht wiederholt.

### Rust

Abgedeckt: Traits (IF), Generics/Constraints (G), `match`/Patterns (EP), `Option` (OPT),
`Result`/`?` bewusst nicht (F16, SL-04), Iteratoren (COL), Closures (FN), Module/`pub` (MOD),
Cargo-Fragen (BP), `derive` als Synthese (META-13), `unsafe` Nein (L13), Integerbreiten (SK),
Formatsprache (S05), Doc-Kommentare (E12), Labels (seit 4.5, `design/loop-labels.md`, gelesen),
Slices (COL-02), `with`-Update (W7), `impl<T: Bound>` (G05), Orphan-Regel (IF-12).

**A-01 — Wie erzwingt ein Typ seine Invariante bei der Konstruktion?**
Befund (gelesen, `docs/guide/05:1-40`): Struct UND Klasse werden ausschließlich über das
Initializer-Literal `S { x = 1 }` gebaut; es gibt keinen Konstruktor mit Rumpf, keine
Validierung, keine private Konstruktion. Heute sind alle Felder sichtbar, also ist jeder Typ von
außen in jedem Zustand herstellbar. MOD-05 macht Member privat — dann ist das Literal von außen
NICHT mehr schreibbar, und jeder Typ mit privatem Feld braucht eine `static fn`-Fabrik. Kein
Dossier stellt die Frage (W12 fragt nur nach Felddefaults, MOD-05 nach Sichtbarkeit, nicht nach
Konstruierbarkeit). Rust: private Felder + `new`; Swift: `init` mit Regeln; Kotlin: `init {}` +
`require`; C#: Konstruktor; Go: Konvention `NewX`. Fragen: (1) Ist „privates Feld ⇒ Literal nur
im Modul" die Regel, und steht sie in §4.2? (2) Braucht 5.0 eine Konstruktorform, oder ist
`static fn` + Literal die eine Antwort — dann muss die Synthese (`Default`, META-14) und der Host
(EP-30 „kein Konstruktor") sie auch nehmen. (3) Was ist ein Struct mit privatem Feld für die
Konformanz-Synthese und für `with` (W7): darf `p with { secret = 1 }` von außen?

**A-02 — Wie leitet ein Typ ohne Vererbung ein Interface an ein Feld weiter?**
Befund (gezählt): „Delegation" hat in 24 Dossiers null Treffer in diesem Sinn. Ohne Vererbung
(bewusst) und ohne `extend` auf Interfaces (IF-35 A) schreibt jeder Wrapper jede Methode von Hand.
Kotlin `by`, Go-Einbettung, Rust `Deref`-Missbrauch, Swift Protocol-Extensions sind vier Antworten
auf diese Frage. Fragen: Bekommt Lyric 5 eine Weiterleitungsform (`:: [Display by inner]`,
Synthese „Konformanz über Feld"), oder ist Boilerplate die bewusste Antwort — und steht das dann im
Guide-Kapitel 7 als Regel, nicht als Schweigen?

**A-03 — Bleibt Lyric ASCII-only bei Bezeichnern?**
Befund (gelesen, `docs/Grammar.md:57-59`): `IdentStart = 'a'..'z' | 'A'..'Z' | '_'`. Rust,
Swift, Kotlin, C#, Go, Python, Scala erlauben Unicode-Bezeichner (XID_Start/XID_Continue), Zig
nicht (aber `@"…"`). Kein Dossier fragt. Zusatz: die Kodierung einer `.lyr`-Datei (UTF-8? BOM?
CRLF?) ist nirgends spezifiziert — S25 regelt Zeilenenden nur IN Mehrzeilen-Literalen. Fragen:
(1) Unicode-Bezeichner ja/nein, und wenn ja mit NFC-Normalisierung und Confusable-Regel (Rust
`uncommon_codepoints`)? (2) Ein Satz in §1: Quelle ist UTF-8, BOM wird ignoriert/abgelehnt,
Zeilenende beliebig.

**A-04 — Welche Container hat 5.0 über List/Map/Set/Deque hinaus?**
Befund (gelesen, `docs/guide/13:11`; gezählt): `stdlib.md` hat null Treffer für
`SortedMap`, `SortedSet`, `PriorityQueue`, `BitSet`, `LruCache`, während
`lyric-v5-features.md` B1 sie alle als P1 führt. Der stdlib-Katalog fragt nach Umfang bei Regex,
HTTP, TLS (SL-01), nicht bei Containern. Fragen: (1) Welche Container gehören in den ersten Ring
und welche in den zweiten (SL-18)? (2) `SortedMap<K,V>` braucht `Ordered<K>` als Constraint —
hängt das an G02 (Self) oder geht es mit `Ordered<K>` heute? (3) Ist eine persistente Sammlung
(HAMT) neben der veränderlichen ein zweiter Mechanismus (Rule 2) — und ist sie die natürliche
Nachricht für Isolates (NL15)?

**A-05 — Gibt es eine Nur-Lese-Sicht auf einen Klassencontainer?**
Befund: `List<T>` ist eine Klasse; `let l = List<int>.empty()` bleibt veränderlich (W3 behandelt
`let` nur für Struct-Felder). Kotlin trennt `List`/`MutableList` im Typ, C# hat
`IReadOnlyList`, Swift `let`-Arrays, Rust `&`/`&mut`. Eine Funktion, die eine Liste nur lesen
will, kann das heute nicht in der Signatur sagen. Frage: Trägt der TYP die Nur-Lese-Sicht
(Interface `ReadOnlyList<T>` / `Iterable` reicht?), die BINDUNG (`let` auf Klassen, W6 B
zurückgestellt), oder niemand — und was empfiehlt der Guide dann für „ich gebe dir meine Liste,
aber ändere sie nicht"?

### Swift

Abgedeckt: Optionals/Narrowing (OPT), `let-else`/`if let` (EP-11, OPT-09), `defer` (F), `mutating`
(W5), `inout` abgelehnt (W38), `throws`/typed throws (F), `Never` (F28/OP-16), Synthese (META-13),
`CaseIterable` (EP-07/META-18), `Codable` (SL-14), Zugriffsstufen (MOD), Erweiterungen (IF-11),
Subscripts (COL-07), `Array(repeating:)` (W36), Grapheme (S01/S16), Tail-Closures (FN),
`@available` (D13), assoziierte Typen abgelehnt (IF-06/G04), `some/any` nicht nötig (Fat Pointer).

**A-06 — Eigenschaften (get/set, `didSet`) oder Felder plus Methoden?**
Befund (gezählt): „Getter/Setter/computed" hat keinen Treffer, der Lyric meint. Swift, C#,
Kotlin, Scala haben Properties; Rust, Go, Zig, Python (ohne `@property`) nicht. Mit MOD-05
(private Member) wird jedes lesbare Feld zu `fn x(): int { return this.x; }` — Kotlin und C#
haben Properties genau gegen dieses Boilerplate eingeführt. Fragen: (1) Sind Felder ein
Vertrag (dann darf ein pub Feld nie zu einer Methode werden, ohne Aufrufer zu brechen — MOD-24
`lyric api` muss Felder als API führen)? (2) Bleibt „Feld oder Methode" die bewusste
Zweiteilung, und steht sie im Guide? (3) Was macht `p.x = 1` auf einem pub Feld einer Klasse mit
der Kapselung — gibt es `pub(get)` (Swift `private(set)`, MOD-Dossier nennt es in der
Vergleichstabelle, stellt aber keine Frage)?

**A-07 — Faule Initialisierung (`lazy`, `Once`)?**
Befund: Modulglobale werden in Deklarationsreihenfolge vor `main` gerechnet (`guide/05:91`,
MOD-10). Swift `lazy var`, Kotlin `by lazy`, Rust `LazyLock`, Scala `lazy val`, C# `Lazy<T>`.
Eine teure Tabelle wird heute bei jedem Programmstart gebaut, auch wenn sie nie gelesen wird.
Frage: Sprachform, `std.core.Lazy<T>` (braucht Closures ohne Allokation? FN30), oder bewusst
nichts — und was heißt es für Reproduzierbarkeit (BP-29) und Budget (L31), wenn Initialisierung
bei der ersten Lesung passiert?

**A-08 — Signale: SIGINT/SIGTERM, geordnetes Herunterfahren?**
Befund (gelesen, `stdlib/std/os.lyr`): `exit`, `env`, `args`, `platform`, `sleep` — kein
Signal. NL33 behandelt nur den sticky `interrupt()`-Waker, der Ctrl+C „schluckt".
`lyric-v5-features.md` B5 nennt „Signal-Handler" ohne Frage im Katalog. Swift/Go/Python/Rust
haben je eine Form. Fragen: (1) Ist ein Signal ein Waker (NL35, eine Form) oder ein Handler?
(2) Was tun `defer`-Ketten und laufende Tasks bei SIGTERM — läuft die Arena (L10) oder nichts
(F33)? (3) Windows-Konsolenereignisse (Ctrl+Break, Close) — dieselbe Abstraktion?

### Go

Abgedeckt: Koroutinen/Channels/`select` (NL), `defer` (F), Fehler als Werte (SL-04), `gofmt`
(E6), `go test`/`-json`/`-cover`/`b.N` (T), `go generate` als `generated(...)` (BP-10), `go doc`
(E12), `go env` (CLI-9), `go fix` (CLI-19), `embed` (META-10), Build-Tags als `-D` (BP-17),
Cross-Compile (BP-19), `init()` (F39), `go vet`/Lint abgelehnt (CLI-22), strukturelle Typen
bewusst nicht (nominal), Go-Form der Labels (gelesen).

**A-09 — Shebang und Skript-Modus für CLI-Werkzeuge?**
Befund (gezählt): `#!` hat null Treffer in `docs/Grammar.md` und in `cli.md`. Lyric zielt laut
README auf „CLI tools"; Deno, Python, Rust-Script, Swift-Skripte und Go (`go run`) decken das.
Fragen: (1) Ist `#!/usr/bin/env lyric` als erste Zeile lexikalisch erlaubt (Zeile ignorieren)?
(2) Cached `lyric run datei.lyr` das Kompilat (CLI-26 fragt nach dem Projekt-Cache, nicht nach
dem Einzeldatei-Fall)? (3) Verhält sich ein Shebang-Lauf wie `lyrvm run` (Capability.All) oder
wie ein Projekt (E21/CLI-16)?

**A-10 — Ist stdout UTF-8, unabhängig von der Konsolen-Codepage?**
Befund (gezählt): `OutputEncoding`/Codepage/`chcp` — null Treffer. Auf Windows druckt ein .NET-
Prozess je nach Codepage etwas anderes; S-Gebiet behandelt Strings nur intern, CLI-13 nur Farbe.
Go, Rust, Python 3.7+ schreiben UTF-8; C# hängt an `Console.OutputEncoding`. Fragen: (1) Verspricht
die Laufzeit UTF-8 auf stdout/stderr/stdin und in `args()`, und wer setzt es — VM, `lyrvm`, der
Host (`HostOptions.Output` ist ein Writer)? (2) Was ist die Zusage bei Umleitung in eine Datei
(gleiche Bytes wie am Terminal)? Das ist ein Konformanzfall, kein Geschmack: `lyrtest` vergleicht
Ausgaben byteweise.

### C#

Abgedeckt: Properties siehe A-06, `using`/`IDisposable` abgelehnt bzw. Arena (F6/L10), `yield`
(NL), LINQ als Kette (COL-35), `Span` (COL-02), `record with` (W7), `switch expression` (match),
`is`-Pattern (EP-05), `checked` (SK-02/03), `Nullable` (OPT), `params` (FN05), `Default` (META-14),
`Decimal`/`BigInteger` (SK-15 b, Bibliothek), Regex/DateTime/TimeZone (SL-01), `Enum.Parse`
(EP-07), `GetHashCode`-Vertrag (SL-26), Overload-Rangfolge (OVL), `[Flags]` siehe A-11,
Exit-Codes (CLI-12/32), `TryGetValue` (OPT-04), NativeAOT (B23), Analyzer-Unterdrückung (D3).

**A-11 — Bitflags: Enum mit `|`/`&`, oder `uint` plus Konstanten?**
Befund (gezählt): `Bitflag`/`Flags`/`OptionSet` — null Treffer, die Lyric meinen. EP-08 fragt
nach Rohwerten, nicht nach Mengen. C# `[Flags]`, Swift `OptionSet`, Rust `bitflags`, Zig
`packed struct(u8)`. Capabilities selbst sind ein Bitset (B10) — die Sprache hat für ihr eigenes
Grundmuster keine Form. Frage: Synthese (`:: [FlagSet]` mit `|`, `&`, `contains`), Bibliothekstyp,
oder bewusst `uint`-Konstanten — und was heißt das für Exhaustiveness (ein Flag-Enum ist nie
erschöpfend)?

**A-12 — Ereignisse/Beobachter: Funktionswerte, Channels, oder nichts?**
Befund: C# `event`, Kotlin `Flow`, Swift `Combine`, Go-Channels. Lyric hat Funktionswerte (FN)
und (geplant) Channels (NL4); ein „mehrere Abonnenten"-Muster steht nirgends. Für Erato (Editor)
ist das das tägliche Muster. Frage: Ist `Channel<T>` mit Broadcast (NL41 sagt: exklusiv für
Waker, Broadcast für Deskriptoren — und für Channels?) die eine Antwort, oder gehört ein
`Signal<T>`/Observer-Typ in den zweiten Ring — oder in den Guide als Idiom aus `List<fn(T)>`?

### Kotlin

Abgedeckt: `data class` (Synthese), `sealed` (IF-17), Null-Sicherheit (OPT), `by lazy` siehe A-07,
`by`-Delegation siehe A-02, Default/benannte Argumente (FN01/02), `when` (match), `inline`
abgelehnt (FN17), Erweiterungen (IF-11), Koroutinen (NL), `Result` (SL-04), `require/check` siehe
A-13, `typealias` (G15), Companion/`static` (G03), Ranges `downTo/step` (COL-05),
Destrukturierung (EP-12), `!!` (OPT-11), `return@label` abgelehnt (FN24), `value class` (opaque),
`ReplaceWith` (D13), `DeprecationLevel` abgelehnt (D13-F).

**A-13 — `assert` im Produktionscode: bleibt es im `release`-Profil?**
Befund (gelesen, `guide/13:7`): `std.core` hat `assert`, `todo`, `unreachable`. T1 diskutiert
ein zerlegendes `assert` NUR für Tests. Rust `debug_assert!`, C# `Debug.Assert`, Kotlin
`-Xassertions`, Java `-ea`, Swift `assert` (nur Debug) vs `precondition` (immer). Fragen: (1) Läuft
`assert` im `release`-Profil, und ist ein profilabhängiges Verhalten mit §3.2 „not
configurable" und BP-29 (Reproduzierbarkeit) verträglich? (2) Gilt T1-B (zerlegender Ausdruck)
auch hier, oder hat Lyric dann zwei `assert`? (3) Braucht es das Paar `assert`/`precondition`
(Swift), wenn `panic` schon da ist?

**A-14 — Singleton/„object": wie hält ein Modul eine Instanz mit Zustand?**
Befund: Kotlin `object`, Scala `object`, Swift `static let shared`, Go Paketvariable. Lyric hat
`static let` (Konstante, `guide/05:70`) und veränderliche Modulglobale (W18). Ein `static let
shared = Cache { … }` ist eine KLASSE hinter einem `let` — veränderlich per Referenz. Frage: Ist
das das Idiom (dann in den Guide), und was ist es unter Isolates (NL27 „Modulglobale je Isolat")
und unter `lyrtest` (Modulzustand je Test isoliert, STATUS §Still open)?

### Python

Abgedeckt: Generatoren/`send()` (NL12/23), `with` (F6), Dekoratoren als Attribute (META),
`**kwargs`/`*args` (FN01/05), Slicing (COL-02), negative Indizes abgelehnt (COL), f-Strings (S),
`__repr__` (S10), `dataclass` (Synthese), `pathlib` (`std.io.path`, gelesen), `argparse`/`logging`
(SL-16), `re` (SL-01), `asyncio` (NL), `doctest` (T17), REPL (E5), `help()` (E12), Introspektion
ausgeschlossen, BigInt (SK-15 b), `in` (COL-31), `is` (W44), Walrus (OP-3), `setrecursionlimit`
(L5), `PYTHONHASHSEED` (L23), `subprocess` (std.process), `signal` siehe A-08, `venv`/Toolchain
(BP-13), `sys.exit` (CLI-12).

**A-15 — Comprehension-Ergonomie: `[f(x) for x in xs if p(x)]` gegen `over(xs).filter(p).map(f).collect()`**
Befund: kein Dossier stellt die Frage nach der KÜRZE des Alltagsfalls; COL-35 will die eager
Welt streichen, womit die lange Kettenform die einzige wird. Python/Scala/Kotlin (`xs.filter{}
.map{}` auf Iterable direkt) sind kürzer als Lyric nach COL-35 B. Frage: Bekommt `T[]`/`List<T>`
die Adapter direkt (COL-12 B Extensions machen das billig), damit `xs.map(f)` ohne `over()`
geht — und ist das dann wieder die zweite Welt, die COL-35 gerade streicht? Eine Regel, EIN
Beispiel im Guide: „so schreibt man filter-map in Lyric 5", mit Zeichenzahl gegen Python.

**A-16 — Was sieht ein Anfänger als Erstes: Import-Pflicht für `println` und Stringmethoden**
Befund (gelesen, README-Beispiel: zwei `import`-Zeilen vor `println`; S19: Stringmethoden
melden eine falsche Import-Warnung; MOD-18 fragt nach Prelude „als Frage fürs stdlib-Gebiet",
das stdlib-Gebiet stellt sie nicht). Python, Go, Kotlin, Swift, Scala drucken ohne Import.
Frage: Bekommt Lyric 5 ein Prelude (`println`, `List`, `Map`, `Display`, `assert`), wer besitzt
die Entscheidung (MOD-18 verweist auf stdlib, stdlib hat sie nicht), und was kostet es die
Sichtbarkeitsregel (MOD-35 implizite Namen)?

### Zig

Abgedeckt: `comptime` (META-06..12/30/37), `errdefer` (F8), Optionals, Slices, `build.zig`
(BP-10), `@embedFile` (META-10), `zig fmt` ohne Optionen (E6), Cross-Compile (BP-19),
`unreachable` (std), Trap-Disziplin (B16), Labels/`break :blk value` abgelehnt (OP-15), beliebige
Bitbreiten (SK-15), `translate-c` als Build-Schritt (F12), Hash im Manifest (BP-06),
`packed`/`extern struct` Layout (F18).

**A-17 — Gibt es eine Wahrheit für „Größe und Ausrichtung eines Werts"?**
Befund: L1 (16 Byte/Element), L41 (außerhalb x64), F18 (C-Layout), W31 (Kostenmodell großer
Werte), SK-36 („layouts" ohne Bedeutung). Vier Gebiete, keine Frage nach EINER Aussage, die ein
Benutzer lesen kann (Zig `@sizeOf`, Rust `size_of`, C# `sizeof`). Frage: Sagt Lyric 5 einem
Benutzer, was ein `struct { a: int8, b: int8 }` im Array kostet — als Spec-Satz (Wertdarstellung
ist QoI, 16 Byte im Referenz-Runtime), als `std.mem.sizeOf<T>()` (comptime-Wert), oder gar nicht?

### Scala

Abgedeckt: `case class` (Synthese), `derives` (META-13 B), Typklassen als Interfaces, `Option`/
`Either`/`Try` (OPT/SL-04/F5), Pattern-Guards (`MatchArm = Pattern [ 'if' Expr ]`, gelesen
`Grammar.md:386`), Extraktoren abgelehnt (EP-15), Union-Typen abgelehnt (F25), HKT abgelehnt
(G08), `@tailrec` (L36/FN18), REPL/Worksheet (E5), Makros abgelehnt, strukturelle Typen abgelehnt,
`lazy val` siehe A-07, `object` siehe A-14, Varianzannotationen abgelehnt (G07/IF-18).

**A-18 — Tupel: benannt, gleich, indexierbar — oder Zweitbürger?**
Befund (gezählt: „Tupel" in 13 Dossiers, als Nebenbemerkung): Tupel-Gleichheit offen (OP-36
„ans Werte-Gebiet", das Werte-Gebiet stellt sie nicht), Tupel-Kontext fehlt (STATUS §Still
open), Tupel-Index (Prototyp 20), Tupel-Adaption (SK-07), Tupel-Pattern (EP), Tupel-Payload-Kopie
(L34), benannte Tupel: null Treffer. Scala, Swift, C#, Python haben benannte Elemente; Rust
nicht. Fragen: (1) Ist ein Tupel in 5.0 ein anonymer Struct mit ALLEN Struct-Pflichten
(Synthese, Display, `with`, `mut`, Hashable), oder ein reiner Transportwert ohne Konformanz?
(2) Wer ist Owner (kein Gebiet nennt sich)? (3) Siehe C-12: streichen?

**A-19 — Partielle Anwendung, Currying, Funktionskomposition: bewusst nicht?**
Befund (gezählt): null Treffer. Scala/Haskell/F# haben es; Kotlin/Swift/Rust nicht. FN behandelt
Methodenwerte (FN12) und generische Funktionswerte (FN13). Frage: ein Satz in FN als „bewusst
nicht" mit Grund (kein Bedarf ohne HKT), damit es nicht in jeder Runde neu gefragt wird.

**Bewusst NICHT als Lücke geführt** (geprüft, im Katalog oder ausdrücklich ausgeschlossen):
Vererbung, `finally`, Threads, Reflexion, Makros, `Option`-Enum neben `?T`, `?` auf Result,
`break` mit Wert, Ordnung auf Optionals, `@Inline`, `unsafe`, HKT, Spezialisierung,
Wertparameter, kovariante Arrays, Ternär (if-Ausdruck OP-14), C-`for(;;)` (Ranges),
Mehrdimensionale Arrays (COL-18), Integer-Division (Spec §3.2:56 trunc, gelesen),
Literal-Formen mit `_`/`0x`/`0b`/`0o` (Grammar :91-95, gelesen), geschachtelte Blockkommentare
(Grammar :47-51, gelesen), Pattern-Guards (gelesen).

---

## B. Lücken zwischen den Gebieten

**B-01 — Ist der Abhängigkeitsgraph über die 966 Fragen überhaupt baubar?**
Befund (gezählt aus `_kompakt-20.json`): `haengtAn` verweist neben Frage-IDs auf **194
verschiedene Freitext-Namen** für ~24 Gebiete — „Diagnostik", „Diagnostik-Gebiet", „Gebiet
Diagnostik", „Gebiet Diagnosen"; „Bytecode-Gebiet", „Bytecode", „Bytecode/VM", „Gebiet Bytecode",
„Bytecode-Format", „Bytecode-/ABI-Gebiet", „Bytecode/VM (bei B)"; dazu Ziele wie „alle", „alle
NL", „Rule 2", „ValueBlock", „rawArrayAlloc". Damit ist kein kritischer Pfad rechenbar, und die
vielen „X zuerst"-Sätze widersprechen sich gegenseitig (W34 zuerst ↔ §3.4a zuerst ↔ OPT-35
„Reihenfolge §3.4a → OPT-18 → OPT-07"; COL-34 vor COL-15; F25 „ZUERST"; META-16 „zu Unrecht auf
Platz 1"). Fragen: (1) Wer legt die kanonische Gebietsliste mit Kurzkennung fest und normalisiert
alle `haengtAn`? (2) Wer rechnet danach die topologische Sortierung und benennt die Zyklen?
(3) Ist das Ergebnis eine Datei im Repo oder bleibt es Scratchpad (siehe B-11)?

**B-02 — Wer ist Owner der Querschnittsthemen, die in fünf und mehr Gebieten „mitentschieden" werden?**
Befund (gezählt): (a) **Unterdrückung einer Warnung**: mindestens fünf Formen im Katalog —
SK-24 `@Allow`, SL-17 `@Allow{warning=…}` auf Funktion/Modul, EP-21 „A für die Ausnahme, C für
die Stufe", META-22 C projektweite Liste in `lyric.json`, D3 `expect`-Direktive für einen
aufgezählten Codesatz, D22 Gruppen als Achse. Das ist Rule 2 im Katalog selbst. (b) **lyrfix**:
20 Gebiete nennen es (113 Treffer), es existiert nicht; CLI-19 sagt Verb `lyric fix`, D9 sagt
`replacement`-Feld, META-33 sagt eigener Modus, OP-31 sagt Minimum, W45 sagt Klassifikation je
Bruch, S27 sagt „Klasse 3 nie automatisch". (c) **Konformanz-Synthese**: META-13 (Swift- vs.
Scala-Paket), IF-08, OPT-31, SK-38, W11, EP-07, OP-26, SL-14, S09/S10, T15 — zehn Gebiete, ein
Paket. (d) **`mut struct`/§3.4a**: W, OPT, EP-16/26, IF-14/23, COL-15, L2/32, NL15, FFI. (e)
**`lyric.json`-Schlüssel**: zwölf Gebiete schlagen ~15 neue Schlüssel vor (META nennt fünf, CLI
vier, BP, E21, T23, FFI F27). Fragen: Für jedes der fünf Themen — welches Gebiet entscheidet,
welche Empfehlung wird verbindlich, und wo steht sie danach EINMAL?

**B-03 — Wie viele Warnungen sieht ein 4.6-Projekt beim ersten 4.7-Build, und wer zählt sie?**
Befund (gezählt, grob): W nennt elf Uhren, SK neun, CLI neun, FN vier, MOD mindestens fünf,
OVL/IF/OP/EP/COL/S/FFI/META je mehrere — in Summe **60 bis 100 neue Warnungen** in 4.7, dazu
SEM0107–0110 seit 4.6 immer an und nicht unterdrückbar (D20: `denyWarnings`-Projekte sind rot).
Kein Dossier plant die 4.x-Serie als Ganzes. Fragen: (1) Gibt es ein Warnungsbudget je Release
(Wellen 4.7/4.8/4.9) und eine Reihenfolge nach Bruchgröße? (2) Was ist der Abstand zwischen der
LETZTEN Uhr und 5.0 — gilt die Regel „ein Minor mit beiden Formen" aus dem v3-Korb? (3) Ist die
Unterdrückung (B-02 a) VOR der ersten Welle gebaut, oder erlebt der Benutzer die Welle ohne
Ventil?

**B-04 — Was ist 5.0: welche der neun Verträge brechen, welche nicht?**
Befund: Der Katalog bewertet Brüche je Frage (major/minor/nein), aber niemand sagt, was der
MAJOR umfasst. Verträge, die je ein Gebiet für sich versioniert: Sprache (Spec-Pin), std-API
(SL-18/27), Bytecode-Format (B8; bleibt 4.x oder wird 5.0?), CLI (CLI-30), `lyric.json`
(BP-16), Host-API `Lyric.Embedding` (siehe B-05), Diagnosecodes und JSON-Strom (D15/CLI-11),
Konformanzsuite/Runner (T20/T33), Pack-Footer (CLI-36), Sprachversionsfeld (W43). Fragen:
(1) Kompatibilitätsmatrix: je Vertrag „bricht/bricht nicht/neu" in 5.0. (2) Was ist DAS Artefakt
von 5.0 im Sinne von Rule 3? (3) Ist ein 4.x-`.lyrbc` unter einem 5.0-`lyrvm` ladbar — G33 und
SK-25 fragen es je für ihr Gebiet, die Antwort gilt für alle.

**B-05 — Ist `Lyric.Embedding` eine versionierte Host-API, und wo steht die Bruchliste für Hosts?**
Befund (gezählt): `HostOptions`/Embedding-API in 17 Dossiers; Brüche verteilt: META-01
(`OnFunctions(name)` bricht), D25 (`Severity.Info`), OVL-14/36 (Mangling), MOD-45 (`main.*`),
EP-30 (Enum-Sicht), IF-46, F23/L37 (Exceptions), T28 (Native ersetzen), SL-34 (Versionsprüfung),
L24 (Budgetobjekt), NL9/35 (step, Waker). `docs/guide/14-embedding.md` enthält kein Wort
„stable/compatibility/breaking" (gezählt). Fragen: (1) Was ist die öffentliche Fläche von
`Lyric.Embedding` (Typen, Optionen, Ereignisse), und gilt SemVer? (2) Ein Host-Migrationsguide
4→5 als Deliverable? (3) Siehe C-07: wer ist der Host, wenn Erato 2 sein eigenes Runtime hat?

**B-06 — Gibt es ein Bedrohungsmodell, und was verspricht `Capability.None` in 5.0 wirklich?**
Befund (gezählt: „Threat/Bedrohungsmodell/Angreifer" null Treffer): Die Sandbox-Zusage ist
heute an mindestens sieben Stellen gebrochen, verteilt auf vier Gebiete — B2 (58-Byte-Modul
tötet den Wirt), B17 (1,6 GB stille Allokation), F1 (`hostAccess` als Bypass), F21 (vergifteter
String → Exit 127), F24 (Reentranz → CLR-StackOverflow), L7 (kein Speicherlimit), T28
(`exit` aus Testcode). `design/abi.md:37` behauptet Abbruchfreiheit (FFI: „gemessen falsch").
Fragen: (1) Wer ist der Angreifer — fremdes `.lyrbc`, fremder Quelltext, fehlerhafter Host? Je
Antwort ist ein anderer Teil des Katalogs Pflicht. (2) Ein Dokument „Was die Sandbox zusagt",
das die sieben Löcher als offen führt, bis sie zu sind. (3) Welche Doku-Sätze (Guide 14,
abi.md, README „capability-gated") werden bis dahin abgeschwächt?

**B-07 — Gibt es ein API-Design-Dokument und ein Namens-Glossar, gegen das std und Sprache geprüft werden?**
Befund: Namensfragen stehen in zehn Gebieten je für sich — SL-10 (Suffix beschreibt nie die
Eingabe), OVL-18 (Typsuffixe), SL-04 (`OrThrow`/`OrErr`), S16 (`isAlpha` vs `isAsciiAlpha`),
S03 (`substring`), COL-14 (`collect`/`toArray`/`collectArray`), NL (`Wait.*`, `Step`), MOD-26
(`internal`), CLI-16 (zwei Schreibweisen je Bit), CLI-19 (`lyric fix` vs `lyrfix`), D22
(Codefamilien). Ein Glossar oder eine Stilregel (Verben, `is/to/as/try/with`-Präfixe,
Singular/Plural, Abkürzungen `fn`/`fmt`/`iter`) hat null Treffer. Fragen: (1) Ein Dokument
nach dem Muster der Swift API Design Guidelines VOR der std-Umbenennungsrunde? (2) Ein Audit der
heutigen std dagegen, mit Zählung der Umbenennungen? (3) Wer entscheidet Wort-Kollisionen über
Ebenen (`pack`: Verb, Paket, Footer — MOD-26 nennt es)?

**B-08 — Wie lernt jemand Lyric 5, und wer prüft, dass der Guide die Wahrheit sagt?**
Befund: 21 Guide-Kapitel (4 600 Zeilen, gezählt), CLAUDE.md: jedes Snippet wird kompiliert
(gelesen); ob die AUSGABEN geprüft werden, ist nicht geprüft (behauptet: nein). EP-20 fordert
„Guide-Kapitel wieder wahr", S17 findet ein Fehlzitat, FN07 findet drei Lambda-Formen ohne
Kapitel, OPT-09 vier Bindungsformen ohne Guide. Kein Dossier fragt nach dem Guide als Ganzem:
Reihenfolge (wann trifft ein Anfänger `::`, `?T`, `throws`, `spawn`, `comptime`), Trennung
Tutorial/Referenz, „erster Tag"-Pfad. Fragen: (1) Bekommt 5.0 eine Sprachreferenz getrennt vom
Guide (die Spec ist keine Benutzerdoku)? (2) Ein Test, der Guide-Ausgaben pinnt, nicht nur
Kompilierbarkeit? (3) Wer schreibt das Kapitel „Lyric für Rust-/Kotlin-/Python-Umsteiger"?

**B-09 — Terminologie und Sprache der Dokumente: ein Glossar EN, und ist die deutsch/englische Doppelung eine Fehlerquelle?**
Befund: Spec, Guide, Meldungen, CONTRIBUTING sind Englisch; STATUS gemischt; PLAN, SPEC-RUNDE,
Design-Dossiers, `lyric-v5-features.md`, CLAUDE.md Deutsch. Begriffe wandern übersetzt
(„Konformanz"/„conformance", „Narrowing"/„narrow", „refused", „Uhr"/„clock", „Wächter"). D27
prüft Meldungsprosa mechanisch, aber gegen kein Glossar. Fragen: (1) Ein Terminologie-Glossar
EN mit deutscher Spalte, gegen das Spec, Guide und Meldungen geprüft werden? (2) Welche
Sprache haben Entscheidungsdokumente (ADRs, B-10) — und muss ein Zweitrunning-Autor (Lyricpp)
Deutsch lesen können?

**B-10 — Wo leben ADRs, welche Vorlage, gilt „30 Tage" für einen Solo-Maintainer mit Claude — und ist CONTRIBUTING für v5 noch der Vertrag?**
Befund (gezählt/gelesen): 17 Dossiers verlangen ADRs; im Checkout gibt es kein ADR-Verzeichnis;
`ADR-001` (`design/abi.md:27`) und `ADR-018` (`TASKLIST.md:130`) werden referenziert und
existieren nirgends. `CONTRIBUTING.md` sagt „non-negotiable until v1.0 ships" (v1.0.0: 2026-08-14,
gelesen) und „Rule 1: until v1.0 is released" — der Text ist abgelaufen. Die Rule-2-Tabelle ist
überholt: „Polymorphism: Interfaces" (Überladung zugelassen, STATUS), „Concurrency:
single-threaded" (Pool-Threads seit 4.2, NL22), „FFI: host-controlled bindings + capability
gating" (`extern "dotnet"` umgeht beides, F1). Fragen: (1) ADR-Verzeichnis, Vorlage,
Nummernkreis, und welche der bereits ausgelieferten Rule-2-Ausnahmen bekommen rückwirkend einen
ADR? (2) Wird CONTRIBUTING für v5 neu geschnitten — Rule 2 mit definiertem Konzeptbegriff (siehe
C-17), Rule 1 mit Ausnahme für Design-Dossiers (STATUS §Still open, viertes Ding)? (3) Gilt der
Scope-Check-Ritus (erster Sonntag) für die v5-Runde, und wann war der letzte?

**B-11 — Wo lebt der Katalog nach der Runde, und wo steht, welche der 966 Fragen entschieden ist?**
Befund: Die Dossiers liegen im Scratchpad (flüchtig, sessiongebunden); `lyric-v5-features.md`
liegt gegen Rule 1 im Repo; PLAN.md, STATUS.md, SPEC-RUNDE.md, `design/*.md` (17 Dateien),
`deliverables/`, `prototypes/` (22) sind sechs weitere Orte. Kein Entscheidungsprotokoll.
Fragen: (1) Eine einzige Quelle der Wahrheit für v5-Entscheidungen (ID, Antwort, Datum, ADR-Link,
Spec-PR-Link)? (2) Werden die Dossiers ins Repo (oder ein `lyric-design`-Repo) überführt, bevor
das Scratchpad verschwindet? (3) Wird `lyric-v5-features.md` nach der Runde gelöscht, weil der
Katalog es ersetzt?

**B-12 — Kann der Spec-Prozess ~300 spec-first-Posten tragen, oder wird 5.0 EINE Spec-Fassung?**
Befund (gezählt): „spec-first/Spec-PR/Regel-PR" 139 Treffer (operatoren 46, generics 20,
interfaces 14), `since:`-Gates 79, Konformanzfälle 69 Nennungen; jeder Posten heute = Regel-PR +
Zwilling + Pin-Ritual. Die Spec hat 14 Kapitel (gelesen) — kein Kapitel für CLI (CLI-30
fordert eins), Manifest (BP-16 D fordert ein Dokument), Embedding/Host-API, Testrunner-Vertrag,
LSP-Erweiterungen, Panik-Katalog (F38). Fragen: (1) Bündelung als „Spec 5.0" (ein Branch, ein
Pin) statt 300 PRs — und was bedeutet das für die Regel „kein since-5.0.0-Fall vor der Regel"?
(2) Welche neuen normativen Dokumente kommen mit 5.0, in welchem Repo? (3) Wer schreibt sie —
die Runde hat für Spec-Text keine Kapazitätsschätzung.

**B-13 — Welche Gebiete schützt die Konformanzsuite nicht, und wird die Matrix vor der v5-Arbeit gefüllt?**
Befund: 178 Fälle (gezählt); COL-38: `LYR-SEM0007` (for-in, `[]`) null Fälle, 11-stdlib zwei;
IF-13: die 4.6-Regel ist nicht gepinnt; T33: Runner widerspricht README („byte-exact" vs
`rstrip`). Fragen: (1) Abdeckungsmatrix Diagnosecode × Gebiet × Fallzahl, veröffentlicht?
(2) Regel: kein Bruch in einem Gebiet ohne vorher gepinnten Ist-Stand (COL-38 sagt es für sich,
niemand für alle)? (3) Läuft die Suite auch gegen `--jit` und `release` (T21, L44 fordern
Zwillinge — wer baut den zweiten Läufer)?

**B-14 — Welches Korpus misst einen 5.0-Bruch, wenn das einzige Programm die stdlib ist?**
Befund (gezählt): stdlib 8 403 Zeilen Lyric, examples 18 410, Konformanzfälle 178; Erato-Skripte
liegen nicht im Checkout (gelesen: `../` hat `lyric`, `lyric-spec`, `lyric-lsp`, Clients, kein
Erato). W42 zählt auf diesem Korpus („8 Structs, 0 mit mut fn") und nennt den Bruch klein.
Fragen: (1) Ist ein Bruch, der im Korpus null Treffer hat, deshalb klein — oder ist der Korpus
zu klein, um irgendetwas zu zeigen? (2) Wird Erato-Code (und der zweite Ring, SL-18) Teil des
Messkorpus, mit einem CI-Job, der jede Uhr dort zählt? (3) Braucht 5.0 ein „großes Programm"
(≥10 k Zeilen) als Dogfood-Artefakt, bevor Brüche bewertet werden?

**B-15 — Ist Lyricpp ein v5-Ziel mit Artefakt, oder ein Gerücht, das die Spec-Runde teurer macht?**
Befund (gezählt): „Lyricpp" in 10 Dossiers (laufzeit 10, skalare 11, strings 10, bytecode 8),
als Adressat für Determinismus (L44, SK-39), Format-Vokabular (SK-34), Konstantenpool (S31),
Stringlänge (S23), Wertdarstellung (L40/41). Laufzeit K13: „nicht im Checkout liegende zweite
Runtime (behauptet)". Fragen: (1) Existiert Lyricpp als Code, wer besitzt ihn, und ist er ein
5.0-Artefakt (Rule 3)? (2) Wenn ja: welche Katalogposten sind dann Artefakte des .NET-Runtimes
und gehören NICHT in die Sprachrunde (JIT, .NET-GC-Knöpfe L39, NativeAOT B23, `extern "dotnet"`)?
(3) Wenn nein: welche Spec-Sätze werden „für zwei Runtimes" geschrieben, ohne dass je einer sie
liest — und ist das der Preis wert?

**B-16 — Wie werden 966 Fragen triagiert, und wer sagt Nein?**
Befund: PLAN.md 4.7 hat acht Fundamentposten + zwölf Ergonomieposten; der Katalog empfiehlt
mehrere hundert Dinge „sofort/4.7/Bugfix". Working mode: Claude plant und implementiert, der
Maintainer reviewt (STATUS §Design decisions, „what to watch is whether the understanding of the
code keeps up with its size"). Fragen: (1) Eine Muss/Kann/Nie-Triage über alle Fragen mit einer
Kapazitätszahl je Welle (Slices/Woche, Review-Stunden)? (2) Welche Empfehlungen sind
„sofort"-Bugfixes, die schon JETZT eine Sweep-Runde bilden (gezählt: ≥40 Posten mit „Sweep",
„4.x-Bugfix", „Konformanzverstoß"), getrennt von Design? (3) Wer hat das Vetorecht gegen Umfang
— derselbe, der die Dossiers geschrieben hat?

**B-17 — Was ist die Release- und Support-Politik nach 5.0?**
Befund (gezählt, `git tag`/`git log`): v1.0.0 2026-08-14, v2.0.0 08-20, v3.0.0 08-23, v4.0.0
08-27 — **vier Majors in dreizehn Tagen**; 78 CHANGELOG-Releases seit v1.0 (16 Wochen); 96 Tags.
„LTS/Langzeit" null Treffer. Fragen: (1) Wie lange bekommt 4.x nach 5.0 Bugfixes, und läuft die
Konformanzsuite dann für zwei Pins? (2) Gibt es eine Zusage „kein Major vor Datum X" nach 5.0 —
ohne sie ist jede Bruchbewertung des Katalogs (major/minor) inhaltsleer, weil ein Major nichts
kostet? (3) Was ist ein Release-Kalender für die 4.x-Wellen (B-03)?

**B-18 — Eine Panik-Matrix statt acht Teilantworten?**
Befund: F33 (Panik läuft keine `defer`), E4/E34 (Debugger hält an), T2/T28 (Runner fängt), F23
(Host sieht), L19/L37 (OOM/CLR-Ausnahmen), NL (Panik in Task), META-29 (Panik in comptime), B2
(Exit-Code), F38 (Katalog der Codes), CLI-12 (Maskierung). Jede Antwort steht in ihrem Gebiet;
was fehlt, ist die Tabelle Kontext × was läuft × wer sieht was × Exit. Frage: Ein Spec-Absatz
„Panik" mit dieser Matrix, Owner Fehler-Gebiet, bevor E4/T2/F23 je für sich bauen?

**B-19 — Ein Determinismus-Kapitel: was ist Zusage, was QoI, was ausdrücklich nicht?**
Befund: Budget zählt Instruktionen (L8/NL30/B22), Floats (L44, SK-39, META-37), Map-Reihenfolge
(L23, COL-28), Hash-Seed (L23), Random (L11), comptime (META-30/37), reproduzierbare Builds
(BP-29), JIT (NL44, L20), Diagnosereihenfolge (D24), Konstantenpool (S31). Zehn Gebiete
versprechen „deterministisch" mit je eigener Reichweite. Fragen: (1) Ein Kapitel, das die
Replay-Zusage (gleiche Eingabe, gleicher Lauf, gleicher Stopp) als EINEN Satz gibt und die
Ausnahmen aufzählt? (2) Ist Replay ein Produktziel (Erato-Editor: Undo/Redo, Tests) oder ein
Nebeneffekt — davon hängt ab, ob L23 C (Host-Randomisierung) je erlaubt ist.

**B-20 — Welche Plattformen sind Tier 1 für 5.0, und was wird nur gebaut, nicht getestet?**
Befund (gelesen, `.github/workflows`): Tests auf ubuntu/windows, Release-Build auch osx-arm64;
MOD-42 (Windows-Gerätenamen), CLI-8 (Case-Insensitivität), A-10 (Konsole), BP-19 (RID), L41
(außerhalb x64), B23 (NativeAOT), E-Clients (VS Code, JetBrains). Fragen: (1) Plattform-Tiers
mit Testzusage (macOS läuft die Suite nicht — behauptet aus der Matrix)? (2) ARM/Linux als
Erato-Ziel? (3) Welche Werkzeuge (lyrls, lyrdbg, REPL) werden auf welcher Plattform getestet?

**B-21 — Was ist eine „Bruch"-Änderung einer BIBLIOTHEK, und wer prüft sie?**
Befund: F27 (throws erweitern = Bruch), OVL-25 (Überladung hinzufügen = kein Bruch, in §4.3a),
IF (Default hinzufügen), MOD-24 (`lyric api` als Textform), BP-02 (Version), FN34
(Parameternamen als Vertrag mit FN01), A-06 (Feld vs Methode). Sechs Gebiete, keine
Kompatibilitätsregel für Bibliotheksautoren (Rust: `cargo semver-checks`, Go: `apidiff`, Swift:
`swift-api-digester`). Fragen: (1) Eine Liste „das ist ein Minor, das ist ein Major" für Lyric-
Bibliotheken in der Spec oder im Manifest-Dokument? (2) `lyric api --diff` als Werkzeug — vor
oder nach dem Paketmanager?

---

## C. Unbequeme Fragen

**C-01 — Wird die freie Überladung in 5.0 zurückgenommen oder auf Arität beschränkt?**
Befund: 41 Fragen im Gebiet; sechs Auswahlalgorithmen statt fünf (OVL-15); Auswahl exponentiell
(49,7 s bei Tiefe 24, OVL-20); Mangling positionsabhängig und von acht Oberflächen gezeigt
(OVL-36); fünf Formatfragen (OVL-09/14/26/36/41); Sichtbarkeit dreimal anders (OVL-19/29); der
std-Suffixabbau hängt daran (OVL-18 „Lackmustest"). STATUS: „knowing it softens Rule 2 … the
shape Oil died of" (gelesen). Der Katalog fragt in 41 Fragen NIE, ob das Feature bleibt.
Fragen: (1) Was hat Überladung seit 3.0 gekauft, das Generics + Constraints + Multi-Konformanz
nicht kaufen (Zählung in stdlib/examples: wie viele echte Überladungssätze außer `slice`)? (2)
Ist eine Beschränkung auf Aritäts-Überladung (Kotlin-nah, kein Ranking) der Kompromiss, der
OVL-01/02/04/05/13/20/28/30 zu Nicht-Fragen macht? (3) Wenn sie bleibt: welcher ADR trägt sie,
und was ist die dokumentierte Obergrenze der Auswahlkosten?

**C-02 — Sind Default-Argumente neben Überladung ein zweiter Mechanismus für „optionales Argument", und sollte einer gehen?**
Befund: FN02 (wo lebt der Default), FN04/FN36 (Interface-Defaults kaputt, major), FN22/23/35
(Trailing/params-Kollisionen), OVL-23 (Regel 3: Zähler), IF-15/40 (Signaturfrage), FN44
(Auswertungsreihenfolge), FN01 (benannte Argumente als drittes Mittel). Go, Rust, Zig haben keine
Defaults; C#, Kotlin, Swift, Python haben sie; Java hat nur Überladung. Frage: Zwei Mechanismen
für eine Frage — welcher bleibt, und was kostet das Streichen von Defaults (Zählung der Defaults
in stdlib/examples) gegen das Streichen der Überladung (C-01)?

**C-03 — Ist der JIT seinen Unterhalt wert?**
Befund (gelesen im Katalog): opt-in (B5); lehnt Closures (FN43: Faktor 16 langsamer), Rekursion
(L35), `yield` (NL36), Wurf/Fang (F37), Enums (B15a → `match` auf beiden langsamen Pfaden),
gemessenen Code (B22) ab; fib(27) im Rauschen (L rev4); Backtrace-Löcher (B6/L20); Budget-
Konflikt (L8, NL44); NativeAOT-Stub schließt Release-JIT aus (B23). Praktisch jeder Alltagscode
(Iteratorketten = Closures, Tasks = yield, Fehler = Wurf) bleibt interpretiert. Kein Dossier fragt
nach Streichen (gezählt: null Treffer). Fragen: (1) Welche Zeile Produktionscode ist heute unter
`--jit` schneller — Zählung über stdlib/examples? (2) Einfrieren bis eine Messung ein Ziel nennt
(B-15: Lyricpp stellt die Frage neu), oder entfernen und die zehn JIT-Fragen (B5/6/15/22/23,
FN17/42/43, L8/20/35, NL36/44, F37) schließen?

**C-04 — Wird `extern "dotnet"` in 5.0 auf Host-registrierte Natives zurückgeschnitten — oder gestrichen?**
Befund (FFI, gemessen dort): Sandbox-Bypass (F1), keine Spec (B2), keine Attribute/Uhr (F25),
untestbar (F28), ungeprüfte Eingangsrichtung (F20/F21), Reentranz-Absturz (F24); .NET-only, ein
zweites Runtime kann es nie erfüllen (B-15); die Bibliotheksumkehr (SL-01/13) und die v5-Liste
(A4 #25, B-Module) bauen darauf. Der Katalog repariert es in 34 Fragen; die Frage „weg damit"
steht nirgends (gezählt: nur `extern "C"`-Symbolzweig „streichen"). Frage: Ist Host-Registrierung
(`RegisterNative`, Native Roots) die eine FFI-Form (Rule 2: „host-controlled bindings"), und
`extern "dotnet"` ein 4.5-Fehler, der mit Uhr geht — welche stdlib-Pläne fallen dann, und ist
das ehrlicher als SL-13 A („Natives, nie extern")?

**C-05 — Für wen laufen die Uhren?**
Befund: Die Warnstufen-Maschinerie (`@Deprecated{until}`, Ratchet SEM0081, §12.5, D19/D20/D22,
lyrfix, 60–100 Uhren, B-03) setzt fremde Codebasen voraus, die über mehrere Minors migrieren.
Korpus: stdlib, examples, Konformanzsuite, Erato (außerhalb) — B-14. CLAUDE.md: „explizit kein
Open-Source-Community-Effort" (gelesen). Fragen: (1) Wer außer dem Maintainer würde eine 4.7-
Warnung sehen? (2) Ist ein harter 5.0-Bruch mit Migrationsguide + einmaligem `lyric fix` (D9)
billiger als drei Minor-Releases Warnstufen, deren Mechanik (Unterdrückung, Gruppen, `renamed`,
Ratchet auf `extern`) selbst zehn Fragen erzeugt? (3) Wenn die Uhren bleiben: als Übung für die
Zeit, in der es fremde Nutzer gibt — dann gehört das so in den ADR.

**C-06 — Lernprojekt oder ausgewachsene Sprache: welches ist es, und was fällt unter der anderen Antwort weg?**
Befund: CLAUDE.md: „persönliches Lernprojekt mit späterem Nutzen … kein Community-Effort";
Auftrag dieser Runde: „ausgewachsene Sprache". Der Katalog plant Paketmanager mit Tarball+Hash
(BP-05), Registry „nicht vor einem Ökosystem", zweiten Bibliotheksring (SL-18), zwei
Editor-Clients, Doc-Generator, `lyric explain`, Deprecation-Gruppen. Fragen: (1) Welche
Katalogposten existieren nur für ein Ökosystem, das es nicht gibt (Registry, Workspaces BP-14,
`--cap-lints` BP-20, D28 „fremd", zwei Clients)? (2) Welche existieren nur für Erato (Reload
B28, DAP-Attach E16, Host-Attribute META-27)? (3) Was bleibt, wenn beides gestrichen ist — und
ist DAS 5.0?

**C-07 — Wenn Erato 2 ein eigenes C++-Runtime bekommt: wer ist der Kunde von `Lyric.Embedding`?**
Befund (Memory: Erato 2 = C++ mit Lyricpp, Erweiterungen als Bytecode; laufzeit K13; B8/B28
nennen Erato als Formatkunden). Das .NET-Embedding (`HostOptions`, `ScriptInstance`, Reload,
`RegisterNative`, `extern "dotnet"`, DAP-Attach, `LangVm`) hat dann keinen bekannten Host außer
`lyrtest`, `lyrrepl` und `lyrdbg`. Fragen: (1) Ist die .NET-VM in 5.0 die Referenzimplementierung
(dann muss Lyricpp ihr folgen, B-15) oder der Werkzeugträger (lyrc/lyrls/lyrtest) mit einer VM
für Tests — dann sind 17 Embedding-Dossier-Treffer Pflege ohne Kunden? (2) Sollten
Embedding-Fragen (L24/25, NL9/35, F23, B5) auf „nach Lyricpp" vertagt werden, statt sie für ein
Runtime zu entscheiden, das Erato verlässt?

**C-08 — Ist 5.0 ein weiterer Korb oder das erste Release mit einer Stabilitätszusage?**
Befund (gezählt): vier Majors in 13 Tagen, 78 Releases in 16 Wochen, „v3 basket" als Muster
(STATUS). Eine „ausgewachsene Sprache" (Auftrag) hat Majors in Jahren. Ohne Zusage ist jede
major/minor-Bewertung im Katalog beliebig: ein Major kostet nichts, also ist kein Bruch teuer.
Fragen: (1) Verspricht 5.0 „kein 6.0 vor Datum X" (und was ist X)? (2) Wenn ja: welche der
heutigen „minor, kann warten"-Empfehlungen werden dadurch zu „jetzt oder nie" (B-04-Matrix)?
(3) Wenn nein: wozu die Uhren (C-05)?

**C-09 — Ist spec-first ein Prozess oder ein nachlaufender Spiegel — und darf 5.0 ohne „kein Tag ohne Spec-Diff" getaggt werden?**
Befund (im Katalog gemessen/gelesen): OP-45 (Display-Interpolation in 4.6.0 gegen §6.6
released, kein Regel-PR, kein Fall), FFI B2 (`extern` seit 4.5 ohne Spec-Kapitel), IF-13 (§5.4
heute verletzt, nicht gepinnt), SK („spec-first an fünf Stellen verletzt"), EP (vier Regeln nur
im Compiler), BP-16 (Projektmodell null Treffer in Grammar.md), G46 (vier Konformanzverstöße von
4.6, heute rot), META-37 (Determinismus-Zusage unbelegt). Fragen: (1) Ehrlich umbenennen
(„spec-follows, Pin nach Release") oder ein hartes Release-Gate (Tag nur mit Spec-Diff und
grünem Zwilling — wer prüft es mechanisch)? (2) Ein Audit „Compiler gegen Spec" als eigene
Sweep-Runde VOR v5, damit die Runde nicht auf einer Spec baut, die 4.6 nicht beschreibt?

**C-10 — Ist `comptime` als Präfixoperator seinen Unterhalt wert?**
Befund (gezählt): `comptime` wird in der stdlib **null**-mal und in examples **einmal**
(`examples/macros/comptime.lyr`) benutzt. META findet: Determinismus-Zusage unbelegt (META-37),
Ergebnistypen eng (META-07), Budget ohne Zahl (META-12), Backtrace fehlt (META-29), Attribute
kennen es nicht (META-06), `embed` braucht eine zweite Form (META-10, ADR-pflichtig), Caching
braucht Toolchain+Host-Version im Schlüssel (META-30). Fragen: (1) Was leistet `comptime`, das
Konstantenfaltung mit Diagnose (OP-44, SK-35) + `embed` + `build.lyr` (BP-17 `-D`) nicht
leisten? (2) Streichen in 5.0 (eine Uhr, ein Beispiel betroffen) oder behalten und dann die
sieben META-Fragen wirklich bezahlen?

**C-11 — Ist `opaque type` als Handle-Darstellung seinen Unterhalt wert?**
Befund: `extend` auf opaque still (STATUS §Still open), opaque über opaque gegen §3.5 (STATUS),
kein Interface möglich (SL-20, Grammar :186-190), FFI-Kreuzung verboten (F4), Literal-Adaption
offen (SK-37), als Typargument (G45), Iterable/Indexable unmöglich (COL-37), Waker/Deskriptor
als opaque (NL3/14/35), Host-Objekt-Identität (F22). SL-20 empfiehlt „Handles als Klassen".
Fragen: (1) Wenn Handles Klassen werden: was bleibt für `opaque` außer dem Newtype-Fall (`Meters
= int`) — und ist ein transparenter Alias mit Konformanzsperre (G15) derselbe Mechanismus? (2) Wie
viele opaque-Deklarationen gibt es (stdlib: `Waker`? Zählung) — rechtfertigen sie sieben
offene Fragen in sechs Gebieten?

**C-12 — Tupel streichen?**
Befund: siehe A-18 — jede Runde findet eine neue Tupel-Lücke, kein Gebiet ist Owner, Structs
mit Synthese (META-14) und `with` (W7) decken den Rückgabefall. Rust und Swift halten Tupel;
Go hat Mehrfachrückgabe; Zig hat anonyme Structs; Kotlin hat `Pair`/`Triple` und rät ab.
Frage: Ist ein Tupel in 5.0 ein voller Werttyp mit allen Pflichten — oder wird es zu einem
anonymen Struct-Literal (Zig-Form), das dieselben Regeln erbt und keine eigenen braucht?

**C-13 — Ist „single-threaded plus Koroutinen" noch wahr, und darf 5.0 `std.task` überhaupt versprechen?**
Befund: Pool-Threads seit 4.2 (NL22 „der Buchstabe ist nicht mehr wörtlich wahr"), Isolates
brauchen ADR (NL15), `std.task` ohne Spec-Abschnitt (NL38), kein Handle (NL1), sticky Waker
(NL33), Sleeper quadratisch (NL17), Frame-Copy 90 ns kalt (NL29), JIT nie (NL36), Generatoren
können in Tasks nicht warten (NL31, VM0015), Cancellation gegen typed throws offen (NL19), `main`
ohne `run()` still (NL26). Fragen: (1) Wird das Nebenläufigkeitsmodell in 5.0 SPEC (Handle,
Abbruch, Bereich, Waker als ein Typ), oder bleibt `std.task` ein Experiment mit „since 5.0"-Fällen?
(2) Wenn Experiment: ist es ehrlicher, `spawn/Wait` bis 6.0 als „instabil" zu markieren, statt
14 NL-Fragen mit Spec-Änderungen zu beantworten? (3) Was heißt Rule 2 „Concurrency" nach 4.2
wörtlich neu (NL15 verlangt die Umformulierung, niemand schreibt sie)?

**C-14 — Für wen ist die Formatstabilität des `.lyrbc`, wenn niemand außer `lyrc` Module erzeugt und alles Whole-Program aus Quelle gebaut wird?**
Befund: B25 (kein getrennter Build), BP-18 (Bibliotheken als Quelle), BP-09 (generische Rümpfe
nicht vorkompilierbar), B1/B16/B17/B26 (Reader validiert nicht — die Formatzusage ist eine Zusage
über die Sprache, nicht die Datei), OVL-14/36 (Mangling), META-02/04/24 (jede Formatrunde macht
Module für alte Hosts unladbar). Erato lädt Erweiterungen als Bytecode (B8) — der eine Kunde.
Fragen: (1) Kosten der Formatzusage seit 1.0 (Spec-Kapitel 13, Reader-Kompatibilität, „v1.0.1
kann keine Source-Map lesen") gegen Nutzen (ein Host, der Bytecode lädt)? (2) Wäre „Format =
Toolchain-Version, kein eigener Vertrag bis Lyricpp" (Python-Modell, das B15 ablehnt) für 5.0
die ehrlichere Zusage?

**C-15 — Wo endet „std ist source-first", wenn `compare`/`hash` auf Strings quadratisch sind?**
Befund: S04/S21 (5,2 s / 2,7 s bei n=40 000; jedes `Map<string,V>` hängt daran), SL-25 (187
ns/Element Kopierschleife, 16 B/Element), SL-13 A („als Natives, nie extern"), Memory: „was in
Lyric geht, wird in Lyric geschrieben". Fragen: (1) Ein Leistungsbudget je std-Kernoperation
(vergleichen, hashen, kopieren, formatieren) mit Messung VOR 5.0, und die Regel, ab wann eine
Operation nativ sein DARF? (2) Ist die Doktrin mit Lyricpp vereinbar (jede native std-Funktion
muss dort in C++ existieren — B-15)? (3) Ist die Bibliotheksumkehr (SL-01: Regex, TLS, Krypto
als .NET-Natives) mit „source-first" überhaupt verträglich, oder ist sie ihr Ende?

**C-16 — Elf Binaries, drei Prozessstarts pro `lyric run`, zwei Editor-Clients, REPL, Profiler, DAP, DocGen, Bench: was wird für 5.0 gestrichen oder eingefroren?**
Befund: CLI-1 (716 ms gegen 189 ms), CLI-25, E5 (REPL: „oder streichen"), E10 (Profiler:
„oder streichen"), E13 (Client-Strategie), tools/Bench („hauseigener Beleg, wie so etwas
kaputtgeht", T18), Lyrstub/Lyrpack (B23), JetBrains-Client (Memory: Checkliste ungefahren).
Kein Dossier fragt nach der Werkzeugliste als Ganzem. Fragen: (1) Welche Werkzeuge haben einen
Nutzer außer dem Maintainer? (2) Liste „bleibt / eingefroren (nur Bugfix) / geht" für 5.0 mit
Begründung — dieselbe Übung wie die stdlib-Entfernungsliste, nur für Werkzeuge. (3) Ein Binary
(`lyric`) mit Subkommandos statt elf (CLI-1 A verteidigt die Trennung aus Auditgründen —
gilt das Argument für lyrfmt, lyrls, lyrrepl, lyrstub?)

**C-17 — Ist Rule 2 noch ein Kriterium oder ein Rhetorikwerkzeug?**
Befund (gezählt): ~260 Nennungen in 24 Dossiers; mehrfach in beide Richtungen für dieselbe
Sache (D22: Gruppen sind ein zweiter Namensraum UND das Rule-2-Argument dafür; META-21 gedreht,
weil die Prämisse falsch war; SK-02 „Modus auf einem Mechanismus"; S12/13 „ein Mechanismus mit
Schaltern"; T14 „gilt für die Sprache, nicht den Runner"; SL K2 „gilt Rule 2 auch für die
Bibliothek?"). Bereits gebilligte Ausnahmen ohne ADR: Überladung, Pool-Threads, `OrThrow`-
Zwillinge (§9.0), `?T` neben Nutzer-Enums (OPT-38), `checked` als Modus, Raw+f+Mehrzeilen-Strings,
`--grant`+`--deny`. Fragen: (1) Braucht Rule 2 einen definierten Konzeptbegriff (was ist EIN
Konzept — „optionales Argument"? „Text aus Werten"? „das geht weg"?) und eine gepflegte Liste
gebilligter Ausnahmen? (2) Wer entscheidet im Streitfall — und ist „Rule 2 sagt" ohne diese
Definition noch ein zulässiges Argument im Katalog?

**C-18 — Hält das Verständnis des Codes mit seiner Größe Schritt — und ist v5 das Release, an dem der Arbeitsmodus bricht?**
Befund (gezählt): 60 397 Zeilen C# in `src/`, 64 044 in `tests/`, 3 328 Testfälle, 886 Commits
in 16 Wochen (869 unter einem Autor-Handle), Working mode „Claude plant und implementiert, der
Maintainer reviewt" mit dem eigenen Warnsatz „what to watch is whether the understanding of the
code keeps up with its size" (STATUS, gelesen). Der Katalog fragt in 966 Fragen nie nach dem
Menschen. Fragen: (1) Welchen Beleg gibt es, dass der Maintainer die Subsysteme, die v5 anfasst
(Overload-Resolver, InstanceTable, Verifier, Scheduler), erklären könnte — ein Review-Protokoll,
selbst geschriebene Slices, ein Architektur-Test? (2) Was ist die Kapazität einer Welle in
Review-Stunden, und wie viele der ~200 „sofort"-Posten passen hinein? (3) Wird der Modus für v5
bewusst bestätigt oder zurückgenommen (CLAUDE.md nennt genau diesen Ort dafür)?

**C-19 — Ist die Fehlerdoktrin (Wert ODER Wurf, mit Zwillingen) ihren Unterhalt wert?**
Befund: SL-04 (drei Namen je fehlbarer Operation: still, `OrThrow`, `OrErr`), F5 (`try?`,
`try e`), F16 (`Result` als Bibliothek, `try e` → Result in 5.0 laut v5-Liste), F18 (Zwillinge
bleiben, weil der JIT den Wurfpfad nie kompiliert), F37, COL-23 (Index: Wert oder Wurf), EP-29
(catch als Pattern), NL19 (Cancelled außerhalb des Vertrags). Fünf Schreibweisen für „das kann
scheitern". SL-04 fragt nach den Namen; niemand fragt nach der Doktrin. Frage: Bleibt „ein Wert
antwortet OB, ein Wurf antwortet WARUM NICHT" (Guide 10) die Regel, obwohl sie jede API
verdoppelt — oder wird 5.0 EINE Form pro Operation verlangen (still ODER werfend, nie beide) mit
`try?` als Brücke, und die Zwillinge gehen?

**C-20 — Warum wird der Sweep nicht VOR der Designrunde fertig?**
Befund: Der Katalog enthält Dutzende Posten, die keine Designfragen sind, sondern gemessene
Defekte in ausgelieferten Releases — CLR-Abstürze (FN Befund 8 `defer { return }` exit 127,
uninit.lyrbc, r31 Instanzkette), Miscompiles (EP r50 64er-Schranke, SK 64-bit-Konstante in
schmalem Slot, `float as char` = 0, `200i8` = -56), stille No-Ops (`extend int[]`, `[x]*n`-
Aliasing, `l[0].v = 9`), Konformanzverstöße (G46 vier Fälle rot), Doppel-Werkzeuge, die sich
widersprechen (T W1–W4: `check` gegen `test`). Die Pipeline (STATUS: Feature → Guide → Release →
Sweep → erst dann nächstes Feature) sagt: kein Feature, solange der Sweep findet. Fragen: (1)
Wie viele der 966 Fragen sind Sweep-Posten (Schätzung ≥ 80) und gehören VOR jede v5-Welle in eine
4.6.x-Patchserie? (2) Ist eine Designrunde auf einem Compiler, der aus gültigem Quelltext
abstürzt, verfrüht — oder ist genau das die Begründung, warum die Runde jeden Befund misst?

---

## Anhang — was geprüft wurde

- Kompaktkatalog `_kompakt-20.json` (10 017 Zeilen) vollständig; die vier Inline-Gebiete
  vollständig.
- Grep über alle 24 Dossiers nach ~60 Stichworten (Labels, Properties, Delegation, Konstruktor,
  BigInt, Container, persistente Sammlungen, Signale, Unicode-Bezeichner, Kodierung, Bitflags,
  lazy, Currying, SemVer, Threat, Konsole, Korpus, Lernkurve, Glossar, ADR, Lyricpp, Erato, JIT/
  Überladung/Tupel/comptime/opaque/extern „streichen", LTS, Positionierung); Kontext gelesen, wo
  ein Treffer zweideutig war.
- Repo: `docs/Grammar.md` (Bezeichner :57, Literale :91-100, Kommentare :47, Labels :374-412,
  MatchArm :386, Keywords :66-73), `docs/guide/05` (:1-40, :68-91), `docs/guide/13` (:7-26,
  :657-663), `stdlib/std/os.lyr` (pub-Fläche), `stdlib/std/` (20 Module), `CONTRIBUTING.md`,
  `STATUS.md` §Still open und §Design decisions, `PLAN.md` :241-320 und :420-470,
  `lyric-v5-features.md` vollständig, `design/loop-labels.md` :1-40, `.github/workflows`
  (Matrix), `../lyric-spec/spec/03-types.md:56` (Division), Tags/Daten via `git`, Zeilenzahlen
  via `wc`, `comptime`-Nutzung via `grep`.
- Nicht geprüft (behauptet): ob Guide-Snippet-Tests Ausgaben vergleichen; ob die Suite auf macOS
  läuft; die genaue Uhrensumme (60–100 ist eine Addition der Dossier-Angaben, keine Zählung im
  Code); Existenz/Stand von Lyricpp und Erato-2-Code.
