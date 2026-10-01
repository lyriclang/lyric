using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Associated types (design/v5/spec/03 T6): declared in an interface, answered by every
/// conformer, named as <c>T.Item</c> through a constraint and fixed there as
/// <c>Container&lt;Item = int&gt;</c>.
/// </summary>
public class AssociatedTypeTests
{
    private const string Prelude = """
        interface Container { type Item; fn first(): Self.Item; }
        interface Mapper { type Out = Self; fn mapped(): Self.Out; }
        struct IntBox :: [Container, Mapper] {
            type Item = int;
            v: int,
            fn first(): int { return this.v; }
            fn mapped(): IntBox { return this; }
        }
        struct StrBox { s: string }
        extend StrBox :: [Container] { type Item = string; fn first(): string { return this.s; } }
        fn firstOf<T :: [Container]>(c: T): T.Item { return c.first(); }
        fn sumFirst<T :: [Container<Item = int>]>(c: T): int { return c.first(); }

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
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ the answer

    [Fact]
    public void The_conformers_answer_is_the_type_through_a_constraint() =>
        Allowed("fn f(): int { return firstOf(IntBox { v = 1 }); }");

    [Fact]
    public void An_extend_block_answers_too() =>
        Allowed("fn f(): string { return firstOf(StrBox { s = \"x\" }); }");

    [Fact]
    public void The_answer_is_what_the_signature_wants() =>
        Assert.Contains("expected 'int'", Rejected(
            "struct Wrong :: [Container] { type Item = int; v: int, fn first(): string { return \"no\"; } }\nfn f(): int { return 0; }",
            "LYR-SEM0042"));

    [Fact]
    public void A_conformer_without_an_answer_is_refused_once() =>
        Assert.Contains("type Item = …", Rejected(
            "struct Mute :: [Container] { v: int, fn first(): int { return this.v; } }\nfn f(): int { return 0; }",
            "LYR-SEM0128"));

    [Fact]
    public void An_answer_no_interface_asks_for_is_refused() =>
        Assert.Contains("answers no interface", Rejected(
            "struct Stray { type Item = int; v: int }\nfn f(): int { return 0; }",
            "LYR-SEM0129"));

    // Through S6 a second block could repeat the answer; coherence (03 T7 X3) refuses the second
    // conformance itself, so the question of a second answer no longer arises.
    [Fact]
    public void A_second_block_of_the_same_conformance_is_a_duplicate() =>
        Rejected("extend IntBox :: [Container] { type Item = int; }\nfn f(): int { return 0; }", "LYR-SEM0133");

    [Fact]
    public void A_default_answers_where_the_conformer_says_nothing() =>
        Allowed("fn f(): IntBox { return IntBox { v = 1 }.mapped(); }");

    [Fact]
    public void A_generic_conformer_answers_with_its_parameter() =>
        Allowed("struct Pair<T> :: [Container] { type Item = T; a: T, b: T, fn first(): T { return this.a; } }\n"
                + "fn f(): int { return firstOf(Pair<int> { a = 3, b = 4 }); }");

    [Fact]
    public void In_a_type_body_Self_Item_is_the_types_own_answer() =>
        Allowed("struct Two :: [Container] { type Item = int; v: int, fn first(): Self.Item { return this.v; } fn again(): Self.Item { return this.first(); } }\n"
                + "fn f(): int { return Two { v = 2 }.again(); }");

    // ------------------------------------------------------------------ the path

    [Fact]
    public void An_associated_type_no_constraint_declares_is_refused() =>
        Assert.Contains("'Nope'", Rejected("fn f<T :: [Container]>(c: T): T.Nope { return c.first(); }\nfn g(): int { return 0; }", "LYR-SEM0128"));

    [Fact]
    public void An_unconstrained_parameter_has_no_associated_type() =>
        Assert.Contains("constrain it", Rejected("fn f<T>(c: T): T.Item { return c; }\nfn g(): int { return 0; }", "LYR-SEM0128"));

    [Fact]
    public void The_open_path_is_displayed_as_written() =>
        Assert.Contains("'T.Item'", Rejected("fn f<T :: [Container]>(c: T): T.Item { return 1; }\nfn g(): int { return 0; }", "LYR-SEM0001"));

    // ------------------------------------------------------------------ the fixation

    [Fact]
    public void A_fixation_at_the_constraint_makes_the_answer_concrete() =>
        Allowed("fn f(): int { return sumFirst(IntBox { v = 1 }); }");

    [Fact]
    public void A_conformer_with_another_answer_does_not_satisfy_the_fixation() =>
        Assert.Contains("Container<Item = int>", Rejected("fn f(): int { return sumFirst(StrBox { s = \"x\" }); }", "LYR-SEM0028"));

    [Fact]
    public void A_fixation_of_no_associated_type_is_refused() =>
        Assert.Contains("'Nope'", Rejected("fn s<T :: [Container<Nope = int>]>(c: T): int { return 0; }\nfn f(): int { return s(IntBox { v = 1 }); }", "LYR-SEM0128"));

    // ------------------------------------------------------------------ D9

    [Fact]
    public void An_interface_with_an_associated_type_is_a_constraint_only() =>
        Assert.Contains("associated type 'Item'", Rejected("fn g(c: Container): int { return 0; }\nfn f(): int { return g(IntBox { v = 1 }); }", "LYR-SEM0126"));
}
