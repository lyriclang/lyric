using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The call says whether an array is one argument or the rest (design/v5/spec/08; the review's
/// A2). <c>f(xs)</c> hands a variadic parameter the array as ONE element — a type error where the
/// element is no array, with the hint — and <c>f(xs...)</c> spreads it: the rest, alone, of the
/// parameter's array type, at a variadic parameter (<c>LYR-SEM0166</c> otherwise). The implicit
/// passing-through of a single array of the parameter's own type is gone.
/// </summary>
public class SpreadTests
{
    private const string Head = """
        fn sum(base: int, xs: int...): int { return base + xs.length(); }
        fn all(xs: int...): int { return xs.length(); }
        fn first<T>(xs: T...): T { return xs[0]; }
        fn plain(xs: int[]): int { return xs.length(); }

        interface Tally { fn total(xs: int...): int; }
        struct Counter :: [Tally] { fn total(xs: int...): int { return xs.length(); } }

        fn use(ys: int[], zs: string[]): int {

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Head + body + "\n}\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static Diagnostic One(string body, string code)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 1, string.Join("\n", de.Diagnostics));
        Assert.Equal(code, de.Diagnostics[0].Code);
        return de.Diagnostics[0];
    }

    [Theory]
    [InlineData("    return all(ys...);")]
    [InlineData("    return sum(1, ys...);")]
    [InlineData("    return all([1, 2]...);")]
    [InlineData("    return all() + all(1) + all(1, 2);")]
    [InlineData("    return sum(base: 1);")]
    public void A_spread_array_and_single_elements_are_silent(string body)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    [Theory]
    [InlineData("    return all(ys);")]
    [InlineData("    return sum(1, ys);")]
    public void An_array_is_one_argument_and_the_message_says_what_spreads_it(string body)
    {
        var error = One(body, "LYR-SEM0001");
        Assert.Contains("'int[]' is one argument here", error.Message);
        Assert.Contains("'...'", Assert.Single(error.Notes!).Message);
    }

    /// <summary>The literal is an array like any other: one element. Its shape decided once.</summary>
    [Fact]
    public void An_array_literal_is_one_argument_too() => One("    return all([1, 2]);", "LYR-SEM0001");

    /// <summary>Where the element takes an array, the array is the element — and the spread the
    /// elements. The type parameter is bound accordingly.</summary>
    [Fact]
    public void A_type_parameter_is_bound_by_what_the_call_says()
    {
        var de = Check("    let whole: int[] = first(ys);\n    let one: int = first(ys...);\n    return whole.length() + one;");
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void A_spread_goes_to_a_variadic_parameter()
    {
        Assert.Contains("'plain' has none", One("    return plain(ys...);", "LYR-SEM0166").Message);
        Assert.Contains("a function value", One("    let f = plain;\n    return f(ys...);", "LYR-SEM0166").Message);
    }

    [Fact]
    public void A_spread_is_the_rest_and_alone_in_it()
    {
        Assert.Contains("goes to 'base'", One("    return sum(ys...);", "LYR-SEM0166").Message);
        Assert.Contains("the whole rest", One("    return sum(0, 1, ys...);", "LYR-SEM0166").Message);
        Assert.Contains("the whole rest", One("    return all(ys..., 4);", "LYR-SEM0166").Message);
    }

    /// <summary>One message for two spreads, on the second one's dots — and none about either
    /// argument, which the parser left its dots.</summary>
    [Fact]
    public void A_call_spreads_one_array()
    {
        var error = One("    return all(ys..., ys...);", "LYR-SEM0166");
        Assert.Contains("this is a second", error.Message);
        Assert.Equal(3, error.Span.End - error.Span.Start);
    }

    [Fact]
    public void A_spread_array_is_of_the_parameters_type() =>
        Assert.Contains("'string[]'", One("    return all(zs...);", "LYR-SEM0001").Message);

    /// <summary>A member call and the qualified form of it (04 D2 R5) are one call: the call the
    /// checker writes for 'Tally.total(c, ys...)' keeps the spread. It was built anew and had
    /// lost it — the array was one argument there.</summary>
    [Theory]
    [InlineData("    let c = Counter { };\n    return c.total(ys...);")]
    [InlineData("    let c = Counter { };\n    return Tally.total(c, ys...);")]
    public void A_method_takes_a_spread_in_both_forms_of_its_call(string body)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    /// <summary>A block behind the call is a second argument in the rest (and, being no 'int',
    /// has an error of its own beside this one).</summary>
    [Fact]
    public void A_trailing_block_beside_a_spread_is_refused() =>
        Assert.Contains(Check("    return all(ys...) { it };").Diagnostics,
            d => d.Code == "LYR-SEM0166" && d.Message.Contains("the whole rest"));

    /// <summary>The dots point at themselves: the error stands on them, not on the argument.</summary>
    [Fact]
    public void The_refusal_stands_on_the_dots()
    {
        var error = One("    return plain(ys...);", "LYR-SEM0166");
        Assert.Equal(3, error.Span.End - error.Span.Start);
    }
}
