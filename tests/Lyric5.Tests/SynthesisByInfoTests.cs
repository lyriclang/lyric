using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The synthesis as std.core's defaults over the fields and variants (spec 05 §14): <c>Equatable</c>, <c>Hashable</c>, <c>Ordered</c>, <c>TotalOrder</c> and <c>Clone</c>
/// are written once in std.core over <c>fields&lt;Self&gt;()</c> and <c>variants&lt;Self&gt;()</c> and set
/// in place for each conformer; the forms that made it possible — a round per variant, <c>e is v</c>,
/// <c>E.[v]</c>, <c>e.[v, p]</c>, <c>E.[v] { … }</c>, an initializer over infos, a <c>comptime if</c> a
/// round decides — are the program's too.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class SynthesisByInfoTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    /// <summary>The family on a struct (05 §14 rules 1, 2): compares, orders, hashes and clones field
    /// by field — the internal field too —; <c>Default</c> stays the compiler's.</summary>
    [Fact]
    public void The_family_is_synthesized_over_a_structs_fields()
    {
        var dir = App("""
            import std.io { println };
            import std.collections { Set };

            struct P :: [Equatable, Hashable, Ordered, Clone, Default] {
                x: int,
                name: string,
                internal n: int = 7,
                tag: string,
            }

            fn main(): void {
                let a = P { x = 1, name = "a", tag = "ab" };
                let b = P { x = 1, name = "a", tag = "ab" };
                let c = P { x = 2, name = "a", tag = "" };
                println(f"{a == b} {a == c} {a < c} {c < a}");
                let d = a.clone();
                println(f"{d == a} {P.default().x} {P.default().name == ""} {P.default().n}");
                var s = Set<P>.new();
                s.insert(a);
                s.insert(b);
                s.insert(c);
                println(f"{s.length()}");
            }
            """);
        Assert.Equal("true false true false\ntrue 0 true 7\n2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The family on an enum (05 §14 rule 3): the same variant with equal payloads, the
    /// variant's position first then the payload, the variant rebuilt, a key per value.</summary>
    [Fact]
    public void The_family_is_synthesized_over_an_enums_variants()
    {
        var dir = App("""
            import std.io { println };
            import std.collections { Set };

            enum E :: [Equatable, Ordered, Clone, Hashable] { A, B(int, string), C { w: int } }

            fn main(): void {
                let a = E.A;
                let b1 = E.B(1, "x");
                let b2 = E.B(1, "x");
                let b3 = E.B(2, "x");
                let c = E.C { w = 4 };
                println(f"{a == a} {b1 == b2} {b1 == b3} {a == b1}");
                println(f"{a < b1} {b1 < b3} {b3 < b1} {c < a} {b1 < c}");
                let k = b1.clone();
                var s = Set<E>.new();
                s.insert(a);
                s.insert(b1);
                s.insert(b2);
                println(f"{k == b1} {c.clone() == c} {s.length()}");
            }
            """);
        Assert.Equal("true true false false\ntrue true false false true\ntrue true 2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A <c>@Shared</c> field is copied, not cloned: the <c>comptime if</c>
    /// in <c>Clone</c>'s default decides per field and checks the chosen arm alone — the other names a
    /// <c>clone</c> the class has not.</summary>
    [Fact]
    public void A_shared_field_is_copied_by_the_default()
    {
        var dir = App("""
            import std.io { println };

            class Parent { name: string }
            struct Child :: [Clone] {
                n: int,
                @Shared parent: Parent,
            }

            fn main(): void {
                let p = Parent { name = "p" };
                let c = Child { n = 1, parent = p };
                let d = c.clone();
                println(f"{d.n} {same(d.parent, p)}");
            }
            """);
        Assert.Equal("1 true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A generic conformer (05 §14 rule 4): the block <c>extend&lt;T :: [Equatable]&gt; Pair&lt;T&gt;</c>
    /// carries the condition, the default compares under it, the instance binds the block's
    /// parameter; an instance over a type without it is refused at the comparison.</summary>
    [Fact]
    public void A_generic_conformer_compares_under_the_blocks_constraint()
    {
        var dir = App("""
            import std.io { println };
            struct Pair<T> :: [Equatable] { a: T, b: T }
            fn main(): void {
                let p = Pair<int> { a = 1, b = 2 };
                let q = Pair<int> { a = 1, b = 2 };
                println(f"{p == q} {p == Pair<int> { a = 1, b = 3 }}");
            }
            """);
        Assert.Equal("true false\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var refused = App("""
            import std.io { println };
            struct NoEq { v: int }
            struct Pair<T> :: [Equatable] { a: T, b: T }
            fn main(): void {
                let p = Pair<NoEq> { a = NoEq { v = 1 }, b = NoEq { v = 2 } };
                println(f"{p == p}");
            }
            """);
        var (exit, _, error) = Run("build", "-C", refused);
        Assert.Equal(1, exit);
        Assert.Contains("main.lyr:6:16: error[LYR-SEM0059]", error);
        Assert.DoesNotContain("LYR-SEM0135", error);
    }

    /// <summary>What is refused is said at the entry (05 §14 rule 6): the missing operation's own
    /// diagnostic, at the conformance-list entry, with the field's round and the default's line as
    /// notes; a shape the synthesis has no form for is <c>LYR-SEM0135</c>; an enum's <c>Default</c>
    /// stays refused.</summary>
    [Fact]
    public void What_is_refused_is_said_at_the_entry()
    {
        var dir = App("""
            struct NoEq { v: int }
            struct Q :: [Equatable] { f: NoEq }
            struct H :: [Hashable] { xs: int[] }
            enum X :: [Default] { A, B }
            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Equal(3, errors.Length);
        Assert.Contains(errors, l => l.Contains("main.lyr:2:14: error[LYR-SEM0059]") && l.Contains("'Equatable' is not synthesized for 'Q'") && l.Contains("write the member"));
        Assert.Contains(errors, l => l.Contains("main.lyr:3:14: error[LYR-SEM0135]") && l.Contains("'xs' is an array or a view"));
        Assert.Contains(errors, l => l.Contains("main.lyr:4:12: error[LYR-SEM0135]") && l.Contains("say which variant"));
        Assert.Contains("note: in the round for the field 'f' of 'Q'", error);
        Assert.Contains("note: where the default says it", error);
    }

    /// <summary>The forms in a program of its own: a round per variant, <c>e is v</c>,
    /// <c>E.[v]</c>, <c>e.[v, p]</c>, <c>E.[v] { … }</c> and an initializer over infos.</summary>
    [Fact]
    public void The_variant_forms_build_test_and_read_a_variant()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { fields, variants, isEnum };

            enum Shape :: [Equatable, Named, Sum, Copy] { Dot, Circle(int, string), Rect { w: int, h: int } }
            struct P :: [Equatable, Copy] { x: int, name: string }

            interface Named {
                fn name(): string {
                    comptime for (v in variants<Self>()) {
                        if (this is v) {
                            return v.name;
                        }
                    }
                    return "?";
                }
                fn byIndex(i: int): ?Self {
                    comptime for (v in variants<Self>()) {
                        comptime if (v.index == 0) { // 'Dot', the unit variant; a round decides the condition
                            if (v.index == i) {
                                return Self.[v];
                            }
                        }
                    }
                    return null;
                }
            }

            interface Sum {
                fn sum(): int {
                    var s = 0;
                    comptime for (v in variants<Self>()) {
                        if (this is v) {
                            comptime for (p in v.payload) {
                                comptime if (p.index == 0) {
                                    s = s + this.[v, p].toInt();
                                } else {
                                    s = s + 100;
                                }
                            }
                        }
                    }
                    return s;
                }
            }

            extend int :: [ToInt] { fn toInt(): int { return this; } }
            extend string :: [ToInt] { fn toInt(): int { return this.length(); } }
            interface ToInt { fn toInt(): int; }

            interface Copy {
                fn copy(): Self {
                    comptime if (isEnum<Self>()) {
                        comptime for (v in variants<Self>()) {
                            if (this is v) {
                                return Self.[v] { comptime for (p in v.payload) [p] = this.[v, p] };
                            }
                        }
                        return this;
                    } else {
                        return Self { comptime for (f in fields<Self>()) [f] = this.[f] };
                    }
                }
            }

            fn main(): void {
                let c = Shape.Circle(3, "xy");
                let r = Shape.Rect { w = 4, h = 8 };
                println(f"{Shape.Dot.name()} {c.name()} {r.name()}");
                println(f"{Shape.Dot.byIndex(0) == Shape.Dot} {Shape.Dot.byIndex(1) == null} {c.sum()} {r.sum()}");
                println(f"{c.copy() == c} {r.copy() == r} {P { x = 1, name = "a" }.copy() == P { x = 1, name = "a" }}");
            }
            """);
        Assert.Equal("Dot Circle Rect\ntrue true 103 104\ntrue true true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The forms refused outside their rounds (<c>LYR-SEM0185</c>), a payload of another
    /// variant as a panic, and a write through an info answering to the field's word
    /// (<c>LYR-SEM0019</c>).</summary>
    [Fact]
    public void The_forms_are_refused_outside_their_rounds()
    {
        var dir = App("""
            import std.meta { fields, variants };

            enum E { A, B(int) }
            struct Row :: [Touch] { tag: string }

            fn outside(e: E, v: int, p: int): int {
                let a = e.[v, p];
                let b = e is v;
                let c = E.[v];
                return 0;
            }

            interface Touch {
                fn touch(): string {
                    comptime for (f in fields<Self>()) {
                        this.[f] = this.[f];
                    }
                    return "touched";
                }
            }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Contains(errors, l => l.Contains("main.lyr:7:13: error[LYR-SEM0185]"));
        Assert.Contains(errors, l => l.Contains("main.lyr:9:13: error[LYR-SEM0185]"));
        Assert.Contains(errors, l => l.Contains("main.lyr:16:13: error[LYR-SEM0019]"));
        Assert.Equal(4, errors.Length); // 'e is v' on an enum value: the type test's own refusal

        var panics = App("""
            import std.io { println };
            import std.meta { variants };

            enum Shape :: [First] { Dot, Circle(int) }

            interface First {
                fn first(): int {
                    comptime for (v in variants<Self>()) {
                        comptime if (v.index == 1) {
                            comptime for (p in v.payload) {
                                return this.[v, p];
                            }
                        }
                    }
                    return -1;
                }
            }

            fn main(): void {
                println(f"{Shape.Circle(7).first()}");
                println(f"{Shape.Dot.first()}");
            }
            """);
        var (built, _, buildError) = Run("build", "-C", panics);
        Assert.True(built == 0, buildError);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(panics, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("7\n", ran.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("is not the variant 'Circle'", ran.Stderr);
    }
    [Fact]
    public void A_lambda_captures_a_rounds_info_and_a_member_of_it_folds()
    {
        var dir = App("""
            import std.io { println };
            import std.meta { fields, FieldInfo };

            struct P :: [Names] { x: int, name: string }

            fn describe(f: FieldInfo): string {
                return f.name + "@" + f"{f.index}";
            }

            interface Names {
                fn names(): string {
                    var out = "";
                    comptime for (f in fields<Self>()) {
                        let get = () => f;
                        let say = () => describe(f);
                        out = out + get().name + "=" + say() + ";";
                    }
                    return out;
                }
            }

            fn main(): void {
                println(P { x = 1, name = "a" }.names());
            }
            """);
        Assert.Equal("x=x@0;name=name@1;\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
