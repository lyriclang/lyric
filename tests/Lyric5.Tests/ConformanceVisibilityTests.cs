using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Where a conformance block reaches (spec 04 rule 10): wherever its type is, its module imported or not — the synthesized ones among them. A
/// type that comes through a re-export, a return value or a field brings its conformances; a
/// block of methods alone reaches where its module is imported or passed on by a <c>pub import</c>
/// (04 rule 9a), not where only its type arrives.
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

    private static string App(string main, string root = "pub import lib.error { CliErrorKind, Thing, Fill };\n") =>
        Package(("app/lyric.toml", AppManifest + "\n[dependencies]\nlib = { path = \"../lib\" }\n"),
            ("app/src/main.lyr", main),
            ("lib/lyric.toml", "[package]\nname = \"lib\"\nversion = \"0.1.0\"\n"),
            ("lib/src/error.lyr", Lib),
            ("lib/src/lib.lyr", root));

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

    /// <summary>A block of methods travels with the <c>pub import</c> that passes its module on
    /// (04 rule 9a): <c>lib</c> passes <c>lib.error</c> on with <c>Thing</c>, and its method with it.
    /// Before, the re-export brought the type and left the method behind.</summary>
    [Fact]
    public void A_block_of_methods_travels_with_the_re_export()
    {
        var root = App("""
            import std.io { println };
            import lib { Thing };

            fn main(): void {
                println(Thing { }.plain());
            }
            """);
        var app = Path.Combine(root, "app");
        Assert.Equal("1\n", BuildAndRun(app, "app", "build", "-C", app));
    }

    /// <summary>Where only the type arrives — a return value, its module imported by no one on the
    /// way — the block of methods stays its module's (04 rule 9a); the conformances come along
    /// (rule 10).</summary>
    [Fact]
    public void A_block_of_methods_stays_where_only_its_type_arrives()
    {
        var root = App("""
            import lib { make };

            fn main(): void {
                let t = make();
                let _ = t.show();
                let _ = t.plain();
            }
            """, root: "import lib.error { Thing };\n\npub fn make(): Thing {\n    return Thing { };\n}\n");
        var app = Path.Combine(root, "app");
        var (exit, _, error) = Run("build", "-C", app);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]: 'Thing' has no member 'plain'", error);
        Assert.DoesNotContain("'show'", error);
    }
}
