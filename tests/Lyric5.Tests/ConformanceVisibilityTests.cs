using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Where a conformance block reaches (design/v5/spec/04; spec 04 rule 10; the sweep after M10,
/// W7): wherever its type is, its module imported or not — the synthesized ones among them. A
/// type that comes through a re-export, a return value or a field brings its conformances; a
/// block of methods alone stays its module's (04 rule 9).
/// </summary>
[Collection("console")]
public class ConformanceVisibilityTests
{
    private const string Lib = """
        pub enum CliErrorKind :: [Equatable, Hashable, Ordered, Clone, Debug] { NoCommand, UnknownOption }

        pub struct Thing :: [Default] { pub n: int = 3 }

        extend Thing :: [Display] {
            fn show(): string { return f"thing {this.n}"; }
        }

        extend Thing {
            pub fn plain(): int { return 1; }
        }

        pub interface Fill :: [Default] {
            static fn fill(): Self { return Self.default(); }
        }
        """;

    private static string App(string main) =>
        Package(("app/lyric.toml", AppManifest + "\n[dependencies]\nlib = { path = \"../lib\" }\n"),
            ("app/src/main.lyr", main),
            ("lib/lyric.toml", "[package]\nname = \"lib\"\nversion = \"0.1.0\"\n"),
            ("lib/src/error.lyr", Lib),
            ("lib/src/lib.lyr", "pub import lib.error { CliErrorKind, Thing, Fill };\n"));

    /// <summary>Every member of the family, a hand-written block and a library default that
    /// asks <c>Self.default()</c> of the conformer's synthesized one — through a re-export, with
    /// the type's own module never imported. Ten errors before W7 (<c>LYR-SEM0059</c>,
    /// <c>LYR-SEM0003</c>, <c>LYR-SEM0028</c>, <c>LYR-SEM0012</c>).</summary>
    [Fact]
    public void A_conformance_is_seen_wherever_its_type_is()
    {
        var root = App("""
            import std.io { println };
            import lib { CliErrorKind, Thing, Fill };

            fn hashes<T :: [Hashable]>(x: T): bool { return true; }
            fn clones<T :: [Clone]>(x: T): bool { return true; }
            fn orders<T :: [Ordered]>(x: T): bool { return true; }
            fn shows<T :: [Display]>(x: T): bool { return true; }

            pub struct Start :: [Fill, Default] { pub port: int = 8080 }

            fn main(): void {
                let k = CliErrorKind.NoCommand;
                println(f"{k == CliErrorKind.NoCommand} {k != CliErrorKind.UnknownOption} {k < CliErrorKind.UnknownOption}");
                println(f"{hashes(k)} {clones(k)} {orders(k)} {shows(Thing { })}");
                println(f"{k.clone().debug()} {Thing { }.show()} {Thing.default().n}");
                println(f"{Start.fill().port}");
            }
            """);
        var app = Path.Combine(root, "app");
        Assert.Equal("true true true\ntrue true true true\nCliErrorKind.NoCommand thing 3 3\n8080\n",
            BuildAndRun(app, "app", "build", "-C", app));
    }

    /// <summary>A block of methods alone is its module's (04 rule 9): through the re-export the
    /// method is not there.</summary>
    [Fact]
    public void A_block_of_methods_alone_stays_its_modules()
    {
        var root = App("""
            import lib { Thing };

            fn main(): void {
                let _ = Thing { }.plain();
            }
            """);
        var app = Path.Combine(root, "app");
        var (exit, _, error) = Run("build", "-C", app);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]: 'Thing' has no member 'plain'", error);
    }
}
