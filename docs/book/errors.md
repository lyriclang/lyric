# Errors

A function that can fail says so in its signature: `throws` and the errors it may throw. Every
call of it is marked `try`, so that a reader sees where a function can stop early, and every error
is either handled or passed on by a function that says so in turn. [A first
program](first-program.md#errors) met the basics; this chapter makes errors of its own, handles
them by kind, and cleans up on the way out.

## An error of your own

An error is a value of a type that conforms to `Error`: it says what went wrong in `message()`, and
carries whatever else the handler may need:

```lyric
struct NotFound :: [Error] {
    name: string,

    fn message(): string {
        return f"no user '{this.name}'";
    }
}

struct Locked :: [Error] {
    fn message(): string {
        return "the account is locked";
    }
}
```

`throw` ends the function with the error. A function that throws more than one kind lists them:

```lyric
struct NotFound :: [Error] {
    name: string,

    fn message(): string {
        return f"no user '{this.name}'";
    }
}

struct Locked :: [Error] {
    fn message(): string {
        return "the account is locked";
    }
}

fn find(name: string): int throws [NotFound, Locked] {
    if (name == "root") {
        throw Locked {};
    }
    if (name != "ada") {
        throw NotFound { name = name };
    }
    return 7;
}
```

The list is part of the signature. A caller knows from it exactly what can come out, and the
compiler holds the body to it: a `throw` of anything else, or a call that throws something the
list leaves out, does not compile.

## Handling by kind

`try { … } catch …` runs the block and, where an error comes out of it, the first `catch` whose
type fits. Inside the block, the `try` marks are not needed: the block is the mark.

```lyric
import std.io { println };

struct NotFound :: [Error] {
    name: string,

    fn message(): string {
        return f"no user '{this.name}'";
    }
}

struct Locked :: [Error] {
    fn message(): string {
        return "the account is locked";
    }
}

fn find(name: string): int throws [NotFound, Locked] {
    if (name == "root") {
        throw Locked {};
    }
    if (name != "ada") {
        throw NotFound { name = name };
    }
    return 7;
}

fn main(): void {
    for (name in ["ada", "bob", "root"]) {
        try {
            println(f"{name}: {find(name)}");
        } catch (e: NotFound) {
            println(f"missing: {e.message()} ({e.name})");
        } catch (e: Locked) {
            println(f"locked: {e.message()}");
        }
    }
}
```

```output
ada: 7
missing: no user 'bob' (bob)
locked: the account is locked
```

`catch (e: Error)` takes any error. A `main` whose `try` leaves something uncaught must say
`throws` itself; an error that leaves `main` ends the program with its message and exit code 1:

```lyric,no_run
struct Broke :: [Error] {
    fn message(): string {
        return "it broke";
    }
}

fn main(): void throws Broke {
    throw Broke {};
}
```

```text
error: it broke
```

The line below it names the place the error was thrown, as a panic's trace does.

## Errors as values

Sometimes an error is not to be handled right away but kept — gathered from many calls, sent to
another task, returned in a list. `Result<T, E>` is the value of a call that gave a `T` or threw an
`E`; `Result.of` runs a function and catches what it throws into one:

```lyric
import std.io { println };

struct Odd :: [Error] {
    n: int,

    fn message(): string {
        return f"{this.n} is odd";
    }
}

fn half(n: int): int throws Odd {
    if (n % 2 != 0) {
        throw Odd { n = n };
    }
    return n / 2;
}

fn main(): void {
    for (n in [4, 7]) {
        let outcome = Result.of(() => try half(n));
        match (outcome) {
            .Ok(v) => println(f"{n}: {v}"),
            .Err(e) => println(f"{n}: {e.message()}"),
        }
    }
}
```

```output
4: 2
7: 7 is odd
```

`get()` turns a `Result` back into a call: the value, or the error thrown again.

## Where an error cannot happen

Now and then a call can throw by its signature but not with the arguments you give it — a parse of
a constant you wrote yourself. `try!` takes the value and turns an error into a panic, which stops
the program with the error's message and where it came from:

```lyric
import std.io { println };

let port = try! int.parse("8080");
println(f"{port}");
```

```output
8080
```

Use it where an error would be a bug in the program, not a thing that happens to it.

## Cleaning up

A function that returns early — with `return`, or because an error came out of a call — still has
to put things back. `defer` runs a statement when the scope is left, whichever way it is left.
`using let` does the same for a value that must be closed: it calls the value's `close()` at the end
of the scope. Both run in the reverse of the order they were written, the last first:

```lyric
import std.io { println };

class Log :: [Closeable] {
    name: string,

    fn close(): void {
        println(f"closed {this.name}");
    }
}

fn work(): void {
    defer println("deferred");
    using let log = Log { name = "log" };
    println(f"working with {log.name}");
}

fn main(): void {
    work();
}
```

```output
working with log
closed log
deferred
```

Files, sockets and locks of the library are `Closeable`; `using let` is how a program holds one
without leaking it when an error passes through.

## Errors and panics

An **error** is something that happens to a correct program: a file that is not there, a number a
user mistyped, a server that does not answer. The program is expected to deal with it, and the
types make sure it does. A **panic** is a bug: an index past the end, an overflow, a `!` on
`null`, an `assert` that does not hold. It stops the program — or, in a program with tasks, the
task it happened in — with a message and the place, and is not caught.

The next chapter, [Interfaces and generics](interfaces-and-generics.md), is about code that works
for many types at once.
