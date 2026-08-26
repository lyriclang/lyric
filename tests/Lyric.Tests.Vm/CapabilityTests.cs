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
/// Capabilities (ADR-007, Doku §20.1) — M8/S6.
///
/// <para>THE REQUIREMENT STANDS IN THE MODULE, THE DECISION AT THE RUNTIME. The compiler writes into the
/// capabilities section WHAT a program wants to touch; at load time the VM checks against WHAT it grants.
/// The separation is not cosmetic: a `.lyrbc` can come from elsewhere, and a host loading foreign
/// bytecode has never seen the compiler. A pure resolve-time check would be worthless to it.</para>
/// </summary>
public class CapabilityTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static BytecodeModule Compile(string source)
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
        return BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!));
    }

    private const string UsesOs = """
        import std.os { platform };
        fn main(): int { let p = platform(); return 0; }
        """;

    // ------------------------------------------------------------------ the requirement in the module

    [Fact]
    public void A_program_that_touches_nothing_requires_nothing() =>
        Assert.Equal(0UL, Compile("fn main(): int { return 0; }").Capabilities);

    [Fact]
    public void Importing_a_gated_module_records_the_requirement() =>
        // The bit mask is part of the bytecode contract: 'osAccess' is bit 2. A test on the numeric value
        // rather than only on "not equal to 0", because a shifted assignment would make every older .lyrbc
        // wrong.
        Assert.Equal((ulong)Capability.OsAccess, Compile(UsesOs).Capabilities);

    [Fact]
    public void Std_time_rides_the_os_bit_rather_than_a_new_one() =>
        // Deliberate (v1.14): reading the clock is a question to the environment, and a NEW bit
        // would be a contract change every older runtime rejects.
        Assert.Equal((ulong)Capability.OsAccess, Compile("""
            import std.time { Instant };
            fn main(): int { return Instant.now().epochMillis() % 2; }
            """).Capabilities);

    [Fact]
    public void The_requirement_survives_a_round_trip() =>
        // It really stands IN the module rather than being kept alongside; the test goes through the writer
        // and the reader.
        Assert.Equal((ulong)Capability.OsAccess,
            BytecodeReader.ReadOrThrow(BytecodeWriter.Write(
                new Lyric.Ir.IrModule([]) { Capabilities = Capability.OsAccess })).Capabilities);

    // ------------------------------------------------------------------ the enforcement

    [Fact]
    public void A_runtime_that_grants_everything_runs_it() =>
        Assert.Equal(0, Interpreter.Run(Compile(UsesOs), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.All).AsI64);

    [Fact]
    public void A_runtime_that_grants_the_right_capability_runs_it() =>
        Assert.Equal(0, Interpreter.Run(Compile(UsesOs), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.OsAccess).AsI64);

    [Fact]
    public void A_runtime_that_grants_nothing_refuses()
    {
        var refused = Assert.Throws<LyricRuntimeException>(() => Interpreter.Run(Compile(UsesOs), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.None));

        Assert.Equal("LYR-CAP0001", refused.Code);
        Assert.Contains("osAccess", refused.Message);
    }

    [Fact]
    public void The_wrong_capability_does_not_help() =>
        // The counter-check to the test above: 'some' capability does not suffice. Without it the check
        // would stay green even if it only looked at "granted != None".
        Assert.Throws<LyricRuntimeException>(() => Interpreter.Run(Compile(UsesOs), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.FileAccess));

    [Fact]
    public void A_program_without_requirements_runs_in_a_sandbox() =>
        // The other direction: the check must not block everything running in a narrow VM. A program
        // requiring nothing runs with 'none' too.
        Assert.Equal(7, Interpreter.Run(Compile("fn main(): int { return 7; }"), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.None).AsI64);

    private const string UsesFile = """
        import std.io.file { exists };
        fn main(): int { if (exists("x")) { return 1; } return 0; }
        """;

    [Fact]
    public void A_module_that_under_declares_a_gated_native_is_refused_even_under_full_grant()
    {
        // The attacker's module: it calls std.io.file but its capabilities section says it needs
        // nothing. Binding used to trust the declaration and hand it the native anyway — so the
        // guarantee the capability comment makes ("a host loading foreign bytes is protected too")
        // did not hold for a module that lies. Now a gated native cannot be bound without the
        // declaration, so the lie is caught at load even where everything is granted.
        var honest = BytecodeWriter.Write(Lower(UsesFile));

        // The capabilities section is section 1, first after the 8-byte header: id 0x01, length
        // 0x01, then the one-byte bitset 0x01 (fileAccess). Zero the bitset to forge the lie.
        Assert.Equal(0x01, honest[8]);   // section id 1
        Assert.Equal(0x01, honest[9]);   // payload length 1
        Assert.Equal(0x01, honest[10]);  // bitset: fileAccess
        var forged = (byte[])honest.Clone();
        forged[10] = 0x00;

        var module = BytecodeReader.ReadOrThrow(forged);
        Assert.Equal(0UL, module.Capabilities);

        var refused = Assert.Throws<LyricRuntimeException>(() => Interpreter.Run(module, [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.All));
        Assert.Equal("LYR-CAP0001", refused.Code);
        Assert.Contains("fileAccess", refused.Message);
    }

    private static Lyric.Ir.IrModule Lower(string source)
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
        Assert.False(de.HasErrors);
        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);
        return ir!;
    }

    // ------------------------------------------------------------------ die Tabelle

    [Fact]
    public void Submodules_inherit_the_requirement_of_their_parent() =>
        // Otherwise every new submodule would be a silent gap: 'std.os.env' has to cost the same as
        // 'std.os'.
        Assert.Equal(Capability.OsAccess, CapabilityTable.RequiredForImport("std.os.env"));

    [Fact]
    public void An_always_allowed_module_costs_nothing()
    {
        Assert.Equal(Capability.None, CapabilityTable.RequiredForImport("std.string"));
        Assert.Equal(Capability.None, CapabilityTable.RequiredForImport("std.collections"));
        Assert.Equal(Capability.None, CapabilityTable.RequiredForImport("std.io.console"));
    }

    [Fact]
    public void A_similar_name_is_not_gated() =>
        // 'std.ostrich' starts with 'std.os' but is a different module. Without the dot boundary in the
        // comparison that would be a wrong rejection.
        Assert.Equal(Capability.None, CapabilityTable.RequiredForImport("std.ostrich"));

    [Fact]
    public void An_unknown_capability_name_is_rejected() =>
        // 'null' rather than 'None': silently granting less than requested would be the dangerous answer,
        // and the caller should report.
        Assert.Null(CapabilityTable.Parse("file,quantum"));

    private const string UsesProcess = """
        import std.process { start };
        fn main(): int { if (start("x", []) == null) { return 0; } return 1; }
        """;

    [Fact]
    public void Std_process_carries_its_own_bit_on_top_of_the_scheduler_s() =>
        // The FIFTH bit (4.0), and the point of it: starting programs is a NEW power, not an
        // osAccess refinement — so it is a bit osAccess does not imply. The os bit still
        // appears BESIDE it, honestly: std.process waits through std.task, whose poll is an
        // environment question. The numeric pin matters for the same reason as bit 2's above —
        // 0x10 is part of the bytecode contract now.
        Assert.Equal((ulong)(Capability.ProcessAccess | Capability.OsAccess),
            Compile(UsesProcess).Capabilities);

    [Fact]
    public void Os_access_does_not_let_a_program_start_processes()
    {
        // The doctrine test: a host that granted the environment questions has not thereby
        // agreed to arbitrary programs being started.
        var refused = Assert.Throws<LyricRuntimeException>(() => Interpreter.Run(
            Compile(UsesProcess), [],
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.OsAccess));

        Assert.Equal("LYR-CAP0001", refused.Code);
        Assert.Contains("processAccess", refused.Message);
    }
}
