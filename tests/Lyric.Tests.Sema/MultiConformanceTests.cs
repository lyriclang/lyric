using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Several conformances to one arithmetic interface (v3.0), from the checker's side: which one an
/// operator picks, what it says when none fits, and what it says when two do.
///
/// <para>Everything else in the language allows a type ONE conformance per interface. These
/// interfaces are the exception because the operator has a second type to select by — the right
/// operand — where an ordinary member call has only a name.</para>
/// </summary>
public class MultiConformanceTests
{
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };

        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private const string Vec2 = """
        import std.core { Mul };

        struct Vec2 :: [Mul<Vec2, Vec2>] {
            x: int,
            fn mul(other: Vec2): Vec2 { return Vec2 { x = this.x * other.x }; }
        }

        extend Vec2 :: [Mul<int, Vec2>] {
            fn mul(other: int): Vec2 { return Vec2 { x = this.x * other }; }
        }
        """;

    [Fact]
    public void Two_conformances_with_two_implementations_are_accepted()
    {
        var de = Check(Vec2 + """

            fn main(): int {
                let a = Vec2 { x = 2 };
                let b = a * a;
                let c = a * 3;
                return b.x + c.x;
            }
            """);
        Assert.False(de.HasErrors);
    }

    [Fact]
    public void A_conformance_without_a_matching_implementation_is_reported()
    {
        // Two conformances, one 'mul'. The name is there, so this is not "does not implement" but
        // "does not match": the message has to say what was expected, or the reader sees an
        // implementation standing right there and no reason it does not count.
        var de = Check("""
            import std.core { Mul };

            struct Vec2 :: [Mul<Vec2, Vec2>, Mul<int, Vec2>] {
                x: int,
                fn mul(other: Vec2): Vec2 { return Vec2 { x = this.x * other.x }; }
            }

            fn main(): int {
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0042");
        Assert.Contains("Mul", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_conformances_taking_the_same_operand_are_ambiguous()
    {
        // Same operand, different results: nothing in 'a * 2' says which is meant. Reported at the
        // USE, not at the declaration — an extend block from another module only meets the first
        // conformance where both are visible, and that is the call site.
        var de = Check("""
            import std.core { Mul };

            struct Vec2 :: [Mul<int, Vec2>] {
                x: int,
                fn mul(other: int): Vec2 { return Vec2 { x = this.x * other }; }
            }

            extend Vec2 :: [Mul<int, int>] {
                fn mul(other: int): int { return this.x * other; }
            }

            fn main(): int {
                let a = Vec2 { x = 2 };
                let b = a * 3;
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0083");
        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_operand_no_conformance_takes_names_both_types()
    {
        var de = Check(Vec2 + """

            fn main(): int {
                let a = Vec2 { x = 2 };
                let b = a * "three";
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0003");
        Assert.Contains("'Vec2' and 'string'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_single_argument_form_says_what_is_missing()
    {
        // The 2.x spelling. It is a type-argument count, and the message names the count rather
        // than leaving the reader to guess that the interface grew a parameter.
        var de = Check("""
            import std.core { Add };

            struct Money :: [Add<Money>] {
                cents: int,
                fn add(other: Money): Money { return Money { cents = this.cents + other.cents }; }
            }

            fn main(): int {
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics,
            d => d.Code == "LYR-SEM0026" && d.Message.Contains("2 type argument", StringComparison.Ordinal));
    }

    [Fact]
    public void A_constraint_picks_its_conformance_too()
    {
        var de = Check(Vec2 + """

            fn scale<T :: [Mul<int, T>]>(value: T, by: int): T {
                return value * by;
            }

            fn main(): int {
                return scale(Vec2 { x = 2 }, 3).x;
            }
            """);
        Assert.False(de.HasErrors);
    }

    [Fact]
    public void A_constraint_naming_the_other_conformance_refuses_the_operand()
    {
        // 'T :: [Mul<T, T>]' promises multiplication by a T and nothing else; an int operand has
        // no conformance to reach, even though the instantiating type happens to have one.
        var de = Check(Vec2 + """

            fn scale<T :: [Mul<T, T>]>(value: T, by: int): T {
                return value * by;
            }

            fn main(): int {
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0003");
    }
}
