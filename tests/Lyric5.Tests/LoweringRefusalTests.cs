using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The lowering's refusals under the code of their reason (design/v5/spec/11 G4, M12a S1): until M12a
/// every one was <c>LYR-IR0001</c>, "this compiler version cannot lower it yet". A delegation to a field
/// that holds another interface is the language's limit for now (design 13, M12b), <c>LYR-SEM0196</c>.
/// </summary>
[Collection("console")]
public class LoweringRefusalTests
{
    [Fact]
    public void A_delegation_to_a_field_of_another_interface_is_refused_under_its_own_code()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
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
                let _ = o.a();
            }
            """));
        var (exit, _, error) = Run("build", "-C", root);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0196]: 'Outer' delegates 'A' to 'inner', a value of another interface", error);
        Assert.DoesNotContain("LYR-IR0001", error);
    }
}
