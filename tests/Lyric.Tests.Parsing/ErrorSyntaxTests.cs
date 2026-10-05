using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// The syntax of errors (design/v5/spec/08 D9, D18, Y4, Y5; spec chapter 06 §2–§3): the
/// <c>throws</c> set — one type, a bracketed list, the bare form — with the list rule's error
/// (PAR0049); the <c>try</c> prefix covering everything to its right (where it may stand:
/// <see cref="TryPlaceTests"/>); <c>try {</c> as the block form at a statement start; the
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

    // --- the set of a function type and of a lambda (03 T17, 08 Y11 F7) ---

    private static FunctionType ParamType(string declaration, int index = 0)
    {
        var (m, de) = ParseModule(declaration);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<FunctionType>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Parameters[index].Type);
    }

    [Fact]
    public void A_function_type_carries_its_set()
    {
        Assert.Equal("E", Assert.IsType<NamedType>(Assert.Single(ParamType("fn f(g: fn(int) -> int throws E) { }").Throws!.Types)).Path[0]);
        Assert.Equal(2, ParamType("fn f(g: fn() -> void throws [A, B]) { }").Throws!.Types.Length);
        Assert.Empty(ParamType("fn f(g: fn() -> void throws) { }").Throws!.Types);
        Assert.Null(ParamType("fn f(g: fn() -> void) { }").Throws);
    }

    [Fact]
    public void A_comma_after_a_function_types_set_ends_the_type()
    {
        // No list rule here: the comma separates two parameters.
        var (m, de) = ParseModule("fn f(g: fn() -> void throws A, b: int) { }");
        Assert.False(de.HasErrors);
        var fn = Assert.IsType<FunctionDecl>(m.Declarations[0]);
        Assert.Equal(2, fn.Parameters.Length);
        Assert.Single(Assert.IsType<FunctionType>(fn.Parameters[0].Type).Throws!.Types);
    }

    [Fact]
    public void A_coroutine_keeps_its_suffix_outside_a_function_type()
    {
        var (m, de) = ParseModule("fn f(c: Coroutine<int> throws E) { }");
        Assert.False(de.HasErrors);
        Assert.IsType<ThrowingType>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Parameters[0].Type);
    }

    [Fact]
    public void A_parenthesized_lambda_may_write_its_set()
    {
        var (expr, de) = ParseExpr("(x: int): int throws [A, B] => x");
        Assert.False(de.HasErrors);
        Assert.Equal(2, Assert.IsType<LambdaExpr>(expr).Throws!.Types.Length);
        Assert.Empty(Assert.IsType<LambdaExpr>(ParseExpr("(x: int): int throws => x").Expr).Throws!.Types);
        Assert.Null(Assert.IsType<LambdaExpr>(ParseExpr("(x: int): int => x").Expr).Throws);
    }

    [Fact]
    public void A_lambdas_set_follows_the_list_rule() =>
        Assert.Contains(ParseExpr("(x: int): int throws A, B => x").De.Diagnostics, d => d.Code == "LYR-PAR0049");

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

    // --- the set form (05 E9 C5, 08 Y6) ---

    private static CatchClause Clause(string clause)
    {
        var (m, de) = ParseModule("fn g() { try { f(); } " + clause + " { } }");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.Single(Assert.IsType<TryStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]).Catches);
    }

    [Fact]
    public void A_set_clause_names_its_types_in_brackets()
    {
        var clause = Clause("catch (e in [A, B])");
        Assert.Equal(2, clause.BindingTypes.Length);
        Assert.Null(clause.BindingType);
        Assert.False(clause.TakesAll);
    }

    [Fact]
    public void A_set_of_one_needs_no_brackets()
    {
        Assert.Single(Clause("catch (e in A)").BindingTypes);
        Assert.Single(Clause("catch (_ in [A])").BindingTypes);
    }

    [Fact]
    public void The_typed_and_the_typeless_clause_are_no_sets()
    {
        Assert.Empty(Clause("catch (e: A)").BindingTypes);
        Assert.True(Clause("catch (e)").TakesAll);
    }

    [Fact]
    public void An_empty_set_is_refused() =>
        Assert.Contains(ParseModule("fn g() { try { f(); } catch (e in []) { } }").De.Diagnostics, d => d.Code == "LYR-PAR0049");

    [Fact]
    public void Several_caught_types_without_brackets_are_the_list_rules_error()
    {
        var (m, de) = ParseModule("fn g() { try { f(); } catch (e in A, B) { } }");
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0049", error.Code);
        Assert.Contains("in [A, B]", error.Message);
        var tried = Assert.IsType<TryStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]);
        Assert.Equal(2, Assert.Single(tried.Catches).BindingTypes.Length); // parsed as the set it meant
    }

    [Fact]
    public void A_typed_clause_with_several_types_is_pointed_at_the_set_form()
    {
        var (m, de) = ParseModule("fn g() { try { f(); } catch (e: A, B) { } }");
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0049", error.Code);
        Assert.Contains("set form", error.Message);
        var clause = Assert.Single(Assert.IsType<TryStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]).Catches);
        Assert.Null(clause.BindingType);
        Assert.Equal(2, clause.BindingTypes.Length);
    }

    [Fact]
    public void The_expression_form_takes_the_set_form_too()
    {
        var (expr, de) = ParseExpr("try f() catch (e in [A, B]) 0");
        Assert.False(de.HasErrors);
        Assert.Equal(2, Assert.Single(Assert.IsType<TryExpr>(expr).Catches).BindingTypes.Length);
    }

    // --- using let (05 E7 R1–R2, 08 Y5 S5) ---

    [Fact]
    public void Using_let_is_a_binding_that_owes_its_scope_a_close()
    {
        var (m, de) = ParseModule("fn g() { using let f = open(); }");
        Assert.False(de.HasErrors);
        var binding = Assert.IsType<BindingStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]);
        Assert.False(binding.IsMutable);
        // The close, written as the defer it is: 'defer try f.close();'.
        var call = Assert.IsType<CallExpr>(Assert.IsType<TryExpr>(Assert.IsType<ExprStmt>(binding.Cleanup!.Body).Expr).Value);
        Assert.Equal("close", Assert.IsType<MemberExpr>(call.Callee).Member);
    }

    [Fact]
    public void Using_var_is_refused() =>
        Assert.Equal("LYR-PAR0052", Assert.Single(ParseModule("fn g() { using var f = open(); }").De.Diagnostics).Code);

    [Fact]
    public void Using_binds_one_name_to_a_value() =>
        Assert.Contains(ParseModule("fn g() { using let (a, b) = pair(); }").De.Diagnostics, d => d.Code == "LYR-PAR0052");

    [Fact]
    public void Using_stays_a_name_elsewhere() =>
        // Contextual: only 'using' before 'let' or 'var' opens the form.
        Assert.False(ParseModule("fn g() { var using = 1; using = 2; }").De.HasErrors);

    [Fact]
    public void The_expression_form_stands_as_a_statement()
    {
        var (m, de) = ParseModule("fn g() { try f() catch (e) log(e); }");
        Assert.False(de.HasErrors);
        var statement = Assert.IsType<ExprStmt>(Assert.IsType<FunctionDecl>(m.Declarations[0]).Body!.Statements[0]);
        Assert.Single(Assert.IsType<TryExpr>(statement.Expr).Catches);
    }
}
