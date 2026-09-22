# Control flow

## Conditions

`if` as a statement takes blocks:

```lyr
import std.io.console { println };

fn main(): int {
    let n = 7;

    if (n > 10) {
        println("large");
    } else if (n > 5) {
        println("medium");
    } else {
        println("small");
    }
    return 0;
}
```

`if` as an expression takes expressions, and `else` is mandatory:

```lyr
fn main(): int {
    let n = 7;
    let bonus = if (n > 5) 10 else 0;
    return bonus;
}
```

## Loops

```lyr
import std.io.console { println };

fn main(): int {
    var i = 0;
    while (i < 3) {
        println(f"while {i}");
        i = i + 1;
    }

    do {
        println("runs at least once");
        i = i + 1;
    } while (i < 3);

    for (n in 0..3) {
        println(f"for {n}");
    }

    for (n in 0..=3) {
        println(f"inclusive {n}");
    }
    return 0;
}
```

`for-in` walks a range, an array, or anything that implements `Iterable<T>`:

```lyr
import std.io.console { println };
import std.collections { List };

fn main(): int {
    let names = ["Ada", "Grace"];
    for (name in names) {
        println(name);
    }

    let numbers = List<int>.empty();
    numbers.push(1);
    numbers.push(2);
    for (n in numbers) {
        println(f"{n}");
    }
    return 0;
}
```

`break` leaves the innermost loop, `continue` starts its next iteration.

A loop may carry a **label**, and `break` or `continue` may name it to reach past the innermost
loop:

```lyr
fn find(grid: int[][], target: int): (int, int) {
    var found = (-1, -1);
    outer: for (i in 0..grid.length) {
        for (j in 0..grid[i].length) {
            if (grid[i][j] == target) {
                found = (i, j);
                break outer;
            }
        }
    }
    return found;
}
```

`continue outer` abandons the rest of the inner loop and starts the outer loop's next
iteration. A label is visible only inside the loop it marks and shares nothing with variables;
one nothing jumps to is a warning, and a label repeating an enclosing one is refused. Every
`defer` between the jump and the loop it names runs on the way out, innermost first.

## Deferred work

`defer` schedules a statement to run when the surrounding scope ends, whichever way it ends.

```lyr
import std.io.console { println };

fn main(): int {
    defer println("cleanup runs last");

    println("work");
    return 0;
}
```

Deferred statements run in reverse order of registration.
