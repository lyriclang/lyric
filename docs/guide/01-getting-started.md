# Getting started

A Lyric program is a file with the extension `.lyr`. Execution begins at `main`.

```lyr
import std.io.console { println };

fn main(): int {
    println("Hello, Lyric!");
    return 0;
}
```

Run it:

```bash
lyric run hello.lyr
```

The return value of `main` becomes the process exit code, masked with `& 0xFF`.

## The tools

| Command | Purpose |
|---|---|
| `lyric run <file>` | compile and execute |
| `lyric check <file>` | compile without writing a file |
| `lyric build <file> -o <out>` | compile to `.lyrbc` |
| `lyric disasm <file>` | print the bytecode |
| `lyric repl` | interactive prompt |

`lyric check` goes as far as the intermediate representation, so a construct the code generator
cannot lower is reported there rather than at the build. It stops before the bytes: a module can
pass everything above and still be one the loader refuses. `lyric check <file> --emit` runs that
last step too — the bytes are produced, loaded and dropped, and no file is written. It is the
answer for a project that compiles each of its files on its own and wants to know they will all
load.

## Arguments

To receive command-line arguments, declare `main` with one parameter of type `string[]`:

```lyr
import std.io.console { println };

fn main(args: string[]): int {
    println(f"got {args.length} argument(s)");
    return 0;
}
```

Everything after `--` on the command line belongs to the program:

```bash
lyric run wc.lyr -- README.md
```
