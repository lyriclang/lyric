using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A mistake is said once (spec 16 §2 rule 2), counted here where the suite cannot count: the suite's
/// runner compares the codes as a set, and these cascades said one code twice.
/// </summary>
[Collection("console")]
public class CascadeTests
{
    private static int Count(string program, string code)
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", program));
        var (exit, _, error) = RunRaw("build", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        return Regex.Matches(error, $@"^error\[{code}\]", RegexOptions.Multiline).Count;
    }

    [Fact]
    public void Type_arguments_the_annotation_got_wrong_are_said_there_alone() =>
        Assert.Equal(1, Count("struct Cell<T> {\n    v: T,\n}\n\nfn main(): void {\n    let b: Cell<int, int> = Cell { v = 1 };\n    let _ = b;\n}\n", "LYR-SEM0026"));

    [Fact]
    public void An_alternative_that_binds_other_names_is_one_error() =>
        Assert.Equal(1, Count("enum E {\n    A(int),\n    B(int),\n}\n\nfn main(): void {\n    let e = E.A(1);\n    match (e) {\n        .A(x) | .B(y) => { let _ = 1; },\n    }\n}\n", "LYR-SEM0032"));

    [Fact]
    public void A_compile_time_only_call_is_said_where_the_program_makes_it() =>
        Assert.Equal(1, Count("import std.syntax { parseExpr };\n\nfn main(): void {\n    let _ = parseExpr(\"1 + 2\").toSource();\n}\n", "LYR-CG0002"));

    /// <summary>The initializer written with ':' is one error, and its fix writes '='.</summary>
    [Fact]
    public void An_initializer_with_colons_is_fixed_to_one_with_equals()
    {
        var root = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", "struct Point {\n    x: int,\n}\n\nfn main(): void {\n    let p = Point { x: 1 };\n    let _ = p;\n}\n"));
        Assert.Equal(1, RunRaw("check", "-C", root).Exit);
        Assert.Equal(0, RunRaw("fix", "-C", root).Exit);
        Assert.Contains("let p = Point { x = 1 };", File.ReadAllText(Path.Combine(root, "src", "main.lyr")));
    }
}
