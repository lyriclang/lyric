using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// M7's artifacts (design/v5/spec/13): the multi-package example — <c>programs/three_packages</c>, a
/// path dependency and one from git — builds offline once the cache holds it, and builds for the
/// other operating system; a dependency's globals are ready before the importer's (07 G2).
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ExampleTests
{
    private static string Examples([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "programs", "three_packages"));

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private const string Report = "bed: 120000 cm2\npath: 40000 cm2\ntotal: 160000 cm2\n";

    /// <summary>As the example's README says: <c>units</c> made a repository, added by git, built
    /// online once — then offline with the repository gone.</summary>
    [Fact]
    public void The_example_of_three_packages_builds_offline()
    {
        var dir = Package(("x", ""));
        Copy(Examples(), dir);
        using var units = new TestRepo();
        var commit = units.Commit(
            ("lyric.toml", File.ReadAllText(Path.Combine(dir, "units", "lyric.toml"))),
            ("src/convert.lyr", File.ReadAllText(Path.Combine(dir, "units", "src", "convert.lyr"))));
        units.Tag("v1.0.0");
        var app = Path.Combine(dir, "app");

        var (added, _, why) = Run("add", "units", "--git", units.Url, "--tag", "v1.0.0", "-C", app);
        Assert.True(added == 0, why);
        Assert.Equal(commit, Assert.Single(LockFile.Read(Path.Combine(app, "lyric.lock"))).Commit);
        Assert.Equal(Report, BuildAndRun(app, "app", "build", "-C", app));

        TestRepo.DeleteTree(units.Dir);
        TestRepo.DeleteTree(Path.Combine(app, "out"));
        Assert.Equal(Report, BuildAndRun(app, "app", "build", "-C", app, "--offline"));

        // The other system's binary, from the same cache: a PE for Windows, an ELF for Linux.
        var other = Target.Host.Os == TargetOs.Windows ? Target.Parse("x86_64-linux-gnu") : Target.Parse("x86_64-windows-gnu");
        var (crossed, _, error) = Run("build", "-C", app, "--offline", "--target", other.Triple);
        Assert.True(crossed == 0, error);
        var binary = File.ReadAllBytes(Path.Combine(app, "out", "debug", other.Triple, "app" + other.ExecutableSuffix));
        Assert.Equal(other.Os == TargetOs.Windows ? "MZ"u8.ToArray() : "\x7fELF"u8.ToArray(), binary[..(other.Os == TargetOs.Windows ? 2 : 4)]);
    }

    /// <summary>Module globals are made eagerly at the start, a module's imports first (07 G2) —
    /// across packages as within one.</summary>
    [Fact]
    public void A_dependency_s_globals_are_ready_before_the_importer_s()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.config { scale };\n\nfn main(): void {\n    println(f\"{scale}\");\n}\n"),
            ("src/config.lyr", "import geo.base { unit };\n\npub let scale: int = unit * 3;\n"),
            ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n"),
            ("deps/geo/src/base.lyr", "fn seven(): int {\n    return 7;\n}\n\npub let unit: int = seven();\n"));
        Assert.Equal("21\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
