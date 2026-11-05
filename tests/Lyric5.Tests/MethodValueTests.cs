using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>obj.method</c> as a value (spec 03 §8.4 rule 2), on the receivers that had no function of the
/// method's own to point at: an interface's value, a type parameter, an instance of a generic type,
/// and <c>?.</c> on all of them. The value is a call waiting for its arguments, so what runs is
/// what a call runs. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class MethodValueTests
{
    private const string Declarations = """
        import std.io { IoError, println };
        import std.task { Cancelled };

        interface Reads { fn put(n: int): int; fn twice(): int { return this.put(40); } mut fn add(n: int): void; }
        class Shared :: [Reads] { var v: int, fn put(n: int): int { return this.v + n; } mut fn add(n: int): void { this.v += n; } }
        struct Copied :: [Reads] { var v: int, fn put(n: int): int { return this.v + n; } mut fn add(n: int): void { this.v += n; } }
        extend int :: [Reads] { fn put(n: int): int { return this + n; } mut fn add(n: int): void { } }
        class Held<T> :: [Reads] { var v: int, item: T, fn put(n: int): int { return this.v + n; } mut fn add(n: int): void { this.v += n; } }
        struct Crate<T> { var v: int, item: T, fn plus(n: int): int { return this.v + n; } fn held(): T { return this.item; }
            fn given(t: T): T { return t; } mut fn bump(): int { this.v += 1; return this.v; } }

        interface Maps<T> { fn get(t: T): T; fn both(t: T): (T, T) { return (this.get(t), this.get(t)); } }
        struct Next :: [Maps<int>] { fn get(t: int): int { return t + 1; } }
        interface Source { type Item; fn one(): Self.Item; }
        struct Ones :: [Source] { type Item = int; fn one(): int { return 1; } }
        interface Counts { fn count(xs: int...): int; }
        struct Counter :: [Counts] { fn count(xs: int...): int { return xs.length(); } }
        struct Rest<T> { fn count(xs: T...): int { return xs.length(); } }
        interface Steps { fn step(&n: int): void; }
        struct Up :: [Steps] { fn step(&n: int): void { n += 1; } }
        struct Boom :: [Error] { fn message(): string { return "boom"; } }
        interface Fails { fn check(n: int): int throws [Boom]; }
        struct Failing :: [Fails] { fn check(n: int): int throws [Boom] { if (n > 0) { throw Boom { }; } return n; } }
        extend<W :: [Reads]> W { fn plain(n: int): int { return this.put(n); } }

        """;

    private static string Output(string body, string functions = "")
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", Declarations + functions + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>Through the slot: the conformer behind the value answers.</summary>
    [Theory]
    [InlineData("fn f(w: Reads): int { let g = w.put; return g(1); }", "f(Shared { v = 3 })", "4\n")]
    [InlineData("fn f(w: Reads): int { let g = w.put; return g(1); }", "f(Copied { v = 3 })", "4\n")]
    // a default, and one written in terms of the abstract member
    [InlineData("fn f(w: Reads): int { let g = w.twice; return g(); }", "f(Shared { v = 3 })", "43\n")]
    // a generic interface's own and its default at the instance
    [InlineData("fn f(w: Maps<int>): int { let g = w.get; return g(1); }", "f(Next { })", "2\n")]
    [InlineData("fn f(w: Maps<int>): int { let g = w.both; return g(1).1; }", "f(Next { })", "2\n")]
    // the rest is one array in the value's type
    [InlineData("fn f(w: Counts): int { let g = w.count; return g([1, 2]); }", "f(Counter { })", "2\n")]
    public void An_interfaces_own_method_is_a_value_on_its_value(string function, string call, string expected) =>
        Assert.Equal(expected, Output($"println({call});", function));

    /// <summary>One value in the source, what a call would reach per type the parameter takes.</summary>
    [Theory]
    [InlineData("fn f<T :: [Reads]>(w: T): int { let g = w.put; return g(1); }", "f(Shared { v = 3 })", "4\n")]
    [InlineData("fn f<T :: [Reads]>(w: T): int { let g = w.put; return g(1); }", "f(Copied { v = 3 })", "4\n")]
    [InlineData("fn f<T :: [Reads]>(w: T): int { let g = w.put; return g(1); }", "f(5)", "6\n")]
    [InlineData("fn f<T :: [Reads]>(w: T): int { let g = w.put; return g(1); }", "f(Held<string> { v = 3, item = \"x\" })", "4\n")]
    // a default on a type parameter: it was malformed IR before it was an error
    [InlineData("fn f<T :: [Reads]>(w: T): int { let g = w.twice; return g(); }", "f(Shared { v = 3 })", "43\n")]
    [InlineData("fn f<T :: [Maps<int>]>(w: T): int { let g = w.get; return g(1); }", "f(Next { })", "2\n")]
    [InlineData("fn f<T :: [Source<Item = int>]>(w: T): int { let g = w.one; return g(); }", "f(Ones { })", "1\n")]
    public void An_interfaces_own_method_is_a_value_on_a_type_parameter(string function, string call, string expected) =>
        Assert.Equal(expected, Output($"println({call});", function));

    [Fact]
    public void A_default_names_its_own_abstract_member_as_a_value() =>
        Assert.Equal("4\n", Output("println(Three { }.bound()(1));", """
            interface Gives { fn put(n: int): int; fn bound(): (fn(int) -> int) { return this.put; } }
            struct Three :: [Gives] { fn put(n: int): int { return n + 3; } }
            """));

    /// <summary>The object shared, a struct copied at the binding (03 §8.4 rule 2), through an
    /// interface and a type parameter as on a type.</summary>
    [Theory]
    [InlineData("fn f(w: Reads): void throws [IoError, Cancelled] { let g = w.add; g(1); println(w.put(0)); }",
        "let c = Shared { v = 2 }; try f(c); println(c.v);", "3\n3\n")]
    [InlineData("fn f<T :: [Reads]>(w: T): void throws [IoError, Cancelled] { let g = w.add; g(2); println(w.put(0)); }",
        "try f(Copied { v = 3 });", "3\n")]
    public void A_mut_fn_through_the_value_writes_what_it_holds(string function, string body, string expected) =>
        Assert.Equal(expected, Output(body, function));

    [Theory]
    [InlineData("fn f(w: Fails): int throws [Boom] { let g = w.check; return try g(0); }", "0\n")]
    [InlineData("fn f(w: Fails): int throws [Boom] { let g = w.check; return try g(1); }", "9\n")]
    [InlineData("fn f<T :: [Fails]>(w: T): int throws [Boom] { let g = w.check; return try g(1); }", "9\n")]
    public void What_the_method_throws_comes_out_of_the_value(string function, string expected) =>
        Assert.Equal(expected, Output("try { println(f(Failing { })); } catch (e: Boom) { println(9); }", function));

    [Fact]
    public void A_place_parameter_stays_a_place() =>
        Assert.Equal("5\n", Output("let g = f(Up { }); var k = 4; g(&k); println(k);",
            "fn f(w: Steps): (fn(&int) -> void) { return w.step; }"));

    /// <summary>A generic type's own method: the receiver's instance of it, the method's type read
    /// at the receiver's arguments.</summary>
    [Theory]
    [InlineData("let b = Crate<string> { v = 7, item = \"x\" }; let g = b.plus; println(g(1));", "8\n")]
    [InlineData("let b = Crate<string> { v = 7, item = \"x\" }; let g = b.held; let h = b.given; println(f\"{g()} {h(\"y\")}\");", "x y\n")]
    [InlineData("let v = Rest<int> { }; let g = v.count; println(g([1, 2]));", "2\n")]
    // a struct copied, a class shared
    [InlineData("let b = Crate<string> { v = 7, item = \"x\" }; let g = b.bump; g(); println(f\"{g()} {b.v}\");", "9 7\n")]
    [InlineData("let k = Held<string> { v = 7, item = \"x\" }; let g = k.add; g(2); println(f\"{k.v} {k.put(0)}\");", "9 9\n")]
    public void A_generic_types_own_method_is_a_value_on_an_instance(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    [Theory]
    // an instance only an instance of the enclosing function names
    [InlineData("fn f<A>(a: A): A { let b = Crate<A> { v = 0, item = a }; let g = b.held; return g(); }", "println(f(\"x\"));", "x\n")]
    // from inside the type's block: the type parameter stands for the block's own
    [InlineData("extend<T> Crate<T> { fn added(): (fn(int) -> int) { return this.plus; } }",
        "let b = Crate<string> { v = 7, item = \"x\" }; println(b.added()(1));", "8\n")]
    public void An_instance_named_in_a_generic_body_binds_at_each_instance(string function, string body, string expected) =>
        Assert.Equal(expected, Output(body, function));

    /// <summary><c>a?.m</c> reads the member of the value there is (03 §3.3 rule 4): a method's is
    /// its closure, the whole a <c>?fn</c>.</summary>
    [Theory]
    [InlineData("let o: ?Shared = Shared { v = 3 }; let g = o?.put; println((g!)(1));", "4\n")]
    [InlineData("let o: ?Copied = Copied { v = 3 }; let g = o?.put; println((g!)(1));", "4\n")]
    [InlineData("let o: ?Crate<string> = Crate<string> { v = 7, item = \"x\" }; let g = o?.plus; println((g!)(1));", "8\n")]
    [InlineData("let o: ?Reads = Shared { v = 3 }; let g = o?.put; println((g!)(1));", "4\n")]
    // a blanket block's member, a function's own
    [InlineData("let o: ?Shared = Shared { v = 3 }; let g = o?.plain; println((g!)(1));", "4\n")]
    [InlineData("let o: ?Shared = null; let g = o?.put; if (g == null) { println(\"none\"); } else { println(\"some\"); }", "none\n")]
    public void A_method_through_a_chain_is_an_optional_value(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    [Fact]
    public void A_method_through_a_chain_on_a_type_parameter() =>
        Assert.Equal("4\n", Output("println((f(Shared { v = 3 })!)(1));",
            "fn f<T :: [Reads]>(o: ?T): ?(fn(int) -> int) { return o?.put; }"));

    /// <summary>The receiver is evaluated once, when the value is made; each value a fresh copy,
    /// also in a loop.</summary>
    [Theory]
    [InlineData("var made = 0; fn mk(): Shared { made += 1; return Shared { v = 3 }; }",
        "let g = mk().put; let h = g(1); g(1); println(f\"{made} {h}\");", "1 4\n")]
    [InlineData("struct Ticks { var v: int, mut fn tick(): int { this.v += 1; return this.v; } }",
        "let s = Ticks { v = 0 }; var fs: (fn() -> int)[] = []; for (_ in 0..2) { fs = fs + [s.tick]; } fs[0](); println(f\"{fs[0]()} {fs[1]()}\");", "2 1\n")]
    public void The_receiver_is_read_once_when_the_value_is_made(string function, string body, string expected) =>
        Assert.Equal(expected, Output(body, function));

    [Fact]
    public void A_lambda_holds_a_method_value_of_what_it_captures() =>
        Assert.Equal("4\n", Output("println(f(Shared { v = 3 }));",
            "fn f(w: Reads): int { let mk = () => { let g = w.put; return g(1); }; return mk(); }"));

    /// <summary>A function value is monomorphic (03 §9.4 rule 3): where a call would bind a
    /// parameter nothing binds it. Refused at the value, also where the position names a type.</summary>
    [Theory]
    [InlineData("struct G { fn gm<U>(u: U): U { return u; } }", "let c = G { }; let g = c.gm;", "generic method 'gm' is not a value")]
    [InlineData("struct G { fn gm<U>(u: U): U { return u; } }", "let c = G { }; let g: fn(int) -> int = c.gm;", "generic method 'gm' is not a value")]
    [InlineData("struct G { static fn mk<U>(u: U): U { return u; } }", "let g = G.mk;", "generic method 'mk' is not a value")]
    [InlineData("interface H { fn hm<U>(u: U): U { return u; } } fn f<T :: [H]>(h: T): void { let g = h.hm; }", "", "generic method 'hm' is not a value")]
    [InlineData("struct G { fn gm<U>(u: U): U { return u; } }", "let o: ?G = G { }; let g = o?.gm;", "generic method 'gm' is not a value")]
    // a parameter of the block only a call binds
    [InlineData("struct M { } struct Cm { } extend Cm :: [From<M>] { static fn from(m: M): Cm { return Cm { }; } }",
        "let m = M { }; let g = m.into;", "'into' is not a value")]
    [InlineData("struct M { } struct Cm { } extend Cm :: [From<M>] { static fn from(m: M): Cm { return Cm { }; } }",
        "let m = M { }; let g: fn() -> Cm = m.into;", "'into' is not a value")]
    public void A_method_a_call_would_bind_is_no_value(string declarations, string body, string message)
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", $"{declarations}\nfn main(): void {{\n{body}\n}}\n"));
        var (exit, _, err) = Run("check", "-C", dir);
        Assert.NotEqual(0, exit);
        Assert.Contains("LYR-SEM0052", err);
        Assert.Contains(message, err);
        Assert.DoesNotContain("LYR-ICE", err);
    }

    /// <summary>The constraint's instance binds what the block leaves to a call (05 §13 rule 9): so
    /// the same member is a value through a constraint.</summary>
    [Fact]
    public void A_constraint_binds_what_only_a_call_binds_on_a_type() =>
        Assert.Equal("7\n", Output("println(f(M { }).v);", """
            struct M { }
            struct Cm { v: int }
            extend Cm :: [From<M>] { static fn from(m: M): Cm { return Cm { v = 7 }; } }
            fn f<T :: [Into<Cm>]>(w: T): Cm { let g = w.into; return g(); }
            """));
}
