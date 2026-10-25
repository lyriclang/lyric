using System.Text.Json;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric api</c>: a package's public surface, one line per element in one spelling, as text and
/// as JSON, and with <c>--diff</c> what changed against a git revision of the package, breaking
/// changes first and exit 1 where there is one. The goldens are under <c>api/golden/</c>.
/// </summary>
[Collection("console")]
public class ApiCommandTests
{
    private static readonly string Golden = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "api", "golden");

    private const string Manifest = "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n";

    private const string Lib = """
        //! Shapes of the plane.

        /// A place.
        pub struct Point :: [Equatable] {
            pub x: float,
            pub y: float = 0.0,
        }

        /// What has an area.
        pub interface Shape {
            fn area(): float;
            fn name(): string {
                return "shape";
            }
        }

        /// Where to go.
        pub enum Dir {
            Up,
            Down,
        }

        /// The distance between two places.
        pub fn distance(a: Point, b: Point): float {
            return a.x - b.x;
        }

        /// The place everything starts at.
        pub let origin: float = 0.0;

        fn helper(): int {
            return 1;
        }

        """;

    private static void MatchesGolden(string name, string actual)
    {
        var path = Path.Combine(Golden, name);
        if (Environment.GetEnvironmentVariable("LYRIC_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Golden);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), $"no golden at {path}; set LYRIC_UPDATE_SNAPSHOTS=1 to write it");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    private static string Geo(string lib = Lib) => Package(("lyric.toml", Manifest), ("src/lib.lyr", lib));

    [Fact]
    public void A_package_prints_its_public_surface_one_line_per_element()
    {
        var (exit, output, error) = RunRaw("api", "-C", Geo());
        Assert.True(exit == 0, error);
        Assert.Equal("", error);
        MatchesGolden("geo.txt", output);
        Assert.DoesNotContain("helper", output);
    }

    [Fact]
    public void The_surface_as_json_holds_each_line_as_an_element()
    {
        var root = Geo();
        var (_, text, _) = RunRaw("api", "-C", root);
        var (exit, json, error) = RunRaw("api", "-C", root, "--json");
        Assert.True(exit == 0, error);
        MatchesGolden("geo.json", json);
        var elements = JsonDocument.Parse(json).RootElement.GetProperty("elements").EnumerateArray()
            .Select(e => e.GetProperty("line").GetString()).ToList();
        Assert.Equal(text.TrimEnd('\n').Split('\n'), elements);
    }

    [Fact]
    public void A_change_of_layout_or_comment_changes_no_line_and_a_parameter_name_does()
    {
        var (_, before, _) = RunRaw("api", "-C", Geo());
        var relaid = Lib.Replace("pub fn distance(a: Point, b: Point): float {",
            "pub fn distance(\n    a: Point,   // from\n    b: Point,\n): float {");
        Assert.Equal(before, RunRaw("api", "-C", Geo(relaid)).Out);
        var renamed = Lib.Replace("pub fn distance(a: Point, b: Point)", "pub fn distance(from: Point, b: Point)");
        Assert.NotEqual(before, RunRaw("api", "-C", Geo(renamed)).Out);
    }

    [Fact]
    public void The_standard_librarys_surface_is_the_same_twice()
    {
        var (exit, first, error) = RunRaw("api", "--std");
        Assert.True(exit == 0, error);
        Assert.Equal(first, RunRaw("api", "--std").Out);
        var collections = string.Concat(first.Split('\n')
            .Where(l => l.Contains(" std.collections.") || l.StartsWith("std.collections.", StringComparison.Ordinal))
            .Select(l => l + "\n"));
        Assert.Contains("pub mut fn std.collections.List.push(v: T): void\n", collections);
        MatchesGolden("std-collections.txt", collections);
    }

    /// <summary>The fixture as a git repository: the first version committed, the second written
    /// over it in the working tree.</summary>
    private static string Repository(string first, string second)
    {
        var root = Geo(first);
        foreach (var args in new[]
                 {
                     new[] { "init", "-q" }, ["add", "-A"],
                     ["-c", "user.email=test@lyric.invalid", "-c", "user.name=test", "commit", "-q", "-m", "first"],
                 })
        {
            var git = ProcessRunner.Run("git", ["-C", root, .. args], TimeSpan.FromMinutes(1));
            Assert.True(git.ExitCode == 0, git.Stderr);
        }
        File.WriteAllText(Path.Combine(root, "src", "lib.lyr"), second);
        return root;
    }

    [Fact]
    public void A_removed_function_is_breaking_and_ends_with_exit_one()
    {
        var root = Repository(Lib, Lib.Replace("/// The distance between two places.\npub fn distance(a: Point, b: Point): float {\n    return a.x - b.x;\n}\n", ""));
        var (exit, output, _) = RunRaw("api", "-C", root, "--diff", "HEAD");
        Assert.Equal(1, exit);
        Assert.Contains("breaking:\n  removed geo.distance — code that names it no longer compiles\n    - pub fn geo.distance(a: Point, b: Point): float\n", output);
    }

    [Fact]
    public void A_package_reached_through_a_link_is_found_in_its_repository()
    {
        // macOS reaches its temporary directory through /var, a link to /private/var, and git
        // answers with the resolved path: the package's place in the repository must come from git.
        if (OperatingSystem.IsWindows()) return;
        var root = Repository(Lib, Lib + "pub fn more(): int {\n    return 1;\n}\n");
        var link = Path.Combine(TestDirectories.Fresh("api-link-"), "geo");
        File.CreateSymbolicLink(link, root);
        var (exit, output, error) = RunRaw("api", "-C", link, "--diff", "HEAD");
        Assert.True(exit == 0, error);
        Assert.Contains("added geo.more", output);
    }

    [Fact]
    public void An_added_function_is_compatible_and_ends_with_exit_zero()
    {
        var root = Repository(Lib, Lib + "pub fn more(): int {\n    return 1;\n}\n");
        var (exit, output, _) = RunRaw("api", "-C", root, "--diff", "HEAD");
        Assert.Equal(0, exit);
        Assert.Contains("compatible:\n  added geo.more — code written before does not see it\n    + pub fn geo.more(): int\n", output);
        Assert.DoesNotContain("breaking:", output);
    }

    [Fact]
    public void A_changed_signature_is_breaking_and_shows_both()
    {
        var root = Repository(Lib, Lib.Replace("pub fn distance(a: Point, b: Point): float", "pub fn distance(a: Point, b: Point, c: Point): float"));
        var (exit, output, _) = RunRaw("api", "-C", root, "--diff", "HEAD");
        Assert.Equal(1, exit);
        Assert.Contains("  changed geo.distance — its signature changed: code written against the old one may not compile\n"
                        + "    - pub fn geo.distance(a: Point, b: Point): float\n"
                        + "    + pub fn geo.distance(a: Point, b: Point, c: Point): float\n", output);
    }

    [Fact]
    public void A_reformatted_file_is_no_change()
    {
        var root = Repository(Lib, Lib.Replace("pub fn distance(a: Point, b: Point): float {",
            "// moved to its own lines\npub fn distance(\n    a: Point,\n    b: Point,\n): float {"));
        var (exit, output, _) = RunRaw("api", "-C", root, "--diff", "HEAD");
        Assert.Equal(0, exit);
        Assert.Equal("no change to the public surface\n", output);
    }

    [Theory]
    [InlineData("    pub y: float = 0.0,\n", "    pub y: float = 0.0,\n    pub z: float,\n", true, "added geo.Point.z — an initializer names every field without a value")]
    [InlineData("    pub y: float = 0.0,\n", "    pub y: float = 0.0,\n    pub z: float = 0.0,\n", false, "added geo.Point.z — code written before does not see it")]
    [InlineData("    Down,\n", "    Down,\n    Left,\n", true, "added geo.Dir.Left — a match over every case of the enum misses it")]
    [InlineData("    fn area(): float;\n", "    fn area(): float;\n    fn sides(): int;\n", true, "added geo.Shape.sides — every conformer must write it")]
    [InlineData("    fn area(): float;\n", "    fn area(): float;\n    fn sides(): int {\n        return 0;\n    }\n", false, "added geo.Shape.sides — code written before does not see it")]
    [InlineData("pub struct Point :: [Equatable] {", "pub struct Point {", true, "removed geo.Point — code that names it no longer compiles")]
    [InlineData("    fn name(): string {\n        return \"shape\";\n    }\n", "    fn name(): string;\n", true, "changed geo.Shape.name — its body is gone: every conformer must write it")]
    [InlineData("    fn area(): float;\n", "    fn area(): float {\n        return 0.0;\n    }\n", false, "changed geo.Shape.area — it gained a body: a conformer may leave it out")]
    public void A_change_breaks_where_the_language_makes_the_user_write_it_out(string from, string to, bool breaking, string said)
    {
        var root = Repository(Lib, Lib.Replace(from, to));
        var (exit, output, error) = RunRaw("api", "-C", root, "--diff", "HEAD");
        Assert.True(exit == (breaking ? 1 : 0), output + error);
        Assert.Contains(said, output);
    }

    [Fact]
    public void A_revision_git_does_not_know_is_refused_with_exit_two()
    {
        var root = Repository(Lib, Lib);
        var (exit, _, error) = RunRaw("api", "-C", root, "--diff", "no-such-revision");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0022]: git cannot read 'no-such-revision'", error);
    }

    [Fact]
    public void A_package_in_no_repository_has_nothing_to_diff_against()
    {
        var (exit, _, error) = RunRaw("api", "-C", Geo(), "--diff", "HEAD");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0022]:", error);
        Assert.Contains("is in no git repository", error);
    }

    [Fact]
    public void The_json_of_a_diff_counts_what_breaks()
    {
        var root = Repository(Lib, Lib.Replace("    Down,\n", "    Down,\n    Left,\n"));
        var (exit, json, _) = RunRaw("api", "-C", root, "--diff", "HEAD", "--json");
        Assert.Equal(1, exit);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(1, doc.GetProperty("breaking").GetInt32());
        var change = doc.GetProperty("changes").EnumerateArray().Single();
        Assert.Equal("added", change.GetProperty("change").GetString());
        Assert.Equal("geo.Dir.Left", change.GetProperty("path").GetString());
        Assert.True(change.GetProperty("breaking").GetBoolean());
    }

    [Fact]
    public void The_standard_library_has_no_revision_to_diff_against()
    {
        var (exit, _, error) = RunRaw("api", "--std", "--diff", "HEAD");
        Assert.Equal(2, exit);
        Assert.Contains("the standard library is none", error);
    }
}
