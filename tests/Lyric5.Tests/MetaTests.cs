using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>std.meta</c> (design/v5/spec/09 A7; 10 B13 M1; 13 M9a P5, S9): what a type is, while
/// compiling. A <c>comptime for</c> over <c>fields&lt;T&gt;()</c> sets its body in place once per
/// field — <c>e.[f]</c> the field of its own type —, and a default that walks <c>Self</c>'s fields
/// is set in place for each conformer. Each answer is a constant of the instance it is asked at;
/// a field is listed where the asking code may name it (R7).
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

    /// <summary>The artifact (13 M9a S9): a synthesis of one's own, <c>ToJson</c>, written once
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

    /// <summary>The questions (10 B13 M1): a generic function's fields per instance, the name and
    /// kind of a type, an enum's variants and their payloads, the conformances, the members of a
    /// type's own body, the attributes a field and a type carry (R4).</summary>
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
