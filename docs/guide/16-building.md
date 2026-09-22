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
cd myapp && lyric build && lyric run out/debug/myapp.lyrbc
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

A project is a directory. `lyric build` there, without naming a file, builds it: from its
`build.lyr` when it has one, and by convention when it has none.

`lyric build` with a **file** still means "compile this file" and goes to the compiler, as it always
did. Only a directory, or no argument at all, builds a project.

## Without a script

A project that says in `lyric.json` where its modules are needs nothing more:

```
lyric.json          { "name": "myapp", "sourceRoot": "src" }
src/main.lyr
```

`lyric build` compiles `src/main.lyr` into `out/debug/myapp.lyrbc`. Without a `lyric.json`, the
`main.lyr` in the directory is the program, named after the directory. A source root without a
`main.lyr` is a library: every module under it is checked as a whole and nothing is written,
because a library is source another project imports. A directory with neither is an error that
names both ways (`LYR-CLI0011`).

## The script

A project with more than one program, one that generates part of its own source, or one that packs
what it built, says so in a `build.lyr` at its root:

```lyr
import std.build { executable };

pub fn build() {
    executable("app", "src/main.lyr");
    executable("mktex", "tools/mktex.lyr");
}
```

```bash
lyric build          # the working directory
lyric build ../game  # somewhere else
```

An artifact has a **name**, an identifier, and an entry file relative to the directory holding
`build.lyr`. The name is what it lands as: `out/debug/app.lyrbc`, and `out/release/app.lyrbc` from
`lyric build --release`. Two profiles, two directories: the classic mistake of starting a stale
debug build cannot happen when the two never share a file.

Every artifact is compiled on its own, whole, from its entry file. There is no link step and nothing
is shared between two of them but the source on disk.

## Nothing is compiled while the script runs

`executable` collects; the compiles happen once `build` has returned. That is why a field set on
the next line still applies — and why a file the script writes is finished before anything reads it.

## What an artifact carries

`executable` returns the artifact, and the script configures it after the fact. Every artifact
starts from the profile the build was started with, and each of its fields is one of the profile's:

```lyr
import std.build { executable, Profile };

pub fn build() {
    let app = executable("app", "src/main.lyr");
    app.sourceMap = false;              // this one field, whatever the profile says

    let tool = executable("mktex", "tools/mktex.lyr");
    tool.use(Profile.release());        // the whole bundle, name included: out/release/
    tool.denyWarnings = true;           // and a field on top of it

    app.output = "dist/app.lyrbc";      // or say where it lands outright
}
```

`Profile.selected()` is the one the command line chose, `Profile.debug()` and `Profile.release()`
the two the toolchain knows. What each means is the table under [Two profiles](#two-profiles); the
script asks the toolchain rather than carrying a copy.

## Packing from the script

```lyr
import std.build { executable, packed };

pub fn build() {
    let app = executable("app", "src/main.lyr");
    packed(app);
}
```

`packed` takes an executable's artifact and declares a second one: the same entry and the same
fields, packed with the stub runtime once compiled, as `lyric pack` does. It lands as
`out/<profile>/app`, with the platform's suffix (`app.exe` on Windows) when the output names none.

## A library in the script

```lyr
import std.build { library };

pub fn build() {
    library("geometry", "src");
}
```

Every module under the root is checked as one compilation, and nothing is written. It is what
`lyric build` does by convention for a source root without a `main.lyr`, made explicit for a project
that holds a library beside its programs.

## It is a program, not a list

A build script runs with every capability and has the whole standard library. It may write files,
read them, and start processes:

```lyr
import std.build { executable };
import std.io.file { writeText };

pub fn build() {
    writeText("src/version.lyr", "module version; pub fn text(): string { return \"1.2.0\"; }");

    executable("app", "src/main.lyr");
}
```

Relative paths mean the same everywhere in the script: they are resolved against the directory
holding `build.lyr`, not against the directory you started the build from.

**This is code you are running.** `lyric build` in a repository you did not write executes a program
you did not write, exactly as `make` and `cmake` do.

## Options from the command line

```lyr
import std.build { executable, option, flag };
import std.io.file { writeText };

pub fn build() {
    let version = option("version", "what version.lyr reports") ?? "0.0.0-dev";
    writeText("src/version.lyr",
        "module version; pub fn text(): string { return \"" + version + "\"; }");

    executable("app", "src/main.lyr");

    if (flag("tools", "also build the tools")) {
        executable("mktex", "tools/mktex.lyr");
    }
}
```

```bash
lyric build -D version=1.2.0 -D tools
lyric build --help                      # the fixed options, then the script's
```

`option` answers `null` when the name was not given and `""` when it was given without a value;
`flag` answers whether it was given. A `-D` the script never asked about is an error that names the
ones it did (`LYR-CLI0003`), because an option that is accepted and does nothing is the one that
costs an afternoon. `lyric build --help` in a project runs `build` to learn the options, collecting
and compiling nothing.

## After the build

```lyr
import std.build { executable };
import std.io.file { writeText };

pub fn build() {
    executable("app", "src/main.lyr");
}

pub fn after() {
    writeText("out/debug/VERSION", "1.2.0");
}
```

A `pub fn after()` runs once every artifact was written, and not otherwise: the work after the
build, copying assets beside the program or zipping a release, that a Makefile needs another target
for.

## Building some of it

```bash
lyric build --only mktex
```

Names one or more artifacts; the others are declared and not built. A name nobody declared is an
error that lists the ones that were (`LYR-CLI0019`).

## What the script does not say

Where modules live is a property of the project, not of a build, so it stays in
[`lyric.json`](12-modules.md#saying-where-the-modules-are):

```json
{
  "name": "game",
  "sourceRoot": "src",
  "nativeRoots": { "engine": "sdk" },
  "dependencies": { "geometry": "../geometry" },
}
```

Both files are read for every artifact. The script never repeats a root or a dependency, and an
editor learns the layout from `lyric.json` without running anything.

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
lyric build --release              # the whole project, every artifact under out/release/
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
in the other shape — and any flag beats it. In a script the same six are fields on the artifact,
and `use` is the bundle.

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

## The form before 4.5

`addExecutable("src/main.lyr", "out/app.lyrbc")`, the entry first and the output named outright,
still builds and warns (`LYR-SEM0076`) until 5.0 removes it. It is `executable("app",
"src/main.lyr")` with `app.output = "out/app.lyrbc"`. The old arguments in the new call,
`executable("src/main.lyr", …)`, are refused with a message that says which way round they go.

## When something goes wrong

| | |
|---|---|
| No `build.lyr`, and nothing to build by convention | `LYR-CLI0011` |
| The script does not compile | its own diagnostics, with file, line and column |
| No `build` function, or it panics | `LYR-CLI0012` |
| `build` declared nothing to compile | `LYR-CLI0012` — silence would look like success |
| An entry file is not there | named before anything is written |
| A `-D` nobody asked about | `LYR-CLI0003`, with the options the script declares |
| An `--only` nobody declared | `LYR-CLI0019`, with the artifacts the script declares |
| An artifact asked for no warnings and got one | `LYR-CLI0016` |
