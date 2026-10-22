using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A local <c>fn</c>: a named closure — it captures as a
/// lambda does, writes a captured <c>var</c> through its cell, calls itself, and is a value of its
/// function type. No type parameters, no default, no variadic parameter.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class LocalFunctionTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    [Fact]
    public void A_local_function_captures_calls_itself_and_is_a_value()
    {
        var dir = App("""
            import std.io { println };

            enum Oops :: [Error] { Bad; fn message(): string { return "oops"; } }

            fn main(): void {
                let base = 10;
                fn fact(n: int): int {
                    return if (n <= 1) 1 else n * fact(n - 1);
                }
                fn plus(n: int): int {
                    return n + base;
                }
                var count = 0;
                fn bump(): void {
                    count += 1;
                }
                fn half(n: int): int throws Oops {
                    if (n % 2 != 0) { throw Oops.Bad; }
                    return n / 2;
                }
                bump();
                bump();
                println(f"{fact(5)} {plus(5)} {count}");
                let f: fn(int) -> int = plus;
                println(f"{f(1)}");
                try {
                    let h = half(8);
                    println(f"{h}");
                    let odd = half(3);
                    println(f"{odd}");
                } catch (_: Oops) {
                    println("odd");
                }
            }
            """);
        Assert.Equal("120 15 2\n11\n4\nodd\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A closure is one function: no type parameters, no default, no variadic rest
    /// — each refused where it stands, nothing after it.</summary>
    [Fact]
    public void A_local_function_takes_no_type_parameters_and_no_defaults()
    {
        var dir = App("""
            fn main(): void {
                fn id<T>(x: T): T {
                    return x;
                }
                fn dflt(n: int = 3): int {
                    return n;
                }
                fn all(ns: int...): int {
                    return 0;
                }
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.Equal(3, errors.Length);
        Assert.All(errors, l => Assert.Contains("[LYR-PAR0060]", l));
        Assert.Contains(errors, l => l.Contains("main.lyr:2:8:") && l.Contains("takes no type parameters"));
        Assert.Contains(errors, l => l.Contains("main.lyr:5:13:") && l.Contains("'n'"));
        Assert.Contains(errors, l => l.Contains("main.lyr:8:12:") && l.Contains("'ns'"));
    }
}
