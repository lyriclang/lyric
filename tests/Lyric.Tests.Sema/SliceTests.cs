using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>Slice&lt;T&gt;</c>, the view of an array (design/v5/spec/03 T13 A2): <c>xs[a..b]</c> and
/// the open forms take one of an array or of a view; it is indexed and measured like the array,
/// its elements are places in the array; an array stands where a view is expected; the rest of
/// an array pattern binds one. The name is a language primitive of <c>std.core</c>, visible
/// without an import (10 C2).
/// </summary>
public class SliceTests
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
    public void A_range_takes_a_view_in_every_open_form() =>
        Allowed("""
            fn f(xs: int[]): int {
                let a: Slice<int> = xs[1..3];
                let b: Slice<int> = xs[..2];
                let c: Slice<int> = xs[2..];
                let d: Slice<int> = xs[..];
                let e: Slice<int> = xs[1..=^1];
                let f: Slice<int> = a[1..];
                return a.length() + b[0] + c[^1] + d.length() + e.length() + f.length();
            }
            """);

    [Fact]
    public void An_array_stands_where_a_view_is_expected() =>
        Allowed("""
            fn total(xs: Slice<int>): int { return xs.length(); }
            struct Holder { view: Slice<string> }
            fn f(xs: int[], names: string[]): int {
                let v: Slice<int> = xs;
                let h = Holder { view = names };
                return total(xs) + v.length() + h.view.length();
            }
            """);

    [Fact]
    public void A_view_does_not_stand_where_an_array_is_expected() =>
        Assert.Contains("'Slice<int>' to 'int[]'", Rejected("fn f(xs: int[]): int[] { return xs[1..]; }", "LYR-SEM0001"));

    [Fact]
    public void A_views_elements_are_places_in_the_array() =>
        Allowed("""
            struct P { var x: int }
            fn f(xs: int[], ps: P[]): int {
                let v = xs[1..];
                v[0] = 5;
                v[1] += 1;
                let pv = ps[..];
                pv[0].x = 9;
                return v[0];
            }
            """);

    [Fact]
    public void A_range_indexes_an_array_or_a_view_only() =>
        Assert.Contains("no view to take", Rejected("fn f(n: int): int { return n[1..2].length(); }", "LYR-SEM0007"));

    [Fact]
    public void The_rest_of_an_array_pattern_is_a_view() =>
        Allowed("""
            fn f(xs: int[]): int {
                return match (xs) {
                    [] => 0,
                    [first, ..rest] => first + rest.length() + (match (rest) { [x, ..more] => x + more.length(), _ => 0 }),
                };
            }
            """);

    [Fact]
    public void The_rest_cannot_be_kept_as_an_array() =>
        Rejected("fn f(xs: int[]): int[] { return match (xs) { [_, ..rest] => rest, _ => xs }; }", "LYR-SEM0001");

    [Fact]
    public void A_view_is_generic_with_its_element() =>
        Allowed("""
            fn first<T>(xs: Slice<T>): T { return xs[0]; }
            fn f(xs: int[], ss: string[]): int { return first(xs) + (if (first(ss[1..]) == "a") 1 else 0); }
            """);

    [Fact]
    public void Slice_takes_one_type_argument() =>
        Rejected("fn f(v: Slice<int, int>): int { return 0; }", "LYR-SEM0026");
}
