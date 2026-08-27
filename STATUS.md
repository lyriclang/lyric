# Lyric — Current State

> This file is the **only** one in the project that changes often. It is updated
> after every finished slice. Claude reads it at the start of a session to know
> where we stand.
>
> Keep the content short. Anything already committed can go —
> `git log --oneline` is the history, not this file.

---

## Current milestone

**Round 3 ships as v4.2.3** (2026-08-27), and it is not clean either. **The heavy finding is a
RESOURCE LEAK across the sandbox boundary, recorded rather than fixed**: a file handle a guest
opens and does not close stays open for the lifetime of the THREAD. Measured over five shapes —
it survives a budget stop, a guest panic, an ordinary return, and the VM and instance being
garbage collected; only an explicit `stream.close` releases it, and on Windows the file stays
locked until then. This is the ThreadStatic-table class `_sockets` (4.0) and `_children` (4.0)
already belong to; files merely made it VISIBLE, because a locked file is observable at once. It
matters because it is the sandbox's own promise: a host that stops an untrusted script is left
holding an OS handle the guest opened, with no way to release it. The fix is per-instance
resource ownership — the tables keyed by instance and released on dispose — which is the same
architecture change sitting behind the "cross-VM isolation is argued, not tested" thread. Not a
sweep's to make. **The second finding is a sentence of mine that was simply false**: 4.2.1
claimed the guard pins live in the CLI suite "because a panic ends the program the lyrtest runner
is running". `lyrtest` survives a panicking test perfectly — fresh instance per test, the panic
reported as a FAIL with its backtrace (scheduler-deep ones included), the run continuing. The
real reason is that `std.test` has no expect-a-panic assertion, so a panic can only ever be a red
test there. Corrected here and at the test's own doc comment; the pushed tag keeps the wrong
sentence. **Two named gaps CLOSED with pins rather than prose** (`StreamHostTests`): a host
granted only `fileAccess` is refused — not at `Compile`, which is why the first attempt to pin it
passed wrongly, but by the time the script runs — and an `ExecutionBudget` does reach inside a
task waiting on a file, stopping the drain with `LYR-CAP0002`. **Clean**: the `LineReader` walk is
LINEAR (25k lines 622ms, 100k lines 2373ms — 3.8x for 4x the work, so the per-refill cursor does
what it was written for). Round 4 follows.

**Round 2 of the sweep ships as v4.2.2** (2026-08-27) — and it is NOT a clean round, so the loop
does not exit here. One fix, one documented limit, two language questions recorded under §Still
open rather than answered. **The fix**: `x == null` / `x ?? y` / `x ??= y` on a non-optional
carried IR0001's default note, *"this compiler version cannot lower it yet"* — false at those
sites, because no version will lower what has nothing to lower. The note is now per-site
(*"a value of this type is never null"*) at the six optional-shaped refusals; the CODE stays one,
per the standing decision that IR0002..0010 remain free. **The check stays in the lowering, and
probing proved that right**: a generic body over `T` compiles `x == null` and `x ?? y` and both
behave correctly at `T = ?int` — only monomorphization can decide it. **What that probe ALSO
turned up is the first recorded question**: `x!` and the `null` pattern over the same `T` are
refused at declaration time (SEM0005/SEM0029), so two of the four ways to ask "is this null?"
defer to monomorphization and two do not. Not additive either way — making `??` strict would break
generic bodies that use it today. **The second question**: an f-string interpolation takes a
scalar, so `f"{instant}"` is refused while `println(instant)` works; 4.1.0 made that felt by
giving the time types a `Display`. Guide 2 and 13 now state the limit; whether the lowering learns
to call `show()` is a feature decision. **Clean in round 2**: round 1's own guard (its panic is
not catchable as an `IoError`, and a computed `max` down to one still reads), a `LineReader`
walked inside a coroutine pulled from a task — a chain in a chain with the read yielding past the
inner chain — the same walk through `map`/`filter`, one handle across two `run()` calls, fifty
queued writes, and the time types through a coroutine. Round 3 follows.

**The sweep after the std train — round 1 — ships as v4.2.1** (2026-08-27). Ten probe groups,
**one real finding**, and it was older and wider than the release that surfaced it: the
`readSome` family clamped `max` into `[1, 1MB]`, so asking for ZERO bytes handed back one. Four
entry points across three modules (`std.io.net.readSome`/`receiveFrom`, `std.process`'s pair,
`std.io.stream.readSome`) — only the last is new in 4.2; the other three have done this since
4.0. **The datagram case is the one that loses data**: `receiveFrom` cuts a payload to `max` and
drops the remainder, so a zero kept one byte and threw the packet away. Now a panic, the
convention `string.substring` and `std.bytes.slice` already follow — there is no value to answer
instead, since empty means EOF and null means a failure with a reason. Pinned in the CLI suite
rather than lyrtest — the reason given here was WRONG and round 3 corrected it: `lyrtest` survives
a panicking test fine, and the real reason is that `std.test` has no expect-a-panic assertion, so
a panic can only ever be a red test there. **Also measured
rather than argued**: the 4.2.0 scheduler fix has a SECOND symptom, and the pre-fix binary was
rebuilt to see it — with a task spinning on `Wait.Now`, an interrupt reached a parked task only
after the spinner gave up (100 000 turns against 52). Pinned. **Clean and worth not re-running**:
two tasks sharing one handle, a 200 000-byte line and a CRLF split across the 64 KB refill
boundary, 2 000 open/close rounds with no descriptor leak, a directory opened as a file, a write
to a read-only handle, `close` while a read is outstanding (the 4.0 crash class — answers `null`),
reads after EOF, `create` truncation, the time conformances at both ends of `int` through
`sortList`/`Set`/`Map`, the stdlib suite under `LYRIC_JIT=1`, and a packed executable. **What the
probes could NOT reach**: the `ExecutionBudget` contract across a stream read (needs an embedding
fixture, not a probe), and cross-VM isolation of the per-thread file table — argued from the
socket precedent, not tested, exactly as in 4.0. Round 2 follows; the loop exits on a clean one.

**v4.2.0 — the file handle — closes the two-release train** (2026-08-27). `std.io.stream`:
`open`/`create`/`openAppend`/`readSome`/`write`/`close` with their §9.0 twins, plus a
`LineReader` that is an `Iterator<string>`, so the adapters chain onto it and every step yields
through the read beneath — the first place the library itself spends what 4.0's dynamic yield
bought. Host side is the `std.process` shape, deliberately and not the `std.io.net` one: **a
regular file is not selectable on any platform** (`Socket.Select` takes sockets, POSIX `select()`
calls a file ready whatever its state), so a handle carries a notify socket and the work runs on
a pool thread. The price, named rather than discovered later: one loopback UDP socket per open
file. `write` answers the OUTCOME rather than the handover, which is where it parts from
`std.process` — a queued write cannot report a full disk at the call.

**Two findings the slice produced, and the first one nearly shipped.** (1) The handle was built
INSIDE `std.io.file` first, and that silently added `osAccess` to every program that merely calls
`text` — measured, 0x1 → 0x5 on a config-file program — because waiting means importing
`std.task` and a module requires the UNION of its imports. Hence the own module: the `std.io.path`
split of 4.0 in the other direction, a path costs no capability and moved out, a handle costs an
extra one and stays out. The lesson is that a capability requirement is a compile-time
consequence of an IMPORT, so any std module that gains a `std.task` import is making that
decision for everyone downstream. (2) **A `Wait.Now` spinner starved every other wait** —
`run` reached `block()` only with an EMPTY run queue, and `block()` is the one place a descriptor
or a deadline is looked at. A 4.0 scheduler bug, not a new one: `std.io.net` and `std.process`
were equally affected, and nothing caught it because no test mixed a spinner with a waiter for
longer than one round. Probe measured `read=0` after 200 000 spinner turns; after the fix
`read=400` in 249. Fixed failing-test-first, pinned in `task_tests` with a 5ms sleeper beside a
bounded spinner — the bound IS the assertion, since without the fix the loop never ends. 165
lyrtest (13 new), all 14 C# projects green, doc floor 533 → 554, module pages 22 → 23, guide 13
gains "A file too large to hold, and a file read beside other work".

**The std round after the major opens with v4.1.0** (2026-08-27): `Instant` and `Duration`
get the four conformances every other value in the library has had — `Equatable`, `Ordered`,
`Hashable`, `Display`. It is the first item of the two-release train the maintainer chose over
opening the HTTP question (the other is a yielding file handle): both are decided before they
are built, where HTTP still owes an answer on TLS. What the inventory actually found: the two
types carried NO conformance at all, so `a < b` was `a.epochMillis() < b.epochMillis()`,
`sortList` over a `List<Instant>` could not be written, and neither type could be printed or
key a `Map`. **`compare` is three comparisons, not a field subtraction** — the pair pinned in
the tests (`±9e18`) overflows that subtraction and answers with the opposite sign, which is the
whole reason the case is in the suite. `Duration.show()` renders `3500ms` rather than `3.5s`:
the type counts whole milliseconds and a divided rendering promises a precision it does not
carry. **What deliberately did NOT come**: `Add`/`Sub` conformances — `plus`/`minus`/`since`
are methods, an operator beside them is a second way to write one calculation, and
`Instant - Instant` is heterogeneous besides. Pure Lyric, no host, no format, no sema: +69 lines
in `stdlib/std/time.lyr`, 8 lyrtest cases (152 total), guide 13 gains "Instants compare, sort
and print", doc floor 525 → 533. One thing NOTED, not fixed: `show()` inherits `iso()`'s
pre-epoch negative-year `padStart` mangling, unreachable this side of 62 billion negative
milliseconds and already recorded.

**v4 SHIPS as v4.0.0** (2026-08-27). The whole `lyric#121` basket delivered — stackful
coroutines with the dynamic yield (items 1–3), `Deque` (6), `std.task` (4), `std.io.net` TCP
(5) and UDP (11), `std.bytes` (5a), `Instant.fromIso` (7), the stdin-pipe proof (8),
interrupts with the driver's cancel shield (9), `std.process` behind the fifth capability bit
(10), `secureRandom` (12), and the `std.io.path` move (13) — every language rule spec-first
(`lyric-spec#26`–`#28`, pin moved to 4.0 with the release), one release at the end as
decided. Release mechanics per the checklist: the tree claimed 4.0.0 since PR#124, README
already carried it, so the release commit is the CHANGELOG dating plus this paragraph — and
the tag waits for green CI on main, the 3.8.1 lesson.

**The 4.0 sweep SHIPS as v4.0.1** (2026-08-27). Round 2 came back CLEAN over six probes —
UDP truncation to `max`, a child writing 128 KB past every pipe buffer, `kill` of a child
that never ends on its own, 20 000 `fromIso` round trips across years 1–9999, 50 000
mixed-end `Deque` operations against a `List` model, exceptions crossing the resume boundary
before AND after a yield — plus a self-check of round 1's own fix: a poll holding one dead
and one live descriptor answers the dead one at once without starving the live one. A clean
round is the loop's exit condition, so the fixes ship. One edge RECORDED, not fixed: a
blocking native (`poll`'s sleep, `std.os.sleep`) is not bounded by an `ExecutionBudget`, so
a guest granted `osAccess` can hold a host's thread — a pre-existing class since 1.x, not a
4.0 regression, and gated by the capability it rides on.

**The 4.0 sweep — round 1 — is DONE** (2026-08-27, ships as 4.0.1). Eight probes aimed at what
the major added; four real findings, each fixed failing-test-first, and one hardening. **The
worst was a CRASH, not a bug**: reading a CLOSED socket inside a task killed the process with
a .NET `ArgumentNullException` stack trace — `netReadReady` answered "no bytes" for an
unknown descriptor WITHOUT recording a reason, the stale would-block sent `readSome` back to
the scheduler, and `poll` then called `Socket.Select` on lists that had resolved to nothing.
Two fixes: an unresolvable descriptor records a real failure (silent form answers `null`, the
twin names it — §9.0 restored on that path), and `poll` names a dead descriptor READY at once,
the convention an errored socket already followed. **The other three**: `LYR-SEM0072` warned
about imports used through their SECOND overload (the accounting looked only at the first
member — under `--deny-warnings` a build break for correct code, and the cheapest finding to
have shipped); one VM's `interrupt()` reached another VM's tasks (the pending flag was
process-wide while the socket table beside it is per-thread for exactly the opposite reason —
the SIGNAL stays process-wide, the programmatic raise is now per VM); and a second VM cleared
the first one's interrupt listening on every poll, turning Ctrl+C back into a process kill
while somebody waited for it (counted now, moving on transitions). Plus the hardening: the
ms→µs conversion overflowed for sleeps beyond ~292 million years into a negative "wait
forever". **What the probes could NOT reach**: the cross-VM isolation itself is argued, not
tested — the honest test needs two VMs on two threads and a timing margin, the shape that
flaked in CI before; both single-VM halves stay covered (lyrtest for the programmatic path,
the Cli SIGINT test for the signal). Probes that came back CLEAN and are worth not
re-running: the dynamic yield's byte-compare type rule (structurally identical structs, and
the same shape across two modules, both correctly refused), nested chains with an inner
resume inside an outer body, reentrant `run()` from inside a task, process use after `close`,
and a doubled `interrupt()`. 144 lyrtest on both engines; all 15 test projects green. The
paragraphs below record how the basket landed, newest first.

**v4 — stackful coroutines — was built on `feature/v4-stackful`** (2026-08-25; the basket is
`lyric#121`, the language rules are spec §10/§10a via `lyric-spec#26`, the format is §13 4.0
via `lyric-spec#27`; the branch does NOT merge until the 4.0 line ships — main stays the 3.9.x
line). **Slice 1 — the chain machinery under the OLD semantics — is BUILT.** A coroutine is a
chain of captured frames now, not a compiled state machine: three opcodes (`mkcoro` 0x79,
`resume` 0x7A, `yield` 0x7B; format 3.6 → 4.0), the body lowers as an ORDINARY void function —
locals in slots; the state object, the re-entry jump table, the lenient parameter and the
zero-field trick all RETIRED — and `resume`/`next()` are one instruction each: the VM answers
`?T` and the advanced-bool directly, and `coroutineIsDone` stays registered only for 3.x
modules. The interpreter captures/restores frame segments over a per-Loop active-resume stack,
which is also why the C-boundary rule costs nothing: a native callback runs a Loop of its own,
and a yield beneath it finds no active resume. Sema untouched — yield stays body-only until
slice 2 — which is what makes the gate meaningful: **the whole pre-4.0 suite passes unchanged
on the new machine** (4921 tests, the Vm suite green under `LYRIC_JIT=1` too; the JIT declines
chain functions, so task-shaped code runs interpreted, the #121 contract). The tree still
claims 3.9.1 while the FORMAT claims 4.0 — nothing releases from this branch, and slice 2
flips the version. A 4.0 reader accepts every 3.x module (a state machine is ordinary code); a
3.x reader rejects a 4.0 module at the first unknown opcode. **What the state machine never
could, pinned in `CoroutineChainTests`**: a chain nested in a chain, DONE once a throw crosses
the pull (the old machine left that edge undefined and nothing pinned it), defers running as
the throw unwinds the chain, the one-driver panic, a hundred suspensions reusing one capture
array. Reader rejections for `mkcoro` seeded in the negative catalogue.

**Slice 2 — the dynamic yield — is BUILT, and the tree claims 4.0.0.** `yield` is legal in
every function: the sema's SEM0038 narrowed to its surviving half (bare `yield;` in a valued
body; the outside-a-body pins retired WITH their rule, replaced by their §10a opposites — a
lambda's yields are the dynamic kind even inside a coroutine body), the lowering annotates a
dynamic yield with the expression's own type, and §10a rule 3 is a BYTE COMPARISON in the VM:
a chain op's encoded type span is recorded at decode (SlotA/SlotB into the code bytes), the
chain keeps its element type's canonical encoding from `mkcoro`, and byte equality is type
equality within a module — the runtime needs no type model for it (`LYR-VM0015`; 0013/0014
landed with slice 1, all three in Appendix A). **The five 4.0 suite cases activate and pass —
conformance 149/149** — including yield-through-a-helper AND through a lambda, the
no-resume panic, the typed mismatch, one-driver, throw-at-depth with defers. **The clocks came
due with the version claim**, because SEM0081 enforces them the moment the tree says 4.0.0:
`listDir` is REMOVED (guide 13 past tense; the native stays registered for 3.x modules; doc
floor 450 → 449, deliberately) and `LYR-SEM0093` is an ERROR (guide 12, the privilege tests
flipped to refusal, the embedding save-fixture moved onto the constructor pattern it always
should have modelled). Guide 11 gains "Yield from any depth"; CHANGELOG carries the 4.0.0
Unreleased section. Full suite green at 4.0.0 across all 14 projects, Vm under `LYRIC_JIT=1`
included.

**Slice 3 — the contracts — is BUILT, and with it the stackful item is COMPLETE.** The four
#121 answers, pinned rather than promised: the **debugger** shows the logical stack because the
logical stack IS the physical one while a chain runs — a breakpoint in a helper beneath a
resume answers `[helper, gen.<body>, main]`, pinned at the DAP level; the **backtrace** of a
panic at depth carries the same splice (with the inliner's standing caveat: a spliced callee
vanishes from every backtrace, chains included — the pin pads past the inline budget, the
ExecutionBudgetTests trick); the **budget** reaches inside a resumed chain (a resume is a call,
its chain's work is that call's work, suspension resets nothing); and the **JIT** declines
every chain-op-containing function while the program answers unchanged, `Refusals` naming it —
task-shaped code runs interpreted, which the option's own tests now state. Two stale sentences
fell: the interpreter's pool comment claimed coroutines "hold no frame across a yield" (chains
do exactly that), and guide 21's reason for the parked debug thread claimed a CLR frame stack
the machine has not had since the explicit stack landed — the honest reason is that a debug
pause is a semaphore inside the loop, not a suspension of it. The item is MERGED to main
(PR #124); main claims the major, the release waits for the basket.

**`Deque<T>` — basket item 6, pulled forward — is BUILT** (branch `feature/v4-deque`): the
scheduler's run queue has to exist before the scheduler, and a `List`'s `removeAt(0)` moves
everything. One ring, O(1) at both ends, `?T` answers with null-means-empty as the whole truth
(no throwing twin, the §9.0 silent-only case), deliberately without iteration — a queue is
drained, not walked. Written in Lyric; six lyrtest cases (ring wrap, growth while wrapped,
head wandering, clear keeps backing, the drain-refill shape); guide 13; doc floor 449 → 464.
**`std.task` — basket item 2 — is BUILT** (branch `feature/v4-task`): the scheduler in Lyric
over `Coroutine<Wait>` chains, a `Deque` run queue, parallel waiter columns, and ONE native —
`poll(read, write, timeoutMillis): [now, readyFd...]`, time and readiness in one answer;
`poll([],[],0)` IS `now`, which keeps the clock inside the single-native budget. Decisions
worth keeping: the module carries **osAccess** (the std.time precedent — sleeping is asking
the OS clock slowly; the descriptors it will watch are net's, and their SOURCES carry the
network bit), `spawn` takes the NON-throwing coroutine type (a task settles its own errors —
the scheduler has nobody to hand an exception to), and a `run` left with only
`Wait.Interrupt` tasks PANICS rather than sleeping forever, until item 9 lands the wiring.
The dynamic yield earns its keep on day one: `breathe()`/`net.readLine`-shaped helpers yield
the `Wait` with no marker on any signature — pinned in `task_tests` beside round-robin
fairness, real 5ms/40ms deadline ordering, spawn-from-a-task, and the empty run. 110 lyrtest
cases, guide 13 §Tasks, doc floor 464 → 473, 18 module pages. **`std.io.net` — basket item 3 — is BUILT** (branch `feature/v4-net`): TCP for tasks, the
`networkAccess` bit awake at last. The shape is the §10a promise kept literally: every waiting
form — `accept`, `connect`, `readSome`, `write` — yields its `Wait` INSIDE the module, so a
server is straight-line code in a task and no signature anywhere says "async". Handles are
opaque (`Listener`/`Socket`; the module's own inward casts are its privilege, the outward cast
is how a `Wait.Readable` gets its number). The natives are non-blocking answer-now calls over
a ThreadStatic fd→Socket table (parallel VMs stay apart, the io-classification precedent);
would-block is kind 6 in the last-failure contract and never surfaces — it is the signal to
wait. `readSome` folds three truths into `?uint8[]`: bytes, EMPTY-is-EOF (the 2.14
convention), null-is-failure, and `IoErrorKind` gains `ConnectionRefused`/`AddressInUse` —
the carrier-behind-kind design absorbing them without breaking a match, as it promised in
3.7. `std.task.poll`'s descriptor half is real: select over the socket table, an errored
socket reporting as readable so the read that follows tells the waiter. Three lyrtest cases
pin it end to end — an echo roundtrip ACROSS the scheduler (with the EOF assertion), the
refused connect naming its kind, the taken address naming its. 113 lyrtest, doc floor 490,
19 module pages, guide 13. **`std.bytes` — basket item 5 — is BUILT** (branch `feature/v4-bytes`), smaller than its
listing: the item named `slice`/`concat`/`indexOf`, and CONCAT turned out to already exist —
`a + b` joins arrays (the `ArrayConcat` opcode's surface since always), so a function would
have been a second name for the operator; Rule 2 says no, and the guide now says `+`. What
landed: `slice` with `string.substring`'s exact edges (negative/past-the-end start panics,
count clamps), `indexOf`/`indexOfFrom` answering `-1` like their string namesakes, pure Lyric,
capability-free. Five lyrtest cases including the three-call line reader the tooling exists
for. Doc floor 493, 20 module pages. Also this session, outside the basket: erato2's AOT
loader popped the OS "invalid image" dialog on a broken package dll (0xC000012F, three times
at the user's desk) — fixed there with an MZ probe plus a thread-local error-mode window
around `LoadLibrary` (erato2 `febdd63`; 3612 assertions green). **`Instant.fromIso` — basket item 7 — is BUILT** (branch `feature/v4-time-fromiso`): the
inverse of `iso()`'s civil-from-days (Hinnant's era arithmetic both ways), strict to exactly
the shape `iso()` writes — UTC only, no offsets, and a February 31st is REFUSED rather than
silently carried into March, because a parser that carries is a parser that lies. `null` says
whether; `fromIsoOrThrow` names the FIELD that broke (`TimeError`, the JsonError-shaped
carrier), and the silent path pays nothing for the sentence it drops — the fault walk runs
only in the twin. Round-trip pinned over epoch, leap edge 2000-03-01, pre-epoch `-1`, and the
year-1 boundary; 122 lyrtest, doc floor 497. One pre-existing edge recorded, not fixed:
`iso()` renders a NEGATIVE year through `padStart`, which mangles the sign
(`fromInt(-1).padStart(4,'0')` is `"00-1"`) — unreachable this side of 62 billion negative
milliseconds, a decision for whoever first needs BCE. The fromIso merge (#129) caught a
LATENT `std.task` bug on CI's JIT tier: with warm-up eating the first 40ms, BOTH test
deadlines were overdue at the first poll, and `wakeSleepers` woke them in list (= spawn)
order — `sleeps_wake_in_deadline_order` was only green on machines fast enough to never
see two deadlines in one poll. Fixed in the scheduler (repeated minimum extraction, ties
keep spawn order), not the test: the test name IS the promised semantics. Two new
deterministic tests force the race with negative waits (a past deadline is due at the next
poll) — the first reproduces the exact CI failure on the old code. 124 lyrtest. Also a
process slip to not repeat: #129 was merged while `gh pr checks --watch` had exited with
"no checks reported" (the watch ran before CI registered) and the `;`-chained merge went
ahead anyway — merge must be `&&`-gated on a green watch, retrying until checks exist.
**Basket item 8, `console.readAll`, turned out to be ALREADY BUILT** — it has existed since
M8b (2026-08-07, `aa40098`), native-backed, documented, registered. The basket listed it as
missing because nothing ever proved it: no test ran the console input natives against an
actual pipe. What item 8 actually delivered is that proof — `StdinTests` in the Cli suite
runs filter programs through `lyric run` with piped input and pins three facts: `readAll`
hands over the whole pipe (trailing line without a final newline included), an EMPTY pipe
reads as `""` (the documented nothing-and-empty-mean-the-same contract, unlike `readLine`,
where EOF is a state), and `lines()` walks a pipe like the filter it is meant for. Nothing in
the stdlib changed; no CHANGELOG entry, since none of it is new in 4.0.

**Basket item 9, interrupt handling, is DONE**: `Wait.Interrupt` comes alive. While a task is
parked there, Ctrl+C goes to the scheduler instead of killing the process, and the new pub
`interrupt()` raises the SAME event from inside — a "quit" command and a signal stay one
mechanism. One interrupt wakes EVERY parked task in park order (a half-woken set would mean
the second Ctrl+C kills the process while a shutdown task still believes it is covered);
raised with nobody parked it is REMEMBERED like a pending signal; the Interrupt-only-run
panic retires — waiting quietly for the interrupt is what such a run is for. The
single-native budget held: `poll` gains a fourth argument (whether anyone is parked, re-read
every call, so the Ctrl+C swallowing lasts exactly as long as somebody listens — `run()`'s
last clock reading disarms it on the way out), and the answer channel gains the impossible
descriptor `-1`. Every poll goes through the new `ask()`, which decides `-1` centrally — a
plain clock reading cannot drop an interrupt. Host-side, three wake paths for the three ways
a poll can wait: a flag, an event for the descriptorless wait, and a UDP self-pipe datagram
for a poll inside select (the one self-pipe shape `Socket.Select` accepts everywhere). Three
lyrtest cases on both engines plus a Unix-only Cli test that sends the real SIGINT end to
end (Windows skips: only the delivery differs). 127 lyrtest, doc floor 498. Embedded stays
the host's business, as decided.

The SIGINT test earned its keep before it ever passed: its first version hung BOTH Linux CI
jobs for six hours (the GitHub default cap) on a bare `ReadLine()`, and chasing the hang
uncovered a real DRIVER bug — `lyric run` is a wrapper, the program runs in a lyrvm child
that inherits the pipes, and a Ctrl+C that reaches the process group killed the DRIVER first,
tearing the pipes off a child that was mid-shutdown and stealing its exit code; a child
parked on `Wait.Interrupt`, which swallows the signal by design, was left orphaned holding
stdout open forever. Three fixes, all in #132: `Tool.Run` raises a cancel shield around the
wait (the child decides, the driver answers with the child's exit code — what every wrapper
owes its child); the test simulates a terminal honestly (`setsid` + a group-targeted kill,
because signalling only the driver tests a delivery that does not exist) with every read
BOUNDED and stderr drained concurrently; and every CI job is capped at 30 minutes, so no
future hang can spend the default six hours twice over again.

**Basket item 10, `std.process`, is DONE** (spec-first: `lyric-spec#28` added
`processAccess`, bit 4 / `0x10`, the first NEW capability bit since 1.0 — starting programs
is a new power, not an `osAccess` refinement, so a host that granted environment questions
has not thereby agreed to it; the os bit still appears BESIDE it honestly, since the module
waits through `std.task`, and the add-only rule of the bit table is now stated in §4.5/§13
plus the mirror). The module is net's doctrine aimed at children: opaque `Child`, silent
forms answer whether, `OrThrow` twins say why with the same `IoError`,
`readSomeOut`/`readSomeErr` speak `readSome`'s three truths, `wait` answers the exit code,
`kill` and `close`. The scheduler bridge is a notify descriptor per child — a UDP self-pipe
registered IN the socket table, so "output arrived" and "the child exited" are ordinary
`Wait.Readable` wakes through the one poll native; the host pumps the streams into buffers
on pool threads and posts a datagram per event, and the natives answer from the buffers and
never block. stdin is a queue drained by its own writer (a write enqueues and returns, the
pipe-full case costs memory rather than a wait, `closeStdin` is the filter's EOF). Four
lyrtest cases run real children on both engines and platforms — echo, an exit code, `sort`
as the stdin round trip (the one filter both worlds ship under the same name), NotFound
through the twin — plus two capability pins, including osAccess alone REFUSING to start a
process. 131 lyrtest, doc floor 511, 21 doc pages; guide 13 shows the sort filter as a
compiled snippet.

**Basket item 11, UDP in `std.io.net`, is DONE** — and it found two bugs beyond itself.
The API: `bind` a `UdpSocket` (port 0 asks the OS), `sendTo` one datagram (whole or not at
all, no delivery promise — that is UDP, not this module), `receiveFrom` waits and answers a
`Packet` — payload plus sender via the last-sender thread-local pair, the last-failure
convention applied to a second two-part answer — and `localPort`/`close` are the same names
TCP uses, chosen by the handle. UDP has no EOF, so an empty payload is a REAL packet (pinned
by a test), the one place the module's bytes mean something different than `readSome`'s.
Finding one, a since-3.0 compiler bug against §4.3a: a module-QUALIFIED call
(`net.localPort(x)` through an alias) bound only the first function of the name and refused
with a type mismatch while the selective import chose correctly — overload resolution
depended on the import style. Fixed in the checker (the qualified route now collects the
module's whole candidate set, public-only from outside) and pinned in `OverloadTests`; these
were the stdlib's FIRST overloads, which is why nothing had ever hit it. Finding two,
DocGen's item anchors collided for overloads (name AND kind equal) — repeats now take an
ordinal (`#fn-close`, `#fn-close-2`). Three lyrtest cases (datagram round trip answering to
the packet's sender, the empty-packet pin, AddressInUse through the twin); 134 lyrtest, doc
floor 524.

**Basket item 12, `secureRandom`, is DONE** — the smallest item, delivered as exactly that:
`pub fn secureRandom(count: int): uint8[]` in `std.random`, `count` bytes from the OS's
cryptographic source, the ONE non-deterministic draw in an otherwise reproducible module and
marked as such in its doc (the `Random` doc already said "keys need a source from the
operating system" — now the sentence ends with its name). Capability-free by design: entropy
reaches no file, clock or network, so an embedded guest that can draw good dice costs a host
nothing. Negative count panics like a bad slice bound; zero answers the empty array. Two
lyrtest cases (asked count + two 32-byte draws differing — a 2^-256 event if not, in which
case the SOURCE is broken; zero is empty). 136 lyrtest, doc floor 525.

**Basket item 13, `std.io.path`, is DONE — and with it THE v4 BASKET IS COMPLETE.** The
seven path helpers (`joinPath`, `fileName`, `parentDir`, `extension`, `stem`,
`withExtension`, `isAbsolute`) moved out of `std.io.file` into their own module, unchanged
in behavior, and the move is the point: a path is a string and touches no disk, so it now
costs NO capability — a sandboxed guest can assemble the path it hands its host without
holding `fileAccess` (pinned in `CapabilityTests`: an `std.io.path` program records bits
`0`). The old names in `io.file` are gone, not doubled — the 4.0 major pays the rename; the
only users (file_tests, Vm's `PathTests`) were rewired. Seven lyrtest cases pin every
documented edge (the `.gitignore` rule, the `/x` root parent, the drive letter); 143
lyrtest, 22 doc pages, floor stays 525 — the items only moved. Next: **the 4.0 release
round** — spec sync (README "describes Lyric 4.0", suite pin → 4.0.0), version files +
README + ratchets + status TOGETHER, ONE major release, tag after green CI, then the big
sweep per the pipeline.

**The attribute round — SHIPS as v3.9.0** (2026-08-25, format stays 3.6; spec-first,
`lyric-spec#24` merged first, this is the twin). Two rules the maintainer picked from the
design round: `@On(Event.Damage)` — ONE positional value, admitted by `std.core.WithArg<T>`
(conformance decides, the marker doctrine again: nothing becomes positional by accident, and a
struct's field order never silently becomes an argument order), filling the FIRST field under
the 2.4/2.10 value rules (`LYR-SEM0094` without the conformance, `LYR-SEM0095` at a
declaration whose first field is not `T` — own list and extend alike); and the group
`@[A, B { … }, C(v)]` behind the one token `@[` — the same list stacking declares, flattened
by the parser so no consumer downstream learns the spelling. **The row of a positional use is
byte-identical to its braces twin** (pinned), so no format change and no host change. The
formatter canonicalizes a declaration's list to the group at two or more; module-header
attributes stay stacked, because they are sequence items whose comments anchor per attribute.
Rejected in the round, recorded so nobody re-derives them: n-ary positional by field order
(field order becomes API, and a declared n-ary contract needs an interface per arity),
bare variant names in attribute values (patterns are the ONE context that resolves them —
measured: `let l: Layout = Packed;` is SEM0002 — a third resolution context would be an
inconsistency), and mixing `(…)` with `{ … }` in one use. Suite 136 → 142, six cases gated
`since: 3.9.0`.

**The sweep ran the same day and shipped as v3.9.1** (spec `lyric-spec#25`, suite → 144):
twelve probes — parent-chain admission, extend-declared WithArg (cross-module and mismatch),
alias-typed T, module-header groups, mixed spellings, recovery shapes, the optional-field
row. **Two real findings, one wart, all fixed failing-test-first.** (1) `LYR-SEM0096`: an
attribute field no row can hold — `n: ?int`, `n = 3` adapting — passed the sema and CRASHED
`lyrc build` on the writer's not-encodable throw; reachable since 3.2, the total-function
doctrine named the place, and the refusal now sits at the use. (2) The WithArg promise check
ran on direct entries only, so a parent chain (`interface Carries :: [WithArg<int>]`) admitted
the form with the mismatch surfacing per-use as SEM0001 — the check walks the entry's closure
now, own list and extend alike. (3) `@Retry(3, 4)` recovery consumed into the declaration
parser and earned a bogus PAR0042 beside the real PAR0018 — recovery goes through the
parenthesis now. Observed, no action: a comment between two stacked attributes survives fmt's
group canonicalization but surfaces inside the following body — the positional mechanism's
pre-existing placement, no loss, the invariant holds. Per the pipeline the loop exits here;
**the v4 design round (concurrency/net) is next.**

**Runde 3 — the opaque clock — SHIPS as v3.8.0** (2026-08-25, format stays 3.6). The last of
the Bestandsaufnahme's spec rounds: making an opaque value becomes the declaring module's
privilege (spec §3.5, `lyric-spec#23`). Measured first: `42 as Entity` compiled in ANY module
that saw the alias, while §3.5 and guide 12 both claimed "a script cannot forge one" — the
claim was only true of scripts that import nothing. The inward cast now warns outside the
declaring module (`LYR-SEM0093`) and 4.0 refuses it — the LYR-SEM0074 warn-then-error path —
with the constructor-function pattern named in the message, which is what keeps Erato's
save-load scripts legitimate through the transition. Outward stays free; the declaring module
changes nothing. The warning is cross-module by nature, so the suite pins the privilege half
and the toolchain's own tests pin the warning (the A9/native-roots precedent). **The M35 sweep
ran before this round and found NOTHING** — six probes (twin/silent agreement, stale-read,
remove semantics, hostile nesting, double utf8, defer unwinding); two pins landed as stdlib
tests, and per the pipeline the loop exited without a patch. With this round, all three rounds
of the 2026-08-25 Bestandsaufnahme are delivered; what remains on that plan is the v4 question
(concurrency/net) and the 4.0 clocks now ticking: `listDir`'s removal, and this warning's
promotion to an error.

**The sweep ran the same day and shipped as v3.8.2** — and the number is the lesson: the
3.8.1 release commit bumped the version files and not the README, the tag failed its own
version test on both platforms, and the release gate refused to publish. Nothing shipped
broken; no download carries 3.8.1; 3.8.2 is the same content with the README telling the
truth. **A release commit is version files + README + ratchets + status, together — and the
tag waits for green CI, not for the local suite.** The sweep itself: eleven probes against the warning's
edges — alias laundering (local transparent alias, import-as), generic laundering (`x as T`,
refused), opaque-over-opaque, every initializer position, extend bodies, cast chains,
`--deny-warnings`, the run path. **One finding, one cause, two faces**: a GLOBAL initializer
was checked with no module context — `ComputeGlobals` runs before the walk that sets it — so
the privilege never warned at module scope, and the same null let a global initializer call
extension methods of modules never imported (`let n = "ab".length();` with no import
compiled). Fixed failing-test-first by giving the globals pass its module; wave 2 over the
fix: global, static, field-default and parameter-default positions warn exactly once each,
the import idiom stays intact, 4889 green. Two pre-existing edges went to §Still open rather
than being fixed, because each needs a decision first (opaque-over-opaque's crossing, extend
on an opaque). Per the pipeline the loop exits here; **next: the attribute round** (maintainer,
2026-08-25 — one-value `@On(…)` parameters via `WithArg<T>`, grouped `@[A, B]`, spec-first,
ships as v3.9.0).

**M35 — "the reason" — SHIPPED as v3.7.0** (2026-08-25, format stays 3.6). Round 2 of the
Bestandsaufnahme, decided by the maintainer as door C, library-wide: a value answers WHETHER, a
throw answers WHY (spec §9.0, `lyric-spec#22`). Every silent form whose failure carries a
reason gains an `OrThrow` twin from the SAME implementation — `std.io.error` (IoError, kind
behind a carrier class so the list can grow without breaking a match), fifteen file twins plus
`entries` healing the last pre-2.14 lie (listDir, deprecated until 4.0), `JsonError` with
line/column via first-refusal-wins leaf recording, `EncodingError` with offsets over a
DecodeAttempt core, `Utf8Error` via the native-classification route (ThreadStatic last-failure
fields + non-pub natives, the parseFloat forward path). Deliberately silent-only: parseInt,
parseFloat, std.os, Map.get — null IS the whole truth there. **Two findings on the way**: an
import mentioned only in a PATTERN warned as unused (the 3.3.0 qualifier gap one node further,
fixed failing-test-first — every `match (e.kind)` hit it); and §3.1's context propagation does
NOT reach tuple elements (`(null, …)` refuses against `(?T, …)` — worked around with a struct,
filed under §Still open as a spec-round candidate). Sweep per pipeline comes next; then Runde 3
(the opaque-`as` clock).

**Spec round 1 and its toolchain twin — SHIPPED as v3.6.0** (2026-08-25, format stays 3.6). The
post-v3 mode is spec-first: `lyric-spec#21` rewrote §8 (inference as seven rules, probed against
the tree before being written down), refused the two silences (§5.1 list repetition, §8.3
conformance-inference ambiguity), corrected three claims about the present (§8.1 polymorphic
recursion, §5.3 interning limits that fell with 2.17/3.0, §3.3 `T[N]`), and grew the suite 121 →
135. The twin (PR #116) delivered LYR-SEM0092 (the order of a `::` list must never decide a
call — measured: it did), the widened LYR-SEM0078 (a list may not repeat itself; entries, not
closures, so parent-beside-child and cross-declaration stay legal), the #115 crash fix
(`catch (_: T)` — the form the SEM0071 note recommends), and LYR-SEM0052 for a generic function
as a value (one sentence instead of a cascade about a `T` nobody wrote). Guide 7 lost two
sentences that had outlived 3.0. Bug-sweep loop per pipeline comes next; the plan behind the
round is the v3/v4 Bestandsaufnahme of 2026-08-25 (Runde 2: the error-reason design round;
Runde 3: the opaque-`as` clock).

**The sweep ran the same day and shipped as v3.6.1**: ~25 probes against what the four changes
do to each other and make newly reachable. Two findings, both fixed failing-test-first. The one
worth remembering: `lyric fmt` turned `catch (_: Boom)` into `catch (_)` — a selective catch
into the catch-all, the formatter CHANGING MEANING — and could always have done so, because the
form parses since forever and only 3.6.0 made it compile (#115), so no corpus ever carried one.
The second was the qualified twin of SEM0052 (`m.ident` as a value went through MemberOfModule
unrefused). Wave 2 over the fixes found nothing; three pre-existing edges went to §Still open
instead of being fixed, because each needs a decision first.

---

**M32 — "die Zaehlung" — SHIPPED as v2.12.0** (2026-08-22, format 3.5 → 3.6). The delivery list
below is the record; the verdict it was built to produce is under §The gate.

**The baseline it starts from**, from `tools/Bench` (Release, two iteration counts differenced
so nothing that happens once is in the figure, instructions per iteration read from an
`ExecutionBudget` rather than counted by hand):

| case | instr/iter | ns/iter | **ns/instr** |
|---|---:|---:|---:|
| loopOnly | 9.0 | 54.8 | **6.09** |
| intAdd | 13.0 | 86.9 | **6.68** |
| floatAdd | 13.0 | 81.2 | **6.25** |
| maskOnly | 15.0 | 109.7 | **7.32** |
| arrayRead | 19.0 | 119.4 | **6.29** |

**The price is flat**: an `add f64` costs what a `br` costs. That is the normal price of a
switch-dispatch stack machine, not a slow interpreter — so the lever is the NUMBER of dispatches,
not their price. The instruction counts agree exactly with the ones Erato's `bench/interp`
reported from the outside, which is the cross-check that the two are measuring the same compiler.

Beside it, the shape that decides slice 5: `Random.nextFloat()` costs ~555 ns, and
`std.random.Random.nextInt` is **53 instructions** for a xorshift64* — six shift-and-xor rounds
written in Lyric.

The delivery list:

- [x] **slice 0** — the spec's minor-version rule says what the format actually does. It read
      "a minor may only add skippable sections", which 3.4 was not — the note beside that version
      said as much, so the document had contradicted itself since the day it shipped, at the one
      place a planner would consult before proposing an opcode. The rule now states the
      compatibility the format has actually delivered: per MODULE, not per version
      (`lyriclang/lyric-spec#7`, merged; mirror synced)
- [x] **slice 1** — the measuring bench: five interpreter cases in `tools/Bench`, Erato's names
      so the numbers are comparable to the report that started this. Two iteration counts
      differenced instead of a baseline case subtracted, and the instructions per iteration are
      READ FROM A BUDGET rather than counted from a disassembly — which is what makes the table
      survive slices 3 and 4, where the number that must fall is the one the harness reports
- [x] **slice 2** — the decode buffer, no format change (maintainer, `066bed9`): a flat
      `VmInstruction[]` instead of an array of `BytecodeInstruction` REFERENCES, and the frame's
      hot state (arrays, instruction list, stack pointer) hoisted into locals of the loop.
      **Measured, and this is the point**: the flat array alone bought NOTHING; the hoisting
      bought 12 % on `loopOnly` and 1–2 % where an instruction does more than move a value. My
      estimate for this slice was "plausibly 1.3–1.8×" and it was wrong in the optimistic
      direction. What it establishes is worth more than the 12 %: the remaining ~6 ns is **the
      dispatch itself**, not the memory around it — so nothing short of executing fewer
      instructions will move this number, which is exactly what slices 3 and 4 do
- [x] **slice 3** — fusion 1, the compare-branch (format 3.6). `brcmp` and `brcmpk` are `condbr`
      with the comparison folded in: they read slots, branch to blocks, and touch the operand
      stack not at all. Selection lives in `Emit/Fusion.cs`, NOT in the IR — a fused instruction
      is a property of the ENCODING, and teaching the verifier, the printer, the inliner and
      scalar replacement a backend shape to save the emitter a step would be the wrong trade
      twice over. **Counted exactly, by the bench reading an `ExecutionBudget`**:

      | case | before | after |
      |---|---:|---:|
      | loopOnly | 9 | **6** |
      | intAdd, floatAdd | 13 | **10** |
      | maskOnly | 15 | **12** |
      | arrayRead | 19 | **16** |

      **Timings not recorded at the time**: three consecutive runs put `floatAdd` at 74.9, 114.6
      and 140.8 ns per iteration, so what answered was the machine and not the change. They were
      taken later, on a quiet box, and stand in slice 4 below — where the comparison is against
      the same harness with the selection switched off rather than against yesterday's numbers.
      Spec: `lyric-spec#8`

- [x] **slice 3b** — the bench's own correction, found while using it: it first differenced two
      SEPARATELY minimized runs, which amplifies noise rather than removing it. It now takes the
      minimum of each run interleaved, over 15 trials. A minimum is the estimator for one-sided
      noise and the difference of two of them estimates the difference; the intermediate version,
      a median of paired differences, is worse and the comment says why — it gives every disturbed
      subtrahend a vote
- [x] **slice 4** — fusion 2, the arithmetic forms. `binll` and `binlk` take their operands from
      slots and write the result into one; they carry any binary operation, comparisons included,
      so `flag = a < b` is the same instruction with a bool destination. The destination may be a
      source, which is what makes `i = i + 1` one instruction.

      **Measured against the same harness with fusion switched off, on the same machine in the
      same session** — a separate worktree with `Fusion.Of` returning nothing, so the only
      difference is the selection:

      | case | instr | ns/iter | instr | ns/iter | |
      |---|---:|---:|---:|---:|---:|
      | | *before* | *before* | *after* | *after* | |
      | loopOnly | 9 | 41.7 | **3** | **16.2** | **2.6×** |
      | intAdd | 13 | 57.4 | **4** | **25.3** | **2.3×** |
      | floatAdd | 13 | 66.1 | **4** | **26.7** | **2.5×** |
      | maskOnly | 15 | 83.6 | **9** | **52.5** | **1.6×** |
      | arrayRead | 19 | 92.2 | **13** | **64.7** | **1.4×** |

      **The time fell with the count, not beside it** — which is the hypothesis this milestone
      rests on, now measured rather than argued: the dispatch is the bill. What did rise is the
      average price per instruction (≈4.9 → ≈6.0 ns), and that is the healthy direction: the
      instructions the fusion removed were the cheapest ones, the moves.

      The two shapes that do NOT fuse are visible in the table too: `maskOnly` keeps a nested
      operation whose inner result is a temp, and `arrayRead` an element load — neither has a
      slot to read from
- [x] **slice 5** — `std.random`. The condition was "only if the crossing measures cheaper than
      the dispatches it saves", so that was measured first: `nativeSqrt` costs 8 instructions and
      48.0 ns against `loopOnly`'s 3 and 15.4 — **a crossing into the host costs about what an
      ordinary instruction costs here**, which is a different world from the ~125 ns Erato
      measures for an engine native marshalling several arguments. Against 53 dispatches the
      decision was not close.

      The state stays in the script; one integer crosses and the next one comes back:

      | | instr/iter | ns/iter |
      |---|---:|---:|
      | `acc = acc + r.nextFloat()` before | 64 | 305–320 |
      | after | **42** | **166–176** |

      **The sequence is identical**, including the replaced zero seed — and it is now PINNED:
      the existing test compared two generators against each other, which would have watched
      xorshift64 be replaced by something else without a word. The new one asserts the values,
      and I checked that it fails when they move.

      What is left in `nextFloat` is `absInt` and a modulo, both written in Lyric. Reaching for
      the top 53 bits instead would be faster and CHANGES THE VALUES — a decision about a
      documented generator, not a performance slice.

- [ ] **found while measuring, not yet done**: the first bench table's *adjusted* columns are
      unreliable since 3.6. They subtract a baseline CASE, which assumes every case carries the
      same loop; selection broke that — the baseline's nested expression does not fuse while a
      plain accumulator does, and the subtraction now reports a NEGATIVE cost for a native call.
      The interpreter table is unaffected (it differences two iteration counts of one program).
      Rebuilding the first table on that method is its own piece of work; the harness says so at
      the top for now
- [x] **slice 6** — measured against a build with selection switched off and the old `nextInt`,
      on the same machine in the same session; CHANGELOG; released as **v2.12.0**

**The patch train before v3.0.0 has started** (maintainer's decision, 2026-08-22). Everything
that needs no major ships first and on its own, deprecating what it replaces; the removals are
the major. **v2.13.0 is the first link**, and it is the mechanism the rest writes with:
`@Deprecated { until }` plus `LYR-SEM0081`. The forms overloading replaces are the exception the
decision names — they arrive WITH v3.0.0, so they carry a promise through v3.5 instead of being
removed in the release that first offers their replacement.

**v2.14.0 is the second**: `std.io.file` reads answer `?T`, so an empty file and a missing one
stop being the same answer — the third convention was the one that lied, and the two that remain
each say what they are for. The old names carry `until = "3.0"`, which is 2.13 doing its job on
the first thing that needed it. What is still missing is a REASON: a missing file and a
permission denied look alike, and closing that means an error type and a `throws` decision — the
larger question, still open.

**v2.15.0 is the third**: a `::` list takes interfaces only — it used to SKIP a wrong entry, so a
declaration could claim a conformance nothing checked — and an interface member may carry
`@Deprecated`, with the conformance question answered at last: an implementation does NOT inherit
the clock, because it is not a use and a conforming type has no choice about it.

**v2.16.0 is the fourth**: several interface parents. The rule against them rested on a claim
about the runtime, and the claim was false — the dispatch table is keyed by (concrete type,
interface), so every ancestor keeps its own numbering and nothing is remapped. What a second
parent costs turned out to be one rule about NAMES, the second half of one that already existed.
The suite lost the case that pinned the old rule and gained three, one of them the exact shape
the old reasoning said would break.

**M33 is done and shipped as v2.17.0**: iterator chaining, the last documented No this project
carried. The answer was one design decision — a member with type parameters of its own gets NO
vtable slot, because a slot holds one function and such a member is one per instantiation — and
everything else followed from it: it must have a body, it may not be overridden, and it works
through an interface VALUE, which is what a chain needs.

**Three findings worth keeping.** (1) The documented No named two walls; the real ones were
different and one of them was a missing CALL, not missing machinery — `RequestMethod` had done
this job for `Box<int>.get` all along. (2) A non-generic method that changes the element type
makes monomorphization diverge; that is now a diagnostic instead of a hang, and it is why
`enumerate` and `chunks` stay free functions. (3) The vtable rows needed a fixed point: a row can
request a method whose body interns types that need rows of their own.

**The design round is done and decided** (see §Design decisions): multi-conformance for the
operator case in 3.0.0, free overloading as its own feature in 3.1.0, the JIT opt-in.

**The JIT is in the 3.0.0 scope, and its first step is not the JIT.** The branch stands on
`e8465d7` — the commit before v2.12.0 — and 25 commits separate it from main, four releases among
them. In those releases the interpreter learned four fused opcodes, and the emitter has never
heard of them:

| | |
|---|---:|
| `brcmp`, `brcmpk`, `binll`, `binlk` in `JitCompiler` | **0** |

After a rebase it would refuse exactly the hot functions — correctly, since refusing is its safety
mechanism, and uselessly, since almost every loop now carries one. The fused forms are EASIER for
an IL emitter than the pairs they replace (`binlk add f64 l1 = l1, 1.5` is four IL instructions
with no evaluation stack; `brcmpk lt i64 l0, k -> t, f` is a `blt`), so this is four cases, not new
machinery — but it comes first, or the next measurement measures a compiler that compiles nothing.

**Verified before any of that**: the branch as it stands passes its whole suite with `LYRIC_JIT=1`
— 4546 tests, no failures. The compiler agrees with the interpreter everywhere the tests reach,
which is the number that makes a second execution engine trustworthy.

Then: #73, the removals, the attribute roots.

## The gate: a register bytecode is NOT worth a major

The question this milestone existed to answer, answered with its own numbers.

**What a register machine would still win.** Compare what each shape costs now against what a
three-address machine would need:

| | 2.11 | **2.12** | a register machine |
|---|---:|---:|---:|
| a counting loop | 9 | **3** | 3 |
| a float accumulator | 13 | **4** | 4 |
| a masked accumulator | 15 | **9** | ~4 |
| an array read | 19 | **13** | ~5 |

The first two are already there — the fused forms ARE three-address instructions over slots, so
for the shapes a loop skeleton is made of, the two machines emit the same number. What remains is
the NESTED EXPRESSION: `acc = (acc + 1) & 1023` keeps its intermediate on the operand stack, and
neither fused form can reach a value that is not in a slot.

**And that gap does not need a major either.** It is one rule in `Emit/Fusion.cs`: today a fusion
requires its temps to be stack-placed and its operands to be named locals. Let selection put an
intermediate in a slot and chain the fused forms through it, and the nested expression becomes two
instructions — inside format 3.x, with the opcodes that already exist. Do that and the operand
stack simply stops being used by hot code, which is the register machine arrived at from the other
side and without a `.lyrbc` that older runtimes cannot read.

**So: no format 4.0 on performance grounds.** v3.0.0 keeps the language items it was cut for
(#73, overloading, the removals); the bytecode goes with it only if something else demands it.

**What the milestone also settled**, and this is the part worth keeping: the cost model. An
instruction costs ~6 ns whatever it does; a crossing into the host costs about the same as one
instruction; the price per instruction ROSE as the count fell, because what a fusion removes are
the moves. Any future optimization argument on this VM starts from those three numbers.

## What we are working on

**v3.0.0 IS RELEASED, with v3.0.1 and v3.0.2 behind it** (2026-08-23). v3.0.2 closes #101, the
Linux CI flake: the driver learned its tools' output directories by BUILDING them a second time
under its own properties — a second project instance writing one output directory — and listed
that directory before the stub publish had run. The failing log dated it: the driver finished at
07:16:19.208 and the stub landed at 07:16:19.377. Derived paths instead of a second build, the
publish invoked explicitly, and the invariant now fails the BUILD rather than a packing test half
a minute later.
 The first pass of the sweep the new
pipeline prescribes found **four defects and two stale guide claims** — every one of them in what
the new features do to each other, and none in what any single feature does alone. The one worth
remembering: two conformances satisfied by two overloads compiled to the wrong call, because the
vtable rows resolved their method by NAME. The sema had already decided it; the lowering was
asking the wrong question. Caught by the IR verifier, which is the only reason it was not silent.
 Everything that needs the major bump,
collected on one line of work; the tree claims **3.0.0** from the first breaking item on, because
the suite gates its cases by that number. Six items, all on main, both engines green and the
specification suite at 108/108. Free overloading was moved in from 3.1.0 by the maintainer, so
the major carries everything the 2.x line had deferred.

The list:

- [x] **free overloading — MOVED INTO 3.0.0** (maintainer, 2026-08-23; branch
      `feature/overloading`). The design round had it as 3.1.0; the maintainer pulled it into the
      major so that v3 carries everything deferred. Functions, methods and extensions overload;
      interface members cannot, and the reason is structural — one slot per name. The five rules
      that decide a call are in the spec (§4.3a) rather than in the implementation, which was the
      DUTY the round attached to admitting a second mechanism. What the build turned up: the
      lowering resolved declarations by NAME in four places, which with two functions of a name
      hands the second one's body to the first one's symbol — `FunctionFor(name, declaration)`
      now asks by declaration. Overloads carry their parameters in the compiled name, a name
      declared once is unchanged, and the diagnostic engine learned a MUTE so a speculative
      type-check reports nothing
- [x] **multi-conformance** (branch `feature/multi-conformance`, 2026-08-23) — door B of the
      design round, plus the part the round had not seen: the interfaces needed a SECOND type
      argument. `Add<T, R>` says what stands on the right and what comes back; without it
      `Mul<float, Vec2>` demands `fn mul(other: float): float` and `Vec2 * 2.0` cannot exist.
      A type may now name one of the four several times, the operator picks by the right
      operand, and the pieces that stopped identifying a method by NAME were: conformance
      checking (matches by signature, not by first hit), the vtable rows (per interface
      INSTANCE, so an interface value dispatches to the conformance it names), and the
      constrained call in the lowering (through that row, so the constraint decides). New
      `LYR-SEM0083` for two conformances taking one operand. Spec: §5.1, §6.1, appendix A,
      four suite cases — `lyriclang/lyric-spec`, branch of the same name, **merges with this
      one**.
- [x] **#73 `Coroutine<T> throws E`** (branch `feature/coroutine-throws`, 2026-08-23) — the
      throwability moved from the call to the TYPE, which is the only place that survives a field.
      The issue's own measurement was the design: a call runs no body, so checking it there checked
      the one event that cannot throw. A coroutine function's clause now describes the coroutine it
      returns; `resume` and `next()` are the throw sites; the type is written with a suffix
      (`?Coroutine<int> throws Exception`) wherever a type is written, refused on anything else
      (`LYR-SEM0084`). Assignment is one-directional — plain fits a throwing slot, not the reverse.
      Purely static: no IR, no bytecode, no runtime change. Spec §10 stops recording a gap, §2 gains
      the suffix, three suite cases
- [x] **every 2.x deprecation removed and `<Version>` at 3.0.0** (2026-08-23, same branch) —
      eleven forms: three in `std.io.file`, the eight free iterator adapters. The bump had to come
      WITH multi-conformance rather than after it: the suite gates cases by the toolchain version,
      and a tree that speaks 3.0 while claiming 2.17 cannot be tested by it. The runner learned
      `//! until:` for that — the mirror of `since:`, which retires a case a major left behind.
      One old bug fell out: a generic interface member on a generic-class receiver
      (`ArrayIterator<int>.zip<string>`) never worked; the free adapters had covered it
- [x] **attribute roots for reachability pruning — ALREADY DELIVERED, found by checking**
      (2026-08-23). The basket carried it as open; it landed with format 3.2 on 2026-08-18, and
      the spec has stated it since (§4.6, §4.7). An attributed function is a root because the row
      in section 11 is a promise to a host that calls it by index — a caller no call graph shows —
      and the rows follow the renumbering. Verified end to end: a non-pub `@Test` in a library
      survives the prune, and `lyrtest` still names the right function after a prune shifts the
      indices. A type attribute needs no method rooting: a host reads such a row for the type's
      SHAPE (`FieldsOf`), never to call into it. What was genuinely missing was the PIN — the only
      coverage was an inliner test about surviving inlining, which says nothing about library
      pruning or renumbering. Two tests in `ExportRootTests` now hold both
- [x] **the JIT's remaining obligations** (branch `feature/jit-obligations`, 2026-08-23) — and
      one that was not on the list: the embedding API had no way to turn it on. `HostOptions` had
      no switch and `LangVm.Instantiate` never passed one, so the audience the JIT is FOR could
      not reach it. `HostOptions.Compile` now wires through, and `ScriptInstance` exposes
      `CompiledFunctions` and `Refusals` — a host cannot tune what it cannot read. Guide 14 gains
      the section: the default and why, the metered-call rule, the refusal list, and the AOT line
      (emitting IL at run time and publishing ahead-of-time are alternatives, not a pair; under
      NativeAOT every function is declined with `no runtime code generation`). CI runs the whole
      suite a second time with `LYRIC_JIT=1`, and publishing needs both engines green.
      **The branch is current with main and passes both**: 4714 interpreted, 4714 compiled

**v1.0.0 through v2.0.0 are released** — annotated tags on the remote, each with a release page.
M0–M10 are finished and tagged (`m0`–`m10-complete`, `v0.1.0`/`v0.5.0`/`v0.9.0`). Releases
v1.8.0 through v1.9.1 carried the three toolchain archives plus two installables; since the org
split the editor clients release from their own repositories, and a toolchain release carries
the archives alone.

**M24 — the freeze prep — is BUILT** (2026-08-19, branch `feature/m24-freeze-prep`, four
slices, ships as v1.15.0). The design leftovers settled BEFORE the semantics freeze. The
delivery list:

- [x] `opaque type`: a new identity over the same layout — explicit `as` is the one crossing,
      equality within one alias, everything else refused; native signatures resolve
      module-local aliases, so an SDK handle crosses as a plain number scripts cannot forge.
      Erato's A4, answered end to end (slice 1)
- [x] the string METHOD API: 26 methods via `extend string`, free forms deprecated toward 2.0;
      concat/repeat stay free as operator backing; `import std.string as strings;` is the
      idiom, and an import whose extensions are used counts as used (slice 2)
- [x] a latent lowering bug fell with it: the global initializer could collide with a
      downstream function id once struct-return buffers and extension requests met in one
      compile; ids merge from one counter now, and holes are a named internal error (slice 2)
- [x] iterator method chaining: probed, documented No — sema-legal, refused by the lowering on
      both paths; see §Design decisions, pinned in LoweringTests (slice 3)
- [x] Grammar §TypeAlias, guides 12 and 13, Erato register A4 updated, CHANGELOG as
      Unreleased (slice 4)

**M23 — the std polish — is BUILT** (2026-08-19, branch `feature/m23-std-polish`, four slices,
ships as v1.14.0). Born from a full audit of the actual std, not a wishlist. The delivery list:

- [x] std.string stops being quadratic: builder/join fold no more, searches and parsers index
      chars; audit rests (German locals in fmt, a torn doc line, a lying section divider) gone
      (slice 1)
- [x] List.clear keeps its backing; test.assertTrue delegates to core.assert (slice 1)
- [x] print/println/eprint/eprintln generic over Display — println(42) works, write/writeln
      deprecated as the second name for the same thing; old bytecode keeps running (slice 2)
- [x] List insert/removeAt/first/last/reverse/swap; Map getOr/clear/entries; Set clear +
      Iterable; iter flatMap/chunks/reduce/first (slice 2)
- [x] arrays cross the native boundary as PARAMETERS — the format always allowed it; the
      registry checks element tags at bind time now (slice 3)
- [x] writeBytes/appendBytes, utf8Encode/utf8Decode (strict: invalid bytes are null, not
      U+FFFD), joinAll behind join/build, fromChars native (slice 3)
- [x] std.random: the generator moved out of math, plus shuffle/choice/nextGaussian; the math
      twin deprecated for one release (slice 4)
- [x] std.time: Instant/Duration over epoch millis, iso() with floor semantics for pre-epoch
      days; osAccess, deliberately no new capability bit (slice 4)
- [x] doc ratchet 370 → 430, still completeness; stdlib-tests grow file/random/time suites;
      guide 13, CHANGELOG as Unreleased (all slices)

**M22 — the language gaps — is BUILT** (2026-08-19, branch `feature/m22-language-gaps`, four
slices, ships as v1.13.0). The delivery list:

- [x] compound assignment reaches through the operator interfaces for variable targets; field
      and element targets stay diagnosed — the shorthand would evaluate the object or index
      twice (slice 1)
- [x] interface inheritance: ONE parent, implication-only — conformance, constraints,
      defaults, throwability and interface values all reach through the chain; the
      chain-prefix slot layout keeps a parent's default valid behind a child receiver
      (slice 2)
- [x] the parent-list rules: only interfaces, no cycles, at most one entry (LYR-SEM0078), no
      redeclaring a chain member (LYR-SEM0079); LYR-PAR0039 retired from error to feature
      (slice 2)
- [x] `std.core` is the library's root — it imports nothing; `newStringBuilder` got its
      @Deprecated, and the attribute keeps its promise now: no metadata row, no DCE root
      (slice 3)
- [x] heterogeneous arithmetic: the probe ran, the answer is a documented No — see §Design
      decisions (slice 3)
- [x] block lambdas infer their return type from their returns, unified like match arms; the
      open-generic case binds U from the block (slice 4)
- [x] Grammar §3.5, guides 3 and 7, CHANGELOG as Unreleased (slice 4)

**M21 — the std rework — is BUILT** (2026-08-19, branch `feature/m21-std-rework`, four
slices, ships as v1.12.0). The delivery list:

- [x] every public item of the standard library documented; the coverage ratchet pins
      COMPLETENESS (370 of 370), not a number (slice 1)
- [x] the audit fixed inline where behavior-neutral: German locals and two parameters, torn
      fragments, milestone references in comments, the misplaced capacity doc (slice 1)
- [x] the import-std.string crash is a diagnostic naming the trap; a builtin-shadowing import
      warns (LYR-SEM0077) (slice 2)
- [x] readBytes — raw bytes against the U+FFFD limitation; write-side filed, array parameters
      have never crossed the native boundary (slice 2)
- [x] constructors on the types (List/Map/Set.empty, StringBuilder.new, Random.seeded); the
      approved relics deprecated with successors, corpus migrated in the same commit (slice 2)
- [x] @Deprecated may sit on generics (the one row-less exception), and a generic static call
      substitutes the caller's T — two Vm regression tests pin both (slice 2)
- [x] stdlib-tests/: 27 behavioral tests in Lyric, run by lyrtest, wired into dotnet test,
      covered by both corpus invariants (slice 3)
- [x] guide 13 documents constructors-on-types and the stale-copy trap; CHANGELOG as
      Unreleased (slice 4)

**M20 — attributes become load-bearing — is BUILT** (2026-08-19, branch
`feature/m20-attributes`, three slices, ships as v1.11.0). The delivery list:

- [x] `@Deprecated` in std.core; every use warns at the use site, the note points at the
      attribute, `message` names the way forward (slice 1)
- [x] resolved by identity, not by name; self- and sibling-exemption; a deprecated module
      warns at its imports; editors strike uses through (slice 1)
- [x] guide 15 documents the compiler-read set as contract (slice 1)
- [x] `std.test`: the `Test` marker, `assertTrue`, `assertEq` naming both values (slice 2)
- [x] `testRoot` in `lyric.json`; only the runner compiles it — the Go shape (slice 2)
- [x] `lyrtest`, the tenth binary: discovery through the attribute rows, a FRESH instance per
      test, panic = FAIL with frames, exit code carries the verdict (slice 2)
- [x] `HostOptions.SourceRoot` in the embedding API (slice 2)
- [x] `lyric test` in the driver; guide chapter 20; CHANGELOG as Unreleased (slice 3)

**M19 — diagnostics — is BUILT** (2026-08-19, branch `feature/m19-diagnostics`, four slices,
the first milestone of the v2 sequence: v1.10.0). The delivery list, ticked point by point:

- [x] four severities — Info joins — render in text, JSON and over the protocol (slice 1)
- [x] a diagnostic carries notes; the problem-matcher head format is pinned by test (slice 1)
- [x] `--deny-warnings` on check and build; a denied build writes no artifact (slice 1)
- [x] warnings: unused locals/loop/catch/pattern bindings, unused imports, unreachable
      statements, static-extension-through-instance as the deprecation clock; duplicate module
      names are an error with a note (slice 2)
- [x] did-you-mean, previous-declaration and declared-here notes; LYR-SEM0046 carries its way
      out as a note (slice 3)
- [x] the first hint: a `var` through which nothing is ever changed (slice 3)
- [x] editors fade unused code and strike through the deprecated form (slice 4)
- [x] the corpus checks in SILENCE, held by a test the way the formatter holds its shape (slice 4)
- [x] guide chapter 19; CHANGELOG prepared as Unreleased (slice 4)

**M18 — the formatter — is BUILT** (2026-08-19, branch `feature/m18-lyrfmt`, stacked on M17,
four slices). The delivery list, ticked point by point as the milestone rule demands:

- [x] the lexer keeps comments on request; the compile path stays byte-identical (slice 1)
- [x] the document algebra and its renderer — only the renderer measures columns (slice 1)
- [x] the whole AST prints: literals from their spans, parentheses re-derived from §6.1 (slice 2)
- [x] comments survive — all three forms, one positional mechanism; blank lines are the
      user's, capped at one (slice 3)
- [x] the corpus invariants: every `.lyr` in the repository formats, is stable, reparses to
      the same tree, loses no comment (slice 3)
- [x] `lyrfmt` in place / `--check` / `--stdin`, `lyric fmt` in the driver (slice 4)
- [x] the repository formats ITSELF, and a test holds it formatted from now on (slice 4)
- [x] guide chapter 18; CONTRIBUTING's "no formatter in v1" clause retired (slice 4)

**M17 — packing: a program becomes one file — is BUILT** (2026-08-18, branch
`feature/m17-lyrpack`, three slices, PR #50). `lyric pack app.lyr` produces a standalone
executable: a prebuilt stub runtime with the `.lyrbc` and a 24-byte footer appended — a byte
copy, no linker. Two new binaries (`lyrpack`, packer, references Core ALONE; `lyrstub`, the
runtime half of a packed program), format contract in `docs/Pack.md`, guide chapter 17, and
both workflows pack-and-run an example on every platform before archiving.

**M16 — the tooling milestone — is CLOSED** (decided and built 2026-08-18, at the post-v1 pace):
the language server learned the project, the editors learned the server. The delivery list,
ticked point by point as the milestone rule demands:

- [x] project-wide compilation (PR #43)
- [x] rename (PR #44)
- [x] workspace symbols (PR #44)
- [x] semantic tokens (PR #45)
- [x] signature help (PR #46)
- [x] folding (PR #46)
- [x] inlay hints (PR #46)
- [x] restart command and status item in VS Code (PR #47)
- [x] task provider with problem matcher (PR #47)
- [x] snippets (PR #47)
- [x] `.vsix` in the release (PR #47)
- [x] the JetBrains thin plugin (PR #48)

The PR stack #43 ← #44 ← #45 ← #46 ← #47 ← #48 is merged and shipped as **v1.8.0**.

**M14 and M15 are what v1.7.0 shipped, both built 2026-08-18**: the interpreter stops allocating
(frame pooling, inlining, scalar replacement, devirtualization) and the native boundary learns
value structs at 0 B per call. PRs #41 and #42, details under *Recently finished*.

**M13, attributes, is what v1.6.0 shipped**: a program says things about itself that a host can
read. Format 3.2, and the 7-day rule was retired with it — it was pre-v1 scope discipline, and v1.0
has shipped; from here the pace is our own.

**M12, the project system, is what v1.2.0 shipped**: `lyric.json` says what a project is, `build.lyr`
says what to build, `lyric new` writes one, and the tools read all of it.

**v1.5.0 shipped operators**: `==`, the orderings, arithmetic and `as` all resolve through the
interface a type declares — no operator syntax, no new opcode. Method overloading was considered and
rejected; the constraint mechanism is this language's overloading.

**M11, the language server, is CLOSED.** Diagnostics while you type, what a name under the cursor is,
where it was declared, a program followed across its files, documentation on hover, the outline of a
file, every place a name occurs, and completion. v1.3.0 shipped the first seven, v1.4.0 the last.

4913 tests green **on both engines** (interpreted and `LYRIC_JIT=1`), bytecode format **3.6**,
**eleven** binaries plus `lyrembed.dll`, version **3.9.0**; the specification in
`lyriclang/lyric-spec` is **NORMATIVE**, its suite stands at 144 cases pinned to 3.9.1 (143/143
against this tree, one platform-gated skip; the 2026-08-25 spec rounds landed spec-first, #21
through #25, each toolchain twin behind its rule), and the toolchain's own CI runs it against the
working tree. *(The test count is the one last counted, at 3.6.0, Debug.)*

**What this state can do**: the whole language of the grammar compiles and runs; a standard library
that largely carries itself (`Map`, `Set`, merge sort, all iterator adapters and the string hash are
written in Lyric); six tools including the REPL, the build runner and a language server that
compiles the PROJECT — references across files in both directions, diagnostics for files nobody has
open, disk changes behind the editor picked up through file watches; a VS Code extension with live
diagnostics; a project that scaffolds, declares its own layout and builds itself with a Lyric
script; and an embedding API with which a C# host loads scripts, sandboxes them, calls functions
out of them and hands its own functions, types and value structs in.

> **The file had grown to 1088 lines by 2026-08-07** and contradicted itself in three places. It has
> been cut back to its own maintenance rule: recent slices, open points, design context. Everything
> else stands in `git log`.

## Recently finished

- [x] **Spec round 1 + twin** (2026-08-25, `lyric-spec#21` + PR #116, released as v3.6.0).
  Details under §Current milestone. **Three findings worth keeping.** (1) The inference loch was
  REAL and order-dependent: the identical call compiled with `[Sink<int>, Sink<string>]` and
  failed with the entries swapped — pinned as a two-way theory before the fix. (2) Probing spec
  claims against the tree found four false sentences (two in §5.3, one in §8.1, one in §3.3) and
  two stale guide claims (guide 7's `v.mul(2.0)`-is-ambiguous and one-place-only) — the
  spec-first mode pays for itself in corrections, not only in new rules. (3) `catch (_: T)`
  crashed the compiler while the SEM0071 note RECOMMENDED that form; found by probing catch
  shapes for the appendix, the class of find sweeps exist for.

- [x] **M34 — the data formats** (2026-08-24, `feature/m34-data-formats`, released as v3.5.0).
  `std.json` and `std.encoding`, both written in Lyric — the first structured-data story the
  library has. What the milestone turned up outside its scope: `std.string.parseFloat` could not
  carry a JSON number — no exponent notation (its own doc said so), and the digit-by-digit
  fraction sum drifted an ulp on long fractions, so `parseFloat(fromFloat(x))` was not reliably
  `x`. It is NATIVE now, correctly rounded; new-native binding rule as with `co.next()` in 2.2.0.

  **Two decisions worth keeping.** The json parser decides int-fits on the DIGITS before calling
  `parseInt`, because `parseInt` wraps on overflow by documented design — a wrapped id would be a
  silently wrong value, the exact shape the sweeps hunt. And the parser carries its own depth cap
  (128): input is data, and the VM's 1024-frame panic is not an answer a `?JsonValue` contract
  may give. **One harness lesson**: `dotnet test | tail` reports tail's exit code, not dotnet's —
  two slice verdicts leaned on that pipe before it was caught; suite gates read the real exit
  now, redirected to a file instead of piped.

  **The sweep ran the same day and found NOTHING** (`fix/m34-sweep`): 41 adversarial probes —
  object/mixed nesting at the bound, number and string corners, eleven refusal shapes, the
  interactions (a `JsonValue` through a coroutine, the REPL, `LYRIC_JIT=1`, capability gating,
  `lyric pack` of a json program) — no crash, no wrong answer, no guide claim off. The probes
  with pin value landed as tests; no patch release, per the pipeline the loop exits here.

- [x] **The pipeline bug sweep** (2026-08-24, `fix/pipeline-sweep`, PR #111). Thirteen fixes,
  bottom up through every stage, each with a failing test first. The three worth keeping were
  reachable from plain source or from foreign bytes and ended in a crash rather than a
  diagnostic: `"\u{80000000}"` wrapped past `Int32` and took the compiler down with an unhandled
  exception; `[0] * n` reached the allocator as an overflowed length and took the PROCESS
  down — the escape `Capability.None` is sold on; and a module that under-declares its
  capabilities was handed `std.io.file` and ran it, so the "a host loading foreign bytes is
  protected too" promise the code makes did not hold.

  **The lesson is the reader's.** Its "must reject" catalogue had four holes between §5/§8.5 and
  the validator — `IsTerminator` knew five of nine terminators, so the stack walk crossed block
  boundaries; `ret`/`retval`/`throw` and the entry-point signature were checked nowhere. The
  round-trip tests could never have found them: they only ever show the reader ACCEPTING what the
  writer produced. What was missing was a negative catalogue of hand-built modules and a
  totality layer under the front end — both added, seeded, so a crash reproduces. The capability
  fix is the other half: the load check refused declaring MORE than granted, this refuses USING
  more than declared, and together the declared bitset is a verified bound rather than a trusted
  one.

- [x] **A24 — the four entrances a chain can start from** (2026-08-24,
  `feature/a24-chain-entrances`, PR #110, released as v3.4.0). `over`, `range`,
  `rangeInclusive` and `compact` in `std.iter`. The last open entry in Erato's register, and it
  cost no language change at all — which is the part worth keeping, because the entry was filed
  as a request for `iter()` on arrays and ranges and that shape is not available: an `extend`
  block cannot bind an element type, and a range is not a value, so `(a..b).iter()` could never
  exist. The fallback the register offered as second best was the only reachable form and it was
  already writable in user code; what the standard library adds is that everyone has it.

  **`compact` is the one with a design in it.** `filterNotNull` on an ITERATOR cannot be
  written — `Iterator<?T>` needs `??T` and `?` does not nest, which is the wall `LYR-SEM0091`
  names. Taking the ARRAY instead dodges it by construction and stays lazy: an array slot can be
  read as `?T` without the end-marker being in the way.

## Measurements

Numbers instead of opinions. Since 2026-08-18 they come from `tools/Bench` — in-process, Release,
100 000 operations per case, minima over nine runs, a scalar loop of the same shape subtracted.
`dotnet run -c Release --project tools/Bench` reproduces them. The M14 baseline:

| Case | ns/op adj. | B/op adj. |
|---|---:|---:|
| call (`fn step(a: float): float`) | 49.9 | **176** |
| struct construction only | 60.6 | 56 |
| construction **plus** method call (`Vec2.add`) | 271.0 | **352** |
| the same through `a + b` (`Add<Vec2>`) | 252.3 | 352 |
| `for-in` over a range (against `while`) | 143.2 | **208** |
| `for-in` over an array (against `while`) | 153.1 | 208 |
| `Set.iter()`, the `callvirt` route (against `while`) | 420.9 | 229 |

One correction to the old numbers: the 112 B for "construction only" was a four-field shape; the
two-field `Vec2` is 56 B. And one to the harness: the interpreter loop is one shared method that
tiered compilation keeps improving while the harness runs, so the cases are measured round-robin
over three cycles, minimum per case — sequentially, the scalar baseline came out slower than the
loop doing the same work plus a call.

**After slice 1 (frame pooling):** call **176 → 0 B**; `for-in` range **208 → 0.1 B** — the
208 B were the frame trio alone; a `Some` over a scalar never allocated, disproving slice 0's
guess. **After slice 2 (inlining), adjusted ns/op:** `Vec2.add` 271 → **112**, `for-in` range
143 → **68**, array 153 → **94**; the ~7 ns residue on a bare call is the spliced
parameter/return traffic through locals.

**After slice 3 (scalar replacement), against the slice-0 baseline, adjusted:**

| Case | baseline | now |
|---|---:|---:|
| construction only | 60.6 ns / 56 B | **18.2 ns / 0 B** |
| `Vec2.add` plus assignment | 271.0 ns / 352 B | **8.4 ns / 0 B** |
| the same through `a + b` | 252.3 ns / 352 B | **6.1 ns / 0 B** |
| `for-in` over a range | 143.2 ns / 208 B | **40.5 ns / 0 B** |
| `for-in` over an array | 153.1 ns / 208 B | **109.7 ns / 0.3 B** |

The `Vec2` gate is met: expression-shaped struct code allocates NOTHING and runs ~30–40× the
baseline. The range loop is 0 B but 1.85× a `while` — the optional ops and the extra block hops
remain; honest, and material for a later peephole rather than this milestone.

**After slice 4 (devirtualization):** the `Set.iter()` loop carries no `callvirt` anymore — the
receiver's single `mkiface` proves the concrete type, and the loop direct-calls
`SetIterator<int>.next` (too big to inline; the call is pooled). The gate was structural and is
met; the time gain drowns in the probing work the loop actually does. One find on the way, caught
by the verifier: a DEFAULT-method slot takes the fat pointer, not the concrete value — `this` in
a default method dispatches virtually.

**The VM is allocation-free at its core** — a loop with floating-point arithmetic allocates nothing
worth mentioning over 100 000 passes. Everything above that is calls and objects.

**Half the bytes have nothing to do with structs**: `Frame.For` allocates three objects per call
(frame, slots, stack). That fixes the order for a later optimization — **frame pooling, then
inlining, then scalar replacement**, not the other way round: the value built in `add` **escapes**
(it is returned), so escape analysis without prior inlining finds nothing. **None of it is built in
v1.**

Within the frame budget: 1000 entities × 10 vector operations × 60 fps ≈ 211 MB/s, roughly one gen0
collection per frame. Gen0 is short — **no reason to move vector mathematics behind natives.**

Measured further: `for-in` over a range costs **1.28×** against a `while` loop. The verifier is
**~50 %** of the lowering time in Debug, not ~90 % — the old claim never had a source. A Release
profile is still outstanding.

**Compiler latency, in-process** (2026-08-14, Release, 15 runs). A full `Check`, standard library
included:

| What | median | min |
|---|---|---|
| 6 lines | 14.3 ms | 7.1 ms |
| ~85 lines with a standard library import | 16.0 ms | 9.2 ms |

The same work through `lyrc check` measures **181 ms** and **212 ms**, against **40 ms** for
`--version` alone. The difference is process start and JIT warm-up: a long-lived process pays it
once, a batch invocation pays it every time. **Measure in the process that will do the work.** The
batch number is an upper bound, and here it was off by a factor of ten in the direction that would
have bought an incremental compiler nobody needs.

**The workspace compile confirms it** (2026-08-18, in-process, Release, 15 runs): a 14-file
project through `CheckProject` measures **median 41.7 ms** against **40.1 ms** for a single file
of it. The standard library dominates; the project's size is in the noise, and the incremental
compiler stays unwarranted at project scale too.

**A18 — an opaque type leaves a name in the module — is RELEASED as v2.11.0** (2026-08-22,
format 3.5). The register's second finding from the save system, and the one that cost a
decision rather than a line. What Erato asked for was "a name in the Types table"; the Types
table has no row for an alias, and referring to one from a field type would need a new type
TAG — a tag is not skippable, because every reader of a signature has to know all of them. So it
became a section of its own (id 14), which an older reader steps over without noticing.

**The shape of the answer is the interesting part**: the field stays an `i64`. The section adds a
NAME beside the type rather than a type, which is the only form that keeps the opacity free at
runtime — the property A4 was built for — while making it visible to a host. A trace, not a
mechanism.

**A17 — an attribute default across a module line — is RELEASED as v2.10.1** (2026-08-22).
Erato filed it hours after the 2.10.0 re-pin: the enum argument worked written and failed
defaulted, so `@Saved` — the first line of every example — did not compile once the class sat in
another module than the attribute. **It is A16 one level down**: declarations were checked in
DISCOVERY order, so a use could be checked before the declaration whose default it depends on,
and a name in that default had no meaning yet. The same walk fixes it, and the same sentence
holds — what a file means must not depend on who was checked first.

**Worth keeping**: A16 changed the globals loop and left the declarations loop alone, because
globals were what the finding was about. One order was right and its neighbour was not, in the
same method, two lines apart.

**An enum variant is an attribute argument — RELEASED as v2.10.0** (2026-08-22, maintainer's
request). The form I weighed and rejected when A11 landed: back then the note said "the row
carries only scalars and strings, and the host would have to interpret variants — much bigger".
Half of that was true and half was an estimate that never got made. The row DID need a new
form, so the format moved 3.3 → 3.4; the host side did NOT need interpreting, because the
field's type names the enum and the enum's entry names its variants — the reader resolves
`Layout.Separate` from what the module already carries and hands the host a name and a tag.

**The compatibility line is the one to remember**: this is the first format change a 3.3 reader
cannot ignore. The Attributes section is skippable as a whole, but a reader that DOES read it
meets a tag it has no case for — so a module whose rows use an enum value does not load on an
older runtime, while one without such a value still does. Same forward path as `co.next()` in
2.2.0, and stated in both the format chapter and the changelog rather than discovered.

**A15 — a debug adapter that attaches — is RELEASED as v2.9.0** (2026-08-22). The register
called it a convenience rather than a blockade, and it was right about the protocol half: the
messages, the session, the `StopReason` translation and the variable references all stood. What
it was NOT right about is where the work sat — the interesting part is not `attach`, it is what
happens when a session ENDS without the program ending. `DebugController.Detach()` is the new
piece: breakpoints go, a parked thread is released, the event stream closes. Without it an
editor that crashes leaves a game standing at its breakpoint for good.

Two questions the register listed as "only an embedder can answer" answered themselves once the
shape was right: WHICH session knows a file is decided by the connection it arrived on (one
server per controller), and `stackTrace` on a running program already failed honestly, because
the controller's inspection surface has always thrown when not paused. The third — a scene
change replacing the controller under a still-connected editor — stays the embedder's, as the
register says.

**A16 — the initialization order follows the imports — is RELEASED as v2.8.0** (2026-08-22).
Erato filed it after E15, and it is the first of their findings that was a real defect rather
than a missing shape: global initialization ran in DISCOVERY order from the entry file, so a
third module decided whether a second one compiled, and the same file compiled or not depending
on who compiled it. Their player compiles every file as its own entry — to read its attribute
rows — so "compiled as an entry" is the normal case for them, and `lyric check main.lyr` said
`ok` about files the player rejected.

The fix is one walk: `Compilation.InitializationOrder()`, a DFS post-order over the import edges,
used by the sema (which decides `LYR-SEM0057`) and by `GlobalTable` (which emits `<globals>`) —
one order for both, or they would drift. **What the register got right and the old comment did
not**: an import IS the dependency statement and it was already there to be read, and the
comment's own citation of Go named the answer it was not taking. **Found while building**: import
cycles are already `LYR-RES0005`, so the walk's cycle guard is not tolerance but survival — the
sema still runs on the broken graph, and a recursing walk would hang instead of letting the
diagnostic through.

**A15 — a DAP `attach` — is NOT built and is not planned yet.** The register calls it "eine
Bequemlichkeit, keine Blockade" and Erato has shipped its own adapter (730 lines against
`DapServer`'s 527). What an `attach` would save is the next embedder writing them again; what it
needs first is an answer to questions only an embedder has — which of five running sessions knows
this file, what `stackTrace` says while the program runs, what happens when the editor
disappears. Worth a design round, not a slice.

**The editor clients are catching up — jetbrains-lyric gains run and debug** (2026-08-22, PR
`lyriclang/jetbrains-lyric#1`, ships as 1.3.0). The plugin could not start a program at all. It has
a run configuration now — `lyric run`, the driver, with the compiler's diagnostics clickable in the
console — and the same configuration under Debug drives `lyrdbg` through the platform's OWN DAP
client: no `XDebugProcess`, no second protocol implementation, the mirror of the LSP wiring one
door further. Refused on purpose and visible in the UI: no breakpoint conditions (the adapter
evaluates names, not expressions) and no exception breakpoints (a panic ends a program instead of
stopping it).

**What the new `verifyPlugin` gate found on its FIRST run**: the plugin was incompatible with the
2026.1 baseline it claims, and had been since the org split. Compiled against 2026.2, the Kotlin
override binds `LspIntegrationProvider.LspClientStarter` — the name the LSP interfaces were renamed
to there — and 2026.1 has no such class, so opening a `.lyr` file would have thrown
`NoSuchClassError`. The rule that follows: **compile against the OLDEST supported release and
verify both ends**. Old names resolve in the new IDE; new names do not resolve in the old one.

**v2.7.1 fell out of the same wiring**: `lyrdbg` refused `setExceptionBreakpoints`, a request an
editor sends during configuration whether or not filters are offered. VS Code never sends it, so
one client was never going to find it.

**What is left for the clients**: neither has ever had a release. Both `release.yml` wait on a
`vX.Y.Z` tag, both READMEs promise an installable, and since the split there is none to download.
Versions stand at 1.3.0 on both sides, and the JetBrains checklist has still never been run against
a released zip.

**A14 — the debugger reaches an invoked function — is RELEASED as v2.7.0** (2026-08-21). Erato
filed it after E13, and it is the same class as A13 one door further: everything M30 built was
offered at `RunEntry` alone, and an embedded script has no `main` to start. One overload —
`Invoke(index, controller, args)`, the shape the budget got in 2.4 — and the whole machinery is
reachable. Nothing in `DebugController` needed changing: its command surface was already a
semaphore and two volatile fields, so a host driving the call from its own thread works as it
stands.

**What was asked for and NOT built**, because the register calls it a wish rather than a
requirement: a controller a host can drive without a second thread, so a window could keep
drawing while the script stands still. That needs an interpreter that returns to its host
mid-instruction and resumes later; this one keeps its frame stack on the CLR stack, which is a
different machine. Guide 21 states it.

**A13 — the compilation error with a place — is RELEASED as v2.6.0** (2026-08-21). Erato filed
it after E12: `EmbeddingException` handed out compiler `Diagnostic`s whose `Span` is an index
into the compilation's source manager, and that manager never left the toolchain — so a host
caught a code and a message, in a project of thirteen files. The place is resolved AT THE THROW
now, into `ScriptDiagnostic` (file, line, column, notes and all). Three options stood in the
register; this is the first one it asked for, and the other two — handing out the manager, or a
rendered blob — were refused for the same reason: a manager outlives nothing and a blob cannot
be jumped to.

**Found beside it, unfiled**: `ScriptPanicException` did not carry the backtrace either. Erato
reached it through `InnerException` — that means naming `LyricPanic`, a runtime type the whole
embedding API exists so a host need NOT reference. `Backtrace` is a property now.

**The formatter breaks operator chains — RELEASED as v2.5.1** (2026-08-21). The last item of
Erato's E7 note, cut out of M31 and done on its own. `BinaryDoc` held no line opportunity at
all, so a chain could not break however long it grew; it flattens the level now and breaks
before every operator, indented one step, operator-leading. **The prediction that cut it from
M31 was wrong**: it changes "the shape of every formatted file in the corpus" — measured, it
changes NOTHING in this repository's corpus, and the only file that moved during development
was `examples/inventory.lyr`, which the match-expression carve-out then put back. The carve-out
is the design finding: a group around a chain containing a hard line can never be flat, so
`return base * match (r) { … }` would have broken its operator off for a reason that has
nothing to do with the width. `Doc.WillBreak` answers that question now, where Prettier's
`willBreak` sits.

**A12 — the foreign value struct — is RELEASED as v2.5.0** (2026-08-21): Erato filed it after
E10, and it is the sibling of A9 in the same file. `NativeStructParameter` looked its type up
with `module.Members.LookupLocal` and required a single-segment path, so an imported or
qualified struct never matched and fell through to `LYR-IR0001`. It asks the resolver now, as
`ResolveLocalAliases` has since 2.2.1. **The 2.2.1 changelog called that restriction
"documented and deliberate" and it was not** — the reason given there explains why the alias
fix did not carry structs along, not why a foreign struct may not be flattened. No spec change:
value structs in native signatures are toolchain surface, and the spec says nothing about them.
Guide 13 gained the `…Int` family table in the same wave — `clampInt` existed all along, but
nothing named the convention where someone would look.

**M31 is BUILT and RELEASED as v2.4.0** (2026-08-21). Details under §Current milestone. What
is left for Erato: the register's A10/A11/A12 entries want closing — all three are delivered.

**The v2.3.1 bug wave is BUILT** (2026-08-21, branch `fix/v2.3.1-bug-wave`, three slices).
Four of the five findings of the post-2.3.0 audit, fixed and pinned: the inherited default
through a child-typed value (#71 — the cause was the DEVIRTUALIZER, not the lowering the issue
suspected, and the constraint route had a second gap beside it), the array-literal element that
lowered without its context (#72 — the same fix carries the interface lift nobody had filed),
the shared console code page behind years of sporadic process-test failures (#74), and the
uppercase float exponent (#75, with the rendering now specified in §11 and routed through one
place). Spec PR: `lyriclang/lyric-spec#1`, suite 85 → 88.

**#73 stays open, deliberately.** The audit called it a design round and it is one — sharper
than filed: the `throws` of a coroutine function is checked at the CALL, which cannot throw,
and unchecked at the `resume`/`next()`, which can. The local case only looks right because the
try covering the call usually covers the resumes too. Every route out of that scope — an
optional, a field, `next()` through a field — reaches `LYR-VM0010`, which Appendix A still
calls "reachable only for a hand-built module". Fixing it means deciding what throwability a
coroutine VALUE carries; that is a language change, not a patch.

Decided 2026-08-21: **document now, carry the type change as its own milestone.** Spec chapter
10 states the gap and Appendix A stops claiming VM0010 is unreachable (`lyric-spec#2`); guide
11 gets the user-facing half — wrap the pull, not only the call. Two answers were weighed and
refused in the text: every pull throwing taxes the ordinary coroutine, and refusing a throwing
coroutine a field would refuse programs whose RUNTIME behaviour is already correct.

**M30 — the debugger — is RELEASED as v2.3.0** (2026-08-20). Details under §Current milestone.
Found on the way and fixed in its own commit: `tools/Bench` had not compiled since the 2.0
deprecation removal (`set_iter` still imported `emptySet`) — every bench run died before the
first number, which is also why the bench gate for the policy loop had no pre-existing baseline
to lean on.

Next: the file-error design round (A) — small vs. big unification — whenever the maintainer
calls it. Erato-side: engine.task and engine.assets rework.

**A9 — the imported-alias fix — is RELEASED as v2.2.1** (2026-08-20): Erato's register filed
A9 the day of the 2.2.0 re-pin — an opaque type imported from a sibling SDK module did not
resolve in a native signature (`LYR-IR0001`), while §3.5 promises the resolution without a
module restriction; the A4 mechanism was purely syntactic and module-local. Fixed with a
binding fallback in `ResolveLocalAliases`: selective and qualified imports, scalar and array
positions, alias chains across modules. Structs in native signatures stay module-local by
design. No spec change — the spec was already right; pinned by an Embedding test (a
conformance case cannot express native roots).

**M29 — the A8 wave — is RELEASED as v2.2.0** (2026-08-20, PR #68). Both coroutine edges
Erato's register filed under A8, plus one the work uncovered:

- [x] slice 1: `Coroutine<T>` lowers as a FIELD type — the AST path `TypeTable.Lower(TypeNode)`
      learns the special case the sema and the LyrType path always had; `List<Coroutine<T>>`
      through the type-argument path too. The closure idiom stops being mandatory.
- [x] slice 2: `co.next()` — the safe pull beside the panicking `resume`. `?T` (bool for
      `Coroutine<void>`); refused on `Coroutine<?T>` (LYR-SEM0080), where null is ambiguous.
      The body takes a lenient flag; its done-exits read a zeroed state field instead of
      manufacturing values; the marker is read from outside by the compiler-bound native
      `std.core.coroutineIsDone` (per-signature import entries — format stays 3.2, an old
      runtime rejects at binding with the import's name, the designed forward path; a module
      not using `next()` keeps loading everywhere). `resume` semantics, frames and panic are
      UNTOUCHED; `isDone` alone was probed and rejected — a pull coroutine cannot answer
      "will there be another value" without pulling (no mainstream generator API has hasNext).
      Spec §10/§4.4/§11/Appendix A amended, suite 81 → 85 (`//! since: 2.2.0`), 85/85 against
      the working tree.
- [x] found on the way, fixed in slice 2: a bare `return;` MID-BODY in a coroutine — §10
      always allowed it — emitted a valueless `ret` from a value-yielding body: internal
      verifier error in Debug, malformed bytecode in Release, exhaustion never marked. It is
      the run-through exit now, shared by all three ways out.

**M28 — the ergonomics wave — is RELEASED as v2.1.0** (2026-08-20, PR #66). Two additive changes the audit measured before they were built:

- [x] `@Deprecated` reaches members: methods, fields, static lets, extend methods — the ONE
      attribute a member admits (no member rows in the format); interface members refused;
      no metadata row, pinned (slice 1)
- [x] the adaptation context propagates structurally: array elements and match/if arms check
      against a §3.1 context, literals adapt, misfits error AT the element or arm; the
      contexted mismatch diagnosis moved from SEM0016 to SEM0001 at the arm; array literals
      at a variadic position keep their shape-decides rule by taking no expectation (slice 2)

Next after the release: the file-error design round (A) — small vs. big unification —
whenever the maintainer calls it.

**M24 was merged and released as v1.15.0** (PR #62) — the freeze prep. With it the
pre-freeze design space is closed; next is v1.16, the spec draft (non-normative) plus the seed
of the conformance suite, and the semantics freeze begins there. The scope came
from a line-by-line audit of the standard library after the first extension list turned out to
describe modules that already existed. Deferred by decision: the string method API via `extend`
and iterator chaining (each needs its own design round plus a probe), the three-convention
file-error cleanup (a 2.0 cut), and member-level `@Deprecated` (the attribute cannot sit on a
member yet — surfaced when StringBuilder.length wanted one).

**The repository moved and the clients moved out** (2026-08-19): the project lives in the
`lyriclang` org — `lyriclang/lyric` is the toolchain, ONE repository with ONE version, and the
editor clients are their own repositories (`vscode-lyric`, `jetbrains-lyric`), split with their
history, versioned on their own cadence, releasing their own installables. The TextMate
grammar's canonical home is `tooling/textmate` HERE, beside the lexer `GrammarTests` pins it
against; each client carries a working copy its `grammar-sync` CI job diffs against this one.
The changelog note about where the installables went is written (Unreleased entry); Erato pins
checked-in binaries (`lib/lyric`), so nothing breaks — its README gets the new URL with the
next pin update.

**M17 and M18 shipped together as v1.9.0** (2026-08-19) — the PR stack #50 (packing) ← #51
(formatter), merged in that order; release commit and annotated tag explicitly delegated for
this release, normally the maintainer's step. The release workflow gates itself: it packs and
runs an example on every platform before an archive exists.

**M17's deliberate limits**: one platform per pack (a foreign platform packs via `--stub` with
that platform's stub out of its archive — no `--target` until someone needs it); the stub ships
untrimmed (measured 73.5 → 13.0 MB, decision material above); capability narrowing at pack time
is a footer field for a future minor. **And one limit the release gate found rather than the
plan**: a packed Mach-O failed codesign's strict validation, so macOS could not RUN packed
programs. *Closed in 3.1.0 (#54): the payload is folded into `__LINKEDIT` and the result signed
ad-hoc through `codesign`, which macOS ships — the header arithmetic is ours and unit-tested, the
signature is not. Both workflows run the packed program on macOS now and verify its signature.*

**M18's deliberate limits**: precedence-redundant parentheses vanish (the AST has no node for
them — keeping them means a parser change, material for the scope check if it itches); a
comment inside an expression surfaces at its statement's end. The
`textDocument/formatting` gap closed right after the release: the server answers with one
whole-document edit off the CURRENT buffer, a buffer that does not parse gets no edits, and
the client's tab preferences are read for nothing — one shape is the contract in the editor
too (`feature/lsp-formatting`).

**Deviation from the plan, recorded**: no own `Lyric.Formatting` library — the formatter is a
namespace in `lyrfe`, because both consumers (lyrfmt, lyrls) already share that assembly and a
fourth library bought naming trouble for zero separation.

**M16 is closed and released as v1.8.0.** What remains from it: the first manual run of the
JetBrains checklist (plugin README) against the released zip, in a 2026.1+ IDE.

The open points for the **2026-09-06 scope check**: heterogeneous arithmetic, compound
assignment through the interfaces, the first compiler-read attribute (v1.11 material), the
`for-in` peephole, Erato's A4 (an opaque `Entity`) and the E4-side adoption — plus, from M16:
parameter-name inlay hints and semantic-token deltas if a measurement ever asks. M19 closed two
former entries: the static-extension asymmetry warns as a deprecation now (the error lands with
2.0), and duplicate module names are `LYR-RES0007`.

**Not renameable, recorded**: a module (rename the file), an enum variant's payload field (no
symbol exists for it), anything whose declaring module is native. Renaming across `build.lyr` is
not covered — the build script sits outside the source root and compiles as its own unit; its
diagnostics say so on the next compile.

**Erato's A2 is answered in its useful direction** — the host declares the value types in its
SDK, the script uses them, nothing allocates. What remains on the register's list for Lyric is
A4 (an opaque `Entity`) and the E4-side adoption. The other open points — heterogeneous
arithmetic, compound assignment through the interfaces, the static-extension asymmetry,
the first compiler-read attribute, the `for-in` peephole — stay material for the **2026-09-06**
scope check.

**One limit stays**: a generic call shows the DECLARED signature, because the
substitution is private to the type checker and a second one in the server would be a second answer
to what `T` became. Measured by a test rather than left as an intention.

**The open question to answer before E4**: the lifetime and identity of a host object across the
boundary — does the host keep it alive or the VM? That is the one place in M10 where I have no
answer yet, and it belongs asked before E4 starts.

## Still open

**Tooling and format:**

- **A guest's OS handles outlive the guest.** A file opened through `std.io.stream` and not
  closed stays open for the lifetime of the THREAD: measured surviving a budget stop, a guest
  panic, an ordinary return, and the VM plus instance being garbage collected — only an explicit
  `close` releases it, and on Windows the file stays locked meanwhile. `_sockets` (4.0) and
  `_children` (4.0) are the same shape; the file case merely made it observable. **This is the
  sandbox's own promise**: a host that stops an untrusted script is left holding handles the guest
  opened. The fix is per-instance resource ownership — the ThreadStatic tables keyed by instance
  and released on dispose — which is the same change the cross-VM isolation question needs, and
  it is a design decision rather than a sweep fix. Found by the 4.2 sweep, round 3.

- **The four ways to ask "is this null?" disagree inside a GENERIC body.** With
  `fn f<T>(x: T)`: `x == null` and `x ?? fallback` COMPILE and behave correctly when `T` is
  instantiated with an optional (measured, `?int` → `false`/`true` and `9`/`3`), while `x!` is
  refused by `LYR-SEM0005` and `match (x) { null => … }` by `LYR-SEM0029`, both at declaration
  time. Two operators defer the question to monomorphization; two answer it up front. Either a
  `T` may be treated as possibly-optional — then the force-unwrap and the pattern should also
  wait — or it may not, and then the coalesce and the null test belong in the sema. **A spec-round
  candidate, and it is NOT additive either way**: making `??` strict would break any generic body
  that uses it today. Found by the 4.2 sweep, round 2; pre-existing.
- **An f-string interpolation holds a SCALAR, and a `Display` conformance does not change that.**
  `f"{p}"` on a struct is `LYR-IR0001: interpolating a non-scalar value` — correctly noted as a
  backend gap, since calling `show()` and splicing is exactly what the lowering would do — while
  `println(p)` works through the constraint. 4.1.0 made the gap FELT rather than created it, by
  giving `Instant` and `Duration` a `Display` and documenting that they print. The limit is now
  stated in guide 2 and guide 13; whether the lowering learns it is a feature decision, not a
  sweep fix. Found by the 4.2 sweep, round 2.

- **A `v1.0.1` runtime cannot read a module with a source map.** The skip that lets a reader step
  over a section it does not know was broken until 3.1, so the forward compatibility the format
  promises does not hold for the one release before it. `--no-source-map` produces a module those
  runtimes accept. Nothing can be done on their side; it is recorded so the next format addition is
  not mistaken for the same bug.
- **An `extend` block on an ARRAY type compiles and does nothing.**
  `extend int[] :: [Iterable<int>] { pub fn iter(): Iterator<int> { ... } }` parses, type-checks
  and is accepted; `xs.iter()` then says *'int[]' has no member 'iter'*. A declaration claiming
  something nothing checks is the shape 2.15 already fixed once for `::` lists. Found 2026-08-24
  while probing A24. The useful version cannot be written anyway — `ExtendDecl` has no generic
  parameters, so `extend T[]` cannot bind an element type; the defect is the SILENCE.
- **An `extend` block on an OPAQUE type compiles and its methods are reachable from nowhere.**
  The array entry's sibling, found by the 3.8.1 sweep: `extend Entity { pub fn again(): … }` on
  an imported opaque type-checks — the body even carries its SEM0093 warning correctly — but
  `spawn().again()` answers *'Entity' has no member* and `5.again()` answers *'int' has no
  member*. Decide whether an opaque takes extends (declaring-module SDK sugar would be the case
  for it) or is refused; what it must not stay is silent.
- **Opaque over opaque: the crossing follows the resolved ROOT, against §3.5's word.**
  `opaque type F = w.Entity;` accepts `42 as F` (F's underlying resolves THROUGH the foreign
  opaque to `int`; silent — F is local) and refuses `spawn() as F`, with a suggestion that
  cannot be taken (give `Entity` the conformance `Into<F>` — an opaque satisfies no
  constraint). §3.5 reads "the explicit `as` to exactly T and back", and T as written is
  `Entity`. No privilege breach — an F never converts to an Entity in either direction — but
  the sentence and the tree disagree. Decide: refuse the form, or define the crossing as the
  WRITTEN underlying. A spec-round candidate. Found by the 3.8.1 sweep.
- **`TypeResult._refs` holds declarations beside uses**, because the definite-assignment
  analysis binds a `BindingStmt`, a `Param`, a `ForInStmt` and the pattern bindings to the symbol
  they themselves declare. Since M19 the separation rule has its first consumer: the
  `WarningAnalyzer` splits the two by `ReferenceEquals(symbol.Declaration, node)`, exactly as the
  table's own documentation prescribes. No split table needed after all.
  - *The receiver-kind question is out of it since v1.4.0 slice 1*, which is what made the table safe
    to add to.
- **Section byte sizes are missing from `lyrvm info`**: the reader discards them after parsing.
  Retrofitting them would mean extending the model with provenance data — a decision of its own.
- **Measure the verifier share in a Release profile** — the Debug numbers are riddled with JIT
  warm-up and serve only as an order of magnitude.
- **§3.1's context does not propagate into TUPLE elements.** `(null, 0, "")` against
  `(?uint8[], int, string)` is "cannot assign" — the null element gets no context, while an
  array element or a match arm would adapt (2.1). Hit by M35's decode cores, worked around
  with a struct (field initializers ARE context positions). The 2.1 sentence says the contexts
  propagate "structurally"; tuples are the structural case it skipped. A spec-round candidate:
  additive, the §3.1 list gains one word.
- **An enum's throwability has three answers.** `enum E :: [Throwable]` with its own
  `message()` (the `;`-member syntax): the sema ACCEPTS throw/throws/catch, the lowering
  refuses the catch (`IR0001`, "catching a non-class type"), and the extend-provided
  conformance is refused by the sema (`SEM0030`) — while §9.1 says "only class values" and
  names structs alone. Probed 2026-08-25 during the M35 design round. Decide it once and align
  all three, whichever way.
- **A transparent alias is refused in a `::` list, against §3.5's word.** `type E =
  Equatable<S>; struct S :: [Equatable<S>, E]` reports "'E' is not an interface" — but §3.5
  says a transparent alias and its type are interchangeable EVERYWHERE, and here the alias
  appears in a diagnostic where the type serves. Either `Conformance.InterfaceOf` resolves
  through aliases, or §3.5 names the exception. Found by the 3.6.1 sweep; pre-existing.
- **A substituted static method as a VALUE is an IR0001 that names `<?>`.**
  `let f = List<int>.empty;` — the sema accepts, the lowering refuses with "member access
  '.empty' on '<?>'": the right verdict in the wrong words, and static-method values may
  simply deserve a lowering one day. Found by the 3.6.1 sweep; pre-existing.
- **A duplicate CONSTRAINT entry is silently deduplicated.** `<T :: [Ord, Ord]>` compiles;
  the 3.6.0 repetition rule deliberately covers conformance and parent lists only, and a
  doubled constraint claims nothing false — it is redundancy, not a lie. Recorded so the
  asymmetry is a decision, not an oversight; a future spec round may extend §5.1's sentence.
- **A parent list swallows a second INSTANCE of one interface in silence.**
  `interface Both :: [Sink<int>, Sink<string>]` compiles, and only `Sink<int>` arrives: the
  parent walk in `CheckInterfaceParents`/`InterfaceClosure` deduplicates by SYMBOL, so the
  second instance vanishes — `viaString(b)` answers "cannot assign 'B' to 'Sink<string>'" while
  the declaration says otherwise. The declaration-side twin of the 3.6.0 list rules: a TYPE may
  conform at several instances since 3.0, an interface's parent list silently cannot. Probed
  2026-08-25 while building the duplicate-entry check. Either it works (the closure walks
  instances, like conformance does since 3.0) or it is refused — what it must not stay is
  silent.

## Design decisions (context)

- AST = immutable records; symbols = mutable classes; binding and types through side tables
  (Roslyn style).
- Builtins as the root scope; two-pass declaration; structured flow analysis (no CFG).
- Type system rules in `docs/Grammar.md`; **`ErrorType` means exclusively "already reported here"** —
  not "unknown". Checked mechanically.
- Generics: monomorphization. The only option that fits this VM — C# reifies and needs a JIT, Java
  erases and pays with boxing; both presuppose that the runtime knows types, and a Lyric value
  carries no type tag.
- **A value carries no type tag.** Every opcode carries its tag in the instruction stream, and the
  dispatch stays static. From that follows the fat-pointer pattern shared by interfaces, closures and
  coroutines: a reference plus a word in `LyrValue`.
- **IR**: the type fields on the instructions are copies for the printer, the temp table is the
  authority — that the two agree is the core job of the verifier.
- **Total functions over today's type universe throw in the `default`** rather than returning a
  substitute value (`IrType.Equal`, `IrNames.*`, `TypeLowering.Lower`, `IrPrinter.TypeStr`,
  `IrBinKind.FromAst`). The throw names the place to follow up when extending. The exception is
  `IrVerifier.Show` — there a throw would hide the finding. *(A `default` that silently does nothing
  has already desynchronised the instruction stream once: `CodeDecoder.SkipType`.)*
- **`IrShape` is the single source for operands, dest and successors**, **`IrNames` the single one
  for scalar names and mnemonics.** Two copies of those switch blocks would be silently wrong code.
- **Lowering**: statements return "does the control flow fall through?"; values crossing block
  boundaries travel through (possibly synthetic) locals, never through temps — **which is exactly why
  this IR needs no phi**. Block density and `Entry == bb0` are structurally guaranteed in the
  `BlockBuilder` rather than checked.
- **Two error classes in the lowering**: valid Lyric the backend state cannot do → `LYR-IR0001` with
  a position; an internal inconsistency → `InternalCompilationException`. **Deliberately exactly one
  IR code** — codes are stable identifiers, the gaps are temporary. `LYR-IR0002..0010` stay free.
  Likewise: a retired number (`LYR-CLI0007`) is **never** issued again.
- **A `FileId` is an index into ONE `SourceManager`, and a `Span` carries one.** That couples every
  span to the manager it was made in, which is why the compiler builds a fresh manager per run and
  why parsed ASTs cannot be shared between runs however immutable they are. Anything long-lived that
  wants to cache across compiles has to cache the manager with them.
- **Line endings are a test contract, not a taste**: `.gitattributes` forces `eol=lf` in the working
  tree as well, because the goldens compare span offsets. **Do not remove it** — without it 14 golden
  tests fail in every fresh clone and the `windows-latest` job breaks.
- **THE DEVELOPMENT PIPELINE** (maintainer, 2026-08-23, standing from here on). One feature at a
  time, and a feature is not done when it compiles:

  ```
  feature  ->  guide update  ->  feature release
                                      |
                                      v
                         [ bug search & fixes  ->  guide update  ->  patch release ]
                                      |                                    |
                                      +------- as long as bugs are found --+
                                      |
                                      v
                            plan the next feature
  ```

  Two things this settles. The GUIDE is part of the feature, not of the release notes: a feature
  whose chapter still describes the old language is unfinished. And a release is followed by a
  deliberate **hunt**, not by waiting for a bug report — the loop exits only when a sweep finds
  nothing, and only then is the next feature planned.

- **Working mode** (scope check 2026-08-02, still in force): Claude plans *and* implements, the
  maintainer reviews — a deliberate deviation from `CLAUDE.md` §Collaboration, where the plan comes
  from Claude and the code from the user. **`CLAUDE.md` names this entry as the one that overrides
  it**, so the deviation lives in one place and is lifted by deleting this bullet. What to watch is
  whether the understanding of the code keeps up with its size. The changelog starts at `v1.0.0`;
  before it the annotated tag message is the release note.
- **At the end of every milestone the delivery list is to be ticked off point by point, not the exit
  criterion alone.** M5 and M6 each silently failed to deliver part of their items; the gap disguised
  itself as a clean diagnostic. For the same reason **six** gates were re-cut in M7, because they
  required language features of later slices.
- **Interface inheritance is implication-only, and SEVERAL parents are allowed since 2.16.** The
  single-parent rule this entry carried from M22 is gone: it rested on the claim that a parent's
  default method needs its own slot indexes to remain valid behind a child-typed receiver, and the
  claim was false — the dispatch table is keyed by (concrete type, interface) and the lowering
  emits a row per interface in the closure, so nothing is ever remapped. **The lesson is not about
  interfaces**: the entry stood for months, it read as a conclusion, and one probe disproved it. A
  design note that explains a restriction is worth re-testing on the day the restriction starts to
  cost something. Two parents contributing one NAME are refused (`LYR-SEM0079`), a diamond is not.
  Redeclaring a chain member is refused
  instead of getting override semantics: without vtable overriding, the same call would dispatch
  differently through the child and through the parent. A child interface VALUE does not convert to
  the parent's type; implication holds for implementing types. `std.core` adopted
  `Hashable :: [Equatable]` with 2.0, as planned.
- **pub declarations are a library's reachability roots — decided YES, LANDED with 2.0**
  (maintainer, 2026-08-19; built 2026-08-20). Before the rule a module without `main` kept the
  well-known standard library wholesale (measured: 7886 bytes for a one-function library).
  Now a library's `pub` surface decides its contents; the raw lowering API keeps the old
  keep-everything behavior for bare snippets, pinned in ExportRootTests. It waited for 2.0
  because it is observable: a host calling a function the surface does not reach finds it
  missing.
- **Iterator method chaining: DELIVERED in 2.17** (M33), and the entry below is kept because it
  was wrong in an instructive way: it named the two walls as "interface-instance layout work plus
  monomorphized defaults" and called the vtable question open. The real walls were a missing
  instance in three lifting sites and a missing branch before the slot lookup, and the vtable
  question answered itself — a generic member gets no slot at all. The original entry:

- **Iterator method chaining: documented No for now** (M24 probe). `xs.iter().map(f).take(3)`
  wants generic default methods on `Iterator<T>`. The sema ACCEPTS them already; the lowering
  refuses on both paths — an interface VALUE fails at instance interning (`fn(T) -> U` in the
  slot signatures, the same wall as generic interface values over struct arguments), and even
  the monomorphized constraint path has no lowering for a default body with its own type
  parameters. Building both means interface-instance layout work plus monomorphized defaults,
  with an open vtable question (there are no generic slots — such defaults could never be
  overridden). Milestone-sized; the spec documents free adapters as THE form, and an
  `IrPinTests` entry keeps today's refusal visible instead of accidental.
- **THE v3 BASKET AND THE PATCH TRAIN BEFORE IT** (maintainer, 2026-08-22). Everything this
  file has deferred goes into v3.0.0, and OVERLOADING joins it. The order is fixed and the
  reason is that a major should be short: **anything that does not need a major ships FIRST, as
  its own patch** — iterator chaining, the new file-error API, member-`@Deprecated` on interface
  members, the ignored non-interface entries in a `::` list, multiple interface parents. Each of
  those deprecates whatever it replaces in the standard library as it lands, and the removal is
  the major, as at 2.0.
  - **One exception, and it is new for this project**: a form that is replaced ONLY by
    overloading is NOT removed at the major. Overloading arrives WITH v3.0.0, so its
    replacements are the same age as the break; deleting them in the same release would give a
    user no version in which both exist. Those forms carry a KEPT-UNTIL promise through v3.5,
    and the promise is written down at the declaration rather than in a release note.
  - **What must still be decided, before any of it**: `@Deprecated` carries a message and
    nothing else, so a promise "kept until 3.5" has no form. See the next entry.
  - **A design round comes before the major**, overloading included, and it has to answer what
    overloading means for a language whose whole dispatch story is generics plus constraints —
    Rule 2 is at stake, and it is the mechanism Oil died of.

- **`@Deprecated` needs a `until` field before the patch train starts** (open, 2026-08-22). A
  kept-until promise that lives in a release note is a promise nobody can check. The smallest
  form that makes it real: one more field on the existing attribute, and a compiler check that
  a promise whose version has ARRIVED is an error at build time — the ratchet removes the form
  instead of a person remembering to. NOT a second attribute (`@Sunset`, `@Until`): that would
  be a second mechanism for "this is going away".

- **An iterator of OPTIONALS cannot exist, and that is the protocol's price** (2026-08-24, from
  the A24 probing). `Iterator<T>.next()` answers `?T` and uses null to mean the end, so an
  element that is itself optional needs `??T` to be told apart from it — and `?` does not nest.
  `Iterator<?T>` is therefore a type this language cannot write, however ordinary `(?T)[]` is as
  a table with empty slots.
  - **It used to crash instead of saying so**; `LYR-SEM0091` is the sentence, with the loop that
    does work in a note. What Erato asked for (`filterNotNull` on a chain) is on the far side of
    this wall, and `std.iter.compact` (3.4) is the way round it: it takes the ARRAY, so a slot
    reads as `?T` with no end-marker in the way. **It is LAZY**, which the plan had assumed it
    could not be — the wall is the iterator SOURCE, not the element type, and taking an array
    removes the source rather than the optional.
  - Rust pays for the general case with `Option<Option<T>>`. Lyric decided against nesting, and
    this is the bill for that decision arriving — worth re-reading the day the iterator protocol
    is opened for another reason, and not worth opening it for.
  - **What WOULD be worth doing on its own merits**: lower `for (x in someArray)` as an index
    loop rather than through an `ArrayIterator`. It drops an allocation per array loop and makes
    the optional case work as a side effect. `LowerForIn` is built entirely on
    `next()`/`optissome`/`optget`, so it is a parallel path through a hot function — a slice,
    not an afternoon.

- **`unsafe` blocks: documented No, with the measurement** (2026-08-22). What an `unsafe` would
  remove — the bounds check in `ldelem`, the panic in `optget` — is a compare and a branch
  INSIDE a dispatch that costs ~23 cycles. It buys about one percent and pays with the property
  the embedding is sold on: `Capability.None` means nothing if a script may read past an array.
  A C-shaped answer to a dispatch-shaped problem.

- **`@Inline` as a language form: documented No** (2026-08-22). The inliner has a budget since
  M14 and `nextFloat` was inlined without being asked; `nextInt` was not, at 53 instructions.
  Forcing it saves ONE call per 53 dispatches — it cannot move a 555 ns measurement. If the
  budget is wrong that is a measurement and a constant, not a keyword, and it would make the
  second compiler-read attribute out of a mechanism that is supposed to describe and do nothing.

- **THE DESIGN ROUND, DECIDED** (maintainer, 2026-08-22). Three answers, and the third one is a
  deliberate exception rather than a drift, which is why it is written here rather than noticed
  later.

  1. **Heterogeneous arithmetic comes as MULTI-CONFORMANCE, not as overloading** — door B of the
     round. A type may conform to `Mul` twice, and the operator picks the conformance by the
     right-hand type; the implementation for each comes from its own `extend` block, so no type
     declares two `mul`. The machinery is half there already: the conformance dedup has keyed on
     the INSTANCE since M22. It extends the mechanism that exists instead of adding one beside
     it. **v3.0.0.**

     *The SHAPE was asked again once it was built (maintainer, 2026-08-23) and confirmed:*
     `Add<T, R>` stays. The one-parameter spelling `Mul<float>` is nicer to write and is not a
     smaller change but a larger one — it needs a `Self` type in interface signatures, and with
     it an object-safety rule, because behind a fat pointer nobody knows what a `Self`-returning
     member gives back. Two arguments keep the interfaces plain generics: usable as VALUES,
     with any result type (`Mul<Vec2, float>` is a dot product). The price is `Add<Vec2, Vec2>`
     in the homogeneous case, paid once per declaration. Asked before the release rather than
     after, because changing it later would be a SECOND break of the same interfaces.

     *BUILT 2026-08-23, and the round had one thing wrong:* door B alone does not carry it. The
     interfaces are `Mul<T>` with `fn mul(other: T): T` — the result is the OPERAND — so
     `Mul<float, …>` on a `Vec2` demands a `mul` returning `float`, and multi-conformance
     changes nothing about that. The second type argument is not an alternative to door B, it is
     its prerequisite, and it is what makes the change breaking. The old "documented No" below
     had that half right and drew the opposite conclusion from it.

  2. **The JIT stays OPT-IN in 3.0.0.** It changes no language and no format, so it needs no
     major on compatibility grounds; it ships with the major because that is where the maintainer
     wants it. Reversing the default would be the change that needs a major, and that is not this
     release. What must be answered first stands under §What we are working on.

  3. **FREE OVERLOADING — decided for v3.1.0, then moved into v3.0.0** (maintainer, 2026-08-23:
     "dann sind wir mit v3 fertig"). *Built in the major, with the duty below discharged in the
     same change: the rules stand in spec §4.3a and guide 3, not in the implementation.*
     **As a language feature, knowing it softens Rule 2**
     (maintainer, asked and reaffirmed after the objection). The objection, so that nobody has to
     reconstruct it: Lyric then has TWO answers to "one name, several types" — generics plus
     constraints, and overloading — and every later question (which wins, what a constraint sees,
     what inference does) has to be answered twice. That is the shape Oil died of.

     It is a decision, not an oversight, and the reason it is defensible: overloading is a
     CAPABILITY, not a convenience, and the maintainer wants the language to have it. What follows
     from that is a duty rather than a doubt — the second mechanism has to be given its rules
     explicitly, in the spec, at the same time as the feature. A mechanism admitted deliberately
     and specified is a different thing from one that arrives by accident.

     **Measured, so the estimate is not a guess**: the lowering already reads a call's target from
     the TYPE side table 30 times against 7 from the binding, and `TypeResult.BindRef` exists —
     the checker is already the authority for what a call refers to. The resolver would bind a
     CANDIDATE SET, the checker select and record the winner, and seven lowering sites move over.
     "Architectural inversion" is what the old entry implied; three quarters of it has already
     happened.

- **"ONE INTERFACE, ONCE" IS GONE — multi-conformance is general since 3.0** (found 2026-08-23,
  written into the specification with `lyric-spec` PR #18). The design round decided
  heterogeneous arithmetic as multi-conformance and framed the four arithmetic interfaces as the
  single exception to the rule. That framing did not survive the release it shipped in. The rule
  was never a CHECK: before overloading, two conformances wanted two methods of one name on one
  type and `LYR-SEM0042` refused the second, which made a consequence look like an enforced
  rule. Overloading removed that barrier for every interface at once —
  `Tag :: [Equatable<Tag>, Equatable<int>]` compiles, runs, and is pinned by the test the 3.0.1
  sweep left behind. What stays true of `Add`/`Sub`/`Mul`/`Div` is only that theirs is the
  selection an OPERATOR performs; a member call selects by its arguments, an interface value by
  the instance it carries. **The lesson is the single-parent entry's again**: a restriction that
  rests on another mechanism's absence expires the moment that mechanism arrives, silently, with
  no test failing — and this one expired in the same release that added it.

- **Heterogeneous operator arithmetic: documented No** (M22 probe) — *SUPERSEDED, shipped in the
  v3.0.0 basket. Kept for one sentence of it that held and one that did not: a two-parameter
  `Mul<Rhs, Out>` does break every existing conformance (true, and that is the breaking change),
  but it buys only one right-hand type per type only WITHOUT multi-conformance. With it, the two
  halves that were each useless alone are the whole feature.* Two facts cap it below
  usefulness: a type conforms to `Mul` ONCE (`Mul<Vec2>` beside `Mul<float>` fails the signature
  check — one `mul`, two wanted signatures), and Lyric has no overloading, so `mul(other: float)`
  beside `mul(other: Vec2)` cannot exist either. A two-parameter `Mul<Rhs, Out>` would break every
  existing conformance and still buy only ONE right-hand type per type — `Vec2 * float` OR
  `Vec2 * Vec2`, never both. Real heterogeneity needs overloading or multi-conformance with
  signature dispatch; that is a v3-class question, not a 2.0 item.

## Last relevant commit

`dap: an exception-breakpoint request is answered, not refused`
(released as v2.7.1 — found by wiring a second editor to the adapter)

---

## How to maintain this file

- After every slice: extend `## Recently finished`, update `## What we are working on`.
- **At most four entries under `## Recently finished`.** The fifth goes — it stands in `git log`.
  This rule existed already; it was ignored for 1088 lines.
- On a milestone change: enter the new milestone at the top.
- Finished points under `## Still open` are to be **deleted**, not struck through.
- **Never** plan new features here.
