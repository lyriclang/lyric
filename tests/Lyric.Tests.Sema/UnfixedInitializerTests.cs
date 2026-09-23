using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// An initializer that fixes no type is <c>LYR-SEM0010</c>, at every depth.
///
/// <para>§7.1: <c>null</c> and <c>[]</c> carry no type of their own, so a binding without an
/// annotation needs one. The check used to ask the WRITTEN FORM — "is the initializer an empty
/// array literal" — and therefore only ever saw the outermost one. Six shapes got past it and died
/// in the lowering as an internal exception: a stack trace with no code, no position and no line
/// for a two-line program. The question is asked of the inferred TYPE now, which has no
/// outermost.</para>
///
/// <para>Each refusal is paired with the acceptance next to it. A check that refused everything
/// would pass the first half of this file on its own.</para>
/// </summary>
public class UnfixedInitializerTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Refuses(string source, string noun)
    {
        var de = Check(source);
        var sem0010 = de.Diagnostics.Where(d => d.Code == "LYR-SEM0010").ToList();
        Assert.True(sem0010.Count == 1,
            $"expected one LYR-SEM0010, got {de.Diagnostics.Count} diagnostic(s): "
            + string.Join(" | ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains($"'{noun}' fixes none on its own", sem0010[0].Message, StringComparison.Ordinal);
    }

    private static void Accepts(string source)
    {
        var de = Check(source);
        Assert.True(!de.HasErrors,
            "expected no error: " + string.Join(" | ", de.Diagnostics
                .Where(d => d.Severity == Severity.Error).Select(d => $"{d.Code}: {d.Message}")));
    }

    // ------------------------------------------------------------------ refused

    [Theory]
    [InlineData("let x = [];", "[]")]              // the one shape that was already caught
    [InlineData("let x = [[]];", "[]")]            // one level deeper, and it was an ICE
    [InlineData("let x = [[[]]];", "[]")]
    [InlineData("let x = ([], []);", "[]")]
    [InlineData("let x = (1, []);", "[]")]
    [InlineData("let x = null;", "null")]
    [InlineData("let x = [null];", "null")]
    public void A_local_whose_initializer_fixes_no_type_needs_an_annotation(string binding, string noun)
        => Refuses($"fn main(): int {{\n    {binding}\n    return 0;\n}}\n", noun);

    /// <summary>The same rule at module level, where the lowering died just as readily.</summary>
    [Theory]
    [InlineData("let g = [[]];", "[]")]
    [InlineData("let g = (1, []);", "[]")]
    [InlineData("let g = [null];", "null")]
    public void A_global_whose_initializer_fixes_no_type_needs_an_annotation(string binding, string noun)
        => Refuses($"{binding}\n\nfn main(): int {{\n    return 0;\n}}\n", noun);

    // ------------------------------------------------------------------ accepted

    [Theory]
    [InlineData("let x: int[] = [];")]
    [InlineData("let x: int[][] = [[]];")]
    [InlineData("let x: ?int = null;")]
    [InlineData("let x: (int, int[]) = (1, []);")]
    [InlineData("let x = [1];")]
    [InlineData("let x = [[1]];")]
    [InlineData("let x = (1, [2]);")]
    public void An_annotation_or_a_real_element_fixes_it(string binding)
        => Accepts($"fn main(): int {{\n    {binding}\n    return 0;\n}}\n");

    /// <summary>
    /// An initializer that already reported WHY it has no type is not spoken over.
    ///
    /// <para>The type test would fire on every error-typed initializer, and a second diagnostic
    /// guessing at "'[]' fixes none" on top of "unknown identifier" helps nobody. The report is
    /// conditioned on the initializer having stayed quiet, which is the invariant an
    /// <c>ErrorType</c> already carries: whoever sees one stays silent.</para>
    /// </summary>
    [Fact]
    public void An_initializer_that_already_failed_is_not_reported_twice()
    {
        var de = Check("fn main(): int {\n    let x = [nope()];\n    return 0;\n}\n");

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0002");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0010");
    }
}
