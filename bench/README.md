# bench/ — the measurement points

The plan (design/v5/spec/13, *Messpunkte*) fixes performance as **ratchets, not claims**: a
number measured on one machine against the same program in C, Go and C#, with a bound the
tree must keep. Every program here prints one checksum; the twins in the other languages
print the same one, or the measurement is void.

    python3 bench/run.py [--runs 5] [--lyric5 <exe>] [--ratchet]

`run.py` builds every twin, checks the checksums agree, times each as the minimum of several
whole-process runs, and prints the table below. `--ratchet` fails when Lyric exceeds the
bounds. C is built by the same compiler (`zig cc`) with the flags of Lyric's release profile —
`-O2`, no contraction of `a * b + c` into a fused multiply-add (01 L10) — Go by `go build`, C#
by `dotnet build -c Release` and run through the host, its JIT start in the number as it is
for a user. A twin whose toolchain is missing is left out.

The bound against C (3×) is also a test: `MeasurementTests` in `tests/Lyric5.Tests` builds the
three programs and their C twins and fails when one exceeds it, on Linux, where the plan's
numbers come from.

## Measurement point 2 (after M3): loops, arithmetic, struct arrays

| Program | What it measures |
|---|---|
| `loops` | 200 million passes of a multiply, an add and a remainder on `int`, every one overflow-checked (03 T2), in two counted loops |
| `arith` | floating point: the points of a 2000 × 2000 grid inside the Mandelbrot set for 200 iterations |
| `structs` | 100 000 particles as an array of structs — the elements inline, four floats each — stepped 2000 times in place through the index |

Bounds: **≤ 3× C, ≤ 1.5× Go**.

Measured 2026-10-01 (WSL2 x86-64, Lyric release profile through `zig cc 0.16`, Go 1.27, .NET 10;
minimum of 5 runs, wall time of the whole process):

| bench | Lyric | C | Go | C# | Lyric / C | Lyric / Go |
|---|---|---|---|---|---|---|
| loops | 0.676 s | 0.699 s | 0.695 s | 0.695 s | 0.97 | 0.97 |
| arith | 0.331 s | 0.290 s | 0.338 s | 0.330 s | 1.14 | 0.98 |
| structs | 0.162 s | 0.160 s | 0.252 s | 0.218 s | 1.02 | 0.64 |

The checked integer arithmetic costs nothing measurable against C's unchecked one: the checks
compile to a flag test the branch predictor never misses. The struct array is laid out like
C's (02 M1), and the index checks (03 T13) sit inside the loop's noise. The 14 % on `arith`
is the loop's shape after the IR (blocks and `goto`, 01 C5), not the arithmetic; it is within
the bound and stays a number to beat.

## Measurement point 3 (after M8a): a Map, a sort, string work

| Program | What it measures |
|---|---|
| `maps` | `Map<int, int>` under std.core's `DefaultHasher` (SipHash-1-3, 10 K2): a million insertions of keys from a xorshift stream, then two million lookups, most of them misses |
| `sorting` | a million `int`s sorted by std.core's stable `sort()` (10 C2), then a checksum over the order |
| `strings` | 300 000 lines built with a `StringBuilder` through f-strings, walked by `lines()`, each cut by `splitOnce`, its number read by `int.parse`, its name searched with `contains` |

Bound: **≤ 1.5× Go**. The twins are Go and C#; C has no map, sort or string library to compare
with, so the C column stays empty.

Measured 2026-10-03, first (WSL2 x86-64, the release profile through `zig cc 0.16`, Go 1.27,
.NET 10; minimum of 3 runs):

| bench | Lyric | Go | C# | Lyric / Go |
|---|---|---|---|---|
| maps | 0.350 s | 0.222 s | 0.307 s | 1.58 |
| sorting | 0.132 s | 0.078 s | 0.170 s | 1.68 |
| strings | 0.084 s | 0.031 s | 0.101 s | 2.69 |

Lyric is ahead of C# in all three and over the bound in all three. Where the time goes (`perf`):
`strings` spends half of it growing the builder through `arrayOf` — a closure per byte — and in
the collector behind it; `sorting` 40 % in the comparison's function value and the calls behind
it, which C cannot inline through; `maps` half in `find`, whose three arrays (controls, keys,
values) cost three cache misses where Go's groups cost one.

After M8a S10c — the builder grows and copies through `memcpy`, `sort()` compares through
`totalCompare` without a function value, `find` compares the key unwrapped (minimum of 5 runs):

| bench | Lyric | Go | C# | Lyric / Go |
|---|---|---|---|---|
| maps | 0.340 s | 0.179 s | 0.218 s | 1.90 |
| sorting | 0.128 s | 0.084 s | 0.162 s | 1.53 |
| strings | 0.070 s | 0.028 s | 0.072 s | 2.53 |

In CI the job *Benchmarks* runs the same table on a shared runner and writes it into the run's
summary. Its timings are too noisy for the ratchet, so the job fails only beyond a wide fence
(`--bound 3.0`: three times Go); the ratchet of 1.5 stays a measurement by hand at each milestone.
`strings` has a fence of its own, five times, until f-strings write into one builder: on the
runner it stands at 3.6 (0.136 s against Go's 0.038 s), the other five at 1.64 or less.

Still over the bound, and the runs vary by a fifth on this machine (Go's `maps` took 0.222 s in
the first measurement); a second one, 7 runs each, gave 1.79, 1.63 and 2.27. What bounds the rest is the shape of the build, not the library alone:
a generic instance is emitted into the program's translation unit and calls std.core's small
non-generic functions — `int.totalCompare`, `isLess`, the hasher's steps — in std.core's, so C
inlines none of them without link-time optimization (ThinLTO is off: it breaks the cross-link
to Windows). `strings` spends 40 % in the collector's allocation: every f-string piece is a
string of its own until f-strings write into one builder (10 S6, `showTo`).

After the review's R9a and R9b (2026-10-06; the machine noisy, minima over alternating rounds):
the IR optimizer runs in release and inlines those calls inside the program's unit — `sorting`
1.66–1.69× → 1.19–1.35× Go, under the bound; `maps` and `strings` stay over it. ThinLTO links to
Windows now (the flag at compile time only, 01 B11); on top of the optimizer its gain measured
within the noise (`sorting` +5 %, `maps` −5 %, `strings` none) and its cost 27–90 % more build
time per program, so the release profile goes without it — `lto = true` is a manifest's choice.

After R9d (01 B14, 2026-10-06): a map's keys and values lie side by side in one array of slots
beside the control words, and an empty table is filled by repetition, not by `arrayOf`'s joined
blocks — `maps` 0.333 → 0.267 s, 1.79× → 1.43× Go (minima of three alternating rounds of seven
runs; cachegrind's LL misses 10.4 M → 3.3 M). A lookup that misses — most of them here — reads
only the control words, small enough to stay in the cache; a hit reads one slot. What remains is
the hasher: SipHash-1-3 (10 K2) is a fifth of the time, Go's AES hash 3–4 %.
