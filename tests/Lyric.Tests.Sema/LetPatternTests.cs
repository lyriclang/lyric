using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// The rules of the binding forms (4.5): a <c>let … else</c> needs its else exactly when the
/// pattern can fail (LYR-SEM0098), the else has to leave (LYR-SEM0098), a pattern that never
/// fails in an if-let/while-let/let-else is a warning (LYR-SEM0104), a <c>let</c> condition
/// lives in an if or while head only, and the names are in scope where the pattern holds.
/// </summary>
public class LetPatternTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        Semantics.Analyze(comp, binding, de);
        return de;
    }

    private const string Shapes = "enum Shape { Circle(int), Rect { w: int, h: int }, Empty, }\n";

    private static void AssertClean(DiagnosticEngine de) =>
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    [Fact]
    public void A_refutable_let_pattern_without_else_is_refused()
    {
        var de = Check(Shapes + "fn f(s: Shape): int { let Circle(r) = s; return r; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0098" && d.Message.Contains("can fail"));
    }

    [Fact]
    public void An_else_that_does_not_leave_is_refused()
    {
        var de = Check(Shapes + "fn f(s: Shape): int { let Circle(r) = s else { let z = 1; }; return r; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0098" && d.Message.Contains("must leave"));
    }

    [Theory]
    [InlineData("return 0;")]
    [InlineData("panic(\"no\");")]
    public void An_else_that_leaves_is_accepted(string exit)
    {
        var de = Check(Shapes + $"fn f(s: Shape): int {{ let Circle(r) = s else {{ {exit} }}; return r; }}");
        AssertClean(de);
    }

    [Fact]
    public void Break_and_continue_leave_an_else_inside_a_loop()
    {
        var de = Check(Shapes + "fn f(xs: Shape[]): int { var n = 0; for (s in xs) { let Circle(r) = s else { continue; }; n = n + r; } return n; }");
        AssertClean(de);
    }

    [Fact]
    public void An_irrefutable_pattern_with_else_warns()
    {
        var de = Check("fn f(): int { let x: int = 5 else { return 0; }; return x; }");
        AssertClean(de);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0104");
    }

    [Fact]
    public void An_irrefutable_let_pattern_needs_no_else()
    {
        var de = Check("struct P { x: int, y: int } fn f(p: P): int { let P { x, y } = p; return x + y; }");
        AssertClean(de);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0104");
    }

    [Fact]
    public void Let_else_over_an_optional_binds_the_inner_type()
    {
        var de = Check("fn f(o: ?int): int { let v = o else { return 0; }; return v + 1; }");
        AssertClean(de);
    }

    [Fact]
    public void If_let_binds_only_in_the_then_branch()
    {
        var de = Check("fn f(o: ?int): int { if (let x = o) { return x; } else { return x; } }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0002" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void If_let_names_are_gone_after_the_if()
    {
        var de = Check("fn f(o: ?int): int { if (let x = o) { return x; } return x; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0002");
    }

    [Fact]
    public void If_let_that_cannot_fail_warns()
    {
        var de = Check("fn f(n: int): int { if (let x = n) { return x; } return 0; }");
        AssertClean(de);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0104" && d.Message.Contains("'if let'"));
    }

    [Fact]
    public void While_let_binds_in_the_body_and_sees_the_narrowed_type()
    {
        var de = Check("""
            class C { n: int, fn next(): ?int { return null; } }
            fn f(c: C): int { var s = 0; while (let v = c.next()) { s = s + v; } return s; }
            """);
        AssertClean(de);
    }

    [Fact]
    public void While_let_over_an_enum_payload_is_accepted()
    {
        var de = Check(Shapes + "class F { fn pull(): Shape { return Shape.Empty; } } fn f(x: F): int { var s = 0; while (let Circle(r) = x.pull()) { s = s + r; } return s; }");
        AssertClean(de);
    }

    [Fact]
    public void Let_else_names_count_as_assigned_afterwards()
    {
        // Definite assignment (§7.7): the else leaves, so the name is bound on the only path out.
        var de = Check(Shapes + "fn f(s: Shape): int { let Circle(r) = s else { return 0; }; let t = r * 2; return t; }");
        AssertClean(de);
    }

    [Fact]
    public void A_throwing_initializer_in_a_let_else_is_seen_by_the_exception_analysis()
    {
        var de = Check("""
            fn risky(): ?int throws Exception { return 1; }
            fn f(): int { let v = risky() else { return 0; }; return v; }
            """);
        // 'f' does not declare throws: the initializer's throw must be reported, not lost.
        Assert.Contains(de.Diagnostics, d => d.Code.StartsWith("LYR-SEM", StringComparison.Ordinal) && d.Message.Contains("throw"));
    }
}
