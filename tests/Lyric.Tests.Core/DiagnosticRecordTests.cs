using Lyric.Core;
using Xunit;

namespace Lyric.Tests.Core;

/// <summary>
/// The record after design/v5/spec/11 G1 (M12a S1): labelled secondary places, notes of a kind, a
/// suggested change with its applicability — and the old forms meaning what they meant.
/// </summary>
public class DiagnosticRecordTests
{
    private static readonly FileId File = new SourceManager().AddVirtual("r.lyr", "let x = 1;\nlet x = 2;");

    [Fact]
    public void A_note_is_a_note_unless_told()
    {
        Assert.Equal(NoteKind.Place, new DiagnosticNote(new Span(File, 4, 5), "first here").Kind);
        Assert.Equal(NoteKind.Place, new DiagnosticNote("synthesized as: x").Kind);
        Assert.Equal(NoteKind.Help, new DiagnosticNote("write it out") { Kind = NoteKind.Help }.Kind);
        Assert.Equal(NoteKind.Category, new DiagnosticNote("a compiler bug") { Kind = NoteKind.Category }.Kind);
    }

    [Fact]
    public void A_suggestion_carries_its_change()
    {
        var fix = new Replacement(new Span(File, 0, 3), "var", Applicability.Safe);
        var note = DiagnosticNote.Suggest("make it a 'var'", fix);
        Assert.Equal(NoteKind.Suggestion, note.Kind);
        Assert.Equal(fix, note.Fix);
        Assert.Equal(fix.Span, note.Location);
        Assert.Null(new DiagnosticNote("advice").Fix);
    }

    [Fact]
    public void Two_reports_are_one_when_every_part_is_the_same_lists_by_their_elements()
    {
        var de = new DiagnosticEngine(new SourceManager());
        Diagnostic Make(string label) => new("D", Severity.Error, new Span(File, 15, 16), "'x' is already defined",
            [new DiagnosticNote(new Span(File, 4, 5), "previous definition")])
        {
            PrimaryLabel = "defined again",
            Labels = [new LabeledSpan(new Span(File, 4, 5), label)],
        };
        de.Report(Make("first here"));
        de.Report(Make("first here"));      // equal lists in new instances: one finding
        de.Report(Make("declared here"));   // another label: another finding
        Assert.Equal(2, de.Count);
    }

    [Fact]
    public void The_old_form_has_no_labels()
    {
        var de = new DiagnosticEngine(new SourceManager());
        de.Report("E", Severity.Error, new Span(File, 0, 3), "msg");
        var d = Assert.Single(de.Diagnostics);
        Assert.Null(d.PrimaryLabel);
        Assert.Null(d.Labels);
    }
}
