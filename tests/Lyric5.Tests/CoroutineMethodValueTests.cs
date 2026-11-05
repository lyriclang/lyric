using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A coroutine's built-in members as values (spec 03 §8.4 rule 2, 10 §1 rule 5). They have no
/// symbol of their own, and each is a method all the same: a closure over the coroutine that does
/// what the call does. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class CoroutineMethodValueTests
{
    private const string Declarations = """
        import std.io { IoError, println };
        import std.task { Cancelled };

        fn gen(): Coroutine<int, string> { yield 1; yield 2; return "end"; }
        fn nothing(): Coroutine<void> { yield; }
        struct Boom :: [Error] { fn message(): string { return "boom"; } }
        fn failing(): Coroutine<int> throws [Boom] { yield 1; throw Boom { }; }
        fn one(): Coroutine<int> { yield 1; }
        fn failingAtClose(): Coroutine<int> throws [Boom] { try { yield 1; } catch (e: Cancelled) { throw Boom { }; } }
        fn asked<T>(c: Coroutine<T>): bool { let g = c.isDone; return g(); }

        """;

    private static string Output(string body)
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", Declarations + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string body)
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", Declarations + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));
        var (exit, _, err) = Run("check", "-C", dir);
        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("LYR-ICE", err);
        return err;
    }

    /// <summary>The value is the same coroutine: it pulls where a call pulls, and asks what a call asks.</summary>
    [Theory]
    [InlineData("let c = gen(); let g = c.next; println(g()!); println(g()!);", "1\n2\n")]
    // both the value and a call pull the one coroutine
    [InlineData("let c = gen(); let g = c.next; c.next(); println(g()!);", "2\n")]
    [InlineData("let c = nothing(); let g = c.next; println(g()); println(g());", "true\nfalse\n")]
    [InlineData("let c = gen(); let g = c.isDone; println(g()); c.next(); c.next(); c.next(); println(g());", "false\ntrue\n")]
    [InlineData("let c = gen(); let g = c.result; println(g() == null); c.next(); c.next(); c.next(); println(g()!);", "true\nend\n")]
    [InlineData("let c = gen(); c.next(); let g = c.close; g(); println(c.isDone());", "true\n")]
    // through the instance of a generic body
    [InlineData("println(asked(one()));", "false\n")]
    // a chain: the closure over the value there is
    [InlineData("let c: ?Coroutine<int, string> = gen(); let g = c?.isDone; println((g!)());", "false\n")]
    [InlineData("let c: ?Coroutine<int, string> = null; let g = c?.isDone; println(g == null);", "true\n")]
    public void Each_member_is_a_closure_over_the_coroutine(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    /// <summary>A pull throws at the call of the value, not where the value is named, and the set
    /// is in the value's type, so the mark and the coverage stand at the call.</summary>
    [Theory]
    [InlineData("let c = failing(); let g = c.next; try { println(g() ?? 0); println(g() ?? 0); } catch (e: Boom) { println(e.message()); }", "1\nboom\n")]
    [InlineData("let c = failingAtClose(); let g = c.close; try { c.next(); g(); println(0); } catch (e: Boom) { println(e.message()); }", "boom\n")]
    public void What_a_pull_throws_comes_out_of_the_values_call(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    [Theory]
    // the mark
    [InlineData("let c = failing(); let g = c.next; let n = g();", "LYR-SEM0138")]
    [InlineData("let c = failing(); let g = c.close; g();", "LYR-SEM0138")]
    // the set is part of the type
    [InlineData("let c = failing(); let g: fn() -> ?int = c.next;", "LYR-SEM0001")]
    // a coroutine of nothing has no result, as a value neither
    [InlineData("let c = nothing(); let g = c.result;", "LYR-SEM0012")]
    public void The_values_type_holds_what_the_call_would_say(string body, string code) =>
        Assert.Contains(code, Refused(body));

    /// <summary>Naming the value pulls nothing, so it needs no mark and no coverage.</summary>
    [Fact]
    public void Naming_a_throwing_pull_throws_nothing() =>
        Assert.Equal("false\n", Output("let c = failing(); let g = c.next; println(c.isDone());"));
}
