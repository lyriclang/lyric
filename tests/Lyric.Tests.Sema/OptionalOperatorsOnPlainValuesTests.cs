using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// An optional-shaped operation on a value that is never null (§6.2, §6.3).
///
/// <para>Three forms, one mistake: <c>x == null</c>, <c>x ?? y</c> and <c>x ??= y</c> on something
/// that carries no absence. All three were refused by the LOWERING as <c>LYR-IR0001</c> — a code
/// §12.1 reserves for valid Lyric this implementation cannot carry, and these are not that: no
/// future release will run them, because there is nothing to run. The note under them said "this
/// compiler version cannot lower it yet", which sent a reader to wait for a release.</para>
///
/// <para>They are checker errors now, each joining the code its family already had:
/// <c>LYR-SEM0059</c> is the equality family (and the expression twin of <c>LYR-SEM0029</c>, which
/// has refused the <c>null</c> PATTERN against a non-optional all along), and <c>LYR-SEM0005</c>
/// is the force-unwrap they sit beside. No new code, because neither is a new rule.</para>
///
/// <para>A BARE TYPE PARAMETER IS OPAQUE, since Lyric 5 (design/v5/spec/03 T4 O2): the three
/// forms are refused on a <c>T</c> as on an <c>int</c>, and asked of a <c>?T</c>. Lyric 4
/// exempted the type parameter here — "the instantiation answers the question" — while the
/// force-unwrap and the <c>null</c> pattern refused it; with <c>??T</c> a type (O1) the
/// exemption has no reading left: a generic <c>?T</c> at <c>T = ?int</c> is <c>??int</c>, and
/// the body asks about its own level.</para>
/// </summary>
public class OptionalOperatorsOnPlainValuesTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static string Codes(DiagnosticEngine de) =>
        string.Join(", ", de.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code));

    // ------------------------------------------------------------------ refused

    [Theory]
    [InlineData("if (x == null) { return 1; }")]
    [InlineData("if (x != null) { return 1; }")]
    [InlineData("if (null == x) { return 1; }")]
    public void A_null_test_on_a_value_that_is_never_null_is_refused(string statement)
    {
        var de = Check($$"""
            fn main(): int {
                let x: int = 5;
                {{statement}}
                return 0;
            }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0059");
    }

    [Fact]
    public void Coalescing_on_a_value_that_is_never_null_is_refused()
    {
        var de = Check("""
            fn main(): int { let x: int = 5; return x ?? 0; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0005");
    }

    [Fact]
    public void Coalescing_assignment_to_such_a_target_is_refused()
    {
        var de = Check("""
            fn main(): int { var x: int = 5; x ??= 0; return x; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0005");
    }

    /// <summary>A class reference is not an optional either — the rule is not about scalars.</summary>
    [Fact]
    public void A_reference_is_not_optional_either()
    {
        var de = Check("""
            class C { n: int }
            fn main(): int { let c = C { n = 1 }; if (c == null) { return 1; } return 0; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0059");
    }

    // ------------------------------------------------------------------ still accepted

    [Fact]
    public void An_optional_still_compares_against_null()
    {
        var de = Check("""
            fn main(): int { let o: ?int = null; if (o == null) { return 1; } return 0; }
            """);

        Assert.False(de.HasErrors, Codes(de));
    }

    [Fact]
    public void An_optional_still_coalesces()
    {
        var de = Check("""
            fn main(): int { let o: ?int = null; return o ?? 3; }
            """);

        Assert.False(de.HasErrors, Codes(de));
    }

    /// <summary>
    /// A bare type parameter is opaque (O2): none of the three is asked of a <c>T</c>, whatever
    /// an instantiation binds it to. The refusal stands at the declaration, where the body is
    /// checked — not one phase later at the instantiation that happens to be non-optional.
    /// </summary>
    [Theory]
    [InlineData("if (x == null) { return 1; } return 0;", "LYR-SEM0059")]
    [InlineData("let y: T = x ?? x; return 0;", "LYR-SEM0005")]
    [InlineData("var z = x; z ??= x; return 0;", "LYR-SEM0005")]
    public void A_bare_type_parameter_is_opaque(string body, string code)
    {
        var de = Check($$"""
            fn probe<T>(x: T): int {
                {{body}}
            }
            fn main(): int { let o: ?int = 1; return probe<?int>(o); }
            """);

        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal(code, error.Code);
        Assert.Contains("bare type parameter is opaque", error.Message);
    }

    /// <summary>The same three on a <c>?T</c>: the body's own level, which every instantiation
    /// has — at <c>T = ?int</c> the parameter is a <c>??int</c> and the test asks about the
    /// outer one.</summary>
    [Theory]
    [InlineData("if (x == null) { return 1; } return 0;")]
    [InlineData("let y: T = x ?? fallback; return 0;")]
    [InlineData("var z = x; z ??= fallback; return 0;")]
    public void An_optional_of_a_type_parameter_is_asked(string body)
    {
        var de = Check($$"""
            fn probe<T>(x: ?T, fallback: T): int {
                {{body}}
            }
            fn main(): int { let o: ?int = 1; return probe<?int>(o, o) + probe<int>(3, 4); }
            """);

        Assert.False(de.HasErrors, Codes(de));
    }
}
