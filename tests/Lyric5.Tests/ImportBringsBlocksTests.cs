using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// An import brings its module's blocks along with its names (spec 04 §3 rule 9a, §4 rule 3): what
/// <c>LYR-SEM0072</c> advises about an import none of whose names is used, and that
/// <c>lyric fix</c>, which takes the advice, leaves a program that still compiles.
/// </summary>
[Collection("console")]
public class ImportBringsBlocksTests
{
    private const string WriteAll =
        "\npub fn write(f: File, b: uint8[]): void throws Error {\n    try f.writeAll(b);\n}\n\nfn main(): void {\n}\n";

    private const string Loud = "pub extend string {\n    pub fn shout(): string {\n        return this + \"!\";\n    }\n}\n\npub fn tag(): int {\n    return 1;\n}\n";

    private const string Conformance =
        "pub struct T {\n    pub n: int,\n}\n\npub interface I {\n    fn who(): int;\n}\n\npub fn ask<U :: [I]>(u: U): int {\n    return u.who();\n}\n";

    /// <summary>The warning before the fix, the file after it; the program compiles and warns of no
    /// import any more.</summary>
    private static (string Before, string After) Fix(params (string Path, string Text)[] files)
    {
        var dir = Package([("lyric.toml", AppManifest), .. files]);
        var (exit, _, before) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(exit == 0, before);
        var (fixedExit, _, fixing) = RunRaw("fix", "-C", dir, "--color", "never");
        Assert.True(fixedExit == 0, fixing);
        var (afterExit, _, after) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(afterExit == 0, after);
        Assert.DoesNotContain("LYR-SEM0072", after);
        return (before, File.ReadAllText(Path.Combine(dir, "src", "main.lyr")));
    }

    [Fact]
    public void A_selective_import_whose_blocks_the_file_calls_keeps_the_module()
    {
        var (before, after) = Fix(("src/main.lyr", "import std.fs { File };\nimport std.io { Writer };\n" + WriteAll));
        Assert.Contains("warning[LYR-SEM0072]: import 'Writer' is never used", before);
        Assert.Contains("help: keep the module alone — it brings 'writeAll', which the file calls", before);
        Assert.Contains("import std.io;\n", after);
        Assert.DoesNotContain("Writer", after);
    }

    [Theory]
    [InlineData("import std.io;")]
    [InlineData("import std.io as output;")]
    public void A_module_import_whose_blocks_the_file_calls_is_used(string import)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "import std.fs { File };\n" + import + "\n" + WriteAll));
        var (exit, _, error) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM0072", error);
    }

    [Fact]
    public void A_name_used_beside_it_leaves_the_module_and_takes_the_other_out()
    {
        var (before, after) = Fix(("src/main.lyr",
            "import std.fs { File };\nimport std.io { Writer, println };\n\npub fn write(f: File, b: uint8[]): void throws Error {\n    try f.writeAll(b);\n}\n\nfn main(): void {\n    println(\"x\");\n}\n"));
        Assert.Contains("help: take it out", before);
        Assert.Contains("import std.io { println };\n", after);
    }

    [Fact]
    public void Blocks_a_pub_import_passes_on_keep_the_import_that_brings_them()
    {
        var (before, after) = Fix(("src/loud.lyr", Loud), ("src/near.lyr", "pub import app.loud { tag };\n"),
            ("src/main.lyr", "import app.near { tag };\n\nfn main(): void {\n    let _ = \"hey\".shout();\n}\n"));
        Assert.Contains("help: keep the module alone — it brings 'shout', which the file calls", before);
        Assert.Contains("import app.near;\n", after);
    }

    [Fact]
    public void Another_import_of_the_module_lets_this_one_go()
    {
        var (before, after) = Fix(("src/main.lyr", "import std.fs { File };\nimport std.io;\nimport std.io { Writer };\n" + WriteAll));
        Assert.Contains("help: take it out", before);
        Assert.DoesNotContain("keep the module alone", before);
        Assert.DoesNotContain("Writer", after);
        Assert.Contains("import std.io;\n", after);
    }

    [Fact]
    public void Of_two_imports_that_bring_the_blocks_one_stays()
    {
        var (_, after) = Fix(("src/main.lyr", "import std.fs { File };\nimport std.io { Writer };\nimport std.io { Reader };\n" + WriteAll));
        Assert.DoesNotContain("Writer", after);
        Assert.DoesNotContain("Reader", after);
        Assert.Contains("import std.io;\n", after);
    }

    [Fact]
    public void What_the_prelude_brings_needs_no_import()
    {
        var (before, after) = Fix(("src/main.lyr",
            "import std.string { StringBuilder };\n\nfn main(): void {\n    let _ = \"x\".toUpper();\n}\n"));
        Assert.Contains("help: take it out", before);
        Assert.DoesNotContain("keep the module alone", before);
        Assert.DoesNotContain("import", after);
    }

    private static (string Path, string Text)[] OrphanConformance(string main) =>
    [
        ("src/t.lyr", Conformance),
        ("src/m.lyr", "import app.t { T, I };\n\npub struct X {\n    pub n: int,\n}\n\nextend T :: [I] {\n    fn who(): int {\n        return this.n;\n    }\n}\n"),
        ("src/main.lyr", main),
    ];

    [Fact]
    public void A_conformance_only_this_import_loads_is_not_taken_out_unread()
    {
        var dir = Package([("lyric.toml", AppManifest), .. OrphanConformance(
            "import app.m { X };\nimport app.t { T, ask };\n\nfn main(): void {\n    let _ = ask(T { n = 1 });\n}\n")]);
        var (exit, _, before) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(exit == 0, before);
        Assert.Contains("help: take it out, unless the program needs a conformance the module gives", before);
        var (fixedExit, _, fixing) = RunRaw("fix", "-C", dir, "--color", "never");
        Assert.True(fixedExit == 0, fixing);
        var (afterExit, _, after) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(afterExit == 0, after);
        Assert.Contains("import app.m { X };", File.ReadAllText(Path.Combine(dir, "src", "main.lyr")));
    }

    [Fact]
    public void A_conformance_of_the_module_own_type_goes_with_its_type()
    {
        var (before, after) = Fix(
            ("src/m.lyr", "pub struct X {\n    pub n: int,\n}\n\nextend X :: [Default] {\n    static fn default(): X {\n        return X { n = 0 };\n    }\n}\n"),
            ("src/main.lyr", "import app.m { X };\n\nfn main(): void {\n}\n"));
        Assert.Contains("help: take it out", before);
        Assert.DoesNotContain("unless the program needs", before);
        Assert.DoesNotContain("import", after);
    }
}
