using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Two places the expected type did not reach (design/v5/spec/03 T8; the review's R4a). The
/// ELEMENTS of a tuple literal: where the position expects a tuple of its length, each element
/// stands where its element type is expected, as a binding's value does — <c>(0, null)</c> is a
/// <c>(int, ?int)</c>. And a FIELD DEFAULT, which is checked with its type's parameters in
/// scope: <c>cell: Cell&lt;T&gt; = Cell&lt;T&gt;.none()</c>.
/// </summary>
public class ExpectedElementTests
{
    private const string Types = """
        enum Color { Red, Green }
        struct Cell<T> {
            v: ?T,
            static fn none(): Cell<T> { return Cell<T> { v = null }; }
        }
        fn take(p: (int, ?int)): int { return p.0; }

        """;

    private static Diagnostic[] Errors(string declarations)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Types + declarations);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToArray();
    }

    private static void Silent(string declarations)
    {
        var errors = Errors(declarations);
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
    }

    private static Diagnostic One(string declarations, string code)
    {
        var errors = Errors(declarations);
        Assert.True(errors.Length == 1, errors.Length + " errors\n" + string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(code, errors[0].Code);
        return errors[0];
    }

    [Theory]
    [InlineData("fn pair(): (int, ?int) { return (0, null); }")]
    [InlineData("fn f(): void { let t: (int, ?string) = (1, \"x\"); }")]
    [InlineData("fn f(): void { let u: (?int, int[]) = (null, []); }")]
    [InlineData("fn f(): void { let n: ((int, ?int), ?string) = ((1, null), null); }")]
    [InlineData("fn f(): int { return take((1, null)); }")]
    [InlineData("fn f(): void { let o: ?(int, ?int) = (1, null); }")]
    [InlineData("fn f(): void { let c: (Color, ?Color) = (.Red, .Green); }")]
    [InlineData("struct S { p: (int, ?int) }\nfn f(): S { return S { p = (1, null) }; }")]
    [InlineData("fn f(): void { var t: (int, ?int) = (1, 2);\n    t = (3, null); }")]
    public void An_element_stands_where_its_type_is_expected(string declarations) => Silent(declarations);

    /// <summary>An element that does not fit is said at the element, as a field's value is.</summary>
    [Fact]
    public void An_element_that_does_not_fit_is_the_elements_error()
    {
        var error = One("fn f(): void { let t: (int, bool) = (1, \"x\"); }", "LYR-SEM0001");
        Assert.Contains("cannot assign 'string' to 'bool'", error.Message);
    }

    /// <summary>Controls: the literal's length is its own, a tuple VALUE is converted by nothing
    /// — it is one value of its type —, and without an expected type an element fixes what it
    /// fixes.</summary>
    [Theory]
    [InlineData("fn f(): void { let t: (int, int) = (1, 2, 3); }", "LYR-SEM0001")]
    [InlineData("fn f(): void { let a = (1, 2);\n    let b: (int, ?int) = a; }", "LYR-SEM0001")]
    [InlineData("fn f(): void { let t = (1, null); }", "LYR-SEM0010")]
    public void What_the_expected_tuple_does_not_reach(string declarations, string code) => One(declarations, code);

    [Theory]
    [InlineData("class Bag<T> {\n    cell: Cell<T> = Cell<T>.none(),\n    label: ?T = null,\n}")]
    [InlineData("class Bag<T> {\n    cell: Cell<T> = Cell.none(),\n}")]
    [InlineData("struct Two<A, B> {\n    first: Cell<A> = Cell<A>.none(),\n    second: (?A, ?B) = (null, null),\n}")]
    public void A_field_default_names_its_types_parameters(string declarations) => Silent(declarations);

    /// <summary>Controls: a default has no 'this', and the type's other members are not its
    /// scope — only its parameters are.</summary>
    [Theory]
    [InlineData("struct P {\n    a: int = 2,\n    b: int = a + 1,\n}", "LYR-SEM0002")]
    [InlineData("struct P {\n    a: int = 2,\n    b: int = this.a,\n}", "LYR-SEM0008")]
    public void A_field_default_names_no_instance(string declarations, string code) => One(declarations, code);
}
