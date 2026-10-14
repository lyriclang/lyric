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
| `third_party/musl-math/` | [musl](https://musl.libc.org/) | 1.2.5 (`musl-1.2.5.tar.gz`) | `a9a118bbe84d8764da0ea0d28b3ab3fae8477fc7e4085d90102b8596fc7c75e4` | MIT, `COPYRIGHT` | the functions of `std.math` that each system's libm rounds its own way — `exp`, `log`, `log2`, `log10`, `pow`, the trigonometric and hyperbolic ones, `cbrt`, `hypot` — under the runtime's prefix (`lyr_names.h`), so that every tier-1 target computes the same bits, and the compiler's port of the same code (`Lyric.Frontend/Ir/Interp/Libm.cs`) answers them at compile time (09 D7, 13 M10 S12 P10) | — |
| `third_party/ucd/` | [Unicode Character Database](https://www.unicode.org/Public/18.0.0/ucd/) | 18.0.0 (`UnicodeData.txt`, `PropList.txt`) | `0736451de439ae7baf1425136617da495e09ee5afbe6e394374db7009ea08950`, `f438f532e8737bb8a2702126cdf9c4af5e357c58c7acf9d9eb2fc7c1a1d955d6` (the two files) | Unicode License V3, `LICENSE` | data, not code: `tooling/unicode/gen.py` writes `runtime/src/unicode_tables.h` from it — a `char`'s category, simple case mappings, White_Space (10 B9 S4, C2) | — |

**Trimmed**: bdwgc keeps `include/`, the top-level `*.c` and `extra/*.c` (the build compiles the
amalgamation `extra/gc.c`); `cord/`, `tests/`, `tools/`, `doc/`, the C++ and assembler sources and
every build system are gone. libbacktrace keeps its `*.c`/`*.h` minus the test programs; the
`config.h` and `backtrace-supported.h` that its configure script would generate are written by
hand in `runtime/src/backtrace_config/`, one file each with a block per platform (M1 S4). Which
files are compiled for which target is `RuntimeLayout.LibbacktraceSources`; the unwinder is
zig's libunwind on Linux (`-lunwind`), libSystem's on macOS.

musl-math keeps the thirty-four units of `src/math/` the seventeen functions reach (`exp.c`,
`__rem_pio2_large.c`, the `*_data.c` tables, the `__math_*` helpers …) and their headers, each
unit with `lyr_names.h` included first — the renames, `__FP_FAST_FMA` off —, and `libm.h` cut to
what they use; they are compiled with `-ffp-contract=off -fno-fast-math`, because the bits are
the contract with the compiler's port. The port's tables are generated: `python3
tooling/libm/gen_tables.py <musl>/src/math src/Lyric.Frontend/Ir/Interp/LibmTables.cs`.

Updating a copy: replace the directory from the new upstream archive with the same trimming,
update this table (version, hash), and run the runtime tests on every Tier 1 target. The UCD is
two files of a version's `ucd/` directory as they stand, and its `license.txt` as `LICENSE`; a new
version is a new pin — run `python3 tooling/unicode/gen.py`, which writes the tables and
`std.string.unicodeVersion`, and change spec 12 §Characters with it. CI fails while the tables
differ from what the generator writes (`gen.py --check`).
