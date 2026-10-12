using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>M9a S11 (design/v5/spec/09 A7 R5; 03 T5 "Review 2026-10-12"): enum reflection as
/// std.core's defaults — <c>VariantName</c> and <c>Variants</c> — and the static defaults they
/// stand on, set in place per conformer.</summary>
[Collection("console")]
public class VariantsTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    /// <summary>The artifact of 13 S11: a names table of an enum with no <c>match</c> — every
    /// variant and its name, one by its name, through the enum and through a constraint.</summary>
    [Fact]
    public void An_enums_variants_are_values_and_names_without_a_match()
    {
        var dir = App("""
            import std.io { println };

            enum Level :: [Variants, Equatable] { Low, Mid, High }

            fn table<T :: [Variants]>(): string {
                var out = "";
                for (v in T.all()) {
                    out = out + v.name() + ";";
                }
                return out;
            }

            fn main(): void {
                println(table<Level>());
                let m = Level.fromName("Mid");
                let x = Level.fromName("Top");
                println(f"{m == Level.Mid} {x == null} {Level.all().length()} {Level.High.name()}");
            }
            """);
        Assert.Equal("Low;Mid;High;\ntrue true 3 High\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A static member of an interface with a body is a default (03 T5): each conformer's
    /// instance, its own static of the name in its place, reached through the name and through a
    /// constraint.</summary>
    [Fact]
    public void A_static_default_is_set_in_place_per_conformer()
    {
        var dir = App("""
            import std.io { println };

            interface Factory {
                static fn make(n: int): Self;
                static fn two(): Self[] {
                    return [Self.make(1), Self.make(2)];
                }
            }

            struct P :: [Factory] {
                v: int,
                static fn make(n: int): P { return P { v = n }; }
            }

            struct Q :: [Factory] {
                v: int,
                static fn make(n: int): Q { return Q { v = n }; }
                static fn two(): Q[] { return [Q { v = 9 }]; }
            }

            fn count<T :: [Factory]>(): int {
                return T.two().length();
            }

            fn main(): void {
                println(f"{P.two().length()} {Q.two().length()} {count<P>()} {count<Q>()} {P.two()[1].v}");
            }
            """);
        Assert.Equal("2 1 2 1 2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>Variants</c> on an enum with a payload variant: refused at the entry, as the
    /// family's refusals are, with the variant named; <c>VariantName</c> alone is fine there.</summary>
    [Fact]
    public void A_variant_with_a_payload_is_refused_at_the_entry()
    {
        var dir = App("""
            enum Shape :: [Variants] { Dot, Circle(int) }

            enum Named :: [VariantName] { Dot, Circle(int) }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("main.lyr:1:16: error[LYR-SEM0135]: 'Variants' is not synthesized for 'Shape': the variant 'Circle' has a payload, and no value without it", error);
        Assert.DoesNotContain("'Named'", error);
    }
}
