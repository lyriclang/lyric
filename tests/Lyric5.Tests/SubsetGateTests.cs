using Lyric.Compiler;
using Lyric5.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The subset gate (M2 S1): what of the 4.x front end's IR the Lyric 5 compiler takes today, and
/// what it refuses with the milestone that brings it. Programs compile against <c>stdlib5/</c>,
/// the seed of the Lyric 5 standard library, never against the 4.x <c>stdlib/</c>.
/// </summary>
public class SubsetGateTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static CompileResult Lower(string source)
    {
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        return SourceCompiler.Lower(ScriptSource.FromBuffer("gate.lyr", source), options);
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

    [Theory]
    [InlineData("fn main(): int { let x = 1.5; return 0; }", "floating-point numbers", "M3")]
    [InlineData("fn main(): int { let c = 'a'; return 0; }", "'char'", "M3")]
    [InlineData("class Box { v: int }\nfn main(): int { let b = Box { v = 1 }; return b.v; }", "classes", "M3")]
    [InlineData("fn main(): int { let xs = [1, 2, 3]; return xs[0]; }", "arrays", "M3")]
    [InlineData("fn main(): int { let o: ?int = null; return 0; }", "optionals", "M3")]
    [InlineData("enum E { A, B }\nfn main(): int { let e = E.A; return 0; }", "enums", "M3")]
    [InlineData("fn main(): int { let f = (x: int): int => x + 1; return f(1); }", "closures", "M3")]
    [InlineData("let limit = 3;\nfn main(): int { return limit; }", "module-level 'let'", "M3")]
    [InlineData("fn gen(): Coroutine<int> { yield 1; }\nfn main(): int { let g = gen(); return 0; }", "coroutines", "M6")]
    public void A_construct_outside_the_core_names_its_milestone(string source, string what, string milestone)
    {
        var refusals = Refusals(source);
        Assert.NotEmpty(refusals);
        Assert.Contains(refusals, r => r.Contains(what) && r.EndsWith($"({milestone})"));
    }

    [Fact]
    public void A_native_function_outside_the_intrinsic_table_is_refused()
    {
        Assert.Contains("std.io.println", SubsetGate.Intrinsics);
        Assert.DoesNotContain("std.io.console.println", SubsetGate.Intrinsics);
    }
}
