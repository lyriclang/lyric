using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The two M3 leftovers that waited for synthesis (M4 S9): <c>?T == ?T</c> through the value's
/// equality (design/v5/spec/03 O6), and <c>[x] * n</c> over an element that is or holds an object,
/// which clones every slot under <c>Clone</c> (03 §5.1, 10 C7). Both are desugared to a
/// <c>std.core</c> call.
/// </summary>
public class OptionalEqualityAndRepeatTests
{
    private const string Core = """
        module std.core;
        pub interface Equatable { fn equals(o: Self): bool; }
        pub interface Ordered :: [Equatable] { fn compare(o: Self): ?Ordering; }
        pub enum Ordering { Less, Equal, Greater }
        pub interface Clone { fn clone(): Self; }
        pub interface Debug { fn debug(): string; }
        extend int :: [Equatable, Clone, Debug] {
            fn equals(o: int): bool { return true; }
            fn clone(): int { return this; }
            fn debug(): string { return ""; }
        }
        extend string :: [Equatable, Clone, Debug] {
            fn equals(o: string): bool { return true; }
            fn clone(): string { return this; }
            fn debug(): string { return this; }
        }
        pub fn arrayOf<T>(n: int, f: fn(int) -> T): T[] { return []; }
        pub fn equalOptionals<T :: [Equatable]>(a: ?T, b: ?T): bool { return true; }
        pub fn repeatArray<T :: [Clone]>(xs: T[], n: int): T[] { return xs; }
        pub fn cloneArray<T :: [Clone]>(xs: T[]): T[] { return xs; }
        pub fn debugArray<T :: [Debug]>(xs: T[]): string { return ""; }
        pub fn debugOptional<T :: [Debug]>(o: ?T): string { return ""; }

        """;

    private const string Prelude = """
        struct P :: [Equatable] { x: int }
        struct NoEq { n: int }
        class Cell :: [Clone] { var v: int }
        class Plain { v: int }
        struct Holder :: [Clone] { items: int[] }
        struct Bare { items: int[] }
        interface Walker { fn walk(): int; }

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

    // --- ?T == ?T ---------------------------------------------------------------------------

    [Fact]
    public void Two_optionals_of_a_scalar_compare() =>
        Allowed("fn f(a: ?int, b: ?int): bool { return a == b && a != b; }");

    [Fact]
    public void An_optional_compares_with_a_value_of_its_type() =>
        Allowed("fn f(a: ?int): bool { return a == 5 && 5 == a && a != 6; }");

    [Fact]
    public void Two_optionals_of_a_conforming_type_compare() =>
        Allowed("fn f(a: ?P, b: ?P, c: P): bool { return a == b && a == c; }");

    [Fact]
    public void The_null_test_stays_what_it_was() =>
        // Two statements: on the right of 'a == null ||' the name is narrowed already (03 §3.2.3).
        Allowed("fn f(a: ?int): bool { let p = a == null; let q = a != null; return p || q; }");

    [Fact]
    public void Without_equatable_on_the_value_the_optionals_do_not_compare() =>
        Assert.Contains("carries no 'Equatable'", Rejected("fn f(a: ?NoEq, b: ?NoEq): bool { return a == b; }", "LYR-SEM0059"));

    [Fact]
    public void Optionals_of_different_values_do_not_compare() =>
        Rejected("fn f(a: ?int, b: ?string): bool { return a == b; }", "LYR-SEM0003");

    [Fact]
    public void There_is_no_ordering_on_an_optional() =>
        Rejected("fn f(a: ?int, b: ?int): bool { return a < b; }", "LYR-SEM0003");

    // --- [x] * n ----------------------------------------------------------------------------

    [Fact]
    public void A_value_element_repeats_natively() =>
        Allowed("fn f(): int { let xs = [1] * 3; let ss = [\"s\"] * 2; let ps = [P { x = 1 }] * 2; return xs.length() + ss.length() + ps.length(); }");

    [Fact]
    public void An_object_element_repeats_through_clone() =>
        Allowed("fn f(): int { let cs = [Cell { v = 1 }] * 3; let hs = [Holder { items = [] }] * 2; return cs.length() + hs.length(); }");

    [Fact]
    public void An_object_element_without_clone_is_refused() =>
        Assert.Contains("arrayOf", Rejected("fn f(): int { let cs = [Plain { v = 1 }] * 3; return cs.length(); }", "LYR-SEM0136"));

    [Fact]
    public void A_struct_holding_an_array_needs_clone_too() =>
        Rejected("fn f(): int { let bs = [Bare { items = [] }] * 2; return bs.length(); }", "LYR-SEM0136");

    [Fact]
    public void An_interface_value_element_is_refused() =>
        Rejected("fn f(w: Walker): int { let ws = [w] * 2; return ws.length(); }", "LYR-SEM0136");

    [Fact]
    public void An_array_of_arrays_needs_clone_of_the_element() =>
        // 'int[]' has no Clone conformance of its own yet (the shape conformance, M8a).
        Rejected("fn f(): int { let xss = [[1, 2]] * 2; return xss.length(); }", "LYR-SEM0136");
}
