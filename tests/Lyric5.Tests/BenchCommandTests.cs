using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric bench</c> (design/v5/spec/10 B12 X4, 11 C2): the package's <c>@Bench</c> functions
/// built into one program in the release profile and run one after another, a line each — the
/// time a run, its allocations and their bytes, the bytes a second where the bench says how many a
/// run handles; <c>--save</c> writes the times, <c>--compare</c> reads them back. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class BenchCommandTests
{
    /// <summary>Three benchmarks: one that allocates nothing, one that says its bytes, one that
    /// allocates a list a run.</summary>
    private const string SpeedBenches =
        "import std.core { blackBox };\n\n"
        + "@Bench\nfn adds(&b: Bench): void {\n    b.iter {\n        let _ = blackBox(1 + 2);\n    }\n}\n\n"
        + "@Bench\nfn reads(&b: Bench): void {\n    let bytes: uint8[] = [1u8] * 1024;\n    b.bytes(1024);\n"
        + "    b.iter {\n        var s = 0;\n        for (x in bytes) {\n            s += x as int;\n        }\n"
        + "        let _ = blackBox(s);\n    }\n}\n\n"
        + "@Bench\nfn makes(&b: Bench): void {\n    b.iter {\n        let xs = List<int>.new();\n        xs.push(1);\n"
        + "        let _ = blackBox(xs.length());\n    }\n}\n";

    private static string App(params (string Path, string Text)[] more) =>
        Package([("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"), .. more]);

    private static string[] Lines(string output) => output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Each_benchmark_has_a_line_with_its_time_its_allocations_and_its_bytes()
    {
        var dir = App(("tests/speed.lyr", SpeedBenches));
        var (exit, output, error) = Run("bench", "-C", dir);
        Assert.True(exit == 0, output + error);
        var lines = Lines(output);
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^bench app\.tests\.speed\.adds: \d+\.\d ns/op, 0 allocs/op, 0 B/op \(\d+ runs\)$", lines[0]);
        Assert.Matches(@"^bench app\.tests\.speed\.reads: \d+\.\d ns/op, 0 allocs/op, 0 B/op, \d+\.\d MB/s \(\d+ runs\)$", lines[1]);
        Assert.Matches(@"^bench app\.tests\.speed\.makes: \d+\.\d ns/op, [1-9]\d* allocs/op, [1-9]\d* B/op \(\d+ runs\)$", lines[2]);
        // The runs were sized until one round took a tenth of a second (12 §Benchmarks); the round
        // reported is the median of five more at that many runs, and may be shorter — the sizing
        // round slowed by a hiccup, the five after it not (a Windows runner reported 94.9 ms, under
        // a floor of 95). What holds: its runs times its time a run is at least half of the tenth,
        // unless the machine doubled its pace between the rounds.
        foreach (var line in lines)
        {
            var measured = System.Text.RegularExpressions.Regex.Match(line, @": (\d+\.\d) ns/op.*\((\d+) runs\)");
            Assert.True(double.Parse(measured.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                        * long.Parse(measured.Groups[2].Value) >= 0.5e8, line);
        }
    }

    [Fact]
    public void A_saved_run_is_compared_with_the_next()
    {
        var dir = App(("tests/speed.lyr", SpeedBenches));
        var saved = Path.Combine(dir, "base.txt");
        var (exit, output, error) = Run("bench", "-C", dir, "-f", "adds", "--save", saved);
        Assert.True(exit == 0, output + error);
        var rows = File.ReadAllLines(saved);
        Assert.Single(rows);
        Assert.Matches(@"^app\.tests\.speed\.adds\t\d+\.\d{3}\t0\t0$", rows[0]);

        (exit, output, error) = Run("bench", "-C", dir, "-f", "adds", "--compare", saved);
        Assert.True(exit == 0, output + error);
        Assert.Matches(@"^bench app\.tests\.speed\.adds: \d+\.\d ns/op, 0 allocs/op, 0 B/op \(\d+ runs\), was \d+\.\d ns/op: [+-]\d+\.\d%$",
            Lines(output)[0]);
    }

    [Fact]
    public void A_file_to_compare_with_that_is_not_there_is_exit_1()
    {
        var dir = App(("tests/speed.lyr", SpeedBenches));
        var (exit, output, error) = Run("bench", "-C", dir, "-f", "adds", "--compare", Path.Combine(dir, "missing.txt"));
        Assert.True(exit == 1, output + error);
        Assert.StartsWith("error: ", output);
    }

    [Fact]
    public void A_package_without_benchmarks_says_so_and_the_filter_chooses()
    {
        var (exit, output, error) = Run("bench", "-C", App());
        Assert.True(exit == 0, output + error);
        Assert.Equal("no benchmarks\n", output.Replace("\r\n", "\n"));

        (exit, output, error) = Run("bench", "-C", App(("tests/speed.lyr", SpeedBenches)), "-f", "nothing");
        Assert.True(exit == 0, output + error);
        Assert.Equal("no benchmark's name holds 'nothing'\n", output.Replace("\r\n", "\n"));
    }
}
