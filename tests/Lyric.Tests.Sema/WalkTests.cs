using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// What a hand-written walk over the syntax tree passed by. Four of them listed the node types
/// they knew and said "nothing here" for the rest — and so a jump, a write, a capture or a
/// return standing in a node the list had no line for was not seen at all: the 'else' of a
/// 'let … else', a block inside an expression, the operand of a type test, the range of a
/// view. Each walks through <see cref="Lyric.AST.AstChildren"/> now, which is total.
/// </summary>
public class WalkTests
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

    private static void Clean(string source)
    {
        var de = Check(source);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    private static void Refused(string code, string source)
    {
        var de = Check(source);
        Assert.True(de.Diagnostics.Count(d => d.Severity == Severity.Error) == 1 && de.Diagnostics.Any(d => d.Code == code),
            $"expected {code} alone, got:\n{string.Join("\n", de.Diagnostics)}");
    }

    private const string Step = "enum Step { More(int), Done }\nfn next(i: int): Step { return if (i < 3) Step.More(i) else Step.Done; }\n";

    // ------------------------------------------------------------------ a jump, wherever it stands

    /// <summary>The idiom: 'while (true)' left by the 'else' of a 'let … else'. The loop was held
    /// for one nothing leaves — the 'return' behind it warned about as unreachable.</summary>
    [Fact]
    public void The_code_behind_a_loop_a_let_else_leaves_is_reached() => Clean(Step + """
        fn count(): int {
            var i = 0;
            while (true) {
                let .More(v) = next(i) else { break; };
                i = v + 1;
            }
            return i;
        }
        """);

    /// <summary>… and without that 'return' the function passed as returning on every path. It
    /// ran off its end.</summary>
    [Fact]
    public void A_break_in_a_let_else_is_a_way_out_of_the_loop() => Refused("LYR-SEM0017", Step + """
        fn count(): int {
            var i = 0;
            while (true) {
                let .More(v) = next(i) else { break; };
                i = v + 1;
            }
        }
        """);

    [Fact]
    public void The_code_behind_a_loop_a_match_arm_leaves_is_reached() => Clean("""
        fn f(xs: int[]): int {
            var i = 0;
            while (true) {
                let v = match (xs[i]) {
                    3 => { if (i > 0) { break; } 1 }
                    _ => 2,
                };
                i = i + v;
            }
            return i;
        }
        """);

    [Fact]
    public void A_break_in_the_arm_of_a_match_expression_is_a_way_out_of_the_loop() => Refused("LYR-SEM0017", """
        fn f(xs: int[]): int {
            var i = 0;
            while (true) {
                let v = match (xs[i]) {
                    3 => { if (i > 0) { break; } 1 }
                    _ => 2,
                };
                i = i + v;
            }
        }
        """);

    /// <summary>A jump deep in an expression: an argument's 'match', in a call, in a binding.</summary>
    [Fact]
    public void A_break_inside_an_argument_is_a_way_out_of_the_loop() => Refused("LYR-SEM0017", """
        fn twice(n: int): int { return n * 2; }
        fn f(n: int): int {
            var i = n;
            while (true) {
                i = twice(match (i) { 0 => { if (n > 0) { break; } 0 } _ => i - 1 });
            }
        }
        """);

    /// <summary>A lambda's body is a function of its own: its 'break' leaves its own loop, and the
    /// loop around the lambda still ends no path but by itself.</summary>
    [Fact]
    public void A_break_in_a_lambda_is_not_the_loops_around_it() => Clean("""
        fn f(): int {
            while (true) {
                let stop = () => { loop { break; } };
                stop();
            }
        }
        """);

    // ------------------------------------------------------------------ a 'do' loop and its jumps

    /// <summary>A block that ends every path speaks for its 'do' loop only when no jump of the
    /// loop stands in it: the 'break' is a way past the 'return'.</summary>
    [Fact]
    public void A_break_before_the_return_in_a_do_loop_is_a_way_past_it() => Refused("LYR-SEM0017", """
        fn f(c: bool): int {
            do {
                if (c) { break; }
                return 1;
            } while (true);
        }
        """);

    /// <summary>A 'continue' reaches the condition, and the condition may end the loop.</summary>
    [Fact]
    public void A_continue_before_the_return_in_a_do_loop_is_a_way_past_it() => Refused("LYR-SEM0017", """
        fn f(c: bool): int {
            var n = 0;
            do {
                n = n + 1;
                if (c && n < 3) { continue; }
                return n;
            } while (n < 2);
        }
        """);

    [Fact]
    public void The_code_behind_a_do_loop_a_break_leaves_is_reached() => Clean("""
        fn f(c: bool): int {
            do {
                if (c) { break; }
                return 1;
            } while (true);
            return 2;
        }
        """);

    /// <summary>The controls: a 'do' block that returns on every path, with no jump of the loop,
    /// ends the function's path — and so does one whose 'continue' meets a condition that is
    /// literally true and nothing breaks.</summary>
    [Fact]
    public void A_do_loop_whose_block_always_returns_ends_the_path()
    {
        Clean("fn f(c: bool): int {\n    do {\n        if (c) { return 1; } else { return 2; }\n    } while (c);\n}\n");
        Clean("fn f(c: bool): int {\n    var n = 0;\n    do {\n        n = n + 1;\n        if (c && n < 3) { continue; }\n        return n;\n    } while (true);\n}\n");
    }

    /// <summary>A jump that names an OUTER loop leaves the inner one for good: an inner 'loop' with
    /// no break of its own is worth 'never', and yet the outer 'do' loop is left.</summary>
    [Fact]
    public void A_labeled_break_through_a_never_loop_leaves_the_outer_one() => Refused("LYR-SEM0017", """
        fn f(): int {
            var n = 0;
            outer: do {
                loop {
                    n = n + 1;
                    if (n > 2) { break outer; }
                }
            } while (true);
        }
        """);

    // ------------------------------------------------------------------ a write, wherever it stands

    /// <summary>The control: a 'let' is not written in an argument.</summary>
    [Fact]
    public void A_let_is_not_written_in_an_argument() => Refused("LYR-SEM0019", """
        struct P { var n: int }
        fn id(n: int): int { return n; }
        fn f(): int {
            let p = P { n = 1 };
            return id(p.n = 2);
        }
        """);

    /// <summary>… nor in the value of a 'let' condition. The list of expression forms had no line
    /// for one, and the write went through: the program printed 2 for a 'let'.</summary>
    [Fact]
    public void A_let_is_not_written_in_a_let_condition() => Refused("LYR-SEM0019", Step + """
        struct P { var n: int }
        fn f(): int {
            let p = P { n = 1 };
            if (let .More(v) = next(p.n = 2)) { return v; }
            return p.n;
        }
        """);

    /// <summary>… nor in the operand of a type test.</summary>
    [Fact]
    public void A_let_is_not_written_in_a_type_test() => Refused("LYR-SEM0019", """
        struct P { var n: int }
        interface Shape { fn area(): int; }
        struct Square :: [Shape] { side: int, fn area(): int { return this.side * this.side; } }
        fn shape(n: int): Shape { return Square { side = n }; }
        fn f(): int {
            let p = P { n = 1 };
            if (shape(p.n = 2) is Square) { return 1; }
            return p.n;
        }
        """);

    // ------------------------------------------------------------------ a value return, wherever it stands

    /// <summary>A block lambda without a context is void only when it returns no value anywhere.
    /// Its returns stood in the arms of a 'match' expression, the walk did not look there, the
    /// lambda was typed void — and each 'return 7;' was then "cannot assign 'int' to 'void'".</summary>
    [Fact]
    public void A_lambda_returns_what_the_arms_of_its_match_expression_return()
    {
        var de = Check("""
            fn f(): int {
                let pick = (n: int) => {
                    let v: int = match (n) {
                        0 => { return 7; }
                        _ => { return 8; }
                    };
                };
                return pick(0) + pick(1);
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }
}
