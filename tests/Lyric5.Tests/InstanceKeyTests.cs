using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// An instance is keyed by the display of its type arguments, so two types with one display are
/// one instance: one layout for both, the second one's values in the first one's shape.
/// <c>(?int)[]</c> and <c>?int[]</c> were both written <c>?int[]</c>; a function that returns a
/// throwing coroutine and a function that throws and returns a plain one both
/// <c>fn() -> Coroutine&lt;int&gt; throws E</c>.
/// </summary>
[Collection("console")]
public class InstanceKeyTests
{
    [Fact]
    public void An_array_of_optionals_and_an_optional_array_are_two_instances()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                struct Holder<T> { v: T }

                fn main(): void {
                    let a = Holder<(?int)[]> { v = [1, null, 3] };
                    let b = Holder<?int[]> { v = null };
                    let c = Holder<?int[]> { v = [4, 5] };
                    println(f"{a.v.length()} {b.v == null} {(c.v ?? []).length()}");
                }
                """));
        Assert.Equal("3 true 2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The same in the other order: whichever comes first made the instance.</summary>
    [Fact]
    public void In_the_other_order_too()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                struct Holder<T> { v: T }

                fn main(): void {
                    let b = Holder<?int[]> { v = null };
                    let a = Holder<(?int)[]> { v = [1, null, 3] };
                    println(f"{b.v == null} {a.v.length()} {a.v[1] == null}");
                }
                """));
        Assert.Equal("true 3 true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Two function types the source tells apart with parentheses (the review's M6-6)
    /// and the display did not. Their layouts are alike, so the program ran; the IR shows that
    /// it held one instance for the two.</summary>
    [Fact]
    public void A_function_returning_a_throwing_coroutine_and_one_that_throws_are_two_instances()
    {
        var dir = Package(("main.lyr", """
            enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }

            struct Holder<T> { v: T }

            fn numbers(): Coroutine<int> throws Oops { yield 1; throw Oops.Bad; }
            fn quiet(): Coroutine<int> { yield 1; }
            fn check(): void throws Oops { throw Oops.Bad; }
            // a factory: it returns a coroutine, and its own call throws. (A body that returned
            // no value would be a generator - the review's M6-3 - whatever the parentheses say.)
            fn refuse(): (Coroutine<int>) throws Oops { try check(); return quiet(); }

            fn main(): int {
                let a = Holder<fn() -> (Coroutine<int> throws Oops)> { v = numbers };
                let b = Holder<fn() -> (Coroutine<int>) throws Oops> { v = refuse };
                return 0;
            }
            """));
        var (exit, ir, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        var holders = ir.Split('\n').Where(line => line.StartsWith("struct ") && line.Contains(" Holder<")).ToArray();
        Assert.Equal(2, holders.Length);
        Assert.Contains(holders, h => h.Contains("Holder<fn() -> (Coroutine<int> throws main.Oops)>"));
        Assert.Contains(holders, h => h.Contains("Holder<fn() -> (Coroutine<int>) throws main.Oops>"));
    }
}
