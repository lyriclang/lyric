# Functions

```lyr
fn add(a: int, b: int): int {
    return a + b;
}

fn main(): int {
    return add(2, 3);
}
```

A function without a return type returns `void`. The return type is written after the parameters.

## Default and variadic parameters

A parameter may have a default. `params` on the last parameter collects the rest into an array.

```lyr
import std.io.console { println };

fn greet(name: string, greeting: string = "Hello"): string {
    return greeting + ", " + name;
}

fn total(params values: int[]): int {
    var sum = 0;
    for (v in values) { sum = sum + v; }
    return sum;
}

fn main(): int {
    println(greet("Ada"));
    println(greet("Ada", "Good evening"));
    return total(1, 2, 3);
}
```

A finished array may be passed to a variadic parameter as a whole.

## Several functions of one name

Two functions may share a name when their parameters tell them apart. The call picks by what you
pass:

```lyr
import std.io.console { println };
import std.string { fromInt };

fn describe(n: int): string {
    return "the number " + fromInt(n);
}

fn describe(s: string): string {
    return "the word " + s;
}

fn describe(a: int, b: int): string {
    return "a pair";
}

fn main(): int {
    println(describe(7));
    println(describe("seven"));
    println(describe(1, 2));
    return 0;
}
```

**The arguments choose, and only the arguments.** Not what you do with the result: `let n: int =
describe(...)` does not reach a different `describe`, because a call has to mean one thing before
anyone looks at where it goes.

The rules, in the order they apply — you rarely need them, and when you do you need them exactly:

1. an argument that matches a parameter's type **exactly** beats one that would adapt, so with
   `f(int)` and `f(float)` in reach, `f(2)` is the int one;
2. a concrete parameter beats a **type parameter**: a function written for your type wins over a
   generic one that would also take it;
3. the overload that needs no **default argument** wins, then the one that is not **variadic**;
4. a type's **own method** beats an extension that fits equally well — the rule that predates
   overloading.

If nothing fits, or two fit exactly as well, the compiler says so and names every candidate it
weighed.

**What may not be overloaded.** Interface members: a method table holds one function per slot and
finds it by name, so two of a name would have nowhere to go. Two functions whose parameters are
the SAME are a redeclaration, even when the results differ.

A lambda passed as an argument does not take part in choosing — it has no type until a parameter
gives it one — so the other arguments have to separate the candidates.

### Using an overloaded name as a value

A call chooses by its arguments; a value has none, so the type it is wanted as chooses instead:

```lyr
import std.io.console { println };
import std.string { fromInt };

fn describe(n: int): string { return "number " + fromInt(n); }
fn describe(s: string): string { return "word " + s; }

fn twice(f: fn(int) -> string, n: int): string {
    return f(n) + f(n);
}

fn main(): int {
    // picks 'describe(int)', because that is the shape 'twice' asks for
    println(twice(describe, 7));
    return 0;
}
```

Where no type says which — `let g = describe;` — the compiler refuses rather than guessing.

## Functions as values

A function name used without parentheses is a value of type `fn(...) -> R`.

```lyr
fn double(n: int): int { return n * 2; }

fn apply(f: fn(int) -> int, value: int): int {
    return f(value);
}

fn main(): int {
    return apply(double, 21);
}
```

## Lambdas

A lambda is written with `=>`. Its body is an expression or a block.

```lyr
fn apply(f: fn(int) -> int, value: int): int {
    return f(value);
}

fn main(): int {
    let triple = (n: int) => n * 3;
    let describe = (n: int): int => { return n + 1; };

    return apply(triple, 3) + apply(describe, 5);
}
```

A lambda captures the variables it uses. A block-bodied lambda infers its return type from its
`return` statements when neither an annotation nor a context provides one — the returns must
agree, the same rule match arms follow.

## Static methods

A type can carry functions that need no instance. They are called through the type.

```lyr
struct Point {
    x: int,
    y: int,

    static fn origin(): Point { return Point { x = 0, y = 0 }; }
    static let ZERO: int = 0;
}

fn main(): int {
    let p = Point.origin();
    return p.x + Point.ZERO;
}
```

On a generic type the type arguments belong to the type:

```lyr
struct Pair<T> {
    a: T,
    b: T,

    static fn both(value: T): Pair<T> { return Pair<T> { a = value, b = value }; }
}

fn main(): int {
    let p = Pair<int>.both(4);
    return p.a + p.b;
}
```
