using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>@Shared</c>: the <c>clone()</c> the compiler writes
/// copies such a field — the reference to a service, a parent — instead of cloning it; without the
/// mark a field with no <c>clone</c> stops the synthesis.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class SharedFieldTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    [Fact]
    public void A_shared_field_is_copied_by_the_synthesized_clone()
    {
        var dir = App("""
            import std.io { println };

            class Service { var n: int }

            struct Node :: [Clone] {
                @Shared service: Service,
                name: string,
            }

            fn main(): void {
                let a = Node { service = Service { n = 1 }, name = "a" };
                let b = a.clone();
                b.service.n = 5;
                println(f"{a.service.n} {b.name}");
            }
            """);
        Assert.Equal("5 a\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void Without_the_mark_a_field_with_no_clone_stops_the_synthesis()
    {
        var dir = App("""
            class Service { var n: int }

            struct Node :: [Clone] {
                service: Service,
            }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("'Clone' is not synthesized for 'Node'", error); // at the entry (05 §14 rule 6)
        Assert.Contains("[LYR-SEM0012]", error);
    }
}
