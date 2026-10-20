using System.IO;
using System.Text.Json;
using Lyric.Core;
using Lyric.Core.Rendering;
using Xunit;

namespace Lyric.Tests.Core;

/// <summary>
/// The JSON form's lines (spec 16 §4; M12a S4): a diagnostic carries the whole record — labels, notes
/// of their kind, a fix with its applicability, every place as file, line, column and offset — and
/// its text form; the count closes once; the toolchain's failure is an event of its own.
/// </summary>
public class EventWriterTests
{
    private static JsonElement[] Lines(StringWriter sw) =>
        [.. sw.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)];

    [Fact]
    public void A_diagnostic_carries_its_labels_notes_and_fix()
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", "let x = 1;\nlet x = 2;");
        var sw = new StringWriter { NewLine = "\n" };
        var events = new EventWriter(sw);
        events.Diagnostic(sm, new Diagnostic("D", Severity.Error, new Span(id, 15, 16), "'x' is bound \"already\"",
            [
                new DiagnosticNote(new Span(id, 4, 5), "first here"),
                new DiagnosticNote("rename one") { Kind = NoteKind.Help },
                DiagnosticNote.Suggest("call it 'y'", new Replacement(new Span(id, 15, 16), "y", Applicability.NeedsReview)),
            ])
        {
            PrimaryLabel = "again",
            Labels = [new LabeledSpan(new Span(id, 4, 5), "first")],
        }, RenderStyle.Plain);
        var d = Assert.Single(Lines(sw));
        Assert.Equal("diagnostic", d.GetProperty("type").GetString());
        Assert.Equal(EventWriter.Version, d.GetProperty("version").GetInt32());
        Assert.Equal("'x' is bound \"already\"", d.GetProperty("message").GetString());
        Assert.Equal("again", d.GetProperty("label").GetString());
        var span = d.GetProperty("span");
        Assert.Equal("test.lyr", span.GetProperty("file").GetString());
        Assert.Equal((2, 5, 15), (span.GetProperty("start").GetProperty("line").GetInt32(),
            span.GetProperty("start").GetProperty("column").GetInt32(), span.GetProperty("start").GetProperty("offset").GetInt32()));
        Assert.Equal("first", d.GetProperty("labels")[0].GetProperty("label").GetString());
        var notes = d.GetProperty("notes");
        Assert.Equal(["note", "help", "suggestion"], notes.EnumerateArray().Select(n => n.GetProperty("kind").GetString()));
        Assert.Equal(JsonValueKind.Null, notes[1].GetProperty("span").ValueKind);
        var fix = notes[2].GetProperty("fix");
        Assert.Equal("y", fix.GetProperty("text").GetString());
        Assert.Equal("needs-review", fix.GetProperty("applicability").GetString());
        Assert.StartsWith("error[D]: 'x' is bound \"already\"\n --> test.lyr:2:5\n", d.GetProperty("rendered").GetString());
        Assert.Equal(1, events.Errors);
    }

    [Fact]
    public void The_count_closes_once_and_a_failure_is_its_own_event()
    {
        var sw = new StringWriter { NewLine = "\n" };
        var events = new EventWriter(sw);
        events.Diagnostic(new SourceManager(), new Diagnostic("W", Severity.Warning, default, "w"), RenderStyle.Plain);
        events.Ice("LYR-ICE0001", "the toolchain failed", "checking 'f'", null);
        events.Summary();
        events.Summary();
        var lines = Lines(sw);
        Assert.Equal(["diagnostic", "ice", "summary"], lines.Select(l => l.GetProperty("type").GetString()));
        Assert.Equal("checking 'f'", lines[1].GetProperty("while").GetString());
        Assert.Equal((0, 1), (lines[2].GetProperty("errors").GetInt32(), lines[2].GetProperty("warnings").GetInt32()));
    }

    [Fact]
    public void Under_the_form_the_engine_draws_nothing_and_says_every_finding_once()
    {
        var sw = new StringWriter { NewLine = "\n" };
        var text = new StringWriter { NewLine = "\n" };
        var events = new EventWriter(sw);
        var de = new DiagnosticEngine(new SourceManager());
        for (var i = 0; i < 3; i++) de.Report($"E{i}", Severity.Error, default, "e");
        // the limit is the drawing's: the form says all three
        de.RenderText(text, RenderStyle.Plain with { Events = events, MaxErrors = 1 });
        de.RenderText(text, RenderStyle.Plain with { Events = events });
        Assert.Equal("", text.ToString());
        Assert.Equal(3, Lines(sw).Length);
    }
}
