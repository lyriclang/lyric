using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// The macro forms (design/v5/spec/08 Y12 Q1, Q5; M9b S2): <c>macro name(...)</c> as a function
/// with its flag, and the sites <c>name!(args)</c>, <c>name!(args) { ... }</c>, <c>name! { ... }</c>,
/// told apart from the unwrap postfix by the '!' standing directly behind the name, before '(' or '{'.
/// </summary>
public class MacroSyntaxTests
{
    private static (Module Module, DiagnosticEngine De) Parse(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("t.lyr", text);
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, id, de).ParseModule(), de);
    }

    private static Expr FirstExpr(string statement)
    {
        var (module, de) = Parse($"fn f(): void {{ {statement} }}");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        var body = ((FunctionDecl)module.Declarations[0]).Body!;
        return body.Statements[0] switch
        {
            ExprStmt e => e.Expr,
            BindingStmt b => b.Initializer!,
            var other => throw new Xunit.Sdk.XunitException($"a {other.GetType().Name}"),
        };
    }

    [Fact]
    public void A_macro_is_a_function_with_its_flag()
    {
        var (module, de) = Parse("pub macro twice(e: Expr): Expr { return e; }\nfn f(): void { }");
        Assert.False(de.HasErrors);
        var macro = Assert.IsType<FunctionDecl>(module.Declarations[0]);
        Assert.True(macro.IsMacro);
        Assert.Equal("twice", macro.Name);
        Assert.Equal(VisibilityWord.Pub, macro.Visibility);
        Assert.False(((FunctionDecl)module.Declarations[1]).IsMacro);
    }

    [Fact]
    public void A_site_with_arguments()
    {
        var site = Assert.IsType<MacroCallExpr>(FirstExpr("twice!(x, 1);"));
        Assert.Equal("twice", site.Name);
        Assert.Equal(2, site.Arguments.Length);
        Assert.Null(site.Trailing);
        Assert.Equal(1, site.BangSpan.Length);
    }

    [Fact]
    public void A_site_with_arguments_and_a_block_ends_at_the_block()
    {
        var (module, de) = Parse("fn f(): void { retry!(3) { try fetch(url) }\n done(); }");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        var body = ((FunctionDecl)module.Declarations[0]).Body!;
        Assert.Equal(2, body.Statements.Length);
        var site = Assert.IsType<MacroCallExpr>(((ExprStmt)body.Statements[0]).Expr);
        Assert.Single(site.Arguments);
        Assert.NotNull(site.Trailing);
        Assert.IsType<TailExprStmt>(site.Trailing!.Statements[0]); // a value block: the tail stands without ';'
    }

    [Fact]
    public void A_site_with_a_block_alone()
    {
        var site = Assert.IsType<MacroCallExpr>(FirstExpr("let v = run! { 2 };"));
        Assert.Empty(site.Arguments);
        Assert.NotNull(site.Trailing);
    }

    [Fact]
    public void The_unwrap_postfix_stays_what_it_was()
    {
        Assert.IsType<PostfixExpr>(FirstExpr("let a = x!;"));
        Assert.IsType<BinaryExpr>(FirstExpr("let b = x! in xs;"));
        var call = Assert.IsType<CallExpr>(FirstExpr("let c = x!.f();"));
        Assert.IsType<MemberExpr>(call.Callee);
        // the '!' behind the name decides, not the space before the '(': a site as well
        Assert.IsType<MacroCallExpr>(FirstExpr("let d = x! (y);"));
        // a space between the name and the '!' is the unwrap, and what follows is read as ever:
        // a call of what it unwrapped
        var spaced = FirstExpr("let e = x !(y);");
        Assert.True(spaced is CallExpr { Callee: PostfixExpr }, $"a {spaced.GetType().Name}");
    }

    [Fact]
    public void A_site_in_an_argument_is_an_argument()
    {
        var call = Assert.IsType<CallExpr>(FirstExpr("println(twice!(twice!(2)));"));
        var outer = Assert.IsType<MacroCallExpr>(call.Arguments[0]);
        Assert.IsType<MacroCallExpr>(outer.Arguments[0]);
    }
}
