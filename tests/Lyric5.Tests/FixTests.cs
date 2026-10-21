using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric fix</c> (spec 16 §6): the safe changes of the findings in the package's own files, pass
/// after pass until none is left; what needs a reader stays, and a second run changes nothing.
/// </summary>
[Collection("console")]
public class FixTests
{
    private const string Manifest = AppManifest + "\n[lints]\nwarn = [\"needless-return\"]\n";

    private const string Before = """
        import std.io { println, print };

        @Deprecated { message = "renamed", replacement = "twice" }
        fn double(x: int): int {
            return x * 2;
        }

        fn twice(x: int): int {
            return x * 2;
        }

        fn pure(): int {
            return 1;
        }

        fn main(): void {
            var total = double(2);
            var count = 0;
            count = count++;
            let my_val = try pure();
            println(f"{total} {count} {my_val}");
            return;
        }

        """;

    // six kinds made: the name out of the import, 'let', the replacement, 'x++', the 'try', the
    // 'return' — and the new name of 'my_val', which needs a reader, left as it is
    private const string After = """
        import std.io { println };

        @Deprecated { message = "renamed", replacement = "twice" }
        fn double(x: int): int {
            return x * 2;
        }

        fn twice(x: int): int {
            return x * 2;
        }

        fn pure(): int {
            return 1;
        }

        fn main(): void {
            let total = twice(2);
            var count = 0;
            count++;
            let my_val = pure();
            println(f"{total} {count} {my_val}");
        }

        """;

    [Fact]
    public void The_safe_changes_are_made_and_what_needs_a_reader_stays()
    {
        var root = Package(("lyric.toml", Manifest), ("src/main.lyr", Before));
        var main = Path.Combine(root, "src", "main.lyr");
        var (exit, _, error) = RunRaw("fix", "-C", root, "--color", "never");
        Assert.True(exit == 0, error);
        Assert.Equal(After, File.ReadAllText(main));
        Assert.Contains("Fixed src/main.lyr (6 changes)", error);
        Assert.Contains("warning[LYR-SEM0215]: 'my_val' is a local", error);

        var (again, _, said) = RunRaw("fix", "-C", root, "--color", "never");
        Assert.Equal(0, again);
        Assert.Equal(After, File.ReadAllText(main));
        Assert.DoesNotContain("Fixed", said);
        Assert.Equal(0, RunRaw("build", "-C", root, "--color", "never").Exit);
    }

    [Fact]
    public void A_dry_run_prints_the_diff_and_writes_nothing()
    {
        var root = Package(("lyric.toml", Manifest), ("src/main.lyr", Before));
        var (exit, output, _) = RunRaw("fix", "-C", root, "--dry-run", "--color", "never");
        Assert.Equal(0, exit);
        Assert.Equal(Before, File.ReadAllText(Path.Combine(root, "src", "main.lyr")));
        Assert.StartsWith("--- a/src/main.lyr\n+++ b/src/main.lyr\n@@ -1,4 +1,4 @@\n-import std.io { println, print };\n+import std.io { println };\n", output);
        Assert.Contains("-    var total = double(2);\n+    let total = twice(2);\n", output);
        Assert.Contains("-    return;\n }\n", output);
    }

    [Fact]
    public void The_json_form_says_each_change()
    {
        var root = Package(("lyric.toml", Manifest), ("src/main.lyr", Before));
        var (exit, output, _) = RunRaw("fix", "-C", root, "--json");
        Assert.Equal(0, exit);
        var changes = output.Split('\n').Where(l => l.StartsWith("{\"type\":\"fix\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, changes.Count);
        Assert.Contains(changes, c => c.Contains("\"code\":\"LYR-SEM0075\"") && c.Contains("\"text\":\"let\"") && c.Contains("\"line\":17"));
    }

    /// <summary>Names out of one list — the last two, whose changes overlap, in two passes —; the
    /// import out of the file where none of its names is used.</summary>
    [Fact]
    public void An_import_loses_its_unused_names_and_goes_where_none_is_used()
    {
        const string main = """
            import std.collections { List, Map, Set };
            import std.math { abs, max };

            fn main(): void {
                let xs = List<int>.new();
                let _ = xs;
            }

            """;
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        var (exit, _, error) = RunRaw("fix", "-C", root, "--color", "never");
        Assert.True(exit == 0, error);
        Assert.Equal("""
            import std.collections { List };

            fn main(): void {
                let xs = List<int>.new();
                let _ = xs;
            }

            """, File.ReadAllText(Path.Combine(root, "src", "main.lyr")));
    }

    /// <summary>A dependency's findings are said with <c>--warn-deps</c> and never changed.</summary>
    [Fact]
    public void A_dependencys_files_are_not_changed()
    {
        const string lib = "pub fn helper(): int {\n    var n = 2;\n    return n;\n}\n";
        var root = Package(
            ("dep/lyric.toml", "[package]\nname = \"dep\"\nversion = \"0.1.0\"\n"),
            ("dep/src/lib.lyr", lib),
            ("app/lyric.toml", AppManifest + "\n[dependencies]\ndep = { path = \"../dep\" }\n"),
            ("app/src/main.lyr", "import dep { helper };\n\nfn main(): void {\n    var x = helper();\n    let _ = x;\n}\n"));
        var (exit, _, error) = RunRaw("fix", "-C", Path.Combine(root, "app"), "--warn-deps", "--color", "never");
        Assert.True(exit == 0, error);
        Assert.Equal(lib, File.ReadAllText(Path.Combine(root, "dep", "src", "lib.lyr")));
        Assert.Contains("let x = helper();", File.ReadAllText(Path.Combine(root, "app", "src", "main.lyr")));
        Assert.Contains("warning[LYR-SEM0075]: 'n' is never reassigned", error);
    }
}
