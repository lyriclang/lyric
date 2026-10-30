using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric fix --from-4</c>: 4.6's <c>hello</c> migrated and run by Lyric 5; a project of 4.x
/// with <c>--dry-run</c> (a diff, nothing written) and without (the files, <c>lyric.toml</c> for
/// <c>lyric.json</c>, the report); a program 4.6 refuses left as it is.
/// </summary>
[Collection("console")]
public class FixFrom4CommandTests
{
    private static readonly string Examples = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "fix-from-4", "examples-4.6");

    [Fact]
    public void Hello_of_4_6_is_migrated_and_runs_with_Lyric_5()
    {
        var dir = TestDirectories.Fresh("lyric-from4-hello-");
        var file = Path.Combine(dir, "hello.lyr");
        File.Copy(Path.Combine(Examples, "hello.lyr"), file);

        var (exit, output, error) = RunRaw("fix", "--from-4", file);
        Assert.True(exit == 0, error);
        Assert.Contains("migrated 1 file; 0 notes in fix-from-4.txt", output);
        Assert.Contains("import std.io { println };", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(dir, "fix-from-4.txt")));

        Assert.Equal("Hello, Lyric!\n", BuildAndRun(dir, "hello", "build", file));
    }

    [Fact]
    public void A_project_is_shown_as_a_diff_then_migrated_with_its_manifest_and_report()
    {
        var dir = TestDirectories.Fresh("lyric-from4-project-");
        File.WriteAllText(Path.Combine(dir, "lyric.json"), "{ \"name\": \"app\", \"sourceRoot\": \"src\", \"toolchain\": \"4.0\" }\n");
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        var main = Path.Combine(dir, "src", "main.lyr");
        const string source = "module app.main;\n\nimport std.io.console { println };\n\nfn main(): int {\n    println(\"app\");\n    return 0;\n}\n";
        File.WriteAllText(main, source);

        var (dry, diff, notes) = RunRaw("fix", "--from-4", "--dry-run", "-C", dir);
        Assert.True(dry == 0, notes);
        Assert.Contains("-module app.main;", diff);
        Assert.Contains("+import std.io { println };", diff);
        Assert.Contains("+[package]", diff);
        Assert.Contains("remove lyric.json", diff);
        Assert.Contains("would change 2 files; 2 notes", diff);
        Assert.Contains("proposal[LYR-MIG0101] src/main.lyr:1:1:", notes);
        Assert.Contains("by hand[LYR-MIG0104] lyric.json:1:1:", notes);
        Assert.Equal(source, File.ReadAllText(main));
        Assert.False(File.Exists(Path.Combine(dir, "lyric.toml")));

        var (exit, said, _) = RunRaw("fix", "--from-4", "-C", dir);
        Assert.Equal(0, exit);
        Assert.Contains("migrated 2 files; 2 notes in fix-from-4.txt", said);
        Assert.False(File.Exists(Path.Combine(dir, "lyric.json")));
        Assert.Equal("[package]\nname = \"app\"\nversion = \"0.1.0\"\n", File.ReadAllText(Path.Combine(dir, "lyric.toml")));
        Assert.Contains("by hand[LYR-MIG0104] lyric.json:1:1: the toolchain line '4.0'", File.ReadAllText(Path.Combine(dir, "fix-from-4.txt")));
        Assert.Equal(0, RunRaw("check", "-C", dir).Exit);
    }

    [Fact]
    public void A_program_4_6_refuses_is_not_touched_and_a_json_form_is_refused()
    {
        var dir = TestDirectories.Fresh("lyric-from4-refused-");
        var file = Path.Combine(dir, "main.lyr");
        const string source = "fn main(): int {\n    return \"no\";\n}\n";
        File.WriteAllText(file, source);

        var (exit, _, error) = RunRaw("fix", "--from-4", file);
        Assert.Equal(1, exit);
        Assert.Contains("  4.6: main.lyr:2:", error);
        Assert.Contains("error[LYR-MIG0001]: Lyric 4.6 refuses the program, and it is not migrated", error);
        Assert.Equal(source, File.ReadAllText(file));

        Assert.Equal(2, RunRaw("fix", "--from-4", "--json", file).Exit);
        var (none, _, missing) = RunRaw("fix", "--from-4", "-C", dir);
        Assert.Equal(2, none);
        Assert.Contains("no lyric.json in", missing);
    }
}
