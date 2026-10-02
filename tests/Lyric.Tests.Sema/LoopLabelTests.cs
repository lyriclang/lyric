using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Loop labels (§7.2): <c>outer: while (…)</c> names a loop, <c>break outer</c> and
/// <c>continue outer</c> name it back. A label is no symbol — it shares nothing with the value
/// namespace and is scoped to its loop's body. Three diagnostics guard the form: a label nothing
/// jumps to (a warning), a label repeating an enclosing one (ambiguous, refused), and a jump to a
/// label no enclosing loop carries. And <c>loop</c> (design/v5/spec/05 E11): the value its breaks
/// give, never without one, the jumps held to a loop of their own function.
/// </summary>
public class LoopLabelTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    [Fact]
    public void A_labeled_break_and_continue_resolve_to_the_named_loop()
    {
        var de = Check("""
            fn f(): int {
                var n = 0;
                outer: for (i in 0..3) {
                    for (j in 0..3) {
                        if (j == 1) { continue outer; }
                        if (i == 2) { break outer; }
                        n += 1;
                    }
                }
                return n;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0103");
    }

    [Fact]
    public void A_label_shares_nothing_with_the_value_namespace()
    {
        var de = Check("""
            fn f(): int {
                let outer = 5;
                outer: while (outer > 0) {
                    break outer;
                }
                return outer;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void A_jump_to_an_unknown_label_is_refused()
    {
        var de = Check("""
            fn f(): int {
                for (i in 0..3) { break nowhere; }
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0101");
        Assert.Contains("nowhere", error.Message);
    }

    [Fact]
    public void A_label_is_scoped_to_its_loop()
    {
        var de = Check("""
            fn f(): int {
                outer: for (i in 0..3) { break outer; }
                for (j in 0..3) { break outer; }
                return 0;
            }
            """);
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0101");
    }

    [Fact]
    public void A_label_repeating_an_enclosing_one_is_refused()
    {
        var de = Check("""
            fn f(): int {
                outer: for (i in 0..3) {
                    outer: for (j in 0..3) { break outer; }
                }
                return 0;
            }
            """);
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0102");
    }

    [Fact]
    public void A_label_nothing_names_is_a_warning()
    {
        var de = Check("""
            fn f(): int {
                outer: for (i in 0..3) { break; }
                return 0;
            }
            """);
        Assert.False(de.HasErrors);
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0103");
    }

    [Fact]
    public void A_label_on_a_non_loop_is_a_parse_error()
    {
        var de = Check("""
            fn f(): int {
                here: let x = 1;
                return x;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-PAR0046");
    }

    // ------------------------------------------------------------------ flow

    /// <summary>'while (true)' diverges unless something breaks out of it. A 'break outer' inside
    /// a NESTED loop leaves the outer one, so the code after it is reachable and a return there is
    /// required — the analysis must look through the inner loop for the labeled form.</summary>
    [Fact]
    public void A_labeled_break_inside_a_nested_loop_ends_the_outer_loop_for_coverage()
    {
        var de = Check("""
            fn f(): int {
                outer: while (true) {
                    while (true) { break outer; }
                }
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0017");
    }

    [Fact]
    public void An_unlabeled_break_in_a_nested_loop_does_not_end_the_outer_loop()
    {
        var de = Check("""
            fn f(): int {
                outer: while (true) {
                    while (true) { break; }
                }
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    // ------------------------------------------------------------------ loop (05 E11)

    [Fact]
    public void A_loop_gives_what_its_breaks_give()
    {
        var de = Check("""
            fn f(xs: int[]): int {
                var i = 0;
                let found: int = loop {
                    if (xs[i] > 2) { break xs[i]; }
                    i += 1;
                };
                return found;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void An_endless_loop_needs_no_return() =>
        Assert.False(Check("fn f(): int { loop { } }").HasErrors);

    [Fact]
    public void A_loop_nothing_leaves_is_never_and_fits_where_any_type_is_expected() =>
        Assert.False(Check("fn f(): int { let n: int = loop { }; }").HasErrors);

    [Theory]
    [InlineData("fn f() { break; }", "LYR-SEM0147")]
    [InlineData("fn f() { continue; }", "LYR-SEM0147")]
    [InlineData("fn f() { while (true) { let g = () => { break; }; } }", "LYR-SEM0147")]
    [InlineData("fn f() { while (true) { break 1; } }", "LYR-SEM0148")]
    [InlineData("fn f() { for (i in 0..3) { break i; } }", "LYR-SEM0148")]
    [InlineData("fn f(c: bool): int { return loop { if (c) { break; } break 1; }; }", "LYR-SEM0149")]
    public void A_jump_is_held_to_a_loop_of_its_own_function(string source, string code) =>
        Assert.Contains(Check(source).Diagnostics, d => d.Code == code);

    [Fact]
    public void A_label_around_a_lambda_is_out_of_its_reach() =>
        Assert.Contains(Check("fn f() { outer: while (true) { let g = () => { break outer; }; } }").Diagnostics,
            d => d.Code == "LYR-SEM0101");

    [Fact]
    public void A_variable_assigned_before_every_break_is_assigned_after_the_loop()
    {
        Assert.False(Check("fn f(): int { var x: int; loop { x = 1; break; } return x; }").HasErrors);
        Assert.Contains(Check("fn f(c: bool): int { var x: int; loop { if (c) { break; } x = 1; break; } return x; }").Diagnostics,
            d => d.Code == "LYR-SEM0018");
    }

    // A branch that jumps adds nothing to what follows the 'if' — the continuation follows the
    // other; a 'do' loop reaches its condition from every 'continue' and is left by every 'break'.
    [Theory]
    [InlineData("fn f(c: bool): int { var x: int; var i = 0; while (i < 3) { if (c) { x = i; } else { break; } i = x + 1; } return i; }")]
    [InlineData("fn f(c: bool): int { var x: int; loop { if (c) { continue; } else { x = 1; } return x; } }")]
    [InlineData("fn f(c: bool, d: bool): int { var x: int; do { x = 1; if (c) { break; } } while (d); return x; }")]
    [InlineData("fn f(k: int): int { var x: int; match (k) { 1 => { x = 1; } _ => panic(\"no\"), } return x; }")]
    public void A_variable_assigned_on_every_way_on_is_assigned(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Theory]
    [InlineData("fn f(c: bool, d: bool): int { var x: int; do { if (c) { break; } x = 1; } while (d); return x; }")]
    [InlineData("fn f(c: bool): int { var x: int; do { if (c) { continue; } x = 1; } while (x < 3); return 0; }")]
    public void A_jump_in_a_do_loop_is_a_way_past_the_assignment(string source) =>
        Assert.Contains(Check(source).Diagnostics, d => d.Code == "LYR-SEM0018");
}
