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

    internal static IReadOnlyList<CEmitter.Unit> EmitC(string name)
    {
        var text = File.ReadAllText(Path.Combine(Programs, name + ".lyr"));
        var options = new CompilerOptions { StdlibRoot = Path.Combine(Root, "stdlib5") };
        var result = SourceCompiler.Lower(ScriptSource.FromBuffer($"programs/{name}.lyr", text), options);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());
        Assert.True(SubsetGate.Check(result.Ir!, result.Diagnostics), rendered.ToString());
        return CEmitter.Emit(result.Ir!, result.Sources, options.StdlibRoot)
            .Select(u => u with { Text = u.Text.Replace("\r\n", "\n") }).ToList();
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
    [InlineData("enums")]
    [InlineData("patterns")]
    [InlineData("arrays")]
    [InlineData("slices")]
    [InlineData("inline")]
    [InlineData("tuples")]
    [InlineData("ranges")]
    [InlineData("with")]
    [InlineData("globals")]
    [InlineData("closures")]
    [InlineData("generics")]
    [InlineData("interfaces")]
    [InlineData("calls")]
    [InlineData("dispatch")]
    [InlineData("selfish")]
    public void The_emission_matches_its_golden(string name)
    {
        var actual = CEmitter.Join(EmitC(name));
        var path = Path.Combine(Golden, name + ".c");
        if (Environment.GetEnvironmentVariable("LYRIC_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Golden);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), $"no golden at {path}; set LYRIC_UPDATE_SNAPSHOTS=1 to write it");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    private const string TUPLES_EXPECTED =
        "pair 1 two\nlabels 3 4 3\nswap 2 9\nnested 5 hi\nfield 5,12 169\narray 3 2 5\nmatch two-first 3hi\noptional 2\n";

    private const string RANGES_EXPECTED = "value 1 4 3\ninclusive 5 9\nheld 2 8 6\nloop 16\n";

    private const string GLOBALS_EXPECTED = "start 3 6 hello!\ncounter 0 3 4\nstatic 0,0 100\nobject 2 bob 9 1\n";

    private const string CLOSURES_EXPECTED =
        "forms 2 6 6 14 3 5\ncapture 3 30 92\ncounter 1 2 3 2\nloop 0 1 2\nthis 15 4\nopt 4 none\nmade 0 10 20\n"
        + "trailing 6 14 6\nbound 2 2 0 20 10\n";

    private const string GENERICS_EXPECTED =
        "value 4 5 hi\npair 3 x x 3\ncollect n7 9 11\nstatic 2 1 true\n";

    private const string INTERFACES_EXPECTED =
        "hit 60 80\nalive alive alive down\nbox 3 10 3\nenum 1 0\narray 4 143 60 60\nchain 882\n";

    private const string CALLS_EXPECTED =
        "arity 7 12 60\nnamed 1 2 3\ndefault 10 2 3\nparams 6 3\nfactory 3 4\n";

    private const string DISPATCH_EXPECTED =
        "paths 7 7 7\nblocks hello hi\nby 4 4 walk run\nouter run4\n";

    private const string SELFISH_EXPECTED =
        "self true false 2\nconstraint true 7\ndefault 3 6 4\n";

    private const string WITH_EXPECTED =
        "moved 3,4 1,2\nswapped 4,3\nnested 9 1 2\nchained 5 7\nheld 20 10\ngeneric 8 hi\n";

    private const string INLINE_EXPECTED =
        "copy 10 1 3 3\nrepeat 0 7 7\nheap 2 60 80\nstruct 100 1 40\ngrid 3 2 2\narray 8 7 7 7\nmatch 15\n";

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
            data.Add("enums", profile, 0,
                "signals red yellow green\nareas 0 12 6 7\n"
                + "describe round round flat square rect origin far@3,4\ngrade zero digit neg many\n"
                + "maybe none green\nmethod true false 2\nscene 6 green\ntree 7\n");
            data.Add("arrays", profile, 0,
                "literal 3 10 20 30\nstore 99 20\nrepeat 5 7 7\nconcat 5 1 5\nfromend 30 99 50\nstruct 3,4 9 5 2\nnested 2 3 6\n"
                + "refs alice bob 41 2\nsum 55\nopt 1 none\n");
            data.Add("slices", profile, 0,
                "view 3 20 40 90\nopen 2 40 5 30\nthrough 21 31 51 31\nnested 2 31 41\nwhole 154\nfield 2 99 51\n"
                + "strings bob cy 2\nstructs 30 6 0\nmatch empty one 10 first 10 rest 4\nempty 0 0\n");
            data.Add("inline", profile, 0, INLINE_EXPECTED);
            data.Add("tuples", profile, 0, TUPLES_EXPECTED);
            data.Add("ranges", profile, 0, RANGES_EXPECTED);
            data.Add("with", profile, 0, WITH_EXPECTED);
            data.Add("globals", profile, 0, GLOBALS_EXPECTED);
            data.Add("closures", profile, 0, CLOSURES_EXPECTED);
            data.Add("generics", profile, 0, GENERICS_EXPECTED);
            data.Add("interfaces", profile, 0, INTERFACES_EXPECTED);
            data.Add("calls", profile, 0, CALLS_EXPECTED);
            data.Add("dispatch", profile, 0, DISPATCH_EXPECTED);
            data.Add("selfish", profile, 0, SELFISH_EXPECTED);
            data.Add("patterns", profile, 0,
                "lights red green green yellow\nshapes 3 6 0\nmatch num-3 flat 5 wide 4 rect 2x3 empty\n"
                + "either stop stop go\nnested 7 none 0 6\niflet 7 else 1 num 3\noptional none green\n");
        }
        return data;
    }

    /// <summary>The cache unit a function belongs to (01 C3), read off its IR name.</summary>
    [Theory]
    [InlineData("main.main", null)]
    [InlineData("main.main.<lambda1>", null)]
    [InlineData("<globals>", null)]
    [InlineData("main.main.<bound_add1>", null)]
    [InlineData("std.core.arrayOf<int>", "std.core.arrayOf<int>")]
    [InlineData("std.core.arrayOf<int>.<lambda0>", "std.core.arrayOf<int>")]
    [InlineData("std.core.Range<int>.next<>", "std.core.Range<int>")]
    [InlineData("main.Pair<Pair<int, string>>.swap<>", "main.Pair<Pair<int, string>>")]
    [InlineData("main.collect<int, fn(int) -> string>", "main.collect<int, fn(int) -> string>")]
    public void A_function_belongs_to_the_unit_of_its_instance(string irName, string? unit) =>
        Assert.Equal(unit, CEmitter.InstanceOf(irName));

    [Fact]
    public void The_generic_instances_are_units_of_their_own()
    {
        var units = EmitC("generics");
        Assert.Null(units[0].Instance);
        Assert.Equal(
            ["main.collect<int, string>", "main.ident<int>", "main.ident<string>", "main.make<bool>",
             "main.swap<int, string>", "main.twice<int>"],
            units.Skip(1).Select(u => u.Instance).ToArray());

        // The module's unit defines the descriptors and the entry, and holds no instance body;
        // an instance's unit holds its function, declares what it calls, and defines no descriptor.
        Assert.Contains("int main(int argc, char **argv)", units[0].Text);
        Assert.DoesNotContain("lyr_main_ident_int__365390bd(int64_t l0_x) {", units[0].Text);
        var twice = units.Single(u => u.Instance == "main.twice<int>").Text;
        Assert.Contains("int64_t lyr_main_twice_int__8457f847(lyr_fn_i64_to_i64 l0_f, int64_t l1_x) {", twice);
        Assert.DoesNotContain("const LyrDesc lyr_desc", twice.Replace("extern const LyrDesc", ""));
        Assert.DoesNotContain("int main(", twice);
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
    [InlineData("enums_gc", Profile.Debug, "kept label-999999 tree 499500")]
    [InlineData("enums_gc", Profile.Release, "kept label-999999 tree 499500")]
    [InlineData("interfaces_gc", Profile.Debug, "kept 1000 junk-999999 500499000")]
    [InlineData("interfaces_gc", Profile.Release, "kept 1000 junk-999999 500499000")]
    [InlineData("arrays_gc", Profile.Debug, "kept 499500 item-999999 tag-999999 500")]
    [InlineData("arrays_gc", Profile.Release, "kept 499500 item-999999 tag-999999 500")]
    [InlineData("globals_gc", Profile.Debug, "kept 499500 item-999999 anchor")]
    [InlineData("globals_gc", Profile.Release, "kept 499500 item-999999 anchor")]
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
        { "index", Profile.Debug, "panic [LYR-RT0003]: index 3 out of bounds for length 3" },
        { "index", Profile.Release, "panic [LYR-RT0003]: index 3 out of bounds for length 3" },
        { "negindex", Profile.Debug, "panic [LYR-RT0003]: index -1 out of bounds for length 3" },
        { "negindex", Profile.Release, "panic [LYR-RT0003]: index -1 out of bounds for length 3" },
        { "badslice", Profile.Debug, "panic [LYR-RT0003]: range 2..7 out of bounds for length 5" },
        { "badslice", Profile.Release, "panic [LYR-RT0003]: range 2..7 out of bounds for length 5" },
        { "inverted", Profile.Debug, "panic [LYR-RT0003]: range 3..1 out of bounds for length 5" },
        { "inverted", Profile.Release, "panic [LYR-RT0003]: range 3..1 out of bounds for length 5" },
        { "inlineindex", Profile.Debug, "panic [LYR-RT0003]: index 4 out of bounds for length 4" },
        { "inlineindex", Profile.Release, "panic [LYR-RT0003]: index 4 out of bounds for length 4" },
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
