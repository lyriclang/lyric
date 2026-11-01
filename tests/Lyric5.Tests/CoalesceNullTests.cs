using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>?T ?? null</c>: the checker gives it the type <c>?T</c>, and the lowering lowered the right
/// side without one — a <c>null</c> has no type of its own, so the program ended as LYR-ICE0002
/// ("'null' in a position without an expected type"). The right side is lowered towards the
/// result's type now, as <c>??=</c> lowers its value. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class CoalesceNullTests
{
    [Fact]
    public void A_null_on_the_right_of_coalesce_is_the_results_none()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };

            fn absent(): ?string throws Error {
                return null;
            }

            fn present(): ?string throws Error {
                return "there";
            }

            fn main(): void {
                let none = (try? absent()) ?? null;
                let there = (try? present()) ?? null;
                let plain: ?int = null;
                let still = plain ?? null;
                println(f"{none == null} {there ?? "-"} {still == null}");
            }
            """));
        Assert.Equal("true there true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
