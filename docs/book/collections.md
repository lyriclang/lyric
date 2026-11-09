# Collections and iterators

An array holds a fixed number of values. The library's three collections grow and shrink: a
**List** keeps values in order, a **Map** finds a value by its key, a **Set** knows whether it holds
a value. All three are in the prelude, and all three are walked by **iterators** — the same small
set of steps for every collection, for a range, for the lines of a file.

## List

A `List<T>` is an array that grows. `push` adds at the end, `pop` takes from the end, and the rest
works as on an array — an index, a length, a `for` loop:

```lyric
import std.io { println };

let xs = List<int>.new();
xs.push(3);
xs.push(1);
xs.push(2);
println(f"{xs} {xs.length()} {xs[0]} {xs.contains(2)}");
xs.insert(0, 9);
let removed = xs.removeAt(1);
println(f"{xs} {removed}");
```

```output
[3, 1, 2] 3 3 true
[9, 1, 2] 3
```

`List.of([…])` makes a list from an array's elements. A `List` is a class: two names that hold one
list see the same elements, which is why a `let` list can still be pushed onto.

Sorting works on a **view** of the elements, the list's `asSlice()`, and sorts them in place:

```lyric
import std.io { println };

let xs = List.of([3, 1, 2]);
xs.asSlice().sort();
println(f"{xs}");
```

```output
[1, 2, 3]
```

## Map

A `Map<K, V>` holds a value for each key. Reading `map[key]` gives a `?V` — the key may not be
there —, so a read is usually followed by a `??`. Writing `null` to a key removes it:

```lyric
import std.io { println };

let ages = Map<string, int>.new();
ages["ada"] = 36;
ages["bob"] = 25;
println(f"{ages["ada"] ?? 0} {ages["zoe"] ?? -1} {ages.length()} {ages.containsKey("bob")}");
ages["bob"] = null;
println(f"{ages.length()}");
```

```output
36 -1 2 true
1
```

A key's type must be `Hashable` — the integers, `string`, and your own types that
conform. A map promises no order of its keys: a program that prints them sorts them first, as the
next example does.

## Set

A `Set<T>` holds each value once; `insert` of a value it holds already changes nothing:

```lyric
import std.io { println };

let seen = Set<string>.new();
seen.insert("a");
seen.insert("a");
seen.insert("b");
println(f"{seen.length()} {seen.contains("b")}");
```

```output
2 true
```

## Iterators

`iter()` gives an **iterator** over a collection, a range or an array: something that hands out one
value at a time. On it stand the **adapters**, which make another iterator — `map`, `filter`, `take`,
`skip`, `enumerate` —, and the **terminators**, which pull the values through and give a result —
`collect`, `sum`, `count`, `fold`, `any`, `all`, `find`, `firstWhere`, `maxBy`. Nothing happens until
a terminator pulls:

```lyric
import std.io { println };

let words = ["pear", "fig", "apple", "kiwi"];
let lengths = words.iter().map((w) => w.length()).collect<List<int>>();
let total = words.iter().map((w) => w.length()).sum();
let longest = words.iter().maxBy((w) => w.length());
let firstLong = words.iter().firstWhere((w) => w.length() > 3);
println(f"{lengths} {total} {longest ?? "-"} {firstLong ?? "-"}");
println(f"{words.iter().any((w) => w == "fig")} {words.iter().count()}");
```

```output
[4, 3, 5, 4] 16 apple pear
true 4
```

`maxBy` and `firstWhere` give an optional: an empty input has no answer. A range is walked the same
way, and `fold` carries a value through the walk:

```lyric
import std.io { println };

let evens = (1..=10).iter().filter((n) => n % 2 == 0).take(3).collect<List<int>>();
let sum = (1..=4).iter().fold(0, (acc, n) => acc + n);
println(f"{evens} {sum}");
```

```output
[2, 4, 6] 10
```

`enumerate` pairs each value with its place, and a `for` loop takes the pair apart:

```lyric
import std.io { println };

for ((i, w) in ["pear", "fig"].iter().enumerate()) {
    println(f"{i}: {w}");
}
```

```output
0: pear
1: fig
```

## Putting them together

A map from a word's length to the words of that length, printed in the order of the lengths —
`sorted()` collects an iterator into a sorted list. A list of text prints its elements in quotes,
as `Debug` writes them, so that an empty text or one with a comma in it can be told apart:

```lyric
import std.io { println };

let words = ["pear", "fig", "apple", "kiwi"];
let byLength = Map<int, List<string>>.new();
for (w in words) {
    let key = w.length();
    if (byLength[key] == null) {
        byLength[key] = List<string>.new();
    }
    byLength[key]!.push(w);
}
for (k in byLength.keys().sorted()) {
    println(f"{k}: {byLength[k]!}");
}
println(words.join(", "));
```

```output
3: ["fig"]
4: ["pear", "kiwi"]
5: ["apple"]
pear, fig, apple, kiwi
```

The `!` after `byLength[key]` says that the key is there: the line before put it there. `join`
writes an array's elements with a separator between them.

The next chapter, [Modules and packages](modules-and-packages.md), splits a program into files.
