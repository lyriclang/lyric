using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Conformance synthesis (design/v5/spec/04 D7, 05 §14): a conformance of the family written
/// without its member is implemented from the fields; a generic type conditionally; a written
/// member replaces the synthesis; a missing field conformance names the field; <c>Debug</c> is
/// given unasked where the fields allow and withdrawn silently where they do not.
/// </summary>
public class SynthesisTests
{
    private const string Prelude = """
        struct NoEq { n: int }
        interface Walker { fn walk(): int; }

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de) { ModuleLoader = Library.Loader(sm, de) };
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

    private static Diagnostic Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0];
    }

    // --- the family -------------------------------------------------------------------------

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void The_family_is_synthesized_for_a_struct() =>
        Allowed("""
            struct P :: [Equatable, Hashable, Ordered, TotalOrder, Clone, Default, Display] { x: int, y: string }
            fn f(p: P, q: P): bool {
                let h: int = p.hash();
                let c: P = p.clone();
                let d: P = P.default();
                let s: string = p.show();
                let o: Ordering = p.totalCompare(q);
                return p == q && p < q && p.compare(q) != null;
            }
            """);

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void A_class_with_reference_fields_is_synthesized() =>
        Allowed("""
            class C :: [Equatable, Hashable, Clone] { items: int[], o: ?int, t: (int, string) }
            fn f(a: C, b: C): bool { let c: C = a.clone(); return a == b && a.hash() == b.hash(); }
            """);

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void An_enum_is_synthesized_variant_by_variant() =>
        Allowed("""
            enum E :: [Equatable, Hashable, Ordered, Clone] { A, B(int), R { w: int } }
            fn f(a: E, b: E): bool { let c: E = a.clone(); return a == b && a < b && a.hash() == 1; }
            """);

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void A_written_member_replaces_the_synthesis() =>
        Allowed("""
            struct W :: [Equatable, Hashable] { x: int, y: int,
                fn equals(o: W): bool { return this.x == o.x; }
            }
            fn f(a: W, b: W): bool { return a == b && a.hash() == b.hash(); }
            """);

    [Fact]
    public void The_implied_parents_come_along() =>
        Allowed("""
            struct H :: [Hashable] { x: int }
            struct T :: [TotalOrder] { x: int }
            fn f(a: H, b: H, c: T, d: T): bool { return a == b && c < d && c == d; }
            """);

    // --- generic types ----------------------------------------------------------------------

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void A_generic_type_is_synthesized_conditionally() =>
        Allowed("""
            struct Pair<T> :: [Equatable, Hashable, Default] { a: T, b: T }
            fn f(p: Pair<int>, q: Pair<int>): bool { let d: Pair<int> = Pair<int>.default(); return p == q && p.hash() == 1; }
            """);

    [Fact]
    public void The_condition_fails_for_an_argument_without_the_conformance() =>
        Rejected("""
            struct Pair<T> :: [Equatable] { a: T, b: T }
            fn f(p: Pair<NoEq>, q: Pair<NoEq>): bool { return p == q; }
            """, "LYR-SEM0059");

    // --- what is refused, and where it is said ----------------------------------------------

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void A_field_without_the_conformance_is_named_at_the_declaration()
    {
        var error = Rejected("struct N :: [Equatable] { k: int, f: NoEq }\n", "LYR-SEM0059");
        Assert.Contains("in the 'Equatable' synthesized for 'N'", error.Message);
        Assert.Contains(error.Notes!, n => n.Message.Contains("this.f == o.f"));
        // Pointed at the conformance list entry in test.lyr (the second file), not into the
        // synthesized text.
        Assert.Equal(2, error.Span.File.Value);
    }

    [Fact]
    public void A_default_of_an_enum_is_refused() =>
        Assert.Contains("say which variant", Rejected("enum Color :: [Default] { Red, Green }\n", "LYR-SEM0135").Message);

    [Fact(Skip = "Moves to Lyric 5 in the next slices.")]
    public void A_clone_of_a_function_field_is_refused() =>
        Assert.Contains("has no synthesized clone", Rejected("struct F :: [Clone] { f: fn() -> int }\n", "LYR-SEM0135").Message);

    [Fact]
    public void A_hint_from_the_synthesized_text_does_not_surface() =>
        Assert.DoesNotContain(Check("struct P :: [Hashable] { x: int, y: int }\n").Diagnostics, d => d.Code == "LYR-SEM0075");

    // --- Debug ------------------------------------------------------------------------------

    [Fact]
    public void Every_type_has_a_debug_unasked() =>
        Allowed("""
            struct P { x: int, o: ?int, xs: int[] }
            enum E { A, B(int) }
            fn f(p: P, e: E): string { return p.debug() + e.debug(); }
            """);

    [Fact]
    public void Display_on_request_is_the_debug_form() =>
        Allowed("struct P :: [Display] { x: int }\nfn f(p: P): string { return p.show(); }\n");

    [Fact]
    public void The_implicit_debug_is_withdrawn_where_a_field_has_none()
    {
        // A class holding an interface value and a function compiles — and has no debug().
        Allowed("class H { w: Walker, f: fn() -> int }\nfn f(h: H): int { return h.f(); }\n");
        Rejected("class H { w: Walker }\nfn f(h: H): string { return h.debug(); }\n", "LYR-SEM0012");
    }

    [Fact]
    public void The_withdrawal_reaches_the_types_that_render_it() =>
        Rejected("class H { w: Walker }\nstruct O { h: H }\nfn f(o: O): string { return o.debug(); }\n", "LYR-SEM0012");

    [Fact]
    public void A_requested_debug_names_the_field_instead() =>
        Assert.Contains("in the 'Debug' synthesized for 'H'",
            Rejected("class H :: [Debug] { w: Walker }\n", "LYR-SEM0012").Message);

    // --- static members of a generic block through the type path (S7a leftover) ------------

    [Fact]
    public void A_static_of_a_generic_block_answers_in_the_instance() =>
        Allowed("""
            struct Box<T> { v: T }
            extend<T :: [Default]> Box<T> { static fn empty(): Box<T> { return Box<T> { v = T.default() }; } }
            fn f(): int { let b: Box<int> = Box<int>.empty(); return b.v; }
            """);
}
