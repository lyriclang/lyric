using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A member of a generic block as a VALUE, bound to its receiver as any method is (spec 03 §8.4):
/// a blanket block's, whatever the receiver is, with <c>mut fn</c> or without. The lowering looked
/// for a function of the method's own, and a generic block's member has one per receiver only:
/// <c>LYR-ICE0002</c>, "reference to 'plain' as a value". Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class BlockMethodValueTests
{
    private const string Declarations = """
        import std.fs { File, readText };
        import std.io { IoError, println };
        import std.task { Cancelled };

        interface Reads { fn put(n: int): int; }
        class Shared :: [Reads] { var v: int, fn put(n: int): int { return this.v + n; } }
        struct Copied :: [Reads] { var v: int, fn put(n: int): int { return this.v + n; } }
        struct Crate<T> :: [Reads] { var v: int, item: T, fn put(n: int): int { return this.v + n; } }
        extend int :: [Reads] { fn put(n: int): int { return this + n; } }

        extend<W :: [Reads]> W {
            fn plain(n: int): int { return this.put(n); }
            fn same(): W { return this; }
        }

        interface Counts { mut fn add(n: int): void; fn get(): int; }
        extend Shared :: [Counts] { mut fn add(n: int): void { this.v += n; } fn get(): int { return this.v; } }
        extend Copied :: [Counts] { mut fn add(n: int): void { this.v += n; } fn get(): int { return this.v; } }
        extend<C :: [Counts]> C { mut fn tick(): int { this.add(1); return this.get(); } }

        extend<T> Crate<T> { fn size(): int { return this.v * 2; } fn held(): T { return this.item; } }

        interface Source { type Item; fn one(): Self.Item; }
        struct Ones :: [Source] { type Item = int; fn one(): int { return 1; } }
        extend<S :: [Source<Item = E>], E> S { fn two(): (E, E) { return (this.one(), this.one()); } }

        fn apply(f: fn(int) -> int, n: int): int { return f(n); }

        """;

    /// <summary>The declarations hold no method value: each program goes red for its own.</summary>
    private static string Output(string body, string functions = "")
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", Declarations + functions + $"\nfn main(): void throws [IoError, Cancelled] {{\n{body}\n}}\n"));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Theory]
    // without 'mut': a class, a struct, a built-in, an instance of a generic type
    [InlineData("let c = Shared { v = 3 }; let g = c.plain; println(g(1));", "4\n")]
    [InlineData("let s = Copied { v = 3 }; let g = s.plain; println(g(1));", "4\n")]
    [InlineData("let n = 5; let g = n.plain; println(g(1));", "6\n")]
    [InlineData("let b = Crate<string> { v = 7, item = \"x\" }; let g = b.plain; println(g(1));", "8\n")]
    // the method's type at the receiver, and a fixation's answer in it
    [InlineData("let c = Shared { v = 3 }; let g = c.same; println(g().v);", "3\n")]
    [InlineData("let o = Ones { }; let g = o.two; println(g().0);", "1\n")]
    // handed on
    [InlineData("let c = Shared { v = 3 }; println(apply(c.plain, 1));", "4\n")]
    // a block that is not blanket: its parameter in the method's type
    [InlineData("let b = Crate<string> { v = 7, item = \"x\" }; let g = b.size; let h = b.held; println(f\"{g()} {h()}\");", "14 x\n")]
    public void A_generic_blocks_member_is_a_value(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    [Fact]
    public void The_receiver_may_be_an_interfaces_value() =>
        Assert.Equal("4\n", Output("println(through(Shared { v = 3 }));",
            "fn through(w: Reads): int { let g = w.plain; return g(1); }"));

    /// <summary>One value in the source, one instance of the member per type the parameter takes.</summary>
    [Fact]
    public void The_receiver_may_be_a_type_parameter() =>
        Assert.Equal("4 6\n", Output("println(f\"{through(Shared { v = 3 })} {through(Copied { v = 5 })}\");",
            "fn through<T :: [Reads]>(w: T): int { let g = w.plain; return g(1); }"));

    /// <summary>The object shared, a struct copied at the binding: a call through it writes the
    /// object, and on a struct the copy, which keeps what it wrote.</summary>
    [Theory]
    [InlineData("let c = Shared { v = 0 }; let g = c.tick; g(); println(f\"{g()} {c.v}\");", "2 2\n")]
    [InlineData("var s = Copied { v = 0 }; let g = s.tick; g(); println(f\"{g()} {s.v}\");", "2 0\n")]
    [InlineData("let s = Copied { v = 0 }; let g = s.tick; println(f\"{g()} {s.v}\");", "1 0\n")]
    public void A_mut_fn_of_a_blanket_block_writes_what_the_value_holds(string body, string expected) =>
        Assert.Equal(expected, Output(body));

    /// <summary>The way it was found: std's blanket block over a writer, whose member throws,
    /// through a value handed back from a function whose parameter holds the file.</summary>
    [Fact]
    public void A_writer_of_std_writes_through_its_method_value()
    {
        var at = Path.Combine(TestDirectories.Fresh("method-value"), "out.txt").Replace('\\', '/');
        var body = $"""
            let f = try File.create("{at}");
            let g = writerOf(f);
            try g("written");
            try f.close();
            println(try readText("{at}"));
            """;
        Assert.Equal("written\n", Output(body,
            "fn writerOf(f: File): (fn(StringView) -> void throws [IoError, Cancelled]) { return f.writeString; }"));
    }

    /// <summary>What was no value before the blanket block's members became values stays none.</summary>
    [Fact]
    public void An_inline_member_of_a_blanket_block_stays_no_value()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            interface Reads { fn put(n: int): int; }
            class Shared :: [Reads] { fn put(n: int): int { return n; } }
            extend<W :: [Reads]> W { inline fn set(n: int): int { return this.put(n); } }
            fn main(): int { let c = Shared { }; let g = c.set; return g(0); }
            """));
        var (exit, _, err) = Run("check", "-C", dir);
        Assert.NotEqual(0, exit);
        Assert.Contains("LYR-SEM0183", err);
    }
}
