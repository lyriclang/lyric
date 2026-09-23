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
/// label no enclosing loop carries.
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
}
