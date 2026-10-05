using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A NEGATED test narrows (design/v5/spec/03 T4 O3, T11; the review's work list): <c>!c</c>
/// proves what <c>c</c> refutes and refutes what it proves. So
/// <c>if (!(s is Circle)) { return; }</c> guards — a type test had no guard form but the empty
/// branch, <c>if (s is Circle) { } else { return; }</c> — and <c>if (!(x != null)) { return; }</c>
/// does what <c>if (x == null) { return; }</c> does. The negation proved nothing: the name kept
/// its type behind the guard (<c>LYR-SEM0012</c>, <c>LYR-SEM0003</c>).
/// </summary>
public class NegatedTestNarrowingTests
{
    private const string Shapes = """
        interface Shape { fn area(): float; }
        class Circle :: [Shape] { radius: float, fn area(): float { return 3.0 * this.radius * this.radius; } }
        class Square :: [Shape] { side: float, fn area(): float { return this.side * this.side; } }

        """;

    private static List<Diagnostic> Errors(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Shapes + source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de, singleProgram: false);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
    }

    private static void Allowed(string source)
    {
        var errors = Errors(source);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static void Rejected(string source, string code)
    {
        var errors = Errors(source);
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    // ------------------------------------------------------------------ a guard

    [Theory]
    // behind a guard that leaves
    [InlineData("fn f(x: ?int): int { if (!(x != null)) { return 0; } return x + 1; }")]
    [InlineData("fn f(s: Shape): float { if (!(s is Circle)) { return -1.0; } return s.radius; }")]
    // in the branch the negation guards
    [InlineData("fn f(x: ?int): int { if (!(x == null)) { return x + 1; } return 0; }")]
    [InlineData("fn f(x: ?int): int { if (!(x == null)) { return x + 1; } else { return 0; } }")]
    // in the other branch
    [InlineData("fn f(x: ?int): int { if (!(x != null)) { return 0; } else { return x + 1; } }")]
    [InlineData("fn f(s: Shape): float { if (!(s is Circle)) { return -1.0; } else { return s.radius; } }")]
    // twice is once
    [InlineData("fn f(x: ?int): int { if (!!(x != null)) { return x * 2; } return 0; }")]
    // the 'if' expression
    [InlineData("fn f(x: ?int): int { return if (!(x == null)) x else 0; }")]
    [InlineData("fn f(x: ?int): int { return if (!(x != null)) 0 else x; }")]
    // the head of a loop
    [InlineData("fn f(x: ?int): int { var left = x; var n = 0; while (!(left == null)) { n += left; left = null; } return n; }")]
    public void A_negated_test_narrows(string function) => Allowed(function);

    /// <summary>De Morgan, by the rule alone: a negated conjunction refutes both sides where it
    /// is false, a negated disjunction proves both where it is true.</summary>
    [Theory]
    [InlineData("fn f(a: ?int, b: ?int): int { if (!(a != null && b != null)) { return -1; } return a + b; }")]
    [InlineData("fn f(a: ?int, b: ?int): int { if (!(a == null || b == null)) { return a + b; } return -1; }")]
    [InlineData("fn f(a: ?int, s: Shape): float { if (!(a != null && s is Circle)) { return -1.0; } return s.radius + (a as float); }")]
    // the left side of '&&' as a negation: the right side runs under what it proves
    [InlineData("fn f(a: ?int): bool { return !(a == null) && a > 0; }")]
    [InlineData("fn f(a: ?int): bool { return !(a != null) || a > 0; }")]
    public void A_negation_around_a_composite_condition_narrows(string function) => Allowed(function);

    // ------------------------------------------------------------------ what it does not prove

    [Theory]
    // in the branch where the value IS null
    [InlineData("fn f(x: ?int): int { if (!(x != null)) { return x + 1; } return 0; }", "LYR-SEM0003")]
    // a negated type test proves nothing in its own branch
    [InlineData("fn f(s: Shape): float { if (!(s is Circle)) { return s.radius; } return 0.0; }", "LYR-SEM0012")]
    // behind a guard that does not leave
    [InlineData("fn f(x: ?int): int { if (!(x != null)) { } return x + 1; }", "LYR-SEM0003")]
    // a negated conjunction proves neither side in its own branch
    [InlineData("fn f(a: ?int, b: ?int): int { if (!(a != null && b != null)) { return a + 1; } return 0; }", "LYR-SEM0003")]
    // a negated disjunction refutes neither side behind it
    [InlineData("fn f(a: ?int, b: ?int): int { if (!(a == null || b == null)) { return 0; } return a + 1; }", "LYR-SEM0003")]
    // the type test's other branch stays unknown: 'not a Circle' is no type
    [InlineData("fn f(s: Shape): float { if (s is Circle) { return 0.0; } return s.radius; }", "LYR-SEM0012")]
    public void A_negated_test_proves_no_more_than_it_says(string function, string code) => Rejected(function, code);
}
