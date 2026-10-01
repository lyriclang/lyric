using Lyric.Core;
using Xunit;

namespace Lyric.Tests.Core;

/// <summary>
/// A synthesized file (design/v5/spec/04 D7) is nobody's to be pointed into: the engine
/// re-points an error from it at the node that asked for the synthesis, labelled, with the
/// synthesized line as a note, and drops its hints and warnings. A probe counts the errors of
/// its own level while muted, so a speculative check inside it is not counted.
/// </summary>
public class SynthesizedSourceTests
{
    private static (SourceManager, DiagnosticEngine, FileId Written, FileId Synthesized) Setup()
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var written = sm.AddVirtual("test.lyr", "struct N :: [Equatable] { f: NoEq }\n");
        var origin = new Span(written, 13, 22);
        var synthesized = sm.AddSynthesized("<synthesized Equatable for main.N>",
            "extend N :: [Equatable] {\n    fn equals(o: N): bool {\n        return this.f == o.f;\n    }\n}\n",
            origin, "in the 'Equatable' synthesized for 'N'");
        return (sm, de, written, synthesized);
    }

    [Fact]
    public void A_written_file_has_no_origin()
    {
        var (sm, _, written, synthesized) = Setup();
        Assert.Null(sm.OriginOf(written));
        Assert.NotNull(sm.OriginOf(synthesized));
        Assert.Null(sm.OriginOf(FileId.None));
    }

    [Fact]
    public void An_error_in_synthesized_text_moves_to_the_origin_with_the_line()
    {
        var (_, de, written, synthesized) = Setup();
        de.Report("LYR-SEM0059", Severity.Error, new Span(synthesized, 62, 64), "'==' is not defined for 'NoEq'");

        var d = Assert.Single(de.Diagnostics);
        Assert.Equal(written, d.Span.File);
        Assert.Equal(13, d.Span.Start);
        Assert.Equal("in the 'Equatable' synthesized for 'N': '==' is not defined for 'NoEq'", d.Message);
        var note = Assert.Single(d.Notes!);
        Assert.Equal("synthesized as: return this.f == o.f;", note.Message);
        Assert.False(note.Location.File.IsValid);
    }

    [Fact]
    public void The_notes_of_the_original_follow_the_synthesized_line()
    {
        var (_, de, _, synthesized) = Setup();
        de.Report("LYR-SEM0012", Severity.Error, new Span(synthesized, 62, 64), "no member",
            new DiagnosticNote("did you mean 'g'"));
        var notes = Assert.Single(de.Diagnostics).Notes!;
        Assert.Equal(2, notes.Count);
        Assert.StartsWith("synthesized as:", notes[0].Message);
        Assert.Equal("did you mean 'g'", notes[1].Message);
    }

    [Fact]
    public void Hints_and_warnings_from_synthesized_text_are_dropped()
    {
        var (_, de, _, synthesized) = Setup();
        de.Report("LYR-SEM0075", Severity.Hint, new Span(synthesized, 30, 31), "'h' is never reassigned");
        de.Report("LYR-SEM0070", Severity.Warning, new Span(synthesized, 30, 31), "unused");
        Assert.Empty(de.Diagnostics);
    }

    [Fact]
    public void A_probe_counts_errors_without_recording_them()
    {
        var (_, de, written, _) = Setup();
        int errors;
        using (var probe = de.Probe())
        {
            de.Report("LYR-SEM0001", Severity.Error, new Span(written, 0, 1), "one");
            de.Report("LYR-SEM0075", Severity.Hint, new Span(written, 0, 1), "a hint");
            de.Report("LYR-SEM0002", Severity.Error, new Span(written, 0, 1), "two");
            errors = probe.Errors;
        }
        Assert.Equal(2, errors);
        Assert.Empty(de.Diagnostics);
        // Closed: reporting records again.
        de.Report("LYR-SEM0003", Severity.Error, new Span(written, 0, 1), "three");
        Assert.Single(de.Diagnostics);
    }

    [Fact]
    public void A_speculative_check_inside_a_probe_is_not_counted()
    {
        var (_, de, written, _) = Setup();
        using var probe = de.Probe();
        using (de.Mute())
            de.Report("LYR-SEM0001", Severity.Error, new Span(written, 0, 1), "a candidate that does not fit");
        de.Report("LYR-SEM0002", Severity.Error, new Span(written, 0, 1), "the probe's own");
        Assert.Equal(1, probe.Errors);
    }

    [Fact]
    public void One_probe_at_a_time()
    {
        var (_, de, _, _) = Setup();
        using var probe = de.Probe();
        Assert.Throws<InvalidOperationException>(() => de.Probe());
    }
}
