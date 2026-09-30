using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>a..b</c> in a <c>for</c> head is the counted loop and no value; everywhere else it is a
/// value of std.core — <c>Range&lt;T&gt;</c>, or <c>RangeInclusive&lt;T&gt;</c> for <c>a..=b</c>
/// — holding its bounds (design/v5/spec/03 T13 A3). Lyric 4 refused the value (LYR-SEM0090).
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

    private static void AssertClean(string source)
    {
        var diagnostics = Check(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == Severity.Error));
    }

    [Fact]
    public void A_range_bound_to_a_let_is_a_value() =>
        AssertClean("import std.core { Range };\nfn main(): int { let r = 1..5; let s: Range<int> = r; return s.end - r.start; }");

    [Fact]
    public void An_inclusive_range_is_its_own_type()
    {
        AssertClean("import std.core { RangeInclusive };\nfn main(): int { let r: RangeInclusive<int> = 1..=5; return r.end; }");
        var d = Assert.Single(Check("import std.core { Range };\nfn main(): int { let r: Range<int> = 1..=5; return r.end; }"),
            x => x.Severity == Severity.Error);
        Assert.Equal("LYR-SEM0001", d.Code);
    }

    [Fact]
    public void A_range_inside_an_array_literal_is_a_value() =>
        AssertClean("fn main(): int { let xs = [1..3, 4..6]; return xs[1].start; }");

    [Fact]
    public void A_range_is_not_an_int()
    {
        var d = Assert.Single(Check("fn eat(x: int): int { return x; }\nfn main(): int { return eat(1..5); }"),
            x => x.Severity == Severity.Error);
        Assert.Equal("LYR-SEM0001", d.Code);
        Assert.Contains("'Range<int>'", d.Message);
    }

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
