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
/// A generic METHOD is monomorphized with its receiver.
///
/// <para>It was not. The call site pushed a receiver and the instance was built without a
/// <c>this</c> slot, so the module declared one parameter fewer than the call supplied — and the
/// value stayed on the stack. The front end was content: <c>lyrc check</c> passed, the IR dump
/// looked right, and the module the writer produced was then refused by its own READER with
/// "block at 0 ends with 1 value(s) on the stack, expected 0".</para>
///
/// <para>THE READ-BACK IS THE TEST, which is why these live here and not in the sema suite:
/// <c>check</c> never emits, so nothing in that half of the pipeline could see it. What found it
/// was <c>LYR-CLI0020</c> — until it existed, the same failure was an unhandled stack trace with
/// no code and no position, and it had been sitting in the tree.</para>
/// </summary>
public class GenericMethodReceiverTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>Compiles, writes the bytes, READS THEM BACK, and runs.</summary>
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

    [Fact]
    public void A_generic_method_on_a_class_is_called_with_its_receiver()
        => Assert.Equal(1, Run("""
            class C { fn id<T>(v: T): T { return v; } }
            fn main(): int { let c = C { }; return c.id<int>(1); }
            """));

    /// <summary>
    /// The receiver is not only counted, it is USABLE — the body reads a field through it.
    ///
    /// <para>A slot allocated but never filled would pass the test above: the arity would match
    /// and the value would be ignored.</para>
    /// </summary>
    [Fact]
    public void The_receiver_is_the_object_the_call_named()
        => Assert.Equal(5, Run("""
            class C { n: int, fn take<T>(v: T): int { return this.n; } }
            fn main(): int { let c = C { n = 5 }; return c.take<int>(1); }
            """));

    [Fact]
    public void A_generic_method_on_a_struct_works_the_same_way()
        => Assert.Equal(2, Run("""
            struct S { n: int, fn id<T>(v: T): T { return v; } }
            fn main(): int { let s = S { n = 1 }; return s.id<int>(2); }
            """));

    /// <summary>A STATIC one has no receiver, and must not be given one.</summary>
    [Fact]
    public void A_static_generic_method_takes_no_receiver()
        => Assert.Equal(3, Run("""
            class C { static fn make<T>(v: T): T { return v; } }
            fn main(): int { return C.make<int>(3); }
            """));

    /// <summary>The control: a free generic function never had the problem.</summary>
    [Fact]
    public void A_free_generic_function_is_unchanged()
        => Assert.Equal(4, Run("""
            fn id<T>(v: T): T { return v; }
            fn main(): int { return id<int>(4); }
            """));

    /// <summary>
    /// Two instances of one method, so the receiver is not accidentally right for one shape only.
    /// </summary>
    [Fact]
    public void Two_instances_of_one_method_each_keep_their_receiver()
        // 2*2 + (4*2)*2 = 20. Written out because the first version said 12 and the run said
        // otherwise -- an expectation put down before the run is what makes that a typo and not a
        // finding.
        => Assert.Equal(20, Run("""
            class C { n: int, fn scale<T>(v: T): int { return this.n * 2; } }
            fn main(): int {
                let a = C { n = 2 };
                let b = C { n = 4 };
                return a.scale<int>(0) + b.scale<bool>(true) * 2;
            }
            """));
}
