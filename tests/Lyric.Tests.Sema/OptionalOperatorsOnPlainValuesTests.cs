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
/// <para>THE TYPE PARAMETER IS EXEMPT and the second half of this file is why. In
/// <c>fn f&lt;T&gt;(x: T)</c> the instantiation answers the question and the declaration cannot;
/// a <c>T</c> bound to <c>?int</c> makes all three ordinary optional operations, measured and
/// correct. Refusing them here would refuse bodies that work. The lowering keeps its check for
/// what the substitution leaves non-optional — see the IR suite, where it is now a backstop
/// rather than the rule.</para>
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
    /// A TYPE PARAMETER keeps all three. The declaration cannot answer the question; only the
    /// instantiation can, and this body is correct for every <c>T</c> that is optional.
    /// </summary>
    [Theory]
    [InlineData("if (x == null) { return 1; } return 0;")]
    [InlineData("let y: T = x ?? x; return 0;")]
    [InlineData("var z = x; z ??= x; return 0;")]
    public void A_type_parameter_is_exempt(string body)
    {
        var de = Check($$"""
            fn probe<T>(x: T): int {
                {{body}}
            }
            fn main(): int { let o: ?int = 1; return probe<?int>(o); }
            """);

        Assert.False(de.HasErrors, Codes(de));
    }

    /// <summary>
    /// And the exemption is not a hole: the same body instantiated at a NON-optional is still
    /// refused, one phase later, because that is the first moment anything can tell.
    /// </summary>
    [Fact]
    public void The_exemption_ends_where_the_instantiation_answers_it()
    {
        var de = Check("""
            fn probe<T>(x: T): int { if (x == null) { return 1; } return 0; }
            fn main(): int { return probe<int>(5); }
            """);

        // The SEMA says nothing — that is the exemption. The refusal is the lowering's, and the
        // IR suite pins it; asserting it here would only re-test the other project's subject.
        Assert.False(de.HasErrors, Codes(de));
    }
}
