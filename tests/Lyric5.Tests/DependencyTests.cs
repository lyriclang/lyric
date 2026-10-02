using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Path dependencies (design/v5/spec/07 V7 P3, P6, P7; 11 W2): a package depends on packages by
/// directory, imports from what it declares and nothing further, holds one package of each name
/// unless its root overrides one — the replaced one never read —, and a library builds as a check
/// (07 B6). Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class DependencyTests
{
    private static string Manifest(string name, string dependencies = "") =>
        $"[package]\nname = \"{name}\"\nversion = \"0.1.0\"\n{(dependencies.Length > 0 ? "\n" + dependencies : "")}";

    private const string Shapes = "pub fn area(w: int, h: int): int {\n    return w * h;\n}\n";
    private const string Count = "pub fn three(): int {\n    return 3;\n}\n";
    private const string AreaMain = "import std.io { println };\nimport geo.shapes { area };\n\nfn main(): void {\n    println(f\"{area(3, 4)}\");\n}\n";

    [Fact]
    public void A_dependency_is_read_from_its_path()
    {
        var dir = Package(("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n")),
            ("src/main.lyr", AreaMain), ("deps/geo/lyric.toml", Manifest("geo")), ("deps/geo/src/shapes.lyr", Shapes));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A package imports what its manifest declares (P6) — the flat closure of 4.x gave
    /// every package everything.</summary>
    [Fact]
    public void A_dependency_of_a_dependency_is_not_imported()
    {
        var main = "import std.io { println };\nimport units.count { three };\n\nfn main(): void {\n    println(f\"{three()}\");\n}\n";
        var dir = Package(("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n")),
            ("src/main.lyr", main),
            ("deps/geo/lyric.toml", Manifest("geo", "[dependencies]\nunits = { path = \"deps/units\" }\n")),
            ("deps/geo/src/shapes.lyr", Shapes),
            ("deps/geo/deps/units/lyric.toml", Manifest("units")), ("deps/geo/deps/units/src/count.lyr", Count));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0014]: package 'units' is no dependency of 'app'", error);
    }

    [Theory]
    [InlineData("other", "the package at '", "is named 'other', not 'geo'")]
    [InlineData(null, "there is no package at '", "no lyric.toml there")]
    public void A_dependency_path_holds_the_package_it_names(string? named, string start, string end)
    {
        var files = new List<(string, string)>
        {
            ("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n")),
            ("src/main.lyr", "fn main(): void {\n}\n"),
            ("deps/geo/src/shapes.lyr", Shapes),
        };
        if (named is not null) files.Add(("deps/geo/lyric.toml", Manifest(named)));
        var dir = Package(files.ToArray());
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0004]: " + start, error);
        Assert.Contains(end, error);
    }

    private static (string, string)[] TwoUnits(string overrides = "") =>
    [
        ("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\nunits = { path = \"deps/units\" }\n" + overrides)),
        ("src/main.lyr", "import std.io { println };\nimport geo.shapes { area };\nimport units.count { three };\n\nfn main(): void {\n    println(f\"{area(3, 4)} {three()}\");\n}\n"),
        ("deps/geo/lyric.toml", Manifest("geo", "[dependencies]\nunits = { path = \"deps/units\" }\n")),
        ("deps/geo/src/shapes.lyr", Shapes),
        ("deps/geo/deps/units/lyric.toml", Manifest("units")), ("deps/geo/deps/units/src/count.lyr", Count),
        ("deps/units/lyric.toml", Manifest("units")), ("deps/units/src/count.lyr", Count),
    ];

    [Fact]
    public void A_program_holds_one_package_of_each_name()
    {
        var dir = Package(TwoUnits());
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0005]: 'units' is read from", error);
    }

    [Fact]
    public void An_override_picks_the_one()
    {
        var dir = Package(TwoUnits("\n[override]\nunits = { path = \"deps/units\" }\n"));
        Assert.Equal("12 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The replaced package is not read (P7): its manifest may be anything.</summary>
    [Fact]
    public void An_overridden_package_is_never_read()
    {
        var files = TwoUnits("\n[override]\nunits = { path = \"deps/units\" }\n")
            .Select(f => f.Item1 == "deps/geo/deps/units/lyric.toml" ? (f.Item1, "this is [not toml\n") : f).ToArray();
        var dir = Package(files);
        Assert.Equal("12 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A library builds as a check (07 B6): its modules through the front end, nothing
    /// built — and 'run' has no program there.</summary>
    [Fact]
    public void A_library_builds_as_a_check()
    {
        var dir = Package(("lyric.toml", Manifest("geo")), ("src/shapes.lyr", Shapes));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);

        File.WriteAllText(Path.Combine(dir, "src", "bad.lyr"), "pub fn broken(): int {\n    return \"x\";\n}\n");
        var (refused, _, why) = Run("build", "-C", dir);
        Assert.Equal(1, refused);
        Assert.Contains("error[LYR-SEM0001]", why);

        var (run, _, nothing) = Run("run", "-C", dir);
        Assert.Equal(2, run);
        Assert.Contains("error[LYR-CLI0005]: package 'geo' is a library", nothing);
    }

    /// <summary>The C is keyed by every package of the graph: a changed dependency builds anew.</summary>
    [Fact]
    public void A_changed_dependency_builds_anew()
    {
        var dir = Package(("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n")),
            ("src/main.lyr", AreaMain), ("deps/geo/lyric.toml", Manifest("geo")), ("deps/geo/src/shapes.lyr", Shapes));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
        File.WriteAllText(Path.Combine(dir, "deps", "geo", "src", "shapes.lyr"), Shapes.Replace("w * h", "w * h + 1"));
        Assert.Equal("13\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
