using System.IO;
using Lyric.Core;
using Lyric.Core.Rendering;
using Xunit;

namespace Lyric.Tests.Core;

/// <summary>
/// The text output after design/v5/spec/11 G5 and G6 (M12a S2), as goldens without colour: the head,
/// the arrow, the gutter, primary and secondary marks with their texts, notes as lines and as
/// blocks, a suggestion, spans across lines, tabs, wide characters, a cut line, the error limit,
/// each finding said once — and colour and paths where the style asks.
/// </summary>
public class TextRendererTests
{
    private static string Render(DiagnosticEngine de, RenderStyle? style = null)
    {
        var sw = new StringWriter { NewLine = "\n" };
        de.RenderText(sw, style ?? RenderStyle.Plain);
        return sw.ToString();
    }

    private static (SourceManager, FileId, DiagnosticEngine) Source(string text)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", text);
        return (sm, id, new DiagnosticEngine(sm));
    }

    [Fact]
    public void One_span_has_the_arrow_the_gutter_and_its_marks()
    {
        var (_, id, de) = Source("let x = #;");
        de.Report("LYR-LEX0001", Severity.Error, new Span(id, 8, 9), "unexpected character '#'");
        Assert.Equal(
            "error[LYR-LEX0001]: unexpected character '#'\n" +
            " --> test.lyr:1:9\n" +
            "  |\n" +
            "1 | let x = #;\n" +
            "  |         ^\n\n", Render(de));
    }

    [Fact]
    public void A_span_is_marked_over_its_length_and_an_empty_one_by_one_mark()
    {
        var (_, id, de) = Source("let foo = 1;");
        de.Report("LYR-SEM0071", Severity.Warning, new Span(id, 4, 7), "unused");
        de.Report("X", Severity.Error, new Span(id, 12, 12), "end of input");
        Assert.Equal(
            "warning[LYR-SEM0071]: unused\n" +
            " --> test.lyr:1:5\n" +
            "  |\n" +
            "1 | let foo = 1;\n" +
            "  |     ^^^\n\n" +
            "error[X]: end of input\n" +
            " --> test.lyr:1:13\n" +
            "  |\n" +
            "1 | let foo = 1;\n" +
            "  |             ^\n\n", Render(de));
    }

    [Fact]
    public void Labels_stand_under_their_lines_the_secondary_ones_with_dashes()
    {
        var (_, id, de) = Source("let x = 1;\nlet x = 2;");
        de.Report(new Diagnostic("D", Severity.Error, new Span(id, 15, 16), "'x' is bound already")
        {
            PrimaryLabel = "bound again",
            Labels = [new LabeledSpan(new Span(id, 4, 5), "first bound here")],
        });
        Assert.Equal(
            "error[D]: 'x' is bound already\n" +
            " --> test.lyr:2:5\n" +
            "  |\n" +
            "1 | let x = 1;\n" +
            "  |     - first bound here\n" +
            "2 | let x = 2;\n" +
            "  |     ^ bound again\n\n", Render(de));
    }

    [Fact]
    public void Two_labels_on_one_line_the_left_text_hangs_under_its_mark()
    {
        var (_, id, de) = Source("foo(a, b)");
        de.Report(new Diagnostic("X", Severity.Error, new Span(id, 7, 8), "msg")
        {
            PrimaryLabel = "this one",
            Labels = [new LabeledSpan(new Span(id, 4, 5), "and this")],
        });
        Assert.Equal(
            "error[X]: msg\n" +
            " --> test.lyr:1:8\n" +
            "  |\n" +
            "1 | foo(a, b)\n" +
            "  |     -  ^ this one\n" +
            "  |     |\n" +
            "  |     and this\n\n", Render(de));
    }

    [Fact]
    public void A_label_in_another_file_has_a_block_of_its_own()
    {
        var sm = new SourceManager();
        var main = sm.AddVirtual("main.lyr", "use lib;\nf(1);");
        var lib = sm.AddVirtual("lib.lyr", "fn f(): void {}");
        var de = new DiagnosticEngine(sm);
        de.Report(new Diagnostic("X", Severity.Error, new Span(main, 9, 13), "'f' takes no argument")
        {
            Labels = [new LabeledSpan(new Span(lib, 3, 4), "declared here")],
        });
        Assert.Equal(
            "error[X]: 'f' takes no argument\n" +
            " --> main.lyr:2:1\n" +
            "  |\n" +
            "2 | f(1);\n" +
            "  | ^^^^\n" +
            " ::: lib.lyr:1:4\n" +
            "  |\n" +
            "1 | fn f(): void {}\n" +
            "  |    - declared here\n\n", Render(de));
    }

    [Fact]
    public void A_note_with_a_place_is_a_block_one_without_a_line()
    {
        var (_, id, de) = Source("let x = 1;\nlet x = 2;");
        de.Report("D", Severity.Error, new Span(id, 15, 16), "'x' is already defined",
            new DiagnosticNote(new Span(id, 4, 5), "previous definition"),
            new DiagnosticNote("a binding is one name in its scope"),
            new DiagnosticNote("rename one of them") { Kind = NoteKind.Help });
        Assert.Equal(
            "error[D]: 'x' is already defined\n" +
            " --> test.lyr:2:5\n" +
            "  |\n" +
            "2 | let x = 2;\n" +
            "  |     ^\n" +
            "note: previous definition\n" +
            " --> test.lyr:1:5\n" +
            "  |\n" +
            "1 | let x = 1;\n" +
            "  |     ^\n" +
            "  |\n" +
            "  = note: a binding is one name in its scope\n" +
            "  = help: rename one of them\n\n", Render(de));
    }

    [Fact]
    public void A_finding_without_a_place_says_its_notes_as_lines()
    {
        var de = new DiagnosticEngine(new SourceManager());
        de.Report("X", Severity.Error, default, "no input", new DiagnosticNote("a note"));
        Assert.Equal("error[X]: no input\n  = note: a note\n\n", Render(de));
    }

    [Fact]
    public void A_suggestion_shows_the_line_as_it_would_read()
    {
        var (_, id, de) = Source("let x = 1;");
        de.Report("X", Severity.Error, new Span(id, 4, 5), "'x' is assigned",
            DiagnosticNote.Suggest("make it a 'var'", new Replacement(new Span(id, 0, 3), "var", Applicability.Safe)));
        Assert.Equal(
            "error[X]: 'x' is assigned\n" +
            " --> test.lyr:1:5\n" +
            "  |\n" +
            "1 | let x = 1;\n" +
            "  |     ^\n" +
            "help: make it a 'var'\n" +
            " --> test.lyr:1:1\n" +
            "  |\n" +
            "1 | var x = 1;\n" +
            "  | ~~~\n\n", Render(de));
    }

    [Fact]
    public void A_span_across_lines_is_marked_on_its_first_and_its_last()
    {
        var (_, id, de) = Source("call(\n  a,\n  b,\n  c)\nrest");
        de.Report(new Diagnostic("X", Severity.Error, new Span(id, 0, 21), "crosses lines") { PrimaryLabel = "this call" });
        Assert.Equal(
            "error[X]: crosses lines\n" +
            " --> test.lyr:1:1\n" +
            "  |\n" +
            "1 | call(\n" +
            "  | ^^^^^\n" +
            "...\n" +
            "4 |   c)\n" +
            "  |   ^^ this call\n\n", Render(de));
    }

    [Fact]
    public void A_line_between_two_marked_ones_is_shown()
    {
        var (_, id, de) = Source("a\nb\nc");
        de.Report(new Diagnostic("X", Severity.Error, new Span(id, 4, 5), "msg")
        {
            Labels = [new LabeledSpan(new Span(id, 0, 1), "from here")],
        });
        Assert.Equal(
            "error[X]: msg\n" +
            " --> test.lyr:3:1\n" +
            "  |\n" +
            "1 | a\n" +
            "  | - from here\n" +
            "2 | b\n" +
            "3 | c\n" +
            "  | ^\n\n", Render(de));
    }

    [Fact]
    public void A_tab_takes_four_columns_and_the_marks_follow()
    {
        var (_, id, de) = Source("\tx = 1");
        de.Report("X", Severity.Error, new Span(id, 1, 2), "msg");
        Assert.Equal(
            "error[X]: msg\n" +
            " --> test.lyr:1:2\n" +
            "  |\n" +
            "1 |     x = 1\n" +
            "  |     ^\n\n", Render(de));
    }

    [Fact]
    public void A_wide_character_takes_two_columns_a_combining_one_none()
    {
        // '日本 = x': two wide characters; 'é = y': an e and its accent, one column
        var (_, id, de) = Source("日本 = x\né = y");
        de.Report("X", Severity.Error, new Span(id, 5, 6), "wide");
        de.Report("Y", Severity.Error, new Span(id, 12, 13), "combining");
        var text = Render(de);
        Assert.Contains("1 | 日本 = x\n  |        ^\n", text);
        Assert.Contains("2 | é = y\n  |     ^\n", text);
    }

    [Fact]
    public void A_long_line_is_cut_around_its_marks()
    {
        var line = string.Concat(Enumerable.Repeat("0123456789", 5));
        var (_, id, de) = Source(line);
        de.Report("X", Severity.Error, new Span(id, 30, 32), "msg");
        Assert.Equal(
            "error[X]: msg\n" +
            " --> test.lyr:1:31\n" +
            "  |\n" +
            "1 | …01234567890123456789…\n" +
            "  |            ^^\n\n", Render(de, RenderStyle.Plain with { Width = 20 }));
    }

    [Fact]
    public void The_gutter_is_as_wide_as_the_widest_line_number()
    {
        var (_, id, de) = Source(string.Concat(Enumerable.Repeat("x\n", 11)) + "y");
        de.Report("X", Severity.Error, new Span(id, 22, 23), "msg");
        Assert.StartsWith("error[X]: msg\n  --> test.lyr:12:1\n   |\n12 | y\n   | ^\n", Render(de));
    }

    [Fact]
    public void After_the_limit_the_errors_are_counted_not_drawn()
    {
        var de = new DiagnosticEngine(new SourceManager());
        de.Report("A", Severity.Error, default, "first");
        de.Report("B", Severity.Error, default, "second");
        de.Report("C", Severity.Error, default, "third");
        de.Report("D", Severity.Warning, default, "fourth");
        Assert.Equal(
            "error[A]: first\n\nerror[B]: second\n\n" +
            "note: 1 more error and 1 more warning or hint not shown; '--max-errors 0' shows all\n\n",
            Render(de, RenderStyle.Plain with { MaxErrors = 2 }));
    }

    [Fact]
    public void A_limit_of_zero_draws_all()
    {
        var de = new DiagnosticEngine(new SourceManager());
        for (var i = 0; i < 60; i++) de.Report($"E{i:D2}", Severity.Error, default, "e");
        Assert.Equal(60, Render(de, RenderStyle.Plain with { MaxErrors = 0 }).Split("error[").Length - 1);
    }

    [Fact]
    public void A_finding_is_said_once_across_renders()
    {
        var de = new DiagnosticEngine(new SourceManager());
        de.Report("A", Severity.Warning, default, "said first");
        Assert.Equal("warning[A]: said first\n\n", Render(de));
        de.Report("B", Severity.Error, default, "said after the gate");
        Assert.Equal("error[B]: said after the gate\n\n", Render(de));
        Assert.Equal("", Render(de));
    }

    [Fact]
    public void Only_findings_start_a_line_with_their_severity()
    {
        // an editor's matcher reads one problem per head: a note's block must not look like one
        var (_, id, de) = Source("abc");
        de.Report("X", Severity.Warning, new Span(id, 0, 1), "msg",
            new DiagnosticNote(new Span(id, 2, 3), "elsewhere"));
        var heads = Render(de).Split('\n').Count(l => l.StartsWith("warning[") || l.StartsWith("error["));
        Assert.Equal(1, heads);
    }

    [Fact]
    public void Colour_paints_the_head_and_the_marks_and_nothing_else_changes()
    {
        var (_, id, de) = Source("let x = #;");
        de.Report("X", Severity.Error, new Span(id, 8, 9), "msg");
        var text = Render(de, RenderStyle.Plain with { Color = true });
        Assert.StartsWith("\u001b[1;91merror[X]\u001b[0m\u001b[1m: msg\u001b[0m\n", text);
        Assert.Contains("\u001b[1;91m^\u001b[0m", text);
        Assert.Equal(
            "error[X]: msg\n --> test.lyr:1:9\n  |\n1 | let x = #;\n  |         ^\n\n",
            System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;]*m", ""));
    }

    [Fact]
    public void Colour_follows_the_option_then_no_color_and_the_terminal()
    {
        string? None(string _) => null;
        Assert.True(RenderStyle.ColorFor("always", false, None));
        Assert.False(RenderStyle.ColorFor("never", true, None));
        Assert.True(RenderStyle.ColorFor("auto", true, None));
        Assert.False(RenderStyle.ColorFor("auto", false, None));
        Assert.False(RenderStyle.ColorFor("auto", true, v => v == "NO_COLOR" ? "1" : null));
        Assert.True(RenderStyle.ColorFor("auto", true, v => v == "NO_COLOR" ? "" : null));
        Assert.False(RenderStyle.ColorFor("auto", true, v => v == "TERM" ? "dumb" : null));
        Assert.Null(RenderStyle.ColorFor("sometimes", true, None));
    }

    [Fact]
    public void Paths_are_said_relative_to_the_package_unless_asked_absolute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pkg");
        var file = Path.Combine(root, "src", "main.lyr");
        var outside = Path.Combine(Path.GetTempPath(), "other", "lib.lyr");
        var style = RenderStyle.Plain with { BaseDirectory = root };
        Assert.Equal(Path.Combine("src", "main.lyr"), style.Display(file));
        Assert.Equal(outside, style.Display(outside));
        Assert.Equal(file, (style with { AbsolutePaths = true }).Display(file));
        Assert.Equal("virtual.lyr", style.Display("virtual.lyr"));
    }

    [Fact]
    public void A_control_character_in_the_line_is_one_stand_in_and_the_mark_stays()
    {
        var (_, id, de) = Source("a\u001bb = 1");
        de.Report("X", Severity.Error, new Span(id, 2, 3), "msg");
        Assert.Contains("1 | a␛b = 1\n  |   ^\n", Render(de));
    }
}
