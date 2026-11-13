using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Members of one name, called and named (spec 08 §1.2 rules 1 and 3): a static beside a method,
/// whichever is declared first; a block on a shape, <c>extend&lt;T&gt; T[]</c>; a call through
/// <c>?.</c>. And <c>this.gm&lt;int&gt;</c>, which reads as a path as <c>c.gm&lt;int&gt;</c> does
/// (03 §9.4 rule 3). Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class MemberOverloadTests
{
    private const string Declarations = """
        import std.io { IoError, println };
        import std.task { Cancelled };

        struct W1 { fn m(a: int): int { return a; } static fn m(a: int, b: int): int { return a + b; } }
        struct W2 { static fn m(a: int, b: int): int { return a + b; } fn m(a: int): int { return a; } }
        struct W3 { fn o(a: int): int { return a; } fn o(a: int, b: int): int { return a + b; } }
        extend<T> T[] { fn ao(a: int): int { return a; } fn ao(a: int, b: int): int { return a + b; } }
        extend int[] { fn io(a: int): int { return a; } fn io(a: int, b: int): int { return a * b; } }
        extend<T> T[] { fn firstOr(): T { return this[0]; } fn firstOr(d: T): T { return if (this.length() > 0) this[0] else d; } }
        extend<T> (T, T) { fn to(a: int): int { return a; } fn to(a: int, b: int): int { return a - b; } }
        class C { fn gm<U>(u: U): U { return u; } fn plain(): int { return 1; } }

        """;

    private static string Dir(string body, string functions = "") => Package(("lyric.toml", AppManifest),
        ("src/main.lyr", Declarations + functions + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));

    private static string Output(string body)
    {
        var dir = Dir(body);
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string body, string functions = "")
    {
        var (exit, _, err) = Run("check", "-C", Dir(body, functions));
        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("LYR-ICE", err);
        return err;
    }

    /// <summary>A value reaches the methods of the name, a type its statics; the count chooses
    /// among them, whichever was declared first.</summary>
    [Theory]
    [InlineData("let w = W1 { }; println(w.m(1)); println(W1.m(1, 2));", "1\n3\n")]
    [InlineData("let w = W2 { }; println(w.m(1)); println(W2.m(1, 2));", "1\n3\n")]
    [InlineData("let w = W1 { }; let h = w.m; println(h(5)); let s = W1.m; println(s(1, 2));", "5\n3\n")]
    [InlineData("let w = W2 { }; let h = w.m; println(h(5)); let s = W2.m; println(s(1, 2));", "5\n3\n")]
    [InlineData("let w: ?W1 = W1 { }; println(w?.m(4) ?? 0);", "4\n")]
    public void A_static_and_a_method_of_one_name_are_each_reached_their_way(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    /// <summary>The count may choose the one the way in does not reach.</summary>
    [Theory]
    [InlineData("let w = W1 { }; println(w.m(1, 2));", "'m' with 2 argument(s) is static — call it on the type: 'W1.m(…)'")]
    [InlineData("println(W1.m(1));", "'m' with 1 argument(s) is an instance method and needs a receiver")]
    [InlineData("println(W2.m(1));", "'m' with 1 argument(s) is an instance method and needs a receiver")]
    public void The_count_may_choose_one_the_way_in_does_not_reach(string body, string message)
    {
        var err = Refused(body);
        Assert.Contains("LYR-SEM0055", err);
        Assert.Contains(message, err);
    }

    /// <summary>Through a value only its methods: a function type that only the static has is
    /// none of them.</summary>
    [Fact]
    public void Through_a_value_a_static_is_no_value() =>
        Assert.Contains("LYR-SEM0001", Refused("let w = W1 { }; let h: fn(int, int) -> int = w.m;"));

    /// <summary>A call through <c>?.</c> of one of several stays optional, as of one.</summary>
    [Fact]
    public void A_chosen_one_through_a_chain_stays_optional() =>
        Assert.Equal("4\n9\n7\n", Output(
            "let w: ?W3 = W3 { }; println(w?.o(4) ?? 0); println(w?.o(4, 5) ?? 0); let z: ?W3 = null; println(z?.o(1, 2) ?? 7);"));

    /// <summary>A block on a shape holds a set too, generic or not, called and named.</summary>
    [Theory]
    [InlineData("let xs = [1]; println(xs.ao(1)); println(xs.ao(1, 2));", "1\n3\n")]
    [InlineData("let xs = [3]; println(xs.io(4)); println(xs.io(4, 5));", "4\n20\n")]
    [InlineData("let xs = [\"a\"]; println(xs.firstOr()); let e: string[] = []; println(e.firstOr(\"z\"));", "a\nz\n")]
    [InlineData("let p = (1, 2); println(p.to(1)); println(p.to(5, 2));", "1\n3\n")]
    [InlineData("let xs: ?int[] = [1]; println(xs?.ao(1, 2) ?? 0);", "3\n")]
    [InlineData("let xs = [1]; let h: fn(int, int) -> int = xs.ao; println(h(1, 2)); let k: fn(int) -> int = xs.ao; println(k(4));", "3\n4\n")]
    [InlineData("let e: string[] = []; let f: fn(string) -> string = e.firstOr; println(f(\"y\"));", "y\n")]
    public void A_shapes_block_holds_a_set(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    [Theory]
    [InlineData("let xs = [1]; println(xs.ao(1, 2, 3));", "LYR-SEM0087")]
    [InlineData("let xs = [1]; let h = xs.ao;", "LYR-SEM0089")]
    public void A_shapes_set_refuses_as_any_set(string body, string code) =>
        Assert.Contains(code, Refused(body));

    /// <summary><c>this.gm&lt;int&gt;</c> with nothing to call is the member with type arguments,
    /// not a comparison; with a call it stays a call.</summary>
    [Theory]
    [InlineData("class D { fn gm<U>(u: U): U { return u; } fn use(): void { let g = this.gm<int>; } }", "LYR-SEM0052", "generic method 'gm' is not a value, also with its type arguments written")]
    [InlineData("class D { fn plain(): int { return 1; } fn use(): void { let g = this.plain<int>; } }", "LYR-SEM0026", "'plain' is not generic and takes no type arguments")]
    [InlineData("class D { c: C, fn use(): void { let g = this.c.gm<int>; } }", "LYR-SEM0052", "generic method 'gm' is not a value, also with its type arguments written")]
    public void Type_arguments_on_this_member_are_no_comparison(string functions, string code, string message)
    {
        var err = Refused("println(1);", functions);
        Assert.Contains(code, err);
        Assert.Contains(message, err);
        Assert.DoesNotContain("LYR-PAR", err);
    }

    [Fact]
    public void A_call_with_type_arguments_on_this_still_calls()
    {
        var dir = Dir("println(D { }.use());",
            "class D { fn gm<U>(u: U): U { return u; } fn use(): int { return this.gm<int>(3); } }");
        Assert.Equal("3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
