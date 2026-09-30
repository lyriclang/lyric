using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>p with { x = 3 }</c> (design/v5/spec/02 M6): a copy of a struct with the named fields
/// replaced — on a struct only, a path reaching through structs held by value, every field
/// named once and existing, no <c>var</c> needed, the result the struct's type, never a statement.
/// </summary>
public class WithTests
{
    private const string Prelude = """
        struct Point { x: int, y: int }
        struct Line { from: Point, to: Point }
        class Holder { var at: Point }
        struct Pair<T> { first: T, second: T }

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
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    [Fact]
    public void A_with_is_a_copy_of_the_structs_type() =>
        Allowed("""
            fn f(p: Point, l: Line, h: Holder): Point {
                let q: Point = p with { x = 3 };
                let both = p with { x = p.y, y = p.x };
                let bent: Line = l with { to.x = 9, from = q };
                h.at = h.at with { y = 1 };
                let pair = Pair<int> { first = 1, second = 2 } with { second = 5 };
                return (p with { x = 5 }) with { y = both.y + bent.to.x + pair.second };
            }
            """);

    [Fact]
    public void No_var_is_needed()
    {
        // 'x' is a fixed field, and 'p' a parameter: nothing is written in place.
        Allowed("fn f(p: Point): Point { return p with { x = 1 }; }");
    }

    [Fact]
    public void A_class_is_refused() =>
        Assert.Contains("a class", Rejected("fn f(h: Holder): Holder { return h with { at = Point { x = 1, y = 2 } }; }", "LYR-SEM0116"));

    [Fact]
    public void A_tuple_is_refused() =>
        Assert.Contains("a tuple", Rejected("fn f(t: (int, int)): (int, int) { return t with { x = 1 }; }", "LYR-SEM0116"));

    [Fact]
    public void A_field_the_struct_does_not_have_is_refused() =>
        Assert.Contains("no field 'z'", Rejected("fn f(p: Point): Point { return p with { z = 1 }; }", "LYR-SEM0015"));

    [Fact]
    public void A_field_twice_is_refused() =>
        Rejected("fn f(p: Point): Point { return p with { x = 1, x = 2 }; }", "LYR-SEM0070");

    [Fact]
    public void A_path_reaches_through_structs_only() =>
        Assert.Contains("not a struct", Rejected("fn f(p: Point): Point { return p with { x.y = 1 }; }", "LYR-SEM0116"));

    [Fact]
    public void The_value_takes_the_fields_type() =>
        Rejected("fn f(p: Point): Point { return p with { x = \"one\" }; }", "LYR-SEM0001");

    [Fact]
    public void A_with_as_a_statement_has_no_effect() =>
        Rejected("fn f(p: Point): int { p with { x = 1 }; return 0; }", "LYR-SEM0022");
}
