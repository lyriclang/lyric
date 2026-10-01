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
    [InlineData("assoc")]
    [InlineData("anything")]
    [InlineData("shapes")]
    [InlineData("vectors")]
    [InlineData("extends")]
    [InlineData("shapes_ext")]
    [InlineData("synth")]
    [InlineData("lambdas")]
    [InlineData("cloning")]
    [InlineData("errors")]
    [InlineData("error_paths")]
    [InlineData("uncaught")]
    [InlineData("try_forms")]
    [InlineData("catch_sets")]
    [InlineData("defer_errors")]
    [InlineData("resources")]
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

    // Conformance synthesis (04 D7): the family, generic types conditionally, enums, the implicit Debug.
    private const string SYNTH_EXPECTED =
        "eq true false lt true false hash true\nclone 1 default 0 0 show P { x = 1, y = 2 }\n"
        + "pair true Pair { a = \"a\", b = \"b\" }\nenum true false true E.B(3) E.R { w = 4 }\n"
        + "class 2 C { items = [1, 2], o = null, t = (7, \"x\") }\nw true\n"
        + "color false true false true false Color.Green\n"
        + "outer true Outer { i = Inner { v = 1 }, s = \"s\", c = 'c', b = true, f = 1.5 }\n"
        + "opt true false true Opt.Some(1)\ndefault D { n = 0, label = \"lbl\" } Box { v = 0 }\n"
        + "total Ordering.Greater true false false\nshape true true false true true true true\n"
        + "inline Mat { m = [1, 2, 3, 4] } true 2 Buf { b = [0, 0] }\nheld 5\n";

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

    private const string ASSOC_EXPECTED =
        "first 41 x\nfixed 42 7\nnested 3 5\n";

    private const string SHAPES_EXT_EXPECTED =
        "sum 6 first 1 last 3\nopt 5 0 some true false\npair 3\nview 2 slice 20\n";

    private const string EXTENDS_EXPECTED =
        "first 1 a\nshow [1, 2] <a, b>\npair 3 x\nconcrete 7\n";

    private const string VECTORS_EXPECTED =
        "add 4 6 sub 2 2 mul 3 6 scaled 4.5 2.5\nneg -1 -2 eq true false lt true false le true\n"
        + "sum 10 13 compound 7 (4, 6)\nrem 1 bits 1 7 6 -3 8 2\nfloat false false partial\n";

    private const string SHAPES_EXPECTED =
        "areas 12 12 6\nnames circle rect tri\ndisplay circle rect\nkinds 2 7 4\n";

    private const string ANYTHING_EXPECTED =
        "is circle r=2 rect 3x4 shape\nmatch 2 7 0\nany true false 5 3\nnamed true false circle\n";

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
            data.Add("assoc", profile, 0, ASSOC_EXPECTED);
            data.Add("anything", profile, 0, ANYTHING_EXPECTED);
            data.Add("shapes", profile, 0, SHAPES_EXPECTED);
            data.Add("vectors", profile, 0, VECTORS_EXPECTED);
            data.Add("extends", profile, 0, EXTENDS_EXPECTED);
            data.Add("shapes_ext", profile, 0, SHAPES_EXT_EXPECTED);
            data.Add("synth", profile, 0, SYNTH_EXPECTED);
            data.Add("lambdas", profile, 0, "forms 12 6 500 7\nfold 10 24\neach 10\npairs 14 10\nbound 21 7\n");
            data.Add("cloning", profile, 0,
                "opt true false true false true false\nmixed true false true\nstruct true false\n"
                + "repeat 3 1 0 0 true\nnested 2 5 0\n");
            data.Add("errors", profile, 0, ERRORS_EXPECTED);
            data.Add("error_paths", profile, 0, ERROR_PATHS_EXPECTED);
            data.Add("try_forms", profile, 0, TRY_FORMS_EXPECTED);
            data.Add("catch_sets", profile, 0, CATCH_SETS_EXPECTED);
            data.Add("defer_errors", profile, 0, DEFER_ERRORS_EXPECTED);
            data.Add("resources", profile, 0, RESOURCES_EXPECTED);
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

    private const string ERRORS_EXPECTED =
        "defer 3\ndefer 2\ndefer 1\ndeep caught 7 at 3\niface caught disk\ndefer 3\nall caught parse\n"
        + "data caught 1..9 got 12\ncause caught config / disk\norder: body\norder: inner defer\norder: clause\n"
        + "rethrow inner saw parse\nrethrow outer got parse\nsister outer got disk\nclean 5 no error\n"
        + "ret defer ran\nret 9\n";

    private const string ERROR_PATHS_EXPECTED =
        "slot: 7 then caught bad\nmethod: 3 then caught bad\ngeneric: 5 then caught Box\nchain: a b c caught\n"
        + "loop: 1 2 skip 4 done\nlambda: caught inside 9\nnested: inner clause caught\nclausereturn defer\n"
        + "clausereturn: 42\n";

    private const string TRY_FORMS_EXPECTED =
        "a 42 b -1\nc true -7\nd false\ne true 42\nf 42\ng -1\nh caught disk\nh fallback\ni data\nj 2\n"
        + "k none\nsaved\nsave failed: disk\nsum 84\ndefer in callee\nm 0\n";

    private const string RESOURCES_EXPECTED =
        "body\nclose b\ndefer between\nclose a\nwork\nclose x\ncaught close x failed\nclose y\ncaught body failed\n"
        + "work\nclose x\ncaught wrapped because close x failed\n";

    private const string DEFER_ERRORS_EXPECTED =
        "earlier defer ran\nnormal: first\nin flight: first\ntwo: second\noutside: first\ninside caught second\n"
        + "break: first\ncontinue: first\ncaught in the defer\nown try: third\ninner defer\nnested: third\n";

    private const string CATCH_SETS_EXPECTED =
        "set took parse\ndisk took disk\nset took timeout\nvalue 4\nsingle disk\nother\n-1\nrethrown disk\n"
        + "parse\ndisk\ntimeout\nok 5\n";

    /// <summary>An error that escapes main (design/v5/spec/05 E6 O4): the output up to the throw,
    /// then on the error stream its message and each cause in the chain, and the exit code 1 — a
    /// panic's is 101. The debug profile goes on with where it was thrown (01 E8): 'config' at its
    /// throw, then 'main' at its call; the release profile keeps no trace.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void An_error_escaping_main_reports_its_chain_and_exits_with_1(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("uncaught"), "uncaught", profile);
        Assert.True(result.ExitCode == 1, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("before\n", result.Stdout.Replace("\r\n", "\n"));
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["error: config", "  caused by: disk"], lines.Take(2));
        if (profile == Profile.Release)
        {
            Assert.Equal(2, lines.Length);
            return;
        }
        Assert.Matches(@"^    at lyr_main_config \(.*programs[\\/]uncaught\.lyr:14\)$", lines[2]);
        Assert.Matches(@"^    at lyr_main_main \(.*programs[\\/]uncaught\.lyr:19\)$", lines[3]);
    }

    /// <summary>A defer that fails while an error leaves main (design/v5/spec/05 E7, E6 O4): the first
    /// error is reported, the defer's below it as suppressed, and the exit code is 1.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_suppressed_error_is_reported_below_the_one_that_won(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("uncaught_suppressed"), "uncaught_suppressed", profile);
        Assert.True(result.ExitCode == 1, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("", result.Stdout);
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["error: first", "  suppressed: second"], lines.Take(2));
        // The trace is the first error's, where it was thrown — not the defer's.
        if (profile == Profile.Debug)
            Assert.Matches(@"^    at lyr_main_main \(.*programs[\\/]uncaught_suppressed\.lyr:10\)$", lines[2]);
        else
            Assert.Equal(2, lines.Length);
    }

    /// <summary>An error thrown deep in a recursion (design/v5/spec/01 E8): the debug trace shows the
    /// recursion's frame once with a count, as a panic's does, and says the stack went deeper than
    /// it kept — the capture keeps as many frames as a panic's trace.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_deep_error_folds_its_recursion_and_says_the_trace_was_cut(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("deep_error"), "deep_error", profile);
        Assert.True(result.ExitCode == 1, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("error: deep", lines[0]);
        if (profile == Profile.Release)
        {
            Assert.Single(lines);
            return;
        }
        Assert.True(lines.Length == 5, $"stderr:\n{result.Stderr}");
        Assert.Matches(@"^    at lyr_main_down \(.*programs[\\/]deep_error\.lyr:4\)$", lines[1]);
        Assert.Matches(@"^    at lyr_main_down \(.*programs[\\/]deep_error\.lyr:5\)$", lines[2]);
        Assert.Matches(@"^    \.\.\. the frame above repeats \d+ more times$", lines[3]);
        Assert.Equal("    ... deeper frames not shown", lines[4]);
    }

    /// <summary>A clause that throws its own binding again goes on with the same error
    /// (design/v5/spec/05 E6 O3): main's report still names what was suppressed into it after a
    /// clause without a type and a typed one each threw it again, and the debug trace is where it
    /// was first thrown, not where it was thrown again.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_rethrown_binding_is_the_error_it_caught(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("rethrow"), "rethrow", profile);
        Assert.True(result.ExitCode == 1, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("middle saw first\nouter saw first\n", result.Stdout.Replace("\r\n", "\n"));
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["error: first", "  suppressed: second"], lines.Take(2));
        if (profile == Profile.Release)
        {
            Assert.Equal(2, lines.Length);
            return;
        }
        (string Function, int Line)[] frames = [("fail", 10), ("inner", 16), ("middle", 20), ("outer", 27), ("main", 34)];
        Assert.True(lines.Length == 2 + frames.Length, $"stderr:\n{result.Stderr}");
        for (var i = 0; i < frames.Length; i++)
            Assert.Matches($@"^    at lyr_main_{frames[i].Function} \(.*programs[\\/]rethrow\.lyr:{frames[i].Line}\)$", lines[2 + i]);
    }

    /// <summary><c>try!</c> on an error (design/v5/spec/05 E4, E8): a panic, <c>LYR-RT0010</c>, with
    /// the error's message and the trace at the <c>try!</c>'s line — and no <c>defer</c> runs, main's
    /// included.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void Try_bang_on_an_error_panics_with_its_message(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(EmitC("forced"), "forced", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("before\n", result.Stdout.Replace("\r\n", "\n"));
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("panic [LYR-RT0010]: 'try!' on an error: boom", lines[0]);
        Assert.Matches(@"^    at lyr_main_main \(.*programs[\\/]forced\.lyr:11\)$", lines[1]);
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
