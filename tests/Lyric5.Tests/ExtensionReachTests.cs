using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Where an <c>extend</c> block of another module than its type's adds members: where its module is
/// imported, or passed on by a <c>pub import</c> — a chain of them, as a name travels. Two such
/// modules seen at one call that both add the name are an error (<c>LYR-SEM0235</c>); before, the
/// first import won and the second was called unused. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class ExtensionReachTests
{
    private const string Loud = "pub extend string {\n    pub fn shout(): string {\n        return this + \"!\";\n    }\n}\n\npub fn tag(): int {\n    return 1;\n}\n";

    [Fact]
    public void A_pub_import_passes_the_extensions_on_through_a_chain()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/loud.lyr", Loud),
            ("src/near.lyr", "pub import app.loud { tag };\n"),
            ("src/far.lyr", "pub import app.near { tag };\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.far { tag };\n\nfn main(): void {\n    println(f\"{\"hey\".shout()} {tag()}\");\n}\n"));
        Assert.Equal("hey! 1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void An_import_that_is_not_passed_on_keeps_the_extensions_to_itself()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/loud.lyr", Loud),
            ("src/near.lyr", "import app.loud { tag };\n\npub fn twice(): int {\n    return tag() * 2;\n}\n"),
            ("src/main.lyr", "import app.near { twice };\n\nfn main(): void {\n    let _ = \"hey\".shout();\n    let _ = twice();\n}\n"));
        var (exit, _, error) = Run("check", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]: 'string' has no member 'shout'", error);
        Assert.Contains("which this module does not import", error);
    }

    [Fact]
    public void A_cycle_of_re_exports_ends()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/a.lyr", "pub import app.b { two };\n\npub fn one(): int {\n    return 1;\n}\n"),
            ("src/b.lyr", "pub import app.a { one };\n\npub fn two(): int {\n    return 2;\n}\n"),
            ("src/main.lyr", "import app.a { one, two };\n\nfn main(): void {\n    let _ = \"hey\".shout();\n    let _ = one() + two();\n}\n"));
        var (exit, _, error) = Run("check", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]", error);
    }

    [Fact]
    public void Two_seen_modules_adding_the_name_are_an_error_that_names_both()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/loud.lyr", Loud),
            ("src/soft.lyr", "pub extend string {\n    pub fn shout(): string {\n        return this + \"?\";\n    }\n}\n"),
            ("src/main.lyr", "import app.loud;\nimport app.soft;\n\nfn main(): void {\n    let _ = \"hey\".shout();\n}\n"));
        var (exit, _, error) = Run("check", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0235]: 'shout' is added to 'string' by 'app.loud' and by 'app.soft', both seen here", error);
    }

    /// <summary>One of them seen, the other not: no conflict — the module that adds the name is the
    /// one imported.</summary>
    [Fact]
    public void Two_modules_adding_the_name_conflict_only_where_both_are_seen()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/loud.lyr", Loud),
            ("src/soft.lyr", "pub extend string {\n    pub fn shout(): string {\n        return this + \"?\";\n    }\n}\n\npub fn quiet(): string {\n    return \"hey\".shout();\n}\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.loud { tag };\nimport app.soft { quiet };\n\nfn main(): void {\n    println(f\"{tag()} {quiet()}\");\n}\n"));
        Assert.Equal("1 hey?\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
