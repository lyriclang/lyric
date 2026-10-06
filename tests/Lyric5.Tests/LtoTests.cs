using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The profile's <c>lto</c> field on every Tier 1 target (design/v5/spec/01 B11; the review's
/// R9b): ThinLTO at compile time, no LTO flag on the link line, none for macOS. Before, a build
/// for Windows failed at the link (zig built its own mingw libc as bitcode) and one for macOS at
/// compile time ("LTO requires using LLD"). Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class LtoTests
{
    private const string Manifest = "[package]\nname = \"hello\"\nversion = \"0.1.0\"\n";

    private const string Main = "import std.io { println };\n\nfn main(): void {\n    let xs = [3, 1, 2];\n    println(f\"hello {xs[0] * 14}\");\n}\n";

    public static TheoryData<string> Targets()
    {
        var data = new TheoryData<string>();
        foreach (var target in Target.Tier1) data.Add(target.Triple);
        return data;
    }

    private static string Built(string dir, Target target)
    {
        var (exit, _, error) = Run("build", "-C", dir, "--profile", "release", "--lto", "--target", target.Triple);
        Assert.True(exit == 0, error);
        return Path.Combine(dir, "out", "release", target.Triple, "hello" + target.ExecutableSuffix);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void An_lto_build_links_for_every_tier_1_target(string triple)
    {
        var exe = Built(Package(("lyric.toml", Manifest), ("src/main.lyr", Main)), Target.Parse(triple));
        Assert.True(File.Exists(exe), $"no binary {exe}");
    }

    /// <summary>On the host the LTO build runs, and says what the plain one says.</summary>
    [Fact]
    public void An_lto_build_runs_on_the_host()
    {
        var exe = Built(Package(("lyric.toml", Manifest), ("src/main.lyr", Main)), Target.Host);
        var ran = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("hello 42\n", ran.Stdout.Replace("\r\n", "\n"));
    }
}
