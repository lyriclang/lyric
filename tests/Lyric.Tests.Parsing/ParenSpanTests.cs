using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// The span of an expression built on a parenthesized operand. <c>(a + b)</c> is the tree of
/// <c>a + b</c> — no node stands for the parentheses — and what is built on it,
/// <c>(a + b).abs()</c>, has to begin at the <c>(</c>: its span is the text a failed
/// <c>assertEq</c> quotes and a diagnostic underlines. It began at <c>a</c>:
/// "1..5).sum() = 10, expected 11".
/// </summary>
public class ParenSpanTests
{
    private static (Expr Expr, string Source) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var expr = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression();
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return (expr, source);
    }

    private static string Text(Node node, string source) => source[node.Span.Start..node.Span.End];

    private static IEnumerable<Node> Nodes(Node root)
    {
        yield return root;
        foreach (var child in AstChildren.Of(root))
        foreach (var inner in Nodes(child))
            yield return inner;
    }

    private static bool Balanced(string text)
    {
        var depth = 0;
        foreach (var c in text)
        {
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') { if (--depth < 0) return false; }
        }
        return depth == 0;
    }

    /// <summary>The whole expression's span is the whole text, whatever stands in parentheses at
    /// its start or at its end.</summary>
    [Theory]
    [InlineData("(1..5).sum()")]
    [InlineData("(a + b) * c")]
    [InlineData("a * (b + c)")]
    [InlineData("(a + b) * (c + d)")]
    [InlineData("-(a + b)")]
    [InlineData("!(a)")]
    [InlineData("(a).b")]
    [InlineData("(a)?.b")]
    [InlineData("((a)).b(c)")]
    [InlineData("(a)[i]")]
    [InlineData("(a)(1)")]
    [InlineData("(x)!")]
    [InlineData("(n)++")]
    [InlineData("(a) as int")]
    [InlineData("(a) is T")]
    [InlineData("(a)..(b)")]
    [InlineData("(a) ?? (b)")]
    [InlineData("(a) = (b)")]
    [InlineData("try (f())")]
    [InlineData("throw (e)")]
    [InlineData("if (c) (a) else (b)")]
    [InlineData("(p) with { x = (1) }")]
    [InlineData("x => (x + 1)")]
    [InlineData("(x: int) => (x + 1)")]
    [InlineData("match (n) { 0 => (a), _ => (b) }")]
    [InlineData("f((a), (b)).g((c))")]
    public void An_expression_spans_its_whole_text(string source)
    {
        var (expr, text) = Parse(source);
        Assert.Equal(text, Text(expr, text));
    }

    /// <summary>… and so does every expression inside it that is built on other expressions: no
    /// span cuts a pair of parentheses in half.</summary>
    [Theory]
    [InlineData("f((1..5).sum(), (a + b) * c)")]
    [InlineData("[(a + b) * c, -(d)]")]
    [InlineData("g(x => (x + 1) * 2, if (c) (a).b else (b).c)")]
    [InlineData("P { x = (a + b) * 2, y = (c)[0] }")]
    [InlineData("((a + b) * (c - d)).pow((e))")]
    public void No_span_cuts_a_pair_of_parentheses(string source)
    {
        var (expr, text) = Parse(source);
        foreach (var node in Nodes(expr).OfType<Expr>())
            Assert.True(Balanced(Text(node, text)), $"{node.GetType().Name}: '{Text(node, text)}'");
    }

    /// <summary>The operand keeps the span of what it is: the name in '(x)' is the name, which a
    /// rename edits — the parentheses are the larger expression's.</summary>
    [Fact]
    public void A_parenthesized_operand_keeps_its_own_span()
    {
        var (expr, text) = Parse("(x).length()");
        var call = Assert.IsType<CallExpr>(expr);
        var member = Assert.IsType<MemberExpr>(call.Callee);
        Assert.Equal("(x).length", Text(member, text));
        Assert.Equal("x", Text(Assert.IsType<IdentifierExpr>(member.Target), text));
    }

    /// <summary>An argument written in parentheses of its own is known to its call with them:
    /// what '@callerExpr' quotes is what the call wrote.</summary>
    [Fact]
    public void A_call_knows_the_arguments_written_in_parentheses()
    {
        var (expr, text) = Parse("f((a + b), c)");
        var call = Assert.IsType<CallExpr>(expr);
        var (argument, written) = Assert.Single(call.Parenthesized!);
        Assert.Same(call.Arguments[0], argument);
        Assert.Equal("(a + b)", text[written.Start..written.End]);
        Assert.Null(Assert.IsType<CallExpr>(Parse("f(a + b, c)").Expr).Parenthesized);
    }
}
