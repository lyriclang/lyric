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
  The Lyric 5 suite runs against this tree in CI since M3 S9 (*Conformance gate*, both
  profiles), at the commit [`spec.pin`](spec.pin) names: a compiler change and its rule are two
  pull requests checked together, the specification merges first, and main is green in between.

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
| M8b | std I/O and system | L | **in progress**: planned 2026-10-07 (S1–S14) |
| M8c | std rest: Unicode, the remaining adapters, `Result`, `std.fmt`, `@Bench` | L | — |
| M9a | `comptime` (the IR interpreter) | L | — |
| M9b | Macros | L | — |
| M10 | std after rule D | XL | — |
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

### M8b — in progress

The plan is design 13's addendum of 2026-10-07: the boundary to C (P1), waiting through the
poller and an I/O pool (P2), the console (P3), handles (P4), the platforms (P5), the spec's
chapter 12 (P6), and the slices S1–S14 with their artefacts. Approved like M8a: no halt until the
close, each slice merged on green CI, the open points collected for the report at the close.

Merged, slice by slice (each PR says what it did and how it was checked):

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
2026-10-07). Claude also merges, tags and releases. Anything that acts outside the repository — creating
repositories, publishing releases, deleting published things — is laid out first and done on the
maintainer's word.

### Environment

WSL2 (Arch) for runtime and backend work, the repository in the WSL file system; Windows is an
equal CI target. `zig cc` (pinned in `tooling/zig-version`) for release and cross builds, clang
for the sanitizer profile, .NET 10 for the compiler. Details in CONTRIBUTING.
