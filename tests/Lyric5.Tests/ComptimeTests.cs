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
        Assert.Equal("12 cases, 0 differ\n", result.Stdout.Replace("\r\n", "\n"));
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
        foreach (var name in new[] { "tower", "checked", "bits", "divisions", "floats", "formats", "parsing", "strings", "structures", "debugs" })
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
        Assert.Contains(errors, l => l.Contains("error[LYR-CT0003]: ") && l.Contains("a file") && l.Contains("(09 Q3)"));
        Assert.Contains(errors, l => l.Contains("note: in 'app.main.read'"));
    }

    /// <summary>What compile time cannot answer the same on every platform yet is refused, not
    /// guessed (09 Q7): a function each C library rounds its own way, a Unicode answer beyond
    /// ASCII. The exactly rounded functions and ASCII's answers are computed.</summary>
    [Theory]
    [InlineData("1.0.exp()", "'exp' rounds as each C library does")]
    [InlineData("2.0.pow(0.5)", "'pow' rounds as each C library does")]
    [InlineData("'É'.isUpper()", "'std.core.unicodeCategory' asks the Unicode tables beyond ASCII")]
    public void What_compile_time_cannot_answer_alike_is_refused(string site, string message)
    {
        var errors = Errors(Head + $"fn main(): void {{\n    println(f\"{{comptime 2.0.sqrt()}} {{comptime 'E'.isUpper()}} {{comptime {site}}}\");\n}}\n");
        var error = Assert.Single(errors, l => l.Contains("error["));
        Assert.Contains("error[LYR-CT0005]: " + message, error);
    }
}
