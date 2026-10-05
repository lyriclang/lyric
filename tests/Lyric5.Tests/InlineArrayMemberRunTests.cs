using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The members of an inline array, <c>T[N]</c> (design/v5/spec/03 T13 A4; the review's M4-2). It
/// has <c>length()</c>, <c>isEmpty()</c> and <c>toArray()</c> wherever it lies; where it lies in
/// the heap — a field of an object, an element of an array — it has the members of a view, called
/// on a view of all of it, as an array has them (lyric-spec 03 §5.2 rule 4); where it lies in a
/// frame no view of it exists, and such a member is refused with the way out
/// (<c>LYR-SEM0115</c>). It had <c>length()</c> alone. A block that targets an inline array added
/// nothing and said nothing; it is refused (<c>LYR-SEM0047</c>). Through <c>Main</c>, so in the
/// console collection.
/// </summary>
[Collection("console")]
public class InlineArrayMemberRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private static string Refused(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        return error;
    }

    [Fact]
    public void Every_inline_array_has_its_three_members()
    {
        Assert.Equal("3 false 3 9 | 4 false 4 | 3 1 2\n", Output("""
            import std.io { println };

            class Board {
                var cells: int[4] = [4, 1, 3, 2],
            }

            fn main(): void {
                var xs: int[3] = [3, 1, 2];
                let copy = xs.toArray();
                xs[0] = 9;
                let b = Board { };
                let held = b.cells.toArray();
                println(f"{xs.length()} {xs.isEmpty()} {copy.length()} {xs[0]} | {b.cells.length()} {b.cells.isEmpty()} {held.length()} | {copy[0]} {copy[1]} {copy[2]}");
            }
            """));
    }

    /// <summary>The view's members where the array lies in the heap — and a write through one
    /// goes to the array, which is what "a view of all of it" means.</summary>
    [Fact]
    public void An_inline_array_in_the_heap_has_the_views_members()
    {
        Assert.Equal("true false 4 2 | 1 1 | 7\n", Output("""
            import std.io { println };

            class Board {
                var cells: int[4] = [4, 1, 3, 2],
            }

            struct Row {
                var cells: int[2],
            }

            fn main(): void {
                let b = Board { };
                let rows: Row[] = [Row { cells = [5, 6] }];
                let first = b.cells.first() ?? -1;
                let last = b.cells.last() ?? -1;
                let at = b.cells.indexOf(1) ?? -1;
                let inRows = rows[0].cells.indexOf(6) ?? -1;
                let grid: int[2][] = [[7, 8]];
                println(f"{b.cells.contains(3)} {b.cells.contains(9)} {first} {last} | {at} {inRows} | {grid[0].first() ?? -1}");
            }
            """));
    }

    [Fact]
    public void A_views_member_on_an_inline_array_in_a_frame_is_refused_with_the_way_out()
    {
        var error = Refused("""
            fn main(): void {
                let xs: int[3] = [3, 1, 2];
                let found = xs.contains(2);
            }
            """);
        Assert.Contains("main.lyr:3:20: error[LYR-SEM0115]", error);
        Assert.Contains("'contains' is a member of a view", error);
        Assert.Contains("xs.toArray().contains(", error);
        Assert.DoesNotContain("LYR-SEM0012", error);
    }

    /// <summary>A member that WRITES reaches the array itself, in an object and as an element
    /// of an array: the receiver is a view of it, no copy.</summary>
    [Fact]
    public void A_write_through_a_views_member_reaches_the_inline_array()
    {
        Assert.Equal("1 2 3 4 | 8 9 | 0 0\n", Output("""
            import std.io { println };

            class Board {
                var cells: int[4] = [4, 1, 3, 2],
            }

            fn main(): void {
                let b = Board { };
                b.cells.sort();
                let rows: int[2][] = [[9, 8], [5, 5]];
                rows[0].reverse();
                rows[1].fill(0);
                println(f"{b.cells[0]} {b.cells[1]} {b.cells[2]} {b.cells[3]} | {rows[0][0]} {rows[0][1]} | {rows[1][0]} {rows[1][1]}");
            }
            """));
    }

    /// <summary>The frame in its forms: a local, a parameter, a field of a struct that is a
    /// local. Each is refused where the member is called, and each has the three built in.</summary>
    [Theory]
    [InlineData("fn main(): void {\n    let xs: int[3] = [3, 1, 2];\n    let n = xs.indexOf(1);\n}\n", "xs.toArray().indexOf(")]
    [InlineData("fn f(xs: int[3]): bool {\n    return xs.contains(1);\n}\nfn main(): void {\n}\n", "xs.toArray().contains(")]
    [InlineData("struct Row {\n    cells: int[2],\n}\nfn main(): void {\n    let r = Row { cells = [5, 6] };\n    let n = r.cells.first();\n}\n", "r.cells.toArray().first(")]
    public void A_frame_has_no_view_of_its_inline_array(string program, string wayOut)
    {
        var error = Refused(program);
        Assert.Contains("error[LYR-SEM0115]", error);
        Assert.Contains(wayOut, error);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(error, @"error\["));
    }

    [Fact]
    public void A_block_does_not_target_an_inline_array()
    {
        var error = Refused("""
            extend<T> T[4] {
                fn head(): T { return this[0]; }
            }

            fn main(): void {
            }
            """);
        Assert.Contains("main.lyr:1:11: error[LYR-SEM0047]", error);
        Assert.Contains("inline array", error);
    }
}
