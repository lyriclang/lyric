# Testing

Tests live in a directory of their own — `tests/` beside your `lyric.json`, or wherever its
`testRoot` points — and only `lyric test` ever compiles them. A production build never sees a
test file, so nothing has to be stripped and nothing ships by accident.

A test is a top-level function marked `@Test`, taking nothing and returning nothing. It fails by
panicking, which is what the assertions in `std.test` do:

```lyr
import std.test { Test, assertEq, assertTrue };

fn double(n: int): int {
    return n * 2;
}

@Test
pub fn doubling_doubles(): void {
    assertEq(double(2), 4);
}

@Test
pub fn doubling_keeps_the_sign(): void {
    assertTrue(double(-3) < 0, "the sign must survive");
}
```

```bash
lyric test
```

```
PASS math_tests.doubling_doubles
PASS math_tests.doubling_keeps_the_sign
2 test(s), all passed
```

The runner compiles every `.lyr` under the test root, finds the `@Test` functions through the
module's attribute rows — the same machinery a host uses to find `@System` functions — and runs
each in a **fresh instance**: module state cannot leak between tests, because no two tests share
an instance. A failing assertion names both values (`expected 5, got 4`); a panic fails the test
with its backtrace; the exit code is `0` when everything passed and `1` otherwise, which is all
a CI step needs.

**What a fresh instance does not reset is what a test OPENED.** A file, socket or child process
belongs to the VM, and the runner uses one VM per test FILE — so a test that opens a file and
does not close it holds it until the rest of that file's tests have run, and on Windows the file
stays locked against them. Tests in other files are unaffected. Close what you open, in a `defer`
if the test can fail in between, and a test that means to leave a handle open belongs in a file
of its own.

Test files are ordinary programs of your project: they import your modules through the
`sourceRoot` the `lyric.json` declares, and the whole standard library and every capability are
theirs — a test is your own code running on your own machine, the same standing `lyric run`
has. They run in the debug profile, so a failing test's backtrace names every frame; `lyric test
--release` runs the same tests against the optimized shape, which is how a suite notices an
optimizer that changed an answer.

A project without a `tests/` directory has no tests, and `lyric test` says so and exits `0`. A
`testRoot` your `lyric.json` names explicitly is a promise, though: if the directory is missing,
that is an error.

`lyric new` writes the directory and one test in it, so a project has the shape from the start.

## Running one of them

```bash
lyric test --filter doubling
```

Runs the tests whose `module.function` contains the text — the name a result line shows, so
narrowing the next run down to a failure is copying part of the line it printed. A filter that
matches nothing is an **error**, not a green run of nothing: a mistyped filter would otherwise
report success having tested not one thing.

`lyric check` is the other half of the same loop: it compiles the test root together with the
source root without running anything, so a test that no longer matches the function it tests is
named in a second rather than after a test run.

`assertEq` wants `[Equatable<T>, Display]` — the comparison needs the one, naming both values in
the failure needs the other. Every built-in scalar and `string` satisfy both; for your own types
you declare the conformances, as [chapter 7](07-interfaces.md) shows.
