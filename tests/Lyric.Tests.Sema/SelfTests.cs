using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>Self</c> in an interface and in a type (design/v5/spec/03 T5), static interface members
/// through a constraint (T5), default type arguments (T18), and the interface that is a
/// constraint only (04 D9).
/// </summary>
public class SelfTests
{
    private const string Prelude = """
        interface Equatable { fn equals(o: Self): bool; }
        interface Parse { static fn parse(s: string): Self; }
        interface Add<Rhs = Self> { fn add(o: Rhs): Self; }
        interface Show { fn show(): string; }
        struct P :: [Equatable, Parse, Add, Show] {
            n: int,
            fn equals(o: P): bool { return this.n == o.n; }
            static fn parse(s: string): P { return P { n = 1 }; }
            fn add(o: P): P { return P { n = this.n + o.n }; }
            fn show(): string { return "p"; }
            fn twin(): Self { return P { n = this.n }; }
        }
        struct Q :: [Add<int>] { n: int, fn add(o: int): Q { return Q { n = this.n + o }; } }

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

    // ------------------------------------------------------------------ Self

    [Fact]
    public void Self_in_an_interface_is_the_conforming_type() =>
        Allowed("fn f(): bool { let p = P { n = 1 }; return p.equals(p.twin()); }");

    [Fact]
    public void An_implementation_names_its_own_type_where_the_interface_says_Self() =>
        Assert.Contains("expected 'R'", Rejected("struct R :: [Equatable] { n: int, fn equals(o: int): bool { return true; } }\nfn f(): int { return 0; }", "LYR-SEM0042"));

    [Fact]
    public void Self_through_a_constraint_is_the_type_parameter() =>
        Allowed("fn same<T :: [Equatable]>(a: T, b: T): bool { return a.equals(b); }\nfn f(): bool { return same(P { n = 1 }, P { n = 2 }); }");

    [Fact]
    public void Self_in_a_type_body_is_the_type() =>
        Allowed("class Node { var next: ?Self, fn me(): Self { return this; } }\nfn f(): int { let n = Node { next = null }; return if (n.me().next == null) 0 else 1; }");

    [Fact]
    public void Self_of_a_generic_type_is_its_own_instance() =>
        Allowed("struct Box<T> { v: T, fn same(): Self { return Box<T> { v = this.v }; } }\nfn f(): int { return Box<int> { v = 3 }.same().v; }");

    // ------------------------------------------------------------------ static members through a constraint

    [Fact]
    public void A_static_interface_member_is_reached_through_the_type_parameter() =>
        Allowed("fn make<T :: [Parse]>(s: string): T { return T.parse(s); }\nfn f(): int { let p: P = make(\"x\"); return p.n; }");

    [Fact]
    public void A_static_member_is_implemented_by_a_static_member() =>
        Assert.Contains("static", Rejected("struct R :: [Parse] { n: int, fn parse(s: string): R { return R { n = 1 }; } }\nfn f(): int { return 0; }", "LYR-SEM0042"));

    [Fact]
    public void A_static_interface_member_declares_only() =>
        Rejected("interface Bad { static fn make(): int { return 1; } }\nfn f(): int { return 0; }", "LYR-SEM0127");

    [Fact]
    public void A_static_member_is_not_called_on_a_value_of_the_parameter() =>
        Assert.Contains("T.parse", Rejected("fn make<T :: [Parse]>(t: T): T { return t.parse(\"x\"); }\nfn f(): int { return 0; }", "LYR-SEM0055"));

    // ------------------------------------------------------------------ default type arguments

    [Fact]
    public void A_default_type_argument_fills_what_is_not_written() =>
        Allowed("fn plus<T :: [Add]>(a: T, b: T): T { return a.add(b); }\nfn f(): int { return plus(P { n = 1 }, P { n = 2 }).n; }");

    [Fact]
    public void The_default_is_Self_at_the_conformance_and_a_written_argument_beats_it() =>
        Allowed("fn f(): int { return Q { n = 1 }.add(2).n + P { n = 1 }.add(P { n = 2 }).n; }");

    [Fact]
    public void A_written_conformance_satisfies_the_short_constraint() =>
        Allowed("struct V :: [Add<V>] { n: int, fn add(o: V): V { return V { n = this.n + o.n }; } }\nfn plus<T :: [Add]>(a: T, b: T): T { return a.add(b); }\nfn f(): int { return plus(V { n = 1 }, V { n = 2 }).n; }");

    [Fact]
    public void A_default_on_a_class_parameter_fills_too() =>
        Allowed("class Map<K, V, H = int> { var k: K, var v: V, var h: H }\nfn f(): int { let m: Map<string, bool> = Map<string, bool> { k = \"a\", v = true, h = 2 }; return m.h; }");

    // ------------------------------------------------------------------ a constraint only (D9)

    [Fact]
    public void An_interface_naming_Self_is_no_type_for_a_value() =>
        Assert.Contains("'Self'", Rejected("fn f(): int { let e: Equatable = P { n = 1 }; return 0; }", "LYR-SEM0126"));

    [Fact]
    public void An_interface_with_a_static_member_is_no_type_for_a_value() =>
        Assert.Contains("static", Rejected("fn f(): int { let e: Parse = P { n = 1 }; return 0; }", "LYR-SEM0126"));

    [Fact]
    public void An_interface_without_either_is_a_value() =>
        Allowed("fn f(): string { let s: Show = P { n = 1 }; return s.show(); }");
}
