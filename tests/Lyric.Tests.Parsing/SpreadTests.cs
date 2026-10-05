using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// <c>f(xs...)</c> (design/v5/spec/08; the review's A2): dots behind an argument of a call
/// spread it over the variadic parameter. The call remembers which argument and where the dots
/// stand — wherever they stand: that a call spreads one argument, the rest, is the checker's
/// to say (<c>LYR-SEM0166</c>), which knows the parameters, and says it once.
/// </summary>
public class SpreadTests
{
    private static (Expr Expr, DiagnosticEngine De) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression(), de);
    }

    private static CallExpr Call(string source)
    {
        var (expr, de) = Parse(source);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<CallExpr>(expr);
    }

    [Theory]
    [InlineData("f(xs...)", 0, 1)]
    [InlineData("f(a, b, xs...)", 2, 3)]
    [InlineData("f(a, make(b)...)", 1, 2)]
    [InlineData("f([1, 2]...)", 0, 1)]
    [InlineData("f<int>(a, xs...)", 1, 2)]
    [InlineData("f(xs...,)", 0, 1)]
    [InlineData("f(a, 1...)", 1, 2)]
    public void The_call_remembers_the_argument_it_spreads(string source, int at, int count)
    {
        var call = Call(source);
        Assert.Equal(count, call.Arguments.Length);
        var (argument, dots) = Assert.Single(call.Spreads!);
        Assert.Same(call.Arguments[at], argument);
        Assert.Equal("...", source[dots.Start..dots.End]);
    }

    [Theory]
    [InlineData("f(xs)")]
    [InlineData("f(a..b)")]
    [InlineData("f()")]
    public void A_call_without_dots_spreads_nothing(string source) => Assert.Null(Call(source).Spreads);

    /// <summary>A trailing block follows the parentheses, and the spread stays the argument in
    /// them (what a variadic function with a block would mean is the checker's to say).</summary>
    [Fact]
    public void A_trailing_block_leaves_the_spread_where_it_is()
    {
        var call = Call("f(xs...) { it }");
        Assert.Equal(2, call.Arguments.Length);
        Assert.Same(call.Arguments[0], Assert.Single(call.Spreads!).Argument);
    }

    /// <summary>The parser notes the dots and refuses none: a diagnostic here would be followed
    /// by the checker's about the same argument, which then has no dots.</summary>
    [Theory]
    [InlineData("f(xs..., 4)", 1)]
    [InlineData("f(xs..., ys...)", 2)]
    public void Dots_are_noted_wherever_they_stand(string source, int count)
    {
        var call = Call(source);
        Assert.Equal(2, call.Arguments.Length);
        Assert.Equal(count, call.Spreads!.Length);
        Assert.Same(call.Arguments[0], call.Spreads[0].Argument);
    }

    /// <summary>An attribute's arguments are no call's: dots there are not read.</summary>
    [Fact]
    public void An_attribute_takes_no_spread()
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        new Parser(sm, sm.AddVirtual("test.lyr", "@Retry(xs...)\nfn f(): void { }\n"), de).ParseModule();
        Assert.True(de.HasErrors);
    }
}
