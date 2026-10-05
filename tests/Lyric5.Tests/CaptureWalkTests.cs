using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A lambda captures a local wherever it reads it. The walk that collects the captures listed the
/// node types it knew, and a view's open range — <c>xs[lo..]</c> — was not among them: a local
/// read only there was no capture, and the lowering met a name it had no slot for
/// (<c>LYR-IR0001</c>). The walk goes through every node now.
/// </summary>
[Collection("console")]
public class CaptureWalkTests
{
    [Fact]
    public void A_local_read_only_in_a_views_range_is_captured()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                fn main(): void {
                    let xs = [1, 2, 3, 4, 5];
                    let lo = 2;
                    let hi = 4;
                    let rest = () => xs[lo..];
                    let middle = () => xs[lo..hi];
                    println(f"{rest().length()} {middle().length()}");
                }
                """));
        Assert.Equal("3 2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
