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
/// Value blocks end to end: the tail of a block arm is the arm's value, the tail of a block
/// lambda its return, a tail is taken BEFORE the block's defers run (§7.5), and a throwing tail
/// unwinds like a throwing arm.
/// </summary>
public class ValueBlockTests
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
    public void A_state_machine_step_reads_as_one_match() =>
        Assert.Equal("tick 1\ntick 2\nbusy(2)\nstopping\nclosed\n", Out("""
            enum State { Idle, Busy(int), Closed }
            enum Event { Start, Tick, Stop }
            fn step(s: State, e: Event): State {
                return match (e) {
                    Event.Start => State.Busy(0),
                    Event.Tick => {
                        let n = match (s) { State.Busy(k) => k + 1, _ => 0 };
                        println(f"tick {n}");
                        State.Busy(n)
                    },
                    Event.Stop => { println("stopping"); State.Closed },
                };
            }
            fn describe(s: State): string {
                return match (s) {
                    State.Idle => "idle",
                    State.Busy(n) => { let base = "busy"; f"{base}({n})" },
                    State.Closed => { return "closed"; },
                };
            }
            fn main(): int {
                var s = State.Idle;
                s = step(s, Event.Start);
                s = step(s, Event.Tick);
                s = step(s, Event.Tick);
                println(describe(s));
                s = step(s, Event.Stop);
                println(describe(s));
                return 0;
            }
            """));

    [Fact]
    public void A_block_lambda_returns_its_tail() =>
        Assert.Equal("41\n0\n", Out("""
            fn main(): int {
                let f = (x: int) => { let y = x * 2; y + 1 };
                let g = (x: int) => { if (x < 0) { return 0; } x * 2 };
                println(f"{f(20)}");
                println(f"{g(-1)}");
                return 0;
            }
            """));

    [Fact]
    public void The_tail_is_taken_before_the_blocks_defers_run() =>
        Assert.Equal("cleanup\n7\n", Out("""
            fn main(): int {
                var n = 7;
                let v = match (true) {
                    true => { defer n = 100; defer println("cleanup"); n },
                    false => 0,
                };
                println(f"{v}");
                return 0;
            }
            """));

    [Fact]
    public void A_throwing_tail_unwinds() =>
        Assert.Equal("1\ncaught\n", Out("""
            class E :: [Throwable] { fn message(): string { return "e"; } }
            fn f(b: bool): int throws E {
                return match (b) { true => { 1 }, false => { let why = "no"; throw E { } } };
            }
            fn main(): int {
                try { println(f"{f(true)}"); println(f"{f(false)}"); }
                catch (e: E) { println("caught"); }
                return 0;
            }
            """));

    [Fact]
    public void A_call_tail_in_a_match_statement_runs() =>
        Assert.Equal("t\n", Out("""
            fn main(): int {
                match (true) { true => { println("t") }, false => { println("f") } }
                return 0;
            }
            """));
}
