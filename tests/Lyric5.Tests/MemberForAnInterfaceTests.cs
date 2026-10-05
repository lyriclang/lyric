using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The member a type has FOR an interface (design/v5/spec/04 D2, D3; lyric-spec 05 §3, §4): its
/// own where it wrote one, in its body or in the block that declares the conformance, else the
/// interface's default. A name alone does not say it — two blocks of one type may each give a
/// <c>greet</c>, one for <c>Greeter</c> and one for <c>Waver</c> — and the interface's table was
/// the one place that asked by the interface. The qualified call asked for the default first; a
/// call through a constraint took the first block that had the name; a static through a
/// constraint took what the checker had recorded, the first candidate that fit, for both
/// conformances. Each a call into the wrong function, without a word. Through <c>Main</c>, so in
/// the console collection.
/// </summary>
[Collection("console")]
public class MemberForAnInterfaceTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>Written <c>Iface.m(x)</c>, the member is the one <c>x</c>'s type has for the
    /// interface (05 §4) — what a call through a constraint reaches.</summary>
    [Fact]
    public void The_qualified_call_reaches_the_types_own_member()
    {
        Assert.Equal("own default block | own default block | own default\n", Output("""
            import std.io { println };

            interface Walker {
                fn walk(): string { return "default"; }
            }

            struct D :: [Walker] {
                n: int,
                fn walk(): string { return "own"; }
            }

            struct E :: [Walker] {
                n: int,
            }

            struct F {
                n: int,
            }

            extend F :: [Walker] {
                fn walk(): string { return "block"; }
            }

            struct Crate<T> :: [Walker] {
                v: T,
                fn walk(): string { return "own"; }
            }

            struct Bare<T> :: [Walker] {
                v: T,
            }

            fn through<W :: [Walker]>(w: W): string { return w.walk(); }

            fn main(): void {
                let d = D { n = 0 };
                let e = E { n = 0 };
                let f = F { n = 0 };
                let crate = Crate<int> { v = 1 };
                let bare = Bare<int> { v = 1 };
                println(f"{Walker.walk(d)} {Walker.walk(e)} {Walker.walk(f)} | {through(d)} {through(e)} {through(f)} | {Walker.walk(crate)} {Walker.walk(bare)}");
            }
            """));
    }

    /// <summary>A <c>mut fn</c> written qualified writes its receiver (05 §4). Through the
    /// interface's table the struct was copied into a box first, and the copy was written:
    /// <c>0 0 | 0 | 2</c>.</summary>
    [Fact]
    public void A_mut_fn_written_qualified_writes_its_receiver()
    {
        Assert.Equal("2 2 | 10 | 2\n", Output("""
            import std.io { println };

            interface Counter {
                mut fn bump(): void;
                fn get(): int;
                mut fn twice(): void { this.bump(); this.bump(); }
            }

            struct C :: [Counter] {
                var n: int,
                mut fn bump(): void { this.n += 1; }
                fn get(): int { return this.n; }
            }

            struct B {
                var n: int,
            }

            extend B :: [Counter] {
                mut fn bump(): void { this.n += 10; }
                fn get(): int { return this.n; }
            }

            fn main(): void {
                var c = C { n = 0 };
                Counter.bump(c);
                Counter.bump(c);
                var b = B { n = 0 };
                Counter.bump(b);
                var d = C { n = 0 };
                Counter.twice(d);
                println(f"{c.n} {Counter.get(c)} | {b.n} | {d.n}");
            }
            """));
    }

    private const string TwoBlocks = """
        import std.io { println };

        interface Greeter {
            fn greet(): string;
            fn loud(): string { return f"{this.greet()}!"; }
        }

        interface Waver {
            fn greet(): string;
            fn soft(): string { return f"{this.greet()}?"; }
        }

        struct Person {
            name: string,
        }

        extend Person :: [Greeter] {
            fn greet(): string { return "hello"; }
        }

        extend Person :: [Waver] {
            fn greet(): string { return "hi"; }
        }

        fn g<T :: [Greeter]>(t: T): string { return t.greet(); }
        fn w<T :: [Waver]>(t: T): string { return t.greet(); }

        """;

    /// <summary>One name, two blocks, one per interface (04 D3): each is reached through its
    /// interface — written qualified, through a constraint, through a value, and from the
    /// interface's own default calling it on <c>this</c>.</summary>
    [Fact]
    public void Two_blocks_of_one_name_are_reached_each_through_its_interface()
    {
        Assert.Equal("hello hi | hello hi | hello hi | hello! hi?\n", Output(TwoBlocks + """
            fn main(): void {
                let p = Person { name = "x" };
                let asGreeter: Greeter = p;
                let asWaver: Waver = p;
                println(f"{Greeter.greet(p)} {Waver.greet(p)} | {g(p)} {w(p)} | {asGreeter.greet()} {asWaver.greet()} | {p.loud()} {p.soft()}");
            }
            """));
    }

    [Fact]
    public void Two_blocks_statics_are_reached_each_through_its_interface()
    {
        Assert.Equal("named titled\n", Output("""
            import std.io { println };

            interface Named {
                static fn label(): string;
            }

            interface Titled {
                static fn label(): string;
            }

            struct Person {
                name: string,
            }

            extend Person :: [Named] {
                static fn label(): string { return "named"; }
            }

            extend Person :: [Titled] {
                static fn label(): string { return "titled"; }
            }

            fn n<T :: [Named]>(): string { return T.label(); }
            fn t<T :: [Titled]>(): string { return T.label(); }

            fn main(): void {
                println(f"{n<Person>()} {t<Person>()}");
            }
            """));
    }

    /// <summary>The same on a builtin and on a generic type, whose blocks' members are instances
    /// of their own — the qualified call there asked for an instance of the interface's member,
    /// which has no body.</summary>
    [Fact]
    public void Two_blocks_of_a_builtin_and_of_a_generic_type_likewise()
    {
        Assert.Equal("hello hi | hello hi | hello hi\n", Output("""
            import std.io { println };

            interface Greeter {
                fn greet(): string;
            }

            interface Waver {
                fn greet(): string;
            }

            extend int :: [Greeter] {
                fn greet(): string { return "hello"; }
            }

            extend int :: [Waver] {
                fn greet(): string { return "hi"; }
            }

            struct Crate<T> {
                v: T,
            }

            extend<T> Crate<T> :: [Greeter] {
                fn greet(): string { return "hello"; }
            }

            extend<T> Crate<T> :: [Waver] {
                fn greet(): string { return "hi"; }
            }

            fn g<T :: [Greeter]>(t: T): string { return t.greet(); }
            fn w<T :: [Waver]>(t: T): string { return t.greet(); }

            fn main(): void {
                let c = Crate<int> { v = 1 };
                println(f"{g(3)} {w(3)} | {g(c)} {w(c)} | {Greeter.greet(c)} {Waver.greet(c)}");
            }
            """));
    }

    /// <summary>And for a generic member, which no table holds: the block of the interface the
    /// constraint names.</summary>
    [Fact]
    public void Two_blocks_generic_members_are_reached_each_through_its_interface()
    {
        Assert.Equal("hello 1 | hi 1\n", Output("""
            import std.io { println };

            interface Greeter {
                fn greet<T :: [Display]>(x: T): string;
            }

            interface Waver {
                fn greet<T :: [Display]>(x: T): string;
            }

            struct Person {
                name: string,
            }

            extend Person :: [Greeter] {
                fn greet<T :: [Display]>(x: T): string { return f"hello {x}"; }
            }

            extend Person :: [Waver] {
                fn greet<T :: [Display]>(x: T): string { return f"hi {x}"; }
            }

            fn g<T :: [Greeter]>(t: T): string { return t.greet(1); }
            fn w<T :: [Waver]>(t: T): string { return t.greet(1); }

            fn main(): void {
                let p = Person { name = "x" };
                println(f"{g(p)} | {w(p)}");
            }
            """));
    }
}
