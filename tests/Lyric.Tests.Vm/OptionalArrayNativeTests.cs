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
/// A native returning <c>?T[]</c> (2.14), the shape a read has when an empty result and a failure
/// are different answers.
///
/// <para>Nothing about the VALUE needed building: an optional over a reference IS the reference,
/// and "no value" is an empty one. What needed building is the TYPE check at binding, and it
/// needs three levels — the tag says optional, its inner says array, and only the element below
/// that separates <c>?string[]</c> from <c>?uint8[]</c>. The case that would go wrong without it
/// is a host handing back bytes where the module expects lines, which would be found much later
/// and somewhere else.</para>
/// </summary>
public class OptionalArrayNativeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static BytecodeModule Compile(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("probe.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
            // The probe module declares its own natives, so it stands in for an SDK.
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule(), "probe", isNative: true);

        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile: " + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true, libraryRoots: true);
        var after = new StringWriter();
        de.RenderText(after);
        Assert.True(ir is not null, "lowering produced nothing: " + after);
        return BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!));
    }

    private const string Source = """
        module probe;

        pub fn readLines(path: string): ?string[];

        pub fn count(path: string): int {
            let ls = readLines(path);
            if (ls == null) { return 0 - 1; }

            // Narrowed by the check above, so no '??' here -- and '??' would in fact be an
            // error on a value the analysis has already proven present.
            return ls.length;
        }
        """;

    private static NativeRegistry Registry(TypeTag element, Func<LyrValue[], LyrValue> answer)
    {
        var registry = NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null);
        registry.RegisterOptionalArrayReturning("probe.readLines",
            [TypeTag.String], element, answer);
        return registry;
    }

    private static LyrValue Strings(params string[] values)
    {
        var array = new LyrValue[values.Length];
        for (var i = 0; i < values.Length; i++) array[i] = LyrValue.FromString(values[i]);
        return LyrValue.FromObject(array);
    }

    [Fact]
    public void An_array_crosses_and_an_empty_one_is_not_nothing()
    {
        var program = LoadedProgram.Load(Compile(Source),
            Registry(TypeTag.String, args => args[0].AsString.Length == 0
                ? LyrValue.None
                : Strings("a", "b")));

        var index = program.IndexOfFunction("probe.count");
        Assert.Equal(2, program.Invoke(index, LyrValue.FromString("has-content")).AsI64);
    }

    [Fact]
    public void Nothing_arrives_as_nothing()
    {
        var program = LoadedProgram.Load(Compile(Source),
            Registry(TypeTag.String, _ => LyrValue.None));

        var index = program.IndexOfFunction("probe.count");
        Assert.Equal(-1, program.Invoke(index, LyrValue.FromString("x")).AsI64);
    }

    [Fact]
    public void An_empty_array_is_distinguishable_from_nothing()
    {
        // The whole reason the shape exists: both of these are "no elements", and only one of
        // them is "no answer".
        var program = LoadedProgram.Load(Compile(Source),
            Registry(TypeTag.String, _ => LyrValue.FromObject(Array.Empty<LyrValue>())));

        var index = program.IndexOfFunction("probe.count");
        Assert.Equal(0, program.Invoke(index, LyrValue.FromString("x")).AsI64);
    }

    [Fact]
    public void An_element_type_the_module_does_not_expect_is_refused_at_load()
    {
        // Two levels of type agree here — optional, and array — and the third does not. Before
        // the check, this bound and the module read bytes as strings.
        var thrown = Assert.Throws<LyricRuntimeException>(() =>
            LoadedProgram.Load(Compile(Source),
                Registry(TypeTag.U8, _ => LyrValue.FromObject(Array.Empty<LyrValue>()))));

        Assert.Equal(VmDiagnostics.ImportsNotBound, thrown.Code);
        Assert.Contains("element type", thrown.Message, StringComparison.Ordinal);
    }
}
