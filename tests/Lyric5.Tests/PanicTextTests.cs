using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What a panic of the catalogue says (design/v5/spec/05 E8; the review's M5-14): its WORD
/// always in front — <c>assertion failed</c>, <c>unreachable</c>, <c>not implemented</c> —, an
/// <c>assert</c> the text of its condition, the program's own message behind. A message used to
/// REPLACE the word: <c>assert(n &gt; 2, "n is small")</c> panicked "n is small", and nothing
/// said that an assertion had failed or which.
///
/// <para><c>Exception("…")</c> and <c>Exception("…", cause: e)</c> (M5-2): the type had no
/// <c>new</c>, so the form every other type of the library has was refused.</para>
///
/// <para>A collection changed while it is walked panics with a code of its own,
/// <c>LYR-RT0016</c> (the review's C4); it was <c>panic</c>'s, <c>LYR-RT0008</c>.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class PanicTextTests
{
    private const string Head = "import std.io { println };\nimport std.collections { List, Map, Set, Deque };\n\n";

    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>The first line a program that builds writes to the error stream when it ends in
    /// a panic.</summary>
    private static string PanicLine(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 101, $"exit {ran.ExitCode}\n{ran.Stdout}\n{ran.Stderr}");
        return ran.Stderr.Replace("\r\n", "\n").Split('\n')[0];
    }

    // ------------------------------------------------------------------ the word in front

    [Theory]
    [InlineData("let n = 1;\n    assert(n > 2);", "panic [LYR-RT0011]: assertion failed: n > 2")]
    [InlineData("let n = 1;\n    assert(n > 2, \"n is small\");", "panic [LYR-RT0011]: assertion failed: n > 2: n is small")]
    [InlineData("let xs = [1, 2];\n    assert(xs.length() == 3 && xs[0] > 0, f\"got {xs.length()}\");", "panic [LYR-RT0011]: assertion failed: xs.length() == 3 && xs[0] > 0: got 2")]
    // what the call wrote, its own parentheses included (09 A11)
    [InlineData("let n = 1;\n    assert((n > 2), \"m\");", "panic [LYR-RT0011]: assertion failed: (n > 2): m")]
    [InlineData("todo();", "panic [LYR-RT0013]: not implemented")]
    [InlineData("todo(\"the parser\");", "panic [LYR-RT0013]: not implemented: the parser")]
    [InlineData("unreachable();", "panic [LYR-RT0012]: unreachable")]
    [InlineData("unreachable(\"no third state\");", "panic [LYR-RT0012]: unreachable: no third state")]
    // 'panic' says what the program wrote, and nothing else
    [InlineData("panic(\"as written\");", "panic [LYR-RT0008]: as written")]
    public void A_panic_of_the_catalogue_says_its_word_first(string statements, string line) =>
        Assert.Equal(line, PanicLine("fn main(): void {\n    " + statements + "\n}\n"));

    /// <summary>An assertion that holds costs its check and says nothing; its message is built
    /// before the check, as every argument is.</summary>
    [Fact]
    public void An_assertion_that_holds_says_nothing()
    {
        Assert.Equal("built\nheld\n", Output("""
            fn text(): string {
                println("built");
                return "never shown";
            }

            fn main(): void {
                let n = 3;
                assert(n > 2, text());
                println("held");
            }
            """));
    }

    // ------------------------------------------------------------------ Exception

    [Fact]
    public void An_exception_is_made_by_a_call()
    {
        Assert.Equal("outer <- boom | plain <- none | by its fields\n", Output("""
            fn fail(n: int): int throws Exception {
                if (n == 0) {
                    throw Exception("boom");
                }
                try {
                    return fail(n - 1);
                } catch (e) {
                    throw Exception("outer", cause: e);
                }
            }

            fn text(e: Error): string {
                return f"{e.message()} <- {e.cause()?.message() ?? "none"}";
            }

            fn main(): void {
                var first = "";
                try {
                    println(f"{fail(1)}");
                } catch (e) {
                    first = text(e);
                }
                let plain = Exception.new("plain");
                let literal = Exception { text = "by its fields" };
                println(f"{first} | {text(plain)} | {literal.message()}");
            }
            """));
    }

    // ------------------------------------------------------------------ changed while walked

    [Theory]
    [InlineData("let xs = List<int>.of([1, 2, 3]);\n    for (x in xs) {\n        xs.push(x);\n    }", "panic [LYR-RT0016]: List: changed while it was walked")]
    [InlineData("let m = Map<int, int>.new();\n    m.insert(1, 1);\n    for (k in m.keys()) {\n        m.insert(k + 10, 0);\n    }", "panic [LYR-RT0016]: Map: changed while it was walked")]
    [InlineData("let d = Deque<int>.new();\n    d.pushBack(1);\n    for (x in d) {\n        d.pushBack(x);\n    }", "panic [LYR-RT0016]: Deque: changed while it was walked")]
    [InlineData("let s = Set<int>.new();\n    s.insert(1);\n    for (x in s) {\n        s.insert(x + 10);\n    }", "panic [LYR-RT0016]: Set: changed while it was walked")]
    public void A_collection_changed_while_it_is_walked_has_its_own_code(string statements, string line) =>
        Assert.Equal(line, PanicLine("fn main(): void {\n    " + statements + "\n}\n"));
}
