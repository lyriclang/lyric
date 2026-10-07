using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The type arguments of a generic type, inferred, through the whole compiler (design/v5/spec/03
/// T8, the review's M8a-2): the instance the checker settled is the one that is built — the
/// program's own types, and the library's factories, which every program wrote with their
/// arguments until now (<c>Atomic&lt;int&gt;.new(1)</c>, <c>List&lt;int&gt;.new()</c>). Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ReceiverInferenceRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_programs_generic_types_are_built_without_their_lists()
    {
        Assert.Equal("3 x 7 true false 4 ab 2.5\n", Output("""
            import std.io { println };

            enum Opt<T> {
                Some(T), None;

                fn isSome(): bool { return match (this) { .Some(_) => true, .None => false }; }
            }

            struct Cell<T> {
                v: T,
                static fn of(v: T): Cell<T> { return Cell<T> { v = v }; }
                static fn turn<U>(v: T, f: fn(T) -> U): Cell<U> { return Cell<U> { v = f(v) }; }
                fn get(): T { return this.v; }
            }

            struct Pair<A, B> {
                first: A,
                second: B,
            }

            fn wrap<U>(u: U): Cell<U> { return Cell.of(u); }

            fn main(): void {
                let c = Cell.of(3);
                let p = Pair { first = "x", second = 7 };
                let some = Opt.Some(1);
                let none: Opt<int> = Opt.None;
                let wide: Cell<?int> = Cell.of(4);
                let turned = Cell.turn("a", (s: string) => f"{s}b");
                let half = Cell { v = 2.5 };
                println(f"{c.get()} {p.first} {p.second} {some.isSome()} {none.isSome()} {wide.get() ?? 0} {turned.get()} {wrap(half.get()).get()}");
            }
            """));
    }

    [Fact]
    public void The_librarys_factories_are_called_through_the_bare_name()
    {
        Assert.Equal("1 2 1 1 5 0\n", Output("""
            import std.io { println };
            import std.sync { Atomic };
            import std.sync { Mutex };

            fn main(): void throws Error {
                let a = Atomic.new(1);
                let xs: List<int> = List.new();
                xs.push(4);
                xs.push(5);
                let names: Map<string, int> = Map.new();
                names.insert("a", 1);
                let seen: Set<int> = Set.new();
                seen.insert(3);
                let m = Mutex.new(5);
                let held = try m.lock { &n => n };
                let none = List.of([1, 2]).length() - 2;
                println(f"{a.load()} {xs.length()} {names.length()} {seen.length()} {held} {none}");
            }
            """));
    }
}
