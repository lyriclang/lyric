using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Direct AST assertions against the parser contract, independent of the AstDumper. The golden tests
/// secure the WHOLE tree through the dumper; these tests check individual invariants — associativity,
/// precedence, recovery — directly on the record tree, so a dumper bug cannot mask a parser bug.
/// </summary>
public class ParserTests
{
    private static (Expr expr, DiagnosticEngine diag) Parse(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var expr = new Parser(sm, id, de).ParseExpression();
        return (expr, de);
    }

    private static (Stmt stmt, DiagnosticEngine diag) ParseStatement(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var stmt = new Parser(sm, id, de).ParseStatement();
        return (stmt, de);
    }

    // --- loop labels ---

    [Fact]
    public void A_label_attaches_to_the_loop_that_follows_it()
    {
        var (stmt, de) = ParseStatement("outer: while (true) { break outer; }");
        Assert.False(de.HasErrors);
        var loop = Assert.IsType<WhileStmt>(stmt);
        Assert.Equal("outer", loop.Label);
        var jump = Assert.IsType<BreakStmt>(Assert.Single(loop.Body.Statements));
        Assert.Equal("outer", jump.Label);
    }

    [Fact]
    public void A_plain_break_carries_no_label()
    {
        var (stmt, de) = ParseStatement("continue;");
        Assert.False(de.HasErrors);
        Assert.Null(Assert.IsType<ContinueStmt>(stmt).Label);
    }

    // --- loop (05 E11, 08 S3/S4) ---

    [Fact]
    public void A_loop_at_the_start_of_a_statement_is_one()
    {
        var (stmt, de) = ParseStatement("loop { break; }");
        Assert.False(de.HasErrors);
        var loop = Assert.IsType<LoopExpr>(Assert.IsType<ExprStmt>(stmt).Expr);
        Assert.IsType<BreakStmt>(Assert.Single(loop.Body.Statements));
    }

    [Fact]
    public void A_loop_is_an_expression_elsewhere_and_may_be_labeled_there()
    {
        var (expr, de) = Parse("outer: loop { loop { break outer 1; } }");
        Assert.False(de.HasErrors);
        var loop = Assert.IsType<LoopExpr>(expr);
        Assert.Equal("outer", loop.Label);
        var inner = Assert.IsType<LoopExpr>(Assert.IsType<ExprStmt>(Assert.Single(loop.Body.Statements)).Expr);
        var jump = Assert.IsType<BreakStmt>(Assert.Single(inner.Body.Statements));
        Assert.Equal("outer", jump.Label);
        Assert.IsType<IntLiteralExpr>(jump.Value);
    }

    [Fact]
    public void A_name_after_break_is_a_label_only_where_a_loop_around_carries_it()
    {
        var (stmt, de) = ParseStatement("outer: loop { break x; break outer; break (outer); }");
        Assert.False(de.HasErrors);
        var body = Assert.IsType<LoopExpr>(Assert.IsType<ExprStmt>(stmt).Expr).Body.Statements;
        var value = Assert.IsType<BreakStmt>(body[0]);
        Assert.Null(value.Label);
        Assert.Equal("x", Assert.IsType<IdentifierExpr>(value.Value).Name);
        var label = Assert.IsType<BreakStmt>(body[1]);
        Assert.Equal("outer", label.Label);
        Assert.Null(label.Value);
        var parenthesized = Assert.IsType<BreakStmt>(body[2]);
        Assert.Null(parenthesized.Label);
        Assert.NotNull(parenthesized.Value);
    }

    [Fact]
    public void A_lambdas_body_sees_no_label_of_the_loops_around_it()
    {
        var (stmt, de) = ParseStatement("outer: loop { let g = () => { loop { break outer; } }; }");
        Assert.False(de.HasErrors);
        var binding = Assert.IsType<BindingStmt>(Assert.Single(Assert.IsType<LoopExpr>(Assert.IsType<ExprStmt>(stmt).Expr).Body.Statements));
        var lambda = Assert.IsType<LambdaExpr>(binding.Initializer);
        // The loop stands last in the lambda's block: it is the block's tail (the tail rule).
        var inner = Assert.IsType<LoopExpr>(Assert.IsType<Block>(lambda.Body).Tail!.Expr);
        var jump = Assert.IsType<BreakStmt>(Assert.Single(inner.Body.Statements));
        Assert.Null(jump.Label);
        Assert.Equal("outer", Assert.IsType<IdentifierExpr>(jump.Value).Name);
    }

    [Fact]
    public void Loop_is_a_word_only_before_its_brace()
    {
        var (expr, de) = Parse("loop + 1");
        Assert.False(de.HasErrors);
        Assert.IsType<BinaryExpr>(expr);
    }

    // --- throw as a prefix expression ---

    /// <summary>'throw' binds like a prefix operator: 'x ?? throw e' is the coalesce with a throw on
    /// the right, and 'throw e ?? f' is NOT 'throw (e ?? f)' — the operand is a unary expression.</summary>
    [Fact]
    public void Throw_in_expression_position_is_a_prefix_operand()
    {
        var (expr, de) = Parse("x ?? throw e");
        Assert.False(de.HasErrors);
        var top = Assert.IsType<BinaryExpr>(expr);
        Assert.Equal(BinaryOp.Coalesce, top.Operator);
        var thrown = Assert.IsType<ThrowExpr>(top.Right);
        Assert.IsType<IdentifierExpr>(thrown.Value);
    }

    [Fact]
    public void Throw_takes_a_unary_operand_not_a_binary_one()
    {
        var (expr, de) = Parse("throw e ?? f");
        Assert.False(de.HasErrors);
        var top = Assert.IsType<BinaryExpr>(expr);
        Assert.IsType<ThrowExpr>(top.Left);
    }

    [Fact]
    public void A_throw_statement_stays_a_statement()
    {
        var (stmt, de) = ParseStatement("throw e;");
        Assert.False(de.HasErrors);
        Assert.IsType<ThrowStmt>(stmt);
    }

    // --- associativity ---

    [Fact]
    public void Coalesce_is_right_associative()
    {
        var (expr, de) = Parse("a ?? b ?? c");
        Assert.False(de.HasErrors);
        var top = Assert.IsType<BinaryExpr>(expr);
        Assert.Equal(BinaryOp.Coalesce, top.Operator);
        Assert.IsType<IdentifierExpr>(top.Left);            // a
        var right = Assert.IsType<BinaryExpr>(top.Right);   // (b ?? c)
        Assert.Equal(BinaryOp.Coalesce, right.Operator);
    }

    [Fact]
    public void Addition_is_left_associative()
    {
        var (expr, _) = Parse("a + b + c");
        var top = Assert.IsType<BinaryExpr>(expr);          // ((a + b) + c)
        Assert.Equal(BinaryOp.Add, top.Operator);
        var left = Assert.IsType<BinaryExpr>(top.Left);
        Assert.Equal(BinaryOp.Add, left.Operator);
        Assert.IsType<IdentifierExpr>(top.Right);           // c
    }

    [Fact]
    public void Assignment_is_right_associative()
    {
        var (expr, _) = Parse("a = b = c");
        var top = Assert.IsType<AssignExpr>(expr);
        Assert.Null(top.Operator);                          // plain '='
        Assert.IsType<AssignExpr>(top.Value);               // b = c
    }

    [Fact]
    public void Compound_assign_carries_base_operator()
    {
        var (expr, _) = Parse("a += b");
        var top = Assert.IsType<AssignExpr>(expr);
        Assert.Equal(BinaryOp.Add, top.Operator);
    }

    [Fact]
    public void Cast_is_left_associative()
    {
        var (expr, _) = Parse("x as int as float");
        var outer = Assert.IsType<CastExpr>(expr);          // (x as int) as float
        Assert.IsType<CastExpr>(outer.Operand);
    }

    // --- precedence ---

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        var (expr, _) = Parse("1 + 2 * 3");
        var top = Assert.IsType<BinaryExpr>(expr);
        Assert.Equal(BinaryOp.Add, top.Operator);
        var right = Assert.IsType<BinaryExpr>(top.Right);
        Assert.Equal(BinaryOp.Mul, right.Operator);
    }

    [Fact]
    public void Postfix_binds_tighter_than_prefix()
    {
        var (expr, _) = Parse("-a!");
        var neg = Assert.IsType<UnaryExpr>(expr);           // -(a!)
        Assert.Equal(UnaryOp.Neg, neg.Operator);
        var unwrap = Assert.IsType<PostfixExpr>(neg.Operand);
        Assert.Equal(PostfixOp.ForceUnwrap, unwrap.Operator);
    }

    [Fact]
    public void Grouping_overrides_precedence()
    {
        var (expr, _) = Parse("(1 + 2) * 3");
        var top = Assert.IsType<BinaryExpr>(expr);
        Assert.Equal(BinaryOp.Mul, top.Operator);
        var left = Assert.IsType<BinaryExpr>(top.Left);
        Assert.Equal(BinaryOp.Add, left.Operator);          // parens survived
    }

    // --- types ---

    [Fact]
    public void Nested_generics_split_the_double_gt()
    {
        var (expr, de) = Parse("x as List<List<int>>");
        Assert.False(de.HasErrors);                         // '>>' correctly split into two '>'
        var cast = Assert.IsType<CastExpr>(expr);
        var outer = Assert.IsType<NamedType>(cast.Type);
        Assert.Equal(["List"], outer.Path);
        var inner = Assert.IsType<NamedType>(Assert.Single(outer.TypeArguments));
        Assert.Equal(["List"], inner.Path);
        var leaf = Assert.IsType<NamedType>(Assert.Single(inner.TypeArguments));
        Assert.Equal(["int"], leaf.Path);
    }

    [Fact]
    public void Dotted_type_path_is_captured()
    {
        var (expr, _) = Parse("d as std.collections.Deque");
        var cast = Assert.IsType<CastExpr>(expr);
        var named = Assert.IsType<NamedType>(cast.Type);
        Assert.Equal(["std", "collections", "Deque"], named.Path);
    }

    [Fact]
    public void Array_type_with_a_length_is_the_inline_array()
    {
        // 'T[8]' is the inline array (design/v5/spec/03 T13 A4), a value of eight elements; the
        // length is a positive decimal literal. Lyric 4 refused the form: its length belonged to
        // the value alone.
        var (expr, diag) = Parse("a as int[8]");
        var cast = Assert.IsType<CastExpr>(expr);
        Assert.Equal(8, Assert.IsType<ArrayType>(cast.Type).Length);
        Assert.False(diag.HasErrors);

        var (_, bad) = Parse("a as int[0]");
        Assert.Equal("LYR-PAR0043", bad.Diagnostics[0].Code);
    }

    // --- f-Strings ---

    [Fact]
    public void Fstring_splits_into_text_and_hole_segments()
    {
        var (expr, de) = Parse("f\"a{b}c\"");
        Assert.False(de.HasErrors);
        var fstr = Assert.IsType<InterpolatedStringExpr>(expr);
        Assert.Collection(fstr.Segments,
            s => Assert.Equal("a", Assert.IsType<InterpText>(s).Text),
            s => Assert.IsType<IdentifierExpr>(Assert.IsType<InterpHole>(s).Expr),
            s => Assert.Equal("c", Assert.IsType<InterpText>(s).Text));
    }

    // --- Recovery / Diagnostics ---

    [Fact]
    public void Range_is_not_chainable()
    {
        var (_, de) = Parse("1..2..3");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0005");
    }

    [Fact]
    public void Tuple_has_no_upper_arity_limit()
    {
        var (expr, de) = Parse("(1, 2, 3, 4, 5)");
        Assert.False(de.HasErrors);
        Assert.Equal(5, Assert.IsType<TupleLitExpr>(expr).Elements.Length);
    }

    [Fact]
    public void Single_element_with_trailing_comma_is_not_a_tuple()
    {
        // The lower bound stays: one element is a grouping rather than a tuple.
        var (_, de) = Parse("(x,)");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0010");
    }

    [Fact]
    public void Empty_array_parses_without_error()
    {
        var (expr, de) = Parse("[]");
        Assert.False(de.HasErrors);
        Assert.Empty(Assert.IsType<ArrayLitExpr>(expr).Elements);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("1 +")]
    [InlineData("* 3")]
    [InlineData("a.")]
    [InlineData("a[")]
    [InlineData("f\"{")]
    [InlineData("(x: ) => x")]
    [InlineData("x as")]
    [InlineData("(((((((((")]
    public void Parser_never_throws_and_reports_on_garbage(string source)
    {
        // The contract: the parser never throws; every error goes out as a diagnostic.
        var (expr, de) = Parse(source);
        Assert.NotNull(expr);
        Assert.True(de.HasErrors);
    }

    // --- Statements (§5) ---

    [Fact]
    public void Let_is_immutable_and_var_is_mutable()
    {
        Assert.False(Assert.IsType<BindingStmt>(ParseStatement("let x = 1;").stmt).IsMutable);
        Assert.True(Assert.IsType<BindingStmt>(ParseStatement("var x = 1;").stmt).IsMutable);
    }

    [Fact]
    public void Binding_captures_type_and_initializer()
    {
        var (stmt, de) = ParseStatement("let x: int = 42;");
        Assert.False(de.HasErrors);
        var binding = Assert.IsType<BindingStmt>(stmt);
        Assert.Equal("x", binding.Name);
        Assert.IsType<NamedType>(binding.Type);
        Assert.IsType<IntLiteralExpr>(binding.Initializer);
    }

    [Fact]
    public void Else_if_chains_as_nested_if()
    {
        var (stmt, de) = ParseStatement("if (a) {} else if (b) {} else {}");
        Assert.False(de.HasErrors);
        var outer = Assert.IsType<IfStmt>(stmt);
        var inner = Assert.IsType<IfStmt>(outer.Else);   // 'else if' → verschachteltes IfStmt
        Assert.IsType<Block>(inner.Else);                // finales 'else' → Block
    }

    [Fact]
    public void Block_collects_statements()
    {
        var (stmt, de) = ParseStatement("{ a(); b(); }");
        Assert.False(de.HasErrors);
        Assert.Equal(2, Assert.IsType<Block>(stmt).Statements.Length);
    }

    [Fact]
    public void Try_collects_typed_and_wildcard_catches()
    {
        var (stmt, de) = ParseStatement("try {} catch (e: E) {} catch (_) {}");
        Assert.False(de.HasErrors);
        var t = Assert.IsType<TryStmt>(stmt);
        Assert.Equal(2, t.Catches.Length);
        Assert.Equal("e", t.Catches[0].BindingName);
        Assert.IsType<NamedType>(t.Catches[0].BindingType);
        Assert.Null(t.Catches[1].BindingName);           // '_' binds nothing
        Assert.Null(t.Catches[1].BindingType);
    }

    // 'Try_without_catch_reports' (PAR0023) retired with the parser's rule (M5 S2b): a block
    // without a clause is the sema's to refuse, SEM0036 — one rule, one diagnostic.
    [Fact]
    public void A_try_without_a_clause_parses_for_the_sema_to_refuse()
    {
        var (stmt, de) = ParseStatement("try { x(); }");
        Assert.Empty(de.Diagnostics);
        Assert.Empty(Assert.IsType<TryStmt>(stmt).Catches);
    }

    [Fact]
    public void Return_without_value_is_null()
    {
        Assert.Null(Assert.IsType<ReturnStmt>(ParseStatement("return;").stmt).Value);
    }

    [Fact]
    public void Defer_expression_form_wraps_in_exprstmt()
    {
        Assert.IsType<ExprStmt>(Assert.IsType<DeferStmt>(ParseStatement("defer f();").stmt).Body);
    }

    [Fact]
    public void Defer_block_form_holds_a_block()
    {
        Assert.IsType<Block>(Assert.IsType<DeferStmt>(ParseStatement("defer { f(); }").stmt).Body);
    }

    [Fact]
    public void Lambda_can_have_a_block_body()
    {
        var lambda = Assert.IsType<LambdaExpr>(Parse("(x) => { return x; }").expr);
        Assert.IsType<Block>(lambda.Body);
    }

    // A block is a delimiter: its statements decide for themselves where a struct initializer may
    // stand. A trailing lambda at the start of a statement kept the statement's ban on its whole
    // body, so 'throw Oops { };' in it read 'Oops' as a value (M6 S5b).
    [Fact]
    public void A_struct_initializer_stands_in_a_trailing_lambda_of_a_statement()
    {
        var (stmt, de) = ParseStatement("xs.forEach { throw Oops { }; };");
        Assert.Empty(de.Diagnostics);
        var call = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(stmt).Expr);
        var body = Assert.IsType<Block>(Assert.IsType<LambdaExpr>(call.Arguments[^1]).Body);
        Assert.IsType<StructInitExpr>(Assert.IsType<ThrowStmt>(Assert.Single(body.Statements)).Value);
    }

    // 02 I3 (N2c): names before a ',' are an initializer's shorthand, mixed with written fields;
    // the same names before '=>' are a trailing block's parameters; one name alone stays a block's
    // here — the sema decides by what the callee names.
    [Fact]
    public void A_name_list_is_an_initializers_shorthand()
    {
        var (expr, de) = Parse("Point { x, y = 2, z }");
        Assert.Empty(de.Diagnostics);
        var init = Assert.IsType<StructInitExpr>(expr);
        Assert.Equal([true, false, true], init.Fields.Select(f => f.IsShorthand));
        Assert.Equal("x", Assert.IsType<IdentifierExpr>(init.Fields[0].Value).Name);
    }

    [Fact]
    public void Names_before_an_arrow_are_a_trailing_blocks_parameters()
    {
        var (expr, de) = Parse("xs.fold(0) { acc, x => acc + x }");
        Assert.Empty(de.Diagnostics);
        var lambda = Assert.IsType<LambdaExpr>(Assert.IsType<CallExpr>(expr).Arguments[^1]);
        Assert.Equal(["acc", "x"], lambda.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void One_name_alone_is_a_trailing_block_except_after_type_arguments()
    {
        var (call, de) = Parse("run { v }");
        Assert.Empty(de.Diagnostics);
        Assert.IsType<LambdaExpr>(Assert.IsType<CallExpr>(call).Arguments[^1]);

        var (init, de2) = Parse("Box<int> { v }");
        Assert.Empty(de2.Diagnostics);
        Assert.True(Assert.Single(Assert.IsType<StructInitExpr>(init).Fields).IsShorthand);
    }

    [Fact]
    public void Match_statement_parses_arms()
    {
        var (stmt, de) = ParseStatement("match (x) { 0 => a(), _ => b() }");
        Assert.False(de.HasErrors);
        var m = Assert.IsType<MatchStmt>(stmt);
        Assert.Equal(2, m.Arms.Length);
        Assert.IsType<LiteralPattern>(m.Arms[0].Pattern);
        Assert.IsType<WildcardPattern>(m.Arms[1].Pattern);
    }

    [Fact]
    public void Match_arm_guard_and_block_body()
    {
        var (stmt, de) = ParseStatement("match (x) { n if n > 0 => { use(n); }, _ => stop() }");
        Assert.False(de.HasErrors);
        var m = Assert.IsType<MatchStmt>(stmt);
        Assert.NotNull(m.Arms[0].Guard);           // 'if n > 0'
        Assert.IsType<Block>(m.Arms[0].Body);       // a block arm
    }

    [Theory]
    [InlineData("let")]
    [InlineData("let x")]
    [InlineData("if")]
    [InlineData("if (a)")]
    [InlineData("while ()")]
    [InlineData("for (x in) {}")]
    [InlineData("do {}")]
    [InlineData("{")]
    public void Statement_parser_never_throws_and_reports(string source)
    {
        var (stmt, de) = ParseStatement(source);
        Assert.NotNull(stmt);
        Assert.True(de.HasErrors);
    }

    // --- declarations ---

    private static (Module module, DiagnosticEngine diag) ParseModule(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, id, de).ParseModule();
        return (module, de);
    }

    [Fact]
    public void Module_captures_header_and_declarations()
    {
        var (m, de) = ParseModule("module app; fn main(): int { return 0; }");
        Assert.False(de.HasErrors);
        Assert.Equal(["app"], m.Header!.Segments);
        var fn = Assert.IsType<FunctionDecl>(Assert.Single(m.Declarations));
        Assert.Equal("main", fn.Name);
        Assert.NotNull(fn.Body);
    }

    [Fact]
    public void Import_alias_and_selective_forms()
    {
        var alias = Assert.IsType<ImportDecl>(Assert.Single(ParseModule("import a.b as C;").module.Declarations));
        Assert.Equal(["a", "b"], alias.Path);
        Assert.Equal("C", Assert.IsType<ImportAlias>(alias.Clause).Alias);

        var sel = Assert.IsType<ImportDecl>(Assert.Single(ParseModule("import a { x, y };").module.Declarations));
        Assert.Equal(["x", "y"], Assert.IsType<ImportSelective>(sel.Clause).Names);
    }

    [Fact]
    public void Empty_selective_import_is_refused()
    {
        // Grammar §2: the list holds at least one name. 'import a { };' parsed silently into an
        // import of nothing.
        var (_, de) = ParseModule("import a { };");
        Assert.True(de.HasErrors);
        Assert.Equal("LYR-PAR0026", de.Diagnostics[0].Code);
    }

    [Fact]
    public void Function_captures_generics_params_return_throws()
    {
        var (m, de) = ParseModule("fn f<T>(x: T): int throws E { return 0; }");
        Assert.False(de.HasErrors);
        var fn = Assert.IsType<FunctionDecl>(m.Declarations[0]);
        Assert.Equal("T", Assert.Single(fn.Generics).Name);
        Assert.Equal("x", Assert.Single(fn.Parameters).Name);
        Assert.IsType<NamedType>(fn.ReturnType);
        Assert.IsType<NamedType>(Assert.Single(fn.Throws!.Types));
    }

    [Fact]
    public void Abstract_function_has_no_body()
    {
        Assert.Null(Assert.IsType<FunctionDecl>(ParseModule("fn getHp(): int;").module.Declarations[0]).Body);
    }

    [Fact]
    public void Throws_without_type_is_the_root()
    {
        // The bare form names no type and means 'Error' (05 E2 K2): an empty list.
        var fn = Assert.IsType<FunctionDecl>(ParseModule("fn risky() throws { }").module.Declarations[0]);
        Assert.NotNull(fn.Throws);
        Assert.Empty(fn.Throws!.Types);
    }

    [Fact]
    public void Params_parameter_flag_is_set()
    {
        var fn = Assert.IsType<FunctionDecl>(ParseModule("fn log(params xs: string[]) { }").module.Declarations[0]);
        Assert.True(Assert.Single(fn.Parameters).IsParams);
    }

    [Fact]
    public void Struct_captures_interfaces_and_ordered_members()
    {
        var (m, de) = ParseModule("struct V :: [Eq] { x: int, fn get(): int { return this.x; } }");
        Assert.False(de.HasErrors);
        var s = Assert.IsType<StructDecl>(m.Declarations[0]);
        Assert.Single(s.Interfaces);
        Assert.IsType<FieldDecl>(s.Members[0]);
        Assert.IsType<FunctionDecl>(s.Members[1]);
    }

    [Fact]
    public void Mut_method_flag_is_set()
    {
        var s = Assert.IsType<StructDecl>(ParseModule("struct S { mut fn go() { } }").module.Declarations[0]);
        Assert.True(Assert.IsType<FunctionDecl>(Assert.Single(s.Members)).IsMut);
    }

    [Fact]
    public void Enum_variant_shapes()
    {
        var (m, de) = ParseModule("enum E { A, B(int), C { x: int } }");
        Assert.False(de.HasErrors);
        var e = Assert.IsType<EnumDecl>(m.Declarations[0]);
        Assert.Equal(3, e.Variants.Length);
        Assert.Null(e.Variants[0].TupleFields);            // A: unit
        Assert.Null(e.Variants[0].StructFields);
        Assert.Single(e.Variants[1].TupleFields!);         // B(int)
        Assert.Single(e.Variants[2].StructFields!);        // C { x: int }
    }

    [Fact]
    public void Generic_constraints_parse()
    {
        var fn = Assert.IsType<FunctionDecl>(ParseModule("fn f<T :: [Ord, Eq]>(): void { }").module.Declarations[0]);
        Assert.Equal(2, Assert.Single(fn.Generics).Constraints.Length);
    }

    [Fact]
    public void Global_var_is_a_module_binding()
    {
        // A module-level 'var' (design/v5/spec/07 V5 G5); Lyric 4 refused it (LYR-PAR0027).
        var (module, diag) = ParseModule("var x = 1;");
        Assert.False(diag.HasErrors);
        Assert.True(Assert.IsType<GlobalBindingDecl>(module.Declarations[0]).Binding.IsMutable);
    }

    [Fact]
    public void Type_alias_parses()
    {
        var (m, de) = ParseModule("type Id = int;");
        Assert.False(de.HasErrors);
        Assert.Equal("Id", Assert.IsType<TypeAliasDecl>(m.Declarations[0]).Name);
    }

    [Fact]
    public void A_generic_type_alias_parses()
    {
        var (m, de) = ParseModule("type Pair<T> = (T, T);");
        Assert.False(de.HasErrors);
        var alias = Assert.IsType<TypeAliasDecl>(m.Declarations[0]);
        Assert.Equal(["T"], alias.Generics.Select(g => g.Name));
        Assert.IsType<TupleType>(alias.Aliased);
    }

    [Theory]
    [InlineData("fn")]
    [InlineData("fn f(")]
    [InlineData("struct")]
    [InlineData("struct S {")]
    [InlineData("import")]
    [InlineData("enum E {")]
    [InlineData("pub")]
    [InlineData("1 + 2;")]
    public void Module_parser_never_throws_and_reports(string source)
    {
        var (m, de) = ParseModule(source);
        Assert.NotNull(m);
        Assert.True(de.HasErrors);
    }

    // --- Patterns + match + if-expression (§6.2/§6.3) ---

    private static (Pattern pattern, DiagnosticEngine diag) ParsePattern(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var pattern = new Parser(sm, id, de).ParsePattern();
        return (pattern, de);
    }

    // 08 Y6 (N2c): 's in [A, B]', '_ in [A, B]', one type bare; inside a tuple the ',' after a
    // bare type is the next element.
    [Fact]
    public void A_type_set_pattern_parses()
    {
        var named = Assert.IsType<TypeSetPattern>(ParsePattern("s in [Circle, Rect]").pattern);
        Assert.Equal("s", named.Name);
        Assert.Equal(2, named.Types.Length);
        var unnamed = Assert.IsType<TypeSetPattern>(ParsePattern("_ in Circle").pattern);
        Assert.Null(unnamed.Name);
        Assert.Single(unnamed.Types);
        var (tuple, de) = ParsePattern("(s in Circle, n)");
        Assert.Empty(de.Diagnostics);
        var elements = Assert.IsType<TuplePattern>(tuple).Elements;
        Assert.IsType<TypeSetPattern>(elements[0]);
        Assert.IsType<BindingPattern>(elements[1]);
    }

    [Fact]
    public void An_empty_type_set_is_refused()
    {
        var (_, de) = ParsePattern("s in []");
        Assert.Equal(["LYR-PAR0049"], de.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Wildcard_and_binding_patterns()
    {
        Assert.IsType<WildcardPattern>(ParsePattern("_").pattern);
        Assert.Equal("x", Assert.IsType<BindingPattern>(ParsePattern("x").pattern).Name);
    }

    [Fact]
    public void Tuple_variant_pattern()
    {
        var v = Assert.IsType<VariantPattern>(ParsePattern("Circle(r)").pattern);
        Assert.Equal(["Circle"], v.Path);
        Assert.Single(v.TupleElements!);
        Assert.Null(v.StructFields);
    }

    [Fact]
    public void Struct_variant_pattern()
    {
        var v = Assert.IsType<VariantPattern>(ParsePattern("Triangle { a, b, c }").pattern);
        Assert.Null(v.TupleElements);
        Assert.Equal(3, v.StructFields!.Length);
    }

    [Fact]
    public void Or_pattern_flattens_alternatives()
    {
        Assert.Equal(3, Assert.IsType<OrPattern>(ParsePattern("1 | 2 | 3").pattern).Alternatives.Length);
    }

    [Fact]
    public void Inclusive_range_pattern()
    {
        Assert.True(Assert.IsType<RangePattern>(ParsePattern("0..=9").pattern).IsInclusive);
    }

    [Theory]
    [InlineData("-y")]      // negated identifier
    [InlineData("-true")]
    [InlineData("3..y")]    // identifier as a range bound
    [InlineData("-x..=9")]
    public void Non_literal_in_a_literal_pattern_is_refused(string input)
    {
        // Grammar §7: a pattern literal is a literal. '-y' used to parse — and the lowering
        // EVALUATED it, a pattern quietly matching a runtime value.
        var (_, de) = ParsePattern(input);
        Assert.True(de.HasErrors);
        Assert.Equal("LYR-PAR0033", de.Diagnostics[0].Code);
    }

    [Fact]
    public void Qualified_path_is_variant_not_binding()
    {
        var v = Assert.IsType<VariantPattern>(ParsePattern("Shape.Circle").pattern);
        Assert.Equal(["Shape", "Circle"], v.Path);
        Assert.Null(v.TupleElements);   // a unit variant, but qualified
        Assert.Null(v.StructFields);
    }

    [Fact]
    public void Match_as_expression()
    {
        var (e, de) = Parse("match (n) { 0 => \"z\", _ => \"o\" }");
        Assert.False(de.HasErrors);
        Assert.Equal(2, Assert.IsType<MatchExpr>(e).Arms.Length);
    }

    [Fact]
    public void If_expression_branches_are_expressions()
    {
        var (e, de) = Parse("if (a) 1 else 2");
        Assert.False(de.HasErrors);
        var ifx = Assert.IsType<IfExpr>(e);
        Assert.IsType<IntLiteralExpr>(ifx.Then);   // the branch is an expression rather than a block
        Assert.IsType<IntLiteralExpr>(ifx.Else);
    }

    [Fact]
    public void Else_if_chain_is_nested_if_expression()
    {
        var ifx = Assert.IsType<IfExpr>(Parse("if (a) 1 else if (b) 2 else 3").expr);
        Assert.IsType<IfExpr>(ifx.Else);           // 'else if' → geschachteltes IfExpr
    }

    [Fact]
    public void If_expression_without_else_reports()
    {
        Assert.Contains(Parse("if (a) 1").diag.Diagnostics, d => d.Code == "LYR-PAR0036");
    }

    [Theory]
    [InlineData("(")]
    [InlineData("Circle(")]
    [InlineData("|")]
    [InlineData("Point {")]
    public void Pattern_parser_never_throws_and_reports(string source)
    {
        var (p, de) = ParsePattern(source);
        Assert.NotNull(p);
        Assert.True(de.HasErrors);
    }

    // --- struct initializers and the '{' disambiguation ---

    [Fact]
    public void Struct_init_parses_fields()
    {
        var (e, de) = Parse("Point { x = 1, y = 2 }");
        Assert.False(de.HasErrors);
        var s = Assert.IsType<StructInitExpr>(e);
        Assert.Equal(["Point"], s.Path);
        Assert.Equal(2, s.Fields.Length);
        Assert.Equal("x", s.Fields[0].Name);
    }

    [Fact]
    public void Struct_init_can_be_empty_and_qualified()
    {
        Assert.Empty(Assert.IsType<StructInitExpr>(Parse("Empty { }").expr).Fields);
        Assert.Equal(["game", "Player"], Assert.IsType<StructInitExpr>(Parse("game.Player { hp = 100 }").expr).Path);
    }

    [Fact]
    public void Struct_init_allowed_in_binding_initializer()
    {
        var (stmt, de) = ParseStatement("let p = Point { x = 1 };");
        Assert.False(de.HasErrors);
        Assert.IsType<StructInitExpr>(Assert.IsType<BindingStmt>(stmt).Initializer);
    }

    [Fact]
    public void Struct_init_re_enabled_inside_call_arguments()
    {
        // The start of a statement forbids a struct initializer, but the argument lies in a delimiter.
        var (stmt, de) = ParseStatement("f(Point { x = 1 });");
        Assert.False(de.HasErrors);
        var call = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(stmt).Expr);
        Assert.IsType<StructInitExpr>(Assert.Single(call.Arguments));
    }

    [Fact]
    public void Bare_struct_init_at_statement_start_is_not_recognized()
    {
        // The '{' disambiguation: 'Foo { … };' as a statement is NOT read as a struct initializer —
        // it is refused in one message (PAR0055), and the braces are read past.
        var (stmt, de) = ParseStatement("Point { x = 1 };");
        Assert.Equal("LYR-PAR0055", Assert.Single(de.Diagnostics).Code);
        Assert.IsType<ErrorExpr>(Assert.IsType<ExprStmt>(stmt).Expr);
    }
}
