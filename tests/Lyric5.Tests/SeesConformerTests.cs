using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>@SeesConformer</c> (11 §1 rule 15, §4 rule 10; the sweep after M10, W10): an interface's
/// defaults ask with the conformer's module and see every field of it; without the marker a
/// default sees what the interface's module may name (R7) — another module's private field is
/// not there, another package's internal one neither.
/// </summary>
[Collection("console")]
public class SeesConformerTests
{
    private const string Args = """
        import std.meta { fields };

        @SeesConformer
        pub interface Whole {
            fn whole(): string {
                var out = "";
                comptime for (f in fields<Self>()) { out = out + f.name + ";"; }
                return out;
            }
        }

        pub interface Seen {
            fn seen(): string {
                var out = "";
                comptime for (f in fields<Self>()) { out = out + f.name + ";"; }
                return out;
            }
        }
        """;

    [Fact]
    public void A_marked_interface_sees_every_field_of_its_conformer()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/args.lyr", Args), ("src/main.lyr", """
            import std.io { println };
            import app.args { Whole, Seen };

            struct Start :: [Whole, Seen] {
                pub a: int,
                b: int,
                private c: int,
            }

            fn main(): void {
                let s = Start { a = 1, b = 2, c = 3 };
                println(s.whole());
                println(s.seen());
            }
            """));
        Assert.Equal("a;b;c;\na;b;\n", BuildAndRun(root, "app", "build", "-C", root));
    }

    [Fact]
    public void The_marker_sits_on_an_interface_alone()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            @SeesConformer
            struct Start { a: int }

            fn main(): void { }
            """));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0065]", error);
    }
}
