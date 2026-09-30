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

## Milestones

| M | Name | Size | State |
|---|---|---|---|
| M0 | Preparation: repos, archive, CI with `zig cc` and NativeAOT, `dev` channel | M | **done** 2026-09-29 |
| M1 | Runtime core in C (Boehm GC behind the allocation API) | M | **done** 2026-09-30 |
| M2 | First native program (IR → C → `zig cc`) | L | **next** |
| M3 | Value model and type system | XL | — |
| M4 | Interfaces and abstraction | L | — |
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

### M2 — First native program

In progress; the plan (13, M2) is agreed: S0 prune, S1 front end behind the subset gate,
S2 C emission, S3 strings and structs, S4 build and run, S5 measurement point 1 and spec.

1. S0: the 4.x VM, tools, bytecode writer and reader, embedding API, debug adapter, their tests,
   `stdlib-tests/`, `templates/`, `tools/Bench`, `build/publish.proj`, the 4.x release workflow
   and the CI jobs that ran the VM (compiled, release profile, conformance, spec mirror,
   publish) are gone — 58 000 lines. `docs/Grammar.md`, `docs/Bytecode.md` and
   `docs/Pack.md` stay as frozen 4.x references, because the guide links them and the site
   renders them until the website round. The tree stays at 4.6.0: the 29 CLI tests a 5.0.0
   claim turned red are gone, but the 4.x `stdlib` carries `@Deprecated(until = "5.0")`
   promises that a 5.0.0 toolchain would turn into build errors (`DeprecationPromise`); that
   ends with M8a, when `std` is rewritten.

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
