using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A lambda's place parameter (design/v5/spec/03 T12; the review's M6-24): <c>(&amp;n) =&gt; …</c>
/// and <c>{ &amp;n =&gt; … }</c> take a place, and the lambda's type is <c>fn(&amp;T) -&gt; R</c>.
/// The mark is the lambda's to write, as <c>&amp;x</c> is the call's (<c>LYR-SEM0157</c> where
/// it and the position disagree); the place is the call's — no closure in the body holds it,
/// and a generator lambda takes none (<c>LYR-SEM0158</c>).
/// </summary>
public class LambdaPlaceTests
{
    private const string Head = """
        fn visit(&x: int, f: fn(&int) -> void): void { f(&x); }
        fn twice(x: int, f: fn(int) -> int): int { return f(f(x)); }
        fn later(g: fn() -> void): void { g(); }

        fn use(): int {
            var n = 1;

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Head + body + "\n    return n;\n}\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Silent(string body)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    private static Diagnostic One(string body, string code)
    {
        var de = Check(body);
        Assert.True(de.Diagnostics.Count == 1, string.Join("\n", de.Diagnostics));
        Assert.Equal(code, de.Diagnostics[0].Code);
        return de.Diagnostics[0];
    }

    [Theory]
    [InlineData("    visit(&n) { &v => v += 1; };")]
    [InlineData("    visit(&n, (&v) => { v = v * 2; });")]
    [InlineData("    visit(&n, (&v: int) => { v = 7; });")]
    [InlineData("    let bump: fn(&int) -> void = (&v) => { v += 1; };\n    bump(&n);")]
    [InlineData("    let bump = (&v: int) => { v += 1; };\n    bump(&n);")]
    [InlineData("    let add = (&total: int, by: int) => { total += by; };\n    add(&n, 5);")]
    public void A_lambda_takes_a_place_and_writes_through_it(string body) => Silent(body);

    /// <summary>The type a lambda with a marked parameter has: one that takes a place.</summary>
    [Fact]
    public void Its_type_takes_a_place()
    {
        var error = One("    let bump = (&v: int) => { v += 1; };\n    let plain: fn(int) -> void = bump;", "LYR-SEM0001");
        Assert.Contains("'fn(&int) -> void'", error.Message);
    }

    [Fact]
    public void A_parameter_handed_a_place_is_marked()
    {
        var error = One("    visit(&n) { v => v += 1; };", "LYR-SEM0157");
        Assert.Contains("'v' is handed a place: write '&v'", error.Message);
    }

    /// <summary>The implicit 'it' cannot be marked: the block names its parameter.</summary>
    [Fact]
    public void The_implicit_it_takes_no_place()
    {
        var error = One("    visit(&n) { it += 1; };", "LYR-SEM0157");
        Assert.Contains("name the parameter and mark it", error.Message);
    }

    [Fact]
    public void A_mark_where_a_value_is_handed_over_is_refused()
    {
        var error = One("    n = twice(n) { &v => v + 1 };", "LYR-SEM0157");
        Assert.Contains("'&v' takes a place, and the function expected here is handed a value", error.Message);
    }

    /// <summary>The place is the call's: a closure in the body would hold it past the call.</summary>
    [Fact]
    public void No_closure_in_the_body_holds_the_place()
    {
        var error = One("    visit(&n) { &v => later(() => { v = 0; }); };", "LYR-SEM0158");
        Assert.Contains("'v' is a place parameter", error.Message);
    }

    /// <summary>A generator's body runs after the call that handed the place over has returned.</summary>
    [Fact]
    public void A_generator_lambda_takes_no_place()
    {
        var de = Check("    let g = (&v: int) => { yield v; };");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0158" && d.Message.Contains("this lambda is a generator"));
    }

    /// <summary>Without a position and without a type, a marked parameter needs its type like
    /// any other.</summary>
    [Fact]
    public void A_place_parameter_without_a_type_needs_one() =>
        One("    let f = (&v) => v;", "LYR-SEM0045");
}
