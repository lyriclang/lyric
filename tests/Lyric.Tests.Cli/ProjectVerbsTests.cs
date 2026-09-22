using Lyric.Core;

namespace Lyric.Tests.Cli;

/// <summary>
/// The verbs without a file argument: in a project, <c>run</c>, <c>pack</c>, <c>check</c> and
/// <c>test</c> answer for the project rather than for a file somebody had to name.
///
/// <para>Every one of them runs with the project as the WORKING DIRECTORY, because that is how
/// they are used and it is the only way to see that the default is the working directory rather
/// than the repository the suite happens to run in.</para>
/// </summary>
public class ProjectVerbsTests
{
    private static ToolResult In(TemporaryDirectory project, params string[] args) =>
        Toolchain.RunIn(project.Path, Toolchain.LyricPath, args);

    /// <summary>A project of two programs, so "the default" is a decision and not the only
    /// candidate.</summary>
    private static TemporaryDirectory TwoPrograms()
    {
        var dir = Toolchain.TempDirectory();

        dir.Write("lyric.json", """{ "name": "game", "sourceRoot": "src" }""");
        dir.Write("build.lyr", """
            import std.build { executable };

            pub fn build() {
                executable("game", "src/main.lyr");
                executable("mktex", "tools/mktex.lyr");
            }
            """);
        dir.Write(Path.Combine("src", "main.lyr"), """
            import std.io.console { println };

            fn main(): int {
                println("the game");
                return 3;
            }
            """);
        dir.Write(Path.Combine("tools", "mktex.lyr"), """
            import std.io.console { println };

            fn main(): int {
                println("the tool");
                return 4;
            }
            """);

        return dir;
    }

    [Fact]
    public void Run_without_a_file_builds_the_default_artifact_and_runs_it()
    {
        // The FIRST declared executable, explicitly — not a rule about names.
        using var project = TwoPrograms();

        var run = In(project, "run");

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("the game", run.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.Path, "out",
            Toolchain.DefaultProfile, "game.lyrbc")));
    }

    [Fact]
    public void Run_with_a_name_runs_that_artifact()
    {
        using var project = TwoPrograms();

        var run = In(project, "run", "mktex");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("the tool", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_nobody_declared_is_refused_with_the_declared_ones_named()
    {
        using var project = TwoPrograms();

        var run = In(project, "run", "mktesx");

        Assert.NotEqual(ExitCodes.Success, run.ExitCode);
        Assert.Contains(CliDiagnostics.UnknownArtifact, run.Err, StringComparison.Ordinal);
        Assert.Contains("game, mktex", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_of_a_project_run_says_nothing_on_stdout()
    {
        // What the driver reads from the runner is ONE path on stdout; what the program prints
        // is the program's. A build line landing in the program's output would be a build line
        // in a pipeline's data.
        using var project = TwoPrograms();

        var run = In(project, "run");

        Assert.Equal("the game\n", run.Out);
        Assert.Contains("game.lyrbc", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_after_the_separator_reach_the_program()
    {
        using var project = TwoPrograms();
        project.Write(Path.Combine("src", "main.lyr"), """
            import std.io.console { println };

            fn main(args: string[]): int {
                println(args[0]);
                return 0;
            }
            """);

        var run = In(project, "run", "--", "hello");

        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Contains("hello", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void The_profile_travels_to_the_build_and_decides_where_it_lands()
    {
        using var project = TwoPrograms();

        var run = In(project, "run", "--release");

        Assert.Equal(3, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(project.Path, "out", "release", "game.lyrbc")));

        // And the value behind '--profile' is not mistaken for an artifact name.
        Assert.Equal(3, In(project, "run", "--profile", "release").ExitCode);
    }

    [Fact]
    public void A_library_project_has_nothing_to_run_and_says_so()
    {
        using var project = Toolchain.TempDirectory();
        project.Write("lyric.json", """{ "name": "geometry", "sourceRoot": "src" }""");
        project.Write(Path.Combine("src", "geometry.lyr"), """
            module geometry;

            pub fn area(): int { return 6; }
            """);

        var run = In(project, "run");

        Assert.NotEqual(ExitCodes.Success, run.ExitCode);
        Assert.Contains(CliDiagnostics.UnknownArtifact, run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_without_a_file_packs_the_default_artifact_in_the_release_profile()
    {
        using var project = TwoPrograms();

        var pack = In(project, "pack");
        Assert.Equal(ExitCodes.Success, pack.ExitCode);

        var executable = Path.Combine(project.Path, "out", "release",
            OperatingSystem.IsWindows() ? "game.exe" : "game");
        Assert.True(File.Exists(executable), $"{executable} was not written");

        var run = Toolchain.Run(executable);
        Assert.Equal(3, run.ExitCode);
        Assert.Contains("the game", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_with_a_name_packs_that_one()
    {
        using var project = TwoPrograms();

        var output = Path.Combine(project.Path,
            OperatingSystem.IsWindows() ? "tex.exe" : "tex");
        Assert.Equal(ExitCodes.Success, In(project, "pack", "mktex", "-o", output).ExitCode);

        Assert.Equal(4, Toolchain.Run(output).ExitCode);
    }

    [Fact]
    public void Check_without_a_file_checks_the_whole_project()
    {
        using var project = TwoPrograms();
        project.Write(Path.Combine("tests", "game_tests.lyr"), """
            import std.test { Test, assertEq };

            @Test
            pub fn two_is_two(): void { assertEq(2, 2); }
            """);

        var check = In(project, "check");

        Assert.Equal(ExitCodes.Success, check.ExitCode);
        // src/main.lyr and the one test; tools/ is not under the source root.
        Assert.Contains("2 modules ok", check.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_check_reads_the_files_TOGETHER()
    {
        // The question a per-file check cannot answer: the test imports a function the source
        // root no longer has. Checking each file on its own would compile the source root
        // happily and never open the test.
        using var project = Toolchain.TempDirectory();
        project.Write("lyric.json", """{ "name": "mathx", "sourceRoot": "src" }""");
        project.Write(Path.Combine("src", "mathx.lyr"), """
            module mathx;

            pub fn double(n: int): int { return n * 2; }
            """);
        project.Write(Path.Combine("tests", "mathx_tests.lyr"), """
            import std.test { Test, assertEq };
            import mathx { triple };

            @Test
            pub fn triples(): void { assertEq(triple(2), 6); }
            """);

        var check = In(project, "check");

        Assert.NotEqual(ExitCodes.Success, check.ExitCode);
        Assert.Contains("triple", check.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_check_takes_a_directory_too()
    {
        using var project = TwoPrograms();

        var check = Toolchain.Lyric("check", project.Path);

        Assert.Equal(ExitCodes.Success, check.ExitCode);
        Assert.Contains("1 module ok", check.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_has_no_one_module_to_emit()
    {
        // '--emit' is about the bytes of ONE module. Accepted and ignored is how a flag costs
        // an afternoon.
        using var project = TwoPrograms();

        var check = In(project, "check", "--emit");

        Assert.Equal(ExitCodes.Usage, check.ExitCode);
        Assert.Contains("--emit", check.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_still_goes_to_the_compiler()
    {
        // The rule that decides: an argument that is on disk or carries a Lyric extension is a
        // file, everything else is a project or an artifact.
        using var project = TwoPrograms();

        Assert.Equal(ExitCodes.Success,
            In(project, "check", Path.Combine("src", "main.lyr")).ExitCode);
        Assert.Equal(3, In(project, "run", Path.Combine("src", "main.lyr")).ExitCode);
    }

    [Fact]
    public void The_test_runner_takes_a_filter_and_a_filter_matching_nothing_is_an_error()
    {
        using var project = TwoPrograms();
        project.Write(Path.Combine("tests", "game_tests.lyr"), """
            import std.test { Test, assertEq };

            @Test
            pub fn one_is_one(): void { assertEq(1, 1); }

            @Test
            pub fn two_is_two(): void { assertEq(2, 2); }
            """);

        var one = In(project, "test", "--filter", "one_is");
        Assert.Equal(ExitCodes.Success, one.ExitCode);
        Assert.Contains("1 test(s), all passed", one.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("two_is_two", one.Out, StringComparison.Ordinal);

        var none = In(project, "test", "--filter", "three");
        Assert.NotEqual(ExitCodes.Success, none.ExitCode);
        Assert.Contains("no test matches 'three'", none.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_project_runs_and_tests_without_anything_being_named()
    {
        // The scaffold end to end, in the shape the first five minutes with Lyric have: new,
        // run, test — no file named at any point.
        using var workspace = Toolchain.TempDirectory();
        Assert.Equal(ExitCodes.Success,
            Toolchain.RunIn(workspace.Path, Toolchain.LyricPath, "new", "demo").ExitCode);

        var project = Path.Combine(workspace.Path, "demo");

        var run = Toolchain.RunIn(project, Toolchain.LyricPath, "run");
        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Contains("Hello, Lyric!", run.Out, StringComparison.Ordinal);

        var test = Toolchain.RunIn(project, Toolchain.LyricPath, "test");
        Assert.Equal(ExitCodes.Success, test.ExitCode);
        Assert.Contains("1 test(s), all passed", test.Out, StringComparison.Ordinal);

        var check = Toolchain.RunIn(project, Toolchain.LyricPath, "check");
        Assert.Equal(ExitCodes.Success, check.ExitCode);
    }

    [Fact]
    public void A_new_library_is_tested_the_same_way()
    {
        using var workspace = Toolchain.TempDirectory();
        Assert.Equal(ExitCodes.Success,
            Toolchain.RunIn(workspace.Path, Toolchain.LyricPath, "new", "greeter", "--lib").ExitCode);

        var project = Path.Combine(workspace.Path, "greeter");

        var test = Toolchain.RunIn(project, Toolchain.LyricPath, "test");
        Assert.Equal(ExitCodes.Success, test.ExitCode);
        Assert.Contains("1 test(s), all passed", test.Out, StringComparison.Ordinal);

        // And a build of it is a check: a library has no artifact to write.
        var build = Toolchain.RunIn(project, Toolchain.LyricPath, "build");
        Assert.Equal(ExitCodes.Success, build.ExitCode);
        Assert.Contains("greeter: 1 module checked", build.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project, "out")));
    }
}
