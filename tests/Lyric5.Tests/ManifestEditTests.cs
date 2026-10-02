using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric add</c> and <c>lyric remove</c> (design/v5/spec/11 C2): one line of
/// <c>[dependencies]</c> written or taken out, the rest of the manifest as it was; a graph that
/// refuses the edit leaves the manifest as it was. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class ManifestEditTests
{
    private const string Written = "# the app\n[package]\nname = \"app\"   # its name\nversion = \"0.1.0\"\n\n"
                                   + "[dependencies]\n# shapes first\ngeo = { path = \"../geo\" }\n\n[profile.release]\nlto = true\n";

    [Fact]
    public void Add_writes_a_line_after_the_table_s_last()
    {
        var edited = ManifestEdit.Add(Written, "units", "{ path = \"../units\" }");
        Assert.Equal(Written.Replace("geo = { path = \"../geo\" }\n", "geo = { path = \"../geo\" }\nunits = { path = \"../units\" }\n"), edited);
    }

    [Fact]
    public void Add_replaces_the_line_that_names_it()
    {
        Assert.Equal(Written.Replace("{ path = \"../geo\" }", "{ git = \"https://x/geo\" }"),
            ManifestEdit.Add(Written, "geo", "{ git = \"https://x/geo\" }"));
        var quoted = Written.Replace("geo = ", "\"geo\" = ");
        Assert.Equal(Written.Replace("{ path = \"../geo\" }", "{ path = \"g\" }"), ManifestEdit.Add(quoted, "geo", "{ path = \"g\" }"));
    }

    [Fact]
    public void Add_begins_the_table_where_there_is_none()
    {
        var plain = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n";
        Assert.Equal("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\ngeo = { path = \"g\" }\n",
            ManifestEdit.Add(plain, "geo", "{ path = \"g\" }"));
    }

    [Fact]
    public void An_edit_keeps_the_line_ends()
    {
        var crlf = Written.Replace("\n", "\r\n");
        Assert.Equal(crlf.Replace("geo = { path = \"../geo\" }\r\n", ""), ManifestEdit.Remove(crlf, "geo"));
    }

    [Fact]
    public void Remove_takes_the_line_out()
    {
        Assert.Equal(Written.Replace("geo = { path = \"../geo\" }\n", ""), ManifestEdit.Remove(Written, "geo"));
        Assert.Null(ManifestEdit.Remove(Written, "units"));
    }

    [Theory]
    [InlineData("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies.geo]\npath = \"../geo\"\n")]
    [InlineData("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\ngeo.path = \"../geo\"\n")]
    public void A_dependency_written_as_a_table_is_left_to_the_person(string text)
    {
        Assert.Throws<InvalidOperationException>(() => ManifestEdit.Add(text, "geo", "{ path = \"g\" }"));
        Assert.Throws<InvalidOperationException>(() => ManifestEdit.Remove(text, "geo"));
    }

    private static string App() => Package(
        ("lyric.toml", AppManifest),
        ("src/main.lyr", "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n"),
        ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n"),
        ("deps/geo/src/shapes.lyr", "pub fn area(w: int, h: int): int {\n    return w * h;\n}\n"));

    [Fact]
    public void Add_writes_the_dependency_relative_to_the_manifest()
    {
        var dir = App();
        var (exit, output, error) = Run("add", "geo", "--path", "deps/geo", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("added geo = { path = \"deps/geo\" }\n", output);
        Assert.Equal(AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n", File.ReadAllText(Path.Combine(dir, "lyric.toml")));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A graph that refuses the dependency leaves the manifest as it was.</summary>
    [Fact]
    public void A_refused_dependency_leaves_the_manifest_as_it_was()
    {
        var dir = App();
        var (exit, _, error) = Run("add", "geo", "--path", "nowhere", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0004]: there is no package at", error);
        Assert.Equal(AppManifest, File.ReadAllText(Path.Combine(dir, "lyric.toml")));
    }

    [Fact]
    public void Add_reads_a_package_from_git_and_locks_it()
    {
        using var repo = new TestRepo();
        var commit = repo.Commit(("lyric.toml", "[package]\nname = \"geo\"\nversion = \"1.0.0\"\n"),
            ("src/shapes.lyr", "pub fn area(w: int, h: int): int {\n    return w * h;\n}\n"));
        repo.Tag("v1.0.0");
        var dir = App();
        var (exit, output, error) = Run("add", "geo", "--git", repo.Url, "--tag", "v1.0.0", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal($"added geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n", output);
        Assert.Equal(commit, Assert.Single(LockFile.Read(Path.Combine(dir, "lyric.lock"))).Commit);

        var (removed, said, why) = Run("remove", "geo", "-C", dir);
        Assert.True(removed == 0, why);
        Assert.Equal("removed geo\n", said);
        Assert.Equal(AppManifest + "\n[dependencies]\n", File.ReadAllText(Path.Combine(dir, "lyric.toml")));
        Assert.Empty(LockFile.Read(Path.Combine(dir, "lyric.lock")));
    }

    [Theory]
    [InlineData(new[] { "add" }, "'add' needs the dependency's name")]
    [InlineData(new[] { "add", "geo" }, "--path or --git, one of them")]
    [InlineData(new[] { "add", "geo", "--path", "a", "--git", "b" }, "--path or --git, one of them")]
    [InlineData(new[] { "add", "geo", "--path", "a", "--tag", "v1" }, "'--tag' names a revision of a repository: it goes with --git")]
    [InlineData(new[] { "add", "geo", "--git", "b", "--tag", "v1", "--rev", "abc1234" }, "a dependency reads one revision")]
    [InlineData(new[] { "remove" }, "'remove' takes the dependency's name, one")]
    [InlineData(new[] { "remove", "units" }, "no dependency 'units' in")]
    public void An_edit_of_the_dependencies_is_checked(string[] args, string why)
    {
        var dir = App();
        var (exit, _, error) = Run([.. args, "-C", dir]);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0003]: ", error);
        Assert.Contains(why, error);
    }

    [Fact]
    public void A_dependency_written_as_a_table_is_refused_by_the_verbs()
    {
        var dir = App();
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest + "\n[dependencies.geo]\npath = \"deps/geo\"\n");
        var (exit, _, error) = Run("remove", "geo", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0008]: 'geo' is written as a table in lyric.toml, not as one line: edit it by hand", error);
    }
}
