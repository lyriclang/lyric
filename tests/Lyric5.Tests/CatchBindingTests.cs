using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The type of a catch binding (design/v5/spec/05 K7, C5; the review's M5-6): where only ONE
/// type can arrive, the binding is that type — in the set form of one, <c>catch (e in A)</c>,
/// which is <c>catch (e: A)</c>, and in the clause without a type that one type reaches. With
/// several it stays an <c>Error</c> that carries the set. It was an <c>Error</c> always, so the
/// field of the one error a function throws was out of reach without a second test
/// (<c>LYR-SEM0012</c>).
///
/// <para>And a clause without a type that NOTHING reaches — the clauses above take every type the
/// block throws — is warned about (<c>LYR-SEM0171</c>; the review's M5-16). It was silent.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class CatchBindingTests
{
    private const string Errors = """
        import std.io { println };
        import std.core { Join };

        struct NotFound :: [Error] {
            path: string,

            fn message(): string {
                return f"no {this.path}";
            }
        }

        struct Denied :: [Error] {
            code: int,

            fn message(): string {
                return f"denied {this.code}";
            }
        }

        fn find(p: string): int throws NotFound {
            if (p == "") {
                throw NotFound { path = "empty" };
            }
            return 1;
        }

        fn open(p: string): int throws [NotFound, Denied] {
            if (p == "x") {
                throw Denied { code = 7 };
            }
            return try find(p);
        }

        """;

    private static (int Exit, string Error, string Dir) Built(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Errors + main));
        var (exit, _, error) = Run("build", "-C", dir);
        return (exit, error, dir);
    }

    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Errors + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>The one error a program is refused with: its text. A second error fails.</summary>
    private static string Refused(string main, string code)
    {
        var (exit, error, _) = Built(main);
        Assert.True(exit != 0, "the program was taken");
        var codes = Regex.Matches(error, @"error\[(LYR-[A-Z]+\d+)\]").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(codes.Length == 1 && codes[0] == code, $"expected exactly one {code}, got:\n{error}");
        return error;
    }

    // ------------------------------------------------------------------ one type

    [Fact]
    public void The_one_type_that_can_arrive_is_the_bindings()
    {
        Assert.Equal("empty\nempty\n7\n", Output("""
            fn main(): void {
                try {
                    println(f"{find("")}");
                } catch (e) {
                    println(e.path);
                }
                try {
                    println(f"{open("")}");
                } catch (e in NotFound) {
                    println(e.path);
                } catch (e) {
                    println(f"{e.code}");
                }
                try {
                    println(f"{open("x")}");
                } catch (e in NotFound) {
                    println(e.path);
                } catch (e) {
                    println(f"{e.code}");
                }
            }
            """));
    }

    /// <summary>The binding is still an error wherever one is asked: an <c>Error</c> parameter,
    /// an <c>Error</c> binding, and <c>throw e</c> — the same error going on (05 E6 O3).</summary>
    [Fact]
    public void A_binding_of_one_type_is_an_error_where_one_is_asked()
    {
        Assert.Equal("no empty\nno empty\nagain: empty\n", Output("""
            fn describe(e: Error): string {
                return e.message();
            }

            fn again(): int throws NotFound {
                try {
                    return find("");
                } catch (e) {
                    println(describe(e));
                    let boxed: Error = e;
                    println(boxed.message());
                    throw e;
                }
            }

            fn main(): void {
                try {
                    println(f"{again()}");
                } catch (e) {
                    println(f"again: {e.path}");
                }
            }
            """));
    }

    /// <summary>In a generic function the one type is the parameter: the binding is an
    /// <c>E</c>, and goes out as one.</summary>
    [Fact]
    public void The_one_type_may_be_a_type_parameter()
    {
        Assert.Equal("empty none\n", Output("""
            fn caught<E :: [Error]>(body: fn() -> int throws E): ?E {
                try {
                    body();
                    return null;
                } catch (e) {
                    return e;
                }
            }

            fn fails(): int throws NotFound {
                return try find("");
            }

            fn passes(): int throws NotFound {
                return try find("ok");
            }

            fn main(): void {
                let first = caught(fails);
                let second = caught(passes);
                println(f"{first?.path ?? "none"} {second?.path ?? "none"}");
            }
            """));
    }

    // ------------------------------------------------------------------ several

    [Fact]
    public void Several_types_stay_an_error_that_carries_the_set()
    {
        Assert.Equal("denied 7\nno empty\nmatched 7\n", Output("""
            fn main(): void {
                try {
                    println(f"{open("x")}");
                } catch (e) {
                    println(e.message());
                }
                try {
                    println(f"{open("")}");
                } catch (e in [NotFound, Denied]) {
                    println(e.message());
                }
                try {
                    println(f"{open("x")}");
                } catch (e) {
                    match (e) {
                        n: NotFound => println(f"matched {n.path}"),
                        d: Denied => println(f"matched {d.code}"),
                    }
                }
            }
            """));
    }

    /// <summary>The variants of the one type are there: a match over the binding is the enum's
    /// own, exhaustive by its variants, with no type pattern and no default.</summary>
    [Fact]
    public void A_binding_of_one_enum_type_is_matched_by_its_variants()
    {
        Assert.Equal("empty | bad -3 | ok 5\n", Output("""
            enum ParseError :: [Error] {
                Empty,
                Bad(int);

                fn message(): string {
                    return "parse";
                }
            }

            fn parse(n: int): int throws ParseError {
                if (n == 0) {
                    throw ParseError.Empty;
                }
                if (n < 0) {
                    throw ParseError.Bad(n);
                }
                return n;
            }

            fn classify(n: int): string {
                try {
                    return f"ok {parse(n)}";
                } catch (e) {
                    return match (e) {
                        .Empty => "empty",
                        .Bad(k) => f"bad {k}",
                    };
                }
            }

            fn main(): void {
                println(f"{classify(0)} | {classify(-3)} | {classify(5)}");
            }
            """));
    }

    /// <summary>An OPEN join — the set of a generic function that joins its parameters' — is
    /// one entry of the set a clause is reached by, and several types under one name: the
    /// binding is an <c>Error</c>, and its <c>message()</c> the thrown value's, not the join's.</summary>
    [Fact]
    public void An_open_join_is_not_one_type()
    {
        Assert.Equal("no empty | 2\n", Output("""
            fn pair<A :: [Error], B :: [Error]>(f: fn() -> int throws A, g: fn() -> int throws B): int throws Join<A, B> {
                let x = try f();
                let y = try g();
                return x + y;
            }

            fn outer<A :: [Error], B :: [Error]>(f: fn() -> int throws A, g: fn() -> int throws B): string {
                try {
                    return f"{pair(f, g)}";
                } catch (e) {
                    return e.message();
                }
            }

            fn main(): void {
                println(f"{outer(() => try find(""), () => try find("ok"))} | {outer(() => try find("a"), () => try find("b"))}");
            }
            """));
    }

    /// <summary>A join of two — what a generic function throws when its argument throws two
    /// (K7) — is two types under one name, and no type for a binding.</summary>
    [Fact]
    public void A_join_is_not_one_type()
    {
        Assert.Equal("denied 7\n", Output("""
            fn run<E :: [Error]>(body: fn() -> int throws E): int throws E {
                return try body();
            }

            fn main(): void {
                try {
                    println(f"{run(() => try open("x"))}");
                } catch (e) {
                    println(e.message());
                }
            }
            """));
    }

    /// <summary>The field of one type breaks when a second type can arrive: the binding is an
    /// <c>Error</c> again.</summary>
    [Theory]
    [InlineData("try {\n        println(f\"{open(\"\")}\");\n    } catch (e) {\n        println(e.path);\n    }")]
    [InlineData("try {\n        println(f\"{open(\"\")}\");\n    } catch (e in [NotFound, Denied]) {\n        println(e.path);\n    }")]
    public void A_second_type_takes_the_field_away(string statement)
    {
        var error = Refused("fn main(): void {\n    " + statement + "\n}\n", "LYR-SEM0012");
        Assert.Contains("'Error' has no member 'path'", error);
    }

    /// <summary>A type pattern asks an interface what it holds (05 §9 rule 3): over a binding
    /// that IS the one type there is nothing to ask.</summary>
    [Fact]
    public void A_type_pattern_over_a_binding_of_one_type_is_refused() =>
        Refused("""
            fn main(): void {
                try {
                    println(f"{find("")}");
                } catch (e) {
                    match (e) {
                        n: NotFound => println(n.path),
                        _ => println("other"),
                    }
                }
            }
            """, "LYR-SEM0131");

    // ------------------------------------------------------------------ nothing reaches it

    /// <summary>The clauses above take every type the block throws: the clause without a type
    /// is dead, and says so (the review's M5-16, a warning).</summary>
    [Theory]
    [InlineData("try {\n        println(f\"{find(\"\")}\");\n    } catch (e: NotFound) {\n        println(e.path);\n    } catch (_) {\n        println(\"never\");\n    }", "empty\n")]
    [InlineData("try {\n        println(f\"{open(\"x\")}\");\n    } catch (e in [NotFound, Denied]) {\n        println(e.message());\n    } catch (_) {\n        println(\"never\");\n    }", "denied 7\n")]
    public void A_clause_without_a_type_that_nothing_reaches_is_warned_about(string statement, string printed)
    {
        var (exit, error, dir) = Built("fn main(): void {\n    " + statement + "\n}\n");
        Assert.True(exit == 0, error);
        Assert.Single(Regex.Matches(error, @"warning\[LYR-SEM0171\]"));
        Assert.Contains("nothing reaches this clause", error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(
            Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromMinutes(1));
        Assert.Equal(printed, ran.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>Where the block throws nothing at all, the older warning speaks, alone
    /// (<c>LYR-SEM0139</c>); and a clause something reaches has no warning.</summary>
    [Fact]
    public void The_warning_is_for_the_dead_clause_alone()
    {
        var (exit, quiet, _) = Built("fn main(): void {\n    try {\n        println(\"quiet\");\n    } catch (_) {\n        println(\"never\");\n    }\n}\n");
        Assert.True(exit == 0, quiet);
        Assert.Contains("warning[LYR-SEM0139]", quiet);
        Assert.DoesNotContain("LYR-SEM0171", quiet);

        var (exit2, live, _) = Built("fn main(): void {\n    try {\n        println(f\"{open(\"x\")}\");\n    } catch (e: NotFound) {\n        println(e.path);\n    } catch (e) {\n        println(e.message());\n    }\n}\n");
        Assert.True(exit2 == 0, live);
        Assert.DoesNotContain("LYR-SEM0171", live);
    }
}
