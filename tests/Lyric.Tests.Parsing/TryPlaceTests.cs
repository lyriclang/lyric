using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Where a 'try' may stand (design/v5/spec/05 E4, 08 Y4; the review's M5-9). The plain mark
/// stands at the start of an expression and covers it, or to the right of an operator and
/// covers from there to the expression's end — without changing how the expression is grouped.
/// 'try?', 'try!' and a 'try' with clauses are worth something else than what they cover and
/// stand at its start (PAR0050).
/// </summary>
public class TryPlaceTests
{
    private static (Expr Expr, DiagnosticEngine De, string Source) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression(), de, source);
    }

    private static Expr Clean(string source)
    {
        var (expr, de, _) = Parse(source);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return expr;
    }

    private static TryExpr Mark(Expr root) => Nodes(root).OfType<TryExpr>().Single();

    private static IEnumerable<Node> Nodes(Node root)
    {
        yield return root;
        foreach (var child in AstChildren.Of(root))
        foreach (var inner in Nodes(child))
            yield return inner;
    }

    [Fact]
    public void At_the_start_the_mark_holds_the_whole_expression()
    {
        var tried = Assert.IsType<TryExpr>(Clean("try a() + b()"));
        Assert.IsType<BinaryExpr>(tried.Value);
        Assert.Null(tried.Reach);
    }

    /// <summary>To the right of an operator the mark is a prefix of its operand: the grouping is
    /// what it is without the word — 'a() + (try b()) * c()' is 'a() + (b() * c())'.</summary>
    [Fact]
    public void To_the_right_of_an_operator_it_changes_no_grouping()
    {
        var sum = Assert.IsType<BinaryExpr>(Clean("a() + try b() * c()"));
        Assert.Equal(BinaryOp.Add, sum.Operator);
        var product = Assert.IsType<BinaryExpr>(sum.Right);
        Assert.Equal(BinaryOp.Mul, product.Operator);
        Assert.IsType<CallExpr>(Assert.IsType<TryExpr>(product.Left).Value);
    }

    /// <summary>… and it covers from its keyword to the end of the expression it stands in.</summary>
    [Theory]
    [InlineData("a() + try b() * c()", "try b() * c()", "a() + try b() * c()")]
    [InlineData("x ?? try f()", "try f()", "x ?? try f()")]
    [InlineData("g(a() + try b(), c())", "try b()", "a() + try b()")]
    [InlineData("(a() + try b()) + c()", "try b()", "a() + try b()")]
    [InlineData("if (k) a() + try b() else c()", "try b()", "a() + try b()")]
    public void It_covers_to_the_end_of_the_expression_it_stands_in(string source, string covered, string expression)
    {
        var mark = Mark(Clean(source));
        var reach = mark.Reach!;
        Assert.Equal(covered, source[mark.KeywordSpan.Start..reach.End]);
        Assert.Equal(expression, source[reach.From..reach.End]);
    }

    [Theory]
    [InlineData("1 + try? f()", "'try?'")]
    [InlineData("1 + try! f()", "'try!'")]
    [InlineData("1 + try f() catch (e) 0", "clauses")]
    public void What_is_worth_something_else_than_it_covers_stands_at_the_start(string source, string named)
    {
        var error = Assert.Single(Parse(source).De.Diagnostics);
        Assert.Equal("LYR-PAR0050", error.Code);
        Assert.Contains(named, error.Message);
    }
}
