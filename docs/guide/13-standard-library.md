# The standard library

The standard library is written in Lyric and ships as source alongside the toolchain.

| Module | Contents |
|---|---|
| `std.core` | `panic`, `assert`, `todo`, `unreachable`, `Exception`, `Display`, `Hashable`, `Equatable`, `Ordered` |
| `std.string` | inspection, search, split, join, trim, pad, parsing, `StringBuilder` |
| `std.fmt` | number formatting, padding, alignment, tables |
| `std.math` | `sqrt`, `pi`, `abs`, `min`, `max`, rounding, trigonometry |
| `std.collections` | `List<T>`, `Map<K, V>`, `Set<T>`, `Indexable<T>`, sorting |
| `std.iter` | `Iterator<T>`, `Iterable<T>`, adapters, the entrances (`over`, `range`, `compact`), `sum` |
| `std.option` | `map`, `andThen`, `filter`, `zip`, `contains`, `toArray`, `iter`, `expect` |
| `std.io.console` | `print`, `println`, `readLine` — the writers take any `Display` value: `println(42)` |
| `std.io.error` | `IoError`, `IoErrorKind` — the reason an I/O operation failed; no capability |
| `std.io.file` | reading and writing files — requires `fileAccess` |
| `std.os` | environment, process, exit — requires `osAccess` |
| `std.random` | `Random.seeded`, `shuffle`, `choice`, `nextGaussian` — deterministic, no capability |
| `std.time` | `Instant`, `Duration`, ISO 8601 — requires `osAccess` |
| `std.json` | `JsonValue`, `parse`, `serialize`, `serializePretty` — JSON, RFC 8259 |
| `std.encoding` | `hexEncode`/`hexDecode`, `base64Encode`/`base64Decode` — RFC 4648 |
| `std.build` | `addExecutable` — only a `build.lyr` run by `lyric build` can use it |

## Whether, or why: the `OrThrow` twins

Since 3.7 a silent form whose failure carries a REASON has a throwing twin, suffixed `OrThrow`:
`text`/`textOrThrow`, `writeText`/`writeTextOrThrow`, `parse`/`parseOrThrow`, and so on through
`std.io.file`, `std.json`, `std.encoding` and `utf8Decode`. Both come from one implementation —
the twin throws a module-specific error type (`IoError`, `JsonError`, `EncodingError`,
`Utf8Error`) exactly where the silent form answers `null` or `false`. [Chapter 10](10-errors.md)
shows both shapes side by side. Where `null` is the whole truth — `env`, `Map.get`, `parseInt` —
the silent form stands alone. `listDir` is deprecated toward 4.0: it answers an empty array to
both "empty" and "unreadable", which `entries` (`?string[]`) and `entriesOrThrow` can finally
tell apart.

## Collections

`List<T>` grows; `T[]` does not.

```lyr
import std.io.console { println };
import std.collections { List };

fn main(): int {
    let items = List<string>.empty();
    items.push("first");
    items.push("second");

    for (item in items) {
        println(item);
    }

    let copy = items.toArray();
    return copy.length;
}
```

## Iteration

Anything implementing `Iterable<T>` works with `for-in`, including your own types.

```lyr
import std.io.console { println };
import std.iter { Iterator, Iterable };

class Countdown :: [Iterable<int>] {
    from: int,

    fn iter(): Iterator<int> {
        return CountdownIter { remaining = this.from };
    }
}

class CountdownIter :: [Iterator<int>] {
    remaining: int,

    mut fn next(): ?int {
        if (this.remaining <= 0) { return null; }
        let value = this.remaining;
        this.remaining = this.remaining - 1;
        return value;
    }
}

fn main(): int {
    var total = 0;
    for (n in Countdown { from = 3 }) {
        total = total + n;
    }
    println(f"{total}");
    return total;
}
```

An `Iterator<T>` yields `?T` and signals the end with `null`. `Iterable<T>` hands out a fresh
iterator per call, so two loops over the same collection do not interfere.

## One name per type, in the library

The standard library distinguishes numeric families by the type in the NAME. `std.math` carries
both side by side:

| float | int |
|---|---|
| `min`, `max` | `minInt`, `maxInt` |
| `clamp` | `clampInt` |
| `abs`, `sign` | `absInt`, `signInt` |

`clamp(index, 0, count - 1)` on three `int`s is therefore `cannot assign 'int' to 'float'`, and
the answer is `clampInt` rather than a conversion there and back. The same shape gave
`fromInt`/`fromFloat` and `parseInt`/`parseFloat` in `std.string` their names.

These names were chosen when the language had no overloading. **It has since 3.0** ([chapter
3](03-functions.md)), so `clamp` could take either family under one name — your own code may
certainly do that. Whether the library follows is a separate decision, and a breaking one: the
names above are what every 2.x program calls.

## Iterators chain

Since 2.17 the adapters are METHODS, so a pipeline reads left to right instead of inside out:

```lyr
import std.iter { collectArray };
import std.collections { List };

fn main(): int {
    let xs = List<int>.empty();
    xs.push(1);
    xs.push(2);
    xs.push(3);

    let out = collectArray<int>(
        xs.iter().map<int>((n: int) => n * 2).filter((n: int) => n > 2).take(2));

    return out.length;
}
```

`map`, `filter`, `take`, `skip`, `takeWhile`, `zip`, `chain` and `flatMap` are methods on
`Iterator<T>`; the free forms still work, warn, and go with 3.0.

Two families stay free, each for a reason worth knowing:

- **`sum`, `sumFloat`, `minValue`, `maxValue`** ask something of the ELEMENT type — that it is a
  number, that it is ordered — and an interface cannot require that of its own parameter.
- **`enumerate` and `chunks`** change the element type without being generic, and a method like
  that would ask the compiler to build `Iterator<(int, T)>`, then `Iterator<(int, (int, T))>`,
  without end. `map` and `flatMap` change it safely because they are generic: they are built per
  use rather than per instance.

## A chain has to start somewhere

A `List<T>` hands out a cursor with `iter()`, because it is a type and can declare
`Iterable<T>`. The three built-in forms cannot: an array and a string have no declaration to hang
a conformance on, and a range is not a value at all — `a..b` is a loop head, so
`(a..b).iter()` could not exist. Four functions are the beginning instead:

```lyr
import std.iter { over, range, rangeInclusive, compact, sum };

fn main(): int {
    let doubled = sum(over<int>([1, 2, 3]).map<int>((n: int) => n * 2));
    let counted = sum(range(1, 4));
    let upTo = sum(rangeInclusive(1, 4));

    let slots: (?int)[] = [10, null, 20];
    let present = sum(compact<int>(slots));

    return doubled + counted + upTo + present;
}
```

`over` walks an array, `range` and `rangeInclusive` walk the numbers that `low..high` and
`low..=high` walk in a loop, and `compact` walks the slots of an array of optionals that are not
`null`.

**`rangeInclusive` is its own function rather than `range(low, high + 1)`** for the reason the
two loop forms are separate: at the type's maximum that `+ 1` wraps, and the chain would yield
nothing at all.

**`compact` takes an ARRAY and not an iterator**, and that is not a convenience. An
`Iterator<?T>` cannot exist: `next()` answers `?T` and spends `null` on "the end", so an optional
element would need `??T` to be told apart from it — and `?` does not nest. The same rule is
why `for (x in slots)` over a `(?T)[]` is refused with `LYR-SEM0091`. Reading the slots directly
is what makes the distinction available, and `compact` is where that reading lives.

## Files answer two ways, and each says which

Since 2.14 `std.io.file` has exactly two shapes, and the choice between them is not a taste:

```lyr
import std.io.file as file;

fn main(): int {
    let path = "notes.txt";

    let content = file.text(path) ?? "";        // a READ answers ?T
    let saved = file.writeText(path, content);  // an OPERATION answers bool

    return if (saved) 0 else 1;
}
```

A **read** answers `?T`: `null` means the file could not be read, and an empty result means an
empty file. A **write, a remove, a copy** answers `bool` — whether it happened; it carries no
value, so nothing is lost by saying only that. A **predicate** (`exists`, `isFile`,
`isDirectory`) answers `bool` too, and there `false` is an answer rather than a failure.

Before 2.14 there were three shapes, and the third one lied: `readBytes` and `readLines` answered
an **empty array** both for an empty file and for a file that is not there. No caller could tell
those apart, and the advice was to ask `exists` first — which is a race, not an answer. The three
old names (`readText`, `readBytes`, `readLines`) warned through the 2.x line and went with 3.0;
`text`, `bytes` and `lines` are what is left.

What none of the shapes carries is a REASON: a missing file and a permission denied look the
same. That gap is known and left open on purpose — carrying reasons means an error type and a
decision about `throws` that this module should not make on its own.

## JSON is one value type

`std.json` reads and writes JSON (RFC 8259). A whole document is one `JsonValue` — an enum over
the six JSON shapes, with arrays as `List<JsonValue>` and objects as `Map<string, JsonValue>`,
the collections you already have rather than a second container API.

```lyr
import std.json { JsonValue, parse, serialize, serializePretty };
import std.io.console { println };
import std.collections { Map };

fn main(): int {
    let doc = parse("{\"name\": \"aria\", \"level\": 3}");
    if (doc == null) {
        return 1;
    }

    let name = doc.field("name");
    if (name != null) {
        println(name.asString() ?? "?");
    }

    let members = Map<string, JsonValue>.empty();
    members.set("saved", JsonValue.Bool(true));
    println(serializePretty(JsonValue.Obj(members), 2));
    println(serialize(doc));
    return 0;
}
```

`parse` answers `?JsonValue`, the standard library's absence convention: `null` means the text
is not JSON — strictly RFC 8259, so no comments, no trailing commas, no `NaN`, and nesting
deeper than 128 containers answers `null` rather than meeting the runtime's call-depth panic.
The accessors (`asString`, `asInt`, `asFloat`, `at`, `field`, …) answer `null` when the value
is not the asked shape, so a walk through a document is null checks — and `match` over the
variants is there when a walk wants all six.

Three number rules worth knowing. A number without fraction or exponent is `Int` and exact
across the whole `int` range — ids round-trip untouched; one beyond that range falls back to
`Float`, keeping magnitude over exactness. An integral `Float` serializes as `3.0`, not `3`, so
what was a float comes back as one. And NaN and the infinities have no JSON spelling; they
serialize as `null`, JavaScript's answer. Object member order follows the map — unspecified —
and duplicate keys in a document take the last value, as in JavaScript.

## Bytes become text and come back

`std.encoding` is the binary-to-text pair: `hexEncode`/`hexDecode` and
`base64Encode`/`base64Decode` (RFC 4648), over the same `uint8[]` that `utf8Encode` and
`file.bytes` speak.

```lyr
import std.encoding { base64Encode, base64Decode, hexEncode };
import std.io.console { println };
import std.string as strings;

fn main(): int {
    let bytes = "Grüße".utf8Encode();
    println(hexEncode(bytes));

    let packed = base64Encode(bytes);
    println(packed);

    let back = base64Decode(packed);
    if (back == null) {
        return 1;
    }
    println(strings.utf8Decode(back) ?? "?");
    return 0;
}
```

The direction convention is `utf8Encode`/`utf8Decode`'s: encoding cannot fail and answers the
text; decoding answers `?uint8[]`, and `null` means the text is not the encoding's canonical
form. Both decoders are strict — no whitespace, no missing padding, nothing outside the
alphabet — because a lenient decoder accepts what the next system rejects; strip decorations
before decoding.

## Capabilities

`std.io.file`, `std.os` and `std.time` require a capability. A standalone run grants
everything; a host grants explicitly, and a module that requires more than it is granted is
rejected before it runs.

## Strings have methods

Since v1.15 the string API is method-shaped: `s.trim()`, `s.split(",")`, `s.contains(x)`,
`s.length()`. The deprecated free forms (`trim(s)`, …) went with 2.0. `concat` and `repeat`
stay free — they back `+` and `*` — and the type-directed families (`fromInt`, `parseInt`, …)
keep their names.

The methods come with the module: any `import std.string { … }` makes them visible, and a file
that needs no free name imports the module under an alias, which avoids shadowing the builtin
type name:

```lyr
import std.string as strings;

fn main(): int {
    return "  Grüße  ".trim().length();
}
```

Two things worth knowing: `s.length()` is a call BECAUSE it costs O(n) — a property would
promise a stored answer this type does not have — and every method returns a NEW string;
`s.trim();` as a statement does nothing, because a Lyric string has nothing to mutate.

## Constructors live on the types

A container comes from its type: `List<int>.empty()`, `Map<string, int>.empty()`,
`Set<int>.empty()`, `StringBuilder.new()`, `Random.seeded(42)` — the latter from `std.random`
since v1.14, where `shuffle`, `choice` and `nextGaussian` live beside it. The old free
functions (`emptyList`, `emptyMap`, `emptySet`, `newRandom`) and the `std.math.Random` twin
went with 2.0.

## Editing the standard library

The toolchain reads the standard library from **beside its own binary** — a copy made at build
time — or from `LYRIC_STDLIB` when that is set. Editing the sources in the repository therefore
changes nothing until the next `dotnet build` refreshes the copies, and a published toolchain
froze its copy when it was packaged. When an edit "is not found", it is almost always this:
point `LYRIC_STDLIB` at the repository's `stdlib/` while working on it.

The library also holds itself accountable: every public item is documented (a test pins
completeness), every file is formatted and compiles without a single diagnostic, and
`stdlib-tests/` carries its behavioral tests — written in Lyric, run by `lyric test`.
