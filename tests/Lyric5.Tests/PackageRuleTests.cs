using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Three rules of the package graph (design/v5/spec/07 V7; the review's M7-6, M7-10 and the work
/// list's "tag against the manifest's version"):
///
/// <para>No package depends on itself, at once or through others: a cycle is refused with its
/// path, <c>app -> geo -> app</c> (<c>LYR-PKG0010</c>). Before, a cycle of directories was taken
/// in silence.</para>
///
/// <para>A git URL is compared, cached and locked in its normal form — a <c>.git</c> and a
/// <c>/</c> at the end dropped, the scheme and the host in lower case: two spellings of one
/// repository are one source. Before, they were two (<c>LYR-PKG0005</c>).</para>
///
/// <para>A tag that is a semantic version names the version the repository's manifest gives
/// there: <c>v1.2.0</c> over <c>version = "1.1.0"</c> is refused (<c>LYR-PKG0006</c>) — minimal
/// version selection chooses by the tag, the lock writes the manifest's version.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class PackageRuleTests
{
    private static string Manifest(string name, string dependencies = "", string version = "0.1.0") =>
        $"[package]\nname = \"{name}\"\nversion = \"{version}\"\n{(dependencies.Length > 0 ? "\n[dependencies]\n" + dependencies : "")}";

    private const string Shapes = "pub fn area(w: int, h: int): int {\n    return w * h;\n}\n";

    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    [Fact]
    public void A_package_cycle_is_refused_with_its_path()
    {
        var dir = Package(("lyric.toml", Manifest("app", "geo = { path = \"deps/geo\" }\n")), ("src/main.lyr", AreaMain),
            ("deps/geo/lyric.toml", Manifest("geo", "app = { path = \"../..\" }\n")), ("deps/geo/src/shapes.lyr", Shapes));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0010]: ", error);
        Assert.Contains("app -> geo -> app", error);
    }

    [Fact]
    public void A_cycle_below_the_root_is_refused_with_its_own_path()
    {
        var dir = Package(("lyric.toml", Manifest("app", "geo = { path = \"deps/geo\" }\n")), ("src/main.lyr", AreaMain),
            ("deps/geo/lyric.toml", Manifest("geo", "units = { path = \"../units\" }\n")), ("deps/geo/src/shapes.lyr", Shapes),
            ("deps/units/lyric.toml", Manifest("units", "geo = { path = \"../geo\" }\n")),
            ("deps/units/src/count.lyr", "pub fn three(): int {\n    return 3;\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0010]: ", error);
        Assert.Contains("geo -> units -> geo", error);
        Assert.DoesNotContain("app -> ", error);
    }

    [Fact]
    public void A_package_that_names_itself_is_a_cycle()
    {
        var dir = Package(("lyric.toml", Manifest("app", "app = { path = \".\" }\n")), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("app -> app", error);
    }

    [Theory]
    [InlineData("HTTPS://Example.COM/Org/Geo.git", "https://example.com/Org/Geo")]
    [InlineData("https://example.com/org/geo/", "https://example.com/org/geo")]
    [InlineData("https://example.com/org/geo.git/", "https://example.com/org/geo")]
    [InlineData("ssh://Git@Example.com:2222/org/geo.git", "ssh://Git@example.com:2222/org/geo")]
    [InlineData("Git@GitHub.com:Org/Geo.git", "Git@github.com:Org/Geo")]
    [InlineData("file:///srv/Repos/geo.git/", "file:///srv/Repos/geo")]
    public void A_git_url_is_read_in_its_normal_form(string written, string normal)
    {
        var dir = Package(("lyric.toml", Manifest("app", $"geo = {{ git = \"{written}\", tag = \"v1.0.0\" }}\n")),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var manifest = Lyric5.Build.Manifest.Read(Path.Combine(dir, "lyric.toml"));
        Assert.Equal(normal, Assert.Single(manifest.Dependencies).Git!.Url);
    }

    [Fact]
    public void Two_spellings_of_one_repository_are_one_source()
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo", version: "1.0.0")), ("src/shapes.lyr", Shapes));
        repo.Tag("v1.0.0");
        var main = "import std.io { println };\nimport geo.shapes { area };\nimport square.side { side };\n\n"
                   + "fn main(): void {\n    println(f\"{area(3, 4)} {side()}\");\n}\n";
        var dir = Package(
            ("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}/\", tag = \"v1.0.0\" }}\nsquare = {{ path = \"deps/square\" }}\n")),
            ("src/main.lyr", main),
            ("deps/square/lyric.toml", Manifest("square", $"geo = {{ git = \"{repo.Url}.git\", tag = \"v1.0.0\" }}\n")),
            ("deps/square/src/side.lyr", "pub fn side(): int {\n    return 4;\n}\n"));
        Assert.Equal("12 4\n", BuildAndRun(dir, "app", "build", "-C", dir));
        // the lock holds the one source, in its normal form
        Assert.Equal(repo.Url, Assert.Single(LockFile.Read(Path.Combine(dir, "lyric.lock"))).Git.Url);
    }

    [Fact]
    public void A_tag_its_manifest_does_not_carry_is_refused()
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo", version: "1.1.0")), ("src/shapes.lyr", Shapes));
        repo.Tag("v1.2.0");
        var dir = Package(("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"v1.2.0\" }}\n")), ("src/main.lyr", AreaMain));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0006]: tag 'v1.2.0' of {repo.Url} holds 'geo' at version 1.1.0", error);
    }

    /// <summary>A tag that is no version names none: nothing to compare.</summary>
    [Fact]
    public void A_tag_that_is_no_version_is_taken_as_it_is()
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo", version: "0.3.0")), ("src/shapes.lyr", Shapes));
        repo.Tag("stable");
        var dir = Package(("lyric.toml", Manifest("app", $"geo = {{ git = \"{repo.Url}\", tag = \"stable\" }}\n")), ("src/main.lyr", AreaMain));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
