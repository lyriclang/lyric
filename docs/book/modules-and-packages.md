# Modules and packages

A program grows past one file. In Lyric every file is a **module**, every directory under `src/`
groups modules, and a **package** — a directory with a `lyric.toml` — is what you build, publish
and depend on. This chapter is about how modules name each other, what one module lets the others
see, and how a package takes in another.

## A file is a module

A module's name is its file's path below `src/`, without `.lyr`, with the package's name in front.
In a package named `shop`:

```text
shop/
  lyric.toml
  src/
    main.lyr          the module shop.main — the program starts at its main
    cart.lyr          shop.cart
    pricing/
      tax.lyr         shop.pricing.tax
```

A file writes no module header: where it lies is what it is called. A directory is no module of
its own, only the place its files' names go through.

## Importing

An `import` names a module and what the file takes from it. The forms are the same for the
standard library, for your own modules and for another package's:

```lyric
import std.io { println, print as write };
import std.io;
import std.math as m;

fn main(): void {
    write("6 * 7 = ");
    println(f"{m.pow(6, 2) + 6}");
    io.println("done");
}
```

```output
6 * 7 = 42
done
```

- **Selective**, `import std.io { println }`, takes the names in braces; `as` renames one.
- **Qualified**, `import std.io;`, takes the module under its last name: `io.println`.
- **Aliased**, `import std.math as m;`, takes it under another name: `m.pow`.

The forms can stand side by side for one module, but each name is imported once: the same import
written twice is an error. An import nothing uses is a warning, so a file's imports say what it
really uses. There is no import of everything a module has.

The **prelude** is imported everywhere without a line: `List`, `Map`, `Set`, `Result`, `Error`,
`assert`, `panic` and the interfaces the language itself uses. `println` is not in it — writing to
the console is something a file says it does.

## What a module shows

A declaration is seen by every module of its own package. A word in front changes that:

```lyric
pub fn total(prices: int[]): int {
    var sum = 0;
    for (p in prices) {
        sum += p;
    }
    return sum + tax(sum);
}

private fn tax(amount: int): int {
    return amount / 10;
}
```

- **`pub`** — every package may use it: it is the package's surface.
- no word — every module of this package, and no other package.
- **`private`** — this module alone.

A field or a method takes the same words: a `pub struct` may keep some fields `private`, so that
only its own module can build it or change them.

A package's **root module**, `src/lib.lyr`, is the name the package has for others: `import shop`
reads it. With `pub import` it passes on what its modules declare, so that a user of the package
writes `import shop { total }` without knowing which file `total` lives in:

```text
// src/lib.lyr
pub import shop.cart { total };
```

## Depending on another package

A package names the packages it uses under `[dependencies]` in its manifest — by a directory, or by
a git repository and a tag:

```toml
[package]
name = "shop"
version = "0.1.0"

[dependencies]
money = { path = "../money" }
http = { git = "https://github.com/example/http", tag = "v1.2.0" }
```

`lyric add` writes such a line for you. The first build reads the git repositories, keeps them in
the user's cache and writes `lyric.lock`: the exact commit of each, so that the next build — on
another machine, in CI — reads the same code. `lyric update` reads the tags anew and moves the lock.

A module imports from its own package, from `std`, and from the packages its manifest names — not
from the packages those depend on. What a package uses, its manifest says.

The next chapter puts the parts so far together in a whole program: [a command-line
tool](project-cli-tool.md).
