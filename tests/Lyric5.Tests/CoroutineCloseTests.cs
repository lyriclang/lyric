using Lyric.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// <c>close()</c> in the front end (design/v5/spec/06 A5, M6 S2b), against <c>stdlib5</c>, where
/// <c>std.task</c> declares <c>Cancelled</c>: a coroutine's <c>close()</c> is marked and covered
/// like its pulls, <c>using</c> closes a coroutine, a yield of a body throws <c>Cancelled</c> to the
/// trys around it — and the <c>try</c> a <c>using</c> writes around its own close never warns.
/// </summary>
public class CoroutineCloseTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static string[] Codes(string source)
    {
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer("close.lyr", source), options);
        return result.Diagnostics.Diagnostics.Select(d => d.Code).ToArray();
    }

    private const string Gen = """
        enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }
        fn quiet(): Coroutine<int> { yield 1; }
        fn loud(): Coroutine<int> throws Oops { yield 1; }

        """;

    [Fact]
    public void Closing_a_coroutine_that_cannot_throw_needs_no_mark() =>
        Assert.Empty(Codes(Gen + "fn main(): int { let c = quiet(); c.close(); return 0; }"));

    [Fact]
    public void Closing_a_throwing_coroutine_is_marked_like_its_pull()
    {
        Assert.Equal(["LYR-SEM0138"], Codes(Gen + "fn main(): int throws Oops { let c = loud(); c.close(); return 0; }"));
        Assert.Empty(Codes(Gen + "fn main(): int throws Oops { let c = loud(); try c.close(); return 0; }"));
    }

    [Fact]
    public void Using_closes_a_coroutine_in_silence() =>
        Assert.Empty(Codes(Gen + "fn main(): int { using let c = quiet(); return c.next() ?? 0; }"));

    [Fact]
    public void Using_a_closeable_whose_close_cannot_throw_warns_nothing() =>
        // The parser writes 'defer try c.close();' — a mark no one can drop (M5 warned LYR-SEM0139).
        Assert.Empty(Codes("""
            class Conn :: [Closeable] { fn close(): void { } }
            fn main(): int { using let c = Conn {}; return 0; }
            """));

    [Fact]
    public void A_clause_around_a_yield_of_the_body_catches_cancelled() =>
        Assert.Empty(Codes("""
            import std.task { Cancelled };
            fn gen(): Coroutine<int, string> {
                try { yield 1; } catch (_: Cancelled) { return "closed"; }
                return "ran out";
            }
            fn main(): int { let c = gen(); c.close(); return 0; }
            """));

    [Fact]
    public void A_coroutine_without_std_task_has_no_close()
    {
        // Without a standard library there is no 'Cancelled', so nothing could unwind the body.
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer("close.lyr",
            "fn gen(): Coroutine<int> { yield 1; }\nfn main(): int { let c = gen(); c.close(); return 0; }"),
            new CompilerOptions());
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Code == "LYR-SEM0012");
    }
}
