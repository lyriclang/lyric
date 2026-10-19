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

At the close of M8a (S16, 2026-10-07; minima over 4 alternating rounds of 7 runs, the machine noisy —
Go's `maps` alone moved between 0.193 and 0.222 s):

| bench | Lyric | Go | C# | Lyric / Go |
|---|---|---|---|---|
| maps | 0.262 s | 0.193 s | 0.223 s | 1.36 |
| sorting | 0.094 s | 0.078 s | 0.171 s | 1.21 |
| strings | 0.046 s | 0.027 s | 0.071 s | 1.70 |

`maps` and `sorting` are within the bound, `strings` over it. What moved them since S10c: the IR
optimizer inlining inside the program's unit (R9a), the map's slots beside its control words
(R9d), f-strings writing into one builder with `[x] * n` filling by doubling (S13 — `strings`
had become 45 % slower until the repetition stopped copying an element at a time). What remains
in `strings` is not measured apart here; the hasher's share of `maps` is as above.

The CI runner, same commit: maps 0.99×, sorting 1.09×, strings 1.80× Go. Since the review of
2026-10-07 the job runs on `main` and by hand, not on every pull request, and `strings` stands
under the common fence of 3× Go.

## M8b's I/O (reported, no bound)

| Program | What it measures |
|---|---|
| `io_copy` | a file of 128 MiB written a MiB at a time, copied by `fs.copy`, the copy read back a MiB at a time and its bytes summed — `std.fs` (10 O5), its calls parking the task on the I/O pool (O10) |
| `echo` | 256 MiB through a TCP echo on 127.0.0.1 within one program: a task writes them 64 KiB at a time, a server task echoes, the main task reads them back and sums them — `std.net` (10 O7), its sockets parking the task on the poller (O10) |

The plan asks for these as a report, not a ratchet (13, S14): `run.py --ratchet` and `--bound`
pass them by. The twin is Go — `io.Copy`, which hands a file-to-file copy to `copy_file_range`,
and its netpoller; C# has none here. CI's job *Benchmarks* reports them beside the others.

Measured 2026-10-08 (WSL2 x86-64, the release profile through `zig cc 0.16`, Go 1.27; minimum
of 3 runs; the files on WSL's `/tmp`, a tmpfs):

| bench | Lyric | Go | Lyric / Go |
|---|---|---|---|
| io_copy | 0.155 s | 0.143 s | 1.08 |
| echo | 0.138 s | 0.137 s | 1.01 |

Both at Go's level, against the guess written before the run: 1.5–3× for the copy, which goes
through two pool calls per MiB where Go hands the whole file to `copy_file_range`. Not profiled;
on a tmpfs the copy is a copy in memory either way, and what both twins do besides — write the
file, read the copy back, sum it — is in both numbers. A disk would measure the system more than
the language.

## Measurement point 4 (after M10): HTTP throughput, JSON, the log filter's regex

| Program | What it measures |
|---|---|
| `http` | HTTP/1.1 throughput in one process: `std.http`'s server answering a kibibyte of JSON, sixty-four client tasks each fetching it five hundred times over their own keep-alive connection (10 Q6) |
| `json` | six thousand records — about a mebibyte of text — encoded with `std.json` and decoded back through the `Codable` synthesis, twenty rounds (10 B10, E7) |
| `regex` | the log filter: 300 000 log lines built in memory, each asked two questions of `std.regex`'s Pike VM — `\b(ERROR|WARN)\b` and `\s[1-9]\d{3,}ms$` (10 B1) |

The plan asks for these as a report, not a ratchet (13, P12): `run.py --ratchet` and `--bound`
pass them by; the plan's hope for `http` was 2× Go. The twins are Go — `net/http`, `encoding/json`,
`regexp` —; C# has none here.

Measured 2026-10-14 (WSL2 x86-64, the release profile through `zig cc 0.16`, Go 1.27; minimum
of 3 runs):

| bench | Lyric | Go | Lyric / Go |
|---|---|---|---|
| http | 1.160 s | 0.492 s | 2.36 |
| json | 1.129 s | 0.289 s | 3.90 |
| regex | 1.274 s | 0.431 s | 2.96 |

What the first run found, and what M10's close (S13) did about it: `std.http` had left Nagle on —
a request's head, and an answer's head and body, were small separate writes, each waiting for the
peer's delayed ACK, 40 ms a request (23 s, 19× Go); `setNoDelay(true)` on both ends, as Go sets
it, gave 2.4×, and a body of known length written with its head in one call 2.36×. The Pike VM
allocated at every position (fresh slots, a new thread list a step, a slot copy at every `Save`):
10.1× Go; `isMatch` now runs without saves and ends at the first match, and the thread lists keep
their storage: 2.96× — the lazy DFA (13 P5, S9b) stays a door. JSON's time is the decode (four
fifths): the tree first (a `Map` per object) and then the layer's work per field — the member
looked up by its hashed name, the `seen` set, the reader's state rebuilt —; both are doors
(a streaming decoder, an ordered object with a linear search for small ones).

## M11: the safepoints, the prologue's check, the own collector (points 2 and 3 again)

M11 put a poll at every loop's head (S3), the prologue's check of the stack at every function's entry (S4)
and made the own collector the default (S5). Measured against the tree before them — M11 S2: Boehm the
default, no poll, no check — 2026-10-16 (WSL2 x86-64, the release profile through `zig cc 0.16`, Go 1.27;
the minimum of 7 alternating rounds; the machine busy, a load of 1.5):

| bench | before | S5 | Go | S5 / before | S5 / Go |
|---|---|---|---|---|---|
| loops | 0.642 s | 0.651 s | 0.656 s | 1.01 | 0.99 |
| arith | 0.295 s | 0.302 s | 0.299 s | 1.03 | 1.01 |
| structs | 0.148 s | 0.146 s | 0.240 s | 0.99 | 0.61 |
| maps | 0.296 s | 0.302 s | 0.210 s | 1.02 | 1.44 |
| sorting | 0.094 s | 0.100 s | 0.078 s | 1.07 | 1.28 |
| strings | 0.049 s | 0.049 s | 0.030 s | 0.99 | 1.64 |

With the flag read at every pass, `structs` lost 13 % — the load and its branch in the inner loop, not the
vectorization (the loop vectorized neither before nor after; the packed instructions that went were in cold
loops). The poll now counts down a local and reads the flag every 1024 passes of the head (13 M11 S5, the
2^k): `structs` is back. `sorting` keeps 4–7 % over four measurements: its emitted C with the poll taken out
runs as before, and the prologue's check costs nothing measurable — what remains is the countdown's decrement
and branch in a tight merge loop; strip-mining counted loops would take it (design 13, the S5 note: decided at P13's
measurement). The bounds hold: `sorting` 1.28× Go, `maps` 1.44×; `strings` stays over 1.5× as before M11.

After M11 (2026-10-18/19; four measurements, the machine noisy — C's own `loops` moved by a quarter): `strings`
1.54–1.68× Go, over the bound every time; `maps` 1.09–1.56×, around it; `sorting` 0.99–1.43×. The loop poll costs
`sorting` 4–7 % (design 13, the S5 note). A profiling sweep before M12 takes `strings` on (design 13, the S10 note).

After the sweep (Y1, 2026-10-19; the machine quiet, two rounds of 7): `strings` 1.27–1.38× Go, `maps` 1.39–1.40×,
`sorting` 1.28–1.29× — all three within the bound. A third of `strings`' instructions had been the search (a
string pattern compared at every byte; now memchr to the first byte's places), the rest the numbers written and
read; 797 million instructions to 434.

## Measurement point 5 (M11): the collector — prepared in S6

| Program | What it measures |
|---|---|
| `gc/binarytrees` | allocation throughput: complete binary trees built, walked and dropped at once, one long-lived tree beside them (the Benchmarks Game's program, on one thread) |
| `gc/cache` | a heap alive: four million entries in a `Map`, each an object with an array of 24 ints, and over it short-lived arrays and replacements — an old slot made to point at a young entry each time |
| `gc/lru` | stores into old objects: a least-recently-used list of 200 000 nodes over a `Map`, ten million lookups, most of them moving a node |

    python3 bench/gc/run.py [--runs 3] [--lyric5 <exe>] [benches...]

Each program in Lyric under its own collector, the same program under `gc = "boehm"`, and the Go twin;
the wall time (the minimum of the runs) and the peak RSS (the largest, from `wait4`). The pauses come
with `lyr_gc_stats()` (S9), and with them the plan's ratchet (13 M11 P12): better than Boehm in
throughput, pauses and RSS, p99 of the pauses under 10 ms with a gibibyte alive.

Measured 2026-10-16 with the generations of S6 (WSL2 x86-64, the release profile through `zig cc 0.16`,
Go 1.27; the minimum of 3 runs; the machine busy, a load of 2.3):

| bench | checksum | Lyric | Lyric (Boehm) | Go | Lyric / Boehm | Lyric / Go |
|---|---|---|---|---|---|---|
| binarytrees | 68332206 | 0.890 s, 122 MiB | 1.858 s, 54 MiB | 1.139 s, 36 MiB | 0.48 | 0.78 |
| cache | 4000000 460271147 | 11.780 s, 3044 MiB | 16.693 s, 1571 MiB | 6.095 s, 1810 MiB | 0.71 | 1.93 |
| lru | 200000 4938130 24392087650802 | 3.925 s, 150 MiB | 3.748 s, 41 MiB | 1.633 s, 36 MiB | 1.05 | 2.40 |

Throughput beats Boehm in `binarytrees` and `cache` and trails it by 5 % in `lru`; RSS matches it in none. `cache`
holds 1.4 GiB alive in 3.0 GiB: short-lived arrays share 256-byte lines with long-lived entries, and a
line with one live byte stays taken — whatever the pacing (design 13, the S6 note). Its
pauses reach 1.4 s for a full collection and 0.9 s for a young one, the young generation as large as the
old (the same note). `lru` stores into old nodes nearly everywhere: its young collections read almost
every line of the old heap through the cards and pause as long as a full one (about 40 ms at 64 MiB).

Measured 2026-10-16 with S9 (the young generation bounded, the marking's changes — design 13, the S9 note):
the numbers from `lyr_gc_stat` and Go's `runtime/metrics`, for `cache` its steady phase only; the machine
quiet; the minimum of 3 runs, the pauses of the fastest:

| bench | twin | time | RSS | collections | pause p99 | pause max |
|---|---|---|---|---|---|---|
| binarytrees | Lyric | 0.933 s | 60 MiB | 259 | 6.42 ms | 10.94 ms |
| binarytrees | Lyric (Boehm) | 1.849 s | 62 MiB | 121 | 9.89 ms | 11.79 ms |
| binarytrees | Go | 1.122 s | 38 MiB | 133 | 0.20 ms | 0.23 ms |
| cache | Lyric | 12.582 s | 2454 MiB | 240 | 11.01 ms | 881.17 ms |
| cache | Lyric (Boehm) | 15.839 s | 1571 MiB | 7 | 718.75 ms | 722.35 ms |
| cache | Go | 5.962 s | 1853 MiB | 1 | 12.58 ms | 12.58 ms |
| lru | Lyric | 3.592 s | 114 MiB | 32 | 14.30 ms | 15.24 ms |
| lru | Lyric (Boehm) | 3.454 s | 41 MiB | 21 | 19.39 ms | 24.34 ms |
| lru | Go | 1.501 s | 37 MiB | 15 | 1.31 ms | 1.31 ms |

    python3 bench/gc/run.py --ratchet

fails today on `cache` (RSS, the longest pause, the 99th percentile at 11 ms) and `lru` (time by 4 %, RSS):
the parallel mark (S9b) follows; RSS waits on the maintainer's decision (design 13, the S6 note).

With S9b (2026-10-19: full collections over 128 MiB alive mark on several threads — design 13, the S9b note), the
machine quiet, the minimum of 3 runs:

| bench | twin | time | RSS | collections | pause p99 | pause max |
|---|---|---|---|---|---|---|
| binarytrees | Lyric | 0.896 s | 60 MiB | 259 | 6.33 ms | 8.06 ms |
| binarytrees | Lyric (Boehm) | 1.781 s | 62 MiB | 118 | 9.33 ms | 9.50 ms |
| binarytrees | Go | 1.047 s | 38 MiB | 131 | 0.16 ms | 0.23 ms |
| cache | Lyric | 11.182 s | 2469 MiB | 240 | 9.20 ms | 462.38 ms |
| cache | Lyric (Boehm) | 14.179 s | 1571 MiB | 7 | 649.39 ms | 661.41 ms |
| cache | Go | 5.225 s | 1852 MiB | 1 | 0.26 ms | 0.26 ms |
| lru | Lyric | 3.341 s | 114 MiB | 32 | 14.16 ms | 14.21 ms |
| lru | Lyric (Boehm) | 3.021 s | 41 MiB | 21 | 15.18 ms | 17.07 ms |
| lru | Go | 1.423 s | 37 MiB | 15 | 0.79 ms | 0.79 ms |

The pauses keep the ratchet now — `cache`'s 99th percentile under 10 ms, every longest pause under Boehm's —; it
fails on RSS (`cache`, `lru`) and `lru`'s time, which wait on the maintainer's decision.
