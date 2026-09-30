using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A tuple's elements in Lyric 5 (design/v5/spec/03 T16): read by position, <c>.0</c>, and by
/// the label a written type gives them, <c>.x</c>; the labels are no part of the type —
/// <c>(x: int, y: int)</c> and <c>(int, int)</c> are one type — and no element is written.
/// </summary>
public class TupleElementTests
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

    [Fact]
    public void Elements_are_read_by_position() =>
        Allowed("fn f(t: (int, string), n: ((int, int), bool)): int { return t.0 + n.0.1 + (if (n.1) 1 else 0) + (if (t.1 == \"\") 1 else 0); }");

    [Fact]
    public void Elements_are_read_by_label_and_by_position_alike() =>
        Allowed("""
            struct S { pos: (x: int, y: int) }
            fn f(p: (x: int, y: int), s: S): int { return p.x + p.1 + s.pos.y + s.pos.0; }
            """);

    [Fact]
    public void A_label_is_no_part_of_the_type() =>
        Allowed("""
            fn labelled(p: (x: int, y: int)): int { return p.x; }
            fn plain(p: (int, int)): int { return p.0; }
            fn f(): int {
                let a: (x: int, y: int) = (1, 2);
                let b: (int, int) = a;
                let c: (u: int, v: int) = b;
                return labelled((3, 4)) + plain(a) + labelled(c) + c.u;
            }
            """);

    [Fact]
    public void A_missing_element_names_the_positions()
    {
        Assert.Contains("'.0' to '.1'", Rejected("fn f(t: (int, int)): int { return t.2; }", "LYR-SEM0012"));
        Assert.Contains("and their labels", Rejected("fn f(t: (x: int, y: int)): int { return t.z; }", "LYR-SEM0012"));
    }

    [Fact]
    public void A_label_of_another_type_is_not_seen() =>
        Rejected("fn f(): int { let a: (x: int, y: int) = (1, 2); let b: (int, int) = a; return b.x; }", "LYR-SEM0012");

    [Fact]
    public void No_element_is_written() =>
        Rejected("fn f(): int { var t = (1, 2); t.0 = 5; return t.0; }", "LYR-SEM0019");

    [Fact]
    public void A_label_twice_is_refused()
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", "fn f(t: (x: int, x: int)): int { return 0; }");
        var de = new DiagnosticEngine(sm);
        new Parser(sm, id, de).ParseModule();
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0010" && d.Message.Contains("'x' is given twice"));
    }
}
