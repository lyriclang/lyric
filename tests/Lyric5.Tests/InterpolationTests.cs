namespace Lyric5.Tests;

/// <summary>
/// An f-string is checked as a plan of calls (10 S6; M8a S13): each piece holds its hole's
/// expression, checked once as the hole and again in the piece. What the hole reports is reported
/// once.
/// </summary>
public class InterpolationTests
{
    private static string[] Codes(string source) =>
        TestCompiler.Lower("interpolation.lyr", source).Diagnostics.Diagnostics.Select(d => d.Code).ToArray();

    [Fact]
    public void An_error_a_hole_recovers_from_is_reported_once()
    {
        // '==' where the type has none is an error that answers bool: the hole goes on to a piece.
        var codes = Codes("""
            import std.io { println };

            struct P { x: int }

            fn main(): void {
                let p = P { x = 1 };
                let s = f"[{p == p}]";
                println(s);
            }
            """);
        Assert.Single(codes, c => c == "LYR-SEM0059");
    }

    [Fact]
    public void A_hole_that_renders_is_no_diagnostic()
    {
        Assert.Empty(Codes("""
            import std.io { println };

            fn main(): void {
                let n = 3;
                println(f"[{n}] [{n:>4}] [{n:?}] [{"s"}] [{2.5}]");
            }
            """));
    }
}
