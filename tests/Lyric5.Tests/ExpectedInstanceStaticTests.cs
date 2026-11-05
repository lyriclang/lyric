using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A bare generic enum whose instance only the position names: <c>Result.of(f)</c> at an expected
/// <c>Result&lt;int, X&gt;</c>. The member was typed at that instance, the receiver stayed without
/// it, and the lowering, which reads the instance off the receiver, reached the static's declared
/// parameters: an internal error at every static of a generic enum, of its body or of a block,
/// never at a struct's. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ExpectedInstanceStaticTests
{
    private const string Prelude = """
        import std.io { println };

        struct X :: [Error] {
            fn message(): string { return "x"; }
        }

        fn g(): int throws X { return 1; }

        fn show(r: Result<int, X>): void {
            println(match (r) { .Ok(v) => f"ok {v}", .Err(e) => f"err {e.message()}" });
        }

        enum O<T> {
            S(T), N;

            static fn body(x: T): O<T> { return O<T>.S(x); }
            static fn bodyNone(): O<T> { return O<T>.N; }
        }

        extend<T> O<T> {
            static let zero: O<T> = O<T>.N;

            static fn blk(x: T): O<T> { return O<T>.S(x); }
            static fn blkNone(): O<T> { return O<T>.N; }
            static fn blkOwn<U>(x: T, _u: U): O<T> { return O<T>.S(x); }
            static inline fn blkInline(x: T): O<T> { return O<T>.S(x); }
        }

        fn o(v: O<int>): void {
            println(match (v) { .S(x) => f"S {x}", .N => "N" });
        }


        """;

    private static string Project(string program) =>
        Package(("lyric.toml", AppManifest), ("src/main.lyr", Prelude + program + "\n"));

    private static string Output(string program)
    {
        var dir = Project(program);
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>The three positions that expect a type, then the two forms that name the
    /// instance otherwise, as controls.</summary>
    [Theory]
    [InlineData("""
        fn h(): Result<int, X> { return Result.of(() => try g()); }
        fn main(): void { show(h()); }
        """)]
    [InlineData("""
        fn main(): void {
            let r: Result<int, X> = Result.of(() => try g());
            show(r);
        }
        """)]
    [InlineData("""
        fn main(): void { show(Result.of(() => try g())); }
        """)]
    [InlineData("""
        fn main(): void {
            let r = Result.of(() => try g());
            show(r);
        }
        """)]
    [InlineData("""
        fn main(): void { show(Result<int, X>.of(() => try g())); }
        """)]
    public void Result_of_is_built_and_checked_however_its_instance_is_named(string program)
    {
        var (exit, _, error) = Run("check", "-C", Project(program));
        Assert.True(exit == 0, error);
        Assert.Equal("ok 1\n", Output(program));
    }

    [Fact]
    public void A_static_of_a_block_on_an_enum_takes_the_instance_the_position_asks_for()
    {
        Assert.Equal("S 1\nN\nS 2\nS 3\nsome\nS 5\n", Output("""
            fn made<T>(x: T): O<T> { return O.blk(x); }

            fn main(): void {
                let a: O<int> = O.blk(1);
                o(a);
                let b: O<int> = O.blkNone();
                o(b);
                let c: O<int> = O.blkOwn(2, "u");
                o(c);
                let d: O<int> = O.blkInline(3);
                o(d);
                let e: ?O<int> = O.blk(4);
                if (e != null) { println("some"); }
                o(made(5));
            }
            """));
    }

    [Fact]
    public void A_static_of_an_enums_body_takes_the_instance_the_position_asks_for()
    {
        Assert.Equal("S 1\nN\nwrapped\n", Output("""
            fn main(): void {
                let a: O<int> = O.body(1);
                o(a);
                let b: O<int> = O.bodyNone();
                o(b);
                let c: O<?int> = O.body(2);
                println(match (c) { .S(_) => "wrapped", .N => "none" });
            }
            """));
    }

    [Fact]
    public void An_implicit_member_names_a_static_of_the_instance()
    {
        Assert.Equal("ok 1\nS 1\nS 2\nN\n", Output("""
            fn main(): void {
                let r: Result<int, X> = .of(() => try g());
                show(r);
                let a: O<int> = .blk(1);
                o(a);
                let b: O<int> = .body(2);
                o(b);
                let c: O<int> = .bodyNone();
                o(c);
            }
            """));
    }

    [Fact]
    public void A_constant_of_a_block_is_the_instances_the_position_asks_for()
    {
        Assert.Equal("N\nN\nN\n", Output("""
            fn main(): void {
                let a: O<int> = O.zero;
                o(a);
                let b: O<int> = .zero;
                o(b);
                o(O<int>.zero);
            }
            """));
    }

    /// <summary>Controls: the variants, which name their instance in the call's type, and a
    /// struct, which never took the way that lost it.</summary>
    [Fact]
    public void Variants_and_a_structs_statics_keep_their_instance()
    {
        Assert.Equal("S 1\nN\nS 2\n3\n", Output("""
            struct B<T> {
                v: T,
                static fn mk(x: T): B<T> { return B<T> { v = x }; }
            }

            extend<T> B<T> {
                static fn mk2(x: T): B<T> { return B<T> { v = x }; }
            }

            fn main(): void {
                let a: O<int> = O.S(1);
                o(a);
                let b: O<int> = O.N;
                o(b);
                let c: O<int> = .S(2);
                o(c);
                let d: B<int> = B.mk(1);
                let e: B<int> = B.mk2(2);
                println(d.v + e.v);
            }
            """));
    }

    /// <summary>An argument that is not of the instance asked for stays the program's mistake.</summary>
    [Theory]
    [InlineData("O.blk(1)")]
    [InlineData("O.body(1)")]
    [InlineData(".blk(1)")]
    public void A_wrong_argument_is_said_against_the_expected_instance(string call)
    {
        var (exit, _, error) = Run("build", "-C", Project($"fn main(): void {{ let _v: O<string> = {call}; }}"));
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0001]: cannot assign 'int' to 'string'", error);
        Assert.DoesNotContain("LYR-ICE", error);
    }
}
