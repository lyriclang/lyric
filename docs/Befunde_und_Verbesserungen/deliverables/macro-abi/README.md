# macro-abi — Deliverables

Branch: `worktree-agent-ab3c14434f8eda027` (Worktree `.claude/worktrees/agent-ab3c14434f8eda027`), Basis dc32100c.

Commits:
- 91b3a96f `abi: extern "dotnet" binds a public static .NET method by reflection (stage 1 prototype)`
- 44cc6606 `comptime: the compiler evaluates a marked expression through the VM in a sandbox (prototype)` (enthält auch design/macros.md und design/abi.md)
- f9757d36 `comptime: refuse secureRandom at compile time — a literal has to be the same on every build` (Abstimmung mit stdlib-redesign; 13 ComptimeTests)

Dateien hier:
- `macros.md` — Analyse, Vergleich (11 Systeme), Design (Synthese + `comptime` + Generatoren), Prototypbericht
- `abi.md` — Analyse, Vergleich (12 FFI-Systeme), Stufenplan (.NET → C → Export → lyrbind), Marshalling-Tabelle, Prototypbericht
- `examples/comptime.lyr`, `examples/dotnet.lyr` — lauffähige Beispiele (`lyric run …`)
- `examples/ComptimeTests.cs`, `examples/ExternDotnetTests.cs` — die Tests (12 + 13, grün)

Testlage nach beiden Commits: Lexing 391, Parsing 491, Resolver 22, Core 379, Sema 766, Formatting 182, Ir 175, Bytecode 184, DocGen 201, Lsp 279, Dap 18, Embedding 222, Vm 1478 grün; Cli 276/277 (InterruptTests.Sigint scheitert am setsid/kill im WSL-Sandbox, unabhängig von den Änderungen).
