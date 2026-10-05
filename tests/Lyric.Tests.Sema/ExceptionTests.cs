using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Errors in the sema (design/v5/spec/05 E1–E3, E6; spec chapter 06 §1–§3): <c>std.core</c>'s
/// <c>Error</c> as the root of everything thrown, caught and declared (SEM0030); the <c>throws</c>
/// SET (SEM0137); the <c>try</c> mark on every throwing call (SEM0138) and the warning for a mark
/// over nothing (SEM0139); coverage by a catch around the site or by the function's set, on the
/// instance (SEM0034); an implementation throwing at most its member (SEM0042); the thrown set of a
/// function type (03 T17; K3, K4, K6); the try/catch structure (SEM0035, SEM0036); panic returning never; the
/// expression forms (05 E4): <c>try?</c> worth <c>?T</c> unflattened, <c>try!</c> worth <c>T</c>,
/// <c>try e catch (x: A) v</c> unified like match arms, a clause without a value (SEM0033),
/// <c>try?</c> over a call without one (SEM0140); a <c>try</c> no error reaches (SEM0139).
/// </summary>
public class ExceptionTests
{
    private const string Core = """
        module std.core;
        pub interface Error {
            fn message(): string;
            fn cause(): ?Error { return null; }
        }
        pub interface Closeable { fn close(): void throws Error; }
        """;

    private const string Prelude = """
        interface IOError :: [Error] { }
        class NotFound :: [Error] {
            path: string,
            fn message(): string { return this.path; }
        }
        class DbError :: [IOError] {
            fn message(): string { return "db"; }
        }
        struct Box<T> :: [Error] { v: T, fn message(): string { return "box"; } }
        enum Parse :: [Error] { Empty, Bad(int); fn message(): string { return "parse"; } }
        struct Plain { x: int }
        fn mayThrow(): int throws NotFound { return 1; }
        fn mayThrowDb(): int throws DbError { return 1; }
        fn mayThrowAny(): int throws { return 1; }
        fn mayThrowBoth(): int throws [NotFound, Parse] { return 1; }
        fn safe(): int { return 1; }
        """;

    private static (TypeResult types, DiagnosticEngine de, Module module) Check(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var core = sm.AddVirtual("core.lyr", Core);
        comp.AddModule(new Parser(sm, core, de).ParseModule());
        var id = sm.AddVirtual("test.lyr", source);
        var module = new Parser(sm, id, de).ParseModule();
        comp.AddModule(module);
        var types = Semantics.Analyze(comp, comp.Resolve(), de);
        return (types, de, module);
    }

    private static DiagnosticEngine Diags(string body) => Check(Prelude + "\n" + body).de;

    private static void AssertClean(DiagnosticEngine de) =>
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    private static void AssertCode(DiagnosticEngine de, string code) =>
        Assert.True(de.Diagnostics.Any(d => d.Code == code),
            $"expected {code}, got: " + string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    // --- what is thrown (SEM0030) ---

    [Fact]
    public void Throw_of_an_error_class_is_clean() =>
        AssertClean(Diags("""fn t(): int throws NotFound { throw NotFound { path = "x" }; }"""));

    [Fact]
    public void An_enum_and_a_generic_struct_are_throwable() =>
        AssertClean(Diags("""
            fn t(n: int): int throws [Parse, Box<int>] {
                if (n > 0) { throw Parse.Bad(n); }
                throw Box<int> { v = n };
            }
            """));

    [Fact]
    public void An_interface_value_of_an_error_is_throwable() =>
        AssertClean(Diags("fn t(e: IOError): int throws IOError { throw e; }"));

    [Fact]
    public void Throw_of_a_non_error_is_reported() =>
        AssertCode(Diags("fn t() throws { throw Plain { x = 1 }; }"), "LYR-SEM0030");

    [Fact]
    public void A_number_is_not_thrown() =>
        AssertCode(Diags("fn t() throws { throw 5; }"), "LYR-SEM0030");

    [Fact]
    public void Throws_clause_with_a_non_error_type_is_reported() =>
        AssertCode(Diags("fn t(): int throws Plain { return 1; }"), "LYR-SEM0030");

    [Fact]
    public void Catch_of_a_non_error_type_is_reported() =>
        AssertCode(Diags("fn t() { try { safe(); } catch (e: Plain) { } }"), "LYR-SEM0030");

    [Fact]
    public void A_refused_throw_is_not_reported_twice()
    {
        var de = Diags("fn t(): int throws NotFound { throw 5; }");
        Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        AssertCode(de, "LYR-SEM0030");
    }

    // --- the throws set (SEM0137) ---

    [Fact]
    public void A_set_names_each_type_once() =>
        AssertCode(Diags("fn t(): int throws [NotFound, NotFound] { return 1; }"), "LYR-SEM0137");

    [Fact]
    public void The_bare_throws_is_the_root() =>
        AssertClean(Diags("fn t(): int throws { return try mayThrowBoth(); }"));

    [Fact]
    public void A_set_covers_each_of_its_types() =>
        AssertClean(Diags("fn t(): int throws [Parse, NotFound] { return try mayThrowBoth(); }"));

    [Fact]
    public void A_set_missing_one_type_does_not_cover_the_call() =>
        AssertCode(Diags("fn t(): int throws NotFound { return try mayThrowBoth(); }"), "LYR-SEM0034");

    // --- the try mark (SEM0138, SEM0139) ---

    [Fact]
    public void An_unmarked_call_is_reported() =>
        AssertCode(Diags("fn t(): int throws NotFound { return mayThrow(); }"), "LYR-SEM0138");

    [Fact]
    public void Try_covers_everything_to_its_right() =>
        AssertClean(Diags("fn t(): int throws NotFound { return try mayThrow() + mayThrow(); }"));

    [Fact]
    public void A_try_block_marks_its_body() =>
        AssertClean(Diags("fn t() { try { let x = mayThrow(); } catch (e: NotFound) { } }"));

    [Fact]
    public void A_try_over_nothing_that_throws_warns()
    {
        var de = Diags("fn t(): int { return try safe(); }");
        AssertClean(de);
        AssertCode(de, "LYR-SEM0139");
    }

    [Fact]
    public void A_try_block_over_nothing_that_throws_warns()
    {
        var de = Diags("fn t() { try { safe(); } catch (_) { } }");
        AssertClean(de);
        AssertCode(de, "LYR-SEM0139");
    }

    [Fact]
    public void A_throw_needs_no_mark() =>
        AssertClean(Diags("""fn t(): int throws NotFound { throw NotFound { path = "x" }; }"""));

    [Fact]
    public void A_marked_statement_is_the_call_it_marks() =>
        AssertClean(Diags("fn t() throws NotFound { try mayThrow(); }"));

    // --- coverage (SEM0034) ---

    [Fact]
    public void A_marked_call_nothing_covers_is_reported() =>
        AssertCode(Diags("fn t(): int { return try mayThrow(); }"), "LYR-SEM0034");

    [Fact]
    public void Coverage_is_on_the_instance()
    {
        AssertCode(Diags("""fn t(): int throws Box<int> { throw Box<string> { v = "s" }; }"""), "LYR-SEM0034");
        AssertClean(Diags("""fn t(): int throws Box<string> { throw Box<string> { v = "s" }; }"""));
    }

    [Fact]
    public void Call_covered_by_interface_throws_is_clean() =>
        AssertClean(Diags("fn t(): int throws IOError { return try mayThrowDb(); }"));

    [Fact]
    public void Interface_throws_does_not_cover_unrelated_type() =>
        AssertCode(Diags("fn t(): int throws IOError { return try mayThrow(); }"), "LYR-SEM0034");

    [Fact]
    public void Call_handled_by_matching_catch_is_clean() =>
        AssertClean(Diags("fn t() { try { let x = mayThrow(); } catch (e: NotFound) { } }"));

    [Fact]
    public void Call_handled_by_interface_catch_is_clean() =>
        AssertClean(Diags("fn t() { try { mayThrowDb(); } catch (e: IOError) { } }"));

    [Fact]
    public void Call_handled_by_catch_all_is_clean() =>
        AssertClean(Diags("fn t() { try { mayThrow(); } catch (_) { } }"));

    [Fact]
    public void A_catch_of_the_root_covers_everything() =>
        AssertClean(Diags("fn t() { try { mayThrowAny(); } catch (e: Error) { } }"));

    [Fact]
    public void Non_matching_catch_does_not_handle() =>
        AssertCode(Diags("fn t() { try { mayThrow(); } catch (e: DbError) { } }"), "LYR-SEM0034");

    [Fact]
    public void Outer_try_handles_through_inner() =>
        AssertClean(Diags("""
            fn t() {
                try {
                    try { mayThrow(); } catch (e: DbError) { }
                } catch (e: NotFound) { }
            }
            """));

    [Fact]
    public void A_bare_throws_call_needs_a_catch_all_or_a_bare_clause()
    {
        AssertCode(Diags("fn t() { try { mayThrowAny(); } catch (e: NotFound) { } }"), "LYR-SEM0034");
        AssertClean(Diags("fn u(): int throws { return try mayThrowAny(); }"));
        AssertClean(Diags("fn v() { try { mayThrowAny(); } catch (_) { } }"));
    }

    // --- catch bodies, rethrow, defer ---

    [Fact]
    public void Throw_in_catch_body_is_not_caught_by_its_own_try() =>
        AssertCode(Diags("fn t() { try { mayThrow(); } catch (e) { throw e; } }"), "LYR-SEM0034");

    [Fact]
    public void Rethrow_with_bare_throws_is_clean() =>
        AssertClean(Diags("fn t(): int throws { try { return mayThrow(); } catch (e) { throw e; } }"));

    [Fact]
    public void A_defer_body_is_a_site_of_its_scope()
    {
        AssertCode(Diags("fn t() { defer try mayThrow(); }"), "LYR-SEM0034");
        AssertCode(Diags("fn w() throws NotFound { defer mayThrow(); }"), "LYR-SEM0138");
        AssertClean(Diags("fn u() throws NotFound { defer try mayThrow(); }"));
    }

    // --- lambdas and context boundaries ---

    [Fact]
    public void A_lambda_body_is_its_own_context()
    {
        // The enclosing function's set does not cover it: the lambda runs later and throws what its
        // own type says (05 E2 K3) — here nothing, the position expecting a function that cannot.
        AssertCode(Diags("fn t() throws NotFound { let f: fn(int) -> int = (x: int) => try mayThrow(); }"), "LYR-SEM0034");
        AssertCode(Diags("fn t() throws NotFound { let f = (x: int) => mayThrow(); }"), "LYR-SEM0138");
    }

    [Fact]
    public void An_enclosing_try_does_not_mark_lambda_bodies() =>
        AssertCode(Diags("fn t() { try { let f = (x: int) => mayThrow(); } catch (_) { } }"), "LYR-SEM0138");

    [Fact]
    public void A_global_initializer_has_no_handler()
    {
        AssertCode(Diags("let g = try mayThrow();"), "LYR-SEM0034");
        AssertCode(Diags("let g = mayThrow();"), "LYR-SEM0138");
    }

    // --- an implementation throws at most its member (K6, SEM0042) ---

    [Fact]
    public void An_implementation_throws_at_most_its_member()
    {
        const string risky = "interface Risky { fn run(): int throws IOError; }\n";
        AssertClean(Diags(risky + "struct S :: [Risky] { fn run(): int throws DbError { return 1; } }"));
        AssertClean(Diags(risky + "struct S :: [Risky] { fn run(): int { return 1; } }"));
        AssertCode(Diags(risky + "struct S :: [Risky] { fn run(): int throws NotFound { return 1; } }"), "LYR-SEM0042");
    }

    // --- main may throw (08 D18) ---

    [Fact]
    public void Main_may_declare_throws()
    {
        AssertClean(Diags("fn main(): int throws NotFound { return try mayThrow(); }"));
        AssertClean(Diags("fn main(): void throws { try mayThrowAny(); }"));
    }

    // --- a throwing function as a value (03 T17): its set in its type; SEM0037 retired ---

    [Fact]
    public void A_throwing_function_is_a_value_that_throws()
    {
        AssertClean(Diags("fn t(): int throws NotFound { let f = mayThrow; return try f(); }"));
        AssertCode(Diags("fn t(): int throws NotFound { let f = mayThrow; return f(); }"), "LYR-SEM0138");
        AssertCode(Diags("fn t(): int { let f = mayThrow; return try f(); }"), "LYR-SEM0034");
        // A method bound to its object likewise.
        AssertClean(Diags("class C { fn m(): int throws NotFound { return 1; } }\n"
            + "fn t(c: C): int throws NotFound { let f = c.m; return try f(); }"));
    }

    // --- the thrown set of a function type (03 T17; 05 E2 K3, K4, K6) ---

    private const string Each = "fn each<E :: [Error]>(f: fn() -> int throws E): int throws E { return try f(); }\n";

    [Fact]
    public void The_set_is_part_of_the_type_and_has_no_order()
    {
        AssertClean(Diags("fn t(k: fn() -> int throws [NotFound, DbError]): (fn() -> int throws [DbError, NotFound]) { return k; }"));
        AssertCode(Diags("fn t(k: fn() -> int throws NotFound): fn() -> int { return k; }"), "LYR-SEM0001");
    }

    [Fact]
    public void A_value_that_throws_less_fits_a_type_that_throws_more()
    {
        // K6, element by element: an interface covers its conformers. Never the way back.
        AssertClean(Diags("fn t(): (fn() -> int throws NotFound) { return safe; }"));
        AssertClean(Diags("fn t(k: fn() -> int throws DbError): (fn() -> int throws [IOError, NotFound]) { return k; }"));
        AssertCode(Diags("fn t(k: fn() -> int throws [NotFound, DbError]): (fn() -> int throws NotFound) { return k; }"), "LYR-SEM0001");
    }

    [Fact]
    public void A_written_set_follows_a_declared_ones_rules_once()
    {
        AssertCode(Diags("fn t(k: fn() -> int throws Plain) { }"), "LYR-SEM0030");
        // The parameter's type is resolved again at every call of 't'; the twice-named type is
        // reported once.
        var de = Diags("fn t(k: fn() -> int throws [NotFound, NotFound]) { }\nfn u() { t(safe); t(safe); }");
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0137");
    }

    [Fact]
    public void A_lambda_is_held_to_the_set_its_position_expects()
    {
        const string run = "fn run(f: fn() -> int throws NotFound): int throws NotFound { return try f(); }\n";
        AssertClean(Diags(run + "fn t(): int throws NotFound { return try run(() => try mayThrow()); }"));
        AssertCode(Diags(run + "fn t(): int throws [NotFound, DbError] { return try run(() => try mayThrowDb()); }"), "LYR-SEM0034");
    }

    [Fact]
    public void A_lambda_without_a_position_throws_what_its_body_lets_escape()
    {
        AssertClean(Diags("fn t(): int throws NotFound { let f = (x: int) => try mayThrow(); return try f(1); }"));
        AssertCode(Diags("fn t(): int throws NotFound { let f = (x: int) => try mayThrow(); return f(1); }"), "LYR-SEM0138");
        // What a try inside the body takes does not escape it.
        AssertClean(Diags("fn t(): int { let f = (x: int) => try mayThrow() catch (_: NotFound) 0; return f(1); }"));
    }

    [Fact]
    public void A_lambda_may_write_its_set()
    {
        AssertClean(Diags("fn t(): int throws NotFound { let f = (x: int): int throws NotFound => try mayThrow(); return try f(1); }"));
        AssertCode(Diags("fn t() { let f = (x: int): int throws DbError => try mayThrow(); }"), "LYR-SEM0034");
    }

    [Fact]
    public void A_type_parameter_in_a_set_is_inferred_from_the_argument()
    {
        AssertClean(Diags(Each + "fn t(): int throws NotFound { return try each(() => try mayThrow()); }"));
        AssertClean(Diags(Each + "fn t(): int throws NotFound { return try each(mayThrow); }"));
        // Nothing thrown binds 'never': the call throws nothing, and a mark over it is warned about.
        AssertClean(Diags(Each + "fn t(): int { return each(safe); }"));
        AssertCode(Diags(Each + "fn t(): int { return try each(safe); }"), "LYR-SEM0139");
    }

    [Fact]
    public void Several_thrown_types_bind_the_root()
    {
        // K7's join: two types make 'Error', which a set of the two does not cover.
        AssertCode(Diags(Each + "fn t(): int throws [NotFound, Parse] { return try each(mayThrowBoth); }"), "LYR-SEM0034");
        AssertClean(Diags(Each + "fn t(): int throws { return try each(mayThrowBoth); }"));
    }

    [Fact]
    public void A_set_names_a_type_parameter_only_under_a_constraint() =>
        AssertCode(Diags("fn bad<E>(f: fn() -> int throws E): int throws E { return try f(); }"), "LYR-SEM0030");

    [Fact]
    public void Plain_function_as_value_is_fine() =>
        AssertClean(Diags("fn t() { let f = safe; }"));

    // --- try/catch structure (SEM0035/0036) ---

    [Fact]
    public void Catch_all_must_be_last() =>
        AssertCode(Diags("fn t() { try { mayThrow(); } catch (e) { } catch (x: NotFound) { } }"), "LYR-SEM0035");

    [Fact]
    public void Try_without_catch_is_reported() =>
        AssertCode(Diags("fn t() { try { safe(); } }"), "LYR-SEM0036");

    // --- catch bindings ---

    [Fact]
    public void Untyped_catch_binds_the_root_with_message() =>
        // e: Error, so e.message() is a string; no SEM0018 on e, because the catch assigns it.
        AssertClean(Diags("fn t() { try { mayThrow(); } catch (e) { let m: string = e.message(); } }"));

    [Fact]
    public void Typed_catch_binds_the_declared_type() =>
        AssertClean(Diags("fn t() { try { mayThrow(); } catch (e: NotFound) { let p: string = e.path; } }"));

    // --- panic: never diverges but does not throw ---

    [Fact]
    public void Panic_counts_as_divergence_for_return_coverage()
    {
        AssertClean(Diags("""fn t(): int { panic("boom"); }"""));
        AssertClean(Diags("""fn u(n: int): int { if (n > 0) { return 1; } panic("unreachable"); }"""));
    }

    [Fact]
    public void Panic_needs_no_throws_declaration() =>
        AssertClean(Diags("""fn t() { panic("boom"); }"""));

    // --- every initializer is a site, including the one a destructuring requires ---

    /// <summary>
    /// A destructuring binding was the one statement the walk had no case for, so its initializer
    /// was never a call site at all: <c>let (a, b) = mk();</c> with a throwing <c>mk</c> compiled
    /// clean in a function that declares nothing.
    /// </summary>
    [Fact]
    public void A_destructuring_initializer_is_a_call_site() =>
        AssertCode(Diags("""
            fn pair(): (int, int) throws NotFound { return (1, 2); }
            fn t(): int { let (a, b) = pair(); return a + b; }
            """), "LYR-SEM0138");

    [Fact]
    public void A_handled_destructuring_initializer_is_clean() =>
        AssertClean(Diags("""
            fn pair(): (int, int) throws NotFound { return (1, 2); }
            fn t(): int {
                try { let (a, b) = pair(); return a + b; }
                catch (e: NotFound) { return 0; }
            }
            """));

    // --- the expression forms: try?, try!, try … catch (05 E4) ---

    private static LyrType TypeOfTry(string body)
    {
        var (types, de, module) = Check(Prelude + "\n" + body);
        AssertClean(de);
        var tried = module.Declarations.OfType<FunctionDecl>().Where(f => f.Name == "t")
            .SelectMany(f => Descendants(f.Body!)).OfType<TryExpr>().First();
        return types.TypeOf(tried);
    }

    private static IEnumerable<Node> Descendants(Node node) =>
        AstChildren.Of(node).SelectMany(child => Descendants(child).Prepend(child));

    [Fact]
    public void Try_question_is_worth_an_optional() =>
        Assert.Equal("?int", TypeFacts.Display(TypeOfTry("fn t(): ?int { return try? mayThrow(); }")));

    [Fact]
    public void Try_question_does_not_flatten() =>
        // "Failed" stays apart from "gave null": the optional operand is wrapped once more.
        Assert.Equal("??int", TypeFacts.Display(TypeOfTry(
            "fn opt(): ?int throws NotFound { return null; }\nfn t(): ??int { return try? opt(); }")));

    [Fact]
    public void Try_bang_is_worth_the_value() =>
        Assert.Equal("int", TypeFacts.Display(TypeOfTry("fn t(): int { return try! mayThrow(); }")));

    [Fact]
    public void The_expression_form_unifies_its_value_and_its_clauses()
    {
        Assert.Equal("int", TypeFacts.Display(TypeOfTry("fn t() { let x = try mayThrow() catch (_) 0; }")));
        // A null clause makes the value optional, as a null arm does; a clause that leaves adds nothing.
        Assert.Equal("?int", TypeFacts.Display(TypeOfTry("fn t() { let x = try mayThrow() catch (_) null; }")));
        Assert.Equal("int", TypeFacts.Display(TypeOfTry("fn t() { let x = try mayThrow() catch (_) { return; }; }")));
    }

    [Fact]
    public void Clauses_of_another_type_do_not_unify() =>
        AssertCode(Diags("""fn t() { let x = try mayThrow() catch (_) "none"; }"""), "LYR-SEM0016");

    [Fact]
    public void A_context_types_every_part() =>
        AssertClean(Diags("fn t() { let x: ?int = try mayThrow() catch (_: NotFound) null; }"));

    [Fact]
    public void Try_question_and_try_bang_take_every_error() =>
        AssertClean(Diags("fn t(): int { let a = try? mayThrowBoth(); return try! mayThrowAny(); }"));

    [Fact]
    public void The_expression_forms_clauses_take_what_they_cover()
    {
        AssertClean(Diags("fn t(): int { return try mayThrow() catch (_: NotFound) 0; }"));
        AssertCode(Diags("fn t(): int { return try mayThrowBoth() catch (_: NotFound) 0; }"), "LYR-SEM0034");
    }

    [Fact]
    public void A_clause_of_the_expression_form_is_not_inside_its_own_try() =>
        // The throw in the first clause passes its sister: nothing covers 'Parse' (E9 C6).
        AssertCode(Diags("fn t(): int { return try mayThrow() catch (_: NotFound) throw Parse.Empty catch (_: Parse) 1; }"),
            "LYR-SEM0034");

    [Fact]
    public void A_clause_without_a_value_leaves()
    {
        AssertCode(Diags("fn t(): int { return try mayThrow() catch (_) { safe(); }; }"), "LYR-SEM0033");
        AssertClean(Diags("fn t(): int { return try mayThrow() catch (_) { return 0; }; }"));
    }

    [Fact]
    public void A_valueless_expression_form_needs_no_value_from_its_clauses() =>
        AssertClean(Diags("fn s(): void throws NotFound { }\nfn t() { try s() catch (_) { safe(); }; }"));

    [Fact]
    public void Try_question_over_a_call_without_a_value_stands_as_a_statement()
    {
        const string s = "fn s(): void throws NotFound { }\n";
        AssertClean(Diags(s + "fn t() { try? s(); }"));
        AssertCode(Diags(s + "fn t() { let x = try? s(); }"), "LYR-SEM0140");
    }

    [Fact]
    public void A_catch_all_stands_last_in_the_expression_form_too() =>
        AssertCode(Diags("fn t(): int { return try mayThrow() catch (_) 0 catch (_: NotFound) 1; }"), "LYR-SEM0035");

    [Fact]
    public void A_signed_try_over_nothing_warns()
    {
        var de = Diags("fn t(): ?int { return try? safe(); }");
        AssertClean(de);
        AssertCode(de, "LYR-SEM0139");
    }

    [Fact]
    public void A_try_block_whose_errors_an_inner_try_takes_warns()
    {
        // Nothing reaches the block's clauses: the try? inside takes all of it.
        var de = Diags("fn t() { try { let a = try? mayThrow(); } catch (_) { } }");
        AssertClean(de);
        AssertCode(de, "LYR-SEM0139");
    }

    [Fact]
    public void An_error_an_inner_clause_misses_reaches_the_outer_try()
    {
        var de = Diags("""
            fn t() {
                try {
                    try { mayThrow(); } catch (_: DbError) { }
                } catch (_: NotFound) { }
            }
            """);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0139");
    }

    [Fact]
    public void What_a_try_question_operand_assigns_is_not_definite_after_it() =>
        AssertCode(Diags("fn t(): int { var x: int; let y = try? (x = mayThrow()); return x; }"), "LYR-SEM0018");

    // --- the clauses: the set form, C1, C2, the set a binding carries (05 E9, E2 K7) ---

    [Fact]
    public void A_set_clause_covers_each_of_its_types()
    {
        AssertClean(Diags("fn t() { try { mayThrowBoth(); } catch (_ in [NotFound, Parse]) { } }"));
        AssertCode(Diags("fn t() { try { mayThrowBoth(); } catch (_ in NotFound) { } }"), "LYR-SEM0034");
    }

    [Fact]
    public void A_set_names_errors_only() =>
        AssertCode(Diags("fn t() { try { mayThrow(); } catch (_ in [NotFound, Plain]) { } }"), "LYR-SEM0030");

    [Fact]
    public void A_type_caught_twice_is_refused()
    {
        AssertCode(Diags("fn t() { try { mayThrowBoth(); } catch (_: Parse) { } catch (_: Parse) { } catch (_) { } }"), "LYR-SEM0141");
        AssertCode(Diags("fn t() { try { mayThrowBoth(); } catch (_ in [Parse, Parse]) { } catch (_) { } }"), "LYR-SEM0141");
        AssertCode(Diags("fn t() { try { mayThrowBoth(); } catch (_ in [Parse, NotFound]) { } catch (_: Parse) { } }"), "LYR-SEM0141");
    }

    [Fact]
    public void A_clause_a_clause_above_takes_whole_is_refused()
    {
        // An interface takes its conformers, the root everything (C2, Java's reading of it).
        AssertCode(Diags("fn t() { try { mayThrowDb(); } catch (_: IOError) { } catch (_: DbError) { } }"), "LYR-SEM0142");
        AssertCode(Diags("fn t() { try { mayThrowDb(); } catch (_ in [IOError, DbError]) { } }"), "LYR-SEM0142");
        AssertCode(Diags("fn t() { try { mayThrowDb(); } catch (_: Error) { } catch (_: DbError) { } }"), "LYR-SEM0142");
    }

    [Fact]
    public void The_other_way_round_both_clauses_are_reached() =>
        // A conformer before its interface: other IOErrors still reach the second clause.
        AssertClean(Diags("fn t() { try { mayThrowDb(); } catch (_: DbError) { } catch (_: IOError) { } }"));

    [Fact]
    public void A_clause_below_the_catch_all_is_refused_once()
    {
        var de = Diags("fn t() { try { mayThrow(); } catch (_) { } catch (_: NotFound) { } }");
        Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        AssertCode(de, "LYR-SEM0035");
    }

    [Fact]
    public void A_try_without_a_clause_is_one_error()
    {
        var de = Diags("fn t() { try { safe(); } }");
        Assert.Equal("LYR-SEM0036", Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error).Code);
    }

    [Fact]
    public void The_catch_all_rethrows_exactly_what_reached_it()
    {
        // K7: 'e' carries what the clauses above left — 'Parse' — and 'throw e' throws just that.
        AssertClean(Diags("fn t(): int throws Parse { try { return mayThrowBoth(); } catch (_: NotFound) { return 0; } catch (e) { throw e; } }"));
        AssertCode(Diags("fn t(): int throws Parse { try { return mayThrowBoth(); } catch (e) { throw e; } }"), "LYR-SEM0034");
    }

    [Fact]
    public void A_set_clause_rethrows_its_set() =>
        AssertClean(Diags("fn t(): int throws [NotFound, Parse] { try { return mayThrowBoth(); } catch (e in [NotFound, Parse]) { throw e; } }"));

    [Fact]
    public void Stored_the_binding_is_an_error() =>
        // The set is the binding's, not a second type: 'x' is an Error, and so is what it throws.
        AssertCode(Diags("fn t(): int throws Parse { try { return mayThrowBoth(); } catch (_: NotFound) { return 0; } catch (e) { let x = e; throw x; } }"),
            "LYR-SEM0034");

    [Fact]
    public void A_match_over_the_set_needs_no_default()
    {
        AssertClean(Diags("""fn t(): string { try { return f"{mayThrowBoth()}"; } catch (e) { return match (e) { _: NotFound => "n", _: Parse => "p" }; } }"""));
        AssertClean(Diags("""fn t(): string { try { return f"{mayThrowDb()}"; } catch (e) { return match (e) { _: IOError => "io" }; } }"""));
    }

    [Fact]
    public void A_match_over_the_set_names_what_it_misses()
    {
        var de = Diags("""fn t(): string { try { return f"{mayThrowBoth()}"; } catch (e) { return match (e) { _: NotFound => "n" }; } }""");
        AssertCode(de, "LYR-SEM0050");
        Assert.Contains(de.Diagnostics, d => d.Message.Contains("'_: Parse'"));
    }

    [Fact]
    public void A_match_over_a_set_clause_needs_no_default() =>
        AssertClean(Diags("""fn t(): string { try { return f"{mayThrowBoth()}"; } catch (e in [NotFound, Parse]) { return match (e) { _: NotFound => "n", _: Parse => "p" }; } }"""));

    [Fact]
    public void A_typed_binding_carries_no_set() =>
        // 'e' is a NotFound, a type of its own: a match over it is the type's question.
        AssertCode(Diags("""fn t(): string { try { return f"{mayThrow()}"; } catch (e: NotFound) { return match (e) { _: NotFound => "n" }; } }"""),
            "LYR-SEM0131");

    // --- using let (05 E7 R1–R6) ---

    private const string Resources = """
        class Res :: [Closeable] {
            fn close(): void throws NotFound { }
            fn check(): void { }
        }
        class Unclosable { fn close(): void { } }
        fn open(): Res { return Res { }; }
        fn keep(r: Res): void { }

        """;

    [Fact]
    public void Using_binds_a_closeable() =>
        AssertCode(Diags(Resources + "fn t() { using let p = Unclosable { }; }"), "LYR-SEM0143");

    [Fact]
    public void What_the_close_throws_is_a_site_of_the_scope()
    {
        AssertCode(Diags(Resources + "fn t() { using let r = open(); }"), "LYR-SEM0034");
        AssertClean(Diags(Resources + "fn t() throws NotFound { using let r = open(); r.check(); }"));
        AssertClean(Diags(Resources + "fn t() { try { using let r = open(); } catch (_: NotFound) { } }"));
    }

    [Fact]
    public void A_using_binding_is_not_unused() =>
        Assert.DoesNotContain(Diags(Resources + "fn t() throws NotFound { using let r = open(); }").Diagnostics,
            d => d.Code == "LYR-SEM0071");

    [Fact]
    public void A_dropped_closeable_warns() =>
        AssertCode(Diags(Resources + "fn t() { open(); }"), "LYR-SEM0144");

    [Fact]
    public void A_closeable_used_only_for_its_other_methods_warns() =>
        AssertCode(Diags(Resources + "fn t() { let r = open(); r.check(); }"), "LYR-SEM0144");

    [Fact]
    public void A_closeable_closed_passed_or_using_bound_does_not_warn()
    {
        Assert.DoesNotContain(Diags(Resources + "fn t() throws NotFound { let r = open(); try r.close(); }").Diagnostics,
            d => d.Code == "LYR-SEM0144");
        Assert.DoesNotContain(Diags(Resources + "fn t() { let r = open(); keep(r); }").Diagnostics,
            d => d.Code == "LYR-SEM0144");
        Assert.DoesNotContain(Diags(Resources + "fn t() throws NotFound { using let r = open(); r.check(); }").Diagnostics,
            d => d.Code == "LYR-SEM0144");
    }

    [Fact]
    public void What_the_operand_assigns_counts_when_every_clause_leaves() =>
        AssertClean(Diags("fn t(): int { var x: int; let y = try (x = mayThrow()) catch (_) { return 0; }; return x; }"));
}
