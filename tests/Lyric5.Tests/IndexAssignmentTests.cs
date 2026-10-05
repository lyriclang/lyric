using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// An assignment through a type's own index, <c>x[k] = v</c> — <c>x.setIndex(k, v)</c>
/// (design/v5/spec/04 D6) — and its compound forms (02 M8a-1, the review's decision):
/// <c>x[k] op= v</c>, <c>x[k]++</c> and <c>x[k]--</c> evaluate the receiver and the key ONCE,
/// read through <c>index</c>, compute, and write through <c>setIndex</c>, as on an array's
/// element. They were refused (<c>LYR-SEM0003</c>, "write it out") or not lowered
/// (<c>LYR-IR0001</c>).
///
/// <para>The plain form had three faults the probes for this slice found. The VALUE was
/// evaluated before the index (04 D6: the target before the value, as an array has it). The
/// value was lowered bare, so <c>xs[0] = null</c> had no type to take (<c>LYR-IR0001</c>) and
/// the assignment AS A VALUE was the right side unconverted — a class where the index holds an
/// interface, which the IR verifier refused (<c>LYR-ICE0001</c>). And the write rule read the
/// tree as written: a <c>let</c> struct was written through <c>r[0] = 9</c>, where
/// <c>r.setIndex(0, 9)</c> is <c>LYR-SEM0019</c>.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class IndexAssignmentTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Counting + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>The one error a program is refused with: its text. A second error fails.</summary>
    private static string Refused(string main, string code)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Counting + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit != 0, "the program was taken");
        var codes = Regex.Matches(error, @"error\[(LYR-[A-Z]+\d+)\]").Select(m => m.Groups[1].Value).ToArray();
        Assert.True(codes.Length == 1 && codes[0] == code, $"expected exactly one {code}, got:\n{error}");
        return error;
    }

    /// <summary>A counter whose every call is seen, and the types the refusals write.</summary>
    private const string Counting = """
        import std.io { println };
        import std.collections { List };
        import std.core { Add };

        class Counter {
            var calls: int = 0,
            mut fn next(): int {
                this.calls += 1;
                return this.calls - 1;
            }
        }

        struct Row :: [IndexSet<int>] {
            type Output = int;
            var a: int,
            var b: int,

            fn index(k: int): int {
                return if (k == 0) this.a else this.b;
            }

            mut fn setIndex(k: int, v: int): void {
                if (k == 0) {
                    this.a = v;
                } else {
                    this.b = v;
                }
            }
        }

        class Fixed :: [Index<int>] {
            type Output = int;

            fn index(k: int): int {
                return k;
            }
        }

        struct V :: [Add] {
            x: int,

            fn add(rhs: V): V {
                return V { x = this.x + rhs.x };
            }
        }

        """;

    // ------------------------------------------------------------------ once

    [Fact]
    public void A_compound_through_an_index_evaluates_the_key_once()
    {
        Assert.Equal("10 11 4 | 4 11 11\n", Output("""
            fn main(): void {
                let xs = List<int>.of([1, 2, 3]);
                let c = Counter { };
                xs[c.next()] += 10;
                xs[c.next()] *= 5;
                xs[c.next()]++;
                let old = xs[c.next() - 3]--;
                let now = ++xs[1];
                println(f"{xs[0]} {xs[1]} {xs[2]} | {c.calls} {old} {now}");
            }
            """));
    }

    [Fact]
    public void A_compound_through_an_index_evaluates_the_receiver_once()
    {
        Assert.Equal("1 12 3 | 2 2 | 4\n", Output("""
            class Cells :: [IndexSet<int>] {
                type Output = int;
                items: int[],

                fn index(k: int): int {
                    return this.items[k];
                }

                mut fn setIndex(k: int, v: int): void {
                    this.items[k] = v;
                }
            }

            fn pick(c: Counter, a: Cells, b: Cells): Cells {
                return if (c.next() == 0) a else b;
            }

            fn main(): void {
                let one = Cells { items = [1, 2, 3] };
                let two = Cells { items = [1, 2, 3] };
                let c = Counter { };
                pick(c, one, two)[c.next()] += 10;
                pick(c, one, two)[c.next() - 3]++;
                println(f"{one.items[0]} {one.items[1]} {one.items[2]} | {two.items[0]} {two.items[1]} | {c.calls}");
            }
            """));
    }

    /// <summary>A key of a type of the conformer's own: read under one key and written under
    /// another is what evaluating it twice would do.</summary>
    [Fact]
    public void A_key_of_any_type_is_evaluated_once()
    {
        Assert.Equal("8 2 2\n", Output("""
            class Tally :: [IndexSet<string>] {
                type Output = int;
                var a: int = 0,
                var b: int = 0,

                fn index(k: string): int {
                    return if (k == "a") this.a else this.b;
                }

                mut fn setIndex(k: string, v: int): void {
                    if (k == "a") {
                        this.a = v;
                    } else {
                        this.b = v;
                    }
                }
            }

            fn name(c: Counter): string {
                return if (c.next() == 0) "a" else "b";
            }

            fn main(): void {
                let t = Tally { };
                let c = Counter { };
                t[name(c)] += 2;
                t[name(c)] += 3;
                t["a"] *= 4;
                t["b"]--;
                println(f"{t.a} {t.b} {c.calls}");
            }
            """));
    }

    [Fact]
    public void A_compound_through_an_index_under_a_constraint()
    {
        Assert.Equal("11 3 2\n", Output("""
            fn bump<T :: [IndexSet<int, Output = int>]>(&t: T, c: Counter): void {
                t[c.next()] += 10;
                t[c.next()]++;
            }

            fn main(): void {
                var xs = List<int>.of([1, 2]);
                let c = Counter { };
                bump(&xs, c);
                println(f"{xs[0]} {xs[1]} {c.calls}");
            }
            """));
    }

    // ------------------------------------------------------------------ the plain form

    /// <summary>The target before the value (04 D6), on a type's own index as on an array: the
    /// list stored the first count at the second — <c>1 10 3</c>.</summary>
    [Fact]
    public void The_target_goes_before_the_value()
    {
        Assert.Equal("11 2 3 | 11 2 3 | 2 2\n", Output("""
            fn main(): void {
                let xs = List<int>.of([1, 2, 3]);
                let c = Counter { };
                xs[c.next()] = c.next() + 10;
                let arr = [1, 2, 3];
                let d = Counter { };
                arr[d.next()] = d.next() + 10;
                println(f"{xs[0]} {xs[1]} {xs[2]} | {arr[0]} {arr[1]} {arr[2]} | {c.calls} {d.calls}");
            }
            """));
    }

    /// <summary>The value takes the shape of the element — <c>null</c> and a bare value into an
    /// optional, a class into an interface — and the assignment is the value STORED: it was
    /// <c>LYR-IR0001</c> for the <c>null</c> and an internal error for the assignment as a
    /// value.</summary>
    [Fact]
    public void A_value_takes_the_shape_of_the_element()
    {
        Assert.Equal("-1 5 | circle circle | 6 6\n", Output("""
            interface Shape {
                fn name(): string;
            }

            class Circle :: [Shape] {
                fn name(): string {
                    return "circle";
                }
            }

            class Square :: [Shape] {
                fn name(): string {
                    return "square";
                }
            }

            fn main(): void {
                let xs = List<?int>.new();
                xs.push(1);
                xs.push(2);
                xs[0] = null;
                xs[1] = 5;
                let shapes = List<Shape>.new();
                shapes.push(Circle { });
                shapes[0] = Square { };
                let stored = (shapes[0] = Circle { });
                let ns = List<int>.of([1]);
                let sum = (ns[0] += 5);
                println(f"{xs[0] ?? -1} {xs[1] ?? -1} | {shapes[0].name()} {stored.name()} | {sum} {ns[0]}");
            }
            """));
    }

    /// <summary><c>??=</c>, <c>&amp;&amp;=</c> and <c>||=</c> read once and write only when they
    /// assign; the right side runs only then.</summary>
    [Fact]
    public void The_short_circuit_forms_write_only_when_they_assign()
    {
        Assert.Equal("7 5 | 1 2 | true true | 3\n", Output("""
            class Log :: [IndexSet<int>] {
                type Output = ?int;
                cells: (?int)[],
                seen: Counter,
                var writes: int = 0,

                fn index(k: int): ?int {
                    this.seen.next();
                    return this.cells[k];
                }

                mut fn setIndex(k: int, v: ?int): void {
                    this.cells[k] = v;
                    this.writes += 1;
                }
            }

            fn main(): void {
                let log = Log { cells = [null, 5], seen = Counter { } };
                let c = Counter { };
                log[0] ??= c.next() + 7;
                log[1] ??= c.next() + 9;
                let reads = log.seen.calls;
                let flags = List<bool>.of([true, false]);
                flags[0] &&= c.next() == 1;
                flags[1] &&= c.next() == 0;
                flags[1] ||= c.next() == 2;
                flags[0] ||= c.next() == 9;
                println(f"{log[0] ?? -1} {log[1] ?? -1} | {log.writes} {reads} | {flags[0]} {flags[1]} | {c.calls}");
            }
            """));
    }

    /// <summary>An index of an index: the inner one is the receiver, evaluated once with all
    /// that leads to it; and a module's list is a receiver like any.</summary>
    [Fact]
    public void A_nested_index_is_evaluated_once()
    {
        Assert.Equal("1 7 2 | 6 4\n", Output("""
            let totals: List<int> = List<int>.of([1, 4]);

            fn main(): void {
                let grid = List<List<int>>.new();
                grid.push(List<int>.of([1, 2]));
                let c = Counter { };
                grid[c.next()][c.next()] += 5;
                totals[0] += 5;
                let last = totals[1]--;
                println(f"{grid[0][0]} {grid[0][1]} {c.calls} | {totals[0]} {last}");
            }
            """));
    }

    /// <summary>The operation is the element's: a literal on the right takes its type, and the
    /// element's own overflow is the operation's.</summary>
    [Fact]
    public void A_narrow_element_takes_a_literal()
    {
        Assert.Equal("4 a!\n", Output("""
            fn main(): void {
                let xs = List<int8>.new();
                xs.push(1);
                xs[0] += 2;
                xs[0]++;
                let names = List<string>.of(["a"]);
                names[0] += "!";
                println(f"{xs[0]} {names[0]}");
            }
            """));
    }

    /// <summary>A struct with a <c>mut fn setIndex</c> is written where it lies: a <c>var</c>
    /// local, a class's field, an array's element.</summary>
    [Fact]
    public void A_struct_is_written_by_index_where_it_lies()
    {
        Assert.Equal("2 9 | 6 4 | 5 5\n", Output("""
            class Holder {
                var row: Row,
            }

            fn main(): void {
                var r = Row { a = 1, b = 2 };
                r[1] += 7;
                r[0]++;
                let h = Holder { row = Row { a = 3, b = 4 } };
                h.row[0] *= 2;
                let rows = [Row { a = 5, b = 6 }];
                rows[0][1]--;
                println(f"{r.a} {r.b} | {h.row.a} {h.row.b} | {rows[0].a} {rows[0].b}");
            }
            """));
    }

    // ------------------------------------------------------------------ refused

    /// <summary>A type read by index and not written (it conforms to <c>Index</c> alone) takes
    /// no write of any form.</summary>
    [Theory]
    [InlineData("f[0] = 1;")]
    [InlineData("f[0] += 1;")]
    [InlineData("f[0] ??= 1;")]
    [InlineData("f[0]++;")]
    [InlineData("--f[0];")]
    public void A_type_read_by_index_is_not_written_by_it(string statement)
    {
        var error = Refused("fn main(): void {\n    let f = Fixed { };\n    " + statement + "\n}\n", "LYR-SEM0019");
        Assert.Contains("is read by index and not written", error);
    }

    /// <summary><c>setIndex</c> is a <c>mut fn</c>: the receiver is a place, by the rule of the
    /// call written out. Every one of these was taken, and wrote.</summary>
    [Theory]
    [InlineData("fn main(): void {\n    let r = Row { a = 1, b = 2 };\n    r[0] = 9;\n}\n", "'r' is bound with 'let'")]
    [InlineData("fn main(): void {\n    let r = Row { a = 1, b = 2 };\n    r[0] += 9;\n}\n", "'r' is bound with 'let'")]
    [InlineData("fn main(): void {\n    let r = Row { a = 1, b = 2 };\n    r[0]++;\n}\n", "'r' is bound with 'let'")]
    [InlineData("fn f<T :: [IndexSet<int, Output = int>]>(t: T): void {\n    t[0] = 1;\n}\nfn main(): void {\n}\n", "'t' is a parameter")]
    [InlineData("fn main(): void {\n    let rows = List<Row>.new();\n    rows.push(Row { a = 1, b = 2 });\n    rows[0][1] = 5;\n}\n", "the element is a copy")]
    [InlineData("class Grid :: [IndexSet<int>] {\n    type Output = int;\n    cells: int[],\n    fn index(k: int): int { return this.cells[k]; }\n    mut fn setIndex(k: int, v: int): void { this.cells[k] = v; }\n    fn sneak(): void { this[0] = 9; }\n}\nfn main(): void {\n}\n", "not declared 'mut fn'")]
    public void The_receiver_of_an_index_write_is_a_place(string program, string reason)
    {
        var error = Refused(program, "LYR-SEM0019");
        Assert.Contains("'x[k] = v' is 'x.setIndex(k, v)'", error);
        Assert.Contains(reason, error);
    }

    /// <summary>The operator is the element's: one it has not is refused once, as on a
    /// variable.</summary>
    [Theory]
    [InlineData("let xs = List<bool>.of([true]);\n    xs[0] += 1;")]
    [InlineData("let xs = List<string>.of([\"a\"]);\n    xs[0]++;")]
    public void An_operator_the_element_has_not_is_refused(string statements) =>
        Refused("fn main(): void {\n    " + statements + "\n}\n", "LYR-SEM0003");

    /// <summary>A compound through an OPERATOR INTERFACE keeps the rule a field and an array's
    /// element have: a simple variable target. Whether that rule stays is one question for the
    /// three, and not this slice's.</summary>
    [Theory]
    [InlineData("let xs = List<V>.new();\n    xs.push(V { x = 1 });\n    xs[0] += V { x = 2 };")]
    [InlineData("let arr = [V { x = 1 }];\n    arr[0] += V { x = 2 };")]
    public void A_compound_through_an_operator_interface_keeps_its_rule(string statements)
    {
        var error = Refused("fn main(): void {\n    " + statements + "\n}\n", "LYR-SEM0003");
        Assert.Contains("needs a simple variable target", error);
    }
}
