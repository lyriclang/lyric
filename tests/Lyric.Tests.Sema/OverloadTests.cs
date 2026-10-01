using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Overloading by arity and nothing else (design/v5/spec/04 D4): one name may take several
/// argument COUNTS; two declarations whose counts overlap are a redeclaration, at the
/// declaration and never at a call. A call counts its arguments and finds one candidate.
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
    public void Two_of_one_count_are_a_redeclaration()
    {
        // Different parameter types are no distinction: a call chooses by how many arguments it
        // passes, never by their types (D4) — one candidate, no ranking.
        var de = Check("""
            fn same(n: int): int { return n; }
            fn same(s: string): int { return 0; }

            fn main(): int { return 0; }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0085");
        Assert.Contains("how many arguments", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_that_overlap_through_a_default_are_a_redeclaration()
    {
        // 'f(a)' and 'f(a, b = 0)' both take one argument: the error stands at the declaration,
        // where the overlap is, not at a call that happens to pass one.
        var de = Check("""
            fn f(a: int): int { return a; }
            fn f(a: int, b: int = 0): int { return a + b; }

            fn main(): int { return 0; }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0085");
        Assert.Contains("taking 1 argument", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_that_differ_are_one_name()
    {
        var de = Check("""
            fn of(hex: int): int { return hex; }
            fn of(r: int, g: int, b: int): int { return r + g + b; }
            fn pad(s: string, width: int = 4): int { return width; }
            fn pad(s: string, width: int, left: bool, fill: string): int { return width; }

            fn main(): int { return of(1) + of(1, 2, 3) + pad("x") + pad("x", 1, true, " "); }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
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
            fn code(a: int, b: int, c: int): int { return 2; }

            fn main(): int { return code(1, 2); }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0087");
        Assert.Contains("takes 2 argument", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Notes);
        Assert.Equal(2, error.Notes!.Count);
    }

    [Fact]
    public void Type_parameters_separate_nothing_either()
    {
        // 4.x ranked 'pick<T>(a: T, b: int)' against 'pick<U>(a: int, b: U)' and found the call
        // ambiguous; the two take two arguments each, and that is the whole question now.
        var de = Check("""
            fn pick<T>(a: T, b: int): int { return 1; }
            fn pick<U>(a: int, b: U): int { return 2; }

            fn main(): int { return 0; }
            """);
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0085");
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
        // Only FUNCTIONS may share a name: they are told apart by how many arguments they take,
        // and a type takes none.
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

    [Fact]
    public void A_module_qualified_call_binds_through_the_alias()
    {
        // The alias route and the selective import reach the same member. (Through 4.x the UDP
        // 'localPort'/'close' were the TCP names' second members by type; D4 gave them names.)
        var de = Check("""
            import std.io.net as net;

            fn main(): int {
                let sock = net.bind("127.0.0.1", 0);
                if (sock == null) {
                    return 1;
                }
                let p = net.udpLocalPort(sock);
                net.closeUdp(sock);
                return if (p > 0) 0 else 1;
            }
            """);
        Assert.False(de.HasErrors);
    }
}
