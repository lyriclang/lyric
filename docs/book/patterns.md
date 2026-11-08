# Patterns

A **pattern** describes the shape of a value: this variant, that number, a point on the x axis, an
array of one element. `match` tries patterns one after the other and runs the first arm whose
pattern fits; a pattern can also bind parts of the value to names on the way. This chapter goes
through the forms a pattern takes and the places it can stand.

## Numbers, ranges and alternatives

A literal matches itself, a range matches its values, and `|` joins alternatives. `_` matches
anything. An arm may add a **guard** after `if`: the arm is taken only where the guard holds:

```lyric
import std.io { println };

fn size(n: int): string {
    return match (n) {
        0 => "none",
        1 | 2 | 3 => "few",
        4..=9 => "several",
        k if k < 0 => "negative",
        _ => "many",
    };
}

fn main(): void {
    println(f"{size(0)} {size(2)} {size(7)} {size(-3)} {size(50)}");
}
```

```output
none few several negative many
```

The arms are tried in the order they are written. `k` in the fourth arm is a **binding**: a bare
name matches every value and names it, here so that the guard can look at it.

`match` is an expression: it gives the value of the arm that ran. Every value must find an arm —
for numbers, that takes an `_` or a binding at the end; for an enum, an arm for each variant, as
the [previous chapter](structs-classes-enums.md) showed.

## Taking values apart

A pattern can look inside a value. A struct's pattern names its type and some of its fields —
a field with `= pattern` is tested, a field by its name alone is bound:

```lyric
import std.io { println };

struct Point {
    x: int,
    y: int,
}

fn where(p: Point): string {
    return match (p) {
        Point { x = 0, y = 0 } => "origin",
        Point { x = 0, y } => f"on the y axis at {y}",
        Point { x, y = 0 } => f"on the x axis at {x}",
        _ => "elsewhere",
    };
}

fn main(): void {
    println(where(Point { x = 0, y = 0 }));
    println(where(Point { x = 0, y = 5 }));
    println(where(Point { x = 2, y = 0 }));
    println(where(Point { x = 1, y = 1 }));
}
```

```output
origin
on the y axis at 5
on the x axis at 2
elsewhere
```

A tuple's pattern has a pattern for each part, and an array's pattern one for each element;
`..` stands for the elements in between, however many there are:

```lyric
import std.io { println };

fn ends(xs: int[]): string {
    return match (xs) {
        [] => "empty",
        [only] => f"just {only}",
        [first, .., last] => f"{first} to {last}",
    };
}

fn main(): void {
    println(f"{ends([])} / {ends([4])} / {ends([1, 2, 3])}");
    let pair = (3, "three");
    let said = match (pair) {
        (0, _) => "zero",
        (n, name) => f"{n} is {name}",
    };
    println(said);
}
```

```output
empty / just 4 / 1 to 3
3 is three
```

Patterns nest to any depth: a variant's payload, a field inside it, a literal inside that.

## Optional values

`null` is a pattern too. Against an optional, the arm after `null` sees the value itself:

```lyric
import std.io { println };

fn describe(maybe: ?int): string {
    return match (maybe) {
        null => "nothing",
        0 => "zero",
        v => f"got {v}",
    };
}

fn main(): void {
    println(f"{describe(null)}, {describe(0)}, {describe(5)}");
}
```

```output
nothing, zero, got 5
```

## Patterns outside `match`

Where only one shape matters, a whole `match` is more than needed. `if (let …)` runs its block
where the pattern fits, with the pattern's names bound inside:

```lyric
import std.io { println };

enum Token {
    Num(int),
    Op(char),
    End,
}

fn firstNumber(tokens: Token[]): int {
    for (t in tokens) {
        if (let .Num(n) = t) {
            return n;
        }
    }
    return -1;
}

fn main(): void {
    println(f"{firstNumber([Token.Op('+'), Token.Num(7), Token.End])}");
}
```

```output
7
```

`while (let …)` loops as long as the pattern fits. A `List`'s `pop` gives the last element, or
`null` once the list is empty, so this loop empties the list from the end:

```lyric
import std.io { println };

var stack = List.of([1, 2, 3]);
while (let top = stack.pop()) {
    println(f"pop {top}");
}
```

```output
pop 3
pop 2
pop 1
```

`let … else` binds the names where the pattern fits, and otherwise runs a block that must leave —
with `return`, `break`, `continue` or a `throw`. After it, the names are bound for the rest of the
function, which keeps the usual path at the left margin:

```lyric
import std.io { println };

enum Token {
    Num(int),
    End,
}

fn tens(t: Token): int {
    let .Num(n) = t else {
        return 0;
    };
    return n * 10;
}

fn main(): void {
    println(f"{tens(Token.Num(4))} {tens(.End)}");
}
```

```output
40 0
```

A plain `let` takes a pattern too, as long as it cannot fail: `let (a, b) = pair;` always fits a
pair. A pattern that might not fit needs the `else`, and the compiler says so where it is missing.

Errors — the other way a function can end — get a chapter of their own. The next one, [What the
compiler tells you](diagnostics.md), is about the messages you meet when a program is not right yet.
