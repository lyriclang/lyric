# Member-Sichtbarkeit: `pub` auf Feldern und Methoden, `pub(module)`

**Status:** designt (Major — der einzige Vorschlag meiner Liste mit Deprecation-Uhr)
**Version:** **Major (5.0)** mit 4.x-Warnstufe — heute ist jedes Member eines `pub`-Typs öffentlich
**Spec:** §3.2/§3.3 (Grammatik der Member), §4.2 (Sichtbarkeit), §5.1 (Konformanz mit privaten Membern), Appendix A (zwei neue Codes) · **Guide:** 05, 12
**Abhängigkeiten:** keine technische; die stdlib muss vorher umgebaut sein (stdlib-redesign hat die Feldklassifikation und die Map/Set-Iteratoren auf Methoden umgestellt).
**Abgestimmt mit:** stdlib-redesign (fordert `pub(module)` für vier Stellen in `collections.lyr`, hat `prototypes/stdlib-field-visibility.md` mit der Klassifikation aller stdlib-Felder geliefert)

## Motivation

Lyric ist gegenüber **allen acht** Vergleichssprachen im Rückstand: ein `pub struct` oder `pub class`
veröffentlicht **jedes** Feld und **jede** Methode. Folgen:

- Kein Typ kann eine Invariante halten. `List` hat `items` und `count` öffentlich; ein Nutzer, der
  `count` setzt, zerstört sie — und der Compiler sagt nichts.
- Jede interne Umbenennung ist ein Breaking Change. Die stdlib kann `List.items` nicht in `storage`
  umbenennen, ohne eine Major-Version zu ziehen.
- Ein Modul kann keine Hilfsmethode haben, die nicht Teil seiner API ist.

`std.collections` allein hat **19 öffentliche Felder**, von denen nach stdlib-redesigns Klassifikation
**keines** öffentlich sein sollte.

## Syntax

```
Member      = [ Visibility ] ( FieldDecl | FunctionDecl ) .
Visibility  = 'pub' [ '(' 'module' ')' ] .
FieldDecl   = IDENTIFIER ':' TypeExpr [ '=' Expr ] .
```

```lyr
pub class Counter {
    pub name: string,           // Teil der API
    count: int,                 // privat: nur dieses Modul
    pub(module) cursor: int,    // modulweit: Map/Set-Iteratoren lesen es

    pub fn value(): int { return this.count; }
    fn invariant(): bool { return this.count >= 0; }   // privat
}
```

- `pub` ist bereits Schlüsselwort; `pub(module)` ist eine Klammerform davon — `module` bleibt
  kontextuell (kein neues reserviertes Wort), weil in dieser Position heute nichts stehen kann.
- **Vorbild Rust** (`pub(crate)`). Swift hat fünf Stufen (`private`/`fileprivate`/`internal`/
  `public`/`open`) — eine davon (`fileprivate`) ist ein bekannter Konstruktionsfehler, weil sie an
  Dateien statt an Modulen hängt. Lyric bekommt **zwei** Stufen plus Default.

## Semantik

### Die drei Stufen

| Schreibweise | Sichtbar |
|---|---|
| (nichts) | nur im **deklarierenden Modul** (und in `extend`-Blöcken desselben Moduls) |
| `pub(module)` | im deklarierenden Modul **und** in seinen Untermodulen (`std.collections` → `std.collections.internal`) |
| `pub` | überall, wo der Typ sichtbar ist |

Der **Default ist privat** — anders als heute. Das ist die Breaking-Entscheidung: Rust, Swift, Kotlin,
C#, Java machen es so; Go koppelt es an die Groß-/Kleinschreibung; TypeScript und Python haben
Konventionen statt Regeln.

### Wer darf was

- **Lesen und Schreiben** eines privaten Feldes: nur Code im deklarierenden Modul. Fehler sonst:
  `LYR-SEM-neu: 'count' is private to 'std.collections'` — mit dem Hinweis auf eine öffentliche
  Alternative, wenn eine gleichnamige Methode existiert (`use 'count()'`).
- **Initializer:** `Counter { name = "a", count = 0 }` außerhalb des Moduls ist ein Fehler, sobald ein
  Feld privat ist. Das ist die **schärfste** Konsequenz und deckt sich mit `LYR-SEM0093` (opaque types:
  „making a 'X' is its declaring module's privilege"). Ein Modul, das Konstruktion erlauben will, bietet
  eine `pub static fn` an. Diese Regel existiert also schon einmal im Compiler, mit derselben Begründung.
- **Konformanz (§5.1):** eine Interface-Methode ist implizit `pub` — ein privates Member kann eine
  Konformanz **nicht** erfüllen (sonst wäre die vtable-Zeile von außen aufrufbar, die Quelle aber
  privat). Eine Konformanz, die auf einer privaten Methode desselben Namens sitzt, ist ein Fehler mit
  klarer Meldung.
- **Patterns (§7.6):** ein Feld-Pattern `Point { x, y }` außerhalb des Moduls darf nur öffentliche
  Felder nennen — sonst wäre Destrukturierung ein Loch in der Kapselung. (Swift und Rust behandeln das
  genauso.)
- **DocGen:** private Member erscheinen nicht.

### Die Deprecation-Uhr (4.x → 5.0)

Das Muster steht in `LYR-SEM0093` (opaque casts: Warnung ab 3.8, Fehler ab 4.0) und ist bewährt:

1. **4.5**: `pub`/`pub(module)` auf Membern wird **geparst und geprüft**, aber ein fehlendes `pub` ist
   noch nicht restriktiv. Ein Zugriff auf ein Member ohne `pub` aus einem fremden Modul erzeugt eine
   **Warnung** (`LYR-SEM-neu`, W). Die stdlib wird vollständig annotiert.
2. **4.6**: die Warnung nennt die Release-Nummer, ab der sie ein Fehler ist (wie `@Deprecated`'s `until`).
3. **5.0**: Fehler. Default wird privat.

Damit hat jedes Projekt zwei Minor-Zyklen Zeit, und `lyric check --deny-warnings` macht die Umstellung
mechanisch prüfbar.

### Lowering

**Null Laufzeitwirkung.** Sichtbarkeit ist eine reine Sema-Frage; die IR kennt keine privaten Felder,
und das Bytecode-Format ändert sich nicht. Einzige Stelle: die **Reachability** (`Reachability.cs`, seit
2.0 „a library's surface decides its contents") nimmt heute alle `pub`-Funktionen als Wurzeln — mit
privaten Methoden werden es **weniger** Wurzeln, also kleinere Bibliotheken. Ein angenehmer Nebeneffekt,
aber er muss getestet werden (eine private Methode, die nur über eine vtable erreichbar ist, darf nicht
wegfallen).

## Wechselwirkungen

- **Opaque types (§3.5):** `opaque type` ist die grobkörnige Variante desselben Gedankens (ein ganzer Typ
  wird gekapselt); Member-Sichtbarkeit ist die feinkörnige. Beide bleiben — die Begründung von SEM0093
  überträgt sich wörtlich auf den Initializer privater Felder.
- **Attribute:** `@Deprecated` auf Membern (stdlib-redesign fordert `OnMethod`/`OnField`-Anker) ist das
  **Übergangswerkzeug**: ein Feld, das 5.0 privat wird, bekommt in 4.x `@Deprecated { until = "5.0" }`.
  Die beiden Features gehören in dieselbe Release-Planung.
- **Konformanz-Synthese:** ein synthetisiertes `equals` liest **alle** Felder, auch private — es steht im
  deklarierenden Modul, also ist das konsistent.
- **Patterns:** siehe oben (pattern-lambda informiert).
- **LSP:** Completion darf private Member außerhalb des Moduls nicht vorschlagen — spürbare
  Verbesserung, heute ist jede Completion-Liste voll mit Interna.
- **Coroutinen/throws/Generics:** keine.

## Breaking — ja, und deshalb 5.0

Jedes Programm, das heute auf ein Feld eines fremden Typs zugreift, bricht — **außer** der Autor
schreibt `pub` davor. Die Uhr macht den Bruch ankündbar; ohne sie wäre das Feature für v1 zu teuer.

## Aufwandsschätzung

| Ebene | Aufwand |
|---|---|
| Lexer | 0 |
| Parser | klein (~40 Z.: `Visibility` vor Membern, `pub(module)`) |
| Resolver | klein (~30 Z.: `Visibility` am Member-Symbol; `Visibility.Module` existiert bereits für Top-Level) |
| Sema | **mittel** (~150 Z.: Zugriffsprüfung bei Member-Ausdruck, Initializer, Pattern, Konformanz; Warn-/Fehlerstufe an der Version) |
| Lowering | 0 (Reachability-Wurzeln prüfen) |
| VM/Bytecode | 0 |
| stdlib | **groß**: alle Felder klassifizieren (liegt vor), Map/Set-Iteratoren auf Methoden (erledigt) |
| Tooling | LSP-Completion, DocGen |

## Vergleich

| Sprache | Stufen | Default | Besonderheit |
|---|---|---|---|
| **Rust** | `pub`, `pub(crate)`, `pub(super)`, `pub(in path)`, privat | privat | Feinste Kontrolle; `pub(crate)` ist die meistgenutzte. Vorbild für `pub(module)`. |
| **Swift** | `private`, `fileprivate`, `internal`, `public`, `open` | `internal` (Modul) | `fileprivate` hängt an Dateien statt Modulen — von der Community als Fehler betrachtet. `open` trennt „sichtbar" von „überschreibbar" (für Lyric irrelevant: keine Vererbung). |
| **Kotlin** | `private`, `protected`, `internal`, `public` | `public` | `internal` ≈ Modul; `public` als Default ist der Punkt, den Kotlin selbst bereut hat. |
| **C#** | `private`, `protected`, `internal`, `public` (+Kombis) | `private` | `internal` ≈ Assembly. |
| **Go** | Groß-/Kleinschreibung | paketprivat | **Zwei Stufen, null Syntax** — die knappste Lösung, aber der Name trägt die Semantik, und Umbenennen ändert die Sichtbarkeit. |
| **TypeScript** | `private`/`protected`/`public` (nur Typprüfung), `#field` (echt) | `public` | `private` ist zur Laufzeit wirkungslos — zwei Mechanismen für eine Sache. |
| **Python** | Konvention `_name`, Name-Mangling `__name` | öffentlich | Keine Regel. |
| **Zig** | `pub` für Deklarationen; Struct-Felder sind **immer** öffentlich | — | Zig hat exakt Lyrics heutiges Problem und lebt damit. |

Rust:
```rust
pub struct Counter {
    pub name: String,
    count: i64,              // privat: nur dieses Modul
    pub(crate) cursor: usize,
}
// Ein fremdes Crate kann Counter nicht literal konstruieren, solange ein Feld privat ist.
```

**Fallen, die Lyric vermeidet:** (1) Swifts `fileprivate` (Datei statt Modul); (2) Kotlins `public` als
Default; (3) TypeScripts wirkungsloses `private` neben `#field`; (4) Gos Kopplung an die Schreibweise;
(5) C#' Explosion aus Kombinationen (`private protected internal`).

**Empfehlung: Rust-Modell in Lyric-Schreibweise** — `pub`, `pub(module)`, Default privat, Uhr über zwei
Minor-Releases. `pub(module)` ist kein Luxus: stdlib-redesign hat **vier konkrete Stellen**
(`MapKeyIterator`, `MapValueIterator`, `MapEntryIterator`, `SetIterator`), an denen ein Iterator die
Felder des Containers liest; ohne die mittlere Stufe müsste die stdlib entweder alles öffentlich lassen
oder Snapshots kopieren (O(n) Speicher pro Iteration).

## Offene Fragen

1. Ist `pub(module)` **abwärts** (Untermodule) oder **aufwärts** (Elternmodul) sichtbar? Vorschlag:
   abwärts, wie Rusts `pub(crate)` innerhalb der Crate — `std.collections.internal` sieht
   `std.collections`. Rusts `pub(super)` (aufwärts) braucht Lyric nicht.
2. Soll ein privates Feld in einem **Initializer desselben Moduls** genügen, oder braucht jeder Typ mit
   privaten Feldern eine Konstruktorfunktion? Vorschlag: Initializer im Modul bleibt erlaubt.
3. `pub` auf **Enum-Varianten**? Vorschlag: nein — ein Enum ist seine Variantenliste; wer Varianten
   verstecken will, nimmt `@NonExhaustive` (pattern-lambda) oder einen opaken Typ.
4. Sollen **Interface-Member** eine Sichtbarkeit tragen können? Nein: ein Interface ist ein Vertrag.

## Spec-Diff

§3.2/§3.3:
```diff
-Member = ( FieldDecl | FunctionDecl ) .
+Member     = [ Visibility ] ( FieldDecl | FunctionDecl ) .
+Visibility = 'pub' [ '(' 'module' ')' ] .
```
§4.2:
```diff
 Only `pub` declarations cross a module boundary.
+The same holds for the MEMBERS of a type: a field or method without `pub` is visible in its
+declaring module alone, `pub(module)` adds that module's submodules, and `pub` publishes it
+wherever the type is visible. A struct initializer written outside the declaring module may
+name only public fields, and a type with a private field therefore cannot be assembled
+elsewhere — the rule `LYR-SEM0093` already states for opaque types, for the same reason. A
+field pattern (§7.6) may likewise name only what is visible. An interface method is public by
+definition: a private member cannot satisfy a conformance.
+
+*Until 5.0 a missing `pub` is a WARNING at the foreign access, not an error, and names the
+release that makes it one — the clock `LYR-SEM0093` used.*
```
Appendix A: zwei neue Zeilen (privates Member von außen — W bis 5.0, dann E; Konformanz auf privatem Member — E).
