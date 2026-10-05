using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What a call wrote beside its arguments reaches the program: the parentheses
/// <c>@callerExpr</c> quotes (09 A11) — where a trailing block follows the call, and where the
/// call is a type's, <c>Shown(…)</c> for <c>Shown.new(…)</c>.
/// </summary>
[Collection("console")]
public class TrailingBlockCallTests
{
    [Fact]
    public void Caller_expr_quotes_the_parentheses_before_a_trailing_block()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                fn show(value: int, f: fn(int) -> int, @callerExpr(value) text: string = ""): string {
                    return f"{text} = {f(value)}";
                }

                fn main(): void {
                    println(show((1 + 2), (x) => x));
                    println(show((1 + 2)) { it });
                }
                """));
        Assert.Equal("(1 + 2) = 3\n(1 + 2) = 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>Shown(…)</c> is <c>Shown.new(…)</c> (04 D8): the call the checker writes for
    /// it is the call as it was written, the parentheses of its arguments with it.</summary>
    [Fact]
    public void The_factorys_call_keeps_the_parentheses_too()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                struct Shown {
                    text: string,
                    static fn new(value: int, @callerExpr(value) text: string = ""): Shown {
                        return Shown { text = text };
                    }
                }

                fn main(): void {
                    println(Shown((1 + 2)).text);
                    println(Shown(1 + 2).text);
                }
                """));
        Assert.Equal("(1 + 2)\n1 + 2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
