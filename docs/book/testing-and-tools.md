# Testing and tools

A program that works today should still work after the next change. This chapter is about the
tests that make sure of it, and the other tools `lyric` brings along: the formatter, the fixer, the
documentation, the editor support.

## Tests

A test is a function marked `@Test`: no parameters, returning nothing, allowed to throw. It
checks with the assertions of `std.test`, and fails when one does not hold:

```lyric
import std.test { assertEq, assertTrue };

fn discounted(cents: int, percent: int): int {
    return cents - cents * percent / 100;
}

@Test
fn aFifthOffAHundred() {
    assertEq(discounted(10000, 20), 8000);
}

@Test
fn nothingOffIsTheSame() {
    let price = 999;
    assertEq(discounted(price, 0), price);
    assertTrue(discounted(price, 100) == 0);
}
```

Tests can stand beside the code they test, in a module under `src/`, or in modules of their own
under `tests/`. A module under `tests/` is part of the package: it imports the package's modules by
their names, and sees what is not private. A package `prices` with its code in `src/discount.lyr`
has its tests in, say, `tests/discount.lyr`:

```text
prices/
├── lyric.toml
├── src/
│   ├── discount.lyr      fn discounted(…)
│   └── main.lyr
└── tests/
    └── discount.lyr      import prices.discount { discounted };
```

`lyric test` builds every `@Test` of the package into one program and runs it. Each test runs in a
task of its own, so one that panics fails and the others still run:

```sh
lyric test
```

```text
ok    prices.tests.discount.aFifthOffAHundred
ok    prices.tests.discount.nothingOff
FAIL  prices.tests.discount.halfOfAnOddPrice
      panic [LYR-RT0008]: discounted(999, 50) = 500, expected 499

3 tests: 2 passed, 1 failed, 0 skipped
```

A failed assertion says what it got, from which expression, and what it expected — the actual
value first — and a backtrace below it says where. The exit code is 1 when a test failed, so a CI
job fails with it. `lyric test -f discount` runs only the tests whose name holds `discount`.

The other assertions follow the same pattern: `assertNotEq`, `assertLess`, `assertContains`,
`assertClose` for floats, `assertNull`. `assertThrows` runs a function and expects an error of a
type, which it returns for a closer look:

```lyric
import std.core { ParseError };
import std.test { assertThrows, assertTrue };

@Test
fn aWordIsNoNumber() {
    let e = assertThrows<ParseError>(() => {
        let _ = try int.parse("seven");
    });
    assertTrue(e.message().length() > 0);
}
```

A test the attribute gives fields to can be skipped with a reason, given a time limit in seconds,
or run beside the other parallel ones: `@Test { skip = "needs a network" }`,
`@Test { timeout = 10 }`, `@Test { parallel = true }`. Parallel is a choice, not the default:
tests share the working directory, the environment and the globals, as threads do.

## Examples in documentation are tests

A ` ```lyric ` block in a documentation comment is a test too. `lyric test` builds and runs it,
and compares what it prints with the ` ```output ` block below it — the same rules as this Book's
snippets. Its line names the element it documents, `ok    doc: prices.discount.discounted
(src/discount.lyr:3)`, so an example in the documentation cannot go stale unnoticed:

```lyric
/// The price with the discount taken off, in cents.
///
/// ```lyric
/// import std.io { println };
///
/// println(discounted(1000, 15));
/// ```
///
/// ```output
/// 850
/// ```
pub fn discounted(cents: int, percent: int): int {
    return cents - cents * percent / 100;
}
```

## Benchmarks

A function marked `@Bench` takes a `Bench` and gives it the code to time. `lyric bench` builds the
benchmarks in the release profile and runs each many times over:

```lyric
@Bench
fn sumAThousand(&b: Bench): void {
    b.iter {
        var total = 0;
        for (i in 0..1000) {
            total += i;
        }
    }
}
```

Each prints its nanoseconds and allocations per run. `--save base.txt` keeps the numbers and
`--compare base.txt` shows the change against them — run before and after an optimization.

## The formatter

`lyric fmt` puts the package's files into the one form: indentation, spaces, where lines break.
There are no settings, so every Lyric program looks the same and a diff shows only what changed.
`lyric fmt --check` changes nothing and fails where a file is not in form — the line for a CI job.

## Check, fix, explain

`lyric check` says everything `build` would say, without making a binary — fast enough to run on
every save. Many findings come with a suggested change, shown as `help:` with the fixed line;
`lyric fix` applies the ones that are safe and says what is left. Each finding has a code, and the
[diagnostics chapter](diagnostics.md) shows how to read them and where each code is explained.

Lints are warnings about code that works but is likely not what was meant — an unused import, a
string grown with `+` in a loop. They are set in `lyric.toml`, per package:

```toml
[lints]
deny = ["unused-import"]
allow = ["string-concat-in-loop"]
```

and one place in the code can allow a lint with an attribute, `@Allow("unused-variable")`, on the
declaration it concerns.

## Documentation

`lyric doc` writes the package's reference under `out/doc/`: every `pub` module, type and function
with its documentation comment, linked to each other — the standard library's reference is made
the same way. `lyric api` prints the public surface, one line per element; `lyric api --diff v1.0.0`
shows what changed against a git tag, which is how a library sees a breaking change before its
users do.

## In the editor

`lyric lsp` is a language server: an editor that speaks LSP gets the diagnostics as you type, go
to definition, hover with the type and the documentation, completion, rename, and the fixes of
`lyric fix` as code actions. `lyric dap` is a debug adapter with lldb behind it: breakpoints in
Lyric source, stepping, and the values of variables. The extensions for VS Code and the JetBrains
IDEs start both.

The next chapter is about what the compiler says when a program is not right yet: [What the
compiler tells you](diagnostics.md).
