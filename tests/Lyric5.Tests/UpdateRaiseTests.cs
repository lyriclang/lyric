using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric update</c> raises a tag (design/v5/spec/07 P4, M7-8): a tag of the root manifest that
/// is a version moves to the newest tag of its line the repository has — never another major,
/// below 1 never another minor, a pre-release only for a pre-release —, written into
/// <c>lyric.toml</c> on its line, the rest of the file as it was; then the lock. A branch, a
/// commit, a tag that is no version: as before. On local repositories, through <c>Main</c>, so in
/// the console collection.
/// </summary>
[Collection("console")]
public class UpdateRaiseTests
{
    private static string Manifest(string name, string dependencies = "", string version = "0.1.0") =>
        $"[package]\nname = \"{name}\"\nversion = \"{version}\"\n{(dependencies.Length > 0 ? "\n[dependencies]\n" + dependencies : "")}";

    private static string Shapes(int plus) => $"pub fn area(w: int, h: int): int {{\n    return w * h + {plus};\n}}\n";

    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    /// <summary>A repository with a commit per version, each tagged with it, its area that much
    /// above 12 — the versions in the order given.</summary>
    private static TestRepo Repo(string name, params string[] versions)
    {
        var repo = new TestRepo();
        for (var i = 0; i < versions.Length; i++)
        {
            repo.Commit(("lyric.toml", Manifest(name, version: versions[i])), ("src/shapes.lyr", Shapes(i)));
            repo.Tag("v" + versions[i]);
        }
        return repo;
    }

    private static string App(string dependencies) =>
        Package(("lyric.toml", Manifest("app", dependencies)), ("src/main.lyr", AreaMain));

    [Fact]
    public void Update_raises_a_tag_to_the_newest_of_its_line()
    {
        // the newest of line 1 is 1.2.0: 2.0.0 is another major, 1.3.0-rc.1 a pre-release
        using var repo = Repo("geo", "1.0.0", "1.1.0", "1.2.0", "2.0.0", "1.3.0-rc.1");
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var (exit, output, error) = Run("update", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains("  raised   geo v1.0.0 -> v1.2.0", output);
        Assert.Contains("tag = \"v1.2.0\"", File.ReadAllText(Path.Combine(dir, "lyric.toml")));
        Assert.Equal("1.2.0", Assert.Single(LockFile.Read(Path.Combine(dir, LockFile.FileName))).Version);
        Assert.Equal("14\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void Below_one_the_minor_is_the_line()
    {
        using var repo = Repo("geo", "0.1.0", "0.1.3", "0.2.0");
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v0.1.0\" }}\n");
        var (exit, output, error) = Run("update", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains("  raised   geo v0.1.0 -> v0.1.3", output);
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The line written as it was — a comment after it, the order of its keys —, the tag
    /// alone changed; every other line of the file untouched.</summary>
    [Fact]
    public void The_manifest_keeps_all_but_the_tag()
    {
        using var repo = Repo("geo", "1.0.0", "1.1.0");
        var written = "# the app\n" + Manifest("app", $"geo = {{ tag = \"v1.0.0\", git = \"{repo.Url}\" }}  # pinned\n");
        var dir = Package(("lyric.toml", written), ("src/main.lyr", AreaMain));
        Assert.Equal(0, Run("update", "-C", dir).Exit);
        Assert.Equal(written.Replace("v1.0.0", "v1.1.0"), File.ReadAllText(Path.Combine(dir, "lyric.toml")));
    }

    [Fact]
    public void A_named_update_raises_that_package_alone()
    {
        using var geo = Repo("geo", "1.0.0", "1.1.0");
        using var units = Repo("units", "1.0.0", "1.1.0");
        var dir = App($"geo = {{ git = \"{geo.Url}\", tag = \"v1.0.0\" }}\nunits = {{ git = \"{units.Url}\", tag = \"v1.0.0\" }}\n");
        var (exit, output, error) = Run("update", "geo", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains("  raised   geo v1.0.0 -> v1.1.0", output);
        Assert.DoesNotContain("raised   units", output);
        var manifest = File.ReadAllText(Path.Combine(dir, "lyric.toml"));
        Assert.Contains($"units = {{ git = \"{units.Url}\", tag = \"v1.0.0\" }}", manifest);
    }

    /// <summary>A branch, a commit and a tag that is no version are not raised: the manifest stays.</summary>
    [Fact]
    public void Branches_commits_and_names_stay()
    {
        using var repo = Repo("geo", "1.0.0", "1.1.0");
        repo.Tag("stable");
        var written = Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"stable\" }}\n");
        var dir = Package(("lyric.toml", written), ("src/main.lyr", AreaMain));
        var (exit, output, error) = Run("update", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("raised", output);
        Assert.Equal(written, File.ReadAllText(Path.Combine(dir, "lyric.toml")));
    }
}
