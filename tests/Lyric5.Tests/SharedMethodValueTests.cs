using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A name several functions share, named through something (spec 08 §1.2 rule 3): the function type
/// the position expects says which, as for a bare name. Through <c>Main</c>, so in the console
/// collection. Also the type arguments on a member, which read as a path and are no type.
/// </summary>
[Collection("console")]
public class SharedMethodValueTests
{
    private const string Declarations = """
        import std.io { IoError, println };
        import std.task { Cancelled };
        import app.util;

        struct V {
            n: int,
            fn o(a: int): int { return this.n + a; }
            fn o(a: int, b: int): int { return this.n + a + b; }
            static fn s(a: int): int { return a; }
            static fn s(a: int, b: int): int { return a + b; }
            fn mixed<U>(u: U): U { return u; }
            fn mixed(a: int, b: int): int { return a + b; }
        }
        extend V { fn p(a: int): int { return a + 100; } fn p(a: int, b: int): int { return a + b + 100; } }
        struct Pack<T> {
            item: T,
            fn o(a: int): int { return a; }
            fn o(a: int, b: T): T { return b; }
            static fn st(a: T): T { return a; }
            static fn st(a: T, b: T): T { return b; }
        }
        class C { n: int, fn gm<U>(u: U): U { return u; } fn plain(): int { return 1; } }
        struct Outer { c: C }
        fn free(a: int): int { return a; }
        fn free(a: int, b: int): int { return a + b; }
        fn apply(f: fn(int, int) -> int): int { return f(1, 2); }

        """;

    private const string Util = """
        pub fn f(a: int): int { return a; }
        pub fn f(a: int, b: int): int { return a + b; }
        """;

    private static string Dir(string body, string functions = "") => Package(("lyric.toml", AppManifest), ("src/util.lyr", Util),
        ("src/main.lyr", Declarations + functions + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));

    private static string Output(string body, string functions = "")
    {
        var dir = Dir(body, functions);
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string body)
    {
        var (exit, _, err) = Run("check", "-C", Dir(body));
        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("LYR-ICE", err);
        return err;
    }

    [Theory]
    [InlineData("let v = V { n = 1 }; let h: fn(int, int) -> int = v.o; println(h(1, 2));", "4\n")]
    [InlineData("let v = V { n = 1 }; let h: fn(int) -> int = v.o; println(h(1));", "2\n")]
    // a block's, a generic instance's at its instance, a static's, a module's
    [InlineData("let v = V { n = 1 }; let h: fn(int, int) -> int = v.p; println(h(1, 2));", "103\n")]
    [InlineData("let b = Pack<int> { item = 2 }; let h: fn(int, int) -> int = b.o; println(h(1, 7));", "7\n")]
    [InlineData("let b = Pack<string> { item = \"x\" }; let h: fn(int, string) -> string = b.o; println(h(1, \"y\"));", "y\n")]
    [InlineData("let h: fn(int, int) -> int = V.s; println(h(1, 2));", "3\n")]
    [InlineData("let h: fn(int, int) -> int = Pack<int>.st; println(h(1, 2));", "2\n")]
    [InlineData("let h: fn(int, int) -> int = util.f; println(h(1, 2));", "3\n")]
    // the position: an argument, an optional, also through a chain and for a bare name
    [InlineData("let v = V { n = 1 }; println(apply(v.o));", "4\n")]
    [InlineData("let v = V { n = 1 }; let h: ?fn(int, int) -> int = v.o; println((h!)(1, 2));", "4\n")]
    [InlineData("let v: ?V = V { n = 1 }; let h: ?fn(int, int) -> int = v?.o; println((h!)(1, 2));", "4\n")]
    [InlineData("let h: ?fn(int, int) -> int = free; println((h!)(1, 2));", "3\n")]
    // a generic one beside it is no candidate for a function type
    [InlineData("let v = V { n = 1 }; let h: fn(int, int) -> int = v.mixed; println(h(1, 2));", "3\n")]
    public void The_position_chooses(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    /// <summary>A return is a position that names the type.</summary>
    [Fact]
    public void A_return_chooses() =>
        Assert.Equal("4\n", Output("println(back(V { n = 1 })(1, 2));", "fn back(v: V): fn(int, int) -> int { return v.o; }"));

    /// <summary>Nothing to choose by, or nothing that fits. The refusal names what there is.</summary>
    [Theory]
    [InlineData("let v = V { n = 1 }; let h = v.o;", "'o' names 2 functions, and nothing here says which")]
    [InlineData("let h = V.s;", "'s' names 2 functions, and nothing here says which")]
    [InlineData("let h = util.f;", "'f' names 2 functions, and nothing here says which")]
    [InlineData("let v = V { n = 1 }; let h: fn(string) -> int = v.o;", "none of them is a 'fn(string) -> int'")]
    [InlineData("let v = V { n = 1 }; let h: fn(int) -> int = v.mixed;", "none of them is a 'fn(int) -> int'")]
    public void Nothing_chooses(string body, string message)
    {
        var err = Refused(body);
        Assert.Contains("LYR-SEM0089", err);
        Assert.Contains(message, err);
    }

    /// <summary>A method of one function needs no position to say which.</summary>
    [Fact]
    public void A_single_function_needs_no_choice() =>
        Assert.Equal("2\n", Output("let c = C { n = 1 }; let g = c.plain; println(g() + 1);"));

    /// <summary><c>c.gm&lt;int&gt;</c> reads as a path, whose head here is a value: the refusal
    /// speaks of the member, not of a type nobody wrote.</summary>
    [Theory]
    [InlineData("let c = C { n = 1 }; let g = c.gm<int>;", "LYR-SEM0052", "generic method 'gm' is not a value, also with its type arguments written")]
    [InlineData("let o = Outer { c = C { n = 1 } }; let g = o.c.gm<int>;", "LYR-SEM0052", "generic method 'gm' is not a value, also with its type arguments written")]
    [InlineData("let c = C { n = 1 }; let g = c.gm<_>;", "LYR-SEM0052", "generic method 'gm' is not a value, also with its type arguments written")]
    [InlineData("let c = C { n = 1 }; let g = c.plain<int>;", "LYR-SEM0026", "'plain' is not generic and takes no type arguments")]
    [InlineData("let c = C { n = 1 }; let g = c.n<int>;", "LYR-SEM0026", "'n' takes no type arguments")]
    [InlineData("let c = C { n = 1 }; let g = c.nosuch<int>;", "LYR-SEM0012", "'C' has no member 'nosuch'")]
    public void Type_arguments_on_a_values_member_are_no_type(string body, string code, string message)
    {
        var err = Refused(body);
        Assert.Contains(code, err);
        Assert.Contains(message, err);
        Assert.DoesNotContain("LYR-SEM0011", err);
    }

    /// <summary>A path whose head is no value is still a type nobody declared.</summary>
    [Fact]
    public void A_path_that_starts_at_no_value_is_still_an_unknown_type() =>
        Assert.Contains("LYR-SEM0011", Refused("let g = nowhere.gm<int>;"));
}
