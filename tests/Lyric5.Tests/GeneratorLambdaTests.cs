using Lyric.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Generator lambdas in the front end (design/v5/spec/08 Y11 F5, D11; M6 S2c), against
/// <c>stdlib5</c>: a lambda with a yield of its own is <c>fn(…) -> Coroutine&lt;Y, R&gt;</c>, its
/// yields and returns checked against a coroutine context or unified into one; calling it throws
/// nothing, its pulls throw what its body lets escape. A function that returns a coroutine without
/// yielding is an ordinary function (D11).
/// </summary>
public class GeneratorLambdaTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static string[] Codes(string source)
    {
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer("lambdas.lyr", source), options);
        return result.Diagnostics.Diagnostics.Select(d => d.Code).ToArray();
    }

    [Fact]
    public void A_yield_makes_a_lambda_a_generator() =>
        Assert.Empty(Codes("""
            fn main(): int {
                let count = (n: int) => { var i = 0; while (i < n) { yield i; i += 1; } };
                let c = count(3);
                return c.next() ?? -1;
            }
            """));

    [Fact]
    public void The_yields_of_an_inferred_generator_unify() =>
        Assert.Equal(["LYR-SEM0016"], Codes("""
            fn main(): int {
                let mixed = () => { yield 1; yield "two"; };
                let c = mixed();
                return 0;
            }
            """));

    [Fact]
    public void A_coroutine_context_checks_the_yields() =>
        Assert.Equal(["LYR-SEM0001"], Codes("""
            fn main(): int {
                let f: fn() -> Coroutine<int> = () => { yield "one"; };
                let c = f();
                return 0;
            }
            """));

    [Fact]
    public void A_generator_with_a_result_returns_it_on_every_path()
    {
        Assert.Equal(["LYR-SEM0046"], Codes("""
            fn main(): int {
                let g = (b: bool) => { yield 1; if (b) { return "early"; } };
                let c = g(true);
                return 0;
            }
            """));
        Assert.Empty(Codes("""
            fn main(): int {
                let g = (b: bool) => { yield 1; if (b) { return "early"; } return "late"; };
                let c = g(true);
                while (c.next() != null) { }
                return if ((c.result() ?? "") == "early") 0 else 1;
            }
            """));
    }

    [Fact]
    public void Calling_a_throwing_generator_throws_nothing_its_pull_does()
    {
        const string Prelude = """
            enum Bad :: [Error] { It; fn message(): string { return "bad"; } }
            fn risky(fail: bool): Coroutine<int> {
                let make = (f: bool) => { if (f) { throw Bad.It; } yield 1; };
                return make(fail);
            }

            """;
        // 'risky' hands on what the lambda made — and its type says the pulls throw nothing, so
        // the lambda's coroutine, which may throw 'Bad', does not fit it.
        Assert.Contains("LYR-SEM0001", Codes(Prelude + "fn main(): int { return 0; }"));
        Assert.Empty(Codes("""
            enum Bad :: [Error] { It; fn message(): string { return "bad"; } }
            fn main(): int {
                let make = (f: bool) => { if (f) { throw Bad.It; } yield 1; };
                let c = make(true);
                try { return c.next() ?? 0; } catch (_: Bad) { return 1; }
            }
            """));
        Assert.Equal(["LYR-SEM0138"], Codes("""
            enum Bad :: [Error] { It; fn message(): string { return "bad"; } }
            fn main(): int throws Bad {
                let make = (f: bool) => { if (f) { throw Bad.It; } yield 1; };
                let c = make(true);
                return c.next() ?? 0;
            }
            """));
    }

    [Fact]
    public void A_function_that_returns_a_coroutine_without_yielding_is_ordinary() =>
        // 08 D11: a coroutine is 'Coroutine<…>' AND a yield of its own. Without the yield the body
        // returns a coroutine value like any function returns any value.
        Assert.Empty(Codes("""
            fn ones(): Coroutine<int> {
                return sequence { while (true) { yield 1; } };
            }
            fn main(): int { return ones().next() ?? 0; }
            """));

    [Fact]
    public void A_bare_yield_makes_a_generator_of_nothing() =>
        Assert.Empty(Codes("""
            fn main(): int {
                let ticks = (n: int) => { var i = 0; while (i < n) { yield; i += 1; } };
                let t = ticks(2);
                var count = 0;
                while (t.next()) { count += 1; }
                return count;
            }
            """));
}
