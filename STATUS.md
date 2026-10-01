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
  profiles); spec-first means the job is red between a spec merge and its compiler merge.

## Milestones

| M | Name | Size | State |
|---|---|---|---|
| M0 | Preparation: repos, archive, CI with `zig cc` and NativeAOT, `dev` channel | M | **done** 2026-09-29 |
| M1 | Runtime core in C (Boehm GC behind the allocation API) | M | **done** 2026-09-30 |
| M2 | First native program (IR → C → `zig cc`) | L | **done** 2026-09-30 |
| M3 | Value model and type system | XL | **done** 2026-10-01 |
| M4 | Interfaces and abstraction | L | **next** |
| M5 | Errors | M | — |
| M6 | Coroutines, scheduler, threads | XL | — |
| M7 | Modules and packages | L | — |
| M8a | std core | XL | — |
| M8b | std I/O and system | L | — |
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
   ends with M8a, when `std` is rewritten.
2. S1: `lyric5 build <file> --emit ir` — the 4.x front end behind the subset gate
   (`Lyric5.Compiler`, `LYR-CG0001` "not yet in Lyric 5: … (M<n>)"), compiling against
   `stdlib5/`, the seed of the Lyric 5 standard library (`std.core`, `std.string`, `std.io`, as
   natively backed declarations — the provisional intrinsic table, which falls with M8a). The
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

### M4 — Interfaces and abstraction

Next. Plan first (13, M4), then slices.

## Design decisions

All of them are in [`design/v5/spec/`](design/v5/spec/) — one document per area, each with the
decision, the reasoning, the comparison with other languages and what was rejected. Nothing
here restates them. Changing a decision reopens its document (CONTRIBUTING, rule 2).

### Working mode

Claude plans **and** implements; the maintainer reviews (in force since the scope check of
2026-08-02, confirmed for Lyric 5). A milestone runs autonomously slice by slice with commit and
push per slice and one PR at the end; Claude also merges, tags and releases. Anything that acts
outside the repository — creating repositories, publishing releases, deleting published things —
is laid out first and done on the maintainer's word.

### Environment

WSL2 (Arch) for runtime and backend work, the repository in the WSL file system; Windows is an
equal CI target. `zig cc` (pinned in `tooling/zig-version`) for release and cross builds, clang
for the sanitizer profile, .NET 10 for the compiler. Details in CONTRIBUTING.
