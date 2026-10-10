using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// M8a's artifact (design/v5/spec/13): the examples inventory, stats and stack, 4.x's examples/ in
/// Lyric 5, each built as a package and run. And M7's artifacts: the multi-package example — <c>programs/three_packages</c>, a
/// path dependency and one from git — builds offline once the cache holds it, and builds for the
/// other operating system; a dependency's globals are ready before the importer's (07 G2).
/// And M8b's: the file tool, <c>programs/filetool.lyr</c>, over a tree the test makes.
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

    private static string ProgramText(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "programs", name + ".lyr"));

    /// <summary>As a user meets them: <c>src/main.lyr</c> of a package, built and run — the prelude
    /// and every module of the standard library at hand.</summary>
    [Theory]
    [InlineData("inventory", "Bread (0 gold)\nSword (15 gold)\nAmulet (80 gold)\nGesamtwert: 95 gold\n"
        + "Erstes im Budget (20): Bread\nBread gratis? true\n")]
    [InlineData("stats", "Summe:        24\nMaximum:      9\nDurchschnitt: 4.80\n")]
    [InlineData("stack", "Groesse: 3\nSpitze:  3\npop -> 3\npop -> 2\npop -> 1\n")]
    public void An_example_of_the_standard_library_runs(string name, string expected)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText(name)));
        Assert.Equal(expected, BuildAndRun(dir, "app", "build", "-C", dir));
    }

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
        // "MZ", and 0x7F "ELF" — byte by byte: C#'s \x takes up to four hex digits, and E is one.
        byte[] magic = other.Os == TargetOs.Windows ? [0x4D, 0x5A] : [0x7F, 0x45, 0x4C, 0x46];
        Assert.Equal(magic, binary[..magic.Length]);
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

    private const string TreeStats = "5 files, 3 directories, 2325 bytes\n  1000 sub/c.txt\n  1000 sub/e.bin\n  300 b.log\n";

    /// <summary>M8b's artifact (13, S14): the file tool built as a package — its three commands over
    /// a tree with an empty directory and two files of one size (the walk's order between them
    /// kept), the copy byte for byte the original, a missing directory and an unknown command.</summary>
    [Fact]
    public void The_file_tool_reports_copies_and_finds()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("filetool")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);

        var tree = Path.Combine(dir, "tree");
        void Put(string rel, int size) =>
            File.WriteAllBytes(Path.Combine(tree, rel), [.. Enumerable.Range(0, size).Select(i => (byte)(i * 7 + rel.Length))]);
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        Directory.CreateDirectory(Path.Combine(tree, "sub", "deeper"));
        Put("a.txt", 5);
        Put("b.log", 300);
        Put(Path.Combine("sub", "c.txt"), 1000);
        Put(Path.Combine("sub", "deeper", "d.txt"), 20);
        Put(Path.Combine("sub", "e.bin"), 1000);

        (int Exit, string Out, string Err) Tool(params string[] args)
        {
            var ran = ProcessRunner.Run(exe, args, TimeSpan.FromMinutes(1));
            return (ran.ExitCode, ran.Stdout.Replace("\r\n", "\n"), ran.Stderr.Replace("\r\n", "\n"));
        }

        Assert.Equal((0, TreeStats, ""), Tool("stats", tree));
        Assert.Equal((0, "a.txt\nsub/c.txt\nsub/deeper/d.txt\n", ""), Tool("find", tree, ".txt"));

        var copy = Path.Combine(dir, "copy");
        Assert.Equal((0, "copied 5 files, 2325 bytes\n", ""), Tool("copy", tree, copy));
        Assert.True(Directory.Exists(Path.Combine(copy, "empty")));
        foreach (var file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(copy, Path.GetRelativePath(tree, file))));
        Assert.Equal((0, TreeStats, ""), Tool("stats", copy));

        var missing = Path.Combine(dir, "missing");
        var (failed, nothing, said) = Tool("stats", missing);
        Assert.Equal((1, ""), (failed, nothing));
        Assert.StartsWith($"filetool: not found: {missing}", said);

        var (unknown, _, usage) = Tool();
        Assert.Equal(2, unknown);
        Assert.StartsWith("usage: filetool", usage);
    }
}
