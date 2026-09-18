using System.Text.Json;
using Lyric.Core;

namespace Lyric.Tests.Cli;

/// <summary>
/// The two profiles at the command line: what a compile is when nobody says, what each flag
/// changes, and that the tools refuse an option they do not know instead of overlooking it.
///
/// <para>Every assertion here reads the RESULT — the sections a module carries, the bytes, the
/// backtrace — rather than the compiler's own report of what it did, because a flag that is
/// accepted and does nothing is exactly the failure these tests exist to catch.</para>
/// </summary>
public sealed class ProfileTests
{
    private const string Program = """
        fn step(a: int): int {
            return a * 3 + 1;
        }

        fn main(): int {
            var acc = 0;
            var i = 0;
            while (i < 10) {
                acc = step(acc) & 255;
                i = i + 1;
            }
            return if (acc > 0) 0 else 1;
        }
        """;

    /// <summary>The suite itself may run under <c>LYRIC_PROFILE=release</c> — that is what the
    /// variable is for, and the tools inherit it — so a test about the DEFAULT pins the rule
    /// rather than the value.</summary>
    private static bool DefaultIsRelease =>
        Environment.GetEnvironmentVariable("LYRIC_PROFILE") == "release";

    private static (bool SourceMap, bool DebugInfo) Sections(string module)
    {
        var info = Toolchain.Lyrvm("info", module, "--json");
        Assert.Equal(0, info.ExitCode);
        using var document = JsonDocument.Parse(info.Out);
        var root = document.RootElement;
        return (root.GetProperty("sourceMap").GetBoolean(), root.GetProperty("debugInfo").GetBoolean());
    }

    private static string Build(TemporaryFile source, TemporaryFile output, params string[] flags)
    {
        var result = Toolchain.Lyrc(["build", source.Path, "-o", output.Path, .. flags]);
        Assert.Equal(0, result.ExitCode);
        return output.Path;
    }

    [Fact]
    public void A_build_is_the_debug_profile_and_release_drops_the_slot_names()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var debug = Toolchain.Temp(".lyrbc");
        using var release = Toolchain.Temp(".lyrbc");
        using var unnamed = Toolchain.Temp(".lyrbc");

        Build(source, debug, "--debug");
        Build(source, release, "--release");
        Build(source, unnamed);

        // Both keep the line numbers; only the release profile drops the names.
        Assert.Equal((true, true), Sections(debug.Path));
        Assert.Equal((true, false), Sections(release.Path));

        // A build that names no profile is the debug one — unless the environment says release.
        Assert.Equal(DefaultIsRelease ? (true, false) : (true, true), Sections(unnamed.Path));

        // And both answer the same: the profile changes the shape, never the program.
        Assert.Equal(Toolchain.Lyrvm("run", debug.Path).ExitCode,
            Toolchain.Lyrvm("run", release.Path).ExitCode);
    }

    [Fact]
    public void The_profile_flag_and_its_short_form_are_one_bundle()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var release = Toolchain.Temp(".lyrbc");
        using var releaseNamed = Toolchain.Temp(".lyrbc");
        using var debug = Toolchain.Temp(".lyrbc");
        using var debugNamed = Toolchain.Temp(".lyrbc");
        using var unnamed = Toolchain.Temp(".lyrbc");

        Build(source, release, "--release");
        Build(source, releaseNamed, "--profile", "release");
        Build(source, debug, "--debug");
        Build(source, debugNamed, "--profile", "debug");
        Build(source, unnamed);

        Assert.Equal(File.ReadAllBytes(release.Path), File.ReadAllBytes(releaseNamed.Path));
        Assert.Equal(File.ReadAllBytes(debug.Path), File.ReadAllBytes(debugNamed.Path));
        Assert.Equal(File.ReadAllBytes(DefaultIsRelease ? release.Path : debug.Path),
            File.ReadAllBytes(unnamed.Path));

        // The two profiles are not the same file, or the assertions above would be vacuous.
        Assert.NotEqual(File.ReadAllBytes(release.Path), File.ReadAllBytes(debug.Path));
    }

    [Fact]
    public void A_field_flag_wins_over_the_profile()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var named = Toolchain.Temp(".lyrbc");
        using var unmapped = Toolchain.Temp(".lyrbc");

        Build(source, named, "--release", "--debug-info");
        Build(source, unmapped, "--debug", "--no-source-map");

        Assert.Equal((true, true), Sections(named.Path));
        Assert.Equal((false, true), Sections(unmapped.Path));
    }

    [Fact]
    public void The_environment_names_the_default_and_a_flag_beats_it()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var fromEnvironment = Toolchain.Temp(".lyrbc");
        using var fromFlag = Toolchain.Temp(".lyrbc");

        var release = new Dictionary<string, string?> { ["LYRIC_PROFILE"] = "release" };
        Assert.Equal(0, Toolchain.RunWithEnvironment(Toolchain.LyrcPath, release,
            "build", source.Path, "-o", fromEnvironment.Path).ExitCode);
        Assert.Equal(0, Toolchain.RunWithEnvironment(Toolchain.LyrcPath, release,
            "build", source.Path, "-o", fromFlag.Path, "--debug").ExitCode);

        Assert.Equal((true, false), Sections(fromEnvironment.Path));
        Assert.Equal((true, true), Sections(fromFlag.Path));
    }

    [Fact]
    public void An_option_nobody_knows_is_refused_by_name()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var module = Toolchain.Temp(".lyrbc");
        Build(source, module);

        var compiler = Toolchain.Lyrc("build", source.Path, "--optimise");
        Assert.Equal(ExitCodes.Usage, compiler.ExitCode);
        Assert.Contains("LYR-CLI0003", compiler.Err);
        Assert.Contains("unknown option '--optimise'", compiler.Err);

        var runtime = Toolchain.Lyrvm("run", module.Path, "--jitt");
        Assert.Equal(ExitCodes.Usage, runtime.ExitCode);
        Assert.Contains("unknown option '--jitt'", runtime.Err);

        // An option that belongs to another command is refused with the reason, not overlooked.
        var misplaced = Toolchain.Lyrc("check", source.Path, "-o", module.Path);
        Assert.Equal(ExitCodes.Usage, misplaced.ExitCode);
        Assert.Contains("only 'build' writes a file", misplaced.Err);

        var unknownProfile = Toolchain.Lyrc("build", source.Path, "--profile", "fast");
        Assert.Equal(ExitCodes.Usage, unknownProfile.ExitCode);
        Assert.Contains("unknown profile 'fast'", unknownProfile.Err);
    }

    [Fact]
    public void The_diagnostic_switches_take_one_pass_out_and_change_no_answer()
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, Program);
        using var fused = Toolchain.Temp(".lyrbc");
        using var unfused = Toolchain.Temp(".lyrbc");
        using var bare = Toolchain.Temp(".lyrbc");

        Build(source, fused, "--release");
        Build(source, unfused, "--release", "--no-fusion");
        Build(source, bare, "--release", "--no-inline", "--no-scalar-replacement",
            "--no-devirtualize", "--no-fusion");

        // The fused forms are in the default encoding and gone without it — the control that
        // makes the switch's silence meaningful.
        var withFusion = Toolchain.Lyrvm("disasm", fused.Path).Out;
        var withoutFusion = Toolchain.Lyrvm("disasm", unfused.Path).Out;
        Assert.Contains("brcmpk", withFusion);
        Assert.DoesNotContain("brcmp", withoutFusion);
        Assert.DoesNotContain("binl", withoutFusion);

        // Without the inliner the callee survives as its own function; with it, the call is gone.
        Assert.Contains("fn main.step", Toolchain.Lyrvm("disasm", bare.Path).Out);
        Assert.DoesNotContain("call main.step", withFusion);

        foreach (var module in new[] { fused, unfused, bare })
            Assert.Equal(0, Toolchain.Lyrvm("run", module.Path).ExitCode);
    }

    [Fact]
    public void The_driver_hands_each_option_to_the_tool_that_takes_it()
    {
        var hello = Toolchain.Example("hello.lyr");

        // '--jit' is the runtime's, '--release' the compiler's; both reach their tool through one
        // 'run' and neither refuses the other's flag.
        var compiled = Toolchain.Lyric("run", hello, "--release", "--jit");
        Assert.Equal(0, compiled.ExitCode);
        Assert.Contains("Hello, Lyric!", compiled.Out);

        // An option neither tool knows is refused by the compiler, which sees it first.
        var bogus = Toolchain.Lyric("run", hello, "--bogus");
        Assert.Equal(ExitCodes.Usage, bogus.ExitCode);
        Assert.Contains("unknown option '--bogus'", bogus.Err);
    }

    [Fact]
    public void Pack_is_the_release_profile_unless_told_otherwise()
    {
        using var directory = Toolchain.TempDirectory();
        directory.Write("app.lyr", Program);
        var source = Path.Combine(directory.Path, "app.lyr");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var shipped = Path.Combine(directory.Path, "shipped" + suffix);
        var debugged = Path.Combine(directory.Path, "debugged" + suffix);

        Assert.Equal(0, Toolchain.Lyric("pack", source, "-o", shipped).ExitCode);
        Assert.Equal(0, Toolchain.Lyric("pack", source, "-o", debugged, "--debug").ExitCode);

        // The slot names are the difference between the two profiles, and they are bytes: the
        // release pack is the smaller file, by exactly the sections it does not carry.
        Assert.True(new FileInfo(shipped).Length < new FileInfo(debugged).Length,
            $"release pack {new FileInfo(shipped).Length} should be smaller than debug pack "
            + $"{new FileInfo(debugged).Length}");
    }

    [Fact]
    public void The_test_runner_takes_a_profile_too()
    {
        using var project = Toolchain.TempDirectory();
        project.Write("lyric.json", "{ \"sourceRoot\": \"src\" }\n");
        project.Write(Path.Combine("src", "mathx.lyr"),
            "module mathx;\n\npub fn triple(n: int): int {\n    return n * 3;\n}\n");
        project.Write(Path.Combine("tests", "math_tests.lyr"),
            "import std.test { Test, assertEq };\nimport mathx { triple };\n\n"
            + "@Test\npub fn triples(): void {\n    assertEq(triple(2), 6);\n}\n");

        foreach (var flags in new[] { Array.Empty<string>(), ["--release"], ["--profile", "debug"] })
        {
            var result = Toolchain.Lyrtest([project.Path, .. flags]);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("1 test(s), all passed", result.Out);
        }

        Assert.Equal(ExitCodes.Usage, Toolchain.Lyrtest(project.Path, "--profile", "fast").ExitCode);
    }
}
