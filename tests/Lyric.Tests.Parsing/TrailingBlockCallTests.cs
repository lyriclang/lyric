using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// A trailing block joins the call it stands behind as that call was written
/// (design/v5/spec/08 Y11 F1, 04 D5). The block built the call anew, and what is kept beside the
/// arguments — the names they were written with, the parentheses of their own — was gone:
/// <c>apply(b: 1, a: 10) { … }</c> was <c>apply(1, 10) { … }</c> to everything after the parser.
/// </summary>
public class TrailingBlockCallTests
{
    private static CallExpr Call(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var expr = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseExpression();
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<CallExpr>(expr);
    }

    [Fact]
    public void The_names_of_the_arguments_stay()
    {
        var call = Call("apply(b: 1, a: 10) { it * 2 }");
        Assert.Equal(3, call.Arguments.Length);
        Assert.Equal(new string?[] { "b", "a" }, call.ArgumentNames!);
        Assert.IsType<LambdaExpr>(call.Arguments[2]);
    }

    [Fact]
    public void The_parentheses_of_an_argument_stay()
    {
        var call = Call("show((1 + 2)) { it }");
        var (argument, written) = Assert.Single(call.Parenthesized!);
        Assert.Same(call.Arguments[0], argument);
        Assert.Equal("(1 + 2)", "show((1 + 2)) { it }"[written.Start..written.End]);
    }

    [Fact]
    public void The_type_arguments_stay() =>
        Assert.Single(Call("fold<int>(0) { it }").TypeArguments!);

    /// <summary>The control: a call that wrote neither has neither.</summary>
    [Fact]
    public void A_call_that_wrote_neither_has_neither()
    {
        var call = Call("fold(0) { it }");
        Assert.Null(call.ArgumentNames);
        Assert.Null(call.Parenthesized);
        Assert.Equal(2, call.Arguments.Length);
    }
}
