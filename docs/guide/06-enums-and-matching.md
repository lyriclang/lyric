# Enums and pattern matching

An enum lists alternatives. A variant may carry a payload, either positionally or by name.

```lyr
enum Shape {
    Circle(float),
    Rectangle(float, float),
    Triangle { a: float, b: float, c: float },
    Empty;

    fn corners(): int {
        return match (this) {
            Circle(r)       => 0,
            Rectangle(w, h) => 4,
            Triangle { a, b, c } => 3,
            Empty           => 0,
        };
    }
}

fn main(): int {
    let s = Shape.Rectangle(3.0, 4.0);
    return s.corners();
}
```

The `;` separates the variants from the methods.

## Constructing

```lyr
enum Shape {
    Circle(float),
    Triangle { a: float, b: float, c: float },
    Empty,
}

fn main(): int {
    let a = Shape.Circle(2.0);
    let b = Shape.Triangle { a = 3.0, b = 4.0, c = 5.0 };
    let c = Shape.Empty;
    return 0;
}
```

## Matching

`match` is exhaustive: every variant must be covered, or the last arm must be `_`.

```lyr
enum Signal { Red, Yellow, Green, }

fn wait(s: Signal): int {
    return match (s) {
        Red    => 60,
        Yellow => 5,
        Green  => 0,
    };
}

fn main(): int { return wait(Signal.Yellow); }
```

Arms may bind payloads, test literals, cover ranges, combine alternatives with `|`, and carry a
guard:

```lyr
fn classify(n: int): int {
    return match (n) {
        0          => 0,
        1 | 2 | 3  => 1,
        4..=9      => 2,
        x if x < 0 => -1,
        _          => 3,
    };
}

fn main(): int { return classify(7); }
```

An arm whose body is an expression ends with `,`. An arm whose body is a block may omit it, but a
block arm must `return` or `throw` — it contributes no value.

## Taking a struct apart

A `match` also destructures a struct or a class. It tests nothing — the value is already of that
type — so one arm covers the whole `match`, and the pattern is simply a list of the fields you
want:

```lyr
struct Point { x: int, y: int }

fn distanceSquared(p: Point): int {
    return match (p) {
        Point { x, y } => x * y + x * y,
    };
}

fn main(): int { return distanceSquared(Point { x = 3, y = 4 }); }
```

`Point { x, y }` binds each field to its own name. Write `Point { x = across, y = up }` to choose
other names, `_` in a field's place to skip it, and leave a field out entirely if you do not want
it — it is not read.

A field pattern only **binds**. Putting a test inside it — `Point { x = 3 }` — is refused, because
a pattern that otherwise never fails would suddenly have a way to.

A bound field is a **copy**, exactly as `let x = p.x;` is. Changing the original afterwards does
not change what the pattern bound.

## Matching an optional

A `?T` of an enum takes both its states in one `match`: `null` for the missing one, the variants
for the one that is there.

```lyr
enum Shape { Dot, Rect { w: int, h: int }, }

fn area(s: ?Shape): int {
    return match (s) {
        null                => -1,
        Shape.Dot           => 0,
        Shape.Rect { w, h } => w * h,
    };
}

fn main(): int { return area(Shape.Rect { w = 3, h = 4 }); }
```

Where you write the `null` arm makes no difference to which arm answers — the arms are tried in
the order you wrote them, as everywhere else.

## Generic enums

The type arguments belong to the enum and precede the variant:

```lyr
enum Result<T> {
    Ok(T),
    Failed { reason: string },
}

fn main(): int {
    let a = Result<int>.Ok(5);
    let b = Result<int>.Failed { reason = "empty" };

    let c: Result<int> = Result.Ok(7);      // taken from the annotation

    return match (a) {
        Ok(v) => v,
        Failed { reason } => 0,
    };
}
```

Where the expected type is known, the arguments may be omitted. In an argument position they
cannot; write them.
