using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A coroutine's <c>throws</c> as a set (spec 10 §1 rule 6): a pull throws each type the clause
/// writes, as a call of such a function does, so a clause for each covers it — called, through a
/// value, in a loop, of a generator lambda. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class CoroutineThrownSetTests
{
    private const string Declarations = """
        import std.io { IoError, println };
        import std.task { Cancelled };

        struct A :: [Error] { fn message(): string { return "a"; } }
        struct B :: [Error] { fn message(): string { return "b"; } }
        fn two(): Coroutine<int> throws [A, B] { yield 1; throw B { }; }
        fn drain(c: Coroutine<int> throws): void throws { var n = try c.next(); while (n != null) { println(n); n = try c.next(); } }

        """;

    private static string Dir(string body) => Package(("lyric.toml", AppManifest),
        ("src/main.lyr", Declarations + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));

    private static string Output(string body)
    {
        var dir = Dir(body);
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string body)
    {
        var (exit, _, err) = Run("check", "-C", Dir(body));
        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("LYR-ICE", err);
        return err;
    }

    private const string Each = "catch (e: A) { println(\"A\"); } catch (e: B) { println(\"B\"); }";

    [Theory]
    [InlineData("let c = two(); try { println(c.next() ?? 0); println(c.next() ?? 0); } " + Each, "1\nB\n")]
    [InlineData("let c = two(); let g = c.next; try { println(g() ?? 0); println(g() ?? 0); } " + Each, "1\nB\n")]
    [InlineData("try { for (x in two()) { println(x); } } " + Each, "1\nB\n")]
    [InlineData("let g = () => { yield 1; if (false) { throw A { }; } throw B { }; }; let c = g(); try { println(c.next() ?? 0); println(c.next() ?? 0); } " + Each, "1\nB\n")]
    public void A_clause_for_each_type_covers_a_pull(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    /// <summary>One of the set left uncovered is named, not the root.</summary>
    [Fact]
    public void The_type_left_uncovered_is_named()
    {
        var err = Refused("let c = two(); try { println(c.next() ?? 0); } catch (e: A) { println(\"A\"); }");
        Assert.Contains("LYR-SEM0034", err);
        Assert.Contains("throws 'B'", err);
    }

    /// <summary>The set is in the type, as the diagnostics write it.</summary>
    [Fact]
    public void The_type_shows_the_set() =>
        Assert.Contains("'Coroutine<int> throws [A, B]'", Refused("let n: int = two();"));

    /// <summary>A coroutine fits where a set that covers its own is expected, never the other way.</summary>
    [Fact]
    public void A_covering_set_takes_it() =>
        Assert.Equal("1\ncaught\n", Output("try { drain(two()); } catch (e: Error) { println(\"caught\"); }"));

    [Fact]
    public void A_narrower_type_does_not() =>
        Assert.Contains("LYR-SEM0001", Refused("let c: Coroutine<int> throws A = two();"));
}
