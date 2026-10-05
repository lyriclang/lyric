using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A constant of a generic type at run time (design/v5/spec/07 G2; the review's M8a-3, M8a-3b):
/// one per instance, folded where it is read — through the written instance, inside the type,
/// through a constraint, in a generic block. A constant that named its type's parameter did not
/// lower (<c>LYR-IR0001</c>: "type parameter 'T' reached lowering unsubstituted"). Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class InstanceConstantRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_constant_is_its_instances()
    {
        Assert.Equal("true true 2 2 | 0 1 | 0 -1 | 2\n", Output("""
            import std.io { println };

            struct Point {
                x: int,
                y: int,
            }

            struct Crate<T> {
                v: T,
                static let empty: ?T = null;
                static let alias: int = count;
                static let count: int = 2;
                static let origin: Point = Point { x = 0, y = -1 };
            }

            struct Acc<T :: [Num]> {
                v: T,
                static let start: T = T.zero;
                static let unit: T = T.one;
            }

            fn main(): void {
                let a: ?int = Crate<int>.empty;
                let b: ?string = Crate<string>.empty;
                let start: int = Acc<int>.start;
                let unit: int8 = Acc<int8>.unit;
                let o = Crate<bool>.origin;
                println(f"{a == null} {b == null} {Crate<int>.count} {Crate<bool>.count} | {start} {unit} | {o.x} {o.y} | {Crate<int>.alias}");
            }
            """));
    }

    [Fact]
    public void A_constant_is_read_inside_its_type_and_in_a_generic_block()
    {
        Assert.Equal("2 true 6 | 2 true\n", Output("""
            import std.io { println };

            struct Stack<T> {
                items: T[],
                static let limit: int = 8;
                static let none: ?T = null;
                fn top(): ?T {
                    if (this.items.length() == 0) {
                        return Stack<T>.none;
                    }
                    return this.items[this.items.length() - 1];
                }
                fn room(): int { return limit - this.items.length(); }
            }

            struct Pair<T> {
                a: T,
                b: T,
            }

            extend<T> Pair<T> {
                static let size: int = 2;
                static let none: ?T = null;
            }

            fn main(): void {
                let s = Stack<int> { items = [1, 2] };
                let e = Stack<string> { items = [] };
                let n: ?int = Pair<int>.none;
                println(f"{s.top() ?? -1} {e.top() == null} {s.room()} | {Pair<int>.size} {n == null}");
            }
            """));
    }

    [Fact]
    public void A_constant_of_an_instance_answers_an_interface()
    {
        Assert.Equal("0 0 | 5\n", Output("""
            import std.io { println };

            interface Zeroed {
                static let zero: Self;
            }

            struct Wrap<T :: [Num]> :: [Zeroed] {
                v: T,
                static let zero: Wrap<T> = Wrap<T> { v = T.zero };
            }

            fn zeroOf<Z :: [Zeroed]>(): Z { return Z.zero; }

            fn main(): void {
                let w: Wrap<int> = zeroOf();
                let small: Wrap<int8> = zeroOf();
                let five = Wrap<int> { v = 5 };
                println(f"{w.v} {small.v} | {five.v}");
            }
            """));
    }

    /// <summary>A generic conformance block's constant answers the interface's for the instances
    /// the block reaches — it was "does not answer the static 'zero' … declare 'static let zero'
    /// on it", at the block that declares it.</summary>
    [Fact]
    public void A_generic_blocks_constant_answers_an_interface()
    {
        Assert.Equal("0 0 | 7\n", Output("""
            import std.io { println };

            interface Zeroed {
                static let zero: Self;
            }

            struct Wrap<T> {
                v: T,
            }

            extend<T :: [Num]> Wrap<T> :: [Zeroed] {
                static let zero: Wrap<T> = Wrap<T> { v = T.zero };
            }

            fn zeroOf<Z :: [Zeroed]>(): Z { return Z.zero; }

            fn main(): void {
                let w: Wrap<int> = zeroOf();
                let direct = Wrap<int8>.zero;
                let seven = Wrap<int> { v = 7 };
                println(f"{w.v} {direct.v} | {seven.v}");
            }
            """));
    }

    /// <summary>
    /// The library's <c>infinity</c> and <c>nan</c> (10 B5) are written by their bits,
    /// <c>float.fromBits(0x7FF0000000000000)</c> — the language has no literal for them —, and were
    /// four globals with a call to initialize in every program. They are the floats those bits
    /// are, where they are read: no global of theirs is in the program.
    /// </summary>
    [Fact]
    public void The_librarys_infinity_and_nan_are_no_globals()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };

            fn top<F :: [Float]>(): F { return F.infinity; }

            fn main(): void {
                let inf = float.infinity;
                let nan = float.nan;
                let small: float32 = float32.infinity;
                let through: float = top();
                println(f"{inf > float.max} {nan != nan} {small > float32.max} {float32.nan != float32.nan} {through == inf} {-inf < -float.max}");
            }
            """));
        var (exit, ir, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("float.infinity", ir);
        Assert.DoesNotContain("float.nan", ir);
        Assert.DoesNotContain("float32.infinity", ir);
        Assert.DoesNotContain("float32.nan", ir);
        Assert.Equal("true true true true true true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_call_in_a_generic_types_constant_is_refused()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            fn make(): int { return 3; }

            struct Crate<T> {
                v: T,
                static let made: int = make();
            }

            fn main(): void {
            }
            """));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("main.lyr:5:28: error[LYR-SEM0169]", error);
        Assert.Contains("a call", error);
    }
}
