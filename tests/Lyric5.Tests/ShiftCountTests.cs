using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The count of a shift is an integer of ANY type, whatever the left operand's (the review's
/// A9f; Rust and Go have it so): <c>a &lt;&lt; n</c> with an <c>int8</c> and an <c>int</c> was
/// refused as every mixed operator is (<c>LYR-SEM0003</c>). The result has the left operand's
/// type, and the left operand is what it would be without the shift: <c>1 &lt;&lt; n</c> took
/// the COUNT's type — a <c>uint8</c> that panics at eight. A count that is negative or not below
/// the width panics (<c>LYR-RT0002</c>) with the count as it was written: through the
/// library's <c>shl</c> an <c>int8</c> shifted by 300 panicked "by 44", the count cast to the
/// left's type first — and shifted by 259 it did not panic: that was a shift by 3.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class ShiftCountTests
{
    private const string Head = "import std.io { println };\nimport std.core { Shl, Shr };\n\n";

    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>What a program that builds wrote to the error stream when it ended in a panic.</summary>
    private static string Panic(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 101, $"exit {ran.ExitCode}\n{ran.Stdout}\n{ran.Stderr}");
        return ran.Stderr.Replace("\r\n", "\n");
    }

    /// <summary>The one error a program is refused with: its text. A second error fails.</summary>
    private static string Refused(string main, string code)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit != 0, "the program was taken");
        var codes = Regex.Matches(error, @"error\[(LYR-[A-Z]+\d+)\]").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(codes.Length == 1 && codes[0] == code, $"expected exactly one {code}, got:\n{error}");
        return error;
    }

    [Fact]
    public void A_count_is_an_integer_of_any_type()
    {
        Assert.Equal("8 50 1099511627776 8 -4\n", Output("""
            fn main(): void {
                let a: int8 = 1;
                let n: int = 3;
                let u: uint8 = 200;
                let m: int8 = 2;
                let w: uint64 = 1;
                let k: int = 40;
                let i: int = 1;
                let c: uint8 = 3;
                let low: int16 = -16;
                let two: uint64 = 2;
                println(f"{a << n} {u >> m} {w << k} {i << c} {low >> two}");
            }
            """));
    }

    /// <summary>The left operand is what it would be alone: a literal is an <c>int</c>, or what
    /// the position wants — a typed binding, a parameter, the other operand of an operator —,
    /// never the count's type. <c>1 &lt;&lt; n</c> with a <c>uint8</c> count was a
    /// <c>uint8</c>, and panicked at eight; and <c>flags &amp; (1 &lt;&lt; bit)</c> compiled
    /// only where <c>bit</c> had the flags' type to lend.</summary>
    [Fact]
    public void The_result_has_the_left_operands_type()
    {
        Assert.Equal("1099511627776 1099511627776 8 16 | 4 133 true\n", Output("""
            fn widen(x: uint64): uint64 {
                return x;
            }

            fn main(): void {
                let n: uint8 = 40;
                let wide = 1 << n;
                let mask: uint64 = 1 << n;
                let small: int8 = 1;
                let three: int = 3;
                let kept: int8 = small << three;
                let four: uint8 = 4;
                let flags: uint8 = 5;
                let bit: int = 2;
                let both = flags & (1 << bit);
                let more = (1 << 7) | flags;
                let same = flags == 5 << (bit - 2);
                println(f"{wide} {mask} {kept} {widen(1 << four)} | {both} {more} {same}");
            }
            """));
    }

    /// <summary>A shifted literal takes its type at every place a literal does: a return, a
    /// field of an initializer, an element of an array, an assignment, an optional.</summary>
    [Fact]
    public void A_shifted_literal_takes_its_type_where_a_literal_does()
    {
        Assert.Equal("8 8 8 16 8 8\n", Output("""
            struct Cell {
                bits: uint8,
            }

            fn ret(n: int): uint8 {
                return 1 << n;
            }

            fn main(): void {
                let n: int = 3;
                let cell = Cell { bits = 1 << n };
                let xs: uint8[] = [1 << n, 2 << n];
                var slot: uint16 = 0;
                slot = 1 << n;
                let opt: ?uint8 = 1 << n;
                println(f"{ret(n)} {cell.bits} {xs[0]} {xs[1]} {slot} {opt ?? 0}");
            }
            """));
    }

    [Fact]
    public void A_compound_shift_takes_any_count()
    {
        Assert.Equal("4 20 3\n", Output("""
            class Cell {
                var bits: uint8 = 5,
            }

            fn main(): void {
                var a: int8 = 1;
                let n: int = 3;
                a <<= n;
                a >>= 1;
                let cell = Cell { };
                let two: int16 = 2;
                cell.bits <<= two;
                let xs: uint16[] = [12];
                let by: uint8 = 2;
                xs[0] >>= by;
                println(f"{a} {cell.bits} {xs[0]}");
            }
            """));
    }

    /// <summary>The check is on the count AS IT IS, in its own type: 300 is not below the eight
    /// bits of an <c>int8</c>, and is not 44 either.</summary>
    [Theory]
    [InlineData("let a: int8 = 1;\n    let n: int = 300;\n    println(f\"{a << n}\");", "shift by 300 exceeds the width of 8 bits")]
    [InlineData("let a: uint8 = 1;\n    let n: int = -1;\n    println(f\"{a << n}\");", "shift by -1 exceeds the width of 8 bits")]
    [InlineData("let a: int = 1;\n    let n: uint8 = 64;\n    println(f\"{a << n}\");", "shift by 64 exceeds the width of 64 bits")]
    [InlineData("let a: uint16 = 1;\n    let n: int8 = 16;\n    println(f\"{a >> n}\");", "shift by 16 exceeds the width of 16 bits")]
    [InlineData("let a: int8 = 1;\n    println(f\"{a << 300}\");", "shift by 300 exceeds the width of 8 bits")]
    [InlineData("let a: int32 = 1;\n    let n: int8 = -128;\n    println(f\"{a >> n}\");", "shift by -128 exceeds the width of 32 bits")]
    public void A_count_outside_the_width_panics_as_it_was_written(string statements, string message)
    {
        var error = Panic("fn main(): void {\n    " + statements + "\n}\n");
        Assert.StartsWith("panic [LYR-RT0002]: " + message + "\n", error);
    }

    /// <summary>Through the operator's interface the count is the interface's — an <c>int</c>
    /// unless the conformer says otherwise —, and the library's integers shift by it as it is.
    /// They cast it to their own type first: 300 on an <c>int8</c> panicked "by 44", and 259
    /// did not panic at all — it was a shift by 3.</summary>
    [Fact]
    public void The_librarys_integers_shift_by_the_count_they_are_given()
    {
        Assert.Equal("8 4 16 2\n", Output("""
            fn up<T :: [Shl<Out = T>]>(x: T, n: int): T {
                return x << n;
            }

            fn down<T :: [Shr<Out = T>]>(x: T, n: int): T {
                return x >> n;
            }

            fn main(): void {
                let a: int8 = 1;
                let b: uint16 = 16;
                let c: uint64 = 1;
                println(f"{up(a, 3)} {down(b, 2)} {up(c, 4)} {down(a.shl(2), 1)}");
            }
            """));
        var error = Panic("""
            fn up<T :: [Shl<Out = T>]>(x: T, n: int): T {
                return x << n;
            }

            fn main(): void {
                let a: int8 = 1;
                println(f"{up(a, 259)}");
            }
            """);
        Assert.StartsWith("panic [LYR-RT0002]: shift by 259 exceeds the width of 8 bits\n", error);
    }

    // ------------------------------------------------------------------ what stays

    /// <summary>A shift takes integers: a float, a bool, a char on either side is refused.</summary>
    [Theory]
    [InlineData("let x = 1.5 << 2;")]
    [InlineData("let a: int = 1;\n    let x = a << 1.5;")]
    [InlineData("let a: int = 1;\n    let x = a >> true;")]
    [InlineData("let x = 'c' << 1;")]
    public void A_shift_takes_integers(string statements) =>
        Refused("fn main(): void {\n    " + statements + "\n}\n", "LYR-SEM0003");

    /// <summary>The other operators keep their rule — one type on both sides.</summary>
    [Theory]
    [InlineData("let x = a & n;")]
    [InlineData("let x = a + n;")]
    [InlineData("let x = a +% n;")]
    // a complement of a literal is no literal — it never adapted —, shifted or not
    [InlineData("let x = a & ~1;")]
    [InlineData("let x = a & ~(1 << n);")]
    public void The_other_operators_keep_one_type(string statement) =>
        Refused("fn main(): void {\n    let a: int8 = 1;\n    let n: int = 3;\n    " + statement + "\n}\n", "LYR-SEM0003");
}
