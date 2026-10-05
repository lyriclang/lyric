using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>f(xs...)</c> at run time (design/v5/spec/08; the review's A2): the spread array IS the
/// variadic parameter — no copy — and an array without the dots is one element. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class SpreadRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_spread_hands_the_array_on_and_elements_make_one()
    {
        Assert.Equal("6 15 15 0 0\n", Output("""
            import std.io { println };

            fn sum(base: int, xs: int...): int {
                var s = base;
                for (x in xs) { s = s + x; }
                return s;
            }

            fn logged(xs: int...): int { return sum(0, xs...); }

            fn main(): void {
                let five = [1, 2, 3, 4, 5];
                let none: int[] = [];
                println(f"{sum(1, 2, 3)} {logged(1, 2, 3, 4, 5)} {sum(0, five...)} {sum(0, none...)} {sum(0, []...)}");
            }
            """));
    }

    /// <summary>No copy: what the function writes into its parameter, the caller sees.</summary>
    [Fact]
    public void The_spread_array_is_the_parameter()
    {
        Assert.Equal("9 9 2 3\n", Output("""
            import std.io { println };

            fn first(xs: int...): int {
                xs[0] = 9;
                return xs[0];
            }

            fn main(): void {
                let xs = [1, 2, 3];
                let written = first(xs...);
                println(f"{written} {xs[0]} {xs[1]} {xs[2]}");
            }
            """));
    }

    /// <summary>A type parameter is bound by what the call says: the array as one element, or
    /// its elements.</summary>
    [Fact]
    public void A_generic_variadic_takes_an_array_as_one_element_or_spread()
    {
        Assert.Equal("1 2 3\n", Output("""
            import std.io { println };

            fn count<T>(xs: T...): int { return xs.length(); }

            fn main(): void {
                let words = ["p", "q"];
                println(f"{count(words)} {count(words...)} {count("a", "b", "c")}");
            }
            """));
    }

    /// <summary>A method, its qualified form (04 D2 R5) and a type's factory (04 D8) take a
    /// spread like a function: the calls the checker writes for the last two keep it.</summary>
    [Fact]
    public void A_method_a_qualified_call_and_a_factory_take_a_spread()
    {
        Assert.Equal("3 3 3\n", Output("""
            import std.io { println };

            interface Tally { fn total(xs: int...): int; }

            struct Counter :: [Tally] {
                seen: int,
                static fn new(xs: int...): Counter { return Counter { seen = xs.length() }; }
                fn total(xs: int...): int { return xs.length(); }
            }

            fn main(): void {
                let ys = [1, 2, 3];
                let c = Counter(ys...);
                println(f"{c.total(ys...)} {Tally.total(c, ys...)} {c.seen}");
            }
            """));
    }
}
