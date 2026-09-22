using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A value block (§6.9): a block in value position — a match arm, a block lambda's body — may end
/// in a tail expression without ';', and that expression is the block's value. The tail joins the
/// arm unification and takes the context type like an expression arm; in a match STATEMENT the
/// value goes nowhere, so the tail is held to the expression-statement rule. A block without a
/// tail keeps its old contract: leave on every path, contribute nothing.
/// </summary>
public class ValueBlockTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Compiles(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void A_block_arm_with_a_tail_delivers_the_tail() =>
        Compiles("""
            fn f(b: bool): int {
                return match (b) {
                    true => { let x = 1; x + 1 },
                    false => 0,
                };
            }
            """);

    [Fact]
    public void The_tail_takes_the_context_type_like_an_expression_arm() =>
        Compiles("""
            fn f(b: bool): int8 {
                let v: int8 = match (b) { true => { 1 }, false => 2 };
                return v;
            }
            """);

    [Fact]
    public void A_tail_disagreeing_with_the_other_arms_is_the_arm_error()
    {
        var de = Check("""
            fn f(b: bool): int {
                let x = match (b) { true => { "s" }, false => 2 };
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0016");
    }

    [Fact]
    public void A_block_arm_without_a_tail_must_still_leave()
    {
        var de = Check("""
            fn f(b: bool): int {
                return match (b) { true => { 1 }, false => { let x = 2; } };
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0033");
        Assert.Contains("tail expression", error.Message);
    }

    [Fact]
    public void A_block_arm_that_returns_needs_no_tail() =>
        Compiles("""
            fn f(b: bool): int {
                return match (b) { true => { return 1; }, false => { 2 } };
            }
            """);

    [Fact]
    public void A_throwing_tail_diverges_like_a_throwing_arm() =>
        Compiles("""
            class E :: [Throwable] { fn message(): string { return "e"; } }
            fn f(b: bool): int throws E {
                return match (b) { true => { 1 }, false => { let why = "no"; throw E { } } };
            }
            """);

    [Fact]
    public void In_a_match_statement_a_bare_value_tail_has_no_effect()
    {
        var de = Check("""
            fn f(b: bool): int {
                match (b) { true => { 5 }, false => { } }
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0022");
    }

    [Fact]
    public void In_a_match_statement_a_call_tail_is_fine() =>
        Compiles("""
            import std.io.console { println };
            fn f(b: bool): int {
                match (b) { true => { println("t") }, false => { println("f"); } }
                return 0;
            }
            """);

    // ------------------------------------------------------------------ lambdas

    [Fact]
    public void A_block_lambda_infers_its_type_from_the_tail() =>
        Compiles("""
            fn f(): int {
                let twice = (x: int) => { let y = x * 2; y + 1 };
                let v: int = twice(3);
                return v;
            }
            """);

    [Fact]
    public void A_tail_and_a_return_unify_in_a_block_lambda() =>
        Compiles("""
            fn f(): int {
                let g = (x: int) => { if (x < 0) { return 0; } x * 2 };
                return g(3);
            }
            """);

    [Fact]
    public void A_tail_checks_against_the_declared_return_type()
    {
        var de = Check("""
            fn f(): int {
                let g = (): string => { 1 };
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0001");
    }

    [Fact]
    public void A_tail_satisfies_a_context_function_type() =>
        Compiles("""
            fn apply(f: fn(int) -> int): int { return f(4); }
            fn main(): int { return apply((x) => { let d = x; d * d }); }
            """);

    [Fact]
    public void A_statement_block_admits_no_tail()
    {
        var de = Check("""
            fn f(): int {
                if (true) { 5 }
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0016");
    }

    [Fact]
    public void A_nested_statement_block_inside_a_value_block_admits_no_tail()
    {
        var de = Check("""
            fn f(b: bool): int {
                return match (b) { true => { if (b) { 5 } 1 }, false => 0 };
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0016");
    }
}
