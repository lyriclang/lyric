# Getting started

This chapter installs the toolchain, makes a package and runs it. Lyric compiles a program through
C into one native binary, so besides the toolchain you need a C compiler.

## Installing

Lyric 5 is in development; nothing is released yet. Every change to `main` is built into the `dev`
prerelease on GitHub: <https://github.com/lyriclang/lyric/releases/tag/dev>. Take the archive for
your system — Linux (x64 or ARM64), macOS (ARM64 or x64) or Windows (x64) — and unpack it where you keep
tools:

```sh
mkdir -p ~/.local/lyric
tar -xzf lyric-5.0.0-dev.*-linux-x64.tar.gz -C ~/.local/lyric
export PATH="$HOME/.local/lyric:$PATH"
```

The archive holds the command `lyric5`, the standard library and the runtime's C source. Until the
first release the command is called `lyric5`; this Book writes `lyric`, and every `lyric` here is
`lyric5` for now.

The C compiler is `zig`, version 0.16.0, from <https://ziglang.org/download/>, on your `PATH`;
clang or gcc work too. `lyric5 --version` says which one it found:

```text
$ lyric5 --version
lyric 5.0.0-dev.20261023+6d7bd38
  edition     5
  c compiler  zig 0.16.0 (/usr/bin/zig)
```

The runtime comes prebuilt for your system. A build with another C compiler than zig 0.16.0, for another
target, or with another collector or a sanitizer builds it from its source first, once; that build
takes a little longer than the ones after it.

## A package

A program lives in a **package**: a directory with a manifest, `lyric.toml`, and its source under
`src/`. `lyric new` makes one:

```text
$ lyric new hello
created program package 'hello' in /home/you/hello
```

The manifest names the package and its version:

```toml
[package]
name = "hello"
version = "0.1.0"
```

and `src/main.lyr` is the program:

```lyric
import std.io { println };

fn main(): void {
    println("Hello, world!");
}
```

```output
Hello, world!
```

A program starts at `main`. `println` comes from the standard library's module `std.io`, and the
`import` at the top names what the file takes from it. A statement ends with `;`.

## Building and running

In the package's directory, `lyric run` builds the program and runs it:

```text
$ cd hello
$ lyric run
Hello, world!
```

`lyric build` builds without running; the binary lands under `out/`, by profile and target —
`out/debug/x86_64-linux-gnu/hello` on Linux. The default profile is `debug`: quick to build, with
checks and debug information. `--profile release` builds the optimized program:

```text
$ lyric build --profile release
$ ./out/release/x86_64-linux-gnu/hello
Hello, world!
```

`lyric check` says what a build would say about the program, without making a binary — the
quickest way to hear about a mistake. The chapter [What the compiler tells you](diagnostics.md)
reads what it says.

Next, [a first program](first-program.md) that does a little more than greet.
