using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// <c>quote { ... }</c> and <c>#{...}</c> (design/v5/spec/08 Y12 Q2, Q3; M9b S3): the quote's
/// content as statements or declarations, the splices by index, a name splice as "#i".
/// </summary>
public class QuoteSyntaxTests
{
    private static (Module Module, DiagnosticEngine De) Parse(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("t.lyr", text);
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, id, de).ParseModule(), de);
    }

    private static QuoteExpr Quote(string expression)
    {
        var (module, de) = Parse($"fn f(): Expr {{ return {expression}; }}");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        var body = ((FunctionDecl)module.Declarations[0]).Body!;
        return Assert.IsType<QuoteExpr>(((ReturnStmt)body.Statements[0]).Value);
    }

    [Fact]
    public void A_lone_expression_is_the_tail()
    {
        var q = Quote("quote { #{e} + 1 }");
        Assert.False(q.Declarations);
        var tail = Assert.IsType<TailExprStmt>(Assert.Single(q.Content));
        var sum = Assert.IsType<BinaryExpr>(tail.Expr);
        var splice = Assert.IsType<SpliceExpr>(sum.Left);
        Assert.Equal(0, splice.Index);
        Assert.Single(q.Splices);
        Assert.IsType<IdentifierExpr>(q.Splices[0]);
    }

    [Fact]
    public void Statements_with_a_splice_standing_as_one()
    {
        var q = Quote("quote { let a = 1; #{stmts} a }");
        Assert.Equal(3, q.Content.Length);
        Assert.IsType<SpliceExpr>(Assert.IsType<ExprStmt>(q.Content[1]).Expr); // no ';' needed after a splice statement
        Assert.IsType<TailExprStmt>(q.Content[2]);
    }

    [Fact]
    public void Declarations_with_a_name_splice()
    {
        var q = Quote("quote { fn #{name}(): int { return #{v}; } struct P { x: int } }");
        Assert.True(q.Declarations);
        Assert.Equal(2, q.Content.Length);
        var fn = Assert.IsType<FunctionDecl>(q.Content[0]);
        Assert.Equal("#0", fn.Name);
        Assert.Equal(2, q.Splices.Length);
        Assert.Equal("P", ((StructDecl)q.Content[1]).Name);
    }

    [Fact]
    public void A_splice_outside_a_quote_is_no_syntax()
    {
        var (_, de) = Parse("fn f(): int { return #{e}; }");
        Assert.True(de.HasErrors);
    }

    [Fact]
    public void Quotes_nest()
    {
        var outer = Quote("quote { quote { #{e} } }");
        var inner = Assert.IsType<QuoteExpr>(Assert.IsType<TailExprStmt>(outer.Content[0]).Expr);
        Assert.Empty(outer.Splices);
        Assert.Single(inner.Splices);
    }
}
