using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The driver's text form as written (spec 16 §1–§2): the arrow with the path relative to
/// the package — from a directory inside it too —, the gutter, <c>--absolute-paths</c>,
/// <c>--max-errors</c>, a <c>--color</c> it does not know, and a declaration that does not parse
/// saying nothing else.
/// </summary>
[Collection("console")]
public class TextFormTests
{
    private const string TwoMistakes = """
        fn main(): void {
            let x: int = "text";
            foo();
        }
        """;

    [Fact]
    public void The_place_is_said_relative_to_the_package_under_its_arrow()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", TwoMistakes));
        var (exit, _, error) = RunRaw("build", "-C", Path.Combine(root, "src"));
        Assert.Equal(1, exit);
        var arrow = $" --> {Path.Combine("src", "main.lyr")}:2:18\n";
        Assert.Contains("error[LYR-SEM0001]: cannot assign 'string' to 'int'\n" + arrow + "  |\n2 |     let x: int = \"text\";\n"
            + "  |            ---   ^^^^^^\n  |            |\n  |            expected 'int' here\n", error);
        Assert.Contains("error[LYR-SEM0002]: unknown identifier 'foo'\n", error);
    }

    [Fact]
    public void Absolute_paths_say_the_file_as_the_file_system_has_it()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", TwoMistakes));
        var (_, _, error) = RunRaw("build", "--absolute-paths", "-C", root);
        Assert.Contains($" --> {Path.Combine(root, "src", "main.lyr")}:2:18\n", error);
    }

    [Fact]
    public void After_the_limit_the_errors_are_counted_and_the_exit_stays()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", TwoMistakes));
        var (exit, _, error) = RunRaw("build", "-C", root, "--max-errors", "1");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0001]", error);
        Assert.DoesNotContain("error[LYR-SEM0002]", error);
        Assert.Contains("note: 1 more error not shown; '--max-errors 0' shows all", error);
    }

    [Fact]
    public void A_colour_mode_it_does_not_know_is_exit_2()
    {
        var (exit, _, error) = RunRaw("build", "--color", "sometimes");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0003]: '--color' takes auto, always or never, not 'sometimes'", error);
        Assert.Equal(2, RunRaw("build", "--max-errors", "many").Exit);
    }

    [Fact]
    public void Colour_always_paints_and_never_does_not()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", TwoMistakes));
        Assert.Contains("\u001b[1;91merror[LYR-SEM0001]", RunRaw("build", "--color", "always", "-C", root).Err);
        Assert.DoesNotContain("\u001b[", RunRaw("build", "--color", "never", "-C", root).Err);
    }

    [Fact]
    public void A_declaration_that_does_not_parse_says_its_syntax_error_alone()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            fn broken(): int {
                let a = ;
                return undefinedName;
            }

            fn main(): void {
                let b: int = "text";
            }
            """));
        var (exit, _, error) = RunRaw("build", "-C", root);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PAR0002]", error);
        Assert.Contains("error[LYR-SEM0001]", error);   // main parsed: its mistake is said
        Assert.DoesNotContain("undefinedName", error);
    }
}
