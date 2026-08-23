# Coroutines

A function whose body contains `yield` is a coroutine. Its return type is `Coroutine<T>`, where
`T` is what it yields. Calling it does not run the body; it produces a coroutine value.

```lyr
import std.io.console { println };

fn counter(): Coroutine<int> {
    var n = 0;
    while (true) {
        yield n;
        n += 1;
    }
}

fn main(): int {
    let c = counter();

    var sum = 0;
    var i = 0;
    while (i < 5) {
        sum = sum + resume c;
        i += 1;
    }

    println(f"sum: {sum}");
    return sum;
}
```

`resume` runs the coroutine until its next `yield` and produces that value. The state between two
`yield`s — locals and the position in the loop — survives. Without that, the loop above would read
`0` five times.

A coroutine may also be finite. When its body runs to the end, it is exhausted:

```lyr
import std.io.console { println };

fn three(): Coroutine<int> {
    yield 10;
    yield 20;
    yield 30;
}

fn main(): int {
    let t = three();
    let a = resume t;
    let b = resume t;

    println(f"{a}, {b}");
    return a + b;
}
```

`resume` on an exhausted coroutine is a panic. A caller either knows how many values there are, or
uses an infinite coroutine and stops itself — or pulls with `next()`:

```lyr
import std.io.console { println };

fn three(): Coroutine<int> {
    yield 10;
    yield 20;
    yield 30;
}

fn main(): int {
    let co = three();
    var sum = 0;
    var live = true;
    while (live) {
        let v = co.next();
        if (v == null) {
            live = false;
        } else {
            sum += v;
        }
    }
    println(f"sum: {sum}");
    return sum;
}
```

`co.next()` is the safe form of the same pull: it advances the coroutine exactly like `resume`
and answers `?T` — the value, or `null` once the body has run to its end. After the end it stays
`null` on every further call; `resume` on the same coroutine still panics, because leniency
belongs to the call, not to the state. The name and shape are `Iterator<T>.next()`'s on purpose.

Two yield types change the answer's form, for the same reason: a `Coroutine<void>` has no value
to wrap, so its `next()` returns `bool` — did it advance? — and `while (p.next()) { }` drives it
to the end. A `Coroutine<?T>` refuses `next()` outright (`LYR-SEM0080`): a `null` there would
mean both "yielded null" and "done", so such a coroutine is driven with `resume` and a protocol
of its own.

A coroutine may also end itself early with a bare `return;` — the next pull is then the panic or
the `null`, exactly as if the body had run through.

A coroutine is an ordinary value: it can be a parameter, a local, a field of a class or struct,
or a type argument — a driver that steps a stored `List<Coroutine<float>>` every frame holds
them like anything else. Copying the value copies a reference to the same suspended state; two
holders drive one coroutine.

## When the body throws

A coroutine body may `throw`, and the exception comes out of the `resume` or `next()` that was
running it — not out of the call that made the coroutine, which runs no body at all. It lands in
the function driving the pull, where a `try` catches it like any other:

```lyr
import std.core { Exception };
import std.io.console { println };

fn steps(): Coroutine<int> throws Exception {
    yield 1;
    throw Exception { text = "the second step failed" };
}

fn main(): int {
    try {
        let co = steps();
        println(f"{resume co}");
        println(f"{resume co}");
    } catch (e: Exception) {
        println(e.message());
    }
    return 0;
}
```

**The throwability is part of the type.** `steps()` above does not produce a `Coroutine<int>`
but a `Coroutine<int> throws Exception`, and the two are different types. The call demands
nothing — it builds a suspended frame and runs no body — while every **pull** of that value is a
throw site: handled by a `try` around it, or declared by the function doing the pulling.

That is what lets it survive being stored. A driver holding coroutines — a cutscene runner, a
task list — writes the throwability into the field, and the compiler keeps asking at every pull:

```lyr
import std.core { Exception };
import std.io.console { println };

fn steps(): Coroutine<int> throws Exception {
    yield 1;
    throw Exception { text = "the second step failed" };
}

class Runner {
    current: ?Coroutine<int> throws Exception = null,

    fn load() {
        this.current = steps();
    }

    fn step(): bool throws Exception {
        let co = this.current;
        if (co == null) { return false; }
        println(f"{resume co}");   // demands handling, so this method declares 'throws'
        return true;
    }
}

fn main(): int {
    let r = Runner { };
    r.load();
    try {
        while (r.step()) { }
    } catch (e: Exception) {
        println(e.message());
    }
    return 0;
}
```

Write `throws` alone (`Coroutine<int> throws`) when the body may throw anything. A coroutine that
cannot throw fits a slot declared `throws` — the slot promises a handler and a value that never
throws keeps that promise — but not the other way round: a throwing coroutine in a plain
`Coroutine<int>` field would be a pull nobody was asked to handle.

`next()` is no way around it: it is lenient about the END of a body, not about a body that
throws, and an exception passes straight through it.

*Before 3.0 the demand was checked at the CALL, which is the one event that cannot throw. It
looked right as long as the coroutine stayed a local beside its `try`, and vanished the moment it
reached a field or an optional — where an exception ended the program with exit code 101.*

Send values (`resume c, v`) do not exist.
