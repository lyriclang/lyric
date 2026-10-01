using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// The syntax of errors (design/v5/spec/08 D9, D18, Y4, Y5; spec chapter 06 §2–§3): the
/// <c>throws</c> set — one type, a bracketed list, the bare form — with the list rule's error
/// (PAR0049); the <c>try</c> prefix covering everything to its right, at the start of the
/// expression it covers (PAR0050); <c>try {</c> as the block form at a statement start; the
/// expression forms (05 E4): <c>try?</c> and <c>try!</c> written against the keyword, and
/// <c>try e catch (x: A) v</c> whose clauses belong to the nearest <c>try</c> on their left (PAR0051
/// after a signed one).
/// </summary>
public class ErrorSyntaxTests
{
    private static (Module Module, DiagnosticEngine De) ParseModule(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseModule();
        return (module, de);
    }

    private static (Expr Expr, DiagnosticEngine De) ParseExpr(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression(), de);
    }

    private static string[] ThrownNames(string declaration)
    {
        var (m, de) = ParseModule(declaration);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var fn = Assert.IsType<FunctionDecl>(m.Declarations[0]);
        return fn.Throws!.Types.Select(t => string.Join(".", Assert.IsType<NamedType>(t).Path)).ToArray();
    }

    // --- the throws set ---

    [Fact]
    public void One_thrown_type_stands_alone() =>
        Assert.Equal(["E"], ThrownNames("fn f(): int throws E { return 0; }"));

    [Fact]
    public void Several_thrown_types_are_a_bracketed_list() =>
        Assert.Equal(["A", "B", "C"], ThrownNames("fn f(): int throws [A, B, C] { return 0; }"));

    [Fact]
    public void A_list_of_one_is_allowed() =>
        Assert.Equal(["A"], ThrownNames("fn f(): int throws [A] { return 0; }"));

    [Fact]
    public void The_bare_throws_names_nothing() =>
        Assert.Empty(ThrownNames("fn f(): int throws { return 0; }"));

    [Fact]
    public void The_bare_throws_on_an_abstract_member()
    {
        var (m, de) = ParseModule("interface I { fn f(): int throws; fn g(): int throws [A, B]; }");
        Assert.False(de.HasErrors);
        var members = Assert.IsType<InterfaceDecl>(m.Declarations[0]).Members;
        Assert.Empty(members[0].Throws!.Types);
        Assert.Equal(2, members[1].Throws!.Types.Length);
    }

    [Fact]
    public void Generic_thrown_types_parse_inside_the_list() =>
        Assert.Equal(["Box", "Wrong"], ThrownNames("fn f(): int throws [Box<int>, Wrong<string>] { return 0; }"));

    [Fact]
    public void Several_types_without_the_brackets_are_the_list_rules_error()
    {
        var (m, de) = ParseModule("fn f(): int throws A, B { return 0; }");
        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("LYR-PAR0049", error.Code);
        Assert.Contains("throws [A, B]", error.Message);
        // Parsed as the list it meant, so the rest of the file reads on.
        Assert.Equal(2, Assert.IsType<FunctionDecl>(m.Declarations[0]).Throws!.Types.Length);
    }

    [Fact]
    public void An_empty_list_is_refused() =>
        Assert.Contains(ParseModule("fn f(): int throws [] { return 0; }").De.Diagnostics, d => d.Code == "LYR-PAR0049");

    // --- the try mark ---

    [Fact]
    public void Try_covers_everything_to_its_right()
    {
        var (expr, de) = ParseExpr("try a + b * c == d");
        Assert.False(de.HasErrors);
        var tried = Assert.IsType<TryExpr>(expr);
        var eq = Assert.IsType<BinaryExpr>(tried.Value);
        Assert.Equal(BinaryOp.Eq, eq.Operator);
    }

    [Fact]
    public void Try_covers_an_assignment_to_its_right()
    {
        var (expr, de) = ParseExpr("try x = f()");
        Assert.False(de.HasErrors);
        Assert.IsType<AssignExpr>(Assert.IsType<TryExpr>(expr).Value);
    }

    [Fact]
    public void Try_stands_on_the_right_of_an_assignment()
    {
        var (expr, de) = ParseExpr("x = try f() + 1");
        Assert.False(de.HasErrors);
        var assign = Assert.IsType<AssignExpr>(expr);
        Assert.IsType<BinaryExpr>(Assert.IsType<TryExpr>(assign.Value).Value);
    }

    [Fact]
    public void The_keyword_span_is_the_word_alone()
    {
        var (expr, _) = ParseExpr("try f()");
        var tried = Assert.IsType<TryExpr>(expr);
        Assert.Equal(0, tried.KeywordSpan.Start);
        Assert.Equal(3, tried.KeywordSpan.End);
    }

    [Fact]
    public void Try_to_the_right_of_an_operator_is_refused()
    {
        var (_, de) = ParseExpr("1 + try f()");
        Assert.Equal("LYR-PAR0050", Assert.Single(de.Diagnostics).Code);
        Assert.Contains(ParseExpr("a ?? try b()").De.Diagnostics, d => d.Code == "LYR-PAR0050");
    }

    [Fact]
    public void Try_in_an_argument_and_a_binding_starts_its_expression()
    {
        var (m, de) = ParseModule("fn g(): int { let x = try f(); return h(try f(), 2); }");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void A_statement_starting_with_try_and_a_brace_is_the_block_form()
    {
        var (m, de) = ParseModule("fn g() { try { f(); } catch (_) { } }");
        Assert.False(de.HasErrors);
        var body = Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!;
        Assert.IsType<TryStmt>(body.Statements[0]);
    }

    [Fact]
    public void A_statement_starting_with_try_and_a_call_is_a_marked_expression()
    {
        var (m, de) = ParseModule("fn g() { try f(); }");
        Assert.False(de.HasErrors);
        var body = Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!;
        var statement = Assert.IsType<ExprStmt>(body.Statements[0]);
        Assert.IsType<CallExpr>(Assert.IsType<TryExpr>(statement.Expr).Value);
    }

    [Fact]
    public void Main_may_declare_a_throws_set()
    {
        var (m, de) = ParseModule("fn main(): void throws [A, B] { }");
        Assert.False(de.HasErrors);
        Assert.Equal(2, Assert.IsType<FunctionDecl>(m.Declarations[0]).Throws!.Types.Length);
    }

    // --- try? and try! ---

    [Fact]
    public void Try_with_a_question_mark_is_the_optional_form()
    {
        var (expr, de) = ParseExpr("try? f() + 1");
        Assert.False(de.HasErrors);
        var tried = Assert.IsType<TryExpr>(expr);
        Assert.Equal(TryKind.Optional, tried.Kind);
        Assert.IsType<BinaryExpr>(tried.Value); // it covers everything to its right, as the mark does
        Assert.Equal(4, tried.KeywordSpan.End);  // 'try?' is what a diagnostic about it points at
    }

    [Fact]
    public void Try_with_a_bang_is_the_forced_form()
    {
        var (expr, de) = ParseExpr("try! f()");
        Assert.False(de.HasErrors);
        Assert.Equal(TryKind.Force, Assert.IsType<TryExpr>(expr).Kind);
    }

    [Fact]
    public void A_bang_after_a_space_is_the_negation_the_mark_covers()
    {
        var (expr, de) = ParseExpr("try !done()");
        Assert.False(de.HasErrors);
        var tried = Assert.IsType<TryExpr>(expr);
        Assert.Equal(TryKind.Propagate, tried.Kind);
        Assert.Equal(UnaryOp.Not, Assert.IsType<UnaryExpr>(tried.Value).Operator);
    }

    [Fact]
    public void The_signed_forms_stand_at_the_start_too()
    {
        var error = Assert.Single(ParseExpr("1 + try? f()").De.Diagnostics);
        Assert.Equal("LYR-PAR0050", error.Code);
        Assert.Contains("'try?'", error.Message);
    }

    // --- the expression form ---

    [Fact]
    public void Clauses_after_a_try_make_the_expression_form()
    {
        var (expr, de) = ParseExpr("try f() catch (e: A) 1 catch (_) { g(); 2 }");
        Assert.False(de.HasErrors);
        var tried = Assert.IsType<TryExpr>(expr);
        Assert.Equal(2, tried.Catches.Length);
        Assert.True(tried.Catches[0].ExpressionBody);
        Assert.IsType<IntLiteralExpr>(tried.Catches[0].Body.Tail!.Expr);
        Assert.False(tried.Catches[1].ExpressionBody);
        Assert.NotNull(tried.Catches[1].Body.Tail); // a value block: its tail is the clause's value
    }

    [Fact]
    public void A_clause_belongs_to_the_nearest_try_on_its_left()
    {
        var (expr, de) = ParseExpr("try f() catch (_: A) try g() catch (_: B) 2");
        Assert.False(de.HasErrors);
        var outer = Assert.IsType<TryExpr>(expr);
        var inner = Assert.IsType<TryExpr>(Assert.Single(outer.Catches).Body.Tail!.Expr);
        Assert.Single(inner.Catches);
    }

    [Fact]
    public void A_clause_body_is_a_value_position()
    {
        // At a statement's start a struct initializer is refused; a clause body is past the start.
        var (_, de) = ParseModule("fn g() { try f() catch (_) P { x = 0 }; }");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void A_clause_after_a_signed_try_is_refused()
    {
        var (expr, de) = ParseExpr("try? f() catch (_) 0");
        Assert.Equal("LYR-PAR0051", Assert.Single(de.Diagnostics).Code);
        Assert.Single(Assert.IsType<TryExpr>(expr).Catches); // parsed on, so nothing cascades
    }

    [Fact]
    public void The_expression_form_stands_as_a_statement()
    {
        var (m, de) = ParseModule("fn g() { try f() catch (e) log(e); }");
        Assert.False(de.HasErrors);
        var statement = Assert.IsType<ExprStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]);
        Assert.Single(Assert.IsType<TryExpr>(statement.Expr).Catches);
    }
}
