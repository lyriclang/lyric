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
    [InlineData("hello")]
    [InlineData("structs")]
    [InlineData("arith")]
    [InlineData("objects")]
    [InlineData("optionals")]
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

    public static TheoryData<string, Profile, int, string> Programs_()
    {
        var data = new TheoryData<string, Profile, int, string>();
        foreach (var profile in new[] { Profile.Debug, Profile.Release })
        {
            data.Add("fib", profile, 55, "");
            data.Add("count", profile, 13, "");
            data.Add("checks", profile, 42, "");
            data.Add("structs", profile, 18, "");
            data.Add("hello", profile, 0, "Hello, Lyric!\n");
            data.Add("fizzbuzz", profile, 0,
                "1\n2\nFizz\n4\nBuzz\nFizz\n7\n8\nFizz\nBuzz\n11\nFizz\n13\n14\nFizzBuzz\n");
            data.Add("strings", profile, 0, "Grüße, Lyric!\nn=42 u=7 b=true\na-b-c\ntab\there\n");
            data.Add("arith", profile, 0,
                "alias 42\nwiden 300\nwrap -128 255 0\nsat 127 -128 0 255\ntrunc -3 3\nchar 65 A ok\n"
                + "shl 8 -1 255\nwrapop 0 255 1\nswrap -128 127\nfloat 1.5 2 0.09999999999999998 0.5\n"
                + "fdiv inf -inf NaN\nf32 0.10000000149011612 0.30000001192092896\ncmp true false\n"
                + "big 9007199254740993 1e+21\n");
            data.Add("objects", profile, 0,
                "alice 150\nshared 175\nidentity true false\nteam alice+bob\nswapped alice\n"
                + "origin 3,4 moved 13,4\ncounter 3\ncopy 3 then 4\nreset 0 kept 3\nmade carol 0\n");
            data.Add("optionals", profile, 0,
                "find 8 -1\nzero 8 0\nlength 3 0\nchain 3 -1\nforce 2\nnested absent null 7\n"
                + "place 9,5 copy 9,2\nname anon\nbox 0 5 b\niflet 42\n");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Programs_))]
    public void A_program_runs_natively(string name, Profile profile, int exit, string stdout)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC(name), name, profile);
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        Assert.Equal("", result.Stderr);
        Assert.Equal(stdout, result.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// A class graph survives collections (01 V4): the program churns through far more memory
    /// than the 16 MiB the harness allows, so it ends only because its garbage is collected, and
    /// it prints the right line only because what the holder references is not — the emitter's
    /// descriptors say which objects hold references, and an object wrongly marked as holding
    /// none would lose what it points at. The harness reports the collections that ran: the
    /// claim needs at least a handful.
    ///
    /// <para>What this does NOT test: the bits of the reference bitmap. The stage-1 collector
    /// scans an object that holds references as a whole and reads only the flag; the bitmap is
    /// held by the layout asserts in the emitted C and by the goldens until a collector reads
    /// it (stage 2, M11).</para>
    /// </summary>
    [Theory]
    [InlineData("objects_gc", Profile.Debug, "kept 499500 held-999999 held-0 alice")]
    [InlineData("objects_gc", Profile.Release, "kept 499500 held-999999 held-0 alice")]
    [InlineData("optionals_gc", Profile.Debug, "list 1000 499500 node-999 tag-999")]
    [InlineData("optionals_gc", Profile.Release, "list 1000 499500 node-999 tag-999")]
    public void A_class_graph_survives_collections(string name, Profile profile, string line)
    {
        var result = RuntimeBuildTests.RunEmittedUnder("limited_main", EmitC(name), name, profile);
        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = result.Stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(line, lines[0]);
        Assert.StartsWith("collections ", lines[1]);
        var collections = int.Parse(lines[1]["collections ".Length..]);
        Assert.True(collections >= 5, $"only {collections} collections ran: the graph was not tested against the collector");
    }

    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_panic_call_ends_with_its_message(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("panic"), "panic", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("panic [LYR-RT0008]: 3 is too many", lines[0]);
        Assert.Matches(@"^    at lyr_main_check \(.*programs[\\/]panic\.lyr:3\)$", lines[1]);
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
        { "shift", Profile.Debug, "panic [LYR-RT0002]: shift by 64 exceeds the width of 64 bits" },
        { "shift", Profile.Release, "panic [LYR-RT0002]: shift by 64 exceeds the width of 64 bits" },
        { "badchar", Profile.Debug, "panic [LYR-RT0009]: 0xD800 is not a Unicode scalar value" },
        { "badchar", Profile.Release, "panic [LYR-RT0009]: 0xD800 is not a Unicode scalar value" },
        { "unwrap", Profile.Debug, "panic [LYR-RT0004]: unwrapped a null value" },
        { "unwrap", Profile.Release, "panic [LYR-RT0004]: unwrapped a null value" },
        { "unwrapref", Profile.Debug, "panic [LYR-RT0004]: unwrapped a null value" },
        { "unwrapref", Profile.Release, "panic [LYR-RT0004]: unwrapped a null value" },
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
