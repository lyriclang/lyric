using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>sealed interface</c> (design/v5/spec/04 D8): the conformers in one module, the match of
/// type patterns exhaustive without <c>_</c>; and the child interface value as a parent value
/// (D10).
/// </summary>
public class SealedTests
{
    private const string Shapes = """
        module shapes;
        pub interface Display { fn show(): string; }
        pub sealed interface Shape :: [Display] { fn area(): int; }
        pub struct Circle :: [Shape] { r: int, fn area(): int { return 1; } fn show(): string { return "c"; } }
        pub class Rect :: [Shape] { w: int, fn area(): int { return 2; } fn show(): string { return "r"; } }
        pub struct Tri { b: int }
        extend Tri :: [Shape] { fn area(): int { return 3; } fn show(): string { return "t"; } }
        pub interface Other { fn other(): int; }

        """;

    private const string Prelude = """
        import shapes { Shape, Display, Circle, Rect, Tri, Other };

        """;

    private static DiagnosticEngine Check(string body, string? shapes = null)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var lib = sm.AddVirtual("shapes.lyr", shapes ?? Shapes);
        comp.AddModule(new Parser(sm, lib, de).ParseModule());
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string body, string? shapes = null)
    {
        var de = Check(body, shapes);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code, string? shapes = null)
    {
        var errors = Check(body, shapes).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ the closed set

    [Fact]
    public void Type_patterns_over_a_sealed_interface_are_exhaustive_without_the_catch_all() =>
        Allowed("fn f(s: Shape): int { return match (s) { c: Circle => c.r, r: Rect => r.w, t: Tri => t.b }; }");

    [Fact]
    public void A_missing_conformer_is_named() =>
        Assert.Contains("'_: Rect'", Rejected("fn f(s: Shape): int { return match (s) { c: Circle => c.r, t: Tri => t.b }; }", "LYR-SEM0050"));

    [Fact]
    public void A_guarded_arm_does_not_count() =>
        Assert.Contains("'_: Rect'", Rejected("fn f(s: Shape): int { return match (s) { c: Circle => c.r, r: Rect if r.w > 0 => r.w, t: Tri => t.b }; }", "LYR-SEM0050"));

    [Fact]
    public void A_conformer_in_another_module_is_refused() =>
        Assert.Contains("sealed", Rejected("struct Sq :: [Shape] { s: int, fn area(): int { return 4; } fn show(): string { return \"s\"; } }\nfn f(): int { return 0; }", "LYR-SEM0132"));

    [Fact]
    public void A_conformance_block_in_another_module_is_refused() =>
        Assert.Contains("sealed", Rejected("struct Sq { s: int }\nextend Sq :: [Shape] { fn area(): int { return 4; } fn show(): string { return \"s\"; } }\nfn f(): int { return 0; }", "LYR-SEM0132"));

    [Fact]
    public void A_child_interface_in_another_module_is_refused() =>
        Assert.Contains("sealed", Rejected("interface Child :: [Shape] { fn extra(): int; }\nfn f(): int { return 0; }", "LYR-SEM0132"));

    [Fact]
    public void An_open_interface_still_needs_the_catch_all() =>
        Rejected("fn f(d: Display): int { return match (d) { c: Circle => c.r }; }", "LYR-SEM0050");

    // ------------------------------------------------------------------ D10

    [Fact]
    public void A_child_interface_value_is_a_parent_value() =>
        Allowed("fn f(s: Shape): string { let d: Display = s; return d.show(); }\nfn g(d: Display): string { return d.show(); }\nfn h(s: Shape): string { return g(s); }");

    [Fact]
    public void An_unrelated_interface_is_not_reached() =>
        Rejected("fn f(s: Shape): Other { return s; }", "LYR-SEM0001");

    [Fact]
    public void The_way_down_is_no_coercion() =>
        Rejected("fn f(d: Display): Shape { return d; }", "LYR-SEM0001");
}
