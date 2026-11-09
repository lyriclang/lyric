# Tasks and coroutines

A program often waits: for a file, a socket, a timer, another part of itself. Lyric lets it wait
without blocking everything else. Two things do that, and they are different: a **task** runs at
the same time as the code that started it, a **coroutine** runs only when its caller asks it for the
next value.

## Coroutines

A function whose return type is `Coroutine<T>` may `yield` values. Calling it runs nothing yet; it
gives a coroutine, and each `next()` runs the body up to its next `yield` and returns that value —
or `null` once the body has run through:

```lyric
import std.io { println };

fn countdown(from: int): Coroutine<int> {
    var n = from;
    while (n > 0) {
        yield n;
        n -= 1;
    }
}

fn main(): void {
    let c = countdown(3);
    while (let n = c.next()) {
        println(n);
    }
    println("liftoff");
}
```

```output
3
2
1
liftoff
```

The local `n` survives each `yield`: the body continues where it paused. A coroutine is an
iterator, so `for` walks it too, and it may be endless — the caller decides how much it takes:

```lyric
import std.io { println };

fn naturals(): Coroutine<int> {
    var n = 0;
    while (true) {
        yield n;
        n += 1;
    }
}

fn main(): void {
    let squares = naturals().map((n) => n * n).take(5).collect<List<int>>();
    println(squares.asSlice().join(", "));
}
```

```output
0, 1, 4, 9, 16
```

A coroutine runs on its caller's thread, in its caller's time. Nothing happens in it between two
`next()` calls.

## Tasks

`spawn` from `std.task` starts a function as a task and returns its handle at once. The task runs
beside the code that started it; `await()` waits until it has ended and gives its result:

```lyric
import std.io { println };
import std.task { spawn };

fn slowSquare(n: int): int {
    return n * n;
}

fn main(): void throws Error {
    var a = spawn(() => slowSquare(6));
    var b = spawn(() => slowSquare(7));
    let first = try a.await();
    let second = try b.await();
    println(first + second);
}
```

```output
85
```

The tasks of a thread take turns: one runs until it waits — for a timer, a socket, a channel, a
child process —, and then another one runs. Waiting is what lets the others go, so a task that
computes for a long time without waiting holds the thread; `yieldNow()` gives it away on purpose.
Tasks are cheap — a server can have one per connection. For work that should
run on several cores at once, `std.thread` has threads and a pool.

A task's errors come back through `await()`. The type of the handle carries them — the lambda here
can throw a `ParseError`, so `await()` can too:

```lyric
import std.core { ParseError };
import std.io { println };
import std.task { spawn };

fn main(): void {
    var parsing = spawn(() => try int.parse("forty-two"));
    try {
        println(parsing.await());
    } catch (e: ParseError) {
        println(f"the task failed: {e.message()}");
    }
}
```

```output
the task failed: the text is not a value of the type
```

## Sleeping and timeouts

`sleep` waits for a `Duration`, and lets the other tasks run meanwhile. `withTimeout` runs a body
and gives up on it after a time, with a `TimedOut` error:

```lyric
import std.io { println };
import std.task { sleep, withTimeout, TimedOut };
import std.time { Duration };

fn main(): void throws Error {
    let quick = try withTimeout(Duration.ofSecs(5), () => {
        try sleep(Duration.ofMillis(10));
        return "in time";
    });
    println(quick);

    try {
        withTimeout(Duration.ofMillis(10), () => {
            try sleep(Duration.ofSecs(5));
        });
    } catch (_: TimedOut) {
        println("too slow");
    }
}
```

```output
in time
too slow
```

Giving up cancels the body: its waiting `sleep` throws `Cancelled`, the body unwinds — `using` and
`defer` run as usual — and only then does `withTimeout` return. A task can be cancelled from its
handle too, with `cancel()`. Cancelling is a request: it takes effect when the task next waits.

## Channels

Tasks talk through a `Channel<T>`. `send` puts a value in, `recv` takes the next one out — waiting
for it if there is none yet — and gives `null` once the channel is closed and empty:

```lyric
import std.io { println };
import std.task { spawn, Channel };

fn main(): void throws Error {
    let words = Channel<string>.new();
    var producer = spawn(() => {
        for (w in ["one", "two", "three"]) {
            try words.send(w);
        }
        words.close();
    });
    var count = 0;
    while (let w = try words.recv()) {
        count += w.length();
    }
    try producer.await();
    println(f"{count} letters");
}
```

```output
11 letters
```

A channel works between threads as well as between the tasks of one thread. `Select` waits on
several channels at once and takes whichever is ready first, with an optional timeout.

## Scopes

A task started with `spawn` lives on its own: if nothing awaits it, nothing notices how it ended. A
`TaskScope` ties tasks to a block. Its `close()` — run by `using` at the end of the block — waits for
every task it started, and the first that fails cancels the others and makes `close()` throw:

```lyric
import std.io { println };
import std.task { sleep, TaskScope };
import std.time { Duration };

fn main(): void throws Error {
    var total = 0;
    {
        using let scope = TaskScope.new();
        for (n in 1..=3) {
            scope.spawn(() => {
                try sleep(Duration.ofMillis(n * 5));
                total += n;
            });
        }
    }
    println(total);
}
```

```output
6
```

When the block ends, all three tasks have ended: none outlives the code that started it. That is
the shape to reach for when a piece of work splits into parts — a request that asks three services,
a directory read file by file — because an error in one part cannot be lost and no part keeps
running after the whole has given up.

The next chapter puts tasks to work on files, processes and the network: [Files, processes, the
network](files-processes-network.md).
