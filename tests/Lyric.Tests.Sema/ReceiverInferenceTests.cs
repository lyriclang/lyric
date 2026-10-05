using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The type arguments of a generic TYPE, inferred (design/v5/spec/03 T8, the review's M8a-2):
/// a static function or a variant called through the type's bare name — <c>Cell.of(3)</c>,
/// <c>Opt.Some(7)</c> — binds the type's parameters as a call binds its own, the type the
/// position expects first, then the arguments; and an initializer without a list takes them
/// from its field values, <c>Cell { v = 3 }</c>, behind the context. What nothing determines is
/// refused with the form to write (<c>LYR-SEM0060</c>).
/// </summary>
public class ReceiverInferenceTests
{
    private const string Types = """
        enum Opt<T> { Some(T), None }
        struct Cell<T> {
            v: T,
            static fn of(v: T): Cell<T> { return Cell<T> { v = v }; }
            static fn none(): ?Cell<T> { return null; }
            static fn both(a: T, b: T): T[] { return [a, b]; }
            static fn turn<U>(v: T, f: fn(T) -> U): Cell<U> { return Cell<U> { v = f(v) }; }
            fn get(): T { return this.v; }
        }
        struct Pair<A, B> {
            first: A,
            second: B,
            static fn of(a: A, b: B): Pair<A, B> { return Pair<A, B> { first = a, second = b }; }
            static fn left(a: A): ?Pair<A, B> { return null; }
        }
        fn show(c: Cell<int>): int { return c.get(); }
        fn inner<T>(c: Cell<T>): T { return c.get(); }

        """;

    private static Diagnostic[] Errors(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Types + "fn use(): void {\n" + body + "\n}\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToArray();
    }

    private static void Silent(string body)
    {
        var errors = Errors(body);
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
    }

    private static Diagnostic One(string body, string code)
    {
        var errors = Errors(body);
        Assert.True(errors.Length == 1, errors.Length + " errors\n" + string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(code, errors[0].Code);
        return errors[0];
    }

    /// <summary>Each line binds what it reads to a type written out: silent only if the instance
    /// is the one the comment names.</summary>
    [Theory]
    // from the arguments
    [InlineData("let c = Cell.of(3);\n    let n: int = c.get();")]
    [InlineData("let p = Pair.of(1, \"x\");\n    let a: int = p.first;\n    let b: string = p.second;")]
    [InlineData("let o = Opt.Some(7);\n    let back: Opt<int> = o;")]
    [InlineData("let n: int = show(Cell.of(4));")]
    [InlineData("let s: string = inner(Cell.of(\"x\"));")]
    // from the type the position expects — before the arguments
    [InlineData("let c: ?Cell<int> = Cell.none();")]
    [InlineData("let c: Cell<?int> = Cell.of(3);\n    let n: ?int = c.get();")]
    [InlineData("let c: Cell<float> = Cell.of(1.5);")]
    [InlineData("let xs: string[] = Cell.both(\"a\", \"b\");")]
    // the type's parameters and the function's own, together
    [InlineData("let c = Cell.turn(1, (n: int) => \"x\");\n    let s: string = c.get();")]
    [InlineData("let c = Cell.turn<string>(1, (n: int) => \"x\");\n    let s: string = c.get();")]
    // an initializer: from its field values, behind the context
    [InlineData("let c = Cell { v = 3 };\n    let n: int = c.v;")]
    [InlineData("let p = Pair { first = 1, second = \"x\" };\n    let b: string = p.second;")]
    [InlineData("let c: Cell<?int> = Cell { v = 3 };\n    let n: ?int = c.v;")]
    [InlineData("let n: int = inner(Cell { v = 3 });")]
    public void A_generic_types_arguments_are_inferred(string body) => Silent("    " + body);

    [Theory]
    [InlineData("let c = Cell.none();", "'T' of 'Cell'", "'Cell<…>.none(…)'")]
    [InlineData("let p = Pair.left(1);", "'B' of 'Pair'", "'Pair<…>.left(…)'")]
    [InlineData("let c = Cell { v = null };", "'T' for 'Cell'", "'Cell<…> { … }'")]
    [InlineData("let c = Cell { v = [] };", "'T' for 'Cell'", "'Cell<…> { … }'")]
    public void What_nothing_determines_is_refused_with_the_form_to_write(string body, string what, string form)
    {
        var error = One("    " + body, "LYR-SEM0060");
        Assert.Contains(what, error.Message);
        Assert.Contains(form, error.Message);
    }

    /// <summary>The expected type binds the type's parameters first: an argument that does not
    /// fit is the argument's error, as under a written list.</summary>
    [Fact]
    public void An_argument_against_the_expected_instance_is_the_arguments_error()
    {
        var error = One("    let c: Cell<int> = Cell.of(\"x\");", "LYR-SEM0001");
        Assert.Contains("'string'", error.Message);
        Assert.Contains("'int'", error.Message);
    }

    /// <summary>Without a call the bare name of a generic type still needs its arguments.</summary>
    [Theory]
    [InlineData("let f = Cell.of;")]
    [InlineData("let n = Opt.None;")]
    public void Without_a_call_the_type_names_its_arguments(string body) => One("    " + body, "LYR-SEM0063");
}
