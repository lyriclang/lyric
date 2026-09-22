# 13 — `static let` im Enum; statische Interface-Anforderungen

Zugehöriger language-review-Punkt: **"Enums dürfen kein `static let` tragen; Interfaces keine statischen Member und keine Konstanten"** (MEDIUM). Semantik wie dort vorgegeben (Monomorphisierung, nicht über Interface-Wert erreichbar).

## Dateien
| Datei | Status | Zeilen (Code) |
|---|---|---|
| `ist.lyr` | läuft, exit 4 — Modul-Konstanten mit Präfix; Witness-Wert als Extra-Argument | 44 |
| `probe-enum-static-let.lyr` | zeigt `LYR-PAR0040` | 6 |
| `soll.lyr` | PROPOSAL | 40 |
| `vergleich.rs` | Rust (assoziierte Konstanten/Funktionen, `T::zero()`), Swift als Kommentar | 32 |
| Lib-Variante | (b) ist bibliotheksseitig nur als Witness-Muster möglich — das IST der Ist-Stand | — |

## Ist-Stand
(a) Ein Enum kann `static fn`, aber kein `static let` (`Parser.Declarations.cs:644`, PAR0040) —
die zugehörigen Konstanten wandern als `LEVEL_LOW_MAX` auf Modulebene, mit Präfix als
Namensraum-Ersatz, und stehen im Enum-Body als freie Namen ohne Bezug. (b) Ein generisches
`sum<T>` braucht ein neutrales Element; ohne statische Anforderung reist ein **Witness**
(`IntMonoid { }`, `Vec2Monoid { }`) als Extra-Argument mit — ein zweiter Typ pro Typ, ein
zweiter Typparameter `M`, und der Aufrufer muss den passenden Witness kennen. Es funktioniert
(Monomorphisierung macht daraus direkte Aufrufe), ist aber die Haskell-Dictionary-Übersetzung
von Hand.

## Soll-Syntax und Grammatik
- **(a)** §3.4: `EnumBody = EnumVariant { ',' EnumVariant } [ ',' ] [ ';' { EnumMember } ]`,
  `EnumMember = FunctionDecl | StaticBinding`. Parser: `ParseEnum` liest nach dem `;` heute nur
  Funktionen; `StaticBinding` wird wie in `ParseStructBody` (`:478`) gelesen. Semantik wie §3.2
  (Initialisierung vor `main`, Deklarationsreihenfolge). Ein Initializer darf Varianten des eigenen
  Enums bauen (`Level.Mid`) — die Variante ist eine Konstante, kein Zyklus. **Eindeutig**: nach
  dem `;` ist `static` heute ein Fehler.
- **(b)** §3.5: `InterfaceMember = FunctionDecl` — `static` parst bereits (`FunctionDecl` §3.1),
  `Parser.Declarations.cs:652` lehnt es ab. Vorschlag: zulassen als **statische Anforderung**:
  - konformer Typ muss `static fn` gleicher Signatur haben (Prüfung neben der Instanz-Prüfung,
    `TypeChecker.cs` Konformanzprüfung, LYR-SEM0020-Familie);
  - `T.zero()` bei `T :: [Zero<T>]` ist ein direkter Aufruf nach Monomorphisierung (§8.1);
    Sema: Member-Lookup auf einem Typparameter für `TypePath '.' IDENTIFIER` (heute nur auf
    konkreten Typen, §8.4);
  - über einen Interface-WERT nicht erreichbar (kein Receiver → `LYR-SEM-neu` mit Hinweis);
    damit bleibt die §3.5-Begründung ("a vtable slot takes a receiver") für Werte wahr, und der
    Interface-WERT braucht keinen neuen Slot — die Methodentabelle ändert sich NICHT (Format 4.0
    unberührt);
  - `Self`-Typwort ist NICHT nötig: `interface Zero<T> { static fn zero(): T; }` mit
    `Vec2 :: [Zero<Vec2>]` ist das bestehende Muster (`Equatable<T>`), konsistent mit §5.
  - `static let` im Interface (Rust `const ZERO: Self`): dieselbe Regel, `T.ZERO` — Vorschlag:
    in Runde 1 nur `static fn`, weil ein statisches `let` in einem Interface ohne Body eine
    Deklaration ohne Initializer ist (§7.1 verbietet das für `let`) — bräuchte eine Sonderform.
- **Spec**: §3.4/§3.5 Grammatik, §5.1 (statische Anforderungen, Nicht-Erreichbarkeit über Werte),
  §8.2 (`T.member` auf Typparametern), §8.4 (Statics auf Instanzen: `List<int>.empty()` ist das
  Vorbild).

## Bewertung
| | Ist | Soll | Rust |
|---|---|---|---|
| Enum-Konstante | Modul-`let` mit Präfix, 2 Z. außerhalb | `static let` im Body | `const` im `impl` |
| Monoid-Summe | Witness-Struct pro Typ (1 Z.) + Extra-Typparameter + Extra-Argument | `T.zero()` | `T::zero()` |
| `sum` Signatur | `<T :: [Add<T,T>], M :: [Monoid<T>]>(xs: T[], m: M)` | `<T :: [Add<T,T>, Zero<T>]>(xs: T[])` | `<T: Add + Zero>` |

- **Fehlerklassen verhindert**: (a) Konstante und Enum laufen auseinander (Präfix ist Konvention,
  kein Bezug); (b) falscher Witness für einen Typ (`sum(vecs, IntMonoid {})` ist ein Typfehler,
  aber `sum(ints, OtherIntMonoid {})` mit anderem `zero` nicht) — mit statischer Anforderung
  gibt es genau eine Antwort pro Typ.
- **Aufwand**: (a) Parser ~10 Z., Sema 0 (StaticBinding-Pfad existiert für Structs), Lowering 0
  (Globale Initialisierung existiert). (b) Parser ~3 Z. (Ablehnung entfernen), Sema mittel
  (~120 Z.: Konformanzprüfung für Statics, `T.f()`-Lookup auf Typparametern, Ablehnung über
  Werte), Lowering klein (~30 Z.: Instanz-Auflösung des statischen Members bei
  Monomorphisierung — `InstanceTable` hat den Mechanismus für Instanzmethoden), VM 0,
  Format 0 (keine vtable-Änderung).
- **Breaking**: nein (Minor), beide Teile additiv.
- **Wechselwirkungen**: Generics (§8.3 Inferenz: `T.zero()` bindet T NICHT — T muss aus
  anderen Argumenten oder explizit kommen: `sum<Vec2>([])` für ein leeres Array); Interface-Werte
  (ausgeschlossen, s.o.); Interface-Vererbung (§5.2: statische Anforderungen erben wie
  Instanz-Anforderungen); Default-Bodies (erlaubt, direkter Aufruf); Overloading (§4.3a: statische
  Anforderungen dürfen nicht überladen werden — wie Interface-Member heute).
- **Offene Semantikfragen**: (1) `static let` in Interfaces — Runde 2. (2) `Self` als Typwort
  (Swift/Rust) — nicht nötig, könnte aber Boilerplate `Zero<Vec2>` sparen; eigener Vorschlag.
  (3) Darf ein `extend` einem BUILTIN eine statische Anforderung geben (`extend int :: [Zero<int>]
  { static fn zero() }`)? Ja — `extend` erlaubt heute Instanzmethoden auf `int` (Guide 7); Statics
  folgen derselben Registrierung.

## Empfehlung
**(a) sofort (Minor, Grammatik-Fußnote). (b) Sprachfeature, Minor, mittlerer Aufwand** — der
größte Nutznießer ist die stdlib (`Iterator`-Fabriken, `Default`-artige Konstruktion,
`Zero`/`One` für numerische Generics), daher stdlib-review informiert.
