using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The two primitives of an array in Lyric 5 (design/v5/spec/03 T13 A1; T14 N4, N6; 10 N8):
/// <c>length()</c>, a call with its parentheses like every length, and the index, an <c>int</c>
/// — narrower integers widen to it, a <c>uint</c> is converted — with <c>^n</c> counting from
/// the end inside the brackets and nowhere else. <c>[x] * n</c> repeats in that order only.
/// </summary>
public class ArrayIndexTests
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

    private static void Allowed(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string source, string code)
    {
        var errors = Check(source).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ length()

    [Fact]
    public void The_length_is_a_call() =>
        Allowed("fn f(xs: int[], grid: int[][]): int { return xs.length() + grid[0].length(); }");

    [Fact]
    public void The_length_without_its_parentheses_is_refused() =>
        Assert.Contains("write 'length()'", Rejected("fn f(xs: int[]): int { return xs.length; }", "LYR-SEM0012"));

    // ------------------------------------------------------------------ the index

    [Fact]
    public void A_narrower_integer_widens_to_the_index() =>
        Allowed("fn f(xs: int[], i: int8, j: uint16, k: int32): int { return xs[i] + xs[j] + xs[k] + xs[0]; }");

    [Fact]
    public void A_uint_index_is_converted_with_as()
    {
        Assert.Contains("'as int'", Rejected("fn f(xs: int[], i: uint): int { return xs[i]; }", "LYR-SEM0007"));
        Allowed("fn f(xs: int[], i: uint): int { return xs[i as int]; }");
    }

    [Fact]
    public void A_non_integer_index_is_refused() =>
        Assert.Contains("'float'", Rejected("fn f(xs: int[]): int { return xs[1.5]; }", "LYR-SEM0007"));

    // ------------------------------------------------------------------ ^n

    [Fact]
    public void From_end_counts_inside_the_brackets() =>
        Allowed("""
            fn f(xs: int[], n: int8): int {
                xs[^1] = xs[^2] + xs[^n];
                return xs[^(n as int + 1)];
            }
            """);

    [Fact]
    public void From_end_outside_the_brackets_is_refused() =>
        Assert.Contains("inside '[…]' only", Rejected("fn f(xs: int[]): int { let i = ^1; return xs[i]; }", "LYR-SEM0114"));

    [Fact]
    public void From_end_as_part_of_a_larger_index_is_refused() =>
        Rejected("fn f(xs: int[]): int { return xs[^1 + 1]; }", "LYR-SEM0114");

    [Fact]
    public void From_end_needs_a_value_with_a_length()
    {
        // Beside the refusal of the indexing itself: two rules, two messages.
        var de = Check("struct P { x: int }\nfn f(p: P): int { return p[^1]; }");
        var d = Assert.Single(de.Diagnostics, x => x.Code == "LYR-SEM0114");
        Assert.Contains("'P' has none", d.Message);
    }

    [Fact]
    public void Exclusive_or_between_operands_is_still_the_operator() =>
        Allowed("fn f(a: int, b: int, xs: int[]): int { return (a ^ b) + xs[a ^ b]; }");

    // ------------------------------------------------------------------ [x] * n

    [Fact]
    public void Repetition_takes_the_array_on_the_left()
    {
        Allowed("fn f(): int[] { return [0] * 5; }");
        Rejected("fn f(): int[] { return 5 * [0]; }", "LYR-SEM0003");
    }
}
