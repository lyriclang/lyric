using Lyric.Compiler;
using Lyric5.Compiler;
using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The C emitter (M2 S2; design/v5/spec/01 L8) on the programs under <c>programs/</c>: the C is
/// compared byte for byte against <c>golden/*.c</c> (a change in emission is a deliberate one —
/// <c>LYRIC_UPDATE_SNAPSHOTS=1</c> rewrites the files), then built with the runtime and run in
/// both profiles, where the exit code and the panic line are the program's own statement of what
/// it does. Programs are read through a display name, so the <c>#line</c> paths are the same on
/// every machine.
/// </summary>
public class CEmitterTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private static readonly string Programs = Path.Combine(Root, "tests", "Lyric5.Tests", "programs");
    private static readonly string Golden = Path.Combine(Root, "tests", "Lyric5.Tests", "golden");

    internal static string EmitC(string name)
    {
        var text = File.ReadAllText(Path.Combine(Programs, name + ".lyr"));
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer($"programs/{name}.lyr", text), options);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());
        Assert.True(SubsetGate.Check(result.Ir!, result.Diagnostics), rendered.ToString());
        return CEmitter.Emit(result.Ir!, result.Sources).Replace("\r\n", "\n");
    }

    [Theory]
    [InlineData("fib")]
    [InlineData("count")]
    [InlineData("checks")]
    public void The_emission_matches_its_golden(string name)
    {
        var actual = EmitC(name);
        var path = Path.Combine(Golden, name + ".c");
        if (Environment.GetEnvironmentVariable("LYRIC_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Golden);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), $"no golden at {path}; set LYRIC_UPDATE_SNAPSHOTS=1 to write it");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    public static TheoryData<string, Profile, int> Programs_() => new()
    {
        { "fib", Profile.Debug, 55 }, { "fib", Profile.Release, 55 },
        { "count", Profile.Debug, 13 }, { "count", Profile.Release, 13 },
        { "checks", Profile.Debug, 42 }, { "checks", Profile.Release, 42 },
    };

    [Theory]
    [MemberData(nameof(Programs_))]
    public void A_program_runs_natively(string name, Profile profile, int exit)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC(name), name, profile);
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        Assert.Equal("", result.Stderr);
    }

    public static TheoryData<string, Profile, string> Panics() => new()
    {
        { "overflow", Profile.Debug, "panic [LYR-RT0002]: arithmetic overflow in '+'" },
        { "overflow", Profile.Release, "panic [LYR-RT0002]: arithmetic overflow in '+'" },
        { "div0", Profile.Debug, "panic [LYR-RT0001]: division by zero" },
        { "div0", Profile.Release, "panic [LYR-RT0001]: division by zero" },
        { "remmin", Profile.Debug, "panic [LYR-RT0002]: arithmetic overflow in '%'" },
        { "remmin", Profile.Release, "panic [LYR-RT0002]: arithmetic overflow in '%'" },
        { "negmin", Profile.Debug, "panic [LYR-RT0002]: arithmetic overflow in '-'" },
        { "negmin", Profile.Release, "panic [LYR-RT0002]: arithmetic overflow in '-'" },
    };

    /// <summary>The checks hold in the release profile too (03 T2: in every profile), and the
    /// trace names the program's function and its <c>.lyr</c> line, through <c>#line</c>.</summary>
    [Theory]
    [MemberData(nameof(Panics))]
    public void A_check_that_fails_panics_with_the_lyr_line(string name, Profile profile, string firstLine)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC(name), name, profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(firstLine, lines[0]);
        // The path as the debug information resolved it: relative on Linux and macOS (DWARF keeps
        // what #line said), absolute on Windows (the PDB resolves it against the build directory).
        Assert.Matches(@"^    at lyr_main_\w+ \(.*programs[\\/]" + name + @"\.lyr:2\)$", lines[1]);
        Assert.Matches(@"^    at lyr_main_main \(.*programs[\\/]" + name + @"\.lyr:3\)$", lines[2]);
    }
}
