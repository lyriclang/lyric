using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>x is T</c> with the smart cast, the type pattern <c>c: T</c>, and <c>Any</c>
/// (design/v5/spec/03 T10, T11; 04 D9).
/// </summary>
public class TypeTestTests
{
    private const string Prelude = """
        interface Shape { fn area(): int; }
        interface Named { fn name(): string; }
        interface Parse { static fn parse(s: string): Self; }
        struct Circle :: [Shape, Named] { r: int, fn area(): int { return 1; } fn name(): string { return "c"; } }
        class Rect :: [Shape] { w: int, fn area(): int { return 2; } }
        enum Dir :: [Shape] { Up, Down; fn area(): int { return 0; } }

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        // 'Any' lives in std.core (03 T10), visible without an import: the seed of it, as the
        // tests of the attributes seed theirs.
        var core = sm.AddVirtual("core.lyr", "module std.core;\npub interface Any { }\n");
        comp.AddModule(new Parser(sm, core, de).ParseModule());
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

    // ------------------------------------------------------------------ is

    [Fact]
    public void Is_tests_an_interface_value_for_a_struct_a_class_or_an_enum() =>
        Allowed("fn f(s: Shape): int { return if (s is Circle) 1 else if (s is Rect) 2 else if (s is Dir) 3 else 0; }");

    [Fact]
    public void In_the_branch_the_name_is_the_tested_type() =>
        Allowed("fn f(s: Shape): int { if (s is Circle) { return s.r; } return 0; }");

    [Fact]
    public void The_narrowing_ends_with_the_branch() =>
        Assert.Contains("'r'", Rejected("fn f(s: Shape): int { if (s is Circle) { } return s.r; }", "LYR-SEM0012"));

    [Fact]
    public void A_conjunction_narrows_too() =>
        Allowed("fn f(s: Shape): int { if (s is Circle && s.r > 0) { return s.r; } return 0; }");

    [Fact]
    public void Is_on_a_known_type_is_refused() =>
        Assert.Contains("known already", Rejected("fn f(c: Circle): bool { return c is Circle; }", "LYR-SEM0131"));

    [Fact]
    public void Is_on_an_optional_asks_for_the_null_test_first() =>
        Assert.Contains("null first", Rejected("fn f(s: ?Shape): bool { return s is Circle; }", "LYR-SEM0131"));

    [Fact]
    public void A_type_never_behind_an_interface_is_refused() =>
        Assert.Contains("never behind", Rejected("fn f(s: Shape): bool { return s is int; }", "LYR-SEM0131"));

    [Fact]
    public void A_type_parameter_is_no_test() =>
        Assert.Contains("type parameter", Rejected("fn f<T>(s: Shape): bool { return s is T; }", "LYR-SEM0131"));

    [Fact]
    public void Interface_to_interface_tests_the_conformance_list() =>
        Allowed("fn f(s: Shape): string { if (s is Named) { return s.name(); } return \"\"; }");

    [Fact]
    public void A_constraint_only_interface_is_no_test_target() =>
        Assert.Contains("constraint", Rejected("fn f(s: Shape): bool { return s is Parse; }", "LYR-SEM0126"));

    // ------------------------------------------------------------------ type patterns

    [Fact]
    public void A_type_pattern_binds_the_name_as_the_type() =>
        Allowed("fn f(s: Shape): int { return match (s) { c: Circle => c.r, r: Rect => r.w, _ => 0 }; }");

    [Fact]
    public void A_type_pattern_without_a_name_tests_only() =>
        Allowed("fn f(s: Shape): int { return match (s) { _: Circle => 1, _ => 0 }; }");

    [Fact]
    public void An_open_interface_needs_the_catch_all() =>
        Rejected("fn f(s: Shape): int { return match (s) { c: Circle => c.r, r: Rect => r.w }; }", "LYR-SEM0050");

    [Fact]
    public void A_type_pattern_on_a_known_type_is_refused() =>
        Assert.Contains("known already", Rejected("fn f(c: Circle): int { return match (c) { x: Circle => x.r, _ => 0 }; }", "LYR-SEM0131"));

    // ------------------------------------------------------------------ Any

    [Fact]
    public void Every_value_is_an_Any_at_the_transition_undeclared() =>
        Allowed("fn f(): int { let a: Any = Circle { r = 1 }; let b: Any = Rect { w = 2 }; let c: Any = Dir.Up; let xs: Any[] = [a, b, c]; return xs.length(); }");

    [Fact]
    public void A_scalar_is_no_Any() =>
        Rejected("fn f(): int { let a: Any = 5; return 0; }", "LYR-SEM0001");

    [Fact]
    public void Out_of_an_Any_through_is() =>
        Allowed("fn f(a: Any): int { if (a is Circle) { return a.r; } return match (a) { r: Rect => r.w, _ => 0 }; }");

    [Fact]
    public void Nothing_converts_to_Any_unasked() =>
        Rejected("fn g(a: Any): int { return 0; }\nfn f(): int { let s: Shape = Circle { r = 1 }; return g(s); }", "LYR-SEM0001");
}
