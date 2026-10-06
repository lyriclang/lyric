using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Closing a generator (design/v5/spec/06 A5; the review's M6-10, M8a-4).
///
/// <para>M6-10: <c>close()</c> unwinds the body with <c>Cancelled</c> and drops it. A
/// <c>defer</c> that threw on the way out was SUPPRESSED into that <c>Cancelled</c> — and
/// dropped with it, in silence. Now <c>close()</c> throws the first suppressed error; an early
/// <c>break</c> out of a <c>for</c> over such a generator throws it at the loop.</para>
///
/// <para>M8a-4: a <c>for</c> over a <c>Closeable</c> iterator closes it on every way out. Where
/// the iterator's type is a type parameter, the checked code did not know, and nothing closed:
/// a generic <c>first(it)</c> left the generator suspended, its <c>defer</c> never run. Now the
/// INSTANCE decides: each monomorphized loop closes where the concrete iterator is
/// <c>Closeable</c>. The rule at the declaration that makes it type-safe: a type that is both an
/// <c>Iterator</c> and <c>Closeable</c> throws in <c>close()</c> only what its <c>Error</c>
/// allows (<c>LYR-SEM0174</c>). The library's adapters — <c>map</c>, <c>filter</c>, <c>zip</c>,
/// … — are <c>Closeable</c> where their inner iterator is, and pass <c>close()</c> on.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class ClosingTests
{
    private const string Head = "import std.io { println };\n\n"
        + "class First :: [Error] { fn message(): string { return \"first\"; } }\n"
        + "class Second :: [Error] { fn message(): string { return \"second\"; } }\n"
        + "fn first(): void throws First { throw First { }; }\n"
        + "fn second(): void throws Second { throw Second { }; }\n\n";

    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string main, string code)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit != 0, "the program was taken");
        var codes = Regex.Matches(error, @"error\[(LYR-[A-Z]+\d+)\]").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(codes.Length == 1 && codes[0] == code, $"expected exactly one {code}, got:\n{error}");
        return error;
    }

    // ------------------------------------------------------------------ M6-10: the suppressed error

    [Fact]
    public void Close_throws_the_first_suppressed_error()
    {
        Assert.Equal("close threw first\n1\n", Output("""
            fn gen(): Coroutine<int> throws [First, Second] {
                defer try second();
                defer try first();
                yield 1;
                yield 2;
            }

            fn main(): void throws Error {
                let c = gen();
                let a = try c.next();
                try {
                    c.close();
                    println("closed quietly");
                } catch (e) {
                    println(f"close threw {e.message()}");
                }
                println(f"{a ?? 0}");
            }
            """));
    }

    [Fact]
    public void An_early_break_throws_the_defer_s_error_at_the_loop()
    {
        Assert.Equal("1\nthe loop threw first\n", Output("""
            fn gen(): Coroutine<int> throws First {
                defer try first();
                yield 1;
                yield 2;
            }

            fn main(): void throws Error {
                try {
                    for (x in gen()) {
                        println(f"{x}");
                        break;
                    }
                    println("left quietly");
                } catch (e) {
                    println(f"the loop threw {e.message()}");
                }
            }
            """));
    }

    // ------------------------------------------------------------------ M8a-4: the instance closes

    [Fact]
    public void A_generic_loop_closes_the_instance_s_iterator()
    {
        Assert.Equal("closed\nfirst 1\nafter\n", Output("""
            fn gen(): Coroutine<int> {
                defer println("closed");
                yield 1;
                yield 2;
                yield 3;
            }

            fn firstOf<I :: [Iterator<Item = int>]>(it: I): int throws I.Error {
                for (x in try it) {
                    return x;
                }
                return -1;
            }

            fn main(): void throws Error {
                let v = try firstOf(gen());
                println(f"first {v}");
                println("after");
            }
            """));
    }

    /// <summary>A generic loop over something that is NOT closeable closes nothing, and a
    /// generic loop that runs to the end closes once.</summary>
    [Fact]
    public void A_generic_loop_over_a_plain_iterator_and_one_that_runs_out()
    {
        Assert.Equal("sum 6\nclosed\nall 3\n", Output("""
            fn gen(): Coroutine<int> {
                defer println("closed");
                yield 1;
                yield 2;
                yield 3;
            }

            fn count<I :: [Iterator<Item = int>]>(it: I): int throws I.Error {
                var n = 0;
                for (x in try it) {
                    n += 1;
                }
                return n;
            }

            fn sum<I :: [Iterator<Item = int>]>(it: I): int throws I.Error {
                var s = 0;
                for (x in try it) {
                    s += x;
                }
                return s;
            }

            fn main(): void throws Error {
                println(f"sum {try sum([1, 2, 3].iter())}");
                println(f"all {try count(gen())}");
            }
            """));
    }

    [Theory]
    [InlineData("gen().map(x => x * 2)", "closed\n")]
    [InlineData("gen().filter(x => x > 1)", "closed\n")]
    [InlineData("gen().zip(other())", "closed\nother closed\n")]
    [InlineData("gen().take(5).enumerate()", "closed\n")]
    public void An_adapter_passes_close_on(string chain, string closed)
    {
        Assert.Equal(closed + "left\n", Output("""
            fn gen(): Coroutine<int> {
                defer println("closed");
                yield 1;
                yield 2;
                yield 3;
            }

            fn other(): Coroutine<int> {
                defer println("other closed");
                yield 10;
                yield 20;
            }

            fn main(): void {
                for (x in CHAIN) {
                    break;
                }
                println("left");
            }
            """.Replace("CHAIN", chain)));
    }

    // ------------------------------------------------------------------ the rule at the declaration

    /// <summary>A type that is an Iterator and Closeable throws in close() only what its Error
    /// allows: so a generic loop's close throws what the loop's next() throws, and the generic body
    /// covers it with the one clause.</summary>
    [Fact]
    public void A_closeable_iterator_s_close_throws_within_its_error() =>
        Refused("""
            struct Bad :: [Iterator, Closeable] {
                type Item = int;
                var n: int = 0,

                mut fn next(): ?int {
                    return null;
                }

                fn close(): void throws First {
                }
            }

            fn main(): void {
            }
            """, "LYR-SEM0174");

    [Fact]
    public void A_closeable_iterator_whose_close_throws_its_error_is_fine()
    {
        Assert.Equal("closing\n0\n", Output("""
            struct Fine :: [Iterator, Closeable] {
                type Item = int;
                type Error = First;
                var n: int = 0,

                mut fn next(): ?int throws First {
                    return null;
                }

                fn close(): void throws First {
                    println("closing");
                }
            }

            fn count<I :: [Iterator<Item = int>]>(it: I): int throws I.Error {
                var n = 0;
                for (x in try it) {
                    n += 1;
                }
                return n;
            }

            fn main(): void throws Error {
                println(f"{try count(Fine { })}");
            }
            """));
    }
}
