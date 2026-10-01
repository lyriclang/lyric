using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Generic extends (design/v5/spec/03 T7): <c>extend&lt;T&gt; List&lt;T&gt;</c>, the conditional
/// conformance <c>extend&lt;T :: [Display]&gt; List&lt;T&gt; :: [Display]</c>, a block on one
/// instance, and coherence — one conformance per type instance and interface, no specialization.
/// </summary>
public class GenericExtendTests
{
    private const string Core = """
        module std.core;
        pub interface Display { fn show(): string; }
        extend int :: [Display] { fn show(): string { return "i"; } }
        extend string :: [Display] { fn show(): string { return this; } }

        """;

    private const string Prelude = """
        struct Foo { n: int }
        struct Box<T> { v: T }
        class List<T> { items: T[] }
        extend<T> List<T> { fn first(): T { return this.items[0]; } }
        extend<T :: [Display]> List<T> :: [Display] { fn show(): string { return "list"; } }

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

    // ------------------------------------------------------------------ members

    [Fact]
    public void A_block_parameter_is_bound_by_the_receiver() =>
        Allowed("fn f(a: List<int>, b: List<string>): string { let n: int = a.first(); return b.first(); }");

    [Fact]
    public void The_bound_parameter_is_checked_like_any() =>
        Rejected("fn f(a: List<int>): string { return a.first(); }", "LYR-SEM0001");

    [Fact]
    public void A_constrained_block_adds_its_member_where_the_constraint_holds() =>
        Allowed("fn f(a: List<int>): string { return a.show(); }");

    [Fact]
    public void A_constrained_block_adds_nothing_where_the_constraint_fails() =>
        Assert.Contains("does not satisfy", Rejected("fn f(a: List<Foo>): string { return a.show(); }", "LYR-SEM0134"));

    [Fact]
    public void A_block_on_one_instance_adds_to_that_instance_alone() =>
        Rejected("extend Box<int> { fn doubled(): int { return this.v * 2; } }\nfn f(b: Box<string>): int { return b.doubled(); }", "LYR-SEM0012");

    [Fact]
    public void Two_block_parameters() =>
        Allowed("struct Pair<A, B> { a: A, b: B }\nextend<A, B> Pair<A, B> { fn swap(): Pair<B, A> { return Pair<B, A> { a = this.b, b = this.a }; } }\n"
                + "fn f(p: Pair<int, string>): Pair<string, int> { return p.swap(); }");

    // ------------------------------------------------------------------ conditional conformance

    [Fact]
    public void The_conformance_holds_where_the_constraint_does() =>
        Allowed("fn f(a: List<int>): Display { return a; }");

    [Fact]
    public void The_conformance_fails_where_the_constraint_does() =>
        Rejected("fn f(a: List<Foo>): Display { return a; }", "LYR-SEM0001");

    [Fact]
    public void A_block_on_one_instance_conforms_that_instance_alone() =>
        Rejected("interface Named { fn name(): string; }\nextend Box<int> :: [Named] { fn name(): string { return \"box\"; } }\n"
                 + "fn f(b: Box<string>): Named { return b; }", "LYR-SEM0001");

    [Fact]
    public void A_constraint_call_reaches_the_block() =>
        Allowed("fn render<T :: [Display]>(x: T): string { return x.show(); }\nfn f(a: List<int>): string { return render(a); }");

    // ------------------------------------------------------------------ coherence

    [Fact]
    public void A_concrete_block_beside_the_generic_one_is_refused() =>
        Assert.Contains("twice", Rejected("extend List<int> :: [Display] { fn show(): string { return \"ints\"; } }\nfn f(): int { return 0; }", "LYR-SEM0133"));

    [Fact]
    public void A_block_beside_the_types_own_declaration_is_refused() =>
        Assert.Contains("twice", Rejected("struct S :: [Display] { n: int, fn show(): string { return \"s\"; } }\nextend S :: [Display] { }\nfn f(): int { return 0; }", "LYR-SEM0133"));

    [Fact]
    public void Two_blocks_on_different_instances_stand() =>
        Allowed("interface Named { fn name(): string; }\nextend Box<int> :: [Named] { fn name(): string { return \"i\"; } }\nextend Box<string> :: [Named] { fn name(): string { return \"s\"; } }\nfn f(): int { return 0; }");

    [Fact]
    public void A_parent_written_out_beside_its_child_is_one_conformance() =>
        Allowed("interface P { fn p(): int; }\ninterface C :: [P] { fn c(): int; }\nstruct S :: [P, C] { n: int, fn p(): int { return 1; } fn c(): int { return 2; } }\nfn f(): int { return 0; }");

    // ------------------------------------------------------------------ the target

    [Fact]
    public void An_array_target_is_not_one_yet() =>
        Rejected("extend<T> T[] { fn len(): int { return 0; } }\nfn f(): int { return 0; }", "LYR-SEM0047");
}
