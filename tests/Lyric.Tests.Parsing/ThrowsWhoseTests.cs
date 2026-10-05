using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Whose a <c>throws</c> is behind a return type that could take it itself (design/v5/spec/03 T17;
/// the review's M5-7, M6-6). A function type as a return type — of a declaration, of a lambda, of
/// another function type — does not take the set behind it in silence any more: parentheses say
/// whose it is (<c>LYR-PAR0056</c>). And a return type's parentheses are remembered, because for a
/// task or a coroutine only the checker knows that they were needed.
/// </summary>
public class ThrowsWhoseTests
{
    private static (Module Module, DiagnosticEngine De, string Source) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseModule(), de, source);
    }

    private static Module Clean(string source)
    {
        var (module, de, _) = Parse(source);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return module;
    }

    private static IEnumerable<Node> Nodes(Node root)
    {
        yield return root;
        foreach (var child in AstChildren.Of(root))
        foreach (var inner in Nodes(child))
            yield return inner;
    }

    private static FunctionDecl First(Module module) => module.Declarations.OfType<FunctionDecl>().First();

    [Theory]
    [InlineData("fn make(): fn() -> int throws E { return g; }")]
    [InlineData("fn make(): ?fn() -> int throws E { return null; }")]
    [InlineData("fn make(): fn(string) -> int[] throws [A, B] { return g; }")]
    [InlineData("fn take(f: fn() -> fn() -> int throws E): void { }")]
    [InlineData("fn make(): (fn() -> fn() -> int throws E) { return g; }")]
    [InlineData("fn main(): void { let m = (): fn() -> int throws E => g; }")]
    public void A_set_behind_a_function_type_that_is_a_return_type_says_whose_it_is(string source)
    {
        var (_, de, _) = Parse(source);
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0056", error.Code);
        Assert.Equal("throws", source[error.Span.Start..error.Span.End]);
        Assert.Contains("is itself a return type", error.Message);
        Assert.Contains("throws …)", Assert.Single(error.Notes!).Message);
    }

    /// <summary>The message names the type as it is written, so its note is the two spellings
    /// to choose from.</summary>
    [Fact]
    public void The_note_spells_both_meanings()
    {
        var note = Assert.Single(Assert.Single(Parse("fn make(): fn(string) -> int throws E { return g; }").De.Diagnostics).Notes!);
        Assert.Contains("'(fn(string) -> int throws …)'", note.Message);
        Assert.Contains("'(fn(string) -> int) throws …'", note.Message);
    }

    [Fact]
    public void In_parentheses_the_set_is_the_returned_functions()
    {
        var fn = First(Clean("fn make(): (fn() -> int throws E) { return g; }"));
        Assert.Null(fn.Throws);
        Assert.True(fn.ReturnGrouped);
        Assert.Single(Assert.IsType<FunctionType>(fn.ReturnType).Throws!.Types);
    }

    [Fact]
    public void Behind_the_parentheses_the_set_is_the_declarations()
    {
        var fn = First(Clean("fn make(): (fn() -> int) throws E { return g; }"));
        Assert.Single(fn.Throws!.Types);
        Assert.True(fn.ReturnGrouped);
        Assert.Null(Assert.IsType<FunctionType>(fn.ReturnType).Throws);
    }

    /// <summary>Under a '?' the parentheses may stand around the function type alone: they close
    /// it all the same.</summary>
    [Fact]
    public void Parentheses_around_the_function_type_alone_close_it_too()
    {
        var fn = First(Clean("fn make(): ?(fn() -> int) throws E { return null; }"));
        Assert.Single(fn.Throws!.Types);
        Assert.False(fn.ReturnGrouped); // the RETURN type is the optional, and that is not in parentheses
        Assert.Null(Assert.IsType<FunctionType>(Assert.IsType<NullableType>(fn.ReturnType).Inner).Throws);
    }

    /// <summary>Where a function type is no return type it takes the set behind it, as it did:
    /// nobody else could.</summary>
    [Theory]
    [InlineData("fn f(g: fn() -> int throws E): void { }")]
    [InlineData("fn f(): void { let g: fn() -> int throws E = h; }")]
    [InlineData("struct S { g: fn() -> int throws E }")]
    [InlineData("struct S { g: ?fn() -> int throws E }")]
    [InlineData("fn f(gs: (fn() -> int throws E)[]): void { }")]
    [InlineData("fn f(m: Map<string, fn() -> int throws E>): void { }")]
    [InlineData("fn f(g: fn(fn() -> int throws E) -> int): void { }")]
    public void A_function_type_that_is_no_return_type_takes_its_set(string source)
    {
        var type = Nodes(Clean(source)).OfType<FunctionType>().Last(t => t.Parameters.Length == 0);
        Assert.Single(type.Throws!.Types);
    }

    [Theory]
    [InlineData("fn f(): (Task<int>) throws E { }", true)]
    [InlineData("fn f(): ((Task<int>)) throws E { }", true)]
    [InlineData("fn f(): (?Task<int>) throws E { }", true)]
    [InlineData("fn f(): (Task<int> throws E) { }", true)]
    [InlineData("fn f(): Task<int> throws E { }", false)]
    [InlineData("fn f(): (Task<int>)[] throws E { }", false)]
    [InlineData("fn f(): ?(Task<int>) throws E { }", false)]
    [InlineData("fn f(): Map<(int), int> throws E { }", false)]
    [InlineData("fn f(): ((int), int) throws E { }", false)]
    [InlineData("fn f(): int { }", false)]
    public void The_parentheses_of_a_declarations_return_type_are_remembered(string source, bool grouped) =>
        Assert.Equal(grouped, First(Clean(source)).ReturnGrouped);

    [Theory]
    [InlineData("fn f(g: fn() -> (Task<int>) throws E): void { }", true)]
    [InlineData("fn f(g: fn() -> (Task<int> throws E)): void { }", true)]
    [InlineData("fn f(g: fn() -> Task<int> throws E): void { }", false)]
    [InlineData("fn f(g: fn((int)) -> int throws E): void { }", false)]
    public void The_parentheses_of_a_function_types_return_type_are_remembered(string source, bool grouped) =>
        Assert.Equal(grouped, Nodes(Clean(source)).OfType<FunctionType>().Single().ReturnGrouped);

    [Theory]
    [InlineData("fn f(): void { let m = (): (Task<int>) throws E => g(); }", true)]
    [InlineData("fn f(): void { let m = (): Task<int> throws E => g(); }", false)]
    [InlineData("fn f(): void { let m = (x: (int)): int throws E => g(); }", false)]
    public void The_parentheses_of_a_lambdas_return_type_are_remembered(string source, bool grouped) =>
        Assert.Equal(grouped, Nodes(Clean(source)).OfType<LambdaExpr>().Single().ReturnGrouped);
}
