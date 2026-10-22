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
