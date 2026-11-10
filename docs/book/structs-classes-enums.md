# Structs, classes and enums

A program's own types give its values a shape and its functions a home. Lyric has three kinds:
a **struct** is a value made of named fields, a **class** is an object that several places can
share, and an **enum** is a value that is one of a list of cases. Each can carry methods.

## Structs

A struct names its fields and their types. A value of it is written with the type's name and its
fields in braces; a field with a default may be left out:

```lyric
import std.io { println };

struct Point {
    x: int,
    y: int = 0,
}

fn main(): void {
    let p = Point { x = 3, y = 4 };
    let q = Point { x = 5 };
    println(f"{p.x} {p.y} {q.y}");
}
```

```output
3 4 0
```

A field is read-only unless it is declared `var`. `with` makes a copy with some fields changed,
and leaves the original as it was:

```lyric
import std.io { println };

struct Point {
    x: int,
    y: int,
}

fn main(): void {
    let p = Point { x = 1, y = 2 };
    let q = p with { y = 7 };
    println(f"{p.y} {q.y}");
}
```

```output
2 7
```

### Methods

Functions declared inside the type are its **methods**. A method reads the value it is called on
as `this`; a method that writes to `this` says so with `mut fn`, and only a `var` field can be
written. A `static fn` belongs to the type and has no `this` — the usual place for a function that
makes a value:

```lyric
import std.io { println };

struct Counter {
    var count: int,

    static fn starting(at: int): Counter {
        return Counter { count = at };
    }

    fn doubled(): int {
        return this.count * 2;
    }

    mut fn add(n: int): void {
        this.count += n;
    }
}

fn main(): void {
    var c = Counter.starting(at: 1);
    c.add(4);
    println(f"{c.count} {c.doubled()}");
}
```

```output
5 10
```

### A struct is a value

Assigning a struct, passing it to a function or storing it copies it: each place has its own.
Changing one copy leaves the others alone:

```lyric
import std.io { println };

struct Point {
    var x: int,

    mut fn moveBy(dx: int): void {
        this.x += dx;
    }
}

fn main(): void {
    let a = Point { x = 1 };
    var b = a;
    b.moveBy(10);
    println(f"{a.x} {b.x}");
}
```

```output
1 11
```

That is why `moveBy` needs `b` to be a `var`: it changes the value that `b` holds.

## Classes

A class looks like a struct, but its value is a reference to one object. Two names that hold the
same object see each other's changes:

```lyric
import std.io { println };

class Account {
    var balance: int,

    mut fn deposit(amount: int): void {
        this.balance += amount;
    }
}

fn main(): void {
    let mine = Account { balance = 10 };
    let shared = mine;
    shared.deposit(5);
    println(f"{mine.balance}");
}
```

```output
15
```

A `let` holding a class keeps naming the same object; the object itself can still change through
its `mut fn`s. Use a class for something that has an identity — an account, a connection, a node
that several others point to — and a struct for a value, like a point or a date. When in doubt, a
struct: copying it is cheap, and nobody changes it behind your back.

## Enums

An enum is a value that is exactly one of its **variants**. The simplest have no data:

```lyric
import std.io { println };

enum Color { Red, Green, Blue }

fn name(c: Color): string {
    return match (c) {
        .Red => "red",
        .Green => "green",
        .Blue => "blue",
    };
}

fn main(): void {
    println(name(Color.Green));
    println(name(.Blue));
}
```

```output
green
blue
```

Where the type is known, `.Blue` is enough: the compiler knows which enum it is. A `match` must
cover every variant; one that forgets a variant does not compile.

### Variants that carry data

A variant can carry values — by position in parentheses, or by name in braces. An enum's own
methods follow its variants, after a `;`:

```lyric
import std.io { println };

enum Shape {
    Circle(float),
    Rect { width: float, height: float },
    Empty;

    fn area(): float {
        return match (this) {
            .Circle(r) => 3.0 * r * r,
            .Rect { width, height } => width * height,
            .Empty => 0.0,
        };
    }
}

fn main(): void {
    let shapes = [Shape.Circle(1.0), Shape.Rect { width = 2.0, height = 3.0 }, Shape.Empty];
    for (s in shapes) {
        println(f"{s.area()}");
    }
}
```

```output
3.0
6.0
0.0
```

In a pattern, a variant is written with its dot. A bare name there is a new binding that matches
anything — the compiler says so where a variant of that name exists.

An enum is how Lyric says "one of these": a result that is a value or an error, a token of a
parser, a command a program reads. The next chapter, [Patterns](patterns.md), shows everything
`match` can take apart.
