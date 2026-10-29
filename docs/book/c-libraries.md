# C libraries

Lyric compiles through C, and C is the one language it talks to directly. This chapter shows how a
package calls C, brings C of its own, compiles a C library from its source, and links one — prebuilt,
from the system, or from an SDK that lies somewhere on your machine. A complete package that does all of it is
[`examples/ffi`](../../examples/ffi).

## Calling C

A C function is declared with `extern "C"` and called like any other. The C library is always
there, so a program can call `abs` right away:

```lyric
import std.io { println };

extern "C" fn abs(n: int32): int32;

fn main(): void {
    println(f"{abs(-7)}");
}
```

```output
7
```

The declaration has to say what C says: C's `int` is 32 bits, so the parameter is an `int32`, not
Lyric's 64-bit `int`. The compiler checks that every type of an `extern "C"` function can cross into
C — integers by their width, `float`, `bool`, `Ptr<T>`, `CStr`, structs marked `@Layout(C)` — and
refuses the others. `std.ffi` has the types for pointers, C strings and callbacks.

## C in the package

C sources of your own go into the package and are named in the manifest's `[native]` section:

```toml
[native]
sources = ["native/*.c"]
include = ["native/include"]
```

The build compiles them with the program, under its profile, and links them in; an `extern "C"`
declaration in any module of the package calls them. A dependency's native part comes along with
the dependency.

`defines` gives the sources their preprocessor definitions — the manifest's way of saying `-D`:

```toml
[native]
sources = ["native/*.c"]
defines = ["RAYLIB_STATIC", "LEVEL=2"]
```

Besides the directories of `include`, every source finds `<lyric.h>`, the toolchain's header for C
that works with Lyric, and the `include` directories of each package your package names under
`[dependencies]` — so a package that wraps a C library shares its headers with the packages that
use it. Only direct dependencies count: a package does not see the headers of a package it never
named.

## Prebuilt libraries

A library you already have built goes into the package and is named by its path. A static library
is an **archive**:

```toml
[native.x86_64-linux-gnu]
archives = ["native/lib/x86_64-linux-gnu/libraylib.a"]

[native.x86_64-windows-gnu]
archives = ["native/lib/x86_64-windows-gnu/libraylib.a"]
```

An archive is built for one target, so it goes into the table of that target's triple. The tables
layer: `[native]` holds for every build, `[native.linux]` and its siblings add to it for one system,
and `[native.x86_64-linux-gnu]` and its siblings add to that for one target. Because the archive is
named by its path, a build for another target links it from any machine:
`lyric build --target x86_64-windows-gnu` on Linux links the Windows archive. On Windows the
archive must be built for the GNU ABI — by MinGW or `zig cc`; one built by MSVC does not link.

A **shared library** is a `.so`, a `.dylib` or a DLL, named in `dylibs`:

```toml
[native.linux]
dylibs = ["native/linux/libgreet.so"]

[native.macos]
dylibs = ["native/macos/libgreet.dylib"]

[native.windows]
dylibs = [{ dll = "native/windows/greet.dll", lib = "native/windows/libgreet.dll.a" }]
```

On Windows the link reads the DLL's import library, so an entry names both. The build puts every
shared library beside each binary it makes — `lyric build`, `run`, `test` and `bench` — and the
binary looks there first: copy the directory, and the program runs anywhere. For that the library
has to name itself by its file name, which most do; one that names the directory it was built in is
refused with `LYR-BLD0006`, and the message says how to rename it.

A prebuilt file has to be part of the package — under `native/`, or named in `[package] include` —
because that is what the package is when someone else gets it from git, and what the lock holds
the bytes of. When you replace one, the next build links again, even if the file kept its time.

## C from its source

Most C libraries are easiest to build from their own source, with the program. You don't copy the
source into your package: `[native.vendor]` names it as a **source tree**, and the native part's
paths reach into it with `@name/`:

```toml
[native.vendor]
raylib = { git = "https://github.com/raysan5/raylib", tag = "5.5" }

[native]
sources = ["@raylib/src/rcore.c", "@raylib/src/rshapes.c", "@raylib/src/utils.c"]
include = ["@raylib/src"]
defines = ["PLATFORM_DESKTOP"]
```

A tree from git names its revision as a dependency does — `tag`, `branch`, `rev`, or none for the
default branch. It is no package: it needs no `lyric.toml`, and every file of the revision is part
of it. The build fetches it into your user cache, once for every project, and `lyric.lock` holds
the commit and a hash of its content with `kind = "native"`; check the lock in, and every build reads
the same C. `lyric update raylib` moves the tree to its branch's head or its tag's new commit, as
it does a package.

A tree can also be a release file — a `.tar.gz` or a `.zip` — at a URL, pinned by its SHA-256:

```toml
[native.vendor]
sqlite = { url = "https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip", hash = "sha256:…" }
```

The build loads the file once, checks its hash, and unpacks it, leaving out the single top directory
most release files have. You don't compute the hash yourself: `lyric add` loads the file and writes
the line with it,

```sh
lyric add sqlite --vendor --url https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip
lyric add raylib --vendor --git https://github.com/raysan5/raylib --tag 5.5
```

and a URL written without its hash is refused, the message giving the hash of what it loaded. A
`.tar.xz` is not unpacked; take such a library from git.

A tree is read only when a build needs it: one named only in `[native.windows]` is never fetched
by a build for Linux. `--offline` reads the cache alone, and `--locked` refuses a tree the lock
does not hold. Compiling a tree runs nothing of it — there is no script in it to trust.

A tree belongs to the package that names it. If two packages of a program compile the same C, its
symbols are defined twice and the link fails, naming both (`LYR-LNK0003`) — share a C library by wrapping it in one package
that the others depend on.

## Bindings from the header

Declaring every function by hand is where binding C gets tedious. A library's header already says
what it has, so the build can write the declarations for you:

```toml
[native]
sources  = ["@raylib/src/rcore.c", "@raylib/src/rshapes.c", "@raylib/src/utils.c"]
include  = ["@raylib/src"]
bindings = [{ header = "@raylib/src/raylib.h", module = "rl" }]
```

The build reads `raylib.h` with the C compiler, as your package's sources see it — the same
`include` and `defines`, for the target you build for — and makes the module `rl` of your package:
in a package `game`, `game.rl`. You import it like any module you wrote:

```text
import game.rl { InitWindow, WindowShouldClose, BeginDrawing, EndDrawing, CloseWindow };

fn main(): void {
    InitWindow(800, 450, "hello");
    while (!WindowShouldClose()) {
        BeginDrawing();
        EndDrawing();
    }
    CloseWindow();
}
```

The module holds the header's functions as `extern "C"` declarations — a variadic one with its
`...` —, its structs as `@Layout(C)` structs, its enums as constants with the enum's name as their
type, its typedefs as aliases, its pointers to functions as `extern "C" fn` types, and every
`#define` that is one number or one string as a constant. Names stay C's: `InitWindow`, not
`initWindow`. A name Lyric reserves gets a `_` after it — a parameter `in` is `in_`, a type
`string` is `string_` — and no lint looks at the module, since you did not write it.

The types are what each C type is **on your target**: a `long` is `int` on Linux and macOS and
`int32` on Windows, a `char` is `uint8` on Linux for Arm, so the module is made for every target
you build for. A pointer you hand to C may be null, so a parameter `const char *` is `?CStr` — a
string goes in as it is — and `T *` is `?Ptr<T>`; in a struct's field, where Lyric takes no
optional, they are `CStr` and `Ptr<T>`, and a null there is yours to look for. A struct the header
names without its fields is a `Ptr<void>` behind its pointer.

What the module cannot hold is left out and listed at its end, with the place in the header and why:

```text
// Left out:
// - union Any (geo.h:16): a union does not cross
// - geo_twice (geo.h:27): a static or inline function has no symbol to link
// - GEO_SQ (geo.h:10): a function-like macro is no declaration
```

Unions, bit fields, function-like macros, `static inline` functions and variables are not in it;
for one of those, write the declaration yourself, or a small C function in your package that wraps
it. The module is made again only when the header, a header it includes, `include`, `defines`, the
target or the toolchain changes.

To see the module — or to keep it in your package instead of making it every build —, write it
out:

```sh
lyric bindgen native/geo.h > src/geo.lyr
```

`lyric bindgen` uses the package's `include` and `defines`, `--target` picks another target, and a
path `@name/…` reads a header of a source tree. A header that does not compile stops the build with
`LYR-BLD0005` and what the C compiler said; a module that also lies under `src/` is
`LYR-RES0016`.

## Libraries of the system

`libs` links libraries by name — `"m"` is `-lm`. What a build needs only on one system goes into a
table for that system:

```toml
[native.linux]
libs = ["X11"]

[native.windows]
libs = ["gdi32", "winmm"]
```

`[native.linux]`, `[native.windows]` and `[native.macos]` add to `[native]` for a build for that
system. A build for Windows links Windows' system libraries from any machine; a library of a Linux
system is found only when you build on Linux.

A library installed on the machine often knows best how it is linked, and tells `pkg-config`.
`pkgConfig` asks it:

```toml
[native.linux]
pkgConfig = ["sdl2", "zlib"]
```

The build runs `pkg-config --cflags --libs sdl2 zlib`; the include directories and definitions it
answers go to your package's C sources (and to its bindings), the libraries and flags to the link.
`PKG_CONFIG` names another `pkg-config`. Its answer is about this machine, so it holds for a build
for this machine only: a build for another target stops with `LYR-BLD0004`.

On macOS, system libraries come as **frameworks**:

```toml
[native.macos]
frameworks = ["Cocoa", "IOKit", "CoreVideo"]
```

Each is linked as `-framework Cocoa`. A framework is part of the macOS SDK, which only a Mac has,
and the C compiler finds it for the Mac it runs on: a build for macOS from Linux or Windows — or
for an Intel Mac from an Apple-silicon one — stops with `LYR-BLD0004`; build it on the Mac it is
for. `frameworks`
belongs in `[native.macos]` or a macOS triple's table; anywhere else it is refused.

## A library from an SDK

Many libraries come as an SDK you unpack somewhere: SDL3 for Windows, say, with the import library
`libSDL3.dll.a` under `lib/` and `SDL3.dll` under `bin/`. Where that lies differs from machine to
machine, so the manifest cannot name it. A **build script** can: `build.lyr` at the package's root
is a program that runs before the package compiles, and `std.build` is how it talks to the build.

```text
// build.lyr
import std.build;
import std.os;

fn main(): void {
    build.rerunIfEnvChanged("SDL3_DIR");
    if (let sdk = os.env("SDL3_DIR")) {
        build.linkSearch(f"{sdk}/x86_64-w64-mingw32/lib");
        build.linkLib("SDL3");
        build.besideBinary(f"{sdk}/x86_64-w64-mingw32/bin/SDL3.dll");
    } else {
        build.warn("set SDL3_DIR to the SDL3 SDK");
    }
}
```

- **`linkSearch(dir)`** tells the link where to look for the libraries of `linkLib` and of `libs`,
  before the linker's own directories. For Windows it finds a MinGW SDK's `libX.dll.a` too. The
  directory is never written into the program: the binary does not depend on where the build found
  the library.
- **`besideBinary(paths…)`** copies files into the directory of every binary the build makes — of
  `lyric build` and `lyric run`, of `lyric test` and `lyric bench`. A binary looks for shared
  libraries in its own directory first, so the program starts from wherever you copy it, as long as
  `SDL3.dll` comes along. A file that is not there, or two files with one name, stop the build with
  `LYR-BLD0006` — `lyric explain LYR-BLD0006` says more.
- **`rerunIfEnvChanged(names…)`** runs the script again when `SDL3_DIR` changes. A script runs only
  when something it depends on changed; without this line, a new value would keep what the last run
  asked.

The root package's build script always runs. A dependency's runs only where your manifest trusts
it, under `[trust] build-scripts = ["name"]`: a build script is a program, and you decide whose
programs run on your machine.

## C a build script compiles

The manifest says what is always so. A build script decides what depends on the build — the target,
the profile, the environment. `build.target()` tells it what the build is for:

```text
// build.lyr
import std.build;

fn main(): void {
    let target = build.target();
    let lua = build.cLibrary("lua").sources("@lua/src/*.c").include("@lua/src").flags("-O2");
    if (target.os == .Windows) {
        lua.define("LUA_USE_WINDOWS");
    } else {
        lua.define("LUA_USE_POSIX");
    }
    lua.compile();
    if (target.os == .MacOS) {
        build.linkFramework("Accelerate");
    }
}
```

**`cLibrary(name)`** is a C library the script compiles with the program. Its `define`s and
`flags` are its own — your package's other C does not see them, so a flag one library needs does
not change another. Its sources see their own `include` first, then what the package's sources see.
Its paths are your package's or a source tree's, as in the manifest.

`target()` holds the `triple`, the `os` and `arch` as `Platform` and `Arch` name them, and whether
the build `isHost`. The script also has the manifest's keys as calls — `linkLib`, `linkArchive`,
`linkFramework`, `pkgConfig` —, for what it decides while it runs.

Some libraries are built by their own build system — CMake, a `configure` script. **`cEnv()`**
gives the environment they compile C under with Lyric's C compiler, for the build's target: `CC`,
`AR` and `CFLAGS`, to hand to a `std.process.Command`. Run the library's build into `outDir()`,
then link what it made with `linkArchive`. **`vendorDir("name")`** is where a source tree lies —
the tree is fetched when the script first asks, and the script runs again from its start, so ask
before doing anything you would not do twice.

## A library for C

The other way round: **`lyric build --lib`** builds a package's `src/lib.lyr` as a library a C
program links. Its `@Export` functions are C functions, declared in the header beside it. Under
`out/<profile>/<target>/` it writes `lib<name>.a` and `<name>.h`, and with `--shared` a shared library
instead of the archive. [`examples/embedded-host`](../../examples/embedded-host) is such a C program.

The archive holds the library, the runtime and the **archives of the packages' native parts**, so a C
program links one file. What it links beside that file, the build says once the archive is written:

```text
note: link out/debug/x86_64-linux-gnu/libcalc.a with out/debug/x86_64-linux-gnu/libgreet.so -lrt -lpthread -lm -ldl -lunwind
```

That covers the packages' shared libraries (`dylibs`), which lie beside the archive, and their
libraries by name with the directories they are searched in. It also covers what `pkg-config` gave,
the frameworks, and the system's libraries the runtime calls. Hand those words to the C compiler after the
archive, in that order. They are the target's: a build for Windows says `-lshell32 -lws2_32`, and one
for macOS often says nothing. `--json` gives the same words as the artifact's `linkArgs`, for a
script or a build system to take:

```text
{"type":"artifact","version":1,"kind":"library","name":"calc","path":"…/libcalc.a","linkArgs":["…/libgreet.so","-lrt","-lpthread","-lm","-ldl","-lunwind"]}
```

Where the C program finds a shared library while it runs is the program's own choice. Put the
files beside it and give it a run path of its own directory (`-Wl,-rpath,$ORIGIN`, on macOS
`-Wl,-rpath,@executable_path`); Windows looks there first anyway. A shared library made with
`--shared` is linked against all of this itself, so nothing is said beside it, and only the packages'
shared libraries lie beside it.

## When the link fails

The compiler trusts an `extern` declaration; the linker checks it. When the link fails, the
build reads what the linker said and points where the fix belongs:

```text
error[LYR-LNK0001]: nothing the link takes defines 'sqlite3_libversion'
 --> src/main.lyr:1:1
  |
1 | extern "C" fn sqlite3_libversion(): CStr;
  | ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ declared here, and defined nowhere the link looks
  |
  = help: name the library that defines it in the package's [native] — 'libs', 'archives', 'frameworks' or 'pkgConfig' — or link it from the build script
```

- **`LYR-LNK0001`** — a symbol nothing defines, at the `extern` line that declares it. Most often
  the library is not named yet; else the name differs from the header's.
- **`LYR-LNK0002`** — a library or framework the linker does not find, at the manifest line that
  names it (or naming the build script or `pkgConfig` that did). Install its development package,
  or let `pkgConfig` say where it lies.
- **`LYR-LNK0003`** — a symbol two packages define, naming both.

Anything else the linker says comes as it is (`LYR-BLD0001`). A C source whose `#include` finds
nothing also says which directories were searched — the package's `include`, then its
dependencies'.

To see the commands themselves, build with **`-v`**: each run of the C compiler, the archiver and
the linker is said as it starts, `running: zig cc …`. What is up to date runs nothing, and says
nothing.

## Loading a library while the program runs

A library the program picks while it runs — a plugin — is not linked at all: `std.ffi`'s `Library`
opens it by its path and looks up its functions by name. [`examples/ffi`](../../examples/ffi) shows
both, a library linked into the program and one loaded while it runs.
