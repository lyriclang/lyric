using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>a..b</c> is a loop head, not a value.
///
/// <para>The grammar says so by omission: <c>..</c> occurs in a <c>match</c> pattern and in the
/// iterable of a <c>for-in</c>, and there is no range expression among the primaries.
/// <c>RangeOf</c> agrees in its own comment — "the internal type of 0..9, not a spec type" — and
/// nothing in the lowering can represent one.</para>
///
/// <para>It was nevertheless accepted wherever the type was INFERRED, and crashed the compiler
/// there: <c>let r = 1..5;</c> and <c>[1..3]</c> both reached <c>TypeLowering.Lower</c> and threw
/// an internal exception with a stack trace and no source position. Where the type was written
/// down the assignment had refused it all along, which is why this went unnoticed.</para>
/// </summary>
public class RangePositionTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics;
    }

    private static void AssertOutOfPosition(string source)
    {
        var diagnostics = Check(source);
        var diagnostic = Assert.Single(diagnostics, d => d.Code == "LYR-SEM0090");
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Span.File.IsValid, "the message has no source position");

        // One message, not two. The range answers ErrorType, so whatever it was handed to says
        // nothing further — 'cannot assign range<int> to int' would name a type the language does
        // not have.
        Assert.Single(diagnostics);
    }

    [Fact]
    public void A_range_bound_to_a_let_is_refused() =>
        AssertOutOfPosition("fn main(): int { let r = 1..5; return 0; }");

    [Fact]
    public void A_range_inside_an_array_literal_is_refused() =>
        AssertOutOfPosition("fn main(): int { let xs = [1..3]; return 0; }");

    [Fact]
    public void A_range_passed_as_an_argument_is_refused() =>
        AssertOutOfPosition(
            "fn eat(x: int): int { return x; }\nfn main(): int { return eat(1..5); }");

    [Fact]
    public void A_range_in_a_for_head_is_fine() =>
        Assert.Empty(Check("fn main(): int { var n = 0; for (i in 1..5) { n = n + i; } return n; }"));

    /// <summary>Parentheses are folded by the parser, so the iterable is still the range node
    /// itself — the in-position test is identity, and this is what keeps it honest.</summary>
    [Fact]
    public void A_parenthesised_range_in_a_for_head_is_fine() =>
        Assert.Empty(Check("fn main(): int { var n = 0; for (i in (1..5)) { n = n + i; } return n; }"));

    /// <summary>A nested loop restores the outer permission on the way out; without that, a range
    /// after an inner loop would be refused in a head where it belongs.</summary>
    [Fact]
    public void A_range_after_a_nested_loop_is_still_fine() =>
        Assert.Empty(Check("""
            fn main(): int {
                var n = 0;
                for (i in 0..2) {
                    for (j in 0..2) { n = n + j + i; }
                }
                for (k in 0..3) { n = n + k; }
                return n;
            }
            """));

    /// <summary>The bounds keep their own message: a range that is out of position AND malformed
    /// is two different mistakes, and the one about the bounds is the one a reader can act on.
    /// </summary>
    [Fact]
    public void Bad_bounds_are_still_reported()
    {
        var diagnostics = Check("fn main(): int { let r = 1..\"five\"; return 0; }");
        Assert.Contains(diagnostics, d => d.Code == "LYR-SEM0003");
    }
}
