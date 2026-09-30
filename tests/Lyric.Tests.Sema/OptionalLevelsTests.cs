using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Optionals in Lyric 5 (design/v5/spec/03 T4): <c>??T</c> is a type (O1), a test narrows the
/// binding by one level from where it stands (O3), <c>?.</c> flattens while <c>??</c> and
/// <c>!</c> peel one level, and nothing is assigned through <c>?.</c> (O4). Lyric 4 refused the
/// nested optional — in the lowering, as "optionals do not nest" — and narrowed every test to
/// the same level, which was the whole story while there was only one.
/// </summary>
public class OptionalLevelsTests
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
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string source, string code)
    {
        var errors = Check(source).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ O1: '??T' is a type

    [Fact]
    public void An_optional_of_an_optional_is_a_type() =>
        Allowed("""
            fn lookup(key: int): ??int {
                if (key == 0) { return null; }
                let stored: ?int = if (key == 1) null else key;
                return stored;
            }
            fn main(): int {
                let a: ??int = 5;
                let b: ? ?int = null;
                let c: ???int = a;
                return if (lookup(7) != null && b == null && c != null) 0 else 1;
            }
            """);

    [Fact]
    public void Each_level_is_its_own_type()
    {
        // A '??int' is not a '?int': the way down is a test or '!', never silence.
        var message = Rejected("""
            fn main(): int { let a: ??int = 5; let b: ?int = a; return 0; }
            """, "LYR-SEM0001");
        Assert.Contains("'??int'", message);
        Assert.Contains("'?int'", message);
        // The way up is a coercion, one 'some' per level.
        Allowed("fn main(): int { let a: ?int = 5; let b: ??int = a; let c: ??int = 7; return 0; }");
    }

    // ------------------------------------------------------------------ O3: one level per test

    [Fact]
    public void A_test_narrows_by_one_level_from_where_it_stands() =>
        Allowed("""
            fn describe(v: ??int): int {
                if (v == null) { return -1; }
                let once: ?int = v;
                if (v == null) { return 0; }
                let twice: int = v;
                return twice + (once ?? 0);
            }
            fn main(): int { return describe(5); }
            """);

    [Fact]
    public void One_test_does_not_reach_the_value_of_a_nested_optional()
    {
        var message = Rejected("""
            fn f(v: ??int): int {
                if (v != null) { return v; }
                return 0;
            }
            fn main(): int { return f(5); }
            """, "LYR-SEM0001");
        Assert.Contains("'?int'", message);
    }

    [Fact]
    public void An_assignment_takes_the_narrowing_back() =>
        Rejected("""
            fn f(start: ?int): int {
                var v = start;
                if (v != null) {
                    v = null;
                    return v;
                }
                return 0;
            }
            fn main(): int { return f(5); }
            """, "LYR-SEM0001");

    // ------------------------------------------------------------------ O4: '?.', '??', '!'

    [Fact]
    public void A_chain_flattens_to_one_optional() =>
        Allowed("""
            class Node { value: int, var next: ?Node }
            fn main(): int {
                let n = Node { value = 1 };
                let v: ?int = n.next?.next?.value;
                let link: ?Node = n.next?.next;
                return (v ?? 0) + (if (link == null) 0 else 1);
            }
            """);

    [Fact]
    public void Coalesce_and_force_peel_one_level()
    {
        Allowed("""
            fn main(): int {
                let a: ??int = 5;
                let inner: ?int = a ?? null;
                let forced: ?int = a!;
                let value: int = a!!;
                return value + (inner ?? 0) + (forced ?? 0);
            }
            """);
        // One '!' on a '??int' is a '?int', not an 'int'.
        Rejected("fn main(): int { let a: ??int = 5; let v: int = a!; return v; }", "LYR-SEM0001");
    }

    [Fact]
    public void Nothing_is_assigned_through_an_optional_chain()
    {
        var message = Rejected("""
            class C { var n: int }
            fn main(): int { let c: ?C = C { n = 1 }; c?.n = 2; return 0; }
            """, "LYR-SEM0019");
        Assert.Contains("'?.'", message);
        // Narrowed, the same write is an ordinary one.
        Allowed("""
            class C { var n: int }
            fn main(): int { let c: ?C = C { n = 1 }; if (c != null) { c.n = 2; } return 0; }
            """);
    }

    [Fact]
    public void A_narrowed_optional_struct_is_written_in_place() =>
        Allowed("""
            struct P { var x: int }
            fn main(): int {
                var p: ?P = P { x = 1 };
                if (p != null) { p.x = 9; }
                return 0;
            }
            """);

    [Fact]
    public void A_narrowed_let_stays_a_let() =>
        Rejected("""
            struct P { var x: int }
            fn main(): int {
                let p: ?P = P { x = 1 };
                if (p != null) { p.x = 9; }
                return 0;
            }
            """, "LYR-SEM0019");
}
