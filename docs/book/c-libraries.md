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
symbols are defined twice and the link fails — share a C library by wrapping it in one package
that the others depend on.

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

## Loading a library while the program runs

A library the program picks while it runs — a plugin — is not linked at all: `std.ffi`'s `Library`
opens it by its path and looks up its functions by name. [`examples/ffi`](../../examples/ffi) shows
both, a library linked into the program and one loaded while it runs.
