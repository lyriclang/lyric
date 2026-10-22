using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A mistake in how a program is written, said as the spelling to change (spec 16 §1 rule 2): a ';'
/// where a list separates with ',', a jump where a value stands, a loop over a string. Each is one
/// finding at its place with a safe fix, and the fixed program is clean.
/// </summary>
[Collection("console")]
public class SeparatorDiagnosticsTests
{
    private static readonly Regex Finding = new(@"error\[(?<code>LYR-[A-Z]+\d+)\][^\n]*\n\s*--> \S+?:(?<line>\d+):(?<col>\d+)");

    /// <summary>The errors of a check, each at its place, the fixed text, and the check after the fix.</summary>
    private static (List<(int Line, int Col, string Code)> Errors, string Fixed, int After) Run(string main)
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        var (_, _, said) = RunRaw("check", "-C", root, "--color", "never");
        var errors = Finding.Matches(said)
            .Select(m => (int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value), m.Groups["code"].Value))
            .ToList();
        var (fixedExit, _, fixError) = RunRaw("fix", "-C", root, "--color", "never");
        Assert.True(fixedExit == 0, fixError);
        var (after, _, _) = RunRaw("check", "-C", root, "--color", "never");
        return (errors, File.ReadAllText(Path.Combine(root, "src", "main.lyr")), after);
    }

    [Fact]
    public void A_field_that_ends_with_a_semicolon_is_one_finding_at_the_semicolon()
    {
        var (errors, fixedText, after) = Run("class Expression {\n    var nodes: int;\n    var depth: int,\n}\n\nfn main(): void {\n}\n");
        Assert.Equal([(2, 19, "LYR-PAR0064")], errors);
        Assert.Contains("    var nodes: int,\n", fixedText);
        Assert.Equal(0, after);
    }

    [Fact]
    public void An_initializer_with_semicolons_is_the_initializer_said_at_each_semicolon()
    {
        var (errors, fixedText, after) = Run("struct Pair {\n    a: int,\n    b: int,\n}\n\nfn main(): void {\n    let p = Pair { a = 1; b = 2; };\n    let _ = p;\n}\n");
        Assert.Equal([(7, 25, "LYR-PAR0064"), (7, 32, "LYR-PAR0064")], errors);
        Assert.Contains("    let p = Pair { a = 1, b = 2 };\n", fixedText);
        Assert.Equal(0, after);
    }

    [Fact]
    public void A_block_of_assignments_after_a_function_stays_its_trailing_lambda()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr",
            "import std.io { println };\n\nfn run(f: fn() -> void): void {\n    f();\n}\n\n"
            + "fn main(): void {\n    var total = 0;\n    run { total = 5; };\n    println(f\"{total}\");\n}\n"));
        Assert.Equal("5\n", BuildAndRun(root, "app", "build", "-C", root));
    }

    [Fact]
    public void A_jump_where_an_arm_value_stands_is_one_finding_and_becomes_a_block()
    {
        var (errors, fixedText, after) = Run("fn main(): void {\n    for (c in \"a b\".chars()) {\n        let n = match (c) {\n"
            + "            ' ' => continue,\n            _ => 1,\n        };\n        let _ = n;\n    }\n}\n");
        Assert.Equal([(4, 20, "LYR-PAR0065")], errors);
        Assert.Contains("            ' ' => { continue; },\n", fixedText);
        Assert.Equal(0, after);
    }

    [Fact]
    public void A_jump_in_a_branch_of_an_if_expression_becomes_a_block()
    {
        var (errors, fixedText, after) = Run("fn main(): void {\n    for (c in \"a b\".chars()) {\n"
            + "        let n = if (c == ' ') continue else 1;\n        let _ = n;\n    }\n}\n");
        Assert.Equal([(3, 31, "LYR-PAR0065")], errors);
        Assert.Contains("let n = if (c == ' ') { continue; } else 1;", fixedText);
        Assert.Equal(0, after);
    }

    [Fact]
    public void A_loop_over_a_string_is_refused_where_it_is_written_and_walks_its_chars_after_the_fix()
    {
        var (errors, fixedText, after) = Run("fn main(): void {\n    let line = \"ab\";\n    for (c in line) {\n        let _ = c;\n    }\n}\n");
        Assert.Equal([(3, 15, "LYR-SEM0232")], errors);
        Assert.Contains("    for (c in line.chars()) {\n", fixedText);
        Assert.Equal(0, after);
    }
}
