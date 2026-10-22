using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>std.meta</c>: what a type is, while
/// compiling. A <c>comptime for</c> over <c>fields&lt;T&gt;()</c> sets its body in place once per
/// field — <c>e.[f]</c> the field of its own type —, and a default that walks <c>Self</c>'s fields
/// is set in place for each conformer. Each answer is a constant of the instance it is asked at;
/// a field is listed where the asking code may name it.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class MetaTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    private static string Manifest(string name, string dependencies = "") =>
        $"[package]\nname = \"{name}\"\nversion = \"0.1.0\"\n{(dependencies.Length > 0 ? "\n" + dependencies : "")}";

    private const string ToJson = """
        interface ToJson {
            fn toJson(): string {
                var out = "{";
                comptime for (f in fields<Self>()) {
                    if (f.index > 0) { out = out + ","; }
                    out = out + f"\"{f.name}\":" + this.[f].toJson();
                }
                return out + "}";
            }
        }

        extend int :: [ToJson] {
            fn toJson(): string { return f"{this}"; }
        }

        extend string :: [ToJson] {
            fn toJson(): string { return f"\"{this}\""; }
        }

        """;

    /// <summary>A synthesis of one's own, <c>ToJson</c>, written once
    /// over <c>Self</c>'s fields — each conformer gets the default set in place for its fields,
    /// each round's <c>this.[f]</c> of that field's type. Nothing is said twice: the hint that a
    /// <c>var</c> is never written read the copies too.</summary>
    [Fact]
    public void A_default_walks_the_fields_of_each_conformer()
    {
        var dir = App("import std.io { println };\nimport std.meta { fields };\n\n" + ToJson + """
            struct Point :: [ToJson] {
                x: int,
                y: int,
            }

            struct Named :: [ToJson] {
                name: string,
                at: Point,
            }

            fn main(): void {
                println(Point { x = 1, y = 2 }.toJson());
                println(Named { name = "a", at = Point { x = 3, y = 4 } }.toJson());
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM0075", error);
        Assert.Equal("{\"x\":1,\"y\":2}\n{\"name\":\"a\",\"at\":{\"x\":3,\"y\":4}}\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A round asks its own sites about its field (<c>comptime if (f.isVar)</c> — each
    /// round's site a site of its own), writes the field in a <c>mut</c> default, sets a generic
    /// conformer's parameters at its instance, and fills the conformer's row: a call through a
    /// value of the interface reaches the default set in place for it.</summary>
    [Fact]
    public void Each_round_is_its_fields_own()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { fields };

            interface Show2 {
                fn show2(): string {
                    var out = "";
                    comptime for (f in fields<Self>()) {
                        comptime if (f.isVar) { out = out + "var "; }
                        out = out + f.name + "=" + this.[f].show2() + " ";
                    }
                    return out;
                }
            }

            interface Reset {
                mut fn reset(): void {
                    comptime for (f in fields<Self>()) {
                        this.[f] = 0;
                    }
                }
            }

            extend int :: [Show2] {
                fn show2(): string { return f"{this}"; }
            }

            struct Counter :: [Show2, Reset] {
                var a: int,
                var b: int,
            }

            struct Pair<T :: [Show2]> :: [Show2] {
                left: T,
                right: T,
            }

            fn main(): void {
                var c = Counter { a = 1, b = 2 };
                println(c.show2());
                c.reset();
                println(c.show2());
                println(Pair<int> { left = 3, right = 4 }.show2());
                let asValue: Show2 = Counter { a = 5, b = 6 };
                println(asValue.show2());
            }
            """);
        Assert.Equal("var a=1 var b=2 \nvar a=0 var b=0 \nleft=3 right=4 \nvar a=5 var b=6 \n",
            BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The questions: a generic function's fields per instance, the name and
    /// kind of a type, an enum's variants and their payloads, the conformances, the members of a
    /// type's own body, the attributes a field and a type carry.</summary>
    [Fact]
    public void The_questions_are_constants_of_the_type_asked()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { fields, variants, conformances, hasConformance, members, attribute, typeAttribute, typeName, isStruct, isEnum };
            import std.core { OnField, OnType };

            struct Rename :: [OnField] { to: string }
            struct Table :: [OnType] { name: string }

            interface Keys {
                fn keys(): string {
                    var out = "";
                    comptime for (f in fields<Self>()) {
                        let renamed = attribute<Rename>(f);
                        out = out + (renamed?.to ?? f.name) + ";";
                    }
                    return out;
                }
            }

            @Table("users")
            struct User :: [Keys] {
                @Rename("id") key: int,
                var name: string,
                fn greet(): string { return "hi"; }
                static fn make(): User { return User { key = 1, name = "a" }; }
                mut fn rename(to: string): void { }
            }

            enum Color { Red, Green(int), Blue { level: int } }

            fn names<T>(): string {
                var s = "";
                comptime for (f in fields<T>()) { s = s + f.name + ":" + f.type.name + ";"; }
                return s;
            }

            fn main(): void {
                println(User.make().keys());
                var list = "";
                comptime for (m in members<User>()) { list = list + m.name + (if (m.isStatic) "!" else "") + (if (m.isMut) "~" else "") + " "; }
                println(list);
                println(typeAttribute<User, Table>()?.name ?? "none");
                println(typeAttribute<Rename, Table>()?.name ?? "none");
                println(names<User>());
                println(f"{typeName<User>()} {isStruct<User>()} {isEnum<User>()} {isEnum<Color>()}");
                var shades = "";
                comptime for (v in variants<Color>()) { shades = shades + v.name + "/" + f"{v.payload.length()}" + " "; }
                println(shades);
                println(f"{hasConformance<User, Keys>()} {hasConformance<Color, Keys>()}");
                var listed = "";
                comptime for (i in conformances<User>()) { listed = listed + i.name + ","; }
                println(f"{listed.contains("Keys")}");
            }
            """);
        Assert.Equal("id;name;\ngreet make! rename~ \nusers\nnone\nkey:int;name:string;\nUser true false true\n"
                     + "Red/0 Green/1 Blue/1 \ntrue false\ntrue\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>R7: the fields listed are those the asking code may name — the interface's module
    /// asks, not the conformer's. Beside the type every field; in another module of the package
    /// the internal ones too; in another package the <c>pub</c> ones alone.</summary>
    [Fact]
    public void A_question_sees_the_fields_its_module_may_name()
    {
        const string keys = "pub interface Keys {\n    fn keys(): string {\n        var out = \"\";\n"
            + "        comptime for (f in fields<Self>()) { out = out + f.name + \";\"; }\n        return out;\n    }\n}\n";
        var dir = Package(("lyric.toml", Manifest("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n")),
            ("src/main.lyr", """
                import std.io { println };
                import std.meta { fields };
                import geo.keys { Keys };
                import app.local { LocalKeys };

                interface HereKeys {
                    fn hereKeys(): string {
                        var out = "";
                        comptime for (f in fields<Self>()) { out = out + f.name + ";"; }
                        return out;
                    }
                }

                struct P :: [Keys, LocalKeys, HereKeys] {
                    pub a: int,
                    b: int,
                    private c: int,
                }

                fn main(): void {
                    let p = P { a = 1, b = 2, c = 3 };
                    println(f"{p.keys()} {p.localKeys()} {p.hereKeys()}");
                }
                """),
            ("src/local.lyr", "import std.meta { fields };\n\n" + keys.Replace("Keys", "LocalKeys").Replace("keys()", "localKeys()")),
            ("deps/geo/lyric.toml", Manifest("geo")),
            ("deps/geo/src/keys.lyr", "import std.meta { fields };\n\n" + keys));
        Assert.Equal("a; a;b; a;b;c;\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What has no round is refused (<c>LYR-SEM0185</c>): <c>e.[f]</c> in a generic
    /// function, where the fields are known only per instance; on a value of another type; with
    /// no loop's variable; <c>attribute</c> of a field no round knows. A site that asks std.meta
    /// about a type parameter inside a larger expression is evaluated once (<c>LYR-SEM0100</c>).</summary>
    [Fact]
    public void What_has_no_round_is_refused()
    {
        var dir = App("""
            import std.meta { fields, attribute };

            fn generic<T>(x: T): int {
                comptime for (f in fields<T>()) {
                    let v = x.[f];
                }
                return 0;
            }

            struct P { a: int }
            struct Q { a: int }

            fn mismatch(p: P, q: Q): int {
                comptime for (f in fields<P>()) {
                    let w = q.[f];
                }
                return 0;
            }

            fn outside(p: P, f: int): int {
                return p.[f];
            }

            fn noRound<T>(): int {
                comptime for (f in fields<T>()) {
                    let r = attribute<P>(f);
                }
                return 0;
            }

            fn sized<T>(): int {
                return comptime fields<T>().length();
            }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Equal(5, errors.Length);
        Assert.Contains(errors, l => l.Contains("main.lyr:5:17: error[LYR-SEM0185]"));
        Assert.Contains(errors, l => l.Contains("main.lyr:15:17: error[LYR-SEM0185]") && l.Contains("holds a field of 'P', and this is 'Q'"));
        Assert.Contains(errors, l => l.Contains("main.lyr:21:12: error[LYR-SEM0185]"));
        Assert.Contains(errors, l => l.Contains("main.lyr:26:17: error[LYR-SEM0185]") && l.Contains("'attribute'"));
        Assert.Contains(errors, l => l.Contains("main.lyr:32:21: error[LYR-SEM0100]") && l.Contains("std.meta"));
    }

    /// <summary>Every declaration that takes an attribute has a reader —
    /// a variant's through <c>variantAttribute</c>, a member's through <c>memberAttribute</c>, a
    /// payload field's through <c>attribute</c> in a round over the payload, a type's, an interface's
    /// and an alias's own through <c>typeAttribute</c>. A round over members decides a
    /// <c>comptime if</c> with the reader and with <c>m.isStatic</c>.</summary>
    [Fact]
    public void Every_attribute_target_has_its_reader()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { variants, members, attribute, variantAttribute, memberAttribute, typeAttribute };
            import std.core { OnField, OnVariant, OnMethod, OnTypeLike };

            struct Tag :: [OnTypeLike] { name: string }
            struct Mark :: [OnField] { to: string }
            struct Wire :: [OnVariant] { code: int }
            struct Route :: [OnMethod] { path: string }

            @Tag("iface")
            interface Greets { fn greet(): string; }

            @Tag("alias")
            type Id = int;

            @Tag("st")
            struct S :: [Greets] {
                @Mark("k") key: int,
                fn greet(): string { return "hi"; }
                @Route("/make") static fn make(): S { return S { key = 1 }; }
            }

            enum Shape {
                @Wire(1) Dot,
                @Wire(2) Box { @Mark("w") width: int, height: int },
                Line(int),
            }

            fn main(): void {
                println(typeAttribute<S, Tag>()?.name ?? "none");
                println(typeAttribute<Greets, Tag>()?.name ?? "none");
                println(typeAttribute<Id, Tag>()?.name ?? "none");
                println(typeAttribute<int, Tag>()?.name ?? "none");
                var out = "";
                comptime for (v in variants<Shape>()) {
                    let code = variantAttribute<Wire>(v)?.code ?? 0;
                    out = out + v.name + "#" + f"{code}";
                    comptime for (p in v.payload) {
                        out = out + " " + p.name + "=" + (attribute<Mark>(p)?.to ?? "-");
                    }
                    out = out + ";";
                }
                println(out);
                var routes = "";
                comptime for (m in members<S>()) {
                    comptime if (memberAttribute<Route>(m) != null) {
                        routes = routes + m.name + "->" + (memberAttribute<Route>(m)?.path ?? "") + ";";
                    }
                    comptime if (m.isStatic) {
                        routes = routes + m.name + "!;";
                    }
                }
                println(routes);
            }
            """);
        Assert.Equal("st\niface\nalias\nnone\nDot#1;Box#2 width=w height=-;Line#0 0=-;\nmake->/make;make!;\n",
            BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Each reader asks its own loop's variable: a type parameter's loop over members or
    /// variants has no rounds (<c>LYR-SEM0185</c>), and a field's info is no variant's.</summary>
    [Fact]
    public void A_reader_asks_its_own_round()
    {
        var dir = App("""
            import std.meta { fields, variants, members, variantAttribute, memberAttribute };
            import std.core { OnVariant, OnMethod };

            struct Wire :: [OnVariant] { }
            struct Route :: [OnMethod] { }

            fn routesOf<T>(): int {
                comptime for (m in members<T>()) {
                    let r = memberAttribute<Route>(m);
                }
                return 0;
            }

            fn wiresOf<E>(): int {
                comptime for (v in variants<E>()) {
                    let r = variantAttribute<Wire>(v);
                }
                return 0;
            }

            struct P { a: int }

            fn mixed(): int {
                comptime for (f in fields<P>()) {
                    let r = variantAttribute<Wire>(f);
                }
                return 0;
            }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Equal(3, errors.Length);
        Assert.Contains(errors, l => l.Contains("main.lyr:9:17: error[LYR-SEM0185]") && l.Contains("'memberAttribute' reads the member"));
        Assert.Contains(errors, l => l.Contains("main.lyr:16:17: error[LYR-SEM0185]") && l.Contains("'variantAttribute' reads the variant"));
        Assert.Contains(errors, l => l.Contains("main.lyr:25:40: error[LYR-SEM0001]") && l.Contains("'VariantInfo'"));
    }

    /// <summary>11 §4 rules 1 and 9: <c>hasDefault</c> is whether the
    /// field's declaration writes an initializer, <c>f.type.kind</c> what its type is, and a round
    /// decides a <c>comptime if</c> over both — written short or qualified. A payload's field has
    /// no default.</summary>
    [Fact]
    public void A_field_info_says_its_kind_and_its_default()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { fields, variants, TypeKind };

            enum Color { Red, Green, Box { w: int = 1, h: int } }

            interface Greets { fn greet(): string; }

            struct Opts {
                port: int = 8080,
                name: ?string,
                verbose: bool = false,
                tags: string[],
                color: Color = .Red,
                pair: (int, int),
                hello: Greets,
                text: string = "",
            }

            fn main(): void {
                var out = "";
                comptime for (f in fields<Opts>()) {
                    out = out + f.name + ":" + f.type.kind.debug() + ":" + f"{f.hasDefault}";
                    comptime if (f.hasDefault) { out = out + ":d"; }
                    comptime if (f.type.kind == .Optional) { out = out + ":o"; }
                    comptime if (f.type.kind == TypeKind.Enum) { out = out + ":e"; }
                    comptime if (f.type.kind != .Primitive && !f.hasDefault) { out = out + ":req"; }
                    out = out + " ";
                }
                println(out);
                var pay = "";
                comptime for (v in variants<Color>()) {
                    comptime for (p in v.payload) { pay = pay + p.name + f"{p.hasDefault}" + " "; }
                }
                println(pay);
            }
            """);
        Assert.Equal("port:TypeKind.Primitive:true:d name:TypeKind.Optional:false:o:req verbose:TypeKind.Primitive:true:d "
                     + "tags:TypeKind.Array:false:req color:TypeKind.Enum:true:d:e pair:TypeKind.Tuple:false:req "
                     + "hello:TypeKind.Interface:false:req text:TypeKind.Primitive:true:d \nwtrue hfalse \n",
            BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>An error in a default set in place says where: the round's field and the conformer
    /// that takes the default, as notes of the line written once.</summary>
    [Fact]
    public void An_error_in_a_round_names_the_field_and_the_conformer()
    {
        var dir = App("import std.meta { fields };\n\n" + ToJson + """
            struct Holder :: [ToJson] {
                n: int,
                flag: bool,
            }

            fn main(): void {
                let s = Holder { n = 1, flag = true }.toJson();
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var at = error.IndexOf("error[LYR-SEM0012]: 'bool' has no member 'toJson'", StringComparison.Ordinal);
        Assert.True(at >= 0, error);
        Assert.Single(error.Split('\n'), l => l.Contains("error["));
        var notes = error[at..];
        Assert.Contains("note: in the round for the field 'flag' of 'Holder'", notes);
        Assert.Contains("note: 'toJson' of 'ToJson' set in place for 'Holder', which takes it", notes);
    }
}
