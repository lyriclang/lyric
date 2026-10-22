using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A parent an interface of the program implies is synthesized as a listed one is (05 §14 rule
/// 5): <c>struct Start :: [Args]</c> under
/// <c>interface Args :: [Default]</c> has its <c>default</c>, <c>Display</c> through a chain of
/// two, the interface in another module. It was <c>LYR-SEM0020</c>, "'Start' does not
/// implement abstract method 'default' of interface 'Default' (implied by 'Args')".
/// </summary>
[Collection("console")]
public class ImpliedSynthesisTests
{
    private const string Args = """
        pub interface Args :: [Default] {
            static fn fill(): Self {
                return Self.default();
            }
        }

        pub interface Named :: [Display] { }
        pub interface Shown :: [Named] { }
        """;

    [Fact]
    public void An_implied_parent_is_synthesized()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/args.lyr", Args), ("src/main.lyr", """
            import std.io { println };
            import app.args { Args, Shown };

            struct Start :: [Args] {
                port: int = 8080,
                count: int,
            }

            struct Labelled :: [Shown] {
                n: int,
            }

            struct Both :: [Args, Default] {
                port: int = 1,
            }

            struct Own :: [Args] {
                port: int = 2,
                static fn default(): Own { return Own { port = 7 }; }
            }

            struct Blocked :: [Args] {
                port: int = 3,
            }

            extend Blocked :: [Default] {
                static fn default(): Blocked { return Blocked { port = 9 }; }
            }

            enum Kind :: [Shown] { A, B(int) }

            fn main(): void {
                println(f"{Start.fill().port} {Start.fill().count}");
                println(f"{Labelled { n = 4 }.show()} {Kind.B(3).show()}");
                println(f"{Both.fill().port} {Own.fill().port} {Blocked.fill().port}");
            }
            """));
        Assert.Equal("8080 0\nLabelled { n = 4 } Kind.B(3)\n1 7 9\n", BuildAndRun(root, "app", "build", "-C", root));
    }

    /// <summary>What the synthesis has no form for is refused at the listed entry, the implied
    /// interface named: an enum has no default (05 §14 rule 3).</summary>
    [Fact]
    public void An_implied_default_of_an_enum_is_refused_at_the_entry()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/args.lyr", Args), ("src/main.lyr", """
            import app.args { Args };

            enum Mode :: [Args] { Fast, Slow }

            fn main(): void { }
            """));
        var (exit, _, error) = Run("build", "-C", root);
        Assert.Equal(1, exit);
        Assert.Contains("main.lyr:3:15: error[LYR-SEM0135]: 'Default' is not synthesized for 'Mode'", error);
    }
}
