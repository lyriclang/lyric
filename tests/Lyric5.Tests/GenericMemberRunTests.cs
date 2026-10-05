using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A generic member of an interface at run time (design/v5/spec/04 D9, the review's A8, M8a-7):
/// a conformer's own member is the one that runs — on the type, through a constraint, and from
/// inside ANOTHER default of the interface, which is the case a default instantiated for the
/// interface's value could not see. And a generic STATIC member through a constraint, the form
/// <c>Integer</c>'s <c>exact</c> and <c>clamping</c> take. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class GenericMemberRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_conformers_own_generic_member_is_the_one_that_runs()
    {
        Assert.Equal("a:1 B!1 | a:7 B!7 | a:x a:x | B!x B!x | B!2 B!2\n", Output("""
            import std.io { println };

            interface Shower {
                fn name(): string;
                fn tag<T :: [Display]>(x: T): string { return f"{this.name()}:{x}"; }
                fn twice<T :: [Display]>(x: T): string { return f"{this.tag(x)} {this.tag(x)}"; }
            }

            struct A :: [Shower] {
                fn name(): string { return "a"; }
            }

            struct B :: [Shower] {
                fn name(): string { return "b"; }
                fn tag<T :: [Display]>(x: T): string { return f"B!{x}"; }
            }

            fn through<S :: [Shower]>(s: S): string { return s.tag(7); }
            fn both<S :: [Shower]>(s: S): string { return s.twice("x"); }

            fn main(): void {
                println(f"{A { }.tag(1)} {B { }.tag(1)} | {through(A { })} {through(B { })} | {both(A { })} | {both(B { })} | {B { }.twice(2)}");
            }
            """));
    }

    [Fact]
    public void A_generic_static_member_is_reached_through_a_constraint()
    {
        Assert.Equal("true 100 0 32767 | true 3\n", Output("""
            import std.io { println };

            interface Narrow {
                static fn from<T :: [Integer]>(v: T): ?Self;
            }

            struct Small :: [Narrow] {
                n: int,
                static fn from<T :: [Integer]>(v: T): ?Small {
                    let n = int.clamping(v);
                    if (n < 10) {
                        return Small { n = n };
                    }
                    return null;
                }
            }

            fn make<N :: [Narrow]>(v: int): ?N { return N.from(v); }
            fn narrow<U :: [Integer]>(v: int): ?U { return U.exact(v); }
            fn hold<U :: [Integer]>(v: int): U { return U.clamping(v); }

            fn main(): void {
                let small = make<Small>(3);
                let big = make<Small>(30);
                println(f"{narrow<int8>(300) == null} {narrow<int8>(100) ?? 0} {hold<uint8>(-5)} {hold<int16>(70000)} | {big == null} {(small ?? Small { n = 0 }).n}");
            }
            """));
    }

    [Fact]
    public void An_interface_with_a_generic_default_is_no_type_of_a_value()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            interface Shower {
                fn name(): string;
                fn tag<T>(x: T): string { return this.name(); }
            }

            struct A :: [Shower] {
                fn name(): string { return "a"; }
            }

            fn main(): void {
                let s: Shower = A { };
            }
            """));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-SEM0126]: 'Shower' is usable as a constraint only", error);
        Assert.Contains("its member 'tag' is generic", error);
    }

    /// <summary>The default's instance is one per conformer whatever the conformer is: a builtin,
    /// an instance of a generic type — and a builtin's own, written in its block, takes the
    /// default's place as a struct's does.</summary>
    [Fact]
    public void A_generic_default_is_instantiated_for_a_builtin_and_for_an_instance()
    {
        Assert.Equal("int:1 crate:2 | int:7 crate:7 | I!1 I!x | P!3 P!3 P!x P!x | I!4 I!4\n", Output("""
            import std.io { println };

            interface Shower {
                fn name(): string;
                fn tag<T :: [Display]>(x: T): string { return f"{this.name()}:{x}"; }
                fn twice<T :: [Display]>(x: T): string { return f"{this.tag(x)} {this.tag(x)}"; }
            }

            extend int :: [Shower] {
                fn name(): string { return "int"; }
            }

            extend int8 :: [Shower] {
                fn name(): string { return "int8"; }
                fn tag<T :: [Display]>(x: T): string { return f"I!{x}"; }
            }

            struct Crate<T> :: [Shower] {
                v: T,
                fn name(): string { return "crate"; }
            }

            struct Pack<T> :: [Shower] {
                v: T,
                fn name(): string { return "pack"; }
                fn tag<U :: [Display]>(x: U): string { return f"P!{x}"; }
            }

            fn through<S :: [Shower]>(s: S): string { return s.tag(7); }
            fn small<S :: [Shower]>(s: S): string { return s.tag("x"); }
            fn both<S :: [Shower]>(s: S): string { return s.twice("x"); }

            fn main(): void {
                let three = 3;
                let crate = Crate<bool> { v = true };
                let other = Crate<int> { v = 1 };
                let pack = Pack<bool> { v = true };
                let n: int8 = 5;
                println(f"{three.tag(1)} {crate.tag(2)} | {through(4)} {through(other)} | {n.tag(1)} {small(n)} | {pack.twice(3)} {both(pack)} | {n.twice(4)}");
            }
            """));
    }

    /// <summary>What a generic type writes in its body is reached through a constraint as what a
    /// plain type writes is: its generic member, and its static — both were gaps
    /// (<c>LYR-IR0001</c>).</summary>
    [Fact]
    public void A_generic_types_own_members_are_reached_through_a_constraint()
    {
        Assert.Equal("crate 3 | crate 5 | 7\n", Output("""
            import std.io { println };

            interface Tagger {
                fn tag<U :: [Display]>(u: U): string;
            }

            interface Made {
                static fn make(): Self;
                fn n(): int;
            }

            struct Crate<T> :: [Tagger, Made] {
                v: int,
                fn tag<U :: [Display]>(u: U): string { return f"crate {u}"; }
                static fn make(): Crate<T> { return Crate<T> { v = 7 }; }
                fn n(): int { return this.v; }
            }

            fn through<G :: [Tagger]>(g: G): string { return g.tag(5); }
            fn fresh<M :: [Made]>(): M { return M.make(); }

            fn main(): void {
                let c: Crate<string> = fresh();
                println(f"{c.tag(3)} | {through(c)} | {c.n()}");
            }
            """));
    }

    /// <summary>A generic member a type leaves to a field (05 §5) would need a forwarder per
    /// instantiation, which is not built: refused where the type delegates. The compiler crashed
    /// at the call ("function has no body").</summary>
    [Fact]
    public void A_generic_member_is_not_forwarded_to_a_field_yet()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };

            interface Tagger {
                fn tag<U>(u: U): string;
            }

            struct Inner :: [Tagger] {
                n: int,
                fn tag<U>(u: U): string { return "inner"; }
            }

            struct Outer :: [Tagger by inner] {
                inner: Inner,
            }

            fn main(): void {
                let o = Outer { inner = Inner { n = 0 } };
                println(o.tag(3));
            }
            """));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("src/main.lyr:12:18: error[LYR-SEM0020]: 'Outer' does not implement the generic member 'tag' of interface 'Tagger'", error);
        Assert.DoesNotContain("LYR-ICE0001", error);
        Assert.DoesNotContain("LYR-IR0001", error);
    }
}
