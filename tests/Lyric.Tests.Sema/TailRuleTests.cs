using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// What a value block is worth (design/v5/spec/08 Y4, Y5 S1; the review's M5-1): its tail's
/// value; nothing where it has none and runs to its end; and no value at all — 'never' — where
/// every path through it leaves, by 'return', 'throw', 'break' or 'continue'. An 'if' with its
/// 'else', a 'match' and a 'loop' standing last in one are its tail.
/// </summary>
public class TailRuleTests
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

    private const string Helpers = "fn a(): void { }\nfn b(): void { }\nfn twice(n: int): int { return n * 2; }\n";

    private static void Accepted(string source)
    {
        var de = Check(Helpers + source);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    private static void Refused(string code, string source)
    {
        var de = Check(Helpers + source);
        Assert.True(de.Diagnostics.Count(d => d.Severity == Severity.Error) == 1 && de.Diagnostics.Any(d => d.Code == code),
            $"expected {code} alone, got:\n{string.Join("\n", de.Diagnostics)}");
    }

    // ------------------------------------------------------------------ 'if' with value blocks

    [Fact]
    public void An_if_with_value_blocks_is_worth_its_branches_tails()
    {
        Accepted("fn f(c: bool): int {\n    let x = if (c) { let d = twice(2); d + 1 } else { 2 };\n    return x;\n}\n");
        // … and each tail meets the type the position wants.
        Refused("LYR-SEM0001", "fn f(c: bool): string {\n    let x: string = if (c) { 1 } else { \"two\" };\n    return x;\n}\n");
    }

    /// <summary>A branch that leaves gives no value and takes no part in the 'if's type — by
    /// 'return' or 'throw', and by a jump of the loop around.</summary>
    [Fact]
    public void A_branch_that_leaves_contributes_nothing()
    {
        Accepted("fn f(c: bool): int {\n    let x = if (c) { return 0; } else { 2 };\n    return x + 1;\n}\n");
        Accepted("""
            fn f(xs: int[]): int {
                var total = 0;
                for (x in xs) {
                    let v = if (x < 0) { continue; } else if (x > 100) { break; } else { x };
                    total = total + v;
                }
                return total;
            }
            """);
    }

    [Fact]
    public void A_branch_that_runs_to_its_end_without_a_tail_is_refused_where_a_value_is_wanted()
    {
        Refused("LYR-SEM0033", "fn f(c: bool): int {\n    let x: int = if (c) { a(); } else { 2 };\n    return x;\n}\n");
        // Without a wanted type the branches are unified, and nothing does not unify with an int.
        Refused("LYR-SEM0016", "fn f(c: bool): int {\n    let x = if (c) { a(); } else { 2 };\n    return 0;\n}\n");
    }

    /// <summary>An 'if' whose branches both leave gives no value: a binding of it ends the path,
    /// and the function needs no 'return' behind it.</summary>
    [Fact]
    public void An_if_whose_branches_both_leave_ends_the_path()
    {
        var de = Check("fn f(c: bool): int {\n    let x: int = if (c) { return 1; } else { return 2; };\n}\n");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    // ------------------------------------------------------------------ the tail rule

    [Fact]
    public void A_lambda_is_worth_the_if_standing_last_in_its_block() => Accepted("""
        fn f(): int {
            let pick = (n: int) => {
                let d = n * 2;
                if (d > 5) { d } else { 0 }
            };
            return pick(4);
        }
        """);

    [Fact]
    public void An_arm_is_worth_the_match_standing_last_in_its_block() => Accepted("""
        fn f(n: int, flag: bool): int {
            return match (n) {
                0 => 0,
                _ => {
                    let big = n > 100;
                    match (big) {
                        true => 2,
                        false => if (flag) { 3 } else { 4 }
                    }
                }
            };
        }
        """);

    [Fact]
    public void A_lambda_is_worth_the_loop_standing_last_in_its_block() => Accepted("""
        fn f(xs: int[]): int {
            let first = (limit: int) => {
                var i = 0;
                loop {
                    if (xs[i] > limit) { break xs[i]; }
                    i = i + 1;
                }
            };
            return first(2);
        }
        """);

    /// <summary>A lambda nobody takes a value from: the 'if', the 'match' and the 'loop' standing
    /// last are worth nothing, as they were as statements.</summary>
    [Fact]
    public void A_void_lambda_may_end_in_any_of_the_three() => Accepted("""
        fn each(xs: int[], g: fn(int) -> void): void { for (x in xs) { g(x); } }
        fn f(xs: int[]): void {
            each(xs) { x => if (x > 1) { a(); } else { b(); } };
            each(xs) { x => match (x) { 1 => a(), 2 => { b(); } _ => { } } };
            each(xs) { x =>
                var n = x;
                loop {
                    if (n <= 0) { break; }
                    n = n - 1;
                }
            };
        }
        """);

    /// <summary>A lambda whose block ends in an 'if' that returns on both sides: its returns are
    /// its type, and the tail gives no value.</summary>
    [Fact]
    public void A_tail_that_gives_no_value_leaves_the_returns_to_say_the_type() => Accepted("""
        fn f(): int {
            let sign = (n: int) => {
                if (n < 0) { return -1; } else { return 1; }
            };
            return sign(3);
        }
        """);

    /// <summary>In an arm of a match STATEMENT the value goes nowhere: an 'if' standing last there
    /// is the statement it would be anywhere in the block, not a bare value.</summary>
    [Fact]
    public void In_a_statement_arm_the_three_are_statements() => Accepted("""
        fn f(n: int, c: bool): void {
            match (n) {
                1 => { if (c) { a(); } else { b(); } }
                _ => { }
            }
        }
        """);

    // ------------------------------------------------------------------ arms that end in a jump

    [Fact]
    public void An_arm_may_end_in_a_break_or_a_continue() => Accepted("""
        fn f(): int {
            var total = 0;
            for (i in 0..10) {
                let v = match (i) {
                    3 => { continue; }
                    7 => { a(); break; }
                    _ => i * 2,
                };
                total = total + v;
            }
            return total;
        }
        """);

    [Fact]
    public void An_arm_that_runs_to_its_end_beside_one_that_gives_a_value_is_refused() => Refused("LYR-SEM0033", """
        fn f(n: int): int {
            let v = match (n) {
                0 => { a(); }
                _ => n * 2,
            };
            return v;
        }
        """);

    /// <summary>A match nobody takes a value from — every arm a block without a tail, or a call
    /// that gives none — is worth nothing; it was refused arm by arm.</summary>
    [Fact]
    public void A_match_nobody_takes_a_value_from_is_worth_nothing() => Accepted("""
        fn f(n: int): void {
            let say = (k: int) => match (k) { 1 => { a(); } _ => b() };
            say(n);
        }
        """);

    // ------------------------------------------------------------------ '??' and a block

    [Fact]
    public void The_right_of_a_coalesce_may_leave_or_give_the_value()
    {
        Accepted("fn f(v: ?int): int {\n    let n = v ?? { return 0; };\n    return n + 1;\n}\n");
        Accepted("fn f(v: ?int): int {\n    let n = v ?? { a(); 7 };\n    return n + 1;\n}\n");
    }

    // ------------------------------------------------------------------ definite assignment

    /// <summary>A branch is a branch (07 §1.5): what only one of them assigns is not assigned
    /// behind the 'if'. Both ran on one set, so this passed.</summary>
    [Fact]
    public void What_one_branch_assigns_is_not_assigned_behind_the_if()
    {
        Refused("LYR-SEM0018", "fn f(c: bool): int {\n    var x: int;\n    let y = if (c) { x = 1; 5 } else { 7 };\n    return x + y;\n}\n");
        Accepted("fn f(c: bool): int {\n    var x: int;\n    let y = if (c) { x = 1; 5 } else { x = 2; 7 };\n    return x + y;\n}\n");
        // … and a branch that leaves adds nothing: the other one speaks for what follows.
        Accepted("fn f(c: bool): int {\n    var x: int;\n    let y = if (c) { return 0; } else { x = 2; 7 };\n    return x + y;\n}\n");
    }
}
