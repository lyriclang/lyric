using Lyric.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// A yield outside a coroutine's own body in the front end (design/v5/spec/06 §10a, A5, A8; M6 S2d),
/// against <c>stdlib5</c>: it throws <c>Cancelled</c> at close as a body's yield does, so its
/// function covers it — <c>throws Cancelled</c> or a clause — and a coroutine's body covers it for
/// the helpers it calls, as it does for its own yields.
/// </summary>
public class DynamicYieldTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static string[] Codes(string source)
    {
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer("dynamic.lyr", source), options);
        return result.Diagnostics.Diagnostics.Select(d => d.Code).ToArray();
    }

    private const string Emit = """
        import std.task { Cancelled };
        fn emit(x: int): void throws Cancelled { yield x; }

        """;

    [Fact]
    public void A_yield_outside_a_body_is_covered_like_a_throw()
    {
        Assert.Equal(["LYR-SEM0034"], Codes("fn emit(x: int): void { yield x; }\nfn main(): int { return 0; }"));
        Assert.Empty(Codes(Emit + "fn main(): int { return 0; }"));
        Assert.Empty(Codes("""
            import std.task { Cancelled };
            fn tolerant(): void { try { yield 1; } catch (_: Cancelled) { } }
            fn main(): int { return 0; }
            """));
    }

    [Fact]
    public void A_body_covers_the_cancelled_of_the_helpers_it_calls()
    {
        Assert.Empty(Codes(Emit + "fn gen(): Coroutine<int> { yield 0; try emit(1); }\nfn main(): int { return gen().next() ?? 0; }"));
        // The call is marked like any call of a throwing function.
        Assert.Equal(["LYR-SEM0138"], Codes(Emit + "fn gen(): Coroutine<int> { yield 0; emit(1); }\nfn main(): int { return 0; }"));
        // A generator lambda's body covers it as a function's does.
        Assert.Empty(Codes(Emit + "fn main(): int { let g = () => { yield 0; try emit(1); }; return g().next() ?? 0; }"));
    }

    [Fact]
    public void An_ordinary_function_calling_a_yielding_helper_covers_its_cancelled() =>
        Assert.Equal(["LYR-SEM0034"], Codes(Emit + "fn relay(): void { try emit(1); }\nfn main(): int { return 0; }"));
}
