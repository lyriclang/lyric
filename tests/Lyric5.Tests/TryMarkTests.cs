using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A mark over what is marked already, where it takes the library to say it (the review's M5-5):
/// the head of a loop in a <c>try</c> block. The block marks the loop's calls, so the head's mark
/// says nothing new — that is the one warning, and none comes about what the loop walks.
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class TryMarkTests
{
    private static string[] WarningsOf(string text)
    {
        var dir = Package(("main.lyr", text));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        return error.Split('\n').Where(line => line.Contains("warning[") && line.Contains("main.lyr")).ToArray();
    }

    private const string Head = """
        enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }
        fn one(): int throws Oops { return 1; }
        fn gen(): Coroutine<int> throws Oops { yield try one(); }


        """;

    [Fact]
    public void A_loop_heads_mark_in_a_try_block_is_a_second_mark()
    {
        var warning = Assert.Single(WarningsOf(Head + """
            fn main(): int {
                try {
                    for (x in try gen()) { if (x > 0) { return x; } }
                } catch (_: Oops) { return 0; }
                return 3;
            }
            """));
        Assert.Contains("LYR-SEM0139", warning);
        Assert.Contains("marks already", warning);
    }

    /// <summary>The control: without the head's mark the block says it all, and nothing warns;
    /// outside a block the head's mark is the mark, and nothing warns either.</summary>
    [Theory]
    [InlineData("fn main(): int {\n    try {\n        for (x in gen()) { if (x > 0) { return x; } }\n    } catch (_: Oops) { return 0; }\n    return 3;\n}\n")]
    [InlineData("fn main(): int throws Oops {\n    for (x in try gen()) { if (x > 0) { return x; } }\n    return 3;\n}\n")]
    public void One_mark_over_a_loop_is_silent(string main) => Assert.Empty(WarningsOf(Head + main));
}
