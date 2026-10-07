using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A panic's trace starts at the first frame of the program's text (spec 13 §1.4 rule 2; N1b). The
/// runtime's frames above it are not shown — the function that raised the panic among them, as
/// <c>lyr_arr_repeat</c> raises RT0007 —, and a program whose package is called like a part of the
/// runtime keeps its frames: they were told apart by name, and a package named <c>trace</c> has
/// functions named <c>lyr_trace_…</c>, which looked like the runtime's trace and lost every frame.
/// </summary>
[Collection("console")]
public class TraceFrameTests
{
    [Fact]
    public void A_trace_starts_at_the_programs_text_whatever_its_package_is_called()
    {
        var dir = Package(("lyric.toml", "[package]\nname = \"trace\"\nversion = \"0.1.0\"\n"), ("src/main.lyr", """
            fn repeated(n: int): int {
                let xs = [1] * n;
                return xs.length();
            }

            fn main(): void {
                let k = repeated(-1);
            }
            """));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(
            Path.Combine(dir, "out", "debug", host.Triple, "trace" + host.ExecutableSuffix), [], TimeSpan.FromSeconds(10));
        var trace = ran.Stderr.Replace("\r\n", "\n");
        Assert.True(ran.ExitCode == 101, $"exit {ran.ExitCode}\nstderr:\n{trace}");
        Assert.StartsWith("panic [LYR-RT0007]: repeat count -1 is negative\n", trace);
        Assert.Contains("    at lyr_trace_main_repeated (", trace);
        Assert.Contains("    at lyr_trace_main_main (", trace);
        Assert.DoesNotContain("lyr_arr_repeat", trace);
    }
}
