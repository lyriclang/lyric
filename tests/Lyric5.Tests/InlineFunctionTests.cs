using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>inline fn</c>: its body set in place at every call,
/// and the lambda literals at its function parameters with it — no closure; a <c>return</c> in
/// one leaves the function that wrote it, <c>break</c> and <c>continue</c> reach that function's
/// loops. The value arguments are evaluated first, left to right, each once. What may not be
/// inline, and what an inline function's parameters may not do, the checker says.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class InlineFunctionTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    private static string[] Lines(string text, string code) =>
        text.Split('\n').Where(l => l.Contains($"[{code}]")).ToArray();

    /// <summary><c>forEach { if (…) return }</c> leaves the function.</summary>
    [Fact]
    public void A_return_in_a_lambda_set_in_place_leaves_the_function()
    {
        var dir = App("""
            import std.io { println };

            fn firstOver(xs: int[], limit: int): int {
                xs.iter().forEach { if (it > limit) { return it; } };
                return -1;
            }

            fn main(): void {
                println(f"{firstOver([1, 4, 9, 2], 3)} {firstOver([1, 2], 3)}");
            }
            """);
        Assert.Equal("4 -1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>break</c> and <c>continue</c> in the lambda are the loop's around the call:
    /// <c>continue</c> goes on with the next row, not the next element; <c>break</c> leaves the
    /// rows.</summary>
    [Fact]
    public void Break_and_continue_reach_the_loop_around_the_call()
    {
        var dir = App("""
            import std.io { println };

            fn sumRows(rows: int[][]): int {
                var total = 0;
                for (row in rows) {
                    row.iter().forEach((x) => {
                        if (x < 0) { break; }
                        if (x == 0) { continue; }
                        total += x;
                    });
                }
                return total;
            }

            fn main(): void {
                println(f"{sumRows([[1, 2, 0, 5], [3, -1, 7], [4]])}");
            }
            """);
        Assert.Equal("6\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>One expansion inside another of the same function: each keeps its own names —
    /// the inner <c>forEach</c>'s iterator does not stand in for the outer's afterwards.</summary>
    [Fact]
    public void An_expansion_inside_another_of_the_same_function_keeps_its_names()
    {
        var dir = App("""
            import std.io { println };

            fn grid(): int {
                var n = 0;
                (0..3).forEach((a) => {
                    (0..4).forEach((b) => { n += a * 10 + b; });
                });
                return n;
            }

            fn main(): void {
                println(f"{grid()}");
            }
            """);
        Assert.Equal("138\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A <c>return</c> that leaves through the inline body runs the body's defer; the
    /// body's own <c>return</c> is the expansion's value; a lambda's block gives its value as its
    /// last expression.</summary>
    [Fact]
    public void A_return_through_the_body_runs_its_defer()
    {
        var dir = App("""
            import std.io { println };

            inline fn guarded(f: fn() -> int): int {
                defer println("left");
                return f() + 1;
            }

            fn useGuard(flag: bool): int {
                let v = guarded(() => {
                    if (flag) { return 100; }
                    5
                });
                return v * 2;
            }

            fn main(): void {
                println(f"{useGuard(true)}");
                println(f"{useGuard(false)}");
            }
            """);
        Assert.Equal("left\n100\nleft\n12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Every way a call reaches an inline function sets it in place: a generic
    /// function, a generic type's method and its static, a generic block's static, an enum's
    /// method, a lambda passed on to another inline function, a type parameter's receiver.</summary>
    [Fact]
    public void Every_call_path_sets_in_place()
    {
        var dir = App("""
            import std.io { println };

            inline fn twice<T>(x: T, f: fn(T) -> T): T {
                let once = f(x);
                return f(once);
            }

            struct Holder<T> {
                value: T,
                inline fn apply<U>(f: fn(T) -> U): U {
                    return f(this.value);
                }
                static inline fn make(v: T): Holder<T> {
                    return Holder { value = v };
                }
            }

            struct Pair<T> {
                a: T,
                b: T,
            }

            extend<T> Pair<T> {
                static inline fn build(f: fn() -> T): Pair<T> {
                    return Pair { a = f(), b = f() };
                }
            }

            enum Light {
                Red, Green;
                inline fn both(f: fn() -> int): int {
                    return f() + f();
                }
            }

            inline fn each(xs: int[], f: fn(int) -> void): void {
                xs.iter().forEach(f);
            }

            fn firstNeg(xs: int[]): int {
                each(xs, (x) => { if (x < 0) { return x; } });
                return 0;
            }

            fn countOf<I :: [Iterator<Item = int>]>(src: I): int throws I.Error {
                var n = 0;
                try src.forEach { n += 1; };
                return n;
            }

            fn pairOf(flag: bool): int {
                let p = Pair<int>.build(() => {
                    if (flag) { return -1; }
                    7
                });
                return p.a + p.b;
            }

            fn main(): void {
                let doubled = twice(3, (v) => v * 2);
                let grown = twice("a", (s) => s + "b");
                println(f"{doubled} {grown}");
                let h = Holder<int>.make(4);
                let s = h.apply((v) => f"<{v}>");
                let n = h.apply((v) => v * 10);
                println(f"{s} {n}");
                println(f"{pairOf(false)} {pairOf(true)}");
                println(f"{Light.Red.both(() => 3)}");
                println(f"{firstNeg([3, -2, -5])} {firstNeg([1])}");
                println(f"{countOf(0..5)}");
            }
            """);
        Assert.Equal("12 abb\n<4> 40\n14 -1\n6\n-2 0\n5\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A throwing lambda's error leaves through the inline body into the caller's error
    /// path: the call's <c>try</c> is its mark.</summary>
    [Fact]
    public void An_error_in_the_lambda_leaves_through_the_body()
    {
        var dir = App("""
            import std.io { println };

            enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }

            fn check(x: int): int throws Oops {
                if (x > 2) { throw Oops.Bad; }
                return x;
            }

            fn sumChecked(xs: int[]): int throws Oops {
                var total = 0;
                try xs.iter().forEach { total += try check(it); };
                return total;
            }

            fn main(): void {
                try {
                    let small = sumChecked([1, 2]);
                    println(f"{small}");
                    let big = sumChecked([1, 5, 2]);
                    println(f"{big}");
                } catch (_: Oops) {
                    println("oops");
                }
            }
            """);
        Assert.Equal("3\noops\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A closure around a lambda set in place captures what the lambda uses — also what
    /// only the lambda names (<c>base</c>), and a <c>var</c> it writes, in its cell.</summary>
    [Fact]
    public void A_closure_around_a_lambda_set_in_place_captures_what_it_uses()
    {
        var dir = App("""
            import std.io { println };

            fn later(xs: int[]): fn() -> int {
                var total = 0;
                return () => {
                    xs.iter().forEach { total += it; };
                    total
                };
            }

            fn adder(base: int): fn(int[]) -> int {
                return (xs) => {
                    var s = 0;
                    xs.iter().forEach { s += it + base; };
                    s
                };
            }

            fn main(): void {
                let k = later([1, 2, 3]);
                let first = k();
                let second = k();
                println(f"{first} {second}");
                println(f"{adder(10)([1, 2])}");
            }
            """);
        Assert.Equal("6 12\n23\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What is set in place leaves nothing behind: no function for the lambda, no
    /// indirect call in the caller — in debug, where no IR inliner runs: what is not there was never
    /// made.</summary>
    [Fact]
    public void No_closure_is_left_in_the_ir()
    {
        var dir = App("""
            import std.io { println };

            fn firstOver(xs: int[], limit: int): int {
                xs.iter().forEach { if (it > limit) { return it; } };
                return -1;
            }

            fn main(): void {
                println(f"{firstOver([1, 4, 9, 2], 3)}");
            }
            """);
        var (exit, ir, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        var own = ir.Split("\nfn ").Where(f => f.StartsWith("app.main.", StringComparison.Ordinal)).ToArray();
        Assert.DoesNotContain(own, f => f.Contains("<lambda", StringComparison.Ordinal));
        var caller = Assert.Single(own, f => f.StartsWith("app.main.firstOver ", StringComparison.Ordinal));
        Assert.DoesNotContain("callind", caller);
    }

    /// <summary>An inline function's function parameter is called — or passed on to another
    /// inline function's —, never kept (SEM0181); an inline function does not reach itself
    /// (SEM0182) and is no value (SEM0183).</summary>
    [Fact]
    public void An_inline_functions_lambda_is_called_and_the_function_is_no_value()
    {
        var dir = App("""
            inline fn keep(f: fn() -> void): fn() -> void {
                return f;
            }

            inline fn down(n: int): int {
                return down(n - 1);
            }

            inline fn inc(x: int): int {
                return x + 1;
            }

            fn main(): void {
                let h = inc;
                keep(() => {});
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("main.lyr:2:12: error[LYR-SEM0181]: 'f' is set in place at every call of 'keep'", Assert.Single(Lines(error, "LYR-SEM0181")));
        Assert.Contains("main.lyr:6:12: error[LYR-SEM0182]: 'down' is an inline function this call reaches again", Assert.Single(Lines(error, "LYR-SEM0182")));
        Assert.Contains("main.lyr:14:13: error[LYR-SEM0183]: 'inc' is an inline function", Assert.Single(Lines(error, "LYR-SEM0183")));
    }

    /// <summary>What an expansion does not take (SEM0184): an interface's member, no body, a
    /// <c>mut</c> receiver, a place parameter, a body that yields.</summary>
    [Fact]
    public void What_cannot_be_inline_is_named()
    {
        var dir = App("""
            interface Shape {
                inline fn area(): int;
            }

            extern "C" inline fn abs(x: int32): int32;

            struct Counter {
                var v: int,
                inline mut fn bump(): void {
                    this.v += 1;
                }
            }

            inline fn add(&x: int): void {
                x += 1;
            }

            inline fn numbers(): Coroutine<int> {
                yield 1;
            }

            fn main(): void { }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var refused = Lines(error, "LYR-SEM0184");
        Assert.Equal(5, refused.Length);
        Assert.Contains(refused, l => l.Contains("main.lyr:2:15:") && l.Contains("an interface's member is called through its conformer's table"));
        Assert.Contains(refused, l => l.Contains("main.lyr:5:22:") && l.Contains("it has no body to set in place"));
        Assert.Contains(refused, l => l.Contains("main.lyr:9:19:") && l.Contains("its receiver would be the caller's place"));
        Assert.Contains(refused, l => l.Contains("main.lyr:14:11:") && l.Contains("'x' takes a place"));
        Assert.Contains(refused, l => l.Contains("main.lyr:18:11:") && l.Contains("its body yields"));
    }

    /// <summary>The order is <c>static inline fn</c>: the other one is refused by its own text
    /// and read as what it says — one error, no cascade.</summary>
    [Fact]
    public void Inline_static_is_refused_with_the_order()
    {
        var dir = App("""
            struct Box2<T> {
                value: T,
                inline static fn make(v: T): Box2<T> {
                    return Box2 { value = v };
                }
            }

            fn main(): void {
                let b = Box2<int>.make(4);
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Contains("main.lyr:3:12: error[LYR-PAR0032]: 'static' comes before 'inline': write 'static inline fn'", Assert.Single(errors));
    }
}
