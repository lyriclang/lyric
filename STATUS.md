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
- **`main` is Lyric 5 in development**, no promise of any kind. The tree still builds the 4.x
  compiler and VM and says **4.6.0** — the Lyric 5 frontend grows out of that code (13, M2), and
  a 5.0.0 claim would fire the 4.x deprecation clocks aimed at 5.0. Beside it grows **`lyric5`**
  (`src/Lyric5`), the Lyric 5 command line as one NativeAOT binary, at **5.0.0** (`dev` builds:
  `5.0.0-dev.<date>+<sha>`).
- **Specification**: `lyriclang/lyric-spec` — branch `script` holds the 4.x text the tree is
  checked against until M17; `main` becomes the Lyric 5 skeleton.

## Milestones

| M | Name | Size | State |
|---|---|---|---|
| M0 | Preparation: repos, archive, CI with `zig cc` and NativeAOT, `dev` channel | M | **done** 2026-09-29 |
| M1 | Runtime core in C (Boehm GC behind the allocation API) | M | **next** |
| M2 | First native program (IR → C → `zig cc`) | L | — |
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

### M1 — Runtime core in C

Next. Plan first (13, M1), then slices.

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
