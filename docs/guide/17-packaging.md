# Packing a program

A Lyric program normally travels as a `.lyrbc` module and runs wherever a `lyrvm` is. Packing
removes that condition: one command wraps your program and the runtime into a single executable
that runs on a machine that has never heard of Lyric.

```bash
lyric pack app.lyr
./app arg1 arg2
```

That is the whole workflow. `lyric pack` compiles the file in the release profile ([chapter
16](16-building.md#two-profiles); `--debug` if you want the other one) and hands the module to
`lyrpack`, which copies a prebuilt runtime — the *stub* — and appends the module to it. No
compiler ships inside the result, nothing is installed on the target machine, and packing takes
about as long as copying the file, because that is what it is.

In a project you name nothing at all: `lyric pack` builds the default artifact in the release
profile and packs it beside the module, as `out/release/app` (with the platform's suffix).
`lyric pack mktex` packs the artifact of that name.

An already compiled module packs directly too:

```bash
lyric build
lyric pack out/debug/app.lyrbc -o dist/app
```

A `build.lyr` can also declare the packed form outright, with `packed(app)`, which is how a
project that always ships one gets it from a plain `lyric build`. See [chapter
16](16-building.md#packing-from-the-script).

## What a packed program is

It is your program, owning the whole command line. Every argument goes to `main` unchanged —
there is no `--` protocol and no wrapper option, not even `--help`. If your program wants a
help text, it prints one:

```lyr
import std.io.console;

fn main(args: string[]): int {
    if (args.length == 0) {
        console.println("usage: app <name>");
        return 2;
    }

    for (name in args) {
        console.println("hello, " + name);
    }
    return 0;
}
```

The exit code is `main`'s return value, a panic exits with 101 and prints its backtrace to
stderr, and the source map travels inside the module, so the backtrace names your lines — the
program behaves exactly as it did under `lyric run`, because it is the same runtime executing
the same bytes.

A packed program runs with **every capability**, like any standalone program. The sandbox
belongs to hosts that embed the runtime; a program someone ships as an executable answers to
the operating system, the way every executable does.

## What to know before shipping

- **One platform per file.** The stub is a native executable, so a pack is for one platform.
  The toolchain archive carries stubs under `stubs/<rid>/`, and `--stub` points at any of them
  explicitly.
- **Size.** The stub contains the whole runtime, so even `return 0;` is megabytes. The program
  on top costs what the module costs — usually kilobytes.
- **macOS packs on macOS.** A Mach-O declares its own size, so the payload cannot simply follow
  it: the packer folds it into the `__LINKEDIT` segment and signs the result ad-hoc through
  `codesign`, which macOS ships. That signature is the half that can only be made there, so
  packing a macOS program on another system is refused with that reason rather than producing a
  file the loader would kill. Packing ON macOS FOR another platform works as everywhere, with
  that platform's stub and `--stub`.

  An ad-hoc signature says the file has not changed since packing, not who made it — which is
  what the loader asks for. Distributing to other people's machines is a question of notarisation,
  on the finished file, with your own identity.
- **Packing is not verification.** `lyrpack` copies bytes and does not look inside the module;
  a broken module packs fine and reports at first start. `lyrvm verify` answers ahead of time.

The byte-level layout — the footer, its versioning, and what the stub does in which failure —
is specified in [Pack.md](../Pack.md).
