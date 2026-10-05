using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A member's bare name through the whole compiler (design/v5/spec/07, the review's M6-30): a
/// call passes a field that cannot be called and reaches the function outside; and what used to
/// crash the compiler — a bare field or method, typed as nothing and handed to the lowering — is
/// one diagnostic. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class BareMemberRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_call_passes_a_field_that_cannot_be_called()
    {
        Assert.Equal("outer field\nhi\n", Output("""
            import std.io { println };

            fn label(): string { return "outer"; }

            struct Tag {
                label: string,
                println: int,
                fn show(): string { return f"{label()} {this.label}"; }
                fn say(): void { println("hi"); }
            }

            fn main(): void {
                let t = Tag { label = "field", println = 0 };
                println(t.show());
                t.say();
            }
            """));
    }

    /// <summary>Controls: a local and a parameter hide field and function whole; a static member
    /// is reached by its name.</summary>
    [Fact]
    public void A_local_hides_the_field_and_a_static_needs_no_receiver()
    {
        Assert.Equal("local 2 3\n", Output("""
            import std.io { println };

            fn label(): string { return "outer"; }

            struct Tag {
                label: string,
                static let step: int = 2;
                static fn of(n: int): int { return n + step; }
                fn show(): string {
                    let label = () => "local";
                    return label();
                }
                fn plain(label: int): int { return label + 1; }
                fn next(): int { return of(1); }
            }

            fn main(): void {
                let t = Tag { label = "field" };
                println(f"{t.show()} {t.plain(1)} {t.next()}");
            }
            """));
    }

    /// <summary>Each of these reached the lowering: as an error type in arithmetic
    /// (<c>LYR-ICE0001</c>), as a call without its receiver (malformed IR), as a reference the
    /// lowering had no word for (<c>LYR-IR0001</c>).</summary>
    [Theory]
    [InlineData("struct P { x: int, fn go(): int { return x * 2; } }")]
    [InlineData("struct P { x: int, fn get(): int { return this.x; } fn go(): int { return get() * 2; } }")]
    [InlineData("struct P { x: int, fn go(): int { let later = () => x + 1; return later(); } }")]
    [InlineData("struct P { x: int, fn go(): int { return x; } }")]
    [InlineData("struct P { x: int }\nextend P { fn get(): int { return this.x; } fn go(): int { return get() * 2; } }")]
    public void A_bare_member_is_one_diagnostic_and_no_crash(string declarations)
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", declarations + "\nfn main(): int { return P { x = 3 }.go(); }\n"));
        var (exit, _, err) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{err}");
        Assert.Contains("error[LYR-SEM0055]", err);
        Assert.Single(Regex.Matches(err, @"error\["));
    }
}
