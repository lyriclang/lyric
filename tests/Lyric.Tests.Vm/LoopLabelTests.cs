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
/// Loop labels at run time: <c>break outer</c> leaves the loop it names and <c>continue outer</c>
/// starts that loop's next iteration — from any depth, over every loop form, and running the
/// defers of every scope left on the way, innermost first (§7.5).
/// </summary>
public class LoopLabelTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Out(string body)
    {
        var source = "import std.io.console { println };\n" + body;
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

        var output = new StringWriter();
        Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(output, TextWriter.Null));
        return output.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Break_outer_leaves_the_named_loop_with_the_inner_state_intact() =>
        Assert.Equal("1,2\n", Out("""
            fn main(): int {
                let g = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];
                var found = (-1, -1);
                outer: for (i in 0..g.length) {
                    for (j in 0..g[i].length) {
                        if (g[i][j] == 6) { found = (i, j); break outer; }
                    }
                }
                let (r, c) = found;
                println(f"{r},{c}");
                return 0;
            }
            """));

    /// <summary>The partial sum of the abandoned row must NOT land in the total: with a flag in
    /// place of the label it did, and that difference is the bug labels exist to prevent.</summary>
    [Fact]
    public void Continue_outer_abandons_the_rest_of_the_inner_loop() =>
        Assert.Equal("8\n", Out("""
            fn main(): int {
                var sum = 0;
                rows: for (row in [[1, 2], [3, -1, 4], [5]]) {
                    var partial = 0;
                    for (x in row) {
                        if (x < 0) { continue rows; }
                        partial += x;
                    }
                    sum += partial;
                }
                println(f"{sum}");
                return 0;
            }
            """));

    [Fact]
    public void Labels_work_over_while_and_do_while() =>
        Assert.Equal("103\n6\n", Out("""
            fn spin(): int {
                var n = 0;
                outer: while (true) {
                    inner: while (true) {
                        n += 1;
                        if (n % 3 == 0) { continue outer; }
                        if (n > 10) { break outer; }
                        if (n % 2 == 0) { break inner; }
                    }
                    n += 100;
                }
                return n;
            }
            fn count(): int {
                var n = 0;
                top: do {
                    n += 1;
                    do {
                        n += 1;
                        if (n > 5) { break top; }
                    } while (true);
                } while (true);
                return n;
            }
            fn main(): int { println(f"{spin()}"); println(f"{count()}"); return 0; }
            """));
}
