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
