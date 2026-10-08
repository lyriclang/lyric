using Lyric.Compiler;
using Lyric5.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The subset gate (M2 S1): what of the 4.x front end's IR the Lyric 5 compiler takes today, and
/// what it refuses with the milestone that brings it. Programs compile against <c>stdlib5/</c>,
/// the seed of the Lyric 5 standard library, never against the 4.x <c>stdlib/</c> — the copy
/// beside the tests, which carries the test-only <c>std.notyet</c>: since M6 S2d the gate takes
/// every instruction, and a native without an intrinsic is the refusal left to show — one no
/// milestone lifts, since a program reaches C through <c>extern "C"</c>.
/// </summary>
public class SubsetGateTests
{
    private static CompileResult Lower(string source)
    {
        return TestCompiler.Lower("gate.lyr", source, stdlib: Path.Combine(AppContext.BaseDirectory, "stdlib5"));
    }

    private static string[] Refusals(string source)
    {
        var result = Lower(source);
        Assert.True(result.Ok, "the front end refused what the gate was to see:\n" + Render(result));
        Assert.NotNull(result.Ir);
        SubsetGate.Check(result.Ir!, result.Diagnostics);
        return result.Diagnostics.Diagnostics.Where(d => d.Code == SubsetGate.NotYet).Select(d => d.Message).ToArray();
    }

    private static string Render(CompileResult result)
    {
        var writer = new StringWriter();
        result.Diagnostics.RenderText(writer);
        return writer.ToString();
    }

    [Fact]
    public void The_core_passes()
    {
        var refusals = Refusals("""
            import std.io { println };

            struct Point { x: int, y: int }

            fn add(a: int, b: int): int { return a + b; }

            fn main(): int {
                var total = 0;
                var i = 0;
                while (i < 10) {
                    if (i % 2 == 0) { total = add(total, i); } else { total = total - 1; }
                    i = i + 1;
                }
                let p = Point { x = total, y = 2 };
                println(f"total {p.x} and {p.y} {true}");
                return p.x;
            }
            """);
        Assert.Empty(refusals);
    }

    // The '[x] * n'-over-objects rows retired with their rule (M4 S9): the sema refuses the
    // element without 'Clone' (LYR-SEM0136) and desugars the rest, so no such instruction
    // reaches the gate — OptionalEqualityAndRepeatTests pins it.
    [Theory]
    [InlineData("import std.notyet { tick };\nfn main(): int { return tick(); }", "the native function 'std.notyet.tick'", "no milestone: a function without a body is the standard library's — a program reaches C through 'extern \"C\"'")]
    public void A_construct_outside_the_core_names_its_milestone(string source, string what, string milestone)
    {
        var refusals = Refusals(source);
        Assert.NotEmpty(refusals);
        Assert.Contains(refusals, r => r.Contains(what) && r.EndsWith($"({milestone})"));
    }

    [Fact]
    public void A_generator_passes()
    {
        var refusals = Refusals("""
            fn count(n: int): Coroutine<int, string> throws Error {
                var i = 0;
                while (i < n) { yield i; i = i + 1; }
                return "done";
            }

            fn main(): int {
                let c = count(3);
                try {
                    let first = c.next() ?? -1;
                    let ended = c.result() ?? "running";
                    return if (c.isDone() && ended == "done") 0 else first;
                } catch (_: Error) {
                    return 1;
                }
            }
            """);
        Assert.Empty(refusals);
    }

    [Fact]
    public void A_native_function_outside_the_intrinsic_table_is_refused()
    {
        Assert.Contains("std.io.consolePut", SubsetGate.Intrinsics);
        Assert.DoesNotContain("std.io.console.println", SubsetGate.Intrinsics);
    }
}
