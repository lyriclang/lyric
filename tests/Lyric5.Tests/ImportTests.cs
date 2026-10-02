using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Imports (design/v5/spec/07 V3): the three forms, a selective item renamed (I2), <c>pub import</c>
/// passing on what it binds (I4), and one rule for an import never used (I5). Through <c>Main</c>,
/// so in the console collection.
/// </summary>
[Collection("console")]
public class ImportTests
{
    private const string Util = "pub fn answer(): int {\n    return 42;\n}\n\nfn helper(): int {\n    return 1;\n}\n";

    [Fact]
    public void A_selective_item_is_renamed()
    {
        var dir = Package(("main.lyr", "import std.io { println as say };\n\nfn main(): void {\n    say(\"hi\");\n}\n"));
        Assert.Equal("hi\n", BuildAndRun(dir, "main", "build", Path.Combine(dir, "main.lyr")));
    }

    [Fact]
    public void A_renamed_item_is_bound_under_its_new_name_alone()
    {
        var dir = Package(("main.lyr", "import std.io { println as say };\n\nfn main(): void {\n    println(\"hi\");\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0002]: unknown identifier 'println'", error);
    }

    /// <summary>What a 'pub import' binds is a pub member of its module: imported from there,
    /// named through it, renamed as it was renamed — the curated surface (I4).</summary>
    [Fact]
    public void A_pub_import_passes_a_name_on()
    {
        var facade = "pub import app.util { answer };\npub import app.util { answer as reply };\n";
        var main = """
            import std.io { println };
            import app.facade;
            import app.facade { answer };

            fn main(): void {
                println(f"{answer()} {facade.answer()} {facade.reply()}");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/facade.lyr", facade), ("src/main.lyr", main));
        Assert.Equal("42 42 42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_pub_import_passes_a_module_on()
    {
        var main = "import std.io { println };\nimport app.facade;\n\nfn main(): void {\n    println(f\"{facade.util.answer()}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/facade.lyr", "pub import app.util;\n"),
            ("src/main.lyr", main));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_pub_import_passes_on_what_is_pub()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/facade.lyr", "pub import app.util { helper };\n"),
            ("src/main.lyr", "import app.facade;\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0010]: 'helper' is not pub, and 'pub import' passes on what is pub", error);
    }

    /// <summary>One rule for every form (I5): the qualified form warned never before.</summary>
    [Theory]
    [InlineData("import std.io;", "io")]
    [InlineData("import std.io as console;", "console")]
    [InlineData("import std.io { println };", "println")]
    public void An_import_never_used_warns_in_every_form(string import, string name)
    {
        var dir = Package(("main.lyr", import + "\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains($"warning[LYR-SEM0072]: import '{name}' is never used", error);
    }

    [Fact]
    public void A_pub_import_is_used_by_being_passed_on()
    {
        var main = "import std.io { println };\nimport app.facade;\n\nfn main(): void {\n    println(f\"{facade.answer()}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util),
            ("src/facade.lyr", "pub import app.util { answer };\n"), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM0072", error);
    }

    [Fact]
    public void An_import_takes_pub_or_no_word()
    {
        var dir = Package(("main.lyr", "internal import std.io { println };\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PAR0053]: an import is its module's own — 'pub import' passes it on", error);
    }
}
