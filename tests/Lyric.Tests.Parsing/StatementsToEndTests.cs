using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// <c>Parser.ParseStatements</c> (M9b S1): every statement of a text to its end, as a block body
/// holds them, and a last expression without its <c>;</c> is the tail, as in a value block.
/// </summary>
public class StatementsToEndTests
{
    private static (Stmt[] Stmts, DiagnosticEngine De) Parse(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("t.lyr", text);
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, id, de).ParseStatements(), de);
    }

    [Fact]
    public void Statements_are_read_to_the_end()
    {
        var (stmts, de) = Parse("let x = 1; if (x > 0) { x = x - 1; } x");
        Assert.False(de.HasErrors);
        Assert.Equal(3, stmts.Length);
        Assert.IsType<BindingStmt>(stmts[0]);
        Assert.IsType<IfStmt>(stmts[1]);
        Assert.IsType<TailExprStmt>(stmts[2]);
    }

    [Fact]
    public void A_last_expression_with_its_semicolon_is_no_tail()
    {
        var (stmts, de) = Parse("f(); g();");
        Assert.False(de.HasErrors);
        Assert.Equal(2, stmts.Length);
        Assert.All(stmts, s => Assert.IsType<ExprStmt>(s));
    }

    [Fact]
    public void A_tail_inside_a_nested_block_is_still_refused()
    {
        // the text's end is where a value block would close, not every block's
        var (_, de) = Parse("{ x }");
        Assert.True(de.HasErrors);
    }

    [Fact]
    public void An_empty_text_holds_no_statement()
    {
        var (stmts, de) = Parse("");
        Assert.False(de.HasErrors);
        Assert.Empty(stmts);
    }
}
