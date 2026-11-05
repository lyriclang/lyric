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

    private const string Marks = """
        enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }
        struct Box {
            v: int,
            static fn make(): int { return 1; }
        }
        fn safe(): int { return 1; }
        fn take(x: int): int { return x; }
        fn each<E :: [Error]>(f: fn() -> int throws E): int throws E { return try f(); }

        fn main(): void throws Error {
            let b = Box { v = 1 };

        """;

    private static (int Errors, int Warnings) MarksOver(string body, string error)
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Marks + "    " + body + "\n}\n"));
        var (exit, _, text) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(error == "" ? 0 : 1, exit);
        return (error == "" ? 0 : Regex.Matches(text, $@"^error\[{error}\]", RegexOptions.Multiline).Count,
            Regex.Matches(text, @"^warning\[LYR-SEM0139\]", RegexOptions.Multiline).Count);
    }

    /// <summary>What was refused may throw anything once it is fixed, so no 'try' over it is told
    /// that nothing throws: the error says the one thing there is, under every form of the mark.</summary>
    [Theory]
    [InlineData("try b.nope();", "LYR-SEM0012")]
    [InlineData("try nope();", "LYR-SEM0002")]
    [InlineData("try nope.x();", "LYR-SEM0002")]
    [InlineData("let _ = try? b.nope();", "LYR-SEM0012")]
    [InlineData("let _ = try! b.nope();", "LYR-SEM0012")]
    [InlineData("let _ = try b.nope() catch (_: Error) { 0 };", "LYR-SEM0012")]
    [InlineData("try { b.nope(); } catch (_: Error) { }", "LYR-SEM0012")]
    [InlineData("let _ = try take(b.nope());", "LYR-SEM0012")]
    [InlineData("let _ = try 1 + b.nope();", "LYR-SEM0012")]
    [InlineData("let _ = 1 + try b.nope();", "LYR-SEM0012")]
    [InlineData("let _ = 1 + try 2 + b.nope();", "LYR-SEM0012")]
    [InlineData("for (x in try b.nope()) { let _ = x; }", "LYR-SEM0012")]
    [InlineData("let _ = try each(() => b.nope());", "LYR-SEM0012")]
    public void A_mark_over_what_was_refused_is_not_warned_about(string body, string error) =>
        Assert.Equal((1, 0), MarksOver(body, error));

    /// <summary>The call is fine and its set is what was refused: the type its clause names, as a
    /// function and as a method, or the one written at the call.</summary>
    [Theory]
    [InlineData("let _ = try broken();")]
    [InlineData("let _ = try Holder { v = 1 }.broken();")]
    [InlineData("let _ = try each<Nope>(() => 1);")]
    public void A_mark_over_a_call_whose_set_was_refused_is_not_warned_about(string body)
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", $$"""
            struct Holder {
                v: int,
                fn broken(): int throws Nope { return 1; }
            }
            fn broken(): int throws Nope { return 1; }
            fn each<E :: [Error]>(f: fn() -> int throws E): int throws E { return try f(); }

            fn main(): void throws Error {
                {{body}}
            }

            """));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        Assert.Contains("'Nope'", error);
        Assert.DoesNotContain("LYR-SEM0139", error);
    }

    /// <summary>The call the report was about, through the library: the member comes with an import
    /// the program lacks, and the call throws once it is there.</summary>
    [Fact]
    public void A_mark_over_a_member_that_wants_an_import_is_not_warned_about()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.fs { File };

            fn main(): void throws Error {
                let f = try File.create("out.txt");
                let b: uint8[] = [1];
                try f.writeAll(b[..]);
            }

            """));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]", error);
        Assert.DoesNotContain("LYR-SEM0139", error);
    }

    /// <summary>What a compile-time round reads carries the error type in a program that is fine,
    /// which keeps its warning: only an error that is out makes a mark over it unknown.</summary>
    [Fact]
    public void A_program_without_errors_keeps_its_warning_over_a_compile_time_read()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.meta { fields };

            interface Z {
                fn z(): int {
                    comptime for (f in fields<Self>()) {
                        let _ = try this.[f];
                    }
                    return 0;
                }
            }

            struct P :: [Z] {
                a: int,
            }

            fn main(): void {
                let _ = P { a = 1 }.z();
            }

            """));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(0, exit);
        Assert.Single(Regex.Matches(error, @"^warning\[LYR-SEM0139\]", RegexOptions.Multiline));
    }

    /// <summary>A mark over nothing stays one: beside a refused call, left of one, and over names
    /// that have no type of their own. A second mark over a refused call is one warning, its own.</summary>
    [Theory]
    [InlineData("let _ = try safe();", "")]
    [InlineData("let xs = [1, 2]; b.nope(); let _ = try xs[1..];", "LYR-SEM0012")]
    [InlineData("let _ = try Box.make();", "")]
    [InlineData("let _ = try Oops.Bad.message();", "")]
    [InlineData("let _ = 1 + try safe() + 2;", "")]
    [InlineData("let _ = try each(() => safe());", "")]
    [InlineData("b.nope(); let _ = try safe();", "LYR-SEM0012")]
    [InlineData("b.nope(); let _ = try? safe();", "LYR-SEM0012")]
    [InlineData("b.nope(); try { let _ = safe(); } catch (_: Error) { }", "LYR-SEM0012")]
    [InlineData("let _ = b.nope() + try safe();", "LYR-SEM0012")]
    [InlineData("try { let _ = try b.nope(); } catch (_: Error) { }", "LYR-SEM0012")]
    public void A_mark_over_nothing_is_warned_about_whatever_else_is_wrong(string body, string error)
    {
        var (errors, warnings) = MarksOver(body, error);
        Assert.Equal(error == "" ? 0 : 1, errors);
        Assert.Equal(1, warnings);
    }

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
