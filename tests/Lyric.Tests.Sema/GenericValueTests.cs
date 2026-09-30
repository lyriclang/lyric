using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Two things Lyric 5 adds to generics (design/v5/spec/03 T8, T17): the placeholder <c>_</c> in a
/// list of type arguments, filled by the inference, and an instantiated generic function as a
/// value, <c>ident&lt;int&gt;</c>. The bare generic name stays no value (<see cref="GenericsTests"/>).
/// </summary>
public class GenericValueTests
{
    private const string Prelude = """
        struct Pair<A, B> { first: A, second: B }
        struct Num { n: int }
        interface Show { fn show(): string; }
        struct Counter {
            n: int,
            static fn make<T>(seed: T, n: int): Counter { return Counter { n = n }; }
            fn bump(): int { return this.n; }
        }
        fn ident<T>(x: T): T { return x; }
        fn plain(x: int): int { return x; }
        fn collect<T, U>(x: T, y: U): U { return y; }
        fn empty<T>(): T[] { return []; }
        fn shown<T :: [Show]>(x: T): T { return x; }
        fn apply(f: fn(int) -> int, x: int): int { return f(x); }

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string body)
    {
        var de = Check(body);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ the placeholder

    [Fact]
    public void A_placeholder_in_a_call_is_filled_from_the_arguments() =>
        Allowed("fn f(): string { return collect<_, string>(1, \"y\"); }");

    [Fact]
    public void A_written_argument_beside_a_placeholder_still_wins()
    {
        // 'collect<_, string>' with an int in the U position: the written 'string' binds first,
        // and the argument is the one that does not fit.
        var message = Rejected("fn f(): string { return collect<_, string>(1, 2); }", "LYR-SEM0001");
        Assert.Contains("'int'", message);
    }

    [Fact]
    public void A_placeholder_nothing_determines_is_reported() =>
        Assert.Contains("'T'", Rejected("fn f(): int { let xs = empty<_>(); return 0; }", "LYR-SEM0060"));

    [Fact]
    public void A_placeholder_is_filled_from_the_return_position_too() =>
        Allowed("fn f(): int[] { return empty<_>(); }");

    [Fact]
    public void A_placeholder_in_an_initializer_comes_from_the_field_values() =>
        Allowed("fn f(): int { let p = Pair<_, string> { first = 3, second = \"x\" }; return p.first; }");

    [Fact]
    public void A_placeholder_in_an_initializer_comes_from_the_context_first() =>
        Allowed("fn f(): bool { let p: Pair<int, bool> = Pair<_, _> { first = 1, second = true }; return p.second; }");

    [Fact]
    public void A_field_value_that_fixes_no_type_fills_no_placeholder()
    {
        // '[]' has no element type of its own; binding A to a hole is no answer.
        var message = Rejected("fn f(): int { let p = Pair<_, int> { first = [], second = 2 }; return p.second; }", "LYR-SEM0060");
        Assert.Contains("'A'", message);
        Assert.Contains("field", message);
    }

    [Fact]
    public void A_faulty_field_value_is_reported_once() =>
        Rejected("fn f(): int { let p = Pair<_, int> { first = nosuch, second = 2 }; return p.second; }", "LYR-SEM0002");

    [Fact]
    public void A_placeholder_outside_a_type_argument_list_is_refused() =>
        Rejected("fn f(): int { let p: Pair<_, int> = Pair<int, int> { first = 1, second = 2 }; return p.second; }", "LYR-SEM0117");

    // ------------------------------------------------------------------ the instantiated function

    [Fact]
    public void An_instantiated_generic_function_is_a_value() =>
        Allowed("""
            fn f(): int {
                let g = ident<int>;
                let h: fn(int) -> int = ident<int>;
                return apply(g, 1) + apply(ident<int>, 2) + h(3);
            }
            """);

    [Fact]
    public void Its_type_is_the_substituted_signature() =>
        Assert.Contains("'fn(string) -> string'",
            Rejected("fn f(): int { let g: fn(int) -> int = ident<string>; return g(1); }", "LYR-SEM0001"));

    [Fact]
    public void A_placeholder_in_it_is_filled_from_the_expected_function_type() =>
        Allowed("fn f(): int { let g: fn(int) -> int = ident<_>; return g(1); }");

    [Fact]
    public void A_placeholder_in_it_without_a_function_type_is_reported() =>
        Assert.Contains("function type", Rejected("fn f(): int { let g = ident<_>; return 0; }", "LYR-SEM0060"));

    [Fact]
    public void The_argument_count_is_checked() =>
        Rejected("fn f(): int { let g = ident<int, int>; return 0; }", "LYR-SEM0026");

    [Fact]
    public void A_function_that_is_not_generic_takes_no_arguments() =>
        Assert.Contains("not generic", Rejected("fn f(): int { let g = plain<int>; return g(1); }", "LYR-SEM0026"));

    [Fact]
    public void The_constraints_hold_for_the_value_form() =>
        Rejected("fn f(): int { let g = shown<Num>; return 0; }", "LYR-SEM0028");

    [Fact]
    public void A_static_generic_method_is_a_value() =>
        Allowed("fn f(): int { let m = Counter.make<bool>; return m(true, 2).n; }");

    [Fact]
    public void An_instance_method_through_its_type_is_none() =>
        Assert.Contains("receiver", Rejected("fn f(): int { let b = Counter.bump<int>; return 0; }", "LYR-SEM0055"));

    [Fact]
    public void The_bare_generic_name_is_still_no_value() =>
        Rejected("fn f(): int { let g = ident; return 0; }", "LYR-SEM0052");
}
