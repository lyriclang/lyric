using Lyric.AST;
using Lyric.Core;
using Lyric.Formatting;
using Lyric.Parsing;

namespace Lyric.Tests.Formatting;

/// <summary>The formatter writes a <c>quote</c> and its splices (M9b S3).</summary>
public class QuoteFormatTests
{
    private static Decl ParseDecl(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("t.lyr", text);
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, id, de).ParseModule();
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        return module.Declarations[0];
    }

    [Fact]
    public void A_quote_of_statements_and_its_splices()
    {
        var text = AstFormatter.FormatNode(ParseDecl("macro m(e: Expr, body: Block): Stmt[] { return quote { let n = #{e}; #{body} n }; }"));
        Assert.Equal("macro m(e: Expr, body: Block): Stmt[] {\n    return quote {\n        let n = #{e};\n        #{body}\n        n\n    };\n}", text);
    }

    [Fact]
    public void A_quote_of_declarations()
    {
        var text = AstFormatter.FormatNode(ParseDecl("fn d(): Decl { return quote { fn g(): int { return 1; } }; }"));
        Assert.Equal("fn d(): Decl {\n    return quote {\n        fn g(): int {\n            return 1;\n        }\n    };\n}", text);
    }
}
