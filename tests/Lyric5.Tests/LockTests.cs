using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Minimal version selection and the lock (design/v5/spec/07 P3, P4, P5): of the versions a graph
/// asks for, the build takes the greatest of one line; <c>lyric.lock</c> holds each revision read
/// at its commit with its content's hash, and <c>lyric update</c> moves it. On local repositories,
/// through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class LockTests
{
    private static string Manifest(string name, string dependencies = "", string version = "0.1.0") =>
        $"[package]\nname = \"{name}\"\nversion = \"{version}\"\n{(dependencies.Length > 0 ? "\n[dependencies]\n" + dependencies : "")}";

    private static string Shapes(string area) => $"pub fn area(w: int, h: int): int {{\n    return {area};\n}}\n";

    private static string Count(int n) => $"pub fn three(): int {{\n    return {n};\n}}\n";

    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    /// <summary><c>geo</c>: <c>v1.0.0</c> computes <c>w * h</c>, <c>v1.1.0</c> — the head of
    /// <c>main</c> — one more.</summary>
    private static TestRepo Geo(out string first, out string second)
    {
        var repo = new TestRepo();
        first = repo.Commit(("lyric.toml", Manifest("geo", version: "1.0.0")), ("src/shapes.lyr", Shapes("w * h")));
        repo.Tag("v1.0.0");
        second = repo.Commit(("lyric.toml", Manifest("geo", version: "1.1.0")), ("src/shapes.lyr", Shapes("w * h + 1")));
        repo.Tag("v1.1.0");
        return repo;
    }

    private static string App(string dependencies, string main = AreaMain) =>
        Package(("lyric.toml", Manifest("app", dependencies)), ("src/main.lyr", main));

    private static string LockPath(string dir) => Path.Combine(dir, LockFile.FileName);

    [Fact]
    public void A_build_writes_the_lock()
    {
        using var repo = Geo(out var first, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var locked = Assert.Single(LockFile.Read(LockPath(dir)));
        Assert.Equal(new Locked("geo", "1.0.0", new GitSource(repo.Url, GitRefKind.Tag, "v1.0.0"), first, locked.Hash), locked);
        var checkout = Path.Combine(UserCache.Root, "git", "checkouts", GitCache.Id(repo.Url), first);
        Assert.Equal(PackageContent.Hash(checkout), locked.Hash);

        // The same resolution leaves the file alone.
        var written = File.GetLastWriteTimeUtc(LockPath(dir));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(written, File.GetLastWriteTimeUtc(LockPath(dir)));
    }

    [Fact]
    public void A_program_that_reads_nothing_from_git_has_no_lock()
    {
        var dir = Package(("lyric.toml", Manifest("app", "geo = { path = \"deps/geo\" }\n")), ("src/main.lyr", AreaMain),
            ("deps/geo/lyric.toml", Manifest("geo")), ("deps/geo/src/shapes.lyr", Shapes("w * h")));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.False(File.Exists(LockPath(dir)));
    }

    /// <summary>A branch stays where the lock holds it (P5), until <c>lyric update</c> moves it.</summary>
    [Fact]
    public void The_lock_holds_a_branch_where_it_was()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", branch = \"main\" }}\n");
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
        repo.Commit(("lyric.toml", Manifest("geo", version: "1.2.0")), ("src/shapes.lyr", Shapes("w * h + 2")));
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var (exit, output, error) = Run("update", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains($"  updated  geo 1.2.0 ({repo.Url} at branch 'main') ", output);
        Assert.Equal("14\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A tag moved in the repository is read where the lock holds it; <c>lyric update</c>
    /// fetches the repository again and moves it.</summary>
    [Fact]
    public void A_moved_tag_stays_where_the_lock_holds_it()
    {
        using var repo = Geo(out _, out var second);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        repo.Git("tag", "-f", "v1.0.0", second);
        // A fresh copy of the repository sees the moved tag; the lock keeps the commit it read.
        TestRepo.DeleteTree(Path.Combine(UserCache.Root, "git", "db", GitCache.Id(repo.Url)));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var (exit, _, error) = Run("update", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>lyric update geo</c> moves geo; the other packages stay at their commits.</summary>
    [Fact]
    public void Update_moves_the_packages_it_names()
    {
        using var geo = new TestRepo();
        geo.Commit(("lyric.toml", Manifest("geo")), ("src/shapes.lyr", Shapes("w * h")));
        using var units = new TestRepo();
        units.Commit(("lyric.toml", Manifest("units")), ("src/count.lyr", Count(3)));
        var main = "import std.io { println };\nimport geo.shapes { area };\nimport units.count { three };\n\n"
                   + "fn main(): void {\n    println(f\"{area(3, 4)} {three()}\");\n}\n";
        var dir = App($"geo = {{ git = \"{geo.Url}\", branch = \"main\" }}\nunits = {{ git = \"{units.Url}\", branch = \"main\" }}\n", main);
        Assert.Equal("12 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
        geo.Commit(("lyric.toml", Manifest("geo")), ("src/shapes.lyr", Shapes("w * h + 1")));
        units.Commit(("lyric.toml", Manifest("units")), ("src/count.lyr", Count(4)));

        var (exit, output, error) = Run("update", "geo", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains("  updated  geo ", output);
        Assert.DoesNotContain("units", output);
        Assert.Equal("13 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(0, Run("update", "-C", dir).Exit);
        Assert.Equal("13 4\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void Update_names_packages_read_from_git()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        var (exit, _, error) = Run("update", "geom", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0003]: no package 'geom' is read from git here: geo", error);

        var plain = Package(("lyric.toml", Manifest("app")), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (none, output, _) = Run("update", "-C", plain);
        Assert.Equal(0, none);
        Assert.Equal("no package is read from git\n", output);
    }

    /// <summary>The lock holds the content (P5): a hash that is not the content's is refused.</summary>
    [Fact]
    public void Content_that_is_not_what_the_lock_holds_is_refused()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var locked = Assert.Single(LockFile.Read(LockPath(dir)));
        File.WriteAllText(LockPath(dir), LockFile.Text([locked with { Hash = "h1:" + Convert.ToBase64String(new byte[32]) }]));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0008]: the content of 'geo' at commit {locked.Commit[..12]} of {repo.Url} is not what lyric.lock holds", error);
    }

    /// <summary>The cache's checkout is the commit's content: one changed by hand is written anew.</summary>
    [Fact]
    public void A_changed_checkout_is_written_anew()
    {
        using var repo = Geo(out var first, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var shapes = Path.Combine(UserCache.Root, "git", "checkouts", GitCache.Id(repo.Url), first, "src", "shapes.lyr");
        File.WriteAllText(shapes, Shapes("w * h + 7"));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(Shapes("w * h"), File.ReadAllText(shapes));
    }

    [Fact]
    public void A_locked_commit_the_repository_lacks_is_refused()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var locked = Assert.Single(LockFile.Read(LockPath(dir)));
        File.WriteAllText(LockPath(dir), LockFile.Text([locked with { Commit = new string('0', 40) }]));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0008]: lyric.lock holds commit 000000000000 of {repo.Url} for 'geo', which the repository does not have", error);
    }

    /// <summary>Offline, the locked commit comes from the cache.</summary>
    [Fact]
    public void Offline_the_lock_reads_the_cache()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", branch = \"main\" }}\n");
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
        TestRepo.DeleteTree(repo.Dir);
        TestRepo.DeleteTree(Path.Combine(dir, "out"));
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir, "--offline"));
    }

    /// <summary>Minimal version selection (P4): <c>v1.0.0</c> asked for here, <c>v1.1.0</c> by a
    /// dependency — the build reads <c>v1.1.0</c>, the least that satisfies both.</summary>
    [Fact]
    public void The_build_takes_the_greatest_version_asked_for()
    {
        using var repo = Geo(out _, out _);
        var dir = Package(
            ("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\nsquare = {{ path = \"deps/square\" }}\n")),
            ("src/main.lyr", AreaMain),
            ("deps/square/lyric.toml", Manifest("square", $"geo = {{ git = \"{repo.Url}\", tag = \"v1.1.0\" }}\n")),
            ("deps/square/src/side.lyr", "pub fn side(): int {\n    return 2;\n}\n"));
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(["v1.0.0", "v1.1.0"], LockFile.Read(LockPath(dir)).Select(e => e.Git.Ref));
    }

    /// <summary>The requirements of every version read count, Go's minimal version selection: the
    /// <c>units</c> v1.2.0 that geo v1.0.0 asks for, though the build reads geo v1.1.0.</summary>
    [Fact]
    public void The_requirements_of_every_version_read_count()
    {
        using var units = new TestRepo();
        units.Commit(("lyric.toml", Manifest("units", version: "1.1.0")), ("src/count.lyr", Count(3)));
        units.Tag("v1.1.0");
        units.Commit(("lyric.toml", Manifest("units", version: "1.2.0")), ("src/count.lyr", Count(4)));
        units.Tag("v1.2.0");
        using var geo = new TestRepo();
        geo.Commit(("lyric.toml", Manifest("geo", $"units = {{ git = \"{units.Url}\", tag = \"v1.2.0\" }}\n", "1.0.0")), ("src/shapes.lyr", Shapes("w * h")));
        geo.Tag("v1.0.0");
        geo.Commit(("lyric.toml", Manifest("geo", $"units = {{ git = \"{units.Url}\", tag = \"v1.1.0\" }}\n", "1.1.0")), ("src/shapes.lyr", Shapes("w * h + 1")));
        geo.Tag("v1.1.0");
        var main = "import std.io { println };\nimport geo.shapes { area };\nimport units.count { three };\n\n"
                   + "fn main(): void {\n    println(f\"{area(3, 4)} {three()}\");\n}\n";
        var dir = Package(
            ("lyric.toml", Manifest("app", $"geo = {{ git = \"{geo.Url}\", tag = \"v1.0.0\" }}\nunits = {{ git = \"{units.Url}\", tag = \"v1.1.0\" }}\n"
                                           + "square = { path = \"deps/square\" }\n")),
            ("src/main.lyr", main),
            ("deps/square/lyric.toml", Manifest("square", $"geo = {{ git = \"{geo.Url}\", tag = \"v1.1.0\" }}\n")),
            ("deps/square/src/side.lyr", "pub fn side(): int {\n    return 2;\n}\n"));
        Assert.Equal("13 4\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("v1.0.0", "v2.0.0")]
    [InlineData("v0.1.0", "v0.2.0")]
    [InlineData("v0.0.1", "v0.0.2")]
    public void Versions_of_two_lines_are_two_packages(string mine, string theirs)
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo")), ("src/shapes.lyr", Shapes("w * h")));
        repo.Tag(mine);
        repo.Tag(theirs);
        var dir = Package(
            ("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"{mine}\" }}\nsquare = {{ path = \"deps/square\" }}\n")),
            ("src/main.lyr", AreaMain),
            ("deps/square/lyric.toml", Manifest("square", $"geo = {{ git = \"{repo.Url}\", tag = \"{theirs}\" }}\n")),
            ("deps/square/src/side.lyr", "pub fn side(): int {\n    return 2;\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0005]: 'geo' is asked for at tag '{mine}' and at tag '{theirs}' of {repo.Url}: two incompatible versions", error);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11", 1)]
    [InlineData("1.0.0+build.5", "1.0.0", 0)]
    public void Versions_are_ordered_as_semver_orders_them(string a, string b, int order)
    {
        Assert.Equal(order, Math.Sign(SemVer.Parse(a)!.CompareTo(SemVer.Parse(b))));
    }

    [Theory]
    [InlineData("v1.2.0", "v1.9.3", true)]
    [InlineData("v1.2.0", "v2.0.0", false)]
    [InlineData("v0.3.0", "0.3.7", true)]
    [InlineData("v0.3.0", "v0.4.0", false)]
    [InlineData("v0.0.3", "v0.0.4", false)]
    public void A_line_is_a_major_version_below_1_a_minor(string a, string b, bool one)
    {
        Assert.Equal(one, SemVer.OfTag(a)!.Line == SemVer.OfTag(b)!.Line);
    }

    [Theory]
    [InlineData("release-1")]
    [InlineData("v1.2")]
    [InlineData("v01.2.3")]
    public void A_tag_that_is_no_version_names_one_revision(string tag)
    {
        Assert.Null(SemVer.OfTag(tag));
    }

    [Fact]
    public void A_lock_reads_what_it_writes()
    {
        var entries = new[]
        {
            new Locked("units", "1.0.0", new GitSource("https://example.org/units", GitRefKind.Branch, "main"), new string('a', 40), "h1:" + Convert.ToBase64String(new byte[32])),
            new Locked("geo", "2.1.0", new GitSource("git@example.org:geo.git", GitRefKind.Tag, "v2.1.0"), new string('b', 40), "h1:" + Convert.ToBase64String(new byte[32])),
            new Locked("io", "0.1.0", new GitSource("file:///srv/io", GitRefKind.Default, null), new string('c', 40), "h1:" + Convert.ToBase64String(new byte[32])),
        };
        var file = Path.Combine(Package(("x", "")), LockFile.FileName);
        LockFile.Write(file, entries);
        Assert.Equal(LockFile.Sorted(entries), LockFile.Read(file));
        Assert.StartsWith("# Written by lyric", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("version = 2\n", "version = 1")]
    [InlineData("version = 1\n[[package]]\nname = \"geo\"\n", "needs a string 'version'")]
    [InlineData("version = 1\n[[package]]\nname = \"geo\"\nversion = \"1.0.0\"\ngit = \"https://x/geo\"\ncommit = \"abc\"\nhash = \"h1:x\"\n", "locked to no commit or no hash")]
    public void A_lock_that_is_no_lock_is_refused(string text, string why)
    {
        var file = Path.Combine(Package(("x", "")), LockFile.FileName);
        File.WriteAllText(file, text);
        var e = Assert.Throws<ManifestException>(() => LockFile.Read(file));
        Assert.Equal("LYR-PKG0002", e.Code);
        Assert.Contains(why, e.Message);
    }
}
