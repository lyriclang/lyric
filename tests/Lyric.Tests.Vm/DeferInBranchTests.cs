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
/// A <c>defer</c> inside an <c>if</c> body belongs to that body (§7.5): it runs when the body's
/// scope ends, and only when the body ran. The <c>then</c> block used to be lowered as bare
/// statements rather than as a scope, so its defers were registered on the ENCLOSING scope —
/// measured: inside a loop body with a defer of its own, the branch's defer ran on every
/// iteration, taken or not; inside a scope without defers, it never ran at all. Found by the loop
/// label tests, which break out of exactly such a branch.
/// </summary>
public class DeferInBranchTests
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
    public void A_defer_in_an_untaken_branch_does_not_run() =>
        // Was "o-a\nbody\no-a\nbody\n": the branch's defer ran on the iteration that skipped it.
        Assert.Equal("body\no-a\nbody\n", Out("""
            fn main(): int {
                for (i in 0..3) {
                    defer println("body");
                    if (i == 1) {
                        defer println("o-a");
                        break;
                    }
                }
                return 0;
            }
            """));

    [Fact]
    public void A_defer_in_a_taken_branch_runs_at_the_branch_end_without_a_break() =>
        // Was "": inside a loop body without defers of its own, the branch's defer never ran.
        Assert.Equal("in\nafter-if\nin\nafter-if\n", Out("""
            fn main(): int {
                for (i in 0..2) {
                    if (true) {
                        defer println("in");
                    }
                    println("after-if");
                }
                return 0;
            }
            """));

    [Fact]
    public void A_defer_in_a_then_branch_with_an_else_runs_once() =>
        Assert.Equal("then\nelse\n", Out("""
            fn f(c: bool): void {
                if (c) {
                    defer println("then");
                } else {
                    defer println("else");
                }
            }
            fn main(): int { f(true); f(false); return 0; }
            """));

    [Fact]
    public void Every_scope_left_by_a_labeled_break_runs_its_defers_innermost_first() =>
        Assert.Equal("inner\nouter\n25\n", Out("""
            fn main(): int {
                var log = 0;
                outer: for (i in 0..3) {
                    defer log += 10;
                    for (j in 0..3) {
                        defer log += 1;
                        if (i == 1 && j == 1) {
                            defer println("outer");
                            defer println("inner");
                            break outer;
                        }
                    }
                }
                println(f"{log}");
                return 0;
            }
            """));
}
