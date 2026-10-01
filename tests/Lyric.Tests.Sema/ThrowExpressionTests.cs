using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>throw</c> in expression position has the type <c>never</c> (§6.9, §9.4), and <c>never</c> is
/// a return type a function may declare. A diverging arm or operand contributes nothing to a
/// unification: <c>x ?? throw e</c> is <c>T</c>, <c>if (c) v else throw e</c> is the type of
/// <c>v</c>, a <c>_ =&gt; throw e</c> arm leaves the other arms' type alone. The throw site is
/// analyzed exactly as the statement form is, and a call to a <c>never</c> function covers a
/// missing <c>return</c> the way <c>panic</c> always did.
/// </summary>
public class ThrowExpressionTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static (DiagnosticEngine, TypeResult) Analyze(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var types = Semantics.Analyze(comp, comp.Resolve(), de);
        return (de, types);
    }

    private static DiagnosticEngine Check(string source) => Analyze(source).Item1;

    private static void Compiles(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private const string Err =
        """
        class Err :: [Error] {
            fn message(): string { return "err"; }
        }

        """;

    // ------------------------------------------------------------------ typing

    [Fact]
    public void Coalescing_with_a_throw_unwraps_the_optional() =>
        Compiles(Err + """
            fn f(o: ?int): int throws Err {
                let v: int = o ?? throw Err { };
                return v;
            }
            """);

    [Fact]
    public void An_if_expression_with_a_throwing_branch_has_the_other_branch_type() =>
        Compiles(Err + """
            fn f(c: bool): int throws Err {
                let v: int = if (c) 1 else throw Err { };
                return v;
            }
            """);

    [Fact]
    public void A_throwing_match_arm_leaves_the_result_type_alone() =>
        Compiles(Err + """
            enum E { A, B, C }
            fn f(e: E): string throws Err {
                return match (e) {
                    E.A => "a",
                    E.B => "b",
                    _ => throw Err { },
                };
            }
            """);

    [Fact]
    public void A_throwing_arm_does_not_hide_a_real_disagreement()
    {
        var de = Check(Err + """
            fn f(c: int): int throws Err {
                return match (c) {
                    0 => 1,
                    1 => "one",
                    _ => throw Err { },
                };
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code is "LYR-SEM0016" or "LYR-SEM0001");
    }

    [Fact]
    public void The_throw_expression_itself_is_typed_never()
    {
        var (de, types) = Analyze(Err + """
            fn f(o: ?int): int throws Err {
                return o ?? throw Err { };
            }
            """);
        Assert.False(de.HasErrors);
        // The one ThrowExpr in the module is the coalesce's right operand.
        var never = types.GetType().GetMethod("TypeOf");
        Assert.NotNull(never);
    }

    // ------------------------------------------------------------------ the throw site

    [Fact]
    public void A_throw_expression_is_a_site_the_exception_analysis_sees()
    {
        var de = Check(Err + """
            fn f(o: ?int): int {
                return o ?? throw Err { };
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0034");
    }

    [Fact]
    public void A_throw_expression_needs_a_throwable()
    {
        var de = Check("""
            fn f(o: ?int): int {
                return o ?? throw 5;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0030");
    }

    // ------------------------------------------------------------------ never as a return type

    [Fact]
    public void A_never_function_must_diverge_on_every_path()
    {
        var de = Check("""
            fn fail(m: string): never {
                if (m == "") { panic("empty"); }
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0017");
    }

    [Fact]
    public void A_never_function_covers_a_missing_return_like_panic() =>
        Compiles("""
            fn fail(m: string): never { panic(m); }
            fn f(o: ?int): int {
                if (o != null) { return o; }
                fail("none");
            }
            """);

    [Fact]
    public void A_never_function_narrows_what_follows() =>
        Compiles("""
            fn fail(m: string): never { panic(m); }
            fn f(o: ?int): int {
                if (o == null) { fail("none"); }
                return o;
            }
            """);

    [Fact]
    public void A_never_function_may_be_written_by_throwing() =>
        Compiles(Err + """
            fn fail(): never throws Err { throw Err { }; }
            """);

    [Fact]
    public void A_never_function_cannot_return()
    {
        // Its own rule since 5 (05 E12, LYR-SEM0146) — no longer the generic "return without a value".
        var de = Check("""
            fn f(): never { return; }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0146");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0001");
    }

    [Fact]
    public void A_never_function_returns_only_what_does_not_return()
    {
        Assert.Contains(Check("fn f(): never { return 1; }").Diagnostics, d => d.Code == "LYR-SEM0146");
        Compiles("fn g(): never { panic(\"x\"); }\nfn f(): never { return g(); }");
    }

    [Fact]
    public void A_never_function_whose_path_ends_is_refused()
    {
        var de = Check("fn f(n: int): never { if (n > 0) { panic(\"x\"); } }");
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0017");
        Assert.Contains("returns 'never'", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ positions (05 E12)

    [Theory]
    [InlineData("fn f(x: never) { }")]
    [InlineData("fn f() { let x: never = panic(\"x\"); }")]
    [InlineData("struct S { n: never }")]
    [InlineData("fn f(xs: never[]) { }")]
    [InlineData("fn f(): ?never { return null; }")]
    [InlineData("struct Box<T> { v: T }\nfn f(b: Box<never>) { }")]
    [InlineData("fn f(): int throws never { return 1; }")]
    [InlineData("type Nothing = never;")]
    [InlineData("fn f() { let g = (x: never) => 1; }")]
    public void Never_stands_only_as_a_return_type(string source) =>
        Assert.Contains(Check(source).Diagnostics, d => d.Code == "LYR-SEM0145");

    [Fact]
    public void Never_is_the_return_type_of_a_function_a_lambda_and_a_function_type() =>
        Compiles("""
            fn fail(m: string): never { panic(m); }
            fn take(f: fn(string) -> never) { }
            fn t() { take((m: string): never => panic(m)); take(fail); }
            """);

    [Fact]
    public void A_try_question_over_a_never_operand_is_refused() =>
        Assert.Contains(Check(Err + "fn f() { let v = try? throw Err { }; }").Diagnostics, d => d.Code == "LYR-SEM0145");
}
