using System.Text.Json;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>out/</c> and the package's verbs around it (design/v5/spec/11 W2 P5, P10): one build at a
/// time writes into <c>out/</c>, <c>lyric clean</c> removes it, <c>lyric metadata</c> writes the
/// package as JSON. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class OutTests
{
    private const string Hello = "import std.io { println };\n\nfn main(): void {\n    println(\"hello\");\n}\n";

    /// <summary>A build holds <c>out/.lock</c>: a second one waits, says so once, and builds when
    /// the first is done.</summary>
    [Fact]
    public void A_second_build_waits_for_the_first()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Hello));
        var project = Project.ProgramsOf(dir)![0];
        var said = new StringWriter();
        var error = TextWriter.Synchronized(said);
        string Said()
        {
            lock (error) return said.ToString();
        }
        var request = new BuildRequest(project, BuildProfile.Of(Profile.Debug), Target.Host, CCompiler.Locate()!, "test");
        Task<(int Exit, string? Executable)> build;
        using (OutLock.Take(project.OutDir, TextWriter.Null))
        {
            build = Task.Run(() => Pipeline.Build(request, error));
            Thread.Sleep(1500);
            Assert.False(build.IsCompleted, "the build did not wait for the lock");
            Assert.Contains($"waiting for another build in {project.OutDir} to finish", Said());
        }
        Assert.True(build.Wait(TimeSpan.FromMinutes(2)), "the build did not finish once the lock was free");
        Assert.True(build.Result.Exit == 0, Said());
        Assert.Single(Said().Split('\n'), line => line.StartsWith("waiting", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_removes_out()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Hello));
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        Assert.True(Directory.Exists(Path.Combine(dir, "out")));
        var (exit, output, error) = Run("clean", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal($"removed {Path.Combine(dir, "out")}\n", output);
        Assert.False(Directory.Exists(Path.Combine(dir, "out")));
        Assert.Equal("nothing to clean\n", Run("clean", "-C", dir).Out);
    }

    [Fact]
    public void Clean_needs_a_package()
    {
        var dir = Package(("notes.txt", "x"));
        var (exit, _, error) = Run("clean", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0004]", error);
        Assert.Equal(2, Run("clean", "--all", "-C", dir).Exit);
    }

    /// <summary>The package as JSON (P10): the graph with where each package is read from, the
    /// programs, the profiles with their fields, the targets, <c>out/</c>.</summary>
    [Fact]
    public void Metadata_writes_the_package_as_json()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "toolchain = \"5.0\"\n\n[dependencies]\ngeo = { path = \"deps/geo\" }\n\n[[bin]]\nname = \"tool\"\nentry = \"src/tool.lyr\"\n\n"
                           + "[profile.staging]\ninherits = \"release\"\nlto = true\n"),
            ("src/main.lyr", Hello), ("src/tool.lyr", Hello),
            ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"1.2.0\"\n"),
            ("deps/geo/src/shapes.lyr", "pub fn one(): int {\n    return 1;\n}\n"));
        var (exit, output, error) = Run("metadata", "-C", dir, "--json");
        Assert.True(exit == 0, error);
        Assert.Single(output.TrimEnd('\n').Split('\n'));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("app", root.GetProperty("root").GetString());

        var packages = root.GetProperty("packages").EnumerateArray().ToList();
        Assert.Equal(["app", "geo"], packages.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("5.0.0", packages[0].GetProperty("toolchain").GetString());
        Assert.Equal(["geo"], packages[0].GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal("1.2.0", packages[1].GetProperty("version").GetString());
        Assert.Equal(Path.Combine(dir, "deps", "geo"), packages[1].GetProperty("path").GetString());

        Assert.Equal(["app", "tool"], root.GetProperty("programs").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("app.tool", root.GetProperty("programs")[1].GetProperty("module").GetString());

        var staging = root.GetProperty("profiles").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "staging");
        Assert.Equal("release", staging.GetProperty("base").GetString());
        Assert.Equal(2, staging.GetProperty("opt").GetInt32());
        Assert.True(staging.GetProperty("lto").GetBoolean());

        Assert.Contains(Target.Host.Triple, root.GetProperty("targets").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(Target.Host.Triple, root.GetProperty("host").GetString());
        Assert.Equal(Path.Combine(dir, "out"), root.GetProperty("out").GetString());
    }

    /// <summary>A package from git says its repository, revision and commit.</summary>
    [Fact]
    public void Metadata_names_the_revision_a_package_is_read_at()
    {
        using var repo = new TestRepo();
        var commit = repo.Commit(("lyric.toml", "[package]\nname = \"geo\"\nversion = \"1.0.0\"\n"), ("src/shapes.lyr", "pub fn one(): int {\n    return 1;\n}\n"));
        repo.Tag("v1.0.0");
        var dir = Package(("lyric.toml", AppManifest + $"\n[dependencies]\ngeo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n"), ("src/main.lyr", Hello));
        var (exit, output, error) = Run("metadata", "-C", dir);
        Assert.True(exit == 0, error);
        using var json = JsonDocument.Parse(output);
        var git = json.RootElement.GetProperty("packages")[1].GetProperty("git");
        Assert.Equal(repo.Url, git.GetProperty("url").GetString());
        Assert.Equal("v1.0.0", git.GetProperty("tag").GetString());
        Assert.Equal(commit, git.GetProperty("commit").GetString());
    }

    [Fact]
    public void Metadata_of_a_library_has_no_programs()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", "pub fn one(): int {\n    return 1;\n}\n"));
        var (exit, output, error) = Run("metadata", "-C", dir);
        Assert.True(exit == 0, error);
        using var json = JsonDocument.Parse(output);
        Assert.Empty(json.RootElement.GetProperty("programs").EnumerateArray());
    }
}
