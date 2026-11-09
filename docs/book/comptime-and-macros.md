# Comptime and macros

Some work is better done once, while the program is compiled, than every time it runs: a table
computed from a formula, a choice between platforms, code that would be the same for every field
of a struct. Lyric does this work in Lyric — the same language, run by the compiler — and in three
steps, from the plainest to the most powerful: `comptime`, type information, and macros.

## `comptime`

`comptime e` evaluates `e` while the program is compiled; the site stands for the value as if it
had been written there. The functions it calls are ordinary functions:

```lyric
import std.io { println };

fn squares(n: int): int[] {
    let out = List<int>.new();
    for (i in 0..n) {
        out.push(i * i);
    }
    return out.toArray();
}

fn main(): void {
    let table = comptime squares(8);
    println(table[7]);
}
```

```output
49
```

The binary holds the eight numbers; `squares` never runs in it. The evaluation is pure: code at
compile time may compute anything, but may not print, read a file, look at the clock or start a
task — a call that reaches the world is an error at the site, naming the call. That keeps a build
the same on every machine. A panic during the evaluation is a compile error too, and a site that
computes too long hits a budget instead of hanging the build.

A value made at compile time is of a value type: a number, a string, an array, a struct, an enum,
a tuple. An object of a class is not — it has an identity that belongs to a run.

`comptime { … }` evaluates a whole block, its last expression the value — the place for a few
`let`s that only the computation needs.

## Choosing by target

`target` and `profile` are constants of the build: the operating system and architecture it is
built for, and whether the profile is a debug one. `comptime if` chooses by them; every branch is
checked, only the chosen one is built:

```lyric
import std.io { println };

fn lineEnd(): string {
    comptime if (target.os == .Windows) {
        return "\r\n";
    } else {
        return "\n";
    }
}

fn main(): void {
    println(lineEnd().length() > 0);
}
```

```output
true
```

`@When(cond)` does the same for a whole declaration at a module's top level — a function that
exists on one system only.

## Type information

`std.meta` answers questions about a type, as constants: `fields<T>()` lists a struct's fields,
`variants<E>()` an enum's variants, `typeName<T>()` its name. `comptime for` walks such a list,
its body once per element, and `e.[f]` is the field of `e` that the info `f` names:

```lyric
import std.io { println };
import std.meta { fields };

struct Config {
    host: string,
    port: int,
    verbose: bool,
}

fn describe(c: Config): void {
    comptime for (f in fields<Config>()) {
        println(f"{f.name} = {c.[f]}");
    }
}

fn main(): void {
    describe(Config { host = "localhost", port = 8080, verbose = false });
}
```

```output
host = localhost
port = 8080
verbose = false
```

The loop is unrolled: the compiled `describe` is three `println`s, each reading its field
directly, with its own type — nothing is looked up at run time. This is how the standard library
writes `Debug`, `Equatable` and the JSON codec for any struct: a default method of an interface
that walks `Self`'s fields.

## Macros

A macro is a function over syntax. It takes pieces of the program — expressions, blocks,
declarations, as values of `std.syntax` — and gives back new ones, which stand in the place of the
site before the module is checked. `quote { … }` writes the code it gives back, and `#{e}` puts a
piece into it:

```lyric
import std.syntax { Block, Expr, Stmt };

/// `retry!(n) { body }` runs the body up to n times, until it does not throw.
pub macro retry(n: Expr, body: Block): Stmt[] {
    return quote {
        var attempts = 0;
        loop {
            attempts += 1;
            try {
                #{body}
                break;
            } catch (e) {
                if (attempts >= #{n}) {
                    throw e;
                }
            }
        }
    };
}
```

A macro expands in the modules that import it, not in its own — so it lives in a module of its own,
and a site calls it with a `!`:

```text
import app.tools { retry };

retry!(3) {
    result = try fetch();
};
```

The quote is hygienic: `attempts` and `e` are the macro's own, fresh at every site, so a variable
`attempts` the caller has beside the site is not touched. `lyric expand` prints a module with its
macros replaced by what they gave, which is the way to see what a site turned into. A macro can
also be an attribute — `@Builder struct Point { … }` adding a `PointBuilder` beside the struct —
and an expression macro can take a little language of its own in a string.

[`examples/macros`](../../examples/macros/src/main.lyr) has all three kinds at work, and the test
suite builds it and compares what `lyric expand` prints.

## Which to reach for

The order of this chapter is the order to try them in. A plain function first; `comptime` when its
result is the same on every run; type information when the same code would be written once per
field; a macro only when the code must take a shape a function cannot — a new kind of statement, a
declaration made from another. A macro is the hardest of the three to read, to debug and to change,
and the compiler can say least about what it will produce.

The next chapter is for readers who know the language this one replaces: [Coming from Lyric
4](coming-from-lyric-4.md).
