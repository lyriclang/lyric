# Building a project

## Starting one

```bash
lyric new myapp          # a program
lyric new mylib --lib    # a module someone imports
```

An app arrives ready to build:

```
myapp/
├── lyric.json      where the modules live
├── build.lyr       what to build
├── .gitignore
└── src/main.lyr
```

```bash
cd myapp && lyric build && lyric run out/myapp.lyrbc
```

A library has no `build.lyr`, because there is nothing to build: it is source another project points
its `sourceRoot` at and imports. Its module file is named after it, so `import mylib` finds
`src/mylib.lyr`.

The name becomes a module name, so it has to be one: letters, digits and `_`, not starting with a
digit. `lyric new` refuses to write into a directory that already holds something.

## Building one

For a single file there is nothing to set up:

```bash
lyric build app.lyr -o app.lyrbc
```

A project with more than one program, or one that generates part of its own source, puts a
`build.lyr` at its root and runs `lyric build` without naming a file.

## The script

```lyr
import std.build { addExecutable };

pub fn build() {
    let app = addExecutable("src/main.lyr", "out/app.lyrbc");
    app.sourceMap(false);

    addExecutable("tools/mktex.lyr", "out/mktex.lyrbc");
}
```

```bash
lyric build          # the working directory
lyric build ../game  # somewhere else
```

Every artifact is compiled on its own, whole, from its entry file. There is no link step and nothing
is shared between two of them but the source on disk.

`lyric build` with a **file** still means "compile this file" and goes to the compiler, as it always
did. Only a directory, or no argument at all, looks for a `build.lyr`.

## Nothing is compiled while the script runs

`addExecutable` collects; the compiles happen once `build` has returned. That is why `sourceMap` on
the next line still applies — and why a file the script writes is finished before anything reads it.

## It is a program, not a list

A build script runs with every capability and has the whole standard library. It may write files,
read them, and start processes:

```lyr
import std.build { addExecutable };
import std.io.file { writeText };

pub fn build() {
    writeText("src/version.lyr", "module version; pub fn text(): string { return \"1.2.0\"; }");

    addExecutable("src/main.lyr", "out/app.lyrbc");
}
```

Relative paths mean the same everywhere in the script: they are resolved against the directory
holding `build.lyr`, not against the directory you started the build from.

**This is code you are running.** `lyric build` in a repository you did not write executes a program
you did not write, exactly as `make` and `cmake` do.

## What the script does not say

Where modules live is a property of the project, not of a build, so it stays in
[`lyric.json`](12-modules.md#saying-where-the-modules-are):

```json
{
  "sourceRoot": "src",
  "nativeRoots": { "engine": "sdk" },
}
```

Both files are read for every artifact. The script never repeats a root, and an editor learns the
layout from `lyric.json` without running anything.

## Two profiles

A compile is one of two shapes, and the shape is a named bundle rather than a list of flags to
remember:

| | `debug` | `release` |
|---|---|---|
| IR optimizations (inlining, scalar replacement, devirtualization) | off | on |
| source map (line numbers) | kept | kept |
| debug info (slot and field names) | kept | dropped |

`lyric build`, `lyric run`, `lyric check` and `lyric test` compile the **debug** profile unless
told otherwise; `lyric pack` compiles the **release** profile unless told otherwise. The reason
is what each shape is for. A debug build keeps every frame, so a panic's backtrace names the
function that failed rather than the caller it was spliced into, and a debugger stops in it. A
release build is what ships. Both keep the source map: a panic in production that names its line
is worth the bytes.

```bash
lyric build app.lyr --release      # or --profile release; --debug is the other name
lyric run app.lyr --release        # the compile flags travel to the compiler
lyric pack app.lyr --debug         # a pack you can debug
```

Every field of a profile stays individually overridable, and a field flag wins over the profile:

```bash
lyric build app.lyr --release --debug-info      # optimized, with the names
lyric build app.lyr --no-source-map             # the debug profile without line numbers
```

`--optimize`/`--no-optimize`, `--source-map`/`--no-source-map` and `--debug-info`/`--no-debug-info`
are the six; `--deny-warnings` stays its own flag. The environment variable `LYRIC_PROFILE`
names the default for a whole process — `LYRIC_PROFILE=release dotnet test` is how a suite runs
in the other shape — and any flag beats it.

What the profiles cost, measured on this interpreter with `tools/Bench`: a loop that builds and
adds structs runs about two and a half times slower in the debug profile, a `for-in` over a range
about one and a half times, and array and iterator loops run about as fast either way. Compiling
is about a tenth faster without the optimizer, which is why the standard library's own test suite
is quicker in the debug profile.

### Switches for bisecting

`lyrc` takes four more switches that belong to no profile: `--no-inline`,
`--no-scalar-replacement`, `--no-devirtualize` and `--no-fusion`. Each takes one optimization or
the fused instruction forms out of a `--release` build, which is how a program that answers
differently or runs slower optimized is narrowed down to the pass responsible. They are
diagnostic aids and carry no compatibility promise beyond that.

Every tool refuses an option it does not know, by name (`LYR-CLI0003`). A flag that is accepted
and does nothing is the one that costs an afternoon.

## When something goes wrong

| | |
|---|---|
| No `build.lyr` in the directory | `LYR-CLI0011` |
| The script does not compile | its own diagnostics, with file, line and column |
| No `build` function, or it panics | `LYR-CLI0012` |
| `build` declared nothing to compile | `LYR-CLI0012` — silence would look like success |
| An entry file is not there | named before anything is written |
