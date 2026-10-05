using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// A brace after a name: an initializer's, <c>Point { x = 1, y = 2 }</c>, or a trailing block's,
/// <c>run { total = 5; }</c> (design/v5/spec/08 Y4; the review's M6-2). Both may begin with
/// <c>name = value</c>. What ends that value decides: a <c>;</c> makes it a statement and the
/// brace a block's; a <c>,</c> or the closing brace makes it a field.
/// </summary>
public class BraceAfterNameTests
{
    private static (Stmt[] Body, DiagnosticEngine De) Body(string statements)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, sm.AddVirtual("test.lyr", "fn f(): void {\n" + statements + "\n}\n"), de).ParseModule();
        return (Assert.IsType<FunctionDecl>(module.Declarations[0]).Body!.Statements, de);
    }

    private static Expr Value(string binding)
    {
        var (body, de) = Body(binding);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<BindingStmt>(body[0]).Initializer!;
    }

    private static LambdaExpr TrailingBlock(Expr expr) =>
        Assert.IsType<LambdaExpr>(Assert.Single(Assert.IsType<CallExpr>(expr).Arguments));

    [Fact]
    public void A_semicolon_behind_the_first_value_makes_the_brace_a_block()
    {
        var block = Assert.IsType<Block>(TrailingBlock(Value("let r = run { total = 5; };")).Body);
        Assert.IsType<AssignExpr>(Assert.IsType<ExprStmt>(Assert.Single(block.Statements)).Expr);
    }

    [Fact]
    public void A_block_that_begins_with_an_assignment_may_go_on_and_end_in_a_tail()
    {
        var block = Assert.IsType<Block>(TrailingBlock(Value("let r = compute { n = 5; n * 2 };")).Body);
        Assert.IsType<ExprStmt>(block.Statements[0]);
        Assert.IsType<BinaryExpr>(block.Tail!.Expr);
    }

    [Theory]
    [InlineData("let p = Point { x = 1, y = 2 };", 2)]
    [InlineData("let p = One { x = 7 };", 1)]
    [InlineData("let p = Empty { };", 0)]
    [InlineData("let p = One { x = 7, };", 1)]
    public void A_comma_or_the_closing_brace_makes_it_a_field(string binding, int fields) =>
        Assert.Equal(fields, Assert.IsType<StructInitExpr>(Value(binding)).Fields.Length);

    /// <summary>The scan is over the first VALUE: a ';' inside it — in a lambda's block, in a
    /// nested call — is not the one that decides.</summary>
    [Fact]
    public void A_semicolon_inside_the_first_value_decides_nothing()
    {
        var init = Assert.IsType<StructInitExpr>(Value("let h = Handler { run = () => { a(); b(); }, id = 1 };"));
        Assert.Equal(2, init.Fields.Length);
        var alone = Assert.IsType<StructInitExpr>(Value("let h = Handler { run = () => { a(); b(); } };"));
        Assert.Single(alone.Fields);
    }

    /// <summary>A block that does not begin with 'name =' was a trailing block before, and is.</summary>
    [Fact]
    public void A_block_of_anything_else_is_a_trailing_block()
    {
        Assert.IsType<IntLiteralExpr>(TrailingBlock(Value("let r = run { 7 };")).Body);
        Assert.IsType<Block>(TrailingBlock(Value("let r = run { show(1); show(2); };")).Body);
    }

    // ------------------------------------------------------------------ at the start of a statement

    /// <summary>What the braces hold tells the two apart at the start of a statement too: a block
    /// of statements after a bare name is the call it looks like. It was refused there whatever
    /// it held.</summary>
    [Fact]
    public void At_the_start_of_a_statement_a_block_after_a_name_is_the_call()
    {
        var (body, de) = Body("run { total = 5; };\nrun { show(1); }\nrun { 7 };");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal(3, body.Length);
        foreach (var statement in body) TrailingBlock(Assert.IsType<ExprStmt>(statement).Expr);
    }

    /// <summary>An initializer still begins no statement (§6.8) — its value would be dropped. It
    /// was a cascade of four to six diagnostics; it is one, and where the braces hold a single
    /// 'name = value' the note says what a forgotten ';' would have meant.</summary>
    [Theory]
    [InlineData("Point { x = 1, y = 2 };", "Point { x = …; }")]
    [InlineData("run { total = 5 };", "run { total = …; }")]
    [InlineData("Empty { };", "Empty { …; }")]
    public void At_the_start_of_a_statement_an_initializer_is_refused_in_one_message(string statement, string meant)
    {
        var (body, de) = Body(statement);
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0055", error.Code);
        Assert.Contains(meant, Assert.Single(error.Notes!).Message);
        Assert.DoesNotContain(body, s => s is ExprStmt { Expr: StructInitExpr });
    }
}
