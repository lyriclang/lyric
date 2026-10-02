using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Coroutine sema (design/v5/spec/06 N2, 08 D11): <c>Coroutine&lt;Y, R = void&gt;</c> as a type form,
/// yield only in coroutines with a value type check (SEM0038), the body's 'return' as the
/// coroutine's result — a bare one where the result is void (SEM0039) —, the pulls 'next()',
/// 'result()' and 'isDone()'. The full pipeline through Semantics.Analyze.
/// </summary>
public class CoroutineTests
{
    private const string Prelude = """
        fn fibonacci(): Coroutine<int> {
            var a = 0;
            var b = 1;
            while (true) {
                yield a;
                let next = a + b;
                a = b;
                b = next;
            }
        }
        fn ticker(): Coroutine<void> {
            yield;
            yield;
        }
        fn summed(xs: int[]): Coroutine<int, int> {
            var sum = 0;
            for (x in xs) {
                yield x;
                sum += x;
            }
            return sum;
        }
        """;

    private static (TypeResult types, DiagnosticEngine de, Module module) Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var module = new Parser(sm, id, de).ParseModule();
        comp.AddModule(module);
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);
        return (types, de, module);
    }

    private static List<BindingStmt> Bindings(IEnumerable<Stmt> stmts)
    {
        var acc = new List<BindingStmt>();
        void Walk(IEnumerable<Stmt> ss)
        {
            foreach (var s in ss)
                switch (s)
                {
                    case BindingStmt b: acc.Add(b); break;
                    case Block bl: Walk(bl.Statements); break;
                    case IfStmt i: Walk(i.Then.Statements); if (i.Else is Block eb) Walk(eb.Statements); break;
                    case ForInStmt f: Walk(f.Body.Statements); break;
                }
        }
        Walk(stmts);
        return acc;
    }

    private static (LyrType type, DiagnosticEngine de) LastInit(string body)
    {
        var (types, de, module) = Check(Prelude + "\n" + body);
        var init = module.Declarations.OfType<FunctionDecl>()
            .Where(f => f.Body is not null)
            .SelectMany(f => Bindings(f.Body!.Statements))
            .Last().Initializer!;
        return (types.TypeOf(init), de);
    }

    private static DiagnosticEngine Diags(string body) => Check(Prelude + "\n" + body).de;

    private static void AssertClean(DiagnosticEngine de) =>
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    private static void AssertType(LyrType expected, LyrType actual) =>
        Assert.True(LyrType.Equal(expected, actual), $"expected '{TypeFacts.Display(expected)}', got '{TypeFacts.Display(actual)}'");

    // --- the basic shape ---

    [Fact]
    public void Fibonacci_pattern_checks_clean()
    {
        AssertClean(Diags("")); // the prelude alone: no SEM0017 despite the Coroutine<int> return type
    }

    [Fact]
    public void Calling_a_coroutine_yields_the_coroutine_type()
    {
        var (t, de) = LastInit("fn u() { let co = fibonacci(); }");
        AssertClean(de);
        AssertType(new CoroutineOf(LyrType.Int), t);
    }

    [Fact]
    public void A_driving_loop_checks_clean()
    {
        AssertClean(Diags("""
            fn u() {
                let co = fibonacci();
                for (i in 0..10) {
                    let v = co.next()!;
                }
            }
            """));
    }

    // --- yield rules (SEM0038) ---

    [Fact]
    public void Yield_outside_a_coroutine_body_is_legal_since_4_0()
    {
        // §10a: which coroutine a yield suspends is a runtime fact, so the checker admits it in
        // every function; a yield with no running coroutine is a panic, not a diagnostic.
        Assert.DoesNotContain(Diags("fn u(): int { yield 1; return 0; }").Diagnostics,
            d => d.Code == "LYR-SEM0038");
    }

    [Fact]
    public void Yield_in_a_lambda_is_the_dynamic_kind()
    {
        // Inside a coroutine body a lambda's yields are still the DYNAMIC kind — whose coroutine
        // they meet is decided by who calls the lambda — so nothing is checked against the
        // enclosing coroutine's element type.
        Assert.DoesNotContain(
            Diags("fn co(): Coroutine<int> { let f = (x: int) => { yield 1; return x; }; yield 2; }")
                .Diagnostics, d => d.Code == "LYR-SEM0038");
    }

    [Fact]
    public void Yield_value_type_is_checked()
    {
        Assert.Contains(Diags("""fn co(): Coroutine<int> { yield "nope"; }""").Diagnostics,
            d => d.Code == "LYR-SEM0001");
    }

    [Fact]
    public void Bare_yield_requires_void_coroutine()
    {
        AssertClean(Diags("fn u() { let t = ticker(); }"));
        Assert.Contains(Diags("fn co(): Coroutine<int> { yield; }").Diagnostics, d => d.Code == "LYR-SEM0038");
    }

    // --- return: the coroutine's result (06 A1) ---

    [Fact]
    public void Bare_return_ends_a_coroutine_early()
    {
        AssertClean(Diags("""
            fn take(xs: int[], limit: int): Coroutine<int> {
                var n = 0;
                for (x in xs) {
                    if (n >= limit) { return; }
                    yield x;
                    n += 1;
                }
            }
            """));
    }

    [Fact]
    public void A_value_returned_where_the_result_is_void_is_reported()
    {
        Assert.Contains(Diags("fn co(): Coroutine<int> { yield 1; return 5; }").Diagnostics,
            d => d.Code == "LYR-SEM0039");
    }

    [Fact]
    public void A_coroutine_with_a_result_returns_it()
    {
        AssertClean(Diags("fn u() { let s = summed([1, 2]); }"));
        Assert.Contains(Diags("""fn co(): Coroutine<int, int> { yield 1; return "no"; }""").Diagnostics,
            d => d.Code == "LYR-SEM0001");
    }

    [Fact]
    public void A_coroutine_with_a_result_returns_it_on_every_path()
    {
        Assert.Contains(Diags("fn co(c: bool): Coroutine<int, int> { yield 1; if (c) { return 2; } }").Diagnostics,
            d => d.Code == "LYR-SEM0017");
        Assert.Contains(Diags("fn co(): Coroutine<int, int> { yield 1; return; }").Diagnostics,
            d => d.Code == "LYR-SEM0001");
        // A body that never ends needs no return.
        AssertClean(Diags("fn co(): Coroutine<int, int> { while (true) { yield 1; } }"));
    }

    // --- the type form Coroutine<Y, R> ---

    [Fact]
    public void Coroutine_takes_one_or_two_type_arguments()
    {
        Assert.Contains(Diags("fn u(c: Coroutine) { }").Diagnostics, d => d.Code == "LYR-SEM0026");
        Assert.Contains(Diags("fn u(c: Coroutine<int, int, int>) { }").Diagnostics, d => d.Code == "LYR-SEM0026");
        AssertClean(Diags("fn u(c: Coroutine<int, string>) { }"));
    }

    [Fact]
    public void A_result_type_is_part_of_the_type()
    {
        Assert.Contains(Diags("fn u() { let c: Coroutine<int> = summed([1]); }").Diagnostics,
            d => d.Code == "LYR-SEM0001");
        var (t, de) = LastInit("fn u() { let c = summed([1]); }");
        AssertClean(de);
        Assert.Equal("Coroutine<int, int>", TypeFacts.Display(t));
    }

    [Fact]
    public void Coroutine_type_substitutes_through_generics()
    {
        var (t, de) = LastInit("""
            fn identity<T>(x: T): T { return x; }
            fn u() { let co = identity(fibonacci()); }
            """);
        AssertClean(de);
        AssertType(new CoroutineOf(LyrType.Int), t); // T = Coroutine<int>
    }

    // --- the pulls: next(), result(), isDone() ---

    [Fact]
    public void Next_yields_the_optional_of_the_element_type()
    {
        var (t, de) = LastInit("fn u() { let co = fibonacci(); let v = co.next(); }");
        AssertClean(de);
        AssertType(new Optional(LyrType.Int), t);
    }

    [Fact]
    public void Next_on_a_void_coroutine_answers_bool()
    {
        var (t, de) = LastInit("fn u() { let t = ticker(); let advanced = t.next(); }");
        AssertClean(de);
        AssertType(LyrType.Bool, t);
    }

    [Fact]
    public void Next_on_an_optional_yield_answers_a_double_optional()
    {
        // A yielded null and the end are two states (03 T4 O1): '??int'.
        var (t, de) = LastInit("""
            fn maybe(): Coroutine<?int> { yield null; yield 1; }
            fn u() { let co = maybe(); let v = co.next(); }
            """);
        AssertClean(de);
        AssertType(new Optional(new Optional(LyrType.Int)), t);
    }

    [Fact]
    public void Result_answers_the_optional_of_the_result_type()
    {
        var (t, de) = LastInit("fn u() { let s = summed([1, 2]); let r = s.result(); }");
        AssertClean(de);
        AssertType(new Optional(LyrType.Int), t);
        Assert.Contains(Diags("fn u() { let co = fibonacci(); let r = co.result(); }").Diagnostics,
            d => d.Code == "LYR-SEM0012");
    }

    [Fact]
    public void IsDone_answers_bool()
    {
        var (t, de) = LastInit("fn u() { let co = fibonacci(); let done = co.isDone(); }");
        AssertClean(de);
        AssertType(LyrType.Bool, t);
    }

    [Fact]
    public void Another_member_stays_unknown()
    {
        Assert.Contains(Diags("fn u() { let co = fibonacci(); let v = co.size(); }").Diagnostics,
            d => d.Code == "LYR-SEM0012");
    }

    [Fact]
    public void Resume_is_a_name_since_Lyric_5()
    {
        // Lyric 4's 'resume co' is 'co.next()!' (12 S04); the word is free.
        AssertClean(Diags("fn u(): int { let resume = 1; return resume; }"));
    }
}
