using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The operator interfaces of Lyric 5 (design/v5/spec/04 D6): <c>Add&lt;Rhs = Self&gt;</c> with
/// its <c>Out</c>, the heterogeneous conformance in a block, <c>Neg</c>, <c>Equatable</c> and
/// <c>Ordered</c> behind <c>==</c> and <c>&lt;</c>, the bit operators, and the generic sum.
/// </summary>
public class OperatorInterfaceTests
{
    // The seed of std.core 5, as far as the operators go.
    private const string Core = """
        module std.core;
        pub interface Add<Rhs = Self> { type Out = Self; fn add(rhs: Rhs): Self.Out; }
        pub interface Sub<Rhs = Self> { type Out = Self; fn sub(rhs: Rhs): Self.Out; }
        pub interface Mul<Rhs = Self> { type Out = Self; fn mul(rhs: Rhs): Self.Out; }
        pub interface Rem<Rhs = Self> { type Out = Self; fn rem(rhs: Rhs): Self.Out; }
        pub interface Neg { type Out = Self; fn neg(): Self.Out; }
        pub interface BitAnd<Rhs = Self> { type Out = Self; fn bitAnd(rhs: Rhs): Self.Out; }
        pub interface BitNot { type Out = Self; fn bitNot(): Self.Out; }
        pub interface Shl<Rhs = int> { type Out = Self; fn shl(rhs: Rhs): Self.Out; }
        pub interface Equatable { fn equals(o: Self): bool; }
        pub enum Ordering { Less, Equal, Greater }
        pub interface Ordered :: [Equatable] { fn compare(o: Self): ?Ordering; }
        pub interface Display { fn show(): string; }
        extend int :: [Add, Equatable] {
            fn add(rhs: int): int { return this + rhs; }
            fn equals(o: int): bool { return this == o; }
        }

        """;

    private const string Prelude = """
        struct V :: [Add, Sub, Mul<int>, Neg, Equatable, Ordered, Display] {
            x: int,
            fn add(rhs: V): V { return V { x = this.x + rhs.x }; }
            fn sub(rhs: V): V { return V { x = this.x - rhs.x }; }
            fn mul(rhs: int): V { return V { x = this.x * rhs }; }
            fn neg(): V { return V { x = -this.x }; }
            fn equals(o: V): bool { return this.x == o.x; }
            fn compare(o: V): ?Ordering { return if (this.x < o.x) Ordering.Less else if (this.x > o.x) Ordering.Greater else Ordering.Equal; }
            fn show(): string { return "v"; }
        }
        extend V :: [Mul<float>] { type Out = float; fn mul(rhs: float): float { return 1.0; } }
        struct P { n: int }

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

    // ------------------------------------------------------------------ arithmetic

    [Fact]
    public void A_plus_b_is_a_dot_add_b() =>
        Allowed("fn f(a: V, b: V): V { return a + b - a; }");

    [Fact]
    public void The_operand_picks_the_conformance_and_the_answer_is_its_Out() =>
        Allowed("fn f(a: V): float { let w: V = a * 2; return a * 2.5; }");

    [Fact]
    public void A_heterogeneous_block_stands_beside_the_own_member() =>
        Allowed("fn f(a: V): int { return (a * 3).x; }");

    [Fact]
    public void Without_the_conformance_the_operator_is_refused() =>
        Assert.Contains("':: [Add]'", Rejected("fn f(a: P, b: P): P { return a + b; }", "LYR-SEM0003"));

    [Fact]
    public void Unary_minus_is_neg() =>
        Allowed("fn f(a: V): V { return -a; }");

    [Fact]
    public void Unary_minus_without_Neg_is_refused() =>
        Assert.Contains("'Neg'", Rejected("fn f(a: P): P { return -a; }", "LYR-SEM0003"));

    [Fact]
    public void Compound_assignment_derives_from_the_operator() =>
        Allowed("fn f(a: V, b: V): V { var acc = a; acc += b; return acc; }");

    [Fact]
    public void The_bit_operators_and_the_shifts_have_interfaces() =>
        Allowed("struct B :: [BitAnd, BitNot, Shl, Rem] { n: int, fn bitAnd(r: B): B { return r; } fn bitNot(): B { return this; } fn shl(r: int): B { return this; } fn rem(r: B): B { return r; } }\n"
                + "fn f(a: B, b: B): B { return (a & b) % ~b << 2; }");

    // ------------------------------------------------------------------ equality and ordering

    [Fact]
    public void Equality_goes_through_Equatable() =>
        Allowed("fn f(a: V, b: V): bool { return a == b && a != b; }");

    [Fact]
    public void Equality_without_Equatable_is_refused() =>
        Assert.Contains("':: [Equatable]'", Rejected("fn f(a: P, b: P): bool { return a == b; }", "LYR-SEM0059"));

    [Fact]
    public void The_orderings_go_through_Ordered() =>
        Allowed("fn f(a: V, b: V): bool { return a < b || a <= b || a > b || a >= b; }");

    [Fact]
    public void Ordering_without_Ordered_is_refused() =>
        Assert.Contains("?Ordering", Rejected("fn f(a: P, b: P): bool { return a < b; }", "LYR-SEM0003"));

    [Fact]
    public void Ordered_implies_Equatable() =>
        Rejected("struct Q :: [Ordered] { n: int, fn compare(o: Q): ?Ordering { return null; } }\nfn f(): int { return 0; }", "LYR-SEM0020");

    // ------------------------------------------------------------------ the generic sum

    [Fact]
    public void A_fixed_Out_makes_the_sum_its_own_type() =>
        Allowed("fn sum<T :: [Add<Out = T>]>(a: T, b: T): T { return a + b; }\nfn f(a: V): V { let n: int = sum(1, 2); return sum(a, a); }");

    [Fact]
    public void Without_the_fixation_the_result_is_T_Out() =>
        Assert.Contains("'T.Out'", Rejected("fn sum<T :: [Add]>(a: T, b: T): T { return a + b; }\nfn f(): int { return 0; }", "LYR-SEM0001"));

    [Fact]
    public void Std_core_types_are_visible_unasked() =>
        Allowed("fn f(a: V, b: V): bool { let o: ?Ordering = a.compare(b); return o == null; }");
}
