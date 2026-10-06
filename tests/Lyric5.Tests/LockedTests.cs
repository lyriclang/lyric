using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>--locked</c> (design/v5/spec/07 M7-9; 11 M7-9) for <c>build</c>, <c>run</c> and <c>test</c>:
/// the build takes <c>lyric.lock</c> exactly as it is and writes nothing — a dependency the lock
/// does not hold is refused before anything is fetched, a lock that holds what the graph no
/// longer reads is refused, and no lock where the program reads from git is refused
/// (<c>LYR-PKG0011</c>, exit 1). Without the flag a build writes the lock on, as before. On local
/// repositories, through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class LockedTests
{
    private static string Manifest(string name, string dependencies = "", string version = "0.1.0") =>
        $"[package]\nname = \"{name}\"\nversion = \"{version}\"\n{(dependencies.Length > 0 ? "\n[dependencies]\n" + dependencies : "")}";

    private static string Shapes(string area) => $"pub fn area(w: int, h: int): int {{\n    return {area};\n}}\n";

    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    private static TestRepo Geo()
    {
        var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo", version: "1.0.0")), ("src/shapes.lyr", Shapes("w * h")));
        repo.Tag("v1.0.0");
        return repo;
    }

    private static string App(string dependencies, string main = AreaMain) =>
        Package(("lyric.toml", Manifest("app", dependencies)), ("src/main.lyr", main));

    private static string LockPath(string dir) => Path.Combine(dir, LockFile.FileName);

    /// <summary>A branch moved since the lock was written: under <c>--locked</c> the build reads
    /// the locked commit and leaves the lock as it is.</summary>
    [Fact]
    public void A_locked_build_takes_the_lock_as_it_is()
    {
        using var repo = Geo();
        var dir = App($"geo = {{ git = \"{repo.Url}\", branch = \"main\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var lockText = File.ReadAllText(LockPath(dir));
        repo.Commit(("lyric.toml", Manifest("geo", version: "1.0.1")), ("src/shapes.lyr", Shapes("w * h + 1")));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "--locked", "-C", dir));
        Assert.Equal(lockText, File.ReadAllText(LockPath(dir)));
    }

    /// <summary>A dependency the lock does not hold is refused before anything is fetched: the
    /// repository named here does not exist, and the refusal is not that it cannot be reached.</summary>
    [Fact]
    public void A_dependency_the_lock_lacks_is_refused_unfetched()
    {
        using var repo = Geo();
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var nowhere = new Uri(Path.Combine(Path.GetTempPath(), "lyric5-no-such-repo-" + Guid.NewGuid().ToString("N"))).AbsoluteUri;
        File.WriteAllText(Path.Combine(dir, "lyric.toml"),
            Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\nunits = {{ git = \"{nowhere}\", tag = \"v1.0.0\" }}\n"));
        var lockText = File.ReadAllText(LockPath(dir));
        var (exit, _, error) = Run("build", "--locked", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0011]: ", error);
        Assert.Contains("'units'", error);
        Assert.Equal(lockText, File.ReadAllText(LockPath(dir)));
    }

    /// <summary>The manifest no longer reads geo; the lock still holds it.</summary>
    [Fact]
    public void A_lock_that_holds_more_than_the_graph_reads_is_refused()
    {
        using var repo = Geo();
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), Manifest("app"));
        File.WriteAllText(Path.Combine(dir, "src", "main.lyr"), "fn main(): void {\n}\n");
        var lockText = File.ReadAllText(LockPath(dir));
        var (exit, _, error) = Run("build", "--locked", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0011]: ", error);
        Assert.Contains("'geo'", error);
        Assert.Equal(lockText, File.ReadAllText(LockPath(dir)));
        // without the flag the build writes the lock on, as before: geo is dropped
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        Assert.Empty(LockFile.Read(LockPath(dir)));
    }

    [Fact]
    public void Without_a_lock_a_locked_build_from_git_is_refused()
    {
        using var repo = Geo();
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        var (exit, _, error) = Run("build", "--locked", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0011]: ", error);
        Assert.False(File.Exists(LockPath(dir)));
    }

    /// <summary>A program that reads nothing from git needs no lock, locked or not.</summary>
    [Fact]
    public void A_program_from_directories_needs_no_lock()
    {
        var dir = Package(("lyric.toml", Manifest("app", "geo = { path = \"deps/geo\" }\n")), ("src/main.lyr", AreaMain),
            ("deps/geo/lyric.toml", Manifest("geo")), ("deps/geo/src/shapes.lyr", Shapes("w * h")));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "--locked", "-C", dir));
        Assert.False(File.Exists(LockPath(dir)));
    }

    [Fact]
    public void Run_and_test_take_the_flag()
    {
        using var repo = Geo();
        var dir = Package(("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n")),
            ("src/main.lyr", AreaMain),
            ("tests/area.lyr", "import geo.shapes { area };\nimport std.test { assertEq };\n\n@Test\nfn areaMultiplies(): void {\n    assertEq(area(3, 4), 12);\n}\n"));
        var (unlocked, _, why) = Run("test", "--locked", "-C", dir);
        Assert.Equal(1, unlocked);
        Assert.Contains("error[LYR-PKG0011]: ", why);
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        // the program runs on the caller's console: its exit is what is seen here
        var (ran, _, error) = Run("run", "--locked", "-C", dir);
        Assert.True(ran == 0, error);
        var (tested, _, failed) = Run("test", "--locked", "-C", dir);
        Assert.True(tested == 0, failed);
    }
}
