# Modules

A file is a module. The header names it; without a header the name comes from the file name.

```lyr
module app.geometry;

pub struct Point { x: int, y: int, }

pub fn origin(): Point { return Point { x = 0, y = 0 }; }

fn helper(): int { return 1; }
```

`pub` exports. Everything else is visible only inside the module.

## Importing

```lyr
import std.io.console;                     // qualified: console.println(…)
import std.math { sqrt, pi };              // selective
import std.collections as coll;            // alias

fn main(): int {
    console.println(f"{sqrt(4.0)}");
    let list = coll.List<int>.empty();
    return list.length();
}
```

A qualified import binds the last path segment as the name. A selective import binds the listed
names directly. An alias renames the module.

## A program of several files

A module path becomes a file path under the directory of the file being compiled:

```
app.lyr            <- the entry file, and the root
util.lyr           <- import util
shapes/circle.lyr  <- import shapes.circle
```

```text
// util.lyr
module util;

pub fn double(n: int): int { return n * 2; }

// app.lyr
import util { double };

fn main(): int { return double(21); }
```

```bash
lyric run app.lyr
```

The two files above are shown together because neither is a program on its own — which is also why
they are not compiled by the test suite the way every other snippet in this guide is.

Three rules follow from where a module is found:

- **A file must agree with the path it was loaded from.** `util.lyr` declares `module util;` or no
  header at all. A header naming something else is an error, because the path is what the import
  wrote down.
- **`std` belongs to the standard library.** A file at `std/io/console.lyr` beside your program is
  never loaded; the import goes to the standard library. Nothing in your own directory can quietly
  take its place.
- **Only the standard library declares functions without a body.** In your own modules a missing
  body is an error, not an import declaration.

Everything ends up in one `.lyrbc`. There is no separate compilation step per file and no link step.

## Saying where the modules are

The rules above need no configuration, and for a program in one directory that is the whole story.
A project with a `src/` directory says so in a `lyric.json` beside it:

```json
{
  // what the project is called
  "name": "game",

  // where our own modules live
  "sourceRoot": "src",

  /* an SDK whose modules may declare functions without a body */
  "nativeRoots": { "engine": "sdk" },

  // other projects this one imports from, by the segment each owns
  "dependencies": { "geometry": "../geometry" },

  // the oldest toolchain that may build it
  "toolchain": "4.5",
}
```

```
lyric.json
src/main.lyr                  <- import shapes.area
src/shapes/area.lyr
sdk/engine/input.lyr          <- import engine.input
../geometry/src/geometry.lyr  <- import geometry
```

The file is searched for upwards from the file being compiled, so it is found from anywhere in the
project. Comments and trailing commas are allowed; it is meant to be edited by hand.

- **`name`** is the project's name — a module name, so letters, digits and `_`. Without it the
  directory's name serves.
- **`sourceRoot`** replaces "the directory of the entry file" as the module root.
- **`nativeRoots`** maps a module path segment to a directory whose modules may declare functions
  without a body. That segment then belongs to the root, and is no longer looked for under
  `sourceRoot`.
- **`dependencies`** maps a module path segment to the directory of another project. That
  project's own `sourceRoot` (or its directory, when it has no `lyric.json`) is where the segment's
  modules come from: `import geometry` reads `geometry.lyr` there and `import geometry.shapes`
  reads `geometry/shapes.lyr`, the same derivation `std` uses. The segment belongs to the
  dependency and is no longer looked for under your own root.
- **`toolchain`** names the oldest toolchain version that may build the project. An older one
  refuses with both numbers (`LYR-CLI0018`) instead of compiling against a language the project
  was not written for. A minimum, nothing more: a newer toolchain builds it.

Every key is optional, and **without the file nothing changes**: the entry file's directory is the
root and no module of your own may declare a native.

### Depending on another project

A dependency is a directory: a library `lyric new mylib --lib` wrote, a checkout beside yours, a
subdirectory. Its modules are ordinary Lyric — a function without a body is an error there as it
is here — and its own `lyric.json` counts: its `nativeRoots` come along, and its `dependencies`
do too, so a library that stands on another library needs no help from the program using it.

Three rules follow from the namespace being one namespace:

- **A segment belongs to one root.** Two of your dependencies that want `mathx` to mean two
  different directories are a conflict, refused with both named. Your own `lyric.json` decides
  it: name `mathx` yourself, and your word stands for everybody under you.
- **`std` is nobody's dependency**, and neither is a segment a native root already owns.
- **A dependency's `toolchain` is a requirement of your build too.** A library written for a
  newer toolchain refuses under an older one, naming its own file.

Nothing is fetched and nothing is versioned: a dependency is where you say it is. What is fetched
from where, pinned and verified, is a later chapter.

A key nobody knows is a warning rather than an error, so a file written for a later version still
loads — but the warning is there, because a typo that does nothing is worse than one that complains.

`lyric.json` is read and never executed. That is what lets an editor learn the layout of a project
without running anything from it.

## Module constants

A module-level `let` is a constant. It is initialized once before `main` runs, in declaration
order.

```lyr
let VERSION = "1.0";
let BANNER = "lyric " + VERSION;

fn main(): int {
    return if (BANNER == "lyric 1.0") 0 else 1;
}
```

An initializer may read a constant declared before it, not one after. There is no module-level
`var`.

Across files the order follows the imports: a module's constants are initialized after those of
every module it imports, so an initializer may read them too.

```text
// config.lyr
module config;

pub let width = 10;
pub let height = 20;

// board.lyr
module board;

import config;

pub let cellCount = config.width * config.height;
```

Two files again, so the test suite does not compile them — but a program of these two does.
Which file you compile does not change that. `config` is initialized before `board` because
`board` says so with its import — whether the program was entered through `board`, through
something that imports it, or through `board` itself.

## Type aliases

```lyr
type Id = int;

fn describe(id: Id): int {
    let copy: Id = id;
    return copy;
}

fn main(): int { return describe(1); }
```

An alias is a name for a type, not a new type: `Id` and `int` are interchangeable.

An **opaque** alias is the opposite: a new identity over the same layout. Nothing converts
implicitly, the explicit `as` to exactly the underlying and back is the one door, and equality
works within the alias — everything else is refused:

```lyr
opaque type Ticket = int;

fn issue(raw: int): Ticket {
    return raw as Ticket;
}

fn main(): int {
    let a = issue(7);
    let b = issue(7);
    // a + 1, a < b, or 'let t: Ticket = 7;' would all be compile errors.
    return if (a == b) a as int else 0;
}
```

The point is the wall: an SDK declares `pub opaque type Entity = int;` beside its natives, a
script holds and returns the handle — and cannot leak it into arithmetic. On the wire the handle
is its underlying number. The wall spans modules: an SDK of several files declares the handle
once and imports it wherever a native signature names it — selectively or module-qualified, in
scalar and array positions alike.

**Making one is the declaring module's privilege**. The inward cast — `42 as
Entity` — used to work from any module that could see the alias, which quietly undid the wall:
a script could forge the very handle the type exists to protect. Outside the declaring module
it is refused (`LYR-SEM0093`; 3.8 announced it as a warning, 4.0 keeps the promise the clock
made). The outward cast (`e as int`) stays free
everywhere — reading the number breaks no promise. If a value legitimately has to be rebuilt
from a stored number — a saved game, a wire message — the declaring module offers a constructor
function for it; issuing stays an issuer's act:

```lyr
opaque type Entity = int;

// The issuer's act, offered as API — what a saved game calls instead of forging.
pub fn entityFromRaw(raw: int): Entity {
    return raw as Entity;
}

fn main(): int {
    let restored = entityFromRaw(42);
    return restored as int;
}
```
