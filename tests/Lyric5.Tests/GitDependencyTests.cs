using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Git dependencies (design/v5/spec/11 W2 P2, P8, P9; 07 V7, P10): a package read from a revision
/// of a repository — a tag, a branch, a commit, the default branch — by way of the user's cache,
/// which <c>--offline</c> reads alone; what the package is made of; one source for each name.
/// The repositories are local ones (<c>file://</c>): the tests reach no network. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class GitDependencyTests
{
    /// <summary>A manifest; a version a tag over it names (the package graph refuses a tag that is
    /// another version, PackageRuleTests).</summary>
    private static string Manifest(string name, string dependencies = "", string version = "0.1.0") =>
        $"[package]\nname = \"{name}\"\nversion = \"{version}\"\n{(dependencies.Length > 0 ? "\n" + dependencies : "")}";

    private static string Shapes(string area) => $"pub fn area(w: int, h: int): int {{\n    return {area};\n}}\n";

    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    /// <summary>The repository of <c>geo</c>: <c>v1.0.0</c> computes <c>w * h</c>, <c>v1.1.0</c> —
    /// the head of <c>main</c> — one more.</summary>
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
        Package(("lyric.toml", Manifest("app", "[dependencies]\n" + dependencies + "\n")), ("src/main.lyr", main));

    [Fact]
    public void A_git_dependency_is_read_at_its_tag()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A branch is read at its head — where the lock then holds it (LockTests).</summary>
    [Fact]
    public void A_branch_is_read_at_its_head()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", branch = \"main\" }}");
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_commit_is_read_by_its_id()
    {
        using var repo = Geo(out var first, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", rev = \"{first[..10]}\" }}");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void Without_a_revision_the_default_branch_is_read()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\" }}");
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_revision_the_repository_lacks_is_refused()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v9.9.9\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0006]: {repo.Url} has no tag 'v9.9.9' for 'geo'", error);
    }

    /// <summary>A repository that cannot be reached is the environment's failure, not the
    /// manifest's (11 C5): exit 2.</summary>
    [Fact]
    public void A_repository_that_cannot_be_read_is_the_environments_failure()
    {
        var nowhere = new Uri(Path.Combine(Path.GetTempPath(), "lyric5-no-repo-" + Guid.NewGuid().ToString("N"))).AbsoluteUri;
        var dir = App($"geo = {{ git = \"{nowhere}\", tag = \"v1.0.0\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains($"error[LYR-PKG0007]: 'geo' cannot be read from {nowhere}: ", error);
    }

    /// <summary>What was fetched once stays in the user's cache (P9): offline, with the repository
    /// gone, the package is read from there.</summary>
    [Fact]
    public void Offline_the_cache_answers()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        TestRepo.DeleteTree(repo.Dir);
        TestRepo.DeleteTree(Path.Combine(dir, "out"));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir, "--offline"));
    }

    [Fact]
    public void Offline_nothing_is_fetched()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        var (exit, _, error) = Run("build", "-C", dir, "--offline");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-PKG0007]: 'geo' is read from", error);
        Assert.Contains("which the cache does not hold, and --offline fetches nothing", error);
    }

    /// <summary>The replaced package is not read (07 P7) — not even fetched: the repository does not exist.</summary>
    [Fact]
    public void An_override_replaces_a_package_from_git_unfetched()
    {
        var nowhere = new Uri(Path.Combine(Path.GetTempPath(), "lyric5-no-repo-" + Guid.NewGuid().ToString("N"))).AbsoluteUri;
        var dir = Package(
            ("lyric.toml", Manifest("app", $"[dependencies]\ngeo = {{ git = \"{nowhere}\", tag = \"v1.0.0\" }}\n\n[override]\ngeo = {{ path = \"deps/geo\" }}\n")),
            ("src/main.lyr", AreaMain), ("deps/geo/lyric.toml", Manifest("geo")), ("deps/geo/src/shapes.lyr", Shapes("w * h")));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A package from git is its repository's package content: a directory beside it is no
    /// part of it.</summary>
    [Fact]
    public void A_package_from_git_names_its_dependencies_by_git()
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", Manifest("geo", "[dependencies]\nunits = { path = \"../units\" }\n", "1.0.0")), ("src/shapes.lyr", Shapes("w * h")));
        repo.Tag("v1.0.0");
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0004]: 'geo' is read from git and names 'units' by a directory", error);
    }

    /// <summary>What a package from git delivers is its package content (11 W2 P8): the default
    /// set minus what it excludes — not its tests, not its notes.</summary>
    [Fact]
    public void A_package_from_git_is_its_package_content()
    {
        using var repo = new TestRepo();
        var commit = repo.Commit(
            ("lyric.toml", Manifest("geo", version: "1.0.0").Replace("version = \"1.0.0\"\n", "version = \"1.0.0\"\nexclude = [\"src/draft.lyr\"]\n")),
            ("src/shapes.lyr", Shapes("w * h")), ("src/draft.lyr", "pub fn later(): int {\n    return 0;\n}\n"),
            ("tests/shapes_test.lyr", "fn main(): void {\n}\n"), ("README.md", "geo\n"), ("notes.txt", "todo\n"));
        repo.Tag("v1.0.0");
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var checkout = Path.Combine(UserCache.Root, "git", "checkouts", GitCache.Id(repo.Url), commit);
        var files = Directory.EnumerateFiles(checkout, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(checkout, f).Replace('\\', '/')).Order(StringComparer.Ordinal);
        Assert.Equal(["README.md", "lyric.toml", "src/shapes.lyr"], files);

        var draft = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}",
            "import geo.draft { later };\n\nfn main(): void {\n    later();\n}\n");
        var (exit, _, error) = Run("build", "-C", draft, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0003]: cannot find module 'geo.draft'", error);
    }

    /// <summary>One package of each name (07 P3): a tag and a branch of one repository are two
    /// sources.</summary>
    [Fact]
    public void Two_revisions_of_one_package_are_refused()
    {
        using var repo = Geo(out _, out _);
        var dir = Package(
            ("lyric.toml", Manifest("app", $"[dependencies]\ngeo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\nshapes = {{ path = \"deps/shapes\" }}\n")),
            ("src/main.lyr", AreaMain),
            ("deps/shapes/lyric.toml", Manifest("shapes", $"[dependencies]\ngeo = {{ git = \"{repo.Url}\", branch = \"main\" }}\n")),
            ("deps/shapes/src/square.lyr", "pub fn side(): int {\n    return 2;\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0005]: 'geo' is read from {repo.Url} at tag 'v1.0.0' and from {repo.Url} at branch 'main'", error);
    }

    /// <summary>The C is keyed by every package of the graph: another tag builds anew.</summary>
    [Fact]
    public void Another_tag_builds_anew()
    {
        using var repo = Geo(out _, out _);
        var dir = App($"geo = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}");
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), Manifest("app", $"[dependencies]\ngeo = {{ git = \"{repo.Url}\", tag = \"v1.1.0\" }}\n"));
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The test repositories' git leaves nothing running behind a command
    /// (<see cref="TestRepo"/>): it reads <c>maintenance.auto</c> as false.</summary>
    [Fact]
    public void A_test_repository_runs_no_maintenance()
    {
        using var repo = new TestRepo();
        Assert.Equal("false", repo.Git("config", "--get", "maintenance.auto"));
    }
}

/// <summary>A git repository in one of the run's directories, made with the git command line
/// under a configuration of its own — the developer's does not reach it —, and removed with what
/// the user's cache holds of it.</summary>
internal sealed class TestRepo : IDisposable
{
    private static readonly Dictionary<string, string> Isolated = new(StringComparer.Ordinal)
    {
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
        // No maintenance: a commit starts `git maintenance run --auto --detach`, which on POSIX
        // goes on in .git after the commit has returned — while the test is removing the
        // repository (CI, ubuntu: "Directory not empty", in the tests that end fastest).
        ["GIT_CONFIG_COUNT"] = "1",
        ["GIT_CONFIG_KEY_0"] = "maintenance.auto",
        ["GIT_CONFIG_VALUE_0"] = "false",
        ["GIT_AUTHOR_NAME"] = "test",
        ["GIT_AUTHOR_EMAIL"] = "test@lyric.invalid",
        ["GIT_COMMITTER_NAME"] = "test",
        ["GIT_COMMITTER_EMAIL"] = "test@lyric.invalid",
    };

    public string Dir { get; } = TestDirectories.Fresh("lyric5-repo-");

    /// <summary>As a manifest names it: a <c>file://</c> URL.</summary>
    public string Url => new Uri(Dir).AbsoluteUri;

    public TestRepo()
    {
        Git("init", "-q", "-b", "main");
    }

    /// <summary>A commit whose tree is <paramref name="files"/> and nothing else; its id.</summary>
    public string Commit(params (string Path, string Text)[] files)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(Dir))
        {
            if (Path.GetFileName(entry) == ".git") continue;
            if (Directory.Exists(entry)) DeleteTree(entry);
            else File.Delete(entry);
        }
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(Dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        Git("add", "-A");
        Git("commit", "-q", "--allow-empty", "-m", "commit");
        return Git("rev-parse", "HEAD");
    }

    public void Tag(string name) => Git("tag", name);

    public string Git(params string[] args)
    {
        var result = ProcessRunner.Run("git", ["-C", Dir, .. args], TimeSpan.FromMinutes(1), environment: Isolated);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)}: {result.Stderr}");
        return result.Stdout.Trim();
    }

    /// <summary>As the run's directories are removed: as far as it goes. What cannot be removed
    /// yet stays — the repository goes with the run's root when the process ends —, and a test
    /// whose checks passed does not fail over its cleanup.</summary>
    public void Dispose()
    {
        var id = GitCache.Id(Url);
        foreach (var directory in new[] { Dir, Path.Combine(UserCache.Root, "git", "db", id), Path.Combine(UserCache.Root, "git", "checkouts", id) })
        {
            try { DeleteTree(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>git keeps its objects read-only, which a recursive delete on Windows refuses.</summary>
    public static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }
}
