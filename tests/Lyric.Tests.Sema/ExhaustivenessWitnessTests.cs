using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// LYR-SEM0050 names the value that falls through (4.5): a WITNESS written the way a pattern is
/// written. "no arm matches 'Some(false)'" says what to add; "missing case(s): 'Some'" said only
/// that something about 'Some' was wrong, and for a nested payload not even that.
///
/// <para>The other half of the test is what must NOT be reported: a match the arms do cover —
/// including the length classes of an array — stays silent, or the new precision would be a new
/// false alarm.</para>
/// </summary>
public class ExhaustivenessWitnessTests
{
    private const string Prelude = """
        enum Opt<T> { Some(T), None, }
        enum Shape { Circle(int), Rect { w: int, h: int }, Empty, }
        enum Res<T, E> { Ok(T), Err(E), }
        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + "\n" + body);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        Semantics.Analyze(comp, binding, de);
        return de;
    }

    private static void AssertWitness(string body, string witness)
    {
        var de = Check(body);
        var d = Assert.Single(de.Diagnostics.Where(x => x.Code == "LYR-SEM0050"));
        Assert.Contains($"'{witness}'", d.Message, StringComparison.Ordinal);
    }

    private static void AssertExhaustive(string body)
    {
        var de = Check(body);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0050");
    }

    // --- the witness reaches into a payload ---

    [Fact]
    public void A_hole_inside_a_one_field_payload_is_named() =>
        AssertWitness("fn f(o: Opt<bool>): int { return match (o) { Some(true) => 1, None => 0 }; }",
            "Some(false)");

    [Fact]
    public void A_hole_two_levels_down_is_named() =>
        AssertWitness("fn f(r: Res<Opt<int>, string>): int { return match (r) { Ok(Some(_)) => 1, Err(_) => 0 }; }",
            "Ok(None)");

    [Fact]
    public void An_optional_payload_hole_is_named() =>
        AssertWitness("fn f(o: Opt<?int>): int { return match (o) { Some(null) => 1, None => 0 }; }",
            "Some(_)");

    // --- a variant nobody names, with its payload left open ---

    [Fact]
    public void A_struct_variant_is_written_with_braces() =>
        AssertWitness("fn f(s: Shape): int { return match (s) { Circle(_) => 0, Empty => 1 }; }",
            "Rect { … }");

    [Fact]
    public void A_tuple_variant_is_written_with_holes() =>
        AssertWitness("fn f(s: Shape): int { return match (s) { Rect { w, h } => w + h, Empty => 1 }; }",
            "Circle(_)");

    [Fact]
    public void A_missing_null_is_still_named() =>
        AssertWitness("fn f(s: ?Shape): int { return match (s) { Circle(_) => 0, Rect { w, h } => 1, Empty => 2 }; }",
            "null");

    // --- arrays: the length classes ---

    [Fact]
    public void The_smallest_uncovered_length_is_written_as_a_pattern() =>
        AssertWitness("fn f(xs: int[]): int { return match (xs) { [] => 0, [_, _] => 2 }; }", "[_]");

    [Fact]
    public void An_array_match_that_covers_every_length_is_silent() =>
        AssertExhaustive("fn f(xs: int[]): int { return match (xs) { [] => 0, [_] => 1, [_, _, ..] => 2 }; }");

    [Fact]
    public void A_bare_rest_covers_every_array() =>
        AssertExhaustive("fn f(xs: int[]): int { return match (xs) { [..] => 1 }; }");

    [Fact]
    public void A_tested_position_covers_no_length_class() =>
        AssertWitness("fn f(xs: int[]): int { return match (xs) { [] => 0, [0, _] => 1, [_] => 2 }; }", "[_, _]");

    // --- what must stay silent ---

    [Fact]
    public void A_covered_bool_payload_is_silent() =>
        AssertExhaustive("fn f(o: Opt<bool>): int { return match (o) { Some(true) => 1, Some(false) => 2, None => 0 }; }");

    [Fact]
    public void A_binding_arm_covers_a_payload() =>
        AssertExhaustive("fn f(o: Opt<bool>): int { return match (o) { Some(v) => 1, None => 0 }; }");

    [Fact]
    public void A_wildcard_still_covers_everything() =>
        AssertExhaustive("fn f(s: Shape): int { return match (s) { Circle(_) => 0, _ => 1 }; }");

    [Fact]
    public void A_variant_with_several_fields_is_covered_by_its_binding_form() =>
        AssertExhaustive("fn f(s: Shape): int { return match (s) { Circle(_) => 0, Rect { w, h } => 1, Empty => 2 }; }");
}
