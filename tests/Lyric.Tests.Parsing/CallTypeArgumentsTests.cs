using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// A call's type arguments closed by '>>' (sweep after M10, open point 148): the lexer reads
/// <c>>></c> as one token, and the lookahead that tells type arguments from a comparison counted
/// only single closes, so <c>read&lt;List&lt;int&gt;&gt;()</c> was a chained comparison
/// (<c>LYR-PAR0059</c>) where <c>read&lt;List&lt;int&gt; &gt;()</c> parsed. A type annotation
/// split the token already.
/// </summary>
public class CallTypeArgumentsTests
{
    private static Expr Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var expr = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression();
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return expr;
    }

    [Fact]
    public void A_double_close_ends_a_calls_type_arguments()
    {
        var call = Assert.IsType<CallExpr>(Parse("read<List<int>>(text)"));
        var outer = Assert.IsType<NamedType>(Assert.Single(call.TypeArguments!));
        Assert.Single(outer.TypeArguments);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void A_double_close_ends_a_method_calls_type_arguments()
    {
        var call = Assert.IsType<CallExpr>(Parse("v.read<List<int>>()"));
        Assert.Single(call.TypeArguments!);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void A_shift_after_a_comparison_stays_one()
    {
        // 'a < b >> c' holds no call: the '<' is a comparison, the '>>' a shift
        var cmp = Assert.IsType<BinaryExpr>(Parse("a < b >> c"));
        Assert.IsType<BinaryExpr>(cmp.Right);
    }
}
