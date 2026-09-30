using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>T[N]</c>, the inline array (design/v5/spec/03 T13 A4): a value of N elements, built from
/// a literal of that length or <c>[x] * N</c>, indexed and measured like an array, copied like
/// a struct, written where it lies; a view of it is taken where it lies in the heap only.
/// </summary>
public class InlineArrayTests
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
    public void A_literal_of_the_length_builds_it() =>
        Allowed("""
            struct Mat { m: float[4] }
            fn f(): int {
                let a: int[3] = [1, 2, 3];
                let z: uint8[64] = [0] * 64;
                let m = Mat { m = [1.0, 2.0, 3.0, 4.0] };
                let grid: int[2][2] = [[1, 2], [3, 4]];
                let m4: float[4][4] = [[0.0] * 4] * 4;
                return a[0] + a.length() + z.length() + grid[1][^1] + (if (m.m[3] > m4[3][3]) 1 else 0);
            }
            """);

    [Fact]
    public void A_literal_of_another_length_is_refused()
    {
        Assert.Contains("holds 3 elements; the literal has 2", Rejected("fn f(): int { let a: int[3] = [1, 2]; return a[0]; }", "LYR-SEM0001"));
        Assert.Contains("the repetition makes 8", Rejected("fn f(): int { let a: int[4] = [0] * 8; return a[0]; }", "LYR-SEM0001"));
    }

    [Fact]
    public void A_heap_array_does_not_stand_for_an_inline_one() =>
        Rejected("fn f(xs: int[]): int { let a: int[3] = xs; return a[0]; }", "LYR-SEM0001");

    [Fact]
    public void The_length_is_part_of_the_type() =>
        Assert.Contains("'int[4]' to 'int[3]'", Rejected("fn f(a: int[4]): int[3] { return a; }", "LYR-SEM0001"));

    [Fact]
    public void An_element_is_written_where_the_array_lies() =>
        Allowed("""
            struct Mat { var m: int[4] }
            class Buf { var data: int[4], fixed: int[2] }
            fn f(b: Buf, arr: int[4][]): int {
                var a: int[3] = [1, 2, 3];
                a[0] = 9;
                var m = Mat { m = [0] * 4 };
                m.m[1] = 5;
                b.data[2] = 7;
                arr[0][3] = 8;
                return a[0] + m.m[1] + b.data[2] + arr[0][3];
            }
            """);

    [Fact]
    public void A_let_freezes_it_like_a_struct()
    {
        Assert.Contains("'let'", Rejected("fn f(): int { let a: int[3] = [1, 2, 3]; a[0] = 9; return a[0]; }", "LYR-SEM0019"));
        Rejected("fn f(a: int[3]): int { a[0] = 9; return a[0]; }", "LYR-SEM0019");
        Rejected("class Buf { fixed: int[2] }\nfn f(b: Buf): int { b.fixed[0] = 1; return 0; }", "LYR-SEM0019");
    }

    [Fact]
    public void A_view_is_taken_where_the_array_lies_in_the_heap() =>
        Allowed("""
            struct Mat { m: int[4] }
            class Buf { data: int[4], mat: Mat }
            fn total(xs: Slice<int>): int { return xs.length(); }
            fn f(b: Buf, arr: int[4][], v: Slice<int[4]>): int {
                return total(b.data) + b.data[1..].length() + total(arr[0]) + arr[1][..2].length()
                    + total(b.mat.m) + total(v[0]);
            }
            """);

    [Fact]
    public void A_view_of_a_frame_is_refused()
    {
        Assert.Contains("lies in a frame", Rejected("fn f(): int { let a: int[3] = [1, 2, 3]; return a[1..].length(); }", "LYR-SEM0115"));
        Rejected("fn total(xs: Slice<int>): int { return xs.length(); }\nfn f(a: int[3]): int { return total(a); }", "LYR-SEM0115");
        Rejected("struct Mat { m: int[4] }\nfn f(): int { let m = Mat { m = [0] * 4 }; return m.m[..].length(); }", "LYR-SEM0115");
    }

    [Fact]
    public void A_value_type_cannot_hold_itself_through_an_inline_array() =>
        Rejected("struct Node { kids: Node[2] }\nfn main(): int { return 0; }", "LYR-SEM0056");

    [Fact]
    public void An_array_pattern_of_the_one_length_covers_it()
    {
        Allowed("fn f(a: int[3]): int { return match (a) { [x, y, z] => x + y + z }; }");
        Allowed("fn f(a: int[3]): int { return match (a) { [x, ..] => x }; }");
        Assert.Contains("'[_, _, _]'", Rejected("fn f(a: int[3]): int { return match (a) { [x, y] => x + y }; }", "LYR-SEM0050"));
    }
}
