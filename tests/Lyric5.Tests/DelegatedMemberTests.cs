using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A member a type leaves to a field (design/v5/spec/04 D1; lyric-spec 05 §5) is forwarded "as
/// if <c>fn walk(d) { this.legs.walk(d); }</c> were written" — so a <c>mut fn</c> writes the
/// FIELD. It wrote a copy: a call of a delegated member went through the interface's table,
/// for which a struct receiver is copied into a box, and the forwarder lifted the field into the
/// interface in turn — a second copy, for a field of a struct type. The original never changed,
/// whatever the outer type was. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class DelegatedMemberTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private const string Counters = """
        import std.io { println };

        interface Counter {
            mut fn bump(): void;
            fn get(): int;
            mut fn twice(): void { this.bump(); this.bump(); }
        }

        struct Inner :: [Counter] {
            var n: int,
            mut fn bump(): void { this.n += 1; }
            fn get(): int { return this.n; }
        }

        struct Outer :: [Counter by inner] {
            var inner: Inner,
        }

        class Shared :: [Counter by inner] {
            var inner: Inner,
        }

        fn bumped<T :: [Counter]>(&t: T): void { t.bump(); }

        """;

    /// <summary>On the type, written qualified, through a constraint with a place, from the
    /// interface's default — and on a class that holds the struct.</summary>
    [Fact]
    public void A_delegated_mut_fn_writes_the_field()
    {
        Assert.Equal("2 2 | 1 | 1 | 2 | 1 3\n", Output(Counters + """
            fn main(): void {
                var o = Outer { inner = Inner { n = 0 } };
                o.bump();
                o.bump();
                var p = Outer { inner = Inner { n = 0 } };
                Counter.bump(p);
                var q = Outer { inner = Inner { n = 0 } };
                bumped(&q);
                var r = Outer { inner = Inner { n = 0 } };
                r.twice();
                let s = Shared { inner = Inner { n = 0 } };
                s.bump();
                let first = s.inner.n;
                s.twice();
                println(f"{o.get()} {o.inner.n} | {p.inner.n} | {q.inner.n} | {r.inner.n} | {first} {s.get()}");
            }
            """));
    }

    /// <summary>Through two delegations, and with a field that is exchanged between two calls
    /// (05 §5 rule 3): the forwarder reads the field at each call.</summary>
    [Fact]
    public void A_delegation_of_a_delegation_writes_the_innermost_field()
    {
        Assert.Equal("2 | 1 10\n", Output(Counters + """
            struct Outermost :: [Counter by outer] {
                var outer: Outer,
            }

            fn main(): void {
                var m = Outermost { outer = Outer { inner = Inner { n = 0 } } };
                m.bump();
                m.bump();
                var o = Outer { inner = Inner { n = 0 } };
                o.bump();
                let before = o.get();
                o.inner = Inner { n = 9 };
                o.bump();
                println(f"{m.outer.inner.n} | {before} {o.get()}");
            }
            """));
    }

    /// <summary>What the field's function throws leaves the forwarder as it would leave the
    /// written one (05 §5, 06 §2). The forwarder was built as a function that throws nothing: the
    /// error was dropped and the call answered <c>0</c> — on the type, through a constraint and
    /// through an interface value.</summary>
    [Fact]
    public void A_delegated_member_that_throws_passes_the_error_on()
    {
        Assert.Equal("2 -1 -2 -3 | 4\n", Output("""
            import std.io { println };

            enum Oops :: [Error] {
                Bad;
                fn message(): string { return "oops"; }
            }

            interface Loader {
                fn load(n: int): int throws Oops;
            }

            struct Disk :: [Loader] {
                k: int,
                fn load(n: int): int throws Oops {
                    if (n < 0) {
                        throw Oops.Bad;
                    }
                    return n + this.k;
                }
            }

            struct Cache :: [Loader by disk] {
                disk: Disk,
            }

            fn through<L :: [Loader]>(l: L, n: int): int throws Oops { return try l.load(n); }

            fn main(): void {
                let c = Cache { disk = Disk { k = 1 } };
                let a = try c.load(1) catch (_: Oops) -1;
                let b = try c.load(-1) catch (_: Oops) -1;
                let d = try through(c, -5) catch (_: Oops) -2;
                let asValue: Loader = c;
                let e = try asValue.load(-1) catch (_: Oops) -3;
                let f = try asValue.load(3) catch (_: Oops) -3;
                println(f"{a} {b} {d} {e} | {f}");
            }
            """));
    }

    /// <summary>Control: an interface VALUE holds its own copy of a struct (05 §2 rule 2), and a
    /// <c>mut fn</c> through it writes that copy — delegated or not. And a field that holds an
    /// interface value shares what it holds.</summary>
    [Fact]
    public void An_interface_value_keeps_its_copy_and_a_field_of_one_shares()
    {
        Assert.Equal("0 1 | 2\n", Output(Counters + """
            class Holder :: [Counter by held] {
                var held: Counter,
            }

            fn main(): void {
                var o = Outer { inner = Inner { n = 0 } };
                var asValue: Counter = o;
                asValue.bump();
                let h = Holder { held = Inner { n = 0 } };
                h.bump();
                h.bump();
                println(f"{o.inner.n} {asValue.get()} | {h.get()}");
            }
            """));
    }
}
