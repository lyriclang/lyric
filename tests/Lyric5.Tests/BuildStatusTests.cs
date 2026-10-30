using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The status lines of a build on the standard error (spec 14 §1.1): each step that does work says
/// one line as it starts, what is up to date says nothing, a build that succeeds ends with
/// <c>Finished</c>; <c>-q</c> and <c>--json</c> say none. A build said nothing but its findings, so
/// a long one — a vendored C library — looked hung.
///
/// <para>Through <c>Main</c>, so in the console collection; the standard error is no terminal here,
/// so the count of the units is never drawn.</para>
/// </summary>
[Collection("console")]
public partial class BuildStatusTests
{
    private const string Main = "fn main(): void {\n}\n";

    private static string Native() => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/twice.c\"]\n"),
        ("native/twice.c", "long twice(long x) { return 2 * x; }\n"),
        ("src/main.lyr", Main));

    /// <summary>The status lines of a command, in their order: what <see cref="Status"/> matches —
    /// with '/' between a path's parts and '\n' after a line, as Windows does not.</summary>
    private static string[] Lines(string error) =>
        [.. error.Replace("\r\n", "\n").Replace('\\', '/').Split('\n').Where(line => Status().IsMatch(line))];

    [GeneratedRegex(@"^ *(Compiling|Checking|Linking|Archiving|Running|Binding|Fetching|Updating|Finished) ")]
    private static partial Regex Status();

    [GeneratedRegex(@"^    Finished debug \[opt 0, debug info\] in \d+\.\d\ds$")]
    private static partial Regex Finished();

    [Fact]
    public void A_build_says_its_steps_and_one_up_to_date_that_it_finished()
    {
        var dir = Native();
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.True(exit == 0, error);
        var lines = Lines(error).Where(l => !l.Contains("the runtime")).ToArray(); // the runtime's is the user cache's
        Assert.Equal("   Compiling app v0.1.0 (src/main.lyr)", lines[0]);
        Assert.Matches(@"^   Compiling \d+ of \d+ C units of app$", lines[1]);
        Assert.StartsWith("     Linking app (out", lines[2]);
        Assert.Matches(Finished(), lines[3]);
        Assert.Equal(4, lines.Length);

        var (again, _, said) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(0, again);
        Assert.Matches(Finished(), Assert.Single(Lines(said)));
    }

    /// <summary>A changed C file compiles that unit alone: the front end's C is up to date, and
    /// says nothing.</summary>
    [Fact]
    public void A_changed_unit_is_the_only_one_compiled()
    {
        var dir = Native();
        Assert.Equal(0, RunRaw("build", "-C", dir).Exit);
        File.WriteAllText(Path.Combine(dir, "native", "twice.c"), "long twice(long x) { return x + x; }\n");
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.True(exit == 0, error);
        var lines = Lines(error);
        Assert.Matches(@"^   Compiling 1 of \d+ C units of app$", lines[0]);
        Assert.StartsWith("     Linking app (out", lines[1]);
        Assert.Matches(Finished(), lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Check_says_what_it_checks()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Main));
        var (exit, _, error) = RunRaw("check", "-C", dir, "--color", "never");
        Assert.True(exit == 0, error);
        var lines = Lines(error);
        Assert.Equal("    Checking app v0.1.0 (src/main.lyr)", lines[0]);
        Assert.Matches(Finished(), lines[1]);
        Assert.Equal(2, lines.Length);
    }

    /// <summary><c>run</c> says the binary it runs, after the build's last line.</summary>
    [Fact]
    public void Run_says_the_binary_after_the_build()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Main));
        var (exit, _, error) = RunRaw("run", "-C", dir, "--color", "never");
        Assert.True(exit == 0, error);
        var lines = Lines(error);
        Assert.Matches(Finished(), lines[^2]);
        Assert.StartsWith("     Running out", lines[^1]);
    }

    /// <summary>A build that fails says its findings, and no <c>Finished</c>.</summary>
    [Fact]
    public void A_failed_build_does_not_finish()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n    let x: int = \"no\";\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(1, exit);
        Assert.Equal(["   Compiling app v0.1.0 (src/main.lyr)"], Lines(error));
        Assert.Contains("error[LYR-SEM", error);
    }

    [Theory]
    [InlineData("-q")]
    [InlineData("--quiet")]
    [InlineData("--json")]
    public void Quiet_and_json_say_no_status(string option)
    {
        var dir = Native();
        var (exit, _, error) = RunRaw("build", "-C", dir, option);
        Assert.True(exit == 0, error);
        Assert.Equal("", error);
    }

    /// <summary>In colour the word is green and bold, as the diagnostics' colours are asked for.</summary>
    [Fact]
    public void The_word_is_drawn_in_colour_where_colour_is_asked_for()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Main));
        var (exit, _, error) = RunRaw("check", "-C", dir, "--color", "always");
        Assert.True(exit == 0, error);
        Assert.Contains("\u001b[1;32m    Checking\u001b[0m app v0.1.0", error);
    }
}
