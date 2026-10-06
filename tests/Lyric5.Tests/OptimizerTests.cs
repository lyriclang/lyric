using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The IR optimizer in the release profile (design/v5/spec/01 B10; the review's B10): the
/// inliner, scalar replacement and devirtualization run where the profile optimizes C at
/// <c>-O2</c> and above, and not in debug. What they run keeps the language's values: a method
/// of a struct or an enum takes the caller's place as <c>this</c> (02 M5), and a binding copies
/// (03 §3). Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class OptimizerTests
{
    private const string Manifest = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

    /// <summary>Methods of values, inlined and not: a <c>mut fn</c> on a local, one that hands
    /// <c>&amp;this</c> on, one on a field of a receiver, an enum's that replaces <c>this</c>;
    /// copies taken between the writes.</summary>
    private const string Values = """
        import std.io { println };

        struct Counter {
            var n: int,

            mut fn bump(): void {
                this.n = this.n + 1;
            }

            mut fn reset(): void {
                clear(&this);
            }

            fn get(): int {
                return this.n;
            }
        }

        fn clear(&c: Counter): void {
            c.n = 0;
        }

        struct Pair {
            var left: Counter,
            var right: Counter,

            mut fn bumpBoth(): void {
                this.left.bump();
                this.right.bump();
                this.right.bump();
            }
        }

        enum Light {
            Red, Green;

            mut fn flip(): void {
                this = match (this) { .Red => .Green, .Green => .Red };
            }

            fn code(): int {
                return match (this) { .Red => 1, .Green => 2 };
            }
        }

        fn main(): void {
            var c = Counter { n = 0 };
            var i = 0;
            while (i < 1000) {
                c.bump();
                i += 1;
            }
            let kept = c;
            c.bump();
            var p = Pair { left = Counter { n = 10 }, right = Counter { n = 20 } };
            p.bumpBoth();
            let q = p;
            p.bumpBoth();
            var light = Light.Red;
            let was = light;
            light.flip();
            var a: int[3] = [1, 2, 3];
            let b = a;
            a[0] = 10;
            let total = c.get();
            c.reset();
            println(f"{kept.get()} {total} {c.get()} {q.left.get()} {p.left.get()} {p.right.get()} {was.code()} {light.code()} {b[0]} {a[0]}");
        }
        """;

    [Theory]
    [InlineData("debug")]
    [InlineData("release")]
    public void Values_stay_values_optimized_or_not(string profile)
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Values));
        var (exit, _, error) = Run("build", "-C", dir, "--profile", profile);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(dir, "out", profile, host.Triple, "app" + host.ExecutableSuffix);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromSeconds(30));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("1000 1001 0 11 12 24 1 2 1 10\n", ran.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>Release inlines a small function — the call is gone from the IR —; debug keeps
    /// every call, as written.</summary>
    [Fact]
    public void Release_inlines_and_debug_does_not()
    {
        const string main = "import std.io { println };\n\nfn twice(n: int): int {\n    return n * 2;\n}\n\n"
                            + "fn main(): void {\n    println(f\"{twice(21)}\");\n}\n";
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", main));
        var (debugExit, debugIr, debugError) = Run("build", "-C", dir, "--emit", "ir", "--profile", "debug");
        Assert.True(debugExit == 0, debugError);
        Assert.Contains("call app.main.twice(", debugIr);
        var (releaseExit, releaseIr, releaseError) = Run("build", "-C", dir, "--emit", "ir", "--profile", "release");
        Assert.True(releaseExit == 0, releaseError);
        Assert.DoesNotContain("call app.main.twice(", releaseIr);
    }
}
