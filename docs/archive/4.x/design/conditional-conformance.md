# Bedingte Konformanz: `extend<T :: [Display]> List<T> :: [Display]`

**Status:** designt (von stdlib-redesign als Blocker gemeldet)
**Version:** Minor (4.5/4.6) — additiv; heute ist jedes generische `extend`-Ziel `LYR-SEM0047`
**Spec:** §2 (`ExtendDecl`), §5.1/§5.5 (Konformanz und Orphan-Regel), §8 (Constraints) , Appendix A (SEM0047 einschränken) · **Guide:** 07, 15
**Abhängigkeiten:** keine harte; entfaltet ihren Wert mit `design/fstring-display.md` (implementiert) und `design/conformance-synthesis.md`.
**Abgestimmt mit:** stdlib-redesign (sie brauchen es für `Display` auf `List`/`Set`/`Map`/`Result`; ihre Alternative — eine Synthese-Sonderregel „`List<T>` ist Display gdw. `T` Display" — habe ich abgelehnt, weil sie eine Compiler-Regel pro Containertyp wäre)

## Motivation

`println([1, 2, 3])` und `println(xs)` für eine `List<int>` sind der häufigste Wunsch nach einer
Debug-Ausgabe. Heute gilt:

- `List<T>` kann **nicht** `Display` deklarieren, denn `show()` müsste die Elemente rendern — und die
  haben nur dann ein `show()`, wenn `T` selbst `Display` ist. Eine unbedingte Konformanz wäre für
  `List<Socket>` eine Lüge.
- Ein `extend List<T> :: [Display]` ist heute `LYR-SEM0047`: „extend target must be a plain named type in
  v1 (no generic, array, tuple or function targets)".
- Die Folge steht in der Usability-Analyse als P0-Bug 8: `println([1,2,3])` **passiert die
  Display-Constraint** und scheitert erst im IR mit Fehlerort `console.lyr:51` — der Nutzer sieht einen
  Fehler in der stdlib.

Ohne dieses Feature kann stdlib-redesign `Display` für Container nicht typsicher anbieten.

## Syntax

```
ExtendDecl = 'extend' [ GenericParams ] TypeExpr [ '::' '[' TypeExpr { ',' TypeExpr } ']' ] '{' { Member } '}' .
GenericParams = '<' GenericParam { ',' GenericParam } '>' .
GenericParam  = IDENTIFIER [ '::' '[' TypeExpr { ',' TypeExpr } ']' ] .
```

```lyr
extend<T :: [Display]> List<T> :: [Display] {
    fn show(): string { … }
}
```

**Die Typparameter stehen vor dem Ziel, nicht im Ziel.** Die naheliegende Form
`extend List<T :: [Display]> :: [Display]` ist nicht parsebar: der Parser liest `List<…>` als
Typargumentliste, und `T :: [Display]` ist dort kein Typ (gemessen: sieben Folgefehler ab
`LYR-PAR0009`). Rust macht es genauso (`impl<T: Display> Display for Vec<T>`), und aus demselben Grund.

`extend` ohne Parameterliste bleibt exakt wie heute.

## Semantik

- **Bindung:** die Parameter der Liste binden im Ziel und in den Membern. Jeder deklarierte Parameter
  muss im Ziel **vorkommen** (`extend<U> List<int>` ist sinnlos → Fehler), und das Ziel muss die
  Parameter **linear** verwenden (jeder genau einmal): `extend<T> Map<T, T>` wäre eine
  Überlappungsquelle und wird refused. Damit bleibt die Instanzsuche eine reine Strukturgleichheit.
- **Konformanz-Frage** (`Satisfies`, `TypeChecker.cs:3205`): „erfüllt `List<int>` die Constraint
  `Display`?" wird beantwortet, indem der Block gegen `List<int>` **unifiziert** wird (`T = int`) und
  danach die Constraints des Blocks geprüft werden (`int :: [Display]` — ja). Für `List<Socket>`
  scheitert die Constraint, und die Antwort ist „nein" — mit einer Diagnose, die **den Grund nennt**:
  `'List<Socket>' does not conform to 'Display': the conditional conformance requires 'Socket :: [Display]'`.
- **Keine Überlappung:** zwei Blöcke, die dieselbe Konformanz für dasselbe Ziel bei überlappenden
  Belegungen anbieten, sind ein Fehler. Da Lyric keine Spezialisierung hat (und sie auch nicht bekommen
  soll — Rusts Spezialisierung ist seit 2015 unstabil), ist die Regel einfach: **pro (Zieldefinition,
  Interface, Argumentbelegung) genau ein Block**, geprüft wie die bestehende Regel „eine Liste darf sich
  nicht wiederholen" (§5.1, SEM0078).
- **Orphan-Regel (§5.5) unverändert:** der Block muss im Modul der Zieldefinition oder im Modul des
  Interfaces stehen. `extend<T> List<T> :: [Display]` gehört damit in `std.collections` oder `std.core` —
  genau dorthin, wo stdlib-redesign ihn haben will.
- **Vererbung der Constraints in die Member:** eine Methode des Blocks darf die Constraints benutzen
  (`T.show()`), und nur diese — wie in jeder generischen Funktion.
- **Lowering:** ein bedingter Block ist eine **Familie von Extension-Methoden**, eine pro benutzter
  Instanz. Die `ExtensionTable` hängt heute an `(TypeSymbol, Modul)`; sie muss an
  `(TypeSymbol, Argumentbelegung, Modul)` hängen, und die Monomorphisierung erzeugt `List<int>.show`
  wie sie `List<int>.push` erzeugt. Das ist der Hauptaufwand — **der Mechanismus existiert**
  (`InstanceTable`), nur die Extension-Seite kennt ihn noch nicht.
- **Vtable:** eine bedingte Konformanz ist genauso eine vtable-Zeile wie eine unbedingte, nur pro
  Instanz. `let d: Display = myIntList;` funktioniert damit.

### Nicht enthalten

- **Bedingte Methoden ohne Konformanz** (`extend<T :: [Ordered<T>]> List<T> { fn sort(): void }`) — fällt
  syntaktisch mit ab und sollte im selben Zug erlaubt werden; Rust nutzt das massiv (`impl<T: Ord> Vec<T>`).
- Arrays/Tupel als Ziel (`extend<T> T[] :: [Display]`): **separat entscheiden.** Es wäre die sauberste
  Lösung für `println([1,2,3])`, berührt aber §3.3 (Arrays sind keine nominalen Typen). Vorschlag:
  in einem zweiten Schritt, nach der generischen Form.

## Wechselwirkungen

- **f-Strings** (implementiert): `f"{xs}"` für `xs: List<int>` funktioniert, sobald der Block existiert —
  ohne jede Compiler-Änderung, weil das Loch nur `Satisfies` fragt.
- **Konformanz-Synthese:** eine bedingte Konformanz kann **nicht** synthetisiert werden (der Body muss
  über die Elemente iterieren, was eine Bibliotheksentscheidung ist). Beide Features sind komplementär:
  Synthese für Wertetypen mit Feldern, bedingte Konformanz für Container.
- **Generics (§8):** die Constraint-Prüfung wird rekursiv (`List<List<int>> :: [Display]` braucht
  `List<int> :: [Display]` braucht `int :: [Display]`). **Rekursionstiefe begrenzen** (Vorschlag: 32,
  mit Diagnose), sonst findet jemand einen Typ, der den Compiler nicht terminieren lässt — Rust hat dafür
  den `recursion_limit` und Swift eine feste Tiefe.
- **Coroutinen/throws/Patterns:** keine.
- **LSP/DocGen:** die Konformanz erscheint am generischen Typ mit ihrer Bedingung.

## Breaking

Nein (Minor). Heute ist jedes generische `extend`-Ziel ein Fehler.

## Aufwandsschätzung

| Ebene | Aufwand |
|---|---|
| Parser | klein (~30 Z.: `GenericParams` vor dem Ziel, die Produktion existiert für Funktionen) |
| Resolver | klein (~30 Z.: Parameter im Block-Scope deklarieren) |
| Sema | **mittel-groß** (~200 Z.: Unifikation Ziel↔Anfrage in `Satisfies`, Constraint-Prüfung nach Substitution, Linearität, Überlappung, Rekursionsgrenze, neue Diagnose mit Grund) |
| Lowering | **mittel** (~150 Z.: `ExtensionTable` auf Argumentbelegung erweitern, Instanz-Erzeugung anstoßen) |
| VM/Bytecode | 0 |
| stdlib | `Display` für `List`/`Set`/`Map`/`Deque`/`Result`/`?T` (stdlib-redesign) |

## Vergleich

| Sprache | Form | Entscheidung |
|---|---|---|
| **Rust** | `impl<T: Display> Display for Vec<T>` | **Vorbild für die Syntax.** Orphan-Regel (`impl` nur im Crate des Typs oder des Traits) verhindert Konflikte; Kohärenz ist global geprüft. Spezialisierung fehlt bis heute. |
| **Swift** | `extension Array: CustomStringConvertible where Element: CustomStringConvertible` | `where`-Klausel **hinter** dem Ziel — lesbarer als Rust, aber Swift kann `where` an vielen Stellen, was die Grammatik aufbläht. |
| **Haskell** | `instance Show a => Show [a]` | Die Urform; Kontexte sind erstklassig. |
| **Kotlin/C#** | keine bedingte Konformanz — `List<T>.toString()` ist eine Methode, die zur Laufzeit `toString` der Elemente ruft | Funktioniert, weil **jeder** Typ `toString` hat: die Frage stellt sich nie, und die Antwort ist notfalls `Foo@1a2b3c`. |
| **Go** | Interfaces sind strukturell: `[]T` implementiert nichts, ein `Stringer` muss von Hand geschrieben werden | Kein Mechanismus; `fmt.Println` löst es per Reflexion. |
| **Zig** | `comptime`-Duck-Typing: `std.fmt` prüft zur Compile-Zeit, ob der Typ eine `format`-Funktion hat | Keine Konformanzen, dafür Fehlermeldungen aus dem Inneren der Bibliothek — genau die Fehlerklasse, die Lyric hier beseitigen will. |

Rust:
```rust
impl<T: fmt::Display> fmt::Display for MyList<T> {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "[{}]", self.items.iter().map(|x| x.to_string()).collect::<Vec<_>>().join(", "))
    }
}
println!("{}", list_of_ints);     // ok
println!("{}", list_of_sockets);  // Compile-Fehler AM AUFRUF, mit Nennung von Socket
```
Swift:
```swift
extension Array: CustomStringConvertible where Element: CustomStringConvertible {
    public var description: String { "[" + map(\.description).joined(separator: ", ") + "]" }
}
```

**Fallen, die Lyric vermeidet:** (1) Kotlins/C#' „jeder Typ hat `toString`" — bequem, aber die Ausgabe
`Foo@1a2b3c` ist wertlos und erscheint erst im Log; (2) Zigs comptime-Duck-Typing mit Fehlern aus dem
Bibliotheksinneren — **das ist exakt Lyrics heutiger P0-Bug 8** (`console.lyr:51`), und dieses Feature
ist sein Gegenmittel; (3) Rusts Spezialisierung — bewusst nicht übernehmen, solange sie nicht einmal in
Rust stabil ist.

**Empfehlung: Rusts Syntax mit Swifts Lesbarkeit im Blick** — `extend<T :: [Display]> List<T> :: [Display]`,
also Parameter vorn (parsebar) und Constraints in Lyrics bestehender `::`-Schreibweise (kein neues
`where`). Die Orphan-Regel bleibt, wie sie ist. **Reihenfolge:** zuerst die Sema-Antwort auf
„erfüllt `List<int>` die Constraint" (damit `println(xs)` einen Fehler **am Aufruf** gibt statt in der
stdlib), dann das Lowering.

## Offene Fragen

1. **Arrays und Tupel als Ziel** — zweiter Schritt (s.o.). Bis dahin bleibt `println([1,2,3])` ein
   Fehler, aber ein **verständlicher**, sobald `Satisfies` strenger antwortet.
2. **Bedingte Methoden ohne Konformanz** im selben Zug? Vorschlag: ja, es ist dieselbe Grammatik.
3. **Rekursionsgrenze** 32 oder konfigurierbar? Vorschlag: fest, mit Diagnose.
4. **`?T :: [Display]` bedingt definierbar? ENTSCHIEDEN: nein** (mit stdlib-redesign abgestimmt, von
   ihnen angenommen). Es würde `f"{opt}"` erlauben und der Entscheidung in `design/fstring-display.md`
   („`?T` rendert nicht") widersprechen; Rust hält es genauso (`Option<T>` ist `Debug`, nicht
   `Display`). Wer eine Abwesenheit drucken will, schreibt `?? "none"` oder ruft
   `std.option.showOptional(o, ifNone)`, das stdlib-redesign für 4.5 aufnimmt. **Die Ausnahme ist der
   Testbericht:** `assertEq` rendert Optionals selbst und schreibt `null` als sichtbares Wort — dort ist
   genau das gewollt, was in Programmausgabe eine Abwesenheit verstecken würde. Der Unterschied liegt
   im Leser, nicht im Typ, und deshalb gehört er in eine Funktion und nicht in eine Konformanz.

## Spec-Diff

§2:
```diff
-ExtendDecl      = 'extend' TypeExpr [ '::' ConformanceList ] '{' { Member } '}' .
+ExtendDecl      = 'extend' [ GenericParams ] TypeExpr [ '::' ConformanceList ] '{' { Member } '}' .
```
§5.5 (Extensions):
```diff
 Extension methods are visible where the declaring module is imported (§4.2).
+An extend block may carry GENERIC PARAMETERS, and its target may then be a generic instance of
+them: `extend<T :: [Display]> List<T> :: [Display]`. The conformance it declares is
+CONDITIONAL — `List<int>` conforms, `List<Socket>` does not, and the refusal names the
+constraint that failed rather than the container. Every parameter must occur in the target and
+occur exactly once, and one (target, interface, argument binding) admits exactly one block:
+Lyric has no specialization, so two blocks that could both answer are an error, not a
+priority question. The orphan rule is unchanged: the block stands in the module of the target's
+definition or of the interface.
```
Appendix A: SEM0047-Text auf „a generic target needs the parameters on the `extend`" ergänzen.
