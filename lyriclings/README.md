# lyriclings

Small exercises to get you used to reading and writing Lyric — in the spirit of
[rustlings](https://github.com/rust-lang/rustlings) and
[ziglings](https://codeberg.org/ziglings/exercises). Every exercise is a `.lyr` file that
does not compile, panics, or prints the wrong thing. You read the comment at the top, fix the
file, and a runner checks it.

Written for **Lyric 4.4.1**. 90 exercises in 21 chapters, following the order of the
[language guide](../docs/guide/).

## Installation

You need the Lyric toolchain. Either a released archive, or a build of this repository:

```bash
dotnet msbuild build/publish.proj          # from the repository root
export LYRIC_CLI=$PWD/artifacts/publish/lyric
```

The runner finds the compiler through the environment variable `LYRIC_CLI`, or as `lyric` on
the `PATH`. (On WSL without a Linux .NET on the path, set `DOTNET_ROOT` as well, e.g. to
`~/.dotnet`.)

Then:

```bash
cd lyriclings
lyric run lyriclings.lyr -- watch
```

## Usage

The runner is itself a Lyric program, `lyriclings.lyr`. Run it from the `lyriclings`
directory (it reads `exercises.json` from there):

| Command | What it does |
|---|---|
| `lyric run lyriclings.lyr -- watch` | shows the next unsolved exercise and re-runs it every time you save the file |
| `lyric run lyriclings.lyr -- next` | the same, once |
| `lyric run lyriclings.lyr -- run <name>` | compiles and runs one exercise and shows everything it prints |
| `lyric run lyriclings.lyr -- hint <name>` | the hint for one exercise |
| `lyric run lyriclings.lyr -- list` | every exercise, by chapter, and whether you have touched it |
| `lyric run lyriclings.lyr -- verify` | every exercise in order; stops at the first one that is not done |
| `lyric run lyriclings.lyr -- verify --solutions` | the same over `solutions/` — must be all green |
| `lyric run lyriclings.lyr -- audit` | for maintainers: every shipped exercise fails the way `exercises.json` says, every solution passes |

Every exercise starts with the line

```
// I AM NOT DONE
```

Fix the file, then delete that line: that is how the runner knows you consider the exercise
finished. It then compiles and runs the file and compares the output with what the exercise
expects. An exercise is done when the program compiles, exits with `0` and prints the
expected output — which is written at the top of every file.

If you are stuck, `hint <name>` tells you what to look for; `solutions/` holds one working
answer per exercise (same file name). Chapter 18 has multi-file exercises: the module files
next to the exercise (`geometry.lyr`, `config.lyr`, `shapes/disc.lyr`) belong to it, and the
fix is sometimes in one of them.

## Chapters

| # | Chapter | Exercises | What you learn |
|---|---|---|---|
| 01 | hello | 2 | `main`, exit codes, importing `println` |
| 02 | variables | 3 | `let` vs `var`, definite assignment, `null` needs a type |
| 03 | numbers | 5 | distinct numeric types, literal adaptation, `as`, wrapping arithmetic, division by zero panics |
| 04 | strings | 4 | `+` and f-strings, no string indexing, string methods via `std.string`, format specifiers |
| 05 | functions | 4 | defaults, overloading by parameters, lambda annotations, `params` |
| 06 | control flow | 5 | `if` as expression, `bool` conditions, ranges are loop heads, `break`/`continue`, `do-while` and definite assignment |
| 07 | arrays and tuples | 4 | one element type, bounds panics, tuple destructuring, arrays are references |
| 08 | structs | 3 | fields and initializers, `mut fn`, value semantics |
| 09 | classes | 2 | reference semantics, a struct cannot contain itself |
| 10 | enums | 4 | payloads, variant shapes, no `==` without `Equatable`, generic enums |
| 11 | pattern matching | 6 | exhaustiveness, guards, literal/or/range patterns, block arms, `?Enum`, struct destructuring |
| 12 | optionals | 6 | `?T`, narrowing (locals only), `!`, `??`, `??=`, `&&` narrowing, no `for` over `(?T)[]` |
| 13 | interfaces | 7 | conformance, exact signatures, `Equatable`, `Ordered`, `Add`, `Indexable`, defaults |
| 14 | generics | 4 | constraints, constraint checks at the call, explicit type arguments |
| 15 | extend | 3 | extending built-ins, conformance through `extend`, static extensions |
| 16 | errors | 5 | `throws` and `try`/`catch`, `Throwable`, `defer` instead of `finally`, typed catch coverage, panic vs exception |
| 17 | coroutines | 3 | `yield`/`resume`, `next()`, throwing coroutines |
| 18 | modules | 5 | `pub`, module paths, headers, import forms, constant order |
| 19 | attributes | 2 | marker interfaces, `@Deprecated` with `until` |
| 20 | standard library | 9 | `parseInt`, `List`, `Map`, `Set` and `Hashable`, `std.iter`, `std.option`, `std.json`, `std.io.file`, `std.test` |
| 21 | quiz | 4 | word count, shapes, a bank with exceptions, an iterable stack |

## How an exercise is checked

`exercises.json` lists every exercise with its path, chapter, hint, the exact output the
finished program prints (`expect`) and the diagnostic the shipped, broken file produces
(`broken`: a code such as `LYR-SEM0019`, `panic:LYR-VM0006`, or `output` for a program that
runs but prints the wrong thing). The runner compiles each file with `lyric run`; exit code
`0` means it ran, `1` a diagnostic, `101` a panic.

No `lyric.json` is needed: every exercise is a single entry file, and the module files of
chapter 18 are resolved relative to it, which is Lyric's default.

## Why the runner is a Lyric program

It exercises exactly what the exercises teach — `std.process` to start the compiler inside a
task, `std.json` for the exercise list, `std.io.file` for the sources — and it needs nothing
but the toolchain. Its one limitation: `watch` polls the file's contents every half second,
because the standard library has no file-change notification.
