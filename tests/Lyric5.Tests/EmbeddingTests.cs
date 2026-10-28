using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A C program that embeds a Lyric library (spec 13 §5): <c>examples/embedded-host</c> — exports
/// that wait as tasks, the pump for the tasks one left, an error and a panic as a status with
/// <c>lyr_take_error</c>, the output captured, a second thread of the host's own — built and run;
/// and a panic where an export has no status to answer with. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class EmbeddingTests
{
    private static string Example([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "examples", "embedded-host"));

    private const string Expected = """
        init again: 1
        sum: 5050
        lyric> job 1 of 3
        lyric> job 2 of 3
        lyric> job 3 of 3
        pumped: yes
        lyric> job 1 of 2
        lyric> job 2 of 2
        parse 42: 0 42
        parse 4x2: status 1
          std.core.Exception: '4x2' is no count
          std.core.ParseError: the text is not a value of the type
        check 0: status 1
          std.core.Exception: zero counts nothing
        check -1: status 2
          LYR-RT0008: a count is never negative: -1
          with its frames
        check 7: 0 7
        nothing left: status 0
        second thread: 55

        """;

    [Fact]
    public void The_embedded_host_runs_its_library()
    {
        var dir = Package(("x", ""));
        ExampleTests.Copy(Example(), dir);
        var (exit, _, error) = Run("build", "-C", dir, "--lib");
        Assert.True(exit == 0, error);
        var o = ExportTests.OutOf(dir);
        var ran = ExportTests.BuildHost(dir, [Path.Combine(o, "libtally.a"), .. ExportTests.SystemLibraries()]);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal(Expected.Replace("\r\n", "\n"), ran.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>An export without a status answers nothing for a panic: the process ends with the
    /// report, as a program does.</summary>
    [Fact]
    public void A_panic_in_an_export_without_a_status_ends_the_host()
    {
        var dir = Package(
            ("lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n"),
            ("src/lib.lyr", "@Export(\"geo_boom\")\npub fn boom(n: int): int {\n    if (n > 0) {\n        panic(\"boom\");\n    }\n    return n;\n}\n"),
            ("host.c", "#include <stdio.h>\n#include \"geo.h\"\n\nint main(void) {\n    if (lyr_init(NULL) != LYR_OK) return 10;\n"
                       + "    printf(\"%lld\\n\", (long long)geo_boom(0));\n    fflush(stdout);\n    geo_boom(1);\n    printf(\"after\\n\");\n    return 0;\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--lib");
        Assert.True(exit == 0, error);
        var ran = ExportTests.BuildHost(dir, [Path.Combine(ExportTests.OutOf(dir), "libgeo.a"), .. ExportTests.SystemLibraries()]);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("0\n", ran.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("panic [LYR-RT0008]: boom", ran.Stderr);
    }
}
