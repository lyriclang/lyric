using Lyric.Ir;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// <c>comptime e</c> through the IR interpreter (design/v5/spec/09 A4, Q3, Q5–Q7; 13 M9a S1): a
/// site is a constant in the binary, computed as the binary computes it, and a site that stops —
/// a panic, its budget spent, a call into the world, a native compile time cannot answer yet — is
/// an error where it stands, with the frames the evaluation went through.
///
/// <para>Through <c>Main</c> where a manifest or a profile matters, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class ComptimeTests
{
    private const string Head = "import std.io { println };\n\n";

    private static string[] Errors(string main, string manifest = AppManifest)
    {
        var dir = Package(("lyric.toml", manifest), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        return error.Split('\n');
    }

    /// <summary>Each case of <c>programs/comptime_same.lyr</c> at compile time and in the binary,
    /// in both profiles: the same text to the byte — every native the cases reach, the number
    /// tower, the float texts and the parse, the strings, the containers, the error paths.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_site_computes_what_the_binary_computes(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC("comptime_same"), "comptime_same", profile);
        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");
        Assert.Equal("14 cases, 0 differ\n", result.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>The comparison above holds vacuously where a site is not computed at all — lowered
    /// as the expression it holds, as before M9a: then <c>main</c> calls each case twice. A
    /// constant leaves the run's call alone.</summary>
    [Fact]
    public void A_site_is_a_constant_in_its_function()
    {
        var text = File.ReadAllText(Path.Combine(CEmitterTests.Programs, "comptime_same.lyr"));
        var result = TestCompiler.Lower("programs/comptime_same.lyr", text);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok, rendered.ToString());
        var ir = result.Ir!;
        var main = ir.Functions.Single(f => f.Name == "app.main.main");
        var called = main.Blocks.SelectMany(b => b.Insts).OfType<Call>()
            .Select(c => ir.Functions[c.Target.Value].Name)
            .ToList();
        foreach (var name in new[] { "tower", "checked", "bits", "divisions", "floats", "formats", "parsing", "strings", "structures", "debugs", "record", "unicode" })
            Assert.Single(called, $"app.main.{name}");
    }

    /// <summary>The artifact of the slice: <c>comptime fib(30)</c> is 832040 in the binary, and
    /// <c>fib</c>, which nothing else calls, is no part of it.</summary>
    [Fact]
    public void A_function_only_a_site_calls_is_gone()
    {
        const string main = Head + "fn fib(n: int): int {\n    return if (n < 2) n else fib(n - 1) + fib(n - 2);\n}\n\n"
                            + "fn main(): void {\n    println(f\"{comptime fib(30)}\");\n}\n";
        var lowered = TestCompiler.Lower("main.lyr", main);
        Assert.True(lowered.Ok);
        Assert.DoesNotContain(lowered.Ir!.Functions, f => f.Name == "app.main.fib");

        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        Assert.Equal("832040\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A site fills the module bindings it reads and no other (13 M9a S5): std.path's,
    /// which asks the platform, stays unfilled. Filled with all the others at the first read, as
    /// S1 did, it stopped every site that read a binding in a program that loads std.path
    /// (<c>LYR-CT0003</c>) — S5 found it through <c>@Test { timeout = LIMIT }</c>.</summary>
    [Fact]
    public void A_site_fills_only_the_globals_it_reads()
    {
        const string main = Head + "import std.path { join };\n\nlet base = 40;\n\n"
                            + "fn main(): void {\n    println(f\"{comptime base + 2} {join(\"a\", \"b\")}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        Assert.Equal($"42 a{Path.DirectorySeparatorChar}b\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    private const string Spin = Head + "fn spin(n: int): int {\n    var s = 0;\n    var i = 0;\n    while (i < n) {\n"
                                + "        s += i;\n        i += 1;\n    }\n    return s;\n}\n\n";

    /// <summary>The budget is each site's, from the root manifest (09 Q5): a round of the loop
    /// takes 13 steps, a thousand rounds 13 014 — two such sites fit a budget of 20 000 each, one
    /// of two thousand rounds does not, and the error names the budget and where it is set.</summary>
    [Fact]
    public void Each_site_has_the_budget_the_manifest_sets()
    {
        var manifest = AppManifest + "comptimeBudget = 20000\n";
        var two = Package(("lyric.toml", manifest),
            ("src/main.lyr", Spin + "fn main(): void {\n    println(f\"{comptime spin(1000)} {comptime spin(1000)}\");\n}\n"));
        Assert.Equal("499500 499500\n", BuildAndRun(two, "app", "build", "-C", two));

        var errors = Errors(Spin + "fn main(): void {\n    println(f\"{comptime spin(2000)}\");\n}\n", manifest);
        var error = Assert.Single(errors, l => l.Contains("error["));
        Assert.Contains("error[LYR-CT0002]: the evaluation took more than its budget of 20000 steps", error);
        Assert.Contains("'comptimeBudget' under [package] in lyric.toml", error);
    }

    /// <summary>A site computes as the binary would (09 Q7): its overflow panics where the profile
    /// checks — an error with the run-time code and the frames —, and wraps where it does not.</summary>
    [Fact]
    public void A_site_overflows_as_the_profile_says()
    {
        const string main = Head + "fn inc(x: int32): int32 {\n    return x + 1;\n}\n\n"
                            + "fn main(): void {\n    println(f\"{comptime inc(2147483647)}\");\n}\n";
        var wrapping = Package(("lyric.toml", AppManifest + "\n[profile.debug]\noverflowChecks = false\n"), ("src/main.lyr", main));
        Assert.Equal("-2147483648\n", BuildAndRun(wrapping, "app", "build", "-C", wrapping));

        var errors = Errors(main);
        Assert.Contains(errors, l => l.Contains("error[LYR-CT0001]: the evaluation panicked: ") && l.Contains("(LYR-RT0002 at run time)"));
        Assert.Contains(errors, l => l.Contains("note: in 'app.main.inc'") && l.Contains("main.lyr:4:12"));
    }

    /// <summary>A panic is an error at the site with the inner backtrace (09 Q6), innermost first:
    /// twelve frames, and how many more there were.</summary>
    [Fact]
    public void A_panic_shows_the_frames_it_came_through()
    {
        var errors = Errors("fn down(n: int): int {\n    if (n == 0) {\n        return 1 / n;\n    }\n    return down(n - 1);\n}\n\n"
                            + "fn main(): void {\n    let _ = comptime down(20);\n}\n");
        Assert.Contains(errors, l => l.Contains("error[LYR-CT0001]: the evaluation panicked: division by zero (LYR-RT0001 at run time)"));
        var frames = errors.Where(l => l.Contains("note: in 'app.main.down'")).ToList();
        Assert.Equal(12, frames.Count);
        Assert.Contains("main.lyr:3:16", frames[0]);
        Assert.Contains("main.lyr:5:12", frames[1]);
        Assert.Contains(errors, l => l.Contains("note: … 9 frames more"));
    }

    /// <summary>Code run at compile time does not reach the world (09 Q3): a file read is an error
    /// at the site, with the chain of calls that led to it.</summary>
    [Fact]
    public void A_call_into_the_world_is_an_error_with_its_chain()
    {
        var errors = Errors("import std.fs { readText };\n\nfn read(): string {\n    return try! readText(\"x.txt\");\n}\n\n"
                            + "fn main(): void {\n    let _ = comptime read();\n}\n");
        Assert.Contains(errors, l => l.Contains("error[LYR-CT0003]: ") && l.Contains("a file") && l.Contains("code run at compile time does not"));
        Assert.Contains(errors, l => l.Contains("note: in 'app.main.read'"));
    }

    /// <summary>Compile time answers what every platform answers alike (09 Q7): the exactly
    /// rounded functions, Unicode from the runtime's own tables (S4), and since M10 S12 the
    /// functions each C library used to round its own way — musl's, in the runtime and in the
    /// compiler as the same code (09 D7); that the bits are the program's is LibmTests.</summary>
    [Fact]
    public void What_every_platform_answers_alike_is_answered()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + "import std.math { exp, pow, sqrt };\n\n"
            + "fn main(): void {\n    println(f\"{comptime sqrt(2.0)} {comptime 'É'.isUpper()} {comptime exp(1.0)} {comptime pow(2.0, 0.5)}\");\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, $"exit {exit}\n{error}");
    }

    /// <summary>A site leaves a value of any value type (09 Q1; 13 M9a P3, S2): the CRC table as
    /// a <c>uint32[]</c>, a fresh array at every evaluation, which a write to the first does not
    /// reach; a struct, enums with payloads, a recursive enum through <c>Box</c>, optionals, a
    /// tuple, an inline array, arrays of strings and of arrays, a range.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_site_leaves_a_value_of_any_value_type(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC("comptime_values"), "comptime_values", profile);
        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("256 1996959894 755167117\n5 0\nPoint { x = 3, y = 0.5 }\n"
                     + "[Shape.Dot, Shape.Circle(Point { x = 1, y = 2.0 }, 3.0), Shape.Named { name = \"n\", at = Point { x = 0, y = 0.0 } }]\n"
                     + "8\nPoint { x = 7, y = -1.5 } null\n(1, \"two\", 3.0)\n[1, 2, 3, 40]\n[\"a\", \"bc\", \"déf\"]\n[[1, 2], [3]]\n0 5\n",
            result.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>An array of scalars is the C's static data, copied into each fresh array; the
    /// function that computed it is no part of the binary.</summary>
    [Fact]
    public void An_array_of_scalars_is_static_data()
    {
        var text = File.ReadAllText(Path.Combine(CEmitterTests.Programs, "comptime_values.lyr"));
        var lowered = TestCompiler.Lower("programs/comptime_values.lyr", text);
        Assert.True(lowered.Ok);
        Assert.DoesNotContain(lowered.Ir!.Functions, f => f.Name == "app.main.crcTable");
        var c = Lyric5.Compiler.CEmitter.Join(CEmitterTests.EmitC("comptime_values"));
        Assert.Matches(@"static uint32_t const lyr_data\d+\[256\] = \{ 0u, 1996959894u, ", c);
        Assert.Contains("__builtin_memcpy(", c);
    }

    /// <summary>What no literal makes is refused where the site stands (09 Q1), with the reason:
    /// a class's object, a view, a function value, an interface value — also inside an array.
    /// A <c>Box</c> is the one class a site leaves.</summary>
    [Theory]
    [InlineData("class C {\n    var n: int = 0,\n}\n\nfn make(): C {\n    return C { };\n}\n", "make()", "'C' is a class, an object with an identity")]
    [InlineData("class C {\n    var n: int = 0,\n}\n\nfn make(): C[] {\n    return [C { }];\n}\n", "make()", "and this one is 'C[]': 'C' is a class")]
    [InlineData("fn view(): Slice<int> {\n    return [1, 2, 3][0..2];\n}\n", "view()", "a view shows an array or a string it does not own")]
    [InlineData("fn add(): fn(int) -> int {\n    return (x: int) => x + 1;\n}\n", "add()", "a function value holds what it captures")]
    [InlineData("fn shown(): Display {\n    return 5;\n}\n", "shown()", "an interface value holds an object of any type")]
    public void A_site_leaves_no_object_view_or_function(string declarations, string site, string why)
    {
        var errors = Errors(declarations + $"\nfn main(): void {{\n    let _ = comptime {site};\n}}\n");
        var error = Assert.Single(errors, l => l.Contains("error["));
        Assert.Contains("error[LYR-SEM0100]: a 'comptime' expression has to produce a value the compiled module can hold as a literal", error);
        Assert.Contains(why, error);
    }

    private const string Forms = Head + "let big = true;\n\nfn names(): string[] {\n    return [\"a\", \"b\", \"c\"];\n}\n\n"
        + "fn onlyWhenSmall(): string {\n    return \"small\";\n}\n\n"
        + "fn main(): void {\n"
        + "    let x = comptime {\n        let a = 3;\n        a * 2\n    };\n    println(f\"{x}\");\n"
        + "    comptime if (big) {\n        println(\"big\");\n    } else if (!big) {\n        println(\"huge\");\n    } else {\n        println(onlyWhenSmall());\n    }\n"
        + "    comptime for (n in names()) {\n        if (n == \"b\") {\n            continue;\n        }\n        println(n);\n    }\n"
        + "    var total = 0;\n"
        + "    comptime for (k in [1, 2, 3, 4]) {\n        if (k == 3) {\n            break;\n        }\n        total += k;\n    }\n    println(f\"{total}\");\n"
        + "    let s = comptime {\n        let xs = [1, 2, 3];\n        var t = 0;\n        for (v in xs) {\n            t += v;\n        }\n"
        + "        xs.iter().map((v: int) => v * t).fold(0, (a: int, b: int) => a + b)\n    };\n    println(f\"{s}\");\n}\n";

    /// <summary>The forms (09 Q2; 13 M9a S3): a block whose last expression is the value, with
    /// locals, a loop and a lambda of its own; <c>comptime if</c> with its <c>else if</c> chain,
    /// whose other branches are checked and not lowered — <c>onlyWhenSmall</c> is no part of the
    /// module; <c>comptime for</c> unrolled over an array a site computed — <c>names</c> is gone
    /// too —, a round's <c>continue</c> the next round, a <c>break</c> past the last.</summary>
    [Fact]
    public void The_forms_compute_choose_and_unroll()
    {
        var lowered = TestCompiler.Lower("main.lyr", Forms);
        Assert.True(lowered.Ok);
        Assert.DoesNotContain(lowered.Ir!.Functions, f => f.Name is "app.main.onlyWhenSmall" or "app.main.names");

        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Forms));
        Assert.Equal("6\nbig\na\nc\n3\n36\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What the forms refuse: a site uses no local of the enclosing function and assigns
    /// no module's variable; a branch <c>comptime if</c> does not take is checked all the same;
    /// <c>comptime for</c> walks an array and binds a name; <c>comptime if</c> takes no
    /// <c>let</c>. The four words are Lyric 5's (08 Y1): no name.</summary>
    [Theory]
    [InlineData("let local = 3;\n    let _ = comptime {\n        local + 1\n    };", "LYR-SEM0100", "'local' is a local of the enclosing function")]
    [InlineData("let _ = comptime {\n        counter = 5;\n        1\n    };", "LYR-SEM0100", "assigns only to what it binds itself")]
    [InlineData("comptime if (false) {\n        let wrong: int = \"s\";\n    }", "LYR-SEM0001", "cannot assign 'string' to 'int'")]
    [InlineData("comptime for (c in \"abc\") {\n    }", "LYR-SEM0100", "walks an array its site computes — this one is 'string'")]
    [InlineData("comptime for ((a, b) in [(1, 2)]) {\n    }", "LYR-SEM0100", "binds the element to a name")]
    [InlineData("comptime if (let x = maybe()) {\n    }", "LYR-SEM0100", "a 'let' condition binds a value of the run")]
    [InlineData("let inline = 3;", "LYR-PAR0020", "'inline' is a keyword")]
    [InlineData("let quote = 3;", "LYR-PAR0020", "'quote' is a keyword")]
    [InlineData("let macro = 3;", "LYR-PAR0020", "'macro' is a keyword")]
    [InlineData("let comptime = 3;", "LYR-PAR0020", "'comptime' is a keyword")]
    public void The_forms_refuse(string statement, string code, string why)
    {
        var errors = Errors("var counter = 0;\n\nfn maybe(): ?int {\n    return null;\n}\n\nfn main(): void {\n    " + statement + "\n}\n");
        Assert.Single(errors, l => l.Contains($"error[{code}]"));
        Assert.Single(errors, l => l.Contains("error["));
        Assert.Contains(why, string.Join("\n", errors));
    }

    private const string Embedding = "import std.io { println };\nimport std.core { embed };\n\n"
        + "fn table(): uint8[] {\n    return embed(\"../data/table.bin\");\n}\n\n"
        + "fn main(): void {\n    let bytes = comptime table();\n    let text = comptime embed(\"../data/hello.txt\");\n"
        + "    println(f\"{bytes.length()} {bytes[0]} {bytes[bytes.length() - 1]} {text.length()}\");\n}\n";

    /// <summary><c>embed</c> (09 Q4; 13 M9a S4, its artifact): a file of the package, relative to
    /// the module that calls, a <c>uint8[]</c> in the binary — through a function a site calls
    /// too; and the build depends on it: changed, it is read again.</summary>
    [Fact]
    public void Embed_reads_a_file_of_the_package_and_a_change_builds_anew()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Embedding),
            ("data/table.bin", "ABC"), ("data/hello.txt", "hello\n"));
        Assert.Equal("3 65 67 6\n", BuildAndRun(dir, "app", "build", "-C", dir));

        File.WriteAllText(Path.Combine(dir, "data", "table.bin"), "XYZW");
        Assert.Equal("4 88 87 6\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What <c>embed</c> refuses (<c>LYR-CT0004</c>): a call the program would make at
    /// run time; a file outside the package's directory; a file that is not there.</summary>
    [Theory]
    [InlineData("    let b = embed(\"../data/table.bin\");\n    println(f\"{b.length()}\");\n", "this call would run in the program")]
    [InlineData("    let b = comptime embed(\"../../outside.bin\");\n    println(f\"{b.length()}\");\n", "lies outside it")]
    [InlineData("    let b = comptime embed(\"../data/none.bin\");\n    println(f\"{b.length()}\");\n", "found no file '../data/none.bin'")]
    public void Embed_refuses_a_run_time_call_a_file_outside_and_none(string body, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("data/table.bin", "ABC"),
            ("src/main.lyr", "import std.io { println };\nimport std.core { embed };\n\nfn main(): void {\n" + body + "}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, error);
        var lines = error.Split('\n');
        Assert.Contains("error[LYR-CT0004]", Assert.Single(lines, l => l.Contains("error[")));
        Assert.Contains(why, error);
    }
}
