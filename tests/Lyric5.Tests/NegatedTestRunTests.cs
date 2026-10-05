using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A negated test narrows, at run time: behind <c>if (!(s is Circle)) { return; }</c> the name
/// is the <c>Circle</c> and its field is read; behind <c>if (!(x != null)) { return; }</c> the
/// value is unwrapped (design/v5/spec/03 T4 O3, T11). Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class NegatedTestRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_negated_type_test_guards()
    {
        Assert.Equal("2.0 -1.0 | 5 0 5 0 | 3 -1 8 5\n", Output("""
            import std.io { println };

            interface Shape {
                fn area(): float;
            }

            class Circle :: [Shape] {
                radius: float,
                fn area(): float {
                    return 3.0 * this.radius * this.radius;
                }
            }

            class Square :: [Shape] {
                side: float,
                fn area(): float {
                    return this.side * this.side;
                }
            }

            fn radiusOf(s: Shape): float {
                if (!(s is Circle)) {
                    return -1.0;
                }
                return s.radius;
            }

            fn next(x: ?int): int {
                if (!(x != null)) {
                    return 0;
                }
                return x + 1;
            }

            fn other(x: ?int): int {
                if (!(x == null)) {
                    return x + 1;
                }
                return 0;
            }

            fn sum(a: ?int, b: ?int): int {
                if (!(a != null && b != null)) {
                    return -1;
                }
                return a + b;
            }

            fn twice(x: ?int): int {
                return if (!!(x != null)) x * 2 else 0;
            }

            fn drain(x: ?int): int {
                var left = x;
                var n = 0;
                while (!(left == null)) {
                    n += left;
                    left = null;
                }
                return n;
            }

            fn main(): void {
                let c = radiusOf(Circle { radius = 2.0 });
                let s = radiusOf(Square { side = 1.0 });
                println(f"{c} {s} | {next(4)} {next(null)} {other(4)} {other(null)} | {sum(1, 2)} {sum(1, null)} {twice(4)} {drain(5)}");
            }
            """));
    }
}
