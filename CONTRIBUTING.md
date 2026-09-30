# Contributing to Lyric

This is currently a solo project. The rules below are the maintainer's
self-binding contract to avoid the failure modes of a previous language
project ("Oil"), where scope creep and parallel-mechanism creep killed
forward progress.

The rules below bind the Lyric 5 line on `main`. The decisions they refer to
live in [`design/v5/spec/`](design/v5/spec/) (areas 00–12, plan in 13); the
Lyric 4.x line continues as [lyriclang/lyric-script](https://github.com/lyriclang/lyric-script)
under its own rules.

---

## The Three Rules

### Rule 1 — No open idea store

Every idea written down in this repository carries **a decision or a clock**:
it is decided in a `design/v5/spec` document (with the reasoning and what was
rejected), or it is named there as a **door** together with what would open
it, or it is an issue with a date by which it gets decided. A list of wishes
without an answer does not exist here — not as a roadmap file, not as a
section of one.

Reason: Oil grew a 2761-line post-v1 roadmap that absorbed all design
energy and prevented v1.0 from ever shipping. The 4.x wording of this rule
("until v1.0 ships") had expired by its own clause; the lesson it carried
had not.

### Rule 2 — One mechanism per concept

Each language concept has exactly one mechanism in Lyric 5:

| Concept | Single mechanism | Decided in |
|---|---|---|
| Error handling | Typed `throws` clauses, `try` marks every throwing call; `Result` is a value, never a propagation path | 05 |
| Cleanup on scope exit | `defer` and `using let` on one LIFO list (no `finally`, no destructors) | 05 R2 |
| Memory management | Tracing GC only (no manual/borrow/refcount) | 01 L1 |
| Polymorphism | Interfaces with defaults, generic `extend`, delegation `by`; no class inheritance | 04 |
| Concurrency | Explicit threads, one scheduler per thread, stackful coroutines and tasks; waiting parks | 06 |
| Foreign code | `extern "C"` — the C ABI is the one boundary | 11 W4 |
| Code generation | `comptime` and macros over typed syntax trees | 09 |

Changing a row reopens its area document: the change is written there with
its reasoning and what it replaces, before any code.

### Rule 3 — Every milestone ships something

A milestone is not done until:

1. Its exit criteria, as recorded in [`STATUS.md`](STATUS.md), are met.
2. A git tag exists.
3. Someone could clone the repo, follow the README, and *do something* with
   it — even if that something is small (e.g. tokenize a file).

No milestone may be marked done by intent alone. There must be an
artifact.

---

## How to add a language feature before 5.0

Don't, unless its area document in [`design/v5/spec/`](design/v5/spec/)
decides it. A door named there is not a feature; it opens with its own
decision.

If you really must add something not decided there:

1. Write the decision into the area document first — question, options,
   comparison with other languages, what is rejected. "It would be nice"
   is not a problem statement.
2. Wait at least **7 days** before opening a PR. The waiting period is
   mandatory even for the maintainer.
3. The PR must include the specification chapter and conformance cases
   (spec-first: rule PR, then its twin), user-facing documentation, and
   tests.

If the change would push the milestone it lands in by more than 100 % of
its size (13), it is rejected by default and stays a door.

---

## How to fix a bug

Bugs do not need the 7-day wait. The standard flow:

1. Add a failing test that demonstrates the bug.
2. Make the test pass.
3. Open a PR. Include the issue number if there is one.

---

## Scope check ritual

On the first Sunday of each month, do a **scope check**:

1. Read the current milestone and its estimate in [`STATUS.md`](STATUS.md).
2. Compare the actual elapsed time with the estimate.
3. If you are >50% over the estimate: honestly evaluate which features can
   be cut.
4. If you are >100% over: re-cut the milestone and record the change in
   `STATUS.md`.

This is the **only** legitimate place for plan adjustment. Plan changes
made on impulse (e.g. "I just thought of something better") are forbidden.

---

## Code style

| Topic | Convention |
|---|---|
| Naming (C# code) | Standard .NET: `PascalCase` types/methods, `_camelCase` private fields, `camelCase` parameters |
| Naming (Lyric stdlib code in `.lyr`) | `PascalCase` types, `camelCase` everything else (see `docs/Grammar.md`) |
| Indentation | 4 spaces, no tabs |
| Line length | Soft 100, hard 120 |
| Trailing commas | Allowed in multi-line lists/blocks |
| `var` (C#) | Prefer when type is obvious from RHS; use explicit type otherwise |
| Comments | English, describing the technique and the logic. No justifications, no project history, no milestone or decision references. |

For C# code, follow these rules manually. For Lyric code (`.lyr`) the formatter is the rule:
`lyric fmt` writes the one shape there is, the repository's own Lyric (stdlib, examples,
templates) is kept formatted, and a test in `Lyric.Tests.Formatting` fails when it stops
being it.

---

## Testing

Each subsystem has its own test project:

- `tests/Lyric.Tests.Core/` — `SourceManager`, `DiagnosticEngine`, `Span`
- `tests/Lyric.Tests.Lexing/` — tokenizer
- `tests/Lyric.Tests.Parsing/` — AST construction
- `tests/Lyric.Tests.Resolver/` — name resolution
- `tests/Lyric.Tests.Sema/` — type checking
- `tests/Lyric.Tests.Ir/` — AST to IR lowering
- `tests/Lyric.Tests.Formatting/` — the formatter, over the whole corpus
- `tests/Lyric.Tests.Lsp/` — the language server over the front end
- `tests/Lyric.Tests.DocGen/` — the documentation site generator
- `tests/Lyric5.Tests/` — the Lyric 5 runtime (C, built and run for every Tier 1 target)
  and its toolchain driver

Tests use xUnit. Golden tests compare against snapshot files in
`tests/<project>/golden/`; set `LYRIC_UPDATE_SNAPSHOTS=1` to rewrite them.

Before committing: `dotnet test` must pass.

---

## Commits

Conventional-ish, but loose. Format:

```
<area>: <short imperative description>

[optional body explaining why, not what]
```

Examples:

```
lexer: handle nested block comments
sema: detect non-exhaustive match with missing variants
docs: clarify defer ordering with multiple defers
```

The area is the subsystem the change lands in.

---

## Releases

Tags follow `vMAJOR.MINOR.PATCH` semver, with all three components written
out from v1.0 on. Every release has an **annotated tag**; its message is the
release note: what the version delivers, and what it cannot do yet.

Two channels (design/v5/spec/11 T5):

- **stable** — pushing an annotated `vX.Y.Z` tag runs `.github/workflows/release.yml`,
  which verifies, packages the Tier 1 targets and publishes the archives as a
  GitHub release. The first Lyric 5 release is `v5.0.0`; the last Lyric 4
  release was `v4.5.0`.
- **dev** — `.github/workflows/dev.yml` builds every push to `main` and
  replaces the rolling `dev` prerelease, versioned `5.0.0-dev.<date>+<sha>`.
  **No promise of any kind**: this is where things are tried and grow before
  they move into a stable release. There is no nightly channel.

**The changelog starts at `v1.0.0`.** A changelog answers "what changed for me
since last time", and that question presupposes something to be compatible with.
Pre-1.0 there was no such promise, neither for the `.lyrbc` format nor for the
language itself, so those releases carry their notes in their annotated tags and
appear in no entry.

From `v1.0.0` on, every release has three things: the tag, a GitHub release page,
and an entry in [`CHANGELOG.md`](CHANGELOG.md). An entry lists what changed for
someone USING the toolchain — the language, the standard library, the bytecode
format, the command line, the embedding API. Compiler internals stay in `git log`.

---

## Development environment

Runtime and backend work happens under **WSL2 (Arch Linux)**, with the
repository in the WSL file system, and Windows as an equal CI target
(design/v5/spec/00):

| Need | Tool |
|---|---|
| Compiler (C#) | .NET SDK 10 (`global.json`) |
| C for release and cross builds | `zig cc` (version in `tooling/zig-version`) |
| C with sanitizers (ASan, UBSan, TSan) | clang — `zig cc` ships no ASan/TSan runtime (measured, 01 C7) |
| Memory checking | valgrind; on Arch set `DEBUGINFOD_URLS=https://debuginfod.archlinux.org` (glibc is stripped) |
| Debugging, profiling | gdb, lldb, perf |
| GitHub | `gh` with the `workflow` scope (`gh auth refresh -s workflow`), or pushes that touch `.github/workflows` are refused |

---

## Questions

Open a GitHub discussion, not an issue. Issues are for actionable work.
