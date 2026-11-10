# Functions and closures

A function names its parameters and their types, the type it gives back, and a body. Lyric
reads a function's signature as its promise: callers are checked against it, and the body is
checked against it once, where it is written. This chapter goes from plain functions to the
functions that are values — lambdas, and the closures they make.

## Parameters and results

```lyric
import std.io { println };

fn area(width: int, height: int): int {
    return width * height;
}

fn main(): void {
    println(f"{area(3, 4)}");
}
```

```output
12
```

A function that gives nothing back declares no result type, or `void`; `main` is one. A
`return` may stand anywhere in a body, and every way through a function that gives a value must
end in one: the compiler checks that none falls off the end.

## Defaults and names

A parameter may have a default, used where the call leaves it out. Every parameter can be named
at the call, which helps where two arguments of one type would be easy to swap. The positional
arguments come first; the named ones follow in any order:

```lyric
import std.io { println };

fn greet(name: string, greeting: string = "Hello"): string {
    return f"{greeting}, {name}!";
}

fn main(): void {
    println(greet("Ada"));
    println(greet("Ada", greeting: "Hi"));
    println(greet(greeting: "Hey", name: "Bo"));
}
```

```output
Hello, Ada!
Hi, Ada!
Hey, Bo!
```

## One name, several counts

Two functions may share a name when they take different numbers of arguments — the call picks
by its count:

```lyric
import std.io { println };

fn area(side: int): int {
    return side * side;
}

fn area(width: int, height: int): int {
    return width * height;
}

fn main(): void {
    println(f"{area(3)} {area(3, 4)}");
}
```

```output
9 12
```

That is the only overloading there is: never by the parameters' types. A name means at most one
function per count, so a call never needs the arguments' types to know which function it calls.

## Functions are values

A function's type is written `fn(int) -> int`: what it takes and what it gives. A function by
its name is a value of such a type, and so is a **lambda**, a function written where it is used —
its parameters in parentheses, then `=>` and its result:

```lyric
import std.io { println };

fn apply(f: fn(int) -> int, x: int): int {
    return f(x);
}

fn double(x: int): int {
    return x * 2;
}

fn main(): void {
    let addOne = (x: int) => x + 1;
    println(f"{apply(double, 21)} {apply(addOne, 1)} {apply((x) => x * x, 5)}");
}
```

```output
42 2 25
```

Where the lambda goes to a parameter whose type is known, its own parameters need no types:
`(x) => x * x` is a `fn(int) -> int` because `apply` asks for one. A lambda's body may also be a
block, with a `return` of its own:

```lyric
import std.io { println };

let clamp = (x: int): int => {
    if (x < 0) {
        return 0;
    }
    return x;
};
println(f"{clamp(-5)} {clamp(5)}");
```

```output
0 5
```

## Closures

A lambda sees the names around it. It **captures** them: a `let` by its value, a `var` as the
variable itself, so that a change on either side is seen on the other. Here the counter's `n`
lives on in the closure after `counter` has returned:

```lyric
import std.io { println };

fn counter(): fn() -> int {
    var n = 0;
    return () => {
        n += 1;
        return n;
    };
}

fn main(): void {
    let next = counter();
    let _ = next();
    let _ = next();
    println(f"{next()}");
}
```

```output
3
```

Each call of `counter` makes a closure of its own, with an `n` of its own.

## Blocks after a call

A function whose last parameter is a function can take it as a block after the call's
parentheses, or without them where it is the only argument. That is how code reads that runs a
body for you — twice here, and in the library a test, a benchmark's loop or a task:

```lyric
import std.io { println };

fn twice(body: fn() -> void): void {
    body();
    body();
}

fn main(): void {
    twice {
        println("again");
    }
}
```

```output
again
again
```

The block follows the parentheses where there are other arguments, and names its own parameters
before a `=>` where the function it stands for takes some:

```lyric
import std.io { println };

fn repeat(times: int, body: fn(int) -> void): void {
    for (i in 0..times) {
        body(i);
    }
}

fn main(): void {
    repeat(2) { round =>
        println(f"round {round}");
    }
}
```

```output
round 0
round 1
```

## Lambdas over collections

Most lambdas are short and go to a library function that calls them for each element. An array's
`iter()` walks it, `filter` keeps what a lambda says yes to, `map` turns each element into another,
and `collect` gathers what comes out:

```lyric
import std.io { println };

let numbers = [1, 2, 3, 4, 5, 6];
let tens = numbers.iter().filter((x) => x % 2 == 0).map((x) => x * 10).collect<List<int>>();
println(f"{tens}");
```

```output
[20, 40, 60]
```

Nothing is copied along the way: the iterator pulls one element at a time through the chain. The
collections and their iterators have a chapter of their own later in the Book.

The next chapter, [Structs, classes and enums](structs-classes-enums.md), gives values a shape and
functions a home: the methods of a type.
