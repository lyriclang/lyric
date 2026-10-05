using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// One method set per type (design/v5/spec/04 D2, D3), the qualified call <c>I.m(x)</c> (R5),
/// delegation <c>:: [I by field]</c> (D1), and what an extend block may not do (D15, X3).
/// </summary>
public class MethodSetTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string source, string code)
    {
        var errors = Check(source).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ one name, one function

    [Fact]
    public void An_extension_cannot_add_a_name_the_type_has() =>
        Assert.Contains("member of 'V' already", Rejected("""
            struct V { x: int, fn tag(): int { return 1; } }
            extend V { fn tag(): int { return 2; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0121"));

    [Fact]
    public void Two_extensions_cannot_add_one_name() =>
        Assert.Contains("twice", Rejected("""
            struct V { x: int }
            extend V { fn tag(): int { return 1; } }
            extend V { fn tag(): int { return 2; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0121"));

    [Fact]
    public void An_extension_cannot_shadow_a_default() =>
        Assert.Contains("default", Rejected("""
            interface Tagged { fn tag(): int { return 1; } }
            struct V :: [Tagged] { x: int }
            extend V { fn tag(): int { return 2; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0121"));

    [Fact]
    public void An_own_member_overrides_a_default_and_is_the_implementation_for_every_conformance() =>
        Allowed("""
            interface A { fn tag(): int { return 1; } }
            interface B { fn tag(): int; }
            struct V :: [A, B] { x: int, fn tag(): int { return 3; } }
            fn main(): int { return V { x = 1 }.tag(); }
            """);

    [Fact]
    public void Two_defaults_nobody_overrides_are_the_types_to_settle() =>
        Assert.Contains("one function for both", Rejected("""
            interface A { fn tag(): int { return 1; } }
            interface B { fn tag(): int { return 2; } }
            struct V :: [A, B] { x: int }
            fn main(): int { return 0; }
            """, "LYR-SEM0043"));

    // ------------------------------------------------------------------ conformance blocks and the qualified call

    private const string Blocks = """
        interface Greeter { fn greet(): string; }
        interface Waver { fn greet(): string; }
        struct Person { name: string }
        extend Person :: [Greeter] { fn greet(): string { return "hello"; } }
        extend Person :: [Waver] { fn greet(): string { return "hi"; } }

        """;

    [Fact]
    public void Two_conformance_blocks_may_each_implement_one_name() =>
        Allowed(Blocks + "fn main(): int { let p = Person { name = \"x\" }; let g: Greeter = p; let w: Waver = p; return if (g.greet() == \"hello\" && w.greet() == \"hi\") 0 else 1; }");

    [Fact]
    public void The_unqualified_call_is_refused_and_says_how_to_qualify()
    {
        var message = Rejected(Blocks + "fn main(): int { let p = Person { name = \"x\" }; return if (p.greet() == \"hello\") 0 else 1; }", "LYR-SEM0122");
        Assert.Contains("Greeter.greet(", message);
    }

    [Fact]
    public void The_qualified_call_reaches_the_implementation_for_that_interface() =>
        Allowed(Blocks + "fn main(): int { let p = Person { name = \"x\" }; return if (Greeter.greet(p) == \"hello\" && Waver.greet(p) == \"hi\") 0 else 1; }");

    private const string Bumper = """
        interface Counter { mut fn bump(): void; }
        struct C :: [Counter] { var n: int, mut fn bump(): void { this.n += 1; } }

        """;

    /// <summary>The qualified call is the member call it stands for (05 §4): a <c>mut fn</c>
    /// writes its receiver, the first argument, which is a place as <c>c.bump()</c>'s is. The
    /// rule read the call as it was written and saw an argument.</summary>
    [Theory]
    [InlineData("let c = C { n = 0 }; Counter.bump(c);", "bound with 'let'")]
    [InlineData("Counter.bump(C { n = 0 });", "temporary")]
    public void The_qualified_call_of_a_mut_fn_needs_a_place(string body, string because) =>
        Assert.Contains(because, Rejected(Bumper + "fn main(): int { " + body + " return 0; }", "LYR-SEM0019"));

    [Fact]
    public void The_qualified_call_of_a_mut_fn_writes_a_var() =>
        Allowed(Bumper + "fn main(): int { var c = C { n = 0 }; Counter.bump(c); return c.n; }");

    /// <summary>… and the <c>var</c> it writes is written: no hint that <c>let</c> would do.</summary>
    [Fact]
    public void A_var_written_through_the_qualified_call_is_not_called_unchanged()
    {
        var hints = Check(Bumper + "fn main(): int { var c = C { n = 0 }; Counter.bump(c); return c.n; }")
            .Diagnostics.Where(d => d.Code == "LYR-SEM0075").ToList();
        Assert.True(hints.Count == 0, string.Join("\n", hints.Select(d => d.Message)));
    }

    /// <summary>Control: the hint is there for a var nothing writes.</summary>
    [Fact]
    public void A_var_nothing_writes_keeps_its_hint() =>
        Assert.Single(Check(Bumper + "fn main(): int { var c = C { n = 0 }; return c.n; }")
            .Diagnostics, d => d.Code == "LYR-SEM0075");

    /// <summary>A delegated member is forwarded "as if it were written" (05 §5): a <c>mut fn</c>
    /// forwarded to a struct in a field that is no <c>var</c> would write what cannot be
    /// written. Said where the type delegates.</summary>
    [Fact]
    public void A_mut_fn_is_not_delegated_to_a_struct_in_a_field_that_is_no_var() =>
        Assert.Contains("to 'inner', a value in a field that is no 'var'", Rejected("""
            interface Counter { mut fn bump(): void; }
            struct Inner :: [Counter] { var n: int, mut fn bump(): void { this.n += 1; } }
            struct Outer :: [Counter by inner] { inner: Inner }
            fn main(): int { return 0; }
            """, "LYR-SEM0019"));

    /// <summary>Controls: a <c>var</c> field; a class in a field that is no <c>var</c> — a
    /// reference, which a <c>mut fn</c> writes through; a member that is no <c>mut fn</c>.</summary>
    [Theory]
    [InlineData("struct Inner :: [Counter] { var n: int, mut fn bump(): void { this.n += 1; } }\nstruct Outer :: [Counter by inner] { var inner: Inner }")]
    [InlineData("class Inner :: [Counter] { var n: int, mut fn bump(): void { this.n += 1; } }\nstruct Outer :: [Counter by inner] { inner: Inner }")]
    public void A_mut_fn_is_delegated_to_what_can_be_written(string types) =>
        Allowed("interface Counter { mut fn bump(): void; }\n" + types + "\nfn main(): int { return 0; }");

    [Fact]
    public void The_qualified_call_takes_the_receiver_first() =>
        Rejected(Blocks + "fn main(): int { let s = Greeter.greet(); return 0; }", "LYR-SEM0014");

    [Fact]
    public void The_qualified_call_needs_a_conforming_receiver() =>
        Assert.Contains("does not conform", Rejected(Blocks + "fn main(): int { let s = Greeter.greet(3); return 0; }", "LYR-SEM0125"));

    [Fact]
    public void A_block_cannot_redeclare_an_own_member() =>
        Rejected("""
            interface Greeter { fn greet(): string; }
            struct Person :: [Greeter] { name: string, fn greet(): string { return "a"; } }
            extend Person { fn greet(): string { return "b"; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0121");

    // ------------------------------------------------------------------ delegation

    private const string Walking = """
        interface Walker { fn walk(): string; fn speed(): int; fn pace(): string { return this.walk(); } }
        class Legs :: [Walker] { var k: int, fn walk(): string { return "walk"; } fn speed(): int { return this.k; } }

        """;

    [Fact]
    public void A_delegated_interface_is_satisfied_by_the_field() =>
        Allowed(Walking + """
            class Dog :: [Walker by legs] { var legs: Legs }
            fn main(): int { let d = Dog { legs = Legs { k = 1 } }; let w: Walker = d; return d.speed() + w.speed() + (if (d.walk() == d.pace()) 0 else 1); }
            """);

    [Fact]
    public void An_own_member_wins_over_the_delegation() =>
        Allowed(Walking + """
            class Dog :: [Walker by legs] { var legs: Legs, fn speed(): int { return 4; } }
            fn main(): int { return Dog { legs = Legs { k = 1 } }.speed(); }
            """);

    [Fact]
    public void A_field_of_the_interface_type_may_be_delegated_to() =>
        Allowed(Walking + """
            class Dog :: [Walker by legs] { var legs: Walker }
            fn main(): int { let d = Dog { legs = Legs { k = 1 } }; return d.speed(); }
            """);

    [Fact]
    public void The_field_must_exist() =>
        Assert.Contains("no field", Rejected(Walking + "class Dog :: [Walker by paws] { var legs: Legs }\nfn main(): int { return 0; }", "LYR-SEM0123"));

    [Fact]
    public void The_field_must_conform() =>
        Assert.Contains("does not conform", Rejected(Walking + "class Dog :: [Walker by legs] { var legs: int }\nfn main(): int { return 0; }", "LYR-SEM0123"));

    [Fact]
    public void One_name_is_not_delegated_to_two_fields() =>
        Assert.Contains("delegated twice", Rejected(Walking + """
            interface Runner { fn walk(): string; }
            class Wheels :: [Runner] { fn walk(): string { return "run"; } }
            class Dog :: [Walker by legs, Runner by wheels] { var legs: Legs, var wheels: Wheels }
            fn main(): int { return 0; }
            """, "LYR-SEM0121"));

    [Fact]
    public void By_has_no_place_on_an_enum() =>
        Rejected("interface I { fn f(): int; }\nenum E :: [I by x] { A; fn f(): int { return 1; } }\nfn main(): int { return 0; }", "LYR-PAR0047");

    // ------------------------------------------------------------------ what an extend block may not do

    [Fact]
    public void An_interface_is_not_extended_directly() =>
        Assert.Contains("extend<T :: [Walker]> T", Rejected(Walking + "extend Walker { fn describe(): string { return \"w\"; } }\nfn main(): int { return 0; }", "LYR-SEM0124"));
}
