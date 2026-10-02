using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Extends on the built-in constructors (design/v5/spec/03 T7 X2): <c>extend&lt;T&gt; T[]</c>,
/// <c>?T</c>, a tuple of fixed arity, <c>Slice&lt;T&gt;</c>, a block on one element type; members
/// only — a conformance of a shape is not written yet.
/// </summary>
public class ConstructorExtendTests
{
    private const string Core = """
        module std.core;
        pub interface Display { fn show(): string; }
        extend int :: [Display] { fn show(): string { return "i"; } }

        """;

    private const string Prelude = """
        struct Foo { n: int }
        extend<T> T[] { fn first(): T { return this[0]; } }
        extend int[] { fn sum(): int { return 0; } }
        extend<T :: [Display]> ?T { fn shown(): string { return "o"; } }
        extend<A, B> (A, B) { fn firstOf(): A { let (a, _) = this; return a; } }
        extend<T> Slice<T> { fn head(): T { return this[0]; } }

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var core = sm.AddVirtual("core.lyr", Core);
        comp.AddModule(new Parser(sm, core, de).ParseModule());
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string body)
    {
        var de = Check(body);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    [Fact]
    public void An_array_member_is_bound_by_the_element() =>
        Allowed("fn f(xs: int[], ss: string[]): string { let n: int = xs.first(); return ss.first(); }");

    [Fact]
    public void A_block_on_one_element_type_adds_to_that_array_alone() =>
        Allowed("fn f(xs: int[]): int { return xs.sum(); }");

    [Fact]
    public void A_block_on_one_element_type_adds_nothing_elsewhere() =>
        Rejected("fn f(xs: string[]): int { return xs.sum(); }", "LYR-SEM0012");

    [Fact]
    public void An_optional_member_under_a_constraint() =>
        Allowed("fn f(o: ?int): string { return o.shown(); }");

    [Fact]
    public void The_constraint_fails_for_the_wrong_element() =>
        Assert.Contains("does not satisfy", Rejected("fn f(o: ?Foo): string { return o.shown(); }", "LYR-SEM0134"));

    [Fact]
    public void A_tuple_member_comes_after_the_elements() =>
        Allowed("fn f(p: (int, string)): int { return p.firstOf() + p.0; }");

    [Fact]
    public void A_slice_member() =>
        Allowed("fn f(v: Slice<int>): int { return v.head(); }");

    [Fact]
    public void A_shape_conforms_through_its_block() =>
        Allowed("interface Describe { fn describe(): string; }\n"
            + "extend<T> T[] :: [Describe] { fn describe(): string { return \"xs\"; } }\n"
            + "fn tell<T :: [Describe]>(x: T): string { return x.describe(); }\n"
            + "fn f(): string { return tell([1, 2]); }");

    [Fact]
    public void A_value_is_not_made_from_a_shape_yet() =>
        Rejected("interface Describe { fn describe(): string; }\n"
            + "extend<T> T[] :: [Describe] { fn describe(): string { return \"xs\"; } }\n"
            + "fn f(): int { let d: Describe = [1, 2]; return 0; }", "LYR-SEM0047");

    [Fact]
    public void A_function_type_is_no_target() =>
        Rejected("extend fn(int) -> int { fn twice(): int { return 0; } }\nfn f(): int { return 0; }", "LYR-SEM0047");

    [Fact]
    public void Length_stays_the_primitive() =>
        Allowed("fn f(xs: int[]): int { return xs.length() + xs.first(); }");
}
