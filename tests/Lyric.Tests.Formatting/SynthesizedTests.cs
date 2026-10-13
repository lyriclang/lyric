using Lyric.AST;
using Lyric.Core;
using Lyric.Formatting;
using Lyric.Parsing;

namespace Lyric.Tests.Formatting;

/// <summary>
/// The formatter over a tree that was built, not read (M9b S1, <c>std.syntax</c>): no source to
/// copy a spelling from, so literals come from their values; empty spans everywhere.
/// </summary>
public class SynthesizedTests
{
    private static Module Parse(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("t.lyr", text);
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, id, de).ParseModule();
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        return module;
    }

    [Fact]
    public void A_parsed_block_keeps_its_statements()
    {
        var fn = (FunctionDecl)Parse("fn f(): void { x++; }").Declarations[0];
        var text = AstFormatter.FormatNode(fn);
        Assert.Equal("fn f(): void {\n    x++;\n}", text);
    }

    [Fact]
    public void A_built_block_keeps_its_statements()
    {
        var x = new IdentifierExpr("x", default);
        var inc = new ExprStmt(new PostfixExpr(x, PostfixOp.Inc, default), default);
        var block = new Block([inc], default);
        var fn = new FunctionDecl(VisibilityWord.None, false, false, "f", [], [], new NamedType(["void"], [], default) { NameSpan = default }, null, block, default) { NameSpan = default };
        Assert.Equal("fn f(): void {\n    x++;\n}", AstFormatter.FormatNode(fn));
        Assert.Equal("{\n    x++;\n}", AstFormatter.FormatNode(block));
        Assert.Equal("x++;", AstFormatter.FormatStatements([inc]));
    }

    [Fact]
    public void Literals_are_written_from_their_values()
    {
        var sum = new BinaryExpr(new FloatLiteralExpr(1500, null, default), BinaryOp.Add,
            new StringLiteralExpr("q\"t\n", default), default);
        Assert.Equal("1500.0 + \"q\\\"t\\n\"", AstFormatter.FormatNode(sum));
        Assert.Equal("'c'", AstFormatter.FormatNode(new CharLiteralExpr('c', default)));
        Assert.Equal("b\"ab\\x00\"", AstFormatter.FormatNode(new ByteStringExpr([(byte)'a', (byte)'b', 0], default)));
        Assert.Equal("7u8", AstFormatter.FormatNode(new IntLiteralExpr(7, IntSuffix.U8, default)));
        Assert.Equal("-3", AstFormatter.FormatNode(new LiteralPattern(new UnaryExpr(UnaryOp.Neg, new IntLiteralExpr(3, null, default), default), default)));
    }
}
