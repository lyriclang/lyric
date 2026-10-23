# A first program

This chapter writes a small calculator: it reads lines like `6 * 7` and prints what they come to.
On the way it meets values and functions, an enum, a struct, and how a function says that it can
fail.

## Values

A few statements are a program already. `let` names a value; an f-string puts values into text
between braces:

```lyric
import std.io { println };

let answer = 6 * 7;
println(f"6 * 7 = {answer}");
```

```output
6 * 7 = 42
```

`answer` is an `int`: the compiler knows it from `6 * 7`, so nothing has to say so. A `let` never
changes; `var` names a value that does:

```lyric
import std.io { println };

var total = 0;
for (n in 1..5) {
    total += n;
}
println(f"1 + 2 + 3 + 4 = {total}");
```

```output
1 + 2 + 3 + 4 = 10
```

`1..5` is the range from 1 up to 5, without the 5.

## Functions

A function names its parameters' types and the type it returns:

```lyric
import std.io { println };

fn add(a: int, b: int): int {
    return a + b;
}

fn main(): void {
    println(add(2, 3));
}
```

```output
5
```

`void` is the type of nothing: `main` returns no value. Its program declares a function of its
own, so it writes its `main` too.

## An enum for the operator

The calculator knows four operators. An **enum** is a type whose values are a list of cases, and
`match` takes a value apart case by case:

```lyric
import std.io { println };

enum Op { Add, Sub, Mul, Div }

fn apply(op: Op, a: int, b: int): int {
    return match (op) {
        .Add => a + b,
        .Sub => a - b,
        .Mul => a * b,
        .Div => a / b,
    };
}

fn main(): void {
    println(apply(Op.Mul, 6, 7));
    println(apply(.Sub, 6, 7));
}
```

```output
42
-1
```

A `match` names every case, or the compiler says which one it misses. Where the type is clear —
`apply` takes an `Op` — a case is written `.Sub` without the enum's name.

## A struct for the line

A line of input is three things: a number, an operator, a number. A **struct** holds them together,
and its fields are given by name:

```lyric
import std.io { println };

enum Op { Add, Sub, Mul, Div }

struct Expr {
    left: int,
    op: Op,
    right: int,
}

fn main(): void {
    let e = Expr { left = 6, op = .Mul, right = 7 };
    println(f"{e.left} and {e.right}");
}
```

```output
6 and 7
```

## Errors

Not every line is a sum: `2 ^ 8` has an operator the calculator does not know, `seven` is no number,
and `1 / 0` has no answer. A function that can fail says so in its signature with `throws`, and
names what it throws. An error is a type that conforms to `Error` — it has a `message()`:

```lyric
enum Op { Add, Sub, Mul, Div }

struct CalcError :: [Error] {
    text: string,

    fn message(): string {
        return this.text;
    }
}

fn opOf(word: StringView): Op throws CalcError {
    if (word == "+") { return .Add; }
    if (word == "-") { return .Sub; }
    if (word == "*") { return .Mul; }
    if (word == "/") { return .Div; }
    throw CalcError { text = f"unknown operator '{word}'" };
}
```

A call to a function that throws is marked with `try`: the error, if one comes, goes on to the
caller — whose own `throws` must name it. A call without the mark does not compile:

```lyric,compile_fail=LYR-SEM0138
import std.core { ParseError };

fn twice(text: string): int throws ParseError {
    return int.parse(text) * 2;
}

fn main(): void throws ParseError {
    let _ = try twice("21");
}
```

```diagnostic
error[LYR-SEM0138]: the call to 'parse' throws 'ParseError' — mark it 'try', so the propagation is seen where it happens
 --> src/main.lyr:4:12
  |
4 |     return int.parse(text) * 2;
  |            ^^^^^^^^^^^^^^^
```

`main` marks its call to `twice`, but line 4 does not mark the call to `int.parse`: it throws a
`ParseError` for text that is no number, and `twice`'s `throws` lets the error go on — yet the
compiler wants to see, at the call itself, that it may. The fix is the missing `try` in front of
`int.parse`; with the mark in both places the program compiles:

```lyric
import std.core { ParseError };
import std.io { println };

fn twice(text: string): int throws ParseError {
    return try int.parse(text) * 2;
}

fn main(): void throws ParseError {
    println(try twice("21"));
}
```

```output
42
```

`try … catch` handles an error where it happens instead.

Here is the whole calculator, run over four lines:

```lyric
import std.core { ParseError };
import std.io { println };

enum Op { Add, Sub, Mul, Div }

struct Expr {
    left: int,
    op: Op,
    right: int,
}

struct CalcError :: [Error] {
    text: string,

    fn message(): string {
        return this.text;
    }
}

fn opOf(word: StringView): Op throws CalcError {
    if (word == "+") { return .Add; }
    if (word == "-") { return .Sub; }
    if (word == "*") { return .Mul; }
    if (word == "/") { return .Div; }
    throw CalcError { text = f"unknown operator '{word}'" };
}

fn parse(line: string): Expr throws [CalcError, ParseError] {
    let words = line.split(' ').toArray();
    if (words.length() != 3) {
        throw CalcError { text = "write: number operator number" };
    }
    return Expr {
        left = try int.parse(words[0]),
        op = try opOf(words[1]),
        right = try int.parse(words[2]),
    };
}

fn eval(e: Expr): int throws CalcError {
    return match (e.op) {
        .Add => e.left + e.right,
        .Sub => e.left - e.right,
        .Mul => e.left * e.right,
        .Div => if (e.right == 0) throw CalcError { text = "division by zero" } else e.left / e.right,
    };
}

fn main(): void {
    for (line in ["6 * 7", "1 / 0", "2 ^ 8", "seven"]) {
        try {
            let e = parse(line);
            println(f"{line} = {eval(e)}");
        } catch (err) {
            println(f"{line}: {err.message()}");
        }
    }
}
```

```output
6 * 7 = 42
1 / 0: division by zero
2 ^ 8: unknown operator '^'
seven: write: number operator number
```

`parse` throws two kinds of error, so its `throws` lists both. Inside the `try` block every call
that throws is covered, and `catch (err)` takes whichever comes; `err.message()` says what it was.
`throw` is an expression too: the `.Div` arm is a value or an error.

## Reading input

The last step reads the lines from the standard input instead of an array. `stdin().lines()` gives
them one by one until the input ends; reading can fail, so the loop is marked `try` too:

```lyric,no_run
import std.io { println, stdin, IoError };

fn main(): void throws IoError {
    for (line in try stdin().lines()) {
        println(f"read: {line}");
    }
}
```

Put the calculator's `main` loop over `stdin().lines()` and it answers what you type:

```text
$ lyric run
6 * 7
6 * 7 = 42
```

The next chapter, [What the compiler tells you](diagnostics.md), is about the messages you meet
when a program is not right yet.
