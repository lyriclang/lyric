using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Generators after the review (design/v5/spec/06 M6-1, M6-3, M6-4; 03 M6-5; 10 M6-7).
///
/// <para>WHAT A GENERATOR IS (M6-3): a function with a <c>Coroutine&lt;…&gt;</c> result that
/// yields itself OR returns no value. One that yields through helpers only, and one with an
/// empty body, were "ordinary functions" that returned nothing (<c>LYR-SEM0017</c>);
/// <c>return make();</c> stays a factory.</para>
///
/// <para><c>main</c> COVERS <c>Cancelled</c> ITSELF (M6-4), as a generator's body does: a
/// program that awaits needed <c>throws Cancelled</c> on <c>main</c>, a clause about a
/// cancellation nobody above <c>main</c> can answer.</para>
///
/// <para>TWO WARNINGS: a <c>mut fn</c> generator on a value type changes a copy of its receiver
/// (M6-1, <c>LYR-SEM0172</c>); a <c>void</c> bound to a name, or written as a parameter's type
/// (M6-5, <c>LYR-SEM0173</c>). Both were silent.</para>
///
/// <para><c>sequence</c> is the prelude's (M6-7).</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class GeneratorDefinitionTests
{
    private const string Head = "import std.io { println };\nimport std.task { spawn, sleep, Cancelled, Task };\nimport std.time { Duration };\n\n";

    private static (int Exit, string Error, string Dir) Built(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        return (exit, error, dir);
    }

    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string main, string code)
    {
        var (exit, error, _) = Built(main);
        Assert.True(exit != 0, "the program was taken");
        var codes = Regex.Matches(error, @"error\[(LYR-[A-Z]+\d+)\]").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(codes.Length == 1 && codes[0] == code, $"expected exactly one {code}, got:\n{error}");
        return error;
    }

    /// <summary>What a program that builds says while it is built, and what it prints.</summary>
    private static (string Diagnostics, string Printed) Ran(string main)
    {
        var (exit, error, dir) = Built(main);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(
            Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        return (error, ran.Stdout.Replace("\r\n", "\n"));
    }

    // ------------------------------------------------------------------ what a generator is

    [Fact]
    public void A_function_that_returns_no_value_is_a_generator()
    {
        Assert.Equal("1 2 | 0 | 1 2 | 7\n", Output("""
            fn emit(n: int): void throws Cancelled {
                yield n;
            }

            fn numbers(): Coroutine<int> {
                try emit(1);
                try emit(2);
            }

            fn none(): Coroutine<int> {
            }

            fn make(): Coroutine<int> {
                return numbers();
            }

            fn early(stop: bool): Coroutine<int> {
                if (stop) {
                    return;
                }
                try emit(7);
            }

            fn text(co: Coroutine<int>): string {
                var out = "";
                for (x in co) {
                    out = if (out == "") f"{x}" else f"{out} {x}";
                }
                return if (out == "") "0" else out;
            }

            fn main(): void {
                println(f"{text(numbers())} | {text(none())} | {text(make())} | {text(early(false))}");
            }
            """));
    }

    /// <summary>"Returns no value" is said of the body as it is written, not of where it can
    /// arrive: a body that only throws is a generator too, and throws at its FIRST PULL — the
    /// call runs nothing. It was a function that threw at its call.</summary>
    [Fact]
    public void A_body_that_only_throws_is_a_generator_that_throws_when_it_is_pulled()
    {
        Assert.Equal("made\noops\n", Output("""
            enum Oops :: [Error] {
                Bad;

                fn message(): string {
                    return "oops";
                }
            }

            fn refuse(): Coroutine<int> throws Oops {
                throw Oops.Bad;
            }

            fn main(): void {
                let c = refuse();
                println("made");
                try {
                    let v = c.next() ?? 0;
                    println(f"not reached {v}");
                } catch (e) {
                    println(e.message());
                }
            }
            """));
    }

    /// <summary>A function that returns a value is a factory and no generator: its body is
    /// held to its result type as any function's.</summary>
    [Fact]
    public void A_function_that_returns_a_value_is_a_factory() =>
        Refused("""
            fn wrong(): Coroutine<int> {
                return 5;
            }

            fn main(): void {
            }
            """, "LYR-SEM0001");

    // ------------------------------------------------------------------ main and Cancelled

    [Fact]
    public void Main_covers_cancelled_itself()
    {
        Assert.Equal("42\n", Output("""
            fn main(): void {
                let t = spawn(() => 41 + 1);
                println(f"{try t.await()}");
            }
            """));
    }

    /// <summary>A cancellation that does leave <c>main</c> is reported as any error that
    /// leaves it is (05 E6 O4), and the program exits with 1 — as with the clause written.</summary>
    [Fact]
    public void A_cancelled_that_leaves_main_is_reported()
    {
        var (exit, error, dir) = Built("""
            fn slow(): int throws Cancelled {
                try sleep(Duration.ofSecs(30));
                return 1;
            }

            fn main(): void {
                let t = spawn(() => try slow());
                t.cancel();
                println("before");
                println(f"{try t.await()}");
                println("after");
            }
            """);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(
            Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromMinutes(1));
        Assert.Equal(1, ran.ExitCode);
        Assert.Equal("before\n", ran.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("error: cancelled", ran.Stderr);
    }

    /// <summary>A named function declares as before; and the awaiting call keeps its mark in
    /// <c>main</c> too.</summary>
    [Theory]
    [InlineData("fn wait(t: Task<int>): int {\n    return try t.await();\n}\nfn main(): void {\n}\n", "LYR-SEM0034")]
    [InlineData("fn main(): void {\n    let t = spawn(() => 1);\n    println(f\"{t.await()}\");\n}\n", "LYR-SEM0138")]
    public void Everything_else_about_cancelled_stays(string program, string code) => Refused(program, code);

    // ------------------------------------------------------------------ the two warnings

    [Fact]
    public void A_mut_generator_on_a_value_type_is_warned_about()
    {
        var (diagnostics, printed) = Ran("""
            struct Counter {
                var n: int,

                mut fn count(): Coroutine<int> {
                    this.n += 1;
                    yield this.n;
                    this.n += 1;
                    yield this.n;
                }

                fn twice(): Coroutine<int> {
                    yield this.n;
                    yield this.n;
                }
            }

            class Tally {
                var n: int = 0,

                mut fn count(): Coroutine<int> {
                    this.n += 1;
                    yield this.n;
                }
            }

            fn main(): void {
                var c = Counter { n = 0 };
                for (x in c.count()) {
                    println(f"{x}");
                }
                println(f"{c.n}");
                let t = Tally { };
                for (x in t.count()) {
                    println(f"{x}");
                }
                println(f"{t.n}");
            }
            """);
        Assert.Equal("1\n2\n0\n1\n1\n", printed);
        Assert.Single(Regex.Matches(diagnostics, @"warning\[LYR-SEM0172\]"));
        Assert.Contains("main.lyr:8:12: warning[LYR-SEM0172]", diagnostics);
        Assert.Contains("changes a copy of 'this'", diagnostics);
    }

    [Fact]
    public void A_void_bound_to_a_name_is_warned_about()
    {
        var (diagnostics, printed) = Ran("""
            fn nothing(): void {
            }

            fn take(v: void): int {
                return 1;
            }

            fn id<T>(v: T): T {
                return v;
            }

            fn main(): void {
                let x: void = nothing();
                let y = nothing();
                let z = id(nothing());
                let maybe: ?void = null;
                let units: void[] = [];
                println(f"{take(x)} {maybe == null} {units.length()}");
            }
            """);
        Assert.Equal("1 true 0\n", printed);
        // the parameter, the two bindings a 'void' is written or inferred for, and the one a
        // generic function answers with it — the binding is the place, whatever made the value
        Assert.Equal(4, Regex.Matches(diagnostics, @"warning\[LYR-SEM0173\]").Count);
        Assert.Contains("main.lyr:8:9: warning[LYR-SEM0173]", diagnostics);
        Assert.Contains("main.lyr:17:9: warning[LYR-SEM0173]", diagnostics);
        Assert.Contains("main.lyr:18:9: warning[LYR-SEM0173]", diagnostics);
        Assert.Contains("main.lyr:19:9: warning[LYR-SEM0173]", diagnostics);
    }

    // ------------------------------------------------------------------ sequence

    [Fact]
    public void Sequence_is_the_preludes()
    {
        Assert.Equal("1 2 3\n", Output("""
            fn main(): void {
                let s = sequence {
                    yield 1;
                    yield 2;
                    yield 3;
                };
                var out = "";
                for (x in s) {
                    out = if (out == "") f"{x}" else f"{out} {x}";
                }
                println(out);
            }
            """));
    }
}
