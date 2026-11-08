using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>run -C &lt;dir&gt;</c> starts the program in <c>&lt;dir&gt;</c>, as <c>cd &lt;dir&gt; &amp;&amp; lyric run</c>
/// does (14 §1.1 rule 2): a relative path the program opens is the directory's, not the one the
/// toolchain was started in — a game's assets beside its <c>src/</c>. The program writes a file by a
/// relative path, and the file is where the directory is. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class RunDirectoryTests
{
    private const string Manifest = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

    private const string WritesHere =
        "import std.fs { writeText };\n\nfn main(): void throws Error {\n    try writeText(\"here.txt\", \"ran\");\n}\n";

    [Fact]
    public void A_program_runs_in_the_directory_C_names()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", WritesHere));
        var (exit, _, error) = Run("run", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("ran", File.ReadAllText(Path.Combine(dir, "here.txt")));
    }

    /// <summary>The directory named, not the package's root above it: <c>-C</c> is where the command
    /// stands, and the package is searched upward from there.</summary>
    [Fact]
    public void A_directory_below_the_package_is_where_the_program_runs()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", WritesHere));
        var below = Path.Combine(dir, "src");
        var (exit, _, error) = Run("run", "-C", below);
        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Path.Combine(below, "here.txt")), "the program did not write in the directory -C named");
        Assert.False(File.Exists(Path.Combine(dir, "here.txt")), "the program ran in the package's root, not in the directory -C named");
    }
}
