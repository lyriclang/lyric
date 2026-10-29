using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;
using static Lyric5.Tests.ToolchainFixture;

namespace Lyric5.Tests;

/// <summary>
/// Which toolchain runs a command (spec 14 §13, 15 §1 rule 6): <c>LYRIC_TOOLCHAIN</c> over the
/// package's pin; this toolchain where it is what is asked for, else the default, else the newest
/// installed that is; at a terminal, the offer of the least stable version that is. And the command
/// handed on: to a stand-in toolchain built here, its arguments and its exit passed through, never
/// handed on twice.
/// </summary>
[Collection("console")]
public class ToolchainSwitchTests
{
    private const string Running = "5.0.0";
    private const string Dev = "6.0.0-dev.20261102+abcdef0";

    /// <summary>A home with <paramref name="versions"/> installed — a directory with a manifest
    /// each — and <paramref name="chosen"/> the default.</summary>
    private static ToolchainHome Home(string? chosen, params string[] versions)
    {
        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        foreach (var version in versions)
        {
            Directory.CreateDirectory(home.DirectoryOf(version));
            File.WriteAllText(Path.Combine(home.DirectoryOf(version), ToolchainManifest.FileName), "{}\n");
        }
        if (chosen is not null) File.WriteAllText(home.Config, $"default = \"{chosen}\"\n");
        return home;
    }

    private static string Pinned(string? pin) =>
        Path.Combine(Package(("lyric.toml", "[package]\nname = \"app\"\nversion = \"0.1.0\"\n" + (pin is null ? "" : $"toolchain = \"{pin}\"\n")),
            ("src/main.lyr", "fn main(): void {\n}\n")), "lyric.toml");

    private static SwitchDecision Decide(ToolchainHome home, string? manifest, string running = Running, params (string Name, string Value)[] environment) =>
        ToolchainSwitch.Decide(running, name => environment.FirstOrDefault(e => e.Name == name).Value, home, manifest);

    private static string? Chosen(SwitchDecision decision) => decision is SwitchDecision.Installed installed ? installed.Version : null;

    [Fact]
    public void The_pin_is_met_here_else_by_the_default_else_by_the_newest_installed()
    {
        var home = Home("5.2.0", "5.1.0", "5.2.0", Dev);
        Assert.IsType<SwitchDecision.Here>(Decide(home, null));
        Assert.IsType<SwitchDecision.Here>(Decide(home, Pinned(null)));
        Assert.IsType<SwitchDecision.Here>(Decide(home, Pinned("5.0")));
        Assert.IsType<SwitchDecision.Here>(Decide(home, Pinned("5.0"), "5.0.0-dev.20261028+0b3eb5e"));

        var byDefault = Decide(home, Pinned("5.1"));
        Assert.Equal("5.2.0", Chosen(byDefault));
        Assert.Equal("package 'app' needs lyric 5.1.0 or later", ((SwitchDecision.Installed)byDefault).Why);
        // a development build of 6.0.0 meets a pin of 5.3, as the newest installed
        Assert.Equal(Dev, Chosen(Decide(home, Pinned("5.3"))));
        Assert.Equal(Dev, Chosen(Decide(Home(null, "5.1.0", Dev), Pinned("5.1"))));
        Assert.Equal("5.2.0", Chosen(Decide(Home("9.9.9", "5.1.0", "5.2.0"), Pinned("5.2"))));

        var missing = Assert.IsType<SwitchDecision.Missing>(Decide(home, Pinned("7.0")));
        Assert.Equal(new SemVer(7, 0, 0, null), missing.Minimum);
        Assert.Equal("app", missing.Package);

        // a manifest that does not read is the verb's to refuse
        var broken = Path.Combine(Package(("lyric.toml", "[package\nname = \"app\"\n")), "lyric.toml");
        Assert.IsType<SwitchDecision.Here>(Decide(home, broken));
    }

    [Fact]
    public void The_variable_names_a_toolchain_and_a_handed_command_is_not_handed_again()
    {
        var home = Home("5.2.0", "5.1.0", "5.2.0", Dev);
        var pinned = Pinned("7.0");
        Assert.IsType<SwitchDecision.Here>(Decide(home, pinned, Running, ("LYRIC_TOOLCHAIN", "local")));
        Assert.IsType<SwitchDecision.Here>(Decide(home, pinned, Running, ("LYRIC_TOOLCHAIN", Running)));
        Assert.IsType<SwitchDecision.Here>(Decide(home, pinned, Running, ("LYRIC_TOOLCHAIN_FROM", "5.2.0")));
        Assert.Equal("5.1.0", Chosen(Decide(home, pinned, Running, ("LYRIC_TOOLCHAIN", "5.1.0"))));
        Assert.Equal(Dev, Chosen(Decide(home, null, Running, ("LYRIC_TOOLCHAIN", ToolchainHome.WithoutBuild(Dev)))));
        Assert.Equal("LYRIC_TOOLCHAIN=5.1.0", ((SwitchDecision.Installed)Decide(home, null, Running, ("LYRIC_TOOLCHAIN", "5.1.0"))).Why);

        var refused = Assert.Throws<ToolchainException>(() => Decide(home, null, Running, ("LYRIC_TOOLCHAIN", "5.1")));
        Assert.Equal("LYR-CLI0025", refused.Code);
        Assert.Contains("LYRIC_TOOLCHAIN names lyric 5.1, which is not installed", refused.Message);
    }

    [Fact]
    public void A_terminal_is_offered_the_least_stable_version_that_meets_the_pin()
    {
        using var server = new MirrorServer();
        var home = ToolchainFixture.Home(server);
        var fetch = ToolchainFetch.ForUser(Running, null);
        SwitchDecision.Missing Needs(long major, long minor) => new(new SemVer(major, minor, 0, null), "app", "lyric.toml", 4);

        var prompt = new StringWriter();
        Assert.Null(ToolchainSwitch.Offer(fetch, home, Needs(8, 0), Running, prompt, () => "n"));
        Assert.Equal("package 'app' needs lyric 8.0.0 or later, and this is lyric 5.0.0; install lyric 8.0.0? [y/N] ", prompt.ToString());
        Assert.Empty(home.Installed());

        var none = new StringWriter();
        Assert.Null(ToolchainSwitch.Offer(fetch, home, Needs(8, 1), Running, none, () => throw new InvalidOperationException("not asked")));
        Assert.Equal("", none.ToString());

        Assert.Equal("8.0.0", ToolchainSwitch.Offer(fetch, home, Needs(7, 2), Running, new StringWriter(), () => "y"));
        Assert.Equal(new[] { "8.0.0" }, home.Installed());
    }

    [Fact]
    public void A_command_is_handed_to_the_toolchain_the_pin_asks_for_with_its_arguments_and_its_exit()
    {
        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        var stand = home.DirectoryOf("7.1.0");
        Directory.CreateDirectory(stand);
        File.WriteAllText(Path.Combine(stand, ToolchainManifest.FileName), "{}\n");
        // a stand-in toolchain: says its arguments and who handed the command on, and exits with 3
        var source = Path.Combine(TestDirectories.Fresh("lyric5-stand-in-"), "stand_in.c");
        File.WriteAllText(source,
            "#include <stdio.h>\n#include <stdlib.h>\n\nint main(int argc, char **argv) {\n"
            + "    for (int i = 1; i < argc; i++) printf(\"%s%s\", i > 1 ? \" \" : \"\", argv[i]);\n"
            + "    const char *from = getenv(\"LYRIC_TOOLCHAIN_FROM\");\n"
            + "    printf(\"\\nfrom %s\\n\", from ? from : \"(none)\");\n    return 3;\n}\n");
        var compiler = CCompiler.Locate() ?? throw new InvalidOperationException("no C compiler");
        string[] compile = [source, "-o", home.BinaryOf("7.1.0")];
        var built = ProcessRunner.Run(compiler.Path, compiler.Kind == CCompilerKind.Zig ? ["cc", .. compile] : compile, TimeSpan.FromMinutes(10));
        Assert.True(built.ExitCode == 0, built.Stderr);

        var package = Path.GetDirectoryName(Pinned("7.0"))!;
        (int Exit, string Out, string Err) Lyric(params (string Name, string Value)[] environment)
        {
            var variables = new Dictionary<string, string> { ["LYRIC_HOME"] = home.Root, ["LYRIC_TOOLCHAIN"] = "", ["LYRIC_TOOLCHAIN_FROM"] = "" };
            foreach (var (name, value) in environment) variables[name] = value;
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var ran = ProcessRunner.Run(host, [Path.Combine(AppContext.BaseDirectory, "lyric5.dll"), "--color", "never", "build", "-C", package],
                TimeSpan.FromMinutes(2), environment: variables);
            return (ran.ExitCode, ran.Stdout.Replace("\r\n", "\n"), ran.Stderr);
        }

        var (exit, output, error) = Lyric();
        Assert.True(exit == 3, $"{exit}: {output}{error}");
        Assert.StartsWith($"--color never build -C {package}\nfrom ", output);
        Assert.DoesNotContain("from (none)", output);

        // a command handed on once is not handed on again, and 'local' runs this toolchain: both refuse the pin
        foreach (var environment in new[] { ("LYRIC_TOOLCHAIN_FROM", "5.0.0"), ("LYRIC_TOOLCHAIN", "local") })
        {
            var (here, _, refused) = Lyric(environment);
            Assert.True(here == 2, refused);
            Assert.Contains("error[LYR-PKG0009]: package 'app' needs lyric 7.0.0 or later", refused);
        }
    }
}
