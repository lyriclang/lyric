# Values and types

Every value in Lyric has a type, and the compiler knows it before the program runs. Most of the
time it finds the type itself; you write one where you want to say what you mean, or where there
is nothing to find it from. This chapter goes through the values a program starts with: numbers,
text, truth values, the absence of a value, and the ways to put several values together.

## Numbers

`int` is a whole number of 64 bits, and the type an integer literal has unless something asks
for another. `float` is a 64-bit floating-point number, the type of a literal with a point or an
exponent:

```lyric
import std.io { println };

let answer = 42;
let ratio = 7.0 / 2.0;
println(f"{answer} {ratio} {0.1 + 0.2}");
println(f"{int.max}");
```

```output
42 3.5 0.30000000000000004
9223372036854775807
```

`0.1 + 0.2` is not `0.3` in any language whose floats follow IEEE 754; Lyric prints the shortest
text that reads back as the same number, and hides nothing.

Integer division cuts towards zero, and `%` takes the sign of the left side:

```lyric
import std.io { println };

println(f"{7 / 2} {-7 / 2} {7 % 3} {-7 % 3}");
```

```output
3 -3 1 -1
```

Literals can be written with separators, in hexadecimal and in binary:

```lyric
import std.io { println };

println(f"{1_000_000} {0xff} {0b1010} {1e3}");
```

```output
1000000 255 10 1000.0
```

### The other sizes

Beside `int` and `float` there are the signed `int8`, `int16` and `int32`, the unsigned `uint8`,
`uint16`, `uint32` and `uint`, and `float32`. A literal takes one of them with a suffix — `3i8`,
`200u8`, `1.5f32` — or from the type it is written for. Each type knows its range:

```lyric
import std.io { println };

let small: int8 = 100;
println(f"{small} {int8.min} {int8.max} {uint8.max}");
```

```output
100 -128 127 255
```

Arithmetic stays in its type. A result that does not fit is not cut down to fit: the program
stops with a panic, in every build profile:

```lyric,no_run
import std.io { println };

let big: int8 = 127;
println(f"{big + 1}");
```

```text
panic [LYR-RT0002]: arithmetic overflow in '+'
```

### Converting

A smaller integer goes where a larger one is expected, because every value of it fits: an `int8`
is an `int` wherever an `int` is wanted. Nothing else converts by itself — not an integer into a
float, not a float into an integer:

```lyric,compile_fail=LYR-SEM0001
fn main(): void {
    let count = 3;
    let half: float = count / 2;
}
```

```diagnostic
error[LYR-SEM0001]: cannot assign 'int' to 'float'
 --> src/main.lyr:3:23
  |
3 |     let half: float = count / 2;
  |               -----   ^^^^^^^^^
  |               |
  |               expected 'float' here
```

`as` converts between any two number types, and says that you know what happens: a float loses
its fraction, an integer too large for the narrower type keeps its low bits:

```lyric
import std.io { println };

let count = 3;
let half = count as float / 2.0;
println(f"{half} {3.9 as int} {-3.9 as int} {300 as uint8}");
```

```output
1.5 3 -3 44
```

## Text

A `string` is text in UTF-8. Its `length()` counts bytes; its `chars()` walk its characters:

```lyric
import std.io { println };

let word = "héllo";
println(f"{word.length()} {word.chars().count()} {word.toUpper()}");
println(f"{word.contains("ll")} {word.replace("h", "j")}");
```

```output
6 5 HÉLLO
true jéllo
```

An **f-string** puts values into text between braces, each written as its type shows it. After a
colon, a hole says how: `.2f` for two decimals, `>5` and `<5` to fill a width, `x` for hexadecimal:

```lyric
import std.io { println };

let pi = 3.14159;
println(f"[{pi:.2f}] [{42:>5}] [{42:<5}] [{255:x}]");
```

```output
[3.14] [   42] [42   ] [ff]
```

A `char` is one Unicode scalar value, written in single quotes. It converts to and from its
number, a `uint32`, and to nothing else:

```lyric
import std.io { println };

let e = 'é';
println(f"{e} {e as uint32} {65u32 as char}");
```

```output
é 233 A
```

A `bool` is `true` or `false`; `&&`, `||` and `!` combine them, and the right side of `&&` and
`||` is evaluated only where the left one does not decide.

## Nothing: optional values

A value that may be missing has an **optional** type, written with a `?` in front: a `?int` holds
an `int` or `null`. A plain `int` is never `null` — the compiler does not let a `null` in — so a
missing value can only be where the type says it can be.

`??` gives the value, or the one on its right where there is none:

```lyric
import std.io { println };

let missing: ?int = null;
let present: ?int = 4;
println(f"{missing ?? 0} {present ?? 0}");
```

```output
0 4
```

A test against `null` narrows the value: inside the branch where it is not `null`, it is the plain
type, and the methods of that type are there:

```lyric
import std.io { println };

fn describe(name: ?string): string {
    if (name != null) {
        return f"{name.length()} letters";
    }
    return "no name";
}

fn main(): void {
    println(describe("Ada"));
    println(describe(null));
}
```

```output
3 letters
no name
```

`?.` calls a method only where there is a value and gives `null` otherwise, and `!` takes the
value out and stops the program with a panic where there is none — for the places where you know
better than the compiler.

## Several values at once

An **array** holds values of one type, in order. Its elements are read and written by index from
0; reading past its end is a panic. A `let` array keeps its length and its elements; a `var`
array's elements may change:

```lyric
import std.io { println };

var scores = [3, 1, 2];
scores[0] = 5;
println(f"{scores} {scores.length()} {scores[2]}");
```

```output
[5, 1, 2] 3 2
```

A `for` loop walks an array's elements:

```lyric
import std.io { println };

var total = 0;
for (score in [5, 1, 2]) {
    total += score;
}
println(f"{total}");
```

```output
8
```

An array has the length it was made with. A `List` grows as you push onto it — it comes with
the collections, later in the Book.

A **tuple** puts a few values of different types together without naming a type for them. Its
parts are `.0`, `.1` and on, and a `let` takes it apart:

```lyric
import std.io { println };

let pair = (1, "one");
let (number, word) = pair;
println(f"{pair.0} {pair.1} {number} {word}");
```

```output
1 one 1 one
```

A tuple is good for a function that gives back two things. Where the parts mean something, a
[struct](structs-classes-enums.md) gives them names.

## Writing a type down

A `let` or a `var` takes the type of its value. Write one after a colon where the value alone
does not say it — an empty array, a `null`, a literal meant to be a smaller number:

```lyric
import std.io { println };

let none: ?string = null;
let bytes: uint8[] = [];
let tiny: int16 = 7;
println(f"{none ?? "-"} {bytes.length()} {tiny}");
```

```output
- 0 7
```

A parameter and a function's result always carry their types: they are the function's promise to
its callers, read where the function is called, and the next chapter,
[Functions and closures](functions-and-closures.md), starts from them.
