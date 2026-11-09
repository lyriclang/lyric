# Coming from Lyric 4

Lyric 5 is a new language with an old name. Lyric 4 ran on a virtual machine on .NET; Lyric 5
compiles through C to a native binary, with a runtime of its own, a collector, and tasks on
stacks of their own. The surface kept much of what worked, and changed what the design round
found wrong. This chapter is for a reader who knows Lyric 4: what to unlearn, and why.

Much stays as it was: `?T` and `x!`, `throws T` and `catch (e: T)`, `Coroutine<T>` with
`next()`, `List<int>.of`, `push`, interfaces with `::`, `@Test` and `assertEq`.

## The first hour

**Imports name the module, not a submodule.** `println` is `std.io`'s:

```text
import std.io.console { println };     // Lyric 4
```

```lyric
import std.io { println };

fn main(): void {
    println("hello");
}
```

```output
hello
```

**No module header.** A file's path names its module; `module app.geometry;` is gone. A bare
`module;` remains only as a place for attributes that concern the whole module.

**`main` takes no arguments and may throw.** The command line is `std.os.args()`, and an error
that leaves `main` ends the program with its message and the exit code 1 (a panic ends it with
101):

```lyric,no_run
import std.io { println };
import std.os { args };

fn main(): int throws Error {
    let given = args();
    println(f"{given.length() - 1} arguments");
    return 0;
}
```

**The manifest is `lyric.toml`**, read and never run, where Lyric 4 had `lyric.json`.

## Values

**A field is written only where it says `var`.** In Lyric 4 every field was writable. Now `var`
means the same in a field as in a local — it may change:

```lyric
import std.io { println };

struct Counter {
    var count: int = 0,
    name: string,
}

fn main(): void {
    var c = Counter { name = "clicks" };
    c.count += 1;
    println(f"{c.name}: {c.count}");
}
```

```output
clicks: 1
```

**Overflow panics**, in every profile, where Lyric 4 wrapped silently. A wrap you want is
spelled: `+%`, `-%`, `*%`. **`int` is `int64`**, one type with two names; Lyric 4 kept them apart.

**A string's `length()` is its bytes**, in constant time; `charCount()` counts characters.
`length()` takes parentheses on everything — arrays too, which had a field `length` in Lyric 4.

**Numbers become text through an f-string**, `f"{n}"`, and are formatted there with one small
language of its own — `{pi:.2f}` instead of .NET's `{pi:N2}`:

```lyric
import std.io { println };

fn main(): void {
    let pi = 3.14159;
    let n = 42;
    println(f"{pi:.2f} {n} {n:>5}|");
}
```

```output
3.14 42    42|
```

**A variant in a pattern has a dot**: `.Red =>`. A bare name in a pattern always binds a new
variable, so a misspelled variant can no longer turn into a catch-all. **Varargs** are
`values: int...`, and an array is spread into them as `f(xs...)`. String escapes are `\u{…}`;
`\xNN` stays for byte strings.

## Errors

**Every call that can throw is marked `try`.** Lyric 4 let throwing calls stand unmarked; now the
reader sees each place an error can come from, and the compiler refuses the call without it. The
twin functions `text` and `textOrThrow` are one function again: it throws, and `try?` makes an
absent value of the error where that is what you want:

```lyric
import std.io { println };

fn main(): void {
    let n = try? int.parse("twelve");
    println(n == null);
}
```

```output
true
```

The root of what is thrown is `Error`, where Lyric 4 had `Throwable`. A function may name several:
`throws [ParseError, IoError]`, and `catch (e in [A, B])` takes either.

## Tasks

Lyric 4 ran coroutines of `Coroutine<Wait>` under a `run()` loop. In Lyric 5, `spawn` starts a
task and returns its handle — `Task<T> throws E` —, `await()` gives its result, and `main` is
itself a task: there is no loop to start. A task may wait anywhere, in any function, without that
function being marked. A coroutine is still a `Coroutine<T>`, resumed with `next()`; `resume` is
gone. [Tasks and coroutines](tasks-and-coroutines.md) has the whole story.

## Visibility

A declaration without `pub` is **internal**: seen in the whole package, not only in its own
module as in Lyric 4. `private` keeps it to its module.

## What is gone

- **The virtual machine and its tools.** There is no bytecode file and no `lyrc`, `lyrvm` and the
  others beside it: one `lyric` command with its verbs builds, runs, tests, formats and serves the
  editor.
- **`extern "dotnet"`.** C is the one foreign language (`extern "C"`; see [C
  libraries](c-libraries.md)).
- **Capabilities and `--grant`.** A native program has the rights its process has; a sandbox is
  the operating system's job, or the job of the embedding host.
- **Hashing by `hash(): int`.** A type hashes by feeding a `Hasher` (`hash(&h: Hasher)`), and a
  `Map` is SipHash — which also means its order differs from run to run. Never rely on it.

## Moving a program

`lyric fix --from-4` rewrites what can be rewritten mechanically — imports, the module header,
fields that are written, `try` marks, patterns — and leaves a note for what needs a person: a
place where wrapping arithmetic was relied on, a string indexed by character. The result is a
start, not a promise; build it and read what `lyric check` says next. [What the compiler tells
you](diagnostics.md) explains how.

This is the Book's last chapter. From here, the standard library's reference has every module —
on the website, or for a package of your own from `lyric doc` — and the specification every rule.
