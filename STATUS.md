# Status

The one file that changes often. It says where the Lyric 5 line stands, which milestone is open,
and how the work is done. The decisions themselves live in [`design/v5/spec/`](design/v5/spec/)
(areas 00–12); the plan with sizes, artefacts and dependencies is
[`13-umsetzungsplan.md`](design/v5/spec/13-umsetzungsplan.md). The Lyric 4 status up to the
4.6 cut is archived at [`docs/archive/4.x/STATUS.md`](docs/archive/4.x/STATUS.md).

## Where things stand

- **Last Lyric 4 release: v4.5.0.** 4.6 was cut but never released; the 4.x line continues as
  [lyriclang/lyric-script](https://github.com/lyriclang/lyric-script) (the 4.6 cut without the
  Lyric 5 migration warnings), on its own roadmap toward `lyric-script 1.0.0`.
- **`main` is Lyric 5 in development**, no promise of any kind. Of the 4.x toolchain the tree
  keeps the front end (lexer, parser, resolver, sema, IR) the Lyric 5 compiler grows out of
  (13, M2), the language server (M13b decides), the standard library source (until M8a) and the
  examples (the input of M16); the VM, the tools, the bytecode format and their tests went with
  M2 S0 and live on in `lyric-script`. The 4.x code says **4.6.0**. Beside it grows **`lyric5`**
  (`src/Lyric5`), the Lyric 5 command line as one NativeAOT binary, at **5.0.0** (`dev` builds:
  `5.0.0-dev.<date>+<sha>`).
- **Specification**: `lyriclang/lyric-spec` — `main` is the Lyric 5.0 text, written milestone by
  milestone; branch `script` holds the 4.x text, which checks `lyric-script`, not this tree: the
  milestones move the language away from it, so the 4.x conformance gate ended with M2 S0.
  The Lyric 5 suite runs against this tree in CI since M3 S9 (*Conformance gate*, one job per profile since M8c S19, both
  profiles; the cases side by side since 2026-10-11, `tooling/conformance/run_parallel.py` over the pinned runner),
  at the commit [`spec.pin`](spec.pin) names: a compiler change and its rule are two
  pull requests checked together, the specification merges first, and main is green in between.
  **A pull request runs what its diff reaches** (`tooling/affected.toml`, 2026-10-11) and a fixed regression group;
  main after a merge, and a dispatched run, run everything — a break the selection misses shows on main.

## Milestones

| M | Name | Size | State |
|---|---|---|---|
| M0 | Preparation: repos, archive, CI with `zig cc` and NativeAOT, `dev` channel | M | **done** 2026-09-29 |
| M1 | Runtime core in C (Boehm GC behind the allocation API) | M | **done** 2026-09-30 |
| M2 | First native program (IR → C → `zig cc`) | L | **done** 2026-09-30 |
| M3 | Value model and type system | XL | **done** 2026-10-01 |
| M4 | Interfaces and abstraction | L | **done** 2026-10-01 |
| M5 | Errors | M | **done** 2026-10-02 |
| M6 | Coroutines, scheduler, threads | XL | **done** 2026-10-02 |
| M7 | Modules and packages | L | **done** 2026-10-02 |
| M8a | std core | XL | **done** 2026-10-07 |
| M8b | std I/O and system | L | **done** 2026-10-08 |
| M8c | std rest: Unicode, the remaining adapters, `Result`, `std.fmt`, `@Bench` | L | **done** 2026-10-09 (S15–S19 after the review) |
| M9a | `comptime` (the IR interpreter), attributes, `std.meta`, the synthesis | XL | **done** 2026-10-13 |
| M9b | Macros | L | **done** 2026-10-13 |
| M10 | std after rule D | XL | **open** since 2026-10-14 |
| M11 | Own GC | XL | — |
| M12 | Diagnostics and lints | L | — |
| M13a | fmt, doc, api | M | — |
| M13b | LSP, DAP, clients | XL | — |
| M14 | FFI and embedding | L | — |
| M15 | Distribution | M | — |
| M16 | Migration (`lyric fix --from-4`) | L | — |
| M17 | Spec 5.0, suite, guide | L | — |
| M18 | Release 5.0.0 | M | — |

Sizes are sessions, not dates: S days, M 1–2 weeks, L 3–4 weeks, XL more. A milestone more than
100 % over its size is re-cut here (CONTRIBUTING, scope check).

### M0 — done (2026-09-29)

1. `lyriclang/lyric-script` exists and its CI is green — `33339164`.
2. `lyric-spec`: branch `script` = the 4.x text (`e314d72`); `main` carries the 5.0 skeleton
   (lyric-spec#45).
3. `main`: `lyric5` (5.0.0) beside the 4.x tree (4.6.0), the Lyric 5 migration warnings reverted,
   4.x planning archived, README/CONTRIBUTING/CHANGELOG on the 5 line — #175.
4. CI: `c-toolchain` (native, cross to every Tier 1 triple, clang sanitizers) and `lyric5-aot`
   green on Linux x64/arm64, Windows and macOS — #175.
5. The rolling `dev` prerelease exists (first build `0634784`, four Tier 1 archives and
   `SHA256SUMS`), every package job checked `lyric5 --version` → `lyric 5.0.0-dev.<date>+<sha>`;
   the `nightly` prerelease and tag are gone. The linux-arm64 package needed the stub to know
   the RID — #176.

Measured on the way and kept: the tree stays 4.6.0 because a 5.0.0 claim fires the 4.x
deprecation clocks aimed at 5.0 (29 CLI tests red); `zig cc` ships no ASan/TSan runtime, so the
sanitizer profile compiles with clang (01 C7).

### M1 — done (2026-09-30)

Merged as #178 (`c8a76159`); the spec side is lyric-spec#46 (`70e7783`).

1. S1 `6ce9d46e`: bdwgc 8.2.12 and libbacktrace vendored (`runtime/THIRD_PARTY.md`);
   `Lyric5.Toolchain` builds `liblyr.a` for all five Tier 1 triples, with a content-hash cache.
2. S2 `f64b09ab`: object model (header → descriptor), allocation, strings, arrays, console,
   `lyr_run_main`; Hello in C runs on every Tier 1 runner.
3. S3 `36ea5b2d`: roots, registered ranges, weak references with a death callback, thread attach.
4. S4 `233a6f00`: panics with backtraces, the check macros, the RT codes, the crash handler,
   stack overflow as a panic, the panic hook. The panic golden passes on Linux, Windows and macOS.
5. S5: ASan+UBSan and TSan over the runtime programs in CI (Linux x64), each with a control that
   shows a real fault is still reported; valgrind locally (`tooling/valgrind/run.sh`,
   `runtime/valgrind.supp`); the measurement below; spec chapter 13 §1, the runtime contract of
   stage 0 (lyric-spec#46).
6. CI fixes after the slices (`ec4ef004`): macOS fault traces from the frame chain, archives
   named by their key, the sanitizer runs confined to the C toolchain job.

Measured (WSL2 x86-64, release profile, zig cc 0.16; no ratchet before M2):

| | Hello in C against `liblyr.a` | plain C hello |
|---|---|---|
| Size, stripped | 202 KB | 3.5 KB |
| Size with debug information | 1.2 MB | 5 KB |
| Start, fork + exec | ≈ 0.8 ms | ≈ 0.6 ms |

Learned on the way and kept:
- `zig cc` writes PDB on Windows, not DWARF. Frames are named through DbgHelp there; libbacktrace
  serves Linux and macOS. The plan had assumed DWARF everywhere.
- `zig cc` links no unwinder into a C program (its libunwind comes in with `-lunwind` on Linux)
  and turns UBSan on by itself at `-O0` (switched off: UBSan belongs to the asan profile).
- macOS keeps line tables out of the executable: the link runs `dsymutil` on a macOS host, which
  the plan had left for M2.
- TSan cannot see Boehm's synchronization. The runtime tells it that a collection orders the
  threads, which it does, instead of suppressing the reports.
- Under the conservative stage, a stale root handle in scanned memory keeps alive whatever
  reuses its cell (documented in `lyr/gc.h`).
- Temporary object files were unique per process only, and parallel builds in one process
  collided on them; on Windows a replace of a file in use fails, so objects and archives are
  published once under content-keyed names and never replaced.
- Apple's unwinder does not leave a signal handler's trampoline: a fault's frames come from the
  frame-pointer chain there.
- TSan delivers the collector's stop signal only at its own points; Boehm's TSan mode (a mutex)
  has none, its spin lock (`nanosleep`) has one. The sanitizer runs live in the C toolchain job
  alone.

**Open thread — the threads program under TSan.** On GitHub's Ubuntu runner (clang 18) the
collector's stop-the-world signals now and then reach a thread only after Boehm's retry limit
(15 s) and it aborts, "Signals delivery fails constantly at GC #2": 3 of 5 runs, with and without
the kernel's `mmap_rnd_bits` preparation. Locally (WSL2, clang 22) 96 stressed runs pass. The
mechanism is not found. The run is local only (`LYRIC5_SANITIZERS=all`) until M11, whose
collector stops threads at safepoints, not with signals; the other TSan runs and the race
control stay in CI.

### M2 — done (2026-09-30)

Merged as #180, #181, #182, #183, #184 and #185 (`07e1221e`); the spec side is lyric-spec#46
and #47 (`ebfe2918`).

The plan (13, M2): S0 prune, S1 front end behind the subset gate, S2 C emission, S3 strings and
structs, S4 build and run, S5 measurement point 1 and spec. Exit criteria: `hello`, `fizzbuzz`
and `fibonacci` (recursive and iterative; the coroutine form returns with M6) run natively on
every Tier 1 target; measurement point 1 holds (below).

1. S0: the 4.x VM, tools, bytecode writer and reader, embedding API, debug adapter, their tests,
   `stdlib-tests/`, `templates/`, `tools/Bench`, `build/publish.proj`, the 4.x release workflow
   and the CI jobs that ran the VM (compiled, release profile, conformance, spec mirror,
   publish) are gone — 58 000 lines. `docs/Grammar.md`, `docs/Bytecode.md` and
   `docs/Pack.md` stay as frozen 4.x references, because the guide links them and the site
   renders them until the website round. The tree stays at 4.6.0: the 29 CLI tests a 5.0.0
   claim turned red are gone, but the 4.x `stdlib` carries `@Deprecated(until = "5.0")`
   promises that a 5.0.0 toolchain would turn into build errors (`DeprecationPromise`); that
   ends when the 4.x front end and its `stdlib/` leave `main` (M17 — not M8a, which wrote
   `stdlib5/` and left the shared front end; the audit of 2026-10-07).
2. S1: `lyric5 build <file> --emit ir` — the 4.x front end behind the subset gate
   (`Lyric5.Compiler`, `LYR-CG0001` "not yet in Lyric 5: … (M<n>)"), compiling against
   `stdlib5/`, the seed of the Lyric 5 standard library (`std.core`, `std.string`, `std.io`, as
   natively backed declarations — the provisional intrinsic table, which `extern "C"` replaces
   with M14 as M8b's plan decided (P1); it outlived M8a, which wrote `std` in Lyric). The
   NativeAOT risk is gone: the front end publishes without a trim warning, 5.3 MB, and the
   binary emits hello's IR in CI on every Tier 1 runner. Found on the way: `for` over a range
   lowers through the 4.x iterator classes and optionals; S2 gives range literals a counted-loop
   lowering.
3. S2: `lyric5 build <file> --emit c` — the C emitter (`CEmitter`): one C function per IR
   function in the platform ABI, locals and temps as C locals, blocks as labels and `goto`,
   `#line` before every statement, checked integer arithmetic through the runtime's macros (with
   `LYR_CHECKED_REM`: `MIN % -1` panics like `MIN / -1`), the `main` wrapper around
   `lyr_run_main`. `for` over a range literal is a counted loop in the front end now (no
   iterator object, no `std.iter`). Golden C for three programs; programs run natively in both
   profiles, and a failed check panics with the `.lyr` line in the trace.
4. S3: strings (literals as static objects, `+`, f-strings with int, uint and bool), structs as
   C values (the IR's struct temps alias storage; `newobj` and `structcopy` make it fresh;
   store, field write, argument and return copy), the intrinsic calls into the runtime
   (`std.io`, `std.string`, `std.core.panic` with the new code RT0008 — the spec table gets it in
   S5). `hello`, `fizzbuzz` and `fibonacci` (recursive and iterative) run natively on Linux and
   Windows in both profiles, plus `structs`, `strings` (UTF-8) and `panic`. New runtime helpers:
   text from `uint` and `bool`, `lyr_panic_message`.
5. S4: `lyric5 build <file>` and `lyric5 run <file> [-- args]` (`Lyric5.Build`): a file is an
   implicit package with `out/` by it, or at the nearest `.git` (P5), the binary under
   `out/<profile>/<target>/` (P4); the C is cached by the source, the toolchain and the emitter
   version, objects by content (L7), the link only when an input is newer — an unchanged program
   compiles nothing twice; the runtime archive is built once per toolchain version, target and
   profile into the user's cache (`~/.cache/lyric`, `LYRIC_CACHE`); `--profile`, `--target`
   (cross builds, run refused), `--emit`; exit codes per C5, the program's own passed through.
   Measured on the way: a warm `run` of hello takes ≈ 120 ms through `dotnet` — S5 measures the
   NativeAOT binary against the C9 budget. The AOT job now runs hello and fib natively on every
   Tier 1 runner.
6. S5: measurement point 1 as ratchet tests (`MeasurementTests`), the spec (lyric-spec#47: 14
   §1 `build`/`run`, 13 §1.4 RT0008 and `MIN % -1`), and two memos that made the warm path:
   the C compiler's version line (starting `zig version` cost 25 ms per invocation) and the
   runtime archive's path (keying the collector's units hashed megabytes per invocation), both
   in the user's cache under the binary's stamp.

Measured (WSL2 x86-64, `lyric5` as NativeAOT, release profile, minimum of many runs):

| | Value | Bound |
|---|---|---|
| hello binary, debug information included | 1.2 MB | < 2 MB (01 L11) |
| hello start | ≈ 1.8 ms (plain C: ≈ 1.0 ms) | < 5 ms (01 L11) |
| `lyric5 run hello` warm | ≈ 12 ms, ≈ 10 ms over the program | ≤ 50 ms over the program (11 C9) |
| `lyric5 --version` | ≈ 7 ms (was 26 ms before the memo) | — |

The ratchets run on Linux: the binary under 2 MB, the runtime's start cost under 4 ms over plain
C and under 5 ms absolute, the warm run under 50 ms over the program. On Windows a process start
is 10–30 ms of its own and the in-process measurement drowned in it; the NativeAOT job prints the
figures for every Tier 1 runner instead. Found on the way: on Windows `dbghelp.dll` came in
through the import table and cost 3 ms of every program start for a report most programs never
write — the runtime loads it at the first trace now, and the start costs ≈ 2.5 ms over plain C
there (was 5).

### M3 — done (2026-10-01)

Merged as #187, #189, #190, #191, #192, #193, #194, #195 and #196 (`648474da`), with #188 for
the `inout` sign; the spec side is lyric-spec#48–#56 (`8149109`).

The plan (13, M3): 02/03 complete, generics by monomorphization with cache units (C3), the
pattern rules of 08. Nine slices: S1 numbers, S2 classes, S3 optionals, S4 enums and patterns,
S5 arrays, views and inline arrays, S6 tuples, ranges, `with` and module-level bindings, S7
function values, S8 generics, S9 the conformance gate and measurement point 2. Exit criteria:
`arith/arrays/structs/enums/tuples/optionals/patterns/generics` run natively; conformance 02/03;
measurement point 2 holds (below).

1. S1: the number tower (03 T1a–T1e, T2): `int64`/`uint64`/`float64` as aliases, lossless
   widening at coercion sites only, `as` with its refusals, `char` and `bool` not numbers,
   shifts and the wrap operators `+% -% *%`, floats as IEEE doubles with `-ffp-contract=off`
   everywhere (01 L10), float text by shortest round trip. The 4.x standard library's 49
   `char as int` sites migrated from the compiler's own diagnostics.
2. S2: classes as objects behind a header with a static descriptor (size, reference bitmap,
   name), the write barrier; fields `var` or fixed and `mut fn` kept (02 M2–M4); a struct
   method's `this` is the caller's place (M5); `Point(1, 2)` ≡ `Point.new(1, 2)`; `same(a, b)`.
3. S3: optionals with the niche for references and `{T; bool}` otherwise (01 V5), `??T`, a
   bare `T` opaque, narrowing by the type the name has where the test stands (a 4.x bug,
   invisible with one level), `x!` as the one checked unwrap (RT0004 with the line).
4. S4: enums as a tag and a union inline (01 V6), the finiteness rule with `Box<T>` in
   `std.core` as the way out (02 M13), `LYR_DESC_CONSERVATIVE` for a word that is a reference
   under one tag only; a bare name in a pattern binds, always (08 Y6, `LYR-SEM0111`), `.Red`
   wherever the position expects an enum (T9/Y9, `LYR-SEM0113`), unreachable arms warned.
5. S5: `T[]` with the elements inline (01 V10), `length()` a call, `^n` in the brackets,
   `Slice<T>` as a 16-byte view (03 T13 A2), `T[N]` as a value (A4); the `[x] * n` gate for
   objects until `Clone` (M4). Deferred, flagged: `StringView` and `s[i]` wait for the string
   round (M8).
6. S6: tuples as structs with positional fields and labels on the type only (03 T16), ranges as
   `std.core` structs with the `for` head still a counted loop, `with` (02 M6), module-level
   `let`/`var` as C statics filled by the module's initializer (07 V5).
7. S7: function values as `{ code, env }` with a thunk for the environment-less (01 V8), the
   three lambda forms and the trailing block with parameters (08 Y11), captures by copy or
   shared box (02 M8), `obj.method` bound, `arrayOf(n, f)`. A 4.x bug on the way: a captured
   `this` was never lowered.
8. S8: `_` in a list of type arguments (03 T8), `ident<int>` as a value (T17), the expected type
   binding what the arguments leave open (`let xs: int[] = empty();` — a 4.x gap T8 lists);
   one C unit per generic instance, content-named, its object never recompiled for an edit
   that leaves it alone (01 C3), every function with external linkage (C4). Tried and left
   out: ThinLTO (C2) — the zig cross-link to Windows loses the mingw libm under it.
9. S9: the conformance runner for `lyric5` (lyric-spec `tools/`), the *Conformance gate* job —
   lyric-spec `main` against the working tree in both profiles, red between a spec merge and
   its compiler merge by design — and `bench/` with measurement point 2 (below).

Conformance: 176 cases (03-types 143, 04-modules 4, 09-patterns 29), all `since: 5.0.0`, green
in both profiles. Test suite: 3420 across the ten projects.

Measured (WSL2 x86-64, release profile through `zig cc 0.16`, Go 1.27, .NET 10; minimum of 5
runs, wall time of the whole process; `bench/run.py`):

| | Lyric | C | Go | C# | Lyric / C | Lyric / Go | Bound |
|---|---|---|---|---|---|---|---|
| `loops` — 200 M checked int passes | 0.676 s | 0.699 s | 0.695 s | 0.695 s | 0.97 | 0.97 | ≤ 3× C, ≤ 1.5× Go |
| `arith` — Mandelbrot 2000², 200 iterations | 0.331 s | 0.290 s | 0.338 s | 0.330 s | 1.14 | 0.98 | ≤ 3× C, ≤ 1.5× Go |
| `structs` — 100 k particles × 2000 steps | 0.162 s | 0.160 s | 0.252 s | 0.218 s | 1.02 | 0.64 | ≤ 3× C, ≤ 1.5× Go |

The bound against C is a ratchet test on Linux (`MeasurementTests`); the bound against Go is
`bench/run.py --ratchet`'s. The checked integer arithmetic costs nothing measurable against
C's unchecked one; the 14 % on `arith` is the loop's shape after the IR, a number to beat in
the optimizer round.

Open from M3, not in the plan: unification with a literal (T8's `same(1, s)` → `T = string`;
the checker binds the first argument), same-scope `let` shadowing in the 4.x front end (the
second binding is accepted and calls resolve to the first), local `fn` / `inline fn` / the
`it`-shadowing warning of 08 Y11, `?E == E` and the `[x] * n` gate wait for M4's synthesis.

### M4 — done (2026-10-01)

Merged as #198–#208 and #209 (S9), the spec side is lyric-spec#58–#62, #64–#70.

The plan (13, M4): 04 complete — interface values as fat pointers with tables per row, boxing
at the transition, generic extends on `T[]`/`?T`/interfaces, associated types, the operator
interfaces (D6), `by`-delegation, overloading by arity, named arguments, `Display`/`Debug` in
the compiler (D7), `Point(1, 2)`, `sealed`; the synthesis of `Equatable`/`Hashable`/`Clone`/
`Default` provisionally in the compiler. Nine slices: S1 interface values, S2 calls, S3 the method
set, S4/S4b `Self` and associated types, S5a/S5b `Any` and `sealed`, S6 the operator interfaces,
S7a/S7b generic extends, S8 synthesis, S9 the close. Exit criteria:
`interfaces/shapes/objects/vectors/closures/lambdas` run natively; conformance 04 (chapter 05).

1. S1 `eadfb0c8`: an interface value as a fat pointer `{ object, table }` (01 V7), a table row
   per (type, interface), a scalar or struct **boxed** at the transition and never anywhere
   else; an interface's members public always.
2. S2 `7af880d7`: overloading by arity (04 D2 R1), arguments by name, defaults filled per call;
   a default cannot read `this`.
3. S3 `6a6f91fe`: one method set per type (04 D2/D3) — a name is one function, two conformance
   blocks implementing it scope it to the interface; the qualified call `Walker.walk(d)`;
   delegation `:: [Walker by legs]` (D1).
4. S4 `a21dd1ef`, S4b `d9bf7da1`: `Self` (03 T5), static members through a constraint
   (`T.parse(s)`), default type arguments; associated types `type Item` with an answer per
   (conformer, conformance instance), fixations `Container<Item = int>`, `Self.Out`.
5. S5a `6b8c0895`, S5b `e74d856e`: `Any` in `std.core` (03 T10) — every struct, class and enum
   is one undeclared —, `is` with narrowing, type patterns, the checked downcast; `sealed`
   (04 D8) with the witness in the error, the child interface value as a parent value (D10).
6. S6 `c9d53996`: the operator interfaces of `std.core` 5 (04 D6): `Add<Rhs = Self> { type
   Out = Self; … }` and its siblings, `Equatable`, `Ordered :: [Equatable] { compare(o): ?Ordering }`,
   `TotalOrder`, `Hashable`, `Display`; the scalars conform; one resolution by the right operand.
7. S7a `b7ea9d9f`, S7b `7182438c`: generic extends `extend<T :: [Display]> List<T> :: [Display]`
   (03 T7 X1) — conditional conformance, coherence per type and interface instance (X3/X4), no
   orphan rule; the built-in constructors as targets (X2): `T[]`, `?T`, tuples, `Slice<T>`,
   members only.
8. S8 `fb8445fa`: conformance synthesis (04 D7, 05 §14) — the family written without a body
   generated as source and compiled like a written block; generic types conditionally; a
   written member replaces the synthesis; `Debug` unasked where the fields allow, probed
   muted and withdrawn where they do not; diagnostics from synthesized text re-pointed at the
   declaration with the synthesized line. `std.core` gains `Debug`, `Clone`, `Default`.
9. S9: the M3 leftovers that waited for synthesis — `?T == ?T` through the value's equality
   (03 O6), `[x] * n` over an object cloning every slot (10 C7) —, both desugared to a
   `std.core` call; the `lambdas` program the plan names; this section.

Conformance: 304 cases (03-types 149, 04-modules 4, 05-interfaces 98, 08-expressions 17,
09-patterns 36), all `since: 5.0.0`, green in both profiles. Test suite: 3643 across the ten
projects.

Open from M4, collected for the 5.0 review (not in the plan): the conformance of a shape
(`T[] :: [Display]`) and with it `Hashable` on `?T` and `Clone` on `int[]` wait for the
collections (M8a); `@Shared` on a field (M9a); a `Default` of an enum has no variant to take;
`Iterator<Item = int>` as a value form (M8a); `string`'s `Ordered`/`Hashable` with the string
round (M8a); the operator desugars bind `equalOptionals`/`repeatArray` by name through the scope
(M7 decides the qualified route); `T.Out` with two instances takes the first answer.

### M5 — done (2026-10-02)

Merged as #210–#219 and #220 (S5), the spec side is lyric-spec#71–#80.

The plan (13, M5): 05 with L5 — the hidden error slot (E1–E3), `throws` sets,
`try`/`try?`/`try!`, `catch` by type and `in [A, B]`, the `Error` root and `Exception`, the
`defer` cleanup chain (E4), `using let`/`Closeable` (the R series), `main throws`, panics (E5,
101), the backtrace profile (E8), `never`. Eleven slices: S1a/S1b the error ABI from the front
end to C, S2a/S2b the expression forms and the clauses, S3a–S3c the throwing `defer`, `using` and
the trace, S4a–S4c function types with sets, `never` and `loop`, S5 the close. Exit criteria:
`bank` and `errors` run natively; conformance 05 (chapter 06); a sanitizer run over the throw
paths.

1. S1a `efe5c92c`: `Error` in `std.core` as the root of everything thrown, caught and declared
   (05 E6 O1); the `throws` SET — one type alone, several in brackets, bare `throws` meaning
   `Error`; the `try` mark on every call of a throwing function (`LYR-SEM0138`), at the start of
   what it covers (`LYR-PAR0050`); a thrown type covered by a clause or the function's set (K8).
2. S1b `b06fa96a`: the error path natively — `Throw`, `ErrorBranch` and `Propagate` as explicit
   edges of the IR, the 4.x handler tables retired; landings are defer chains ending in the
   innermost `try`'s dispatch (one descriptor test per clause) or in the propagation; in C a
   hidden `LyrErr **` last parameter and one branch per call; an error leaving `main` is
   reported with its cause chain, exit 1.
3. S2a `f51a88c1`: `try?` (an absent value, no flattening), `try!` (a panic, `LYR-RT0010`), and
   `try e catch (…) v` as an expression with value-block clauses.
4. S2b `7d3e2abc`: `catch (e in [A, B])`; a type caught once per `try` (C1), a clause no value
   reaches refused (C2); the set a binding carries (K7) — precise rethrow, `match` exhaustive
   over it. The conformance runner compares error codes exactly.
5. S3a `72bb85be`: a `defer` that throws — the first error wins, the second is suppressed into
   it (05 E7); `main`'s report lists what was suppressed.
6. S3b `bd82bf8d`: `using let` over `Closeable` (R1–R6) and `Exception` as the ready-made error
   (its field is `text`: `message` would collide with `Error.message()`).
7. S3c `4b994488`: where an error was thrown, in the debug profile (01 E8), folded and cut like a
   panic's trace; a rethrow of the clause's own binding keeps its record (05 E6 O3).
8. S4a `7d6bf240`: the thrown set of a function type (03 T17, K3/K4/K6) — written, expected or
   inferred for a lambda, `throws E` bound from the argument, a smaller set where a larger is
   expected; every function value's code takes the error slot, so the coercion costs nothing.
9. S4b `ae238e7a`: `never` only as the whole return type (`LYR-SEM0145`), a never function does
   not return (`LYR-SEM0146`); `panic`, `assert`, `unreachable` and `todo` in `std.core`
   (`LYR-RT0008`, `RT0011`–`RT0013`); a never value in any value position ends the statement
   (it was an ICE).
10. S4c `57a6374b`: `loop` with `break value` and labels (05 E11, 08 S3/S4) — `never` without a
    break, `LYR-SEM0147`–`SEM0149`, a jump held to its own function; definite assignment follows
    the jumps, which closed a 4.x hole: a `break` or `continue` in a `do` loop slipped past an
    assignment. Spec chapter 07 gets its first section.
11. S5: the `bank` program (after `examples/bank.lyr`: an account's errors, a transfer that puts
    the money back, `try?`); the throw paths — thirteen programs, errors leaving `main` and a
    `try!` included — under ASan and UBSan in the C toolchain job, where UBSan also holds every
    `__builtin_unreachable()` the lowering sealed to never being reached (a planted one is the
    control); this section.

Conformance: 409 cases (03-types 149, 04-modules 4, 05-interfaces 98, 06-errors 87,
07-statements 18, 08-expressions 17, 09-patterns 36), all `since: 5.0.0`, green in both
profiles. Test suite: 3887 across the ten projects.

Open from M5, collected for the 5.0 review (not in the plan): `e.suppressed()` and
`e.backtrace()` in Lyric (the record is not reachable from a caught value); the error slot on
every function value (one pointer per indirect call) to confirm; `?never` decided as an error
even in a return position; `assert` evaluates its message eagerly until `inline` (M9);
`std.core`'s higher-order functions pass no set yet (M8c); a block expression at the end of a
value block — `if`, `match`, `loop` alike — is a statement, not the block's value; spec 07 past
§1 and the grammar chapter are still to write.

### M6 — done (2026-10-02)

Merged as #221–#242 and #243 (S8); the spec side is lyric-spec#81–#104.

The plan (13, M6): L4 and 06 — stackful coroutines (the switch per ABI, stacks with guard pages,
the foreign-frame count), `park`/`unpark`, a scheduler per thread, the poller in C, `spawn`/
`Task`/`TaskScope`/`spawnDetached`, `Channel`/`Select`/`Timer`/`sleep`/`timeout`, `Cancelled`,
threads with `Mutex`/`RwLock`/`Once`/`Atomic`, generators as iterators, signals as a channel.
Twenty-four slices. Exit criteria: `generator.lyr`, a channel ping-pong, a thread-pool test, a
Ctrl+C shutdown example; conformance 06 (spec chapter 10); the TSan profile green.

1. S1 `faf3284b`: stackful coroutines in the runtime (01 L4): the switch in assembly per ABI,
   stacks with guard pages from a pool, the foreign-frame count, a suspended coroutine's stack
   scanned as its own kind of object (a dropped one dies with it), the switch under Boehm as a
   reader of a two-atomic lock whose writer is a collection.
2. S2a–S2d (`39a11f25`, `9a541b10`, `16de95ee`, `150d8a48`): generators natively —
   `Coroutine<Y, R>` with `next()`, `result()`, `isDone()`; `close()` throwing `Cancelled` at the
   suspended `yield` (06 A5) and `using` on a coroutine; generator lambdas and `sequence { }`; a
   function is a coroutine by its type and a `yield` of its own (08 D11); a `yield` in a function
   a coroutine calls, checked at run time against the yield type. S2e (spec only): a coroutine
   has identity.
3. S3a–S3e (`3cd5599f`, `931735b7`, `dced5e2b`, `4876f356`, `4f31b0bd`): `park` and the monotonic
   clock; a poller per thread (epoll + eventfd, kqueue, an event on Windows); the scheduler in
   Lyric (06 N6 S1) with `main` a task where the program waits, `sleep`, `yieldNow`,
   `spawnDetached`; `void` as a type argument; `spawn` and `Task<T> throws E` with `await()`.
4. S4a–S4d (`c36f7622`, `5b853c57`, `fae91a66`, `ac34abf9`): cooperative cancellation — every wait
   of a cancelled task throws `Cancelled` (06 N9); `TaskScope` (T5); `withTimeout`/`TimedOut`; a
   panic leaves its coroutine (05 E8): a task's panic ends the task, `status()` shows `Panicked`,
   `await()` panics again.
5. S5a–S5b (`ef92a32c`, `c588b62e`): `Channel<T>` with `send`/`recv`/`close`; `Select.on(c) { … }`
   with trailing lambdas, `timeout(d) { … }`, `Timer.after(d)`. Found and fixed on the way: a
   trailing lambda at the start of a statement kept the statement's ban on struct initializers
   for its whole body (`241f6651`).
6. S6a–S6d (`f1759dae`, `784bf296`, `72e834ca`, `a0576993`, `cd3559eb`, `0c4acb7c`, `96f23793`):
   `std.sync.Atomic<T>` over the C11 builtins; a wait protocol for threads — a waker claims the
   context with a compare-and-set and posts it to its own scheduler, the woken context leaves its
   places itself; `Thread.spawn` with an inbox per scheduler; channels and select across threads
   (a lock per channel, a select locking its channels in id order); `Mutex`, `RwLock`, `Once`;
   `Pool`. Found and fixed: a cancel from another thread could be lost between a wait's check and
   its beginning; TSan's model of the collector missed an explicit free (`50c7e50b`).
7. S7 (`08958aac`, `4ce61814`): signals as a channel (10 Q9) — the handler marks and wakes a
   watcher thread through a self-pipe, never Lyric code; `signals(Signal.Interrupt, …)`.
8. S8: this section.

The artifacts: `generators`, `channels` and `thread_channels`, `pool`, `shutdown` (driven by
`SignalTests` with `kill -USR1`/`-INT`) in `tests/Lyric5.Tests/programs`. Every threaded program
runs clean under TSan locally (`LYRIC5_SANITIZERS=all`), stressed; in CI the threaded runs stay
out, as with M1's open thread below, and the single-threaded ones are in.

Not done from the plan, decided on the way: generators as iterators (I7: `for (x in co)`,
`Coroutine :: [Iterator]`) move to M8a, where `for` learns every iterable at once (S2e); the
Windows poller has no sockets until M8b S11 (a binding to AFD of its own; wepoll was dropped,
06 M6-29). `Thread`, `Pool`, the locks and the signals lived in `std.task` until M8b S1 cut
`std.thread`, `std.sync` and `std.os` out of it.

Conformance: 549 cases (03-types 151, 04-modules 4, 05-interfaces 98, 06-errors 88,
07-statements 18, 08-expressions 17, 09-patterns 36, 10-concurrency 137), all `since: 5.0.0`,
green in both profiles. Test suite: 4060 across the ten projects.

Open from M6, collected for the 5.0 review (not in the plan): the select is biased to its first
case — a closed channel in front starves the rest (Go picks at random); a task that panics inside
a lock's body leaves the lock held (Rust poisons); unsubscribing from signals is lazy; `inout`
parameters (T12) would let a lock hand out a place instead of a guard; every `std.task` program
carries the module's types and globals (type pruning); `thread.spawn` (T1) collides with
`Thread.spawn` (SEM0085); the spin locks and the wake-all of the locks are unmeasured.

### M7 — done (2026-10-02)

Merged as #244–#260; the spec side is lyric-spec#105–#121.

The plan (13, M7): 07 and W2 — visibility `private`/`internal`/`pub`, the module's name from its
path, `pub import`, the prelude, editions, `lyric.toml`, path and git dependencies, MVS,
`lyric.lock` with hashes, `[native]`, profiles, targets, the `out/` lock, the reproducibility
test, `lyric metadata`, `lyric clean/add/update`, `build.lyr`. Exit criteria: a multi-package
example with a git dependency builds offline; reproducible twice from two directories; a
cross-build linux→windows.

1. S1 (#244): the package core — `lyric.toml` read by a TOML subset of our own (`[package]` name,
   version, edition), `src/main.lyr` the program, a module named by its path, the header refused,
   a single file a package of its own with `std` alone; the C cache keyed by every source.
2. S2a–S2c (#245–#248): visibility — three words, `internal` the default, one predicate on
   every resolution route; fields, methods, extend blocks, interface helpers; a declaration no
   more visible than the types it names (SEM0151), members no more than their type (SEM0152).
3. S3a–S3b (#249–#250): `{ f as g }`, `pub import`, one unused-import rule; `std.prelude`, one
   namespace per module, the builtin names. S3c (moving `Thread`, the locks and the signals out of
   `std.task`) went to M8b: `std.task` ↔ `std.sync` is a cycle until `Atomic` moves down.
4. S4 (#251): path dependencies, one package per name, `[override]`, imports only from what a
   manifest declares (RES0014); a library builds as a check.
5. S5a–S5b (#252–#253): git dependencies through the user's cache, `--offline`, the package
   content (P8); minimal version selection over tags, `lyric.lock` with commits and `h1` hashes,
   `lyric update`.
6. S6a–S6e (#254–#258): profiles with `inherits` and their six fields (`overflowChecks = false`
   is 03 T2's explicit switch), field flags, `LYRIC_PROFILE`; `[[bin]]` and `--bin`; the toolchain
   pin; `out/.lock`, `lyric clean`, `lyric metadata`; `lyric add`/`remove`; `[native]` and
   `extern "C"` for scalars (stage 1 — M14 completes the type table).
7. S8a (#259): reproducibility — one package from two directories a second apart, the same
   bytes in debug and release and for each operating system's format: `-ffile-prefix-map`, `.` as
   the compilation directory, a generic instance's unit named apart from where it lies, a PE
   image's stamps and PDB GUID from its own bytes, a Mach-O image without its debug map (the dSYM
   beside it on a macOS host).
8. S8b (#260): this section, the example, the init order across packages.

The artifacts: `tests/Lyric5.Tests/programs/three_packages` — a program, a path dependency and one
from git — built online once, then offline with the repository gone, then for the other
operating system (`ExampleTests`); `ReproducibilityTests` for P6.

Not done from the plan, decided on the way: `build.lyr` (BS1–BS6) waits for M8b — `std.build`
works through `std.fs` and `std.process`, which come with it; S3c went to M8b as well. The
editions exist as the field (`edition = "5"`, 07 V9) with the one edition there is.

Conformance: 637 cases, all `since: 5.0.0`, green in both profiles — 03-types 151, 04-modules 59,
05-interfaces 98, 06-errors 88, 07-statements 18, 08-expressions 17, 09-patterns 36,
10-concurrency 137, 13-abi 3, 15-project 30. Test suite: 4395 across the ten projects.

Open from M7, collected for the 5.0 review (the PR bodies say more): the root module of a
package (`import geo` alone); package cycles; a git URL's identity as written; a package from git
at its repository's root only (no monorepos); MVS counting every version read; `lyric update`
raising no version and no `--locked`; `extern "C"`'s stage-1 types; the `lyric5` CLI codes beside
the 4.x catalogue; reproducibility checked on one machine at a time (zig's own libunwind keeps
the directory zig built it in; gcc maps the working directory, into its cache keys).

### M8a — done (2026-10-07)

Merged as #261–#307 (S1–S11a) and, after the review of 2026-10-05, #308–#373 (the blocks R0–R9,
then S12–S16); the spec side is lyric-spec#122 onward. Slice by slice (each PR says what it did
and how it was checked):

- **Tests:** `lyric test`, `@Test`, `std.test` with `@callerExpr`, subtests, the watchdog (S1).
- **Language the library needs:** `&x: T` (S2a), `static let` in interfaces and blocks (S2b),
  blanket and form blocks with their conformances (S2c), defaults per conformer (S2d), the
  variadic parameter `nums: int...` (S9b).
- **Numbers (B5):** the tower `Num`/`Signed`/`Integer`/`Float`, checked/saturating/wrapping,
  `parse`, float methods, the shortest float text, `std.math` (S3).
- **Iteration (B6):** `Iterator`/`Iterable` with `type Item` and `type Error`, `for` over every
  iterable, closing, `for (x in try it)`, the adapters as blanket extends, the join of two
  thrown types, the terminators (S4).
- **Arrays and views (C2, C10):** their members, `arrayOf` in linear time, the sorts (S5).
- **Hashing (K2, C4, C5):** `Hasher` with SipHash-1-3 and FNV, `Hashable` streaming into a
  hasher, `Map` as a Swiss table (S6).
- **Collections:** `Index`/`IndexSet` (B4), `List` (C3), a map's walks, `Set`, `toList` (S7);
  `Deque` (C6) and `Heap` (C1) (S11a; the least first since the review's A1).
- **Strings, first and second part (B9 S1–S3):** search, split, trim, replace, pad,
  `StringBuilder`; a `char` literal beyond the basic plane (S8).
- **The prelude naming the collections (B2)** (S9a); **the format language (08 Y7, 10 S7)**,
  checked where a spec is written (S9c).
- **The artifact:** `lyric test` runs the std tests (147); the examples inventory, stats and
  stack build and run as packages (S10a); measurement point 3 is measured (S10b, S10c) and
  **not met**: maps 1.90×, sorting 1.53×, strings 2.53× Go, ahead of C# in all three. What
  bounds it — no inlining across translation units, the map's three arrays, f-string
  allocation — is in `bench/README.md`.

**The review of 2026-10-05.** After S11a the maintainer went through the open points of M4 to
M8a and decided them: 110 decisions, written into the area documents, each in a section "Review
2026-10-05". They re-cut the milestone (13, the addendum there):

- **M8a closes with** the review's decisions — blocks R0 (the spec pin, these texts, test
  hygiene), R1 (the compiler's footing: a depth limit, the emitter's tests as packages, names
  instead of numbers in the C, lowering only what is reached), R2 syntax, R3 names and modules,
  R4 types and inference, R5 errors and generators, R6 concurrency, R7 std corrections, R8
  packages and the command line, R9 performance — and then with what M8b needs of the library:
  `StringView`, `Pattern`, `fromUtf8` (S12); `showTo`/`debugTo` and f-strings writing into one
  builder (S13); containers' `Debug`, `Display` and `Equatable` (S14); `collect`/`FromIterator`
  and `x in xs` (S15); the close with measurement point 3 measured again (S16).
- **Built from the list the review left M8a** — all of it: the members of `T[N]` (R4e),
  `copyInto` (R7c); text as a view, `StringView` and its members, `Pattern` (`char`, `string`, a
  view, `fn(char) -> bool`) for search, split and replace, `string.fromUtf8`, `Utf8Error`,
  `fromUtf8Lossy`, function types as extend targets (S12); `Display.showTo` and `Debug.debugTo`,
  f-strings writing into one builder, `[x] * n` by doubling (S13); `==`, `Debug` and `Display` for
  arrays, views and every container — a set and a map equal in any order —, a shape conforming
  through its block comparing and rendering (S14a); `withCapacity` on every container, `Heap.of`
  (N3's name for the review's `Heap.from`, S14b); `FromIterator`, `collect()`, `toSet()`, a fixed
  associated type read inside every type (S15a); `x in xs` and `x !in xs` through `Contains<T>`,
  a call's receiver evaluated before its arguments (S15b).
- **Measurement point 3 again (S16):** maps 1.36×, sorting 1.21×, strings 1.70× Go
  (S10c: 1.90, 1.53, 2.53), ahead of C# in all three; maps and sorting within the bound, strings
  over it — on the CI runner 0.99×, 1.09× and 1.80×. The numbers and what moved them are in
  `bench/README.md`.
- **The open points, decided (2026-10-07):** what the run R0–S16 collected, gone through with the
  maintainer — in the design documents 01, 03, 04, 10 and 11, each in a section "Review
  2026-10-07", with their clocks (M8c, M12, M10/M11). Built at once: release traces stated as best
  effort (spec 13 §1.4), CI's platform jobs and benchmarks on `main` only, `strings` under the
  common fence; `arrayOf` filling in one allocation, the `spawn` test waiting instead of sleeping.
- **Found at the close (S16):** no map over objects compiled since R9d — its table filled by
  `[empty] * n`, which a generic body passed and every instance over an object refused as a
  compiler bug (`LYR-CG0001`); no test held one. Now a generic `[x] * n` asks for `Clone` (03 §5.1),
  and the containers fill through std's own `filled`, shared, one allocation.
- **Not built, and M8c's** (after M8b): the Unicode tables, `char`'s predicates, `toUpper` and
  `toLower`; `mapNotNull`, `flatMap`, `flatten`, `chunks`, `windows`, `dedup`, `scan`,
  `peekable`, `rev`, `cycle`, and lambdas that throw inside adapters; `toMap`/`sorted` and the
  other collecting terminators beyond `collect` and `toSet`; the set operations, `map[k]`,
  `list[a..b]`, `+` and `*` on collections; `Result`, `From`/`Into`; `std.fmt.format` at run time; an interface with a
  fixed associated type as a value; `suppressed()` and `backtrace()` at a `catch` binding;
  `@Bench` and `lyric bench`.
- **Clocks the review set:** measurement point 3 (at the close of M8a: maps and sorting within it,
  strings at 1.70×) holds its bound of 1.5× Go and is to be met
  after M11, or decided again; the prologue's stack check and `Atomic` over a class come with
  M11; the notes on hidden candidates and the lint for foreign conformances with M12.

### M8b — done (2026-10-08)

The plan is design 13's addendum of 2026-10-07: the boundary to C (P1), waiting through the
poller and an I/O pool (P2), the console (P3), handles (P4), the platforms (P5), the spec's
chapter 12 (P6), and the slices S1–S14 with their artefacts. Approved like M8a: no halt until the
close, each slice merged on green CI — a slice of platform code on a dispatched run on every
platform as well —, the open points collected for the report at the close.

Merged as #374 onward, the spec side lyric-spec#221 onward; slice by slice (each PR says what it
did and how it was checked):

- **S1, the module cut** (10 Q9, Q10): the locks in `std.sync`, with `Semaphore` (06 K4, the one
  waker of 5.0); `Thread` and `Pool` in `std.thread`, with `parallelMap` (06 P5) on the program's
  own pool; the signals in `std.os`. What they stand on stays in `std.task`, internal to std.
- **Before S2, the catch-up block N.** An audit of M0–M8a against `main` (2026-10-07) found what
  the earlier milestones decided and did not build, or built wrong — mostly decisions of the area
  documents that never reached a plan row or a list, so they had no clock. N builds them: N1 the
  faults, N2 the language's gaps from M3/M4, N3 the texts; the rest got a clock each, decided with
  the maintainer. **N1a**: `s * n` on text, `opaque type` refused, a generator without a yield
  closeable, a closed scope takes no task, a name bound once in a scope — a parameter counting as
  bound in the body's block. **N1b**: a module binding nothing reads goes when its initializer
  only gives its value (01 B13 — hello has no global any more); a panic's trace starts at the
  program's text, whatever its package is called; no message names a finished milestone.
  **N2a**: tuples of two to eight elements compare, hash and print where their elements do;
  optionals are Equatable and Hashable; arrays and the containers are Clone — a shape's block
  may now leave a parent interface to another block on the same shape (05 §13 rule 6).
  **N2b**: an alias takes type parameters, `type Pair<T> = (T, T)` (03 T15); a class listing
  `Identity` compares by the object and hashes its address (02 M10), and the prelude passes
  `Identity` on. A coroutine's `Identity` (06 A7) is N2d's: blocks on `Coroutine` have gaps of
  their own.
  **N2c**: a name alone is its field, `Point { x, y }` (02 I3) — after a type one name alone
  too, where the parser reads a trailing block and the checker decides; a pattern takes a type
  set, `s in [Circle, Rect] =>` (08 Y6), whose binding carries the set as a catch binding's does.
  **N2d**: a coroutine is `Identity` (06 A7) — `==` is `same`, a set of coroutines finds one twice,
  whatever its pulls throw; a block on `Coroutine<Y, R>` is a shape's block, as one on `Slice<T>`.
  **N3a**: the texts — comments that promised a finished milestone, the CHANGELOG's M8a entry,
  this file's claims about M8a and wepoll, design 01/04/10/13; the audit's clocks stand in 13
  ("Nachtrag 2026-10-07 — Prüfung M0–M8a und der Nachholblock N").
  **N2e**: the literals 08 Y7 decided — raw strings `r"…"`/`r#"…"#`, multi-line `"""…"""` without
  the closing line's indentation, byte strings `b"…"` (a `uint8[]`), `fr"…"`/`f"""…"""`; `\x` is a
  byte string's alone; `module` and `params` are names; spec chapter 01 written (15 cases).
  **N3b**: the spec's core rules — 08 precedence (a comparison and an equality do not chain, now
  a parse error), evaluation order, `++`/`--`; 07 `if`/`while`/`do` (a body without braces said
  once); 04 the order across modules; the chapter heads. **The catch-up block is done.**
- **S2** io core: `IoError`/`IoErrorKind` (10 O3; the field `cause` is `inner`, as `Exception`'s),
  `Reader`/`Writer`/`Seek`/`SeekFrom`, the defaults (`readExact`, `readToEnd`, `readToString`,
  `writeAll`, `writeString`), `copy`, `ByteReader` and `ByteBuffer` — all in Lyric; spec 12
  "Input and output", seven cases.
- **S3** buffers and text (10 O2): `BufReader`, `BufWriter` (`Closeable`, `close()` flushes; the
  R3 warning when nothing closes it), `TextReader` (`readLine`, `lines`, `chars`, `readToEnd`,
  `skipBom`; a split codepoint waits for its rest, bytes that are no UTF-8 throw `InvalidData` at
  their offset), `TextWriter`, and `Writer.flush()` (B8). **Classes, not O2's structs with inline
  bytes**: every read hands the inner reader a view of the buffer, and a frame's inline array
  has none (03 §5.3). Spec 12 rules 6–8, nine cases.
- **S4a** encodings (10 Q5): `std.encoding` with `EncodingError { offset, detail }`, `Base64`,
  `Base64Url` (RFC 4648, padded, decoded strictly), `Hex`, `Utf16`; `fromBytesLE`/`fromBytesBE`
  on every integer type and the twelve getters on `Slice<uint8>`. 4.x's `std.bytes` goes up in
  it. Spec 12 "Encodings", seven cases.
- **S4b** cryptography (10 Q4): `std.crypto` with `Digest` (`size`, `blockSize`, `new`, `update`,
  `finish(): uint8[]` — Q4's `uint8[N]` names a length no interface can, 5.0 having no generic
  constant), `Sha256`, `Sha512`, `Sha1`, `Md5`, `sha256(bytes): uint8[32]`, `Hmac<D>`,
  `randomBytes`, `randomUint64`, `constantTimeEq`; the constants computed from their definitions,
  the vectors checked against Python's hashlib. Spec 12 "Cryptography", five cases.
- **S4c** random numbers (10 Q2): `std.random` with `Random` (ChaCha8; `seeded`, its stream fixed
  by spec 12 for every 5.x; `fresh`), its draws, `shuffle`, `choice`, `sample`, and the free forms
  over the thread's own generator, kept with its scheduler in `std.task`. Q2's `random.int` and
  `random.float` are names 07 K5 gives no declaration; the free forms carry `Random`'s names.
  Spec 12 "Random numbers", six cases. **S4 is done.**
- **S5a** clocks and spans (10 Q1, first half): the runtime's wall clock (`lyr_clock_realtime_ns`:
  `CLOCK_REALTIME`, `GetSystemTimePreciseAsFileTime`); `Duration` complete — arithmetic, order,
  hash, default, Go's text and Go's `ParseDuration`; `Instant` (`now`, `epochSecs`/`epochMillis`,
  `+ Duration`, `since`) and `Monotonic` (`now`, `elapsed`). Spec 12 "Time", five cases.
- **S5b** the calendar (10 Q1, second half): `Date`, `Time`, `DateTime`, `Weekday`, `Zone.utc` and
  `Zone.fixed`, `TimeError`; the proleptic Gregorian calendar through Hinnant's civil algorithms,
  checked against Python's `datetime`; RFC 3339, read strictly, for `Instant` and `DateTime`.
  Spec 12 "Time" rules 5–8, five cases. **S5 is done**; `Zone.load`, `Zone.local` and the format
  patterns are M10's.
- **S6a** paths (10 O6): `std.path` as Go's `path/filepath`, lexically — `join`, `normalize`,
  `fileName`, `parent`, `extension`, `stem`, `withExtension`, `isAbsolute`, `relative`,
  `components`, `separator` —, Windows' volumes and both separators included; checked against
  `go run` on POSIX. The runtime says which system it was built for (`lyr_os_platform`, with a
  runtime test). Spec 12 "Paths", four cases.
- **S6b** files (10 O4, O5; P1, P2, P4): the runtime's file calls (`runtime/src/fs.c`, POSIX and
  Windows — one system call each, EINTR taken again, the system's error as a kind and a code);
  `std.fs` with `File` (`open`, `create`, `openWith(OpenOptions)`; `Reader`, `Writer`, `Seek`,
  `Closeable`; `closed` throws `Closed`) whose calls run on the program's I/O pool, the task parked;
  a file's call is no cancellation point (`awaitThrough` in `std.task`). Asking whether a scheduler
  runs makes no task of `main` (`schedulerRuns`; the compiler's walk for M6-26 leaves its yes
  out). A program over the pool runs under ASan and, locally, TSan. Spec 12 "Files", five cases.
- **S6c** the conveniences (10 O5): `fs.readText`, `readBytes`, `writeText`, `writeBytes`,
  `appendText` — each closes its file on every way out — and `fs.lines(path)`, a
  `FileLinesIter :: [Iterator, Closeable]` that closes its file at its end, at its error and when
  a loop leaves it; the decoder's `InvalidData` gets the path. The kinds a file can give, each
  checked with its path; checked on Windows for real through WSL interop. Spec 12 "Files" rules
  6–7, three cases. **S6 is done.**
- **S7a** directories, first part (10 O5): the runtime's directory calls (`fs.c`, POSIX and
  Windows: stat through links, mkdir, rmdir, unlink, rename, a directory's entries, realpath and
  `GetFinalPathNameByHandleW`, the working directory); `fs.metadata` (`Metadata`), `exists`,
  `createDir`, `remove`, `removeDir`, `rename`, `readDir` (`DirEntry`, a `Closeable`
  `ReadDirIter`), `canonicalize`, `absolute`. Checked on Windows for real through WSL interop.
  Spec 12 "Directories", four cases. S7b: what is put together from these.
- **S7b** trees (10 O5's rest): `createDirAll`, `removeAll` (links removed, never followed),
  `copy` (refused onto itself), `walk` (depth first, by name, nothing held open), `tempDir` and
  `tempFile` (new and the program's own, their paths; the lookup is S9's `os.tempDir`). The
  runtime: `stat` without following, an exclusive create, the system's temporary directory. The
  plan's check — a tree made, walked and removed — in the std tests on every platform they run on
  and as a case. Spec 12 "Directories" rules 5–8, three cases. **S7 is done.**
- **S8a** the console's output (10 O9; P3): `print`, `println`, `eprint`, `eprintln` over
  `Display`, `flush()`; both streams buffered in the runtime (`runtime/src/console.c`, 8 KiB each
  under a lock — a line at a time at a terminal or a host's writer, a block otherwise) and
  flushed at every end: `main`'s return, a panic's report, an error leaving `main`. The flush runs
  on the I/O pool (moved to `std.io`) where a scheduler runs. A program that sets no scheduler
  keeps nothing of the pool (`SchedulerFree`, a compiler pass): hello stays at 1.35 MB. Spec 12
  "The console", four cases. S8b: `stdin()`, the streams as `Reader`/`Writer`s.
- **S8b** the console's input and streams (10 O9; P2): `stdin()` — one for the program, made at
  the first call, a `Reader` with `readLine()` and `lines()` through a buffer of its own, read on
  the I/O pool where a scheduler runs; the runtime reads a pipe or a file as bytes and a Windows
  console through `ReadConsoleW` as UTF-8. `stdout()`/`stderr()` are `Writer`s over print's
  buffers. One map of the runtime's failures to all fifteen kinds, in `std.io`. The conformance
  runner learned `//! stdin:`. Spec 12 "The console" rules 4–5, three cases. **S8 is done.**
- **S8c** the console after the review of 2026-10-08 (10 "Review 2026-10-08", the maintainer's
  decisions on M8b's open points, recorded there and in 03 and 13): the standard error a line at a
  time wherever it goes; a flush of `print`, `println`, `flush()` or the one when `main` returns
  that finds the standard output a pipe nobody reads ends the program as SIGPIPE would (POSIX,
  where the runtime owns the signals; Windows goes on, as Go does), while a `Writer` of `stdout()`
  still throws `BrokenPipe`. Spec 12 "The console" rules 2, 3, 6; no case (the runner sees
  neither), `ConsoleTests` do.
- **S9** the system (10 Q9's rest): `os.args()` (on Windows from the command line as UTF-16),
  `env`, `envs`, `setEnv`, `cwd`, `setCwd`, `exit` (the console flushed, nothing else run),
  `platform()`/`arch()` (`Platform`, `Arch`), `homeDir`, `tempDir` (which `fs.tempDir` now asks),
  `hostname`, `cpuCount`, `pid`. Spec 12 "The system", six cases.
- **S10a** a reader's and a writer's call may be cancelled (10 "Review 2026-10-08", 1j): `Reader`,
  `Writer` throw `[IoError, Cancelled]`, their doc comments say why; the defaults, `copy`, the
  buffers and the text throw both, a `TextReader`'s lines `Error = Join<IoError, Cancelled>`. What
  never waits without bound declares `IoError` alone (memory, `File`, the console); `std.fs` and
  `stdin()` keep `IoError` (`uncancelled`, a `StdinLinesIter`). Spec 12 rules 2–8 of "Input and
  output", "Files" 5, "The console" 4; four cases.
- **S10b** TCP over the poller (10 O7; P2): the poller arms a descriptor one-shot, each way apart
  (epoll's table re-arms the other way; kqueue's filters are apart anyway), its fired tokens taken
  after each wait; a task parks in `awaitReady` — a cancel ends it —, a token is a slot and a count,
  so a late readiness wakes nobody; a thread with sockets waiting asks the poller every 61 turns,
  and a socket's wait is no deadlock. `runtime/src/net.c` (POSIX; Windows Unsupported until S11):
  one call each, non-blocking. `std.net`: `IpAddr` (RFC 4291 read, RFC 5952 written), `SocketAddr`,
  `TcpListener`, `TcpStream`, `Shutdown`; a close wakes the waiters of its thread with `Closed`.
  100 connections on one thread. Spec 12 "The network", five cases. S10c: UDP, `resolve`, the echo
  server.
- **S10c** UDP, the resolver, the echo server (10 O7): `UdpSocket` (`bind`, `sendTo`, `recvFrom`
  with the sender, `localAddr`); `resolve(host)` through `getaddrinfo` on the I/O pool, each address
  once; a socket's `Waits` (std.task) holds its reader and writer, so a close on any thread ends
  their waits (the M8b open point 1t). **The echo server** — S10's artifact — serves each connection
  in a task of one scope and stops at Ctrl+C. It found a compiler bug: a task whose body never
  returns (`spawn` with `T = never`) failed in the lowering, which read the generic `T` — fixed
  (`Diverges` at the instance). Spec 12 "The network" rules 5, 7, 8 and 10's never-returning task,
  four cases. **S10 is done.**
- **S11** the network on Windows (06 S2, the review's M6-29): the poller a completion port a
  thread — `PostQueuedCompletionStatus` wakes it — and a socket's wait an AFD poll request, one a
  socket for every way armed on it (wepoll's and mio's rule; one a way hung where a socket waits
  both ways), cancelled and asked again on its completion when more ways come; ntdll's calls looked
  up at run time. `net.c` is POSIX's and Winsock's in one file. Every network test runs on Windows
  now, the echo server's Interrupt aside (a POSIX `kill`).
- **S12a** processes on POSIX (10 O8, Q9): `std.process` — `Command` (an options struct), `Stdio`,
  `Child` (`stdin: ?PipeWriter`, `stdout`/`stderr: ?PipeReader`, `wait`, `kill`, `signal`),
  `ExitStatus`, `Output`; `runtime/src/process.c` forks and execs (PATH looked up by the parent, the
  child doing only what is safe after a fork, SIGPIPE back at its default); a child's end comes as
  SIGCHLD, whose pipe a reaper thread watches, asking each child it waits for — never
  `waitpid(-1)` —, and closing a channel every waiter receives. The pipes wait on the poller. **The
  pipeline example** (S12's artifact): `printf | sort | uniq`, joined by tasks. Spec 12 "Processes",
  six cases. S12b: Windows.
- **S12b** processes on Windows: `CreateProcessW` — the arguments on one command line quoted as
  the C runtime reads them back (a Lyric child checks it: spaces, quotes, backslashes before a quote
  and at the end, an empty one), the environment a UTF-16 block, only the child's three handles
  inherited (a handle list); a child's end wakes the reaper's poller from a registered wait; the
  pipes block and run on the I/O pool; a kill is TerminateProcess (code 1), and there are no other
  signals. An end's encoding marks a signal by bit 40, since a Windows exit code is any 32 bits.
  **S12 is done.**
- **S13** the build script (11 W3 BS1–BS6): a package's `build.lyr` built as a program of its
  own (`build`, `build/`, its `[build-dependencies]` as a graph of its own in the same lock) and
  run before the compile; `std.build` (`target`, `profile`, `outDir`, `genDir`, `rerunIfChanged`,
  `linkLib`, `cFlags`, `compileC`, `warn`), the directives through a file; `gen/` the module
  space `app.gen.*`; a cache over the script, its dependencies, its inputs and `gen/`; a
  dependency's script only with the root's `[trust] build-scripts`. Spec 15 §8, five cases.
- **S14** the close: **the file tool** (`tests/Lyric5.Tests/programs/filetool.lyr`, M8b's
  artifact) — `stats`, `copy` and `find` over a tree through `fs.walk`, `fs.copy`, `std.path` and
  `os.args()`, a failure on the standard error with its path and code 1 —, built as a package and
  run by `ExampleTests`, and as a real Windows process. The dogfood: the std tests (293) and the
  examples at M8b's state, the tests' own three warnings gone. **M8b's I/O measured** (reported,
  no bound): a file of 128 MiB copied and read back 1.08×, a TCP echo of 256 MiB 1.01× Go — the
  numbers and the guess they beat in `bench/README.md`.

**M8b is done.** Every slice of the plan is built; what it moved on the way stands in its row —
`Zone.load`, `Zone.local` and the time's format patterns to M10; O2's buffers classes, not
structs over inline bytes (03 §5.3). The open points the run collected: those up to S8b decided
with the maintainer on 2026-10-08 (10 and 03 "Review 2026-10-08", 13 "Durchsicht 2026-10-08",
built in S8c and S10a); those after — the form of `IpAddr`, `resolve` no point of cancellation,
`fork`/`exec` for `posix_spawn`, a command's `env` over the parent's, the pipes' concrete types, the
reaper a thread, Windows' pipes blocking on the I/O pool — decided with the maintainer the same
day (10 "Review 2026-10-08 (2)"): confirmed as built, with two clocks for M10 — deadlines for
`resolve`, a Windows child's pipes through the completion port. M8c follows.

### M8c — done (2026-10-09)

The plan is design 13's addendum of 2026-10-08: the Unicode tables generated into C (P1), lambdas
that throw through 05 K4 (P2), the builder without `&` and `std.fmt.format` (P3), the language's
pieces as spec rules first (P4), the spec (P5), the platforms (P6), and the slices S1–S14 with
their artefacts — every clock that stood on M8c, each point checked against the code first.
Approved like M8b: no halt until the close, each slice merged on green CI, the open points
collected for the report at the close; before M9a the maintainer is asked again.

Merged, slice by slice (each PR says what it did and how it was checked):

- **S1** Unicode (10 B9 S4, S5, C2): the Unicode Character Database 18.0.0 checked in under
  `runtime/third_party/ucd/` (two files and the license); `tooling/unicode/gen.py` writes the
  runtime's tables — the general category in two stages, the simple case mappings, White_Space —
  and `std.string.unicodeVersion`, and CI checks that it would write the same. `char` has
  `category(): UnicodeCategory`, `isAlpha`, `isDigit`, `isAlphanumeric`, `isWhitespace`, `isUpper`,
  `isLower`, `isControl`, the four `isAscii*`, `toUpper`/`toLower`, `toDigit(radix)` and
  `char.fromDigit`. Every scalar value checked against a reading of the files apart from the
  generator. Spec 12 "Characters", five cases.
- **S2** text over Unicode (10 S1, S3, S4, S12c, S12d, S14): `trim` and its two ends take
  White_Space, `trimMatches` and its ends any pattern, again and again; `isBlank`; `toUpper`/
  `toLower` on text by the simple mappings; `toBytes()` a copy; `string.fromChars`; `chars()` is
  `DoubleEnded`, `charIndices()` gives the byte each character begins at; `parse<T>()` takes `T`
  from where the result goes. `Debug` of a text or a `char` writes it as it would be written —
  the quote, the backslash and the controls escaped, a character that does not print as `\u{…}`.
  `Contains<char>` on text went to S9: the generic `contains<P>` is no witness for it yet. Spec 12
  "Strings", seven cases.
- **S3a** two pieces of the language (03 S3, S15b): an array literal where a view is expected
  checks its elements against the view's element type, `take([1, 2])` for `Slice<uint8>`; two
  characters bound a range — `c in 'a'..='z'`, and walked, in a `for` head too, it gives the
  scalar values in between, the surrogates passed over. The walk of every range goes through an
  internal `Step` of std.core (Rust's), one block per integer type and `char`: a block of its own
  for `Range<char>` beside the one for `Integer` would conform it twice (no specialization, 04).
  Spec 03 §5.2 rule 4, §7, 12 §Iteration; four cases.
- **S3b** a bare `T[N]` compares and hashes where `T` does (10 C8, S4b): `d == sha256(x)`, and a
  `uint8[16]` is a key. The resolver writes, for every length the program writes anywhere, a
  block of `Equatable` and one of `Hashable` on `T[N]` into std.core — synthesized as source, as
  the conformances of 04 D7 are; the only blocks on an inline array, a written one is still
  refused. A block over a generic length cannot be written, one over a written length can.
  **S3 is done.** Spec 03 §5.3 rule 7; three cases.
- **S4** lambdas that throw (P2, 05 K4): every adapter, terminator and higher function of std
  that takes a function takes one that may throw — `E :: [Error]` bound at the call, an
  adapter's `Error` the join with its source's, a terminator throwing the join; `never` where
  the function throws nothing, and then nothing changes. `map`, `filter`, `takeWhile`,
  `skipWhile`, `inspect`; `fold`, `reduce`, `any`, `all`, `none`, `find`, `position`,
  `forEach`; `arrayOf`, `partitionPoint`, `sortBy`, `sortByKey`; `List.removeWhere`,
  `Map.getOrInsert`, `update`, `retain`. What a throw leaves: a sort every element (its runs
  sorted by swaps, its buffer put back by a `defer`), `removeWhere` what it decided and the rest,
  a map a map. Spec 12 §Iteration, §Arrays and views, §Collections; four cases.
- **S5** the rest of I4: the adapters `mapNotNull`, `flatMap` and `flatten` (over any
  `Iterable`), `dedup`/`dedupBy`, `scan` (the start first, as Kotlin's), `peekable()` (a
  `PeekableIter`, the names ending in `Iter`), `rev()` (no `DoubleEnded` itself: its source's
  back throws nothing, its front may), `cycle()` (of a `Clone` iterator — an array's walk, a
  range, a string's characters); the terminators `minBy`/`maxBy`, `average()` of `int` or
  `float`, and in std.collections `chunks`, `windows`, `sorted`, `sortedBy`, `partition`,
  `groupBy`, `toMap`. `collect` into an array or a list starts with the room `sizeHint` gives,
  and an exact one makes no copy. `toMap` needed the language to bind a block's parameters through
  a fixation inside a type, `Iterator<Item = (K, V)>` (05 §13 rule 2) — the checker and the
  lowering bind alike now. Spec 12 §Iteration, 05 §13; four cases.
- **S6a** collections (10 C3, C4, C5, C7, N3): a set's `union`, `intersect`, `difference`,
  `symmetricDifference`, `isSubset`, `isSuperset`, `isDisjoint` (methods, no operators) and
  `Set.of`; a map's `m[k]` (`?V`) and `m[k] = v` through `IndexSet<K>` — `m[k] = null` takes `k`
  out, as Swift's —, `Map.from(pairs)` and `entries()`; `List.from(it)` and `shrinkToFit()`; a
  view plus a view, a new array. `list[a..b]` is not built: 03 N2's `Index<Range…>` would be a
  second conformance of `Index` beside `Index<int>`, which D4 refuses — a decision for the
  maintainer; until then `list.asSlice()[a..b]`. Spec 12 §Collections, 03 §5.1; four cases.
- **S6b** array literals into collections (10 K4): `FromArrayLiteral<T>` in std.core, with blocks
  for `List`, `Deque`, `Heap`, `Set` and `Map` — `let s: Set<int> = [1, 2]`, through one `?` too.
  **S6 is done.** Spec 12 §Collections; three cases.
- **S7** results and conversions (05 E1, 04): `Result<T, E :: [Error]>` — `Ok`, `Err`,
  `Result.of { … }`, `try r.get()` —, and `From<T>`/`Into<T>` with the blanket
  `extend<T, U :: [From<T>]> T :: [Into<U>]`, its coherence asked over the whole program; `as`
  converts numbers only. Spec 12 "Results and conversions", 05 §13 rule 9; eight cases.
- **S8a** the builder without `&` (10 S13, 01 S13): `showTo`, `debugTo` and `format` take the
  `StringBuilder` by its reference; `reserve` is a check, its growth out of line. Spec 12; four cases.
- **S8b** a built-in's value as an interface value (01 V7): a box per built-in type,
  `let d: Display = 5`. Spec 05; three cases.
- **S8c** `std.fmt.format(template, args...)`, the f-strings' format language read at run time; a
  spec that fits no type panics. **S8 is done.** Spec 12 "Formatting"; four cases.
- **S9a** `Iterator<Item = T>` as a value (03 M4-1): an interface value with its associated types
  fixed, taken and returned and walked by `for`. Spec 05; six cases.
- **S9b** a generic member as a witness (05 §1 rule 2): `contains<P :: [Pattern]>` answers
  `Contains<char>` at `P = char`, so `'b' in "abc"`; a string takes its witness from its view.
  **S9 is done.** Spec 05, 12; three cases.
- **S10** what a clause's binding tells of its error (05 E6 O3, the review's M5-3):
  `e.suppressed()` and `e.backtrace(): ?Backtrace`, in the clause itself and not in a lambda
  there; the record kept for the clause; the runtime's trace naming locked, since a `Backtrace` is
  shown on any thread. Spec 06 §4; five cases.
- **S11** checksums and bytes (10 Q3, Z1): `crc32`, `crc32c` and `adler32` with a running value,
  their tables made at the first call — a module binding of `std.hash` would have built them in
  every program —; `toBytesLE`/`toBytesBE` on every integer type. Spec 12; three cases.
- **S12a** the tests (10 X1, X3): `assertContains`, `assertEmpty`, the first difference of two
  long values named under the failure, `@Test { parallel = true }` — after the others, at once,
  without subtests. Spec 14 §3.
- **S12b** the benchmarks (10 X4): `@Bench fn name(&b: Bench)`, `b.iter { … }`, `b.bytes(n)`,
  `blackBox`; the runtime counts each thread's allocations; `lyric5 bench` in the release
  profile, with `--save` and `--compare`, and the std's eight benchmarks under `tests/std`.
  **S12 is done.** Spec 12 "Benchmarks", 14 §4; one case.
- **S13** four diagnostics, once each: `1.` and `.5` (`LYR-LEX0016`), a name beyond ASCII, a
  constraint only written for a value (`LYR-SEM0126` there, then no type), `lyric5 test --target`.
  Spec 01, 05, 14; four cases.
- **S14** the close: **the word counter** (`tests/Lyric5.Tests/programs/wordcount.lyr`, M8c's
  artifact) — the lines, words, characters and bytes of a file, its most frequent words split at
  White_Space and lowercased by the simple mapping, a CRC-32 —, built as a package and run by
  `ExampleTests` over a text in four scripts, checked against Python's reading of it. The dogfood:
  the std tests (394), the examples, `lyric5 bench` over the std's benchmarks. The rows S6b–S13
  above were written here, at the close.

On the way, outside the plan: a child that ended before the reaper knew of it was never reaped,
its `wait` hanging (M8b S12's; the processes test's two timeouts) — fixed in a PR of its own.

**M8c's plan is built.** Every slice of it stands; what it moved stands in its row — `list[a..b]`
for a decision, `Contains<char>` on text from S2 to S9, S8 and S12 split. The open points the run
collected went to the maintainer with the report and were decided on 2026-10-09 (design, each area's
"Review 2026-10-09"); nine of them carry the clock M8c and follow in five slices (design 13, the
addendum after M8c's S14 row). With S19 they are built: **M8c is done.** M9a follows, once the maintainer is asked.

After the review:

- **S15** `is`, type patterns and type sets on the built-in types — `bool`, `char`, the ten
  number types, `string`, `StringView` —, exact (an `int8` is no `int`, an alias is its type), and
  `Any` takes their values (`let a: Any = 5`, design 03 T10 as written). The test compares the
  descriptor of the built-in's box (S8b). Spec 05 §9 rules 1–3, 09 §2 rule 12; five cases, the
  pin `any-takes-no-scalar` retired with its rule.
- **S16** a debug text for an optional — `null` or the value's own, through the helper a
  synthesized `debug()` used already; no `Display` — and `Debug` and `Display` for `T[N]` as for
  `T[]` (`[1, 2, 3]`), synthesized per written length beside S3b's `Equatable` and `Hashable`, so
  `assertEq('7'.toDigit(10), 7)` and `{x.toBytesLE()}` compile. Spec 03 §3.4 rule 1, §5.3 rule 8;
  three cases.
- **S17** a synthesized `==`, `hash` and debug text take an inline array field whole, through
  its length's blocks — unrolled, a `uint8[4096]` field did not compile (`LYR-SEM0105`, deeper
  than the checker walks), and 64 bytes cost 3 400 lines of C —; `never` as a type argument where
  the parameter's bound is `Error` (`MapIter<…, never>`, `f<never>()`), refused for any other
  (`LYR-SEM0145`); `dedup()` names its empty set so. Spec 06 §9.3 rule 3; two cases.
- **S18** `list[a..b]` is the range of the list's view — `AsSlice<T>` in `std.core`, `List`
  conforms, the bounds and `^n` the view's —; `LYR-SEM0126` at a field's type and within a written
  type (an element of an array, a view or a tuple, a type argument: `List<Equatable>`), said once,
  the uses and initializers silent; an empty trailing block, `subtest("row") { }`, a lambda of no
  statements (a parser case: `{ }` read as an expression). Spec 03 §5.2 rule 7, §8.2 rule 3, 05 §7
  rule 3; five cases.
- **S19** the conformance gate in one job per profile, a matrix — shipped from `main` as a PR of
  its own before S15–S18, so every later gate ran split. `main` requires no check by name: no
  setting followed, the merge scripts match the prefix.
- **S20** `never` as a type parameter's default under `Error` — the maintainer's answer to the
  question after S15–S19: the nine adapters write `E :: [Error] = never`, so `MapIter<I, U>` is
  again the one whose function throws nothing, as before S4. Spec 06 §9.3 rule 3, 12 §Iteration;
  two cases.

### M9a — done (2026-10-13)

`comptime` through an IR interpreter, the attributes, `std.meta`, the synthesis after A5: the plan
is design 13's addendum "der Plan für M9a" (P1–P13, S1–S12), re-cut to XL after every point was
checked against the code — of M9a almost nothing stood. Approved like M8c on 2026-10-09: no halt
until it closes, merged on green CI, open points collected for the report at the close; before
M9b the maintainer is asked again.

- **S1** the interpreter (`src/Lyric.Frontend/Ir/Interp/`): the typed IR with explicit frames —
  calls, structs, enums, tuples, optionals, arrays, objects, closures, interface values, errors
  and `defer` —, the pure natives as C# twins of the runtime's (the number tower, the texts of
  floats and their parse, strings and views, the C library's exactly rounded five, Unicode's
  answers for ASCII), the world refused at the site (`LYR-CT0003`, with the chain of calls).
  `comptime e` is evaluated: a lowering hoists each site into a function of its own, the
  interpreter runs it, and the real lowering writes the value — `comptime fib(30)` is 832040 in
  the binary and `fib` is gone. A panic is `LYR-CT0001` with the run-time code and the frames, a
  site over its budget `LYR-CT0002` (`comptimeBudget` in `[package]`, each site's); overflow as
  the profile says. `programs/comptime_same.lyr` computes twelve cases at compile time and at run
  time, the same to the byte in both profiles. P11 corrected: two lowerings where there are sites,
  not one reused. Spec 11 §2 rules 1–5, 15 §1 rule 7; seven cases.
- **S2** a site leaves a value of any value type (P3): arrays — of scalars or strings as the C's
  static data, copied into a fresh array at every evaluation —, inline arrays, tuples, optionals,
  structs, enums, `Box`. A class's object, a view, a function value, a coroutine and an interface
  value are refused with the reason (`LYR-SEM0100`). `_` binds nothing, found by the probes. Spec
  11 §2 rule 1, 07 §4 rule 2; four cases.
- **S3** the forms and the words (P4): `comptime { … }`, a block whose last expression is the
  value, with locals, loops and lambdas of its own; `comptime if`, its condition a site and its
  `else if`s choosing the same way — every branch checked, the chosen one lowered; `comptime for`
  over an array a site computes, its body unrolled per element. `comptime`, `inline`, `quote` and
  `macro` are words in Lyric 5. Spec 11 §2 rules 6–8, 01 §3; six cases, two older ones renamed.
- **S4** determinism and `embed` (P2's rest, Q4): the Unicode tables a third time, as C# for the
  interpreter — `gen.py --check` holds all three —, so compile time answers Unicode as the program
  does; `std.core.embed(path)`, a file of the package as `uint8[]`, called by what a site reaches
  (a call the program would make, a file outside the package or none: `LYR-CT0004`), the build
  depending on what it read. Spec 11 §2 rule 9; three cases.
- **S5** the attributes (P7): on every declaration, where one of the sixteen markers of
  std.core admits them; each a value computed while the program compiles — the struct it writes,
  a `comptime` site —, which `lyric test` reads: `@Test { timeout = LIMIT }` runs with `LIMIT`. The
  parenthesized form fills the one field without a default; Lyric 5 writes no attribute rows, and
  an attributed function is no root. Found on the way: the interpreter filled every global at the
  first read of one; each now fills alone. Spec 11 §1 rules 1, 4–6; four cases.
- **S6** the compiler instructions (P8): `@callerLine` and `@callerFile` — the file as the binary's
  backtrace names it, `app/src/main.lyr` —; `@Deprecated` read from its computed value after the
  evaluation, its `replacement` a note, its `until` due against the claimed 5.0.0; `@AlwaysInline`,
  `@NoInline` and `@Cold` as C attributes, `@NoInline` the IR inliner's too; `@MustUse` (on `Result`
  and the free `spawn`); `@NonExhaustive` (on `IoErrorKind`), which a match outside the package
  covers with `_`; `@Allow(code)` with `[lints] allow`, an `@Allow` that takes back nothing warning.
  `SEM0067` falls for them. Found on the way: a call through an interface dropped its defaults,
  names and variadic rest; the verifier now counts a `callvirt`'s arguments. Spec 11 §1 rules 2,
  7–13, 09 §3 rule 5, 10 §2 rule 8, 12, 15 §1 rule 4; twenty-three cases.
- **S7** target, profile and `@When` (P9): `target` (`os`, `arch`, `pointerWidth`) and `profile` (`debug`),
  constants of the build in std.core and the prelude — a cross-build's target, which `--emit` now
  honours too —; a program that reads neither carries neither (a `comptime` initializer is
  prunable; hello's C byte for byte). `@When(cond)` on top-level declarations and on the header
  `module;`, which names nothing (a reading: 07 M2 left no place for a module's attributes):
  the `@When` sites first, then the rest but what they leave out, checked and not built; an
  active use is `LYR-CT0006`, one in the branch a `comptime if` does not take is none. `lyric
  test` leaves such tests out. `std.path` asks the target, not the runtime. Spec 11 §3, §1
  rules 4 and 7, 04 §2 rule 2, 01 rule 4, 12; eight cases.
- **S8** `inline fn`, local `fn` and `@Shared` (P10): `inline fn` sets its body in place at every
  call, and the lambda literals at its function parameters with it — Kotlin's semantics: the
  arguments once, a lambda literal alone set in place, its `return` leaving the function that
  wrote it through the body's defers, its `break` and `continue` reaching that function's loops;
  `forEach` is inline, with no closure and no indirect call. A local `fn` is a named closure that
  calls itself, a value of its type. What cannot be inline is refused (`LYR-SEM0184`: an
  interface's member, a function without a body, a `mut fn`, a place parameter, a body that
  yields). A `@Shared` field is copied by `clone()`, not cloned. Spec 08 F6/F8, 05 §14; thirteen
  cases.
- **S9** `std.meta` (P5, with R7): `fields`, `variants`, `members`, `conformances`,
  `hasConformance`, `typeName`, `isStruct`/`isClass`/`isEnum`/`isInterface`, `attribute`,
  `typeAttribute` — questions without a body, answered by the lowering as constants of the type
  at the instance. `comptime for` over a known type's fields has a round per field, `f` that
  field's info and `e.[f]` the field, of its own type (the enclosing function is parsed again
  per round); a default that walks `Self`'s fields is set in place for each conformer and
  checked as the conformer's — a `ToJson` written once. A question sees what the asking module
  may name (R7). Found on the way: `comptime` sites were compared as AST records, by value.
  Spec 11 §4 rules 1–6; ten cases.
- **S12a** std computes while compiling (S12's first part): `std.hash`'s CRC tables are
  `comptime` module bindings — data in the program, copied once before its entry where a CRC is
  asked for, nothing where it is not; M8c S11's `SpinLock` class is gone. std's sites take the
  toolchain's budget, a program's `comptimeBudget` is its own code's. `programs/constants.lyr`
  for Lyric 5. Open: every build evaluates std's sites, read or not (D8, clocked at S12).
- **S10** the synthesis after A5 (P6; the review of 2026-10-12, D2–D5): `Equatable`,
  `Hashable`, `Ordered`, `TotalOrder` and `Clone` are std.core's defaults, written once over
  `fields<Self>()` and `variants<Self>()` and set in place for each conformer; on the Lyric 5
  path `Synthesis` emits the head alone (`extend<T :: [I]> P<T> :: [I] { }`, rule 4 = D3).
  `Default`, `Display`, `Debug` and `Identity` stay the compiler's (a static interface member
  declares only). The forms the family needed (D4, D5): `comptime for (v in variants<E>())`
  with `e is v`, `E.[v]`, `E.[v](…)`, `E.[v] { … }`, `comptime for (p in v.payload)` and
  `e.[v, p]` (a panic on another variant); an initializer over infos,
  `T { comptime for (f in fields<T>()) [f] = e }`; `comptime if (c) a else b` as a value; and a
  `comptime if` a round decides checks the chosen branch alone. The family asks with the
  conformer's module (D2): private fields compare, hash and clone; what is wrong in one of its
  defaults is said at the conformer's entry, the field's round and the default's line as notes
  (05 §14 rule 6); a shape without a form is `LYR-SEM0135`. std.meta's questions and structs
  live in std.core now (core imports nothing), re-exported by std.meta. `T[N]` clones element
  by element (a block per written length, as it compares); a round's info takes no slot — a
  read of `v.index` is the number, and a default that decides with the info alone leaves none
  in the C. Spec 05 §14 rules 1 and 6, 08 §2 rule 6, 11 §4 rules 7–10; fourteen cases.
- **S11** enum reflection (R5; design 03 "Review 2026-10-12"): a static interface function
  with a body is a default — set in place per conformer as an instance default is, `E.m(…)`
  and `T.m(…)` its instance, the conformer's own static in its place; `LYR-SEM0127` stays for a
  `static let` with a value. On it, std.core's `VariantName` (`name()`, any enum) and
  `Variants :: [VariantName]` (`all()`, `fromName(s)`; unit enums, `LYR-SEM0135` at the entry
  otherwise), both in the prelude — `all`, since `variants` inside the default is the question.
  The artifact: a names table of an enum with no `match`. Spec 05 §2 rule 2, 11 §4 rules 5 and
  11, 04's prelude; four cases.
- **S11b** `std.math`'s free functions (10 Z2 as the maintainer re-decided it on 2026-10-12): the
  elementary functions — `sqrt`, `cbrt`, `exp`, `ln`, `log2`, `log10`, `pow(x, e)`, the
  trigonometric and hyperbolic ones, `atan2(y, x)`, `hypot`, `floor`, `ceil`, `round`, `trunc`
  — are free functions generic over `Float`, `abs` and `signum` over `Signed`; the type keeps
  what describes it or its value. libm's externs moved with them; std.core is 163 lines
  shorter. `x.sqrt()` is no member any more (`LYR-SEM0012`). Spec 12 "Numbers" and "std.math";
  four cases, the first that compute a root or an angle at all.
- **S11c** the `std.core` audit (design 10 "Review 2026-10-13"): the adapters are `std.iter`'s,
  a module the prelude names, so its blocks reach every iterator everywhere and a struct is
  imported where its type is written; `std.core` is ordered by subject with a table of contents
  — 5275 lines from 6363, `std.iter` 942. What stays, stays for a reason (the format
  language's conformances, the tuples' written-out blocks, the scalars' natives). A build of
  hello: 1554 → 1555 ms; its C unchanged. Spec 12 "Iteration", 04 rule 5; three
  cases import from `std.iter`.
- **S12b** D1 and D8 (the review of 2026-10-12): `assert(c) { f"…" }` — `assert` is an
  `inline fn` of std.core whose message is a lambda built only on failure, the native
  `assertFailed` its panic; the old string form is a type mismatch. A module binding's
  `comptime` site is evaluated only where the program reaches the binding — the hoisting pass
  walks from the program's roots and the sites outside bindings to the globals touched, their
  initializers in turn —; an unreached binding gets no initializer, and the pruning drops its
  slot. hello builds 1530 → 1443 ms. Spec 06 §9 rule 1, 11 §2 rule 10; four cases.
- **S12** the close: `programs/constants.lyr` (S12a) and the `comptime` tables (S12a) stand;
  the dogfood — the std tests (394), the programs `ExampleTests` and `CEmitterTests` run, the
  std's eight benchmarks under `lyric5 bench` —; measurement point 1: hello 1 362 896 bytes,
  841 lines of C, starts in 1.28 ms against plain C's 0.94. (The 4.x `examples/` stay 4.x's
  until M17 brings the texts over.) The rows S8–S12b above were written at S10 and after.

**M9a's plan is built.** Every slice of P1–P13 stands, three slices more than the plan had
(S11b `std.math` free, S11c the `std.core` audit, S12b D1 and D8 — the maintainer's decisions
of 2026-10-12), and `Default` stays the compiler's for a reason of its own (no form names a
field's type). The readings built on the way, for the maintainer's veto: a static interface
function with a body is a default (03 "Review 2026-10-12"); `Variants.all()`, not
`variants()`; an integer's `pow(n)` stays a method; `std.iter` as a module the prelude imports
without a name; an inline body carries the call's line. The open points of S10–S12b went into
the report. **M9a is done.**

### M9b — done (2026-10-13)

Macros over `std.syntax`: the plan is design 13's addendum "der Plan für M9b" (P1–P10, S1–S6),
approved by the maintainer on 2026-10-13 after the seven readings of M9a's report were confirmed.
The mode of M8c and M9a: no halt until it closes, merged on green CI, open points collected for
the report at the close.

- **S1** `std.syntax` and the bridge (P1, P2): the syntax tree as a Lyric library — `Expr`,
  `Stmt`, `Decl`, `Type`, `Pattern` as `@NonExhaustive` enums with `Box` payloads, the structs of
  the declarations, `Span { file, line, column }`, `Ident`, `Literal`, `Block` —;
  `parseExpr`/`parseStmts`/`parseDecls` reading a text as the compiler does (the parser got an
  entry for statements to the end of a text, the tail rule holding there); `toSource()` writing
  a node as the formatter does, which learned to write a built tree from its values (no source
  to copy a spelling from); `children()`/`walk(f)`; `Ident.fresh(hint)`. The bridge
  (`Ir/Interp/SyntaxBridge.cs`) turns the compiler's tree into the interpreter's values and back,
  type-directed by the module's own definitions — it knows names, not layouts. Two findings on
  the way: a type position now looks past a value of the name in the scope chain
  (`enum Expr { Literal(Literal, Span) }` named its own variant, `LYR-RES0002`), and the gate
  walks a recursive enum once instead of forever. Run-time use of the module is `LYR-CG0002`;
  `Box<T>` is `Debug` where `T` is. The artifact: every program of the emitter tests goes
  tree → values → tree to the same text (`SyntaxTests`, 154 programs), and `toSource` of the
  cases equals the formatter. Spec 12 §Syntax rules 1–6; three cases; a std self-test.
- **S2** `macro` and `name!(…)` (P3, P4 for the expression and the statement form, P6, P7
  without `quote`): a `macro` is a function over syntax — parameters and result types of
  `std.syntax`, no type parameters (`LYR-SEM0186`), never called nor a value (`LYR-SEM0187`); a
  site `name!(args)`, `name!(args) { … }`, `name! { … }` (the `!` directly behind the name) hands
  its arguments over as trees, by the parameters' kinds (`LYR-SEM0191`), and takes the result in
  its place — an `Expr`, or statements where the site is a statement (`LYR-SEM0188`). The
  expansion is a phase per module in import order (`Compiler/MacroExpander.cs`): a round analyzes
  the compilation with the sites as holes, lowers the round's macros as the roots of a hoisting
  pass (the unexpanded modules as stubs), runs each site in the interpreter with the bridge's
  values, rewrites the trees (`AST/AstRewriter.cs`, generic over the records) and resolves a fresh
  compilation over them; a module's own macro is refused in it (`LYR-SEM0189`), a site inside an
  argument travels as `Expr.Macro` and expands in the round after, 32 rounds are the limit
  (`LYR-CT0007`). `error(at, msg)` is the macro's own error (`LYR-MAC0001`) at the node with a note
  at the `!`; a panic is `LYR-CT0001` at the site. The artifacts: `twice!(x)` as an `Expr` result,
  a macro from an imported module (and one whose module uses a macro itself), the own module's
  call refused, `error(node, …)` with both spans (`MacroTests`). Spec 11 §5 rules 1–5; five cases.
- **S3** `quote { … }` and `#{…}` with hygiene (P5): a quote is a tree as a value of its content's
  kind — `Expr` for a lone tail, `Stmt[]`/`Block` for statements, `Decl`/`Decl[]` for
  declarations, the expected type deciding where the content allows two (`LYR-SEM0194` without
  `std.syntax`); `#{e}` splices the value of the function's code by its position — an
  expression, a statement of its own (arrays and a block's statements spliced in, a splice
  statement ending at its `}`), a declaration's name (`fn #{name}()`, the content holding `#i`) —
  checked against the position (`LYR-SEM0193`). The lowering numbers the quotes
  (`IrModule.Quotes`) and calls a native of its own per quote, `std.syntax.quote#i`, with the
  splices' values; the bridge builds the tree and, per evaluation, renames every name the content
  binds (`Ident.fresh`) and marks every free name as the macro module's (`Ident.from`,
  `Named`'s module), which the checker and the resolver answer in that module's scope. The
  artifacts: `retry!(3) { … }` from a quote (`QuoteTests`), the caller's `attempts` untouched
  beside the quote's, `#{stmts}` spliced, `fn #{name}()`, `helper()` of the macro's module
  though the site never imports it. The formatter writes `quote`, `#{…}` and `macro`. Spec 11
  §5 rules 6–8; three cases.
- **S4** attribute macros (P4 Art 3, F2, T4, T5): a `macro` whose first parameter is a
  declaration of `std.syntax` — `StructDecl` and `ClassDecl` are two structs now, beside `FnDecl`,
  `EnumDecl`, `InterfaceDecl`, `ExtendDecl`, `LetDecl` and `Decl` for any — sits on a top-level
  declaration as an attribute, no `!` (T4); its result is a `Decl` or a `Decl[]` (`LYR-SEM0186`).
  The expander collects such attributes as sites beside the `!` ones: the declaration reaches
  the macro without this attribute and with the rest (T5), the attribute's positional argument
  fills the parameters after it, fields are refused as a data attribute's and a declaration of
  another kind than the parameter's (`LYR-SEM0191`); the result stands in the declaration's
  place, a `Decl[]` spliced into the module (`AstRewriter` splices declarations now). The
  checker skips an attribute that names a macro in the pass with the sites in. `#{t}` in type
  position splices a `Type`, which a declaration macro needs. The artifacts: `@Builder struct P`
  adds `PBuilder` (built with the constructors), `@Retry(3) fn fetch` wraps the body through a
  quote, `@Logged` prepends a statement of the macro module's (`AttributeMacroTests`); T5 is the
  pin that the other declarations stay. Spec 11 §5 rules 9–10, 12 names the two structs; three
  cases.
- **S5** the diagnostics of expanded code and `lyric expand` (P7, P8): what a macro gives back
  stands in a file of its own — the expander writes the result as source (the formatter), reads
  it again (the parser) and zips the two trees (`MacroExpander.Resynthesize`/`Zip`): a node the
  caller wrote keeps its place, since the bridge now builds a node where its value's span says
  (`SyntaxBridge.Within`), and a node of the macro's stands in the synthesized file, whose origin
  is the site. The engine's re-pointing of synthesized files does the rest, extended to chain
  (a site an expansion holds) and to carry an origin's notes: an error in expanded code is said
  at the site, `in the expansion of 'name!': …`, with `synthesized as:` the line and `from the
  quote of 'name' here` at the macro's module; a warning in the macro's code falls, one in the
  caller's block stays; the same diagnostic at one place is said once (an argument spliced
  twice). `lyric expand [<file>]` prints the entry module after the expansion in the formatter's
  form (`SourceCompiler.Expand`, `Pipeline.Expand`); the debug print went. The parser reads a
  value block as an expression where an expansion writes one (`ParseExpressionOrBlock`). The
  artifacts: `bad!(1)` with the three places (`ExpansionDiagnosticTests`), `lyric expand` over a
  program with two sites (`ExpandCommandTests`). Spec 11 §5 rule 11, 14 §1.1 rule 6; three cases.
- **S6** the close: `examples/macros` is a Lyric 5 package — `retry!` from a quote, `@Builder`
  from the constructors, `formula!("2x^2 + 3x + 1", x)` as a language of its own read with
  `parseExpr` —, built, run and expanded by `ExampleTests`; the 4.x `comptime.lyr` went (S12a's
  `constants.lyr` is its heir), and a package under `examples/` is not the 4.x corpus. Point 104:
  the std self-tests write under `fs.tempDir()` and remove it — a run from a foreign directory
  leaves nothing there (400/400). The doors: `Default` stays with the compiler (10 Review
  2026-10-13 A3, confirmed); `comptime for` over tuples stays a door — a macro could write the
  tuple blocks per size, but std.core cannot carry a site of its own macro (P6), and a macro
  module in std would cost every program a round. Measurement point 1: hello 791 936 bytes
  (debug profile), 841 lines of C, start 1.48 ms against 1.03 ms plain C (minimum of 20); a
  build with one site 2.2 s against 1.57 s without (point 117: a round is an analysis more).
  CHANGELOG M9b; the report with the open points 105–146.

M9b closed on 2026-10-13 with S6. The open points of S1–S6 (105–146) went into the report;
the maintainer is asked before M10.

### M10 — open (2026-10-14)

The standard library after rule D: the plan is design 13's addendum "der Plan für M10" (P1–P13,
S1–S13) — `std.codec` with the `Codable` synthesis, `std.json`, `std.toml`, `std.uri`, `std.regex`,
`std.http` client and server, `std.compress`, `std.term`, the time zones and UTS #35 patterns, the
libm port for compile time, the Windows child pipes over the completion port, measurement point 4.
Released by the maintainer on 2026-10-14, every reading of the M9b report confirmed; the mode of
M9b: no halt until it closes, merged on green CI, open points collected for the report at the close.

- **S1** `std.codec` (P1): the pair `Encode`/`Decode`/`Codable`; the ten-form container model
  a format implements as primitives (the scalars, begin/end around the containers; the decoder
  pull-based with `isNull`/`seqHasNext`/`structField`/`beginVariant`, a mismatch leaving the
  value where it was); the layer a conformance writes with on the format itself (`field`,
  `optional`, `element`, `entry`, `next`, `nextKey`, `value`, the variant forms); the errors with
  kind, path and detail; the conformances of the integers (the narrow ones through `int`,
  `Invalid` where they do not fit), the floats, `bool`, `string`, `char`, `Instant`, `?T`, `T[]`,
  `Slice<T>`, `List`, `Deque`, `Set`, `Map`, the tuples of two and three; `Bytes { value }` as the
  byte form; `Value`, the tree of the forms with its encoder and decoder, itself `Codable`. Three
  readings against the plan's letter: the format passed by reference (`&e`, as a hasher — a `mut`
  method cannot be called on a parameter), the layer on the format rather than sub-encoder
  objects (which would copy the format), `uint8[]` a sequence and `Bytes` the byte form (a
  conformance over `T[]` cannot ask what `T` is). The compiler lets a `mut` method's `this` of a
  type parameter's type be a place (03 §2.3a said so); `Box<T>` is `Equatable` where `T` is.
  Found: a static conformance call on a generic type inside a generic extend reaches the lowering
  unsubstituted (worked around), and `>>` closing two type arguments of a call parses as an
  operator. The artifacts: a struct's fields through the Value encoder in order (`tests/std/tests/
  codec.lyr`, 7 tests), `missing: field 'name' (at User)`. Spec 12 §Codecs rules 1–6; three cases.
- **S2** the `Codable` synthesis and `@Codec` (P2; 10 E3, E4): `Encode.encode` and `Decode.decode`
  are defaults written over `std.meta` — a struct's fields in declaration order under their wire
  names, a field the data lacks as `decodeAbsent` gives it (`null` for an optional, `Missing`
  else; `?T` overrides the static default), an enum's variant as its name with the payload (one
  field the value, several a sequence) or, under `@Codec { tag }`, as a struct of the tag and the
  payload's fields. `@Codec :: [OnField, OnType] { name, skip, tag, rename: Case, denyUnknown }`
  with `Case { Same, CamelCase, SnakeCase }`; `wireName`, `snakeCase`, `camelCase`; the layer
  gained `element`, `payload`, `fieldOrAbsent`. The compiler: a pending initializer round (a
  default checked once over `Self`) reports no open type argument — the instance's round binds
  it from the field's type (`[f] = try d.field(name)` infers); a round decides `f.type.name`,
  `v.payload.length()` and `startsWith` over such a string; `Encode`/`Decode` joined the family
  that asks with the conformer's module (11 §4 rule 10), so private fields serialize. Readings:
  `default = true` is not read (a `Default` fallback needs the field's type in a constraint
  position — door); `attribute<A>(f)` is read in a default's rounds only, not in a generic
  helper over `T` (`LYR-SEM0185`), so the name list is built inside the defaults. The artifacts:
  `struct User :: [Codable]` without a line, `@Codec { name = "mail" }`, `@Codec { tag = "kind" }`,
  an own pair kept (`tests/std/tests/codec_synth.lyr`, 4 tests). Spec 12 §Codecs rules 7–8,
  11 §4 rule 10; three cases.

## Design decisions

All of them are in [`design/v5/spec/`](design/v5/spec/) — one document per area, each with the
decision, the reasoning, the comparison with other languages and what was rejected. Nothing
here restates them. Changing a decision reopens its document (CONTRIBUTING, rule 2).

### Working mode

Claude plans **and** implements; the maintainer reviews (in force since the scope check of
2026-08-02, confirmed for Lyric 5). Every slice is a PR of its own — the spec PR merged first —,
merged once its CI is green; the milestones follow each other without a pause until 5.0 stands,
and the last push toward 5.0 waits for the maintainer's review of the points collected on the
way (those of M4 to M8a were reviewed on 2026-10-05, those of the run that closed M8a on
2026-10-07, M8b's on 2026-10-08, M8c's on 2026-10-09). Claude also merges, tags and releases. Anything that acts outside the repository — creating
repositories, publishing releases, deleting published things — is laid out first and done on the
maintainer's word.

### Environment

WSL2 (Arch) for runtime and backend work, the repository in the WSL file system; Windows is an
equal CI target. `zig cc` (pinned in `tooling/zig-version`) for release and cross builds, clang
for the sanitizer profile, .NET 10 for the compiler. Details in CONTRIBUTING.
