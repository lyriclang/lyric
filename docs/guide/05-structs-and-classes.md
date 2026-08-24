# Structs and classes

Both group named fields. They differ in what a binding holds.

- A **struct** is a value. Assigning it copies.
- A **class** is a reference. Assigning it shares.

```lyr
struct Point { x: int, y: int, }

class Counter { value: int, }

fn main(): int {
    var a = Point { x = 1, y = 2 };
    var b = a;
    b.x = 99;                       // a.x is still 1

    let first = Counter { value = 0 };
    let second = first;
    second.value = 5;               // first.value is now 5 as well

    return a.x + first.value;
}
```

A field may have a default, and an initializer may then leave it out:

```lyr
struct Config {
    retries: int = 3,
    verbose: bool = false,
}

fn main(): int {
    let c = Config { verbose = true };
    return c.retries;
}
```

## Who may reach a field

A field belongs to its module. Outside it, reading, writing and naming it in an initializer all
need `pub` on the field:

```lyr
pub struct Account {
    pub owner: string,
    balance: int,

    pub fn shows(): int { return this.balance; }
}

pub fn opened(owner: string): Account {
    return Account { owner = owner, balance = 0 };
}
```

Another module reads `owner` and calls `shows()`, and cannot touch `balance` — not to read it, and
not to write a value the type would never have produced. Inside this module everything is reachable
as before: the unit is the MODULE, not the type, so a helper beside a type needs no permission.

This arrived in 3.3, and until then fields had no visibility at all — they were the last member
kind without it, while types, functions, globals and constants all took `pub`. Through the 3.x
line an out-of-module reach is a warning; from 4.0 it is an error. If a field was meant to be read
from elsewhere, say so with `pub`; if it was not, the warning is pointing at a missing method.

The fields of an enum variant take no `pub` and stay visible everywhere: they are what `match`
reads, so a private one could not be matched at all.

## Methods

Members are separated by `,`. A method that writes to `this` is marked `mut`.

```lyr
class Counter {
    value: int,

    fn get(): int { return this.value; }

    mut fn increment(): void { this.value = this.value + 1; }
}

fn main(): int {
    let c = Counter { value = 0 };
    c.increment();
    c.increment();
    return c.get();
}
```

`mut` on a method of a struct means the receiver is written back to the caller's value.

## Constants on a type

`static let` attaches a constant to a type. `static fn` attaches a function that needs no
receiver.

```lyr
struct Vec2 {
    x: float,
    y: float,

    static let DIMENSIONS: int = 2;

    static fn zero(): Vec2 { return Vec2 { x = 0.0, y = 0.0 }; }

    fn sum(): float { return this.x + this.y; }
}

fn main(): int {
    let v = Vec2.zero();
    return Vec2.DIMENSIONS + v.sum() as int;
}
```

Constants are initialized in declaration order before `main` runs. An initializer may read a
constant declared before it, not one declared after.
