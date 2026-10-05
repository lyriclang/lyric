using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Value blocks and the tail rule (design/v5/spec/08 Y4, Y5 S1; the review's M5-1). A value block
/// stands as a lambda's body, an arm, a clause of a 'try' expression, a branch of an 'if'
/// expression and the right of '??'. Its last statement, written without ';', is its value — an
/// expression, or an 'if' with its 'else', a 'match' or a 'loop'; in the middle of the block, and
/// in every statement block, those three are the statements they always were.
/// </summary>
public class ValueBlockTests
{
    private static Expr Expression(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var expr = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression();
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return expr;
    }

    private static (Module Module, DiagnosticEngine De) Module(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseModule(), de);
    }

    /// <summary>The statements of the block a lambda has as its body, in 'fn f(): void { run(BODY); }'
    /// or '… run BODY' for a trailing one.</summary>
    private static Block LambdaBlock(string call)
    {
        var (module, de) = Module("fn f(): void {\n    " + call + ";\n}\n");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var statement = Assert.IsType<ExprStmt>(Assert.IsType<FunctionDecl>(module.Declarations[0]).Body!.Statements[0]);
        var lambda = Assert.IsType<LambdaExpr>(Assert.IsType<CallExpr>(statement.Expr).Arguments[^1]);
        return Assert.IsType<Block>(lambda.Body);
    }

    // ------------------------------------------------------------------ a value block as an expression

    [Fact]
    public void A_branch_of_an_if_expression_may_be_a_value_block()
    {
        var iff = Assert.IsType<IfExpr>(Expression("if (c) { let a = 1; a } else { 2 }"));
        var then = Assert.IsType<BlockExpr>(iff.Then).Block;
        Assert.IsType<BindingStmt>(then.Statements[0]);
        Assert.IsType<IdentifierExpr>(then.Tail!.Expr);
        Assert.IsType<IntLiteralExpr>(Assert.IsType<BlockExpr>(iff.Else).Block.Tail!.Expr);
    }

    [Fact]
    public void An_else_if_ladder_of_value_blocks_nests()
    {
        var iff = Assert.IsType<IfExpr>(Expression("if (a) { 1 } else if (b) { 2 } else { 3 }"));
        var second = Assert.IsType<IfExpr>(iff.Else);
        Assert.IsType<BlockExpr>(second.Then);
        Assert.IsType<BlockExpr>(second.Else);
    }

    [Fact]
    public void The_two_forms_of_a_branch_mix()
    {
        var iff = Assert.IsType<IfExpr>(Expression("if (c) { 1 } else 2"));
        Assert.IsType<BlockExpr>(iff.Then);
        Assert.IsType<IntLiteralExpr>(iff.Else);
    }

    /// <summary>The block ends the branch: what follows it is the whole 'if's, as in Rust. Without
    /// braces the else branch takes everything to its right, as it always did.</summary>
    [Fact]
    public void An_operator_behind_a_block_branch_takes_the_whole_if()
    {
        var sum = Assert.IsType<BinaryExpr>(Expression("if (c) { 1 } else { 2 } + 3"));
        Assert.IsType<IfExpr>(sum.Left);
        Assert.IsType<BinaryExpr>(Assert.IsType<IfExpr>(Expression("if (c) 1 else 2 + 3")).Else);
    }

    [Fact]
    public void The_right_of_a_coalesce_may_be_a_value_block()
    {
        var coalesce = Assert.IsType<BinaryExpr>(Expression("x ?? { log(); 0 }"));
        Assert.Equal(BinaryOp.Coalesce, coalesce.Operator);
        var block = Assert.IsType<BlockExpr>(coalesce.Right).Block;
        Assert.IsType<ExprStmt>(block.Statements[0]);
        Assert.IsType<IntLiteralExpr>(block.Tail!.Expr);
    }

    // ------------------------------------------------------------------ the tail rule

    [Fact]
    public void An_if_with_its_else_standing_last_is_the_blocks_value()
    {
        var block = LambdaBlock("run((n: int) => { let d = n * 2; if (d > 5) { d } else { 0 } })");
        var iff = Assert.IsType<IfExpr>(block.Tail!.Expr);
        Assert.IsType<IdentifierExpr>(Assert.IsType<BlockExpr>(iff.Then).Block.Tail!.Expr);
    }

    [Fact]
    public void In_the_middle_of_the_block_it_is_a_statement()
    {
        var block = LambdaBlock("run((n: int) => { if (n > 5) { a(); } else { b(); } n })");
        Assert.IsType<IfStmt>(block.Statements[0]);
        Assert.IsType<IdentifierExpr>(block.Tail!.Expr);
    }

    /// <summary>… and there its branches are statement blocks: a value without ';' in one is the
    /// missing semicolon it always was.</summary>
    [Fact]
    public void A_value_in_a_statement_if_is_a_missing_semicolon()
    {
        var (_, de) = Module("fn f(): void {\n    run((n: int) => { if (n > 5) { 1 } else { 2 } n });\n}\n");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0016");
    }

    [Fact]
    public void Without_an_else_it_is_a_statement_wherever_it_stands()
    {
        var block = LambdaBlock("run((n: int) => { if (n > 5) { a(); } })");
        Assert.IsType<IfStmt>(Assert.Single(block.Statements));
        Assert.Null(block.Tail);
    }

    /// <summary>A condition that binds makes the 'if' a statement: the names it binds are a
    /// statement's (09 §if-let).</summary>
    [Fact]
    public void An_if_that_binds_is_a_statement()
    {
        var block = LambdaBlock("run((n: ?int) => { if (let v = n) { a(v); } else { b(); } })");
        Assert.IsType<IfStmt>(Assert.Single(block.Statements));
    }

    /// <summary>The form without braces is a statement nowhere (08 Y5 S1): in a value block it is
    /// the expression it is everywhere else.</summary>
    [Fact]
    public void The_form_without_braces_is_an_expression_in_a_value_block()
    {
        var block = LambdaBlock("apply(4) { n => if (n > 2) 10 else 20 }");
        Assert.IsType<IfExpr>(block.Tail!.Expr);
    }

    [Fact]
    public void A_match_standing_last_is_the_blocks_value_and_a_statement_before_that()
    {
        var block = LambdaBlock("run((n: int) => { match (n) { 0 => { a(); } _ => { b(); } } match (n) { 0 => 1, _ => 2 } })");
        Assert.IsType<MatchStmt>(block.Statements[0]);
        Assert.IsType<MatchExpr>(block.Tail!.Expr);
    }

    [Fact]
    public void A_loop_standing_last_is_the_blocks_value_with_its_label()
    {
        var block = LambdaBlock("run((n: int) => { loop { break; } outer: loop { break outer n; } })");
        Assert.IsType<LoopExpr>(Assert.IsType<ExprStmt>(block.Statements[0]).Expr);
        Assert.Equal("outer", Assert.IsType<LoopExpr>(block.Tail!.Expr).Label);
    }

    /// <summary>A function's body is a statement block: nothing in it is a tail.</summary>
    [Fact]
    public void In_a_function_body_the_three_stay_statements()
    {
        var (module, de) = Module("fn f(c: bool): int {\n    if (c) { return 1; } else { return 2; }\n}\n"
            + "fn g(n: int): int {\n    match (n) { 0 => { return 1; } _ => { return 2; } }\n}\n");
        Assert.False(de.HasErrors);
        Assert.IsType<IfStmt>(Assert.IsType<FunctionDecl>(module.Declarations[0]).Body!.Statements[0]);
        Assert.IsType<MatchStmt>(Assert.IsType<FunctionDecl>(module.Declarations[1]).Body!.Statements[0]);
    }

    /// <summary>The blocks of a statement 'if' in a value block are statement blocks, and an 'if'
    /// standing last in one of them is no tail: the rule is the value block's own statements'.</summary>
    [Fact]
    public void The_rule_holds_for_the_value_blocks_own_statements_only()
    {
        var block = LambdaBlock("run((n: int) => { if (n > 5) { if (n > 9) { a(); } else { b(); } } n })");
        var outer = Assert.IsType<IfStmt>(block.Statements[0]);
        Assert.IsType<IfStmt>(Assert.Single(outer.Then.Statements));
    }

    // ------------------------------------------------------------------ every lambda's block is a value block

    [Fact]
    public void A_bare_lambdas_block_has_a_tail()
    {
        var (module, de) = Module("fn f(): void {\n    let g: fn(int) -> int = x => { let d = x * 2; d + 1 };\n}\n");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var binding = Assert.IsType<BindingStmt>(Assert.IsType<FunctionDecl>(module.Declarations[0]).Body!.Statements[0]);
        Assert.NotNull(Assert.IsType<Block>(Assert.IsType<LambdaExpr>(binding.Initializer).Body).Tail);
    }

    [Fact]
    public void A_trailing_block_of_statements_has_a_tail()
    {
        var block = LambdaBlock("apply(3) { let d = it * 2; d + 1 }");
        Assert.IsType<BindingStmt>(block.Statements[0]);
        Assert.IsType<BinaryExpr>(block.Tail!.Expr);
    }

    /// <summary>A trailing block that begins with an 'if': one that is a statement makes the braces
    /// a block; one that is an expression is the one expression they hold.</summary>
    [Fact]
    public void A_trailing_block_beginning_with_an_if_is_read_by_what_the_if_is()
    {
        var statement = LambdaBlock("each(xs) { if (it > 1) { show(it); } }");
        Assert.IsType<IfStmt>(Assert.Single(statement.Statements));

        var (module, de) = Module("fn f(): void {\n    apply(4) { if (it > 2) { 10 } else { 20 } };\n}\n");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var call = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(
            Assert.IsType<FunctionDecl>(module.Declarations[0]).Body!.Statements[0]).Expr);
        Assert.IsType<IfExpr>(Assert.IsType<LambdaExpr>(call.Arguments[^1]).Body);
    }
}
