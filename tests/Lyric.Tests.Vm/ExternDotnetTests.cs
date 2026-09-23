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
/// <c>extern "dotnet"</c> — stage 1 of the ABI: a program declares a public static .NET method
/// by symbol, the compiler writes an import row named <c>dotnet:Type::Method</c> and the
/// <c>hostAccess</c> bit, and the runtime binds the row by reflection at load time. No new
/// opcode, no format change: the import table was always symbolic.
/// </summary>
public class ExternDotnetTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static (BytecodeModule? Module, DiagnosticEngine Diagnostics) Compile(string source)
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
        if (de.HasErrors) return (null, de);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        return (ir is null ? null : BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir)), de);
    }

    private static BytecodeModule Ok(string source)
    {
        var (module, de) = Compile(source);
        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.True(module is not null, "source did not compile: " + writer);
        return module!;
    }

    private static long Run(BytecodeModule module, Capability granted = Capability.All) =>
        Interpreter.Run(module, [], NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            granted).AsI64;

    private const string Clamp = """
        extern "dotnet" fn clamp(value: int, lo: int, hi: int): int = "System.Math::Clamp";
        fn main(): int { return clamp(99, 0, 10); }
        """;

    // ------------------------------------------------------------------ the compiler's half

    [Fact]
    public void The_import_row_is_named_after_the_abi_and_the_symbol()
    {
        var import = Assert.Single(Ok(Clamp).Imports);
        Assert.Equal("dotnet:System.Math::Clamp", import.Name);
        Assert.Equal([TypeTag.I64, TypeTag.I64, TypeTag.I64], import.ParamTypes.Select(p => p.Tag));
        Assert.Equal(TypeTag.I64, import.ReturnType.Tag);
    }

    [Fact]
    public void The_dotnet_abi_needs_a_symbol_naming_type_and_method()
    {
        // A bare name cannot say which type holds the method, so "dotnet" has no default symbol.
        var (_, de) = Compile("""
            extern "dotnet" fn Cbrt(x: float): float;
            fn main(): int { return Cbrt(8.0) as int; }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0099" && d.Message.Contains("Type::Method"));
    }

    [Fact]
    public void An_extern_declaration_records_hostAccess() =>
        Assert.Equal((ulong)Capability.HostAccess, Ok(Clamp).Capabilities);

    [Fact]
    public void An_unknown_abi_is_refused_at_the_declaration()
    {
        var (_, de) = Compile("""
            extern "c" fn strlen(s: string): int;
            fn main(): int { return 0; }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0099");
    }

    [Fact]
    public void A_type_that_does_not_cross_is_refused_at_the_declaration()
    {
        var (_, de) = Compile("""
            extern "dotnet" fn f(xs: int[]): ?int = "System.Math::Abs";
            fn main(): int { return 0; }
            """);
        Assert.Equal(2, de.Diagnostics.Count(d => d.Code == "LYR-SEM0099"));
    }

    // ------------------------------------------------------------------ the runtime's half

    [Fact]
    public void A_bound_method_runs_with_marshalled_scalars() =>
        Assert.Equal(10, Run(Ok(Clamp)));

    [Fact]
    public void Strings_bools_and_chars_cross_both_ways() =>
        Assert.Equal(1, Run(Ok("""
            extern "dotnet" fn isWhite(c: char): bool = "System.Char::IsWhiteSpace";
            extern "dotnet" fn upper(c: char): char = "System.Char::ToUpperInvariant";
            extern "dotnet" fn empty(s: string): bool = "System.String::IsNullOrEmpty";
            fn main(): int {
                return if (isWhite(' ') && !isWhite('x') && upper('a') == 'A' && empty("") && !empty("x")) 1 else 0;
            }
            """)));

    [Fact]
    public void An_assembly_qualified_type_loads_its_assembly() =>
        Assert.Equal(1, Run(Ok("""
            extern "dotnet" fn host(): string = "System.Net.Dns, System.Net.NameResolution::GetHostName";
            fn main(): int { return if (host() != "") 1 else 0; }
            """)));

    [Fact]
    public void A_runtime_that_does_not_grant_hostAccess_refuses_at_load()
    {
        var refused = Assert.Throws<LyricRuntimeException>(() => Run(Ok(Clamp), Capability.None));
        Assert.Equal("LYR-CAP0001", refused.Code);
        Assert.Contains("hostAccess", refused.Message);
    }

    [Fact]
    public void A_symbol_nobody_can_bind_is_refused_at_load_with_its_name()
    {
        var refused = Assert.Throws<LyricRuntimeException>(() => Run(Ok("""
            extern "dotnet" fn nope(x: float): float = "System.Math::NoSuch";
            fn main(): int { nope(1.0); return 0; }
            """)));
        Assert.Equal("LYR-VM0005", refused.Code);
        Assert.Contains("NoSuch", refused.Message);
    }

    [Fact]
    public void The_lyric_signature_picks_the_overload_and_a_missing_one_is_named()
    {
        var refused = Assert.Throws<LyricRuntimeException>(() => Run(Ok("""
            extern "dotnet" fn sqrt(x: string): float = "System.Math::Sqrt";
            fn main(): int { sqrt("x"); return 0; }
            """)));
        Assert.Contains("no overload", refused.Message);
    }

    [Fact]
    public void A_host_exception_is_a_panic_in_stage_one()
    {
        var panic = Assert.Throws<LyricPanic>(() => Run(Ok("""
            extern "dotnet" fn parse(s: string): int32 = "System.Int32::Parse";
            fn main(): int { return parse("zz") as int; }
            """)));
        Assert.Equal("LYR-VM0016", panic.Code);
        Assert.Contains("FormatException", panic.Message);
    }

    [Fact]
    public void A_host_registration_wins_over_reflection()
    {
        // An explicit registration is a decision; reflection is the default. A host that wants
        // 'System.Math::Clamp' to mean something else for its scripts says so.
        var natives = NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null);
        natives.Register("dotnet:System.Math::Clamp", [TypeTag.I64, TypeTag.I64, TypeTag.I64],
            TypeTag.I64, _ => LyrValue.FromI64(42));
        Assert.Equal(42, Interpreter.Run(Ok(Clamp), [], natives, Capability.All).AsI64);
    }
}
