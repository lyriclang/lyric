using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// What overloading refuses (v3.0). The feature is the second mechanism this language has for
/// "one name, several types", and it was admitted knowing that; what keeps it from becoming a
/// place where nobody can predict the answer is the set of rules below.
/// </summary>
public class OverloadTests
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

    [Fact]
    public void Two_with_the_same_parameters_are_a_redeclaration()
    {
        // Even though the results differ: a call site cannot choose by what it gets back, so a
        // rule that let it would be one nobody could hold in their head.
        var de = Check("""
            fn same(n: int): int { return n; }
            fn same(n: int): string { return "x"; }

            fn main(): int { return 0; }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0085");
        Assert.Contains("what they TAKE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_interface_member_may_not_be_overloaded()
    {
        // The structural reason: a method table holds one function per slot and finds it by name.
        var de = Check("""
            interface Shape {
                fn area(): int;
                fn area(scale: int): int;
            }

            fn main(): int { return 0; }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0088");
        Assert.Contains("one function per slot", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_no_candidate_takes_names_them_all()
    {
        var de = Check("""
            fn code(n: int): int { return 1; }
            fn code(s: string): int { return 2; }

            fn main(): int { return code(true); }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0087");
        Assert.Contains("(bool)", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Notes);
        Assert.Equal(2, error.Notes!.Count);
    }

    [Fact]
    public void A_call_two_candidates_fit_equally_is_ambiguous()
    {
        // Two type parameters take the argument equally well, and nothing separates them.
        var de = Check("""
            fn pick<T>(a: T, b: int): int { return 1; }
            fn pick<U>(a: int, b: U): int { return 2; }

            fn main(): int { return pick(1, 2); }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0086");
        Assert.Contains("ambiguous", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_without_a_type_to_pick_by_is_refused()
    {
        var de = Check("""
            fn step(n: int): int { return n; }
            fn step(s: string): int { return 0; }

            fn main(): int {
                let g = step;
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0089");
        Assert.Contains("names 2 functions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_beside_a_type_of_one_name_is_still_a_collision()
    {
        // Only FUNCTIONS may share a name: they are told apart by their parameters, and a type
        // has none.
        var de = Check("""
            fn thing(): int { return 0; }
            struct thing { x: int, }

            fn main(): int { return 0; }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-RES0001");
    }

    [Fact]
    public void Overloading_across_two_scopes_does_not_happen()
    {
        // An inner declaration hides an outer one whole, as it always did. Anything else would
        // make a local shadow depend on the argument types at every call.
        var de = Check("""
            fn f(s: string): int { return 1; }

            fn main(): int {
                let f = 7;
                return f;
            }
            """);
        Assert.False(de.HasErrors);
    }
}
