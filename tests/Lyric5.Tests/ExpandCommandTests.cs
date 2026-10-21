using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric expand</c> (design/v5/spec/13 M9b P8; 14 §1.1): the program's entry module after
/// its macros' expansion, as source on standard output - the results in the sites' places, the
/// rest as written, in the formatter's form.
/// </summary>
[Collection("console")]
public class ExpandCommandTests
{
    private const string Macros = """
        import std.syntax { Expr, Block, Stmt };

        pub macro twice(e: Expr): Expr {
            return quote { #{e} + #{e} };
        }

        pub macro unless(c: Expr, body: Block): Stmt[] {
            return quote { if (!#{c}) { #{body} } };
        }

        """;

    private static string Build(string main) =>
        Package(("lyric.toml", AppManifest), ("src/macros.lyr", Macros), ("src/main.lyr", main));

    /// <summary>The artifact (13 M9b S5): the module with its sites replaced, nothing built.</summary>
    [Fact]
    public void Expand_prints_the_module_with_its_sites_replaced()
    {
        var dir = Build("""
            import std.io { println };
            import app.macros { twice, unless };

            // a comment, which the formatter's form drops
            fn main(): void {
                let x = 3;
                println(twice!(x));
                unless!(x > 5) {
                    println("small");
                }
            }
            """);
        var (exit, output, error) = Run("expand", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("""
            import std.io { println };
            import app.macros { twice, unless };

            fn main(): void {
                let x = 3;
                println(x + x);
                if (!(x > 5)) {
                    println("small");
                }
            }

            """, output);
        Assert.False(Directory.Exists(Path.Combine(dir, "out")), "expand builds nothing");
    }

    [Fact]
    public void A_program_without_a_site_prints_as_it_is()
    {
        var dir = Build("import std.io { println };\n\nfn main(): void {\n    println(\"plain\");\n}\n");
        var (exit, output, error) = Run("expand", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("import std.io { println };\n\nfn main(): void {\n    println(\"plain\");\n}\n", output);
    }

    [Fact]
    public void What_the_expansion_needs_is_reported_and_the_rest_is_builds()
    {
        // a site of no macro: exit 1, as build says it
        var dir = Build("import std.io { println };\n\nfn main(): void {\n    println(nothing!(1));\n}\n");
        var (exit, output, error) = Run("expand", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("LYR-SEM0190", error);
        Assert.Equal("", output);
        // a type error after the expansion is build's: expand prints the tree
        var wrong = Build("import std.io { println };\nimport app.macros { twice };\n\nfn main(): void {\n    let n: int = twice!(\"s\");\n}\n");
        var (exit2, output2, error2) = Run("expand", "-C", wrong);
        Assert.True(exit2 == 0, error2);
        Assert.Contains("let n: int = \"s\" + \"s\";", output2);
    }

    [Fact]
    public void Expand_refuses_an_emission()
    {
        var dir = Build("fn main(): void { }\n");
        var (exit, _, error) = Run("expand", "-C", dir, "--emit", "ir");
        Assert.Equal(2, exit);
        // expand has no '--emit' in its table: the option is not one of its own
        Assert.Contains("unknown option '--emit' for 'expand'", error);
    }
}
