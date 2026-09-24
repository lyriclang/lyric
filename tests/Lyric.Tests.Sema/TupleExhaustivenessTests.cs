using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Exhaustiveness through a TUPLE scrutinee (§7.6).
///
/// <para>There was no case for a tuple in the coverage computation at all, so it fell to the
/// default and demanded a <c>_</c> arm however completely the arms covered it. The pattern
/// compiler has known the form since 4.4; only the coverage did not.</para>
///
/// <para>DECIDED ONLY WHERE ONE COLUMN TESTS, and the second half of this file is why. With one
/// refutable column the others are wildcards in every row, so covering the tuple IS covering that
/// column — an exact reduction. With two it is not: <c>(A, true)</c> beside <c>(B, false)</c>
/// covers each column separately and leaves <c>(A, false)</c> open. Accepting that would be worse
/// than refusing it, because the lowering drops the last arm's test on an exhaustive match — a
/// missing case would silently run the previous arm's body.</para>
/// </summary>
public class TupleExhaustivenessTests
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

    private const string Enum = "enum E { A(int), B, }\n";

    /// <summary>The coverage gap's message, or <c>null</c> when the match is exhaustive.</summary>
    private static string? Gap(DiagnosticEngine de) =>
        de.Diagnostics.Where(d => d.Code == "LYR-SEM0050").Select(d => d.Message).FirstOrDefault();

    // ------------------------------------------------------------------ accepted

    /// <summary>The measured case: one testing column, the other a binding in every row.</summary>
    [Fact]
    public void One_testing_column_is_decided()
    {
        var de = Check(Enum + """
            fn f(e: E, m: int): int {
                match ((e, m)) {
                    (E.A(n), k) => { return n + k; }
                    (E.B, k) => { return k; }
                }
            }
            """);
        Assert.Null(Gap(de));
    }

    /// <summary>Wildcards throughout: the first row already matches everything.</summary>
    [Fact]
    public void All_wildcard_rows_need_no_default()
    {
        var de = Check(Enum + """
            fn f(e: E, m: int): int {
                match ((e, m)) { (x, k) => { return k; } }
            }
            """);
        Assert.Null(Gap(de));
    }

    /// <summary>A bool column counts as enumerable too, and covering it suffices.</summary>
    [Fact]
    public void A_bool_column_is_decided_the_same_way()
    {
        var de = Check("""
            fn f(b: bool, m: int): int {
                match ((b, m)) {
                    (true, k) => { return k; }
                    (false, k) => { return k + 1; }
                }
            }
            """);
        Assert.Null(Gap(de));
    }

    // ------------------------------------------------------------------ refused

    /// <summary>A real gap is still a gap, and the witness names it as a TUPLE.</summary>
    [Fact]
    public void A_missing_variant_is_reported_with_a_tuple_witness()
    {
        var de = Check(Enum + """
            fn f(e: E, m: int): int {
                match ((e, m)) { (E.A(n), k) => { return n + k; } }
            }
            """);
        var gap = Gap(de);
        Assert.NotNull(gap);
        Assert.Contains("(B, _)", gap!, StringComparison.Ordinal);
    }

    /// <summary>
    /// TWO testing columns keep the old answer. Not because the match is wrong — it is complete —
    /// but because deciding it needs the pattern matrix, and half of that would accept holes.
    /// </summary>
    [Fact]
    public void Two_testing_columns_still_ask_for_a_default()
    {
        var de = Check(Enum + """
            fn f(e: E, b: bool): int {
                match ((e, b)) {
                    (E.A(n), true) => { return n; }
                    (E.A(n), false) => { return 0; }
                    (E.B, true) => { return 1; }
                    (E.B, false) => { return 2; }
                }
            }
            """);
        Assert.NotNull(Gap(de));
    }

    /// <summary>And a default settles it, as it always did.</summary>
    [Fact]
    public void A_default_arm_covers_two_testing_columns()
    {
        var de = Check(Enum + """
            fn f(e: E, b: bool): int {
                match ((e, b)) {
                    (E.A(n), true) => { return n; }
                    _ => { return 1; }
                }
            }
            """);
        Assert.Null(Gap(de));
    }
}
