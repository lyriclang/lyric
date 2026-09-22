using Lyric.Core;

namespace Lyric.Tests.Cli;

/// <summary>
/// <c>std.build</c> with its model in Lyric: an artifact has a name, lands under its profile,
/// carries the profile's options as fields, and the script may ask the command line, pack,
/// check a library and run something after.
///
/// <para>The tests of the 4.x form (<see cref="BuildScriptTests"/>) still hold: it builds as it
/// did and warns. What is pinned here is what 4.5 added, and the seam — the runner writes an
/// entry around the script, and nothing about that may show through.</para>
/// </summary>
public class BuildV2Tests
{
    private static TemporaryDirectory Project(string buildScript, string? projectFile = null)
    {
        var dir = Toolchain.TempDirectory();

        dir.Write("build.lyr", buildScript);
        if (projectFile is not null) dir.Write("lyric.json", projectFile);

        dir.Write(Path.Combine("src", "main.lyr"), """
            import std.io.console { println };

            fn main(): int {
                println("built");
                return 7;
            }
            """);

        return dir;
    }

    private static string Out(TemporaryDirectory project, string profile, string file) =>
        Path.Combine(project.Path, "out", profile, file);

    [Fact]
    public void An_executable_lands_under_its_profile_and_the_two_profiles_sit_side_by_side()
    {
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                executable("app", "src/main.lyr");
            }
            """);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path, "--debug").ExitCode);
        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path, "--release").ExitCode);

        var debug = Path.Combine(project.Path, "out", "debug", "app.lyrbc");
        var release = Path.Combine(project.Path, "out", "release", "app.lyrbc");
        Assert.True(File.Exists(debug), $"{debug} was not written");
        Assert.True(File.Exists(release), $"{release} was not written");

        var run = Toolchain.Lyrvm("run", debug);
        Assert.Equal(7, run.ExitCode);
        Assert.Contains("built", run.Out, StringComparison.Ordinal);
        Assert.Equal(7, Toolchain.Lyrvm("run", release).ExitCode);
    }

    [Fact]
    public void A_field_set_after_the_declaration_still_applies()
    {
        // The reason nothing is compiled while the script runs, in the new spelling: the
        // field stands on the line AFTER the call that produced the artifact.
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                let stripped = executable("stripped", "src/main.lyr");
                stripped.sourceMap = false;

                executable("mapped", "src/main.lyr");
            }
            """);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path).ExitCode);

        var stripped = new FileInfo(Out(project, Toolchain.DefaultProfile, "stripped.lyrbc"));
        var mapped = new FileInfo(Out(project, Toolchain.DefaultProfile, "mapped.lyrbc"));
        Assert.True(stripped.Exists && mapped.Exists);
        Assert.True(stripped.Length < mapped.Length,
            $"stripped {stripped.Length} should be smaller than mapped {mapped.Length}");
    }

    [Fact]
    public void Use_takes_a_whole_profile_for_one_artifact_and_names_its_directory()
    {
        // A tool shipped beside a game still being debugged: the bundle, name included, so the
        // tool lands under release/ while the build as a whole is debug.
        using var project = Project("""
            import std.build { executable, Profile };

            pub fn build() {
                executable("game", "src/main.lyr");

                let tool = executable("tool", "src/main.lyr");
                tool.use(Profile.release());
            }
            """);

        // '--debug' outright: the point is the two directories, whatever the process default.
        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path, "--debug").ExitCode);

        Assert.True(File.Exists(Out(project, "debug", "game.lyrbc")));
        Assert.True(File.Exists(Out(project, "release", "tool.lyrbc")));
        Assert.False(File.Exists(Out(project, "debug", "tool.lyrbc")));
    }

    [Fact]
    public void An_explicit_output_wins_over_the_derivation()
    {
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                let app = executable("app", "src/main.lyr");
                app.output = "dist/app.lyrbc";
            }
            """);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path).ExitCode);

        Assert.True(File.Exists(Path.Combine(project.Path, "dist", "app.lyrbc")));
        Assert.False(Directory.Exists(Path.Combine(project.Path, "out")));
    }

    private const string OptionsScript = """
        import std.build { executable, option, flag };
        import std.io.file { writeText };

        pub fn build() {
            let version = option("version", "what the program returns") ?? "1";
            writeText("src/gen.lyr", "fn main(): int { return " + version + "; }");
            executable("gen", "src/gen.lyr");

            if (flag("extra", "also build the extra program")) {
                executable("extra", "src/main.lyr");
            }
        }
        """;

    [Fact]
    public void Options_and_flags_come_from_the_command_line()
    {
        using var project = Project(OptionsScript);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path).ExitCode);
        Assert.Equal(1, Toolchain.Lyrvm("run", Out(project, Toolchain.DefaultProfile, "gen.lyrbc")).ExitCode);
        Assert.False(File.Exists(Out(project, Toolchain.DefaultProfile, "extra.lyrbc")));

        Assert.Equal(ExitCodes.Success,
            Toolchain.Lyrbuild(project.Path, "-D", "version=42", "-Dextra").ExitCode);
        Assert.Equal(42, Toolchain.Lyrvm("run", Out(project, Toolchain.DefaultProfile, "gen.lyrbc")).ExitCode);
        Assert.True(File.Exists(Out(project, Toolchain.DefaultProfile, "extra.lyrbc")));
    }

    [Fact]
    public void An_option_nobody_asked_about_is_refused_with_the_declared_ones_named()
    {
        // The typo that would otherwise do nothing in silence. Checked once 'build' has
        // returned, because only then is the script's word complete.
        using var project = Project(OptionsScript);

        var build = Toolchain.Lyrbuild(project.Path, "-D", "verison=42");

        Assert.Equal(ExitCodes.Usage, build.ExitCode);
        Assert.Contains("verison", build.Err, StringComparison.Ordinal);
        Assert.Contains("version", build.Err, StringComparison.Ordinal);
        Assert.Contains("extra", build.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Out(project, Toolchain.DefaultProfile, "gen.lyrbc")));
    }

    [Fact]
    public void Help_lists_the_scripts_options_after_the_fixed_ones()
    {
        using var project = Project(OptionsScript);

        var help = Toolchain.Lyrbuild(project.Path, "--help");

        Assert.Equal(ExitCodes.Success, help.ExitCode);
        Assert.Contains("--only", help.Out, StringComparison.Ordinal);
        Assert.Contains("what the program returns", help.Out, StringComparison.Ordinal);
        Assert.Contains("also build the extra program", help.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(Out(project, Toolchain.DefaultProfile, "gen.lyrbc")), "help compiled something");
    }

    [Fact]
    public void Only_builds_the_named_artifacts_and_an_unknown_name_lists_the_known()
    {
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                executable("first", "src/main.lyr");
                executable("second", "src/main.lyr");
            }
            """);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(project.Path, "--only", "second").ExitCode);
        Assert.True(File.Exists(Out(project, Toolchain.DefaultProfile, "second.lyrbc")));
        Assert.False(File.Exists(Out(project, Toolchain.DefaultProfile, "first.lyrbc")));

        var unknown = Toolchain.Lyrbuild(project.Path, "--only", "third");
        Assert.Equal(ExitCodes.Usage, unknown.ExitCode);
        Assert.Contains(CliDiagnostics.UnknownArtifact, unknown.Err, StringComparison.Ordinal);
        Assert.Contains("first, second", unknown.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void After_runs_when_everything_was_written_and_not_otherwise()
    {
        using var succeeds = Project("""
            import std.build { executable };
            import std.io.file { writeText };

            pub fn build() {
                executable("app", "src/main.lyr");
            }

            pub fn after() {
                writeText("after.txt", "done");
            }
            """);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(succeeds.Path).ExitCode);
        Assert.Equal("done", File.ReadAllText(Path.Combine(succeeds.Path, "after.txt")));

        using var fails = Project("""
            import std.build { executable };
            import std.io.file { writeText };

            pub fn build() {
                executable("app", "src/absent.lyr");
            }

            pub fn after() {
                writeText("after.txt", "done");
            }
            """);

        var build = Toolchain.Lyrbuild(fails.Path);
        Assert.NotEqual(ExitCodes.Success, build.ExitCode);
        Assert.Contains("absent.lyr", build.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fails.Path, "after.txt")), "after ran on a failed build");
    }

    [Fact]
    public void A_packed_program_comes_out_of_the_script()
    {
        // Through the driver, where the packer and its stub lie beside the runner — the packer
        // is a PROCESS the runner starts, not a copy of it.
        using var project = Project("""
            import std.build { executable, packed };

            pub fn build() {
                let app = executable("app", "src/main.lyr");
                packed(app);
            }
            """);

        var build = Toolchain.Lyric("build", project.Path);
        Assert.Equal(ExitCodes.Success, build.ExitCode);

        var executable = Out(project, Toolchain.DefaultProfile,OperatingSystem.IsWindows() ? "app.exe" : "app");
        Assert.True(File.Exists(executable), $"{executable} was not written");
        Assert.True(File.Exists(Out(project, Toolchain.DefaultProfile, "app.lyrbc")));

        var run = Toolchain.Run(executable);
        Assert.Equal(7, run.ExitCode);
        Assert.Contains("built", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_is_checked_as_a_whole_and_nothing_is_written()
    {
        using var project = Project("""
            import std.build { library };

            pub fn build() {
                library("geometry", "lib");
            }
            """);
        project.Write(Path.Combine("lib", "geometry.lyr"), """
            module geometry;

            import geometry.shapes { area };

            pub fn twice(): int { return area() * 2; }
            """);
        project.Write(Path.Combine("lib", "geometry", "shapes.lyr"), """
            module geometry.shapes;

            pub fn area(): int { return 6; }
            """);

        var build = Toolchain.Lyrbuild(project.Path);
        Assert.Equal(ExitCodes.Success, build.ExitCode);
        Assert.Contains("geometry: 2 modules checked", build.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project.Path, "out")));

        // And a library that does not hold together fails as a compile does.
        project.Write(Path.Combine("lib", "geometry", "shapes.lyr"), """
            module geometry.shapes;

            pub fn area(): int { return "six"; }
            """);
        var broken = Toolchain.Lyrbuild(project.Path);
        Assert.NotEqual(ExitCodes.Success, broken.ExitCode);
        Assert.Contains("shapes.lyr", broken.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_script_the_main_file_builds_by_convention()
    {
        using var named = Toolchain.TempDirectory();
        named.Write("lyric.json", """{ "name": "conv", "sourceRoot": "src" }""");
        named.Write(Path.Combine("src", "main.lyr"), "fn main(): int { return 3; }");

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(named.Path, "--release").ExitCode);
        var module = Out(named, "release", "conv.lyrbc");
        Assert.True(File.Exists(module), $"{module} was not written");
        Assert.Equal(3, Toolchain.Lyrvm("run", module).ExitCode);

        // Without a lyric.json: main.lyr beside, named after the directory.
        using var bare = Toolchain.TempDirectory();
        bare.Write("main.lyr", "fn main(): int { return 4; }");

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(bare.Path).ExitCode);
        Assert.Equal(4, Toolchain.Lyrvm("run",
            Out(bare, Toolchain.DefaultProfile,$"{new DirectoryInfo(bare.Path).Name}.lyrbc")).ExitCode);
    }

    [Fact]
    public void Without_a_script_or_a_main_file_the_source_root_is_a_library()
    {
        using var project = Toolchain.TempDirectory();
        project.Write("lyric.json", """{ "name": "geometry", "sourceRoot": "src" }""");
        project.Write(Path.Combine("src", "geometry.lyr"), """
            module geometry;

            pub fn area(): int { return 6; }
            """);

        var build = Toolchain.Lyrbuild(project.Path);

        Assert.Equal(ExitCodes.Success, build.ExitCode);
        Assert.Contains("geometry: 1 module checked", build.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project.Path, "out")));
    }

    [Fact]
    public void Options_need_a_script_to_answer_them()
    {
        using var bare = Toolchain.TempDirectory();
        bare.Write("main.lyr", "fn main(): int { return 4; }");

        var build = Toolchain.Lyrbuild(bare.Path, "-D", "ship");

        Assert.Equal(ExitCodes.Usage, build.ExitCode);
        Assert.Contains("build.lyr", build.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_form_still_builds_and_warns_towards_the_new_one()
    {
        using var project = Project("""
            import std.build { addExecutable };

            pub fn build() {
                addExecutable("src/main.lyr", "out/app.lyrbc");
            }
            """);

        var build = Toolchain.Lyrbuild(project.Path);

        Assert.Equal(ExitCodes.Success, build.ExitCode);
        Assert.True(File.Exists(Path.Combine(project.Path, "out", "app.lyrbc")));
        Assert.Contains("LYR-SEM0076", build.Err, StringComparison.Ordinal);
        Assert.Contains("executable(name, entry)", build.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_is_not_an_identifier_says_which_way_round_the_arguments_go()
    {
        // The 4.x arguments in the 4.5 call, with the deprecation warning overlooked: refused
        // at the call, naming the form that was meant.
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                executable("src/main.lyr", "out/app.lyrbc");
            }
            """);

        var build = Toolchain.Lyrbuild(project.Path);

        Assert.NotEqual(ExitCodes.Success, build.ExitCode);
        Assert.Contains("is not an artifact name", build.Err, StringComparison.Ordinal);
        Assert.Contains("addExecutable", build.Err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project.Path, "out")));
    }

    [Fact]
    public void The_driver_hands_the_build_options_through()
    {
        // '--profile release' and '-D extra' both carry a value the driver must not mistake
        // for the path: a build with them still goes to the runner.
        using var project = Project(OptionsScript);

        var build = Toolchain.Lyric("build", "--profile", "release", "-D", "extra", project.Path);

        Assert.Equal(ExitCodes.Success, build.ExitCode);
        Assert.True(File.Exists(Out(project, "release", "gen.lyrbc")));
        Assert.True(File.Exists(Out(project, "release", "extra.lyrbc")));
    }

    [Fact]
    public void An_artifact_that_asks_for_no_warnings_is_refused_when_it_gets_one()
    {
        using var project = Project("""
            import std.build { executable };

            pub fn build() {
                let app = executable("app", "src/main.lyr");
                app.denyWarnings = true;
            }
            """);
        project.Write(Path.Combine("src", "main.lyr"), """
            fn main(): int {
                let unused = 1;
                return 0;
            }
            """);

        var build = Toolchain.Lyrbuild(project.Path);

        Assert.NotEqual(ExitCodes.Success, build.ExitCode);
        Assert.Contains(CliDiagnostics.WarningsDenied, build.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Out(project, Toolchain.DefaultProfile, "app.lyrbc")));
    }
}
