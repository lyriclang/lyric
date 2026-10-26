using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric fmt</c>: the package's Lyric files — or the files named — in the one form; <c>--check</c>
/// writes nothing and fails where a file would change; a file that does not parse is left as it is.
/// </summary>
[Collection("console")]
public class FmtCommandTests
{
    private const string Loose = "fn main(): void { let x = 1; if (x > 0) { return; } }\n";

    private const string Formed = """
        fn main(): void {
            let x = 1;
            if (x > 0) {
                return;
            }
        }

        """;

    [Fact]
    public void Fmt_puts_the_package_in_the_form_and_a_second_run_changes_nothing()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Loose), ("tests/one.lyr", Loose), ("build.lyr", Loose));

        var (exit, output, _) = RunRaw("fmt", "-C", root);
        Assert.Equal(0, exit);
        foreach (var file in new[] { "src/main.lyr", "tests/one.lyr", "build.lyr" })
        {
            Assert.Equal(Formed, File.ReadAllText(Path.Combine(root, file)));
            Assert.Contains($"formatted {file}", output);
        }

        var written = File.GetLastWriteTimeUtc(Path.Combine(root, "src/main.lyr"));
        var (again, said, _) = RunRaw("fmt", "-C", root);
        Assert.Equal(0, again);
        Assert.Equal("", said);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(root, "src/main.lyr")));
    }

    /// <summary>An extern block stays a block: its head once, a function with a visibility of its
    /// own keeps it; C's variadic arguments stay after the last parameter.</summary>
    [Fact]
    public void Fmt_keeps_an_extern_block()
    {
        const string loose = "pub extern \"C\" {fn abs(x: int32): int32;\n  private fn labs(x: int): int = \"labs\";\nfn printf(f: CStr,...): int32;}\n\nfn main(): void {\n}\n";
        const string formed = """
            pub extern "C" {
                fn abs(x: int32): int32;
                private fn labs(x: int): int = "labs";
                fn printf(f: CStr, ...): int32;
            }

            fn main(): void { }

            """;
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", loose));
        Assert.Equal(0, RunRaw("fmt", "-C", root).Exit);
        Assert.Equal(formed, File.ReadAllText(Path.Combine(root, "src/main.lyr")));
    }

    [Fact]
    public void Check_names_what_would_change_writes_nothing_and_fails_until_it_is_formed()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Loose), ("src/kept.lyr", Formed));

        var (exit, output, error) = RunRaw("fmt", "-C", root, "--check");
        Assert.Equal(1, exit);
        Assert.Equal("src/main.lyr\n", output.ReplaceLineEndings("\n"));
        Assert.Contains("1 file is not in the form", error);
        Assert.Equal(Loose, File.ReadAllText(Path.Combine(root, "src/main.lyr")));

        var (diffExit, diff, _) = RunRaw("fmt", "-C", root, "--check", "--diff");
        Assert.Equal(1, diffExit);
        Assert.Contains("--- a/src/main.lyr", diff);
        Assert.Contains("+    let x = 1;", diff);

        Assert.Equal(0, RunRaw("fmt", "-C", root).Exit);
        Assert.Equal(0, RunRaw("fmt", "-C", root, "--check").Exit);
    }

    [Fact]
    public void A_file_that_does_not_parse_is_left_as_it_is_and_said()
    {
        const string broken = "fn main(): void { let x = ; }\n";
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", broken), ("src/other.lyr", Loose));

        var (exit, _, error) = RunRaw("fmt", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PAR", error);
        Assert.Contains("src/main.lyr: not formatted — it does not parse", error);
        Assert.Equal(broken, File.ReadAllText(Path.Combine(root, "src/main.lyr")));
        Assert.Equal(Formed, File.ReadAllText(Path.Combine(root, "src/other.lyr")));
    }

    [Fact]
    public void Named_files_are_formatted_alone_without_a_package()
    {
        var root = Package(("loose.lyr", Loose), ("left.lyr", Loose));

        var (exit, _, _) = RunRaw("fmt", "-C", root, "loose.lyr");
        Assert.Equal(0, exit);
        Assert.Equal(Formed, File.ReadAllText(Path.Combine(root, "loose.lyr")));
        Assert.Equal(Loose, File.ReadAllText(Path.Combine(root, "left.lyr")));
    }

    [Fact]
    public void Without_a_package_or_a_file_fmt_says_where_it_looked()
    {
        var root = Package(("notes.txt", "nothing here"));

        var (exit, _, error) = RunRaw("fmt", "-C", root);
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0004", error);
        Assert.Contains("lyric5 fmt <file.lyr>", error);
    }

    [Fact]
    public void Diff_goes_with_check()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Loose));

        var (exit, _, error) = RunRaw("fmt", "-C", root, "--diff");
        Assert.NotEqual(0, exit);
        Assert.Contains("'--diff' goes with '--check'", error);
        Assert.Equal(Loose, File.ReadAllText(Path.Combine(root, "src/main.lyr")));
    }

    [Fact]
    public void Fmt_writes_lf_line_ends()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Loose.Replace("\n", "\r\n")));

        Assert.Equal(0, RunRaw("fmt", "-C", root).Exit);
        Assert.DoesNotContain("\r", File.ReadAllText(Path.Combine(root, "src/main.lyr")));
    }

    [Fact]
    public void The_help_names_the_verb_and_its_options()
    {
        var (exit, output, _) = RunRaw("fmt", "--help");
        Assert.Equal(0, exit);
        Assert.Contains("usage: lyric5 fmt [<file.lyr>…]", output);
        Assert.Contains("--check", output);
        Assert.Contains("--diff", output);
    }
}
