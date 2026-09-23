using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>&amp;&amp;=</c>, <c>||=</c> and <c>??=</c> — the three operators §2 lists and the lowering
/// could not carry.
///
/// <para>They are in the grammar (<c>AssignOp</c>), so they are part of the language; only one of
/// the nine combinations of operator and target worked. <c>??=</c> lowered on a local and was an
/// internal error on a field and on an element; the short-circuit pair was an internal error
/// everywhere. Three copies of the same four-block diamond is how they came to disagree, so there
/// is one now.</para>
///
/// <para>SHORT-CIRCUITING IS THE POINT, and it is what the second half of this file measures. If
/// these were <c>x = x op e</c> the right side would always run, and an assignment that calls a
/// function it was not supposed to call is a different program — the value alone cannot tell the
/// two apart, which is why the counter is here.</para>
/// </summary>
public class ShortCircuitAssignTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static long Run(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    // ------------------------------------------------------------------ the value

    [Theory]
    [InlineData("var b = true; b &&= false;", 0)]
    [InlineData("var b = true; b &&= true;", 1)]
    [InlineData("var b = false; b &&= true;", 0)]
    [InlineData("var b = false; b ||= true;", 1)]
    [InlineData("var b = false; b ||= false;", 0)]
    [InlineData("var b = true; b ||= false;", 1)]
    public void On_a_local_the_value_is_what_the_operator_says(string body, long expected)
        => Assert.Equal(expected, Run($"fn main(): int {{ {body} if (b) {{ return 1; }} return 0; }}"));

    [Theory]
    [InlineData("var o: ?int = null; o ??= 5;", 5)]
    [InlineData("var o: ?int = 3; o ??= 5;", 3)]
    public void Coalescing_assignment_on_a_local_fills_only_an_empty_one(string body, long expected)
        => Assert.Equal(expected, Run($"fn main(): int {{ {body} return o ?? 0; }}"));

    /// <summary>A FIELD: an internal error before, for all three operators.</summary>
    [Theory]
    [InlineData("class C { v: ?int, }", "let c = C { v = null }; c.v ??= 5; return c.v ?? 0;", 5)]
    [InlineData("class C { v: ?int, }", "let c = C { v = 3 }; c.v ??= 5; return c.v ?? 0;", 3)]
    [InlineData("class C { b: bool, }", "let c = C { b = true }; c.b &&= false; if (c.b) { return 1; } return 0;", 0)]
    [InlineData("class C { b: bool, }", "let c = C { b = false }; c.b ||= true; if (c.b) { return 1; } return 0;", 1)]
    public void On_a_field_it_works_too(string decl, string body, long expected)
        => Assert.Equal(expected, Run($"{decl}\nfn main(): int {{ {body} }}"));

    /// <summary>An ELEMENT: likewise.</summary>
    [Theory]
    [InlineData("var xs: (?int)[] = [null]; xs[0] ??= 7; return xs[0] ?? 0;", 7)]
    [InlineData("var xs: (?int)[] = [2]; xs[0] ??= 7; return xs[0] ?? 0;", 2)]
    [InlineData("var bs = [true]; bs[0] &&= false; if (bs[0]) { return 1; } return 0;", 0)]
    public void On_an_element_it_works_too(string body, long expected)
        => Assert.Equal(expected, Run($"fn main(): int {{ {body} }}"));

    // ------------------------------------------------------------------ the short circuit

    private const string Counter =
        """
        class Counter {
            n: int,
            mut fn hit(): bool { this.n = this.n + 1; return true; }
            mut fn hitInt(): int { this.n = this.n + 1; return 9; }
        }
        """;

    /// <summary>
    /// How often the right side ran. Each case is paired with the one that must run it, because a
    /// lowering that NEVER evaluates the right side would satisfy the skipping half alone.
    /// </summary>
    [Theory]
    [InlineData("var b = false; b &&= c.hit();", 0)]   // false && _ is false: no call
    [InlineData("var b = true; b &&= c.hit();", 1)]
    [InlineData("var b = true; b ||= c.hit();", 0)]    // true || _ is true: no call
    [InlineData("var b = false; b ||= c.hit();", 1)]
    [InlineData("var o: ?int = 1; o ??= c.hitInt();", 0)]
    [InlineData("var o: ?int = null; o ??= c.hitInt();", 1)]
    public void The_right_side_runs_exactly_when_it_must(string body, long calls)
        => Assert.Equal(calls,
            Run($"{Counter}\nfn main(): int {{ let c = Counter {{ n = 0 }}; {body} return c.n; }}"));

    /// <summary>
    /// The receiver and the index are evaluated ONCE, however often the target is read.
    ///
    /// <para>The helper reads the target twice — before the test and after the merge — so a
    /// lowering that re-evaluated the path would call <c>at</c> three times for one statement.</para>
    /// </summary>
    [Fact]
    public void The_target_path_is_evaluated_once()
        => Assert.Equal(1, Run($$"""
            {{Counter}}
            fn main(): int {
                let c = Counter { n = 0 };
                var xs: (?int)[] = [null, null];
                xs[at(c)] ??= 7;
                return c.n;
            }

            fn at(c: Counter): int { c.hit(); return 0; }
            """));
}
