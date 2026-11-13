# Interfaces and generics

Some code does not care which type it works on, as long as the type can do certain things: sort
anything that can be compared, total the area of anything that has one. An **interface** names
what a type must be able to do; a type **conforms** by doing it. A **generic** function or type
takes the type as a parameter, bounded by the interfaces it needs.

## Interfaces

An interface lists methods. A type names the interfaces it conforms to after `::` and implements
their methods; a method the interface writes out itself is a **default** the type may replace:

```lyric
import std.io { println };

interface Shape {
    fn area(): float;

    fn describe(): string {
        return f"a shape of area {this.area()}";
    }
}

struct Circle :: [Shape] {
    r: float,

    fn area(): float {
        return 3.0 * this.r * this.r;
    }
}

struct Square :: [Shape] {
    side: float,

    fn area(): float {
        return this.side * this.side;
    }

    fn describe(): string {
        return f"a square of side {this.side}";
    }
}

fn main(): void {
    let shapes: Shape[] = [Circle { r = 1.0 }, Square { side = 2.0 }];
    for (s in shapes) {
        println(s.describe());
    }
}
```

```output
a shape of area 3.0
a square of side 2.0
```

A value of type `Shape` holds any value that conforms, and a call on it reaches that value's own
method — the circle's `area`, the square's `describe`. Which method runs is decided by the value,
while the program runs; a call of a method on a struct or a class of known type is decided by the
compiler.

An implementation that adds to the default, rather than replacing it, calls it by the interface's
name. In the type's own `describe`, `Shape.describe(this)` is the default; anywhere else
`Shape.describe(x)` is the method `x`'s type has:

```lyric
import std.io { println };

interface Shape {
    fn area(): float;

    fn describe(): string {
        return f"a shape of area {this.area()}";
    }
}

struct Square :: [Shape] {
    side: float,

    fn area(): float {
        return this.side * this.side;
    }

    fn describe(): string {
        return Shape.describe(this) + f", a square of side {this.side}";
    }
}

fn main(): void {
    println(Square { side = 2.0 }.describe());
}
```

```output
a shape of area 4.0, a square of side 2.0
```

### Which one is it?

A `match` can ask an interface value for its type. `c: Circle` matches a circle and binds it as
one, with its fields:

```lyric
import std.io { println };

interface Shape {
    fn area(): float;
}

struct Circle :: [Shape] {
    r: float,

    fn area(): float {
        return 3.0 * this.r * this.r;
    }
}

struct Square :: [Shape] {
    side: float,

    fn area(): float {
        return this.side * this.side;
    }
}

fn kind(s: Shape): string {
    return match (s) {
        c: Circle => f"circle of radius {c.r}",
        _: Square => "square",
        _ => "something else",
    };
}

fn main(): void {
    println(kind(Circle { r = 1.0 }));
    println(kind(Square { side = 2.0 }));
}
```

```output
circle of radius 1.0
square
```

Asking a value its type is a fallback. Where each type should behave in its own way, a method of
the interface says it better: the next type added cannot forget to implement it.

## The library's interfaces

Much of the language works through interfaces of the standard library. A type that conforms to
`Display` is written by an f-string through its `show()`. `extend` adds a conformance to a type
after the fact — your own type, or one of the library's:

```lyric
import std.io { println };

struct Point :: [Equatable] {
    x: int,
    y: int,
}

extend Point :: [Display] {
    fn show(): string {
        return f"({this.x}, {this.y})";
    }
}

fn main(): void {
    let a = Point { x = 1, y = 2 };
    println(f"{a} {a == Point { x = 1, y = 2 }}");
}
```

```output
(1, 2) true
```

`Equatable` gives `==` and `!=`. For a struct whose fields are all equatable, naming the interface
is enough: the comparison is field by field, and nothing has to be written. `Ordered` gives `<` and
the other comparisons, `Hashable` makes a value a key of a `Map`, and `Closeable` is what
[`using let`](errors.md#cleaning-up) closes.

## Generic functions

A function takes type parameters in angle brackets. A **bound** after `::` says which interfaces
an argument's type must conform to, and only what those interfaces give can be used in the body:

```lyric
import std.io { println };

fn largest<T :: [Ordered]>(xs: T[]): T {
    var best = xs[0];
    for (x in xs) {
        if (x > best) {
            best = x;
        }
    }
    return best;
}

fn main(): void {
    println(f"{largest([3, 9, 2])} {largest(["pear", "apple"])}");
}
```

```output
9 pear
```

The call does not name `T`: the compiler finds it from the argument. The body is checked once,
against the bound — `>` is there because `Ordered` gives it — and the compiler then makes a copy of
the function for each type it is called with, so a generic call costs what a plain one costs.

## Generic types

A struct, a class, an enum or an interface can take type parameters too:

```lyric
import std.io { println };

struct Pair<A, B> {
    first: A,
    second: B,
}

fn main(): void {
    let p = Pair { first = 1, second = "one" };
    println(f"{p.first} {p.second}");
}
```

```output
1 one
```

`List<T>`, `Map<K, V>` and `Result<T, E>` of the library are generic types of this kind. There is no
relation between `Pair<int, string>` and `Pair<int8, string>`: they are two types, as `List<Circle>`
and `List<Shape>` are.

The next chapter, [Collections and iterators](collections.md), puts those library types to work.
