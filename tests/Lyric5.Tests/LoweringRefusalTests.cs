using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The lowering's refusals under the code of their reason, not all under <c>LYR-IR0001</c>, "this
/// compiler version cannot lower it yet". A delegation to a field
/// that holds a child interface was one of them, <c>LYR-SEM0196</c>, and is lowered now: the field's
/// value goes through the parent's table.
/// </summary>
[Collection("console")]
public class LoweringRefusalTests
{
    [Fact]
    public void A_delegation_to_a_field_of_a_child_interface_forwards_through_the_parents_table()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };

            interface A {
                fn a(): int;
            }

            interface B :: [A] {
                fn b(): int;
            }

            class Impl :: [B] {
                fn a(): int { return 1; }
                fn b(): int { return 2; }
            }

            class Outer :: [A by inner] {
                inner: B,
            }

            fn main(): void {
                let o = Outer { inner = Impl {} };
                println(f"{o.a()}");
                let a: A = o;
                println(f"{a.a()}");
            }
            """));
        Assert.Equal("1\n1\n", BuildAndRun(root, "app", "build", "-C", root));
    }
}
