# Project: a command-line tool

This chapter builds a whole program from the parts of the chapters before: `logfilter`, a small
grep for log files. It prints the lines of a file that a regular expression matches, and takes a
few options:

```text
logfilter [-v] [-c] [-n] [--color | --no-color] <pattern> <file>
```

`-v` prints the lines that do *not* match, `-c` only their count, `-n` each line with its number.
Matches are colored on a terminal. Like grep, it ends with 0 when it printed a line, 1 when there
was none, and 2 when it was called wrong.

The finished program is [`examples/logfilter`](../../examples/logfilter/src/main.lyr); the test
suite builds it and runs it over a log. Here it grows in steps, each a program of its own.

## The package

`lyric new` makes the directory with a manifest and a `main`:

```sh
lyric new logfilter
cd logfilter
```

```toml
[package]
name = "logfilter"
version = "0.1.0"
```

The program is `src/main.lyr`; `lyric run -- <args>` builds it and passes it what follows `--`.

## The arguments

`std.os.args()` gives the command line, the program's own name first. Taking it apart is a plain
loop — and a function of its own, so it can be tried with any arguments, not only the real ones:

```lyric
import std.io { println };

struct Options {
    var invert: bool = false,
    var countOnly: bool = false,
    var numbered: bool = false,
    var pattern: string = "",
    var file: string = "",
}

/// The options of a command line, or null where it is not one the tool takes.
fn parse(given: string[]): ?Options {
    var o = Options { };
    var rest: List<string> = List.new();
    for (a in given[1..]) {
        if (a == "-v") {
            o.invert = true;
        } else if (a == "-c") {
            o.countOnly = true;
        } else if (a == "-n") {
            o.numbered = true;
        } else if (a.startsWith("-") && a.length() > 1) {
            return null;
        } else {
            rest.push(a);
        }
    }
    if (rest.length() != 2) {
        return null;
    }
    o.pattern = rest[0];
    o.file = rest[1];
    return o;
}

fn main(): void {
    if (let o = parse(["logfilter", "-n", "ERROR", "server.log"])) {
        println(f"{o.pattern} in {o.file}, numbered: {o.numbered}");
    }
    println(parse(["logfilter", "-x", "ERROR", "server.log"]) == null);
}
```

```output
ERROR in server.log, numbered: true
true
```

The fields have defaults, so `Options { }` is the tool called with no flags; they are `var`, so the
loop can set them. `null` stands for "not
a command line I take"; `main` turns it into the usage message and the exit code 2.

## Matching

`std.regex` has the regular expressions. `Regex.new` compiles a pattern and throws a `RegexError`
for one it cannot take — the user typed it, so that is an error to report, not a bug:

```lyric
import std.io { println };
import std.regex { Regex, RegexError };

fn main(): void throws Error {
    let lines = [
        "08:00:01 INFO  server started",
        "08:00:07 ERROR db: connection refused",
        "08:00:11 WARN  slow request 1203ms",
    ];
    let re = try Regex.new("ERROR|WARN");
    for (line in lines) {
        if (re.isMatch(line)) {
            println(line);
        }
    }

    try {
        let _ = Regex.new("(ERROR");
    } catch (e: RegexError) {
        println(f"logfilter: {e.message()}");
    }
}
```

```output
08:00:07 ERROR db: connection refused
08:00:11 WARN  slow request 1203ms
logfilter: the group is not closed at offset 0
```

The syntax is RE2's: no backreferences, and in exchange no pattern can make a match take
exponential time — a property worth having in a tool that takes patterns from anyone.

## Colors

On a terminal, each match is shown in bold red. `std.term` has the styles; `force` wraps a text in
one, and `findAll` gives the matches with where they start and end:

```lyric
import std.core { StringBuilder };
import std.io { println };
import std.regex { Regex };
import std.term { Style, Color };

fn highlighted(re: Regex, line: string, style: Style): string {
    let out = StringBuilder.new();
    var last = 0;
    var it = re.findAll(line);
    let bytes = line.bytes();
    while (let m = it.next()) {
        out.appendStr(string.fromUtf8Lossy(bytes[last..m.start]));
        out.appendStr(style.force(m.value));
        last = m.end;
    }
    out.appendStr(string.fromUtf8Lossy(bytes[last..bytes.length()]));
    return out.toString();
}

fn main(): void throws Error {
    let re = try Regex.new("ERROR");
    let shown = highlighted(re, "db ERROR here", Style { fg = Color.Red, bold = true });
    println(shown.replace("\u{1b}", "ESC"));
}
```

```output
db ESC[1;31mERRORESC[0m here
```

The positions are byte offsets, so the line is cut as bytes and put back together as text. The
pieces go into a `StringBuilder`: growing a string with `+` in a loop copies all of it each round,
and the compiler warns about it. Whether
to color at all is `colorsEnabled()`: true on a terminal, false into a pipe or a file, and the
`NO_COLOR` and `FORCE_COLOR` variables have their say. `--color` and `--no-color` override it.

## Putting it together

`main` returns an `int`: that is the exit code. It may also throw — a file that cannot be read
ends the program with the error's message and the code 1:

```lyric,no_run
import std.fs { readText };
import std.io { eprintln, println };
import std.os { args };
import std.regex { Regex };

fn main(): int throws Error {
    let given = args();
    if (given.length() != 3) {
        eprintln("usage: logfilter <pattern> <file>");
        return 2;
    }
    let re = try Regex.new(given[1]);
    let text = try readText(given[2]);
    var selected = 0;
    for (line in text.lines()) {
        if (re.isMatch(line)) {
            selected += 1;
            println(line);
        }
    }
    return if (selected > 0) 0 else 1;
}
```

This is the core of the tool; the finished one adds the options, the numbers and the colors, and
catches a `RegexError` to report it with the code 2 instead of 1. Run over a log:

```sh
lyric run -- -n "ERROR|WARN" server.log
```

```text
2:2026-10-14T08:00:05Z WARN  slow request GET /api/users 1203ms
3:2026-10-14T08:00:07Z ERROR db: connection refused (attempt 3)
5:2026-10-14T08:00:11Z ERROR db: timeout after 5000ms
```

`lyric build --profile release` makes the binary for good, under `out/release/<target>/`: one file,
with nothing to install beside it.

## What it used

Options in a struct with defaults; an optional for "not valid"; a loop over a slice; `std.regex`
and its error; byte offsets and text; `main`'s return value as the exit code and its `throws` for
the failures a user can cause. The next chapter turns to programs that wait for many things at once:
[tasks and coroutines](tasks-and-coroutines.md).
