# Third-party C in the runtime

Vendored so that the runtime builds from source for every target with nothing but a C compiler
(design/v5/spec/01 L11: no dependency beyond libc; 11 T3: the runtime is built per triple into the
cache). Each directory is a trimmed copy of an upstream release: the sources the build compiles,
the headers they include, and the upstream license text. Build systems, tests, tools and
documentation are left out.

| Directory | Upstream | Version | Archive SHA-256 | License | Why | Until |
|---|---|---|---|---|---|---|
| `third_party/bdwgc/` | [ivmai/bdwgc](https://github.com/ivmai/bdwgc) (Boehm–Demers–Weiser GC) | 8.2.12 (release `gc-8.2.12.tar.gz`) | `42e5194ad06ab6ffb806c83eb99c03462b495d979cda782f3c72c08af833cd4e` | MIT-style, in `README.QUICK` and `README.md` | GC stage 1 behind the allocation API (01 L1) | M11 (own GC) |
| `third_party/libbacktrace/` | [ianlancetaylor/libbacktrace](https://github.com/ianlancetaylor/libbacktrace) | commit `0b9b49cf4a2c` (2026-09-03; no releases upstream) | `a906e760862af4231fd4e98a0456ab85da610d3af0f1568a8002484b42c21d32` (GitHub tarball) | BSD-3-Clause, `LICENSE` | symbolized backtraces for panics and crashes on Linux (ELF) and macOS (Mach-O, from a `.dSYM`); Windows names its frames through DbgHelp, because zig cc writes PDB there (01 E8) | — |

**Trimmed**: bdwgc keeps `include/`, the top-level `*.c` and `extra/*.c` (the build compiles the
amalgamation `extra/gc.c`); `cord/`, `tests/`, `tools/`, `doc/`, the C++ and assembler sources and
every build system are gone. libbacktrace keeps its `*.c`/`*.h` minus the test programs; the
`config.h` and `backtrace-supported.h` that its configure script would generate are written by
hand in `runtime/src/backtrace_config/`, one file each with a block per platform (M1 S4). Which
files are compiled for which target is `RuntimeLayout.LibbacktraceSources`; the unwinder is
zig's libunwind on Linux (`-lunwind`), libSystem's on macOS.

Updating a copy: replace the directory from the new upstream archive with the same trimming,
update this table (version, hash), and run the runtime tests on every Tier 1 target.
