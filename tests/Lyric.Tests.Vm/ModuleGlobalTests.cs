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
/// Reading and writing a module's globals from outside it (v3.2).
///
/// <para>For a TOOL — a debugger's Globals scope, an editor showing a running game what it holds.
/// A program reaches its own globals with an instruction and needs none of this; what had no way
/// in was the host, which is what a load test found by wanting it.</para>
/// </summary>
public class ModuleGlobalTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LoadedProgram Load(string source)
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

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile: " + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);
        return LoadedProgram.Load(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null));
    }

    private const string TwoGlobals = """
        let start = 7;
        let step = 3;

        pub fn total(): int {
            return start + step;
        }
        """;

    [Fact]
    public void The_globals_can_be_counted_and_read()
    {
        var program = Load(TwoGlobals);

        Assert.Equal(2, program.GlobalCount);

        // Which slot holds which name is the module's business (Module.GlobalNames); what this
        // asks is only that the initializer ran and the values are reachable.
        var values = Enumerable.Range(0, program.GlobalCount)
            .Select(i => program.ReadGlobal(i).AsI64)
            .ToArray();

        Assert.Contains(7L, values);
        Assert.Contains(3L, values);
    }

    [Fact]
    public void A_written_global_is_what_the_program_then_reads()
    {
        var program = Load(TwoGlobals);
        Assert.Equal(10L, program.Invoke(program.IndexOfFunction("main.total")).AsI64);

        var seven = Enumerable.Range(0, program.GlobalCount)
            .First(i => program.ReadGlobal(i).AsI64 == 7);
        program.WriteGlobal(seven, LyrValue.FromI64(100));

        Assert.Equal(103L, program.Invoke(program.IndexOfFunction("main.total")).AsI64);
    }

    [Fact]
    public void An_index_outside_the_slots_is_refused_on_both_sides()
    {
        var program = Load(TwoGlobals);

        Assert.Throws<ArgumentOutOfRangeException>(() => program.ReadGlobal(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.ReadGlobal(program.GlobalCount));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => program.WriteGlobal(program.GlobalCount, LyrValue.FromI64(1)));
    }

    [Fact]
    public void A_module_without_globals_has_none()
    {
        var program = Load("""
            pub fn answer(): int { return 42; }
            """);

        Assert.Equal(0, program.GlobalCount);
    }
}
