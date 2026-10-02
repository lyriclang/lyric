using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The prelude and one namespace per module (design/v5/spec/07 V3 I8, V6 K1, K3, K5, K6; 10 B2):
/// <c>std.prelude</c> is named without an import and the rest of the library is imported; a name
/// hiding the prelude's warns; a builtin type's name is no declaration's; a name is not also a
/// submodule; <c>std</c> is the library's. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class PreludeTests
{
    private static (int Exit, string Error) Check(string text, string file = "main.lyr")
    {
        var dir = Package((file, text));
        var (exit, _, error) = Run("build", Path.Combine(dir, file), "--emit", "ir");
        return (exit, error);
    }

    [Fact]
    public void The_prelude_is_named_without_an_import()
    {
        var text = """
            import std.io { println };

            fn check(e: Error): string {
                return e.message();
            }

            fn main(): void {
                assert(true);
                let b = Box<int> { value = 2 };
                let r = Range<int> { start = 0, end = b.value };
                println(f"{check(Exception { text = "x" })} {r.end}");
            }
            """;
        var dir = Package(("main.lyr", text));
        Assert.Equal("x 2\n", BuildAndRun(dir, "main", "build", Path.Combine(dir, "main.lyr")));
    }

    /// <summary>What the prelude does not pass on is imported (10 B2): the operator interfaces
    /// (Rust's std::ops), 'Any', 'sequence', 'arrayOf'.</summary>
    [Theory]
    [InlineData("fn take(x: Any): void {\n}\n\nfn main(): void {\n}\n", "Any")]
    [InlineData("struct P {\n    x: int,\n}\n\nextend P :: [Add] {\n    fn add(o: P): P {\n        return P { x = this.x + o.x };\n    }\n}\n\nfn main(): void {\n}\n", "Add")]
    [InlineData("fn main(): void {\n    let xs = arrayOf(3, (i: int) => i);\n}\n", "arrayOf")]
    public void What_the_prelude_does_not_carry_is_imported(string text, string name)
    {
        var (exit, error) = Check(text);
        Assert.True(exit == 1, error);
        Assert.Contains($"'{name}'", error);
        var imported = Check($"import std.core {{ {name} }};\n\n" + text);
        Assert.True(imported.Exit == 0, imported.Error);
    }

    [Theory]
    [InlineData("import std.io { println };\n\nfn assert(x: bool): void {\n    println(\"mine\");\n}\n\nfn main(): void {\n    assert(true);\n}\n", "assert")]
    [InlineData("import std.io { println as todo };\n\nfn main(): void {\n    todo(\"x\");\n}\n", "todo")]
    [InlineData("struct Box {\n    n: int,\n}\n\nfn main(): void {\n    let b = Box { n = 1 };\n}\n", "Box")]
    public void A_name_hiding_the_preludes_warns(string text, string name)
    {
        var (exit, error) = Check(text);
        Assert.True(exit == 0, error);
        Assert.Contains($"warning[LYR-SEM0153]: '{name}' hides the prelude's '{name}' in this module", error);
    }

    [Theory]
    [InlineData("struct int {\n    x: int,\n}\n", "int")]
    [InlineData("fn string(): int {\n    return 1;\n}\n", "string")]
    [InlineData("type bool = int;\n", "bool")]
    public void A_builtin_type_name_is_no_declarations(string declaration, string name)
    {
        var (exit, error) = Check(declaration + "\nfn main(): void {\n}\n");
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-RES0011]: '{name}' is a builtin type — no declaration takes its name", error);
        Assert.True(error.Split("error[").Length == 2, $"one error —\n{error}");
    }

    [Fact]
    public void A_name_is_not_also_a_submodule()
    {
        var main = "import std.io { println };\nimport app.net;\nimport app.net.http;\n\nfn main(): void {\n    println(f\"{net.http()} {http.get()}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/net.lyr", "pub fn http(): int {\n    return 1;\n}\n"),
            ("src/net/http.lyr", "pub fn get(): int {\n    return 2;\n}\n"), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0012]: 'http' in module 'app.net' is also the module 'app.net.http'", error);
    }

    [Fact]
    public void A_single_file_named_std_is_refused()
    {
        var (exit, error) = Check("fn main(): void {\n}\n", "std.lyr");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0013]: 'std' is the standard library's name", error);
    }

    /// <summary>A desugared operator calls std.core's helper whatever the program names — a
    /// function of the program called 'equalOptionals' once took '==' on optionals over.</summary>
    [Fact]
    public void An_operators_helper_is_the_compilers()
    {
        var text = """
            import std.io { println };

            fn equalOptionals(a: int, b: int): bool {
                return false;
            }

            fn main(): void {
                let a: ?int = 1;
                let b: ?int = 1;
                println(f"{a == b}");
            }
            """;
        var dir = Package(("main.lyr", text));
        Assert.Equal("true\n", BuildAndRun(dir, "main", "build", Path.Combine(dir, "main.lyr")));
    }
}
