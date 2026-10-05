using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A constant of a generic type is one per instance (design/v5/spec/07 G2; the review's M8a-3,
/// M8a-3b), in the type's body and in a generic block, and its initializer is a CONSTANT: a
/// literal, <c>null</c>, an empty literal, another constant — <c>T.zero</c> under a constraint
/// too —, a unit variant, a struct initializer of these. It is folded where it is read, so
/// nothing runs for it and no order is asked; a call is refused, with the way out
/// (<c>LYR-SEM0169</c>). A generic block's constant was refused altogether
/// (<c>LYR-SEM0159</c>).
/// </summary>
public class InstanceConstantTests
{
    private const string Prelude = """
        interface Zero { static let zero: Self; }
        extend int :: [Zero] { static let zero: int = 0; }
        enum Mode { Fast, Slow }
        struct Point { x: int, y: int }
        class Node { var n: int }
        fn make(): int { return 3; }

        """;

    private static Diagnostic[] Errors(string declarations)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + declarations);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de, singleProgram: false);
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
    [InlineData("static let count: int = 2;")]
    [InlineData("static let low: int = -1;")]
    [InlineData("static let none: ?T = null;")]
    [InlineData("static let empty: T[] = [];")]
    [InlineData("static let name: string = \"box\";")]
    [InlineData("static let mode: Mode = Mode.Fast;")]
    [InlineData("static let mode: Mode = .Slow;")]
    [InlineData("static let origin: Point = Point { x = 0, y = -1 };")]
    [InlineData("static let a: int = 1;\n    static let b: int = a;")]
    [InlineData("static let a: int = 1;\n    static let b: int = Box<T>.a;")]
    // no order among them: one may name another that stands later (the rule of slots,
    // LYR-SEM0057, is not theirs)
    [InlineData("static let b: int = a;\n    static let a: int = 1;")]
    public void A_constant_initializer_is_taken(string member) =>
        Silent("struct Box<T> {\n    v: T,\n    " + member + "\n}\n");

    [Fact]
    public void A_constant_of_the_parameters_constraint_is_one() =>
        Silent("struct Acc<T :: [Zero]> {\n    v: T,\n    static let start: T = T.zero;\n    static let both: (Acc<T>) = Acc<T> { v = T.zero };\n}\n");

    /// <summary>In a generic block too, where it was refused altogether.</summary>
    [Fact]
    public void A_generic_block_holds_constants() =>
        Silent("struct Pair<T> {\n    a: T,\n    b: T,\n}\nextend<T> Pair<T> {\n    static let size: int = 2;\n    static let none: ?T = null;\n}\n"
               + "fn f(): int {\n    let n: ?string = Pair<string>.none;\n    return Pair<int>.size;\n}\n");

    [Theory]
    [InlineData("static let made: int = make();", "a call")]
    [InlineData("static let sum: int = 1 + 2;", "an operation")]
    [InlineData("static let node: Node = Node { n = 1 };", "an object of the class 'Node'")]
    [InlineData("static let list: int[] = [1, 2];", "an array with elements")]
    [InlineData("static let origin: Point = Point { x = make(), y = 0 };", "a call")]
    public void What_would_run_is_refused_with_the_way_out(string member, string what)
    {
        var error = One("struct Box<T> {\n    v: T,\n    " + member + "\n}\n", "LYR-SEM0169");
        Assert.Contains(what, error.Message);
        Assert.Contains("static fn", error.Message);
    }

    [Fact]
    public void In_a_generic_block_likewise() =>
        One("struct Pair<T> {\n    a: T,\n}\nextend<T> Pair<T> {\n    static let made: int = make();\n}\n", "LYR-SEM0169");

    /// <summary>Folded where it is read, a constant that names itself would never end.</summary>
    [Theory]
    [InlineData("static let a: int = a;")]
    [InlineData("static let a: int = b;\n    static let b: int = a;")]
    public void A_constant_does_not_name_itself(string members)
    {
        var errors = Errors("struct Box<T> {\n    v: T,\n    " + members + "\n}\n");
        Assert.Contains(errors, e => e.Code == "LYR-SEM0169" && e.Message.Contains("itself"));
    }

    /// <summary>One per instance, so it is read on an instance: the bare name of the type names
    /// none.</summary>
    [Fact]
    public void A_constant_is_read_on_an_instance()
    {
        var error = One("struct Box<T> {\n    v: T,\n    static let count: int = 2;\n}\nfn f(): int {\n    return Box.count;\n}\n", "LYR-SEM0063");
        Assert.Contains("write its type arguments", error.Message);
    }

    /// <summary>A generic block's constant is there where the block's constraints hold.</summary>
    [Fact]
    public void A_generic_blocks_constant_is_there_under_its_constraints()
    {
        const string Block = "struct Pair<T> {\n    a: T,\n}\nextend<T :: [Zero]> Pair<T> {\n    static let start: T = T.zero;\n}\n";
        Silent(Block + "fn f(): int {\n    return Pair<int>.start;\n}\n");
        var error = One(Block + "fn f(): string {\n    return Pair<string>.start;\n}\n", "LYR-SEM0134");
        Assert.Contains("which 'Pair<string>' does not satisfy", error.Message);
    }

    /// <summary>Control: a type that is generic in nothing keeps the rule of a module binding —
    /// its constant is filled before the entry runs, by an ordinary expression (04 §1).</summary>
    [Fact]
    public void A_plain_types_constant_keeps_its_initializer() =>
        Silent("struct Plain {\n    v: int,\n    static let made: int = make();\n    static let sum: int = 1 + 2;\n}\n");
}
