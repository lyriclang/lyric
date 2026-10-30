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

The C compiler is `zig`, version 0.16.0. `lyric5 toolchain install zig` fetches it from zig's
mirrors, checks zig's signature and puts it under `~/.lyric`, where `lyric` looks before your
`PATH`; a zig, clang or gcc on the `PATH` works too, and `LYRIC_CC` names one over both.
`lyric5 --version` says which one it found:

```text
$ lyric5 --version
lyric 5.0.0-dev.20261023+6d7bd38
  edition     5
  c compiler  zig 0.16.0 (/usr/bin/zig)
```

The runtime comes prebuilt for your system. A build with another C compiler than zig 0.16.0, for another
target, or with another collector or a sanitizer builds it from its source first, once; that build
takes a little longer than the ones after it.

## Several toolchains

`lyric toolchain` keeps toolchains side by side under `~/.lyric` (`LYRIC_HOME` puts them elsewhere)
and checks each before it is in place: the channel's list is signed, and every archive is checked
against it. `~/.lyric/bin` on your `PATH` runs the default:

```sh
lyric toolchain install stable     # or dev, or a version such as 5.0.0
lyric toolchain list               # the installed ones; --available, what the channels offer
lyric toolchain default 5.0.0
lyric toolchain update             # the newest of the default's channel, beside it
```

A package can name the least toolchain it needs, in `lyric.toml`:

```toml
[package]
name = "hello"
version = "0.1.0"
toolchain = "5.1"
```

A command in that package then runs with an installed toolchain that is 5.1 or later — the default
where it is — whichever one you started; at a terminal, one is offered for installing where none
is. `LYRIC_TOOLCHAIN=local` runs the one you started, and `LYRIC_TOOLCHAIN=5.1.0` a version.

The channels are published from the first signed release on; until then `lyric toolchain` installs
only from a mirror of your own, which `LYRIC_MIRROR` names with its key in `LYRIC_MIRROR_KEY`.

`lyric env` says what a build here reads and where each value comes from — the C compiler and zig
(`installed` or `PATH`), the profile, and each lint's level with who set it (`-D`, `[lints]`).

## Coming from Lyric 4

`lyric fix --from-4` migrates a Lyric 4 program: run it in the directory of its `lyric.json`, or name one
file. It reads the program with Lyric 4.6's own checker, so the program has to check with 4.6 first, then
rewrites the files in place — the module headers go, the imports name Lyric 5's modules, `lyric.json`
becomes `lyric.toml` — and lists in `fix-from-4.txt` what it leaves to you. `--dry-run` shows the changes
as a diff first. Then `lyric check` says what is left.

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
   Compiling hello v0.1.0 (src/main.lyr)
   Compiling 3 of 3 C units of hello
     Linking hello (out/debug/x86_64-linux-gnu/hello)
    Finished debug [opt 0, debug info] in 1.12s
     Running out/debug/x86_64-linux-gnu/hello
Hello, world!
```

The lines before the greeting are the build's, on the standard error: each step that does work
says what it works on, and the last how long it all took. Run it again and nothing has changed —
only `Finished` and `Running` are said. The first build of a profile also compiles the runtime,
once for every program on the machine; `-q` says none of these lines, only the findings.

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

## Debugging

A debug build carries debug information, and its C names a field and a local as the program
does, so a debugger shows `p.x`, not a name the compiler made. `lyric dap` is the debug adapter
an editor starts: it builds the program and hands it to LLVM's `lldb-dap`, which you install
beside the toolchain (`LYRIC_LLDB_DAP` names one that is not on the `PATH`); a panic stops the
program where it happens. In plain lldb, `command source lldb/lyric.lldbinit` from the
toolchain's directory shows Lyric's strings, arrays and enums.

On Windows `lyric dap` is measured, not promised: LLVM 20's `lldb-dap.exe` needs the Python its
lldb was built with (`python310.dll`) and, on the machines measured, ends as it starts even
then. The program carries a PDB there, which Visual Studio's debugger reads.

Next, [a first program](first-program.md) that does a little more than greet.
