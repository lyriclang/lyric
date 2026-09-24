using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Four findings of the diagnostics round, each about a message rather than a rule.
///
/// <para>They share a shape worth naming: none of them changed what compiles, and all four cost a
/// reader time. A suggestion that cannot be followed, an error whose consequences are reported as
/// three more errors, an ambiguity between a function and itself, and a declaration that was
/// accepted and then ignored.</para>
/// </summary>
public class DiagnosticQualityTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source, bool withStdlib = false)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        if (withStdlib)
            comp.ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static string MessageOf(DiagnosticEngine de, string code) =>
        de.Diagnostics.Where(d => d.Code == code).Select(d => d.Message).FirstOrDefault() ?? "";

    // ---------------------------------------------------------- a type where a value belongs

    /// <summary>
    /// <c>LYR-SEM0052</c> suggested <c>'X { … }'</c> for every type symbol there is.
    ///
    /// <para>Only a struct or a class can take that advice. An enum is built through a variant, an
    /// interface is not built at all, and <c>let i = int;</c> was answered with "did you mean
    /// 'int { … }'?" — a form the language does not have. A suggestion costs the reader the time
    /// it takes to try it, which is why a wrong one is worse than none.</para>
    /// </summary>
    [Fact]
    public void A_class_still_gets_the_brace_form()
    {
        var de = Check("""
            class C { n: int }
            fn main(): int { let c = C; return 0; }
            """);

        Assert.Contains("C { … }", MessageOf(de, "LYR-SEM0052"), StringComparison.Ordinal);
    }

    /// <summary>An enum is reached through a variant, and the message names one that exists.</summary>
    [Fact]
    public void An_enum_is_pointed_at_a_variant()
    {
        var de = Check("""
            enum Shape { Round, Square, }
            fn main(): int { let s = Shape; return 0; }
            """);

        var message = MessageOf(de, "LYR-SEM0052");
        Assert.Contains("Shape.Round", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Shape { … }", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An interface, an alias and a builtin get NO suggestion. Each of them used to get one that
    /// cannot be written.
    /// </summary>
    [Theory]
    [InlineData("interface Named { fn name(): string; }", "Named")]
    [InlineData("type Alias = int;", "Alias")]
    [InlineData("", "int")]
    public void What_cannot_be_built_is_not_suggested(string declaration, string name)
    {
        var de = Check($$"""
            {{declaration}}
            fn main(): int { let x = {{name}}; return 0; }
            """);

        var message = MessageOf(de, "LYR-SEM0052");
        Assert.Contains("not a value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("did you mean", message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------- a destructuring that failed

    /// <summary>
    /// A failed destructuring binds its names anyway, at the error type.
    ///
    /// <para>Without it the names do not exist, and every use of them is a second diagnostic about
    /// the first one's consequence: <c>let (a, b) = 5;</c> answered <c>LYR-SEM0058</c> and then
    /// "unknown identifier 'a'" and "unknown identifier 'b'". Of the three, one was the
    /// mistake.</para>
    /// </summary>
    [Fact]
    public void A_failed_destructuring_does_not_make_its_names_unknown()
    {
        var de = Check("""
            fn main(): int {
                let (a, b) = 5;
                return a + b;
            }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0058");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0002");
    }

    /// <summary>The arity mismatch is the same story and had the same ending.</summary>
    [Fact]
    public void A_destructuring_of_the_wrong_arity_does_not_either()
    {
        var de = Check("""
            fn pair(): (int, int) { return (1, 2); }
            fn main(): int {
                let (a, b, c) = pair();
                return a + b + c;
            }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0058");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0002");
    }

    /// <summary>The control: a destructuring that WORKS still binds what it says it binds.</summary>
    [Fact]
    public void A_good_destructuring_is_unaffected()
    {
        var de = Check("""
            fn pair(): (int, int) { return (1, 2); }
            fn main(): int {
                let (a, b) = pair();
                return a + b;
            }
            """);

        Assert.False(de.HasErrors);
    }

    // ---------------------------------------------------------- a function against itself

    /// <summary>
    /// Importing one name twice made a call AMBIGUOUS between a function and itself.
    ///
    /// <para>Two copies of one symbol fit each other exactly by construction, so the overload set
    /// could not separate them — and the two notes under the message pointed at the same file,
    /// line and column. The set is deduplicated now; where a name arrives by two routes, none of
    /// them is the wrong one, and counting the function twice is what was wrong.</para>
    /// </summary>
    [Fact]
    public void A_name_imported_twice_is_one_function()
    {
        var de = Check("""
            import std.io.console { println };
            import std.io.console { println };

            fn main(): int { println("x"); return 0; }
            """, withStdlib: true);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0086");
    }

    /// <summary>
    /// The control: a REAL ambiguity is still one. The deduplication must not make two different
    /// functions look like one.
    /// </summary>
    [Fact]
    public void Two_different_functions_are_still_ambiguous()
    {
        var de = Check("""
            fn take(x: int, y: int64): int { return 1; }
            fn take(x: int64, y: int): int { return 2; }
            fn main(): int { return take(1, 1); }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code is "LYR-SEM0086" or "LYR-SEM0087");
    }

    // ---------------------------------------------------------- a parameter declared twice

    /// <summary>
    /// <c>fn f(x: int, x: int)</c> compiled, and the argument written for the second parameter
    /// went nowhere: the first won every lookup, so <c>f(1, 2)</c> returning <c>x</c> answered 1.
    ///
    /// <para>A parameter list is a scope like a module or a type body — the one scope nobody had
    /// asked about. A second binding of a name can only replace the first or be unreachable, and
    /// both readings silently discard an argument the caller wrote out.</para>
    /// </summary>
    [Fact]
    public void A_parameter_name_binds_once()
    {
        var de = Check("fn f(x: int, x: int): int { return x; }\nfn main(): int { return f(1, 2); }");

        var diagnostic = Assert.Single(de.Diagnostics, d => d.Code == "LYR-RES0001");
        Assert.Contains("parameter list", diagnostic.Message, StringComparison.Ordinal);
        Assert.NotNull(diagnostic.Notes);
    }

    /// <summary>A lambda's parameter list is one too.</summary>
    [Fact]
    public void A_lambdas_parameters_are_a_list_as_well()
    {
        var de = Check("""
            fn apply(f: fn(int, int) -> int): int { return f(1, 2); }
            fn main(): int { return apply((x: int, x: int): int => x); }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-RES0001");
    }

    /// <summary>
    /// The control: two parameters of DIFFERENT names are what every function has, and <c>_</c> is
    /// the deliberate non-name — it may repeat, as it does in a pattern.
    /// </summary>
    [Fact]
    public void Distinct_names_and_repeated_underscores_are_fine()
    {
        var de = Check("""
            fn f(x: int, y: int): int { return x + y; }
            fn g(_: int, _: int): int { return 0; }
            fn main(): int { return f(1, 2) + g(3, 4); }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-RES0001");
    }
}
