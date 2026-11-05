using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The notes on a failed lookup (spec 04 §6): a module path a typo away from one there is; a local that
/// hides a module or a prelude name, where the member is the hidden one's; a method a block adds in a
/// module this one does not import.
/// </summary>
[Collection("console")]
public class LookupNoteTests
{
    private static string Check(params (string Path, string Text)[] files)
    {
        var root = Package([("lyric.toml", AppManifest), .. files]);
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        return error;
    }

    [Fact]
    public void A_module_path_one_letter_off_gets_the_path_there_is()
    {
        var error = Check(("src/main.lyr", "import std.colections { List };\n\nfn main(): void {\n}\n"));
        Assert.Contains("error[LYR-RES0003]: cannot find module 'std.colections'", error);
        Assert.Contains("help: did you mean 'std.collections'?", error);
        Assert.Contains("import std.collections { List };", error);
    }

    [Fact]
    public void A_local_that_hides_a_module_is_said_where_it_bites()
    {
        var error = Check(("src/main.lyr", "import std.path;\n\nfn main(): void {\n    let path = \"a/b\";\n    let _ = path.join(\"c\");\n}\n"));
        Assert.Contains("error[LYR-SEM0012]: 'string' has no member 'join'", error);
        Assert.Contains("note: 'path' is here the local of type 'string', which hides the module 'std.path'", error);
    }

    [Fact]
    public void A_local_that_hides_a_prelude_name_is_said_where_it_bites()
    {
        var error = Check(("src/main.lyr", "fn main(): void {\n    let List = 3;\n    let _ = List.new();\n}\n"));
        Assert.Contains("note: 'List' is here the local of type 'int', which hides the prelude's type 'List'", error);
    }

    [Fact]
    public void A_method_a_module_not_imported_adds_is_named()
    {
        var error = Check(
            ("src/shapes.lyr", "pub struct Point {\n    pub x: int,\n}\n"),
            ("src/extra.lyr", "import app.shapes { Point };\n\npub extend Point {\n    pub fn norm(): int {\n        return this.x;\n    }\n}\n"),
            ("src/helper.lyr", "import app.extra;\nimport app.shapes { Point };\n\npub fn normOf(p: Point): int {\n    return p.norm();\n}\n"),
            ("src/main.lyr", "import app.helper { normOf };\nimport app.shapes { Point };\n\nfn main(): void {\n    let p = Point { x = 1 };\n    let _ = normOf(p);\n    let _ = p.norm();\n}\n"));
        Assert.Contains("error[LYR-SEM0012]: 'Point' has no member 'norm'", error);
        Assert.Contains("note: 'norm' is added to 'Point' in module 'app.extra', which this module does not import", error);
    }

    private const string WriteAll = "pub fn write(f: File, b: uint8[]): void throws Error {\n    try f.writeAll(b);\n}\n\nfn main(): void {\n}\n";

    [Fact]
    public void A_method_a_blanket_block_adds_names_the_module_to_import()
    {
        var error = Check(("src/main.lyr", "import std.fs { File };\n\n" + WriteAll));
        Assert.Contains("error[LYR-SEM0012]: 'File' has no member 'writeAll'", error);
        Assert.Contains("help: 'writeAll' is added by a block in module 'std.io', which this module does not import — write 'import std.io;'", error);
    }

    [Fact]
    public void The_import_the_help_names_gives_the_method()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "import std.fs { File };\nimport std.io;\n\n" + WriteAll));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM0012", error);
    }

    [Fact]
    public void A_receiver_the_blanket_block_does_not_reach_is_not_sent_there()
    {
        var error = Check(("src/main.lyr", "import std.fs { File };\n\nfn main(): void {\n    let _ = File.exists;\n    let n = 1;\n    n.writeAll();\n}\n"));
        Assert.Contains("error[LYR-SEM0012]: 'int' has no member 'writeAll'", error);
        Assert.DoesNotContain("module 'std.io'", error);
    }

    [Fact]
    public void A_method_a_block_on_a_shape_adds_names_the_module_to_import()
    {
        var error = Check(
            ("src/arrays.lyr", "pub extend<T> T[] {\n    pub fn second(): T {\n        return this[1];\n    }\n}\n"),
            ("src/helper.lyr", "import app.arrays;\n\npub fn two(): int {\n    return [1, 2].second();\n}\n"),
            ("src/main.lyr", "import app.helper { two };\n\nfn main(): void {\n    let _ = two();\n    let _ = [1, 2].second();\n}\n"));
        Assert.Contains("error[LYR-SEM0012]: 'int[]' has no member 'second'", error);
        Assert.Contains("help: 'second' is added by a block in module 'app.arrays', which this module does not import — write 'import app.arrays;'", error);
    }

    [Fact]
    public void A_method_no_import_would_give_is_not_offered()
    {
        var error = Check(
            ("src/wrap.lyr", "pub interface Tagged {\n    fn tag(): int;\n}\n\nextend<T :: [Tagged]> T {\n    private fn hiddenTwice(): int {\n        return this.tag() * 2;\n    }\n}\n\npub struct Box {\n    pub n: int,\n}\n\nextend Box :: [Tagged] {\n    fn tag(): int {\n        return this.n;\n    }\n}\n\npub fn box(): Box {\n    return Box { n = 1 };\n}\n"),
            ("src/helper.lyr", "import app.wrap;\n\npub fn made(): wrap.Box {\n    return wrap.box();\n}\n"),
            ("src/main.lyr", "import app.helper { made };\n\nfn main(): void {\n    let _ = made().hiddenTwice();\n}\n"));
        Assert.Contains("error[LYR-SEM0012]", error);
        Assert.DoesNotContain("write 'import", error);
    }

    private const string Gauge = "pub struct Gauge {\n    pub v: int,\n\n    pub fn read(): int {\n        return this.v;\n    }\n\n"
        + "    private fn read(scale: int): int {\n        return this.v * scale;\n    }\n}\n\n"
        + "pub fn tick(): int {\n    return 1;\n}\n\nprivate fn tick(n: int): int {\n    return n;\n}\n";

    [Fact]
    public void A_count_only_a_private_member_takes_is_named()
    {
        var error = Check(("src/util.lyr", Gauge),
            ("src/main.lyr", "import app.util { Gauge };\n\nfn main(): void {\n    let g = Gauge { v = 2 };\n    let _ = g.read(3);\n}\n"));
        Assert.Contains("error[LYR-SEM0014]: call expects 0 argument(s), got 1", error);
        Assert.Contains("note: the one that takes 1 is not for this module: 'read' is private to module 'app.util'", error);
    }

    [Fact]
    public void A_count_only_a_private_overload_of_an_import_takes_is_named()
    {
        var error = Check(("src/util.lyr", Gauge),
            ("src/main.lyr", "import app.util { tick };\n\nfn main(): void {\n    let _ = tick(4);\n}\n"));
        Assert.Contains("error[LYR-SEM0014]: call expects 0 argument(s), got 1", error);
        Assert.Contains("note: the one that takes 1 is not for this module: 'tick' is private to module 'app.util'", error);
    }
}
