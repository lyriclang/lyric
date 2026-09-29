# Lyric Evolution Team — Zusammenfassung (Team-Lead)

Stand: 2026-09-22, alle Branches auf Basis dc32100c (v4.4.1). 5 Agenten, 94 Einträge in TASKLIST.md,
5 Branches mit 55 Commits und rund 20.300 hinzugefügten Zeilen. Alle Arbeitsbäume sauber. Deliverables unter deliverables/.

## Branch-Übersicht

| Agent | Branch | Commits | Umfang | Tests |
|---|---|---|---|---|
| lyriclings | worktree-agent-ac0107771533da8b3 | 3 | 190 Dateien, +5147 | audit 90/90, verify 90/90 |
| new-features | worktree-agent-a4962b919be70e622 | 13 | 58 Dateien, +4733/−78 | Sema 812, Vm 1475, alle grün |
| pattern-lambda | worktree-agent-a1c2eb789de86ba9d | 11 | 37 Dateien, +3706/−519 | Vm 1558, Sema 807, 95 neue Tests |
| stdlib-redesign | worktree-agent-aa7b5e912a78e6f2d | 24 | 32 Dateien, +5035/−625 | stdlib 211/211, alle grün |
| macro-abi | worktree-agent-ab3c14434f8eda027 | 4 | 40 Dateien, +1649/−31 | 25 neue Tests, alle grün |

In allen Läufen scheitert nur `InterruptTests.Sigint_wakes_the_parked_task` (276/277 in Lyric.Tests.Cli).
Drei Agenten haben unabhängig bestätigt, dass das an der Signalzustellung in dieser WSL-Sandbox liegt
und branchunabhängig auftritt.

## 1. lyriclings — 90 Übungen, fertig und verifiziert
21 Kapitel entlang des Guides, von hello bis zu vier Quiz-Übungen, die mehrere Konzepte kombinieren.
Aufteilung der gebrochenen Zustände: 68 Diagnosen (mit notiertem Code), 13 Paniken, 9 falsche Ausgaben.
Runner in reinem Lyric (std.process, std.task, std.json, std.io.file) mit watch, next, run, hint, list,
verify und einem Maintainer-Modus audit, der prüft, dass jede kaputte Datei genau den notierten Code
erzeugt UND ihre Lösung besteht: 90 auditiert, 0 Probleme; verify mit Lösungen 90/90; alle Lösungen
ohne Warnung unter --deny-warnings. Einzige Einschränkung: watch pollt, weil std.io.file keine mtime hatte
(stdlib-redesign hat modifiedMillis nachgeliefert).

## 2. new-features — 6 Features gebaut, 6 designt
Gebaut und lauffähig: f-String rendert Display statt nur Skalare; `throw` als Ausdruck mit `never` als
Rückgabetyp; Labels für break/continue; Value-Block mit Tail-Expression; `try` trägt zur Definite
Assignment bei; dazu ein nebenbei gefundener defer-Bug (ein defer im if-Körper lief im nicht genommenen
Zweig nie). Designt mit Grammatik, Semantik, Lowering-Skizze und Spec-Diff: `?T == ?T`,
Konformanz-Synthese nach Swift-Modell, typed throws in drei Stufen, `try`-Ausdruck, bedingte Konformanz,
Member-Sichtbarkeit (Major 5.0). Roadmap mit 22 Positionen über 4.5/4.6/5.0 und einer Liste bewusst
abgelehnter Vorschläge mit Begründung (nestbare Optionals, Option<T> als Enum, `?`-Operator, break value,
derive-Schlüsselwort).

## 3. pattern-lambda — der Pattern-Compiler ersetzt die Zwei-Pass-Architektur
Kern: `LowerPattern` emittiert Test und Bindung pro Knoten gemeinsam und steigt mit demselben Code in
Tupel, Varianten-Payloads, Struct-Felder und Array-Positionen ab. Bewusst ein Backtracking-Automat und
kein Entscheidungsbaum, weil §7.6 die geschriebene Arm- und Guard-Reihenfolge zusichert. Damit fallen
zwölf alte Defekte auf einmal: verschachtelte Varianten, Tupel mit Varianten und Literalen, Or-Patterns
mit Bindungen (seit STATUS.md offen), Feldmuster mit Test, Struct-Aliasing, Literal-Adaption an die
Scrutinee-Breite, Abdeckung über `?T`-Payload, Guard in Klammern, der lyriclings-ICE.
Neu gebaut: if let / while let / let else, Patterns in for-Köpfen und Lambda-Parametern, Array-Patterns
in allen Längenklassen, Exhaustiveness-Diagnose mit Zeugen-Pattern, Closure-Kurzsyntax `x => x * 2`,
Trailing-Lambda mit implizitem `it`, Parameter-Destructuring. Widerlegt: die vermutete Lücke bei der
Lambda-Inferenz existiert nicht.

## 4. stdlib-redesign — std.result, Iterator-Methoden, Container, acht Bugfixes
Gebaut: `std.result` mit Result<T,E> und Brücken (parseIntOrErr, textOrErr, parseOrErr); 14 Terminatoren
als Default-Methoden auf Iterator plus fünf Adapter, damit die Kette nicht mehr bricht; Container-Paket
(List.of/slice/filter, Map.keys/entries/getOrInsert/update mit Verdichtung, Set, arrayOf, groupBy);
std.hash mit vier nativen Digests; sieben neue Test-Assertions; Zahlen mit checked- und
saturating-Familien. Acht Bugfixes, darunter der parseInt-Überlauf, powInt, die widersprüchlichen
trim-Varianten, Random ohne Modulo-Bias, die unbegrenzt wachsende Map und std.os.args.
Dazu ein Compiler-Fix: Methoden generischer Enums wurden nie gelowert.
Zielbild: `throws` bleibt der einzige Fehlermechanismus, Result ist ein Wert; die Antwortform steht im
Namen; 4.5 additiv, 5.0 im Bild des try-Ausdrucks mit Abschaffung der 45 OrThrow-Zwillinge.

## 5. macro-abi — Makros nur in drei Formen, ABI zuerst über .NET
Makro-Urteil: ein allgemeines Makrosystem lohnt sich nicht. Alles, was Syntax erzeugt oder ersetzt,
kostet zwei Sprachen für Formatter und LSP und unterläuft die Konventionen; alles, was ungesandboxt im
Compiler läuft, bricht das Sandbox-Versprechen beim Bauen. Empfohlen und prototypisiert: Konformanz-
Synthese (bei new-features), `comptime` als Ausdruck, Generatoren über build.lyr.
Gebaut: `comptime e` mit eigenem Evaluator, der das Modul mit Capability.None und Instruktionsbudget
über die VM ausführt und das Ergebnis als Konstante einsetzt (Potenztabellen, fib 90, Format-Spec-Prüfung
zur Compile-Zeit; die Hilfsfunktionen verschwinden aus dem Modul). Und `extern "dotnet" fn … = "Typ::Methode"`
mit Reflection-Binder, breitenexaktem Marshalling und Capability-Prüfung: ohne Grant LYR-CAP0001.
ABI-Stufenplan: .NET zuerst (kein Opcode, kein Formatwechsel, sofort BCL für Hash, TLS, HTTP, Regex),
dann C über NativeLibrary mit std.ffi, `unsafe`-Block und eigenem ffiAccess-Bit, dann die Export-Richtung,
dann ein Bindings-Generator. Vergleichstabellen über 11 Makro- und 12 FFI-Systeme.

## Was die drei abgestimmten Agenten füreinander gebaut haben
- stdlib-redesigns Result brauchte Ok/Err-Patterns inklusive `Result<?int,E>`: pattern-lambda hat die drei
  zugehörigen Compiler-Defekte gefixt.
- pattern-lambda hat Trailing-Lambdas mit implizitem `it` nur bei einem Parameter gebaut, weil
  stdlib-redesign es für die Adapter-Ergonomie angefordert hat; ebenso `for ((i, v) in …)` und Array-Patterns.
- new-features hat pattern-lambdas Design für werfende Funktionstypen übernommen und in drei Punkten
  präzisiert; pattern-lambda hat das zurückübernommen.
- stdlib-redesigns Anker (combineHash, Display.show, Ok/Err, `next(): ?T`) stehen unverändert in
  new-features' Designs.
- macro-abi hat Capability-Namen, `uint8[]` als einzigen Bytepuffer und `throws HostError` von
  stdlib-redesign übernommen; die Synthese bleibt nach Absprache das Swift-Modell ohne derive-Attribut.
- Offener Dissens, dokumentiert mit beiden Begründungen: `?T :: [Display]` (new-features dagegen,
  stdlib-redesign dafür), Kompromissvorschlag `showOptional(o, ifNone)`.

## Gemeinsame Befunde, die aus mehreren Richtungen kamen
1. Generische Methode auf generischem Typ ist nicht lowerbar. Blockiert Result.map, List.map,
   Iterator.toList. Von stdlib-redesign gefunden, von new-features als HIGH in die 4.5-Voraussetzungen
   übernommen.
2. Semantikfehler erscheinen als LYR-IR0001 „cannot lower it yet". Von lyriclings aus Lernendensicht,
   von stdlib-redesign aus API-Sicht und schon vom Bug-Hunt-Team gemeldet.
3. `mut fn` wird auf Klassen nicht erzwungen, auf Structs schon. Der Guide macht keinen Unterschied,
   die Regel ist so nicht lehrbar.
4. Lambda kann keine throws-Klausel tragen (SEM0084). Verhindert assertThrows und typisierte Callbacks.
5. Feld und Methode teilen einen Namensraum (RES0001).

## Wichtigster Befund: der Verifier läuft nach dem Optimierer
Vom Team gefunden, vom Team-Lead am Code nachgeprüft:
`ModuleLowerer.cs:468-485` lässt Inliner, ScalarReplacement, Devirtualizer und Reachability laufen,
und erst `:487` ruft `IrVerifier.VerifyOrThrow`. `Phase.cs:52-57` schaltet den Verifier zudem nur im
Debug-Build ein.

Daraus folgen zwei Dinge, die weit über den Anlass hinausreichen:
- **Eine Optimierung kann fehlerhaftes IR zudecken.** Frisst der Inliner einen kleinen Methodenkörper
  samt fehlerhaftem Aufruf, läuft das Programm grün durch. Derselbe Fall mit einer Schleife im Körper
  (der Inliner lehnt ab) meldet `call to Box<?int>.or: arg 1 is i64, expected ?i64`.
- **„Läuft durch `lyric run`" ist kein Beleg für wohlgeformtes IR.** Zusammen mit dem Debug-only-Verifier
  ist ein Lowering-Defekt im Release-Build doppelt unsichtbar.
Empfehlung beider beteiligter Agenten und des Team-Leads: vor den Optimierungspässen verifizieren, wie
LLVM es tut, und zusätzlich im Release verifizieren oder wenigstens den Bytecode-Reader typisieren
lassen (vgl. Bughunt P1-19).

**Der Umbau wurde durchgerechnet, nicht geschätzt.** Mit `Optimize = false` verifiziert bestehen die
gesamte stdlib, ihre 211 Tests und alle 47 Beispiele des Repositoriums die Prüfung ohne einen einzigen
Befund. Es gibt heute also nichts, was der Optimierer stillschweigend repariert. Beide Branches haben
unabhängig gemessen und kommen auf dasselbe Bild: fünf Tests setzen die Optimierung voraus, null
Verifier-Befunde. Wer die Reihenfolge ändert, fasst genau diese fünf Tests an und sonst nichts.

Methodische Konsequenz, die das Team an sich selbst gezogen hat: ein Test für einen Lowering-Defekt muss
unoptimiert lowern, sonst ist er grün und beweist nichts. Pattern-lambdas eigener Test hatte genau diesen
Fehler und hätte den Bug, für den er geschrieben wurde, nicht gefangen; korrigiert fallen mit entfernter
Substitution sechs von sieben Zeilen statt zwei. Derselbe Fehlertyp trat zweimal auf, einmal über die
Optimierungsstufe und einmal über ein Literal-Argument, das die geprüfte Operation gar nicht auslöste.

## Nachtrag nach dem Gegenlesen (verifiziert)
Nach den Abschlussberichten haben pattern-lambda und stdlib-redesign sich gegenseitig gegengelesen und
dabei drei Dinge korrigiert, die im ursprünglichen Bild falsch waren (der Anlass war der oben
beschriebene Verifier-Befund, der vier Runden lang plausible, aber falsche Erklärungen erzeugt hat,
weil zwei Beteiligte unterschiedliche Pipelines maßen und beide für „den Compiler" hielten):

1. **Derselbe Compiler-Fix existiert doppelt.** Beide Branches haben unabhängig einen Helfer
   `InstanceSubstitution` eingeführt und an denselben drei Aufrufstellen in FunctionLowerer.cs eingehängt,
   damit die Argumente einer Methode einer generischen Instanz unter deren Substitution gelowert werden.
   Die Fassungen sind inhaltlich gleich, stehen aber an verschiedenen Zeilen. Beim Merge bleibt eine.
2. **Die erste Fassung war halb.** pattern-lambdas ursprünglicher Fix ließ den statischen Aufrufpfad aus;
   das fiel erst durch die Rückfrage von stdlib-redesign auf und ist mit 5dca585f nachgezogen.
   Die frühere Merge-Empfehlung „pattern-lambdas Fassung behalten, weil sie die umfassendere ist" hätte
   genau den halben Fix festgeschrieben und ist durch eine inhaltliche Prüfliste ersetzt: vier Wege zu
   einer Methode einer generischen Instanz, Argumente als Literale.
3. **Eine weitergegebene Erklärung ist widerlegt.** Die These, eine Klasse mit Interface-Konformanz nehme
   einen anderen Lowering-Pfad und verdecke den Defekt deshalb, hält der Messung nicht stand: die
   Enum-Zeile bricht, beide Klassen-Zeilen laufen. Welchen Pfad eine Klassenmethode stattdessen nimmt,
   ist offen und im Test als offen markiert, nicht beantwortet.

Der zwischenzeitlich offene Faden ist geschlossen: die abweichenden Zahlen beider Branches waren keine
Baumdifferenz, sondern eine ungemessene Suite. Nachgemessen berichten beide Seiten dasselbe, bis auf die
Testnamen identisch.

Beide gekoppelten Agenten haben nach ihren Abschlussberichten weitergearbeitet, weil die Rückfragen des
jeweils anderen sie reaktiviert haben: stdlib-redesign von 14 auf 24 Commits, new-features von 9 auf 13,
pattern-lambda von 5 auf 11. Das Gegenlesen hat mehr korrigiert als die letzten Features hinzugefügt haben.

## Merge-Empfehlung
Alle fünf Branches liegen auf derselben Basis. Empfohlene Reihenfolge, weil sich nur zwei Paare berühren:
1. lyriclings (eigenes Verzeichnis, keine Überschneidung) und macro-abi (nur Frontend-Ergänzungen plus
   eine neue VM-Datei).
2. pattern-lambda, dann new-features. Beide Agenten haben vier Berührungspunkte beidseitig dokumentiert:
   LowerArm (new-features' Diverges und TailSink gewinnen), ParseMatchArm (verschiedene Zeilen),
   LowerIf (dieselbe Stelle, gleiche Bedeutung), LowerReturn (beide haben einen never-Zweig, einen behalten).
   Danach lässt sich pattern-lambdas HoldsStatements durch den Value-Block ersetzen.
3. stdlib-redesign zuletzt. Dabei entsteht ein echter Konflikt in FunctionLowerer.cs, weil der
   InstanceSubstitution-Fix auf beiden Branches steht. Eine Fassung behalten und inhaltlich prüfen,
   statt nach Umfang zu entscheiden: alle vier Wege zu einer Methode einer generischen Instanz müssen
   die Substitution tragen, einschließlich des statischen Aufrufpfads, und der Test dazu muss seine
   Argumente als Literale führen.
