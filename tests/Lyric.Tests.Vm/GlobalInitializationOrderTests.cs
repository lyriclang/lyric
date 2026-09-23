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
/// A global read before its initializer ran is <c>LYR-VM0017</c> (§4.3).
///
/// <para><c>LYR-SEM0057</c> catches the read an initializer NAMES, so <c>let a = b;</c> above
/// <c>let b = 1;</c> never compiles. A read that travels through a CALL is not named there, and no
/// order of declarations makes it legal — <c>readB</c> needs <c>b</c>, and <c>b</c> comes after
/// <c>a</c>. It used to answer the slot's unwritten contents: a zero of the right type, with
/// nothing said. Measured before the fix: the program returned 0 where the same program with the
/// declarations swapped returned 7.</para>
///
/// <para>Following calls statically is deliberately not required by §4.3 — it would have to be
/// conservative about an indirect call, a lambda and an interface dispatch, and would then refuse
/// programs that are fine.</para>
/// </summary>
public class GlobalInitializationOrderTests
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

    private const string ThroughACall =
        """
        let a = readB();
        let b = 7;

        fn readB(): int { return b; }
        fn main(): int { return a; }
        """;

    private const string InOrder =
        """
        let b = 7;
        let a = readB();

        fn readB(): int { return b; }
        fn main(): int { return a; }
        """;

    [Fact]
    public void A_read_that_reaches_a_later_global_through_a_call_panics()
    {
        var panic = Assert.Throws<LyricPanic>(() => Run(ThroughACall));
        Assert.Equal("LYR-VM0017", panic.Code);
    }

    /// <summary>
    /// The control, and the reason the panic matters: the SAME program in the right order works.
    ///
    /// <para>Without this the guard could refuse every global read and the test above would still
    /// pass — and a guard that refuses everything is how a startup check gets reverted.</para>
    /// </summary>
    [Fact]
    public void The_same_program_in_declaration_order_answers()
        => Assert.Equal(7, Run(InOrder));

    /// <summary>
    /// The map is consulted only WHILE the initializer runs. Afterwards every slot holds a value,
    /// so an ordinary read must not be able to trip it — including a read from a function the
    /// initializer also called.
    /// </summary>
    [Fact]
    public void Ordinary_global_reads_after_startup_are_untouched()
        => Assert.Equal(12, Run("""
            let b = 5;
            let a = readB();

            fn readB(): int { return b; }
            fn main(): int { return a + readB() + 2; }
            """));

    /// <summary>A global reading one declared ABOVE it is the rule working, not the guard.</summary>
    [Fact]
    public void A_global_may_read_one_declared_above_it()
        => Assert.Equal(9, Run("""
            let first = 4;
            let second = first + 5;

            fn main(): int { return second; }
            """));
}
