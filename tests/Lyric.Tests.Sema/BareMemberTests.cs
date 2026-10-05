using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A member's bare name in its type's body (design/v5/spec/07, the review's M6-30). The names of
/// a type's members are in scope between its braces: a static one is reached by its name, and
/// one that belongs to an instance — a field, a method that is not static — is written through
/// its receiver, <c>this.label</c>; the bare name is refused once, with what to write
/// (<c>LYR-SEM0055</c>), where the checker used to say nothing and the lowering crashed. In a
/// CALL a field counts only when it can be called: <c>label()</c> beside a field
/// <c>label: string</c> means the function outside.
/// </summary>
public class BareMemberTests
{
    private const string Frame = """
        fn run(): string { return "outer"; }
        fn size(xs: int[]): int { return 0; }
        fn bump(&n: int): void { n += 1; }
        fn pair(a: int): int { return a; }
        fn pair(a: int, b: int): int { return a + b; }
        struct In { n: int }
        struct P {
            var x: int,
            inner: In,
            run: fn() -> string,
            fn get(): int { return this.x; }
            fn add(a: int): int { return this.x + a; }
            fn add(a: int, b: int): int { return this.x + a + b; }
            fn pick<T>(a: T, b: T): T { return a; }
            fn size(): int { return 0; }

        """;

    private static Diagnostic[] Errors(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToArray();
    }

    private static void Silent(string source)
    {
        var errors = Errors(source);
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
    }

    private static Diagnostic One(string source, string code)
    {
        var errors = Errors(source);
        Assert.True(errors.Length == 1, errors.Length + " errors\n" + string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(code, errors[0].Code);
        return errors[0];
    }

    /// <summary>One method in <c>P</c>, which has fields, a field that can be called, methods —
    /// two of one name, a generic one — and functions outside named like two of them.</summary>
    [Theory]
    // a field, wherever it is read or written
    [InlineData("fn f(): int { return x; }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("mut fn f(): void { x = 1; }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("mut fn f(): void { x += 1; }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("fn f(): int { return inner.n; }", "'inner' is a field of 'P'", "'this.inner'")]
    [InlineData("fn f(): string { return f\"{x}\"; }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("mut fn f(): void { bump(&x); }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("fn f(): fn() -> int { return () => x + 1; }", "'x' is a field of 'P'", "'this.x'")]
    // as the argument of a function that has several counts
    [InlineData("fn f(): int { return pair(x, 1); }", "'x' is a field of 'P'", "'this.x'")]
    [InlineData("fn f(): int { return pair(get()); }", "'get' is a method of 'P'", "'this.get(…)'")]
    // a field that can be called is what the call means — before the function outside
    [InlineData("fn f(): string { return run(); }", "'run' is a field of 'P'", "'this.run(…)'")]
    [InlineData("fn f(): fn() -> string { return run; }", "'run' is a field of 'P'", "'this.run'")]
    // a method: one, one of two, a generic one, as a value, before a function outside
    [InlineData("fn f(): int { return get(); }", "'get' is a method of 'P'", "'this.get(…)'")]
    [InlineData("fn f(): int { return add(1); }", "'add' is a method of 'P'", "'this.add(…)'")]
    [InlineData("fn f(): int { return add(1, 2); }", "'add' is a method of 'P'", "'this.add(…)'")]
    [InlineData("fn f(): int { return pick(1, 2); }", "'pick' is a method of 'P'", "'this.pick(…)'")]
    [InlineData("fn f(): fn() -> int { return get; }", "'get' is a method of 'P'", "'this.get'")]
    [InlineData("fn f(): int { return size([1, 2]); }", "'size' is a method of 'P'", "'this.size(…)'")]
    // where there is no 'this'
    [InlineData("static fn f(): int { return x; }", "'x' is a field of 'P'", "no 'this'")]
    [InlineData("static fn f(): int { return get(); }", "'get' is a method of 'P'", "no 'this'")]
    public void A_member_of_an_instance_is_written_through_its_receiver(string method, string what, string hint)
    {
        var error = One(Frame + "    " + method + "\n}\n", "LYR-SEM0055");
        Assert.Contains(what, error.Message);
        Assert.Contains(hint, error.Message);
    }

    /// <summary>A compound assignment writes its target and reads it as the operator's left
    /// side: checked once, so what is wrong with it is said once.</summary>
    [Theory]
    [InlineData("fn f(): void { y += 1; }")]
    [InlineData("fn f(xs: int[]): void { xs[y] += 1; }")]
    [InlineData("fn f(xs: int[]): void { xs[y] *= 2; }")]
    public void A_compound_assignment_says_once_what_is_wrong_with_its_target(string function)
    {
        var error = One(function, "LYR-SEM0002");
        Assert.Contains("'y'", error.Message);
    }

    /// <summary>The call means the function outside — so its type is the function's, and the
    /// 'string' it gives is refused as the method's 'int'.</summary>
    [Fact]
    public void A_call_passes_a_field_that_cannot_be_called()
    {
        var error = One("""
            fn label(): string { return "outer"; }
            struct Tag {
                label: int,
                fn show(): int { return label(); }
            }
            """, "LYR-SEM0001");
        Assert.Contains("'string'", error.Message);
    }

    /// <summary>A type's name in call position is its factory (08 Y9), behind a field too: the
    /// call has the factory's type, so the 'int' it reads is refused as the method's 'string'.</summary>
    [Fact]
    public void A_call_passes_a_field_on_to_a_types_factory()
    {
        var error = One("""
            struct Point { x: int, static fn new(x: int): Point { return Point { x = x }; } }
            struct Line {
                Point: int,
                fn make(): string { return Point(3).x; }
            }
            """, "LYR-SEM0001");
        Assert.Contains("'int'", error.Message);
    }

    [Fact]
    public void A_field_that_cannot_be_called_and_nothing_else_is_no_callee()
    {
        var error = One("""
            struct Tag {
                label: int,
                fn show(): int { return label(); }
            }
            """, "LYR-SEM0013");
        Assert.Contains("'label' is a field of 'Tag'", error.Message);
    }

    /// <summary>Controls: what hides the field whole, and what needs no receiver.</summary>
    [Fact]
    public void A_local_and_a_parameter_hide_the_field_whole() => Silent("""
        fn label(): string { return "outer"; }
        struct Tag {
            label: int,
            fn show(): string {
                let label = () => "local";
                return label();
            }
            fn plain(label: string): string { return label; }
        }
        """);

    [Fact]
    public void A_static_member_is_named_bare() => Silent("""
        struct Id {
            n: int,
            static let step: int = 2;
            static fn of(n: int): Id { return Id { n = n }; }
            fn next(): Id { return of(this.n + step); }
            static fn first(): Id { return of(step); }
        }
        """);

    private const string Mixed = """
        struct M {
            n: int,
            static fn of(a: int): int { return a; }
            fn of(a: int, b: int): int { return a + b + this.n; }

        """;

    /// <summary>Of a static and a method of one name the count chooses (08 §1.2), and the choice
    /// says whether a receiver is missing.</summary>
    [Fact]
    public void Of_a_static_and_a_method_the_count_chooses()
    {
        Silent(Mixed + "    fn one(): int { return of(1); }\n}\n");
        var error = One(Mixed + "    fn two(): int { return of(1, 2); }\n}\n", "LYR-SEM0055");
        Assert.Contains("'of' is a method of 'M'", error.Message);
    }

    [Fact]
    public void A_blocks_method_is_called_on_a_value_too()
    {
        var error = One("""
            struct P { x: int }
            extend P {
                fn get(): int { return this.x; }
                fn twice(): int { return get() * 2; }
            }
            """, "LYR-SEM0055");
        Assert.Contains("'get' is a method of 'P'", error.Message);
        Assert.Contains("'this.get(…)'", error.Message);
    }

    [Fact]
    public void A_default_calls_its_interfaces_member_on_this()
    {
        var error = One("""
            interface Shape {
                fn area(): int;
                fn twice(): int { return area() * 2; }
            }
            """, "LYR-SEM0055");
        Assert.Contains("'area' is a method of 'Shape'", error.Message);
    }

    /// <summary>A variant is written through its type, in its enum's own methods too — as in a
    /// pattern there (<c>LYR-SEM0111</c>).</summary>
    [Theory]
    [InlineData("enum Color { Red, Green;\n    fn other(): Color { return Green; }\n}\n", "'Green' is a variant of 'Color'", "'.Green' or 'Color.Green'")]
    [InlineData("enum Expr { Num(int), Neg(int);\n    fn flip(): Expr { return Neg(1); }\n}\n", "'Neg' is a variant of 'Expr'", "'.Neg' or 'Expr.Neg'")]
    public void A_variant_is_written_through_its_type(string source, string what, string hint)
    {
        var error = One(source, "LYR-SEM0055");
        Assert.Contains(what, error.Message);
        Assert.Contains(hint, error.Message);
    }
}
