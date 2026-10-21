using System.Diagnostics;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric new</c>, <c>lyric init</c> and <c>lyric completions</c> (spec 14 §7): a package's first
/// files, a repository by the flag or by <c>LYRIC_GIT_ON_NEW</c>, a name refused with the one it would
/// be; the shells' scripts made from the verb table, loaded where the shell is there.
/// </summary>
[Collection("console")]
public class NewCommandTests
{
    [Fact]
    public void New_makes_a_package_that_runs()
    {
        var root = TestDirectories.Fresh("lyric5-new-");
        var (exit, output, error) = RunRaw("new", "app", "-C", root, "--no-git");
        Assert.True(exit == 0, error);
        Assert.Contains("created program package 'app'", output);
        var app = Path.Combine(root, "app");
        Assert.Equal("[package]\nname = \"app\"\nversion = \"0.1.0\"\n", File.ReadAllText(Path.Combine(app, "lyric.toml")));
        Assert.Equal("out/\n", File.ReadAllText(Path.Combine(app, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(app, ".git")));
        Assert.False(File.Exists(Path.Combine(app, "build.lyr")));
        var (built, _, buildError) = RunRaw("build", "-C", app);
        Assert.True(built == 0, buildError);
        var host = Target.Host;
        var ran = ProcessRunner.Run(Path.Combine(app, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromMinutes(1));
        Assert.Equal(0, ran.ExitCode);
        Assert.Equal("Hello, world!\n", ran.Stdout.Replace("\r\n", "\n"));

        var (again, _, refused) = RunRaw("new", "app", "-C", root);
        Assert.Equal(2, again);
        Assert.Contains("error[LYR-CLI0021]:", refused);
    }

    [Fact]
    public void A_library_and_init_where_the_directory_is()
    {
        var root = TestDirectories.Fresh("lyric5-new-");
        Assert.Equal(0, RunRaw("new", "geo", "--lib", "-C", root, "--no-git").Exit);
        Assert.True(File.Exists(Path.Combine(root, "geo", "src", "lib.lyr")));
        Assert.Equal(0, RunRaw("build", "-C", Path.Combine(root, "geo")).Exit);

        var here = Path.Combine(root, "my-tool");
        Directory.CreateDirectory(here);
        var (named, _, error) = RunRaw("init", "-C", here, "--no-git");
        Assert.Equal(2, named);
        Assert.Contains("'my-tool' is no package name", error);
        Assert.Contains("'my_tool' is one (--name names it)", error);
        Assert.Equal(0, RunRaw("init", "-C", here, "--name", "my_tool", "--no-git").Exit);
        Assert.Contains("name = \"my_tool\"", File.ReadAllText(Path.Combine(here, "lyric.toml")));
        Assert.Equal(2, RunRaw("init", "-C", here, "--name", "my_tool").Exit);
    }

    [Fact]
    public void Git_comes_with_the_flag_or_the_environment_and_never_without_either()
    {
        if (!HasGit()) return;
        var root = TestDirectories.Fresh("lyric5-new-");
        var previous = Environment.GetEnvironmentVariable("LYRIC_GIT_ON_NEW");
        try
        {
            Environment.SetEnvironmentVariable("LYRIC_GIT_ON_NEW", null);
            Assert.Equal(0, RunRaw("new", "plain", "-C", root).Exit);
            Assert.False(Directory.Exists(Path.Combine(root, "plain", ".git")));

            Assert.Equal(0, RunRaw("new", "flagged", "-C", root, "--git").Exit);
            Assert.True(Directory.Exists(Path.Combine(root, "flagged", ".git")));

            Environment.SetEnvironmentVariable("LYRIC_GIT_ON_NEW", "1");
            var (exit, output, _) = RunRaw("new", "asked", "-C", root);
            Assert.Equal(0, exit);
            Assert.Contains("initialized a git repository", output);
            Assert.True(Directory.Exists(Path.Combine(root, "asked", ".git")));
            // inside a repository the environment asks for none: the package is part of it
            Assert.Equal(0, RunRaw("new", "inner", "-C", Path.Combine(root, "asked")).Exit);
            Assert.False(Directory.Exists(Path.Combine(root, "asked", "inner", ".git")));
            // the flag over the environment
            Assert.Equal(0, RunRaw("new", "refused", "-C", root, "--no-git").Exit);
            Assert.False(Directory.Exists(Path.Combine(root, "refused", ".git")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYRIC_GIT_ON_NEW", previous);
        }
    }

    public static TheoryData<string> Shells() => ["bash", "zsh", "fish", "pwsh"];

    /// <summary>A script names every verb, and the shell that is there loads it without a word; a
    /// shell that is not installed here is left out.</summary>
    [Theory]
    [MemberData(nameof(Shells))]
    public void The_completions_name_every_verb_and_load(string shell)
    {
        var (exit, script, _) = RunRaw("completions", shell);
        Assert.Equal(0, exit);
        foreach (var verb in new[] { "build", "check", "fix", "env", "new", "init", "completions" })
            Assert.Contains(verb, script);
        Assert.Contains("deny-warnings", script);

        // each shell's own extension: pwsh dot-sources a '.ps1', and runs a file without one as a program
        var extension = shell switch { "bash" => ".sh", "zsh" => ".zsh", "fish" => ".fish", _ => ".ps1" };
        var file = Path.Combine(TestDirectories.Fresh("lyric5-completions-"), "lyric" + extension);
        File.WriteAllText(file, script);
        var (program, arguments) = shell switch
        {
            "bash" => ("bash", new[] { "-c", $"source '{file}' && complete -p lyric5" }),
            "zsh" => ("zsh", new[] { "-c", $"autoload -U compinit && compinit -u && source '{file}'" }),
            "fish" => ("fish", new[] { "-c", $"source '{file}'" }),
            _ => ("pwsh", new[] { "-NoProfile", "-Command", $". '{file}'" }),
        };
        if (OperatingSystem.IsWindows() && shell != "pwsh") return;
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
        }
        catch (System.ComponentModel.Win32Exception) { return; }
        using var started = process!;
        var stderr = started.StandardError.ReadToEnd();
        started.StandardOutput.ReadToEnd();
        Assert.True(started.WaitForExit(60_000), $"{shell} did not end");
        Assert.True(started.ExitCode == 0, $"{shell}: {stderr}");
        Assert.Equal("", stderr.Trim());
    }

    [Fact]
    public void Completions_name_a_shell()
    {
        Assert.Equal(2, RunRaw("completions", "tcsh").Exit);
        Assert.Equal(2, RunRaw("completions").Exit);
    }

    private static bool HasGit()
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            git!.WaitForExit();
            return git.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
