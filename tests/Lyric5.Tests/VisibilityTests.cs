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

    /// <summary>The members of a type follow the same words (07 V2 S0, S5): a field, a method, a
    /// static; an extend block's word is its methods' default; a conformance is as visible as its
    /// type and interface.</summary>
    private const string Members = """
        pub struct Point { pub x: int, y: int, private z: int }
        pub fn origin(): Point { return Point { x = 0, y = 0, z = 0 }; }
        pub struct Open { pub a: int, b: int }
        pub fn open(): Open { return Open { a = 1, b = 2 }; }
        pub fn doubled(): int { return open().twice(); }
        pub class Box {
            pub var v: int,
            private secret: int,
            private fn hide(): int { return 1; }
            pub fn show(): int { return this.hide() + 1; }
        }
        pub fn box(): Box { return Box { v = 1, secret = 2 }; }
        pub struct Counter {
            n: int,
            static fn make(): Counter { return Counter { n = 0 }; }
            private static fn hidden(): Counter { return Counter { n = 1 }; }
        }
        private extend Open { fn twice(): int { return this.a * 2; } }
        extend Open { fn thrice(): int { return this.a * 3; } }
        pub interface Shape { fn area(): int; }
        extend Open :: [Shape] { fn area(): int { return this.a; } }
        """;

    [Theory]
    [InlineData("a field read", "let n = util.origin().z;", "'z' is private to module 'app.util'")]
    [InlineData("an initializer", "let p = util.Point { x = 1, y = 2, z = 3 };",
        "'Point' cannot be built here: 'z' is private to module 'app.util' — its factory builds it")]
    [InlineData("a copy with 'with'", "let p = util.origin() with { x = 5 };",
        "'Point' cannot be copied with 'with' here: 'z' is private to module 'app.util'")]
    [InlineData("a method", "let n = util.box().hide();", "'hide' is private to module 'app.util'")]
    [InlineData("a field of a class", "let n = util.box().secret;", "'secret' is private to module 'app.util'")]
    [InlineData("a static", "let c = util.Counter.hidden();", "'hidden' is private to module 'app.util'")]
    [InlineData("a field in a pattern", "match (util.origin()) {\n        util.Point { z } => {}\n    }",
        "'z' is private to module 'app.util'")]
    public void A_hidden_member_is_refused_on_every_route(string route, string statement, string why)
    {
        var main = $"import app.util;\n\nfn main(): void {{\n    {statement}\n}}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Members), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 1, $"{route}: exit {exit}\n{error}");
        Assert.Contains($"error[LYR-RES0009]: {why}", error);
        Assert.True(error.Split("error[").Length == 2, $"{route}: one error, no cascade —\n{error}");
    }

    [Fact]
    public void What_a_member_word_allows_is_reached()
    {
        var main = """
            import std.io { println };
            import app.util;

            fn main(): void {
                let b = util.box();
                b.v = 7;
                let o = util.open();
                println(f"{util.origin().x} {util.origin().y} {b.v} {b.show()} {o.thrice()} {o.area()} {util.doubled()} {util.Counter.make().n}");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Members), ("src/main.lyr", main));
        Assert.Equal("0 0 7 2 3 1 2 0\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A method its block keeps to its module is not there for another — the way a hidden
    /// overload is not (07 V2 S5).</summary>
    [Fact]
    public void A_private_extension_method_is_not_there()
    {
        var main = "import app.util;\n\nfn main(): void {\n    let n = util.open().twice();\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Members), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0012]: 'Open' has no member 'twice'", error);
    }

    [Theory]
    [InlineData("pub interface I {\n    pub fn f(): int;\n}\n",
        "LYR-SEM0118", "'pub' on 'f' — an interface's members are as visible as the interface")]
    [InlineData("pub interface I {\n    internal fn f(): int;\n}\n",
        "LYR-SEM0118", "'internal' on 'f' — an interface's members are as visible as the interface")]
    [InlineData("pub interface I {\n    fn f(): int;\n}\n\nstruct S {\n    n: int,\n}\n\npub extend S :: [I] {\n    fn f(): int {\n        return 1;\n    }\n}\n",
        "LYR-SEM0150", "a conformance is as visible as its type and its interface — 'pub' narrows nothing here")]
    [InlineData("pub interface I {\n    fn f(): int;\n}\n\nstruct S {\n    n: int,\n}\n\nextend S :: [I] {\n    private fn f(): int {\n        return 1;\n    }\n}\n",
        "LYR-SEM0150", "'private' on 'f' — a conformance's methods are as visible as the conformance")]
    public void A_word_that_narrows_nothing_is_refused(string declarations, string code, string why)
    {
        var dir = Package(("main.lyr", declarations + "\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {why}", error);
    }

    /// <summary>An interface's private helper (07 V2 S4): its defaults call it, on any value of
    /// the interface; no conformer answers it, and a conformer's method of its name is the
    /// conformer's own — the default still calls the helper.</summary>
    private const string Greeter = """
        interface Greeter {
            fn name(): string;
            fn greet(): string { return this.wrap(this.name()); }
            private fn wrap(s: string): string { return f"<{s}>"; }
        }
        struct P :: [Greeter] {
            n: string,
            fn name(): string { return this.n; }
            fn wrap(s: string): string { return f"[{s}]"; }
        }
        """;

    [Fact]
    public void An_interface_helper_is_its_defaults_and_no_conformers()
    {
        var main = "import std.io { println };\n" + Greeter + """

            interface Box<T> {
                fn get(): T;
                fn show(): string { return this.tag(); }
                private fn tag(): string { return "box"; }
            }
            struct IntBox :: [Box<int>] { v: int, fn get(): int { return this.v; } }

            fn main(): void {
                let p = P { n = "x" };
                let g: Greeter = p;
                println(f"{p.greet()} {g.greet()} {p.wrap("y")} {IntBox { v = 1 }.show()}");
            }
            """;
        var dir = Package(("main.lyr", main));
        Assert.Equal("<x> <x> [y] box\n", BuildAndRun(dir, "main", "build", Path.Combine(dir, "main.lyr")));
    }

    [Theory]
    [InlineData("outside", "fn main(): void {\n    let g: Greeter = P { n = \"x\" };\n    let s = g.wrap(\"z\");\n}\n")]
    [InlineData("a child interface's default", "interface Loud :: [Greeter] {\n    fn twice(): string { return this.wrap(\"b\"); }\n}\n\nfn main(): void {\n}\n")]
    public void An_interface_helper_is_called_by_its_defaults_alone(string where, string rest)
    {
        var dir = Package(("main.lyr", Greeter + "\n" + rest));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 1, $"{where}: exit {exit}\n{error}");
        Assert.Contains("error[LYR-RES0009]: 'wrap' is a helper of interface 'Greeter' — only its defaults call it", error);
        Assert.True(error.Split("error[").Length == 2, $"{where}: one error —\n{error}");
    }

    [Fact]
    public void A_private_requirement_has_no_body_to_help_with()
    {
        var dir = Package(("main.lyr", "interface I {\n    private fn f(): int;\n}\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0118]: 'private' on 'f' — a private member of an interface is a helper and has a body", error);
    }

    /// <summary>A declaration is no more visible than the types it names (07 V2 S2, Swift's general
    /// form): what another module could reach but not use is refused where it is declared.</summary>
    [Theory]
    [InlineData("a pub result", "pub fn make(): Secret {\n    return Secret { n = 1 };\n}\n", "'make' is pub, but it names 'Secret', which is private")]
    [InlineData("an internal parameter", "fn take(s: Secret): int {\n    return s.n;\n}\n", "'take' is internal, but it names 'Secret', which is private")]
    [InlineData("a pub field", "pub struct P {\n    pub s: Secret,\n}\n", "'P.s' is pub, but it names 'Secret', which is private")]
    [InlineData("an internal field", "struct Q {\n    x: Secret,\n}\n", "'Q.x' is internal, but it names 'Secret', which is private")]
    [InlineData("an alias", "pub type Alias = Secret;\n", "'Alias' is pub, but it names 'Secret', which is private")]
    [InlineData("a payload", "pub enum E {\n    A(Secret),\n    B,\n}\n", "'E.A' is pub, but it names 'Secret', which is private")]
    [InlineData("an inferred binding", "pub let g = Secret { n = 3 };\n", "'g' is pub, but it names 'Secret', which is private")]
    [InlineData("an interface member", "pub interface I {\n    fn f(): Secret;\n}\n", "'I.f' is pub, but it names 'Secret', which is private")]
    [InlineData("a constraint", "private interface Hid {\n    fn h(): int;\n}\n\npub fn g<T :: [Hid]>(x: T): int {\n    return x.h();\n}\n", "'g' is pub, but it names 'Hid', which is private")]
    public void A_declaration_names_no_less_visible_type(string what, string declarations, string why)
    {
        var dir = Package(("main.lyr", "private struct Secret {\n    n: int,\n}\n\n" + declarations + "\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 1, $"{what}: exit {exit}\n{error}");
        Assert.Contains($"error[LYR-SEM0151]: {why}", error);
        Assert.True(error.Split("error[").Length == 2, $"{what}: one error —\n{error}");
    }

    /// <summary>A private declaration names private types freely, and a member without a word
    /// follows its type — no warning for what nobody wrote (07 V2 S3).</summary>
    [Fact]
    public void A_private_declaration_names_private_types()
    {
        var text = "private struct Secret {\n    n: int,\n}\n\nprivate fn ok(s: Secret): int {\n    return s.n;\n}\n\nfn main(): int {\n    return ok(Secret { n = 2 });\n}\n";
        var dir = Package(("main.lyr", text));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM015", error);
    }

    /// <summary>A member whose written word is wider than its type's is allowed and said (07 V2
    /// S3): it is exported only once the type is.</summary>
    [Fact]
    public void A_member_wider_than_its_type_warns()
    {
        var dir = Package(("main.lyr", "struct Wide {\n    pub x: int,\n}\n\nfn main(): int {\n    let w = Wide { x = 1 };\n    return w.x;\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("warning[LYR-SEM0152]: 'x' is pub, but its type 'Wide' is internal — the member is exported only once the type is", error);
    }

    /// <summary>A program roots in its entry's main (07 B6): a 'pub' function nothing reaches is
    /// no part of it — the 4.x tools kept every 'pub' function as a library's root.</summary>
    [Fact]
    public void A_program_roots_in_its_main()
    {
        var dir = Package(("roots.lyr", "pub fn unused(): int {\n    return 1;\n}\n\npub fn used(): int {\n    return 2;\n}\n\nfn main(): int {\n    return used();\n}\n"));
        var (exit, output, error) = Run("build", Path.Combine(dir, "roots.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("fn roots.used", output);
        Assert.DoesNotContain("fn roots.unused", output);
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
