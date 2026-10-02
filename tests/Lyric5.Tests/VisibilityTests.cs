using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Visibility (design/v5/spec/07 V2): a declaration is its module's (<c>private</c>), its package's
/// (<c>internal</c> — the default) or everyone's (<c>pub</c>), and the question is asked at every
/// route a name takes out of its module, by one rule (LYR-RES0009) — the 4.x front end asked it at
/// the selective import alone. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class VisibilityTests
{
    private const string Util = """
        import std.io { println };

        private fn hidden(): int { return 1; }
        fn shared(): int { return 3; }
        internal fn said(): int { return 5; }
        pub fn exported(): int { return 4; }
        private struct Secret { x: int }
        private let secret = 7;
        pub fn f(x: int): string { return "f1"; }
        private fn f(x: int, y: int): string { return "f2"; }
        pub fn both(): string { return f(1, 2); }
        private interface Walker { fn walk(): int; }
        struct Legs :: [Walker] { n: int, fn walk(): int { return this.n; } }
        fn legs(): Legs { return Legs { n = 2 }; }
        """;

    [Theory]
    [InlineData("a qualified call", "import app.util;\n\nfn main(): void {\n    let x = util.hidden();\n}\n",
        "'hidden' is private to module 'app.util'")]
    [InlineData("a selective import", "import app.util { hidden };\n\nfn main(): void {\n    let x = hidden();\n}\n",
        "'hidden' is private to module 'app.util'")]
    [InlineData("a type in a signature", "import app.util;\n\nfn take(s: util.Secret): int {\n    return 1;\n}\n\nfn main(): void {\n}\n",
        "'Secret' is private to module 'app.util'")]
    [InlineData("a type in a body", "import app.util;\n\nfn main(): void {\n    let s: ?util.Secret = null;\n}\n",
        "'Secret' is private to module 'app.util'")]
    [InlineData("a constraint", "import app.util;\n\nfn g<T :: [util.Walker]>(x: T): int {\n    return 1;\n}\n\nfn main(): void {\n}\n",
        "'Walker' is private to module 'app.util'")]
    [InlineData("a conformance list", "import app.util;\n\nstruct Mine :: [util.Walker] {\n    fn walk(): int {\n        return 1;\n    }\n}\n\nfn main(): void {\n}\n",
        "'Walker' is private to module 'app.util'")]
    [InlineData("an extend target", "import app.util;\n\nextend util.Secret {\n    fn twice(): int {\n        return 2;\n    }\n}\n\nfn main(): void {\n}\n",
        "'Secret' is private to module 'app.util'")]
    [InlineData("an initializer", "import app.util;\n\nfn main(): void {\n    let s = util.Secret { x = 1 };\n}\n",
        "'Secret' is private to module 'app.util'")]
    [InlineData("a global", "import app.util;\n\nfn main(): void {\n    let x = util.secret;\n}\n",
        "'secret' is private to module 'app.util'")]
    [InlineData("an interface-qualified call", "import app.util;\n\nfn main(): void {\n    let n = util.Walker.walk(util.legs());\n}\n",
        "'Walker' is private to module 'app.util'")]
    [InlineData("another module's import", "import app.util;\n\nfn main(): void {\n    util.println(\"x\");\n}\n",
        "'println' is an import of module 'app.util', and an import is its module's own")]
    [InlineData("the standard library's internals", "import std.task;\n\nfn main(): void {\n    let s = task.here();\n}\n",
        "'here' is internal to package 'std'")]
    public void A_hidden_name_is_refused_on_every_route(string route, string main, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 1, $"{route}: exit {exit}\n{error}");
        Assert.Contains($"error[LYR-RES0009]: {why}", error);
        Assert.True(error.Split("error[").Length == 2, $"{route}: one error, no cascade —\n{error}");
    }

    [Fact]
    public void No_word_and_internal_reach_the_whole_package()
    {
        var main = """
            import std.io { println };
            import app.util;
            import app.util { exported };

            private fn own(): int {
                return 6;
            }

            fn main(): void {
                println(f"{util.shared()} {util.said()} {exported()} {own()} {util.f(1)} {util.both()}");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/main.lyr", main));
        Assert.Equal("3 5 4 6 f1 f2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Of an overload set, what may not be named from here is not there: the call meets
    /// the one visible function.</summary>
    [Fact]
    public void A_private_overload_is_not_there()
    {
        var main = "import app.util;\n\nfn main(): void {\n    let s = util.f(1, 2);\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0014]: call expects 1 argument(s), got 2", error);
    }

    [Theory]
    [InlineData("pub private fn f(): int {\n    return 1;\n}\n\nfn main(): void {\n}\n",
        "a declaration takes one visibility word")]
    [InlineData("enum E {\n    pub A,\n    B,\n}\n\nfn main(): void {\n}\n",
        "a variant is as visible as its enum")]
    public void A_word_where_none_goes_is_refused(string text, string why)
    {
        var dir = Package(("main.lyr", text));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PAR0053]: {why}", error);
    }

    [Fact]
    public void A_field_takes_a_word()
    {
        var text = """
            struct P {
                pub x: int,
                private var y: int,
                internal z: int,
            }

            fn main(): int {
                let p = P { x = 1, y = 2, z = 3 };
                return p.x + p.y + p.z;
            }
            """;
        var dir = Package(("main.lyr", text));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
    }
}
