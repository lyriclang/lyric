using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Nesting that goes too deep is a diagnostic, not a dead process (§12.4).
///
/// <para>Three shapes took the whole toolchain down, each in a different phase, and none of them
/// said a word first — `Stack overflow.` on stderr, no code, no position. They killed `lyrls` and
/// `lyrdbg` just as readily, because those run the same front end.</para>
///
/// <para>TWO CODES, BECAUSE THERE ARE TWO PHASES. §12.1 partitions the areas by pipeline stage,
/// and the split is not bookkeeping: the parse and the check fail on DIFFERENT programs. A chain
/// of `+` is read by a loop — shallow to parse — and builds a left-leaning tree the checker then
/// walks to its full depth. A wall of `[]` is the same story in the type parser.</para>
///
/// <para>The floor is pinned beside the ceiling. A limit that refused everything would satisfy the
/// first half of this file on its own, and the specification requires 128 to be accepted.</para>
/// </summary>
public class NestingLimitTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        if (!de.HasErrors) Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static string Parens(int depth) =>
        "fn main(): int {\n    return " + new string('(', depth) + "1" + new string(')', depth) + ";\n}\n";

    /// <summary>Deep parentheses exhaust the PARSE: it recurses once per level.</summary>
    [Fact]
    public void Parentheses_past_the_bound_are_a_parse_diagnostic()
    {
        var de = Check(Parens(5_000));
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0045");
    }

    /// <summary>
    /// A chain of <c>+</c> exhausts the CHECK and not the parse — which is why the two have their
    /// own codes. The parser reads this in a loop and never recurses.
    /// </summary>
    [Fact]
    public void A_long_operator_chain_is_a_check_diagnostic()
    {
        var de = Check("fn main(): int {\n    return " + string.Join("+", Enumerable.Repeat("1", 20_000)) + ";\n}\n");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0105");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-PAR0045");
    }

    /// <summary>A wall of <c>[]</c> is the same split, in the type parser.</summary>
    [Fact]
    public void A_deep_array_type_is_a_check_diagnostic()
    {
        var de = Check("fn main(): int {\n    let x: int" + string.Concat(Enumerable.Repeat("[]", 2_000))
                       + " = [];\n    return 0;\n}\n");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0105");
    }

    /// <summary>
    /// The floor §12.4 requires, and one level of ordinary code beside it.
    ///
    /// <para>Without this the limit could be set to 1 and everything above would still pass.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(128)]
    public void Nesting_up_to_the_specified_floor_is_accepted(int depth)
    {
        var de = Check(Parens(depth));
        Assert.False(de.HasErrors,
            string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    /// <summary>
    /// The bound is the implementation's and the message says so.
    ///
    /// <para>§12.4 leaves the depth unspecified on purpose — it belongs to a thread's stack, not
    /// to Lyric — so a reader who hits it must not go looking for the language rule that refused
    /// them. There is none.</para>
    /// </summary>
    [Fact]
    public void The_message_says_the_limit_is_this_implementations()
    {
        var de = Check(Parens(5_000));
        var deep = Assert.Single(de.Diagnostics.Where(d => d.Code == "LYR-PAR0045"));
        Assert.Contains("the language", deep.Message, StringComparison.Ordinal);
        Assert.Contains("this implementation does", deep.Message, StringComparison.Ordinal);
    }
}
