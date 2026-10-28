# C libraries

Lyric compiles through C, and C is the one language it talks to directly. This chapter shows how a
package calls C, brings C of its own, and links a C library — from the system, or from an SDK that
lies somewhere on your machine. A complete package that does all of it is
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
