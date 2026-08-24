using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Bytecode;

/// <summary>
/// What the loader may conclude about a <c>callvirt</c>, and where it has to stop.
///
/// <para>The argument count of an interface slot is not written anywhere: the Types section names
/// the slots and nothing more, and the loader reads the count off an Impls row instead — every
/// implementation of a slot shares its signature, so any row will do. A module that implements
/// the interface nowhere has no row, and then there is nothing to read.</para>
///
/// <para>That is not a corner: a module which declares an interface, calls through it, and leaves
/// the implementing to whoever imports it is an ordinary library, and compiling every file of a
/// project on its own is an ordinary way to build one. Such a module was rejected by its own
/// loader — the missing row was answered with "no arguments, no result", so a two-argument call
/// looked as though it left its arguments behind, and the block was reported as ending two values
/// deep. The code was correct; the reading of it was not.</para>
/// </summary>
public class InterfaceCallShapeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IrModule Lower(string source)
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
        Assert.False(de.HasErrors, "source did not compile:\n" + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);
        return ir!;
    }

    private static BytecodeModule Compile(string source) =>
        BytecodeReader.ReadOrThrow(BytecodeWriter.Write(Lower(source)));

    /// <summary>Two slots of DIFFERENT arity, because one arity would have hidden the fault
    /// behind whichever number happened to be assumed.</summary>
    private const string Library = """
        pub interface Ground {
            fn plantable(column: int, row: int): bool;
            fn changed(column: int, row: int, standing: bool): void;
        }

        pub class Field {
            ground: Ground,
            standing: bool[],
            width: int,

            pub fn put(column: int, row: int): bool {
                if (!this.ground.plantable(column, row)) { return false; }
                this.standing[row * this.width + column] = true;
                this.ground.changed(column, row, true);
                return true;
            }
        }
        """;

    /// <summary>The Impls rows for ONE interface, by name — the standard library brings rows of
    /// its own, and this test is about the absence of a particular one.</summary>
    private static IEnumerable<BytecodeImpl> RowsFor(BytecodeModule module, string iface) =>
        module.Impls.Where(i =>
            module.Types[i.Interface].Name.EndsWith(iface, StringComparison.Ordinal));

    private static BytecodeFunction FunctionEnding(BytecodeModule module, string suffix) =>
        module.Functions.Single(f => f.Name.EndsWith(suffix, StringComparison.Ordinal));

    [Fact]
    public void A_library_that_only_calls_through_an_interface_loads()
    {
        var module = Compile(Library);

        // Nothing here implements 'Ground'. That absence IS the case: without a row the loader
        // has no signature to read, and it used to fill the gap with a guess.
        Assert.Empty(RowsFor(module, "Ground"));
        Assert.NotNull(FunctionEnding(module, "Field.put"));
    }

    /// <summary>
    /// The code was right the whole time, which is why the loader had no business rejecting it:
    /// the deepest moment in 'put' is the three-argument call with its receiver beneath it, and
    /// four is the depth its own emitter recorded.
    /// </summary>
    [Fact]
    public void The_declared_maximum_matches_what_the_calls_actually_need()
    {
        Assert.Equal(4, FunctionEnding(Compile(Library), "Field.put").MaxStack);
    }

    /// <summary>
    /// The same source with an implementation beside it. The row is there, the arity is derivable,
    /// and the walk runs to the end of every block — this is the half that must not be lost when
    /// the other half stops early.
    /// </summary>
    [Fact]
    public void The_same_program_with_an_implementation_still_validates()
    {
        var module = Compile(Library + """

            pub class Valley :: [Ground] {
                pub fn plantable(column: int, row: int): bool { return column + row > 0; }
                pub fn changed(column: int, row: int, standing: bool): void { }
            }

            pub fn main(): int {
                let f = Field { ground = Valley { }, standing = [false] * 16, width = 4 };
                if (f.put(1, 1)) { return 0; }
                return 1;
            }
            """);

        var row = Assert.Single(RowsFor(module, "Ground"));
        Assert.Equal(2, row.Methods.Count);
        Assert.Contains(module.Functions,
            f => f.Name.EndsWith("Valley.changed", StringComparison.Ordinal));
    }
}
