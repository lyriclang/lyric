using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// A lambda's place parameter (design/v5/spec/03 T12; the review's M6-24): <c>&amp;</c> before the
/// parameter's name, in the parenthesized form and in a trailing block's parameter list —
/// <c>(&amp;n) =&gt; …</c>, <c>{ &amp;n =&gt; … }</c>. Not before a pattern (<c>LYR-PAR0058</c>), and
/// not in the bare form, where a leading <c>&amp;</c> is the mark of a place ARGUMENT.
/// </summary>
public class LambdaPlaceTests
{
    private static (Expr Expr, DiagnosticEngine De) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression(), de);
    }

    private static IEnumerable<Node> Nodes(Node root)
    {
        yield return root;
        foreach (var child in AstChildren.Of(root))
        foreach (var inner in Nodes(child))
            yield return inner;
    }

    private static LambdaExpr Lambda(string source)
    {
        var (expr, de) = Parse(source);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Nodes(expr).OfType<LambdaExpr>().Single();
    }

    [Theory]
    [InlineData("(&n) => n", "n", true)]
    [InlineData("(&n: int) => n", "n", true)]
    [InlineData("(a, &b) => a", "a,b", false, true)]
    [InlineData("(&a: int, &b: int) => a", "a,b", true, true)]
    [InlineData("f { &n => n }", "n", true)]
    [InlineData("f { &acc, x => acc }", "acc,x", true, false)]
    [InlineData("f(1) { x, &acc => acc }", "x,acc", false, true)]
    [InlineData("(n) => n", "n", false)]
    [InlineData("f { n => n }", "n", false)]
    public void The_mark_stands_before_the_parameters_name(string source, string names, params bool[] places)
    {
        var lambda = Lambda(source);
        Assert.Equal(names.Split(','), lambda.Parameters.Select(p => p.Name).ToArray());
        Assert.Equal(places, lambda.Parameters.Select(p => p.IsPlace).ToArray());
    }

    /// <summary>The parameter's span holds its mark; its name's span is the name.</summary>
    [Fact]
    public void A_place_parameters_span_begins_at_its_mark()
    {
        const string source = "(&count: int) => count";
        var parameter = Assert.Single(Lambda(source).Parameters);
        Assert.Equal("&count: int", source[parameter.Span.Start..parameter.Span.End]);
        Assert.Equal("count", source[parameter.NameSpan.Start..parameter.NameSpan.End]);
    }

    /// <summary>A pattern binds the values it takes apart; a place is one name for the caller's.
    /// One message — the pattern is read on as the parameter it is.</summary>
    [Theory]
    [InlineData("(&(a, b)) => a")]
    [InlineData("f { &(a, b) => a }")]
    public void The_mark_stands_before_no_pattern(string source)
    {
        var (expr, de) = Parse(source);
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0058", error.Code);
        Assert.Equal("&", source[error.Span.Start..error.Span.End]);
        Assert.NotNull(Assert.Single(Nodes(expr).OfType<LambdaExpr>().Single().Parameters).Pattern);
    }

    /// <summary>The control: '&amp;' at the start of an argument is the mark of a place argument,
    /// as it was — a lambda in parentheses behind it or not.</summary>
    [Fact]
    public void An_arguments_mark_is_no_parameters()
    {
        var (expr, de) = Parse("f(&x, (&n) => n)");
        Assert.False(de.HasErrors);
        var call = Assert.IsType<CallExpr>(expr);
        Assert.Equal(UnaryOp.Place, Assert.IsType<UnaryExpr>(call.Arguments[0]).Operator);
        Assert.True(Assert.Single(Assert.IsType<LambdaExpr>(call.Arguments[1]).Parameters).IsPlace);
    }
}
