using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// What a 'try' covers by where it stands (design/v5/spec/05 E4; the review's M5-9, M5-5): at the
/// start of an expression all of it; to the right of an operator what follows it, to the end of
/// the expression — a throwing call LEFT of it is not marked. A mark over what is marked already
/// is warned about.
/// </summary>
public class TryPlaceTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private const string Head = """
        import std.core { Exception };

        fn one(): int throws Exception { return 1; }
        fn two(): int throws Exception { return 2; }
        fn plain(): int { return 3; }
        fn maybe(): ?int { return null; }
        fn apply(g: fn() -> int throws Exception): int throws Exception { return try g(); }

        fn f(): int throws Exception {

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Head + body + "\n}\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Silent(string body)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void A_mark_right_of_an_operator_covers_what_follows_it()
    {
        Silent("    return plain() + try one() + two();");
        Silent("    return plain() * (plain() + try one() * two());");
    }

    [Fact]
    public void A_throwing_call_left_of_the_mark_is_not_marked()
    {
        var de = Check("    return one() + try two();");
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-SEM0138", error.Code);
        Assert.Contains("left of the 'try'", error.Message);
        Assert.Contains("try a() + b()", Assert.Single(error.Notes!).Message);
    }

    /// <summary>The cover ends with the expression the mark stands in: one in parentheses does
    /// not reach what stands outside them, nor an argument's the next argument.</summary>
    [Theory]
    [InlineData("    return (plain() + try one()) + two();")]
    [InlineData("    return max(plain() + try one(), two());\n}\nfn max(a: int, b: int): int { return if (a > b) a else b;")]
    public void The_cover_ends_with_the_expression_the_mark_stands_in(string body)
    {
        var error = Assert.Single(Check(body).Diagnostics);
        Assert.Equal("LYR-SEM0138", error.Code);
        Assert.Contains("'two'", error.Message);
    }

    [Fact]
    public void A_mark_right_of_an_operator_with_nothing_throwing_behind_it_warns()
    {
        var warning = Assert.Single(Check("    return plain() + try plain();").Diagnostics);
        Assert.Equal("LYR-SEM0139", warning.Code);
        Assert.Contains("nothing to the right", warning.Message);
    }

    /// <summary>A second mark behind the first, a mark under a marked expression, a mark in a
    /// 'try' block: each says nothing the other does not (M5-5, M5-9).</summary>
    [Theory]
    [InlineData("    return try one() + try two();")]
    [InlineData("    return plain() + try one() + try two();")]
    [InlineData("    try { return try one(); } catch (_: Exception) { return 0; }")]
    public void A_mark_over_what_is_marked_already_warns(string body)
    {
        var warning = Assert.Single(Check(body).Diagnostics);
        Assert.Equal("LYR-SEM0139", warning.Code);
        Assert.Contains("marks already", warning.Message);
    }

    /// <summary>A lambda's body is a function of its own: a mark to its left marks nothing in it,
    /// and a mark in it is no second one.</summary>
    [Fact]
    public void A_mark_does_not_reach_into_a_lambda() =>
        Silent("    return plain() + try two() + apply(() => try one());");

    [Fact]
    public void A_call_in_a_lambda_behind_a_mark_is_still_unmarked()
    {
        var error = Assert.Single(Check("    return plain() + try two() + apply(() => one());").Diagnostics);
        Assert.Equal("LYR-SEM0138", error.Code);
        Assert.Contains("'one'", error.Message);
        Assert.DoesNotContain("left of the 'try'", error.Message);
    }

    /// <summary>A value block to the right of the mark is part of the expression: its sites are
    /// marked, as they are under a mark at the start.</summary>
    [Fact]
    public void The_cover_goes_into_a_value_block_behind_the_mark() =>
        Silent("    return plain() + try one() + (maybe() ?? { let w = two(); w + 1 });");

    /// <summary>Without a body to throw from, the same holds for the hint: a global's
    /// initializer names the mark behind the unmarked call.</summary>
    [Fact]
    public void The_hint_is_given_outside_a_function_too()
    {
        var errors = Check("    return plain();\n}\nlet g: int = one() + try two();\nfn h(): int { return g;").Diagnostics
            .Where(d => d.Code == "LYR-SEM0138").ToList();
        Assert.Contains("left of the 'try'", Assert.Single(errors).Message);
    }
}
